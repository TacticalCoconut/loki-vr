using System;
using System.Collections.Generic;
using System.Text;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 26 (w26skill), the design's item 25 of wave 23 (answer 57, 2026-10-12): "On the preview animations, for passive such as
    /// "Increases dodge rate" or "Increases crit chance", exemplify those by having the character dodge or crit a couple of times in the
    /// preview. On other previews that you currently just have the character sitting around with a buff symbol around them, find ways to
    /// exemplify the skill's gains". WHAT A PASSIVE OR A BUFF GAINS THE PLAYER, with BEFORE / AFTER numbers (the skill tree's preview,
    /// UI/SkillDiorama.cs, writes them over its scene).
    ///   The table  ONE row per skill (its AEGIS name), every amount read in rAthena's own code (e985006, Renewal) with its lines in the
    ///              row's comment and in design notes; nothing guessed. A skill with no row shows no gain (its scene as before).
    ///   A line     a stat and its amount at a level. BEFORE → AFTER when the amount is ADDED to a number the status page shows (STR..LUK,
    ///              HIT, FLEE, CRIT in whole points, the weight limit) and nothing the client cannot see scales it; "+N" / "+N%" otherwise
    ///              (a mastery's ATK is added per hit in battle.cpp, never to the status page's ATK; Max HP's fixed bonus is scaled by
    ///              the % bonuses after it; a % amount). A condition is named: "ATK +40 (dagger, sword)".
    ///   The level  the skill as the player has it: the learned level (a buff: the level its cast uses), or 1 when it is not learned.
    ///   Before     a passive not learned: before = the stat now, after = now + the amount at level 1. A passive learned at L: the stat
    ///              now holds it: before = now - amount(L), after = now ("without it → with it"). A buff: before = now, after = now +
    ///              amount; its status icon ON the player now: the stat already holds a cast of it (maybe another player's, at another
    ///              level): "+N" only.
    /// Nothing here is rAthena-specific code: it is the player's own numbers and a table; it would read the same from the SpacetimeDB
    /// module, which sends the same status packets.
    /// </summary>
    public static class SkillGains
    {
        /// <summary>What the preview's characters do to show the gain (UI/SkillDiorama.cs): the Poring lunges and the character
        /// side-steps ("Miss"); the character strikes criticals; slow swings, then quick ones; a swing that misses, then hits.</summary>
        public enum Demo { None, Dodge, Crit, Speed, Accuracy }
        public enum Stat { Str, Agi, Vit, Int, Dex, Luk, Hit, Flee, Crit, Weight, Atk, Matk, Def, Mdef, MaxHp, Damage, DoubleHit, Block, Range, AttackSpeed, AllStats, StrIntDex, HolyResist, FireResist, NeutralResist, AgiPct, DexPct, HpRegen, SpRegen,
            // wfollow (2026-10-14): the third / fourth / expanded / transcendent jobs' rows
            Pow, Sta, Wis, Spl, Con, Crt, PAtk, SMatk, Res, MRes, HPlus, CRate, MaxSp, MaxHpPct, MaxSpPct, AtkPct, MatkPct, MdefPct, WeightPct,
            CartWeight, SpCost, AllStatsPct, StaWisSpl, PowConCrt, DamagePerHit, PAtkSMatk }

        /// <summary>The character as the client knows it now (Game/Progression.cs, Game/PlayerState.cs, the hands' weapon).</summary>
        public sealed class Who
        {
            /// <summary>STR, AGI, VIT, INT, DEX, LUK as the status page shows them (base + bonus).</summary>
            public readonly int[] Stats = new int[6];
            public int Hit, Flee, Crit, BaseLevel = 1, Job;
            /// <summary>The weight limit as the status page shows it (rAthena's max_weight / 10).</summary>
            public int WeightLimit;
            public long MaxHp = 1, MaxSp = 1;
            // wfollow: the job level (a few buffs use it), the trait stats POW..CRT as the status page shows them (base + bonus) and the
            // numbers made of them (Progression 0x00b0 vars 225-230), the weapon in the right hand (rAthena W_* type, 0 bare hands), a weapon
            // in the left hand (-1 none), a shield
            public int JobLevel = 1;
            public readonly int[] Traits = new int[6];
            public int PAtk, SMatk, Res, MRes, HPlus, CRate;
            public int Right, Left = -1; public bool Shield;
            /// <summary>wfollow (item 4): the status page's ASPD now (the server's: (2000 - amotion) / 10; 0 unknown), and whether the character
            /// rides (a Peco, a dragon, a warg, a Mado Gear: rAthena's riding penalties are not modelled here).</summary>
            public int Aspd; public bool Riding;
            /// <summary>A skill's learned level (0: not learned), the level a cast of it uses, whether a status icon (EFST) is on the player.</summary>
            public Func<int, int> Learned = s => 0, CastLevel = s => 0;
            public Func<int, bool> IconOn = e => false;
        }

        /// <summary>An amount of a row at a level: the stat, the amount (CRIT in tenths, as rAthena keeps it), whether it can be shown
        /// as before → after, its condition (a Lang key; null: none).</summary>
        public struct Amount
        {
            public Stat Stat; public int Value; public bool Shown; public string Cond;
            /// <summary>wfollow (item 4): an attack-speed line's part of rAthena's formula (Game/AspdModel.cs): the fixed bonus kind (7, 6, 20
            /// or 15: status_calc_aspd fixed), the percent (status_calc_aspd percent), whole ASPD added after (status_calc_fix_aspd), and a
            /// passive's val (status_base_amotion_pc).</summary>
            public int AspdFix, AspdPct, AspdFlat, AspdVal;
        }

        public sealed class Row
        {
            public string Aegis = ""; public bool Buff; public int Icon = -1; public Demo Demo;
            /// <summary>The amounts at a level for this character.</summary>
            public Func<int, Who, List<Amount>> At;
            /// <summary>The rAthena lines the amounts come from (the doc's table; the log).</summary>
            public string Source = "";
        }

        /// <summary>A line of the caption: its stat, before / after (when <see cref="BeforeAfter"/>), the amount (CRIT in tenths), its text.</summary>
        public struct Line { public Stat Stat; public bool BeforeAfter; public int Before, After, Value; public string Cond, Text, Main, CondText; }

        /// <summary>What the preview shows for a skill.</summary>
        public sealed class Gain
        {
            public int Skill, Level; public string Aegis = "";
            public bool Buff, Learned, IconOn; public Demo Demo;
            public readonly List<Line> Lines = new List<Line>();
            public string Source = "";
        }

        // ------------------------------------------------------------------ the table
        private static Amount A(Stat s, int v, bool shown = false, string cond = null) => new Amount { Stat = s, Value = v, Shown = shown, Cond = cond };
        /// <summary>wfollow (item 4): an attack-speed line with its part of the formula.</summary>
        private static Amount Asp(int fix = 0, int pct = 0, int flat = 0, int val = 0, string cond = null) => new Amount { Stat = Stat.AttackSpeed, Cond = cond, AspdFix = fix, AspdPct = pct, AspdFlat = flat, AspdVal = val };
        private static List<Amount> L(params Amount[] a) => new List<Amount>(a);
        private static Row P(string aegis, Demo demo, string src, Func<int, Who, List<Amount>> at) => new Row { Aegis = aegis, Buff = false, Demo = demo, Source = src, At = at };
        private static Row B(string aegis, int icon, Demo demo, string src, Func<int, Who, List<Amount>> at) => new Row { Aegis = aegis, Buff = true, Icon = icon, Demo = demo, Source = src, At = at };

        /// <summary>A Thief line's second job or later (rAthena's "JOBL_2 and the first job Thief": status.cpp:4518): a job that is not
        /// the Thief itself (6, 4007 Thief High, 4029 Baby Thief) with one of them in its line (the client data's inherit chain).</summary>
        public static bool ThiefSecond(int job)
        {
            if (job == 6 || job == 4007 || job == 4029) return false;
            foreach (int j in SkillTreeDb.Chain(job)) if (j == 6 || j == 4007 || j == 4029) return true;
            return false;
        }

        // the weapon conditions' names (Lang keys)
        private const string DaggerSword = "dagger, sword", TwoHand = "two-handed sword", Spear = "spear", AxeSword = "axe, sword", Fist = "bare hand, knuckle",
            Katar = "katar", Instrument = "instrument", Whip = "whip", Book = "book", Mace = "mace", Gun = "gun", Bow = "bow", Dagger = "dagger",
            VsUndead = "vs undead, demon", VsBrute = "vs brute, insect",
            // wfollow
            Mado = "Mado Gear", Dragon = "on a dragon", VsRanger = "vs brute, doram, plant, fish", VsFireEarth = "vs fire, earth", Axe = "axe",
            BareHand = "bare hand", FeelDay = "your map, its day", FeelMaps = "on your maps", MaxJob = "max job level", SwordSpear = "sword, spear",
            ShieldOn = "shield", BowInstWhip = "bow, instrument, whip", TwoHandStaff = "two-handed staff", DaggerKatar = "dagger, katar";

        /// <summary>wfollow: rAthena's W_* weapon types the rows test (src/map/pc.hpp:959-984).</summary>
        private const int W_MACE = 8, W_2HMACE = 9;

        public static readonly Dictionary<string, Row> Table = Build();
        private static Dictionary<string, Row> Build()
        {
            var rows = new[]
            {
                // ---- passives
                P("TF_MISS", Demo.Dodge, "status.cpp:4517-4518", (l, w) => L(A(Stat.Flee, l * (ThiefSecond(w.Job) ? 4 : 3), true))),
                P("MO_DODGE", Demo.Dodge, "status.cpp:4519-4520", (l, w) => L(A(Stat.Flee, l * 3 / 2, true))),
                P("AC_OWL", Demo.Accuracy, "status.cpp:4283-4284; HIT from DEX :2640-2642", (l, w) => L(A(Stat.Dex, l, true), A(Stat.Hit, l, true))),
                P("DC_DANCINGLESSON", Demo.Crit, "status.cpp:4528-4529; battle.cpp:2385-2387", (l, w) => L(A(Stat.Crit, l * 10, true), A(Stat.Atk, 3 * l, false, Whip))),
                P("PR_MACEMASTERY", Demo.Crit, "status.cpp:4531-4532; battle.cpp:2366-2369", (l, w) => L(A(Stat.Crit, l * 10, false, Mace), A(Stat.Atk, 3 * l, false, Mace))),
                P("SA_DRAGONOLOGY", Demo.None, "status.cpp:4281-4282", (l, w) => L(A(Stat.Int, (l + 1) / 2, true))),
                P("BS_HILTBINDING", Demo.None, "status.cpp:4279-4280, :4344-4345", (l, w) => L(A(Stat.Str, 1, true), A(Stat.Atk, 4))),
                P("MC_INCCARRY", Demo.None, "status.cpp:3678-3679 (2000 a level, in tenths)", (l, w) => L(A(Stat.Weight, 200 * l, true))),
                P("GS_SINGLEACTION", Demo.Accuracy, "status.cpp:4492-4494; ASPD :2391-2392", (l, w) => w.Right >= AspdModel.W_REVOLVER && w.Right <= AspdModel.W_GRENADE ? L(A(Stat.Hit, 2 * l, false, Gun), Asp(val: (l + 1) / 2)) : L(A(Stat.Hit, 2 * l, false, Gun))),
                P("GS_SNAKEEYE", Demo.Accuracy, "status.cpp:4495-4498", (l, w) => L(A(Stat.Hit, l, false, Gun), A(Stat.Range, l, false, Gun))),
                P("AC_VULTURE", Demo.None, "status.cpp:4485-4491 (Renewal: no HIT)", (l, w) => L(A(Stat.Range, l, false, Bow))),
                P("SM_SWORD", Demo.None, "battle.cpp:2332-2339", (l, w) => L(A(Stat.Atk, 4 * l, false, DaggerSword))),
                P("SM_TWOHAND", Demo.None, "battle.cpp:2343-2345", (l, w) => L(A(Stat.Atk, 4 * l, false, TwoHand))),
                P("KN_SPEARMASTERY", Demo.None, "battle.cpp:2347-2353 (on foot; riding 5 a level)", (l, w) => L(A(Stat.Atk, 4 * l, false, Spear))),
                P("AM_AXEMASTERY", Demo.None, "battle.cpp:2332-2335, :2359-2362", (l, w) => L(A(Stat.Atk, 3 * l, false, AxeSword))),
                P("MO_IRONHAND", Demo.None, "battle.cpp:2373-2379", (l, w) => L(A(Stat.Atk, 3 * l, false, Fist))),
                P("AS_KATAR", Demo.None, "battle.cpp:2393-2395", (l, w) => L(A(Stat.Atk, 3 * l, false, Katar))),
                P("BA_MUSICALLESSON", Demo.None, "battle.cpp:2381-2383; ASPD status.cpp:8352-8357", (l, w) => L(A(Stat.Atk, 3 * l, false, Instrument), Asp(pct: l))),
                P("SA_ADVANCEDBOOK", Demo.None, "battle.cpp:2389-2391; ASPD status.cpp:2387-2389", (l, w) => w.Right == AspdModel.W_BOOK ? L(A(Stat.Atk, 3 * l, false, Book), Asp(val: (l - 1) / 2 + 1)) : L(A(Stat.Atk, 3 * l, false, Book))),
                P("BS_WEAPONRESEARCH", Demo.None, "battle.cpp:2308-2311", (l, w) => L(A(Stat.Atk, 2 * l))),
                P("HT_BEASTBANE", Demo.None, "battle.cpp:2302-2303", (l, w) => L(A(Stat.Atk, 4 * l, false, VsBrute))),
                P("AL_DEMONBANE", Demo.None, "battle.cpp:2291-2294", (l, w) => L(A(Stat.Atk, (int)(l * (w.BaseLevel / 20.0 + 3.0)), false, VsUndead))),
                P("AL_DP", Demo.None, "battle.cpp:4816-4818", (l, w) => L(A(Stat.Def, (int)((w.BaseLevel / 25.0 + 3.0) * l + 0.5), false, VsUndead))),
                P("CR_TRUST", Demo.None, "status.cpp:3150-3151, :4676-4677", (l, w) => L(A(Stat.MaxHp, 200 * l), A(Stat.HolyResist, 5 * l))),
                P("BS_SKINTEMPER", Demo.None, "status.cpp:4678-4681", (l, w) => L(A(Stat.FireResist, 5 * l), A(Stat.NeutralResist, l))),
                P("TF_DOUBLE", Demo.None, "battle.cpp:4311-4327 (Renewal: 7 a level)", (l, w) => L(A(Stat.DoubleHit, 7 * l, false, Dagger))),
                P("SM_RECOVERY", Demo.None, "status.cpp:5304-5305; conf/battle/player.conf:63", (l, w) => L(A(Stat.HpRegen, (int)(5 * l + l * w.MaxHp / 500)))),
                P("MG_SRECOVERY", Demo.None, "status.cpp:5310-5311; conf/battle/player.conf:63", (l, w) => L(A(Stat.SpRegen, (int)(3 * l + l * w.MaxSp / 500)))),
                // ---- buffs (the icon: status.hpp enum efst_type, EFST_PROVOKE = 0 at :1457)
                B("AL_BLESSING", 10, Demo.Accuracy, "status.cpp:11567-11571, :6777-6779, :6978-6980, :7060-7062, :6004-6016, :7584-7585", (l, w) => L(A(Stat.StrIntDex, l), A(Stat.Hit, 3 * l, true))),
                B("AL_INCAGI", 12, Demo.Dodge, "status.cpp:10854, :6844-6845, :6019-6032, :8345-8346", (l, w) => L(A(Stat.Agi, 2 + l, true), A(Stat.Flee, 2 + l, true), Asp(pct: l))),
                B("AL_ANGELUS", 9, Demo.None, "status.cpp:11620-11621, :7879-7881, :3187-3188", (l, w) => L(A(Stat.Def, w.Stats[2] / 2 * (5 * l) / 100), A(Stat.MaxHp, 50 * l))),
                B("PR_GLORIA", 21, Demo.Crit, "status.cpp:7129-7130, :6136-6141, :6004-6016, :6019-6032", (l, w) => L(A(Stat.Luk, 30, true), A(Stat.Crit, 90, true), A(Stat.Hit, (w.Stats[5] + 30) / 3 - w.Stats[5] / 3, true))),
                B("PR_IMPOSITIO", 15, Demo.None, "status.cpp:11623-11624, :7348-7349, :7444-7446", (l, w) => L(A(Stat.Atk, 5 * l), A(Stat.Matk, 5 * l))),
                B("MO_EXPLOSIONSPIRITS", 86, Demo.Crit, "status.cpp:11127-11128, :7509-7510", (l, w) => L(A(Stat.Crit, 75 + 25 * l, true))),
                B("KN_TWOHANDQUICKEN", 2, Demo.Speed, "status.cpp:7520-7521, :7586-7587, :8249, :8417-8419", (l, w) => L(Asp(fix: 7), A(Stat.Crit, (2 + l) * 10, true), A(Stat.Hit, 2 * l, true))),
                B("BS_ADRENALINE", 23, Demo.Speed, "status.cpp:7588-7589, :8249, :8433-8435", (l, w) => L(Asp(fix: 7), A(Stat.Hit, 3 * l + 5, true))),
                B("MC_LOUD", 30, Demo.None, "status.cpp:6769-6770, :7319-7320", (l, w) => L(A(Stat.Str, 4, true), A(Stat.Atk, 30))),
                B("BS_OVERTHRUST", 25, Demo.None, "status.cpp:11588-11595; battle.cpp:4425-4426", (l, w) => L(A(Stat.Damage, 5 * l))),
                B("SM_ENDURE", 1, Demo.None, "status.cpp:7946-7947", (l, w) => L(A(Stat.Mdef, l))),
                B("AC_CONCENTRATION", 3, Demo.None, "status.cpp:11577-11584, :6836-6837, :7048-7049", (l, w) => L(A(Stat.AgiPct, 2 + l), A(Stat.DexPct, 2 + l))),
                B("CR_AUTOGUARD", 58, Demo.None, "status.cpp:11293-11300", (l, w) => L(A(Stat.Block, GuardChance(l)))),
                B("SN_SIGHT", 115, Demo.Crit, "status.cpp:11630-11632, :6771-6772, :7513-7514, :7551-7552, :6004-6016, :6136-6141", (l, w) => L(A(Stat.AllStats, 5), A(Stat.Crit, 15 + 10 * l, true), A(Stat.Hit, 3 * l + 5 + (w.Stats[5] + 5) / 3 - w.Stats[5] / 3, true))),
                // ---- wfollow (2026-10-14): transcendent jobs (design notes lists every line; status.cpp unless named)
                P("HP_MEDITATIO", Demo.None, "status.cpp:3353-3354, :3596", (l, w) => L(A(Stat.MaxSpPct, l))),
                P("HP_MANARECHARGE", Demo.None, "status.cpp:4659-4660; skill.cpp:9780-9781", (l, w) => L(A(Stat.SpCost, 4 * l))),
                P("HW_SOULDRAIN", Demo.None, "status.cpp:3355-3356, :3596", (l, w) => L(A(Stat.MaxSpPct, 2 * l))),
                P("ASC_KATAR", Demo.None, "battle.cpp:5574-5577", (l, w) => L(A(Stat.Damage, 10 + 2 * l, false, Katar))),
                B("LK_CONCENTRATION", 105, Demo.Accuracy, "status.cpp:11609-11617, :7555-7556; battle.cpp:4368-4369, :4419-4421", (l, w) => L(A(Stat.Hit, 10 * l, true), A(Stat.Damage, 5 + 2 * l))),
                B("LK_BERSERK", 107, Demo.Speed, "status.cpp:3206-3207, :8282-8283; battle.cpp:4429-4433", (l, w) => L(Asp(fix: 15), A(Stat.MaxHpPct, 200), A(Stat.Damage, 200))),
                B("LK_AURABLADE", 103, Demo.None, "battle.cpp:4917-4923 (Renewal: (3 + level) x base level a hit)", (l, w) => L(A(Stat.DamagePerHit, (3 + l) * w.BaseLevel))),
                B("HP_ASSUMPTIO", 473, Demo.None, "status.cpp:7776-7779; skills/acolyte/assumptio.cpp:10-20", (l, w) => L(A(Stat.Def, 50 * l))),
                B("HW_MAGICPOWER", 113, Demo.None, "status.cpp:10923-10924, :6331-6333", (l, w) => L(A(Stat.MatkPct, 5 * l))),
                B("WS_OVERTHRUSTMAX", 188, Demo.None, "status.cpp:11585-11586; battle.cpp:4427-4428", (l, w) => L(A(Stat.Damage, 20 * l))),
                B("SN_WINDWALK", 116, Demo.Dodge, "status.cpp:11347-11348, :7635-7636", (l, w) => L(A(Stat.Flee, (l + 1) / 2, true))),
                // ---- wfollow: third jobs
                P("RA_RESEARCHTRAP", Demo.None, "status.cpp:4285-4286, :3312-3313", (l, w) => L(A(Stat.Int, l, true), A(Stat.MaxSp, 200 + 20 * l))),
                P("WM_LESSON", Demo.None, "status.cpp:3314-3315, :5314-5315; conf/battle/player.conf:63", (l, w) => L(A(Stat.MaxSp, 30 * l), A(Stat.SpRegen, 3 + 3 * l))),
                P("NC_MAINFRAME", Demo.None, "status.cpp:4583-4584", (l, w) => L(A(Stat.Def, 20 + 20 * l, false, Mado))),
                P("NC_TRAININGAXE", Demo.Accuracy, "status.cpp:4500-4503; battle.cpp:2359-2371", (l, w) => w.Right == W_MACE || w.Right == W_2HMACE
                    ? L(A(Stat.Hit, 2 * l, false, Mace), A(Stat.Atk, 4 * l, false, Mace)) : L(A(Stat.Hit, 3 * l, false, Axe), A(Stat.Atk, 5 * l, false, Axe))),
                P("RK_DRAGONTRAINING", Demo.None, "status.cpp:3682-3683 (5000 + 2000 a level, in tenths)", (l, w) => L(A(Stat.Weight, 500 + 200 * l, false, Dragon))),
                P("NC_MADOLICENCE", Demo.None, "battle.cpp:2300", (l, w) => L(A(Stat.Atk, 15 * l))),
                P("GN_TRAINING_SWORD", Demo.None, "battle.cpp:2332-2341", (l, w) => L(A(Stat.Atk, 10 * l, false, DaggerSword))),
                P("GN_REMODELING_CART", Demo.None, "status.cpp:3736-3738 (5000 a level, in tenths)", (l, w) => L(A(Stat.CartWeight, 500 * l))),
                P("RA_RANGERMAIN", Demo.None, "battle.cpp:2295-2296", (l, w) => L(A(Stat.Atk, 5 * l, false, VsRanger))),
                P("NC_RESEARCHFE", Demo.None, "battle.cpp:2297-2298", (l, w) => L(A(Stat.Atk, 10 * l, false, VsFireEarth))),
                B("GC_HALLUCINATIONWALK", 334, Demo.Dodge, "status.cpp:11898-11900, :7655-7656", (l, w) => L(A(Stat.Flee, 50 * l, true))),
                B("AB_CLEMENTIA", 10, Demo.Accuracy, "skills/acolyte/crementia.cpp:18-20; status.cpp:11567-11571, :6777-6779, :6978-6980, :7060-7062, :6004-6016, :7584-7585", (l, w) =>
                    { int b = w.Learned(34) + w.JobLevel / 10; return L(A(Stat.StrIntDex, b), A(Stat.Hit, 3 * b, true)); }),
                B("AB_CANTO", 12, Demo.Dodge, "skills/acolyte/cantocandidus.cpp:18-20; status.cpp:10854, :6844-6845, :6019-6032, :8345-8346", (l, w) =>
                    { int a = w.Learned(29) + w.JobLevel / 10; return L(A(Stat.Agi, 2 + a, true), A(Stat.Flee, 2 + a, true), Asp(pct: a)); }),
                B("AB_LAUDAAGNUS", 331, Demo.None, "status.cpp:3234-3235", (l, w) => L(A(Stat.MaxHpPct, 2 + 2 * l))),
                B("LG_FORCEOFVANGUARD", 391, Demo.None, "status.cpp:3216-3217", (l, w) => L(A(Stat.MaxHpPct, 3 * l))),
                B("LG_PRESTIGE", 402, Demo.None, "status.cpp:12147, :7816-7817", (l, w) => L(A(Stat.Def, (15 * l + 10 * w.Learned(249)) * w.BaseLevel / 100))),
                B("LG_INSPIRATION", 407, Demo.Accuracy, "status.cpp:12170-12172, :6793-6794 (and the five others), :7375-7376, :7557-7558, :6004-6016", (l, w) =>
                    L(A(Stat.AllStats, 6 * l), A(Stat.Atk, 40 * l), A(Stat.Hit, 12 * l + 6 * l + (w.Stats[5] + 6 * l) / 3 - w.Stats[5] / 3, true))),
                B("SR_RAISINGDRAGON", 410, Demo.Crit, "skills/acolyte/raisingdragon.cpp:17; status.cpp:11127-11128, :7509-7510, :3220-3221, :3371-3372", (l, w) => L(A(Stat.Crit, 75 + 25 * l, true), A(Stat.MaxHpPct, l), A(Stat.MaxSpPct, l))),
                B("SR_GENTLETOUCH_CHANGE", 426, Demo.Speed, "status.cpp:12187-12189, :7377-7378, :8331-8332", (l, w) => L(A(Stat.Atk, 8 * l), Asp(pct: w.Stats[1] * l / 60))),
                B("SR_GENTLETOUCH_REVITALIZE", 427, Demo.None, "status.cpp:12193, :3222-3223", (l, w) => L(A(Stat.MaxHpPct, 2 * l))),
                B("SC_INVISIBILITY", 403, Demo.Crit, "status.cpp:11976-11978, :7523-7524", (l, w) => L(A(Stat.Crit, 200 * l, true))),
                B("SC_AUTOSHADOWSPELL", 393, Demo.None, "skill.cpp:13749-13765; status.cpp:7468-7469", (l, w) => L(A(Stat.Matk, 5 * l))),
                B("WA_SYMPHONY_OF_LOVER", 430, Demo.None, "status.cpp:12055-12056, :7960-7961; skills/archer/symphonyoflovers.cpp:20", (l, w) => L(A(Stat.MdefPct, 2 * l + w.Learned(2412) + w.JobLevel / 4))),
                B("WA_MOONLIT_SERENADE", 447, Demo.None, "status.cpp:12058-12060, :7466-7467; skills/archer/moonlitserenade.cpp:20", (l, w) => L(A(Stat.Matk, 4 + 3 * l + w.Learned(2412) + w.JobLevel / 5))),
                B("MI_RUSH_WINDMILL", 442, Demo.None, "status.cpp:12059-12060, :7383-7384; skills/archer/windmillrushattack.cpp:21", (l, w) => L(A(Stat.Atk, 4 + 3 * l + w.Learned(2412) + w.JobLevel / 5))),
                B("MI_ECHOSONG", 443, Demo.None, "status.cpp:12062-12063, :7820-7821; skills/archer/echosong.cpp:21", (l, w) => L(A(Stat.Def, 6 * l + w.Learned(2412) + w.JobLevel / 4))),
                B("WM_FRIGG_SONG", 715, Demo.None, "status.cpp:12479-12480, :3212-3213", (l, w) => L(A(Stat.MaxHpPct, 5 * l))),
                B("WM_LERADS_DEW", 451, Demo.None, "status.cpp:12110-12111, :3214-3215; skills/archer/leradsdew.cpp:24", (l, w) => L(A(Stat.MaxHpPct, 2 + 3 * l + Math.Min(3 * w.Learned(2412), 25)))),
                B("ALL_FULL_THROTTLE", 720, Demo.None, "status.cpp:12455-12457, :6803-6804 (and the five others)", (l, w) => L(A(Stat.AllStatsPct, 20))),
                B("GN_CARTBOOST", 461, Demo.None, "battle.cpp:3864-3867", (l, w) => L(A(Stat.Atk, 10 * l))),
                // ---- wfollow: Taekwon / Star Gladiator / Soul Linker / Star Emperor / Soul Reaper, Ninja / Kagerou / Oboro, Gunslinger / Rebellion, Super Novice
                P("TK_RUN", Demo.None, "battle.cpp:2373-2375", (l, w) => L(A(Stat.Atk, 10 * l, false, BareHand))),
                P("TK_POWER", Demo.None, "battle.cpp:4216-4222", (l, w) => L(A(Stat.Damage, 20 * l))),
                B("SG_SUN_COMFORT", 169, Demo.None, "status.cpp:11634-11636, :7873-7874; skill.cpp:8620-8629", (l, w) => L(A(Stat.Def, (w.BaseLevel + w.Stats[4] + w.Stats[5]) / 2, false, FeelDay))),
                B("SG_MOON_COMFORT", 170, Demo.Dodge, "status.cpp:11637-11639, :7639-7640; skill.cpp:8620-8629", (l, w) => L(A(Stat.Flee, (w.BaseLevel + w.Stats[4] + w.Stats[5]) / 10, true, FeelDay))),
                B("SG_STAR_COMFORT", 171, Demo.Speed, "status.cpp:8341-8342; skill.cpp:8620-8629", (l, w) => L(Asp(pct: 3 * l, cond: FeelDay))),
                P("SG_KNOWLEDGE", Demo.None, "status.cpp:3684-3685; clif.cpp:10932-10937", (l, w) => L(A(Stat.WeightPct, 10 * l, false, FeelMaps))),
                P("SG_DEVIL", Demo.Speed, "status.cpp:2390-2391", (l, w) => L(Asp(val: 1 + l, cond: MaxJob))),
                P("SL_KAINA", Demo.None, "status.cpp:3310-3311", (l, w) => L(A(Stat.MaxSp, 30 * l))),
                B("SJ_LUNARSTANCE", 1038, Demo.None, "status.cpp:12712-12715, :3240-3241", (l, w) => L(A(Stat.MaxHpPct, 2 + l))),
                B("SJ_SUNSTANCE", 1040, Demo.None, "status.cpp:12708-12711, :7324-7325, :7412-7413", (l, w) => L(A(Stat.AtkPct, 2 + l))),
                B("SJ_UNIVERSESTANCE", 1039, Demo.None, "status.cpp:12725-12728, :6811-6812 (and the five others)", (l, w) => L(A(Stat.AllStats, 2 + l))),
                B("SJ_STARSTANCE", 1043, Demo.Speed, "status.cpp:12716-12719, :8349-8350", (l, w) => L(Asp(pct: 4 + 2 * l))),
                B("SP_SOULGOLEM", 1058, Demo.None, "status.cpp:12748-12750, :7834-7835, :7964-7965", (l, w) => L(A(Stat.Def, 60 * l), A(Stat.Mdef, 15 + 5 * l))),
                B("SP_SOULSHADOW", 1055, Demo.Crit, "status.cpp:12752-12754, :7527-7528, :8387-8388", (l, w) => L(A(Stat.Crit, 10 * (10 + 2 * l), true), Asp(flat: (1 + l) / 2))),
                B("SP_SOULFALCON", 1057, Demo.Accuracy, "status.cpp:12756-12762 (level 5: 13, the code's else never runs), :7414-7415, :7593-7594", (l, w) => L(A(Stat.Atk, 10 * l), A(Stat.Hit, l >= 3 ? 13 : 10, true))),
                B("SP_SOULFAIRY", 1056, Demo.None, "status.cpp:12764-12765, :7458-7459", (l, w) => L(A(Stat.Matk, 10 * l))),
                P("NV_BREAKTHROUGH", Demo.None, "status.cpp:3159-3160, :3323-3324; battle.cpp:2314-2315", (l, w) =>
                    L(A(Stat.MaxHp, 350 * l + (l > 4 ? 250 : 0)), A(Stat.MaxSp, 30 * l + (l > 4 ? 50 : 0)), A(Stat.Atk, 15 * l + (l > 4 ? 25 : 0)))),
                P("NV_TRANSCENDENCE", Demo.None, "status.cpp:3161-3162, :3325-3326, :7433-7435", (l, w) =>
                    L(A(Stat.MaxHp, 350 * l + (l > 4 ? 250 : 0)), A(Stat.MaxSp, 30 * l + (l > 4 ? 50 : 0)), A(Stat.Matk, 15 * l + (l > 4 ? 25 : 0)))),
                B("NJ_NEN", 208, Demo.None, "status.cpp:6775-6776, :6984-6985", (l, w) => L(A(Stat.Str, l, true), A(Stat.Int, l, true))),
                P("NJ_NINPOU", Demo.None, "status.cpp:5312-5313; conf/battle/player.conf:63", (l, w) => L(A(Stat.SpRegen, (int)(3 * l + l * w.MaxSp / 500)))),
                B("KO_IZAYOI", 652, Demo.None, "status.cpp:7472-7473", (l, w) => L(A(Stat.Matk, 25 * l))),
                B("GS_ADJUSTMENT", 209, Demo.Dodge, "status.cpp:7645-7646", (l, w) => L(A(Stat.Flee, 30, true))),
                B("GS_INCREASING", 210, Demo.Accuracy, "status.cpp:6846-6847, :7066-7067, :7561-7562, :6004-6016", (l, w) => L(A(Stat.Agi, 4, true), A(Stat.Dex, 4, true), A(Stat.Hit, 24, true))),
                B("GS_MADNESSCANCEL", 203, Demo.Speed, "status.cpp:8280-8281; battle.cpp:4558-4559", (l, w) => L(Asp(fix: 20), A(Stat.Atk, 100))),
                B("GS_GATLINGFEVER", 204, Demo.Speed, "status.cpp:11648-11652, :8339-8340; battle.cpp:4569-4570", (l, w) => L(Asp(pct: l), A(Stat.Atk, 20 + 10 * l))),
                B("RL_HEAT_BARREL", 759, Demo.Speed, "status.cpp:12579-12588, :8389-8390", (l, w) => L(Asp(flat: l))),
                // ---- wfollow: fourth jobs (a trait number is added after every rate: status.cpp:4544-4571, :8548-8710)
                P("HN_SELFSTUDY_TATICS", Demo.None, "status.cpp:4548-4549", (l, w) => L(A(Stat.PAtk, l, true))),
                P("HN_SELFSTUDY_SOCERY", Demo.None, "status.cpp:4550-4551", (l, w) => L(A(Stat.SMatk, l, true))),
                P("NW_P_F_I", Demo.None, "status.cpp:4552-4553", (l, w) => L(A(Stat.PAtk, l + 2, false, Gun))),
                P("NW_GRENADE_MASTERY", Demo.Accuracy, "status.cpp:4329-4330; HIT, FLEE from CON :2640-2647", (l, w) => L(A(Stat.Con, l, true), A(Stat.Hit, 2 * l, true), A(Stat.Flee, 2 * l, true))),
                P("SOA_TALISMAN_MASTERY", Demo.None, "status.cpp:4554-4555", (l, w) => L(A(Stat.SMatk, l, true))),
                P("SOA_SOUL_MASTERY", Demo.None, "status.cpp:4333-4334; MATK from SPL :2571, :2590", (l, w) => L(A(Stat.Spl, l, true), A(Stat.Matk, 5 * l))),
                P("SKE_WAR_BOOK_MASTERY", Demo.None, "status.cpp:4508-4509, :4556-4557", (l, w) => L(A(Stat.Hit, 3 * l, false, Book), A(Stat.PAtk, l + 2, false, Book))),
                P("IG_SPEAR_SWORD_M", Demo.None, "status.cpp:4506-4507; pc.cpp:7422-7423", (l, w) => L(A(Stat.Hit, 3 * l, false, SwordSpear))),
                P("IG_SHIELD_MASTERY", Demo.None, "status.cpp:4570-4571; pc.cpp:7419-7420", (l, w) => L(A(Stat.Res, 3 * l, false, ShieldOn))),
                P("TR_STAGE_MANNER", Demo.None, "status.cpp:4544-4547", (l, w) => L(A(Stat.PAtkSMatk, 3 * l, false, BowInstWhip))),
                P("AG_TWOHANDSTAFF", Demo.None, "status.cpp:4560-4562", (l, w) => L(A(Stat.SMatk, 2 * l, false, TwoHandStaff))),
                P("SH_MYSTICAL_CREATURE_MASTERY", Demo.None, "status.cpp:4564-4567", (l, w) => L(A(Stat.PAtk, l * 15 / 10, true), A(Stat.SMatk, l * 15 / 10, true))),
                P("SHC_SHADOW_SENSE", Demo.Dodge, "status.cpp:4523-4524, :4535-4540, :6147-6150", (l, w) => L(A(Stat.Flee, 10 * l, true), A(Stat.Crit, 100 + 40 * l, false, DaggerKatar))),
                B("IQ_POWERFUL_FAITH", 1160, Demo.None, "status.cpp:12808-12810, :7416-7417, :8553-8554", (l, w) => L(A(Stat.Atk, 5 + 5 * l), A(Stat.PAtk, 5 + 2 * l, true))),
                B("IQ_FIRM_FAITH", 1162, Demo.None, "status.cpp:12812-12814, :8629-8630, :3244-3245", (l, w) => L(A(Stat.Res, 8 * l, true), A(Stat.MaxHpPct, 2 * l))),
                B("IQ_SINCERE_FAITH", 1161, Demo.Speed, "status.cpp:12816-12818, :8391-8392", (l, w) => L(Asp(flat: (1 + l) / 2))),
                B("IG_GUARD_STANCE", 1202, Demo.None, "status.cpp:12820-12822, :7842-7843", (l, w) => L(A(Stat.Def, 50 + 50 * l))),
                B("IG_ATTACK_STANCE", 1203, Demo.None, "status.cpp:12833-12835, :8563-8564, :8600-8601 (db/re/status.yml CalcFlags Watk, Def only: the status page shows them at the next full refresh)", (l, w) => L(A(Stat.PAtk, 3 * l), A(Stat.SMatk, 3 * l))),
                B("CD_COMPETENTIA", 1201, Demo.None, "status.cpp:12853-12854, :8555-8556, :8592-8593", (l, w) => L(A(Stat.PAtk, 10 * l, true), A(Stat.SMatk, 10 * l, true))),
                B("CD_RELIGIO", 1227, Demo.None, "status.cpp:12856-12858, :7200-7201, :7220-7221, :7240-7241, :6464-6476", (l, w) =>
                    L(A(Stat.StaWisSpl, 2 * l), A(Stat.Res, 2 * l + 2 * l / 3 * 5, true), A(Stat.MRes, 2 * l + 2 * l / 3 * 5, true))),
                B("CD_BENEDICTUM", 1228, Demo.Accuracy, "status.cpp:12856-12858, :7180-7181, :7260-7261, :7280-7281, :6450-6455, :6008-6016", (l, w) =>
                    L(A(Stat.PowConCrt, 2 * l), A(Stat.PAtk, 2 * l / 3 + 2 * l / 5, true), A(Stat.Hit, 4 * l, true))),
                B("CD_PRESENS_ACIES", 1200, Demo.None, "status.cpp:12850-12851, :8699-8700", (l, w) => L(A(Stat.CRate, 2 * l, true))),
                B("ABC_ABYSS_SLAYER", 1245, Demo.Accuracy, "status.cpp:12885-12887, :8557-8558, :8594-8595, :7597-7598", (l, w) => L(A(Stat.PAtk, 10 + 2 * l, true), A(Stat.SMatk, 10 + 2 * l, true), A(Stat.Hit, 100 + 20 * l, true))),
                B("SH_TEMPORARY_COMMUNION", 1377, Demo.None, "status.cpp:12990-12991, :8570-8571, :8607-8608, :8681-8682", (l, w) => L(A(Stat.PAtk, 3 * l, true), A(Stat.SMatk, 3 * l, true), A(Stat.HPlus, 3 * l, true))),
                B("SH_MARINE_FESTIVAL_OF_KI_SUL", 1367, Demo.Accuracy, "status.cpp:12993-12995, :7182-7183, :7262-7263, :7282-7283, :6450-6455, :6008-6016", (l, w) =>
                    L(A(Stat.PowConCrt, 2 * l), A(Stat.PAtk, 2 * l / 3 + 2 * l / 5, true), A(Stat.Hit, 4 * l, true))),
                B("SH_SANDY_FESTIVAL_OF_KI_SUL", 1368, Demo.None, "status.cpp:12993-12995, :7202-7203, :7222-7223, :7242-7243, :6464-6476", (l, w) =>
                    L(A(Stat.StaWisSpl, 2 * l), A(Stat.Res, 2 * l + 2 * l / 3 * 5, true), A(Stat.MRes, 2 * l + 2 * l / 3 * 5, true))),
                B("NW_INTENSIVE_AIM", 1345, Demo.Accuracy, "status.cpp:7599-7600, :7531-7532, :7326-7327", (l, w) => L(A(Stat.Hit, 250, true), A(Stat.Crit, 300, true), A(Stat.Atk, 150))),
                // ---- wfollow (item 4): the quickens and the attack-speed passives not in the table before
                B("CR_SPEARQUICKEN", 68, Demo.Speed, "status.cpp:8248-8250, :7518-7519, :7666-7667", (l, w) => L(Asp(fix: 7), A(Stat.Crit, 30 * l, true), A(Stat.Flee, 2 * l, true))),
                B("KN_ONEHAND", 161, Demo.Speed, "status.cpp:8248-8250", (l, w) => L(Asp(fix: 7))),
                B("BS_ADRENALINE2", 147, Demo.Speed, "status.cpp:8251-8252", (l, w) => L(Asp(fix: 6))),
                P("RG_PLAGIARISM", Demo.Speed, "status.cpp:8358-8359", (l, w) => L(Asp(pct: l))),
                B("NW_HIDDEN_CARD", 1354, Demo.None, "status.cpp:12966-12967, :8566-8567", (l, w) => L(A(Stat.PAtk, 3 * l, true))),
            };
            var d = new Dictionary<string, Row>();
            foreach (var r in rows) d[r.Aegis] = r;
            return d;
        }

        /// <summary>Guard's block chance at a level (rAthena status.cpp:11296-11299: the sum of max(1, 5 - i/2) for i below the level).</summary>
        public static int GuardChance(int lv) { int v = 0; for (int i = 0; i < lv; i++) { int t = 5 - i / 2; v += t < 1 ? 1 : t; } return v; }

        public static Row RowOf(int skill) { var a = SkillDb.Get(skill).Aegis; return !string.IsNullOrEmpty(a) && Table.TryGetValue(a, out var r) ? r : null; }

        /// <summary>The stat now as the status page shows it (CRIT in whole points); null: a stat without a number there.</summary>
        public static int? Now(Stat s, Who w)
        {
            switch (s)
            {
                case Stat.Str: return w.Stats[0]; case Stat.Agi: return w.Stats[1]; case Stat.Vit: return w.Stats[2];
                case Stat.Int: return w.Stats[3]; case Stat.Dex: return w.Stats[4]; case Stat.Luk: return w.Stats[5];
                case Stat.Hit: return w.Hit; case Stat.Flee: return w.Flee; case Stat.Crit: return w.Crit; case Stat.Weight: return w.WeightLimit;
                // wfollow: the trait stats and their numbers (the status page's Traits side)
                case Stat.Pow: return w.Traits[0]; case Stat.Sta: return w.Traits[1]; case Stat.Wis: return w.Traits[2];
                case Stat.Spl: return w.Traits[3]; case Stat.Con: return w.Traits[4]; case Stat.Crt: return w.Traits[5];
                case Stat.PAtk: return w.PAtk; case Stat.SMatk: return w.SMatk; case Stat.Res: return w.Res; case Stat.MRes: return w.MRes;
                case Stat.HPlus: return w.HPlus; case Stat.CRate: return w.CRate;
                default: return null;
            }
        }

        /// <summary>What the preview shows for a skill for this character (null: no row: no gain).</summary>
        public static Gain For(int skill, Who w)
        {
            var row = RowOf(skill); if (row == null || w == null) return null;
            int learned = Math.Max(0, w.Learned(skill));
            int lv = learned <= 0 ? 1 : row.Buff ? Math.Max(1, Math.Min(learned, w.CastLevel(skill) > 0 ? w.CastLevel(skill) : learned)) : learned;
            var g = new Gain { Skill = skill, Aegis = row.Aegis, Level = lv, Buff = row.Buff, Learned = learned > 0, Demo = row.Demo, Source = row.Source };
            g.IconOn = row.Buff && row.Icon >= 0 && w.IconOn(row.Icon);
            foreach (var a in row.At(lv, w))
            {
                if (a.Value == 0 && a.Stat != Stat.AttackSpeed) continue; // (wfollow: a number that is 0 at this level gains nothing)
                var line = new Line { Stat = a.Stat, Value = a.Value, Cond = a.Cond };
                int? now = a.Shown && !g.IconOn ? Now(a.Stat, w) : null;
                // CRIT is kept in tenths: a whole number of points can be written before → after (the status page shows cri / 10)
                if (now.HasValue && (a.Stat != Stat.Crit || a.Value % 10 == 0))
                {
                    int add = a.Stat == Stat.Crit ? a.Value / 10 : a.Value;
                    line.BeforeAfter = true;
                    if (!row.Buff && learned > 0) { line.After = now.Value; line.Before = now.Value - add; }
                    else { line.Before = now.Value; line.After = now.Value + add; }
                }
                if (a.Stat == Stat.AttackSpeed && !g.IconOn)
                {
                    int agiAdd = 0; foreach (var b in row.At(lv, w)) if (b.Stat == Stat.Agi) agiAdd += b.Value;
                    var ba = AspdBeforeAfter(row, a, learned > 0, agiAdd, w);
                    if (ba != null) { line.BeforeAfter = true; line.Before = ba.Value.before; line.After = ba.Value.after; }
                }
                line.Text = TextOf(line); line.Main = TextOf(line, false); line.CondText = line.Cond != null ? "(" + Lang.T(line.Cond) + ")" : "";
                g.Lines.Add(line);
            }
            return g;
        }

        // ------------------------------------------------------------------ wfollow (item 4): the attack-speed number
        /// <summary>Statuses whose attack-speed effect the preview does not compute (rAthena status_calc_aspd :8237-8366, status_calc_fix_aspd
        /// :8376-8393: the quickens, Madness Canceller, Frenzy, the potions, the songs, ...; the icons from db/re/status.yml + status.hpp
        /// efst_type): with one of them on, today's ASPD is not recomputed and the line stays "Attack speed up".</summary>
        public static readonly int[] AspdIcons = { 2, 161, 23, 147, 68, 71, 203, 107, 37, 38, 39, 40, 75, 1088, 87, 62, 109, 125, 351, 335, 343, 399, 403, 409,
            429, 441, 438, 426, 478, 598, 204, 171, 569, 12, 81, 1043, 581, 552, 554, 556, 322, 1055, 759, 1161 };

        /// <summary>The passives' part of today's ASPD (status.cpp:2387-2395 val, :8352-8360 percent); null: one that is not modelled
        /// (Demon of the Sun Moon and Stars: its job-level rule).</summary>
        private static (int val, int pct)? AspdPassives(Who w)
        {
            int val = 0, pct = 0;
            int book = w.Learned(274), single = w.Learned(509), devil = w.Learned(441), music = w.Learned(315), plag = w.Learned(225);
            if (book > 0 && w.Right == AspdModel.W_BOOK) val += (book - 1) / 2 + 1;           // SA_ADVANCEDBOOK
            if (single > 0 && w.Right >= AspdModel.W_REVOLVER && w.Right <= AspdModel.W_GRENADE) val += (single + 1) / 2; // GS_SINGLEACTION
            if (devil > 0) return null;                                                            // SG_DEVIL: Star Emperor or the job's last level
            pct += music + plag;                                                                    // BA_MUSICALLESSON, RG_PLAGIARISM
            return (val, pct);
        }

        /// <summary>The ASPD before / after a skill, computed as rAthena does (Game/AspdModel.cs), or null when it cannot be exact: the ASPD
        /// unknown, riding, a status that changes the attack speed on, a job or weapon not in the table, today's number not the server's,
        /// or over the cap the client knows.</summary>
        /// <summary>Why the last attack-speed line has (or has not) a number: the inputs and the numbers (the log, the smoke).</summary>
        public static string AspdNote { get; private set; } = "";

        public static (int before, int after)? AspdBeforeAfter(Row row, Amount a, bool learned, int agiAdd, Who w)
        {
            AspdNote = $"job {w.Job}, hands {w.Right}/{w.Left}{(w.Shield ? " + shield" : "")}, AGI {w.Stats[1]}, DEX {w.Stats[4]}, the server's ASPD {w.Aspd}";
            if (w.Aspd <= 0 || w.Riding) { AspdNote += w.Riding ? ": riding" : ": no ASPD yet"; return null; }
            foreach (int e in AspdIcons) if (w.IconOn(e)) { AspdNote += $": status {e} on"; return null; }
            var pas = AspdPassives(w); if (pas == null) { AspdNote += ": Demon of the Sun Moon and Stars"; return null; }
            var x = new AspdModel.Input { Job = w.Job, Right = w.Right, Left = w.Left, Shield = w.Shield, Agi = w.Stats[1], Dex = w.Stats[4], Fixed = 0, Val = pas.Value.val, Pct = pas.Value.pct };
            // rAthena counts the percent part (the passives' too) only while the character has any status change (status_calc_aspd
            // :8239-8242 returns 0 for an empty one): the client cannot see every status, so today's number decides which it is
            int? now = AspdModel.Display(x);
            var none = x; none.Pct = 0; int? nowNone = AspdModel.Display(none);
            AspdNote += $"; the passives val {x.Val}, percent {x.Pct}: today {now} (or {nowNone} with no status change)";
            if (now == null) return null;
            bool empty = now.Value != w.Aspd && nowNone == w.Aspd;
            if (now.Value != w.Aspd && !empty) { AspdNote += ": not the server's (equipment's ASPD bonuses, an unseen status, ...)"; return null; }
            if (empty) { x = none; now = nowNone; }
            int cap = AspdModel.KnownCap(w.Job) ?? 190;
            int before, after;
            if (!row.Buff)
            {
                if (empty && a.AspdPct != 0) { AspdNote += ": no status change on: a percent passive adds nothing now"; return null; }
                var without = x; without.Val -= learned ? a.AspdVal : 0; without.Pct -= learned ? a.AspdPct : 0;
                var with = x; with.Val += learned ? 0 : a.AspdVal; with.Pct += learned ? 0 : a.AspdPct;
                int? b0 = AspdModel.Display(without), a0 = AspdModel.Display(with); if (b0 == null || a0 == null) return null;
                before = b0.Value; after = a0.Value;
            }
            else
            {
                var with = x; with.Fixed = AspdModel.FixedWith(0, a.AspdFix); with.Pct = pas.Value.pct + a.AspdPct; with.Agi += agiAdd; // (a cast: a status change on)
                int? a0 = AspdModel.Display(with); if (a0 == null) return null;
                before = now.Value; after = a0.Value + a.AspdFlat;
            }
            if (after > cap || before > cap) { if (AspdModel.KnownCap(w.Job) == null) { AspdNote += $": over {cap}, this job's cap not known"; return null; } after = Math.Min(after, cap); before = Math.Min(before, cap); }
            AspdNote += $": {before} → {after}";
            return (before, after);
        }

        /// <summary>A stat's short name as the status page writes it (Lang keys).</summary>
        public static string NameOf(Stat s)
        {
            switch (s)
            {
                case Stat.Str: return Lang.T("STR"); case Stat.Agi: case Stat.AgiPct: return Lang.T("AGI"); case Stat.Vit: return Lang.T("VIT");
                case Stat.Int: return Lang.T("INT"); case Stat.Dex: case Stat.DexPct: return Lang.T("DEX"); case Stat.Luk: return Lang.T("LUK");
                case Stat.Hit: return Lang.T("HIT"); case Stat.Flee: return Lang.T("FLEE"); case Stat.Crit: return Lang.T("CRIT");
                case Stat.Weight: return Lang.T("Weight limit"); case Stat.Atk: return Lang.T("ATK"); case Stat.Matk: return Lang.T("MATK");
                case Stat.Def: return Lang.T("DEF"); case Stat.Mdef: case Stat.MdefPct: return Lang.T("MDEF"); case Stat.MaxHp: case Stat.MaxHpPct: return Lang.T("Max HP");
                // wfollow: the trait names as the status page writes them (Progression.TraitNames, WristMenu: never translated)
                case Stat.Pow: case Stat.Sta: case Stat.Wis: case Stat.Spl: case Stat.Con: case Stat.Crt: return Progression.TraitNames[(int)s - (int)Stat.Pow];
                case Stat.PAtk: return "P.ATK"; case Stat.SMatk: return "S.MATK"; case Stat.Res: return "RES"; case Stat.MRes: return "MRES";
                case Stat.HPlus: return "H.PLUS"; case Stat.CRate: return "C.RATE";
                case Stat.MaxSp: case Stat.MaxSpPct: return Lang.T("Max SP"); case Stat.AtkPct: return Lang.T("ATK"); case Stat.MatkPct: return Lang.T("MATK");
                case Stat.WeightPct: return Lang.T("Weight limit"); case Stat.CartWeight: return Lang.T("Cart weight limit");
                case Stat.AttackSpeed: return Lang.T("ASPD"); // (wfollow, item 4)
                default: return "";
            }
        }

        /// <summary>The colour of an "after" number and of a gain.</summary>
        public const string Up = "#8CFF96";

        /// <summary>A line's text: "FLEE 120 → 135", "ATK +40 (dagger, sword)", "Damage +25%" (the numbers green: rich text).</summary>
        public static string TextOf(Line l, bool withCond = true)
        {
            string cond = l.Cond != null && withCond ? " (" + Lang.T(l.Cond) + ")" : "";
            string G(string s) => "<color=" + Up + ">" + s + "</color>";
            if (l.BeforeAfter) return NameOf(l.Stat) + " " + l.Before + " → " + G(l.After.ToString()) + cond;
            switch (l.Stat)
            {
                case Stat.Crit: return NameOf(l.Stat) + " " + G("+" + (l.Value % 10 == 0 ? (l.Value / 10).ToString() : (l.Value / 10) + "." + (l.Value % 10))) + cond;
                case Stat.Damage: return G(Lang.F("Damage +{0}%", l.Value)) + cond;
                case Stat.DoubleHit: return G(Lang.F("Double hit {0}%", l.Value)) + cond;
                case Stat.Block: return G(Lang.F("Block {0}%", l.Value)) + cond;
                case Stat.Range: return G(Lang.F("Range +{0}", l.Value)) + cond;
                case Stat.AttackSpeed: return G(Lang.T("Attack speed up")) + cond;
                case Stat.AllStats: return G(Lang.F("All stats +{0}", l.Value)) + cond;
                case Stat.StrIntDex: return G(Lang.F("STR, INT, DEX +{0}", l.Value)) + cond;
                case Stat.HolyResist: return G(Lang.F("Holy resist +{0}%", l.Value)) + cond;
                case Stat.FireResist: return G(Lang.F("Fire resist +{0}%", l.Value)) + cond;
                case Stat.NeutralResist: return G(Lang.F("Neutral resist +{0}%", l.Value)) + cond;
                case Stat.HpRegen: return G(Lang.F("HP +{0} every 10 s", l.Value)) + cond;
                case Stat.SpRegen: return G(Lang.F("SP +{0} every 10 s", l.Value)) + cond;
                case Stat.AgiPct: case Stat.DexPct: return NameOf(l.Stat) + " " + G("+" + l.Value + "%") + cond;
                // wfollow
                case Stat.MaxHpPct: case Stat.MaxSpPct: case Stat.AtkPct: case Stat.MatkPct: case Stat.MdefPct: case Stat.WeightPct: return NameOf(l.Stat) + " " + G("+" + l.Value + "%") + cond;
                case Stat.SpCost: return G(Lang.F("SP cost -{0}%", l.Value)) + cond;
                case Stat.AllStatsPct: return G(Lang.F("All stats +{0}%", l.Value)) + cond;
                case Stat.StaWisSpl: return "STA, WIS, SPL " + G("+" + l.Value) + cond;
                case Stat.PowConCrt: return "POW, CON, CRT " + G("+" + l.Value) + cond;
                case Stat.PAtkSMatk: return "P.ATK, S.MATK " + G("+" + l.Value) + cond; // (wfollow: one line, so its long condition gets a line of its own)
                case Stat.DamagePerHit: return G(Lang.F("Damage +{0} a hit", l.Value)) + cond;
                default: return NameOf(l.Stat) + " " + G("+" + l.Value) + cond;
            }
        }

        /// <summary>A line as plain text (no colour tags: the log, the smoke).</summary>
        public static string Plain(string s) { var b = new StringBuilder(s.Length); bool tag = false; foreach (char c in s) { if (c == '<') tag = true; else if (c == '>') tag = false; else if (!tag) b.Append(c); } return b.ToString(); }

        /// <summary>The conditions' names (Lang keys), for the string table's check: each one is written as a Lang.N here so the table
        /// knows the code uses it (Lang.T takes them from the constants above).</summary>
        private static readonly string[] CondNames =
        {
            Lang.N("dagger, sword"), Lang.N("two-handed sword"), Lang.N("spear"), Lang.N("axe, sword"), Lang.N("bare hand, knuckle"), Lang.N("katar"),
            Lang.N("instrument"), Lang.N("whip"), Lang.N("book"), Lang.N("mace"), Lang.N("gun"), Lang.N("bow"), Lang.N("dagger"),
            Lang.N("vs undead, demon"), Lang.N("vs brute, insect"),
            // wfollow
            Lang.N("Mado Gear"), Lang.N("on a dragon"), Lang.N("vs brute, doram, plant, fish"), Lang.N("vs fire, earth"), Lang.N("axe"), Lang.N("bare hand"),
            Lang.N("your map, its day"), Lang.N("on your maps"), Lang.N("max job level"), Lang.N("sword, spear"), Lang.N("shield"), Lang.N("bow, instrument, whip"),
            Lang.N("two-handed staff"), Lang.N("dagger, katar"),
        };
        public static int CondCount => CondNames.Length;
    }
}
