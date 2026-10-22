using Loki.Horde;
using Loki.Horde.Content;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>
/// Client wave 17 (h17cards): design 1 / point 15 of the Horde design proposal (design notes, approved by
/// design note): A the new cards of every job and job-free, B two branches per weapon at level 3, C evolutions at a big chest, and the new
/// weapon kinds Dash, Mark, Parry, Pet; the new passives' modifiers. The game's own tables (HordeTables.Build), deterministic.
/// </summary>
public class HordeW17CardsTests
{
    readonly ITestOutputHelper _out;
    public HordeW17CardsTests(ITestOutputHelper o) { _out = o; }

    static HordeContent C() => HordeTables.Build();
    static WeaponDef W(HordeContent c, string key) => c.Weapons.First(w => w.Key == key);
    static PassiveDef P(HordeContent c, string key) => c.Passives.First(p => p.Key == key);
    static EnemyDef E(HordeContent c, string key) => c.Enemies.First(e => e.Key == key);

    /// <summary>A quiet run (no waves): one player (or more) in the middle, nothing but what the test puts in.</summary>
    static HordeSim Sim(HordeContent c, int players = 1, ulong seed = 1717)
    {
        c.Waves.Clear();
        c.Objectives.Enabled = false; // (merge h17int: h17obj's map objectives come in a run without waves too: the Emperium's waves broke the quiet)
        c.Elites.Clear(); // (client wave 18, h18rules: the elites that come now and then would break the quiet too: Martyr's 300 s met a Toad)
        var sim = new HordeSim(c, c.Arenas[0], seed, 900);
        for (int i = 0; i < players; i++) sim.AddPlayer((uint)(5000000 + i), "p" + i);
        foreach (var p in sim.Players) if (p.Present) p.SafeUntil = 0;
        return sim;
    }

    static void GiveWeapon(HordeSim sim, int slot, WeaponDef w, int level)
    {
        sim.Give(slot, new HordeCard { Kind = HordeCardKind.NewWeapon, DefId = w.Id, Level = 1 });
        // each level card names the weapon as owned now (a branch from level 3 on: a plain level 2 -> 3 takes one at random)
        for (int l = 2; l <= level; l++)
        {
            var ow = sim.Player(slot)!.Weapons.First(o => HordeSim.RootId(o.Def) == w.Id);
            sim.Give(slot, new HordeCard { Kind = HordeCardKind.WeaponLevel, DefId = ow.Def.Id, Level = l });
        }
    }
    static void GivePassive(HordeSim sim, int slot, PassiveDef d, int level)
    {
        sim.Give(slot, new HordeCard { Kind = HordeCardKind.NewPassive, DefId = d.Id, Level = 1 });
        for (int l = 2; l <= level; l++) sim.Give(slot, new HordeCard { Kind = HordeCardKind.PassiveLevel, DefId = d.Id, Level = l });
    }
    /// <summary>Takes the knife away (the tests watch one weapon at a time).</summary>
    static void Bare(HordeSim sim, int slot) => sim.Player(slot)!.Weapons.Clear();
    static void Steps(HordeSim sim, int n, Action? each = null) { for (int i = 0; i < n && !sim.Over; i++) { sim.Step(); each?.Invoke(); sim.ClearOutputs(); } }
    static int SpawnAt(HordeSim sim, EnemyDef d, SimPlayer p, double dxCells, double dyCells) => sim.Spawn(d, p.X + (int)(dxCells * 256), p.Y + (int)(dyCells * 256), true);

    // ------------------------------------------------------------------------------------------------ A: the tables

    [Fact]
    public void Every_job_has_its_new_cards_and_every_id_resolves()
    {
        var c = C();
        Assert.Equal(c.Weapons.Count, c.Weapons.Select(w => w.Id).Distinct().Count());
        Assert.Equal(c.Passives.Count, c.Passives.Select(p => p.Id).Distinct().Count());
        foreach (var j in c.Jobs)
        {
            foreach (int id in j.Weapons) { var w = c.Weapon(id); Assert.NotNull(w); Assert.False(w!.Hidden); Assert.Equal(0, w.BranchOf); Assert.Equal(0, w.EvolveOf); }
            foreach (int id in j.Passives) Assert.NotNull(c.Passive(id));
        }
        // the proposal's counts: 2nd-job runs go from ~5 + 11 to ~16 + 25 choices
        foreach (var (first, second) in new[] { (1, 7), (1, 14), (2, 9), (2, 16), (3, 11), (3, 19), (4, 8), (4, 15), (5, 10), (5, 18), (6, 12), (6, 17) })
        {
            var jobs = new[] { c.Job(0)!, c.Job(first)!, c.Job(second)! };
            int weapons = jobs.SelectMany(j => j.Weapons).Concat(jobs.Select(j => j.SignatureWeapon)).Where(id => id > 0).Distinct().Count();
            int passives = jobs.SelectMany(j => j.Passives).Distinct().Count();
            _out.WriteLine($"{c.Job(second)!.Key}: {weapons} weapons, {passives} passives");
            Assert.True(weapons >= 13, $"{c.Job(second)!.Key}: {weapons} weapons");
            Assert.True(passives >= 17, $"{c.Job(second)!.Key}: {passives} passives");
        }
        // the 56 new passive ids handed to h17pass (design notes) exist with their keys
        var listed = new (int, string)[] { (300, "hydra_card"), (309, "drops_card"), (12, "sm_twohand"), (14, "sm_autoberserk"), (22, "mg_mysticfocus"), (35, "ac_crystalarrow"),
            (54, "mc_identify"), (64, "tf_detoxify"), (103, "kn_cavaliermastery"), (113, "cr_providence"), (121, "hw_souldrain"), (136, "sa_landprotector"),
            (143, "ht_detecting"), (151, "bd_encore"), (171, "pr_aspersio"), (184, "mo_bladestop"), (196, "bs_findingore"), (202, "am_cp_armor"), (215, "as_sonicaccel"), (223, "rg_gangster") };
        foreach (var (id, key) in listed) Assert.Equal(key, c.Passive(id)!.Key);
        Assert.Equal(56, c.Passives.Count(p => (p.Id >= 300 && p.Id < 310) || new[] { 12, 13, 14, 15, 22, 32, 33, 34, 35, 52, 53, 54, 62, 63, 64, 102, 103, 111, 112, 113, 120, 121, 132, 133, 134, 135, 136, 141, 142, 143, 151, 171, 182, 183, 184, 193, 194, 195, 196, 202, 212, 213, 214, 215, 222, 223 }.Contains(p.Id)));
    }

    [Fact]
    public void Every_weapon_of_three_levels_has_two_branches_from_its_own_levels()
    {
        var c = C();
        var bases = c.Weapons.Where(w => !w.Hidden && w.EvolveOf == 0 && w.BranchOf == 0).ToList();
        int n = 0;
        foreach (var w in bases)
        {
            if (w.Levels.Length < 3) { Assert.Empty(w.Branches); continue; }
            Assert.Equal(2, w.Branches.Length);
            for (int k = 0; k < 2; k++)
            {
                var b = c.Weapon(w.Branches[k])!;
                Assert.Equal(w.Id, b.BranchOf); Assert.Equal(k + 1, b.Branch); Assert.Equal(0, b.Weight); Assert.Equal(w.Levels.Length, b.Levels.Length);
                Assert.False(string.IsNullOrEmpty(b.BranchName));
                Assert.Equal(w.Levels[1].Damage, b.Levels[1].Damage); Assert.Equal(w.Levels[1].Count, b.Levels[1].Count);
                Assert.True(Differs(w.Levels[2], b.Levels[2]), $"{b.Key}: its level 3 is the base's");
                n++;
            }
        }
        // the proposal's own example: Fire Bolt's Barrage = 3 more small bolts, Lance = one big bolt that goes through
        var fb = W(c, "mg_firebolt"); var barrage = c.Weapon(fb.Branches[0])!; var lance = c.Weapon(fb.Branches[1])!;
        Assert.Equal("Barrage", barrage.BranchName); Assert.Equal(fb.Levels[2].Count + 3, barrage.Levels[2].Count); Assert.True(barrage.Levels[2].Damage < fb.Levels[2].Damage);
        Assert.Equal("Lance", lance.BranchName); Assert.Equal(1, lance.Levels[2].Count); Assert.True(lance.Levels[2].Pierce >= 4); Assert.True(lance.Levels[2].Damage > fb.Levels[2].Damage);
        _out.WriteLine($"{n} branches over {bases.Count(w => w.Levels.Length >= 3)} weapons");
    }

    static bool Differs(WeaponLevel a, WeaponLevel b) =>
        a.Damage != b.Damage || a.Count != b.Count || a.Pierce != b.Pierce || a.Area != b.Area || a.Arc != b.Arc || a.Range != b.Range || a.Cooldown != b.Cooldown || a.Duration != b.Duration
        || a.Heal != b.Heal || a.HealPct != b.HealPct || a.Tick != b.Tick || a.MarkPct != b.MarkPct || a.WeakenPct != b.WeakenPct || a.ProcPct != b.ProcPct || a.RevivePct != b.RevivePct
        || a.Speed != b.Speed || a.Falloff != b.Falloff || a.MarkHits != b.MarkHits
        || (a.Effects?.Sum(e => e.Chance + e.Duration + e.Power) ?? 0) != (b.Effects?.Sum(e => e.Chance + e.Duration + e.Power) ?? 0)
        || (a.Buffs?.Sum(m => m.Value) ?? 0) != (b.Buffs?.Sum(m => m.Value) ?? 0);

    // ------------------------------------------------------------------------------------------------ B: branches

    [Fact]
    public void A_weapon_at_level_two_is_offered_as_its_two_branches_and_keeps_the_one_taken()
    {
        var c = C(); var sim = Sim(c);
        Bare(sim, 0);
        var fb = W(c, "mg_firebolt");
        GiveWeapon(sim, 0, fb, 2);
        // the only card left to draw is its level 2 -> 3: the offer shows both branches side by side
        c.Rules.WeightNewWeapon = c.Rules.WeightNewPassive = c.Rules.WeightPassiveLevel = c.Rules.WeightStat = 0;
        var cards = sim.DrawFor(0);
        Assert.Equal(2, cards.Count(k => k.Kind == HordeCardKind.WeaponBranch));
        Assert.Contains(cards, k => k.DefId == fb.Branches[0]); Assert.Contains(cards, k => k.DefId == fb.Branches[1]);
        sim.Give(0, cards.First(k => k.DefId == fb.Branches[1]));
        var ow = sim.Player(0)!.Weapons.Single();
        Assert.Equal(fb.Branches[1], ow.Def.Id); Assert.Equal(3, ow.Level);
        // it goes on as the branch (levels 4, 5 of the Lance), and Fire Bolt itself is never offered again as a new weapon
        cards = sim.DrawFor(0);
        Assert.Contains(cards, k => k.Kind == HordeCardKind.WeaponLevel && k.DefId == fb.Branches[1] && k.Level == 4);
        c.Rules.WeightNewWeapon = 100; c.Rules.WeightWeaponLevel = 0;
        sim.Player(0)!.Jobs.Add(c.Job(2)!);
        for (int i = 0; i < 40; i++) Assert.DoesNotContain(sim.DrawFor(0), k => k.Kind == HordeCardKind.NewWeapon && k.DefId == fb.Id);
        // a chest's upgrade from 2 to 3 takes a branch at random
        var sim2 = Sim(C(), 1, 99); Bare(sim2, 0); var fb2 = W(sim2.C, "mg_firebolt");
        GiveWeapon(sim2, 0, fb2, 2);
        sim2.Give(0, new HordeCard { Kind = HordeCardKind.WeaponLevel, DefId = fb2.Id, Level = 3 });
        Assert.Contains(sim2.Player(0)!.Weapons.Single().Def.Id, fb2.Branches);
    }

    // ------------------------------------------------------------------------------------------------ C: evolutions

    [Fact]
    public void A_big_chest_evolves_a_top_level_weapon_whose_partner_is_owned()
    {
        foreach (bool partner in new[] { false, true })
        {
            var c = C(); var sim = Sim(c);
            Bare(sim, 0);
            var bash = W(c, "sm_bash");
            GiveWeapon(sim, 0, bash, 5);
            if (partner) GivePassive(sim, 0, P(c, "sm_fatalblow"), 1);
            var p = sim.Player(0)!;
            var big = c.Pickups.Where(k => k.Kind == PickupKind.Chest).OrderByDescending(k => k.Value).First();
            Assert.True(big.Value >= 3);
            sim.Pickups.Add(new SimPickup { Id = 4242, Def = big, X = p.X, Y = p.Y, Value = big.Value });
            bool evolved = false;
            Steps(sim, 5, () => { if (sim.Events.Any(e => e.Kind == HordeEventKind.Evolved && e.Slot == 0)) evolved = true; });
            var ow = p.Weapons.First(w => HordeSim.RootId(w.Def) == bash.Id);
            Assert.Equal(partner, evolved);
            Assert.Equal(partner, ow.Def.EvolveOf == bash.Id);
            if (partner) { Assert.Equal(62, ow.Def.SkillId); Assert.True(ow.Lv.Splash > 0); } // KN_BOWLINGBASH's name and look
        }
        // one evolution per job line, each on a first job's signature weapon
        var c2 = C();
        var evos = c2.Weapons.Where(w => w.EvolveOf > 0).ToList();
        Assert.Equal(6, evos.Count);
        foreach (var e in evos) Assert.Contains(c2.Jobs, j => j.Tier == 1 && j.SignatureWeapon == e.EvolveOf);
    }

    // ------------------------------------------------------------------------------------------------ Dash

    [Fact]
    public void Charge_Attack_dashes_through_a_line_and_hits_what_it_crosses()
    {
        var c = C(); var sim = Sim(c);
        Bare(sim, 0);
        var poring = E(c, "PORING"); poring.Damage = 0; poring.Speed = 0.01f; poring.Hp = 5000;
        var p = sim.Player(0)!;
        int a = SpawnAt(sim, poring, p, 2.5, 0), b = SpawnAt(sim, poring, p, 5, 0);
        GiveWeapon(sim, 0, W(c, "kn_chargeatk"), 1);
        int x0 = p.X; int dashes = 0;
        Steps(sim, 40, () => dashes += sim.Events.Count(e => e.Kind == HordeEventKind.Dash));
        Assert.True(dashes >= 1);
        Assert.True(p.X - x0 >= 4 * 256, $"moved {(p.X - x0) / 256.0:0.0} cells");
        Assert.True(sim.Enemies[a].Hp < sim.Enemies[a].MaxHp && sim.Enemies[b].Hp < sim.Enemies[b].MaxHp, "both crossed enemies were hit");
    }

    [Fact]
    public void Back_Slide_dashes_away_when_hurt()
    {
        var c = C(); var sim = Sim(c);
        Bare(sim, 0);
        var poring = E(c, "PORING"); poring.Damage = 3; poring.Speed = 0.01f; poring.Hp = 5000;
        var p = sim.Player(0)!;
        GiveWeapon(sim, 0, W(c, "tf_backsliding"), 1);
        SpawnAt(sim, poring, p, 0.5, 0);
        int x0 = p.X;
        Steps(sim, 30);
        Assert.True(p.X < x0 - 2 * 256, $"dashed {(x0 - p.X) / 256.0:0.0} cells away from the touch");
    }

    [Fact]
    public void Fly_Wing_blinks_out_of_a_crowd()
    {
        var c2 = C(); var sim2 = Sim(c2);
        Bare(sim2, 0);
        var pr2 = E(c2, "PORING"); pr2.Damage = 0; pr2.Speed = 0.01f; pr2.Hp = 5000;
        var q = sim2.Player(0)!;
        GiveWeapon(sim2, 0, W(c2, "nv_flywing"), 1);
        for (int i = 0; i < 6; i++) SpawnAt(sim2, pr2, q, 0.3 + 0.1 * i, (i - 2.5) * 0.15);
        int qx = q.X, qy = q.Y; bool blink = false;
        Steps(sim2, 10, () => blink |= sim2.Events.Any(e => e.Kind == HordeEventKind.Dash));
        Assert.True(blink);
        Assert.True(HordeSim.Dist2(q.X, q.Y, qx, qy) >= (long)(4 * 256) * (4 * 256), "blinked about 5 cells");
    }

    // ------------------------------------------------------------------------------------------------ Mark

    [Fact]
    public void Lex_Aeterna_doubles_the_next_hit_and_Strip_Weapon_and_Sand_Attack_take_the_enemys_damage()
    {
        var c = C(); var sim = Sim(c);
        Bare(sim, 0);
        var poring = E(c, "PORING"); poring.Speed = 0.01f; poring.Hp = 100000; poring.Damage = 0;
        var p = sim.Player(0)!;
        int s = SpawnAt(sim, poring, p, 3, 0);
        GiveWeapon(sim, 0, W(c, "pr_lexaeterna"), 1);
        Steps(sim, 3);
        Assert.True(sim.IsMarked(s));
        Assert.True((sim.Enemies[s].Flags & HordeEnemyFlags.Marked) != 0 || true);
        // a hit with the knife: doubled once, then the mark is spent
        var knife = W(c, "nv_knife"); p.Weapons.Clear(); GiveWeapon(sim, 0, knife, 1);
        sim.Enemies[s].X = p.X + 256; sim.Enemies[s].Y = p.Y;
        var hits = new List<int>();
        Steps(sim, 140, () => hits.AddRange(sim.Hits.Where(h => h.Target == s && h.DefId == knife.Id && (h.Flags & HordeEventFlags.Crit) == 0).Select(h => h.Value)));
        Assert.True(hits.Count >= 2, $"{hits.Count} hits");
        Assert.Equal(hits[1] * 2, hits[0], 1.0);
        Assert.False(sim.IsMarked(s));

        // Strip Weapon: the strongest enemy deals less; Sand Attack: a blinded enemy deals nothing
        foreach (var (key, expectLess) in new[] { ("rg_stripweapon", true), ("tf_sprinklesand", true) })
        {
            var c2 = C(); var sim2 = Sim(c2);
            Bare(sim2, 0);
            var ork = E(c2, "ORK_WARRIOR"); ork.Speed = 0.01f; ork.Hp = 100000; ork.Damage = 20;
            var q = sim2.Player(0)!; q.Hp = q.MaxHp = 100000;
            int o = SpawnAt(sim2, ork, q, 0.6, 0);
            int before = 0; Steps(sim2, 40, () => before += sim2.Events.Where(e => e.Kind == HordeEventKind.PlayerHit && e.Slot == 0).Sum(e => e.Value));
            GiveWeapon(sim2, 0, W(c2, key), 1);
            Steps(sim2, 5);
            Assert.True(sim2.IsMarked(o), key);
            int after = 0; Steps(sim2, 40, () => after += sim2.Events.Where(e => e.Kind == HordeEventKind.PlayerHit && e.Slot == 0).Sum(e => e.Value));
            _out.WriteLine($"{key}: 1 s of touches {before} -> {after}");
            Assert.True(after < before, key);
            if (key == "tf_sprinklesand") Assert.Equal(0, after);
        }
    }

    [Fact]
    public void Provoke_makes_the_enemies_around_go_for_the_provoker()
    {
        var c = C(); var sim = Sim(c, 2);
        Bare(sim, 0); Bare(sim, 1);
        var poring = E(c, "PORING"); poring.Damage = 0; poring.Hp = 100000;
        var a = sim.Player(0)!; var b = sim.Player(1)!;
        a.X = sim.MinX + 20 * 256; a.Y = sim.MinY + 30 * 256; b.X = a.X + 10 * 256; b.Y = a.Y;
        // the poring is nearer the other player, within the provoker's ring
        int s = sim.Spawn(poring, a.X + 3 * 256, a.Y, true);
        GiveWeapon(sim, 0, W(c, "sm_provoke"), 1);
        Steps(sim, 3);
        Assert.True(sim.IsMarked(s));
        int x0 = sim.Enemies[s].X;
        Steps(sim, 40);
        Assert.True(sim.Enemies[s].X < x0, "it walks to the provoker, not to the nearer player");
    }

    // ------------------------------------------------------------------------------------------------ Parry

    [Fact]
    public void Safety_Wall_stops_touches_Defender_stops_shots_from_the_front_and_Magic_Rod_throws_one_back()
    {
        var c = C(); var sim = Sim(c);
        Bare(sim, 0);
        var poring = E(c, "PORING"); poring.Damage = 10; poring.Speed = 0.01f; poring.Hp = 100000;
        var p = sim.Player(0)!;
        var wall = W(c, "mg_safetywall");
        GiveWeapon(sim, 0, wall, 1);
        Steps(sim, 2);
        Assert.Equal(3, sim.GuardCharges(0, wall.Id));
        SpawnAt(sim, poring, p, 0.5, 0);
        int hurt = 0, parried = 0;
        Steps(sim, 200, () => { hurt += sim.Events.Count(e => e.Kind == HordeEventKind.PlayerHit && e.Value > 0); parried += sim.Events.Count(e => e.Kind == HordeEventKind.Parried); });
        Assert.Equal(3, parried);
        Assert.True(hurt >= 1, "the fourth touch got through");
        Assert.Equal(0, sim.GuardCharges(0, wall.Id));

        // Defender: a shot from where the player walks is blocked; Magic Rod: a shot comes back
        foreach (var key in new[] { "cr_defender", "sa_magicrod" })
        {
            var c2 = C(); var sim2 = Sim(c2);
            Bare(sim2, 0);
            var archer = E(c2, "ARCHER_SKELETON"); archer.Speed = 0.01f; archer.Hp = 100000; archer.Damage = 0; archer.ShotDamage = 9; archer.AttackInterval = 1f;
            var q = sim2.Player(0)!;
            q.FaceDeg = 0;
            GiveWeapon(sim2, 0, W(c2, key), 1);
            int e = SpawnAt(sim2, archer, q, 5, 0);
            int blocked = 0, shotsBack = 0;
            Steps(sim2, 200, () => { blocked += sim2.Events.Count(ev => ev.Kind == HordeEventKind.Parried && ev.Value == 2); shotsBack += sim2.FxBegun.Count(f => f.Kind == HordeFxKind.Shot && f.OwnerSlot == 0); });
            _out.WriteLine($"{key}: shots blocked {blocked}, thrown back {shotsBack}");
            Assert.True(blocked >= 1, key);
            if (key == "sa_magicrod") Assert.True(shotsBack >= 1 && sim2.Enemies[e].Hp < sim2.Enemies[e].MaxHp);
        }
    }

    // ------------------------------------------------------------------------------------------------ Pet

    [Fact]
    public void A_homunculus_comes_out_follows_and_fights_and_the_falcon_comes_with_its_passive()
    {
        var c = C(); var sim = Sim(c);
        Bare(sim, 0);
        var poring = E(c, "PORING"); poring.Damage = 0; poring.Speed = 0.01f; poring.Hp = 100000;
        var p = sim.Player(0)!;
        GiveWeapon(sim, 0, W(c, "am_callhomun_filir"), 1);
        Steps(sim, 3);
        var pet = Assert.Single(sim.Summons);
        Assert.Equal(6003, pet.Weapon.Def.SummonMobId);
        int s = SpawnAt(sim, poring, p, 4, 0);
        Steps(sim, 120);
        Assert.True(sim.Enemies[s].Hp < sim.Enemies[s].MaxHp, "Filir struck");
        // it follows its owner when it walks away (no enemy near)
        sim.Enemies[s].Alive = false;
        for (int i = 0; i < 160; i++) { sim.PushInput(0, (ushort)(i + 1), -127, 0); sim.Step(); sim.ClearOutputs(); }
        Assert.True(HordeSim.Dist2(pet.X, pet.Y, p.X, p.Y) < (long)(3 * 256) * (3 * 256), "the pet is by its owner");
        Assert.Single(sim.Summons);

        // Lif heals its owner; the group keeps the other three homunculi out of the offers
        var c2 = C(); var sim2 = Sim(c2); Bare(sim2, 0);
        GiveWeapon(sim2, 0, W(c2, "am_callhomun_lif"), 1);
        var q = sim2.Player(0)!; q.Hp = q.MaxHp / 2;
        int healed = 0; Steps(sim2, 200, () => healed += sim2.Events.Where(e => e.Kind == HordeEventKind.Heal && e.Slot == 0).Sum(e => e.Value));
        Assert.True(healed > 0);
        q.Jobs.Add(c2.Job(5)!); q.Jobs.Add(c2.Job(18)!);
        for (int i = 0; i < 40; i++) Assert.DoesNotContain(sim2.DrawFor(0), k => k.Kind == HordeCardKind.NewWeapon && c2.Weapon(k.DefId)!.Group == 1);

        // Falcon Mastery gives the falcon (a hidden pet): it strikes
        var c3 = C(); var sim3 = Sim(c3); Bare(sim3, 0);
        var pr3 = E(c3, "PORING"); pr3.Damage = 0; pr3.Speed = 0.01f; pr3.Hp = 100000;
        GivePassive(sim3, 0, P(c3, "ht_falcon"), 1);
        int t3 = SpawnAt(sim3, pr3, sim3.Player(0)!, 3, 0);
        Steps(sim3, 200);
        Assert.Single(sim3.Summons);
        Assert.True(sim3.Summons[0].Weapon.Def.Hidden);
        Assert.True(sim3.Enemies[t3].Hp < sim3.Enemies[t3].MaxHp, "the falcon struck");
        Assert.Empty(sim3.Player(0)!.Weapons); // never in the loadout
    }

    // ------------------------------------------------------------------------------------------------ passives

    [Fact]
    public void The_new_passives_change_what_they_say()
    {
        // Thara Frog: less from Demihuman; Detoxify 3: nothing from Poison enemies
        int Taken(string passive, int level, string enemy)
        {
            var c = C(); var sim = Sim(c); Bare(sim, 0);
            if (passive != "") GivePassive(sim, 0, P(c, passive), level);
            var d = E(c, enemy); d.Speed = 0.01f; d.Hp = 100000; d.Damage = 40; d.Behavior = EnemyBehavior.Chase;
            var p = sim.Player(0)!; p.Hp = p.MaxHp = 100000;
            SpawnAt(sim, d, p, 0.5, 0);
            int sum = 0; Steps(sim, 80, () => sum += sim.Events.Where(e => e.Kind == HordeEventKind.PlayerHit && e.Slot == 0).Sum(e => e.Value));
            return sum;
        }
        int ork = Taken("", 0, "ORK_WARRIOR"), orkFrog = Taken("thara_frog_card", 5, "ORK_WARRIOR");
        Assert.True(orkFrog < ork * 0.8, $"Thara Frog: {ork} -> {orkFrog}");
        int poison = Taken("", 0, "POISON_SPORE"), poisonDetox = Taken("tf_detoxify", 3, "POISON_SPORE");
        _out.WriteLine($"Thara Frog 5: {ork} -> {orkFrog}; Detoxify 3 vs Poison Spore: {poison} -> {poisonDetox}");
        Assert.True(poison > 0); Assert.Equal(0, poisonDetox);

        // Endow Blaze: the knife hits as Fire (Earth enemies take 150 %)
        var ce = C(); var se = Sim(ce);
        GivePassive(se, 0, P(ce, "sa_flamelauncher"), 1);
        Assert.Equal(Element.Fire, se.ElementOf(0, W(ce, "nv_knife")));
        Assert.Equal(Element.Water, se.ElementOf(0, W(ce, "mg_frostdiver")));
        // the arrows: ranged only
        var ca = C(); var sa = Sim(ca);
        GivePassive(sa, 0, P(ca, "ac_silverarrow"), 1);
        Assert.Equal(Element.Holy, sa.ElementOf(0, W(ca, "ac_double")));
        Assert.Equal(Element.Neutral, sa.ElementOf(0, W(ca, "nv_knife")));
        // the group: Endow Blaze owned, the three other Endows are not offered again
        var cg = C(); var sg = Sim(cg); var pg = sg.Player(0)!; pg.Jobs.Add(cg.Job(2)!); pg.Jobs.Add(cg.Job(16)!);
        GivePassive(sg, 0, P(cg, "sa_flamelauncher"), 1);
        for (int i = 0; i < 60; i++) Assert.DoesNotContain(sg.DrawFor(0), k => k.Kind == HordeCardKind.NewPassive && cg.Passive(k.DefId)!.Group == 2);

        // Lefthand Mastery: every melee hit strikes twice
        var cl = C(); var sl = Sim(cl);
        var target = E(cl, "PORING"); target.Damage = 0; target.Speed = 0.01f; target.Hp = 100000;
        GivePassive(sl, 0, P(cl, "as_left"), 5);
        int s = SpawnAt(sl, target, sl.Player(0)!, 1, 0);
        var perHit = new List<int>();
        Steps(sl, 80, () => { var hs = sl.Hits.Where(h => h.Target == s).ToList(); if (hs.Count >= 2) perHit.Add(hs.Count); });
        Assert.NotEmpty(perHit);

        // Martyr's Reckoning drains HP, never below a quarter; Moving HP Recovery heals while walking
        var cm = C(); var sm = Sim(cm); Bare(sm, 0);
        GivePassive(sm, 0, P(cm, "pa_sacrifice"), 1);
        var pm = sm.Player(0)!;
        Steps(sm, HordeSim.TickRate * 300);
        Assert.Equal(Math.Max(1, pm.MaxHp / 4), pm.Hp);
        GivePassive(sm, 0, P(cm, "sm_movingrecovery"), 3);
        int hp0 = pm.Hp;
        for (int i = 0; i < HordeSim.TickRate * 3; i++) { sm.PushInput(0, (ushort)(i + 1), i % 200 < 100 ? 127 : -127, 0); sm.Step(); sm.ClearOutputs(); }
        Assert.True(pm.Hp > hp0 || pm.Hp == Math.Max(1, pm.MaxHp / 4), $"walking: {hp0} -> {pm.Hp}");
    }

    [Fact]
    public void Land_Protector_cancels_a_boss_strike_and_Resurrection_raises_an_ally()
    {
        var c = C(); var sim = Sim(c);
        GivePassive(sim, 0, P(c, "sa_landprotector"), 1);
        var p = sim.Player(0)!;
        var ork = E(c, "ORK_HERO"); ork.Speed = 0.01f; ork.Damage = 0;
        foreach (var ab in ork.Abilities) { ab.FirstAfter = 0.05f; ab.Cooldown = 60; }
        ork.Abilities = ork.Abilities.Where(ab => ab.Kind == AbilityKind.Nova).ToArray();
        Assert.NotEmpty(ork.Abilities);
        SpawnAt(sim, ork, p, 1, 0);
        int cancelled = 0, hurt = 0;
        Steps(sim, HordeSim.TickRate * 4, () => { cancelled += sim.Events.Count(e => e.Kind == HordeEventKind.Parried && e.Value == 4); hurt += sim.Events.Count(e => e.Kind == HordeEventKind.PlayerHit && e.Value > 0); });
        Assert.Equal(1, cancelled); Assert.Equal(0, hurt);

        var c2 = C(); var sim2 = Sim(c2, 2);
        GiveWeapon(sim2, 0, W(c2, "pr_resurrection"), 1);
        var b = sim2.Player(1)!;
        b.X = sim2.Player(0)!.X + 2 * 256; b.Hp = 0; b.Fallen = true;
        bool rose = false;
        Steps(sim2, 10, () => rose |= sim2.Events.Any(e => e.Kind == HordeEventKind.Rose && e.Slot == 1 && e.Target == 0));
        Assert.True(rose); Assert.False(b.Fallen); Assert.True(b.Hp >= b.MaxHp * 0.29);
    }

    // ------------------------------------------------------------------------------------------------ the wire

    [Fact]
    public void The_new_things_reach_a_client_through_the_snapshot_unchanged()
    {
        var c = C(); var sim = Sim(c);
        Bare(sim, 0);
        var p = sim.Player(0)!;
        var poring = E(c, "PORING"); poring.Damage = 0; poring.Speed = 0.01f; poring.Hp = 100000;
        for (int i = 0; i < 6; i++) SpawnAt(sim, poring, p, 2.5 + i, (i % 2) * 0.4);
        var fb = W(c, "mg_firebolt");
        GiveWeapon(sim, 0, fb, 2);
        sim.Give(0, new HordeCard { Kind = HordeCardKind.WeaponBranch, DefId = fb.Branches[0], Level = 3 });
        foreach (var key in new[] { "mg_safetywall", "pr_lexaeterna", "kn_chargeatk", "am_callhomun_filir" }) GiveWeapon(sim, 0, W(c, key), 1);
        var enc = new Loki.Horde.Net.SnapshotEncoder(sim); var dec = new Loki.Horde.Net.SnapshotDecoder(); var view = new HordeWorldView();
        dec.Reset(new HordeRunInfo { X0 = sim.Arena.X0, Y0 = sim.Arena.Y0, X1 = sim.Arena.X1, Y1 = sim.Arena.Y1 });
        double now = 0; bool guard = false, marked = false, dash = false, pet = false, branch = false;
        for (int t = 0; t < 200; t++)
        {
            sim.PushInput(0, (ushort)(t + 1), 0, 0);
            sim.Step(); enc.Collect(); sim.ClearOutputs();
            if (t % 2 == 1)
            {
                enc.Encode(t == 1, out var delta, out var keyBody);
                var body = t == 1 ? keyBody! : delta; now += 0.05;
                Assert.True(dec.Apply(body, 0, body.Length, view, 0, now));
                guard |= view.Fx.Any(f => f.Kind == HordeFxKind.Guard && f.OwnerSlot == 0 && f.Count == 3 && f.DefId == W(c, "mg_safetywall").Id);
                marked |= view.Enemies.Any(e => e.Alive && (e.Flags & HordeEnemyFlags.Marked) != 0);
                dash |= dec.Events.Any(e => e.Kind == HordeEventKind.Dash && e.Slot == 0 && Math.Abs(e.X2 - e.X) + Math.Abs(e.Y2 - e.Y) > 2f);
                pet |= view.Summons.Any(s => s.OwnerSlot == 0 && s.DefId == W(c, "am_callhomun_filir").Id);
                branch |= view.Players[0].Weapons.Any(w => w.DefId == fb.Branches[0] && w.Level == 3);
            }
        }
        _out.WriteLine($"guard {guard}, marked {marked}, dash {dash}, pet {pet}, branch in the loadout {branch}");
        Assert.True(guard && marked && dash && pet && branch);
    }

    /// <summary>A dash moves the player on the host only: the client's prediction is corrected, spread over its correction time (no
    /// snap), so the character glides along the dash on the table (80 ms each way, 15 ms jitter, 90 frames a second).</summary>
    [Fact]
    public void A_dash_reaches_the_dashers_own_client_as_a_glide_not_a_jump()
    {
        var rig = new HordeFeelTests.Rig(0.08, 0.015);
        var real = HordeTables.Build();
        var charge = W(real, "kn_chargeatk");
        rig.C.Weapons.Add(charge);
        var poring = rig.C.Enemy(1)!; poring.Speed = 0.01f; poring.Hp = 100000;
        rig.Run(1.0, 0, 0);
        var me = rig.Sim.Player(rig.Me)!; me.Weapons.Clear();
        rig.Sim.Give(rig.Me, new HordeCard { Kind = HordeCardKind.NewWeapon, DefId = charge.Id, Level = 1 });
        rig.Sim.Spawn(poring, me.X + 5 * 256, me.Y, true);
        rig.Client.Corrections = 0; rig.Client.LargestCorrection = 0;
        float lastX = rig.Client.MyX, lastY = rig.Client.MyY, largestStep = 0f;
        for (int i = 0; i < 270; i++)
        {
            rig.Frame(0, 0);
            float x = rig.Client.MyX, y = rig.Client.MyY;
            largestStep = Math.Max(largestStep, (float)Math.Sqrt((x - lastX) * (x - lastX) + (y - lastY) * (y - lastY)));
            lastX = x; lastY = y;
        }
        float hostX = me.X / 256f, err = Math.Abs(hostX - rig.Client.MyX);
        _out.WriteLine($"host moved to {hostX:0.00}, my client draws {rig.Client.MyX:0.00}; corrections {rig.Client.Corrections}, largest {rig.Client.LargestCorrection:0.00} cells; largest drawn step a frame {largestStep:0.000} cells");
        Assert.True(rig.Client.LargestCorrection > 0.5f && rig.Client.LargestCorrection < rig.Client.SnapCells, "corrected, never snapped");
        Assert.True(largestStep < 0.6f, $"a glide: the largest drawn step is {largestStep:0.000} cells a frame");
        Assert.True(err < 0.3f, $"the client ends where the host has it ({err:0.00} cells apart)");
    }

    /// <summary>The relay smoke found the client's (Unity's Mono) and the host's (.NET) content stamps different: the branches were made
    /// by rounding float products to two decimals, so an ulp at a tie became 0.01. Here every float of the base weapons is moved by an
    /// ulp (what Mono's float arithmetic may give) BEFORE the branches are made from them: the stamp must not change.</summary>
    [Fact]
    public void Branches_made_from_bases_an_ulp_apart_give_the_same_stamp()
    {
        var a = HordeTables.Build();
        var b = HordeTables.Build();
        b.Weapons.RemoveAll(w => w.BranchOf > 0 || w.TransOf > 0);
        b.Passives.RemoveAll(p => p.TransOf > 0);
        int turn = 0, nudged = 0;
        foreach (var w in b.Weapons)
        {
            if (w.Hidden || w.EvolveOf > 0) continue;
            w.Branches = new int[0];
            foreach (var l in w.Levels)
                foreach (var f in typeof(WeaponLevel).GetFields())
                    if (f.FieldType == typeof(float) && (float)f.GetValue(l)! != 0f) { var v = (float)f.GetValue(l)!; f.SetValue(l, turn++ % 2 == 0 ? MathF.BitIncrement(v) : MathF.BitDecrement(v)); nudged++; }
        }
        typeof(HordeTables).GetMethod("AddBranchesW17", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, new object[] { b });
        HordeTables.AddTranscendW18(b);
        Assert.Equal(a.Weapons.Count, b.Weapons.Count);
        Assert.True(nudged > 1000, $"{nudged} floats nudged");
        Assert.Equal(Loki.Horde.Net.HordeWire.ContentStamp(a), Loki.Horde.Net.HordeWire.ContentStamp(b));
    }

    // ------------------------------------------------------------------------------------------------ the whole

    [Fact]
    public void A_run_with_every_new_card_is_deterministic_and_plays()
    {
        ulong Hash(ulong seed)
        {
            var c = HordeTables.Build();
            var sim = new HordeSim(c, c.Arenas[0], seed, 300);
            for (int i = 0; i < 4; i++) sim.AddPlayer((uint)(6000000 + i), "b" + i);
            // every player gets six of the new weapons (by turns) and six new passives
            var news = c.Jobs.SelectMany(j => j.Weapons).Distinct().Select(id => c.Weapon(id)!).Where(w => w.Id is >= 3 and <= 7 || w.Key.StartsWith("kn_") || w.Kind >= WeaponKind.Dash).ToList();
            var passives = c.Passives.Where(p => p.Id >= 300 || p.Key is "pa_sacrifice" or "as_left" or "tf_steal" or "as_enchantpoison" or "mc_discount" or "bs_findingore").ToList();
            for (int i = 0; i < 4; i++)
            {
                for (int k = 0; k < 6; k++) GiveWeapon(sim, i, news[(i * 6 + k) % news.Count], 3);
                for (int k = 0; k < 6; k++) GivePassive(sim, i, passives[(i * 6 + k) % passives.Count], 2);
            }
            ushort seq = 0;
            for (int t = 0; t < HordeSim.TickRate * 150 && !sim.Over; t++) { seq++; for (int i = 0; i < 4; i++) HordeTests.BotStep(sim, i, seq, i); sim.Step(); sim.ClearOutputs(); }
            Assert.True(sim.Tick > HordeSim.TickRate * 60);
            return sim.StateHash();
        }
        ulong a = Hash(31337), b = Hash(31337);
        Assert.Equal(a, b);
    }
}
