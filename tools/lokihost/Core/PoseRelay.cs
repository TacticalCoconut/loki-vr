using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Loki.Net;
using Loki.Horde.Net;

namespace LokiHost.Core;

/// <summary>
/// The headset pose relay (client wave 3, w3presence): every Loki client in VR sends its head and hands 10-20 times a second,
/// in the map's own frame, with its map name; the relay forwards each pose to the other clients on the same map, adding the
/// sender's RO account id (AID) and character name from its HELLO. A separate channel beside the RO protocol: rAthena never
/// sees it, and a SpacetimeDB implementation of the client's IPresenceChannel can replace it later without any gameplay
/// change. The wire format is the client's own file (client/Assets/Loki/Runtime/Net/PresenceWire.cs, compiled into this
/// project). The threat model is in tools/lokihost/README.md ("Headsets"): a friends' server, no authentication; a spoofed
/// AID moves or hides a floating headset, nothing else. Everything is bounded: clients, clients per address, frame size,
/// messages per second, queued frames per client, the time to say HELLO, idle time.
///
/// Voice (client wave 6, w6voice) rides the same connections: a client that said HELLO with version 2 may send VOICE frames
/// (20 ms of encoded speech each, only while its noise gate is open); the relay stamps them with the speaker's AID and name and
/// forwards them to every other version-2 client, WHATEVER ITS MAP (friends keep talking while one walks to town; each listener
/// decides what it plays), and says VOICE_END when a speaker stops sharing or disconnects. The relay never decodes, stores or logs
/// the sound. A version-1 client (before wave 6) gets exactly what it got before: never a voice frame. With voice switched off
/// (<see cref="VoiceEnabled"/> false: HostSettings.VoiceEnabled) WELCOME 2 says so and VOICE frames are dropped.
///
/// The game-data check (client wave 7, w7host): when the relay was given its host's data stamp (<see cref="HostDataStamp"/>:
/// a hash of the map cells of the host's Ragnarok Online install, client IO/MapDataStamp.cs), WELCOME 2 carries the capability
/// bit CapData; a client of wave 7 then sends its own stamp once (DATA), the relay answers "the same" or "differs" with the
/// host's stamp (DATA_HOST), keeps the answer per player (<see cref="Players"/>: Loki Host's window lists them) and logs one
/// line. An older client never sends DATA and is listed as "no stamp"; without a host stamp the bit is not set and nothing
/// is compared. The stamp is not a secret and proves nothing: a client that lies about it only fools its own warning.
///
/// Horde (client wave 8, w8core): when the relay was given a <see cref="Horde"/> service, WELCOME 2 carries the capability
/// bit HordeWire.CapHorde; a wave-8 client then says H_HELLO and may send Horde messages (0x10..0x1F: its stick, answers,
/// choices), which go to the service; the service sends Horde frames (0x90..0x9F, up to 65,535 bytes) only to clients that
/// said H_HELLO. Older clients never see the bit's effect; an older relay never sets it. A malformed or untimely Horde message
/// is dropped (the connection stays: headsets and voice go on); more than <see cref="Limits.MaxHordePerSecond"/> a second are
/// dropped. When a full queue pushes out a Horde frame the client is marked, and the service sends it a key snapshot next.
/// Client wave 9 (h9move): H_HELLO carries Horde's protocol version and H_STATUS the relay's; a client of another version is
/// told so by the service (never a garbled run) and keeps everything else (HordeService.Hello).
///
/// Settings per account (client wave 18, w18acct; the design's point 1): when the relay was given an <see cref="AccountSettings"/> store
/// (Loki Host: its database, AccountSettingsDb), WELCOME 2 carries PresenceWire.CapSettings; a wave-18 client then proves its
/// login (SETTINGS_AUTH: rAthena's web token of this login, checked by the store against the login table) and is answered with
/// its account's stored settings (SETTINGS_OF); once accepted it may send its own (SETTINGS_PUT, up to 60,000 bytes of JSON:
/// only then does this connection's reader take frames longer than a voice frame), which the store keeps unless a newer save is
/// stored (SETTINGS_ACK). A connection is only ever answered with ITS AID's settings, and only after its proof matched. The
/// store's work runs off the read loop, one message at a time per connection, in order. A malformed settings message closes
/// the connection (as DATA); more than <see cref="Limits.MaxSettingsAuthPerConnection"/> proofs or
/// <see cref="Limits.MaxSettingsPutsPerMinute"/> saves a minute are dropped. Older clients never see the bit's effect; an older
/// relay never sets it; a relay without a store (the standalone relay without --accounts-db) does not set it.
///
/// Drawings (client wave 20, w20pen; the design's point 57, the air pen): when <see cref="DrawingsEnabled"/>, WELCOME 2 carries DrawWire.CapDraw; a
/// wave-20 client then says DRAW_READY and may send DRAW frames (parts of its 3D strokes in the map's frame, erasures, the list of its strokes),
/// which go to the READY players of its map whom the frame's audience ("Drawings seen by") allows, by the casting menus' rule, each copy with
/// what the sender is to that viewer (DRAW_OF's relation byte: the viewer's client applies its own "Drawings I see"). No drawing is kept here; a
/// viewer who was sent a player's strokes and is left out later is told once to drop them.
///
/// Exact spots (free movement, design note 2026-10-14): when <see cref="SpotsEnabled"/>, WELCOME 2 carries SpotWire.CapSpot; a new client
/// then says SPOT_READY and sends where it draws its own character (SPOT: map, place, velocity, facing), which goes to the READY players on that
/// map whose own last spot is within <see cref="Limits.SpotRange"/> (SPOT_OF). rAthena keeps the characters on cells; the clients draw them where
/// their players stopped. Nothing is kept but each connection's last spot.
/// </summary>
public sealed class PoseRelay : IAsyncDisposable, IHordeTransport
{
    public sealed class Limits
    {
        public int MaxClients { get; init; } = 64;
        /// <summary>Friends behind one home router share a public address; a LAN party shares none.</summary>
        public int MaxPerAddress { get; init; } = 16;
        public TimeSpan HelloWithin { get; init; } = TimeSpan.FromSeconds(5);
        /// <summary>A client that sends nothing (not even a PING: clients ping every 2 s off a map) for this long is closed.</summary>
        public TimeSpan IdleAfter { get; init; } = TimeSpan.FromSeconds(15);
        /// <summary>Poses above this rate are dropped (clients send 15 per second).</summary>
        public int MaxPosesPerSecond { get; init; } = 40;
        /// <summary>Frames waiting for a slow receiver; the oldest are dropped first (a newer pose supersedes them).</summary>
        public int QueueFrames { get; init; } = 64;
        /// <summary>Voice frames above this rate are dropped. A speaker sends 50 per second (20 ms each); after a stall of the
        /// speaker's PC the client sends at most 5 late frames at once, and after a stall of its connection (a lost packet being
        /// sent again) the frames of up to half a second arrive together: 50 + 25.</summary>
        public int MaxVoicePerSecond { get; init; } = 75;
        /// <summary>The queue of a version-2 (voice) client: with voice, 64 frames are a third of a second of three people talking
        /// plus their poses; 128 keep about 0.6 s. The oldest are dropped first: late speech is worth nothing.</summary>
        public int VoiceQueueFrames { get; init; } = 128;
        /// <summary>Client wave 18 (w18share): GESTURE messages taken per second from one connection (a client sends at most 15 a second
        /// per menu while it changes, five menus; more are dropped, never the connection); how old a player's relations may be before
        /// the relay reads them again (rAthena itself writes them only at a character's save: see RelationsDb).</summary>
        public int MaxGesturesPerSecond { get; init; } = 90;
        public TimeSpan RelationsRefresh { get; init; } = TimeSpan.FromSeconds(30);
        /// <summary>Client wave 20 (w20pen): DRAW messages taken per second from one connection (a client sends at most 10 a second while it
        /// draws and 25 a second while it sends its drawings again; more are dropped, never the connection).</summary>
        public int MaxDrawsPerSecond { get; init; } = 60;
        /// <summary>DATA messages taken from one connection (a client sends one; more are dropped, never forwarded).</summary>
        public int MaxDataPerConnection { get; init; } = 4;
        /// <summary>Horde messages taken per second from one connection (a player sends 40 stick ticks a second since client wave 9,
        /// a burst of up to 5 after a hitch of its frame, and a few more).</summary>
        public int MaxHordePerSecond { get; init; } = 120;
        /// <summary>Client wave 18 (w18acct): SETTINGS_AUTH messages taken from one connection (a client sends one per link, and one
        /// more when the host held a newer save than its own); SETTINGS_PUT messages taken a minute (a client sends one a few
        /// seconds after the player changed something).</summary>
        public int MaxSettingsAuthPerConnection { get; init; } = 8;
        public int MaxSettingsPutsPerMinute { get; init; } = 30;
        /// <summary>Free movement (wfree): SPOT messages taken per second from one connection (a client sends 15 a second while its character
        /// moves and one more at each start, stop or turn; more are dropped, never the connection); how far (cells, Chebyshev) a viewer's own
        /// last spot may be from the sender's (rAthena shows a client the units within area_size 14 cells: conf/battle/client.conf:39).</summary>
        public int MaxSpotsPerSecond { get; init; } = 40;
        public float SpotRange { get; init; } = 32f;
    }

    /// <summary>Client wave 18 (w18acct): where each account's settings are kept (null: this relay keeps none and does not say it can).</summary>
    public IAccountSettingsStore? AccountSettings { get; set; }
    long _settingsLoads, _settingsSaves, _settingsRefused;
    /// <summary>Settings sent to their account (SETTINGS_OF found), stored (SETTINGS_ACK stored), proofs refused.</summary>
    public long SettingsLoads => Interlocked.Read(ref _settingsLoads);
    public long SettingsSaves => Interlocked.Read(ref _settingsSaves);
    public long SettingsRefused => Interlocked.Read(ref _settingsRefused);

    /// <summary>Horde's service (client wave 8): null = this relay does not run Horde (the capability bit is not set).</summary>
    public HordeService? Horde { get; set; }

    bool IHordeTransport.SendHorde(uint aid, byte[] frame)
    {
        lock (_lock)
        {
            foreach (var p in _peers) if (p.Aid == aid && p.HordeHello && !p.Closing) return p.Enqueue(frame);
        }
        return false;
    }

    int IHordeTransport.HordeQueueDepth(uint aid)
    {
        lock (_lock) foreach (var p in _peers) if (p.Aid == aid && p.HordeHello && !p.Closing) return p.Queued;
        return 0;
    }

    bool IHordeTransport.TakeHordeLost(uint aid)
    {
        lock (_lock) foreach (var p in _peers) if (p.Aid == aid && p.HordeHello && !p.Closing) { bool l = p.HordeLost; p.HordeLost = false; return l; }
        return false;
    }

    public int Port { get; private set; }
    public Limits Limit { get; }
    /// <summary>One line per notable event (a client joins, changes map, leaves, is refused); never per pose.</summary>
    public event Action<string>? Log;

    TcpListener? _listener;
    CancellationTokenSource? _cts;
    Task? _acceptLoop;
    readonly object _lock = new();
    readonly List<Peer> _peers = new();
    long _forwarded, _dropped, _voiceIn, _voiceForwarded, _voiceDropped, _queueDropped, _dataIn, _dataDiffers;

    public PoseRelay(int port, Limits? limits = null, bool voice = true, string dataStamp = "", string dataLabel = "")
    {
        Port = port; Limit = limits ?? new Limits(); VoiceEnabled = voice;
        HostDataStamp = PresenceWire.ValidStamp(dataStamp) ? dataStamp : "";
        HostDataLabel = HostDataStamp == "" ? "" : System.Text.Encoding.UTF8.GetString(PresenceWire.LabelBytes(dataLabel));
    }

    /// <summary>The host's own data stamp ("" = none: players' game data is not compared) and its label (the install's patch).</summary>
    public string HostDataStamp { get; }
    public string HostDataLabel { get; }
    /// <summary>DATA messages taken; how many of them differed from the host's stamp.</summary>
    public long DataIn => Interlocked.Read(ref _dataIn);
    public long DataDiffers => Interlocked.Read(ref _dataDiffers);

    public enum DataState
    {
        /// <summary>No DATA from this client: an older Loki, or it is still reading its maps.</summary>
        NoStamp,
        Same,
        Differs,
    }

    /// <summary>One connected player, for Loki Host's window.</summary>
    public sealed record Player(uint Aid, string Name, string? Map, byte Version, string Address, DataState Data, string DataStamp, string DataLabel);

    /// <summary>Every client that said HELLO, with what is known of its game data.</summary>
    public List<Player> Players()
    {
        lock (_lock)
            return _peers.Where(p => p.Aid != 0).Select(p => new Player(p.Aid, p.Name, p.Map, p.Version, p.Address.ToString(),
                p.DataStamp == "" || HostDataStamp == "" ? DataState.NoStamp : p.DataStamp == HostDataStamp ? DataState.Same : DataState.Differs,
                p.DataStamp, p.DataLabel)).ToList();
    }

    /// <summary>Forward voice between version-2 clients (off: they are told so in WELCOME, their VOICE frames are dropped).</summary>
    public bool VoiceEnabled { get; }
    public bool Running => _listener != null;
    public long Forwarded => Interlocked.Read(ref _forwarded);
    public long Dropped => Interlocked.Read(ref _dropped);
    /// <summary>Voice frames accepted from speakers; copies queued to listeners; frames dropped (over the rate, or voice off).</summary>
    public long VoiceIn => Interlocked.Read(ref _voiceIn);
    public long VoiceForwarded => Interlocked.Read(ref _voiceForwarded);
    public long VoiceDropped => Interlocked.Read(ref _voiceDropped);
    /// <summary>Frames (poses or voice) pushed out of a slow receiver's full queue.</summary>
    public long QueueDropped => Interlocked.Read(ref _queueDropped);

    // ---- client wave 18 (w18share), the design's point 33: the casting menus friends see (Loki.Net.GestureWire: the frames, the version rule)
    /// <summary>Forward casting menus (WELCOME 2 says so: GestureWire.CapGesture). Off (a relay made without it: the tests of older
    /// waves): the bit is not set and no client sends any. Loki Host and the standalone relay switch it on.</summary>
    public bool GesturesEnabled { get; set; }
    /// <summary>Where players' relations are read (Loki Host: its rAthena database). Null: a menu shown to "Everyone" is forwarded, one
    /// shown only to friends, family or guild is forwarded to nobody (the relay cannot tell who they are).</summary>
    public IRelationsSource? Relations { get; set; }
    long _gesturesIn, _gesturesForwarded, _gesturesWithheld, _gesturesDropped;
    /// <summary>GESTURE messages taken; copies sent to viewers; copies NOT sent to a viewer on the sender's map whom its choice leaves out
    /// (or whose relations are not known yet); messages dropped (over the rate, no map yet, malformed).</summary>
    public long GesturesIn => Interlocked.Read(ref _gesturesIn);
    public long GesturesForwarded => Interlocked.Read(ref _gesturesForwarded);
    public long GesturesWithheld => Interlocked.Read(ref _gesturesWithheld);
    public long GesturesDropped => Interlocked.Read(ref _gesturesDropped);
    public string GestureStatsLine() => $"menus: in {GesturesIn}, forwarded {GesturesForwarded}, withheld {GesturesWithheld}, dropped {GesturesDropped}";
    // ---- client wave 20 (w20pen), the design's point 57: the air pen's drawings other players see (Loki.Net.DrawWire: the frames, the version rule)
    /// <summary>Forward drawings (WELCOME 2 says so: DrawWire.CapDraw). Off (a relay made without it: the tests of older waves): the bit is not
    /// set and no client sends any. Loki Host and the standalone relay switch it on.</summary>
    public bool DrawingsEnabled { get; set; }
    long _drawsIn, _drawsForwarded, _drawsWithheld, _drawsDropped, _drawsRevoked;
    /// <summary>DRAW messages taken; copies sent to viewers; copies NOT sent to a viewer on the sender's map whom its "Drawings seen by" leaves
    /// out (or whose relations are not known yet); messages dropped (over the rate, no map yet, malformed).</summary>
    public long DrawsIn => Interlocked.Read(ref _drawsIn);
    public long DrawsForwarded => Interlocked.Read(ref _drawsForwarded);
    public long DrawsWithheld => Interlocked.Read(ref _drawsWithheld);
    public long DrawsDropped => Interlocked.Read(ref _drawsDropped);
    /// <summary>Viewers told by the relay itself to drop a player's drawings (they had been sent strokes and are left out now).</summary>
    public long DrawsRevoked => Interlocked.Read(ref _drawsRevoked);
    public string DrawStatsLine() => $"drawings: in {DrawsIn}, forwarded {DrawsForwarded}, withheld {DrawsWithheld}, dropped {DrawsDropped}, revoked {DrawsRevoked}";
    // ---- free movement (wfree), design note 2026-10-14: the players' exact spots (Loki.Net.SpotWire: the frames, the version rule)
    /// <summary>Forward exact spots (WELCOME 2 says so: SpotWire.CapSpot). Off (a relay made without it: the tests of older waves): the bit is
    /// not set and no client sends any. Loki Host and the standalone relay switch it on.</summary>
    public bool SpotsEnabled { get; set; }
    long _spotsIn, _spotsForwarded, _spotsFar, _spotsDropped;
    /// <summary>SPOT messages taken; copies sent to viewers; copies NOT sent to a SPOT_READY viewer on the same map whose own spot is beyond
    /// <see cref="Limits.SpotRange"/> (or not known yet); messages dropped (over the rate, malformed).</summary>
    public long SpotsIn => Interlocked.Read(ref _spotsIn);
    public long SpotsForwarded => Interlocked.Read(ref _spotsForwarded);
    public long SpotsFar => Interlocked.Read(ref _spotsFar);
    public long SpotsDropped => Interlocked.Read(ref _spotsDropped);
    public string SpotStatsLine() => $"spots: in {SpotsIn}, forwarded {SpotsForwarded}, out of range {SpotsFar}, dropped {SpotsDropped}";
    /// <summary>The counters in one line (the standalone relay's --stats, the smokes).</summary>
    public string StatsLine() => $"stats: voice in {VoiceIn}, voice forwarded {VoiceForwarded}, voice dropped {VoiceDropped}, poses forwarded {Forwarded}, poses dropped {Dropped}, queue dropped {QueueDropped}";

    /// <summary>A player's game data in words (Loki Host's window, the standalone relay's log).</summary>
    public static string DataText(Player p) => p.Data switch
    {
        DataState.Same => "the host's game data" + (p.DataLabel != "" ? $" ({p.DataLabel})" : ""),
        DataState.Differs => $"OTHER game data than the host ({p.DataStamp}{(p.DataLabel != "" ? ", " + p.DataLabel : "")})",
        _ => p.DataStamp != "" ? $"game data {p.DataStamp}{(p.DataLabel != "" ? ", " + p.DataLabel : "")} (not compared: no host stamp)"
            : "no data stamp (an older Loki, or still reading its maps)",
    };

    /// <summary>Every connected player with its game data, in one line (the standalone relay's --stats; the smokes).</summary>
    public string PlayersLine()
    {
        var l = Players().OrderBy(p => p.Aid).ToList();
        return "players: " + (l.Count == 0 ? "none" : string.Join("; ", l.Select(p => $"{p.Name} (AID {p.Aid}) {DataText(p)}")));
    }

    /// <summary>(AID, name, map or null) of every client that said HELLO.</summary>
    public List<(uint aid, string name, string? map)> Snapshot()
    {
        lock (_lock) return _peers.Where(p => p.Aid != 0).Select(p => (p.Aid, p.Name, p.Map)).ToList();
    }

    /// <summary>Listens on every interface (friends on the LAN and the internet), or on <paramref name="bind"/>. Port 0 picks a free one.</summary>
    public void Start(IPAddress? bind = null)
    {
        if (_listener != null) return;
        var l = new TcpListener(bind ?? IPAddress.Any, Port);
        l.Start();
        Port = ((IPEndPoint)l.LocalEndpoint).Port;
        _listener = l;
        _cts = new CancellationTokenSource();
        // On the thread pool, whatever thread calls Start: Loki Host's window starts the relay from its UI thread, and an accept
        // loop begun there would run every read, write and timer of every client on that one thread, behind whatever the window is
        // doing (found by client wave 7's smoke in a scratch Loki Host: a client's first HELLO was answered 3 s late, after its
        // WELCOME timeout, while the window was busy; the standalone relay, which has no such thread, answered at once).
        var token = _cts.Token;
        _acceptLoop = Task.Run(() => AcceptLoop(l, token));
        Say($"headset relay listening on port {Port} (voice {(VoiceEnabled ? "on" : "off")})");
        Say(HostDataStamp != "" ? $"players' game data is compared with the host's ({HostDataStamp}{(HostDataLabel != "" ? ", " + HostDataLabel : "")})"
                                : "players' game data is not compared (no host data stamp)");
    }

    public async Task StopAsync()
    {
        var l = _listener;
        if (l == null) return;
        _listener = null;
        _cts!.Cancel();
        l.Stop();
        List<Peer> all;
        lock (_lock) all = _peers.ToList();
        foreach (var p in all) p.Close("relay stopped");
        try { await _acceptLoop!; } catch (Exception) { }
        foreach (var p in all) { try { await p.Done; } catch (Exception) { } }
        Say($"headset relay stopped ({Forwarded} poses forwarded, {Dropped} dropped; voice: {VoiceIn} frames in, {VoiceForwarded} forwarded, {VoiceDropped} dropped; {QueueDropped} dropped from full queues)");
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    void Say(string line) => Log?.Invoke(line);

    async Task AcceptLoop(TcpListener l, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await l.AcceptTcpClientAsync(ct); }
            catch (Exception) { break; }
            var addr = (c.Client.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None;
            string? refuse = null;
            lock (_lock)
            {
                if (_peers.Count >= Limit.MaxClients) refuse = $"{Limit.MaxClients} clients already";
                else if (_peers.Count(p => p.Address.Equals(addr)) >= Limit.MaxPerAddress) refuse = $"{Limit.MaxPerAddress} clients from {addr} already";
            }
            if (refuse != null) { Say($"refused a connection from {addr}: {refuse}"); c.Dispose(); continue; }
            c.NoDelay = true;
            var peer = new Peer(this, c, addr);
            lock (_lock) _peers.Add(peer);
            peer.Run(ct);
        }
    }

    /// <summary>Queues a frame to every client on <paramref name="map"/> except <paramref name="except"/> (under _lock).</summary>
    void BroadcastLocked(string map, Peer except, byte[] frame)
    {
        foreach (var p in _peers)
            if (p != except && p.Aid != 0 && p.Map == map && p.Enqueue(frame)) Interlocked.Increment(ref _forwarded);
    }

    void OnHello(Peer p, uint aid, byte[] nameUtf8, string name, byte version)
    {
        Peer? old;
        lock (_lock)
        {
            // one connection per account: a client that reconnects (its old socket not yet timed out) replaces the old one, which
            // leaves the registry at once (its clean-up, OnClosed, still tells its map's players GONE)
            old = _peers.FirstOrDefault(x => x != p && x.Aid == aid);
            if (old != null) { _peers.Remove(old); old.Close($"replaced by a new connection from {p.Address}"); }
            p.Aid = aid; p.NameUtf8 = nameUtf8; p.Name = name; p.Version = version;
        }
        // a version-1 client gets the WELCOME it always got; a version-2 client learns whether voice is relayed here
        p.Enqueue(PresenceWire.EncodeWelcome(version, (byte)((VoiceEnabled ? PresenceWire.CapVoice : 0) | (HostDataStamp != "" ? PresenceWire.CapData : 0) | (Horde != null ? HordeWire.CapHorde : 0)
            | (AccountSettings != null ? PresenceWire.CapSettings : 0) // client wave 18 (w18acct)
            | (GesturesEnabled ? GestureWire.CapGesture : 0) // client wave 18 (w18share); both bits OR-ed at the wave 18 merge
            | (DrawingsEnabled ? DrawWire.CapDraw : 0) // client wave 20 (w20pen)
            | (SpotsEnabled ? SpotWire.CapSpot : 0)))); // free movement (wfree)
        Say($"{name} (AID {aid}) connected from {p.Address}{(version >= PresenceWire.VersionVoice ? " (voice)" : "")}");
        if ((GesturesEnabled || DrawingsEnabled) && version >= PresenceWire.VersionVoice) RefreshRelations(p); // client wave 18 (w18share): who this player is to the others (client wave 20, w20pen: for the drawings too)
    }

    /// <summary>A validated DATA message: the player's data stamp is kept, compared with the host's and answered (client wave 7).</summary>
    void OnData(Peer p, string stamp, string label)
    {
        lock (_lock)
        {
            if (p.Closing) return;
            p.DataStamp = stamp; p.DataLabel = label;
        }
        Interlocked.Increment(ref _dataIn);
        if (HostDataStamp == "") return; // (a client that sends DATA unasked: kept for the list, nothing to compare with)
        bool same = stamp == HostDataStamp;
        if (!same) Interlocked.Increment(ref _dataDiffers);
        var answer = PresenceWire.EncodeDataHost(same ? PresenceWire.DataSame : PresenceWire.DataDiffers, HostDataStamp, HostDataLabel);
        if (answer != null) p.Enqueue(answer);
        string theirs = stamp + (label != "" ? ", " + label : ""), ours = HostDataStamp + (HostDataLabel != "" ? ", " + HostDataLabel : "");
        Say(same ? $"{p.Name} (AID {p.Aid}) has the host's game data ({theirs})"
                 : $"{p.Name} (AID {p.Aid}) has OTHER game data than the host ({theirs}; the host's: {ours}): walking may not match on some maps; they were told");
    }

    /// <summary>A validated VOICE payload of a version-2 client: to every other version-2 client, whatever its map.</summary>
    void OnVoice(Peer p, byte[] payload)
    {
        if (!VoiceEnabled) { Interlocked.Increment(ref _voiceDropped); return; }
        lock (_lock)
        {
            if (p.Closing) return;
            if (!p.Talking) { p.Talking = true; Say($"{p.Name} (AID {p.Aid}) shares voice"); }
            Interlocked.Increment(ref _voiceIn);
            byte[]? frame = null;
            foreach (var x in _peers)
            {
                if (x == p || x.Aid == 0 || x.Version < PresenceWire.VersionVoice) continue;
                frame ??= PresenceWire.EncodeVoiceOf(p.Aid, p.NameUtf8, payload, 0, payload.Length);
                if (x.Enqueue(frame)) Interlocked.Increment(ref _voiceForwarded);
            }
        }
    }

    /// <summary>The speaker switched its voice off (VOICE_STOP) or its connection ended: the listeners free its stream (under _lock).</summary>
    void EndVoiceLocked(Peer p, string? why)
    {
        if (!p.Talking) return;
        p.Talking = false;
        // a replaced connection's AID lives on in its successor: no VOICE_END while that one talks
        if (_peers.Any(x => x != p && x.Aid == p.Aid && x.Talking)) return;
        var frame = PresenceWire.EncodeVoiceEnd(p.Aid);
        foreach (var x in _peers)
            if (x != p && x.Aid != 0 && x.Aid != p.Aid && x.Version >= PresenceWire.VersionVoice) x.Enqueue(frame);
        if (why != null) Say($"{p.Name} (AID {p.Aid}) stopped sharing voice ({why})");
    }

    void OnVoiceStop(Peer p)
    {
        lock (_lock) { if (!p.Closing) EndVoiceLocked(p, "switched off"); }
    }

    // ---- client wave 18 (w18share), the design's point 33 and its addition ("can be toggled for privacy to only be visible to friends,
    // family, or clan members"; "And/or"): a player's open casting menus go to the players on its map who may see them. The relay is
    // the filter: a viewer the sender's choice leaves out is never sent the frame (its own client could not be trusted to look away).
    // The sender's choice rides in every GESTURE (its audience byte: GestureWire.TryReadHeader); who is who comes from rAthena's own
    // tables through Relations (RelationsDb: friends, spouse / parents / child, guild: design note, "I meant my Guild"), read at HELLO and again when older than
    // Limits.RelationsRefresh at the next menu that needs them (off the read loop; until known, nothing restricted is sent to or from
    // that player). Identity is the HELLO's account id and character name, as everywhere on this relay (README "Headsets": a friends'
    // server, no authentication; a client claiming another account takes that account's connection over and is seen doing so).
    // Client wave 18 merge (the orchestrator): a RESTRICTED menu (friends / family / guild) travels only between connections whose
    // account the relay has VERIFIED with rAthena's web token of that login (w18acct's SETTINGS_AUTH proof, checked by the settings
    // store as rA web/auth.cpp does: Peer.SettingsOk): a client that only CLAIMS an account in its HELLO is sent "Everyone" menus only,
    // and its own restricted menus go to nobody. A relay without a settings store (AccountSettings null) verifies nobody: its
    // restricted menus go to nobody.

    /// <summary>A GESTURE_READY: this connection draws friends' menus (only such connections are sent GESTURE_OF).</summary>
    void OnGestureReady(Peer p)
    {
        bool first;
        lock (_lock) { if (p.Closing) return; first = !p.GestureReady; p.GestureReady = true; }
        if (first) Say($"{p.Name} (AID {p.Aid}) draws friends' casting menus");
    }

    /// <summary>A GESTURE whose header was read: to every READY player on the sender's map whom its audience allows.</summary>
    void OnGesture(Peer p, byte[] payload, byte audience)
    {
        Interlocked.Increment(ref _gesturesIn);
        lock (_lock)
        {
            if (p.Closing) return;
            if (p.Map == null) { Interlocked.Increment(ref _gesturesDropped); return; } // (no pose yet: nobody knows where it is)
            byte[]? frame = null;
            foreach (var x in _peers)
            {
                if (x == p || x.Aid == 0 || x.Aid == p.Aid || !x.GestureReady || x.Map != p.Map || x.Closing) continue;
                if (!AllowedLocked(p, x, audience)) { Interlocked.Increment(ref _gesturesWithheld); continue; }
                frame ??= GestureWire.EncodeGestureOf(p.Aid, payload, 0, payload.Length);
                if (x.Enqueue(frame)) Interlocked.Increment(ref _gesturesForwarded);
            }
        }
    }

    /// <summary>May <paramref name="viewer"/> see a menu of <paramref name="sender"/> shown to <paramref name="audience"/>? Everyone: yes.
    /// Otherwise any of the switched-on relations (friends, family, guild) between their characters: both must be known (fail closed).
    /// Old relations are read again in the background (under _lock: nothing waits here). Client wave 18 merge: restricted, both the
    /// sender's and the viewer's accounts must be verified by their login's token (<see cref="Peer.SettingsOk"/>).</summary>
    bool AllowedLocked(Peer sender, Peer viewer, byte audience)
    {
        if ((audience & GestureWire.ToEveryone) != 0) return true;
        if (Relations == null) return false;
        if (!sender.SettingsOk || !viewer.SettingsOk) return false; // (client wave 18 merge: an account claimed, not proved: Everyone only)
        long now = Environment.TickCount64, refresh = (long)Limit.RelationsRefresh.TotalMilliseconds;
        if (now - sender.RelAt > refresh) RefreshRelations(sender); // (RelAt 0: never read; a read under way is not started twice)
        if (now - viewer.RelAt > refresh) RefreshRelations(viewer);
        var a = sender.Rel; var b = viewer.Rel;
        if (a == null || b == null) return false;
        return Allowed(a, b, audience);
    }

    /// <summary>The rule itself: any switched-on relation between the two characters (or Everyone).</summary>
    public static bool Allowed(CharRelations sender, CharRelations viewer, byte audience) =>
        (audience & GestureWire.ToEveryone) != 0
        || (audience & GestureWire.ToFriends) != 0 && sender.FriendOf(viewer)
        || (audience & GestureWire.ToFamily) != 0 && sender.FamilyOf(viewer)
        || (audience & GestureWire.ToGuild) != 0 && sender.GuildOf(viewer);

    // ---- client wave 20 (w20pen), the design's point 57 and the design notes of 2026-10-10 ("Drawings seen by", default Friends only; "Drawings I see",
    // default Friends only): a player's drawings go to the players on its map whom its "Drawings seen by" allows, by the casting menus' rule
    // (AllowedLocked: the sender's audience byte, rAthena's relations, both accounts proved by their login's token for anything but Everyone).
    // The relay keeps no drawing (only, per sender, which accounts were sent its strokes: one left out later is told once to drop them): the
    // sender's client sends its drawings again when a new player comes onto its map and every 20 s
    // (UI/PenDrawings.cs). Each copy carries what the relay knows of the sender to that viewer (friend / family / guild: RelationBitsLocked),
    // with which the viewer's client applies its own "Drawings I see" (fail closed: 0 when not known or not proved).

    /// <summary>A DRAW_READY: this connection draws other players' drawings (only such connections are sent DRAW_OF).</summary>
    void OnDrawReady(Peer p)
    {
        bool first;
        lock (_lock) { if (p.Closing) return; first = !p.DrawReady; p.DrawReady = true; }
        if (first) Say($"{p.Name} (AID {p.Aid}) draws other players' drawings");
    }

    /// <summary>A DRAW whose header was read: to every READY player on the sender's map whom its audience allows, each copy with the relation
    /// of the sender to that viewer.</summary>
    void OnDraw(Peer p, byte[] payload, byte audience, byte op)
    {
        Interlocked.Increment(ref _drawsIn);
        lock (_lock)
        {
            if (p.Closing) return;
            if (p.Map == null) { Interlocked.Increment(ref _drawsDropped); return; } // (no pose yet: nobody knows where it is)
            foreach (var x in _peers)
            {
                if (x == p || x.Aid == 0 || x.Aid == p.Aid || !x.DrawReady || x.Map != p.Map || x.Closing) continue;
                if (!AllowedLocked(p, x, audience))
                {
                    Interlocked.Increment(ref _drawsWithheld);
                    // a viewer who was sent strokes of this player and is left out now (a friendship ended, the choice narrowed without the
                    // client's own clear) is told once to drop them: nobody keeps on their table what they may no longer see
                    if (p.DrawViewers.Remove(x.Aid) && x.Enqueue(DrawWire.EncodeDrawOf(p.Aid, 0, RevokePayload, 0, RevokePayload.Length))) Interlocked.Increment(ref _drawsRevoked);
                    continue;
                }
                if (!x.Enqueue(DrawWire.EncodeDrawOf(p.Aid, RelationBitsLocked(p, x), payload, 0, payload.Length))) continue;
                Interlocked.Increment(ref _drawsForwarded);
                if (op == DrawWire.OpStroke) { if (p.DrawViewers.Count >= Limit.MaxClients * 4) p.DrawViewers.Clear(); p.DrawViewers.Add(x.Aid); }
                else if (op == DrawWire.OpClear) p.DrawViewers.Remove(x.Aid);
            }
        }
    }

    // ---- free movement (wfree), design note 2026-10-14 ("exact sub-tile position sync across players"): rAthena keeps every
    // character on a cell; a Loki client draws its own where the player stopped (up to half a cell from that cell) and says where in a SPOT.
    // The relay forwards it to the SPOT_READY players on the SPOT's own map (a desktop player sends no pose, so the pose's map is not used)
    // whose own last spot is within Limits.SpotRange: a client is told only about players the server shows it anyway (rAthena's view is
    // area_size 14 cells), never where a far player stands. Nothing is kept but each connection's last spot.

    /// <summary>A SPOT_READY: this connection draws other players at their spots (only such connections are sent SPOT_OF).</summary>
    void OnSpotReady(Peer p)
    {
        bool first;
        lock (_lock) { if (p.Closing) return; first = !p.SpotReady; p.SpotReady = true; }
        if (first) Say($"{p.Name} (AID {p.Aid}) draws other players at their exact spots");
    }

    /// <summary>A validated SPOT: kept as this connection's last spot, then to every SPOT_READY player on that map within range.</summary>
    void OnSpot(Peer p, byte[] payload, in SpotWire.State s)
    {
        Interlocked.Increment(ref _spotsIn);
        lock (_lock)
        {
            if (p.Closing) return;
            if (p.SpotMap != s.Map) { p.SpotMap = s.Map; Say($"{p.Name} (AID {p.Aid}) sends its exact spot on {s.Map}"); }
            p.SpotX = s.X; p.SpotY = s.Y;
            byte[]? frame = null;
            foreach (var x in _peers)
            {
                if (x == p || x.Aid == 0 || x.Aid == p.Aid || !x.SpotReady || x.Closing || x.SpotMap != s.Map) continue;
                if (SpotWire.Distance(x.SpotX, x.SpotY, s.X, s.Y) > Limit.SpotRange) { Interlocked.Increment(ref _spotsFar); continue; }
                frame ??= SpotWire.EncodeSpotOf(p.Aid, payload, 0, payload.Length);
                if (x.Enqueue(frame)) Interlocked.Increment(ref _spotsForwarded);
            }
        }
    }

    /// <summary>The relay's own "drop this player's drawings" (a DRAW payload: CLEAR), sent as that player's DRAW_OF with no relation.</summary>
    static readonly byte[] RevokePayload = { DrawWire.Draw, DrawWire.Format, GestureWire.ToEveryone, DrawWire.OpClear };

    /// <summary>What <paramref name="sender"/> is to <paramref name="viewer"/> (friend / family / guild bits, GestureWire's), for the viewer's own
    /// filter: only when both accounts are proved and both relations known (else 0: fail closed); old relations are read again in the
    /// background (under _lock).</summary>
    byte RelationBitsLocked(Peer sender, Peer viewer)
    {
        if (Relations == null || !sender.SettingsOk || !viewer.SettingsOk) return 0;
        long now = Environment.TickCount64, refresh = (long)Limit.RelationsRefresh.TotalMilliseconds;
        if (now - sender.RelAt > refresh) RefreshRelations(sender);
        if (now - viewer.RelAt > refresh) RefreshRelations(viewer);
        return RelationBits(sender.Rel, viewer.Rel);
    }

    /// <summary>The relation bits between two characters (0 when either is not known).</summary>
    public static byte RelationBits(CharRelations? sender, CharRelations? viewer) =>
        sender == null || viewer == null ? (byte)0 : (byte)((sender.FriendOf(viewer) ? GestureWire.ToFriends : 0) | (sender.FamilyOf(viewer) ? GestureWire.ToFamily : 0) | (sender.GuildOf(viewer) ? GestureWire.ToGuild : 0));

    /// <summary>Reads a player's relations again, in the background, once at a time (nothing without a source).</summary>
    void RefreshRelations(Peer p)
    {
        var src = Relations;
        if (src == null || p.Aid == 0 || Interlocked.Exchange(ref p.RelLoading, 1) == 1) return;
        uint aid = p.Aid; string name = p.Name;
        _ = Task.Run(async () =>
        {
            try
            {
                var r = await src.LoadAsync(aid, name);
                bool first; lock (_lock) { first = p.RelAt == 0; p.Rel = r; p.RelAt = Environment.TickCount64; }
                if (first) Say(r == null ? $"{name} (AID {aid}): no character of that name on the account: casting menus shown to friends, family or guild only are not sent to or from them"
                                         : $"{name} (AID {aid}) is character {r.CharId}: {r.Friends.Count} on friend lists, spouse {(r.Partner != 0 ? r.Partner.ToString() : "none")}, parents {(r.Father != 0 || r.Mother != 0 ? $"{r.Father} / {r.Mother}" : "none")}, child {(r.Child != 0 ? r.Child.ToString() : "none")}, guild {(r.Guild != 0 ? r.Guild.ToString() : "none")}");
            }
            catch (Exception e) { lock (_lock) p.RelAt = Environment.TickCount64; Say($"{name} (AID {aid}): relations not read ({e.Message}): its restricted casting menus wait"); }
            finally { Interlocked.Exchange(ref p.RelLoading, 0); }
        });
    }

    void OnPose(Peer p, byte[] payload, PresenceWire.PoseBody body)
    {
        lock (_lock)
        {
            if (p.Closing) return; // a message read after Close (a replaced connection) is not forwarded
            if (p.Map != body.Map)
            {
                if (p.Map != null) BroadcastLocked(p.Map, p, PresenceWire.EncodeGone(p.Aid));
                p.DrawViewers.Clear(); // client wave 20 (w20pen): the old map's players drop this player's drawings at GONE
                p.Map = body.Map;
                Say($"{p.Name} (AID {p.Aid}) is on {body.Map}");
            }
            BroadcastLocked(body.Map, p, PresenceWire.EncodePoseOf(p.Aid, p.NameUtf8, payload, 0, payload.Length));
        }
    }

    void OnLeave(Peer p)
    {
        lock (_lock)
        {
            if (p.Map == null || p.Closing) return;
            BroadcastLocked(p.Map, p, PresenceWire.EncodeGone(p.Aid));
            Say($"{p.Name} (AID {p.Aid}) left {p.Map}");
            p.DrawViewers.Clear(); // client wave 20 (w20pen): GONE drops this player's drawings there
            p.Map = null;
        }
    }

    void OnClosed(Peer p, string why)
    {
        bool hordeGone = false;
        lock (_lock)
        {
            _peers.Remove(p);
            // Horde: the account is gone unless a newer connection of it (a replaced one) said H_HELLO already
            hordeGone = p.HordeHello && p.Aid != 0 && !_peers.Any(x => x.Aid == p.Aid && x.HordeHello);
            EndVoiceLocked(p, null);
            // a replaced connection's AID lives on in its successor: its map's players get GONE only if the successor is elsewhere
            if (p.Map != null && p.Aid != 0 && !_peers.Any(x => x.Aid == p.Aid && x.Map == p.Map)) BroadcastLocked(p.Map, p, PresenceWire.EncodeGone(p.Aid));
        }
        if (hordeGone) Horde?.OnPeerGone(p.Aid);
        Say(p.Aid != 0 ? $"{p.Name} (AID {p.Aid}) disconnected: {why}" : $"a connection from {p.Address} closed before HELLO: {why}");
    }

    // ---- client wave 18 (w18acct): settings per account

    /// <summary>A validated SETTINGS_AUTH: the store checks the proof for THIS connection's AID; accepted, the account's settings
    /// (or "none yet") are sent back; refused, only that is said. Off the read loop, in order with the connection's other settings work.</summary>
    void OnSettingsAuth(Peer p, string proof)
    {
        var store = AccountSettings;
        if (store == null) return;
        uint aid = p.Aid; string name = p.Name;
        p.Serial(async () =>
        {
            byte state; long at = 0; string? json = null; string? why = null;
            try
            {
                bool ok = await store.VerifyAsync(aid, proof);
                p.SettingsOk = ok;
                if (!ok) state = PresenceWire.SettingsRefused;
                else
                {
                    var row = await store.LoadAsync(aid);
                    if (row == null) state = PresenceWire.SettingsNone;
                    else { state = PresenceWire.SettingsFound; (at, json) = row.Value; }
                }
            }
            catch (Exception e) { state = PresenceWire.SettingsFailed; why = e.Message; }
            var frame = PresenceWire.EncodeSettingsOf(state, at, json);
            if (frame == null) { why = $"its stored settings cannot be sent ({json?.Length ?? 0} characters)"; state = PresenceWire.SettingsFailed; frame = PresenceWire.EncodeSettingsOf(state, 0, null)!; }
            if (p.Closing) return;
            if (state == PresenceWire.SettingsFound) Interlocked.Increment(ref _settingsLoads);
            if (state == PresenceWire.SettingsRefused) Interlocked.Increment(ref _settingsRefused);
            p.Enqueue(frame);
            Say(state switch
            {
                PresenceWire.SettingsFound => $"{name} (AID {aid}): settings sent ({json!.Length} characters, saved {Stamp(at)})",
                PresenceWire.SettingsNone => $"{name} (AID {aid}): no settings stored yet for this account",
                PresenceWire.SettingsRefused => $"{name} (AID {aid}): settings REFUSED: the proof is not this account's login (an older login, or another account's client)",
                _ => $"{name} (AID {aid}): settings could not be read: {why}",
            });
        });
    }

    /// <summary>A validated SETTINGS_PUT: stored for this connection's AID when its proof was accepted, unless a newer save is stored.</summary>
    void OnSettingsPut(Peer p, long savedAt, string json)
    {
        var store = AccountSettings;
        if (store == null) return;
        uint aid = p.Aid; string name = p.Name;
        p.Serial(async () =>
        {
            byte state; long held = 0; string? why = null;
            if (!p.SettingsOk) state = PresenceWire.SettingsRefused;
            else
            {
                try { var (stored, h) = await store.SaveAsync(aid, savedAt, json); state = stored ? PresenceWire.SettingsStored : PresenceWire.SettingsOlder; held = h; }
                catch (Exception e) { state = PresenceWire.SettingsFailed; why = e.Message; }
            }
            if (p.Closing) return;
            if (state == PresenceWire.SettingsStored) Interlocked.Increment(ref _settingsSaves);
            p.Enqueue(PresenceWire.EncodeSettingsAck(state, held)!);
            Say(state switch
            {
                PresenceWire.SettingsStored => $"{name} (AID {aid}): settings stored ({json.Length} characters, saved {Stamp(savedAt)})",
                PresenceWire.SettingsOlder => $"{name} (AID {aid}): settings NOT stored: the host holds a newer save ({Stamp(held)} after this one's {Stamp(savedAt)})",
                PresenceWire.SettingsRefused => $"{name} (AID {aid}): settings NOT stored: no accepted proof on this connection",
                _ => $"{name} (AID {aid}): settings could not be stored: {why}",
            });
        });
    }

    static string Stamp(long ms) => ms <= 0 ? "never" : DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");

    /// <summary>A Horde message of a version-2 connection (only when this relay runs Horde).</summary>
    void OnHorde(Peer p, byte[] payload)
    {
        var h = Horde;
        if (h == null || p.Closing) return;
        if (payload[0] == HordeWire.HHello)
        {
            bool first;
            lock (_lock) { first = !p.HordeHello; p.HordeHello = true; }
            if (first) Say($"{p.Name} (AID {p.Aid}) can play Horde");
        }
        else if (!p.HordeHello) return;
        h.OnMessage(p.Aid, p.Name, payload);
    }

    sealed class Peer
    {
        readonly PoseRelay _r;
        readonly TcpClient _c;
        readonly Channel<byte[]> _out;
        readonly CancellationTokenSource _cts = new();
        public IPAddress Address { get; }
        public uint Aid;
        /// <summary>The version of its HELLO: 1 = poses only (it is never sent a voice frame), 2 = voice too.</summary>
        public byte Version = PresenceWire.Version;
        /// <summary>Sent voice since its HELLO or its last VOICE_STOP (the listeners hold a stream for it).</summary>
        public bool Talking;
        public string Name = "";
        public byte[] NameUtf8 = Array.Empty<byte>();
        public string? Map;
        /// <summary>What its DATA said (client wave 7); "" until then.</summary>
        public string DataStamp = "", DataLabel = "";
        /// <summary>Client wave 18 (w18share): said GESTURE_READY (only then is it sent friends' menus); its character's relations (null:
        /// not known), when they were last read (TickCount64, 0 never), a read under way (1).</summary>
        public bool GestureReady; public CharRelations? Rel; public long RelAt; public int RelLoading;
        long _gestureWindowStart; int _gesturesInWindow;
        /// <summary>Client wave 20 (w20pen): said DRAW_READY (only then is it sent other players' drawings).</summary>
        public bool DrawReady;
        long _drawWindowStart; int _drawsInWindow;
        /// <summary>Client wave 20 (w20pen): the accounts this connection's strokes were sent to on its map (under the relay's lock): one of them
        /// left out later is told once to drop them (PoseRelay.OnDraw). Emptied when this player changes map (GONE drops them there).</summary>
        public readonly HashSet<uint> DrawViewers = new();
        /// <summary>Free movement (wfree): said SPOT_READY (only then is it sent other players' spots); its own last spot (under the relay's
        /// lock; SpotMap null until its first SPOT: it is sent nothing before, as its place is not known).</summary>
        public bool SpotReady; public string? SpotMap; public float SpotX, SpotY;
        long _spotWindowStart; int _spotsInWindow;
        int _dataMessages;
        /// <summary>Horde (client wave 8): said H_HELLO (only then is it sent Horde frames); a Horde frame of its queue was dropped.</summary>
        public bool HordeHello, HordeLost;
        long _hordeWindowStart; int _hordeInWindow;
        /// <summary>Client wave 18 (w18acct): its last SETTINGS_AUTH was accepted (only then are its settings stored, and only then
        /// does its reader take frames longer than a voice frame).</summary>
        public volatile bool SettingsOk;
        int _settingsAuths; long _settingsWindowStart; int _settingsPutsInWindow;
        Task _settingsTail = Task.CompletedTask;
        readonly object _settingsGate = new();
        /// <summary>Runs the store's work for this connection after the work queued before it (off the read loop: in order).</summary>
        public void Serial(Func<Task> work)
        {
            lock (_settingsGate) _settingsTail = _settingsTail.ContinueWith(_ => work(), TaskScheduler.Default).Unwrap();
        }
        /// <summary>The longest payload this connection's reader takes now (a version-1 client 200, a version-2 one a voice frame's
        /// 274, one whose settings proof was accepted 65,535: SETTINGS_PUT).</summary>
        int MaxIn => Version < PresenceWire.VersionVoice ? PresenceWire.MaxPayload : SettingsOk ? PresenceWire.MaxPayloadSettings : PresenceWire.MaxPayloadVoice;
        public int Queued => Volatile.Read(ref _queued);
        public Task Done { get; private set; } = Task.CompletedTask;
        string? _closeReason;
        readonly object _closeGate = new();
        /// <summary>Set by Close: nothing more of this connection is handled or forwarded.</summary>
        public volatile bool Closing;
        long _windowStart; int _inWindow;
        long _voiceWindowStart; int _voiceInWindow;
        /// <summary>Frames waiting to be written. The bound is kept here, not by the channel, because it depends on the HELLO: a
        /// version-1 client keeps its 64, a voice client gets <see cref="Limits.VoiceQueueFrames"/>; both drop the oldest first.</summary>
        int _queued;

        public Peer(PoseRelay r, TcpClient c, IPAddress addr)
        {
            _r = r; _c = c; Address = addr;
            _out = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
        }

        /// <summary>Queues a frame for this client; when its queue is at the bound, the oldest frames are dropped to make room.</summary>
        public bool Enqueue(byte[] frame)
        {
            int bound = Version >= PresenceWire.VersionVoice ? _r.Limit.VoiceQueueFrames : _r.Limit.QueueFrames;
            while (Volatile.Read(ref _queued) >= bound)
            {
                if (!_out.Reader.TryRead(out var gone)) break;
                Interlocked.Decrement(ref _queued); Interlocked.Increment(ref _r._queueDropped);
                if (gone.Length > 2 && HordeWire.IsRelayType(gone[2])) HordeLost = true; // its snapshots no longer follow: a key next
            }
            if (!_out.Writer.TryWrite(frame)) return false;
            Interlocked.Increment(ref _queued);
            return true;
        }

        public void Close(string why)
        {
            lock (_closeGate) { _closeReason ??= why; }
            Closing = true;
            try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        }

        public void Run(CancellationToken relay)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(relay, _cts.Token);
            var reader = ReadLoop(linked.Token);
            var writer = WriteLoop(linked.Token);
            Done = Task.Run(async () =>
            {
                await Task.WhenAny(reader, writer);
                Close("connection ended");
                try { await Task.WhenAll(reader, writer); } catch (Exception) { }
                _c.Dispose();
                linked.Dispose();
                string why; lock (_closeGate) why = _closeReason ?? "closed";
                _r.OnClosed(this, why);
            });
        }

        async Task ReadLoop(CancellationToken ct)
        {
            var s = _c.GetStream();
            var frames = new PresenceWire.FrameReader();
            var buf = new byte[4096];
            var body = new PresenceWire.PoseBody();
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(Aid == 0 ? _r.Limit.HelloWithin : _r.Limit.IdleAfter);
                    int n;
                    try { n = await s.ReadAsync(buf, timeout.Token); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { Close(Aid == 0 ? "no HELLO in time" : "silent too long"); return; }
                    if (n <= 0) { Close("closed by the client"); return; }
                    frames.Append(buf, 0, n);
                    while (true)
                    {
                        // a connection that said HELLO 2 may carry voice frames (up to 274 bytes), every other stays at 200; client wave
                        // 18 (w18acct): one whose settings proof was accepted may send its settings (up to 65,535). Read before EVERY
                        // frame: the proof is accepted off this loop, and the client's long SETTINGS_PUT may follow in the same read
                        frames.MaxLength = MaxIn;
                        int k = frames.Next(out var p);
                        if (k == 0) break;
                        if (k < 0) { Close("bad frame length"); return; }
                        if (!Handle(p, body)) return;
                    }
                }
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or OperationCanceledException) { Close(e.Message); }
        }

        /// <summary>One message; false closes the connection.</summary>
        bool Handle(byte[] p, PresenceWire.PoseBody body)
        {
            if (Closing) return false;
            if (Aid == 0)
            {
                if (!PresenceWire.TryDecodeHello(p, 0, p.Length, out var aid, out var nameUtf8, out var name, out var version)) { Close("the first message was not a valid HELLO"); return false; }
                _r.OnHello(this, aid, nameUtf8, name, version);
                return true;
            }
            switch (p[0])
            {
                case PresenceWire.Pose:
                    if (!PresenceWire.TryDecodePose(p, 0, p.Length, body)) { Close("invalid POSE"); return false; }
                    long now = Environment.TickCount64;
                    if (now - _windowStart >= 1000) { _windowStart = now; _inWindow = 0; }
                    if (++_inWindow > _r.Limit.MaxPosesPerSecond) { Interlocked.Increment(ref _r._dropped); return true; }
                    _r.OnPose(this, p, body);
                    return true;
                case PresenceWire.Leave:
                    if (p.Length != 1) { Close("invalid LEAVE"); return false; }
                    _r.OnLeave(this);
                    return true;
                case PresenceWire.Ping:
                    return p.Length == 1 || Fail("invalid PING");
                // voice: only on a connection that said HELLO 2 (for a version-1 connection these stay unknown messages, as before)
                case PresenceWire.Voice when Version >= PresenceWire.VersionVoice:
                {
                    if (!PresenceWire.TryReadVoice(p, 0, p.Length, out _)) return Fail("invalid VOICE");
                    long t = Environment.TickCount64;
                    if (t - _voiceWindowStart >= 1000) { _voiceWindowStart = t; _voiceInWindow = 0; }
                    if (++_voiceInWindow > _r.Limit.MaxVoicePerSecond) { Interlocked.Increment(ref _r._voiceDropped); return true; }
                    _r.OnVoice(this, p);
                    return true;
                }
                // the game-data check (client wave 7): a version-2 connection's own stamp, a few times at most
                case PresenceWire.Data when Version >= PresenceWire.VersionVoice:
                {
                    if (!PresenceWire.TryDecodeData(p, 0, p.Length, out var stamp, out var label)) return Fail("invalid DATA");
                    if (++_dataMessages > _r.Limit.MaxDataPerConnection) return true;
                    _r.OnData(this, stamp, label);
                    return true;
                }
                // client wave 18 (w18share): casting menus, a version-2 connection to a relay that forwards them (its WELCOME said so);
                // dropped (never closed) when malformed or over the rate: the headsets and the voice go on
                case GestureWire.GestureReady when Version >= PresenceWire.VersionVoice && _r.GesturesEnabled:
                    if (p.Length == 1) _r.OnGestureReady(this);
                    return true;
                case GestureWire.Gesture when Version >= PresenceWire.VersionVoice && _r.GesturesEnabled:
                {
                    if (!GestureWire.TryReadHeader(p, 0, p.Length, out var audience, out _)) { Interlocked.Increment(ref _r._gesturesDropped); return true; }
                    long t = Environment.TickCount64;
                    if (t - _gestureWindowStart >= 1000) { _gestureWindowStart = t; _gesturesInWindow = 0; }
                    if (++_gesturesInWindow > _r.Limit.MaxGesturesPerSecond) { Interlocked.Increment(ref _r._gesturesDropped); return true; }
                    _r.OnGesture(this, p, audience);
                    return true;
                }
                // client wave 20 (w20pen): drawings, a version-2 connection to a relay that forwards them (its WELCOME said so); dropped (never
                // closed) when malformed or over the rate: the headsets and the voice go on
                case DrawWire.DrawReady when Version >= PresenceWire.VersionVoice && _r.DrawingsEnabled:
                    if (p.Length == 1) _r.OnDrawReady(this);
                    return true;
                case DrawWire.Draw when Version >= PresenceWire.VersionVoice && _r.DrawingsEnabled:
                {
                    if (!DrawWire.TryReadHeader(p, 0, p.Length, out var audience, out var op)) { Interlocked.Increment(ref _r._drawsDropped); return true; }
                    long t = Environment.TickCount64;
                    if (t - _drawWindowStart >= 1000) { _drawWindowStart = t; _drawsInWindow = 0; }
                    if (++_drawsInWindow > _r.Limit.MaxDrawsPerSecond) { Interlocked.Increment(ref _r._drawsDropped); return true; }
                    _r.OnDraw(this, p, audience, op);
                    return true;
                }
                // free movement (wfree): exact spots, a version-2 connection to a relay that forwards them (its WELCOME said so); dropped
                // (never closed) when malformed or over the rate: the headsets and the voice go on
                case SpotWire.SpotReady when Version >= PresenceWire.VersionVoice && _r.SpotsEnabled:
                    if (p.Length == 1) _r.OnSpotReady(this);
                    return true;
                case SpotWire.Spot when Version >= PresenceWire.VersionVoice && _r.SpotsEnabled:
                {
                    if (!SpotWire.TryReadSpot(p, 0, p.Length, out var spot)) { Interlocked.Increment(ref _r._spotsDropped); return true; }
                    long t = Environment.TickCount64;
                    if (t - _spotWindowStart >= 1000) { _spotWindowStart = t; _spotsInWindow = 0; }
                    if (++_spotsInWindow > _r.Limit.MaxSpotsPerSecond) { Interlocked.Increment(ref _r._spotsDropped); return true; }
                    _r.OnSpot(this, p, spot);
                    return true;
                }
                // Horde (client wave 8): a version-2 connection to a relay that runs Horde; dropped (never closed) when malformed
                case >= HordeWire.ClientFirst and <= HordeWire.ClientLast when Version >= PresenceWire.VersionVoice && _r.Horde != null:
                {
                    long t = Environment.TickCount64;
                    if (t - _hordeWindowStart >= 1000) { _hordeWindowStart = t; _hordeInWindow = 0; }
                    if (++_hordeInWindow > _r.Limit.MaxHordePerSecond) return true;
                    _r.OnHorde(this, p);
                    return true;
                }
                case PresenceWire.VoiceStop when Version >= PresenceWire.VersionVoice:
                    if (p.Length != 1) return Fail("invalid VOICE_STOP");
                    _r.OnVoiceStop(this);
                    return true;
                // client wave 18 (w18acct): settings per account, a version-2 connection to a relay that keeps them
                case PresenceWire.SettingsAuth when Version >= PresenceWire.VersionVoice && _r.AccountSettings != null:
                {
                    if (!PresenceWire.TryDecodeSettingsAuth(p, 0, p.Length, out var proof)) return Fail("invalid SETTINGS_AUTH");
                    if (++_settingsAuths > _r.Limit.MaxSettingsAuthPerConnection) return true;
                    _r.OnSettingsAuth(this, proof);
                    return true;
                }
                case PresenceWire.SettingsPut when Version >= PresenceWire.VersionVoice && _r.AccountSettings != null:
                {
                    if (!PresenceWire.TryDecodeSettingsPut(p, 0, p.Length, out var savedAt, out var json)) return Fail("invalid SETTINGS_PUT");
                    long t = Environment.TickCount64;
                    if (t - _settingsWindowStart >= 60000) { _settingsWindowStart = t; _settingsPutsInWindow = 0; }
                    if (++_settingsPutsInWindow > _r.Limit.MaxSettingsPutsPerMinute) return true;
                    _r.OnSettingsPut(this, savedAt, json);
                    return true;
                }
                default:
                    return Fail($"unknown message 0x{p[0]:x2}");
            }
        }

        bool Fail(string why) { Close(why); return false; }

        async Task WriteLoop(CancellationToken ct)
        {
            var s = _c.GetStream();
            try
            {
                while (await _out.Reader.WaitToReadAsync(ct))
                    while (_out.Reader.TryRead(out var f)) { Interlocked.Decrement(ref _queued); await s.WriteAsync(f, ct); }
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or OperationCanceledException or ChannelClosedException) { }
        }
    }
}
