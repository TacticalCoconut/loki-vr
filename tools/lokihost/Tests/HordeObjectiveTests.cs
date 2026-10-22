using Loki.Horde;
using Loki.Horde.Content;
using Loki.Horde.Net;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>
/// Client wave 17 (h17obj): the map objectives (design proposal point 24; client/Assets/Loki/Runtime/Horde/Core/HordeSim.Objectives.cs):
/// when they come (1:30, then every 75-100 s, never during a boss's entrance, one at a time or two with 3+ players), how they are
/// claimed (3 s in the circle, enemies inside slow it, it drains when left, a claim under way finishes), who gets the gift (the
/// claimer and the allies within 5 cells), each of the seven kinds' gift, determinism, and the snapshot's block.
/// </summary>
public class HordeObjectiveTests
{
    readonly ITestOutputHelper _out;
    public HordeObjectiveTests(ITestOutputHelper o) { _out = o; }

    static int T(double seconds) => HordeSim.Ticks(seconds);

    /// <summary>The engine tests' tables, no waves, harmless enemies (nobody falls), only the given kinds drawn (none given: all).</summary>
    static HordeSim Make(int players, out HordeContent c, ulong seed = 11, Action<HordeContent>? tune = null, params ObjectiveKind[] kinds)
    {
        c = HordeTestContent.Build();
        c.Waves.Clear(); foreach (var e in c.Enemies) e.Damage = 0;
        if (kinds.Length > 0) { var w = new int[8]; foreach (var k in kinds) w[(int)k] = 10; c.Objectives.KindWeights = w; }
        tune?.Invoke(c);
        var sim = new HordeSim(c, c.Arenas[0], seed, 900);
        for (int i = 0; i < players; i++) sim.AddPlayer((uint)(5000000 + i), "p" + i);
        return sim;
    }

    /// <summary>Steps (any open offer answered with its first card) until <paramref name="until"/> says so or the tick limit.</summary>
    static void Run(HordeSim sim, int maxTick, Func<HordeSim, bool>? until = null, Action<HordeSim>? each = null)
    {
        while (sim.Tick < maxTick && !sim.Over)
        {
            foreach (var p in sim.Players) if (p.Present && p.Open != null) sim.Choose(p.Slot, p.Open.Id, 0);
            sim.Step();
            each?.Invoke(sim);
            bool stop = until != null && until(sim);
            sim.ClearOutputs();
            if (stop) return;
        }
    }

    static void Put(SimPlayer p, int x, int y) { p.X = x; p.Y = y; p.Vx = p.Vy = 0; }
    static double Cells(long d2) => Math.Sqrt(d2) / 256.0;

    // ------------------------------------------------------------------------------------------------ when

    [Fact]
    public void Alone_the_first_comes_at_1_30_then_one_every_75_to_100_s_one_at_a_time_and_each_fades_after_25_s()
    {
        var sim = Make(1, out var c);
        var ups = new List<int>(); var fades = new Dictionary<int, int>(); var upAt = new Dictionary<int, int>(); int most = 0;
        Run(sim, T(800), each: s =>
        {
            foreach (var e in s.Events)
            {
                if (e.Kind == HordeEventKind.ObjectiveUp) { ups.Add(s.Tick); upAt[e.Target] = s.Tick; }
                if (e.Kind == HordeEventKind.ObjectiveFaded) fades[e.Target] = s.Tick;
            }
            most = Math.Max(most, s.Objectives.Count);
        });
        _out.WriteLine("up at " + string.Join(", ", ups.Select(t => $"{t / 40.0:0.0} s")));
        Assert.True(ups.Count >= 7, $"{ups.Count} objectives in 800 s");
        Assert.Equal(T(90), ups[0]);
        for (int i = 1; i < ups.Count; i++) Assert.InRange(ups[i] - ups[i - 1], T(75), T(100));
        Assert.Equal(1, most);                                    // one at a time alone
        foreach (var kv in upAt) if (fades.TryGetValue(kv.Key, out int f)) Assert.Equal(kv.Value + T(25), f); // nobody claimed: each faded 25 s after it came
        Assert.Equal(ups.Count - (sim.Objectives.Count), fades.Count);
        // the kinds vary (never the same twice in a row) and the Emperium came at most once
        Assert.Equal(sim.ObjectivesUp, ups.Count);
    }

    [Fact]
    public void None_appears_during_a_boss_entrance_it_waits_until_the_entrance_is_over()
    {
        // a boss at 1:25 (warned 8 s before): its entrance is 1:17 to 1:35; the first objective (due 1:30) waits until it is over
        var sim = Make(1, out var c, tune: cc => cc.Waves.Add(new WaveDef { From = 85, To = 86, EnemyId = 20, Count = 1, Once = true, WarnSeconds = 8 }));
        int first = -1, boss = -1;
        Run(sim, T(200), s => first >= 0, s =>
        {
            foreach (var e in s.Events) { if (e.Kind == HordeEventKind.ObjectiveUp && first < 0) first = s.Tick; if (e.Kind == HordeEventKind.BossAppeared) boss = s.Tick; }
        });
        Assert.Equal(T(85), boss);
        Assert.True(sim.InBossEntrance(T(90)) && sim.InBossEntrance(T(77)) && !sim.InBossEntrance(T(76)));
        Assert.Equal(T(85) + T(c.Objectives.BossEntranceAfterSeconds) + 1, first);
        _out.WriteLine($"boss at {boss / 40.0} s, first objective at {first / 40.0} s (due at 90 s)");
    }

    [Fact]
    public void Three_players_get_two_at_once_far_apart_and_away_from_everyone()
    {
        var sim = Make(3, out var c);
        Run(sim, T(91));
        var ups = sim.Objectives.Where(o => o.State == ObjectiveState.Up).ToList();
        Assert.Equal(2, ups.Count);
        double apart = Cells(HordeSim.Dist2(ups[0].X, ups[0].Y, ups[1].X, ups[1].Y));
        Assert.True(apart >= c.Objectives.ApartCells, $"{apart:0.0} cells apart");
        Assert.NotEqual(ups[0].Kind, ups[1].Kind);
        foreach (var o in ups) foreach (var p in sim.Players.Where(p => p.Present))
            Assert.True(Cells(HordeSim.Dist2(o.X, o.Y, p.X, p.Y)) >= c.Objectives.NearCells * 0.6, "away from the players");
        foreach (var o in ups) Assert.True(o.X >= sim.MinX + 4 * 256 && o.X <= sim.MaxX - 4 * 256 && o.Y >= sim.MinY + 4 * 256 && o.Y <= sim.MaxY - 4 * 256, "inside the arena's margin");
        _out.WriteLine($"two at once: {ups[0].Kind} and {ups[1].Kind}, {apart:0.0} cells apart");
        // two players: one at a time
        var two = Make(2, out _);
        Run(two, T(91));
        Assert.Single(two.Objectives);
    }

    // ------------------------------------------------------------------------------------------------ the claim

    [Fact]
    public void Standing_in_the_circle_claims_it_in_3_s_enemies_inside_slow_it_and_it_drains_when_left()
    {
        // alone in it: 3 s
        var sim = Make(1, out var c, 11, null, ObjectiveKind.Shrine);
        Run(sim, T(91));
        var o = sim.Objectives.Single(); var me = sim.Player(0)!;
        int start = sim.Tick, claimed = -1;
        Put(me, o.X, o.Y);
        Run(sim, T(200), s => claimed >= 0, s => { foreach (var e in s.Events) if (e.Kind == HordeEventKind.ObjectiveClaimed) claimed = s.Tick; });
        Assert.Equal(T(3), claimed - start);

        // four enemies inside: 100 - 4 x 12 = 52 % speed (5.8 s)
        var sim2 = Make(1, out var c2, 11, cc => { foreach (var e in cc.Enemies) e.Hp = 1000000; }, ObjectiveKind.Shrine);
        Run(sim2, T(91));
        var o2 = sim2.Objectives.Single(); var me2 = sim2.Player(0)!;
        Put(me2, o2.X, o2.Y);
        for (int i = 0; i < 4; i++) sim2.Spawn(c2.Enemy(1)!, o2.X + (i % 2 == 0 ? 256 : -256), o2.Y + (i < 2 ? 256 : -256), true);
        int s2 = sim2.Tick, claimed2 = -1, crowdSeen = 0;
        Run(sim2, T(300), s => claimed2 >= 0, s => { if (s.Objectives.Count > 0) crowdSeen = Math.Max(crowdSeen, s.Objectives[0].Crowd); foreach (var e in s.Events) if (e.Kind == HordeEventKind.ObjectiveClaimed) claimed2 = s.Tick; });
        Assert.Equal(4, crowdSeen);
        Assert.InRange(claimed2 - s2, 225, 235); // 120 / 0.52 = 231 ticks
        _out.WriteLine($"alone in it: {(claimed - start) / 40.0:0.00} s; with 4 enemies inside: {(claimed2 - s2) / 40.0:0.00} s");

        // half filled, left for 1 s (it drains at half speed), back: 1.5 + 1 + 2 s
        var sim3 = Make(1, out _, 11, null, ObjectiveKind.Shrine);
        Run(sim3, T(91));
        var o3 = sim3.Objectives.Single(); var me3 = sim3.Player(0)!;
        int s3 = sim3.Tick, claimed3 = -1;
        Put(me3, o3.X, o3.Y);
        Run(sim3, s3 + 60);
        Assert.InRange(o3.Progress, 0.49, 0.51);
        Put(me3, o3.X + 6 * 256, o3.Y);
        Run(sim3, s3 + 100);
        Assert.InRange(o3.Progress, 0.32, 0.35);
        Assert.Equal(-1, o3.Claimer);
        Put(me3, o3.X, o3.Y);
        Run(sim3, T(300), s => claimed3 >= 0, s => { foreach (var e in s.Events) if (e.Kind == HordeEventKind.ObjectiveClaimed) claimed3 = s.Tick; });
        Assert.InRange(claimed3 - s3, 178, 182);
    }

    [Fact]
    public void A_claim_under_way_at_the_deadline_finishes_and_an_empty_one_fades()
    {
        var sim = Make(1, out _, 11, null, ObjectiveKind.Well);
        Run(sim, T(91));
        var o = sim.Objectives.Single(); var me = sim.Player(0)!;
        Run(sim, o.Deadline - 40); // in at 1 s before the deadline: 3 s to fill
        Put(me, o.X, o.Y);
        bool claimed = false, faded = false;
        Run(sim, o.Deadline + T(5), s => claimed || faded, s => { foreach (var e in s.Events) { if (e.Kind == HordeEventKind.ObjectiveClaimed) claimed = true; if (e.Kind == HordeEventKind.ObjectiveFaded) faded = true; } });
        Assert.True(claimed); Assert.False(faded);
    }

    [Fact]
    public void The_gift_goes_to_the_claimer_and_to_the_allies_within_5_cells_only()
    {
        var sim = Make(3, out var c, 11, null, ObjectiveKind.Shrine, ObjectiveKind.Well);
        Run(sim, T(91));
        var o = sim.Objectives.First(x => x.Kind == ObjectiveKind.Shrine);
        var a = sim.Player(0)!; var b = sim.Player(1)!; var far = sim.Player(2)!;
        Put(a, o.X, o.Y); Put(b, o.X + 4 * 256, o.Y); Put(far, o.X - 8 * 256, o.Y);
        var got = new List<int>(); int claimer = -1;
        Run(sim, T(200), s => claimer >= 0, s => { foreach (var e in s.Events) { if (e.Target != o.Id) continue; if (e.Kind == HordeEventKind.ObjectiveGot) got.Add(e.Slot); if (e.Kind == HordeEventKind.ObjectiveClaimed) claimer = e.Slot; } });
        Assert.Equal(0, claimer);
        Assert.Equal(new[] { 0, 1 }, got.OrderBy(x => x).ToArray());
        Assert.InRange(a.S.CooldownPct, -23.11, -23.09); Assert.InRange(b.S.CooldownPct, -23.11, -23.09); Assert.Equal(0, far.S.CooldownPct, 3);
        Assert.Equal(2, sim.ObjectiveBuffs.Count(x => x.Kind == ObjectiveKind.Shrine));
    }

    // ------------------------------------------------------------------------------------------------ the seven gifts

    [Fact]
    public void Shrine_gives_30_percent_attack_speed_for_20_s()
    {
        var sim = Make(1, out _, 11, null, ObjectiveKind.Shrine);
        Run(sim, T(91));
        var o = sim.Objectives.Single(); var me = sim.Player(0)!;
        var w = me.Weapons[0]; int cdBefore = Cooldown(me, w);
        Put(me, o.X, o.Y);
        int claimed = -1;
        Run(sim, T(200), s => claimed >= 0, s => { foreach (var e in s.Events) if (e.Kind == HordeEventKind.ObjectiveClaimed) claimed = s.Tick; });
        int cdWith = Cooldown(me, w);
        Assert.InRange(cdBefore / (double)cdWith, 1.28, 1.32); // +30 % attacks a second
        Run(sim, claimed + T(20) - 1);
        Assert.Contains(sim.ObjectiveBuffs, x => x.Kind == ObjectiveKind.Shrine);
        Run(sim, claimed + T(20) + 1);
        Assert.Equal(0, me.S.CooldownPct, 3);
        Assert.DoesNotContain(sim.ObjectiveBuffs, x => x.Kind == ObjectiveKind.Shrine);
        _out.WriteLine($"cooldown {cdBefore} ticks -> {cdWith} ticks for 20 s");
    }

    static int Cooldown(SimPlayer p, OwnedWeapon w) => Math.Max(1, HordeSim.Ticks(w.Lv.Cooldown * Math.Max(0.25, 1 + p.S.CooldownPct / 100.0)));

    [Fact]
    public void Well_heals_4_percent_of_max_hp_every_second_for_15_s()
    {
        var sim = Make(1, out _, 11, null, ObjectiveKind.Well);
        Run(sim, T(91));
        var o = sim.Objectives.Single(); var me = sim.Player(0)!;
        me.Hp = 10; Put(me, o.X, o.Y);
        int claimed = -1, heals = 0, healed = 0;
        Run(sim, T(200), s => claimed >= 0, s => { foreach (var e in s.Events) if (e.Kind == HordeEventKind.ObjectiveClaimed) claimed = s.Tick; });
        Run(sim, claimed + T(17), each: s => { foreach (var e in s.Events) if (e.Kind == HordeEventKind.Heal && e.Slot == 0) { heals++; healed += e.Value; } });
        int per = (int)Math.Round(me.MaxHp * 0.04);
        Assert.Equal(15, heals);
        Assert.Equal(15 * per, healed);
        Assert.Equal(10 + 15 * per, me.Hp);
    }

    [Fact]
    public void Pylons_burn_enemies_on_their_walls_for_20_s_and_nothing_inside_or_outside()
    {
        var sim = Make(1, out var c, 11, cc => { foreach (var e in cc.Enemies) { e.Hp = 1000000; e.Behavior = EnemyBehavior.Still; } }, ObjectiveKind.Pylons);
        Run(sim, T(91));
        var o = sim.Objectives.Single(); var me = sim.Player(0)!;
        Put(me, o.X, o.Y);
        int claimed = -1;
        Run(sim, T(200), s => claimed >= 0, s => { foreach (var e in s.Events) if (e.Kind == HordeEventKind.ObjectiveClaimed) claimed = s.Tick; });
        Assert.Equal(ObjectiveState.Active, o.State);
        Assert.Equal(claimed + T(20), o.EndTick);
        // one on a wall, one inside, one outside (the walls stand 9 cells from the pylon, kept in the arena)
        ObjectiveRulesDef.PylonSquare(o.X, o.Y, 9 * 256, sim.MinX, sim.MinY, sim.MaxX, sim.MaxY, out float x0, out _, out float x1, out _);
        bool left = x0 - 4 * 256 >= sim.MinX; int wall = (int)(left ? x0 : x1), dir = left ? -1 : 1;
        int onWall = sim.Spawn(c.Enemy(1)!, wall, o.Y + 256, true), inside = sim.Spawn(c.Enemy(1)!, wall - dir * 5 * 256, o.Y, true), outside = sim.Spawn(c.Enemy(1)!, wall + dir * 3 * 256, o.Y, true);
        var hits = new Dictionary<int, int>();
        Run(sim, claimed + T(2) + 1, each: s => { foreach (var h in s.Hits) if (h.DefId == c.Objectives.PylonWeapon.Id && (h.Flags & HordeEventFlags.Dot) == 0) hits[h.Target] = hits.GetValueOrDefault(h.Target) + 1; });
        Assert.InRange(hits.GetValueOrDefault(onWall), 4, 5);           // struck every 0.5 s (its burn's ticks are counted apart)
        Assert.True(sim.Enemies[onWall].DotUntil > sim.Tick, "burning");
        Assert.False(hits.ContainsKey(inside)); Assert.False(hits.ContainsKey(outside));
        Run(sim, claimed + T(20) + 1);
        Assert.Empty(sim.Objectives);
        hits.Clear();
        Run(sim, claimed + T(25), each: s => { foreach (var h in s.Hits) if (h.DefId == c.Objectives.PylonWeapon.Id && (h.Flags & HordeEventFlags.Dot) == 0) hits[h.Target] = 1; });
        Assert.Empty(hits);
    }

    [Fact]
    public void Flame_ring_turns_three_fire_balls_around_the_claimer_and_near_allies_for_25_s()
    {
        var sim = Make(2, out var c, 11, cc => { foreach (var e in cc.Enemies) { e.Hp = 1000000; e.Behavior = EnemyBehavior.Still; } }, ObjectiveKind.FlameRing);
        Run(sim, T(91));
        var o = sim.Objectives.Single(); var a = sim.Player(0)!; var b = sim.Player(1)!;
        Put(a, o.X, o.Y); Put(b, o.X + 3 * 256, o.Y + 3 * 256);
        int claimed = -1;
        Run(sim, T(200), s => claimed >= 0, s => { foreach (var e in s.Events) if (e.Kind == HordeEventKind.ObjectiveClaimed) claimed = s.Tick; });
        var rings = sim.Fx.Where(f => f.Kind == HordeFxKind.Orbit && f.DefId == c.Objectives.FlameRingWeapon.Id).ToList();
        Assert.Equal(2, rings.Count);
        Assert.All(rings, f => { Assert.Equal(3, f.Count); Assert.Equal(claimed + T(25), f.EndTick); });
        int target = sim.Spawn(c.Enemy(1)!, a.X + (int)(2.2 * 256), a.Y, true);
        int ringHits = 0;
        Run(sim, claimed + T(3), each: s => { foreach (var h in s.Hits) if (h.DefId == c.Objectives.FlameRingWeapon.Id && h.Target == target) ringHits++; });
        Assert.True(ringHits >= 3, $"{ringHits} ring hits in 3 s");
        Run(sim, claimed + T(25) + 2);
        Assert.DoesNotContain(sim.Fx, f => f.DefId == c.Objectives.FlameRingWeapon.Id && !f.Ended);
        Assert.DoesNotContain(sim.ObjectiveBuffs, x => x.Kind == ObjectiveKind.FlameRing);
    }

    [Fact]
    public void Meteor_beacon_strikes_the_three_thickest_crowds()
    {
        var sim = Make(1, out var c, 11, cc => { foreach (var e in cc.Enemies) { e.Hp = 1000000; e.Behavior = EnemyBehavior.Still; } }, ObjectiveKind.Meteor);
        Run(sim, T(91));
        var o = sim.Objectives.Single(); var me = sim.Player(0)!;
        // crowds of 5, 3 and 2 within reach, one lone enemy
        (int x, int y, int n)[] crowds = { (o.X + 8 * 256, o.Y, 5), (o.X - 8 * 256, o.Y, 3), (o.X, o.Y + 8 * 256, 2), (o.X, o.Y - 9 * 256, 1) };
        for (int k = 0; k < crowds.Length; k++) crowds[k] = (HordeSim.Clamp(crowds[k].x, sim.MinX + 300, sim.MaxX - 300), HordeSim.Clamp(crowds[k].y, sim.MinY + 300, sim.MaxY - 300), crowds[k].n);
        foreach (var cr in crowds) for (int i = 0; i < cr.n; i++) sim.Spawn(c.Enemy(1)!, cr.x + (i % 3) * 100, cr.y + (i / 3) * 100, true);
        Put(me, o.X, o.Y);
        int claimed = -1; var strikes = new List<(float x, float y)>();
        Run(sim, T(200), s => claimed >= 0, s => { foreach (var e in s.Events) if (e.Kind == HordeEventKind.ObjectiveClaimed) claimed = s.Tick; });
        var warns = sim.Fx.Where(f => f.Kind == HordeFxKind.Warning && f.DefId == c.Objectives.MeteorWeapon.Id).ToList();
        Assert.Equal(3, warns.Count);
        for (int i = 0; i < 3; i++) Assert.True(Cells(HordeSim.Dist2(warns[i].X, warns[i].Y, crowds[i].x, crowds[i].y)) < 1.5, $"strike {i} on crowd {i}");
        Run(sim, claimed + T(2), each: s => { foreach (var e in s.Events) if (e.Kind == HordeEventKind.Strike && e.DefId == c.Objectives.MeteorWeapon.Id) strikes.Add((e.X, e.Y)); });
        Assert.Equal(3, strikes.Count);
    }

    [Fact]
    public void Guarded_chest_has_an_elite_on_a_leash_gives_three_upgrades_and_its_guard_leaves_if_it_fades()
    {
        Action<HordeContent> tough = cc => cc.Enemy(10)!.Hp = 100000; // (the player's own bolts must not kill the guard meanwhile)
        var sim = Make(1, out var c, 11, tough, ObjectiveKind.Chest);
        Run(sim, T(91));
        var o = sim.Objectives.Single(); var me = sim.Player(0)!;
        var guard = sim.Enemies[o.GuardSlot];
        Assert.True(guard.Alive); Assert.Equal(EnemyRank.Elite, guard.Def.Rank);
        Assert.Equal((int)Math.Round(c.Enemy(10)!.Hp * 0.5), guard.MaxHp); // the weakest elite at half its HP
        double farthest = 0;
        Run(sim, T(96), each: s => farthest = Math.Max(farthest, Cells(HordeSim.Dist2(guard.X, guard.Y, o.X, o.Y))));
        Assert.True(farthest <= c.Objectives.GuardLeashCells + 0.01, $"the guard went {farthest:0.00} cells from its box");
        // taken (the guard inside slows it: weight 3)
        int levelBefore = me.Weapons.Sum(w => w.Level);
        Put(me, o.X, o.Y);
        int claimed = -1;
        Run(sim, T(200), s => claimed >= 0, s => { foreach (var e in s.Events) if (e.Kind == HordeEventKind.ObjectiveClaimed) claimed = s.Tick; });
        Assert.True(claimed > 0);
        Assert.Equal(Math.Min(levelBefore + 3, me.Weapons.Sum(w => w.Def.Levels.Length)), me.Weapons.Sum(w => w.Level));
        Assert.True(guard.Alive, "the guard fights on");
        // another run: nobody takes it: the guard leaves with its box
        var sim2 = Make(1, out _, 11, tough, ObjectiveKind.Chest);
        Run(sim2, T(91));
        var o2 = sim2.Objectives.Single(); var g2 = sim2.Enemies[o2.GuardSlot];
        Run(sim2, o2.Deadline + 1);
        Assert.Empty(sim2.Objectives); Assert.False(g2.Alive);
        Assert.Equal(0, sim2.Player(0)!.Kills);
    }

    [Fact]
    public void Emperium_is_held_30_s_under_waves_then_gives_upgrades_and_a_magnet_and_comes_once_a_run()
    {
        var sim = Make(1, out var c, 11, cc => { foreach (var e in cc.Enemies) e.Hp = 1000000; }, ObjectiveKind.Emperium);
        Run(sim, T(91));
        var o = sim.Objectives.Single(); var me = sim.Player(0)!;
        Assert.Equal(ObjectiveKind.Emperium, o.Kind);
        Assert.Equal(4 * 256, o.Radius);
        sim.Pickups.Add(new SimPickup { Id = 60000, Def = c.Pickup(1)!, X = sim.MinX + 300, Y = sim.MinY + 300, Value = 1 }); // a gem far away
        Put(me, o.X, o.Y);
        int start = sim.Tick, claimed = -1, waves = 0;
        Run(sim, T(400), s => claimed >= 0, s =>
        {
            foreach (var e in s.Events) { if (e.Kind == HordeEventKind.ObjectiveClaimed) claimed = s.Tick; if (e.Kind == HordeEventKind.EmperiumWave) waves++; }
            if (s.Objectives.Count > 0) Put(me, o.X, o.Y); // holding the circle
        });
        Assert.InRange(claimed - start, T(30) - 1, T(30) + 1);
        Assert.Equal(5, waves); // at 0, 6, 12, 18, 24 s
        Assert.Equal(me.Slot, sim.Pickups.First(p => p.Id == 60000).PulledBy);
        Assert.Equal(Math.Min(1 + 3, me.Weapons[0].Def.Levels.Length), me.Weapons[0].Level);
        // the run goes on: no second Emperium (and no other kind is drawn here)
        Run(sim, T(800));
        Assert.True(sim.EmperiumSeen); Assert.Equal(1, sim.ObjectivesUp);
        // left alone 6 s after it started: lost
        var sim2 = Make(1, out _, 11, null, ObjectiveKind.Emperium);
        Run(sim2, T(91));
        var o2 = sim2.Objectives.Single(); var me2 = sim2.Player(0)!;
        Put(me2, o2.X, o2.Y); Run(sim2, sim2.Tick + T(2));
        Assert.Equal(ObjectiveState.Defending, o2.State);
        Put(me2, o2.X + 10 * 256, o2.Y);
        int left = sim2.Tick, lostAt = -1, lostValue = -1;
        Run(sim2, T(200), s => lostAt >= 0, s => { foreach (var e in s.Events) if (e.Kind == HordeEventKind.ObjectiveFaded) { lostAt = s.Tick; lostValue = e.Value; } });
        Assert.Equal(1, lostValue);
        Assert.InRange(lostAt - left, T(6) - 1, T(6) + 1);
    }

    // ------------------------------------------------------------------------------------------------ the game's tables, determinism, the wire

    [Fact]
    public void With_the_games_tables_and_three_bots_a_run_is_deterministic_and_objectives_come_and_go()
    {
        string Play(out int ups, out int claims)
        {
            var c = HordeTables.Build();
            var sim = new HordeSim(c, c.Arenas[0], 4242, 900);
            for (int i = 0; i < 3; i++) sim.AddPlayer((uint)(5100000 + i), "bot" + i);
            var trace = new System.Text.StringBuilder(); ushort seq = 0;
            int u = 0, cl = 0;
            for (int t = 0; t < T(420) && !sim.Over; t++)
            {
                seq++;
                for (int i = 0; i < 3; i++)
                {
                    // bots: the first walks to the nearest objective, the others as HordeTests' bots
                    var p = sim.Player(i);
                    var o = sim.Objectives.Where(x => x.State != ObjectiveState.Active).OrderBy(x => p == null ? 0 : HordeSim.Dist2(x.X, x.Y, p.X, p.Y)).FirstOrDefault();
                    if (i == 0 && p != null && o != null)
                    {
                        double a = Math.Atan2(o.Y - p.Y, o.X - p.X); bool there = HordeSim.Dist2(o.X, o.Y, p.X, p.Y) < 256L * 256;
                        sim.PushInput(i, seq, there ? 0 : (int)(Math.Cos(a) * 127), there ? 0 : (int)(Math.Sin(a) * 127));
                        if (p.Open != null) sim.Choose(i, p.Open.Id, 0);
                    }
                    else HordeTests.BotStep(sim, i, seq, i);
                }
                sim.Step();
                foreach (var e in sim.Events) { if (e.Kind == HordeEventKind.ObjectiveUp) u++; if (e.Kind == HordeEventKind.ObjectiveClaimed) cl++; if (e.Kind >= HordeEventKind.ObjectiveUp) trace.Append($"{sim.Tick}:{e.Kind}:{e.Target}:{e.DefId}:{e.Slot};"); }
                trace.Append(sim.StateHash().ToString("x")).Append('|');
                sim.ClearOutputs();
            }
            ups = u; claims = cl;
            return trace.ToString();
        }
        var a = Play(out int ups, out int claims); var b = Play(out _, out _);
        Assert.Equal(a, b);
        Assert.True(ups >= 6, $"{ups} objectives (two at once with three players)");
        Assert.True(claims >= 1, $"{claims} claimed by the bot that walks to them");
        _out.WriteLine($"420 s, three bots: {ups} objectives, {claims} claimed; the same twice");
    }

    [Fact]
    public void The_snapshot_carries_the_objectives_and_the_gifts_to_the_client()
    {
        var sim = Make(1, out var c, 11, null, ObjectiveKind.Chest);
        var enc = new SnapshotEncoder(sim); var dec = new SnapshotDecoder(); var view = new HordeWorldView();
        dec.Reset(new HordeRunInfo { X0 = sim.Arena.X0, Y0 = sim.Arena.Y0, X1 = sim.Arena.X1, Y1 = sim.Arena.Y1 });
        double now = 0; bool first = true; var seen = new List<HordeEvent>();
        void Snap()
        {
            enc.Encode(first, out var delta, out var key);
            var body = first ? key! : delta; first = false; now += 0.05;
            Assert.True(dec.Apply(body, 0, body.Length, view, 0, now));
            seen.AddRange(dec.Events);
        }
        for (int t = 0; t < T(91); t++) { sim.PushInput(0, (ushort)(t + 1), 0, 0); sim.Step(); enc.Collect(); sim.ClearOutputs(); if (t % 2 == 1) Snap(); }
        Snap();
        var o = sim.Objectives.Single(); var v = view.Objectives.Single();
        Assert.Equal(o.Id, v.Id); Assert.Equal(ObjectiveKind.Chest, v.Kind); Assert.Equal(ObjectiveState.Up, v.State);
        Assert.True(Math.Abs(o.X / 256f - v.X) < 1e-3 && Math.Abs(o.Y / 256f - v.Y) < 1e-3);
        Assert.Equal(3f, v.Radius); Assert.Equal(o.StartTick, v.StartTick); Assert.Equal(o.Deadline, v.EndTick); Assert.Equal(o.GuardSlot, v.GuardSlot);
        Assert.Contains(seen, e => e.Kind == HordeEventKind.ObjectiveUp && e.Target == o.Id && e.DefId == (int)ObjectiveKind.Chest && Math.Abs(e.X - v.X) < 1e-3);
        // half claimed, then claimed: the progress, the claimer, the gift's event
        var me = sim.Player(0)!; Put(me, o.X, o.Y);
        for (int t = 0; t < 40; t++) { sim.PushInput(0, (ushort)(5000 + t), 0, 0); sim.Step(); enc.Collect(); sim.ClearOutputs(); }
        Snap();
        Assert.Equal(0, view.Objectives.Single().Claimer); Assert.InRange(view.Objectives.Single().Progress, 0.15f, 0.5f);
        for (int t = 0; t < 400 && sim.Objectives.Count > 0; t++) { sim.PushInput(0, (ushort)(6000 + t), 0, 0); sim.Step(); enc.Collect(); sim.ClearOutputs(); if (t % 2 == 1) Snap(); }
        Snap();
        Assert.Empty(view.Objectives);
        Assert.Contains(seen, e => e.Kind == HordeEventKind.ObjectiveClaimed && e.Target == o.Id && e.Slot == 0);
        Assert.Contains(seen, e => e.Kind == HordeEventKind.ObjectiveGot && e.Target == o.Id && e.Slot == 0);
        // a shrine's gift travels as a buff with its end
        var sim2 = Make(1, out _, 11, null, ObjectiveKind.Shrine);
        Run(sim2, T(91)); var o2 = sim2.Objectives.Single(); Put(sim2.Player(0)!, o2.X, o2.Y); Run(sim2, T(100));
        var enc2 = new SnapshotEncoder(sim2); var dec2 = new SnapshotDecoder(); var view2 = new HordeWorldView();
        dec2.Reset(new HordeRunInfo { X0 = sim2.Arena.X0, Y0 = sim2.Arena.Y0, X1 = sim2.Arena.X1, Y1 = sim2.Arena.Y1 });
        enc2.Encode(true, out _, out var key2);
        Assert.True(dec2.Apply(key2!, 0, key2!.Length, view2, 0, 1.0));
        var bv = view2.ObjectiveBuffs.Single();
        Assert.Equal(0, bv.Slot); Assert.Equal(ObjectiveKind.Shrine, bv.Kind); Assert.Equal(sim2.ObjectiveBuffs.Single().Until, bv.Until);
    }

    [Fact]
    public void Over_many_seeds_every_kind_comes_first_by_its_weight()
    {
        var counts = new int[8]; ulong seed = 0x5EED;
        for (int i = 0; i < 700; i++)
        {
            seed = seed * 6364136223846793005UL + 1442695040888963407UL; // (seeds as different as the host's clock gives)
            var sim = Make(1, out _, seed);
            Run(sim, T(91));
            counts[(int)sim.Objectives.Single().Kind]++;
        }
        _out.WriteLine("first objective's kind over 700 seeds: " + string.Join(", ", Enumerable.Range(1, 7).Select(k => $"{(ObjectiveKind)k} {counts[k]}")));
        for (int k = 1; k <= 6; k++) Assert.InRange(counts[k], 70, 145);   // weight 16 of 104: 108 expected
        Assert.InRange(counts[7], 25, 85);                                  // the Emperium, weight 8: 54 expected
    }

    [Fact]
    public void The_objectives_numbers_are_in_the_stamp_and_their_weapons_are_never_offered()
    {
        var a = HordeTables.Build(); var b = HordeTables.Build();
        Assert.Equal(HordeWire.ContentStamp(a), HordeWire.ContentStamp(b));
        b.Objectives.ClaimSeconds = 4f;
        Assert.NotEqual(HordeWire.ContentStamp(a), HordeWire.ContentStamp(b));
        var d = HordeTables.Build(); d.Objectives.MeteorWeapon.Levels[0].Damage++;
        Assert.NotEqual(HordeWire.ContentStamp(a), HordeWire.ContentStamp(d));
        foreach (var w in new[] { a.Objectives.FlameRingWeapon, a.Objectives.MeteorWeapon, a.Objectives.PylonWeapon })
        {
            Assert.Same(w, a.Weapon(w.Id));
            Assert.DoesNotContain(a.Weapons, x => x.Id == w.Id);
            Assert.DoesNotContain(a.Jobs, j => j.Weapons.Contains(w.Id) || j.SignatureWeapon == w.Id);
        }
        Assert.Equal(4, HordeWire.Version); // (client wave 18, h18feel: 3 -> 4, the souls' block after this one)
    }
}
