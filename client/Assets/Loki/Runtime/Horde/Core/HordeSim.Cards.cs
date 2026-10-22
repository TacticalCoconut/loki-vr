#nullable disable
using System;
using System.Collections.Generic;

namespace Loki.Horde
{
    /// <summary>
    /// Client wave 17 (h17cards): the rules of design 1 / point 15 (design notes, approved as
    /// written): the new weapon kinds (Dash, Mark, Parry, Pet), the new passives' modifiers (Mod 36-58), branches at level 3 (B) and
    /// evolutions at a big chest (C). HordeSim.cs calls in by marked one-line hooks ("client wave 17 (h17cards)"); HordeSim.Weapons.cs
    /// (the weapons' file) calls in where a weapon kind needs it. Deterministic: every roll is the run's own dice (_rng).
    /// The ten passives whose main effect is a trigger (Auto Berserk, Mystic Focus, Making Arrow, Hiding, Auto Counter, Reflect Shield,
    /// Soul Drain, Blade Stop, Cloaking, Gangster's Paradise) are h17pass's proc engine's, not here (design notes).
    /// </summary>
    public sealed partial class HordeSim
    {
        // ================================================================================================= per-player state

        /// <summary>The new modifiers, summed (RecalcCards): what PlayerStats does not hold.</summary>
        private sealed class CardMods
        {
            public readonly double[] VsSize = new double[3], TakenVsRace = new double[10], TakenVsElement = new double[10];
            public double StunAll, StunMelee, ArcPct, MovingRegen, ChestBonus, MovingMelee, PoisonOnHit, Offhand, TrapArm, TrapArea, Echo, HpDrain;
            public double MagicVsBoss, Providence, ChestDropKill, ChestDropElite, ExpPickupHeal, Steal, CancelCells, CancelEverySeconds, CopyShot;
            public int EndowAll = -1, EndowRanged = -1, Aspersio = -1;
            public double AspersioDamage;
            public readonly Dictionary<int, double> WeaponDamage = new Dictionary<int, double>(), WeaponCooldown = new Dictionary<int, double>();
            public void Clear()
            {
                Array.Clear(VsSize, 0, 3); Array.Clear(TakenVsRace, 0, 10); Array.Clear(TakenVsElement, 0, 10);
                StunAll = StunMelee = ArcPct = MovingRegen = ChestBonus = MovingMelee = PoisonOnHit = Offhand = TrapArm = TrapArea = Echo = HpDrain = 0;
                MagicVsBoss = Providence = ChestDropKill = ChestDropElite = ExpPickupHeal = Steal = CancelCells = CancelEverySeconds = CopyShot = 0;
                EndowAll = EndowRanged = Aspersio = -1; AspersioDamage = 0;
                WeaponDamage.Clear(); WeaponCooldown.Clear();
            }
        }

        private sealed class GuardState { public int Charges, Until, FxId; }

        private sealed class CardState
        {
            public readonly CardMods M = new CardMods();
            public int DashUntil, DashVx, DashVy; public OwnedWeapon DashWeapon; public readonly List<int> DashHit = new List<int>();
            public readonly Dictionary<OwnedWeapon, GuardState> Guards = new Dictionary<OwnedWeapon, GuardState>();
            /// <summary>Weapons a passive gives (the falcon): stepped as weapons, never in the loadout.</summary>
            public readonly List<OwnedWeapon> Hidden = new List<OwnedWeapon>();
            public double DrainCarry, MoveRegenCarry;
            public int VendingNext, CancelNext;
            public int CopyUntil, CopyNext, CopyDamage; public OwnedWeapon CopyWeapon;
        }

        private readonly CardState[] _cs = new CardState[HordeLimits.MaxPlayers];
        private CardState CS(SimPlayer p) => _cs[p.Slot] ?? (_cs[p.Slot] = new CardState());

        /// <summary>A player enters a slot: its card state starts over (AddPlayer's hook).</summary>
        private void CardsReset(int slot) { if (slot >= 0 && slot < _cs.Length) _cs[slot] = new CardState(); TransReset(slot); }

        /// <summary>The hit a player takes: what kind it is (a touch, a shot, an area strike: Parry's flags). Merge h17int (client wave 17):
        /// ONE source for who hit whom and how, h17read's HurtPlayer parameters (fromEnemy, via; the merge brief): the kind is read from
        /// via (a touch and an exploder's burst = Touches, as ParryFlags.Touches says; a shot = Shots; anything else an area strike) and
        /// the enemy is fromEnemy, where h17cards' branch set two fields around each call.</summary>
        private const int HurtTouch = 1, HurtShot = 2, HurtStrike = 4;
        private static int HurtKindOf(HordeHitVia via) => via == HordeHitVia.Touch || via == HordeHitVia.Burst ? HurtTouch : via == HordeHitVia.Shot ? HurtShot : HurtStrike;

        /// <summary>The weapon's root: the weapon a branch, evolution, or transcendence comes from (per-weapon modifiers name the root).</summary>
        public static int RootId(WeaponDef w) => w == null ? 0 : w.TransOf > 0 ? w.TransOf : w.BranchOf > 0 ? w.BranchOf : w.EvolveOf > 0 ? w.EvolveOf : w.Id;

        // ================================================================================================= modifiers

        private static void AddCardMod(CardMods s, ModDef m)
        {
            if (m == null) return;
            double v = m.Value; int a = m.Arg;
            switch (m.Kind)
            {
                case Mod.DamageVsSizePct: if (a >= 0 && a < 3) s.VsSize[a] += v; break;
                case Mod.DamageTakenVsRacePct: if (a >= 0 && a < 10) s.TakenVsRace[a] += v; break;
                case Mod.DamageTakenVsElementPct: if (a >= 0 && a < 10) s.TakenVsElement[a] += v; break;
                case Mod.StunOnHitPct: if (a == 1) s.StunMelee += v; else s.StunAll += v; break;
                case Mod.ArcPct: s.ArcPct += v; break;
                case Mod.MovingRegen: s.MovingRegen += v; break;
                case Mod.EndowElement:
                    if (a < 0 || a >= 10) break;
                    if (v >= 2.5) s.Aspersio = a; else if (v >= 1.5) s.EndowRanged = a; else s.EndowAll = a;
                    break;
                case Mod.ChestBonusPct: s.ChestBonus += v; break;
                case Mod.MovingMeleePct: s.MovingMelee += v; break;
                case Mod.PoisonOnHitPct: s.PoisonOnHit += v; break;
                case Mod.OffhandPct: s.Offhand += v; break;
                case Mod.TrapPct: if (a == 1) s.TrapArea += v; else s.TrapArm += v; break;
                case Mod.EchoPct: s.Echo += v; break;
                case Mod.HpDrainPct: s.HpDrain += v; break;
                case Mod.WeaponDamagePct: s.WeaponDamage[a] = (s.WeaponDamage.TryGetValue(a, out var d) ? d : 0) + v; break;
                case Mod.WeaponCooldownPct: s.WeaponCooldown[a] = (s.WeaponCooldown.TryGetValue(a, out var cd) ? cd : 0) + v; break;
                case Mod.MagicVsBossPct: s.MagicVsBoss += v; break;
                case Mod.ProvidencePct: s.Providence += v; break;
                case Mod.ChestDropPct: if (a == 1) s.ChestDropElite += v; else s.ChestDropKill += v; break;
                case Mod.ExpPickupHealPct: s.ExpPickupHeal += v; break;
                case Mod.StealPct: s.Steal += v; break;
                case Mod.CancelStrikeCells: s.CancelCells = Math.Max(s.CancelCells, v); break;
                case Mod.CancelStrikeEvery: s.CancelEverySeconds = s.CancelEverySeconds > 0 ? Math.Min(s.CancelEverySeconds, v) : v; break;
                case Mod.CopyShotPct: s.CopyShot += v; break;
            }
        }

        /// <summary>Recalc's last step: the new modifiers (from the same sources: passives, the job, buffs) and the weapons the passives
        /// give.</summary>
        private void RecalcCards(SimPlayer p)
        {
            var st = CS(p); var s = st.M;
            s.Clear();
            foreach (var op in p.Passives)
            {
                if (op.Level < 1 || op.Def.Levels.Length == 0) continue;
                var mods = op.Def.Levels[Math.Min(op.Level, op.Def.Levels.Length) - 1];
                if (mods != null) foreach (var m in mods)
                    {
                        AddCardMod(s, m);
                        if (m != null && m.Kind == Mod.EndowElement && m.Value >= 2.5)
                            foreach (var m2 in mods) if (m2 != null && m2.Kind == Mod.ElementDamagePct && m2.Arg == m.Arg) s.AspersioDamage = m2.Value;
                    }
                // a weapon the passive gives (the falcon), at the passive's level
                if (op.Def.GivesWeapon > 0)
                {
                    var gw = C.Weapon(op.Def.GivesWeapon);
                    if (gw != null && gw.Levels.Length > 0)
                    {
                        var have = st.Hidden.Find(x => x.Def == gw);
                        int lvl = Math.Min(op.Level, gw.Levels.Length);
                        if (have == null) st.Hidden.Add(new OwnedWeapon { Def = gw, Level = lvl, NextTick = Tick + 1 });
                        else have.Level = lvl;
                    }
                }
            }
            var job = C.Job(p.JobId);
            if (job?.Mods != null) foreach (var m in job.Mods) AddCardMod(s, m);
            foreach (var b in p.Buffs) if (b.Mods != null) foreach (var m in b.Mods) AddCardMod(s, m);
        }

        /// <summary>The element a player's weapon hits with: its own, or (a Neutral weapon) the endowed one (arrows: ranged only).</summary>
        private Element CardsElement(SimPlayer p, WeaponDef w)
        {
            if (w.Element != Element.Neutral) return w.Element;
            var s = CS(p).M;
            if (s.EndowRanged >= 0 && w.Class == DamageClass.Ranged) return (Element)s.EndowRanged;
            if (s.EndowAll >= 0) return (Element)s.EndowAll;
            return Element.Neutral;
        }

        /// <summary>The new damage factors (DamageMul's hook): vs size, Pierce's hits by size, magic vs bosses, the enemy's marks, melee
        /// while walking, per-weapon damage.</summary>
        private double CardsDamageMul(SimPlayer p, WeaponDef w, SimEnemy e)
        {
            var s = CS(p).M;
            double m = 1;
            if (e != null)
            {
                int size = (int)e.Def.Size;
                if (size >= 0 && size < 3) m *= Math.Max(0.1, 1 + s.VsSize[size] / 100.0);
                if (w.HitsBySize) m *= 1 + Clamp(size, 0, 2);
                if (w.Class == DamageClass.Magic && e.Def.Rank != EnemyRank.Normal) m *= 1 + s.MagicVsBoss / 100.0;
                m *= MarkMul(e);
            }
            if (w.Class == DamageClass.Melee && p.Moving && s.MovingMelee != 0) m *= Math.Max(0.1, 1 + s.MovingMelee / 100.0);
            if (s.WeaponDamage.Count > 0 && s.WeaponDamage.TryGetValue(RootId(w), out double wd)) m *= Math.Max(0.1, 1 + wd / 100.0);
            m *= TransDamageMul(p, w, e);
            return m;
        }

        /// <summary>A weapon's cooldown with the per-weapon modifier (Sonic Acceleration).</summary>
        private int CooldownTicks(SimPlayer p, OwnedWeapon ow, WeaponLevel lv)
        {
            var s = CS(p).M;
            double pct = p.S.CooldownPct + (s.WeaponCooldown.Count > 0 && s.WeaponCooldown.TryGetValue(RootId(ow.Def), out double v) ? v : 0);
            return Math.Max(1, Ticks(lv.Cooldown * Math.Max(0.25, 1 + pct / 100.0)));
        }

        // ================================================================================================= marks on enemies

        private readonly int[] _mkSerial = new int[HordeLimits.MaxEnemies], _mkUntil = new int[HordeLimits.MaxEnemies], _mkHits = new int[HordeLimits.MaxEnemies];
        private readonly int[] _mkTaunt = new int[HordeLimits.MaxEnemies], _wkUntil = new int[HordeLimits.MaxEnemies];
        private readonly float[] _mkPct = new float[HordeLimits.MaxEnemies], _wkPct = new float[HordeLimits.MaxEnemies];
        private readonly int[] _stolen = new int[HordeLimits.MaxEnemies];

        private bool MarkedNow(SimEnemy e) => _mkSerial[e.Slot] == e.Serial + 1 && (_mkUntil[e.Slot] > Tick || _wkUntil[e.Slot] > Tick);
        private double MarkMul(SimEnemy e) => _mkSerial[e.Slot] == e.Serial + 1 && _mkUntil[e.Slot] > Tick && _mkPct[e.Slot] > 0 ? 1 + _mkPct[e.Slot] / 100.0 : 1;
        /// <summary>The share of its damage a (weakened) enemy deals: 1 = all, 0 = none (blinded).</summary>
        private double WeakenOf(int slot)
        {
            if (slot < 0 || slot >= Enemies.Length) return 1;
            var e = Enemies[slot];
            return e.Alive && _mkSerial[slot] == e.Serial + 1 && _wkUntil[slot] > Tick ? Math.Max(0, 1 - _wkPct[slot] / 100.0) : 1;
        }

        private void MarkEnemy(SimPlayer p, WeaponLevel lv, SimEnemy e)
        {
            int i = e.Slot, until = Tick + DurationTicks(p, lv.Duration > 0 ? lv.Duration : 4);
            if (_mkSerial[i] != e.Serial + 1) { _mkSerial[i] = e.Serial + 1; _mkUntil[i] = _wkUntil[i] = 0; _mkPct[i] = _wkPct[i] = 0; _mkHits[i] = 0; _mkTaunt[i] = -1; }
            if (lv.MarkPct > 0 || lv.Taunt)
            {
                if (_mkUntil[i] <= Tick) { _mkPct[i] = 0; _mkHits[i] = 0; _mkTaunt[i] = -1; }
                _mkPct[i] = Math.Max(_mkPct[i], lv.MarkPct); _mkHits[i] = lv.MarkHits; _mkUntil[i] = Math.Max(_mkUntil[i], until);
                if (lv.Taunt) _mkTaunt[i] = p.Slot;
            }
            if (lv.WeakenPct > 0)
            {
                if (_wkUntil[i] <= Tick) _wkPct[i] = 0;
                _wkPct[i] = Math.Max(_wkPct[i], lv.WeakenPct); _wkUntil[i] = Math.Max(_wkUntil[i], until);
            }
        }

        /// <summary>StepEnemies' hook (after the flags are set): the Marked flag, and a taunted enemy goes for its marker.</summary>
        private void CardsEnemy(SimEnemy e, ref SimPlayer target, ref long d2)
        {
            if (_mkSerial[e.Slot] != e.Serial + 1) return;
            if (!MarkedNow(e)) return;
            e.Flags |= HordeEnemyFlags.Marked;
            int t = _mkTaunt[e.Slot];
            if (t >= 0 && _mkUntil[e.Slot] > Tick && t < Players.Length && Players[t].Present && !Players[t].Fallen)
            {
                target = Players[t];
                long dx = target.X - e.X, dy = target.Y - e.Y; d2 = dx * dx + dy * dy;
            }
        }

        // ================================================================================================= the player's tick

        /// <summary>StepPlayers' hook (a standing player, after its walk): a dash going on, HP while walking, Martyr's drain, Fly Wing,
        /// the guards (expiry, Defender's slower walk), Plagiarism's copied shot.</summary>
        private void StepCardsPlayer(SimPlayer p)
        {
            var st = CS(p); var s = st.M;
            // a dash: the impulse moves the player (the client corrects its prediction smoothly); every enemy near the path is hit once
            if (st.DashUntil > Tick && st.DashWeapon != null)
            {
                p.X = Clamp(p.X + st.DashVx, MinX, MaxX); p.Y = Clamp(p.Y + st.DashVy, MinY, MaxY);
                var dw = st.DashWeapon; var dl = dw.Lv;
                if ((dl.Damage > 0 || dl.Effects != null) && !IsSlide(dw.Def)) // client wave 18 (h18rules): Back Slide's damage is its trail's (HordeSim.Rules.cs)
                {
                    int area = AreaUnits(p, dl.Area > 0 ? dl.Area : 1f);
                    foreach (int i in Near(p.X, p.Y, area + Units(1.5)))
                    {
                        var e = Enemies[i];
                        if (!e.Alive) continue;
                        int key = i * 256 + e.Serial;
                        if (st.DashHit.Contains(key)) continue;
                        int rr = area + e.Radius;
                        if (Dist2(p.X, p.Y, e.X, e.Y) > (long)rr * rr) continue;
                        st.DashHit.Add(key);
                        if (dl.Damage > 0) HitEnemy(p, dw, dl, e, dl.Damage, p.X - st.DashVx * 2, p.Y - st.DashVy * 2);
                        else ApplyEffects(p, dw, dl, e, p.X, p.Y);
                    }
                }
            }
            // HP while walking (Moving HP Recovery)
            if (s.MovingRegen > 0 && p.Moving && p.Hp < p.MaxHp)
            {
                st.MoveRegenCarry += s.MovingRegen / TickRate;
                if (st.MoveRegenCarry >= 1) { int h = (int)st.MoveRegenCarry; st.MoveRegenCarry -= h; p.Hp = Math.Min(p.MaxHp, p.Hp + h); }
            }
            // Martyr's Reckoning: max HP lost each second, never below a quarter
            if (s.HpDrain > 0)
            {
                int floor = Math.Max(1, p.MaxHp / 4);
                if (p.Hp > floor)
                {
                    st.DrainCarry += p.MaxHp * s.HpDrain / 100.0 / TickRate;
                    if (st.DrainCarry >= 1) { int d = (int)st.DrainCarry; st.DrainCarry -= d; p.Hp = Math.Max(floor, p.Hp - d); }
                }
                else st.DrainCarry = 0;
            }
            // the weapons that go off by themselves: Fly Wing (crowded), the guards' upkeep
            foreach (var ow in p.Weapons)
            {
                var w = ow.Def; if (w.Levels.Length == 0) continue;
                var lv = ow.Lv;
                if (w.Kind == WeaponKind.Dash && w.DashOn == DashTrigger.Crowded && Tick >= ow.NextTick && st.DashUntil <= Tick)
                {
                    int need = Math.Max(1, lv.Count), body = Units(R.BodyRadius) + Units(0.25), near = 0;
                    long sx = 0, sy = 0;
                    foreach (int i in Near(p.X, p.Y, body + Units(1.5))) { var e = Enemies[i]; int rr = body + e.Radius; if (e.Alive && Dist2(p.X, p.Y, e.X, e.Y) <= (long)rr * rr) { near++; sx += e.X; sy += e.Y; } }
                    if (near >= need) { Blink(p, ow, lv, near > 0 ? Math.Atan2(p.Y - sy / near, p.X - sx / near) : 0); ow.NextTick = Tick + CooldownTicks(p, ow, lv); }
                }
                else if (w.Kind == WeaponKind.Parry && st.Guards.TryGetValue(ow, out var g))
                {
                    bool up = g.Charges > 0 && g.Until > Tick;
                    if (!up && g.FxId != 0) { EndGuardFx(g); if (lv.Duration > 0) ow.NextTick = Math.Max(ow.NextTick, Tick + CooldownTicks(p, ow, lv)); }
                    // Defender: you walk slower while the guard is up (the level's Buffs, kept fresh)
                    if (up && lv.Buffs != null && lv.Buffs.Length > 0)
                    {
                        var b = p.Buffs.Find(x => x.WeaponId == w.Id && x.SourceSlot == p.Slot);
                        if (b == null) { p.Buffs.Add(new SimBuff { Mods = lv.Buffs, Until = Tick + 2, SourceSlot = p.Slot, WeaponId = w.Id }); Recalc(p, false); }
                        else { bool ch = b.Mods != lv.Buffs; b.Mods = lv.Buffs; b.Until = Tick + 2; if (ch) Recalc(p, false); }
                    }
                }
            }
            // Plagiarism: the copied shot, fired back every 1.5 s while the copy lasts
            if (st.CopyUntil > Tick && Tick >= st.CopyNext && st.CopyDamage > 0)
            {
                var cw = C.Weapon(HiddenCopyId());
                int target = cw != null ? FindTarget(p.X, p.Y, Targeting.Nearest, Units(8), 0) : -1;
                if (target >= 0)
                {
                    if (st.CopyWeapon == null || st.CopyWeapon.Def != cw) st.CopyWeapon = new OwnedWeapon { Def = cw, Level = 1 };
                    var e = Enemies[target]; var clv = st.CopyWeapon.Lv;
                    int sp = Math.Max(1, Units(clv.Speed > 0 ? clv.Speed / TickRate : 9.0 / TickRate));
                    var f = ShootFrom(p, st.CopyWeapon, clv, Math.Atan2(e.Y - p.Y, e.X - p.X), sp, Units(9), -1, p.X, p.Y);
                    if (f != null) f.BaseDamage = st.CopyDamage * (1 + s.CopyShot / 100.0);
                    st.CopyNext = Tick + Ticks(1.5);
                }
            }
            StepTransPlayer(p); // client wave 18 (h18trans): point 30
        }

        private readonly List<int> _near = new List<int>(64);
        /// <summary>The live enemies within r of (x, y), without the grid: StepPlayers runs before the tick's grid is built (on the
        /// run's first tick it was never built: its lists are not valid yet).</summary>
        private int[] Near(int x, int y, int r)
        {
            _near.Clear();
            long r2 = (long)r * r;
            for (int i = 0; i < Enemies.Length; i++) { var e = Enemies[i]; if (e.Alive && Dist2(x, y, e.X, e.Y) <= r2) _near.Add(i); }
            return _near.ToArray();
        }

        private int _hiddenCopyId = -1;
        /// <summary>The tables' hidden copied-shot weapon (key rg_plagiarism_shot), found once.</summary>
        private int HiddenCopyId()
        {
            if (_hiddenCopyId >= 0) return _hiddenCopyId;
            _hiddenCopyId = 0;
            foreach (var w in C.Weapons) if (w.Hidden && w.Kind == WeaponKind.Bolt) { _hiddenCopyId = w.Id; break; }
            return _hiddenCopyId;
        }

        // ================================================================================================= dash

        /// <summary>A dash of <paramref name="cells"/> at <paramref name="ang"/> over 0.2 s (an impulse; the player cannot be hurt meanwhile).</summary>
        private void StartDash(SimPlayer p, OwnedWeapon ow, double ang, int units)
        {
            var st = CS(p);
            int sx = p.X, sy = p.Y;
            int ticks = Math.Max(4, Ticks(0.2));
            int vx = (int)Math.Round(Math.Cos(ang) * units / ticks), vy = (int)Math.Round(Math.Sin(ang) * units / ticks);
            st.DashVx = vx; st.DashVy = vy; st.DashUntil = Tick + ticks; st.DashWeapon = ow; st.DashHit.Clear();
            p.HurtUntil = Math.Max(p.HurtUntil, st.DashUntil);
            int ex = Clamp(p.X + vx * ticks, MinX, MaxX), ey = Clamp(p.Y + vy * ticks, MinY, MaxY);
            AddEvent(HordeEventKind.Dash, HordeEventFlags.None, p.Slot, -1, ow.Def.Id, ow.Level, p.X, p.Y, (float)(ang * 180 / Math.PI), 0, units / (float)U, ex, ey);
            OnTransDashStart(p, ow, sx, sy, ex, ey, ticks); // client wave 18 (h18trans): point 30
        }

        /// <summary>Fly Wing: the player blinks at once Range cells at <paramref name="ang"/> (away from the crowd).</summary>
        private void Blink(SimPlayer p, OwnedWeapon ow, WeaponLevel lv, double ang)
        {
            int units = Units(lv.Range > 0 ? lv.Range : 5);
            int fx = p.X, fy = p.Y;
            p.X = Clamp(p.X + (int)Math.Round(Math.Cos(ang) * units), MinX, MaxX); p.Y = Clamp(p.Y + (int)Math.Round(Math.Sin(ang) * units), MinY, MaxY);
            p.HurtUntil = Math.Max(p.HurtUntil, Tick + Ticks(0.5));
            AddEvent(HordeEventKind.Dash, HordeEventFlags.None, p.Slot, -1, ow.Def.Id, ow.Level, fx, fy, (float)(ang * 180 / Math.PI), 1, units / (float)U, p.X, p.Y);
            OnTransDashStart(p, ow, fx, fy, p.X, p.Y, 1); // client wave 18 (h18trans): point 30
        }

        // ================================================================================================= guards (Parry)

        private GuardState GuardOf(SimPlayer p, OwnedWeapon ow)
        {
            var st = CS(p);
            if (!st.Guards.TryGetValue(ow, out var g)) st.Guards[ow] = g = new GuardState();
            return g;
        }

        private void EndGuardFx(GuardState g)
        {
            if (g.FxId == 0) return;
            foreach (var f in Fx) if (f.Id == g.FxId && f.Kind == HordeFxKind.Guard && !f.Ended) { EndFx(f, false); break; }
            g.FxId = 0;
        }

        /// <summary>The guard as drawn: a Guard fx with its charges (a change ends the old one and begins a new one).</summary>
        private void ShowGuard(SimPlayer p, OwnedWeapon ow, GuardState g)
        {
            EndGuardFx(g);
            if (g.Charges <= 0 || g.Until <= Tick) return;
            var lv = ow.Lv;
            var f = NewFx(HordeFxKind.Guard);
            f.OwnerSlot = p.Slot; f.DefId = ow.Def.Id; f.Level = ow.Level; f.X = p.X; f.Y = p.Y; f.Radius = AreaUnits(p, lv.Area > 0 ? lv.Area : 1f);
            f.Angle = lv.Arc; f.Count = g.Charges; f.Follows = true; f.EndTick = g.Until; f.Weapon = ow; f.Lv = lv;
            BeginFx(f);
            g.FxId = f.Ended ? 0 : f.Id;
        }

        /// <summary>FireOnce for a Parry weapon: raise the guard (all charges), or give one charge back to a standing guard.</summary>
        private bool FireParry(SimPlayer p, OwnedWeapon ow, WeaponLevel lv)
        {
            var g = GuardOf(p, ow);
            int max = Math.Max(1, lv.Count);
            bool up = g.Charges > 0 && g.Until > Tick;
            if (!up) { g.Charges = max; g.Until = lv.Duration > 0 ? Tick + DurationTicks(p, lv.Duration) : int.MaxValue; ShowGuard(p, ow, g); return true; }
            if (lv.Duration <= 0 && g.Charges < max) { g.Charges++; ShowGuard(p, ow, g); return true; }
            return false;
        }

        /// <summary>HurtPlayer's hook before the dodge: a guard blocks the hit (and Magic Rod throws a shot back), the damage taken by the
        /// enemy's race / element and Providence, a weakened enemy's share. True: nothing more happens to the player.</summary>
        private bool CardsBlock(SimPlayer p, ref int dmg, int enemyDefId, int x, int y, int fromEnemy, HordeHitVia via)
        {
            var st = CS(p);
            int hurtKind = HurtKindOf(via), hurtFrom = fromEnemy; // (merge h17int: h17read's parameters)
            foreach (var ow in p.Weapons)
            {
                if (ow.Def.Kind != WeaponKind.Parry || (ow.Def.Parry & (ParryFlags)hurtKind) == 0) continue;
                if (!st.Guards.TryGetValue(ow, out var g) || g.Charges <= 0 || g.Until <= Tick) continue;
                var lv = ow.Lv;
                if (lv.Arc > 0 && lv.Arc < 360)
                {
                    double a = Math.Atan2(y - p.Y, x - p.X) * 180 / Math.PI - p.FaceDeg;
                    while (a > 180) a -= 360; while (a < -180) a += 360;
                    if (Math.Abs(a) > lv.Arc / 2.0) continue;
                }
                g.Charges--;
                AddEvent(HordeEventKind.Parried, HordeEventFlags.None, p.Slot, hurtFrom, ow.Def.Id, hurtKind, p.X, p.Y);
                if ((ow.Def.Parry & ParryFlags.Returns) != 0 && hurtKind == HurtShot)
                {
                    double ang = Math.Atan2(y - p.Y, x - p.X);
                    if (hurtFrom >= 0 && Enemies[hurtFrom].Alive) ang = Math.Atan2(Enemies[hurtFrom].Y - p.Y, Enemies[hurtFrom].X - p.X);
                    int sp = Math.Max(1, Units((lv.Speed > 0 ? lv.Speed : 10) / TickRate));
                    var f = ShootFrom(p, ow, lv, ang, sp, Units(10), -1, p.X, p.Y);
                    if (f != null) f.BaseDamage = lv.Damage + dmg;
                }
                OnTransParried(p, ow, lv, x, y); // client wave 18 (h18trans): point 30
                if (g.Charges <= 0 && lv.Duration > 0) ow.NextTick = Math.Max(ow.NextTick, Tick + CooldownTicks(p, ow, lv));
                ShowGuard(p, ow, g);
                return true;
            }
            // the damage taken: the enemy's race / element (cards, Detoxify), Providence (the best within 6 cells), a weakened enemy
            double mul = WeakenOf(hurtFrom);
            var ed = C.Enemy(enemyDefId);
            if (ed != null)
            {
                var s = st.M;
                double pct = s.TakenVsRace[(int)ed.Race % 10] + s.TakenVsElement[(int)ed.Element % 10];
                if (ed.Race == Race.Demon || ed.Element == Element.Undead)
                {
                    double best = 0; int six = Units(6);
                    foreach (var q in Players) if (q.Present && !q.Fallen && Dist2(q.X, q.Y, p.X, p.Y) <= (long)six * six) best = Math.Min(best, CS(q).M.Providence);
                    pct += best;
                }
                mul *= Math.Max(0, 1 + pct / 100.0);
            }
            if (mul < 0.9999)
            {
                dmg = (int)Math.Round(dmg * mul);
                if (dmg <= 0) { AddEvent(HordeEventKind.PlayerHit, HordeEventFlags.Dodged, p.Slot, fromEnemy, enemyDefId, 0, p.X, p.Y, (float)via, 0, 0, x, y); return true; } // (merge h17int: + the attacker, as h17read's own dodge)
            }
            return false;
        }

        /// <summary>HurtPlayer's hook after the damage: Back Slide dashes away, Plagiarism copies a shot.</summary>
        private void CardsHurt(SimPlayer p, int raw, int enemyDefId, int x, int y, HordeHitVia via)
        {
            var st = CS(p);
            if (p.Hp > 0 && st.DashUntil <= Tick)
                foreach (var ow in p.Weapons)
                {
                    if (ow.Def.Kind != WeaponKind.Dash || ow.Def.DashOn != DashTrigger.WhenHit || Tick < ow.NextTick) continue;
                    var lv = ow.Lv;
                    double ang = Math.Atan2(p.Y - y, p.X - x);
                    if (x == p.X && y == p.Y) ang = (p.FaceDeg + 180) * Math.PI / 180.0;
                    StartDash(p, ow, ang, Units(lv.Range > 0 ? lv.Range : 3));
                    SlideBegan(p, ow, lv); // client wave 18 (h18rules), point 27: the trail, then unseen, unhurt, faster (HordeSim.Rules.cs)
                    ow.NextTick = Tick + CooldownTicks(p, ow, lv);
                    break;
                }
            if (HurtKindOf(via) == HurtShot && st.M.CopyShot > 0 && raw > 0)
            {
                st.CopyDamage = raw; st.CopyUntil = Tick + Ticks(10); st.CopyNext = Math.Max(st.CopyNext, Tick + Ticks(0.3));
            }
        }

        /// <summary>StepFx: a guard follows its owner and ends with it.</summary>
        private void StepGuardFx(SimFx f)
        {
            var p = f.OwnerSlot >= 0 ? Players[f.OwnerSlot] : null;
            if (p == null || !p.Present || p.Fallen || Tick >= f.EndTick) { EndFx(f, false); if (p != null && f.Weapon != null && CS(p).Guards.TryGetValue(f.Weapon, out var g) && g.FxId == f.Id) g.FxId = 0; return; }
            f.X = p.X; f.Y = p.Y;
        }

        // ================================================================================================= hits and kills

        private bool _casting;

        /// <summary>HitEnemy's hook after the damage: a mark spent, the on-hit cards (stun, poison, the off-hand blow, Steal, Turn Undead,
        /// an evolved Bash's burst), Auto Spell.</summary>
        private void CardsAfterHit(SimPlayer p, OwnedWeapon ow, WeaponLevel lv, SimEnemy e, int dmg, bool crit, bool died, int fromX, int fromY)
        {
            var s = CS(p).M; var w = ow.Def;
            int i = e.Slot;
            if (_mkSerial[i] == e.Serial + 1 && _mkUntil[i] > Tick && _mkHits[i] > 0 && --_mkHits[i] == 0) { _mkUntil[i] = Tick; _mkPct[i] = 0; }
            if (!died && e.Alive)
            {
                bool melee = w.Class == DamageClass.Melee;
                double stun = s.StunAll + (melee ? s.StunMelee : 0);
                if (stun > 0 && _rng.Chance(stun))
                {
                    double resist = Math.Max(0, 1 - e.Def.EffectResist / 100.0);
                    if (resist > 0) { e.StunUntil = Math.Max(e.StunUntil, Tick + Ticks((melee && s.StunMelee > 0 ? 1.0 : 0.6) * resist)); AddEvent(HordeEventKind.Effect, HordeEventFlags.None, p.Slot, i, w.Id, (int)EffectKind.Stun, e.X, e.Y); }
                }
                if (s.PoisonOnHit > 0 && e.DotUntil <= Tick && _rng.Chance(s.PoisonOnHit))
                {
                    e.DotUntil = Tick + Ticks(4); e.DotEvery = Ticks(1); e.DotNext = Tick + e.DotEvery;
                    e.DotDamage = Math.Max(1, (int)Math.Round(Math.Max(3, s.PoisonOnHit / 2) * DamageMul(p, w, e))); e.DotSlot = p.Slot; e.DotWeapon = w.Id;
                    AddEvent(HordeEventKind.Effect, HordeEventFlags.None, p.Slot, i, w.Id, (int)EffectKind.Dot, e.X, e.Y);
                }
                if (s.Steal > 0 && _stolen[i] != e.Serial + 1 && _rng.Chance(s.Steal)) { _stolen[i] = e.Serial + 1; DropExp(e.X, e.Y, 1); }
                if (lv.ExecutePct > 0 && e.Def.Race == Race.Undead && e.Def.Rank == EnemyRank.Normal && _rng.Chance(lv.ExecutePct))
                    DamageEnemy(e, e.Hp, p.Slot, w.Id, true, false, fromX, fromY);
                if (melee && s.Offhand > 0 && e.Alive) DamageEnemy(e, Math.Max(1, (int)Math.Round(dmg * s.Offhand / 100.0)), p.Slot, w.Id, false, false, fromX, fromY);
            }
            // an evolved Bash / Deadly Poison: the hit enemy bursts on its neighbours (no chain of chains)
            if (lv.Splash > 0 && lv.SplashPct > 0 && !_casting)
            {
                int r = AreaUnits(p, lv.Splash); int sd = Math.Max(1, (int)Math.Round(dmg * lv.SplashPct / 100.0));
                foreach (int j in Near(e.X, e.Y, r + Units(1.5))) // (not _q: a shot's loop may be walking it)
                {
                    if (j == i) continue;
                    var o = Enemies[j]; int rr = r + o.Radius;
                    if (o.Alive && Dist2(e.X, e.Y, o.X, o.Y) <= (long)rr * rr) DamageEnemy(o, sd, p.Slot, w.Id, false, false, e.X, e.Y);
                }
                AddEvent(HordeEventKind.Strike, HordeEventFlags.None, p.Slot, -1, w.Id, ow.Level, e.X, e.Y, r / (float)U);
            }
            // Auto Spell: a hit of another weapon may cast a free bolt
            if (!_casting && !w.CastOnHit)
                foreach (var aw in p.Weapons)
                {
                    if (!aw.Def.CastOnHit || aw == ow || Tick < aw.NextTick) continue;
                    var al = aw.Lv;
                    if (al.ProcPct <= 0 || !_rng.Chance(al.ProcPct)) continue;
                    _casting = true;
                    bool cast = CastRandom(p, aw);
                    _casting = false;
                    if (cast) aw.NextTick = Tick + CooldownTicks(p, aw, al);
                    break;
                }
            TransAfterHit(p, ow, lv, e, dmg, crit, died); // client wave 18 (h18trans): point 30
        }

        /// <summary>One cast of a random weapon of <paramref name="ow"/>'s list, at its level (Abracadabra, Auto Spell). False: nothing to aim at.</summary>
        private bool CastRandom(SimPlayer p, OwnedWeapon ow)
        {
            var list = ow.Def.RandomOf;
            if (list == null || list.Length == 0) return false;
            var def = C.Weapon(list[_rng.Next(list.Length)]);
            if (def == null || def.Levels.Length == 0 || def.RandomOf.Length > 0) return false;
            // a branch of Abracadabra casts its spells higher (its Count - 1 = levels added)
            var temp = new OwnedWeapon { Def = def, Level = Clamp(ow.Level + Math.Max(0, ow.Lv.Count - 1), 1, def.Levels.Length), NextTick = Tick };
            bool ok = FireOnce(p, temp, temp.Lv, true);
            if (ok) OnTransRandomCast(p, ow); // client wave 18 (h18trans): point 30
            return ok;
        }

        /// <summary>EnemyDied's hook: Ore Discovery's small chest, Item Appraisal's extra chest from an elite or a boss, the marks cleared.</summary>
        private void CardsOnKill(SimEnemy e, SimPlayer killer)
        {
            TransOnKill(e, killer); // client wave 18 (h18trans): point 30 (before _mkSerial is cleared so MarkViral can see the mark)
            _mkSerial[e.Slot] = 0;
            if (killer != null && CS(killer).M.ChestDropKill > 0 && _rng.Chance(CS(killer).M.ChestDropKill))
            {
                var small = ChestDef(false);
                if (small != null) AddPickup(small, e.X, e.Y, small.Value);
            }
            if (e.Def.Rank != EnemyRank.Normal)
            {
                double best = 0;
                foreach (var q in Players) if (q.Present) best = Math.Max(best, CS(q).M.ChestDropElite);
                if (best > 0 && _rng.Chance(best))
                {
                    var chest = ChestDef(e.Def.Rank >= EnemyRank.Boss);
                    double a = _rng.NextDouble() * Math.PI * 2;
                    if (chest != null) AddPickup(chest, e.X + Units(Math.Cos(a) * 3), e.Y + Units(Math.Sin(a) * 3), chest.Value);
                }
            }
        }

        /// <summary>The tables' chest pickup: the smallest (one upgrade) or the biggest.</summary>
        private PickupDef ChestDef(bool big)
        {
            PickupDef best = null;
            foreach (var pd in C.Pickups) if (pd.Kind == PickupKind.Chest && (best == null || (big ? pd.Value > best.Value : pd.Value < best.Value))) best = pd;
            return best;
        }

        // ================================================================================================= pickups and chests

        /// <summary>TakePickup's hook: Vending heals on an EXP pickup (at most once every 2 s).</summary>
        private void CardsPickup(SimPlayer p, SimPickup k)
        {
            if (k.Def.Kind != PickupKind.Exp) return;
            TransOnPickup(p); // client wave 18 (h18trans): point 30
            var st = CS(p);
            if (st.M.ExpPickupHeal <= 0 || Tick < st.VendingNext || p.Hp >= p.MaxHp || p.Fallen) return;
            int h = Math.Max(1, (int)Math.Round(p.MaxHp * st.M.ExpPickupHeal / 100.0));
            int before = p.Hp; p.Hp = Math.Min(p.MaxHp, p.Hp + h);
            st.VendingNext = Tick + Ticks(2);
            AddEvent(HordeEventKind.Heal, HordeEventFlags.None, p.Slot, p.Slot, 0, p.Hp - before, p.X, p.Y);
        }

        /// <summary>A chest taken (point 15 C): a big chest (3+ upgrades) first turns one weapon at its top level whose partner is owned into
        /// its evolved form (it costs one of the chest's upgrades); Discount may add an upgrade; then the upgrades.</summary>
        private void CardsChest(SimPlayer p, SimPickup k) => ChestOf(p, Math.Max(1, k.Value));

        /// <summary>A chest of <paramref name="value"/> upgrades opened by <paramref name="p"/>: Discount may add one, a big chest (<paramref name="value"/> 3+) may evolve
        /// a weapon (it costs one upgrade). Merge h17int (client wave 17): h17obj's guarded chest (an Old Violet Box of three) opens through
        /// this too, as h17cards' rule says ("any chest of 3+ upgrades").</summary>
        private void ChestOf(SimPlayer p, int value)
        {
            int n = value;
            if (CS(p).M.ChestBonus > 0 && _rng.Chance(CS(p).M.ChestBonus)) n++;
            if (value >= 3 && TryEvolve(p)) n--;
            for (int i = 0; i < n; i++) ChestUpgrade(p);
        }

        /// <summary>The evolved form a weapon may take now (its top level, its partner owned), or null.</summary>
        public WeaponDef EvolutionFor(SimPlayer p, OwnedWeapon ow)
        {
            var w = ow.Def;
            if (w.EvolveOf > 0 || w.Hidden || ow.Level < w.Levels.Length) return null;
            int root = RootId(w);
            foreach (var e in C.Weapons)
            {
                if (e.EvolveOf != root) continue;
                if (e.NeedsWeapon > 0) { bool has = false; foreach (var o in p.Weapons) if (RootId(o.Def) == e.NeedsWeapon) has = true; if (!has) continue; }
                if (e.NeedsPassive > 0) { bool has = false; foreach (var o in p.Passives) if (PassiveRootId(o.Def) == e.NeedsPassive) has = true; if (!has) continue; }
                return e;
            }
            return null;
        }

        private bool TryEvolve(SimPlayer p)
        {
            foreach (var ow in p.Weapons)
            {
                var e = EvolutionFor(p, ow);
                if (e == null) continue;
                int was = ow.Def.Id;
                ow.Def = e; ow.Level = Math.Min(ow.Level, e.Levels.Length);
                RestartOrbit(p, ow);
                Recalc(p, false); p.LoadoutVersion++;
                AddEvent(HordeEventKind.Evolved, HordeEventFlags.None, p.Slot, -1, e.Id, was, p.X, p.Y);
                return true;
            }
            return false;
        }

        // ================================================================================================= offers (branches, groups)

        /// <summary>DrawCards' hook on the candidates: a weapon owned as its branch or evolved form is out; a group's other members are out
        /// once one is owned (the arrows, Endow, the homunculi).</summary>
        private void CardsFilter(SimPlayer p, List<Cand> newW, List<Cand> newP)
        {
            // a weapon owned as its branch, evolved, or transcended form is owned (it is never offered again as new)
            newW.RemoveAll(cd => { foreach (var o in p.Weapons) if (RootId(o.Def) == cd.DefId) return true; return false; });
            newW.RemoveAll(cd => { var w = C.Weapon(cd.DefId); if (w == null || w.Group == 0) return false; foreach (var o in p.Weapons) if (o.Def.Group == w.Group) return true; return false; });
            newP.RemoveAll(cd => { foreach (var o in p.Passives) if (PassiveRootId(o.Def) == cd.DefId) return true; return false; });
            newP.RemoveAll(cd => { var d = C.Passive(cd.DefId); if (d == null || d.Group == 0) return false; foreach (var o in p.Passives) if (o.Def.Group == d.Group) return true; return false; });
        }

        /// <summary>DrawCards' hook on a drawn card: a weapon's level 2 → 3 card is its two branches, side by side (one at random when only
        /// one place is left). True when it put the branches in.</summary>
        private bool CardsBranchCards(SimPlayer p, Cand ch, List<HordeCard> picked, int want)
        {
            if (ch.Kind != HordeCardKind.WeaponLevel || ch.Level != 3) return false;
            var w = C.Weapon(ch.DefId);
            if (w == null || w.Branches == null || w.Branches.Length < 2) return false;
            int room = want - picked.Count;
            if (room >= 2) { picked.Add(new HordeCard { Kind = HordeCardKind.WeaponBranch, DefId = w.Branches[0], Level = 3 }); picked.Add(new HordeCard { Kind = HordeCardKind.WeaponBranch, DefId = w.Branches[1], Level = 3 }); }
            else picked.Add(new HordeCard { Kind = HordeCardKind.WeaponBranch, DefId = w.Branches[_rng.Next(2)], Level = 3 });
            return true;
        }

        /// <summary>ApplyCard's case for a branch card: the owned weapon becomes its branch, at level 3.</summary>
        private void ApplyBranch(SimPlayer p, HordeCard c)
        {
            var b = C.Weapon(c.DefId);
            if (b == null || b.BranchOf <= 0) return;
            foreach (var ow in p.Weapons)
                if (ow.Def.Id == b.BranchOf && ow.Level < b.Levels.Length) { ow.Def = b; ow.Level = Math.Max(ow.Level + 1, 3); RestartOrbit(p, ow); return; }
        }

        /// <summary>A plain level card (a chest's upgrade) that takes a weapon from 2 to 3: it takes one of its branches at random.
        /// True when it did (the caller does not add the level).</summary>
        private bool CardsLevelUp(SimPlayer p, OwnedWeapon ow)
        {
            var w = ow.Def;
            if (ow.Level != 2 || w.Branches == null || w.Branches.Length < 2) return false;
            var b = C.Weapon(w.Branches[_rng.Next(2)]);
            if (b == null) return false;
            ow.Def = b; ow.Level = 3;
            return true;
        }

        /// <summary>After a weapon's last blow of a burst: Chain Combo's extra blow (the hidden weapon, at the passive's level).</summary>
        private void CardsCombo(SimPlayer p, OwnedWeapon ow)
        {
            var w = ow.Def;
            if (w.ComboPassive <= 0 || w.ComboWeapon <= 0) return;
            OwnedPassive have = null; foreach (var op in p.Passives) if (PassiveRootId(op.Def) == w.ComboPassive) have = op;
            if (have == null) return;
            var cw = C.Weapon(w.ComboWeapon);
            if (cw == null || cw.Levels.Length == 0) return;
            var temp = new OwnedWeapon { Def = cw, Level = Clamp(have.Level, 1, cw.Levels.Length), NextTick = Tick };
            FireOnce(p, temp, temp.Lv, true);
        }

        // ================================================================================================= area strikes, heals

        /// <summary>StepFx's hook before an enemy's area strikes: Land Protector cancels it near its player (once every few seconds).</summary>
        private bool CardsCancelStrike(SimFx f)
        {
            foreach (var p in Players)
            {
                if (!p.Present || p.Fallen) continue;
                var st = CS(p);
                if (st.M.CancelCells <= 0 || Tick < st.CancelNext) continue;
                int r = f.Radius + Units(st.M.CancelCells);
                if (Dist2(p.X, p.Y, f.X, f.Y) > (long)r * r) continue;
                st.CancelNext = Tick + Ticks(Math.Max(1, st.M.CancelEverySeconds));
                AddEvent(HordeEventKind.Parried, HordeEventFlags.None, p.Slot, f.OwnerEnemy, 0, HurtStrike, f.X, f.Y, f.Radius / (float)U);
                return true;
            }
            return false;
        }

        private readonly Dictionary<long, ModDef[]> _aspersioMods = new Dictionary<long, ModDef[]>();

        /// <summary>Heal's hook: Aspersio makes the healed ally's Neutral weapons Holy for 10 s.</summary>
        private void CardsHealed(SimPlayer healer, SimPlayer q)
        {
            var s = CS(healer).M;
            if (s.Aspersio < 0) return;
            long key = s.Aspersio * 100000L + (long)Math.Round(s.AspersioDamage * 100);
            if (!_aspersioMods.TryGetValue(key, out var mods))
                _aspersioMods[key] = mods = new[] { new ModDef(Mod.EndowElement, 1, s.Aspersio), new ModDef(Mod.ElementDamagePct, (float)s.AspersioDamage, s.Aspersio) };
            int until = Tick + Ticks(10);
            const int AspersioBuffId = -17;
            var b = q.Buffs.Find(x => x.WeaponId == AspersioBuffId && x.SourceSlot == healer.Slot);
            if (b == null) { q.Buffs.Add(new SimBuff { Mods = mods, Until = until, SourceSlot = healer.Slot, WeaponId = AspersioBuffId }); Recalc(q, false); }
            else { bool ch = b.Mods != mods; b.Mods = mods; b.Until = until; if (ch) Recalc(q, false); }
        }

        // ================================================================================================= the new kinds' uses

        /// <summary>FireOnce for a Dash (on its cooldown): a dash at its target, Range cells at most (through it by a cell).</summary>
        private bool FireDash(SimPlayer p, OwnedWeapon ow, WeaponLevel lv)
        {
            var w = ow.Def;
            if (w.DashOn != DashTrigger.Cooldown || CS(p).DashUntil > Tick) return false;
            int reach = Units(lv.Range > 0 ? lv.Range : 5);
            var t = w.Target == Targeting.Self || w.Target == Targeting.Facing || w.Target == Targeting.LowestHpAlly || w.Target == Targeting.RandomAlly ? Targeting.Nearest : w.Target;
            int target = FindTarget(p.X, p.Y, t, reach, 0);
            if (target < 0) return false;
            var e = Enemies[target];
            long d = HordeMove.ISqrt(Dist2(p.X, p.Y, e.X, e.Y));
            int units = (int)Math.Min(reach, d + Units(1));
            StartDash(p, ow, Math.Atan2(e.Y - p.Y, e.X - p.X), Math.Max(Units(1), units));
            return true;
        }

        /// <summary>FireOnce for a Mark: one enemy (Area 0), a cone toward the aim (Arc), or everyone within Area of the player (Self) or
        /// of the target.</summary>
        private bool FireMark(SimPlayer p, OwnedWeapon ow, WeaponLevel lv)
        {
            var w = ow.Def;
            int range = Units(lv.Range > 0 ? lv.Range : 8);
            int area = lv.Area > 0 ? AreaUnits(p, lv.Area) : 0;
            var hit = new List<int>();
            int cx = p.X, cy = p.Y; double ang = 0;
            if (w.Target == Targeting.Self && area > 0)
            {
                _q.Clear(); _grid.Query(p.X, p.Y, area + Units(1.5), Enemies, _q);
                foreach (int i in _q) { var e = Enemies[i]; int rr = area + e.Radius; if (e.Alive && Dist2(p.X, p.Y, e.X, e.Y) <= (long)rr * rr) hit.Add(i); }
            }
            else
            {
                if (!AimAngle(p, ow, area > 0 && lv.Arc > 0 ? Math.Max(range, area) : range, area, out ang, out int target) || target < 0) return false;
                if (area <= 0) hit.Add(target);
                else if (lv.Arc > 0)
                {
                    double half = lv.Arc * Math.PI / 360.0;
                    _q.Clear(); _grid.Query(p.X, p.Y, area + Units(1.5), Enemies, _q);
                    foreach (int i in _q)
                    {
                        var e = Enemies[i]; int rr = area + e.Radius;
                        if (!e.Alive || Dist2(p.X, p.Y, e.X, e.Y) > (long)rr * rr) continue;
                        double a = Math.Atan2(e.Y - p.Y, e.X - p.X) - ang;
                        while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI;
                        if (Math.Abs(a) <= half) hit.Add(i);
                    }
                }
                else
                {
                    var t0 = Enemies[target]; cx = t0.X; cy = t0.Y;
                    _q.Clear(); _grid.Query(cx, cy, area + Units(1.5), Enemies, _q);
                    foreach (int i in _q) { var e = Enemies[i]; int rr = area + e.Radius; if (e.Alive && Dist2(cx, cy, e.X, e.Y) <= (long)rr * rr) hit.Add(i); }
                }
            }
            if (hit.Count == 0) return false;
            foreach (int i in hit)
            {
                var e = Enemies[i];
                if (!e.Alive) continue;
                MarkEnemy(p, lv, e);
                e.Flags |= HordeEnemyFlags.Marked;
                if (lv.Damage > 0) HitEnemy(p, ow, lv, e, lv.Damage, p.X, p.Y);
                else if (lv.Effects != null) ApplyEffects(p, ow, lv, e, p.X, p.Y);
            }
            if (area > 0 && lv.Arc > 0) AddEvent(HordeEventKind.Swing, HordeEventFlags.None, p.Slot, -1, w.Id, ow.Level, p.X, p.Y, (float)(ang * 180 / Math.PI), lv.Arc, area / (float)U);
            else if (area > 0) AddEvent(HordeEventKind.Nova, HordeEventFlags.None, p.Slot, -1, w.Id, ow.Level, cx, cy, area / (float)U);
            else { var e = Enemies[hit[0]]; AddEvent(HordeEventKind.Strike, HordeEventFlags.None, p.Slot, -1, w.Id, ow.Level, e.X, e.Y, Math.Max(0.6f, e.Radius / (float)U)); }
            return true;
        }

        /// <summary>FireOnce for a Pet: it comes out once and stays for the run (it is stepped with the summons).</summary>
        private bool FirePet(SimPlayer p, OwnedWeapon ow)
        {
            int maxPets = ow.Def.Trans == TransKind.PetTwin ? 2 : 1; // client wave 18 (h18trans): point 30
            int have = 0; foreach (var s in Summons) if (s.OwnerSlot == p.Slot && (s.Weapon == ow || RootId(s.Weapon.Def) == RootId(ow.Def))) { s.Weapon = ow; have++; }
            if (have >= maxPets) return false;
            if (Summons.Count >= HordeLimits.MaxSummons) return false;
            var sm = new SimSummon { Id = _nextSummonId, OwnerSlot = p.Slot, X = p.X + Units(have == 0 ? 1 : -1), Y = p.Y, Weapon = ow, MaxHp = 0, Hp = 0, Until = int.MaxValue, NextAttack = Tick + 1 };
            _nextSummonId = _nextSummonId >= 65535 ? 1 : _nextSummonId + 1;
            sm.X = Clamp(sm.X, MinX, MaxX);
            Summons.Add(sm);
            AddEvent(HordeEventKind.Summon, HordeEventFlags.None, p.Slot, sm.Id, ow.Def.Id, ow.Level, sm.X, sm.Y);
            return true;
        }

        /// <summary>A pet's tick (StepSummons): it follows its owner and strikes, casts, heals or guards by its mode.</summary>
        private void StepPet(SimSummon s, SimPlayer owner)
        {
            var w = s.Weapon.Def; var lv = s.Weapon.Lv;
            int sp = Math.Max(1, Units((lv.Speed > 0 ? lv.Speed : 4.5) / TickRate));
            // where it rests: beside its owner (the falcon circles it)
            double ra = w.Hidden ? (Tick % (TickRate * 3)) * Math.PI * 2 / (TickRate * 3) : (s.Id % 8) * Math.PI / 4;
            int restX = Clamp(owner.X + (int)Math.Round(Math.Cos(ra) * Units(w.Hidden ? 1.5 : 1.0)), MinX, MaxX), restY = Clamp(owner.Y + (int)Math.Round(Math.Sin(ra) * Units(w.Hidden ? 1.5 : 1.0)), MinY, MaxY);
            int gx = restX, gy = restY, target = -1;
            bool act = !owner.Fallen && Tick >= s.NextAttack;
            switch (w.Pet)
            {
                case PetMode.Striker:
                case PetMode.Guard:
                {
                    int leash = Units(w.Pet == PetMode.Guard ? 3 : 8);
                    target = FindTarget(owner.X, owner.Y, Targeting.Nearest, leash, 0);
                    if (target >= 0 && !owner.Fallen) { gx = Enemies[target].X; gy = Enemies[target].Y; }
                    int reach = Units(lv.Range > 0 ? lv.Range : 1.5);
                    if (target >= 0 && act && Dist2(s.X, s.Y, gx, gy) <= (long)(reach + Enemies[target].Radius) * (reach + Enemies[target].Radius))
                    {
                        s.NextAttack = Tick + Math.Max(1, Ticks(lv.Tick));
                        if (lv.Area > 0)
                        {
                            int area = AreaUnits(owner, lv.Area);
                            _q.Clear(); _grid.Query(gx, gy, area + Units(1.5), Enemies, _q);
                            foreach (int j in _q.ToArray()) { var e = Enemies[j]; if (e.Alive && Dist2(gx, gy, e.X, e.Y) <= (long)(area + e.Radius) * (area + e.Radius)) HitEnemy(owner, s.Weapon, lv, e, lv.Damage, s.X, s.Y); }
                            AddEvent(HordeEventKind.Strike, HordeEventFlags.None, owner.Slot, -1, w.Id, s.Weapon.Level, gx, gy, area / (float)U);
                        }
                        else HitEnemy(owner, s.Weapon, lv, Enemies[target], lv.Damage, s.X, s.Y);
                        OnTransPetAct(owner, s, gx, gy); // client wave 18 (h18trans): point 30
                    }
                    if (w.Pet == PetMode.Guard && lv.Buffs != null && lv.Buffs.Length > 0 && !owner.Fallen)
                    {
                        var b = owner.Buffs.Find(x => x.WeaponId == w.Id && x.SourceSlot == owner.Slot);
                        if (b == null) { owner.Buffs.Add(new SimBuff { Mods = lv.Buffs, Until = Tick + 2, SourceSlot = owner.Slot, WeaponId = w.Id }); Recalc(owner, false); }
                        else { bool ch = b.Mods != lv.Buffs; b.Mods = lv.Buffs; b.Until = Tick + 2; if (ch) Recalc(owner, false); }
                    }
                    break;
                }
                case PetMode.Caster:
                    if (act)
                    {
                        target = FindTarget(s.X, s.Y, Targeting.Nearest, Units(lv.Range > 0 ? lv.Range : 7), 0);
                        if (target >= 0)
                        {
                            var e = Enemies[target];
                            ShootFrom(owner, s.Weapon, lv, Math.Atan2(e.Y - s.Y, e.X - s.X), Math.Max(1, Units(8.0 / TickRate)), Units((lv.Range > 0 ? lv.Range : 7) + 1), target, s.X, s.Y);
                            s.NextAttack = Tick + Math.Max(1, Ticks(lv.Tick));
                            OnTransPetAct(owner, s, e.X, e.Y); // client wave 18 (h18trans): point 30
                        }
                    }
                    target = -1;
                    break;
                case PetMode.Healer:
                    if (act)
                    {
                        var q = LowestAlly(owner, Units(lv.Range > 0 ? lv.Range : 6));
                        if (q != null && q.Hp < q.MaxHp)
                        {
                            Heal(owner, q, lv, w.Id);
                            AddEvent(HordeEventKind.Nova, HordeEventFlags.None, owner.Slot, q.Slot, w.Id, s.Weapon.Level, q.X, q.Y, 1f);
                            s.NextAttack = Tick + Math.Max(1, Ticks(lv.Tick));
                            OnTransPetAct(owner, s, q.X, q.Y); // client wave 18 (h18trans): point 30
                        }
                    }
                    break;
            }
            // move toward the goal (its target, or its rest place); it stops at its reach of a target
            long dd = Dist2(s.X, s.Y, gx, gy);
            int stop = target >= 0 ? Units(lv.Range > 0 ? lv.Range : 1.5) * 3 / 4 : Units(0.2);
            if (dd > (long)stop * stop)
            {
                int d = HordeMove.ISqrt(dd);
                int step = Math.Min(sp * (target < 0 && dd > (long)Units(4) * Units(4) ? 2 : 1), d);
                s.X = Clamp(s.X + (int)((long)(gx - s.X) * step / d), MinX, MaxX); s.Y = Clamp(s.Y + (int)((long)(gy - s.Y) * step / d), MinY, MaxY);
            }
        }

        /// <summary>A player's shot from (x, y) (a pet's, a returned or copied shot): Shoot's record, from another place; returns it.</summary>
        private SimFx ShootFrom(SimPlayer p, OwnedWeapon ow, WeaponLevel lv, double ang, int speed, int range, int target, int x, int y)
        {
            var f = NewFx(HordeFxKind.Shot);
            f.OwnerSlot = p.Slot; f.DefId = ow.Def.Id; f.Level = ow.Level; f.X = x; f.Y = y;
            f.VX = (int)Math.Round(Math.Cos(ang) * speed); f.VY = (int)Math.Round(Math.Sin(ang) * speed);
            f.Radius = Units(0.3); f.EndTick = Tick + Math.Max(1, range / Math.Max(1, speed));
            f.Weapon = ow; f.Lv = lv; f.BaseDamage = lv.Damage; f.PierceLeft = lv.Pierce + CountPassiveTrans(p, PassiveTransKind.Deadeye);
            f.BurstArea = lv.Area > 0 && ow.Def.Kind == WeaponKind.Bolt ? AreaUnits(p, lv.Area) : 0;
            f.Target = ow.Def.Homing ? target : -1;
            BeginFx(f);
            return f;
        }

        /// <summary>FireOnce for an Aura that raises the fallen (Resurrection): a fallen ally within Range rises at once. False: none fallen.</summary>
        private bool FireRevive(SimPlayer p, OwnedWeapon ow, WeaponLevel lv)
        {
            int range = Units(lv.Range > 0 ? lv.Range : 8);
            foreach (var q in Players)
            {
                if (q == p || !q.Present || !q.Fallen || Dist2(q.X, q.Y, p.X, p.Y) > (long)range * range) continue;
                q.Fallen = false; q.Revive = 0;
                q.Hp = Math.Max(1, (int)Math.Round(q.MaxHp * Math.Min(100, lv.RevivePct) / 100.0));
                q.SafeUntil = Tick + Ticks(R.ReviveSafeSeconds);
                p.Revives++;
                foreach (var w in q.Weapons) w.NextTick = Math.Max(w.NextTick, Tick + 1);
                AddEvent(HordeEventKind.Rose, HordeEventFlags.None, q.Slot, p.Slot, ow.Def.Id, 0, q.X, q.Y);
                AddEvent(HordeEventKind.Nova, HordeEventFlags.None, p.Slot, q.Slot, ow.Def.Id, ow.Level, q.X, q.Y, 1.5f);
                return true;
            }
            return false;
        }

        /// <summary>The weapons a player's passives give (the falcon): stepped by StepWeapons after its own.</summary>
        private List<OwnedWeapon> HiddenWeapons(SimPlayer p) => CS(p).Hidden;

        // ================================================================================================= for tests and tools

        /// <summary>Tests and the balance tool: one card applied as if the player had chosen it (no offer, no pause).</summary>
        public void Give(int slot, HordeCard c)
        {
            var p = Player(slot);
            if (p == null) return;
            int n = OffersClosed.Count;
            ApplyCard(p, new HordeOffer { Id = 0 }, c);
            if (OffersClosed.Count > n) OffersClosed.RemoveAt(n); // (no offer was open)
        }

        /// <summary>Tests: a card offer drawn for the player now, with the run's dice.</summary>
        public HordeCard[] DrawFor(int slot) { var p = Player(slot); return p == null ? new HordeCard[0] : DrawCards(p); }

        /// <summary>Tests: a player's Mark is on that enemy now.</summary>
        public bool IsMarked(int enemySlot) => enemySlot >= 0 && enemySlot < Enemies.Length && Enemies[enemySlot].Alive && MarkedNow(Enemies[enemySlot]);

        /// <summary>Tests: the charges left on a player's guard of that weapon (0: down).</summary>
        public int GuardCharges(int slot, int weaponId)
        {
            var p = Player(slot); if (p == null) return 0;
            foreach (var kv in CS(p).Guards) if (kv.Key.Def.Id == weaponId || RootId(kv.Key.Def) == weaponId) return kv.Value.Charges > 0 && kv.Value.Until > Tick ? kv.Value.Charges : 0;
            return 0;
        }

        /// <summary>Tests: the element a player's weapon hits with now (its own, or endowed).</summary>
        public Element ElementOf(int slot, WeaponDef w) { var p = Player(slot); return p == null ? w.Element : CardsElement(p, w); }
    }
}
