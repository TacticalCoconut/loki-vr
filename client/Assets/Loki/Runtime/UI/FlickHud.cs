using System.Collections.Generic;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 12 (w12cast): what the "flick" casting scheme (and the wand's plain strokes, which draw the same) shows near the
    /// hand. A small panel standing where the gesture began, facing the player, in the plane the strokes are measured in:
    ///   before the first stroke   the eight rows around the start, each as "bar · row" with the icon of its middle slot (what
    ///                             letting go after that stroke takes), 7.5 cm from the start in its direction;
    ///   after the first stroke    that row's 3 x 3 around the corner: its middle slot on the corner, the other eight 7.5 cm
    ///                             from it in their directions, the pad's layout; the chosen one lit.
    /// A thin line from the start to the corner to the hand shows the strokes so far.
    /// Client wave 14 (w14cast), point 5 (design note: "the menu that you flick towards should be non-transparent from the very beginning.
    /// Every single option that is currently available to be selected should be visible, even the empty ones. Additionally, there is
    /// a very significant delay between me pressing the button for it to appear and it actually appearing"): drawn from the
    /// gesture's FIRST frame (wave 12 waited for a 0.25 s hesitation: Game/FlickRecognizer.cs), every choice of the stroke at hand
    /// shown, an empty one as a darker plate (<see cref="EmptyColour"/>) with its label, every plate OPAQUE (<see cref="PlateAlpha"/>;
    /// wave 12 drew them at 0.45, an empty one at 0.27). The strokes' line stays at <see cref="LineAlpha"/>; the texts have a black
    /// outline. Still no panel (design note, 2026-10-06, 21:50: "excluded from the 'UI elements block gaze selection' system"): made without
    /// a background (UiKit.Panel background false): no collider, no UiBlocker: the laser's and the look's rays pass through it.
    /// Client wave 15 (w15cast), point 32 (design note: "keep showing me the line designating and showing the first flick where it came from. I
    /// don't want to see that line, just make it disappear, but I do want a small animation to show that I have opened a submenu"): the
    /// line from the start to the first flick's corner is gone (the second stroke's, from the corner to the hand, stays); when a row's
    /// 3 x 3 opens (or the first stroke settles on another row) its nine plates POP out of the corner: from <see cref="PopFrom"/> of
    /// their size at the corner to their places and full size in <see cref="PopSeconds"/> s (ease-out).
    /// Client wave 16 (w16cast), design note (2026-10-08): "The empty slots on the flick menu as far too opaque. Also, I don't like the 1.1 , 1.2 ,
    /// 1.3 , etc nomenclature for the slots on the menu. Just put large "1" , "2" , "3" on the EMPTY SLOTS", then "The empty flick slots
    /// should at least be mildly visible, and have an opaque portrait outline" and "the numbers should be fully opaque":
    ///   an EMPTY slot   a see-through fill (<see cref="EmptyAlpha"/>), an opaque outline round its plate (<see cref="FramePx"/>: a dark rim
    ///                   outside, a pale line inside, both alpha 1) and its number LARGE in the middle (<see cref="NumberFont"/>, alpha 1);
    ///   a FILLED slot   as before: opaque, its icon, its number small under it;
    ///   the numbers     a slot's number in the menu it is on, in reading order round the middle: the eight rows 1-8 (up-left 1 … down-right
    ///                   8 = bar 1 rows 1-4, bar 2 rows 1-4), a row's slots 1-4 and 6-9 (the pad's layout; the middle, 5, is the cancel since the
    ///                   centre's removal below). No "bar · row" text.
    ///   a ROW's plate   (the design's answer, 2026-10-08: "Yes, count it as filled if any slot is filled") on the first step a row is FILLED when
    ///                   any of its flick slots holds something, EMPTY only when all are empty; it shows the icon of its first filled flick
    ///                   slot in reading order (<see cref="RowIconSlot"/>).
    ///   the CENTRE      (design note, 2026-10-08: "Remove the center slot on the flick menu. That way the user can cancel a cast") no plate in the
    ///                   middle on either step: a CANCEL mark (a red ring with an X, dark-rimmed, opaque: <see cref="CancelPx"/>) at the start
    ///                   before the first stroke and at the row's middle after it; letting go there takes nothing (Game/FlickRecognizer.cs
    ///                   TryResult). It is drawn larger (<see cref="CancelLitScale"/>) while letting go would cancel, smaller and darker while a
    ///                   slot is chosen. A row's step shows its eight slots 1-4 and 6-9 in their places; its slot 5 is not on the flick.
    /// Client wave 18 (w18flick), the design's point 32 ("Just like the wand has the option to place the circle either in front of your view or where
    /// the tip of the wand is, do the same for the flick motion"): with "Flick shown: In front of you" (VR/Comfort.cs FlickFront) the same
    /// picture is the HEAD's child (head-locked, <see cref="BeginFront"/>) at the wand wheel's default front place (UI/WandWheel.cs
    /// FrontDefault: 0.60 m, 11 degrees under the line of sight), facing the eyes, at <see cref="FrontScale"/> of life size, the hand's dot
    /// <see cref="FrontDot"/> times as large, drawn over the panels (<see cref="FrontSortingOrder"/>). Only the picture moves: the strokes are
    /// still measured from where the gesture began (LokiGame.W12Cast.cs). "At your hand" (the default) is wave 12-17's place.
    /// Client wave 20 (w20colors), the design's point 51 ("Give each one of the submenus for the flick a color pattern so that I can know at a glance
    /// what submenu I have opened"): every row has its colour (UI/RowColours.cs, the one palette). On the first step each row's plate is drawn in
    /// ITS row's colour, on the second step every slot of the open row in THAT row's colour, so the open row is known by its colour:
    ///   the outline   every plate, filled or empty, has the opaque outline (<see cref="FramePx"/>: the dark rim, then a line in the row's
    ///                 colour; wave 16 drew it pale and on empty plates only);
    ///   the fill      tinted with the row's colour: a filled plate <see cref="FilledTint"/> of the way (still opaque), an empty one's see-through
    ///                 fill <see cref="EmptyTint"/> of the way (still <see cref="EmptyAlpha"/>) (<see cref="FilledFill"/>, <see cref="EmptyFill"/>);
    ///   the cancel    on the second step the cancel mark's RING is in the row's colour (dimmed as before while a slot is chosen); its X stays red;
    ///                 on the first step (no row open yet) the ring stays red.
    /// The chosen slot stays gold (MarkColour); icons, numbers and the hand's dot keep their own colours.
    /// </summary>
    public sealed class FlickHud : MonoBehaviour
    {
        public const float Scale = 0.0003f, RingPx = 250f, IconPx = 120f, Size = 900f;
        private RectTransform _rt; private Hotkeys _keys; private Inventory _inv; private Progression _prog;
        private readonly List<RectTransform> _slots = new List<RectTransform>(); // 0-7 the directions, 8 the middle
        private readonly List<Image> _bg = new List<Image>(), _icons = new List<Image>();
        private readonly List<Text> _labels = new List<Text>(), _numbers = new List<Text>();
        private readonly List<GameObject> _frames = new List<GameObject>();
        private readonly List<Image[]> _frameLines = new List<Image[]>(); // client wave 20 (w20colors): each outline's line in the row's colour
        private readonly int[] _plateRow = new int[9]; // client wave 20: the row whose colour plate i is drawn in now
        private RectTransform _seg1, _seg2, _dot, _cancel; private readonly List<Graphic> _cancelLight = new List<Graphic>(), _cancelAll = new List<Graphic>();
        private WheelShape _cancelRing; private int _cancelRow = -1; // client wave 20: the cancel's ring (its light piece), the row whose colour it has (-1: red)
        /// <summary>Client wave 16 (w16cast): the cancel mark at the centre: its ring's outer radius in pixels (1.2 cm), its size while letting go
        /// would cancel (else 1), its colours.</summary>
        public const float CancelPx = 40f, CancelLitScale = 1.2f;
        public static readonly Color CancelColour = new Color(1f, 0.42f, 0.38f, 1f), CancelDim = new Color(0.62f, 0.26f, 0.24f, 1f);
        private bool _cancelLit; private CastLook.State _look; // (client wave 21, w21tools: the look in force, UI/CastLook.cs)
        private int _phase = -1, _d1 = -2, _d2 = -2;
        /// <summary>Client wave 18 (w18flick), point 32: the picture in front of the eyes: the share of life size, the dot's size there, the
        /// canvas order (as the wand wheel's front picture: a note under the line of sight never covers it); drawn there now.</summary>
        public const float FrontScale = WandWheel.FrontScale, FrontDot = WandWheel.FrontDot; public const int FrontSortingOrder = WandWheel.FrontSortingOrder;
        public bool Front { get; private set; }
        private Transform _home; private Vector3 _homeScale; private int _homeOrder;
        public bool Shown => _rt != null && _rt.gameObject.activeSelf;
        /// <summary>The icons are showing (client wave 14: from the gesture's first frame).</summary>
        public bool IconsShown { get; private set; }
        public int ShownPhase => _phase;
        /// <summary>The plates drawn now (smoke: every choice of the stroke at hand, empty ones too: 8 before the first stroke, 9 after).</summary>
        public int PlatesShown { get { int n = 0; foreach (var r in _slots) if (r.gameObject.activeSelf) n++; return n; } }
        /// <summary>Client wave 15 (w15cast), point 32: the row's 3 x 3 popping out of the corner: how long, from what size.</summary>
        public const float PopSeconds = 0.15f, PopFrom = 0.3f;
        private float _popStart = -1f;
        /// <summary>The pop's progress, 0-1 (1: done; smoke); the first stroke's line shown (smoke: never since client wave 15).</summary>
        public float PopProgress { get; private set; } = 1f;
        public bool FirstLineShown => _seg1 != null && _seg1.gameObject.activeSelf;
        public const float PlateAlpha = 1f, MarkAlpha = 1f, LineAlpha = 0.6f;
        private static readonly Color SlotColour = new Color(0.08f, 0.09f, 0.13f, PlateAlpha), MarkColour = new Color(0.95f, 0.75f, 0.3f, MarkAlpha);
        /// <summary>Client wave 16 (w16cast), point 1: the fill of a choice with nothing on it: see-through but still mildly visible (wave 14
        /// drew it opaque; wave 12 at 0.27); its colour wave 14's, darker than a filled plate.</summary>
        public const float EmptyAlpha = 0.35f;
        public static readonly Color EmptyColour = new Color(0.035f, 0.04f, 0.055f, EmptyAlpha);
        /// <summary>Client wave 16 (w16cast): an empty plate's opaque outline (<see cref="FramePx"/> wide: the outer <see cref="FrameRimPx"/> dark,
        /// the rest pale; 2.1 mm at <see cref="Scale"/>) and its large number (<see cref="NumberFont"/> px: about 1.8 cm tall). Client wave 20
        /// (w20colors): on every plate, the rest in the row's colour.</summary>
        public const float FramePx = 7f, FrameRimPx = 2f; public const int NumberFont = 84;
        public static readonly Color FrameRim = new Color(0f, 0f, 0f, 1f), NumberColour = new Color(1f, 0.85f, 0.45f, 1f);
        /// <summary>Client wave 20 (w20colors), point 51: how far a plate's fill is taken towards its row's colour (UI/RowColours.cs): a filled
        /// plate (opaque) and an empty one (see-through at <see cref="EmptyAlpha"/>); the cancel ring's colour while a slot is chosen (the row's
        /// colour times this, as <see cref="CancelDim"/> is to <see cref="CancelColour"/>).</summary>
        public const float FilledTint = 0.35f, EmptyTint = 0.45f, CancelRowDim = 0.62f;
        /// <summary>The chosen plate's fill in the look in force (client wave 21, w21tools: gold in Plain, the theme's dark wood in Themed).</summary>
        public static Color ChosenFill => CastLook.C(MarkColour);
        public static Color FilledFill(int row) => RowColours.Tint(CastLook.C(SlotColour), row, FilledTint);
        public static Color EmptyFill(int row) => RowColours.Tint(CastLook.C(EmptyColour), row, EmptyTint);
        /// <summary>The row whose colour plate i is drawn in now (client wave 20).</summary>
        public int PlateRow(int i) => _plateRow[i];
        /// <summary>The plate behind icon i (0-7 the directions, 8 the middle) as drawn now (smoke).</summary>
        public Color PlateColour(int i) => _bg[i].color;
        public bool PlateShown(int i) => _slots[i].gameObject.activeSelf;
        /// <summary>Client wave 15 (w15cast): the size plate i is drawn at now (1: full; smaller while the row's 3 x 3 pops out).</summary>
        public float PlateScale(int i) => _slots[i].localScale.x;

        public static FlickHud Create(Transform parent, string name, Hotkeys keys, Inventory inv, Progression prog)
        {
            var rt = UiKit.Panel(name, parent, new Vector2(Size, Size), Scale, false);
            var f = rt.gameObject.AddComponent<FlickHud>();
            f._rt = rt; f._keys = keys; f._inv = inv; f._prog = prog;
            for (int i = 0; i < 9; i++)
            {
                var box = UiKit.Box(rt, "slot" + i, Vector2.zero, new Vector2(IconPx, IconPx), SlotColour);
                f._slots.Add(box); f._bg.Add(box.GetComponent<Image>());
                f._icons.Add(UiKit.Icon(box, new Vector2(0, 12), new Vector2(76, 76)));
                var t = UiKit.Label(box, "", new Vector2(0, -40), new Vector2(IconPx - 6, 36), 24, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.45f));
                t.resizeTextForBestFit = true; t.resizeTextMinSize = 15; t.resizeTextMaxSize = 24;
                var o = t.gameObject.AddComponent<Outline>(); o.effectColor = Color.black; o.effectDistance = new Vector2(2f, -2f);
                f._labels.Add(t);
                // client wave 16 (w16cast): an empty plate's opaque outline and its large number (shown only while the slot is empty)
                f._frames.Add(Frame(box, IconPx, out var line)); f._frameLines.Add(line); // (client wave 20: on every plate, its line in the row's colour)
                var n = UiKit.Label(box, "", Vector2.zero, new Vector2(IconPx, IconPx), NumberFont, TextAnchor.MiddleCenter, NumberColour);
                n.horizontalOverflow = HorizontalWrapMode.Overflow; n.verticalOverflow = VerticalWrapMode.Overflow;
                var no = n.gameObject.AddComponent<Outline>(); no.effectColor = Color.black; no.effectDistance = new Vector2(3f, -3f);
                n.gameObject.SetActive(false);
                f._numbers.Add(n);
            }
            f._seg1 = UiKit.Box(rt, "stroke1", Vector2.zero, new Vector2(1, 8), new Color(1f, 0.9f, 0.5f, LineAlpha));
            f._seg2 = UiKit.Box(rt, "stroke2", Vector2.zero, new Vector2(1, 8), new Color(1f, 0.9f, 0.5f, LineAlpha));
            f._cancel = f.CancelMark(rt); // client wave 16: under the hand's dot
            f._dot = UiKit.Box(rt, "hand", Vector2.zero, new Vector2(18, 18), new Color(1f, 0.95f, 0.6f, 0.95f));
            f._look = CastLook.Capture(rt); f._look.Halo = true; // client wave 21 (w21tools): built plain; drawn in the look in force from the first Draw (themed: dark numbers on cream plates, their outline pale)
            f._home = parent; f._homeScale = rt.localScale; var cv = rt.GetComponent<Canvas>(); f._homeOrder = cv != null ? cv.sortingOrder : 10; // (client wave 18: where it goes back from in front)
            rt.gameObject.SetActive(false);
            return f;
        }

        /// <summary>The gesture began: the panel stands at <paramref name="origin"/> in the strokes' plane (right / up of
        /// <paramref name="rotation"/>), the eight rows drawn at once (client wave 14).</summary>
        public void Begin(Vector3 origin, Quaternion rotation)
        {
            SetFront(false, null); // (client wave 18, point 32: at the hand)
            transform.SetPositionAndRotation(origin, rotation);
            Start0();
        }
        /// <summary>Client wave 18 (w18flick), point 32: the gesture began with "Flick shown: In front of you": the same picture head-locked in
        /// front of the eyes (see the class); the hand's place is still measured from where the gesture began.</summary>
        public void BeginFront(Transform head)
        {
            SetFront(true, head);
            var local = WandWheel.FrontDefault; transform.localPosition = local; transform.localRotation = Quaternion.LookRotation(local.normalized, Vector3.up);
            Start0();
        }
        private void SetFront(bool on, Transform head)
        {
            Front = on; var t = on ? head : _home;
            if (t != null && transform.parent != t) transform.SetParent(t, false);
            _rt.localScale = on ? Vector3.one * (Scale * FrontScale) : _homeScale;
            _dot.localScale = Vector3.one * (on ? FrontDot : 1f);
            var cv = _rt.GetComponent<Canvas>(); int order = on ? FrontSortingOrder : _homeOrder; if (cv != null && cv.sortingOrder != order) cv.sortingOrder = order;
        }
        private void Start0()
        {
            _phase = -1; _d1 = _d2 = -2; IconsShown = false;
            if (!_rt.gameObject.activeSelf) _rt.gameObject.SetActive(true);
            Draw(0, -1, -1, Vector2.zero, Vector2.zero);
        }

        public void Hide() { IconsShown = false; if (_rt.gameObject.activeSelf) _rt.gameObject.SetActive(false); }

        /// <summary>One frame: the recogniser's state and the hand's place in the plane (metres). Rows 1-8 are fixed (bar 1 rows 1-4,
        /// bar 2 rows 1-4).</summary>
        public void Draw(int phase, int dir1, int dir2, Vector2 corner, Vector2 hand)
        {
            float k = 1f / Scale;
            bool relook = CastLook.Sync(_look); if (relook) _cancelRow = -2; // client wave 21 (w21tools): "Casting look" changed: every plate and the cancel mark painted again
            _dot.anchoredPosition = hand * k; if (!_dot.gameObject.activeSelf) _dot.gameObject.SetActive(true);
            // client wave 16 (w16cast): the cancel mark at the centre: the start, then the row's middle (it goes with the corner)
            _cancel.anchoredPosition = phase >= 1 ? corner * k : Vector2.zero; if (!_cancel.gameObject.activeSelf) _cancel.gameObject.SetActive(true);
            bool lit = phase != 2; int cancelRow = phase >= 1 && dir1 >= 0 ? FlickRecognizer.RowOfDir(dir1) : -1; // (client wave 20: the open row's colour)
            if (lit != _cancelLit || cancelRow != _cancelRow) SetCancelLit(lit, cancelRow);
            bool lines = phase > 0;
            Segment(_seg1, Vector2.zero, corner * k, false); Segment(_seg2, corner * k, hand * k, lines); // (client wave 15: no line to the first flick)
            bool relayout = relook || phase != _phase || dir1 != _d1 || dir2 != _d2;
            // client wave 15 (w15cast), point 32: a row's 3 x 3 opens (from the eight rows, or another row): it pops out of the corner
            if (phase >= 1 && (_phase < 1 || dir1 != _d1)) _popStart = Time.unscaledTime;
            if (phase < 1) _popStart = -1f;
            float e = _popStart < 0f ? 1f : Mathf.Clamp01((Time.unscaledTime - _popStart) / PopSeconds); PopProgress = e;
            float ease = 1f - (1f - e) * (1f - e) * (1f - e);
            // (the corner moves while the first stroke goes on: the row's icons go with it)
            if (phase >= 1) for (int i = 0; i < 9; i++)
            {
                _slots[i].anchoredPosition = corner * k + (i < 8 ? FlickRecognizer.DirVector(i) * (RingPx * ease) : Vector2.zero);
                _slots[i].localScale = Vector3.one * Mathf.Lerp(PopFrom, 1f, ease);
            }
            else for (int i = 0; i < 9; i++) if (_slots[i].localScale.x != 1f) _slots[i].localScale = Vector3.one;
            if (!relayout) return;
            _phase = phase; _d1 = dir1; _d2 = dir2; IconsShown = true;
            for (int i = 0; i < 9; i++)
            {
                bool on = i < 8; // (client wave 16: no middle plate on either step: the middle is the cancel mark)
                if (_slots[i].gameObject.activeSelf != on) _slots[i].gameObject.SetActive(on);
                if (!on) continue;
                if (phase == 0)
                {
                    // the eight rows: the icon of the row's middle slot (what letting go after the first stroke takes) and the row's number
                    // in this menu, 1-8 (client wave 16: no "bar · row" any more)
                    // (client wave 16, the design's answer: the row is filled when any of its flick slots is: RowIconSlot)
                    int row = FlickRecognizer.RowOfDir(i), at = RowIconSlot(_keys, row);
                    _slots[i].anchoredPosition = FlickRecognizer.DirVector(i) * RingPx;
                    Fill(i, at >= 0 && _keys != null ? _keys.At(row, at) : default, (row + 1).ToString(), false, row); // (client wave 20: in ITS row's colour)
                }
                else
                {
                    // a row's eight slots round its middle (1-4, 6-9 in their places); lit only when a second stroke chose it (client wave 16:
                    // the middle, slot 5, is gone: letting go there cancels)
                    int row = FlickRecognizer.RowOfDir(dir1), slot = FlickRecognizer.CellOfDir(i);
                    Fill(i, _keys != null ? _keys.At(row, slot) : default, (slot + 1).ToString(), phase == 2 && i == dir2, row); // (client wave 20: in the open row's colour)
                }
            }
        }

        /// <summary>A thin bar from a to b (pixels on the panel).</summary>
        private static void Segment(RectTransform seg, Vector2 a, Vector2 b, bool on)
        {
            float len = (b - a).magnitude; on &= len > 2f;
            if (seg.gameObject.activeSelf != on) seg.gameObject.SetActive(on);
            if (!on) return;
            seg.anchoredPosition = (a + b) * 0.5f; seg.sizeDelta = new Vector2(len, 8f);
            seg.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
        }

        /// <summary>Client wave 16 (w16cast), the design's answer ("Yes, count it as filled if any slot is filled") and the centre's removal: the slot
        /// whose icon a row's plate shows on the flick's first step: the first of its FLICK slots (1-4, 6-9; slot 5 is not on the flick)
        /// holding something, in reading order; -1: all eight are empty (the plate is drawn empty, whatever slot 5 holds). The quick-slot
        /// picker's flick layout uses the same.</summary>
        public static int RowIconSlot(Hotkeys keys, int row)
        {
            if (keys == null) return -1;
            for (int s = 0; s < Hotkeys.QuickSlots; s++) if (s != 4 && !keys.At(row, s).Empty) return s;
            return -1;
        }

        /// <summary>Plate i shows the quick slot <paramref name="h"/> (empty: nothing on it); <paramref name="number"/> is its number in the menu
        /// at hand: small under a filled slot's icon, large in an empty one (client wave 16). Client wave 20 (w20colors): drawn in
        /// <paramref name="row"/>'s colour (its outline's line, its fill's tint); the chosen one's fill stays gold.</summary>
        private void Fill(int i, Hotkeys.Slot h, string number, bool marked, int row)
        {
            Sprite s = h.Empty ? null : h.IsSkill ? WristMenu.SkillIcon(SkillDb.Get(h.Id)) : WristMenu.ItemIcon(GameDb.GetItem(h.Id));
            _icons[i].sprite = s; _icons[i].enabled = s != null;
            _labels[i].text = h.Empty ? "" : number;
            _numbers[i].text = h.Empty ? number : ""; if (_numbers[i].gameObject.activeSelf != h.Empty) _numbers[i].gameObject.SetActive(h.Empty);
            if (!_frames[i].activeSelf) _frames[i].SetActive(true); // (client wave 20: every plate's outline; wave 16: the empty ones' only)
            _plateRow[i] = row; var line = RowColours.Of(row); foreach (var img in _frameLines[i]) img.color = line;
            _bg[i].color = marked ? CastLook.C(MarkColour) : h.Empty ? EmptyFill(row) : FilledFill(row);
            // client wave 21 (w21tools): themed, a plate's numbers are dark with a pale outline on its cream, pale with a dark one on the chosen
            // (dark) plate; plain: never touched
            if (!CastLook.Plain)
                foreach (var t in new[] { _labels[i], _numbers[i] })
                {
                    t.color = CastLook.Number(marked);
                    var o = t.GetComponent<Outline>(); if (o != null) o.effectColor = CastLook.Outline(marked, o.effectColor.a);
                }
        }

        /// <summary>Client wave 16 (w16cast): the cancel mark: a ring with an X in it, each piece drawn dark and a little wider first (legible over
        /// a bright and over a dark scene), then in <see cref="CancelColour"/>; every piece opaque.</summary>
        private RectTransform CancelMark(RectTransform parent)
        {
            var go = new GameObject("cancel", typeof(RectTransform)); go.transform.SetParent(parent, false); go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero; rt.sizeDelta = Vector2.one * (CancelPx * 2f);
            float r1 = CancelPx, r0 = CancelPx - 14f, bar = (r0 - 3f) * 2f;
            foreach (bool light in new[] { false, true })
            {
                float g = light ? 2.5f : 0f; var c = light ? CancelColour : FrameRim;
                var ring = WheelShape.Create(rt, light ? "ring" : "ringRim", 0f, 360f, 0f, 0f, r0 + g, r1 - g, 0f, false); ring.Paint(c, c);
                Graphic a = UiKit.Box(rt, light ? "x1" : "x1Rim", Vector2.zero, new Vector2(bar - g * 2f, 12f - g * 2f), c).GetComponent<Image>(); a.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                Graphic b = UiKit.Box(rt, light ? "x2" : "x2Rim", Vector2.zero, new Vector2(bar - g * 2f, 12f - g * 2f), c).GetComponent<Image>(); b.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);
                _cancelAll.Add(ring); _cancelAll.Add(a); _cancelAll.Add(b);
                if (light) { _cancelLight.Add(ring); _cancelLight.Add(a); _cancelLight.Add(b); _cancelRing = ring; }
            }
            return rt;
        }
        /// <summary>The cancel mark drawn as "letting go cancels" (larger, bright) or not (smaller, dim); client wave 20 (w20colors): its ring in
        /// <paramref name="row"/>'s colour on the second step (-1: the first step: red as the X).</summary>
        private void SetCancelLit(bool lit, int row)
        {
            _cancelLit = lit; _cancelRow = row; _cancel.localScale = Vector3.one * (lit ? CancelLitScale : 1f);
            var c = lit ? CancelColour : CancelDim;
            foreach (var g in _cancelLight) { if (g is WheelShape w) w.Paint(c, c); else g.color = c; }
            if (row >= 0 && _cancelRing != null) { var rc = lit ? RowColours.Of(row) : RowColours.Dark(row, CancelRowDim); _cancelRing.Paint(rc, rc); }
        }

        /// <summary>Client wave 16 (w16cast): an opaque outline just inside a square plate of <paramref name="size"/> pixels: a dark rim
        /// <see cref="FrameRimPx"/> wide outside, a line inside it (together <see cref="FramePx"/>); client wave 20 (w20colors): on every plate,
        /// <paramref name="line"/> (its four pieces) painted in the row's colour by <see cref="Fill"/>.</summary>
        private static GameObject Frame(RectTransform box, float size, out Image[] line)
        {
            var go = new GameObject("frame", typeof(RectTransform)); go.transform.SetParent(box, false); go.layer = box.gameObject.layer;
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(size, size);
            Ring(rt, size, FrameRimPx, FrameRim);
            line = Ring(rt, size - 2f * FrameRimPx, FramePx - FrameRimPx, Color.white);
            go.SetActive(false);
            return go;
        }
        private static Image[] Ring(RectTransform parent, float size, float w, Color c)
        {
            float h = (size - w) * 0.5f;
            return new[]
            {
                UiKit.Box(parent, "top", new Vector2(0f, h), new Vector2(size, w), c).GetComponent<Image>(),
                UiKit.Box(parent, "bottom", new Vector2(0f, -h), new Vector2(size, w), c).GetComponent<Image>(),
                UiKit.Box(parent, "left", new Vector2(-h, 0f), new Vector2(w, size - 2f * w), c).GetComponent<Image>(),
                UiKit.Box(parent, "right", new Vector2(h, 0f), new Vector2(w, size - 2f * w), c).GetComponent<Image>(),
            };
        }

        // ---- client wave 18 (w18share), the design's point 33: what a friend's copy of this picture needs (UI/GestureShare.cs reads it; a
        // friend's copy is this same class, drawn by UI/FriendGestures.cs through Begin / Draw)
        /// <summary>The strokes as drawn now: the two directions (-2: nothing drawn yet), the corner (the row's middle from the first
        /// stroke on) and the hand's dot, metres in the strokes' plane.</summary>
        public int ShownDir1 => _d1;
        public int ShownDir2 => _d2;
        public Vector2 ShownCorner => _phase >= 1 ? _cancel.anchoredPosition * Scale : Vector2.zero;
        public Vector2 ShownHand => _dot.anchoredPosition * Scale;
        /// <summary>The hand's dot drawn this many times its size (a friend's copy of a picture shown before the eyes).</summary>
        public void SetDotScale(float k) { if (_dot != null) _dot.localScale = Vector3.one * k; }
    }
}
