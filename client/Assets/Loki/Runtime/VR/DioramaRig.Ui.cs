using System.Collections.Generic;
using UnityEngine;

namespace Loki.VR
{
    /// <summary>
    /// Client wave 11 (w11menu), from the design's 2026-10-06 playtest: how the rig chooses a button.
    ///   Which of several buttons under one ray: a list row is a button with its ★ / Use / Equip buttons on top of it, all with
    ///   colliders whose front faces lie in one plane, so Physics.Raycast returned either one and it changed from frame to frame
    ///   ("it keeps rapidly flashing"; "instead of equipping the item ... it actually just opens up the item's description page").
    ///   Now every button of the panel the ray meets first is looked at and the one on top wins (UI.UiButton.Outranks: the higher
    ///   priority, then the smaller button), the same every frame, for the laser and for the gaze.
    ///   The gaze: one calibrated ray (<see cref="GazeRay"/>: Settings → Comfort moves its start between the eyes and turns it up /
    ///   down) for the buttons here and for the world's gaze lock and gaze cell (Game/Targeting.cs). It chooses with tolerance: on
    ///   the panel looked at, the button right under it, else the nearest one within <see cref="GazeToleranceDeg"/>; the button
    ///   chosen is kept until the gaze has moved <see cref="GazeHysteresisDeg"/> away from it or into another (no flicker at an edge).
    ///   A panel hides what is behind it: only buttons of the first panel the gaze meets can be chosen, the world's gaze lock does
    ///   not reach through a panel (<see cref="UiBetween"/>), and while the gaze is on a panel a trigger pull that presses no button
    ///   does nothing in the world (<see cref="GazeOnPanel"/>).
    ///   The pointer hand's stick belongs to the slider or the scrolling text under the laser / gaze (UI.IUiStick), and a slider's
    ///   knob follows the laser while the trigger is held.
    ///   The dot: where the gaze ray lands, while <see cref="ShowGazeDot"/> says so.
    /// </summary>
    public sealed partial class DioramaRig
    {
        /// <summary>The laser's order among the transparent renderers. Unity sorts them by sorting order BEFORE the render queue, and
        /// the panels are canvases of order 10 (UI/UiKit.cs Panel; name plates 5): at the default order 0 the laser was painted before
        /// every panel whatever its queue, i.e. under them ("the laser only disappears when I point at UI elements").</summary>
        public const int LaserSortingOrder = 30;
        public const float GazeToleranceDeg = 1.5f, GazeHysteresisDeg = 0.5f;
        /// <summary>Half the distance between the eyes when the headset does not say (the average adult's 64 mm).</summary>
        public const float DefaultHalfEyeDistance = 0.032f;
        public float HalfEyeDistance { get; private set; } = DefaultHalfEyeDistance;

        /// <summary>The gaze ray as calibrated: from between the eyes (or from an eye: Comfort.GazeSide), turned up / down by
        /// Comfort.GazeUp degrees. The buttons and the world's gaze lock both use this one. Client wave 20 (w20tut), point 65: then turned by
        /// this PC's look calibration (VR/LookCalibration.cs Apply; none: unchanged).</summary>
        public Ray GazeRay
        {
            get
            {
                var r = GazeRaySliders;
                return LookCalibration.Done ? new Ray(r.origin, LookCalibration.Apply(Head.transform, r.direction)) : r;
            }
        }
        /// <summary>Client wave 20 (w20tut), point 65: the gaze ray with the sliders only (wave 11's ray): what the look calibration measures
        /// (UI/LookCalibrator.cs), so a new calibration never builds on the old one.</summary>
        public Ray GazeRaySliders
        {
            get
            {
                var h = Head.transform;
                var o = h.position + h.right * (Comfort.GazeSide * HalfEyeDistance);
                // Unity: a positive turn about +right tips a forward line down
                var d = Mathf.Abs(Comfort.GazeUp) > 1e-4f ? Quaternion.AngleAxis(-Comfort.GazeUp, h.right) * h.forward : h.forward;
                return new Ray(o, d);
            }
        }
        /// <summary>The gaze ray met a panel this frame (a button or a panel's background), where, and which panel.</summary>
        public bool GazeOnPanel { get; private set; }
        public Vector3 GazePanelPoint { get; private set; }
        public Transform GazePanel { get; private set; }
        /// <summary>Trigger pulls that did nothing because the gaze was on a panel with no button chosen (smoke, log).</summary>
        public int GazeBlockedClicks { get; private set; }
        /// <summary>The gaze dot is wanted now (LokiGame: the setting, or Settings → Comfort is open).</summary>
        public System.Func<bool> ShowGazeDot;
        public Transform GazeDot { get; private set; }
        /// <summary>The slider the laser is dragging (null: none).</summary>
        public UI.UiSlider Dragging { get; private set; }
        /// <summary>The pointer hand's stick was used by a slider / scrolling text this frame (so the world did not turn).</summary>
        public bool StickTakenByUi { get; private set; }
        /// <summary>Client wave 14 (w14laser), point 11: the hand whose grip alone is the camera's this frame (resize mode, or the Table view's
        /// free grab; no board held; null: none or both): the Follow view's pitch follows its height meanwhile (VR/ViewController.cs). It replaces
        /// wave 11's turn stick (design note, 2026-10-07: "where you need to hold the turn stick and then raise or lower my hand is very bad").</summary>
        public Transform PitchHand { get; private set; }
        // ---- client wave 11 (w11menu), part 2: the new menu (UI/MenuRing.cs, UI/MenuBoard.cs)
        /// <summary>The launcher ring has the walking hand's stick (it points at the ring's icons): no walking, no table move meanwhile.</summary>
        public bool WalkStickTaken { get; set; }
        /// <summary>The walking hand's stick as a menu reads it.</summary>
        public Vector2 OffStick
        {
            get
            {
                var v = InputHeld ? Vector2.zero : Off.StickValue;
                return v;
            }
        }
        /// <summary>A trigger pull this frame, and the button it pressed (null: none): the ring takes a pull that pressed nothing.</summary>
        public bool ClickedThisFrame { get; private set; }
        public UI.UiButton PressedThisFrame { get; private set; }
        /// <summary>While this says so, a pull that presses no button does nothing in the world (the ring is open).</summary>
        public System.Func<bool> BlockWorldClicks;
        /// <summary>Client wave 13 (w13items), the brief's twelfth item: a pull that pressed no button and landed on a panel (the laser's panel,
        /// else the one the look is on) is offered to that panel's owner first; true = taken (the bag: the locked item is used:
        /// UI/WristMenu.Board.cs BagPull). Pulls taken so far (smoke, log).</summary>
        public System.Func<Transform, bool> PanelPull;
        public int PanelPulls { get; private set; }
        /// <summary>Client wave 23 (w23tree): a pull that pressed no button is offered here FIRST (before a panel's own pull and before
        /// the world); true = taken: it does nothing else (the skill trees: a pull on nothing takes the locked skill's lock off and
        /// neither walks nor attacks: LokiGame.W22Tree.cs W23TreeEmptyPull). Pulls taken so far (smoke, log).</summary>
        public System.Func<bool> EmptyPull;
        public int EmptyPulls { get; private set; }
        /// <summary>Client wave 13 (w13resize): the pointing hand holds the wand while a menu is open (LokiGame.W12Cast.cs): its trigger
        /// presses buttons (laser or look) and does nothing in the world.</summary>
        public bool MenuClicksOnly { get; set; }
        /// <summary>Client wave 12 (w12cast): the pointer hand holds the wand: its laser is not drawn (the wand's own line is, along the
        /// same ray: VR/WandHolster.cs); the ray itself, what it is on and the cell cursor go on as always.</summary>
        public bool HidePointerRay { get; set; }
        /// <summary>The panel the laser is on (a button or the background), null: none.</summary>
        public Transform LaserPanel { get; private set; }
        /// <summary>The board the pointer hand holds by its grip (null: none), and whether both grips resize it now.</summary>
        public UI.UiGrabbable Grabbed { get; private set; }
        public bool GrabResizing { get; private set; }
        private float _grabDist, _grabHands0, _grabScale0; private Vector3 _grabLocal; private bool _gripWas;

        // client wave 13 (w13resize): a board is taken and resized by a grip in RESIZE MODE only, as the table is (LokiInput.Hand.
        // CameraGrip); outside the mode a grip with the laser on a board does nothing to it
        private bool MainGrip(LokiInput.Hand r) => r.CameraGrip;
        private bool OffGrip(LokiInput.Hand l) => l.CameraGrip;

        /// <summary>A board is moved by grabbing it (resize mode): the pointer hand's grip made while its laser is on the board holds
        /// it; it then follows where the laser points, at the distance it was taken at, always turned to the head; with the other
        /// hand's grip too, pulling the hands apart / together resizes it (as the table); letting go leaves it there
        /// (UiGrabbable.Released). The table is not resized meanwhile (Update: bothGrips).</summary>
        private void UpdateGrab(LokiInput.Hand l, LokiInput.Hand r, Vector3 origin, Vector3 dir)
        {
            bool grip = MainGrip(r), down = grip && !_gripWas; _gripWas = grip;
            if (Grabbed == null)
            {
                if (!down || LaserPanel == null || !PointedWorld.HasValue) return;
                var g = LaserPanel.GetComponent<UI.UiGrabbable>();
                if (g == null || g.Held) return; // (client wave 13, w13grab: a hand holds it by its edge already: LokiGame.W13Grab.cs)
                Grabbed = g; g.Held = true; g.Grabs++; GrabResizing = false;
                _grabDist = Vector3.Distance(origin, PointedWorld.Value);
                _grabLocal = g.transform.InverseTransformPoint(PointedWorld.Value);
                Haptics.Pulse(Haptics.Kind.Grab, true, 0.2f, 0.03f);
                return;
            }
            var t = Grabbed.transform;
            if (!grip || !Grabbed.isActiveAndEnabled)
            {
                Grabbed.Held = false; var done = Grabbed; Grabbed = null; GrabResizing = false;
                try { done.Released?.Invoke(); } catch (System.Exception e) { Debug.LogException(e); }
                return;
            }
            bool two = OffGrip(l);
            float hands = Mathf.Max(Vector3.Distance(LeftHand.position, RightHand.position), 0.05f);
            if (two && !GrabResizing) { GrabResizing = true; _grabHands0 = hands; _grabScale0 = t.localScale.x; }
            else if (!two) GrabResizing = false;
            if (GrabResizing)
            {
                float s = Mathf.Clamp(_grabScale0 * hands / _grabHands0, Grabbed.MinScale, Grabbed.MaxScale);
                t.localScale = Vector3.one * s;
            }
            // the point of the board that was taken stays on the laser, at the distance it was taken at; the board faces the head
            var p = origin + dir * _grabDist;
            t.position = p;
            for (int i = 0; i < 6; i++) { UI.UiKit.FaceHead(t, Head); t.position = p - (t.TransformPoint(_grabLocal) - t.position); } // (turning it moves the taken point: a few rounds settle both)
        }

        private static readonly RaycastHit[] UiHits = new RaycastHit[96];
        private readonly List<UI.UiButton> _cand = new List<UI.UiButton>(256);
        private UI.UiButton _gazeKeep;
        private Material _dotMat;

        /// <summary>Client wave 14 (w14ui), point 18 (design note, 2026-10-07: "When I try to aim with the laser at a menu and the ground is in the way,
        /// the laser gets interrupted by the ground and I can't click on the menu"): of the first <paramref name="n"/> hits, the nearest PANEL
        /// collider (a button or a panel's background: <see cref="IsUi"/>) whatever of the world lies in front of it, else the nearest
        /// collider (-1: none). The panels are drawn over the world (Loki/UIOverlay, no depth test), so a panel the laser crosses is what
        /// the eye sees there. <paramref name="overWorld"/>: a world collider was nearer on the ray (the panel won over it).</summary>
        /// <summary>Client wave 20 (w20rooms), point 54: a world collider at a point the game does not draw (an indoor map's other rooms:
        /// LokiGame sets it from World/RoomMask.cs): the laser goes through it. Null: nothing is skipped.</summary>
        public static System.Func<Collider, Vector3, bool> HiddenWorld;

        private static int LaserHit(int n, out bool overWorld)
        {
            int best = -1, ui = -1;
            for (int i = 0; i < n; i++)
            {
                if (HiddenWorld != null && !IsUi(UiHits[i].collider) && HiddenWorld(UiHits[i].collider, UiHits[i].point)) continue;
                if (best < 0 || UiHits[i].distance < UiHits[best].distance) best = i;
                if ((ui < 0 || UiHits[i].distance < UiHits[ui].distance) && IsUi(UiHits[i].collider)) ui = i;
            }
            overWorld = ui >= 0 && ui != best && UiHits[best].distance < UiHits[ui].distance;
            return ui >= 0 ? ui : best;
        }

        /// <summary>Client wave 14 (w14ui), point 18: the laser crossed a world collider before the panel it is on this frame (the panel won).</summary>
        public bool LaserPanelOverWorld { get; private set; }

        /// <summary>Client wave 14 (w14ui), point 18: the nearest panel collider on a ray of the laser's kind (every layer: the see-through
        /// minimap's too), whatever of the world lies in front of it (the wand's line: LokiGame.W12Wand.cs W12Ray).</summary>
        public bool LaserOnPanel(Ray ray, float max, out RaycastHit hit)
        {
            hit = default;
            int n = Physics.RaycastNonAlloc(ray, UiHits, max, ~0, QueryTriggerInteraction.Ignore), best = -1;
            for (int i = 0; i < n; i++)
            {
                if (best >= 0 && UiHits[i].distance >= UiHits[best].distance) continue;
                if (IsUi(UiHits[i].collider)) best = i;
            }
            if (best < 0) return false;
            hit = UiHits[best];
            return true;
        }

        private static bool IsUi(Collider c) => c.GetComponent<UI.UiButton>() != null || c.GetComponent<UI.UiBlocker>() != null;

        /// <summary>Of the buttons of <paramref name="panel"/> among the first <paramref name="n"/> hits: the one on top.</summary>
        private static UI.UiButton TopButton(int n, Transform panel)
        {
            UI.UiButton top = null;
            for (int i = 0; i < n; i++)
            {
                var b = UiHits[i].collider.GetComponent<UI.UiButton>();
                if (b == null || !b.isActiveAndEnabled || b.Panel != panel) continue;
                if (UI.UiButton.Outranks(b, top)) top = b;
            }
            return top;
        }

        /// <summary>The first panel a ray meets (a button or a panel's background), whatever of the world lies in front of it: the
        /// panels are drawn over the world (Loki/UIOverlay, no depth test), so what the eye sees there is the panel.</summary>
        public bool RayOnUi(Ray ray, float max, out RaycastHit hit)
        {
            hit = default;
            // client wave 13 (w13look), item 22: the look's rays skip the layer only the laser meets (the see-through minimap's colliders:
            // UI/LookSelect.cs LaserOnlyLayer, UI/Minimap.W3Walk.cs): it is never "on a panel" there and nothing behind it is hidden
            int n = Physics.RaycastNonAlloc(ray, UiHits, max, UI.LookSelect.GazeMask, QueryTriggerInteraction.Ignore), best = -1;
            for (int i = 0; i < n; i++)
            {
                if (best >= 0 && UiHits[i].distance >= UiHits[best].distance) continue;
                if (IsUi(UiHits[i].collider)) best = i;
            }
            if (best < 0) return false;
            hit = UiHits[best];
            return true;
        }

        /// <summary>A panel lies between two points (the gaze lock asks: a unit behind an open menu is not a target).</summary>
        public bool UiBetween(Vector3 from, Vector3 to)
        {
            var d = to - from; float len = d.magnitude;
            if (len < 1e-4f) return false;
            return RayOnUi(new Ray(from, d / len), len, out _);
        }

        /// <summary>The angles between the gaze and a button's face, in degrees, as seen from <paramref name="range"/> metres:
        /// outside (0 on the face), inside (how deep inside the face, 0 outside) and the face's smaller side.</summary>
        private static void Measure(UI.UiButton b, Vector3 point, float range, out float outside, out float inside, out float minDim)
        {
            var rt = (RectTransform)b.transform; var l = rt.InverseTransformPoint(point); var r = rt.rect;
            float dx = Mathf.Abs(l.x - r.center.x) - r.width * 0.5f, dy = Mathf.Abs(l.y - r.center.y) - r.height * 0.5f;
            float k = Mathf.Abs(rt.lossyScale.x) * Mathf.Rad2Deg / Mathf.Max(range, 0.05f); // small angles: metres over the distance
            if (dx <= 0f && dy <= 0f) { outside = 0f; inside = Mathf.Min(-dx, -dy) * k; }
            else { outside = new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude * k; inside = 0f; }
            minDim = Mathf.Min(r.width, r.height) * k;
        }

        // client wave 13 (w13look), items 23 and 24: and the look may choose on that panel now (the baseline "Look select" and the panel's eye:
        // UI/LookSelect.cs); a panel it may not choose on still blocks it (RayOnUi above: nothing behind it is reached)
        // client wave 23 (w23lines), item 5: and the button is not the laser's alone (UI/UiKit.cs UiButton.LaserOnly: the tutorial's "End
        // tutorial" and "Skip step"): the look right on such a button chooses nothing (PickGazed: never whatever lies beside or under it)
        private bool Choosable(UI.UiButton b) => b != null && b.isActiveAndEnabled && b.Interactable && !b.LaserOnly && GazeChoosable != null && GazeChoosable(b) && UI.LookSelect.PanelChoosable(b.Panel);

        /// <summary>The button the gaze chooses (null: none). onUi / panel / point / range: what <see cref="RayOnUi"/> found.</summary>
        private UI.UiButton PickGazed(Ray gaze, bool onUi, Transform panel, Vector3 point, float range)
        {
            var cur = Choosable(_gazeKeep) ? _gazeKeep : null;
            if (!onUi)
            {
                // off every panel: a button at a panel's edge is kept while the gaze is within the margin of it
                if (cur != null)
                {
                    var rt = (RectTransform)cur.transform; var plane = new Plane(rt.forward, rt.position);
                    if (plane.Raycast(gaze, out float enter)) { Measure(cur, gaze.GetPoint(enter), enter, out float o, out _, out _); if (o <= GazeHysteresisDeg) return _gazeKeep = cur; }
                }
                return _gazeKeep = null;
            }
            if (cur != null && cur.Panel != panel) cur = null; // another panel is in front now
            panel.GetComponentsInChildren(false, _cand);
            UI.UiButton top = null; bool topInside = false; float topIn = 0f, topOut = float.MaxValue, topMin = 0f;
            for (int i = 0; i < _cand.Count; i++)
            {
                var b = _cand[i];
                if (!b.isActiveAndEnabled) continue;
                Measure(b, point, range, out float o, out float inn, out float md);
                if (o <= 0f)
                {
                    if (!topInside || UI.UiButton.Outranks(b, top)) { top = b; topInside = true; topIn = inn; topOut = 0f; topMin = md; }
                }
                else if (!topInside && o <= GazeToleranceDeg && o < topOut && Choosable(b)) { top = b; topOut = o; topMin = md; }
            }
            _cand.Clear();
            // right under the gaze is a button that cannot be pressed now: nothing is chosen, rather than whatever lies under it
            if (topInside && !Choosable(top)) return _gazeKeep = null;
            if (cur == null || cur == top) return _gazeKeep = top;
            Measure(cur, point, range, out float curOut, out _, out _);
            if (top == null) return _gazeKeep = curOut <= GazeToleranceDeg + GazeHysteresisDeg ? cur : null;
            // the gaze must have moved this far from the kept button towards / into the new one (less for a very small new one)
            float need = Mathf.Min(GazeHysteresisDeg, 0.3f * topMin);
            float lead = (topInside ? topIn : -topOut) + curOut;
            return _gazeKeep = lead >= need ? top : cur;
        }

        /// <summary>The pointer hand's stick this frame (0 while the wheels hold the input).</summary>
        private Vector2 PointerStick(LokiInput.Hand r)
        {
            var v = InputHeld ? Vector2.zero : r.StickValue;
            return v;
        }

        private bool PointerHeld(LokiInput.Hand r)
        {
            bool held = !InputHeld && r.TriggerValue > 0.5f;
            return held;
        }

        /// <summary>A trigger pull on the chosen button: a slider's track takes the place pointed at (the laser then drags it) or one
        /// gaze step; every other button is clicked.</summary>
        private void Press(UI.UiButton chosen, bool byLaser, Vector3 point)
        {
            var sl = chosen.Slider;
            if (sl != null && sl.Track == chosen && chosen.Interactable) { sl.PressAt(point, byLaser); if (byLaser) Dragging = sl; Audio.LokiAudio.Instance?.PlayUi(); }
            else chosen.Click();
        }

        private void UpdateDrag(LokiInput.Hand r, Vector3 origin, Vector3 dir)
        {
            if (Dragging == null) return;
            if (!PointerHeld(r) || !Dragging.isActiveAndEnabled) { Dragging.Commit(); Dragging = null; return; }
            if (Dragging.RayPoint(new Ray(origin, dir), out var p)) Dragging.DragTo(p);
        }

        private void UpdateGazeDot(Ray gaze)
        {
            bool want = ShowGazeDot != null && ShowGazeDot() && (UI.LookSelect.UiOn || UI.LookSelect.WorldOn); // (client wave 13, w13look: hidden while nothing is chosen by looking)
            if (!want) { if (GazeDot != null && GazeDot.gameObject.activeSelf) GazeDot.gameObject.SetActive(false); return; }
            if (GazeDot == null) BuildGazeDot();
            // where the ray lands: the panel it meets, else whatever of the world it meets, else 2 m out
            float dist = 2f;
            if (GazeOnPanel) dist = Vector3.Distance(gaze.origin, GazePanelPoint);
            else if (Physics.Raycast(gaze, out var hit, 50f, UI.LookSelect.GazeMask, QueryTriggerInteraction.Ignore)) dist = hit.distance;
            var at = gaze.GetPoint(Mathf.Max(0.05f, dist - 0.002f));
            if (!GazeDot.gameObject.activeSelf) GazeDot.gameObject.SetActive(true);
            GazeDot.position = at;
            GazeDot.rotation = Quaternion.LookRotation(gaze.direction, Head.transform.up);
            GazeDot.localScale = Vector3.one * (dist * GazeDotRadians); // the same size in the view at every distance
        }
        /// <summary>The dot's width as an angle: 0.6 degrees.</summary>
        public const float GazeDotRadians = 0.6f * Mathf.Deg2Rad;

        private void BuildGazeDot()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "GazeDot"; go.layer = UI.UiKit.PanelLayer; // not in the desktop spectator's view, like the panels
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            const int n = 32; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "GazeDot", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float r = new Vector2(x - (n - 1) * 0.5f, y - (n - 1) * 0.5f).magnitude / (n * 0.5f);
                    // a bright ring with a dark rim and an open middle: seen on light and on dark, and what is under it stays readable
                    byte a = (byte)(r > 1f ? 0 : r > 0.82f ? 200 : r > 0.5f ? 255 : r > 0.36f ? 200 : 0);
                    byte v = (byte)(r > 0.82f || r <= 0.5f ? 20 : 255);
                    px[y * n + x] = new Color32(v, (byte)(v == 255 ? 235 : 20), (byte)(v == 255 ? 90 : 20), a);
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            var sh = Shader.Find("Loki/UIOverlay");
            _dotMat = new Material(sh != null ? sh : Shader.Find("Sprites/Default")) { name = "LokiGazeDot", mainTexture = tex, renderQueue = LaserQueue };
            var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = _dotMat; mr.sortingOrder = LaserSortingOrder - 1;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            GazeDot = go.transform;
        }
    }
}
