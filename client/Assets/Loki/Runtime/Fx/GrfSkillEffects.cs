using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Loki.Data;
using Loki.IO;
using UnityEngine;

namespace Loki.Effects
{
    /// <summary>
    /// Client wave 4c (w4fx2; design notes): the skill effects the player's own GRF gives beyond roBrowserLegacy's tables and the ez2
    /// tables, read when the effect manager has loaded the ez2 tables (Gravity's data: never shipped). A step-for-step port of
    /// client/tools/fx/grf_skill_effects.py (the reference: fx_coverage.py counts with it; a change there must be made here too).
    ///
    /// Sources (all in the player's GRF):
    ///  O  the official client's own skill → effect table, data\luafiles514\lua files\skilleffectinfo\skilleffectinfolist.lub
    ///     (SKILL_EFFECT_INFO_LIST[SKID.x] = { effectID = {EFID…}, targetEffectID, groundEffectID, toTargetEffectID, beginEffectID,
    ///     waveFileName, targetWaveFileName, beginMotionType, onTarget, LaunchZC_USE_SKILL }; getters in skilleffectinfo_f.lub), run after
    ///     skillinfoz\skillid.lub (SKID), skilleffectinfo\effectid.lub (EFID) and actorstate.lub (ACTOR_STATE). roBrowserLegacy does not read
    ///     this file and nothing found documents the fields' moments: Loki takes their names — targetEffectID / targetWaveFileName on the
    ///     skill's target (rB's effectId moment), effectID / waveFileName on the caster (rB's effectIdOnCaster), beginEffectID as the cast
    ///     aura (the aura rB otherwise picks from the cast packet's element, Entity.js:1846-1892). groundEffectID and toTargetEffectID are
    ///     kept, never spawned (none of their ids has a drawable part in this GRF); beginMotionType (a body action) and onTarget are not used.
    ///     The official sounds are added only for skills rB / ez2 give no effect (no sound played twice).
    ///  N1 an effect id the official EFID table names and the effect table lacks: the GRF's one effect file named exactly like it
    ///     (EF_FULLMOON_KICK → effect\fullmoon_kick\fullmoon_kick.str, EF_SOUL_REAPER → sprite\이팩트\soul_reaper\soul_reaper.spr).
    ///  N2 an official entry with exactly one referenced effect id still without parts: the one effect file named after its skill (rule S).
    ///  C  a skill with no effect id in rB, ez2 or the official table: the one effect file named after its full aegis name (rule S without
    ///     the class-less form: the GRF's file names are generic words — miss, owl, aura — that the class-less forms of passives hit),
    ///     played as rB plays an ez2 entry without a suffix (effectId: on the skill's target, the caster for a self skill; DBManager.js
    ///     mergeEz2Effects 4380-4560).
    ///  S  the name rule: the skill's aegis name and (N2 only) its name without the class prefix when exactly one skill has that remainder
    ///     (Ez2Effects.Find's rule); effect files: a file named so (any depth) and every effect file under a folder named so, except a file
    ///     named after another skill or under another skill's folder (it belongs to that skill). The union must be exactly ONE file, else
    ///     nothing is tied (the order and moments of several files are the client's code).
    /// Effect files: data\texture\effect\**\*.str (STRM 0x94 with at least one texture where the renderer loads it: effect\&lt;folder&gt;) and
    /// data\sprite\이팩트\**\*.spr with its .act; a file an rB / ez2 effect already loads is never taken. Parts: STR {file, texturePath = its
    /// folder, attachedEntity} (Ez2Effects' part), SPR {file, attachedEntity}, sounds {wav, attachedEntity}.
    /// </summary>
    public static class GrfSkillEffects
    {
        private const string Lua = "data\\luafiles514\\lua files\\";
        public static readonly string[] Lubs =
        {
            Lua + "skillinfoz\\skillid.lub", Lua + "skilleffectinfo\\effectid.lub", Lua + "skilleffectinfo\\actorstate.lub", Lua + "skilleffectinfo\\skilleffectinfolist.lub",
        };
        private const string EffectDir = "data\\texture\\effect\\";
        private static readonly string SpriteDir = "data\\sprite\\" + Loki.Sprites.RoNames.Effect + "\\";

        /// <summary>One line per decision (rule O / N1 / N2 / C, and the ambiguous ones), as grf_skill_effects.py --report writes them.</summary>
        public static readonly List<(int skill, string rule, string detail)> Log = new List<(int, string, string)>();
        public static int OfficialEntries { get; private set; }
        public static int EffectsAdded { get; private set; }
        public static int SkillsTouched { get; private set; }
        public static bool Loaded { get; private set; }
        /// <summary>The official entries as read (skill id → field → value), for the smoke.</summary>
        public static readonly Dictionary<int, Dictionary<string, object>> Official = new Dictionary<int, Dictionary<string, object>>();

        private sealed class FileRef : IComparable<FileRef>
        {
            public string Kind, Rel; // "STR" / "SPR", path under effect\ or 이팩트\ without the extension
            public int CompareTo(FileRef o) { int c = string.CompareOrdinal(Kind, o.Kind); return c != 0 ? c : string.CompareOrdinal(Rel, o.Rel); }
            public override string ToString() => Kind + " " + Rel;
        }

        /// <param name="read">a GRF path → bytes (the effect manager's file system)</param>
        /// <param name="archives">the GRFs whose file lists are searched (the Python reference reads data.grf's)</param>
        /// <param name="skillIdOfAegis">upper-case aegis name → skill id (skills.tsv)</param>
        public static void Load(Func<string, byte[]> read, IReadOnlyList<GrfArchive> archives, IReadOnlyDictionary<string, int> skillIdOfAegis)
        {
            Log.Clear(); Official.Clear(); OfficialEntries = EffectsAdded = SkillsTouched = 0; Loaded = false;
            // ---- the official table (lubs run in order; any missing: nothing from this GRF)
            var vm = new LuaVM();
            foreach (var p in Lubs)
            {
                byte[] b = null; try { b = read(p); } catch (Exception e) { Debug.LogWarning($"[W4Fx2] {p}: {e.Message}"); }
                if (b == null) { Debug.Log($"[W4Fx2] {p} not in the GRF: no official skill effect table"); Log.Add((0, "O", "skilleffectinfo lubs not in this GRF")); return; }
                try { vm.Run(b); } catch (Exception e) { Debug.LogWarning($"[W4Fx2] {p}: {e.Message}"); return; }
            }
            var list = vm.Globals.Get("SKILL_EFFECT_INFO_LIST") as LuaTable; var efidT = vm.Globals.Get("EFID") as LuaTable;
            if (list == null || efidT == null) { Debug.LogWarning("[W4Fx2] SKILL_EFFECT_INFO_LIST / EFID missing"); return; }
            var table = new SortedDictionary<long, Dictionary<string, object>>();
            foreach (var kv in list.Items())
            {
                if (!(kv.Key is long sid) || !(kv.Value is LuaTable v)) continue;
                var e = new Dictionary<string, object>();
                foreach (var f in new[] { "effectID", "targetEffectID", "groundEffectID", "toTargetEffectID" }) { var ids = Ids(v.Get(f)); if (ids.Count > 0) e[f] = ids; }
                foreach (var f in new[] { "beginEffectID", "beginMotionType" }) { var x = v.Get(f); if (x is long || x is double) e[f] = Convert.ToInt32(x is long l ? l : (long)(double)x); }
                foreach (var f in new[] { "waveFileName", "targetWaveFileName" }) if (v.Get(f) is LuaStr s && s.B.Length > 0) e[f] = s.Latin1;
                foreach (var f in new[] { "onTarget", "LaunchZC_USE_SKILL" }) { var x = v.Get(f); if (x != null) e[f] = Loki.Data.Lua.Truthy(x); }
                table[sid] = e; Official[(int)sid] = e;
            }
            OfficialEntries = table.Count;
            var efid = new List<(string name, int id)>();
            foreach (var kv in efidT.Items()) if (kv.Key is LuaStr n && (kv.Value is long || kv.Value is double)) efid.Add((n.Latin1, Convert.ToInt32(kv.Value is long l ? l : (long)(double)kv.Value)));

            // ---- the GRF's effect files, without the ones rB / ez2 load
            var taken = TakenFiles(archives);
            var byBase = new Dictionary<string, SortedSet<FileRef>>(StringComparer.Ordinal); var byFolder = new Dictionary<string, SortedSet<FileRef>>(StringComparer.Ordinal);
            foreach (var g in archives)
                foreach (var n in g.Names)
                {
                    if (taken.Contains(n)) continue;
                    FileRef f;
                    if (n.StartsWith(EffectDir, StringComparison.Ordinal) && n.EndsWith(".str", StringComparison.Ordinal)) f = new FileRef { Kind = "STR", Rel = n.Substring(EffectDir.Length, n.Length - EffectDir.Length - 4) };
                    else if (n.StartsWith(SpriteDir, StringComparison.Ordinal) && n.EndsWith(".spr", StringComparison.Ordinal) && Has(archives, n.Substring(0, n.Length - 4) + ".act")) f = new FileRef { Kind = "SPR", Rel = n.Substring(SpriteDir.Length, n.Length - SpriteDir.Length - 4) };
                    else continue;
                    var segs = f.Rel.Split('\\');
                    Add(byBase, segs[segs.Length - 1], f);
                    for (int i = 0; i < segs.Length - 1; i++) Add(byFolder, segs[i], f);
                }
            var drawableCache = new Dictionary<string, bool>(StringComparer.Ordinal);
            bool Drawable(FileRef f)
            {
                string k = f.ToString(); if (drawableCache.TryGetValue(k, out bool ok)) return ok;
                ok = f.Kind == "SPR" || StrDrawable(read, archives, f.Rel);
                drawableCache[k] = ok; return ok;
            }
            var skillOfName = new Dictionary<string, int>(StringComparer.Ordinal);
            var aegisOfId = new Dictionary<int, string>();
            foreach (var kv in skillIdOfAegis) { skillOfName[kv.Key.ToLowerInvariant()] = kv.Value; if (!aegisOfId.ContainsKey(kv.Value)) aegisOfId[kv.Value] = kv.Key; }
            var classless = Classless(skillIdOfAegis);
            List<FileRef> Named(IEnumerable<string> names, int sid)
            {
                var o = new SortedSet<FileRef>();
                foreach (var n in names) { if (byBase.TryGetValue(n, out var a)) o.UnionWith(a); if (byFolder.TryGetValue(n, out var b)) o.UnionWith(b); }
                return o.Where(f => f.Rel.Split('\\').All(seg => !skillOfName.TryGetValue(seg, out int other) || other == sid)).Where(Drawable).ToList();
            }
            var added = new Dictionary<string, List<EffectPart>>(StringComparer.Ordinal);
            bool HasKey(string key) => EffectDb.Effect(key) != null || added.ContainsKey(key);

            // ---- N1: official EF names → files (the file's own name)
            foreach (var (name, eid) in efid.OrderBy(x => x.id))
            {
                if (HasKey(eid.ToString()) || !name.StartsWith("EF_", StringComparison.Ordinal)) continue;
                string want = name.Substring(3).ToLowerInvariant();
                var fs = byBase.TryGetValue(want, out var set) ? set.Where(Drawable).ToList() : new List<FileRef>();
                if (fs.Count == 1) { added[eid.ToString()] = new List<EffectPart> { PartOf(fs[0]) }; Log.Add((0, "N1", $"{eid} {name} -> {fs[0]}")); }
                else if (fs.Count > 1) Log.Add((0, "N1-ambiguous", $"{eid} {name} -> {string.Join(", ", fs)}"));
            }
            // ---- O + N2
            var touched = new HashSet<int>();
            foreach (var kv in table)
            {
                int sid = (int)kv.Key; var e = kv.Value;
                var refd = new List<int>(); foreach (var f in new[] { "targetEffectID", "effectID" }) if (e.TryGetValue(f, out var ids)) refd.AddRange((List<int>)ids);
                var bare = refd.Where(i => !HasKey(i.ToString())).Distinct().OrderBy(i => i).ToList();
                if (bare.Count == 1)
                {
                    var fs = Named(SkillNames(aegisOfId, sid, classless), sid);
                    if (fs.Count == 1) { added[bare[0].ToString()] = new List<EffectPart> { PartOf(fs[0]) }; Log.Add((sid, "N2", $"{bare[0]} <- {fs[0]}")); }
                }
                var s = EffectDb.SkillOrNew(sid, aegisOfId.TryGetValue(sid, out var an) ? an : null, "sei");
                bool hadData = HasData(s);
                if (s.Source == "rB" || s.Source == "ez2" || s.Source == "rB+ez2") s.Source += "+sei";
                if (e.TryGetValue("targetEffectID", out var t)) foreach (int i in (List<int>)t) s.EffectId = EffectDb.Append(s.EffectId, i.ToString());
                if (e.TryGetValue("effectID", out var c)) foreach (int i in (List<int>)c) s.EffectIdOnCaster = EffectDb.Append(s.EffectIdOnCaster, i.ToString());
                if (e.TryGetValue("groundEffectID", out var gr)) foreach (int i in (List<int>)gr) s.GroundEffectId = EffectDb.Append(s.GroundEffectId, i.ToString());
                if (e.TryGetValue("beginEffectID", out var be)) s.CastAuraId = new[] { ((int)be).ToString() };
                if (!hadData)
                {
                    if (e.TryGetValue("waveFileName", out var w)) { string k = $"sei_{sid}_wav"; added[k] = new List<EffectPart> { Wav((string)w) }; s.EffectIdOnCaster = EffectDb.Append(s.EffectIdOnCaster, k); }
                    if (e.TryGetValue("targetWaveFileName", out var tw)) { string k = $"sei_{sid}_twav"; added[k] = new List<EffectPart> { Wav((string)tw) }; s.EffectId = EffectDb.Append(s.EffectId, k); }
                }
                touched.Add(sid);
                Log.Add((sid, "O", Describe(e)));
            }
            // ---- C: skills with no effect data at all (by aegis name order, as the reference)
            foreach (var kv in skillIdOfAegis.OrderBy(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal))
            {
                int sid = kv.Value; string a = kv.Key;
                if (table.ContainsKey(sid) || !aegisOfId.TryGetValue(sid, out var first) || first != a) continue;
                var cur = EffectDb.Skill(sid); if (cur != null && HasData(cur)) continue;
                var fs = Named(new[] { a.ToLowerInvariant() }, sid);
                if (fs.Count == 1)
                {
                    string k = "grf_" + a.ToLowerInvariant(); added[k] = new List<EffectPart> { PartOf(fs[0]) };
                    var s = EffectDb.SkillOrNew(sid, a, "grf"); if (s.Source != "grf") s.Source += "+grf";
                    s.EffectId = EffectDb.Append(s.EffectId, k); touched.Add(sid);
                    Log.Add((sid, "C", fs[0].ToString()));
                }
                else if (fs.Count > 1) Log.Add((sid, "C-ambiguous", $"{fs.Count} files: {string.Join(", ", fs.Take(6))}"));
            }
            foreach (var kv in added) if (EffectDb.AddEffect(kv.Key, kv.Key, kv.Value)) EffectsAdded++;
            SkillsTouched = touched.Count; Loaded = true;
            Debug.Log($"[W4Fx2] official skill effect table: {OfficialEntries} skills; effects added {EffectsAdded}, skills touched {SkillsTouched}: " +
                      $"N1 {Log.Count(x => x.rule == "N1")}, N2 {Log.Count(x => x.rule == "N2")}, C {Log.Count(x => x.rule == "C")}, " +
                      $"ambiguous (not tied) {Log.Count(x => x.rule.EndsWith("ambiguous"))}");
            foreach (var l in Log) if (l.rule != "O") Debug.Log($"[W4Fx2] {l.rule} {l.skill}: {l.detail}");
        }

        /// <summary>A skill with an effect id in any field (rB's SkillEffect incl. groundEffectId, or an ez2 entry): grf_skill_effects.py skills_with_data.</summary>
        public static bool HasData(SkillFx s) => s.EffectId != null || s.EffectIdOnCaster != null || s.HitEffectId != null || s.HitEffectIdOnCaster != null ||
            s.BeforeHitEffectId != null || s.BeforeHitEffectIdOnCaster != null || s.ReleaseEffectId != null || s.BeginCastEffectId != null ||
            s.SuccessEffectId != null || s.SuccessEffectIdOnCaster != null || s.GroundEffectId != null;

        private static List<int> Ids(object t)
        {
            var o = new List<int>();
            if (!(t is LuaTable lt)) return o;
            foreach (var kv in lt.Items().Where(x => x.Key is long).OrderBy(x => (long)x.Key))
                if (kv.Value is long l) o.Add((int)l); else if (kv.Value is double d) o.Add((int)d);
            return o;
        }

        private static string Describe(Dictionary<string, object> e) =>
            "{" + string.Join(", ", e.Select(kv => kv.Key + ": " + (kv.Value is List<int> l ? "[" + string.Join(", ", l) + "]" : kv.Value is bool b ? (b ? "true" : "false") : Convert.ToString(kv.Value)))) + "}";

        private static void Add(Dictionary<string, SortedSet<FileRef>> d, string k, FileRef f) { if (!d.TryGetValue(k, out var s)) d[k] = s = new SortedSet<FileRef>(); s.Add(f); }
        private static bool Has(IReadOnlyList<GrfArchive> archives, string n) { foreach (var g in archives) if (g.Contains(n)) return true; return false; }

        private static EffectPart PartOf(FileRef f)
        {
            var p = new Dictionary<string, object> { { "type", f.Kind }, { "attachedEntity", true } };
            if (f.Kind == "STR")
            {
                int slash = f.Rel.LastIndexOf('\\');
                p["file"] = f.Rel.Replace('\\', '/'); p["texturePath"] = slash >= 0 ? f.Rel.Substring(0, slash + 1) : "";
            }
            else p["file"] = f.Rel;
            return new EffectPart { Type = f.Kind, P = p };
        }

        private static EffectPart Wav(string w)
        {
            // the lub's names carry ".wav"; EffectManager adds the extension only when it is missing
            return new EffectPart { Type = "", P = new Dictionary<string, object> { { "wav", w }, { "attachedEntity", true } } };
        }

        /// <summary>An STR the renderer can draw: STRM 0x94 (StrFile.Parse) with at least one texture at effect\&lt;its folder&gt;&lt;name&gt; (StrFxRenderer).</summary>
        private static bool StrDrawable(Func<string, byte[]> read, IReadOnlyList<GrfArchive> archives, string rel)
        {
            StrFile s;
            try { s = StrFile.Parse(read(EffectDir + rel + ".str")); } catch { return false; }
            int slash = rel.LastIndexOf('\\'); string folder = slash >= 0 ? rel.Substring(0, slash + 1) : "";
            foreach (var l in s.Layers) foreach (var t in l.Textures) if (Has(archives, RoPath.Normalize(EffectDir + folder + t))) return true;
            return false;
        }

        /// <summary>The GRF paths rB's table and the ez2 entries (already in EffectDb) load: STR parts effect\&lt;file&gt;.str (a %d file: every
        /// number), SPR parts sprite\이팩트\&lt;file&gt;.spr, 3D / 2D spriteName (이팩트) and absoluteSpriteName — every dictionary of every part,
        /// nested ones too (grf_skill_effects.py taken_files).</summary>
        private static HashSet<string> TakenFiles(IReadOnlyList<GrfArchive> archives)
        {
            var t = new HashSet<string>(StringComparer.Ordinal); var pats = new List<Regex>();
            void AddPath(string path)
            {
                path = RoPath.Normalize(path);
                if (path.Contains("%d")) pats.Add(new Regex("^" + Regex.Escape(path).Replace("%d", @"\d+") + "$", RegexOptions.CultureInvariant));
                else t.Add(path);
            }
            void Walk(object o)
            {
                if (o is Dictionary<string, object> d)
                {
                    string ty = d.TryGetValue("type", out var tv) ? tv as string : null;
                    if (d.TryGetValue("file", out var fv) && fv is string f && f.Length > 0)
                    {
                        if (ty == "STR") AddPath(EffectDir + f + ".str");
                        else if (ty == "SPR") AddPath(SpriteDir + f + ".spr");
                    }
                    if (d.TryGetValue("spriteName", out var sv) && sv is string sn && sn.Length > 0) AddPath(SpriteDir + sn + ".spr");
                    if (d.TryGetValue("absoluteSpriteName", out var av) && av is string asn && asn.Length > 0) AddPath(asn + ".spr");
                    foreach (var v in d.Values) Walk(v);
                }
                else if (o is List<object> l) foreach (var x in l) Walk(x);
            }
            foreach (var kv in EffectDb.AllEffects) foreach (var p in kv.Value) Walk(p.P);
            if (pats.Count > 0) foreach (var g in archives) foreach (var n in g.Names) if (pats.Any(r => r.IsMatch(n))) t.Add(n);
            return t;
        }

        private static Dictionary<string, int> Classless(IReadOnlyDictionary<string, int> map)
        {
            var c = new Dictionary<string, int>(StringComparer.Ordinal); var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in map)
            {
                int u = kv.Key.IndexOf('_'); if (u <= 0 || u == kv.Key.Length - 1) continue;
                string rest = kv.Key.Substring(u + 1);
                if (!seen.Add(rest)) { if (c.TryGetValue(rest, out int other) && other != kv.Value) c[rest] = -1; }
                else c[rest] = kv.Value;
            }
            return c;
        }

        private static IEnumerable<string> SkillNames(Dictionary<int, string> aegisOfId, int sid, Dictionary<string, int> classless)
        {
            if (!aegisOfId.TryGetValue(sid, out var a)) yield break;
            yield return a.ToLowerInvariant();
            int u = a.IndexOf('_');
            if (u > 0 && u < a.Length - 1 && classless.TryGetValue(a.Substring(u + 1), out int id) && id == sid) yield return a.Substring(u + 1).ToLowerInvariant();
        }
    }
}
