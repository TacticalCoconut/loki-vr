using System.Diagnostics;
using Loki.Horde;
using Loki.Horde.Content;
using Loki.Horde.Net;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>Client wave 8 (w8core): what a full run costs: 8 players, the field kept at 300 enemies, snapshots at 10 a second
/// (bytes per client per second, the largest snapshot, a key's size) and the tick's time.</summary>
public class HordeMeasureTests
{
    readonly ITestOutputHelper _out;
    public HordeMeasureTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Eight_players_and_300_enemies_fit_the_wire_and_the_tick()
    {
        foreach (var (name, c) in new[] { ("game tables", HordeTables.Build()), ("test tables", HordeTestContent.Build()) })
        {
            var sim = new HordeSim(c, c.Arenas[0], 11, 900);
            for (int i = 0; i < 8; i++) sim.AddPlayer((uint)(2000000 + i), "bot" + i, (byte)(i % 2 + 1));
            var enc = new SnapshotEncoder(sim);
            var normal = c.Enemies.First(e => e.Rank == EnemyRank.Normal);
            var dec = new SnapshotDecoder();
            var view = new HordeWorldView();
            dec.Reset(new HordeRunInfo { X0 = sim.Arena.X0, Y0 = sim.Arena.Y0, X1 = sim.Arena.X1, Y1 = sim.Arena.Y1 });
            var rnd = new Random(5);
            long bytes = 0, snaps = 0, maxSnap = 0, keyBytes = 0; double tickMs = 0, tickMax = 0; int ticks = 0, enemiesMin = int.MaxValue;
            ushort seq = 0;
            var sw = new Stopwatch();
            for (int t = 0; t < HordeSim.TickRate * 90 && !sim.Over; t++)
            {
                // keep the field full (the waves do it too; this holds it at 300 whatever the tables' pace)
                while (sim.EnemiesAlive < 300)
                    sim.Spawn(normal, sim.MinX + rnd.Next(sim.MaxX - sim.MinX), sim.MinY + rnd.Next(sim.MaxY - sim.MinY), true);
                seq++;
                for (int i = 0; i < 8; i++) HordeTests.BotStep(sim, i, seq, i);
                sw.Restart(); sim.Step(); sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds; tickMs += ms; if (ms > tickMax && t > 20) tickMax = ms; ticks++;
                enc.Collect(); sim.ClearOutputs();
                if (sim.Paused) continue; // someone is choosing: the tick stood (the bots answer at their next step); not a second of the stream
                if (t % 2 == 1)
                {
                    bool key = t % 200 == 1;
                    enc.Encode(key, out var delta, out var k);
                    var f = SnapshotEncoder.Frame(0, delta)!;
                    if (t > 40) { bytes += f.Length; snaps++; maxSnap = Math.Max(maxSnap, f.Length); enemiesMin = Math.Min(enemiesMin, sim.EnemiesAlive); }
                    if (k != null) keyBytes = Math.Max(keyBytes, k.Length + 5);
                    // the client side applies the delta (or the first key) and must agree on every live enemy
                    Assert.True(dec.Apply(key && !dec.HaveKey ? k! : delta, 0, (key && !dec.HaveKey ? k! : delta).Length, view, 0, t / (double)HordeSim.TickRate), "snapshot applied");
                }
            }
            int alive = 0; foreach (var e in sim.Enemies) if (e.Alive) alive++;
            Assert.Equal(alive, view.EnemiesAlive);
            for (int i = 0; i < HordeLimits.MaxEnemies; i++)
                if (sim.Enemies[i].Alive)
                {
                    Assert.True(Math.Abs(sim.Enemies[i].X / 256f - view.Enemies[i].X) < 0.1f && Math.Abs(sim.Enemies[i].Y / 256f - view.Enemies[i].Y) < 0.1f, $"enemy {i} place");
                }
            double perSec = bytes / (snaps / (HordeSim.TickRate / 2.0));
            _out.WriteLine($"{name}: 8 players, >= {enemiesMin} enemies: snapshot mean {bytes / Math.Max(1, snaps)} bytes, largest {maxSnap}, key {keyBytes}; " +
                           $"{perSec / 1024:0.0} KB/s per client at {HordeSim.TickRate / 2} snapshots/s (+ TCP/IP headers ~0.5 KB/s); tick mean {tickMs / ticks:0.000} ms, max {tickMax:0.00} ms (of {1000 / HordeSim.TickRate})");
            Assert.True(perSec < 64 * 1024, "per-client stream under 64 KB/s");
            Assert.True(tickMs / ticks < 10, "tick under 10 ms on average");
        }
    }
}
