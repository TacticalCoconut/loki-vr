using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 11 (w11menu), part 2: the new menu's launcher, a ring of large icons over the walking hand in place of the wrist
    /// menu's twenty tabs (UI/WristMenu.Board.cs has the groups). Each icon is a button of 150 px = 6.0 cm (7.6° from 45 cm) with a
    /// picture from the player's own game data (an item's or a skill's icon, drawn large) and the group's name under it. Chosen by:
    ///   the stick   the walking hand's stick pushed towards an icon marks it (as the skill wheels: UI/SkillWheel.cs); letting the
    ///               stick go opens it, and so does the pointer hand's trigger while it is marked; no walking meanwhile
    ///               (DioramaRig.WalkStickTaken);
    ///   the laser   on an icon + trigger;
    ///   the gaze    looking at an icon + trigger (tracked hands: a pinch).
    /// It follows the hand and faces the head while open (it is small and on the hand, like the wrist menu was); the same button
    /// that opened it (X / A, or the strip's Menu) closes it.
    /// TWIST (Settings → Comfort "Menu ring: twist"; specified on 2026-10-06: "a toggleable control type for the wheel that
    /// chooses ... by rotating your wrist ... whichever one is above it is chosen"): the ring opens with the group opened last at
    /// the top, under a fixed marker. Rolling the hand that carries the ring about its own forward (forearm) axis steps the choice,
    /// measured from the hand's pose when the ring opened: one group per <see cref="TwistStep"/> degrees of wrist (Settings →
    /// Comfort "Ring twist", 18° by default, 14-30°), so all eight are within 4 steps either way (the group opposite the top is
    /// reached both ways). The choice changes when the roll is <see cref="TwistHysteresis"/> degrees past the middle between two
    /// steps (it never flickers at a boundary), with a light tick in that hand. Opened by a trigger (below); B closes the ring
    /// without opening anything. Looking does NOT choose on the ring in this mode (WristMenu.GazeAllowed); the laser on an icon +
    /// trigger still opens that icon. The walking stick stays the character's.
    /// THE ARCH (client wave 13, item 9; design note, 2026-10-07: "instead of it being a circle, make it an arch on top of the player's hand
    /// and reduce just how much it twists around when the player rotates the wrist. So that it's not a one-to-one ratio"): in twist
    /// mode the groups sit on an arch over the hand, its centre just over the hand (<see cref="ArchUp"/>), radius
    /// <see cref="ArchRadius"/> px = 28 cm, upright for the player whatever the controller's roll (it faces the head). The chosen
    /// group is at the top (6.0 cm, its name under it), the neighbours down both sides 12° apart (<see cref="ArchStep"/>), smaller
    /// (4.2, 3.45, 2.7 cm) and fainter; seven are drawn, the eighth (four steps away) is not and cannot be pressed. The picture slides
    /// along the arch with the wrist by <see cref="TwistRatio"/> arch degrees per wrist degree (12 / 18 = 0.67 by default: always under
    /// 1), eased; the choice keeps its hysteresis and tick. Point mode keeps the circle.
    /// CLIENT WAVE 14 (w14menu), points 2, 3 and 25 of the design's headset test of wave 13 and the design's answer 3. Point 2: "The wrist rotating
    /// menu loops back around itself That's not meant to happen. The icons should all be visible at once. And I also have to rotate
    /// my wrist way too much to reach the ends of each side." Point 3: "instead of rotating the icons, the icon stays stationary. It's
    /// just that the selector rotates with my wrist." Point 25: it always opens on Settings. Answer 3: "Provide a "rotation"
    /// sensitivity slider on the settings. Also Provide the option to choose between the circle and arch configurations."
    ///   The arch holds ALL EIGHT groups, every one drawn and pressable, and it has two ENDS: it is the ring of eight cut opposite
    ///   Settings (<see cref="ArchSlot"/>: Companions, Social, World, SETTINGS, Character, Items, Skills, Quests from left to right),
    ///   so it opens on Settings (WristMenu.Board.cs ShowRing) with three groups to its left and four to its right; rolling past an
    ///   end does nothing more (no wrap). Wrist per step: Settings → Controls → Menus "Rotation sensitivity" (VR/Comfort.cs
    ///   RingTwistStep, 10° by default: the farthest end 40° of wrist; wave 13: 72°). Hysteresis min(2.5°, a quarter step).
    ///   "Ring icons: Still" (the default, VR/Comfort.cs RingStill): the icons stay where they are, <see cref="ArchStillStep"/>° apart
    ///   (the arch ±52.5°), the chosen one full size (6.0 cm), the others <see cref="ArchStillScale"/> of it; a gold SELECTOR frame
    ///   slides along the arch with the wrist (eased, never past an end icon). "Slide": wave 13's way, the icons slide under a fixed
    ///   marker at the top, now all eight drawn (12° apart near the top, 9° beyond the third: <see cref="SlideAngle"/>).
    ///   "Ring shape: Circle": wave 11's twist ring of eight (it is a ring: the group opposite Settings is 4 steps either way), the
    ///   icons turning under the marker (Slide) or still with the selector going round (Still).
    /// THE BUTTONS (client wave 13, the brief's fourteenth item; design note, 2026-10-07: "pressing the menu button again when I have the
    /// menu open should close the wheel, but not open any menus. It should open the menu that I have highlighted when I press the
    /// trigger instead"), both modes: the menu button pressed with the ring open closes it and opens nothing
    /// (WristMenu.Board.cs ToggleRing). The HIGHLIGHTED group (<see cref="Highlighted"/>: the stick's mark, twist's top, else the
    /// icon the laser or the look is on) is opened by the pointing hand's trigger, as before, and also by the trigger of the hand
    /// that carries the ring: while the ring is open that trigger is the ring's (VR/LokiInput.cs ClaimForMenu: the skill wheels do
    /// not see it), and a pull that opened a group stays the ring's until it is let go, so the skill wheels never open from it
    /// after the ring closed. One hand can open the ring, twist and pull.
    /// THE OPENING (client wave 12; design note, 2026-10-06: "an animation where it kind of like rotates all of the different icons, dropping
    /// them off one by one into position. Similar to the way that the menu appears in Sword Art Online"): the icons come in as one
    /// stack at the top place, a little inside the ring and smaller, and turn clockwise around the ring's centre; as the stack
    /// passes each place it leaves that place's icon there, which settles outwards onto it with a slight overshoot: the icon at the
    /// top first, then the ring's order. <see cref="OpenSweep"/> s for the turn (one icon every 43 ms), <see cref="OpenSettle"/> s
    /// for each to settle: <see cref="OpenSeconds"/> s in all. What moves is a picture only (<see cref="MenuRingFx"/>: a second
    /// panel with no colliders and no buttons): the real buttons stand at their places from the first frame, unseen until their
    /// picture arrives, so the stick, the laser, the look and the twist choose exactly as without the animation, and nothing can be
    /// pressed at a place it is only passing. An icon the laser or the look is on, or the stick marks, is put in place at once.
    /// Closing: the icons fade and shrink in <see cref="MenuRingFx.CloseSeconds"/> s (pictures again: the ring itself is gone at
    /// once). No sound. No Settings switch: it is a 21 cm ring over the hand for less than half a second, not a movement of the world.
    /// CLIENT WAVE 15 (w15menu), points 1 and 13 of the design's notes on wave 14. Point 1 ("the rotation is inverted ... just make it a setting
    /// that you can toggle the direction"): Settings → Controls → Menus "Ring direction" (VR/Comfort.cs RingReversed, read at each opening):
    /// Reversed (the default) turns the roll's sign round, on the arch and on the circle, in both ways of moving (selector / icons).
    /// Point 13 ("the initial option that is highlighted should be the character tab"): the ring opens on CHARACTER (WristMenu.Board.cs
    /// ShowRing). The arch keeps wave 14's order, cut opposite Settings (<see cref="Show"/>'s <c>centre</c>): Companions, Social, World,
    /// Settings, CHARACTER, Items, Skills, Quests; Character is the icon just right of the middle (the still arch's +7.5°, as Settings was
    /// at -7.5°), four steps to the left end, three to the right end. Client wave 19 (w19small), point 50 (design note: "the leftmost option
    /// should be the companions one"): Companions and Social swapped in the ring's order (WristMenu.Board.cs Groups), so Companions is the
    /// arch's left end whichever "Ring direction" (it turns the roll's sign only, never the places).
    /// CLIENT WAVE 18 (w18menu), point 2 ("a toggle that allows them to slightly snap to the closest option ... If my hands are shaky"): with
    /// Settings → Controls → Menus "Ring snap: On" (the default; VR/Comfort.cs RingSnap, strength 50 %, range 30 %, read at the opening) the
    /// twist picture (the still selector, the sliding icons, the circle's turn) is pulled towards the CHOSEN icon's centre: within the range
    /// (a share of a step) of it the picture moves only (1 - strength × closeness) as much as the wrist (wave 18's SnapPull), beyond it
    /// it follows the wrist; when the choice moves on, the picture eases onto the new icon. The choice itself changes a little later
    /// (+ 0.5 × strength × range of a step, at most 0.35 of a step past the middle in all), so a shaky hand at
    /// the border between two icons does not flip it. Off: wave 15's picture exactly.
    /// CLIENT WAVE 19 (w19world), point 49 (design note: "the snap function on the rotating wrist does not work whatsoever. Even at maximum strength,
    /// and range, it does not do anything"): the snap acted on the drawn picture only and added at most 0.1 of a step to WHEN the choice
    /// moves on. Now it holds the CHOICE: with the snap the choice leaves an icon only when the wrist is 0.5 + base + strength × (
    /// <see cref="SnapLeaveMax"/> - base) of a step past it (base = <see cref="TwistHysteresis"/> in steps, 0.25 at 10°: 50 %: 0.85,
    /// 100 %: 0.95, nearly at the next icon), and the picture is a detent (<see cref="SnapPicture"/>): within the range it moves only
    /// (1 - strength) as much as the wrist (100 %: not at all), beyond it it catches up to the wrist by the leave point, where the
    /// choice moves on with a firmer tick. Every twist mode (arch / circle, selector / icons, either direction); point mode (the stick,
    /// the laser, the look) chooses directly: no snap there (Loki's reading, flagged).
    /// </summary>
    public sealed class MenuRing : MonoBehaviour
    {
        public const float Size = 760, Radius = 290, IconPx = 150, Scale = 0.0004f, Deadzone = 0.5f, Up = 0.24f; // (wave 28, wcodex: Radius 268 -> 290: nine 150 px icons 40° apart need 287 px or more so that no two overlap)
        private RectTransform _rt; private Transform _hand; private Camera _head; private VR.DioramaRig _rig;
        private readonly List<UiButton> _buttons = new List<UiButton>();
        private readonly List<Image> _icons = new List<Image>();
        private readonly List<Text> _labels = new List<Text>();
        private Text _centre; private RectTransform _centreBg;
        private int _marked = -1; private bool _stickWas, _armed;
        // twist
        public const float TwistHysteresisMax = 2.5f;
        /// <summary>Degrees of wrist past the middle between two steps before the choice changes (2.5°, at most a quarter step).</summary>
        public static float TwistHysteresis => Mathf.Min(TwistHysteresisMax, 0.25f * TwistStep);
        /// <summary>The circle's farthest step either way (the arch's ends: <see cref="ArchLeft"/>).</summary>
        public const int TwistMaxSteps = 4;
        private bool _twist, _arch, _still; private int _top, _step, _lo, _hi, _archCentre, _topSlot; private float _dir = 1f; private Quaternion _q0; private float _phi; private RectTransform _markFrame; private Text _markArrow;
        // client wave 13, item 9 (w13ring): twist mode's ARCH over the hand (see TWIST above). Its own numbers: the arch's radius
        // (700 px = 28 cm: its centre just over the hand, so the top icon is 30 cm over it), the angle between two neighbours on it
        // (12°), how many steps from the top an icon is fully drawn / gone (2.5 / 3.5: seven of the eight shown, the eighth, four
        // steps away, hidden), and the sizes: 1.0 at the top (150 px = 6.0 cm), 0.70 one step away, 0.575 two, 0.45 three.
        // Client wave 14 (w14menu): every icon on the arch (ArchFull / ArchGone of wave 13 are gone); the slide's spacing beyond the third
        // step (ArchFar: the small icons there), the still arch's spacing and the size of its icons that are not chosen.
        public const float ArchRadius = 700f, ArchStep = 12f, ArchFar = 9f, ArchUp = 0.02f, ArchCarry = 0.90f, ArchStillStep = 15f, ArchStillScale = 0.82f;
        /// <summary>Degrees of wrist roll for one step (Settings → Controls → Menus "Rotation sensitivity": VR/Comfort.cs RingTwistStep,
        /// 10 by default).</summary>
        public static float TwistStep => VR.Comfort.RingTwistStep;
        /// <summary>The sliding arch's slide per degree of wrist roll near its top (12° of arch a step).</summary>
        public static float TwistRatio => ArchStep / TwistStep;
        /// <summary>An icon's size on the sliding arch, <paramref name="steps"/> steps from its top (- = left): 1.0, 0.70, 0.575, then 0.45.</summary>
        public static float ArchScale(float steps) { float a = Mathf.Abs(steps); return 1f - 0.30f * Mathf.Min(1f, a) - 0.25f * Mathf.Clamp01((a - 1f) / 2f); }
        /// <summary>The sliding arch: degrees from its top of a place <paramref name="steps"/> steps away (12° a step up to 3, 9° beyond).</summary>
        public static float SlideAngle(float steps) { float a = Mathf.Abs(steps); return (steps < 0f ? -1f : 1f) * (a <= 3f ? ArchStep * a : ArchStep * 3f + ArchFar * (a - 3f)); }
        /// <summary>The still arch: degrees from its top of slot <paramref name="slot"/> (0 = the left end) of <paramref name="n"/>.</summary>
        public static float StillAngle(float slot, int n) => (slot - (n - 1) * 0.5f) * ArchStillStep;
        /// <summary>A place on the arch <paramref name="deg"/> degrees from its top (- = left).</summary>
        public static Vector2 ArchPosDeg(float deg, float radius = ArchRadius) { float a = deg * Mathf.Deg2Rad; return new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * radius; }
        public static Vector2 ArchPos(float steps, float radius = ArchRadius) => ArchPosDeg(SlideAngle(steps), radius);
        /// <summary>How many groups are left of the opening one on the arch (3 of 8: Settings opens with three to its left, four to its right).</summary>
        public static int ArchLeft(int n) => (n - 1) / 2;
        /// <summary>Group i's slot on the arch, 0 = its left end: the ring's own order, cut opposite the arch's centre group (Settings; wave 14: the opening group).</summary>
        public int ArchSlot(int i) { int n = _buttons.Count; return n == 0 ? 0 : (((i - _archCentre) % n + n) % n + ArchLeft(n)) % n; }
        /// <summary>Client wave 15 (w15menu), point 1: the roll's sign in force (-1: "Ring direction: Reversed"), read at the opening (smoke, log).</summary>
        public float Direction => _dir;
        // ---- client wave 18 (w18menu), point 2: the ring snap (VR/Comfort.cs RingSnap / RingSnapStrength / RingSnapRange, read at the opening)
        private float _snapS, _snapR;
        /// <summary>The snap in force since the opening: its strength (0: off) and range in steps; the extra hysteresis it adds, in steps; the
        /// picture's target in steps before the easing (smoke, log).</summary>
        public float SnapStrength => _snapS;
        public float SnapRange => _snapR;
        public float SnapHysteresis => _snapS > 0f ? _snapS * Mathf.Max(0f, SnapLeaveMax - TwistHysteresis / TwistStep) : 0f;
        /// <summary>Point 49: how far past the middle between two icons the wrist must go to leave the chosen one, in steps (without the
        /// snap: <see cref="TwistHysteresis"/> alone).</summary>
        public float LeaveBorder => 0.5f + TwistHysteresis / TwistStep + SnapHysteresis;
        public float SnapTarget { get; private set; }
        /// <summary>Point 49: at 100 % the choice leaves an icon 0.45 of a step past the middle (0.95 from it: nearly at the next icon).</summary>
        public const float SnapLeaveMax = 0.45f;
        /// <summary>Point 49: the picture's offset from the chosen icon's centre, <paramref name="d"/> steps of wrist away from it, as a detent:
        /// within <paramref name="range"/> steps it moves (1 - strength) as much as the wrist (strength 1: it stays on the icon), beyond it it
        /// catches up so that it meets the wrist at <paramref name="border"/> (where the choice moves on); past that it follows the wrist (an
        /// arch's end). Continuous and never backwards.</summary>
        public static float SnapPicture(float d, float strength, float range, float border)
        {
            float a = Mathf.Abs(d);
            if (strength <= 0f || range <= 1e-4f) return d;
            if (a <= range) return d * (1f - strength);
            if (a >= border || border <= range + 1e-4f) return d;
            float held = range * (1f - strength);
            return Mathf.Sign(d) * (held + (a - range) * (border - held) / (border - range));
        }
        private float _xvis; private float[] _archAt = new float[0], _archAng = new float[0], _archScl = new float[0];
        /// <summary>The twist's picture now: how many steps it has slid (Slide) or the selector has gone (Still) from the opening, eased;
        /// icon i's place in steps (Slide: from the top; Still: from the opening group's slot); its angle from the arch's top and its size.</summary>
        public float ArchSlide => _xvis;
        public float ArchAt(int i) => i >= 0 && i < _archAt.Length ? _archAt[i] : 0f;
        public float ArchAngle(int i) => i >= 0 && i < _archAng.Length ? _archAng[i] : 0f;
        public float ArchSize(int i) => i >= 0 && i < _archScl.Length ? _archScl[i] : 1f;
        /// <summary>The shape and the way in force (smoke, log): the arch (else the circle), the icons still (else sliding); the step limits.</summary>
        public bool Arch => _arch && Shown;
        public bool Still => _still && Shown;
        public int StepMin => _lo;
        public int StepMax => _hi;
        /// <summary>The selector's (still) or the marker's place on the ring's panel now, in px (smoke).</summary>
        public Vector2 MarkerAt => _markFrame != null ? _markFrame.anchoredPosition : Vector2.zero;
        // the opening (client wave 12)
        public const float OpenSweep = 0.30f, OpenSettle = 0.12f, OpenSeconds = OpenSweep + OpenSettle, CarryRadius = 0.70f, CarryScale = 0.62f;
        private MenuRingFx _fx; private readonly List<CanvasGroup> _groups = new List<CanvasGroup>();
        private bool _opening; private float _openT0; private bool[] _arrived = new bool[0];
        /// <summary>The opening is still running; the icons in the order they were put in place at the last opening; how long it took (smoke, log).</summary>
        public bool Opening => _opening && Shown;
        public readonly List<int> Arrivals = new List<int>();
        public float OpenedIn { get; private set; }
        public MenuRingFx Fx => _fx;
        /// <summary>The ring is in twist mode now; the hand's roll from its pose at the opening, in degrees (+ = counter-clockwise seen
        /// along the hand's forward); steps taken by rolling and picks by the menu button (smoke, log).</summary>
        public bool Twist => _twist && Shown;
        public float TwistRoll { get; private set; }
        public int TwistSteps { get; private set; }
        public int MenuButtonPicks { get; private set; }
        /// <summary>Groups opened by the trigger of the hand that carries the ring (client wave 13; smoke, log).</summary>
        public int RingHandPicks { get; private set; }
        private bool _ringTrig;
        /// <summary>The group a trigger would open now: the one the stick marks or twist mode has at the top, else (point mode) the icon
        /// the laser or the look is on; -1: none.</summary>
        public int Highlighted
        {
            get
            {
                if (!Shown) return -1;
                if (_marked >= 0) return _marked;
                for (int i = 0; i < _buttons.Count; i++) if (_buttons[i].Hovered) return i;
                return -1;
            }
        }
        public bool MarkerShown => _markFrame != null && _markFrame.gameObject.activeInHierarchy;
        private Action<int> _onPick;
        private string[] _names = new string[0];
        public bool Shown => _rt != null && _rt.gameObject.activeSelf;
        public int Count => _buttons.Count;
        /// <summary>The icon the stick marks (-1: none) and how each choice so far was made (smoke, log).</summary>
        public int Marked => _marked;
        public int StickPicks { get; private set; }
        public int TriggerPicks { get; private set; }
        public int ButtonPicks { get; private set; }
        public UiButton Button(int i) => i >= 0 && i < _buttons.Count ? _buttons[i] : null;

        public static MenuRing Create(Transform parent, VR.DioramaRig rig, Transform hand, Camera head, IList<(string name, Sprite icon)> groups, Action<int> onPick)
        {
            var rt = UiKit.Panel("MenuRing", parent, new Vector2(Size, Size), Scale, false);
            var r = rt.gameObject.AddComponent<MenuRing>();
            r._rt = rt; r._rig = rig; r._hand = hand; r._head = head; r._onPick = onPick;
            r._names = new string[groups.Count];
            for (int i = 0; i < groups.Count; i++)
            {
                int idx = i; var p = SlotPos(i, groups.Count);
                r._names[i] = groups[i].name;
                var b = UiKit.Button(rt, "", p, new Vector2(IconPx, IconPx), () => r.Pick(idx, 2), 20);
                b.Normal = new Color(0.08f, 0.09f, 0.13f, 0.92f); b.SetHover(false);
                var brt = (RectTransform)b.transform;
                var icon = UiKit.Icon(brt, new Vector2(0, 18), new Vector2(88, 88));
                icon.sprite = groups[i].icon; icon.enabled = icon.sprite != null;
                var label = UiKit.Label(brt, groups[i].name, new Vector2(0, -50), new Vector2(IconPx - 8, 40), 26, TextAnchor.MiddleCenter);
                label.resizeTextForBestFit = true; label.resizeTextMinSize = 17; label.resizeTextMaxSize = 26;
                r._buttons.Add(b); r._icons.Add(icon); r._labels.Add(label);
                r._groups.Add(b.gameObject.AddComponent<CanvasGroup>());
            }
            r._arrived = new bool[groups.Count]; r._archAt = new float[groups.Count]; r._archAng = new float[groups.Count]; r._archScl = new float[groups.Count];
            r._fx = MenuRingFx.Create(parent, groups.Count, r._names);
            for (int i = 0; i < groups.Count; i++) r.SetIcon(i, groups[i].icon);
            // a dark plate behind the name in the middle (the ring has no background: over bright ground the name alone was faint)
            r._centreBg = UiKit.Box(rt, "centrebg", Vector2.zero, new Vector2(350, 120), new Color(0.07f, 0.08f, 0.12f, 0.88f));
            r._centreBg.gameObject.SetActive(false);
            r._centre = UiKit.Label(rt, "", Vector2.zero, new Vector2(330, 110), 30, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.45f));
            r._centre.resizeTextForBestFit = true; r._centre.resizeTextMinSize = 18; r._centre.resizeTextMaxSize = 30;
            // twist mode's fixed marker: a frame round the top place and an arrow over it (under the icons: made first in the order)
            r._markFrame = UiKit.Box(rt, "marker", new Vector2(0, Radius), new Vector2(IconPx + 16, IconPx + 16), new Color(1f, 0.85f, 0.4f, 1f));
            r._markFrame.SetAsFirstSibling();
            r._markArrow = UiKit.Label(rt, "▼", new Vector2(0, Radius + IconPx / 2 + 20), new Vector2(60, 34), 30, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.4f));
            r._markFrame.gameObject.SetActive(false); r._markArrow.gameObject.SetActive(false);
            rt.gameObject.SetActive(false);
            return r;
        }

        /// <summary>Icon i's place: the first at the top, the others clockwise.</summary>
        public static Vector2 SlotPos(int i, int n) { float a = i * (360f / n) * Mathf.Deg2Rad; return new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * Radius; }
        /// <summary>The icon a stick points at (-1 inside the dead zone).</summary>
        public static int SlotAt(Vector2 stick, int n)
        {
            if (stick.magnitude < Deadzone || n <= 0) return -1;
            float a = Mathf.Atan2(stick.x, stick.y) * Mathf.Rad2Deg; if (a < 0) a += 360f;
            return Mathf.RoundToInt(a / (360f / n)) % n;
        }

        public void SetHand(Transform hand) { _hand = hand; }
        /// <summary>The picture of icon i (the game data's own; null: the name alone, larger).</summary>
        public void SetIcon(int i, Sprite s)
        {
            if (i < 0 || i >= _icons.Count) return;
            _icons[i].sprite = s; _icons[i].enabled = s != null;
            var lrt = (RectTransform)_labels[i].transform;
            lrt.anchoredPosition = s != null ? new Vector2(0, -50) : Vector2.zero; lrt.sizeDelta = new Vector2(IconPx - 8, s != null ? 40 : 90);
            if (_fx != null) _fx.SetIcon(i, s);
        }

        /// <summary>Opens the ring. twist: worked by rolling the hand (then <paramref name="top"/>, Settings since client wave 14, is chosen:
        /// on the arch in its middle, on the circle at the top); <paramref name="arch"/>: twist's arch (else the circle);
        /// <paramref name="still"/>: the icons stay and a selector moves (else they slide under a fixed marker). Not twist: worked by
        /// pointing (stick, laser, look) on the circle, as before.</summary>
        /// Client wave 15 (w15menu), point 13: <paramref name="centre"/> is the group the arch is cut opposite of (-1: <paramref name="top"/>,
        /// wave 14's way), so the arch keeps its order whichever group it opens on.
        public void Show(bool twist = false, int top = 0, bool arch = true, bool still = false, int centre = -1)
        {
            // a stick already pushed when the ring opens (the player was walking) marks nothing until it came back to the middle once
            _marked = -1; _stickWas = false; _armed = false;
            // client wave 13: a trigger of the ring's hand already held when the ring opens is not a pull of the ring (it must be let go first)
            _ringTrig = _rig != null && _rig.Off.TriggerValue > 0.35f; if (_ringTrig) _rig.Off.ClaimForMenu();
            int n = _buttons.Count;
            _twist = twist && _hand != null; _arch = _twist && arch; _still = _twist && still;
            _top = Mathf.Clamp(top, 0, Mathf.Max(0, n - 1)); _step = 0; TwistRoll = 0f;
            _archCentre = centre >= 0 && centre < n ? centre : _top; _topSlot = ArchSlot(_top); // (client wave 15, point 13)
            _dir = VR.Comfort.RingReversed ? -1f : 1f; // (client wave 15, point 1)
            _snapS = _twist && VR.Comfort.RingSnap ? VR.Comfort.RingSnapStrength : 0f; _snapR = VR.Comfort.RingSnapRange; SnapTarget = 0f; // (client wave 18, w18menu, point 2)
            // client wave 14 (w14menu), point 2: the arch has two ends (no wrap); the circle reaches the group opposite both ways, as before
            if (_arch) { _lo = -_topSlot; _hi = n - 1 - _topSlot; } else { _lo = -TwistMaxSteps; _hi = TwistMaxSteps; }
            if (_twist) { _q0 = _hand.rotation; _marked = _top; }
            _phi = _twist && !_arch ? _top * (360f / Mathf.Max(1, n)) : 0f;
            _markFrame.gameObject.SetActive(_twist); _markArrow.gameObject.SetActive(_twist);
            _centre.resizeTextMaxSize = _twist ? 46 : 30; _centre.fontSize = _twist ? 46 : 30;
            // client wave 13, item 9: the arch (the marker at its top, the name under the top place); the circle: the name in the middle
            PutMarker(0f, _arch ? ArchRadius : Radius);
            // (client wave 14: under the still arch the two middle icons sit 7.5° off the top, their bottoms 6 px lower: the name 24 px lower too)
            var cy = _arch ? new Vector2(0, ArchRadius - IconPx / 2 - 62 - (_still ? 24 : 0)) : Vector2.zero; _centre.rectTransform.anchoredPosition = cy; _centreBg.anchoredPosition = cy;
            _xvis = 0f;
            for (int i = 0; i < n; i++) if (!_buttons[i].gameObject.activeSelf) _buttons[i].gameObject.SetActive(true); // (client wave 14: every icon shown, both shapes)
            if (_arch) ArchLayout(0f);
            else { for (int i = 0; i < n; i++) { _buttons[i].transform.localScale = Vector3.one; _archScl[i] = 1f; } Turn(_phi); }
            if (!_rt.gameObject.activeSelf) _rt.gameObject.SetActive(true);
            if (_rig != null) _rig.WalkStickTaken = !_twist; // twist: the stick stays the character's
            Place(); Paint();
            // the opening: every button is at its place and works from now on; its picture is on its way (MenuRingFx)
            _opening = true; _openT0 = Time.unscaledTime; Arrivals.Clear(); OpenedIn = 0f;
            for (int i = 0; i < n; i++) { _arrived[i] = false; _groups[i].alpha = 0f; }
            _fx.BeginOpen(transform);
            Animate();
            Debug.Log($"[Menu] ring opened: {(!_twist ? "point (circle)" : $"twist, {(_arch ? "arch" : "circle")}, icons {(_still ? "still, the selector moves" : "slide")}, {TwistStep:0.#} deg of wrist a step, direction {(_dir < 0f ? "reversed" : "normal")}, steps {_lo}..{_hi}, snap {(_snapS > 0f ? $"{_snapS * 100f:0} % within {_snapR * 100f:0} % of a step (+{SnapHysteresis:0.###} step hysteresis)" : "off")}")}, chosen '{(_marked >= 0 ? _names[_marked] : "-")}'");
        }

        /// <summary>Twist: the marker (Slide: fixed at the top) or the selector (Still) at <paramref name="deg"/> degrees from the top of a
        /// ring or arch of radius <paramref name="r"/> px, its arrow outside it pointing in.</summary>
        private void PutMarker(float deg, float r)
        {
            var dir = ArchPosDeg(deg, 1f);
            _markFrame.anchoredPosition = dir * r;
            _markArrow.rectTransform.anchoredPosition = dir * (r + IconPx / 2 + 20);
            _markArrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -deg);
        }

        private static float EaseOutBack(float x) { const float c1 = 1.70158f, c3 = c1 + 1f; x = Mathf.Clamp01(x) - 1f; return 1f + c3 * x * x * x + c1 * x * x; }

        private void Arrive(int i)
        {
            if (_arrived[i]) return;
            _arrived[i] = true; _groups[i].alpha = 1f; _fx.Drop(i); Arrivals.Add(i);
        }

        /// <summary>One frame of the opening: where each picture is, which ones have been left at their places.</summary>
        private void Animate()
        {
            if (!_opening) return;
            float t = Time.unscaledTime - _openT0;
            int n = _buttons.Count, first = _twist ? _top : 0; float pitch = 360f / n;
            float sweep = Mathf.Clamp01(t / OpenSweep) * (n - 1) * pitch; // how far the stack has turned from the top place
            _fx.Follow(transform);
            bool all = true;
            if (_arch)
            {
                // client wave 13, item 9: the arch's opening: the stack starts at the top and fans out down both sides along the arch;
                // as it passes an icon's place it leaves that icon, which settles outwards onto it (the top first, then outwards).
                // Client wave 14: in degrees along the arch (every icon is on it), the fan reaching the farthest place at OpenSweep
                float far = 1f; for (int i = 0; i < n; i++) far = Mathf.Max(far, Mathf.Abs(_archAng[i]));
                float fan = Mathf.Clamp01(t / OpenSweep) * far; // degrees from the top the two streams have reached
                for (int k = 0; k < n; k++)
                {
                    int i = (first + k) % n;
                    if (_arrived[i]) continue;
                    float a = _archAng[i], d = Mathf.Abs(a);
                    float settle = (t - OpenSweep * d / far) / OpenSettle;
                    if (settle >= 1f || _buttons[i].Hovered) { Arrive(i); continue; }
                    all = false;
                    float at = settle <= 0f ? (a < 0f ? -1f : 1f) * Mathf.Min(d, fan) : a; // carried with its stream, then at its place
                    float e = settle <= 0f ? 0f : EaseOutBack(settle);
                    _fx.Put(i, ArchPosDeg(at, Mathf.LerpUnclamped(ArchCarry, 1f, e) * ArchRadius), Mathf.LerpUnclamped(CarryScale, 1f, e) * _archScl[i], Mathf.Clamp01(0.6f + 0.4f * e));
                }
                if (all) { _opening = false; OpenedIn = t; _fx.EndOpen(); }
                return;
            }
            for (int k = 0; k < n; k++)
            {
                int i = (first + k) % n;
                if (_arrived[i]) continue;
                float settle = (t - OpenSweep * k / (n - 1)) / OpenSettle;
                if (settle >= 1f || _buttons[i].Hovered || (i == _marked && !_twist)) { Arrive(i); continue; }
                all = false;
                var place = ((RectTransform)_buttons[i].transform).anchoredPosition; // (twist: its place turns with the wrist)
                float a = (Mathf.Atan2(place.x, place.y) * Mathf.Rad2Deg - Mathf.Max(0f, k * pitch - sweep)) * Mathf.Deg2Rad;
                float e = settle <= 0f ? 0f : EaseOutBack(settle); // 0 while it is carried; to 1, a little past it and back, while it settles
                _fx.Put(i, new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * (Mathf.LerpUnclamped(CarryRadius, 1f, e) * Radius), Mathf.LerpUnclamped(CarryScale, 1f, e), Mathf.Clamp01(0.6f + 0.4f * e));
            }
            if (all) { _opening = false; OpenedIn = t; _fx.EndOpen(); }
        }

        /// <summary>The icons' places with the ring turned phi degrees counter-clockwise (as the player sees it); the icons stay upright.</summary>
        private void Turn(float phi)
        {
            int n = _buttons.Count;
            for (int i = 0; i < n; i++)
            {
                float a = (i * (360f / n) - phi) * Mathf.Deg2Rad;
                ((RectTransform)_buttons[i].transform).anchoredPosition = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * Radius;
            }
        }

        /// <summary>Twist mode's arch at <paramref name="x"/> steps from the opening (eased). Slide (client wave 13, item 9; client wave 14:
        /// every icon on it, no wrap): icon i sits (its slot - the opening slot - x) steps from the top, at that place's angle and size,
        /// under the fixed marker. Still (client wave 14, point 3): icon i stays at its slot's place, the chosen one full size, and the
        /// selector is at x steps from the opening slot (never past an end icon).</summary>
        private void ArchLayout(float x)
        {
            int n = _buttons.Count;
            for (int i = 0; i < n; i++)
            {
                int slot = ArchSlot(i);
                float ang, sc;
                if (_still) { _archAt[i] = slot - _topSlot; ang = StillAngle(slot, n); sc = i == _marked ? 1f : ArchStillScale; }
                else { float st = slot - _topSlot - x; _archAt[i] = st; ang = SlideAngle(st); sc = ArchScale(st); }
                _archAng[i] = ang; _archScl[i] = sc;
                var b = _buttons[i];
                ((RectTransform)b.transform).anchoredPosition = ArchPosDeg(ang);
                b.transform.localScale = Vector3.one * sc;
                if (_arrived[i] || !_opening) _groups[i].alpha = 1f;
            }
            if (_still) PutMarker(StillAngle(_topSlot + Mathf.Clamp(x, _lo, _hi), n), ArchRadius);
        }

        /// <summary>The roll of <paramref name="to"/> against <paramref name="from"/> about from's own forward axis, in degrees.</summary>
        public static float RollBetween(Quaternion from, Quaternion to)
        {
            var d = Quaternion.Inverse(from) * to;
            return Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(d.z, d.w) * Mathf.Rad2Deg);
        }

        /// <summary>Twist mode, every frame: the hand's roll since the opening steps the choice (within the step limits: the arch's ends,
        /// the circle's ±4) and moves the picture: the arch's icons or selector, the circle's turn or its selector.</summary>
        private void UpdateTwist()
        {
            int n = _buttons.Count;
            TwistRoll = _hand != null ? RollBetween(_q0, _hand.rotation) : 0f;
            float x = _dir * TwistRoll / TwistStep; // in steps (client wave 15, point 1: the direction's sign)
            float border = LeaveBorder; // (client wave 18, point 2: + the snap's hysteresis; client wave 19, point 49: the snap's hold)
            if (Mathf.Abs(x - _step) > border)
            {
                int to = Mathf.Clamp(Mathf.RoundToInt(x), _lo, _hi);
                // a tick in the hand that carries the ring (point 49: firmer with the snap: 0.12 + 0.13 × strength)
                if (to != _step) { _step = to; TwistSteps++; VR.Haptics.Pulse(VR.Haptics.Kind.Menu, false, 0.12f + 0.13f * _snapS, 0.015f); }
            }
            _marked = ((_top + _step) % n + n) % n;
            // client wave 18 (w18menu), point 2 / client wave 19, point 49: with the snap, the picture is a detent on the CHOSEN icon (SnapPicture)
            float xs = _snapS > 0f ? _step + SnapPicture(x - _step, _snapS, _snapR, LeaveBorder) : x;
            // the picture follows the wrist, eased (a trembling hand does not shake it): sliding icons at most half a step past an end, a
            // still ring's selector never past an end icon
            float target = _still ? Mathf.Clamp(xs, _lo, _hi) : Mathf.Clamp(xs, _lo - 0.5f, _hi + 0.5f);
            SnapTarget = target;
            _xvis = Mathf.Lerp(_xvis, target, 1f - Mathf.Exp(-22f * Time.unscaledDeltaTime));
            if (_arch) ArchLayout(_xvis);
            else if (_still) PutMarker(_xvis * (360f / n), Radius); // the circle stays; the selector goes round it
            else { _phi = (_top + _xvis) * (360f / n); Turn(_phi); } // the circle turns under the marker (wave 11's way)
        }


        public void Hide()
        {
            _marked = -1;
            if (_rt.gameObject.activeSelf)
            {
                // the closing: the pictures of the icons that are in place fade where they were (the buttons are gone at once)
                var at = new Vector2[_buttons.Count]; var sc = new float[_buttons.Count]; var shown = new bool[_buttons.Count];
                for (int i = 0; i < at.Length; i++) { at[i] = ((RectTransform)_buttons[i].transform).anchoredPosition; sc[i] = _buttons[i].transform.localScale.x; shown[i] = _arrived[i] && _buttons[i].gameObject.activeSelf; }
                _fx.BeginClose(transform, at, shown, sc);
            }
            _opening = false; for (int i = 0; i < _groups.Count; i++) _groups[i].alpha = 1f;
            if (_rt.gameObject.activeSelf) _rt.gameObject.SetActive(false);
            if (_rig != null) _rig.WalkStickTaken = false;
        }

        private void OnDisable() { if (_rig != null) _rig.WalkStickTaken = false; }

        /// <summary>how: 0 the stick let go, 1 the trigger on the marked icon, 2 the icon's own button (laser / gaze), 4 the trigger of the
        /// ring's own hand (client wave 13).</summary>
        private void Pick(int i, int how)
        {
            if (i < 0 || i >= _buttons.Count) return;
            if (how == 0) StickPicks++; else if (how == 1) TriggerPicks++; else if (how == 2) ButtonPicks++;
            Hide();
            _onPick?.Invoke(i);
        }

        private void Place()
        {
            if (_hand == null || _head == null) return;
            var toHead = _head.transform.position - _hand.position; toHead.y = 0; if (toHead.sqrMagnitude > 1e-4f) toHead.Normalize();
            transform.position = _hand.position + Vector3.up * (_arch ? ArchUp : Up) + toHead * 0.04f; // (client wave 13, item 9: the arch's centre just over the hand; client wave 14: the twist circle sits as the point circle)
            UiKit.FaceHead(transform, _head);
        }

        private void Paint()
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                _buttons[i].Normal = i == _marked ? new Color(0.45f, 0.35f, 0.15f, 0.96f) : new Color(0.08f, 0.09f, 0.13f, 0.92f);
                if (!_buttons[i].Hovered) _buttons[i].SetHover(false);
            }
            int shown = _marked;
            if (shown < 0) for (int i = 0; i < _buttons.Count; i++) if (_buttons[i].Hovered) shown = i;
            _centre.text = shown >= 0 ? _names[shown] : "";
            if (_centreBg.gameObject.activeSelf != (shown >= 0)) _centreBg.gameObject.SetActive(shown >= 0);
        }

        private void LateUpdate()
        {
            if (!_rt.gameObject.activeSelf) return;
            Place();
            if (_rig == null) { Animate(); Paint(); return; }
            // client wave 13 (w13resize): the trigger of the hand that carries the ring: the ring's while it is open; a pull opens the highlighted group
            var off = _rig.Off; float otv = off.TriggerValue; bool oTrig = _ringTrig ? otv > 0.35f : otv > 0.6f;
            if (otv > 0.35f) off.ClaimForMenu();
            bool oPull = oTrig && !_ringTrig; _ringTrig = oTrig;
            if (oPull) { int h = Highlighted; if (h >= 0) { RingHandPicks++; Pick(h, 4); return; } }
            if (_twist)
            {
                UpdateTwist();
                Animate();
                // the pointing hand's trigger with no button under the laser: the icon at the top
                if (_rig.ClickedThisFrame && _rig.PressedThisFrame == null) { Pick(_marked, 1); return; }
                Paint(); return;
            }
            var stick = _rig.OffStick;
            if (!_armed) { if (stick.magnitude < 0.25f) _armed = true; Animate(); Paint(); return; }
            int at = SlotAt(stick, _buttons.Count);
            if (at >= 0) { _marked = at; _stickWas = true; }
            else if (_stickWas && stick.magnitude < 0.25f) { _stickWas = false; int pick = _marked; if (pick >= 0) { Pick(pick, 0); return; } }
            Animate();
            // the pointer hand's trigger with no button under the laser / gaze: the marked icon
            if (_marked >= 0 && _rig.ClickedThisFrame && _rig.PressedThisFrame == null) { Pick(_marked, 1); return; }
            Paint();
        }
    }

    /// <summary>
    /// Client wave 12: the pictures of the menu ring's icons that move while the ring opens and fade when it closes (see MenuRing).
    /// A panel of its own with the ring's size and place, with NO collider and NO button on it: the laser, the look and the
    /// trigger never meet it. The opening is driven by the ring (its LateUpdate); the closing runs here by itself.
    /// </summary>
    public sealed class MenuRingFx : MonoBehaviour
    {
        public const float CloseSeconds = 0.12f;
        private RectTransform _rt;
        private readonly List<RectTransform> _items = new List<RectTransform>();
        private readonly List<CanvasGroup> _groups = new List<CanvasGroup>();
        private readonly List<Image> _icons = new List<Image>();
        private readonly List<Text> _labels = new List<Text>();
        private bool _closing; private float _closeT0; private float[] _closeScale = new float[0];
        public bool Shown => _rt != null && _rt.gameObject.activeSelf;
        public bool Closing => _closing && Shown;
        /// <summary>Picture i is being shown (on its way, or fading), and where it is on the ring's panel (smoke).</summary>
        public bool ItemShown(int i) => Shown && i >= 0 && i < _items.Count && _items[i].gameObject.activeSelf;
        public Vector2 ItemAt(int i) => _items[i].anchoredPosition;
        public float ItemScale(int i) => _items[i].localScale.x;

        public static MenuRingFx Create(Transform parent, int n, string[] names)
        {
            var rt = UiKit.Panel("MenuRingFx", parent, new Vector2(MenuRing.Size, MenuRing.Size), MenuRing.Scale, false);
            var f = rt.gameObject.AddComponent<MenuRingFx>(); f._rt = rt;
            for (int i = 0; i < n; i++)
            {
                var box = UiKit.Box(rt, "icon" + i, Vector2.zero, new Vector2(MenuRing.IconPx, MenuRing.IconPx), new Color(0.08f, 0.09f, 0.13f, 0.92f));
                f._groups.Add(box.gameObject.AddComponent<CanvasGroup>());
                f._icons.Add(UiKit.Icon(box, new Vector2(0, 18), new Vector2(88, 88)));
                var label = UiKit.Label(box, names[i], new Vector2(0, -50), new Vector2(MenuRing.IconPx - 8, 40), 26, TextAnchor.MiddleCenter);
                label.resizeTextForBestFit = true; label.resizeTextMinSize = 17; label.resizeTextMaxSize = 26;
                f._labels.Add(label); f._items.Add(box);
            }
            rt.gameObject.SetActive(false);
            return f;
        }

        public void SetIcon(int i, Sprite s)
        {
            if (i < 0 || i >= _icons.Count) return;
            _icons[i].sprite = s; _icons[i].enabled = s != null;
            var lrt = (RectTransform)_labels[i].transform;
            lrt.anchoredPosition = s != null ? new Vector2(0, -50) : Vector2.zero; lrt.sizeDelta = new Vector2(MenuRing.IconPx - 8, s != null ? 40 : 90);
        }

        public void Follow(Transform ring) { transform.SetPositionAndRotation(ring.position, ring.rotation); }

        public void BeginOpen(Transform ring)
        {
            _closing = false; Follow(ring);
            for (int i = 0; i < _items.Count; i++) { _items[i].gameObject.SetActive(true); _groups[i].alpha = 0f; }
            if (!_rt.gameObject.activeSelf) _rt.gameObject.SetActive(true);
        }
        public void Put(int i, Vector2 at, float scale, float alpha) { _items[i].anchoredPosition = at; _items[i].localScale = Vector3.one * scale; _groups[i].alpha = alpha; }
        /// <summary>Picture i reached its place: the real button shows from now on.</summary>
        public void Drop(int i) { if (_items[i].gameObject.activeSelf) _items[i].gameObject.SetActive(false); }
        public void EndOpen() { if (!_closing && _rt.gameObject.activeSelf) _rt.gameObject.SetActive(false); }

        /// <summary>The ring closes: the icons that were in place fade and shrink where they stood.</summary>
        public void BeginClose(Transform ring, Vector2[] at, bool[] shown, float[] scale = null)
        {
            Follow(ring); _closing = true; _closeT0 = Time.unscaledTime;
            _closeScale = new float[_items.Count];
            for (int i = 0; i < _items.Count; i++)
            {
                bool on = i < at.Length && shown[i];
                _items[i].gameObject.SetActive(on);
                _closeScale[i] = scale != null && i < scale.Length ? scale[i] : 1f; // (client wave 13, item 9: the arch's icons keep their sizes)
                if (on) Put(i, at[i], _closeScale[i], 1f);
            }
            if (!_rt.gameObject.activeSelf) _rt.gameObject.SetActive(true);
        }

        private void LateUpdate()
        {
            if (!_closing) return;
            float x = (Time.unscaledTime - _closeT0) / CloseSeconds;
            if (x >= 1f) { _closing = false; _rt.gameObject.SetActive(false); return; }
            for (int i = 0; i < _items.Count; i++)
                if (_items[i].gameObject.activeSelf) { _items[i].localScale = Vector3.one * Mathf.Lerp(1f, 0.8f, x) * (i < _closeScale.Length ? _closeScale[i] : 1f); _groups[i].alpha = 1f - x; }
        }
    }
}
