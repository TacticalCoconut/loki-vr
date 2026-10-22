#nullable disable
using System;
using System.Collections.Generic;

namespace Loki.Horde.Content
{
    // ====================================================================================================================
    // Horde (client wave 8): WHAT IS IN THE GAME (w8content). The engine's types are in ../HordeContent.cs (w8core).
    //
    // EVERY NUMBER IN THESE TABLES IS LOKI'S OWN DESIGN: damage, HP, cooldowns, areas, EXP, waves, scaling, score and
    // reward are the mode's own, tuned for a 15-minute co-op bullet-heaven run. None of them is an official Ragnarok value
    // and none may be presented as one.
    //
    // What refers to Ragnarok does so by rAthena's ids only, checked against rAthena's database (C:\rAthena,
    // read-only): job ids (src/common/mmo.hpp e_job), skill ids (db/re/skill_db.yml + db/re/skill_tree.yml), mob ids
    // (db/re/mob_db.yml), item ids (db/re/item_db_*.yml). The RO facts each entry relies on (which job has the skill, its
    // max level, element, what it does) are quoted in the comments with their source; names, sprites, icons and sounds
    // come from the player's own install at run time, never from this repository.
    //
    // The design document is design notes (the job tree, every mapping, the waves, the balance numbers) and,
    // for the second pass (client wave 9, h9content: pacing, the waves' pulse, movement and weapon feel), design notes client/tools/w8content/check_content.py checks every id / AegisName here against rAthena's database.
    // ====================================================================================================================
    public static partial class HordeTables
    {
        /// <summary>Builds the whole content of a run. The engine never changes it; both the host and the client build it.</summary>
        public static HordeContent Build()
        {
            var c = new HordeContent();
            c.Rules = Rules();
            c.Scaling = Scaling();
            c.Score = Score();
            c.Reward = Reward();
            c.Stats.AddRange(Stats());
            c.ElementPct = ElementTable();
            AddJobs(c);
            AddEnemies(c);
            BoostNormalEnemies(c);
            AddElitesW18(c); // client wave 18 (h18rules): the elites that come now and then (HordeElites.cs)
            AddWaves(c);
            AddPickups(c);
            AddArenas(c);
            c.Rules.FinalBossEnemyId = EnemyBaphomet;
            AddPassiveProcs(c); // client wave 17 (h17pass): every passive's body and trigger, by id (HordePassiveProcs.cs); keep it LAST
            AddTranscendW18(c); // client wave 18 (h18trans): every weapon's two transcendence branches and every passive's transcended form (HordeTranscend.cs)
            return c;
        }

        /// <summary>Base normal enemies are stronger (+45% HP, +35% damage).</summary>
        static void BoostNormalEnemies(HordeContent c)
        {
            foreach (var e in c.Enemies)
            {
                if (e == null || e.Rank != EnemyRank.Normal) continue;
                e.Hp = Math.Max(e.Hp + 6, (int)Math.Round(e.Hp * 1.45));
                e.Damage = Math.Max(e.Damage + 2, (int)Math.Round(e.Damage * 1.35));
                if (e.ShotDamage > 0) e.ShotDamage = Math.Max(e.ShotDamage + 2, (int)Math.Round(e.ShotDamage * 1.35));
            }
        }

        // ---------------------------------------------------------------- the run character, levels, jobs (Loki's own)
        static RulesDef Rules()
        {
            return new RulesDef
            {
                ReferenceRunSeconds = 900f,
                FinalBossSeconds = 120f,
                // Wave 9: the final boss is announced 10 s before it comes (HordeEventKind.Incoming); elites 5 s, bosses 8 s
                // (WaveDef.WarnSeconds in HordeEnemies.AddWaves).
                FinalBossWarnSeconds = 10f,
                // client wave 18 (h18rules), the design's point 29 ("increase enemy density by about 40%"): 300 -> 420 with the spawns (x 1.4 below);
                // it never bound in the bots' runs (HordeBalance density: at most 153 alive, 8 bots), it keeps the same room above the field
                MaxEnemies = 420,
                StartHp = 170,
                // Wave 9: a run gives ~28 levels instead of ~43 (the slower, pulsed level curve below), so each level
                // gives more HP: 170 + 6 x 27 = 332 at 15:00 (wave 8: 170 + 4 x 42 = 338).
                HpPerLevel = 6,
                MoveSpeed = 4f,
                BodyRadius = 0.4f,
                // Wave 9: the magnet grows with the run level: 2.0 cells + 0.08 a level (2.3 at level 5, 3.1 at 15, 4.2 at 28),
                // the percent passives (Enlarge Weight Limit, Greed, the Hunter's) on top.
                PickupRadius = 2f,
                PickupRadiusPerLevel = 0.08f,
                HurtGraceSeconds = 0.4f,
                CritChance = 5f,
                CritDamagePct = 150f,
                StartWeapon = WNvKnife,
                MaxWeapons = 6,
                MaxPassives = 6,
                // Run EXP from level n+1 to n+2. WAVE 9 PACING (Loki's own; design notes "Pacing"): level 2 at
                // ~0:12, level 5 at ~1:00, then a level every 20-30 s to level 15 (the second job, ~5:10), 30-40 s to level
                // 23 (10:00), flattening to 50-70 s to level 28 (15:00). The table is FITTED to the solo bots' measured EXP
                // intake with these waves (client/tools/w8content/fit_exp.py from HordeBalance's "curve"); wave 8's table
                // gave level 2 at 0:38 (nothing to kill arrived earlier) and then a level every 16-20 s (43 levels a run).
                ExpTable = new[] { 9, 9, 22, 32, 32, 32, 36, 54, 75, 75, 85, 115, 135, 135, 180, 210, 210, 210, 210, 280, 280, 280, 340, 400, 440, 480, 520 },
                ExpGrowthPct = 10f,
                MaxLevel = 99,
                // Wave 8's share (the taker the whole pickup, the others half): NOT READ since 2026-10-05 (design note: one EXP bar for the
                // party); kept for the stamp of older tables.
                ExpSharePct = 50f,
                // One party pool and one party level (design note, 2026-10-05): the party's next level costs ExpTable[level] x
                // PartyExpScale[players - 1]. MEASURED by h9content (HordeBalance -- pool 3, design notes "Pacing"):
                // the pickups of 2 / 4 / 8 bots add up to 1.51x / 2.53x / 4.28x a solo player's (kills follow the spawn
                // scaling 0.55 n + 0.45, not the player count), so "x players" would level a four-party at 65 % of solo's rate.
                // These numbers keep every party size on the solo curve (level 2 at ~0:12, 5 at ~1:00, a level every 20-40 s).
                // client wave 18 (h18rules), point 29: x 1.4 with the spawns (wave 17: 1, 1.55, 2.1, 2.65, 3.2, 3.75, 4.3, 4.8), so 40 % more
                // enemies do not level the party 40 % faster: the pacing above stays (HordeBalance: level 5 at 56 s, 15 at 323 s;
                // wave 17: 55 s, 318 s). Written out, never computed (the content stamp).
                // client wave 18 (h18rules), point 31: every player a whole solo share of enemies (SpawnMul), so the party's pickups grow
                // with it: HordeBalance pool (2 seeds) measured 1.92 / 3.70 / 7.40 x a solo player's for 2 / 4 / 8 bots (with point 29's
                // spawns: 1.57 / 2.64 / 4.45, its PartyExpScale 1.4, 2.17, 2.94, 3.71, 4.48, 5.25, 6.02, 6.72); x 1.4 (point 29), the sizes
                // between linear. The closeness groups' extra enemies drop no
                // extra EXP (HordeSim.Rules.cs ExpOf), so the pacing holds wherever the party stands.
                // client wave 19 (h18rules), the solo retune (design note: "Retune it harder"): solo 1.4 -> 1.9 with SpawnMul[0] (the same factor:
                // the solo pacing kept, level 5 / 15 at 56 / 315 s); parties unchanged. Measured: design notes "Solo retune".
                PartyExpScale = new[] { 1.9f, 2.69f, 3.93f, 5.18f, 6.48f, 7.77f, 9.07f, 10.36f },
                // client wave 18 (h18rules), the design's point 31: a group of normal enemies is larger for every other standing player within
                // 10 cells of the player it comes to (one more share for an ally on top of it, half for one 5 cells away: HordeSim.Rules.cs)
                ClusterSpawnCells = 10f,
                // client wave 18 (h18rules), point 31, design note: "Yes, cap the closeness bit": at most one extra share (n stacked: 2 n shares)
                ClusterSpawnMaxShares = 1f,
                CardsPerOffer = 3,
                ChoiceSeconds = 30f,
                // First job at run level 5 (~1:00), second job at 15 (~5:10 on the wave 9 curve; it was 20 at ~5:15 on wave
                // 8's faster curve). Tiers 2+ have no Next in this version (transcendent, third and fourth classes are left
                // for later), so the later marks are never reached as job changes.
                JobMarks = new[] { 5, 15, 30, 45, 60 },
                WeightNewWeapon = 100,
                WeightWeaponLevel = 120,
                WeightNewPassive = 80,
                WeightPassiveLevel = 80,
                WeightStat = 40,
                // Fewer cards a run (wave 9), so a stat card is worth 3 points instead of 2.
                StatCardPoints = 3,
                ReviveSeconds = 12f,
                ReviveRadius = 3f,
                ReviveNearMul = 3f,
                ReviveHpPct = 50f,
                ReviveSafeSeconds = 2f,
            };
        }

        // ---------------------------------------------------------------- 1 to 8 players (Loki's own)
        static ScalingDef Scaling()
        {
            return new ScalingDef
            {
                // More enemies, a little more HP each: n players deal ~n times the damage, the horde grows ~0.55 n + 0.45.
                // client wave 18 (h18rules), the design's point 29 ("increase enemy density by about 40%"): every wave's spawns x 1.4 (wave 9's
                // 1, 1.6, 2.2, 2.8, 3.3, 3.8, 4.3, 4.8 for 1-8 players, each x 1.4: the party scaling's ratios kept). Written out, never
                // computed (the content stamp: COORD h17cards 02:10). Measured: design notes "Point 29".
                // client wave 18 (h18rules), the design's point 31 ("The amount of enemies on horde mode needs to be increased for each player that
                // is present"): each player brings a whole solo share, n x 1.4 (was 1.4, 2.24, 3.08, 3.92, 4.62, 5.32, 6.02, 6.72 just above).
                // client wave 19 (h18rules), the solo retune (design note: "Retune it harder"; the elites had made solo easier: bots won 28 of 52):
                // ALONE 1.4 -> 1.9 (+36 % enemies; the EXP requirement with it), parties n x 1.4 unchanged. HordeBalance, 520 solo runs: 50 % ->
                // 32 % won (wave 17: 31 %); design notes "Solo retune".
                SpawnMul = new[] { 1.9f, 2.8f, 4.2f, 5.6f, 7f, 8.4f, 9.8f, 11.2f },
                // Wave 9, one party level (2026-10-05): a party now levels at the solo curve (wave 8's shared pickups gave a
                // four-party 1.3x solo's levels, eight 1.5x), so every player has fewer cards than before and the per-player
                // enemy HP / damage scaling is 8-10 % lower than wave 8's for 3+ players (measured: the healer-less four-party
                // and the eight-party wiped on wave 8's values).
                EnemyHpMul = new[] { 1f, 1.12f, 1.24f, 1.36f, 1.48f, 1.6f, 1.72f, 1.84f },
                // Elites and bosses: every player can hit them, but in a crowd most shots go to the nearer adds (measured with the bots),
                // so their HP grows slower than the party's damage.
                EliteHpMul = new[] { 1f, 1.4f, 1.8f, 2.2f, 2.6f, 3f, 3.4f, 3.8f },
                BossHpMul = new[] { 1f, 1.4f, 1.8f, 2.1f, 2.4f, 2.7f, 3f, 3.2f },
                EnemyDamageMul = new[] { 1f, 1.05f, 1.1f, 1.15f, 1.2f, 1.25f, 1.3f, 1.35f },
                // Wave 9: the slower level curve (~28 levels a run instead of ~43) means less player power late, so the
                // enemies grow slower too (wave 8: 10 % HP and 5 % damage a minute). Measured with the bots.
                EnemyHpPctPerMinute = 6f,
                EnemyDamagePctPerMinute = 3f,
            };
        }

        // ---------------------------------------------------------------- score (Loki's own)
        static ScoreDef Score()
        {
            return new ScoreDef
            {
                PerKill = 1,
                PerEliteKill = 25,
                PerBossKill = 150,
                PerRunLevel = 10,
                PerJobTier = 60,
                PerDamage1000 = 1,
                // Healing and raising allies count as much as killing: support jobs must be worth taking in co-op.
                PerHeal100 = 3,
                PerRevive = 40,
                PerSecond = 1,
                Victory = 600,
            };
        }

        // ---------------------------------------------------------------- the real character's reward (Loki's own)
        static RewardDef Reward()
        {
            // Item 56 (the design's playtest list, 2026-10-12): increased EXP rewards that scale with both survival time and enemies killed
            // (and continue scaling past 15:00 in endless mode up to MaxShare). Reference 15-minute run with standard kills:
            // Low 10 %, Normal 20 %, High 35 % of a level.
            return new RewardDef
            {
                SecondsForFull = 900f,
                ScoreForFull = 3000,
                MinSeconds = 60f,
                BaseCapPct = new[] { 0f, 10f, 20f, 35f },
                JobCapPct = new[] { 0f, 10f, 20f, 35f },
            };
        }

        // ---------------------------------------------------------------- RO's six stats (Loki's own worth per point)
        static IEnumerable<StatDef> Stats()
        {
            // What each stat does in Ragnarok (status.cpp: STR -> melee ATK, AGI -> ASPD and Flee, VIT -> Max HP, DEF and HP
            // recovery, INT -> MATK and heals, DEX -> ranged ATK, HIT and cast time, LUK -> critical and perfect dodge) decides
            // WHICH modifier a point gives; how much it gives is the mode's own.
            yield return new StatDef { Stat = Stat.Str, PerPoint = Ms(M(Mod.MeleeDamagePct, 2.5f)) };
            yield return new StatDef { Stat = Stat.Agi, PerPoint = Ms(M(Mod.CooldownPct, -0.7f), M(Mod.Dodge, 0.3f)) };
            yield return new StatDef { Stat = Stat.Vit, PerPoint = Ms(M(Mod.MaxHp, 6f), M(Mod.Regen, 0.05f), M(Mod.DamageTakenPct, -0.3f)) };
            yield return new StatDef { Stat = Stat.Int, PerPoint = Ms(M(Mod.MagicDamagePct, 2.5f), M(Mod.HealPowerPct, 2f)) };
            yield return new StatDef { Stat = Stat.Dex, PerPoint = Ms(M(Mod.RangedDamagePct, 2.5f), M(Mod.CooldownPct, -0.3f)) };
            yield return new StatDef { Stat = Stat.Luk, PerPoint = Ms(M(Mod.CritChance, 0.5f), M(Mod.Dodge, 0.1f), M(Mod.CritDamagePct, 1f)) };
        }

        // ---------------------------------------------------------------- elements
        /// <summary>
        /// ElementPct[weapon element, enemy element]. The RELATIONS are Ragnarok's (rAthena db/re/attr_fix.yml, level 1:
        /// which element is strong or weak against which); the VALUES are softened for a horde game (Loki's own): RO's 25 %
        /// becomes 50 % and RO's 0 % (immune) becomes 25 %, so no weapon is ever useless against a wave; 75 / 90 / 100 / 125 /
        /// 150 are kept.
        /// </summary>
        static int[,] ElementTable()
        {
            // rows: attacker Neutral Water Earth Fire Wind Poison Holy Dark Ghost Undead; columns: defender, same order.
            int[,] ro =
            {
                { 100, 100, 100, 100, 100, 100, 100, 100,  90, 100 },
                { 100,  25, 100, 150,  90, 150, 100, 100, 100, 100 },
                { 100, 100,  25,  90, 150, 150, 100, 100, 100, 100 },
                { 100,  90, 150,  25, 100, 150, 100, 100, 100, 125 },
                { 100, 150,  90, 100,  25, 150, 100, 100, 100, 100 },
                { 100, 150, 150, 150, 150,   0,  75,  75,  75,  75 },
                { 100, 100, 100, 100, 100,  75,   0, 125, 100, 125 },
                { 100, 100, 100, 100, 100,  75, 125,   0, 100,   0 },
                {  90, 100, 100, 100, 100,  75,  90,  90, 125, 100 },
                { 100, 100, 100,  90, 100,  75, 125,   0, 100,   0 },
            };
            var t = new int[10, 10];
            for (int a = 0; a < 10; a++)
                for (int d = 0; d < 10; d++)
                {
                    int v = ro[a, d];
                    t[a, d] = v == 0 ? 25 : v == 25 ? 50 : v;
                }
            return t;
        }

        // ---------------------------------------------------------------- arenas
        static void AddArenas(HordeContent c)
        {
            // Base arenas quadrupled in area (2x width and 2x height), and scaled by +20% per additional party member in ArenaDef.ScaledForParty.
            c.Arenas.Add(new ArenaDef { Id = 1, Map = "prt_fild08", X0 = 204, Y0 = 20, X1 = 332, Y1 = 148 });
            c.Arenas.Add(new ArenaDef { Id = 2, Map = "yuno_fild01", X0 = 172, Y0 = 20, X1 = 282, Y1 = 130 });
            c.Arenas.Add(new ArenaDef { Id = 3, Map = "moc_fild07", X0 = 262, Y0 = 53, X1 = 356, Y1 = 147 });
        }

        // ---------------------------------------------------------------- helpers (plain data builders)
        static ModDef M(Mod kind, float value, int arg = 0) { return new ModDef(kind, value, arg); }
        static ModDef[] Ms(params ModDef[] mods) { return mods; }
        static EffectDef Fx(EffectKind kind, float chance, float duration, float power = 0f, float tick = 1f) { return new EffectDef(kind, chance, duration, power, tick); }
        static EffectDef[] Fxs(params EffectDef[] fx) { return fx; }

        /// <summary>n levels made by f(level index 0..n-1).</summary>
        static WeaponLevel[] Lv(int n, Func<int, WeaponLevel> f)
        {
            var a = new WeaponLevel[n];
            for (int i = 0; i < n; i++) a[i] = f(i);
            return a;
        }

        static ModDef[][] PLv(int n, Func<int, ModDef[]> f)
        {
            var a = new ModDef[n][];
            for (int i = 0; i < n; i++) a[i] = f(i);
            return a;
        }

        static WeaponDef W(int id, string key, int skillId, WeaponKind kind, Targeting target, DamageClass cls, Element element, WeaponLevel[] levels, int weight = 100)
        {
            return new WeaponDef { Id = id, Key = key, SkillId = skillId, Kind = kind, Target = target, Class = cls, Element = element, Levels = levels, Weight = weight };
        }

        static PassiveDef P(int id, string key, int skillId, int itemId, ModDef[][] levels, int weight = 100)
        {
            return new PassiveDef { Id = id, Key = key, SkillId = skillId, ItemId = itemId, Levels = levels, Weight = weight };
        }
    }
}
