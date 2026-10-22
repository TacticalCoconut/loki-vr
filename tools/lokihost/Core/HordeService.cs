using System.Collections.Concurrent;
using System.Diagnostics;
using Loki.Horde;
using Loki.Horde.Net;

namespace LokiHost.Core;

/// <summary>How the relay hands Horde frames to its clients (PoseRelay implements it).</summary>
public interface IHordeTransport
{
    /// <summary>Queues a whole frame for the client of that account (one that said H_HELLO); false when there is none.</summary>
    bool SendHorde(uint aid, byte[] frame);
    /// <summary>Frames waiting for that client (a slow one is sent fewer snapshots).</summary>
    int HordeQueueDepth(uint aid);
    /// <summary>True (once) when the relay had to drop a Horde frame of that client from its full queue: it needs a key snapshot.</summary>
    bool TakeHordeLost(uint aid);
}

/// <summary>Where the host writes its reward decisions and learns what rAthena's side granted (Loki Host: HordeRewardDb).</summary>
public interface IHordeRewards
{
    /// <summary>Writes one decision (a row rAthena's NPC script grants once). Returns false when it could not be written.</summary>
    Task<bool> RecordAsync(HordeRewardRow row);
    /// <summary>Rows granted since the last call that were not reported yet (marks them reported).</summary>
    Task<List<HordeRewardRow>> TakeGrantedAsync();
}

public sealed class HordeRewardRow
{
    public long Id;
    public uint RunId;
    public uint AccountId;
    public string CharName = "";
    public int BaseShare, JobShare;
    public long BaseExp, JobExp;
}

/// <summary>The mode's settings (Loki Host's window: HostSettings.Horde*).</summary>
public sealed class HordeSettings
{
    public bool Enabled = true;
    public float RunSeconds = 900;
    public HordeRewardSize Reward = HordeRewardSize.Normal;
    /// <summary>0 = a random arena of the tables (among those offered); else that ArenaDef id.</summary>
    public int ArenaId;
    public float InviteSeconds = 30;
    /// <summary>Seconds a player whose link dropped keeps its place in the run (it stands still meanwhile).</summary>
    public float AwaySeconds = 60;
    /// <summary>The server's town maps (its "town" map flags), for the prompt's warning.</summary>
    public List<string> Towns = new();
    /// <summary>Arena ids NOT to offer (Loki Host found cells of them unwalkable in its map cache).</summary>
    public HashSet<int> ArenasRefused = new();
}

/// <summary>
/// Horde inside Loki Host's relay (client wave 8, w8core): the invitation, the answers, the run's simulation (HordeSim, the
/// client's own engine) on its own thread at HordeSim.TickRate ticks a second (40 since client wave 9; snapshots every
/// <see cref="SnapshotEvery"/> ticks), snapshots to the players, the results, and the reward
/// decisions (written down through <see cref="IHordeRewards"/>; rAthena's side grants them). Host-authoritative: a client
/// only ever sends its stick, its answers and its card choices; every number of the results and the reward is the host's.
/// </summary>
public sealed class HordeService : IDisposable
{
    public readonly HordeContent Content;
    public readonly string ContentStamp;
    public HordeSettings Settings { get; }
    public IHordeTransport? Transport { get; set; }
    public IHordeRewards? Rewards { get; set; }
    public event Action<string>? Log;
    /// <summary>Snapshots every this many ticks (2 = 20 a second at 40 ticks; client wave 9).</summary>
    public int SnapshotEvery { get; set; } = 2;
    /// <summary>A client with more frames than this waiting is skipped for a snapshot (and gets a key later).</summary>
    public int SlowQueue { get; set; } = 48;
    /// <summary>Real milliseconds per tick: 25 (40 a second). Tests run faster.</summary>
    public int TickMs { get; set; } = 1000 / HordeSim.TickRate;

    sealed class PeerState { public string Name = ""; public byte Sex; public bool Ok; public byte Protocol; public bool NeedKey = true; }
    sealed class MemberState { public string Name = ""; public HordeMemberState State; public int Slot = -1; public long AwaySinceMs = -1; }

    readonly object _gate = new();
    readonly ConcurrentQueue<(uint aid, string name, byte[]? payload)> _inbox = new();
    readonly Dictionary<uint, PeerState> _peers = new();
    readonly Dictionary<uint, MemberState> _members = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    Thread? _thread;
    volatile bool _stop;
    HordePhase _phase = HordePhase.Idle;
    uint _runId;
    uint _starterAid; string _starterName = "";
    long _deadlineMs;
    HordeSim? _sim;
    SnapshotEncoder? _enc;
    ArenaDef? _arena;
    ulong _seed;
    long _lastStatusMs, _lastRewardPollMs;
    volatile bool _rewardPollBusy;
    string _lastRunLine = "";
    // measurements (the bots' test, the window)
    public long SnapshotBytesSent, SnapshotsSent, SnapshotsSkipped, KeysSent;
    public double TickMsTotal; public long TicksRun; public double TickMsMax;

    public HordeService(HordeContent content, HordeSettings settings)
    {
        Content = content;
        ContentStamp = HordeWire.ContentStamp(content);
        Settings = settings;
    }

    void Say(string s) { try { Log?.Invoke("[horde] " + s); } catch (Exception) { } }

    public void Start()
    {
        if (_thread != null) return;
        _stop = false;
        _thread = new Thread(Loop) { IsBackground = true, Name = "loki-horde" };
        _thread.Start();
        Say($"Horde {(Settings.Enabled ? "on" : "off")}: runs of {Settings.RunSeconds / 60:0.#} min, reward {Settings.Reward}, tables {ContentStamp}, {Content.Arenas.Count - Settings.ArenasRefused.Count} arena(s), {Settings.Towns.Count} town maps known");
    }

    public void Stop()
    {
        var t = _thread;
        if (t == null) return;
        lock (_gate) { if (_sim != null && !_sim.Over) { _sim.End(HordeOutcome.Stopped); Finish(); } }
        _stop = true;
        t.Join(2000);
        _thread = null;
        // the reward rows of a run that just ended are written before the database may stop (Loki Host stops it next)
        Task[] pending; lock (_pending) pending = _pending.ToArray();
        try { Task.WaitAll(pending, TimeSpan.FromSeconds(10)); } catch (Exception) { }
    }

    readonly List<Task> _pending = new();

    public void Dispose() => Stop();

    // ------------------------------------------------------------------------------------------- from the relay (any thread)

    public void OnMessage(uint aid, string name, byte[] payload) => _inbox.Enqueue((aid, name, payload));
    public void OnPeerGone(uint aid) => _inbox.Enqueue((aid, "", null));

    // ------------------------------------------------------------------------------------------- for the window

    public sealed record PlayerLine(string Name, int Level, int JobId, bool Fallen, bool Away);
    public sealed record Info(HordePhase Phase, uint RunId, float Seconds, float RunLength, List<PlayerLine> Players, int Enemies, string LastRun, int Connected);

    public Info GetInfo()
    {
        lock (_gate)
        {
            var pl = new List<PlayerLine>();
            if (_sim != null) foreach (var p in _sim.Players) if (p.Present) pl.Add(new PlayerLine(p.Name, p.Level, p.JobId, p.Fallen, p.Away));
            if (_phase == HordePhase.Gathering) foreach (var m in _members.Values) if (m.State == HordeMemberState.Joined) pl.Add(new PlayerLine(m.Name, 1, 0, false, false));
            return new Info(_phase, _runId, _sim != null ? _sim.Tick / (float)HordeSim.TickRate : 0, Settings.RunSeconds, pl, _sim?.EnemiesAlive ?? 0, _lastRunLine, _peers.Count);
        }
    }

    // ------------------------------------------------------------------------------------------- the loop

    void Loop()
    {
        long next = _clock.ElapsedMilliseconds;
        int tickInRun = 0;
        while (!_stop)
        {
            long now = _clock.ElapsedMilliseconds;
            if (now < next) { Thread.Sleep((int)Math.Min(20, next - now)); continue; }
            next += Math.Max(1, TickMs);
            if (now - next > 500) next = now; // far behind (the PC slept): do not run a burst of ticks
            try
            {
                lock (_gate)
                {
                    Drain(now);
                    if (_phase == HordePhase.Gathering && (now >= _deadlineMs || EveryoneAnswered())) BeginRun();
                    if (_phase == HordePhase.Running && _sim != null)
                    {
                        long t0 = Stopwatch.GetTimestamp();
                        _sim.Step();
                        double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                        TickMsTotal += ms; TicksRun++; if (ms > TickMsMax) TickMsMax = ms;
                        _enc!.Collect();
                        foreach (var ev in _sim.Events) { var ol = HordeSim.ObjectiveLogLine(ev, _sim.Tick); if (ol != null) Say($"run {_runId}: {ol}"); } // client wave 17 (h17obj): the map objectives in the host's log
                        foreach (var ev in _sim.Events) { var sl = HordeSim.SoulLogLine(ev, _sim.Tick); if (sl != null) Say($"run {_runId}: {sl}"); } // client wave 18 (h18feel): soul retrieval in the host's log (point 35)
                        foreach (var kv in _sim.OffersOpened) { var aid = AidOfSlot(kv.Key); if (aid != 0) Send(aid, HordeWire.EncodeOffer(kv.Value)); }
                        foreach (var kv in _sim.OffersClosed) { var aid = AidOfSlot(kv.Key); if (aid != 0 && kv.Value != 0) Send(aid, HordeWire.EncodeOfferClosed(kv.Value)); }
                        _sim.ClearOutputs();
                        CheckAway(now);
                        if (++tickInRun >= SnapshotEvery || _sim.Over) { tickInRun = 0; Snapshot(); }
                        if (_sim.Over) Finish();
                    }
                    if (now - _lastStatusMs >= 1000 && _phase != HordePhase.Idle) BroadcastStatus();
                }
                if (Rewards != null && !_rewardPollBusy && now - _lastRewardPollMs >= 2000) { _lastRewardPollMs = now; PollRewards(); }
            }
            catch (Exception e) { Say("ERROR in the Horde loop: " + e); }
        }
    }

    void Send(uint aid, byte[] frame) { try { Transport?.SendHorde(aid, frame); } catch (Exception) { } }

    uint AidOfSlot(int slot) { foreach (var kv in _members) if (kv.Value.Slot == slot && kv.Value.State == HordeMemberState.Joined) return kv.Key; return 0; }

    HordeStatus BuildStatus()
    {
        var s = new HordeStatus
        {
            Enabled = Settings.Enabled, Phase = _phase, RunId = _runId, StarterAid = _starterAid, StarterName = _starterName,
            SecondsLeft = _phase == HordePhase.Gathering ? Math.Max(0, (_deadlineMs - _clock.ElapsedMilliseconds) / 1000f) : 0,
            RunSeconds = _sim != null ? _sim.Tick / (float)HordeSim.TickRate : 0, RunLength = Settings.RunSeconds, ArenaMap = _arena?.Map ?? "",
            Reward = Settings.Reward, LowestLevel = 1,
        };
        int lowest = int.MaxValue;
        foreach (var kv in _members)
        {
            var m = new HordeMember { Aid = kv.Key, Name = kv.Value.Name, State = kv.Value.State, Slot = kv.Value.Slot };
            var p = _sim != null && kv.Value.Slot >= 0 ? _sim.Player(kv.Value.Slot) : null;
            if (p != null) { m.Level = p.Level; m.JobId = p.JobId; m.Fallen = p.Fallen; lowest = Math.Min(lowest, p.Level); }
            s.Members.Add(m);
        }
        if (lowest != int.MaxValue) s.LowestLevel = lowest;
        foreach (var kv in _peers) if (kv.Value.Ok) s.Online.Add(new HordeMember { Aid = kv.Key, Name = kv.Value.Name }); // client wave 18 (h18rules), point 38: who could be invited
        return s;
    }

    void BroadcastStatus()
    {
        _lastStatusMs = _clock.ElapsedMilliseconds;
        var st = BuildStatus();
        byte[]? ok = null, notOk = null;
        foreach (var kv in _peers)
        {
            var f = kv.Value.Ok ? (ok ??= HordeWire.EncodeStatus(st, true)) : (notOk ??= HordeWire.EncodeStatus(st, false));
            Send(kv.Key, f);
        }
    }

    void Drain(long now)
    {
        while (_inbox.TryDequeue(out var m))
        {
            if (m.payload == null) { PeerGone(m.aid); continue; }
            var msg = HordeWire.DecodeClient(m.payload, 0, m.payload.Length);
            if (msg.Kind == 0) continue;
            if (msg.Kind == HordeWire.HHello) { Hello(m.aid, m.name, msg); continue; }
            if (!_peers.TryGetValue(m.aid, out var peer)) continue; // nothing before H_HELLO
            switch (msg.Kind)
            {
                case HordeWire.HStart: StartRequest(m.aid, peer, now, msg.Invite); break; // (client wave 18, h18rules, point 38: + the chosen players)
                case HordeWire.HAnswer: Answer(m.aid, peer, msg.RunId, msg.Join); break;
                case HordeWire.HInput:
                    if (_sim != null && _members.TryGetValue(m.aid, out var mi) && mi.State == HordeMemberState.Joined && mi.Slot >= 0) _sim.PushInput(mi.Slot, msg.Seq, msg.Dx, msg.Dy);
                    break;
                case HordeWire.HChoose:
                    if (_sim != null && _members.TryGetValue(m.aid, out var mc) && mc.State == HordeMemberState.Joined && mc.Slot >= 0) _sim.Choose(mc.Slot, msg.OfferId, msg.Index);
                    break;
                case HordeWire.HLeave: Leave(m.aid, "left the run"); break;
                case HordeWire.HEnd: EndRun(m.aid, msg.RunId); break; // client wave 14 (w14kit), point 15
                case HordeWire.HPause: // client wave 15 (w15hfx), point 19: the Horde menu's Pause / Resume, for everyone
                    if (_sim != null && _members.TryGetValue(m.aid, out var mp) && mp.State == HordeMemberState.Joined && mp.Slot >= 0 && _sim.Hold(mp.Slot, msg.Join))
                        Say($"{mp.Name} (AID {m.aid}) {HoldWords(_sim.LastHold)} run {_runId} at {_sim.Tick / HordeSim.TickRate:0} s"); // (client wave 18, h18rules: the countdown)
                    break;
                case HordeWire.HCast:
                    if (_sim != null && _members.TryGetValue(m.aid, out var ma) && ma.State == HordeMemberState.Joined && ma.Slot >= 0)
                        _sim.CastActive(ma.Slot, msg.WeaponId, msg.TargetX, msg.TargetY);
                    break;
            }
        }
    }

    /// <summary>Client wave 18 (h18rules), point 23: what a pause request did, for the log.</summary>
    static string HoldWords(HordeHoldResult r) => r switch
    {
        HordeHoldResult.Paused => "paused", HordeHoldResult.Resumed => "resumed",
        HordeHoldResult.PauseCounting => $"asked to pause ({HordeSim.PauseCountSeconds:0} s countdown)", HordeHoldResult.ResumeCounting => $"asked to resume ({HordeSim.PauseCountSeconds:0} s countdown)",
        HordeHoldResult.Cancelled => "cancelled the countdown of", _ => "changed the pause of",
    };

    void Hello(uint aid, string name, HordeWire.ClientMsg msg)
    {
        // the protocol version first (client wave 9): a client of another version is told "not ok" and never sent a run; it
        // keeps its headsets, voice and data check (nothing of its connection is touched)
        bool protoOk = msg.Version == HordeWire.Version;
        bool ok = protoOk && msg.Stamp == ContentStamp;
        _peers[aid] = new PeerState { Name = name, Sex = msg.Sex, Ok = ok, Protocol = msg.Version, NeedKey = true };
        Send(aid, HordeWire.EncodeTowns(Settings.Towns));
        Send(aid, HordeWire.EncodeStatus(BuildStatus(), ok));
        if (ok) BroadcastStatus(); // client wave 18 (h18rules), point 38: everyone's list of who could be invited has one more
        if (!protoOk) Say($"{name} (AID {aid}) runs another version of Horde (its protocol {msg.Version}, this host's {HordeWire.Version}): it cannot play here until one of the two updates Loki; its headsets and voice go on");
        else if (!ok) Say($"{name} (AID {aid}) has other Horde tables ({msg.Stamp}; the host's: {ContentStamp}): it cannot play here until one of the two updates Loki");
        // back after a dropped link: its place in the run is still there
        if (_phase == HordePhase.Running && _sim != null && ok && _members.TryGetValue(aid, out var mem) && mem.State == HordeMemberState.Joined && mem.Slot >= 0 && _sim.Player(mem.Slot) != null)
        {
            _sim.SetAway(mem.Slot, false); mem.AwaySinceMs = -1;
            SendRun(aid, mem.Slot, false);
            var open = _sim.Player(mem.Slot)!.Open;
            if (open != null) Send(aid, HordeWire.EncodeOffer(open));
            Say($"{name} (AID {aid}) is back in run {_runId}");
        }
    }

    void PeerGone(uint aid)
    {
        if (!_peers.Remove(aid)) return;
        BroadcastStatus(); // client wave 18 (h18rules), point 38: one fewer who could be invited
        if (_members.TryGetValue(aid, out var m) && m.State == HordeMemberState.Joined && _sim != null && m.Slot >= 0)
        {
            _sim.SetAway(m.Slot, true); m.AwaySinceMs = _clock.ElapsedMilliseconds;
            Say($"{m.Name} (AID {aid}) lost its link during run {_runId}: it keeps its place for {Settings.AwaySeconds:0} s");
        }
    }

    void CheckAway(long now)
    {
        foreach (var kv in _members)
        {
            var m = kv.Value;
            if (m.State != HordeMemberState.Joined || m.AwaySinceMs < 0 || now - m.AwaySinceMs < Settings.AwaySeconds * 1000) continue;
            m.AwaySinceMs = -1;
            if (_sim != null && m.Slot >= 0) _sim.RemovePlayer(m.Slot);
            m.State = HordeMemberState.Left;
            Say($"{m.Name} (AID {kv.Key}) did not come back: out of run {_runId} (its result stands)");
            BroadcastStatus();
        }
    }

    bool EveryoneAnswered()
    {
        foreach (var kv in _members) if (kv.Value.State == HordeMemberState.Asked && _peers.ContainsKey(kv.Key)) return false;
        return true;
    }

    /// <summary>Client wave 18 (h18rules), the design's point 38: a seat of the run: a player who joined it (gathering: Joined; playing: one
    /// that got a slot, present or gone: a seat stays its player's for the run, point 25's rejoin). At most HordeLimits.MaxRunPlayers.</summary>
    static bool Seated(MemberState m, HordePhase phase) => phase == HordePhase.Gathering ? m.State == HordeMemberState.Joined : m.Slot >= 0;
    int SeatsTaken() { int n = 0; foreach (var kv in _members) if (Seated(kv.Value, _phase)) n++; return n; }
    /// <summary>The run was started without a list (a client from before): anyone may join it (late too), seated up to the cap.</summary>
    bool _openRun = true;

    void StartRequest(uint aid, PeerState peer, long now, uint[]? invite = null)
    {
        if (!Settings.Enabled || !peer.Ok || _phase != HordePhase.Idle) { Send(aid, HordeWire.EncodeStatus(BuildStatus(), peer.Ok)); return; }
        var arenas = Content.Arenas.Where(a => !Settings.ArenasRefused.Contains(a.Id)).ToList();
        if (arenas.Count == 0) { Say($"{peer.Name} asked for a run, but no arena can be offered (none in the tables, or none walkable in this server's map cache)"); return; }
        _runId++;
        _seed = (ulong)DateTime.UtcNow.Ticks ^ ((ulong)_runId << 32);
        _arena = Settings.ArenaId != 0 ? arenas.FirstOrDefault(a => a.Id == Settings.ArenaId) ?? arenas[0] : arenas[(int)(_seed % (ulong)arenas.Count)];
        _phase = HordePhase.Gathering;
        _starterAid = aid; _starterName = peer.Name;
        _deadlineMs = now + (long)(Settings.InviteSeconds * 1000);
        _members.Clear();
        _members[aid] = new MemberState { Name = peer.Name, State = HordeMemberState.Joined };
        var inv = new HordeInvite { RunId = _runId, StarterAid = aid, StarterName = peer.Name, Seconds = Settings.InviteSeconds, ArenaMap = _arena.Map, RunLength = Settings.RunSeconds, Reward = Settings.Reward };
        var frame = HordeWire.EncodeInvite(inv);
        int asked = 0;
        // client wave 18 (h18rules), point 38: only the players the starter chose, in its order, at most HordeLimits.MaxRunPlayers - 1 of
        // them (a client from before names none: everyone, seated up to the cap as they answer)
        _openRun = invite == null;
        var who = invite != null ? invite.Distinct().Where(x => x != aid && _peers.ContainsKey(x)).Take(HordeLimits.MaxRunPlayers - 1).ToList() : _peers.Keys.Where(x => x != aid).ToList();
        foreach (var k in who)
        {
            var pk = _peers[k];
            if (!pk.Ok) continue;
            _members[k] = new MemberState { Name = pk.Name, State = HordeMemberState.Asked };
            Send(k, frame); asked++;
        }
        Say($"{peer.Name} (AID {aid}) is starting Horde run {_runId} on {_arena.Map} ({_arena.X0},{_arena.Y0})-({_arena.X1},{_arena.Y1}); {asked} other player(s) asked; it begins in {Settings.InviteSeconds:0} s or when all answered");
        BroadcastStatus();
    }

    void Answer(uint aid, PeerState peer, uint runId, bool join)
    {
        if (runId != _runId || !peer.Ok) return;
        if (_phase == HordePhase.Gathering)
        {
            // client wave 18 (h18rules), point 38: only an invited player answers; a join takes a seat, if one is left
            if (!_members.TryGetValue(aid, out var m))
            {
                if (!_openRun) { Say($"{peer.Name} (AID {aid}) asked to join run {_runId}: not invited by its starter"); Send(aid, HordeWire.EncodeStatus(BuildStatus(), peer.Ok)); return; }
                _members[aid] = m = new MemberState { Name = peer.Name }; // (a run open to everyone: as before)
            }
            if (join && m.State != HordeMemberState.Joined && SeatsTaken() >= HordeLimits.MaxRunPlayers) { Say($"{peer.Name} (AID {aid}) asked to join run {_runId}: it is full ({HordeLimits.MaxRunPlayers} players)"); Send(aid, HordeWire.EncodeStatus(BuildStatus(), peer.Ok)); return; }
            m.State = join ? HordeMemberState.Joined : HordeMemberState.Declined;
            Say($"{peer.Name} (AID {aid}) {(join ? "joins" : "declines")} run {_runId}");
            BroadcastStatus();
        }
        else if (_phase == HordePhase.Running && join && _sim != null)
        {
            if (_members.TryGetValue(aid, out var m) && m.State == HordeMemberState.Joined && m.Slot >= 0 && _sim.Player(m.Slot) != null) return;
            // client wave 18 (h18rules), point 38: a run joined late only by a player its starter invited (or one who played it: its seat is
            // still its own), and only while a seat is left
            if (m == null && !_openRun) { Say($"{peer.Name} (AID {aid}) asked to join run {_runId}: not invited by its starter"); return; }
            if ((m == null || m.Slot < 0) && SeatsTaken() >= HordeLimits.MaxRunPlayers) { Say($"{peer.Name} (AID {aid}) asked to join run {_runId}: it is full ({HordeLimits.MaxRunPlayers} players)"); BroadcastStatus(); return; }
            int slot = _sim.AddPlayer(aid, peer.Name, peer.Sex);
            if (slot < 0) { Say($"{peer.Name} (AID {aid}) could not join run {_runId}: it is full"); return; }
            _members[aid] = new MemberState { Name = peer.Name, State = HordeMemberState.Joined, Slot = slot };
            SendRun(aid, slot, true);
            Say($"{peer.Name} (AID {aid}) {(_sim.Player(slot)!.Rebuilt ? "is back in" : "joins")} run {_runId} late, at level {_sim.Player(slot)!.Level}{(_sim.Player(slot)!.Rebuilt ? " with its build" : "")}");
            BroadcastStatus();
        }
    }

    void SendRun(uint aid, int slot, bool late)
    {
        var a = _sim?.Arena ?? _arena!;
        var info = new HordeRunInfo
        {
            RunId = _runId, Slot = slot, ArenaId = a.Id, Map = a.Map, X0 = a.X0, Y0 = a.Y0, X1 = a.X1, Y1 = a.Y1, TickRate = HordeSim.TickRate,
            SnapshotEvery = Math.Max(1, SnapshotEvery), RunLength = Settings.RunSeconds, FinalBossSeconds = Content.Rules.FinalBossSeconds, Late = late,
        };
        Send(aid, HordeWire.EncodeRun(info, _sim!.Tick));
        if (_peers.TryGetValue(aid, out var p)) p.NeedKey = true;
    }

    void BeginRun()
    {
        var joined = _members.Where(kv => kv.Value.State == HordeMemberState.Joined && _peers.ContainsKey(kv.Key)).ToList();
        if (joined.Count == 0)
        {
            Say($"run {_runId}: nobody joined (or everyone who did is gone): no run");
            _phase = HordePhase.Idle; _members.Clear(); BroadcastStatus(); return;
        }
        var runArena = _arena!.ScaledForParty(joined.Count);
        _sim = new HordeSim(Content, runArena, _seed, Settings.RunSeconds);
        _enc = new SnapshotEncoder(_sim);
        foreach (var kv in joined) kv.Value.Slot = _sim.AddPlayer(kv.Key, kv.Value.Name, _peers[kv.Key].Sex);
        foreach (var kv in _members) if (kv.Value.State == HordeMemberState.Asked) kv.Value.State = HordeMemberState.Declined;
        _phase = HordePhase.Running;
        foreach (var kv in joined) SendRun(kv.Key, kv.Value.Slot, false);
        Say($"run {_runId} begins on {runArena.Map} ({runArena.X0},{runArena.Y0})-({runArena.X1},{runArena.Y1}) with {joined.Count} player(s): {string.Join(", ", joined.Select(j => j.Value.Name))} (seed {_seed}; bosses: {string.Join(" → ", _sim.BossOrder.Select(id => Content.Enemy(id)?.Key ?? id.ToString()))})");
        BroadcastStatus();
    }

    void Snapshot()
    {
        var sim = _sim!;
        bool wantKey = false;
        foreach (var kv in _members)
        {
            if (kv.Value.State != HordeMemberState.Joined || !_peers.TryGetValue(kv.Key, out var peer)) continue;
            if (Transport != null && Transport.TakeHordeLost(kv.Key)) peer.NeedKey = true;
            if (peer.NeedKey) wantKey = true;
        }
        _enc!.Encode(wantKey, out var delta, out var key);
        foreach (var kv in _members)
        {
            if (kv.Value.State != HordeMemberState.Joined || kv.Value.Slot < 0 || !_peers.TryGetValue(kv.Key, out var peer)) continue;
            var p = sim.Player(kv.Value.Slot);
            if (p == null) continue;
            if (Transport != null && Transport.HordeQueueDepth(kv.Key) > SlowQueue) { peer.NeedKey = true; SnapshotsSkipped++; continue; }
            var body = peer.NeedKey ? key : delta;
            if (body == null) continue;
            var f = SnapshotEncoder.Frame(p.AckSeq, body);
            if (f == null) continue;
            Send(kv.Key, f);
            if (peer.NeedKey) KeysSent++;
            peer.NeedKey = false;
            SnapshotBytesSent += f.Length; SnapshotsSent++;
        }
    }

    void Leave(uint aid, string why)
    {
        if (_phase == HordePhase.Gathering && _members.TryGetValue(aid, out var g)) { g.State = HordeMemberState.Declined; BroadcastStatus(); return; }
        if (_phase != HordePhase.Running || _sim == null || !_members.TryGetValue(aid, out var m) || m.State != HordeMemberState.Joined || m.Slot < 0) return;
        _sim.RemovePlayer(m.Slot);
        m.State = HordeMemberState.Left;
        var res = _sim.Results(_runId, HordeRewardSize.Off);
        Send(aid, HordeWire.EncodeResults(res, m.Slot));
        Say($"{m.Name} (AID {aid}) {why} {_runId} after {_sim.Tick / HordeSim.TickRate:0} s (its result stands; its reward is decided at the run's end)");
        BroadcastStatus();
    }

    /// <summary>Client wave 14 (w14kit), point 15 of the design's headset list (answer 12: "Even if you're not alone, it still ends the run for
    /// everyone"): H_END ends the run being played FOR EVERYONE, asked by a player who was in it (still in it, or left it: a "Left"
    /// member, whose result stands). It ends as Stop does (Stopped, then Finish): every player's result and reward as at a run's end,
    /// i.e. the EXP earned so far (leavers included, as always); the phase goes back to Idle, so the run can no longer be joined.
    /// A request for another run id, or from someone never in this run (asked, declined, not a member), is ignored.</summary>
    void EndRun(uint aid, uint runId)
    {
        if (_phase != HordePhase.Running || _sim == null || runId != _runId || !_members.TryGetValue(aid, out var m)) return;
        if (m.State != HordeMemberState.Joined && m.State != HordeMemberState.Left) return;
        Say($"{m.Name} (AID {aid}) ended run {_runId} for everyone after {_sim.Tick / HordeSim.TickRate:0} s");
        _sim.End(HordeOutcome.Stopped);
        Finish();
    }

    void Finish()
    {
        var sim = _sim!;
        var res = sim.Results(_runId, Rewards != null ? Settings.Reward : HordeRewardSize.Off);
        if (Rewards == null) foreach (var p in res.Players) { p.Reward = HordeRewardState.None; }
        foreach (var kv in _members)
        {
            if (kv.Value.Slot < 0 || kv.Value.State == HordeMemberState.Declined || kv.Value.State == HordeMemberState.Asked) continue;
            if (_peers.ContainsKey(kv.Key)) Send(kv.Key, HordeWire.EncodeResults(res, kv.Value.Slot));
        }
        // the reward decisions: one row per player with a share, written for rAthena's side (also for those who left or are gone)
        if (Rewards != null)
            foreach (var p in res.Players)
            {
                if (p.BaseShare <= 0 && p.JobShare <= 0) continue;
                var row = new HordeRewardRow { RunId = _runId, AccountId = p.Aid, CharName = p.Name, BaseShare = p.BaseShare, JobShare = p.JobShare };
                var rw = Rewards;
                var task = Task.Run(async () =>
                {
                    bool ok;
                    try { ok = await rw.RecordAsync(row); } catch (Exception e) { ok = false; Say($"reward of {row.CharName} (AID {row.AccountId}) NOT written: {e.Message}"); }
                    if (ok) Say($"reward written for {row.CharName} (AID {row.AccountId}): {row.BaseShare / 100.0:0.##} % of its next base level, {row.JobShare / 100.0:0.##} % of its next job level (granted by the server once, when it is online)");
                });
                lock (_pending) { _pending.RemoveAll(x => x.IsCompleted); _pending.Add(task); }
            }
        _lastRunLine = $"run {_runId} {DateTime.Now:HH:mm}: {res.Outcome} after {res.Seconds / 60:0.0} min on {_arena!.Map}; " +
                       string.Join("; ", res.Players.Select(p => $"{p.Name} lv {p.Level} job {p.JobId} kills {p.Kills} score {p.Score} reward {p.BaseShare / 100.0:0.##}%/{p.JobShare / 100.0:0.##}%{(p.Left ? " (left)" : "")}"));
        Say(_lastRunLine + $" [ticks: {TicksRun}, mean {TickMsTotal / Math.Max(1, TicksRun):0.000} ms, max {TickMsMax:0.0} ms; snapshots {SnapshotsSent} ({SnapshotBytesSent / Math.Max(1, SnapshotsSent)} bytes each), {KeysSent} keys, {SnapshotsSkipped} skipped; hits not drawn {_enc!.HitsDropped}]");
        LastResults = res;
        _sim = null; _enc = null; _phase = HordePhase.Idle; _members.Clear();
        BroadcastStatus();
    }

    /// <summary>The results of the last run that ended (tests, the window).</summary>
    public HordeResults? LastResults { get; private set; }

    void PollRewards()
    {
        _rewardPollBusy = true;
        var rw = Rewards!;
        _ = Task.Run(async () =>
        {
            try
            {
                var rows = await rw.TakeGrantedAsync();
                foreach (var r in rows)
                {
                    Say($"reward GRANTED to {r.CharName} (AID {r.AccountId}) for run {r.RunId}: {r.BaseExp} base EXP, {r.JobExp} job EXP");
                    Send(r.AccountId, HordeWire.EncodeReward(new HordeRewardNote { RunId = r.RunId, BaseExp = r.BaseExp, JobExp = r.JobExp, BaseShare = r.BaseShare, JobShare = r.JobShare }));
                }
            }
            catch (Exception e) { Say("reading granted rewards failed: " + e.Message); }
            finally { _rewardPollBusy = false; }
        });
    }
}
