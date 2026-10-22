using Loki.Horde;
using Loki.Horde.Content;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>
/// Client wave 15 (w15horde), the design's points of 2026-10-08 on Horde: 21 (the bosses come in a random order), 25 (a player back in
/// a run it left gets its build back), 26 (no time limit on a choice when alone), 27 (the real character's reward: a small time
/// reward; the NPC's level-cap guard is HordeHostTests' and the w8reward smoke's).
/// </summary>
public class HordeW15Tests
{
    readonly ITestOutputHelper _out;
    public HordeW15Tests(ITestOutputHelper o) { _out = o; }

    static HordeSim Sim(int players, out HordeContent c, ulong seed = 77)
    {
        c = HordeTestContent.Build();
        c.Waves.Clear(); foreach (var e in c.Enemies) e.Damage = 0; // nobody falls: the offers and the builds only
        var sim = new HordeSim(c, c.Arenas[0], seed, 900);
        for (int i = 0; i < players; i++) sim.AddPlayer((uint)(4000000 + i), "p" + i);
        return sim;
    }

    /// <summary>Runs the sim until the party's next level-up opens its offers (porings killed beside player 0).</summary>
    static void NextPause(HordeSim sim, HordeContent c)
    {
        var poring = c.Enemy(1)!;
        int level = sim.PartyLevel, guard = 0;
        while (!sim.Paused && guard++ < 8000)
        {
            var p0 = sim.Player(0) ?? sim.Players.First(p => p.Present);
            if (sim.EnemiesAlive < 6) sim.Spawn(poring, p0.X + 300, p0.Y, true);
            sim.Step(); sim.ClearOutputs();
        }
        Assert.True(sim.Paused, $"a level-up came (level {sim.PartyLevel}, tick {sim.Tick})");
    }

    // ------------------------------------------------------------------------------------------------ point 26

    [Fact]
    public void Alone_a_choice_has_no_time_limit_and_the_limit_starts_when_a_second_player_comes_in()
    {
        var sim = Sim(1, out var c);
        NextPause(sim, c);
        var me = sim.Player(0)!;
        Assert.NotNull(me.Open);
        Assert.Equal(0f, me.Open!.Seconds);                    // the client shows no time bar and never picks by itself
        Assert.Equal(HordeSim.NoDeadline, me.OpenDeadline);
        int id = me.Open.Id, wall0 = sim.WallTick;
        for (int i = 0; i < HordeSim.TickRate * 600; i++) { sim.Step(); sim.ClearOutputs(); } // ten minutes of wall time
        Assert.True(sim.Paused); Assert.NotNull(me.Open); Assert.Equal(id, me.Open!.Id); Assert.False(me.IdleChooser);
        Assert.Equal(0, sim.ChoiceLimitsMet);
        // a second player comes in while it is open: the limit starts now (the run's current one) and the offer is sent again
        int slot = sim.AddPlayer(4000009, "late");
        sim.Step();
        Assert.Contains(sim.OffersOpened, kv => kv.Key == 0 && kv.Value.Id == id && kv.Value.Seconds == sim.CurrentChoiceSeconds);
        Assert.Equal(30f, me.Open!.Seconds);
        sim.ClearOutputs();
        // it leaves again before the limit: alone again, no limit (sent again with Seconds 0)
        sim.RemovePlayer(slot);
        sim.Step();
        Assert.Contains(sim.OffersOpened, kv => kv.Key == 0 && kv.Value.Id == id && kv.Value.Seconds == 0f);
        Assert.Equal(HordeSim.NoDeadline, me.OpenDeadline);
        sim.ClearOutputs();
        for (int i = 0; i < HordeSim.TickRate * 60; i++) { sim.Step(); sim.ClearOutputs(); }
        Assert.NotNull(me.Open);
        // answered: the world goes on, and the limit was never met
        int answered = me.Open!.Id;
        while (sim.Paused) { if (me.Open != null) sim.Choose(0, me.Open.Id, 0); sim.Step(); sim.ClearOutputs(); }
        Assert.Equal(0, sim.ChoiceLimitsMet); Assert.Equal(30f, sim.CurrentChoiceSeconds);
        _out.WriteLine($"alone: offer {id} open for {(sim.WallTick - wall0) / HordeSim.TickRate} s of wall time, never taken by the host; answered {answered}");
    }

    // ------------------------------------------------------------------------------------------------ point 25

    [Fact]
    public void A_player_back_in_a_run_it_left_gets_its_whole_build_back()
    {
        var sim = Sim(2, out var c);
        // a few level-ups, every card taken by both (card 0, or 1 for player 1 for some variety)
        for (int round = 0; round < 6; round++)
        {
            NextPause(sim, c);
            while (sim.Paused) { foreach (var p in sim.Players) if (p.Present && p.Open != null) sim.Choose(p.Slot, p.Open.Id, p.Slot == 1 ? Math.Min(1, p.Open.Cards.Length - 1) : 0); sim.Step(); sim.ClearOutputs(); }
        }
        var before = sim.Player(1)!;
        string Build(SimPlayer p) =>
            $"job {p.JobId} tier {p.Tier} jobs [{string.Join(",", p.Jobs.Select(j => j.JobId))}] weapons [{string.Join(",", p.Weapons.Select(w => $"{w.Def.Id}@{w.Level}{(w.Signature ? "s" : "")}"))}] " +
            $"passives [{string.Join(",", p.Passives.Select(x => $"{x.Def.Id}@{x.Level}"))}] stats [{string.Join(",", p.StatPoints)}]";
        // the next level-up: player 1 leaves with its card choice still open
        NextPause(sim, c);
        Assert.NotNull(before.Open);
        string built = Build(before); int maxHp = before.MaxHp; int speed = before.SpeedPerTick;
        Assert.True(before.Weapons.Count + before.Passives.Count >= 2, built);
        sim.RemovePlayer(1);
        Assert.Equal(1, before.CardsOwed);
        while (sim.Paused) { var p0 = sim.Player(0)!; if (p0.Open != null) sim.Choose(0, p0.Open.Id, 0); sim.Step(); sim.ClearOutputs(); }
        for (int i = 0; i < 200; i++) { sim.Step(); sim.ClearOutputs(); }
        // back: a new slot with the same build, the open card choice owed
        int slot = sim.AddPlayer(4000001, "p1");
        Assert.NotEqual(1, slot);
        var back = sim.Player(slot)!;
        Assert.True(back.Rebuilt);
        Assert.Equal(built, Build(back));
        Assert.Equal(maxHp, back.MaxHp); Assert.Equal(back.MaxHp, back.Hp); Assert.Equal(speed, back.SpeedPerTick);
        sim.Step();
        Assert.NotNull(back.Open);
        Assert.False(back.Open!.IsJob);
        _out.WriteLine($"left with: {built}\nback with: {Build(back)} (slot {slot}, a card choice owed and opened)");
        // a newcomer (another account) starts from nothing as before
        int fresh = sim.AddPlayer(4000077, "new");
        Assert.False(sim.Player(fresh)!.Rebuilt);
        Assert.Single(sim.Player(fresh)!.Weapons);
        // the results add up the two stays of player 1 in one row
        sim.End(HordeOutcome.Stopped);
        var res = sim.Results(1, HordeRewardSize.Normal);
        Assert.Single(res.Players, r => r.Aid == 4000001);
    }

    // ------------------------------------------------------------------------------------------------ point 21

    [Fact]
    public void The_bosses_come_in_a_random_order_each_run_with_the_strength_of_their_mark()
    {
        var c = HordeTables.Build();
        var bosses = c.Waves.Where(w => w.Once && c.Enemy(w.EnemyId)?.Rank == EnemyRank.Boss).OrderBy(w => w.From).Select(w => w.EnemyId).ToList();
        Assert.True(bosses.Count >= 2, "the tables have at least two boss marks");
        // client wave 17 (h17boss), point 23: the bosses without a mark of their own (Mistress, Eddga, Maya, Moonlight Flower) are dealt too:
        // each run's marks get distinct bosses of the whole pool (rewritten from "the marks' own bosses in some order"; still: never twice in
        // a run, the seed decides, a fair deal)
        var pool = c.Enemies.Where(e => e.Rank == EnemyRank.Boss).Select(e => e.Id).ToList();
        Assert.True(pool.Count >= 6, $"the pool holds the two mark bosses and the four new ones ({pool.Count})");
        var orders = new Dictionary<string, int>();
        var atMark = bosses.Select(_ => new Dictionary<int, int>()).ToList();
        const int Runs = 600;
        for (ulong seed = 1; seed <= Runs; seed++)
        {
            var sim = new HordeSim(c, c.Arenas[0], seed * 7919, 900);
            Assert.Equal(bosses.Count, sim.BossOrder.Count);                         // one boss per mark
            Assert.Equal(sim.BossOrder.Count, sim.BossOrder.Distinct().Count());     // never twice in a run
            Assert.All(sim.BossOrder, id => Assert.Contains(id, pool));
            for (int mk = 0; mk < bosses.Count; mk++) atMark[mk][sim.BossOrder[mk]] = atMark[mk].TryGetValue(sim.BossOrder[mk], out var k) ? k + 1 : 1;
            var key = string.Join(" → ", sim.BossOrder.Select(id => c.Enemy(id)!.Key));
            orders[key] = orders.TryGetValue(key, out var n) ? n + 1 : 1;
            Assert.Equal(sim.BossOrder, new HordeSim(c, c.Arenas[0], seed * 7919, 900).BossOrder); // the seed decides (deterministic)
        }
        foreach (var kv in orders.OrderByDescending(kv => kv.Value)) _out.WriteLine($"{kv.Value,3} runs: {kv.Key}");
        int fair = Runs / pool.Count / 2; // at least half of a fair share
        for (int mk = 0; mk < bosses.Count; mk++)
        {
            _out.WriteLine($"mark {mk + 1}: " + string.Join(", ", pool.Select(id => $"{c.Enemy(id)!.Key} {(atMark[mk].TryGetValue(id, out var k) ? k : 0)}")));
            foreach (int id in pool) Assert.True(atMark[mk].TryGetValue(id, out var k) && k >= fair, $"{c.Enemy(id)!.Key} comes at mark {mk + 1} in {k} of {Runs} runs (at least {fair}: a fair deal)");
        }
        Assert.True(orders.Count >= pool.Count * (pool.Count - 1) * 3 / 4, $"most of the {pool.Count * (pool.Count - 1)} orders occur ({orders.Count})");

        // the first boss mark, played: a seed whose first boss is NOT the tables' first takes the first boss's HP and damage
        ulong swapped = 0;
        for (ulong seed = 1; seed <= 200 && swapped == 0; seed++) if (new HordeSim(c, c.Arenas[0], seed * 7919, 900).BossOrder[0] != bosses[0]) swapped = seed * 7919;
        Assert.NotEqual(0UL, swapped);
        foreach (var e in c.Enemies) { e.Damage = 0; e.ShotDamage = 0; }
        var first = c.Enemy(bosses[0])!; var comes = c.Enemy(new HordeSim(c, c.Arenas[0], swapped, 900).BossOrder[0])!;
        var run = new HordeSim(c, c.Arenas[0], swapped, 900);
        run.AddPlayer(4100000, "solo");
        SimEnemy? boss = null; int guard = 0;
        var mark = c.Waves.First(w => w.Once && w.EnemyId == bosses[0]);
        while (boss == null && guard++ < HordeSim.TickRate * (int)(mark.From + 30))
        {
            var me = run.Player(0)!;
            if (me.Open != null) run.Choose(0, me.Open.Id, 0);
            run.Step(); run.ClearOutputs();
            boss = run.Enemies.FirstOrDefault(e => e.Alive && e.Def.Rank == EnemyRank.Boss);
        }
        Assert.NotNull(boss);
        Assert.Equal(comes.Id, boss!.Def.Id);
        double hpMul = c.Scaling.BossHpMul is { Length: > 0 } m ? m[0] : 1.0;
        Assert.Equal((int)Math.Round(first.Hp * hpMul), boss.MaxHp);
        Assert.Equal(first.Hp / (double)comes.Hp, boss.HpScale, 6);
        _out.WriteLine($"seed {swapped}: at {mark.From} s {comes.Key} came in place of {first.Key} with {boss.MaxHp} HP (its own {comes.Hp}, the mark's {first.Hp})");
    }

    // ------------------------------------------------------------------------------------------------ point 27

    [Fact]
    public void The_reward_is_a_small_time_reward()
    {
        var r = HordeTables.Build().Reward;
        int Base(float secs, HordeRewardSize size, int score = 1400, int kills = 500) { HordeReward.Shares(r, score, secs, size, out int b, out int j, kills); Assert.Equal(b, j); return b; }
        Assert.Equal(2000, Base(900, HordeRewardSize.Normal));               // a full 15-minute run with 500 kill-points: 20 % of the next level
        Assert.True(Base(900, HordeRewardSize.Normal, 2000, 1000) > Base(900, HordeRewardSize.Normal, 1400, 500)); // more kills raise the reward
        Assert.True(Base(1200, HordeRewardSize.Normal, 2000, 750) > Base(900, HordeRewardSize.Normal, 1400, 500)); // endless survival raises the reward
        Assert.Equal(1000, Base(450, HordeRewardSize.Normal, 700, 250));     // half time & half kills: 10 %
        Assert.Equal(0, Base(59, HordeRewardSize.Normal));                   // under MinSeconds: nothing
        Assert.Equal(1000, Base(900, HordeRewardSize.Low));
        Assert.Equal(3500, Base(900, HordeRewardSize.High));
        Assert.Equal(0, Base(900, HordeRewardSize.Off));
    }
}
