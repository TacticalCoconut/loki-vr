#nullable disable
using System;
using System.Collections.Generic;

namespace Loki.Horde.Content
{
    // ====================================================================================================================
    // Client wave 18 (h18trans), point 30 of the design's Horde list:
    //   - Every non-hidden base weapon gets TWO transcendence branches (5000 + id and 6000 + id) offered side by side
    //     after the weapon reaches its maximum level.
    //   - Every base passive gets a transcended form (1000 + id) offered after the passive reaches its maximum level.
    //   - Transcended items keep their original effect (scaled by 1.3x at their terminal level) and gain a completely
    //     reworked mechanic (TransKind / PassiveTransKind) executed in HordeSim.Transcend.cs.
    // ====================================================================================================================
    public static partial class HordeTables
    {
        static float TrF(double v) => (float)v;
        static int TrI(double v) => (int)Math.Round(v);

        public static void AddTranscendW18(HordeContent c)
        {
            AddWeaponTranscendW18(c);
            AddPassiveTranscendW18(c);
        }

        static void AddWeaponTranscendW18(HordeContent c)
        {
            var bases = new List<WeaponDef>();
            foreach (var w in c.Weapons)
                if (!w.Hidden && w.EvolveOf == 0 && w.BranchOf == 0 && w.TransOf == 0 && w.Levels != null && w.Levels.Length > 0)
                    bases.Add(w);

            foreach (var w in bases)
            {
                var t1 = BuildTransWeapon(w, 5000 + w.Id, w.Key + "_t1", 1);
                var t2 = BuildTransWeapon(w, 6000 + w.Id, w.Key + "_t2", 2);
                var pair = new[] { t1.Id, t2.Id };
                w.TransBranches = pair;
                var b1 = c.Weapon(1000 + w.Id); if (b1 != null) b1.TransBranches = pair;
                var b2 = c.Weapon(2000 + w.Id); if (b2 != null) b2.TransBranches = pair;
                c.Weapons.Add(t1);
                c.Weapons.Add(t2);
            }
        }

        static WeaponDef BuildTransWeapon(WeaponDef w, int id, string key, int branch)
        {
            var top = w.Levels[w.Levels.Length - 1];
            var l = CopyTransLevel(top);

            // Base transcendence stat boost (+30% power, 15% faster cooldown, stronger buffs/effects)
            if (l.Damage > 0) l.Damage = Math.Max(l.Damage + 2, TrI(l.Damage * 1.3));
            if (l.Heal > 0) l.Heal = Math.Max(l.Heal + 2, TrI(l.Heal * 1.3));
            if (l.HealPct > 0) l.HealPct = TrF(l.HealPct * 1.3);
            if (l.Cooldown > 0.25f) l.Cooldown = TrF(Math.Max(0.2, l.Cooldown * 0.85));
            if (l.Buffs != null)
                foreach (var m in l.Buffs)
                    if (m != null) m.Value = TrF(m.Value * 1.3);
            if (l.Effects != null)
                foreach (var e in l.Effects)
                    if (e != null)
                    {
                        if (e.Kind == EffectKind.Knockback || e.Kind == EffectKind.Slow) e.Power = TrF(e.Power * 1.25);
                        else { e.Chance = Math.Min(100f, TrF(e.Chance * 1.25)); e.Duration = TrF(e.Duration * 1.25); }
                    }

            TransKind tk = PickTransKind(w, branch);
            string tName = TransBranchName(tk);
            ApplyTransBranchLevel(w, l, tk);

            return new WeaponDef
            {
                Id = id,
                Key = key,
                SkillId = w.SkillId,
                ItemId = w.ItemId,
                Kind = w.Kind,
                Target = w.Target,
                Class = w.Class,
                Element = w.Element,
                Zone = w.Zone,
                Homing = w.Homing,
                SummonMobId = w.SummonMobId,
                SummonHp = tk == TransKind.SummonExplode ? Math.Max(60, TrI(w.SummonHp * 1.6)) : Math.Max(w.SummonHp, TrI(w.SummonHp * 1.3)),
                Weight = 0,
                Levels = new[] { l },
                DashOn = w.DashOn,
                Parry = w.Parry,
                Pet = w.Pet,
                HitsBySize = w.HitsBySize,
                RandomOf = w.RandomOf,
                CastOnHit = w.CastOnHit,
                BonusCountFrom = w.BonusCountFrom,
                ComboPassive = w.ComboPassive,
                ComboWeapon = w.ComboWeapon,
                SummonBursts = w.SummonBursts,
                Group = w.Group,
                TransOf = w.Id,
                TransBranch = branch,
                TransName = tName,
                Trans = tk,
            };
        }

        public static TransKind PickTransKind(WeaponDef w, int branch)
        {
            bool one = branch == 1;
            if (w.RandomOf != null && w.RandomOf.Length > 0)
                return one ? TransKind.RandomTwin : TransKind.RandomOverload;
            switch (w.Kind)
            {
                case WeaponKind.Bolt: return one ? TransKind.BoltNova : TransKind.BoltReturn;
                case WeaponKind.MeleeArc: return one ? TransKind.ArcWave : TransKind.ArcVortex;
                case WeaponKind.Nova: return one ? TransKind.NovaEcho : TransKind.NovaImplode;
                case WeaponKind.AreaAtTarget: return one ? TransKind.AreaCascade : TransKind.AreaScorch;
                case WeaponKind.GroundZone: return one ? TransKind.ZoneDrift : TransKind.ZoneCollapse;
                case WeaponKind.Orbiter: return one ? TransKind.OrbitSpiral : TransKind.OrbitShoot;
                case WeaponKind.Chain: return one ? TransKind.ChainFork : TransKind.ChainOverload;
                case WeaponKind.Aura: return one ? TransKind.AuraPulse : TransKind.AuraNova;
                case WeaponKind.Summon: return one ? TransKind.SummonLegion : TransKind.SummonExplode;
                case WeaponKind.Trap: return one ? TransKind.TrapSeeker : TransKind.TrapCluster;
                case WeaponKind.Dash: return one ? TransKind.DashMirage : TransKind.DashNova;
                case WeaponKind.Mark: return one ? TransKind.MarkSpread : TransKind.MarkDetonate;
                case WeaponKind.Parry: return one ? TransKind.ParryTempest : TransKind.ParryPrism;
                case WeaponKind.Pet: return one ? TransKind.PetTwin : TransKind.PetApex;
                default: return one ? TransKind.NovaEcho : TransKind.NovaImplode;
            }
        }

        public static string TransBranchName(TransKind k)
        {
            switch (k)
            {
                case TransKind.BoltNova: return "Starburst";
                case TransKind.BoltReturn: return "Boomerang";
                case TransKind.ArcWave: return "Shockwave";
                case TransKind.ArcVortex: return "Whirlwind";
                case TransKind.NovaEcho: return "Aftershock";
                case TransKind.NovaImplode: return "Singularity";
                case TransKind.AreaCascade: return "Cataclysm";
                case TransKind.AreaScorch: return "Scorched Earth";
                case TransKind.ZoneDrift: return "Living Storm";
                case TransKind.ZoneCollapse: return "Supernova";
                case TransKind.OrbitSpiral: return "Breathing Orbit";
                case TransKind.OrbitShoot: return "Starlight Volley";
                case TransKind.ChainFork: return "Forked Lightning";
                case TransKind.ChainOverload: return "Arc Overload";
                case TransKind.AuraPulse: return "Sanctified Pulse";
                case TransKind.AuraNova: return "Radiant Detonation";
                case TransKind.SummonLegion: return "Legion";
                case TransKind.SummonExplode: return "Volatile Kin";
                case TransKind.TrapSeeker: return "Seeker Mines";
                case TransKind.TrapCluster: return "Cluster Mines";
                case TransKind.DashMirage: return "Phantom Afterimage";
                case TransKind.DashNova: return "Thunderous Arrival";
                case TransKind.MarkSpread: return "Contagion";
                case TransKind.MarkDetonate: return "Doom Brand";
                case TransKind.ParryTempest: return "Aegis Tempest";
                case TransKind.ParryPrism: return "Mirror Prism";
                case TransKind.PetTwin: return "Twin Companions";
                case TransKind.PetApex: return "Apex Companion";
                case TransKind.RandomTwin: return "Twin Chorus";
                case TransKind.RandomOverload: return "Grand Arcanum";
                default: return "Ascended";
            }
        }

        static void ApplyTransBranchLevel(WeaponDef w, WeaponLevel l, TransKind tk)
        {
            switch (tk)
            {
                case TransKind.BoltNova:
                    l.Count = Math.Max(8, l.Count + 6);
                    l.Pierce += 1;
                    l.Spread = 360f;
                    break;
                case TransKind.BoltReturn:
                    l.Damage = Math.Max(1, TrI(l.Damage * 1.15));
                    l.Pierce = 99;
                    l.Falloff = 100f;
                    l.Range = TrF(Math.Max(l.Range, 6f) * 1.2);
                    break;
                case TransKind.ArcWave:
                    l.Range = TrF(l.Range * 1.2);
                    break;
                case TransKind.ArcVortex:
                    l.Arc = 360f;
                    l.Range = TrF(l.Range * 1.35);
                    l.Damage = Math.Max(1, TrI(l.Damage * 1.15));
                    break;
                case TransKind.NovaEcho:
                    l.Area = TrF(l.Area * 1.25);
                    break;
                case TransKind.NovaImplode:
                    l.Area = TrF(l.Area * 1.2);
                    l.Damage = Math.Max(1, TrI(l.Damage * 1.15));
                    break;
                case TransKind.AreaCascade:
                    l.Area = TrF(l.Area * 1.15);
                    break;
                case TransKind.AreaScorch:
                    l.Damage = Math.Max(1, TrI(l.Damage * 1.15));
                    l.Area = TrF(l.Area * 1.2);
                    break;
                case TransKind.ZoneDrift:
                    l.Area = TrF(l.Area * 1.35);
                    l.Duration = TrF(l.Duration * 1.25);
                    break;
                case TransKind.ZoneCollapse:
                    if (l.Damage > 0) l.Damage = Math.Max(1, TrI(l.Damage * 1.2));
                    l.Area = TrF(l.Area * 1.2);
                    break;
                case TransKind.OrbitSpiral:
                    l.Count += 2;
                    l.Area = TrF(l.Area * 1.2);
                    l.Speed = TrF(l.Speed * 1.2);
                    break;
                case TransKind.OrbitShoot:
                    l.Count += 1;
                    l.Damage = Math.Max(1, TrI(l.Damage * 1.2));
                    break;
                case TransKind.ChainFork:
                    l.Count += 2;
                    l.Falloff = 100f;
                    break;
                case TransKind.ChainOverload:
                    l.Damage = Math.Max(1, TrI(l.Damage * 1.2));
                    l.Area = TrF(l.Area + 1.2);
                    break;
                case TransKind.AuraPulse:
                    l.Area = TrF(Math.Max(l.Area, 2.2f) * 1.4);
                    if (w.Target == Targeting.LowestHpAlly) l.Range = TrF(Math.Max(l.Range, 2.2f) * 1.4);
                    if (l.RevivePct > 0) l.RevivePct = 100f;
                    break;
                case TransKind.AuraNova:
                    l.Area = TrF(Math.Max(l.Area, 2.2f) * 1.25);
                    if (l.RevivePct > 0) l.RevivePct = 100f;
                    break;
                case TransKind.SummonLegion:
                    l.Count += 2;
                    l.Speed = TrF(Math.Max(l.Speed, 4f) * 1.4);
                    l.Area = TrF(Math.Max(l.Area, 1.5f));
                    break;
                case TransKind.SummonExplode:
                    l.Damage = Math.Max(1, TrI(l.Damage * 1.25));
                    l.Tick = TrF(Math.Max(0.25, l.Tick * 0.75));
                    break;
                case TransKind.TrapSeeker:
                    l.Count += 2;
                    l.Area = TrF(l.Area * 1.2);
                    break;
                case TransKind.TrapCluster:
                    l.Damage = Math.Max(1, TrI(l.Damage * 1.2));
                    l.Area = TrF(l.Area * 1.25);
                    break;
                case TransKind.DashMirage:
                    l.Range = TrF(l.Range * 1.3);
                    l.Cooldown = TrF(Math.Max(0.4, l.Cooldown * 0.8));
                    break;
                case TransKind.DashNova:
                    l.Damage = Math.Max(45, TrI(l.Damage * 1.3));
                    l.Range = TrF(l.Range * 1.2);
                    break;
                case TransKind.MarkSpread:
                    l.Count += 2;
                    l.Area = TrF(Math.Max(l.Area, 2.5f) * 1.3);
                    l.MarkPct = TrF(Math.Max(l.MarkPct, 20f) * 1.25);
                    break;
                case TransKind.MarkDetonate:
                    l.MarkPct = TrF(Math.Max(l.MarkPct, 25f) * 1.35);
                    l.Duration = TrF(Math.Max(l.Duration, 4f) * 1.25);
                    break;
                case TransKind.ParryTempest:
                    l.Count += 1;
                    l.Damage = Math.Max(80, TrI(l.Damage * 1.4));
                    l.Area = TrF(Math.Max(l.Area, 2.8f));
                    break;
                case TransKind.ParryPrism:
                    l.Count += 2;
                    l.Cooldown = TrF(Math.Max(0.5, l.Cooldown * 0.75));
                    l.Damage = Math.Max(70, TrI(l.Damage * 1.3));
                    break;
                case TransKind.PetTwin:
                    l.Tick = TrF(Math.Max(0.25, l.Tick * 0.8));
                    break;
                case TransKind.PetApex:
                    if (l.Damage > 0) l.Damage = Math.Max(1, TrI(l.Damage * 1.35));
                    if (l.Heal > 0) l.Heal = Math.Max(1, TrI(l.Heal * 1.35));
                    if (l.HealPct > 0) l.HealPct = TrF(l.HealPct * 1.35);
                    break;
                case TransKind.RandomTwin:
                    l.Cooldown = TrF(Math.Max(0.25, l.Cooldown * 0.75));
                    if (w.CastOnHit) l.ProcPct = Math.Min(100f, TrF(l.ProcPct * 1.35));
                    break;
                case TransKind.RandomOverload:
                    l.Count += 2;
                    if (w.CastOnHit) l.ProcPct = Math.Min(100f, TrF(l.ProcPct * 1.25));
                    break;
            }
        }

        static WeaponLevel CopyTransLevel(WeaponLevel l)
        {
            var n = l.Clone();
            if (l.Effects != null)
            {
                n.Effects = new EffectDef[l.Effects.Length];
                for (int i = 0; i < l.Effects.Length; i++)
                    n.Effects[i] = l.Effects[i] == null ? null : new EffectDef(l.Effects[i].Kind, l.Effects[i].Chance, l.Effects[i].Duration, l.Effects[i].Power, l.Effects[i].Tick);
            }
            if (l.Buffs != null)
            {
                n.Buffs = new ModDef[l.Buffs.Length];
                for (int i = 0; i < l.Buffs.Length; i++)
                    n.Buffs[i] = l.Buffs[i] == null ? null : new ModDef(l.Buffs[i].Kind, l.Buffs[i].Value, l.Buffs[i].Arg);
            }
            return n;
        }

        // ================================================================================================================
        // PASSIVE TRANSCENDENCE (1000 + id)
        // ================================================================================================================
        static void AddPassiveTranscendW18(HordeContent c)
        {
            var bases = new List<PassiveDef>();
            foreach (var p in c.Passives)
                if (p.TransOf == 0 && p.Levels != null && p.Levels.Length > 0)
                    bases.Add(p);

            foreach (var p in bases)
                c.Passives.Add(BuildTransPassive(p));
        }

        static PassiveDef BuildTransPassive(PassiveDef p)
        {
            var top = p.Levels[p.Levels.Length - 1];
            ModDef[] transMods;
            if (top != null && top.Length > 0)
            {
                transMods = new ModDef[top.Length];
                for (int i = 0; i < top.Length; i++)
                {
                    var m = top[i];
                    if (m == null) continue;
                    // Note: CooldownPct, WeaponCooldownPct, DamageTaken*, and ProvidencePct are beneficial when negative!
                    bool negGood = m.Kind == Mod.CooldownPct || m.Kind == Mod.WeaponCooldownPct || m.Kind == Mod.DamageTakenPct
                        || m.Kind == Mod.DamageTakenVsRacePct || m.Kind == Mod.DamageTakenVsElementPct || m.Kind == Mod.ProvidencePct;
                    bool beneficial = negGood ? m.Value < 0 : m.Value > 0;
                    float val = m.Kind == Mod.EndowElement
                        ? m.Value
                        : m.Kind == Mod.CancelStrikeEvery
                            ? TrF(Math.Max(1.0, m.Value * 0.75))
                            : beneficial ? TrF(m.Value * 1.35) : TrF(m.Value * 0.5);
                    transMods[i] = new ModDef(m.Kind, val, m.Arg);
                }
            }
            else
            {
                transMods = new[] { new ModDef(Mod.DamagePct, 15f) };
            }

            PassiveTransKind ptk = PickPassiveTransKind(p, top);
            PassiveProcDef proc = null;
            if (p.Proc != null)
            {
                int lv = Math.Max(1, p.Levels.Length);
                var pd = p.Proc;
                proc = new PassiveProcDef
                {
                    Kind = pd.Kind,
                    Body = pd.Body,
                    Count = pd.Count > 1 ? Math.Max(1, pd.Count - 1) : pd.Count,
                    Seconds = TrF(Math.Max(0.2, PassiveProcDef.At(pd.Seconds, pd.SecondsPerLevel, lv) * 0.8)),
                    SecondsPerLevel = 0f,
                    Power = TrF(PassiveProcDef.At(pd.Power, pd.PowerPerLevel, lv) * 1.35),
                    PowerPerLevel = 0f,
                    Damage = TrF(PassiveProcDef.At(pd.Damage, pd.DamagePerLevel, lv) * 1.35),
                    DamagePerLevel = 0f,
                    Area = TrF(PassiveProcDef.At(pd.Area, pd.AreaPerLevel, lv) * 1.25),
                    AreaPerLevel = 0f,
                    Chance = Math.Min(100f, TrF(PassiveProcDef.At(pd.Chance, pd.ChancePerLevel, lv) * 1.35)),
                    ChancePerLevel = 0f,
                    Threshold = pd.Threshold,
                    Mul = pd.Mul > 1f ? TrF(1.0 + (pd.Mul - 1.0) * 1.35) : pd.Mul,
                    Cooldown = TrF(Math.Max(0.2, PassiveProcDef.At(pd.Cooldown, pd.CooldownPerLevel, lv) * 0.75)),
                    CooldownPerLevel = 0f,
                    Arg = pd.Arg,
                    PickupId = pd.PickupId,
                    HitEffect = pd.HitEffect,
                    HitEffectSeconds = TrF(pd.HitEffectSeconds * 1.3),
                    ClassMask = pd.ClassMask,
                    RaceMask = pd.RaceMask,
                    ElementMask = pd.ElementMask,
                    SizeMask = pd.SizeMask,
                    RankMask = pd.RankMask,
                    BodyMob = pd.BodyMob,
                    BodyItem = pd.BodyItem,
                };
            }

            return new PassiveDef
            {
                Id = 1000 + p.Id,
                Key = p.Key + "_t",
                SkillId = p.SkillId,
                ItemId = p.ItemId,
                Weight = 0,
                Levels = new[] { transMods },
                Group = p.Group,
                GivesWeapon = p.GivesWeapon,
                Proc = proc,
                TransOf = p.Id,
                TransName = PassiveTransName(ptk),
                Trans = ptk,
            };
        }

        public static PassiveTransKind PickPassiveTransKind(PassiveDef p, ModDef[] top)
        {
            if (p.GivesWeapon > 0) return PassiveTransKind.Deadeye;
            if (top != null && top.Length > 0 && top[0] != null)
            {
                switch (top[0].Kind)
                {
                    case Mod.MaxHp:
                    case Mod.MaxHpPct:
                    case Mod.Regen:
                    case Mod.MovingRegen:
                    case Mod.Defence:
                    case Mod.DamageTakenPct:
                    case Mod.DamageTakenVsRacePct:
                    case Mod.DamageTakenVsElementPct:
                    case Mod.ProvidencePct:
                    case Mod.Vit:
                    case Mod.CancelStrikeCells:
                    case Mod.CancelStrikeEvery:
                        return PassiveTransKind.VitalFortress;
                    case Mod.MoveSpeedPct:
                    case Mod.Dodge:
                    case Mod.Agi:
                        return PassiveTransKind.Windrunner;
                    case Mod.DamagePct:
                    case Mod.MeleeDamagePct:
                    case Mod.MovingMeleePct:
                    case Mod.OffhandPct:
                    case Mod.ArcPct:
                    case Mod.Str:
                    case Mod.KnockbackPct:
                    case Mod.WeaponDamagePct:
                        return PassiveTransKind.Warlord;
                    case Mod.RangedDamagePct:
                    case Mod.ProjectileCount:
                    case Mod.ProjectileSpeedPct:
                    case Mod.TrapPct:
                    case Mod.Dex:
                        return PassiveTransKind.Deadeye;
                    case Mod.MagicDamagePct:
                    case Mod.CooldownPct:
                    case Mod.WeaponCooldownPct:
                    case Mod.AreaPct:
                    case Mod.DurationPct:
                    case Mod.EchoPct:
                    case Mod.Int:
                    case Mod.ElementDamagePct:
                    case Mod.EndowElement:
                        return PassiveTransKind.Archmage;
                    case Mod.CritChance:
                    case Mod.CritDamagePct:
                    case Mod.Luk:
                    case Mod.EffectChancePct:
                    case Mod.StunOnHitPct:
                    case Mod.PoisonOnHitPct:
                        return PassiveTransKind.FortuneCrit;
                    case Mod.DamageVsRacePct:
                    case Mod.DamageVsElementPct:
                    case Mod.DamageVsBossPct:
                    case Mod.DamageVsSizePct:
                    case Mod.MagicVsBossPct:
                        return PassiveTransKind.Slayer;
                    case Mod.PickupRadiusPct:
                    case Mod.ExpGainPct:
                    case Mod.HealPowerPct:
                    case Mod.ReviveSpeedPct:
                    case Mod.HpOnKill:
                        return PassiveTransKind.Harvester;
                }
            }
            if (p.Key == "mg_mysticfocus" || p.Key == "hw_souldrain") return PassiveTransKind.Archmage;
            if (p.Key == "ac_makingarrow") return PassiveTransKind.Deadeye;
            return PassiveTransKind.Warlord;
        }

        public static string PassiveTransName(PassiveTransKind k)
        {
            switch (k)
            {
                case PassiveTransKind.VitalFortress: return "Immortal Bastion";
                case PassiveTransKind.Windrunner: return "Tempest Stride";
                case PassiveTransKind.Warlord: return "Overwhelming Force";
                case PassiveTransKind.Deadeye: return "Ricochet Mastery";
                case PassiveTransKind.Archmage: return "Arcane Convergence";
                case PassiveTransKind.FortuneCrit: return "Fated Execution";
                case PassiveTransKind.Slayer: return "Bane Mastery";
                case PassiveTransKind.Harvester: return "Soul Harvest";
                default: return "Ascended";
            }
        }
    }
}
