using System;
using System.Collections.Generic;
using System.Globalization;
using Loki.IO;
using Loki.Sprites;
using UnityEngine;

namespace Loki.Effects
{
    /// <summary>
    /// Client wave 5 (w5fxown; design notes): LOKI'S OWN skill effects for the skills that have none — a DESIGN, NOT the
    /// official client's look. No source shows what the official client draws for these skills (it decides them in its program:
    /// design notes), so the design specified on 2026-09-30 for Loki's own ("Build the missing ones yourself"). Never presented as official.
    ///
    /// The table: Resources/Fx/loki_own_fx.txt, written by client/tools/fx/build_own_fx.py from rAthena's facts per skill (db/re/
    /// skill_db.yml e985006: element, target type, damage / no damage, splash area, hit count, status) and the GRF's file NAMES; it holds
    /// no Gravity data. One visual language: a recipe per skill from templates (strike, shot, bolt, nova, field, aura, song, bless, heal,
    /// curse, mark, pulse, zone), the element giving the colour and the art (the palette rows); hand-made recipes for the skills players
    /// notice first (Soul Linker spirits, Kaite / Kaupe / Eske, the Taekwon kicks and stances, Star Gladiator's Comfort / Warmth / Feeling,
    /// Napalm Vulcan, Ganbantein, Mirror Image, Gatling Fever, Tarot Card of Fate, Cart Termination); recipe "grf" for the 4th-job skills
    /// whose own GRF folders hold their art (the files theirs, the moments and anchors Loki's).
    ///
    /// Rules: a skill whose sourced effect (rB / ez2 / the official skilleffectinfolist / GRF files: w4fx, w4fx2) draws on this GRF keeps
    /// it — no own effect is added (<see cref="Sourced"/>); the sourced sounds keep playing (nothing here is removed or replaced). The
    /// Settings switch "Loki's own skill effects" (<see cref="Enabled"/>, on by default) turns all of them off. Every GRF file / texture is
    /// read from the player's own GRF at run time (never shipped); one that GRF lacks is skipped (a GRF STR part) or drawn in code (a
    /// texture), never an error — friends see these through their own clients and GRFs.
    /// The parts are ordinary effect parts (type "OWN" → <see cref="OwnFxRenderer"/>, or the GRF's STR / SPR) under keys "own:…" spammed
    /// through <see cref="EffectManager.Spam"/>: its timing, the Comfort density rule and the clean-up apply as to every other effect.
    /// Moments (the packets rAthena sends, Game/CombatFx.cs): 0x0b1a cast start → "cast"; 0x01de damage → travel (now → + attackMT),
    /// impact (+ attackMT), each further hit (+ 200 ms · i: rB / w3combat's schedule), splash targets; 0x09cb no damage → use / link /
    /// impact; 0x0117 ground → area at the cell.
    /// </summary>
    public sealed class OwnSkillFx
    {
        /// <summary>The Settings switch (World/UserSettings.cs ownSkillEffects, the Graphics page): false = only the sourced effects.</summary>
        public static bool Enabled = true;

        public sealed class Palette { public int Id; public string Name; public Color Col; public bool Add; public string Spark, Orb, Column, Puff, Sigil; }
        public sealed class GrfFile { public string Phase, Anchor, Kind, File; public int Level; public bool Present; }
        public sealed class Row
        {
            public int Id; public string Aegis, Verdict, Recipe, Arg, Kind, Target, Type, Status; public int Pal, Range, Splash, Hits, MaxLv;
            public List<GrfFile> Files = new List<GrfFile>();
            public string Template => Recipe == "grf" ? Arg : Recipe;
        }

        private readonly EffectManager _m; private readonly Func<uint, Unit> _unit;
        private readonly Dictionary<int, Row> _rows = new Dictionary<int, Row>();
        private readonly Dictionary<int, Palette> _pal = new Dictionary<int, Palette>();
        private readonly HashSet<int> _sourced = new HashSet<int>();
        private readonly Dictionary<string, float> _recent = new Dictionary<string, float>();
        private readonly HashSet<string> _keys = new HashSet<string>();
        public bool Loaded { get; private set; }
        public IReadOnlyDictionary<int, Row> Rows => _rows;
        /// <summary>Skills of the table whose sourced effect draws on this GRF: they keep it, no own effect.</summary>
        public IReadOnlyCollection<int> Sourced => _sourced;
        public int GrfFiles { get; private set; }
        public int GrfFilesPresent { get; private set; }
        /// <summary>Every own effect key built so far (smoke).</summary>
        public IReadOnlyCollection<string> Keys => _keys;
        /// <summary>skill, phase, key, owner: an own effect was spammed (smoke, the guest's view).</summary>
        public event Action<int, string, string, uint> Played;

        public OwnSkillFx(EffectManager m, Func<uint, Unit> unit) { _m = m; _unit = unit; }

        // ------------------------------------------------------------------------------------------------ the table
        /// <summary>Reads the table (Resources) and checks it against the player's GRF; after the sourced tables are loaded (w4fx's ez2, w4fx2's
        /// GrfSkillEffects), so a skill they make draw keeps its own effect.</summary>
        public void Load()
        {
            _rows.Clear(); _pal.Clear(); _sourced.Clear(); GrfFiles = GrfFilesPresent = 0; Loaded = false;
            var ta = Resources.Load<TextAsset>("Fx/loki_own_fx");
            if (ta == null) { Debug.LogWarning("[OwnFx] Resources/Fx/loki_own_fx is missing: no own effects"); return; }
            foreach (var raw in ta.text.Split('\n'))
            {
                var line = raw.TrimEnd('\r'); if (line.Length == 0 || line[0] == '#') continue;
                var c = line.Split('\t');
                try
                {
                    if (c[0] == "palette" && c.Length >= 10)
                    {
                        var rgb = c[3].Split(',');
                        _pal[int.Parse(c[1], CultureInfo.InvariantCulture)] = new Palette
                        {
                            Id = int.Parse(c[1], CultureInfo.InvariantCulture), Name = c[2], Add = c[4] != "alpha",
                            Col = new Color(F(rgb[0]), F(rgb[1]), F(rgb[2]), 1f), Spark = "effect\\" + c[5], Orb = "effect\\" + c[6], Column = "effect\\" + c[7], Puff = "effect\\" + c[8], Sigil = "effect\\" + c[9],
                        };
                    }
                    else if (c[0] == "skill" && c.Length >= 16)
                    {
                        var r = new Row
                        {
                            Id = I(c[1]), Aegis = c[2], Verdict = c[3], Recipe = c[4], Arg = c[5], Pal = I(c[6]), Kind = c[7], Target = c[8], Type = c[9],
                            Range = I(c[10]), Splash = I(c[11]), Hits = I(c[12]), MaxLv = I(c[13]), Status = c[14],
                        };
                        foreach (var f in c[15].Split(';'))
                        {
                            var q = f.Split(':'); if (q.Length < 5) continue;
                            var gf = new GrfFile { Phase = q[0], Anchor = q[1], Kind = q[2], File = q[3], Level = I(q[4]) };
                            gf.Present = gf.Kind == "SPR" ? _m.Assets != null && _m.Assets.SpriteExists("data\\sprite\\" + RoNames.Effect + "\\" + gf.File.Replace('/', '\\'))
                                                          : _m.Assets != null && _m.Assets.Exists("data\\texture\\effect\\" + gf.File.Replace('/', '\\') + ".str");
                            GrfFiles++; if (gf.Present) GrfFilesPresent++;
                            r.Files.Add(gf);
                        }
                        _rows[r.Id] = r;
                    }
                }
                catch (Exception e) { Debug.LogWarning($"[OwnFx] bad table line '{line.Substring(0, Math.Min(60, line.Length))}': {e.Message}"); }
            }
            foreach (var id in _rows.Keys) if (SourcedDraws(id)) _sourced.Add(id);
            Loaded = true;
            Debug.Log($"[OwnFx] Loki's own skill effects (a design, not the official look): {_rows.Count} skills, {_pal.Count} palettes, {_sourced.Count} keep their sourced effect, GRF files {GrfFilesPresent} of {GrfFiles} in this GRF; switch {(Enabled ? "on" : "off")}");
        }
        private static float F(string s) => float.Parse(s.Trim(), CultureInfo.InvariantCulture);
        private static int I(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

        /// <summary>Whether one of the skill's sourced effect keys has a part that draws with this GRF's files (the rule that keeps w4fx /
        /// w4fx2's effects: an own effect is never added on top of one).</summary>
        public bool SourcedDraws(int sid)
        {
            var s = EffectDb.Skill(sid); if (s == null) return false;
            foreach (var l in new[] { s.EffectId, s.EffectIdOnCaster, s.HitEffectId, s.HitEffectIdOnCaster, s.BeforeHitEffectId, s.BeforeHitEffectIdOnCaster, s.ReleaseEffectId, s.BeginCastEffectId, s.SuccessEffectId, s.SuccessEffectIdOnCaster })
            {
                if (l == null) continue;
                foreach (var k in l)
                {
                    if (k.StartsWith("own:", StringComparison.Ordinal)) continue;
                    var parts = EffectDb.Effect(k); if (parts == null) continue;
                    foreach (var p in parts) if (EffectManager.Drawable(k, p) && FilePresent(p)) return true;
                }
            }
            return false;
        }
        private bool FilePresent(EffectPart p)
        {
            var a = _m.Assets; if (a == null) return true;
            string f = p.S("file");
            switch (p.Type)
            {
                case "STR": return string.IsNullOrEmpty(f) || f.Contains("%d") || a.Exists("data\\texture\\effect\\" + f.Replace('/', '\\') + ".str");
                case "SPR": return string.IsNullOrEmpty(f) || a.SpriteExists("data\\sprite\\" + RoNames.Effect + "\\" + f.Replace('/', '\\'));
                case "CYLINDER": { string t = p.S("textureName"); return string.IsNullOrEmpty(t) || a.Exists("data\\texture\\effect\\" + t.Replace('/', '\\') + ".tga"); }
                default: return true;
            }
        }

        /// <summary>The skill draws Loki's own effect now: the switch is on, the table has it and no sourced effect draws.</summary>
        public bool Applies(int sid) => Enabled && Loaded && _rows.ContainsKey(sid) && !_sourced.Contains(sid);
        public Row RowOf(int sid) => _rows.TryGetValue(sid, out var r) ? r : null;

        // ------------------------------------------------------------------------------------------------ the moments
        private bool Recent(string what, float window)
        {
            float now = Time.time;
            if (_recent.TryGetValue(what, out float t) && now - t < window) return true;
            _recent[what] = now;
            if (_recent.Count > 512) { var old = new List<string>(); foreach (var kv in _recent) if (now - kv.Value > 5f) old.Add(kv.Key); foreach (var k in old) _recent.Remove(k); }
            return false;
        }
        private Vector3 PosOf(uint aid) { var u = aid != 0 ? _unit(aid) : null; return u != null ? u.transform.localPosition : Vector3.zero; }

        /// <summary>0x0b1a: the cast bar starts (castMs &gt; 0) — a recipe's cast phase on the caster.</summary>
        public void OnCast(int sid, uint caster, uint target, int x, int y, uint castMs)
        {
            if (!Applies(sid)) return;
            _recent["cast:" + sid + ":" + caster] = Time.time + castMs / 1000f; // the use after this cast does not repeat the cast phase
            Play(sid, "cast", 0, caster, null, Time.time, target);
        }

        /// <summary>The use moment on the caster, once per use: a cast phase when no cast bar showed it, and the caster-side parts.</summary>
        private void Use(Row r, uint src, int lv, float t, uint target)
        {
            if (src == 0 || Recent("use:" + r.Id + ":" + src, 0.35f)) return;
            if (!(_recent.TryGetValue("cast:" + r.Id + ":" + src, out float until) && Time.time < until + 1.5f)) Play(r.Id, "cast", lv, src, null, t, target);
            Play(r.Id, "use", lv, src, null, t, target);
        }

        /// <summary>0x01de skill damage on one target (action 5: a splash target of the same use). Area recipes draw their area once per use
        /// (on the caster, or at the first target's cell when no 0x0117 came) and a small impact on every target; the others travel from the
        /// caster and land on the target at + attackMT; every further hit lands 200 ms later (the w3combat schedule).</summary>
        public void OnDamage(int sid, int lv, uint src, uint dst, int dmg, int count, float mt, int action)
        {
            if (!Applies(sid)) return;
            var r = _rows[sid]; float now = Time.time; mt = Mathf.Clamp(mt, 0.05f, 1.2f);
            bool splashTarget = action == 5;
            Use(r, src, lv, now, dst);
            string area = AreaKind(r);
            if (area == "caster") { if (!Recent("area:" + sid + ":" + src, 0.5f)) Play(sid, "area", lv, src, null, now, 0); }
            else if (area == "cell") { if (!Recent("area:" + sid + ":" + src, 1.0f)) { var u = _unit(dst); if (u != null) Play(sid, "area", lv, src, u.LogicalLocal, now, 0); } } // client wave 10 (w10move): a skill's area is cell-shaped round the target's LOGICAL cell, not its drawn body
            else if (!splashTarget)
            {
                if (src != dst && src != 0) Play(sid, "travel", lv, dst, null, now, src, mt);
                Play(sid, "impact", lv, dst, null, now + mt, src);
            }
            if (area != null || splashTarget) Play(sid, "splash", lv, dst, null, now + mt, src);
            int hits = Mathf.Clamp(count, 1, 15);
            for (int i = 1; i < hits; i++) Play(sid, "hit", lv, dst, null, now + mt + 0.2f * i, src);
        }
        /// <summary>Where a recipe draws its area: "caster" (around the caster), "cell" (on the ground), or null (it lands on the target).</summary>
        private static string AreaKind(Row r)
        {
            string tpl = r.Template;
            if (r.Recipe == "grf") { var a = AnchorOf(r); if (a != "target") return a; }
            if (tpl == "nova" || r.Recipe == "tk_storm") return "caster";
            if (tpl == "field" || tpl == "zone" || r.Recipe == "ganbantein") return "cell";
            return null;
        }

        /// <summary>0x09cb a skill without damage on a target (a buff, a heal, a debuff, a self skill: target == source).</summary>
        public void OnNoDamage(int sid, int lv, uint target, uint src, bool ok)
        {
            if (!Applies(sid)) return;
            var r = _rows[sid]; float now = Time.time; string tpl = r.Template;
            if (src == 0) src = target;
            Use(r, src, lv, now, target);
            if (tpl == "nova" || tpl == "aura" || tpl == "song" || tpl == "pulse" || IsSelfHand(r.Recipe) || (r.Recipe == "grf" && AnchorOf(r) == "caster"))
            {
                if (!Recent("area:" + sid + ":" + target, 0.5f)) Play(sid, "area", lv, target, null, now, src);
                return;
            }
            if (tpl == "field" || tpl == "zone" || r.Recipe == "ganbantein" || (r.Recipe == "grf" && AnchorOf(r) == "cell"))
            {
                if (!Recent("area:" + sid + ":" + src, 1.0f)) Play(sid, "area", lv, src, PosOf(target), now, 0);
                return;
            }
            float delay = 0f;
            if (src != target && _unit(src) != null && _unit(target) != null) { Play(sid, "travel", lv, target, null, now, src, 0.45f); delay = LinkDelay(r); }
            Play(sid, "impact", lv, target, null, now + delay, src);
        }

        /// <summary>0x0117 a ground skill at a cell.</summary>
        public void OnGround(int sid, int lv, uint src, int x, int y)
        {
            if (!Applies(sid)) return;
            var r = _rows[sid]; float now = Time.time;
            Use(r, src, lv, now, 0);
            if (!Recent("area:" + sid + ":" + src, 0.6f)) Play(sid, "area", lv, src, _m.CellPos(x, y), now, 0);
        }

        private static bool IsSelfHand(string recipe) => recipe == "tk_storm" || recipe == "tk_ready" || recipe == "tk_dodge" || recipe == "tk_sevenwind" || recipe == "sg_comfort" || recipe == "sg_warm" || recipe == "sg_feel" || recipe == "mirror" || recipe == "gatling";
        private static float LinkDelay(Row r) => r.Recipe == "spirit" ? 0.5f : 0.35f;
        private static string AnchorOf(Row r) { foreach (var f in r.Files) if (f.Phase == "main" && f.Present) return f.Anchor; return r.Target == "Ground" ? "cell" : r.Target == "Self" ? "caster" : "target"; }
        private static bool HasPhase(Row r, string phase, int lv) { foreach (var f in r.Files) if (f.Phase == phase && f.Present && (f.Level == 0 || f.Level == lv)) return true; return false; }

        /// <summary>Spams a phase's own key (built on first use) on an owner (or at a position), from <paramref name="start"/>.</summary>
        private void Play(int sid, string phase, int lv, uint owner, Vector3? pos, float start, uint other, float travel = 0f)
        {
            var r = _rows[sid];
            int lvKey = LevelMatters(r) ? Mathf.Max(lv, 1) : 0;
            int tr = travel > 0f ? Mathf.RoundToInt(travel * 20f) : 0; // travel time in 50 ms steps: the key carries it (parts are fixed per key)
            int present = 0; if (r.Recipe == "grf") foreach (var f in r.Files) if (f.Present) present++;
            // the key names what its parts hold: the level when it matters, the travel time, and (recipe grf) how many of its GRF files are present
            string key = $"own:{sid}:{phase}{(lvKey > 0 ? ":" + lvKey : "")}{(tr > 0 ? ":t" + tr : "")}{(r.Recipe == "grf" ? ":f" + present : "")}";
            if (EffectDb.Effect(key) == null)
            {
                var parts = Build(r, phase, lvKey, tr * 50f);
                if (parts == null || parts.Count == 0) { EffectDb.AddEffect(key, key, new List<EffectPart>()); return; }
                EffectDb.AddEffect(key, $"{r.Aegis} {phase} (Loki's own)", parts); _keys.Add(key);
            }
            else if (EffectDb.Effect(key).Count == 0) return;
            if (owner == 0 && pos == null) return;
            // Loki's placement: a GRF STR laid on a cell sits ON the ground (StrFxRenderer gives a ground STR rB's half-cell-low base, which a VR
            // viewer looking from the side sees cut in half by the ground)
            if (pos != null && r.Recipe == "grf" && EffectDb.Effect(key).TrueForAll(q => q.Type == "STR" || q.Type == "SPR")) pos = pos.Value + Vector3.up * 0.5f;
            _m.Spam(key, owner, pos, start, -1f, other);
            Played?.Invoke(sid, phase, key, owner);
        }
        private static bool LevelMatters(Row r)
        {
            if (r.Recipe == "tk_sevenwind" || r.Recipe == "sg_feel" || r.Recipe == "sg_hate") return true;
            foreach (var f in r.Files) if (f.Level > 0) return true;
            return false;
        }

        // ------------------------------------------------------------------------------------------------ recipes → parts
        private static object N(double v) => v;
        /// <summary>A part's parameters: numbers as doubles (EffectPart reads MiniJson numbers), colours / pairs as lists.</summary>
        private static EffectPart Part(string type, params object[] kv)
        {
            var d = new Dictionary<string, object> { { "type", type } };
            for (int i = 0; i + 1 < kv.Length; i += 2)
            {
                object v = kv[i + 1];
                switch (v)
                {
                    case int n: v = (double)n; break;
                    case float f: v = (double)f; break;
                    case Color c: v = new List<object> { (double)c.r, (double)c.g, (double)c.b, (double)c.a }; break;
                    case Vector2 r2: v = new List<object> { (double)r2.x, (double)r2.y }; break;
                    case string[] ss: { var l = new List<object>(); foreach (var s in ss) l.Add(s); v = l; break; }
                }
                d[(string)kv[i]] = v;
            }
            return new EffectPart { Type = type, P = d };
        }
        private static EffectPart Own(params object[] kv) { var p = Part("OWN", kv); if (!p.P.ContainsKey("attachedEntity")) p.P["attachedEntity"] = true; return p; }
        private static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);
        private static Color Lighter(Color c, float k = 0.45f) => Color.Lerp(c, Color.white, k);
        private static Vector2 R(float a, float b) => new Vector2(a, b);
        private const string Slash = "effect\\yellowslash.tga", Shock = "effect\\explosive.tga", SoftRing = "effect\\ring_b.bmp", Glow = "effect\\glow11.bmp";
        /// <summary>The steel palette's puff texture (the GRF's grey cloud): dust and smoke of the hand-made recipes.</summary>
        private string Puff => Pal(-1).Puff;
        private Palette Pal(int id) => _pal.TryGetValue(id, out var p) ? p : _pal.TryGetValue(0, out var n) ? n : new Palette { Col = Color.white, Spark = "", Orb = "", Column = "", Puff = "", Sigil = "" };
        /// <summary>The drawn radius: SplashArea n covers (2n + 1)² cells → n + 0.5; -1 (the whole screen) 4.5; at most 8 cells (party-wide
        /// buffs of 14-15 cells read as an area without a ring sweeping the whole map).</summary>
        private static float Radius(Row r, float min) => Mathf.Min(8f, Mathf.Max(min, r.Splash > 0 ? r.Splash + 0.5f : r.Splash < 0 ? 4.5f : min));

        private List<EffectPart> Build(Row r, string phase, int lv, float travelMs)
        {
            var l = new List<EffectPart>();
            if (r.Recipe == "grf")
            {
                foreach (var f in r.Files)
                {
                    if (!f.Present || (f.Level != 0 && f.Level != lv)) continue;
                    string fp = f.Phase == "main" ? (f.Anchor == "target" ? "impact" : "area") : f.Phase;
                    if (f.Phase == "hit" && (phase == "splash" || phase == "impact")) fp = phase; // the first hit and each splash target show the hit file too
                    if (fp != phase) continue;
                    l.Add(GrfPart(f));
                }
                if (l.Count > 0) return l;
                // a phase whose files this GRF lacks (or that has none) draws the template's parts (Loki's), never nothing
                if (phase == "cast") return l;
            }
            if (TryHand(r, phase, lv, travelMs, l)) return l;
            Template(r, r.Template, phase, lv, travelMs, l);
            return l;
        }
        private static EffectPart GrfPart(GrfFile f)
        {
            bool attached = f.Anchor != "cell";
            if (f.Kind == "SPR") return Part("SPR", "file", f.File.Replace('/', '\\'), "attachedEntity", attached);
            int slash = f.File.LastIndexOf('/');
            // ownNearFade: Fx/FxRenderers.cs StrFxRenderer fades these near the eye (VR comfort: some are 7-20 cells wide, a first-person view
            // of one on yourself would be filled with it)
            return Part("STR", "file", f.File, "texturePath", slash >= 0 ? f.File.Substring(0, slash + 1).Replace('/', '\\') : "", "attachedEntity", attached, "ownNearFade", true);
        }

        // ---- the templates (Loki's design language): element colour C, a lighter core, the palette's GRF art
        private void Template(Row r, string tpl, string phase, int lv, float travelMs, List<EffectPart> l)
        {
            var p = Pal(r.Pal); var C = p.Col; var L = Lighter(C);
            switch (tpl)
            {
                case "strike":
                    if (phase == "impact") { l.Add(Arc(L, 2.0f, -30f, 75f, 60f, 260)); l.Add(Sparks(p, 10, 0)); l.Add(Flash(L, 0.5f, 1.4f, 220, "chest")); if (r.Splash > 0) l.Add(ShockRing(C, Radius(r, 1f), 0)); }
                    else if (phase == "hit") { l.Add(Arc(L, 1.5f, 150f, 70f, 80f, 200)); l.Add(Sparks(p, 5, 0)); }
                    else if (phase == "splash") { l.Add(Sparks(p, 6, 0)); l.Add(Flash(L, 0.4f, 0.9f, 180, "chest")); }
                    break;
                case "shot":
                    if (phase == "use") l.Add(Flash(L, 0.25f, 0.7f, 140, "chest"));
                    else if (phase == "travel") l.Add(Own("shape", "streak", "tex", "", "fallback", "streak", "color", A(L, 0.95f), "width", 0.12f, "length", 0.7f, "travel", Mathf.Clamp(travelMs, 60f, 300f), "duration", Mathf.Clamp(travelMs, 60f, 300f) + 140f));
                    else if (phase == "impact") { l.Add(Sparks(p, 9, 0)); l.Add(Flash(L, 0.4f, 1.2f, 200, "chest")); if (r.Splash > 0) l.Add(ShockRing(C, Radius(r, 1f), 0)); }
                    else if (phase == "hit" || phase == "splash") l.Add(Sparks(p, 5, 0));
                    break;
                case "bolt":
                    if (phase == "travel")
                    {
                        if (r.Arg == "multi") // Tetra Vortex: the four elements (fire, water, wind, earth) fly together
                        {
                            int i = 0; foreach (int el in new[] { 3, 1, 4, 2 }) { var q = Pal(el); l.Add(Orb(q, travelMs, 0.4f + 0.25f * i, i * 60)); i++; }
                        }
                        else l.Add(Orb(p, travelMs, 0.5f, 0));
                    }
                    else if (phase == "impact")
                    {
                        if (r.Arg == "multi") foreach (int el in new[] { 3, 1, 4, 2 }) l.Add(Sparks(Pal(el), 6, 0));
                        else l.Add(Sparks(p, 14, 0));
                        l.Add(Flash(L, 0.6f, 1.6f, 260, "chest")); l.Add(Ring(SoftRing, C, 0.3f, Radius(r, 1.2f), 450, 0));
                    }
                    else if (phase == "hit" || phase == "splash") { l.Add(Sparks(p, 6, 0)); l.Add(Flash(L, 0.3f, 0.9f, 160, "chest")); }
                    break;
                case "nova":
                    if (phase == "area")
                    {
                        float rad = Radius(r, 1.5f);
                        l.Add(ShockRing(C, rad, 0)); l.Add(Column(p.Column, C, 0.5f, 1.2f, 1.6f, 150, 450, 0.5f));
                        l.Add(Own("shape", "burst", "tex", p.Spark, "fallback", "spark", "color", A(L, 0.9f), "count", 18, "spread", "disc", "speed", R(rad * 1.6f, rad * 2.6f), "life", R(0.3f, 0.5f), "size", R(0.25f, 0.4f), "size1", 0.05f, "drag", 2f, "anchor", "waist", "duration", 600));
                    }
                    else if (phase == "splash" || phase == "hit") { l.Add(Sparks(p, 5, 0)); l.Add(Flash(L, 0.3f, 0.9f, 160, "chest")); }
                    break;
                case "field":
                    if (phase == "area")
                    {
                        float rad = Radius(r, 1.5f);
                        l.Add(Own("shape", "disc", "tex", p.Sigil, "fallback", "sigil", "color", A(Lighter(C, 0.35f), 0.95f), "r", rad, "appear", 200, "fade", 450, "spin", 40, "duration", 1300, "attachedEntity", false));
                        l.Add(Own("shape", "motes", "tex", p.Spark, "fallback", "spark", "color", A(L, 0.9f), "from", "above", "radius", rad * 0.85f, "h0", 2.6f, "h1", 3.6f, "vy", R(-7f, -5f), "count", 22, "emit", 650, "life", R(0.45f, 0.6f), "size", R(0.28f, 0.42f), "duration", 1300, "attachedEntity", false));
                        l.Add(Own("shape", "ring", "tex", Shock, "fallback", "ring", "color", A(C, 0.65f), "r0", 0.4f, "r1", rad, "duration", 450, "delayOffset", 650, "attachedEntity", false));
                    }
                    else if (phase == "splash" || phase == "hit" || phase == "impact") { l.Add(Sparks(p, 6, 0)); l.Add(Flash(L, 0.3f, 1.0f, 180, "chest")); }
                    break;
                case "aura":
                    if (phase == "area" || phase == "impact")
                    {
                        l.Add(Ring(SoftRing, C, 0.3f, 1.3f, 600, 0));
                        l.Add(Column(p.Column, C, 0.55f, 0.7f, 2.6f, 300, 900, 0.5f));
                        l.Add(Motes(p.Spark, L, 16, 700, 0.6f, 0f, 1.2f, R(1.2f, 2.2f), 0.8f, R(0.2f, 0.32f), 0));
                    }
                    break;
                case "song":
                    if (phase == "area" || phase == "impact")
                    {
                        float rad = Radius(r, 2f);
                        l.Add(Ring(SoftRing, C, 0.5f, rad, 900, 0)); l.Add(Flash(L, 0.4f, 1.2f, 300, "chest"));
                        if (r.Arg == "notes") // performers: notes from the GRF rising and turning around them
                            foreach (var n in new[] { "effect\\c_melody_wing\\note1.bmp", "effect\\c_melody_wing\\note2.bmp", "effect\\c_melody_wing\\note3.bmp" })
                                l.Add(Own("shape", "motes", "tex", n, "fallback", "spark", "color", A(Color.white, 0.95f), "from", "ring", "radius", 1.0f, "h0", 0.5f, "h1", 1.5f, "vy", R(0.8f, 1.3f), "swirl", 70, "count", 5, "emit", 800, "life", R(1.0f, 1.3f), "size", R(0.55f, 0.7f), "duration", 1500));
                        else l.Add(Motes(p.Spark, L, 14, 700, 0.8f, 0f, 1.4f, R(1.0f, 1.8f), 0.9f, R(0.22f, 0.34f), 0));
                    }
                    break;
                case "bless":
                    if (phase == "use") l.Add(Flash(L, 0.3f, 0.8f, 240, "chest"));
                    else if (phase == "travel") l.Add(Link(p.Spark, L, 12, 450, 0.35f, 0.4f));
                    else if (phase == "impact")
                    {
                        l.Add(Own("shape", "motes", "tex", p.Spark, "fallback", "spark", "color", A(L, 0.9f), "from", "above", "radius", 0.6f, "h0", 2.6f, "h1", 3.1f, "vy", R(-2.2f, -1.4f), "count", 14, "emit", 500, "life", R(0.7f, 0.9f), "size", R(0.24f, 0.34f), "duration", 1200));
                        l.Add(Ring(SoftRing, C, 0.3f, 1.1f, 700, 300));
                    }
                    break;
                case "heal":
                {
                    var g = new Color(0.62f, 1f, 0.68f);
                    if (phase == "use") l.Add(Flash(Lighter(g), 0.3f, 0.8f, 240, "chest"));
                    else if (phase == "travel") l.Add(Link("effect\\pikapika_white.tga", Lighter(g), 10, 450, 0.35f, 0.5f));
                    else if (phase == "impact" || phase == "area")
                    {
                        l.Add(Motes("effect\\ring_star.tga", Lighter(g, 0.3f), 14, 700, 0.6f, 0f, 1.0f, R(1.2f, 2.0f), 0.8f, R(0.3f, 0.45f), 0));
                        l.Add(Flash(g, 0.6f, 1.5f, 400, "chest")); l.Add(Column(p.Column, g, 0.55f, 0.7f, 2.2f, 250, 700, 0.35f));
                    }
                    break;
                }
                case "curse":
                    if (phase == "impact" || phase == "area")
                    {
                        l.Add(Own("shape", "motes", "tex", p.Spark, "fallback", "spark", "color", A(C, 0.9f), "from", "ring", "radius", 1.0f, "h0", 1.8f, "h1", 2.6f, "vy", R(-1.8f, -1.2f), "inward", 1.2f, "swirl", 220, "count", 16, "emit", 600, "life", R(0.8f, 1.0f), "size", R(0.25f, 0.36f), "duration", 1300));
                        l.Add(Ring(SoftRing, C, 1.4f, 0.3f, 700, 0));
                        l.Add(Own("shape", "burst", "tex", p.Puff, "fallback", "dot", "blend", "alpha", "color", new Color(0.16f, 0.1f, 0.22f, 0.55f), "count", 6, "spread", "up", "speed", R(0.3f, 0.7f), "life", R(0.8f, 1.1f), "size", R(0.7f, 1.0f), "size1", 1.3f, "anchor", "head", "duration", 1200));
                    }
                    break;
                case "mark":
                    if (phase == "impact" || phase == "area") { l.Add(Ring(SoftRing, C, 0.2f, 1.0f, 450, 0)); l.Add(Flash(L, 0.4f, 1.0f, 220, "chest")); l.Add(Sparks(p, 6, 0)); }
                    break;
                case "pulse":
                    if (phase == "area" || phase == "impact") { l.Add(Ring(SoftRing, C, 0.2f, 1.1f, 500, 0)); l.Add(Motes(p.Spark, L, 6, 300, 0.5f, 0.2f, 1.0f, R(1.0f, 1.6f), 0.6f, R(0.2f, 0.28f), 0)); }
                    break;
                case "zone":
                    if (phase == "area")
                    {
                        float rad = Radius(r, 1f);
                        l.Add(Own("shape", "disc", "tex", p.Sigil, "fallback", "sigil", "color", A(Lighter(C, 0.35f), 0.9f), "r", rad, "appear", 200, "fade", 400, "spin", 30, "duration", 1000, "attachedEntity", false));
                        l.Add(Own("shape", "ring", "tex", SoftRing, "fallback", "ring", "color", A(C, 0.6f), "r0", 0.3f, "r1", rad + 0.3f, "duration", 600, "attachedEntity", false));
                    }
                    break;
            }
        }

        // ---- building blocks (sizes in cells, times in ms)
        private static EffectPart Sparks(Palette p, int n, int delay) => Own("shape", "burst", "tex", p.Spark, "fallback", "spark", "color", A(Lighter(p.Col, 0.3f), 0.95f), "count", n, "speed", R(2f, 4f), "life", R(0.25f, 0.45f), "size", R(0.22f, 0.36f), "size1", 0.05f, "drag", 3f, "anchor", "chest", "duration", 500, "delayOffset", delay);
        private static EffectPart Flash(Color c, float s0, float s1, int ms, string anchor, int delay = 0) => Own("shape", "flash", "tex", Glow, "fallback", "dot", "color", A(c, 0.8f), "s0", s0, "s1", s1, "anchor", anchor, "duration", ms, "delayOffset", delay);
        private static EffectPart Arc(Color c, float size, float tilt, float sweep, float tiltRand, int ms, string orient = "v", string anchor = "chest", int delay = 0) =>
            Own("shape", "arc", "tex", Slash, "fallback", "crescent", "color", A(c, 0.9f), "size", size, "tilt", tilt, "sweep", sweep, "tiltRand", tiltRand, "orient", orient, "anchor", anchor, "duration", ms, "delayOffset", delay);
        private static EffectPart ShockRing(Color c, float r1, int delay) => Own("shape", "ring", "tex", Shock, "fallback", "ring", "color", A(c, 0.7f), "r0", 0.3f, "r1", r1, "duration", 450, "delayOffset", delay);
        private static EffectPart Ring(string tex, Color c, float r0, float r1, int ms, int delay) => Own("shape", "ring", "tex", tex, "fallback", "ring", "color", A(c, 0.75f), "r0", r0, "r1", r1, "duration", ms, "delayOffset", delay);
        private static EffectPart Column(string tex, Color c, float r0, float r1, float h, int rise, int ms, float alpha, int delay = 0) => Own("shape", "column", "tex", tex, "fallback", "dot", "color", A(c, alpha), "r0", r0, "r1", r1, "h", h, "rise", rise, "duration", ms, "delayOffset", delay);
        private static EffectPart Motes(string tex, Color c, int n, int emit, float radius, float h0, float h1, Vector2 vy, float life, Vector2 size, int delay) =>
            Own("shape", "motes", "tex", tex, "fallback", "spark", "color", A(c, 0.9f), "from", "column", "radius", radius, "h0", h0, "h1", h1, "vy", vy, "count", n, "emit", emit, "life", R(life * 0.8f, life * 1.1f), "size", size, "duration", emit + life * 1100f, "delayOffset", delay);
        private static EffectPart Link(string tex, Color c, int n, int travel, float spread, float arc) => Own("shape", "link", "tex", tex, "fallback", "spark", "color", A(c, 0.9f), "count", n, "travel", travel, "spread", spread, "arc", arc, "size", R(0.22f, 0.32f), "anchor", "chest", "duration", travel + spread * 1000f + 100f);
        private static EffectPart Orb(Palette p, float travelMs, float arc, int delay) => Own("shape", "orb", "tex", p.Orb, "fallback", "dot", "color", A(Color.white, 0.95f), "size", 0.7f, "arc", arc, "trail", true, "travel", Mathf.Clamp(travelMs, 120f, 900f), "anchor", "chest", "duration", Mathf.Clamp(travelMs, 120f, 900f) + 350f, "delayOffset", delay);

        // ---- hand-made recipes for the skills players notice first (Loki's design; the brief's list)
        private bool TryHand(Row r, string phase, int lv, float travelMs, List<EffectPart> l)
        {
            var p = Pal(r.Pal);
            switch (r.Recipe)
            {
                case "spirit": return Spirit(r, phase, l);
                case "kaite": return Kaite(phase, l);
                case "kaupe": return Kaupe(phase, l);
                case "eske": return Eske(phase, l);
                case "tk_storm": return TkStorm(r, phase, l);
                case "tk_down": return TkDown(phase, l);
                case "tk_turn": return TkTurn(r, phase, l);
                case "tk_counter": return TkCounter(phase, l);
                case "tk_ready": return TkReady(r.Arg, phase, l);
                case "tk_dodge": return TkDodge(phase, l);
                case "tk_sevenwind": return TkSevenWind(r, lv, phase, l);
                case "sg_comfort": return Celestial(r.Arg, phase, l, false);
                case "sg_warm": return Celestial(r.Arg, phase, l, true);
                case "sg_feel": return Feel(lv, phase, l);
                case "sg_hate": return Hate(lv, phase, l);
                case "napalm": return Napalm(phase, travelMs, l);
                case "ganbantein": return Ganbantein(phase, l);
                case "mirror": return Mirror(phase, l);
                case "gatling": return Gatling(phase, l);
                case "tarot": return Tarot(phase, l);
                case "cartterm": return CartTermination(phase, l);
            }
            return false;
        }

        /// <summary>The Soul Linker's spirits: a lilac sigil under the linker, spirit light flowing to the target, then a column, motes and a
        /// halo in the linked class's colour (its GRF sparkle texture: pikapika_&lt;class&gt;).</summary>
        private bool Spirit(Row r, string phase, List<EffectPart> l)
        {
            var parts = r.Arg.Split('|'); string tex = "effect\\" + parts[0]; var rgb = parts.Length > 1 ? parts[1].Split(',') : new[] { "1", "1", "1" };
            var cls = new Color(F(rgb[0]), F(rgb[1]), F(rgb[2])); var lilac = new Color(0.82f, 0.64f, 1f);
            switch (phase)
            {
                case "use":
                    l.Add(Own("shape", "disc", "tex", "effect\\magic_target.tga", "fallback", "sigil", "color", A(lilac, 0.75f), "r", 1.3f, "appear", 200, "fade", 350, "spin", 60, "duration", 1000));
                    l.Add(Flash(lilac, 0.4f, 1.0f, 300, "chest")); return true;
                case "travel": l.Add(Link("effect\\pikapika_lilac.tga", lilac, 16, 600, 0.5f, 0.8f)); return true;
                case "impact": case "area":
                    l.Add(Column("effect\\ring_white.tga", cls, 0.6f, 0.75f, 3.0f, 400, 1500, 0.5f));
                    l.Add(Motes(tex, Lighter(cls, 0.2f), 20, 1200, 0.7f, 0f, 1.0f, R(1.0f, 1.8f), 1.0f, R(0.3f, 0.45f), 0));
                    l.Add(Own("shape", "ring", "tex", "", "fallback", "ring", "color", A(cls, 0.8f), "r0", 0.9f, "r1", 0.7f, "y0", 3.0f, "y1", 0.05f, "linear", true, "duration", 1400));
                    l.Add(Flash(cls, 0.6f, 1.6f, 350, "chest", 1250));
                    return true;
                case "cast": return true;
            }
            return true;
        }
        /// <summary>Kaite (reflects magic): a pale lilac bubble around the target with crystal shards turning in it.</summary>
        private bool Kaite(string phase, List<EffectPart> l)
        {
            var c = new Color(0.82f, 0.78f, 1f);
            if (phase == "use") l.Add(Flash(c, 0.3f, 0.8f, 240, "chest"));
            else if (phase == "travel") l.Add(Link("effect\\pikapika_lilac.tga", c, 10, 450, 0.3f, 0.5f));
            else if (phase == "impact" || phase == "area")
            {
                l.Add(Own("shape", "flash", "tex", Shock, "fallback", "ring", "color", A(c, 0.45f), "s0", 1.2f, "s1", 2.6f, "hold", 900, "pulse", 0.04f, "anchor", "waist", "fadeout", 0.35f, "duration", 1500));
                l.Add(Own("shape", "orbit", "tex", "effect\\icearrow.tga", "fallback", "spark", "color", A(Color.white, 0.9f), "count", 8, "radius", 1.1f, "h0", 0.6f, "h1", 1.6f, "speed", 160, "size", 0.5f, "duration", 1400));
                l.Add(Sparks(Pal(8), 10, 1250));
            }
            return true;
        }
        /// <summary>Kaupe (dodges a hit): pale feathers spiralling up around the target.</summary>
        private bool Kaupe(string phase, List<EffectPart> l)
        {
            var c = new Color(0.78f, 0.95f, 1f);
            if (phase == "use") l.Add(Flash(c, 0.3f, 0.8f, 240, "chest"));
            else if (phase == "travel") l.Add(Link("effect\\pikapika_cyan.tga", c, 10, 450, 0.3f, 0.5f));
            else if (phase == "impact" || phase == "area")
            {
                l.Add(Own("shape", "orbit", "tex", "effect\\feather.bmp", "fallback", "spark", "color", A(Color.white, 0.95f), "count", 10, "radius", 0.85f, "h0", 0.1f, "h1", 2.3f, "speed", 300, "size", 0.38f, "duration", 1200));
                l.Add(Ring(SoftRing, c, 0.3f, 1.2f, 600, 0));
            }
            return true;
        }
        /// <summary>Eske (the target takes more damage): crimson embers sinking into it, a crimson ring closing.</summary>
        private bool Eske(string phase, List<EffectPart> l)
        {
            var c = new Color(1f, 0.35f, 0.25f);
            if (phase == "impact" || phase == "area")
            {
                l.Add(Own("shape", "motes", "tex", "effect\\fireparticle.tga", "fallback", "dot", "color", A(c, 0.95f), "from", "ring", "radius", 1.1f, "h0", 1.6f, "h1", 2.6f, "vy", R(-1.6f, -1.0f), "inward", 1.3f, "swirl", -200, "count", 18, "emit", 600, "life", R(0.8f, 1.0f), "size", R(0.3f, 0.42f), "duration", 1300));
                l.Add(Ring(SoftRing, c, 1.5f, 0.3f, 700, 0)); l.Add(Flash(c, 0.5f, 1.3f, 300, "chest", 500));
            }
            return true;
        }
        /// <summary>Taekwon's Tornado Kick: crescents of wind laid flat, spinning out around the kicker to the splash area, and dust.</summary>
        private bool TkStorm(Row r, string phase, List<EffectPart> l)
        {
            var c = new Color(0.82f, 1f, 0.86f);
            if (phase == "area")
            {
                float rad = Radius(r, 2f);
                l.Add(Own("shape", "whirl", "tex", Slash, "fallback", "crescent", "color", A(c, 0.85f), "count", 4, "r0", 0.6f, "r1", rad, "size", 1.6f, "speed", 720, "anchor", "waist", "duration", 550));
                l.Add(ShockRing(c, rad, 0));
                l.Add(Own("shape", "burst", "tex", Puff, "fallback", "dot", "blend", "alpha", "color", new Color(0.85f, 0.8f, 0.7f, 0.45f), "count", 10, "spread", "disc", "speed", R(2f, 3.5f), "life", R(0.5f, 0.7f), "size", R(0.6f, 0.9f), "size1", 1.2f, "drag", 2f, "duration", 800));
            }
            else if (phase == "splash" || phase == "hit") { l.Add(Arc(c, 1.4f, 0f, 160f, 180f, 220, "h", "waist")); l.Add(Sparks(Pal(-1), 5, 0)); }
            return true;
        }
        /// <summary>Heel Drop: a crescent coming down from above the target, then dust and stones at its feet.</summary>
        private bool TkDown(string phase, List<EffectPart> l)
        {
            var c = new Color(1f, 0.9f, 0.7f);
            if (phase == "impact")
            {
                l.Add(Arc(c, 2.4f, 120f, -150f, 10f, 300, "v", "chest"));
                l.Add(Own("shape", "ring", "tex", Shock, "fallback", "ring", "color", new Color(0.95f, 0.8f, 0.55f, 0.7f), "r0", 0.3f, "r1", 1.7f, "duration", 450, "delayOffset", 150));
                l.Add(Own("shape", "burst", "tex", "effect\\stone-1.bmp", "fallback", "dot", "color", A(Color.white, 0.95f), "count", 8, "spread", "up", "speed", R(2f, 4f), "gravity", 10f, "floor", true, "life", R(0.6f, 0.9f), "size", R(0.18f, 0.28f), "duration", 1000, "delayOffset", 150));
                l.Add(Flash(c, 0.5f, 1.4f, 220, "feet", 150));
            }
            else if (phase == "hit") l.Add(Sparks(Pal(-1), 5, 0));
            return true;
        }
        /// <summary>Roundhouse Kick: a wide flat crescent sweeping round the target, a shock ring pushing out.</summary>
        private bool TkTurn(Row r, string phase, List<EffectPart> l)
        {
            var c = new Color(0.75f, 0.88f, 1f);
            if (phase == "impact") { l.Add(Arc(c, 2.8f, 0f, 220f, 180f, 320, "h", "waist")); l.Add(ShockRing(c, Radius(r, 1.5f) + 0.5f, 120)); l.Add(Sparks(Pal(-1), 8, 100)); }
            else if (phase == "splash" || phase == "hit") l.Add(Sparks(Pal(-1), 6, 0));
            return true;
        }
        /// <summary>Counter Kick: an upward diagonal crescent and a white starburst.</summary>
        private bool TkCounter(string phase, List<EffectPart> l)
        {
            if (phase == "impact")
            {
                l.Add(Arc(Color.white, 2.2f, -45f, 110f, 15f, 260, "v", "chest"));
                l.Add(Own("shape", "flash", "tex", "effect\\pikapika_white.tga", "fallback", "spark", "color", A(Color.white, 0.85f), "s0", 0.6f, "s1", 1.9f, "spin", 3f, "anchor", "chest", "duration", 320));
                l.Add(Sparks(Pal(-1), 10, 0));
            }
            else if (phase == "hit") l.Add(Sparks(Pal(-1), 5, 0));
            return true;
        }
        /// <summary>The four kick stances: a small ring and motes rising, in the kick's own colour.</summary>
        private bool TkReady(string arg, string phase, List<EffectPart> l)
        {
            if (phase != "area" && phase != "impact") return true;
            var c = arg == "storm" ? new Color(0.62f, 1f, 0.72f) : arg == "down" ? new Color(1f, 0.66f, 0.32f) : arg == "turn" ? new Color(0.52f, 0.75f, 1f) : new Color(1f, 1f, 1f);
            l.Add(Ring(SoftRing, c, 0.2f, 1.0f, 500, 0)); l.Add(Motes("effect\\pikapika_white.tga", c, 8, 300, 0.45f, 0.1f, 0.8f, R(1.6f, 2.4f), 0.6f, R(0.2f, 0.28f), 0));
            return true;
        }
        /// <summary>Tumbling: small crescents whirling at the feet.</summary>
        private bool TkDodge(string phase, List<EffectPart> l)
        {
            if (phase != "area" && phase != "impact") return true;
            var c = new Color(0.9f, 0.95f, 1f);
            l.Add(Own("shape", "whirl", "tex", Slash, "fallback", "crescent", "color", A(c, 0.8f), "count", 3, "r0", 0.3f, "r1", 0.9f, "size", 0.9f, "speed", 900, "y", 0.15f, "duration", 400));
            l.Add(Ring(SoftRing, c, 0.2f, 1.0f, 450, 0));
            return true;
        }
        /// <summary>Mild Wind: sparks of the level's element (skill_db.yml TK_SEVENWIND: Earth, Wind, Water, Fire, Ghost, Dark, Holy) circling up.</summary>
        private bool TkSevenWind(Row r, int lv, string phase, List<EffectPart> l)
        {
            if (phase != "area" && phase != "impact") return true;
            var els = r.Arg.Split(','); int el = lv >= 1 && lv <= els.Length ? I(els[lv - 1]) : r.Pal; var p = Pal(el);
            l.Add(Own("shape", "orbit", "tex", p.Spark, "fallback", "spark", "color", A(Lighter(p.Col, 0.25f), 0.9f), "count", 10, "radius", 0.9f, "h0", 0.2f, "h1", 1.9f, "speed", 260, "size", 0.36f, "duration", 1200));
            l.Add(Ring(SoftRing, p.Col, 0.3f, 1.2f, 600, 0));
            return true;
        }
        /// <summary>Star Gladiator's sun / moon / star: Comfort (a symbol above the head, its light falling round), Warmth (a warm ring
        /// spreading round the caster under the symbol).</summary>
        private bool Celestial(string body, string phase, List<EffectPart> l, bool warm)
        {
            if (phase != "area" && phase != "impact") return true;
            var c = body == "sun" ? new Color(1f, 0.78f, 0.3f) : body == "moon" ? new Color(0.72f, 0.82f, 1f) : new Color(0.9f, 0.92f, 1f);
            Symbol(body, c, l, 1300);
            if (warm)
            {
                l.Add(Own("shape", "ring", "tex", SoftRing, "fallback", "ring", "color", A(c, 0.7f), "r0", 0.5f, "r1", 3.5f, "duration", 1000));
                l.Add(Column("effect\\ring_yellow.tga", c, 0.7f, 1.0f, 1.4f, 300, 900, 0.35f));
            }
            else
            {
                l.Add(Own("shape", "disc", "tex", "effect\\magic_target.tga", "fallback", "sigil", "color", A(c, 0.7f), "r", 1.5f, "appear", 250, "fade", 400, "spin", 30, "duration", 1300));
                l.Add(Own("shape", "motes", "tex", body == "star" ? "effect\\ring_star.tga" : "effect\\pikapika_white.tga", "fallback", "spark", "color", A(Lighter(c, 0.3f), 0.9f), "from", "above", "radius", 1.0f, "h0", 2.6f, "h1", 3.0f, "vy", R(-1.6f, -1.0f), "count", 14, "emit", 900, "life", R(0.9f, 1.2f), "size", R(0.25f, 0.35f), "duration", 1500));
            }
            return true;
        }
        private static void Symbol(string body, Color c, List<EffectPart> l, int ms)
        {
            if (body == "sun") l.Add(Own("shape", "flash", "tex", "effect\\glow01.bmp", "fallback", "dot", "color", A(c, 0.85f), "s0", 0.6f, "s1", 1.0f, "hold", ms - 400, "pulse", 0.06f, "anchor", "above", "y", 0.3f, "fadeout", 0.3f, "duration", ms));
            else if (body == "moon") l.Add(Own("shape", "arc", "tex", "", "fallback", "crescent", "color", A(c, 0.9f), "size", 1.1f, "tilt", 20f, "sweep", 0f, "anchor", "above", "y", 0.3f, "duration", ms));
            else l.Add(Own("shape", "flash", "tex", "effect\\pikapika_ultramarine.tga", "fallback", "spark", "color", A(c, 0.9f), "s0", 0.6f, "s1", 1.0f, "hold", ms - 400, "spin", 1.5f, "anchor", "above", "y", 0.3f, "fadeout", 0.3f, "duration", ms));
        }
        /// <summary>Feeling the Sun, Moon and Stars (level 1 sun, 2 moon, 3 star: rAthena feel_map[skill_lv - 1]): the symbol and a wide sigil
        /// marking the map.</summary>
        private bool Feel(int lv, string phase, List<EffectPart> l)
        {
            if (phase != "area" && phase != "impact") return true;
            string body = lv == 2 ? "moon" : lv == 3 ? "star" : "sun"; var c = body == "sun" ? new Color(1f, 0.78f, 0.3f) : body == "moon" ? new Color(0.72f, 0.82f, 1f) : new Color(0.9f, 0.92f, 1f);
            Symbol(body, c, l, 1600);
            l.Add(Own("shape", "disc", "tex", "effect\\magic_y_01.tga", "fallback", "sigil", "color", A(c, 0.7f), "r", 2.6f, "appear", 400, "fade", 500, "spin", 20, "duration", 1600));
            return true;
        }
        /// <summary>Hatred of the Sun, Moon and Stars (on a monster, level 1-3): the symbol over the marked monster and a ring at its feet.</summary>
        private bool Hate(int lv, string phase, List<EffectPart> l)
        {
            if (phase != "impact" && phase != "area") return true;
            string body = lv == 2 ? "moon" : lv == 3 ? "star" : "sun"; var c = body == "sun" ? new Color(1f, 0.6f, 0.25f) : body == "moon" ? new Color(0.6f, 0.72f, 1f) : new Color(0.85f, 0.85f, 1f);
            Symbol(body, c, l, 1300); l.Add(Ring(SoftRing, c, 1.2f, 0.4f, 700, 0));
            return true;
        }
        /// <summary>Napalm Vulcan (ghost, one hit per level): violet ghost lights falling onto the target, one per hit, each bursting.</summary>
        private bool Napalm(string phase, float travelMs, List<EffectPart> l)
        {
            var c = new Color(0.8f, 0.55f, 1f); var p = Pal(8);
            if (phase == "use") l.Add(Flash(c, 0.3f, 0.9f, 260, "chest"));
            else if (phase == "impact" || phase == "hit")
            {
                l.Add(Own("shape", "orb", "tex", "effect\\glow12.bmp", "fallback", "dot", "from", "above", "drop", 4f, "accel", true, "color", A(c, 0.85f), "size", 1.6f, "trail", true, "travel", 220, "anchor", "chest", "duration", 520, "delayOffset", -220));
                l.Add(Own("shape", "orb", "tex", "effect\\ghost01.bmp", "fallback", "dot", "from", "above", "drop", 4f, "accel", true, "color", A(Color.white, 0.95f), "size", 1.1f, "trail", false, "travel", 220, "anchor", "chest", "duration", 520, "delayOffset", -220));
                l.Add(Sparks(p, 10, 0)); l.Add(Flash(c, 0.5f, 1.4f, 260, "chest"));
                if (phase == "impact") l.Add(Ring(SoftRing, c, 0.3f, 1.5f, 450, 0));
            }
            else if (phase == "splash") l.Add(Sparks(p, 6, 0));
            return true;
        }
        /// <summary>Ganbantein (clears the ground skills of 3 × 3 cells): a golden ring sigil, a beam falling into it, the sigil breaking up.</summary>
        private bool Ganbantein(string phase, List<EffectPart> l)
        {
            var c = new Color(1f, 0.92f, 0.65f);
            if (phase != "area" && phase != "impact") return true;
            l.Add(Own("shape", "disc", "tex", "effect\\magic_ring_a.bmp", "fallback", "sigil", "color", A(c, 0.85f), "r", 1.9f, "appear", 250, "fade", 300, "spin", 70, "duration", 1500, "attachedEntity", false));
            l.Add(Own("shape", "column", "tex", "effect\\ring_white.tga", "fallback", "dot", "color", A(c, 0.5f), "r0", 0.9f, "r1", 1.2f, "h", 6f, "rise", 350, "down", true, "duration", 1000, "delayOffset", 200, "attachedEntity", false));
            l.Add(Own("shape", "burst", "tex", "effect\\pikapika_white.tga", "fallback", "spark", "color", A(c, 0.95f), "count", 24, "spread", "disc", "speed", R(3f, 6f), "life", R(0.35f, 0.55f), "size", R(0.25f, 0.4f), "size1", 0.05f, "drag", 2f, "duration", 700, "delayOffset", 1150, "attachedEntity", false));
            l.Add(Own("shape", "flash", "tex", Glow, "fallback", "dot", "color", A(c, 0.75f), "s0", 0.8f, "s1", 2.0f, "anchor", "feet", "y", 0.3f, "duration", 300, "delayOffset", 1100, "attachedEntity", false));
            return true;
        }
        /// <summary>Mirror Image: three dark afterimages of the ninja's own sprite slide out to the sides and fade, with smoke.</summary>
        private bool Mirror(string phase, List<EffectPart> l)
        {
            if (phase != "area" && phase != "impact") return true;
            l.Add(Own("shape", "ghost", "color", new Color(0.6f, 0.7f, 1f, 0.65f), "count", 3, "spread", 1.0f, "duration", 900));
            l.Add(Own("shape", "burst", "tex", Puff, "fallback", "dot", "blend", "alpha", "color", new Color(0.55f, 0.58f, 0.7f, 0.5f), "count", 8, "spread", "disc", "speed", R(1.0f, 2.0f), "life", R(0.6f, 0.8f), "size", R(0.6f, 0.9f), "size1", 1.3f, "drag", 2f, "y", 0.3f, "duration", 900));
            return true;
        }
        /// <summary>Gatling Fever: tracer streaks whirling round the gunslinger, muzzle sparks and an orange ring.</summary>
        private bool Gatling(string phase, List<EffectPart> l)
        {
            if (phase != "area" && phase != "impact") return true;
            var c = new Color(1f, 0.72f, 0.32f);
            l.Add(Own("shape", "whirl", "tex", "effect\\line3.tga", "fallback", "streak", "color", A(c, 0.9f), "count", 6, "r0", 0.7f, "r1", 1.1f, "size", 0.9f, "speed", 900, "anchor", "waist", "duration", 1200));
            l.Add(Own("shape", "burst", "tex", "effect\\fireparticle.tga", "fallback", "dot", "color", A(c, 0.95f), "count", 12, "spread", "disc", "speed", R(2f, 4f), "life", R(0.2f, 0.35f), "size", R(0.2f, 0.32f), "anchor", "waist", "duration", 500));
            l.Add(Ring(SoftRing, c, 0.3f, 1.3f, 600, 0));
            return true;
        }
        /// <summary>Tarot Card of Fate: one of the GRF's tarot cards rises over the target, turns twice and dissolves into violet sparks.</summary>
        private bool Tarot(string phase, List<EffectPart> l)
        {
            if (phase == "use") { l.Add(Flash(new Color(0.85f, 0.6f, 1f), 0.3f, 0.8f, 220, "chest")); return true; }
            if (phase != "impact" && phase != "area") return true;
            l.Add(Own("shape", "card", "tex", "", "fallback", "dot", "texs", new[] { "effect\\tarot01.tga", "effect\\tarot02.tga", "effect\\tarot03.tga", "effect\\tarot04.tga" }, "color", A(Color.white, 0.95f), "size", 1.4f, "spinTime", 800, "turns", 2f, "duration", 1600));
            l.Add(Own("shape", "burst", "tex", "effect\\pikapika_violet.tga", "fallback", "spark", "color", A(new Color(0.85f, 0.65f, 1f), 0.9f), "count", 12, "speed", R(1.5f, 3f), "life", R(0.3f, 0.5f), "size", R(0.22f, 0.32f), "anchor", "above", "y", 0.6f, "duration", 600, "delayOffset", 1400));
            return true;
        }
        /// <summary>Cart Termination: a heavy crescent slamming down, a dusty shock ring and the zeny it costs flying as gold coins.</summary>
        private bool CartTermination(string phase, List<EffectPart> l)
        {
            if (phase == "impact")
            {
                l.Add(Arc(new Color(0.95f, 0.95f, 1f), 3.0f, 100f, -160f, 10f, 300, "v", "chest"));
                l.Add(Own("shape", "ring", "tex", Shock, "fallback", "ring", "color", new Color(0.95f, 0.82f, 0.55f, 0.7f), "r0", 0.3f, "r1", 2.0f, "duration", 450, "delayOffset", 150));
                for (int i = 1; i <= 5; i++)
                    l.Add(Own("shape", "burst", "tex", $"effect\\efst_gold_shower\\coin00{i}.bmp", "fallback", "dot", "color", A(Color.white, 0.95f), "count", 3, "spread", "up", "speed", R(3f, 6f), "gravity", 12f, "floor", true, "life", R(0.9f, 1.3f), "size", R(0.5f, 0.65f), "duration", 1400, "delayOffset", 150));
                l.Add(Own("shape", "burst", "tex", Puff, "fallback", "dot", "blend", "alpha", "color", new Color(0.8f, 0.72f, 0.6f, 0.45f), "count", 8, "spread", "disc", "speed", R(1.5f, 3f), "life", R(0.6f, 0.8f), "size", R(0.6f, 0.9f), "size1", 1.3f, "drag", 2f, "duration", 900, "delayOffset", 150));
            }
            else if (phase == "hit") l.Add(Sparks(Pal(-1), 6, 0));
            return true;
        }
    }
}
