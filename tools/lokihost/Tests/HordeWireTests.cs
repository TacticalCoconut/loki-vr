using System.Net;
using Loki.Horde;
using Loki.Horde.Net;
using Loki.Net;
using LokiHost.Core;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>Client wave 9 (h9move): Horde's protocol version, and the pieces of the walk that both ends must compute alike.</summary>
public class HordeWireTests
{
    readonly ITestOutputHelper _out;
    public HordeWireTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public async Task A_wave_8_client_on_this_relay_is_told_not_ok_and_never_sent_a_run_while_headsets_and_voice_go_on()
    {
        var content = HordeTestContent.Build();
        var horde = new HordeService(content, new HordeSettings { InviteSeconds = 1 }) { TickMs = 10 };
        var relay = new PoseRelay(0) { Horde = horde };
        horde.Transport = relay;
        var log = new List<string>(); horde.Log += l => { lock (log) log.Add(l); }; relay.Log += l => { lock (log) log.Add(l); };
        relay.Start(IPAddress.Loopback); horde.Start();
        try
        {
            // a wave-8 client: HELLO 2, then H_HELLO with protocol 1 and ITS stamp (whatever it is), then it tries to start a run
            using var old = new System.Net.Sockets.TcpClient(); old.Connect(IPAddress.Loopback, relay.Port);
            var s = old.GetStream();
            s.Write(PresenceWire.EncodeHello(3300001, "Eight", PresenceWire.VersionVoice));
            var h = new HordeWire.W().U8(HordeWire.HHello).U8(1).U8(1).Str(HordeWire.ContentStamp(content), HordeWire.MaxStampBytes).Frame();
            s.Write(h);
            s.Write(HordeWire.EncodeStart());
            // a current bot plays a run on the same relay meanwhile
            using var bot = new HordeBot("127.0.0.1", relay.Port, 3300002, "Nine", content);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (bot.Client.LinkState != HordeLinkState.Ready && sw.ElapsedMilliseconds < 4000) { bot.Update(); Thread.Sleep(5); }
            bot.Client.RequestStart();
            while (!bot.Client.InRun && sw.ElapsedMilliseconds < 8000) { bot.Update(); Thread.Sleep(5); }
            Assert.True(bot.Client.InRun);
            for (int i = 0; i < 100; i++) { bot.Update(); Thread.Sleep(10); }
            // what the old client got: WELCOME, H_TOWNS, H_STATUS frames (content not ok, the protocol byte at the end) and nothing of a run
            var buf = new byte[65536]; int n = 0;
            while (old.Client.Poll(200000, System.Net.Sockets.SelectMode.SelectRead)) { int k = s.Read(buf, n, buf.Length - n); if (k <= 0) break; n += k; }
            var frames = new PresenceWire.FrameReader(HordeWire.MaxPayload);
            frames.Append(buf, 0, n);
            int statuses = 0, runs = 0, snaps = 0, invites = 0, others = 0;
            while (true)
            {
                int k = frames.NextView(out var p, out int o, out int len);
                if (k == 0) break;
                Assert.True(k > 0);
                switch (p[o])
                {
                    case HordeWire.HStatus:
                    {
                        statuses++;
                        var st = new HordeStatus();
                        Assert.True(HordeWire.DecodeStatus(p, o, len, st, out bool ok));
                        Assert.False(ok);
                        Assert.Equal(HordeWire.Version, st.Protocol);
                        break;
                    }
                    case HordeWire.HRun: runs++; break;
                    case HordeWire.HSnap: snaps++; break;
                    case HordeWire.HInvite: invites++; break;
                    case PresenceWire.Welcome: case HordeWire.HTowns: break;
                    default: others++; break;
                }
            }
            _out.WriteLine($"old client got {statuses} statuses, {runs} runs, {snaps} snapshots, {invites} invites, {others} other frames");
            Assert.True(statuses >= 1);
            Assert.Equal(0, runs); Assert.Equal(0, snaps); Assert.Equal(0, invites);
            Assert.True(old.Connected);
            lock (log) Assert.Contains(log, l => l.Contains("another version of Horde"));
            lock (log) Assert.DoesNotContain(log, l => l.Contains("Eight") && l.Contains("disconnected"));
        }
        finally { horde.Stop(); await relay.StopAsync(); }
    }

    [Fact]
    public void A_status_without_the_protocol_byte_is_a_wave_8_relay_and_the_client_says_OtherProtocol()
    {
        // H_STATUS as a wave-8 relay writes it: no trailing protocol byte
        var st = new HordeStatus { Enabled = true, Phase = HordePhase.Idle };
        var full = HordeWire.EncodeStatus(st, true);
        var old = new byte[full.Length - 4];
        Array.Copy(full, 2, old, 0, full.Length - 4); // the payload (after the u16 length) without its protocol byte (client wave 18, h18rules: and the list after it, H_STATUS's new tail)
        var decoded = new HordeStatus();
        Assert.True(HordeWire.DecodeStatus(old, 0, old.Length, decoded, out bool ok1));
        Assert.True(ok1); Assert.Equal(1, decoded.Protocol);
        Assert.True(HordeWire.DecodeStatus(full, 2, full.Length - 2, decoded, out _));
        Assert.Equal(HordeWire.Version, decoded.Protocol);
        // the client's state machine on such a relay
        var c = new HordeClient(HordeTestContent.Build(), () => 0.0);
        var sent = new List<byte[]>(); c.Send = sent.Add;
        c.OnLinked(true); c.Poll();
        Assert.Single(sent); // its H_HELLO
        c.OnFrame(old, 0, old.Length); c.Poll();
        Assert.Equal(HordeLinkState.OtherProtocol, c.LinkState);
        c.RequestStart(); c.Poll();
        Assert.Single(sent); // nothing more is sent
        // and a run message from such a relay is ignored (never a garbled run)
        var run = HordeWire.EncodeRun(new HordeRunInfo { RunId = 1, Slot = 0, Map = "prt_fild08", X0 = 1, Y0 = 1, X1 = 10, Y1 = 10, TickRate = 20 }, 0);
        c.OnFrame(run, 2, run.Length - 2); c.Poll();
        Assert.False(c.InRun);
        // the same relay saying protocol 2: Ready
        c.OnFrame(full, 2, full.Length - 2); c.Poll();
        Assert.Equal(HordeLinkState.Ready, c.LinkState);
    }

    [Fact]
    public void The_ramp_is_integer_and_the_same_on_both_ends_and_reaches_full_speed_in_its_time()
    {
        int speed = 26; // 4 cells/s at 40 ticks
        int accel = HordeMove.RampPerTick(speed, 0.10f), decel = HordeMove.RampPerTick(speed, 0.06f);
        int x = 150 * 256, y = 150 * 256, vx = 0, vy = 0;
        int x2 = x, y2 = y, vx2 = 0, vy2 = 0;
        var rnd = new Random(3);
        int ticksToFull = -1;
        for (int i = 0; i < 400; i++)
        {
            int dx = i < 100 ? 127 : i < 200 ? 0 : rnd.Next(-127, 128), dy = i < 200 ? 0 : rnd.Next(-127, 128);
            HordeMove.StepRamp(ref x, ref y, ref vx, ref vy, dx, dy, speed, accel, decel, 192, 130 * 256, 130 * 256, 221 * 256 - 1, 221 * 256 - 1);
            HordeMove.StepRamp(ref x2, ref y2, ref vx2, ref vy2, dx, dy, speed, accel, decel, 192, 130 * 256, 130 * 256, 221 * 256 - 1, 221 * 256 - 1);
            if (ticksToFull < 0 && vx == speed) ticksToFull = i + 1;
            if (i == 150) Assert.Equal(0, vx); // stopped within 50 ticks of the stick's release
            Assert.True(Math.Abs(vx) <= speed && Math.Abs(vy) <= speed);
        }
        Assert.Equal(x, x2); Assert.Equal(y, y2); Assert.Equal(vx, vx2); Assert.Equal(vy, vy2);
        Assert.InRange(ticksToFull, 3, 5); // 0.10 s at 40 ticks = 4 ticks
        _out.WriteLine($"accel {accel} decel {decel} units/tick², full speed after {ticksToFull} ticks");
        // the soft wall: walking east into the bound slows over the last 0.75 cell and never crosses it
        x = 220 * 256; vx = speed; int lastStep = speed;
        for (int i = 0; i < 60; i++)
        {
            int before = x;
            HordeMove.StepRamp(ref x, ref y, ref vx, ref vy, 127, 0, speed, accel, decel, 192, 130 * 256, 130 * 256, 221 * 256 - 1, 221 * 256 - 1);
            int step = x - before;
            Assert.True(step <= lastStep, "the step toward the wall never grows");
            lastStep = step;
        }
        Assert.True(x <= 221 * 256 - 1);
        Assert.True(x >= 221 * 256 - 1 - 2); // and it does reach it (within rounding)
    }

    /// <summary>Client wave 14 (w14kit), point 15: H_END carries its run id and decodes as itself; a cut or padded one is dropped.</summary>
    [Fact]
    public void H_END_round_trips_with_its_run_id_and_a_malformed_one_is_dropped()
    {
        var f = HordeWire.EncodeEnd(4242);
        Assert.Equal(new byte[] { 5, 0, HordeWire.HEnd, 0x92, 0x10, 0, 0 }, f);
        var m = HordeWire.DecodeClient(f, 2, f.Length - 2);
        Assert.Equal(HordeWire.HEnd, m.Kind);
        Assert.Equal(4242u, m.RunId);
        Assert.Equal(0, HordeWire.DecodeClient(f, 2, f.Length - 3).Kind);
        Assert.Equal(0, HordeWire.DecodeClient(new byte[] { HordeWire.HEnd, 1, 0, 0, 0, 9 }, 0, 6).Kind);
    }

    [Fact]
    public void A_knockback_impulse_travels_its_distance_and_dies()
    {
        int keep = 75, units = 2 * 256;
        int vx = HordeMove.ImpulseFor(units, keep), vy = 0, x = 0, ticks = 0;
        while (vx != 0 && ticks < 100) { x += vx; HordeMove.Decay(ref vx, ref vy, keep); ticks++; }
        _out.WriteLine($"2 cells of knockback: travelled {x / 256f:0.00} cells in {ticks} ticks ({ticks * 1000 / HordeSim.TickRate} ms)");
        Assert.InRange(x / 256f, 1.8f, 2.0f);
        Assert.InRange(ticks, 8, 20);
    }

    [Fact]
    public void Dir8_follows_the_clients_convention_and_the_hysteresis_stops_the_flicker()
    {
        Assert.Equal(6, HordeMove.Dir8(0)); Assert.Equal(5, HordeMove.Dir8(45)); Assert.Equal(4, HordeMove.Dir8(90)); Assert.Equal(3, HordeMove.Dir8(135));
        Assert.Equal(2, HordeMove.Dir8(180)); Assert.Equal(1, HordeMove.Dir8(225)); Assert.Equal(0, HordeMove.Dir8(270)); Assert.Equal(7, HordeMove.Dir8(315));
        Assert.Equal(6, HordeMove.Dir8(-10)); Assert.Equal(7, HordeMove.Dir8(-45));
        for (int d = 0; d < 8; d++) Assert.Equal(d, HordeMove.Dir8(HordeMove.Dir8Centre(d)));
        // a stick wobbling ±5° around the east / north-east boundary (22.5°): no hysteresis flickers, with it the direction holds
        int plain = 6, hyst = 6, flickersPlain = 0, flickersHyst = 0;
        var rnd = new Random(9);
        for (int i = 0; i < 1000; i++)
        {
            float a = 22.5f + (float)(rnd.NextDouble() * 10 - 5);
            int np = HordeMove.Dir8(a); if (np != plain) { flickersPlain++; plain = np; }
            int nh = HordeMove.Dir8Hysteresis(hyst, a, 10f); if (nh != hyst) { flickersHyst++; hyst = nh; }
        }
        _out.WriteLine($"wobble at a boundary: {flickersPlain} direction changes without hysteresis, {flickersHyst} with");
        Assert.True(flickersPlain > 100);
        Assert.Equal(0, flickersHyst);
        // a real turn is still followed
        Assert.Equal(4, HordeMove.Dir8Hysteresis(6, 90, 10f));
    }

    [Fact]
    public void The_table_follow_never_overshoots_or_snaps_and_stays_still_inside_its_dead_zone()
    {
        foreach (var mode in new[] { HordeFollowMode.Smooth, HordeFollowMode.Tight, HordeFollowMode.Fixed })
        {
            var f = new HordeTableFollow { Mode = mode };
            f.Reset(100, 100);
            // a small shuffle inside the dead zone: the focus does not move
            float dz = HordeTableFollow.DeadZone(mode) * 0.9f;
            for (int i = 0; i < 90; i++) f.Step(100 + dz * (i % 2 == 0 ? 1 : -1), 100, 0, 0, 1f / 90);
            Assert.Equal(100, f.X, 3); Assert.Equal(100, f.Y, 3);
            // a walk east at 4 cells/s for 3 s: the focus follows without ever passing the target, with no frame jumping more than it could
            float x = 100, maxStep = 0, maxAhead = 0; float px = f.X;
            for (int i = 0; i < 270; i++)
            {
                x += 4f / 90; f.Step(x, 100, 4, 0, 1f / 90);
                maxStep = Math.Max(maxStep, Math.Abs(f.X - px)); px = f.X;
                float lead = HordeTableFollow.Lead(mode);
                maxAhead = Math.Max(maxAhead, f.X - (x + lead));
            }
            Assert.True(maxAhead <= 0.01f, $"{mode}: the focus passed the target by {maxAhead}");
            Assert.True(maxStep < 0.6f, $"{mode}: a frame of {maxStep} cells");
            // the character jumps 30 cells (a late join elsewhere): the focus glides to the dead zone's edge, never snaps (no frame
            // moves more than a few cells), within a couple of seconds
            float far = x + 30; maxStep = 0; px = f.X; int frames = 0;
            float settle = HordeTableFollow.DeadZone(mode) + 0.05f;
            while (Math.Abs(f.X - far) > settle && frames++ < 2000) { f.Step(far, 100, 0, 0, 1f / 90); maxStep = Math.Max(maxStep, Math.Abs(f.X - px)); px = f.X; }
            _out.WriteLine($"{mode}: 30-cell jump closed to the dead zone in {frames / 90f:0.00} s, largest frame step {maxStep:0.00} cells");
            Assert.True(maxStep < 3f, $"{mode}: a snap of {maxStep} cells in one frame");
            Assert.True(frames < 90 * 4, $"{mode}: {frames} frames to close");
        }
    }

    [Fact]
    public void Enemy_moves_beyond_a_byte_travel_as_jumps_and_the_decoder_agrees_on_every_place()
    {
        var c = HordeTestContent.Build(); c.Waves.Clear();
        var sim = new HordeSim(c, c.Arenas[0], 5, 900);
        sim.AddPlayer(1, "a");
        var enc = new SnapshotEncoder(sim); var dec = new SnapshotDecoder(); var view = new HordeWorldView();
        dec.Reset(new HordeRunInfo { X0 = sim.Arena.X0, Y0 = sim.Arena.Y0, X1 = sim.Arena.X1, Y1 = sim.Arena.Y1 });
        var poring = c.Enemy(1)!;
        var p = sim.Player(0)!;
        for (int i = 0; i < 20; i++) sim.Spawn(poring, p.X + 1000 + i * 300, p.Y + (i % 3) * 300, true);
        double now = 0;
        for (int t = 0; t < 200; t++)
        {
            // every 10 ticks one enemy is thrown 5 cells (a knockback bigger than a byte of 1/64 cells carries in one snapshot)
            if (t % 10 == 5) { var e = sim.Enemies[t / 10 % 20]; if (e.Alive) e.X = Math.Min(sim.MaxX, e.X + 5 * 256); }
            sim.PushInput(0, (ushort)(t + 1), 0, 0);
            sim.Step(); enc.Collect(); sim.ClearOutputs();
            if (t % 2 == 1)
            {
                enc.Encode(t == 1, out var delta, out var key);
                var body = t == 1 ? key! : delta;
                now += 0.05;
                Assert.True(dec.Apply(body, 0, body.Length, view, 0, now));
                for (int i = 0; i < HordeLimits.MaxEnemies; i++)
                    if (sim.Enemies[i].Alive)
                        Assert.True(Math.Abs(sim.Enemies[i].X / 256f - view.Enemies[i].X) < 1 / 64f + 1e-3f && Math.Abs(sim.Enemies[i].Y / 256f - view.Enemies[i].Y) < 1 / 64f + 1e-3f, $"enemy {i} at tick {t}");
            }
        }
    }

    /// <summary>Client wave 15 (w15hfx), point 19: H_PAUSE carries pause / resume (ClientMsg.Join); a cut or padded one is dropped.</summary>
    [Fact]
    public void H_PAUSE_round_trips_pause_and_resume_and_a_malformed_one_is_dropped()
    {
        var on = HordeWire.EncodePause(true); var off = HordeWire.EncodePause(false);
        Assert.Equal(new byte[] { 2, 0, HordeWire.HPause, 1 }, on);
        var m = HordeWire.DecodeClient(on, 2, on.Length - 2);
        Assert.Equal(HordeWire.HPause, m.Kind); Assert.True(m.Join);
        m = HordeWire.DecodeClient(off, 2, off.Length - 2);
        Assert.Equal(HordeWire.HPause, m.Kind); Assert.False(m.Join);
        Assert.Equal(0, HordeWire.DecodeClient(on, 2, 1).Kind);
        Assert.Equal(0, HordeWire.DecodeClient(new byte[] { HordeWire.HPause, 1, 0 }, 0, 3).Kind);
    }
}
