using System.Collections.Generic;

namespace Loki.Sprites
{
    /// <summary>
    /// Client wave 4 (w4units): which class a player unit is drawn as, and the few looks the e_option bits and the job id decide
    /// beyond the plain body — mounts, costume outfits, the orc head, babies' size, and the body names the client's job-name table
    /// lacks. Protocol-generic: everything here reads the job id and the e_option bits of the unit packets / ZC_STATE_CHANGE 0x0229
    /// (rAthena clif.cpp:1111-1128, 4378-4432) and the status icons, as the SpacetimeDB module sends them too.
    ///
    /// What rAthena sends (e985006):
    ///  • e_option (status.hpp:3024-3063): RIDING 0x20 (Peco / gryphon), DRAGON1..5 0x80000 / 0x800000 / 0x1000000 / 0x2000000 /
    ///    0x4000000 (@mount's colour 1-5, atcommand.cpp:5016-5026), WUGRIDER 0x200000, MADOGEAR 0x400000; ORCISH 0x800; the costume
    ///    outfits WEDDING 0x1000, XMAS 0x10000, SUMMER 0x40000, HANBOK 0x8000000, OKTOBERFEST 0x10000000, SUMMER2 0x20000000.
    ///  • Knights and crusaders on a Peco get the mounted class only when their view data is rebuilt (status_set_viewdata,
    ///    status.cpp:9401-9422: a job change pc.cpp:10998, a disguise :3077, the end of flying :11241); mounting live sends no new
    ///    class (pc_setoption, pc.cpp:11177-11248: new_look stays 0 for RIDING), so the class on the wire is the base one (seen in the
    ///    w4units smoke: a Knight on a Peco stays 7) — every mount keeps the base class on the wire and the client picks the mounted
    ///    body (roBrowserLegacy DB/Jobs/MountTable.js).
    ///  • A costume outfit keeps the class too; the server only blanks the weapon and shield (map_session_data::update_look,
    ///    pc.cpp:628-650: OPTION_COSTUME → 0) and, when the battle config asks, the clothes colour (clif.cpp:4006-4017); the client
    ///    draws the outfit's body from the option bit (roBrowserLegacy EntityState.js:412-425: WEDDING → 22, XMAS → 26, SUMMER → 27).
    ///
    /// Order (roBrowserLegacy EntityState.js:375-501 updateEffectState, :503-537 updateAllRidingState; a reimplementation, the
    /// official client could not be checked): a boarding mount (EFST_ALL_RIDING, AllMountTable) unless a riding bit applies; a riding
    /// bit (MountTable) over it; a costume outfit over both.
    /// </summary>
    public static class UnitLooks
    {
        public const int OptOrcish = 0x800, OptWedding = 0x1000, OptXmas = 0x10000, OptSummer = 0x40000, OptHanbok = 0x8000000,
            OptOktoberfest = 0x10000000, OptSummer2 = 0x20000000;
        public const int OptDragon1 = 0x80000, OptDragon2 = 0x800000, OptDragon3 = 0x1000000, OptDragon4 = 0x2000000, OptDragon5 = 0x4000000;
        public const int CostumeMask = OptWedding | OptXmas | OptSummer | OptHanbok | OptOktoberfest | OptSummer2; // rA OPTION_COSTUME
        /// <summary>The option bits that change what a player's body is drawn as: a change of any of them rebuilds the look.</summary>
        public const int LookMask = Unit.RidingMask | CostumeMask | OptOrcish;

        /// <summary>
        /// roBrowserLegacy DB/Jobs/MountTable.js (ids from JobConst.js): base class → the class drawn while a riding bit is set (Knights
        /// and crusaders too: live, rAthena sends their base class, see the summary).
        /// rB's CRUSADER_2ND / RUNE_KNIGHT_2ND … keys resolve through JobConst.js to 4332 … 4338 (the costume "2nd job" ids,
        /// rA mmo.hpp JOB_*_2ND); CRUSADER_2ND and CRUSADER2_2ND are not in JobConst.js, so rB stores nothing for them.
        /// </summary>
        private static readonly Dictionary<int, int> MountTable = new Dictionary<int, int>
        {
            { 7, 13 }, { 4008, 4014 }, { 4030, 4036 },                         // Knight → Peco knight
            { 14, 21 }, { 4015, 4022 }, { 4037, 4044 },                        // Crusader → Peco crusader
            { 4054, 4080 }, { 4060, 4081 }, { 4096, 4109 }, { 4332, 4345 },    // Rune Knight → dragon (룬나이트쁘띠)
            { 4066, 4082 }, { 4073, 4083 }, { 4102, 4110 }, { 4338, 4348 },    // Royal Guard → gryphon (그리폰가드)
            { 4056, 4084 }, { 4062, 4085 }, { 4098, 4111 }, { 4337, 4346 },    // Ranger → warg (레인져늑대)
            { 4058, 4086 }, { 4064, 4087 }, { 4100, 4112 }, { 4333, 4347 },    // Mechanic → Mado Gear (마도기어)
            { 4257, 4278 }, { 4253, 4279 }, { 4252, 4280 }, { 4258, 4281 },    // Windhawk, Meister, Dragon Knight, Imperial Guard
        };

        /// <summary>The Rune Knight dragon classes (RUNE_KNIGHT2 4080, _H 4081, _B 4109, _2ND 4345: body 룬나이트쁘띠).</summary>
        private static bool IsRuneDragon(int drawn) => drawn == 4080 || drawn == 4081 || drawn == 4109 || drawn == 4345;
        private const string RuneDragon = "·é³ªÀÌÆ®»Ú¶ì"; // 룬나이트쁘띠 (CP949 as Latin-1)

        /// <summary>What a player is drawn as: the class (its weapon folder, palettes and actions) and, when not null, a body sprite name
        /// that replaces the class's own (a coloured dragon).</summary>
        public struct Drawn
        {
            public int Job;
            public string BodyName;
            /// <summary>The race folder follows the base class (a Doram in a costume outfit keeps the Doram outfit: the GRF has
            /// 도람족\몸통\*\결혼 / 여름 / 한복).</summary>
            public bool Doram;
            public bool Orcish;
            public override string ToString() => $"class {Job}{(BodyName != null ? " body " + BodyName : "")}{(Doram ? " doram" : "")}{(Orcish ? " orc head" : "")}";
        }

        /// <param name="job">the class on the wire (LOOK_BASE)</param>
        /// <param name="option">the unit's e_option bits</param>
        /// <param name="allRiding">EFST_ALL_RIDING on the unit (Unit.AllRiding)</param>
        public static Drawn Resolve(int job, int option, bool allRiding)
        {
            int drawn = 0;
            if (allRiding && AllMountTable.Map.TryGetValue(job, out int b)) drawn = b;                   // updateAllRidingState
            if ((option & Unit.RidingMask) != 0 && MountTable.TryGetValue(job, out int m)) drawn = m;   // updateEffectState: riding
            int costume = CostumeClass(option);
            if (costume > 0) drawn = costume;                                                            // updateEffectState: costume
            var d = new Drawn { Job = drawn > 0 ? drawn : job, Doram = RoNames.IsDoram(job), Orcish = (option & OptOrcish) != 0 };
            if (costume == 0 && IsRuneDragon(d.Job)) d.BodyName = DragonBody(option);
            return d;
        }

        /// <summary>
        /// The costume outfit class of the option bits, 0 = none. WEDDING → 22, XMAS → 26, SUMMER → 27 as roBrowserLegacy
        /// (EntityState.js:412-425); HANBOK → 28, OKTOBERFEST → 29, SUMMER2 → 30: roBrowserLegacy defines these ids (JobConst.js:40-42)
        /// and the bits (StatusState.js:61-62) but draws nothing for them — Loki pairs each bit with rAthena's class of the same name
        /// (mmo.hpp e_job JOB_HANBOK 28, JOB_OKTOBERFEST 29, JOB_SUMMER2 30; status.hpp OPTION_HANBOK / OKTOBERFEST / SUMMER2), whose
        /// bodies the GRF has (한복, 옥토버패스트, 여름2).
        /// </summary>
        public static int CostumeClass(int option)
        {
            if ((option & OptSummer2) != 0) return 30;
            if ((option & OptOktoberfest) != 0) return 29;
            if ((option & OptHanbok) != 0) return 28;
            if ((option & OptSummer) != 0) return 27;
            if ((option & OptXmas) != 0) return 26;
            if ((option & OptWedding) != 0) return 22;
            return 0;
        }

        /// <summary>
        /// The dragon's colour: DRAGON1 is the plain 룬나이트쁘띠; DRAGON2..5 → 룬나이트쁘띠2..5 (the GRF's numbered bodies). No
        /// reference names the pairing (roBrowserLegacy draws every colour as 룬나이트쁘띠 and has the ids RUNE_CHICKEN2..5,
        /// JobConst.js:129-136, without names); it is Loki's reading of the matching numbers (rA OPTION_DRAGON2..5 = @mount 2..5).
        /// </summary>
        public static string DragonBody(int option)
        {
            if ((option & OptDragon2) != 0) return RuneDragon + "2";
            if ((option & OptDragon3) != 0) return RuneDragon + "3";
            if ((option & OptDragon4) != 0) return RuneDragon + "4";
            if ((option & OptDragon5) != 0) return RuneDragon + "5";
            return null;
        }

        // ------------------------------------------------------------------ babies
        /// <summary>
        /// Baby classes are drawn with the grown-up body at 4/5 size: roBrowserLegacy EntityView.js:253 (xSize = ySize = isBaby ? 4 : 5)
        /// with DB/Jobs/BabyTable.js. The set: rB's BabyTable (resolved through JobConst.js) plus rAthena's JOB_BABY_* classes it misses
        /// (mmo.hpp e_job: BABY_RANGER2 4111, BABY_MECHANIC2 4112). rB decides by the drawn class; Loki by the base class or the drawn one,
        /// so a baby on a boarding mount (AllMountTable → a class rB's list lacks, e.g. 4230) stays small.
        /// </summary>
        private static readonly HashSet<int> Babies = new HashSet<int>
        {
            4023, 4024, 4025, 4026, 4027, 4028, 4029, 4030, 4031, 4032, 4033, 4034, 4035, 4036, 4037, 4038, 4039, 4040, 4041, 4042, 4043, 4044,
            4045, 4096, 4097, 4098, 4099, 4100, 4101, 4102, 4103, 4104, 4105, 4106, 4107, 4108, 4109, 4110, 4111, 4112, 4158, 4159, 4160, 4161,
            4162, 4163, 4164, 4165, 4166, 4167, 4168, 4169, 4170, 4171, 4172, 4173, 4174, 4175, 4176, 4177, 4178, 4179, 4180, 4181, 4182, 4191,
            4193, 4194, 4195, 4196, 4205, 4206, 4207, 4208, 4209, 4210, 4220, 4222, 4223, 4224, 4225, 4226, 4227, 4228, 4229, 4238, 4241, 4242,
            4244,
        };
        public static bool IsBaby(int job) => Babies.Contains(job);
        public const float BabyScale = 0.8f;

        // ------------------------------------------------------------------ body names the table lacks
        /// <summary>
        /// Body sprite names for classes the client's name table (spritenames.txt pc rows: roBrowserLegacy JobNameTable.js) has no row for,
        /// so that they don't fall back to the Novice body:
        ///  • baby classes → the grown-up class's name: rB's own rule for every baby it lists (JobNameTable.js:230-273 duplicateEntry(X, X_B)),
        ///    applied to the babies its table misses (SUPERNOVICE_B, SUPERNOVICE2_B, NINJA_B … REBELLION_B, STAR2_B: BabyTable.js lists them);
        ///  • ARCH_BISHOP_2ND 4336 → the Arch Bishop body: rB's duplicateEntry (JobNameTable.js:259) spells it ARCHBISHOP_2ND, a name
        ///    JobConst.js lacks (it has ARCH_BISHOP_2ND = 4336), so its row is lost;
        ///  • HANBOK 28, OKTOBERFEST 29, SUMMER2 30 → 한복, 옥토버패스트, 여름2 (the GRF's bodies; rB has the ids, not the names).
        /// Returns the class whose name to use (or -1: see <see cref="ExtraName"/>).
        /// </summary>
        public static int NameSource(int job)
        {
            switch (job)
            {
                case 4045: return 23;   // SUPERNOVICE_B → SUPERNOVICE
                case 4191: return 4190; // SUPERNOVICE2_B → SUPERNOVICE2
                case 4222: return 25;   // NINJA_B → NINJA
                case 4223: return 4211; // KAGEROU_B → KAGEROU
                case 4224: return 4212; // OBORO_B → OBORO
                case 4225: return 4046; // TAEKWON_B → TAEKWON
                case 4226: return 4047; // STAR_B → STAR
                case 4227: return 4049; // LINKER_B → LINKER
                case 4228: return 24;   // GUNSLINGER_B → GUNSLINGER
                case 4229: return 4215; // REBELLION_B → REBELLION
                case 4238: return 4048; // STAR2_B → STAR2
                case 4336: return 4057; // ARCH_BISHOP_2ND → ARCHBISHOP
                default: return -1;
            }
        }

        /// <summary>
        /// A table name the GRF does not have, corrected: ELEMENTAL_MASTER_RIDING 4274 is 'elemental_master_riding' in roBrowserLegacy
        /// (JobNameTable.js:208) while the GRF spells the body 'elemetal_master_riding' — the same misspelling as ELEMENTAL_MASTER's own
        /// 'elemetal_master' (JobNameTable.js:106), which rB has right (the GRF: data\sprite\인간족\몸통\*\elemetal_master_riding_*.spr).
        /// </summary>
        public static string CorrectedName(int job, string tableName) =>
            job == 4274 && tableName == "elemental_master_riding" ? "elemetal_master_riding" : tableName;

        /// <summary>The GRF body names of the costume classes rB's table lacks (CP949 as Latin-1), or null.</summary>
        public static string ExtraName(int job)
        {
            switch (job)
            {
                case 28: return "ÇÑº¹";                                     // 한복
                case 29: return "¿ÁÅä¹öÆÐ½ºÆ®"; // 옥토버패스트
                case 30: return "¿©¸§2";                                    // 여름2
                default: return null;
            }
        }

        /// <summary>roBrowserLegacy DB.getHeadPath (DBManager.js:1320-1324): with the ORCISH option the head is 이팩트\orcface.</summary>
        public const string OrcHead = "data\\sprite\\" + RoNames.Effect + "\\orcface";
    }
}
