using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Loki.Effects
{
    /// <summary>
    /// Client wave 4 (w4fx): the newer skills' effects the official client keeps as data, the GRF's EZ2STREffect tables
    /// (data\contentdata\effectdata\ez2streffect.bson: 192 entries on the 2026-09-30 LATAM GRF; data\effectdata\ez2streffect.bson: 130 more,
    /// no key in common). Each entry names one STR file (FilePath under data\texture\effect), a wav, a start delay, whether it loops
    /// (IsInfinite), sits on the floor (IsFloor / IsToGround) and its PosX / PosY offset. They are Gravity's data: read from the player's own
    /// GRF when the effect manager starts, never shipped.
    ///
    /// The file does not say which skill plays which entry: the official client decides that in its code. roBrowserLegacy (a
    /// reimplementation, the only reference: DB/DBManager.js mergeEz2Effects, lines 4380-4560) guesses it by fuzzy name matching; Loki keeps
    /// only its EXACT rules, so no entry is tied to a skill on a partial match:
    ///  1. the entry name without its suffix equals a skill's aegis name (rB pass 1, exact key) — or the aegis name without its class prefix
    ///     when only one skill has that remainder (rB's skidFuzzy key form restricted to unambiguous keys; also used by rules 2 and 3);
    ///  2. a folder of its FilePath equals a skill's aegis name (rB pass 2 / 3, e.g. DRAGON_KNIGHT\DK_CHARGINGPIERCE\…);
    ///  3. its wav's file name equals a skill's aegis name (rB pass 4, e.g. effect\TR_ROKI_CAPRICCIO.wav);
    ///  4. a suffixed entry takes its base entry's skill (rB pass 5: calumitygale_cast → calumitygale).
    /// The suffix picks the moment (rB SUFFIX_TO_FIELD, DBManager.js:4383-4400): _hit / _hitsub / _target → hitEffectId; _cast, _cast_bottom,
    /// _single, _bottom, _begin, _start, _end, _loop, _buff, _aura, _main, _sync → effectId; no suffix → effectId, or groundEffectId for a
    /// floor entry (which rB never plays). The part is rB's (DBManager.js:4520-4536): one STR, attached to its owner (one cycle even when
    /// IsInfinite: see Load), delayed by StartDelayTime, offset by PosX / PosY.
    /// </summary>
    public static class Ez2Effects
    {
        public const string ContentPath = "data\\contentdata\\effectdata\\ez2streffect.bson", OlderPath = "data\\effectdata\\ez2streffect.bson";
        public static int Entries { get; private set; }
        public static int Mapped { get; private set; }
        public static int SkillsTouched { get; private set; }
        /// <summary>Entry names no exact rule tied to a skill (logged; the smoke prints them).</summary>
        public static readonly List<string> Unmapped = new List<string>();

        // rB DBManager.js:4383-4400, in rB's order (the first suffix the name ends with wins)
        private static readonly (string suffix, string field)[] SuffixToField =
        {
            ("hit", "hit"), ("hitsub", "hit"), ("target", "hit"), ("cast", "effect"), ("cast_bottom", "effect"), ("single", "effect"),
            ("bottom", "effect"), ("begin", "effect"), ("start", "effect"), ("end", "effect"), ("loop", "effect"), ("buff", "effect"),
            ("aura", "effect"), ("main", "effect"), ("sync", "effect"),
        };
        // rB HARDCODED_FIELD_MAPPING (DBManager.js:4405-4408)
        private static readonly Dictionary<string, string> Hardcoded = new Dictionary<string, string> { { "broken_limit_buff", "effect" }, { "abyss_flame_target", "hit" } };

        private sealed class Entry { public string Name, FilePath, SoundPath; public bool Floor, Infinite; public double PosX, PosY, Delay; }

        /// <summary>Reads both tables (missing ones are skipped), adds their effects to <see cref="EffectDb"/> and ties them to skills.
        /// skillIdOfAegis: the client's skill table (skills.tsv, from skillid.lub) upper-cased aegis name → skill id.</summary>
        public static void Load(Func<string, byte[]> read, IReadOnlyDictionary<string, int> skillIdOfAegis)
        {
            Entries = Mapped = SkillsTouched = 0; Unmapped.Clear();
            var all = new List<Entry>();
            foreach (var path in new[] { ContentPath, OlderPath })
            {
                byte[] b = null;
                try { b = read(path); } catch (Exception e) { Debug.LogWarning($"[Ez2] {path}: {e.Message}"); }
                if (b == null) { Debug.Log($"[Ez2] {path} not in the GRF"); continue; }
                try { int n = Parse(b, all); Debug.Log($"[Ez2] {path}: {n} effects"); }
                catch (Exception e) { Debug.LogWarning($"[Ez2] {path}: unreadable ({e.Message})"); }
            }
            Entries = all.Count;
            // entry → skill, exact rules only
            var skillOf = new Dictionary<string, int>();
            var meta = new Dictionary<string, (string baseName, bool suffixed, string field)>();
            foreach (var e in all)
            {
                string baseName = e.Name; bool suffixed = false; string field = e.Floor ? "ground" : "effect";
                foreach (var (suffix, f) in SuffixToField)
                    if (e.Name.EndsWith("_" + suffix, StringComparison.Ordinal)) { baseName = e.Name.Substring(0, e.Name.Length - suffix.Length - 1); suffixed = true; field = f; break; }
                if (Hardcoded.TryGetValue(e.Name, out var hf)) field = hf;
                meta[e.Name] = (baseName, suffixed, field);
                int id = Find(skillIdOfAegis, baseName);
                if (id == 0) foreach (var seg in e.FilePath.Split('\\')) { id = Find(skillIdOfAegis, seg); if (id != 0) break; }
                if (id == 0 && !string.IsNullOrEmpty(e.SoundPath))
                {
                    string snd = e.SoundPath; int s = snd.LastIndexOf('\\'); if (s >= 0) snd = snd.Substring(s + 1);
                    if (snd.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) snd = snd.Substring(0, snd.Length - 4);
                    id = Find(skillIdOfAegis, snd);
                }
                if (id != 0) skillOf[e.Name] = id;
            }
            foreach (var e in all) // rule 4 after every entry has had its own chance (rB runs its inheritance pass last)
                if (!skillOf.ContainsKey(e.Name) && meta[e.Name].suffixed && skillOf.TryGetValue(meta[e.Name].baseName, out int b)) skillOf[e.Name] = b;

            var touched = new HashSet<int>();
            foreach (var e in all)
            {
                // the effect itself (rB DBManager.js:4520-4536)
                int slash = e.FilePath.LastIndexOf('\\');
                var p = new Dictionary<string, object>
                {
                    { "type", "STR" },
                    { "file", (e.FilePath.EndsWith(".str", StringComparison.OrdinalIgnoreCase) ? e.FilePath.Substring(0, e.FilePath.Length - 4) : e.FilePath).Replace('\\', '/') },
                    { "texturePath", slash >= 0 ? e.FilePath.Substring(0, slash + 1) : "" },
                    // rB sets repeat = IsInfinite, and its loop then runs until the unit leaves view; the official client ends such a loop with
                    // the status it belongs to, a link this table does not give, so Loki plays one cycle (the flag is kept as "infinite")
                    { "xOffset", e.PosX }, { "yOffset", e.PosY }, { "delayLate", e.Delay }, { "infinite", e.Infinite }, { "attachedEntity", true },
                };
                if (!string.IsNullOrEmpty(e.SoundPath))
                {
                    string w = e.SoundPath.Replace('\\', '/'); if (w.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) w = w.Substring(0, w.Length - 4);
                    p["wav"] = w;
                }
                EffectDb.AddEffect(e.Name, e.Name, new List<EffectPart> { new EffectPart { Type = "STR", P = p } });
                if (!skillOf.TryGetValue(e.Name, out int sid)) { Unmapped.Add(e.Name); continue; }
                Mapped++; touched.Add(sid);
                var s = EffectDb.SkillOrNew(sid, null);
                if (s.Source == "rB") s.Source = "rB+ez2";
                switch (meta[e.Name].field)
                {
                    case "hit": s.HitEffectId = EffectDb.Append(s.HitEffectId, e.Name); break;
                    case "ground": s.GroundEffectId = EffectDb.Append(s.GroundEffectId, e.Name); break;
                    default: s.EffectId = EffectDb.Append(s.EffectId, e.Name); break;
                }
            }
            SkillsTouched = touched.Count;
            Debug.Log($"[Ez2] {Entries} effects, {Mapped} tied to {SkillsTouched} skills by exact names, {Unmapped.Count} not tied: {string.Join(", ", Unmapped)}");
        }

        private static Dictionary<string, int> _classless; private static IReadOnlyDictionary<string, int> _classlessOf;
        /// <summary>A name's skill: its aegis name exactly, else — rB's skidFuzzy key form (DBManager.js:4418-4435), kept only where it is
        /// unambiguous — the aegis name without its class prefix (TR_ROSEBLOSSOM → ROSEBLOSSOM) when exactly one skill has that remainder.</summary>
        private static int Find(IReadOnlyDictionary<string, int> map, string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            string k = name.ToUpperInvariant();
            if (map.TryGetValue(k, out int id)) return id;
            if (!ReferenceEquals(_classlessOf, map))
            {
                _classlessOf = map; _classless = new Dictionary<string, int>();
                var seen = new HashSet<string>();
                foreach (var kv in map)
                {
                    int u = kv.Key.IndexOf('_'); if (u <= 0 || u == kv.Key.Length - 1) continue;
                    string rest = kv.Key.Substring(u + 1);
                    if (!seen.Add(rest)) { if (_classless.TryGetValue(rest, out int other) && other != kv.Value) _classless[rest] = -1; }
                    else _classless[rest] = kv.Value;
                }
            }
            return _classless.TryGetValue(k, out int c) && c > 0 ? c : 0;
        }

        /// <summary>The table's documents: { "EZ2STREffect": { name: { FilePath, SoundPath, IsFloor, IsToGround, IsInfinite, PosX, PosY,
        /// StartDelayTime, … } } } (rB loadBSONFile unwraps a single root key, DBManager.js:4355-4362).</summary>
        private static int Parse(byte[] b, List<Entry> into)
        {
            int n = 0, p = 0;
            while (p + 4 <= b.Length)
            {
                int size = BitConverter.ToInt32(b, p); if (size < 5 || p + size > b.Length) break;
                var doc = ReadDoc(b, p);
                p += size;
                var root = doc.Count == 1 ? AsDoc(First(doc)) : doc;
                if (root == null) continue;
                foreach (var kv in root)
                {
                    var d = AsDoc(kv.Value); if (d == null) continue;
                    into.Add(new Entry
                    {
                        Name = kv.Key, FilePath = Str(d, "FilePath"), SoundPath = Str(d, "SoundPath"),
                        Floor = Bool(d, "IsFloor") || Bool(d, "IsToGround"), Infinite = Bool(d, "IsInfinite"),
                        PosX = Num(d, "PosX"), PosY = Num(d, "PosY"), Delay = Num(d, "StartDelayTime"),
                    });
                    n++;
                }
            }
            return n;
        }

        private static object First(Dictionary<string, object> d) { foreach (var kv in d) return kv.Value; return null; }
        private static Dictionary<string, object> AsDoc(object o) => o as Dictionary<string, object>;
        private static string Str(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) && v is string s ? s : "";
        private static bool Bool(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) && v is bool x && x;
        private static double Num(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) ? v is double x ? x : v is int i ? i : v is long l ? l : 0 : 0;

        /// <summary>A BSON document (bsonspec.org 1.1): the element types these tables use — double, string, document, array, bool, null,
        /// int32, int64; any other type stops the read (logged by the caller).</summary>
        private static Dictionary<string, object> ReadDoc(byte[] b, int p)
        {
            int size = BitConverter.ToInt32(b, p); int end = p + size; p += 4;
            var o = new Dictionary<string, object>();
            while (p < end - 1)
            {
                byte t = b[p++];
                int z = p; while (b[z] != 0) z++;
                string name = Encoding.UTF8.GetString(b, p, z - p); p = z + 1;
                object v;
                switch (t)
                {
                    case 0x01: v = BitConverter.ToDouble(b, p); p += 8; break;
                    case 0x02: { int len = BitConverter.ToInt32(b, p); v = Encoding.UTF8.GetString(b, p + 4, Math.Max(0, len - 1)); p += 4 + len; break; }
                    case 0x03: case 0x04:
                    {
                        int len = BitConverter.ToInt32(b, p); var d = ReadDoc(b, p); p += len;
                        if (t == 0x04) { var l = new List<object>(); foreach (var kv in d) l.Add(kv.Value); v = l; } else v = d;
                        break;
                    }
                    case 0x08: v = b[p] != 0; p += 1; break;
                    case 0x0A: v = null; break;
                    case 0x10: v = BitConverter.ToInt32(b, p); p += 4; break;
                    case 0x12: v = BitConverter.ToInt64(b, p); p += 8; break;
                    default: throw new FormatException($"BSON element type 0x{t:x2} ('{name}')");
                }
                o[name] = v;
            }
            return o;
        }
    }
}
