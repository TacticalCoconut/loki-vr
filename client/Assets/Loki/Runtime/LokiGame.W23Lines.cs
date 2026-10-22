using System;
using Loki.UI;
using Loki.VR;
using UnityEngine;
using Part = Loki.UI.ControllerGhost.PartId;
using Profile = Loki.VR.ControllerProfiles.Profile;

namespace Loki
{
    /// <summary>
    /// Client wave 23 (w23lines): the tutorial's ACTION LINES. design note (playtest notes, answers
    /// 39, 45, 46, 48):
    ///   Item 5   "the player should not be able to pick the skip tutorial button with their eyes to avoid accidentally canceling the
    ///            tutorial": the buttons that leave the tutorial early are the laser's alone (UI/UiKit.cs UiButton.LaserOnly, read by the
    ///            look's own gate VR/DioramaRig.Ui.cs Choosable): the guide's "End tutorial" on every page that has it (the steps', the
    ///            casting ways' lessons'), the casting panel's "End tutorial" (LokiGame.W22Cast.cs W22CastEnter) and "Skip step"
    ///            (<see cref="W23SkipStepLaserOnly"/>). "Start", "Back", "Casting ways", "Continue the tutorial", "Enter the world" and the
    ///            calibration's "Cancel" / "Close" stay the look's too.
    ///   Item 10  "you just say that hold the stick down ... You don't say what, which one of the sides I'm meant to be pressing. So you need
    ///            to do a pass on all the text ... properly naming and identifying what buttons need to be interacted with and in what way":
    ///            every action line names the HAND, the CONTROL by its name on the controller in the player's hands and a verb with one
    ///            reading ("click in your left stick", "push your left stick forward", "pull your right trigger", "squeeze your left grip",
    ///            "press B on your left controller"). <see cref="W23Control"/> is the ONE table: for each input a line names and each
    ///            controller kind, the part (the ghost controller's: UI/ControllerGhost.cs), the hand, the word (the Settings →
    ///            Controls "Buttons: ..." page's own: VR/ControllerProfiles.cs Buttons) and the device's control (VR/LokiInput.cs
    ///            BeginFrame). The lines are built from it below; a step a controller cannot do is passed with a line saying why.
    ///   Item 13  "when it tells me to open the settings menu, it actually wants me to open the settings menu inside of the settings
    ///            menu": one action line per press, each with its own check mark, told apart by what the player sees: the button on the
    ///            controller, the icon on the ring, the tab on the board (<see cref="W23RingLine"/>; the steps' parts are built when the
    ///            step begins: LokiGame.W18Tutorial.cs W18TStep.Build). A line that needs no press of its own (the look up at the menu
    ///            above) shares its part with the press it leads to (<see cref="W23Or"/>): the part's line is always the next thing to do.
    ///   Item 11  NOT written (the code does not say what the sentence would say): design notes has the facts.
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>Item 5: "Skip step" is the laser's alone too (the brief's reading of "any Skip"; the design named the button that ends the
        /// tutorial). False: the look presses "Skip step" again.</summary>
        public const bool W23SkipStepLaserOnly = true;
        /// <summary>client wave 23 (the merge), item 13: the Settings step's help part is done when a setting's help has been in view this
        /// long (it was: the frame it appeared, which is the frame the window opens under the laser that pressed "Settings…").</summary>
        public const float W23HelpPartSeconds = 1f;
        /// <summary>Item 10: the longest an action line may be, in words (the brief's number).</summary>
        public const int W23MaxWords = 14;
        /// <summary>The walking hand's stick pushed this far counts as "pushed toward an icon" (the ring marks an icon from 0.25: UI/MenuRing.cs).</summary>
        public const float W23StickPushed = 0.5f;

        /// <summary>The inputs the tutorial's lines name. Main = the pointing hand, Off = the walking hand (Comfort.LeftHanded swaps them).</summary>
        public enum W23In { MainTrigger, OffTrigger, AnyTrigger, MainGrip, OffGrip, AnyGrip, BothGrips, OffStick, MainStick, AnyStick, ViewButton, Menu, Settings }
        /// <summary>The controller kinds whose lines differ (VR/ControllerProfiles.cs Buttons' own five cases).</summary>
        public enum W23Kind { Stick, Wmr, Vive, Simple, Hand }
        public enum W23RingHow { Stick, Twist, Point }

        /// <summary>One input on one controller: whether the controller has it, the ghost's part that acts it, the hand (-1 the left one,
        /// +1 the right one, 0 either or both), the word a line names it by (English: the Settings table's own word, found in the row
        /// <see cref="Row"/> of VR/ControllerProfiles.cs Buttons), the device's control that the game reads for it (null: none: a hand
        /// read from its joints, a button of the strip over the walking wrist: <see cref="Strip"/>).</summary>
        public struct W23Ctl { public bool Exists, Strip; public Part Part; public int Side; public string Word, Row, Device; }

        public static W23Kind W23KindOf(Profile p)
        {
            switch (p)
            {
                case Profile.Wmr: return W23Kind.Wmr;
                case Profile.Vive: return W23Kind.Vive;
                case Profile.KhrSimple: return W23Kind.Simple;
                case Profile.Hands: case Profile.MsftHands: case Profile.HandJoints: return W23Kind.Hand;
                default: return W23Kind.Stick; // (Index, the Touch family, Reverb G2, another controller, none known: as ControllerProfiles.Buttons)
            }
        }

        /// <summary>THE TABLE (item 10): input <paramref name="what"/> on a controller of profile <paramref name="p"/>.</summary>
        public static W23Ctl W23Control(W23In what, Profile p, bool leftHanded)
        {
            var k = W23KindOf(p); int main = leftHanded ? -1 : 1, off = -main;
            W23Ctl C(Part part, int side, string word, string row, string device, bool strip = false) => new W23Ctl { Exists = true, Part = part, Side = side, Word = word, Row = row, Device = device, Strip = strip };
            switch (what)
            {
                case W23In.MainTrigger: case W23In.OffTrigger: case W23In.AnyTrigger:
                {
                    // LokiInput.Hand.Trigger: trigger; the simple controller's select; a hand's pinch (aim-activate) or air tap (select)
                    int side = what == W23In.MainTrigger ? main : what == W23In.OffTrigger ? off : 0; string row = what == W23In.MainTrigger ? "Click, attack, talk, pick up" : null;
                    if (k == W23Kind.Simple) return C(Part.Trigger, side, "select", row, "select");
                    if (k == W23Kind.Hand) return C(Part.Body, side, p == Profile.MsftHands ? "air-tap" : "pinch", row, p == Profile.MsftHands ? "select" : p == Profile.Hands ? "pointerActivateValue" : null);
                    return C(Part.Trigger, side, "trigger", row, "trigger");
                }
                case W23In.MainGrip: case W23In.OffGrip: case W23In.AnyGrip: case W23In.BothGrips:
                {
                    // LokiInput.Hand.Grip: grip; a hand's grasp / squeeze (a fist); the simple controller has none
                    if (k == W23Kind.Simple) return default;
                    int side = what == W23In.MainGrip ? main : what == W23In.OffGrip ? off : 0; bool both = what == W23In.BothGrips;
                    string row = both ? "Zoom the table" : what == W23In.OffGrip ? "Move the table" : "Move or resize a menu board";
                    if (k == W23Kind.Hand) return C(Part.Body, side, both ? "fists" : "fist", row, p == Profile.MsftHands ? "squeeze" : p == Profile.Hands ? "graspValue" : null);
                    return C(Part.Grip, side, both ? "grips" : "grip", row, "grip");
                }
                case W23In.OffStick: case W23In.MainStick: case W23In.AnyStick:
                {
                    // LokiInput.Hand.StickValue: the stick; the Vive's trackpad pressed at its edge; none on the simple controller and hands
                    int side = what == W23In.MainStick ? main : what == W23In.OffStick ? off : 0; string row = what == W23In.MainStick ? "Turn" : "Walk";
                    if (k == W23Kind.Stick) return C(Part.Stick, side, "stick", row, "thumbstick");
                    if (k == W23Kind.Wmr) return C(Part.Stick, side, "thumbstick", row, "joystick");
                    if (k == W23Kind.Vive) return C(Part.Pad, side, "trackpad", row, "trackpad");
                    return default;
                }
                case W23In.ViewButton:
                    // LokiInput.Hand.StickClickHeld on the walking hand (held: the next view); the strip's "View" where there is no stick
                    if (k == W23Kind.Stick) return C(Part.Stick, off, "stick", "Recentre / switch view", "thumbstickClicked");
                    if (k == W23Kind.Wmr) return C(Part.Stick, off, "thumbstick", "Recentre / switch view", "joystickClicked");
                    if (k == W23Kind.Vive) return C(Part.Pad, off, "trackpad", "Recentre / switch view", "trackpadClicked");
                    if (k == W23Kind.Simple) return C(Part.Trigger, off, "wrist", "Minimap, NPC next / sit down, recentre", null, true);
                    return C(Part.Body, off, "wrist", "Recentre, switch view", null, true);
                case W23In.Menu:
                    // LokiInput.Hand.SecondaryDown on the walking hand: its upper face button; WMR: its touchpad; the Vive's and the simple
                    // controller's menu button; hands: the strip's "Menu"
                    if (k == W23Kind.Stick) return C(Part.B, off, p == Profile.Index ? "B" : leftHanded ? "B" : "Y", "Wrist menu", "secondaryButton");
                    if (k == W23Kind.Wmr) return C(Part.Pad, off, "touchpad", "Wrist menu", "touchpadClicked");
                    if (k == W23Kind.Hand) return C(Part.Body, off, "wrist", "Wrist menu, minimap, chat, back, next", null, true);
                    return C(Part.Menu, off, "menu button", "Wrist menu", "menu");
                case W23In.Settings:
                    // LokiInput.Hand.SettingsDown: the Touch family's left menu button, either of the Reverb G2's; no other controller has one
                    if (ControllerProfiles.IsTouch(p)) return C(Part.Menu, -1, "menu button", "Settings", "menu");
                    if (p == Profile.ReverbG2) return C(Part.Menu, 0, "menu button", "Settings", "menu");
                    return default;
            }
            return default;
        }

        /// <summary>The controller the lines are written for: the one in the hands (the pointing hand's first, as the Settings page:
        /// UI/ComfortWindows.cs Active); none known: the Index's, as the Settings page shows.</summary>
        public static Profile W23P
        {
            get
            {
                var p = ComfortWindow.Active(); return p == Profile.None ? Profile.Index : p;
            }
        }
        private static W23Kind W23K => W23KindOf(W23P);
        private static bool W23Hand => W23K == W23Kind.Hand;
        /// <summary>No stick of any kind (hands, the simple controller): the walking, turning and wheel lessons are passed.</summary>
        private static bool W23NoSticks => W23K == W23Kind.Hand || W23K == W23Kind.Simple;
        /// <summary>No grip (the simple controller): Resize mode, Reposition, the pad, the flick and the wand cannot be done.</summary>
        private static bool W23NoGrip => W23K == W23Kind.Simple;
        /// <summary>How the ring's icons are chosen now: by the wrist's roll ("Menu ring: Twist"), by the walking hand's stick, or (no
        /// stick) by pointing at one (UI/MenuRing.cs).</summary>
        private static W23RingHow W23Ring => Comfort.RingTwist ? W23RingHow.Twist : W23NoSticks ? W23RingHow.Point : W23RingHow.Stick;

        // the hand's word as the Settings table writes it: ~m~ beside a trigger, a stick, a grip, a controller, a wrist; ~f~ beside a hand
        private static string W23M(bool main) => main != Comfort.LeftHanded ? Lang.T("~m~right") : Lang.T("~m~left");
        private static string W23F(bool main) => main != Comfort.LeftHanded ? Lang.T("~f~right") : Lang.T("~f~left");

        private static W18TPhase W23U(W18TPhase ph, params W23In[] uses) { ph.Uses = uses; return ph; }
        /// <summary>A part with two lines: <paramref name="lead"/> while <paramref name="first"/> says so (what must be done before the
        /// part's own press can be: "look up until the menu appears", "open the ring again"), else its own. One check mark, at its press.</summary>
        private static W18TPhase W23Or(Func<bool> first, Func<string> lead, W18TPhase ph)
        {
            var own = ph.Body; ph.Alt = new[] { lead, own };
            ph.Body = () => first() ? lead() : own();
            return ph;
        }

        // ------------------------------------------------------------------ the lines
        /// <summary>The click of the pointing hand, as the end of a sentence: "pull your right trigger".</summary>
        private static string W23Pull()
        {
            switch (W23K)
            {
                case W23Kind.Simple: return Lang.F("press select on your {0} controller", W23M(true));
                case W23Kind.Hand: return W23P == Profile.MsftHands ? Lang.F("air-tap with your {0} hand", W23F(true)) : Lang.F("pinch with your {0} hand", W23F(true));
                default: return Lang.F("pull your {0} trigger", W23M(true));
            }
        }
        /// <summary>"Point at Start and pull your right trigger."</summary>
        private static string W23Press(string label) => Lang.F("Point at {0} and {1}.", label, W23Pull());
        /// <summary>"Point at On beside Resize mode and pull your right trigger." (a choice on a row of the menu above)</summary>
        private static string W23PressBeside(string label, string row) => Lang.F("Point at {0} beside {1} and {2}.", label, row, W23Pull());

        private static string W23StickForward()
        {
            string h = W23M(false);
            return W23K == W23Kind.Vive ? Lang.F("Press the top edge of your {0} trackpad.", h) : W23K == W23Kind.Wmr ? Lang.F("Push your {0} thumbstick forward.", h) : Lang.F("Push your {0} stick forward.", h);
        }
        private static string W23StickTurn()
        {
            string h = W23M(true);
            return W23K == W23Kind.Vive ? Lang.F("Press the left or right edge of your {0} trackpad.", h) : W23K == W23Kind.Wmr ? Lang.F("Push your {0} thumbstick left or right.", h) : Lang.F("Push your {0} stick left or right.", h);
        }
        private static string W23ViewName(ViewController.ViewMode m) => m == ViewController.ViewMode.Table ? Lang.T("Table") : m == ViewController.ViewMode.Follow ? Lang.T("~view~Follow") : Lang.T("First person");
        /// <summary>One change of view (VR/DioramaRig.cs: the walking hand's stick click held ViewHoldSeconds; the strip's "View").</summary>
        private static string W23ViewLine(string view)
        {
            string h = W23M(false);
            switch (W23K)
            {
                case W23Kind.Vive: return Lang.F("Press and hold your {0} trackpad's middle until the view is {1}.", h, view);
                case W23Kind.Wmr: return Lang.F("Click in your {0} thumbstick and hold until the view is {1}.", h, view);
                case W23Kind.Simple: return Lang.F("Press {0} over your {1} wrist with your laser for the {2} view.", Lang.T("View"), h, view);
                case W23Kind.Hand: return Lang.F("Pinch {0} over your {1} wrist for the {2} view.", Lang.T("View"), h, view);
                default: return Lang.F("Click in your {0} stick and hold until the view is {1}.", h, view);
            }
        }

        /// <summary>The menu button's press, as the start of a sentence: "Press B on your left controller".</summary>
        private static string W23MenuPress()
        {
            string h = W23M(false);
            switch (W23K)
            {
                case W23Kind.Wmr: return Lang.F("Press your {0} touchpad", h);
                case W23Kind.Vive: case W23Kind.Simple: return Lang.F("Press the menu button on your {0} controller", h);
                case W23Kind.Hand: return Lang.F("Pinch {0} over your {1} wrist", Lang.T("Menu"), h);
                default: return Lang.F("Press {0} on your {1} controller", W23Control(W23In.Menu, W23P, Comfort.LeftHanded).Word, h);
            }
        }
        private static string W23OpenRing() => Lang.F("{0} to open the menu ring.", W23MenuPress());
        private static string W23OpenMenu() => Lang.F("{0} to open the menu.", W23MenuPress());
        private static string W23CloseMenu() => Lang.F("{0} again to close it.", W23MenuPress());
        /// <summary>The Settings button, where a controller has one (the Touch family's left menu button; either of the Reverb G2's).</summary>
        private static string W23SettingsButton() => W23P == Profile.ReverbG2 ? Lang.T("Press the menu button on either controller to open Settings.") : Lang.T("Press the menu button on your left controller to open Settings.");

        private static string W23StickToAnIcon()
        {
            string h = W23M(false), c = Lang.T("Character");
            return W23K == W23Kind.Vive ? Lang.F("Press your {0} trackpad's edge toward the {1} icon.", h, c) : W23K == W23Kind.Wmr ? Lang.F("Push your {0} thumbstick toward the {1} icon.", h, c) : Lang.F("Push your {0} stick toward the {1} icon.", h, c);
        }
        private static string W23StickLetGo()
        {
            string h = W23M(false), c = Lang.T("Character");
            return W23K == W23Kind.Vive ? Lang.F("Let go of your {0} trackpad to open the {1} menu.", h, c) : W23K == W23Kind.Wmr ? Lang.F("Let go of your {0} thumbstick to open the {1} menu.", h, c) : Lang.F("Let go of your {0} stick to open the {1} menu.", h, c);
        }
        private static string W23StickToIcon(string icon)
        {
            string h = W23M(false);
            return W23K == W23Kind.Vive ? Lang.F("Press your {0} trackpad's edge toward the {1} icon, then let go.", h, icon)
                 : W23K == W23Kind.Wmr ? Lang.F("Push your {0} thumbstick toward the {1} icon, then let go.", h, icon) : Lang.F("Push your {0} stick toward the {1} icon, then let go.", h, icon);
        }
        private static string W23RollTo(string icon) => Lang.F("Roll your {0} wrist until {1} is on top.", W23M(false), icon);
        private static string W23OpenTop() => Lang.F("Then {0} to open the icon on top.", W23Pull());
        private static string W23PointIcon(string icon) => Lang.F("Point at the {0} icon and {1}.", icon, W23Pull());

        /// <summary>Item 13: the next thing to do on the way through the menu ring to the icon of <paramref name="group"/>: the ring shut,
        /// the menu button; open, the icon by the ring's own way (the stick toward it and let go; the wrist's roll, then the trigger;
        /// pointed at, then the trigger).</summary>
        private string W23RingLine(Func<string> icon, int group)
        {
            var ring = Hud != null && Hud.Wrist != null ? Hud.Wrist.Ring : null;
            if (ring == null || !ring.Shown) return W23OpenRing();
            switch (W23Ring)
            {
                case W23RingHow.Twist: return ring.Highlighted == group ? W23OpenTop() : W23RollTo(icon());
                case W23RingHow.Point: return W23PointIcon(icon());
                default: return W23StickToIcon(icon());
            }
        }
        /// <summary>Every line <see cref="W23RingLine"/> can show (the checks read them all).</summary>
        private static Func<string>[] W23RingLines(Func<string> icon)
        {
            switch (W23Ring)
            {
                case W23RingHow.Twist: return new Func<string>[] { W23OpenRing, () => W23RollTo(icon()), W23OpenTop };
                case W23RingHow.Point: return new Func<string>[] { W23OpenRing, () => W23PointIcon(icon()) };
                default: return new Func<string>[] { W23OpenRing, () => W23StickToIcon(icon()) };
            }
        }
        /// <summary>The input that opens a ring's icon now (the table's: the walking hand's stick, else the pointing hand's click).</summary>
        private static W23In W23RingInput => W23Ring == W23RingHow.Stick ? W23In.OffStick : W23In.MainTrigger;

        /// <summary>Item 13: the parts of the way through the ring to <paramref name="group"/>'s icon, one per press: the menu button (the
        /// ring is open), with "Menu ring: Twist" the roll (the icon is on top), the icon (<paramref name="there"/>). <paramref name="line"/>
        /// is what each shows: the next thing to do, whatever the player did before (a ring closed again: the menu button again).</summary>
        private void W23RingWay(W18TStep st, Func<string> icon, int group, Func<bool> there, Func<string> line)
        {
            bool RingShown() => Hud != null && Hud.Wrist != null && Hud.Wrist.Ring != null && Hud.Wrist.Ring.Shown;
            var all = W23RingLines(icon);
            W18TPhase P(Func<bool> done, params W23In[] uses) { var ph = W23U(W18TP(line, done), uses); ph.Alt = all; return ph; }
            st.Phases.Add(P(() => RingShown() || there(), W23In.Menu));
            if (W23Ring == W23RingHow.Twist) st.Phases.Add(P(() => there() || (RingShown() && Hud.Wrist.Ring.Highlighted == group))); // (the wrist's roll: no control)
            st.Phases.Add(P(there, W23RingInput));
        }

        private static string W23MoveMap()
        {
            string h = W23M(false);
            switch (W23K)
            {
                case W23Kind.Hand: return Lang.F("Make a fist with your {0} hand and move it to move the map.", W23F(false));
                case W23Kind.Vive: return Lang.F("Squeeze your {0} grip and press its trackpad's edge to move the map.", h);
                case W23Kind.Wmr: return Lang.F("Squeeze your {0} grip and push its thumbstick to move the map.", h);
                default: return W23P == Profile.Index ? Lang.F("Squeeze your {0} grip firmly and push its stick to move the map.", h) : Lang.F("Squeeze your {0} grip and push its stick to move the map.", h);
            }
        }
        private static string W23ResizeMap() => W23Hand ? Lang.T("Make two fists and move your hands apart to resize the map.")
            : W23P == Profile.Index ? Lang.T("Squeeze both grips firmly and move your hands apart to resize the map.") : Lang.T("Squeeze both grips and move your hands apart to resize the map.");
        private static string W23CarryMinimap() => W23Hand ? Lang.F("Make a fist with your {0} hand on the minimap, move it, let go.", W23F(true))
            : Lang.F("Point at the minimap, squeeze your {0} grip, move it, let go.", W23M(true));
        private static string W23GrabPanel() => W23Hand ? Lang.F("Point at the practice panel's edge and make a fist with your {0} hand.", W23F(true))
            : W23NoGrip ? Lang.F("Point at the practice panel's edge and hold select on your {0} controller.", W23M(true))
            : Lang.F("Point at the practice panel's edge and squeeze your {0} grip.", W23M(true));

        /// <summary>The casting gesture's start (VR/LokiInput.cs CastFrame: the grip squeezed while the index finger is off the trigger; a
        /// controller whose trigger has no touch sensor: the trigger not pulled; a tracked hand: its pointing pose), as the start of a sentence.</summary>
        private static string W23CastOpen() => W23Hand ? Lang.T("Point your index finger, curl the others")
            : ControllerProfiles.IsTouch(W23P) || W23P == Profile.Index ? Lang.T("Point your index finger, squeeze that hand's grip") : Lang.T("Finger off the trigger, squeeze that hand's grip");
        private static string W23PadLine() => W23Hand ? Lang.F("{0}, touch a slot, open your hand.", W23CastOpen()) : Lang.F("{0}, touch a slot, let go.", W23CastOpen());
        private static string W23FlickLine() => W23Hand ? Lang.F("{0}, flick twice, open your hand.", W23CastOpen()) : Lang.F("{0}, flick twice, let go.", W23CastOpen());
        private static string W23WandTake() => W23Hand ? Lang.T("Lower a hand to your hip and make a fist to take the wand.") : Lang.T("Lower a hand to your hip and squeeze its grip to take the wand.");
        private static string W23WandWheel(bool slices) => W23Hand
            ? (slices ? Lang.F("With the wand, {0}, point its tip at a slice, let go.", W23PinchHold()) : Lang.F("With the wand, {0}, draw two strokes, let go.", W23PinchHold()))
            : (slices ? Lang.T("Hold that hand's trigger, point the wand's tip at a slice, let go.") : Lang.T("Hold that hand's trigger, draw two strokes with the wand, let go."));
        /// <summary>A tracked hand's held click, inside a sentence (the Microsoft hand profile's is an air tap: VR/ControllerProfiles.cs Buttons).</summary>
        private static string W23PinchHold() => W23P == Profile.MsftHands ? Lang.T("air-tap and hold") : Lang.T("pinch and hold");
        private static string W23WandBack() => W23Hand ? Lang.T("Lower that hand to your hip and make a fist again.") : Lang.T("Lower that hand to your hip and squeeze its grip again.");
        private static string W23WheelPick() => W23K == W23Kind.Vive ? Lang.T("Keep holding and press either trackpad's edge toward a slot.")
            : W23K == W23Kind.Wmr ? Lang.T("Keep holding and push either thumbstick toward a slot.") : Lang.T("Keep holding and push either stick toward a slot.");

        // ------------------------------------------------------------------ item 5
        /// <summary>The guide's two buttons as they are now: "End tutorial" and "Skip step" are the laser's alone; whatever else stands
        /// there ("Enter the world", "Start", "Back", the calibration's "Cancel" / "Close") is the look's too.</summary>
        private void W23GuideLaserOnly(bool endTutorial, bool skipStep)
        {
            if (W18TGuide == null) return;
            if (W18TGuide.RightButton != null) W18TGuide.RightButton.LaserOnly = endTutorial;
            if (W18TGuide.LeftButton != null) W18TGuide.LeftButton.LaserOnly = skipStep && W23SkipStepLaserOnly;
        }
    }
}
