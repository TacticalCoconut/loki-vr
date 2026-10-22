using System;
using System.Collections.Generic;
using Loki.IO;
using Loki.Sprites;
using UnityEngine;

namespace Loki.Effects
{
    /// <summary>
    /// Client wave 5 (w5fxown): the shapes of LOKI'S OWN skill effects — a design drawn in code, NOT the official client's look (no source
    /// shows what the official client draws for these skills: design notes). One effect part of type "OWN" (built by
    /// <see cref="OwnSkillFx"/>, spawned through <see cref="EffectManager.Spam"/> like any other part: its timing, the Comfort density rule,
    /// following the owner and the map clean-up are the effect manager's) draws one shape:
    ///   burst   sparks thrown from a point (sphere / flat disc / upward cone), with gravity and drag
    ///   motes   particles emitted over a time in a column / above the head / on a ring, rising, falling, swirling or closing in
    ///   orbit   particles circling the owner, rising or sinking
    ///   link    particles flowing from the other unit (the caster) to the owner (the target)
    ///   ring    a flat ring on the ground (or a halo at a height) growing / shrinking and fading
    ///   disc    a flat sigil that appears, turns and fades
    ///   column  an open cylinder of light rising around the owner
    ///   orb     a glowing ball travelling from the caster to the target with a trail (or falling from above)
    ///   streak  a straight beam from the caster to the target that fades at once (shots)
    ///   arc     a crescent slash: horizontal around the target, vertical or diagonal facing the viewer; it sweeps and fades
    ///   whirl   crescents laid flat in a ring that spins around the owner
    ///   flash   a camera-facing glow that grows and fades (impacts, never bigger than its size in cells)
    ///   card    a card that rises above the head, turns and fades (Tarot Card of Fate)
    ///   ghost   afterimages of the owner's current sprite sliding out to the sides and fading (Mirror Image)
    /// Art: textures from the player's GRF named by the part (data\texture\…, read at run time, never shipped); a texture that GRF lacks is
    /// replaced by one drawn here (soft dot, ring, spark, crescent, sigil), never an error. Sizes are in cells (they read the same at table
    /// scale and in first person, where a cell is 0.7 m). VR comfort: no camera movement, nothing full-screen, the Loki/OwnFx shader fades
    /// whatever comes near the eye and caps each layer's brightness.
    /// </summary>
    public sealed class OwnFxRenderer : FxRenderer
    {
        public override bool EndsAtEndTick => false; // ends itself (Finished) when its shape is done
        private string _shape; private float _dur; private EffectPart _p;
        private Color _color; private bool _add; private Material _mat;
        private Mesh _mesh; private readonly List<Vector3> _v = new List<Vector3>(); private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<Color> _c = new List<Color>(); private readonly List<int> _t = new List<int>();
        private System.Random _rng;

        // ---------------------------------------------------------------- creation
        public static OwnFxRenderer Create(EffectManager m, EffectManager.Instance inst)
        {
            var p = inst.Part; string shape = p.S("shape", "");
            if (string.IsNullOrEmpty(shape)) return null;
            if (shape == "ghost" && (inst.Owner == null || inst.Owner.Sprite == null || inst.Owner.Sprite.CurrentMesh == null)) return null; // nothing to copy
            var r = Make<OwnFxRenderer>(m, inst, "own:" + shape);
            r._p = p; r._shape = shape; r._dur = Mathf.Max(0.05f, p.F("duration", 800f) / 1000f);
            r._rng = new System.Random(unchecked((int)(inst.OwnerAid * 7919u + (uint)inst.Dup * 104729u + (uint)(Time.frameCount * 31))));
            r._color = Col(p, "color", Color.white); r._add = p.S("blend", "add") != "alpha";
            r._mesh = new Mesh { name = "ownfx" }; r._mesh.MarkDynamic();
            var tex = r.Tex(p.S("tex", ""), p.S("fallback", "dot"));
            r._mat = MaterialFor(tex, r._add);
            var mr = Attach(r.gameObject, r._mesh, r._mat);
            mr.sortingOrder = p.I("order");
            r.gameObject.transform.localPosition = Vector3.zero; // vertices are map-local (the fx root's space)
            r.Init();
            r.LateUpdate();
            return r;
        }

        // ---------------------------------------------------------------- textures and materials
        private static readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();
        private static Shader _shader;
        private static Material MaterialFor(Texture2D tex, bool add)
        {
            string key = (tex != null ? tex.GetInstanceID() : 0) + (add ? ":a" : ":b");
            if (_mats.TryGetValue(key, out var m) && m != null) return m;
            if (_shader == null) _shader = Shader.Find("Loki/OwnFx") ?? Shader.Find("Loki/Glow");
            m = new Material(_shader) { mainTexture = tex, name = "ownfx:" + (tex != null ? tex.name : "none") + (add ? "+" : "") };
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", add ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _mats[key] = m;
            return m;
        }
        /// <summary>A texture under data\texture\ from the player's GRF, or (missing there) one drawn in code. Counted for the smoke.</summary>
        private Texture2D Tex(string name, string fallback)
        {
            if (!string.IsNullOrEmpty(name))
            {
                string path = "data\\texture\\" + name.Replace('/', '\\');
                if (Manager != null && Manager.Assets != null && Manager.Assets.Exists(path)) { var t = Manager.Texture(name); if (t != null) { GrfTextures++; return t; } }
                MissingTextures++; LastMissing = name;
            }
            return CodeTex(fallback);
        }
        /// <summary>Textures taken from the GRF / drawn in code instead because the GRF lacks them (smoke counters).</summary>
        public static int GrfTextures, MissingTextures; public static string LastMissing;
        private Texture2D[] TexList(string key)
        {
            var l = _p.L(key); if (l == null || l.Count == 0) return null;
            var o = new List<Texture2D>();
            foreach (var x in l)
            {
                string n = MiniJson.Str(x, ""); if (n.Length == 0) continue;
                string path = "data\\texture\\" + n.Replace('/', '\\');
                if (Manager.Assets != null && Manager.Assets.Exists(path)) { var t = Manager.Texture(n); if (t != null) { o.Add(t); GrfTextures++; continue; } }
                MissingTextures++; LastMissing = n;
            }
            return o.Count > 0 ? o.ToArray() : null;
        }

        private static readonly Dictionary<string, Texture2D> _code = new Dictionary<string, Texture2D>();
        /// <summary>Textures drawn in code (Loki's own): dot, ring, spark, crescent, sigil, streak.</summary>
        public static Texture2D CodeTex(string kind)
        {
            if (string.IsNullOrEmpty(kind)) kind = "dot";
            if (_code.TryGetValue(kind, out var t) && t != null) return t;
            const int n = 64;
            t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "ownfx-" + kind };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f, d = Mathf.Sqrt(u * u + v * v), a;
                    switch (kind)
                    {
                        case "ring": a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.82f) * 9f) + (d < 0.82f ? 0.08f * d : 0f); break;
                        case "spark": { float star = Mathf.Max(Mathf.Clamp01(1f - Mathf.Abs(u) * 9f) * Mathf.Clamp01(1f - Mathf.Abs(v)), Mathf.Clamp01(1f - Mathf.Abs(v) * 9f) * Mathf.Clamp01(1f - Mathf.Abs(u))); a = Mathf.Clamp01(star + Mathf.Clamp01(1f - d * 2.2f)); break; }
                        case "crescent": { float outer = Mathf.Clamp01((1f - d) * 6f), inner = Mathf.Clamp01((Vector2.Distance(new Vector2(u, v), new Vector2(0.28f, 0f)) - 0.78f) * 6f); a = outer * inner * Mathf.Clamp01(0.5f - u * 0.5f + 0.5f); break; }
                        case "sigil": { float ring1 = Mathf.Clamp01(1f - Mathf.Abs(d - 0.9f) * 22f), ring2 = Mathf.Clamp01(1f - Mathf.Abs(d - 0.68f) * 26f); float ang = Mathf.Atan2(v, u); float ticks = d > 0.68f && d < 0.9f ? Mathf.Clamp01(1f - Mathf.Abs(Mathf.Sin(ang * 6f)) * 6f) * 0.8f : 0f; float star = d < 0.66f ? Mathf.Clamp01(1f - Mathf.Abs(Mathf.Sin(ang * 3f + d * 4f)) * 10f) * 0.5f : 0f; a = Mathf.Clamp01(ring1 + ring2 + ticks + star); break; }
                        case "streak": a = Mathf.Clamp01(1f - Mathf.Abs(v) * 1.4f) * Mathf.Clamp01(1f - Mathf.Abs(u) * 0.9f); a *= a; break;
                        default: a = Mathf.Clamp01(1f - d); a *= a; break;
                    }
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            t.SetPixels32(px); t.Apply(true, false);
            _code[kind] = t;
            return t;
        }

        // ---------------------------------------------------------------- helpers
        private static Color Col(EffectPart p, string key, Color d)
        {
            var l = p.L(key); if (l == null || l.Count < 3) return d;
            return new Color((float)MiniJson.Num(l[0]), (float)MiniJson.Num(l[1]), (float)MiniJson.Num(l[2]), l.Count > 3 ? (float)MiniJson.Num(l[3]) : 1f);
        }
        private float Rand(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
        private float RangeP(string key, float d)
        {
            var l = _p.L(key);
            if (l != null && l.Count >= 2) return Rand((float)MiniJson.Num(l[0]), (float)MiniJson.Num(l[1]));
            return _p.F(key, d);
        }
        /// <summary>A unit's height in cells: a player's body 2.4 (the composite frame's size includes weapons and garments, and Overhead puts
        /// players' name plates at 2.7), others their frame's height.</summary>
        public static float Height(Unit u) => u == null ? 2.1f : u.IsPlayerType ? 2.4f : u.Sprite != null ? Mathf.Clamp(u.Sprite.SizeCells.y, 0.8f, 3f) : 1.5f;
        /// <summary>The anchor height factor of a unit's height: feet 0, waist 0.4, chest 0.6, head 0.9, above 1.05.</summary>
        private static float AnchorFactor(string a) => a == "chest" ? 0.6f : a == "waist" ? 0.4f : a == "head" ? 0.9f : a == "above" ? 1.05f : 0f;
        private Vector3 OwnerPos => BasePos;
        private Vector3 OtherPos
        {
            get
            {
                var o = Inst.Other;
                if (o != null && o.gameObject != null) return o.transform.localPosition;
                return Inst.OtherPosition;
            }
        }
        private bool HasOther => Inst.Other != null && Inst.Other.gameObject != null && Inst.Other != Inst.Owner;
        private float OwnerH => Height(Inst.Owner);
        private Vector3 Center(string anchorKey = "anchor") => OwnerPos + Vector3.up * (AnchorFactor(_p.S(anchorKey, "feet")) * OwnerH + _p.F("y"));
        private Vector3 CamRight, CamUp, CamFwd;
        private void CameraAxes()
        {
            var cam = Cam; var root = Manager.FxRoot;
            if (cam == null || root == null) { CamRight = Vector3.right; CamUp = Vector3.up; CamFwd = Vector3.forward; return; }
            CamRight = root.InverseTransformDirection(cam.transform.right).normalized;
            CamUp = root.InverseTransformDirection(cam.transform.up).normalized;
            CamFwd = root.InverseTransformDirection(cam.transform.forward).normalized;
        }
        private Vector3 CamPosLocal { get { var cam = Cam; return cam != null ? Manager.FxRoot.InverseTransformPoint(cam.transform.position) : Vector3.up * 50f; } }
        private static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x); }
        /// <summary>Alpha over a life: fade in over the first <paramref name="fin"/>, out over the last <paramref name="fout"/> (fractions).</summary>
        private static float Fade(float k, float fin, float fout) => Mathf.Clamp01(fin > 0 ? k / fin : 1f) * Mathf.Clamp01(fout > 0 ? (1f - k) / fout : 1f);

        private void Quad(Vector3 c, Vector3 r, Vector3 u, Color col, Vector2 uv0, Vector2 uv1)
        {
            int n = _v.Count;
            _v.Add(c - r - u); _v.Add(c + r - u); _v.Add(c - r + u); _v.Add(c + r + u);
            _uv.Add(new Vector2(uv0.x, uv0.y)); _uv.Add(new Vector2(uv1.x, uv0.y)); _uv.Add(new Vector2(uv0.x, uv1.y)); _uv.Add(new Vector2(uv1.x, uv1.y));
            _c.Add(col); _c.Add(col); _c.Add(col); _c.Add(col);
            _t.Add(n); _t.Add(n + 2); _t.Add(n + 1); _t.Add(n + 2); _t.Add(n + 3); _t.Add(n + 1);
        }
        private void Quad(Vector3 c, Vector3 r, Vector3 u, Color col) => Quad(c, r, u, col, Vector2.zero, Vector2.one);
        private void Billboard(Vector3 c, float size, float rot, Color col)
        {
            float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot), h = size * 0.5f;
            var r = (CamRight * cs + CamUp * sn) * h; var u = (-CamRight * sn + CamUp * cs) * h;
            Quad(c, r, u, col);
        }
        private void Flush()
        {
            _mesh.Clear();
            if (_v.Count == 0) return;
            _mesh.SetVertices(_v); _mesh.SetUVs(0, _uv); _mesh.SetColors(_c); _mesh.SetTriangles(_t, 0); _mesh.RecalculateBounds();
            _v.Clear(); _uv.Clear(); _c.Clear(); _t.Clear();
        }

        // ---------------------------------------------------------------- particles (burst, motes, orbit, link)
        private struct P { public Vector3 Pos, Vel; public float Born, Life, S0, S1, Rot, Spin, Ang, Rad, H0, H1, Off; public int Tex; }
        private readonly List<P> _ps = new List<P>();
        private Texture2D[] _texs; private int _emitted, _toEmit; private float _emitTime;
        private Vector3 _anchor0;

        private void Init()
        {
            _anchor0 = Center();
            switch (_shape)
            {
                case "burst": _toEmit = Mathf.Clamp(_p.I("count", 12), 1, 120); EmitBurst(_toEmit); break;
                case "motes": _toEmit = Mathf.Clamp(_p.I("count", 16), 1, 160); _emitTime = Mathf.Max(0.01f, _p.F("emit", 600f) / 1000f); break;
                case "orbit": _toEmit = Mathf.Clamp(_p.I("count", 8), 1, 48); for (int i = 0; i < _toEmit; i++) _ps.Add(new P { Ang = i * Mathf.PI * 2f / _toEmit + Rand(-0.15f, 0.15f), Rad = RangeP("radius", 0.9f), S0 = RangeP("size", 0.35f), H0 = _p.F("h0", 0.2f), H1 = _p.F("h1", 1.8f), Off = Rand(0f, 0.25f), Rot = Rand(0f, 6.28f), Spin = Rand(-3f, 3f) }); break;
                case "link": _toEmit = Mathf.Clamp(_p.I("count", 14), 1, 80); for (int i = 0; i < _toEmit; i++) _ps.Add(new P { Off = i / (float)_toEmit * _p.F("spread", 0.5f), S0 = RangeP("size", 0.3f), Ang = Rand(0f, 6.28f), Rad = Rand(0.05f, 0.3f), Rot = Rand(0f, 6.28f) }); break;
                case "ghost": InitGhosts(); break;
            }
            // "texs": one of several GRF textures, picked per part (a recipe that wants variety spawns several copies: coins, notes, cards)
            _texs = TexList("texs");
            if (_texs != null) GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(_texs[_rng.Next(_texs.Length)], _add);
        }

        private void EmitBurst(int n)
        {
            string spread = _p.S("spread", "sphere"); float r0 = _p.F("radius", 0.1f);
            for (int i = 0; i < n; i++)
            {
                Vector3 d;
                if (spread == "disc") { float a = Rand(0f, 6.2832f); d = new Vector3(Mathf.Cos(a), Rand(0f, 0.25f), Mathf.Sin(a)); }
                else if (spread == "up") { float a = Rand(0f, 6.2832f), s = Rand(0f, 0.55f); d = new Vector3(Mathf.Cos(a) * s, 1f, Mathf.Sin(a) * s); }
                else if (spread == "down") { float a = Rand(0f, 6.2832f), s = Rand(0f, 0.5f); d = new Vector3(Mathf.Cos(a) * s, -1f, Mathf.Sin(a) * s); }
                else d = UnityEngine.Random.onUnitSphere;
                d.Normalize();
                _ps.Add(new P
                {
                    Pos = d * r0 * Rand(0.3f, 1f), Vel = d * RangeP("speed", 2.5f) + Vector3.up * _p.F("up"), Born = 0f, Life = RangeP("life", 0.6f),
                    S0 = RangeP("size", 0.35f), S1 = _p.Has("size1") ? RangeP("size1", 0.1f) : -1f, Rot = Rand(0f, 6.28f), Spin = Rand(-4f, 4f), Tex = _rng.Next(16),
                });
            }
            _emitted = n;
        }

        private void EmitMote(float now)
        {
            string from = _p.S("from", "column"); float r = _p.F("radius", 0.7f), h0 = _p.F("h0", 0f), h1 = _p.F("h1", 2f);
            float a = Rand(0f, 6.2832f), rr = r * Mathf.Sqrt(Rand(0.05f, 1f));
            Vector3 pos;
            if (from == "ring") pos = new Vector3(Mathf.Cos(a) * r, Rand(h0, h1), Mathf.Sin(a) * r);
            else if (from == "above") pos = new Vector3(Mathf.Cos(a) * rr, Rand(h0, h1), Mathf.Sin(a) * rr);
            else pos = new Vector3(Mathf.Cos(a) * rr, Rand(h0, h1), Mathf.Sin(a) * rr);
            var vel = new Vector3(0f, RangeP("vy", 1f), 0f) + new Vector3(Rand(-1f, 1f), 0f, Rand(-1f, 1f)) * _p.F("jitter", 0.1f);
            _ps.Add(new P { Pos = pos, Vel = vel, Born = now, Life = RangeP("life", 0.9f), S0 = RangeP("size", 0.3f), S1 = _p.Has("size1") ? RangeP("size1", 0.1f) : -1f, Rot = Rand(0f, 6.28f), Spin = Rand(-2f, 2f), Tex = _rng.Next(16) });
        }

        /// <summary>Particles of every shape that uses them, drawn as camera-facing quads (one mesh, one draw per part).</summary>
        private void TickParticles(float t)
        {
            float dt = Time.deltaTime; var c = _shape == "orbit" || _shape == "link" ? Center() : (_p.B("attachedEntity") ? Center() : _anchor0);
            float g = _p.F("gravity"), drag = _p.F("drag");
            if (_shape == "motes") { int want = Mathf.Min(_toEmit, Mathf.CeilToInt(_toEmit * Mathf.Clamp01(t / _emitTime))); while (_emitted < want) { EmitMote(t); _emitted++; } }
            float swirl = _p.F("swirl") * Mathf.Deg2Rad, inward = _p.F("inward");
            bool alive = false; float fin = _p.F("fadein", 0.15f), fout = _p.F("fadeout", 0.4f);
            if (_shape == "orbit")
            {
                float k = Mathf.Clamp01(t / _dur), w = _p.F("speed", 220f) * Mathf.Deg2Rad;
                for (int i = 0; i < _ps.Count; i++)
                {
                    var p = _ps[i];
                    float rad = p.Rad * Mathf.Lerp(1f, _p.F("shrink", 1f), k);
                    var pos = c + new Vector3(Mathf.Cos(p.Ang + w * t) * rad, Mathf.Lerp(p.H0, p.H1, Mathf.Clamp01(k + p.Off * (1f - k))) + Mathf.Sin(t * 5f + p.Ang) * 0.06f, Mathf.Sin(p.Ang + w * t) * rad);
                    float alpha = _color.a * Fade(k, fin, fout);
                    Billboard(pos, p.S0, p.Rot + p.Spin * t, new Color(_color.r, _color.g, _color.b, alpha));
                }
                alive = t < _dur;
            }
            else if (_shape == "link")
            {
                Vector3 from = HasOther ? OtherPos + Vector3.up * (Height(Inst.Other) * 0.6f) : c + Vector3.up * 2.5f, to = c;
                float travel = Mathf.Max(0.1f, _p.F("travel", 450f) / 1000f);
                var dir = to - from; var side = Vector3.Cross(dir.normalized, Vector3.up); if (side.sqrMagnitude < 1e-4f) side = Vector3.right; side.Normalize();
                for (int i = 0; i < _ps.Count; i++)
                {
                    var p = _ps[i]; float s = (t - p.Off) / travel;
                    if (s < 0f || s > 1f) { if (s < 0f) alive = true; continue; }
                    alive = true;
                    var pos = Vector3.Lerp(from, to, EaseOut(s)) + (side * Mathf.Cos(p.Ang + s * 9f) + Vector3.up * Mathf.Sin(p.Ang + s * 9f)) * p.Rad * Mathf.Sin(s * Mathf.PI) + Vector3.up * _p.F("arc") * Mathf.Sin(s * Mathf.PI);
                    Billboard(pos, p.S0 * (1f - 0.4f * s), p.Rot + s * 6f, new Color(_color.r, _color.g, _color.b, _color.a * Fade(s, 0.1f, 0.25f)));
                }
            }
            else
            {
                for (int i = 0; i < _ps.Count; i++)
                {
                    var p = _ps[i]; float age = t - p.Born, k = age / Mathf.Max(0.01f, p.Life);
                    if (k >= 1f) continue;
                    alive = true;
                    p.Vel += Vector3.down * g * dt; if (drag > 0) p.Vel *= Mathf.Max(0f, 1f - drag * dt);
                    if (swirl != 0f) { float ca = Mathf.Cos(swirl * dt), sa = Mathf.Sin(swirl * dt); p.Pos = new Vector3(p.Pos.x * ca - p.Pos.z * sa, p.Pos.y, p.Pos.x * sa + p.Pos.z * ca); }
                    if (inward != 0f) { var hz = new Vector3(p.Pos.x, 0f, p.Pos.z); float m = hz.magnitude; if (m > 0.02f) p.Pos -= hz / m * Mathf.Min(m, inward * dt); }
                    p.Pos += p.Vel * dt;
                    if (_p.B("floor") && c.y + p.Pos.y < OwnerPos.y + 0.03f) { p.Pos.y = OwnerPos.y + 0.03f - c.y; p.Vel = new Vector3(p.Vel.x * 0.5f, Mathf.Abs(p.Vel.y) * 0.35f, p.Vel.z * 0.5f); }
                    _ps[i] = p;
                    float size = p.S1 >= 0f ? Mathf.Lerp(p.S0, p.S1, k) : p.S0;
                    float alpha = _color.a * Fade(k, fin, fout);
                    if (_p.B("stretch") && p.Vel.sqrMagnitude > 0.01f) Stretched(c + p.Pos, p.Vel, size, new Color(_color.r, _color.g, _color.b, alpha));
                    else Billboard(c + p.Pos, size, p.Rot + p.Spin * age, new Color(_color.r, _color.g, _color.b, alpha));
                }
                if (_shape == "motes" && _emitted < _toEmit) alive = true;
            }
            if (!alive && t > 0.05f) Finished = true;
        }
        /// <summary>A spark stretched along its velocity as the viewer sees it.</summary>
        private void Stretched(Vector3 pos, Vector3 vel, float size, Color col)
        {
            var toCam = (CamPosLocal - pos).normalized;
            var along = Vector3.ProjectOnPlane(vel, toCam); float len = Mathf.Clamp(along.magnitude * 0.06f, size * 0.5f, size * 3f);
            if (along.sqrMagnitude < 1e-6f) { Billboard(pos, size, 0f, col); return; }
            along.Normalize(); var side = Vector3.Cross(along, toCam).normalized;
            Quad(pos, side * size * 0.25f, along * len * 0.5f, col);
        }

        // ---------------------------------------------------------------- the other shapes
        /// <summary>The near-eye fade in world metres from the map's scale (0.35 → 0.9 cells), set once a frame for every own effect (Loki/OwnFx).</summary>
        public const float NearFadeGoneCells = 0.35f, NearFadeFullCells = 0.9f;
        /// <summary>The GRF STR parts of own recipes (big layers: Fx/FxRenderers.cs StrFxRenderer "ownNearFade") fade from 3 cells to gone at
        /// 1.2 cells from the eye: in first person a 4th-job light on yourself never fills the view; at table scale nothing changes.</summary>
        public static float StrNearFade(Vector3 worldCentre, Camera cam, Transform fxRoot)
        {
            if (cam == null || fxRoot == null) return 1f;
            float cells = Vector3.Distance(cam.transform.position, worldCentre) / Mathf.Max(1e-6f, fxRoot.lossyScale.x);
            return Mathf.Clamp01((cells - 1.2f) / (3f - 1.2f));
        }
        private static int _fadeFrame = -1;
        private void LateUpdate()
        {
            if (Inst == null || Manager == null) return;
            if (_fadeFrame != Time.frameCount && Manager.FxRoot != null)
            {
                _fadeFrame = Time.frameCount; float s = Manager.FxRoot.lossyScale.x;
                Shader.SetGlobalVector("_LokiOwnNearFade", new Vector4(NearFadeGoneCells * s, NearFadeFullCells * s, 0f, 0f));
            }
            if (OwnerGone) { Finished = true; return; }
            CameraAxes();
            float t = T;
            switch (_shape)
            {
                case "burst": case "motes": case "orbit": case "link": TickParticles(t); break;
                case "ring": TickRing(t); break;
                case "disc": TickDisc(t); break;
                case "column": TickColumn(t); break;
                case "orb": TickOrb(t); break;
                case "streak": TickStreak(t); break;
                case "arc": TickArc(t); break;
                case "whirl": TickWhirl(t); break;
                case "flash": TickFlash(t); break;
                case "card": TickCard(t); break;
                case "ghost": TickGhost(t); break;
                default: Finished = true; break;
            }
            if (_shape != "burst" && _shape != "motes" && _shape != "link" && t > _dur) Finished = true;
            Flush();
        }

        private void TickRing(float t)
        {
            float k = Mathf.Clamp01(t / _dur), e = _p.B("linear") ? k : EaseOut(k);
            float r = Mathf.Lerp(_p.F("r0", 0.2f), _p.F("r1", 1.5f), e);
            float y = Mathf.Lerp(_p.F("y0", 0.04f), _p.F("y1", _p.F("y0", 0.04f)), e);
            var c = (_p.B("attachedEntity") ? OwnerPos : Inst.Position) + Vector3.up * y;
            float a = _color.a * Fade(k, _p.F("fadein", 0.1f), _p.F("fadeout", 0.6f));
            float ang = (_p.F("rot") + _p.F("spin") * t) * Mathf.Deg2Rad;
            var rx = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r; var rz = new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * r;
            Quad(c, rx, rz, new Color(_color.r, _color.g, _color.b, a));
        }
        private void TickDisc(float t)
        {
            float appear = Mathf.Max(0.01f, _p.F("appear", 250f) / 1000f), fade = Mathf.Max(0.01f, _p.F("fade", 400f) / 1000f);
            float s = EaseOut(t / appear) * _p.F("r", 1.5f);
            float a = _color.a * Mathf.Clamp01(t / appear) * Mathf.Clamp01((_dur - t) / fade);
            var c = (_p.B("attachedEntity") ? OwnerPos : Inst.Position) + Vector3.up * _p.F("y0", 0.05f);
            float ang = (_p.F("rot") + _p.F("spin", 40f) * t) * Mathf.Deg2Rad;
            var rx = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * s; var rz = new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * s;
            Quad(c, rx, rz, new Color(_color.r, _color.g, _color.b, a));
        }
        private void TickColumn(float t)
        {
            float k = Mathf.Clamp01(t / _dur), rise = Mathf.Clamp01(t / Mathf.Max(0.01f, _p.F("rise", 300f) / 1000f));
            float r = Mathf.Lerp(_p.F("r0", 0.6f), _p.F("r1", 0.8f), EaseOut(k)), h = _p.F("h", 2.4f) * EaseOut(rise);
            float a = _color.a * Fade(k, 0.08f, _p.F("fadeout", 0.5f));
            var c = OwnerPos + Vector3.up * _p.F("y0", 0f);
            bool down = _p.B("down"); // grows downward from h (a beam falling from the sky)
            const int sides = 20; float spin = (_p.F("spin", 60f) * t) * Mathf.Deg2Rad;
            for (int i = 0; i < sides; i++)
            {
                float a0 = spin + i * Mathf.PI * 2f / sides, a1 = spin + (i + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * r), p1 = new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * r);
                float yb = down ? _p.F("h", 2.4f) - h : 0f, yt = down ? _p.F("h", 2.4f) : h;
                int n = _v.Count; var col = new Color(_color.r, _color.g, _color.b, a);
                _v.Add(c + p0 + Vector3.up * yb); _v.Add(c + p1 + Vector3.up * yb); _v.Add(c + p0 + Vector3.up * yt); _v.Add(c + p1 + Vector3.up * yt);
                float u0 = i / (float)sides * 2f, u1 = (i + 1) / (float)sides * 2f;
                _uv.Add(new Vector2(u0, 0f)); _uv.Add(new Vector2(u1, 0f)); _uv.Add(new Vector2(u0, 1f)); _uv.Add(new Vector2(u1, 1f));
                _c.Add(col); _c.Add(col); _c.Add(col * new Color(1, 1, 1, 0.15f)); _c.Add(col * new Color(1, 1, 1, 0.15f));
                _t.Add(n); _t.Add(n + 2); _t.Add(n + 1); _t.Add(n + 2); _t.Add(n + 3); _t.Add(n + 1);
            }
        }
        private readonly List<(Vector3 pos, float born)> _trail = new List<(Vector3, float)>();
        private void TickOrb(float t)
        {
            float travel = Mathf.Max(0.05f, _p.F("travel", _dur * 1000f) / 1000f), s = Mathf.Clamp01(t / travel);
            var to = OwnerPos + Vector3.up * (AnchorFactor(_p.S("anchor", "chest")) * OwnerH + _p.F("y"));
            Vector3 from = _p.S("from", "") == "above" || !HasOther ? to + new Vector3(_p.F("dx", 0.6f), _p.F("drop", 4f), _p.F("dz", 0.3f)) : OtherPos + Vector3.up * (Height(Inst.Other) * 0.6f);
            var pos = Vector3.Lerp(from, to, _p.B("accel") ? s * s : s) + Vector3.up * _p.F("arc") * Mathf.Sin(s * Mathf.PI);
            float size = _p.F("size", 0.6f) * (1f + 0.1f * Mathf.Sin(t * 30f));
            if (s < 1f)
            {
                if (_p.B("trail") && (_trail.Count == 0 || (pos - _trail[_trail.Count - 1].pos).sqrMagnitude > 0.02f)) _trail.Add((pos, t));
                Billboard(pos, size, t * _p.F("spin", 6f), new Color(_color.r, _color.g, _color.b, _color.a * Mathf.Clamp01(t / 0.06f)));
            }
            for (int i = _trail.Count - 1; i >= 0; i--)
            {
                float age = t - _trail[i].born; if (age > 0.3f) { _trail.RemoveAt(i); continue; }
                Billboard(_trail[i].pos, size * (0.7f - age * 1.5f), i, new Color(_color.r, _color.g, _color.b, _color.a * 0.5f * (1f - age / 0.3f)));
            }
            if (s >= 1f && _trail.Count == 0) Finished = true;
        }
        private void TickStreak(float t)
        {
            float k = Mathf.Clamp01(t / _dur);
            Vector3 b = OwnerPos + Vector3.up * OwnerH * 0.6f, a = HasOther ? OtherPos + Vector3.up * Height(Inst.Other) * 0.6f : b + Vector3.up * 3f;
            float head = Mathf.Clamp01(t / Mathf.Max(0.01f, _p.F("travel", 120f) / 1000f)); var tip = Vector3.Lerp(a, b, head); var tail = Vector3.Lerp(a, b, Mathf.Clamp01(head - _p.F("length", 0.6f)));
            var mid = (tip + tail) * 0.5f; var along = (tip - tail) * 0.5f; if (along.sqrMagnitude < 1e-6f) return;
            var toCam = (CamPosLocal - mid).normalized; var side = Vector3.Cross(along.normalized, toCam).normalized * _p.F("width", 0.12f) * (1f - 0.5f * k);
            Quad(mid, along, side, new Color(_color.r, _color.g, _color.b, _color.a * (1f - k)), new Vector2(0f, 0f), new Vector2(1f, 1f));
        }
        private float _arcTilt = float.NaN;
        private void TickArc(float t)
        {
            float k = Mathf.Clamp01(t / _dur), e = EaseOut(k);
            if (float.IsNaN(_arcTilt)) _arcTilt = _p.F("tilt") + Rand(-_p.F("tiltRand"), _p.F("tiltRand"));
            var c = Center(); float size = _p.F("size", 1.6f) * Mathf.Lerp(0.85f, 1.1f, e);
            float rot = (_arcTilt + _p.F("sweep", 120f) * e) * Mathf.Deg2Rad;
            float a = _color.a * Fade(k, 0.05f, 0.55f);
            var col = new Color(_color.r, _color.g, _color.b, a);
            string orient = _p.S("orient", "v");
            if (orient == "h")
            {
                var rx = new Vector3(Mathf.Cos(rot), 0f, Mathf.Sin(rot)) * size * 0.5f; var rz = new Vector3(-Mathf.Sin(rot), 0f, Mathf.Cos(rot)) * size * 0.5f;
                Quad(c, rx, rz, col);
            }
            else Billboard(c, size, rot, col); // "v" and "d" (the tilt makes it diagonal), facing the viewer
        }
        private void TickWhirl(float t)
        {
            float k = Mathf.Clamp01(t / _dur), e = EaseOut(k);
            int n = Mathf.Clamp(_p.I("count", 4), 1, 12); var c = Center();
            float r = Mathf.Lerp(_p.F("r0", 0.6f), _p.F("r1", 2f), e), size = _p.F("size", 1.4f), w = _p.F("speed", 540f) * Mathf.Deg2Rad;
            float a = _color.a * Fade(k, 0.08f, 0.5f);
            for (int i = 0; i < n; i++)
            {
                float ang = i * Mathf.PI * 2f / n + w * t;
                var pos = c + new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(t * 7f + i) * 0.08f, Mathf.Sin(ang) * r);
                // the crescent lies flat, its back to the centre, sweeping round (its texture's convex side points along +u)
                var outv = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)); var tang = new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                Quad(pos, outv * size * 0.5f, tang * size * 0.5f, new Color(_color.r, _color.g, _color.b, a));
            }
        }
        private void TickFlash(float t)
        {
            float k = Mathf.Clamp01(t / _dur);
            float hold = _p.F("hold", 0f) / 1000f, pulse = _p.F("pulse");
            float s = Mathf.Lerp(_p.F("s0", 0.4f), _p.F("s1", 1.2f), EaseOut(k)) * (1f + pulse * Mathf.Sin(t * 9f));
            float a = _color.a * (t < hold ? Mathf.Clamp01(t / 0.08f) : Fade(k, 0.06f, _p.F("fadeout", 0.7f)));
            Billboard(Center(), s, _p.F("rot") * Mathf.Deg2Rad + t * _p.F("spin"), new Color(_color.r, _color.g, _color.b, a));
        }
        private float _cardAspect = -1f;
        private void TickCard(float t)
        {
            float k = Mathf.Clamp01(t / _dur);
            if (_cardAspect < 0f) { var tex = GetComponent<MeshRenderer>().sharedMaterial.mainTexture; _cardAspect = tex != null && tex.height > 0 ? tex.width / (float)tex.height : 0.5f; }
            var c = OwnerPos + Vector3.up * (OwnerH + 0.25f + 0.4f * EaseOut(Mathf.Clamp01(t / 0.5f)));
            float spinT = _p.F("spinTime", 800f) / 1000f, turns = _p.F("turns", 2f);
            float yaw = t < spinT ? EaseOut(t / spinT) * turns * Mathf.PI * 2f : 0f;
            float h = _p.F("size", 1.4f) * 0.5f, w = h * _cardAspect;
            var flat = Vector3.ProjectOnPlane(CamRight, Vector3.up).normalized; if (flat.sqrMagnitude < 1e-4f) flat = Vector3.right;
            var toCam = Vector3.Cross(Vector3.up, flat);
            var r = (flat * Mathf.Cos(yaw) + toCam * Mathf.Sin(yaw)) * w;
            float a = _color.a * Fade(k, 0.08f, 0.25f);
            Quad(c, r, Vector3.up * h, new Color(_color.r, _color.g, _color.b, a));
        }

        // ---- ghost: afterimages of the owner's sprite (Mirror Image)
        private readonly List<(GameObject go, Vector3 dir, Material mat)> _ghosts = new List<(GameObject, Vector3, Material)>();
        private readonly List<Mesh> _ghostMeshes = new List<Mesh>();
        private void InitGhosts()
        {
            var sp = Inst.Owner.Sprite; var quad = sp.CurrentQuad; var mesh = sp.CurrentMesh; var tex = sp.CurrentTexture;
            if (quad == null || mesh == null || tex == null) return;
            int n = Mathf.Clamp(_p.I("count", 3), 1, 6);
            if (_shader == null) _shader = Shader.Find("Loki/OwnFx") ?? Shader.Find("Loki/Glow");
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("ghost" + i); go.transform.SetParent(transform, false);
                // a copy of the sprite's mesh with white vertex colours (the sprite mesh has none; Loki/OwnFx multiplies by them)
                var copy = UnityEngine.Object.Instantiate(mesh); var white = new Color[copy.vertexCount]; for (int w = 0; w < white.Length; w++) white[w] = Color.white; copy.colors = white;
                _ghostMeshes.Add(copy);
                go.AddComponent<MeshFilter>().sharedMesh = copy;
                var mat = new Material(_shader) { mainTexture = tex, name = "ownfx:ghost" };
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                float side = n == 1 ? 1f : (i % 2 == 0 ? 1f : -1f) * (1f + i / 2);
                _ghosts.Add((go, new Vector3(side, 0f, i == 2 ? -0.6f : 0f), mat));
            }
        }
        private void TickGhost(float t)
        {
            var sp = Inst.Owner != null ? Inst.Owner.Sprite : null; var quad = sp != null ? sp.CurrentQuad : null;
            float k = Mathf.Clamp01(t / _dur), e = EaseOut(Mathf.Clamp01(t / (_dur * 0.5f)));
            foreach (var (go, dir, mat) in _ghosts)
            {
                if (quad == null) { go.SetActive(false); continue; }
                var offWorld = Manager.FxRoot.TransformVector(CamRight * dir.x * _p.F("spread", 0.9f) * e + Vector3.ProjectOnPlane(CamFwd, Vector3.up).normalized * dir.z * e);
                go.transform.position = quad.position + offWorld;
                go.transform.rotation = quad.rotation;
                var ls = Manager.FxRoot.lossyScale; var qs = quad.lossyScale;
                go.transform.localScale = new Vector3(qs.x / Mathf.Max(1e-5f, ls.x), qs.y / Mathf.Max(1e-5f, ls.y), qs.z / Mathf.Max(1e-5f, ls.z));
                mat.SetColor("_Color", new Color(_color.r, _color.g, _color.b, _color.a * Fade(k, 0.1f, 0.5f)));
            }
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            foreach (var g in _ghosts) if (g.mat != null) Destroy(g.mat);
            foreach (var m in _ghostMeshes) if (m != null) Destroy(m);
        }
    }
}
