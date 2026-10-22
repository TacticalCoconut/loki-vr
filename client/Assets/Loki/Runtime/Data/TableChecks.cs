using System;
using System.Collections.Generic;
using System.Globalization;
using Loki.IO;

namespace Loki.Data
{
    /// <summary>
    /// Client wave 7 (w7data): is a table the first-run builder just made fit to replace the last good one? A table that fails
    /// here is not swapped in: the player keeps the last good version and the game starts (Data/ClientDataBuilder.cs).
    /// Pure C# (no UnityEngine).
    ///
    /// The checks, in this order (the first that fails is the reason):
    ///  1. It parses: no NUL byte in a text table; a JSON table parses as one object.
    ///  2. Rows: each data row (not empty, not a "#" comment) has at least the columns its loader reads. The loaders skip a
    ///     shorter row (their "if (c.Length &lt; n) continue"), so up to 1 row in 100 may be malformed (counted, reported); more
    ///     than that means the source or the port went wrong.
    ///  3. The fixed minimum of rows: about a quarter of what the 2026-09-30 LATAM install (patch 1486) gives, rounded down.
    ///     Reason: these tables only grow from patch to patch (items, skills, maps, quests are added); a quarter of today's
    ///     size is far below any install of this client generation and far above a cut-off or wrongly read source. It is a
    ///     judgement, not a number Gravity publishes.
    ///  4. The key rows every session needs (the Novice and the six first jobs, Red Potion 501, Basic Skill and Bash, prontera,
    ///     the Poring's sprite, message 0 …): a table without them is not that table.
    ///  5. Against the last good set made from the same install folder: at least 80 % of its rows. Reason: a patch adds rows
    ///     and now and then removes a few (an event's items); losing a fifth of a table in one patch has not happened and looks
    ///     like a half-read source. There is no upper bound: growth is the normal case. So that this judgement can never lock a
    ///     player out, a table refused ONLY for this reason is accepted when the next start produces the very same bytes again
    ///     (the same result twice is what the install really holds, not a half-written file).
    /// </summary>
    public static class TableChecks
    {
        public sealed class Rule
        {
            public string File;
            /// <summary>Fixed minimum of data rows (JSON: of entries), and the 2026-09-30 count it was taken from.</summary>
            public int MinRows, Seen;
            /// <summary>Text tables: the columns a row needs, by the row's first column ("" = every row).</summary>
            public Dictionary<string, int> Cols;
            /// <summary>Line starts that must be present (text tables), keys that must be present (JSON).</summary>
            public string[] Keys = new string[0];
            public bool Json;
        }

        public sealed class Result
        {
            public bool Ok; public int Rows, BadRows;
            /// <summary>Why the table is refused (null when it is good).</summary>
            public string Problem;
            /// <summary>True when the only failed check is the comparison with the last good set (check 5).</summary>
            public bool OnlyRelative;
        }

        public const double RelativeFloor = 0.8, BadRowShare = 0.01;

        private static Dictionary<string, int> One(int n) => new Dictionary<string, int> { { "", n } };
        private static Dictionary<string, int> Kinds(params object[] kv)
        {
            var d = new Dictionary<string, int>();
            for (int i = 0; i < kv.Length; i += 2) d[(string)kv[i]] = (int)kv[i + 1];
            return d;
        }

        /// <summary>One rule per generated file (ClientDataBuilder.GeneratedFiles). Seen = rows on 2026-10-04 (LATAM, patch 1486).</summary>
        public static readonly Rule[] Rules =
        {
            new Rule { File = "msgstrings.tsv", Seen = 4385, MinRows = 1000, Cols = One(3), Keys = new[] { "0\t" } },
            new Rule { File = "mapnames.tsv", Seen = 1143, MinRows = 280, Cols = One(3), Keys = new[] { "prontera\t" } },
            // job names: the J rows; the Novice and the six first jobs by id and Gravity's own constant
            new Rule { File = "skilltree.tsv", Seen = 4162, MinRows = 1000, Cols = Kinds("J", 7, "S", 6, "T", 4, "N", 4),
                       Keys = new[] { "J\t0\tJT_NOVICE\t", "J\t1\tJT_SWORDMAN\t", "J\t2\tJT_MAGICIAN\t", "J\t3\tJT_ARCHER\t", "J\t4\tJT_ACOLYTE\t", "J\t5\tJT_MERCHANT\t", "J\t6\tJT_THIEF\t" } },
            new Rule { File = "skills.tsv", Seen = 1806, MinRows = 450, Cols = One(6), Keys = new[] { "1\tNV_BASIC\t", "5\tSM_BASH\t" } },
            new Rule { File = "randomoptions.tsv", Seen = 251, MinRows = 60, Cols = One(5), Keys = new[] { "1\tVAR_MAXHPAMOUNT\t" } },
            new Rule { File = "worldmap.tsv", Seen = 487, MinRows = 120, Cols = Kinds("W", 5, "M", 11, "D", 10, "I", 3), Keys = new[] { "W\t0\t" } }, // UI/WorldMapWindow.cs:48-61
            new Rule { File = "spritenames.txt", Seen = 8831, MinRows = 2200, Cols = Kinds("pc", 3, "npc", 3, "acc", 3, "robe", 3, "wpn", 3), Keys = new[] { "pc\t0\t", "npc\t1002\t" } },
            new Rule { File = "efst.tsv", Seen = 1245, MinRows = 310, Cols = One(4), Keys = new[] { "0\tEFST_PROVOKE\t" } },
            new Rule { File = "items.tsv", Seen = 30701, MinRows = 7500, Cols = One(7), Keys = new[] { "501\t" } },
            new Rule { File = "quests.tsv", Seen = 10482, MinRows = 2600, Cols = One(5) },
            new Rule { File = "account/achievements_en.json", Seen = 363, MinRows = 90, Json = true },
            new Rule { File = "account/achievements_pt.json", Seen = 363, MinRows = 90, Json = true },
            new Rule { File = "account/titles.tsv", Seen = 58, MinRows = 14, Cols = One(3) },
            new Rule { File = "emotions.tsv", Seen = 88, MinRows = 22, Cols = One(2), Keys = new[] { "0\t" } },
            new Rule { File = "stateicons.tsv", Seen = 771, MinRows = 190, Cols = One(3), Keys = new[] { "0\t" } },
            new Rule { File = "appearance.txt", Seen = 3459, MinRows = 860, Cols = One(3), Keys = new[] { "wjob\t0\t" } },
            new Rule { File = "itemviews.tsv", Seen = 12104, MinRows = 3000, Cols = One(5), Keys = new[] { "1101\t" } },
            // rows = the projectile + spawn + sprites entries
            new Rule { File = "fx/mob_attack_fx.json", Seen = 114, MinRows = 28, Json = true, Keys = new[] { "projectile", "spawn", "sprites" } },
            new Rule { File = "mapsky.tsv", Seen = 106, MinRows = 26, Cols = Kinds("S", 6, "C", 19) },
            new Rule { File = "navigation.tsv", Seen = 27755, MinRows = 6900, Cols = Kinds("M", 6, "L", 12, "E", 3, "N", 9, "D", 3, "B", 9, "T", 7), Keys = new[] { "M\tprontera\t" } }, // Game/Navigation.cs:74-86
            new Rule { File = "hateffects.tsv", Seen = 254, MinRows = 60, Cols = One(8) },
            // client wave 20 (w20rooms): 150 I + 1314 R + 130 G rows on 2026-10-10 (LATAM, patch 1486); World/RoomMask.cs reads I 10, R 10, G 3 columns
            new Rule { File = "rooms.tsv", Seen = 1594, MinRows = 390, Cols = Kinds("I", 10, "R", 10, "G", 3), Keys = new[] { "I\tprt_in\t" } },
        };

        public static Rule For(string rel)
        {
            foreach (var r in Rules) if (r.File == rel) return r;
            return null;
        }

        /// <summary>Checks a table. <paramref name="lastGoodRows"/>: the rows of the last good version made from the same install
        /// folder, or -1 when there is none to compare with.</summary>
        public static Result Check(string rel, byte[] data, int lastGoodRows)
        {
            var res = new Result();
            var rule = For(rel);
            if (data == null || data.Length == 0) { res.Problem = "the table is empty"; return res; }
            if (rule == null) { res.Ok = true; res.Rows = -1; return res; } // a table without a rule (none today) is taken as made
            try
            {
                string problem = rule.Json ? CheckJson(rule, data, res) : CheckText(rule, data, res);
                if (problem != null) { res.Problem = problem; return res; }
            }
            catch (Exception e) { res.Problem = "the table does not parse (" + e.GetType().Name + ": " + e.Message.Trim() + ")"; return res; }
            if (res.Rows < rule.MinRows) { res.Problem = $"{res.Rows} rows, fewer than the minimum of {rule.MinRows} (a real install gives about {rule.Seen})"; return res; }
            if (lastGoodRows > 0 && res.Rows < lastGoodRows * RelativeFloor)
            {
                res.Problem = $"{res.Rows} rows, fewer than {RelativeFloor * 100:0} % of the last good table's {lastGoodRows}";
                res.OnlyRelative = true; return res;
            }
            res.Ok = true; return res;
        }

        /// <summary>The data rows of a table as <see cref="Check"/> counts them (-1 when it cannot be counted).</summary>
        public static int CountRows(string rel, byte[] data)
        {
            var rule = For(rel);
            if (rule == null || data == null) return -1;
            var res = new Result();
            try { if (rule.Json) CheckJson(rule, data, res); else CheckText(rule, data, res); } catch { return -1; }
            return res.Rows;
        }

        private static string CheckText(Rule rule, byte[] d, Result res)
        {
            if (Array.IndexOf(d, (byte)0) >= 0) return "the table holds a NUL byte (binary data where text should be)";
            var found = new bool[rule.Keys.Length];
            var keys = new byte[rule.Keys.Length][];
            for (int k = 0; k < keys.Length; k++) keys[k] = Py.Utf8NoBom.GetBytes(rule.Keys[k]);
            int rows = 0, bad = 0; string firstBad = null;
            int p = 0, n = d.Length;
            while (p < n)
            {
                int e = Array.IndexOf(d, (byte)'\n', p); if (e < 0) e = n;
                int end = e > p && d[e - 1] == (byte)'\r' ? e - 1 : e;
                if (end > p && d[p] != (byte)'#')
                {
                    rows++;
                    int cols = 1, firstTab = -1;
                    for (int i = p; i < end; i++) if (d[i] == (byte)'\t') { if (firstTab < 0) firstTab = i; cols++; }
                    int need;
                    if (!rule.Cols.TryGetValue("", out need))
                    {
                        string kind = Py.Latin1(Slice(d, p, (firstTab < 0 ? end : firstTab) - p));
                        if (!rule.Cols.TryGetValue(kind, out need)) need = int.MaxValue; // a row kind the loader does not know
                    }
                    if (cols < need) { bad++; if (firstBad == null) firstBad = $"row {rows} has {cols} column(s)" + (need == int.MaxValue ? " of an unknown kind" : $", {need} needed"); }
                    for (int k = 0; k < keys.Length; k++) if (!found[k] && StartsWith(d, p, end, keys[k])) found[k] = true;
                }
                p = e + 1;
            }
            res.Rows = rows; res.BadRows = bad;
            if (bad > rows * BadRowShare) return $"{bad} of {rows} rows are malformed ({firstBad})";
            for (int k = 0; k < keys.Length; k++) if (!found[k]) return "the row \"" + rule.Keys[k].TrimEnd('\t').Replace("\t", " ") + "\" is missing";
            return null;
        }

        private static string CheckJson(Rule rule, byte[] d, Result res)
        {
            var o = MiniJson.AsObject(MiniJson.Parse(Py.Utf8NoBom.GetString(d)));
            if (o == null) return "the table is not a JSON object";
            if (rule.Keys.Length == 0) res.Rows = o.Count;
            else
            {
                int rows = 0;
                foreach (var k in rule.Keys)
                {
                    if (!o.TryGetValue(k, out var v)) return "the entry \"" + k + "\" is missing";
                    var sub = MiniJson.AsObject(v); var list = MiniJson.AsList(v);
                    rows += sub != null ? sub.Count : list != null ? list.Count : 0;
                }
                res.Rows = rows;
            }
            return null;
        }

        private static byte[] Slice(byte[] d, int at, int len) { var b = new byte[len]; Array.Copy(d, at, b, 0, len); return b; }

        private static bool StartsWith(byte[] d, int at, int end, byte[] key)
        {
            if (end - at < key.Length) return false;
            for (int i = 0; i < key.Length; i++) if (d[at + i] != key[i]) return false;
            return true;
        }
    }
}
