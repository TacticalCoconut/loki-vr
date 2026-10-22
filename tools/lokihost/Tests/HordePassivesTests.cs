using Loki.Horde;
using Loki.Horde.Content;
using Loki.Horde.Net;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>
/// Client wave 17 (h17pass), design 2 / point 22: every passive's trigger (HordeSim.Passives.cs). Each rule is played on the engine
/// tests' own tables (HordeTestContent) with a test passive carrying the trigger, a dummy to hit and a biter that touches, so the
/// numbers are the test's and not the game's balance; the game's tables are checked for a trigger and a body on every passive.
/// Deterministic, as the other Horde tests.
/// </summary>
public class HordePassivesTests
{
    readonly ITestOutputHelper _out;
    public HordePassivesTests(ITestOutputHelper o) { _out = o; }

    const int Dummy = 50, Biter = 51, EliteBiter = 52, Walker = 53, Chaser = 54, TestZoneWeapon = 70, TestPassive = 900;
    const int U = HordeSim.U, T = HordeSim.TickRate;

    /// <summary>The engine tests' tables + a dummy (Still, huge HP, harmless), a biter (Still, 10 a touch every 0.5 s), an elite
    /// biter, a walker (chases, harmless), a zone laid under the runner, the triggers' pickups, and ONE test passive with
    /// <paramref name="proc"/> (its levels' modifiers: <paramref name="mods"/>). No waves, no random criticals.</summary>
    static HordeContent Content(PassiveProcDef? proc, params ModDef[] mods)
    {
        var c = HordeTestContent.Build();
        c.Waves.Clear();
        c.Rules.CritChance = 0;
        c.Rules.StartHp = 1000; // nobody falls in a test
        c.Rules.ExpTable = new[] { 100000000 }; c.Rules.ExpGrowthPct = 0; // and no level-up pauses the world
        c.Enemies.Add(new EnemyDef { Id = Dummy, MobId = 1002, Key = "DUMMY", Behavior = EnemyBehavior.Still, Hp = 1000000, Damage = 0, Exp = 0, Radius = 0.5f, Race = Race.Brute });
        c.Enemies.Add(new EnemyDef { Id = Biter, MobId = 1002, Key = "BITER", Behavior = EnemyBehavior.Still, Hp = 1000000, Damage = 10, AttackInterval = 0.5f, Exp = 0, Radius = 0.5f });
        c.Enemies.Add(new EnemyDef { Id = EliteBiter, MobId = 1031, Key = "ELITE_BITER", Rank = EnemyRank.Elite, Behavior = EnemyBehavior.Still, Hp = 1000000, Damage = 10, AttackInterval = 0.5f, Exp = 0, Radius = 0.5f });
        c.Enemies.Add(new EnemyDef { Id = Walker, MobId = 1002, Key = "WALKER", Behavior = EnemyBehavior.Chase, Hp = 1000000, Damage = 0, Speed = 2f, Exp = 0, Radius = 0.5f });
        c.Enemies.Add(new EnemyDef { Id = Chaser, MobId = 1002, Key = "CHASER", Behavior = EnemyBehavior.Chase, Hp = 1000000, Damage = 10, AttackInterval = 0.5f, Speed = 3f, Exp = 0, Radius = 0.5f });
        c.Weapons.Add(new WeaponDef { Id = TestZoneWeapon, Key = "test_zone", Kind = WeaponKind.GroundZone, Target = Targeting.Self, Class = DamageClass.Magic, Weight = 0,
            Levels = HordeTestContent.Lv(HordeTestContent.L(dmg: 0, cd: 30f, area: 3f, duration: 30f, tick: 0.5f)) });
        c.Pickups.Add(new PickupDef { Id = 240, Key = "proc_clover", Kind = PickupKind.Heal, ItemId = 705, Value = 3 });
        c.Pickups.Add(new PickupDef { Id = 241, Key = "proc_heal_mote", Kind = PickupKind.Heal, ItemId = 507, Value = 2 });
        c.Pickups.Add(new PickupDef { Id = 242, Key = "proc_arrow_bundle", Kind = PickupKind.Passive, ItemId = 12004, Value = 1 });
        if (proc != null) c.Passives.Add(new PassiveDef { Id = TestPassive, Key = "test_passive", Levels = new[] { mods, mods, mods }, Proc = proc });
        return c;
    }

    /// <summary>A sim with one runner holding the test passive at level 1 (and only the weapons given, or the start bolt).</summary>
    static HordeSim Sim(HordeContent c, out SimPlayer me, ulong seed = 17, params int[] weapons)
    {
        var sim = new HordeSim(c, c.Arenas[0], seed, 900);
        sim.AddPlayer(5000001, "runner");
        me = sim.Player(0)!;
        if (c.Passive(TestPassive) != null) Assert.True(sim.GivePassive(0, TestPassive, 1));
        if (weapons.Length > 0)
        {
            me.Weapons.Clear();
            foreach (int id in weapons) me.Weapons.Add(new OwnedWeapon { Def = c.Weapon(id)!, Level = 1, NextTick = 1 });
        }
        me.SafeUntil = 0;
        return sim;
    }

    static int Put(HordeSim sim, int enemyId, double dxCells, double dyCells)
    {
        var p = sim.Player(0)!;
        return sim.Spawn(sim.C.Enemy(enemyId)!, p.X + (int)Math.Round(dxCells * U), p.Y + (int)Math.Round(dyCells * U), true);
    }

    /// <summary>Steps <paramref name="ticks"/> ticks (the runner stands, or walks with <paramref name="walk"/>), collecting events.</summary>
    static List<HordeEvent> Run(HordeSim sim, int ticks, (int dx, int dy)? walk = null, List<HordeEvent>? into = null)
    {
        var all = into ?? new List<HordeEvent>();
        for (int i = 0; i < ticks && !sim.Over; i++)
        {
            if (walk != null) sim.PushInput(0, (ushort)(sim.WallTick + 1), walk.Value.dx, walk.Value.dy);
            sim.Step();
            all.AddRange(sim.Events); all.AddRange(sim.Hits);
            sim.ClearOutputs();
        }
        return all;
    }

    static List<int> MyHits(List<HordeEvent> ev, int target) => ev.Where(e => e.Kind == HordeEventKind.Hit && e.Slot == 0 && e.Target == target && e.DefId != 0).Select(e => e.Value).ToList();
    static int Procs(List<HordeEvent> ev) => ev.Count(e => e.Kind == HordeEventKind.Proc && e.Slot == 0);

    // ------------------------------------------------------------------------------------------------ the game's tables

    [Fact]
    public void Every_passive_of_the_games_tables_has_a_trigger_and_a_body_and_h17cards_new_ids_are_all_named()
    {
        var c = HordeTables.Build();
        foreach (var p in c.Passives)
        {
            Assert.True(p.Proc != null, $"passive {p.Id} {p.Key} has no trigger");
            Assert.NotEqual(ProcKind.None, p.Proc!.Kind);
            Assert.NotEqual(ProcBody.None, p.Proc.Body);
            if (p.Proc.PickupId != 0) Assert.NotNull(c.Pickup(p.Proc.PickupId));
        }
        // design notes: the 56 new ids (they take their triggers when the branches are merged)
        int[] h17cards = { 300, 301, 302, 303, 304, 305, 306, 307, 308, 309, 12, 13, 14, 15, 22, 32, 33, 34, 35, 52, 53, 54, 62, 63, 64, 102, 103, 111, 112, 113,
                           120, 121, 132, 133, 134, 135, 136, 141, 142, 143, 151, 171, 182, 183, 184, 193, 194, 195, 196, 202, 212, 213, 214, 215, 222, 223 };
        var named = HordeTables.PassiveProcIds().ToList();
        Assert.Equal(56, h17cards.Length);
        foreach (int id in h17cards) Assert.Contains(id, named);
        Assert.Equal(named.Count, named.Distinct().Count());
        _out.WriteLine($"{c.Passives.Count} passives in the tables, each with a trigger; {named.Count} ids named ({named.Count - c.Passives.Count} wait for h17cards' merge)");
        foreach (var g in c.Passives.GroupBy(p => p.Proc!.Kind)) _out.WriteLine($"  {g.Key}: {string.Join(", ", g.Select(p => p.Key))}");
    }

    // ------------------------------------------------------------------------------------------------ counters

    [Fact]
    public void HitCount_every_tenth_hit_is_heavy()
    {
        var c = Content(new PassiveProcDef { Kind = ProcKind.HitCount, Body = ProcBody.Orbiter, Count = 10, Power = 80 });
        var sim = Sim(c, out _);
        int d = Put(sim, Dummy, 3, 0);
        var ev = Run(sim, T * 25);
        var hits = MyHits(ev, d);
        _out.WriteLine("hits: " + string.Join(" ", hits));
        Assert.True(hits.Count >= 20);
        for (int i = 0; i < hits.Count; i++) Assert.Equal((i + 1) % 10 == 0 ? 14 : 8, hits[i]); // 8 × 1.8 = 14.4
        Assert.Equal(hits.Count / 10, sim.ProcsFired(0, ProcKind.HitCount));
    }

    [Fact]
    public void CritCount_five_criticals_drop_a_clover_at_the_enemy()
    {
        var c = Content(new PassiveProcDef { Kind = ProcKind.CritCount, Body = ProcBody.Stars, Count = 5, Power = 3, PickupId = 240 });
        c.Rules.CritChance = 100;
        var sim = Sim(c, out _);
        int d = Put(sim, Dummy, 3, 0);
        var ev = Run(sim, T * 8);
        int crits = ev.Count(e => e.Kind == HordeEventKind.Hit && e.Slot == 0 && (e.Flags & HordeEventFlags.Crit) != 0);
        int clovers = sim.Pickups.Count(k => k.Def.Id == 240);
        _out.WriteLine($"{crits} criticals, {clovers} clovers lying, {sim.ProcsFired(0, ProcKind.CritCount)} drops");
        Assert.True(crits >= 5);
        Assert.Equal(crits / 5, sim.ProcsFired(0, ProcKind.CritCount));
        Assert.True(clovers >= 1);
        var k0 = sim.Pickups.First(k => k.Def.Id == 240);
        Assert.Equal(3, k0.Value);
        Assert.True(HordeSim.Dist2(k0.X, k0.Y, sim.Enemies[d].X, sim.Enemies[d].Y) < (long)U * U);
    }

    [Fact]
    public void Mark_three_shot_hits_mark_an_enemy_and_it_takes_more_from_everyone()
    {
        var c = Content(new PassiveProcDef { Kind = ProcKind.Mark, Body = ProcBody.Trail, Count = 3, Seconds = 4f, Power = 25 });
        var sim = Sim(c, out _);
        int d = Put(sim, Dummy, 3, 0);
        var ev = Run(sim, T * 6);
        var hits = MyHits(ev, d);
        _out.WriteLine("hits: " + string.Join(" ", hits));
        Assert.Equal(new[] { 8, 8, 8, 10 }, hits.Take(4).ToArray()); // the 4th: 8 × 1.25
        Assert.Equal(25, sim.MarkedPct(d));
        Assert.Contains(ev, e => e.Kind == HordeEventKind.Proc && e.Target == d);
    }

    [Fact]
    public void FullSwing_a_swing_of_three_gives_part_of_its_wait_back_and_one_swing_does_not()
    {
        int Gap(bool three, bool passive)
        {
            var c = Content(passive ? new PassiveProcDef { Kind = ProcKind.FullSwing, Body = ProcBody.Arc, Count = 3, Power = 25 } : null);
            var sim = Sim(c, out _, 17, 2); // sm_bash: 1.2 s, reach 2.2, 120°
            Put(sim, Dummy, 1.6, 0);
            if (three) { Put(sim, Dummy, 1.5, 0.5); Put(sim, Dummy, 1.5, -0.5); }
            var ev = Run(sim, T * 5);
            var swings = ev.Where(e => e.Kind == HordeEventKind.Swing && e.Slot == 0).Select(e => e.Tick).ToList();
            Assert.True(swings.Count >= 3);
            return swings[2] - swings[1];
        }
        int plain = Gap(true, false), one = Gap(false, true), full = Gap(true, true);
        _out.WriteLine($"ticks between swings: no passive {plain}, one enemy {one}, three enemies {full}");
        Assert.Equal(48, plain); Assert.Equal(48, one);
        Assert.Equal(48 - 12, full); // 25 % of 48 ticks back
    }

    [Fact]
    public void DodgeCounter_three_dodges_in_a_row_make_a_puff_and_a_hit_breaks_the_streak()
    {
        var c = Content(new PassiveProcDef { Kind = ProcKind.DodgeCounter, Body = ProcBody.AfterImage, Count = 3, Damage = 20, Area = 2f }, new ModDef(Mod.Dodge, 60));
        var sim = Sim(c, out var me, 23, 2);
        me.Weapons.Clear(); // no weapon: only the puff hurts the biter
        int b = Put(sim, Chaser, 0.7, 0); // (a puff pushes it away: it comes back)
        var ev = Run(sim, T * 60);
        // walk the hits in order: the streak of dodges, a puff at each third, a hit taken resets it
        int streak = 0, puffsExpected = 0;
        foreach (var e in ev.Where(e => e.Kind == HordeEventKind.PlayerHit && e.Slot == 0))
        {
            if ((e.Flags & HordeEventFlags.Dodged) != 0) { if (++streak == 3) { streak = 0; puffsExpected++; } }
            else streak = 0;
        }
        int puffHits = ev.Count(e => e.Kind == HordeEventKind.Hit && e.Slot == 0 && e.Target == b && e.DefId == 0);
        _out.WriteLine($"puffs {sim.ProcsFired(0, ProcKind.DodgeCounter)} (expected {puffsExpected}), puff hits on the biter {puffHits}");
        Assert.True(puffsExpected >= 2);
        Assert.Equal(puffsExpected, sim.ProcsFired(0, ProcKind.DodgeCounter));
        Assert.Equal(puffsExpected, puffHits);
        Assert.All(ev.Where(e => e.Kind == HordeEventKind.Hit && e.DefId == 0), e => Assert.Equal(20, e.Value));
    }

    // ------------------------------------------------------------------------------------------------ timers

    [Fact]
    public void Shell_takes_the_first_hit_after_its_time_whole()
    {
        var c = Content(new PassiveProcDef { Kind = ProcKind.Shell, Body = ProcBody.Shell, Seconds = 8f });
        var sim = Sim(c, out var me, 17, 2);
        me.Weapons.Clear();
        Put(sim, Biter, 0.7, 0);
        var ev = Run(sim, T * 7);
        Assert.Equal(0, sim.ProcsFired(0, ProcKind.Shell));
        int landed7 = ev.Count(e => e.Kind == HordeEventKind.PlayerHit && e.Value > 0);
        Assert.True(landed7 >= 6);
        ev = Run(sim, T * 3, null, ev);
        Assert.Equal(1, sim.ProcsFired(0, ProcKind.Shell));
        int landed = ev.Count(e => e.Kind == HordeEventKind.PlayerHit && e.Value > 0);
        Assert.Equal(10 * landed, (int)me.DamageTaken);
        var crack = ev.First(e => e.Kind == HordeEventKind.Proc);
        _out.WriteLine($"shell cracked at {crack.Tick / (double)T:F2} s; {landed} hits landed, HP {me.Hp}");
        Assert.InRange(crack.Tick, 8 * T, 9 * T);
    }

    [Fact]
    public void Coat_soaks_then_fills_again_after_four_seconds_without_damage()
    {
        var c = Content(new PassiveProcDef { Kind = ProcKind.Coat, Body = ProcBody.Coat, Seconds = 4f, Power = 2 });
        var sim = Sim(c, out var me, 17, 2);
        me.Weapons.Clear();
        Run(sim, 2);
        int full = sim.CoatMax(0);
        Assert.Equal(20, full); // 2 % of 1000
        Assert.Equal(full, sim.CoatLeft(0));
        int b = Put(sim, Biter, 0.7, 0);
        var ev = Run(sim, T * 2);
        Assert.Equal(0, sim.CoatLeft(0));
        int lost = me.MaxHp - me.Hp;
        int landed = ev.Where(e => e.Kind == HordeEventKind.PlayerHit).Sum(e => e.Value);
        int soaked = ev.Where(e => e.Kind == HordeEventKind.Proc && (e.Flags & HordeEventFlags.Dot) != 0).Sum(e => e.Value);
        _out.WriteLine($"coat {full}; soaked {soaked}, HP lost {lost}, landed {landed}");
        Assert.Equal(full, soaked); Assert.Equal(lost, landed); Assert.True(landed > 0);
        sim.Enemies[b].Damage = 0; // the biter stops
        int last = ev.Where(e => e.Kind == HordeEventKind.PlayerHit).Max(e => e.Tick);
        Run(sim, last + 4 * T - 1 - sim.Tick);
        Assert.Equal(0, sim.CoatLeft(0));
        Run(sim, 2);
        Assert.Equal(full, sim.CoatLeft(0));
    }

    [Fact]
    public void StillBoost_standing_two_seconds_doubles_the_passive_and_walking_ends_it()
    {
        var c = Content(new PassiveProcDef { Kind = ProcKind.StillBoost, Body = ProcBody.Motes, Seconds = 2f, Mul = 2f }, new ModDef(Mod.Regen, 2f));
        var sim = Sim(c, out var me);
        Run(sim, 2 * T - 2);
        Assert.Equal(2, me.S.Regen, 3);
        Run(sim, 3);
        Assert.True(sim.ProcActive(0, ProcKind.StillBoost));
        Assert.Equal(4, me.S.Regen, 3);
        Run(sim, 3, (127, 0));
        Assert.False(sim.ProcActive(0, ProcKind.StillBoost));
        Assert.Equal(2, me.S.Regen, 3);
        // a level taken while the boost holds: the boost follows the new level (here: 2 → 2 regen at every level; the test passive's levels are alike, so the total stays 4)
        Run(sim, 2 * T + 2);
        Assert.Equal(4, me.S.Regen, 3);
        Assert.True(sim.GivePassive(0, TestPassive, 2));
        Run(sim, 2);
        Assert.True(sim.ProcActive(0, ProcKind.StillBoost));
        Assert.Equal(4, me.S.Regen, 3);
    }

    [Fact]
    public void Stride_three_seconds_of_walking_charge_the_next_hit()
    {
        var c = Content(new PassiveProcDef { Kind = ProcKind.Stride, Body = ProcBody.Dust, Seconds = 3f, Power = 50 });
        var sim = Sim(c, out var me);
        Run(sim, 3 * T + 4, (0, 127));
        Assert.Equal(255, StateOf(sim, 0, ProcKind.Stride).Charge);
        int d = Put(sim, Dummy, 3, 0);
        var hits = MyHits(Run(sim, T * 4), d);
        _out.WriteLine("hits: " + string.Join(" ", hits));
        Assert.Equal(12, hits[0]); Assert.Equal(8, hits[1]);
    }

    [Fact]
    public void IdleCharge_a_hit_after_three_quiet_seconds_is_stronger()
    {
        var c = Content(new PassiveProcDef { Kind = ProcKind.IdleCharge, Body = ProcBody.Glow, Seconds = 3f, Power = 60 });
        var sim = Sim(c, out _);
        int d = Put(sim, Dummy, 3, 0);
        var hits = MyHits(Run(sim, T * 3), d);
        Assert.Equal(13, hits[0]); Assert.All(hits.Skip(1), h => Assert.Equal(8, h));
        sim.Enemies[d].X += 30 * U; // out of reach: 3 s without a hit
        Run(sim, T * 4);
        sim.Enemies[d].X -= 30 * U;
        hits = MyHits(Run(sim, T * 2), d);
        _out.WriteLine("after the lull: " + string.Join(" ", hits));
        Assert.Equal(13, hits[0]);
    }

    [Fact]
    public void MagnetPulse_pulls_the_gems_within_its_area_every_period()
    {
        var c = Content(new PassiveProcDef { Kind = ProcKind.MagnetPulse, Body = ProcBody.PickupRing, Seconds = 5f, Area = 6f });
        var sim = Sim(c, out var me);
        me.Weapons.Clear();
        var gem = c.Pickup(1)!;
        sim.Pickups.Add(new SimPickup { Id = 9001, Def = gem, X = me.X + 5 * U, Y = me.Y, Value = 1 });
        sim.Pickups.Add(new SimPickup { Id = 9002, Def = gem, X = me.X - 8 * U, Y = me.Y, Value = 1 });
        Run(sim, 5 * T - 2);
        Assert.Equal(-1, sim.Pickups.First(k => k.Id == 9001).PulledBy);
        Run(sim, 3);
        Assert.Equal(1, sim.ProcsFired(0, ProcKind.MagnetPulse));
        var near = sim.Pickups.FirstOrDefault(k => k.Id == 9001);
        Assert.True(near == null || near.PulledBy == 0);
        Assert.Equal(-1, sim.Pickups.First(k => k.Id == 9002).PulledBy);
    }

    [Fact]
    public void TimerCharge_doubles_the_next_magic_use_and_comes_back_after_its_time()
    {
        var c = Content(new PassiveProcDef { Kind = ProcKind.TimerCharge, Body = ProcBody.Glow, Seconds = 3f, Power = 100, ClassMask = 1 << (int)DamageClass.Magic });
        var sim = Sim(c, out _, 17, 4); // mg_firebolt: 14 × 2 bolts, magic
        int d = Put(sim, Dummy, 3, 0);
        var ev = Run(sim, T * 2);
        Assert.All(MyHits(ev, d), h => Assert.Equal(14, h));
        ev = Run(sim, T * 3);
        var hits = MyHits(ev, d);
        _out.WriteLine("hits: " + string.Join(" ", hits));
        Assert.Contains(28, hits);
        Assert.Equal(1, sim.ProcsFired(0, ProcKind.TimerCharge));
    }

    // ------------------------------------------------------------------------------------------------ thresholds and touches

    [Fact]
    public void LowHpBoost_under_its_threshold_gives_its_damage_and_stops_above()
    {
        var c = Content(new PassiveProcDef { Kind = ProcKind.LowHpBoost, Body = ProcBody.RedAura, Threshold = 25, Power = 30 });
        var sim = Sim(c, out var me);
        Run(sim, 2);
        Assert.Equal(0, me.S.DamagePct, 3);
        me.Hp = me.MaxHp / 5;
        Run(sim, 1);
        Assert.True(sim.ProcActive(0, ProcKind.LowHpBoost));
        Assert.Equal(30, me.S.DamagePct, 3);
        me.Hp = me.MaxHp;
        Run(sim, 1);
        Assert.Equal(0, me.S.DamagePct, 3);
    }

    [Fact]
    public void LowHpHide_falling_under_its_threshold_makes_the_runner_untouchable_once_per_cooldown()
    {
        var c = Content(new PassiveProcDef { Kind = ProcKind.LowHpHide, Body = ProcBody.Mist, Threshold = 30, Seconds = 2f, Cooldown = 30f });
        var sim = Sim(c, out var me, 17, 2);
        me.Weapons.Clear();
        me.Hp = 305;
        Put(sim, Biter, 0.7, 0);
        var ev = Run(sim, T * 2);
        Assert.Equal(1, sim.ProcsFired(0, ProcKind.LowHpHide));
        int hits = ev.Count(e => e.Kind == HordeEventKind.PlayerHit && e.Value > 0);
        _out.WriteLine($"hits in the first 2 s: {hits}, HP {me.Hp}");
        Assert.Equal(1, hits); // the hit that took it under 30 %, then nothing for 2 s
        Assert.Equal(295, me.Hp);
        ev = Run(sim, T * 3);
        Assert.True(ev.Count(e => e.Kind == HordeEventKind.PlayerHit && e.Value > 0) >= 4);
        Assert.Equal(1, sim.ProcsFired(0, ProcKind.LowHpHide)); // once per 30 s
    }

    [Fact]
    public void HurtCounter_and_Reflect_answer_a_touch_and_not_a_shot()
    {
        var counter = Content(new PassiveProcDef { Kind = ProcKind.HurtCounter, Body = ProcBody.Slash, Damage = 30, Cooldown = 1f });
        var sim = Sim(counter, out var me, 17, 2);
        me.Weapons.Clear();
        int b = Put(sim, Biter, 0.7, 0);
        var ev = Run(sim, T * 4);
        var back = ev.Where(e => e.Kind == HordeEventKind.Hit && e.Slot == 0 && e.Target == b && e.DefId == 0).ToList();
        int touches = ev.Count(e => e.Kind == HordeEventKind.PlayerHit);
        _out.WriteLine($"counter: {touches} touches, {back.Count} counters");
        Assert.True(back.Count >= 3 && back.Count < touches);
        Assert.All(back, e => Assert.Equal(30, e.Value));
        for (int i = 1; i < back.Count; i++) Assert.True(back[i].Tick - back[i - 1].Tick >= T);

        var reflect = Content(new PassiveProcDef { Kind = ProcKind.Reflect, Body = ProcBody.Shield, Power = 50 });
        sim = Sim(reflect, out me, 17, 2);
        me.Weapons.Clear();
        b = Put(sim, Biter, 0.7, 0);
        ev = Run(sim, T * 3);
        back = ev.Where(e => e.Kind == HordeEventKind.Hit && e.Slot == 0 && e.Target == b && e.DefId == 0).ToList();
        Assert.Equal(ev.Count(e => e.Kind == HordeEventKind.PlayerHit), back.Count);
        Assert.All(back, e => Assert.Equal(5, e.Value));
    }

    [Fact]
    public void BossBlock_blocks_an_elites_hit_once_per_cooldown_and_never_a_normal_ones()
    {
        var c = Content(new PassiveProcDef { Kind = ProcKind.BossBlock, Body = ProcBody.Shield, Cooldown = 20f });
        var sim = Sim(c, out var me, 17, 2);
        me.Weapons.Clear();
        Put(sim, Biter, 0.7, 0);
        Run(sim, T * 2);
        Assert.Equal(0, sim.ProcsFired(0, ProcKind.BossBlock));
        var c2 = Content(new PassiveProcDef { Kind = ProcKind.BossBlock, Body = ProcBody.Shield, Cooldown = 20f });
        sim = Sim(c2, out me, 17, 2);
        me.Weapons.Clear();
        Put(sim, EliteBiter, 0.7, 0);
        var ev = Run(sim, T * 3);
        Assert.Equal(1, sim.ProcsFired(0, ProcKind.BossBlock));
        Assert.Equal(10 * ev.Count(e => e.Kind == HordeEventKind.PlayerHit && e.Value > 0), (int)me.DamageTaken);
    }

    [Fact]
    public void Cloak_enemies_lose_a_still_runner_until_it_moves()
    {
        var c = Content(new PassiveProcDef { Kind = ProcKind.Cloak, Body = ProcBody.Mist, Seconds = 1f, Area = 4f, Cooldown = 8f });
        var sim = Sim(c, out var me);
        me.Weapons.Clear();
        int w = Put(sim, Walker, 8, 0);
        Run(sim, T + 2);
        Assert.True(sim.ProcActive(0, ProcKind.Cloak));
        int x0 = sim.Enemies[w].X;
        Run(sim, T);
        Assert.Equal(x0, sim.Enemies[w].X); // it lost its runner: it stands
        Run(sim, 4, (127, 0));
        Assert.False(sim.ProcActive(0, ProcKind.Cloak));
        x0 = sim.Enemies[w].X;
        Run(sim, T / 2);
        Assert.NotEqual(x0, sim.Enemies[w].X);
    }

    [Fact]
    public void Gangster_still_two_seconds_and_normal_touches_miss_for_its_time()
    {
        var c = Content(new PassiveProcDef { Kind = ProcKind.Gangster, Body = ProcBody.Mist, Seconds = 2f, Area = 3f, Cooldown = 12f });
        var sim = Sim(c, out var me, 17, 2);
        me.Weapons.Clear();
        Run(sim, 2 * T + 2);
        Assert.True(sim.ProcActive(0, ProcKind.Gangster));
        Put(sim, Biter, 0.7, 0);
        var ev = Run(sim, 2 * T);
        Assert.Equal(0, ev.Count(e => e.Kind == HordeEventKind.PlayerHit));
        ev = Run(sim, 2 * T);
        Assert.True(ev.Count(e => e.Kind == HordeEventKind.PlayerHit) >= 2);
    }

    [Fact]
    public void DodgeReady_a_dodge_fires_the_named_weapon_at_once()
    {
        var c = Content(new PassiveProcDef { Kind = ProcKind.DodgeReady, Body = ProcBody.AfterImage, Arg = 2 }, new ModDef(Mod.Dodge, 60));
        var sim = Sim(c, out _, 29, 2);
        Put(sim, Biter, 0.7, 0);
        var ev = Run(sim, T * 20);
        int fired = sim.ProcsFired(0, ProcKind.DodgeReady);
        var swings = ev.Where(e => e.Kind == HordeEventKind.Swing).Select(e => e.Tick).ToList();
        int quick = 0; for (int i = 1; i < swings.Count; i++) if (swings[i] - swings[i - 1] < 48) quick++;
        _out.WriteLine($"{fired} dodges fired Bash at once; {quick} swings came before the wait");
        Assert.True(fired >= 2);
        Assert.Equal(fired, quick);
    }

    // ------------------------------------------------------------------------------------------------ kills, zones

    [Fact]
    public void Bane_kills_of_its_targets_drop_heal_motes_at_most_once_per_cooldown()
    {
        var c = Content(new PassiveProcDef { Kind = ProcKind.Bane, Body = ProcBody.RaceIcon, Chance = 100, Cooldown = 3f, PickupId = 241, RaceMask = 1 << (int)Race.Plant });
        var sim = Sim(c, out var me);
        me.Weapons[0].Level = 3;
        var poring = c.Enemy(1)!; // Plant
        var drops = new List<int>(); var kills = new List<int>();
        for (int i = 0; i < T * 10; i++)
        {
            if (sim.EnemiesAlive < 3) sim.Spawn(poring, me.X + 3 * U, me.Y + (i % 3 - 1) * U, true);
            sim.Step();
            foreach (var e in sim.Events) { if (e.Kind == HordeEventKind.Proc) drops.Add(e.Tick); if (e.Kind == HordeEventKind.EnemyDied && e.Slot == 0) kills.Add(e.Tick); }
            sim.ClearOutputs();
        }
        _out.WriteLine($"motes at ticks {string.Join(" ", drops)}; kills at {string.Join(" ", kills)}");
        Assert.True(me.Kills > drops.Count);
        Assert.True(drops.Count >= 3);
        for (int i = 1; i < drops.Count; i++) Assert.True(drops[i] - drops[i - 1] >= 3 * T);
    }

    [Fact]
    public void KillHeal_heals_on_a_magic_kill_and_KillDrop_bundles_give_a_shot()
    {
        var c = Content(new PassiveProcDef { Kind = ProcKind.KillHeal, Body = ProcBody.Motes, Damage = 5, ClassMask = 1 << (int)DamageClass.Magic });
        var sim = Sim(c, out var me, 17, 4);
        me.Hp = 50;
        var poring = c.Enemy(1)!;
        for (int i = 0; i < T * 6; i++) { if (sim.EnemiesAlive < 2) sim.Spawn(poring, me.X + 3 * U, me.Y, true); me.Hp = Math.Min(me.Hp, 50); sim.Step(); sim.ClearOutputs(); }
        Assert.True(sim.ProcsFired(0, ProcKind.KillHeal) >= 2);

        var c2 = Content(new PassiveProcDef { Kind = ProcKind.KillDrop, Body = ProcBody.Trail, Chance = 100, Seconds = 10f, PickupId = 242 });
        sim = Sim(c2, out me);
        int bundles = 0, takenAt = -1;
        for (int i = 0; i < T * 6 && bundles == 0; i++)
        {
            if (sim.EnemiesAlive < 1) sim.Spawn(c2.Enemy(1)!, me.X + (int)(1.2 * U), me.Y, true);
            sim.Step();
            foreach (var e in sim.Events) if (e.Kind == HordeEventKind.PickupTaken && e.DefId == 242) { bundles++; takenAt = e.Tick; }
            sim.ClearOutputs();
        }
        Assert.True(bundles >= 1);
        Assert.Equal(1, me.S.ProjectileCount, 3);
        foreach (var e in sim.Enemies) e.Alive = false; // nothing more to kill: no new bundle
        sim.Pickups.RemoveAll(k => k.Def.Id == 242);
        Run(sim, takenAt + 10 * T - 1 - sim.Tick);
        Assert.Equal(1, me.S.ProjectileCount, 3);
        Run(sim, 2);
        Assert.Equal(0, me.S.ProjectileCount, 3);
    }

    [Fact]
    public void OwnZone_inside_ones_own_zone_gives_its_damage()
    {
        var c = Content(new PassiveProcDef { Kind = ProcKind.OwnZone, Body = ProcBody.ZonePulse, Power = 10 });
        var sim = Sim(c, out var me, 17, TestZoneWeapon);
        Run(sim, 4);
        Assert.True(sim.ProcActive(0, ProcKind.OwnZone));
        Assert.Equal(10, me.S.DamagePct, 3);
        Run(sim, 2 * T, (127, 0)); // 8 cells away from the zone (3 cells wide), it stays where it was laid
        Assert.False(sim.ProcActive(0, ProcKind.OwnZone));
        Assert.Equal(0, me.S.DamagePct, 3);
    }

    // ------------------------------------------------------------------------------------------------ the wire, determinism

    static HordeProcView StateOf(HordeSim sim, int slot, ProcKind k)
    {
        var b = new byte[HordeSim.ProcKindSlots * 4];
        int n = sim.ProcStates(slot, b);
        for (int i = 0; i < n; i++) if (b[i * 4] == (byte)k) return new HordeProcView { Kind = k, Charge = b[i * 4 + 1], Stacks = b[i * 4 + 2], Flags = b[i * 4 + 3] };
        return default;
    }

    [Fact]
    public void The_snapshot_carries_every_runners_families_and_a_delta_only_the_changed_ones()
    {
        var c = HordeTables.Build();
        c.Waves.Clear();
        var sim = new HordeSim(c, c.Arenas[0], 5, 900);
        sim.AddPlayer(1, "a"); sim.AddPlayer(2, "b");
        Assert.True(sim.GivePassive(0, 2, 3));   // Andre: HitCount
        Assert.True(sim.GivePassive(0, 3, 1));   // Pupa: Shell
        Assert.True(sim.GivePassive(0, 4, 1));   // Zombie: StillBoost
        Assert.True(sim.GivePassive(1, 21, 2));  // Energy Coat
        var enc = new SnapshotEncoder(sim); var dec = new SnapshotDecoder(); var view = new HordeWorldView();
        var run = new HordeRunInfo { X0 = c.Arenas[0].X0, Y0 = c.Arenas[0].Y0, X1 = c.Arenas[0].X1, Y1 = c.Arenas[0].Y1, Slot = 0 };
        dec.Reset(run);
        for (int t = 0; t < T * 12; t++)
        {
            sim.Step(); enc.Collect(); sim.ClearOutputs();
            if (t % 2 != 1) continue;
            enc.Encode(t == 1, out var delta, out var key);
            var body = t == 1 ? key! : delta;
            Assert.True(dec.Apply(body, 0, body.Length, view, 0, t / (double)T));
            // the block is the body's last bytes: count its runners
            for (int s = 0; s < 2; s++)
            {
                var want = new byte[HordeSim.ProcKindSlots * 4]; int n = sim.ProcStates(s, want);
                Assert.Equal(n, view.Players[s].Procs.Count);
                for (int i = 0; i < n; i++)
                {
                    var v = view.Players[s].Procs[i];
                    Assert.Equal(want[i * 4], (byte)v.Kind); Assert.Equal(want[i * 4 + 1], v.Charge); Assert.Equal(want[i * 4 + 2], v.Stacks); Assert.Equal(want[i * 4 + 3], v.Flags);
                }
            }
        }
        Assert.Equal(3, view.Players[0].Procs.Count); // HitCount, Shell, StillBoost: in the order of the passives
        Assert.Equal(new[] { ProcKind.HitCount, ProcKind.Shell, ProcKind.StillBoost }, view.Players[0].Procs.Select(p => p.Kind).ToArray());
        Assert.Equal(ProcKind.Coat, view.Players[1].Procs.Single().Kind);
        Assert.True(view.Players[0].Procs[1].Ready); // 12 s: the shell is up
    }

    [Fact]
    public void Runs_with_passives_are_deterministic_tick_for_tick()
    {
        ulong Play(out string fired)
        {
            var c = HordeTables.Build();
            var sim = new HordeSim(c, c.Arenas[0], 99, 300);
            for (int i = 0; i < 3; i++) sim.AddPlayer((uint)(6000 + i), "bot" + i);
            int[][] builds = { new[] { 1, 2, 3, 4, 6, 41 }, new[] { 7, 11, 21, 40, 101, 190 }, new[] { 5, 30, 50, 61, 130, 181 } };
            for (int i = 0; i < 3; i++) foreach (int id in builds[i]) sim.GivePassive(i, id, 3);
            ushort seq = 0; ulong h = 0;
            for (int t = 0; t < T * 150 && !sim.Over; t++)
            {
                seq++;
                for (int i = 0; i < 3; i++) HordeTests.BotStep(sim, i, seq, i);
                sim.Step(); sim.ClearOutputs();
                if (t % 200 == 0) h = h * 31 + sim.StateHash();
            }
            var kinds = Enum.GetValues<ProcKind>().Where(k => k != ProcKind.None);
            fired = string.Join(", ", kinds.Select(k => $"{k} {Enumerable.Range(0, 3).Sum(s => sim.ProcsFired(s, k))}").Where(s => !s.EndsWith(" 0")));
            return h ^ sim.StateHash();
        }
        ulong a = Play(out var fa), b = Play(out var fb);
        _out.WriteLine("triggers: " + fa);
        Assert.Equal(a, b); Assert.Equal(fa, fb);
        Assert.Contains("HitCount", fa); Assert.Contains("CritCount", fa);
    }
}
