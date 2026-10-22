#nullable disable
namespace Loki.Horde.Content
{
    // ====================================================================================================================
    // Client wave 18 (h18rules): the ELITES that come now and then. design note (after wave 18's first pass): "make some Elite Enemies, that
    // sit somewhere between a boss and a regular enemy. They should have an HP bar visible above them." The orchestrator's proposal,
    // built: 6-8 kinds across the run's stages, a step above each stage's normal monsters (HP and damage between a normal enemy and a
    // boss), one or two signature attacks each from h17boss's telegraphed kinds (every one warned 1-1.2 s first), one every 45-75 s
    // from the 2nd minute (more with more players: RulesDef.ElitePlayersPerExtra), worth more EXP, a chest now and then. The engine's
    // rule: HordeSim.Rules.cs StepElites; their HP bars: Horde/Client/HordeEliteBars.cs. Wave 17's two marked elites (Eclipse at 3:30,
    // Vagabond Wolf at 8:00, announced) stay as they are, outside this deal (they also stay the guarded chest's guard).
    // RO facts (mob id, AegisName, element, race, size, Class Boss without MvpExp: a mini-boss) are rAthena's db/re/mob_db.yml, checked
    // by name; EVERY NUMBER (HP, damage, speed, EXP, attacks, drops, the windows) IS LOKI'S OWN, never an official value.
    // ====================================================================================================================
    public static partial class HordeTables
    {
        // the elites of the deal: ids 102-108 (100-199 are the elites; 100 / 101 are wave 17's marked ones)
        const int EliteMastering = 102, EliteToad = 103, EliteDragonFly = 104, EliteGhostring = 105, EliteChimera = 106, EliteDarkIllusion = 107, EliteExecutioner = 108;

        /// <summary>An elite's drops: a chest now and then (off the path, as a boss's), a big heal often.</summary>
        static DropDef[] EliteDrops(float chest) => new[] { new DropDef(PickChest, chest, ChestScatter), new DropDef(PickHealBig, 35f), new DropDef(PickMagnet, 15f) };

        static void AddElitesW18(HordeContent c)
        {
            var r = c.Rules;
            r.EliteFirstSeconds = 60f;     // "from the 2nd minute"
            r.EliteEverySeconds = 45f;     // then one every 45-75 s (the run's dice)
            r.EliteEveryMaxSeconds = 75f;
            r.EliteQuietSeconds = 20f;     // none from 20 s before a boss's / a marked elite's mark to 20 s after it
            r.ElitePlayersPerExtra = 3;    // 1-3 players: one at a time; 4-6: two; 7-8: three

            //                     id                mob   AegisName                                  HP  dmg speed exp  element         race          size
            // ---- THE PLAINS (1:00-4:00; Porings, Lunatics, Fabres, Chonchons, Spores, Thief Bugs: 9-30 HP)
            // MASTERING 1090 (mob_db.yml: Water 1, Plant, Medium, Class Boss): the Poring's big one: calls Porings, bounces onto you.
            var mastering = E(EliteMastering, 1090, "MASTERING", EnemyRank.Elite, EnemyBehavior.Chase, 450, 9, 1.8f, 20, Element.Water, Race.Plant, UnitSize.Medium, 0.6f);
            mastering.KnockbackResist = 70f; mastering.EffectResist = 40f; mastering.DrawScale = 150f;
            mastering.Abilities = new[]
            {
                new EnemyAbilityDef { Kind = AbilityKind.SummonAdds, Cooldown = 14f, FirstAfter = 2f, Count = 4, EnemyId = EnemyPoring, SkillId = 196 },
                new EnemyAbilityDef { Kind = AbilityKind.Teleport, Shape = AbilityShape.Jump, Aim = BossAim.Nearest, Cooldown = 8f, FirstAfter = 5f, Delay = 1f, Area = 1.6f, Damage = 12 },
            };
            mastering.Drops = EliteDrops(30f);
            c.Enemies.Add(mastering);
            // TOAD 1089 (Water 1, Fish, Medium, Class Boss): pulls you in with its tongue, spits a fan of water.
            var toad = E(EliteToad, 1089, "TOAD", EnemyRank.Elite, EnemyBehavior.Chase, 500, 10, 2.2f, 20, Element.Water, Race.Fish, UnitSize.Medium, 0.6f);
            toad.KnockbackResist = 70f; toad.EffectResist = 40f; toad.DrawScale = 150f;
            toad.Abilities = new[]
            {
                new EnemyAbilityDef { Kind = AbilityKind.Pull, Cooldown = 14f, FirstAfter = 4f, Delay = 1f, Range = 6f, Speed = 2f, Duration = 1.5f },
                new EnemyAbilityDef { Kind = AbilityKind.Cone, Shape = AbilityShape.Fan, Aim = BossAim.Nearest, Cooldown = 9f, FirstAfter = 7f, Delay = 1f, Range = 7f, Arc = 60f, Count = 5, Speed = 6f, Damage = 8 },
            };
            toad.Drops = EliteDrops(30f);
            c.Enemies.Add(toad);
            // DRAGON_FLY 1091 (Wind 1, Insect, Small, Class Boss): the Chonchons' big one: fast, it dashes along a warned lane.
            var dragonFly = E(EliteDragonFly, 1091, "DRAGON_FLY", EnemyRank.Elite, EnemyBehavior.Chase, 380, 10, 3.4f, 20, Element.Wind, Race.Insect, UnitSize.Small, 0.45f);
            dragonFly.KnockbackResist = 60f; dragonFly.EffectResist = 40f; dragonFly.DrawScale = 140f;
            dragonFly.Abilities = new[]
            {
                new EnemyAbilityDef { Kind = AbilityKind.Line, Shape = AbilityShape.Dash, Aim = BossAim.Nearest, Cooldown = 8f, FirstAfter = 4f, Delay = 1f, Range = 8f, Speed = 11f, Width = 1.2f, Damage = 14 },
            };
            dragonFly.Drops = EliteDrops(30f);
            c.Enemies.Add(dragonFly);

            // ---- THE UNDEAD (4:00-7:30; Zombies, Skeletons, Archer Skeletons: 55-60 HP)
            // GHOSTRING 1120 (Ghost 4, Demon, Medium, Class Boss): Neutral hits do half (the element table); it vanishes and comes back
            // near the farthest player, and closes a ring of orbs on you.
            var ghostring = E(EliteGhostring, 1120, "GHOSTRING", EnemyRank.Elite, EnemyBehavior.Chase, 700, 14, 2.4f, 35, Element.Ghost, Race.Demon, UnitSize.Medium, 0.6f);
            ghostring.KnockbackResist = 80f; ghostring.EffectResist = 50f; ghostring.DrawScale = 150f;
            ghostring.Abilities = new[]
            {
                new EnemyAbilityDef { Kind = AbilityKind.Teleport, Aim = BossAim.Farthest, Cooldown = 12f, FirstAfter = 6f, Delay = 1f, Width = 3f },
                new EnemyAbilityDef { Kind = AbilityKind.Ring, Shape = AbilityShape.Inward, Aim = BossAim.Random, Cooldown = 12f, FirstAfter = 9f, Delay = 1.2f, Range = 4f, Count = 8, Speed = 3f, Damage = 12 },
            };
            ghostring.Drops = EliteDrops(35f);
            c.Enemies.Add(ghostring);

            // ---- THE WILD (7:30-10:45; Wolves, Poison Spores, Orc Warriors: 55-140 HP)
            // CHIMERA 1283 (Fire 3, Brute, Large, Class Boss): a fire cone, and it leaps onto you.
            var chimera = E(EliteChimera, 1283, "CHIMERA", EnemyRank.Elite, EnemyBehavior.Chase, 1600, 20, 2.6f, 55, Element.Fire, Race.Brute, UnitSize.Large, 0.7f);
            chimera.KnockbackResist = 80f; chimera.EffectResist = 50f; chimera.DrawScale = 140f;
            chimera.Abilities = new[]
            {
                new EnemyAbilityDef { Kind = AbilityKind.Cone, Aim = BossAim.Nearest, Cooldown = 9f, FirstAfter = 5f, Delay = 1.1f, Range = 4.5f, Arc = 90f, Damage = 22 },
                new EnemyAbilityDef { Kind = AbilityKind.Teleport, Shape = AbilityShape.Jump, Aim = BossAim.Nearest, Cooldown = 11f, FirstAfter = 8f, Delay = 1f, Area = 1.8f, Damage = 20 },
            };
            chimera.Drops = EliteDrops(35f);
            c.Enemies.Add(chimera);

            // ---- THE TOMB (10:45-13:30; Munaks, Isis: 190-250 HP)
            // DARK_ILLUSION 1302 (Undead 4, Demon, Large, Class Boss): marks a player (the mark bursts), and a dark cone.
            var illusion = E(EliteDarkIllusion, 1302, "DARK_ILLUSION", EnemyRank.Elite, EnemyBehavior.Chase, 2200, 22, 2.2f, 70, Element.Undead, Race.Demon, UnitSize.Large, 0.7f);
            illusion.KnockbackResist = 80f; illusion.EffectResist = 50f; illusion.DrawScale = 140f;
            illusion.Abilities = new[]
            {
                new EnemyAbilityDef { Kind = AbilityKind.Mark, Aim = BossAim.Random, Cooldown = 12f, FirstAfter = 6f, Delay = 2f, Duration = 1f, Area = 2.2f, Damage = 26 },
                new EnemyAbilityDef { Kind = AbilityKind.Cone, Aim = BossAim.Nearest, Cooldown = 10f, FirstAfter = 9f, Delay = 1.1f, Range = 5f, Arc = 90f, Damage = 24 },
            };
            illusion.Drops = EliteDrops(40f);
            c.Enemies.Add(illusion);

            // ---- THE GATE (12:30-15:00; Nightmares, Isis: 220-250 HP)
            // EXECUTIONER 1205 (Dark 2, Formless, Large, Class Boss): a sword that dashes along a lane, and a ring of blades from it.
            var executioner = E(EliteExecutioner, 1205, "EXECUTIONER", EnemyRank.Elite, EnemyBehavior.Chase, 2600, 26, 2.6f, 80, Element.Dark, Race.Formless, UnitSize.Large, 0.7f);
            executioner.KnockbackResist = 85f; executioner.EffectResist = 60f; executioner.DrawScale = 140f;
            executioner.Abilities = new[]
            {
                new EnemyAbilityDef { Kind = AbilityKind.Line, Shape = AbilityShape.Dash, Aim = BossAim.Nearest, Cooldown = 10f, FirstAfter = 5f, Delay = 1.2f, Range = 9f, Speed = 12f, Width = 1.4f, Damage = 26 },
                new EnemyAbilityDef { Kind = AbilityKind.Ring, Cooldown = 14f, FirstAfter = 9f, Delay = 1f, Range = 6f, Width = 0.8f, Speed = 6f, Arc = 80f, Damage = 20 },
            };
            executioner.Drops = EliteDrops(40f);
            c.Enemies.Add(executioner);

            // the deal: who may come when (seconds of the 900 s reference run)
            c.Elites.Add(new EliteDeal { EnemyId = EliteMastering, From = 60f, To = 240f });
            c.Elites.Add(new EliteDeal { EnemyId = EliteToad, From = 60f, To = 240f });
            c.Elites.Add(new EliteDeal { EnemyId = EliteDragonFly, From = 120f, To = 240f });
            c.Elites.Add(new EliteDeal { EnemyId = EliteGhostring, From = 240f, To = 450f });
            c.Elites.Add(new EliteDeal { EnemyId = EliteChimera, From = 450f, To = 645f });
            c.Elites.Add(new EliteDeal { EnemyId = EliteDarkIllusion, From = 645f, To = 810f });
            c.Elites.Add(new EliteDeal { EnemyId = EliteExecutioner, From = 750f, To = 900f });
        }
    }
}
