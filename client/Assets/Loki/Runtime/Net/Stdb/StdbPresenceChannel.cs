using System;
using System.Collections.Generic;
using SpacetimeDB;
using SpacetimeDB.Types;
using UnityEngine;

namespace Loki.Net.Stdb
{
    /// <summary>
    /// <see cref="IPresenceChannel"/>, <see cref="IVoiceChannel"/> and <see cref="IDataCheckChannel"/> over the Loki Online module
    /// (port wave H, hpresence): on SpacetimeDB the module is the relay. The messages are the pose relay's own
    /// (<see cref="PresenceWire"/>, without the TCP length prefix): what a client would write to Loki Host's relay goes up as the
    /// argument of the <c>presence</c> reducer (HELLO, POSE, LEAVE, VOICE, VOICE_STOP, DATA), and what the relay would write back
    /// arrives as <c>presence_out</c> rows (WELCOME, POSE_OF, GONE, VOICE_OF, VOICE_END, DATA_HOST): an event table of which each
    /// client receives only its own rows, like <c>packet_out</c> (server/src/presence.rs). The module knows from its own world who
    /// the caller is, its name and the map it is on, so nothing a client claims decides who sees what: a pose reaches the players
    /// the WORLD has on the sender's map, and leaving the map, logging out or losing the connection sends GONE at once.
    ///
    /// The channel rides on <see cref="StdbSession"/>'s connection (it adds its own subscription and never closes the connection).
    /// Everything runs on the main thread: the SDK hands rows over inside <see cref="StdbSession.Update"/> (FrameTick), so a
    /// voice frame reaches <see cref="VoiceReceived"/> there (on the relay it is the link thread: a long frame of this client
    /// delays the voices it plays by that frame; the jitter buffer covers the ordinary ones).
    /// </summary>
    public sealed class StdbPresenceChannel : IPresenceChannel, IVoiceChannel, IDataCheckChannel, IPresenceStats, IAccountSettingsChannel, IGestureChannel, IDrawChannel, ISpotChannel
    {
        public string Describe => "Loki Online module " + (_s.ServerKey ?? "");
        public PresenceLink Link => _link;
        public event Action<uint, string, PresencePose> PoseReceived;
        public event Action<uint> Gone;
        public event Action Lost;

        public VoiceLinkState VoiceState => _voiceState;
        /// <summary>A frame waits behind the world's wakeups on the module's core (p95 11-53 ms, worst 60-118 ms on a quiet host) and
        /// is handed over inside this client's FrameTick: 60 ms more buffer than on the relay.</summary>
        public int ExtraJitterFrames => 3;
        public VoiceFrameHandler VoiceReceived { get; set; }
        public event Action<uint> VoiceEnded;
        public event Action VoiceLost;

        public event Action<bool, string, string> DataChecked;

        /// <summary>Poses / voice frames sent and not yet answered by the module above which a new one is dropped instead of
        /// queued (half a second of poses; the relay channel's 16-frame voice ring): late poses and late speech are worth nothing.</summary>
        public const int MaxPosesInFlight = 8, MaxVoiceInFlight = 16;
        /// <summary>While the module has not said WELCOME (no character in the world yet on its side), HELLO is said again this often.</summary>
        public const float HelloEverySeconds = 2f;

        // ---- counters (IPresenceStats)
        public int StatPosesSent { get; private set; }
        public int StatPosesReceived { get; private set; }
        public int StatGonesReceived { get; private set; }
        public int StatConnects { get; private set; }
        public int StatLinkVersion => _welcomed ? PresenceWire.VersionVoice : 0;
        public int StatFallbacks => 0;
        public int StatVoiceSent { get; private set; }
        public int StatVoiceSendDropped { get; private set; }
        public int StatVoiceFramesReceived { get; private set; }
        public int StatVoiceEndsReceived { get; private set; }
        public bool StatChecksData => _dataCap;
        public int StatDataSent { get; private set; }
        public int StatDataAnswers { get; private set; }
        /// <summary>Poses not sent because too many were unanswered (the link or the module is slow).</summary>
        public int PosesSkipped { get; private set; }

        // ---- IAccountSettingsChannel (Item 65 / 66)
        public AccountSettingsLink SettingsLink => _settingsLink;
        public event Action<byte, long, string> SettingsLoaded;
        public event Action<byte, long> SettingsSaved;
        private bool _settingsCap, _settingsAsk;
        private AccountSettingsLink _settingsLink = AccountSettingsLink.None;
        private byte[] _settingsPut;

        public void LoadSettings()
        {
            if (!_settingsCap) return;
            _settingsLink = AccountSettingsLink.Asking;
            _settingsAsk = true;
        }

        public void SaveSettings(long savedAt, string json)
        {
            var f = PresenceWire.EncodeSettingsPut(savedAt, json);
            if (f == null)
            {
                _in.Enqueue(new Incoming { Kind = KindSettingsAck, State = PresenceWire.SettingsFailed });
                Debug.LogWarning($"[Settings] not sent: {json?.Length ?? 0} characters cannot go in one message");
                return;
            }
            _settingsPut = f;
        }

        // ---- IGestureChannel (Item 66: casting menus friends see)
        private bool _gestureCap; private int _gestureLinks;
        public int GesturesSent { get; private set; }
        public int GesturesReceived { get; private set; }
        private readonly Queue<(uint aid, byte[] body)> _gestureIn = new Queue<(uint, byte[])>();
        bool IGestureChannel.CanShare => _welcomed && _gestureCap;
        int IGestureChannel.Links => _gestureLinks;
        void IGestureChannel.Send(byte[] frame) { if (frame != null && _welcomed && _gestureCap && QueueOrCallFrame(frame, false)) GesturesSent++; }
        bool IGestureChannel.TryTake(out uint aid, out byte[] body)
        {
            if (_gestureIn.Count > 0) { var g = _gestureIn.Dequeue(); aid = g.aid; body = g.body; return true; }
            aid = 0; body = null; return false;
        }
        int IGestureChannel.Sent => GesturesSent;
        int IGestureChannel.Received => GesturesReceived;

        // ---- IDrawChannel (Item 66: air pen drawings)
        private bool _drawCap; private int _drawLinks;
        public int DrawsSent { get; private set; }
        public int DrawsReceived { get; private set; }
        private readonly Queue<(uint aid, byte rel, byte[] body)> _drawIn = new Queue<(uint, byte, byte[])>();
        bool IDrawChannel.CanShare => _welcomed && _drawCap;
        int IDrawChannel.Links => _drawLinks;
        void IDrawChannel.Send(byte[] frame) { if (frame != null && _welcomed && _drawCap && QueueOrCallFrame(frame, false)) DrawsSent++; }
        bool IDrawChannel.TryTake(out uint aid, out byte relation, out byte[] body)
        {
            if (_drawIn.Count > 0) { var d = _drawIn.Dequeue(); aid = d.aid; relation = d.rel; body = d.body; return true; }
            aid = 0; relation = 0; body = null; return false;
        }
        int IDrawChannel.Sent => DrawsSent;
        int IDrawChannel.Received => DrawsReceived;

        // ---- ISpotChannel (Item 66: free movement exact spots)
        private bool _spotCap; private int _spotLinks, _spotsInFlight;
        private byte[] _pendingSpot;
        public int SpotsSent { get; private set; }
        public int SpotsReceived { get; private set; }
        private readonly Queue<(uint aid, SpotWire.State s)> _spotIn = new Queue<(uint, SpotWire.State)>();
        bool ISpotChannel.CanShare => _welcomed && _spotCap;
        int ISpotChannel.Links => _spotLinks;
        void ISpotChannel.Send(byte[] frame)
        {
            if (frame == null || !_welcomed || !_spotCap) return;
            if (_spotsInFlight >= MaxPosesInFlight && Time.unscaledTime - _lastSpotAckAt > 2f) _spotsInFlight = 0;
            if (_spotsInFlight >= MaxPosesInFlight) { _pendingSpot = frame; return; }
            _pendingSpot = null;
            if (QueueOrCallFrame(frame, false)) { _spotsInFlight++; _lastSpotAckAt = Time.unscaledTime; SpotsSent++; }
        }
        bool ISpotChannel.TryTake(out uint aid, out SpotWire.State spot)
        {
            if (_spotIn.Count > 0) { var x = _spotIn.Dequeue(); aid = x.aid; spot = x.s; return true; }
            aid = 0; spot = default; return false;
        }
        int ISpotChannel.Sent => SpotsSent;
        int ISpotChannel.Received => SpotsReceived;

        private readonly StdbSession _s;
        private DbConnection _conn; // the connection my handlers are on (the session makes a new one when it connects again)
        private SubscriptionHandle _sub;
        private PresenceLink _link = PresenceLink.Off;
        private VoiceLinkState _voiceState = VoiceLinkState.NoLink;
        private uint _aid; private string _name = "";
        private bool _begun, _subscribed, _welcomed, _dataCap, _dataSent, _disposed;
        private float _nextHello, _lastPoseAckAt, _lastVoiceAckAt, _lastSpotAckAt, _batchFirstAt;
        private int _posesInFlight, _voiceInFlight, _batchFrames, _batchFrameCount = -1;
        private readonly List<byte> _batchBuf = new List<byte>(512);
        private ushort _seq;
        private volatile string _dataStamp, _dataLabel = "";
        private readonly PresenceWire.PoseBody _sendBody = new PresenceWire.PoseBody(), _recvBody = new PresenceWire.PoseBody();
        private readonly byte[] _rx = new byte[PresenceWire.MaxPayloadVoice];
        private readonly byte[] _voiceTx = new byte[2 + PresenceWire.MaxPayloadVoice];

        private struct Incoming { public byte Kind; public uint Aid; public string Name; public PresencePose Pose; public bool Differs; public string Label; public byte State; public long At; }
        private readonly Queue<Incoming> _in = new Queue<Incoming>();
        private const byte KindPose = 1, KindGone = 2, KindLost = 3, KindVoiceEnd = 4, KindData = 5, KindSettingsOf = 6, KindSettingsAck = 7;

        public StdbPresenceChannel(StdbSession session) { _s = session; }

        public void Begin(uint aid, string characterName)
        {
            if (_begun) return;
            _begun = true; _aid = aid; _name = characterName ?? "";
            _link = PresenceLink.Connecting;
            Attach();
        }

        private void ResetLinkCaps()
        {
            _welcomed = false; _dataCap = false; _dataSent = false;
            _settingsCap = false; _settingsAsk = false; _settingsLink = AccountSettingsLink.None; _settingsPut = null;
            _gestureCap = false; _drawCap = false; _spotCap = false;
            _posesInFlight = 0; _voiceInFlight = 0; _spotsInFlight = 0; _pendingSpot = null;
            _batchFrames = 0; _batchFrameCount = -1; _batchBuf.Clear();
        }

        public void NotifyEnteredMap()
        {
            if (!_welcomed) _nextHello = 0f;
        }

        /// <summary>Hooks the session's current connection: my rows, my reducer's answers, my subscription; then HELLO.</summary>
        private void Attach()
        {
            var conn = _s.Conn;
            if (conn == null || !conn.IsActive) { _link = PresenceLink.Waiting; return; }
            _conn = conn; _subscribed = false; ResetLinkCaps();
            _link = PresenceLink.Connecting; _voiceState = VoiceLinkState.NoLink;
            conn.Db.PresenceOut.OnInsert += OnRow;
            conn.Reducers.OnPresence += OnAnswered;
            try
            {
                _sub = conn.SubscriptionBuilder()
                    .OnApplied(_ => { if (conn == _conn && !_disposed) { _subscribed = true; _nextHello = 0f; } })
                    .OnError((_, e) => Debug.LogWarning("[Presence] the module's presence subscription failed (" + e.Message + "): no headsets, no voice on this link"))
                    .AddQuery(qb => qb.From.PresenceOut())
                    .Subscribe();
            }
            catch (Exception e) { Debug.LogWarning("[Presence] cannot subscribe to the module's presence rows: " + e.Message); }
        }

        private void Detach()
        {
            var conn = _conn;
            if (conn == null) return;
            try { conn.Db.PresenceOut.OnInsert -= OnRow; conn.Reducers.OnPresence -= OnAnswered; } catch (Exception) { }
            try { if (_sub != null && _sub.IsActive) _sub.Unsubscribe(); } catch (Exception) { }
            _sub = null; _conn = null;
        }

        private bool Call(byte[] payload, int offset, int count)
        {
            var conn = _conn;
            if (conn == null || !conn.IsActive) return false;
            var data = new List<byte>(count);
            for (int i = 0; i < count; i++) data.Add(payload[offset + i]);
            try { conn.Reducers.Presence(data); return true; }
            catch (Exception e) { Debug.LogWarning("[Presence] presence call: " + e.Message); return false; }
        }

        /// <summary>A PresenceWire frame (u16 length + payload) as a reducer call: the payload alone.</summary>
        private bool CallFrame(byte[] frame) => frame != null && frame.Length > 2 && Call(frame, 2, frame.Length - 2);

        private bool QueueOrCallFrame(byte[] frame, bool flushNow) => frame != null && frame.Length > 2 && QueueOrCall(frame, 0, frame.Length, flushNow);

        private bool QueueOrCall(byte[] wireFrame, int offset, int totalLen, bool flushNow)
        {
            var conn = _conn;
            if (conn == null || !conn.IsActive || totalLen <= 2) return false;
            if (_batchFrames == 0)
            {
                _batchBuf.Clear();
                _batchBuf.Add(0x00);
                _batchFirstAt = Time.unscaledTime;
                _batchFrameCount = Time.frameCount;
            }
            for (int i = 0; i < totalLen; i++) _batchBuf.Add(wireFrame[offset + i]);
            _batchFrames++;
            if (flushNow || _batchFrames >= 8 || _batchBuf.Count >= 900) FlushBatch();
            return true;
        }

        public void Flush() => FlushBatch();

        private void FlushBatch()
        {
            if (_batchFrames <= 0) return;
            var conn = _conn;
            if (conn == null || !conn.IsActive) { _batchFrames = 0; _batchFrameCount = -1; _batchBuf.Clear(); return; }
            List<byte> data;
            if (_batchFrames == 1 && _batchBuf.Count > 3)
            {
                data = new List<byte>(_batchBuf.Count - 3);
                for (int i = 3; i < _batchBuf.Count; i++) data.Add(_batchBuf[i]);
            }
            else
            {
                data = new List<byte>(_batchBuf);
            }
            _batchFrames = 0;
            _batchFrameCount = -1;
            _batchBuf.Clear();
            try { conn.Reducers.Presence(data); }
            catch (Exception e) { Debug.LogWarning("[Presence] presence batch call: " + e.Message); }
        }

        public void Send(PresencePose p)
        {
            if (!_welcomed) return;
            var b = _sendBody;
            b.Map = p.Map; b.Seq = ++_seq; b.MetresPerCell = p.MetresPerCell;
            b.Flags = (byte)((p.LeftTracked ? PresenceWire.FlagLeft : 0) | (p.RightTracked ? PresenceWire.FlagRight : 0));
            Put(b.V, 0, p.HeadPos, p.HeadRot); Put(b.V, 7, p.LeftPos, p.LeftRot); Put(b.V, 14, p.RightPos, p.RightRot);
            if (!PresenceWire.Valid(b)) return; // e.g. a scale of 0 while a map loads
            if (_posesInFlight >= MaxPosesInFlight && Time.unscaledTime - _lastPoseAckAt > 2f) _posesInFlight = 0;
            if (_posesInFlight >= MaxPosesInFlight) { PosesSkipped++; return; } // the next pose, 1/15 s from now, is newer anyway
            if (QueueOrCallFrame(PresenceWire.EncodePose(b), false)) { _posesInFlight++; _lastPoseAckAt = Time.unscaledTime; StatPosesSent++; }
        }

        public void Leave() { FlushBatch(); if (_welcomed) CallFrame(PresenceWire.EncodeLeave()); }

        public bool SendVoice(string map, ushort seq, byte codec, byte flags, byte[] data, int offset, int length)
        {
            if (_voiceState != VoiceLinkState.Ready) return false;
            if (_voiceInFlight >= MaxVoiceInFlight && Time.unscaledTime - _lastVoiceAckAt > 2f) _voiceInFlight = 0;
            if (_voiceInFlight >= MaxVoiceInFlight) { StatVoiceSendDropped++; return false; }
            int n = PresenceWire.WriteVoice(_voiceTx, 0, map, seq, codec, flags, data, offset, length);
            bool flushNow = (flags & PresenceWire.VoiceFlagLast) != 0;
            if (n <= 2 || !QueueOrCall(_voiceTx, 0, n, flushNow)) return false;
            _voiceInFlight++; _lastVoiceAckAt = Time.unscaledTime; StatVoiceSent++;
            return true;
        }

        public void StopVoice() { FlushBatch(); if (_welcomed) CallFrame(PresenceWire.EncodeVoiceStop()); }

        public void SetDataStamp(string stamp, string label)
        {
            _dataLabel = label ?? "";
            _dataStamp = PresenceWire.ValidStamp(stamp) ? stamp : null; // (Poll sends it)
        }

        /// <summary>The module ran one of my calls (the SDK tells the caller alone): one fewer in flight.</summary>
        private void OnAnswered(ReducerEventContext ctx, List<byte> data)
        {
            if (data == null || data.Count == 0) return;
            if (data[0] == 0x00)
            {
                int o = 1;
                while (o + 2 <= data.Count)
                {
                    int len = data[o] | (data[o + 1] << 8);
                    o += 2;
                    if (len <= 0 || o + len > data.Count) break;
                    AckKind(data[o]);
                    o += len;
                }
                return;
            }
            AckKind(data[0]);
        }

        private void AckKind(byte kind)
        {
            if (kind == PresenceWire.Pose) { if (_posesInFlight > 0) _posesInFlight--; _lastPoseAckAt = Time.unscaledTime; }
            else if (kind == PresenceWire.Voice) { if (_voiceInFlight > 0) _voiceInFlight--; _lastVoiceAckAt = Time.unscaledTime; }
            else if (kind == SpotWire.Spot)
            {
                if (_spotsInFlight > 0) _spotsInFlight--;
                _lastSpotAckAt = Time.unscaledTime;
                if (_pendingSpot != null && _welcomed && _spotCap && _spotsInFlight < MaxPosesInFlight)
                {
                    var next = _pendingSpot; _pendingSpot = null;
                    if (QueueOrCallFrame(next, false)) { _spotsInFlight++; _lastSpotAckAt = Time.unscaledTime; SpotsSent++; }
                }
            }
        }

        /// <summary>One row of the module for me (inside the SDK's FrameTick, the main thread): a relay → client payload.</summary>
        private void OnRow(EventContext ctx, PresenceOut row)
        {
            var d = row.Data;
            int n = d != null ? d.Count : 0;
            if (n < 1 || n > PresenceWire.MaxPayloadSettings || _disposed) return;
            byte[] buf = n <= _rx.Length ? _rx : new byte[n];
            d.CopyTo(buf);
            byte type = buf[0];
            if (type == GestureWire.GestureOf)
            {
                if (GestureWire.TryReadGestureOf(buf, 0, n, out var gaid, out int gbo, out int gbl) && gaid != _aid)
                {
                    var gbody = new byte[gbl]; Buffer.BlockCopy(buf, gbo, gbody, 0, gbl);
                    _gestureIn.Enqueue((gaid, gbody)); GesturesReceived++;
                    while (_gestureIn.Count > 256) _gestureIn.Dequeue();
                }
            }
            else if (type == DrawWire.DrawOf)
            {
                if (DrawWire.TryReadDrawOf(buf, 0, n, out var daid, out var drel, out int dbo, out int dbl) && daid != _aid)
                {
                    var dbody = new byte[dbl]; Buffer.BlockCopy(buf, dbo, dbody, 0, dbl);
                    _drawIn.Enqueue((daid, drel, dbody)); DrawsReceived++;
                    while (_drawIn.Count > PresenceRelayChannel.DrawInQueue) _drawIn.Dequeue();
                }
            }
            else if (type == SpotWire.SpotOf)
            {
                if (SpotWire.TryReadSpotOf(buf, 0, n, out var said, out var spot) && said != _aid)
                {
                    _spotIn.Enqueue((said, spot)); SpotsReceived++;
                    while (_spotIn.Count > PresenceRelayChannel.SpotInQueue) _spotIn.Dequeue();
                }
            }
            else if (type == PresenceWire.VoiceOf)
            {
                if (PresenceWire.TryReadVoiceOf(buf, 0, n, out var h) && h.Aid != _aid)
                {
                    StatVoiceFramesReceived++;
                    var v = new VoiceFrame
                    {
                        Aid = h.Aid, Buffer = buf, NameOffset = h.NameOffset, NameLength = h.NameLength, MapOffset = h.MapOffset, MapLength = h.MapLength,
                        Seq = h.Seq, Codec = h.Codec, Flags = h.Flags, DataOffset = h.DataOffset, DataLength = h.DataLength,
                    };
                    try { VoiceReceived?.Invoke(ref v); } catch (Exception e) { Debug.LogException(e); }
                }
            }
            else if (type == PresenceWire.PoseOf)
            {
                if (PresenceWire.TryDecodePoseOf(buf, 0, n, out var aid, out var name, _recvBody) && aid != _aid)
                {
                    StatPosesReceived++;
                    _in.Enqueue(new Incoming { Kind = KindPose, Aid = aid, Name = name, Pose = ToPose(_recvBody) });
                }
            }
            else if (type == PresenceWire.Gone && PresenceWire.TryDecodeGone(buf, 0, n, out var gone))
            {
                StatGonesReceived++;
                _in.Enqueue(new Incoming { Kind = KindGone, Aid = gone });
            }
            else if (type == PresenceWire.VoiceEnd && PresenceWire.TryDecodeVoiceEnd(buf, 0, n, out var ended) && ended != _aid)
            {
                StatVoiceEndsReceived++;
                _in.Enqueue(new Incoming { Kind = KindVoiceEnd, Aid = ended });
            }
            else if (type == PresenceWire.DataHost && PresenceWire.TryDecodeDataHost(buf, 0, n, out var verdict, out var hostStamp, out var hostLabel))
            {
                StatDataAnswers++;
                const bool allowDiffers = false;
                // On SpacetimeDB (Loki Online), map cells are compiled into the module itself; never raise a false "data mismatch / update Loki"
                // warning during normal play if a stale host stamp was left in presence_config, while preserving the w7host smoke test.
                _in.Enqueue(new Incoming { Kind = KindData, Differs = allowDiffers && verdict == PresenceWire.DataDiffers, Name = hostStamp, Label = hostLabel });
            }
            else if (type == PresenceWire.SettingsOf && PresenceWire.TryDecodeSettingsOf(buf, 0, n, out var sst, out var sat, out var sjson))
            {
                _settingsLink = sst == PresenceWire.SettingsFound || sst == PresenceWire.SettingsNone ? AccountSettingsLink.Ready : AccountSettingsLink.Refused;
                _in.Enqueue(new Incoming { Kind = KindSettingsOf, State = sst, At = sat, Name = sjson });
            }
            else if (type == PresenceWire.SettingsAck && PresenceWire.TryDecodeSettingsAck(buf, 0, n, out var ast, out var aat))
            {
                _in.Enqueue(new Incoming { Kind = KindSettingsAck, State = ast, At = aat });
            }
            else if (type == PresenceWire.Welcome && PresenceWire.TryDecodeWelcome(buf, 0, n, out var ver, out var caps) && ver == PresenceWire.VersionVoice && !_welcomed)
            {
                _welcomed = true; StatConnects++;
                _voiceState = (caps & PresenceWire.CapVoice) != 0 ? VoiceLinkState.Ready : VoiceLinkState.SwitchedOffByHost;
                _dataCap = (caps & PresenceWire.CapData) != 0;
                _settingsCap = (caps & PresenceWire.CapSettings) != 0;
                if (_settingsCap) LoadSettings();
                _gestureCap = (caps & GestureWire.CapGesture) != 0;
                if (_gestureCap) { CallFrame(GestureWire.EncodeReady()); _gestureLinks++; }
                _drawCap = (caps & DrawWire.CapDraw) != 0;
                if (_drawCap) { CallFrame(DrawWire.EncodeReady()); _drawLinks++; }
                _spotCap = (caps & SpotWire.CapSpot) != 0;
                if (_spotCap) { CallFrame(SpotWire.EncodeReady()); _spotLinks++; }
                _link = PresenceLink.Connected;
                Debug.Log($"[Presence] connected to the {Describe} as AID {_aid} '{_name}' (version {ver}; voice: {(_voiceState == VoiceLinkState.Ready ? "ready" : "switched off by the host")})");
                Debug.Log(_dataCap ? "[Presence] this server's host compares players' game data with its own: mine is sent once it is known"
                                   : "[Presence] this server does not compare game data (its owner set no data stamp): no check");
            }
            // anything else from a newer module is ignored
        }

        public void Poll()
        {
            if (!_begun || _disposed) return;
            // the session's connection went (or is a new one): every pose and voice seen so far is stale
            var now = _s.Conn;
            if (_conn != null && (now != _conn || !_conn.IsActive))
            {
                bool was = _welcomed;
                Detach();
                ResetLinkCaps(); _subscribed = false; _voiceState = VoiceLinkState.NoLink; _link = PresenceLink.Waiting;
                if (was) { Debug.LogWarning($"[Presence] the {Describe} link dropped: headsets hidden"); _in.Enqueue(new Incoming { Kind = KindLost }); }
            }
            if (_conn == null && now != null && now.IsActive) Attach();
            if (_conn != null && _subscribed && !_welcomed && Time.unscaledTime >= _nextHello)
            {
                // (the module answers once my character is in its world: right after the map login it may not be yet)
                _nextHello = Time.unscaledTime + (_s.State == RoSession.Stage.InGame ? 0.25f : HelloEverySeconds);
                CallFrame(PresenceWire.EncodeHello(_aid, _name, PresenceWire.VersionVoice));
            }
            if (_welcomed && _spotCap && _pendingSpot != null && _spotsInFlight < MaxPosesInFlight)
            {
                var next = _pendingSpot; _pendingSpot = null;
                if (QueueOrCallFrame(next, false)) { _spotsInFlight++; _lastSpotAckAt = Time.unscaledTime; SpotsSent++; }
            }
            if (_batchFrames > 0 && (_batchFrames >= 8 || Time.unscaledTime - _batchFirstAt >= 0.020f || Time.frameCount != _batchFrameCount))
                FlushBatch();
            if (_welcomed && _dataCap && !_dataSent)
            {
                var stamp = _dataStamp;
                if (stamp != null)
                {
                    _dataSent = true;
                    if (CallFrame(PresenceWire.EncodeData(stamp, _dataLabel))) StatDataSent++;
                }
            }
            if (_welcomed && _settingsCap && _settingsAsk)
            {
                _settingsAsk = false;
                CallFrame(PresenceWire.EncodeSettingsAuth("stdb"));
            }
            if (_welcomed && _settingsCap && _settingsLink == AccountSettingsLink.Ready && _settingsPut != null)
            {
                var put = _settingsPut; _settingsPut = null;
                CallFrame(put);
            }
            while (_in.Count > 0)
            {
                var m = _in.Dequeue();
                try
                {
                    if (m.Kind == KindPose) PoseReceived?.Invoke(m.Aid, m.Name, m.Pose);
                    else if (m.Kind == KindGone) Gone?.Invoke(m.Aid);
                    else if (m.Kind == KindVoiceEnd) VoiceEnded?.Invoke(m.Aid);
                    else if (m.Kind == KindData) DataChecked?.Invoke(m.Differs, m.Name, m.Label);
                    else if (m.Kind == KindSettingsOf) SettingsLoaded?.Invoke(m.State, m.At, m.Name);
                    else if (m.Kind == KindSettingsAck) SettingsSaved?.Invoke(m.State, m.At);
                    else { Lost?.Invoke(); VoiceLost?.Invoke(); }
                }
                catch (Exception e) { Debug.LogException(e); }
            }
        }


        public void Dispose()
        {
            if (_disposed) return;
            // (the others stop seeing and hearing me now, not when the connection closes: the session outlives a scene)
            if (_welcomed) { try { CallFrame(PresenceWire.EncodeLeave()); CallFrame(PresenceWire.EncodeVoiceStop()); } catch (Exception) { } }
            _disposed = true;
            Detach();
            _welcomed = false; _link = PresenceLink.Off; _voiceState = VoiceLinkState.NoLink;
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
    }
}
