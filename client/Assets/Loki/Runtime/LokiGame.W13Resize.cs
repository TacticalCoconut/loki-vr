using System;
using Loki.Game;
using Loki.UI;
using Loki.VR;
using UnityEngine;
using UnityEngine.UI;

namespace Loki
{
    /// <summary>
    /// Client wave 13 (w13resize), deploy step 1: the OVERHEAD MENU (UI/OverheadMenu.cs) and what it switches, and the one rule for
    /// the new casting.
    ///   Resize mode   (the brief's first item; design note, 2026-10-07: "they can tap on a button to toggle the resize mode. At which point
    ///                 they just have to grip the controller to control everything. Then they can tap the button again to end the
    ///                 resizing mode. Or they can shake the right controller.") On: a plain grip is the camera's alone
    ///                 (VR/LokiInput.cs Hand.CameraGrip): the walking hand's grip + its stick pans the table, both grips resize it,
    ///                 the pointing hand's grip with the laser on a board holds it, both grips resize it (VR/DioramaRig.cs,
    ///                 VR/DioramaRig.Ui.cs); a tracked hand: its fist. Meanwhile no casting gesture, no wand taken, no item buttons
    ///                 (the design's reading, 2026-10-07: "a grip is the camera's alone"), and a wand in a hand goes back to the hip when
    ///                 the mode starts (a skill on its tip is dropped with it, as when its grip is let go). Off, the normal state:
    ///                 NO grip moves the table or a board (wave 12's full grip is gone). A small mark over each hand says the mode
    ///                 is on, one note says how to end it. It ends by its Off choice or by a SHAKE of the pointing controller
    ///                 (Game/ShakeDetector.cs; the hand that holds the laser: the right one, the left in left-handed mode). It stays
    ///                 on over a map change and a Horde run (ended only by the player; a restart of the game starts with it off).
    ///   Casting mode  (the brief's sixteenth item; design note: "a button to the hidden menu above the player that toggles the different
    ///                 casting modes: by clicking it cycles along the different casting modes") the same stored setting as Settings
    ///                 → Controls "Casting gesture" (VR/Comfort.cs CastGesture): one press = the next of off / pad / flick / wand,
    ///                 wrapping round, all four shown with the current one lit. A change calls off a gesture under way (nothing
    ///                 taken: LokiGame.W12Cast.cs) and a wand in the hand goes back to the hip when the mode leaves "wand"
    ///                 (LokiGame.W12Wand.cs). Client wave 15 (w15menu), point 31 (design note: "Right now, when I click it, it will rotate
    ///                 through the options from left to right. I would like it to simply choose the one that I am aiming at instead"):
    ///                 a SWITCH like every other control: a press on a choice selects THAT choice; no cycle.
    ///   CASTING ALLOWED (the brief's seventh item; design note: "the grip still activates stuff like the wheel and other casting systems while
    ///                 I am messing around with menus and stuff. Those should stay deactivated"): <see cref="W13CastingAllowed"/> is
    ///                 the ONE test every entry point of the new casting asks (the pad / flick start gesture and the item buttons:
    ///                 LokiGame.W12Cast.cs; taking the wand, its wheel / strokes and its buttons: LokiGame.W12Wand.cs): false while
    ///                 resize mode is on, the wand holster is being placed, or any MENU panel is open (UI/UiKit.cs PanelKinds: every
    ///                 panel made by UiKit.Panel is decided by an explicit list: a menu, a HUD panel that is simply always there, or
    ///                 one of the casting's own pictures; a name nobody decided counts as a menu and the smoke fails on it; the new
    ///                 boards are menus: the design's reading). A gesture under way is closed with nothing taken and does not open again
    ///                 until its grip is let go; a wand in the hand stays there, its trigger presses the menus' buttons (laser or
    ///                 look) and does nothing in the world, a skill on its tip stays. The skill wheels, the forearm bar and the
    ///                 menus' own Use buttons are not touched by this rule.
    /// </summary>
    public sealed partial class LokiGame
    {
        public OverheadMenu W13Overhead { get; private set; }
        public bool W13ResizeMode => LokiInput.ResizeMode;
        /// <summary>The shake that ends resize mode (the pointing hand), and the meter that measures ordinary play outside it (log only).</summary>
        public readonly ShakeDetector W13Shake = new ShakeDetector();
        private readonly ShakeDetector _w13Meter = new ShakeDetector();
        /// <summary>Resize modes started; ended by a shake; casting-mode changes made on the overhead menu (smoke, log).</summary>
        public int W13ResizeStarts { get; private set; }
        public int W13ShakeEnds { get; private set; }
        public int W13CastModeChanges { get; private set; }
        /// <summary>The last note resize mode showed (smoke, log).</summary>
        public string W13LastNote { get; private set; }
        private float _w13ModeSince, _w13MeterSince;
        private string _w13Blocker;
        /// <summary>What switched the new casting off at the last asking (null: nothing).</summary>
        public string W13CastingBlocker => _w13Blocker;

        /// <summary>The new casting (the pad / flick gesture, the wand, the item buttons) may act now: no resize mode, no holster
        /// placement, no menu open (UI/UiKit.cs PanelKinds). Asked where it is used, so a menu closed this frame counts this frame.</summary>
        public bool W13CastingAllowed
        {
            get
            {
                if (LokiInput.ResizeMode) { _w13Blocker = "resize mode"; return false; }
                // client wave 23 (the merge), item 11, the design's option A: the tutorial's Resize step that begins in the Table view asks for the
                // Table view's free grab ("Squeeze your left grip ..."), and a grip squeezed with the index finger off the trigger is the casting
                // gesture's, not the table's (LokiGame.W14Laser.cs W14TableNo): the casting is off for that step, as it is in Resize mode, so the
                // grip the line names is the map's whatever the finger does
                if (W18TutRunning && _w23ResizeFree && W18TutStepKey == "resize") { _w13Blocker = "the tutorial's Resize step"; return false; }
                if (W13Reposition) { _w13Blocker = "reposition"; return false; } // client wave 13 (w13view): its grips carry things (LokiGame.W13View.cs)
                if (W12WandPlacing) { _w13Blocker = "the wand holster's placement"; return false; }
                if (W13PanelHeld) { _w13Blocker = "a panel held"; return false; } // client wave 13 (w13grab), item 19: a placing state (LokiGame.W13Grab.cs)
                // (client wave 19, point 44, design note: "The floating menu above me counts as a menu for the purposes of the tutorial saying that I have
                // to close an open menu first": while the tutorial runs the overhead menu does not block the casting. Outside it, it still does
                // while it is awake: wave 13's one casting rule, unchanged: flagged)
                if (PanelKinds.MenuOpen(out _w13Blocker, W18TutRunning ? "OverheadMenu" : null)) return false;
                _w13Blocker = null; return true;
            }
        }

        private RectTransform[] _w13Marks; private Text[] _w13MarkText;

        private void InitW13Resize(LokiBootstrap boot)
        {
            var rig = boot.Rig; if (rig == null || _w12 == null) return;
            var m = W13Overhead = OverheadMenu.Create(rig.transform, rig.Head);
            m.SetPageName(0, () => Lang.T("Menu")); // client wave 21 (w21over), point 58: the name on the frame's gold plate
            m.AddSwitch("resize", () => Lang.T("Resize mode"), 2, () => LokiInput.ResizeMode ? 1 : 0, k => W13SetResizeMode(k == 1, "the overhead menu"),
                k => k == 0 ? Lang.T("~switch~Off") : Lang.T("~switch~On"));
            m.AddSwitch("casting", () => Lang.T("Casting gesture"), 4, () => Comfort.CastGesture, k => W13SetCastGesture(k, "the overhead menu"), ComfortWindow.CastName); // (client wave 15, point 31: was AddCycle)
            InitW13View(boot, m); // client wave 13 (w13view): Reposition, the minimap, the forearm bar; the freed button's hold (LokiGame.W13View.cs)
            InitW13Grab(boot, m); // client wave 13 (w13grab), items 19 and 20: "Panel tilt", the edge grab's indicators and its claim (LokiGame.W13Grab.cs)
            InitW14Ui(boot); // client wave 14 (w14ui): the trigger's edge grab, the menu button that closes everything (LokiGame.W14Ui.cs)
            InitW14Menu(boot); // client wave 14 (w14menu): the overhead menu in Reposition, the buttons' tips (LokiGame.W14Menu.cs)
            InitW13Look(boot, m); // client wave 13 (w13look): "Look select", the minimap clicked by the laser (LokiGame.W13Look.cs)
            InitW14Laser(boot, m); // client wave 14 (w14laser): "Laser walking", the walking hand's laser, the Table view's free grab (LokiGame.W14Laser.cs)
            InitW20Bubbles(boot, m); // client wave 20 (w20bubbles), point 56: the Quick Use Bubbles, the main page's last row and their editor's page (LokiGame.W20Bubbles.cs)
            m.AddSwitch("visionaim", () => Lang.T("Aim spells with vision"), 2, () => Comfort.VisionAimSpells ? 1 : 0, k => W13SetVisionAim(k == 1, "the overhead menu"),
                k => k == 0 ? Lang.T("~switch~Off") : Lang.T("~switch~On"));
            Comfort.Changed += () => { if (W13Overhead != null) W13Overhead.Refresh(); };
            if (Hud != null && Hud.Wrist != null) PanelKinds.SetOpenTest(Hud.Wrist.transform, () => Hud.Wrist.ShownOnWrist); // (New style: the wrist menu's object only hosts the boards' pages)
            _w13Marks = new RectTransform[2]; _w13MarkText = new Text[2];
            for (int i = 0; i < 2; i++)
            {
                var rt = UiKit.Panel(i == 0 ? "ModeMarkLeft" : "ModeMarkRight", rig.transform, new Vector2(300, 64), 0.0003f, false);
                UiKit.Box(rt, "plate", Vector2.zero, new Vector2(300, 64), new Color(0.05f, 0.05f, 0.08f, 0.6f));
                UiKit.Box(rt, "edge", new Vector2(0, -30), new Vector2(300, 4), OverheadMenu.Lit);
                _w13MarkText[i] = UiKit.Label(rt, "", Vector2.zero, new Vector2(290, 58), 34, TextAnchor.MiddleCenter, new Color(1f, 0.86f, 0.45f));
                _w13MarkText[i].resizeTextForBestFit = true; _w13MarkText[i].resizeTextMinSize = 20; _w13MarkText[i].resizeTextMaxSize = 34;
                rt.gameObject.SetActive(false); _w13Marks[i] = rt;
            }
        }

        /// <summary>The note when resize mode starts: how to end it with the pointing hand.</summary>
        private string W13OnNote => ControllerProfiles.IsHand(_boot.Rig.Main.Profile)
            ? (Comfort.LeftHanded ? Lang.T("Resize mode on. Shake the left hand to stop.") : Lang.T("Resize mode on. Shake the right hand to stop."))
            : (Comfort.LeftHanded ? Lang.T("Resize mode on. Shake the left controller to stop.") : Lang.T("Resize mode on. Shake the right controller to stop."));

        /// <summary>Resize mode on / off (the overhead menu's switch, a shake, a smoke).</summary>
        public void W13SetResizeMode(bool on, string why)
        {
            if (LokiInput.ResizeMode == on) return;
            if (on)
            {
                if (W13Reposition) W13SetReposition(false, "resize mode starts"); // client wave 13 (w13view): never both
                if (_wandHand != null) W12WandDrop(); // the grips are the camera's: the wand goes back to the hip (a skill on its tip with it)
                if (_w12 != null) foreach (var h in _w12) if (h.Open) { h.Cancelled = true; W12CloseGesture(h, false); }
                W13ResizeStarts++; _w13ModeSince = Time.unscaledTime;
                for (int i = 0; i < 2; i++) { _w13Holds[i] = -1; _w13GripWas[i] = _w12 != null && _w12[i].In.PlainGrip; } // client wave 21 (w21wand): the wand's sizing: a grip already held takes nothing (as Reposition: LokiGame.W13View.cs)
            }
            else Debug.Log($"[Shake] resize mode lasted {Time.unscaledTime - _w13ModeSince:0.0} s: at most {W13Shake.MostAtOnce} reversal(s) within {ShakeDetector.Window:0.0} s ({ShakeDetector.Reversals} end it), the fastest frame {W13Shake.Fastest:0.00} m/s");
            if (!on) W13PlaceReleaseAll(); // client wave 21 (w21wand): a wand held for sizing goes back to the hip, its size kept (LokiGame.W21Wand.cs)
            LokiInput.ResizeMode = on; W13Shake.Reset();
            Debug.Log($"[Resize] resize mode {(on ? "ON" : "off")} ({why})");
            if (!on) Hud?.Dismiss(W13OnNote); // client wave 18 (w18wand), point 18: the mode's hint goes the moment it ends
            W13LastNote = on ? W13OnNote : Lang.T("Resize mode off.");
            Hud?.Notify(W13LastNote, on ? 6f : 3f);
            Haptics.Both(Haptics.Kind.Grab, 0.25f, 0.04f);
        }

        /// <summary>The casting gesture (0 off, 1 pad, 2 flick, 3 wand; wraps round): the overhead menu's cycle. The same setting as
        /// Settings → Controls "Casting gesture". A gesture under way is called off; a wand in the hand goes back when the mode
        /// leaves "wand". (Client wave 25, w25tut: <paramref name="note"/> false: no notice, the tutorial's own quiet changes.)</summary>
        public void W13SetCastGesture(int mode, string why, bool note = true)
        {
            mode = ((mode % 4) + 4) % 4; int was = Comfort.CastGesture;
            if (mode == was) return;
            if (_w12 != null) foreach (var h in _w12) if (h.Open) { h.Cancelled = true; W12CloseGesture(h, false); }
            if (was == 3 && _wandHand != null) W12WandDrop();
            Comfort.CastGesture = mode; Comfort.Save(); W13CastModeChanges++;
            if (ComfortUi != null && ComfortUi.Visible) ComfortUi.Refresh(); // (the Settings row is the same setting: it shows the same)
            Debug.Log($"[Cast] casting gesture {Comfort.CastGestures[was]} → {Comfort.CastGestures[mode]} ({why})");
            if (note) Hud?.Notify(Lang.F("Casting gesture: {0}", ComfortWindow.CastName(mode)), 3f);
        }

        private void UpdateW13Resize()
        {
            var rig = _boot.Rig; var m = W13Overhead;
            if (rig == null || m == null || _w12 == null) return;
            float dt = Time.unscaledDeltaTime;
            m.Tick(VR.WandHolster.HeadYaw(rig.Head.transform), rig.GazeRay, dt);
            UpdateW13View(); // client wave 13 (w13view): the minimap's state, the elbow button, Reposition's grips (LokiGame.W13View.cs)
            UpdateW14Menu(); // client wave 14 (w14menu): the overhead menu's placing state, the buttons' tips (LokiGame.W14Menu.cs)
            UpdateW20Bubbles(); // client wave 20 (w20bubbles), point 56: the Quick Use Bubbles: places, looks, touch and trigger uses (LokiGame.W20Bubbles.cs)
            bool mode = LokiInput.ResizeMode || W13Reposition; // (client wave 13, w13view: Reposition has the same mark and shake)
            // (client wave 14, w14laser, point 11: in the Follow view one grip tilts the view)
            bool follow = View != null && View.Mode == ViewController.ViewMode.Follow;
            m.Hint = LokiInput.ResizeMode ? (follow ? Lang.T("Grip: tilt. Both grips: resize.") : Lang.T("Grip: move. Both grips: resize.")) : W13Reposition ? Lang.T("Grip: carry. Both grips: resize.") : W24TutHint; // (client wave 24, w24, item 15: the tutorial's current action line, on the menu the player is looking up at)
            // the mark over each hand: the mode is on
            for (int i = 0; i < 2; i++)
            {
                var mk = _w13Marks[i];
                if (mk.gameObject.activeSelf != mode) mk.gameObject.SetActive(mode);
                if (!mode) continue;
                string word = LokiInput.ResizeMode ? Lang.T("~mark~Resize mode") : Lang.T("~mark~Reposition");
                if (_w13MarkText[i].text != word) _w13MarkText[i].text = word;
                mk.position = _w12[i].T.position + Vector3.up * 0.09f; UiKit.FaceHead(mk, rig.Head);
            }
            var at = rig.transform.InverseTransformPoint(rig.PointerHand.position); // (the rig's own space: the table's moves mean nothing to it)
            if (!mode)
            {
                // ordinary play, measured for the headset check (log only): once a minute, when the hand reversed at all
                _w13Meter.Update(at, dt);
                if (Time.unscaledTime - _w13MeterSince >= 60f)
                {
                    if (_w13MeterSince > 0f && _w13Meter.MostAtOnce > 0) Debug.Log($"[Shake] ordinary movement, the last minute: at most {_w13Meter.MostAtOnce} reversal(s) of the pointing hand within {ShakeDetector.Window:0.0} s ({ShakeDetector.Reversals} would end resize mode), the fastest frame {_w13Meter.Fastest:0.00} m/s");
                    _w13MeterSince = Time.unscaledTime; _w13Meter.Reset();
                }
                return;
            }
            // a shake of the pointing controller ends the mode (resize mode or, client wave 13 w13view, Reposition). Client wave 13 (w13grab):
            // not while that hand holds a panel: its shake is the panel's (LokiGame.W13Grab.cs: the two are told apart by what the hand holds)
            if (W13Holding(Comfort.LeftHanded ? 0 : 1) != null) { W13Shake.Reset(); return; }
            if (W13Shake.Update(at, dt)) { W13ShakeEnds++; if (LokiInput.ResizeMode) W13SetResizeMode(false, "a shake"); else W13SetReposition(false, "a shake"); }
        }
    }
}
