using System;
using System.Collections.Generic;
using Loki.Formats;
using Loki.IO;
using Loki.World;
using UnityEngine;

namespace Loki.Effects
{
    /// <summary>
    /// Client wave 4 (w4fx): the FUNC parts of roBrowserLegacy's effect table that draw something on the ground, rebuilt from what each FUNC
    /// hands to <c>this.add</c> (tools/fx/dump_fx.mjs runs every FUNC with recording stand-ins and ships the records as the part's "calls").
    /// roBrowserLegacy is a reimplementation, the only reference for these (the official client draws them in its code):
    ///  • FlatColorTile(name, {r,g,b,a}) — a flat 1 × 1 cell quad of that colour (Renderer/Effects/FlatColorTile.js, uSize 0.5);
    ///  • Tiles.HoveringTexture(tex, size, alpha) — a flat textured quad, 2·size cells wide, bobbing 0.2-0.6 cells above the cell
    ///    (Tiles.js:141-163: height + 0.4 − 0.2·sin(odd·π + t / 540π), texels darker than 0.1 discarded, alpha fixed);
    ///  • LPEffect / SpiderWeb — FlatTexture('aaa copy.bmp') / ('spiderweb.tga'), 2·(0.8 / 1.5 + 0.05·sin(…)) cells, texels darker than 0.5
    ///    discarded, alpha 0.7 (LPEffect.js, SpiderWeb.js, *.fs);
    ///  • PropertyGround(pos, top, bottom, height, tex) — Volcano / Deluge / Violent Gale: a twisted open cone turning 125°/s, its sizes
    ///    pulsing by max(0.5, sin(t / 360π + rand·10)), texels darker than 0.5 discarded, alpha 0.42 (PropertyGround.js / .vs / .fs);
    ///  • QuadHorn(part) at a cell offset — Heaven's Drive's 5 × 5 stone spikes (EffectTable.js EF_HEAVENSDRIVE), drawn by QuadHornFx;
    ///  • MagicRing(owner, top, bottom, height, tex, endTick) — the cast ring of Sharp Shooting / Preserve (496_beforecast): the same cone on
    ///    the caster, turning 250°/s, no pulse, alpha 0.7, until endTick or the caster's cast bar ends (MagicRing.js / .vs / .fs).
    /// Songs, dances, ensembles, Gospel, Fog Wall, Gravitation Field, Evil Land, Moonlit's salmon tile and Into the Abyss use the first two
    /// (Renderer/Effects/Songs.js). Camera quakes, weather, screen shaders and the body tint / size / spin animations are not drawn here
    /// (VR comfort; weather is w4world's; the unit looks are w4units' ActorSprite): design notes lists them.
    /// Times are rB's global renderer tick in ms (Time.time · 1000); positions are map-local cells, y up.
    /// </summary>
    public sealed class FuncCallsFx : FxRenderer
    {
        private static readonly HashSet<string> Drawn = new HashSet<string> { "FlatColorTile", "Tiles.HoveringTexture", "LPEffect", "SpiderWeb", "PropertyGround", "QuadHorn", "MagicRing" };
        private abstract class Child { public GameObject Go; public MeshRenderer Mr; public MaterialPropertyBlock Block = new MaterialPropertyBlock(); public abstract void Tick(FuncCallsFx o, float tickMs, Vector3 basePos); }
        private readonly List<Child> _children = new List<Child>();
        private readonly List<FxRenderer> _owned = new List<FxRenderer>();
        private static int _odd, _pgNum;

        /// <summary>The recorded calls of a FUNC part that this renderer draws (0 = none).</summary>
        public static int DrawableCalls(EffectPart p)
        {
            var calls = p?.L("calls"); if (calls == null) return 0;
            int n = 0;
            foreach (var c in calls) { var add = MiniJson.AsObject(MiniJson.AsObject(c)?.TryGetValue("add", out var a) == true ? a : null); if (add != null && Drawn.Contains(MiniJson.Str(add.TryGetValue("__cls", out var k) ? k : null, ""))) n++; }
            return n;
        }

        public static FuncCallsFx Create(EffectManager m, EffectManager.Instance inst)
        {
            if (DrawableCalls(inst.Part) == 0) return null;
            var f = Make<FuncCallsFx>(m, inst, "func:" + inst.Key);
            foreach (var c in inst.Part.L("calls"))
            {
                var rec = MiniJson.AsObject(c); if (rec == null || !rec.TryGetValue("add", out var av)) continue;
                var add = MiniJson.AsObject(av); if (add == null) continue;
                string cls = MiniJson.Str(add.TryGetValue("__cls", out var k) ? k : null, "");
                var args = MiniJson.AsList(add.TryGetValue("__args", out var ar) ? ar : null) ?? new List<object>();
                var fac = MiniJson.AsList(add.TryGetValue("__factory", out var fa) ? fa : null) ?? new List<object>();
                try { f.AddChild(cls, args, fac); } catch (Exception e) { Debug.LogWarning($"[Fx] {inst.Key} {cls}: {e.Message}"); }
            }
            if (f._children.Count == 0 && f._owned.Count == 0) { Destroy(f.gameObject); return null; }
            f.LateUpdate();
            return f;
        }

        private void AddChild(string cls, List<object> args, List<object> fac)
        {
            switch (cls)
            {
                case "FlatColorTile":
                {
                    var spec = fac.Count > 1 ? MiniJson.AsObject(fac[1]) : null; if (spec == null) return;
                    var col = new Color((float)MiniJson.Num(spec["r"]), (float)MiniJson.Num(spec["g"]), (float)MiniJson.Num(spec["b"]), spec.TryGetValue("a", out var a) ? (float)MiniJson.Num(a) : 0.5f);
                    _children.Add(Flat(new ColorTile { Color = col }, null, 0f, -1f, 0.5f));
                    break;
                }
                case "Tiles.HoveringTexture":
                {
                    string tex = fac.Count > 0 ? MiniJson.Str(fac[0], "") : "";
                    // Songs.js BragiSpellEffect: a random note spell_01..08 per unit (the dump pins it to 01); AbyssEffects: one of three gems
                    if (tex.EndsWith("spell_01.bmp")) tex = tex.Replace("spell_01.bmp", "spell_0" + UnityEngine.Random.Range(1, 9) + ".bmp");
                    else if (tex.Contains(AbyssGems[0])) tex = tex.Replace(AbyssGems[0], AbyssGems[UnityEngine.Random.Range(0, 3)]);
                    float size = fac.Count > 1 ? (float)MiniJson.Num(fac[1], 1) : 1f, alpha = fac.Count > 2 ? (float)MiniJson.Num(fac[2], 1) : 1f;
                    var ht = TexOf(tex); if (ht == null) return; // not in this GRF
                    _children.Add(Flat(new HoverTile { Size = size, Odd = (++_odd % 2 == 0) }, ht, 0.1f, alpha, size));
                    break;
                }
                case "LPEffect": { var t = Manager.Texture("effect\\aaa copy.bmp"); if (t != null) _children.Add(Flat(new PulseTile { Base = 0.8f, Odd = (++_odd % 2 == 0) }, t, 0.5f, 0.7f, 0.8f)); break; }
                case "SpiderWeb": { var t = Manager.Texture("effect\\spiderweb.tga"); if (t != null) _children.Add(Flat(new PulseTile { Base = 1.5f, Odd = (++_odd % 2 == 0) }, t, 0.5f, 0.7f, 1.5f)); break; }
                case "PropertyGround":
                {
                    // (position, topSize, bottomSize, height, textureName, tick)
                    float top = (float)MiniJson.Num(args.Count > 1 ? args[1] : null, 1), bottom = (float)MiniJson.Num(args.Count > 2 ? args[2] : null, 1), h = (float)MiniJson.Num(args.Count > 3 ? args[3] : null, 1);
                    string tex = args.Count > 4 ? MiniJson.Str(args[4], "") : "";
                    var ct = Manager.Texture("effect\\" + tex + ".tga"); if (ct == null) return; // not in this GRF
                    _children.Add(Cone(new ConeChild { Top = top, Bottom = bottom, Height = h, Rand = UnityEngine.Random.value * 10f, Ix = _pgNum++ % 16 }, ct, 0.42f));
                    break;
                }
                case "MagicRing":
                {
                    // (target entity, topSize, bottomSize, height, textureName, endTick = startTick + 10000)
                    float top = (float)MiniJson.Num(args.Count > 1 ? args[1] : null, 1), bottom = (float)MiniJson.Num(args.Count > 2 ? args[2] : null, 1), h = (float)MiniJson.Num(args.Count > 3 ? args[3] : null, 1);
                    string tex = args.Count > 4 ? MiniJson.Str(args[4], "") : ""; float endMs = (float)MiniJson.Num(args.Count > 5 ? args[5] : null, 10000);
                    var ct = Manager.Texture("effect\\" + tex + ".tga"); if (ct == null) return;
                    _children.Add(Cone(new ConeChild { Top = top, Bottom = bottom, Height = h, Ring = true, EndMs = endMs }, ct, 0.7f));
                    break;
                }
                case "QuadHorn":
                {
                    // (part, Inst {position: [100 + i, 100 + j, h]}, Init): the dump ran the FUNC at cell (100, 100)
                    var part = args.Count > 0 ? MiniJson.AsObject(args[0]) : null; if (part == null) return;
                    var instArgs = args.Count > 1 ? MiniJson.AsObject(args[1]) : null;
                    var pos = instArgs != null ? MiniJson.AsList(instArgs.TryGetValue("position", out var pv) ? pv : null) : null;
                    float dx = pos != null && pos.Count > 1 ? (float)MiniJson.Num(pos[0]) - 100f : 0f, dy = pos != null && pos.Count > 1 ? (float)MiniJson.Num(pos[1]) - 100f : 0f;
                    var cell = Inst.Position + new Vector3(dx, 0, dy); cell.y = Manager.Gat.Height(cell.x - 0.5f, cell.z - 0.5f);
                    var sub = new EffectManager.Instance
                    {
                        Key = Inst.Key, Part = new EffectPart { Type = "QuadHorn", P = part }, Position = cell, OtherPosition = Inst.OtherPosition, Start = Inst.Start,
                        End = Inst.Start + (float)MiniJson.Num(part.TryGetValue("duration", out var du) ? du : null, 1000) / 1000f,
                    };
                    var r = QuadHornFx.Create(Manager, sub);
                    if (r != null) { r.Inst = sub; _owned.Add(r); }
                    break;
                }
            }
        }

        private static readonly string[] AbyssGems = { "·¹µåÁª½ºÅæ", "¿»·Î¿ìÁª½ºÅæ", "ºí·çÁª½ºÅæ" }; // Songs.js AbyssEffects: red / yellow / blue gemstone (CP949 bytes)

        private Texture2D TexOf(string rbPath)
        {
            // rB paths are 'data/texture/effect/x.bmp'; RoAssets.Texture takes the part under data\texture\
            string p = rbPath.Replace('/', '\\'); const string pre = "data\\texture\\";
            if (p.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) p = p.Substring(pre.Length);
            return Manager.Texture(p);
        }

        // ---- shared pieces
        private static Shader _tileShader;
        private static Shader TileShader => _tileShader != null ? _tileShader : (_tileShader = Shader.Find("Loki/FxTile") ?? EffectManager.StrShader); // Resources/Shaders/LokiFxTile.shader
        private static readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();
        /// <summary>A material per (texture, cutoff, fixed alpha): rB's tile shaders discard dark texels and force the alpha (Tiles.fs, LPEffect.fs,
        /// PropertyGround.fs); FlatColorTile draws the colour alone (FlatColorTile.fs). Normal alpha blending (rB's default blendFunc).</summary>
        private static Material TileMaterial(Texture2D tex, float cutoff, float alpha)
        {
            string key = (tex != null ? tex.GetInstanceID() : 0) + ":" + cutoff + ":" + alpha;
            if (_mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(TileShader) { name = "fxtile:" + (tex != null ? tex.name : "colour"), mainTexture = tex != null ? tex : Texture2D.whiteTexture };
            m.SetFloat("_Cutoff", cutoff); m.SetFloat("_FixedAlpha", alpha);
            _mats[key] = m;
            return m;
        }
        private static Mesh _flat;
        /// <summary>The ±1 flat quad of Tiles.js / FlatColorTile.js in the ground plane, both faces, its texture turned 180° (Tiles.js loadTexture
        /// draws the image rotated by π before uploading it).</summary>
        private static Mesh FlatQuad()
        {
            if (_flat != null) return _flat;
            _flat = new Mesh { name = "fxtile" };
            _flat.vertices = new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(1, 0, 1), new Vector3(-1, 0, 1) };
            _flat.uv = new[] { new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0) };
            _flat.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
            _flat.RecalculateBounds();
            return _flat;
        }
        private Child Flat(Child c, Texture2D tex, float cutoff, float alpha, float size)
        {
            c.Go = new GameObject(c.GetType().Name); c.Go.transform.SetParent(transform, false);
            c.Go.AddComponent<MeshFilter>().sharedMesh = FlatQuad();
            c.Mr = c.Go.AddComponent<MeshRenderer>(); c.Mr.sharedMaterial = TileMaterial(tex, cutoff, alpha);
            c.Mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; c.Mr.receiveShadows = false;
            c.Go.transform.localScale = new Vector3(size, 1, size);
            return c;
        }
        private Child Cone(ConeChild c, Texture2D tex, float alpha)
        {
            c.Go = new GameObject(c.Ring ? "MagicRing" : "PropertyGround"); c.Go.transform.SetParent(transform, false);
            c.Mesh = ConeChild.Build(); c.Go.AddComponent<MeshFilter>().sharedMesh = c.Mesh;
            c.Mr = c.Go.AddComponent<MeshRenderer>(); c.Mr.sharedMaterial = TileMaterial(tex, 0.5f, alpha);
            c.Mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; c.Mr.receiveShadows = false;
            return c;
        }

        private sealed class ColorTile : Child
        {
            public Color Color;
            public override void Tick(FuncCallsFx o, float tickMs, Vector3 basePos)
            {
                Go.transform.localPosition = new Vector3(0, 0.02f, 0);
                Block.SetColor("_Color", Color); Mr.SetPropertyBlock(Block);
            }
        }
        private sealed class HoverTile : Child
        {
            public float Size; public bool Odd;
            public override void Tick(FuncCallsFx o, float tickMs, Vector3 basePos)
            {
                float h = 0.4f - 0.2f * Mathf.Sin((Odd ? Mathf.PI : 0f) + tickMs / (540f * Mathf.PI)); // Tiles.js:155-157
                Go.transform.localPosition = new Vector3(0, h, 0);
                Block.SetColor("_Color", Color.white); Mr.SetPropertyBlock(Block);
            }
        }
        private sealed class PulseTile : Child
        {
            public float Base; public bool Odd;
            public override void Tick(FuncCallsFx o, float tickMs, Vector3 basePos)
            {
                float s = Base + 0.05f * Mathf.Sin((Odd ? Mathf.PI : 0f) + tickMs / (540f * Mathf.PI)); // LPEffect.js:20-22, SpiderWeb.js:20-22
                Go.transform.localPosition = new Vector3(0, 0.03f, 0); Go.transform.localScale = new Vector3(s, 1, s);
                Block.SetColor("_Color", Color.white); Mr.SetPropertyBlock(Block);
            }
        }
        private sealed class ConeChild : Child
        {
            public float Top, Bottom, Height, Rand, EndMs; public int Ix; public bool Ring; public Mesh Mesh;
            private Vector3[] _base, _v;
            /// <summary>PropertyGround.js generatePropertyGround: 20 sides, the top ring turned half a side, texture u around, v 0 bottom / 1 top.</summary>
            public static Mesh Build()
            {
                const int total = 20;
                var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
                for (int i = 0; i < total; i++)
                {
                    float a0 = i / (float)total, a1 = (i + 1) / (float)total, b0 = (i + 0.5f) / total, b1 = (i + 1.5f) / total;
                    int n = v.Count;
                    v.Add(new Vector3(Mathf.Sin(a0 * Mathf.PI * 2), 0, Mathf.Cos(a0 * Mathf.PI * 2))); uv.Add(new Vector2(a0, 0));
                    v.Add(new Vector3(Mathf.Sin(b0 * Mathf.PI * 2), 1, Mathf.Cos(b0 * Mathf.PI * 2))); uv.Add(new Vector2(b0, 1));
                    v.Add(new Vector3(Mathf.Sin(a1 * Mathf.PI * 2), 0, Mathf.Cos(a1 * Mathf.PI * 2))); uv.Add(new Vector2(a1, 0));
                    v.Add(new Vector3(Mathf.Sin(b1 * Mathf.PI * 2), 1, Mathf.Cos(b1 * Mathf.PI * 2))); uv.Add(new Vector2(b1, 1));
                    tri.AddRange(new[] { n, n + 1, n + 2, n + 1, n + 3, n + 2, n, n + 2, n + 1, n + 1, n + 2, n + 3 });
                }
                var m = new Mesh { name = "propertyground" }; m.MarkDynamic();
                m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateBounds();
                return m;
            }
            public override void Tick(FuncCallsFx o, float tickMs, Vector3 basePos)
            {
                if (_base == null) { _base = Mesh.vertices; _v = new Vector3[_base.Length]; }
                float mult = Ring ? 1f : Mathf.Max(0.5f, Mathf.Sin(tickMs / (360f * Mathf.PI) + Rand)); // PropertyGround.js:130-133 (MagicRing: none)
                for (int i = 0; i < _base.Length; i++) { var b = _base[i]; bool top = b.y > 0.5f; float s = (top ? Top : Bottom) * mult; _v[i] = new Vector3(b.x * s, top ? Height : 0f, b.z * s); }
                Mesh.vertices = _v; Mesh.RecalculateBounds();
                // 16 shared rotations: offset i·2π/16 + tick / 8 degrees (PropertyGround.js beforeRender)
                Go.transform.localRotation = Quaternion.Euler(0, Ring ? tickMs / 4f : Ix * 360f / 16f + tickMs / 8f, 0); // MagicRing.js beforeRender: tick / 4 degrees
                // MagicRing.js render: done at endTick, or once the target's cast bar is gone
                if (Ring && (o.T * 1000f > EndMs || (o.T > 0.3f && o.Inst.Owner != null && o.Inst.Owner.Overhead != null && !o.Inst.Owner.Overhead.CastShown))) Go.SetActive(false);
                Block.SetColor("_Color", Color.white); Mr.SetPropertyBlock(Block);
            }
        }

        private void LateUpdate()
        {
            if (Inst == null) return;
            transform.localPosition = BasePos;
            float tick = Now * 1000f;
            foreach (var c in _children) c.Tick(this, tick, BasePos);
            if (_children.Count == 0) { bool any = false; foreach (var r in _owned) if (r != null && !r.Finished) any = true; if (!any) Finished = true; }
            else { bool shown = false; foreach (var c in _children) if (c.Go.activeSelf) shown = true; if (!shown) Finished = true; }
        }
        public override bool EndsAtEndTick => _children.Count == 0;
        private void OnDestroy() { foreach (var r in _owned) if (r != null) Destroy(r.gameObject); foreach (var c in _children) if (c is ConeChild k && k.Mesh != null) Destroy(k.Mesh); }
    }

    /// <summary>
    /// RSM parts (client wave 4, w4fx): the trap models of Hunters, Rangers and Geneticists (ef_trap_*, rB EffectTable.js) — an RSM from
    /// data\model\ standing on the unit's cell. roBrowserLegacy's placement (Renderer/Effects/RsmEffect.js:626-645 + RsmEffect.vs): the model's
    /// instance scale (−0.075, −0.075, 0.075) in cells per model unit and its RSM1 centring (box centre x / z, lowest point on the ground),
    /// so in Loki space (RsmMeshBuilder already applies F = diag(1/5, −1/5, 1/5)) it is 0.375 × the map-model size with x mirrored.
    /// Animated trap models play their node animation (RsmAnimator's mesh re-posing, 30 fps like rB's default).
    /// </summary>
    public sealed class RsmFx : FxRenderer
    {
        private RsmFile _rsm; private Mesh _mesh; private Matrix4x4 _rootFix; private RsmMeshBuilder.Buffers _buf; private float _loopMs; private int _lastFrame = -1;
        private static Shader _modelShader;
        public override bool EndsAtEndTick => Inst != null && Inst.End > 0;
        public const float Scale = 0.075f / 0.2f;

        public static RsmFx Create(EffectManager m, EffectManager.Instance inst)
        {
            string file = inst.Part.S("file"); if (string.IsNullOrEmpty(file)) return null;
            string path = RoPath.Normalize(file + ".rsm");
            var mesh = m.Assets.ModelMesh(path, out var rsm);
            if (mesh == null || rsm == null) return null;
            var r = Make<RsmFx>(m, inst, "rsm:" + file);
            r._rsm = rsm;
            if (RsmMeshBuilder.IsAnimated(rsm))
            {
                r._rootFix = RsmMeshBuilder.StaticRootFix(rsm); r._buf = new RsmMeshBuilder.Buffers(); r._loopMs = RsmMeshBuilder.LoopMs(rsm);
                r._mesh = RsmMeshBuilder.Build(rsm, 0f); r._mesh.MarkDynamic(); mesh = r._mesh;
            }
            r.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = r.gameObject.AddComponent<MeshRenderer>();
            if (_modelShader == null) _modelShader = Shader.Find("Loki/Model");
            var mats = new Material[Mathf.Max(rsm.Textures.Count, 1)];
            for (int i = 0; i < mats.Length; i++)
            {
                string tn = i < rsm.Textures.Count ? rsm.Textures[i] : "";
                var mat = new Material(_modelShader) { name = "fxrsm:" + tn };
                mat.SetTexture("_MainTex", m.Assets.Texture(tn)); mat.SetFloat("_Alpha", rsm.Alpha); mat.SetFloat("_Cull", 0f); // the mirrored scale flips the winding
                mats[i] = mat;
            }
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.transform.localScale = new Vector3(-Scale, Scale, Scale);
            return r;
        }

        private void LateUpdate()
        {
            if (OwnerGone) { Finished = true; return; }
            transform.localPosition = BasePos;
            if (_mesh != null && _loopMs > 0f)
            {
                float t = (T * 1000f) % _loopMs; int frame = (int)(t * 30f / 1000f);
                if (frame != _lastFrame) { _lastFrame = frame; RsmMeshBuilder.Fill(_rsm, t, _rootFix, _buf, false); _mesh.SetVertices(_buf.Verts); _mesh.SetNormals(_buf.Norms); _mesh.RecalculateBounds(); }
            }
        }
        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            var mr = GetComponent<MeshRenderer>(); if (mr != null) foreach (var m in mr.sharedMaterials) if (m != null) Destroy(m);
        }
    }
}
