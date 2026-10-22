using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>What a panel made by <see cref="UiKit.Panel"/> looks like (client wave 21, w21kit: THE PANEL THEME).
    /// Auto: a panel with a background is Framed (the names in <see cref="Theme.LegacyNames"/> keep the old dark look until their
    /// own agent restyles them), one without has no picture at all. Framed: the dark wood frame with the gold line and corner scrolls,
    /// the cream canvas with leather edging and stitching. Card: a cream parchment card (tooltips, notices, item cards). SeeThrough:
    /// the gold line and scrolls only, nothing behind the lettering (HUD pieces). Legacy: the dark blue panel of waves 1-20.</summary>
    public enum PanelLook { Auto, Framed, Card, SeeThrough, Legacy, None }

    /// <summary>Sides of a box (a facet joined to its neighbour has no frame on that side: <see cref="UiKit.Join"/>).</summary>
    [Flags] public enum Sides { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }

    /// <summary>What a text or a button stands on (a <see cref="UiSurface"/> on the panel or on a part of it).</summary>
    public enum Surface { None, Canvas, Card, Gold, Wood, Clear }

    /// <summary>Marks a panel (or a part of one) as a surface of the theme: labels and buttons made under it take its colours, and a
    /// <see cref="ThemedText"/> under it keeps its letters readable on it (<see cref="Theme.Readable"/>).</summary>
    public sealed class UiSurface : MonoBehaviour
    {
        public Surface Kind;
        /// <summary>The theme's unit on this panel: panel px per design px of the frame's pictures (<see cref="Theme.Unit"/>).</summary>
        public float Unit = 0.45f;
        /// <summary>How far the frame stands out of the panel's own rectangle, px (0: a card, see-through, a part).</summary>
        public float Outset;
        public Color Color => Theme.ColorOf(Kind);
        /// <summary>Cream / parchment / gold: dark ink on it.</summary>
        public bool Light => Kind == Surface.Canvas || Kind == Surface.Card || Kind == Surface.Gold;
        public bool Themed => Kind != Surface.None;
    }

    /// <summary>
    /// Client wave 21 (w21kit): THE PANEL THEME, the one place for the look of every panel (design note, 2026-10-10: "For the inventory one, you
    /// are to use that as reference for every panel UI element ... Everything else gets updated"; the picture:
    /// the UI theme palette). A dark wood frame with a thin gold line and small gold
    /// scrolls at the corners; inside, cream canvas with brown leather edging and dashed stitching; a small gold title plate; soft
    /// square cells; round gold buttons; a hovered thing outlined in gold; cream parchment cards.
    /// Every picture here is DRAWN IN CODE at first use (no file, nothing from the game's data): <see cref="Frame"/>,
    /// <see cref="CanvasFill"/>, <see cref="CardFill"/>, <see cref="Plate"/>, <see cref="Soft"/>, <see cref="SoftRim"/>,
    /// <see cref="HoverRim"/>, <see cref="Disc"/>, <see cref="Ring"/>, <see cref="SeeFrame"/>.
    /// CONTRAST (WCAG 2.x, the rule of UI/UiKit.cs <see cref="UiKit.Contrast"/>), measured on <see cref="Canvas"/> #EFE3C6:
    ///   <see cref="Ink"/> #2A1C11 12.9 : 1 (the text), <see cref="InkDim"/> #54402C 7.7 : 1 (second lines), <see cref="InkFaint"/>
    ///   #6E5A42 5.1 : 1 (switched off), <see cref="Bad"/> #8C1D12 7.2, <see cref="Good"/> #1A5020 7.5, <see cref="Accent"/> #74370A 7.2,
    ///   <see cref="Blue"/> #1B4682 7.3; on a card (#F6EBCF) 13.9 / 8.2 / 5.5; on a button (<see cref="Cell"/> #DDCBA4) Ink 10.3; on the
    ///   lit choice (<see cref="Lit"/> #E2B344) Ink 8.5; on the title plate (#D9A93F) Ink 7.6; on the wood <see cref="OnWood"/> 11.4,
    ///   the gold line 6.7. (The smoke w21kit prints and checks them.)
    /// Any OTHER colour a text is given (the dark panels' light colours, the game data's ^RRGGBB codes, rich-text tags) is turned at draw
    /// time into the nearest colour that reaches <see cref="MinContrast"/> on what is behind it (<see cref="Readable"/>).
    /// </summary>
    public static class Theme
    {
        // ------------------------------------------------------------------ the palette
        public static readonly Color Wood = Rgb(0x3A2415), WoodDark = Rgb(0x23150B), WoodLight = Rgb(0x5B3C22);
        public static readonly Color Gold = Rgb(0xD9A93F), GoldLight = Rgb(0xF5D77F), GoldDark = Rgb(0x8A651C);
        public static readonly Color Canvas = Rgb(0xEFE3C6), CanvasShade = Rgb(0xE3D5B1);
        public static readonly Color Leather = Rgb(0x7B4B2B), LeatherDark = Rgb(0x53301B), LeatherLight = Rgb(0x9C6B42);
        public static readonly Color Parchment = Rgb(0xF6EBCF), ParchmentEdge = Rgb(0xDCC797);
        /// <summary>The text on canvas, cards and gold; second lines; switched-off things.</summary>
        public static readonly Color Ink = Rgb(0x2A1C11), InkDim = Rgb(0x54402C), InkFaint = Rgb(0x6E5A42);
        /// <summary>Coloured inks on canvas: good news / bad news / a highlighted word (names, prices, headings) / a link or a mark.</summary>
        public static readonly Color Good = Rgb(0x1A5020), Bad = Rgb(0x8C1D12), Accent = Rgb(0x74370A), Blue = Rgb(0x1B4682);
        /// <summary>Text on the wood frame and on a see-through piece.</summary>
        public static readonly Color OnWood = Rgb(0xEFE3C6), OnClear = Rgb(0xFFF6DC);
        /// <summary>A plain button and an item cell (a soft square a little darker than the canvas), under the laser, switched off.</summary>
        public static readonly Color Cell = Rgb(0xDDCBA4), CellHover = Rgb(0xFFF7E0), CellOff = Rgb(0xE6DCC4), CellRim = new Color(0.33f, 0.19f, 0.10f, 0.55f);
        /// <summary>The current choice / a round gold button, and under the laser.</summary>
        public static readonly Color Lit = Rgb(0xE2B344), LitHover = Rgb(0xF6D36E);
        /// <summary>A row's band on the canvas, a highlighted row, a thin rule.</summary>
        public static readonly Color Row = new Color(0.33f, 0.19f, 0.10f, 0.07f), RowLit = new Color(0.85f, 0.66f, 0.25f, 0.34f), Rule = new Color(0.33f, 0.19f, 0.10f, 0.45f);
        /// <summary>The least contrast of a text on its surface (7 : 1, WCAG's AAA for small text: the rule the dark panels had for the
        /// game data's colours, UI/UiKit.cs ReadableLuminance); a switched-off (faint) text: 4.5 : 1 (AA).</summary>
        public const float MinContrast = 7f, MinFaint = 4.5f;

        /// <summary>Panels that keep the dark look of waves 1-20 until their own agent restyles them (wave 21: Horde's choice, the casting
        /// pads, the chat, the overhead menu, the forearm bar and its strip, the keyboard). To theme one: take its name out of here, or
        /// pass a look to <see cref="UiKit.Panel"/>.</summary>
        public static readonly HashSet<string> LegacyNames = new HashSet<string> { "CastPadLeft", "CastPadRight" }; // (client wave 21, w21over: the overhead menu is themed: UI/OverheadMenu.cs) (client wave 21, w21tools: the chat, the forearm bar, the button strip and the keyboard have their looks now) (client wave 21, w21horde: Horde's job choice is themed)

        private static Color Rgb(int v) => new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
        /// <summary>RRGGBB of a colour (for a rich-text tag: "&lt;color=#" + Theme.Hex(Theme.Good) + "&gt;").</summary>
        public static string Hex(Color c) { Color32 k = c; return $"{k.r:X2}{k.g:X2}{k.b:X2}"; }
        public static Color ColorOf(Surface s) => s == Surface.Canvas ? Canvas : s == Surface.Card ? Parchment : s == Surface.Gold ? Gold : s == Surface.Wood ? Wood : Color.clear;
        /// <summary>The surface a transform stands on (null: none: a dark panel of waves 1-20 or a bare HUD piece).</summary>
        public static UiSurface SurfaceOf(Transform t) => t != null ? t.GetComponentInParent<UiSurface>(true) : null;
        /// <summary>The colour a new text gets on this surface when its maker names none.</summary>
        public static Color TextOn(UiSurface s) => s == null || !s.Themed ? UiKit.TextColor : s.Light ? Ink : s.Kind == Surface.Wood ? OnWood : OnClear;

        // ------------------------------------------------------------------ sizes
        /// <summary>Design px of the pictures: the wood band, what of it lies under the leather, the leather band; the stitching's distance
        /// from the canvas' edge, a stitch, the gap, its width.</summary>
        public const float WoodPx = 64f, UnderPx = 8f, LeatherPx = 22f, StitchInPx = 15f, StitchPx = 20f, StitchGapPx = 13f, StitchWidePx = 4.5f;
        /// <summary>The frame's wood, in metres: 6 % of the panel's smaller side, between 5 mm and 20 mm.</summary>
        public const float WoodShare = 0.06f, WoodMin = 0.005f, WoodMax = 0.020f;
        /// <summary>Panel px per design px on a panel of this size: the wood is <see cref="WoodShare"/> of its smaller side (5-20 mm).</summary>
        public static float Unit(Vector2 sizePx, float metresPerPx)
        {
            float m = Mathf.Max(1e-6f, Mathf.Abs(metresPerPx));
            float wood = Mathf.Clamp(Mathf.Min(sizePx.x, sizePx.y) * m * WoodShare, WoodMin, WoodMax);
            return wood / m / WoodPx;
        }
        /// <summary>How far a Framed panel's frame stands out of its rectangle, px.</summary>
        public static float FrameOutset(float unit) => (WoodPx + LeatherPx) * unit;

        // ------------------------------------------------------------------ readable colours
        private static readonly Dictionary<long, Color32> ReadCache = new Dictionary<long, Color32>();
        /// <summary>The colour a text of colour <paramref name="c"/> is DRAWN with on <paramref name="back"/>: itself when its contrast is at
        /// least <see cref="MinContrast"/>; otherwise, on a light surface, a grey becomes an ink (bright: <see cref="Ink"/>, middle:
        /// <see cref="InkDim"/>, dark: <see cref="InkFaint"/>, which needs <see cref="MinFaint"/>) and a colour keeps its hue (gold and
        /// yellow become the burnt sienna of <see cref="Accent"/>) and is darkened until it reaches the contrast; on a dark surface it is
        /// lightened towards white. Alpha is kept. <paramref name="faint"/>: a switched-off text (4.5 : 1 is enough).</summary>
        public static Color32 Readable(Color32 c, Color back, bool faint = false)
        {
            Color32 b32 = back;
            long key = ((long)c.r << 40) | ((long)c.g << 32) | ((long)c.b << 24) | ((long)b32.r << 16) | ((long)b32.g << 8) | b32.b | (faint ? 1L << 48 : 0L);
            if (ReadCache.TryGetValue(key, out var hit)) { hit.a = c.a; return hit; }
            Color col = new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f), bk = new Color(back.r, back.g, back.b, 1f);
            float need = faint ? MinFaint : MinContrast;
            float lb = UiKit.Luminance(bk);
            bool light = (lb + 0.05f) * (lb + 0.05f) >= 0.0525f; // black on it reads better than white
            if (faint && light)
            {
                col = InkFaint; // a switched-off text: the faint ink, darkened only where the surface asks for it
                for (int i = 0; i < 40 && UiKit.Contrast(col, bk) < need; i++) col = Color.Lerp(col, Color.black, 0.06f);
            }
            else if (UiKit.Contrast(col, bk) < need)
            {
                if (light)
                {
                    Color.RGBToHSV(col, out float h, out float s, out float v);
                    if (s < 0.25f)
                    {
                        float l = UiKit.Luminance(col);
                        if (l >= 0.55f) col = Ink; else if (l >= 0.25f) col = InkDim; else { col = InkFaint; need = Mathf.Min(need, MinFaint); }
                    }
                    else
                    {
                        if (h >= 30f / 360f && h <= 65f / 360f) h = 24f / 360f; // gold and yellow do not read on cream: burnt sienna
                        col = Color.HSVToRGB(h, Mathf.Max(s, 0.8f), Mathf.Min(v, 0.62f));
                    }
                    for (int i = 0; i < 40 && UiKit.Contrast(col, bk) < need; i++) col = Color.Lerp(col, Color.black, 0.06f);
                }
                else for (int i = 0; i < 40 && UiKit.Contrast(col, bk) < need; i++) col = Color.Lerp(col, Color.white, 0.08f);
            }
            Color32 r = col; r.a = 255;
            if (ReadCache.Count > 4096) ReadCache.Clear();
            ReadCache[key] = r;
            r.a = c.a; return r;
        }
        /// <summary>The laser's line over a light surface of the theme (VR/DioramaRig*.cs): on nothing it can press, on a button; and how
        /// strong its far end is there (0.1 elsewhere). The dark panels' pale yellow and light blue lines vanish on cream (w11fix "1 laser
        /// over a panel, pixels": 22 of 30 points showed; it asks for 27).</summary>
        public static readonly Color LaserIdle = new Color(0.78f, 0.38f, 0.02f, 0.95f), LaserOnButton = new Color(0.08f, 0.42f, 1f, 0.95f);
        public const float LaserEndAlpha = 0.35f;
        /// <summary>An input box and a bar's groove on canvas: lighter than the canvas.</summary>
        public static readonly Color Field = Rgb(0xFBF4E2);
        private static HashSet<int> _own;
        private static int Key(Color c) { Color32 k = c; return (k.r << 16) | (k.g << 8) | k.b; }
        /// <summary>One of the theme's own colours (whatever its alpha).</summary>
        public static bool Own(Color c)
        {
            if (_own == null)
            {
                _own = new HashSet<int>();
                foreach (var k in new[] { Wood, WoodDark, WoodLight, Gold, GoldLight, GoldDark, Canvas, CanvasShade, Leather, LeatherDark, LeatherLight, Parchment, ParchmentEdge, Ink, InkDim, InkFaint,
                    Good, Bad, Accent, Blue, OnWood, OnClear, Cell, CellHover, CellOff, CellRim, Lit, LitHover, Row, RowLit, Rule, Field }) _own.Add(Key(k));
            }
            return _own.Contains(Key(c));
        }
        /// <summary>The colour a plain box (a button's fill, a row's band, a rule, a bar) of colour <paramref name="c"/> is DRAWN with on the
        /// theme's light surfaces (UI/Theme.cs <see cref="ThemedImage"/>): the dark panels' colours become their light counterparts, so
        /// code written for the dark panel keeps its meaning (chosen, switched off, under the laser) without being rewritten:
        ///   the theme's own colours and any warm light colour: as they are;
        ///   a tint (alpha under 0.5): gold stays a gold tint (stronger: cream swallows it), white / grey / blue become a leather tint,
        ///     another hue keeps it, darker;
        ///   black: an input box (<see cref="Field"/>); a dark grey: switched off (<see cref="CellOff"/>); a light grey or white (a knob,
        ///     a rail, a rule): ink;
        ///   a dark gold or brown (the dark panels' chosen tab): <see cref="Lit"/>;
        ///   the dark panels' blues: their own background becomes canvas, a button <see cref="Cell"/>, a hovered button
        ///     <see cref="CellHover"/>, a light blue-grey line ink;
        ///   a dark green / red / violet button: the same hue, pale (30 % towards the plain button); a bright one (a bar, a mark): the
        ///     same hue as a dark ink.</summary>
        public static Color Fill(Color c)
        {
            if (c.a < 0.02f || Own(c)) return c;
            Color.RGBToHSV(new Color(c.r, c.g, c.b, 1f), out float h, out float s, out float v);
            float l = UiKit.Luminance(c), deg = h * 360f;
            bool warm = deg >= 20f && deg <= 70f, blue = deg >= 190f && deg <= 265f && v <= 0.78f; // (a bright blue is a bar or a mark: it keeps its hue)
            if (c.a < 0.5f)
            {
                if (warm && s >= 0.3f) return new Color(0.85f, 0.66f, 0.25f, Mathf.Min(0.6f, c.a * 2.4f));
                if (s < 0.25f || blue) return new Color(Row.r, Row.g, Row.b, Mathf.Min(0.5f, v > 0.5f ? c.a * 1.7f : c.a * 0.55f));
                var t = Color.HSVToRGB(h, Mathf.Max(s, 0.6f), 0.5f); t.a = Mathf.Min(0.55f, c.a * 1.8f); return t;
            }
            Color o;
            if (warm && l >= 0.40f && s >= 0.08f) return c;
            if (s < 0.14f) o = v < 0.06f ? Field : l < 0.30f ? CellOff : l >= 0.55f ? Ink : InkDim;
            else if (warm) o = Lit;
            else if (blue) o = v <= 0.14f ? Canvas : l < 0.115f ? Cell : l < 0.30f ? CellHover : InkDim;
            else if (l < 0.30f) o = Color.Lerp(Color.HSVToRGB(h, Mathf.Min(0.42f, s * 0.6f), 0.86f), Cell, 0.3f); // pale, a little towards the plain button: it sits in the palette
            else o = Readable(c, Canvas);
            o.a = c.a; return o;
        }

        /// <summary><paramref name="top"/> over <paramref name="under"/> (plain alpha).</summary>
        public static Color Over(Color top, Color under) { float a = Mathf.Clamp01(top.a); return new Color(top.r * a + under.r * (1 - a), top.g * a + under.g * (1 - a), top.b * a + under.b * (1 - a), 1f); }

        // ------------------------------------------------------------------ the pictures (drawn in code, once)
        private static Sprite _frame, _canvas, _card, _plate, _soft, _softRim, _hoverRim, _disc, _ring, _seeFrame;
        /// <summary>The wood frame: 9-slice, its middle empty (the canvas lies on it). Border = wood + what lies under the leather.</summary>
        public static Sprite Frame => JobTheme.Pick(JobTheme.Slot.Frame, _frame != null ? _frame : (_frame = MakeFrame())); // (client wave 21, w21jobui: the job variant's, UI/JobTheme.cs; Base: this very sprite)
        /// <summary>The cream canvas with its leather edging: 9-slice.</summary>
        public static Sprite CanvasFill => JobTheme.Pick(JobTheme.Slot.Canvas, _canvas != null ? _canvas : (_canvas = MakeCanvas()));
        /// <summary>A parchment card with a soft shadow: 9-slice (the shadow is <see cref="CardShadowPx"/> design px outside the card).</summary>
        public static Sprite CardFill => JobTheme.Pick(JobTheme.Slot.Card, _card != null ? _card : (_card = MakeCard()));
        public const float CardShadowPx = 8f;
        /// <summary>The gold title plate: 9-slice.</summary>
        public static Sprite Plate => JobTheme.Pick(JobTheme.Slot.Plate, _plate != null ? _plate : (_plate = MakePlate()));
        /// <summary>A soft square, white (tint it): 9-slice; its thin rim; the gold outline of a hovered thing (in its own colours).</summary>
        public static Sprite Soft => _soft != null ? _soft : (_soft = MakeSoft(0));
        public static Sprite SoftRim => _softRim != null ? _softRim : (_softRim = MakeSoft(1));
        public static Sprite HoverRim => _hoverRim != null ? _hoverRim : (_hoverRim = MakeSoft(2));
        /// <summary>A round button, shaded white (tint it gold); a thin gold ring with a pale middle (an icon's header).</summary>
        public static Sprite Disc => _disc != null ? _disc : (_disc = MakeDisc());
        public static Sprite Ring => _ring != null ? _ring : (_ring = MakeRing());
        /// <summary>The see-through frame: the gold line and the corner scrolls only: 9-slice.</summary>
        public static Sprite SeeFrame => JobTheme.Pick(JobTheme.Slot.SeeFrame, _seeFrame != null ? _seeFrame : (_seeFrame = MakeSeeFrame()));
        /// <summary>Client wave 21 (w21jobui): the stitching's thread: the leather brown, or the job variant's accent (UI/JobTheme.cs).</summary>
        public static Color Thread => JobTheme.Thread(new Color(Leather.r, Leather.g, Leather.b, 0.85f));

        private static Sprite Make(string name, int w, int h, Color[] px, Vector4 border)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "LokiTheme:" + name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4, hideFlags = HideFlags.DontSave };
            tex.SetPixels(px); tex.Apply(true, false);
            var sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            sp.name = "LokiTheme:" + name; sp.hideFlags = HideFlags.DontSave;
            return sp;
        }
        private static float Cov(float d) => Mathf.Clamp01(0.5f - d);
        private static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));
        private static float RoundRect(float px, float py, float hw, float hh, float r)
        {
            float qx = Mathf.Abs(px) - hw + r, qy = Mathf.Abs(py) - hh + r;
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Min(Mathf.Max(qx, qy), 0f) + Mathf.Sqrt(ox * ox + oy * oy) - r;
        }
        private static float Hash(int x, int y) { unchecked { uint n = (uint)(x * 374761393 + y * 668265263); n = (n ^ (n >> 13)) * 1274126177u; return ((n ^ (n >> 16)) & 0xFFFF) / 65535f; } }
        private static float Noise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(Hash(xi, yi), Hash(xi + 1, yi), fx), Mathf.Lerp(Hash(xi, yi + 1), Hash(xi + 1, yi + 1), fx), fy);
        }
        private static Color Mix(Color a, Color b, float t) { t = Mathf.Clamp01(t); return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, 1f); }
        private static Color Mul(Color a, float k) => new Color(Mathf.Clamp01(a.r * k), Mathf.Clamp01(a.g * k), Mathf.Clamp01(a.b * k), 1f);

        /// <summary>A small scroll: a spiral of 1.3 turns whose outer end touches the gold line. cx: along the edge from the corner, cy: into
        /// the frame, both design px; the spiral's middle at (ax, ay), outer radius r. Returns its coverage.</summary>
        private static float Scroll(float cx, float cy, float ax, float ay, float r, float half)
        {
            float px = cx - ax, py = cy - ay, rho = Mathf.Sqrt(px * px + py * py);
            if (rho > r + half + 1f) return 0f;
            const float turns = 2.6f * Mathf.PI; float perRad = (r - 1.4f) / turns;
            // the outer end at the top (towards the line: cy smaller); t = how far back along the spiral from that end
            float phi = Mathf.Atan2(py, px) + Mathf.PI / 2f; // 0 at the top
            float t0 = Mathf.Repeat(-phi, 2f * Mathf.PI);
            float best = 99f;
            for (int k = 0; k < 2; k++)
            {
                float t = t0 + 2f * Mathf.PI * k; if (t > turns) break;
                best = Mathf.Min(best, Mathf.Abs(rho - (r - perRad * t)));
            }
            return Cov(best - half);
        }

        private static Sprite MakeFrame()
        {
            const int N = 160; float band = WoodPx, under = UnderPx; const float g = 21f, rc = 13f, sx = g + rc + 14f, sy = g + 11.2f, sr = 9.6f;
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    float dxl = fx, dxr = N - fx, dyb = fy, dyt = N - fy;
                    float dx = Mathf.Min(dxl, dxr), dy = Mathf.Min(dyb, dyt), d = Mathf.Min(dx, dy);
                    Color c;
                    if (d > band + under) { px[y * N + x] = new Color(WoodDark.r, WoodDark.g, WoodDark.b, 0f); continue; }
                    bool horizontal = dy < dx; // the top or the bottom board
                    float side = horizontal ? (dyt < dyb ? 1f : -1f) : (dxl < dxr ? 0.6f : -0.6f); // lit from the top left
                    float along = horizontal ? fx : fy;
                    float grain = Noise(d * 0.9f + (horizontal ? 0f : 37f), along * 0.035f) * 0.6f + Noise(d * 2.3f + 11f, along * 0.012f + (horizontal ? 5f : 9f)) * 0.4f;
                    c = Mul(Wood, 0.86f + 0.3f * grain);
                    if (d > band) c = WoodDark;
                    else
                    {
                        c = Mix(c, side > 0 ? WoodLight : WoodDark, Mathf.Abs(side) * 0.75f * Mathf.Exp(-(d - 5.5f) * (d - 5.5f) / 9f)); // the outer bevel
                        c = Mix(c, WoodDark, Smooth(3.2f, 1.2f, d)); // the outer edge
                        c = Mix(c, side > 0 ? WoodDark : WoodLight, Mathf.Abs(side) * 0.55f * Mathf.Exp(-(d - band + 7.5f) * (d - band + 7.5f) / 7f)); // the inner bevel faces the other way
                        c = Mix(c, WoodDark, Smooth(band - 4.5f, band - 1.5f, d) * 0.85f); // the groove at the canvas
                        if (dx < band && dy < band) c = Mix(c, WoodDark, 0.5f * Cov(Mathf.Abs(dx - dy) * 0.7071f - 0.7f)); // the mitre
                        // the gold line, its corner rounded, a dark hairline on both sides
                        float sd = Mathf.Abs(RoundRect(fx - N / 2f, fy - N / 2f, N / 2f - g, N / 2f - g, rc));
                        c = Mix(c, WoodDark, 0.6f * Cov(sd - 3.5f));
                        float line = Cov(sd - 2.1f);
                        // the scrolls: one hanging from the line on each side of the corner, and a small diamond in the corner
                        float scroll = Mathf.Max(Scroll(dx, dy, sx, sy, sr, 1.6f), Scroll(dy, dx, sx, sy, sr, 1.6f));
                        float dia = Cov((Mathf.Abs(dx - 11.5f) + Mathf.Abs(dy - 11.5f)) * 0.7071f - 3.2f);
                        float shade = 0.6f * Mathf.Max(Mathf.Max(Scroll(dx - 0.9f, dy - 0.9f, sx, sy, sr, 2.3f), Scroll(dy - 0.9f, dx - 0.9f, sx, sy, sr, 2.3f)), Cov((Mathf.Abs(dx - 11.5f) + Mathf.Abs(dy - 11.5f)) * 0.7071f - 4.4f));
                        c = Mix(c, WoodDark, shade * (1f - line));
                        float gold = Mathf.Max(line, Mathf.Max(scroll, dia));
                        var gc = Mix(Gold, GoldLight, 0.55f * Mathf.Clamp01(1f - sd / 1.5f) + 0.25f * (side > 0 ? 1f : 0f));
                        c = Mix(c, gc, gold);
                    }
                    c.a = 1f; px[y * N + x] = c;
                }
            float b = band + under;
            return Make("frame", N, N, px, new Vector4(b, b, b, b));
        }

        private static Sprite MakeCanvas()
        {
            const int N = 96; float lea = LeatherPx; const float r = 9f;
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float fx = x + 0.5f - N / 2f, fy = y + 0.5f - N / 2f;
                    float sd = RoundRect(fx, fy, N / 2f, N / 2f, r), d = -sd;
                    Color c;
                    if (d < lea)
                    {
                        float n = Noise((x + 3) * 0.55f, (y + 7) * 0.55f);
                        c = Mul(Leather, 0.9f + 0.2f * n);
                        c = Mix(c, LeatherLight, 0.6f * Mathf.Exp(-(d - 6f) * (d - 6f) / 10f) * (fy > -fx ? 1f : 0.45f));
                        c = Mix(c, LeatherDark, Smooth(2.6f, 0.6f, d));
                        c = Mix(c, LeatherDark, Smooth(lea - 4.5f, lea - 0.5f, d) * 0.9f);
                    }
                    else c = Mix(Mul(CanvasShade, 0.9f), Canvas, Smooth(lea, lea + 13f, d));
                    c.a = Cov(sd); px[y * N + x] = c;
                }
            return Make("canvas", N, N, px, new Vector4(40, 40, 40, 40));
        }

        private static Sprite MakeCard()
        {
            const int N = 64; float m = CardShadowPx; const float r = 3.5f;
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float fx = x + 0.5f - N / 2f, fy = y + 0.5f - N / 2f;
                    float sd = RoundRect(fx, fy, N / 2f - m, N / 2f - m, r);
                    float shadow = 0.34f * Smooth(m - 1f, -2f, RoundRect(fx - 2.2f, fy + 2.6f, N / 2f - m, N / 2f - m, r + 1f));
                    float n = Noise(x * 0.45f, y * 0.45f);
                    Color c = Mix(Parchment, ParchmentEdge, Smooth(-7f - 3f * n, 0f, sd) * 0.85f);
                    c = Mix(c, Leather, 0.55f * Cov(Mathf.Abs(sd + 0.8f) - 0.8f));
                    float a = Cov(sd);
                    // paper over its shadow
                    Color o = new Color(Mathf.Lerp(0.08f, c.r, a), Mathf.Lerp(0.05f, c.g, a), Mathf.Lerp(0.02f, c.b, a), a + (1f - a) * shadow);
                    px[y * N + x] = o;
                }
            return Make("card", N, N, px, new Vector4(22, 22, 22, 22));
        }

        private static Sprite MakePlate()
        {
            const int W = 96, H = 48; const float notch = 7f;
            var px = new Color[W * H];
            var edge = Rgb(0x4A3209);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float fx = x + 0.5f - W / 2f, fy = y + 0.5f - H / 2f;
                    float hw = W / 2f - 1f, hh = H / 2f - 1f;
                    float sd = RoundRect(fx, fy, hw, hh, 2f);
                    float cx = Mathf.Abs(fx) - hw, cy = Mathf.Abs(fy) - hh;
                    sd = Mathf.Max(sd, notch - Mathf.Sqrt(cx * cx + cy * cy)); // the corners cut round
                    float t = (fy + hh) / (2f * hh); // 0 bottom, 1 top
                    Color c = t > 0.55f ? Mix(Gold, GoldLight, (t - 0.55f) / 0.45f * 0.9f) : Mix(Mix(GoldDark, Gold, 0.55f), Gold, t / 0.55f);
                    c = Mix(c, t > 0.5f ? GoldLight : GoldDark, 0.65f * Cov(Mathf.Abs(sd + 5.2f) - 0.7f)); // the engraved inner line
                    c = Mix(c, edge, Cov(Mathf.Abs(sd + 1.1f) - 1.1f));
                    c.a = Cov(sd); px[y * W + x] = c;
                }
            return Make("plate", W, H, px, new Vector4(22, 16, 22, 16));
        }

        /// <summary>kind 0: the soft square (white); 1: its thin rim (white); 2: the gold hover outline (its own colours).</summary>
        private static Sprite MakeSoft(int kind)
        {
            int N = kind == 2 ? 48 : 32; float r = kind == 2 ? 11f : 8f;
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float fx = x + 0.5f - N / 2f, fy = y + 0.5f - N / 2f;
                    float sd = RoundRect(fx, fy, N / 2f, N / 2f, r);
                    Color c = Color.white;
                    if (kind == 0) c.a = Cov(sd);
                    else if (kind == 1) c.a = Cov(Mathf.Abs(sd + 1.3f) - 1.3f);
                    else
                    {
                        float d = -sd; // the band: a dark gold hairline outside, gold, a light line inside
                        c = d < 1.4f ? GoldDark : d < 4.2f ? Mix(Gold, GoldLight, 0.25f) : GoldLight;
                        c = Mix(c, Mix(GoldDark, Color.black, 0.35f), Cov(d - 1.3f));
                        c.a = Cov(Mathf.Abs(sd + 2.9f) - 2.9f);
                    }
                    px[y * N + x] = c;
                }
            return kind == 2 ? Make("hoverRim", N, N, px, new Vector4(16, 16, 16, 16)) : Make(kind == 0 ? "soft" : "softRim", N, N, px, new Vector4(12, 12, 12, 12));
        }

        private static Sprite MakeDisc()
        {
            const int N = 96; const float r = N / 2f - 1f;
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float fx = x + 0.5f - N / 2f, fy = y + 0.5f - N / 2f;
                    float sd = Mathf.Sqrt(fx * fx + fy * fy) - r, d = -sd;
                    float v = Mathf.Lerp(0.84f, 1f, Mathf.Clamp01((fy + r) / (2f * r))); // lighter at the top
                    float hx = fx + r * 0.3f, hy = fy - r * 0.35f;
                    v = Mathf.Min(1f, v + 0.1f * Mathf.Exp(-(hx * hx + hy * hy) / (r * r * 0.12f)));
                    if (d < 3.6f) v = 0.5f; else if (d < 5.4f) v = Mathf.Lerp(0.5f, 1f, (d - 3.6f) / 1.8f);
                    px[y * N + x] = new Color(v, v, v, Cov(sd));
                }
            return Make("disc", N, N, px, Vector4.zero);
        }

        private static Sprite MakeRing()
        {
            const int N = 128; const float r = N / 2f - 1f;
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float fx = x + 0.5f - N / 2f, fy = y + 0.5f - N / 2f;
                    float sd = Mathf.Sqrt(fx * fx + fy * fy) - r, d = -sd;
                    Color c; float a;
                    if (d < 1.5f) { c = GoldDark; a = Cov(sd); }
                    else if (d < 5.2f) { c = Mix(Gold, GoldLight, fy > 0 ? 0.35f : 0f); a = 1f; }
                    else if (d < 6.4f) { c = GoldDark; a = 0.85f; }
                    else { c = Parchment; a = 0.6f; }
                    c.a = a; px[y * N + x] = c;
                }
            return Make("ring", N, N, px, Vector4.zero);
        }

        private static Sprite MakeSeeFrame()
        {
            const int N = 112; const float g = 6f, rc = 11f;
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    float dx = Mathf.Min(fx, N - fx), dy = Mathf.Min(fy, N - fy);
                    float sd = Mathf.Abs(RoundRect(fx - N / 2f, fy - N / 2f, N / 2f - g, N / 2f - g, rc));
                    float line = Cov(sd - 1.5f);
                    float scroll = Mathf.Max(Scroll(dx, dy, g + rc + 11f, g + 8.2f, 6.8f, 1.2f), Scroll(dy, dx, g + rc + 11f, g + 8.2f, 6.8f, 1.2f));
                    float gold = Mathf.Max(line, scroll);
                    float dark = Mathf.Max(Cov(sd - 3f), Mathf.Max(Scroll(dx, dy, g + rc + 11f, g + 8.2f, 6.8f, 2.4f), Scroll(dy, dx, g + rc + 11f, g + 8.2f, 6.8f, 2.4f))) * 0.55f; // a dark edge: seen on a bright sky too
                    Color c = Mix(Mul(WoodDark, 0.6f), Mix(Gold, GoldLight, 0.4f), gold);
                    c.a = Mathf.Max(gold, dark); px[y * N + x] = c;
                }
            return Make("seeFrame", N, N, px, new Vector4(46, 46, 46, 46));
        }
    }

    /// <summary>An Image whose picture stands <see cref="Outset"/> px out of its rectangle on every side (the wood frame around a panel, a
    /// card's shadow): the panel's own rectangle, and every code that resizes its "border" and "bg", stay what they were.</summary>
    public sealed class OutsetImage : Image
    {
        private float _outset, _joinPad; private Sides _join;
        public float Outset { get => _outset; set { if (!Mathf.Approximately(_outset, value)) { _outset = value; SetVerticesDirty(); } } }
        /// <summary>Sides on which nothing stands out: the picture ends <see cref="JoinPad"/> px outside its rectangle there (a facet joined
        /// to its neighbour: the frame's board on that side is not drawn, the top and bottom boards run to the fold).</summary>
        public Sides Join { get => _join; set { if (_join != value) { _join = value; SetVerticesDirty(); } } }
        public float JoinPad { get => _joinPad; set { _joinPad = value; SetVerticesDirty(); } }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            base.OnPopulateMesh(vh);
            if ((_outset == 0f && _join == Sides.None) || vh.currentVertCount == 0) return;
            var r = GetPixelAdjustedRect(); float mx = r.center.x, my = r.center.y;
            // every vertex moves away from the middle: the edges move out, the 9-slice's borders keep their width
            UIVertex v = default;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                var p = v.position;
                if (p.x > mx + 0.01f) p.x = (_join & Sides.Right) != 0 ? r.xMax + _joinPad : p.x + _outset; else if (p.x < mx - 0.01f) p.x = (_join & Sides.Left) != 0 ? r.xMin - _joinPad : p.x - _outset;
                if (p.y > my + 0.01f) p.y = (_join & Sides.Top) != 0 ? r.yMax + _joinPad : p.y + _outset; else if (p.y < my - 0.01f) p.y = (_join & Sides.Bottom) != 0 ? r.yMin - _joinPad : p.y - _outset;
                v.position = p; vh.SetUIVertex(v, i);
            }
        }
    }

    /// <summary>Dashed stitching: along the edges of its rectangle (<see cref="Inset"/> px inside) or, <see cref="Line"/>, one dashed line
    /// along its longer middle. Drawn as quads: crisp at any size, no picture.</summary>
    public sealed class StitchGraphic : MaskableGraphic
    {
        private float _inset, _dash = 9f, _gap = 6f, _wide = 2f; private bool _line; private Sides _skip;
        /// <summary>Sides without stitching (a facet joined to its neighbour): the other sides' stitching runs to that edge.</summary>
        public Sides Skip { get => _skip; set { if (_skip != value) { _skip = value; SetVerticesDirty(); } } }
        public float Inset { get => _inset; set { _inset = value; SetVerticesDirty(); } }
        public float Dash { get => _dash; set { _dash = Mathf.Max(1f, value); SetVerticesDirty(); } }
        public float Gap { get => _gap; set { _gap = Mathf.Max(0.5f, value); SetVerticesDirty(); } }
        public float Wide { get => _wide; set { _wide = Mathf.Max(0.5f, value); SetVerticesDirty(); } }
        public bool Line { get => _line; set { _line = value; SetVerticesDirty(); } }
        /// <summary>Stitches drawn at the last rebuild (smoke).</summary>
        public int Stitches { get; private set; }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); Stitches = 0;
            var r = GetPixelAdjustedRect();
            if (_line)
            {
                if (r.width >= r.height) Run(vh, new Vector2(r.xMin, r.center.y), new Vector2(r.xMax, r.center.y));
                else Run(vh, new Vector2(r.center.x, r.yMax), new Vector2(r.center.x, r.yMin));
                return;
            }
            bool sl = (_skip & Sides.Left) != 0, sr = (_skip & Sides.Right) != 0, st = (_skip & Sides.Top) != 0, sb = (_skip & Sides.Bottom) != 0;
            float x0 = sl ? r.xMin : r.xMin + _inset, x1 = sr ? r.xMax : r.xMax - _inset, y0 = sb ? r.yMin : r.yMin + _inset, y1 = st ? r.yMax : r.yMax - _inset;
            if (x1 - x0 < _dash || y1 - y0 < _dash) return;
            float c = _dash * 0.8f; // the corners stay open: a stitch never bends
            float cl = sl ? 0f : c, cr = sr ? 0f : c, cb = sb ? 0f : c, ct = st ? 0f : c;
            if (!st) Run(vh, new Vector2(x0 + cl, y1), new Vector2(x1 - cr, y1));
            if (!sb) Run(vh, new Vector2(x0 + cl, y0), new Vector2(x1 - cr, y0));
            if (!sl) Run(vh, new Vector2(x0, y0 + cb), new Vector2(x0, y1 - ct));
            if (!sr) Run(vh, new Vector2(x1, y0 + cb), new Vector2(x1, y1 - ct));
        }

        private void Run(VertexHelper vh, Vector2 a, Vector2 b)
        {
            float len = Vector2.Distance(a, b); if (len < _dash * 0.5f) return;
            int n = Mathf.Max(1, Mathf.FloorToInt((len + _gap) / (_dash + _gap)));
            if (n > 400) n = 400;
            float gap = n > 1 ? (len - n * _dash) / (n - 1) : 0f; // the stitches spread to end exactly at both ends
            Vector2 dir = (b - a) / len, nrm = new Vector2(-dir.y, dir.x) * (_wide * 0.5f);
            Color32 col = color;
            for (int i = 0; i < n; i++)
            {
                Vector2 s = a + dir * (i * (_dash + gap)), e = s + dir * Mathf.Min(_dash, len);
                int at = vh.currentVertCount;
                vh.AddVert(s - nrm, col, Vector2.zero); vh.AddVert(s + nrm, col, Vector2.zero); vh.AddVert(e + nrm, col, Vector2.zero); vh.AddVert(e - nrm, col, Vector2.zero);
                vh.AddTriangle(at, at + 1, at + 2); vh.AddTriangle(at, at + 2, at + 3);
                Stitches++;
            }
        }
    }

    /// <summary>The text every <see cref="UiKit.Label"/> is: a uGUI Text that, on a surface of the theme, DRAWS each letter in a colour
    /// readable on what is behind it (<see cref="Theme.Readable"/>: the surface, or the button / box it is a child of). Its
    /// <c>color</c> and <c>text</c> stay what the code set (the dark panels' light colours, rich-text tags, the game data's codes):
    /// only the drawn letters change. Off a themed surface (a dark panel of waves 1-20, the ring, a bare HUD piece) it is a plain Text.</summary>
    public sealed class ThemedText : Text
    {
        private bool _resolved; private UiSurface _surface; private Image _behind; private bool _faint;
        /// <summary>A switched-off text (a button that cannot be pressed now): drawn in the faint ink.</summary>
        public bool Faint { get => _faint; set { if (_faint != value) { _faint = value; SetVerticesDirty(); } } }
        /// <summary>The surface it stands on (null: none).</summary>
        public UiSurface Surface { get { Resolve(); return _surface; } }

        private void Resolve()
        {
            if (_resolved) return;
            _resolved = true; _surface = null; _behind = null;
            for (var t = transform.parent; t != null; t = t.parent)
            {
                var s = t.GetComponent<UiSurface>();
                if (s != null) { _surface = s; break; }
                if (_behind == null) { var img = t.GetComponent<Image>(); if (img != null) _behind = img; }
            }
        }
        protected override void OnTransformParentChanged() { base.OnTransformParentChanged(); _resolved = false; }
        protected override void OnEnable() { _resolved = false; base.OnEnable(); } // (moved to another panel while hidden: no message came)

        /// <summary>What is behind the letters now (false: no themed surface: the letters are drawn as they are).</summary>
        public bool Backdrop(out Color back)
        {
            Resolve(); back = Color.clear;
            if (_surface == null || !_surface.Themed || _surface.Kind == Loki.UI.Surface.Clear) return false;
            back = _surface.Color;
            if (_behind != null && _behind.enabled && _behind.gameObject.activeInHierarchy) back = Theme.Over(_behind is ThemedImage ti ? ti.Drawn : _behind.color, back);
            return true;
        }
        /// <summary>The colour a letter of colour <paramref name="c"/> is drawn with now.</summary>
        public Color32 Drawn(Color32 c) => Backdrop(out var back) ? Theme.Readable(c, back, _faint) : c;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            base.OnPopulateMesh(vh);
            if (vh.currentVertCount == 0 || !Backdrop(out var back)) return;
            UIVertex v = default; Color32 last = default, mapped = default; bool have = false;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                var c = v.color;
                if (!have || c.r != last.r || c.g != last.g || c.b != last.b) { last = c; mapped = Theme.Readable(c, back, _faint); have = true; }
                if (mapped.r != c.r || mapped.g != c.g || mapped.b != c.b) { v.color = new Color32(mapped.r, mapped.g, mapped.b, c.a); vh.SetUIVertex(v, i); }
            }
        }
    }

    /// <summary>The image every <see cref="UiKit.Box"/> is: a plain box (or one of the theme's white pictures) that, on a light surface of
    /// the theme, is DRAWN in the theme's counterpart of its colour (<see cref="Theme.Fill"/>). Its <c>color</c> stays what the code
    /// set. <see cref="Raw"/>: drawn as it is (a colour that is data: a row's colour, a swatch).</summary>
    public sealed class ThemedImage : Image
    {
        private bool _resolved, _raw; private UiSurface _surface;
        public bool Raw { get => _raw; set { if (_raw != value) { _raw = value; SetVerticesDirty(); } } }
        private bool Applies
        {
            get
            {
                if (_raw) return false;
                if (!_resolved) { _resolved = true; _surface = Theme.SurfaceOf(transform.parent); }
                if (_surface == null || !_surface.Light) return false;
                var sp = sprite;
                return sp == null || sp == Theme.Soft || sp == Theme.SoftRim || sp == Theme.Disc;
            }
        }
        protected override void OnTransformParentChanged() { base.OnTransformParentChanged(); _resolved = false; }
        protected override void OnEnable() { _resolved = false; base.OnEnable(); } // (moved to another panel while hidden: no message came)
        /// <summary>The colour it is drawn with now.</summary>
        public Color Drawn
        {
            get
            {
                if (!Applies) return color;
                var c = color; var f = Theme.Fill(c);
                // a thin line (a rule, a divider: 4.5 px or less): leather brown unless it is dark already (gold and white lines vanish on cream)
                var r = rectTransform.rect;
                if (c.a >= 0.3f && Mathf.Min(Mathf.Abs(r.width), Mathf.Abs(r.height)) <= 4.5f && UiKit.Luminance(f) > 0.25f) return new Color(Theme.Leather.r, Theme.Leather.g, Theme.Leather.b, Mathf.Max(0.6f, c.a));
                return f;
            }
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            base.OnPopulateMesh(vh);
            if (vh.currentVertCount == 0 || !Applies) return;
            Color32 want = Drawn, have = color;
            if (want.r == have.r && want.g == have.g && want.b == have.b && want.a == have.a) return;
            UIVertex v = default;
            for (int i = 0; i < vh.currentVertCount; i++) { vh.PopulateUIVertex(ref v, i); v.color = want; vh.SetUIVertex(v, i); }
        }
    }

    /// <summary>The small gold title plate on a panel's top edge: as wide as its text (between <see cref="MinW"/> and <see cref="MaxW"/>).</summary>
    public sealed class UiTitlePlate : MonoBehaviour
    {
        public Text Text { get; private set; }
        public float MinW = 220f, MaxW = 700f, PadX = 34f;
        private RectTransform _rt; private float _h; private string _last;
        internal void Init(RectTransform rt, Text text, float height) { _rt = rt; Text = text; _h = height; }
        public void Set(string s) { if (Text != null) { Text.text = s ?? ""; Fit(); } }
        public void Fit()
        {
            if (Text == null || _rt == null) return;
            _last = Text.text;
            bool show = !string.IsNullOrEmpty(UiKit.StripTags(_last));
            var img = _rt.GetComponent<Image>(); if (img != null) img.enabled = show;
            float w = Mathf.Clamp(Text.preferredWidth + 2f * PadX, MinW, MaxW);
            _rt.sizeDelta = new Vector2(w, _h);
            Text.rectTransform.sizeDelta = new Vector2(w - 2f * PadX + 8f, _h - 6f);
        }
        private void LateUpdate() { if (Text != null && !ReferenceEquals(Text.text, _last) && Text.text != _last) Fit(); }
    }
}
