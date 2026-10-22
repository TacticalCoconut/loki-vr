#nullable disable
namespace Loki.Horde.Content
{
    // ====================================================================================================================
    // Horde: the job tree, weapons and passives (w8content). EVERY NUMBER HERE IS LOKI'S OWN DESIGN, never an official
    // Ragnarok value. The RO facts quoted per entry come from rAthena (C:\rAthena): "db" = db/re/skill_db.yml
    // (Id, MaxLevel of db/re/skill_tree.yml for that job, Element, HitCount, SplashArea, Knockback, Status), and the
    // source file named where a behaviour matters (src/map/skills/<class>/<skill>.cpp, src/map/*.cpp with line numbers).
    // A weapon's mode levels: 5 for RO skills of 5 or 10 levels (mode level n ~ RO level n or 2n), the skill's own count
    // otherwise; "(RO max 1; the mode's levels 2+ are own)" where a quest skill grows anyway.
    // "PROVISIONAL" marks a mapping onto the closest engine kind where the engine lacks the exact behaviour tonight.
    // ====================================================================================================================
    public static partial class HordeTables
    {
        // ---- rAthena job ids (src/common/mmo.hpp e_job)
        const int JobNovice = 0, JobSwordman = 1, JobMage = 2, JobArcher = 3, JobAcolyte = 4, JobMerchant = 5, JobThief = 6,
            JobKnight = 7, JobPriest = 8, JobWizard = 9, JobBlacksmith = 10, JobHunter = 11, JobAssassin = 12, JobCrusader = 14,
            JobMonk = 15, JobSage = 16, JobRogue = 17, JobAlchemist = 18, JobBard = 19, JobDancer = 20;

        // ---- weapon ids (the mode's own; blocks of 10 per job)
        const int WNvKnife = 1, WNvFirstAid = 2;
        const int WSmBash = 10, WSmMagnum = 11, WSmEndure = 12;
        const int WMgFireBolt = 20, WMgFrostDiver = 21, WMgFireWall = 22, WMgThunderStorm = 23;
        const int WAcDouble = 30, WAcShower = 31, WAcChargeArrow = 32;
        const int WAlHolyLight = 40, WAlHeal = 41, WAlBlessing = 42, WAlIncAgi = 43;
        const int WMcMammonite = 50, WMcCartRevolution = 51, WMcLoud = 52;
        const int WTfPoison = 60, WTfThrowStone = 61;
        const int WKnBowlingBash = 100, WKnBrandishSpear = 101, WKnSpearBoomerang = 102;
        const int WCrHolyCross = 110, WCrGrandCross = 111, WCrShieldBoomerang = 112, WCrDevotion = 113;
        const int WWzStormGust = 120, WWzMeteor = 121, WWzJupitel = 122, WWzVermilion = 123, WWzQuagmire = 124;
        const int WWzHeavenDrive = 130, WSaVolcano = 131, WSaDeluge = 132, WSaViolentGale = 133;
        const int WHtBlitzBeat = 140, WHtClaymore = 141, WHtLandMine = 142, WHtFreezingTrap = 143;
        const int WBaMusicalStrike = 150, WBaPoemBragi = 151, WBaAppleIdun = 152, WBaDissonance = 153;
        const int WDcThrowArrow = 160, WDcServiceForYou = 161, WDcDontForgetMe = 162, WDcFortuneKiss = 163;
        const int WPrSanctuary = 170, WPrMagnus = 171, WPrKyrie = 172, WPrImpositio = 173;
        const int WMoTripleAttack = 180, WMoFingerOffensive = 181, WMoInvestigate = 182, WMoExtremityFist = 183;
        const int WBsAdrenaline = 190, WBsOverThrust = 191, WBsHammerFall = 192;
        const int WAmDemonstration = 200, WAmAcidTerror = 201, WAmPotionPitcher = 202, WAmCannibalize = 203;
        const int WAsSonicBlow = 210, WAsGrimtooth = 211, WAsVenomDust = 212, WAsSplasher = 213;
        const int WRgBackstab = 220, WRgRaid = 221;

        // ---- passive ids (the mode's own; 1-9 monster cards, then blocks of 10 per job)
        const int PPoringCard = 1, PAndreCard = 2, PPupaCard = 3, PZombieCard = 4, PChonchonCard = 5, PLunaticCard = 6, PArcherSkeletonCard = 7;
        const int PSmRecovery = 10, PSmSword = 11;
        const int PMgSRecovery = 20, PMgEnergyCoat = 21;
        const int PAcOwl = 30, PAcVulture = 31;
        const int PAlDp = 40, PAlDemonBane = 41;
        const int PMcOvercharge = 50, PMcIncCarry = 51;
        const int PTfDouble = 60, PTfMiss = 61;
        const int PKnSpearMastery = 100, PKnRiding = 101;
        const int PCrTrust = 110;
        const int PSaAdvancedBook = 130, PSaFreeCast = 131;
        const int PHtBeastBane = 140;
        const int PBaMusicalLesson = 150;
        const int PDcDancingLesson = 160;
        const int PPrMaceMastery = 170;
        const int PMoIronHand = 180, PMoDodge = 181;
        const int PBsWeaponResearch = 190, PBsSkinTemper = 191, PBsGreed = 192;
        const int PAmAxeMastery = 200, PAmLearningPotion = 201;
        const int PAsKatar = 210, PAsRight = 211;
        const int PRgSnatcher = 220, PRgTunnelDrive = 221;

        static void AddJobs(HordeContent c)
        {
            AddNovice(c);
            AddSwordmanLine(c);
            AddMageLine(c);
            AddArcherLine(c);
            AddAcolyteLine(c);
            AddMerchantLine(c);
            AddThiefLine(c);
            AddCardsW17(c); // client wave 17 (h17cards): point 15 A + B + C (Content/HordeCards.cs)
        }

        static JobDef Job(int jobId, string key, int tier, int[] next, int signature, int[] weapons, int[] passives, ModDef[] mods, int[] statBonus)
        {
            return new JobDef { JobId = jobId, Key = key, Tier = tier, Next = next, SignatureWeapon = signature, Weapons = weapons, Passives = passives, Mods = mods, StatBonus = statBonus };
        }

        static int[] Ids(params int[] ids) { return ids; }
        static readonly int[] None = new int[0];
        static readonly ModDef[] NoMods = new ModDef[0];

        // ================================================================================================================
        // NOVICE (JOB_NOVICE 0; skill_tree.yml "Novice": NV_BASIC 9, NV_FIRSTAID 1, NV_TRICKDEAD 1)
        // ================================================================================================================
        static void AddNovice(HordeContent c)
        {
            // The starting weapon: the Novice's plain attack (a Novice starts with a Knife, item 1201, in rAthena's
            // conf/char_athena.conf start_items). Not a skill: SkillId 0, the client names it by Key through Lang and (wave 9)
            // draws the Knife's icon by ItemId (db/re/item_db_equip.yml 1201 Knife).
            var knife = W(WNvKnife, "nv_knife", 0, WeaponKind.MeleeArc, Targeting.Nearest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 16 + 3 * i, Cooldown = 0.75f - 0.03f * i, Range = 2f, Arc = 160 }), 60);
            knife.ItemId = 1201;
            c.Weapons.Add(knife);
            // NV_FIRSTAID (142, RO max 1): heals the caster 5 HP (skills/novice/firstaid.cpp: status_heal(target, 5, 0, 0)).
            // Mode: a small self heal on a timer (levels 2-3 are own).
            c.Weapons.Add(W(WNvFirstAid, "nv_firstaid", 142, WeaponKind.Aura, Targeting.Self, DamageClass.Magic, Element.Neutral,
                Lv(3, i => new WeaponLevel { Cooldown = 2f - 0.2f * i, Area = 0f, Heal = 2 + 2 * i, Duration = 0.1f }), 70));

            // Monster cards: generic passives open to every job from the start, drawn as the card item. Their RO effect
            // (db/re/item_db_etc.yml Script) decides the modifier; the size is the mode's own.
            // Poring_Card 4001: "bonus bLuk,2; bonus bFlee2,1;"
            c.Passives.Add(P(PPoringCard, "poring_card", 0, 4001, PLv(5, i => Ms(M(Mod.Luk, 2 * (i + 1)), M(Mod.Dodge, 1 + i)))));
            // Andre_Card 4043: "bonus bBaseAtk,20;"
            c.Passives.Add(P(PAndreCard, "andre_card", 0, 4043, PLv(5, i => Ms(M(Mod.DamagePct, 5 * (i + 1))))));
            // Pupa_Card 4003: "bonus bMaxHP,700;"
            c.Passives.Add(P(PPupaCard, "pupa_card", 0, 4003, PLv(5, i => Ms(M(Mod.MaxHpPct, 8 * (i + 1))))));
            // Zombie_Card 4038: "bonus bHPrecovRate,20;"
            c.Passives.Add(P(PZombieCard, "zombie_card", 0, 4038, PLv(5, i => Ms(M(Mod.Regen, 0.3f * (i + 1))))));
            // Chonchon_Card 4009: "bonus bAgi,1; bonus bFlee,2;"
            c.Passives.Add(P(PChonchonCard, "chonchon_card", 0, 4009, PLv(5, i => Ms(M(Mod.Agi, 1 + i), M(Mod.Dodge, 1.5f * (i + 1))))));
            // Lunatic_Card 4006: "bonus bLuk,1; bonus bCritical,1; bonus bFlee2,1;"
            c.Passives.Add(P(PLunaticCard, "lunatic_card", 0, 4006, PLv(5, i => Ms(M(Mod.CritChance, 2 * (i + 1)), M(Mod.Luk, 1 + i)))));
            // Archer_Skeleton_Card 4094: "bonus bLongAtkRate,10;" (long-range damage)
            c.Passives.Add(P(PArcherSkeletonCard, "archer_skeleton_card", 0, 4094, PLv(5, i => Ms(M(Mod.RangedDamagePct, 6 * (i + 1))))));

            c.Jobs.Add(Job(JobNovice, "JOB_NOVICE", 0, Ids(JobSwordman, JobMage, JobArcher, JobAcolyte, JobMerchant, JobThief), 0,
                Ids(WNvFirstAid),
                Ids(PPoringCard, PAndreCard, PPupaCard, PZombieCard, PChonchonCard, PLunaticCard, PArcherSkeletonCard),
                NoMods, new int[6]));
        }

        // ================================================================================================================
        // SWORDMAN line: Swordman (1) -> Knight (7) / Crusader (14)
        // ================================================================================================================
        static void AddSwordmanLine(HordeContent c)
        {
            // SM_BASH (5, max 10, Weapon element, 1 hit, sc Stun): +30 % per level (skills/swordman/bash.cpp:5); from level 6
            // with SM_FATALBLOW it may stun (bash.cpp:14-17). Mode: a heavy swing at the nearest; levels 3+ may stun.
            c.Weapons.Add(W(WSmBash, "sm_bash", 5, WeaponKind.MeleeArc, Targeting.Nearest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel
                {
                    Damage = 28 + 11 * i, Cooldown = 1.1f - 0.05f * i, Range = 2f, Arc = 120,
                    Effects = i >= 2 ? Fxs(Fx(EffectKind.Stun, 10 + 5 * (i - 2), 1f)) : null,
                })));
            // SM_MAGNUM (7, max 10, Fire, splash 2 = 5x5 around the caster, knockback 2): magnum.cpp:32-33 hits every enemy
            // around the caster. Mode: a fire burst around the player that pushes enemies back.
            c.Weapons.Add(W(WSmMagnum, "sm_magnum", 7, WeaponKind.Nova, Targeting.Self, DamageClass.Melee, Element.Fire,
                Lv(5, i => new WeaponLevel { Damage = 10 + 4 * i, Cooldown = 2f - 0.15f * i, Area = 2.2f + 0.2f * i, Effects = Fxs(Fx(EffectKind.Knockback, 100, 0, 2)) })));
            // SM_ENDURE (8, max 10, sc Endure): keeps the caster from flinching and adds MDEF (status.cpp:7947).
            // Mode PROVISIONAL: a self buff of less damage taken for a few seconds.
            c.Weapons.Add(W(WSmEndure, "sm_endure", 8, WeaponKind.Aura, Targeting.Self, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 8f, Area = 0f, Duration = 6f + i, Buffs = Ms(M(Mod.DamageTakenPct, -(10 + 3 * i))) }), 80));
            // SM_RECOVERY (4, max 10, passive): HP recovery.
            c.Passives.Add(P(PSmRecovery, "sm_recovery", 4, 0, PLv(5, i => Ms(M(Mod.Regen, 0.4f * (i + 1))))));
            // SM_SWORD (2, max 10, passive): Sword Mastery, ATK with swords.
            c.Passives.Add(P(PSmSword, "sm_sword", 2, 0, PLv(5, i => Ms(M(Mod.MeleeDamagePct, 6 * (i + 1))))));
            c.Jobs.Add(Job(JobSwordman, "JOB_SWORDMAN", 1, Ids(JobKnight, JobCrusader), WSmBash,
                Ids(WSmMagnum, WSmEndure), Ids(PSmRecovery, PSmSword),
                Ms(M(Mod.MaxHpPct, 20f), M(Mod.Regen, 0.5f)), new[] { 2, 0, 2, 0, 0, 0 }));

            // ---------------- KNIGHT (7)
            // KN_BOWLINGBASH (62, max 10, 2 hits, splash 2 around the target, knockback 1-5 by level): +40 % per level
            // (skills/swordman/bowlingbash.cpp:17), hits every enemy around its target (bowlingbash.cpp:24-25).
            // Mode: a strike at the nearest that hits everything around it and pushes it away (the 2 hits folded in).
            c.Weapons.Add(W(WKnBowlingBash, "kn_bowlingbash", 62, WeaponKind.AreaAtTarget, Targeting.Nearest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 50 + 20 * i, Cooldown = 1.6f - 0.08f * i, Range = 2.5f, Area = 1.6f + 0.15f * i, Delay = 0.05f, Effects = Fxs(Fx(EffectKind.Knockback, 100, 0, 1 + (i + 1) / 2)) })));
            // KN_BRANDISHSPEAR (57, max 10, splash 2, knockback 2): hits the enemies in a fan in front of the knight
            // (skills/swordman/brandishspear.cpp:5, map_foreachindir). Mode: a wide frontal swing in the walking direction.
            c.Weapons.Add(W(WKnBrandishSpear, "kn_brandishspear", 57, WeaponKind.MeleeArc, Targeting.Facing, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 24 + 9 * i, Cooldown = 1.8f - 0.1f * i, Range = 3f + 0.1f * i, Arc = 100, Effects = Fxs(Fx(EffectKind.Knockback, 100, 0, 2)) })));
            // KN_SPEARBOOMERANG (59, max 5, range 3/5/7/9/11 by level): a thrown spear. Mode: a bolt whose range is RO's
            // own range per level.
            c.Weapons.Add(W(WKnSpearBoomerang, "kn_spearboomerang", 59, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Ranged, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 25 + 10 * i, Cooldown = 1.6f - 0.05f * i, Speed = 9f, Range = 3 + 2 * i, Pierce = i >= 3 ? 1 : 0 })));
            // KN_SPEARMASTERY (55, max 10, passive): ATK with spears.
            c.Passives.Add(P(PKnSpearMastery, "kn_spearmastery", 55, 0, PLv(5, i => Ms(M(Mod.MeleeDamagePct, 5 * (i + 1))))));
            // KN_RIDING (63, max 1, passive): riding a Peco; mounted characters walk faster (status.cpp:3680-3681 speed).
            // Mode: move speed (levels 2-3 are own; the client keeps the unmounted sprite).
            c.Passives.Add(P(PKnRiding, "kn_riding", 63, 0, PLv(3, i => Ms(M(Mod.MoveSpeedPct, 6 * (i + 1))))));
            c.Jobs.Add(Job(JobKnight, "JOB_KNIGHT", 2, None, WKnBowlingBash,
                Ids(WKnBrandishSpear, WKnSpearBoomerang), Ids(PKnSpearMastery, PKnRiding),
                Ms(M(Mod.MaxHpPct, 20f), M(Mod.MeleeDamagePct, 10f)), new[] { 3, 0, 2, 0, 0, 0 }));

            // ---------------- CRUSADER (14)
            // CR_HOLYCROSS (253, max 10, Holy, 2 hits, blind 3 % per level: skills/swordman/holycross.cpp:26).
            // Mode: a holy two-hit swing (hits folded in).
            c.Weapons.Add(W(WCrHolyCross, "cr_holycross", 253, WeaponKind.MeleeArc, Targeting.Nearest, DamageClass.Melee, Element.Holy,
                Lv(5, i => new WeaponLevel { Damage = 39 + 14 * i, Cooldown = 1.3f - 0.05f * i, Range = 2f, Arc = 80 })));
            // CR_GRANDCROSS (254, max 10, Holy, magic): a cross of holy damage around the caster (a ground unit:
            // skills/swordman/grandcross.cpp:24) that also hurts the caster in RO, and blinds Undead / Demon (31-33).
            // Mode: two holy bursts around the player; the cost in own HP is not modelled.
            c.Weapons.Add(W(WCrGrandCross, "cr_grandcross", 254, WeaponKind.Nova, Targeting.Self, DamageClass.Magic, Element.Holy,
                Lv(5, i => new WeaponLevel { Damage = 26 + 9 * i, Cooldown = 4.2f - 0.2f * i, Area = 3f, Count = 2, Burst = 0.3f })));
            // CR_SHIELDBOOMERANG (251, max 5, range 3/5/7/9/11): a thrown shield; Neutral in renewal
            // (skills/swordman/shieldboomerang.cpp:20-23). Mode: a bolt with RO's ranges.
            c.Weapons.Add(W(WCrShieldBoomerang, "cr_shieldboomerang", 251, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Ranged, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 28 + 11 * i, Cooldown = 1.8f - 0.05f * i, Speed = 9f, Range = 3 + 2 * i })));
            // CR_DEVOTION (255, max 5, range 7-11): the Crusader takes the damage of the allies it protects
            // (battle.cpp:427-433). Mode PROVISIONAL (no damage transfer in the engine yet): allies near the Crusader take
            // less damage while it lasts.
            c.Weapons.Add(W(WCrDevotion, "cr_devotion", 255, WeaponKind.Aura, Targeting.Self, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 8f, Area = 5f + 0.5f * i, Duration = 6f + i, Buffs = Ms(M(Mod.DamageTakenPct, -(10 + 3 * i))) })));
            // CR_TRUST (248, max 10, passive): Faith, Max HP and holy resistance.
            c.Passives.Add(P(PCrTrust, "cr_trust", 248, 0, PLv(5, i => Ms(M(Mod.MaxHpPct, 5 * (i + 1))))));
            // The Crusader's tree also holds AL_HEAL 10 and AL_DEMONBANE 10 (skill_tree.yml "Crusader"): the Acolyte's defs.
            c.Jobs.Add(Job(JobCrusader, "JOB_CRUSADER", 2, None, WCrHolyCross,
                Ids(WCrGrandCross, WCrShieldBoomerang, WCrDevotion, WAlHeal), Ids(PCrTrust, PAlDemonBane),
                Ms(M(Mod.MaxHpPct, 20f), M(Mod.DamageTakenPct, -5f)), new[] { 1, 0, 3, 1, 0, 0 }));
        }

        // ================================================================================================================
        // MAGE line: Mage (2) -> Wizard (9) / Sage (16)
        // ================================================================================================================
        static void AddMageLine(HordeContent c)
        {
            // MG_FIREBOLT (19, max 10, Fire, HitCount = level): Mode: Count fire bolts at the nearest (RO's hits as bolts).
            c.Weapons.Add(W(WMgFireBolt, "mg_firebolt", 19, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Magic, Element.Fire,
                Lv(5, i => new WeaponLevel { Damage = 13 + 3 * i, Count = 1 + i, Burst = 0.12f, Cooldown = 1.6f - 0.05f * i, Speed = 8f, Range = 8f })));
            // MG_FROSTDIVER (15, max 10, Water, sc Freeze): freeze chance min(lv*3+35, lv+60) (skills/mage/frostdiver.cpp:23).
            // Mode: a water bolt that may freeze: the chance is RO's at levels 2/4/6/8/10.
            c.Weapons.Add(W(WMgFrostDiver, "mg_frostdiver", 15, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Magic, Element.Water,
                Lv(5, i => new WeaponLevel { Damage = 14 + 5 * i, Cooldown = 1.8f - 0.1f * i, Speed = 7f, Range = 8f, Effects = Fxs(Fx(EffectKind.Freeze, new[] { 41, 47, 53, 59, 65 }[i], 1.5f)) })));
            // MG_FIREWALL (18, max 10, Fire, ground unit Firewall, knockback 2). Mode: a burning wall of fire pillars at the nearest enemy
            // that blocks and forcefully knocks back enemies that touch or walk into it.
            var fireWall = W(WMgFireWall, "mg_firewall", 18, WeaponKind.GroundZone, Targeting.Nearest, DamageClass.Magic, Element.Fire,
                Lv(5, i => new WeaponLevel { Damage = 14 + 5 * i, Count = 2 + i / 2, Tick = 0.25f, Cooldown = 4.2f - 0.25f * i, Range = 5.5f, Area = 1.5f + 0.15f * i, Duration = 5f + 0.5f * i, Effects = Fxs(Fx(EffectKind.Knockback, 100, 0, 2.8f + 0.2f * i)) }));
            fireWall.Zone = ZoneFlags.BlocksEnemies;
            c.Weapons.Add(fireWall);
            // MG_THUNDERSTORM (21, max 10, Wind, ground area, HitCount = level). Mode: a delayed lightning area on the densest pack.
            c.Weapons.Add(W(WMgThunderStorm, "mg_thunderstorm", 21, WeaponKind.AreaAtTarget, Targeting.Densest, DamageClass.Magic, Element.Wind,
                Lv(5, i => new WeaponLevel { Damage = 11 + 4 * i, Cooldown = 2f - 0.1f * i, Range = 8f, Area = 2.2f, Delay = 0.6f })));
            // MG_SRECOVERY (9, max 10, passive): SP recovery. Mode: shorter cooldowns (the mode has no SP; SP is what lets a
            // caster cast again in RO).
            c.Passives.Add(P(PMgSRecovery, "mg_srecovery", 9, 0, PLv(5, i => Ms(M(Mod.CooldownPct, -3 * (i + 1))))));
            // MG_ENERGYCOAT (157, max 1): damage taken lowered while SP lasts (skill.cpp:2834). Mode: less damage taken
            // (levels 2-3 are own).
            c.Passives.Add(P(PMgEnergyCoat, "mg_energycoat", 157, 0, PLv(3, i => Ms(M(Mod.DamageTakenPct, -5 * (i + 1))))));
            c.Jobs.Add(Job(JobMage, "JOB_MAGE", 1, Ids(JobWizard, JobSage), WMgFireBolt,
                Ids(WMgFrostDiver, WMgFireWall, WMgThunderStorm), Ids(PMgSRecovery, PMgEnergyCoat),
                Ms(M(Mod.MagicDamagePct, 10f)), new[] { 0, 0, 0, 3, 1, 0 }));

            // ---------------- WIZARD (9)  (the Wizard has no passive skill of its own in skill_tree.yml)
            // WZ_STORMGUST (89, max 10, Water, ground unit, sc Freeze, knockback 2). Mode: a lasting freezing storm on the densest pack.
            c.Weapons.Add(W(WWzStormGust, "wz_stormgust", 89, WeaponKind.GroundZone, Targeting.Densest, DamageClass.Magic, Element.Water,
                Lv(5, i => new WeaponLevel { Damage = 18 + 7 * i, Tick = 0.75f, Duration = 3f, Area = 2.5f + 0.15f * i, Range = 8f, Cooldown = 6.5f - 0.3f * i, Effects = Fxs(Fx(EffectKind.Freeze, 18 + 4 * i, 1.5f), Fx(EffectKind.Knockback, 100, 0, 1.8f)) })));
            // WZ_METEOR (83, max 10, Fire, meteors on random cells of the area: skills/mage/meteorstorm.cpp:18-21; stun
            // 3 % per level: :32). Mode: fire strikes on random enemies, one to three at a time.
            c.Weapons.Add(W(WWzMeteor, "wz_meteor", 83, WeaponKind.AreaAtTarget, Targeting.Random, DamageClass.Magic, Element.Fire,
                Lv(5, i => new WeaponLevel { Damage = 48 + 16 * i, Count = 1 + (i + 1) / 2, Burst = 0.25f, Range = 8f, Area = 1.6f, Delay = 0.8f, Cooldown = 3.8f - 0.18f * i, Effects = Fxs(Fx(EffectKind.Stun, 6 + 3 * i, 1f)) })));
            // WZ_JUPITEL (84, max 10, Wind, 3-12 hits, knockback 2-7). Mode: a heavy lightning bolt that throws back.
            c.Weapons.Add(W(WWzJupitel, "wz_jupitel", 84, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Magic, Element.Wind,
                Lv(5, i => new WeaponLevel { Damage = 37 + 14 * i, Cooldown = 1.8f - 0.1f * i, Speed = 10f, Range = 8f, Effects = Fxs(Fx(EffectKind.Knockback, 100, 0, 2 + i)) })));
            // WZ_VERMILION (85, max 10, Wind, ground area, sc Blind). Mode: a wide delayed lightning field.
            c.Weapons.Add(W(WWzVermilion, "wz_vermilion", 85, WeaponKind.AreaAtTarget, Targeting.Densest, DamageClass.Magic, Element.Wind,
                Lv(5, i => new WeaponLevel { Damage = 32 + 12 * i, Cooldown = 4.0f - 0.2f * i, Range = 8f, Area = 3.5f, Delay = 1f })));
            // WZ_QUAGMIRE (92, max 5, Earth, ground unit, sc Quagmire): lowers AGI / DEX and walk speed inside
            // (status.cpp:6850-6851). Mode: a slowing zone, no damage.
            c.Weapons.Add(W(WWzQuagmire, "wz_quagmire", 92, WeaponKind.GroundZone, Targeting.Densest, DamageClass.Magic, Element.Earth,
                Lv(5, i => new WeaponLevel { Damage = 0, Tick = 0.5f, Duration = 5f, Area = 2.5f, Range = 8f, Cooldown = 6f, Effects = Fxs(Fx(EffectKind.Slow, 100, 1f, 30 + 5 * i)) }), 70));
            c.Jobs.Add(Job(JobWizard, "JOB_WIZARD", 2, None, WWzStormGust,
                Ids(WWzMeteor, WWzJupitel, WWzVermilion, WWzQuagmire), None,
                Ms(M(Mod.MagicDamagePct, 15f), M(Mod.AreaPct, 10f)), new[] { 0, 0, 0, 3, 2, 0 }));

            // ---------------- SAGE (16)
            // WZ_HEAVENDRIVE (91, max 5 in the Sage's tree, Earth, ground area, 1-5 hits). Mode: a delayed earth area on the densest pack.
            c.Weapons.Add(W(WWzHeavenDrive, "wz_heavendrive", 91, WeaponKind.AreaAtTarget, Targeting.Densest, DamageClass.Magic, Element.Earth,
                Lv(5, i => new WeaponLevel { Damage = 52 + 18 * i, Cooldown = 3.4f - 0.15f * i, Range = 8f, Area = 2f, Delay = 0.4f })));
            // SA_VOLCANO (285, max 5, ground unit): raises ATK inside (status.cpp:7300-7301) and fire damage.
            // Mode: a zone laid under the Sage: allies inside deal more damage, fire weapons even more.
            c.Weapons.Add(W(WSaVolcano, "sa_volcano", 285, WeaponKind.GroundZone, Targeting.Self, DamageClass.Magic, Element.Fire,
                Lv(5, i => new WeaponLevel { Damage = 0, Tick = 0.5f, Duration = 8f + i, Area = 3f, Cooldown = 10f, Buffs = Ms(M(Mod.DamagePct, 6 + 2 * i), M(Mod.ElementDamagePct, 10 + 5 * i, (int)Element.Fire)) })));
            // SA_DELUGE (286, max 5, ground unit): raises Max HP inside (status.cpp:3204-3205) and water damage (battle.cpp:547-549).
            c.Weapons.Add(W(WSaDeluge, "sa_deluge", 286, WeaponKind.GroundZone, Targeting.Self, DamageClass.Magic, Element.Water,
                Lv(5, i => new WeaponLevel { Damage = 0, Tick = 0.5f, Duration = 8f + i, Area = 3f, Cooldown = 10f, Buffs = Ms(M(Mod.MaxHpPct, 8 + 3 * i), M(Mod.ElementDamagePct, 10 + 5 * i, (int)Element.Water)) })));
            // SA_VIOLENTGALE (287, max 5, ground unit): raises Flee inside (status.cpp:7637-7638) and wind damage (battle.cpp:539-541).
            c.Weapons.Add(W(WSaViolentGale, "sa_violentgale", 287, WeaponKind.GroundZone, Targeting.Self, DamageClass.Magic, Element.Wind,
                Lv(5, i => new WeaponLevel { Damage = 0, Tick = 0.5f, Duration = 8f + i, Area = 3f, Cooldown = 10f, Buffs = Ms(M(Mod.Dodge, 6 + 2 * i), M(Mod.ElementDamagePct, 10 + 5 * i, (int)Element.Wind)) })));
            // SA_ADVANCEDBOOK (274, max 10, passive): book mastery, ATK and attack speed with books.
            c.Passives.Add(P(PSaAdvancedBook, "sa_advancedbook", 274, 0, PLv(5, i => Ms(M(Mod.DamagePct, 3 * (i + 1)), M(Mod.CooldownPct, -2 * (i + 1))))));
            // SA_FREECAST (278, max 10, passive): walk while casting. Mode: move speed and a little cooldown.
            c.Passives.Add(P(PSaFreeCast, "sa_freecast", 278, 0, PLv(5, i => Ms(M(Mod.MoveSpeedPct, 4 * (i + 1)), M(Mod.CooldownPct, -1 * (i + 1))))));
            c.Jobs.Add(Job(JobSage, "JOB_SAGE", 2, None, WWzHeavenDrive,
                Ids(WSaVolcano, WSaDeluge, WSaViolentGale), Ids(PSaAdvancedBook, PSaFreeCast),
                Ms(M(Mod.MagicDamagePct, 10f), M(Mod.CooldownPct, -5f)), new[] { 0, 1, 0, 3, 1, 0 }));
        }

        // ================================================================================================================
        // ARCHER line: Archer (3) -> Hunter (11) / Bard (19, male) / Dancer (20, female)
        // ================================================================================================================
        static void AddArcherLine(HordeContent c)
        {
            // AC_DOUBLE (46, max 10, 2 hits, range 9): Double Strafe. Mode: two arrows at the nearest.
            c.Weapons.Add(W(WAcDouble, "ac_double", 46, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Ranged, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 13 + 5 * i, Count = 2, Burst = 0.1f, Cooldown = 1.1f - 0.05f * i, Speed = 10f, Range = 9f, Pierce = i >= 2 ? 1 : 0 })));
            // AC_SHOWER (47, max 10, splash 1 -> 2 from level 6, knockback 2): Arrow Shower on a ground area.
            c.Weapons.Add(W(WAcShower, "ac_shower", 47, WeaponKind.AreaAtTarget, Targeting.Densest, DamageClass.Ranged, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 12 + 5 * i, Cooldown = 2f - 0.1f * i, Range = 8f, Area = i >= 2 ? 2.3f : 1.5f, Delay = 0.3f, Effects = Fxs(Fx(EffectKind.Knockback, 100, 0, 2)) })));
            // AC_CHARGEARROW (148, max 1, knockback 6): Arrow Repel. Mode: a piercing arrow that throws back (levels 2-3 own).
            c.Weapons.Add(W(WAcChargeArrow, "ac_chargearrow", 148, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Ranged, Element.Neutral,
                Lv(3, i => new WeaponLevel { Damage = 20 + 8 * i, Cooldown = 2f - 0.2f * i, Speed = 11f, Range = 9f, Pierce = 2 + i, Effects = Fxs(Fx(EffectKind.Knockback, 100, 0, 4)) })));
            // AC_OWL (43, max 10, passive): Owl's Eye, +1 DEX per level.
            c.Passives.Add(P(PAcOwl, "ac_owl", 43, 0, PLv(5, i => Ms(M(Mod.Dex, 2 * (i + 1))))));
            // AC_VULTURE (44, max 10, passive): Vulture's Eye, more range and HIT. Mode: faster arrows and ranged damage.
            c.Passives.Add(P(PAcVulture, "ac_vulture", 44, 0, PLv(5, i => Ms(M(Mod.ProjectileSpeedPct, 6 * (i + 1)), M(Mod.RangedDamagePct, 3 * (i + 1))))));
            // Bard is offered to male characters and Dancer to female ones, as in RO (pc.cpp:7739 "case MAPID_BARDDANCER:
            // return sex?JOB_BARD:JOB_DANCER;"): JobDef.Sex on the two jobs below; the engine filters Next by it.
            c.Jobs.Add(Job(JobArcher, "JOB_ARCHER", 1, Ids(JobHunter, JobBard, JobDancer), WAcDouble,
                Ids(WAcShower, WAcChargeArrow), Ids(PAcOwl, PAcVulture),
                Ms(M(Mod.RangedDamagePct, 10f)), new[] { 0, 1, 0, 0, 3, 0 }));

            // ---------------- HUNTER (11)
            // HT_BLITZBEAT (129, max 5, 1-5 hits, splash 1, ignores Flee): the falcon's dive (also by itself with a LUK
            // chance on attack: skill.cpp:1351). Mode: the falcon strikes small areas on random enemies by itself.
            c.Weapons.Add(W(WHtBlitzBeat, "ht_blitzbeat", 129, WeaponKind.AreaAtTarget, Targeting.Random, DamageClass.Ranged, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 20 + 8 * i, Count = 1 + i / 2, Burst = 0.3f, Cooldown = 1.6f - 0.06f * i, Range = 8f, Area = 1.2f, Delay = 0.2f })));
            // HT_CLAYMORETRAP (123, max 5, Fire, splash 2): a fire trap.
            c.Weapons.Add(W(WHtClaymore, "ht_claymoretrap", 123, WeaponKind.Trap, Targeting.Self, DamageClass.Ranged, Element.Fire,
                Lv(5, i => new WeaponLevel { Damage = 45 + 17 * i, Cooldown = 2f - 0.1f * i, Count = 3 + i, Delay = 0.5f, Area = 1.8f, Duration = 20f })));
            // HT_LANDMINE (116, max 5, Earth, sc Stun: skills/archer/landmine.cpp:19).
            c.Weapons.Add(W(WHtLandMine, "ht_landmine", 116, WeaponKind.Trap, Targeting.Self, DamageClass.Ranged, Element.Earth,
                Lv(5, i => new WeaponLevel { Damage = 36 + 14 * i, Cooldown = 1.8f, Count = 4 + i, Delay = 0.5f, Area = 1f, Duration = 20f, Effects = Fxs(Fx(EffectKind.Stun, 30, 1.5f)) })));
            // HT_FREEZINGTRAP (121, max 5, Water, splash 1): freezes what it catches (skills/archer/freezingtrap.cpp:21, 100 %).
            c.Weapons.Add(W(WHtFreezingTrap, "ht_freezingtrap", 121, WeaponKind.Trap, Targeting.Self, DamageClass.Ranged, Element.Water,
                Lv(5, i => new WeaponLevel { Damage = 20 + 7 * i, Cooldown = 2f, Count = 3 + i / 2, Delay = 0.5f, Area = 1.4f, Duration = 20f, Effects = Fxs(Fx(EffectKind.Freeze, 100, 1.5f + 0.25f * i)) })));
            // HT_BEASTBANE (126, max 10, passive): +damage against Brute and Insect (battle.cpp:2302-2303).
            c.Passives.Add(P(PHtBeastBane, "ht_beastbane", 126, 0, PLv(5, i => Ms(M(Mod.DamageVsRacePct, 6 * (i + 1), (int)Race.Brute), M(Mod.DamageVsRacePct, 6 * (i + 1), (int)Race.Insect)))));
            c.Jobs.Add(Job(JobHunter, "JOB_HUNTER", 2, None, WHtBlitzBeat,
                Ids(WHtClaymore, WHtLandMine, WHtFreezingTrap), Ids(PHtBeastBane),
                Ms(M(Mod.RangedDamagePct, 12f), M(Mod.PickupRadiusPct, 10f)), new[] { 0, 2, 0, 0, 3, 1 }));

            // ---------------- BARD (19)
            // BA_MUSICALSTRIKE (316, max 5, 2 hits, range 9): Melody Strike. Mode: two notes at the nearest.
            c.Weapons.Add(W(WBaMusicalStrike, "ba_musicalstrike", 316, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Ranged, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 17 + 6 * i, Count = 2, Burst = 0.1f, Cooldown = 1.3f - 0.05f * i, Speed = 10f, Range = 9f })));
            // BA_POEMBRAGI (321, max 10, song): shorter cast time and after-cast delay for those in it (skill.cpp:10170-10171,
            // 10332-10333). Mode: allies around the Bard get shorter cooldowns.
            c.Weapons.Add(W(WBaPoemBragi, "ba_poembragi", 321, WeaponKind.Aura, Targeting.Self, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 6f, Area = 5f, Duration = 7f, Buffs = Ms(M(Mod.CooldownPct, -(6 + 2 * i))) })));
            // BA_APPLEIDUN (322, max 10, song): Max HP up (status.cpp:3202-3203) and HP recovery (pc.cpp:10768-10769).
            c.Weapons.Add(W(WBaAppleIdun, "ba_appleidun", 322, WeaponKind.Aura, Targeting.Self, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 3f, Area = 5f, Duration = 4f, HealPct = 1f + 0.5f * i, Buffs = Ms(M(Mod.MaxHpPct, 5 + 2 * i)) })));
            // BA_DISSONANCE (317, max 5): Unchained Serenade, damage to enemies around the performer
            // (skills/archer/unchainedserenade.cpp). Mode: a pulse of damage around the Bard.
            c.Weapons.Add(W(WBaDissonance, "ba_dissonance", 317, WeaponKind.Aura, Targeting.Self, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 1f, Area = 3.5f + 0.2f * i, Damage = 10 + 4 * i, Duration = 0.1f })));
            // BA_MUSICALLESSON (315, max 10, passive): instrument mastery (songs and Melody Strike).
            c.Passives.Add(P(PBaMusicalLesson, "ba_musicallesson", 315, 0, PLv(5, i => Ms(M(Mod.DurationPct, 6 * (i + 1)), M(Mod.RangedDamagePct, 3 * (i + 1))))));
            var bard = Job(JobBard, "JOB_BARD", 2, None, WBaMusicalStrike,
                Ids(WBaPoemBragi, WBaAppleIdun, WBaDissonance), Ids(PBaMusicalLesson),
                Ms(M(Mod.CooldownPct, -5f), M(Mod.MaxHpPct, 5f)), new[] { 0, 1, 0, 2, 2, 0 });
            bard.Sex = 1; // male characters only (pc.cpp:7739)
            c.Jobs.Add(bard);

            // ---------------- DANCER (20)
            // DC_THROWARROW (324, max 5, 2 hits, range 9): Slinging Arrow.
            c.Weapons.Add(W(WDcThrowArrow, "dc_throwarrow", 324, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Ranged, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 17 + 6 * i, Count = 2, Burst = 0.1f, Cooldown = 1.3f - 0.05f * i, Speed = 10f, Range = 9f })));
            // DC_SERVICEFORYOU (330, max 10, dance): Max SP up and SP cost down (status.cpp:3373-3374, 4662-4663).
            // Mode: shorter cooldowns for allies around (the mode has no SP).
            c.Weapons.Add(W(WDcServiceForYou, "dc_serviceforyou", 330, WeaponKind.Aura, Targeting.Self, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 6f, Area = 5f, Duration = 7f, Buffs = Ms(M(Mod.CooldownPct, -(5 + 2 * i)), M(Mod.MagicDamagePct, 4 + i)) })));
            // DC_DONTFORGETME (328, max 10, dance): enemies inside walk and attack slower (status.cpp:8084-8085, 8288-8289).
            // Mode: enemies around the Dancer are slowed.
            c.Weapons.Add(W(WDcDontForgetMe, "dc_dontforgetme", 328, WeaponKind.Aura, Targeting.Self, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 1f, Area = 4f + 0.25f * i, Duration = 0.1f, Effects = Fxs(Fx(EffectKind.Slow, 100, 1.2f, 25 + 5 * i)) })));
            // DC_FORTUNEKISS (329, max 10, dance): critical up (status.cpp:7511-7512).
            c.Weapons.Add(W(WDcFortuneKiss, "dc_fortunekiss", 329, WeaponKind.Aura, Targeting.Self, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 6f, Area = 5f, Duration = 7f, Buffs = Ms(M(Mod.CritChance, 5 + 2 * i)) })));
            // DC_DANCINGLESSON (323, max 10, passive): whip mastery (dances and Slinging Arrow).
            c.Passives.Add(P(PDcDancingLesson, "dc_dancinglesson", 323, 0, PLv(5, i => Ms(M(Mod.DurationPct, 6 * (i + 1)), M(Mod.RangedDamagePct, 3 * (i + 1))))));
            var dancer = Job(JobDancer, "JOB_DANCER", 2, None, WDcThrowArrow,
                Ids(WDcServiceForYou, WDcDontForgetMe, WDcFortuneKiss), Ids(PDcDancingLesson),
                Ms(M(Mod.CooldownPct, -5f), M(Mod.MaxHpPct, 5f)), new[] { 0, 1, 0, 2, 2, 0 });
            dancer.Sex = 2; // female characters only (pc.cpp:7739)
            c.Jobs.Add(dancer);
        }

        // ================================================================================================================
        // ACOLYTE line: Acolyte (4) -> Priest (8) / Monk (15)
        // ================================================================================================================
        static void AddAcolyteLine(HordeContent c)
        {
            // AL_HOLYLIGHT (156, max 1, Holy, magic, range 9): +25 % ratio (skills/acolyte/holylight.cpp:21).
            // Mode: a holy bolt (levels 2-5 are own).
            c.Weapons.Add(W(WAlHolyLight, "al_holylight", 156, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Magic, Element.Holy,
                Lv(5, i => new WeaponLevel { Damage = 26 + 9 * i, Cooldown = 1.2f - 0.05f * i, Speed = 8f, Range = 8f, Pierce = 2 + i / 2 })));
            // AL_HEAL (28, max 10, Holy): heals the target (skills/acolyte/heal.cpp:21); cast on an Undead-element enemy it
            // damages it (skill.cpp:4416-4421). Mode: heals the ally (or self) with the lowest HP share in range.
            // PROVISIONAL: the damage to Undead is not in the engine yet.
            c.Weapons.Add(W(WAlHeal, "al_heal", 28, WeaponKind.Aura, Targeting.LowestHpAlly, DamageClass.Magic, Element.Holy,
                Lv(5, i => new WeaponLevel { Cooldown = 2f - 0.1f * i, Range = 7f, Heal = 5 + 2 * i, HealPct = 1f, Duration = 0.1f })));
            // AL_BLESSING (34, max 10): +STR, +INT, +DEX (status.cpp:6777-6779, 6978-6980, 7060). Mode: the three stats to
            // the Acolyte and allies around.
            c.Weapons.Add(W(WAlBlessing, "al_blessing", 34, WeaponKind.Aura, Targeting.Self, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 10f, Area = 6f, Duration = 9f + i, Buffs = Ms(M(Mod.Str, 2 + i), M(Mod.Int, 2 + i), M(Mod.Dex, 2 + i)) })));
            // AL_INCAGI (29, max 10): +AGI and walk speed (status.cpp:6844-6845, 8345-8346).
            c.Weapons.Add(W(WAlIncAgi, "al_incagi", 29, WeaponKind.Aura, Targeting.Self, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 10f, Area = 6f, Duration = 9f + i, Buffs = Ms(M(Mod.MoveSpeedPct, 10 + 2 * i), M(Mod.Agi, 2 + i)) })));
            // AL_DP (22, max 10, passive): Divine Protection, less damage from Undead and Demons (battle.cpp:4817).
            // Mode PROVISIONAL (no damage-taken-by-race modifier): less damage taken from everything, smaller.
            c.Passives.Add(P(PAlDp, "al_dp", 22, 0, PLv(5, i => Ms(M(Mod.DamageTakenPct, -3 * (i + 1))))));
            // AL_DEMONBANE (23, max 10, passive): +damage against Undead-element or Demon-race monsters (battle.cpp:2291-2294).
            c.Passives.Add(P(PAlDemonBane, "al_demonbane", 23, 0, PLv(5, i => Ms(M(Mod.DamageVsElementPct, 8 * (i + 1), (int)Element.Undead), M(Mod.DamageVsRacePct, 8 * (i + 1), (int)Race.Demon)))));
            c.Jobs.Add(Job(JobAcolyte, "JOB_ACOLYTE", 1, Ids(JobPriest, JobMonk), WAlHolyLight,
                Ids(WAlHeal, WAlBlessing, WAlIncAgi), Ids(PAlDp, PAlDemonBane),
                Ms(M(Mod.HealPowerPct, 15f), M(Mod.MaxHpPct, 10f)), new[] { 0, 0, 1, 2, 1, 0 }));

            // ---------------- PRIEST (8)
            // PR_SANCTUARY (70, max 10, Holy, ground unit): heals everyone inside each tick and damages Undead-element /
            // Demon-race enemies instead (skill.cpp:6919-6941). Mode: a healing zone laid under the Priest.
            // PROVISIONAL: its damage to Undead / Demons is not in the engine yet (a zone damages every enemy or none).
            c.Weapons.Add(W(WPrSanctuary, "pr_sanctuary", 70, WeaponKind.GroundZone, Targeting.Self, DamageClass.Magic, Element.Holy,
                Lv(5, i => new WeaponLevel { Damage = 0, Tick = 1f, Duration = 6f + 0.5f * i, Area = 2.5f + 0.1f * i, Cooldown = 11f - 0.5f * i, Heal = 10 + 5 * i, HealPct = 1.5f })));
            // PR_MAGNUS (79, max 10, Holy, ground unit, HitCount = level): in renewal it hits every enemy inside
            // (skill.cpp:6963-6969), +30 % against Undead / Demons (skills/acolyte/magnusexorcismus.cpp:21-22).
            // Mode: a holy zone on the densest pack (the +30 % is not per weapon in the engine: the Acolyte's Demon Bane covers it).
            c.Weapons.Add(W(WPrMagnus, "pr_magnus", 79, WeaponKind.GroundZone, Targeting.Densest, DamageClass.Magic, Element.Holy,
                Lv(5, i => new WeaponLevel { Damage = 20 + 8 * i, Tick = 0.75f, Duration = 4f, Area = 2.5f, Range = 8f, Cooldown = 8.0f - 0.4f * i })));
            // PR_KYRIE (73, max 10): a barrier that blocks hits until it breaks (battle.cpp:1291-1302).
            // Mode PROVISIONAL (no shield in the engine yet): the ally with the lowest HP share takes much less damage for a while.
            c.Weapons.Add(W(WPrKyrie, "pr_kyrie", 73, WeaponKind.Aura, Targeting.LowestHpAlly, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 7f - 0.4f * i, Range = 7f, Duration = 5f + 0.5f * i, Buffs = Ms(M(Mod.DamageTakenPct, -(20 + 5 * i))) })));
            // PR_IMPOSITIO (66, max 5, party): weapon ATK up (status.cpp:7348-7349).
            c.Weapons.Add(W(WPrImpositio, "pr_impositio", 66, WeaponKind.Aura, Targeting.Self, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 10f, Area = 6f, Duration = 9f + i, Buffs = Ms(M(Mod.DamagePct, 6 + 3 * i)) })));
            // PR_MACEMASTERY (65, max 10, passive): ATK with maces.
            c.Passives.Add(P(PPrMaceMastery, "pr_macemastery", 65, 0, PLv(5, i => Ms(M(Mod.MeleeDamagePct, 5 * (i + 1)), M(Mod.HealPowerPct, 2 * (i + 1))))));
            // The Priest's tree also holds MG_SRECOVERY 10 (skill_tree.yml "Priest"): the Mage's def.
            c.Jobs.Add(Job(JobPriest, "JOB_PRIEST", 2, None, WPrSanctuary,
                Ids(WPrMagnus, WPrKyrie, WPrImpositio), Ids(PPrMaceMastery, PMgSRecovery),
                Ms(M(Mod.HealPowerPct, 20f), M(Mod.MagicDamagePct, 5f)), new[] { 0, 0, 1, 3, 1, 0 }));

            // ---------------- MONK (15)
            // MO_TRIPLEATTACK (263, max 10, passive in RO, 3 hits): a 30 % chance on a normal hit to strike three times
            // (battle.cpp:7267-7281). Mode: a three-blow combo at the nearest.
            c.Weapons.Add(W(WMoTripleAttack, "mo_tripleattack", 263, WeaponKind.MeleeArc, Targeting.Nearest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 17 + 7 * i, Count = 3, Burst = 0.12f, Cooldown = 1.1f - 0.05f * i, Range = 2f, Arc = 100 })));
            // MO_FINGEROFFENSIVE (267, max 5, range 9, one hit per spirit sphere thrown). Mode: spheres thrown at the nearest.
            c.Weapons.Add(W(WMoFingerOffensive, "mo_fingeroffensive", 267, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Ranged, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 17 + 4 * i, Count = 1 + i, Burst = 0.1f, Cooldown = 2f - 0.1f * i, Speed = 9f, Range = 9f })));
            // MO_INVESTIGATE (266, max 5, ignores Flee): Occult Impaction, stronger against high-DEF targets.
            // Mode: a hard blow aimed at the strongest enemy in reach (elites and bosses first).
            c.Weapons.Add(W(WMoInvestigate, "mo_investigate", 266, WeaponKind.MeleeArc, Targeting.Strongest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 41 + 15 * i, Cooldown = 2f - 0.1f * i, Range = 2f, Arc = 40 })));
            // MO_EXTREMITYFIST (271, max 5): Asura Strike, one huge hit that spends all SP and spheres. Mode: the heaviest
            // single blow of the mode, at the strongest enemy in reach, every two seconds (wave 9: no weapon rests above 2 s).
            c.Weapons.Add(W(WMoExtremityFist, "mo_extremityfist", 271, WeaponKind.MeleeArc, Targeting.Strongest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 40 + 15 * i, Cooldown = 2f - 0.1f * i, Range = 2.5f, Arc = 40 }), 80));
            // MO_IRONHAND (259, max 10, passive): Iron Fists, ATK with knuckles.
            c.Passives.Add(P(PMoIronHand, "mo_ironhand", 259, 0, PLv(5, i => Ms(M(Mod.MeleeDamagePct, 5 * (i + 1))))));
            // MO_DODGE (265, max 10, passive): Flee.
            c.Passives.Add(P(PMoDodge, "mo_dodge", 265, 0, PLv(5, i => Ms(M(Mod.Dodge, 3 * (i + 1))))));
            c.Jobs.Add(Job(JobMonk, "JOB_MONK", 2, None, WMoTripleAttack,
                Ids(WMoFingerOffensive, WMoInvestigate, WMoExtremityFist), Ids(PMoIronHand, PMoDodge),
                Ms(M(Mod.MeleeDamagePct, 12f), M(Mod.Dodge, 5f)), new[] { 2, 2, 1, 0, 0, 0 }));
        }

        // ================================================================================================================
        // MERCHANT line: Merchant (5) -> Blacksmith (10) / Alchemist (18)
        // ================================================================================================================
        static void AddMerchantLine(HordeContent c)
        {
            // MC_MAMMONITE (42, max 10, 1 hit): +50 % per level (skills/merchant/mammonite.cpp:10), costs zeny in RO.
            // Mode: a heavy blow at the nearest (no zeny).
            c.Weapons.Add(W(WMcMammonite, "mc_mammonite", 42, WeaponKind.MeleeArc, Targeting.Nearest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 36 + 14 * i, Cooldown = 1.4f - 0.05f * i, Range = 2f, Arc = 110 })));
            // MC_CARTREVOLUTION (153, max 1, splash 1, knockback 2): the cart swung around; more damage with a heavier cart
            // (skills/merchant/cartrevolution.cpp:15). Mode: a burst around the player that pushes back (levels 2-5 own).
            c.Weapons.Add(W(WMcCartRevolution, "mc_cartrevolution", 153, WeaponKind.Nova, Targeting.Self, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 14 + 5 * i, Cooldown = 2f - 0.1f * i, Area = 2f + 0.15f * i, Effects = Fxs(Fx(EffectKind.Knockback, 100, 0, 2)) })));
            // MC_LOUD (155, max 1): Crazy Uproar, +STR (status.cpp:6769) and ATK (7319). Mode: a self buff of STR (levels 2-3 own).
            c.Weapons.Add(W(WMcLoud, "mc_loud", 155, WeaponKind.Aura, Targeting.Self, DamageClass.Melee, Element.Neutral,
                Lv(3, i => new WeaponLevel { Cooldown = 12f, Area = 0f, Duration = 10f, Buffs = Ms(M(Mod.Str, 4 + 2 * i)) }), 80));
            // MC_OVERCHARGE (38, max 10, passive): sells for more. Mode PROVISIONAL: more run EXP from what is gathered.
            c.Passives.Add(P(PMcOvercharge, "mc_overcharge", 38, 0, PLv(5, i => Ms(M(Mod.ExpGainPct, 4 * (i + 1))))));
            // MC_INCCARRY (36, max 10, passive): Enlarge Weight Limit, carry more. Mode PROVISIONAL: a wider pickup radius.
            c.Passives.Add(P(PMcIncCarry, "mc_inccarry", 36, 0, PLv(5, i => Ms(M(Mod.PickupRadiusPct, 15 * (i + 1))))));
            c.Jobs.Add(Job(JobMerchant, "JOB_MERCHANT", 1, Ids(JobBlacksmith, JobAlchemist), WMcMammonite,
                Ids(WMcCartRevolution, WMcLoud), Ids(PMcOvercharge, PMcIncCarry),
                Ms(M(Mod.MaxHpPct, 15f), M(Mod.ExpGainPct, 5f)), new[] { 2, 0, 1, 0, 1, 0 }));

            // ---------------- BLACKSMITH (10)
            // BS_ADRENALINE (111, max 5, splash -1 = the party): Adrenaline Rush, faster attacks (status.cpp:8433-8435) for
            // the smith and the party (skills/merchant/adrenalinerush.cpp:21). Mode: shorter cooldowns for allies around.
            c.Weapons.Add(W(WBsAdrenaline, "bs_adrenaline", 111, WeaponKind.Aura, Targeting.Self, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 10f, Area = 6f, Duration = 9f, Buffs = Ms(M(Mod.CooldownPct, -(8 + 2 * i))) })));
            // BS_OVERTHRUST (113, max 5, party): Power-Thrust, weapon damage up (battle.cpp:4425-4426).
            c.Weapons.Add(W(WBsOverThrust, "bs_overthrust", 113, WeaponKind.Aura, Targeting.Self, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 10f, Area = 6f, Duration = 9f, Buffs = Ms(M(Mod.DamagePct, 6 + 3 * i)) })));
            // BS_HAMMERFALL (110, max 5, splash 2, NoDamage, sc Stun): stuns everything in a 5x5 area, no damage
            // (skills/merchant/hammerfall.cpp:15). Mode: a stunning area on the densest pack, no damage.
            c.Weapons.Add(W(WBsHammerFall, "bs_hammerfall", 110, WeaponKind.AreaAtTarget, Targeting.Densest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 0, Cooldown = 2f, Range = 4f, Area = 2f, Delay = 0.2f, Effects = Fxs(Fx(EffectKind.Stun, 20 + 8 * i, 1.2f)) }), 80));
            // BS_WEAPONRESEARCH (107, max 10, passive): ATK and HIT with every weapon in renewal.
            c.Passives.Add(P(PBsWeaponResearch, "bs_weaponresearch", 107, 0, PLv(5, i => Ms(M(Mod.DamagePct, 3 * (i + 1))))));
            // BS_SKINTEMPER (109, max 5, passive): resistance to fire and neutral damage. Mode: less damage taken.
            c.Passives.Add(P(PBsSkinTemper, "bs_skintemper", 109, 0, PLv(5, i => Ms(M(Mod.DamageTakenPct, -3 * (i + 1))))));
            // BS_GREED (1013, max 1): picks up every item around (skills/merchant/greed.cpp:17). Mode: a wider pickup radius
            // (levels 2-3 own).
            c.Passives.Add(P(PBsGreed, "bs_greed", 1013, 0, PLv(3, i => Ms(M(Mod.PickupRadiusPct, 25 * (i + 1))))));
            c.Jobs.Add(Job(JobBlacksmith, "JOB_BLACKSMITH", 2, None, WBsAdrenaline,
                Ids(WBsOverThrust, WBsHammerFall), Ids(PBsWeaponResearch, PBsSkinTemper, PBsGreed),
                Ms(M(Mod.MeleeDamagePct, 20f), M(Mod.MaxHpPct, 15f)), new[] { 2, 0, 1, 0, 2, 0 }));

            // ---------------- ALCHEMIST (18)
            // AM_DEMONSTRATION (229, max 5, Fire, ground unit): Bomb, a burning patch (skills/merchant/bomb.cpp:17, 20-21).
            c.Weapons.Add(W(WAmDemonstration, "am_demonstration", 229, WeaponKind.GroundZone, Targeting.Densest, DamageClass.Ranged, Element.Fire,
                Lv(5, i => new WeaponLevel { Damage = 13 + 4 * i, Tick = 0.5f, Duration = 3f + 0.5f * i, Area = 1.6f, Range = 7f, Cooldown = 3.5f - 0.2f * i })));
            // AM_ACIDTERROR (230, max 5, ignores Flee, sc Bleeding). Mode: an acid flask at the nearest that may make it bleed.
            c.Weapons.Add(W(WAmAcidTerror, "am_acidterror", 230, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Ranged, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 31 + 13 * i, Cooldown = 2f - 0.1f * i, Speed = 8f, Range = 8f, Effects = Fxs(Fx(EffectKind.Dot, 25 + 5 * i, 4f, 3 + i)) })));
            // AM_POTIONPITCHER (231, max 5): throws a potion that heals an ally (skills/merchant/aidpotion.cpp:53-69).
            c.Weapons.Add(W(WAmPotionPitcher, "am_potionpitcher", 231, WeaponKind.Aura, Targeting.LowestHpAlly, DamageClass.Ranged, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 2f - 0.1f * i, Range = 8f, Heal = 6 + 3 * i, HealPct = 0.5f, Duration = 0.1f })));
            // AM_CANNIBALIZE (232, max 5): Summon Flora, plants that fight for the alchemist, by level G_MANDRAGORA 1589,
            // G_HYDRA 1579, G_FLORA 1575, G_PARASITE 1555, G_GEOGRAPHER 1590 (skills/merchant/summonflora.cpp:12).
            // Mode: rooted plants drawn as G_FLORA (one look for every level).
            var flora = W(WAmCannibalize, "am_cannibalize", 232, WeaponKind.Summon, Targeting.Nearest, DamageClass.Ranged, Element.Earth,
                Lv(5, i => new WeaponLevel { Count = 1 + (i + 1) / 2, Cooldown = 2f, Duration = 20f, Damage = 14 + 6 * i, Tick = 1f, Range = 3.5f, Speed = 0f }));
            flora.SummonMobId = 1575;
            flora.SummonHp = 60;
            c.Weapons.Add(flora);
            // AM_AXEMASTERY (226, max 10, passive): ATK with axes.
            c.Passives.Add(P(PAmAxeMastery, "am_axemastery", 226, 0, PLv(5, i => Ms(M(Mod.MeleeDamagePct, 5 * (i + 1))))));
            // AM_LEARNINGPOTION (227, max 10, passive): Potion Research, potions heal more (aidpotion.cpp:53).
            c.Passives.Add(P(PAmLearningPotion, "am_learningpotion", 227, 0, PLv(5, i => Ms(M(Mod.HealPowerPct, 6 * (i + 1))))));
            c.Jobs.Add(Job(JobAlchemist, "JOB_ALCHEMIST", 2, None, WAmDemonstration,
                Ids(WAmAcidTerror, WAmPotionPitcher, WAmCannibalize), Ids(PAmAxeMastery, PAmLearningPotion),
                Ms(M(Mod.DamagePct, 5f), M(Mod.HealPowerPct, 10f)), new[] { 0, 0, 1, 2, 2, 0 }));
        }

        // ================================================================================================================
        // THIEF line: Thief (6) -> Assassin (12) / Rogue (17)
        // ================================================================================================================
        static void AddThiefLine(HordeContent c)
        {
            // TF_POISON (52, max 10, Poison, sc Poison): Envenom. Mode: a quick poison stab that may poison.
            c.Weapons.Add(W(WTfPoison, "tf_poison", 52, WeaponKind.MeleeArc, Targeting.Nearest, DamageClass.Melee, Element.Poison,
                Lv(5, i => new WeaponLevel { Damage = 22 + 8 * i, Cooldown = 0.8f - 0.04f * i, Range = 2f, Arc = 130, Effects = Fxs(Fx(EffectKind.Dot, 40 + 10 * i, 4f, 4 + 2 * i)) })));
            // TF_THROWSTONE (152, max 1, range 7, sc Stun): Stone Fling. Mode: thrown stones that may stun (levels 2-5 own).
            c.Weapons.Add(W(WTfThrowStone, "tf_throwstone", 152, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Ranged, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 14 + 6 * i, Count = 1 + i / 2, Burst = 0.12f, Cooldown = 1.3f - 0.05f * i, Speed = 9f, Range = 7f, Effects = Fxs(Fx(EffectKind.Stun, 5 + 2 * i, 0.8f)) })));
            // TF_DOUBLE (48, max 10, passive): Double Attack, a 7 % per level chance of a double hit (battle.cpp:4311-4328).
            // Mode PROVISIONAL (no per-hit double strike in the engine): critical chance, the closest kind.
            c.Passives.Add(P(PTfDouble, "tf_double", 48, 0, PLv(5, i => Ms(M(Mod.CritChance, 3 * (i + 1))))));
            // TF_MISS (49, max 10, passive): Improve Dodge, Flee.
            c.Passives.Add(P(PTfMiss, "tf_miss", 49, 0, PLv(5, i => Ms(M(Mod.Dodge, 3 * (i + 1))))));
            c.Jobs.Add(Job(JobThief, "JOB_THIEF", 1, Ids(JobAssassin, JobRogue), WTfPoison,
                Ids(WTfThrowStone), Ids(PTfDouble, PTfMiss),
                Ms(M(Mod.Dodge, 10f), M(Mod.MoveSpeedPct, 5f), M(Mod.MaxHpPct, 10f)), new[] { 1, 3, 0, 0, 1, 0 }));

            // ---------------- ASSASSIN (12)
            // AS_SONICBLOW (136, max 10, 8 hits, sc Stun). Mode: eight quick blows at the nearest.
            c.Weapons.Add(W(WAsSonicBlow, "as_sonicblow", 136, WeaponKind.MeleeArc, Targeting.Nearest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 8 + 3 * i, Count = 8, Burst = 0.06f, Cooldown = 1.8f - 0.1f * i, Range = 1.8f, Arc = 50, Effects = Fxs(Fx(EffectKind.Stun, 3 + i, 0.8f)) })));
            // AS_GRIMTOOTH (137, max 5, range 3-7, splash 1). Mode: a strike at range that bursts on its target.
            c.Weapons.Add(W(WAsGrimtooth, "as_grimtooth", 137, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 24 + 8 * i, Cooldown = 1.6f - 0.05f * i, Speed = 10f, Range = 3 + i, Area = 1f })));
            // AS_VENOMDUST (140, max 10, Poison, ground unit, sc Poison). Mode: a poison cloud on the densest pack.
            c.Weapons.Add(W(WAsVenomDust, "as_venomdust", 140, WeaponKind.GroundZone, Targeting.Densest, DamageClass.Melee, Element.Poison,
                Lv(5, i => new WeaponLevel { Damage = 0, Tick = 1f, Duration = 5f + i, Area = 1.8f, Range = 5f, Cooldown = 7f - 0.3f * i, Effects = Fxs(Fx(EffectKind.Dot, 100, 4f, 3 + 1.5f * i)) })));
            // AS_SPLASHER (141, max 10, splash 2): Venom Splasher, the target bursts after a delay.
            c.Weapons.Add(W(WAsSplasher, "as_splasher", 141, WeaponKind.AreaAtTarget, Targeting.Strongest, DamageClass.Melee, Element.Poison,
                Lv(5, i => new WeaponLevel { Damage = 30 + 11 * i, Cooldown = 2f - 0.1f * i, Range = 4f, Area = 2f, Delay = 1.5f })));
            // AS_KATAR (134, max 10, passive): katar mastery, critical with katars.
            c.Passives.Add(P(PAsKatar, "as_katar", 134, 0, PLv(5, i => Ms(M(Mod.CritDamagePct, 8 * (i + 1)), M(Mod.MeleeDamagePct, 2 * (i + 1))))));
            // AS_RIGHT (132, max 5, passive): right-hand mastery, damage of the main hand.
            c.Passives.Add(P(PAsRight, "as_right", 132, 0, PLv(5, i => Ms(M(Mod.MeleeDamagePct, 5 * (i + 1))))));
            c.Jobs.Add(Job(JobAssassin, "JOB_ASSASSIN", 2, None, WAsSonicBlow,
                Ids(WAsGrimtooth, WAsVenomDust, WAsSplasher), Ids(PAsKatar, PAsRight),
                Ms(M(Mod.MeleeDamagePct, 10f), M(Mod.CritChance, 5f), M(Mod.MoveSpeedPct, 5f)), new[] { 2, 3, 0, 0, 0, 1 }));

            // ---------------- ROGUE (17)
            // RG_BACKSTAP (212, max 10, sc Stun): Back Stab, a strong blow from behind. Mode: a heavy stab at the strongest
            // enemy in reach.
            c.Weapons.Add(W(WRgBackstab, "rg_backstap", 212, WeaponKind.MeleeArc, Targeting.Strongest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 45 + 17 * i, Cooldown = 1.5f - 0.05f * i, Range = 1.9f, Arc = 50, Effects = Fxs(Fx(EffectKind.Stun, 10, 1f)) })));
            // RG_RAID (214, max 5, splash 2 around the rogue, sc Raid): Sightless Mind (targets take more damage after:
            // battle.cpp:1754). Mode: a burst around the player that may stun.
            c.Weapons.Add(W(WRgRaid, "rg_raid", 214, WeaponKind.Nova, Targeting.Self, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 14 + 5 * i, Cooldown = 2f - 0.1f * i, Area = 2.3f, Effects = Fxs(Fx(EffectKind.Stun, 15 + 3 * i, 1f)) })));
            // RG_SNATCHER (210, max 10, passive): Gank, steals on attack. Mode PROVISIONAL: more run EXP (the run's loot).
            c.Passives.Add(P(PRgSnatcher, "rg_snatcher", 210, 0, PLv(5, i => Ms(M(Mod.ExpGainPct, 4 * (i + 1))))));
            // RG_TUNNELDRIVE (213, max 5, passive): walk while hidden (status.cpp:8062). Mode: move speed.
            c.Passives.Add(P(PRgTunnelDrive, "rg_tunneldrive", 213, 0, PLv(5, i => Ms(M(Mod.MoveSpeedPct, 4 * (i + 1))))));
            // The Rogue's tree also holds AC_DOUBLE 10 and AC_VULTURE 10 (skill_tree.yml "Rogue"): the Archer's defs.
            c.Jobs.Add(Job(JobRogue, "JOB_ROGUE", 2, None, WRgBackstab,
                Ids(WRgRaid, WAcDouble), Ids(PRgSnatcher, PRgTunnelDrive, PAcVulture),
                Ms(M(Mod.DamagePct, 8f), M(Mod.Dodge, 5f), M(Mod.ExpGainPct, 5f)), new[] { 1, 2, 0, 0, 2, 1 }));
        }
    }
}
