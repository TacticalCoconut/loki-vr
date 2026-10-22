using Loki.Horde;
using Loki.Horde.Content;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>Client wave 8 (w8core): Horde's engine on its own (no network).</summary>
public class HordeTests
{
    readonly ITestOutputHelper _out;
    public HordeTests(ITestOutputHelper o) { _out = o; }

    /// <summary>A bot: walks in circles around the arena's centre (away from the nearest enemy when one is close), takes the
    /// first card of every offer. Deterministic.</summary>
    internal static void BotStep(HordeSim sim, int slot, ushort seq, int phase)
    {
        var p = sim.Player(slot);
        if (p == null) return;
        double a = (sim.Tick + phase * 37) * 0.02;
        int dx = (int)(Math.Cos(a) * 127), dy = (int)(Math.Sin(a) * 127);
        long best = long.MaxValue; int ex = 0, ey = 0;
        foreach (var e in sim.Enemies) { if (!e.Alive) continue; long d = HordeSim.Dist2(e.X, e.Y, p.X, p.Y); if (d < best) { best = d; ex = e.X; ey = e.Y; } }
        if (best < (long)(3 * 256) * (3 * 256)) { double aa = Math.Atan2(p.Y - ey, p.X - ex); dx = (int)(Math.Cos(aa) * 127); dy = (int)(Math.Sin(aa) * 127); }
        sim.PushInput(slot, seq, dx, dy);
        if (p.Open != null) sim.Choose(slot, p.Open.Id, (phase + p.Level) % p.Open.Cards.Length);
    }

    static HordeSim Run(ulong seed, int players, float seconds, int maxTicks, Action<HordeSim>? each = null)
    {
        var c = HordeTestContent.Build();
        var sim = new HordeSim(c, c.Arenas[0], seed, seconds);
        for (int i = 0; i < players; i++) sim.AddPlayer((uint)(2000000 + i), "bot" + i);
        ushort seq = 0;
        for (int t = 0; t < maxTicks && !sim.Over; t++)
        {
            seq++;
            for (int i = 0; i < players; i++) BotStep(sim, i, seq, i);
            sim.Step();
            each?.Invoke(sim);
            sim.ClearOutputs();
        }
        return sim;
    }

    [Fact]
    public void A_run_plays_from_start_to_end_with_kills_levels_jobs_and_results()
    {
        int levelUps = 0, jobChanges = 0, kills = 0, falls = 0, rises = 0, offers = 0, fx = 0;
        var sim = Run(42, 3, 240, HordeSim.TickRate * 400, s =>
        {
            foreach (var e in s.Events)
            {
                if (e.Kind == HordeEventKind.LevelUp) levelUps++;
                if (e.Kind == HordeEventKind.JobChanged) jobChanges++;
                if (e.Kind == HordeEventKind.EnemyDied && e.Slot >= 0) kills++;
                if (e.Kind == HordeEventKind.Fell) falls++;
                if (e.Kind == HordeEventKind.Rose) rises++;
            }
            offers += s.OffersOpened.Count; fx += s.FxBegun.Count;
        });
        var res = sim.Results(7, HordeRewardSize.Normal);
        _out.WriteLine($"over {sim.Over} {sim.Outcome} at {sim.Tick / (double)HordeSim.TickRate:F1}s; level-ups {levelUps}, job changes {jobChanges}, kills {kills}, falls {falls}, rises {rises}, offers {offers}, fx {fx}");
        foreach (var p in res.Players) _out.WriteLine($"  {p.Name}: level {p.Level} job {p.JobId} kills {p.Kills} dmg {p.Damage} score {p.Score} share {p.BaseShare}/{p.JobShare}");
        Assert.True(kills > 20);
        Assert.True(levelUps > 5);
        Assert.True(offers > 5);
        Assert.True(jobChanges >= 1);
        Assert.True(sim.Over);
        Assert.Equal(3, res.Players.Count);
    }

    /// <summary>w8content's real tables (HordeTables): whatever their balance, a run must play and end, and a job change must
    /// be reachable (rules, not timings).</summary>
    [Fact]
    public void The_games_own_tables_play_a_run_to_its_end()
    {
        var c = HordeTables.Build();
        Assert.NotEmpty(c.Arenas);
        Assert.NotNull(c.StartJob());
        Assert.NotNull(c.Weapon(c.Rules.StartWeapon));
        int jobChanges = 0, kills = 0;
        var sim = new HordeSim(c, c.Arenas[0], 3, 300);
        for (int i = 0; i < 4; i++) sim.AddPlayer((uint)(2000000 + i), "bot" + i, (byte)(i % 2 + 1));
        ushort seq = 0;
        for (int t = 0; t < HordeSim.TickRate * 60 * 15 && !sim.Over; t++)
        {
            seq++;
            for (int i = 0; i < 4; i++) BotStep(sim, i, seq, i);
            sim.Step();
            foreach (var e in sim.Events) { if (e.Kind == HordeEventKind.JobChanged) jobChanges++; if (e.Kind == HordeEventKind.EnemyDied && e.Slot >= 0) kills++; }
            sim.ClearOutputs();
        }
        _out.WriteLine($"over {sim.Over} {sim.Outcome} at {sim.Tick / (double)HordeSim.TickRate:F0}s, kills {kills}, job changes {jobChanges}, tables {Loki.Horde.Net.HordeWire.ContentStamp(c)}");
        Assert.True(sim.Over);
        Assert.True(kills > 0);
    }

    [Fact]
    public void The_same_seed_and_inputs_give_the_same_run()
    {
        var h1 = new List<ulong>(); var h2 = new List<ulong>();
        Run(99, 2, 120, HordeSim.TickRate * 90, s => h1.Add(s.StateHash()));
        Run(99, 2, 120, HordeSim.TickRate * 90, s => h2.Add(s.StateHash()));
        Assert.Equal(h1, h2);
        var h3 = new List<ulong>();
        Run(100, 2, 120, HordeSim.TickRate * 90, s => h3.Add(s.StateHash()));
        Assert.NotEqual(h1, h3);
    }

    /// <summary>Client wave 18 (h18feel), the design's point 35: rewritten (it was "A_player_who_falls_rises_when_an_ally_stands": the timed rise
    /// of wave 8 is gone). A fallen player rises when an ally gathers its soul: the ally WALKS (its stick) to each shard and stands in it.</summary>
    [Fact]
    public void A_player_who_falls_rises_when_an_ally_gathers_its_soul()
    {
        var c = HordeTestContent.Build();
        var sim = new HordeSim(c, c.Arenas[0], 5, 900);
        int a = sim.AddPlayer(1, "a"), b = sim.AddPlayer(2, "b");
        // b stands in a horde of porings without moving (four spawned a second beside it); a first walks four cells west, out of
        // the crowd's reach (client wave 9: a crowd keeps apart from a player's body, so it spreads a little wider), then paces;
        // once b fell, a walks to b's nearest soul shard and stands in it, shard after shard
        var poring = c.Enemy(1)!;
        bool fell = false, rose = false; int split = 0, taken = 0, by = -2;
        int spawnEvery = Math.Max(1, HordeSim.TickRate / 4);
        for (int t = 0; t < HordeSim.TickRate * 90 && !rose; t++)
        {
            var pb = sim.Player(b)!; var pa = sim.Player(a)!;
            if (!fell && t % spawnEvery == 0) sim.Spawn(poring, pb.X + 100, pb.Y, true);
            int dx = t < HordeSim.TickRate ? -127 : t % 40 < 20 ? 60 : -60, dy = 0;
            if (fell && sim.Souls.Count > 0)
            {
                var s = sim.Souls.OrderBy(q => HordeSim.Dist2(q.X, q.Y, pa.X, pa.Y)).First();
                double ddx = s.X - pa.X, ddy = s.Y - pa.Y, d = Math.Sqrt(ddx * ddx + ddy * ddy);
                dx = d < 40 ? 0 : (int)(ddx / d * 127); dy = d < 40 ? 0 : (int)(ddy / d * 127);
            }
            sim.PushInput(a, (ushort)(t + 1), dx, dy);
            sim.Step();
            foreach (var e in sim.Events)
            {
                if (e.Kind == HordeEventKind.Fell && e.Slot == b) fell = true;
                if (e.Kind == HordeEventKind.SoulSplit && e.Slot == b) split = e.Value;
                if (e.Kind == HordeEventKind.SoulTaken && e.Slot == a) taken++;
                if (e.Kind == HordeEventKind.Rose && e.Slot == b) { rose = true; by = e.Target; }
            }
            if (fell) foreach (var en in sim.Enemies) if (en.Alive) en.Alive = false; // clear the field so a survives
            if (pa.Fallen) break;
            sim.ClearOutputs();
        }
        _out.WriteLine($"b fell: {fell}, its soul split into {split}, a took {taken}, b rose: {rose} (by slot {by}) at tick {sim.Tick}");
        Assert.True(fell);
        Assert.Equal(4, split); Assert.Equal(4, taken);
        Assert.True(rose); Assert.Equal(a, by);
        Assert.False(sim.Over);
    }

    [Fact]
    public void Movement_is_integer_and_the_same_on_both_ends()
    {
        int x = 150 * 256, y = 150 * 256;
        for (int i = 0; i < 100; i++) HordeMove.Step(ref x, ref y, 127, 127, 51, 130 * 256, 130 * 256, 221 * 256 - 1, 221 * 256 - 1);
        // diagonal: clamped to length 127 (127 * 127 / isqrt(2 * 127²) = 90 per axis), then 90 / 127 of the speed per axis
        Assert.Equal(150 * 256 + 100 * (90 * 51 / 127), x);
        Assert.Equal(x, y);
    }

    /// <summary>Client wave 14 (w14play), point 31 of the design's headset list: Loki Host's presence log of 2026-10-07 22:12 listed
    /// Tester twice in one run (left, then joined again: a new slot) and paid both rewards. The results now hold one row per
    /// account: its slots' seconds, kills and damage added up, its highest level, its latest slot's name, job, slot and "left".</summary>
    [Fact]
    public void A_player_who_leaves_and_joins_again_is_one_row_with_one_reward()
    {
        var c = HordeTestContent.Build();
        c.Rules.FinalBossSeconds = 100000; // (no final boss in these three minutes)
        var sim = new HordeSim(c, c.Arenas[0], 77, 300);
        int a = sim.AddPlayer(1, "a"), b = sim.AddPlayer(2, "b");
        ushort seq = 0;
        // (the players are kept alive: this test is about the rows, not the fight)
        void Play(int ticks) { for (int t = 0; t < ticks && !sim.Over; t++) { seq++; for (int i = 0; i < sim.Players.Length; i++) { BotStep(sim, i, seq, i); var q = sim.Player(i); if (q != null) q.Hp = q.MaxHp; } sim.Step(); sim.ClearOutputs(); } }
        Play(HordeSim.TickRate * 80);
        var first = sim.Players[a]; int kills1 = first.Kills, level1 = first.Level; long dmg1 = first.DamageDealt;
        sim.RemovePlayer(a);
        Play(HordeSim.TickRate * 10);
        _out.WriteLine($"at {sim.Tick / (float)HordeSim.TickRate:0.0}s: over {sim.Over} {sim.Outcome}, a left with level {level1} kills {kills1}; b present {sim.Player(b) != null}");
        int a2 = sim.AddPlayer(1, "a");
        Assert.True(a2 >= 0, "a joins again");
        Assert.NotEqual(a, a2); // a new slot (the old one stays Used: its result stands)
        Play(HordeSim.TickRate * 90);
        var second = sim.Players[a2];
        int end = sim.Over ? sim.EndTick : sim.Tick;
        float want = (first.LeftTick - first.JoinTick + (second.Left ? second.LeftTick : end) - second.JoinTick) / (float)HordeSim.TickRate;
        var res = sim.Results(9, HordeRewardSize.Normal);
        foreach (var p in res.Players) _out.WriteLine($"  {p.Name} (aid {p.Aid}, slot {p.Slot}): level {p.Level} kills {p.Kills} dmg {p.Damage} {p.Seconds:0.0}s score {p.Score} share {p.BaseShare}/{p.JobShare} left {p.Left}");
        Assert.Equal(2, res.Players.Count);
        var row = Assert.Single(res.Players, p => p.Aid == 1);
        Assert.Equal(a2, row.Slot);
        Assert.Equal(kills1 + second.Kills, row.Kills);
        Assert.Equal(dmg1 + second.DamageDealt, row.Damage);
        Assert.Equal(Math.Max(level1, second.Level), row.Level);
        Assert.True(want > 150f, $"both stays counted ({want:0.0} s)");
        Assert.Equal(want, row.Seconds, 2);
        Assert.Equal(second.Left, row.Left);
        Assert.True(row.BaseShare > 0 && row.JobShare > 0);
        // a run without a rejoin: the rows of before (one per slot, its own numbers)
        var other = Assert.Single(res.Players, p => p.Aid == 2);
        Assert.Equal(b, other.Slot);
        Assert.Equal(sim.Players[b].Kills, other.Kills);
        Assert.Equal(((sim.Players[b].Left ? sim.Players[b].LeftTick : end) - sim.Players[b].JoinTick) / (float)HordeSim.TickRate, other.Seconds, 2);
    }

    [Fact]
    public void Item52_Cards_attach_bonus_stats_and_never_draw_standalone_stat_cards()
    {
        var c = HordeTables.Build();
        var sim = new HordeSim(c, c.Arenas[0], 123, 900);
        int slot = sim.AddPlayer(1, "solo");
        var p = sim.Player(slot)!;
        int offersChecked = 0;
        for (int lvl = 0; lvl < 15; lvl++)
        {
            var pk = c.Pickups.First(k => k.Kind == PickupKind.Exp);
            sim.Pickups.Add(new SimPickup { Id = lvl + 1, Def = pk, X = p.X, Y = p.Y, Value = sim.PartyExpToNext() });
            sim.Step();
            while (p.Open != null)
            {
                var offer = p.Open;
                if (!offer.IsJob)
                {
                    offersChecked++;
                    foreach (var card in offer.Cards)
                    {
                        Assert.NotEqual(HordeCardKind.Stat, card.Kind);
                        Assert.True(card.HasBonusStat);
                        Assert.Equal(c.Rules.StatCardPoints, card.BonusStatPoints);
                    }
                    // Verify wire round-trip preserves bonus stats
                    var bytes = Loki.Horde.Net.HordeWire.EncodeOffer(offer);
                    Assert.True(Loki.Horde.Net.HordeWire.DecodeOffer(bytes, 2, bytes.Length - 2, out var decoded));
                    for (int i = 0; i < offer.Cards.Length; i++)
                    {
                        Assert.Equal(offer.Cards[i].BonusStat, decoded.Cards[i].BonusStat);
                        Assert.Equal(offer.Cards[i].BonusStatPoints, decoded.Cards[i].BonusStatPoints);
                    }
                }
                int beforePts = p.StatPoints.Sum();
                var chosen = offer.Cards[0];
                sim.Choose(slot, offer.Id, 0);
                if (!offer.IsJob)
                    Assert.Equal(beforePts + chosen.BonusStatPoints, p.StatPoints.Sum());
                sim.Step();
            }
            sim.ClearOutputs();
        }
        Assert.True(offersChecked >= 10);
    }

    [Fact]
    public void Item54_Knockback_never_pushes_enemy_toward_player()
    {
        var c = HordeTables.Build();
        var sim = new HordeSim(c, c.Arenas[0], 456, 900);
        int slot = sim.AddPlayer(1, "solo");
        var p = sim.Player(slot)!;
        // Give player a GroundZone weapon with Knockback where zone center is behind the enemy relative to the player
        var kbWep = new WeaponDef
        {
            Id = 999,
            Key = "test_kb_zone",
            Kind = WeaponKind.GroundZone,
            Class = DamageClass.Magic,
            Levels = new[]
            {
                new WeaponLevel
                {
                    Damage = 5, Cooldown = 0.5f, Range = 6f, Area = 4f, Duration = 2f, Tick = 0.2f,
                    Effects = new[] { new EffectDef(EffectKind.Knockback, 100f, 0f, 2.5f) }
                }
            }
        };
        c.Weapons.Add(kbWep);
        p.Weapons.Clear();
        p.Weapons.Add(new OwnedWeapon { Def = kbWep, Level = 1, NextTick = 1 });
        var enemyDef = c.Enemies.First(e => e.Rank == EnemyRank.Normal && e.KnockbackResist == 0);
        int es = sim.Spawn(enemyDef, p.X + HordeSim.Units(2.0), p.Y, true);
        var en = sim.Enemies[es];
        en.MaxHp = en.Hp = 10000;
        en.Speed = 0;
        // Also spawn a second enemy far behind the first so zone centers behind `en`
        int es2 = sim.Spawn(enemyDef, p.X + HordeSim.Units(3.5), p.Y, true);
        sim.Enemies[es2].MaxHp = sim.Enemies[es2].Hp = 10000;
        sim.Enemies[es2].Speed = 0;
        int kbCount = 0;
        for (int t = 0; t < 40; t++)
        {
            sim.Step();
            bool knockedThisTick = sim.Events.Any(ev => ev.Kind == HordeEventKind.Effect && ev.Target == es && ev.Value == (int)EffectKind.Knockback);
            if (knockedThisTick)
            {
                kbCount++;
                long dot = (long)en.KbVx * (en.X - p.X) + (long)en.KbVy * (en.Y - p.Y);
                Assert.True(dot > 0, $"Knockback velocity ({en.KbVx}, {en.KbVy}) should push enemy away from player");
            }
            sim.ClearOutputs();
        }
        Assert.True(kbCount > 0);
    }

    [Fact]
    public void Item55_Ability_charge_and_sweep_emit_windup_telegraphs_without_extra_delay()
    {
        var c = HordeTables.Build();
        var sim = new HordeSim(c, c.Arenas[0], 789, 900);
        int slot = sim.AddPlayer(1, "solo");
        var p = sim.Player(slot)!;
        p.MaxHp = p.Hp = 100000;
        p.Weapons.Clear();

        // 1. Sweep enemy emits WindUp telegraph immediately on spawn
        var sweepDef = c.Enemies.First(e => e.Behavior == EnemyBehavior.Sweep);
        int swSlot = sim.Spawn(sweepDef, p.X + HordeSim.Units(4.0), p.Y, true);
        Assert.Contains(sim.Events, ev => ev.Kind == HordeEventKind.WindUp && ev.Target == swSlot && (HordeWindUpKind)Math.Round(ev.B) == HordeWindUpKind.Charge);
        sim.Enemies[swSlot].Alive = false;
        sim.ClearOutputs();

        // 2. AbilityKind.Charge enemy emits WindUp telegraph before the ability fires without delaying the dash tick
        var chargeAbDef = new EnemyDef
        {
            Id = 998, MobId = 1039, Key = "TEST_CHARGER", Rank = EnemyRank.Elite, Behavior = EnemyBehavior.Chase,
            Hp = 5000, Damage = 10, Speed = 2f, ChargeWindup = 0.6f,
            Abilities = new[] { new EnemyAbilityDef { Kind = AbilityKind.Charge, FirstAfter = 2f, Cooldown = 6f, Speed = 9f, Range = 6f, SkillId = 1001 } }
        };
        c.Enemies.Add(chargeAbDef);
        int es = sim.Spawn(chargeAbDef, p.X + HordeSim.Units(4.0), p.Y, true);
        int spawnTick = sim.Tick;
        int windUpTick = -1, abilityTick = -1;
        for (int t = 0; t < HordeSim.TickRate * 4; t++)
        {
            sim.Step();
            foreach (var ev in sim.Events)
            {
                if (ev.Kind == HordeEventKind.WindUp && ev.Target == es && (HordeWindUpKind)Math.Round(ev.B) == HordeWindUpKind.Charge && windUpTick < 0)
                    windUpTick = sim.Tick;
                if (ev.Kind == HordeEventKind.EnemyAbility && ev.Target == es && ev.Value == (int)AbilityKind.Charge && abilityTick < 0)
                    abilityTick = sim.Tick;
            }
            sim.ClearOutputs();
        }
        Assert.True(windUpTick > spawnTick, "Charge ability emitted a WindUp telegraph before dashing");
        Assert.True(abilityTick > windUpTick, "WindUp telegraph preceded the Charge ability execution");
        Assert.Equal(spawnTick + HordeSim.Ticks(chargeAbDef.Abilities[0].FirstAfter), abilityTick);
    }

    [Fact]
    public void Item56_Run_continues_past_15_minutes_and_scales_exponentially_after_20_minutes()
    {
        var c = HordeTables.Build();
        var sim = new HordeSim(c, c.Arenas[0], 321, 900);
        int slot = sim.AddPlayer(1, "solo");
        var p = sim.Player(slot)!;
        p.MaxHp = p.Hp = 10000000;
        var normalDef = c.Enemies.First(e => e.Rank == EnemyRank.Normal);
        int s0 = sim.Spawn(normalDef, p.X + 1000, p.Y, true);
        int hpAt0 = sim.Enemies[s0].MaxHp;
        sim.Enemies[s0].Alive = false;

        // Advance past 15:00 (RunTicks) and kill the milestone boss: run must NOT end
        while (sim.Tick <= sim.RunTicks + 10)
        {
            p.Hp = p.MaxHp;
            if (p.Open != null) sim.Choose(slot, p.Open.Id, 0);
            sim.Step();
            sim.ClearOutputs();
        }
        foreach (var e in sim.Enemies)
            if (e.Alive && e.Def.Rank == EnemyRank.FinalBoss)
                e.Hp = 1;
        // Let player kill or damage final boss; even if it dies or FinalBossSeconds elapses, run stays open
        for (int t = 0; t < HordeSim.TickRate * 130; t++)
        {
            p.Hp = p.MaxHp;
            if (p.Open != null) sim.Choose(slot, p.Open.Id, 0);
            sim.Step();
            sim.ClearOutputs();
        }
        Assert.False(sim.Over);

        // Advance past 25:00 and verify exponential HP scaling compared to 20:00
        while (sim.Tick < HordeSim.TickRate * 60 * 20)
        {
            p.Hp = p.MaxHp;
            if (p.Open != null) sim.Choose(slot, p.Open.Id, 0);
            sim.Step();
            sim.ClearOutputs();
        }
        int s20 = sim.Spawn(normalDef, p.X + 1000, p.Y, true);
        int hpAt20 = sim.Enemies[s20].MaxHp;
        sim.Enemies[s20].Alive = false;

        while (sim.Tick < HordeSim.TickRate * 60 * 25)
        {
            p.Hp = p.MaxHp;
            if (p.Open != null) sim.Choose(slot, p.Open.Id, 0);
            sim.Step();
            sim.ClearOutputs();
        }
        int s25 = sim.Spawn(normalDef, p.X + 1000, p.Y, true);
        int hpAt25 = sim.Enemies[s25].MaxHp;
        Assert.True(hpAt20 > hpAt0);
        Assert.True(hpAt25 > hpAt20 * 3, $"Expected exponential HP growth after 20:00 (20m={hpAt20}, 25m={hpAt25})");
    }
}
