#nullable disable
using System.Collections.Generic;
using Loki.Horde;

namespace LokiHost.Tests
{
    /// <summary>
    /// The engine tests' own tables (w8core): a copy of the first placeholder, kept apart from w8content's HordeTables so that
    /// the engine's rule tests (falling and rising, determinism, the relay's run with bots) do not change with the game's
    /// balance. Every number is a test value (Loki's own, not official); Ragnarok things by rAthena's ids only.
    /// </summary>
    public static class HordeTestContent
    {
        public static HordeContent Build()
        {
            var c = new HordeContent();
            var r = c.Rules;
            r.StartWeapon = 1;
            r.FinalBossEnemyId = 90;
            r.ExpTable = new[] { 5, 8, 12, 16, 20, 25, 30, 36, 42, 50, 58, 66, 75, 85, 95 };
            r.JobMarks = new[] { 5, 15, 30, 45, 60 };

            c.Arenas.Add(new ArenaDef { Id = 1, Map = "prt_fild08", X0 = 130, Y0 = 130, X1 = 220, Y1 = 220 });

            // ---- weapons (SkillId = rAthena skill ids: NV_BASIC 1 is not an attack; the placeholder borrows these)
            c.Weapons.Add(new WeaponDef { Id = 1, Key = "novice_bolt", SkillId = 0, Kind = WeaponKind.Bolt, Class = DamageClass.Ranged, Weight = 0,
                Levels = Lv(L(dmg: 8, cd: 1.0f, count: 1, speed: 10, range: 8), L(dmg: 10, cd: 0.9f, count: 1, speed: 10, range: 8), L(dmg: 12, cd: 0.8f, count: 2, speed: 11, range: 9, spread: 10)) });
            c.Weapons.Add(new WeaponDef { Id = 2, Key = "sm_bash", SkillId = 5, Kind = WeaponKind.MeleeArc, Class = DamageClass.Melee, Weight = 0,
                Levels = Lv(L(dmg: 20, cd: 1.2f, range: 2.2f, arc: 120, fx: E(EffectKind.Stun, 10, 1f))) });
            c.Weapons.Add(new WeaponDef { Id = 3, Key = "sm_magnum", SkillId = 7, Kind = WeaponKind.Nova, Class = DamageClass.Melee, Element = Element.Fire,
                Levels = Lv(L(dmg: 15, cd: 3f, area: 2.5f, fx: E(EffectKind.Knockback, 100, 0, 1.5f)), L(dmg: 20, cd: 2.7f, area: 2.8f, fx: E(EffectKind.Knockback, 100, 0, 1.5f))) });
            c.Weapons.Add(new WeaponDef { Id = 4, Key = "mg_firebolt", SkillId = 19, Kind = WeaponKind.Bolt, Class = DamageClass.Magic, Element = Element.Fire, Weight = 0,
                Levels = Lv(L(dmg: 14, cd: 1.1f, count: 2, speed: 9, range: 9, spread: 12), L(dmg: 16, cd: 1.0f, count: 3, speed: 9, range: 9, spread: 12)) });
            c.Weapons.Add(new WeaponDef { Id = 5, Key = "mg_firewall", SkillId = 18, Kind = WeaponKind.GroundZone, Class = DamageClass.Magic, Element = Element.Fire, Target = Targeting.Densest,
                Levels = Lv(L(dmg: 6, cd: 5f, range: 7, area: 1.5f, duration: 4f, tick: 0.5f), L(dmg: 8, cd: 4.5f, range: 7, area: 1.8f, duration: 4f, tick: 0.5f)) });
            c.Weapons.Add(new WeaponDef { Id = 6, Key = "mg_frostdiver_orb", SkillId = 15, Kind = WeaponKind.Orbiter, Class = DamageClass.Magic, Element = Element.Water,
                Levels = Lv(L(dmg: 8, count: 2, range: 2f, area: 0.5f, speed: 0.5f, tick: 0.5f, fx: E(EffectKind.Freeze, 15, 1.5f))) });

            // ---- passives (SkillId: SM_SWORD 2, SM_RECOVERY 4, MG_SRECOVERY 9)
            c.Passives.Add(new PassiveDef { Id = 1, Key = "sm_recovery", SkillId = 4, Levels = new[] { M(new ModDef(Mod.Regen, 0.5f)), M(new ModDef(Mod.Regen, 1f)), M(new ModDef(Mod.Regen, 1.5f)) } });
            c.Passives.Add(new PassiveDef { Id = 2, Key = "sm_sword", SkillId = 2, Levels = new[] { M(new ModDef(Mod.MeleeDamagePct, 10)), M(new ModDef(Mod.MeleeDamagePct, 20)) } });
            c.Passives.Add(new PassiveDef { Id = 3, Key = "mg_srecovery", SkillId = 9, Levels = new[] { M(new ModDef(Mod.CooldownPct, -8)), M(new ModDef(Mod.CooldownPct, -15)) } });

            c.Stats.Add(new StatDef { Stat = Stat.Str, PerPoint = M(new ModDef(Mod.MeleeDamagePct, 2)) });
            c.Stats.Add(new StatDef { Stat = Stat.Agi, PerPoint = M(new ModDef(Mod.MoveSpeedPct, 1), new ModDef(Mod.CooldownPct, -0.5f)) });
            c.Stats.Add(new StatDef { Stat = Stat.Vit, PerPoint = M(new ModDef(Mod.MaxHpPct, 2), new ModDef(Mod.Regen, 0.05f)) });
            c.Stats.Add(new StatDef { Stat = Stat.Int, PerPoint = M(new ModDef(Mod.MagicDamagePct, 2)) });
            c.Stats.Add(new StatDef { Stat = Stat.Dex, PerPoint = M(new ModDef(Mod.RangedDamagePct, 2), new ModDef(Mod.CooldownPct, -0.3f)) });
            c.Stats.Add(new StatDef { Stat = Stat.Luk, PerPoint = M(new ModDef(Mod.CritChance, 0.5f), new ModDef(Mod.ExpGainPct, 1)) });

            // ---- jobs (rAthena job ids: JOB_NOVICE 0, JOB_SWORDMAN 1, JOB_MAGE 2)
            c.Jobs.Add(new JobDef { JobId = 0, Key = "JOB_NOVICE", Tier = 0, Next = new[] { 1, 2 }, Weapons = new int[0], Passives = new[] { 1 } });
            c.Jobs.Add(new JobDef { JobId = 1, Key = "JOB_SWORDMAN", Tier = 1, SignatureWeapon = 2, Weapons = new[] { 3 }, Passives = new[] { 2 },
                Mods = M(new ModDef(Mod.MaxHpPct, 20)), StatBonus = new[] { 3, 0, 3, 0, 0, 0 } });
            c.Jobs.Add(new JobDef { JobId = 2, Key = "JOB_MAGE", Tier = 1, SignatureWeapon = 4, Weapons = new[] { 5, 6 }, Passives = new[] { 3 },
                Mods = M(new ModDef(Mod.MagicDamagePct, 10)), StatBonus = new[] { 0, 0, 0, 4, 2, 0 } });

            // ---- pickups (items: Jellopy 909, Stone 7049, Red_Potion 501, Gift_Box 644, Old_Blue_Box 603)
            c.Pickups.Add(new PickupDef { Id = 1, Key = "exp_small", Kind = PickupKind.Exp, ItemId = 909, Value = 1 });
            c.Pickups.Add(new PickupDef { Id = 2, Key = "exp_large", Kind = PickupKind.Exp, ItemId = 7049, Value = 10 });
            c.Pickups.Add(new PickupDef { Id = 3, Key = "heal", Kind = PickupKind.Heal, ItemId = 501, Value = 30 });
            c.Pickups.Add(new PickupDef { Id = 4, Key = "magnet", Kind = PickupKind.Magnet, ItemId = 644 });
            c.Pickups.Add(new PickupDef { Id = 5, Key = "chest", Kind = PickupKind.Chest, ItemId = 603, Value = 1 });

            // ---- enemies (mobs: PORING 1002, FABRE 1007, POPORING 1031, DROPS 1113, ANGELING 1096, OSIRIS 1038)
            c.Enemies.Add(new EnemyDef { Id = 1, MobId = 1002, Key = "PORING", Hp = 12, Damage = 4, Speed = 1.6f, Exp = 1, Element = Element.Water, Race = Race.Plant, Drops = new[] { new DropDef(3, 1f) } });
            c.Enemies.Add(new EnemyDef { Id = 2, MobId = 1007, Key = "FABRE", Hp = 18, Damage = 5, Speed = 1.4f, Exp = 2, Element = Element.Earth, Race = Race.Insect, Size = UnitSize.Small });
            c.Enemies.Add(new EnemyDef { Id = 3, MobId = 1113, Key = "DROPS", Hp = 25, Damage = 7, Speed = 1.9f, Exp = 3, Element = Element.Fire, Race = Race.Plant, Drops = new[] { new DropDef(4, 0.5f) } });
            c.Enemies.Add(new EnemyDef { Id = 10, MobId = 1031, Key = "POPORING", Rank = EnemyRank.Elite, Hp = 300, Damage = 10, Speed = 1.5f, Radius = 0.8f, Exp = 25, Element = Element.Poison, Race = Race.Plant, DrawScale = 150, KnockbackResist = 60, Drops = new[] { new DropDef(5, 100f) } });
            c.Enemies.Add(new EnemyDef { Id = 20, MobId = 1096, Key = "ANGELING", Rank = EnemyRank.Boss, Hp = 2500, Damage = 18, Speed = 1.3f, Radius = 1f, Exp = 100, Element = Element.Holy, Race = Race.Angel, KnockbackResist = 100, EffectResist = 100, DrawScale = 160,
                Abilities = new[] { new EnemyAbilityDef { Kind = AbilityKind.SummonAdds, Cooldown = 12, FirstAfter = 4, Count = 6, EnemyId = 1 } }, Drops = new[] { new DropDef(5, 100f) } });
            c.Enemies.Add(new EnemyDef { Id = 90, MobId = 1038, Key = "OSIRIS", Rank = EnemyRank.FinalBoss, Hp = 12000, Damage = 25, Speed = 1.4f, Radius = 1.2f, Exp = 0, Element = Element.Undead, Race = Race.Undead, KnockbackResist = 100, EffectResist = 100, DrawScale = 180,
                Abilities = new[] { new EnemyAbilityDef { Kind = AbilityKind.AreaAtPlayers, Cooldown = 6, FirstAfter = 3, Damage = 30, Area = 2f, Delay = 1.5f, Count = 2 } } });

            // ---- waves (seconds of the 900 s reference run)
            c.Waves.Add(new WaveDef { From = 0, To = 300, EnemyId = 1, Interval = 1.0f, Count = 2, Pattern = SpawnPattern.Ring });
            c.Waves.Add(new WaveDef { From = 120, To = 600, EnemyId = 2, Interval = 1.5f, Count = 3, Pattern = SpawnPattern.Ring });
            c.Waves.Add(new WaveDef { From = 300, To = 900, EnemyId = 3, Interval = 1.0f, Count = 3, Pattern = SpawnPattern.Edge });
            c.Waves.Add(new WaveDef { From = 450, To = 900, EnemyId = 1, Interval = 3f, Count = 12, Pattern = SpawnPattern.Circle });
            c.Waves.Add(new WaveDef { From = 240, EnemyId = 10, Once = true });
            c.Waves.Add(new WaveDef { From = 480, EnemyId = 10, Once = true });
            c.Waves.Add(new WaveDef { From = 600, EnemyId = 20, Once = true });
            return c;
        }

        // ---- helpers for writing tables
        public static WeaponLevel[] Lv(params WeaponLevel[] l) => l;
        public static ModDef[] M(params ModDef[] m) => m;
        public static EffectDef E(EffectKind k, float chance, float duration, float power = 0f, float tick = 1f) => new EffectDef(k, chance, duration, power, tick);
        public static WeaponLevel L(int dmg = 0, float cd = 1f, int count = 1, int pierce = 0, float speed = 0, float range = 0, float area = 0, float arc = 0,
                                    float spread = 0, float burst = 0.1f, float delay = 0, float duration = 0, float tick = 1f, int heal = 0, float healPct = 0,
                                    float falloff = 100f, EffectDef fx = null, ModDef[] buffs = null)
            => new WeaponLevel { Damage = dmg, Cooldown = cd, Count = count, Pierce = pierce, Speed = speed, Range = range, Area = area, Arc = arc, Spread = spread,
                                 Burst = burst, Delay = delay, Duration = duration, Tick = tick, Heal = heal, HealPct = healPct, Falloff = falloff,
                                 Effects = fx != null ? new[] { fx } : null, Buffs = buffs };
    }
}
