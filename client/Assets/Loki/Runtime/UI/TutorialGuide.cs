using System;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 18 (w18tut), point 34 of the design's notes after wave 17 (design notes): the GUIDE of the interactive tutorial
    /// (LokiGame.W18Tutorial.cs runs the steps). A panel over the player's view: which step ("Tutorial · 3 / 20") and a bar of the steps
    /// done, the step's title, then (client wave 20, w20tut, point 40: design note, "You can be very verbose on the tutorial. You do not need to be
    /// short whatsoever, be detailed in the text that you write. require the user to be very informed about how things work") the step's
    /// EXPLANATION in full (what the control is, what it does, why and when you use it: <see cref="AboutText"/>), what to do NOW in larger
    /// gold-white letters (<see cref="BodyText"/>), a line that says how on this controller (or, in green, that the step is done), and two
    /// buttons: "Skip step" (or "Start" on the first step) and "End tutorial" ("Enter the world" on the last).
    ///   Kind     a HUD panel (UI/UiKit.cs PanelKind.Hud, given here, not by name): it never switches the new casting off, so the casting
    ///            steps work with it open; it is not in UI/PanelGrab.cs' list, so a grip never takes it. Its buttons are pressed as any
    ///            panel's: the laser or the look, then the trigger.
    ///   Where    client wave 19 (w19world), point 44 (design note: "the tutorial is positioned slightly above me"): AT EYE LEVEL, <see cref="Distance"/>
    ///            m ahead of the eyes (level), its middle <see cref="Up"/> m higher (0: level with the eyes), facing them, never near the
    ///            overhead menu's look-up zone (60° up, woken within 36°: UI/OverheadMenu.cs; the tallest page's top stays under ~17° up).
    ///            It stays in the room (wave 13's rule: a panel never follows the head) and glides back over the view only when the eyes
    ///            have been turned more than <see cref="FollowAngle"/> degrees away, left or right, for <see cref="FollowAfter"/> s (looking
    ///            up or down never moves it), and not while <see cref="Hold"/> says so. Client wave 20 (w20tut), point 65: <see cref="Drop"/>
    ///            degrees lower while the look calibration's dots stand where it would be (its middle that far below the eyes' level).
    ///   Size     client wave 20 (w20tut), point 40: <see cref="WidthPx"/> px wide (0.58 m: about 35° at 0.9 m) and AS TALL AS ITS TEXT
    ///            (<see cref="Layout"/>: the texts measured at their sizes, from <see cref="MinHeightPx"/> to <see cref="MaxHeightPx"/>
    ///            px; past that the explanation shrinks to fit, down to <see cref="AboutMinPx"/> px). At <see cref="MetresPerPx"/>: the
    ///            explanation's <see cref="AboutPx"/> px are 1.15° at 0.9 m; what to do now <see cref="NowPx"/> px, 1.3° (wave 11's reading
    ///            target for page text, w11menu D).
    ///   Clip     points 53 (a looping clip of each step's action over its text) and 59 (a ghost of the player's controller), client wave
    ///            22 (w22clips): they stand in <see cref="Media"/>, a band between the bar and the title that is <see cref="MediaPx"/> px
    ///            tall (0 and hidden: no clip); with it the panel grows downward (see <see cref="MediaPx"/>).
    /// </summary>
    public sealed class TutorialGuide : MonoBehaviour
    {
        // Client wave 24 (w24), item 14 (design note, 2026-10-12: "do revise where the tutorial panel is whenever I am doing the tutorial,
        // because I have frequently lost its position when I am looking around and doing things"). WHY IT GOT LOST: it came back
        // only when the eyes had been turned FollowAngle 55 + |Side| degrees from it (93 to 107 degrees for a guide at a side
        // place) for 1.2 s, so it stood far outside the view for long; nothing showed where it was; and a new instruction could
        // appear on a guide the player was not looking at. NOW:
        //   Home    as before: where the head faces (its heading, level: never its pitch), Side degrees to a side (0 in most
        //           steps; wave 23's side places where a step needs the middle free: LokiGame.W23Place.cs W23GuideSide).
        //   Follow  it does not move while it is within FollowAngle (35) degrees of the view's middle (no swimming while it is
        //           looked at), nor while it stands within SideSlack (15) degrees of its home for the heading now (a guide at
        //           a side place is always that far from the view's middle). Outside both for FollowAfter (1) s: it glides home
        //           in GlideSeconds (0.5 s). Never while Hold says so (a panel held), nor while the laser or the look is on one
        //           of its buttons.
        //   Line    a NEW ACTION LINE (a part done, a step begun) brings it home at once when it is outside both (ComeHome).
        //   Pointer while it is counted as away and while it glides, Away is true: the game shows a gold glow at the view's
        //           edge on its side (LokiGame.W24Tut.cs; VR/EdgeGlow.cs).
        // FLAGGED: the three numbers and SideSlack; the side places are wave 23's (one side for every step is not possible: the
        // menu ring and the forearm bar are at the walking hand, the casting tools in front).
        public const float MetresPerPx = 0.0006f, Distance = 0.9f, Up = 0f, FollowAngle = 12f, FollowAfter = 0.08f, GlideSeconds = 0.25f, SideSlack = 5f;
        /// <summary>It is outside the comfortable cone and away from its home now (or gliding there): the pointer shows.</summary>
        public bool Away { get; private set; }
        /// <summary>Its angle from the view's middle and from its home for the heading now, degrees, level (smoke, log); times a new
        /// action line brought it home.</summary>
        public float FromView { get; private set; }
        public float FromHome { get; private set; }
        public int HomeCalls { get; private set; }
        private bool ButtonHot => (_left != null && _left.Hovered) || (_right != null && _right.Hovered) || (_mid != null && _mid.Hovered);
        /// <summary>A new action line: when the guide is outside the cone and away from its home, it comes home at once.</summary>
        public void ComeHome()
        {
            if (!Visible || _glideT >= 0f || _anchored) return; // (client wave 25: beside the menu above it stays there)
            var cam = _head != null ? _head() : null; if (cam == null) return;
            Measure(cam.transform);
            if (FromView <= FollowAngle || FromHome <= SideSlack || (Hold != null && Hold()) || ButtonHot) return;
            _from = _rt.position; _to = InFront(); _glideT = 0f; _away = 0f; Glides++; HomeCalls++;
        }
        private void Measure(Transform t)
        {
            var f = t.forward; f.y = 0f; var d = _rt.position - t.position; d.y = 0f; var h = InFront() - t.position; h.y = 0f;
            bool ok = f.sqrMagnitude > 1e-4f && d.sqrMagnitude > 1e-4f;
            FromView = ok ? Vector3.Angle(f, d) : 0f; FromHome = ok && h.sqrMagnitude > 1e-4f ? Vector3.Angle(h, d) : 0f;
        }
        public const float WidthPx = 960f, MinHeightPx = 340f, MaxHeightPx = 940f, Pad = 24f;
        public const int StepPx = 24, TitlePx = 40, AboutPx = 30, AboutMinPx = 24, NowPx = 34, StatusPx = 28;
        /// <summary>Client wave 23 (w23place), the design's answers 47 ("the practice panel ... gets occluded by the tutorial panel") and 50 ("the
        /// tutorial page is also occluding the different casting options, such as a pad"): the guide's canvas order, UNDER every panel's
        /// (UI/UiKit.cs Panel: 10; it was 18, over them): the guide is the page the player reads, never the thing they work on, and it
        /// stands behind what they work on (0.9 m; a window opens 0.62 m away, a tool at the hand). Over the name plates (5). The ghost
        /// controller (UI/TutorialClip.cs ClipGhostStand.Order) is one above it, still under the panels.</summary>
        public const int SortingOrder = 8;
        /// <summary>Client wave 23 (w23place): the whole guide's opacity (1: as always). LokiGame.W23Place.cs fades it while a casting tool's
        /// box meets the guide's from the eyes (the design's answer 50), and back after.</summary>
        public float Alpha
        {
            get => _alpha;
            set
            {
                value = Mathf.Clamp01(value); if (Mathf.Abs(value - _alpha) < 1e-4f) return; _alpha = value;
                if (_group == null) { _group = _rt.gameObject.AddComponent<CanvasGroup>(); _group.interactable = false; _group.blocksRaycasts = false; } // (as UI/CastPad.cs' own: no EventSystem reads these)
                _group.alpha = value;
            }
        }
        private CanvasGroup _group; private float _alpha = 1f;
        /// <summary>Client wave 23 (w23place): degrees to the RIGHT of straight ahead the guide stands (negative: left; 0: in front, as
        /// always), at the same distance and height, facing the eyes: beside a window or the hands' working space instead of behind them
        /// (LokiGame.W23Place.cs W23GuideSide says which steps). It then glides back over the view only when the eyes have turned
        /// <see cref="FollowAngle"/> + |Side| degrees away from it.</summary>
        public float Side { get; private set; }
        /// <summary>Sets <see cref="Side"/>; a guide that is shown glides there (<see cref="GlideSeconds"/>), a hidden one is placed there
        /// when it is shown.</summary>
        public void SetSide(float degreesRight)
        {
            degreesRight = Mathf.Clamp(degreesRight, -80f, 80f); if (Mathf.Abs(degreesRight - Side) < 0.01f) return;
            Side = degreesRight;
            if (Visible && _head != null && _head() != null) { _from = _rt.position; _to = InFront(); _glideT = 0f; _away = 0f; }
        }
        /// <summary>The panel's size now (px): <see cref="WidthPx"/> by its text's height.</summary>
        public Vector2 SizePx => _rt != null ? _rt.sizeDelta : new Vector2(WidthPx, MinHeightPx);
        private static readonly Color Gold = new Color(1f, 0.82f, 0.38f), Green = new Color(0.55f, 0.95f, 0.55f), BarBg = new Color(0.2f, 0.22f, 0.3f, 1f);
        private static readonly Color NowColour = new Color(1f, 0.93f, 0.72f), AboutColour = new Color(0.88f, 0.89f, 0.92f);
        private RectTransform _rt, _fill, _bar, _border, _bg, _media;
        private Text _step, _title, _about, _now, _status;
        private UiButton _left, _right, _mid;
        /// <summary>Client wave 22 (w22clips, the casting ways): a third button between the two (null or "": none), set before Show: on the
        /// welcome page of a tutorial shown again it goes straight to the casting ways.</summary>
        public string MiddleLabel { get; set; }
        public Action MiddlePressed;
        public UiButton MiddleButton => _mid;
        private BoxCollider _col;
        private Func<Camera> _head;
        private float _away, _glideT = -1f, _progress; private Vector3 _from, _to, _lastHeadPos; private bool _hasLastHead;
        private string _laidOut;
        /// <summary>The runner's answers to the two buttons (the left one: "Skip step" or "Start"; the right one: "End tutorial" or "Enter
        /// the world").</summary>
        public Action LeftPressed, RightPressed;
        /// <summary>While true the guide stays where it is (LokiGame.W18Tutorial.cs: while a panel is held, the hands are busy).</summary>
        public Func<bool> Hold;
        public bool Visible => _rt != null && _rt.gameObject.activeSelf;
        public string TitleText => _title.text;
        /// <summary>What to do now (the step's part), and (client wave 20) the step's explanation.</summary>
        public string BodyText => _now.text;
        public string AboutText => _about.text;
        public string StatusText => _status.text;
        public string StepText => _step.text;
        public UiButton LeftButton => _left;
        public UiButton RightButton => _right;
        public Transform Panel => _rt;
        /// <summary>The explanation's font size as drawn (smaller than <see cref="AboutPx"/> only when a page is taller than <see cref="MaxHeightPx"/>).</summary>
        public int AboutDrawnPx { get; private set; } = AboutPx;
        /// <summary>Client wave 20 (w20tut), point 65: degrees below the eyes' level the guide's middle stands (0: at eye level).</summary>
        public float Drop { get; private set; }
        /// <summary>Points 53 / 59 (client wave 22, w22clips: LokiGame.W22Clips.cs puts the step's clip and the ghost controller in it): the
        /// band for a step's clip, <see cref="WidthPx"/> - 2 <see cref="Pad"/> wide, and its height (px; 0: none, hidden). With a band the
        /// panel may be that much taller than <see cref="MaxHeightPx"/> (<see cref="MediaExtraPx"/>) and stands half of it lower
        /// (<see cref="MediaDropPx"/>): it grows DOWNWARD, its top edge stays where a page without a clip has it (under the overhead
        /// menu's look-up zone, as client wave 20 measured), and the text is read that much lower.</summary>
        public RectTransform Media => _media;
        public float MediaPx { get; set; }
        public const float MediaGapPx = 10f;
        /// <summary>The share of the band's height the panel's middle stands lower: 0.5 keeps the top edge where it is without a band
        /// (0: the middle stays at eye level and the panel grows both ways).</summary>
        public const float MediaDropShare = 0.5f;
        public float MediaExtraPx => MediaPx > 0f ? MediaGapPx + MediaPx : 0f;
        public float MediaDropPx => MediaExtraPx * MediaDropShare;
        private float _dropApplied;
        /// <summary>Glides back over the view (smoke, log).</summary>
        public int Glides { get; private set; }

        // client wave 25 (w25tut), item 7 (design note: "Whenever I am meant to be looking up at the menu during the tutorial and I have an
        // instruction to follow on the tutorial panel, have the tutorial panel also move up next to the menu above me. So that I can both
        // follow what it is telling me without having to look down."): while <see cref="Anchor"/> gives a place (LokiGame.W25Tut.cs:
        // beside the menu above), the guide glides there in GlideSeconds and then stays on it every frame (the menu goes with the head's
        // position and the body's heading), facing the eyes; it never follows the view meanwhile and is never counted as away (no
        // pointer). When the place goes, it glides home as after any glide.
        /// <summary>Client wave 25 (w25tut), item 7: the guide's place while it is pinned beside something (null, or a null answer: its
        /// own home, as always).</summary>
        public Func<Vector3?> Anchor;
        /// <summary>Client wave 25: the guide stands on (or glides to) <see cref="Anchor"/>'s place now; times it went there (smoke, log).</summary>
        public bool Anchored => _anchored;
        public int Lifts { get; private set; }
        private bool _anchored; private float _anchorT;

        public static TutorialGuide Create(Transform parent, Func<Camera> head)
        {
            var size = new Vector2(WidthPx, MinHeightPx);
            var rt = UiKit.Panel("TutorialGuide", parent, size, MetresPerPx, true, PanelKind.Hud, PanelLook.Card); // client wave 21 (w21kit): the guide is a parchment card of the theme
            var canvas = rt.GetComponent<Canvas>(); if (canvas != null) canvas.sortingOrder = SortingOrder; // (client wave 23, w23place: under the panels; it was 18)
            var g = rt.gameObject.AddComponent<TutorialGuide>();
            g._rt = rt; g._head = head; g._col = rt.GetComponent<BoxCollider>();
            g._border = (RectTransform)rt.Find("border"); g._bg = (RectTransform)rt.Find("bg");
            float tw = WidthPx - 2f * Pad;
            g._step = UiKit.Label(rt, "", Vector2.zero, new Vector2(tw, 34f), StepPx, TextAnchor.MiddleLeft, UiKit.Dim);
            g._bar = UiKit.Box(rt, "bar", Vector2.zero, new Vector2(tw, 6f), BarBg);
            g._fill = UiKit.Box(rt, "fill", Vector2.zero, new Vector2(0f, 6f), Gold);
            g._fill.pivot = new Vector2(0f, 0.5f);
            var mgo = new GameObject("media", typeof(RectTransform)); mgo.layer = rt.gameObject.layer; mgo.transform.SetParent(rt, false);
            g._media = (RectTransform)mgo.transform; g._media.anchorMin = g._media.anchorMax = g._media.pivot = new Vector2(0.5f, 0.5f); mgo.SetActive(false);
            g._title = UiKit.Label(rt, "", Vector2.zero, new Vector2(tw, 50f), TitlePx, TextAnchor.MiddleLeft, Gold);
            g._title.resizeTextForBestFit = true; g._title.resizeTextMinSize = 30; g._title.resizeTextMaxSize = TitlePx;
            g._about = UiKit.Label(rt, "", Vector2.zero, new Vector2(tw, 100f), AboutPx, TextAnchor.UpperLeft, AboutColour);
            g._now = UiKit.Label(rt, "", Vector2.zero, new Vector2(tw, 50f), NowPx, TextAnchor.UpperLeft, NowColour);
            g._now.fontStyle = FontStyle.Bold;
            g._status = UiKit.Label(rt, "", Vector2.zero, new Vector2(tw, 40f), StatusPx, TextAnchor.UpperLeft, UiKit.Dim);
            g._left = UiKit.Button(rt, Lang.T("Skip step"), Vector2.zero, new Vector2(WidthPx / 2f - 40f, 60f), () => g.LeftPressed?.Invoke(), 30);
            g._right = UiKit.Button(rt, Lang.T("End tutorial"), Vector2.zero, new Vector2(WidthPx / 2f - 40f, 60f), () => g.RightPressed?.Invoke(), 30);
            g._mid = UiKit.Button(rt, Lang.T("Casting ways"), Vector2.zero, new Vector2(WidthPx / 3f - 28f, 60f), () => g.MiddlePressed?.Invoke(), 30); g._mid.gameObject.SetActive(false);
            g.Layout(true);
            rt.gameObject.SetActive(false);
            return g;
        }

        /// <summary>Shows step <paramref name="index"/> (0-based) of <paramref name="count"/>. <paramref name="done"/>: the status line in green
        /// and the step counted on the bar. A null button label hides that button.</summary>
        public void Show(int index, int count, string title, string about, string now, string status, bool done, string left, string right)
            => Show(Lang.F("Tutorial · {0} / {1}", index + 1, count), Mathf.Clamp01(count > 0 ? (index + (done ? 1f : 0f)) / count : 0f), title, about, now, status, done, left, right);

        /// <summary>Client wave 20 (w20tut): the guide with its own top line (the look calibration from Settings: "Look calibration") and the
        /// bar at <paramref name="progress"/> (0-1).</summary>
        public void Show(string header, float progress, string title, string about, string now, string status, bool done, string left, string right)
        {
            bool place = !Visible;
            if (place) _rt.gameObject.SetActive(true);
            Set(_step, header); Set(_title, title); Set(_about, about); Set(_now, now); Set(_status, status);
            _status.color = done ? Green : UiKit.Dim;
            _progress = Mathf.Clamp01(progress);
            Button(_left, left); Button(_right, right); Button(_mid, MiddleLabel);
            Layout(false);
            if (place) PlaceInFront();
        }
        private static void Set(Text t, string s) { s = s ?? ""; if (t.text != s) t.text = s; }
        private static void Button(UiButton b, string label)
        {
            bool on = !string.IsNullOrEmpty(label);
            if (b.gameObject.activeSelf != on) b.gameObject.SetActive(on);
            if (on && b.Text != null && b.Text.text != label) b.SetLabel(label);
        }

        /// <summary>The preferred height of <paramref name="t"/>'s text at <paramref name="px"/> on the text width (px).</summary>
        private static float HeightOf(Text t, int px)
        {
            if (string.IsNullOrEmpty(t.text)) return 0f;
            var gs = t.GetGenerationSettings(new Vector2(WidthPx - 2f * Pad, 10000f));
            gs.scaleFactor = 1f; gs.resizeTextForBestFit = false; gs.fontSize = px; gs.verticalOverflow = VerticalWrapMode.Overflow;
            return Mathf.Ceil(t.cachedTextGeneratorForLayout.GetPreferredHeight(t.text, gs)) + 4f;
        }

        /// <summary>Client wave 20 (w20tut), point 40: the panel as tall as its texts, top to bottom: the step line, the bar, the clip's band
        /// (points 53 / 59, 0 now), the title, the explanation, what to do now, the status, the buttons.</summary>
        private void Layout(bool force)
        {
            string key = _step.text + "\u0001" + _title.text + "\u0001" + _about.text + "\u0001" + _now.text + "\u0001" + _status.text + "\u0001" + MediaPx + "\u0001" + _left.gameObject.activeSelf + _right.gameObject.activeSelf + _mid.gameObject.activeSelf;
            if (!force && key == _laidOut) { SetFill(); return; }
            _laidOut = key;
            float tw = WidthPx - 2f * Pad;
            const float stepH = 34f, barH = 6f, titleH = 50f, gap = 10f, btnH = 60f;
            int aboutPx = AboutPx; float aboutH = HeightOf(_about, aboutPx);
            float nowH = HeightOf(_now, NowPx), statusH = Mathf.Max(HeightOf(_status, StatusPx), _status.text.Length > 0 ? 36f : 0f);
            bool buttons = _left.gameObject.activeSelf || _right.gameObject.activeSelf || _mid.gameObject.activeSelf;
            float media = Mathf.Max(0f, MediaPx);
            float Total(float ah) => Pad + stepH + 8f + barH + (media > 0f ? gap + media : 0f) + gap + titleH + (ah > 0f ? gap + ah : 0f) + (nowH > 0f ? gap + nowH : 0f) + (statusH > 0f ? gap + statusH : 0f) + (buttons ? gap + 6f + btnH : 0f) + Pad;
            // too tall: the explanation shrinks (never what to do now)
            float limit = MaxHeightPx + MediaExtraPx; // (client wave 22, w22clips: the clip's band comes on top of the text's limit)
            while (Total(aboutH) > limit && aboutPx > AboutMinPx) { aboutPx--; aboutH = HeightOf(_about, aboutPx); }
            AboutDrawnPx = aboutPx; _about.fontSize = aboutPx;
            float h = Mathf.Clamp(Total(aboutH), MinHeightPx, limit);
            // the band appeared, went or changed while the guide stands: the panel's middle moves by half the change (its top edge stays)
            if (Mathf.Abs(_dropApplied - MediaDropPx) > 0.01f) { if (Visible && Drop <= 0f) _rt.position += Vector3.down * ((MediaDropPx - _dropApplied) * MetresPerPx); _dropApplied = MediaDropPx; }
            _rt.sizeDelta = new Vector2(WidthPx, h);
            if (_border != null) _border.sizeDelta = new Vector2(WidthPx, h);
            if (_bg != null) _bg.sizeDelta = new Vector2(WidthPx - 6f, h - 6f);
            if (_col != null) _col.size = new Vector3(WidthPx, h, 4f);
            float y = h / 2f - Pad; // from the top edge, going down (anchored at the middle)
            void Put(RectTransform r, float height, float width = -1f) { r.sizeDelta = new Vector2(width < 0f ? tw : width, height); r.anchoredPosition = new Vector2(0f, y - height / 2f); y -= height; }
            Put(_step.rectTransform, stepH); y -= 8f;
            Put(_bar, barH); _fill.anchoredPosition = new Vector2(-tw / 2f, _bar.anchoredPosition.y);
            if (media > 0f) { y -= gap; Put(_media, media); if (!_media.gameObject.activeSelf) _media.gameObject.SetActive(true); }
            else if (_media.gameObject.activeSelf) _media.gameObject.SetActive(false);
            y -= gap; Put(_title.rectTransform, titleH);
            if (aboutH > 0f) { y -= gap; Put(_about.rectTransform, aboutH); } else _about.rectTransform.sizeDelta = new Vector2(tw, 0f);
            if (nowH > 0f) { y -= gap; Put(_now.rectTransform, nowH); } else _now.rectTransform.sizeDelta = new Vector2(tw, 0f);
            if (statusH > 0f) { y -= gap; Put(_status.rectTransform, statusH); } else _status.rectTransform.sizeDelta = new Vector2(tw, 0f);
            float by = -h / 2f + Pad + btnH / 2f;
            if (_mid.gameObject.activeSelf)
            {
                // three buttons side by side (the two outer ones a third of the width each while the middle one is there)
                float w3 = WidthPx / 3f - 28f; _left.Resize(new Vector2(w3, 60f)); _right.Resize(new Vector2(w3, 60f)); _mid.Resize(new Vector2(w3, 60f));
                ((RectTransform)_left.transform).anchoredPosition = new Vector2(-(w3 + 12f), by); ((RectTransform)_mid.transform).anchoredPosition = new Vector2(0f, by);
                ((RectTransform)_right.transform).anchoredPosition = new Vector2(w3 + 12f, by);
            }
            else
            {
                _left.Resize(new Vector2(WidthPx / 2f - 40f, 60f)); _right.Resize(new Vector2(WidthPx / 2f - 40f, 60f));
                ((RectTransform)_left.transform).anchoredPosition = new Vector2(-WidthPx / 4f + 6f, by);
                ((RectTransform)_right.transform).anchoredPosition = new Vector2(WidthPx / 4f - 6f, by);
            }
            SetFill();
        }
        private void SetFill() { _fill.sizeDelta = new Vector2((WidthPx - 2f * Pad) * _progress, 6f); }

        public void Hide() { if (_rt != null && _rt.gameObject.activeSelf) _rt.gameObject.SetActive(false); _glideT = -1f; _away = 0f; _anchored = false; _hasLastHead = false; }

        /// <summary>Client wave 20 (w20tut), point 65: the guide's middle <paramref name="degreesDown"/> below the eyes' level from now on (0: at
        /// eye level, as always); placed there at once when shown.</summary>
        public void SetDrop(float degreesDown)
        {
            Drop = Mathf.Clamp(degreesDown, 0f, 70f);
            if (Visible) PlaceInFront();
        }

        /// <summary>Where the guide stands for the eyes now: ahead (level), <see cref="Up"/> higher, or <see cref="Drop"/> degrees down.</summary>
        private Vector3 InFront()
        {
            var t = _head().transform;
            var f = t.forward; f.y = 0f; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
            if (Mathf.Abs(Side) > 0.01f) f = Quaternion.AngleAxis(Side, Vector3.up) * f; // (client wave 23, w23place: beside the view's middle; + is right)
            if (Drop > 0f) { float a = Drop * Mathf.Deg2Rad; return t.position + (f * Mathf.Cos(a) - Vector3.up * Mathf.Sin(a)) * Distance; }
            return t.position + f * Distance + Vector3.up * (Up - MediaDropPx * MetresPerPx); // (client wave 22: half the clip's band lower, see MediaPx)
        }

        public void PlaceInFront()
        {
            var cam = _head != null ? _head() : null; if (cam == null || _rt == null) return;
            _rt.position = InFront(); UiKit.FaceHead(_rt, cam); _glideT = -1f; _away = 0f; _dropApplied = MediaDropPx;
            _lastHeadPos = cam.transform.position; _hasLastHead = true;
        }

        /// <summary>Once a frame while shown: faces the eyes, follows head translation 1:1, and follows head rotation smoothly.</summary>
        public void Tick(float dt)
        {
            if (!Visible) return;
            var cam = _head != null ? _head() : null; if (cam == null) return;
            var t = cam.transform;
            if (_hasLastHead)
            {
                var headDelta = t.position - _lastHeadPos;
                if (headDelta.sqrMagnitude > 1e-8f) { _rt.position += headDelta; _from += headDelta; _to += headDelta; }
            }
            _lastHeadPos = t.position; _hasLastHead = true;
            // client wave 25 (w25tut), item 7: pinned beside something (the menu above): glide there, then stay on it
            Vector3? at = null; try { at = Anchor != null ? Anchor() : null; } catch (Exception e) { Debug.LogException(e); }
            if (at.HasValue)
            {
                if (!_anchored) { _anchored = true; _anchorT = 0f; _from = _rt.position; Lifts++; }
                _anchorT = Mathf.Min(1f, _anchorT + dt / GlideSeconds);
                _rt.position = Vector3.Lerp(_from, at.Value, Mathf.SmoothStep(0f, 1f, _anchorT));
                _glideT = -1f; _away = 0f; Away = false;
                UiKit.FaceHead(_rt, cam);
                return;
            }
            if (_anchored) { _anchored = false; _from = _rt.position; _to = InFront(); _glideT = 0f; _away = 0f; Glides++; } // (the place went: home)
            if (_glideT >= 0f)
            {
                _to = InFront();
                _glideT += dt / GlideSeconds; float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_glideT));
                _rt.position = Vector3.Lerp(_from, _to, k);
                if (_glideT >= 1f) _glideT = -1f;
                Away = _glideT >= 0f;
            }
            else
            {
                Measure(t);
                bool away = FromView > FollowAngle && FromHome > SideSlack;
                bool hold = (Hold != null && Hold()) || ButtonHot;
                _away = away && !hold ? _away + dt : 0f; Away = _away > 0f;
                if (_away >= FollowAfter) { _from = _rt.position; _to = InFront(); _glideT = 0f; _away = 0f; Glides++; }
                else if (!hold && FromHome > 2f)
                {
                    _rt.position = Vector3.Lerp(_rt.position, InFront(), 1f - Mathf.Exp(-8f * dt));
                }
            }
            UiKit.FaceHead(_rt, cam);
        }
    }
}
