using Loki.Horde;
using Loki.Horde.Net;
using Loki.Net;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>
/// Client wave 9 (h9move): what the movement FEELS like, measured. A host (HordeSim + SnapshotEncoder, the very code the relay
/// runs) and one HordeClient (the very code the Unity client runs) on ONE virtual clock, joined by a simulated link (a one-way
/// delay with jitter, frames in order), the client polled at 90 frames a second as a headset does. Measured, before and after
/// the wave's changes (design notes): input-to-own-motion latency, the size and number of prediction corrections and
/// the steps they make in my own drawn motion, the frame-to-frame velocity variance (jitter) of a remote player and of an enemy
/// as drawn from the snapshots, and the snapshot bytes per second. Every number is printed; the asserts are the wave's targets.
/// </summary>
public class HordeFeelTests
{
    readonly ITestOutputHelper _out;
    public HordeFeelTests(ITestOutputHelper o) { _out = o; }

    /// <summary>One link direction: frames arrive delay ± jitter later, never out of order.</summary>
    internal sealed class Pipe
    {
        readonly Queue<(double at, byte[] f)> _q = new();
        readonly Random _rnd; readonly double _delay, _jitter; double _lastAt;
        public long Bytes;
        public Pipe(Random rnd, double delay, double jitter) { _rnd = rnd; _delay = delay; _jitter = jitter; }
        public void Send(double now, byte[] f)
        {
            double at = now + _delay + (_rnd.NextDouble() * 2 - 1) * _jitter;
            if (at < _lastAt) at = _lastAt;
            _lastAt = at; _q.Enqueue((at, f)); Bytes += f.Length;
        }
        public bool TryTake(double now, out byte[] f)
        {
            if (_q.Count > 0 && _q.Peek().at <= now) { f = _q.Dequeue().f; return true; }
            f = null!; return false;
        }
    }

    /// <summary>The rig: host at the engine's tick rate, snapshots every SnapshotEvery ticks, one predicted client (slot 0) and
    /// one remote player (slot 1) walked by the host itself, frames at 90 Hz.</summary>
    internal sealed class Rig
    {
        public readonly HordeContent C;
        public readonly HordeSim Sim;
        public readonly SnapshotEncoder Enc;
        public readonly HordeClient Client;
        public readonly Pipe Down, Up;
        public double Now;
        public int SnapshotEvery = 2;
        readonly Random _frameRnd;
        double _nextTick, _frameDt;
        int _tickInRun; bool _needKey = true;
        public int Me = 0, Remote = 1;
        public int RemoteDx = 127, RemoteDy = 0; ushort _remoteSeq;
        public long SnapshotBytes; public int Snapshots;

        public Rig(double delay, double jitter, int seed = 7)
        {
            C = HordeTestContent.Build();
            // nobody falls here: no waves, and the enemies spawned by the test do not hurt (the measurement is of motion only)
            C.Waves.Clear(); foreach (var e in C.Enemies) e.Damage = 0;
            Sim = new HordeSim(C, C.Arenas[0], 1234, 900);
            Enc = new SnapshotEncoder(Sim);
            var rnd = new Random(seed);
            Down = new Pipe(rnd, delay, jitter); Up = new Pipe(rnd, delay, jitter);
            _frameRnd = new Random(seed + 1);
            Client = new HordeClient(C, () => Now);
            Client.Send = f => Up.Send(Now, f);
            Sim.AddPlayer(5000001, "me"); Sim.AddPlayer(5000002, "remote");
            _frameDt = 1.0 / 90;
            _nextTick = 0;
            // the link comes up: WELCOME with Horde → H_HELLO → status (ok) → the run
            Client.OnLinked(true);
            Client.Poll();
            PumpUp();
            var st = new HordeStatus { Enabled = true, Phase = HordePhase.Running, RunId = 1 };
            Deliver(HordeWire.EncodeStatus(st, true));
            var a = Sim.Arena;
            Deliver(HordeWire.EncodeRun(new HordeRunInfo { RunId = 1, Slot = Me, ArenaId = a.Id, Map = a.Map, X0 = a.X0, Y0 = a.Y0, X1 = a.X1, Y1 = a.Y1, TickRate = HordeSim.TickRate, RunLength = 900, FinalBossSeconds = 120 }, Sim.Tick));
        }

        void Deliver(byte[] frame) => Down.Send(Now, frame);

        void PumpUp()
        {
            while (Up.TryTake(Now, out var f))
            {
                var m = HordeWire.DecodeClient(f, 2, f.Length - 2);
                if (m.Kind == HordeWire.HInput) Sim.PushInput(Me, m.Seq, m.Dx, m.Dy);
            }
        }

        void HostTick()
        {
            PumpUp();
            _remoteSeq++;
            Sim.PushInput(Remote, _remoteSeq, RemoteDx, RemoteDy);
            Sim.Step();
            Enc.Collect(); Sim.ClearOutputs();
            if (++_tickInRun >= SnapshotEvery)
            {
                _tickInRun = 0;
                Enc.Encode(_needKey, out var delta, out var key);
                var body = _needKey ? key : delta; _needKey = false;
                var f = SnapshotEncoder.Frame(Sim.Player(Me)!.AckSeq, body)!;
                Deliver(f); SnapshotBytes += f.Length; Snapshots++;
            }
        }

        /// <summary>One client frame: the clock moves, frames due arrive, the host ticks when due, the client polls, then sets its stick (as HordeGame.Update does).</summary>
        public void Frame(float stickX, float stickY)
        {
            Now += _frameDt + (_frameRnd.NextDouble() * 2 - 1) * 0.0015;
            while (_nextTick <= Now) { HostTick(); _nextTick += 1.0 / HordeSim.TickRate; }
            PumpUp();
            while (Down.TryTake(Now, out var f)) Client.OnFrame(f, 2, f.Length - 2);
            Client.Poll();
            Client.SetMove(stickX, stickY);
        }

        public void Run(double seconds, float sx, float sy) { double end = Now + seconds; while (Now < end) Frame(sx, sy); }
    }

    static (double mean, double sd, double max) Stats(List<double> v)
    {
        if (v.Count == 0) return (0, 0, 0);
        double m = v.Average(), s = Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / v.Count), mx = v.Max();
        return (m, s, mx);
    }

    /// <summary>A remote thing's drawn place, as HordeWorldDraw / HordePlayers draw it today.</summary>
    static (float x, float y) Drawn(HordeWorldView w, double now, float px, float py, float x, float y)
    {
        float a = w.Alpha(now);
        return (px + (x - px) * a, py + (y - py) * a);
    }

    [Theory]
    [InlineData("LAN", 0.002, 0.001)]
    [InlineData("internet", 0.035, 0.012)]
    public void Feel_latency_corrections_jitter_and_bytes(string link, double delay, double jitter)
    {
        var rig = new Rig(delay, jitter);
        var c = rig.Client;
        rig.Run(1.5, 0, 0);
        Assert.True(c.InRun, "the client is in the run");
        float speed = rig.C.Rules.MoveSpeed;

        // ---- 1. input-to-own-motion latency: from SetMove(1, 0) to the first frame my drawn place has moved
        var lat = new List<double>();
        for (int i = 0; i < 40; i++)
        {
            float dir = i % 2 == 0 ? 1 : -1; // east, then back west: never into the arena's wall
            rig.Run(0.3 + i * 0.013, 0, 0);
            float x0 = c.MyX, y0 = c.MyY; double t0 = rig.Now;
            int guard = 0;
            while (Math.Abs(c.MyX - x0) < 1e-4f && Math.Abs(c.MyY - y0) < 1e-4f && guard++ < 90) rig.Frame(dir, 0);
            lat.Add(rig.Now - t0);
            rig.Run(0.3, dir, 0);
        }
        var (lm, ls, lx) = Stats(lat);

        // ---- 2. a minute of play: the stick changes direction every 0.4 s (and rests now and then); corrections and my own drawn steps
        c.Corrections = 0; c.LargestCorrection = 0;
        var dirRnd = new Random(3);
        var mySteps = new List<double>(); int myStalls = 0, myFrames = 0; float lx0 = c.MyX, ly0 = c.MyY; double lt = rig.Now;
        var remoteV = new List<double>(); int remoteZero = 0, remoteBack = 0, remoteFrames = 0; double remoteMaxJump = 0; float rx0 = 0, ry0 = 0; bool rInit = false; double rvxPrev = 0;
        var enemyV = new List<double>(); int enemyZero = 0; double enemyMaxJump = 0; int enemySlot = -1; float ex0 = 0, ey0 = 0;
        long bytes0 = rig.Down.Bytes; double t1 = rig.Now;
        float sx = 1, sy = 0; double nextTurn = rig.Now + 0.4;
        var w = c.World;
        // one enemy far from everyone, walking at the remote player (it chases): its drawn motion
        var poring = rig.C.Enemy(1)!;
        var rp = rig.Sim.Player(rig.Remote)!;
        enemySlot = rig.Sim.Spawn(poring, Math.Max(rig.Sim.MinX, rp.X - 20 * 256), rp.Y, true);
        while (rig.Now < t1 + 60)
        {
            if (rig.Now >= nextTurn)
            {
                nextTurn = rig.Now + 0.4;
                if (dirRnd.Next(5) == 0) { sx = sy = 0; }
                else { double a = dirRnd.NextDouble() * Math.PI * 2; sx = (float)Math.Cos(a); sy = (float)Math.Sin(a); }
            }
            // the remote player bounces between the arena's sides
            if (rp.X > rig.Sim.MaxX - 3 * 256) rig.RemoteDx = -127; else if (rp.X < rig.Sim.MinX + 3 * 256) rig.RemoteDx = 127;
            rig.Frame(sx, sy);
            w.Prepare(rig.Now); // as the drawing does first thing (Alpha): this frame's Prev / Cur of everything
            double dt = rig.Now - lt; lt = rig.Now;
            // my own drawn motion
            float mx = c.MyX, my = c.MyY; double step = Math.Sqrt((mx - lx0) * (mx - lx0) + (my - ly0) * (my - ly0)); lx0 = mx; ly0 = my;
            myFrames++;
            bool stickHeld = sx != 0 || sy != 0;
            if (step / dt > speed * 1.6) mySteps.Add(step);            // faster than I can walk: a correction's jump
            if (stickHeld && step < 1e-5 && rig.Now > t1 + 0.3) myStalls++;
            // the remote player as drawn
            var pv = w.Players[rig.Remote];
            if (pv.Present)
            {
                var (dx, dy) = Drawn(w, rig.Now, pv.PrevX, pv.PrevY, pv.X, pv.Y);
                if (rInit)
                {
                    double vx = (dx - rx0) / dt, vy = (dy - ry0) / dt, v = Math.Sqrt(vx * vx + vy * vy);
                    remoteFrames++;
                    remoteV.Add(v);
                    if (v < 1e-3) remoteZero++;
                    if (Math.Sign(vx) != 0 && Math.Sign(rvxPrev) != 0 && Math.Sign(vx) != Math.Sign(rvxPrev) && Math.Abs(vx) > 0.5 && Math.Abs(rvxPrev) > 0.5) remoteBack++;
                    remoteMaxJump = Math.Max(remoteMaxJump, v * dt);
                    rvxPrev = vx;
                }
                rx0 = dx; ry0 = dy; rInit = true;
            }
            // the enemy as drawn
            if (enemySlot >= 0 && w.Enemies[enemySlot].Alive)
            {
                var ev = w.Enemies[enemySlot];
                var (dx, dy) = Drawn(w, rig.Now, ev.PrevX, ev.PrevY, ev.X, ev.Y);
                if (ex0 != 0 || ey0 != 0)
                {
                    double v = Math.Sqrt((dx - ex0) * (dx - ex0) + (dy - ey0) * (dy - ey0)) / dt;
                    enemyV.Add(v); if (v < 1e-3) enemyZero++; enemyMaxJump = Math.Max(enemyMaxJump, v * dt);
                }
                ex0 = dx; ey0 = dy;
            }
        }
        double secs = rig.Now - t1;
        var (rm, rs, rmx) = Stats(remoteV);
        var (em, es, emx) = Stats(enemyV);
        double bytesPerSec = (rig.Down.Bytes - bytes0) / secs;
        _out.WriteLine($"[{link}: one-way {delay * 1000:0} ms ± {jitter * 1000:0}] tick {HordeSim.TickRate}/s, snapshots {HordeSim.TickRate / rig.SnapshotEvery}/s, frames 90/s");
        _out.WriteLine($"  latency stick → own motion: mean {lm * 1000:0.0} ms, sd {ls * 1000:0.0}, max {lx * 1000:0.0} ms ({lat.Count} tries)");
        _out.WriteLine($"  corrections in {secs:0} s: {c.Corrections} (> 1/16 cell), largest {c.LargestCorrection:0.000} cells; my drawn steps (> 1.6× walk speed): {mySteps.Count}, largest {(mySteps.Count > 0 ? mySteps.Max() : 0):0.000} cells; stalls with the stick held: {myStalls} of {myFrames} frames");
        _out.WriteLine($"  remote player as drawn: speed mean {rm:0.00} cells/s (walks at {speed}), sd {rs:0.00} ({(rm > 0 ? rs / rm * 100 : 0):0} %), largest frame jump {rmx / 90:0.000} cells ({remoteMaxJump:0.000}); frozen frames {remoteZero} of {remoteFrames}, reversals {remoteBack}");
        _out.WriteLine($"  enemy as drawn: speed mean {em:0.00} cells/s (walks at {poring.Speed}), sd {es:0.00} ({(em > 0 ? es / em * 100 : 0):0} %), largest frame jump {enemyMaxJump:0.000} cells; frozen frames {enemyZero} of {enemyV.Count}");
        _out.WriteLine($"  snapshots down: {bytesPerSec / 1024:0.00} KB/s with 2 players and {rig.Sim.EnemiesAlive} enemies ({rig.Snapshots} snapshots, {(rig.Snapshots > 0 ? rig.SnapshotBytes / rig.Snapshots : 0)} bytes each); inputs up {rig.Up.Bytes / (rig.Now):0} B/s");
        _out.WriteLine($"  render delay {w.RenderDelayTicks:0.00} ticks ({w.RenderDelayTicks * 1000 / HordeSim.TickRate:0} ms behind the earliest arrivals); frames extrapolated {w.FramesExtrapolated} of {w.FramesPrepared}");
        Assert.Equal(0, c.SnapshotsRefused);
        Assert.True(c.InRun);
        // the wave's targets (design notes §1; wave 8 measured 42 ms, 267-402 steps, 15-17 % frozen frames, 19-38 % / 33-53 % sd)
        Assert.True(lm < 0.030 && lx < 0.050, $"latency {lm * 1000:0} ms mean, {lx * 1000:0} max");
        Assert.True(mySteps.Count == 0, $"{mySteps.Count} own drawn steps");
        Assert.True(myStalls < myFrames / 100, $"{myStalls} frozen frames with the stick held");
        Assert.True(rm > 0 && rs / rm < 0.20, $"remote player speed sd {rs / rm * 100:0} %");
        Assert.True(remoteBack <= 2, $"{remoteBack} reversals");
        Assert.True(em > 0 && es / em < 0.25, $"enemy speed sd {es / em * 100:0} %");
        Assert.True(w.FramesExtrapolated < w.FramesPrepared / 100, $"{w.FramesExtrapolated} extrapolated frames of {w.FramesPrepared}");
    }
}
