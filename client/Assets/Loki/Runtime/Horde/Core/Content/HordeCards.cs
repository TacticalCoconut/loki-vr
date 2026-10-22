#nullable disable
using System;
using System.Collections.Generic;

namespace Loki.Horde.Content
{
    // ====================================================================================================================
    // Client wave 17 (h17cards): design 1 / point 15 of the Horde design proposal (design notes), which
    // the design approved as written ("Approved design specification."):
    //   A  more cards: every job's new weapons and passives, and the job-free ones (items and monster cards);
    //   B  branches: at level 3 a weapon card offers its two branches; the run keeps the one taken (generated per weapon kind,
    //      named by the proposal's own example for bolts: Barrage / Lance);
    //   C  evolutions: one per job line, on the first job's signature weapon at its top level + a named card; a big chest turns it.
    // EVERY NUMBER HERE IS LOKI'S OWN DESIGN (as in HordeJobs.cs), never an official Ragnarok value. The RO ids are rAthena's
    // (C:\rAthena db/re/skill_db.yml, item_db_*.yml, mob_db.yml), checked one by one (design notes).
    // Id blocks: weapons keep the job blocks of HordeJobs.cs (new ids after the old ones); branches 1000 + id (branch 1) and
    // 2000 + id (branch 2); evolutions 3000 + id; hidden weapons 4001+. Passives keep the job blocks; the job-free cards 300-309.
    // ====================================================================================================================
    public static partial class HordeTables
    {
        // ---- new weapon ids
        const int WNvFireBottle = 3, WNvHolyWater = 4, WNvCobweb = 5, WNvYggBerry = 6, WNvFlyWing = 7;
        const int WSmProvoke = 13;
        const int WMgColdBolt = 24, WMgLightningBolt = 25, WMgSoulStrike = 26, WMgNapalmBeat = 27, WMgSafetyWall = 28, WMgSight = 29;
        const int WAcConcentration = 33;
        const int WAlRuwach = 44, WAlPneuma = 45, WAlAngelus = 46, WAlDecAgi = 47, WAlCrucis = 48, WAlAquaBenedicta = 49;
        const int WMcPushcart = 53;
        const int WTfSandAttack = 62, WTfBackSlide = 63;
        const int WKnPierce = 103, WKnSpearStab = 104, WKnChargeAttack = 105, WKnTwoHandQuicken = 106;
        const int WCrShieldCharge = 114, WCrDefender = 115;
        const int WWzFirePillar = 125, WWzFrostNova = 126, WWzIceWall = 127, WWzEarthSpike = 128, WWzSightrasher = 129;
        const int WSaAutoSpell = 134, WSaAbracadabra = 135, WSaMagicRod = 136;
        const int WHtAnkleSnare = 144, WHtSkidTrap = 145, WHtSandman = 146, WHtBlastMine = 147;
        const int WBaWhistle = 154, WBaAssassinCross = 155, WBaFrostJoker = 156, WCgArrowVulcan = 157;
        const int WDcHumming = 164, WDcScream = 165;
        const int WPrLexAeterna = 174, WPrTurnUndead = 175, WPrGloria = 176, WPrResurrection = 177;
        const int WMoSpiritSpheres = 184, WMoBodyRelocation = 185, WMoKiExplosion = 186;
        const int WWsCartBoost = 193;
        const int WAmSphereMine = 204, WAmLif = 205, WAmAmistr = 206, WAmFilir = 207, WAmVanilmirth = 208;
        const int WAsVenomKnife = 214;
        const int WRgIntimidate = 222, WRgStripWeapon = 223, WRgStripArmor = 224, WRgCloseConfine = 225;
        // hidden weapons (never a card): what the engine strikes with for a passive
        const int WHiddenFalcon = 4001, WHiddenChainCombo = 4002, WHiddenCopiedShot = 4003;
        // evolutions (3000 + the weapon they evolve from)
        const int WEvoBash = 3000 + WSmBash, WEvoFireBolt = 3000 + WMgFireBolt, WEvoDouble = 3000 + WAcDouble, WEvoHolyLight = 3000 + WAlHolyLight,
            WEvoMammonite = 3000 + WMcMammonite, WEvoEnvenom = 3000 + WTfPoison;

        // ---- new passive ids (design notes: fixed for h17pass)
        const int PHydraCard = 300, PSkelWorkerCard = 301, PTharaFrogCard = 302, PRaydricCard = 303, PMantisCard = 304, PWhisperCard = 305,
            PSoldierSkeletonCard = 306, PSavageBabeCard = 307, PElderWillowCard = 308, PDropsCard = 309;
        const int PSmTwoHand = 12, PSmFatalBlow = 13, PSmAutoBerserk = 14, PSmMovingRecovery = 15;
        const int PMgMysticFocus = 22;
        const int PAcMakingArrow = 32, PAcFireArrow = 33, PAcSilverArrow = 34, PAcCrystalArrow = 35;
        const int PMcDiscount = 52, PMcVending = 53, PMcIdentify = 54;
        const int PTfHiding = 62, PTfSteal = 63, PTfDetoxify = 64;
        const int PKnAutoCounter = 102, PKnCavalierMastery = 103;
        const int PCrReflectShield = 111, PCrMartyr = 112, PCrProvidence = 113;
        const int PWzMagicCrasher = 120, PWzSoulDrain = 121;
        const int PSaEndowFire = 132, PSaEndowWater = 133, PSaEndowWind = 134, PSaEndowEarth = 135, PSaLandProtector = 136;
        const int PHtFalconMastery = 141, PHtSteelCrow = 142, PHtDetect = 143;
        const int PBdEncore = 151;
        const int PPrAspersio = 171;
        const int PMoChainCombo = 182, PMoSteelBody = 183, PMoBladeStop = 184;
        const int PBsHiltBinding = 193, PBsWeaponPerfection = 194, PBsMaximize = 195, PBsOreDiscovery = 196;
        const int PAmChemicalProtection = 202;
        const int PAsEnchantPoison = 212, PAsCloaking = 213, PAsLefthand = 214, PAsSonicAccel = 215;
        const int PRgPlagiarism = 222, PRgGangster = 223;

        static ModDef[] NoModsAt(int i) => new ModDef[0];

        static void AddCardsW17(HordeContent c)
        {
            AddJobFreeW17(c);
            AddSwordmanW17(c);
            AddMageW17(c);
            AddArcherW17(c);
            AddAcolyteW17(c);
            AddMerchantW17(c);
            AddThiefW17(c);
            AddHiddenW17(c);
            ApplySingleVsAreaBalance(c);
            AddEvolutionsW17(c);
            AddBranchesW17(c); // last: every weapon above (old and new) gets its two branches
        }

        /// <summary>Single-target attacks are +50% stronger (1.5x); area attacks are ~10% weaker (0.9x).</summary>
        static void ApplySingleVsAreaBalance(HordeContent c)
        {
            foreach (var w in c.Weapons)
            {
                if (w == null || w.Levels == null) continue;
                foreach (var l in w.Levels)
                {
                    if (l == null || l.Damage <= 0) continue;
                    bool isSingle = false;
                    switch (w.Kind)
                    {
                        case WeaponKind.Bolt:
                            isSingle = l.Area <= 0.05f && l.Pierce <= 1 && w.Target != Targeting.Self;
                            break;
                        case WeaponKind.MeleeArc:
                            isSingle = l.Arc > 0f && l.Arc <= 65f && l.Area <= 0.05f;
                            break;
                        case WeaponKind.Mark:
                        case WeaponKind.Pet:
                            isSingle = l.Area <= 0.05f;
                            break;
                        case WeaponKind.Summon:
                            isSingle = !w.SummonBursts && l.Area <= 0.05f;
                            break;
                        case WeaponKind.Dash:
                            isSingle = w.Target == Targeting.Nearest || w.Target == Targeting.Strongest;
                            break;
                    }
                    float mul = isSingle ? 1.5f : 0.9f;
                    l.Damage = (int)Math.Max(1.0, Math.Round(l.Damage * mul));
                }
            }
        }

        /// <summary>Adds weapon and passive ids to a job's card lists (the old ones stay first).</summary>
        static void Extend(HordeContent c, int jobId, int[] weapons, int[] passives)
        {
            var j = c.Job(jobId);
            if (j == null) throw new InvalidOperationException("no job " + jobId);
            var w = new List<int>(j.Weapons); foreach (int id in weapons) if (!w.Contains(id)) w.Add(id); j.Weapons = w.ToArray();
            var p = new List<int>(j.Passives); foreach (int id in passives) if (!p.Contains(id)) p.Add(id); j.Passives = p.ToArray();
        }

        static WeaponDef Item(WeaponDef w, int itemId) { w.ItemId = itemId; return w; }
        /// <summary>Client wave 18 (h18feel), point 37: the Yggdrasil Berry's heal by level (its new, weaker baseline; the balance runner may
        /// set the old one back to compare: client/tools/w8content/HordeBalance --oldberry).</summary>
        public static float[] BerryCooldown = { 90f, 80f, 70f }, BerryHealPct = { 10f, 15f, 20f };
        public static float BerryBelowPct = 50f;

        // ================================================================================================================
        // JOB-FREE (the Novice's lists: every player). Weapons drawn as items, cards as the card items.
        // ================================================================================================================
        static void AddJobFreeW17(HordeContent c)
        {
            // Fire_Bottle 7135 (Bottle Grenade, AM_DEMONSTRATION's item): a burning puddle where enemies crowd.
            c.Weapons.Add(Item(W(WNvFireBottle, "nv_firebottle", 0, WeaponKind.GroundZone, Targeting.Densest, DamageClass.Ranged, Element.Fire,
                Lv(5, i => new WeaponLevel { Damage = 8 + 3 * i, Tick = 0.5f, Duration = 3f + 0.4f * i, Area = 1.6f + 0.1f * i, Range = 7f, Cooldown = 4.5f - 0.25f * i }), 80), 7135));
            // Holy_Water 523: a holy splash on the strongest enemy; allies in it are healed.
            c.Weapons.Add(Item(W(WNvHolyWater, "nv_holywater", 0, WeaponKind.GroundZone, Targeting.Strongest, DamageClass.Magic, Element.Holy,
                Lv(5, i => new WeaponLevel { Damage = 22 + 8 * i, Heal = 6 + 3 * i, Tick = 0.5f, Duration = 1f, Area = 1.5f + 0.1f * i, Range = 7f, Cooldown = 5.5f - 0.25f * i }), 80), 523));
            // Spiderweb 1025 (Cobweb): a web trap that slows.
            c.Weapons.Add(Item(W(WNvCobweb, "nv_cobweb", 0, WeaponKind.Trap, Targeting.Self, DamageClass.Ranged, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 4 + 2 * i, Cooldown = 3f - 0.2f * i, Count = 2 + i / 2, Delay = 0.4f, Area = 1.4f + 0.1f * i, Duration = 15f, Effects = Fxs(Fx(EffectKind.Slow, 100, 3f, 40 + 5 * i)) }), 80), 1025));
            // Yggdrasilberry 607: a heal when hurt. Client wave 18 (h18feel), the design's point 37: it also SAVES its holder's life (HordeSim.Souls.cs
            // YggSave: full HP, the enemies round it cleared, 4 s invulnerable; alone it is spent and its slot burnt, in a party it rests 150 s);
            // "I am weakening the currently existing power entirely so its power budget can be directed towards the revive mechanic": the heal's
            // new baseline at every level is BerryHealPct / BerryCooldown below 50 % (was 40 / 50 / 60 % every 40 / 35 / 30 s below 60 %)
            c.Weapons.Add(Item(W(WNvYggBerry, "nv_yggberry", 0, WeaponKind.Aura, Targeting.LowestHpAlly, DamageClass.Magic, Element.Neutral,
                Lv(3, i => new WeaponLevel { Cooldown = BerryCooldown[i], Range = 0.3f, HealPct = BerryHealPct[i], BelowPct = BerryBelowPct, Duration = 0.1f }), 60), 607));
            // Wing_Of_Fly 601: when 6+ enemies touch you, you blink 5 cells away (to the side with the fewest).
            var fly = Item(W(WNvFlyWing, "nv_flywing", 0, WeaponKind.Dash, Targeting.Self, DamageClass.Magic, Element.Neutral,
                Lv(3, i => new WeaponLevel { Count = i < 2 ? 6 : 5, Range = 5f + i, Cooldown = 8f - i }), 70), 601);
            fly.DashOn = DashTrigger.Crowded;
            c.Weapons.Add(fly);

            // Monster cards (db/re/item_db_etc.yml Script decides the modifier; the size is the mode's own).
            // Hydra_Card 4035: "bonus2 bAddRace,RC_DemiHuman,20;"
            c.Passives.Add(P(PHydraCard, "hydra_card", 0, 4035, PLv(5, i => Ms(M(Mod.DamageVsRacePct, 6 * (i + 1), (int)Race.Demihuman)))));
            // Skel_Worker_Card 4092: "bonus2 bAddSize,Size_Medium,15; bonus bBaseAtk,5;"
            c.Passives.Add(P(PSkelWorkerCard, "skel_worker_card", 0, 4092, PLv(5, i => Ms(M(Mod.DamageVsSizePct, 6 * (i + 1), (int)UnitSize.Medium)))));
            // Thara_Frog_Card 4058: "bonus2 bSubRace,RC_DemiHuman,30;"
            c.Passives.Add(P(PTharaFrogCard, "thara_frog_card", 0, 4058, PLv(5, i => Ms(M(Mod.DamageTakenVsRacePct, -6 * (i + 1), (int)Race.Demihuman)))));
            // Daydric_Card 4133 (Raydric Card): "bonus2 bSubEle,Ele_Neutral,20;"
            c.Passives.Add(P(PRaydricCard, "raydric_card", 0, 4133, PLv(5, i => Ms(M(Mod.DamageTakenVsElementPct, -5 * (i + 1), (int)Element.Neutral)))));
            // Mantis_Card 4079: "bonus bStr,3;"
            c.Passives.Add(P(PMantisCard, "mantis_card", 0, 4079, PLv(5, i => Ms(M(Mod.Str, 2 * (i + 1))))));
            // Whisper_Card 4102: "bonus bFlee,20; bonus2 bSubEle,Ele_Ghost,-50;"
            c.Passives.Add(P(PWhisperCard, "whisper_card", 0, 4102, PLv(5, i => Ms(M(Mod.Dodge, 2 * (i + 1))))));
            // Soldier_Skeleton_Card 4086: "bonus bCritical,9;"
            c.Passives.Add(P(PSoldierSkeletonCard, "soldier_skeleton_card", 0, 4086, PLv(5, i => Ms(M(Mod.CritChance, 3 * (i + 1))))));
            // Savage_Babe_Card 4017: "bonus2 bAddEff,Eff_Stun,500;" (5 % to stun)
            c.Passives.Add(P(PSavageBabeCard, "savage_babe_card", 0, 4017, PLv(5, i => Ms(M(Mod.StunOnHitPct, 2 * (i + 1))))));
            // Elder_Wilow_Card 4052: "bonus bInt,2;"
            c.Passives.Add(P(PElderWillowCard, "elder_willow_card", 0, 4052, PLv(5, i => Ms(M(Mod.Int, 2 * (i + 1))))));
            // Drops_Card 4004: "bonus bDex,1; bonus bHit,3;"
            c.Passives.Add(P(PDropsCard, "drops_card", 0, 4004, PLv(5, i => Ms(M(Mod.Dex, 2 * (i + 1))))));

            Extend(c, JobNovice, Ids(WNvFireBottle, WNvHolyWater, WNvCobweb, WNvYggBerry, WNvFlyWing),
                Ids(PHydraCard, PSkelWorkerCard, PTharaFrogCard, PRaydricCard, PMantisCard, PWhisperCard, PSoldierSkeletonCard, PSavageBabeCard, PElderWillowCard, PDropsCard));
        }

        // ================================================================================================================
        // SWORDMAN line
        // ================================================================================================================
        static void AddSwordmanW17(HordeContent c)
        {
            // SM_PROVOKE (6, max 10): enemies around the swordman rush it and take more damage (its DEF lowered: status.cpp). Mode: a ring of marks.
            c.Weapons.Add(W(WSmProvoke, "sm_provoke", 6, WeaponKind.Mark, Targeting.Self, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Area = 3f + 0.3f * i, MarkPct = 15 + 5 * i, Taunt = true, Duration = 4f + 0.5f * i, Cooldown = 7f - 0.3f * i })));
            // SM_TWOHAND (3, max 10, passive): two-handed sword mastery. Mode: wider swings and a little melee damage.
            c.Passives.Add(P(PSmTwoHand, "sm_twohand", 3, 0, PLv(5, i => Ms(M(Mod.ArcPct, 8 * (i + 1)), M(Mod.MeleeDamagePct, 3 * (i + 1))))));
            // SM_FATALBLOW (145, max 1): Bash may stun. Mode: melee hits may stun (levels 2-3 own); Bash's evolution partner.
            c.Passives.Add(P(PSmFatalBlow, "sm_fatalblow", 145, 0, PLv(3, i => Ms(M(Mod.StunOnHitPct, new[] { 4, 7, 10 }[i], 1)))));
            // SM_AUTOBERSERK (146, max 1): under 25 % HP, more ATK. The trigger is h17pass's (proc engine): no modifier here.
            c.Passives.Add(P(PSmAutoBerserk, "sm_autoberserk", 146, 0, PLv(3, NoModsAt)));
            // SM_MOVINGRECOVERY (144, max 1): HP recovery while walking (levels 2-3 own).
            c.Passives.Add(P(PSmMovingRecovery, "sm_movingrecovery", 144, 0, PLv(3, i => Ms(M(Mod.MovingRegen, 1f + 0.5f * i)))));
            Extend(c, JobSwordman, Ids(WSmProvoke), Ids(PSmTwoHand, PSmFatalBlow, PSmAutoBerserk, PSmMovingRecovery));

            // ---------------- KNIGHT
            // KN_PIERCE (56, max 10): hits a small / medium / large target 1 / 2 / 3 times (skills/swordman/pierce.cpp).
            var pierce = W(WKnPierce, "kn_pierce", 56, WeaponKind.MeleeArc, Targeting.Strongest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 11 + 4 * i, Cooldown = 1.2f - 0.05f * i, Range = 2.2f, Arc = 40 }));
            pierce.HitsBySize = true;
            c.Weapons.Add(pierce);
            // KN_SPEARSTAB (58, max 10, knockback 6): a thrust that pushes everything in a line.
            c.Weapons.Add(W(WKnSpearStab, "kn_spearstab", 58, WeaponKind.MeleeArc, Targeting.Nearest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 26 + 10 * i, Cooldown = 1.5f - 0.06f * i, Range = 3.5f + 0.2f * i, Arc = 30, Effects = Fxs(Fx(EffectKind.Knockback, 100, 0, 3)) })));
            // KN_CHARGEATK (1001, max 1): the knight charges at a far target. Mode: a dash through a line of enemies (levels 2-5 own).
            var charge = W(WKnChargeAttack, "kn_chargeatk", 1001, WeaponKind.Dash, Targeting.Farthest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Range = 6f + 0.5f * i, Area = 1f, Damage = 30 + 12 * i, Cooldown = 4f - 0.2f * i }));
            charge.DashOn = DashTrigger.Cooldown;
            c.Weapons.Add(charge);
            // KN_TWOHANDQUICKEN (60, max 10): attack speed (status.cpp). Mode: a burst of attack speed.
            c.Weapons.Add(W(WKnTwoHandQuicken, "kn_twohandquicken", 60, WeaponKind.Aura, Targeting.Self, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 12f, Duration = 6f + 0.5f * i, Buffs = Ms(M(Mod.CooldownPct, -(12 + 3 * i))) }), 80));
            // KN_AUTOCOUNTER (61, max 5): a counter when hit. The trigger is h17pass's: no modifier here.
            c.Passives.Add(P(PKnAutoCounter, "kn_autocounter", 61, 0, PLv(5, NoModsAt)));
            // KN_CAVALIERMASTERY (64, max 5): fights well while riding. Mode: melee damage while walking.
            c.Passives.Add(P(PKnCavalierMastery, "kn_cavaliermastery", 64, 0, PLv(5, i => Ms(M(Mod.MovingMeleePct, 5 * (i + 1))))));
            Extend(c, JobKnight, Ids(WKnPierce, WKnSpearStab, WKnChargeAttack, WKnTwoHandQuicken), Ids(PKnAutoCounter, PKnCavalierMastery));

            // ---------------- CRUSADER
            // CR_SHIELDCHARGE (250, max 5, sc Stun, knockback): a dash that stuns.
            var shieldCharge = W(WCrShieldCharge, "cr_shieldcharge", 250, WeaponKind.Dash, Targeting.Nearest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Range = 5f, Area = 1f, Damage = 25 + 10 * i, Cooldown = 4.5f - 0.2f * i, Effects = Fxs(Fx(EffectKind.Stun, 100, 1f + 0.1f * i)) }));
            shieldCharge.DashOn = DashTrigger.Cooldown;
            c.Weapons.Add(shieldCharge);
            // CR_DEFENDER (257, max 5): less ranged damage, slower walk (status.cpp). Mode: a front guard against shots; you walk slower.
            var defender = W(WCrDefender, "cr_defender", 257, WeaponKind.Parry, Targeting.Self, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Count = 2 + i / 2, Cooldown = 3f - 0.3f * i, Arc = 140, Area = 1.1f, Buffs = Ms(M(Mod.MoveSpeedPct, -(15 - 2 * i))) }), 80);
            defender.Parry = ParryFlags.Shots;
            c.Weapons.Add(defender);
            // CR_REFLECTSHIELD (252): melee reflected. The trigger is h17pass's: no modifier here.
            c.Passives.Add(P(PCrReflectShield, "cr_reflectshield", 252, 0, PLv(5, NoModsAt)));
            // PA_SACRIFICE (368): Martyr's Reckoning, HP for damage.
            c.Passives.Add(P(PCrMartyr, "pa_sacrifice", 368, 0, PLv(5, i => Ms(M(Mod.DamagePct, 8 * (i + 1)), M(Mod.HpDrainPct, 0.5f)))));
            // CR_PROVIDENCE (256): the party resists Demons and Holy... Mode: you and allies near take less from Demons and Undead.
            c.Passives.Add(P(PCrProvidence, "cr_providence", 256, 0, PLv(5, i => Ms(M(Mod.ProvidencePct, -6 * (i + 1))))));
            Extend(c, JobCrusader, Ids(WCrShieldCharge, WCrDefender), Ids(PCrReflectShield, PCrMartyr, PCrProvidence));
        }

        // ================================================================================================================
        // MAGE line
        // ================================================================================================================
        static void AddMageW17(HordeContent c)
        {
            // MG_COLDBOLT (14) / MG_LIGHTNINGBOLT (20) (max 10, HitCount = level): Fire Bolt's elements.
            c.Weapons.Add(W(WMgColdBolt, "mg_coldbolt", 14, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Magic, Element.Water,
                Lv(5, i => new WeaponLevel { Damage = 13 + 3 * i, Count = 1 + i, Burst = 0.12f, Cooldown = 1.6f - 0.05f * i, Speed = 8f, Range = 8f })));
            c.Weapons.Add(W(WMgLightningBolt, "mg_lightningbolt", 20, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Magic, Element.Wind,
                Lv(5, i => new WeaponLevel { Damage = 13 + 3 * i, Count = 1 + i, Burst = 0.12f, Cooldown = 1.6f - 0.05f * i, Speed = 8f, Range = 8f })));
            // MG_SOULSTRIKE (13, Ghost, 1-5 hits): homing ghost bolts.
            var soul = W(WMgSoulStrike, "mg_soulstrike", 13, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Magic, Element.Ghost,
                Lv(5, i => new WeaponLevel { Damage = 9 + 3 * i, Count = 1 + (i + 1) / 2, Spread = 25, Cooldown = 1.4f - 0.05f * i, Speed = 7f, Range = 9f }));
            soul.Homing = true;
            c.Weapons.Add(soul);
            // MG_NAPALMBEAT (11, Ghost, splash): a ghost bolt that splashes.
            c.Weapons.Add(W(WMgNapalmBeat, "mg_napalmbeat", 11, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Magic, Element.Ghost,
                Lv(5, i => new WeaponLevel { Damage = 14 + 5 * i, Area = 1.3f + 0.1f * i, Cooldown = 1.5f - 0.06f * i, Speed = 8f, Range = 8f })));
            // MG_SAFETYWALL (12, max 10): blocks melee hits, level + 1 of them (skill.cpp). Mode: a guard that stops touches.
            var wall = W(WMgSafetyWall, "mg_safetywall", 12, WeaponKind.Parry, Targeting.Self, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { Count = 3 + i, Duration = 8f + i, Cooldown = 10f - 0.5f * i, Area = 1f }));
            wall.Parry = ParryFlags.Touches;
            c.Weapons.Add(wall);
            // MG_SIGHT (10, Fire): a fire ball circling the caster. Fire Bolt's evolution partner.
            c.Weapons.Add(W(WMgSight, "mg_sight", 10, WeaponKind.Orbiter, Targeting.Self, DamageClass.Magic, Element.Fire,
                Lv(5, i => new WeaponLevel { Count = 1, Range = 1.6f, Area = 0.55f, Speed = 0.6f, Tick = 0.5f, Damage = 6 + 3 * i }), 80));
            // Mystic Focus (Loki's own): every few seconds the next spell is doubled. The trigger is h17pass's: no modifier here.
            c.Passives.Add(P(PMgMysticFocus, "mg_mysticfocus", 0, 0, PLv(3, NoModsAt)));
            Extend(c, JobMage, Ids(WMgColdBolt, WMgLightningBolt, WMgSoulStrike, WMgNapalmBeat, WMgSafetyWall, WMgSight), Ids(PMgMysticFocus));

            // ---------------- WIZARD
            // WZ_FIREPILLAR (80, Fire, ground unit trap): a pillar that bursts when an enemy steps on it.
            c.Weapons.Add(W(WWzFirePillar, "wz_firepillar", 80, WeaponKind.Trap, Targeting.Self, DamageClass.Magic, Element.Fire,
                Lv(5, i => new WeaponLevel { Damage = 40 + 15 * i, Cooldown = 2.5f - 0.15f * i, Count = 2 + i / 2, Delay = 0.5f, Area = 1.3f, Duration = 20f })));
            // WZ_FROSTNOVA (88, Water, sc Freeze): a freezing ring around the caster.
            c.Weapons.Add(W(WWzFrostNova, "wz_frostnova", 88, WeaponKind.Nova, Targeting.Self, DamageClass.Magic, Element.Water,
                Lv(5, i => new WeaponLevel { Damage = 12 + 4 * i, Cooldown = 3f - 0.15f * i, Area = 2.6f + 0.15f * i, Effects = Fxs(Fx(EffectKind.Freeze, 30 + 5 * i, 1.5f)) })));
            // WZ_ICEWALL (87, Water, ground unit): a wall of ice. Mode: a ring under you that enemies cannot cross (bosses can).
            var iceWall = W(WWzIceWall, "wz_icewall", 87, WeaponKind.GroundZone, Targeting.Self, DamageClass.Magic, Element.Water,
                Lv(5, i => new WeaponLevel { Damage = 0, Tick = 0.5f, Area = 1.8f, Duration = 2.5f + 0.3f * i, Cooldown = 9f - 0.4f * i }), 70);
            iceWall.Zone = ZoneFlags.BlocksEnemies;
            c.Weapons.Add(iceWall);
            // WZ_EARTHSPIKE (90, Earth, 1-5 hits): spikes from the ground. Mode: spikes that run through a line of enemies.
            c.Weapons.Add(W(WWzEarthSpike, "wz_earthspike", 90, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Magic, Element.Earth,
                Lv(5, i => new WeaponLevel { Damage = 16 + 5 * i, Pierce = 3 + i, Count = 1 + i / 3, Burst = 0.2f, Cooldown = 1.8f - 0.08f * i, Speed = 9f, Range = 8f })));
            // WZ_SIGHTRASHER (81, Fire, knockback): fire in eight directions around the caster.
            c.Weapons.Add(W(WWzSightrasher, "wz_sightrasher", 81, WeaponKind.Bolt, Targeting.Self, DamageClass.Magic, Element.Fire,
                Lv(5, i => new WeaponLevel { Damage = 15 + 5 * i, Count = 8, Spread = 45, Speed = 8f, Range = 5f + 0.5f * i, Cooldown = 2.2f - 0.1f * i })));
            // HW_MAGICCRASHER (365) / HW_SOULDRAIN (364): High Wizard skills the proposal gives the Wizard.
            c.Passives.Add(P(PWzMagicCrasher, "hw_magiccrasher", 365, 0, PLv(5, i => Ms(M(Mod.MagicVsBossPct, 8 * (i + 1))))));
            // Soul Drain: a heal on every spell kill. The trigger is h17pass's: no modifier here.
            c.Passives.Add(P(PWzSoulDrain, "hw_souldrain", 364, 0, PLv(5, NoModsAt)));
            Extend(c, JobWizard, Ids(WWzFirePillar, WWzFrostNova, WWzIceWall, WWzEarthSpike, WWzSightrasher), Ids(PWzMagicCrasher, PWzSoulDrain));

            // ---------------- SAGE
            // SA_AUTOSPELL (279): a free bolt on attack (Fire / Cold / Lightning Bolt by level in RO). Mode: hits may cast one at random.
            var auto = W(WSaAutoSpell, "sa_autospell", 279, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { ProcPct = 8 + 3 * i, Cooldown = 0.6f, Range = 8f, Speed = 8f }));
            auto.CastOnHit = true; auto.RandomOf = Ids(WMgFireBolt, WMgColdBolt, WMgLightningBolt);
            c.Weapons.Add(auto);
            // SA_ABRACADABRA (290): a random skill. Mode: a random spell of the mage line every few seconds.
            var abra = W(WSaAbracadabra, "sa_abracadabra", 290, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 6f - 0.5f * i, Range = 8f, Speed = 8f }), 80);
            abra.RandomOf = Ids(WMgFireBolt, WMgColdBolt, WMgLightningBolt, WMgSoulStrike, WMgNapalmBeat, WMgFrostDiver, WMgThunderStorm, WWzJupitel, WWzMeteor, WWzHeavenDrive, WWzFrostNova, WMgFireWall);
            c.Weapons.Add(abra);
            // SA_MAGICROD (276): absorbs a spell aimed at the caster. Mode: a guard that catches a shot and throws it back.
            var rod = W(WSaMagicRod, "sa_magicrod", 276, WeaponKind.Parry, Targeting.Self, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { Count = 1 + i / 2, Cooldown = 4f - 0.3f * i, Damage = 20 + 8 * i, Area = 1.1f, Speed = 10f }));
            rod.Parry = ParryFlags.Shots | ParryFlags.Returns;
            c.Weapons.Add(rod);
            // SA_FLAMELAUNCHER 280 / SA_FROSTWEAPON 281 / SA_LIGHTNINGLOADER 282 / SA_SEISMICWEAPON 283: Endow. Mode: every Neutral weapon
            // takes the element (one of the four per run: group 2).
            c.Passives.Add(Group(P(PSaEndowFire, "sa_flamelauncher", 280, 0, PLv(3, i => Ms(M(Mod.EndowElement, 1, (int)Element.Fire), M(Mod.ElementDamagePct, 5 * (i + 1), (int)Element.Fire))), 25), 2));
            c.Passives.Add(Group(P(PSaEndowWater, "sa_frostweapon", 281, 0, PLv(3, i => Ms(M(Mod.EndowElement, 1, (int)Element.Water), M(Mod.ElementDamagePct, 5 * (i + 1), (int)Element.Water))), 25), 2));
            c.Passives.Add(Group(P(PSaEndowWind, "sa_lightningloader", 282, 0, PLv(3, i => Ms(M(Mod.EndowElement, 1, (int)Element.Wind), M(Mod.ElementDamagePct, 5 * (i + 1), (int)Element.Wind))), 25), 2));
            c.Passives.Add(Group(P(PSaEndowEarth, "sa_seismicweapon", 283, 0, PLv(3, i => Ms(M(Mod.EndowElement, 1, (int)Element.Earth), M(Mod.ElementDamagePct, 5 * (i + 1), (int)Element.Earth))), 25), 2));
            // SA_LANDPROTECTOR (288): no ground skill works inside. Mode: an enemy area strike near you is cancelled (every few seconds).
            c.Passives.Add(P(PSaLandProtector, "sa_landprotector", 288, 0, PLv(3, i => Ms(M(Mod.CancelStrikeCells, 2f + 0.5f * i), M(Mod.CancelStrikeEvery, 10 - 2 * i)))));
            Extend(c, JobSage, Ids(WSaAutoSpell, WSaAbracadabra, WSaMagicRod), Ids(PSaEndowFire, PSaEndowWater, PSaEndowWind, PSaEndowEarth, PSaLandProtector));
        }

        static PassiveDef Group(PassiveDef p, int group) { p.Group = group; return p; }

        // ================================================================================================================
        // ARCHER line
        // ================================================================================================================
        static void AddArcherW17(HordeContent c)
        {
            // AC_CONCENTRATION (45, max 10): Improve Concentration, +AGI / DEX. Mode: a burst of attack speed.
            c.Weapons.Add(W(WAcConcentration, "ac_concentration", 45, WeaponKind.Aura, Targeting.Self, DamageClass.Ranged, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 12f, Duration = 6f + 0.5f * i, Buffs = Ms(M(Mod.CooldownPct, -(10 + 2 * i)), M(Mod.Dex, 2 + i)) }), 80));
            // AC_MAKINGARROW (147): kills drop arrow bundles. The trigger is h17pass's: no modifier here.
            c.Passives.Add(P(PAcMakingArrow, "ac_makingarrow", 147, 0, PLv(3, NoModsAt)));
            // Fire_Arrow 1752 / Silver_Arrow 1751 / Crystal_Arrow 1754: the bow's element (one of the three per run: group 1).
            c.Passives.Add(Group(P(PAcFireArrow, "ac_firearrow", 0, 1752, PLv(3, i => Ms(M(Mod.EndowElement, 2, (int)Element.Fire), M(Mod.ElementDamagePct, 5 * (i + 1), (int)Element.Fire))), 34), 1));
            c.Passives.Add(Group(P(PAcSilverArrow, "ac_silverarrow", 0, 1751, PLv(3, i => Ms(M(Mod.EndowElement, 2, (int)Element.Holy), M(Mod.ElementDamagePct, 5 * (i + 1), (int)Element.Holy))), 33), 1));
            c.Passives.Add(Group(P(PAcCrystalArrow, "ac_crystalarrow", 0, 1754, PLv(3, i => Ms(M(Mod.EndowElement, 2, (int)Element.Wind), M(Mod.ElementDamagePct, 5 * (i + 1), (int)Element.Wind))), 33), 1));
            Extend(c, JobArcher, Ids(WAcConcentration), Ids(PAcMakingArrow, PAcFireArrow, PAcSilverArrow, PAcCrystalArrow));

            // ---------------- HUNTER
            // HT_ANKLESNARE (117): holds the one who steps in. HT_SKIDTRAP (115): pushes. HT_SANDMAN (119): sleep. HT_BLASTMINE (122, Wind).
            c.Weapons.Add(W(WHtAnkleSnare, "ht_anklesnare", 117, WeaponKind.Trap, Targeting.Self, DamageClass.Ranged, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 5 + 2 * i, Cooldown = 2.2f - 0.1f * i, Count = 3 + i / 2, Delay = 0.5f, Area = 1f, Duration = 20f, Effects = Fxs(Fx(EffectKind.Stun, 100, 2.5f + 0.5f * i)) })));
            c.Weapons.Add(W(WHtSkidTrap, "ht_skidtrap", 115, WeaponKind.Trap, Targeting.Self, DamageClass.Ranged, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 10 + 4 * i, Cooldown = 1.8f, Count = 3 + i / 2, Delay = 0.5f, Area = 1.2f, Duration = 20f, Effects = Fxs(Fx(EffectKind.Knockback, 100, 0, 4 + 0.5f * i)) })));
            c.Weapons.Add(W(WHtSandman, "ht_sandman", 119, WeaponKind.Trap, Targeting.Self, DamageClass.Ranged, Element.Earth,
                Lv(5, i => new WeaponLevel { Damage = 6 + 2 * i, Cooldown = 2.5f - 0.1f * i, Count = 2 + i / 2, Delay = 0.5f, Area = 2f + 0.15f * i, Duration = 20f, Effects = Fxs(Fx(EffectKind.Stun, 70 + 5 * i, 3f)) })));
            c.Weapons.Add(W(WHtBlastMine, "ht_blastmine", 122, WeaponKind.Trap, Targeting.Self, DamageClass.Ranged, Element.Wind,
                Lv(5, i => new WeaponLevel { Damage = 55 + 20 * i, Cooldown = 2f - 0.1f * i, Count = 3 + i / 2, Delay = 0.6f, Area = 1.6f, Duration = 20f })));
            // HT_FALCON (127): the falcon. Mode: a falcon companion that strikes by itself (the hidden weapon WHiddenFalcon).
            var falcon = P(PHtFalconMastery, "ht_falcon", 127, 0, PLv(5, NoModsAt));
            falcon.GivesWeapon = WHiddenFalcon;
            c.Passives.Add(falcon);
            // HT_STEELCROW (128): Blitz Beat's damage. Mode: the falcon's and Blitz Beat's damage.
            c.Passives.Add(P(PHtSteelCrow, "ht_steelcrow", 128, 0, PLv(5, i => Ms(M(Mod.WeaponDamagePct, 15 * (i + 1), WHiddenFalcon), M(Mod.WeaponDamagePct, 15 * (i + 1), WHtBlitzBeat)))));
            // HT_DETECTING (130): reveals hidden enemies. Mode: traps arm sooner and are wider.
            c.Passives.Add(P(PHtDetect, "ht_detecting", 130, 0, PLv(3, i => Ms(M(Mod.TrapPct, 30 * (i + 1), 0), M(Mod.TrapPct, 5 * (i + 1), 1)))));
            Extend(c, JobHunter, Ids(WHtAnkleSnare, WHtSkidTrap, WHtSandman, WHtBlastMine), Ids(PHtFalconMastery, PHtSteelCrow, PHtDetect));

            // ---------------- BARD / DANCER
            // BA_WHISTLE (319): Flee and perfect dodge for those around. BA_ASSASSINCROSS (320): attack speed. BA_FROSTJOKER (318): may freeze.
            c.Weapons.Add(W(WBaWhistle, "ba_whistle", 319, WeaponKind.Aura, Targeting.Self, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 6f, Area = 5f, Duration = 7f, Buffs = Ms(M(Mod.Dodge, 4 + 1.5f * i), M(Mod.MoveSpeedPct, 3 + i)) })));
            c.Weapons.Add(W(WBaAssassinCross, "ba_assassincross", 320, WeaponKind.Aura, Targeting.Self, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 6f, Area = 5f, Duration = 7f, Buffs = Ms(M(Mod.CooldownPct, -(8 + 2 * i))) })));
            c.Weapons.Add(W(WBaFrostJoker, "ba_frostjoker", 318, WeaponKind.Aura, Targeting.Self, DamageClass.Magic, Element.Water,
                Lv(5, i => new WeaponLevel { Cooldown = 4f - 0.2f * i, Area = 4f + 0.25f * i, Duration = 0.1f, Effects = Fxs(Fx(EffectKind.Freeze, 25 + 5 * i, 1.5f)) })));
            // CG_ARROWVULCAN (394, 9 hits; Clown / Gypsy): a barrage of arrows (both lines here, as the proposal says).
            c.Weapons.Add(W(WCgArrowVulcan, "cg_arrowvulcan", 394, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Ranged, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 10 + 3 * i, Count = 5 + i / 2, Burst = 0.05f, Cooldown = 2f - 0.08f * i, Speed = 11f, Range = 8f })));
            // DC_HUMMING (327): HIT for those around. DC_SCREAM (326): may stun those around.
            c.Weapons.Add(W(WDcHumming, "dc_humming", 327, WeaponKind.Aura, Targeting.Self, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 6f, Area = 5f, Duration = 7f, Buffs = Ms(M(Mod.DamagePct, 4 + 1.5f * i), M(Mod.CritChance, 2 + i)) })));
            c.Weapons.Add(W(WDcScream, "dc_scream", 326, WeaponKind.Aura, Targeting.Self, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 4f - 0.2f * i, Area = 4f + 0.25f * i, Duration = 0.1f, Effects = Fxs(Fx(EffectKind.Stun, 25 + 5 * i, 1.2f)) })));
            // BD_ENCORE (305): songs again. Mode: auras last longer and may pulse twice (Bard and Dancer).
            c.Passives.Add(P(PBdEncore, "bd_encore", 305, 0, PLv(3, i => Ms(M(Mod.DurationPct, 10 * (i + 1)), M(Mod.EchoPct, 25 * (i + 1))))));
            Extend(c, JobBard, Ids(WBaWhistle, WBaAssassinCross, WBaFrostJoker, WCgArrowVulcan), Ids(PBdEncore));
            Extend(c, JobDancer, Ids(WDcHumming, WDcScream, WCgArrowVulcan), Ids(PBdEncore));
        }

        // ================================================================================================================
        // ACOLYTE line
        // ================================================================================================================
        static void AddAcolyteW17(HordeContent c)
        {
            // AL_RUWACH (24, Holy): a holy light around the caster. AL_PNEUMA (25): ranged attacks cannot reach inside.
            c.Weapons.Add(W(WAlRuwach, "al_ruwach", 24, WeaponKind.Orbiter, Targeting.Self, DamageClass.Magic, Element.Holy,
                Lv(5, i => new WeaponLevel { Count = 1 + i / 2, Range = 1.8f, Area = 0.5f, Speed = 0.5f, Tick = 0.6f, Damage = 8 + 3 * i })));
            var pneuma = W(WAlPneuma, "al_pneuma", 25, WeaponKind.GroundZone, Targeting.Self, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 0, Tick = 0.5f, Area = 1.6f + 0.1f * i, Duration = 6f + i, Cooldown = 10f - 0.5f * i }), 80);
            pneuma.Zone = ZoneFlags.BlocksShots;
            c.Weapons.Add(pneuma);
            // AL_ANGELUS (33): party VIT DEF. Mode: less damage for you and allies around.
            c.Weapons.Add(W(WAlAngelus, "al_angelus", 33, WeaponKind.Aura, Targeting.Self, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 10f, Area = 6f, Duration = 9f + i, Buffs = Ms(M(Mod.DamageTakenPct, -(6 + 2 * i)), M(Mod.MaxHpPct, 3 + i)) })));
            // AL_DECAGI (30): slows. Mode: a slowing burst where enemies crowd.
            c.Weapons.Add(W(WAlDecAgi, "al_decagi", 30, WeaponKind.AreaAtTarget, Targeting.Densest, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 0, Cooldown = 3f - 0.15f * i, Range = 7f, Area = 1.8f + 0.15f * i, Delay = 0.2f, Effects = Fxs(Fx(EffectKind.Slow, 100, 2.5f + 0.25f * i, 35 + 5 * i)) })));
            // AL_CRUCIS (32): Signum Crucis, lowers DEF around. Mode: enemies around you take more damage.
            c.Weapons.Add(W(WAlCrucis, "al_crucis", 32, WeaponKind.Mark, Targeting.Self, DamageClass.Magic, Element.Holy,
                Lv(5, i => new WeaponLevel { Area = 4f + 0.3f * i, MarkPct = 10 + 4 * i, Duration = 6f, Cooldown = 8f - 0.4f * i })));
            // AL_HOLYWATER (31): Aqua Benedicta. Mode: a holy puddle under you (hurts enemies, heals a little).
            c.Weapons.Add(W(WAlAquaBenedicta, "al_holywater", 31, WeaponKind.GroundZone, Targeting.Self, DamageClass.Magic, Element.Holy,
                Lv(5, i => new WeaponLevel { Damage = 14 + 5 * i, Heal = 5 + 2 * i, Tick = 1f, Duration = 4f + 0.5f * i, Area = 1.5f + 0.1f * i, Cooldown = 9.5f - 0.4f * i })));
            Extend(c, JobAcolyte, Ids(WAlRuwach, WAlPneuma, WAlAngelus, WAlDecAgi, WAlCrucis, WAlAquaBenedicta), None);

            // ---------------- PRIEST
            // PR_LEXAETERNA (78, max 1): the next hit is doubled. Mode: marks the strongest; its next hit counts twice (levels 2-3 own).
            c.Weapons.Add(W(WPrLexAeterna, "pr_lexaeterna", 78, WeaponKind.Mark, Targeting.Strongest, DamageClass.Magic, Element.Neutral,
                Lv(3, i => new WeaponLevel { Range = 8f, MarkPct = 100, MarkHits = 1, Duration = 8f, Cooldown = 3f - 0.6f * i })));
            // PR_TURNUNDEAD (77, Holy): may destroy an Undead at once.
            c.Weapons.Add(W(WPrTurnUndead, "pr_turnundead", 77, WeaponKind.AreaAtTarget, Targeting.Nearest, DamageClass.Magic, Element.Holy,
                Lv(5, i => new WeaponLevel { Damage = 20 + 8 * i, Area = 1f, Delay = 0.2f, Range = 7f, Cooldown = 1.8f - 0.08f * i, ExecutePct = 6 + 2 * i })));
            // PR_GLORIA (75): party LUK. Mode: more critical hits for you and allies around.
            c.Weapons.Add(W(WPrGloria, "pr_gloria", 75, WeaponKind.Aura, Targeting.Self, DamageClass.Magic, Element.Holy,
                Lv(5, i => new WeaponLevel { Cooldown = 10f, Area = 6f, Duration = 9f + i, Buffs = Ms(M(Mod.CritChance, 5 + 2 * i), M(Mod.Luk, 2 + i)) })));
            // ALL_RESURRECTION (54): raises a fallen player. Mode: a fallen ally near you gets up at once (every 90 s at level 1).
            c.Weapons.Add(W(WPrResurrection, "pr_resurrection", 54, WeaponKind.Aura, Targeting.LowestHpAlly, DamageClass.Magic, Element.Holy,
                Lv(5, i => new WeaponLevel { Range = 10f, RevivePct = 30 + 10 * i, Cooldown = 90f - 10f * i, Duration = 0.1f }), 60));
            // PR_ASPERSIO (68, Holy endow). Mode: after one of your heals the healed ally's Neutral weapons are Holy for 10 s.
            c.Passives.Add(P(PPrAspersio, "pr_aspersio", 68, 0, PLv(5, i => Ms(M(Mod.EndowElement, 3, (int)Element.Holy), M(Mod.ElementDamagePct, 4 * (i + 1), (int)Element.Holy)))));
            Extend(c, JobPriest, Ids(WPrLexAeterna, WPrTurnUndead, WPrGloria, WMgSafetyWall, WPrResurrection), Ids(PPrAspersio));

            // ---------------- MONK
            // MO_CALLSPIRITS (261): spirit spheres. Mode: spheres circling you (Finger Offensive throws one more per level).
            c.Weapons.Add(W(WMoSpiritSpheres, "mo_callspirits", 261, WeaponKind.Orbiter, Targeting.Self, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Count = 1 + i, Range = 1.4f, Area = 0.4f, Speed = 0.7f, Tick = 0.5f, Damage = 6 + 2 * i })));
            var finger = c.Weapon(WMoFingerOffensive); if (finger != null) finger.BonusCountFrom = WMoSpiritSpheres;
            // MO_BODYRELOCATION (264): Snap, the monk moves to a cell at once. Mode: a dash to the strongest enemy that strikes on arrival.
            var snap = W(WMoBodyRelocation, "mo_bodyrelocation", 264, WeaponKind.Dash, Targeting.Strongest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Range = 6f, Area = 1.2f, Damage = 20 + 8 * i, Cooldown = 5f - 0.3f * i }), 80);
            snap.DashOn = DashTrigger.Cooldown;
            c.Weapons.Add(snap);
            // MO_BALKYOUNG (1016): Ki Explosion, a pushing burst that may stun.
            c.Weapons.Add(W(WMoKiExplosion, "mo_balkyoung", 1016, WeaponKind.Nova, Targeting.Self, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 15 + 5 * i, Area = 2.2f + 0.15f * i, Cooldown = 3f - 0.15f * i, Effects = Fxs(Fx(EffectKind.Knockback, 100, 0, 3), Fx(EffectKind.Stun, 30, 1f)) })));
            // MO_CHAINCOMBO (272): after Triple Attack. Mode: a fourth, heavier blow after Triple Attack's three (the hidden WHiddenChainCombo).
            c.Passives.Add(P(PMoChainCombo, "mo_chaincombo", 272, 0, PLv(5, i => Ms(M(Mod.WeaponDamagePct, 4 * (i + 1), WMoTripleAttack)))));
            var triple = c.Weapon(WMoTripleAttack); if (triple != null) { triple.ComboPassive = PMoChainCombo; triple.ComboWeapon = WHiddenChainCombo; }
            // MO_STEELBODY (268): much more DEF, slower. Mode: much less damage taken, slower walk.
            c.Passives.Add(P(PMoSteelBody, "mo_steelbody", 268, 0, PLv(3, i => Ms(M(Mod.DamageTakenPct, -(12 + 8 * i)), M(Mod.MoveSpeedPct, -10)))));
            // MO_BLADESTOP (269): catches a blow. The trigger is h17pass's: no modifier here.
            c.Passives.Add(P(PMoBladeStop, "mo_bladestop", 269, 0, PLv(3, NoModsAt)));
            Extend(c, JobMonk, Ids(WMoSpiritSpheres, WMoBodyRelocation, WMoKiExplosion), Ids(PMoChainCombo, PMoSteelBody, PMoBladeStop));
        }

        // ================================================================================================================
        // MERCHANT line
        // ================================================================================================================
        static void AddMerchantW17(HordeContent c)
        {
            // MC_PUSHCART (39): the cart. Mode: your cart rams around you.
            c.Weapons.Add(W(WMcPushcart, "mc_pushcart", 39, WeaponKind.Orbiter, Targeting.Self, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Count = 1, Range = 1.5f, Area = 0.7f + 0.05f * i, Speed = 0.45f + 0.03f * i, Tick = 0.5f, Damage = 14 + 5 * i, Effects = Fxs(Fx(EffectKind.Knockback, 100, 0, 1.5f)) })));
            // MC_DISCOUNT (37): buys cheaper. Mode: a chest may give one more upgrade. Mammonite's evolution partner.
            c.Passives.Add(P(PMcDiscount, "mc_discount", 37, 0, PLv(3, i => Ms(M(Mod.ChestBonusPct, new[] { 34, 67, 100 }[i])))));
            // MC_VENDING (41): a shop. Mode: EXP pickups heal a little (at most once every 2 s).
            c.Passives.Add(P(PMcVending, "mc_vending", 41, 0, PLv(3, i => Ms(M(Mod.ExpPickupHealPct, 1f + 0.5f * i)))));
            // MC_IDENTIFY (40): Item Appraisal. Mode: elites and bosses drop one more chest sometimes.
            c.Passives.Add(P(PMcIdentify, "mc_identify", 40, 0, PLv(3, i => Ms(M(Mod.ChestDropPct, 25 * (i + 1), 1)))));
            Extend(c, JobMerchant, Ids(WMcPushcart), Ids(PMcDiscount, PMcVending, PMcIdentify));

            // ---------------- BLACKSMITH
            // WS_CARTBOOST (387): walk faster with the cart. Mode: a burst of speed.
            c.Weapons.Add(W(WWsCartBoost, "ws_cartboost", 387, WeaponKind.Aura, Targeting.Self, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Cooldown = 12f, Duration = 6f + 0.5f * i, Buffs = Ms(M(Mod.MoveSpeedPct, 15 + 4 * i)) }), 80));
            // BS_HILTBINDING (105): +STR, longer Adrenaline Rush / Power-Thrust. Mode: STR and longer boosts.
            c.Passives.Add(P(PBsHiltBinding, "bs_hiltbinding", 105, 0, PLv(3, i => Ms(M(Mod.Str, 2 * (i + 1)), M(Mod.DurationPct, 5 * (i + 1))))));
            // BS_WEAPONPERFECT (112): no size penalty. Mode: more damage to Small and Large enemies.
            c.Passives.Add(P(PBsWeaponPerfection, "bs_weaponperfect", 112, 0, PLv(5, i => Ms(M(Mod.DamageVsSizePct, 6 * (i + 1), (int)UnitSize.Small), M(Mod.DamageVsSizePct, 6 * (i + 1), (int)UnitSize.Large)))));
            // BS_MAXIMIZE (114): always the highest damage. Mode: more damage and harder criticals.
            c.Passives.Add(P(PBsMaximize, "bs_maximize", 114, 0, PLv(5, i => Ms(M(Mod.DamagePct, 4 * (i + 1)), M(Mod.CritDamagePct, 6 * (i + 1))))));
            // BS_FINDINGORE (106): kills may drop ore. Mode: kills may drop a small chest.
            c.Passives.Add(P(PBsOreDiscovery, "bs_findingore", 106, 0, PLv(3, i => Ms(M(Mod.ChestDropPct, 0.15f * (i + 1), 0)))));
            Extend(c, JobBlacksmith, Ids(WWsCartBoost), Ids(PBsHiltBinding, PBsWeaponPerfection, PBsMaximize, PBsOreDiscovery));

            // ---------------- ALCHEMIST
            // AM_SPHEREMINE (233): a Marine Sphere (mob 1142) that walks to enemies and explodes.
            var mine = W(WAmSphereMine, "am_spheremine", 233, WeaponKind.Summon, Targeting.Nearest, DamageClass.Ranged, Element.Fire,
                Lv(5, i => new WeaponLevel { Count = 1 + i / 2, Cooldown = 3f - 0.15f * i, Duration = 12f, Damage = 40 + 15 * i, Area = 1.8f, Range = 0.8f, Speed = 2.5f, Tick = 0.5f }));
            mine.SummonMobId = 1142; mine.SummonBursts = true;
            c.Weapons.Add(mine);
            // AM_CALLHOMUN (243): the homunculus (db/re/homunculus_db.yml classes 6001 Lif, 6002 Amistr, 6003 Filir, 6004 Vanilmirth).
            // Mode: a pet for the whole run, one of the four per run (group 1).
            var lif = W(WAmLif, "am_callhomun_lif", 243, WeaponKind.Pet, Targeting.LowestHpAlly, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { Tick = 2f - 0.1f * i, Heal = 6 + 3 * i, HealPct = 1f, Range = 6f, Speed = 4.5f, Cooldown = 1f }), 25);
            lif.Pet = PetMode.Healer; lif.SummonMobId = 6001; lif.Group = 1;
            var amistr = W(WAmAmistr, "am_callhomun_amistr", 243, WeaponKind.Pet, Targeting.Nearest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 12 + 5 * i, Tick = 1f, Range = 1.5f, Speed = 4.5f, Cooldown = 1f, Buffs = Ms(M(Mod.DamageTakenPct, -(6 + 2 * i))) }), 25);
            amistr.Pet = PetMode.Guard; amistr.SummonMobId = 6002; amistr.Group = 1;
            var filir = W(WAmFilir, "am_callhomun_filir", 243, WeaponKind.Pet, Targeting.Nearest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 12 + 5 * i, Tick = 1f - 0.05f * i, Range = 1.5f, Speed = 6f, Cooldown = 1f }), 25);
            filir.Pet = PetMode.Striker; filir.SummonMobId = 6003; filir.Group = 1;
            var vanil = W(WAmVanilmirth, "am_callhomun_vanilmirth", 243, WeaponKind.Pet, Targeting.Nearest, DamageClass.Magic, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 14 + 5 * i, Tick = 1.4f - 0.06f * i, Range = 7f, Speed = 4.5f, Cooldown = 1f }), 25);
            vanil.Pet = PetMode.Caster; vanil.SummonMobId = 6004; vanil.Group = 1;
            c.Weapons.Add(lif); c.Weapons.Add(amistr); c.Weapons.Add(filir); c.Weapons.Add(vanil);
            // AM_CP_ARMOR (236): Chemical Protection. Mode: less damage taken and some defence.
            c.Passives.Add(P(PAmChemicalProtection, "am_cp_armor", 236, 0, PLv(5, i => Ms(M(Mod.DamageTakenPct, -4 * (i + 1)), M(Mod.Defence, 1 + i)))));
            Extend(c, JobAlchemist, Ids(WAmSphereMine, WAmLif, WAmAmistr, WAmFilir, WAmVanilmirth), Ids(PAmChemicalProtection));
        }

        // ================================================================================================================
        // THIEF line
        // ================================================================================================================
        static void AddThiefW17(HordeContent c)
        {
            // TF_SPRINKLESAND (149, Earth, sc Blind): sand in the eyes. Mode: a cone in front; blinded enemies miss (deal nothing) for a while.
            c.Weapons.Add(W(WTfSandAttack, "tf_sprinklesand", 149, WeaponKind.Mark, Targeting.Nearest, DamageClass.Melee, Element.Earth,
                Lv(5, i => new WeaponLevel { Damage = 10 + 4 * i, Range = 3f, Area = 3f, Arc = 70, WeakenPct = 100, Duration = 1.5f + 0.25f * i, Cooldown = 4f - 0.2f * i })));
            // TF_BACKSLIDING (150): a jump back. Mode: when hurt you dash away (every few seconds).
            // client wave 18 (h18rules), the design's point 27: the path explodes (Damage within Area cells, Delay s after, an area every cell:
            // HordeSim.Rules.cs), then Duration s unseen, unhurt and faster (Buffs); the wait x 1.5 (wave 17: 6 - 0.3 a level, no damage)
            var slide = W(WTfBackSlide, "tf_backsliding", 150, WeaponKind.Dash, Targeting.Self, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Range = 3f + 0.25f * i, Cooldown = 9f - 0.45f * i, Damage = 30 + 10 * i, Area = 0.8f, Delay = 0.6f, Duration = 1.5f, Buffs = Ms(M(Mod.MoveSpeedPct, 50f)) }), 80);
            slide.DashOn = DashTrigger.WhenHit;
            c.Weapons.Add(slide);
            // TF_HIDING (51): The trigger is h17pass's: no modifier here.
            c.Passives.Add(P(PTfHiding, "tf_hiding", 51, 0, PLv(3, NoModsAt)));
            // TF_STEAL (50): steals an item. Mode: a hit may drop a small EXP pickup (once per enemy).
            c.Passives.Add(P(PTfSteal, "tf_steal", 50, 0, PLv(5, i => Ms(M(Mod.StealPct, 3 * (i + 1))))));
            // TF_DETOXIFY (53): cures poison. Mode: less damage from Poison enemies. Envenom's evolution partner.
            c.Passives.Add(P(PTfDetoxify, "tf_detoxify", 53, 0, PLv(3, i => Ms(M(Mod.DamageTakenVsElementPct, -new[] { 40, 70, 100 }[i], (int)Element.Poison)))));
            Extend(c, JobThief, Ids(WTfSandAttack, WTfBackSlide), Ids(PTfHiding, PTfSteal, PTfDetoxify));

            // ---------------- ASSASSIN
            // AS_VENOMKNIFE (1004, Poison): a thrown poisoned knife.
            c.Weapons.Add(W(WAsVenomKnife, "as_venomknife", 1004, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Ranged, Element.Poison,
                Lv(5, i => new WeaponLevel { Damage = 15 + 5 * i, Cooldown = 1.3f - 0.05f * i, Speed = 11f, Range = 8f, Effects = Fxs(Fx(EffectKind.Dot, 60 + 8 * i, 4f, 3 + 1.5f * i)) })));
            // AS_ENCHANTPOISON (138): Poison endow. Mode: every hit may poison.
            c.Passives.Add(P(PAsEnchantPoison, "as_enchantpoison", 138, 0, PLv(5, i => Ms(M(Mod.PoisonOnHitPct, 6 * (i + 1))))));
            // AS_CLOAKING (135): The trigger is h17pass's: no modifier here.
            c.Passives.Add(P(PAsCloaking, "as_cloaking", 135, 0, PLv(3, NoModsAt)));
            // AS_LEFT (133): left-hand mastery. Mode: every melee hit strikes again, weaker.
            c.Passives.Add(P(PAsLefthand, "as_left", 133, 0, PLv(5, i => Ms(M(Mod.OffhandPct, 20 * (i + 1))))));
            // AS_SONICACCEL (1003): Sonic Blow faster and stronger.
            c.Passives.Add(P(PAsSonicAccel, "as_sonicaccel", 1003, 0, PLv(3, i => Ms(M(Mod.WeaponCooldownPct, -10 * (i + 1), WAsSonicBlow), M(Mod.WeaponDamagePct, 15 * (i + 1), WAsSonicBlow)))));
            Extend(c, JobAssassin, Ids(WAsVenomKnife), Ids(PAsEnchantPoison, PAsCloaking, PAsLefthand, PAsSonicAccel));

            // ---------------- ROGUE
            // RG_INTIMIDATE (219): Snatch, both go somewhere else. Mode: throws the strongest enemy near you far away.
            c.Weapons.Add(W(WRgIntimidate, "rg_intimidate", 219, WeaponKind.MeleeArc, Targeting.Strongest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 25 + 9 * i, Cooldown = 3f - 0.15f * i, Range = 2f, Arc = 50, Effects = Fxs(Fx(EffectKind.Knockback, 100, 0, 6 + i)) })));
            // RG_STRIPWEAPON (215) / RG_STRIPARMOR (217): the target's ATK / DEF down. Mode: the strongest enemy deals less / takes more.
            c.Weapons.Add(W(WRgStripWeapon, "rg_stripweapon", 215, WeaponKind.Mark, Targeting.Strongest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Range = 4f, WeakenPct = 20 + 5 * i, Duration = 8f + i, Cooldown = 6f - 0.3f * i })));
            c.Weapons.Add(W(WRgStripArmor, "rg_striparmor", 217, WeaponKind.Mark, Targeting.Strongest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Range = 4f, MarkPct = 15 + 5 * i, Duration = 8f + i, Cooldown = 6f - 0.3f * i })));
            // RG_CLOSECONFINE (1005): both cannot move. Mode: holds the strongest enemy near you (not a boss) and it takes a little more.
            c.Weapons.Add(W(WRgCloseConfine, "rg_closeconfine", 1005, WeaponKind.Mark, Targeting.Strongest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Range = 3f, MarkPct = 10, Duration = 2f + 0.5f * i, Cooldown = 6f - 0.3f * i, Effects = Fxs(Fx(EffectKind.Stun, 100, 2f + 0.5f * i)) })));
            // RG_PLAGIARISM (225): copies a skill used on the rogue. Mode: the last enemy shot that hit you is fired back for 10 s.
            c.Passives.Add(P(PRgPlagiarism, "rg_plagiarism", 225, 0, PLv(5, i => Ms(M(Mod.CopyShotPct, 20 * (i + 1))))));
            // RG_GANGSTER (223): The trigger is h17pass's: no modifier here.
            c.Passives.Add(P(PRgGangster, "rg_gangster", 223, 0, PLv(3, NoModsAt)));
            // The Rogue's tree holds SM_SWORD 10 (skill_tree.yml "Rogue"): the Swordman's def.
            Extend(c, JobRogue, Ids(WRgIntimidate, WRgStripWeapon, WRgStripArmor, WRgCloseConfine), Ids(PRgPlagiarism, PRgGangster, PSmSword));
        }

        // ================================================================================================================
        // HIDDEN weapons: what the engine strikes with for a passive (never a card)
        // ================================================================================================================
        static void AddHiddenW17(HordeContent c)
        {
            // The falcon of Falcon Mastery: a pet that strikes small areas (Blitz Beat's look), at the passive's level.
            var falcon = W(WHiddenFalcon, "ht_falcon_pet", 129, WeaponKind.Pet, Targeting.Nearest, DamageClass.Ranged, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 14 + 6 * i, Tick = 2f, Range = 1.2f, Area = 1f, Speed = 9f, Cooldown = 1f }), 0);
            falcon.Pet = PetMode.Striker; falcon.Hidden = true;
            c.Weapons.Add(falcon);
            // Chain Combo's blow after Triple Attack (MO_CHAINCOMBO's look), at the passive's level.
            var combo = W(WHiddenChainCombo, "mo_chaincombo_blow", 272, WeaponKind.MeleeArc, Targeting.Nearest, DamageClass.Melee, Element.Neutral,
                Lv(5, i => new WeaponLevel { Damage = 30 + 12 * i, Range = 2.2f, Arc = 100, Cooldown = 1f }), 0);
            combo.Hidden = true;
            c.Weapons.Add(combo);
            // Plagiarism's copied shot (RG_PLAGIARISM's look): its damage is the copied shot's (the engine sets it).
            var copy = W(WHiddenCopiedShot, "rg_plagiarism_shot", 225, WeaponKind.Bolt, Targeting.Nearest, DamageClass.Ranged, Element.Neutral,
                Lv(1, i => new WeaponLevel { Damage = 10, Cooldown = 1.5f, Speed = 9f, Range = 8f }), 0);
            copy.Hidden = true;
            c.Weapons.Add(copy);
        }

        // ================================================================================================================
        // C. EVOLUTIONS: one per job line, on the first job's signature at its top level + a named card of that first job
        // (so every job of the line can make it). A big chest (3+ upgrades: Ork Hero's, Osiris's) turns it.
        // ================================================================================================================
        static void AddEvolutionsW17(HordeContent c)
        {
            // Swordman: Bash 5 + Fatal Blow -> Bowling Bash (KN_BOWLINGBASH 62's look): a heavy swing whose every hit bursts on its
            // neighbours (the knockback chain).
            Evolve(c, WEvoBash, WSmBash, 62, "", 0, PSmFatalBlow, top => { top.Damage = 95; top.Arc = 150; top.Range = 2.4f; top.Cooldown = 1.0f; top.Splash = 1.6f; top.SplashPct = 60; top.Effects = Fxs(Fx(EffectKind.Knockback, 100, 0, 2.5f), Fx(EffectKind.Stun, 30, 1f)); });
            // Mage: Fire Bolt 5 + Sight -> Meteor Bolt (Loki's own name; WZ_METEOR 83's look): bolts that burst into fire.
            Evolve(c, WEvoFireBolt, WMgFireBolt, 83, "Meteor Bolt", WMgSight, 0, top => { top.Damage = 28; top.Count = 5; top.Area = 1.5f; top.Cooldown = 1.4f; top.Speed = 9f; top.Range = 9f; });
            // Archer: Double Strafe 5 + Vulture's Eye -> Sharp Shooting (SN_SHARPSHOOTING 382): arrows that go through a whole line.
            Evolve(c, WEvoDouble, WAcDouble, 382, "", 0, PAcVulture, top => { top.Damage = 48; top.Count = 2; top.Pierce = 99; top.Falloff = 100; top.Cooldown = 0.85f; top.Speed = 13f; top.Range = 11f; });
            // Acolyte: Holy Light 5 + Ruwach -> Judex (AB_JUDEX 2038's look): holy light that bursts on its target.
            Evolve(c, WEvoHolyLight, WAlHolyLight, 2038, "", WAlRuwach, 0, top => { top.Damage = 70; top.Count = 2; top.Burst = 0.15f; top.Area = 1.6f; top.Pierce = 0; top.Cooldown = 0.95f; });
            // Merchant: Mammonite 5 + Discount -> Cart Termination (WS_CARTTERMINATION 485): a huge cart blow that stuns.
            Evolve(c, WEvoMammonite, WMcMammonite, 485, "", 0, PMcDiscount, top => { top.Damage = 150; top.Arc = 140; top.Range = 2.4f; top.Cooldown = 1.1f; top.Effects = Fxs(Fx(EffectKind.Stun, 40, 1.2f), Fx(EffectKind.Knockback, 100, 0, 2)); });
            // Thief: Envenom 5 + Detoxify -> Enchant Deadly Poison (ASC_EDP 378's look): a stab whose poison is deadly and spreads.
            Evolve(c, WEvoEnvenom, WTfPoison, 378, "", 0, PTfDetoxify, top => { top.Damage = 60; top.Arc = 150; top.Cooldown = 0.6f; top.Splash = 1.4f; top.SplashPct = 40; top.Effects = Fxs(Fx(EffectKind.Dot, 100, 5f, 24)); });
        }

        /// <summary>An evolved form of <paramref name="baseId"/>: the base's levels, the top one changed by <paramref name="top"/>. Its name: the
        /// install's name of its skill (<paramref name="skillId"/>), or Loki's own <paramref name="name"/> when no RO skill has it.</summary>
        static void Evolve(HordeContent c, int id, int baseId, int skillId, string name, int needsWeapon, int needsPassive, Action<WeaponLevel> top)
        {
            var b = c.Weapon(baseId);
            var e = CopyDef(b, id, b.Key + "_evo", 0);
            e.SkillId = skillId; e.EvolveOf = baseId; e.NeedsWeapon = needsWeapon; e.NeedsPassive = needsPassive; e.EvolvedName = name;
            top(e.Levels[e.Levels.Length - 1]);
            c.Weapons.Add(e);
        }

        static WeaponLevel CopyLevel(WeaponLevel l)
        {
            var n = l.Clone();
            if (l.Effects != null) { n.Effects = new EffectDef[l.Effects.Length]; for (int i = 0; i < l.Effects.Length; i++) n.Effects[i] = l.Effects[i] == null ? null : new EffectDef(l.Effects[i].Kind, l.Effects[i].Chance, l.Effects[i].Duration, l.Effects[i].Power, l.Effects[i].Tick); }
            if (l.Buffs != null) { n.Buffs = new ModDef[l.Buffs.Length]; for (int i = 0; i < l.Buffs.Length; i++) n.Buffs[i] = l.Buffs[i] == null ? null : new ModDef(l.Buffs[i].Kind, l.Buffs[i].Value, l.Buffs[i].Arg); }
            return n;
        }

        static WeaponDef CopyDef(WeaponDef b, int id, string key, int weight)
        {
            var levels = new WeaponLevel[b.Levels.Length];
            for (int i = 0; i < levels.Length; i++) levels[i] = CopyLevel(b.Levels[i]);
            return new WeaponDef
            {
                Id = id, Key = key, SkillId = b.SkillId, ItemId = b.ItemId, Kind = b.Kind, Target = b.Target, Class = b.Class, Element = b.Element, Zone = b.Zone,
                Homing = b.Homing, SummonMobId = b.SummonMobId, SummonHp = b.SummonHp, Weight = weight, Levels = levels,
                DashOn = b.DashOn, Parry = b.Parry, Pet = b.Pet, HitsBySize = b.HitsBySize, RandomOf = b.RandomOf, CastOnHit = b.CastOnHit,
                BonusCountFrom = b.BonusCountFrom, ComboPassive = b.ComboPassive, ComboWeapon = b.ComboWeapon, SummonBursts = b.SummonBursts, Group = b.Group,
            };
        }

        // ================================================================================================================
        // B. BRANCHES: every weapon with 3+ levels (old and new; not hidden, not evolved) gets two, made from its own levels 3+ by a
        // rule of its kind. Bolts follow the proposal's example (Fire Bolt: Barrage = 3 more small bolts, Lance = one big bolt that goes
        // through); every other kind has its own two (one wider / more, one stronger). The numbers are Loki's own.
        // ================================================================================================================
        static void AddBranchesW17(HordeContent c)
        {
            var bases = new List<WeaponDef>();
            foreach (var w in c.Weapons) if (!w.Hidden && w.EvolveOf == 0 && w.BranchOf == 0 && w.Levels.Length >= 3) bases.Add(w);
            foreach (var w in bases)
            {
                var b1 = CopyDef(w, 1000 + w.Id, w.Key + "_b1", 0); b1.BranchOf = w.Id; b1.Branch = 1;
                var b2 = CopyDef(w, 2000 + w.Id, w.Key + "_b2", 0); b2.BranchOf = w.Id; b2.Branch = 2;
                b1.BranchName = BranchName(w, 1); b2.BranchName = BranchName(w, 2);
                for (int i = 2; i < w.Levels.Length; i++) { BranchLevel(w, b1.Levels[i], 1); BranchLevel(w, b2.Levels[i], 2); }
                w.Branches = new[] { b1.Id, b2.Id };
                c.Weapons.Add(b1); c.Weapons.Add(b2);
            }
        }

        /// <summary>The branch's name, a Lang key of the client (en = the key).</summary>
        public static string BranchName(WeaponDef w, int branch)
        {
            bool one = branch == 1;
            switch (w.Kind)
            {
                case WeaponKind.Bolt: return one ? "Barrage" : "Lance";
                case WeaponKind.MeleeArc: return one ? "Sweep" : "Crush";
                case WeaponKind.Nova: return one ? "Wave" : "Burst";
                case WeaponKind.AreaAtTarget: return one ? "Rain" : "Impact";
                case WeaponKind.GroundZone: return one ? "Spread" : "Strong";
                case WeaponKind.Orbiter: return one ? "Swarm" : "Heavy";
                case WeaponKind.Chain: return one ? "Fork" : "Strong";
                case WeaponKind.Aura: return one ? "Wide" : "Strong";
                case WeaponKind.Summon: return one ? "Pack" : "Strong";
                case WeaponKind.Trap: return one ? "Field" : "Big";
                case WeaponKind.Dash: return one ? "Long" : "Strong";
                case WeaponKind.Mark: return one ? "Wide" : "Deep";
                case WeaponKind.Parry: return one ? "Many" : "Swift";
                case WeaponKind.Pet: return one ? "Strong" : "Swift";
                default: return one ? "Wide" : "Strong";
            }
        }

        /// <summary>A branch's float, from a product computed in double (the same bits on Unity's Mono and .NET's RyuJIT; no rounding to
        /// two decimals: a rounding at a tie turned an ulp into 0.01 and the host's and the client's content stamps differed).</summary>
        static float R2(double v) => (float)v;
        static int Ri(double v) => (int)Math.Round(v);

        /// <summary>One level (3+) of a branch: the base's level changed by the kind's rule. Balance (Loki's own, measured with the
        /// balance runner, design notes): a "more" branch spreads about 120 % of the level's damage over more shots / orbs /
        /// helpers / targets; a "stronger" branch is about +30 % on one target; neither is more than a good level card's step.</summary>
        static void BranchLevel(WeaponDef w, WeaponLevel l, int branch)
        {
            bool one = branch == 1;
            void Buffs(double k) { if (l.Buffs != null) foreach (var m in l.Buffs) if (m != null) m.Value = R2(m.Value * k); }
            void FxMore(double k) { if (l.Effects != null) foreach (var e in l.Effects) if (e != null) { if (e.Kind == EffectKind.Knockback || e.Kind == EffectKind.Slow) e.Power = R2(e.Power * k); else { e.Chance = Math.Min(100f, R2(e.Chance * k)); e.Duration = R2(e.Duration * k); } } }
            // more of them, each weaker: about 120 % of the level's damage in all
            void Spread(int more) { int n = Math.Max(1, l.Count); int m = n + more; l.Damage = Math.Max(1, Ri(l.Damage * 1.2 * n / m)); l.Count = m; }
            switch (w.Kind)
            {
                case WeaponKind.Bolt:
                    if (w.RandomOf != null && w.RandomOf.Length > 0)
                    {
                        // Auto Spell: one casts more often (its shortest gap), the other on more hits; Abracadabra: one casts more often,
                        // the other casts its spells two levels higher (Count - 1 = levels added)
                        if (one) l.Cooldown = R2(l.Cooldown * 0.7);
                        else if (w.CastOnHit) l.ProcPct = R2(l.ProcPct * 1.5);
                        else l.Count += 2;
                        break;
                    }
                    if (one) { Spread(3); l.Pierce = Math.Max(0, l.Pierce - 1); } // the proposal's Barrage: 3 more small bolts (each goes through one fewer)
                    else { l.Damage = Ri(l.Damage * Math.Max(1, l.Count) * 1.1); l.Count = 1; l.Pierce += 4; l.Speed = R2(l.Speed * 1.25); } // Lance: one big bolt that goes through
                    break;
                case WeaponKind.MeleeArc:
                    if (one) { l.Arc = Math.Min(360f, l.Arc + 80f); l.Range = R2(l.Range + 0.5); l.Damage = Ri(l.Damage * 0.85); }
                    else { l.Damage = Ri(l.Damage * 1.4); l.Cooldown = R2(l.Cooldown * 1.1); }
                    break;
                case WeaponKind.Nova:
                    if (one) { l.Area = R2(l.Area * 1.3); l.Damage = Ri(l.Damage * 0.85); }
                    else l.Damage = Ri(l.Damage * 1.35);
                    break;
                case WeaponKind.AreaAtTarget:
                    if (one) { if (l.Damage > 0) Spread(1); else { l.Count += 1; l.Cooldown = R2(l.Cooldown * 0.85); } }
                    else { l.Damage = Ri(l.Damage * 1.35); l.Area = R2(l.Area * 1.15); FxMore(1.25); }
                    break;
                case WeaponKind.GroundZone:
                    if (one) { l.Area = R2(l.Area * 1.25); l.Duration = R2(l.Duration * 1.2); }
                    else { l.Damage = Ri(l.Damage * 1.35); l.Heal = Ri(l.Heal * 1.3); l.HealPct = R2(l.HealPct * 1.3); Buffs(1.3); FxMore(1.25); if (l.Damage == 0 && l.Heal == 0 && l.HealPct <= 0 && (l.Buffs == null || l.Buffs.Length == 0) && l.Effects == null) l.Cooldown = R2(l.Cooldown * 0.8); }
                    break;
                case WeaponKind.Orbiter:
                    if (one) Spread(Math.Max(1, l.Count / 2));
                    else { l.Damage = Ri(l.Damage * 1.35); l.Area = R2(l.Area * 1.25); l.Speed = R2(l.Speed * 0.9); }
                    break;
                case WeaponKind.Chain:
                    if (one) { l.Count += 2; l.Falloff = Math.Min(100f, l.Falloff + 5f); }
                    else { l.Damage = Ri(l.Damage * 1.35); l.Area = R2(l.Area + 1f); }
                    break;
                case WeaponKind.Aura:
                    if (one) { if (w.Target == Targeting.LowestHpAlly) l.Range = R2(Math.Max(l.Range, 1f) * 1.5); else l.Area = R2(Math.Max(l.Area, 1f) * 1.4); if (l.Buffs != null && l.Buffs.Length > 0) l.Duration = R2(l.Duration * 1.2); if (l.RevivePct > 0) l.Cooldown = R2(l.Cooldown * 0.8); }
                    else { l.Damage = Ri(l.Damage * 1.35); l.Heal = Ri(l.Heal * 1.3); l.HealPct = R2(l.HealPct * 1.3); Buffs(1.3); FxMore(1.25); l.RevivePct = Math.Min(100f, R2(l.RevivePct * 1.5)); }
                    break;
                case WeaponKind.Summon:
                    if (one) Spread(1);
                    else { l.Damage = Ri(l.Damage * 1.25); l.Tick = R2(l.Tick * 0.9); }
                    break;
                case WeaponKind.Trap:
                    if (one) { l.Count += 2; l.Cooldown = R2(l.Cooldown * 0.85); l.Damage = Ri(l.Damage * 0.9); }
                    else { l.Damage = Ri(l.Damage * 1.3); l.Area = R2(l.Area * 1.15); FxMore(1.25); }
                    break;
                case WeaponKind.Dash:
                    if (one) { l.Range = R2(l.Range * 1.4); l.Area = R2(Math.Max(l.Area, 0.5) * 1.2); if (l.Damage == 0) l.Cooldown = R2(l.Cooldown * 0.85); }
                    else { l.Damage = Ri(l.Damage * 1.35); FxMore(1.25); if (l.Damage == 0) l.Cooldown = R2(l.Cooldown * 0.7); }
                    break;
                case WeaponKind.Mark:
                    if (one) l.Area = R2(l.Area > 0 ? l.Area * 1.4 : 2f);
                    else { l.MarkPct = R2(l.MarkPct * 1.3); l.WeakenPct = Math.Min(100f, R2(l.WeakenPct * 1.3)); l.Duration = R2(l.Duration * 1.2); FxMore(1.25); if (l.MarkHits > 0) l.MarkHits += 1; }
                    break;
                case WeaponKind.Parry:
                    if (one) l.Count += 1;
                    else l.Cooldown = R2(l.Cooldown * 0.75);
                    break;
                case WeaponKind.Pet:
                    if (one) { l.Damage = Ri(l.Damage * 1.3); l.Heal = Ri(l.Heal * 1.3); l.HealPct = R2(l.HealPct * 1.3); Buffs(1.3); }
                    else l.Tick = R2(l.Tick * 0.8);
                    break;
            }
        }
    }
}
