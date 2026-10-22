using Loki.Horde;
using Loki.Horde.Content;
using Loki.Horde.Net;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>
/// Client wave 17 (h17boss), point 23 of the Horde design proposal: the bosses' telegraphed attacks (HordeSim.Bosses.cs). Each kind on a
/// still test boss with that one attack (HordeTestContent's tables, touches harmless), then the game's own tables: every boss and elite
/// casts each of its attacks, each warned 1-1.5 s before it strikes, deterministic, and the pull's client prediction equals the host.
/// </summary>
public class HordeBossTests
{
    readonly ITestOutputHelper _out;
    public HordeBossTests(ITestOutputHelper o) { _out = o; }

    const int U = HordeSim.U;

    /// <summary>The test tables, no waves, harmless touches, plus a still boss (id 77) with exactly these attacks (each due at once).</summary>
    static HordeSim Arena(out EnemyDef boss, params EnemyAbilityDef[] abs)
    {
        var c = HordeTestContent.Build();
        c.Waves.Clear();
        foreach (var e in c.Enemies) e.Damage = 0;
        foreach (var a in abs) a.FirstAfter = 0;
        boss = new EnemyDef { Id = 77, MobId = 1087, Key = "ORK_HERO", Rank = EnemyRank.Boss, Behavior = EnemyBehavior.Still, Hp = 100000, Damage = 0, Speed = 1f, Radius = 0.9f, KnockbackResist = 100, EffectResist = 100, Abilities = abs };
        c.Enemies.Add(boss);
        c.Enemies.Add(new EnemyDef { Id = 78, MobId = 1013, Key = "WOLF", Behavior = EnemyBehavior.Sweep, Hp = 50, Damage = 9, Speed = 7f, Radius = 0.45f });
        return new HordeSim(c, c.Arenas[0], 4242, 900);
    }

    /// <summary>A player at (cx + dx, cy + dy) cells from the arena's centre, hurtable at once, with lots of HP.</summary>
    static SimPlayer Put(HordeSim sim, uint aid, double dx, double dy)
    {
        int slot = sim.AddPlayer(aid, "p" + aid);
        var p = sim.Player(slot)!;
        p.X = (sim.MinX + sim.MaxX) / 2 + (int)Math.Round(dx * U); p.Y = (sim.MinY + sim.MaxY) / 2 + (int)Math.Round(dy * U);
        p.MaxHp = p.Hp = 5000; p.SafeUntil = 0;
        return p;
    }

    static int Centre(HordeSim sim, bool y) => y ? (sim.MinY + sim.MaxY) / 2 : (sim.MinX + sim.MaxX) / 2;

    /// <summary>Steps with every player standing (a zero stick each tick, as a client sends), collecting hits on players and the fx begun.</summary>
    static void Run(HordeSim sim, int ticks, List<HordeEvent> hits, List<SimFx>? begun = null, Action<HordeSim>? each = null)
    {
        for (int t = 0; t < ticks; t++)
        {
            foreach (var p in sim.Players) if (p.Present) sim.PushInput(p.Slot, (ushort)(sim.WallTick + 1), 0, 0);
            each?.Invoke(sim);
            sim.Step();
            foreach (var e in sim.Events) if (e.Kind == HordeEventKind.PlayerHit && (e.Flags & HordeEventFlags.Dodged) == 0) hits.Add(e);
            begun?.AddRange(sim.FxBegun);
            sim.ClearOutputs();
        }
    }

    // ------------------------------------------------------------------------------------------------ Line

    [Fact]
    public void A_dash_shows_its_lane_first_then_the_caster_dashes_along_it_and_hits_a_player_in_it_once()
    {
        var sim = Arena(out var def, new EnemyAbilityDef { Kind = AbilityKind.Line, Shape = AbilityShape.Dash, Cooldown = 60, Delay = 1.2f, Range = 10, Speed = 12, Damage = 22 });
        var inLane = Put(sim, 1, 5, 0); var aside = Put(sim, 2, 4, 3.5);
        int bx = Centre(sim, false), by = Centre(sim, true);
        int slot = sim.Spawn(def, bx, by, true);
        var hits = new List<HordeEvent>(); var begun = new List<SimFx>();
        Run(sim, 2, hits, begun);
        var lane = Assert.Single(begun, f => f.Kind == HordeFxKind.EnemyLine);
        Assert.Equal(48, lane.ArmTick - lane.StartTick);                          // 1.2 s of warning
        Assert.Equal(10 * U, (int)Math.Round(Math.Sqrt((double)lane.VX * lane.VX + (double)lane.VY * lane.VY)), 2.0);
        Assert.Equal((int)AbilityShape.Dash, lane.Level);
        Run(sim, lane.ArmTick - sim.Tick - 1, hits);
        Assert.Empty(hits);                                                        // nothing before the strike
        Assert.Equal(bx, sim.Enemies[slot].X);                                     // it stood while it aimed
        Run(sim, 40, hits);
        Assert.Single(hits, h => h.Slot == inLane.Slot && h.Value == 22);         // once, though it passed through
        Assert.DoesNotContain(hits, h => h.Slot == aside.Slot);
        Assert.InRange((sim.Enemies[slot].X - bx) / (double)U, 9.5, 10.5);        // 10 cells along the lane
        Assert.Equal(1, sim.BossStrikes[(int)AbilityKind.Line]);
    }

    [Fact]
    public void A_lane_strike_hits_only_who_stands_in_a_lane_and_parallel_lanes_leave_room_between()
    {
        var sim = Arena(out var def, new EnemyAbilityDef { Kind = AbilityKind.Line, Cooldown = 60, Delay = 1f, Range = 14, Width = 1.6f, Count = 2, Spacing = 4, Damage = 26 });
        var target = Put(sim, 1, 6, 0);        // the aim: between the two lanes (each 2 cells to a side)
        var onLane = Put(sim, 2, 9, 2);        // in the left lane (centre 2 cells north of the aim line)
        var outside = Put(sim, 3, 9, 4.2);     // north of it: 2.2 cells from the lane's centre > 0.8 + 0.4
        sim.Spawn(def, Centre(sim, false), Centre(sim, true), true);
        var hits = new List<HordeEvent>();
        Run(sim, 60, hits);
        Assert.Single(hits, h => h.Slot == onLane.Slot && h.Value == 26);
        Assert.DoesNotContain(hits, h => h.Slot == target.Slot);
        Assert.DoesNotContain(hits, h => h.Slot == outside.Slot);
    }

    [Fact]
    public void Runners_spawn_one_per_lane_and_run_along_it_across_the_arena()
    {
        var sim = Arena(out var def, new EnemyAbilityDef { Kind = AbilityKind.Line, Shape = AbilityShape.Runners, Cooldown = 60, Delay = 1.2f, Count = 3, Spacing = 2.5f, EnemyId = 78 });
        Put(sim, 1, 6, 0);
        sim.Spawn(def, Centre(sim, false), Centre(sim, true), true);
        var hits = new List<HordeEvent>();
        Run(sim, 49, hits);
        var runners = sim.Enemies.Where(e => e.Alive && e.Def.Id == 78).ToList();
        Assert.Equal(3, runners.Count);
        Assert.All(runners, r => Assert.True(r.DashVx > 0 && Math.Abs(r.DashVy) <= 1, $"runs east ({r.DashVx}, {r.DashVy})"));
        var ys = runners.Select(r => (r.Y - Centre(sim, true)) / (double)U).OrderBy(v => v).ToList();
        Assert.Equal(-2.5, ys[0], 1); Assert.Equal(0, ys[1], 1); Assert.Equal(2.5, ys[2], 1);
        Run(sim, HordeSim.TickRate * 10, hits);
        Assert.DoesNotContain(sim.Enemies, e => e.Alive && e.Def.Id == 78);     // gone at the arena's edge
    }

    // ------------------------------------------------------------------------------------------------ Cone

    [Fact]
    public void A_cone_hits_within_its_reach_and_slows_for_its_time()
    {
        var sim = Arena(out var def, new EnemyAbilityDef { Kind = AbilityKind.Cone, Cooldown = 60, Delay = 1.2f, Range = 6, Arc = 70, Damage = 16, SlowPct = 70, Duration = 1.5f });
        var aim = Put(sim, 1, 3, 0); var edge = Put(sim, 2, 4, 2.4); var behind = Put(sim, 3, -3, 0); var far = Put(sim, 4, 7, 0);
        int speed0 = aim.SpeedPerTick;
        sim.Spawn(def, Centre(sim, false), Centre(sim, true), true);
        var hits = new List<HordeEvent>();
        Run(sim, 50, hits);
        foreach (var p in new[] { aim, edge }) Assert.Single(hits, h => h.Slot == p.Slot && h.Value == 16);
        Assert.DoesNotContain(hits, h => h.Slot == behind.Slot || h.Slot == far.Slot);
        Assert.True(aim.SpeedPerTick < speed0 * 0.4, $"slowed: {aim.SpeedPerTick} of {speed0} units a tick");
        Assert.Equal(speed0, behind.SpeedPerTick);
        Run(sim, HordeSim.TickRate * 2, hits);
        Assert.Equal(speed0, aim.SpeedPerTick);                 // 1.5 s later it is gone
    }

    [Fact]
    public void A_fan_fires_its_shots_across_the_arc_at_the_strike()
    {
        var sim = Arena(out var def, new EnemyAbilityDef { Kind = AbilityKind.Cone, Shape = AbilityShape.Fan, Cooldown = 60, Delay = 1f, Range = 12, Arc = 70, Count = 7, Speed = 6, Damage = 14 });
        Put(sim, 1, 0, 6);
        sim.Spawn(def, Centre(sim, false), Centre(sim, true), true);
        var hits = new List<HordeEvent>(); var begun = new List<SimFx>();
        Run(sim, 45, hits, begun);
        var shots = begun.Where(f => f.Kind == HordeFxKind.EnemyShot).ToList();
        Assert.Equal(7, shots.Count);
        var angles = shots.Select(s => Math.Atan2(s.VY, s.VX) * 180 / Math.PI).OrderBy(a => a).ToList();
        Assert.InRange(angles[0], 90 - 35 - 1.5, 90 - 35 + 1.5); Assert.InRange(angles[6], 90 + 35 - 1.5, 90 + 35 + 1.5); // (integer velocities: within 1.5 degrees)
        Assert.Single(begun, f => f.Kind == HordeFxKind.EnemyCone && f.Count == 7);
    }

    // ------------------------------------------------------------------------------------------------ Ring

    [Fact]
    public void An_expanding_ring_hits_each_player_once_as_it_passes_except_in_its_gap()
    {
        var sim = Arena(out var def, new EnemyAbilityDef { Kind = AbilityKind.Ring, Cooldown = 60, Delay = 1f, Range = 9, Width = 0.8f, Speed = 7, Arc = 70, Damage = 20 });
        var a = Put(sim, 1, 4, 0); var b = Put(sim, 2, 0, 4); var c = Put(sim, 3, 10, 0);
        sim.Spawn(def, Centre(sim, false), Centre(sim, true), true);
        var hits = new List<HordeEvent>(); var begun = new List<SimFx>();
        Run(sim, 2, hits, begun);
        var ring = Assert.Single(begun, f => f.Kind == HordeFxKind.EnemyRing);
        Assert.Equal(70, ring.Count);
        // put a in the gap's middle and b opposite it, 4 cells out
        double g = ring.Angle * Math.PI / 180;
        a.X = ring.X + (int)(Math.Cos(g) * 4 * U); a.Y = ring.Y + (int)(Math.Sin(g) * 4 * U);
        b.X = ring.X - (int)(Math.Cos(g) * 4 * U); b.Y = ring.Y - (int)(Math.Sin(g) * 4 * U);
        Run(sim, 120, hits);
        Assert.DoesNotContain(hits, h => h.Slot == a.Slot);     // in the gap
        Assert.Single(hits, h => h.Slot == b.Slot && h.Value == 20);
        Assert.DoesNotContain(hits, h => h.Slot == c.Slot);     // beyond its 9 cells
        Assert.Equal(ring.ArmTick + (int)Math.Ceiling(9 * U / (double)HordeSim.Units(7.0 / HordeSim.TickRate)), ring.EndTick);
    }

    [Fact]
    public void An_inward_ring_sends_its_orbs_from_every_place_but_the_gap_to_the_centre()
    {
        var sim = Arena(out var def, new EnemyAbilityDef { Kind = AbilityKind.Ring, Shape = AbilityShape.Inward, Cooldown = 60, Delay = 1.2f, Range = 5, Count = 13, Speed = 3, Damage = 16 });
        var p = Put(sim, 1, 6, 0);
        sim.Spawn(def, Centre(sim, false), Centre(sim, true), true);
        var hits = new List<HordeEvent>(); var begun = new List<SimFx>();
        Run(sim, 49, hits, begun);                              // the strike's tick: the orbs are made, not moved yet
        var ring = Assert.Single(begun, f => f.Kind == HordeFxKind.EnemyRing);
        Assert.Equal(p.X, ring.X); Assert.Equal(p.Y, ring.Y);  // around the player
        var orbs = begun.Where(f => f.Kind == HordeFxKind.EnemyShot).ToList();
        Assert.Equal(12, orbs.Count);
        foreach (var o in orbs)
        {
            Assert.Equal(5 * U, Math.Sqrt(HordeSim.Dist2(o.X, o.Y, ring.X, ring.Y)), 2.0);
            Assert.True((long)o.VX * (ring.X - o.X) + (long)o.VY * (ring.Y - o.Y) > 0, "flies inward");
            double a = Math.Atan2(o.Y - ring.Y, o.X - ring.X) * 180 / Math.PI;
            double off = Math.Abs(((a - ring.Angle) % 360 + 540) % 360 - 180);
            Assert.True(off > 360.0 / 13 / 2, $"no orb in the gap ({off:F0}° from it)");
        }
    }

    // ------------------------------------------------------------------------------------------------ Pull

    [Fact]
    public void A_pull_drags_a_standing_player_toward_the_caster_but_walking_away_still_gains_ground()
    {
        var sim = Arena(out var def, new EnemyAbilityDef { Kind = AbilityKind.Pull, Cooldown = 60, Delay = 1f, Range = 8, Speed = 2.2f, Duration = 2 });
        var stand = Put(sim, 1, 6, 0); var walk = Put(sim, 2, -6, 0); var far = Put(sim, 3, 0, 9);
        sim.Spawn(def, Centre(sim, false), Centre(sim, true), true);
        int s0 = stand.X, w0 = walk.X, f0 = far.Y;
        var hits = new List<HordeEvent>();
        for (int t = 0; t < 41 + 80; t++)
        {
            sim.PushInput(stand.Slot, (ushort)(t + 1), 0, 0); sim.PushInput(walk.Slot, (ushort)(t + 1), t >= 40 ? -127 : 0, 0); sim.PushInput(far.Slot, (ushort)(t + 1), 0, 0);
            sim.Step(); sim.ClearOutputs();
        }
        double pulled = (s0 - stand.X) / (double)U, walked = (w0 - walk.X) / (double)U;
        _out.WriteLine($"standing: pulled {pulled:F2} cells in 2 s; walking away: {walked:F2} cells further");
        Assert.InRange(pulled, 4.0, 4.5);                       // 2.2 cells/s for 2 s
        Assert.True(walked > 2.5 && walked < 7.5, $"walking away at 4 cells/s still gains ground, less than without the pull ({walked:F2})");
        Assert.Equal(f0, far.Y);                                 // 9 cells: out of its 8
        Assert.Empty(hits);
    }

    [Fact]
    public void The_clients_pull_prediction_is_the_hosts_pull_through_the_wire()
    {
        // the host's pull on my walk, and the client's: the view's fx (decoded from real snapshots) through HordeSim.PullView, input by input
        var sim = Arena(out var def, new EnemyAbilityDef { Kind = AbilityKind.Pull, Cooldown = 60, Delay = 1f, Range = 8, Speed = 2.2f, Duration = 2 });
        var me = Put(sim, 1, 5.3, 1.7);
        sim.Spawn(def, Centre(sim, false), Centre(sim, true), true);
        var enc = new SnapshotEncoder(sim); var dec = new SnapshotDecoder(); var view = new HordeWorldView();
        dec.Reset(new HordeRunInfo { X0 = sim.Arena.X0, Y0 = sim.Arena.Y0, X1 = sim.Arena.X1, Y1 = sim.Arena.Y1 });
        int px = 0, py = 0, vx = 0, vy = 0; bool synced = false; int compared = 0, pulled = 0;
        for (int t = 0; t < 140; t++)
        {
            int dx = t % 50 < 25 ? 90 : -60, dy = t % 30 < 15 ? 40 : -100;
            sim.PushInput(me.Slot, (ushort)(t + 1), dx, dy);
            sim.Step(); enc.Collect(); sim.ClearOutputs();
            enc.Encode(t == 0, out var delta, out var key);
            var body = t == 0 ? key! : delta;
            Assert.True(dec.Apply(body, 0, body.Length, view, me.Slot, t * 0.025));
            if (!synced) { px = me.X; py = me.Y; vx = me.Vx; vy = me.Vy; synced = true; continue; }
            // the client's step for the input the host just applied at sim.Tick (the view holds the snapshot of that same tick)
            HordeMove.ClampStick(ref dx, ref dy);
            HordeMove.StepRamp(ref px, ref py, ref vx, ref vy, dx, dy, me.SpeedPerTick, me.AccelPerTick, me.DecelPerTick, HordeSim.Units(sim.R.WallSoftCells), sim.MinX, sim.MinY, sim.MaxX, sim.MaxY);
            int bx = px, by = py;
            HordeSim.PullView(view, sim.Tick, ref px, ref py, sim.MinX, sim.MinY, sim.MaxX, sim.MaxY);
            if (bx != px || by != py) pulled++;
            Assert.Equal(me.X, px); Assert.Equal(me.Y, py);
            compared++;
        }
        Assert.InRange(pulled, 60, 80);                                       // the pull was live (2 s = 80 ticks; fewer when I stood at its stop)
        _out.WriteLine($"{compared} inputs ({pulled} of them pulled): the client's place equals the host's on every one");
    }

    // ------------------------------------------------------------------------------------------------ Mark

    [Fact]
    public void A_mark_follows_its_player_then_stays_put_and_bursts_on_who_is_still_there()
    {
        var sim = Arena(out var def, new EnemyAbilityDef { Kind = AbilityKind.Mark, Aim = BossAim.Nearest, Cooldown = 60, Delay = 2, Duration = 1, Area = 2.5f, Damage = 30 });
        var marked = Put(sim, 1, 4, 0); var ally = Put(sim, 2, 12, 0);
        sim.Spawn(def, Centre(sim, false), Centre(sim, true), true);
        var hits = new List<HordeEvent>(); var begun = new List<SimFx>();
        // the marked player walks 2 s toward its ally (the mark follows), then out of it in the locked second; the ally stays
        for (int t = 0; t < 140; t++)
        {
            int dx = t < 60 ? 127 : t < 80 ? 0 : t < 120 ? -127 : 0;
            sim.PushInput(marked.Slot, (ushort)(t + 1), dx, 0); sim.PushInput(ally.Slot, (ushort)(t + 1), 0, 0);
            sim.Step();
            foreach (var e in sim.Events) if (e.Kind == HordeEventKind.PlayerHit) hits.Add(e);
            begun.AddRange(sim.FxBegun); sim.ClearOutputs();
        }
        var marks = begun.Where(f => f.Kind == HordeFxKind.EnemyMark).ToList();
        Assert.Equal(2, marks.Count);
        Assert.Equal(marked.Slot, marks[0].Target);
        Assert.Equal(-1, marks[1].Target);
        Assert.Equal(80, marks[0].ArmTick - marks[0].StartTick);      // follows 2 s
        Assert.Equal(40, marks[1].ArmTick - marks[1].StartTick);      // then stays 1 s
        Assert.DoesNotContain(hits, h => h.Slot == marked.Slot);      // it stepped out in time
        Assert.Single(hits, h => h.Slot == ally.Slot && h.Value == 30); // the ally it was carried to did not
        _out.WriteLine($"locked at {(marks[1].X - Centre(sim, false)) / (double)U:F1} cells; ally at 12; hits: {string.Join(", ", hits.Select(h => h.Slot))}");
    }

    // ------------------------------------------------------------------------------------------------ Grid

    [Fact]
    public void A_checker_grid_strikes_its_even_squares_then_its_odd_ones_and_twice_as_fast_in_the_second_phase()
    {
        var sim = Arena(out var def,
            new EnemyAbilityDef { Kind = AbilityKind.Grid, Shape = AbilityShape.Checker, Aim = BossAim.Nearest, Cooldown = 60, Delay = 1.3f, Width = 2, Count = 7, Duration = 1, Damage = 34, PhaseCooldownPct = 50 },
            new EnemyAbilityDef { Kind = AbilityKind.Phase, Cooldown = 1, HpBelow = 50 });
        var centre = Put(sim, 1, 3, 0);
        var odd = Put(sim, 2, 5, 0);        // one square east of the player's (the centre square is even)
        var outside = Put(sim, 3, 3, 8);    // 8 cells north: beyond the 7 squares' 7 cells
        int slot = sim.Spawn(def, Centre(sim, false), Centre(sim, true), true);
        var hits = new List<HordeEvent>(); var begun = new List<SimFx>();
        Run(sim, 2, hits, begun);
        var grid = Assert.Single(begun, f => f.Kind == HordeFxKind.EnemyGrid);
        Assert.Equal(centre.X, grid.X);
        Assert.Equal(52, grid.ArmTick - grid.StartTick); Assert.Equal(40, grid.EndTick - grid.ArmTick);
        Run(sim, grid.ArmTick - sim.Tick, hits);
        Assert.Single(hits); Assert.Equal(centre.Slot, hits[0].Slot);
        Run(sim, grid.EndTick - sim.Tick, hits);
        Assert.Equal(2, hits.Count); Assert.Equal(odd.Slot, hits[1].Slot);
        Assert.DoesNotContain(hits, h => h.Slot == outside.Slot);
        // the second phase: its HP under half: the phase event once, the next grid comes at once with its halves 0.5 s apart
        var boss = sim.Enemies[slot]; boss.Hp = boss.MaxHp * 49 / 100;
        var phases = new List<HordeEvent>(); begun.Clear();
        for (int t = 0; t < HordeSim.TickRate * 40 && !begun.Any(f => f.Kind == HordeFxKind.EnemyGrid); t++)
        {
            foreach (var p in sim.Players) if (p.Present) sim.PushInput(p.Slot, (ushort)(sim.WallTick + 1), 0, 0);
            sim.Step(); phases.AddRange(sim.Events.Where(e => e.Kind == HordeEventKind.BossPhase)); begun.AddRange(sim.FxBegun); sim.ClearOutputs();
        }
        Assert.Single(phases);
        var g2 = Assert.Single(begun, f => f.Kind == HordeFxKind.EnemyGrid);
        Assert.Equal(20, g2.EndTick - g2.ArmTick);
        _out.WriteLine($"second phase at tick {phases[0].Tick}, the next grid at {g2.StartTick} (cooldown 60 s halved, the wait halved at once)");
    }

    [Fact]
    public void A_cross_strikes_the_row_and_column_through_its_player_and_rain_falls_as_round_warnings()
    {
        var sim = Arena(out var def,
            new EnemyAbilityDef { Kind = AbilityKind.Grid, Shape = AbilityShape.Cross, Aim = BossAim.Nearest, Cooldown = 60, Delay = 1.3f, Width = 1.5f, Count = 9, Damage = 28 });
        var p = Put(sim, 1, 4, 0); var onRow = Put(sim, 2, 9, 0); var diag = Put(sim, 3, 6, 2);
        sim.Spawn(def, Centre(sim, false), Centre(sim, true), true);
        var hits = new List<HordeEvent>();
        Run(sim, 60, hits);
        Assert.Contains(hits, h => h.Slot == p.Slot); Assert.Contains(hits, h => h.Slot == onRow.Slot);
        Assert.DoesNotContain(hits, h => h.Slot == diag.Slot);

        var rain = Arena(out var rdef, new EnemyAbilityDef { Kind = AbilityKind.Grid, Shape = AbilityShape.Rain, Aim = BossAim.Nearest, Cooldown = 60, Delay = 1.1f, Count = 7, Area = 1, Spacing = 4, Duration = 0.15f, Damage = 12 });
        Put(rain, 1, 5, 0);
        rain.Spawn(rdef, Centre(rain, false), Centre(rain, true), true);
        var begun = new List<SimFx>();
        Run(rain, 2, new List<HordeEvent>(), begun);
        var drops = begun.Where(f => f.Kind == HordeFxKind.EnemyWarning).OrderBy(f => f.EndTick).ToList();
        Assert.Equal(7, drops.Count);
        Assert.Equal(44, drops[0].EndTick - drops[0].StartTick);
        Assert.Equal(6 * 6, drops[6].EndTick - drops[0].EndTick);
    }

    // ------------------------------------------------------------------------------------------------ Teleport

    [Fact]
    public void A_jump_lands_where_the_player_stood_and_a_teleport_lands_its_width_from_the_player()
    {
        var sim = Arena(out var def, new EnemyAbilityDef { Kind = AbilityKind.Teleport, Shape = AbilityShape.Jump, Aim = BossAim.Nearest, Cooldown = 60, Delay = 1f, Area = 1.5f, Damage = 16 });
        var p = Put(sim, 1, 6, 0);
        int slot = sim.Spawn(def, Centre(sim, false), Centre(sim, true), true);
        var hits = new List<HordeEvent>(); var begun = new List<SimFx>();
        // the player steps away after the shadow appears: the boss lands on the empty spot
        for (int t = 0; t < 45; t++) { sim.PushInput(p.Slot, (ushort)(t + 1), t > 2 ? 127 : 0, 0); sim.Step(); foreach (var e in sim.Events) if (e.Kind == HordeEventKind.PlayerHit) hits.Add(e); begun.AddRange(sim.FxBegun); sim.ClearOutputs(); }
        var jump = Assert.Single(begun, f => f.Kind == HordeFxKind.EnemyTeleport);
        Assert.Equal(Centre(sim, false) + 6 * U, jump.X);
        Assert.Equal(jump.X, sim.Enemies[slot].X);
        Assert.Empty(hits);
        // staying under it is a hit
        var sim2 = Arena(out var def2, new EnemyAbilityDef { Kind = AbilityKind.Teleport, Shape = AbilityShape.Jump, Aim = BossAim.Nearest, Cooldown = 60, Delay = 1f, Area = 1.5f, Damage = 16 });
        var q = Put(sim2, 1, 6, 0); sim2.Spawn(def2, Centre(sim2, false), Centre(sim2, true), true);
        var hits2 = new List<HordeEvent>(); Run(sim2, 45, hits2);
        Assert.Single(hits2, h => h.Slot == q.Slot && h.Value == 16);
        // a teleport: 3 cells from the player, on the arena's centre side
        var sim3 = Arena(out var def3, new EnemyAbilityDef { Kind = AbilityKind.Teleport, Aim = BossAim.Farthest, Cooldown = 60, Delay = 1f, Width = 3 });
        var near = Put(sim3, 1, -2, 0); var farP = Put(sim3, 2, 20, 0);
        int s3 = sim3.Spawn(def3, Centre(sim3, false), Centre(sim3, true), true);
        Run(sim3, 45, new List<HordeEvent>());
        Assert.Equal(17.0, (sim3.Enemies[s3].X - Centre(sim3, false)) / (double)U, 1);
    }

    // ------------------------------------------------------------------------------------------------ Trail, the turn

    [Fact]
    public void A_trail_slows_who_walks_in_it_and_fades()
    {
        var c = HordeTestContent.Build(); c.Waves.Clear(); foreach (var e in c.Enemies) e.Damage = 0;
        var walker = new EnemyDef { Id = 77, MobId = 1038, Key = "OSIRIS", Rank = EnemyRank.Boss, Behavior = EnemyBehavior.Chase, Hp = 100000, Speed = 2f, Radius = 0.8f, KnockbackResist = 100, EffectResist = 100,
            Abilities = new[] { new EnemyAbilityDef { Kind = AbilityKind.Trail, Cooldown = 60, FirstAfter = 0, Duration = 6, Area = 1.2f, Linger = 6, SlowPct = 40, SkillId = 92 } } };
        c.Enemies.Add(walker);
        var sim = new HordeSim(c, c.Arenas[0], 9, 900);
        var p = Put(sim, 1, 10, 0);
        sim.Spawn(walker, Centre(sim, false), Centre(sim, true), true);
        var begun = new List<SimFx>(); var hits = new List<HordeEvent>();
        Run(sim, HordeSim.TickRate * 3, hits, begun);
        var zones = begun.Where(f => f.Kind == HordeFxKind.EnemyTrail).ToList();
        Assert.InRange(zones.Count, 4, 7);                      // every 0.5 s while it walks
        Assert.All(zones, z => { Assert.Equal(40, z.Count); Assert.Equal(240, z.EndTick - z.StartTick); });
        int speed0 = p.SpeedPerTick;
        p.X = zones[1].X; p.Y = zones[1].Y;                       // into a patch
        Run(sim, 2, hits);
        Assert.True(p.SpeedPerTick < speed0 * 0.7, $"40 % slower inside: {p.SpeedPerTick} of {speed0}");
        p.X += 8 * U; Run(sim, 20, hits);
        Assert.Equal(speed0, p.SpeedPerTick);
        Assert.Empty(hits);
    }

    [Fact]
    public void A_caster_shows_one_warning_at_a_time()
    {
        var sim = Arena(out var def,
            new EnemyAbilityDef { Kind = AbilityKind.Cone, Cooldown = 3, Delay = 1.2f, Range = 4, Arc = 90, Damage = 5 },
            new EnemyAbilityDef { Kind = AbilityKind.Line, Cooldown = 3, Delay = 1.2f, Range = 10, Width = 1, Damage = 5 },
            new EnemyAbilityDef { Kind = AbilityKind.Nova, Cooldown = 3, Delay = 1.2f, Area = 3, Damage = 5 });
        Put(sim, 1, 3, 0);
        sim.Spawn(def, Centre(sim, false), Centre(sim, true), true);
        var begun = new List<SimFx>();
        Run(sim, HordeSim.TickRate * 20, new List<HordeEvent>(), begun);
        var warnings = begun.Where(f => f.Kind == HordeFxKind.EnemyCone || f.Kind == HordeFxKind.EnemyLine || f.Kind == HordeFxKind.EnemyWarning).OrderBy(f => f.StartTick).ToList();
        Assert.True(warnings.Count >= 6, $"{warnings.Count} warnings in 20 s");
        for (int i = 1; i < warnings.Count; i++)
            Assert.True(warnings[i].StartTick >= warnings[i - 1].EndTick + HordeSim.Ticks(HordeSim.BossGapSeconds),
                $"warning {i} ({warnings[i].Kind}) began at {warnings[i].StartTick}, the one before struck at {warnings[i - 1].EndTick}");
        Assert.Contains(warnings, f => f.Kind == HordeFxKind.EnemyCone); Assert.Contains(warnings, f => f.Kind == HordeFxKind.EnemyLine); Assert.Contains(warnings, f => f.Kind == HordeFxKind.EnemyWarning);
    }

    // ------------------------------------------------------------------------------------------------ the game's own tables

    [Fact]
    public void Every_boss_and_elite_of_the_tables_uses_each_of_its_attacks_and_warns_one_to_one_and_a_half_seconds_first()
    {
        var c = HordeTables.Build();
        var casters = c.Enemies.Where(e => e.Rank >= EnemyRank.Elite).ToList();
        Assert.Equal(9 + 7, casters.Count);                        // Eclipse, Vagabond Wolf, Ork Hero, Osiris, the four new ones, Baphomet; client wave 18 (h18rules): + the 7 elites of the deal (HordeElites.cs), each held to the same rule below
        foreach (var def in casters)
        {
            var sim = new HordeSim(c, c.Arenas[0], 1234, 900);
            var me = Put(sim, 1, 0, 0); var ally = Put(sim, 2, 3, 3);
            int slot = sim.Spawn(def, me.X + 6 * U, me.Y, true);
            var begun = new List<SimFx>(); // (the tables' waves stay: a run's normal background)
            for (int t = 0; t < HordeSim.TickRate * 90 && sim.Enemies[slot].Alive; t++)
            {
                if (t == HordeSim.TickRate * 45) { var b = sim.Enemies[slot]; b.Hp = b.MaxHp * 45 / 100; } // into a second phase, if it has one
                foreach (var p in sim.Players) if (p.Present) { p.Hp = p.MaxHp; sim.PushInput(p.Slot, (ushort)(t + 1), (int)(Math.Cos(t * 0.02) * 127), (int)(Math.Sin(t * 0.02) * 127)); if (p.Open != null) sim.Choose(p.Slot, p.Open.Id, 0); }
                sim.Step(); begun.AddRange(sim.FxBegun); sim.ClearOutputs();
            }
            var own = def.Abilities.Select(a => a.Kind).Where(HordeSim.IsBossKind).Distinct().ToList();
            _out.WriteLine($"{def.Key}: " + string.Join(", ", own.Select(k => $"{k} {sim.BossCasts[(int)k]}/{sim.BossStrikes[(int)k]}")));
            foreach (var k in own) Assert.True(sim.BossCasts[(int)k] > 0, $"{def.Key} used its {k}");
            foreach (var f in begun.Where(f => HordeSim.IsBossFx(f.Kind) && f.Kind != HordeFxKind.EnemyTrail && f.Kind != HordeFxKind.EnemyMark))
                Assert.InRange(f.ArmTick - f.StartTick, HordeSim.TickRate, HordeSim.TickRate * 3 / 2);
            foreach (var f in begun.Where(f => f.Kind == HordeFxKind.EnemyMark && f.Target >= 0)) Assert.InRange(f.ArmTick - f.StartTick, HordeSim.TickRate, HordeSim.TickRate * 3);
        }
    }

    [Fact]
    public void A_boss_fight_is_deterministic_tick_for_tick()
    {
        var c = HordeTables.Build();
        foreach (var key in new[] { "ORK_HERO", "OSIRIS", "BAPHOMET", "MISTRESS", "EDDGA", "MAYA", "MOONLIGHT" })
        {
            var def = c.Enemies.First(e => e.Key == key);
            List<ulong> One()
            {
                var sim = new HordeSim(c, c.Arenas[0], 77, 900);
                var h = new List<ulong>();
                Put(sim, 1, 0, 0); Put(sim, 2, -4, 2);
                sim.Spawn(def, Centre(sim, false) + 5 * U, Centre(sim, true), true);
                for (int t = 0; t < HordeSim.TickRate * 30; t++)
                {
                    foreach (var p in sim.Players) if (p.Present) { p.Hp = p.MaxHp; sim.PushInput(p.Slot, (ushort)(t + 1), (int)(Math.Cos(t * 0.03 + p.Slot) * 127), (int)(Math.Sin(t * 0.03) * 127)); }
                    sim.Step(); sim.ClearOutputs(); h.Add(sim.StateHash());
                }
                return h;
            }
            Assert.Equal(One(), One());
        }
    }

    [Fact]
    public void The_new_kinds_travel_on_the_existing_fx_and_event_records_unchanged()
    {
        var c = HordeTables.Build();
        var sim = new HordeSim(c, c.Arenas[0], 3, 900);
        var me = Put(sim, 1, 0, 0);
        var enc = new SnapshotEncoder(sim); var dec = new SnapshotDecoder(); var view = new HordeWorldView();
        dec.Reset(new HordeRunInfo { X0 = sim.Arena.X0, Y0 = sim.Arena.Y0, X1 = sim.Arena.X1, Y1 = sim.Arena.Y1 });
        foreach (var key in new[] { "ORK_HERO", "OSIRIS", "BAPHOMET", "EDDGA", "MOONLIGHT" }) sim.Spawn(c.Enemies.First(e => e.Key == key), me.X + 6 * U, me.Y + 2 * U, true);
        var kinds = new HashSet<HordeFxKind>(); var events = new HashSet<HordeEventKind>(); int compared = 0;
        for (int t = 0; t < HordeSim.TickRate * 40; t++)
        {
            me.Hp = me.MaxHp;
            sim.PushInput(me.Slot, (ushort)(t + 1), 0, 0);
            var begun = new List<SimFx>();
            sim.Step(); enc.Collect(); begun.AddRange(sim.FxBegun);
            foreach (var e in sim.Events) events.Add(e.Kind);
            sim.ClearOutputs();
            enc.Encode(t == 0, out var delta, out var keyBody);
            var body = t == 0 ? keyBody! : delta;
            Assert.True(dec.Apply(body, 0, body.Length, view, me.Slot, t * 0.025));
            foreach (var f in begun.Where(f => HordeSim.IsBossFx(f.Kind)))
            {
                var v = view.FxOf(f.Id); Assert.NotNull(v);
                kinds.Add(f.Kind);
                Assert.Equal(f.X / (float)U, v!.X, 3); Assert.Equal(f.Y / (float)U, v.Y, 3);
                Assert.Equal(f.VX, (int)Math.Round(v.VX * U / HordeSim.TickRate));   // a vector in units: VX / TickRate in cells
                Assert.Equal(f.VY, (int)Math.Round(v.VY * U / HordeSim.TickRate));
                Assert.Equal(f.Radius, (int)Math.Round(v.Radius * U)); Assert.Equal(f.Range, (int)Math.Round(v.Range * U));
                Assert.Equal((int)Math.Round(f.Angle), (int)v.Angle); Assert.Equal(f.Count, v.Count); Assert.Equal(f.Level, v.Level);
                Assert.Equal(f.ArmTick, v.ArmTick); Assert.Equal(f.EndTick, v.EndTick); Assert.Equal(f.Target, v.Target);
                compared++;
            }
            foreach (var e in dec.Events.Where(e => e.Kind == HordeEventKind.BossStrike)) Assert.True(e.Value >= (int)AbilityKind.Line && e.Value <= (int)AbilityKind.Trail);
        }
        _out.WriteLine($"{compared} warnings compared; kinds {string.Join(", ", kinds)}; events {string.Join(", ", events.Where(k => k >= HordeEventKind.BossPhase))}");
        Assert.True(kinds.Count >= 6, $"kinds seen: {string.Join(", ", kinds)}");
        Assert.Contains(HordeEventKind.BossStrike, events);
        Assert.Equal(4, HordeWire.Version); // (client wave 18, h18feel: 3 -> 4, the souls' block)
    }
}
