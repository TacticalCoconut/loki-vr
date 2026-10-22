using System.Net;
using Loki.Horde;
using Loki.Horde.Net;
using Loki.Net;
using LokiHost.Core;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>
/// Client wave 18 (h18rules), the design's point 38: "for now, artificially insert a maximum limitation of four players at the same time
/// [in a Horde run] ... allow the player who is starting the run to choose who is being invited." Through the real relay: the host lists
/// who could be invited (H_STATUS), the starter's chosen players alone are invited (H_START's list), a run seats at most
/// HordeLimits.MaxRunPlayers (4), a fifth is refused, a seat stays its player's (point 25's rejoin), and a client from before (H_START with
/// no list) still invites everyone, seated up to the cap.
/// </summary>
public class HordeW18PartyTests
{
    readonly ITestOutputHelper _out;
    public HordeW18PartyTests(ITestOutputHelper o) { _out = o; }

    static void Pump(IEnumerable<HordeBot> bots, Func<bool> until, int maxMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!until() && sw.ElapsedMilliseconds < maxMs) { foreach (var b in bots) b.Update(); Thread.Sleep(5); }
        foreach (var b in bots) b.Update();
    }

    [Fact]
    public void The_cap_is_four()
    {
        Assert.Equal(4, HordeLimits.MaxRunPlayers);
        // H_START's list on the wire; a start with no list (a client from before) names nobody
        var f = HordeWire.EncodeStart(new uint[] { 7, 8, 9 });
        var m = HordeWire.DecodeClient(f, 2, f.Length - 2);
        Assert.Equal(HordeWire.HStart, m.Kind); Assert.Equal(new uint[] { 7, 8, 9 }, m.Invite);
        var f0 = HordeWire.EncodeStart(new uint[0]); var m0 = HordeWire.DecodeClient(f0, 2, f0.Length - 2);
        Assert.Equal(HordeWire.HStart, m0.Kind); Assert.Empty(m0.Invite!);
        var fo = HordeWire.EncodeStart(); var mo = HordeWire.DecodeClient(fo, 2, fo.Length - 2);
        Assert.Equal(HordeWire.HStart, mo.Kind); Assert.Null(mo.Invite);
        // H_STATUS's list of who could be invited
        var st = new HordeStatus { Enabled = true }; st.Online.Add(new HordeMember { Aid = 5, Name = "Ana" }); st.Online.Add(new HordeMember { Aid = 6, Name = "Bo" });
        var sf = HordeWire.EncodeStatus(st, true); var back = new HordeStatus();
        Assert.True(HordeWire.DecodeStatus(sf, 2, sf.Length - 2, back, out bool ok)); Assert.True(ok);
        Assert.Equal(new[] { "Ana", "Bo" }, back.Online.Select(x => x.Name).ToArray()); Assert.Equal(HordeWire.Version, back.Protocol);
    }

    [Fact]
    public async Task Only_the_chosen_are_invited_four_seats_a_fifth_refused_and_a_seat_stays_its_players()
    {
        var content = HordeTestContent.Build();
        content.Rules.FinalBossSeconds = 100000; content.Rules.StartHp = 1000000;
        var log = new List<string>();
        var horde = new HordeService(content, new HordeSettings { RunSeconds = 900, InviteSeconds = 4, Towns = { "prontera" } }) { TickMs = 5 };
        horde.Log += l => { lock (log) log.Add(l); };
        var relay = new PoseRelay(0) { Horde = horde };
        horde.Transport = relay;
        relay.Start(IPAddress.Loopback);
        horde.Start();
        try
        {
            var bots = new List<HordeBot>();
            for (int i = 0; i < 6; i++) bots.Add(new HordeBot("127.0.0.1", relay.Port, (uint)(2001001 + i), "P" + i, content, (byte)(i % 2 + 1)));
            var (a, b, c, d, e, g) = (bots[0], bots[1], bots[2], bots[3], bots[4], bots[5]);
            Pump(bots, () => bots.All(x => x.Client.LinkState == HordeLinkState.Ready) && a.Client.Status.Online.Count == 6, 6000);
            Assert.All(bots, x => Assert.Equal(HordeLinkState.Ready, x.Client.LinkState));
            Assert.Equal(6, a.Client.Status.Online.Count); // everyone the host could invite, the starter too (its client leaves itself out)
            // 1. the starter names b, c, d and e: only the first three are invited (four seats with the starter); g is never asked
            a.Client.RequestStart(new[] { b.Aid, c.Aid, d.Aid, e.Aid });
            Pump(bots, () => a.Client.InRun && b.Client.InRun && c.Client.InRun && d.Client.InRun, 10000);
            _out.WriteLine("invites: " + string.Join(", ", bots.Select(x => $"{x.Name} {x.Invites}")));
            Assert.True(a.Client.InRun && b.Client.InRun && c.Client.InRun && d.Client.InRun);
            Assert.Equal(new[] { 0, 1, 1, 1, 0, 0 }, bots.Select(x => x.Invites).ToArray());
            Assert.False(e.Client.InRun); Assert.False(g.Client.InRun);
            // 2. a fifth, invited or not, is refused: g never asked, e named fifth
            g.Client.Answer(a.Client.Status.RunId, true); e.Client.Answer(a.Client.Status.RunId, true);
            Pump(bots, () => false, 1200);
            Assert.False(g.Client.InRun); Assert.False(e.Client.InRun);
            Assert.Equal(4, a.Client.World.Players.Count(p => p.Present));
            lock (log) { Assert.Contains(log, l => l.Contains("P5") && l.Contains("not invited")); Assert.Contains(log, l => l.Contains("P4") && l.Contains("not invited")); }
            // 3. c leaves: its seat stays its own: the run is still full for others, c comes back (point 25: with its build)
            c.Client.LeaveRun();
            Pump(bots, () => !c.Client.InRun && a.Client.World.Players.Count(p => p.Present) == 3, 3000);
            Assert.Equal(3, a.Client.World.Players.Count(p => p.Present));
            g.Client.Answer(a.Client.Status.RunId, true);
            Pump(bots, () => false, 800);
            Assert.False(g.Client.InRun);
            c.Results = null; c.Client.Answer(a.Client.Status.RunId, true);
            Pump(bots, () => c.Client.InRun, 4000);
            Assert.True(c.Client.InRun, "a player who left its seat takes it back");
            Assert.Equal(4, a.Client.World.Players.Count(p => p.Present));
            lock (log) Assert.Contains(log, l => l.Contains("P2") && l.Contains("is back in"));
            a.Client.EndRun(a.Client.Status.RunId);
            Pump(bots, () => a.Client.Status.Phase == HordePhase.Idle, 4000);
            // 4. alone: an empty list asks nobody, the run begins at once
            int[] inv0 = bots.Select(x => x.Invites).ToArray();
            b.Client.RequestStart(new uint[0]);
            Pump(bots, () => b.Client.InRun, 4000);
            Assert.True(b.Client.InRun);
            Assert.Equal(inv0, bots.Select(x => x.Invites).ToArray());
            b.Client.EndRun(b.Client.Status.RunId);
            Pump(bots, () => a.Client.Status.Phase == HordePhase.Idle, 4000);
            // 5. a client from before (no list): everyone is invited, four are seated, the fifth to answer is refused
            foreach (var x in bots) x.Results = null;
            a.Client.RequestStart();
            Pump(bots, () => a.Client.Status.Phase == HordePhase.Running && bots.Count(x => x.Client.InRun) >= 4, 10000);
            Pump(bots, () => false, 600);
            _out.WriteLine("everyone asked: in the run " + string.Join(", ", bots.Where(x => x.Client.InRun).Select(x => x.Name)));
            Assert.Equal(4, bots.Count(x => x.Client.InRun));
            Assert.Equal(4, a.Client.World.Players.Count(p => p.Present));
            lock (log) Assert.Contains(log, l => l.Contains("it is full (4 players)"));
            // a seated player leaves: an invited one still outside is refused (the seat is kept), the leaver takes it back
            var leaver = bots.First(x => x.Client.InRun && x != a); var outside = bots.First(x => !x.Client.InRun);
            leaver.Client.LeaveRun();
            Pump(bots, () => !leaver.Client.InRun && a.Client.World.Players.Count(p => p.Present) == 3, 3000);
            int full0; lock (log) full0 = log.Count(l => l.Contains(outside.Name) && l.Contains("it is full"));
            outside.Client.Answer(a.Client.Status.RunId, true);
            Pump(bots, () => false, 1000);
            Assert.False(outside.Client.InRun, "the leaver's seat is kept for it");
            lock (log) Assert.True(log.Count(l => l.Contains(outside.Name) && l.Contains("it is full")) > full0);
            leaver.Client.Answer(a.Client.Status.RunId, true);
            Pump(bots, () => leaver.Client.InRun, 4000);
            Assert.True(leaver.Client.InRun);
            Assert.Equal(4, a.Client.World.Players.Count(p => p.Present));
            foreach (var x in bots) x.Dispose();
        }
        finally
        {
            horde.Stop();
            await relay.StopAsync();
            lock (log) foreach (var l in log.Where(l => l.Contains("[horde]"))) _out.WriteLine(l);
        }
    }
}
