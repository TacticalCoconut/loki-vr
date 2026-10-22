using Loki.Horde;
using Loki.Horde.Net;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>
/// Client wave 17 (h17read): the engine's part of the readability proposal (point 28; HordeSim.Readability.cs). Every hit on a player
/// names its attacker and its place; a shooter aims 0.3 s (Winding, a WindUp event naming its target) and then fires at that player; a
/// charger keeps the direction its wind-up began with and says it (the WindUp event with the dash's end). Each test also sends the
/// events through the snapshot (SnapshotEncoder → SnapshotDecoder) and checks what a client reads.
/// </summary>
public class HordeW17ReadTests
{
    readonly ITestOutputHelper _out;
    public HordeW17ReadTests(ITestOutputHelper o) { _out = o; }

    const int ArcherId = 30, WolfId = 31, CasterId = 32, BruteId = 33;

    /// <summary>The engine tests' tables plus three test enemies: a shooter, a charger, a still caster of an area (test values).</summary>
    static HordeContent Content()
    {
        var c = HordeTestContent.Build();
        c.Enemies.Add(new EnemyDef { Id = ArcherId, MobId = 1016, Key = "ARCHER_SKELETON", Behavior = EnemyBehavior.Ranged, Hp = 5000, Damage = 0, Speed = 1.6f,
            ShotDamage = 6, ShotRange = 6f, ShotSpeed = 7f, AttackInterval = 2f, Radius = 0.45f, Exp = 1 });
        c.Enemies.Add(new EnemyDef { Id = WolfId, MobId = 1013, Key = "WOLF", Behavior = EnemyBehavior.Charge, Hp = 5000, Damage = 0, Speed = 0.5f,
            ChargeCooldown = 3f, ChargeWindup = 0.7f, ChargeSpeed = 8f, Radius = 0.45f, Exp = 1 });
        c.Enemies.Add(new EnemyDef { Id = CasterId, MobId = 1038, Key = "OSIRIS", Rank = EnemyRank.Boss, Behavior = EnemyBehavior.Still, Hp = 50000, Damage = 0, Radius = 0.8f,
            KnockbackResist = 100, EffectResist = 100, Exp = 0,
            Abilities = new[] { new EnemyAbilityDef { Kind = AbilityKind.AreaAtPlayers, Cooldown = 4f, FirstAfter = 2f, Damage = 10, Area = 1.5f, Delay = 1f, Count = 1 } } });
        c.Enemies.Add(new EnemyDef { Id = BruteId, MobId = 1023, Key = "ORK_WARRIOR", Behavior = EnemyBehavior.Chase, Hp = 5000, Damage = 5, Speed = 1f, Radius = 0.5f, Exp = 1 });
        c.Waves.Clear(); // only the enemies a test spawns
        return c;
    }

    /// <summary>A host and one client's view: each Step runs the engine one tick, sends the snapshot and decodes it.</summary>
    sealed class Rig
    {
        public readonly HordeContent C = Content();
        public readonly HordeSim Sim;
        readonly SnapshotEncoder _enc; readonly SnapshotDecoder _dec = new SnapshotDecoder();
        public readonly HordeWorldView View = new HordeWorldView();
        public readonly List<HordeEvent> SimEvents = new List<HordeEvent>(), WireEvents = new List<HordeEvent>();
        public readonly List<SimFx> FxBegun = new List<SimFx>();
        int _n; double _now;
        public Rig(ulong seed = 7)
        {
            Sim = new HordeSim(C, C.Arenas[0], seed, 900);
            Sim.AddPlayer(1, "me");
            _enc = new SnapshotEncoder(Sim);
            _dec.Reset(new HordeRunInfo { X0 = Sim.Arena.X0, Y0 = Sim.Arena.Y0, X1 = Sim.Arena.X1, Y1 = Sim.Arena.Y1 });
        }
        public void Step()
        {
            Sim.Step();
            SimEvents.AddRange(Sim.Events); FxBegun.AddRange(Sim.FxBegun);
            _enc.Collect(); Sim.ClearOutputs();
            _enc.Encode(_n == 0, out var d, out var k);
            var body = _n == 0 ? k! : d; _n++; _now += 1.0 / HordeSim.TickRate;
            Assert.True(_dec.Apply(body, 0, body.Length, View, 0, _now));
            WireEvents.AddRange(_dec.Events);
        }
        public SimEnemy Spawn(int id, double dxCells, double dyCells)
        {
            var p = Sim.Player(0)!;
            int slot = Sim.Spawn(C.Enemy(id)!, p.X + (int)Math.Round(dxCells * HordeSim.U), p.Y + (int)Math.Round(dyCells * HordeSim.U), true);
            return Sim.Enemies[slot];
        }
    }

    static void SameOnTheWire(HordeEvent s, HordeEvent w)
    {
        Assert.Equal(s.Kind, w.Kind); Assert.Equal(s.Slot, w.Slot); Assert.Equal(s.Target, w.Target); Assert.Equal(s.DefId, w.DefId); Assert.Equal(s.Value, w.Value);
        Assert.Equal(s.X, w.X, 2); Assert.Equal(s.Y, w.Y, 2); Assert.Equal(s.X2, w.X2, 2); Assert.Equal(s.Y2, w.Y2, 2);
        Assert.True(Math.Abs(s.A - w.A) <= 0.051f && Math.Abs(s.B - w.B) <= 0.051f && Math.Abs(s.C - w.C) <= 0.051f, $"A/B/C {s.A}/{s.B}/{s.C} sent, {w.A}/{w.B}/{w.C} read");
    }

    [Fact]
    public void A_touch_names_the_enemy_and_its_place_and_the_client_reads_it()
    {
        var r = new Rig();
        var poring = r.Spawn(BruteId, 0.6, 0.3); // a chaser that touches (Damage 5); the first 2 s after joining nobody is hurt (ReviveSafeSeconds)
        HordeEvent hit = default; bool got = false; int ex = 0, ey = 0;
        for (int i = 0; i < HordeSim.TickRate * 4 && !got; i++)
        {
            r.Step();
            foreach (var e in r.SimEvents) if (e.Kind == HordeEventKind.PlayerHit && !got) { hit = e; got = true; ex = poring.X; ey = poring.Y; }
        }
        Assert.True(got, "no hit");
        var me = r.Sim.Player(0)!;
        Assert.Equal(poring.Slot, hit.Target);
        Assert.Equal((float)HordeHitVia.Touch, hit.A);
        Assert.Equal(HordeSim.Cells(ex), hit.X2, 3); Assert.Equal(HordeSim.Cells(ey), hit.Y2, 3); // where the poring stood when it touched
        Assert.Equal(HordeSim.Cells(me.X), hit.X, 3); // X / Y stay the victim's
        var w = r.WireEvents.First(e => e.Kind == HordeEventKind.PlayerHit);
        SameOnTheWire(hit, w);
        _out.WriteLine($"touch: me {hit.X:0.00},{hit.Y:0.00}, attacker slot {w.Target} at {w.X2:0.00},{w.Y2:0.00}, via {(HordeHitVia)(int)w.A}, {w.Value} damage");
    }

    [Fact]
    public void A_shooter_aims_for_0_3_s_standing_then_fires_at_the_aimed_player_and_the_hit_says_where_it_was_fired_from()
    {
        var r = new Rig();
        var archer = r.Spawn(ArcherId, 4, 0); // within its 6-cell range from the start
        int aimTick = -1, fireTick = -1; int ax = 0, ay = 0; bool movedWhileAiming = false, windingAll = true;
        HordeEvent wind = default;
        for (int i = 0; i < HordeSim.TickRate * 4 && fireTick < 0; i++)
        {
            int x0 = archer.X, y0 = archer.Y;
            r.Step();
            foreach (var e in r.SimEvents) if (e.Kind == HordeEventKind.WindUp && aimTick < 0) { wind = e; aimTick = r.Sim.Tick; ax = archer.X; ay = archer.Y; }
            r.SimEvents.Clear();
            if (aimTick >= 0 && r.FxBegun.Any(f => f.Kind == HordeFxKind.EnemyShot)) fireTick = r.Sim.Tick;
            else if (aimTick >= 0)
            {
                if (r.Sim.Tick > aimTick && (archer.X != x0 || archer.Y != y0)) movedWhileAiming = true;
                if ((archer.Flags & HordeEnemyFlags.Winding) == 0) windingAll = false;
            }
        }
        Assert.True(aimTick > 0 && fireTick > 0, $"aim {aimTick}, fire {fireTick}");
        Assert.Equal(HordeSim.AimTicks, fireTick - aimTick); // 0.3 s = 12 ticks of aim, then the shot
        Assert.Equal(12, HordeSim.AimTicks);
        Assert.False(movedWhileAiming); Assert.True(windingAll);
        var me = r.Sim.Player(0)!;
        Assert.Equal(0, wind.Slot); Assert.Equal(archer.Slot, wind.Target); Assert.Equal(ArcherId, wind.DefId); Assert.Equal(HordeSim.AimTicks, wind.Value);
        Assert.Equal((float)HordeWindUpKind.Shot, wind.B);
        Assert.Equal(HordeSim.Cells(me.X), wind.X2, 3); Assert.Equal(HordeSim.Cells(ax), wind.X, 3);
        Assert.True(Math.Abs(wind.A - 180f) < 1f || Math.Abs(wind.A + 180f) < 1f, $"aimed west at me: {wind.A}°");
        Assert.Equal(4f, wind.C, 1);
        var shot = r.FxBegun.First(f => f.Kind == HordeFxKind.EnemyShot);
        double shotDeg = Math.Atan2(shot.VY, shot.VX) * 180 / Math.PI, toMe = Math.Atan2(me.Y - shot.Y, me.X - shot.X) * 180 / Math.PI;
        Assert.True(Math.Abs(Math.IEEERemainder(shotDeg - toMe, 360)) < 1.0, $"shot {shotDeg:0.0}°, to me {toMe:0.0}°");
        SameOnTheWire(wind, r.WireEvents.First(e => e.Kind == HordeEventKind.WindUp));
        // the view: the archer flagged Winding while it aims (C1's "!")
        // the hit: from where it was fired (the archer's place at the shot)
        HordeEvent hit = default; bool got = false;
        for (int i = 0; i < HordeSim.TickRate * 2 && !got; i++) { r.Step(); foreach (var e in r.SimEvents) if (e.Kind == HordeEventKind.PlayerHit) { hit = e; got = true; } r.SimEvents.Clear(); }
        Assert.True(got);
        Assert.Equal((float)HordeHitVia.Shot, hit.A); Assert.Equal(archer.Slot, hit.Target); Assert.Equal(ArcherId, hit.DefId);
        Assert.Equal(HordeSim.Cells(ax), hit.X2, 2); Assert.Equal(HordeSim.Cells(ay), hit.Y2, 2);
        SameOnTheWire(hit, r.WireEvents.First(e => e.Kind == HordeEventKind.PlayerHit));
        // the rhythm is the table's: the next aim begins AttackInterval − 0.3 s after the shot, the next shot AttackInterval after it
        int aim2 = -1, fire2 = -1; r.FxBegun.Clear();
        for (int i = 0; i < HordeSim.TickRate * 4 && fire2 < 0; i++)
        {
            r.Step();
            foreach (var e in r.SimEvents) if (e.Kind == HordeEventKind.WindUp && aim2 < 0) aim2 = r.Sim.Tick;
            r.SimEvents.Clear();
            if (r.FxBegun.Any(f => f.Kind == HordeFxKind.EnemyShot)) fire2 = r.Sim.Tick;
        }
        Assert.Equal(fireTick + HordeSim.Ticks(2.0), fire2);
        Assert.Equal(fire2 - HordeSim.AimTicks, aim2);
        _out.WriteLine($"aim at tick {aimTick} ({wind.A:0}°, {wind.C:0.0} cells), shot at {fireTick}, hit from {hit.X2:0.00},{hit.Y2:0.00}; next aim {aim2}, next shot {fire2}");
    }

    [Fact]
    public void A_frozen_shooter_loses_its_aim_and_aims_again()
    {
        var r = new Rig();
        var archer = r.Spawn(ArcherId, 4, 0);
        int aims = 0;
        for (int i = 0; i < HordeSim.TickRate * 3 && aims == 0; i++) { r.Step(); aims += r.SimEvents.Count(e => e.Kind == HordeEventKind.WindUp); r.SimEvents.Clear(); }
        Assert.Equal(1, aims);
        archer.FrozenUntil = r.Sim.Tick + HordeSim.Ticks(1.0); // frozen mid-aim
        r.FxBegun.Clear();
        while (r.Sim.Tick + 1 < archer.FrozenUntil) { r.Step(); Assert.True((archer.Flags & (HordeEnemyFlags.Winding | HordeEnemyFlags.Frozen)) == HordeEnemyFlags.Frozen); }
        Assert.DoesNotContain(r.FxBegun, f => f.Kind == HordeFxKind.EnemyShot); // no shot while frozen, none at the thaw without a new aim
        int aimAgain = -1, fire = -1;
        for (int i = 0; i < HordeSim.TickRate * 2 && fire < 0; i++)
        {
            r.Step();
            if (aimAgain < 0 && r.SimEvents.Any(e => e.Kind == HordeEventKind.WindUp)) aimAgain = r.Sim.Tick;
            r.SimEvents.Clear();
            if (r.FxBegun.Any(f => f.Kind == HordeFxKind.EnemyShot)) fire = r.Sim.Tick;
        }
        Assert.True(aimAgain > 0); Assert.Equal(aimAgain + HordeSim.AimTicks, fire);
    }

    [Fact]
    public void A_charge_dashes_where_its_wind_up_pointed_even_when_the_player_moved()
    {
        var r = new Rig();
        var wolf = r.Spawn(WolfId, 5, 0);
        HordeEvent wind = default; int windTick = -1;
        for (int i = 0; i < HordeSim.TickRate * 5 && windTick < 0; i++)
        {
            r.Step();
            foreach (var e in r.SimEvents) if (e.Kind == HordeEventKind.WindUp) { wind = e; windTick = r.Sim.Tick; }
            r.SimEvents.Clear();
        }
        Assert.True(windTick > 0, "no wind-up");
        Assert.Equal((float)HordeWindUpKind.Charge, wind.B); Assert.Equal(0, wind.Slot); Assert.Equal(wolf.Slot, wind.Target);
        Assert.Equal(HordeSim.Ticks(0.7), wind.Value);
        Assert.Equal(8f, wind.C, 1); // 8 cells/s for 1 s
        SameOnTheWire(wind, r.WireEvents.First(e => e.Kind == HordeEventKind.WindUp));
        // I walk north during the wind-up: the dash keeps the wind-up's direction (it misses me)
        bool charging = false; ushort seq = 0;
        for (int i = 0; i < HordeSim.TickRate * 2 && !charging; i++)
        {
            r.Sim.PushInput(0, ++seq, 0, 127);
            r.Step(); r.SimEvents.Clear();
            charging = (wolf.Flags & HordeEnemyFlags.Charging) != 0;
            if (!charging) Assert.True((wolf.Flags & HordeEnemyFlags.Winding) != 0 || r.Sim.Tick == windTick + wind.Value);
        }
        Assert.True(charging);
        double dashDeg = Math.Atan2(wolf.DashVy, wolf.DashVx) * 180 / Math.PI;
        Assert.True(Math.Abs(Math.IEEERemainder(dashDeg - wind.A, 360)) < 0.5, $"dash {dashDeg:0.0}°, wind-up {wind.A:0.0}°");
        var me = r.Sim.Player(0)!;
        double nowToMe = Math.Atan2(me.Y - wolf.Y, me.X - wolf.X) * 180 / Math.PI;
        Assert.True(Math.Abs(Math.IEEERemainder(nowToMe - wind.A, 360)) > 5, $"the player moved: {nowToMe:0.0}° now vs {wind.A:0.0}°");
        // the arrow's end is where the dash goes: start + the dash's velocity × its ticks (inside the arena)
        float endX = HordeSim.Cells(Math.Max(r.Sim.MinX, Math.Min(r.Sim.MaxX, HordeSim.Units(wind.X) + wolf.DashVx * HordeSim.Ticks(1.0))));
        Assert.Equal(endX, wind.X2, 1);
        _out.WriteLine($"wind-up at tick {windTick}: {wind.A:0.0}° for {wind.C:0.0} cells to {wind.X2:0.0},{wind.Y2:0.0}; the dash {dashDeg:0.0}°, me now at {nowToMe:0.0}°");
    }

    [Fact]
    public void An_area_strike_names_its_caster_not_its_centre()
    {
        var r = new Rig();
        var caster = r.Spawn(CasterId, 5, 3);
        HordeEvent hit = default; bool got = false;
        for (int i = 0; i < HordeSim.TickRate * 5 && !got; i++)
        {
            r.Step();
            foreach (var e in r.SimEvents) if (e.Kind == HordeEventKind.PlayerHit) { hit = e; got = true; }
            r.SimEvents.Clear();
        }
        Assert.True(got, "no area hit");
        Assert.Equal((float)HordeHitVia.Area, hit.A); Assert.Equal(caster.Slot, hit.Target);
        Assert.Equal(HordeSim.Cells(caster.X), hit.X2, 3); Assert.Equal(HordeSim.Cells(caster.Y), hit.Y2, 3);
        SameOnTheWire(hit, r.WireEvents.First(e => e.Kind == HordeEventKind.PlayerHit));
    }

    [Fact]
    public void Shooters_and_chargers_play_the_same_run_for_the_same_seed()
    {
        ulong Hash(ulong seed)
        {
            var r = new Rig(seed); ulong h = 0; ushort seq = 0;
            r.Spawn(ArcherId, 4, 1); r.Spawn(ArcherId, -5, 2); r.Spawn(WolfId, 5, -3); r.Spawn(WolfId, -6, -1);
            for (int i = 0; i < HordeSim.TickRate * 20; i++)
            {
                double a = i * 0.03;
                r.Sim.PushInput(0, ++seq, (int)(Math.Cos(a) * 127), (int)(Math.Sin(a) * 127));
                r.Step(); h = h * 31 + r.Sim.StateHash();
                foreach (var e in r.SimEvents) h = h * 17 + (ulong)(e.Kind) + (ulong)(e.X2 * 256) + (ulong)(e.A * 10);
                r.SimEvents.Clear();
            }
            return h;
        }
        Assert.Equal(Hash(11), Hash(11));
        Assert.NotEqual(Hash(11), Hash(12));
    }
}
