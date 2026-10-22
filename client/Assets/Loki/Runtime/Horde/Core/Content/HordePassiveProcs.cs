#nullable disable
using System.Collections.Generic;

namespace Loki.Horde.Content
{
    // ====================================================================================================================
    // Client wave 17 (h17pass), design 2 / point 22 of design notes: EVERY passive's body and trigger,
    // by passive id (the engine's rules: ../HordeSim.Passives.cs; the drawing: Horde/Client/HordePassivesDraw.cs). Applied at the
    // END of HordeTables.Build() (one marked line there): a passive id that is not in the tables (yet) is skipped, so h17cards'
    // new passives (design notes, ids fixed by h17cards) take their bodies and triggers when the branches
    // are merged. EVERY NUMBER HERE IS LOKI'S OWN DESIGN; the families and their numbers are the proposal's table where it gives
    // them ("3 dodges", "every 10th hit", "8 s", "2 s doubles it", "every 5 crits", "3 times", "3+ enemies", "4 s", "3 s ... +50 %",
    // "every 15 s", "+10 %", "3 s ... +60 %"), and h17cards' suggested numbers for the ten passives whose whole effect is a trigger.
    // The RO things named are rAthena's ids (db/re/item_db_*.yml, db/re/mob_db.yml): they are only the client's looks.
    // ====================================================================================================================
    public static partial class HordeTables
    {
        // ---- the passives' own pickups (PickupDef ids 240-249 are h17pass's)
        const int PpPickClover = 240, PpPickMote = 241, PpPickArrows = 242;
        // ---- rAthena item ids (the pickups' looks): Clover 705, Red_Herb 507, Arrow_Container 12004 (the Quiver); bodies: Pupa_Card 4003,
        // Soft_Feather 7063, Poison_Bottle 678
        const int PpItemClover = 705, PpItemRedHerb = 507, PpItemQuiver = 12004, PpItemPupaCard = 4003, PpItemFeather = 7063, PpItemPoison = 678;
        // ---- rAthena mob ids (the orbiting bodies): ANDRE 1095, MANTIS 1139, SAVAGE_BABE 1167
        const int PpMobAndre = 1095, PpMobMantis = 1139, PpMobSavageBabe = 1167;
        // ---- weapon ids a trigger names (HordeJobs.cs): MO_TRIPLEATTACK
        const int PpWeaponTripleAttack = 180;

        /// <summary>Client wave 17 (h17pass): every passive's trigger and body (by id; ids not in the tables are skipped).</summary>
        static void AddPassiveProcs(HordeContent c)
        {
            // the pickups some triggers drop (a clover heals Value % of the taker's max HP; a heal mote the same; an arrow bundle gives
            // Value more shots for a while)
            c.Pickups.Add(new PickupDef { Id = PpPickClover, Key = "proc_clover", Kind = PickupKind.Heal, ItemId = PpItemClover, Value = 3 });
            c.Pickups.Add(new PickupDef { Id = PpPickMote, Key = "proc_heal_mote", Kind = PickupKind.Heal, ItemId = PpItemRedHerb, Value = 2 });
            c.Pickups.Add(new PickupDef { Id = PpPickArrows, Key = "proc_arrow_bundle", Kind = PickupKind.Passive, ItemId = PpItemQuiver, Value = 1 });
            foreach (var kv in Procs())
            {
                var p = c.Passive(kv.Key);
                if (p != null) p.Proc = kv.Value;
            }
        }

        // ---------------------------------------------------------------- the families (one maker each: the proposal's numbers)

        /// <summary>Poring / Chonchon / Whisper / Improve Dodge: a dodged hit leaves an after-image; 3 dodges in a row: a free counter
        /// puff (20 damage, +8 a level, within 2 cells, pushing 1 cell).</summary>
        static PassiveProcDef PpEcho() => new PassiveProcDef { Kind = ProcKind.DodgeCounter, Body = ProcBody.AfterImage, Count = 3, Damage = 20, DamagePerLevel = 8, Area = 2f };
        /// <summary>Andre / Mantis (and the hit cards): a small monster circles you; every 10th hit is a heavy hit, +80 % (+20 % a level).</summary>
        static PassiveProcDef PpAnt(int mob, int item = 0) => new PassiveProcDef { Kind = ProcKind.HitCount, Body = ProcBody.Orbiter, Count = 10, Power = 80, PowerPerLevel = 20, BodyMob = mob, BodyItem = item };
        /// <summary>Pupa: a shell (under the HP bar too) takes the first hit after 8 s (0.5 s sooner a level).</summary>
        static PassiveProcDef PpShell() => new PassiveProcDef { Kind = ProcKind.Shell, Body = ProcBody.Shell, Seconds = 8f, SecondsPerLevel = -0.5f, BodyItem = PpItemPupaCard };
        /// <summary>Zombie / HP Recovery / SP Recovery: green motes rise; standing still 2 s doubles the passive (a ring fills).</summary>
        static PassiveProcDef PpStill() => new PassiveProcDef { Kind = ProcKind.StillBoost, Body = ProcBody.Motes, Seconds = 2f, Mul = 2f };
        /// <summary>Lunatic / Double Attack / Soldier Skeleton: a critical hit pops a star; every 5 criticals drop a lucky clover (heals 3 %, +0.5 % a level).</summary>
        static PassiveProcDef PpLucky() => new PassiveProcDef { Kind = ProcKind.CritCount, Body = ProcBody.Stars, Count = 5, Power = 3, PowerPerLevel = 0.5f, PickupId = PpPickClover };
        /// <summary>Archer Skeleton / Vulture's Eye / Owl's Eye (and the arrows, Drops, Detect): shots leave a trail; 3 hits of your shots
        /// on one enemy mark it for 4 s: it takes +15 % (+3 % a level) from everyone.</summary>
        static PassiveProcDef PpMark() => new PassiveProcDef { Kind = ProcKind.Mark, Body = ProcBody.Trail, Count = 3, Seconds = 4f, Power = 15, PowerPerLevel = 3 };
        /// <summary>The melee masteries: the swing is drawn bigger and brighter; a swing that hits 3+ enemies gives 20 % (+5 % a level) of its wait back.</summary>
        static PassiveProcDef PpSweep() => new PassiveProcDef { Kind = ProcKind.FullSwing, Body = ProcBody.Arc, Count = 3, Power = 20, PowerPerLevel = 5 };
        /// <summary>Energy Coat / Divine Protection / Skin Temper / Faith (and the "less damage" cards): a blue coat soaks 5 % of max HP
        /// (+1.5 % a level; a runner's coats together at most 25 %: HordeSim.CoatCapPct); it fills again after 4 s without damage.</summary>
        static PassiveProcDef PpCoat() => new PassiveProcDef { Kind = ProcKind.Coat, Body = ProcBody.Coat, Seconds = 4f, Power = 5, PowerPerLevel = 1.5f };
        /// <summary>Riding / Free Cast / Tunnel Drive / Cavalier Mastery: a dust trail (a Peco Peco for Riding); 3 s of walking charge a
        /// dash: the next hit +50 % (+10 % a level).</summary>
        static PassiveProcDef PpStride(bool peco = false) => new PassiveProcDef { Kind = ProcKind.Stride, Body = peco ? ProcBody.Peco : ProcBody.Dust, Seconds = 3f, Power = 50, PowerPerLevel = 10 };
        /// <summary>Overcharge / Gank / Greed / Enlarge Weight Limit (and the Merchant's new ones, Steal, Ore Discovery): the pickup reach is
        /// drawn; EXP gems glow; every 15 s (1 s less a level) a magnet pulse pulls every gem within 6 cells (+1 a level).</summary>
        static PassiveProcDef PpMagnet() => new PassiveProcDef { Kind = ProcKind.MagnetPulse, Body = ProcBody.PickupRing, Seconds = 15f, SecondsPerLevel = -1f, Area = 6f, AreaPerLevel = 1f };
        /// <summary>Demon Bane / Beast Bane (and the "vs" cards): enemies of the bane show an icon; killing one drops a heal mote (2 %)
        /// 30 % of the time (+5 % a level), at most once every 3 s.</summary>
        static PassiveProcDef PpBane(int races = 0, int elements = 0, int sizes = 0, int ranks = 0) =>
            new PassiveProcDef { Kind = ProcKind.Bane, Body = ProcBody.RaceIcon, Chance = 30, ChancePerLevel = 5, Cooldown = 3f, PickupId = PpPickMote, RaceMask = races, ElementMask = elements, SizeMask = sizes, RankMask = ranks };
        /// <summary>Advanced Book / Musical / Dancing Lesson / Potion Research (and Encore, Aspersio, Land Protector): your zones and
        /// songs pulse; standing in your own zone (or with your own aura's boost) +10 % damage.</summary>
        static PassiveProcDef PpZone() => new PassiveProcDef { Kind = ProcKind.OwnZone, Body = ProcBody.ZonePulse, Power = 10 };
        /// <summary>Weapon Research (and the Blacksmith's new ones): the hand glows when ready; a hit after 3 s without hitting does +60 % (+10 % a level).</summary>
        static PassiveProcDef PpResearch() => new PassiveProcDef { Kind = ProcKind.IdleCharge, Body = ProcBody.Glow, Seconds = 3f, Power = 60, PowerPerLevel = 10 };
        /// <summary>A timer charge of the magic weapons' next use (Mystic Focus, Elder Willow, the Endows).</summary>
        static PassiveProcDef PpFocus(float seconds, float secondsPerLevel, float power, float powerPerLevel, int classMask) =>
            new PassiveProcDef { Kind = ProcKind.TimerCharge, Body = ProcBody.Glow, Seconds = seconds, SecondsPerLevel = secondsPerLevel, Power = power, PowerPerLevel = powerPerLevel, ClassMask = classMask };

        static int PpBit(int b) => 1 << b;

        /// <summary>The passive ids this file gives a trigger (tests: h17cards' new ids must all be here).</summary>
        public static IEnumerable<int> PassiveProcIds() { foreach (var kv in Procs()) yield return kv.Key; }
        const int PpMagic = 1 << (int)DamageClass.Magic;

        /// <summary>Every passive id with its trigger (existing passives first, then h17cards' new ones by their fixed ids).</summary>
        static IEnumerable<KeyValuePair<int, PassiveProcDef>> Procs()
        {
            KeyValuePair<int, PassiveProcDef> Pp(int id, PassiveProcDef d) => new KeyValuePair<int, PassiveProcDef>(id, d);
            // ---- the passives of client wave 14's tables (HordeJobs.cs), as the proposal's table groups them
            yield return Pp(PPoringCard, PpEcho()); yield return Pp(PChonchonCard, PpEcho()); yield return Pp(PTfMiss, PpEcho());
            yield return Pp(PAndreCard, PpAnt(PpMobAndre));
            yield return Pp(PPupaCard, PpShell());
            yield return Pp(PZombieCard, PpStill()); yield return Pp(PSmRecovery, PpStill()); yield return Pp(PMgSRecovery, PpStill());
            yield return Pp(PLunaticCard, PpLucky()); yield return Pp(PTfDouble, PpLucky());
            yield return Pp(PArcherSkeletonCard, PpMark()); yield return Pp(PAcVulture, PpMark()); yield return Pp(PAcOwl, PpMark());
            yield return Pp(PSmSword, PpSweep()); yield return Pp(PKnSpearMastery, PpSweep()); yield return Pp(PAmAxeMastery, PpSweep()); yield return Pp(PPrMaceMastery, PpSweep());
            yield return Pp(PMoIronHand, PpSweep()); yield return Pp(PAsRight, PpSweep()); yield return Pp(PAsKatar, PpSweep());
            yield return Pp(PMgEnergyCoat, PpCoat()); yield return Pp(PAlDp, PpCoat()); yield return Pp(PBsSkinTemper, PpCoat()); yield return Pp(PCrTrust, PpCoat());
            yield return Pp(PKnRiding, PpStride(true)); yield return Pp(PSaFreeCast, PpStride()); yield return Pp(PRgTunnelDrive, PpStride());
            yield return Pp(PMcOvercharge, PpMagnet()); yield return Pp(PRgSnatcher, PpMagnet()); yield return Pp(PBsGreed, PpMagnet()); yield return Pp(PMcIncCarry, PpMagnet());
            // Demon Bane: Undead-element or Demon-race (battle.cpp:2291-2294, as its modifiers); Beast Bane: Brute and Insect (its modifiers)
            yield return Pp(PAlDemonBane, PpBane(races: PpBit((int)Race.Demon), elements: PpBit((int)Element.Undead)));
            yield return Pp(PHtBeastBane, PpBane(races: PpBit((int)Race.Brute) | PpBit((int)Race.Insect)));
            yield return Pp(PSaAdvancedBook, PpZone()); yield return Pp(PBaMusicalLesson, PpZone()); yield return Pp(PDcDancingLesson, PpZone()); yield return Pp(PAmLearningPotion, PpZone());
            yield return Pp(PBsWeaponResearch, PpResearch());
            // Dodge (Monk): a dodged hit lets the next Triple Attack come at once (an after-image too)
            yield return Pp(PMoDodge, new PassiveProcDef { Kind = ProcKind.DodgeReady, Body = ProcBody.AfterImage, Arg = PpWeaponTripleAttack });

            // ---- h17cards' new passives (design notes; ids fixed there): applied when they are in the tables
            // job-free monster cards 300-309
            yield return Pp(300, PpBane(races: PpBit((int)Race.Demihuman)));                                  // Hydra: vs Demihuman
            yield return Pp(301, PpBane(sizes: PpBit((int)UnitSize.Medium)));                                 // Skeleton Worker: vs Medium size
            yield return Pp(302, PpCoat()); yield return Pp(303, PpCoat());                                    // Thara Frog, Raydric: less damage
            yield return Pp(304, PpAnt(PpMobMantis));                                                       // Mantis: STR
            yield return Pp(305, PpEcho());                                                                 // Whisper: dodge
            yield return Pp(306, PpLucky());                                                                // Soldier Skeleton: critical
            yield return Pp(307, new PassiveProcDef { Kind = ProcKind.HitCount, Body = ProcBody.Orbiter, Count = 10, Power = 40, PowerPerLevel = 10, HitEffect = EffectKind.Stun, HitEffectSeconds = 1f, BodyMob = PpMobSavageBabe }); // Savage Babe: the 10th hit stuns
            yield return Pp(308, PpFocus(10f, 0f, 40, 10, PpMagic));                                        // Elder Willow: INT
            yield return Pp(309, PpMark());                                                                 // Drops: DEX
            // Swordman 12-15
            yield return Pp(12, PpSweep());                                                                 // Two-Hand Sword Mastery
            yield return Pp(13, new PassiveProcDef { Kind = ProcKind.HitCount, Body = ProcBody.Orbiter, Count = 10, Power = 40, PowerPerLevel = 10, HitEffect = EffectKind.Stun, HitEffectSeconds = 1f }); // Fatal Blow
            // Auto Berserk (h17cards: under 25 % HP, +20 / 30 / 40 % damage, a red aura)
            yield return Pp(14, new PassiveProcDef { Kind = ProcKind.LowHpBoost, Body = ProcBody.RedAura, Threshold = 25, Power = 20, PowerPerLevel = 10 });
            // Moving HP Recovery: walking 2 s doubles it (its own modifiers: regen while walking, h17cards')
            yield return Pp(15, new PassiveProcDef { Kind = ProcKind.MoveBoost, Body = ProcBody.Motes, Seconds = 2f, Mul = 2f });
            // Mage 22: Mystic Focus (h17cards: every 8 / 7 / 6 s the next magic weapon use deals double; the hand glows)
            yield return Pp(22, PpFocus(8f, -1f, 100, 0, PpMagic));
            // Archer 32-35: Making Arrow (h17cards: a kill drops an arrow bundle 4 / 6 / 8 % of the time; taking it: +1 shot for 10 s), the arrows
            yield return Pp(32, new PassiveProcDef { Kind = ProcKind.KillDrop, Body = ProcBody.Trail, Chance = 4, ChancePerLevel = 2, Seconds = 10f, PickupId = PpPickArrows });
            yield return Pp(33, PpMark()); yield return Pp(34, PpMark()); yield return Pp(35, PpMark());
            // Merchant 52-54: Discount, Vending, Item Appraisal (the pickup family)
            yield return Pp(52, PpMagnet()); yield return Pp(53, PpMagnet()); yield return Pp(54, PpMagnet());
            // Thief 62-64: Hiding (h17cards: falling under 30 % HP, 2 / 2.5 / 3 s untouchable, once per 30 s), Steal, Detoxify
            yield return Pp(62, new PassiveProcDef { Kind = ProcKind.LowHpHide, Body = ProcBody.Mist, Threshold = 30, Seconds = 2f, SecondsPerLevel = 0.5f, Cooldown = 30f });
            yield return Pp(63, PpMagnet()); yield return Pp(64, PpCoat());
            // Knight 102-103: Auto Counter (h17cards: touched, a counter slash 30 + 12 a level, once per 1 s), Cavalier Mastery
            yield return Pp(102, new PassiveProcDef { Kind = ProcKind.HurtCounter, Body = ProcBody.Slash, Damage = 30, DamagePerLevel = 12, Cooldown = 1f });
            yield return Pp(103, PpStride());
            // Crusader 111-113: Reflect Shield (h17cards: a touch returns 15 % a level of its damage), Martyr's Reckoning (under half HP its
            // own modifiers count 1.5 times: a red aura), Providence
            yield return Pp(111, new PassiveProcDef { Kind = ProcKind.Reflect, Body = ProcBody.Shield, Power = 15, PowerPerLevel = 15 });
            yield return Pp(112, new PassiveProcDef { Kind = ProcKind.LowHpBoost, Body = ProcBody.RedAura, Threshold = 50, Mul = 1.5f });
            yield return Pp(113, PpCoat());
            // Wizard 120-121: Magic Crasher (elites and bosses are its bane), Soul Drain (h17cards: a kill by a magic weapon heals 0.5 HP a level)
            yield return Pp(120, PpBane(ranks: PpBit((int)EnemyRank.Elite) | PpBit((int)EnemyRank.Boss) | PpBit((int)EnemyRank.FinalBoss)));
            yield return Pp(121, new PassiveProcDef { Kind = ProcKind.KillHeal, Body = ProcBody.Motes, Damage = 0.5f, DamagePerLevel = 0.5f, ClassMask = PpMagic });
            // Sage 132-136: the four Endows (a charge of the next magic use), Land Protector
            yield return Pp(132, PpFocus(10f, 0f, 30, 10, PpMagic)); yield return Pp(133, PpFocus(10f, 0f, 30, 10, PpMagic));
            yield return Pp(134, PpFocus(10f, 0f, 30, 10, PpMagic)); yield return Pp(135, PpFocus(10f, 0f, 30, 10, PpMagic));
            yield return Pp(136, PpZone());
            // Hunter 141-143: Falcon Mastery, Steel Crow (a feather circles; every 10th hit heavy), Detect (the marks)
            yield return Pp(141, PpAnt(0, PpItemFeather)); yield return Pp(142, PpAnt(0, PpItemFeather)); yield return Pp(143, PpMark());
            // Bard / Dancer 151, Priest 171: Encore, Aspersio (the zone family)
            yield return Pp(151, PpZone()); yield return Pp(171, PpZone());
            // Monk 182-184: Chain Combo, Steel Body, Blade Stop (h17cards: blocks the next hit of an elite or boss, once per 20 / 17 / 14 s)
            yield return Pp(182, PpSweep()); yield return Pp(183, PpCoat());
            yield return Pp(184, new PassiveProcDef { Kind = ProcKind.BossBlock, Body = ProcBody.Shield, Cooldown = 20f, CooldownPerLevel = -3f });
            // Blacksmith 193-196: Hilt Binding (a spark circles; heavy hits), Weapon Perfection, Maximize Power (the idle charge), Ore Discovery
            yield return Pp(193, PpAnt(0)); yield return Pp(194, PpResearch()); yield return Pp(195, PpResearch()); yield return Pp(196, PpMagnet());
            // Alchemist 202: Chemical Protection
            yield return Pp(202, PpCoat());
            // Assassin 212-215: Enchant Poison (a poison bottle circles; heavy hits), Cloaking (h17cards: standing still 1.5 / 1 / 0.5 s,
            // enemies lose you until you move, 4 s at most, then 8 s apart), Lefthand Mastery, Sonic Acceleration
            yield return Pp(212, PpAnt(0, PpItemPoison));
            yield return Pp(213, new PassiveProcDef { Kind = ProcKind.Cloak, Body = ProcBody.Mist, Seconds = 1.5f, SecondsPerLevel = -0.5f, Area = 4f, Cooldown = 8f });
            yield return Pp(214, PpSweep()); yield return Pp(215, PpSweep());
            // Rogue 222-223: Plagiarism (an after-image; 3 dodges in a row: a puff), Gangster's Paradise (h17cards: standing still 2 s,
            // normal enemies do not touch you for 3 s, once per 12 / 10 / 8 s)
            yield return Pp(222, PpEcho());
            yield return Pp(223, new PassiveProcDef { Kind = ProcKind.Gangster, Body = ProcBody.Mist, Seconds = 2f, Area = 3f, Cooldown = 12f, CooldownPerLevel = -2f });
        }
    }
}

namespace Loki.Horde
{
    /// <summary>Client wave 17 (h17pass): a trigger's numbers for the content stamp (HordeWire.ContentStamp hashes them in its 1/1000 steps).</summary>
    public static class HordePassiveStamp
    {
        public static IEnumerable<double> Numbers(PassiveProcDef d)
        {
            if (d == null) yield break;
            foreach (var v in new double[]
            {
                (int)d.Kind, (int)d.Body, d.Count, d.Seconds, d.SecondsPerLevel, d.Power, d.PowerPerLevel, d.Damage, d.DamagePerLevel, d.Area, d.AreaPerLevel,
                d.Chance, d.ChancePerLevel, d.Threshold, d.Mul, d.Cooldown, d.CooldownPerLevel, d.Arg, d.PickupId, (int)d.HitEffect, d.HitEffectSeconds,
                d.ClassMask, d.RaceMask, d.ElementMask, d.SizeMask, d.RankMask, d.BodyMob, d.BodyItem,
            }) yield return v;
        }
    }
}
