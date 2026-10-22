using System;
using System.Collections.Generic;
using Loki.Game;
using Loki.Net;
using Loki.UI;
using Loki.VR;
using UnityEngine;
using UnityEngine.UI;

namespace Loki
{
    /// <summary>
    /// Client wave 13 (w13view), deploy step 2: the overhead menu's three new controls and what they switch (design notes
    /// items 6, 10 and 13; item 3, the wand wheel in front of the eyes, is UI/WandWheel.cs + LokiGame.W12Wand.cs).
    ///   Reposition    (item 6 as items 10 and 15 and the design's word of ~14:25 left it: "Yes, both too"; the pads are not in it: item 15
    ///                 opens the pad at the hand) "Reposition: off / on". On: a plain grip takes, carries and leaves where it is let
    ///                 go: the see-through minimap (while it is shown), the wand wheel's picture in front of the eyes (shown for it
    ///                 while the wand is the casting gesture and "Wand wheel shown: in front of you"), the wand's holster marker
    ///                 (the wand scheme; the Settings row "Wand holster: Place…" stays) and the elbow button's place along the
    ///                 forearm (the bar in "elbow button" mode). Taken by the hand at it (its grip point within reach of it), or, for
    ///                 the two pictures in front of the eyes (out of an arm's reach by default), by the pointing hand's laser on it
    ///                 and that hand's grip: it then rides the laser at the distance it was taken, as a board does in resize mode.
    ///                 The other hand's grip meanwhile resizes the two pictures (the hands apart / together; VR/Comfort.cs
    ///                 PlaceMinScale..PlaceMaxScale). Let go: kept per player (loki-user.json, the head's frame for the pictures).
    ///                 "Reset" (beside the switch while it is on) puts all four back. Starting Reposition ends resize mode and
    ///                 starting resize mode ends Reposition; a shake of the pointing controller ends it (LokiGame.W13Resize.cs);
    ///                 meanwhile the new casting is inert (W13CastingAllowed) and a wand in a hand goes back to the hip.
    ///   Minimap       (item 10) "Minimap: off / on" and, while it is on, "-", "+" and "World map" beside it. On: the minimap is a
    ///                 see-through display in front of the eyes, head-locked (UI/Minimap.cs). Its state, zoom and place are kept per
    ///                 player. No controller button opens it any more (the menu took its button: UI/LokiHud.cs).
    ///   Forearm bar   (item 13) "Forearm bar: on / off / elbow" (the Settings row is the same stored value: VR/Comfort.cs
    ///                 QuickBarMode). Elbow: a small button at the elbow of the arm that carries the bar (the walking hand's; the
    ///                 right one in left-handed mode), estimated from that hand's pose: <see cref="ElbowBack"/> m back from the grip's
    ///                 middle along the forearm (the hand's pointing direction: OpenXR's aim pose when the runtime gives one, else the
    ///                 grip pose's forward; a tracked hand: <see cref="ElbowBackHand"/> m back from its palm along its forward), lifted
    ///                 <see cref="ElbowLift"/> m off the arm along the hand's own up: a fixed offset in the controller's frame, so it
    ///                 stays put on the arm as the arm moves. Touching it with the other hand (its controller's tip, or a tracked
    ///                 hand's index fingertip) within <see cref="ElbowTouch"/> m shows / hides the bar once: a tick, then nothing
    ///                 until that tip has gone <see cref="ElbowRearm"/> m away and <see cref="ElbowLockout"/> s have passed; the laser
    ///                 or look + trigger press it like any button. It works with menus open (it is not a casting system); in
    ///                 Reposition the touch does not toggle (the grip carries it along the arm instead).
    /// </summary>
    public sealed partial class LokiGame
    {
        public const float ElbowBack = 0.33f, ElbowBackHand = 0.30f, ElbowLift = 0.04f, ElbowTouch = 0.035f, ElbowRearm = 0.055f, ElbowLockout = 0.4f, ElbowScale = 0.0003f, ElbowPx = 140f;
        /// <summary>A hand takes a picture in front of the eyes when its grip point is within this of the picture's plane and inside
        /// its outline grown by <see cref="PlaceMargin"/>; the holster and the elbow button within <see cref="PlaceReach"/> of them.</summary>
        public const float PlaceDepth = 0.10f, PlaceMargin = 0.04f, PlaceReach = 0.07f;

        public bool W13Reposition { get; private set; }
        /// <summary>Reposition: modes started; things taken; resizes begun; ticks of a hand or the laser coming onto one (smoke, log).</summary>
        public int W13RepositionStarts { get; private set; }
        public int W13PlaceGrabs { get; private set; }
        public int W13PlaceResizes { get; private set; }
        public int W13PlaceTicks { get; private set; }
        /// <summary>The elbow button: toggles by touch, by its button (laser / look); the bar shown / hidden since (smoke, log).</summary>
        public int W13ElbowTouches { get; private set; }
        public int W13ElbowPresses { get; private set; }

        private sealed class Placeable
        {
            public string Name; public Func<bool> Shown; public Func<Vector3> Centre; public Func<Vector3, bool> Near; public Func<Ray, float> Laser;
            public Func<float> Factor; public bool Resizable; public Action<Vector3, float> Carry; public Action Release; public Action<bool> Look;
            public int By = -1; public bool ByLaser; public Vector3 Offset; public float Dist; public bool Resizing; public float Hands0, Factor0;
            /// <summary>Client wave 18 (w18flick): which hands (0 left, 1 right) may take it, by grip or (the pointing hand) laser; null: both.</summary>
            public Func<int, bool> Hand;
            /// <summary>Client wave 21 (w21wand): its size factor's limits (Reposition's own by default; the wand's: VR/Comfort.cs WandMinScale /
            /// WandMaxScale), and whether it is RESIZE MODE's thing (the wand itself: LokiGame.W21Wand.cs) and not Reposition's: each mode's
            /// grips take only its own things (<see cref="W13PlaceUsable"/>).</summary>
            public float Min = Comfort.PlaceMinScale, Max = Comfort.PlaceMaxScale; public bool InResize;
            /// <summary>Client wave 21 (w21wand): which hand's grip may resize it while the other hand holds it; null: either.</summary>
            public Func<int, bool> Resizer;
            public readonly bool[] NearWas = new bool[2]; public bool LaserWas, LitWas;
        }
        private readonly List<Placeable> _w13Places = new List<Placeable>();
        private readonly bool[] _w13GripWas = new bool[2]; private readonly int[] _w13Holds = { -1, -1 };
        private bool _w13MapWant;
        private RectTransform _w13Elbow; private UiButton _w13ElbowBtn; private Image _w13ElbowBg; private Text _w13ElbowGlyph;
        private bool _w13ElbowArmed = true, _w13ElbowLit; private float _w13ElbowLock;
        /// <summary>What a hand carries in Reposition now (null: nothing).</summary>
        public string W13Carried { get { foreach (var p in _w13Places) if (p.By >= 0 && !p.InResize) return p.Name; return null; } } // (client wave 21, w21wand: Reposition's things only: the wand sized in resize mode is LokiGame.W21Wand.cs W21WandSizing)
        public bool W13Resizing { get { foreach (var p in _w13Places) if (p.By >= 0 && p.Resizing && !p.InResize) return true; return false; } }
        /// <summary>The elbow button (null before the game starts) and whether it is shown now.</summary>
        public Transform W13ElbowButton => _w13Elbow;
        public bool W13ElbowShown => _w13Elbow != null && _w13Elbow.gameObject.activeSelf;

        /// <summary>Called by InitW13Resize after its two controls: the overhead menu's three new rows, the elbow button, what
        /// Reposition can take.</summary>
        private void InitW13View(LokiBootstrap boot, OverheadMenu m)
        {
            var rig = boot.Rig;
            m.AddSwitch("reposition", () => Lang.T("Reposition"), 2, () => W13Reposition ? 1 : 0, k => W13SetReposition(k == 1, "the overhead menu"),
                k => k == 0 ? Lang.T("~switch~Off") : Lang.T("~switch~On"));
            m.AddExtra("reset", () => Lang.T("~place~Reset"), 160f, () => W13ResetPlaces("the overhead menu"), () => W13Reposition);
            m.AddSwitch("minimap", () => Lang.T("Minimap"), 2, () => Comfort.MinimapOn ? 1 : 0, k => W13SetMinimap(k == 1, "the overhead menu"),
                k => k == 0 ? Lang.T("~switch~Off") : Lang.T("~switch~On"));
            m.AddExtra("out", () => "−", 80f, () => W13MinimapZoom(-1), () => Comfort.MinimapOn, 72).Tip = () => Lang.T("Zoom out"); // (client wave 14, point 7: their tips)
            m.AddExtra("in", () => "+", 80f, () => W13MinimapZoom(1), () => Comfort.MinimapOn, 72).Tip = () => Lang.T("Zoom in");
            m.AddExtra("world", () => Lang.T("World map"), 190f, () => { Debug.Log("[Minimap] the world map, from the overhead menu"); MiniMap?.OpenWorldMap?.Invoke(); }, () => Comfort.MinimapOn);
            m.AddSwitch("bar", () => Lang.T("Forearm bar"), 3, () => Comfort.QuickBarMode, k => W13SetBarMode(k, "the overhead menu"),
                k => k == 0 ? Lang.T("~bar~On") : k == 1 ? Lang.T("~bar~Off") : Lang.T("~bar~Elbow"));
            if (MiniMap != null) MiniMap.ZoomStep = Comfort.MinimapZoom;
            if (Hud != null && Hud.Wrist != null) Hud.Wrist.ToggleMinimap = () => W13SetMinimap(!(MiniMap != null && MiniMap.Visible), "the menu's View page");
            // the brief's eighteenth item: the freed button (X / left A): held, resize mode through the one toggle the overhead menu and
            // the shake use; pressed, the microphone (the design's answer of ~15:05)
            if (Hud != null) { Hud.FreeHold = () => W13SetResizeMode(!LokiInput.ResizeMode, "a hold of the freed button"); Hud.FreePress = () => W13ToggleMic("a press of the freed button"); }
            BuildW13MicMark(rig);
            // the elbow button: a small HUD panel of its own (UI/UiKit.cs PanelKinds: "ElbowButton" never switches the casting off)
            _w13Elbow = UiKit.Panel("ElbowButton", rig.transform, new Vector2(ElbowPx, ElbowPx), ElbowScale, false);
            _w13ElbowBtn = UiKit.Button(_w13Elbow, "≡", Vector2.zero, new Vector2(ElbowPx, ElbowPx), () => { W13ElbowPresses++; W13ToggleElbowBar("its button (laser or look)"); }, 76);
            _w13ElbowBg = _w13ElbowBtn.GetComponent<Image>(); _w13ElbowGlyph = _w13ElbowBtn.Text;
            UiKit.Box(_w13Elbow, "ring", Vector2.zero, new Vector2(ElbowPx + 10, ElbowPx + 10), HudLook.ButtonEdge(0.85f)).SetAsFirstSibling(); // (client wave 21, w21tools: the theme's dark gold edge; black before)
            _w13Elbow.gameObject.SetActive(false);
            BuildW19ChatButton(rig); // client wave 19 (w19view41), point 41: the chat button beside it
            // what Reposition can take
            _w13Places.Add(new Placeable
            {
                Name = "the minimap", Shown = () => MiniMap != null && MiniMap.Visible && !MiniMap.AtHand, Centre = () => MiniMap.transform.position,
                Near = p => W13InRect(MiniMap.FrameWorld, p, PlaceDepth, PlaceMargin), Laser = ray => W13RayRect(MiniMap.FrameWorld, ray), Factor = () => MiniMap.SizeFactor, Resizable = true,
                Carry = (w, f) => MiniMap.CarryTo(w, f), Look = lit => MiniMap.SetPlacingLook(lit),
                Release = () => { Comfort.MinimapPlaced = true; Comfort.MinimapOffset = MiniMap.PlaceLocal; Comfort.MinimapScale = MiniMap.SizeFactor; Comfort.Save(); Debug.Log($"[Place] the minimap: {W13Cm(MiniMap.PlaceLocal)} from the eyes ({W13Deg(MiniMap.PlaceLocal)}), size x{MiniMap.SizeFactor:0.00}"); },
            });
            _w13Places.Add(new Placeable
            {
                Name = "the wand wheel's picture", Shown = () => W12Wheel != null && W12Wheel.Placing, Centre = () => W12Wheel.transform.position,
                Near = p => W13InDisc(W12Wheel.transform, WandWheelGeo.OuterEnd * W12Wheel.FrontFactor, p, PlaceDepth, PlaceMargin), Laser = ray => W13RayDisc(W12Wheel.transform, WandWheelGeo.OuterEnd * W12Wheel.FrontFactor, ray),
                Factor = () => W12Wheel.FrontRelative, Resizable = true, Carry = (w, f) => W12Wheel.CarryFront(w, f), Look = lit => W12Wheel.SetPlacingLook(lit),
                Release = () => { Comfort.WheelPlaced = true; Comfort.WheelOffset = W12Wheel.FrontPlace; Comfort.WheelScale = W12Wheel.FrontRelative; Comfort.Save(); Debug.Log($"[Place] the wand wheel's picture: {W13Cm(W12Wheel.FrontPlace)} from the eyes ({W13Deg(W12Wheel.FrontPlace)}), size x{W12Wheel.FrontRelative:0.00} of its default ({W12Wheel.FrontFactor:0.00} of life size)"); },
            });
            _w13Places.Add(new Placeable
            {
                // client wave 14 (w14cast), point 32 ("Add a resize option for the wand too"): the other hand's grip resizes the wand (and
                // its holster marker) as it does the pictures; the size is Settings → Controls "Wand size" too (VR/Comfort.cs WandScale)
                Name = "the wand's holster", Shown = () => W13Reposition && W12Wand != null && W12Wand.MarkerShown, Centre = () => W12Wand.HolsterPos,
                Near = p => Vector3.Distance(p, W12Wand.HolsterPos) <= WandHolster.Radius, Factor = () => Comfort.WandScale, Resizable = true,
                Min = Comfort.WandMinScale, Max = Comfort.WandMaxScale, // client wave 21 (w21wand): the wand's own limits (the fingertip .. the default)
                Carry = (w, f) => { W12Wand.PlaceAt(w); Comfort.WandScale = Mathf.Clamp(f, Comfort.WandMinScale, Comfort.WandMaxScale); },
                Release = () => { Comfort.Save(); Debug.Log($"[Place] the wand's holster: offset {Comfort.WandOffset.x:0.00}, {Comfort.WandOffset.y:0.00}, {Comfort.WandOffset.z:0.00} m from the body anchor, the wand's size x{Comfort.WandScale:0.00}"); },
            });
            _w13Places.Add(new Placeable
            {
                Name = "the elbow button", Shown = () => W13ElbowShown, Centre = () => _w13Elbow.position,
                Near = p => Vector3.Distance(p, _w13Elbow.position) <= PlaceReach, Factor = () => 1f, Carry = (w, f) => W13SlideElbow(w),
                Release = () => { Comfort.Save(); Debug.Log($"[Place] the elbow button: {Comfort.ElbowOffset * 100f:0.0} cm along the forearm from the estimated elbow"); },
            });
            _w13Places.Add(new Placeable
            {
                // client wave 19 (w19view41), point 41: the chat button rides beside the elbow button: either slides both along the forearm
                Name = "the chat button", Shown = () => W19ChatButtonShown, Centre = () => _w19Chat.position,
                Near = p => Vector3.Distance(p, _w19Chat.position) <= PlaceReach, Factor = () => 1f, Carry = (w, f) => W13SlideElbow(w - W13ElbowFrame().axis * ChatGap),
                Release = () => { Comfort.Save(); Debug.Log($"[Place] the chat button (and the elbow button): {Comfort.ElbowOffset * 100f:0.0} cm along the forearm from the estimated elbow"); },
            });
            _w13Places.Add(new Placeable
            {
                Name = "the Horde proc counters",
                Shown = () => Loki.Horde.Client.HordePassivesDraw.ActiveInstance != null && Loki.Horde.Client.HordePassivesDraw.ActiveInstance.OverheadVisible,
                Centre = () => Loki.Horde.Client.HordePassivesDraw.ActiveInstance.OverheadPosition,
                Near = p => W13InRect(Loki.Horde.Client.HordePassivesDraw.ActiveInstance.OverheadFrameWorld, p, PlaceDepth, PlaceMargin),
                Laser = ray => W13RayRect(Loki.Horde.Client.HordePassivesDraw.ActiveInstance.OverheadFrameWorld, ray),
                Factor = () => Loki.Horde.Client.HordePassivesDraw.OverheadScale,
                Resizable = true,
                Carry = (w, f) => Loki.Horde.Client.HordePassivesDraw.ActiveInstance.CarryOverheadTo(w, f),
                Look = lit => Loki.Horde.Client.HordePassivesDraw.ActiveInstance?.SetOverheadPlacingLook(lit),
                Release = () => { Debug.Log($"[Place] the Horde proc counters: offset {Loki.Horde.Client.HordePassivesDraw.OverheadLocalOffset}, size x{Loki.Horde.Client.HordePassivesDraw.OverheadScale:0.00}"); },
            });
        }

        private static string W13Cm(Vector3 v) => $"{v.x * 100f:0.0} / {v.y * 100f:0.0} / {v.z * 100f:0.0} cm right / up / ahead";
        private static string W13Deg(Vector3 v) => $"{Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg:0.0} deg right, {Mathf.Atan2(v.y, new Vector2(v.x, v.z).magnitude) * Mathf.Rad2Deg:0.0} deg up, {v.magnitude * 100f:0} cm away";

        // ------------------------------------------------------------------ geometry of the pictures in front of the eyes
        private static bool W13InRect((Vector3 c, Vector3 r, Vector3 u) rect, Vector3 p, float depth, float margin)
        {
            var d = p - rect.c; float rl = rect.r.magnitude, ul = rect.u.magnitude; if (rl < 1e-5f || ul < 1e-5f) return false;
            var n = Vector3.Cross(rect.r, rect.u).normalized;
            return Mathf.Abs(Vector3.Dot(d, n)) <= depth && Mathf.Abs(Vector3.Dot(d, rect.r / rl)) <= rl + margin && Mathf.Abs(Vector3.Dot(d, rect.u / ul)) <= ul + margin;
        }
        /// <summary>The distance along the ray to the rectangle (its outline grown by 2 cm), or -1.</summary>
        private static float W13RayRect((Vector3 c, Vector3 r, Vector3 u) rect, Ray ray)
        {
            float rl = rect.r.magnitude, ul = rect.u.magnitude; if (rl < 1e-5f || ul < 1e-5f) return -1f;
            var plane = new Plane(Vector3.Cross(rect.r, rect.u).normalized, rect.c);
            if (!plane.Raycast(ray, out float t) || t <= 0f) return -1f;
            var d = ray.GetPoint(t) - rect.c;
            return Mathf.Abs(Vector3.Dot(d, rect.r / rl)) <= rl + 0.02f && Mathf.Abs(Vector3.Dot(d, rect.u / ul)) <= ul + 0.02f ? t : -1f;
        }
        private static bool W13InDisc(Transform t, float radius, Vector3 p, float depth, float margin)
        {
            var d = p - t.position; float off = Mathf.Abs(Vector3.Dot(d, t.forward));
            return off <= depth && Vector3.ProjectOnPlane(d, t.forward).magnitude <= radius + margin;
        }
        private static float W13RayDisc(Transform t, float radius, Ray ray)
        {
            var plane = new Plane(t.forward, t.position);
            if (!plane.Raycast(ray, out float d) || d <= 0f) return -1f;
            return (ray.GetPoint(d) - t.position).magnitude <= radius + 0.02f ? d : -1f;
        }

        // ------------------------------------------------------------------ Reposition
        private string W13RepositionNote => ControllerProfiles.IsHand(_boot.Rig.Main.Profile)
            ? (Comfort.LeftHanded ? Lang.T("Reposition on. Shake the left hand to stop.") : Lang.T("Reposition on. Shake the right hand to stop."))
            : (Comfort.LeftHanded ? Lang.T("Reposition on. Shake the left controller to stop.") : Lang.T("Reposition on. Shake the right controller to stop."));

        /// <summary>Reposition on / off (the overhead menu's switch, a shake, resize mode starting, a smoke).</summary>
        public void W13SetReposition(bool on, string why)
        {
            if (W13Reposition == on) return;
            if (on)
            {
                if (LokiInput.ResizeMode) W13SetResizeMode(false, "Reposition starts"); // never both
                if (_wandHand != null) W12WandDrop(); // the grips carry things: the wand goes back to the hip (a skill on its tip with it)
                if (_w12 != null) foreach (var h in _w12) if (h.Open) { h.Cancelled = true; W12CloseGesture(h, false); }
                W13RepositionStarts++; _w13ModeSince = Time.unscaledTime;
            }
            else W13PlaceReleaseAll();
            W13Reposition = on; W13Shake.Reset();
            for (int i = 0; i < 2; i++) { _w13Holds[i] = -1; _w13GripWas[i] = _w12 != null && _w12[i].In.PlainGrip; } // (a grip already held takes nothing)
            if (!on && W12Wheel != null && W12Wheel.Placing) W12Wheel.Close();
            Debug.Log($"[Place] Reposition {(on ? "ON" : "off")} ({why})");
            if (!on) Hud?.Dismiss(W13RepositionNote); // client wave 18 (w18wand), point 18: the mode's hint goes the moment it ends
            W13LastNote = on ? W13RepositionNote : Lang.T("Reposition off.");
            Hud?.Notify(W13LastNote, on ? 6f : 3f);
            Haptics.Both(Haptics.Kind.Ui, 0.25f, 0.04f);
        }

        /// <summary>Everything Reposition places back at its default (the minimap, the wheel's picture, the holster, the elbow button).</summary>
        public void W13ResetPlaces(string why)
        {
            W13PlaceReleaseAll();
            Comfort.MinimapPlaced = false; Comfort.MinimapScale = 1f; Comfort.WheelPlaced = false; Comfort.WheelScale = 1f; Comfort.WandPlaced = false; Comfort.ElbowOffset = 0f;
            Comfort.WandScale = Comfort.WandMaxScale; // client wave 14 (w14cast), point 32: the wand's size with its holster (wave 13's item crosses are gone); client wave 21 (w21wand): its default is its largest
            Comfort.OverheadPlaced = false; Comfort.OverheadScale = 1f; // client wave 14 (w14menu), point 8: the overhead menu
            for (int i = 0; i < 2; i++) { Comfort.PadOffset[i] = Vector3.zero; Comfort.PadTurn[i] = Quaternion.identity; } // client wave 18 (w18flick), point 15: the pads on the fingertip
            Loki.Horde.Client.HordePassivesDraw.ResetOverheadPlace();
            W24ResetPlaces(); // client wave 24 (w24): the keyboard's places (the world's and the login screen's) and the login panel's (LokiGame.W24Login.cs)
            Loki.World.UserSettings.ClearMenuPlaces();
            Comfort.Save();
            MiniMap?.ApplyPlace(); if (W12Wheel != null && W12Wheel.Front) W12Wheel.ApplyFrontPlace(); W13Overhead?.PlaceNow();
            Debug.Log($"[Place] reset ({why}): the minimap, the wand wheel's picture, the holster (and the wand's size), the elbow button, the overhead menu, the pads (client wave 18), the keyboard and the login panel (client wave 24) at their defaults");

            W13LastNote = Lang.T("Places reset."); Hud?.Notify(W13LastNote, 3f);
        }

        private void W13PlaceReleaseAll()
        {
            foreach (var p in _w13Places)
            {
                if (p.By >= 0) { try { p.Release(); } catch (Exception e) { Debug.LogException(e); } }
                p.By = -1; p.Resizing = false; p.NearWas[0] = p.NearWas[1] = false; p.LaserWas = false;
                if (p.LitWas && p.Look != null) { try { p.Look(false); } catch { } p.LitWas = false; }
            }
            _w13Holds[0] = _w13Holds[1] = -1;
        }

        /// <summary>Client wave 21 (w21wand): a thing the grips may take now: shown, and its mode is on (Reposition's things in Reposition;
        /// the wand itself in resize mode).</summary>
        private bool W13PlaceUsable(Placeable p) => (p.InResize ? LokiInput.ResizeMode : W13Reposition) && p.Shown();

        /// <summary>Once a frame while Reposition is on (client wave 21, w21wand: or resize mode, for the wand): hands and the laser coming
        /// onto things (a tick), grips taking them, carrying, the other grip resizing, letting go (saved).</summary>
        private void UpdateW13Places()
        {
            var rig = _boot.Rig; if (rig == null || _w12 == null) return;
            var grip = new bool[2]; var down = new bool[2]; var at = new Vector3[2];
            for (int i = 0; i < 2; i++) { grip[i] = _w12[i].In.PlainGrip; down[i] = grip[i] && !_w13GripWas[i] && !_w12[i].In.GrabClaimed; _w13GripWas[i] = grip[i]; at[i] = _w12[i].T.position; } // (client wave 13, w13grab: a grip that took a panel takes nothing here)
            int main = Comfort.LeftHanded ? 0 : 1;
            var ray = new Ray(rig.PointerOrigin, rig.PointerDirection.sqrMagnitude > 1e-8f ? rig.PointerDirection.normalized : rig.PointerHand.forward);
            // what each hand and the laser are on (a tick on coming onto a thing that is free)
            var laserD = new float[_w13Places.Count];
            for (int k = 0; k < _w13Places.Count; k++)
            {
                var p = _w13Places[k];
                if (!W13PlaceUsable(p)) { if (p.By >= 0) { p.Release(); _w13Holds[p.By] = -1; if (p.Resizing) _w13Holds[1 - p.By] = -1; p.By = -1; p.Resizing = false; } laserD[k] = -1f; p.NearWas[0] = p.NearWas[1] = p.LaserWas = false; continue; }
                bool lit = p.By >= 0;
                for (int i = 0; i < 2; i++)
                {
                    bool near = p.Near != null && (p.Hand == null || p.Hand(i)) && p.Near(at[i]); // (client wave 18, w18flick: a pad's own hand does not take it)
                    if (near && !p.NearWas[i] && p.By < 0 && _w13Holds[i] < 0) { W13PlaceTicks++; Haptics.Pulse(Haptics.Kind.Ui, _w12[i].In.IsMain, 0.12f, 0.02f); }
                    p.NearWas[i] = near; lit |= near;
                }
                laserD[k] = p.Laser != null && (p.Hand == null || p.Hand(main)) ? p.Laser(ray) : -1f;
                bool on = laserD[k] >= 0f;
                if (on && !p.LaserWas && p.By < 0 && _w13Holds[main] < 0) { W13PlaceTicks++; Haptics.Pulse(Haptics.Kind.Ui, true, 0.12f, 0.02f); }
                p.LaserWas = on; lit |= on;
                if (lit != p.LitWas && p.Look != null) { p.Look(lit); }
                p.LitWas = lit;
            }
            // a grip taking something: what the hand is at first, else (the pointing hand) the nearest thing on its laser
            for (int i = 0; i < 2; i++)
            {
                if (!down[i] || _w13Holds[i] >= 0) continue;
                // (the other hand carries a picture: this grip resizes it, below; it takes nothing)
                int held = _w13Holds[1 - i]; if (held >= 0 && _w13Places[held].By == 1 - i && _w13Places[held].Resizable) continue;
                int pick = -1; bool byLaser = false;
                for (int k = 0; k < _w13Places.Count && pick < 0; k++) if (_w13Places[k].By < 0 && _w13Places[k].NearWas[i]) pick = k;
                if (pick < 0 && i == main)
                {
                    float best = float.MaxValue;
                    for (int k = 0; k < _w13Places.Count; k++) if (_w13Places[k].By < 0 && laserD[k] >= 0f && laserD[k] < best) { best = laserD[k]; pick = k; byLaser = true; }
                }
                if (pick < 0) continue;
                var p = _w13Places[pick]; var c = p.Centre();
                p.By = i; p.ByLaser = byLaser; p.Resizing = false; _w13Holds[i] = pick; if (!p.InResize) W13PlaceGrabs++; // (client wave 21, w21wand: Reposition's count stays Reposition's: the tutorial waits on it)
                if (byLaser) { p.Dist = laserD[pick]; p.Offset = c - ray.GetPoint(p.Dist); } else p.Offset = c - at[i];
                Haptics.Pulse(Haptics.Kind.Ui, _w12[i].In.IsMain, 0.25f, 0.04f);
                Debug.Log($"[Place] {p.Name} taken by the {(i == 0 ? "left" : "right")} hand{(byLaser ? $"'s laser at {p.Dist * 100f:0} cm" : "")}");
            }
            // carrying, resizing, letting go
            for (int k = 0; k < _w13Places.Count; k++)
            {
                var p = _w13Places[k]; if (p.By < 0) continue;
                int by = p.By, other = 1 - by;
                if (!grip[by])
                {
                    p.Release(); Debug.Log($"[Place] {p.Name} let go");
                    _w13Holds[by] = -1; if (p.Resizing && _w13Holds[other] == k) _w13Holds[other] = -1;
                    p.By = -1; p.Resizing = false; continue;
                }
                float hands = Mathf.Max(Vector3.Distance(at[0], at[1]), 0.05f);
                if (p.Resizable && !p.Resizing && down[other] && _w13Holds[other] < 0 && (p.Resizer == null || p.Resizer(other))) { p.Resizing = true; _w13Holds[other] = k; p.Hands0 = hands; p.Factor0 = p.Factor(); if (!p.InResize) W13PlaceResizes++; Haptics.Pulse(Haptics.Kind.Ui, _w12[other].In.IsMain, 0.2f, 0.03f); }
                else if (p.Resizing && !grip[other]) { p.Resizing = false; if (_w13Holds[other] == k) _w13Holds[other] = -1; }
                float f = p.Resizing ? Mathf.Clamp(p.Factor0 * hands / p.Hands0, p.Min, p.Max) : p.Factor(); // (client wave 21, w21wand: each thing's own limits)
                var to = p.ByLaser && by == main ? ray.GetPoint(p.Dist) + p.Offset : at[by] + p.Offset;
                p.Carry(to, f);
            }
        }

        // ------------------------------------------------------------------ the minimap
        /// <summary>The see-through minimap on / off (the overhead menu, the wrist strip's Map, a smoke); kept per player.</summary>
        public void W13SetMinimap(bool on, string why)
        {
            Comfort.MinimapOn = on; Comfort.Save();
            if (MiniMap != null) { MiniMap.ApplyPlace(); MiniMap.Show(on && W13InGame); }
            _w13MapWant = on && W13InGame;
            Debug.Log($"[Minimap] {(on ? "on" : "off")} ({why})");
            Haptics.Pulse(Haptics.Kind.Ui, true, 0.15f, 0.02f);
        }
        /// <summary>The overhead menu's - / +: the minimap's zoom, kept per player.</summary>
        public void W13MinimapZoom(int dir)
        {
            if (MiniMap == null) return;
            if (dir > 0) MiniMap.ZoomIn(); else MiniMap.ZoomOut();
            Comfort.MinimapZoom = MiniMap.ZoomStep; Comfort.Save();
        }
        private bool W13InGame => Session != null && Session.State == RoSession.Stage.InGame && Entities != null && Entities.Self != null;

        // ------------------------------------------------------------------ the forearm bar and its elbow button
        /// <summary>The forearm bar: 0 always on, 1 always off, 2 the elbow button (the overhead menu; the Settings row writes the same).</summary>
        public void W13SetBarMode(int mode, string why)
        {
            mode = Mathf.Clamp(mode, 0, 2); if (mode == Comfort.QuickBarMode) return;
            int was = Comfort.QuickBarMode; Comfort.QuickBarMode = mode; Comfort.Save();
            Debug.Log($"[Bar] forearm bar {Comfort.QuickBarModes[was]} → {Comfort.QuickBarModes[mode]} ({why})");
        }
        /// <summary>The elbow button: the bar shown / hidden (kept per player).</summary>
        public void W13ToggleElbowBar(string why)
        {
            if (Comfort.QuickBarMode != 2) return;
            Comfort.ElbowBarOpen = !Comfort.ElbowBarOpen; Comfort.Save();
            Debug.Log($"[Bar] the elbow button ({why}): the bar {(Comfort.ElbowBarOpen ? "shown" : "hidden")}");
        }

        /// <summary>The forearm that carries the bar: the hand, the estimated elbow, the forearm's axis (towards the hand), the hand's up
        /// made square to it.</summary>
        public (Transform hand, Vector3 elbow, Vector3 axis, Vector3 up) W13ElbowFrame()
        {
            var rig = _boot.Rig; var hand = Quick != null && Quick.Hand != null ? Quick.Hand : rig.OffHand;
            var input = hand == rig.LeftHand ? rig.Input.Left : rig.Input.Right;
            Vector3 axis = hand.forward;
            if (!input.JointMode && input.TryAim(out _, out var ar)) { var a = rig.transform.rotation * (ar * Vector3.forward); if (a.sqrMagnitude > 1e-6f) axis = a.normalized; }
            var up = Vector3.ProjectOnPlane(hand.up, axis); if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.up, axis); up.Normalize();
            float back = (input.JointMode ? ElbowBackHand : ElbowBack) - Comfort.ElbowOffset;
            return (hand, hand.position - axis * back, axis, up);
        }
        /// <summary>Reposition: the elbow button slid along the forearm to where the carrying hand is (projected on the arm's axis).</summary>
        private void W13SlideElbow(Vector3 world)
        {
            var f = W13ElbowFrame(); var input = f.hand == _boot.Rig.LeftHand ? _boot.Rig.Input.Left : _boot.Rig.Input.Right;
            float back = input.JointMode ? ElbowBackHand : ElbowBack;
            float along = Vector3.Dot(world - f.hand.position, f.axis); // (negative: behind the hand)
            Comfort.ElbowOffset = Mathf.Clamp(back + along, Comfort.ElbowOffsetMin, Comfort.ElbowOffsetMax);
        }

        private void UpdateW13Elbow()
        {
            var rig = _boot.Rig; if (_w13Elbow == null || rig == null) return;
            bool show = Comfort.QuickBarMode == 2 && W13InGame && !W8HordeBlocksWorld && (Hud == null || !Hud.Wrist.ShownOnWrist);
            if (_w13Elbow.gameObject.activeSelf != show) { _w13Elbow.gameObject.SetActive(show); _w13ElbowArmed = false; }
            bool chat = W19ChatButtonWanted; // client wave 19 (w19view41), point 41: the chat button, in every bar mode
            if (_w19Chat != null && _w19Chat.gameObject.activeSelf != chat) { _w19Chat.gameObject.SetActive(chat); _w13ElbowArmed = false; }
            if (!show && !chat) return;
            var f = W13ElbowFrame();
            if (show)
            {
                _w13Elbow.position = f.elbow + f.up * ElbowLift; UiKit.FaceHead(_w13Elbow, rig.Head);
                // its look: lit gold while the bar is shown, dark while hidden (the state, never what a press would do)
                bool lit = Comfort.ElbowBarOpen;
                if (lit != _w13ElbowLit || _w13ElbowBtn.Normal != (lit ? OverheadMenu.Lit : OverheadMenu.Unlit))
                {
                    _w13ElbowLit = lit; _w13ElbowBtn.Normal = lit ? OverheadMenu.Lit : OverheadMenu.Unlit; _w13ElbowBtn.Hover = lit ? OverheadMenu.LitHover : UiKit.ButtonHover;
                    _w13ElbowBtn.SetHover(_w13ElbowBtn.Hovered); _w13ElbowGlyph.color = lit ? OverheadMenu.LitText : UiKit.Dim;
                }
            }
            if (chat) UpdateW19ChatButton(f);
            // the other hand's tip touching one: one touch, one toggle (the nearer of the two buttons; both wait for the tip to leave)
            if (_w12 == null || W13Reposition) { _w13ElbowArmed = false; return; }
            int other = f.hand == rig.LeftHand ? 1 : 0; var tip = W12Tip(_w12[other]);
            float dE = show ? Vector3.Distance(tip, _w13Elbow.position) : float.MaxValue, dC = chat ? Vector3.Distance(tip, _w19Chat.position) : float.MaxValue;
            float d = Mathf.Min(dE, dC), now = Time.unscaledTime;
            if (_w13ElbowArmed && d <= ElbowTouch && now >= _w13ElbowLock)
            {
                _w13ElbowArmed = false; _w13ElbowLock = now + ElbowLockout;
                Haptics.Pulse(Haptics.Kind.Ui, _w12[other].In.IsMain, 0.25f, 0.03f);
                if (dC < dE) { W19ChatTouches++; W19ChatPress($"a touch of the {(other == 0 ? "left" : "right")} hand"); }
                else { W13ElbowTouches++; W13ToggleElbowBar($"a touch of the {(other == 0 ? "left" : "right")} hand"); }
            }
            else if (!_w13ElbowArmed && d > ElbowRearm && now >= _w13ElbowLock) _w13ElbowArmed = true;
        }

        // ------------------------------------------------------------------ client wave 19 (w19view41), point 41: the chat button
        // design note (2026-10-10): "The only way to open and close the chat should be by pressing a second button that rests on the player's left
        // elbow as well." Beside the elbow button: the same size and look (a dark square, lit gold while the chat is shown), a speech bubble
        // drawn on it, ChatGap m further along the forearm towards the hand. Shown whenever the player is in the game, in EVERY forearm bar
        // mode (in "on" / "off" there is no elbow button: it is then alone near the elbow), Horde's arena included, not while the classic
        // wrist menu sits on that wrist. Pressed by the laser or look + trigger, or touched by the other hand's tip like the elbow button
        // (the nearer of the two takes a touch). A Hud panel (UI/UiKit.cs PanelKinds "ChatButton"): never an open menu. Reposition slides
        // it with the elbow button (one place: VR/Comfort.cs ElbowOffset).
        public const float ChatGap = 0.06f;
        private RectTransform _w19Chat; private UiButton _w19ChatBtn; private Image[] _w19ChatInk; private bool _w19ChatLit;
        /// <summary>The chat button (null before the game starts), whether it is shown, its presses by laser / look and touches (smoke, log).</summary>
        public Transform W19ChatButton => _w19Chat;
        public UiButton W19ChatButtonUi => _w19ChatBtn;
        public bool W19ChatButtonShown => _w19Chat != null && _w19Chat.gameObject.activeSelf;
        public int W19ChatPresses { get; private set; }
        public int W19ChatTouches { get; private set; }
        private bool W19ChatButtonWanted => Hud != null && W13InGame && !Hud.Wrist.ShownOnWrist;

        private void BuildW19ChatButton(DioramaRig rig)
        {
            _w19Chat = UiKit.Panel("ChatButton", rig.transform, new Vector2(ElbowPx, ElbowPx), ElbowScale, false);
            _w19ChatBtn = UiKit.Button(_w19Chat, "", Vector2.zero, new Vector2(ElbowPx, ElbowPx), () => { W19ChatPresses++; W19ChatPress("its button (laser or look)"); }, 76);
            _w19ChatBtn.gameObject.name = "btn:chat";
            _w19ChatBtn.Tip = () => Lang.T("Chat: show / hide");
            UiKit.Box(_w19Chat, "ring", Vector2.zero, new Vector2(ElbowPx + 10, ElbowPx + 10), HudLook.ButtonEdge(0.85f)).SetAsFirstSibling(); // (client wave 21, w21tools: as the elbow button's)
            // the speech bubble: a body, its tail at the lower left, three dots in the button's own colour
            var b = (RectTransform)_w19ChatBtn.transform;
            var body = UiKit.Box(b, "bubble", new Vector2(0f, 10f), new Vector2(84f, 54f), UiKit.Dim);
            var tail = UiKit.Box(b, "tail", new Vector2(-22f, -20f), new Vector2(20f, 20f), UiKit.Dim); tail.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var d1 = UiKit.Box(b, "dot", new Vector2(-22f, 10f), new Vector2(11f, 11f), OverheadMenu.Unlit);
            var d2 = UiKit.Box(b, "dot", new Vector2(0f, 10f), new Vector2(11f, 11f), OverheadMenu.Unlit);
            var d3 = UiKit.Box(b, "dot", new Vector2(22f, 10f), new Vector2(11f, 11f), OverheadMenu.Unlit);
            _w19ChatInk = new[] { body.GetComponent<Image>(), tail.GetComponent<Image>(), d1.GetComponent<Image>(), d2.GetComponent<Image>(), d3.GetComponent<Image>() };
            _w19ChatLit = true; // (the first update paints it)
            _w19Chat.gameObject.SetActive(false);
        }

        /// <summary>The chat button pressed (its button) or touched: the chat shown / hidden (UI/LokiHud.cs ToggleChat; refused in the tutorial).</summary>
        public void W19ChatPress(string why)
        {
            if (Hud == null) return;
            Hud.ToggleChat();
            Debug.Log($"[Chat] the chat button ({why}): the chat {(Hud.Chat.Visible ? "shown" : "hidden")}");
        }

        private void UpdateW19ChatButton((Transform hand, Vector3 elbow, Vector3 axis, Vector3 up) f)
        {
            _w19Chat.position = f.elbow + f.axis * ChatGap + f.up * ElbowLift; UiKit.FaceHead(_w19Chat, _boot.Rig.Head);
            bool lit = Hud.Chat.Visible;
            if (lit == _w19ChatLit && _w19ChatBtn.Normal == (lit ? OverheadMenu.Lit : OverheadMenu.Unlit)) return;
            _w19ChatLit = lit; _w19ChatBtn.Normal = lit ? OverheadMenu.Lit : OverheadMenu.Unlit; _w19ChatBtn.Hover = lit ? OverheadMenu.LitHover : UiKit.ButtonHover;
            _w19ChatBtn.SetHover(_w19ChatBtn.Hovered);
            var ink = lit ? OverheadMenu.LitText : UiKit.Dim;
            for (int i = 0; i < _w19ChatInk.Length; i++) _w19ChatInk[i].color = i < 2 ? ink : _w19ChatBtn.Normal;
        }

        // ------------------------------------------------------------------ the eighteenth item: the microphone on the freed button's press
        // design note (2026-10-07, ~15:05): "set the press (hold stays as toggle resize mode) to mute and unmute microphone instead of sitting
        // down and getting up". The Voice page has no switch of its own for the microphone but "Share my voice" (Voice/VoiceChat.cs
        // SettingsRows; the microphone is open only while it is on): the press toggles that stored switch, so the page and the button
        // stay in step. A note says "Mic off" / "Mic on"; while it is off a small crossed microphone (see-through, 2.6 cm) floats where
        // the open microphone's icon sits, over the walking wrist (Voice/VoiceChat.cs UpdateIcon), so a muted player always knows.
        public const float MicMarkScale = 0.00036f;
        private RectTransform _w13MicMark;
        /// <summary>Presses that toggled the microphone (smoke, log).</summary>
        public int W13MicToggles { get; private set; }
        public bool W13MicMarkShown => _w13MicMark != null && _w13MicMark.gameObject.activeSelf;

        public void W13ToggleMic(string why)
        {
            Loki.Voice.VoiceSettings.Share = !Loki.Voice.VoiceSettings.Share; Loki.Voice.VoiceSettings.Save(); W13MicToggles++;
            bool on = Loki.Voice.VoiceSettings.Share;
            Debug.Log($"[Voice] my microphone {(on ? "on" : "off")} ({why}): Share my voice {on}");
            W13LastNote = on ? Lang.T("Mic on") : Lang.T("Mic off"); Hud?.Notify(W13LastNote, 2f);
            Haptics.Pulse(Haptics.Kind.Ui, false, 0.2f, 0.03f);
        }

        private void BuildW13MicMark(DioramaRig rig)
        {
            _w13MicMark = UiKit.Panel("MicMuted", rig.transform, new Vector2(72, 72), MicMarkScale, false);
            var grey = HudLook.Mark(0.55f); var red = new Color(1f, 0.3f, 0.25f, 0.75f); // (client wave 21, w21tools: the theme's cream at the same alpha; the cross stays red)
            UiKit.Box(_w13MicMark, "capsule", new Vector2(0, 12), new Vector2(18, 34), grey);
            UiKit.Box(_w13MicMark, "cradle", new Vector2(0, -9), new Vector2(34, 4), grey);
            UiKit.Box(_w13MicMark, "stem", new Vector2(0, -17), new Vector2(4, 14), grey);
            UiKit.Box(_w13MicMark, "foot", new Vector2(0, -25), new Vector2(22, 4), grey);
            UiKit.Box(_w13MicMark, "cross", Vector2.zero, new Vector2(78, 7), red).localRotation = Quaternion.Euler(0f, 0f, 45f);
            _w13MicMark.gameObject.SetActive(false);
        }

        private void UpdateW13MicMark()
        {
            var rig = _boot.Rig; if (_w13MicMark == null || rig == null) return;
            bool show = !Loki.Voice.VoiceSettings.Share && W13InGame && !W8HordeBlocksWorld;
            if (_w13MicMark.gameObject.activeSelf != show) _w13MicMark.gameObject.SetActive(show);
            if (!show) return;
            var hand = rig.OffHand; var toHead = rig.Head.transform.position - hand.position; toHead.y = 0; if (toHead.sqrMagnitude > 1e-4f) toHead.Normalize();
            _w13MicMark.position = hand.position + Vector3.up * 0.035f + toHead * 0.03f; UiKit.FaceHead(_w13MicMark, rig.Head);
        }

        // ------------------------------------------------------------------ once a frame (from UpdateW13Resize)
        private void UpdateW13View()
        {
            var rig = _boot.Rig; if (rig == null) return;
            // the minimap follows its stored state when the game starts or the player changes it (a smoke's own toggles are left alone)
            bool want = Comfort.MinimapOn && W13InGame;
            if (want != _w13MapWant) { _w13MapWant = want; if (MiniMap != null) { MiniMap.ApplyPlace(); MiniMap.Show(want); } }
            UpdateW13Elbow();
            UpdateW13MicMark();
            // Reposition: the wand wheel's picture shown for placing (the wand scheme, "in front of you"), then the grips
            bool world = W13InGame && !Player.Dead && !W8HordeBlocksWorld;
            if (!world && W13Reposition) W13SetReposition(false, "the world went away");
            if (W12Wheel != null)
            {
                bool wheel = W13Reposition && Comfort.CastGesture == 3 && Comfort.WandWheelFront && _wandHand == null;
                if (wheel && !W12Wheel.Placing) W12Wheel.ShowForPlacing(rig.Head.transform, W12WandSlots.quick, Hotkeys.CurrentTab, W12WandSlots.quickItem); // (client wave 18, w18wand: items too)
                else if (!wheel && W12Wheel.Placing) W12Wheel.Close();
            }
            UpdateW18Flick(); // client wave 18 (w18flick), point 15: the pads' ghosts in Reposition (LokiGame.W18Flick.cs)
            if (W13Reposition || LokiInput.ResizeMode) UpdateW13Places(); // (client wave 21, w21wand: resize mode's grips on the wand: the same taking and two-grip resizing)
        }
    }
}
