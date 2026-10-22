using System.Diagnostics;
using Loki.Horde;
using Loki.Horde.Content;
using Loki.Horde.Net;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>
/// Client wave 18 (h18rules): the design's points 27 (Back Slide: an exploding trail, then 1.5 s unseen, unhurt and faster; a longer wait) and
/// 29 (enemy density +40 %) on the game's own tables (HordeTables.Build), deterministic. Point 23's pause: HordePauseTests and
/// HordeRelayTests (rewritten there).
/// </summary>
public class HordeW18RulesTests
{
    readonly ITestOutputHelper _out;
    public HordeW18RulesTests(ITestOutputHelper o) { _out = o; }

    static HordeContent C() => HordeTables.Build();
    static WeaponDef W(HordeContent c, string key) => c.Weapons.First(w => w.Key == key);
    static EnemyDef E(HordeContent c, string key) => c.Enemies.First(e => e.Key == key);

    static HordeSim Quiet(HordeContent c, int players = 1)
    {
        c.Waves.Clear(); c.Objectives.Enabled = false; c.Elites.Clear();
        var sim = new HordeSim(c, c.Arenas[0], 1818, 900);
        for (int i = 0; i < players; i++) sim.AddPlayer((uint)(5100000 + i), "p" + i);
        foreach (var p in sim.Players) if (p.Present) { p.SafeUntil = 0; p.Weapons.Clear(); }
        return sim;
    }
    static void Give(HordeSim sim, int slot, WeaponDef w, int level)
    {
        sim.Give(slot, new HordeCard { Kind = HordeCardKind.NewWeapon, DefId = w.Id, Level = 1 });
        for (int l = 2; l <= level; l++)
        {
            var ow = sim.Player(slot)!.Weapons.First(o => HordeSim.RootId(o.Def) == w.Id);
            sim.Give(slot, new HordeCard { Kind = HordeCardKind.WeaponLevel, DefId = ow.Def.Id, Level = l });
        }
    }

    // ------------------------------------------------------------------------------------------------ point 27: Back Slide

    [Fact]
    public void Back_Slide_leaves_an_exploding_trail_then_the_runner_is_unseen_unhurt_and_faster()
    {
        var c = C(); var sim = Quiet(c);
        var slide = W(c, "tf_backsliding");
        // the tables: the wait x 1.5 of wave 17's (6 - 0.3 a level), the trail's damage, 0.8 cells, 0.6 s, 1.5 s, +50 % speed
        Assert.Equal(9f, slide.Levels[0].Cooldown, 3); Assert.Equal(7.2f, slide.Levels[4].Cooldown, 3);
        Assert.Equal(27, slide.Levels[0].Damage); Assert.Equal(63, slide.Levels[4].Damage);
        foreach (var l in slide.Levels) { Assert.Equal(0.8f, l.Area, 3); Assert.Equal(0.6f, l.Delay, 3); Assert.Equal(1.5f, l.Duration, 3); Assert.Equal(Mod.MoveSpeedPct, l.Buffs![0].Kind); Assert.Equal(50f, l.Buffs[0].Value, 3); }
        Assert.True(HordeSim.IsSlide(slide));
        Give(sim, 0, slide, 1);
        var p = sim.Player(0)!;
        int speed0 = p.SpeedPerTick;
        var enc = new SnapshotEncoder(sim); var dec = new SnapshotDecoder(); var view = new HordeWorldView();
        dec.Reset(new HordeRunInfo { X0 = sim.Arena.X0, Y0 = sim.Arena.Y0, X1 = sim.Arena.X1, Y1 = sim.Arena.Y1 });
        int n = 0;
        void Step()
        {
            sim.Step(); enc.Collect();
            if (++n % 2 == 0) { enc.Encode(n == 2, out var d, out var k); var body = n == 2 ? k! : d; Assert.True(dec.Apply(body, 0, body.Length, view, 0, n / 40.0)); }
        }
        // a poring touching from the east (it hurts), a crowd of hardy porings on the dash's way west (the trail's targets)
        var poring = E(c, "PORING"); poring.Damage = 5; poring.Speed = 0.3f; poring.Hp = 100000;
        int toucher = sim.Spawn(poring, p.X + 100, p.Y, true);
        var crowd = new List<int>();
        for (int i = 0; i < 4; i++) crowd.Add(sim.Spawn(poring, p.X - 256 * (i + 1), p.Y + 300, true));
        int x0 = p.X, y0 = p.Y;
        var areas = new List<SimFx>(); var strikes = new List<HordeEvent>(); var hits = new List<HordeEvent>(); var hurt = new List<HordeEvent>();
        int dashTick = -1, guard = 0;
        while (dashTick < 0 && guard++ < 200)
        {
            Step();
            foreach (var e in sim.Events) if (e.Kind == HordeEventKind.Dash && e.Slot == 0) dashTick = sim.Tick;
            areas.AddRange(sim.FxBegun.Where(f => f.Kind == HordeFxKind.Warning && f.DefId == slide.Id));
            sim.ClearOutputs();
        }
        Assert.True(dashTick > 0, "Back Slide went off");
        int until = sim.SlideUntil(0);
        _out.WriteLine($"dash at tick {dashTick}, unseen until {until} ({(until - dashTick) / 40.0:0.00} s)");
        Assert.InRange(until - dashTick, HordeSim.Ticks(1.5) + 1, HordeSim.Ticks(1.5) + HordeSim.Ticks(0.2));
        Assert.True(sim.SlideHidden(p));
        Assert.InRange(p.SpeedPerTick, speed0 * 3 / 2 - 1, speed0 * 3 / 2 + 1); // +50 % (whole units a tick)
        // enemies lose the runner: a fast lunatic 8 cells off stands (no target) while the runner is unseen
        var lunatic = E(c, "LUNATIC"); lunatic.Speed = 3f; lunatic.Damage = 0;
        int far = sim.Spawn(lunatic, p.X + 256 * 8, p.Y + 256 * 2, true);
        long dFar0 = HordeSim.Dist2(sim.Enemies[far].X, sim.Enemies[far].Y, x0, y0);
        // the dash and the time after it: the trail, its strikes, nothing hurts the runner, nothing chases it
        int hp0 = p.Hp;
        bool vanishedSeen = false;
        for (int t = 0; t < 60; t++)
        {
            Step();
            areas.AddRange(sim.FxBegun.Where(f => f.Kind == HordeFxKind.Warning && f.DefId == slide.Id));
            strikes.AddRange(sim.Events.Where(e => e.Kind == HordeEventKind.Strike && e.DefId == slide.Id));
            hits.AddRange(sim.Hits.Where(h => h.DefId == slide.Id));
            hurt.AddRange(sim.Events.Where(e => e.Kind == HordeEventKind.PlayerHit && e.Slot == 0 && e.Value > 0));
            if ((view.Players[0].Flags & HordePlayerFlags.Vanished) != 0) vanishedSeen = true;
            // the toucher is put next to the runner each tick: it touches, nothing lands
            sim.Enemies[toucher].X = p.X + 100; sim.Enemies[toucher].Y = p.Y;
            sim.ClearOutputs();
        }
        _out.WriteLine($"trail: {areas.Count} areas at {string.Join(" ", areas.Select(f => $"({(f.X - x0) / 256.0:0.0},{(f.Y - y0) / 256.0:0.0}) r {f.Radius / 256.0:0.00} after {(f.EndTick - f.StartTick) / 40.0:0.00} s"))}; {strikes.Count} strikes, {hits.Count} hits ({hits.Sum(h => h.Value)} damage); dashed {(x0 - p.X) / 256.0:0.0} cells");
        Assert.InRange(areas.Count, 3, 6);                                   // one every cell of a ~3-cell dash, the first where the hit came
        Assert.Equal(x0, areas[0].X); Assert.Equal(y0, areas[0].Y);
        for (int i = 1; i < areas.Count; i++) Assert.True(HordeSim.Dist2(areas[i].X, areas[i].Y, areas[i - 1].X, areas[i - 1].Y) >= 128L * 128, "half a cell or more apart");
        foreach (var f in areas) { Assert.Equal(HordeSim.Ticks(0.6), f.EndTick - f.StartTick); Assert.Equal(HordeSim.Units(0.8), f.Radius); Assert.Equal(0, f.OwnerSlot); }
        Assert.Equal(areas.Count, strikes.Count);                            // each explodes
        Assert.Contains(hits, h => h.Target == toucher || crowd.Contains(h.Target)); // and hurts what stands in it
        Assert.Empty(hurt); Assert.Equal(hp0, p.Hp);                         // unhurt
        Assert.True(vanishedSeen, "the snapshot marks the runner Vanished");
        Assert.True(sim.SlideHidden(p));
        long dFar1 = HordeSim.Dist2(sim.Enemies[far].X, sim.Enemies[far].Y, x0, y0);
        _out.WriteLine($"the lunatic: {Math.Sqrt(dFar0) / 256:0.00} -> {Math.Sqrt(dFar1) / 256:0.00} cells from where the runner was hit, after 60 ticks unseen");
        Assert.True(Math.Abs(Math.Sqrt(dFar1) - Math.Sqrt(dFar0)) < 16, "not chased while unseen (it stood)");
        // after it: seen, hurt again, the speed back, the flag gone
        while (sim.Tick <= until + 2) { Step(); sim.ClearOutputs(); }
        Assert.False(sim.SlideHidden(p)); Assert.Equal(speed0, p.SpeedPerTick);
        long dFar2 = HordeSim.Dist2(sim.Enemies[far].X, sim.Enemies[far].Y, p.X, p.Y);
        for (int t = 0; t < 4; t++) { Step(); sim.ClearOutputs(); }
        Assert.True(Math.Sqrt(HordeSim.Dist2(sim.Enemies[far].X, sim.Enemies[far].Y, p.X, p.Y)) < Math.Sqrt(dFar2) - 40, "seen again: chased");
        Assert.True((view.Players[0].Flags & HordePlayerFlags.Vanished) == 0);
        bool hurtAfter = false;
        for (int t = 0; t < 120 && !hurtAfter; t++)
        {
            sim.Enemies[toucher].X = p.X + 100; sim.Enemies[toucher].Y = p.Y;
            Step(); hurtAfter = sim.Events.Any(e => e.Kind == HordeEventKind.PlayerHit && e.Slot == 0 && e.Value > 0); sim.ClearOutputs();
        }
        Assert.True(hurtAfter, "hurt again once the 1.5 s are over");
        // the wait: no second dash before 9 s (level 1)
        var ow = p.Weapons.First(o => o.Def == slide);
        Assert.Equal(dashTick + HordeSim.Ticks(9), ow.NextTick);
    }

    [Fact]
    public void Back_Slides_branches_keep_the_rules_the_long_one_lays_more_areas()
    {
        int AreasOf(int branch, out WeaponLevel lv)
        {
            var c = C(); var sim = Quiet(c);
            var slide = W(c, "tf_backsliding");
            sim.Give(0, new HordeCard { Kind = HordeCardKind.NewWeapon, DefId = slide.Id, Level = 1 });
            sim.Give(0, new HordeCard { Kind = HordeCardKind.WeaponLevel, DefId = slide.Id, Level = 2 });
            sim.Give(0, new HordeCard { Kind = HordeCardKind.WeaponBranch, DefId = branch * 1000 + slide.Id, Level = 3 });
            var p = sim.Player(0)!; var ow = p.Weapons.First(o => HordeSim.RootId(o.Def) == slide.Id);
            Assert.Equal(branch * 1000 + slide.Id, ow.Def.Id); Assert.Equal(3, ow.Level);
            Assert.True(HordeSim.IsSlide(ow.Def));
            var poring = E(c, "PORING"); poring.Damage = 5; poring.Speed = 0.01f; poring.Hp = 100000;
            sim.Spawn(poring, p.X + 100, p.Y, true);
            int areas = 0; bool unseen = false;
            for (int t = 0; t < 80; t++) { sim.Step(); unseen |= sim.SlideHidden(p); areas += sim.FxBegun.Count(f => f.Kind == HordeFxKind.Warning && HordeSim.RootId(c.Weapon(f.DefId)) == slide.Id); sim.ClearOutputs(); }
            lv = ow.Lv;
            _out.WriteLine($"{ow.Def.Key} level 3 (range {lv.Range:0.##}, area {lv.Area:0.##}, damage {lv.Damage}, every {lv.Cooldown:0.##} s): {areas} areas, unseen {unseen}");
            Assert.True(unseen, "the branch keeps the unseen time");
            return areas;
        }
        int longer = AreasOf(1, out var l1), strong = AreasOf(2, out var l2);
        Assert.True(strong >= 3); Assert.True(longer > strong, "the Long branch's longer dash lays more areas");
        Assert.True(l1.Area > l2.Area, "the Long branch's areas are bigger");
        Assert.True(l2.Damage > l1.Damage, "the Strong branch's trail hurts more");
    }

    // ------------------------------------------------------------------------------------------------ point 29: density

    static readonly float[] W17SpawnMul = { 1f, 1.6f, 2.2f, 2.8f, 3.3f, 3.8f, 4.3f, 4.8f }, W17PartyExp = { 1f, 1.55f, 2.1f, 2.65f, 3.2f, 3.75f, 4.3f, 4.8f };

    [Fact]
    public void Enemy_density_is_wave_17s_times_1_4_with_the_level_pacing_kept()
    {
        // point 29: a solo player's share x 1.4 (and its EXP requirement with it); point 31: n players n shares (the requirement as
        // measured: design notes). REWRITTEN for the solo retune (client wave 19, design note: "Retune it harder"): ALONE the share
        // is wave 17's x 1.9 and its EXP requirement grows with it (the pacing kept); every party size is n x 1.4 as before.
        var c = C();
        Assert.Equal(W17SpawnMul[0] * 1.9, c.Scaling.SpawnMul[0], 3); Assert.Equal(W17PartyExp[0] * 1.9, c.Rules.PartyExpScale[0], 3);
        for (int i = 1; i < 8; i++) Assert.Equal(1.4 * (i + 1), c.Scaling.SpawnMul[i], 3);
        for (int i = 1; i < 8; i++) Assert.True(c.Rules.PartyExpScale[i] > W17PartyExp[i] * 1.4 && c.Rules.PartyExpScale[i] > c.Rules.PartyExpScale[i - 1]);
        Assert.Equal(420, c.Rules.MaxEnemies);
        Assert.True(HordeLimits.MaxEnemies - c.Rules.MaxEnemies >= 100, "room above the waves' cap for bosses and their adds");
        // the same run (seed, tables, one unarmed runner nothing can hurt) with wave 17's spawns and with these: enemies spawned in 3 min
        int Spawned(bool w17, int players, int seconds, out int maxAlive)
        {
            var cc = C(); cc.Objectives.Enabled = false;
            cc.Rules.ClusterSpawnCells = 0; // (point 31's closeness: its own test below; here the players stand 1.5 cells apart)
            cc.Elites.Clear(); // (the elites and their adds: their own test below; here the waves alone)
            if (w17) cc.Scaling.SpawnMul = (float[])W17SpawnMul.Clone();
            var sim = new HordeSim(cc, cc.Arenas[0], 29, 900);
            for (int i = 0; i < players; i++) { sim.AddPlayer((uint)(5200000 + i), "p" + i); sim.Player(i)!.Weapons.Clear(); }
            var serial = new int[HordeLimits.MaxEnemies]; var alive = new bool[HordeLimits.MaxEnemies]; int spawned = 0; maxAlive = 0;
            for (int t = 0; t < seconds * HordeSim.TickRate; t++)
            {
                foreach (var p in sim.Players) if (p.Present) p.SafeUntil = sim.Tick + 10;
                sim.Step(); sim.ClearOutputs();
                for (int i = 0; i < HordeLimits.MaxEnemies; i++)
                {
                    var e = sim.Enemies[i];
                    if (e.Alive && (!alive[i] || serial[i] != e.Serial)) spawned++;
                    alive[i] = e.Alive; serial[i] = e.Serial;
                }
                maxAlive = Math.Max(maxAlive, sim.EnemiesAlive);
            }
            return spawned;
        }
        // (nothing is killed here, so the field fills up: 4 players for 30 s only, before either reaches its cap); 1 player: x 1.9 (the
        // solo retune; it was x 1.4 with the same +-5 % room: 1.33-1.47), 4 players: x 1.4 x 4 / 2.8 (wave 17's four had 2.8 solo shares) = x 2.0
        foreach (var (players, seconds, lo, hi) in new[] { (1, 180, 1.80, 2.0), (4, 30, 1.9, 2.1) })
        {
            int before = Spawned(true, players, seconds, out int maxBefore), after = Spawned(false, players, seconds, out int maxAfter);
            _out.WriteLine($"{players} player(s), {seconds} s, nothing killed: {before} enemies spawned with wave 17's numbers (at most {maxBefore} alive), {after} now (at most {maxAfter}; x {after / (double)before:0.00})");
            Assert.True(maxBefore < 300 && maxAfter < 420, "no cap reached");
            Assert.InRange(after / (double)before, lo, hi);
        }
    }

    // ------------------------------------------------------------------------------------------------ point 31: per player, closeness

    /// <summary>the design's point 31: every player brings a whole solo share of enemies, and a group spawned around a player is x (1 + the sum
    /// over the other standing players of max(0, 1 - d / 10 cells), the sum CAPPED at 1: the design's "Yes, cap the closeness bit"). One wave (a poring every 2 s, 20 groups), the players held in place,
    /// unhurt and unarmed, nothing killed: the enemies spawned = 20 x 1.4 x n x the closeness (symmetric layouts: every player's is the
    /// same), at 0 / 5 / 10 / 15 cells, 1, 2, 4 and 8 players. Their EXP share is 1 / the closeness (the pacing kept).</summary>
    [Fact]
    public void More_enemies_for_every_player_and_more_still_when_they_stand_close()
    {
        (int spawned, double expScale) Run(Func<int, int, (double x, double y)> place, int n)
        {
            var c = C(); c.Objectives.Enabled = false; c.Waves.Clear(); c.Rules.MaxEnemies = 510;
            Assert.Equal(10f, c.Rules.ClusterSpawnCells); Assert.Equal(1f, c.Rules.ClusterSpawnMaxShares);
            c.Waves.Add(new WaveDef { From = 0, To = 40, EnemyId = E(c, "PORING").Id, Interval = 2f, Count = 1, Pattern = SpawnPattern.Ring });
            var sim = new HordeSim(c, c.Arenas[0], 31, 900);
            int cx = (sim.MinX + sim.MaxX) / 2, cy = (sim.MinY + sim.MaxY) / 2;
            for (int i = 0; i < n; i++) { sim.AddPlayer((uint)(5300000 + i), "p" + i); var p = sim.Player(i)!; p.Weapons.Clear(); var (x, y) = place(i, n); p.X = cx + (int)Math.Round(x * 256); p.Y = cy + (int)Math.Round(y * 256); }
            var serial = new int[HordeLimits.MaxEnemies]; var alive = new bool[HordeLimits.MaxEnemies]; int spawned = 0; double scale = 1;
            for (int t = 0; t < 42 * HordeSim.TickRate; t++)
            {
                foreach (var p in sim.Players) if (p.Present) p.SafeUntil = sim.Tick + 10;
                sim.Step(); sim.ClearOutputs();
                for (int i = 0; i < HordeLimits.MaxEnemies; i++)
                {
                    var e = sim.Enemies[i];
                    if (e.Alive && (!alive[i] || serial[i] != e.Serial)) { spawned++; scale = e.ExpScale; }
                    alive[i] = e.Alive; serial[i] = e.Serial;
                }
            }
            return (spawned, scale);
        }
        (double, double) Stacked(int i, int n) => (0, 0);
        // 2 players: a line; 4: a square; 8: the ring of a 3 x 3 grid (its middle empty); neighbours <spacing> cells apart
        Func<int, int, (double, double)> Grid(double s) => (i, n) =>
        {
            if (n == 2) return (i == 0 ? -s / 2 : s / 2, 0);
            if (n == 4) return ((i % 2 - 0.5) * s, (i / 2 - 0.5) * s);
            var ring = new[] { (-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0) };
            return (ring[i].Item1 * s, ring[i].Item2 * s);
        };
        // the closeness of every player of a layout is the same here: 2 at 5 cells: 1 + 0.5; 4 / 8 at 5 cells: 1 + min(1, 1.29) = 2 (capped)
        var cases = new List<(string what, int n, Func<int, int, (double, double)> place, double close)> { ("alone", 1, Stacked, 1) };
        foreach (int n in new[] { 2, 4, 8 })
        {
            cases.Add(($"{n} at 0 cells", n, Stacked, 2));
            cases.Add(($"{n} at 5 cells", n, Grid(5), n == 2 ? 1.5 : 2));
            cases.Add(($"{n} at 10 cells", n, Grid(10), 1));
            cases.Add(($"{n} at 15 cells", n, Grid(15), 1));
        }
        foreach (var (what, n, place, close) in cases)
        {
            var (got, scale) = Run(place, n);
            // REWRITTEN for the solo retune (client wave 19): alone the share is 1.9 (was 1.4, as a party's every player still brings)
            double share = n == 1 ? 1.9 : 1.4 * n, want = 20 * share * close;
            _out.WriteLine($"{what}: {got} enemies spawned (expected 20 x {share:0.0#} x {close:0.000} = {want:0.0}; x {got / 28.0:0.00} of 1.4 shares); EXP share {scale:0.000}");
            Assert.InRange(got, (int)Math.Floor(want) - 1, (int)Math.Ceiling(want) + 1);
            Assert.Equal(1 / close, scale, 3);
        }
    }

    // ------------------------------------------------------------------------------------------------ the elites (design note, after the first pass)

    /// <summary>design note: "make some Elite Enemies, that sit somewhere between a boss and a regular enemy. They should have an HP bar visible above
    /// them." The deal: 7 kinds (mob_db Class Boss, no MvpExp), HP and damage between the stage's normal enemies and the bosses, one or two
    /// telegraphed attacks each; the first at 1:00, then one every 45-75 s, the kinds of the run's stage, none near a Once mark; with 4
    /// players two at a time and none while two are alive; each a BossAppeared (Elite) and its HP in every snapshot (the bar's data).</summary>
    [Fact]
    public void Elites_come_now_and_then_by_the_stage_and_their_HP_travels_in_the_snapshot()
    {
        var c0 = C();
        var deal = c0.Elites.Select(el => c0.Enemy(el.EnemyId)!).ToList();
        Assert.True(deal.Count >= 6 && deal.Count <= 8, $"{deal.Count} kinds");
        int bossMin = c0.Enemies.Where(e => e.Rank == EnemyRank.Boss).Min(e => e.Hp);
        foreach (var d in deal)
        {
            Assert.Equal(EnemyRank.Elite, d.Rank); Assert.InRange(d.Abilities.Length, 1, 3);
            Assert.True(d.Hp > 250 && d.Hp < bossMin, $"{d.Key} HP {d.Hp} between the normal enemies' (at most 250) and a boss's ({bossMin})");
            Assert.Contains(d.Drops, x => c0.Pickup(x.PickupId)!.Kind == PickupKind.Chest);
            Assert.True(d.Exp >= 20);
        }
        // the guarded chest's guard stays a marked elite (Eclipse), not one of the deal
        Assert.Equal(60f, c0.Rules.EliteFirstSeconds); Assert.Equal(45f, c0.Rules.EliteEverySeconds); Assert.Equal(75f, c0.Rules.EliteEveryMaxSeconds);
        // ---- alone: the times, the kinds, the quiet around a boss's mark (Ork Hero added at 3:20)
        var c = C(); c.Objectives.Enabled = false; c.Waves.Clear();
        c.Waves.Add(new WaveDef { From = 200, To = 201, EnemyId = c.Enemies.First(e => e.Key == "ORK_HERO").Id, Count = 1, Pattern = SpawnPattern.Ring, Once = true, WarnSeconds = 8 });
        var sim = new HordeSim(c, c.Arenas[0], 18, 900);
        sim.AddPlayer(5400000, "p0"); var p = sim.Player(0)!;
        var spawns = new List<(double t, string key)>();
        for (int t = 0; t < 420 * HordeSim.TickRate; t++)
        {
            p.SafeUntil = sim.Tick + 10;
            if (p.Open != null) sim.Choose(0, p.Open.Id, 0); // (its cards: the world waits while one is open)
            foreach (var e in sim.Enemies) if (e.Alive && sim.IsDealElite(e.Def.Id)) e.Hp = Math.Min(e.Hp, 1); // they fall at the first stab
            foreach (var e in sim.Enemies) if (e.Alive && e.Def.Rank == EnemyRank.Boss) { e.X = sim.MinX + 256; e.Y = sim.MinY + 256; } // the boss kept away
            sim.Step();
            foreach (var ev in sim.Events) if (ev.Kind == HordeEventKind.BossAppeared && (ev.Flags & HordeEventFlags.Elite) != 0) spawns.Add((sim.Tick / 40.0, c.Enemy(ev.DefId)!.Key));
            sim.ClearOutputs();
        }
        _out.WriteLine("alone: " + string.Join(", ", spawns.Select(x => $"{x.t:0.0} s {x.key}")));
        Assert.True(spawns.Count >= 5);
        Assert.InRange(spawns[0].t, 59.9, 61);
        for (int i = 1; i < spawns.Count; i++) Assert.InRange(spawns[i].t - spawns[i - 1].t, 44.9, 75 + 20 + 1);
        foreach (var (t, key) in spawns)
        {
            Assert.False(t > 180 && t < 220, $"an elite at {t:0.0} s, within 20 s of the boss's mark (200 s)");
            if (t < 120) Assert.Contains(key, new[] { "MASTERING", "TOAD" });
            else if (t < 240) Assert.Contains(key, new[] { "MASTERING", "TOAD", "DRAGON_FLY" });
            else if (t < 450) Assert.Equal("GHOSTRING", key);
        }
        // ---- 4 players: two at a time, none more while they live; their HP in the snapshot
        var c4 = C(); c4.Objectives.Enabled = false; c4.Waves.Clear();
        var s4 = new HordeSim(c4, c4.Arenas[0], 19, 900);
        for (int i = 0; i < 4; i++) { s4.AddPlayer((uint)(5400100 + i), "p" + i); s4.Player(i)!.Weapons.Clear(); }
        var enc = new SnapshotEncoder(s4); var dec = new SnapshotDecoder(); var view = new HordeWorldView();
        dec.Reset(new HordeRunInfo { X0 = s4.Arena.X0, Y0 = s4.Arena.Y0, X1 = s4.Arena.X1, Y1 = s4.Arena.Y1 });
        int n = 0; int at = -1;
        for (int t = 0; t < 200 * HordeSim.TickRate; t++)
        {
            foreach (var q in s4.Players) if (q.Present) q.SafeUntil = s4.Tick + 10;
            s4.Step(); enc.Collect(); s4.ClearOutputs();
            if (at < 0 && s4.ElitesSpawned > 0) at = s4.Tick;
            if (t % 2 == 1) { enc.Encode(n == 0, out var d, out var k); var body = n == 0 ? k! : d; n++; Assert.True(dec.Apply(body, 0, body.Length, view, 0, t / 40.0)); }
        }
        _out.WriteLine($"4 players, nothing killed: {s4.ElitesSpawned} elites by 200 s, the first at {at / 40.0:0.0} s");
        Assert.Equal(2, s4.ElitesSpawned);
        var elites = Enumerable.Range(0, HordeLimits.MaxEnemies).Where(i => s4.Enemies[i].Alive && s4.IsDealElite(s4.Enemies[i].Def.Id)).ToList();
        Assert.Equal(2, elites.Count);
        Assert.True(s4.Enemies[elites[0]].MaxHp > c4.Enemy(s4.Enemies[elites[0]].Def.Id)!.Hp, "an elite's HP grows with the players (EliteHpMul)");
        var el0 = s4.Enemies[elites[0]]; el0.Hp = el0.MaxHp / 2;
        s4.Step(); enc.Collect(); s4.ClearOutputs(); enc.Encode(false, out var d2, out _); Assert.True(dec.Apply(d2, 0, d2.Length, view, 0, 201.0));
        var ve = view.Enemies[elites[0]];
        Assert.True(ve.Alive); Assert.Equal(el0.Def.Id, ve.DefId); Assert.Equal(EnemyRank.Elite, c4.Enemy(ve.DefId)!.Rank);
        Assert.InRange(ve.Hp, 126, 128); // half its HP: the bar's fill, 0..255
    }

    /// <summary>The cost of the bigger cap: 8 players, the field kept at 420 enemies (the new cap), snapshots at 20 a second through the
    /// real encoder and decoder (as HordeMeasureTests does at 300).</summary>
    [Fact]
    public void Eight_players_and_420_enemies_fit_the_wire_and_the_tick()
    {
        var c = C();
        int field = c.Rules.MaxEnemies;
        Assert.Equal(420, field);
        var sim = new HordeSim(c, c.Arenas[0], 11, 900);
        for (int i = 0; i < 8; i++) sim.AddPlayer((uint)(2000000 + i), "bot" + i, (byte)(i % 2 + 1));
        var enc = new SnapshotEncoder(sim);
        var normal = c.Enemies.First(e => e.Rank == EnemyRank.Normal);
        var dec = new SnapshotDecoder(); var view = new HordeWorldView();
        dec.Reset(new HordeRunInfo { X0 = sim.Arena.X0, Y0 = sim.Arena.Y0, X1 = sim.Arena.X1, Y1 = sim.Arena.Y1 });
        var rnd = new Random(5);
        long bytes = 0, snaps = 0, maxSnap = 0, keyBytes = 0; double tickMs = 0, tickMax = 0; int ticks = 0, enemiesMin = int.MaxValue;
        ushort seq = 0; var sw = new Stopwatch();
        for (int t = 0; t < HordeSim.TickRate * 90 && !sim.Over; t++)
        {
            while (sim.EnemiesAlive < field) sim.Spawn(normal, sim.MinX + rnd.Next(sim.MaxX - sim.MinX), sim.MinY + rnd.Next(sim.MaxY - sim.MinY), true);
            seq++;
            for (int i = 0; i < 8; i++) HordeTests.BotStep(sim, i, seq, i);
            sw.Restart(); sim.Step(); sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds; tickMs += ms; if (ms > tickMax && t > 20) tickMax = ms; ticks++;
            enc.Collect(); sim.ClearOutputs();
            if (sim.Paused) continue;
            if (t % 2 == 1)
            {
                bool key = t % 200 == 1;
                enc.Encode(key, out var delta, out var k);
                var f = SnapshotEncoder.Frame(0, delta)!;
                if (t > 40) { bytes += f.Length; snaps++; maxSnap = Math.Max(maxSnap, f.Length); enemiesMin = Math.Min(enemiesMin, sim.EnemiesAlive); }
                if (k != null) keyBytes = Math.Max(keyBytes, k.Length + 5);
                Assert.True(dec.Apply(key && !dec.HaveKey ? k! : delta, 0, (key && !dec.HaveKey ? k! : delta).Length, view, 0, t / (double)HordeSim.TickRate), "snapshot applied");
            }
        }
        int alive = 0; foreach (var e in sim.Enemies) if (e.Alive) alive++;
        Assert.Equal(alive, view.EnemiesAlive);
        for (int i = 0; i < HordeLimits.MaxEnemies; i++)
            if (sim.Enemies[i].Alive) Assert.True(Math.Abs(sim.Enemies[i].X / 256f - view.Enemies[i].X) < 0.1f && Math.Abs(sim.Enemies[i].Y / 256f - view.Enemies[i].Y) < 0.1f, $"enemy {i} place");
        double perSec = bytes / (snaps / (HordeSim.TickRate / 2.0));
        _out.WriteLine($"game tables: 8 players, >= {enemiesMin} enemies: snapshot mean {bytes / Math.Max(1, snaps)} bytes, largest {maxSnap}, key {keyBytes}; {perSec / 1024:0.0} KB/s per client; tick mean {tickMs / ticks:0.000} ms, max {tickMax:0.00} ms (of {1000 / HordeSim.TickRate})");
        Assert.True(enemiesMin >= field * 85 / 100, "the field stayed (nearly) full between the top-ups");
        Assert.True(perSec < 64 * 1024, "per-client stream under 64 KB/s");
        Assert.True(keyBytes < HordeWire.MaxPayload, "a key fits one frame");
        Assert.True(tickMs / ticks < 10, "tick under 10 ms on average");
    }
}
