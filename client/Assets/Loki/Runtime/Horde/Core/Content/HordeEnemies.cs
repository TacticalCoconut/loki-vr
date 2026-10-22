#nullable disable
namespace Loki.Horde.Content
{
    // ====================================================================================================================
    // Horde: enemies, waves and pickups (w8content; second pass h9content, client wave 9). EVERY NUMBER HERE IS LOKI'S OWN
    // DESIGN (HP, damage, speed, EXP, waves, drop chances), never an official Ragnarok value. RO facts per enemy (mob id,
    // AegisName, element, race, size, class, what it summons) come from rAthena's db/re/mob_db.yml and
    // db/re/mob_skill_db.txt (line numbers quoted).
    //
    // Wave 9 (h9content): the schedule has a PULSE (pressure peaks of ~30 s with breathers of 25-30 s between them, the
    // elites and bosses at the end of a peak), and enemy speeds are set against the player's 4 cells/s in three kinds:
    // slow swarms (30-50 % of the player), fast chasers that a plain player outruns by a hair (82-90 %), sweepers that
    // cross faster than the player and are dodged sideways (125-140 %); bosses walk at 45-60 %. The design document is
    // design notes
    // ====================================================================================================================
    public static partial class HordeTables
    {
        // ---- enemy ids (the mode's own): 1-99 normal, 100-199 elites, 200-299 bosses, 300 the final boss
        const int EnemyPoring = 1, EnemyLunatic = 2, EnemyFabre = 3, EnemyChonchon = 4, EnemySpore = 5, EnemyThiefBug = 6,
            EnemyZombie = 7, EnemySkeleton = 8, EnemyArcherSkeleton = 9, EnemyWolf = 10, EnemyPoisonSpore = 11,
            EnemyOrkWarrior = 12, EnemyHunterFly = 13, EnemyMunak = 14, EnemyIsis = 15, EnemyNightmare = 16,
            EnemyHighOrc = 20, EnemyBaphometJr = 21;
        const int EnemyEclipse = 100, EnemyVagabondWolf = 101;
        const int EnemyOrkHero = 200, EnemyOsiris = 201;
        const int EnemyBaphomet = 300;
        // client wave 17 (h17boss), point 23: the four new mid-run bosses (dealt among the boss marks: HordeSim.BossPool) and the adds
        // and runners of the new attacks
        const int EnemyMistress = 202, EnemyEddga = 203, EnemyMaya = 204, EnemyMoonlight = 205;
        const int EnemyGiantHornet = 22, EnemyBigfoot = 23, EnemyArclouse = 24, EnemyNineTail = 25, EnemyOrcMarcher = 26, EnemyWolfRunner = 27;

        // ---- pickup ids (the mode's own)
        const int PickExpS = 1, PickExpM = 2, PickExpL = 3, PickHeal = 4, PickHealBig = 5, PickMagnet = 6, PickBomb = 7, PickChest = 8, PickChestBig = 9;

        // ---- the player's speed the enemies are set against (RulesDef.MoveSpeed, HordeTables.cs): 4 cells/s.
        //      slow swarm 1.2-2.0 | fast chaser 3.3-3.6 (outrun by a hair; a speed passive makes it comfortable) |
        //      sweeper 5.0-5.5 (crosses the arena; dodged sideways) | ranged 1.6 (stops at its shot range) | boss 1.8-2.4
        const float SpeedSwarmSlow = 1.3f, SpeedSwarm = 1.6f, SpeedSwarmBrisk = 2.0f, SpeedChaserLight = 3.3f, SpeedChaser = 3.6f,
            SpeedBruteWalk = 3.0f, SpeedSweep = 5.0f, SpeedSweepFast = 5.5f;

        /// <summary>Wave 9: an elite's / boss's chest lands this many cells from the body, in a random direction (DropDef.ScatterCells):
        /// off the path, so moving pays. Heals and magnets stay at the body.</summary>
        const float ChestScatter = 5f;

        static DropDef[] CommonDrops(float heal)
        {
            return new[] { new DropDef(PickHeal, heal), new DropDef(PickMagnet, 0.45f), new DropDef(PickBomb, 0.5f) };
        }

        static EnemyDef E(int id, int mobId, string key, EnemyRank rank, EnemyBehavior behavior, int hp, int damage, float speed, int exp,
            Element element, Race race, UnitSize size, float radius)
        {
            return new EnemyDef
            {
                Id = id, MobId = mobId, Key = key, Rank = rank, Behavior = behavior, Hp = hp, Damage = damage, Speed = speed, Exp = exp,
                Element = element, Race = race, Size = size, Radius = radius, AttackInterval = 1f,
                Drops = CommonDrops(rank == EnemyRank.Normal ? 1.5f : 0f),
            };
        }

        static void AddEnemies(HordeContent c)
        {
            // Normal monsters, in the order they come. Element / race / size are mob_db.yml's (so Demon Bane, Beast Bane
            // and the element table work as in RO); HP, damage, speed and EXP are Loki's own.
            //                id                 mob   AegisName                             HP  dmg speed exp  element (db)      race (db)        size (db)
            c.Enemies.Add(E(EnemyPoring, 1002, "PORING", EnemyRank.Normal, EnemyBehavior.Chase, 10, 5, SpeedSwarm, 1, Element.Water, Race.Plant, UnitSize.Medium, 0.45f));
            // Lunatic: the first fast chaser (82 % of the player), from 0:30.
            c.Enemies.Add(E(EnemyLunatic, 1063, "LUNATIC", EnemyRank.Normal, EnemyBehavior.Chase, 9, 4, SpeedChaserLight, 1, Element.Neutral, Race.Brute, UnitSize.Small, 0.35f));
            c.Enemies.Add(E(EnemyFabre, 1007, "FABRE", EnemyRank.Normal, EnemyBehavior.Chase, 15, 5, 1.8f, 1, Element.Earth, Race.Insect, UnitSize.Small, 0.35f));
            // Chonchon: a sweeping pack that crosses faster than the player: step aside, do not run.
            var chonchon = E(EnemyChonchon, 1011, "CHONCHON", EnemyRank.Normal, EnemyBehavior.Sweep, 9, 6, SpeedSweep, 1, Element.Wind, Race.Insect, UnitSize.Small, 0.35f);
            c.Enemies.Add(chonchon);
            c.Enemies.Add(E(EnemySpore, 1014, "SPORE", EnemyRank.Normal, EnemyBehavior.Chase, 30, 6, SpeedSwarm, 2, Element.Water, Race.Plant, UnitSize.Medium, 0.45f));
            // Thief Bug: the fast chaser of the first minutes (90 % of the player).
            c.Enemies.Add(E(EnemyThiefBug, 1051, "THIEF_BUG", EnemyRank.Normal, EnemyBehavior.Chase, 26, 6, SpeedChaser, 2, Element.Neutral, Race.Insect, UnitSize.Small, 0.35f));
            c.Enemies.Add(E(EnemyZombie, 1015, "ZOMBIE", EnemyRank.Normal, EnemyBehavior.Chase, 60, 10, SpeedSwarmSlow, 4, Element.Undead, Race.Undead, UnitSize.Medium, 0.45f));
            c.Enemies.Add(E(EnemySkeleton, 1076, "SKELETON", EnemyRank.Normal, EnemyBehavior.Chase, 60, 10, SpeedSwarmBrisk, 4, Element.Undead, Race.Undead, UnitSize.Medium, 0.45f));
            // Archer Skeleton: the ranged kind: walks until 6 cells away, then shoots every 2.6 s; a player nearer than 4
            // cells makes it back off (KeepRange, wave 9): it keeps its distance, as a ranged kind should.
            var archerSkel = E(EnemyArcherSkeleton, 1016, "ARCHER_SKELETON", EnemyRank.Normal, EnemyBehavior.Ranged, 55, 6, SpeedSwarm, 4, Element.Undead, Race.Undead, UnitSize.Medium, 0.45f);
            archerSkel.ShotDamage = 6; archerSkel.ShotRange = 6f; archerSkel.ShotSpeed = 7f; archerSkel.AttackInterval = 2.6f; archerSkel.KeepRange = 4f;
            c.Enemies.Add(archerSkel);
            // Wolf: walks at 75 % of the player and charges (0.7 s wind-up, then 8 cells/s for a second) every 5 s.
            var wolf = E(EnemyWolf, 1013, "WOLF", EnemyRank.Normal, EnemyBehavior.Charge, 80, 10, SpeedBruteWalk, 4, Element.Earth, Race.Brute, UnitSize.Medium, 0.45f);
            wolf.ChargeCooldown = 5f; wolf.ChargeWindup = 0.7f; wolf.ChargeSpeed = 8f;
            c.Enemies.Add(wolf);
            // Poison Spore: bursts on touch (1.6 cells); brisk enough that it must be shot before it arrives.
            var poisonSpore = E(EnemyPoisonSpore, 1077, "POISON_SPORE", EnemyRank.Normal, EnemyBehavior.Exploder, 55, 18, 2.4f, 3, Element.Poison, Race.Plant, UnitSize.Medium, 0.45f);
            poisonSpore.BurstArea = 1.6f;
            c.Enemies.Add(poisonSpore);
            c.Enemies.Add(E(EnemyOrkWarrior, 1023, "ORK_WARRIOR", EnemyRank.Normal, EnemyBehavior.Chase, 140, 15, SpeedSwarmBrisk, 5, Element.Earth, Race.Demihuman, UnitSize.Medium, 0.5f));
            c.Enemies.Add(E(EnemyHunterFly, 1035, "HUNTER_FLY", EnemyRank.Normal, EnemyBehavior.Sweep, 70, 14, SpeedSweepFast, 4, Element.Wind, Race.Insect, UnitSize.Small, 0.4f));
            c.Enemies.Add(E(EnemyMunak, 1026, "MUNAK", EnemyRank.Normal, EnemyBehavior.Chase, 190, 18, 1.8f, 6, Element.Undead, Race.Undead, UnitSize.Medium, 0.45f));
            c.Enemies.Add(E(EnemyIsis, 1029, "ISIS", EnemyRank.Normal, EnemyBehavior.Chase, 250, 17, SpeedSwarmBrisk, 7, Element.Dark, Race.Demon, UnitSize.Large, 0.6f));
            var nightmare = E(EnemyNightmare, 1061, "NIGHTMARE", EnemyRank.Normal, EnemyBehavior.Charge, 220, 22, SpeedBruteWalk, 8, Element.Ghost, Race.Demon, UnitSize.Large, 0.6f);
            nightmare.ChargeCooldown = 5f; nightmare.ChargeWindup = 0.7f; nightmare.ChargeSpeed = 9f;
            c.Enemies.Add(nightmare);
            // Adds only (called by bosses, never in the waves): the slaves rAthena's mob_skill_db.txt gives these bosses.
            // G_HIGH_ORC 1439: Orc Hero's NPC_SUMMONSLAVE (mob_skill_db.txt:417-418). Fire / Demihuman / Large.
            c.Enemies.Add(E(EnemyHighOrc, 1439, "G_HIGH_ORC", EnemyRank.Normal, EnemyBehavior.Chase, 120, 12, 2.4f, 4, Element.Fire, Race.Demihuman, UnitSize.Large, 0.55f));
            // BAPHOMET_ 1101 (Baphomet Jr.): Baphomet's NPC_SUMMONSLAVE on spawn (mob_skill_db.txt:204). Dark / Demon / Small.
            // A fast chaser: the final boss's adds hunt the player down.
            c.Enemies.Add(E(EnemyBaphometJr, 1101, "BAPHOMET_", EnemyRank.Normal, EnemyBehavior.Chase, 180, 18, SpeedChaser, 5, Element.Dark, Race.Demon, UnitSize.Small, 0.4f));
            // client wave 17 (h17boss), point 23: the new bosses' slaves (mob_skill_db.txt NPC_SUMMONSLAVE: Mistress 1604 :310, Eddga 1603
            // :588, Maya 1477 :759, Moonlight Flower 1471 :801), called by them, never in the waves
            c.Enemies.Add(E(EnemyGiantHornet, 1604, "G_GIANT_HONET", EnemyRank.Normal, EnemyBehavior.Chase, 60, 8, 3.4f, 2, Element.Wind, Race.Insect, UnitSize.Small, 0.35f));
            c.Enemies.Add(E(EnemyBigfoot, 1603, "G_BIGFOOT", EnemyRank.Normal, EnemyBehavior.Chase, 220, 16, 2.2f, 5, Element.Earth, Race.Brute, UnitSize.Large, 0.6f));
            c.Enemies.Add(E(EnemyArclouse, 1477, "G_ARCLOUSE", EnemyRank.Normal, EnemyBehavior.Chase, 45, 8, 2.8f, 1, Element.Earth, Race.Insect, UnitSize.Medium, 0.4f));
            c.Enemies.Add(E(EnemyNineTail, 1471, "G_NINE_TAIL", EnemyRank.Normal, EnemyBehavior.Chase, 110, 12, 3.3f, 3, Element.Fire, Race.Brute, UnitSize.Medium, 0.45f));
            // the runners of a lane (AbilityShape.Runners): Ork Hero's Orc Squad marches at 4.5 cells/s (a little faster than you: step
            // aside), Vagabond Wolf's Howl runs at 7; both cross the arena and are gone at its edge (Sweep)
            c.Enemies.Add(E(EnemyOrcMarcher, 1023, "ORK_WARRIOR", EnemyRank.Normal, EnemyBehavior.Sweep, 140, 15, 4.5f, 2, Element.Earth, Race.Demihuman, UnitSize.Medium, 0.5f));
            c.Enemies.Add(E(EnemyWolfRunner, 1013, "WOLF", EnemyRank.Normal, EnemyBehavior.Sweep, 60, 14, 7f, 1, Element.Earth, Race.Brute, UnitSize.Medium, 0.45f));

            // ---- elites: mob_db Class "Boss" monsters that are not MVPs (no MvpExp), drawn larger
            // ECLIPSE 1093 (Neutral 3, Brute, Medium, Class Boss): summons LUNATIC 1063 (mob_skill_db.txt:453-454).
            var eclipse = E(EnemyEclipse, 1093, "ECLIPSE", EnemyRank.Elite, EnemyBehavior.Chase, 900, 14, SpeedBruteWalk, 40, Element.Neutral, Race.Brute, UnitSize.Medium, 0.6f);
            eclipse.KnockbackResist = 80f; eclipse.EffectResist = 50f; eclipse.DrawScale = 150f;
            eclipse.Abilities = new[]
            {
                new EnemyAbilityDef { Kind = AbilityKind.SummonAdds, Cooldown = 12f, FirstAfter = 2f, Count = 6, EnemyId = EnemyLunatic, SkillId = 196 },
                // client wave 17 (h17boss), point 23: Carrot Rain (7 small circles around a player, 0.15 s apart) and Hop (a jump where the
                // nearest player stood; its shadow first)
                new EnemyAbilityDef { Kind = AbilityKind.Grid, Shape = AbilityShape.Rain, Aim = BossAim.Random, Cooldown = 10f, FirstAfter = 5f, Delay = 1.1f, Count = 7, Area = 1f, Spacing = 4f, Duration = 0.15f, Damage = 12 },
                new EnemyAbilityDef { Kind = AbilityKind.Teleport, Shape = AbilityShape.Jump, Aim = BossAim.Nearest, Cooldown = 9f, FirstAfter = 7f, Delay = 1f, Area = 1.5f, Damage = 16 },
            };
            eclipse.Drops = new[] { new DropDef(PickChest, 100f, ChestScatter), new DropDef(PickHealBig, 50f) };
            c.Enemies.Add(eclipse);
            // VAGABOND_WOLF 1092 (Earth, Brute, Medium, Class Boss): summons WOLF 1013 (mob_skill_db.txt:448-450).
            var vwolf = E(EnemyVagabondWolf, 1092, "VAGABOND_WOLF", EnemyRank.Elite, EnemyBehavior.Charge, 2600, 24, SpeedBruteWalk, 80, Element.Earth, Race.Brute, UnitSize.Medium, 0.65f);
            vwolf.ChargeCooldown = 6f; vwolf.ChargeWindup = 0.9f; vwolf.ChargeSpeed = 9f;
            vwolf.KnockbackResist = 80f; vwolf.EffectResist = 50f; vwolf.DrawScale = 150f;
            vwolf.Abilities = new[]
            {
                new EnemyAbilityDef { Kind = AbilityKind.SummonAdds, Cooldown = 15f, FirstAfter = 1f, Count = 4, EnemyId = EnemyWolf, SkillId = 196 },
                // client wave 17 (h17boss), point 23: Howl (3 wolves run along parallel lanes 2.5 cells apart: stand between two, or out of
                // them) and Pounce (a jump where the nearest player stood)
                new EnemyAbilityDef { Kind = AbilityKind.Line, Shape = AbilityShape.Runners, Aim = BossAim.Nearest, Cooldown = 14f, FirstAfter = 6f, Delay = 1.2f, Count = 3, Spacing = 2.5f, EnemyId = EnemyWolfRunner },
                new EnemyAbilityDef { Kind = AbilityKind.Teleport, Shape = AbilityShape.Jump, Aim = BossAim.Nearest, Cooldown = 10f, FirstAfter = 9f, Delay = 1f, Area = 1.6f, Damage = 22 },
            };
            vwolf.Drops = new[] { new DropDef(PickChest, 100f, ChestScatter), new DropDef(PickHealBig, 50f) };
            c.Enemies.Add(vwolf);

            // ---- bosses (MVPs of mob_db: MvpExp > 0); bosses walk at 45-60 % of the player: their threat is the adds and the
            //      telegraphed strikes, never a chase
            // ORK_HERO 1087 (Earth 2, Demihuman, Large, MVP): NPC_SUMMONSLAVE G_HIGH_ORC 1439 (mob_skill_db.txt:417-418).
            var orkHero = E(EnemyOrkHero, 1087, "ORK_HERO", EnemyRank.Boss, EnemyBehavior.Chase, 3000, 14, 1.8f, 150, Element.Earth, Race.Demihuman, UnitSize.Large, 0.9f);
            orkHero.KnockbackResist = 100f; orkHero.EffectResist = 100f; orkHero.DrawScale = 130f;
            orkHero.Abilities = new[]
            {
                new EnemyAbilityDef { Kind = AbilityKind.SummonAdds, Cooldown = 25f, FirstAfter = 3f, Count = 2, EnemyId = EnemyHighOrc, SkillId = 196 },
                // client wave 17 (h17boss): the nova every 12 s (was 9: four more attacks share its turns now)
                new EnemyAbilityDef { Kind = AbilityKind.Nova, Cooldown = 12f, FirstAfter = 6f, Damage = 24, Area = 3f, Delay = 1.2f, SkillId = 0 },
                // client wave 17 (h17boss), point 23: War Cry Charge (a red lane, then a 10-cell dash at 12 cells/s: KN_CHARGEATK 1001's look),
                // Ground Slam (a 100 degree cone, 4.5 cells: NPC_GROUNDATTACK 185, Orc Hero's own, mob_skill_db.txt:414), Shock Ring (a band
                // from it out to 9 cells at 7 cells/s, a 70 degree gap), Orc Squad (4 Orc Warriors march side by side across the arena)
                new EnemyAbilityDef { Kind = AbilityKind.Line, Shape = AbilityShape.Dash, Aim = BossAim.Nearest, Cooldown = 12f, FirstAfter = 9f, Delay = 1.2f, Range = 10f, Speed = 12f, Damage = 22, SkillId = 1001 },
                new EnemyAbilityDef { Kind = AbilityKind.Cone, Aim = BossAim.Nearest, Cooldown = 10f, FirstAfter = 13f, Delay = 1.1f, Range = 4.5f, Arc = 100f, Damage = 26, SkillId = 185 },
                new EnemyAbilityDef { Kind = AbilityKind.Ring, Cooldown = 15f, FirstAfter = 18f, Delay = 1f, Range = 9f, Width = 0.8f, Speed = 7f, Arc = 70f, Damage = 20 },
                new EnemyAbilityDef { Kind = AbilityKind.Line, Shape = AbilityShape.Runners, Aim = BossAim.Nearest, Cooldown = 22f, FirstAfter = 16f, Delay = 1.3f, Count = 4, Spacing = 1.2f, EnemyId = EnemyOrcMarcher },
            };
            orkHero.Drops = new[] { new DropDef(PickChestBig, 100f, ChestScatter), new DropDef(PickHealBig, 100f), new DropDef(PickMagnet, 100f) };
            c.Enemies.Add(orkHero);
            // OSIRIS 1038 (Undead 4, Undead race, Medium, MVP): summons ISIS 1029 on spawn (mob_skill_db.txt:182); uses
            // NPC_DARKBREATH 202 (mob_skill_db.txt:176-177).
            var osiris = E(EnemyOsiris, 1038, "OSIRIS", EnemyRank.Boss, EnemyBehavior.Chase, 6500, 24, SpeedSwarmBrisk, 300, Element.Undead, Race.Undead, UnitSize.Medium, 0.8f);
            osiris.KnockbackResist = 100f; osiris.EffectResist = 100f; osiris.DrawScale = 140f;
            osiris.Abilities = new[]
            {
                new EnemyAbilityDef { Kind = AbilityKind.SummonAdds, Cooldown = 25f, FirstAfter = 1f, Count = 4, EnemyId = EnemyIsis, SkillId = 196 },
                // client wave 17 (h17boss): Dark Breath every 9 s (was 7: four more attacks share its turns now)
                new EnemyAbilityDef { Kind = AbilityKind.AreaAtPlayers, Cooldown = 9f, FirstAfter = 5f, Damage = 26, Area = 2f, Delay = 1.2f, Count = 2, SkillId = 202 },
                // client wave 17 (h17boss), point 23: Curse Ring (12 dark orbs on a 5-cell circle around a player, one place empty, close in at
                // 3 cells/s: NPC_CURSEATTACK 181, mob_skill_db.txt:174), Sand Pull (everyone within 8 cells pulled toward it at 2.2 cells/s
                // for 2 s), Death Mark (follows a player 2 s, stays 1 s, bursts 2.5 cells: NPC_DARKTHUNDER 341, :184), Mummy Trail (6 s of
                // sand behind it, 40 % slower inside, each patch 6 s: WZ_QUAGMIRE 92, Osiris's own, :186)
                new EnemyAbilityDef { Kind = AbilityKind.Ring, Shape = AbilityShape.Inward, Aim = BossAim.Random, Cooldown = 14f, FirstAfter = 8f, Delay = 1.2f, Range = 5f, Count = 13, Speed = 3f, Damage = 16, SkillId = 181 },
                new EnemyAbilityDef { Kind = AbilityKind.Pull, Cooldown = 18f, FirstAfter = 12f, Delay = 1f, Range = 8f, Speed = 2.2f, Duration = 2f },
                new EnemyAbilityDef { Kind = AbilityKind.Mark, Aim = BossAim.Random, Cooldown = 16f, FirstAfter = 18f, Delay = 2f, Duration = 1f, Area = 2.5f, Damage = 30, SkillId = 341 },
                new EnemyAbilityDef { Kind = AbilityKind.Trail, Cooldown = 18f, FirstAfter = 6f, Duration = 6f, Area = 1.2f, Linger = 6f, SlowPct = 40f, SkillId = 92 },
            };
            osiris.Drops = new[] { new DropDef(PickChestBig, 100f, ChestScatter), new DropDef(PickHealBig, 100f), new DropDef(PickMagnet, 100f) };
            c.Enemies.Add(osiris);
            AddNewBosses(c); // client wave 17 (h17boss), point 23

            // ---- the final boss
            // BAPHOMET 1039 (Dark 3, Demon, Large, MVP): BAPHOMET_ 1101 on spawn (mob_skill_db.txt:204), NPC_EARTHQUAKE 653
            // (:192-193), NPC_DARKBREATH 202 (:199).
            var baph = E(EnemyBaphomet, 1039, "BAPHOMET", EnemyRank.FinalBoss, EnemyBehavior.Chase, 8000, 40, 2.4f, 500, Element.Dark, Race.Demon, UnitSize.Large, 1f);
            baph.KnockbackResist = 100f; baph.EffectResist = 100f; baph.DrawScale = 150f;
            // client wave 17 (h17boss), point 23 ("Lightning Grid, Dark Breath (a wide cone), Teleport, Summon Baphomet Jr. x4; at 50 % HP
            // Brandish and the grid twice as fast"): Baphomet Jr. x4 (was 3); the Earthquake nova kept, every 16 s (was 12); Dark Breath is a
            // 120 degree cone now (it was 3 circles at the players); Lightning Grid (7 x 7 squares of 2 cells around a player: the even ones
            // strike after 1.3 s, the odd ones 1 s later: WZ_VERMILION 85, Baphomet's own, mob_skill_db.txt:205); Teleport (2.5 cells from
            // the farthest player: AL_TELEPORT 26, :189); under 50 % HP its second phase: Brandish (a 180 degree cone, 5 cells:
            // KN_BRANDISHSPEAR 57, :191) and the grid twice as often, its halves 0.5 s apart
            baph.Abilities = new[]
            {
                new EnemyAbilityDef { Kind = AbilityKind.SummonAdds, Cooldown = 25f, FirstAfter = 1f, Count = 4, EnemyId = EnemyBaphometJr, SkillId = 196 },
                new EnemyAbilityDef { Kind = AbilityKind.Nova, Cooldown = 16f, FirstAfter = 8f, Damage = 45, Area = 4.5f, Delay = 1.5f, SkillId = 653 },
                new EnemyAbilityDef { Kind = AbilityKind.Cone, Aim = BossAim.Nearest, Cooldown = 9f, FirstAfter = 4f, Delay = 1.2f, Range = 7f, Arc = 120f, Damage = 40, SkillId = 202 },
                new EnemyAbilityDef { Kind = AbilityKind.Grid, Shape = AbilityShape.Checker, Aim = BossAim.Random, Cooldown = 14f, FirstAfter = 6f, Delay = 1.3f, Width = 2f, Count = 7, Duration = 1f, Damage = 34, PhaseCooldownPct = 50f, SkillId = 85 },
                new EnemyAbilityDef { Kind = AbilityKind.Teleport, Aim = BossAim.Farthest, Cooldown = 15f, FirstAfter = 12f, Delay = 1f, Width = 2.5f, SkillId = 26 },
                new EnemyAbilityDef { Kind = AbilityKind.Phase, Cooldown = 1f, FirstAfter = 0f, HpBelow = 50f },
                new EnemyAbilityDef { Kind = AbilityKind.Cone, Aim = BossAim.Nearest, Cooldown = 8f, FirstAfter = 0f, Delay = 1f, Range = 5f, Arc = 180f, Damage = 48, HpBelow = 50f, SkillId = 57 },
            };
            baph.Drops = new[] { new DropDef(PickChestBig, 100f, ChestScatter), new DropDef(PickHealBig, 100f), new DropDef(PickMagnet, 100f) };
            c.Enemies.Add(baph);
        }

        /// <summary>
        /// Client wave 17 (h17boss), point 23 of the design proposal: four new mid-run bosses for the random order ("point 21 deals among
        /// them": they have no boss mark of their own; HordeSim.BossPool puts them in the deal of the two marks, so a run meets two of
        /// the six). Each has three attacks. Their HP and touch damage are a middle between Ork Hero's and Osiris's: a boss always takes
        /// the HP and damage of the mark it lands on (point 21), its attacks and adds too, so these numbers only set the RATIO of its
        /// attacks to its touch. RO facts are mob_db.yml's (id, AegisName, element, race, size, MVP) and mob_skill_db.txt's (lines quoted);
        /// every number is Loki's own.
        /// </summary>
        static void AddNewBosses(HordeContent c)
        {
            DropDef[] BossDrops() => new[] { new DropDef(PickChestBig, 100f, ChestScatter), new DropDef(PickHealBig, 100f), new DropDef(PickMagnet, 100f) };
            // MISTRESS 1059 (Wind 4, Insect, Small, MVP; mob_db.yml:2851): her bees (G_GIANT_HONET 1604, :310), a lightning lane
            // (WZ_JUPITEL 84, :314), a ring of silence spreading from her (NPC_WIDESILENCE 663, :303)
            var mistress = E(EnemyMistress, 1059, "MISTRESS", EnemyRank.Boss, EnemyBehavior.Chase, 4500, 18, 2.4f, 220, Element.Wind, Race.Insect, UnitSize.Small, 0.8f);
            mistress.KnockbackResist = 100f; mistress.EffectResist = 100f; mistress.DrawScale = 150f;
            mistress.Abilities = new[]
            {
                new EnemyAbilityDef { Kind = AbilityKind.SummonAdds, Cooldown = 20f, FirstAfter = 2f, Count = 5, EnemyId = EnemyGiantHornet, SkillId = 196 },
                new EnemyAbilityDef { Kind = AbilityKind.Line, Aim = BossAim.Nearest, Cooldown = 8f, FirstAfter = 5f, Delay = 1.2f, Range = 14f, Width = 1.6f, Damage = 26, SkillId = 84 },
                new EnemyAbilityDef { Kind = AbilityKind.Ring, Cooldown = 13f, FirstAfter = 10f, Delay = 1f, Range = 8f, Width = 0.8f, Speed = 6f, Arc = 80f, Damage = 18, SkillId = 663 },
            };
            mistress.Drops = BossDrops();
            c.Enemies.Add(mistress);
            // EDDGA 1115 (Fire 1, Brute, Large, MVP; :5651): fire pillars in a cross through a player (WZ_FIREPILLAR 80's look; 9 squares of
            // 1.5 cells a line), a pounce that lands with a Magnum Break (SM_MAGNUM 7, :592), Bigfoot (G_BIGFOOT 1603, :588)
            var eddga = E(EnemyEddga, 1115, "EDDGA", EnemyRank.Boss, EnemyBehavior.Chase, 4500, 18, 2f, 220, Element.Fire, Race.Brute, UnitSize.Large, 1f);
            eddga.KnockbackResist = 100f; eddga.EffectResist = 100f; eddga.DrawScale = 130f;
            eddga.Abilities = new[]
            {
                new EnemyAbilityDef { Kind = AbilityKind.SummonAdds, Cooldown = 25f, FirstAfter = 3f, Count = 2, EnemyId = EnemyBigfoot, SkillId = 196 },
                new EnemyAbilityDef { Kind = AbilityKind.Grid, Shape = AbilityShape.Cross, Aim = BossAim.Random, Cooldown = 10f, FirstAfter = 6f, Delay = 1.3f, Width = 1.5f, Count = 9, Damage = 28, SkillId = 80 },
                new EnemyAbilityDef { Kind = AbilityKind.Teleport, Shape = AbilityShape.Jump, Aim = BossAim.Nearest, Cooldown = 12f, FirstAfter = 9f, Delay = 1.2f, Area = 2f, Damage = 26, SkillId = 7 },
            };
            eddga.Drops = BossDrops();
            c.Enemies.Add(eddga);
            // MAYA 1147 (Earth 4, Insect, Large, MVP; :7214): an ant swarm (G_ARCLOUSE 1477, :759), a petrifying cone (MG_STONECURSE 16's
            // look: 70 % slower for 1.5 s), Heaven's Drive at two players (WZ_HEAVENDRIVE 91, Maya's own, :762)
            var maya = E(EnemyMaya, 1147, "MAYA", EnemyRank.Boss, EnemyBehavior.Chase, 4500, 18, 1.8f, 220, Element.Earth, Race.Insect, UnitSize.Large, 1f);
            maya.KnockbackResist = 100f; maya.EffectResist = 100f; maya.DrawScale = 130f;
            maya.Abilities = new[]
            {
                new EnemyAbilityDef { Kind = AbilityKind.SummonAdds, Cooldown = 18f, FirstAfter = 2f, Count = 6, EnemyId = EnemyArclouse, SkillId = 196 },
                new EnemyAbilityDef { Kind = AbilityKind.Cone, Aim = BossAim.Nearest, Cooldown = 11f, FirstAfter = 6f, Delay = 1.2f, Range = 6f, Arc = 70f, Damage = 16, SlowPct = 70f, Duration = 1.5f, SkillId = 16 },
                new EnemyAbilityDef { Kind = AbilityKind.AreaAtPlayers, Cooldown = 9f, FirstAfter = 10f, Delay = 1.2f, Area = 2.2f, Count = 2, Damage = 24, SkillId = 91 },
            };
            maya.Drops = BossDrops();
            c.Enemies.Add(maya);
            // MOONLIGHT 1150 (Fire 3, Demon, Medium, MVP; :7372): teleports (AL_TELEPORT 26, :782), Nine Tails (G_NINE_TAIL 1471, :801), a
            // fan of 7 fox fires across 70 degrees (MG_FIREBOLT 19, hers, :792)
            var moon = E(EnemyMoonlight, 1150, "MOONLIGHT", EnemyRank.Boss, EnemyBehavior.Chase, 4500, 18, 2.6f, 220, Element.Fire, Race.Demon, UnitSize.Medium, 0.8f);
            moon.KnockbackResist = 100f; moon.EffectResist = 100f; moon.DrawScale = 150f;
            moon.Abilities = new[]
            {
                new EnemyAbilityDef { Kind = AbilityKind.SummonAdds, Cooldown = 22f, FirstAfter = 2f, Count = 3, EnemyId = EnemyNineTail, SkillId = 196 },
                new EnemyAbilityDef { Kind = AbilityKind.Teleport, Aim = BossAim.Random, Cooldown = 12f, FirstAfter = 8f, Delay = 1f, Width = 3f, SkillId = 26 },
                new EnemyAbilityDef { Kind = AbilityKind.Cone, Shape = AbilityShape.Fan, Aim = BossAim.Nearest, Cooldown = 7f, FirstAfter = 4f, Delay = 1f, Range = 12f, Arc = 70f, Count = 7, Speed = 6f, Damage = 14, SkillId = 19 },
            };
            moon.Drops = BossDrops();
            c.Enemies.Add(moon);
        }

        static WaveDef Wave(float from, float to, int enemy, float interval, int count, SpawnPattern pattern = SpawnPattern.Ring, int maxAlive = 0)
        {
            return new WaveDef { From = from, To = to, EnemyId = enemy, Interval = interval, Count = count, Pattern = pattern, MaxAlive = maxAlive };
        }

        /// <summary>One elite or boss at its mark (the engine's Once: forced, never skipped for MaxEnemies, exactly Count = 1
        /// whatever the player count), announced warnSeconds before it comes (HordeEventKind.Incoming): 5 s for an elite,
        /// 8 s for a boss (Loki's own; the final boss: RulesDef.FinalBossWarnSeconds 10).</summary>
        static WaveDef Once(float at, int enemy, float warnSeconds)
        {
            return new WaveDef { From = at, To = at + 1f, EnemyId = enemy, Count = 1, Pattern = SpawnPattern.Ring, Once = true, WarnSeconds = warnSeconds };
        }

        const float WarnElite = 5f, WarnBoss = 8f;

        /// <summary>A group of normal enemies at one moment (a closed circle, a sweeping pack): a one-spawn wave, so the
        /// count is honoured and scaled by the player count (the engine's Once spawns exactly one, whatever Count says).</summary>
        static WaveDef Group(float at, int enemy, int count, SpawnPattern pattern)
        {
            return new WaveDef { From = at, To = at + 0.5f, EnemyId = enemy, Interval = 1f, Count = count, Pattern = pattern };
        }

        static void AddWaves(HordeContent c)
        {
            // Seconds of the 900 s reference run; counts are for ONE player (Scaling.SpawnMul scales them).
            // THE PULSE (Loki's own): pressure peaks "P" of 25-30 s, breathers "B" of 25-30 s with a thin trickle (the field
            // is never empty, so there is always EXP to gather), elites at the end of a peak, bosses after a breather. The
            // chart per 10 s: `dotnet run --project client/tools/w8content/HordeBalance -- pulse`.

            // ---- 0:00-3:55 THE PLAINS (Water / Earth / Wind plants and insects: Fire, Wind and Poison shine)
            // opening: a small closed circle walking in from 9 cells, so the first kill comes within seconds and level 2 by ~0:12
            c.Waves.Add(Group(0, EnemyPoring, 5, SpawnPattern.Circle));
            c.Waves.Add(Wave(3, 30, EnemyPoring, 1.2f, 1));
            // P1 0:30-0:55 Porings thick + the first fast chasers
            c.Waves.Add(Wave(30, 55, EnemyPoring, 0.8f, 1));
            c.Waves.Add(Wave(30, 55, EnemyLunatic, 1f, 1));
            // B 0:55-1:20
            c.Waves.Add(Wave(55, 80, EnemyPoring, 2.5f, 1));
            c.Waves.Add(Wave(55, 80, EnemyLunatic, 4f, 1));
            // P2 1:20-1:50 the Fabre swarm, Chonchon packs sweeping through twice
            c.Waves.Add(Wave(80, 110, EnemyFabre, 0.6f, 1));
            c.Waves.Add(Group(85, EnemyChonchon, 6, SpawnPattern.Cluster));
            c.Waves.Add(Group(100, EnemyChonchon, 6, SpawnPattern.Cluster));
            // B 1:50-2:15
            c.Waves.Add(Wave(110, 135, EnemyFabre, 2.5f, 1));
            // P3 2:15-2:45 Spores and Thief Bugs, a Chonchon pack
            c.Waves.Add(Wave(135, 165, EnemySpore, 1f, 1));
            c.Waves.Add(Wave(135, 165, EnemyThiefBug, 1.5f, 1));
            c.Waves.Add(Group(150, EnemyChonchon, 8, SpawnPattern.Cluster));
            // B 2:45-3:05
            c.Waves.Add(Wave(165, 185, EnemySpore, 3f, 1));
            c.Waves.Add(Wave(165, 185, EnemyThiefBug, 4f, 1));
            // P4 3:05-3:35 Thief Bugs thick; the first ELITE at 3:30 (announced 5 s before: WarnSeconds, when the engine has it)
            c.Waves.Add(Wave(185, 215, EnemyThiefBug, 1f, 1));
            c.Waves.Add(Wave(185, 215, EnemySpore, 2f, 1));
            c.Waves.Add(Once(210, EnemyEclipse, WarnElite));
            // B 3:35-4:00 (the elite and its Lunatics are the pressure)
            c.Waves.Add(Wave(215, 240, EnemyThiefBug, 3f, 1));
            c.Waves.Add(Wave(215, 240, EnemySpore, 4f, 1));

            // ---- 4:00-7:30 THE UNDEAD (Holy, Fire and Demon Bane)
            // P5 4:00-4:30 Zombies and Skeletons
            c.Waves.Add(Wave(240, 270, EnemyZombie, 1.2f, 1));
            c.Waves.Add(Wave(240, 270, EnemySkeleton, 1.8f, 1));
            // B 4:30-4:55; Archer Skeletons from the edge from here to 9:20, at most 4 standing
            c.Waves.Add(Wave(270, 295, EnemyZombie, 3f, 1));
            c.Waves.Add(Wave(270, 560, EnemyArcherSkeleton, 10f, 1, SpawnPattern.Edge, 4));
            // P6 4:55-5:25 Skeletons thick, a closed circle of 12 Zombies at 5:05
            c.Waves.Add(Wave(295, 325, EnemySkeleton, 0.8f, 1));
            c.Waves.Add(Wave(295, 325, EnemyZombie, 1.5f, 1));
            c.Waves.Add(Group(305, EnemyZombie, 12, SpawnPattern.Circle));
            // B 5:25-5:40, then the first BOSS at 5:40 (announced 8 s before), a trickle while it is fought
            c.Waves.Add(Wave(325, 345, EnemySkeleton, 3f, 1));
            c.Waves.Add(Once(340, EnemyOrkHero, WarnBoss));
            c.Waves.Add(Wave(345, 390, EnemyZombie, 2.5f, 1));
            // P7 6:30-7:00 the dead come again, a second circle of 10 Zombies at 6:45
            c.Waves.Add(Wave(390, 420, EnemySkeleton, 1f, 1));
            c.Waves.Add(Wave(390, 420, EnemyZombie, 1.4f, 1));
            c.Waves.Add(Group(405, EnemyZombie, 10, SpawnPattern.Circle));
            // B 7:00-7:30
            c.Waves.Add(Wave(420, 450, EnemyZombie, 3f, 1));

            // ---- 7:30-10:45 THE WILD (Brute packs: Beast Bane; Poison Spores that burst; Orcs)
            // P8 7:30-8:00 Wolf packs and Poison Spores; the second ELITE at 8:00 (announced 5 s before)
            c.Waves.Add(Wave(450, 480, EnemyWolf, 2.5f, 3, SpawnPattern.Cluster));
            c.Waves.Add(Wave(450, 480, EnemyPoisonSpore, 1.2f, 1));
            c.Waves.Add(Once(480, EnemyVagabondWolf, WarnElite));
            // B 8:00-8:25 (the elite and its wolves are the pressure)
            c.Waves.Add(Wave(480, 505, EnemyPoisonSpore, 3f, 1));
            // P9 8:25-8:55 Wolf packs thick, Hunter Fly packs sweeping twice
            c.Waves.Add(Wave(505, 535, EnemyWolf, 2f, 3, SpawnPattern.Cluster));
            c.Waves.Add(Wave(505, 535, EnemyPoisonSpore, 1.5f, 1));
            c.Waves.Add(Group(510, EnemyHunterFly, 6, SpawnPattern.Cluster));
            c.Waves.Add(Group(525, EnemyHunterFly, 6, SpawnPattern.Cluster));
            // B 8:55-9:20
            c.Waves.Add(Wave(535, 560, EnemyWolf, 6f, 2, SpawnPattern.Cluster));
            // P10 9:20-9:50 the Orcs
            c.Waves.Add(Wave(560, 590, EnemyOrkWarrior, 0.7f, 1));
            c.Waves.Add(Wave(560, 590, EnemyPoisonSpore, 2f, 1));
            c.Waves.Add(Group(575, EnemyHunterFly, 8, SpawnPattern.Cluster));
            // B 9:50-10:00, then the second BOSS at 10:00 (announced 8 s before), a trickle while it is fought
            c.Waves.Add(Wave(590, 600, EnemyOrkWarrior, 3f, 1));
            c.Waves.Add(Once(600, EnemyOsiris, WarnBoss));
            c.Waves.Add(Wave(600, 645, EnemyOrkWarrior, 2.5f, 1));

            // ---- 10:45-13:30 THE TOMB (Munaks and Isis: Holy and Demon Bane again)
            // P11 10:45-11:15
            c.Waves.Add(Wave(645, 675, EnemyMunak, 1f, 1));
            c.Waves.Add(Wave(645, 675, EnemyOrkWarrior, 1.5f, 1));
            // B 11:15-11:40
            c.Waves.Add(Wave(675, 700, EnemyMunak, 3f, 1));
            // P12 11:40-12:10 Munaks thick, a closed circle of 14 at 11:52, a Hunter Fly pack
            c.Waves.Add(Wave(700, 730, EnemyMunak, 0.8f, 1));
            c.Waves.Add(Group(712, EnemyMunak, 10, SpawnPattern.Circle));
            c.Waves.Add(Group(720, EnemyHunterFly, 8, SpawnPattern.Cluster));
            // B 12:10-12:35
            c.Waves.Add(Wave(730, 755, EnemyMunak, 3f, 1));
            c.Waves.Add(Wave(730, 755, EnemyIsis, 5f, 1));
            // P13 12:35-13:05 Isis joins
            c.Waves.Add(Wave(755, 785, EnemyIsis, 1.4f, 1));
            c.Waves.Add(Wave(755, 785, EnemyMunak, 1.4f, 1));
            // B 13:05-13:30
            c.Waves.Add(Wave(785, 810, EnemyIsis, 3f, 1));

            // ---- 13:30-15:00 THE GATE (Demons; Ghost resists Neutral: Holy carries), then the final boss at 15:00
            // P14 13:30-14:00 Nightmares charge, Isis, a Hunter Fly pack
            c.Waves.Add(Wave(810, 840, EnemyNightmare, 3f, 2, SpawnPattern.Cluster));
            c.Waves.Add(Wave(810, 840, EnemyIsis, 1.4f, 1));
            c.Waves.Add(Group(825, EnemyHunterFly, 8, SpawnPattern.Cluster));
            // B 14:00-14:25
            c.Waves.Add(Wave(840, 865, EnemyIsis, 3f, 1));
            c.Waves.Add(Wave(840, 865, EnemyNightmare, 6f, 1, SpawnPattern.Cluster));
            // P15 14:25-14:55 the last peak: Isis thick, Nightmare pairs, a closed circle of 12 Isis at 14:40
            c.Waves.Add(Wave(865, 895, EnemyIsis, 1f, 1));
            c.Waves.Add(Wave(865, 895, EnemyNightmare, 3.5f, 2, SpawnPattern.Cluster));
            c.Waves.Add(Group(880, EnemyIsis, 8, SpawnPattern.Circle));
            // B 14:55-15:00 five seconds of nothing, then the FINAL BOSS (Rules.FinalBossEnemyId at 900 s; announced 10 s
            // before when the engine has FinalBossWarnSeconds). The waves stop at 15:00: the fight is the boss and its adds.
        }

        static void AddPickups(HordeContent c)
        {
            // Drawn with these items' sprites from the player's install (db/re/item_db_etc.yml / item_db_usable.yml ids).
            c.Pickups.Add(new PickupDef { Id = PickExpS, Key = "exp_small", Kind = PickupKind.Exp, ItemId = 717, Value = 1 });    // Blue_Gemstone
            c.Pickups.Add(new PickupDef { Id = PickExpM, Key = "exp_medium", Kind = PickupKind.Exp, ItemId = 715, Value = 5 });   // Yellow_Gemstone
            c.Pickups.Add(new PickupDef { Id = PickExpL, Key = "exp_large", Kind = PickupKind.Exp, ItemId = 716, Value = 25 });   // Red_Gemstone
            c.Pickups.Add(new PickupDef { Id = PickHeal, Key = "heal", Kind = PickupKind.Heal, ItemId = 501, Value = 20 });       // Red_Potion
            c.Pickups.Add(new PickupDef { Id = PickHealBig, Key = "heal_big", Kind = PickupKind.Heal, ItemId = 504, Value = 50 }); // White_Potion
            c.Pickups.Add(new PickupDef { Id = PickMagnet, Key = "magnet", Kind = PickupKind.Magnet, ItemId = 12210 });           // Bubble_Gum (its look)
            c.Pickups.Add(new PickupDef { Id = PickBomb, Key = "bomb", Kind = PickupKind.Bomb, ItemId = 7135, Value = 120, Area = 5f }); // Fire_Bottle (AM_DEMONSTRATION's item)
            c.Pickups.Add(new PickupDef { Id = PickChest, Key = "chest", Kind = PickupKind.Chest, ItemId = 603, Value = 1 });     // Old_Blue_Box
            c.Pickups.Add(new PickupDef { Id = PickChestBig, Key = "chest_big", Kind = PickupKind.Chest, ItemId = 617, Value = 3 }); // Old_Violet_Box
        }
    }
}
