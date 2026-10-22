using System;
using System.Collections.Generic;
using System.IO;
using Loki.IO;
using UnityEngine;

namespace Loki.Effects
{
    /// <summary>
    /// One part of an effect (research/skill_effects.md §1.5): `type` STR / SPR / CYLINDER / 3D / 2D / QuadHorn / TRAIL / FUNC / RSM
    /// / WATERFALL or none (sound only) plus its parameters, kept as the raw JSON map because the 3D/2D families use ~90 keys.
    /// </summary>
    public sealed class EffectPart
    {
        public string Type = ""; public Dictionary<string, object> P;
        public bool Has(string k) => P != null && P.ContainsKey(k) && P[k] != null;
        public double Num(string k, double d = 0) => P != null && P.TryGetValue(k, out var v) ? MiniJson.Num(v, d) : d;
        public float F(string k, float d = 0) => (float)Num(k, d);
        public int I(string k, int d = 0) => P != null && P.TryGetValue(k, out var v) ? MiniJson.Int(v, d) : d;
        public bool B(string k, bool d = false) => P != null && P.TryGetValue(k, out var v) ? MiniJson.Bool(v, d) : d;
        public string S(string k, string d = null) => P != null && P.TryGetValue(k, out var v) ? MiniJson.Str(v, d) : d;
        public List<object> L(string k) => P != null && P.TryGetValue(k, out var v) ? MiniJson.AsList(v) : null;
        /// <summary>A number, or a random pick from a [min, max] pair (QuadHorn-style parameters).</summary>
        public float R(string k, float d = 0)
        {
            if (P == null || !P.TryGetValue(k, out var v)) return d;
            var l = MiniJson.AsList(v);
            if (l != null && l.Count >= 2) return UnityEngine.Random.Range((float)MiniJson.Num(l[0]), (float)MiniJson.Num(l[1]));
            return (float)MiniJson.Num(v, d);
        }
        public override string ToString() => Type + (Has("file") ? ":" + S("file") : Has("textureName") ? ":" + S("textureName") : "");
    }

    /// <summary>What a skill plays on which packet (research §1.3). Ids are effect keys: numbers as decimal strings, or roBrowser's string keys.</summary>
    public sealed class SkillFx
    {
        public string Name = "";
        public string[] EffectId, EffectIdOnCaster, HitEffectId, HitEffectIdOnCaster, BeforeHitEffectId, BeforeHitEffectIdOnCaster, ReleaseEffectId, BeginCastEffectId, SuccessEffectId, SuccessEffectIdOnCaster;
        /// <summary>rB SkillEffect's groundEffectId: documentation only there (no rB code plays it; DBManager.js:4446 is its only reader). Kept for
        /// the coverage count (client wave 4, w4fx), never spawned: ground skills draw through their unit (ZC_SKILL_ENTRY → skill_unit.json).</summary>
        public string[] GroundEffectId;
        /// <summary>Client wave 4c (w4fx2): the official skilleffectinfolist's beginEffectID — drawn instead of the element's cast aura while a cast
        /// bar runs (<see cref="EffectManager.OnCastStart"/>; <see cref="GrfSkillEffects"/>).</summary>
        public string[] CastAuraId;
        public bool HideCastBar, HideCastAura;
        /// <summary>Where the entry came from: "rB" (DB/Skills/SkillEffect.js), "ez2" (the GRF's ez2streffect.bson, <see cref="Ez2Effects"/>), "sei"
        /// (the GRF's official skilleffectinfolist.lub), "grf" (a GRF effect file named after the skill) — joined by "+" when several (<see cref="GrfSkillEffects"/>).</summary>
        public string Source = "rB";
    }

    /// <summary>
    /// The client's effect tables as roBrowserLegacy transcribed them (DB/Effects/EffectTable.js, DB/Skills/SkillEffect.js,
    /// DB/Skills/SkillUnit.js), shipped as StreamingAssets/data/fx/*.json. Effect ids equal rAthena's EF_* numbers, unit ids its UNT_*.
    /// </summary>
    public static class EffectDb
    {
        private static Dictionary<string, List<EffectPart>> _effects; private static Dictionary<string, string> _names;
        private static Dictionary<int, SkillFx> _skills; private static Dictionary<int, string> _units;
        public static bool Loaded => _effects != null;
        public static int EffectCount => _effects?.Count ?? 0;

        public static void Load()
        {
            if (_effects != null) return;
            _effects = new Dictionary<string, List<EffectPart>>(); _names = new Dictionary<string, string>();
            _skills = new Dictionary<int, SkillFx>(); _units = new Dictionary<int, string>();
            string dir = Path.Combine(Application.streamingAssetsPath, "data", "fx");
            try
            {
                var table = MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(Path.Combine(dir, "effect_table.json"))));
                foreach (var kv in table)
                {
                    var o = MiniJson.AsObject(kv.Value); if (o == null) continue;
                    var parts = new List<EffectPart>();
                    var entries = MiniJson.AsList(o.TryGetValue("entries", out var e) ? e : null);
                    if (entries != null) foreach (var pe in entries) { var pm = MiniJson.AsObject(pe); if (pm == null) continue; parts.Add(new EffectPart { Type = MiniJson.Str(pm.TryGetValue("type", out var t) ? t : null, ""), P = pm }); }
                    _effects[kv.Key] = parts; _names[kv.Key] = MiniJson.Str(o.TryGetValue("name", out var nm) ? nm : null, kv.Key);
                }
                var skills = MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(Path.Combine(dir, "skill_effect.json"))));
                foreach (var kv in skills)
                {
                    if (!int.TryParse(kv.Key, out int sid)) continue;
                    var o = MiniJson.AsObject(kv.Value); if (o == null) continue;
                    var s = new SkillFx { Name = MiniJson.Str(o.TryGetValue("name", out var nm) ? nm : null, "") };
                    s.EffectId = Ids(o, "effectId"); s.EffectIdOnCaster = Ids(o, "effectIdOnCaster"); s.HitEffectId = Ids(o, "hitEffectId"); s.HitEffectIdOnCaster = Ids(o, "hitEffectIdOnCaster");
                    s.BeforeHitEffectId = Ids(o, "beforeHitEffectId"); s.BeforeHitEffectIdOnCaster = Ids(o, "beforeHitEffectIdOnCaster"); s.ReleaseEffectId = Ids(o, "releaseEffectId");
                    s.BeginCastEffectId = Ids(o, "beginCastEffectId"); s.SuccessEffectId = Ids(o, "successEffectId"); s.SuccessEffectIdOnCaster = Ids(o, "successEffectIdOnCaster");
                    s.GroundEffectId = Ids(o, "groundEffectId");
                    s.HideCastBar = o.TryGetValue("hideCastBar", out var hb) && MiniJson.Bool(hb); s.HideCastAura = o.TryGetValue("hideCastAura", out var ha) && MiniJson.Bool(ha);
                    _skills[sid] = s;
                }
                var units = MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(Path.Combine(dir, "skill_unit.json"))));
                foreach (var kv in units)
                {
                    if (!int.TryParse(kv.Key, out int uid)) continue;
                    var o = MiniJson.AsObject(kv.Value); if (o == null || !o.TryGetValue("effectId", out var ev) || ev == null) continue;
                    string key = MiniJson.Str(ev, null);
                    if (key != null && key != "-1") _units[uid] = key;
                }
                Debug.Log($"[EffectDb] {_effects.Count} effects, {_skills.Count} skills, {_units.Count} ground units");
            }
            catch (Exception e) { Debug.LogError("[EffectDb] failed to load data/fx: " + e.Message); }
        }

        /// <summary>An id value: number → "N", string key, array → several, function → nothing (roBrowser computes those in JS).</summary>
        private static string[] Ids(Dictionary<string, object> o, string key)
        {
            if (!o.TryGetValue(key, out var v) || v == null) return null;
            if (v is double d) return new[] { ((int)Math.Round(d)).ToString() };
            if (v is string s) return new[] { s };
            var l = MiniJson.AsList(v);
            if (l != null) { var r = new List<string>(); foreach (var x in l) { var k = MiniJson.Str(x, null); if (k != null) r.Add(k); } return r.Count > 0 ? r.ToArray() : null; }
            return null;
        }

        public static List<EffectPart> Effect(string key) { Load(); return key != null && _effects.TryGetValue(key, out var l) ? l : null; }
        public static List<EffectPart> Effect(int id) => Effect(id.ToString());
        public static string EffectName(string key) { Load(); return key != null && _names.TryGetValue(key, out var n) ? n : key; }
        public static SkillFx Skill(int id) { Load(); return _skills.TryGetValue(id, out var s) ? s : null; }

        /// <summary>
        /// Client wave 5 (w5looks): a skill's effectId when rB computes it from the caster (a JS function in DB/Skills/SkillEffect.js, which
        /// Loki's tables drop: <see cref="Ids"/>). The only one, MO_EXTREMITYFIST 271 (Asura Strike, SkillEffect.js:298-305): [510, 'quake']
        /// (EF_BEGINASURA11) when the caster's job is in CHAMPION_JOBS — MONK_H 4016, SURA 4070, SURA_H 4077, SURA_B 4106, SURA_2ND 4342,
        /// INQUISITOR 4262 (:50-59, rB's entity _job: the class, not a mount) — else [328, 'quake'] (EF_BEGINASURA); no caster unit: 328.
        /// Otherwise (and after those) the table's effectId.
        /// </summary>
        public static string[] EffectIdFor(int skill, int casterJob)
        {
            var table = Skill(skill)?.EffectId;
            if (skill != 271) return table;
            var ids = ChampionJobs.Contains(casterJob) ? new[] { "510", "quake" } : new[] { "328", "quake" };
            if (table != null) foreach (var k in table) ids = Append(ids, k); // keys the GRF's tables added (Ez2Effects / GrfSkillEffects), if any
            return ids;
        }
        private static readonly HashSet<int> ChampionJobs = new HashSet<int> { 4016, 4070, 4077, 4106, 4342, 4262 };
        public static string Unit(int unt) { Load(); return _units.TryGetValue(unt, out var k) ? k : null; }

        // ---- client wave 4 (w4fx): effects added at run time from the player's GRF (Gravity's data is never shipped: Ez2Effects reads
        // data\contentdata\effectdata\ez2streffect.bson and data\effectdata\ez2streffect.bson when the effect manager starts)
        public static IEnumerable<KeyValuePair<int, SkillFx>> Skills { get { Load(); return _skills; } }
        public static IEnumerable<KeyValuePair<int, string>> Units { get { Load(); return _units; } }
        /// <summary>Every effect key with its parts (client wave 4c, w4fx2: the files rB / ez2 already load, GrfSkillEffects.TakenFiles).</summary>
        public static IEnumerable<KeyValuePair<string, List<EffectPart>>> AllEffects { get { Load(); return _effects; } }
        /// <summary>Adds an effect key (never replaces one rB defines: rB's tables win where both have the key).</summary>
        public static bool AddEffect(string key, string name, List<EffectPart> parts)
        {
            Load();
            if (key == null || _effects.ContainsKey(key)) return false;
            _effects[key] = parts; _names[key] = name ?? key;
            return true;
        }
        /// <summary>The skill's entry, created (Source "ez2", or the given source) when rB has none.</summary>
        public static SkillFx SkillOrNew(int id, string name, string source = "ez2")
        {
            Load();
            if (_skills.TryGetValue(id, out var s)) return s;
            s = new SkillFx { Name = name ?? "", Source = source };
            _skills[id] = s;
            return s;
        }
        /// <summary>Appends an effect key to one of a skill's id lists (rB DBManager.js mergeEz2Effects: a second id makes the field a list).</summary>
        public static string[] Append(string[] list, string key)
        {
            if (list == null) return new[] { key };
            foreach (var k in list) if (k == key) return list;
            var r = new string[list.Length + 1]; list.CopyTo(r, 0); r[list.Length] = key; return r;
        }
    }
}
