#nullable disable
using System;
using System.Collections.Generic;

namespace Loki.Horde
{
    /// <summary>
    /// Client wave 17 (h17boss), point 23 of the Horde design proposal (design notes section 3, design note:
    /// "Approved design specification."): the bosses' and elites' telegraphed attacks. Nine ability kinds beside the engine's
    /// own (AbilityKind Line, Cone, Ring, Pull, Mark, Grid, Teleport, Phase, Trail: HordeContent.cs says what each reads). Every
    /// attack that hurts first shows its warning for its Delay (the tables give 1-1.5 s; a mark 3 s): a lasting effect of kind
    /// HordeFxKind.EnemyLine .. EnemyTrail, sent once with everything the client needs to draw it (Horde/Client/HordeTelegraphs.cs),
    /// whose ArmTick is the strike. The strike hits what is in the drawn area then (a player's body counts, as for the engine's
    /// round warnings: within the area + RulesDef.BodyRadius).
    ///
    /// The engine's rules (Loki's own; the hooks in HordeSim.cs are marked "client wave 17 (h17boss)"):
    ///   - ONE warning at a time per caster: an attack with a warning (these kinds, and the engine's Nova / AreaAtPlayers) waits
    ///     while the caster's previous one has not struck (+ <see cref="BossGapSeconds"/>); its cooldown starts when it is used.
    ///   - The caster STANDS while it aims a lane, a cone, an expanding ring, a pull or a teleport (the warning starts at its feet;
    ///     the snapshot flags it Winding); it walks on during a grid, a mark, an inward ring.
    ///   - A strike lands even if its caster died meanwhile (the warning was shown); a dash, a jump, a pull and runners need it alive.
    ///   - Slows are buffs (SimBuff, MoveSpeedPct): the player's speed is the snapshot's, so the client's walk prediction follows.
    ///   - A pull moves a player's place outside its own walk: <see cref="PullStep"/> is the one rule, applied per input both by the
    ///     host (StepPlayers) and by the client's prediction (HordeClient.Step), so the drawn walk stays exact.
    ///   - The bosses without a mark of their own (the four new ones) join point 21's deal (<see cref="BossPool"/>).
    /// Deterministic: every choice comes from the run's dice.
    /// </summary>
    public sealed partial class HordeSim
    {
        /// <summary>Seconds a caster waits after a warned attack struck before it begins the next one (Loki's own).</summary>
        public const double BossGapSeconds = 0.5;
        /// <summary>Seconds between two zones of a trail (while the caster walks).</summary>
        public const double TrailEverySeconds = 0.5;
        /// <summary>Seconds a trail's slow stays on a player after it stepped out.</summary>
        public const double TrailSlowSeconds = 0.3;
        /// <summary>Seconds a caster roars at its second phase (it stands).</summary>
        public const double PhaseRoarSeconds = 1.0;
        /// <summary>The SimBuff ids of the two slows (WeaponId; SourceSlot -1): a petrifying cone's and a trail's. Negative: never a weapon's.</summary>
        public const int StoneSlowId = -1701, TrailSlowId = -1702;

        /// <summary>A caster's state (by enemy slot; reset when the slot gets a new enemy).</summary>
        private sealed class BossState
        {
            public int Serial = -1, Spawn = -1;
            public int BusyUntil, StandUntil;
            public bool Phase2;
            public int DashLeft, DashVx, DashVy, DashDamage, DashHit;
            /// <summary>The tick it landed a jump / a teleport: flagged Charging for the next snapshot (the client does not lean it as knocked back).</summary>
            public int LandedAt = int.MinValue / 2;
            public int TrailUntil, TrailNext, TrailRadius, TrailLinger, TrailSkill; public float TrailSlow; public int TrailX = int.MinValue, TrailY;
        }

        /// <summary>A warning's engine-side data (its SimFx is what the wire carries).</summary>
        private sealed class BossCast
        {
            public EnemyAbilityDef Ab;
            public int Slot, Serial, Spawn, DefId;
            public bool Struck;
            /// <summary>Line: the unit direction; Teleport / Mark: the place.</summary>
            public double Ux, Uy;
            public int Len;
        }

        private readonly BossState[] _boss = new BossState[HordeLimits.MaxEnemies];
        private readonly Dictionary<SimFx, BossCast> _casts = new Dictionary<SimFx, BossCast>();
        /// <summary>Counters for the tests and Loki Host's log: warnings begun and strikes, per AbilityKind.</summary>
        public readonly int[] BossCasts = new int[16], BossStrikes = new int[16];

        private BossState Boss(SimEnemy e)
        {
            var b = _boss[e.Slot];
            if (b == null) _boss[e.Slot] = b = new BossState();
            if (b.Serial != e.Serial || b.Spawn != e.SpawnTick) { _boss[e.Slot] = b = new BossState { Serial = e.Serial, Spawn = e.SpawnTick }; }
            return b;
        }

        private SimEnemy CasterOf(BossCast c)
        {
            var e = c.Slot >= 0 && c.Slot < Enemies.Length ? Enemies[c.Slot] : null;
            return e != null && e.Alive && e.Serial == c.Serial && e.SpawnTick == c.Spawn ? e : null;
        }

        /// <summary>The kinds this file adds.</summary>
        public static bool IsBossKind(AbilityKind k) => k >= AbilityKind.Line && k <= AbilityKind.Trail;
        /// <summary>The lasting effects this file makes (never dropped when the effects are many: a warning is never left out).</summary>
        public static bool IsBossFx(HordeFxKind k) => k >= HordeFxKind.EnemyLine && k <= HordeFxKind.EnemyTrail;
        /// <summary>The events of this file that carry the event record's extra block (X2 / Y2, A, B, C).</summary>
        public static bool BossEventHasExtra(HordeEventKind k) => k == HordeEventKind.BossStrike || k == HordeEventKind.PlayerSlowed;

        private int BossDamage(SimEnemy e, EnemyAbilityDef ab) => (int)Math.Round(ab.Damage * Arr(C.Scaling.EnemyDamageMul, PlayersForScaling()) * e.DmgScale);

        // =============================================================================================== point 21's deal

        /// <summary>The bosses of the tables that have no boss mark of their own join the deal of the marks (after the marks' own,
        /// by id: a table without them deals exactly as before). Called by DealBosses with the marks' ids.</summary>
        private void BossPool(List<int> ids)
        {
            var extra = new List<int>();
            foreach (var d in C.Enemies) if (d.Rank == EnemyRank.Boss && !ids.Contains(d.Id) && !extra.Contains(d.Id)) extra.Add(d.Id);
            extra.Sort();
            ids.AddRange(extra);
        }

        // =============================================================================================== the turn

        /// <summary>An attack that takes the caster's turn: one with a warning (these kinds but the trail; the engine's own with a Delay
        /// but the adds), and the phase's roar.</summary>
        private static bool Warned(EnemyAbilityDef ab) =>
            IsBossKind(ab.Kind) ? ab.Kind == AbilityKind.Phase || ab.Kind != AbilityKind.Trail && ab.Delay > 0 : ab.Kind != AbilityKind.SummonAdds && ab.Delay > 0;

        /// <summary>Whether the ability may be used at all now: under its HpBelow, a phase not yet entered.</summary>
        private static bool Eligible(SimEnemy e, BossState b, EnemyAbilityDef ab) =>
            !(IsBossKind(ab.Kind) && ab.HpBelow > 0 && e.Hp * 100.0 >= ab.HpBelow * (double)e.MaxHp) && !(ab.Kind == AbilityKind.Phase && b.Phase2);

        /// <summary>Whether ability <paramref name="i"/> of <paramref name="e"/> (due) is used now; when it is a warned one, the caster is
        /// booked until it struck, and of its due warned attacks the one that has waited longest goes first (none starves). The engine's
        /// own kinds without a warning are never held back.</summary>
        private bool BossTakeTurn(SimEnemy e, int i)
        {
            var ab = e.Def.Abilities[i];
            if (!IsBossKind(ab.Kind) && !Warned(ab)) return true; // the engine's adds, barrages, charges: as before
            var b = Boss(e);
            if (!Eligible(e, b, ab)) return false;
            if (!Warned(ab)) return true;
            if (Tick < b.BusyUntil || Tick < b.StandUntil || b.DashLeft > 0 || e.DashUntil > Tick || e.WindUntil > Tick) return false;
            var defs = e.Def.Abilities;
            for (int j = 0; j < defs.Length && j < e.AbilityNext.Length; j++)
                if (j != i && Tick >= e.AbilityNext[j] && (e.AbilityNext[j] < e.AbilityNext[i] || e.AbilityNext[j] == e.AbilityNext[i] && j < i) && Warned(defs[j]) && Eligible(e, b, defs[j])) return false;
            b.BusyUntil = Tick + Ticks(CastSeconds(ab, b) + BossGapSeconds);
            return true;
        }

        /// <summary>Seconds from a warned attack's start to its last strike (the caster's turn).</summary>
        private double CastSeconds(EnemyAbilityDef ab, BossState b)
        {
            double d = Math.Max(0.025, ab.Delay);
            switch (ab.Kind)
            {
                case AbilityKind.Phase: return PhaseRoarSeconds;
                case AbilityKind.Line: return ab.Shape == AbilityShape.Dash && ab.Speed > 0 ? d + (ab.Range > 0 ? ab.Range : 10) / ab.Speed : d;
                case AbilityKind.Pull: case AbilityKind.Mark: return d + Math.Max(0, ab.Duration);
                case AbilityKind.Grid:
                    return ab.Shape == AbilityShape.Checker ? d + Math.Max(0, ab.Duration) * PhaseMul(ab, b)
                         : ab.Shape == AbilityShape.Rain ? d + Math.Max(0, ab.Count - 1) * Math.Max(0, ab.Duration) : d;
                default: return d;
            }
        }

        private static double PhaseMul(EnemyAbilityDef ab, BossState b) => b != null && b.Phase2 && ab.PhaseCooldownPct > 0 ? ab.PhaseCooldownPct / 100.0 : 1.0;

        /// <summary>The cooldown's factor for an ability being used now (its caster's second phase: PhaseCooldownPct).</summary>
        private double BossCooldownMul(SimEnemy e, EnemyAbilityDef ab)
        {
            if (ab.PhaseCooldownPct <= 0) return 1.0;
            var b = _boss[e.Slot];
            return b != null && b.Serial == e.Serial && b.Spawn == e.SpawnTick && b.Phase2 ? ab.PhaseCooldownPct / 100.0 : 1.0;
        }

        // =============================================================================================== the caster's own moves

        /// <summary>Each tick for each enemy that is not held (StepEnemies): 0 = it moves as its behaviour says; 1 = it stands (casting:
        /// Winding); 2 = it moved itself (a dash: nothing else of its tick happens). Also lays a trail.</summary>
        private int BossMove(SimEnemy e)
        {
            if (e.Def.Abilities.Length == 0 && _boss[e.Slot] == null) return 0;
            var b = Boss(e);
            if (b.TrailUntil > Tick && Tick >= b.TrailNext) LayTrail(e, b);
            if (b.DashLeft > 0)
            {
                int sp = HordeMove.ISqrt((long)b.DashVx * b.DashVx + (long)b.DashVy * b.DashVy);
                int vx = b.DashVx, vy = b.DashVy;
                if (sp > b.DashLeft && sp > 0) { vx = (int)((long)vx * b.DashLeft / sp); vy = (int)((long)vy * b.DashLeft / sp); }
                int nx = Clamp(e.X + vx, MinX, MaxX), ny = Clamp(e.Y + vy, MinY, MaxY);
                bool edge = nx != e.X + vx || ny != e.Y + vy;
                e.X = nx; e.Y = ny;
                b.DashLeft = edge ? 0 : b.DashLeft - Math.Max(1, sp);
                e.Flags |= HordeEnemyFlags.Charging;
                int reach = e.Radius + Units(R.BodyRadius);
                foreach (var p in Players)
                {
                    if (!p.Present || p.Fallen || (b.DashHit & (1 << p.Slot)) != 0) continue;
                    if (Dist2(p.X, p.Y, e.X, e.Y) > (long)reach * reach) continue;
                    b.DashHit |= 1 << p.Slot;
                    HurtPlayer(p, b.DashDamage, e.Def.Id, e.X, e.Y, e.Slot, HordeHitVia.Area); // (merge h17int: who and how, h17read's parameters)
                }
                return 2;
            }
            if (Tick - b.LandedAt <= 2) e.Flags |= HordeEnemyFlags.Charging;
            if (Tick < b.StandUntil) { e.Flags |= HordeEnemyFlags.Winding; return 1; }
            return 0;
        }

        private void LayTrail(SimEnemy e, BossState b)
        {
            b.TrailNext = Tick + Math.Max(1, Ticks(TrailEverySeconds));
            // only where it has walked on (half a zone from the last one): a standing caster lays nothing new
            if (b.TrailX != int.MinValue && Dist2(e.X, e.Y, b.TrailX, b.TrailY) < (long)(b.TrailRadius / 2) * (b.TrailRadius / 2)) return;
            b.TrailX = e.X; b.TrailY = e.Y;
            var f = NewFx(HordeFxKind.EnemyTrail);
            f.OwnerEnemy = e.Slot; f.DefId = b.TrailSkill; f.X = e.X; f.Y = e.Y; f.Radius = b.TrailRadius; f.Count = (int)Math.Round(b.TrailSlow);
            f.ArmTick = Tick; f.EndTick = Tick + Math.Max(1, b.TrailLinger); f.EnemyDefId = e.Def.Id;
            BeginBossFx(f);
        }

        // =============================================================================================== the attacks

        /// <summary>The kinds of this file (UseAbility's default).</summary>
        private void UseBossAbility(SimEnemy e, EnemyAbilityDef ab)
        {
            if (!IsBossKind(ab.Kind)) return;
            var b = Boss(e);
            int delay = Math.Max(1, Ticks(ab.Delay));
            switch (ab.Kind)
            {
                case AbilityKind.Phase:
                {
                    b.Phase2 = true; b.StandUntil = Tick + Math.Max(1, Ticks(PhaseRoarSeconds));
                    // the abilities that come sooner in the second phase: their wait shrinks at once too
                    var defs = e.Def.Abilities;
                    for (int j = 0; j < defs.Length && j < e.AbilityNext.Length; j++)
                        if (defs[j].PhaseCooldownPct > 0 && e.AbilityNext[j] > Tick) e.AbilityNext[j] = Tick + (int)((e.AbilityNext[j] - Tick) * (defs[j].PhaseCooldownPct / 100.0));
                    AddEvent(HordeEventKind.BossPhase, RankFlags(e.Def.Rank), -1, e.Slot, e.Def.Id, (int)(e.Hp * 100L / Math.Max(1, e.MaxHp)), e.X, e.Y);
                    BossCasts[(int)ab.Kind]++;
                    return;
                }
                case AbilityKind.Trail:
                    b.TrailUntil = Tick + Math.Max(1, Ticks(ab.Duration)); b.TrailNext = Tick; b.TrailX = int.MinValue;
                    b.TrailRadius = Math.Max(16, Units(ab.Area)); b.TrailLinger = Math.Max(1, Ticks(ab.Linger > 0 ? ab.Linger : 5)); b.TrailSlow = ab.SlowPct; b.TrailSkill = ab.SkillId;
                    BossCasts[(int)ab.Kind]++;
                    return;
            }
            var t = BossTarget(e, ab.Aim);
            if (t == null) return; // nobody standing: nothing is aimed (the cooldown runs anyway)
            SimFx f = null;
            var c = new BossCast { Ab = ab, Slot = e.Slot, Serial = e.Serial, Spawn = e.SpawnTick, DefId = e.Def.Id };
            switch (ab.Kind)
            {
                case AbilityKind.Line:
                {
                    double a = Math.Atan2(t.Y - e.Y, t.X - e.X); c.Ux = Math.Cos(a); c.Uy = Math.Sin(a);
                    int edge = ToEdge(e.X, e.Y, c.Ux, c.Uy);
                    c.Len = Math.Min(ab.Range > 0 ? Math.Min(Units(ab.Range), edge) : edge, Units(127)); // inside the arena; the wire's vector: at most 127 cells
                    f = NewFx(HordeFxKind.EnemyLine);
                    f.X = e.X; f.Y = e.Y; f.VX = (int)Math.Round(c.Ux * c.Len); f.VY = (int)Math.Round(c.Uy * c.Len);
                    f.Count = ab.Shape == AbilityShape.Dash ? 1 : Math.Max(1, ab.Count); f.Range = Units(ab.Spacing);
                    if (ab.Shape == AbilityShape.Dash) f.Radius = e.Radius;
                    else if (ab.Shape == AbilityShape.Runners) { var rd = C.Enemy(ab.EnemyId); f.Radius = rd != null ? Math.Max(16, Units(rd.Radius)) : Units(0.5); }
                    else f.Radius = Math.Max(16, Units(ab.Width / 2));
                    f.ArmTick = Tick + delay;
                    int dashTicks = ab.Shape == AbilityShape.Dash ? Math.Max(1, (int)Math.Ceiling(c.Len / (double)Math.Max(1, Units(ab.Speed / TickRate)))) : 0;
                    f.EndTick = f.ArmTick + dashTicks;
                    b.StandUntil = f.ArmTick + 1;
                    break;
                }
                case AbilityKind.Cone:
                {
                    double a = Math.Atan2(t.Y - e.Y, t.X - e.X); c.Ux = Math.Cos(a); c.Uy = Math.Sin(a);
                    c.Len = Units(ab.Range > 0 ? ab.Range : 4);
                    f = NewFx(HordeFxKind.EnemyCone);
                    f.X = e.X; f.Y = e.Y; f.VX = (int)Math.Round(c.Ux * c.Len); f.VY = (int)Math.Round(c.Uy * c.Len);
                    f.Angle = Math.Max(5, Math.Min(360, (int)Math.Round(ab.Arc))); f.Count = ab.Shape == AbilityShape.Fan ? Math.Max(1, ab.Count) : 0;
                    f.Radius = c.Len; f.ArmTick = f.EndTick = Tick + delay;
                    b.StandUntil = f.ArmTick + 1;
                    break;
                }
                case AbilityKind.Ring:
                {
                    f = NewFx(HordeFxKind.EnemyRing);
                    f.Angle = _rng.Next(360);
                    if (ab.Shape == AbilityShape.Inward)
                    {
                        f.X = t.X; f.Y = t.Y; f.Range = Units(ab.Range > 0 ? ab.Range : 5); f.Count = Math.Max(3, Math.Min(64, ab.Count)); f.Radius = Units(0.35);
                        f.ArmTick = f.EndTick = Tick + delay;
                    }
                    else
                    {
                        f.X = e.X; f.Y = e.Y; f.Range = Units(ab.Range > 0 ? ab.Range : 8); f.Radius = Math.Max(16, Units(ab.Width > 0 ? ab.Width : 0.8));
                        f.Count = Math.Max(0, Math.Min(255, (int)Math.Round(ab.Arc)));
                        int sp = Math.Max(1, Units((ab.Speed > 0 ? ab.Speed : 6) / TickRate));
                        f.ArmTick = Tick + delay; f.EndTick = f.ArmTick + Math.Max(1, (f.Range + sp - 1) / sp);
                        b.StandUntil = f.ArmTick + 1;
                    }
                    break;
                }
                case AbilityKind.Pull:
                {
                    f = NewFx(HordeFxKind.EnemyPull);
                    f.X = e.X; f.Y = e.Y; f.Radius = Units(ab.Range > 0 ? ab.Range : 8); f.Range = Math.Max(1, Units((ab.Speed > 0 ? ab.Speed : 2) / TickRate));
                    f.Angle = e.Radius + Units(R.BodyRadius) + Units(0.6);
                    f.ArmTick = Tick + delay; f.EndTick = f.ArmTick + Math.Max(1, Ticks(ab.Duration > 0 ? ab.Duration : 2));
                    b.StandUntil = f.EndTick;
                    break;
                }
                case AbilityKind.Mark:
                {
                    f = NewFx(HordeFxKind.EnemyMark);
                    f.X = t.X; f.Y = t.Y; f.Target = t.Slot; f.Radius = Units(ab.Area > 0 ? ab.Area : 2);
                    f.ArmTick = f.EndTick = Tick + delay;
                    break;
                }
                case AbilityKind.Grid:
                {
                    if (ab.Shape == AbilityShape.Rain) { Rain(e, ab, t); BossCasts[(int)ab.Kind]++; return; }
                    f = NewFx(HordeFxKind.EnemyGrid);
                    f.X = t.X; f.Y = t.Y; f.Range = Math.Max(64, Units(ab.Width > 0 ? ab.Width : 2));
                    f.Count = Math.Max(1, Math.Min(31, ab.Count | 1)); // odd: the player's square is the middle one
                    f.ArmTick = Tick + delay;
                    f.EndTick = ab.Shape == AbilityShape.Checker ? f.ArmTick + Math.Max(1, Ticks(Math.Max(0.1, ab.Duration) * PhaseMul(ab, b))) : f.ArmTick;
                    break;
                }
                case AbilityKind.Teleport:
                {
                    int dx, dy;
                    if (ab.Shape == AbilityShape.Jump) { dx = t.X; dy = t.Y; }
                    else
                    {
                        // a teleport: Width cells from the player, on the arena's centre side (it stays inside; from the caster's side when
                        // the player is at the centre)
                        int cx = (MinX + MaxX) / 2, cy = (MinY + MaxY) / 2;
                        long vx = cx - t.X, vy = cy - t.Y; long d = HordeMove.ISqrt(vx * vx + vy * vy);
                        if (d < Units(1)) { vx = e.X - t.X; vy = e.Y - t.Y; d = Math.Max(1, HordeMove.ISqrt(vx * vx + vy * vy)); }
                        int w = Units(ab.Width > 0 ? ab.Width : 2.5);
                        dx = t.X + (int)(vx * w / Math.Max(1, d)); dy = t.Y + (int)(vy * w / Math.Max(1, d));
                    }
                    dx = Clamp(dx, MinX, MaxX); dy = Clamp(dy, MinY, MaxY);
                    f = NewFx(HordeFxKind.EnemyTeleport);
                    f.X = dx; f.Y = dy; f.VX = Clamp(e.X - dx, -32767, 32767); f.VY = Clamp(e.Y - dy, -32767, 32767);
                    f.Radius = ab.Shape == AbilityShape.Jump ? Units(ab.Area > 0 ? ab.Area : 1.5) : 0;
                    f.ArmTick = f.EndTick = Tick + delay;
                    b.StandUntil = f.ArmTick + 1;
                    break;
                }
            }
            if (f == null) return;
            f.OwnerEnemy = e.Slot; f.DefId = ab.SkillId; f.Level = (int)ab.Shape; f.EnemyDefId = e.Def.Id; f.Damage = BossDamage(e, ab);
            _casts[f] = c;
            BeginBossFx(f);
            BossCasts[(int)ab.Kind]++;
        }

        /// <summary>The player an ability aims at (standing players only; null when none stands).</summary>
        private SimPlayer BossTarget(SimEnemy e, BossAim aim)
        {
            int n = 0; SimPlayer far = null; long farD = -1;
            foreach (var p in Players)
            {
                if (!p.Present || p.Fallen) continue;
                n++;
                long d = Dist2(p.X, p.Y, e.X, e.Y);
                if (d > farD) { farD = d; far = p; }
            }
            if (n == 0) return null;
            if (aim == BossAim.Farthest) return far;
            if (aim == BossAim.Random)
            {
                int k = _rng.Next(n);
                foreach (var p in Players) if (p.Present && !p.Fallen && k-- == 0) return p;
            }
            return NearestStanding(e.X, e.Y, out _);
        }

        /// <summary>Units from (x, y) along the unit direction (ux, uy) to the arena's bound.</summary>
        private int ToEdge(int x, int y, double ux, double uy)
        {
            double t = double.MaxValue;
            if (ux > 1e-9) t = Math.Min(t, (MaxX - x) / ux); else if (ux < -1e-9) t = Math.Min(t, (MinX - x) / ux);
            if (uy > 1e-9) t = Math.Min(t, (MaxY - y) / uy); else if (uy < -1e-9) t = Math.Min(t, (MinY - y) / uy);
            return t == double.MaxValue ? 0 : Math.Max(0, (int)t);
        }

        /// <summary>Grid Rain: Count round warnings of the engine's own kind (EnemyWarning) around the player, Duration apart.</summary>
        private void Rain(SimEnemy e, EnemyAbilityDef ab, SimPlayer t)
        {
            int dmg = BossDamage(e, ab), delay = Math.Max(1, Ticks(ab.Delay)), spread = Units(ab.Spacing > 0 ? ab.Spacing : 4);
            for (int i = 0; i < Math.Max(1, ab.Count); i++)
            {
                double a = _rng.NextDouble() * Math.PI * 2, r = Math.Sqrt(_rng.NextDouble()) * spread;
                var f = NewFx(HordeFxKind.EnemyWarning);
                f.OwnerEnemy = e.Slot; f.DefId = ab.SkillId; f.X = Clamp(t.X + (int)(Math.Cos(a) * r), MinX, MaxX); f.Y = Clamp(t.Y + (int)(Math.Sin(a) * r), MinY, MaxY);
                f.Radius = Units(ab.Area > 0 ? ab.Area : 1); f.EndTick = Tick + delay + Ticks(i * Math.Max(0, ab.Duration)); f.Damage = dmg; f.EnemyDefId = e.Def.Id;
                BeginFx(f);
            }
        }

        // =============================================================================================== the warnings' strikes

        private void EndCast(SimFx f, bool hit) { EndFx(f, hit); _casts.Remove(f); }

        /// <summary>As BeginFx, but never dropped when the effects are many (a warning is never left out: its strike rides on it).</summary>
        private void BeginBossFx(SimFx f) { Fx.Add(f); FxBegun.Add(f); }

        /// <summary>Each tick after StepFx (Step's hook): every live warning of this file, in the order they were made; the ended ones go.</summary>
        private void StepBosses()
        {
            int n = Fx.Count; bool any = false;
            for (int i = 0; i < n; i++) { var f = Fx[i]; if (f.Ended || !IsBossFx(f.Kind)) continue; StepBossFx(f); any = true; }
            if (any) Fx.RemoveAll(x => x.Ended);
        }

        private void StepBossFx(SimFx f)
        {
            if (!IsBossFx(f.Kind)) return;
            if (f.Kind == HordeFxKind.EnemyTrail) { StepTrail(f); return; }
            if (!_casts.TryGetValue(f, out var c)) { EndCast(f, false); return; }
            var caster = CasterOf(c);
            int hitFrom = caster != null ? caster.Slot : -1; // (merge h17int: the attacker for h17read's PlayerHit; -1 once the caster is gone, as HurtFromArea)
            var ab = c.Ab;
            switch (f.Kind)
            {
                case HordeFxKind.EnemyLine:
                    if (ab.Shape == AbilityShape.Dash)
                    {
                        if (caster == null) { EndCast(f, false); return; }
                        if (!c.Struck && Tick >= f.ArmTick)
                        {
                            c.Struck = true;
                            var b = Boss(caster);
                            int sp = Math.Max(1, Units(ab.Speed / TickRate));
                            b.DashLeft = c.Len; b.DashVx = (int)Math.Round(c.Ux * sp); b.DashVy = (int)Math.Round(c.Uy * sp); b.DashDamage = f.Damage; b.DashHit = 0;
                            Strike(f, c, f.X, f.Y, f.X + f.VX, f.Y + f.VY, f.Radius / (float)U, 0f);
                        }
                        if (c.Struck && (Tick >= f.EndTick || Boss(caster).DashLeft <= 0)) EndCast(f, true);
                        return;
                    }
                    if (Tick < f.ArmTick) return;
                    if (ab.Shape == AbilityShape.Runners)
                    {
                        if (caster != null) Runners(f, c, caster);
                    }
                    else
                    {
                        int reach = f.Radius + Units(R.BodyRadius);
                        foreach (var p in Players)
                        {
                            if (!p.Present || p.Fallen) continue;
                            for (int k = 0; k < Math.Max(1, f.Count); k++)
                            {
                                LaneStart(f, c, k, out int lx, out int ly);
                                if (SegDist2(p.X, p.Y, lx, ly, lx + f.VX, ly + f.VY) <= (long)reach * reach) { HurtPlayer(p, f.Damage, c.DefId, lx, ly, hitFrom, HordeHitVia.Area); break; }
                            }
                        }
                    }
                    Strike(f, c, f.X, f.Y, f.X + f.VX, f.Y + f.VY, f.Radius / (float)U, 0f);
                    EndCast(f, true);
                    return;
                case HordeFxKind.EnemyCone:
                {
                    if (Tick < f.ArmTick) return;
                    double axis = Math.Atan2(f.VY, f.VX), half = f.Angle * Math.PI / 360.0;
                    if (f.Count > 0)
                    {
                        // a fan: Count shots across the arc
                        int sp = Math.Max(1, Units((ab.Speed > 0 ? ab.Speed : 6) / TickRate)), n = f.Count;
                        for (int i = 0; i < n; i++)
                        {
                            double a = n == 1 ? axis : axis - half + 2 * half * i / (n - 1);
                            var s = NewFx(HordeFxKind.EnemyShot);
                            s.OwnerEnemy = f.OwnerEnemy; s.DefId = c.DefId; s.X = f.X; s.Y = f.Y; s.VX = (int)Math.Round(Math.Cos(a) * sp); s.VY = (int)Math.Round(Math.Sin(a) * sp);
                            s.Radius = Units(0.35); s.EndTick = Tick + Math.Max(1, f.Radius / sp); s.Damage = f.Damage;
                            BeginFx(s);
                        }
                    }
                    else
                    {
                        int reach = f.Radius + Units(R.BodyRadius), body = Units(R.BodyRadius);
                        foreach (var p in Players)
                        {
                            if (!p.Present || p.Fallen) continue;
                            long d2 = Dist2(p.X, p.Y, f.X, f.Y);
                            if (d2 > (long)reach * reach) continue;
                            double d = Math.Sqrt(d2);
                            double off = Math.Abs(AngleDiff(Math.Atan2(p.Y - f.Y, p.X - f.X), axis));
                            double tol = d > body ? Math.Asin(Math.Min(1.0, body / d)) : Math.PI; // the body's own width
                            if (off > half + tol) continue;
                            HurtPlayer(p, f.Damage, c.DefId, f.X, f.Y, hitFrom, HordeHitVia.Area);
                            if (ab.SlowPct > 0 && p.Present && !p.Fallen) Slow(p, StoneSlowId, ab.SlowPct, Tick + Math.Max(1, Ticks(ab.Duration > 0 ? ab.Duration : 1.5)), ab.SkillId);
                        }
                    }
                    Strike(f, c, f.X, f.Y, f.X + f.VX, f.Y + f.VY, f.Radius / (float)U, (float)f.Angle);
                    EndCast(f, true);
                    return;
                }
                case HordeFxKind.EnemyRing:
                {
                    if (Tick < f.ArmTick) return;
                    if (f.Level == (int)AbilityShape.Inward)
                    {
                        // the orbs: one on each place of the circle but the gap's, flying to its centre
                        int sp = Math.Max(1, Units((ab.Speed > 0 ? ab.Speed : 3) / TickRate));
                        for (int k = 1; k < f.Count; k++)
                        {
                            double a = (f.Angle + k * 360.0 / f.Count) * Math.PI / 180.0;
                            var s = NewFx(HordeFxKind.EnemyShot);
                            s.OwnerEnemy = f.OwnerEnemy; s.DefId = c.DefId; s.X = f.X + (int)Math.Round(Math.Cos(a) * f.Range); s.Y = f.Y + (int)Math.Round(Math.Sin(a) * f.Range);
                            s.VX = -(int)Math.Round(Math.Cos(a) * sp); s.VY = -(int)Math.Round(Math.Sin(a) * sp);
                            s.Radius = f.Radius; s.EndTick = Tick + Math.Max(1, f.Range / sp); s.Damage = f.Damage;
                            BeginFx(s);
                        }
                        Strike(f, c, f.X, f.Y, f.X, f.Y, f.Range / (float)U, 0f);
                        EndCast(f, true);
                        return;
                    }
                    if (!c.Struck) { c.Struck = true; Strike(f, c, f.X, f.Y, f.X, f.Y, f.Range / (float)U, f.Count); }
                    // the band: its outer edge at r, Radius wide; each player once, outside the gap
                    double span = Math.Max(1, f.EndTick - f.ArmTick);
                    int r = (int)Math.Min(f.Range, f.Range * (Tick - f.ArmTick) / span);
                    int body2 = Units(R.BodyRadius);
                    foreach (var p in Players)
                    {
                        if (!p.Present || p.Fallen || f.HitList.Contains(p.Slot)) continue;
                        int d = HordeMove.ISqrt(Dist2(p.X, p.Y, f.X, f.Y));
                        if (d > r + body2 || d < r - f.Radius - body2) continue;
                        if (f.Count > 0 && d > 0 && Math.Abs(AngleDiff(Math.Atan2(p.Y - f.Y, p.X - f.X), f.Angle * Math.PI / 180.0)) <= f.Count * Math.PI / 360.0) continue; // in the gap
                        f.HitList.Add(p.Slot);
                        HurtPlayer(p, f.Damage, c.DefId, f.X, f.Y, hitFrom, HordeHitVia.Area);
                    }
                    if (Tick >= f.EndTick) EndCast(f, true);
                    return;
                }
                case HordeFxKind.EnemyPull:
                    if (caster == null || Tick >= f.EndTick) { EndCast(f, false); return; }
                    if (!c.Struck && Tick >= f.ArmTick) { c.Struck = true; Strike(f, c, f.X, f.Y, f.X, f.Y, f.Radius / (float)U, 0f); }
                    return;
                case HordeFxKind.EnemyMark:
                {
                    if (f.Target >= 0)
                    {
                        // following its player until the lock, then a new mark stays where it was
                        var p = Players[f.Target];
                        if (p.Present && !p.Fallen) { f.X = p.X; f.Y = p.Y; }
                        if (Tick < f.ArmTick) return;
                        EndCast(f, false);
                        var m = NewFx(HordeFxKind.EnemyMark);
                        m.OwnerEnemy = f.OwnerEnemy; m.DefId = f.DefId; m.Level = f.Level; m.EnemyDefId = f.EnemyDefId; m.Damage = f.Damage; m.Target = -1;
                        m.X = f.X; m.Y = f.Y; m.Radius = f.Radius; m.ArmTick = m.EndTick = Tick + Math.Max(1, Ticks(ab.Duration > 0 ? ab.Duration : 1));
                        _casts[m] = c;
                        BeginBossFx(m);
                        return;
                    }
                    if (Tick < f.ArmTick) return;
                    int reach = f.Radius + Units(R.BodyRadius);
                    foreach (var p in Players) if (p.Present && !p.Fallen && Dist2(p.X, p.Y, f.X, f.Y) <= (long)reach * reach) HurtPlayer(p, f.Damage, c.DefId, f.X, f.Y, hitFrom, HordeHitVia.Area);
                    Strike(f, c, f.X, f.Y, f.X, f.Y, f.Radius / (float)U, 0f);
                    EndCast(f, true);
                    return;
                }
                case HordeFxKind.EnemyGrid:
                {
                    bool checker = f.Level == (int)AbilityShape.Checker;
                    if (!c.Struck && Tick >= f.ArmTick) { c.Struck = true; GridStrike(f, c, checker ? 0 : 2); if (!checker) { EndCast(f, true); return; } }
                    if (c.Struck && checker && Tick >= f.EndTick) { GridStrike(f, c, 1); EndCast(f, true); }
                    return;
                }
                case HordeFxKind.EnemyTeleport:
                {
                    if (Tick < f.ArmTick) return;
                    if (caster != null)
                    {
                        int fromX = caster.X, fromY = caster.Y;
                        caster.X = f.X; caster.Y = f.Y; caster.KbVx = caster.KbVy = 0; caster.Flags |= HordeEnemyFlags.Charging; Boss(caster).LandedAt = Tick;
                        if (f.Radius > 0 && f.Damage > 0)
                        {
                            int reach = f.Radius + Units(R.BodyRadius);
                            foreach (var p in Players) if (p.Present && !p.Fallen && Dist2(p.X, p.Y, f.X, f.Y) <= (long)reach * reach) HurtPlayer(p, f.Damage, c.DefId, f.X, f.Y, hitFrom, HordeHitVia.Area);
                        }
                        Strike(f, c, f.X, f.Y, fromX, fromY, f.Radius / (float)U, 0f);
                    }
                    EndCast(f, caster != null);
                    return;
                }
            }
        }

        /// <summary>Grid squares strike: <paramref name="which"/> 0 = the even squares, 1 = the odd ones, 2 = the cross (the middle row and column).</summary>
        private void GridStrike(SimFx f, BossCast c, int which)
        {
            int side = Math.Max(1, f.Range), n = Math.Max(1, f.Count), half = n * side / 2, mid = n / 2;
            var caster = CasterOf(c); int hitFrom = caster != null ? caster.Slot : -1; // (merge h17int: the attacker, as StepBossFx)
            foreach (var p in Players)
            {
                if (!p.Present || p.Fallen) continue;
                int ox = p.X - f.X + half, oy = p.Y - f.Y + half;
                if (ox < 0 || oy < 0 || ox >= n * side || oy >= n * side) continue;
                int i = ox / side, j = oy / side;
                bool hit = which == 2 ? i == mid || j == mid : ((i + j) & 1) == which;
                if (hit) HurtPlayer(p, f.Damage, c.DefId, f.X - half + i * side + side / 2, f.Y - half + j * side + side / 2, hitFrom, HordeHitVia.Area);
            }
            Strike(f, c, f.X, f.Y, f.X, f.Y, side / (float)U, which);
        }

        /// <summary>Line Runners: one EnemyId on each lane's start, running along it (a Sweep enemy: gone at the arena's edge).</summary>
        private void Runners(SimFx f, BossCast c, SimEnemy caster)
        {
            var def = C.Enemy(c.Ab.EnemyId);
            if (def == null) return;
            _spawnHpScale = caster.HpScale; _spawnDmgScale = caster.DmgScale; // (a shuffled boss's runners take its mark's strength, as its adds)
            for (int k = 0; k < Math.Max(1, f.Count); k++)
            {
                LaneStart(f, c, k, out int lx, out int ly);
                int s = Spawn(def, Clamp(lx, MinX, MaxX), Clamp(ly, MinY, MaxY), true);
                if (s < 0) continue;
                var r = Enemies[s];
                r.DashVx = (int)Math.Round(c.Ux * r.Speed); r.DashVy = (int)Math.Round(c.Uy * r.Speed);
            }
            _spawnHpScale = _spawnDmgScale = 1;
        }

        /// <summary>Lane k's start: the cast's place moved sideways (lanes centred on the aim).</summary>
        private static void LaneStart(SimFx f, BossCast c, int k, out int x, out int y)
        {
            double off = (k - (Math.Max(1, f.Count) - 1) / 2.0) * f.Range;
            x = f.X + (int)Math.Round(-c.Uy * off); y = f.Y + (int)Math.Round(c.Ux * off);
        }

        private void Strike(SimFx f, BossCast c, int x, int y, int x2, int y2, float a, float b)
        {
            AddEvent(HordeEventKind.BossStrike, HordeEventFlags.None, -1, f.OwnerEnemy, c.Ab.SkillId, (int)c.Ab.Kind, x, y, a, b, 0f, x2, y2);
            BossStrikes[(int)c.Ab.Kind]++;
        }

        private void StepTrail(SimFx f)
        {
            if (Tick >= f.EndTick) { EndFx(f, false); return; }
            int until = Tick + Math.Max(1, Ticks(TrailSlowSeconds));
            foreach (var p in Players)
                if (p.Present && !p.Fallen && Dist2(p.X, p.Y, f.X, f.Y) <= (long)f.Radius * f.Radius) Slow(p, TrailSlowId, f.Count, until, f.DefId);
        }

        /// <summary>A slow on a player (a SimBuff: its speed is recomputed and sent; refreshed, never stacked by the same source kind).</summary>
        private void Slow(SimPlayer p, int id, float pct, int until, int skillId)
        {
            if (pct <= 0 || p.Fallen) return;
            foreach (var x in p.Buffs) if (x.WeaponId == id && x.SourceSlot == -1) { if (until > x.Until) x.Until = until; return; }
            p.Buffs.Add(new SimBuff { Mods = new[] { new ModDef(Mod.MoveSpeedPct, -Math.Min(95f, pct)) }, Until = until, SourceSlot = -1, WeaponId = id });
            Recalc(p, false);
            AddEvent(HordeEventKind.PlayerSlowed, HordeEventFlags.None, p.Slot, -1, skillId, (int)Math.Round(pct), p.X, p.Y, (until - Tick) / (float)TickRate);
        }

        // =============================================================================================== the pull

        /// <summary>A player's place after one step of every live pull (StepPlayers, after each input's walk).</summary>
        private void BossPull(SimPlayer p)
        {
            foreach (var f in Fx)
            {
                if (f.Kind != HordeFxKind.EnemyPull || f.Ended || Tick < f.ArmTick || Tick >= f.EndTick) continue;
                PullStep(f.X, f.Y, f.Radius, f.Range, (int)f.Angle, ref p.X, ref p.Y, MinX, MinY, MaxX, MaxY);
            }
        }

        /// <summary>THE pull rule (host and client prediction): a place within <paramref name="reach"/> of (cx, cy) moves
        /// <paramref name="perTick"/> units toward it, never nearer than <paramref name="stop"/>, then inside the arena. Integers only.</summary>
        public static void PullStep(int cx, int cy, int reach, int perTick, int stop, ref int x, ref int y, int minX, int minY, int maxX, int maxY)
        {
            long dx = cx - x, dy = cy - y, d2 = dx * dx + dy * dy;
            if (d2 > (long)reach * reach || d2 == 0) return;
            int d = HordeMove.ISqrt(d2);
            if (d <= stop) return;
            int s = Math.Min(perTick, d - stop);
            x += (int)(dx * s / d); y += (int)(dy * s / d);
            if (x < minX) x = minX; else if (x > maxX) x = maxX;
            if (y < minY) y = minY; else if (y > maxY) y = maxY;
        }

        /// <summary>The client's side of <see cref="PullStep"/>: every pull of the view live at <paramref name="tick"/>, on a place in units
        /// (the view's places convert back to the host's units exactly: 1/256 cell steps).</summary>
        public static void PullView(HordeWorldView w, int tick, ref int x, ref int y, int minX, int minY, int maxX, int maxY)
        {
            if (w == null) return;
            foreach (var f in w.Fx)
            {
                if (f.Kind != HordeFxKind.EnemyPull || tick < f.ArmTick || tick >= f.EndTick) continue;
                PullStep((int)Math.Round(f.X * U), (int)Math.Round(f.Y * U), (int)Math.Round(f.Radius * U), (int)Math.Round(f.Range * U), (int)Math.Round(f.Angle), ref x, ref y, minX, minY, maxX, maxY);
            }
        }

        // =============================================================================================== geometry

        private static double AngleDiff(double a, double b)
        {
            double d = a - b;
            while (d > Math.PI) d -= 2 * Math.PI;
            while (d < -Math.PI) d += 2 * Math.PI;
            return d;
        }

        /// <summary>Squared distance (units²) from (px, py) to the segment (ax, ay)-(bx, by).</summary>
        public static long SegDist2(int px, int py, int ax, int ay, int bx, int by)
        {
            long vx = bx - ax, vy = by - ay, wx = px - ax, wy = py - ay;
            long vv = vx * vx + vy * vy;
            if (vv == 0) return wx * wx + wy * wy;
            double t = (wx * (double)vx + wy * (double)vy) / vv;
            if (t < 0) t = 0; else if (t > 1) t = 1;
            double cx = ax + t * vx - px, cy = ay + t * vy - py;
            return (long)Math.Round(cx * cx + cy * cy);
        }
    }
}
