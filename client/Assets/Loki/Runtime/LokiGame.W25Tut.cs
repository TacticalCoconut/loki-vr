using System;
using System.Collections.Generic;
using Loki.UI;
using Loki.VR;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 25 (w25tut): the tutorial. the design's next list after wave 24 (playtest notes; MAS doc
    /// loki-next-list-2026-10-14), items 2-7 and 9, the design's words there.
    ///   Item 2   THE ARROW. A green arrow floats over the practice Poring, pointing down at it, in every part that asks the player to
    ///            reach the Poring (the laser step, each of the look step's three rounds, the "act" step), until the player's laser
    ///            (the laser step), look (the look step) or either (the act step) first reaches it in that part; then it is gone for that
    ///            part (W18TPhase.Poring; drawn by UI/TutorialMarks.cs). After a hop of the look step the next round shows it again.
    ///   Item 3   THE RESIZE STEP. Both grips moved the map too (a resize turns about the hands' middle: VR/DioramaRig.cs), so one squeeze
    ///            of both grips did the move part and then the resize part. Now the move counts only a pan (one grip and its stick, a
    ///            hand's fist: the rig's PanningNow, <see cref="W25MoveDone"/>) and the resize only a squeeze of both grips that BEGINS
    ///            in its own part (<see cref="W25ResizeDone"/>). In the Follow view the step's last part ("... until the view is Follow")
    ///            is not made: it was done at once, a second check mark for nothing (LokiGame.W18Tutorial.cs, the step's Build).
    ///   Item 4   THE LOOK UP gets its own part and check mark (<see cref="W25LookUpPart"/>: Resize mode, Reposition, the bubbles lesson;
    ///            done when the menu above is awake and the part has been shown <see cref="W25LookUpSeconds"/> s). GREEN OUTLINES on every thing
    ///            an action line asks the player to press, take or drag (W18TPhase.Marks; UI/TutorialMarks.cs): "Start", "Enter the
    ///            world", the menu above's "On" (Resize mode, Reposition), "Edit…" and the bubbles' page's "Back", the minimap to carry,
    ///            the practice panel to take, the ring's icon to open, the Settings / Skills icon and their board's tab, the classic
    ///            menu's tab and "Settings…", the Settings window's "Close", the open pad, the open skill wheels, the bubbles, the elbow
    ///            button and the forearm bar; on the page after the casting ways its "Continue".
    ///   Item 5   THE WAND glows green in the wand lesson (W18TPhase.Wand), and is HIDDEN until then: the tutorial starts with no casting
    ///            gesture on (<see cref="W25TutBegin"/>; the player's own comes back at its end unless a way is chosen: item 9), and a
    ///            lesson, done or left, puts the gesture back as it was before it (LokiGame.W22Cast.cs W22LessonEnd).
    ///   Item 6   THE BUBBLES' OPACITY SLIDERS on the menu above take no laser, look or stick while the tutorial runs (washed, as any
    ///            button that cannot be pressed: <see cref="W25SlidersLock"/>; UI/UiSlider.cs).
    ///   Item 7   THE GUIDE BESIDE THE MENU ABOVE while the line is about it (W18TPhase.AtMenu: the overhead step, the look-up parts,
    ///            "On", "Edit…", "Back"): from <see cref="W25LiftDelay"/> s into such a part (or at once when the menu is awake) it glides
    ///            up to stand beside the menu, on the walking hand's side, <see cref="W25LiftGapDeg"/>° from its frame, facing the eyes
    ///            (<see cref="W25GuideAnchor"/>; UI/TutorialGuide.cs Anchor), and stays there until a part is about something else.
    ///   Item 9   THE WAY TO START WITH. Once every way the player can try was tried, the columns stay <see cref="W25AllTriedPause"/> s
    ///            (the last check is seen) and the next page comes by itself (UI/CastingChoice.cs, stage 1): "You tried every way to
    ///            cast." with "Continue" and "Try casting a bit more". "Continue" (or "Continue the tutorial" on the columns) asks
    ///            "Which way do you want to start with?" ("You can switch any time in Settings.", stage 2), one button per way. The way
    ///            pressed is on and every other is off (<see cref="W25StartWith"/>: the casting gesture, the forearm bar), and the
    ///            tutorial goes on. The Casting panel stands further away (UI/CastingWays.cs Distance 1.6 m).
    /// Hooks (marked "client wave 25 (w25tut)"): LokiGame.W18Tutorial.cs (the parts' fields and marks, InitW18Tut, W18TutBegin,
    /// W18TutFinish, UpdateW18Tut's casting branch), LokiGame.W20Tut.cs (UpdateW20Tut, the look step), LokiGame.W22Cast.cs (the lessons,
    /// W22LessonEnd, W22Continue, W22CastLeave), LokiGame.W24Bubbles.cs (the sliders), LokiGame.W13Resize.cs (a quiet gesture change),
    /// UI/TutorialGuide.cs (Anchor), UI/CastingWays.cs (Distance), UI/UiSlider.cs, VR/DioramaRig.cs (ScalingNow).
    /// </summary>
    public sealed partial class LokiGame
    {
        // ---- every number here is Loki's (FLAGGED in design notes)
        /// <summary>Item 4: a look-up part is done when the menu above is awake and the part has been shown at least this long (a look
        /// that was already up when the part began still gets its own moment and check mark).</summary>
        public const float W25LookUpSeconds = 0.5f;
        /// <summary>Item 7: the guide rises beside the menu above this long into a part about it (at once when the menu is awake), and
        /// stands this far beside the menu's frame from the eyes (degrees), at its own distance from the eyes.</summary>
        public const float W25LiftDelay = 0.6f, W25LiftGapDeg = 3f;
        /// <summary>Item 9: the columns with every way checked stay this long before the next page comes.</summary>
        public const float W25AllTriedPause = 1.5f;
        /// <summary>Item 5: the tutorial starts with no casting gesture on (the wand hidden until its lesson).</summary>
        public const bool W25WandHiddenAtStart = true;

        /// <summary>The green marks (item 2's arrow, items 4 / 5's outlines); the page after the casting ways (item 9).</summary>
        public TutorialMarks W25Marks { get; private set; }
        public CastingChoice W25Choice { get; private set; }
        /// <summary>The way chosen to start with ("" none yet); the page after the casting ways is up (its stage: 0 none, 1, 2); it came by
        /// itself (every way tried); the guide stood beside the menu above (times); the arrow went (times) (smoke, log).</summary>
        public string W25ChosenKey => _w25Chosen ?? "";
        public int W25ChoiceStage => W25Choice != null && W25Choice.Visible ? W25Choice.Stage : 0;
        public int W25ChoiceAuto { get; private set; }
        public int W25Lifts { get; private set; }
        public int W25ArrowGone { get; private set; }
        public bool W25GuideLifted => _w25Lifted;
        /// <summary>The arrow is wanted now (the part asks for the Poring and it was not reached in it yet).</summary>
        public bool W25ArrowWanted { get; private set; }

        private string _w25Chosen, _w25Part; private float _w25PartSince, _w25AllTriedAt = -1f, _w25LookSince; private int _w25Gen;
        /// <summary>A part begins (LokiGame.W18Tutorial.cs W18TutGo and the next part, LokiGame.W22Cast.cs W22Pick): the same step's part
        /// begun again is a new part (its arrow and its lift start over).</summary>
        private void W25PartBegan() { _w25Gen++; }
        private bool _w25ArrowDone, _w25Lifted, _w25LastAtMenu, _w25AllShown;
        private UiSlider _w25SliderAll, _w25SliderOwn;
        private readonly Dictionary<string, Transform> _w25Named = new Dictionary<string, Transform>();

        private void InitW25Tut()
        {
            var rig = _boot.Rig; if (rig == null) return;
            W25Marks = TutorialMarks.Create(() => rig.Head); W25Marks.Fill = W25Fill;
            if (W18TGuide != null) W18TGuide.Anchor = W25GuideAnchor;
        }

        /// <summary>LokiGame.W18Tutorial.cs W18TutBegin: no casting way on (item 5), no way chosen, nothing tried (item 9).</summary>
        private void W25TutBegin()
        {
            _w25Chosen = null; _w25AllTriedAt = -1f; _w25AllShown = false; _w25Part = null; _w25Lifted = false;
            W25ChoiceHide();
            if (W25WandHiddenAtStart && Comfort.CastGesture != 0) W13SetCastGesture(0, "the tutorial: no casting way on until its lesson", false);
        }

        // ------------------------------------------------------------------ the parts' helpers (LokiGame.W18Tutorial.cs, LokiGame.W22Cast.cs)
        private static W18TPhase W25Arrow(W18TPhase ph, Func<bool> reached) { ph.Poring = reached; return ph; }
        private static W18TPhase W25Mark(W18TPhase ph, Func<IEnumerable<Transform>> marks) { ph.Marks = marks; return ph; }
        private static W18TPhase W25AtMenu(W18TPhase ph) { ph.AtMenu = true; return ph; }
        private static W18TPhase W25WandGlow(W18TPhase ph) { ph.Wand = true; return ph; }
        private static IEnumerable<Transform> W25Of(Component c) { if (c != null) yield return c.transform; }
        private static IEnumerable<Transform> W25Of(Transform t) { if (t != null) yield return t; }

        /// <summary>Item 4: the look up to the menu above, a part of its own (<paramref name="line"/>: "Look up until the menu above you
        /// appears." or the bubbles lesson's "Look higher ..."), done when the menu is awake, the part shown <see cref="W25LookUpSeconds"/> s at least;
        /// the guide goes up beside the menu (item 7).</summary>
        private W18TPhase W25LookUpPart(Func<string> line)
            => W25AtMenu(W18TP(line, () => W13Overhead != null && W13Overhead.Usable && Time.unscaledTime - _w25LookSince >= W25LookUpSeconds, null, () => _w25LookSince = Time.unscaledTime));

        /// <summary>Wave 28 (wcodex): the unit a part's arrow stands over when it is not the practice Poring (W18TPhase.ArrowOver), alive.</summary>
        private Sprites.Unit W25ArrowUnit(W18TPhase ph)
        {
            Sprites.Unit u = null; try { u = ph.ArrowOver(); } catch (Exception e) { Debug.LogException(e); }
            return u != null && Entities != null && Entities.Get(u.Id) == u ? u : null;
        }
        /// <summary>Item 2: the look is on the practice Poring (its look lock: Game/Targeting.cs).</summary>
        private bool W25LookOnPoring() => _w18tDummy != null && Target != null && Target.Locked != null && Target.Locked.Unit == _w18tDummy;

        // ---- item 3: the resize step's two parts, each by its own action
        private float _w25Moved, _w25Scale0; private Vector3 _w25MapWas; private bool _w25ScaleArmed;
        private void W25MoveEnter() { _w25Moved = 0f; _w25MapWas = _boot.Rig.Diorama.position; }
        /// <summary>The map moved <see cref="W18TMapMoved"/> m by a PAN (one grip and its stick, a hand's fist), summed over the frames the
        /// rig panned; a resize's own turn of the map is not counted.</summary>
        private bool W25MoveDone()
        {
            var rig = _boot.Rig; var p = rig.Diorama.position;
            if (rig.PanningNow) { var d = p - _w25MapWas; d.y = 0f; _w25Moved += d.magnitude; }
            _w25MapWas = p; return _w25Moved >= W18TMapMoved;
        }
        private void W25ResizeEnter() { _w25ScaleArmed = false; _w25Scale0 = _boot.Rig.Diorama.localScale.x; }
        /// <summary>The map resized by <see cref="W18TMapScaled"/> by a squeeze of both grips that began in this part (a squeeze held over
        /// from the part before counts only once it is let go and taken again).</summary>
        private bool W25ResizeDone()
        {
            var rig = _boot.Rig; float s = rig.Diorama.localScale.x;
            if (!rig.ScalingNow) { _w25ScaleArmed = true; _w25Scale0 = s; return false; }
            if (!_w25ScaleArmed) return false;
            float k = s / Mathf.Max(1e-4f, _w25Scale0); return k >= W18TMapScaled || k <= 1f / W18TMapScaled;
        }

        // ---- item 4: what a part's line asks for
        private Func<IEnumerable<Transform>> W25MarkOverhead(string button) => () => W25Of(W25OverheadButton(button));
        private Transform W25OverheadButton(string name)
        {
            if (W13Overhead == null) return null;
            if (_w25Named.TryGetValue("over:" + name, out var t) && t != null) return t;
            foreach (var b in W13Overhead.GetComponentsInChildren<UiButton>(true)) if (b.name == name) { _w25Named["over:" + name] = b.transform; return b.transform; }
            return null;
        }
        /// <summary>A button of the classic wrist menu by its object's name ("btn:" and its English label: UI/UiKit.cs Button).</summary>
        private Func<IEnumerable<Transform>> W25MarkWrist(string name) => () =>
        {
            if (Hud == null || Hud.Wrist == null || !Hud.Wrist.ShownOnWrist) return W25Of((Transform)null);
            Transform found = null;
            if (_w25Named.TryGetValue("wrist:" + name, out var t) && t != null && t.gameObject.activeInHierarchy) found = t;
            else foreach (var b in Hud.Wrist.GetComponentsInChildren<UiButton>(false)) if (b.name == name) { found = b.transform; _w25Named["wrist:" + name] = found; break; }
            return W25Of(found);
        };
        private IEnumerable<Transform> W25MarkPracticePanel() => W25Of(_w18tPanel != null && _w18tPanel.Visible && !W18TPanelHeld ? _w18tPanel.transform : null);
        /// <summary>The ring's Character icon (group 0) that the menu tutorial step asks the player to highlight and open.</summary>
        private IEnumerable<Transform> W25MarkRingPick()
        {
            var ring = Hud != null && Hud.Wrist != null ? Hud.Wrist.Ring : null;
            return W25Of(ring != null && ring.Shown ? ring.Button(0) : null);
        }
        /// <summary>The way to a group's page: its board's tab <paramref name="tab"/> while that board shows, else the group's icon on the ring.</summary>
        private Func<IEnumerable<Transform>> W25MarkRingTo(int group, string tab) => () =>
        {
            var w = Hud != null ? Hud.Wrist : null; if (w == null) return W25Of((Transform)null);
            if (w.Board != null && w.Board.Shown && w.BoardGroup == group)
            {
                int e = group >= 0 && group < WristMenu.Groups.Length ? Array.FindIndex(WristMenu.Groups[group].Entries, x => x.Label == tab) : -1;
                return W25Of(e >= 0 && w.CurrentTab != WristMenu.Tab.Skills ? w.Board.Tab(e) : null);
            }
            return W25Of(w.Ring != null && w.Ring.Shown ? w.Ring.Button(group) : null);
        };
        private UiButton _w25Close;
        /// <summary>The Settings window's own "Close" (its footer's button of that label).</summary>
        private IEnumerable<Transform> W25MarkSettingsClose()
        {
            if (ComfortUi == null || !ComfortUi.Visible || ComfortUi.Window == null) return W25Of((Transform)null);
            string close = Lang.T("Close");
            if (_w25Close == null || !_w25Close.gameObject.activeInHierarchy || _w25Close.Text == null || _w25Close.Text.text != close)
            {
                _w25Close = null;
                foreach (var b in ComfortUi.Window.GetComponentsInChildren<UiButton>(false)) if (b.Text != null && b.Text.text == close) { _w25Close = b; break; }
            }
            return W25Of(_w25Close);
        }
        private IEnumerable<Transform> W25MarkPads()
        {
            foreach (bool left in new[] { true, false }) { var p = W12Pad(left); if (p != null && p.Shown) yield return p.transform; }
        }
        private IEnumerable<Transform> W25MarkWheels()
        {
            if (!WheelsOpen) yield break;
            if (WheelLeft != null) yield return WheelLeft.transform;
            if (WheelRight != null) yield return WheelRight.transform;
        }
        private IEnumerable<Transform> W25MarkBubbles()
        {
            for (int i = 0; i < BubbleCount; i++) { var b = W20Bubble(i); if (b != null && b.Drawn) yield return b.transform; }
        }

        // ------------------------------------------------------------------ once a frame (LokiGame.W20Tut.cs UpdateW20Tut)
        private W18TStep W25StepNow => W18TutRunning && _w18tSteps != null && _w18tIndex >= 0 && _w18tIndex < _w18tSteps.Count ? (_w22Lesson ?? _w18tSteps[_w18tIndex]) : null;
        /// <summary>The part waiting now (null: none: the step is done, or the casting ways' page is up).</summary>
        private W18TPhase W25PartNow
        {
            get
            {
                var st = W25StepNow; if (st == null || _w18tDoneAt >= 0f || (st.Key == "casting" && _w22Lesson == null) || st.Phases.Count == 0) return null;
                return st.Phases[Mathf.Clamp(_w18tPhase, 0, st.Phases.Count - 1)];
            }
        }

        private void UpdateW25Tut()
        {
            W25SlidersLock();
            if (!W18TutRunning)
            {
                if (W13Overhead != null) W13Overhead.Held = W14OverheadHeld;
                _w25Part = null; _w25Lifted = false; W25ArrowWanted = false; return;
            }
            float now = Time.unscaledTime; var st = W25StepNow; var ph = W25PartNow;
            string key = st == null ? "" : st.Key + ":" + _w18tPhase + "#" + _w25Gen + (_w18tDoneAt >= 0f ? ":done" : "");
            if (key != _w25Part) { _w25Part = key; _w25PartSince = now; _w25ArrowDone = false; if (ph != null) _w25LastAtMenu = ph.AtMenu; }
            // item 2: the arrow goes the moment the laser / the look reaches the Poring in this part
            bool reached = false;
            if (ph != null && ph.Poring != null && !_w25ArrowDone) { try { reached = ph.Poring(); } catch (Exception e) { Debug.LogException(e); } }
            if (reached) { _w25ArrowDone = true; W25ArrowGone++; Debug.Log($"[Tutorial] the arrow over the Poring goes: '{key}' reached it"); }
            W25ArrowWanted = ph != null && ph.Poring != null && !_w25ArrowDone && (ph.ArrowOver != null ? W25ArrowUnit(ph) != null : _w18tDummy != null && Entities != null && Entities.Get(W18TDummyId) == _w18tDummy);
            // item 7: the guide beside the menu above while the line is about it (a step done on such a part keeps it there for its "Done!")
            bool want = ph != null ? ph.AtMenu : _w18tDoneAt >= 0f && _w25LastAtMenu && st != null && st.Key != "casting";
            if (!want) { if (_w25Lifted) Debug.Log($"[Tutorial] the guide comes back from beside the menu above ('{key}')"); _w25Lifted = false; }
            else if (!_w25Lifted && W13Overhead != null && (W13Overhead.Usable || now - _w25PartSince >= W25LiftDelay))
            { _w25Lifted = true; W25Lifts++; Debug.Log($"[Tutorial] the guide goes up beside the menu above ('{key}', the menu {(W13Overhead.Usable ? "awake" : "not looked at yet")})"); }
            // Item 25: once the look-up part succeeded and the step is on an overhead-menu button part (AtMenu && Marks != null),
            // keep W13Overhead open and usable so glancing at the tutorial guide beside it never hides the menu or reverts the prompt.
            if (W13Overhead != null)
            {
                bool holdMenu = ph != null && ph.AtMenu && ph.Marks != null;
                W13Overhead.Held = W14OverheadHeld || holdMenu;
            }
        }

        /// <summary>Item 7: the guide's place beside the menu above (UI/TutorialGuide.cs Anchor): the look at the menu's middle turned
        /// about the menu's own up line, to the walking hand's side, by the menu's half width + <see cref="W25LiftGapDeg"/> + the guide's
        /// half width as the eyes see them, at the guide's own distance (<see cref="TutorialGuide.Distance"/>): level with the menu and
        /// clear of its frame (its wood too) from the eyes. (A place in the menu's plane was not: the guide turned to face the eyes swings
        /// its near edge back over the menu's edge: smoke w25tut D on the first build.)</summary>
        private Vector3? W25GuideAnchor()
        {
            if (!_w25Lifted || !W18TutRunning || W13Overhead == null || W18TGuide == null || _boot.Rig == null || _boot.Rig.Head == null) return null;
            var (c, r, u) = W13Overhead.FrameWorld; var eye = _boot.Rig.Head.transform.position; var to = c - eye; float dm = to.magnitude;
            if (dm < 1e-3f || u.sqrMagnitude < 1e-8f) return null;
            float dg = TutorialGuide.Distance;
            if (W25StepNow != null && W25StepNow.Key == "cast-bubbles" && r.sqrMagnitude > 1e-8f)
            {
                float halfH = W18TGuide.SizePx.y * TutorialGuide.MetresPerPx * 0.5f;
                float pitchDown = Mathf.Atan2(u.magnitude, dm) * Mathf.Rad2Deg + W25LiftGapDeg + Mathf.Atan2(halfH, dg) * Mathf.Rad2Deg;
                return eye + Quaternion.AngleAxis(pitchDown, r.normalized) * (to / dm) * dg;
            }
            float side = Comfort.LeftHanded ? 1f : -1f; // (+: to the right about the menu's up line; the walking hand is the left one of a right-handed player)
            float half = W18TGuide.SizePx.x * TutorialGuide.MetresPerPx * 0.5f;
            float turn = Mathf.Atan2(r.magnitude, dm) * Mathf.Rad2Deg + W25LiftGapDeg + Mathf.Atan2(half, dg) * Mathf.Rad2Deg;
            return eye + Quaternion.AngleAxis(side * turn, u.normalized) * (to / dm) * dg;
        }

        /// <summary>The marks of the frame (UI/TutorialMarks.cs LateUpdate): the arrow, the outlines of what the part asks for, the wand.</summary>
        private void W25Fill(TutorialMarks m)
        {
            if (!W18TutRunning) return;
            if (W25Choice != null && W25Choice.Visible && W25Choice.Stage == 1 && W25Drawn(W25Choice.ContinueButton.transform)) m.Outline((RectTransform)W25Choice.ContinueButton.transform);
            var ph = W25PartNow; if (ph == null) return;
            if (W25ArrowWanted) { var top = ph.ArrowOver != null ? WCodexTutTop(W25ArrowUnit(ph)) : W20DummyTop(); if (top.HasValue) m.Arrow(top.Value); }
            if (ph.Marks != null)
            {
                IEnumerable<Transform> list = null; try { list = ph.Marks(); } catch (Exception e) { Debug.LogException(e); }
                if (list != null) foreach (var t in list) if (t is RectTransform rt && W25Drawn(rt)) m.Outline(rt);
            }
            if (ph.Wand && W12Wand != null && W12Wand.Shown && W12Wand.Tip != null)
            {
                // the wand: a band along it from its handle's end to its bead, turned to the eyes, as thick as its handle and bead
                var a = W12Wand.Wand.position; var b = W12Wand.Tip.position; var axis = b - a; float len = axis.magnitude; var cam = _boot.Rig.Head;
                if (len > 1e-3f && cam != null)
                {
                    axis /= len; var mid = (a + b) * 0.5f; var side = Vector3.Cross(axis, (cam.transform.position - mid).normalized);
                    if (side.sqrMagnitude > 1e-6f)
                    {
                        side.Normalize(); float r = 0.013f * Mathf.Max(0.3f, W12Wand.Wand.localScale.x);
                        m.Outline(a - side * r - axis * r, b - side * r + axis * r, b + side * r + axis * r, a + side * r - axis * r); m.Marked.Add("Wand");
                    }
                }
            }
        }

        /// <summary>A panel's piece is drawn now: active, its canvas on, not faded out by its groups.</summary>
        private static bool W25Drawn(Transform t)
        {
            if (t == null || !t.gameObject.activeInHierarchy) return false;
            var cv = t.GetComponentInParent<Canvas>(); if (cv != null && !cv.rootCanvas.enabled) return false;
            float a = 1f; foreach (var g in t.GetComponentsInParent<CanvasGroup>()) a *= g.alpha;
            return a > 0.3f;
        }

        // ------------------------------------------------------------------ item 6: the bubbles' opacity sliders
        /// <summary>While the tutorial runs the two opacity sliders take nothing (washed as a button that cannot be pressed); after it
        /// they work again.</summary>
        private void W25SlidersLock()
        {
            bool on = !W18TutRunning;
            foreach (var s in new[] { _w25SliderAll, _w25SliderOwn })
            {
                if (s == null || s.Track == null || s.Track.Interactable == on) continue;
                s.Track.Interactable = on; s.Track.SetHover(s.Track.Hovered && on); // (a drag under way writes nothing more: UI/UiSlider.cs DragTo)
                Debug.Log($"[Bubbles] the opacity slider '{s.Track.name}' {(on ? "works again" : "takes nothing while the tutorial runs")}");
            }
        }

        // ------------------------------------------------------------------ item 9: the way to start with
        /// <summary>LokiGame.W22Cast.cs W22LessonEnd, a lesson done: once every way the player can try was tried (the first time), the
        /// page after the casting ways comes <see cref="W25AllTriedPause"/> s later.</summary>
        private void W25AfterTried()
        {
            if (_w25AllShown || _w22Ways == null) return;
            foreach (var w in _w22Ways) { bool blocked = w.Blocked != null && w.Blocked() != null; if (!blocked && !_w22Tried.Contains(w.Key)) return; }
            _w25AllShown = true; _w25AllTriedAt = Time.unscaledTime;
            Debug.Log($"[Tutorial] every way that can be tried was tried ({string.Join(", ", _w22Tried)}): the next page in {W25AllTriedPause:0.0} s");
        }

        /// <summary>LokiGame.W18Tutorial.cs UpdateW18Tut, the casting step with no lesson: the page after the columns, when it is up or
        /// due. True: it is the page now (the columns are not ticked).</summary>
        private bool W25ChoiceTick(float dt)
        {
            if (_w25AllTriedAt >= 0f && Time.unscaledTime - _w25AllTriedAt >= W25AllTriedPause) { _w25AllTriedAt = -1f; W25ChoiceAuto++; W25ChoiceShow(1, "every way was tried"); }
            if (W25Choice == null || !W25Choice.Visible) return false;
            W25ChoiceTexts();
            return true;
        }

        /// <summary>The page after the casting ways at <paramref name="stage"/> (1: every way tried, Continue / Try more; 2: which way to
        /// start with); the columns hidden.</summary>
        private void W25ChoiceShow(int stage, string why)
        {
            var rig = _boot.Rig; if (rig == null || _w22Ways == null) return;
            if (W25Choice == null || W25Choice.WayButtons.Count != _w22Ways.Count)
            {
                if (W25Choice != null) Destroy(W25Choice.gameObject);
                W25Choice = CastingChoice.Create(rig.transform, () => rig.Head, _w22Ways.Count);
                W25Choice.ContinuePressed = () => W25ChoiceShow(2, "its 'Continue'");
                W25Choice.MorePressed = () => { W25ChoiceHide(); Debug.Log("[Tutorial] 'Try casting a bit more': the columns again"); };
                W25Choice.EndPressed = () => { if (W18TutRunning) W18TutRight(); };
                W25Choice.WayPressed = W25StartWith;
                W25Choice.EndButton.LaserOnly = true; // (client wave 23, w23lines, item 5's rule: a button that ends the tutorial is the laser's alone)
            }
            _w25AllTriedAt = -1f; // (a page asked for meanwhile, "Continue the tutorial" in the pause: the one that came by itself is not due any more)
            W22ColumnsHide(); W18TGuide.Hide();
            W25Choice.Show(); // (in front of the eyes when it was not up)
            W25ChoiceTexts(stage);
            Debug.Log($"[Tutorial] the page after the casting ways, stage {stage} ({why}); tried {(_w22Tried.Count > 0 ? string.Join(", ", _w22Tried) : "none")}");
        }
        private static bool W25IsStartWay(string key) =>
            key == "cast-pad" || key == "cast-flick" || key == "cast-wand" || key == "cast-elbow" || key == "cast-wheels";

        private void W25ChoiceTexts(int stage = 0)
        {
            var c = W25Choice; if (c == null || _w22Ways == null) return;
            if (stage == 0) stage = c.Stage;
            if (stage == 1) { c.Set(1, Lang.T("You tried every way to cast."), W23Press(Lang.T("Continue")), null); return; }
            var labels = new List<string>();
            foreach (var w in _w22Ways) labels.Add(!W25IsStartWay(w.Key) || (w.Blocked != null && w.Blocked() != null) ? null : w.Name());
            c.Set(2, Lang.T("Which way do you want to start with?"), Lang.T("You can switch any time in Settings.") + "\n" + Lang.F("Point at one and {0}.", W23Pull()), labels);
        }
        private void W25ChoiceHide() { if (W25Choice != null) W25Choice.Hide(); }

        /// <summary>A way pressed on the page after the casting ways: it is on, every other one off (the casting gesture: the pad 1,
        /// the flick 2, the wand 3, else off; the forearm bar: by the elbow button and shown for the forearm bar, else always off; the
        /// skill wheels, the bubbles and the Skills page have no switch and stay as they are), and the tutorial goes on.</summary>
        private void W25StartWith(int i)
        {
            if (!W18TutRunning || _w22Ways == null || i < 0 || i >= _w22Ways.Count || _w18tSteps[_w18tIndex].Key != "casting") return;
            var w = _w22Ways[i]; if (!W25IsStartWay(w.Key) || (w.Blocked != null && w.Blocked() != null)) return;
            _w25Chosen = w.Key;
            Comfort.SkillWheels = w.Key == "cast-wheels";
            int gesture = w.Key == "cast-pad" ? 1 : w.Key == "cast-flick" ? 2 : w.Key == "cast-wand" ? 3 : 0;
            W13SetCastGesture(gesture, "the tutorial: the way to start with", false);
            if (w.Key == "cast-elbow") { W13SetBarMode(2, "the tutorial: the way to start with"); if (!Comfort.ElbowBarOpen) { Comfort.ElbowBarOpen = true; Comfort.Save(); } }
            else W13SetBarMode(1, "the tutorial: the way to start with (every other way off)");
            W25ChoiceHide();
            _w18tLog.Add("casting:done");
            Debug.Log($"[Tutorial] the way to start with: '{w.Key}' (casting gesture {Comfort.CastGesture}, forearm bar {Comfort.QuickBarModes[Comfort.QuickBarMode]}, skill wheels {Comfort.SkillWheels}); tried {(_w22Tried.Count > 0 ? string.Join(", ", _w22Tried) : "none")}");
            Hud?.Notify(Lang.F("You start with: {0}", w.Name()), 4f);
            W18TutGo(_w18tIndex + 1);
        }
    }
}
