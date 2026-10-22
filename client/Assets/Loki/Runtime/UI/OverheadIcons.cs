using System;
using System.Collections.Generic;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 21 (w21over), the design's point 58: the overhead menu's LINE ICONS (UI/OverheadMenu.cs: one in a gold ring before each
    /// control's name: THE PANEL THEME's "dark line icons in thin gold rings", the UI theme palette).
    /// Every icon is DRAWN IN CODE at first use: strokes of one width with round ends, white on nothing (the menu tints them the theme's
    /// ink); no file, nothing from the game's data. <see cref="For"/> gives a control's icon by its key (the keys the menu's sections use:
    /// LokiGame.W13Resize.cs, W13View.cs, W13Grab.cs, W13Look.cs, W14Laser.cs, W20Bubbles.cs); a key nobody drew gets a plain dot.
    ///   resize      two arrows along one diagonal (larger / smaller)          reposition, move   four arrows from the middle (carry)
    ///   casting     a wand with a spark at its tip                            minimap            a folded map
    ///   bar         three slots in a row (the forearm bar)                    tilt               a tilted panel and the turn's arrow
    ///   look, shown an eye                                                    laserwalk          a ray landing on a ground ring
    ///   bubbles     three bubbles                                             bubble             one bubble
    ///   holds       a slot with a spark in it (what the bubble holds)         active             the on / off mark
    ///   useby       a fingertip and the ripples of its touch
    /// </summary>
    public static class OverheadIcons
    {
        /// <summary>The picture's side (px), a stroke's width and a thin one's (shares of the side).</summary>
        public const int Px = 128;
        public const float Stroke = 0.084f, Thin = 0.060f;
        private static readonly Dictionary<string, Sprite> Made = new Dictionary<string, Sprite>();
        /// <summary>Every key that has a drawing of its own.</summary>
        public static readonly string[] Keys = { "resize", "reposition", "move", "casting", "minimap", "bar", "tilt", "look", "shown", "visionaim", "laserwalk", "bubbles", "bubble", "holds", "active", "useby" };

        /// <summary>The icon of the control <paramref name="key"/> (made once).</summary>
        public static Sprite For(string key)
        {
            key = key ?? "";
            if (Made.TryGetValue(key, out var s) && s != null) return s;
            var ink = new Ink();
            switch (key)
            {
                case "resize": Resize(ink); break;
                case "reposition": case "move": Move(ink); break;
                case "casting": Wand(ink); break;
                case "minimap": Map(ink); break;
                case "bar": Bar(ink); break;
                case "tilt": Tilt(ink); break;
                case "look": case "shown": case "visionaim": Eye(ink); break;
                case "laserwalk": LaserWalk(ink); break;
                case "bubbles": Bubbles(ink); break;
                case "bubble": Bubble(ink); break;
                case "holds": Holds(ink); break;
                case "active": Power(ink); break;
                case "useby": Touch(ink); break;
                default: ink.Dot(0.5f, 0.5f, 0.12f); break;
            }
            s = ink.Bake("LokiOverIcon:" + (Array.IndexOf(Keys, key) >= 0 ? key : "dot"));
            Made[key] = s;
            return s;
        }
        /// <summary>The key has a drawing of its own (not the plain dot).</summary>
        public static bool Has(string key) => Array.IndexOf(Keys, key ?? "") >= 0;

        // ------------------------------------------------------------------ the drawings (0-1, y up)
        private static void Resize(Ink k)
        {
            k.Line(0.24f, 0.24f, 0.76f, 0.76f);
            k.Line(0.76f, 0.76f, 0.52f, 0.76f); k.Line(0.76f, 0.76f, 0.76f, 0.52f);
            k.Line(0.24f, 0.24f, 0.48f, 0.24f); k.Line(0.24f, 0.24f, 0.24f, 0.48f);
        }
        private static void Move(Ink k)
        {
            k.Line(0.5f, 0.13f, 0.5f, 0.87f); k.Line(0.13f, 0.5f, 0.87f, 0.5f);
            k.Line(0.5f, 0.87f, 0.38f, 0.75f); k.Line(0.5f, 0.87f, 0.62f, 0.75f);
            k.Line(0.5f, 0.13f, 0.38f, 0.25f); k.Line(0.5f, 0.13f, 0.62f, 0.25f);
            k.Line(0.13f, 0.5f, 0.25f, 0.38f); k.Line(0.13f, 0.5f, 0.25f, 0.62f);
            k.Line(0.87f, 0.5f, 0.75f, 0.38f); k.Line(0.87f, 0.5f, 0.75f, 0.62f);
        }
        private static void Wand(Ink k)
        {
            k.Line(0.17f, 0.17f, 0.56f, 0.56f);
            // the spark at its tip: four arms, and two small ones beside it
            k.Line(0.70f, 0.52f, 0.70f, 0.88f, Thin); k.Line(0.52f, 0.70f, 0.88f, 0.70f, Thin);
            k.Dot(0.36f, 0.76f, 0.04f); k.Dot(0.80f, 0.36f, 0.04f);
        }
        private static void Map(Ink k)
        {
            k.Poly(true, 0.13f, 0.75f, 0.38f, 0.83f, 0.62f, 0.75f, 0.87f, 0.83f, 0.87f, 0.25f, 0.62f, 0.17f, 0.38f, 0.25f, 0.13f, 0.17f);
            k.Line(0.38f, 0.83f, 0.38f, 0.25f, Thin); k.Line(0.62f, 0.75f, 0.62f, 0.17f, Thin);
        }
        private static void Bar(Ink k)
        {
            k.Poly(true, 0.11f, 0.36f, 0.89f, 0.36f, 0.89f, 0.64f, 0.11f, 0.64f);
            k.Line(0.37f, 0.36f, 0.37f, 0.64f, Thin); k.Line(0.63f, 0.36f, 0.63f, 0.64f, Thin);
        }
        private static void Tilt(Ink k)
        {
            // a panel turned 20 degrees, and the turn's arrow over its corner
            float c = Mathf.Cos(20f * Mathf.Deg2Rad), s = Mathf.Sin(20f * Mathf.Deg2Rad); const float hw = 0.27f, hh = 0.17f, cx = 0.47f, cy = 0.44f;
            Vector2 P(float x, float y) => new Vector2(cx + x * c - y * s, cy + x * s + y * c);
            var a = P(-hw, -hh); var b = P(hw, -hh); var d = P(hw, hh); var e = P(-hw, hh);
            k.Poly(true, a.x, a.y, b.x, b.y, d.x, d.y, e.x, e.y);
            k.Arc(0.47f, 0.44f, 0.42f, 48f, 88f, Thin);
            var tip = new Vector2(0.47f + 0.42f * Mathf.Cos(88f * Mathf.Deg2Rad), 0.44f + 0.42f * Mathf.Sin(88f * Mathf.Deg2Rad));
            k.Line(tip.x, tip.y, tip.x + 0.10f, tip.y + 0.045f, Thin); k.Line(tip.x, tip.y, tip.x + 0.085f, tip.y - 0.08f, Thin);
        }
        private static void Eye(Ink k)
        {
            k.Curve(0.11f, 0.5f, 0.5f, 0.90f, 0.89f, 0.5f); k.Curve(0.11f, 0.5f, 0.5f, 0.10f, 0.89f, 0.5f);
            k.Circle(0.5f, 0.5f, 0.125f, Thin); k.Dot(0.5f, 0.5f, 0.05f);
        }
        private static void LaserWalk(Ink k)
        {
            // the ray from the hand (upper left) lands in a ring on the ground
            k.Dot(0.19f, 0.84f, 0.06f); k.Line(0.19f, 0.84f, 0.58f, 0.36f);
            k.Ellipse(0.60f, 0.31f, 0.27f, 0.12f, Thin);
        }
        private static void Bubbles(Ink k)
        {
            k.Circle(0.36f, 0.42f, 0.22f); k.Arc(0.36f, 0.42f, 0.125f, 105f, 160f, Thin);
            k.Circle(0.71f, 0.68f, 0.14f, Thin); k.Circle(0.73f, 0.27f, 0.085f, Thin);
        }
        private static void Bubble(Ink k) { k.Circle(0.5f, 0.5f, 0.33f); k.Arc(0.5f, 0.5f, 0.20f, 100f, 165f, Thin); }
        private static void Holds(Ink k)
        {
            k.Poly(true, 0.18f, 0.18f, 0.82f, 0.18f, 0.82f, 0.82f, 0.18f, 0.82f);
            k.Line(0.5f, 0.33f, 0.5f, 0.67f, Thin); k.Line(0.33f, 0.5f, 0.67f, 0.5f, Thin);
            k.Line(0.41f, 0.41f, 0.59f, 0.59f, Thin); k.Line(0.41f, 0.59f, 0.59f, 0.41f, Thin);
        }
        private static void Power(Ink k) { k.Arc(0.5f, 0.46f, 0.31f, 125f, 415f); k.Line(0.5f, 0.50f, 0.5f, 0.88f); }
        private static void Touch(Ink k)
        {
            k.Line(0.5f, 0.12f, 0.5f, 0.50f, Stroke * 1.5f);
            k.Arc(0.5f, 0.50f, 0.22f, 25f, 155f, Thin); k.Arc(0.5f, 0.50f, 0.36f, 32f, 148f, Thin);
        }

        // ------------------------------------------------------------------ strokes into pixels
        private sealed class Ink
        {
            private readonly List<Vector4> _segs = new List<Vector4>(); private readonly List<float> _half = new List<float>();
            public void Line(float x0, float y0, float x1, float y1, float width = Stroke) { _segs.Add(new Vector4(x0, y0, x1, y1)); _half.Add(width * 0.5f); }
            public void Dot(float x, float y, float r) { _segs.Add(new Vector4(x, y, x, y)); _half.Add(r); }
            public void Poly(bool closed, params float[] xy)
            {
                int n = xy.Length / 2;
                for (int i = 0; i + 1 < n; i++) Line(xy[2 * i], xy[2 * i + 1], xy[2 * i + 2], xy[2 * i + 3]);
                if (closed && n > 2) Line(xy[2 * n - 2], xy[2 * n - 1], xy[0], xy[1]);
            }
            public void Arc(float cx, float cy, float r, float fromDeg, float toDeg, float width = Stroke) => Ellipse(cx, cy, r, r, width, fromDeg, toDeg);
            public void Circle(float cx, float cy, float r, float width = Stroke) => Ellipse(cx, cy, r, r, width);
            public void Ellipse(float cx, float cy, float rx, float ry, float width = Stroke, float fromDeg = 0f, float toDeg = 360f)
            {
                int n = Mathf.Max(4, Mathf.CeilToInt(Mathf.Abs(toDeg - fromDeg) / 9f));
                for (int i = 0; i < n; i++)
                {
                    float a = (fromDeg + (toDeg - fromDeg) * i / n) * Mathf.Deg2Rad, b = (fromDeg + (toDeg - fromDeg) * (i + 1) / n) * Mathf.Deg2Rad;
                    Line(cx + rx * Mathf.Cos(a), cy + ry * Mathf.Sin(a), cx + rx * Mathf.Cos(b), cy + ry * Mathf.Sin(b), width);
                }
            }
            /// <summary>A curve from (x0, y0) to (x1, y1) bent towards (cx, cy) (a quadratic Bezier).</summary>
            public void Curve(float x0, float y0, float cx, float cy, float x1, float y1, float width = Stroke)
            {
                const int n = 18; Vector2 last = new Vector2(x0, y0);
                for (int i = 1; i <= n; i++)
                {
                    float t = i / (float)n, u = 1f - t; var p = new Vector2(u * u * x0 + 2f * u * t * cx + t * t * x1, u * u * y0 + 2f * u * t * cy + t * t * y1);
                    Line(last.x, last.y, p.x, p.y, width); last = p;
                }
            }

            public Sprite Bake(string name)
            {
                var px = new Color32[Px * Px];
                for (int y = 0; y < Px; y++)
                    for (int x = 0; x < Px; x++)
                    {
                        float fx = (x + 0.5f) / Px, fy = (y + 0.5f) / Px, best = 9f;
                        for (int i = 0; i < _segs.Count; i++)
                        {
                            var s = _segs[i]; float dx = s.z - s.x, dy = s.w - s.y, len2 = dx * dx + dy * dy;
                            float t = len2 > 1e-9f ? Mathf.Clamp01(((fx - s.x) * dx + (fy - s.y) * dy) / len2) : 0f;
                            float qx = fx - (s.x + dx * t), qy = fy - (s.y + dy * t), d = Mathf.Sqrt(qx * qx + qy * qy) - _half[i];
                            if (d < best) best = d;
                        }
                        float a = Mathf.Clamp01(0.5f - best * Px); // (one pixel of soft edge)
                        px[y * Px + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                var tex = new Texture2D(Px, Px, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4, hideFlags = HideFlags.DontSave };
                tex.SetPixels32(px); tex.Apply(true, false);
                var sp = Sprite.Create(tex, new Rect(0, 0, Px, Px), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                sp.name = name; sp.hideFlags = HideFlags.DontSave;
                return sp;
            }
        }
    }
}
