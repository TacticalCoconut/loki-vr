using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Texts the client looks up by id or map name:
    ///  • data/msgstrings.tsv — the client's msgstringtable (data\msgstringtable_ml.csv in the user's LATAM GRF: base64
    ///    columns, English = column 2, Portuguese = column 7; the row number is the 0-based message id rAthena sends,
    ///    verified: 52 = MSI_CANT_GET_ITEM_BECAUSE_WEIGHT, 1923 = MSI_BUSY, 1574/1575 = MSI_SKILL_SUCCESS/FAIL).
    ///  • data/mapnames.tsv — data\english\mapnametable.txt and data\mapnametable.txt (map → display name).
    /// Client wave 4b (w4lang): <see cref="Portuguese"/> follows the language setting (Lang.cs); loki.json "textLanguage" is no longer read.
    /// </summary>
    public static class ClientText
    {
        private static readonly List<string> _msgEn = new List<string>(), _msgPt = new List<string>();
        private static readonly Dictionary<string, (string en, string pt)> _maps = new Dictionary<string, (string, string)>();
        private static bool _loaded;
        public static bool Portuguese;

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            string dir = Path.Combine(Application.streamingAssetsPath, "data");
            string p = Loki.Data.ClientData.File("msgstrings.tsv"); // w3data: the per-user copy first
            if (File.Exists(p))
                foreach (var line in File.ReadLines(p, Encoding.UTF8))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    var c = line.Split('\t');
                    if (c.Length < 3 || !int.TryParse(c[0], out int id)) continue;
                    while (_msgEn.Count <= id) { _msgEn.Add(""); _msgPt.Add(""); }
                    _msgEn[id] = c[1].Replace("\u200b", "").Replace("\\n", "\n"); _msgPt[id] = c[2].Replace("\u200b", "").Replace("\\n", "\n"); // wave 28: zero-width spaces out (older tables)
                }
            else Debug.LogWarning("[ClientText] missing " + p);
            p = Loki.Data.ClientData.File("mapnames.tsv");
            if (File.Exists(p))
                foreach (var line in File.ReadLines(p, Encoding.UTF8))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    var c = line.Split('\t');
                    if (c.Length >= 3) _maps[c[0].ToLowerInvariant()] = (c[1].Replace("\u200b", ""), c[2].Replace("\u200b", "")); // wave 28: keys as MapName looks them up
                }
            ApplyPack();
            int noEnMsg = 0, noEnMap = 0;
            for (int i = 0; i < _msgEn.Count; i++) if (_msgEn[i].Length == 0 && _msgPt[i].Length > 0) noEnMsg++;
            foreach (var m in _maps.Values) if (m.en.Length == 0 && m.pt.Length > 0) noEnMap++;
            Debug.Log($"[ClientText] {_msgEn.Count} messages, {_maps.Count} map names; with Portuguese and no English (never shown in English): {noEnMsg} messages, {noEnMap} map names");
        }

        /// <summary>Wave 28 (wtextc): the text pack's msgstrings.tsv (id, en, pt) and mapnames.tsv (map, en, pt) over the player's
        /// tables: a non-empty pack field wins; a row the player's table lacks is ignored (counted).</summary>
        private static void ApplyPack()
        {
            var tm = Loki.Data.TextPack.Table("msgstrings.tsv");
            if (tm != null)
            {
                int applied = 0, unknown = 0; string first = null;
                foreach (var c in tm.Rows)
                {
                    if (!int.TryParse(c[0], out int id) || id < 0 || id >= _msgEn.Count) { unknown++; if (first == null) first = c[0]; continue; }
                    _msgEn[id] = Loki.Data.TextPack.Pick(c[1], _msgEn[id]); _msgPt[id] = Loki.Data.TextPack.Pick(c[2], _msgPt[id]); applied++;
                }
                Loki.Data.TextPack.Report(tm, applied, unknown, first);
            }
            var tn = Loki.Data.TextPack.Table("mapnames.tsv");
            if (tn != null)
            {
                int applied = 0, unknown = 0; string first = null;
                foreach (var c in tn.Rows)
                {
                    string k = c[0].ToLowerInvariant();
                    if (!_maps.TryGetValue(k, out var n)) { unknown++; if (first == null) first = c[0]; continue; }
                    _maps[k] = (Loki.Data.TextPack.Pick(c[1], n.en), Loki.Data.TextPack.Pick(c[2], n.pt)); applied++;
                }
                Loki.Data.TextPack.Report(tn, applied, unknown, first);
            }
        }

        /// <summary>Wave 28: every message and map name in the game's language (the no-Portuguese check reads them).</summary>
        public static IEnumerable<string> AllTexts()
        {
            Load();
            for (int i = 0; i < _msgEn.Count; i++) { string s = Msg(i); if (!s.StartsWith("(message #", System.StringComparison.Ordinal)) yield return s; }
            foreach (var k in new List<string>(_maps.Keys)) yield return MapName(k);
        }

        /// <summary>The message for a msgstringtable id, or "(message #id)" when unknown.</summary>
        public static string Msg(int id)
        {
            Load();
            if (id >= 0 && id < _msgEn.Count)
            {
                string s = Portuguese && _msgPt[id].Length > 0 ? _msgPt[id] : _msgEn[id];
                if (s.Length == 0 && Portuguese) s = _msgPt[id]; // wave 28 (wtextc): English never shows the Portuguese column (it did when the English one was empty)
                if (s.Length > 0) return s;
            }
            return Lang.F("(message #{0})", id); // w4lang
        }

        private static readonly System.Text.RegularExpressions.Regex MsgTag = new System.Text.RegularExpressions.Regex(@"<MSG>(\d+)(?:,([^<]*))?</MSG>");
        /// <summary>
        /// Expands the client's &lt;MSG&gt;n&lt;/MSG&gt; / &lt;MSG&gt;n,arg,…&lt;/MSG&gt; references with msgstringtable text (0-based, as ZC_MSG) —
        /// rAthena writes them into system mails, e.g. the daily check-in reward (conf/msg_conf/map_msg.conf:856-862: 3455 "Management",
        /// 3456 "%d Day Login Reward").
        /// </summary>
        public static string Expand(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf("<MSG>", System.StringComparison.Ordinal) < 0) return s ?? "";
            return MsgTag.Replace(s, m =>
            {
                string t = Msg(int.Parse(m.Groups[1].Value));
                if (m.Groups[2].Success) foreach (var a in m.Groups[2].Value.Split(',')) t = Fill(t, a.Trim());
                return t;
            });
        }

        /// <summary>Fills the first %d/%s/%.2f-style placeholder with a value (ZC_MSG_VALUE: "the value fills the %d").</summary>
        public static string Fill(string fmt, string value)
        {
            int i = fmt.IndexOf('%');
            while (i >= 0 && i + 1 < fmt.Length)
            {
                int j = i + 1;
                while (j < fmt.Length && (char.IsDigit(fmt[j]) || fmt[j] == '.' || fmt[j] == '-')) j++;
                if (j < fmt.Length && (fmt[j] == 'd' || fmt[j] == 's' || fmt[j] == 'f' || fmt[j] == 'u' || fmt[j] == 'i'))
                    return fmt.Substring(0, i) + value + fmt.Substring(j + 1);
                i = fmt.IndexOf('%', i + 1);
            }
            return fmt + " " + value;
        }

        /// <summary>"prontera" / "prontera.gat" → "Prontera City, …" (falls back to the map id).</summary>
        public static string MapName(string map)
        {
            Load();
            if (string.IsNullOrEmpty(map)) return "";
            string k = map.ToLowerInvariant();
            int dot = k.IndexOf('.'); if (dot > 0) k = k.Substring(0, dot);
            if (_maps.TryGetValue(k, out var n))
            {
                string s = Portuguese && n.pt.Length > 0 ? n.pt : n.en;
                if (s.Length == 0 && Portuguese) s = n.pt; // wave 28 (wtextc): English never shows the Portuguese name
                if (s.Length > 0) return s;
            }
            if (!Portuguese) { string en = Navigation.EnglishMapName(k); if (en.Length > 0) return en; } // wave 28: the install's English map names (System\english\mapinfo.lub)
            return k;
        }
    }
}
