using Loki.Horde;
using Loki.Horde.Net;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>
/// Client wave 9 (h9move), the design's rules of 2026-10-05 (00:05 / 00:10): the world pauses while anyone chooses a card or a job,
/// and the party shares one EXP bar and one run level.
/// </summary>
public class HordePauseTests
{
    readonly ITestOutputHelper _out;
    public HordePauseTests(ITestOutputHelper o) { _out = o; }

    static HordeSim Sim(int players, out HordeContent c)
    {
        c = HordeTestContent.Build();
        c.Waves.Clear(); foreach (var e in c.Enemies) e.Damage = 0; // nobody falls: these tests are about the offers and the pool
        var sim = new HordeSim(c, c.Arenas[0], 77, 900);
        for (int i = 0; i < players; i++) sim.AddPlayer((uint)(4000000 + i), "p" + i);
        return sim;
    }

    [Fact]
    public void The_world_waits_while_someone_chooses_and_the_tick_stands()
    {
        var sim = Sim(2, out var c);
        // the party reaches level 2 through the engine's own path: porings beside player 0 are killed by its starting weapon,
        // their EXP pickups taken
        var poring = c.Enemy(1)!;
        int guard = 0;
        while (sim.PartyLevel == 1 && guard++ < 4000)
        {
            var p0 = sim.Player(0)!;
            if (sim.EnemiesAlive < 6) sim.Spawn(poring, p0.X + 300, p0.Y, true);
            sim.PushInput(0, (ushort)guard, 0, 0); sim.PushInput(1, (ushort)guard, 0, 0);
            sim.Step(); sim.ClearOutputs();
        }
        Assert.Equal(2, sim.PartyLevel);
        Assert.Equal(2, sim.Player(0)!.Level); Assert.Equal(2, sim.Player(1)!.Level);
        // the offers open at the next step and the world stops
        sim.Step();
        Assert.True(sim.Paused);
        Assert.NotNull(sim.Player(0)!.Open); Assert.NotNull(sim.Player(1)!.Open);
        int tick = sim.Tick; ulong hash = sim.StateHash();
        var rp = sim.Player(1)!; int x1 = rp.X;
        for (int i = 0; i < 100; i++) { sim.PushInput(1, (ushort)(5000 + i), 127, 0); sim.Step(); sim.ClearOutputs(); }
        Assert.Equal(tick, sim.Tick);                 // nothing advanced
        Assert.Equal(hash, sim.StateHash());          // nothing changed
        Assert.Equal(x1, rp.X);                       // inputs were not applied (they wait)
        Assert.True(sim.WallTick > tick + 50);
        // one answers: still paused (the other is choosing); the other answers: the world resumes at the next step
        Assert.True(sim.Choose(0, sim.Player(0)!.Open!.Id, 0));
        sim.Step(); Assert.True(sim.Paused);
        Assert.True(sim.Choose(1, rp.Open!.Id, 0));
        sim.Step(); Assert.False(sim.Paused); Assert.Equal(tick + 1, sim.Tick);
        // the queued inputs are applied now (two or three a tick), never lost
        for (int i = 0; i < 30; i++) { sim.Step(); sim.ClearOutputs(); }
        Assert.True(rp.X > x1);
        _out.WriteLine($"paused {sim.WallTick - sim.Tick} wall ticks in all; resumed at tick {sim.Tick}");
    }

    /// <summary>Runs the sim until the party's next level-up opens its offers (the world pauses).</summary>
    static void NextPause(HordeSim sim, HordeContent c)
    {
        var poring = c.Enemy(1)!;
        int level = sim.PartyLevel, guard = 0;
        while (sim.PartyLevel == level && guard++ < 8000)
        {
            var p0 = sim.Player(0)!;
            if (sim.EnemiesAlive < 6) sim.Spawn(poring, p0.X + 300, p0.Y, true);
            sim.Step(); sim.ClearOutputs();
        }
        var pl = sim.Player(0)!;
        Assert.True(sim.PartyLevel > level, $"a level-up came (level {sim.PartyLevel}, pool {sim.PartyExp}/{sim.PartyExpToNext()}, tick {sim.Tick}, wall {sim.WallTick}, paused {sim.Paused}, over {sim.Over}, enemies {sim.EnemiesAlive}, pickups {sim.Pickups.Count}, p0 hp {pl.Hp} fallen {pl.Fallen} weapons {pl.Weapons.Count} open {(pl.Open != null)} queued {pl.Offers.Count})");
        sim.Step(); sim.ClearOutputs();
        Assert.True(sim.Paused);
    }

    /// <summary>design note, 2026-10-05 01:25: a run where nobody answers: 30, 20, 10, 5, 5 s and the host picks for them. (Solo too until
    /// client wave 15; the design's point 26 of 2026-10-08 removed the limit when alone: HordeW15Tests proves that case.)</summary>
    [Theory]
    [InlineData(2)]
    public void A_choice_nobody_answers_times_out_and_the_runs_limit_drops_30_20_10_5_5(int players)
    {
        var sim = Sim(players, out var c);
        Assert.Equal(30f, c.Rules.ChoiceSeconds); Assert.Equal(10f, c.Rules.ChoiceSecondsStep); Assert.Equal(5f, c.Rules.ChoiceSecondsMin);
        var seen = new List<float>();
        for (int round = 0; round < 5; round++)
        {
            NextPause(sim, c);
            float limit = sim.Player(0)!.Open!.Seconds;
            seen.Add(limit);
            Assert.Equal(sim.CurrentChoiceSeconds, limit);
            foreach (var p in sim.Players) if (p.Present) Assert.Equal(limit, p.Open!.Seconds);
            // at a job mark (party level 5) the pause has two windows per player: the job, then the card
            int windows = sim.Players.Any(p => p.Present && p.Offers.Count > 0) ? 2 : 1;
            int tick = sim.Tick, wall0 = sim.WallTick;
            // nobody answers: each window lasts the limit (wall time), then the host picks card 0 for everyone and the world goes on
            int guard = 0;
            while (sim.Paused && guard++ < windows * HordeSim.Ticks(limit) + 10) { sim.Step(); sim.ClearOutputs(); }
            Assert.False(sim.Paused, $"round {round}: still paused after {windows} × {limit} s (level {sim.PartyLevel}, limit now {sim.CurrentChoiceSeconds})");
            Assert.Equal(tick + 1, sim.Tick);
            Assert.InRange(sim.WallTick - wall0, windows * HordeSim.Ticks(limit) - 1, windows * HordeSim.Ticks(limit) + 2);
            foreach (var p in sim.Players) if (p.Present) Assert.True(p.IdleChooser);
            _out.WriteLine($"round {round}: level {sim.PartyLevel}, {windows} window(s) of {limit} s, nobody answered → limit {sim.CurrentChoiceSeconds} s");
        }
        _out.WriteLine($"{players} player(s), nobody answers: limits {string.Join(", ", seen)} s; met {sim.ChoiceLimitsMet} times");
        Assert.Equal(new[] { 30f, 20f, 10f, 5f, 5f }, seen);
    }

    /// <summary>Every open offer answered with card 0 at once, until the pause ends.</summary>
    static void AnswerAll(HordeSim sim)
    {
        for (int guard = 0; sim.Paused && guard < 10; guard++)
        {
            foreach (var p in sim.Players) if (p.Present && p.Open != null) Assert.True(sim.Choose(p.Slot, p.Open.Id, 0));
            sim.Step(); sim.ClearOutputs();
        }
        Assert.False(sim.Paused);
    }

    /// <summary>An answered pause keeps the limit; an answer in the window's last second that ENDS the pause counts as the limit met;
    /// a late answer followed by an early one that ends the pause does not.</summary>
    [Fact]
    public void An_answered_pause_keeps_the_limit_and_a_last_second_answer_counts_as_met()
    {
        var sim = Sim(2, out var c);
        // level 2: answered at once: still 30
        NextPause(sim, c);
        Assert.Equal(30f, sim.Player(0)!.Open!.Seconds);
        AnswerAll(sim);
        Assert.Equal(30f, sim.CurrentChoiceSeconds);
        // level 3: one answers early, the other 0.6 s before the deadline (h9flow's client auto-pick): met → 20
        NextPause(sim, c);
        Assert.True(sim.Choose(0, sim.Player(0)!.Open!.Id, 0));
        int late = HordeSim.Ticks(30f) - HordeSim.Ticks(0.6f);
        for (int i = 0; i < late - 1; i++) { sim.Step(); sim.ClearOutputs(); Assert.True(sim.Paused); }
        Assert.True(sim.Choose(1, sim.Player(1)!.Open!.Id, 1));
        sim.Step(); sim.ClearOutputs(); Assert.False(sim.Paused);
        Assert.Equal(20f, sim.CurrentChoiceSeconds); Assert.Equal(1, sim.ChoiceLimitsMet);
        // level 4: the pause shows 20 and answers at 5 s keep it; the limit never goes back up
        NextPause(sim, c);
        Assert.Equal(20f, sim.Player(0)!.Open!.Seconds);
        for (int i = 0; i < HordeSim.Ticks(5f); i++) { sim.Step(); sim.ClearOutputs(); }
        AnswerAll(sim);
        Assert.Equal(20f, sim.CurrentChoiceSeconds);
        // level 5 (the job mark: two windows, the job then the card): player 0 answers its job in the last second, player 1 too,
        // then both answer their cards at once → the answer that ENDED the pause was early → not met, still 20
        NextPause(sim, c);
        Assert.True(sim.Player(0)!.Open!.IsJob);
        for (int i = 0; i < HordeSim.Ticks(19.5f); i++) { sim.Step(); sim.ClearOutputs(); }
        Assert.True(sim.Choose(0, sim.Player(0)!.Open!.Id, 0));
        sim.Step(); sim.ClearOutputs(); Assert.True(sim.Paused); // player 1 still choosing its job; player 0's card opened
        Assert.True(sim.Choose(1, sim.Player(1)!.Open!.Id, 0));
        sim.Step(); sim.ClearOutputs(); Assert.True(sim.Paused); // the cards are open now (a second window each, 20 s)
        Assert.False(sim.Player(1)!.Open!.IsJob);
        AnswerAll(sim);
        Assert.Equal(20f, sim.CurrentChoiceSeconds); Assert.Equal(1, sim.ChoiceLimitsMet);
        // level 6: nobody answers: 20 s, met → 10
        NextPause(sim, c);
        for (int i = 0; i < HordeSim.Ticks(20f) + 2 && sim.Paused; i++) { sim.Step(); sim.ClearOutputs(); }
        Assert.False(sim.Paused);
        Assert.Equal(10f, sim.CurrentChoiceSeconds); Assert.Equal(2, sim.ChoiceLimitsMet);
        _out.WriteLine($"limit after the rounds: {sim.CurrentChoiceSeconds} s, met {sim.ChoiceLimitsMet} times");
    }

    [Fact]
    public void The_party_shares_one_exp_bar_and_the_requirement_scales_with_the_players()
    {
        var one = Sim(1, out var c);
        var four = Sim(4, out _);
        Assert.Equal(one.ExpToNext(1), one.PartyExpToNext());
        Assert.Equal((int)Math.Round(one.ExpToNext(1) * c.Rules.PartyExpScale[3]), four.PartyExpToNext()); // h9content's measured 2.65 for four
        // the same pickups give the same pool to everyone: level-ups come for all at once, with one offer each
        var poring = c.Enemy(1)!;
        int offers = 0, guard = 0;
        while (four.PartyLevel < 3 && guard++ < 8000)
        {
            var p0 = four.Player(0)!;
            if (four.EnemiesAlive < 8) four.Spawn(poring, p0.X + 300, p0.Y, true);
            four.Step();
            offers += four.OffersOpened.Count;
            foreach (var p in four.Players) if (p.Present && p.Open != null) four.Choose(p.Slot, p.Open.Id, 0);
            four.ClearOutputs();
        }
        four.Step(); offers += four.OffersOpened.Count; // the level-up's offers open at the step after it
        Assert.True(four.Paused);
        Assert.Equal(3, four.PartyLevel);
        foreach (var p in four.Players) if (p.Present) Assert.Equal(3, p.Level);
        Assert.Equal(8, offers); // two level-ups × four players, one card offer each (level 5 is the first job mark)
        _out.WriteLine($"four players reached party level 3 at tick {four.Tick} with {offers} offers; pool {four.PartyExp}/{four.PartyExpToNext()}");
    }

    [Fact]
    public void The_pause_flag_and_the_party_bar_travel_in_the_snapshot()
    {
        var sim = Sim(2, out var c);
        var enc = new SnapshotEncoder(sim);
        var dec = new SnapshotDecoder(); var view = new HordeWorldView();
        dec.Reset(new HordeRunInfo { X0 = sim.Arena.X0, Y0 = sim.Arena.Y0, X1 = sim.Arena.X1, Y1 = sim.Arena.Y1 });
        var poring = c.Enemy(1)!;
        int guard = 0; double now = 0;
        bool sawPause = false;
        while (guard++ < 4000 && !sawPause)
        {
            var p0 = sim.Player(0)!;
            if (sim.EnemiesAlive < 6) sim.Spawn(poring, p0.X + 300, p0.Y, true);
            sim.Step(); enc.Collect(); sim.ClearOutputs();
            enc.Encode(guard == 1, out var delta, out var key);
            var body = guard == 1 ? key! : delta;
            now += 1.0 / HordeSim.TickRate;
            Assert.True(dec.Apply(body, 0, body.Length, view, 0, now));
            Assert.Equal(sim.PartyLevel, view.PartyLevel); Assert.Equal(sim.PartyExp, view.PartyExp); Assert.Equal(sim.PartyExpToNext(), view.PartyExpToNext);
            Assert.Equal(sim.PartyLevel, view.Players[1].Level);
            if (sim.Paused)
            {
                sawPause = true;
                Assert.True(view.Paused);
                Assert.Equal(2, view.PausedBy.Count);
                Assert.Equal(1f, view.Alpha(now + 0.5)); // nothing extrapolated while the world waits
            }
        }
        Assert.True(sawPause);
        foreach (var p in sim.Players) if (p.Present) sim.Choose(p.Slot, p.Open!.Id, 0);
        sim.Step(); enc.Collect(); sim.ClearOutputs();
        enc.Encode(false, out var d2, out _);
        now += 1.0 / HordeSim.TickRate;
        Assert.True(dec.Apply(d2, 0, d2.Length, view, 0, now));
        Assert.False(view.Paused); Assert.Empty(view.PausedBy);
    }

    /// <summary>Client wave 15 (w15hfx), point 19 of the design's wave 14 list: the Horde menu's Pause holds the world for everyone (the tick
    /// stands, inputs wait), a second pause does not change the holder, ANY player resumes it, the snapshot marks the holder (Held) and
    /// says paused; an open choice does not run out while the pause holds; a holder who leaves ends the pause.
    /// REWRITTEN client wave 18 (h18rules), the design's point 23: with another player in the run a pause comes after a 3 s countdown (the world
    /// runs meanwhile; the snapshot carries who asked and the seconds left), Pause again cancels it, and the resume counts down 3 s too (the
    /// world stands meanwhile; Resume again cancels). Everything wave 15 proved still holds once the countdown is over.</summary>
    [Fact]
    public void A_menu_pause_holds_the_world_for_everyone_and_anyone_resumes_it()
    {
        var sim = Sim(2, out var c);
        var enc = new SnapshotEncoder(sim);
        var dec = new SnapshotDecoder(); var view = new HordeWorldView();
        dec.Reset(new HordeRunInfo { X0 = sim.Arena.X0, Y0 = sim.Arena.Y0, X1 = sim.Arena.X1, Y1 = sim.Arena.Y1 });
        double now = 0; int n = 0;
        void Snap()
        {
            sim.Step(); enc.Collect(); sim.ClearOutputs();
            enc.Encode(n == 0, out var d, out var k);
            var body = n == 0 ? k! : d; n++; now += 1.0 / HordeSim.TickRate;
            Assert.True(dec.Apply(body, 0, body.Length, view, 0, now));
        }
        int count = HordeSim.Ticks(HordeSim.PauseCountSeconds);
        Assert.Equal(120, count);
        for (int i = 0; i < 5; i++) Snap();
        Assert.False(sim.Paused); Assert.Equal(-1, view.PauseCountBy);
        Assert.True(sim.PauseCountsDown(0)); // another player is in the run
        // Pause: a countdown, not a pause; the world runs during it; everyone sees who asked and the seconds left
        Assert.True(sim.Hold(0, true)); Assert.Equal(HordeHoldResult.PauseCounting, sim.LastHold);
        int t0 = sim.Tick;
        Snap();
        Assert.False(sim.Paused); Assert.Equal(-1, sim.HeldBy); Assert.Equal(0, view.PauseCountBy); Assert.True(view.PauseCountToPause);
        Assert.InRange(view.PauseCountSeconds, 2.9f, 3.0f);
        for (int i = 0; i < 40; i++) Snap();
        Assert.Equal(t0 + 41, sim.Tick); Assert.InRange(view.PauseCountSeconds, 1.9f, 2.0f);
        // Pause again: cancelled (by anyone: the other player's button reads Cancel too); nothing paused afterwards
        Assert.True(sim.Hold(1, true)); Assert.Equal(HordeHoldResult.Cancelled, sim.LastHold);
        for (int i = 0; i < count + 10; i++) Snap();
        Assert.False(sim.Paused); Assert.Equal(-1, sim.HeldBy); Assert.Equal(-1, view.PauseCountBy);
        // a resume during a pause's countdown cancels it too
        Assert.True(sim.Hold(0, true)); Assert.True(sim.Hold(1, false)); Assert.Equal(HordeHoldResult.Cancelled, sim.LastHold);
        Assert.Equal(-1, sim.PauseCountBy);
        // Pause, and the countdown runs out: the run pauses, its asker holds it
        Assert.True(sim.Hold(0, true));
        int wall0 = sim.WallTick;
        while (!sim.Paused && sim.WallTick < wall0 + count + 5) Snap();
        Assert.Equal(wall0 + count, sim.WallTick);
        Assert.True(sim.Paused); Assert.Equal(0, sim.HeldBy); Assert.Equal(-1, sim.PauseCountBy);
        Assert.False(sim.Hold(1, true)); // already held: the first holder stays
        Snap();
        Assert.True(view.Paused); Assert.Equal(new[] { 0 }, view.PausedBy.ToArray()); Assert.Equal(-1, view.PauseCountBy);
        Assert.True((view.Players[0].Flags & HordePlayerFlags.Held) != 0); Assert.True((view.Players[1].Flags & HordePlayerFlags.Held) == 0);
        int tick = sim.Tick; var p1 = sim.Player(1)!; int x1 = p1.X;
        for (int i = 0; i < 80; i++) { sim.PushInput(1, (ushort)(100 + i), 127, 0); Snap(); }
        Assert.Equal(tick, sim.Tick); Assert.Equal(x1, p1.X);
        // Resume (the other player: anyone may): a countdown too, the world stands during it; Resume again cancels it
        Assert.True(sim.Hold(1, false)); Assert.Equal(HordeHoldResult.ResumeCounting, sim.LastHold);
        for (int i = 0; i < 60; i++) Snap();
        Assert.True(sim.Paused); Assert.Equal(tick, sim.Tick); Assert.Equal(1, view.PauseCountBy); Assert.False(view.PauseCountToPause);
        Assert.InRange(view.PauseCountSeconds, 1.4f, 1.6f);
        Assert.True(sim.Hold(1, false)); Assert.Equal(HordeHoldResult.Cancelled, sim.LastHold);
        for (int i = 0; i < count + 10; i++) Snap();
        Assert.True(sim.Paused); Assert.Equal(0, sim.HeldBy); Assert.Equal(tick, sim.Tick);
        Assert.True(sim.Hold(1, false));
        wall0 = sim.WallTick;
        while (sim.Paused && sim.WallTick < wall0 + count + 5) Snap();
        Assert.Equal(wall0 + count, sim.WallTick);
        Assert.False(sim.Paused); Assert.Equal(-1, sim.HeldBy); Assert.False(view.Paused);
        Assert.True((view.Players[0].Flags & HordePlayerFlags.Held) == 0);
        Assert.False(sim.Hold(0, false)); // nothing to resume
        // an open choice waits while the pause holds: no auto-pick however long it lasts
        NextPause(sim, c);
        var open0 = sim.Player(0)!.Open!; Assert.NotNull(open0);
        Assert.True(sim.Hold(1, true));
        for (int i = 0; i < count; i++) { sim.Step(); sim.ClearOutputs(); }
        Assert.Equal(1, sim.HeldBy);
        int wall = sim.WallTick;
        for (int i = 0; i < (int)(c.Rules.ChoiceSeconds * HordeSim.TickRate) + 200; i++) { sim.Step(); sim.ClearOutputs(); }
        Assert.Same(open0, sim.Player(0)!.Open); Assert.Equal(1, sim.HeldBy);
        _out.WriteLine($"held {sim.WallTick - wall} wall ticks with a choice open (limit {c.Rules.ChoiceSeconds} s): still open");
        // a resume's countdown ends with its asker (its link gone); the holder leaves: the hold ends with it (the choice pause goes on
        // until the remaining chooser answers)
        Assert.True(sim.Hold(0, false)); Assert.Equal(0, sim.PauseCountBy);
        sim.SetAway(0, true); sim.Step(); sim.ClearOutputs();
        Assert.Equal(-1, sim.PauseCountBy); Assert.Equal(1, sim.HeldBy);
        sim.SetAway(0, false);
        sim.RemovePlayer(1);
        sim.Step(); sim.ClearOutputs();
        Assert.Equal(-1, sim.HeldBy);
        Assert.True(sim.Choose(0, open0.Id, 0));
        sim.Step(); sim.ClearOutputs();
        Assert.False(sim.Paused);
    }

    /// <summary>Client wave 18 (h18rules), the design's point 23: ALONE, the Horde menu pauses at once when it opens and resumes when it closes
    /// (the client sends pause / resume; no countdown, no Pause button). A player whose link is gone does not count: its partner is
    /// alone. A countdown left running when the other player went away gives way to the solo rule.</summary>
    [Fact]
    public void Alone_the_menu_pauses_and_resumes_at_once()
    {
        var sim = Sim(1, out _);
        for (int i = 0; i < 5; i++) { sim.Step(); sim.ClearOutputs(); }
        Assert.False(sim.PauseCountsDown(0));
        int tick = sim.Tick;
        Assert.True(sim.Hold(0, true)); Assert.Equal(HordeHoldResult.Paused, sim.LastHold); Assert.Equal(-1, sim.PauseCountBy);
        sim.Step(); sim.ClearOutputs();
        Assert.True(sim.Paused); Assert.Equal(0, sim.HeldBy); Assert.Equal(tick, sim.Tick);
        Assert.True(sim.Hold(0, false)); Assert.Equal(HordeHoldResult.Resumed, sim.LastHold);
        sim.Step(); sim.ClearOutputs();
        Assert.False(sim.Paused); Assert.Equal(tick + 1, sim.Tick);
        // two players, the second's link gone: the first is alone (at once); back: a countdown again
        var two = Sim(2, out _);
        for (int i = 0; i < 5; i++) { two.Step(); two.ClearOutputs(); }
        two.SetAway(1, true);
        Assert.False(two.PauseCountsDown(0));
        Assert.True(two.Hold(0, true)); Assert.Equal(HordeHoldResult.Paused, two.LastHold);
        Assert.True(two.Hold(0, false)); Assert.Equal(HordeHoldResult.Resumed, two.LastHold);
        two.SetAway(1, false);
        Assert.True(two.PauseCountsDown(0));
        Assert.True(two.Hold(0, true)); Assert.Equal(HordeHoldResult.PauseCounting, two.LastHold); Assert.Equal(0, two.PauseCountBy);
        two.SetAway(1, true);
        Assert.True(two.Hold(0, true)); Assert.Equal(HordeHoldResult.Paused, two.LastHold); Assert.Equal(-1, two.PauseCountBy); Assert.Equal(0, two.HeldBy);
    }
}
