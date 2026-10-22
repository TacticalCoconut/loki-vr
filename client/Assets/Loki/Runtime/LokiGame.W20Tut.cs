using System;
using System.Collections.Generic;
using Loki.Game;
using Loki.Sprites;
using Loki.UI;
using Loki.VR;
using Loki.World;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 20 (w20tut): points 42, 43 and 65 of the design's notes after wave 17 (playtest notes), with point 40's
    /// texts in LokiGame.W18Tutorial.cs.
    ///   Point 42  (design note: "when the player looks at the pouring under this exact moment during the tutorial, there needs to be a little ring that
    ///             shows up around the pouring that fills up ... and then have the pouring move to another location nearby. And repeat that one
    ///             more time. Like a total of three times.") The tutorial's look step: the filling ring (UI/LookFillRing.cs) stands round the
    ///             practice Poring; it fills while the look has locked the Poring (Game/Targeting.cs Locked) AND points within the lock's own
    ///             cone (<see cref="Targeting.ConeDegrees"/>, 11°: the lock's 1.6 × "sticky" margin does not fill it), and drains when the look
    ///             leaves. Full: a check mark on the Poring, which then walks (Sprites/Unit.cs WalkTo, its own pace) to a walkable cell
    ///             <see cref="W20HopNear"/>-<see cref="W20HopFar"/> cells from the character and at least <see cref="W20HopApart"/> cells from
    ///             where it stood, preferably within <see cref="W20HopViewDeg"/>° of where the head faces (so it stays in view and the look must
    ///             move to follow it); the ring waits until it has arrived. <see cref="W20LookRounds"/> times, then the step is done.
    ///   Point 43  (design note: "just do a little check mark to show that, yeah, that's exactly what you had to do. In fact, do that check mark effect
    ///             every time that the player completes a specific section.") EVERY part of every tutorial step the player completes pops a
    ///             check mark (UI/CheckMarkFx.cs: green disc, white tick, a soft tick sound, a rumble tick) where the thing was done (the Poring,
    ///             a calibration dot) or, for the rest, along the look <see cref="W20CheckDistance"/> m out: so looking up at the overhead
    ///             menu gets its check up there, the moment the menu wakes. A passed or skipped step gets none.
    ///   Point 65  (design note: "Before the tutorial plays ... a calibration sequence for the eyesight choosing system"; "Stored per PC") the look
    ///             calibration (UI/LookCalibrator.cs runs it; VR/LookCalibration.cs keeps it): the tutorial's step after the welcome (passed
    ///             when this PC already has one), and Settings → Comfort → Look select "Recalibrate look" (it closes Settings and runs on the
    ///             tutorial's guide, headed "Look calibration", with Cancel / Close; refused while the tutorial runs: "End the tutorial
    ///             first."). Meanwhile the look ring (the gaze dot) is hidden (so the player looks with the eyes, not by putting Loki's ring
    ///             on the dot) and the guide stands below the dots (TutorialGuide.SetDrop); after it the look ring shows for
    ///             <see cref="W20RingAfter"/> s where the look now lands.
    /// Hooks (marked "client wave 20 (w20tut)"): LokiGame.cs (InitW20Tut after InitW11Menu, UpdateW20Tut after UpdateW18Tut, the smoke's
    /// dispatch), LokiGame.W18Tutorial.cs (the steps, the check after each part, the guide's buttons outside the tutorial), UI/ComfortWindows.cs
    /// (the "Recalibrate look" row), UI/SettingHelp.cs (its help), VR/DioramaRig.Ui.cs (GazeRay through the calibration), World/UserSettings.cs
    /// (the per-PC fields), Audio/LokiAudio.cs (PlayClip).
    /// </summary>
    public sealed partial class LokiGame
    {
        public const int W20LookRounds = 3, W20HopNear = 3, W20HopFar = 5, W20HopApart = 4;
        public const float W20HopViewDeg = 40f, W20CheckDistance = 0.75f, W20RingAfter = 3f, W20GuideGapDeg = 2f;

        public LookCalibrator W20Cal { get; private set; }
        private LookFillRing _w20PoringRing;
        private int _w20LookFills; private float _w20LookFill;
        private bool _w20CalStandalone; private float _w20CalCloseAt = -1f, _w20RingUntil = -1f;
        /// <summary>Check marks the tutorial popped ("key:part"), Poring hops, the Poring ring's fill now, look rounds done, recalibrations
        /// started from Settings, refusals (smoke, log).</summary>
        public int W20TutChecks { get; private set; }
        public readonly List<string> W20CheckLog = new List<string>();
        public int W20PoringHops { get; private set; }
        public float W20PoringFill => _w20LookFill;
        public LookFillRing W20PoringRing => _w20PoringRing;
        public int W20LookFills => _w20LookFills;
        public int W20Recalibrations { get; private set; }
        public int W20RecalibrateRefusals { get; private set; }
        public bool W20CalStandalone => _w20CalStandalone;

        private void InitW20Tut(LokiBootstrap boot)
        {
            var rig = boot.Rig; if (rig == null) return;
            W20Cal = LookCalibrator.Create(rig.transform, () => rig.Head, () => rig.GazeRaySliders);
            W20Cal.DotFinished += i => { if (_w20CalStandalone) CheckMarkFx.Pop(rig.transform, () => rig.Head, W20Cal.LastDotAt); };
            W20Cal.Finished += r => { _w20RingUntil = Time.unscaledTime + W20RingAfter; if (_w20CalStandalone) _w20CalCloseAt = Time.unscaledTime + 4f; };
            // the look ring hidden during a calibration, and shown for a moment after it (VR/DioramaRig.Ui.cs UpdateGazeDot)
            var shown = rig.ShowGazeDot;
            rig.ShowGazeDot = () => !W20Cal.Running && (Time.unscaledTime < _w20RingUntil || (shown != null && shown()));
            if (ComfortUi != null) ComfortUi.Recalibrate = () => W20Recalibrate("Settings → Comfort → Look select");
            Debug.Log($"[Look] this PC's look calibration: {(LookCalibration.Done ? $"{LookCalibration.Yaw:+0.00;-0.00;0.00}° sideways, {LookCalibration.Pitch:+0.00;-0.00;0.00}° up / down ({UserSettings.Prefs.lookCalDots} dots, {UserSettings.Prefs.lookCalAt})" : "none")}");
        }

        /// <summary>Once a frame (LokiGame.cs Update, after the tutorial): the calibration started from Settings, on the guide.</summary>
        private void UpdateW20Tut()
        {
            UpdateW23Place(); // client wave 23 (w23place): the practice Poring's light and hum, the guide beside / under / fading for what the player works on (LokiGame.W23Place.cs; after the tutorial drew its guide)
            UpdateW25Tut(); // client wave 25 (w25tut): the arrow's and the marks' state, the guide beside the menu above, the bubbles' sliders (LokiGame.W25Tut.cs)
            if (W20Cal != null && W20Cal.Running) W20KeepGuideBelowDots();
            if (!_w20CalStandalone || W18TGuide == null) return;
            if (W18TutRunning) { W20CalStandaloneEnd("the tutorial started"); return; }
            float now = Time.unscaledTime; var c = W20Cal; int n = LookCalibrator.Dots.Length;
            bool fin = c.State == LookCalibrator.Stage.Finished;
            string nowText = fin ? Lang.T("Done! Your look is calibrated.") : Lang.F("Look at dot {0} of {1} — slow down and hold still to calibrate.", Mathf.Min(c.DotsDone + 1, n), n);
            W18TGuide.Show(Lang.T("Look calibration"), n > 0 ? c.DotsDone / (float)n : 1f, Lang.T("Calibrate your look"), W20CalAbout(), nowText,
                fin ? W20CalResultText() : W20CalHow(), fin, null, fin ? Lang.T("Close") : Lang.T("Cancel"));
            W23GuideLaserOnly(false, false); // (client wave 23, w23lines, item 5: outside the tutorial the guide's button is "Cancel" / "Close": the look's too)
            W18TGuide.Tick(Time.unscaledDeltaTime);
            if (fin && _w20CalCloseAt > 0f && now >= _w20CalCloseAt) W20CalStandaloneEnd("finished");
        }

        // ------------------------------------------------------------------ point 65: the look calibration
        private string W20CalAbout() => Lang.T("Loki can tell what you're looking at. Slow down and hold your head still on each dot to calibrate.");

        /// <summary>The status line while a dot waits: what the player should do now.</summary>
        private string W20CalHow()
        {
            var c = W20Cal; if (c == null) return "";
            switch (c.State)
            {
                case LookCalibrator.Stage.Wait:
                    if (c.HeadSpeed > LookCalibrator.StillDegPerSec)
                        return c.LookOff > LookCalibrator.NearDeg ? Lang.T("Look at the dot and slow down to calibrate.") : Lang.T("Slow down and hold still to calibrate.");
                    return c.LookOff > LookCalibrator.NearDeg ? Lang.T("Look at the dot.") : Lang.T("Slow down and hold still to calibrate.");
                case LookCalibrator.Stage.Settle: return Lang.T("Slow down and hold still to calibrate…");
                case LookCalibrator.Stage.Fill: return Lang.T("Hold still — calibrating…");
                case LookCalibrator.Stage.Between: return Lang.T("Next dot…");
                default: return "";
            }
        }

        /// <summary>The correction found, in words ("Calibrated. Correction: 2.5° left and 1° up.").</summary>
        private string W20CalResultText() => Lang.T("Done! Your look is calibrated."); // (client wave 23: the correction's angles are the maker's business, not the player's)

        /// <summary>The tutorial's calibration step (LokiGame.W18Tutorial.cs W18TBuildSteps): passed when this PC has a calibration; one part per dot.</summary>
        private void W20CalStep(W18TStep st)
        {
            int n = LookCalibrator.Dots.Length;
            st.About = W20CalAbout;
            st.Unavailable = () => W20Cal == null ? "" : LookCalibration.Done ? Lang.T("Already calibrated. Skipped.") : null;
            st.Enter = () => W20CalBegin("the tutorial");
            st.Leave = () => W20CalEnd();
            st.CheckAt = () => W20Cal != null && W20Cal.DotsDone > 0 ? W20Cal.LastDotAt : (Vector3?)null;
            st.DoneText = W20CalResultText;
            for (int k = 1; k <= n; k++)
            {
                int kk = k;
                st.Phases.Add(W18TP(() => Lang.F("Look at dot {0} of {1} — slow down and hold still to calibrate.", kk, n), () => W20Cal != null && W20Cal.DotsDone >= kk, W20CalHow));
            }
        }

        private void W20CalBegin(string why)
        {
            if (W20Cal == null) return;
            W20Cal.Begin();
            W20KeepGuideBelowDots();
            Debug.Log($"[Look] calibration ({why}): the look ring hidden, the guide below the dots");
        }

        private void W20CalEnd()
        {
            if (W20Cal != null && W20Cal.Running) W20Cal.Cancel();
            if (W20Cal != null) W20Cal.HideDots(); // (client wave 23, w23place: a finished run's dots stay as their checks until the step is left)
            if (W18TGuide != null && W18TGuide.Drop > 0f) W18TGuide.SetDrop(0f);
        }

        /// <summary>The guide's top <see cref="W20GuideGapDeg"/>° under the lowest dot's ring (its middle that far down plus half its height).</summary>
        private void W20KeepGuideBelowDots()
        {
            if (W18TGuide == null) return;
            float half = Mathf.Atan(W18TGuide.SizePx.y * TutorialGuide.MetresPerPx * 0.5f / TutorialGuide.Distance) * Mathf.Rad2Deg;
            float drop = LookCalibrator.UpDeg + LookCalibrator.RingDeg + W20GuideGapDeg + half;
            if (Mathf.Abs(drop - W18TGuide.Drop) > 0.5f) W18TGuide.SetDrop(drop);
        }

        /// <summary>Settings → Comfort → Look select "Recalibrate look": the calibration on its own (never during the tutorial).</summary>
        public void W20Recalibrate(string why)
        {
            if (W18TutRunning) { W20RecalibrateRefusals++; Hud?.Notify(Lang.T("End the tutorial first."), 3f); Debug.Log($"[Look] recalibration refused ({why}): the tutorial runs"); return; }
            if (W20Cal == null || W18TGuide == null) return;
            _w20CalStandalone = true; _w20CalCloseAt = -1f; W20Recalibrations++;
            W18TGuide.Hide();
            W20CalBegin(why);
        }

        private void W20CalStandaloneEnd(string why)
        {
            if (!_w20CalStandalone) return;
            _w20CalStandalone = false; _w20CalCloseAt = -1f;
            W20CalEnd();
            if (!W18TutRunning && W18TGuide != null) W18TGuide.Hide();
            Debug.Log($"[Look] the calibration from Settings ended ({why})");
        }

        /// <summary>The guide's buttons outside the tutorial (LokiGame.W18Tutorial.cs W18TutLeft / W18TutRight): the calibration's Cancel / Close.</summary>
        private void W20CalGuideLeft() { }
        private void W20CalGuideRight() { if (_w20CalStandalone) W20CalStandaloneEnd(W20Cal != null && W20Cal.Running ? "cancelled: nothing kept" : "closed"); }

        // ------------------------------------------------------------------ point 42: the look step's filling ring and the Poring's hops
        private void W20LookStep(W18TStep st)
        {
            st.Enter = () => { W18TDummyEnsure(); _w20LookFills = 0; _w20LookFill = 0f; };
            st.Tick = W20LookTick;
            st.Leave = () => { if (_w20PoringRing != null) _w20PoringRing.Hide(); _w20LookFill = 0f; };
            st.CheckAt = W20DummyTop;
            for (int k = 1; k <= W20LookRounds; k++)
            {
                int kk = k;
                st.Phases.Add(W25Arrow(W18TP(() => Lang.T("Look at the Poring until its ring fills."), () => _w20LookFills >= kk,
                    () => _w18tDummy != null && _w18tDummy.Walking ? Lang.T("It hopped. Look at it again.") : (string)null), W25LookOnPoring)); // (client wave 25, w25tut, item 2: the green arrow over the Poring until the look is on it, in every round)
            }
        }

        private void W20LookTick(float dt)
        {
            W18TDummyEnsure();
            var u = _w18tDummy; var rig = _boot.Rig;
            if (u == null || rig == null) { if (_w20PoringRing != null) _w20PoringRing.Hide(); return; }
            if (_w20PoringRing == null) _w20PoringRing = LookFillRing.Create(rig.transform, () => rig.Head, "PoringLookRing");
            if (u.Walking) { _w20LookFill = 0f; _w20PoringRing.Fill = 0f; _w20PoringRing.Hide(); return; } // (it hops: the ring waits for it)
            var c = W20DummyCentre(u); var ray = rig.GazeRay;
            float cone = Target != null ? Target.ConeDegrees : 11f;
            bool looked = Target != null && Target.Locked != null && Target.Locked.Unit == u && Vector3.Angle(ray.direction, c - ray.origin) <= cone;
            _w20LookFill = Mathf.Clamp01(_w20LookFill + (looked ? dt / LookFillRing.FillSeconds : -dt / LookFillRing.DrainSeconds));
            _w20PoringRing.Fill = _w20LookFill; _w20PoringRing.Show(c, W20DummyRadius(u));
            if (_w20LookFill < 1f) return;
            _w20LookFills++; _w20LookFill = 0f; _w20PoringRing.Fill = 0f;
            Debug.Log($"[Tutorial] the Poring's ring is full ({_w20LookFills} of {W20LookRounds}) at {u.Cell}");
            if (_w20LookFills < W20LookRounds) W20PoringHop(u);
        }

        private static Vector3 W20DummyCentre(Unit u) { var col = u.GetComponent<Collider>(); return col != null ? col.bounds.center : u.transform.position; }
        private static float W20DummyRadius(Unit u) { var col = u.GetComponent<Collider>(); return col != null ? Mathf.Max(0.02f, Mathf.Max(col.bounds.extents.x, col.bounds.extents.y) * 1.25f) : 0.05f; }
        /// <summary>Just above the practice Poring (where its check mark pops); null: no Poring.</summary>
        private Vector3? W20DummyTop()
        {
            var u = _w18tDummy; if (u == null) return null;
            var col = u.GetComponent<Collider>();
            return col != null ? col.bounds.center + Vector3.up * (col.bounds.extents.y + 0.03f) : u.transform.position + Vector3.up * 0.08f;
        }

        private Vector2Int _w20PrevCell = new Vector2Int(-999, -999);
        /// <summary>The Poring walks to a new cell near the character (see the class's notes for the rules).</summary>
        private void W20PoringHop(Unit u)
        {
            var self = Entities != null ? Entities.Self : null; var gat = _boot.Map.Current != null ? _boot.Map.Current.Gat : null;
            if (self == null || gat == null) return;
            var map = _boot.Map.transform; float y = self.transform.localPosition.y;
            int sx = Mathf.RoundToInt(self.Cell.x), sy = Mathf.RoundToInt(self.Cell.y), ox = Mathf.RoundToInt(u.Cell.x), oy = Mathf.RoundToInt(u.Cell.y);
            if (W24DaisHop(u, ox, oy)) return; // client wave 24 (w24), item 5: on the tutorial's own map the hops walk a large triangle around the dais
            // client wave 23 (w23place), the design's answer 38: the hops stay where the Poring is placed (LokiGame.W23Place.cs): 20 to 35° below
            // eye level, within W23PoringBearingMax° of where the head faces, W20HopApart to W23PoringHopMaxCells cells from where it
            // stood. None there (a narrow floor, first person's large cells): the same band up to twice as far to the sides, 2 cells
            // apart. None either: wave 20's own rule (W20HopNear - W20HopFar cells from the character), which may be at the feet
            var inView = new List<Vector2Int>(); var wide = new List<Vector2Int>(); var any = new List<Vector2Int>();
            for (int dx = -W23PoringSearchCells; dx <= W23PoringSearchCells; dx++)
                for (int dy = -W23PoringSearchCells; dy <= W23PoringSearchCells; dy++)
                {
                    int r = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)); if (r < 2) continue;
                    int x = sx + dx, yy = sy + dy;
                    if (!gat.InBounds(x, yy) || !gat.Get(x, yy).Walkable) continue;
                    if (Room != null && !Room.Visible(new Vector2(x, yy))) continue; // client wave 20 merge (point 54): never into another room of an indoor map (not drawn there, no collider: the look could not find it)
                    int apart = Mathf.Max(Mathf.Abs(x - ox), Mathf.Abs(yy - oy));
                    int prevApart = Mathf.Max(Mathf.Abs(x - _w20PrevCell.x), Mathf.Abs(yy - _w20PrevCell.y));
                    if (r >= W20HopNear && r <= W20HopFar && apart >= W20HopApart && prevApart >= 2) any.Add(new Vector2Int(x, yy));
                    if (!W23Angles(map.TransformPoint(new Vector3(x + 0.5f, y, yy + 0.5f)), out float down, out float bearing)) continue;
                    if (down < W23PoringDownMin || down > W23PoringDownMax) continue;
                    if (apart >= W20HopApart && prevApart >= W20HopApart && apart <= W23PoringHopMaxCells && Mathf.Abs(bearing) <= W23PoringBearingMax) inView.Add(new Vector2Int(x, yy));
                    else if (apart >= 2 && prevApart >= 2 && Mathf.Abs(bearing) <= 2f * W23PoringBearingMax) wide.Add(new Vector2Int(x, yy));
                }
            var pick = inView.Count > 0 ? inView : wide.Count > 0 ? wide : any;
            if (pick.Count == 0) { Debug.Log($"[Tutorial] the Poring has no cell to hop to from {ox},{oy}: it stays"); return; }
            var to = pick[UnityEngine.Random.Range(0, pick.Count)];
            _w20PrevCell = new Vector2Int(ox, oy);
            u.WalkTo(ox, oy, to.x, to.y);
            W20PoringHops++;
            Debug.Log($"[Tutorial] the Poring hops {ox},{oy} → {to.x},{to.y} ({(inView.Count > 0 ? "ahead, 20 to 35° below eye level" : wide.Count > 0 ? "in the band, further to the side: none was ahead" : "near the character: no cell of the band was free")}; {inView.Count} / {wide.Count} / {any.Count} cells could take it)");
        }

        // ------------------------------------------------------------------ point 43: the check mark after every part done
        private void W20TutCheck(W18TStep st, int part)
        {
            var rig = _boot.Rig; if (rig == null) return;
            Vector3? at = null; try { at = st.CheckAt?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            var look = rig.GazeRay;
            if (!at.HasValue) at = look.GetPoint(W20CheckDistance);
            CheckMarkFx.Pop(rig.transform, () => rig.Head, at.Value);
            W20TutChecks++; W20CheckLog.Add(st.Key + ":" + (part + 1));
            Debug.Log($"[Tutorial] check mark: '{st.Key}' part {part + 1} at {Vector3.Distance(rig.Head.transform.position, at.Value):0.00} m, {Vector3.Angle(look.direction, at.Value - look.origin):0.0}° from the look");
        }
    }
}
