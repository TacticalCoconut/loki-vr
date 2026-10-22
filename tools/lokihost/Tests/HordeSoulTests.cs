using System.Reflection;
using Loki.Horde;
using Loki.Horde.Net;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>
/// Client wave 18 (h18feel), the design's point 35: soul retrieval (client/Assets/Loki/Runtime/Horde/Core/HordeSim.Souls.cs). The shard
/// count (4, +1 a player above 2, +1 a death, -2 deaths at a class change), where the shards land, the claim, the rise beside the
/// claimer invulnerable for 5 s and faster (both rises: the design's follow-up), the class-change level bringing every fallen player back, shared shards (the design's example exactly,
/// and three fallen players), +20 % enemies while shards lie, alone (no shards, the run ends), determinism and the snapshot's block.
/// </summary>
public class HordeSoulTests
{
    readonly ITestOutputHelper _out;
    public HordeSoulTests(ITestOutputHelper o) { _out = o; }

    static int T(double seconds) => HordeSim.Ticks(seconds);
    const int U = HordeSim.U;

    /// <summary>The engine tests' tables, no waves, harmless enemies, no objectives; players 12 cells apart round the arena's middle.</summary>
    static HordeSim Make(int players, out HordeContent c, ulong seed = 21, Action<HordeContent>? tune = null)
    {
        c = HordeTestContent.Build();
        c.Waves.Clear(); foreach (var e in c.Enemies) e.Damage = 0;
        c.Objectives.Enabled = false;
        tune?.Invoke(c);
        var sim = new HordeSim(c, c.Arenas[0], seed, 900);
        for (int i = 0; i < players; i++) sim.AddPlayer((uint)(7000000 + i), "p" + i);
        int cx = (sim.Arena.X0 + sim.Arena.X1 + 1) / 2 * U, cy = (sim.Arena.Y0 + sim.Arena.Y1 + 1) / 2 * U;
        for (int i = 0; i < players; i++) Put(sim.Player(i)!, cx + (int)(Math.Cos(i * 2.1) * 12 * U), cy + (int)(Math.Sin(i * 2.1) * 12 * U));
        Steps(sim, T(2.5)); // (past the joining's safety)
        return sim;
    }

    static void Put(SimPlayer p, int x, int y) { p.X = x; p.Y = y; p.Vx = p.Vy = 0; }

    /// <summary>Steps n ticks (every open offer answered with its first card), the events of each step to <paramref name="each"/>.</summary>
    static void Steps(HordeSim sim, int n, Action<HordeSim>? each = null)
    {
        for (int t = 0; t < n && !sim.Over; t++)
        {
            foreach (var p in sim.Players) if (p.Present && p.Open != null) sim.Choose(p.Slot, p.Open.Id, 0);
            sim.Step(); each?.Invoke(sim); sim.ClearOutputs();
        }
    }

    /// <summary>The player falls the engine's way: an enemy hitting very hard touches it (then it is gone). Returns its SoulSplit's count (-1 none).</summary>
    static int Fall(HordeSim sim, int slot, List<HordeEvent>? log = null)
    {
        var p = sim.Player(slot)!; int n = -1;
        int e = sim.Spawn(sim.C.Enemy(1)!, p.X + 40, p.Y, true);
        sim.Enemies[e].Damage = 1_000_000; sim.Enemies[e].Speed = 0;
        for (int t = 0; t < T(3) && !p.Fallen; t++)
        {
            foreach (var q in sim.Players) if (q.Present && q.Open != null) sim.Choose(q.Slot, q.Open.Id, 0);
            sim.Step();
            foreach (var ev in sim.Events) { if (ev.Kind == HordeEventKind.SoulSplit && ev.Slot == slot) n = ev.Value; log?.Add(ev); }
            sim.ClearOutputs();
        }
        sim.Enemies[e].Alive = false;
        Assert.True(p.Fallen, $"slot {slot} fell");
        return n;
    }

    /// <summary>The claimer stands in the shard until it is taken (its events to <paramref name="log"/>).</summary>
    static void Claim(HordeSim sim, int claimer, SimSoul s, List<HordeEvent>? log = null)
    {
        var p = sim.Player(claimer)!; Put(p, s.X, s.Y);
        for (int t = 0; t < T(4) && sim.Souls.Contains(s); t++)
        {
            foreach (var q in sim.Players) if (q.Present && q.Open != null) sim.Choose(q.Slot, q.Open.Id, 0);
            sim.Step(); if (log != null) log.AddRange(sim.Events); sim.ClearOutputs();
        }
        Assert.DoesNotContain(s, sim.Souls);
    }

    static int Mask(params int[] slots) { int m = 0; foreach (int s in slots) m |= 1 << s; return m; }
    static void GainExp(HordeSim sim, int exp) => typeof(HordeSim).GetMethod("GainExp", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(sim, new object[] { exp });

    // ------------------------------------------------------------------------------------------------ the count

    [Fact]
    public void The_count_is_4_plus_one_a_player_above_2_plus_one_a_death()
    {
        var r = new SoulRulesDef();
        Assert.Equal(4, r.Count(2, 0)); Assert.Equal(5, r.Count(3, 0)); Assert.Equal(6, r.Count(4, 0)); Assert.Equal(4, r.Count(1, 0));
        Assert.Equal(5, r.Count(2, 1)); Assert.Equal(7, r.Count(3, 2)); Assert.Equal(16, r.Count(8, 40)); // (MaxShards)
        // in the engine, for 2, 3 and 4 players: the first fall splits into 4 / 5 / 6 shards
        foreach (int n in new[] { 2, 3, 4 })
        {
            var sim = Make(n, out _);
            int got = Fall(sim, 1);
            Assert.Equal(4 + (n - 2), got); Assert.Equal(got, sim.Souls.Count); Assert.Equal(got, sim.SoulTotal[1]); Assert.Equal(got, sim.SoulNeed[1]);
            Assert.All(sim.Souls, s => Assert.Equal(Mask(1), s.Owners));
        }
    }

    [Fact]
    public void Each_death_adds_one_and_a_class_change_takes_two_off()
    {
        var sim = Make(2, out _);
        var counts = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            counts.Add(Fall(sim, 1));
            foreach (var s in sim.Souls.ToList()) if (sim.Souls.Contains(s)) Claim(sim, 0, s);
            Assert.False(sim.Player(1)!.Fallen);
            Steps(sim, T(11)); // (past the rise's invulnerability)
        }
        Assert.Equal(new[] { 4, 5, 6 }, counts);
        Assert.Equal(3, sim.SoulDeaths(sim.Player(1)!.Aid));
        // the party reaches the first class-change level (5): both change class; the deaths' part drops 3 → 1
        while (sim.PartyLevel < 5) GainExp(sim, sim.PartyExpToNext() - sim.PartyExp);
        Steps(sim, T(1));
        Assert.NotEqual(0, sim.Player(1)!.JobId);
        Assert.Equal(1, sim.SoulDeaths(sim.Player(1)!.Aid));
        Assert.Equal(5, Fall(sim, 1)); // 4 + 1
    }

    // ------------------------------------------------------------------------------------------------ where, the claim, the rise

    [Fact]
    public void Shards_land_12_to_20_cells_away_inside_the_arena_apart_and_the_same_every_time()
    {
        string Places(ulong seed)
        {
            var sim = Make(2, out var c, seed);
            var body = sim.Player(1)!;
            Fall(sim, 1);
            int margin = (int)(c.Souls.EdgeMarginCells * U);
            foreach (var s in sim.Souls)
            {
                double d = Math.Sqrt(HordeSim.Dist2(s.X, s.Y, body.X, body.Y)) / U;
                Assert.InRange(d, 11.99, 20.01);
                Assert.InRange(s.X, sim.MinX + margin, sim.MaxX - margin); Assert.InRange(s.Y, sim.MinY + margin, sim.MaxY - margin);
                foreach (var o in sim.Souls) if (o != s) Assert.True(Math.Sqrt(HordeSim.Dist2(s.X, s.Y, o.X, o.Y)) / U >= 2.99, "apart");
                Assert.Equal((int)(1.5 * U), s.Radius);
            }
            return string.Join(" ", sim.Souls.Select(s => $"{s.X},{s.Y}"));
        }
        string a = Places(21), b = Places(21), c2 = Places(22);
        _out.WriteLine(a);
        Assert.Equal(a, b); Assert.NotEqual(a, c2);
    }

    [Fact]
    public void A_shard_takes_1_5_s_enemies_slow_it_and_the_last_one_raises_the_player_beside_the_claimer_invulnerable_5_s()
    {
        var sim = Make(2, out var c);
        Fall(sim, 1);
        var pb = sim.Player(1)!; var pa = sim.Player(0)!;
        // the first: 1.5 s, alone in it
        var s0 = sim.Souls[0]; int t0 = sim.Tick; Claim(sim, 0, s0);
        Assert.InRange(sim.Tick - t0, T(1.5) - 1, T(1.5) + 1);
        Assert.Equal(3, sim.SoulNeed[1]); Assert.InRange(pb.Revive, 0.24, 0.26);
        // the second with two enemies inside: 1.5 s / 0.76
        var s1 = sim.Souls[0];
        for (int k = 0; k < 2; k++) { int e = sim.Spawn(c.Enemy(1)!, s1.X, s1.Y + (k == 0 ? U : -U), true); sim.Enemies[e].Speed = 0; sim.Enemies[e].Hp = sim.Enemies[e].MaxHp = 1_000_000; } // (1 cell from its middle, 2 apart: nothing pushes them out; the claimer's bolts do not kill them)
        t0 = sim.Tick; Claim(sim, 0, s1);
        Assert.InRange(sim.Tick - t0, (int)(T(1.5) / 0.76) - 1, (int)(T(1.5) / 0.76) + 2);
        foreach (var en in sim.Enemies) en.Alive = false;
        // the rest: the last one raises b beside a
        var log = new List<HordeEvent>();
        while (sim.Souls.Count > 0) Claim(sim, 0, sim.Souls[0], log);
        var rose = log.Single(e => e.Kind == HordeEventKind.Rose);
        Assert.Equal(1, rose.Slot); Assert.Equal(0, rose.Target); Assert.Equal(1, rose.Value);
        Assert.False(pb.Fallen);
        Assert.InRange(Math.Sqrt(HordeSim.Dist2(pa.X, pa.Y, pb.X, pb.Y)) / U, 0.9, 1.1);
        Assert.Equal((int)Math.Round(pb.MaxHp * 0.5), pb.Hp);
        Assert.Equal(1, pa.Revives); Assert.Equal(1, sim.SoulRevives);
        // invulnerable 5 s (the design's follow-up; it was 10): a hard hitter on it does nothing until then
        int hp = pb.Hp, rise = sim.Tick;
        int h = sim.Spawn(c.Enemy(1)!, pb.X + 30, pb.Y, true); sim.Enemies[h].Damage = 3; sim.Enemies[h].Speed = 0; sim.Enemies[h].Hp = sim.Enemies[h].MaxHp = 1_000_000;
        Steps(sim, T(4.9) - 1);
        Assert.Equal(hp, pb.Hp); Assert.Equal(rise + T(5), pb.SafeUntil);
        Steps(sim, T(1));
        Assert.True(pb.Hp < hp, "hurt after the 5 s");
    }

    /// <summary>design note after point 35: "A soul shard revive should give 5s, as should a class-change revive. During this time, the player
    /// also moves and attacks much faster." Both rises: 5 s unhurt, +60 % walking speed, every weapon's wait / 1.6; then as before.</summary>
    [Fact]
    public void Both_rises_give_5_s_unhurt_and_faster_walking_and_attacks_then_normal()
    {
        var waitOf = typeof(HordeSim).GetMethod("CooldownTicks", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(SimPlayer), typeof(OwnedWeapon), typeof(WeaponLevel) }, null)!;
        foreach (bool bySouls in new[] { true, false })
        {
            var sim = Make(2, out var c);
            foreach (var q in sim.Players) if (q.Present) q.Weapons.Clear(); // (no kills, so no level-up changes a speed meanwhile)
            var pb = sim.Player(1)!; var probe = new OwnedWeapon { Def = c.Weapon(1)!, Level = 1 };
            Fall(sim, 1);
            var log = new List<HordeEvent>();
            if (bySouls) while (sim.Souls.Count > 0) Claim(sim, 0, sim.Souls[0], log);
            else
            {
                while (sim.PartyLevel < 5) GainExp(sim, sim.PartyExpToNext() - sim.PartyExp);
                log.AddRange(sim.Events);
                // every card and the class choice answered (the run waits meanwhile: the 5 s have not begun to run out)
                for (int k = 0; k < 200 && sim.Players.Any(q => q.Present && (q.Open != null || q.Offers.Count > 0)); k++) { foreach (var q in sim.Players) if (q.Present && q.Open != null) sim.Choose(q.Slot, q.Open.Id, 0); sim.Step(); sim.ClearOutputs(); }
            }
            var rose = log.Single(e => e.Kind == HordeEventKind.Rose && e.Slot == 1);
            Assert.Equal(bySouls ? 1 : 2, rose.Value);
            Assert.Equal(rose.Tick + T(5), pb.SafeUntil);
            Assert.Contains(pb.Buffs, b => b.WeaponId == HordeSim.RiseBuffId && b.Until == rose.Tick + T(5));
            int speedOn = pb.SpeedPerTick, waitOn = (int)waitOf.Invoke(sim, new object[] { pb, probe, probe.Lv })!; double moveOn = pb.S.MovePct, cdOn = pb.S.CooldownPct;
            Steps(sim, pb.SafeUntil - sim.Tick + 2);
            int speedOff = pb.SpeedPerTick, waitOff = (int)waitOf.Invoke(sim, new object[] { pb, probe, probe.Lv })!;
            _out.WriteLine($"{(bySouls ? "soul shards" : "class-change level")}: unhurt 5 s; speed {speedOff} -> {speedOn} units a tick, a weapon's wait {waitOff} -> {waitOn} ticks; then as before");
            Assert.DoesNotContain(pb.Buffs, b => b.WeaponId == HordeSim.RiseBuffId);
            Assert.Equal(60, moveOn - pb.S.MovePct, 3); Assert.Equal(-37.5, cdOn - pb.S.CooldownPct, 3);
            Assert.InRange(speedOn / (double)speedOff, 1.5, 1.7); Assert.InRange(waitOn / (double)waitOff, 0.55, 0.7);
        }
    }

    [Fact]
    public void Alone_a_fall_makes_no_shards_and_ends_the_run()
    {
        var sim = Make(1, out _);
        var log = new List<HordeEvent>();
        var p = sim.Player(0)!; int e = sim.Spawn(sim.C.Enemy(1)!, p.X + 40, p.Y, true); sim.Enemies[e].Damage = 1_000_000;
        Steps(sim, T(3), s => log.AddRange(s.Events));
        Assert.True(sim.Over); Assert.Equal(HordeOutcome.Defeat, sim.Outcome);
        Assert.Empty(sim.Souls); Assert.DoesNotContain(log, x => x.Kind == HordeEventKind.SoulSplit);
    }

    [Fact]
    public void The_class_change_level_brings_every_fallen_player_back_and_their_shards_go()
    {
        var sim = Make(3, out _);
        Fall(sim, 1); Fall(sim, 2);
        Assert.True(sim.Souls.Count > 0);
        var at1 = (sim.Player(1)!.X, sim.Player(1)!.Y);
        var log = new List<HordeEvent>();
        while (sim.PartyLevel < 4) GainExp(sim, sim.PartyExpToNext() - sim.PartyExp);
        Assert.True(sim.Player(1)!.Fallen); // (level 4: not a class change)
        GainExp(sim, sim.PartyExpToNext() - sim.PartyExp); log.AddRange(sim.Events);
        Assert.Equal(5, sim.PartyLevel);
        Assert.False(sim.Player(1)!.Fallen); Assert.False(sim.Player(2)!.Fallen);
        Assert.Equal(at1, (sim.Player(1)!.X, sim.Player(1)!.Y));
        Assert.Equal(2, log.Count(e => e.Kind == HordeEventKind.Rose && e.Value == 2 && e.Target == -1));
        Assert.Empty(sim.Souls); Assert.Equal(0, sim.SoulTotal[1]); Assert.Equal(0, sim.SoulTotal[2]);
        Steps(sim, T(1)); // (its card choices of levels 2-5 and its class choice, each answered with the first card)
        Assert.NotEqual(0, sim.Player(1)!.JobId); Assert.NotEqual(0, sim.Player(2)!.JobId);
    }

    // ------------------------------------------------------------------------------------------------ shared shards

    [Fact]
    public void Shared_shards_example_A_drops_4_B_takes_2_C_drops_4_two_count_for_both()
    {
        // the design's numbers: four shards each (the example leaves out the +1 for a third player: PerPlayerAbove 0 here)
        var sim = Make(3, out _, 21, c => c.Souls.PerPlayerAbove = 0);
        const int A = 0, B = 1, C = 2;
        Assert.Equal(4, Fall(sim, A));
        Claim(sim, B, sim.Souls[0]); Claim(sim, B, sim.Souls[0]);
        Assert.Equal(2, sim.SoulNeed[A]);
        var aOld = sim.Souls.ToList();
        Assert.Equal(4, Fall(sim, C));
        var cNew = sim.Souls.Except(aOld).ToList();
        Assert.Equal(4, cNew.Count);
        Assert.Equal(2, cNew.Count(s => s.Owners == Mask(A, C))); Assert.Equal(2, cNew.Count(s => s.Owners == Mask(C)));
        Assert.All(aOld, s => Assert.Equal(Mask(A), s.Owners)); // A's older shards count only for A
        Assert.Equal(2, sim.SoulNeed[A]); Assert.Equal(4, sim.SoulNeed[C]);
        // B claims the two shared: A rises, C needs 2; A's older shards are gone (they counted for nobody fallen any more)
        var log = new List<HordeEvent>();
        foreach (var s in cNew.Where(s => s.Owners == Mask(A, C)).ToList()) Claim(sim, B, s, log);
        Assert.False(sim.Player(A)!.Fallen); Assert.True(sim.Player(C)!.Fallen);
        Assert.Contains(log, e => e.Kind == HordeEventKind.Rose && e.Slot == A && e.Target == B && e.Value == 1);
        Assert.Equal(2, sim.SoulNeed[C]); Assert.Equal(2, sim.Souls.Count); Assert.All(sim.Souls, s => Assert.Equal(Mask(C), s.Owners));
        foreach (var s in sim.Souls.ToList()) Claim(sim, B, s);
        Assert.False(sim.Player(C)!.Fallen);
    }

    [Fact]
    public void Three_fallen_players_a_shard_counts_for_each_who_still_needs_one()
    {
        var sim = Make(4, out _); // 6 shards each (4 players)
        const int A = 0, B = 1, C = 2, D = 3;
        Assert.Equal(6, Fall(sim, A));
        for (int k = 0; k < 3; k++) Claim(sim, D, sim.Souls[0]);
        Assert.Equal(3, sim.SoulNeed[A]);
        var before = sim.Souls.ToList();
        Assert.Equal(6, Fall(sim, B));
        var bNew = sim.Souls.Except(before).ToList();
        Assert.Equal(3, bNew.Count(s => s.Owners == Mask(A, B))); Assert.Equal(3, bNew.Count(s => s.Owners == Mask(B)));
        before = sim.Souls.ToList();
        Assert.Equal(6, Fall(sim, C));
        var cNew = sim.Souls.Except(before).ToList();
        // A needs 3, B needs 6: C's first 3 count for A, B and C, the other 3 for B and C
        Assert.Equal(3, cNew.Count(s => s.Owners == Mask(A, B, C))); Assert.Equal(3, cNew.Count(s => s.Owners == Mask(B, C)));
        // one shard for all three: each comes one closer
        Claim(sim, D, cNew.First(s => s.Owners == Mask(A, B, C)));
        Assert.Equal(2, sim.SoulNeed[A]); Assert.Equal(5, sim.SoulNeed[B]); Assert.Equal(5, sim.SoulNeed[C]);
        // the other two triple ones: A rises; B and C need 3 each
        foreach (var s in cNew.Where(s => s.Owners == Mask(A, B, C)).ToList()) if (sim.Souls.Contains(s)) Claim(sim, D, s);
        Assert.False(sim.Player(A)!.Fallen); Assert.Equal(3, sim.SoulNeed[B]); Assert.Equal(3, sim.SoulNeed[C]);
        Assert.All(sim.Souls, s => Assert.Equal(0, s.Owners & Mask(A)));
    }

    // ------------------------------------------------------------------------------------------------ +20 % enemies

    [Fact]
    public void While_shards_lie_the_waves_spawn_20_percent_more()
    {
        double PerPulse(bool fall)
        {
            var sim = Make(2, out var c, 21, cc =>
            {
                cc.Waves.Add(new WaveDef { EnemyId = 1, From = 0, To = 900, Interval = 1f, Count = 10, Pattern = SpawnPattern.Ring });
                cc.Rules.MaxEnemies = 10000;
            });
            foreach (var p in sim.Players) p.Weapons.Clear(); // (nothing is killed: every enemy that appears is counted, then taken away)
            if (fall) Fall(sim, 1);
            var alive = typeof(HordeSim).GetProperty("EnemiesAlive")!;
            void Clear() { foreach (var e in sim.Enemies) e.Alive = false; alive.SetValue(sim, 0); } // (the cap counts the living ones)
            Clear();
            int seen = 0, pulses = 0;
            for (int t = 0; t < T(20); t++)
            {
                sim.Step();
                int now = 0; foreach (var e in sim.Enemies) if (e.Alive) now++;
                if (now > 0) { seen += now; pulses++; }
                Clear(); sim.ClearOutputs();
            }
            Assert.Equal(fall, sim.Souls.Count > 0);
            _out.WriteLine($"{(fall ? "shards on the field" : "no shard")}: {seen} enemies in {pulses} pulses");
            return seen / (double)Math.Max(1, pulses);
        }
        double off = PerPulse(false), on = PerPulse(true);
        Assert.InRange(on / off, 1.17, 1.23);
    }

    // ------------------------------------------------------------------------------------------------ determinism, the wire

    [Fact]
    public void The_same_seed_plays_the_same_falls_shards_and_rises()
    {
        ulong Play()
        {
            var sim = Make(3, out _);
            Fall(sim, 1); Claim(sim, 0, sim.Souls[0]); Fall(sim, 2);
            while (sim.Souls.Count > 0) Claim(sim, 0, sim.Souls[0]);
            Steps(sim, T(2));
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void The_snapshot_carries_the_shards_whom_they_count_for_and_the_counts()
    {
        var sim = Make(3, out _, 21, c => c.Souls.PerPlayerAbove = 0);
        var enc = new SnapshotEncoder(sim); var dec = new SnapshotDecoder(); var view = new HordeWorldView();
        dec.Reset(new HordeRunInfo { X0 = sim.Arena.X0, Y0 = sim.Arena.Y0, X1 = sim.Arena.X1, Y1 = sim.Arena.Y1 });
        double now = 0; bool first = true; var seen = new List<HordeEvent>();
        void Snap() { enc.Encode(first, out var delta, out var key); var body = first ? key! : delta; first = false; now += 0.05; Assert.True(dec.Apply(body, 0, body.Length, view, 0, now)); seen.AddRange(dec.Events); }
        void Go(int n) { for (int t = 0; t < n; t++) { foreach (var q in sim.Players) if (q.Present && q.Open != null) sim.Choose(q.Slot, q.Open.Id, 0); sim.Step(); enc.Collect(); sim.ClearOutputs(); if (t % 2 == 1) Snap(); } Snap(); }
        Snap();
        var p0 = sim.Player(0)!; int e = sim.Spawn(sim.C.Enemy(1)!, p0.X + 40, p0.Y, true); sim.Enemies[e].Damage = 1_000_000; sim.Enemies[e].Speed = 0;
        Go(T(2)); sim.Enemies[e].Alive = false;
        Assert.True(p0.Fallen);
        Assert.Equal(sim.Souls.Count, view.Souls.Count);
        for (int i = 0; i < sim.Souls.Count; i++)
        {
            var s = sim.Souls[i]; var v = view.Souls[i];
            Assert.Equal(s.Id, v.Id); Assert.True(Math.Abs(s.X / 256f - v.X) < 1e-3 && Math.Abs(s.Y / 256f - v.Y) < 1e-3);
            Assert.Equal(1.5f, v.Radius); Assert.Equal(s.Owners, v.Owners); Assert.True(v.CountsFor(0));
        }
        var cv = view.SoulCountOf(0); Assert.Equal(4, cv.Total); Assert.Equal(4, cv.Need);
        Assert.Contains(seen, x => x.Kind == HordeEventKind.SoulSplit && x.Slot == 0 && x.Value == 4);
        // half a claim: its progress and claimer; then taken
        var s0 = sim.Souls[0]; var pb = sim.Player(1)!; Put(pb, s0.X, s0.Y);
        Go(T(0.7));
        var v0 = view.Souls.Single(x => x.Id == s0.Id); Assert.Equal(1, v0.Claimer); Assert.InRange(v0.Progress, 0.3f, 0.6f);
        Go(T(1.2));
        Assert.DoesNotContain(view.Souls, x => x.Id == s0.Id);
        Assert.Equal(3, view.SoulCountOf(0).Need);
        Assert.Contains(seen, x => x.Kind == HordeEventKind.SoulTaken && x.Slot == 1 && x.Target == s0.Id && x.DefId == 1);
        Assert.InRange(view.Players[0].Revive, 0.24f, 0.26f);
    }
}
