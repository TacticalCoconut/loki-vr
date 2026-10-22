using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 21 (w21inv), the design's point 62 (the new inventory: "a curved panel that sits in front of you"): the CURVE, made in code.
    /// The inventory is a strip on a cylinder whose axis stands <see cref="Radius"/> px in front of the panel's middle (towards the
    /// eyes: the panel's -z), upright in the panel's own frame. A place on the strip is (x along the arc, y up), both in the panel's
    /// pixels. Flat things (a column of cells, a header) are their own small rectangles touching the cylinder (<see cref="Place"/>: as
    /// the wide Character board's wings, UI/MenuBoard.cs MakeWing); the frame, the canvas and the stitching are drawn by Graphics whose
    /// vertices are put on the cylinder (<see cref="BentImage"/>, <see cref="BentStitch"/>), from the kit's own pictures (UI/Theme.cs).
    /// </summary>
    public static class Bend
    {
        /// <summary>The arc place (x, y), <paramref name="off"/> px nearer the axis (the eyes), in the panel's own frame.</summary>
        public static Vector3 Point(float radius, float x, float y, float off = 0f)
        {
            float a = x / radius, r = radius - off;
            return new Vector3(r * Mathf.Sin(a), y, r * Mathf.Cos(a) - radius);
        }
        public static Quaternion Turn(float radius, float x) => Quaternion.Euler(0f, x / radius * Mathf.Rad2Deg, 0f);
        /// <summary>Stands a flat rectangle on the cylinder: its middle at the arc place, facing the axis.</summary>
        public static void Place(RectTransform t, float radius, float x, float y, float off = 0f)
        {
            t.anchorMin = t.anchorMax = new Vector2(0.5f, 0.5f); t.pivot = new Vector2(0.5f, 0.5f);
            t.localPosition = Point(radius, x, y, off); t.localRotation = Turn(radius, x);
        }
        /// <summary>A point of the panel's frame as a place on the strip: (x along the arc, y, how far off the cylinder: negative =
        /// nearer the axis, as a flat panel's z).</summary>
        public static Vector3 Unbend(float radius, Vector3 local)
        {
            float zz = local.z + radius, r = Mathf.Sqrt(local.x * local.x + zz * zz);
            return new Vector3(Mathf.Atan2(local.x, zz) * radius, local.y, r - radius);
        }
    }

    /// <summary>Client wave 21 (w21inv): marks a panel whose picture is bent on a cylinder (the new inventory under the menu board's
    /// root): the edge a hand or the laser takes it by is the bent strip's (UI/PanelGrab.cs Rim).</summary>
    public sealed class UiBend : MonoBehaviour
    {
        /// <summary>The cylinder's radius in the panel's px (0: flat now).</summary>
        public float Radius { get; set; }
        public bool On => enabled && Radius > 1f;
        private float _x0 = float.NaN, _x1;
        /// <summary>The part of the strip that is there now (arc px: the bundle, or what has unrolled), where it is not the whole rectangle.</summary>
        public bool HasSpan => !float.IsNaN(_x0);
        public float SpanMid => (_x0 + _x1) * 0.5f;
        public float SpanHalf => (_x1 - _x0) * 0.5f;
        public void SetSpan(float x0, float x1) { _x0 = x0; _x1 = x1; }
        public void ClearSpan() { _x0 = float.NaN; }
        private Matrix4x4 _draw = Matrix4x4.identity, _drawInv = Matrix4x4.identity; private float _scale = 1f;
        /// <summary>Where the strip's drawing stands in the panel's own frame (the bundle out of the backpack is small in the hand and
        /// grows as it springs open; client wave 23, w23inv: it is never turned any more: the equipment panel no longer turns the
        /// drawing down, so <paramref name="rot"/> is the identity): its place, turn, size.</summary>
        public void SetDraw(Vector3 pos, Quaternion rot, float scale)
        {
            _scale = Mathf.Max(0.01f, scale); _draw = Matrix4x4.TRS(pos, rot, Vector3.one * _scale); _drawInv = _draw.inverse;
        }
        public float Shrink => _scale;
        /// <summary>The strip's arc place (x, y) in the panel's own frame, as it is drawn now.</summary>
        public Vector3 Local(float x, float y, float off = 0f) => _draw.MultiplyPoint3x4(Bend.Point(Radius, x, y, off));
        /// <summary>A point of the panel's frame as a place on the strip as it is drawn now (x along the arc, y, off the cylinder).</summary>
        public Vector3 Place(Vector3 local) => Bend.Unbend(Radius, _drawInv.MultiplyPoint3x4(local));
        /// <summary>Where a ray of the panel's own frame meets the strip's cylinder as it is drawn now: how many times (0-2) and at
        /// which distances along the ray, the nearer first (the ray's own parameter: metres for a world ray brought into the frame).</summary>
        public int Raycast(Vector3 origin, Vector3 dir, out float near, out float far)
        {
            near = far = 0f;
            // in the drawing's frame the cylinder is x^2 + (z + R)^2 = R^2, its axis along y
            var o = _drawInv.MultiplyPoint3x4(origin); var d = _drawInv.MultiplyVector(dir);
            float ox = o.x, oz = o.z + Radius, a = d.x * d.x + d.z * d.z, b = 2f * (ox * d.x + oz * d.z), c = ox * ox + oz * oz - Radius * Radius;
            if (a < 1e-12f) return 0;
            float disc = b * b - 4f * a * c; if (disc < 0f) return 0;
            float sq = Mathf.Sqrt(disc); near = (-b - sq) / (2f * a); far = (-b + sq) / (2f * a);
            if (near > 0f) return 2;
            near = far; return far > 0f ? 1 : 0;
        }
    }

    /// <summary>A 9-slice picture of the theme drawn BENT on the cylinder: the rectangle [X0, X1] x [Y0, Y1] of the strip (arc px), its
    /// borders <see cref="Unit"/> panel px per design px, standing <see cref="Outset"/> px out of it (as UI/Theme.cs OutsetImage), cut
    /// into pieces of at most <see cref="Seg"/> px along the arc.</summary>
    public sealed class BentImage : MaskableGraphic
    {
        private Sprite _sprite; private float _unit = 1f, _radius = 900f, _outset, _x0, _x1, _y0, _y1; private bool _fill = true;
        public const float Seg = 26f;
        public Sprite Sprite { get => _sprite; set { _sprite = value; SetAllDirty(); } }
        public float Unit { get => _unit; set { _unit = Mathf.Max(0.01f, value); SetVerticesDirty(); } }
        public float Radius { get => _radius; set { _radius = Mathf.Max(10f, value); SetVerticesDirty(); } }
        public float Outset { get => _outset; set { _outset = value; SetVerticesDirty(); } }
        public bool FillCenter { get => _fill; set { _fill = value; SetVerticesDirty(); } }
        public float X0 => _x0; public float X1 => _x1; public float Y0 => _y0; public float Y1 => _y1;
        public override Texture mainTexture => _sprite != null ? _sprite.texture : s_WhiteTexture;
        /// <summary>Quads drawn at the last rebuild (smoke).</summary>
        public int Quads { get; private set; }
        public void SetRect(float x0, float x1, float y0, float y1)
        {
            if (x0 == _x0 && x1 == _x1 && y0 == _y0 && y1 == _y1) return;
            _x0 = x0; _x1 = x1; _y0 = y0; _y1 = y1; SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); Quads = 0;
            if (_sprite == null || _x1 - _x0 < 0.5f || _y1 - _y0 < 0.5f) return;
            var b = _sprite.border; float tw = _sprite.rect.width, th = _sprite.rect.height;
            float bl = b.x * _unit, bb = b.y * _unit, br = b.z * _unit, bt = b.w * _unit;
            float x0 = _x0 - _outset, x1 = _x1 + _outset, y0 = _y0 - _outset, y1 = _y1 + _outset;
            float w = x1 - x0, h = y1 - y0;
            if (bl + br > w) { float k = w / (bl + br); bl *= k; br *= k; }
            if (bb + bt > h) { float k = h / (bb + bt); bb *= k; bt *= k; }
            float[] xs = { x0, x0 + bl, x1 - br, x1 }, us = { 0f, b.x / tw, 1f - b.z / tw, 1f };
            float[] ys = { y0, y0 + bb, y1 - bt, y1 }, vs = { 0f, b.y / th, 1f - b.w / th, 1f };
            Color32 c = color;
            for (int cx = 0; cx < 3; cx++)
            {
                float xa = xs[cx], xb = xs[cx + 1]; if (xb - xa < 0.01f) continue;
                int n = Mathf.Max(1, Mathf.CeilToInt((xb - xa) / Seg));
                for (int cy = 0; cy < 3; cy++)
                {
                    if (cx == 1 && cy == 1 && !_fill) continue;
                    float ya = ys[cy], yb = ys[cy + 1]; if (yb - ya < 0.01f) continue;
                    for (int i = 0; i < n; i++)
                    {
                        float t0 = (float)i / n, t1 = (float)(i + 1) / n;
                        float xl = Mathf.Lerp(xa, xb, t0), xr = Mathf.Lerp(xa, xb, t1);
                        float ul = Mathf.Lerp(us[cx], us[cx + 1], t0), ur = Mathf.Lerp(us[cx], us[cx + 1], t1);
                        int at = vh.currentVertCount;
                        vh.AddVert(Bend.Point(_radius, xl, ya), c, new Vector2(ul, vs[cy])); vh.AddVert(Bend.Point(_radius, xl, yb), c, new Vector2(ul, vs[cy + 1]));
                        vh.AddVert(Bend.Point(_radius, xr, yb), c, new Vector2(ur, vs[cy + 1])); vh.AddVert(Bend.Point(_radius, xr, ya), c, new Vector2(ur, vs[cy]));
                        vh.AddTriangle(at, at + 1, at + 2); vh.AddTriangle(at, at + 2, at + 3);
                        Quads++;
                    }
                }
            }
        }
    }

    /// <summary>Dashed stitching drawn BENT on the cylinder: rectangles (a pocket's seam) and lines given in the strip's arc px; a
    /// stitch whose middle lies outside [ClipX0, ClipX1] is not drawn (the canvas not unrolled there yet).</summary>
    public sealed class BentStitch : MaskableGraphic
    {
        private readonly List<Vector4> _runs = new List<Vector4>(); // (ax, ay, bx, by)
        private float _radius = 900f, _dash = 9f, _gap = 6f, _wide = 2f, _c0 = float.NegativeInfinity, _c1 = float.PositiveInfinity;
        public float Radius { get => _radius; set { _radius = Mathf.Max(10f, value); SetVerticesDirty(); } }
        public float Dash { get => _dash; set { _dash = Mathf.Max(1f, value); SetVerticesDirty(); } }
        public float Gap { get => _gap; set { _gap = Mathf.Max(0.5f, value); SetVerticesDirty(); } }
        public float Wide { get => _wide; set { _wide = Mathf.Max(0.5f, value); SetVerticesDirty(); } }
        public int Stitches { get; private set; }
        public void Clear() { _runs.Clear(); SetVerticesDirty(); }
        public void AddLine(Vector2 a, Vector2 b) { _runs.Add(new Vector4(a.x, a.y, b.x, b.y)); SetVerticesDirty(); }
        /// <summary>A seam round a rectangle, its corners left open (a stitch never bends).</summary>
        public void AddRect(float x0, float x1, float y0, float y1)
        {
            float c = _dash * 0.8f;
            AddLine(new Vector2(x0 + c, y1), new Vector2(x1 - c, y1)); AddLine(new Vector2(x0 + c, y0), new Vector2(x1 - c, y0));
            AddLine(new Vector2(x0, y0 + c), new Vector2(x0, y1 - c)); AddLine(new Vector2(x1, y0 + c), new Vector2(x1, y1 - c));
        }
        public void Clip(float x0, float x1) { if (x0 == _c0 && x1 == _c1) return; _c0 = x0; _c1 = x1; SetVerticesDirty(); }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); Stitches = 0;
            Color32 col = color;
            foreach (var run in _runs)
            {
                Vector2 a = new Vector2(run.x, run.y), b = new Vector2(run.z, run.w);
                float len = Vector2.Distance(a, b); if (len < _dash * 0.5f) continue;
                int n = Mathf.Clamp(Mathf.FloorToInt((len + _gap) / (_dash + _gap)), 1, 600);
                float gap = n > 1 ? (len - n * _dash) / (n - 1) : 0f;
                Vector2 dir = (b - a) / len, nrm = new Vector2(-dir.y, dir.x) * (_wide * 0.5f);
                for (int i = 0; i < n; i++)
                {
                    Vector2 s = a + dir * (i * (_dash + gap)), e = s + dir * Mathf.Min(_dash, len);
                    float mid = (s.x + e.x) * 0.5f; if (mid < _c0 || mid > _c1) continue;
                    int at = vh.currentVertCount;
                    vh.AddVert(P(s - nrm), col, Vector2.zero); vh.AddVert(P(s + nrm), col, Vector2.zero); vh.AddVert(P(e + nrm), col, Vector2.zero); vh.AddVert(P(e - nrm), col, Vector2.zero);
                    vh.AddTriangle(at, at + 1, at + 2); vh.AddTriangle(at, at + 2, at + 3);
                    Stitches++;
                }
            }
        }
        private Vector3 P(Vector2 p) => Bend.Point(_radius, p.x, p.y);
    }

    /// <summary>The rolled end of the canvas: an upright half cylinder (the half that faces the eyes) with its two straps and buckles,
    /// its picture drawn in code (the theme's colours: UI/Theme.cs). Stands on its own small rectangle (Bend.Place); <see cref="R"/> is
    /// its radius in px, <see cref="Height"/> its height; <see cref="Mirror"/>: the seam on the other side (the left roll of the
    /// compact bundle).</summary>
    public sealed class RollGraphic : MaskableGraphic
    {
        private float _r = 60f, _h = 800f; private bool _mirror;
        public float R { get => _r; set { if (!Mathf.Approximately(_r, value)) { _r = Mathf.Max(1f, value); SetVerticesDirty(); } } }
        public float Height { get => _h; set { if (!Mathf.Approximately(_h, value)) { _h = value; SetVerticesDirty(); } } }
        public bool Mirror { get => _mirror; set { if (_mirror != value) { _mirror = value; SetVerticesDirty(); } } }
        public override Texture mainTexture => Picture;
        public const int Segs = 14;
        /// <summary>The straps' places along the roll (share of its height from the bottom) and their height.</summary>
        public const float StrapA = 0.27f, StrapB = 0.73f, StrapH = 0.07f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Color32 c = color; float hy = _h * 0.5f;
            // the two ends first (drawn under the wall: seen only from above the top or from under the bottom)
            for (int end = 0; end < 2; end++)
            {
                float y = end == 0 ? hy : -hy; int at = vh.currentVertCount;
                vh.AddVert(new Vector3(0, y, 0), c, CapUv(0, 0));
                for (int i = 0; i <= 20; i++) { float a = i / 20f * Mathf.PI * 2f; vh.AddVert(new Vector3(_r * Mathf.Sin(a), y, -_r * Mathf.Cos(a)), c, CapUv(Mathf.Sin(a), Mathf.Cos(a))); }
                for (int i = 0; i < 20; i++) vh.AddTriangle(at, at + 1 + i, at + 2 + i);
            }
            // the wall: the half towards the eyes (-z), from the left edge to the right edge
            int w0 = vh.currentVertCount;
            for (int i = 0; i <= Segs; i++)
            {
                float t = (float)i / Segs, phi = Mathf.Lerp(-88f, 88f, t) * Mathf.Deg2Rad, u = _mirror ? 1f - t : t;
                var p = new Vector3(_r * Mathf.Sin(phi), 0f, -_r * Mathf.Cos(phi));
                vh.AddVert(p + Vector3.down * hy, c, new Vector2(u, 0f)); vh.AddVert(p + Vector3.up * hy, c, new Vector2(u, WallV));
            }
            for (int i = 0; i < Segs; i++) { int a = w0 + i * 2; vh.AddTriangle(a, a + 1, a + 3); vh.AddTriangle(a, a + 3, a + 2); }
        }

        // ---- the picture: TexW x (WallPx + CapPx): the wall below, the end's spiral in a square above it
        private const int TexW = 96, WallPx = 384, CapPx = 48;
        private static float WallV => (float)WallPx / (WallPx + CapPx);
        private static Vector2 CapUv(float sx, float cz) => new Vector2((0.5f + sx * 0.48f) * CapPx / TexW, (WallPx + (0.5f + cz * 0.48f) * CapPx) / (WallPx + CapPx));
        private static Texture2D _tex;
        public static Texture2D Picture => _tex != null ? _tex : (_tex = MakePicture());

        private static Color Mix(Color a, Color b, float t) { t = Mathf.Clamp01(t); return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, 1f); }
        private static float Cov(float d) => Mathf.Clamp01(0.5f - d);

        private static Texture2D MakePicture()
        {
            int H = WallPx + CapPx; var px = new Color[TexW * H];
            for (int y = 0; y < WallPx; y++)
                for (int x = 0; x < TexW; x++)
                {
                    float u = (x + 0.5f) / TexW, v = (y + 0.5f) / WallPx;
                    Color c = Theme.Canvas;
                    // the canvas' own leather edge runs round the roll at its two ends; a seam of stitches beside it
                    float dEnd = Mathf.Min(v, 1f - v) * WallPx;
                    if (dEnd < 13f) c = Mix(Mix(Theme.Leather, Theme.LeatherLight, 0.5f * Mathf.Exp(-(dEnd - 5f) * (dEnd - 5f) / 8f)), Theme.LeatherDark, Mathf.SmoothStep(0f, 1f, (dEnd - 9f) / 4f) * 0.8f);
                    else if (Mathf.Abs(dEnd - 20f) < 1.2f && (x % 9) < 5) c = Mix(c, Theme.Leather, 0.85f);
                    // the flap: where the rolled canvas ends, its leather edge upright on the roll, a stitched seam beside it
                    float dFlap = (u - 0.66f) * TexW;
                    if (dEnd >= 13f)
                    {
                        if (dFlap > 0f && dFlap < 8f) c = Mix(Theme.Leather, Theme.LeatherDark, dFlap < 1.5f ? 0.7f : dFlap > 6.5f ? 0.6f : 0f);
                        else if (dFlap <= 0f && dFlap > -3f) c = Mix(c, Theme.WoodDark, 0.28f); // its shadow on the layer under it
                        else if (Mathf.Abs(dFlap - 13f) < 1.1f && (y % 9) < 5) c = Mix(c, Theme.Leather, 0.85f);
                    }
                    // the two straps, a buckle on each
                    foreach (float sv in new[] { StrapA, StrapB })
                    {
                        float dv = Mathf.Abs(v - sv) * WallPx, half = StrapH * WallPx * 0.5f;
                        if (dv < half)
                        {
                            c = Mix(Theme.Leather, Theme.LeatherLight, 0.35f * Mathf.Exp(-dv * dv / 30f));
                            c = Mix(c, Theme.LeatherDark, Mathf.SmoothStep(0f, 1f, (dv - half + 3.5f) / 3.5f) * 0.9f);
                            if (Mathf.Abs(dv - (half - 5.5f)) < 0.8f && (x % 8) < 4) c = Mix(c, Theme.Parchment, 0.55f); // its seam
                        }
                        else if (dv < half + 2.5f) c = Mix(c, Theme.WoodDark, 0.3f);
                        // the buckle: a gold frame a little taller than the strap, its prong across
                        float bx = Mathf.Abs(u - 0.40f) * TexW, by = dv;
                        float box = Mathf.Max(bx - 9f, by - (half + 5f));
                        float frame = Cov(Mathf.Abs(box + 1.8f) - 1.8f);
                        float prong = bx < 9f ? Cov(by - 1.3f) : 0f;
                        float gold = Mathf.Max(frame, prong);
                        if (gold > 0f) c = Mix(c, Mix(Theme.Gold, Theme.GoldLight, v > sv ? 0.5f : 0f), gold);
                        else if (box < 2.2f && box > 0f) c = Mix(c, Theme.WoodDark, 0.35f);
                    }
                    // the round of the roll: light a little left of its middle, dark towards both edges
                    float phi = (u - 0.5f) * Mathf.PI, shade = 0.56f + 0.44f * Mathf.Clamp01(Mathf.Cos(phi + 0.30f));
                    shade *= 1f - 0.22f * Mathf.SmoothStep(0.80f, 1f, Mathf.Abs(u - 0.5f) * 2f);
                    c = new Color(c.r * shade, c.g * shade, c.b * shade, 1f);
                    px[y * TexW + x] = c;
                }
            // the end: the canvas wound in a spiral, its leather edge outside
            for (int y = 0; y < CapPx; y++)
                for (int x = 0; x < TexW; x++)
                {
                    Color c = Theme.CanvasShade;
                    if (x < CapPx)
                    {
                        float fx = x + 0.5f - CapPx / 2f, fy = y + 0.5f - CapPx / 2f, rho = Mathf.Sqrt(fx * fx + fy * fy), ang = Mathf.Atan2(fy, fx);
                        float turn = rho - ang / (2f * Mathf.PI) * 4.2f; float band = Mathf.Repeat(turn, 4.2f);
                        c = Mix(Theme.Canvas, Theme.Leather, band < 1.3f ? 0.75f : 0f);
                        if (rho > CapPx / 2f - 3.2f) c = Theme.Leather;
                        c = new Color(c.r * 0.9f, c.g * 0.9f, c.b * 0.9f, 1f);
                    }
                    px[(WallPx + y) * TexW + x] = c;
                }
            var tex = new Texture2D(TexW, H, TextureFormat.RGBA32, true) { name = "LokiInv:roll", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4, hideFlags = HideFlags.DontSave };
            tex.SetPixels(px); tex.Apply(true, false);
            return tex;
        }
    }

    /// <summary>The inventory's line pictures, drawn in code as strokes (white: tinted with the theme's ink): one per section (a helmet,
    /// a sword, a flask, cards, arrows, a pouch) and the two round buttons' (sort: two arrows; filter: a funnel).</summary>
    public static class InvIcons
    {
        public const int N = 96;
        private static readonly Dictionary<string, Sprite> Made = new Dictionary<string, Sprite>();
        public static readonly string[] Names = { "helmet", "sword", "flask", "cards", "arrows", "pouch", "sort", "filter", "pin" };

        public static Sprite Get(string name)
        {
            if (Made.TryGetValue(name, out var s) && s != null) return s;
            var segs = new List<Vector4>(); float half = 0.026f;
            switch (name)
            {
                case "helmet":
                    Arc(segs, 0.5f, 0.46f, 0.30f, 0f, 180f); Line(segs, 0.15f, 0.46f, 0.85f, 0.46f);
                    Poly(segs, false, 0.21f, 0.46f, 0.24f, 0.20f, 0.37f, 0.15f, 0.40f, 0.36f); Poly(segs, false, 0.79f, 0.46f, 0.76f, 0.20f, 0.63f, 0.15f, 0.60f, 0.36f);
                    Line(segs, 0.5f, 0.46f, 0.5f, 0.22f); Line(segs, 0.5f, 0.76f, 0.5f, 0.87f); Arc(segs, 0.5f, 0.46f, 0.20f, 35f, 145f);
                    break;
                case "sword":
                    Poly(segs, true, 0.31f, 0.39f, 0.74f, 0.82f, 0.87f, 0.87f, 0.82f, 0.74f, 0.39f, 0.31f);
                    Line(segs, 0.21f, 0.47f, 0.47f, 0.21f); Line(segs, 0.33f, 0.33f, 0.20f, 0.20f); Arc(segs, 0.165f, 0.165f, 0.045f, 0f, 360f);
                    Line(segs, 0.40f, 0.40f, 0.76f, 0.76f);
                    break;
                case "flask":
                    Arc(segs, 0.5f, 0.37f, 0.25f, 112f, 428f); Line(segs, 0.405f, 0.60f, 0.405f, 0.77f); Line(segs, 0.595f, 0.60f, 0.595f, 0.77f);
                    Line(segs, 0.36f, 0.79f, 0.64f, 0.79f); Poly(segs, false, 0.27f, 0.40f, 0.38f, 0.44f, 0.50f, 0.39f, 0.62f, 0.44f, 0.73f, 0.40f);
                    Arc(segs, 0.57f, 0.27f, 0.035f, 0f, 360f);
                    break;
                case "cards":
                    Poly(segs, true, 0.44f, 0.16f, 0.44f, 0.72f, 0.82f, 0.72f, 0.82f, 0.16f);
                    Poly(segs, false, 0.44f, 0.26f, 0.22f, 0.33f, 0.33f, 0.84f, 0.68f, 0.78f, 0.675f, 0.72f);
                    Poly(segs, true, 0.63f, 0.57f, 0.72f, 0.44f, 0.63f, 0.31f, 0.54f, 0.44f);
                    break;
                case "arrows":
                    Line(segs, 0.14f, 0.30f, 0.70f, 0.86f); Poly(segs, false, 0.53f, 0.83f, 0.70f, 0.86f, 0.67f, 0.69f);
                    Line(segs, 0.14f, 0.30f, 0.14f, 0.43f); Line(segs, 0.14f, 0.30f, 0.27f, 0.30f); Line(segs, 0.21f, 0.37f, 0.21f, 0.50f); Line(segs, 0.21f, 0.37f, 0.34f, 0.37f);
                    Line(segs, 0.32f, 0.12f, 0.86f, 0.66f); Poly(segs, false, 0.69f, 0.63f, 0.86f, 0.66f, 0.83f, 0.49f);
                    Line(segs, 0.32f, 0.12f, 0.32f, 0.25f); Line(segs, 0.32f, 0.12f, 0.45f, 0.12f); Line(segs, 0.39f, 0.19f, 0.39f, 0.32f); Line(segs, 0.39f, 0.19f, 0.52f, 0.19f);
                    break;
                case "pouch":
                    Arc(segs, 0.5f, 0.37f, 0.27f, 132f, 408f); Line(segs, 0.32f, 0.57f, 0.40f, 0.66f); Line(segs, 0.68f, 0.57f, 0.60f, 0.66f);
                    Line(segs, 0.37f, 0.66f, 0.63f, 0.66f); Poly(segs, false, 0.40f, 0.66f, 0.33f, 0.82f, 0.50f, 0.75f, 0.67f, 0.82f, 0.60f, 0.66f);
                    Arc(segs, 0.5f, 0.36f, 0.075f, 0f, 360f);
                    break;
                case "pin":
                    half = 0.034f;
                    Arc(segs, 0.5f, 0.66f, 0.17f, 0f, 360f); Line(segs, 0.5f, 0.49f, 0.5f, 0.12f); Line(segs, 0.36f, 0.49f, 0.64f, 0.49f);
                    break;
                case "sort":
                    half = 0.036f;
                    Line(segs, 0.34f, 0.20f, 0.34f, 0.80f); Poly(segs, false, 0.21f, 0.64f, 0.34f, 0.80f, 0.47f, 0.64f);
                    Line(segs, 0.66f, 0.80f, 0.66f, 0.20f); Poly(segs, false, 0.53f, 0.36f, 0.66f, 0.20f, 0.79f, 0.36f);
                    break;
                default: // filter
                    half = 0.036f;
                    Poly(segs, true, 0.18f, 0.78f, 0.82f, 0.78f, 0.57f, 0.48f, 0.57f, 0.20f, 0.43f, 0.28f, 0.43f, 0.48f);
                    break;
            }
            var px = new Color[N * N]; float hw = half * N;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f, best = 1e9f;
                    foreach (var s4 in segs)
                    {
                        float ax = s4.x * N, ay = s4.y * N, bx = s4.z * N, by = s4.w * N, dx = bx - ax, dy = by - ay;
                        float l2 = dx * dx + dy * dy, t = l2 < 1e-6f ? 0f : Mathf.Clamp01(((fx - ax) * dx + (fy - ay) * dy) / l2);
                        float ex = fx - (ax + dx * t), ey = fy - (ay + dy * t), d = ex * ex + ey * ey;
                        if (d < best) best = d;
                    }
                    px[y * N + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - (Mathf.Sqrt(best) - hw)));
                }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "LokiInv:" + name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4, hideFlags = HideFlags.DontSave };
            tex.SetPixels(px); tex.Apply(true, false);
            s = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
            s.name = "LokiInv:" + name; s.hideFlags = HideFlags.DontSave;
            Made[name] = s;
            return s;
        }

        private static void Line(List<Vector4> l, float ax, float ay, float bx, float by) => l.Add(new Vector4(ax, ay, bx, by));
        private static void Poly(List<Vector4> l, bool closed, params float[] p)
        {
            int n = p.Length / 2;
            for (int i = 0; i + 1 < n; i++) Line(l, p[2 * i], p[2 * i + 1], p[2 * i + 2], p[2 * i + 3]);
            if (closed && n > 2) Line(l, p[2 * n - 2], p[2 * n - 1], p[0], p[1]);
        }
        private static void Arc(List<Vector4> l, float cx, float cy, float r, float a0, float a1)
        {
            int n = Mathf.Max(4, Mathf.CeilToInt(Mathf.Abs(a1 - a0) / 12f));
            for (int i = 0; i < n; i++)
            {
                float t0 = Mathf.Lerp(a0, a1, (float)i / n) * Mathf.Deg2Rad, t1 = Mathf.Lerp(a0, a1, (float)(i + 1) / n) * Mathf.Deg2Rad;
                Line(l, cx + r * Mathf.Cos(t0), cy + r * Mathf.Sin(t0), cx + r * Mathf.Cos(t1), cy + r * Mathf.Sin(t1));
            }
        }
    }
}
