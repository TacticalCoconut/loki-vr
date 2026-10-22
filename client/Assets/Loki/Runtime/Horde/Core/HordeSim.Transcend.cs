#nullable disable
using System;
using System.Collections.Generic;

namespace Loki.Horde
{
    // ====================================================================================================================
    // Client wave 18 (h18trans), point 30 of the design's Horde list:
    //   The simulation rules for weapon transcendence (two branches per weapon: 5000 + root and 6000 + root) and passive
    //   transcendence (1000 + id). Pure C#, deterministic (uses the run's _rng).
    // ====================================================================================================================
    public sealed partial class HordeSim
    {
        private sealed class TransPlayerState
        {
            public int AuraPulses;
            public int PetActs;
            public int DashEndTick, DashEndDmg;
            public OwnedWeapon DashEndWeapon;
            public int FortressNext, ArchmageNext, WindTrailNext, BaneBlastNext;
            public int WarlordHits, DeadeyeHits, HarvesterKills;
            public bool InChainEffect;
        }

        private readonly TransPlayerState[] _ts = new TransPlayerState[HordeLimits.MaxPlayers];
        private TransPlayerState TS(SimPlayer p) => _ts[p.Slot] ?? (_ts[p.Slot] = new TransPlayerState());

        private void TransReset(int slot)
        {
            if (slot >= 0 && slot < _ts.Length) _ts[slot] = new TransPlayerState();
        }

        public static int PassiveRootId(PassiveDef d) => d == null ? 0 : d.TransOf > 0 ? d.TransOf : d.Id;

        private static int CountPassiveTrans(SimPlayer p, PassiveTransKind k)
        {
            int n = 0;
            foreach (var op in p.Passives) if (op.Def.Trans == k) n++;
            return n;
        }

        // ================================================================================================= offers & cards

        /// <summary>DrawCards' hook: adds WeaponTranscend and PassiveTranscend candidates for max-level weapons and passives.</summary>
        private void TransOfferCandidates(SimPlayer p, List<Cand> lvlW, List<Cand> lvlP)
        {
            foreach (var ow in p.Weapons)
            {
                if (ow.Level < ow.Def.Levels.Length || ow.Def.TransOf > 0 || ow.Def.EvolveOf > 0 || YggBurntWeapon(p, ow)) continue;
                int root = RootId(ow.Def);
                var rw = C.Weapon(root) ?? ow.Def;
                if (rw.TransBranches == null || rw.TransBranches.Length < 2) continue;
                if (C.Weapon(rw.TransBranches[0]) == null || C.Weapon(rw.TransBranches[1]) == null) continue;
                lvlW.Add(new Cand
                {
                    Kind = HordeCardKind.WeaponTranscend,
                    DefId = ow.Def.Id,
                    Level = ow.Level + 1,
                    Weight = Math.Max(1, rw.Weight > 0 ? rw.Weight : 100),
                });
            }
            foreach (var op in p.Passives)
            {
                if (op.Level < op.Def.Levels.Length || op.Def.TransOf > 0) continue;
                var tp = C.Passive(1000 + op.Def.Id);
                if (tp == null) continue;
                lvlP.Add(new Cand
                {
                    Kind = HordeCardKind.PassiveTranscend,
                    DefId = tp.Id,
                    Level = op.Level + 1,
                    Weight = Math.Max(1, op.Def.Weight > 0 ? op.Def.Weight : 80),
                });
            }
        }

        /// <summary>DrawCards' hook: expands a drawn WeaponTranscend candidate into its two branch cards side by side.</summary>
        private bool TransBranchCards(SimPlayer p, Cand ch, List<HordeCard> picked, int want)
        {
            if (ch.Kind != HordeCardKind.WeaponTranscend) return false;
            var w = C.Weapon(ch.DefId);
            var rw = w != null ? (C.Weapon(RootId(w)) ?? w) : null;
            if (rw == null || rw.TransBranches == null || rw.TransBranches.Length < 2) return false;
            // Always place both transcendence branches side by side (up to 6 cards total in the choice panel)
            if (picked.Count + 2 <= 6)
            {
                picked.Add(new HordeCard { Kind = HordeCardKind.WeaponTranscend, DefId = rw.TransBranches[0], Level = ch.Level });
                picked.Add(new HordeCard { Kind = HordeCardKind.WeaponTranscend, DefId = rw.TransBranches[1], Level = ch.Level });
            }
            else
            {
                picked.Add(new HordeCard { Kind = HordeCardKind.WeaponTranscend, DefId = rw.TransBranches[_rng.Next(2)], Level = ch.Level });
            }
            return true;
        }

        private void ApplyWeaponTranscend(SimPlayer p, HordeCard c)
        {
            var t = C.Weapon(c.DefId);
            if (t == null || t.TransOf <= 0) return;
            foreach (var ow in p.Weapons)
            {
                if (RootId(ow.Def) == t.TransOf && ow.Def.TransOf == 0)
                {
                    int newLv = Math.Max(ow.Level + 1, c.Level > 0 ? c.Level : 6);
                    ow.Def = t;
                    ow.Level = newLv;
                    RestartOrbit(p, ow);
                    for (int i = Summons.Count - 1; i >= 0; i--)
                        if (Summons[i].OwnerSlot == p.Slot && Summons[i].Weapon == ow) Summons.RemoveAt(i);
                    ow.NextTick = Tick + 1;
                    return;
                }
            }
        }

        private void ApplyPassiveTranscend(SimPlayer p, HordeCard c)
        {
            var tp = C.Passive(c.DefId);
            if (tp == null || tp.TransOf <= 0) return;
            foreach (var op in p.Passives)
            {
                if (op.Def.Id == tp.TransOf && op.Def.TransOf == 0)
                {
                    op.Def = tp;
                    op.Level = Math.Max(op.Level + 1, c.Level > 0 ? c.Level : 6);
                    return;
                }
            }
        }

        private void TransChestCandidates(SimPlayer p, List<HordeCard> opts)
        {
            foreach (var ow in p.Weapons)
            {
                if (ow.Level < ow.Def.Levels.Length || ow.Def.TransOf > 0 || ow.Def.EvolveOf > 0 || YggBurntWeapon(p, ow)) continue;
                var rw = C.Weapon(RootId(ow.Def)) ?? ow.Def;
                if (rw.TransBranches != null && rw.TransBranches.Length >= 2)
                    opts.Add(new HordeCard { Kind = HordeCardKind.WeaponTranscend, DefId = rw.TransBranches[_rng.Next(2)], Level = ow.Level + 1 });
            }
            foreach (var op in p.Passives)
            {
                if (op.Level < op.Def.Levels.Length || op.Def.TransOf > 0) continue;
                var tp = C.Passive(1000 + op.Def.Id);
                if (tp != null)
                    opts.Add(new HordeCard { Kind = HordeCardKind.PassiveTranscend, DefId = tp.Id, Level = op.Level + 1 });
            }
        }

        // ================================================================================================= weapon mechanics

        private void PullEnemiesToward(int cx, int cy, int radiusUnits, int pullUnits)
        {
            _q.Clear(); _grid.Query(cx, cy, radiusUnits + Units(1.5), Enemies, _q);
            foreach (int i in _q)
            {
                var e = Enemies[i];
                if (!e.Alive || e.Def.KnockbackResist >= 100) continue;
                int rr = radiusUnits + e.Radius;
                long dx = cx - e.X, dy = cy - e.Y, dd = dx * dx + dy * dy;
                if (dd <= 16 || dd > (long)rr * rr) continue;
                int d = Math.Max(1, HordeMove.ISqrt(dd));
                int step = Math.Min(d - Units(0.35), (int)Math.Round(pullUnits * Math.Max(0.2, 1.0 - e.Def.KnockbackResist / 100.0)));
                if (step <= 0) continue;
                e.X = Clamp(e.X + (int)(dx * step / d), MinX, MaxX);
                e.Y = Clamp(e.Y + (int)(dy * step / d), MinY, MaxY);
            }
        }

        private bool FireTransBolt(SimPlayer p, OwnedWeapon ow, WeaponLevel lv, int range, int speed, int count, double ang, int target, bool burst)
        {
            var tk = ow.Def.Trans;
            if (tk == TransKind.BoltNova)
            {
                int n = Math.Max(8, count);
                for (int i = 0; i < n; i++)
                {
                    double a = ang + i * (Math.PI * 2.0 / n);
                    Shoot(p, ow, lv, a, speed, range, -1);
                }
                return true;
            }
            return false;
        }

        private void OnTransMeleeSwing(SimPlayer p, OwnedWeapon ow, WeaponLevel lv, double ang, int reach)
        {
            var tk = ow.Def.Trans;
            if (tk == TransKind.ArcVortex)
            {
                PullEnemiesToward(p.X, p.Y, (int)(reach * 1.6), Units(1.4));
            }
            else if (tk == TransKind.ArcWave)
            {
                int sp = Math.Max(1, Units(10.0 * Math.Max(0.2, 1 + p.S.ProjectileSpeedPct / 100.0) / TickRate));
                int wRange = Math.Max(Units(6.5), reach * 3);
                for (int i = -1; i <= 1; i++)
                {
                    var f = ShootFrom(p, ow, lv, ang + i * 0.22, sp, wRange, -1, p.X, p.Y);
                    f.BaseDamage = Math.Max(1, lv.Damage * 0.65);
                    f.PierceLeft = Math.Max(3, lv.Pierce + 3);
                }
            }
        }

        private void OnTransNova(SimPlayer p, OwnedWeapon ow, WeaponLevel lv, int area)
        {
            var tk = ow.Def.Trans;
            if (tk == TransKind.NovaEcho)
            {
                var f = NewFx(HordeFxKind.Warning);
                f.OwnerSlot = p.Slot; f.DefId = ow.Def.Id; f.Level = ow.Level; f.X = p.X; f.Y = p.Y;
                f.Radius = (int)Math.Round(area * 1.4);
                f.EndTick = Tick + Math.Max(1, Ticks(0.32));
                f.Weapon = ow; f.Lv = lv; f.BaseDamage = Math.Max(1, lv.Damage * 0.85);
                f.Count = 1;
                BeginFx(f);
            }
            else if (tk == TransKind.NovaImplode)
            {
                PullEnemiesToward(p.X, p.Y, (int)(area * 1.55), Units(1.6));
                var z = NewFx(HordeFxKind.Zone);
                z.OwnerSlot = p.Slot; z.DefId = ow.Def.Id; z.Level = ow.Level; z.X = p.X; z.Y = p.Y;
                z.Radius = (int)Math.Round(area * 0.85);
                z.Every = Math.Max(1, Ticks(0.4)); z.NextTick = Tick + z.Every; z.EndTick = Tick + DurationTicks(p, 3.0f);
                z.Weapon = ow; z.Lv = lv; z.BaseDamage = Math.Max(1, lv.Damage * 0.35);
                BeginFx(z);
            }
        }

        private void OnTransWarningStrike(SimPlayer p, SimFx f)
        {
            if (f.Weapon?.Def == null) return;
            var tk = f.Weapon.Def.Trans;
            if (tk == TransKind.AreaCascade && f.Count == 0)
            {
                for (int i = 0; i < 3; i++)
                {
                    double a = i * (Math.PI * 2.0 / 3.0) + (Tick & 7) * 0.2;
                    int d = (int)Math.Round(f.Radius * 0.85);
                    int sx = Clamp(f.X + (int)Math.Round(Math.Cos(a) * d), MinX, MaxX);
                    int sy = Clamp(f.Y + (int)Math.Round(Math.Sin(a) * d), MinY, MaxY);
                    var w = NewFx(HordeFxKind.Warning);
                    w.OwnerSlot = p.Slot; w.DefId = f.DefId; w.Level = f.Level; w.X = sx; w.Y = sy;
                    w.Radius = Math.Max(Units(0.8), (int)Math.Round(f.Radius * 0.75));
                    w.EndTick = Tick + Math.Max(1, Ticks(0.25));
                    w.Weapon = f.Weapon; w.Lv = f.Lv; w.BaseDamage = Math.Max(1, f.BaseDamage * 0.55);
                    w.Count = 1;
                    BeginFx(w);
                }
            }
            else if (tk == TransKind.AreaScorch && f.Count == 0)
            {
                var z = NewFx(HordeFxKind.Zone);
                z.OwnerSlot = p.Slot; z.DefId = f.DefId; z.Level = f.Level; z.X = f.X; z.Y = f.Y;
                z.Radius = Math.Max(Units(1.0), (int)Math.Round(f.Radius * 0.9));
                z.Every = Math.Max(1, Ticks(0.45)); z.NextTick = Tick + z.Every; z.EndTick = Tick + DurationTicks(p, 3.5f);
                z.Weapon = f.Weapon; z.Lv = f.Lv; z.BaseDamage = Math.Max(1, f.BaseDamage * 0.35);
                z.Count = 1;
                BeginFx(z);
            }
        }

        private bool StepTransShot(SimFx f)
        {
            if (f.Weapon?.Def == null || f.Weapon.Def.Trans != TransKind.BoltReturn) return false;
            if (f.Count == 0 && Tick >= f.EndTick)
            {
                f.Count = 1;
                f.VX = -f.VX;
                f.VY = -f.VY;
                f.Target = -1;
                f.HitList.Clear();
                int flight = Math.Max(4, Tick - f.StartTick);
                f.EndTick = Tick + flight;
                return true;
            }
            return false;
        }

        private void StepTransZone(SimPlayer p, SimFx f, bool expiring)
        {
            if (f.Weapon?.Def == null) return;
            var tk = f.Weapon.Def.Trans;
            if (expiring)
            {
                if (tk == TransKind.ZoneCollapse && f.Count == 0)
                {
                    int rad = (int)Math.Round(f.Radius * 1.4);
                    if (f.BaseDamage > 0)
                        StrikeArea(p, f.Weapon, f.Lv, f.X, f.Y, rad, Math.Max(40, f.BaseDamage * 2.2));
                    if (f.Heal > 0 || f.HealPct > 0)
                    {
                        foreach (var q in Players)
                            if (q.Present && !q.Fallen && Dist2(q.X, q.Y, f.X, f.Y) <= (long)rad * rad)
                                Heal(p, q, f.Lv, f.DefId);
                        AddEvent(HordeEventKind.Nova, HordeEventFlags.None, p.Slot, -1, f.DefId, f.Level, f.X, f.Y, rad / (float)U);
                    }
                }
                return;
            }
            if (tk == TransKind.ZoneDrift && !f.Follows)
            {
                int t = FindTarget(f.X, f.Y, Targeting.Nearest, Units(10), 0);
                if (t >= 0)
                {
                    var e = Enemies[t];
                    long dx = e.X - f.X, dy = e.Y - f.Y, d = HordeMove.ISqrt(dx * dx + dy * dy);
                    if (d > 8)
                    {
                        int sp = Math.Max(1, Units(2.2 / TickRate));
                        f.X = Clamp(f.X + (int)(dx * sp / d), MinX, MaxX);
                        f.Y = Clamp(f.Y + (int)(dy * sp / d), MinY, MaxY);
                    }
                }
            }
            else if (tk == TransKind.ZoneCollapse && Tick >= f.NextTick)
            {
                PullEnemiesToward(f.X, f.Y, (int)(f.Radius * 1.35), Units(0.75));
            }
        }

        private int TransOrbitRange(SimFx f)
        {
            if (f.Weapon?.Def != null && f.Weapon.Def.Trans == TransKind.OrbitSpiral)
            {
                double wave = Math.Sin((Tick - f.StartTick) * 0.12);
                return Math.Max(Units(0.8), (int)Math.Round(f.Range * (1.0 + 0.42 * wave)));
            }
            return f.Range;
        }

        private void StepTransOrbitShoot(SimPlayer p, SimFx f, int ox, int oy)
        {
            if (f.Weapon?.Def == null || f.Weapon.Def.Trans != TransKind.OrbitShoot) return;
            if ((Tick - f.StartTick) % Math.Max(1, Ticks(1.5)) != 0) return;
            int target = FindTarget(ox, oy, Targeting.Nearest, Units(8), 0);
            if (target < 0) return;
            var e = Enemies[target];
            int sp = Math.Max(1, Units(10.0 / TickRate));
            var shot = ShootFrom(p, f.Weapon, f.Lv, Math.Atan2(e.Y - oy, e.X - ox), sp, Units(8), target, ox, oy);
            shot.BaseDamage = Math.Max(1, f.BaseDamage * 0.7);
            shot.PierceLeft = 2;
        }

        private void OnTransChain(SimPlayer p, OwnedWeapon ow, WeaponLevel lv, int first, int jumps, int jump, List<int> hit)
        {
            var tk = ow.Def.Trans;
            if (tk == TransKind.ChainFork && first >= 0 && Enemies[first].Alive)
            {
                int fx = Enemies[first].X, fy = Enemies[first].Y;
                int cur = FindTarget(fx, fy, Targeting.Nearest, jump, 0, hit);
                double dmg = lv.Damage * 0.85;
                for (int k = 0; k < jumps && cur >= 0; k++)
                {
                    var e = Enemies[cur];
                    AddEvent(HordeEventKind.Chain, HordeEventFlags.None, p.Slot, cur, ow.Def.Id, ow.Level, fx, fy, 0, 0, 0, e.X, e.Y);
                    fx = e.X; fy = e.Y; hit.Add(cur);
                    HitEnemy(p, ow, lv, e, dmg, fx, fy);
                    cur = FindTarget(fx, fy, Targeting.Nearest, jump, 0, hit);
                }
            }
            else if (tk == TransKind.ChainOverload)
            {
                int rad = AreaUnits(p, 1.8f);
                foreach (int idx in hit.ToArray())
                {
                    var e = Enemies[idx];
                    if (e.Alive && e.Def.EffectResist < 100)
                        e.StunUntil = Math.Max(e.StunUntil, Tick + Ticks(0.6 * (1.0 - e.Def.EffectResist / 100.0)));
                    StrikeArea(p, ow, lv, e.X, e.Y, rad, Math.Max(1, lv.Damage * 0.55));
                }
            }
        }

        private void OnTransAuraPulse(SimPlayer p, OwnedWeapon ow, WeaponLevel lv, int area)
        {
            var tk = ow.Def.Trans;
            if (tk == TransKind.AuraPulse)
            {
                double scorch = Math.Max(45, lv.Damage > 0 ? lv.Damage * 0.6 : 55);
                _q.Clear(); _grid.Query(p.X, p.Y, area + Units(1.5), Enemies, _q);
                foreach (int i in _q.ToArray())
                {
                    var e = Enemies[i]; int rr = area + e.Radius;
                    if (e.Alive && Dist2(p.X, p.Y, e.X, e.Y) <= (long)rr * rr)
                        HitEnemy(p, ow, lv, e, scorch, p.X, p.Y);
                }
                foreach (var q in Players)
                {
                    if (!q.Present || q.Fallen || Dist2(q.X, q.Y, p.X, p.Y) > (long)area * area) continue;
                    int h = Math.Max(2, q.MaxHp * 4 / 100);
                    int before = q.Hp; q.Hp = Math.Min(q.MaxHp, q.Hp + h);
                    if (q.Hp > before) AddEvent(HordeEventKind.Heal, HordeEventFlags.None, q.Slot, p.Slot, ow.Def.Id, q.Hp - before, q.X, q.Y);
                }
            }
            else if (tk == TransKind.AuraNova)
            {
                var ts = TS(p);
                if (++ts.AuraPulses >= 3)
                {
                    ts.AuraPulses = 0;
                    int rad = (int)Math.Round(area * 1.45);
                    double blast = Math.Max(90, lv.Damage * 1.8);
                    _q.Clear(); _grid.Query(p.X, p.Y, rad + Units(1.5), Enemies, _q);
                    foreach (int i in _q.ToArray())
                    {
                        var e = Enemies[i]; int rr = rad + e.Radius;
                        if (!e.Alive || Dist2(p.X, p.Y, e.X, e.Y) > (long)rr * rr) continue;
                        if (!HitEnemy(p, ow, lv, e, blast, p.X, p.Y) && e.Alive && e.Def.EffectResist < 100)
                            e.StunUntil = Math.Max(e.StunUntil, Tick + Ticks(0.8 * (1.0 - e.Def.EffectResist / 100.0)));
                    }
                    AddEvent(HordeEventKind.Strike, HordeEventFlags.None, p.Slot, -1, ow.Def.Id, ow.Level, p.X, p.Y, rad / (float)U);
                }
            }
        }

        private void OnTransSummonLost(SimSummon s, SimPlayer owner)
        {
            if (!owner.Present || s.Weapon?.Def == null || s.Weapon.Def.Trans != TransKind.SummonExplode) return;
            var lv = s.Weapon.Lv;
            int rad = AreaUnits(owner, 2.8f);
            StrikeArea(owner, s.Weapon, lv, s.X, s.Y, rad, Math.Max(60, lv.Damage * 2.5));
        }

        private void StepTransTrap(SimPlayer p, SimFx f, bool triggered)
        {
            if (f.Weapon?.Def == null) return;
            var tk = f.Weapon.Def.Trans;
            if (!triggered)
            {
                if (tk == TransKind.TrapSeeker && Tick >= f.ArmTick)
                {
                    int t = FindTarget(f.X, f.Y, Targeting.Nearest, Units(5), 0);
                    if (t >= 0)
                    {
                        var e = Enemies[t];
                        long dx = e.X - f.X, dy = e.Y - f.Y, d = HordeMove.ISqrt(dx * dx + dy * dy);
                        if (d > 8)
                        {
                            int sp = Math.Max(1, Units(3.2 / TickRate));
                            f.X = Clamp(f.X + (int)(dx * sp / d), MinX, MaxX);
                            f.Y = Clamp(f.Y + (int)(dy * sp / d), MinY, MaxY);
                        }
                    }
                }
                return;
            }
            if (tk == TransKind.TrapCluster && f.Count == 0)
            {
                for (int i = 0; i < 3; i++)
                {
                    double a = i * (Math.PI * 2.0 / 3.0) + (Tick & 7) * 0.3;
                    int d = Units(1.6);
                    var m = NewFx(HordeFxKind.Trap);
                    m.OwnerSlot = p.Slot; m.DefId = f.DefId; m.Level = f.Level;
                    m.X = Clamp(f.X + (int)Math.Round(Math.Cos(a) * d), MinX, MaxX);
                    m.Y = Clamp(f.Y + (int)Math.Round(Math.Sin(a) * d), MinY, MaxY);
                    m.Radius = Math.Max(Units(0.8), (int)Math.Round(f.Radius * 0.7));
                    m.ArmTick = Tick + Ticks(0.35);
                    m.EndTick = Tick + DurationTicks(p, 10f);
                    m.Weapon = f.Weapon; m.Lv = f.Lv; m.BaseDamage = Math.Max(1, f.BaseDamage * 0.55);
                    m.Count = 1;
                    BeginFx(m);
                }
            }
        }

        private void OnTransDashStart(SimPlayer p, OwnedWeapon ow, int x0, int y0, int x1, int y1, int ticks)
        {
            var tk = ow.Def.Trans;
            var lv = ow.Lv;
            if (tk == TransKind.DashMirage)
            {
                var z = NewFx(HordeFxKind.Zone);
                z.OwnerSlot = p.Slot; z.DefId = ow.Def.Id; z.Level = ow.Level;
                z.X = (x0 + x1) / 2; z.Y = (y0 + y1) / 2;
                z.Radius = AreaUnits(p, Math.Max(1.8f, lv.Area));
                z.Every = Math.Max(1, Ticks(0.4)); z.NextTick = Tick + 1; z.EndTick = Tick + DurationTicks(p, 2.5f);
                z.Weapon = ow; z.Lv = lv; z.BaseDamage = Math.Max(45, lv.Damage * 0.65);
                BeginFx(z);
            }
            else if (tk == TransKind.DashNova)
            {
                int rad = AreaUnits(p, 2.6f);
                double dmg = Math.Max(65, lv.Damage);
                StrikeArea(p, ow, lv, x0, y0, rad, dmg);
                var ts = TS(p);
                ts.DashEndTick = Tick + ticks;
                ts.DashEndDmg = (int)Math.Round(dmg);
                ts.DashEndWeapon = ow;
            }
        }

        private void OnTransParried(SimPlayer p, OwnedWeapon ow, WeaponLevel lv, int attX, int attY)
        {
            var tk = ow.Def.Trans;
            if (tk == TransKind.ParryTempest)
            {
                int rad = AreaUnits(p, Math.Max(2.8f, lv.Area));
                StrikeArea(p, ow, lv, p.X, p.Y, rad, Math.Max(90, lv.Damage));
            }
            else if (tk == TransKind.ParryPrism)
            {
                double baseAng = Math.Atan2(attY - p.Y, attX - p.X);
                int sp = Math.Max(1, Units(11.0 / TickRate));
                for (int i = -1; i <= 1; i++)
                {
                    var shot = ShootFrom(p, ow, lv, baseAng + i * 0.25, sp, Units(9), -1, p.X, p.Y);
                    shot.BaseDamage = Math.Max(75, lv.Damage);
                    shot.PierceLeft = 3;
                }
            }
        }

        private void OnTransPetAct(SimPlayer owner, SimSummon s, int tx, int ty)
        {
            var w = s.Weapon.Def;
            if (w.Trans != TransKind.PetApex) return;
            var ts = TS(owner);
            if (++ts.PetActs >= 3)
            {
                ts.PetActs = 0;
                var lv = s.Weapon.Lv;
                int rad = AreaUnits(owner, 2.6f);
                double dmg = Math.Max(85, lv.Damage * 1.8);
                StrikeArea(owner, s.Weapon, lv, tx, ty, rad, dmg);
            }
        }

        private void OnTransRandomCast(SimPlayer p, OwnedWeapon ow)
        {
            var tk = ow.Def.Trans;
            if (tk == TransKind.RandomTwin)
            {
                var list = ow.Def.RandomOf;
                if (list != null && list.Length > 0)
                {
                    var def = C.Weapon(list[_rng.Next(list.Length)]);
                    if (def != null && def.Levels.Length > 0 && def.RandomOf.Length == 0)
                    {
                        var temp = new OwnedWeapon { Def = def, Level = def.Levels.Length, NextTick = Tick };
                        FireOnce(p, temp, temp.Lv, true);
                    }
                }
            }
            else if (tk == TransKind.RandomOverload)
            {
                int rad = AreaUnits(p, 2.8f);
                StrikeArea(p, ow, ow.Lv, p.X, p.Y, rad, 95);
            }
        }

        // ================================================================================================= passive keystones

        private void StepTransPlayer(SimPlayer p)
        {
            var ts = TS(p);
            if (ts.DashEndTick == Tick && ts.DashEndWeapon != null)
            {
                int rad = AreaUnits(p, 2.6f);
                StrikeArea(p, ts.DashEndWeapon, ts.DashEndWeapon.Lv, p.X, p.Y, rad, ts.DashEndDmg);
                ts.DashEndWeapon = null;
            }

            // VitalFortress ("Immortal Bastion"): every 4s pulses a Max-HP-scaling shockwave and heals
            int fort = CountPassiveTrans(p, PassiveTransKind.VitalFortress);
            if (fort > 0 && Tick >= ts.FortressNext)
            {
                ts.FortressNext = Tick + Ticks(4.0);
                int h = Math.Max(2, p.MaxHp * (3 * fort) / 100);
                int before = p.Hp; p.Hp = Math.Min(p.MaxHp, p.Hp + h);
                if (p.Hp > before) AddEvent(HordeEventKind.Heal, HordeEventFlags.None, p.Slot, p.Slot, 0, p.Hp - before, p.X, p.Y);
                int rad = AreaUnits(p, 2.8f);
                double dmg = Math.Max(35, p.MaxHp * 0.15 * fort);
                _q.Clear(); _grid.Query(p.X, p.Y, rad + Units(1.5), Enemies, _q);
                foreach (int i in _q.ToArray())
                {
                    var e = Enemies[i]; int rr = rad + e.Radius;
                    if (e.Alive && Dist2(p.X, p.Y, e.X, e.Y) <= (long)rr * rr)
                        ProcStrike(p, e, dmg, DamageClass.Melee, p.X, p.Y);
                }
                AddEvent(HordeEventKind.Nova, HordeEventFlags.None, p.Slot, -1, 0, fort, p.X, p.Y, rad / (float)U);
            }

            // Windrunner ("Tempest Stride"): moving leaves sparking wind trails
            int wind = CountPassiveTrans(p, PassiveTransKind.Windrunner);
            if (wind > 0 && p.Moving && Tick >= ts.WindTrailNext)
            {
                ts.WindTrailNext = Tick + Ticks(0.6);
                int rad = AreaUnits(p, 1.6f);
                double dmg = 45 * wind;
                _q.Clear(); _grid.Query(p.X, p.Y, rad + Units(1.5), Enemies, _q);
                foreach (int i in _q.ToArray())
                {
                    var e = Enemies[i]; int rr = rad + e.Radius;
                    if (!e.Alive || Dist2(p.X, p.Y, e.X, e.Y) > (long)rr * rr) continue;
                    if (!ProcStrike(p, e, dmg, DamageClass.Magic, p.X, p.Y) && e.Alive && e.Def.EffectResist < 100)
                    {
                        e.SlowUntil = Math.Max(e.SlowUntil, Tick + Ticks(1.5));
                        e.SlowPct = Math.Max(e.SlowPct, 35);
                    }
                }
                AddEvent(HordeEventKind.Nova, HordeEventFlags.None, p.Slot, -1, 0, wind, p.X, p.Y, rad / (float)U);
            }

            // Archmage ("Arcane Convergence"): every 3.5s erupts an arcane nova and reduces all weapon cooldowns
            int arch = CountPassiveTrans(p, PassiveTransKind.Archmage);
            if (arch > 0 && Tick >= ts.ArchmageNext)
            {
                ts.ArchmageNext = Tick + Ticks(3.5);
                int cdSave = Ticks(0.35 * arch);
                foreach (var ow in p.Weapons)
                    if (ow.NextTick > Tick) ow.NextTick = Math.Max(Tick, ow.NextTick - cdSave);
                int rad = AreaUnits(p, 3.2f);
                double dmg = 90 * arch;
                _q.Clear(); _grid.Query(p.X, p.Y, rad + Units(1.5), Enemies, _q);
                foreach (int i in _q.ToArray())
                {
                    var e = Enemies[i]; int rr = rad + e.Radius;
                    if (e.Alive && Dist2(p.X, p.Y, e.X, e.Y) <= (long)rr * rr)
                        ProcStrike(p, e, dmg, DamageClass.Magic, p.X, p.Y);
                }
                AddEvent(HordeEventKind.Nova, HordeEventFlags.None, p.Slot, -1, 0, arch, p.X, p.Y, rad / (float)U);
            }
        }

        private double TransDamageMul(SimPlayer p, WeaponDef w, SimEnemy e)
        {
            if (e == null) return 1.0;
            int slayer = CountPassiveTrans(p, PassiveTransKind.Slayer);
            if (slayer <= 0) return 1.0;
            // Slayer ("Bane Mastery"): half of your highest racial/elemental/boss bonus applies to all foes
            double bestBane = p.S.VsBoss;
            for (int i = 0; i < 10; i++)
            {
                if (p.S.VsRace[i] > bestBane) bestBane = p.S.VsRace[i];
                if (p.S.VsElement[i] > bestBane) bestBane = p.S.VsElement[i];
            }
            return bestBane > 0 ? 1.0 + (bestBane * 0.5 * slayer) / 100.0 : 1.0 + 0.12 * slayer;
        }

        private void TransAfterHit(SimPlayer p, OwnedWeapon ow, WeaponLevel lv, SimEnemy e, int dmg, bool crit, bool died)
        {
            var ts = TS(p);
            if (ts.InChainEffect) return;
            ts.InChainEffect = true;
            try
            {
                // Warlord ("Overwhelming Force"): every 4th hit unleashes a cleave shockwave and sunders
                int warlord = CountPassiveTrans(p, PassiveTransKind.Warlord);
                if (warlord > 0 && ++ts.WarlordHits >= 4)
                {
                    ts.WarlordHits = 0;
                    int rad = AreaUnits(p, 2.4f);
                    double cleave = Math.Max(15, dmg * 0.6 * warlord);
                    _q.Clear(); _grid.Query(e.X, e.Y, rad + Units(1.5), Enemies, _q);
                    foreach (int i in _q.ToArray())
                    {
                        var o = Enemies[i]; int rr = rad + o.Radius;
                        if (!o.Alive || Dist2(e.X, e.Y, o.X, o.Y) > (long)rr * rr) continue;
                        if (o != e) DamageEnemy(o, (int)Math.Round(cleave), p.Slot, ow.Def.Id, false, false, e.X, e.Y);
                    }
                    AddEvent(HordeEventKind.Strike, HordeEventFlags.None, p.Slot, -1, ow.Def.Id, ow.Level, e.X, e.Y, rad / (float)U);
                }

                // Deadeye ("Ricochet Mastery"): every 3rd ranged hit fires a ricochet bolt to a nearby enemy
                int deadeye = CountPassiveTrans(p, PassiveTransKind.Deadeye);
                if (deadeye > 0 && ow.Def.Class == DamageClass.Ranged && ++ts.DeadeyeHits >= 3)
                {
                    ts.DeadeyeHits = 0;
                    var ex = new List<int> { e.Slot };
                    int next = FindTarget(e.X, e.Y, Targeting.Nearest, Units(6), 0, ex);
                    if (next >= 0)
                    {
                        var ne = Enemies[next];
                        AddEvent(HordeEventKind.Chain, HordeEventFlags.None, p.Slot, next, ow.Def.Id, ow.Level, e.X, e.Y, 0, 0, 0, ne.X, ne.Y);
                        DamageEnemy(ne, Math.Max(10, (int)Math.Round(dmg * 0.75 * deadeye)), p.Slot, ow.Def.Id, false, false, e.X, e.Y);
                    }
                }

                // FortuneCrit ("Fated Execution"): crits chain lightning and execute low-HP non-bosses
                int fortune = CountPassiveTrans(p, PassiveTransKind.FortuneCrit);
                if (fortune > 0)
                {
                    if (!died && e.Alive && e.Def.Rank == EnemyRank.Normal && (long)e.Hp * 100 <= (long)e.MaxHp * (12 * fortune))
                    {
                        DamageEnemy(e, e.Hp, p.Slot, ow.Def.Id, true, false, p.X, p.Y);
                        died = !e.Alive;
                    }
                    if (crit)
                    {
                        var ex = new List<int> { e.Slot };
                        int next = FindTarget(e.X, e.Y, Targeting.Nearest, Units(4.5), 0, ex);
                        if (next >= 0)
                        {
                            var ne = Enemies[next];
                            AddEvent(HordeEventKind.Chain, HordeEventFlags.None, p.Slot, next, ow.Def.Id, ow.Level, e.X, e.Y, 0, 0, 0, ne.X, ne.Y);
                            DamageEnemy(ne, Math.Max(10, (int)Math.Round(dmg * 0.6 * fortune)), p.Slot, ow.Def.Id, false, false, e.X, e.Y);
                        }
                    }
                }

                // Slayer ("Bane Mastery"): favored targets explode on hit (at most once per 0.5s)
                int slayer = CountPassiveTrans(p, PassiveTransKind.Slayer);
                if (slayer > 0 && Tick >= ts.BaneBlastNext)
                {
                    bool favored = e.Def.Rank != EnemyRank.Normal && p.S.VsBoss > 0
                        || p.S.VsRace[(int)e.Def.Race % 10] > 0
                        || p.S.VsElement[(int)e.Def.Element % 10] > 0;
                    if (favored)
                    {
                        ts.BaneBlastNext = Tick + Ticks(0.5);
                        int rad = AreaUnits(p, 2.0f);
                        int bd = Math.Max(12, (int)Math.Round(dmg * 0.55 * slayer));
                        _q.Clear(); _grid.Query(e.X, e.Y, rad + Units(1.5), Enemies, _q);
                        foreach (int i in _q.ToArray())
                        {
                            var o = Enemies[i]; int rr = rad + o.Radius;
                            if (o.Alive && o != e && Dist2(e.X, e.Y, o.X, o.Y) <= (long)rr * rr)
                                DamageEnemy(o, bd, p.Slot, ow.Def.Id, false, false, e.X, e.Y);
                        }
                        AddEvent(HordeEventKind.Strike, HordeEventFlags.None, p.Slot, -1, ow.Def.Id, ow.Level, e.X, e.Y, rad / (float)U);
                    }
                }
            }
            finally
            {
                ts.InChainEffect = false;
            }
        }

        private void TransOnKill(SimEnemy e, SimPlayer killer)
        {
            if (killer == null || !killer.Present) return;
            var ts = TS(killer);
            if (ts.InChainEffect) return;
            ts.InChainEffect = true;
            try
            {
                // MarkSpread ("Contagion") & MarkDetonate ("Doom Brand")
                if (_mkSerial[e.Slot] == e.Serial + 1 && (_mkUntil[e.Slot] > Tick || _wkUntil[e.Slot] > Tick))
                {
                    foreach (var ow in killer.Weapons)
                    {
                        if (ow.Def.Trans == TransKind.MarkSpread)
                        {
                            var lv = ow.Lv;
                            int jumped = 0;
                            _q.Clear(); _grid.Query(e.X, e.Y, Units(4.5), Enemies, _q);
                            foreach (int j in _q)
                            {
                                var o = Enemies[j];
                                if (!o.Alive || o == e || MarkedNow(o)) continue;
                                MarkEnemy(killer, lv, o);
                                o.Flags |= HordeEnemyFlags.Marked;
                                AddEvent(HordeEventKind.Chain, HordeEventFlags.None, killer.Slot, j, ow.Def.Id, ow.Level, e.X, e.Y, 0, 0, 0, o.X, o.Y);
                                if (++jumped >= 2) break;
                            }
                        }
                        else if (ow.Def.Trans == TransKind.MarkDetonate)
                        {
                            var lv = ow.Lv;
                            int rad = AreaUnits(killer, 2.5f);
                            StrikeArea(killer, ow, lv, e.X, e.Y, rad, Math.Max(110, lv.Damage * 2));
                        }
                    }
                }

                // Harvester ("Soul Harvest"): every 5th kill fires homing soul bolts and heals
                int harv = CountPassiveTrans(killer, PassiveTransKind.Harvester);
                if (harv > 0 && ++ts.HarvesterKills >= 5)
                {
                    ts.HarvesterKills = 0;
                    TriggerHarvesterBolts(killer, harv, e.X, e.Y);
                }
            }
            finally
            {
                ts.InChainEffect = false;
            }
        }

        private void TransOnPickup(SimPlayer p)
        {
            int harv = CountPassiveTrans(p, PassiveTransKind.Harvester);
            if (harv > 0) TriggerHarvesterBolts(p, harv, p.X, p.Y);
        }

        private void TriggerHarvesterBolts(SimPlayer p, int harv, int fromX, int fromY)
        {
            if (!p.Fallen && p.Hp < p.MaxHp)
            {
                int h = Math.Max(1, p.MaxHp * (2 * harv) / 100);
                p.Hp = Math.Min(p.MaxHp, p.Hp + h);
            }
            var ex = new List<int>();
            for (int k = 0; k < 2; k++)
            {
                int t = FindTarget(fromX, fromY, Targeting.Nearest, Units(8), 0, ex);
                if (t < 0) break;
                ex.Add(t);
                var e = Enemies[t];
                AddEvent(HordeEventKind.Chain, HordeEventFlags.None, p.Slot, t, 0, 1, fromX, fromY, 0, 0, 0, e.X, e.Y);
                ProcStrike(p, e, 65 * harv, DamageClass.Magic, fromX, fromY);
            }
        }
    }
}
