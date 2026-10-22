using System;
using System.Collections.Generic;
using Loki.Formats;
using Loki.IO;
using Loki.Sprites;
using Loki.World;
using UnityEngine;

namespace Loki.Effects
{
    /// <summary>Base of every effect renderer: a GameObject under the map's fx root (map-local units = cells, y up) that follows its owner when attached.</summary>
    public abstract class FxRenderer : MonoBehaviour
    {
        public EffectManager Manager; public EffectManager.Instance Inst;
        /// <summary>Set when the renderer has nothing more to draw (STR reached maxKey, sprite ended…).</summary>
        public bool Finished;
        /// <summary>True when the manager should end this instance at its endTick (cylinders, particles); STRs end on their own frame count.</summary>
        public virtual bool EndsAtEndTick => true;
        public bool Repeated;
        protected float Now => Time.time;
        protected float T => Inst == null ? 0f : Now - Inst.Start;
        protected float Duration => Inst == null || Inst.End <= 0 ? 0f : Inst.End - Inst.Start;
        /// <summary>0..1 over the instance's duration (roBrowser's `steps`/100).</summary>
        protected float Steps => Duration > 0 ? Mathf.Clamp01(T / Duration) : 1f;

        protected static T Make<T>(EffectManager m, EffectManager.Instance inst, string name) where T : FxRenderer
        {
            var go = new GameObject(name);
            go.transform.SetParent(m.FxRoot, false);
            var r = go.AddComponent<T>();
            r.Manager = m; r.Inst = inst;
            go.transform.localPosition = inst.Position;
            return r;
        }

        /// <summary>The owner's current map-local position (attached parts), else the snapshot.</summary>
        protected Vector3 BasePos
        {
            get
            {
                if (Inst != null && Inst.Attached && Inst.Owner != null && Inst.Owner.gameObject != null) return Inst.Owner.transform.localPosition;
                return Inst != null ? Inst.Position : Vector3.zero;
            }
        }
        protected bool OwnerGone => Inst != null && Inst.Attached && (Inst.Owner == null || Inst.Owner.gameObject == null);

        /// <summary>Spherical billboard: the object faces the head like the STR/sprite shaders do (model-view 3×3 = identity).</summary>
        protected void FaceCamera()
        {
            var cam = Manager != null ? Manager.Head : null; if (cam == null) cam = Camera.main; if (cam == null) return;
            transform.rotation = cam.transform.rotation;
        }
        protected Camera Cam { get { var c = Manager != null ? Manager.Head : null; return c != null ? c : Camera.main; } }
        /// <summary>Camera yaw in the map's frame, degrees (roBrowser Camera.angle[1]).</summary>
        protected float CameraYaw { get { var c = Cam; if (c == null) return 0f; var f = Manager.FxRoot.InverseTransformDirection(c.transform.forward); f.y = 0; return f.sqrMagnitude > 1e-6f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : 0f; } }

        protected static Mesh Quad(float w, float h, float ox = 0, float oy = 0)
        {
            var m = new Mesh();
            m.vertices = new[] { new Vector3(-w / 2 + ox, -h / 2 + oy, 0), new Vector3(w / 2 + ox, -h / 2 + oy, 0), new Vector3(-w / 2 + ox, h / 2 + oy, 0), new Vector3(w / 2 + ox, h / 2 + oy, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1, 0, 1, 2, 2, 1, 3 };
            m.RecalculateBounds();
            return m;
        }
        protected static MeshRenderer Attach(GameObject go, Mesh mesh, Material mat)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            return mr;
        }
    }

    /// <summary>A Gravity STR effect (research §2): one billboard quad per layer, key frames evaluated per frame, D3D blend factors per key.</summary>
    public sealed class StrFxRenderer : FxRenderer
    {
        private StrFile _str; private string _texPath; private Texture2D[][] _tex; private GameObject[] _layers; private Mesh[] _meshes; private MeshRenderer[] _mr;
        private readonly StrFile.Frame _frame = new StrFile.Frame();
        private readonly Vector3[] _v = new Vector3[4]; private readonly Vector2[] _uv = new Vector2[4];
        public override bool EndsAtEndTick => false;
        private bool _loop; private float _xOff, _yOff;
        /// <summary>Client wave 5 (w5fxown): an own recipe's GRF STR (part "ownNearFade") fades near the eye (OwnFxRenderer.StrNearFade).</summary>
        private bool _ownNearFade;

        public static StrFxRenderer Create(EffectManager m, EffectManager.Instance inst)
        {
            var part = inst.Part;
            string file = part.S("file"); if (string.IsNullOrEmpty(file)) return null;
            if (file.Contains("%d")) { var r = part.L("rand"); int a = r != null && r.Count > 1 ? MiniJson.Int(r[0]) : 1, b = r != null && r.Count > 1 ? MiniJson.Int(r[1]) : 1; file = file.Replace("%d", UnityEngine.Random.Range(a, b + 1).ToString()); }
            var str = m.Str(file); if (str == null) return null;
            var s = Make<StrFxRenderer>(m, inst, "str:" + file);
            s._str = str; s._texPath = part.S("texturePath", "") ?? "";
            s._tex = new Texture2D[str.Layers.Length][]; s._layers = new GameObject[str.Layers.Length]; s._meshes = new Mesh[str.Layers.Length]; s._mr = new MeshRenderer[str.Layers.Length];
            for (int i = 0; i < str.Layers.Length; i++)
            {
                var l = str.Layers[i];
                s._tex[i] = new Texture2D[l.Textures.Length];
                for (int t = 0; t < l.Textures.Length; t++) s._tex[i][t] = m.Texture("effect\\" + s._texPath + l.Textures[t]);
                var go = new GameObject("layer" + i); go.transform.SetParent(s.transform, false);
                s._meshes[i] = new Mesh(); s._meshes[i].MarkDynamic();
                s._meshes[i].vertices = new Vector3[4]; s._meshes[i].uv = new Vector2[4]; s._meshes[i].triangles = new[] { 0, 1, 2, 2, 1, 3, 0, 2, 1, 2, 3, 1 };
                s._mr[i] = Attach(go, s._meshes[i], null);
                go.SetActive(false);
                s._layers[i] = go;
            }
            s._loop = inst.Persistent && !part.Has("repeat"); // a persistent unit loops its STR in place (roBrowser re-spams it; same look, cheaper)
            s._xOff = part.F("xOffset"); s._yOff = part.F("yOffset");
            s._ownNearFade = part.B("ownNearFade"); // client wave 5 (w5fxown)
            s.Draw();
            return s;
        }

        private void LateUpdate()
        {
            if (OwnerGone) { Finished = true; return; }
            transform.localPosition = BasePos;
            FaceCamera();
            Draw();
        }

        private void Draw()
        {
            float key = T * _str.Fps;
            if (key >= _str.MaxKey && _str.MaxKey > 0) { if (_loop) key %= _str.MaxKey; else { Finished = true; foreach (var g in _layers) g.SetActive(false); return; } }
            for (int i = 0; i < _str.Layers.Length; i++)
            {
                var l = _str.Layers[i];
                if (l.Textures.Length == 0 || !StrFile.Evaluate(l, key, _frame) || _frame.TexIndex >= _tex[i].Length || _tex[i][_frame.TexIndex] == null) { _layers[i].SetActive(false); continue; } // a texture this GRF lacks: the layer is not drawn (w4fx)
                var f = _frame;
                float ang = -f.Angle * Mathf.Deg2Rad, c = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                // rB StrEffect.js:364-375 + StrEffect.vs: an STR on an entity is centred at (320, 320) with no vertical base and moved by the
                // part's xOffset / yOffset × PIXEL_TO_WORLD_Z (1/5) × 35 px (ez2 entries' PosX / PosY); one on the ground keeps the 0.5 base
                // (client wave 4, w4fx: attached STRs sat half a cell low before)
                float ox, oy;
                if (Inst.Attached) { ox = (f.PosX - 320f) / ActorSprite.PixelsPerCell + _xOff * 0.2f; oy = -((f.PosY - 320f) / ActorSprite.PixelsPerCell - _yOff * 0.2f); }
                else { ox = (f.PosX - 320f) / ActorSprite.PixelsPerCell; oy = -((f.PosY - 320f) / ActorSprite.PixelsPerCell + 0.5f); }
                // corners 0 TL, 1 TR, 2 BR, 3 BL in the file; pixel y grows downwards
                for (int k = 0; k < 4; k++)
                {
                    float x = f.Xy[k] / ActorSprite.PixelsPerCell, y = -f.Xy[4 + k] / ActorSprite.PixelsPerCell;
                    _v[k] = new Vector3(x * c - y * sn + ox, x * sn + y * c + oy, 0);
                }
                // roBrowser ignores the file's uv[] and uses the four corners (StrEffect.js:337-355); kept until the uv layout is verified on real files
                _uv[0] = new Vector2(0, 1); _uv[1] = new Vector2(1, 1); _uv[2] = new Vector2(1, 0); _uv[3] = new Vector2(0, 0);
                var mesh = _meshes[i]; mesh.vertices = _v; mesh.uv = _uv; mesh.RecalculateBounds();
                int src = EffectManager.D3dBlend(f.SrcAlpha), dst = EffectManager.D3dBlend(f.DestAlpha);
                if (src < 0) src = (int)UnityEngine.Rendering.BlendMode.SrcAlpha; if (dst < 0) dst = (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
                var mat = Manager.Material(_tex[i][f.TexIndex], src, dst);
                var mr = _mr[i];
                if (mr.sharedMaterial != mat) mr.sharedMaterial = mat;
                var block = _block ?? (_block = new MaterialPropertyBlock());
                float fade = _ownNearFade ? OwnFxRenderer.StrNearFade(transform.TransformPoint(new Vector3(ox, oy, 0f)), Cam, Manager.FxRoot) : 1f; // client wave 5 (w5fxown)
                block.SetColor("_Color", new Color(f.Color[0], f.Color[1], f.Color[2], f.Color[3] * fade));
                mr.SetPropertyBlock(block);
                _layers[i].SetActive(true);
            }
        }
        private MaterialPropertyBlock _block;
        private void OnDestroy() { if (_meshes != null) foreach (var m in _meshes) if (m != null) Destroy(m); }
    }

    /// <summary>CYLINDER parts (Cylinder.js): an open cone between bottomSize at the feet and topSize at height, tinted, additive by blendMode, with the five growth animations.</summary>
    public sealed class CylinderFx : FxRenderer
    {
        private Mesh _mesh; private Vector3[] _base; private Vector3[] _v; private MeshRenderer _mr; private MaterialPropertyBlock _block;
        private float _top, _bottom, _height, _alphaMax; private int _anim; private bool _fade, _rotate, _rotWithCam, _fixedPersp; private float _ax, _ay, _az; private Vector3 _pos; private Color _tint;
        private int _sides, _draw;

        public static CylinderFx Create(EffectManager m, EffectManager.Instance inst)
        {
            var p = inst.Part;
            string tex = p.S("textureName"); if (string.IsNullOrEmpty(tex)) return null;
            var c = Make<CylinderFx>(m, inst, "cyl:" + tex);
            c._top = p.F("topSize"); c._bottom = p.F("bottomSize"); c._height = p.F("height"); c._alphaMax = p.F("alphaMax") > 0 ? p.F("alphaMax") : 1f;
            c._anim = p.I("animation"); c._fade = p.B("fade"); c._rotate = p.B("rotate"); c._rotWithCam = p.B("rotateWithCamera"); c._fixedPersp = p.B("fixedPerspective");
            c._ax = p.F("angleX") + (p.Has("angleXRandom") ? Mathf.Floor(UnityEngine.Random.value * p.F("angleXRandom")) : 0);
            c._ay = p.F("angleY") + (p.Has("angleYRandom") ? Mathf.Floor(UnityEngine.Random.value * p.F("angleYRandom")) : 0);
            c._az = p.F("angleZ") + (p.Has("angleZRandom") ? Mathf.Floor(UnityEngine.Random.value * p.F("angleZRandom")) : 0);
            if (p.B("rotateToTarget")) { var d = inst.OtherPosition - inst.Position; c._ay += 90f - Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg; }
            if (p.B("rotateWithSource")) c._ay += 180f - 45f * inst.Direction;
            c._pos = new Vector3(p.F("posX"), p.F("posZ"), p.F("posY"));
            c._tint = new Color(p.Has("red") ? p.F("red") : 1f, p.Has("green") ? p.F("green") : 1f, p.Has("blue") ? p.F("blue") : 1f, 1f);
            c._sides = p.Has("totalCircleSides") ? p.I("totalCircleSides") : 20; c._draw = p.Has("circleSides") ? p.I("circleSides") : c._sides;
            float repeat = p.Has("repeatTextureX") ? p.F("repeatTextureX") : 1f;
            // mesh: (sin, cos) ring, z flag 0 bottom / 1 top (Cylinder.js:44-81)
            int n = c._draw + 1;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int i = 0; i <= c._draw; i++)
            {
                float a = (float)i / c._sides;
                float u = a * (c._sides / (float)c._draw) * repeat;
                verts.Add(new Vector3(Mathf.Sin(a * Mathf.PI * 2), 0, Mathf.Cos(a * Mathf.PI * 2))); uvs.Add(new Vector2(u, 0)); // bottom (v flipped for Unity: file v=1 at the bottom)
                verts.Add(new Vector3(Mathf.Sin(a * Mathf.PI * 2), 1, Mathf.Cos(a * Mathf.PI * 2))); uvs.Add(new Vector2(u, 1)); // top
            }
            for (int i = 0; i < c._draw; i++)
            {
                int b0 = i * 2, t0 = i * 2 + 1, b1 = (i + 1) * 2, t1 = (i + 1) * 2 + 1;
                tris.Add(b0); tris.Add(t0); tris.Add(b1); tris.Add(t0); tris.Add(t1); tris.Add(b1);
                tris.Add(b0); tris.Add(b1); tris.Add(t0); tris.Add(t0); tris.Add(b1); tris.Add(t1);
            }
            c._base = verts.ToArray(); c._v = new Vector3[c._base.Length];
            c._mesh = new Mesh(); c._mesh.MarkDynamic(); c._mesh.vertices = c._v; c._mesh.uv = uvs.ToArray(); c._mesh.triangles = tris.ToArray();
            var t2d = m.Texture("effect\\" + tex + ".tga");
            if (t2d == null) { Destroy(c.gameObject); return null; } // not in this GRF (w4fx)
            if (repeat > 1f) t2d.wrapMode = TextureWrapMode.Repeat;
            c._mr = Attach(c.gameObject, c._mesh, m.Material(t2d, (int)UnityEngine.Rendering.BlendMode.SrcAlpha, EffectManager.PartBlend(p.I("blendMode"))));
            c.Tick();
            return c;
        }

        private void LateUpdate() { if (OwnerGone) { Finished = true; return; } Tick(); }

        private void Tick()
        {
            float t = T * 1000f, dur = Duration * 1000f;
            float height = _height, top = _top, bottom = _bottom;
            switch (_anim)
            {
                case 1: height = dur > 1000 ? (t <= 1000 ? t / 1000f * _height : _height) : (dur > 0 ? t / dur * _height : _height); break;
                case 2: top = dur > 1000 ? (t <= 1000 ? t / 1000f * _top : _top) : (dur > 0 ? t / dur * _top : _top); break;
                case 3: if (dur > 0) { bottom = (1 - t / dur) * _bottom; top = (1 - t / dur) * _top; height = t < dur / 2 ? t * _height / (dur / 2) : (dur - t) * _height / (dur / 2); } break;
                case 4: if (dur > 0) { bottom = Mathf.Max(0, t / dur * _bottom); top = Mathf.Max(0, t / dur * _top); } break;
                case 5: if (dur > 0) height = t < dur / 2 ? t * 2 / dur * _height : (dur - t) * _height / (dur / 2); break;
            }
            float alpha = _alphaMax;
            if (_fade && dur > 0) { if (t < dur / 4) alpha = t * _alphaMax / (dur / 4); else if (t > dur * 0.75f) alpha = (dur - t) * _alphaMax / (dur / 4); }
            for (int i = 0; i < _base.Length; i++) { var b = _base[i]; float s = b.y > 0.5f ? top : bottom; _v[i] = new Vector3(b.x * s, b.y > 0.5f ? height : 0f, b.z * s); }
            _mesh.vertices = _v; _mesh.RecalculateBounds();
            var rot = Quaternion.identity;
            if (_rotate) rot *= Quaternion.Euler(0, Now * 1000f / 4f % 360f, 0);
            rot *= Quaternion.Euler(_ax, _ay, _az);
            var pos = BasePos;
            if (_rotWithCam || _fixedPersp)
            {
                float yaw = CameraYaw;
                if (_fixedPersp) { var c = Cam; if (c != null) { float pitch = Manager.FxRoot.InverseTransformDirection(c.transform.forward).y; rot = Quaternion.Euler(-Mathf.Asin(Mathf.Clamp(pitch, -1, 1)) * Mathf.Rad2Deg, 0, 0) * rot; } }
                rot = Quaternion.Euler(0, yaw, 0) * rot;
                pos += Quaternion.Euler(0, yaw, 0) * _pos;
            }
            else pos += _pos;
            transform.localPosition = pos; transform.localRotation = rot;
            (_block ?? (_block = new MaterialPropertyBlock())).SetColor("_Color", new Color(_tint.r, _tint.g, _tint.b, Mathf.Clamp01(alpha)));
            _mr.SetPropertyBlock(_block);
        }
        private void OnDestroy() { if (_mesh != null) Destroy(_mesh); }
    }

    /// <summary>3D / 2D parts (ThreeDEffect.js, TwoDEffect.js): a camera-facing textured quad (or an effect sprite's first frame) that moves from its
    /// start offsets to its end offsets over the lifetime, fades, sparkles, spins and can fly between the two entities (fromSrc / toSrc, arc, retreat).</summary>
    public sealed class BillboardFx : FxRenderer
    {
        private Texture2D[] _frames; private float _frameDelay; private MeshRenderer _mr; private Mesh _mesh; private MaterialPropertyBlock _block; private Material _mat0; private int _dst;
        private Vector3 _pStart, _pEnd; private bool _pxs, _pys, _pzs, _rotPos; private float _rotPosX, _rotPosY, _nbRot, _rotLate; private bool _cw;
        private float _sx0, _sy0, _sx1, _sy1; private bool _sizeSmooth; private float _angle, _toAngle; private bool _rotate, _rotCam, _fadeIn, _fadeOut, _sparkle; private float _sparkN;
        private float _alphaMax, _arc, _retreat; private Color _tint; private bool _shadow, _2d, _circle; private float _dur2d;
        private bool _toTarget; private float _baseAngle;

        private static float Rand(float a, float b) => UnityEngine.Random.Range(a, b);

        public static BillboardFx Create(EffectManager m, EffectManager.Instance inst, bool twoD)
        {
            var p = inst.Part;
            var b = Make<BillboardFx>(m, inst, (twoD ? "2d:" : "3d:") + (p.S("file") ?? p.S("spriteName") ?? p.S("absoluteSpriteName") ?? "?"));
            b._2d = twoD;
            // texture(s)
            var list = p.L("fileList");
            if (list != null && list.Count > 0) { b._frames = new Texture2D[list.Count]; for (int i = 0; i < list.Count; i++) b._frames[i] = m.Texture(MiniJson.Str(list[i], "")); b._frameDelay = p.F("frameDelay", 10f); }
            else if (p.Has("file")) b._frames = new[] { m.Texture(p.S("file")) };
            else if (p.B("shadowTexture")) { b._frames = new[] { SpriteTexture(m, "data\\sprite\\shadow") }; b._shadow = true; }
            else if (p.Has("absoluteSpriteName")) b._frames = new[] { SpriteTexture(m, p.S("absoluteSpriteName")) };
            else if (p.Has("spriteName")) b._frames = new[] { SpriteTexture(m, "data\\sprite\\" + RoNames.Effect + "\\" + p.S("spriteName")) };
            if (b._frames == null || b._frames.Length == 0 || b._frames[0] == null) { Destroy(b.gameObject); return null; }
            b._alphaMax = Mathf.Clamp01((p.Has("alphaMax") ? p.F("alphaMax") : 1f) + p.F("alphaMaxDelta") * inst.Dup);
            b._tint = new Color(p.Has("red") ? p.F("red") : 1f, p.Has("green") ? p.F("green") : 1f, p.Has("blue") ? p.F("blue") : 1f, 1f);
            // positions (ThreeDEffect.js:107-244); RO x/y are map x/z here, RO z (height) is y
            float xs = 0, xe = 0, ys = 0, ye = 0, zs = 0, ze = 0;
            Axis(p, "posx", ref xs, ref xe); Axis(p, "posy", ref ys, ref ye); Axis(p, "posz", ref zs, ref ze);
            float xo = p.F("xOffset"), yo = p.F("yOffset"), zo = p.F("zOffset"), zos = p.F("zOffsetStart"), zoe = p.F("zOffsetEnd");
            if (p.B("fromSrc") || p.B("toSrc"))
            {
                var d = inst.OtherPosition - inst.Position;
                float rsx = p.Has("posxStartRand") ? Rand(-p.F("posxStartRand"), p.F("posxStartRand")) : 0, rsy = p.Has("posyStartRand") ? Rand(-p.F("posyStartRand"), p.F("posyStartRand")) : 0, rsz = p.Has("poszStartRand") ? Rand(-p.F("poszStartRand"), p.F("poszStartRand")) : 0;
                float rex = p.Has("posxEndRand") ? Rand(-p.F("posxEndRand"), p.F("posxEndRand")) : 0, rey = p.Has("posyEndRand") ? Rand(-p.F("posyEndRand"), p.F("posyEndRand")) : 0, rez = p.Has("poszEndRand") ? Rand(-p.F("poszEndRand"), p.F("poszEndRand")) : 0;
                if (p.B("fromSrc")) { xs = xo + rsx; xe = d.x + xo + rex; ys = yo + rsy; ye = d.z + yo + rey; zs = zo + zos + rsz; ze = d.y + zo + zoe + rez; }
                else { xs = d.x + xo + rsx; xe = xo + rex; ys = d.z + yo + rsy; ye = yo + rey; zs = d.y + zo + zos + rsz; ze = zo + zoe + rez; }
            }
            b._pStart = new Vector3(xs, zs, ys); b._pEnd = new Vector3(xe, ze, ye);
            b._pxs = p.B("posxSmooth"); b._pys = p.B("posySmooth"); b._pzs = p.B("poszSmooth");
            b._rotPosX = p.F("rotatePosX"); b._rotPosY = p.F("rotatePosY"); b._rotPos = b._rotPosX > 0 || b._rotPosY > 0; b._nbRot = p.F("nbOfRotation") > 0 ? p.F("nbOfRotation") : 1f;
            b._rotLate = Mathf.Max(0, p.F("rotateLate")) + p.F("rotateLateDelta") * inst.Dup; b._cw = p.B("rotationClockwise");
            b._arc = p.F("arc"); b._retreat = p.F("retreat");
            if (p.B("soulStrikePattern") || p.B("drainPattern"))
            {
                int hit = p.B("drainPattern") ? inst.Dup + 1 : Mathf.FloorToInt(inst.Start * 1000f / Mathf.Max(1f, p.F("duration"))) % 5;
                float oa = hit * 72f * Mathf.Deg2Rad; b._pStart.x += Mathf.Cos(oa) * 2f; b._pStart.z += Mathf.Sin(oa) * 2f;
                b._retreat = b._retreat * (1 + hit * 0.2f); b._arc = b._arc * (1 + hit * 0.1f); b._angle += hit * 72f;
            }
            // sizes (sprite pixels; ThreeDEffect.js:246-318)
            float size = p.Has("size") ? p.F("size") : 1f; b._sx0 = b._sy0 = b._sx1 = b._sy1 = size;
            if (p.Has("sizeDelta")) { float dd = p.F("sizeDelta") * inst.Dup; b._sx0 += dd; b._sy0 += dd; b._sx1 += dd; b._sy1 += dd; }
            if (p.Has("sizeStart")) { b._sx0 = b._sy0 = p.F("sizeStart"); } if (p.Has("sizeEnd")) { b._sx1 = b._sy1 = p.F("sizeEnd"); }
            if (p.Has("sizeX")) { b._sx0 = b._sx1 = p.F("sizeX"); } if (p.Has("sizeY")) { b._sy0 = b._sy1 = p.F("sizeY"); }
            if (p.Has("sizeStartX")) b._sx0 = p.F("sizeStartX"); if (p.Has("sizeStartY")) b._sy0 = p.F("sizeStartY"); if (p.Has("sizeEndX")) b._sx1 = p.F("sizeEndX"); if (p.Has("sizeEndY")) b._sy1 = p.F("sizeEndY");
            if (p.Has("sizeRand")) { float s = size + Rand(-p.F("sizeRand"), p.F("sizeRand")); b._sx0 = b._sy0 = b._sx1 = b._sy1 = s; }
            if (p.Has("sizeRandX")) { float mid = p.Has("sizeRandXMiddle") ? p.F("sizeRandXMiddle") : 100f; b._sx0 = b._sx1 = Rand(mid - p.F("sizeRandX"), mid + p.F("sizeRandX")); }
            if (p.Has("sizeRandY")) { float mid = p.Has("sizeRandYMiddle") ? p.F("sizeRandYMiddle") : 100f; b._sy0 = b._sy1 = Rand(mid - p.F("sizeRandY"), mid + p.F("sizeRandY")); }
            if (twoD)
            {
                if (p.Has("sizeRandStartX")) b._sx0 = Rand(p.F("sizeRandStartX") * 0.5f, p.F("sizeRandStartX")); if (p.Has("sizeRandStartY")) b._sy0 = Rand(p.F("sizeRandStartY") * 0.5f, p.F("sizeRandStartY"));
                if (p.Has("sizeRandEndX")) b._sx1 = Rand(p.F("sizeRandEndX") * 0.5f, p.F("sizeRandEndX")); if (p.Has("sizeRandEndY")) b._sy1 = Rand(p.F("sizeRandEndY") * 0.5f, p.F("sizeRandEndY"));
                b._circle = p.B("circlePattern");
                if (b._circle) { float r = Rand(p.F("circleInnerSize"), p.F("circleOuterSizeRand", 1f)); float a = Rand(0, 360) * Mathf.Deg2Rad; b._pStart += new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r); b._pEnd = b._pStart; }
                if (p.Has("angleRand")) b._angle += Rand(0, p.F("angleRand"));
            }
            // a sprite's size is a percentage of its frame, not pixels (rB ThreeDEffect.js:722-725: width = frame.width × layer.scale / 100 × sizeX):
            // without this, a sprite projectile such as ef_arrow_projectile was stretched to a size/35-cell square (client wave 3, w3combat)
            if ((p.Has("absoluteSpriteName") || p.Has("spriteName")) && !p.B("shadowTexture") && list == null && !p.Has("file"))
            {
                float fw = b._frames[0].width / 100f, fh = b._frames[0].height / 100f;
                b._sx0 *= fw; b._sx1 *= fw; b._sy0 *= fh; b._sy1 *= fh;
            }
            b._sizeSmooth = p.B("sizeSmooth");
            b._angle += p.F("angle"); b._toAngle = p.F("toAngle"); b._rotate = p.B("rotate"); b._rotCam = p.B("rotateWithCamera");
            b._baseAngle = b._angle; b._toTarget = p.B("rotateToTarget") && (b._pEnd - b._pStart).sqrMagnitude > 1e-6f;
            if (p.B("rotateToTarget")) { var dd = b._pEnd - b._pStart; b._angle += 90f - Mathf.Atan2(dd.z, dd.x) * Mathf.Rad2Deg; }
            b._fadeIn = p.B("fadeIn"); b._fadeOut = p.B("fadeOut") || (twoD && p.B("fade")); b._sparkle = p.B("sparkling");
            b._sparkN = p.F("sparkNumber") > 0 ? p.F("sparkNumber") : p.L("sparkNumberRand") is List<object> sr && sr.Count > 1 ? Rand((float)MiniJson.Num(sr[0]), (float)MiniJson.Num(sr[1])) : 1f;
            b._dst = EffectManager.PartBlend(p.I("blendMode"));
            b._mesh = Quad(1f, 1f);
            b._mat0 = m.Material(b._frames[0], (int)UnityEngine.Rendering.BlendMode.SrcAlpha, b._dst);
            b._mr = Attach(b.gameObject, b._mesh, b._mat0);
            b.Tick();
            return b;
        }

        private static void Axis(EffectPart p, string k, ref float s, ref float e)
        {
            s = p.F(k + "Start"); e = p.F(k + "End");
            if (p.Has(k)) { s = e = p.F(k); }
            if (p.Has(k + "Rand")) { s = Rand(-p.F(k + "Rand"), p.F(k + "Rand")); e = s; }
            if (p.Has(k + "RandDiff")) { s = Rand(-p.F(k + "RandDiff"), p.F(k + "RandDiff")); e = Rand(-p.F(k + "RandDiff"), p.F(k + "RandDiff")); }
            if (p.Has(k + "StartRand")) { float mid = p.F(k + "StartRandMiddle"); s = Rand(mid - p.F(k + "StartRand"), mid + p.F(k + "StartRand")); }
            if (p.Has(k + "EndRand")) { float mid = p.F(k + "EndRandMiddle"); e = Rand(mid - p.F(k + "EndRand"), mid + p.F(k + "EndRand")); }
        }

        /// <summary>The first frame of an effect sprite as a texture (ThreeDEffect.js:441-447 uses frames[0]).</summary>
        private static readonly Dictionary<string, Texture2D> _sprTex = new Dictionary<string, Texture2D>();
        private static Texture2D SpriteTexture(EffectManager m, string basePath)
        {
            if (_sprTex.TryGetValue(basePath, out var cached)) return cached;
            var spr = m.Assets.SpriteSpr(basePath);
            Texture2D tex = null;
            if (spr != null)
            {
                int type = spr.IndexedFrames.Length > 0 ? 0 : 1;
                var gpu = SpriteFrameCache.Get(spr, 0, type);
                if (gpu != null && gpu.Width > 0 && gpu.Height > 0 && gpu.Pixels != null)
                {
                    tex = new Texture2D(gpu.Width, gpu.Height, TextureFormat.RGBA32, false) { name = "fxspr:" + basePath, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                    tex.SetPixels32(ImageDecoders.FlipRows(gpu.Pixels, gpu.Width, gpu.Height)); tex.Apply(false, false);
                }
            }
            if (tex == null) Debug.LogWarning("[Fx] effect sprite missing: " + basePath);
            _sprTex[basePath] = tex;
            return tex;
        }

        private static float Smooth(float steps, float a, float b) => a == b ? a : Mathf.Log10(steps * 0.09f + 1f) * (b - a) + a;
        private static float Lin(float steps, float a, float b) => a == b ? a : steps * (b - a) / 100f + a;

        private void LateUpdate() { if (OwnerGone) { Finished = true; return; } Tick(); }

        private void Tick()
        {
            float steps = Steps * 100f; float t = T, end = Duration;
            var pos = BasePos;
            float x, y, z;
            if (_rotPosX > 0) { x = _rotPosX * Mathf.Cos((steps * 3.5f * _nbRot) * Mathf.Deg2Rad - _rotLate * Mathf.PI / 2f); if (_cw) x = -x; } else x = _pxs ? Smooth(steps, _pStart.x, _pEnd.x) : Lin(steps, _pStart.x, _pEnd.x);
            if (_rotPosY > 0) y = _rotPosY * Mathf.Sin((steps * 3.5f * _nbRot) * Mathf.Deg2Rad - _rotLate * Mathf.PI / 2f); else y = _pys ? Smooth(steps, _pStart.z, _pEnd.z) : Lin(steps, _pStart.z, _pEnd.z);
            if (_retreat != 0f)
            {
                float lx = Lin(steps, _pStart.x, _pEnd.x), ly = Lin(steps, _pStart.z, _pEnd.z);
                float dx = _pEnd.x - _pStart.x, dy = _pEnd.z - _pStart.z; float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist > 0.001f) { dx /= dist; dy /= dist; float rf = Mathf.Sin(steps * Mathf.PI / 100f) * _retreat; lx -= dx * rf; ly -= dy * rf; }
                x = lx; y = ly;
            }
            z = _pzs ? Smooth(steps, _pStart.y, _pEnd.y) : Lin(steps, _pStart.y, _pEnd.y);
            if (_arc != 0f) z += _arc * Mathf.Sin(steps * Mathf.PI / 100f);
            var local = new Vector3(x, z, y);
            if (_2d) { var yaw = Quaternion.Euler(0, CameraYaw, 0); local = yaw * local; } // 2D offsets are camera-relative (TwoDEffect.js:454-505)
            pos += local;
            if (_shadow) pos.y = Manager.Gat.Height(pos.x - 0.5f, pos.z - 0.5f) + 0.02f;
            transform.localPosition = pos;
            float alpha = _alphaMax;
            if (_fadeIn && end > 0 && t < end / 4) alpha = t * _alphaMax / (end / 4);
            else if (_fadeOut && end > 0 && t > end * 0.75f) alpha = (end - t) * _alphaMax / (end / 4);
            else if (_sparkle) alpha = _alphaMax * (Mathf.Cos(steps * 11f * _sparkN * Mathf.Deg2Rad) + 1f) / 2f;
            alpha = Mathf.Clamp01(alpha);
            float sx = (_sizeSmooth ? Smooth(steps, _sx0, _sx1) : Lin(steps, _sx0, _sx1)) / ActorSprite.PixelsPerCell, sy = (_sizeSmooth ? Smooth(steps, _sy0, _sy1) : Lin(steps, _sy0, _sy1)) / ActorSprite.PixelsPerCell;
            float angle = _rotate ? Lin(steps, _angle, _toAngle) : _angle;
            if (_toTarget && !_rotate && ScreenAngle(out float screenDeg)) angle = _baseAngle + 90f - screenDeg; // the flight as the viewer sees it
            else if (_rotCam) angle += CameraYaw;
            if (_shadow) { transform.localRotation = Quaternion.Euler(90, 0, 0); }
            else { FaceCamera(); transform.rotation *= Quaternion.Euler(0, 0, -angle); }
            transform.localScale = new Vector3(Mathf.Abs(sx), Mathf.Abs(sy), 1f);
            if (_frames.Length > 1 && _frameDelay > 0) { int fi = (int)(t * 1000f / _frameDelay) % _frames.Length; var mat = Manager.Material(_frames[fi], (int)UnityEngine.Rendering.BlendMode.SrcAlpha, _dst); if (_mr.sharedMaterial != mat) _mr.sharedMaterial = mat; }
            (_block ?? (_block = new MaterialPropertyBlock())).SetColor("_Color", new Color(_tint.r, _tint.g, _tint.b, alpha));
            _mr.SetPropertyBlock(_block);
        }
        /// <summary>
        /// rotateToTarget: rB turns the sprite by 90 − atan2 of the flight in its fixed, north-up camera (ThreeDEffect.js:330-337, plus
        /// the camera yaw for rotateWithCamera), which is the flight's angle on screen. In VR the viewer looks from anywhere, so the flight's
        /// start and end are projected into the head camera and their on-screen angle is used (client wave 3, w3combat: an arrow seen from
        /// the side was drawn some 60° off its path). False when the camera or a point is behind it.
        /// </summary>
        private bool ScreenAngle(out float deg)
        {
            deg = 0f; var cam = Cam; if (cam == null || Manager == null) return false;
            var root = Manager.FxRoot; var bp = BasePos;
            var a = cam.transform.InverseTransformPoint(root.TransformPoint(bp + _pStart));
            var b = cam.transform.InverseTransformPoint(root.TransformPoint(bp + _pEnd));
            if (a.z <= 1e-4f || b.z <= 1e-4f) return false;
            float dx = b.x / b.z - a.x / a.z, dy = b.y / b.z - a.y / a.z;
            if (dx * dx + dy * dy < 1e-10f) return false;
            deg = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
            return true;
        }

        private void OnDestroy() { if (_mesh != null) Destroy(_mesh); }
    }

    /// <summary>SPR parts (spamSprite / EntityAttachments.js): an effect sprite (data\sprite\이팩트\file.act) played over the owner, `head` = 100 px up.</summary>
    public sealed class SprFx : FxRenderer
    {
        private SprFile _spr; private ActFile _act; private bool _head, _dir, _repeat, _stopAtEnd; private int _actionBase; private float _frameMs; private GameObject _quad; private MeshFilter _mf; private Material _mat; private string _lastKey;
        private readonly List<SpriteFrameCache.CompositeLayer> _layers = new List<SpriteFrameCache.CompositeLayer>();
        public override bool EndsAtEndTick => Inst != null && Inst.End > 0;

        public static SprFx Create(EffectManager m, EffectManager.Instance inst)
        {
            var p = inst.Part; string file = p.S("file"); if (string.IsNullOrEmpty(file)) return null;
            if (!m.Assets.Sprite("data\\sprite\\" + RoNames.Effect + "\\" + file, out var spr, out var act) || act.Actions.Length == 0) return null;
            var s = Make<SprFx>(m, inst, "spr:" + file);
            // EntityAttachments.add: `direction` is false whenever the options carry a `frame` key — spamSprite always passes one, so spammed sprites never turn
            s._spr = spr; s._act = act; s._head = p.B("head"); s._dir = false; s._repeat = p.B("repeat") || inst.Persistent; s._stopAtEnd = p.B("stopAtEnd");
            s._frameMs = p.Has("delayFrame") ? p.F("delayFrame") : 0f;
            s._quad = new GameObject("q"); s._quad.transform.SetParent(s.transform, false);
            s._mf = s._quad.AddComponent<MeshFilter>();
            var mr = s._quad.AddComponent<MeshRenderer>(); s._mat = new Material(Shader.Find("Loki/Sprite")); s._mat.SetFloat("_Relief", 0.1f); mr.sharedMaterial = s._mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            if (inst.Owner == null && !inst.Attached) { /* detached sprite at the position: the base object already sits there */ }
            return s;
        }

        private void LateUpdate()
        {
            if (OwnerGone) { Finished = true; return; }
            transform.localPosition = BasePos;
            var cam = Cam; if (cam != null) { var to = transform.position - cam.transform.position; to.y = 0; if (to.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up); }
            int action = 0;
            if (_dir && Inst.Owner != null && Inst.Owner.Sprite != null) action = ((Inst.Owner.Sprite.Direction + ActorSprite.CameraDirection) & 7) % _act.Actions.Length;
            var a = _act.Actions[action];
            if (a.Frames.Length == 0) { Finished = true; return; }
            float frameMs = _frameMs > 0 ? _frameMs : Mathf.Max(a.Delay, 1f) * 25f;
            int f = (int)(T * 1000f / frameMs);
            if (f >= a.Frames.Length) { if (_repeat) f %= a.Frames.Length; else if (_stopAtEnd) f = a.Frames.Length - 1; else { Finished = true; return; } }
            var frame = a.Frames[f];
            _layers.Clear();
            var kb = new System.Text.StringBuilder("fx:" + _spr.GetHashCode() + ":" + action + ":" + f + "|");
            foreach (var layer in frame.Layers)
            {
                if (layer.SpriteNumber < 0) continue;
                int available = layer.SpriteType == 1 ? _spr.RgbaFrames.Length : _spr.IndexedFrames.Length;
                if (layer.SpriteNumber >= available || layer.Color.a == 0 || layer.Scale.x == 0f || layer.Scale.y == 0f) continue;
                var gpu = SpriteFrameCache.Get(_spr, layer.SpriteNumber, layer.SpriteType);
                if (gpu.Width == 0 || gpu.Height == 0) continue;
                _layers.Add(new SpriteFrameCache.CompositeLayer { Frame = gpu, X = layer.X, Y = layer.Y, Mirror = layer.Mirror, Color = layer.Color, ScaleX = layer.Scale.x, ScaleY = layer.Scale.y, Angle = layer.Angle });
            }
            string key = kb.ToString();
            if (_layers.Count == 0) { _quad.SetActive(false); return; }
            if (key != _lastKey)
            {
                _lastKey = key;
                var comp = SpriteFrameCache.GetComposite(key, _layers, out var center);
                if (comp == null) { _quad.SetActive(false); return; }
                _mf.sharedMesh = comp.Mesh; _mat.mainTexture = comp.Texture;
                float sc = 1f / ActorSprite.PixelsPerCell;
                // EntityAttachments.js:60-69, 297-303: xOffset/yOffset REPLACE the head offset (-100 px) when given; pixel y grows downwards
                float xo = Inst.Part.F("xOffset"), yo = Inst.Part.F("yOffset");
                float originX = xo, originY = (xo != 0 || yo != 0) ? -yo : (_head ? 100f : 0f);
                _quad.transform.localPosition = new Vector3((center.x + originX) * sc, (originY - center.y) * sc, -0.02f);
                _quad.transform.localScale = new Vector3(sc, sc, sc);
            }
            _quad.SetActive(true);
        }
        private void OnDestroy() { if (_mat != null) Destroy(_mat); }
    }

    /// <summary>QuadHorn parts (QuadHorn.js): a four-sided pyramid (ice / stone spikes) rising from the ground, with the emerge animations and fades.</summary>
    public sealed class QuadHornFx : FxRenderer
    {
        private Mesh _mesh; private MeshRenderer _mr; private MaterialPropertyBlock _block; private float _height, _bottom, _ox, _oy, _oz, _rx, _ry, _rz, _animSpeed, _rise, _fadeOut, _animOut; private int _anim; private Color _color;

        public static QuadHornFx Create(EffectManager m, EffectManager.Instance inst)
        {
            var p = inst.Part; string tex = p.S("textureFile"); if (string.IsNullOrEmpty(tex)) return null;
            var t2d = m.Texture(tex); if (t2d == null) return null; // not in this GRF (w4fx)
            var q = Make<QuadHornFx>(m, inst, "horn:" + tex);
            q._height = p.R("height", 1f); q._bottom = p.R("bottomSize", 0.5f); q._ox = p.R("offsetX"); q._oy = p.R("offsetY"); q._oz = p.R("offsetZ");
            q._rx = p.R("rotateX"); q._ry = p.R("rotateY"); q._rz = p.R("rotateZ") + 180f; q._anim = p.I("animation"); q._animSpeed = p.R("animationSpeed", 300f); q._rise = p.R("riseDistance", 2f); q._fadeOut = p.R("fadeOut"); q._animOut = p.R("animationOut");
            var col = p.L("color"); q._color = col != null && col.Count >= 4 ? new Color((float)MiniJson.Num(col[0]), (float)MiniJson.Num(col[1]), (float)MiniJson.Num(col[2]), (float)MiniJson.Num(col[3])) : Color.white;
            // pyramid: apex (0,1,0), base corners (±1, 0, ±1) (QuadHorn.js:19-37)
            q._mesh = new Mesh();
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>();
            Vector3[] b = { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(1, 0, 1), new Vector3(-1, 0, 1) };
            for (int i = 0; i < 4; i++)
            {
                int n = v.Count; v.Add(b[i]); v.Add(b[(i + 1) % 4]); v.Add(new Vector3(0, 1, 0)); uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(0.5f, 1));
                tr.Add(n); tr.Add(n + 2); tr.Add(n + 1); tr.Add(n); tr.Add(n + 1); tr.Add(n + 2);
            }
            q._mesh.vertices = v.ToArray(); q._mesh.uv = uv.ToArray(); q._mesh.triangles = tr.ToArray(); q._mesh.RecalculateBounds();
            q._mr = Attach(q.gameObject, q._mesh, m.Material(t2d, (int)UnityEngine.Rendering.BlendMode.SrcAlpha, EffectManager.PartBlend(p.I("blendMode"))));
            q.Tick();
            return q;
        }
        private void LateUpdate() { if (OwnerGone) { Finished = true; return; } Tick(); }
        private void Tick()
        {
            float t = T * 1000f, dur = Duration * 1000f;
            float h = _height, oz = _oz; float alpha = _color.a;
            float k = Mathf.Clamp01(_animSpeed > 0 ? t / _animSpeed : 1f);
            switch (_anim)
            {
                case 1: h = _height * k; break;
                case 2: oz = _oz * k; break;
                case 3: oz = _oz + _height / 2f * k; break;
                case 4: { float e = 1f - (1f - k) * (1f - k); oz = _oz - _rise * (1f - e); break; }
            }
            if (_fadeOut > 0 && dur > 0 && t > dur - _fadeOut) alpha *= Mathf.Clamp01((dur - t) / _fadeOut);
            if (_animOut > 0 && dur > 0 && t > dur) { float e = Mathf.Clamp01((t - dur) / _animOut); oz -= _rise * e; if (e >= 1f) Finished = true; }
            transform.localPosition = BasePos + new Vector3(_ox, oz, _oy);
            transform.localRotation = Quaternion.Euler(_rx, _ry, _rz);
            transform.localScale = new Vector3(_bottom, h, _bottom);
            (_block ?? (_block = new MaterialPropertyBlock())).SetColor("_Color", new Color(_color.r, _color.g, _color.b, alpha));
            _mr.SetPropertyBlock(_block);
        }
        private void OnDestroy() { if (_mesh != null) Destroy(_mesh); }
    }

    /// <summary>The FUNC parts worth reproducing (research §3): EF_LOCKON 60 (spinning lockon128 ground quad on the target) and EF_GROUNDSAMPLE 513 (magic_target
    /// plane on the cast cell). Camera quakes, screen tints and the aura FUNCs are skipped on purpose (VR comfort / not needed).</summary>
    public sealed class FuncFx : FxRenderer
    {
        private MeshRenderer _mr; private Mesh _mesh; private MaterialPropertyBlock _block; private int _kind;

        public static bool Handles(string key) => int.TryParse(key, out var n) && (n == EffectManager.EF_LOCKON || n == EffectManager.EF_GROUNDSAMPLE);
        public static FuncFx Create(EffectManager m, EffectManager.Instance inst)
        {
            if (!Handles(inst.Key)) return null;
            int id = int.Parse(inst.Key);
            var f = Make<FuncFx>(m, inst, "func:" + inst.Key);
            f._kind = id;
            f._mesh = Quad(1f, 1f);
            var tex = m.Texture(id == EffectManager.EF_LOCKON ? "effect\\lockon128.tga" : "effect\\magic_target.tga");
            f._mr = Attach(f.gameObject, f._mesh, m.Material(tex, (int)UnityEngine.Rendering.BlendMode.SrcAlpha, (int)UnityEngine.Rendering.BlendMode.One));
            f.Tick();
            return f;
        }
        private void LateUpdate() { if (OwnerGone) { Finished = true; return; } Tick(); }
        private void Tick()
        {
            var pos = BasePos; pos.y += 0.03f;
            transform.localPosition = pos;
            float t = T * 1000f;
            if (_kind == EffectManager.EF_LOCKON)
            {
                float size = (6f - Mathf.Clamp(t / 50f, 1f, 5f)) * 3f; // 15 → 3 cells over the first 250 ms (LockOnTarget.js:83-91)
                transform.localRotation = Quaternion.Euler(90, 45f + t / 4f, 0);
                transform.localScale = new Vector3(size, size, 1);
                float flick = (20f - (t / 20f % 20f)) / 20f;
                (_block ?? (_block = new MaterialPropertyBlock())).SetColor("_Color", new Color(1f, flick, flick, 1f));
            }
            else
            {
                float size = 3f;
                transform.localRotation = Quaternion.Euler(90, t / 25f, 0); // 40°/s (MagicTarget.js:255-256)
                transform.localScale = new Vector3(size, size, 1);
                (_block ?? (_block = new MaterialPropertyBlock())).SetColor("_Color", Color.white);
            }
            _mr.SetPropertyBlock(_block);
        }
        private void OnDestroy() { if (_mesh != null) Destroy(_mesh); }
    }
}
