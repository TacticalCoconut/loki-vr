using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using Loki.Horde.Net;

namespace Loki.Net
{
    /// <summary>
    /// <see cref="IPresenceChannel"/> over Loki Host's pose relay (tools/lokihost/Core/PoseRelay.cs; wire format
    /// <see cref="PresenceWire"/>): one TCP connection, owned by a background thread that connects (never blocking a frame),
    /// says HELLO, writes the latest pose (older ones are dropped when the link is slow), pings every 2 s off a map, reads what
    /// the relay forwards and reconnects with a growing pause (2, 4, 8, 15 s) when the relay is missing or the link drops.
    /// Loki Host starts the relay with the servers; a server without it simply shows no headsets.
    ///
    /// Client wave 6 (w6voice): the same connection is the <see cref="IVoiceChannel"/>. The channel says HELLO with version 2;
    /// a relay from before voice closes that HELLO, and the channel connects again at once with version 1 (headsets as before,
    /// <see cref="VoiceState"/> OldRelay). Each new connection tries version 2 first (Loki Host may have been updated meanwhile).
    /// My voice frames wait in a small ring (16 frames, filled by the main thread, emptied by the link thread: no lock, no
    /// allocation per frame); other players' frames are handed to <see cref="VoiceReceived"/> on the link thread straight from
    /// the read buffer.
    ///
    /// Client wave 7 (w7host): the same connection is the <see cref="IDataCheckChannel"/>. When the relay's WELCOME 2 carries
    /// the capability bit <see cref="PresenceWire.CapData"/> (a Loki Host that knows its host's data stamp), the channel sends
    /// this client's stamp once per link, as soon as the game handed it over (<see cref="SetDataStamp"/>), and raises
    /// <see cref="DataChecked"/> with the relay's answer. An older relay never sets the bit: nothing is sent, nothing changes.
    ///
    /// Client wave 8 (w8core): the same connection carries Horde. When the relay's WELCOME 2 carries
    /// <see cref="HordeWire.CapHorde"/>, the attached <see cref="IHordeLink"/> (Horde/Core/Net/HordeClient.cs, which implements
    /// IHordeChannel) is told so and sends its H_HELLO through <see cref="SendHorde"/>; Horde frames from the relay (which may be
    /// up to 65,535 bytes on such a link) are handed to it on this link thread. An older relay never sets the bit: the link is
    /// told "no Horde here", nothing Horde is sent.
    ///
    /// Client wave 18 (w18acct): the same connection is the <see cref="IAccountSettingsChannel"/>. When the relay's WELCOME 2 carries
    /// <see cref="PresenceWire.CapSettings"/> (a Loki Host that keeps each account's settings), the channel proves this login with
    /// <see cref="AccountProof"/> (rAthena's web token of the login: RoSession.WebAuthToken) once per link, raises
    /// <see cref="SettingsLoaded"/> with the relay's answer, and sends the latest <see cref="SaveSettings"/> once the proof was taken.
    /// An older relay never sets the bit: nothing is sent, nothing changes. A login without a token sends nothing either (NoProof).
    ///
    /// Client wave 18 (w18share) carries the casting menus and client wave 20 (w20pen) the air pen's drawings the same way: a capability bit
    /// (<see cref="GestureWire.CapGesture"/>, <see cref="DrawWire.CapDraw"/>), a READY frame first, then the game's frames (SendGesture /
    /// SendDraw) and the other players' (TryTakeGesture / TryTakeDraw).
    ///
    /// Free movement (module) carries the players' exact spots the same way (<see cref="SpotWire.CapSpot"/>, SPOT_READY first): my
    /// latest spot (<see cref="SendSpot"/>: a newer one replaces one not yet written) and the others' (<see cref="TryTakeSpot"/>).
    /// </summary>
    public sealed class PresenceRelayChannel : IPresenceChannel, IVoiceChannel, IDataCheckChannel, IPresenceStats, IAccountSettingsChannel, IGestureChannel, IDrawChannel, ISpotChannel
    {
        public readonly string Host; public readonly int Port;
        public string Describe => $"relay {Host}:{Port}";
        public PresenceLink Link => _link;
        public event Action<uint, string, PresencePose> PoseReceived;
        public event Action<uint> Gone;
        public event Action Lost;

        // ---- voice (IVoiceChannel)
        public VoiceLinkState VoiceState => _voiceState;
        public int ExtraJitterFrames => 0;
        public VoiceFrameHandler VoiceReceived { get; set; }
        public event Action<uint> VoiceEnded;
        public event Action VoiceLost;
        /// <summary>The version this link said HELLO with and was welcomed for (0 while not linked).</summary>
        public int LinkVersion => _linkVersion;
        /// <summary>Voice counters for the log and the smokes: my frames written to the relay, my frames dropped because the ring
        /// was full (the link stalled), other players' frames received, VOICE_END messages, connections that fell back to version 1.</summary>
        public int VoiceSent, VoiceSendDropped, VoiceFramesReceived, VoiceEndsReceived, Fallbacks;

        // ---- the game-data check (IDataCheckChannel)
        public event Action<bool, string, string> DataChecked;
        /// <summary>The relay of this link compares game data (its WELCOME said so); false while not linked.</summary>
        public bool RelayChecksData => _dataCap;
        /// <summary>My DATA messages written to the relay; its DATA_HOST answers received.</summary>
        public int DataSent, DataAnswers;
        private volatile string _dataStamp, _dataLabel = "";
        private volatile bool _dataCap;

        // ---- Horde (client wave 8)
        private volatile IHordeLink _horde;
        private readonly ConcurrentQueue<byte[]> _hordeOut = new ConcurrentQueue<byte[]>();
        private volatile bool _hordeCap;
        /// <summary>The relay of this link runs Horde (its WELCOME said so); false while not linked.</summary>
        public bool RelayRunsHorde => _hordeCap;
        /// <summary>Horde frames written / received on this channel (all links).</summary>
        public int HordeSent, HordeReceived;

        /// <summary>Attaches the Horde client (once, before or after the link is up): it is told about every link from now on.</summary>
        public void AttachHorde(IHordeLink link)
        {
            _horde = link;
            if (_link == PresenceLink.Connected) link.OnLinked(_hordeCap);
        }

        /// <summary>One whole Horde frame (u16 length + payload) to send on the current link; dropped when there is none or
        /// the relay does not run Horde. Any thread.</summary>
        public void SendHorde(byte[] frame)
        {
            if (frame == null || !_hordeCap || _link != PresenceLink.Connected) return;
            if (_hordeOut.Count > 256) return; // the link is stuck: the host will resync
            _hordeOut.Enqueue(frame);
        }

        // ---- client wave 18 (w18acct): the account's settings (IAccountSettingsChannel)
        /// <summary>This login's proof for the relay (rAthena's web token: RoSession.WebAuthToken; "" none). Set before <see cref="Begin"/>.</summary>
        public string AccountProof { get; set; } = "";
        public AccountSettingsLink SettingsLink => _settingsLink;
        public event Action<byte, long, string> SettingsLoaded;
        public event Action<byte, long> SettingsSaved;
        /// <summary>The relay of this link keeps each account's settings (its WELCOME said so); false while not linked.</summary>
        public bool RelayKeepsSettings => _settingsCap;
        /// <summary>SETTINGS_AUTH / SETTINGS_PUT written; SETTINGS_OF / SETTINGS_ACK received (all links).</summary>
        public int SettingsAuthSent, SettingsPutsSent, SettingsOfReceived, SettingsAcksReceived;
        private volatile bool _settingsCap, _settingsAsk;
        private volatile AccountSettingsLink _settingsLink = AccountSettingsLink.None;
        private byte[] _settingsPut; // (under _gate) the latest save waiting for a link whose proof was taken

        public void LoadSettings()
        {
            if (!_settingsCap) return;
            if (!PresenceWire.ValidProof(AccountProof)) { _settingsLink = AccountSettingsLink.NoProof; return; }
            _settingsLink = AccountSettingsLink.Asking; _settingsAsk = true; // (the link thread sends it on its next round)
        }

        public void SaveSettings(long savedAt, string json)
        {
            var f = PresenceWire.EncodeSettingsPut(savedAt, json);
            if (f == null) { _in.Enqueue(new Incoming { Kind = KindSettingsAck, State = PresenceWire.SettingsFailed }); Debug.LogWarning($"[Settings] not sent: {json?.Length ?? 0} characters cannot go in one message"); return; }
            lock (_gate) _settingsPut = f;
        }

        public void SetDataStamp(string stamp, string label)
        {
            _dataLabel = label ?? "";
            _dataStamp = PresenceWire.ValidStamp(stamp) ? stamp : null; // (the link thread sends it on its next round)
        }

        // ---- client wave 18 (w18share), the design's point 33: the casting menus friends see (Net/GestureWire.cs; the game's side:
        // Net/IGestureChannel.cs RelayGestureChannel). When the relay's WELCOME 2 carries GestureWire.CapGesture, the link says
        // GESTURE_READY at once (it draws friends' menus) and from then on carries my GESTURE frames, in order, after the pose of the same
        // round (the relay learns my map from the pose); the friends' GESTURE_OF frames wait for the main thread. An older relay never
        // sets the bit: nothing is sent (it would close the link on the unknown message) and nothing arrives.
        private volatile bool _gestureCap; private volatile int _gestureLinks;
        private readonly ConcurrentQueue<byte[]> _gestureOut = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<(uint aid, byte[] body)> _gestureIn = new ConcurrentQueue<(uint, byte[])>();
        /// <summary>The current link forwards casting menus; links so far that could; GESTURE frames written, GESTURE_OF frames received.</summary>
        public bool RelayForwardsMenus => _gestureCap && _link == PresenceLink.Connected;
        public int GestureLinks => _gestureLinks;
        public int GesturesSent, GesturesReceived;
        public const int GestureQueue = 64;

        /// <summary>One whole GESTURE frame on the current link (none, or a relay without menus: dropped). A stuck link keeps the newest
        /// <see cref="GestureQueue"/>. Any thread.</summary>
        public void SendGesture(byte[] frame)
        {
            if (frame == null || !RelayForwardsMenus) return;
            _gestureOut.Enqueue(frame);
            while (_gestureOut.Count > GestureQueue && _gestureOut.TryDequeue(out _)) { }
        }

        /// <summary>A friend's menu (its account id, the body from its format byte). Main thread.</summary>
        public bool TryTakeGesture(out uint aid, out byte[] body)
        {
            if (_gestureIn.TryDequeue(out var g)) { aid = g.aid; body = g.body; return true; }
            aid = 0; body = null; return false;
        }

        // ---- client wave 20 (w20pen), the design's point 57: the air pen's drawings other players see (Net/DrawWire.cs; the game's side:
        // Net/IDrawChannel.cs RelayDrawChannel). The casting menus' way: when the relay's WELCOME 2 carries DrawWire.CapDraw, the link says
        // DRAW_READY at once and from then on carries my DRAW frames, in order, after the pose of the same round; the DRAW_OF frames wait for the
        // main thread. An older relay never sets the bit: nothing is sent (it would close the link on the unknown message) and nothing arrives.
        private volatile bool _drawCap; private volatile int _drawLinks;
        private readonly ConcurrentQueue<byte[]> _drawOut = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<(uint aid, byte rel, byte[] body)> _drawIn = new ConcurrentQueue<(uint, byte, byte[])>();
        /// <summary>The current link forwards drawings; links so far that could; DRAW frames written, DRAW_OF frames received.</summary>
        public bool RelayForwardsDrawings => _drawCap && _link == PresenceLink.Connected;
        public int DrawLinks => _drawLinks;
        public int DrawsSent, DrawsReceived;
        /// <summary>DRAW frames waiting for a stuck link (the newest are kept: the game sends its drawings again every 20 s), and DRAW_OF frames
        /// waiting for the main thread.</summary>
        public const int DrawQueue = 256, DrawInQueue = 2048;

        /// <summary>One whole DRAW frame on the current link (none, or a relay without drawings: dropped). Any thread.</summary>
        public void SendDraw(byte[] frame)
        {
            if (frame == null || !RelayForwardsDrawings) return;
            _drawOut.Enqueue(frame);
            while (_drawOut.Count > DrawQueue && _drawOut.TryDequeue(out _)) { }
        }

        /// <summary>Another player's drawing frame (its account id, what it is to me as the relay knows it, the body from its format byte). Main thread.</summary>
        public bool TryTakeDraw(out uint aid, out byte relation, out byte[] body)
        {
            if (_drawIn.TryDequeue(out var d)) { aid = d.aid; relation = d.rel; body = d.body; return true; }
            aid = 0; relation = 0; body = null; return false;
        }

        // ---- free movement (wfree), design note 2026-10-14: the players' exact spots (Net/SpotWire.cs; the game's side: Net/ISpotChannel.cs
        // RelaySpotChannel). When the relay's WELCOME 2 carries SpotWire.CapSpot, the link says SPOT_READY at once and from then on writes my
        // LATEST spot (a newer one replaces one the link has not written yet: only where I am now matters); the others' SPOT_OF frames wait for
        // the main thread. An older relay never sets the bit: nothing is sent (it would close the link on the unknown message), nothing arrives.
        private volatile bool _spotCap, _spotReadyPending; private volatile int _spotLinks;
        private byte[] _spotLatest; // (under _gate)
        private readonly ConcurrentQueue<(uint aid, SpotWire.State s)> _spotIn = new ConcurrentQueue<(uint, SpotWire.State)>();
        /// <summary>The current link forwards spots; links so far that could; SPOT frames written, SPOT_OF frames received.</summary>
        public bool RelayForwardsSpots => _spotCap && _link == PresenceLink.Connected;
        public int SpotLinks => _spotLinks;
        public int SpotsSent, SpotsReceived;
        public const int SpotInQueue = 512;

        /// <summary>My spot (a whole SPOT frame, Net/SpotWire.cs Encode) on the current link; dropped when there is none or the relay does not
        /// forward spots. Any thread.</summary>
        public void SendSpot(byte[] frame)
        {
            if (frame == null || !RelayForwardsSpots) return;
            lock (_gate) _spotLatest = frame;
        }

        /// <summary>Another player's spot (its account id and the spot). Main thread.</summary>
        public bool TryTakeSpot(out uint aid, out SpotWire.State s)
        {
            if (_spotIn.TryDequeue(out var x)) { aid = x.aid; s = x.s; return true; }
            aid = 0; s = default; return false;
        }

        /// <summary>Counters for smokes and the log.</summary>
        public int PosesSent, PosesReceived, GonesReceived, Connects;
        public const int ConnectTimeoutMs = 3000, WelcomeTimeoutMs = 3000, PingEveryMs = 2000, MaxBackoffMs = 15000;
        /// <summary>How long the link thread waits for incoming bytes before it looks at what there is to send: a pose or a voice
        /// frame leaves at most this late (15 ms before voice; 5 ms keeps a voice frame's wait short).</summary>
        public const int PollMicroseconds = 5000;

        // ---- IGestureChannel, IDrawChannel, ISpotChannel (Item 66: unified interface casts across Relay and SpacetimeDB)
        bool IGestureChannel.CanShare => RelayForwardsMenus;
        int IGestureChannel.Links => GestureLinks;
        void IGestureChannel.Send(byte[] frame) => SendGesture(frame);
        bool IGestureChannel.TryTake(out uint aid, out byte[] body) => TryTakeGesture(out aid, out body);
        int IGestureChannel.Sent => GesturesSent;
        int IGestureChannel.Received => GesturesReceived;

        bool IDrawChannel.CanShare => RelayForwardsDrawings;
        int IDrawChannel.Links => DrawLinks;
        void IDrawChannel.Send(byte[] frame) => SendDraw(frame);
        bool IDrawChannel.TryTake(out uint aid, out byte relation, out byte[] body) => TryTakeDraw(out aid, out relation, out body);
        int IDrawChannel.Sent => DrawsSent;
        int IDrawChannel.Received => DrawsReceived;

        bool ISpotChannel.CanShare => RelayForwardsSpots;
        int ISpotChannel.Links => SpotLinks;
        void ISpotChannel.Send(byte[] frame) => SendSpot(frame);
        bool ISpotChannel.TryTake(out uint aid, out SpotWire.State spot) => TryTakeSpot(out aid, out spot);
        int ISpotChannel.Sent => SpotsSent;
        int ISpotChannel.Received => SpotsReceived;

        // ---- IPresenceStats (port wave H, hpresence): the counters above, whichever transport a smoke runs on
        int IPresenceStats.StatPosesSent => PosesSent; int IPresenceStats.StatPosesReceived => PosesReceived; int IPresenceStats.StatGonesReceived => GonesReceived;
        int IPresenceStats.StatConnects => Connects; int IPresenceStats.StatLinkVersion => _linkVersion; int IPresenceStats.StatFallbacks => Fallbacks;
        int IPresenceStats.StatVoiceSent => VoiceSent; int IPresenceStats.StatVoiceSendDropped => VoiceSendDropped;
        int IPresenceStats.StatVoiceFramesReceived => VoiceFramesReceived; int IPresenceStats.StatVoiceEndsReceived => VoiceEndsReceived;
        bool IPresenceStats.StatChecksData => _dataCap; int IPresenceStats.StatDataSent => DataSent; int IPresenceStats.StatDataAnswers => DataAnswers;

        private volatile PresenceLink _link = PresenceLink.Off;
        private volatile VoiceLinkState _voiceState = VoiceLinkState.NoLink;
        private volatile int _linkVersion;
        private uint _aid; private string _name = "";
        private Thread _thread; private volatile bool _stop; private TcpClient _tcp;
        private readonly object _gate = new object();
        private byte[] _latestPose; private bool _leavePending;
        private readonly PresenceWire.PoseBody _sendBody = new PresenceWire.PoseBody();
        private ushort _seq;

        // my voice frames: a ring written by the main thread (_voiceWrite) and emptied by the link thread (_voiceRead)
        private const int VoiceRing = 16, VoiceRingMask = VoiceRing - 1;
        private readonly byte[][] _voiceSlots = new byte[VoiceRing][];
        private readonly int[] _voiceLengths = new int[VoiceRing];
        private volatile int _voiceWrite, _voiceRead;
        private volatile bool _voiceStopPending;

        private struct Incoming { public byte Kind; public uint Aid; public string Name; public PresencePose Pose; public bool Differs; public string Label; public byte State; public long At; }
        private readonly ConcurrentQueue<Incoming> _in = new ConcurrentQueue<Incoming>();
        private const byte KindPose = 1, KindGone = 2, KindLost = 3, KindVoiceEnd = 4, KindData = 5, KindSettingsOf = 6, KindSettingsAck = 7; // (6, 7: client wave 18, w18acct)

        public PresenceRelayChannel(string host, int port)
        {
            Host = host; Port = port;
            for (int i = 0; i < VoiceRing; i++) _voiceSlots[i] = new byte[2 + PresenceWire.MaxPayloadVoice];
        }

        public void Begin(uint aid, string characterName)
        {
            if (_thread != null) return;
            _aid = aid; _name = characterName ?? "";
            _thread = new Thread(Run) { IsBackground = true, Name = "loki-presence" };
            _thread.Start();
        }

        public void Send(PresencePose p)
        {
            var b = _sendBody;
            b.Map = p.Map; b.Seq = ++_seq; b.MetresPerCell = p.MetresPerCell;
            b.Flags = (byte)((p.LeftTracked ? PresenceWire.FlagLeft : 0) | (p.RightTracked ? PresenceWire.FlagRight : 0));
            Put(b.V, 0, p.HeadPos, p.HeadRot); Put(b.V, 7, p.LeftPos, p.LeftRot); Put(b.V, 14, p.RightPos, p.RightRot);
            if (!PresenceWire.Valid(b)) return; // e.g. a scale of 0 while a map loads: the relay would close us for it
            var frame = PresenceWire.EncodePose(b);
            lock (_gate) { _latestPose = frame; _leavePending = false; }
        }

        public void Leave() { lock (_gate) { _latestPose = null; _leavePending = true; } }

        public bool SendVoice(string map, ushort seq, byte codec, byte flags, byte[] data, int offset, int length)
        {
            if (_voiceState != VoiceLinkState.Ready) return false;
            int w = _voiceWrite;
            if (w - _voiceRead >= VoiceRing) { Interlocked.Increment(ref VoiceSendDropped); return false; }
            int n = PresenceWire.WriteVoice(_voiceSlots[w & VoiceRingMask], 0, map, seq, codec, flags, data, offset, length);
            if (n <= 0) return false;
            _voiceLengths[w & VoiceRingMask] = n;
            _voiceStopPending = false;
            _voiceWrite = w + 1; // published: the link thread may send it
            return true;
        }

        public void StopVoice() { _voiceStopPending = true; }

        public void Poll()
        {
            while (_in.TryDequeue(out var m))
            {
                try
                {
                    if (m.Kind == KindPose) PoseReceived?.Invoke(m.Aid, m.Name, m.Pose);
                    else if (m.Kind == KindGone) Gone?.Invoke(m.Aid);
                    else if (m.Kind == KindVoiceEnd) VoiceEnded?.Invoke(m.Aid);
                    else if (m.Kind == KindData) DataChecked?.Invoke(m.Differs, m.Name, m.Label);
                    else if (m.Kind == KindSettingsOf) SettingsLoaded?.Invoke(m.State, m.At, m.Name); // client wave 18 (w18acct): the JSON rides in Name
                    else if (m.Kind == KindSettingsAck) SettingsSaved?.Invoke(m.State, m.At);
                    else { Lost?.Invoke(); VoiceLost?.Invoke(); }
                }
                catch (Exception e) { Debug.LogException(e); }
            }
        }


        public void Dispose()
        {
            _stop = true;
            try { _tcp?.Close(); } catch (Exception) { }
            if (_thread != null && !_thread.Join(1000)) Debug.LogWarning("[Presence] the relay thread did not stop within 1 s");
            _link = PresenceLink.Off; _voiceState = VoiceLinkState.NoLink;
        }

        private static void Put(float[] v, int o, Vector3 p, Quaternion q)
        {
            v[o] = p.x; v[o + 1] = p.y; v[o + 2] = p.z; v[o + 3] = q.x; v[o + 4] = q.y; v[o + 5] = q.z; v[o + 6] = q.w;
        }

        private static PresencePose ToPose(PresenceWire.PoseBody b)
        {
            var v = b.V;
            return new PresencePose
            {
                Map = b.Map, MetresPerCell = b.MetresPerCell,
                HeadPos = new Vector3(v[0], v[1], v[2]), HeadRot = Norm(v, 3),
                LeftPos = new Vector3(v[7], v[8], v[9]), LeftRot = Norm(v, 10),
                RightPos = new Vector3(v[14], v[15], v[16]), RightRot = Norm(v, 17),
                LeftTracked = (b.Flags & PresenceWire.FlagLeft) != 0, RightTracked = (b.Flags & PresenceWire.FlagRight) != 0,
            };
        }

        private static Quaternion Norm(float[] v, int o) { var q = new Quaternion(v[o], v[o + 1], v[o + 2], v[o + 3]); q.Normalize(); return q; }

        // ---------------------------------------------------------------------------------------------- the link thread

        private void Run()
        {
            int failures = 0;
            var buf = new byte[4096];
            var body = new PresenceWire.PoseBody();
            var frames = new PresenceWire.FrameReader();
            var voice = new VoiceFrame();
            bool version1 = false; // this attempt says HELLO 1: the relay closed our HELLO 2 a moment ago (a relay from before voice)
            while (!_stop)
            {
                bool welcomed = false, connected = false, retryAsVersion1 = false, dataSent = false;
                byte helloVersion = version1 ? PresenceWire.Version : PresenceWire.VersionVoice;
                _gestureCap = false; // client wave 18 (w18share): until this link's WELCOME says otherwise
                _drawCap = false; // client wave 20 (w20pen): the same
                _spotCap = false; _spotReadyPending = false; // free movement (wfree): the same
                _link = PresenceLink.Connecting;
                TcpClient tcp = null;
                try
                {
                    tcp = new TcpClient { NoDelay = true };
                    _tcp = tcp;
                    var ar = tcp.BeginConnect(Host, Port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(ConnectTimeoutMs)) throw new TimeoutException("no answer in " + ConnectTimeoutMs / 1000 + " s");
                    tcp.EndConnect(ar);
                    connected = true;
                    var s = tcp.GetStream();
                    var hello = PresenceWire.EncodeHello(_aid, _name, helloVersion);
                    s.Write(hello, 0, hello.Length);
                    // a connection that said HELLO 2 may be sent voice frames (up to 274 bytes); a version-1 one keeps the 200-byte bound
                    frames = new PresenceWire.FrameReader(helloVersion >= PresenceWire.VersionVoice ? PresenceWire.MaxPayloadVoice : PresenceWire.MaxPayload);
                    _voiceRead = _voiceWrite; _voiceStopPending = false; // nothing from before this link is sent on it
                    long welcomeBy = Now() + WelcomeTimeoutMs, lastSent = Now();
                    while (!_stop)
                    {
                        if (welcomed)
                        {
                            byte[] f; bool leave;
                            lock (_gate) { f = _latestPose; _latestPose = null; leave = _leavePending; _leavePending = false; }
                            if (leave) { var l = PresenceWire.EncodeLeave(); s.Write(l, 0, l.Length); lastSent = Now(); }
                            if (f != null) { s.Write(f, 0, f.Length); lastSent = Now(); Interlocked.Increment(ref PosesSent); }
                            // my voice: every frame that waits, in order; then the stop, if one was asked for after them
                            if (_voiceState == VoiceLinkState.Ready)
                            {
                                bool stopVoice = _voiceStopPending;
                                int w = _voiceWrite;
                                while (_voiceRead != w)
                                {
                                    int slot = _voiceRead & VoiceRingMask;
                                    s.Write(_voiceSlots[slot], 0, _voiceLengths[slot]);
                                    _voiceRead = _voiceRead + 1; lastSent = Now(); Interlocked.Increment(ref VoiceSent);
                                }
                                if (stopVoice && _voiceWrite == w) { _voiceStopPending = false; var v = PresenceWire.EncodeVoiceStop(); s.Write(v, 0, v.Length); lastSent = Now(); }
                            }
                            // Horde (client wave 8): the frames the Horde client queued, in order
                            if (_hordeCap) while (_hordeOut.TryDequeue(out var hf)) { s.Write(hf, 0, hf.Length); lastSent = Now(); Interlocked.Increment(ref HordeSent); }
                            // client wave 18 (w18share): my casting menus (and first, this link's GESTURE_READY), in order, after the pose above
                            if (_gestureCap) while (_gestureOut.TryDequeue(out var gf)) { s.Write(gf, 0, gf.Length); lastSent = Now(); Interlocked.Increment(ref GesturesSent); }
                            // client wave 20 (w20pen): my drawings (and first, this link's DRAW_READY), in order, after the pose above
                            if (_drawCap) while (_drawOut.TryDequeue(out var df)) { s.Write(df, 0, df.Length); lastSent = Now(); Interlocked.Increment(ref DrawsSent); }
                            // free movement (wfree): SPOT_READY once per link, then my latest spot
                            if (_spotCap)
                            {
                                if (_spotReadyPending) { _spotReadyPending = false; var rd = SpotWire.EncodeReady(); s.Write(rd, 0, rd.Length); lastSent = Now(); }
                                byte[] sp; lock (_gate) { sp = _spotLatest; _spotLatest = null; }
                                if (sp != null) { s.Write(sp, 0, sp.Length); lastSent = Now(); Interlocked.Increment(ref SpotsSent); }
                            }
                            // my data stamp: once per link, to a relay that compares (client wave 7), as soon as the game knows it
                            if (_dataCap && !dataSent)
                            {
                                var stamp = _dataStamp;
                                if (stamp != null)
                                {
                                    dataSent = true;
                                    var d = PresenceWire.EncodeData(stamp, _dataLabel);
                                    if (d != null) { s.Write(d, 0, d.Length); lastSent = Now(); Interlocked.Increment(ref DataSent); }
                                }
                            }
                            // the account's settings (client wave 18, w18acct): the proof when asked (once per link by itself), then the latest save
                            if (_settingsCap && _settingsAsk)
                            {
                                _settingsAsk = false;
                                var a = PresenceWire.EncodeSettingsAuth(AccountProof);
                                if (a != null) { s.Write(a, 0, a.Length); lastSent = Now(); Interlocked.Increment(ref SettingsAuthSent); }
                            }
                            if (_settingsCap && _settingsLink == AccountSettingsLink.Ready)
                            {
                                byte[] put; lock (_gate) { put = _settingsPut; _settingsPut = null; }
                                if (put != null) { s.Write(put, 0, put.Length); lastSent = Now(); Interlocked.Increment(ref SettingsPutsSent); }
                            }
                            if (Now() - lastSent >= PingEveryMs) { var pg = PresenceWire.EncodePing(); s.Write(pg, 0, pg.Length); lastSent = Now(); }
                        }
                        else if (Now() > welcomeBy) throw new TimeoutException("no WELCOME: not a Loki pose relay on this port?");
                        if (!tcp.Client.Poll(PollMicroseconds, SelectMode.SelectRead)) continue;
                        int n = s.Read(buf, 0, buf.Length);
                        if (n <= 0) throw new Exception("closed by the relay");
                        frames.Append(buf, 0, n);
                        while (true)
                        {
                            int k = frames.NextView(out var p, out int o, out int len);
                            if (k == 0) break;
                            if (k < 0) throw new Exception("bad frame from the relay");
                            if (!welcomed)
                            {
                                if (!PresenceWire.TryDecodeWelcome(p, o, len, out var ver, out var caps) || ver != helloVersion) throw new Exception("not a Loki pose relay (or another version)");
                                welcomed = true; failures = 0; _linkVersion = ver; Interlocked.Increment(ref Connects);
                                _voiceState = ver < PresenceWire.VersionVoice ? VoiceLinkState.OldRelay
                                    : (caps & PresenceWire.CapVoice) != 0 ? VoiceLinkState.Ready : VoiceLinkState.SwitchedOffByHost;
                                _link = PresenceLink.Connected;
                                _dataCap = ver >= PresenceWire.VersionVoice && (caps & PresenceWire.CapData) != 0;
                                // client wave 18 (w18share): a relay that forwards casting menus: nothing older is sent on this link, READY first
                                while (_gestureOut.TryDequeue(out _)) { }
                                if (ver >= PresenceWire.VersionVoice && (caps & GestureWire.CapGesture) != 0) { _gestureOut.Enqueue(GestureWire.EncodeReady()); _gestureCap = true; Interlocked.Increment(ref _gestureLinks); }
                                // client wave 20 (w20pen): a relay that forwards drawings: the same, DRAW_READY first
                                while (_drawOut.TryDequeue(out _)) { }
                                if (ver >= PresenceWire.VersionVoice && (caps & DrawWire.CapDraw) != 0) { _drawOut.Enqueue(DrawWire.EncodeReady()); _drawCap = true; Interlocked.Increment(ref _drawLinks); }
                                // free movement (wfree): a relay that forwards exact spots: nothing older is sent on this link, SPOT_READY first
                                lock (_gate) _spotLatest = null;
                                if (ver >= PresenceWire.VersionVoice && (caps & SpotWire.CapSpot) != 0) { _spotReadyPending = true; _spotCap = true; Interlocked.Increment(ref _spotLinks); }
                                _hordeCap = ver >= PresenceWire.VersionVoice && (caps & HordeWire.CapHorde) != 0;
                                // a relay that runs Horde may send this link frames up to 65,535 bytes (only after our H_HELLO)
                                if (_hordeCap) frames.MaxLength = HordeWire.MaxPayload;
                                // client wave 18 (w18acct): a relay that keeps settings may send the account's (SETTINGS_OF, up to 65,535 bytes)
                                _settingsCap = ver >= PresenceWire.VersionVoice && (caps & PresenceWire.CapSettings) != 0;
                                if (_settingsCap) { frames.MaxLength = PresenceWire.MaxPayloadSettings; LoadSettings(); }
                                while (_hordeOut.TryDequeue(out _)) { } // nothing from an earlier link is sent on this one
                                try { _horde?.OnLinked(_hordeCap); } catch (Exception he) { Debug.LogException(he); }
                                Debug.Log($"[Presence] connected to the {Describe} as AID {_aid} '{_name}' (version {ver}; voice: {VoiceText(_voiceState)})");
                                Debug.Log(_dataCap ? "[Presence] this server's host compares players' game data with its own: mine is sent once it is known"
                                                   : "[Presence] this server does not compare game data (an older Loki Host, or no Ragnarok Online folder set there): no check");
                                continue;
                            }
                            byte type = p[o];
                            // client wave 18 (w18share): a friend's casting menu (copied out of the read buffer; a main thread that stops
                            // taking them keeps the newest 256)
                            if (type == GestureWire.GestureOf)
                            {
                                if (GestureWire.TryReadGestureOf(p, o, len, out var gaid, out int gbo, out int gbl) && gaid != _aid)
                                {
                                    var gbody = new byte[gbl]; Buffer.BlockCopy(p, gbo, gbody, 0, gbl);
                                    _gestureIn.Enqueue((gaid, gbody)); Interlocked.Increment(ref GesturesReceived);
                                    while (_gestureIn.Count > 256 && _gestureIn.TryDequeue(out _)) { }
                                }
                                continue;
                            }
                            // client wave 20 (w20pen): another player's drawing (copied out of the read buffer; a main thread that stops taking
                            // them keeps the newest DrawInQueue)
                            if (type == DrawWire.DrawOf)
                            {
                                if (DrawWire.TryReadDrawOf(p, o, len, out var daid, out var drel, out int dbo, out int dbl) && daid != _aid)
                                {
                                    var dbody = new byte[dbl]; Buffer.BlockCopy(p, dbo, dbody, 0, dbl);
                                    _drawIn.Enqueue((daid, drel, dbody)); Interlocked.Increment(ref DrawsReceived);
                                    while (_drawIn.Count > DrawInQueue && _drawIn.TryDequeue(out _)) { }
                                }
                                continue;
                            }
                            // free movement (wfree): another player's exact spot (a main thread that stops taking them keeps the newest SpotInQueue)
                            if (type == SpotWire.SpotOf)
                            {
                                if (SpotWire.TryReadSpotOf(p, o, len, out var said, out var spot) && said != _aid)
                                {
                                    _spotIn.Enqueue((said, spot)); Interlocked.Increment(ref SpotsReceived);
                                    while (_spotIn.Count > SpotInQueue && _spotIn.TryDequeue(out _)) { }
                                }
                                continue;
                            }
                            if (type == PresenceWire.VoiceOf)
                            {
                                if (PresenceWire.TryReadVoiceOf(p, o, len, out var h) && h.Aid != _aid)
                                {
                                    Interlocked.Increment(ref VoiceFramesReceived);
                                    voice.Aid = h.Aid; voice.Buffer = p; voice.NameOffset = h.NameOffset; voice.NameLength = h.NameLength;
                                    voice.MapOffset = h.MapOffset; voice.MapLength = h.MapLength; voice.Seq = h.Seq; voice.Codec = h.Codec; voice.Flags = h.Flags;
                                    voice.DataOffset = h.DataOffset; voice.DataLength = h.DataLength;
                                    try { VoiceReceived?.Invoke(ref voice); } catch (Exception e) { Debug.LogException(e); }
                                }
                            }
                            else if (type == PresenceWire.PoseOf)
                            {
                                if (PresenceWire.TryDecodePoseOf(p, o, len, out var aid, out var name, body) && aid != _aid)
                                {
                                    Interlocked.Increment(ref PosesReceived);
                                    _in.Enqueue(new Incoming { Kind = KindPose, Aid = aid, Name = name, Pose = ToPose(body) });
                                }
                            }
                            else if (type == PresenceWire.Gone && PresenceWire.TryDecodeGone(p, o, len, out var gone))
                            {
                                Interlocked.Increment(ref GonesReceived);
                                _in.Enqueue(new Incoming { Kind = KindGone, Aid = gone });
                            }
                            else if (type == PresenceWire.VoiceEnd && PresenceWire.TryDecodeVoiceEnd(p, o, len, out var ended) && ended != _aid)
                            {
                                Interlocked.Increment(ref VoiceEndsReceived);
                                _in.Enqueue(new Incoming { Kind = KindVoiceEnd, Aid = ended });
                            }
                            else if (type == PresenceWire.DataHost && PresenceWire.TryDecodeDataHost(p, o, len, out var verdict, out var hostStamp, out var hostLabel))
                            {
                                Interlocked.Increment(ref DataAnswers);
                                _in.Enqueue(new Incoming { Kind = KindData, Differs = verdict == PresenceWire.DataDiffers, Name = hostStamp, Label = hostLabel });
                            }
                            else if (type == PresenceWire.SettingsOf && PresenceWire.TryDecodeSettingsOf(p, o, len, out var sst, out var sat, out var sjson)) // client wave 18 (w18acct)
                            {
                                Interlocked.Increment(ref SettingsOfReceived);
                                _settingsLink = sst == PresenceWire.SettingsFound || sst == PresenceWire.SettingsNone ? AccountSettingsLink.Ready : AccountSettingsLink.Refused;
                                _in.Enqueue(new Incoming { Kind = KindSettingsOf, State = sst, At = sat, Name = sjson });
                            }
                            else if (type == PresenceWire.SettingsAck && PresenceWire.TryDecodeSettingsAck(p, o, len, out var ast, out var aat))
                            {
                                Interlocked.Increment(ref SettingsAcksReceived);
                                _in.Enqueue(new Incoming { Kind = KindSettingsAck, State = ast, At = aat });
                            }
                            else if (HordeWire.IsRelayType(type))
                            {
                                Interlocked.Increment(ref HordeReceived);
                                try { _horde?.OnFrame(p, o, len); } catch (Exception he) { Debug.LogException(he); }
                            }
                            // anything else from a newer relay is ignored
                        }
                    }
                }
                catch (Exception e)
                {
                    if (!_stop)
                    {
                        // the relay took the connection and closed it before any WELCOME to a HELLO 2: a relay from before voice
                        // (it refuses any HELLO but version 1). Once more, at once, as version 1: headsets work, voice does not.
                        if (connected && !welcomed && helloVersion >= PresenceWire.VersionVoice && !(e is TimeoutException))
                        {
                            retryAsVersion1 = true; Interlocked.Increment(ref Fallbacks);
                            Debug.Log($"[Presence] the {Describe} closed a version-2 HELLO ({e.Message}): an older relay without voice? Connecting again as version 1");
                        }
                        else
                        {
                            failures++;
                            if (welcomed) Debug.LogWarning($"[Presence] the {Describe} link dropped ({e.Message}): headsets hidden, reconnecting");
                            else if (failures == 1 || failures % 20 == 0) Debug.Log($"[Presence] no {Describe} ({e.Message}); trying again every few seconds (attempt {failures})");
                        }
                    }
                }
                finally { try { tcp?.Close(); } catch (Exception) { } }
                _voiceState = VoiceLinkState.NoLink; _linkVersion = 0; _dataCap = false;
                _settingsCap = false; _settingsAsk = false; _settingsLink = AccountSettingsLink.None; lock (_gate) _settingsPut = null; // client wave 18 (w18acct): the next link asks again
                if (welcomed) { _hordeCap = false; try { _horde?.OnUnlinked(); } catch (Exception he) { Debug.LogException(he); } }
                if (welcomed) _in.Enqueue(new Incoming { Kind = KindLost });
                if (_stop) break;
                version1 = retryAsVersion1; // (after a version-1 link, or a failed version-1 try, the next connection asks for voice again)
                if (retryAsVersion1) continue;
                _link = PresenceLink.Waiting;
                int wait = Math.Min(MaxBackoffMs, 2000 << Math.Min(Math.Max(failures - 1, 0), 3));
                for (int t = 0; t < wait && !_stop; t += 100) Thread.Sleep(100);
            }
            _link = PresenceLink.Off;
        }

        private static string VoiceText(VoiceLinkState s) =>
            s == VoiceLinkState.Ready ? "ready" : s == VoiceLinkState.OldRelay ? "none (a relay from before voice)" : s == VoiceLinkState.SwitchedOffByHost ? "switched off by the host" : "no link";

        private static long Now() => Environment.TickCount;

        // ---------------------------------------------------------------------------------------------- where the relay is

        [Serializable] private sealed class ConfigFile { public string loginHost = ""; public int loginPort; public int presencePort; public ConfigEntry[] servers = new ConfigEntry[0]; }
        [Serializable] private sealed class ConfigEntry { public string host = ""; public int port; public int presencePort; }

        /// <summary>
        /// The relay's port for the server this game logged in to (<paramref name="host"/>:<paramref name="loginPort"/>): the matching
        /// server entry's <c>presencePort</c> in the config file (Loki Host's share line carries it), else the file's top-level
        /// <c>presencePort</c> when the login came from loginHost / loginPort, else the login port + 3 (Loki Host: 7900 → 7903).
        /// Read from the file itself: LokiConfig.ServerEntry has no such field yet (w3login owns it; COORD 2026-09-30 02:07).
        /// // presencePort (w3login): once ServerEntry carries it, the login screen's chosen entry can hand it over directly.
        /// </summary>
        public static (int port, string from) PortFor(string configPath, string host, int loginPort)
        {
            try
            {
                if (!string.IsNullOrEmpty(configPath) && System.IO.File.Exists(configPath))
                {
                    var c = JsonUtility.FromJson<ConfigFile>(System.IO.File.ReadAllText(configPath));
                    if (c != null)
                    {
                        if (c.servers != null)
                            foreach (var e in c.servers)
                                if (e != null && e.presencePort > 0 && e.presencePort < 65536 && e.port == loginPort && string.Equals((e.host ?? "").Trim(), host, StringComparison.OrdinalIgnoreCase))
                                    return (e.presencePort, "its server entry");
                        if (c.presencePort > 0 && c.presencePort < 65536 && c.loginPort == loginPort && string.Equals((c.loginHost ?? "").Trim(), host, StringComparison.OrdinalIgnoreCase))
                            return (c.presencePort, "presencePort in the config");
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning($"[Presence] {configPath}: {e.Message}"); }
            return (loginPort + 3, "login port + 3");
        }
    }
}
