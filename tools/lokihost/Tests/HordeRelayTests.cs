using System.Net;
using System.Net.Sockets;
using Loki.Horde;
using Loki.Horde.Content;
using Loki.Horde.Net;
using Loki.Net;
using LokiHost.Core;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>Client wave 8 (w8core): Horde through the real relay, with headless bots (the client's own HordeClient).</summary>
public class HordeRelayTests
{
    readonly ITestOutputHelper _out;
    public HordeRelayTests(ITestOutputHelper o) { _out = o; }

    static void Pump(IEnumerable<HordeBot> bots, Func<bool> until, int maxMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!until() && sw.ElapsedMilliseconds < maxMs) { foreach (var b in bots) b.Update(); Thread.Sleep(5); }
    }

    [Fact]
    public async Task Bots_start_join_decline_join_late_level_change_jobs_fall_rise_and_see_the_results()
    {
        var content = HordeTestContent.Build();
        content.Rules.FinalBossSeconds = 15;
        var log = new List<string>();
        var horde = new HordeService(content, new HordeSettings { RunSeconds = 150, InviteSeconds = 5, Towns = { "prontera", "izlude" } }) { TickMs = 10 };
        horde.Log += l => { lock (log) log.Add(l); };
        var relay = new PoseRelay(0) { Horde = horde };
        relay.Log += l => { lock (log) log.Add(l); };
        horde.Transport = relay;
        relay.Start(IPAddress.Loopback);
        horde.Start();
        try
        {
            using var a = new HordeBot("127.0.0.1", relay.Port, 2000001, "Alpha", content);
            using var b = new HordeBot("127.0.0.1", relay.Port, 2000002, "Bravo", content, 2);
            using var c = new HordeBot("127.0.0.1", relay.Port, 2000003, "Charlie", content) { AutoJoin = false };
            var bots = new List<HordeBot> { a, b, c };
            c.Client.Invited += inv => c.Client.Answer(inv.RunId, false);
            Pump(bots, () => bots.All(x => x.Client.LinkState == HordeLinkState.Ready), 5000);
            Assert.All(bots, x => Assert.Equal(HordeLinkState.Ready, x.Client.LinkState));
            Assert.Contains("prontera", a.Client.Status.Towns);
            a.Client.RequestStart();
            Pump(bots, () => a.Client.InRun && b.Client.InRun, 8000);
            Assert.True(a.Client.InRun); Assert.True(b.Client.InRun); Assert.False(c.Client.InRun);
            Assert.Equal(1, b.Invites); Assert.Equal(1, c.Invites); Assert.Equal(0, a.Invites);
            // a late join: a fourth bot connects and joins the run in progress from the panel
            Pump(bots, () => a.Client.World.Seconds > 10, 8000);
            using var d = new HordeBot("127.0.0.1", relay.Port, 2000004, "Delta", content);
            bots.Add(d);
            Pump(bots, () => d.Client.LinkState == HordeLinkState.Ready && d.Client.Status.Phase == HordePhase.Running, 5000);
            d.Client.Answer(d.Client.Status.RunId, true);
            Pump(bots, () => d.Client.InRun, 5000);
            Assert.True(d.Client.InRun);
            Assert.True(d.Client.Run!.Late);
            // Bravo stands still for a while (it falls), then walks again (it rises when an ally stands)
            Pump(bots, () => a.Client.World.Seconds > 40, 30000);
            b.Idle = true;
            Pump(bots, () => b.Falls > 0 || a.Results != null, 60000);
            b.Idle = false;
            Pump(bots, () => a.Results != null && b.Results != null && d.Results != null, 120000);
            Assert.NotNull(a.Results); Assert.NotNull(b.Results); Assert.NotNull(d.Results);
            Assert.Null(c.Results);
            var r = a.Results!;
            foreach (var p in r.Players) _out.WriteLine($"{p.Name}: lv {p.Level} job {p.JobId} kills {p.Kills} falls {p.Falls} revives {p.Revives} score {p.Score} {p.Seconds:0}s share {p.BaseShare}/{p.JobShare} {p.Reward}");
            foreach (var x in bots) _out.WriteLine($"bot {x.Name}: invites {x.Invites} levelups {x.LevelUps} jobs {x.JobChanges} falls {x.Falls} rises {x.Rises} offers {x.OffersTaken} snaps {x.Snapshots} refused {x.Client.SnapshotsRefused} corrections {x.Client.Corrections} (max {x.Client.LargestCorrection:0.00} cells) bytes in {x.BytesIn}");
            _out.WriteLine($"service: snapshots {horde.SnapshotsSent}, {horde.SnapshotBytesSent / Math.Max(1, horde.SnapshotsSent)} bytes each, keys {horde.KeysSent}, skipped {horde.SnapshotsSkipped}; ticks {horde.TicksRun} mean {horde.TickMsTotal / Math.Max(1, horde.TicksRun):0.000} ms max {horde.TickMsMax:0.00} ms");
            Assert.Equal(3, r.Players.Count);
            Assert.True(a.LevelUps > 3 && a.JobChanges >= 1, "levels and a job change");
            Assert.True(b.Falls >= 1, "Bravo fell");
            Assert.True(r.Players.Sum(p => p.Kills) > 50);
            Assert.All(bots.Where(x => x != c), x => Assert.Equal(0, x.Client.SnapshotsRefused));
            Assert.All(r.Players, p => Assert.Equal(HordeRewardState.None, p.Reward)); // no reward sink: nothing promised
            Pump(bots, () => a.Client.Status.Phase == HordePhase.Idle, 3000);
            Assert.Equal(HordePhase.Idle, a.Client.Status.Phase);
        }
        finally
        {
            horde.Stop();
            await relay.StopAsync();
            lock (log) foreach (var l in log.Where(l => l.Contains("[horde]") || l.Contains("Horde"))) _out.WriteLine(l);
        }
    }

    [Fact]
    public async Task A_new_client_on_a_relay_without_Horde_is_told_so_and_keeps_its_link()
    {
        // a relay as client wave 7 left it (no Horde service: no capability bit): the client says nothing of Horde
        var relay = new PoseRelay(0);
        var log = new List<string>();
        relay.Log += l => { lock (log) log.Add(l); };
        relay.Start(IPAddress.Loopback);
        try
        {
            using var bot = new HordeBot("127.0.0.1", relay.Port, 3100001, "New", HordeTestContent.Build());
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (bot.Client.LinkState == HordeLinkState.NoLink && sw.ElapsedMilliseconds < 4000) { bot.Update(); Thread.Sleep(5); }
            Assert.Equal(HordeLinkState.OldHost, bot.Client.LinkState);
            Assert.Equal(PresenceWire.CapVoice, bot.WelcomeCaps);
            bot.Client.RequestStart(); // ignored: nothing is sent to a relay that does not know Horde
            for (int i = 0; i < 50; i++) { bot.Update(); Thread.Sleep(10); }
            Assert.Single(relay.Players());
            lock (log) Assert.DoesNotContain(log, l => l.Contains("unknown message") || l.Contains("disconnected"));
        }
        finally { await relay.StopAsync(); }
    }

    [Fact]
    public async Task Old_clients_and_clients_that_never_say_H_HELLO_get_nothing_of_Horde()
    {
        var content = HordeTestContent.Build();
        var horde = new HordeService(content, new HordeSettings { InviteSeconds = 2 }) { TickMs = 10 };
        var relay = new PoseRelay(0) { Horde = horde };
        horde.Transport = relay;
        relay.Start(IPAddress.Loopback);
        horde.Start();
        try
        {
            // a version-1 client (before wave 6): WELCOME 1, exactly as before
            using var old = new TcpClient(); old.Connect(IPAddress.Loopback, relay.Port);
            var os = old.GetStream();
            os.Write(PresenceWire.EncodeHello(3000001, "Old", PresenceWire.Version));
            // a wave-7 client (version 2, never says H_HELLO)
            using var w7 = new TcpClient(); w7.Connect(IPAddress.Loopback, relay.Port);
            var ws = w7.GetStream();
            ws.Write(PresenceWire.EncodeHello(3000002, "Seven", PresenceWire.VersionVoice));
            using var bot = new HordeBot("127.0.0.1", relay.Port, 3000003, "Bot", content);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (bot.Client.LinkState != HordeLinkState.Ready && sw.ElapsedMilliseconds < 4000) { bot.Update(); Thread.Sleep(5); }
            bot.Client.RequestStart();
            while (!bot.Client.InRun && sw.ElapsedMilliseconds < 8000) { bot.Update(); Thread.Sleep(5); }
            Assert.True(bot.Client.InRun);
            for (int i = 0; i < 100; i++) { bot.Update(); Thread.Sleep(10); }
            static byte[] Read(NetworkStream s, TcpClient t) { var b = new byte[65536]; int n = 0; while (t.Client.Poll(200000, SelectMode.SelectRead)) { int k = s.Read(b, n, b.Length - n); if (k <= 0) break; n += k; } return b[..n]; }
            var got1 = Read(os, old); var got2 = Read(ws, w7);
            // old: exactly the WELCOME 1 frame, nothing else
            Assert.Equal(PresenceWire.EncodeWelcome(), got1);
            // wave 7: WELCOME 2 with the capability byte (voice | horde), then nothing
            Assert.Equal(new byte[] { 7, 0, PresenceWire.Welcome, (byte)'L', (byte)'K', (byte)'P', (byte)'R', 2, PresenceWire.CapVoice | HordeWire.CapHorde }, got2);
            Assert.True(bot.Snapshots > 5);
        }
        finally { horde.Stop(); await relay.StopAsync(); }
    }

    /// <summary>Client wave 14 (w14play), point 31: the reward decisions written down (Loki Host: HordeRewardDb), as rows.</summary>
    sealed class RecordedRewards : IHordeRewards
    {
        public readonly List<HordeRewardRow> Rows = new();
        public Task<bool> RecordAsync(HordeRewardRow row) { lock (Rows) Rows.Add(row); return Task.FromResult(true); }
        public Task<List<HordeRewardRow>> TakeGrantedAsync() => Task.FromResult(new List<HordeRewardRow>());
    }

    /// <summary>Client wave 14 (w14play), point 31 of the design's headset list: Loki Host's presence log of 2026-10-07 22:12 listed Tester
    /// twice in one run ("Tester lv 5 ... (left); Tester lv 13 ... (left)") and wrote both rewards. Through the real relay: a bot leaves
    /// the run (H_LEAVE), answers its invitation again (H_ANSWER join: a late join in a new slot) and plays to the end; the results list
    /// it once and ONE reward row is written for it.</summary>
    [Fact]
    public async Task A_player_who_leaves_and_joins_again_is_listed_once_and_rewarded_once()
    {
        var content = HordeTestContent.Build();
        content.Rules.FinalBossSeconds = 100000; // (no final boss: the run ends by its time)
        var log = new List<string>();
        var rewards = new RecordedRewards();
        var horde = new HordeService(content, new HordeSettings { RunSeconds = 150, InviteSeconds = 3, Towns = { "prontera" } }) { TickMs = 5, Rewards = rewards };
        horde.Log += l => { lock (log) log.Add(l); };
        var relay = new PoseRelay(0) { Horde = horde };
        horde.Transport = relay;
        relay.Start(IPAddress.Loopback);
        horde.Start();
        try
        {
            using var a = new HordeBot("127.0.0.1", relay.Port, 2000011, "Tester", content);
            using var b = new HordeBot("127.0.0.1", relay.Port, 2000012, "Friend", content, 2);
            var bots = new List<HordeBot> { a, b };
            Pump(bots, () => bots.All(x => x.Client.LinkState == HordeLinkState.Ready), 5000);
            a.Client.RequestStart();
            Pump(bots, () => a.Client.InRun && b.Client.InRun, 8000);
            Assert.True(a.Client.InRun && b.Client.InRun);
            uint runId = a.Client.Status.RunId;
            Pump(bots, () => a.Client.World.Seconds > 65, 60000);
            a.Client.LeaveRun();
            Pump(bots, () => a.Results != null, 5000);
            Assert.NotNull(a.Results); // (the leaver's own results, sent at once)
            a.Results = null;
            Pump(bots, () => false, 1500);
            a.Client.Answer(runId, true);
            Pump(bots, () => a.Client.InRun, 5000);
            Assert.True(a.Client.InRun);
            Assert.True(a.Client.Run!.Late);
            Pump(bots, () => a.Results != null && b.Results != null, 120000);
            Assert.NotNull(a.Results); Assert.NotNull(b.Results);
            Pump(bots, () => { lock (rewards.Rows) return rewards.Rows.Count >= 2; }, 5000);
            Pump(bots, () => false, 500); // (a second row for the same player would have been written by now)
            var r = b.Results!;
            foreach (var p in r.Players) _out.WriteLine($"{p.Name} (aid {p.Aid}, slot {p.Slot}): lv {p.Level} kills {p.Kills} {p.Seconds:0}s score {p.Score} share {p.BaseShare}/{p.JobShare} {p.Reward} left {p.Left}");
            lock (rewards.Rows) foreach (var w in rewards.Rows) _out.WriteLine($"reward row: run {w.RunId} {w.CharName} (AID {w.AccountId}) {w.BaseShare}/{w.JobShare}");
            Assert.Equal(2, r.Players.Count);
            Assert.Single(r.Players, p => p.Aid == a.Aid);
            Assert.True(r.Players.Single(p => p.Aid == a.Aid).Seconds > 120f, "both stays of the leaver counted");
            lock (rewards.Rows)
            {
                Assert.Single(rewards.Rows, w => w.AccountId == a.Aid);
                Assert.Single(rewards.Rows, w => w.AccountId == b.Aid);
            }
        }
        finally
        {
            horde.Stop();
            await relay.StopAsync();
            lock (log) foreach (var l in log.Where(l => l.Contains("[horde]") || l.Contains("Horde"))) _out.WriteLine(l);
        }
    }

    /// <summary>Client wave 14 (w14kit), point 15 of the design's headset list ("a button that ends the run completely if I've already left
    /// the run"; answer 12: it ends the run for everyone, each with the EXP so far). Through the real relay: Tester leaves a run that
    /// Friend still plays, then sends H_END: the run ends at once for both (results to both, "stopped", long before its time), BOTH get
    /// a reward row (the EXP so far), the phase is Idle (the run can no longer be joined: a late H_ANSWER does nothing). Before that, a
    /// third bot that declined the invitation sends H_END and nothing happens; an H_END with another run id does nothing either.</summary>
    [Fact]
    public async Task A_player_who_left_ends_the_run_for_everyone_with_the_exp_so_far()
    {
        var content = HordeTestContent.Build();
        content.Rules.FinalBossSeconds = 100000; // (no final boss)
        content.Rules.StartHp = 1000000; // (nobody falls: the run must still be going when it is ended)
        content.Reward.MinSeconds = 10f; // (a reward from 10 s in the run: the test waits 15)
        var log = new List<string>();
        var rewards = new RecordedRewards();
        var horde = new HordeService(content, new HordeSettings { RunSeconds = 900, InviteSeconds = 3, Towns = { "prontera" } }) { TickMs = 5, Rewards = rewards };
        horde.Log += l => { lock (log) log.Add(l); };
        var relay = new PoseRelay(0) { Horde = horde };
        horde.Transport = relay;
        relay.Start(IPAddress.Loopback);
        horde.Start();
        try
        {
            using var a = new HordeBot("127.0.0.1", relay.Port, 2000021, "Tester", content);
            using var b = new HordeBot("127.0.0.1", relay.Port, 2000022, "Friend", content, 2);
            using var c = new HordeBot("127.0.0.1", relay.Port, 2000023, "Bystander", content) { AutoJoin = false };
            var bots = new List<HordeBot> { a, b, c };
            Pump(bots, () => bots.All(x => x.Client.LinkState == HordeLinkState.Ready), 5000);
            a.Client.RequestStart();
            Pump(bots, () => a.Client.InRun && b.Client.InRun, 8000);
            Assert.True(a.Client.InRun && b.Client.InRun);
            Assert.False(c.Client.InRun);
            uint runId = a.Client.Status.RunId;
            Pump(bots, () => a.Client.World.Seconds > 15, 60000); // past the reward's MinSeconds (10 s here)
            a.Client.LeaveRun();
            Pump(bots, () => a.Results != null, 5000);
            Assert.NotNull(a.Results);
            a.Results = null;
            Pump(bots, () => false, 800);
            Assert.True(b.Client.InRun, "the run goes on after the leave");
            // not a player of this run: ignored; another run id: ignored
            c.Client.EndRun(runId);
            a.Client.EndRun(runId + 7);
            Pump(bots, () => b.Results != null, 1500);
            Assert.Null(b.Results);
            Assert.True(b.Client.InRun, "H_END from a bystander / for another run changes nothing");
            float secondsAtEnd = b.Client.World.Seconds;
            a.Client.EndRun(runId);
            Pump(bots, () => a.Results != null && b.Results != null, 5000);
            Assert.NotNull(a.Results); Assert.NotNull(b.Results);
            Assert.False(b.Client.InRun);
            Assert.Equal(HordeOutcome.Stopped, b.Results!.Outcome);
            Assert.True(b.Results.Seconds < secondsAtEnd + 30f, $"ended at once ({b.Results.Seconds:0} s, asked at {secondsAtEnd:0} s), not by its time (900 s)");
            Pump(bots, () => { lock (rewards.Rows) return rewards.Rows.Count >= 2; }, 5000);
            foreach (var p in b.Results.Players) _out.WriteLine($"{p.Name} (aid {p.Aid}): lv {p.Level} {p.Seconds:0}s score {p.Score} share {p.BaseShare}/{p.JobShare} {p.Reward} left {p.Left}");
            lock (rewards.Rows)
            {
                foreach (var w in rewards.Rows) _out.WriteLine($"reward row: run {w.RunId} {w.CharName} (AID {w.AccountId}) {w.BaseShare}/{w.JobShare}");
                Assert.Single(rewards.Rows, w => w.AccountId == a.Aid && w.BaseShare > 0);
                Assert.Single(rewards.Rows, w => w.AccountId == b.Aid && w.BaseShare > 0);
                Assert.DoesNotContain(rewards.Rows, w => w.AccountId == c.Aid);
            }
            Pump(bots, () => a.Client.Status.Phase == HordePhase.Idle, 3000);
            Assert.Equal(HordePhase.Idle, a.Client.Status.Phase);
            a.Client.Answer(runId, true);
            Pump(bots, () => a.Client.InRun, 1500);
            Assert.False(a.Client.InRun, "an ended run cannot be joined");
            lock (log) Assert.Contains(log, l => l.Contains($"ended run {runId} for everyone"));
        }
        finally
        {
            horde.Stop();
            await relay.StopAsync();
            lock (log) foreach (var l in log.Where(l => l.Contains("[horde]") || l.Contains("Horde"))) _out.WriteLine(l);
        }
    }

    /// <summary>Client wave 15 (w15hfx), point 19 of the design's wave 14 list: through the real relay, Tester pauses the run from the Horde
    /// menu (H_PAUSE): both clients see it paused, Tester marked as the holder, the clock stands; Friend resumes it (anyone may) and
    /// the clock runs again.
    /// REWRITTEN client wave 18 (h18rules), the design's point 23: with two players the pause comes after a 3 s countdown that both clients see
    /// (who asked, the seconds left) while the clock still runs; Pause pressed again cancels it (no pause follows); Friend's resume counts
    /// down too, the clock standing meanwhile. The host's log names each request. (The host ticks 5 times faster here: 3 s = 0.6 s.)</summary>
    [Fact]
    public async Task A_player_pauses_the_run_for_everyone_and_another_resumes_it()
    {
        var content = HordeTestContent.Build();
        content.Rules.FinalBossSeconds = 100000; content.Rules.StartHp = 1000000;
        var log = new List<string>();
        var horde = new HordeService(content, new HordeSettings { RunSeconds = 900, InviteSeconds = 3, Towns = { "prontera" } }) { TickMs = 5 };
        horde.Log += l => { lock (log) log.Add(l); };
        var relay = new PoseRelay(0) { Horde = horde };
        horde.Transport = relay;
        relay.Start(IPAddress.Loopback);
        horde.Start();
        try
        {
            using var a = new HordeBot("127.0.0.1", relay.Port, 2000031, "Tester", content);
            using var b = new HordeBot("127.0.0.1", relay.Port, 2000032, "Friend", content, 2);
            var bots = new List<HordeBot> { a, b };
            Pump(bots, () => bots.All(x => x.Client.LinkState == HordeLinkState.Ready), 5000);
            a.Client.RequestStart();
            Pump(bots, () => a.Client.InRun && b.Client.InRun, 8000);
            Assert.True(a.Client.InRun && b.Client.InRun);
            Pump(bots, () => a.Client.World.Seconds > 3 && !a.Client.World.Paused, 20000);
            int aSlot = a.Client.MySlot, bSlot = b.Client.MySlot;
            // Pause pressed twice: the countdown starts and is cancelled; no pause follows
            a.Client.Pause(true); a.Client.Pause(true);
            float c0 = b.Client.World.Seconds;
            Pump(bots, () => b.Client.World.Seconds > c0 + HordeSim.PauseCountSeconds + 1f, 5000);
            Assert.False(b.Client.World.Paused, "a cancelled countdown pauses nothing");
            Assert.Equal(-1, b.Client.World.PauseCountBy);
            // Pause: Friend sees Tester's countdown while the clock runs, then the pause
            a.Client.Pause(true);
            Pump(bots, () => b.Client.World.PauseCountBy == aSlot, 3000);
            Assert.Equal(aSlot, b.Client.World.PauseCountBy); Assert.True(b.Client.World.PauseCountToPause, "a countdown to pause");
            Assert.InRange(b.Client.World.PauseCountSeconds, 0.05f, HordeSim.PauseCountSeconds);
            Assert.False(b.Client.World.Paused, "the countdown has not paused the run yet");
            float s0 = b.Client.World.Seconds;
            Pump(bots, () => b.Client.World.Paused && (b.Client.World.Players[aSlot].Flags & HordePlayerFlags.Held) != 0, 3000);
            Assert.True(b.Client.World.Paused, "Friend sees the run paused");
            Assert.True(b.Client.World.Seconds > s0 + 1f, $"the clock ran during the countdown ({s0:0.0} -> {b.Client.World.Seconds:0.0})");
            Assert.True((b.Client.World.Players[aSlot].Flags & HordePlayerFlags.Held) != 0, "Tester is marked as the holder");
            Assert.Equal(-1, b.Client.World.PauseCountBy);
            Pump(bots, () => a.Client.World.Paused, 2000);
            Assert.True(a.Client.World.Paused);
            float t0 = b.Client.World.Seconds;
            Pump(bots, () => false, 700);
            Assert.Equal(t0, b.Client.World.Seconds); // the clock stands
            b.Client.Pause(false); // anyone resumes: a countdown, the clock still standing
            Pump(bots, () => a.Client.World.PauseCountBy == bSlot, 3000);
            Assert.Equal(bSlot, a.Client.World.PauseCountBy); Assert.False(a.Client.World.PauseCountToPause, "a countdown to resume");
            Assert.Equal(t0, a.Client.World.Seconds);
            Pump(bots, () => b.Client.World.Seconds > t0 + 1f, 5000);
            Assert.True(b.Client.World.Seconds > t0 + 1f, "the run goes on after Friend's resume");
            Assert.True((b.Client.World.Players[aSlot].Flags & HordePlayerFlags.Held) == 0);
            lock (log)
            {
                Assert.Contains(log, l => l.Contains("Tester") && l.Contains("asked to pause"));
                Assert.Contains(log, l => l.Contains("Tester") && l.Contains("cancelled the countdown"));
                Assert.Contains(log, l => l.Contains("Friend") && l.Contains("asked to resume"));
            }
        }
        finally
        {
            horde.Stop();
            await relay.StopAsync();
            lock (log) foreach (var l in log.Where(l => l.Contains("[horde]") || l.Contains("Horde"))) _out.WriteLine(l);
        }
    }
}
