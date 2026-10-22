using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Loki.IO;

namespace Loki.Data
{
    /// <summary>
    /// Wave 28 (wtextc): the text pack — a folder of rewritten game texts (the 2026-10-15 text overhaul: item, quest, skill, map,
    /// status, message, world map, navigation, achievement and title texts in English and Brazilian Portuguese) read at run time,
    /// OUTSIDE the build. Its rows override the first-run tables (<see cref="ClientDataBuilder"/>, made from the player's own game
    /// data) row by row, field by field: a non-empty pack field replaces the table's field of the same row; an empty one keeps it.
    /// A row whose key the player's table does not have is ignored (counted). Without a pack the game shows the first-run tables
    /// as before.
    ///
    /// Where: loki.json "textPack" (<see cref="World.LokiConfig.textPack"/>): an absolute folder, or a folder relative to the game's
    /// own folder (the one holding Loki.exe); the default "textpack" is the folder next to the game, where the friends' package puts
    /// it (client/release/make-friends-package.ps1 -TextPack); "" turns the pack off.
    ///
    /// The folder holds textpack.json (the manifest: format 1, name, version, and every file with its SHA-256 and size) and the
    /// tables it lists (UTF-8, tab-separated, "#" lines are comments; the columns of each are <see cref="Columns"/>). A file is used
    /// only when it is listed and its SHA-256 matches (a damaged or half-copied file is refused whole, logged), it is valid UTF-8,
    /// and then row by row: the key present, no U+FFFD, no control characters, no zero-width spaces (a row failing any is skipped,
    /// counted). client/tools/textpack/build_textpack.py makes the folder from the overhaul's tables and does the deeper checks
    /// (ids against the player's data, no empty names, no lost Portuguese, no information lost).
    /// Pure C# (no UnityEngine): the bootstrap gives the game's folder and the log.
    /// </summary>
    public static class TextPack
    {
        public const string ManifestName = "textpack.json";
        public const int Format = 1;

        /// <summary>The tables a pack may hold and their columns (the key columns first). "\n" in a text is written "\\n", as in the
        /// first-run tables.</summary>
        public static readonly Dictionary<string, string[]> Columns = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "items.tsv", new[] { "id", "name_en", "name_pt", "desc_en", "desc_pt" } },
            { "quests.tsv", new[] { "id", "title_en", "title_pt", "summary_en", "summary_pt", "desc_en", "desc_pt" } },
            { "skills.tsv", new[] { "id", "name_en", "name_pt", "desc_en", "desc_pt" } },
            { "efst.tsv", new[] { "id", "title_en", "title_pt" } },
            { "msgstrings.tsv", new[] { "id", "en", "pt" } },
            { "mapnames.tsv", new[] { "map", "en", "pt" } },
            // W: world index, "" / M: world, map / D: world, dungeon group
            { "worldmap.tsv", new[] { "kind", "world", "key", "en", "pt" } },
            // M: map, English name, Portuguese name / N and L: id, name, label / B: map, monster id, name
            { "navigation.tsv", new[] { "kind", "key", "a", "b" } },
            { "account/titles.tsv", new[] { "id", "pt", "en" } },
            // job names (the skill tree's J rows): job id, English, Portuguese
            { "skilltree.tsv", new[] { "job", "name_en", "name_pt" } },
        };

        /// <summary>The JSON tables a pack may hold: { id: { n, s, d, res: [[text, count], …] } } (only the texts are read).</summary>
        public static readonly string[] JsonFiles = { "account/achievements_en.json", "account/achievements_pt.json" };

        public sealed class PackTable
        {
            public string File;
            public readonly List<string[]> Rows = new List<string[]>();
            /// <summary>Rows the pack check skipped (malformed, bad characters) and the first one's reason.</summary>
            public int Rejected; public string FirstReject;
        }

        /// <summary>Where log lines go (the bootstrap sets Debug.Log).</summary>
        public static Action<string> Log = _ => { };

        private static readonly object Gate = new object();
        private static string _folder; private static bool _manifestRead;
        private static Dictionary<string, (string sha, long size)> _files;
        private static readonly Dictionary<string, PackTable> _tables = new Dictionary<string, PackTable>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Dictionary<string, object>> _json = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);

        /// <summary>The pack folder in use, or null.</summary>
        public static string Folder { get { lock (Gate) { ReadManifest(); return _files != null ? _folder : null; } } }
        /// <summary>True when a pack with a readable manifest is in use.</summary>
        public static bool Active => Folder != null;
        /// <summary>What the pack is, or why there is none (the log and the Settings window's data line).</summary>
        public static string Status { get { lock (Gate) { ReadManifest(); return _status; } } }
        private static string _status = "not configured";
        /// <summary>The manifest's name and version ("" without a pack).</summary>
        public static string Name { get; private set; } = "";
        public static string Version { get; private set; } = "";

        /// <summary>Once at start-up: <paramref name="configured"/> is loki.json's "textPack", <paramref name="gameDir"/> the folder of
        /// Loki.exe (a relative path is taken from there).</summary>
        public static void Configure(string configured, string gameDir)
        {
            lock (Gate)
            {
                _tables.Clear(); _json.Clear(); _files = null; _manifestRead = false; Name = ""; Version = "";
                _folder = Resolve(configured, gameDir);
                _status = _folder == null ? "off (loki.json \"textPack\" is empty)" : "not read yet: " + _folder;
            }
        }

        /// <summary>loki.json's value as a folder: null for "", the path itself when rooted, else under the game's folder.</summary>
        public static string Resolve(string configured, string gameDir)
        {
            if (string.IsNullOrWhiteSpace(configured)) return null;
            string p = configured.Trim();
            try { return Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(gameDir ?? "", p)); }
            catch (Exception) { return p; }
        }

        private static void ReadManifest()
        {
            if (_manifestRead) return;
            _manifestRead = true;
            if (_folder == null) return;
            string mf = Path.Combine(_folder, ManifestName);
            if (!Directory.Exists(_folder) || !File.Exists(mf)) { _status = "none (no " + mf + ")"; Log("[TextPack] no text pack: " + _status); return; }
            try
            {
                var o = MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(mf, new UTF8Encoding(false, true))));
                if (o == null) throw new Exception("the manifest is not a JSON object");
                int fmt = MiniJson.Int(o.TryGetValue("format", out var f) ? f : null, -1);
                if (fmt != Format) throw new Exception("manifest format " + fmt + ", this game reads format " + Format);
                var files = new Dictionary<string, (string, long)>(StringComparer.Ordinal);
                foreach (var e in MiniJson.AsList(o.TryGetValue("files", out var fl) ? fl : null) ?? new List<object>())
                {
                    var fo = MiniJson.AsObject(e); if (fo == null) continue;
                    string path = MiniJson.Str(fo.TryGetValue("path", out var pv) ? pv : null, ""), sha = MiniJson.Str(fo.TryGetValue("sha256", out var sv) ? sv : null, "");
                    if (path.Length > 0 && sha.Length == 64) files[path] = (sha.ToLowerInvariant(), (long)MiniJson.Num(fo.TryGetValue("bytes", out var bv) ? bv : null, -1));
                }
                Name = MiniJson.Str(o.TryGetValue("name", out var nv) ? nv : null, "");
                Version = MiniJson.Str(o.TryGetValue("version", out var vv) ? vv : null, "");
                _files = files;
                _status = $"'{Name}' {Version}, {files.Count} files, {_folder}";
                Log($"[TextPack] text pack {_status}");
            }
            catch (Exception e) { _status = "refused (" + mf + ": " + e.Message + ")"; Log("[TextPack] the text pack is not used: " + _status); }
        }

        /// <summary>The bytes of a listed file whose SHA-256 matches the manifest, or null (logged once).</summary>
        private static byte[] Verified(string rel)
        {
            if (_files == null || !_files.TryGetValue(rel, out var want)) return null;
            string p = Path.Combine(_folder, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(p)) { Log($"[TextPack] {rel}: listed in the manifest but missing ({p}): not used"); return null; }
            var b = File.ReadAllBytes(p);
            string sha;
            using (var h = SHA256.Create()) sha = ClientDataBuilder.Hex(h.ComputeHash(b));
            if (sha != want.sha || (want.size >= 0 && b.Length != want.size))
            {
                Log($"[TextPack] {rel}: SHA-256 {sha} ({b.Length} bytes) is not the manifest's {want.sha} ({want.size} bytes): a damaged or changed file, not used");
                return null;
            }
            return b;
        }

        /// <summary>The UTF-8 text of any file the manifest lists whose SHA-256 matches (e.g. the Codex's codex/creatures.tsv), or null
        /// when there is no pack, the file is not listed, changed or not UTF-8.</summary>
        public static string Text(string rel)
        {
            lock (Gate)
            {
                ReadManifest();
                var b = Verified(rel);
                if (b == null) return null;
                try { return new UTF8Encoding(false, true).GetString(b); }
                catch (DecoderFallbackException) { Log($"[TextPack] {rel}: not valid UTF-8: not used"); return null; }
            }
        }

        /// <summary>A pack table's checked rows, or null when there is no pack or the pack has no usable file of that name.</summary>
        public static PackTable Table(string rel)
        {
            lock (Gate)
            {
                ReadManifest();
                if (_files == null) return null;
                if (_tables.TryGetValue(rel, out var cached)) return cached;
                PackTable t = null;
                if (Columns.TryGetValue(rel, out var cols))
                {
                    var b = Verified(rel);
                    if (b != null)
                    {
                        string text = null;
                        try { text = new UTF8Encoding(false, true).GetString(b); }
                        catch (DecoderFallbackException) { Log($"[TextPack] {rel}: not valid UTF-8: not used"); }
                        if (text != null) t = Parse(rel, text, cols.Length);
                    }
                }
                _tables[rel] = t;
                return t;
            }
        }

        /// <summary>The rows of a table text (for the harness too): rows with fewer columns, an empty key, U+FFFD, a control character
        /// or a zero-width space are skipped and counted.</summary>
        public static PackTable Parse(string rel, string text, int columns)
        {
            var t = new PackTable { File = rel };
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
            int line = 0;
            foreach (var raw in text.Split('\n'))
            {
                line++;
                string l = raw.EndsWith("\r", StringComparison.Ordinal) ? raw.Substring(0, raw.Length - 1) : raw;
                if (l.Length == 0 || l[0] == '#') continue;
                var c = l.Split('\t');
                string why = c.Length < columns ? $"{c.Length} columns, {columns} needed" : c[0].Length == 0 ? "no key" : BadText(l);
                if (why != null) { t.Rejected++; if (t.FirstReject == null) t.FirstReject = $"line {line}: {why}"; continue; }
                t.Rows.Add(c);
            }
            return t;
        }

        /// <summary>Null when a line's text is clean, else what is wrong with it.</summary>
        public static string BadText(string s)
        {
            foreach (char ch in s)
            {
                if (ch == '\uFFFD') return "a U+FFFD (a character lost in a decoding)";
                if (ch == '\u200b') return "a zero-width space";
                if (ch < 0x20 && ch != '\t') return $"the control character U+{(int)ch:X4}";
            }
            return null;
        }

        /// <summary>A pack JSON table (achievements) as id → entry, or null.</summary>
        public static Dictionary<string, object> Json(string rel)
        {
            lock (Gate)
            {
                ReadManifest();
                if (_files == null) return null;
                if (_json.TryGetValue(rel, out var cached)) return cached;
                Dictionary<string, object> o = null;
                var b = Verified(rel);
                if (b != null)
                    try { o = MiniJson.AsObject(MiniJson.Parse(new UTF8Encoding(false, true).GetString(b))); }
                    catch (Exception e) { Log($"[TextPack] {rel}: does not parse ({e.Message}): not used"); }
                _json[rel] = o;
                return o;
            }
        }

        /// <summary>One log line per table a loader applied: rows used, rows the pack check skipped, rows whose key the player's table lacks.</summary>
        public static void Report(PackTable t, int applied, int unknown, string firstUnknown)
        {
            if (t == null) return;
            Log($"[TextPack] {t.File}: {applied} rows applied, {t.Rejected} skipped by the pack check{(t.FirstReject != null ? " (first: " + t.FirstReject + ")" : "")}, " +
                $"{unknown} not in your game data{(firstUnknown != null ? " (first: " + firstUnknown + ")" : "")}");
        }

        /// <summary>The pack's text when it has one ("\\n" made a line break), else the table's.</summary>
        public static string Pick(string pack, string table) => string.IsNullOrEmpty(pack) ? table : pack.Replace("\\n", "\n");
    }
}
