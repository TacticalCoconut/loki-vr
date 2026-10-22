#nullable disable
using System;
using System.Collections.Generic;

namespace Loki.Horde
{
    public sealed partial class HordeSim
    {
        // =============================================================================================== modifiers

        private static void AddMod(PlayerStats s, ModDef m, double mul)
        {
            if (m == null) return;
            double v = m.Value * mul;
            int arg = m.Arg < 0 ? 0 : m.Arg % 10;
            switch (m.Kind)
            {
                case Mod.MaxHp: s.MaxHpFlat += v; break;
                case Mod.MaxHpPct: s.MaxHpPct += v; break;
                case Mod.Regen: s.Regen += v; break;
                case Mod.MoveSpeedPct: s.MovePct += v; break;
                case Mod.DamagePct: s.DamagePct += v; break;
                case Mod.MeleeDamagePct: s.MeleePct += v; break;
                case Mod.RangedDamagePct: s.RangedPct += v; break;
                case Mod.MagicDamagePct: s.MagicPct += v; break;
                case Mod.CooldownPct: s.CooldownPct += v; break;
                case Mod.AreaPct: s.AreaPct += v; break;
                case Mod.ProjectileCount: s.ProjectileCount += v; break;
                case Mod.ProjectileSpeedPct: s.ProjectileSpeedPct += v; break;
                case Mod.DurationPct: s.DurationPct += v; break;
                case Mod.PickupRadiusPct: s.PickupRadiusPct += v; break;
                case Mod.CritChance: s.Crit += v; break;
                case Mod.CritDamagePct: s.CritDamage += v; break;
                case Mod.ExpGainPct: s.ExpGainPct += v; break;
                case Mod.Defence: s.Defence += v; break;
                case Mod.DamageTakenPct: s.DamageTakenPct += v; break;
                case Mod.Dodge: s.Dodge += v; break;
                case Mod.HealPowerPct: s.HealPowerPct += v; break;
                case Mod.ReviveSpeedPct: s.ReviveSpeedPct += v; break;
                case Mod.Str: s.StatsAdded[0] += v; break;
                case Mod.Agi: s.StatsAdded[1] += v; break;
                case Mod.Vit: s.StatsAdded[2] += v; break;
                case Mod.Int: s.StatsAdded[3] += v; break;
                case Mod.Dex: s.StatsAdded[4] += v; break;
                case Mod.Luk: s.StatsAdded[5] += v; break;
                case Mod.DamageVsRacePct: s.VsRace[arg] += v; break;
                case Mod.DamageVsElementPct: s.VsElement[arg] += v; break;
                case Mod.ElementDamagePct: s.ElementDamage[arg] += v; break;
                case Mod.DamageVsBossPct: s.VsBoss += v; break;
                case Mod.EffectChancePct: s.EffectChance += v; break;
                case Mod.KnockbackPct: s.KnockbackPct += v; break;
                case Mod.HpOnKill: s.HpOnKill += v; break;
            }
        }

        /// <summary>Sums the player's modifiers: passives (their level's), the current job's, buffs, then its stat points
        /// through HordeContent.Stats; max HP and walking speed follow.</summary>
        private void Recalc(SimPlayer p, bool init)
        {
            var s = p.S;
            s.Clear();
            s.Crit = R.CritChance;
            foreach (var op in p.Passives)
            {
                if (op.Level < 1 || op.Def.Levels.Length == 0) continue;
                var mods = op.Def.Levels[Math.Min(op.Level, op.Def.Levels.Length) - 1];
                if (mods != null) foreach (var m in mods) AddMod(s, m, 1);
            }
            var job = C.Job(p.JobId);
            if (job?.Mods != null) foreach (var m in job.Mods) AddMod(s, m, 1);
            foreach (var b in p.Buffs) if (b.Mods != null) foreach (var m in b.Mods) AddMod(s, m, 1);
            foreach (var sd in C.Stats)
            {
                int i = (int)sd.Stat;
                if (i < 0 || i >= 6 || sd.PerPoint == null) continue;
                double pts = p.StatPoints[i] + s.StatsAdded[i];
                if (pts == 0) continue;
                foreach (var m in sd.PerPoint) if (m != null && (m.Kind < Mod.Str || m.Kind > Mod.Luk)) AddMod(s, m, pts);
            }
            int oldMax = p.MaxHp;
            p.MaxHp = Math.Max(1, (int)Math.Round((R.StartHp + R.HpPerLevel * (p.Level - 1) + s.MaxHpFlat) * Math.Max(0.1, 1 + s.MaxHpPct / 100.0)));
            if (!init && p.MaxHp > oldMax && !p.Fallen) p.Hp = Math.Min(p.MaxHp, p.Hp + (p.MaxHp - oldMax));
            if (p.Hp > p.MaxHp) p.Hp = p.MaxHp;
            p.SpeedPerTick = Math.Max(1, (int)Math.Round(R.MoveSpeed * Math.Max(0.2, 1 + s.MovePct / 100.0) * U / TickRate));
            // the ramp (client wave 9): the client derives the same two integers from the speed it is sent (HordeClient.Step)
            p.AccelPerTick = HordeMove.RampPerTick(p.SpeedPerTick, R.MoveAccelSeconds);
            p.DecelPerTick = HordeMove.RampPerTick(p.SpeedPerTick, R.MoveDecelSeconds);
            RecalcCards(p); // client wave 17 (h17cards): the new modifiers (HordeSim.Cards.cs)
        }

        private int CooldownTicks(SimPlayer p, WeaponLevel lv) => Math.Max(1, Ticks(lv.Cooldown * Math.Max(0.25, 1 + p.S.CooldownPct / 100.0)));
        private int AreaUnits(SimPlayer p, float cells) => Math.Max(1, Units(cells * Math.Max(0.2, 1 + p.S.AreaPct / 100.0)));
        private int DurationTicks(SimPlayer p, float seconds) => Math.Max(1, Ticks(seconds * Math.Max(0.2, 1 + p.S.DurationPct / 100.0)));
        private int CountOf(SimPlayer p, WeaponLevel lv, bool counts) => Math.Max(1, lv.Count + (counts ? (int)Math.Floor(p.S.ProjectileCount) : 0));
        /// <summary>Client wave 17 (h17cards): the level of the weapon whose count this one adds (Finger Offensive + Spirit Spheres), 0 none.</summary>
        private int BonusCount(SimPlayer p, WeaponDef w)
        {
            if (w.BonusCountFrom <= 0) return 0;
            foreach (var o in p.Weapons) if (RootId(o.Def) == w.BonusCountFrom) return o.Level;
            return 0;
        }

        // =============================================================================================== aiming

        private readonly List<int> _q = new List<int>(64);

        /// <summary>The enemy slot a weapon aims at, or -1 (none within range).</summary>
        private int FindTarget(int x, int y, Targeting t, int range, int area, List<int> exclude = null)
        {
            _q.Clear(); _grid.Query(x, y, range, Enemies, _q);
            int best = -1; long bestKey = 0;
            long r2 = (long)range * range;
            int seen = 0;
            foreach (int i in _q)
            {
                var e = Enemies[i];
                if (exclude != null && exclude.Contains(i)) continue;
                long d = Dist2(x, y, e.X, e.Y);
                if (d > r2) continue;
                long key;
                switch (t)
                {
                    case Targeting.Random: seen++; if (_rng.Next(seen) == 0) best = i; continue;
                    case Targeting.Strongest: key = -((long)e.Hp << 20) + d / 1024; break;
                    case Targeting.Farthest: key = -d; break;
                    case Targeting.Densest:
                    {
                        int near = 0; long a2 = (long)area * area;
                        foreach (int j in _q) if (j != i && Dist2(e.X, e.Y, Enemies[j].X, Enemies[j].Y) <= a2) near++;
                        key = -((long)near << 32) + d / 1024; break;
                    }
                    default: key = d; break;
                }
                if (best < 0 || key < bestKey) { best = i; bestKey = key; }
            }
            return best;
        }

        private SimPlayer LowestAlly(SimPlayer p, int range)
        {
            SimPlayer best = null; double bestF = 2;
            foreach (var q in Players)
            {
                if (!q.Present || q.Fallen || Dist2(q.X, q.Y, p.X, p.Y) > (long)range * range) continue;
                double f = q.Hp / (double)Math.Max(1, q.MaxHp);
                if (f < bestF) { bestF = f; best = q; }
            }
            return best;
        }

        private void Heal(SimPlayer healer, SimPlayer q, WeaponLevel lv, int weaponId)
        {
            if (q == null || q.Fallen || !q.Present) return;
            double h = (lv.Heal + lv.HealPct / 100.0 * q.MaxHp) * Math.Max(0, 1 + healer.S.HealPowerPct / 100.0);
            int amount = (int)Math.Round(h);
            if (amount <= 0) return;
            int before = q.Hp; q.Hp = Math.Min(q.MaxHp, q.Hp + amount);
            int got = q.Hp - before;
            if (got <= 0) return;
            if (q != healer) healer.Healing += got;
            AddEvent(HordeEventKind.Heal, HordeEventFlags.None, q.Slot, healer.Slot, weaponId, got, q.X, q.Y);
            CardsHealed(healer, q); // client wave 17 (h17cards): Aspersio
        }

        // =============================================================================================== weapons

        /// <summary>True for the curated set of high-impact active skills that can be aimed with a hand laser and fired with trigger (max 2 per player: 1 per hand).</summary>
        public static bool IsActiveWeapon(int id)
        {
            int root = id >= 6000 ? id - 6000 : id >= 5000 ? id - 5000 : id >= 3000 ? id - 3000 : id >= 2000 ? id - 2000 : id >= 1000 ? id - 1000 : id;
            return root == 22   // Fire Wall (30s CD: wide blocking flame barrier + knockback shockwave)
                || root == 28   // Safety Wall (30s CD: 3.5s invulnerability + 25% HP heal + sanctuary barrier)
                || root == 45   // Pneuma (30s CD: 3.5s invulnerability + 25% HP heal + wind ward)
                || root == 111  // Grand Cross (45s CD: massive holy cross + 3.5s invulnerability + 25% HP heal)
                || root == 120  // Storm Gust (40s CD: blizzard + 3s AoE freeze & knockback)
                || root == 121  // Meteor Storm (40s CD: cataclysmic meteor barrage + 2.5s AoE stun)
                || root == 123  // Lord of Vermilion (40s CD: wide lightning tempest + 2.5s AoE stun)
                || root == 170  // Sanctuary (45s CD: 3.5s invulnerability + 25% HP heal + giant healing zone)
                || root == 183; // Asura Strike (60s CD: 5x damage supreme nuke + 2.5s AoE stun & knockback)
        }

        /// <summary>Dedicated manual trigger cooldown (30–60s) for active skills.</summary>
        public static float ActiveCooldownSeconds(int id)
        {
            int root = id >= 6000 ? id - 6000 : id >= 5000 ? id - 5000 : id >= 3000 ? id - 3000 : id >= 2000 ? id - 2000 : id >= 1000 ? id - 1000 : id;
            switch (root)
            {
                case 22:
                case 28:
                case 45:
                    return 30f;
                case 120:
                case 121:
                case 123:
                    return 40f;
                case 111:
                case 170:
                    return 45f;
                case 183:
                    return 60f;
                default:
                    return 35f;
            }
        }

        private int ActiveCooldownTicks(SimPlayer p, int weaponId)
        {
            double baseSec = ActiveCooldownSeconds(weaponId);
            double mul = Math.Max(0.45, 1.0 + p.S.CooldownPct / 100.0);
            return Math.Max(Ticks(20.0), Ticks(baseSec * mul));
        }

        private readonly bool[] _manualCastUsed = new bool[HordeLimits.MaxPlayers];

        /// <summary>Casts an owned active skill at map-unit coordinates (targetX, targetY) when triggered by the player's hand laser + trigger.</summary>
        public bool CastActive(int slot, int weaponId, int targetX, int targetY)
        {
            var p = Player(slot);
            if (p == null || !p.Present || p.Fallen || Paused) return false;
            _manualCastUsed[slot] = true;
            int wantRoot = weaponId >= 6000 ? weaponId - 6000 : weaponId >= 5000 ? weaponId - 5000 : weaponId >= 3000 ? weaponId - 3000 : weaponId >= 2000 ? weaponId - 2000 : weaponId >= 1000 ? weaponId - 1000 : weaponId;
            OwnedWeapon ow = null;
            foreach (var w in p.Weapons)
                if (w.Def.Id == weaponId || RootId(w.Def) == wantRoot) { ow = w; break; }
            if (ow == null || ow.Def.Levels.Length == 0 || Tick < ow.ActiveNextTick) return false;
            var lv = ow.Lv;
            int tx = Clamp(targetX, MinX, MaxX), ty = Clamp(targetY, MinY, MaxY);
            var def = ow.Def;
            int root = RootId(def);
            int baseArea = AreaUnits(p, lv.Area > 0 ? lv.Area : 2.2f);
            int area = Math.Max(Units(1.8), baseArea * 145 / 100);
            double dmgMul = root == 183 ? 5.0 : 3.5; // High-impact ultimate multiplier (5x for Asura Strike, 3.5x for others)
            double boostDmg = Math.Max(35.0, lv.Damage) * dmgMul;

            // Defensive / Holy ultimates (Safety Wall 28, Pneuma 45, Grand Cross 111, Sanctuary 170) grant 3.5s invulnerability + instant 25% MaxHP heal
            if (root == 28 || root == 45 || root == 111 || root == 170)
            {
                p.SafeUntil = Math.Max(p.SafeUntil, Tick + Ticks(3.5));
                int healAmt = Math.Max(1, p.MaxHp * 25 / 100);
                int before = p.Hp;
                p.Hp = Math.Min(p.MaxHp, before + healAmt);
                int gained = p.Hp - before;
                if (gained > 0)
                {
                    p.Healing += gained;
                    AddEvent(HordeEventKind.Heal, HordeEventFlags.None, p.Slot, p.Slot, def.Id, gained, p.X, p.Y);
                }
            }

            switch (def.Kind)
            {
                case WeaponKind.GroundZone:
                {
                    int n = Math.Max(2, CountOf(p, lv, true));
                    double ang = Math.Atan2(ty - p.Y, tx - p.X);
                    double perp = ang + Math.PI * 0.5;
                    int durTicks = DurationTicks(p, (lv.Duration > 0 ? lv.Duration : 5f) * 1.75f);
                    if ((def.Zone & ZoneFlags.BlocksEnemies) != 0)
                    {
                        var f = NewFx(HordeFxKind.Zone);
                        f.OwnerSlot = p.Slot; f.DefId = def.Id; f.Level = ow.Level; f.X = tx; f.Y = ty;
                        f.Radius = Math.Max(Units(0.65), area * 65 / 100);
                        f.Range = Math.Max(Units(2.2), area * Math.Max(3, n) * 52 / 100);
                        f.Angle = Math.Round(perp * 180.0 / Math.PI);
                        f.Every = Math.Max(1, Ticks(lv.Tick)); f.NextTick = Tick + 1; f.EndTick = Tick + durTicks;
                        f.Weapon = ow; f.Lv = lv; f.BaseDamage = boostDmg; f.Heal = lv.Heal * 2; f.HealPct = lv.HealPct * 2f; f.ZoneFlags = def.Zone;
                        f.Follows = false;
                        BeginFx(f);
                    }
                    else
                    {
                        for (int i = 0; i < n; i++)
                        {
                            double off = (i - (n - 1) * 0.5) * area * 1.05;
                            int zx = Clamp(tx + (int)Math.Round(Math.Cos(perp) * off), MinX, MaxX);
                            int zy = Clamp(ty + (int)Math.Round(Math.Sin(perp) * off), MinY, MaxY);
                            var f = NewFx(HordeFxKind.Zone);
                            f.OwnerSlot = p.Slot; f.DefId = def.Id; f.Level = ow.Level; f.X = zx; f.Y = zy; f.Radius = area;
                            f.Every = Math.Max(1, Ticks(lv.Tick)); f.NextTick = Tick + 1; f.EndTick = Tick + durTicks;
                            f.Weapon = ow; f.Lv = lv; f.BaseDamage = boostDmg; f.Heal = Math.Max(lv.Heal * 2, root == 170 ? 25 : 0); f.HealPct = Math.Max(lv.HealPct * 2f, root == 170 ? 3f : 0f); f.ZoneFlags = def.Zone;
                            f.Follows = false;
                            BeginFx(f);
                        }
                    }
                    break;
                }
                case WeaponKind.AreaAtTarget:
                {
                    int n = Math.Max(3, CountOf(p, lv, true) + 1);
                    for (int i = 0; i < n; i++)
                    {
                        double a = i * Math.PI * 2.0 / Math.Max(1, n);
                        double r = i == 0 ? 0 : area * 0.55;
                        int sx = Clamp(tx + (int)Math.Round(Math.Cos(a) * r), MinX, MaxX);
                        int sy = Clamp(ty + (int)Math.Round(Math.Sin(a) * r), MinY, MaxY);
                        var f = NewFx(HordeFxKind.Warning);
                        f.OwnerSlot = p.Slot; f.DefId = def.Id; f.Level = ow.Level; f.X = sx; f.Y = sy; f.Radius = area;
                        f.EndTick = Tick + Math.Max(1, Ticks(Math.Min(0.25f, lv.Delay) + i * 0.10f)); f.Weapon = ow; f.Lv = lv; f.BaseDamage = boostDmg;
                        BeginFx(f);
                    }
                    break;
                }
                default:
                {
                    StrikeArea(p, ow, lv, tx, ty, area, boostDmg);
                    break;
                }
            }

            // High-impact crowd-control shockwave at target cell on every active skill cast
            long ccR = (long)area * 135 / 100, ccR2 = ccR * ccR;
            int ccEnd = Tick + Ticks(root == 120 ? 3.0 : 2.5);
            int v0 = HordeMove.ImpulseFor(Units(2.5), _kbKeepPct);
            foreach (var e in Enemies)
            {
                if (!e.Alive) continue;
                long d2 = Dist2(tx, ty, e.X, e.Y);
                if (d2 > ccR2) continue;
                if (root == 120) e.FrozenUntil = Math.Max(e.FrozenUntil, ccEnd);
                else e.StunUntil = Math.Max(e.StunUntil, ccEnd);
                double resist = Math.Max(0.0, 1.0 - e.Def.KnockbackResist / 100.0);
                if (resist > 0.0 && CanKnockback(e))
                {
                    long dx = e.X - tx, dy = e.Y - ty;
                    if (dx == 0 && dy == 0) dx = 1;
                    long d = Math.Max(1, HordeMove.ISqrt(dx * dx + dy * dy));
                    long imp = (long)(v0 * resist);
                    e.KbVx = Clamp(e.KbVx + (int)(dx * imp / d), -8 * U, 8 * U);
                    e.KbVy = Clamp(e.KbVy + (int)(dy * imp / d), -8 * U, 8 * U);
                }
            }

            ow.ActiveNextTick = Tick + ActiveCooldownTicks(p, def.Id);
            ow.NextTick = Tick + CooldownTicks(p, ow, lv);
            return true;
        }

        private void StepWeapons()
        {
            foreach (var p in Players)
            {
                if (!p.Present || p.Fallen) continue;
                foreach (var ow in p.Weapons) StepWeapon(p, ow);
                foreach (var ow in HiddenWeapons(p)) StepWeapon(p, ow); // client wave 17 (h17cards): the weapons a passive gives (the falcon)
            }
            StepSummons();
        }

        private void StepWeapon(SimPlayer p, OwnedWeapon ow)
        {
            if (ow.Def.Levels.Length == 0) return;
            var lv = ow.Lv;
            if (ow.BurstLeft > 0 && Tick >= ow.BurstNext)
            {
                ow.BurstLeft--; ow.BurstNext = Tick + Math.Max(1, Ticks(lv.Burst)); FireOnce(p, ow, lv, true);
                if (ow.BurstLeft == 0) CardsCombo(p, ow); // client wave 17 (h17cards): Chain Combo's blow after Triple Attack's last
            }
            if (ow.Def.Kind == WeaponKind.Orbiter) { StepOrbiterWeapon(p, ow, lv); return; }
            if (ow.Def.CastOnHit) return; // client wave 17 (h17cards): Auto Spell casts on the other weapons' hits only
            if (YggOff(p, ow)) return; // client wave 18 (h18feel), point 37: a burnt / resting Yggdrasil Berry does nothing
            if (_manualCastUsed[p.Slot] && IsActiveWeapon(ow.Def.Id)) return; // active skill: aimed with laser + trigger
            if (Tick < ow.NextTick) return;
            if (FireOnce(p, ow, lv, false)) ow.NextTick = Tick + CooldownTicks(p, ow, lv); // client wave 17 (h17cards): the per-weapon cooldown (Sonic Acceleration)
            else ow.NextTick = Tick + 2; // nothing to aim at: look again shortly
        }

        private bool AimAngle(SimPlayer p, OwnedWeapon ow, int range, int area, out double angle, out int target)
        {
            target = -1; angle = p.FaceDeg * Math.PI / 180.0;
            var t = ow.Def.Target;
            if (t == Targeting.Facing || t == Targeting.Self) return true;
            target = FindTarget(p.X, p.Y, t == Targeting.LowestHpAlly || t == Targeting.RandomAlly ? Targeting.Nearest : t, range, area);
            if (target < 0) return false;
            var e = Enemies[target];
            angle = Math.Atan2(e.Y - p.Y, e.X - p.X);
            return true;
        }

        /// <summary>One use of a weapon (or one more of a burst). False: nothing to aim at (the cooldown does not start).</summary>
        private bool FireOnce(SimPlayer p, OwnedWeapon ow, WeaponLevel lv, bool burst)
        {
            var w = ow.Def;
            int range = Units(lv.Range > 0 ? lv.Range : 8);
            if (w.RandomOf != null && w.RandomOf.Length > 0) return CastRandom(p, ow); // client wave 17 (h17cards): Abracadabra (Auto Spell never comes here)
            switch (w.Kind)
            {
                // client wave 17 (h17cards): the new kinds (HordeSim.Cards.cs)
                case WeaponKind.Dash: return FireDash(p, ow, lv);
                case WeaponKind.Mark: return FireMark(p, ow, lv);
                case WeaponKind.Parry: return FireParry(p, ow, lv);
                case WeaponKind.Pet: return FirePet(p, ow);
                case WeaponKind.Bolt:
                {
                    if (!AimAngle(p, ow, range, AreaUnits(p, lv.Area), out double ang, out int target)) return false;
                    int count = CountOf(p, lv, true) + BonusCount(p, w); // client wave 17 (h17cards): Finger Offensive throws the Spirit Spheres
                    int speed = Math.Max(1, Units(lv.Speed * Math.Max(0.2, 1 + p.S.ProjectileSpeedPct / 100.0) / TickRate));
                    if (FireTransBolt(p, ow, lv, range, speed, count, ang, target, burst)) return true; // client wave 18 (h18trans): point 30
                    if (lv.Spread > 0 || burst)
                    {
                        int n = burst ? 1 : count;
                        double spread = lv.Spread * Math.PI / 180.0;
                        for (int i = 0; i < n; i++) Shoot(p, ow, lv, ang + (i - (n - 1) / 2.0) * spread, speed, range, target);
                    }
                    else
                    {
                        Shoot(p, ow, lv, ang, speed, range, target);
                        if (count > 1) { ow.BurstLeft = count - 1; ow.BurstNext = Tick + Math.Max(1, Ticks(lv.Burst)); }
                    }
                    return true;
                }
                case WeaponKind.MeleeArc:
                {
                    int reach = AreaUnits(p, lv.Range > 0 ? lv.Range : 2);
                    if (!AimAngle(p, ow, reach + Units(1), 0, out double ang, out _)) return false;
                    double half = Math.Min(360.0, (lv.Arc > 0 ? lv.Arc : 90) * Math.Max(0.2, 1 + CS(p).M.ArcPct / 100.0)) * Math.PI / 360.0; // client wave 17 (h17cards): wider swings (Two-Hand Sword Mastery)
                    double baseDmg = lv.Damage;
                    _q.Clear(); _grid.Query(p.X, p.Y, reach + Units(1.5), Enemies, _q);
                    foreach (int i in _q.ToArray())
                    {
                        var e = Enemies[i];
                        if (!e.Alive) continue;
                        int rr = reach + e.Radius;
                        if (Dist2(p.X, p.Y, e.X, e.Y) > (long)rr * rr) continue;
                        double a = Math.Atan2(e.Y - p.Y, e.X - p.X) - ang;
                        while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI;
                        if (Math.Abs(a) > half && half < Math.PI) continue;
                        HitEnemy(p, ow, lv, e, baseDmg, p.X, p.Y);
                    }
                    AddEvent(HordeEventKind.Swing, HordeEventFlags.None, p.Slot, -1, w.Id, ow.Level, p.X, p.Y, (float)(ang * 180 / Math.PI), (float)(half * 360 / Math.PI), reach / (float)U);
                    OnTransMeleeSwing(p, ow, lv, ang, reach); // client wave 18 (h18trans): point 30
                    if (!burst && CountOf(p, lv, false) > 1) { ow.BurstLeft = CountOf(p, lv, false) - 1; ow.BurstNext = Tick + Math.Max(1, Ticks(lv.Burst)); }
                    return true;
                }
                case WeaponKind.Nova:
                {
                    int area = AreaUnits(p, lv.Area > 0 ? lv.Area : 2);
                    _q.Clear(); _grid.Query(p.X, p.Y, area + Units(1.5), Enemies, _q);
                    foreach (int i in _q.ToArray())
                    {
                        var e = Enemies[i];
                        int rr = area + e.Radius;
                        if (e.Alive && Dist2(p.X, p.Y, e.X, e.Y) <= (long)rr * rr) HitEnemy(p, ow, lv, e, lv.Damage, p.X, p.Y);
                    }
                    AddEvent(HordeEventKind.Nova, HordeEventFlags.None, p.Slot, -1, w.Id, ow.Level, p.X, p.Y, area / (float)U);
                    OnTransNova(p, ow, lv, area); // client wave 18 (h18trans): point 30
                    if (!burst && CountOf(p, lv, false) > 1) { ow.BurstLeft = CountOf(p, lv, false) - 1; ow.BurstNext = Tick + Math.Max(1, Ticks(lv.Burst)); }
                    return true;
                }
                case WeaponKind.AreaAtTarget:
                {
                    int area = AreaUnits(p, lv.Area > 0 ? lv.Area : 1.5f);
                    int target = FindTarget(p.X, p.Y, w.Target == Targeting.Self ? Targeting.Nearest : w.Target, range, area);
                    if (target < 0 && w.Target != Targeting.Self) return false;
                    int tx = target >= 0 ? Enemies[target].X : p.X, ty = target >= 0 ? Enemies[target].Y : p.Y;
                    var f = NewFx(HordeFxKind.Warning);
                    f.OwnerSlot = p.Slot; f.DefId = w.Id; f.Level = ow.Level; f.X = tx; f.Y = ty; f.Radius = area;
                    f.EndTick = Tick + Math.Max(1, Ticks(lv.Delay)); f.Weapon = ow; f.Lv = lv; f.BaseDamage = lv.Damage;
                    BeginFx(f);
                    int n = CountOf(p, lv, true);
                    if (!burst && n > 1) { ow.BurstLeft = n - 1; ow.BurstNext = Tick + Math.Max(1, Ticks(lv.Burst)); }
                    return true;
                }
                case WeaponKind.GroundZone:
                {
                    int area = AreaUnits(p, lv.Area > 0 ? lv.Area : 1.5f);
                    int n = burst ? 1 : CountOf(p, lv, true);
                    bool any = false;
                    if ((w.Zone & ZoneFlags.BlocksEnemies) != 0 && w.Target != Targeting.Self && (w.Zone & ZoneFlags.FollowsOwner) == 0)
                    {
                        int target = FindTarget(p.X, p.Y, w.Target == Targeting.LowestHpAlly || w.Target == Targeting.RandomAlly ? Targeting.Nearest : w.Target, range, area);
                        if (target < 0) return false;
                        int tx0 = Enemies[target].X, ty0 = Enemies[target].Y;
                        double ang = Math.Atan2(ty0 - p.Y, tx0 - p.X);
                        double perp = ang + Math.PI * 0.5;
                        var f = NewFx(HordeFxKind.Zone);
                        f.OwnerSlot = p.Slot; f.DefId = w.Id; f.Level = ow.Level; f.X = tx0; f.Y = ty0;
                        f.Radius = Math.Max(Units(0.45), area * 55 / 100);
                        f.Range = Math.Max(Units(1.4), area * Math.Max(2, n) * 42 / 100);
                        f.Angle = Math.Round(perp * 180.0 / Math.PI);
                        f.Every = Math.Max(1, Ticks(lv.Tick)); f.NextTick = Tick + 1; f.EndTick = Tick + DurationTicks(p, lv.Duration > 0 ? lv.Duration : 3);
                        f.Weapon = ow; f.Lv = lv; f.BaseDamage = lv.Damage; f.Heal = lv.Heal; f.HealPct = lv.HealPct; f.ZoneFlags = w.Zone;
                        f.Follows = false;
                        BeginFx(f);
                        return true;
                    }
                    var used = new List<int>();
                    for (int i = 0; i < n; i++)
                    {
                        int tx = p.X, ty = p.Y;
                        if (w.Target != Targeting.Self && (w.Zone & ZoneFlags.FollowsOwner) == 0)
                        {
                            int target = FindTarget(p.X, p.Y, w.Target == Targeting.LowestHpAlly || w.Target == Targeting.RandomAlly ? Targeting.Nearest : w.Target, range, area, used);
                            if (target < 0) { if (i == 0) return false; break; }
                            used.Add(target); tx = Enemies[target].X; ty = Enemies[target].Y;
                        }
                        var f = NewFx(HordeFxKind.Zone);
                        f.OwnerSlot = p.Slot; f.DefId = w.Id; f.Level = ow.Level; f.X = tx; f.Y = ty; f.Radius = area;
                        f.Every = Math.Max(1, Ticks(lv.Tick)); f.NextTick = Tick + 1; f.EndTick = Tick + DurationTicks(p, lv.Duration > 0 ? lv.Duration : 3);
                        f.Weapon = ow; f.Lv = lv; f.BaseDamage = lv.Damage; f.Heal = lv.Heal; f.HealPct = lv.HealPct; f.ZoneFlags = w.Zone;
                        f.Follows = (w.Zone & ZoneFlags.FollowsOwner) != 0;
                        BeginFx(f); any = true;
                    }
                    return any;
                }
                case WeaponKind.Chain:
                {
                    int first = FindTarget(p.X, p.Y, w.Target == Targeting.Self || w.Target == Targeting.Facing ? Targeting.Nearest : w.Target, range, 0);
                    if (first < 0) return false;
                    int jumps = CountOf(p, lv, true);
                    int jump = AreaUnits(p, lv.Area > 0 ? lv.Area : 3);
                    var hit = new List<int>();
                    int fx = p.X, fy = p.Y, cur = first;
                    double dmg = lv.Damage;
                    for (int k = 0; k <= jumps && cur >= 0; k++)
                    {
                        var e = Enemies[cur];
                        AddEvent(HordeEventKind.Chain, HordeEventFlags.None, p.Slot, cur, w.Id, ow.Level, fx, fy, 0, 0, 0, e.X, e.Y);
                        fx = e.X; fy = e.Y; hit.Add(cur);
                        HitEnemy(p, ow, lv, e, dmg, fx, fy);
                        dmg *= Math.Max(0, lv.Falloff) / 100.0;
                        cur = FindTarget(fx, fy, Targeting.Nearest, jump, 0, hit);
                    }
                    OnTransChain(p, ow, lv, first, jumps, jump, hit); // client wave 18 (h18trans): point 30
                    return true;
                }
                case WeaponKind.Aura:
                {
                    int area = AreaUnits(p, lv.Area > 0 ? lv.Area : 3);
                    if (lv.RevivePct > 0) return FireRevive(p, ow, lv); // client wave 17 (h17cards): Resurrection
                    if (w.Target == Targeting.LowestHpAlly || w.Target == Targeting.RandomAlly)
                    {
                        var q = LowestAlly(p, range);
                        if (q == null || q.Hp >= q.MaxHp && (lv.Buffs == null || lv.Buffs.Length == 0)) return false;
                        if (lv.BelowPct > 0 && (long)q.Hp * 100 >= (long)q.MaxHp * lv.BelowPct) return false; // client wave 17 (h17cards): Yggdrasil Berry waits for a real need
                        Heal(p, q, lv, w.Id); GiveBuff(p, q, lv, w.Id);
                        AddEvent(HordeEventKind.Nova, HordeEventFlags.None, p.Slot, q.Slot, w.Id, ow.Level, q.X, q.Y, 1f);
                        OnTransAuraPulse(p, ow, lv, area); // client wave 18 (h18trans): point 30
                        return true;
                    }
                    foreach (var q in Players)
                    {
                        if (!q.Present || q.Fallen || Dist2(q.X, q.Y, p.X, p.Y) > (long)area * area) continue;
                        Heal(p, q, lv, w.Id); GiveBuff(p, q, lv, w.Id);
                    }
                    if (lv.Damage > 0 || lv.Effects != null) // client wave 17 (h17cards): an aura with effects and no damage (Don't Forget Me, Frost Joker, Scream) applies them; it was skipped
                    {
                        _q.Clear(); _grid.Query(p.X, p.Y, area + Units(1.5), Enemies, _q);
                        foreach (int i in _q.ToArray())
                        {
                            var e = Enemies[i]; int rr = area + e.Radius;
                            if (!e.Alive || Dist2(p.X, p.Y, e.X, e.Y) > (long)rr * rr) continue;
                            if (lv.Damage > 0) HitEnemy(p, ow, lv, e, lv.Damage, p.X, p.Y); else ApplyEffects(p, ow, lv, e, p.X, p.Y);
                        }
                    }
                    AddEvent(HordeEventKind.Nova, HordeEventFlags.None, p.Slot, -1, w.Id, ow.Level, p.X, p.Y, area / (float)U);
                    OnTransAuraPulse(p, ow, lv, area); // client wave 18 (h18trans): point 30
                    if (!burst && CS(p).M.Echo > 0 && _rng.Chance(CS(p).M.Echo)) { ow.BurstLeft = 1; ow.BurstNext = Tick + Ticks(0.5); } // client wave 17 (h17cards): Encore, the aura pulses again
                    return true;
                }
                case WeaponKind.Trap:
                {
                    int max = CountOf(p, lv, true), mine = 0;
                    SimFx oldest = null;
                    foreach (var f0 in Fx) if (f0.Kind == HordeFxKind.Trap && f0.Weapon == ow && !f0.Ended) { mine++; if (oldest == null) oldest = f0; }
                    if (mine >= max && oldest != null) EndFx(oldest, false);
                    var f = NewFx(HordeFxKind.Trap);
                    var tm = CS(p).M; // client wave 17 (h17cards): Detect arms traps sooner and makes them wider
                    f.OwnerSlot = p.Slot; f.DefId = w.Id; f.Level = ow.Level; f.X = p.X; f.Y = p.Y; f.Radius = AreaUnits(p, (lv.Area > 0 ? lv.Area : 1) * (float)Math.Max(0.2, 1 + tm.TrapArea / 100.0));
                    f.ArmTick = Tick + Ticks(lv.Delay * Math.Max(0, 1 - tm.TrapArm / 100.0)); f.EndTick = Tick + DurationTicks(p, lv.Duration > 0 ? lv.Duration : 20);
                    f.Weapon = ow; f.Lv = lv; f.BaseDamage = lv.Damage;
                    BeginFx(f);
                    return true;
                }
                case WeaponKind.Summon:
                {
                    int alive = 0; foreach (var s in Summons) if (s.Weapon == ow) alive++;
                    if (alive >= CountOf(p, lv, true) || Summons.Count >= HordeLimits.MaxSummons) return true;
                    var sm = new SimSummon
                    {
                        Id = _nextSummonId, OwnerSlot = p.Slot, X = p.X, Y = p.Y, Weapon = ow, MaxHp = Math.Max(0, w.SummonHp), Hp = Math.Max(0, w.SummonHp),
                        Until = lv.Duration > 0 ? Tick + DurationTicks(p, lv.Duration) : int.MaxValue, NextAttack = Tick + 1,
                    };
                    _nextSummonId = _nextSummonId >= 65535 ? 1 : _nextSummonId + 1;
                    Summons.Add(sm);
                    AddEvent(HordeEventKind.Summon, HordeEventFlags.None, p.Slot, sm.Id, w.Id, ow.Level, p.X, p.Y);
                    return true;
                }
            }
            return false;
        }

        private void GiveBuff(SimPlayer from, SimPlayer q, WeaponLevel lv, int weaponId)
        {
            if (lv.Buffs == null || lv.Buffs.Length == 0) return;
            int until = Tick + DurationTicks(from, lv.Duration > 0 ? lv.Duration : 5);
            var b = q.Buffs.Find(x => x.WeaponId == weaponId && x.SourceSlot == from.Slot);
            if (b == null) { q.Buffs.Add(new SimBuff { Mods = lv.Buffs, Until = until, SourceSlot = from.Slot, WeaponId = weaponId }); Recalc(q, false); }
            else { bool changed = b.Mods != lv.Buffs; b.Mods = lv.Buffs; b.Until = until; if (changed) Recalc(q, false); }
        }

        private void Shoot(SimPlayer p, OwnedWeapon ow, WeaponLevel lv, double ang, int speed, int range, int target)
        {
            var f = NewFx(HordeFxKind.Shot);
            f.OwnerSlot = p.Slot; f.DefId = ow.Def.Id; f.Level = ow.Level; f.X = p.X; f.Y = p.Y;
            f.VX = (int)Math.Round(Math.Cos(ang) * speed); f.VY = (int)Math.Round(Math.Sin(ang) * speed);
            f.Radius = Units(0.3); f.EndTick = Tick + Math.Max(1, range / Math.Max(1, speed));
            f.Weapon = ow; f.Lv = lv; f.BaseDamage = lv.Damage; f.PierceLeft = lv.Pierce + CountPassiveTrans(p, PassiveTransKind.Deadeye);
            f.BurstArea = lv.Area > 0 ? AreaUnits(p, lv.Area) : 0;
            f.Target = ow.Def.Homing ? target : -1;
            BeginFx(f);
        }

        private void StepOrbiterWeapon(SimPlayer p, OwnedWeapon ow, WeaponLevel lv)
        {
            SimFx live = null;
            foreach (var f in Fx) if (f.Id == ow.OrbitFxId && f.Kind == HordeFxKind.Orbit && !f.Ended) { live = f; break; }
            if (live != null || Tick < ow.NextTick) return;
            var o = NewFx(HordeFxKind.Orbit);
            o.OwnerSlot = p.Slot; o.DefId = ow.Def.Id; o.Level = ow.Level; o.X = p.X; o.Y = p.Y;
            o.Count = CountOf(p, lv, true); o.Range = AreaUnits(p, lv.Range > 0 ? lv.Range : 2); o.Radius = AreaUnits(p, lv.Area > 0 ? lv.Area : 0.5f);
            o.Turns = lv.Speed > 0 ? lv.Speed : 0.5; o.Angle = 0; o.Every = Math.Max(1, Ticks(lv.Tick));
            o.EndTick = lv.Duration > 0 ? Tick + DurationTicks(p, lv.Duration) : int.MaxValue;
            o.Weapon = ow; o.Lv = lv; o.BaseDamage = lv.Damage; o.Follows = true;
            ow.OrbitFxId = o.Id; ow.OrbHits.Clear();
            BeginFx(o);
        }

        /// <summary>A weapon whose level changed: its orbit (if any) is made again with the new numbers.</summary>
        private void RestartOrbit(SimPlayer p, OwnedWeapon ow)
        {
            if (ow.Def.Kind != WeaponKind.Orbiter) return;
            foreach (var f in Fx) if (f.Id == ow.OrbitFxId && !f.Ended) { EndFx(f, false); break; }
            ow.NextTick = Tick + 1;
        }

        private void StepSummons()
        {
            for (int i = Summons.Count - 1; i >= 0; i--)
            {
                var s = Summons[i];
                var owner = Players[s.OwnerSlot];
                if (!owner.Present || Tick >= s.Until || (s.MaxHp > 0 && s.Hp <= 0))
                {
                    Summons.RemoveAt(i);
                    AddEvent(HordeEventKind.Summon, HordeEventFlags.Kill, s.OwnerSlot, s.Id, s.Weapon.Def.Id, 0, s.X, s.Y);
                    OnTransSummonLost(s, owner); // client wave 18 (h18trans): point 30
                    continue;
                }
                if (s.Weapon.Def.Kind == WeaponKind.Pet) { StepPet(s, owner); continue; } // client wave 17 (h17cards): a pet follows its owner
                var lv = s.Weapon.Lv;
                int reach = Units(lv.Range > 0 ? lv.Range : 1.5);
                int target = FindTarget(s.X, s.Y, Targeting.Nearest, Units(10), 0);
                int sp = Math.Max(1, Units((lv.Speed > 0 ? lv.Speed : 4) / TickRate));
                int gx, gy;
                if (target >= 0) { gx = Enemies[target].X; gy = Enemies[target].Y; }
                else { gx = owner.X + Units(1); gy = owner.Y; }
                long dd = Dist2(s.X, s.Y, gx, gy);
                int stop = target >= 0 ? reach * 3 / 4 : Units(0.8);
                if (dd > (long)stop * stop)
                {
                    int d = HordeMove.ISqrt(dd);
                    s.X = Clamp(s.X + (int)((long)(gx - s.X) * sp / d), MinX, MaxX); s.Y = Clamp(s.Y + (int)((long)(gy - s.Y) * sp / d), MinY, MaxY);
                }
                if (target >= 0 && Tick >= s.NextAttack && Dist2(s.X, s.Y, gx, gy) <= (long)(reach + Enemies[target].Radius) * (reach + Enemies[target].Radius))
                {
                    s.NextAttack = Tick + Math.Max(1, Ticks(lv.Tick));
                    if (lv.Area > 0)
                    {
                        int area = AreaUnits(owner, lv.Area);
                        _q.Clear(); _grid.Query(gx, gy, area + Units(1.5), Enemies, _q);
                        foreach (int j in _q.ToArray()) { var e = Enemies[j]; if (e.Alive && Dist2(gx, gy, e.X, e.Y) <= (long)(area + e.Radius) * (area + e.Radius)) HitEnemy(owner, s.Weapon, lv, e, lv.Damage, s.X, s.Y); }
                        AddEvent(HordeEventKind.Strike, HordeEventFlags.None, owner.Slot, -1, s.Weapon.Def.Id, s.Weapon.Level, gx, gy, area / (float)U);
                    }
                    else HitEnemy(owner, s.Weapon, lv, Enemies[target], lv.Damage, s.X, s.Y);
                    if (s.Weapon.Def.SummonBursts) { Summons.RemoveAt(i); AddEvent(HordeEventKind.Summon, HordeEventFlags.Kill, s.OwnerSlot, s.Id, s.Weapon.Def.Id, 0, s.X, s.Y); OnTransSummonLost(s, owner); } // client wave 17 (h17cards): a Sphere Mine bursts once
                }
            }
        }

        // =============================================================================================== lasting effects

        private SimFx NewFx(HordeFxKind kind)
        {
            int id = _nextFxId;
            for (int guard = 0; guard < 70000; guard++)
            {
                bool used = false;
                foreach (var f in Fx) if (f.Id == id && !f.Ended) { used = true; break; }
                if (!used) break;
                id = id >= 65535 ? 1 : id + 1;
            }
            _nextFxId = id >= 65535 ? 1 : id + 1;
            return new SimFx { Id = id, Kind = kind, StartTick = Tick };
        }

        private void BeginFx(SimFx f)
        {
            if (Fx.Count >= HordeLimits.MaxFx && f.Kind != HordeFxKind.Orbit && f.Kind != HordeFxKind.EnemyWarning) return; // too many: this one is not made
            Fx.Add(f); FxBegun.Add(f);
        }

        private void EndFx(SimFx f, bool hit)
        {
            if (f.Ended) return;
            f.Ended = true;
            FxEnded.Add(new KeyValuePair<int, bool>(f.Id, hit));
        }

        private void StepFx()
        {
            int n = Fx.Count;
            for (int i = 0; i < n; i++)
            {
                var f = Fx[i];
                if (f.Ended) continue;
                switch (f.Kind)
                {
                    case HordeFxKind.Shot: StepShot(f); break;
                    case HordeFxKind.EnemyShot: StepEnemyShot(f); break;
                    case HordeFxKind.Zone: StepZone(f); break;
                    case HordeFxKind.Trap: StepTrap(f); break;
                    case HordeFxKind.Warning:
                        if (Tick >= f.EndTick)
                        {
                            var p = Players[f.OwnerSlot];
                            if (p.Present) { StrikeArea(p, f.Weapon, f.Lv, f.X, f.Y, f.Radius, f.BaseDamage); OnTransWarningStrike(p, f); }
                            EndFx(f, false);
                        }
                        break;
                    case HordeFxKind.EnemyWarning:
                        if (Tick >= f.EndTick)
                        {
                            if (CardsCancelStrike(f)) { EndFx(f, false); break; } // client wave 17 (h17cards): Land Protector
                            foreach (var q in Players)
                                if (q.Present && !q.Fallen && Dist2(q.X, q.Y, f.X, f.Y) <= (long)(f.Radius + Units(R.BodyRadius)) * (f.Radius + Units(R.BodyRadius)))
                                    HurtFromArea(q, f); // client wave 17 (h17read): HurtPlayer with the caster's place (HordeSim.Readability.cs)
                            AddEvent(HordeEventKind.Strike, HordeEventFlags.None, -1, f.OwnerEnemy, f.DefId, f.Damage, f.X, f.Y, f.Radius / (float)U);
                            EndFx(f, false);
                        }
                        break;
                    case HordeFxKind.Orbit: StepOrbit(f); break;
                    case HordeFxKind.Guard: StepGuardFx(f); break; // client wave 17 (h17cards): a Parry weapon's guard follows its owner
                }
            }
            Fx.RemoveAll(x => x.Ended);
        }

        private void StrikeArea(SimPlayer p, OwnedWeapon ow, WeaponLevel lv, int x, int y, int radius, double dmg)
        {
            _q.Clear(); _grid.Query(x, y, radius + Units(1.5), Enemies, _q);
            foreach (int i in _q.ToArray())
            {
                var e = Enemies[i];
                int rr = radius + e.Radius;
                if (e.Alive && Dist2(x, y, e.X, e.Y) <= (long)rr * rr) HitEnemy(p, ow, lv, e, dmg, x, y);
            }
            AddEvent(HordeEventKind.Strike, HordeEventFlags.None, p.Slot, -1, ow.Def.Id, ow.Level, x, y, radius / (float)U);
        }

        private void StepShot(SimFx f)
        {
            var p = Players[f.OwnerSlot];
            if (!p.Present) { EndFx(f, false); return; }
            if (f.Target >= 0)
            {
                var t = Enemies[f.Target];
                if (t.Alive)
                {
                    int sp = HordeMove.ISqrt((long)f.VX * f.VX + (long)f.VY * f.VY);
                    long dx = t.X - f.X, dy = t.Y - f.Y; long d = Math.Max(1, HordeMove.ISqrt(dx * dx + dy * dy));
                    f.VX = (f.VX * 3 + (int)(dx * sp / d)) / 4; f.VY = (f.VY * 3 + (int)(dy * sp / d)) / 4;
                }
            }
            f.X += f.VX; f.Y += f.VY;
            if (f.X < MinX || f.X > MaxX || f.Y < MinY || f.Y > MaxY) { EndFx(f, false); return; }
            _q.Clear(); _grid.Query(f.X, f.Y, f.Radius + Units(1.5), Enemies, _q);
            foreach (int i in _q)
            {
                var e = Enemies[i];
                if (!e.Alive || f.HitList.Contains(i * 256 + e.Serial)) continue;
                int rr = f.Radius + e.Radius;
                if (Dist2(f.X, f.Y, e.X, e.Y) > (long)rr * rr) continue;
                if (f.BurstArea > 0)
                {
                    StrikeArea(p, f.Weapon, f.Lv, f.X, f.Y, f.BurstArea, f.BaseDamage);
                    EndFx(f, true);
                    return;
                }
                f.HitList.Add(i * 256 + e.Serial);
                HitEnemy(p, f.Weapon, f.Lv, e, f.BaseDamage, f.X - f.VX, f.Y - f.VY);
                if (f.PierceLeft-- <= 0) { EndFx(f, true); return; }
                f.BaseDamage *= Math.Max(0, f.Lv.Falloff) / 100.0;
                break;
            }
            if (Tick >= f.EndTick && !StepTransShot(f)) EndFx(f, false); // client wave 18 (h18trans): point 30 (BoltBoomerang returns)
        }

        private void StepEnemyShot(SimFx f)
        {
            f.X += f.VX; f.Y += f.VY;
            if (f.X < MinX || f.X > MaxX || f.Y < MinY || f.Y > MaxY || Tick >= f.EndTick) { EndFx(f, false); return; }
            foreach (var z in Fx)
            {
                if (z.Kind != HordeFxKind.Zone || (z.ZoneFlags & ZoneFlags.BlocksShots) == 0 || z.Ended) continue;
                WallClosestPoint(z, f.X, f.Y, out int zx, out int zy);
                if (Dist2(f.X, f.Y, zx, zy) <= (long)z.Radius * z.Radius) { EndFx(f, true); return; }
            }
            int body = Units(R.BodyRadius) + f.Radius;
            foreach (var q in Players)
            {
                if (!q.Present || q.Fallen || Dist2(q.X, q.Y, f.X, f.Y) > (long)body * body) continue;
                HurtFromShot(q, f); // client wave 17 (h17read): HurtPlayer with the place the shot was fired from (HordeSim.Readability.cs)
                EndFx(f, true);
                return;
            }
        }

        private void StepZone(SimFx f)
        {
            var p = Players[f.OwnerSlot];
            if (!p.Present || Tick >= f.EndTick)
            {
                if (p.Present && Tick >= f.EndTick) StepTransZone(p, f, true);
                EndFx(f, false);
                return;
            }
            if (f.Follows) { f.X = p.X; f.Y = p.Y; }
            StepTransZone(p, f, false); // client wave 18 (h18trans): point 30 (ZoneDrift, ZoneCollapse)
            if (Tick < f.NextTick) return;
            f.NextTick = Tick + f.Every;
            if (f.BaseDamage > 0 || f.Lv.Effects != null)
            {
                int qRad = f.Radius + ((f.ZoneFlags & ZoneFlags.BlocksEnemies) != 0 ? f.Range : 0) + Units(1.5);
                _q.Clear(); _grid.Query(f.X, f.Y, qRad, Enemies, _q);
                foreach (int i in _q.ToArray())
                {
                    var e = Enemies[i];
                    if (!e.Alive) continue;
                    WallClosestPoint(f, e.X, e.Y, out int zx, out int zy);
                    int rr = f.Radius + e.Radius;
                    if (Dist2(zx, zy, e.X, e.Y) > (long)rr * rr) continue;
                    if (f.BaseDamage > 0) HitEnemy(p, f.Weapon, f.Lv, e, f.BaseDamage, zx, zy);
                    else ApplyEffects(p, f.Weapon, f.Lv, e, zx, zy);
                }
            }
            if (f.Heal > 0 || f.HealPct > 0 || f.Lv.Buffs != null)
            {
                foreach (var q in Players)
                {
                    if (!q.Present || q.Fallen || Dist2(q.X, q.Y, f.X, f.Y) > (long)f.Radius * f.Radius) continue;
                    if (f.Heal > 0 || f.HealPct > 0) Heal(p, q, f.Lv, f.DefId);
                    if (f.Lv.Buffs != null && f.Lv.Buffs.Length > 0)
                    {
                        var b = q.Buffs.Find(x => x.WeaponId == f.DefId && x.SourceSlot == p.Slot);
                        int until = Tick + f.Every + 1;
                        if (b == null) { q.Buffs.Add(new SimBuff { Mods = f.Lv.Buffs, Until = until, SourceSlot = p.Slot, WeaponId = f.DefId }); Recalc(q, false); }
                        else b.Until = Math.Max(b.Until, until);
                    }
                }
            }
        }

        private void StepTrap(SimFx f)
        {
            var p = Players[f.OwnerSlot];
            if (!p.Present || Tick >= f.EndTick) { EndFx(f, false); return; }
            if (Tick < f.ArmTick) return;
            StepTransTrap(p, f, false); // client wave 18 (h18trans): point 30 (TrapSeeker crawls toward enemies)
            _q.Clear(); _grid.Query(f.X, f.Y, f.Radius + Units(1.5), Enemies, _q);
            foreach (int i in _q)
            {
                var e = Enemies[i];
                int rr = f.Radius + e.Radius;
                if (!e.Alive || Dist2(f.X, f.Y, e.X, e.Y) > (long)rr * rr) continue;
                StrikeArea(p, f.Weapon, f.Lv, f.X, f.Y, f.Radius, f.BaseDamage);
                StepTransTrap(p, f, true); // client wave 18 (h18trans): point 30 (TrapCluster spawns sub-traps)
                EndFx(f, true);
                return;
            }
        }

        /// <summary>Orb i's place at tick t (the client draws it with the same formula from the Orbit's start values).</summary>
        public static void OrbPlace(int cx, int cy, int range, double angleDeg, double turns, int count, int i, double ticksSinceStart, out int x, out int y)
        {
            double a = (angleDeg + 360.0 * turns * ticksSinceStart / TickRate + i * 360.0 / Math.Max(1, count)) * Math.PI / 180.0;
            x = cx + (int)Math.Round(Math.Cos(a) * range); y = cy + (int)Math.Round(Math.Sin(a) * range);
        }

        private void StepOrbit(SimFx f)
        {
            var p = Players[f.OwnerSlot];
            var ow = f.Weapon;
            if (!p.Present || p.Fallen) { EndFx(f, false); return; }
            if (Tick >= f.EndTick) { EndFx(f, false); ow.NextTick = Tick + CooldownTicks(p, f.Lv); return; }
            f.X = p.X; f.Y = p.Y;
            int rRange = TransOrbitRange(f); // client wave 18 (h18trans): point 30 (OrbitBreathing breathes radius)
            for (int k = 0; k < f.Count; k++)
            {
                OrbPlace(p.X, p.Y, rRange, f.Angle, f.Turns, f.Count, k, Tick - f.StartTick, out int ox, out int oy);
                StepTransOrbitShoot(p, f, ox, oy); // client wave 18 (h18trans): point 30 (OrbitShooter fires bolts)
                _q.Clear(); _grid.Query(ox, oy, f.Radius + Units(1.5), Enemies, _q);
                foreach (int i in _q.ToArray())
                {
                    var e = Enemies[i];
                    int rr = f.Radius + e.Radius;
                    if (!e.Alive || Dist2(ox, oy, e.X, e.Y) > (long)rr * rr) continue;
                    int key = (k << 20) | (i << 8) | e.Serial;
                    if (ow.OrbHits.TryGetValue(key, out int last) && Tick - last < f.Every) continue;
                    ow.OrbHits[key] = Tick;
                    HitEnemy(p, ow, f.Lv, e, f.BaseDamage, ox, oy);
                }
            }
            if (ow.OrbHits.Count > 2000) ow.OrbHits.Clear();
        }

        // =============================================================================================== results

        public HordeResults Results(uint runId, HordeRewardSize size)
        {
            var res = new HordeResults { RunId = runId, Outcome = Outcome, Seconds = (Over ? EndTick : Tick) / (float)TickRate };
            float total = Math.Max(1f, res.Seconds);
            // client wave 14 (w14play), point 31 of the design's headset list: Loki Host's presence log of 2026-10-07 22:12 listed Tester twice
            // in one run ("Tester lv 5 ... (left); Tester lv 13 ... (left)") and wrote and granted BOTH rewards. A player who leaves a run
            // (RemovePlayer: its slot stays Used) and answers the run's invitation again joins it late in a NEW slot (AddPlayer takes a
            // slot that was never used; Loki Host's HordeService.Answer), so the results had one row per slot. Now one row per account:
            // its slots' time, kills, damage, healing, falls and revives added up, its highest level and job tier, the name, job, slot and
            // "left" of its latest slot; the score and the reward come from that one row, as for a player who never left. A run without a
            // rejoin gives exactly the rows it gave before (same order, same numbers).
            var byAid = new Dictionary<uint, List<SimPlayer>>(); var order = new List<uint>();
            foreach (var p in Players)
            {
                if (!p.Used) continue;
                if (!byAid.TryGetValue(p.Aid, out var l)) { byAid[p.Aid] = l = new List<SimPlayer>(); order.Add(p.Aid); }
                l.Add(p);
            }
            foreach (uint aid in order)
            {
                var entries = byAid[aid]; var p = entries[0];
                foreach (var q in entries) if (q.JoinTick > p.JoinTick || (q.JoinTick == p.JoinTick && q.Slot > p.Slot)) p = q; // the latest slot
                int ticks = 0, kills = 0, elites = 0, bosses = 0, falls = 0, revives = 0, level = 0, tier = 0; long damage = 0, healing = 0;
                foreach (var q in entries)
                {
                    int end = q.Left ? q.LeftTick : (Over ? EndTick : Tick);
                    ticks += Math.Max(0, end - q.JoinTick);
                    kills += q.Kills; elites += q.EliteKills; bosses += q.BossKills; falls += q.Falls; revives += q.Revives; damage += q.DamageDealt; healing += q.Healing;
                    level = Math.Max(level, q.Level); tier = Math.Max(tier, q.Tier);
                }
                float secs = ticks / (float)TickRate;
                var sc = C.Score;
                long own = (long)kills * sc.PerKill + (long)elites * sc.PerEliteKill + (long)bosses * sc.PerBossKill
                         + (long)(level - 1) * sc.PerRunLevel + (long)tier * sc.PerJobTier + damage / 1000 * sc.PerDamage1000
                         + healing / 100 * sc.PerHeal100 + (long)revives * sc.PerRevive;
                long party = (long)(secs * sc.PerSecond) + (Outcome == HordeOutcome.Victory && !p.Left ? (long)Math.Round(sc.Victory * Math.Min(1f, secs / total)) : 0);
                int score = (int)Math.Min(int.MaxValue, Math.Max(0, own + party));
                HordeReward.Shares(C.Reward, score, secs, size, out int bs, out int js, kills, elites, bosses);
                res.Players.Add(new HordePlayerResult
                {
                    Slot = p.Slot, Aid = p.Aid, Name = p.Name, Level = level, JobId = p.JobId, Kills = kills, EliteKills = elites, BossKills = bosses,
                    Falls = falls, Revives = revives, Damage = damage, Healing = healing, Seconds = secs, Score = score,
                    BaseShare = bs, JobShare = js, Reward = bs > 0 || js > 0 ? HordeRewardState.Pending : HordeRewardState.None, Left = p.Left,
                });
            }
            return res;
        }
    }

    /// <summary>From a run's kills and survival time to the real character's reward: shares of its next base / job level in 1/100 of a percent.
    /// The host's decision (RewardDef: Loki's own numbers), never a client's.</summary>
    public static class HordeReward
    {
        /// <summary>The most any run can give, whatever the tables say: 100 % of a level (10000).</summary>
        public const int MaxShare = 10000;
        /// <summary>Reference weighted kill points for a full 15-minute run (normal kills + 10x elites + 40x bosses).</summary>
        public const double ReferenceKillPoints = 500.0;

        public static void Shares(RewardDef r, int score, float secondsInRun, HordeRewardSize size, out int baseShare, out int jobShare, int kills = -1, int eliteKills = 0, int bossKills = 0)
        {
            baseShare = jobShare = 0;
            if (r == null || size == HordeRewardSize.Off || secondsInRun < r.MinSeconds || (score <= 0 && kills <= 0)) return;
            // Item 56 (2026-10-12): EXP rewards are significantly higher and scale with BOTH time survived and enemies killed,
            // continuing to scale past 15:00 in endless mode up to MaxShare.
            double f;
            if (r.SecondsForFull > 0)
            {
                double timePart = secondsInRun / (double)r.SecondsForFull;
                double killPoints = kills >= 0
                    ? kills + eliteKills * 10.0 + bossKills * 40.0
                    : Math.Max(0.0, score - secondsInRun);
                double killPart = killPoints / ReferenceKillPoints;
                f = timePart * 0.4 + killPart * 0.6;
            }
            else
            {
                f = r.ScoreForFull > 0 ? score / (double)r.ScoreForFull : 1.0;
            }
            int i = (int)size;
            double bc = r.BaseCapPct != null && i < r.BaseCapPct.Length ? r.BaseCapPct[i] : 0, jc = r.JobCapPct != null && i < r.JobCapPct.Length ? r.JobCapPct[i] : 0;
            baseShare = Math.Max(0, Math.Min(MaxShare, (int)Math.Round(bc * 100 * f)));
            jobShare = Math.Max(0, Math.Min(MaxShare, (int)Math.Round(jc * 100 * f)));
        }
    }
}
