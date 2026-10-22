using System.Reflection;
using Loki.Horde;
using Loki.Horde.Net;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>
/// Client wave 18 (h18feel), the design's point 37: the Yggdrasil Berry saves its holder's life (HordeSim.Souls.cs YggSave / YggClear).
/// Alone: spent, its slot burnt (never fires, never levels, the weapon cap still counts it). In a party: it rests 150 s (no save, no
/// heal), then saves again. The save: full HP, 4 s invulnerable, normal enemies within 4 cells killed (the holder's kills), elites and
/// bosses there pushed out alive; two lethal hits in one tick are one save; a save means no fall and no soul split (point 35).
/// </summary>
public class HordeYggTests
{
    readonly ITestOutputHelper _out;
    public HordeYggTests(ITestOutputHelper o) { _out = o; }

    static int T(double seconds) => HordeSim.Ticks(seconds);
    const int U = HordeSim.U, BerryId = 990001;

    /// <summary>The engine tests' tables + a berry card (item 607, as the game's: a heal below 50 %), no waves, no objectives, harmless
    /// enemies; the given players 12 cells apart, slot 0 holding the berry.</summary>
    static HordeSim Make(int players, out HordeContent c, Action<HordeContent>? tune = null)
    {
        c = HordeTestContent.Build();
        c.Waves.Clear(); foreach (var e in c.Enemies) e.Damage = 0;
        c.Objectives.Enabled = false;
        c.Weapons.Add(new WeaponDef { Id = BerryId, Key = "nv_yggberry", ItemId = SoulRulesDef.YggdrasilBerry, Kind = WeaponKind.Aura, Target = Targeting.LowestHpAlly, Class = DamageClass.Magic, Weight = 0,
            Levels = new[] { new WeaponLevel { Cooldown = 5f, Range = 0.3f, HealPct = 20f, BelowPct = 50f, Duration = 0.1f } } });
        tune?.Invoke(c);
        var sim = new HordeSim(c, c.Arenas[0], 31, 900);
        for (int i = 0; i < players; i++) sim.AddPlayer((uint)(8000000 + i), "p" + i);
        int cx = (sim.Arena.X0 + sim.Arena.X1 + 1) / 2 * U, cy = (sim.Arena.Y0 + sim.Arena.Y1 + 1) / 2 * U;
        for (int i = 0; i < players; i++) { var p = sim.Player(i)!; p.X = cx + (int)(Math.Cos(i * 2.1) * 12 * U); p.Y = cy + (int)(Math.Sin(i * 2.1) * 12 * U); p.Weapons.Clear(); }
        sim.Player(0)!.Weapons.Add(new OwnedWeapon { Def = c.Weapon(BerryId)!, Level = 1, NextTick = 1 });
        Steps(sim, T(2.5));
        return sim;
    }

    static void Steps(HordeSim sim, int n, List<HordeEvent>? log = null)
    {
        for (int t = 0; t < n && !sim.Over; t++)
        {
            foreach (var p in sim.Players) if (p.Present && p.Open != null) sim.Choose(p.Slot, p.Open.Id, 0);
            sim.Step(); log?.AddRange(sim.Events); log?.AddRange(sim.Hits); sim.ClearOutputs();
        }
    }

    /// <summary>A still enemy of the given rank's first kind beside the runner, hitting for <paramref name="dmg"/> (its own HP huge).</summary>
    static int Hitter(HordeSim sim, int slot, int dmg, double cellsAway = 0.15)
    {
        var p = sim.Player(slot)!;
        int e = sim.Spawn(sim.C.Enemy(1)!, p.X + (int)(cellsAway * U), p.Y, true);
        sim.Enemies[e].Damage = dmg; sim.Enemies[e].Speed = 0; sim.Enemies[e].Hp = sim.Enemies[e].MaxHp = 1_000_000;
        return e;
    }

    static int Still(HordeSim sim, EnemyDef d, int x, int y, int hp = 1_000)
    {
        int e = sim.Spawn(d, x, y, true); sim.Enemies[e].Speed = 0; sim.Enemies[e].Damage = 0; sim.Enemies[e].Hp = sim.Enemies[e].MaxHp = hp; return e;
    }

    [Fact]
    public void Alone_a_lethal_hit_is_saved_once_full_HP_4_s_invulnerable_then_the_berry_is_spent_and_its_slot_burnt()
    {
        var sim = Make(1, out var c);
        var p = sim.Player(0)!; var log = new List<HordeEvent>();
        int e = Hitter(sim, 0, 1_000_000);
        Steps(sim, T(0.5), log);
        sim.Enemies[e].Alive = false; // (the hitter was a normal enemy within 4 cells: the save killed it anyway)
        var saved = log.Single(x => x.Kind == HordeEventKind.LifeSaved);
        Assert.Equal(0, saved.Slot); Assert.Equal(BerryId, saved.DefId); Assert.Equal(0, saved.Value);
        Assert.DoesNotContain(log, x => x.Kind == HordeEventKind.Fell);
        Assert.False(p.Fallen); Assert.False(sim.Over); Assert.Equal(p.MaxHp, p.Hp);
        Assert.Equal(saved.Tick + T(4), p.SafeUntil);
        Assert.True(sim.YggBurnt(p));
        Assert.Contains(p.Weapons, w => w.Def.Id == BerryId); // the slot stays taken
        // it never fires again: hurt below 50 %, no heal from it for a minute
        Steps(sim, T(4.5));
        p.Hp = p.MaxHp / 4; log.Clear();
        Steps(sim, T(60), log);
        Assert.DoesNotContain(log, x => x.Kind == HordeEventKind.Nova && x.DefId == BerryId);
        Assert.Equal(p.MaxHp / 4, p.Hp);
        // and it never saves again: the next lethal hit ends the run
        Hitter(sim, 0, 1_000_000); Steps(sim, T(1));
        Assert.True(sim.Over); Assert.Equal(HordeOutcome.Defeat, sim.Outcome);
    }

    [Fact]
    public void A_burnt_slot_is_never_levelled_nor_refilled_and_the_weapon_cap_still_counts_it()
    {
        var sim = Make(1, out var c, cc => { cc.Rules.MaxWeapons = 3; foreach (var w in cc.Weapons) if (w.Id != BerryId && w.Levels.Length > 0) w.Weight = Math.Max(1, w.Weight); });
        var p = sim.Player(0)!;
        // two more weapons: at the cap of 3 with the berry
        foreach (var w in c.Weapons.Where(w => w.Id != BerryId && w.Levels.Length > 1 && w.Kind != WeaponKind.Orbiter).Take(2)) p.Weapons.Add(new OwnedWeapon { Def = w, Level = 1, NextTick = sim.Tick + 1 });
        int nonSig = p.Weapons.Count(w => !w.Signature);
        Assert.Equal(3, nonSig);
        Hitter(sim, 0, 1_000_000); Steps(sim, T(0.5));
        Assert.True(sim.YggBurnt(p));
        // many level-ups and their offers: never the berry's level, never a new weapon; the weapons stay three, the berry at level 1
        var gain = typeof(HordeSim).GetMethod("GainExp", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var seen = new List<HordeCard>();
        for (int k = 0; k < 12; k++)
        {
            gain.Invoke(sim, new object[] { sim.PartyExpToNext() - sim.PartyExp });
            for (int t = 0; t < T(1); t++) { if (p.Open != null) { if (p.Open.Cards != null) seen.AddRange(p.Open.Cards); sim.Choose(0, p.Open.Id, 0); } sim.Step(); sim.ClearOutputs(); }
        }
        _out.WriteLine($"{seen.Count} cards seen: {string.Join(", ", seen.Select(x => x.Kind + ":" + x.DefId))}");
        Assert.True(seen.Count > 20);
        Assert.DoesNotContain(seen, x => x.Kind == HordeCardKind.WeaponLevel && x.DefId == BerryId);
        Assert.DoesNotContain(seen, x => x.Kind == HordeCardKind.NewWeapon);
        Assert.Equal(3, p.Weapons.Count(w => !w.Signature)); Assert.Equal(1, p.Weapons.Single(w => w.Def.Id == BerryId).Level);
    }

    [Fact]
    public void In_a_party_it_rests_150_s_no_save_no_heal_then_saves_again_and_a_save_means_no_soul_split()
    {
        var sim = Make(2, out var c);
        var p = sim.Player(0)!; var log = new List<HordeEvent>();
        int h = Hitter(sim, 0, 1_000_000);
        Steps(sim, T(0.3), log); sim.Enemies[h].Alive = false;
        var saved = log.Single(x => x.Kind == HordeEventKind.LifeSaved);
        Assert.Equal(150, saved.Value);
        Assert.DoesNotContain(log, x => x.Kind == HordeEventKind.Fell || x.Kind == HordeEventKind.SoulSplit);
        Assert.Empty(sim.Souls); Assert.False(sim.YggBurnt(p)); Assert.Equal(saved.Tick + T(150), sim.YggCoolUntil(p));
        // resting: hurt below 50 %, no heal from it
        Steps(sim, T(4.5)); p.Hp = p.MaxHp / 4; log.Clear();
        Steps(sim, T(100), log);
        Assert.DoesNotContain(log, x => x.Kind == HordeEventKind.Nova && x.DefId == BerryId);
        Assert.Equal(p.MaxHp / 4, p.Hp);
        // a lethal hit while it rests: it falls (and its soul splits)
        log.Clear(); h = Hitter(sim, 0, 1_000_000); Steps(sim, T(0.5), log); sim.Enemies[h].Alive = false;
        Assert.True(p.Fallen); Assert.Contains(log, x => x.Kind == HordeEventKind.SoulSplit && x.Slot == 0);
        // the ally gathers it; past the 150 s the berry heals and saves again
        while (sim.Souls.Count > 0) { var s = sim.Souls[0]; var q = sim.Player(1)!; q.X = s.X; q.Y = s.Y; Steps(sim, T(2)); }
        Assert.False(p.Fallen);
        Steps(sim, Math.Max(1, sim.YggCoolUntil(p) - sim.Tick + 1));
        p.Hp = p.MaxHp / 4; log.Clear(); Steps(sim, T(6), log);
        Assert.Contains(log, x => x.Kind == HordeEventKind.Nova && x.DefId == BerryId);
        Assert.True(p.Hp > p.MaxHp / 4, "healed again");
        log.Clear(); h = Hitter(sim, 0, 1_000_000); Steps(sim, T(0.5), log);
        Assert.Single(log, x => x.Kind == HordeEventKind.LifeSaved); Assert.False(p.Fallen); Assert.Equal(2, sim.YggSaves);
    }

    [Fact]
    public void Two_lethal_hits_in_one_tick_are_one_save()
    {
        var sim = Make(2, out _);
        var p = sim.Player(0)!; var log = new List<HordeEvent>();
        Hitter(sim, 0, 1_000_000, 0.1); Hitter(sim, 0, 1_000_000, -0.1);
        Steps(sim, T(0.5), log);
        Assert.Single(log, x => x.Kind == HordeEventKind.LifeSaved);
        Assert.False(p.Fallen); Assert.Equal(p.MaxHp, p.Hp); Assert.Equal(1, sim.YggSaves);
    }

    [Fact]
    public void The_save_kills_normal_enemies_within_4_cells_and_pushes_elites_and_bosses_out_alive()
    {
        var sim = Make(1, out var c);
        var p = sim.Player(0)!;
        var normal = c.Enemies.First(e => e.Rank == EnemyRank.Normal); var elite = c.Enemies.First(e => e.Rank == EnemyRank.Elite); var boss = c.Enemies.First(e => e.Rank == EnemyRank.Boss);
        var near = new List<int>();
        for (int k = 0; k < 6; k++) { double a = k * Math.PI / 3; near.Add(Still(sim, normal, p.X + (int)(Math.Cos(a) * 3 * U), p.Y + (int)(Math.Sin(a) * 3 * U), 50)); }
        int far = Still(sim, normal, p.X + 6 * U, p.Y);
        int el = Still(sim, elite, p.X - 2 * U, p.Y, 100_000), bo = Still(sim, boss, p.X, p.Y + 3 * U, 100_000);
        int kills0 = p.Kills, pickups0 = sim.Pickups.Count;
        var log = new List<HordeEvent>();
        Hitter(sim, 0, 1_000_000);
        Steps(sim, T(1.5), log);
        Assert.Single(log, x => x.Kind == HordeEventKind.LifeSaved);
        Assert.All(near, i => Assert.False(sim.Enemies[i].Alive));
        Assert.True(sim.Enemies[far].Alive);
        Assert.True(p.Kills >= kills0 + 6 + 1, $"kills {p.Kills - kills0} (6 round it + the hitter)");
        Assert.True(sim.Pickups.Count > pickups0, "their EXP dropped");
        foreach (int i in new[] { el, bo })
        {
            var e = sim.Enemies[i]; double d = Math.Sqrt(HordeSim.Dist2(e.X, e.Y, p.X, p.Y)) / U;
            _out.WriteLine($"{e.Def.Key} ({e.Def.Rank}) alive {e.Alive} at {d:0.00} cells");
            Assert.True(e.Alive); Assert.True(d > 4.0, $"{e.Def.Key} pushed out: {d:0.00} cells");
        }
        Assert.Equal(2, sim.YggPushes);
    }

    [Fact]
    public void The_snapshot_says_whose_berry_is_burnt_or_resting()
    {
        var sim = Make(2, out _);
        var enc = new SnapshotEncoder(sim); var dec = new SnapshotDecoder(); var view = new HordeWorldView();
        dec.Reset(new HordeRunInfo { X0 = sim.Arena.X0, Y0 = sim.Arena.Y0, X1 = sim.Arena.X1, Y1 = sim.Arena.Y1 });
        double now = 0; bool first = true;
        void Snap() { enc.Encode(first, out var delta, out var key); var body = first ? key! : delta; first = false; now += 0.05; Assert.True(dec.Apply(body, 0, body.Length, view, 0, now)); }
        Snap(); Assert.Empty(view.Berries);
        Hitter(sim, 0, 1_000_000);
        for (int t = 0; t < T(0.5); t++) { sim.Step(); enc.Collect(); sim.ClearOutputs(); }
        Snap();
        var b = view.BerryOf(0); Assert.False(b.Burnt); Assert.Equal(sim.YggCoolUntil(sim.Player(0)!), b.Until); Assert.Single(view.Berries);
    }
}
