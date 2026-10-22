using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace Loki.VR
{
    /// <summary>
    /// Client wave 4 (w4vr): every OpenXR interaction profile the vendored OpenXR plugin offers for hands and controllers, how Loki
    /// reads each one, and the button table the Settings window's Controls page shows. Client wave 4b (w4xr): the plugin is now
    /// com.unity.xr.openxr 1.12.1 (client/Packages/local), which adds the Meta Quest Touch
    /// Plus profile (Quest 3's controllers); the other profiles' files are unchanged apart from comments (1.10.0 / 1.12.1 diffed).
    /// The profiles are enabled in the build by Editor/LokiSetup.cs (EnsureInteractionProfiles); each one registers an Input System
    /// layout (OpenXRLoader.InitializeInternal → OpenXRInput.RegisterLayouts) whose controls are listed in the plugin's
    /// Runtime/Features/Interactions/*.cs (1.12.1 line numbers):
    ///   Valve Index (ValveIndexControllerProfile.cs:51-249)      trigger (+ click), grip (+ force), thumbstick + click, A / B on both
    ///                                                            hands, trackpad (+ force, touch), system (SteamVR's own button)
    ///   Oculus Touch (OculusTouchControllerProfile.cs:50-223)    trigger, grip, thumbstick + click, X / Y left, A / B right, left menu,
    ///                                                            thumbrest touch; the right "menu" is /input/system/click (:273)
    ///   Meta Quest Touch Pro (MetaQuestTouchProControllerProfile.cs:50-280)  as Touch + trigger curl / slide / proximity, stylus
    ///   Meta Quest Touch Plus (MetaQuestTouchPlusControllerProfile.cs:50-259) as Touch + trigger force / curl / slide / proximity;
    ///                                                            needs the runtime's XR_META_touch_controller_plus (:376-383)
    ///   HP Reverb G2 (HPReverbG2ControllerProfile.cs:51)         trigger, grip, thumbstick + click, X / Y left, A / B right, menu
    ///   HTC Vive wand (HTCViveControllerProfile.cs:51)           trigger, grip (a button), menu, trackpad (position, click, touch)
    ///   Windows MR (MicrosoftMotionControllerProfile.cs:51)      trigger, grip, thumbstick + click, menu, touchpad (+ click)
    ///   Khronos simple (KHRSimpleControllerProfile.cs:45)        select and menu only (the fallback every runtime offers)
    ///   Hand interaction (HandInteractionProfile.cs:47)          XR_EXT_hand_interaction: aim pose, pinch / aim-activate, grasp
    ///   Microsoft hand interaction (MicrosoftHandInteraction.cs:46)  XR_MSFT_hand_interaction: select (air tap), squeeze
    /// Plus one profile that is not an OpenXR interaction profile: <see cref="Profile.HandJoints"/>, a hand tracked by XR Hands
    /// (com.unity.xr.hands 1.5.1, XR_EXT_hand_tracking; VR/HandJoints.cs) when no tracked OpenXR device is on that hand — Loki then
    /// reads the pinch, the fist and the pointing ray from its joints.
    /// The plugin has NO profile for Vive Cosmos / Vive Focus 3 / Pico controllers: those come with vendor packages (not approved).
    /// Whether a runtime drives such a controller through one of the profiles above is up to the runtime (for visual inspection).
    /// Built with the plugin's USE_STICK_CONTROL_THUMBSTICKS define (Editor/LokiSetup.cs): without it, the Index / Touch / Touch Pro /
    /// Touch Plus / Reverb G2 / Vive / WMR layouts cannot create a device at all (their FinishSetup asks for a StickControl the layout
    /// declares as a Vector2Control, e.g. ValveIndexControllerProfile.cs:18-22 against :231, MetaQuestTouchPlusControllerProfile.cs:17-21
    /// against :227) — found by the w4vr smoke; 1.12.1's project validation still lists the define as "[Optional]"
    /// (OpenXRProjectValidation.cs:185-199), so the plugin never sets it by itself.
    /// Not enabled on purpose: the plugin's "additive" features (Palm Pose, Hand Interaction Poses, D-Pad Binding: IsAdditive,
    /// OpenXRInput.cs:192-218) create a SECOND device per hand with the LeftHand / RightHand usage (PalmPoseInteraction.cs:50-51,
    /// HandCommonPosesInteraction.cs:50, DPadInteraction.cs:107-108), which the generic &lt;XRController&gt;{LeftHand} bindings would read
    /// together with the real controller; and Eye Gaze (EyeGazeInteraction.cs), because Loki aims with the head, not the eyes.
    /// </summary>
    public static class ControllerProfiles
    {
        /// <summary>w4xr: TouchPlus and HandJoints are appended (the other values stay as they were).</summary>
        public enum Profile { None, Index, Touch, TouchPro, ReverbG2, Vive, Wmr, KhrSimple, Hands, MsftHands, Other, TouchPlus, HandJoints }

        public sealed class Info
        {
            public Profile Id; public string Name; public string Layout; public string Feature; public string OpenXrPath;
        }

        /// <summary>The profiles Loki enables, in the order the Controls page lists them (Layout = the plugin's layout class name,
        /// Feature = the OpenXRInteractionFeature class LokiSetup enables).</summary>
        public static readonly Info[] All =
        {
            new Info { Id = Profile.Index, Name = "Valve Index", Layout = "ValveIndexController", Feature = "ValveIndexControllerProfile", OpenXrPath = "/interaction_profiles/valve/index_controller" },
            new Info { Id = Profile.Touch, Name = "Oculus / Meta Touch", Layout = "OculusTouchController", Feature = "OculusTouchControllerProfile", OpenXrPath = "/interaction_profiles/oculus/touch_controller" },
            new Info { Id = Profile.TouchPro, Name = "Meta Quest Touch Pro", Layout = "QuestProTouchController", Feature = "MetaQuestTouchProControllerProfile", OpenXrPath = "/interaction_profiles/facebook/touch_controller_pro" },
            // client wave 4b (w4xr): Quest 3's controllers (MetaQuestTouchPlusControllerProfile.cs:50, :264)
            new Info { Id = Profile.TouchPlus, Name = "Meta Quest Touch Plus", Layout = "QuestTouchPlusController", Feature = "MetaQuestTouchPlusControllerProfile", OpenXrPath = "/interaction_profiles/meta/touch_controller_plus" },
            new Info { Id = Profile.ReverbG2, Name = "HP Reverb G2", Layout = "ReverbG2Controller", Feature = "HPReverbG2ControllerProfile", OpenXrPath = "/interaction_profiles/hp/mixed_reality_controller" },
            new Info { Id = Profile.Vive, Name = "HTC Vive wand", Layout = "ViveController", Feature = "HTCViveControllerProfile", OpenXrPath = "/interaction_profiles/htc/vive_controller" },
            new Info { Id = Profile.Wmr, Name = "Windows Mixed Reality", Layout = "WMRSpatialController", Feature = "MicrosoftMotionControllerProfile", OpenXrPath = "/interaction_profiles/microsoft/motion_controller" },
            new Info { Id = Profile.KhrSimple, Name = "Simple controller (Khronos)", Layout = "KHRSimpleController", Feature = "KHRSimpleControllerProfile", OpenXrPath = "/interaction_profiles/khr/simple_controller" },
            new Info { Id = Profile.Hands, Name = "Hand tracking", Layout = "HandInteraction", Feature = "HandInteractionProfile", OpenXrPath = "/interaction_profiles/ext/hand_interaction_ext" },
            new Info { Id = Profile.MsftHands, Name = "Hand tracking (Microsoft)", Layout = "HoloLensHand", Feature = "MicrosoftHandInteraction", OpenXrPath = "/interaction_profiles/microsoft/hand_interaction" },
        };

        /// <summary>The OpenXRInteractionFeature class names LokiSetup enables (the build's OpenXR settings).</summary>
        public static IEnumerable<string> FeatureNames { get { foreach (var i in All) yield return i.Feature; } }

        public static Info Get(Profile p) { foreach (var i in All) if (i.Id == p) return i; return null; }
        public static string NameOf(Profile p) => p == Profile.None ? Lang.T("~controller~none") : p == Profile.Other ? Lang.T("another controller") : p == Profile.HandJoints ? Lang.T("Tracked hand (joints)") : Get(p)?.Name ?? p.ToString();

        /// <summary>The profile of an Input System device, by its layout (the plugin names each layout after its class).</summary>
        public static Profile Of(InputDevice d)
        {
            if (d == null) return Profile.None;
            string l = d.layout;
            foreach (var i in All) if (IsLayout(l, i.Layout)) return i.Id;
            return d is XRController ? Profile.Other : Profile.None;
        }

        private static bool IsLayout(string layout, string wanted)
        {
            if (string.Equals(layout, wanted, StringComparison.OrdinalIgnoreCase)) return true;
            try { return InputSystem.IsFirstLayoutBasedOnSecond(layout, wanted); } catch { return false; }
        }

        /// <summary>Profiles without the A / B (X / Y) face buttons: the Settings "button strip" over the walking wrist shows itself for them.</summary>
        public static bool LacksFaceButtons(Profile p) => p == Profile.Vive || p == Profile.KhrSimple || IsHand(p);
        /// <summary>Hands (no stick, no buttons): the table moves by grabbing it. w4xr: a hand read from its joints (XR Hands) too.</summary>
        public static bool IsHand(Profile p) => p == Profile.Hands || p == Profile.MsftHands || p == Profile.HandJoints;
        /// <summary>The Touch family: X / Y on the left, A / B on the right, a free menu button on the left (w4xr: + Touch Plus).</summary>
        public static bool IsTouch(Profile p) => p == Profile.Touch || p == Profile.TouchPro || p == Profile.TouchPlus;
        /// <summary>The face buttons' names on a controller: (lower, upper). Client wave 14 (w14cast): moved here from wave 12's
        /// Game/ItemButtons.cs (the item buttons are gone; the wand's button names still use it).</summary>
        public static (string lower, string upper) FaceNames(Profile p, bool leftHand)
        {
            if (p == Profile.Index) return ("A", "B");
            if (p == Profile.Vive || p == Profile.Wmr || p == Profile.KhrSimple || IsHand(p)) return ("1", "2");
            return leftHand ? ("X", "Y") : ("A", "B");
        }

        /// <summary>What the player does and on which input, per profile: the Controls page's table. <paramref name="leftHanded"/>
        /// swaps the hands (Comfort.LeftHanded: the laser / turning hand is "main", the walking / menu hand "off"). Every line is a
        /// whole sentence (phase 2 translates the UI). The same rules drive LokiInput.Hand, so the table is what the code does.
        /// Client wave 4b (w4lang): "how" is in the current language (Lang.F, the hand word agreeing with its noun: ~m~ for o gatilho /
        /// o analógico / o controle / o pulso, ~f~ for a mão); "action" stays the English name (Lang.N) — the tutorial looks actions up
        /// by it — and is translated where it is shown (Lang.T(action)).</summary>
        public static List<(string action, string how)> Buttons(Profile p, bool leftHanded)
        {
            string main = leftHanded ? Lang.T("~m~left") : Lang.T("~m~right"), off = leftHanded ? Lang.T("~m~right") : Lang.T("~m~left");
            string mainF = leftHanded ? Lang.T("~f~left") : Lang.T("~f~right"), offF = leftHanded ? Lang.T("~f~right") : Lang.T("~f~left");
            // face-button names by physical hand: Touch-style controllers have X / Y on the left and A / B on the right; Index has A / B on both
            string offA, offB, mainA, mainB;
            if (p == Profile.Index) { offA = mainA = "A"; offB = mainB = "B"; }
            else { offA = leftHanded ? "A" : "X"; offB = leftHanded ? "B" : "Y"; mainA = leftHanded ? "X" : "A"; mainB = leftHanded ? "Y" : "B"; }
            var t = new List<(string, string)>();
            switch (p)
            {
                case Profile.Index: case Profile.Touch: case Profile.TouchPro: case Profile.TouchPlus: case Profile.ReverbG2: case Profile.Other: case Profile.None:
                    t.Add((Lang.N("Click, attack, talk, pick up"), Lang.F("Pull the {0} trigger.", main)));
                    t.Add((Lang.N("Walk"), Lang.F("Push the {0} stick.", off)));
                    t.Add((Lang.N("Turn"), Lang.F("Push the {0} stick left or right.", main)));
                    t.Add((Lang.N("Face what the laser points at"), Lang.F("Click the {0} stick.", main)));
                    t.Add((Lang.N("Recentre / switch view"), Lang.F("Click the {0} stick. Hold: switch view.", off)));
                    // client wave 13 (w13view): the menu on the minimap's old button; the minimap on the menu above you; the freed lower button's
                    // hold (resize mode) and press (the microphone) in the last rows
                    t.Add((Lang.N("Wrist menu"), Lang.F("Press {0} on the {1} controller.", offB, off)));
                    t.Add((Lang.N("Minimap"), Lang.T("Look up: Minimap.")));
                    t.Add((Lang.N("NPC next / sit down"), Lang.F("Press {0} on the {1} controller.", mainA, main)));
                    t.Add((Lang.N("Cancel skill / stop / chat"), Lang.F("Press {0} on the {1} controller.", mainB, main)));
                    // the Index needs a firm squeeze to move or resize the table (LokiInput.Hand.TableGripHeld, specified 2026-10-01).
                    // Client wave 13 (w13resize): the table and the boards move only while RESIZE MODE is on (the overhead menu; design note,
                    // 2026-10-07), by a plain grip then; wave 12's full grip (grip + trigger + thumb) is gone
                    t.Add((Lang.N("Move the table"), p == Profile.Index ? Lang.F("Resize mode: squeeze the {0} grip firmly, push the {0} stick.", off)
                        : Lang.F("Resize mode: hold the {0} grip, push the {0} stick.", off)));
                    t.Add((Lang.N("Zoom the table"), p == Profile.Index ? Lang.T("Resize mode: squeeze both grips firmly, pull apart or together.")
                        : Lang.T("Resize mode: hold both grips, pull apart or together.")));
                    t.Add((Lang.N("Move or resize a menu board"), Lang.T("Grip its edge by hand or laser, or hold the trigger on it. Resize mode: both grips resize."))); // (client wave 13, w13grab: any window, by its edge; client wave 14, w14ui: the trigger too)
                    // client wave 12 (w12cast), parts B + C: the casting gesture's start (client wave 14, w14cast: the item buttons' row is gone
                    // with them: the design's answer 5)
                    t.Add((Lang.N("Cast from the quick bar"), IsTouch(p) || p == Profile.Index ? Lang.T("Grip while pointing your index finger.")
                        : Lang.T("Grip without touching the trigger.")));
                    if (IsTouch(p)) t.Add((Lang.N("Settings"), Lang.T("Press the menu button on the left controller.")));
                    else if (p == Profile.ReverbG2) t.Add((Lang.N("Settings"), Lang.T("Press the menu button on either controller.")));
                    else t.Add((Lang.N("Settings"), Lang.T("Open the menu, then Settings.")));
                    // client wave 12 (w12cast): the wand's sky rule (design note, 2026-10-06, 22:00). The last row: the smokes read the rows above by number
                    t.Add((Lang.N("Cancel a wand skill"), Lang.F("Press {0} on the {1} controller, or aim at the sky and pull the trigger.", mainB, main)));
                    // client wave 14 (w14cast), point 13 + the design's answer 10: the wand stays in the hand; a grip at the hip (or letting go there) puts it back
                    t.Add((Lang.N("Take or put away the wand"), Lang.T("Grip at your hip: take it or put it back.")));
                    // client wave 15 (w15cast), point 30: a firm squeeze anywhere puts it back
                    t.Add((Lang.N("Put the wand away"), Lang.T("Squeeze the grip hard, anywhere.")));

                    break;
                case Profile.Wmr:
                    t.Add((Lang.N("Click, attack, talk, pick up"), Lang.F("Pull the {0} trigger.", main)));
                    t.Add((Lang.N("Walk"), Lang.F("Push the {0} thumbstick.", off)));
                    t.Add((Lang.N("Turn"), Lang.F("Push the {0} thumbstick left or right.", main)));
                    t.Add((Lang.N("Face what the laser points at"), Lang.F("Click the {0} thumbstick.", main)));
                    t.Add((Lang.N("Recentre / switch view"), Lang.F("Click the {0} thumbstick. Hold: switch view.", off)));
                    t.Add((Lang.N("Wrist menu"), Lang.F("Press the {0} touchpad.", off))); // client wave 13 (w13view): the minimap's old button
                    t.Add((Lang.N("Minimap"), Lang.T("Look up: Minimap.")));
                    t.Add((Lang.N("NPC next / sit down"), Lang.F("Press the {0} touchpad.", main)));
                    t.Add((Lang.N("Cancel skill / stop / chat"), Lang.F("Press the menu button on the {0} controller.", main)));
                    t.Add((Lang.N("Move the table"), Lang.F("Resize mode: hold the {0} grip, push the {0} thumbstick.", off)));
                    t.Add((Lang.N("Zoom the table"), Lang.T("Resize mode: hold both grips, pull apart or together.")));
                    t.Add((Lang.N("Move or resize a menu board"), Lang.T("Grip its edge by hand or laser, or hold the trigger on it. Resize mode: both grips resize."))); // (client wave 13, w13grab: any window, by its edge; client wave 14, w14ui: the trigger too)
                    t.Add((Lang.N("Settings"), Lang.T("Open the menu, then Settings.")));
                    break;
                case Profile.Vive:
                    t.Add((Lang.N("Click, attack, talk, pick up"), Lang.F("Pull the {0} trigger.", main)));
                    t.Add((Lang.N("Walk"), Lang.F("Press the {0} trackpad's edge.", off)));
                    t.Add((Lang.N("Turn"), Lang.F("Press the {0} trackpad's left or right edge.", main)));
                    t.Add((Lang.N("Face what the laser points at"), Lang.F("Press the middle of the {0} trackpad.", main)));
                    t.Add((Lang.N("Recentre / switch view"), Lang.F("Press the {0} trackpad's middle. Hold: switch view.", off)));
                    t.Add((Lang.N("Wrist menu"), Lang.F("Press the menu button on the {0} controller.", off)));
                    t.Add((Lang.N("Cancel skill / stop / chat"), Lang.F("Press the menu button on the {0} controller.", main)));
                    t.Add((Lang.N("Minimap, NPC next / sit down"), Lang.F("Buttons over the {0} wrist.", off)));
                    t.Add((Lang.N("Move the table"), Lang.F("Resize mode: hold the {0} grip, press the {0} trackpad's edge.", off)));
                    t.Add((Lang.N("Zoom the table"), Lang.T("Resize mode: hold both grips, pull apart or together.")));
                    t.Add((Lang.N("Move or resize a menu board"), Lang.T("Grip its edge by hand or laser, or hold the trigger on it. Resize mode: both grips resize."))); // (client wave 13, w13grab: any window, by its edge; client wave 14, w14ui: the trigger too)
                    t.Add((Lang.N("Settings"), Lang.T("Open the menu, then Settings.")));
                    break;
                case Profile.KhrSimple:
                    t.Add((Lang.N("Click, attack, talk, pick up"), Lang.F("Press select on the {0} controller (laser or gaze).", main)));
                    t.Add((Lang.N("Walk"), Lang.T("Point the laser at the ground and press select.")));
                    t.Add((Lang.N("Wrist menu"), Lang.F("Press the menu button on the {0} controller.", off)));
                    t.Add((Lang.N("Cancel skill / stop / chat"), Lang.F("Press the menu button on the {0} controller.", main)));
                    t.Add((Lang.N("Minimap, NPC next / sit down, recentre"), Lang.F("Buttons over the {0} wrist.", off)));
                    t.Add((Lang.N("Turn, move and zoom the table"), Lang.T("Not on this controller: wrist menu, View.")));
                    t.Add((Lang.N("Settings"), Lang.T("Open the menu, then Settings.")));
                    break;
                case Profile.Hands: case Profile.MsftHands: case Profile.HandJoints:
                    t.Add((Lang.N("Click, attack, talk, pick up"), p == Profile.MsftHands ? Lang.F("The {0} hand's ray, then air-tap.", mainF) : Lang.F("The {0} hand's ray, then pinch.", mainF)));
                    t.Add((Lang.N("Walk"), Lang.T("Point the ray at the ground and pinch.")));
                    t.Add((Lang.N("Move the table"), Lang.F("Resize mode: make a fist with the {0} hand, move it.", offF)));
                    t.Add((Lang.N("Zoom the table"), Lang.T("Resize mode: two fists, pull apart or together.")));
                    t.Add((Lang.N("Move or resize a menu board"), Lang.T("Make a fist at its edge. Resize mode: two fists resize."))); // (client wave 13, w13grab)
                    if (p == Profile.HandJoints) t.Add((Lang.N("Cast from the quick bar"), Lang.T("Point your index finger, other fingers curled. Open the hand to choose.")));
                    t.Add((Lang.N("Wrist menu, minimap, chat, back, next"), Lang.F("Pinch the buttons over the {0} wrist.", off)));
                    t.Add((Lang.N("Recentre, switch view"), Lang.F("Pinch the buttons over the {0} wrist.", off)));
                    // w4xr: Meta's menu gesture (XR_FB_hand_tracking_aim MenuPressed, VR/HandJoints.cs) on the walking hand
                    if (p == Profile.HandJoints) t.Add((Lang.N("Wrist menu (Meta headsets)"), Lang.F("Turn the {0} palm towards you and pinch.", offF)));
                    t.Add((Lang.N("Settings"), Lang.T("Open the menu, then Settings.")));
                    break;
            }
            // client wave 13 (w13resize): the overhead menu (resize mode, the casting gesture), the one rule for the new casting, the menu
            // ring's buttons (the brief's fourteenth item). The last rows: the smokes read the rows above by number
            {
                bool hands = p == Profile.Hands || p == Profile.MsftHands || p == Profile.HandJoints;
                // (client wave 13, w13view: + Reposition, the minimap and the forearm bar on it; the shake ends Reposition too)
                t.Add((Lang.N("The menu above you"), hands
                    ? Lang.F("Look up for Resize mode, Reposition and more. Shake the {0} hand to end them.", mainF)
                    : Lang.F("Look up for Resize mode, Reposition and more. Shake the {0} controller to end them.", main)));
                t.Add((Lang.N("Casting and menus"), Lang.T("No casting with a menu open or in Resize mode.")));
                t.Add((Lang.N("Menu ring"), Lang.T("Trigger: open the lit icon. Menu button or B: close.")));
                // client wave 13 (w13view), the brief's eighteenth item: the button the menu left (the walking hand's lower face button; WMR: its
                // menu button): held, resize mode; pressed, the microphone. Profiles without one (Vive, the simple controller, hands): none
                if (p == Profile.Wmr) t.Add((Lang.N("Resize mode, microphone"), Lang.F("Menu button on the {0} controller: hold for Resize mode, press for mic on / off.", off)));
                else if (!hands && p != Profile.Vive && p != Profile.KhrSimple)
                    t.Add((Lang.N("Resize mode, microphone"), Lang.F("{0} on the {1} controller: hold for Resize mode, press for mic on / off.", offA, off)));
                // client wave 13 (w13grab), item 20: the backpack grab (after every older row: the smokes read those by number)
                if (p != Profile.KhrSimple) t.Add((Lang.N("Take out the bag"), hands ? Lang.T("Make a fist behind your shoulder.") : Lang.T("Grip behind your shoulder.")));
                // client wave 14 (w14laser), points 23, 11 and 10 (after every older row: the smokes read those by number): the Table view's free
                // grab, the Follow view's tilt, the walking hand's laser
                if (p != Profile.KhrSimple)
                {
                    t.Add((Lang.N("Table view"), hands ? Lang.T("Fists work without Resize mode.") : Lang.T("Grips work without Resize mode.")));
                    t.Add((Lang.N("Tilt the Follow view"), hands ? Lang.T("Resize mode: one fist, raise or lower it.") : Lang.T("Resize mode: one grip, raise or lower it.")));
                }
                t.Add((Lang.N("Second laser"), hands ? Lang.F("The {0} hand's ray presses panel buttons too.", offF) : Lang.F("The {0} laser presses panel buttons too.", off)));
                // client wave 15 (w15grab), point 5 (after every older row: the smokes read those by number): the holding hand's stick pushes / pulls a held panel
                if (!hands && p != Profile.KhrSimple)
                    t.Add((Lang.N("Held panel nearer / farther"), p == Profile.Vive ? Lang.T("Press that hand's trackpad top or bottom.") : Lang.T("Push that hand's stick up or down.")));
            }
            return t;
        }
    }
}
