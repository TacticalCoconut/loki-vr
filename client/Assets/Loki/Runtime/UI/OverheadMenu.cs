using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 13 (w13resize): the OVERHEAD MENU (design note, 2026-10-07: "There is a hidden open transparent menu that sits at around 60
    /// degrees above the player. And it only becomes visible when the player looks up at it. The player can then tap on a button to
    /// toggle the resize mode"). What its controls switch is LokiGame.W13Resize.cs's and LokiGame.W13View.cs's.
    ///   Where     its middle <see cref="Elevation"/> degrees above the horizontal, <see cref="Distance"/> m from the eyes, straight
    ///             ahead of the player's BODY (the heading the wand's holster hangs from, estimated from the head alone:
    ///             VR/WandHolster.cs BodyYaw: it stays put while the head looks around), turned to face the eyes. It goes with the
    ///             head's position, never with its glance: the player looks up AT it.
    ///   Hidden    while the look (the calibrated gaze ray: VR/DioramaRig.Ui.cs GazeRay) is far from its middle it is not drawn (its
    ///             canvas is off) and EVERY collider of it is off: the laser and the look pass through where it would be, it hides
    ///             nothing from the gaze lock and it is no menu for the casting (UI/UiKit.cs PanelKinds: its open test is
    ///             <see cref="Usable"/>).
    ///   Appearing it starts to show when the look comes within <see cref="FadeFrom"/> degrees of its middle and is fully drawn at
    ///             <see cref="WakeAngle"/>; from there it is USABLE (its colliders are on: a panel like any other, pressed with the
    ///             laser or with look + trigger) and stays so, fully drawn, until the look is more than <see cref="SleepAngle"/>
    ///             away (the hysteresis: a look that wanders to a control at its end does not lose it). See-through background.
    ///   Controls  a SWITCH: its choices side by side, the current one lit (gold AND a dot before its name); pressing a choice
    ///             selects that choice. A CYCLE (design note: "by clicking it cycles along the different casting modes"): one large
    ///             button showing every choice side by side with the current one lit the same way; a press anywhere on it moves to
    ///             the next choice, wrapping round; its title carries ↻. Nothing on it is labelled with what a press would choose.
    ///             A control may carry small action buttons after its choices (<see cref="AddExtra"/>: verbs, shown only when their
    ///             test says so: Reposition's Reset, the minimap's - / + / World map).
    ///   Rows      client wave 13 (w13view): five controls (resize mode, the casting gesture, Reposition, the minimap, the forearm bar)
    ///             are one ROW each: the name in a left column, the choices to its right, so the menu stays about as wide as two
    ///             controls side by side were (the sizes: the smoke w13view measures and logs them in degrees).
    ///   Placed    client wave 14 (w14menu), point 8 (design note: "While in reposition mode, I should also be able to move the menu that floats
    ///             above me"): one of Reposition's things (LokiGame.W14Menu.cs): taken by the hand at it or the pointing laser + grip,
    ///             carried, resized by the other grip, kept per player (VR/Comfort.cs OverheadOffset: the BODY's frame from the eyes, so
    ///             it still goes with the head's position and the body's heading, never the glance), Reset puts it back. While
    ///             Reposition is on it is always drawn, at least <see cref="PlacingAlpha"/> (so it can be found and taken; usable only
    ///             when looked at, as ever); while it is carried it is fully drawn and usable (Reposition is ended from it). Outside
    ///             Reposition: invisible until looked at, as before.
    ///   Pages     client wave 20 (w20bubbles), point 56 (design note: "the floating menu above the player will have a section called Quick Use
    ///             Bubbles that you click, and it transforms that floating menu into a menu that controls the bubbles"): the menu shows ONE
    ///             page of controls at a time. Page 0 is every control added before any <see cref="BeginPage"/>; a section adds its own
    ///             page after BeginPage(n) and turns the menu to it with <see cref="ShowPage"/> (its entry on page 0 is an
    ///             <see cref="AddAction"/> row: a title and one button). The other pages' controls are off (nothing drawn, no collider).
    ///             The test hooks below count and index the SHOWN page's controls; <c>TestButtons</c> counts every page's buttons.
    ///   Look      client wave 21 (w21over), the design's point 58 ("redesign the appearance of the menu that floats above you. require it to be a little
    ///             bit more modern and look a little bit more appealing"; "Do not allow it to lose functionality. require it to still be easily
    ///             readable and usable"): THE PANEL THEME (UI/Theme.cs; the UI theme palette): a
    ///             dark wood frame with a thin gold line and corner scrolls (<see cref="WoodMm"/> mm of wood, standing OUT of the rectangle),
    ///             cream canvas with a leather edge and stitching, the page's name on a small gold plate on the frame's top edge; every
    ///             control a row: a dark line icon in a gold ring (UI/OverheadIcons.cs), its name, then its choices as the Settings rows show
    ///             them (side by side, soft squares, the current one gold with a dot and bold letters), its action buttons after them (a
    ///             verb: a soft square; − / +: small round gold buttons); the rows in GROUPS with a stitched line between them
    ///             (<see cref="Looks"/>: what each control's key is grouped with and its place there; a key nobody listed comes last). The
    ///             hint line stands above the rows. Under the laser or the look a button turns light and gets the theme's gold outline.
    ///             Nothing is smaller or fainter than on the dark see-through panel of waves 13-20: the same px for every letter and every
    ///             laser collider, every button as far from the menu's place sideways as it was (the rectangle grew to the LEFT by the icons'
    ///             column: <see cref="FrameWorld"/> is not centred on the menu's place), dark ink on opaque cream (the smoke w21over
    ///             measures both menus from the eyes). The main page's rows stand <see cref="Lift"/> px above the menu's place (the ninth
    ///             row no longer hangs below the others: the bubbles under the menu keep more room).
    /// </summary>
    public sealed class OverheadMenu : MonoBehaviour
    {
        // (client wave 21, w21over: Scale 0.00045 -> 0.00046 and a row 126 -> 118 px high (CycleFrame 7 -> 3): the rows changed places for the
        // groups, so a control may stand farther off the menu's middle than it did: 2 % more metres a px keep every letter and every hit
        // area at least as large FROM THE EYES as on the menu of waves 13-20 (the smoke w21over's table); the lower rows closer together
        // (16 px between two rows' buttons, it was 24; two choices of a row stay 10 px apart) keep the page off the bubbles under it)
        public const float Elevation = 60f, Distance = 0.75f, Scale = 0.00046f;
        public const float FadeFrom = 36f, WakeAngle = 22f, SleepAngle = 32f, FadeSeconds = 0.15f;
        public const float SegH = 112f, CycleFrame = 3f, RowH = SegH + 2 * CycleFrame, RowGap = 10f, TitleW = 300f, Pad = 22f, Gap = 10f, ExtraGap = 18f, HintH = 46f;
        /// <summary>Client wave 21 (w21over): a control's gold ring (px across) and the room after it; the extra room between two groups (the
        /// stitched line runs in its middle); the room under the last row; how far at most the rows' block stands above the menu's place
        /// (px: a page taller than <see cref="BaseRows"/> plain rows is lifted by half of what it is taller, at most this: the main page's
        /// nine rows, so that its lower edge stays clear of the bubbles); the frame's wood (mm; the theme's own rule would give 20: a
        /// slimmer frame leaves the bubbles under the menu more room).</summary>
        public const float IconPx = 92f, IconGap = 14f, GroupGap = 14f, PadBottom = 10f, Lift = 50f, WoodMm = 14f;
        public const float SwitchSegW = 165f, CycleSegW = 145f;
        /// <summary>Client wave 14 (w14menu), point 8: how solid it is drawn at least while Reposition is on.</summary>
        public const float PlacingAlpha = 0.5f;
        public const int LabelPx = 38, TitlePx = 38;
        public static readonly Color Lit = new Color(0.86f, 0.66f, 0.22f, 1f), LitHover = new Color(1f, 0.82f, 0.4f, 1f), Unlit = new Color(0.12f, 0.14f, 0.21f, 0.92f);
        public static readonly Color LitText = new Color(0.1f, 0.07f, 0.02f, 1f), Bg = new Color(0.05f, 0.06f, 0.10f, 0.55f), Border = new Color(0.85f, 0.7f, 0.35f, 0.8f);
        // (Lit / LitHover / Unlit / LitText are also the elbow and chat buttons' and the bubbles' colours: LokiGame.W13View.cs, UI/QuickBubble.cs;
        // Bg / Border were the dark see-through panel's: the menu itself is the theme's canvas since client wave 21)
        /// <summary>Client wave 21 (w21over): the menu's own ink on its buttons (a little darker than the theme's: each kind of letter is at
        /// least as contrasted as it was on the dark panel: the smoke w21over's table); a round gold button's fill and under the laser (a
        /// lighter gold than the lit choice's: its glyph is as contrasted as the white one on dark blue was).</summary>
        public static readonly Color MenuInk = LitText, DiscGold = new Color(0.933f, 0.776f, 0.369f, 1f), DiscHover = new Color(1f, 0.969f, 0.878f, 1f);

        /// <summary>Client wave 21 (w21over): where each control stands: its group (a stitched line between two groups) and its place in it.
        /// The main page: what the grips arrange (resize mode, Reposition, how a held panel turns) | how you act (the casting gesture, the
        /// look, the laser's walking) | what is shown around you (the minimap, the forearm bar, the Quick Use Bubbles). The bubbles' page:
        /// which bubble | what it holds and how it behaves | its place and size. The controls' own order (their index: the test hooks, the
        /// order the sections add them in) is not changed by this. A key that is not here: after every listed group, in its own order.</summary>
        public static readonly Dictionary<string, (int group, int place)> Looks = new Dictionary<string, (int group, int place)>
        {
            { "resize", (0, 0) }, { "reposition", (0, 1) }, { "tilt", (0, 2) },
            { "casting", (1, 0) }, { "visionaim", (1, 1) }, { "look", (1, 2) }, { "laserwalk", (1, 3) },
            { "minimap", (2, 0) }, { "bar", (2, 1) }, { "bubbles", (2, 2) },
            { "bubble", (0, 0) }, { "holds", (1, 0) }, { "active", (1, 1) }, { "shown", (1, 2) }, { "useby", (1, 3) }, { "move", (2, 0) },
        };

        private sealed class Extra
        {
            public UiButton Button; public float Width, BaseWidth; public Func<bool> Shown; public Func<string> Label;
            /// <summary>Client wave 24 (w24), item 16: a LEADING button: it stands at the row's left end, before the row's icon
            /// (<see cref="AddLead"/>), not after the choices.</summary>
            public bool Lead;
            /// <summary>Client wave 24 (w24), item 11: a SLIDER after the row's choices (<see cref="AddExtraSlider"/>): its track is
            /// the Button; the value's text after it.</summary>
            public UiSlider Slider; public Text Value; public Func<string> ValueLabel;
        }
        private sealed class Control
        {
            public Func<string> Name; public string Key; public Text Title; public float Width; public bool Cycle; public int Count;
            /// <summary>Client wave 20 (w20bubbles), point 56: the page it is on (0: the main page); an action row (one plain button, never lit).</summary>
            public int Page; public bool Action;
            /// <summary>Client wave 21 (w21over): its index (the order it was added in), its group and place there (<see cref="Looks"/>), its gold ring.</summary>
            public int Index, Group, Place; public UiRingIcon Ring;
            /// <summary>Client wave 14 (w14ui): one choice's width now (px): the widest of its labels, lit, on one line (<see cref="FitChoices"/>).</summary>
            public float SegW;
            public Func<int> Get; public Action<int> Pick; public Func<int, string> Label;
            public readonly List<UiButton> Buttons = new List<UiButton>(); // a switch: one per choice; a cycle: one
            public readonly List<Image> SegBg = new List<Image>(); public readonly List<Text> SegText = new List<Text>();
            public readonly List<Extra> Extras = new List<Extra>();
            public int Was = -1; public bool HoverWas;
            public float ChoicesWidth => Width;
            public float RowWidth { get { float w = Width; int k = 0; for (int i = 0; i < Extras.Count; i++) { if (Extras[i].Lead) continue; w += (k++ == 0 ? ExtraGap : Gap) + Extras[i].Width; } return w; } }
            /// <summary>Client wave 24 (w24): the width its leading buttons take at the row's left end (0: none).</summary>
            public float LeadWidth { get { float w = 0f; foreach (var e in Extras) if (e.Lead) w += (w > 0f ? Gap : 0f) + e.Width; return w; } }

        }
        private RectTransform _rt; private Canvas _canvas; private CanvasGroup _group; private Camera _head;
        private readonly List<Control> _controls = new List<Control>();
        private Collider[] _cols = new Collider[0]; private RectTransform _border, _bg; private Text _hint;
        // ---- client wave 21 (w21over): the frame's reach beyond the rectangle (px), the rectangle's middle against the menu's place (px), the
        // title plate and each page's name, the stitched lines between the groups, the shown page's controls in the drawn order
        private float _outset; private Vector2 _mid; private UiTitlePlate _plate;
        private readonly Dictionary<int, Func<string>> _pageNames = new Dictionary<int, Func<string>>();
        private readonly List<StitchGraphic> _rules = new List<StitchGraphic>(); private readonly List<Control> _order = new List<Control>();
        /// <summary>Client wave 21 (w21over): the name on the gold plate while page <paramref name="page"/> is shown (none: no plate).</summary>
        public void SetPageName(int page, Func<string> name) { _pageNames[Mathf.Max(0, page)] = name; _repaint = true; }
        private bool _layout = true;
        // ---- client wave 20 (w20bubbles), point 56: the page shown, the page new controls go on, the shown page's controls, a repaint due
        private int _page, _addPage; private bool _repaint;
        private readonly List<Control> _shown = new List<Control>();
        /// <summary>Client wave 20 (w20bubbles), point 56: the page shown now (0: the main page).</summary>
        public int Page => _page;
        /// <summary>The controls added from now on go on page <paramref name="page"/> (0: the main page).</summary>
        public void BeginPage(int page) { _addPage = Mathf.Max(0, page); }
        /// <summary>Turns the menu to page <paramref name="page"/> (its controls laid out and painted anew at once).</summary>
        public void ShowPage(int page, string why)
        {
            page = Mathf.Max(0, page); if (page == _page) return;
            Debug.Log($"[Overhead] page {_page} → {page} ({why})");
            _page = page; _layout = true; _repaint = true; PageTurns++;
        }
        /// <summary>Pages turned so far (smoke, log).</summary>
        public int PageTurns { get; private set; }
        /// <summary>Lays out and paints the shown page anew (a label or a title that depends on state changed: a section's own change).</summary>
        public void Refresh() { _layout = true; _repaint = true; }
        /// <summary>The shown page's control i (index within the shown page).</summary>
        private Control At(int i)
        {
            int k = 0;
            foreach (var c in _controls) { if (c.Page != _page) continue; if (k == i) return c; k++; }
            throw new ArgumentOutOfRangeException(nameof(i), $"page {_page} has {k} controls, not {i + 1}");
        }
        /// <summary>Usable now: fully drawn, its colliders on.</summary>
        public bool Usable { get; private set; }
        /// <summary>How solid it is drawn (0: not drawn at all), the angle between the look and its middle, its middle in the world.</summary>
        public float Alpha { get; private set; }
        public float Angle { get; private set; } = 180f;
        public Vector3 Centre => transform.position;
        /// <summary>The SHOWN page's controls (client wave 20: one page at a time; page 0 holds every control of waves 13-19 and the entry of
        /// the Quick Use Bubbles).</summary>
        public int ControlCount { get { int n = 0; foreach (var c in _controls) if (c.Page == _page) n++; return n; } }
        /// <summary>Times it woke (became usable) and presses of its controls (smoke, log).</summary>
        public int Wakes { get; private set; }
        public int Presses { get; private set; }
        /// <summary>The one line under the controls (what the mode that is on does; empty: nothing is on).</summary>
        public string Hint { get => _hint.text; set { if (_hint.text != (value ?? "")) _hint.text = value ?? ""; } }
        /// <summary>Its size in metres (width, height).</summary>
        public Vector2 Size => _rt.sizeDelta * Scale * SizeFactor;
        // ---- client wave 14 (w14menu), point 8: Reposition
        /// <summary>Reposition is on (LokiGame.W14Menu.cs): drawn at least <see cref="PlacingAlpha"/>; carried now: fully drawn and usable.</summary>
        public bool Placing { get; set; }
        public bool Held { get; set; }
        /// <summary>Its size factor (Reposition's resize; 1: as built).</summary>
        public float SizeFactor => VR.Comfort.OverheadScale;
        private float _yaw;
        /// <summary>Its default place: <see cref="Elevation"/>° up, <see cref="Distance"/> m ahead in the body's frame (right, up, ahead).</summary>
        public static Vector3 DefaultOffset => Quaternion.Euler(-Elevation, 0f, 0f) * Vector3.forward * Distance;
        /// <summary>Its place now in the body's frame from the eyes.</summary>
        public static Vector3 Offset => VR.Comfort.OverheadPlaced ? VR.Comfort.OverheadOffset : DefaultOffset;
        /// <summary>Its frame in the world: the middle, half its width along its right, half its height along its up (metres).</summary>
        /// <summary>(Client wave 21, w21over: the menu AS DRAWN, its wood frame too: what Reposition takes hold of and the air pen floats beside;
        /// its middle is not the menu's place: the rectangle's middle is _mid px from it.)</summary>
        public (Vector3 c, Vector3 r, Vector3 u) FrameWorld { get { float s = Scale * SizeFactor; var sz = (_rt.sizeDelta * 0.5f + new Vector2(_outset, _outset)) * s; return (transform.position + transform.right * (_mid.x * s) + transform.up * (_mid.y * s), transform.right * sz.x, transform.up * sz.y); } }
        /// <summary>Client wave 20 (w20bubbles), point 56: a page's rows beyond this many hang BELOW the box the first ones make (the main page's ninth
        /// row, "Quick Use Bubbles", under wave 14's eight): the menu's place (its transform, what the look must come within <see cref="WakeAngle"/>
        /// of) stays the middle of the first <see cref="BaseRows"/> rows, so those keep their angles from it (a page simply one row taller put the
        /// Portuguese "Casting gesture" row's last choice 23.1° from the middle, beyond the wake angle: run w14ui183821), and the new row is the
        /// one nearest the bubbles below. Pages of up to BaseRows rows are centred as before.</summary>
        public const int BaseRows = 8; // (client wave 21, w21over: no row hangs any more: a page's rows are one block; a page taller than BaseRows plain rows is lifted, at most Lift px)
        /// <summary>How far (metres) the panel's rectangle is lower than centred on the menu's place now (negative: higher).</summary>
        public float Drop => -_mid.y * Scale * SizeFactor;
        /// <summary>Carried by Reposition to <paramref name="world"/> at size <paramref name="factor"/> (kept in VR/Comfort.cs; saved at the let go).</summary>
        public void CarryTo(Vector3 world, float factor)
        {
            if (_head == null) return;
            VR.Comfort.OverheadPlaced = true;
            VR.Comfort.OverheadOffset = VR.Comfort.ClampOverheadPlace(Quaternion.Inverse(Quaternion.Euler(0f, _yaw, 0f)) * (world - _head.transform.position));
            VR.Comfort.OverheadScale = Mathf.Clamp(factor, VR.Comfort.PlaceMinScale, VR.Comfort.PlaceMaxScale);
            PlaceNow();
        }
        /// <summary>Puts it where its place says now (the eyes' position, the body's heading of the last Tick).</summary>
        public void PlaceNow()
        {
            if (_head == null) return;
            transform.position = _head.transform.position + Quaternion.Euler(0f, _yaw, 0f) * Offset;
            transform.localScale = Vector3.one * (Scale * SizeFactor);
            UiKit.FaceHead(transform, _head);
        }

        public static OverheadMenu Create(Transform parent, Camera head)
        {
            // client wave 21 (w21over): the theme's framed panel (UI/UiKit.cs Panel: the name is out of Theme.LegacyNames). The frame's width
            // comes from the size the panel is made with (UI/Theme.cs Unit: the wood is 6 % of the smaller side): made WoodMm wide here, and
            // kept when Layout gives the rectangle its real size
            var rt = UiKit.Panel("OverheadMenu", parent, new Vector2(900f, WoodMm * 0.001f / Theme.WoodShare / Scale), Scale);
            var m = rt.gameObject.AddComponent<OverheadMenu>();
            m._rt = rt; m._head = head; m._canvas = rt.GetComponent<Canvas>(); m._group = rt.gameObject.AddComponent<CanvasGroup>();
            m._border = (RectTransform)rt.Find("border"); m._bg = (RectTransform)rt.Find("bg");
            m._outset = UiKit.OutsetOf(rt);
            for (int i = 0; i < 4; i++) { var rule = UiKit.StitchLine(rt, Vector2.zero, new Vector2(100f, 0f)); rule.gameObject.SetActive(false); m._rules.Add(rule); }
            m._plate = UiKit.TitlePlate(rt, "", 34, 700f);
            m._hint = UiKit.Label(rt, "", Vector2.zero, new Vector2(560, HintH - 6), 30, TextAnchor.MiddleCenter, Theme.Ink);
            m._hint.fontStyle = FontStyle.Bold;
            m._hint.resizeTextForBestFit = true; m._hint.resizeTextMinSize = 22; m._hint.resizeTextMaxSize = 30;
            PanelKinds.SetOpenTest(rt, () => m != null && m.Usable); // a menu for the new casting only while it can be used
            m.Apply(0f, false);
            return m;
        }

        /// <summary>A switch: its choices side by side (labels: <paramref name="label"/>), the current one (<paramref name="get"/>) lit;
        /// pressing a choice calls <paramref name="pick"/> with it (not when it is already the current one).</summary>
        public void AddSwitch(string key, Func<string> name, int count, Func<int> get, Action<int> pick, Func<int, string> label)
        {
            var c = new Control { Key = key, Name = name, Count = count, Get = get, Pick = pick, Label = label, Width = count * SwitchSegW + (count - 1) * Gap, SegW = SwitchSegW, Page = _addPage };
            Dress(c); c.Title = Title(name());
            for (int i = 0; i < count; i++)
            {
                int k = i;
                var b = UiKit.Button(_rt, "", Vector2.zero, new Vector2(SwitchSegW, SegH), () => { Presses++; if (c.Get() != k) c.Pick(k); }, LabelPx);
                b.name = "switch:" + key + ":" + i;
                c.Buttons.Add(b); c.SegBg.Add(b.GetComponent<Image>()); c.SegText.Add(b.Text);
            }
            _controls.Add(c); _layout = true;
        }

        /// <summary>A cycle: one button showing every choice side by side, the current one lit; a press anywhere on it calls
        /// <paramref name="next"/> (the owner moves to the next choice).</summary>
        public void AddCycle(string key, Func<string> name, int count, Func<int> get, Action next, Func<int, string> label)
        {
            var c = new Control { Key = key, Name = name, Count = count, Get = get, Pick = _ => next(), Label = label, Cycle = true, Width = count * CycleSegW + (count - 1) * Gap + 2 * CycleFrame, SegW = CycleSegW, Page = _addPage };
            Dress(c); c.Title = Title(name() + "  ↻");
            var b = UiKit.Button(_rt, "", Vector2.zero, new Vector2(c.Width, SegH + 2 * CycleFrame), () => { Presses++; next(); }, LabelPx);
            b.name = "cycle:" + key;
            var brt = (RectTransform)b.transform;
            for (int i = 0; i < count; i++)
            {
                var seg = UiKit.Soft(brt, "choice" + i, new Vector2(-c.Width / 2 + CycleFrame + CycleSegW / 2 + i * (CycleSegW + Gap), 0), new Vector2(CycleSegW, SegH), Theme.Cell);
                var t = UiKit.Label(seg, "", Vector2.zero, new Vector2(CycleSegW - 10, SegH - 8), LabelPx, TextAnchor.MiddleCenter, MenuInk);
                t.resizeTextForBestFit = true; t.resizeTextMinSize = 24; t.resizeTextMaxSize = LabelPx;
                c.SegBg.Add(seg.GetComponent<Image>()); c.SegText.Add(t);
            }
            c.Buttons.Add(b);
            _controls.Add(c); _layout = true;
        }

        /// <summary>Client wave 20 (w20bubbles), point 56: an ACTION row: its title, then one plain button (a choice's size, the button colour,
        /// never lit) whose text is <paramref name="label"/> (read anew at every layout: <see cref="Refresh"/>); a press calls
        /// <paramref name="press"/>. Action buttons may follow it (<see cref="AddExtra"/>).</summary>
        public UiButton AddAction(string key, Func<string> name, Func<string> label, Action press)
        {
            var c = new Control { Key = key, Name = name, Count = 1, Get = () => -1, Pick = _ => press(), Label = _ => label(), Width = SwitchSegW, SegW = SwitchSegW, Page = _addPage, Action = true };
            Dress(c); c.Title = Title(name());
            var b = UiKit.Button(_rt, "", Vector2.zero, new Vector2(SwitchSegW, SegH), () => { Presses++; press(); }, LabelPx);
            b.name = "action:" + key;
            c.Buttons.Add(b); c.SegBg.Add(b.GetComponent<Image>()); c.SegText.Add(b.Text);
            _controls.Add(c); _layout = true;
            return b;
        }

        /// <summary>Client wave 13 (w13view): a small action button (a verb) after the choices of the control added last, shown only
        /// while <paramref name="shown"/> says so (its place is kept either way: nothing moves under the laser).</summary>
        public UiButton AddExtra(string key, Func<string> label, float widthPx, Action press, Func<bool> shown, int fontPx = LabelPx)
        {
            var c = _controls[_controls.Count - 1];
            // client wave 21 (w21over): a one-glyph verb (− / +) is the theme's small round gold button (the same laser collider: the whole
            // widthPx x SegH); a worded one (Reset, World map, Back, Clear) the plain soft square
            string first = label() ?? ""; bool round = UiKit.StripTags(first).Trim().Length <= 1;
            var b = round ? UiKit.RoundButton(_rt, first, Vector2.zero, new Vector2(widthPx, SegH), () => { Presses++; press(); }, fontPx)
                          : UiKit.Button(_rt, first, Vector2.zero, new Vector2(widthPx, SegH), () => { Presses++; press(); }, fontPx);
            if (round && b.Round) { b.Normal = DiscGold; b.Hover = DiscHover; b.SetHover(false); }
            if (b.Text != null) b.Text.color = MenuInk;
            b.name = "extra:" + c.Key + ":" + key;
            c.Extras.Add(new Extra { Button = b, Width = widthPx, BaseWidth = widthPx, Shown = shown, Label = label });
            _layout = true;
            return b; // (client wave 14, w14menu: its tip may be set)
        }

        /// <summary>Client wave 24 (w24), item 16 (design note: "the back button should not be all the way to the right. It should be all the
        /// way to the left. So on the first row, it should be back, the icon indicating a bubble, then the text bubble, and then
        /// one, two, three, four"): a worded button at the LEFT end of the row of the control added last, before its icon. It
        /// stands where that row's icon stood; the row's icon and name move right by its width (the name's room is that much
        /// narrower on this row). The page is no wider and no button is farther from the menu's middle than the icons' column.</summary>
        public UiButton AddLead(string key, Func<string> label, float widthPx, Action press)
        {
            var b = AddExtra(key, label, widthPx, press, null);
            var c = _controls[_controls.Count - 1]; c.Extras[c.Extras.Count - 1].Lead = true; // (its object keeps an action button's name, "extra:<row>:<key>": what finds it by name still does)
            return b;
        }

        /// <summary>Client wave 24 (w24), item 11: a SLIDER after the choices of the control added last (no row of its own: the
        /// page is no taller): a track <see cref="SliderW"/> px wide and a button's height (the kit's slider: the laser places
        /// and drags the knob, the pointing hand's stick steps it) and its value as a text after it (<see cref="ValueW"/> px).
        /// The two together are as wide as two choices (340 px), so a row that carries one is no wider than the widest row was:
        /// the page does not grow and nothing on it moves (the menu is centred on its names and choices).</summary>
        public const float SliderW = 170f, ValueW = 2f * SwitchSegW + Gap - SliderW - Gap;
        public UiSlider AddExtraSlider(string key, float min, float max, float step, Func<float> get, Action<float> set, Action commit, Func<string> label)
        {
            var c = _controls[_controls.Count - 1];
            var s = UiSlider.Create(_rt, Vector2.zero, new Vector2(SliderW, SegH));
            s.Bind(new UiSlider.Spec { Min = min, Max = max, Step = step, Get = get, Set = v => { set(v); _repaint = true; }, Commit = commit });
            s.Track.name = "slider:" + key;
            var t = UiKit.Label(_rt, "", Vector2.zero, new Vector2(ValueW, SegH), LabelPx, TextAnchor.MiddleLeft, MenuInk);
            t.name = "value:" + key; t.resizeTextForBestFit = true; t.resizeTextMinSize = 24; t.resizeTextMaxSize = LabelPx;
            c.Extras.Add(new Extra { Button = s.Track, Width = SliderW + Gap + ValueW, BaseWidth = SliderW + Gap + ValueW, Shown = null, Label = () => "", Slider = s, Value = t, ValueLabel = label });
            _layout = true;
            return s;
        }

        private Text Title(string name)
        {
            var t = UiKit.Label(_rt, name, Vector2.zero, new Vector2(TitleW, RowH), TitlePx, TextAnchor.MiddleLeft, Theme.Ink);
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 24; t.resizeTextMaxSize = TitlePx;
            return t;
        }

        /// <summary>Client wave 21 (w21over): a new control's index, its group and place (<see cref="Looks"/>) and its gold ring with the
        /// control's line icon (UI/OverheadIcons.cs: drawn in code).</summary>
        private void Dress(Control c)
        {
            c.Index = _controls.Count;
            if (Looks.TryGetValue(c.Key ?? "", out var look)) { c.Group = look.group; c.Place = look.place; } else { c.Group = 1000; c.Place = c.Index; }
            c.Ring = UiKit.RingIcon(_rt, Vector2.zero, IconPx);
            c.Ring.name = "ring:" + c.Key;
            c.Ring.Icon.sprite = OverheadIcons.For(c.Key); c.Ring.Icon.color = Theme.Ink; c.Ring.Icon.enabled = true;
            ((RectTransform)c.Ring.Icon.transform).sizeDelta = new Vector2(IconPx * 0.64f, IconPx * 0.64f);
        }

        // ---- client wave 14 (w14ui), the "● Upright" label (the design's headset test of wave 13: the overhead menu's "● Upright" wrapped on two
        // lines): every choice fits on ONE line. Each control's choices are as wide as its widest label needs, lit (the "● " and bold: the
        // widest form), at the full <see cref="LabelPx"/>, plus <see cref="SegPadPx"/> on each side, never narrower than before (165 px a
        // switch's choice, 145 a cycle's); the texts never wrap. Measured anew when the language changes (at each waking).
        public const float SegPadPx = 12f;
        private string _fitSig;
        /// <summary>The width (px) a label takes on one line, lit (bold, with its dot), at the choices' full size.</summary>
        private float LitWidth(Text t, string label)
        {
            var gs = t.GetGenerationSettings(new Vector2(10000f, SegH));
            gs.fontSize = LabelPx; gs.fontStyle = FontStyle.Bold; gs.resizeTextForBestFit = false;
            gs.horizontalOverflow = HorizontalWrapMode.Overflow; gs.verticalOverflow = VerticalWrapMode.Overflow; gs.scaleFactor = 1f;
            return t.cachedTextGeneratorForLayout.GetPreferredWidth("● " + label, gs);
        }
        private string FitSignature() { var sb = new System.Text.StringBuilder(); foreach (var c in _controls) { for (int i = 0; i < c.Count; i++) sb.Append(c.Label(i)).Append('|'); foreach (var e in c.Extras) sb.Append(e.Label()).Append('|'); } return sb.ToString(); }
        private void FitChoices()
        {
            _fitSig = FitSignature();
            foreach (var c in _controls)
            {
                float need = c.Cycle ? CycleSegW : SwitchSegW;
                for (int i = 0; i < c.Count; i++) need = Mathf.Max(need, Mathf.Ceil(LitWidth(c.SegText[i], c.Label(i))) + 2f * SegPadPx);
                c.SegW = need;
                if (c.Cycle)
                {
                    c.Width = c.Count * need + (c.Count - 1) * Gap + 2 * CycleFrame;
                    var brt = (RectTransform)c.Buttons[0].transform; brt.sizeDelta = new Vector2(c.Width, SegH + 2 * CycleFrame);
                    var bt = c.Buttons[0].Text; if (bt != null) ((RectTransform)bt.transform).sizeDelta = new Vector2(c.Width - 12, SegH + 2 * CycleFrame - 4);
                    var col = c.Buttons[0].GetComponent<BoxCollider>(); if (col != null) col.size = new Vector3(c.Width, SegH + 2 * CycleFrame, col.size.z);
                    for (int i = 0; i < c.Count; i++)
                    {
                        var seg = (RectTransform)c.SegBg[i].transform; seg.sizeDelta = new Vector2(need, SegH); seg.anchoredPosition = new Vector2(-c.Width / 2 + CycleFrame + need / 2 + i * (need + Gap), 0);
                        ((RectTransform)c.SegText[i].transform).sizeDelta = new Vector2(need - 10, SegH - 8);
                    }
                }
                else
                {
                    c.Width = c.Count * need + (c.Count - 1) * Gap;
                    for (int i = 0; i < c.Count; i++)
                    {
                        var b = c.Buttons[i]; var brt = (RectTransform)b.transform; brt.sizeDelta = new Vector2(need, SegH);
                        ((RectTransform)c.SegText[i].transform).sizeDelta = new Vector2(need - 12, SegH - 4);
                        var col = b.GetComponent<BoxCollider>(); if (col != null) col.size = new Vector3(need, SegH, col.size.z);
                    }
                }
                foreach (var t in c.SegText) t.horizontalOverflow = HorizontalWrapMode.Overflow; // (sized to fit: one line, never wrapped)
                // the action buttons after the choices (Reset, - / + / World map): the same, at their own size, not lit
                foreach (var e in c.Extras)
                {
                    if (e.Slider != null) continue; // (client wave 24: a slider keeps its own sizes)
                    var t = e.Button.Text; if (t == null) continue;
                    var gs = t.GetGenerationSettings(new Vector2(10000f, SegH));
                    gs.fontSize = t.fontSize; gs.resizeTextForBestFit = false; gs.horizontalOverflow = HorizontalWrapMode.Overflow; gs.verticalOverflow = VerticalWrapMode.Overflow; gs.scaleFactor = 1f;
                    e.Width = Mathf.Max(e.BaseWidth, Mathf.Ceil(t.cachedTextGeneratorForLayout.GetPreferredWidth(e.Label(), gs)) + 2f * SegPadPx);
                    ((RectTransform)e.Button.transform).sizeDelta = new Vector2(e.Width, SegH); ((RectTransform)t.transform).sizeDelta = new Vector2(e.Width - 12, SegH - 4);
                    var col = e.Button.GetComponent<BoxCollider>(); if (col != null) col.size = new Vector3(e.Width, SegH, col.size.z);
                    t.horizontalOverflow = HorizontalWrapMode.Overflow;
                }
            }
        }

        private void Layout()
        {
            _layout = false;
            FitChoices(); // client wave 14 (w14ui): every choice on one line
            // client wave 20 (w20bubbles), point 56: only the shown page's controls are laid out and on; the others' objects are off
            _shown.Clear(); foreach (var c in _controls) { bool on = c.Page == _page; if (on) _shown.Add(c); SetOn(c, on); }
            // client wave 21 (w21over): the drawn order: by group, then by the place in it (UI/OverheadMenu.cs Looks)
            _order.Clear(); _order.AddRange(_shown);
            _order.Sort((a, b) => a.Group != b.Group ? a.Group.CompareTo(b.Group) : a.Place != b.Place ? a.Place.CompareTo(b.Place) : a.Index.CompareTo(b.Index));
            float rows = 0f; foreach (var c in _shown) rows = Mathf.Max(rows, c.RowWidth);
            rows = Mathf.Max(rows, 600f - 2f * Pad - TitleW - Gap);
            int n = _order.Count, breaks = 0; for (int i = 1; i < n; i++) if (_order[i].Group != _order[i - 1].Group) breaks++;
            float block = n * RowH + Mathf.Max(0, n - 1) * RowGap + breaks * GroupGap;
            // everything below is measured from the MENU'S PLACE (its transform: what the look must come within WakeAngle of). The choices
            // begin where they began on the menu of waves 13-20 (which was centred on its place): so every button is as far from the
            // menu's middle sideways as it was, and the icons' column is new room to the LEFT. The rows' block is one piece around the place
            // (a block taller than BaseRows plain rows: lifted by half of what it is taller, at most Lift px); the hint line above it
            float lift = Mathf.Clamp((block - (BaseRows * RowH + (BaseRows - 1) * RowGap)) * 0.5f, 0f, Lift);
            float x0 = -(Pad + TitleW + Gap + rows + Pad) * 0.5f + Pad + TitleW + Gap;
            // client wave 24 (w24), item 16: a page with a button at a row's LEFT end (the bubbles' page's Back) is centred on the
            // menu's place as a whole, its icons' column too: that button is then no farther from the menu's middle than the
            // page's right end (measured: 19.6° in English, 21.9° in Portuguese; where the icons' column was extra room to the left
            // it was 21.0° and 23.2°, beyond the 22° within which the menu wakes). A page without one: as before.
            { float lead = 0f; foreach (var c in _shown) lead = Mathf.Max(lead, c.LeadWidth); if (lead > 0f) x0 = -(Pad + IconPx + IconGap + TitleW + Gap + rows + Pad) * 0.5f + Pad + IconPx + IconGap + TitleW + Gap; }
            float left = x0 - Gap - TitleW - IconGap - IconPx - Pad, right = x0 + rows + Pad;
            float top = block * 0.5f + lift + HintH + Pad * 0.5f, bottom = -block * 0.5f + lift - PadBottom;
            float w = right - left, h = top - bottom;
            _mid = new Vector2((left + right) * 0.5f, (top + bottom) * 0.5f);
            _rt.pivot = new Vector2(0.5f - _mid.x / w, 0.5f - _mid.y / h);
            _rt.sizeDelta = new Vector2(w, h);
            _border.sizeDelta = new Vector2(w, h); _bg.sizeDelta = new Vector2(w - 6, h - 6);
            // (its own collider behind the whole menu, the frame too: the laser stops on it)
            var col = _rt.GetComponent<BoxCollider>(); if (col != null) { col.size = new Vector3(w + 2f * _outset, h + 2f * _outset, 4f); col.center = new Vector3(_mid.x, _mid.y, col.center.z); }
            // (a child's place is counted from the rectangle's middle)
            Vector2 Rel(float x, float y) => new Vector2(x - _mid.x, y - _mid.y);
            float yy = block * 0.5f + lift - RowH / 2; int rule = 0;
            for (int k = 0; k < n; k++)
            {
                var c = _order[k];
                // client wave 24 (w24), item 16: a row with leading buttons: they stand where its icon stood; its icon and its name
                // move right by their width (the name's room is that much narrower)
                float lw = c.LeadWidth, shift = lw > 0f ? lw + Gap : 0f, tw = TitleW - shift;
                ((RectTransform)c.Ring.transform).anchoredPosition = Rel(left + Pad + shift + IconPx / 2, yy);
                var trt = (RectTransform)c.Title.transform; trt.anchoredPosition = Rel(x0 - Gap - tw / 2, yy); trt.sizeDelta = new Vector2(tw, RowH);
                if (c.Cycle) ((RectTransform)c.Buttons[0].transform).anchoredPosition = Rel(x0 + c.Width / 2, yy);
                else for (int i = 0; i < c.Buttons.Count; i++) ((RectTransform)c.Buttons[i].transform).anchoredPosition = Rel(x0 + c.SegW / 2 + i * (c.SegW + Gap), yy);
                float x = x0 + c.Width, lx = left + Pad; int after = 0;
                for (int i = 0; i < c.Extras.Count; i++)
                {
                    var e = c.Extras[i];
                    if (e.Lead) { ((RectTransform)e.Button.transform).anchoredPosition = Rel(lx + e.Width / 2, yy); lx += e.Width + Gap; continue; } // (client wave 24, item 16: at the row's left end)
                    x += after++ == 0 ? ExtraGap : Gap;
                    if (e.Slider != null)
                    {
                        // client wave 24 (w24), item 11: the track, then its value's text
                        e.Slider.Place(Rel(x + SliderW / 2, yy), SliderW);
                        ((RectTransform)e.Value.transform).anchoredPosition = Rel(x + SliderW + Gap + ValueW / 2, yy); x += e.Width; continue;
                    }
                    ((RectTransform)e.Button.transform).anchoredPosition = Rel(x + e.Width / 2, yy); x += e.Width;
                }
                yy -= RowH + RowGap;
                if (k + 1 < n && _order[k + 1].Group != c.Group)
                {
                    // the stitched line between two groups, in the middle of the room between their rows
                    if (rule < _rules.Count)
                    {
                        var st = _rules[rule++]; var srt = st.rectTransform; float ry = yy + RowH / 2 + RowGap / 2 - GroupGap / 2;
                        srt.anchoredPosition = Rel((left + right) * 0.5f, ry); srt.sizeDelta = new Vector2(w - 2f * Pad, 8f);
                        if (!st.gameObject.activeSelf) st.gameObject.SetActive(true);
                        st.SetVerticesDirty();
                    }
                    yy -= GroupGap;
                }
            }
            for (; rule < _rules.Count; rule++) if (_rules[rule].gameObject.activeSelf) _rules[rule].gameObject.SetActive(false);
            var hrt = (RectTransform)_hint.transform; hrt.anchoredPosition = Rel((left + right) * 0.5f, block * 0.5f + lift + HintH / 2); hrt.sizeDelta = new Vector2(w - 2 * Pad, HintH - 6);
            _cols = _rt.GetComponentsInChildren<Collider>(true);
            foreach (var cl in _cols) cl.enabled = Usable;
        }

        /// <summary>Client wave 20 (w20bubbles), point 56: a control's title and buttons on (its page is shown) or off; a shown page's action
        /// buttons are then shown or not by their own test (<see cref="Paint"/>).</summary>
        private static void SetOn(Control c, bool on)
        {
            if (c.Title.gameObject.activeSelf != on) c.Title.gameObject.SetActive(on);
            if (c.Ring != null && c.Ring.gameObject.activeSelf != on) c.Ring.gameObject.SetActive(on);
            foreach (var b in c.Buttons) if (b.gameObject.activeSelf != on) b.gameObject.SetActive(on);
            if (!on) foreach (var e in c.Extras) { if (e.Button.gameObject.activeSelf) e.Button.gameObject.SetActive(false); if (e.Value != null && e.Value.gameObject.activeSelf) e.Value.gameObject.SetActive(false); } // (client wave 24: a slider's value text with it)
        }

        /// <summary>Repaints a control whose state (or hover) changed: the current choice lit (gold, a dot, bold), the others plain; the
        /// action buttons shown or not.</summary>
        private void Paint(bool force)
        {
            if (force && _plate != null) { string pn = _pageNames.TryGetValue(_page, out var f) && f != null ? f() ?? "" : ""; if (_plate.Text.text != pn) _plate.Set(pn); } // (client wave 21: the page's name on the gold plate)
            foreach (var c in _shown) // (client wave 20, w20bubbles: the shown page's controls only)
            {
                foreach (var e in c.Extras)
                {
                    bool on = e.Shown == null || e.Shown();
                    if (e.Button.gameObject.activeSelf != on) e.Button.gameObject.SetActive(on);
                    if (e.Slider != null)
                    {
                        // client wave 24 (w24), item 11: the knob at the value, the value as a text
                        if (e.Value.gameObject.activeSelf != on) e.Value.gameObject.SetActive(on);
                        if (on) { e.Slider.Refresh(); string vt = e.ValueLabel(); if (e.Value.text != vt) e.Value.text = vt; }
                        continue;
                    }
                    if (force && on) e.Button.SetLabel(e.Label());
                }
                int now = c.Get(); bool hover = c.Cycle && c.Buttons[0].Hovered;
                if (!force && now == c.Was && hover == c.HoverWas) continue;
                c.Was = now; c.HoverWas = hover;
                if (force) c.Title.text = c.Cycle ? c.Name() + "  ↻" : c.Name();
                if (c.Action)
                {
                    // client wave 20 (w20bubbles), point 56: an action row's one button: its label, plain (never lit: it is a verb, not a state)
                    // (client wave 21, w21over: the theme's plain button: a soft square, the menu's dark ink)
                    var ab = c.Buttons[0]; c.SegText[0].text = c.Label(0); c.SegText[0].color = MenuInk; c.SegText[0].fontStyle = FontStyle.Normal;
                    ab.Normal = Theme.Cell; ab.Hover = Theme.CellHover; ab.SetHover(ab.Hovered);
                    continue;
                }
                for (int i = 0; i < c.Count; i++)
                {
                    bool lit = i == now; string label = c.Label(i);
                    c.SegText[i].text = lit ? "● " + label : label;
                    // (client wave 21, w21over: the theme's colours: the current choice gold, the others soft squares; dark ink on both)
                    c.SegText[i].color = lit ? LitText : MenuInk; c.SegText[i].fontStyle = lit ? FontStyle.Bold : FontStyle.Normal;
                    if (c.Cycle) c.SegBg[i].color = lit ? (hover ? LitHover : Theme.Lit) : (hover ? Theme.CellHover : Theme.Cell);
                    else { var b = c.Buttons[i]; b.Normal = lit ? Theme.Lit : Theme.Cell; b.Hover = lit ? LitHover : Theme.CellHover; b.SetHover(b.Hovered); } // (LitHover: the lit choice under the laser as light as it was)
                }
            }
        }

        /// <summary>Once a frame: the place above the head, the look's angle to it, the fade and the colliders.</summary>
        public void Tick(float bodyYaw, Ray look, float dt)
        {
            if (_head == null) return;
            if (_layout) Layout();
            // Follow the head's horizontal heading: 1:1 when not awake so looking up from any direction finds it directly overhead,
            // and smoothly when awake if the head turns more than 12° so looking across its buttons stays steady while turning brings it along.
            if (!Usable && !Held) _yaw = bodyYaw;
            else if (!Held)
            {
                float diff = Mathf.DeltaAngle(_yaw, bodyYaw);
                if (Mathf.Abs(diff) > 12f)
                {
                    float targetYaw = bodyYaw - Mathf.Sign(diff) * 12f;
                    _yaw = Mathf.LerpAngle(_yaw, targetYaw, 1f - Mathf.Exp(-10f * dt));
                }
            }
            PlaceNow();
            var pos = transform.position;
            Angle = Vector3.Angle(look.direction, pos - look.origin);
            bool wake = false;
            if (!Usable && (Angle <= WakeAngle || Held)) { Usable = true; Wakes++; wake = true; if (FitSignature() != _fitSig) Layout(); } // (client wave 14: the labels changed: measured anew)
            else if (Usable && Angle > SleepAngle && !Held) Usable = false; // (client wave 14: carried, it stays usable)
            float target = Usable ? 1f : Mathf.Clamp01((FadeFrom - Angle) / (FadeFrom - WakeAngle));
            if (Placing) target = Mathf.Max(target, PlacingAlpha); // (client wave 14, point 8: in Reposition it can always be found)
            Apply(Mathf.MoveTowards(Alpha, target, dt / FadeSeconds), Usable);
            if (Alpha > 0f) { Paint(wake || _repaint); _repaint = false; } // (painted anew at every waking: the language may have changed; client wave 20: and after a page turn or a Refresh)
        }

        private void Apply(float alpha, bool usable)
        {
            Alpha = alpha; _group.alpha = alpha;
            bool draw = alpha > 0.002f;
            if (_canvas.enabled != draw) _canvas.enabled = draw;
            foreach (var c in _cols) if (c != null && c.enabled != usable) c.enabled = usable;
            if (!usable) foreach (var c in _controls) { foreach (var b in c.Buttons) if (b.Hovered) b.SetHover(false); foreach (var e in c.Extras) if (e.Button.Hovered) e.Button.SetHover(false); }
        }
    }
}
