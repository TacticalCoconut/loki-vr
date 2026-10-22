using System.Collections.Generic;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 12 (w12cast): one piece of the wand's wheel: the part of a ring between two straight edges, filled, with an
    /// outline inside its rim (the sketch's dark outlines) and, inside the outline, a band of the piece's own colour drawn more
    /// solid than the fill (so the colour is still told over a bright scene, where a see-through fill pales). Angles in degrees clockwise from straight up, lengths in the
    /// panel's pixels; each straight edge stands <c>off</c> in from the line through the middle (so a gap between two pieces
    /// has parallel sides, as drawn). A disc is the same from the middle all the way round. No raycast target, no collider.
    /// </summary>
    public sealed class WheelShape : MaskableGraphic
    {
        private float _a0, _a1, _off0, _off1, _r0, _r1, _line, _band; private bool _disc; private Color32 _fill, _edge, _rim;
        public Color32 FillColour => _fill;
        public Color32 EdgeColour => _edge;
        public Color32 RimColour => _rim;
        /// <summary>Client wave 22 (w22clips): the piece's own shape, for the tutorial clips' recorder (LokiGame.W22ClipsRec.cs): from / to
        /// (degrees clockwise from straight up), the inner and the outer radius (the panel's pixels), a whole disc.</summary>
        public float AngleFrom => _a0;
        public float AngleTo => _a1;
        public float RadiusIn => _disc ? 0f : _r0;
        public float RadiusOut => _r1;
        public bool IsDisc => _disc;

        public static WheelShape Create(RectTransform parent, string name, float a0, float a1, float off0, float off1, float r0, float r1, float line, bool disc, float band = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero; rt.sizeDelta = Vector2.one * (r1 * 2f);
            var s = go.AddComponent<WheelShape>();
            s._a0 = a0; s._a1 = a1; s._off0 = off0; s._off1 = off1; s._r0 = r0; s._r1 = r1; s._line = line; s._disc = disc; s._band = band;
            s.raycastTarget = false; if (UiKit.Overlay != null) s.material = UiKit.Overlay;
            return s;
        }

        private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        /// <summary>The fill, the outline, and the band of colour inside the outline (a piece made without a band has none).</summary>
        public void Paint(Color fill, Color edge, Color rim)
        {
            Color32 f = fill, e = edge, r = rim;
            if (Same(f, _fill) && Same(e, _edge) && Same(r, _rim)) return;
            _fill = f; _edge = e; _rim = r; SetVerticesDirty();
        }
        public void Paint(Color fill, Color edge) => Paint(fill, edge, fill);

        private static Vector2 Polar(float r, float deg) { float a = deg * Mathf.Deg2Rad; return new Vector2(Mathf.Sin(a) * r, Mathf.Cos(a) * r); }
        /// <summary>How many degrees an edge standing <paramref name="off"/> in from the line through the middle is turned at radius r.</summary>
        private static float Lean(float off, float r) => r <= 1e-3f || off <= 0f ? 0f : Mathf.Asin(Mathf.Clamp01(off / r)) * Mathf.Rad2Deg;
        private Vector2 At(float r, float off0, float off1, float t) => Polar(r, Mathf.Lerp(_a0 + Lean(off0, r), _a1 - Lean(off1, r), t));

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            int n = Mathf.Max(3, Mathf.CeilToInt((_a1 - _a0) / 5f));
            // three outlines of the piece, each inside the last: its rim (p), the outline's inner side (q), the band's inner side (s)
            float w = _line, v = _line + _band;
            float qi = _disc ? 0f : _r0 + w, qo = _r1 - w, q0 = _disc ? 0f : _off0 + w, q1 = _disc ? 0f : _off1 + w;
            float si = _disc ? 0f : _r0 + v, so = _r1 - v, s0 = _disc ? 0f : _off0 + v, s1 = _disc ? 0f : _off1 + v;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                Vector2 pin = At(_r0, _off0, _off1, t), qin = At(qi, q0, q1, t), sin = At(si, s0, s1, t), sout = At(so, s0, s1, t), qout = At(qo, q0, q1, t), pout = At(_r1, _off0, _off1, t);
                vh.AddVert(pin, _edge, Vector2.zero); vh.AddVert(qin, _edge, Vector2.zero);   // 0, 1: the outline along the inner rim
                vh.AddVert(qin, _rim, Vector2.zero); vh.AddVert(sin, _rim, Vector2.zero);     // 2, 3: the band along the inner rim
                vh.AddVert(sin, _fill, Vector2.zero); vh.AddVert(sout, _fill, Vector2.zero);  // 4, 5: the fill
                vh.AddVert(sout, _rim, Vector2.zero); vh.AddVert(qout, _rim, Vector2.zero);   // 6, 7: the band along the outer rim
                vh.AddVert(qout, _edge, Vector2.zero); vh.AddVert(pout, _edge, Vector2.zero); // 8, 9: the outline along the outer rim
            }
            void Strip(int b, int nb, int k) { vh.AddTriangle(b + k, b + k + 1, nb + k + 1); vh.AddTriangle(b + k, nb + k + 1, nb + k); }
            for (int i = 0; i < n; i++)
            {
                int b = i * 10, nb = b + 10;
                if (!_disc && w > 0f) Strip(b, nb, 0);
                if (!_disc && _band > 0f) Strip(b, nb, 2);
                Strip(b, nb, 4);
                if (_band > 0f) Strip(b, nb, 6);
                if (w > 0f) Strip(b, nb, 8);
            }
            if (!_disc)
                foreach (int b in new[] { 0, n * 10 }) // the two straight edges: the outline, then the band
                {
                    if (w > 0f) { vh.AddTriangle(b, b + 9, b + 8); vh.AddTriangle(b, b + 8, b + 1); }
                    if (_band > 0f) { vh.AddTriangle(b + 2, b + 7, b + 6); vh.AddTriangle(b + 2, b + 6, b + 3); }
                }
        }
    }

    /// <summary>
    /// Client wave 12 (w12cast): the picture of the wand's two-tier wheel, as sketched on 2026-10-06, 21:40
    /// (design notes; the shape and which piece the tip is on: Game/WandWheelGeo.cs; the taking:
    /// LokiGame.W12Wand.cs). It stands where the wand's tip was when the trigger went down, facing the player, and stays there.
    ///   The main wheel  a white middle, four tan inner segments (up, right, down, left: the wand's four quick skills, each with
    ///                   its icon, its name and the seconds of a cooldown), four dark red outer segments (the four rows of the
    ///                   shown hotbar: "Row n" and the icons of the first four things on it), gaps between the quadrants, dark
    ///                   outlines.
    ///   A sub-wheel     beside the main wheel on the side of the outer segment the tip touched, joined to it by two fanning
    ///                   lines; a white middle and four tan wedges each split in two: the row's slots 1 to 8, clockwise from the
    ///                   top (each with its number, icon, name, an item's amount, a cooldown's seconds).
    ///   Slices, items   client wave 18 (w18wand), points 14 and 13: the inner ring has "Wand slices" pieces (2-8; four: the quadrants above;
    ///                   other counts: Game/WandWheelGeo.cs Slices, the icon and the name fitted inside each piece: InnerSlot), rebuilt at an
    ///                   opening when the count changed; a piece may hold an item (its icon, its name, the amount in the bag; no cooldown).
    ///   In place        client wave 14 (w14cast), point 14 ("Wand rows: in place", the default): the same row's wheel drawn IN the main
    ///                   wheel's place (its middle on the wheel's middle), the main wheel and the fanning lines hidden, "Row n" in its
    ///                   white middle; the tip's dot counts from that middle (<see cref="Draw"/>'s inPlace). Client wave 16 (w16cast), point
    ///                   3: the picture never moves for it (at the wand neither): the row's wheel stands on the first wheel's middle.
    ///   See-through     by the design's rule of 21:50 ("The wheels need to be partially transparent (except the name and icon of the
    ///                   skills)"): the fills, the white middles, the outlines and the fanning lines are drawn with the alphas
    ///                   below; the piece under the tip is brighter and more solid. Inside each piece's outline runs a band of
    ///                   its own colour, <see cref="BandPx"/> pixels (3 mm) wide and more solid than the fill: over a bright
    ///                   scene a see-through tan and a see-through dark red both pale to nearly the same, and the band keeps
    ///                   the sketch's two colours apart. Icons and texts are drawn fully opaque, the texts with a black
    ///                   outline (legible over a bright and over a dark scene without a plate behind them).
    ///   Not a panel     ("they need to be excluded from the 'UI elements block gaze selection' system"): made without a
    ///                   background (UiKit.Panel background false): no collider, no UiBlocker, no UiButton: the laser's and the
    ///                   look's rays (VR/DioramaRig.cs, Game/Targeting.cs: Physics rays against colliders) pass through it.
    ///   In front        client wave 13 (w13view), the brief's third item (design note, 2026-10-07: "when the player holds the trigger, the
    ///                   display itself shows up in front of the vision. Not on the wand, even though the control itself is still made
    ///                   relative to where the player held the trigger ... This should be the default setting"): with "Wand wheel
    ///                   shown: in front of you" (VR/Comfort.cs WandWheelFront) the same picture is the HEAD's child (head-locked:
    ///                   playtesting confirmed it follows the head) at <see cref="FrontDefault"/>: <see cref="FrontDistance"/> m from the
    ///                   eyes, its middle <see cref="FrontDown"/> degrees under the line of sight, facing the eyes, drawn at
    ///                   <see cref="FrontScale"/> of life size. The tip's dot is the cursor (drawn <see cref="FrontDot"/> times as
    ///                   large there): it sits where the tip is relative to where the trigger went down, moving FrontScale times
    ///                   as far as the tip. Only the picture moves: the control is the tip's own movement in the plane the wheel
    ///                   would have had at the wand (LokiGame.W12Wand.cs), the same sizes in cm, the same keeping and ticks. The
    ///                   player may put the picture elsewhere and resize it (the overhead menu's Reposition: VR/Comfort.cs
    ///                   WheelOffset / WheelScale). Still no collider: the look locks through it.
    ///   Row colours     client wave 20 (w20colors), the design's point 51: the rows' outer segments and the open row's cells in the rows' own colours
    ///                   (UI/RowColours.cs; <see cref="RowLitWhite"/>).
    ///   Its own size    client wave 23 (w23wand), the design's answer 55 ("Wheel should be resizable on its own, not according to the wand"): the
    ///                   picture AT THE WAND is drawn at the share of life size <see cref="Open"/> is handed (VR/Comfort.cs WheelScale:
    ///                   Settings "Wheel size", Reposition's two grips; 1 = the 22 cm of waves 12-22), about its middle on the tip;
    ///                   <see cref="Draw"/>'s tip is in the wheel's OWN metres (life size) as always, so the dot stands on the tip at every
    ///                   size. In front of the eyes the same value sizes the picture (<see cref="ApplyFrontPlace"/>, placed or not).
    ///   On the tip      the same answer ("Spawning the secondary wheel at the wand tip avoids offset issues"): at the wand
    ///                   a row's wheel opens centred on the tip as it is then: the whole picture goes there (<see cref="Recentre"/>).
    /// </summary>
    public sealed class WandWheel : MonoBehaviour
    {
        public const float Scale = 0.0003f, LinePx = 7f, BandPx = 10f;
        /// <summary>The picture in front of the eyes (client wave 13): its distance, how far under the line of sight its middle is, the
        /// share of life size it is drawn at, how much larger the tip's dot is drawn there.</summary>
        public const float FrontDistance = 0.60f, FrontDown = 11f, FrontScale = 0.70f, FrontDot = 2.2f;
        /// <summary>The front picture is drawn after the panels (10) and the normal wand (11): a note shown under the line of sight
        /// (UI/LokiHud.cs, 14 degrees down) never covers it; at the wand it keeps the panels' order.</summary>
        public const int FrontSortingOrder = 12, WandSortingOrder = 10;
        /// <summary>The default place in the head's frame (metres: right, up, ahead).</summary>
        public static Vector3 FrontDefault => Quaternion.Euler(FrontDown, 0f, 0f) * Vector3.forward * FrontDistance;
        private Transform _home; private float _factor = 1f;
        /// <summary>The picture is in front of the eyes now (else at the wand); the size it is drawn at (a share of life size; at the
        /// wand: the size <see cref="Open"/> was handed, the wheel's own: client wave 23, w23wand); its place in the head's frame.</summary>
        public bool Front { get; private set; }
        public float FrontFactor => _factor;
        public Vector3 FrontPlace => _rt.localPosition;
        /// <summary>Shown for Reposition (the main wheel only, no trigger held).</summary>
        public bool Placing { get; private set; }
        /// <summary>Where the tip's dot is drawn, in the wheel's own plane (metres of its life-size geometry, whatever size it is drawn at).</summary>
        public Vector2 DotPlane => _dot.rectTransform.anchoredPosition * Scale;
        public bool DotShown => _dot.gameObject.activeInHierarchy;
        /// <summary>The sketch's own colours (sampled from the picture: tan 185, 122, 87; dark red 136, 0, 21) and how solid each
        /// part is drawn: a fill, the fill of a slot with nothing on it, the white middles, the outlines, the piece under the
        /// tip, the fanning lines and the pale edge that keeps them visible over a dark scene.</summary>
        public static readonly Color Tan = new Color32(185, 122, 87, 255), Red = new Color32(136, 0, 21, 255), LitTan = new Color(1f, 0.84f, 0.45f), LitRed = new Color(0.90f, 0.20f, 0.24f);
        public const float TanAlpha = 0.50f, RedAlpha = 0.55f, BandAlpha = 0.85f, EmptyAlpha = 0.28f, CentreAlpha = 0.22f, EdgeAlpha = 0.70f, LitAlpha = 0.88f, FanAlpha = 0.70f, FanHaloAlpha = 0.30f;
        /// <summary>Client wave 20 (w20colors), the design's point 51 and answer ("Yes": the wand wheel's rows too): the rows are drawn in their colours
        /// (UI/RowColours.cs): a row's outer segment filled (at <see cref="RedAlpha"/>) and banded (<see cref="BandAlpha"/>) in it, lit (the tip on
        /// it, or its row open) <see cref="RowLitWhite"/> of the way to white at <see cref="LitAlpha"/>; the open row's cells banded in it (their
        /// fill stays tan, the chosen one gold-tan) and its white middle tinted with it at <see cref="RowCentreAlpha"/>. The inner slices (the
        /// wand's own quick slots, no row) stay tan.</summary>
        public const float RowLitWhite = 0.35f, RowCentreAlpha = 0.35f;
        private RectTransform _rt, _main, _sub, _fan; private WheelShape _centre, _subCentre, _dot; private CastLook.State _look; // (client wave 21, w21tools: the look in force, UI/CastLook.cs)
        private readonly WheelShape[] _inner = new WheelShape[MaxSlices], _outer = new WheelShape[4], _cells = new WheelShape[8];
        private sealed class SlotUi { public Image Icon; public Text Name, Cool, Count; }
        private readonly SlotUi[] _quickUi = new SlotUi[MaxSlices], _cellUi = new SlotUi[8];
        private readonly Text[] _rowLabel = new Text[4]; private readonly Image[,] _rowIcons = new Image[4, 4];
        private readonly List<RectTransform> _fanLines = new List<RectTransform>();
        private Hotkeys _keys; private Inventory _inv; private Progression _prog; private QuickBar _bar;
        private readonly int[] _quick = new int[MaxSlices]; private int _tab, _region = -2, _subDir = -1; private bool _dirty, _subOn, _inPlace;
        // ---- client wave 18 (w18wand), points 13 and 14: the inner ring's slices (built for <see cref="BuiltSlices"/>; rebuilt at the next
        // opening when "Wand slices" changed), and which of them hold an ITEM (its icon, name and the amount in the bag; no cooldown)
        public const int MaxSlices = World.UserSettings.MaxWandSlices;
        private readonly bool[] _quickItem = new bool[MaxSlices], _nameOff = new bool[MaxSlices];
        private int _slices;
        /// <summary>How many inner slices the picture is built with now.</summary>
        public int BuiltSlices => _slices;
        /// <summary>An item's amount beside its icon, from the icon's middle (cm at the icon's full 70 px; the cells' own offset).</summary>
        private static readonly Vector2 CountOffCm = new Vector2(0.95f, -0.3f);
        private Text _subTitle;
        /// <summary>Client wave 14 (w14cast): the row's wheel is drawn in the main wheel's place now.</summary>
        public bool InPlace => SubShown && _inPlace;
        public bool Shown => _rt != null && _rt.gameObject.activeSelf;
        /// <summary>The main wheel / a sub-wheel is drawn now; the side of the sub-wheel (0 up, 1 right, 2 down, 3 left); the
        /// region drawn as under the tip (Game/WandWheelGeo.cs numbering; -1 none).</summary>
        public bool MainShown => Shown && _main.gameObject.activeSelf;
        public bool SubShown => Shown && _sub.gameObject.activeSelf;
        public int SubDir => _subDir;
        public int LitRegion => _region;

        private const float Cm = 0.01f / Scale; // the panel's pixels in a centimetre

        public static WandWheel Create(Transform parent, Hotkeys keys, Inventory inv, Progression prog, QuickBar bar)
        {
            float px = 1f / Scale, side = (WandWheelGeo.SubDistance + WandWheelGeo.SubEnd + 0.06f) * 2f * px;
            var rt = UiKit.Panel("WandWheel", parent, new Vector2(side, side), Scale, false); // no background: no collider, no UiBlocker
            var w = rt.gameObject.AddComponent<WandWheel>();
            w._rt = rt; w._keys = keys; w._inv = inv; w._prog = prog; w._bar = bar;
            // the fanning lines first (under the wheels), then the main wheel, the sub-wheel, the tip's dot on top
            w._fan = Group(rt, "fan");
            float sy = (WandWheelGeo.OuterEnd + 0.004f) * Mathf.Cos(20f * Mathf.Deg2Rad) / 0.01f, sx = (WandWheelGeo.OuterEnd + 0.004f) * Mathf.Sin(20f * Mathf.Deg2Rad) / 0.01f;
            float ey = (WandWheelGeo.SubDistance + 0.010f) / 0.01f, ex = (WandWheelGeo.SubEnd + 0.045f) / 0.01f;
            foreach (float sgn in new[] { -1f, 1f })
            {
                Vector2 a = new Vector2(sgn * sx, sy) * Cm, b = new Vector2(sgn * ex, ey) * Cm;
                Bar(w._fan, "halo", a, b, 15f, new Color(1f, 0.96f, 0.86f, FanHaloAlpha));
                w._fanLines.Add(Bar(w._fan, "line", a, b, 7f, new Color(0.06f, 0.04f, 0.04f, FanAlpha)));
            }
            w._main = Group(rt, "main");
            w._centre = WheelShape.Create(w._main, "centre", 0f, 360f, 0f, 0f, 0f, (WandWheelGeo.Centre - 0.0015f) * px, 0f, true);
            for (int d = 0; d < 4; d++)
            {
                w._inner[d] = w.InnerShape(4, d); // (client wave 18: built for four slices; rebuilt at an opening when "Wand slices" says otherwise)
                w._outer[d] = WheelShape.Create(w._main, "outer" + d, d * 90f - 45f, d * 90f + 45f, WandWheelGeo.Gap * 0.5f * px, WandWheelGeo.Gap * 0.5f * px, WandWheelGeo.OuterStart * px, WandWheelGeo.OuterEnd * px, LinePx, false, BandPx);
            }
            w._slices = 4;
            for (int d = 0; d < 4; d++)
            {
                // an inner segment: the quick skill's icon towards the middle, its name on the wide side (left / right: under the icon)
                bool vertical = (d & 1) == 0; float s = d == 0 || d == 1 ? 1f : -1f;
                w._quickUi[d] = w.InnerSlot(4, d);
                // an outer segment: "Row n" and the first four things on that row
                Vector2 labelAt = vertical ? new Vector2(0f, s * 10.25f) : new Vector2(s * 8.6f, 3.9f);
                w._rowLabel[d] = Lettering(w._main, labelAt * Cm, new Vector2((vertical ? 3.6f : 3.0f) * Cm, 0.95f * Cm), 27, 16, new Color(1f, 0.93f, 0.78f));
                for (int i = 0; i < 4; i++)
                {
                    float k = (i - 1.5f) * (vertical ? 1.7f : -1.55f);
                    w._rowIcons[d, i] = UiKit.Icon(w._main, (vertical ? new Vector2(k, s * 8.65f) : new Vector2(s * 9.2f, k)) * Cm, new Vector2(50f, 50f));
                }
            }
            w._sub = Group(rt, "sub");
            w._subCentre = WheelShape.Create(w._sub, "centre", 0f, 360f, 0f, 0f, 0f, (WandWheelGeo.SubCentre - 0.0015f) * px, 0f, true);
            for (int c = 0; c < 8; c++)
                w._cells[c] = WheelShape.Create(w._sub, "cell" + c, c * 45f, c * 45f + 45f, WandWheelGeo.CellOff0(c) * px, WandWheelGeo.CellOff1(c) * px, WandWheelGeo.SubCentre * px, WandWheelGeo.SubEnd * px, LinePx, false, BandPx);
            for (int c = 0; c < 8; c++)
            {
                var u = WandWheelGeo.Dir(c * 45f + 22.5f); var at = u * 6.2f;
                w._cellUi[c] = Slot(w._sub, (at + new Vector2(0f, 0.4f)) * Cm, 66f, (at + new Vector2(0f, -1.15f)) * Cm, new Vector2(3.5f * Cm, 1.25f * Cm), 20, 40, new Vector2(0.95f, -0.3f) * Cm);
                Lettering(w._sub, u * 3.55f * Cm, new Vector2(34f, 30f), 22, 22, new Color(1f, 0.93f, 0.78f)).text = (c + 1).ToString();
            }
            w._subTitle = Lettering(w._sub, Vector2.zero, new Vector2(4.6f * Cm, 1.2f * Cm), 24, 16, new Color(1f, 0.93f, 0.78f)); // client wave 14: "Row n" in its middle, in place only
            w._dot = WheelShape.Create(rt, "tip", 0f, 360f, 0f, 0f, 0f, 11f, 3f, true); w._dot.Paint(new Color(1f, 0.95f, 0.6f, 1f), new Color(0f, 0f, 0f, 0.9f));
            if (keys != null) keys.Changed += () => w._dirty = true;
            if (inv != null) inv.Changed += () => w._dirty = true;
            if (prog != null) prog.Changed += () => w._dirty = true;
            w._look = CastLook.Capture(rt); // client wave 21 (w21tools): built plain; drawn in the look in force from the first Draw
            rt.gameObject.SetActive(false);
            return w;
        }

        private static RectTransform Group(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero; rt.sizeDelta = Vector2.zero;
            return rt;
        }

        /// <summary>A straight bar from a to b (pixels).</summary>
        private static RectTransform Bar(RectTransform parent, string name, Vector2 a, Vector2 b, float width, Color colour)
        {
            var box = UiKit.Box(parent, name, (a + b) * 0.5f, new Vector2((b - a).magnitude, width), colour);
            box.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
            return box;
        }

        /// <summary>An opaque text with a black outline: legible over a bright and over a dark scene, with no plate behind it.</summary>
        private static Text Lettering(RectTransform parent, Vector2 at, Vector2 size, int font, int min, Color colour)
        {
            var t = UiKit.Label(parent, "", at, size, font, TextAnchor.MiddleCenter, colour);
            t.resizeTextForBestFit = min < font; t.resizeTextMinSize = min; t.resizeTextMaxSize = font; t.lineSpacing = 0.85f;
            var o = t.gameObject.AddComponent<Outline>(); o.effectColor = Color.black; o.effectDistance = new Vector2(2f, -2f);
            return t;
        }

        private static SlotUi Slot(RectTransform parent, Vector2 iconAt, float iconPx, Vector2 nameAt, Vector2 nameSize, int nameFont, int coolFont, Vector2 countOff)
        {
            var s = new SlotUi();
            s.Icon = UiKit.Icon(parent, iconAt, new Vector2(iconPx, iconPx));
            s.Name = Lettering(parent, nameAt, nameSize, nameFont, 14, new Color(1f, 0.97f, 0.9f));
            s.Cool = Lettering(parent, iconAt, new Vector2(iconPx + 30f, iconPx), coolFont, coolFont, new Color(1f, 0.62f, 0.45f));
            if (countOff != Vector2.zero) s.Count = Lettering(parent, iconAt + countOff, new Vector2(70f, 30f), 22, 22, new Color(1f, 0.87f, 0.45f));
            return s;
        }

        // ------------------------------------------------------------------ client wave 18 (w18wand), points 13 and 14: the inner ring
        /// <summary>Inner slice i of n: the piece between i x 360 / n ± 180 / n degrees (four: wave 12's quadrants exactly).</summary>
        private WheelShape InnerShape(int n, int i)
        {
            float px = 1f / Scale, a = i * 360f / n, h = 180f / n;
            return WheelShape.Create(_main, "inner" + i, a - h, a + h, WandWheelGeo.Gap * 0.5f * px, WandWheelGeo.Gap * 0.5f * px, WandWheelGeo.Centre * px, WandWheelGeo.InnerEnd * px, LinePx, false, BandPx);
        }

        /// <summary>Inner slice i of n's icon, name, cooldown and an item's amount. Four slices: wave 12's places (up / down: the name beyond
        /// the icon; left / right: under it). Any other count: the icon <see cref="SliceIconCm"/> and the name <see cref="SliceNameCm"/> out
        /// along the slice's axis, each as large as fits inside the slice (an icon of 70 px at most; a name <see cref="SliceNameHighCm"/>
        /// tall and as wide as fits; narrower than <see cref="SliceNameMinCm"/>: no name, the icon alone).</summary>
        private SlotUi InnerSlot(int n, int i)
        {
            if (n == 4)
            {
                bool vertical = (i & 1) == 0; float s = i == 0 || i == 1 ? 1f : -1f;
                Vector2 iconAt = vertical ? new Vector2(0f, s * 4.3f) : new Vector2(s * 4.85f, 0.75f), nameAt = vertical ? new Vector2(0f, s * 6.15f) : new Vector2(s * 4.85f, -1.2f);
                _nameOff[i] = false;
                return Slot(_main, iconAt * Cm, 70f, nameAt * Cm, new Vector2((vertical ? 5.6f : 3.9f) * Cm, 1.3f * Cm), 22, 44, CountOffCm * Cm);
            }
            var u = WandWheelGeo.Dir(i * 360f / n);
            Vector2 ic = u * SliceIconCm, nc = u * SliceNameCm;
            float icon = Mathf.Max(FitCm(x => BoxInSlice(n, i, ic, new Vector2(x, x) * 0.5f), 0.6f, 70f / Cm), 0.6f);
            float nw = FitCm(x => BoxInSlice(n, i, nc, new Vector2(x * 0.5f, SliceNameHighCm * 0.5f)), 0.3f, 4.6f);
            float k = icon / (70f / Cm);
            _nameOff[i] = nw < SliceNameMinCm;
            return Slot(_main, ic * Cm, icon * Cm, nc * Cm, new Vector2(Mathf.Max(nw, 0.3f) * Cm, SliceNameHighCm * Cm), 22, Mathf.Max(20, Mathf.RoundToInt(44f * k)), CountOffCm * (k * Cm));
        }
        /// <summary>The generic places (cm from the middle along a slice's axis), a name's height and its least width (cm).</summary>
        public const float SliceIconCm = 4.2f, SliceNameCm = 6.0f, SliceNameHighCm = 1.05f, SliceNameMinCm = 1.2f;
        /// <summary>A box (centre, half size: cm in the wheel's plane) lies inside inner slice i of n, 1 mm clear of its edges (its corners,
        /// the middles of its sides and its centre are checked).</summary>
        private static bool BoxInSlice(int n, int i, Vector2 c, Vector2 half)
        {
            float a = i * 360f / n, h = 180f / n, m = 0.001f, off = WandWheelGeo.Gap * 0.5f + m;
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    if (!WandWheelGeo.InShape((c + new Vector2(x * half.x, y * half.y)) * 0.01f, a - h, a + h, off, off, WandWheelGeo.Centre + m, WandWheelGeo.InnerEnd - m, 0f)) return false;
            return true;
        }
        /// <summary>The largest value in [lo, hi] for which <paramref name="ok"/> holds (it holds for every smaller one); 0 when not even lo.</summary>
        private static float FitCm(System.Func<float, bool> ok, float lo, float hi)
        {
            if (!ok(lo)) return 0f; if (ok(hi)) return hi;
            for (int k = 0; k < 24; k++) { float mid = (lo + hi) * 0.5f; if (ok(mid)) lo = mid; else hi = mid; }
            return lo;
        }

        /// <summary>The inner ring rebuilt for n slices (the setting changed since it was built): the old pieces and their texts go, the new
        /// pieces are put first under the main wheel (after its white middle: drawn under every text), their texts after.</summary>
        private void BuildInner(int n)
        {
            if (n == _slices) return;
            for (int i = 0; i < _slices; i++)
            {
                if (_inner[i] != null) Destroy(_inner[i].gameObject);
                var ui = _quickUi[i];
                if (ui != null) { Destroy(ui.Icon.gameObject); Destroy(ui.Name.gameObject); Destroy(ui.Cool.gameObject); if (ui.Count != null) Destroy(ui.Count.gameObject); }
                _inner[i] = null; _quickUi[i] = null;
            }
            for (int i = 0; i < n; i++) { _inner[i] = InnerShape(n, i); _inner[i].transform.SetSiblingIndex(1 + i); }
            for (int i = 0; i < n; i++) _quickUi[i] = InnerSlot(n, i);
            Debug.Log($"[Wand] the wheel's inner ring rebuilt: {_slices} → {n} slices");
            _slices = n; _dirty = true;
            _look = CastLook.Capture(_rt, true); // client wave 21 (w21tools): the new slices' texts in the look in force (nothing else is touched: a hidden piece keeps what it had)
        }

        /// <summary>The trigger went down: the wheel stands at <paramref name="origin"/> in the plane of <paramref name="rotation"/>
        /// (its right / up) with these four quick skills and this hotbar's rows; nothing is drawn until <see cref="Draw"/> says so.
        /// Client wave 18 (w18wand): the inner ring's slots (as many as "Wand slices"), each a skill, or an item where <paramref name="items"/> says so.
        /// Client wave 23 (w23wand): <paramref name="size"/>: the share of life size the picture is drawn at, about its middle (the wheel's
        /// own size: LokiGame.W12Wand.cs W23WheelSize; 1: the wheel of waves 12-22, to the bit): its pieces, its texts, the tip's dot and
        /// <see cref="PlaneWorld"/> all follow it.</summary>
        public void Open(Vector3 origin, Quaternion rotation, int[] quick, int tab, bool[] items = null, float size = 1f)
        {
            if (_home == null) _home = transform.parent;
            if (transform.parent != _home) transform.SetParent(_home, false);
            size = Mathf.Clamp(size, 0.05f, 8f);
            Front = false; Placing = false; _factor = size; _rt.localScale = Vector3.one * (Scale * size); _dot.rectTransform.localScale = Vector3.one; SetOrder(WandSortingOrder);
            transform.SetPositionAndRotation(origin, rotation);
            Begin(quick, items, tab);
        }

        /// <summary>Client wave 13 (w13view): the trigger went down with "in front of you": the same wheel as the head's child at its
        /// place there (the player's own, else <see cref="FrontDefault"/>), facing the eyes; nothing drawn until <see cref="Draw"/>.</summary>
        public void OpenFront(Transform head, int[] quick, int tab, bool[] items = null)
        {
            if (_home == null) _home = transform.parent;
            if (transform.parent != head) transform.SetParent(head, false);
            Front = true; Placing = false; ApplyFrontPlace(); _dot.rectTransform.localScale = Vector3.one * FrontDot; SetOrder(FrontSortingOrder);
            Begin(quick, items, tab);
        }

        /// <summary>Client wave 13 (w13view): Reposition shows the front picture (its main wheel, no tip) so the player can place it.</summary>
        public void ShowForPlacing(Transform head, int[] quick, int tab, bool[] items = null)
        {
            OpenFront(head, quick, tab, items); Placing = true;
            Draw(-1, -1, Vector2.zero, true); _dot.gameObject.SetActive(false);
        }

        private void SetOrder(int order) { var c = _rt.GetComponent<Canvas>(); if (c != null && c.sortingOrder != order) c.sortingOrder = order; }
        /// <summary>The canvas's sorting order now (smoke).</summary>
        public int SortingOrder { get { var c = _rt.GetComponent<Canvas>(); return c != null ? c.sortingOrder : 0; } }

        // ---- client wave 18 (w18share), the design's point 33: what a friend's copy of this picture needs (UI/GestureShare.cs reads it; a
        // friend's copy is this same class, drawn by UI/FriendGestures.cs through Open / Draw)
        /// <summary>The bar whose rows the outer ring shows, and inner slot i's id as drawn (0: empty).</summary>
        public int ShownTab => _tab;
        public int ShownQuick(int i) => i >= 0 && i < _quick.Length ? _quick[i] : 0;
        /// <summary>The inner ring's slices drawn now, and whether inner slot i holds an item (wave 18's w18wand: 2-8 slices, items on them;
        /// merged).</summary>
        public int ShownSlices => _slices; // (client wave 18 merge: w18wand's slices)
        public bool ShownQuickItem(int i) => i >= 0 && i < _quickItem.Length && _quickItem[i]; // (client wave 18 merge: w18wand's items)
        /// <summary>A friend's copy opens with the friend's slices and items (not my own "Wand slices"): w18wand's Begin builds the inner ring
        /// for <see cref="SlicesOverride"/> when it is set (Begin).</summary>
        public int SlicesOverride { get; set; }
        public void OpenAsFriend(Vector3 origin, Quaternion rotation, int[] quick, bool[] items, int tab, int slices)
        {
            SlicesOverride = slices;
            Open(origin, rotation, quick, tab, items); // (client wave 18 merge: w18wand's items)
        }
        /// <summary>A friend's copy of a picture shown before the eyes: the tip's dot as large as there, drawn in the front picture's order.</summary>
        public void ShowAsFront(bool front)
        {
            _dot.rectTransform.localScale = Vector3.one * (front ? FrontDot : 1f);
            SetOrder(front ? FrontSortingOrder : WandSortingOrder);
        }

        /// <summary>The front picture's place in force: the player's own (VR/Comfort.cs) or the default.</summary>
        public void ApplyFrontPlace()
        {
            // (client wave 23, w23wand: the wheel's size is the player's whether the picture was placed or not: Settings "Wheel size")
            PlaceFront(VR.Comfort.WheelPlaced ? VR.Comfort.WheelOffset : FrontDefault, VR.Comfort.WheelScale);
        }
        /// <summary>Client wave 23 (w23wand): the picture at the wand goes to a point of the world, its middle on it (a row's wheel opens on
        /// the tip: LokiGame.W12Wand.cs); its turn and size stay. In front of the eyes: nothing.</summary>
        public void Recentre(Vector3 world) { if (!Front) transform.position = world; }
        /// <summary>The front picture at a place of the head's frame, its size a factor of the default (<see cref="FrontScale"/> of life
        /// size = 1; Reposition's limits VR/Comfort.cs PlaceMinScale..PlaceMaxScale); it faces the eyes.</summary>
        public void PlaceFront(Vector3 local, float relative)
        {
            local = VR.Comfort.ClampHeadPlace(local); _factor = FrontScale * Mathf.Clamp(relative, VR.Comfort.PlaceMinScale, VR.Comfort.PlaceMaxScale);
            _rt.localPosition = local; _rt.localRotation = Quaternion.LookRotation(local.normalized, Vector3.up); _rt.localScale = Vector3.one * (Scale * _factor);
        }
        /// <summary>The front picture's size as a factor of its default (1 = <see cref="FrontScale"/> of life size).</summary>
        public float FrontRelative => _factor / FrontScale;
        /// <summary>Reposition: the front picture carried to a point of the world, at a size factor of its default.</summary>
        public void CarryFront(Vector3 world, float relative) { if (Front && _rt.parent != null) PlaceFront(_rt.parent.InverseTransformPoint(world), relative); }

        private void Begin(int[] quick, bool[] items, int tab)
        {
            BuildInner(SlicesOverride > 0 ? SlicesOverride : WandWheelGeo.Slices); // client wave 18 (w18wand), point 14: "Wand slices" as it is now (w18share at the merge: a friend's copy has THEIR slices)
            for (int i = 0; i < MaxSlices; i++) { _quick[i] = quick != null && i < quick.Length ? quick[i] : 0; _quickItem[i] = items != null && i < items.Length && items[i]; } // (point 13)
            _tab = tab; _region = -2; _subDir = -1; _subOn = false; _inPlace = false; _dirty = true;
            _main.gameObject.SetActive(false); _sub.gameObject.SetActive(false); _fan.gameObject.SetActive(false); _dot.gameObject.SetActive(false);
            if (!_rt.gameObject.activeSelf) _rt.gameObject.SetActive(true);
        }

        public void Close() { Placing = false; _placeLit = false; if (_rt.gameObject.activeSelf) _rt.gameObject.SetActive(false); }
        private bool _placeLit;
        /// <summary>Reposition: the white middle lit while a hand or the laser can take the picture, or holds it.</summary>
        public void SetPlacingLook(bool lit) { if (_placeLit == lit) return; _placeLit = lit; _dirty = true; if (MainShown) Paint(); }

        /// <summary>One frame: the region the tip is on, the open sub-wheel (-1 none), the tip's place in the plane (metres), whether
        /// the wheel is drawn yet, and (client wave 14) whether the row's wheel stands in the main wheel's place (the tip then counts
        /// from its middle).</summary>
        public void Draw(int region, int sub, Vector2 tip, bool visible, bool inPlace = false)
        {
            if (CastLook.Sync(_look)) _dirty = true; // client wave 21 (w21tools): "Casting look" changed
            inPlace &= sub >= 0;
            bool mainOn = visible && !inPlace;
            if (_main.gameObject.activeSelf != mainOn) { _main.gameObject.SetActive(mainOn); _dirty = true; }
            if (_dot.gameObject.activeSelf != visible) _dot.gameObject.SetActive(visible);
            bool subOn = visible && sub >= 0;
            if (sub >= 0 && (sub != _subDir || inPlace != _inPlace))
            {
                _subDir = sub; _inPlace = inPlace; _dirty = true;
                _sub.anchoredPosition = inPlace ? Vector2.zero : WandWheelGeo.SubCentreOf(sub) / Scale; _fan.localRotation = Quaternion.Euler(0f, 0f, -90f * sub);
            }
            if (subOn != _subOn) { _subOn = subOn; _sub.gameObject.SetActive(subOn); _dirty = true; }
            bool fanOn = subOn && !_inPlace;
            if (_fan.gameObject.activeSelf != fanOn) _fan.gameObject.SetActive(fanOn);
            _dot.rectTransform.anchoredPosition = tip / Scale;
            if (region != _region) { _region = region; _dirty = true; }
            if (_dirty && visible) Paint();
        }

        private static Color A(Color c, float a) { c.a = a; return c; }
        private bool Known(int skillId) => skillId > 0 && (_prog == null || (_prog.Skills.TryGetValue(skillId, out var k) && k.Level > 0));

        private void Paint()
        {
            _dirty = false;
            // client wave 21 (w21tools): every constant through the look in force (UI/CastLook.cs; Plain: untouched); the rows' colours never
            var edge = CastLook.C(new Color(0f, 0f, 0f, EdgeAlpha)); var edgeLit = CastLook.C(new Color(0f, 0f, 0f, 0.95f)); Color tan = CastLook.C(Tan), litTan = CastLook.C(LitTan);
            _centre.Paint(_placeLit ? CastLook.C(new Color(1f, 0.85f, 0.4f, 0.85f)) : CastLook.Pale(CentreAlpha), Color.clear); // (client wave 13: lit while Reposition can take it)
            for (int d = 0; d < _slices; d++)
            {
                // an inner slice: a skill (its icon and name), or (client wave 18, point 13) an item: its icon, name and the amount in the bag
                int id = _quick[d]; bool lit = _region == WandWheelGeo.Inner + d; var ui = _quickUi[d];
                _inner[d].Paint(lit ? A(litTan, LitAlpha) : A(tan, id > 0 ? TanAlpha : EmptyAlpha), lit ? edgeLit : edge, lit ? A(litTan, 0.95f) : A(tan, BandAlpha));
                if (id > 0 && _quickItem[d]) { var info = GameDb.GetItem(id); Picture(ui.Icon, WristMenu.ItemIcon(info)); ui.Name.text = _nameOff[d] ? "" : info.Name; if (ui.Count != null) ui.Count.text = (_inv != null ? _inv.Count(id) : 0).ToString(); }
                else if (id > 0) { var info = SkillDb.Get(id); Picture(ui.Icon, WristMenu.SkillIcon(info)); ui.Name.text = _nameOff[d] ? "" : Known(id) ? info.Name : "<color=#b0b0b0>" + info.Name + "</color>"; if (ui.Count != null) ui.Count.text = ""; }
                else { ui.Icon.enabled = false; ui.Name.text = ""; if (ui.Count != null) ui.Count.text = ""; }
            }
            for (int d = 0; d < 4; d++)
            {
                int row = _tab * Hotkeys.RowsPerTab + d; bool open = _subOn && _subDir == d;
                bool red = open || _region == WandWheelGeo.Outer + d;
                // client wave 20 (w20colors), point 51: a row's segment in ITS row's colour (UI/RowColours.cs; the sketch's dark red before), as
                // see-through as before; lit (the tip on it, its row open) lighter and more solid
                _outer[d].Paint(red ? RowColours.Lit(row, RowLitWhite, LitAlpha) : RowColours.Of(row, RedAlpha), open ? edgeLit : edge, red ? RowColours.Lit(row, RowLitWhite, 0.95f) : RowColours.Of(row, BandAlpha));
                _rowLabel[d].text = Lang.F("Row {0}", d + 1);
                int n = 0;
                for (int s = 0; s < 8 && n < 4; s++)
                {
                    var h = _keys != null ? _keys.At(row, s) : default; if (h.Empty) continue;
                    Picture(_rowIcons[d, n++], h.IsSkill ? WristMenu.SkillIcon(SkillDb.Get(h.Id)) : WristMenu.ItemIcon(GameDb.GetItem(h.Id)));
                }
                for (; n < 4; n++) _rowIcons[d, n].enabled = false;
            }
            if (!_subOn) return;
            int subRow = _tab * Hotkeys.RowsPerTab + _subDir;
            _subCentre.Paint(RowColours.Of(subRow, RowCentreAlpha), Color.clear); // (client wave 20: the open row's colour; white before)
            _subTitle.text = _inPlace ? Lang.F("Row {0}", _subDir + 1) : "";

            for (int c = 0; c < 8; c++)
            {
                var h = _keys != null ? _keys.At(subRow, c) : default; bool lit = _region == WandWheelGeo.Cell + c; var ui = _cellUi[c];
                // (client wave 20, w20colors: each slot's band in the open row's colour, its fill the sketch's tan; the chosen one all gold-tan)
                _cells[c].Paint(lit ? A(litTan, LitAlpha) : A(tan, h.Empty ? EmptyAlpha : TanAlpha), lit ? edgeLit : edge, lit ? A(litTan, 0.95f) : RowColours.Of(subRow, BandAlpha));
                if (h.Empty) { ui.Icon.enabled = false; ui.Name.text = ""; ui.Count.text = ""; continue; }
                if (h.IsSkill) { var info = SkillDb.Get(h.Id); Picture(ui.Icon, WristMenu.SkillIcon(info)); ui.Name.text = info.Name; ui.Count.text = Known(h.Id) ? "" : "—"; }
                else { var info = GameDb.GetItem(h.Id); Picture(ui.Icon, WristMenu.ItemIcon(info)); ui.Name.text = info.Name; ui.Count.text = (_inv != null ? _inv.Count(h.Id) : 0).ToString(); }
            }
        }

        private static void Picture(Image icon, Sprite s) { icon.sprite = s; icon.enabled = s != null; icon.color = Color.white; }

        private static string Seconds(float left) => left > 0.05f ? (left >= 1f ? Mathf.CeilToInt(left).ToString() : left.ToString("0.0")) : "";

        private void LateUpdate()
        {
            if (!MainShown || _bar == null) return;
            for (int d = 0; d < _slices; d++) { string t = _quick[d] > 0 && !_quickItem[d] ? Seconds(_bar.BlockedSkill(_quick[d]).left) : ""; if (_quickUi[d].Cool.text != t) _quickUi[d].Cool.text = t; } // (client wave 18: n slices; an item has no cooldown)
            if (!_subOn) return;
            int row = _tab * Hotkeys.RowsPerTab + _subDir;
            for (int c = 0; c < 8; c++) { string t = Seconds(_bar.BlockedAt(row, c).left); if (_cellUi[c].Cool.text != t) _cellUi[c].Cool.text = t; }
        }

        /// <summary>A point of the wheel's plane (metres from its middle) in the world.</summary>
        public Vector3 PlaneWorld(Vector2 m) => _rt.TransformPoint(new Vector3(m.x / Scale, m.y / Scale, 0f));

        // ---- client wave 18 (w18wand), point 20
        /// <summary>Point 20 (design note: "the wand should not be able to occlude the wheel itself ... any part of the wand that is occluding the wheel be
        /// it by being in front or behind it, becomes a ghostly blue transparent entity"): the discs the wheel covers now, in the world: the main
        /// wheel (or the row's wheel in its place) and an open sub-wheel beside it (centre, radius; radius 0: none), each
        /// <see cref="CoverMargin"/> beyond its outline, and the wheel's plane's normal. The wand is drawn as its ghost wherever it lies over
        /// them on the way from the eye, in front of the wheel or behind it (VR/WandHolster.cs WheelToShaders; Resources/Shaders/
        /// LokiWandPanels.cginc LokiOverWheel). False: nothing of the wheel is drawn now.</summary>
        public const float CoverMargin = 0.004f;
        public bool CoverDiscs(out Vector3 c0, out float r0, out Vector3 c1, out float r1, out Vector3 normal)
        {
            c0 = c1 = Vector3.zero; r0 = r1 = 0f; normal = _rt != null ? _rt.forward : Vector3.forward;
            if (!Shown) return false;
            float unit = Vector3.Distance(PlaneWorld(Vector2.zero), PlaneWorld(Vector2.right)); // (world metres for a metre of the wheel's own geometry: its size)
            if (MainShown) { c0 = PlaneWorld(Vector2.zero); r0 = (WandWheelGeo.OuterEnd + CoverMargin) * unit; }
            if (SubShown)
            {
                if (_inPlace) { c0 = PlaneWorld(Vector2.zero); r0 = Mathf.Max(r0, (WandWheelGeo.SubEnd + CoverMargin) * unit); }
                else { c1 = PlaneWorld(_sub.anchoredPosition * Scale); r1 = (WandWheelGeo.SubEnd + CoverMargin) * unit; }
            }
            return r0 > 0f || r1 > 0f;
        }
    }
}
