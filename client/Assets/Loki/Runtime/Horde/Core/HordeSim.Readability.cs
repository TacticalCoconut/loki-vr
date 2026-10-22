#nullable disable
using System;

namespace Loki.Horde
{
    /// <summary>
    /// Client wave 17 (h17read): the engine's part of the readability proposal (design notes, the design's point
    /// 28; design notes of 2026-10-08: "Approved design specification."): what a client needs to show where attacks come from and where
    /// they go. Every rule here runs in Loki Host (this folder is compiled there) and is deterministic.
    ///   - Every PlayerHit names its attacker (D1 / D2 / D3 / D5): Target = the enemy's slot, X2 / Y2 = its place (the enemy for a touch
    ///     or a burst, where a shot was fired from, the caster of an area strike: the area's centre when the caster is gone), A = how
    ///     (<see cref="HordeHitVia"/>).
    ///   - A shooter AIMS for <see cref="AimSeconds"/> before each shot (Ranged / Still with ShotDamage, C4): it stands, it carries the
    ///     Winding flag (C1's "!"), and a WindUp event names the player it aims at; the shot then flies at that player's place as it is at
    ///     the shot (at the nearest standing player when the aimed one fell or left; no shot when nobody stands). A shooter that stays in
    ///     range shoots exactly as often as before (the aim ends at its ShotNext); one that comes into range shoots AimSeconds later than
    ///     it did. Frozen or stunned, it loses its aim.
    ///   - A charger KEEPS the direction its wind-up began with (C3; before: it aimed at the wind-up's end): a WindUp event tells the
    ///     direction and the dash's end, so the arrow drawn on the ground is the path the dash takes; stepping out of it dodges it.
    /// The state is kept in arrays by enemy slot, valid only for the enemy (Serial and SpawnTick) that took it: a slot's next tenant
    /// starts clean, and nothing here is added to SimEnemy.
    /// </summary>
    public sealed partial class HordeSim
    {
        /// <summary>The aim before every enemy shot (the proposal's C4: "for 0.3 s before it fires").</summary>
        public const float AimSeconds = 0.3f;
        /// <summary>A charge's dash lasts this long (HordeSim.StepEnemies: DashUntil = Tick + Ticks(1.0)): the arrow's length is ChargeSpeed × it.</summary>
        public const float ChargeDashSeconds = 1f;
        public static int AimTicks => Math.Max(1, Ticks(AimSeconds));

        private readonly int[] _aimUntil = new int[HordeLimits.MaxEnemies], _aimSlot = new int[HordeLimits.MaxEnemies];
        private readonly int[] _aimSerial = new int[HordeLimits.MaxEnemies], _aimSpawn = new int[HordeLimits.MaxEnemies];
        private readonly int[] _lockVx = new int[HordeLimits.MaxEnemies], _lockVy = new int[HordeLimits.MaxEnemies], _lockAt = new int[HordeLimits.MaxEnemies];
        private readonly int[] _lockSerial = new int[HordeLimits.MaxEnemies], _lockSpawn = new int[HordeLimits.MaxEnemies];

        private bool Aiming(SimEnemy e) { int i = e.Slot; return _aimUntil[i] != 0 && _aimSerial[i] == e.Serial && _aimSpawn[i] == e.SpawnTick; }

        /// <summary>One tick of a shooter's aim and shot. <paramref name="target"/>: the nearest standing player (null: none);
        /// <paramref name="inRange"/>: it is within the enemy's shot range. True while the enemy aims or shoots this tick (it stands).</summary>
        private bool AimShot(SimEnemy e, SimPlayer target, bool inRange)
        {
            int i = e.Slot;
            if (!Aiming(e))
            {
                if (!inRange || target == null || Tick < e.ShotNext - AimTicks) return false;
                _aimUntil[i] = Tick + AimTicks; _aimSlot[i] = target.Slot; _aimSerial[i] = e.Serial; _aimSpawn[i] = e.SpawnTick;
                e.Flags |= HordeEnemyFlags.Winding;
                WindUpEvent(e, HordeWindUpKind.Shot, target.Slot, target.X, target.Y, AimTicks);
                return true;
            }
            if (Tick < _aimUntil[i]) { e.Flags |= HordeEnemyFlags.Winding; return true; }
            // the shot: at the aimed player as it is now; the nearest standing one when it fell or left
            _aimUntil[i] = 0;
            var t = Player(_aimSlot[i]);
            if (t == null || t.Fallen) t = target;
            if (t == null) return false;
            e.ShotNext = Tick + Math.Max(1, Ticks(e.Def.AttackInterval));
            EnemyShoot(e, t.X, t.Y);
            return true;
        }

        /// <summary>A frozen or stunned enemy loses its aim (it aims again from the start when it can act).</summary>
        private void CancelAim(SimEnemy e) { if (Aiming(e)) _aimUntil[e.Slot] = 0; }

        /// <summary>A charge's wind-up began (WindUntil set): its dash's velocity is fixed now, toward the target's place, and told.</summary>
        private void LockDash(SimEnemy e, SimPlayer target)
        {
            if (target == null) return;
            int i = e.Slot;
            double a = Math.Atan2(target.Y - e.Y, target.X - e.X); int cs = Units(e.Def.ChargeSpeed / TickRate);
            _lockVx[i] = (int)(Math.Cos(a) * cs); _lockVy[i] = (int)(Math.Sin(a) * cs);
            _lockAt[i] = e.WindUntil; _lockSerial[i] = e.Serial; _lockSpawn[i] = e.SpawnTick;
            int dash = Ticks(ChargeDashSeconds);
            int ex = Clamp(e.X + _lockVx[i] * dash, MinX, MaxX), ey = Clamp(e.Y + _lockVy[i] * dash, MinY, MaxY);
            WindUpEvent(e, HordeWindUpKind.Charge, target.Slot, ex, ey, Math.Max(1, e.WindUntil - Tick));
        }

        /// <summary>The wind-up's end: the dash takes the kept velocity (false: nothing kept for this wind-up).</summary>
        private bool TakeLockedDash(SimEnemy e)
        {
            int i = e.Slot;
            if (_lockAt[i] == 0 || _lockAt[i] != e.WindUntil || _lockSerial[i] != e.Serial || _lockSpawn[i] != e.SpawnTick) return false;
            e.DashVx = _lockVx[i]; e.DashVy = _lockVy[i]; _lockAt[i] = 0;
            return true;
        }

        /// <summary>The WindUp event (HordeView.cs: HordeEventKind.WindUp): enemy <paramref name="e"/> winds up for <paramref name="ticks"/>
        /// toward (<paramref name="toX"/>, <paramref name="toY"/>) units. Other wind-ups (a boss's dash, h17boss) may tell themselves with it too.</summary>
        internal void WindUpEvent(SimEnemy e, HordeWindUpKind kind, int targetSlot, int toX, int toY, int ticks)
        {
            double dx = toX - e.X, dy = toY - e.Y;
            float deg = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI), len = (float)(Math.Sqrt(dx * dx + dy * dy) / U);
            AddEvent(HordeEventKind.WindUp, RankFlags(e.Def.Rank), targetSlot, e.Slot, e.Def.Id, ticks, e.X, e.Y, deg, (float)kind, len, toX, toY);
        }

        /// <summary>An enemy area struck player <paramref name="q"/>: the attacker is its caster while it lives, else the area's centre.</summary>
        private void HurtFromArea(SimPlayer q, SimFx f)
        {
            var c = f.OwnerEnemy >= 0 && f.OwnerEnemy < Enemies.Length ? Enemies[f.OwnerEnemy] : null;
            bool caster = c != null && c.Alive && c.Def != null && c.Def.Id == f.EnemyDefId;
            HurtPlayer(q, f.Damage, f.EnemyDefId, caster ? c.X : f.X, caster ? c.Y : f.Y, caster ? c.Slot : -1, HordeHitVia.Area);
        }

        /// <summary>An enemy shot struck player <paramref name="q"/>: the attacker's place is where it was fired from (StepEnemyShot has moved
        /// it once a tick since its StartTick's own step: an enemy shot flies straight).</summary>
        private void HurtFromShot(SimPlayer q, SimFx f)
        {
            long n = Math.Max(0, Tick - f.StartTick + 1);
            int ox = (int)Math.Max(MinX, Math.Min(MaxX, f.X - f.VX * n)), oy = (int)Math.Max(MinY, Math.Min(MaxY, f.Y - f.VY * n));
            HurtPlayer(q, f.Damage, f.DefId, ox, oy, f.OwnerEnemy, HordeHitVia.Shot);
        }
    }
}
