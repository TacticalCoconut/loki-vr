using System.Collections.Generic;
using System.IO;
using Loki.IO;
using UnityEngine;

namespace Loki.Sprites
{
    /// <summary>Sprite file-name tables (StreamingAssets/data/spritenames.txt) and the client's folder conventions.</summary>
    public static class RoNames
    {
        // EUC-KR folder names as Latin-1 strings (byte-exact)
        public const string Human = "ÀÎ°£Á·";      // 인간족
        public const string Doram = "µµ¶÷Á·";      // 도람족
        public const string Body = "¸öÅë";                   // 몸통
        public const string Head = "¸Ó¸®Åë";       // 머리통
        public const string Monster = "¸ó½ºÅÍ";    // 몬스터
        public const string Accessory = "¾Ç¼¼»ç¸®"; // 악세사리
        public const string Robe = "·Îºê";                   // 로브
        public const string Effect = "ÀÌÆÑÆ®";     // 이팩트
        public const string Male = "³²";                               // 남
        public const string Female = "¿©";                             // 여

        private static readonly Dictionary<int, string> _pc = new Dictionary<int, string>();
        private static readonly Dictionary<int, string> _npc = new Dictionary<int, string>();
        private static readonly Dictionary<int, string> _acc = new Dictionary<int, string>();
        private static readonly Dictionary<int, string> _robe = new Dictionary<int, string>();
        private static readonly Dictionary<int, string> _wpn = new Dictionary<int, string>();
        private static bool _loaded;

        // Hair style id → sprite index (roBrowser HairIndexTable; the client remaps the first 13 styles)
        private static readonly int[] HairF = { 2, 2, 4, 7, 1, 5, 3, 6, 12, 10, 9, 11, 8 };
        private static readonly int[] HairM = { 2, 2, 1, 7, 5, 4, 3, 6, 8, 9, 10, 12, 11 };

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            string p = Loki.Data.ClientData.File("spritenames.txt");
            if (!File.Exists(p)) { Debug.LogError("[RoNames] missing " + p); return; }
            foreach (var line in File.ReadAllLines(p, RoReader.Latin1))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var f = line.Split('\t');
                if (f.Length < 3 || !int.TryParse(f[1], out int id)) continue;
                switch (f[0])
                {
                    case "pc": _pc[id] = f[2]; break;
                    case "npc": _npc[id] = f[2]; break;
                    case "acc": _acc[id] = f[2]; break;
                    case "robe": _robe[id] = f[2]; break;
                    case "wpn": _wpn[id] = f[2]; break;
                }
            }
            Debug.Log($"[RoNames] {_pc.Count} pc, {_npc.Count} npc/mob, {_acc.Count} headgear, {_robe.Count} robes, {_wpn.Count} weapons");
        }

        public static bool IsPlayer(int job) => (job >= 0 && job < 45) || (job >= 4001 && job <= 4361);
        public static bool IsDoram(int job) => (job >= 4217 && job <= 4220) || job == 4308 || job == 4315;
        public static bool IsNpc(int job) => (job > 45 && job < 130) || (job >= 401 && job < 1000) || (job >= 10001 && job < 19999) || (job > 22300 && job < 22313) || job == 32767;
        public static bool IsHomunculus(int job) => (job >= 6001 && job <= 6016) || (job >= 6048 && job <= 6052);
        public static bool IsMercenary(int job) => job >= 6017 && job <= 6046;

        public static string Sex(int sex) => sex == 0 ? Female : Male;

        /// <summary>
        /// The renewal client draws a few units as Granny 3-D models (jobname "*.gr2": the Emperium, castle guardians, guild flags, treasure boxes).
        /// Since client wave 4b (w4granny) Loki draws them in 3-D too (Sprites/GrannyUnit.cs, from the player's GRF); these nearest 2-D sprites of
        /// the same GRF stay as the fallback: while a model loads, when it cannot be read or decoded, and for a .gr2 name outside
        /// roBrowserLegacy's roster (a Poring, as rB draws).
        /// </summary>
        private static readonly Dictionary<string, string> Gr2StandIns = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "Empelium90_0.gr2", $"data\\sprite\\{Monster}\\s_empel_1" },             // Emperium → the WoE SE guardian stone
            { "Kguardian90_7.gr2", $"data\\sprite\\{Monster}\\md_ed_lgthighguard" },   // knight guardian
            { "Sguardian90_9.gr2", $"data\\sprite\\{Monster}\\md_ed_lgtguard" },       // soldier guardian
            { "Aguardian90_8.gr2", $"data\\sprite\\{Monster}\\beta_guards" },          // archer guardian
            { "Guildflag90_1.gr2", "data\\sprite\\npc\\1_flag_eagle" },
            { "TREASUREBOX_2.gr2", "data\\sprite\\npc\\4_treasure_box" },
        };
        private static string Gr2StandIn(string name) =>
            name == null || !name.EndsWith(".gr2", System.StringComparison.OrdinalIgnoreCase) ? null : Gr2StandIns.TryGetValue(name, out var s) ? s : $"data\\sprite\\{Monster}\\poring";

        /// <summary>Client wave 4b (w4granny): the Granny model a class's job name names ("Empelium90_0.gr2"), or null — the same lookup as
        /// <see cref="BodyPath(int, int, bool)"/> (an NPC's or a monster's row of the name table; players, homunculi and mercenaries have none).</summary>
        public static string Gr2Name(int job)
        {
            EnsureLoaded();
            if (IsPlayer(job) || IsHomunculus(job) || IsMercenary(job) || job == 45 || job == 111 || job == 139 || job == 2337) return null;
            string n = IsNpc(job) ? (_npc.TryGetValue(job, out var a) ? a : _npc.TryGetValue(46, out var b) ? b : null) : _npc.TryGetValue(job, out var c) ? c : null;
            return n != null && n.EndsWith(".gr2", System.StringComparison.OrdinalIgnoreCase) ? n : null;
        }

        /// <summary>Path without extension for a unit's body sprite, or null when the unit has no sprite (e.g. warp portals).</summary>
        public static string BodyPath(int job, int sex) => BodyPath(job, sex, IsDoram(job));

        /// <summary>
        /// Client wave 5 (w5looks): a sprite name with every run of backslashes made one. The mercenaries' names in the player's own
        /// data\luafiles514\lua files\datainfo\jobname.lub carry the sex folder with TWO backslash bytes — string constants of 11 bytes such as
        /// JT_MER_ARCHER01 = "여\\활용병", JT_MER_LANCER01 = "남\\창용병", the swordman's "남\\검용병" (the only three doubled separators of that
        /// file, at bytes 158271 / 158497 / 158725 of the 2026-09-30 LATAM GRF) — and the first-run table keeps Gravity's bytes as they are
        /// (Data/GrfTools.cs SpriteNames = tools/clientdata/build_spritenames.py: rows 6017-6046 of spritenames.txt). The bodies are at
        /// data\sprite\인간족\몸통\남\검용병 etc. with one separator, as roBrowserLegacy has the names (DB/Monsters/MonsterTable.js:3534, 3554:
        /// one backslash; DBManager.js:1229-1234 builds the path from them). Not done in RoPath.Normalize: the GRF itself holds 532 entries
        /// whose names start "data\\" with two separators (e.g. data\\sprite\로브\…), which a global fold would make unreachable.
        /// </summary>
        public static string OneSeparator(string name)
        {
            if (string.IsNullOrEmpty(name) || name.IndexOf("\\\\", System.StringComparison.Ordinal) < 0) return name;
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char ch in name) if (ch != '\\' || sb.Length == 0 || sb[sb.Length - 1] != '\\') sb.Append(ch);
            return sb.ToString();
        }

        /// <summary>Body sprite path with the race folder chosen by the caller (client wave 4, w4units: a Doram in a costume outfit keeps
        /// the Doram folder, UnitLooks.Drawn.Doram).</summary>
        public static string BodyPath(int job, int sex, bool doram)
        {
            EnsureLoaded();
            if (job == 45 || job == 111 || job == 139 || job == 2337) return null;
            if (IsPlayer(job))
            {
                string race = doram ? Doram : Human;
                string name = PcName(job) ?? _pc[0];
                return $"data\\sprite\\{race}\\{Body}\\{Sex(sex)}\\{name}_{Sex(sex)}";
            }
            if (IsNpc(job)) { string nn = _npc.TryGetValue(job, out var n1) ? n1 : _npc[46]; return Gr2StandIn(nn) ?? "data\\sprite\\npc\\" + nn.ToLowerInvariant(); }
            if (IsHomunculus(job)) return "data\\sprite\\homun\\" + (_npc.TryGetValue(job, out var h) ? h : "").ToLowerInvariant();
            if (IsMercenary(job)) return $"data\\sprite\\{Human}\\{Body}\\" + OneSeparator(_npc.TryGetValue(job, out var m) ? m : ""); // w5looks: Gravity's "남\\검용병"
            if (_npc.TryGetValue(job, out var mob)) return Gr2StandIn(mob) ?? $"data\\sprite\\{Monster}\\" + mob.ToLowerInvariant();
            return $"data\\sprite\\{Monster}\\poring";
        }

        public static string HeadPath(int hairStyle, int job, int sex)
        {
            EnsureLoaded();
            string race = IsDoram(job) ? Doram : Human;
            return $"data\\sprite\\{race}\\{Head}\\{Sex(sex)}\\{HairIndex(hairStyle, job, sex)}_{Sex(sex)}";
        }

        /// <summary>Hair style → head sprite / hair palette index (roBrowserLegacy HairIndexTable; doram styles are identity).</summary>
        public static int HairIndex(int hairStyle, int job, int sex)
        {
            if (IsDoram(job)) return hairStyle;
            var tbl = sex == 0 ? HairF : HairM;
            return hairStyle >= 0 && hairStyle < tbl.Length ? tbl[hairStyle] : hairStyle;
        }

        /// <summary>Body sprite file name of a PC job (pc rows; null when the job has none).</summary>
        public static string PcName(int job)
        {
            EnsureLoaded();
            if (_pc.TryGetValue(job, out var n)) return UnitLooks.CorrectedName(job, n);
            // client wave 4 (w4units): classes the name table lacks (babies, ARCH_BISHOP_2ND, the Hanbok / Oktoberfest / Summer 2 outfits)
            string extra = UnitLooks.ExtraName(job);
            if (extra != null) return extra;
            int src = UnitLooks.NameSource(job);
            return src >= 0 && _pc.TryGetValue(src, out var s) ? s : null;
        }
        /// <summary>True when the name table itself has a row for the class (the smoke's job table).</summary>
        public static bool HasPcRow(int job) { EnsureLoaded(); return _pc.ContainsKey(job); }
        /// <summary>WeaponNameTable suffix for a weapon view id ("_검", "_1207", …), or null.</summary>
        public static string WeaponName(int view) { EnsureLoaded(); return _wpn.TryGetValue(view, out var n) && !string.IsNullOrEmpty(n) ? n : null; }
        /// <summary>RobeNameTable folder name for a garment view id, or null.</summary>
        public static string RobeName(int view) { EnsureLoaded(); return view > 0 && _robe.TryGetValue(view, out var n) && !string.IsNullOrEmpty(n) ? n : null; }

        public static string HeadgearPath(int viewId, int sex)
        {
            EnsureLoaded();
            if (viewId <= 0 || !_acc.TryGetValue(viewId, out var n)) return null;
            return $"data\\sprite\\{Accessory}\\{Sex(sex)}\\{Sex(sex)}{n}"; // AccNameTable entries start with '_'
        }

        public static string MobName(int id) { EnsureLoaded(); return _npc.TryGetValue(id, out var n) ? n : null; }
        /// <summary>Client wave 11 (w11menu): every id of the client's NPC / monster identity table with its sprite name (the console
        /// commands' monster browser looks a monster's id up by name: Game/GmPickers.cs).</summary>
        public static IEnumerable<KeyValuePair<int, string>> NpcSprites() { EnsureLoaded(); return _npc; }
    }
}
