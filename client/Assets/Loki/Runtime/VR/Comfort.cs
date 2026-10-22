using System;
using Loki.World;
using UnityEngine;
using UnityEngine.InputSystem.XR;

namespace Loki.VR
{
    /// <summary>
    /// Comfort, controls, audio and graphics settings (item 10; the Settings window, client wave 4), the same for every character:
    /// turning (smooth with a speed, or snap with an angle), the comfort vignette while the world moves in Follow / First person,
    /// seated play (a lower table), the table height, left-handed play (laser, turning and dialog buttons on the left hand; walking,
    /// wrist menu and minimap on the right), controller rumble, the forearm quick bar, volumes, how many skill effects other people's
    /// fights draw, the size of name tags, the render scale / anti-aliasing / shadows, the floating headsets and the first-launch tutorial.
    /// Client wave 4 (w4vr): kept per Windows user in loki-user.json (World/UserSettings.cs, <c>-lokiUserDir</c> in tests) instead of
    /// PlayerPrefs; until the first change is saved, the old PlayerPrefs values are read (never written, nothing is deleted from them).
    /// </summary>
    public static class Comfort
    {
        public static bool SnapTurn;
        public static int SnapAngle = 45;
        public static float TurnSpeed = 90f;
        public static bool Vignette = true;
        public static float VignetteStrength = 0.65f;
        /// <summary>Client wave 19 (w19world), point 48: the Follow view's "Character edge glow" (On by default), its strength (10-100 %, 50 %)
        /// and sensitivity (10-90 %, 40 %: it starts at 80 % of the way to the view's edge since client wave 25, w25misc, item 10; it was 60 %). VR/EdgeGlow.cs.</summary>
        public static bool EdgeGlow = true; public static float EdgeGlowStrength = EdgeGlowStrengthDefault, EdgeGlowSensitivity = EdgeGlowSensitivityDefault;
        public const float EdgeGlowStrengthDefault = 0.5f, EdgeGlowStrengthMin = 0.1f, EdgeGlowStrengthMax = 1f;
        public const float EdgeGlowSensitivityDefault = 0.4f, EdgeGlowSensitivityMin = 0.1f, EdgeGlowSensitivityMax = 0.9f;
        /// <summary>Knockbacks, jumps and fast moves of your own character in the Follow / first-person views: false = a quick
        /// slide (with a vignette flash), true = at once (client wave 3, w3move; EntityManager.SnapSelfSlides).</summary>
        public static bool SnapKnockback;
        public static bool Seated;
        /// <summary>Client wave 5 (w5weather): the rain's lightning flashes (roBrowser RainWeather.js: a white-blue flash over the whole
        /// view, 0.8 alpha, every 5-60 s). Off by default (the design's comfort rule: no full-view flashes or tints in a headset unless asked
        /// for); off, the thunder still sounds and the rain's grey veil stays as it is (World/Weather.cs Rain).</summary>
        public static bool LightningFlashes;
        public static float TableHeight = 0.95f;
        public static bool LeftHanded;
        public static bool Haptics = true;
        public static float HapticStrength = 0.6f;
        /// <summary>Client wave 13, item 4 (w13ring): each rumble kind's own switch and strength (0-1, a factor on the amplitude its calls
        /// use), by (int)<see cref="VR.Haptics.Kind"/>; the defaults (on, 1) are wave 12's rumble exactly. Settings → Rumble.</summary>
        public static readonly bool[] RumbleOn = NewRumbleOn();
        public static readonly float[] RumbleStrength = NewRumbleStrength();
        private static bool[] NewRumbleOn() { var a = new bool[VR.Haptics.KindCount]; for (int i = 0; i < a.Length; i++) a[i] = true; return a; }
        private static float[] NewRumbleStrength() { var a = new float[VR.Haptics.KindCount]; for (int i = 0; i < a.Length; i++) a[i] = 1f; return a; }
        public const float RumbleStep = 0.05f; // (client wave 15, w15menu, point 6: 5 % steps; wave 13: 10 %, whose values are on this grid too)
        /// <summary>Every kind back to on and 100 % (the Rumble page's "Defaults").</summary>
        public static void RumbleDefaults() { for (int i = 0; i < RumbleOn.Length; i++) { RumbleOn[i] = true; RumbleStrength[i] = 1f; } }
        /// <summary>Rumble when the laser moves onto a button or clicks one (w4vr): the Buttons kind's switch since client wave 13.</summary>
        public static bool UiRumble { get => RumbleOn[(int)VR.Haptics.Kind.Ui]; set => RumbleOn[(int)VR.Haptics.Kind.Ui] = value; }
        /// <summary>A heartbeat rumble while HP is at a quarter or less (w4vr, VR/VrFeedback.cs): the Low HP kind's switch since client wave 13.</summary>
        public static bool Heartbeat { get => RumbleOn[(int)VR.Haptics.Kind.LowHp]; set => RumbleOn[(int)VR.Haptics.Kind.LowHp] = value; }
        public static bool TutorialDone;
        /// <summary>The forearm quick bar is not "always off" (client wave 13, w13view: a property of <see cref="QuickBarMode"/> since the
        /// setting has three values; true sets "always on", false "always off").</summary>
        public static bool QuickBar { get => QuickBarMode != 1; set => QuickBarMode = value ? 0 : 1; }
        /// <summary>The laser leaves from the controller's aim pose (OpenXR /input/aim/pose) instead of its grip pose (w4vr).</summary>
        public static bool LaserFromAim = true;
        /// <summary>The button strip over the walking wrist: 0 automatic, 1 always, 2 never (w4vr, UI/QuickBar.cs ButtonStrip).</summary>
        public static int ButtonStrip;
        public static float MasterVolume = 1f, MusicVolume = 0.45f, EffectsVolume = 0.9f;
        /// <summary>Skill effects: 0 all, 1 fewer (other people's effects only near you, at most 2 copies), 2 quiet (other players' effects hidden). See EffectManager.Density.</summary>
        public static int EffectsLevel;
        /// <summary>Name tags, HP bars, chat bubbles and damage numbers above units (Overhead): a size factor.</summary>
        public static float NameScale = 1f;
        /// <summary>Graphics (w4vr): the eye texture scale, MSAA samples (-1 = the build's), shadows 0 off / 1 near / 2 far.</summary>
        public static float RenderScale = 1f;
        public static int Msaa = -1, Shadows = 2;
        /// <summary>w3presence's floating headsets: draw friends' headsets; send mine.</summary>
        public static bool ShowHeadsets = true, ShareHeadset = true;
        /// <summary>Client wave 18 (w18share), the design's point 33 and its addition: my casting menus shown to friends (Settings → Headsets
        /// "Casting menus shown"), to everyone or only to the relations switched on ("Seen by"; "Friends", "Family", "Guild": design note, "I meant my Guild").</summary>
        public static bool MenusShared = true, MenusEveryone = true, MenusFriends = true, MenusFamily = true, MenusGuild = true;
        /// <summary>Who sees my menus as the relay's audience bits (Net/GestureWire.cs ToEveryone ...); 0: nobody (nothing is sent).</summary>
        public static byte MenusAudience => (byte)(!MenusShared ? 0 : MenusEveryone ? 1 : (MenusFriends ? 2 : 0) | (MenusFamily ? 4 : 0) | (MenusGuild ? 8 : 0));
        /// <summary>Client wave 20 (w20pen), the design's point 57 and the design notes of 2026-10-10: who sees MY air pen drawings (Settings → Headsets
        /// "Drawings seen by": Everyone, or "Only these" and any of "Friends see mine", "Family sees mine", "Guild sees mine"; the default: friends
        /// only) and whose drawings I see ("Drawings I see": the same choices, "Friends' drawings" ...; the default: friends only).</summary>
        public static bool DrawSeenEveryone, DrawSeenFriends = true, DrawSeenFamily, DrawSeenGuild;
        public static bool DrawSeeEveryone, DrawSeeFriends = true, DrawSeeFamily, DrawSeeGuild;
        /// <summary>Who sees my drawings as the relay's audience bits (Net/GestureWire.cs ToEveryone ...); 0: nobody (nothing is sent).</summary>
        public static byte DrawAudience => (byte)(DrawSeenEveryone ? 1 : (DrawSeenFriends ? 2 : 0) | (DrawSeenFamily ? 4 : 0) | (DrawSeenGuild ? 8 : 0));
        /// <summary>Whose drawings I see, the same bits: a sender is drawn when this has Everyone, or shares a bit with what the relay says the
        /// sender is to me (UI/PenDrawings.cs Shows).</summary>
        public static byte DrawFilter => (byte)(DrawSeeEveryone ? 1 : (DrawSeeFriends ? 2 : 0) | (DrawSeeFamily ? 4 : 0) | (DrawSeeGuild ? 8 : 0));
        /// <summary>Client wave 5 (w5looks): skills' camera quakes (roBrowserLegacy Camera.setQuake: Fx/CameraQuake.cs) — off by default.</summary>
        public static bool CameraShake;
        /// <summary>Client wave 5 (w5looks): Hallucination turns our view upside down (roBrowserLegacy VerticalFlip: Fx/ScreenFlip.cs) — off by default.</summary>
        public static bool HallucinationFlip;
        /// <summary>Client wave 10 (w10move): Smooth (true, the default) or Classic movement drawing and stick (Sprites/Unit.Smooth.cs,
        /// Game/StickSteer.cs). Only what is drawn and what the stick asks for changes; the server walks in cells either way.</summary>
        public static bool SmoothMove = true;
        /// <summary>Client wave 11 (w11menu): the gaze ray's calibration (specified after the 2026-10-06 playtest: "a slider to calibrate where eye-gaze selection lands"). GazeSide: the ray starts at
        /// the left eye (-1), between the eyes (0, as before) or at the right eye (+1), so the correction holds at every distance;
        /// GazeUp: degrees the ray is turned up (+) or down (-); GazeDot: the dot where it lands is always shown (else only while
        /// Settings → Comfort is open). One ray for the buttons and for the world's gaze lock (VR/DioramaRig.Ui.cs GazeRay).</summary>
        public static float GazeSide, GazeUp; public static bool GazeDot;
        public const float GazeSideStep = 0.05f, GazeUpStep = 0.5f, GazeUpMax = 10f;
        /// <summary>Client wave 11 (w11menu): the Follow view's extra pitch in degrees (VR/ViewController.cs FollowExtraPitch), kept per player.</summary>
        public static float FollowPitch = 7f;
        /// <summary>Client wave 19 (w19view41), point 55: the view the player last chose (VR/ViewController.cs ViewMode as a number; -1 none
        /// yet: the first map opens in ViewController.DefaultMode), kept with the account (World/UserSettings.cs viewMode).</summary>
        public static int LastView = -1;
        /// <summary>Client wave 11 (w11menu), part 2: the menu's style, 0 new (the default: UI/MenuRing.cs, UI/MenuBoard.cs) or 1 classic
        /// (the wrist menu), and the board's size factor / distance / height as the player left them.</summary>
        public static int MenuStyle; public static float BoardScale = 1f, BoardDistance = 0.65f, BoardHeight = -0.10f;
        /// <summary>Client wave 22 (w22tree), the design's point 64 ("Keeping the current default flat display as a fallback"): Settings → Controls →
        /// Menus "Skill tree: 3D / Flat": the Skills page is the 3D skill tree in front of the player (true, the default) or the flat page
        /// (UI/WristMenu.Tree.cs). The tree stands where the boards do (BoardDistance / BoardHeight / BoardScale).</summary>
        public static bool SkillTree3D = true;
        /// <summary>Client wave 22 (w22tree), the design's point 64 (2026-10-11: "There should be two ways. One that spawns tress around you in a
        /// circle as they are unlocked ... The other opens a single tree in front of you and has forward and backwards button above it";
        /// ""Around you" is indeed meant to be the default"): Settings → Controls → Menus "Skill trees: Around you / One at a time"
        /// (UI/SkillTree3D.cs). With the account.</summary>
        public static bool SkillTreesAround = true;
        /// <summary>Client wave 23 (w23tree), item 24 (design note: "(Toggleable on the settings) While the skill trees are open, all other sound
        /// sources become muffled"): Settings -> Audio "Skill trees: muffle other sounds", On by default (Audio/LokiAudio.cs Muffled).</summary>
        public static bool SkillTreeMuffle = true;
        /// <summary>Client wave 11 (w11menu): the menu ring is worked by rolling the wrist (true) or by pointing (false, the default):
        /// specified on 2026-10-06 (UI/MenuRing.cs).</summary>
        public static bool RingTwist;
        /// <summary>Client wave 13 (w13items), item 11: B and X on a menu opened from another: 0 close it outright and forget the history (the
        /// default: design note, 2026-10-07: "Toggleable, with the default being 'close the menu outright'"), 1 back to the menu before (UI/MenuNav.cs).</summary>
        public static int MenuBack;
        /// <summary>Client wave 12 (w12cast): the casting gesture: 0 off, 1 pad (the default), 2 flick, 3 wand (LokiGame.W12Cast.cs), and
        /// "cast when taken" (a skill that needs no target, or a target skill with a unit already locked, is cast as it is taken).
        /// specified on 2026-10-06: "Build both as settings to compare"; the wand the same evening.</summary>
        public static int CastGesture = 1; public static bool CastOnTake = true;
        public static readonly string[] CastGestures = { "off", "pad", "flick", "wand" };
        /// <summary>Client wave 12 (w12cast): the wand holster's place as the player left it (VR/WandHolster.cs); WandPlaced false:
        /// the default place, on the pointing hand's side.</summary>
        public static bool WandPlaced; public static Vector3 WandOffset = new Vector3(0.22f, -0.60f, 0.05f);
        /// <summary>Client wave 12 (w12cast): the wand's quick cast: 0 off (the trigger draws the flick's two strokes), 1 strokes (the
        /// two-tier wheel of the design's sketch: its inner segments are four quick skills, its outer ones open the rows), 2 buttons
        /// (while the wand is held, A / the stick's click / stick up / down cast their own; the trigger draws the two strokes),
        /// 3 both (the default: the wheel and the buttons). specified on 2026-10-06, 20:35 and 21:40 (LokiGame.W12Wand.cs).</summary>
        public static int WandQuick = 3;
        /// <summary>Client wave 12 (w12cast): "Wand aim: wand / look" (design note, 2026-10-06, 20:50: "a toggle that makes it so I don't have
        /// to aim with the wand"): true = a skill taken with the wand is cast at once at what the look has.</summary>
        public static bool WandAimLook;
        /// <summary>Item 5: "Aim spells with vision" toggle (overhead menu + Settings → Controls → Casting): when true, spells target where the player looks across all casting methods.</summary>
        public static bool VisionAimSpells;
        /// <summary>Client wave 13 (w13resize), the brief's fifteenth item (design note, 2026-10-07: "for the pad, it should open not on a fixed
        /// position on the view, but rather on a fixed position around where the hand was when it was opened"): the side of one cell of
        /// the casting pad, in metres (Settings → Controls "Pad size"; UI/CastPad.cs). The default <see cref="PadCellDefault"/> is the
        /// flick's stroke (Game/FlickRecognizer.cs: 6 cm): a one-cell movement is the movement wave 12 already asks for one stroke.</summary>
        public static float PadCell = PadCellDefault;
        public const float PadCellDefault = 0.06f, PadCellMin = 0.04f, PadCellMax = 0.10f, PadCellStep = 0.001f; // (client wave 15, point 6: 1 mm; wave 13: 5 mm)
        // ---- client wave 13 (w13view): items 3, 6, 10 and 13 of design notes
        /// <summary>Item 3 (design note, 2026-10-07: "when the player holds the trigger, the display itself shows up in front of the vision ...
        /// This should be the default setting"): "Wand wheel shown: in front of you (true) / at the wand". WheelPlaced / WheelOffset /
        /// WheelScale: where the player put that picture with Reposition (the head's frame; false: UI/WandWheel.cs FrontDefault).</summary>
        public static bool WandWheelFront = true, WheelPlaced; public static Vector3 WheelOffset; public static float WheelScale = 1f;
        /// <summary>Item 10: the see-through minimap in front of the eyes (the overhead menu's "Minimap"): shown, its zoom step, and
        /// where the player put it with Reposition (the head's frame; false: UI/Minimap.cs DisplayDefault) and its size factor.</summary>
        public static bool MinimapOn, MinimapPlaced; public static int MinimapZoom; public static Vector3 MinimapOffset; public static float MinimapScale = 1f;
        /// <summary>Item 13 (design note: "cycles between always on, always off, and adding a button to the elbow that toggles it on and
        /// off"): the forearm quick bar: 0 always on (the default: what players had), 1 always off, 2 a button at the elbow; whether
        /// the bar is shown in that mode (the button toggles it); the elbow button's place along the forearm (Reposition).</summary>
        public static int QuickBarMode; public static bool ElbowBarOpen = true; public static float ElbowOffset;
        /// <summary>Client wave 13 (w13grab), item 19 (design note, 2026-10-07: "By default the panels cannot be rotated along the pitch or roll
        /// axis, but shaking a panel unlocks that rotation until it is closed. The default behavior can be toggled with the overhead menu"):
        /// "Panel tilt: upright (false, the default) / free": a held panel turns only about the vertical, or takes the hand's full rotation.
        /// A shaken panel gets the other one until it is closed (LokiGame.W13Grab.cs). The overhead menu and Settings → Controls.</summary>
        public static bool PanelTiltFree;
        public static readonly string[] QuickBarModes = { "always on", "always off", "elbow button" };
        public const float ElbowOffsetMin = -0.10f, ElbowOffsetMax = 0.12f;
        /// <summary>Client wave 14 (w14cast). Point 5: "Cast grip" (Settings → Controls): how firmly the casting grip is squeezed: 0 light
        /// (the default), 1 medium, 2 firm (the resize threshold, as before: VR/LokiInput.cs CastForceOn). Point 14 + the design's answer 11
        /// ("New replacing. Remember that this is meant to be a variant of the current one"): "Wand rows": 0 in place (the
        /// default: an outer segment REPLACES the main wheel with that row's wheel, the cursor back in its middle), 1 beside (wave 12's
        /// sub-wheel beside the main wheel). Point 32 ("Add a resize option for the wand too"): the wand's size, a factor (Settings
        /// slider; Reposition's two grips on the holster), within Reposition's limits.</summary>
        public static int CastGrip, WandRows; public static float WandScale = 1f;
        /// <summary>Client wave 21 (w21wand), design note (2026-10-11): "the maximum size is its current default size, and the minimum size makes it
        /// so that the tip of my finger is the casting tip much like the positioning of the flick casting method": the wand's size
        /// (<see cref="WandScale"/>) now runs from <see cref="WandMinScale"/> to <see cref="WandMaxScale"/> everywhere it is changed (resize
        /// mode's two grips on the wand: LokiGame.W21Wand.cs; the Settings slider; Reposition's two grips on the holster). The largest is the
        /// size the wand always had by default (1: VR/WandHolster.cs Length, 30 cm); wave 14's sizes above it (up to 160 %) are gone. The
        /// smallest is the size at which a controller's wand ends on the flick's fingertip point (LokiGame.W12Cast.cs W12Tip:
        /// <see cref="LokiGame.TipAhead"/> ahead of the grip pose, on the wand's own line): 10 / 30 cm. A tracked hand's fingertip is its
        /// index tip, off that line: VR/WandHolster.cs HeldPose leans the wand to it.</summary>
        public const float WandMaxScale = 1f, WandMinScale = LokiGame.TipAhead / WandHolster.Length;
        public static readonly string[] CastGrips = { "light", "medium", "firm" }, WandRowStyles = { "in place", "beside" };
        // client wave 14 (w14laser), point 20
        /// <summary>Client wave 14 (w14laser), point 20 (design note, 2026-10-07: "the walking with clicking should be toggleable ... a button on the floating
        /// menu above me that just disables the laser line when it comes to walking around"): "Laser walking: on (the default) / off". Off: the
        /// pointing hand's trigger on the ground walks nowhere and the laser is not drawn on the ground (VR/DioramaRig.cs; LokiGame.W14Laser.cs).</summary>
        public static bool LaserWalk = true;
        /// <summary>Client wave 15 (w15grab), point 8 (design note, 2026-10-08: "When a panel is grabbed, it is way too susceptible for me to be like,
        /// have shaky hands. Give it a little bit of smoothing"): "Panel smoothing", 0 off to 1 the most (Settings → Controls → Menus, a slider
        /// in 5 % steps); how a held panel's filter is tuned by it: LokiGame.W15Grab.cs. Default 0.5.</summary>
        public static float PanelSmoothing = PanelSmoothingDefault;
        public const float PanelSmoothingDefault = 0.5f;
        /// <summary>Client wave 18 (w18menu), point 6 (design note, 2026-10-09: "Double the maximum amount of panel smoothing on the slider"): the
        /// slider and the file go to 2 (200 %); the filter's time constants scale with the value (LokiGame.W15Grab.cs W15Place), so 200 %
        /// steadies twice as much as 100 %.</summary>
        public const float PanelSmoothingMax = 2f;
        // ---- client wave 18 (w18menu), point 2
        /// <summary>Client wave 18 (w18menu), point 2 (design note, 2026-10-09: "When rotating the wrist with the different submenus, require a toggle that
        /// allows them to slightly snap to the closest option ... This toggle should also be able to control the strength and range of the
        /// snapping"; answer: "on by default, medium strength also"): the twist ring's "Ring snap" On (the default) / Off; its strength (how much
        /// of the wrist's roll the selector ignores at the chosen icon's centre: 10-100 %, 50 % by default) and its range (how far from that
        /// centre the pull reaches, a share of the step between two icons: 10-50 %, 30 % by default, the middle). Both sliders in 1 % steps (wave 15's
        /// point 6: every number fine). UI/MenuRing.cs SnapPicture / LeaveBorder (client wave 19, point 49: the snap holds the choice too).</summary>
        public static bool RingSnap = true; public static float RingSnapStrength = RingSnapStrengthDefault, RingSnapRange = RingSnapRangeDefault;
        public const float RingSnapStrengthDefault = 0.5f, RingSnapStrengthMin = 0.1f, RingSnapStrengthMax = 1f;
        public const float RingSnapRangeDefault = 0.3f, RingSnapRangeMin = 0.1f, RingSnapRangeMax = 0.5f, RingSnapStep = FinePercent;
        // ---- client wave 18 (w18wand), point 14
        /// <summary>Client wave 18 (w18wand), point 14 (design note: "Provide the ability to control how many slices the ring menu has. Up to a maximum
        /// of eight." and "the default amount is still four"): how many slices the wand's INNER wheel has (its quick slots; the four outer
        /// row segments stay), Settings → Controls → Wand "Wand slices", <see cref="WandSlicesMin"/>-<see cref="WandSlicesMax"/>, default 4
        /// (Game/WandWheelGeo.cs divides the circle evenly, slice 1 at the top, clockwise).</summary>
        public static int WandSlices = WandSlicesDefault;
        public const int WandSlicesMin = 2, WandSlicesMax = World.UserSettings.MaxWandSlices, WandSlicesDefault = 4;
        // client wave 18 (w18flick)
        /// <summary>Client wave 18 (w18flick), point 7 (design note, 2026-10-09: "just Provide a slider that controls the duration ... and set the default
        /// duration to zero"): "Flick follow" (Settings → Controls → Casting), how long the flick's row middle follows the hand after the first
        /// stroke, in seconds: 0 (the default) to <see cref="FlickFollowMax"/> in <see cref="FlickFollowStep"/> steps (Game/FlickRecognizer.cs
        /// FollowSeconds; the wand's plain strokes use the same recogniser and setting).</summary>
        public static float FlickFollow;
        public const float FlickFollowMax = 0.30f, FlickFollowStep = 0.02f;
        /// <summary>Point 16 (design note: "if I slap a specific pad, it will cycle to a second one ... Slap should be a default feature of the pad
        /// system" and "a slider that controls how much force is needed"; then, after the wave: "Keep the slap as an alternate mode to swap. The
        /// default is still the palm method"): "Pad rows: Palm (the default) / Slap"; PadSlap = Slap mode on: the pointing hand's pad shows row 1
        /// of the bar, the other hand's row 2, and a slap of the pad's own hand switches it to row 3 / 4 and back (the palm picks no row then);
        /// Palm: wave 12's palm rows, no slap. "Slap strength" (shown in Slap mode only): the speed (m/s) the hand must push through the pad,
        /// away from the eyes, for a slap (LokiGame.W18Flick.cs).</summary>
        public static bool PadSlap; public static float SlapSpeed = SlapSpeedDefault;
        public const float SlapSpeedDefault = 0.8f, SlapSpeedMin = 0.4f, SlapSpeedMax = 2.0f, SlapSpeedStep = 0.05f;
        /// <summary>Point 15 (design note: "allow the player to reposition where the pad shows up relative to my hand when I activate it"): each hand's
        /// pad place as Reposition left it: the middle cell's offset from the fingertip in the pad's own frame at the fingertip (metres: right,
        /// up, away from the eyes) and its turn from facing the eyes (0 / identity: wave 13's place, the middle cell on the fingertip).
        /// Index 0 the left hand, 1 the right.</summary>
        public static readonly Vector3[] PadOffset = new Vector3[2];
        /// <summary>Point 32 (design note: "Just like the wand has the option to place the circle either in front of your view or where the tip of the
        /// wand is, do the same for the flick motion"): "Flick shown: In front of you (true) / At your hand (false, the default)": the flick's
        /// picture head-locked in front of the eyes, or standing where the gesture began (UI/FlickHud.cs BeginFront).</summary>
        public static bool FlickFront;
        /// <summary>Item 43 (design note): analog grip pressure threshold needed to open the flick menu (15%–95%, default 75%), and which hand
        /// can open the flick menu (0 Dominant, 1 Off-hand, 2 Both).</summary>
        public static float FlickGripThreshold = FlickGripThresholdDefault;
        public const float FlickGripThresholdDefault = 0.75f, FlickGripThresholdMin = 0.15f, FlickGripThresholdMax = 0.95f;
        public static int FlickHandMode = 2;
        public static readonly string[] FlickHandModes = { "dominant", "off-hand", "both" };
        /// <summary>Item 48 (design note): whether the radial skill wheels were chosen as the active casting method.</summary>
        public static bool SkillWheels;
        /// <summary>Client wave 21 (w21tools), design note (2026-10-10, THE PANEL THEME's scope): "Provide a toggle that makes them look like the current
        /// super single design again in case require it": "Casting look: Themed (false, the default) / Plain" (Settings → Controls → Casting):
        /// how the pad, the flick, the wand's wheel and the Quick Use Bubbles are drawn (UI/CastLook.cs). Kept with the account.</summary>
        public static bool CastPlain;
        /// <summary>Client wave 21 (w21jobui; design note, 2026-10-11: "You can either choose to have your panels be themed after the job (and their
        /// progression) that you are, or be the base panel UI"): "Panel theme: My job (0) / Base (1)"; -1: never chosen: the default
        /// (UI/JobTheme.cs DefaultMyJob, read through JobTheme.UseJob). With the account.</summary>
        public static int PanelTheme = -1;
        public static readonly Quaternion[] PadTurn = { Quaternion.identity, Quaternion.identity };
        public const float PadOffsetMax = 0.5f;
        /// <summary>The forearm quick bar is drawn now (by the setting alone: UI/QuickBar.cs adds the menus and the game's state).</summary>
        public static bool QuickBarShown => QuickBarMode == 0 || (QuickBarMode == 2 && ElbowBarOpen);
        /// <summary>Reposition's size factors (the minimap, the wand wheel's front picture) and the places a head-locked picture may
        /// take (metres in the head's frame: right / up / ahead).</summary>
        public const float PlaceMinScale = 0.5f, PlaceMaxScale = 1.6f;
        public static Vector3 ClampHeadPlace(Vector3 v) => new Vector3(Mathf.Clamp(v.x, -0.6f, 0.6f), Mathf.Clamp(v.y, -0.6f, 0.45f), Mathf.Clamp(v.z, 0.25f, 1.0f));
        /// <summary>Anything changed (the rig re-reads the table height, hands swap).</summary>
        public static event Action Changed;
        private static bool _loaded;
        /// <summary>Where the values came from at the last load: "file", "imported from PlayerPrefs" or "defaults" (smoke).</summary>
        public static string LoadedFrom { get; private set; } = "";

        /// <summary>Client wave 15 (w15menu), point 6 (design note: "Not an actual slider that I can customize in with higher granularity please make
        /// it so that I have more granularity"): the steps of the Settings sliders, over the same ranges as the lists below (their first and
        /// last values): snap angle 5°, turn speed 5°/s, percentages (volumes, vignette strength, wand size) 1 %, render scale, name tags,
        /// microphone gain, voice volume, Horde HUD size 5 %, the table's height 1 cm, the ring's wrist turn 1°; rumble <see cref="RumbleStep"/>,
        /// the pad <see cref="PadCellStep"/>. Every value saved before is on the new grid (the old lists' values are multiples of these steps).</summary>
        public const float FineSnapAngle = 5f, FineTurnSpeed = 5f, FinePercent = 0.01f, FineScale = 0.05f, FineTableHeight = 0.01f, FineRingTurn = 1f;
        public static readonly int[] SnapAngles = { 30, 45, 60, 90 };
        public static readonly float[] TurnSpeeds = { 45f, 60f, 90f, 120f, 180f };
        public static readonly float[] NameScales = { 0.8f, 1f, 1.25f, 1.5f, 2f };
        public static readonly string[] EffectsLevels = { "all", "fewer", "quiet" };
        public static readonly float[] HapticStrengths = { 0.2f, 0.4f, 0.6f, 0.8f, 1f };
        public static readonly float[] RenderScales = { 0.7f, 0.8f, 0.9f, 1f, 1.1f, 1.2f, 1.3f, 1.5f };
        public static readonly int[] MsaaLevels = { -1, 0, 2, 4, 8 };
        public static readonly string[] ShadowLevels = { "off", "near", "far" };
        public static readonly string[] ButtonStripModes = { "automatic", "always", "never" };

        /// <summary>Table height actually used: seated play lowers the table by 25 cm.</summary>
        public static float EffectiveTableHeight => Mathf.Clamp(TableHeight - (Seated ? 0.25f : 0f), 0.4f, 1.4f);

        public static void Load()
        {
            if (_loaded) return; _loaded = true;
            try
            {
                var s = UserSettings.Prefs;
                if (!s.saved) { ImportPlayerPrefs(s); LoadedFrom = "imported from PlayerPrefs"; }
                else LoadedFrom = "file";
                SnapTurn = s.snapTurn; SnapAngle = s.snapAngle; TurnSpeed = s.turnSpeed;
                Vignette = s.vignette; VignetteStrength = s.vignetteStrength; SnapKnockback = s.snapKnockback;
                // client wave 19 (w19world), point 48 (an older file: on, 50 %, 40 %)
                EdgeGlow = s.edgeGlow;
                EdgeGlowStrength = s.edgeGlowStrength < 0f ? EdgeGlowStrengthDefault : Mathf.Clamp(s.edgeGlowStrength, EdgeGlowStrengthMin, EdgeGlowStrengthMax);
                EdgeGlowSensitivity = s.edgeGlowSensitivity < 0f ? EdgeGlowSensitivityDefault : Mathf.Clamp(s.edgeGlowSensitivity, EdgeGlowSensitivityMin, EdgeGlowSensitivityMax);
                Seated = s.seated; TableHeight = s.tableHeight; TutorialDone = s.tutorialDone; LightningFlashes = s.lightningFlashes;
                LeftHanded = s.leftHanded; Haptics = s.haptics; HapticStrength = Mathf.Clamp(s.hapticStrength, 0.1f, 1f);
                UiRumble = s.uiRumble; Heartbeat = s.heartbeat; QuickBar = s.quickBar;
                LoadRumbleKinds(s); // client wave 13, item 4 (w13ring)
                LaserFromAim = s.laserPose == 0; ButtonStrip = Mathf.Clamp(s.buttonStrip, 0, 2);
                MasterVolume = Mathf.Clamp01(s.masterVolume); MusicVolume = Mathf.Clamp01(s.music); EffectsVolume = Mathf.Clamp01(s.effects);
                EffectsLevel = Mathf.Clamp(s.effectsLevel, 0, 2); NameScale = s.nameScale > 0 ? s.nameScale : 1f;
                RenderScale = s.renderScale > 0 ? Mathf.Clamp(s.renderScale, 0.5f, 2f) : 1f; Msaa = s.msaa; Shadows = Mathf.Clamp(s.shadows, 0, 2);
                ShowHeadsets = s.showHeadsets; ShareHeadset = s.shareHeadset;
                MenusShared = s.menusShared; MenusEveryone = s.menusEveryone; MenusFriends = s.menusFriends; MenusFamily = s.menusFamily; MenusGuild = s.menusGuild; // client wave 18 (w18share)
                DrawSeenEveryone = s.drawSeenEveryone; DrawSeenFriends = s.drawSeenFriends; DrawSeenFamily = s.drawSeenFamily; DrawSeenGuild = s.drawSeenGuild; // client wave 20 (w20pen)
                DrawSeeEveryone = s.drawSeeEveryone; DrawSeeFriends = s.drawSeeFriends; DrawSeeFamily = s.drawSeeFamily; DrawSeeGuild = s.drawSeeGuild;
                CameraShake = s.cameraShake; HallucinationFlip = s.hallucinationFlip; // w5looks
                SmoothMove = s.smoothMove; Sprites.Unit.Smooth = SmoothMove; // client wave 10 (w10move)
                GazeSide = Mathf.Clamp(s.gazeSide, -1f, 1f); GazeUp = Mathf.Clamp(s.gazeUp, -GazeUpMax, GazeUpMax); GazeDot = s.gazeDot; // client wave 11 (w11menu)
                FollowPitch = Mathf.Clamp(s.followPitch, -60f, 60f);
                LastView = s.viewMode >= 0 && s.viewMode <= 2 ? s.viewMode : -1; // client wave 19 (w19view41), point 55
                RingTwist = s.ringTwist;
                MenuBack = s.menuBack == 1 ? 1 : 0; // client wave 13 (w13items)
                CastGesture = Mathf.Clamp(s.castGesture, 0, 3); CastOnTake = s.castOnTake; WandQuick = Mathf.Clamp(s.wandQuick, 0, 3); WandAimLook = s.wandAimLook; VisionAimSpells = s.visionAimSpells; // client wave 12 (w12cast) + item 5
                WandPlaced = s.wandPlaced; WandOffset = new Vector3(Mathf.Clamp(s.wandX, -0.8f, 0.8f), Mathf.Clamp(s.wandY, -1.4f, 0.3f), Mathf.Clamp(s.wandZ, -0.6f, 0.8f));
                PadCell = Mathf.Clamp(s.padCell > 0f ? s.padCell : PadCellDefault, PadCellMin, PadCellMax); // client wave 13 (w13resize)
                // client wave 14 (w14menu), points 2 and 3: the wrist turn per step has its own key since wave 14 (ringTurn: wave 13's ringTwistStep,
                // 18° in every file that saved once, is left unread: the new default is far less wrist), the arch or the circle, icons still or sliding
                RingTwistStep = Mathf.Clamp(s.ringTurn > 0f ? s.ringTurn : RingTwistStepDefault, RingTwistStepMin, RingTwistStepMax);
                RingArch = s.ringArch; RingStill = s.ringStill; RingReversed = s.ringReversed; // (client wave 15, w15menu, point 1)
                OverheadPlaced = s.overheadPlaced; OverheadOffset = ClampOverheadPlace(new Vector3(s.overheadX, s.overheadY, s.overheadZ)); OverheadScale = Mathf.Clamp(s.overheadScale > 0f ? s.overheadScale : 1f, PlaceMinScale, PlaceMaxScale); // client wave 14 (w14menu), point 8
                // client wave 13 (w13view): the wand wheel's picture, the see-through minimap, the forearm bar's three ways (an older file:
                // quickBar's on / off, read above)
                WandWheelFront = s.wandWheelFront; WheelPlaced = s.wheelPlaced; WheelOffset = ClampHeadPlace(new Vector3(s.wheelX, s.wheelY, s.wheelZ)); WheelScale = Mathf.Clamp(s.wheelScale > 0f ? s.wheelScale : 1f, PlaceMinScale, PlaceMaxScale);
                MinimapOn = s.minimapOn; MinimapZoom = Mathf.Clamp(s.minimapZoom, 0, 2); MinimapPlaced = s.minimapPlaced; MinimapOffset = ClampHeadPlace(new Vector3(s.minimapX, s.minimapY, s.minimapZ)); MinimapScale = Mathf.Clamp(s.minimapScale > 0f ? s.minimapScale : 1f, PlaceMinScale, PlaceMaxScale);
                if (s.quickBarMode >= 0) QuickBarMode = Mathf.Clamp(s.quickBarMode, 0, 2);
                ElbowBarOpen = s.elbowBarOpen; ElbowOffset = Mathf.Clamp(s.elbowOffset, ElbowOffsetMin, ElbowOffsetMax);
                PanelTiltFree = s.panelTiltFree; // client wave 13 (w13grab), item 19
                CastGrip = Mathf.Clamp(s.castGrip, 0, 2); WandRows = s.wandRows == 1 ? 1 : 0; WandScale = Mathf.Clamp(s.wandScale > 0f ? s.wandScale : 1f, WandMinScale, WandMaxScale); // client wave 14 (w14cast); client wave 21 (w21wand): the wand's own limits (a size above the default saved before: the default)
                LaserWalk = s.laserWalk; // client wave 14 (w14laser), point 20
                PanelSmoothing = s.panelSmoothing < 0f ? PanelSmoothingDefault : Mathf.Clamp(s.panelSmoothing, 0f, PanelSmoothingMax); // client wave 15 (w15grab), point 8; client wave 18 (w18menu), point 6: to 200 %
                // client wave 18 (w18menu), point 2: the ring snap (an older file: on, 50 %, 30 %)
                RingSnap = s.ringSnap;
                RingSnapStrength = s.ringSnapStrength < 0f ? RingSnapStrengthDefault : Mathf.Clamp(s.ringSnapStrength, RingSnapStrengthMin, RingSnapStrengthMax);
                RingSnapRange = s.ringSnapRange < 0f ? RingSnapRangeDefault : Mathf.Clamp(s.ringSnapRange, RingSnapRangeMin, RingSnapRangeMax);
                WandSlices = s.wandSlices <= 0 ? WandSlicesDefault : Mathf.Clamp(s.wandSlices, WandSlicesMin, WandSlicesMax); // client wave 18 (w18wand), point 14
                // client wave 18 (w18flick): points 7, 16 and 15 (an older file: follow 0, slap on at the default speed, the pads at the fingertip)
                FlickFollow = Mathf.Clamp(s.flickFollow, 0f, FlickFollowMax); PadSlap = s.padRows == 1; SlapSpeed = Mathf.Clamp(s.slapSpeed > 0f ? s.slapSpeed : SlapSpeedDefault, SlapSpeedMin, SlapSpeedMax);
                LoadPadPlace(0, s.padLeft); LoadPadPlace(1, s.padRight); FlickFront = s.flickFront;
                FlickGripThreshold = s.flickGripThreshold > 0f ? Mathf.Clamp(s.flickGripThreshold, FlickGripThresholdMin, FlickGripThresholdMax) : FlickGripThresholdDefault;
                FlickHandMode = Mathf.Clamp(s.flickHandMode, 0, 2);
                SkillWheels = s.skillWheels;
                CastPlain = s.castPlain; // client wave 21 (w21tools): "Casting look" (an older file: Themed)
                PanelTheme = s.panelTheme == 0 || s.panelTheme == 1 ? s.panelTheme : -1; // client wave 21 (w21jobui)
                SkillTree3D = !s.skillTreeFlat; SkillTreesAround = !s.skillTreesOne; // client wave 22 (w22tree), point 64
                SkillTreeMuffle = !s.skillTreeLoud; // client wave 23 (w23tree), item 24
                MenuStyle = s.menuStyle == 1 ? 1 : 0; BoardScale = Mathf.Clamp(s.boardScale, 0.5f, 2f); BoardDistance = Mathf.Clamp(s.boardDistance, 0.3f, 1.6f); BoardHeight = Mathf.Clamp(s.boardHeight, -0.9f, 0.6f);
                Audio.LokiAudio.MonsterSounds = Mathf.Clamp(s.monsterSounds, 0, 2); // client wave 9 (h9feel): Settings → Voice "Monster sounds"
                // the import stays in memory until a setting is changed (Save): starting the game never rewrites loki-user.json by itself,
                // and PlayerPrefs are left as they were
            }
            catch (Exception e) { Debug.LogWarning("[Comfort] load: " + e.Message); }
            ApplyAudio(); ApplyGraphics();
            Debug.Log($"[Comfort] settings {LoadedFrom} ({UserSettings.FilePath}): rumble {(Haptics ? Mathf.RoundToInt(HapticStrength * 100) + " %" : "off")}, left-handed {LeftHanded}, render scale {RenderScale:0.0#}, msaa {Msaa}, shadows {ShadowLevels[Shadows]}");
        }

        /// <summary>The settings as they were before wave 4 (PlayerPrefs keys "loki.*"): read only, never deleted.</summary>
        private static void ImportPlayerPrefs(UserSettings.Settings s)
        {
            s.snapTurn = PlayerPrefs.GetInt("loki.snap", 0) != 0;
            s.snapAngle = PlayerPrefs.GetInt("loki.snapAngle", 45);
            s.turnSpeed = PlayerPrefs.GetFloat("loki.turnSpeed", 90f);
            s.vignette = PlayerPrefs.GetInt("loki.vignette", 1) != 0;
            s.vignetteStrength = PlayerPrefs.GetFloat("loki.vignetteStrength", 0.65f);
            s.snapKnockback = PlayerPrefs.GetInt("loki.snapKnockback", 0) != 0;
            s.seated = PlayerPrefs.GetInt("loki.seated", 0) != 0;
            s.tableHeight = PlayerPrefs.GetFloat("loki.tableHeight", 0.95f);
            s.leftHanded = PlayerPrefs.GetInt("loki.leftHanded", 0) != 0;
            s.haptics = PlayerPrefs.GetInt("loki.haptics", 1) != 0;
            s.hapticStrength = PlayerPrefs.GetFloat("loki.hapticStrength", 0.6f);
            s.tutorialDone = PlayerPrefs.GetInt("loki.tutorialDone", 0) != 0;
            s.quickBar = PlayerPrefs.GetInt("loki.quickBar", 1) != 0;
            s.music = PlayerPrefs.GetFloat("loki.music", 0.45f); s.effects = PlayerPrefs.GetFloat("loki.sfx", 0.9f);
            s.effectsLevel = Mathf.Clamp(PlayerPrefs.GetInt("loki.effects", 0), 0, 2); s.nameScale = PlayerPrefs.GetFloat("loki.nameScale", 1f);
            s.showHeadsets = PlayerPrefs.GetInt("loki.presence.show", 1) != 0; s.shareHeadset = PlayerPrefs.GetInt("loki.presence.share", 1) != 0;
        }

        public static void Save()
        {
            var s = UserSettings.Prefs;
            s.snapTurn = SnapTurn; s.snapAngle = SnapAngle; s.turnSpeed = TurnSpeed;
            s.vignette = Vignette; s.vignetteStrength = VignetteStrength; s.snapKnockback = SnapKnockback;
            s.edgeGlow = EdgeGlow; s.edgeGlowStrength = EdgeGlowStrength; s.edgeGlowSensitivity = EdgeGlowSensitivity; // client wave 19 (w19world), point 48
            s.seated = Seated; s.tableHeight = TableHeight; s.tutorialDone = TutorialDone; s.lightningFlashes = LightningFlashes;
            s.leftHanded = LeftHanded; s.haptics = Haptics; s.hapticStrength = HapticStrength;
            s.uiRumble = UiRumble; s.heartbeat = Heartbeat; s.quickBar = QuickBar;
            s.laserPose = LaserFromAim ? 0 : 1; s.buttonStrip = ButtonStrip;
            s.masterVolume = MasterVolume; s.music = MusicVolume; s.effects = EffectsVolume;
            s.effectsLevel = EffectsLevel; s.nameScale = NameScale;
            s.renderScale = RenderScale; s.msaa = Msaa; s.shadows = Shadows;
            s.showHeadsets = ShowHeadsets; s.shareHeadset = ShareHeadset;
            s.menusShared = MenusShared; s.menusEveryone = MenusEveryone; s.menusFriends = MenusFriends; s.menusFamily = MenusFamily; s.menusGuild = MenusGuild; // client wave 18 (w18share)
            s.drawSeenEveryone = DrawSeenEveryone; s.drawSeenFriends = DrawSeenFriends; s.drawSeenFamily = DrawSeenFamily; s.drawSeenGuild = DrawSeenGuild; // client wave 20 (w20pen)
            s.drawSeeEveryone = DrawSeeEveryone; s.drawSeeFriends = DrawSeeFriends; s.drawSeeFamily = DrawSeeFamily; s.drawSeeGuild = DrawSeeGuild;
            s.cameraShake = CameraShake; s.hallucinationFlip = HallucinationFlip; // w5looks
            s.smoothMove = SmoothMove; Sprites.Unit.Smooth = SmoothMove; // client wave 10 (w10move): the units draw by it from the next frame (a walk in progress eases over, no jump)
            s.monsterSounds = Audio.LokiAudio.MonsterSounds; // client wave 9 (h9feel)
            s.gazeSide = GazeSide; s.gazeUp = GazeUp; s.gazeDot = GazeDot; s.followPitch = FollowPitch; // client wave 11 (w11menu)
            s.viewMode = LastView; // client wave 19 (w19view41), point 55
            s.skillTreeFlat = !SkillTree3D; s.skillTreesOne = !SkillTreesAround; // client wave 22 (w22tree), point 64
            s.skillTreeLoud = !SkillTreeMuffle; // client wave 23 (w23tree), item 24
            s.menuStyle = MenuStyle; s.boardScale = BoardScale; s.boardDistance = BoardDistance; s.boardHeight = BoardHeight; s.ringTwist = RingTwist;
            s.menuBack = MenuBack; // client wave 13 (w13items)
            s.castGesture = CastGesture; s.castOnTake = CastOnTake; s.wandQuick = WandQuick; s.wandAimLook = WandAimLook; s.visionAimSpells = VisionAimSpells; s.wandPlaced = WandPlaced; s.wandX = WandOffset.x; s.wandY = WandOffset.y; s.wandZ = WandOffset.z; // client wave 12 (w12cast) + item 5
            s.padCell = PadCell; // client wave 13 (w13resize)
            SaveRumbleKinds(s); // client wave 13, item 4 (w13ring)
            s.ringTurn = RingTwistStep; s.ringArch = RingArch; s.ringStill = RingStill; // client wave 14 (w14menu), points 2 and 3
            s.ringReversed = RingReversed; // client wave 15 (w15menu), point 1
            s.overheadPlaced = OverheadPlaced; s.overheadX = OverheadOffset.x; s.overheadY = OverheadOffset.y; s.overheadZ = OverheadOffset.z; s.overheadScale = OverheadScale; // point 8
            s.wandWheelFront = WandWheelFront; s.wheelPlaced = WheelPlaced; s.wheelX = WheelOffset.x; s.wheelY = WheelOffset.y; s.wheelZ = WheelOffset.z; s.wheelScale = WheelScale; // client wave 13 (w13view)
            s.minimapOn = MinimapOn; s.minimapZoom = MinimapZoom; s.minimapPlaced = MinimapPlaced; s.minimapX = MinimapOffset.x; s.minimapY = MinimapOffset.y; s.minimapZ = MinimapOffset.z; s.minimapScale = MinimapScale;
            s.quickBarMode = QuickBarMode; s.elbowBarOpen = ElbowBarOpen; s.elbowOffset = ElbowOffset;
            s.panelTiltFree = PanelTiltFree; // client wave 13 (w13grab), item 19
            s.castGrip = CastGrip; s.wandRows = WandRows; s.wandScale = WandScale; // client wave 14 (w14cast)
            s.laserWalk = LaserWalk; // client wave 14 (w14laser), point 20
            s.panelSmoothing = PanelSmoothing; // client wave 15 (w15grab), point 8
            s.ringSnap = RingSnap; s.ringSnapStrength = RingSnapStrength; s.ringSnapRange = RingSnapRange; // client wave 18 (w18menu), point 2
            s.wandSlices = WandSlices; // client wave 18 (w18wand), point 14
            s.flickFollow = FlickFollow; s.padRows = PadSlap ? 1 : 0; s.slapSpeed = SlapSpeed; s.padLeft = SavePadPlace(0); s.padRight = SavePadPlace(1); s.flickFront = FlickFront; // client wave 18 (w18flick)
            s.flickGripThreshold = FlickGripThreshold; s.flickHandMode = FlickHandMode; s.skillWheels = SkillWheels;
            s.castPlain = CastPlain; // client wave 21 (w21tools)
            s.panelTheme = PanelTheme; // client wave 21 (w21jobui)
            s.saved = true;
            UserSettings.Save();
            ApplyAudio(); ApplyGraphics();
            Changed?.Invoke();
        }

        /// <summary>Client wave 18 (w18flick), point 15: one hand's pad place from the file (clamped; a broken turn: none).</summary>
        private static void LoadPadPlace(int hand, UserSettings.PadPlace pp)
        {
            if (pp == null) { PadOffset[hand] = Vector3.zero; PadTurn[hand] = Quaternion.identity; return; }
            PadOffset[hand] = new Vector3(Mathf.Clamp(pp.x, -PadOffsetMax, PadOffsetMax), Mathf.Clamp(pp.y, -PadOffsetMax, PadOffsetMax), Mathf.Clamp(pp.z, -PadOffsetMax, PadOffsetMax));
            var q = new Quaternion(pp.qx, pp.qy, pp.qz, pp.qw); float n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            PadTurn[hand] = n > 0.5f && n < 1.5f ? new Quaternion(q.x / n, q.y / n, q.z / n, q.w / n) : Quaternion.identity;
        }
        private static UserSettings.PadPlace SavePadPlace(int hand) { var o = PadOffset[hand]; var q = PadTurn[hand]; return new UserSettings.PadPlace { x = o.x, y = o.y, z = o.z, qx = q.x, qy = q.y, qz = q.z, qw = q.w }; }

        /// <summary>Client wave 13, item 4: the kinds' switches and strengths from the file, by kind name (a kind the file does not name
        /// keeps on / 100 %; uiRumble and heartbeat, older than the kinds, have been read into Ui / LowHp just before).</summary>
        private static void LoadRumbleKinds(UserSettings.Settings s)
        {
            for (int i = 0; i < RumbleStrength.Length; i++) RumbleStrength[i] = 1f;
            if (s.rumbleKinds == null) return;
            foreach (var k in s.rumbleKinds)
            {
                if (k == null || string.IsNullOrEmpty(k.kind) || !Enum.TryParse(k.kind, out VR.Haptics.Kind kind)) continue;
                RumbleOn[(int)kind] = k.on; RumbleStrength[(int)kind] = Mathf.Clamp01(Mathf.Round(k.strength / RumbleStep) * RumbleStep);
            }
        }
        private static void SaveRumbleKinds(UserSettings.Settings s)
        {
            var l = new System.Collections.Generic.List<UserSettings.RumbleKind>();
            foreach (var kind in VR.Haptics.Named) l.Add(new UserSettings.RumbleKind { kind = kind.ToString(), on = RumbleOn[(int)kind], strength = RumbleStrength[(int)kind] });
            s.rumbleKinds = l.ToArray();
        }

        /// <summary>Client wave 13, item 9 (w13ring): the menu ring's twist mode: degrees the wrist rolls for one step (UI/MenuRing.cs).
        /// Client wave 14 (w14menu), point 2 (design note: "I also have to rotate my wrist way too much to reach the ends of each side") and
        /// answer 3 ("Provide a "rotation" sensitivity slider on the settings"): Settings → Controls → Menus "Rotation sensitivity", a
        /// slider over <see cref="RingTurnSteps"/> (more sensitive to the right: fewer degrees a step); the default 10° (wave 13: 18°),
        /// so the farthest group (4 steps from Settings) is 40° of wrist (wave 13: 72°).</summary>
        public static float RingTwistStep = RingTwistStepDefault;
        public const float RingTwistStepDefault = 10f, RingTwistStepMin = 6f, RingTwistStepMax = 30f;
        /// <summary>The sensitivity slider's values, degrees of wrist per step, from the least sensitive to the most.</summary>
        public static readonly float[] RingTurnSteps = { 30f, 24f, 20f, 18f, 16f, 14f, 12f, 10f, 9f, 8f, 7f, 6f };
        /// <summary>Client wave 14 (w14menu), answer 3 ("Provide the option to choose between the circle and arch configurations"): twist
        /// mode's shape, the arch over the hand (true) or the circle (false, the default: design note item 28). Point 3 ("the icon stays
        /// stationary. It's just that the selector rotates with my wrist"): the icons stay still and a selector moves (true, the default
        /// chosen by w14menu: every icon in one place, all eight seen at once) or the icons slide under a fixed marker (wave 13's way).</summary>
        public static bool RingArch = false, RingStill = true;
        /// <summary>Client wave 15 (w15menu), point 1 (design note: "On the arch wrist rotation mode, the rotation is inverted", then: "just make it a
        /// setting that you can toggle the direction"): Settings → Controls → Menus "Ring direction: Normal / Reversed", for the arch and the
        /// circle. Normal is wave 14's way (the hand's top rolling left moves the choice right); Reversed (true, the default, the orchestrator's
        /// proposal: playtesting found Normal inverted) moves it the way the hand's top rolls.</summary>
        public static bool RingReversed = true;
        /// <summary>Client wave 14 (w14menu), point 8 (design note: "While in reposition mode, I should also be able to move the menu that floats
        /// above me"): where Reposition left the overhead menu, in the BODY's frame from the eyes (metres: right, up, ahead; false: its
        /// default, UI/OverheadMenu.cs Elevation / Distance) and its size factor.</summary>
        public static bool OverheadPlaced; public static Vector3 OverheadOffset; public static float OverheadScale = 1f;
        /// <summary>The places the overhead menu may take: 0.3 to 1.2 m from the eyes, within 0.8 m to either side, 0.6 m below to 1.0 m above.</summary>
        public static Vector3 ClampOverheadPlace(Vector3 v)
        {
            v = new Vector3(Mathf.Clamp(v.x, -0.8f, 0.8f), Mathf.Clamp(v.y, -0.6f, 1.0f), Mathf.Clamp(v.z, -0.3f, 1.2f));
            float m = v.magnitude; if (m < 0.3f) v = m > 1e-4f ? v * (0.3f / m) : new Vector3(0f, 0.3f, 0f); else if (m > 1.2f) v *= 1.2f / m;
            return v;
        }

        /// <summary>Tests: forget what was loaded and read the file again (after UserSettings.Reload()).</summary>
        public static void Reload() { _loaded = false; Load(); }
        /// <summary>Client wave 18 (w18acct): the account's settings came in (UserSettings.ApplyAccountJson): every value read again from
        /// them, then <see cref="Changed"/> (the rig re-reads the table height and the hands, the Settings window redraws, the menu
        /// takes its style).</summary>
        public static void Reapply() { _loaded = false; Load(); Changed?.Invoke(); }

        /// <summary>design note, 2026-10-05: test clients must never play at full volume on the PC ("set them all to 1 or 0"). Set by
        /// -lokiMute 1 (any build) and by every -lokiSmoke 1 run: the listener stays at 0 whatever the saved master volume.</summary>
        public static bool Muted;
        /// <summary>What a muted instance plays at: 60 dB down, not 0, because the voice smoke measures the left / right balance at
        /// the mixer's output (LokiGame.W6VoiceSmoke.cs "spatialised") and a listener at 0 hands it silence (found by hpresence).</summary>
        public const float MutedGain = 0.001f;
        /// <summary>The listener's gain while <see cref="Muted"/>: 0 (true silence; port wave J, 2026-10-08: a w5weather test client
        /// at MutedGain, 60 dB down, was still heard on the PC: its thunder and the map music), MutedGain only for the voice smokes
        /// (-lokiSmokeOnly w6voice-*), which measure at the mixer's output (LokiGame.cs Begin).</summary>
        public static float MutedVolume { get; set; } = 0f;

        public static void ApplyAudio()
        {
            AudioListener.volume = Muted ? MutedVolume : MasterVolume;
            var a = Audio.LokiAudio.Instance; if (a != null) { a.MusicVolume = MusicVolume; a.EffectsVolume = EffectsVolume; }
        }

        /// <summary>Render scale, anti-aliasing and shadows. XRSettings.eyeTextureResolutionScale only matters while a headset
        /// renders (desktop runs keep the value); the build's quality defaults are Editor/LokiSetup.cs ConfigureAll's.</summary>
        public static void ApplyGraphics()
        {
            // only what changed: a new eye texture scale or MSAA level re-creates the eye textures (a hitch in the headset)
            try { if (Mathf.Abs(UnityEngine.XR.XRSettings.eyeTextureResolutionScale - RenderScale) > 0.001f) UnityEngine.XR.XRSettings.eyeTextureResolutionScale = RenderScale; }
            catch (Exception e) { Debug.LogWarning("[Comfort] render scale: " + e.Message); }
            if (Msaa >= 0 && QualitySettings.antiAliasing != Msaa) QualitySettings.antiAliasing = Msaa;
            var sq = Shadows == 0 ? ShadowQuality.Disable : ShadowQuality.All; float sd = Shadows == 1 ? 25f : 60f;
            if (QualitySettings.shadows != sq) QualitySettings.shadows = sq;
            if (Shadows != 0 && Mathf.Abs(QualitySettings.shadowDistance - sd) > 0.01f) QualitySettings.shadowDistance = sd;
        }

        /// <summary>A scene reload (character select / log out) drops every listener of the old scene before the new one subscribes.</summary>
        public static void ClearListeners() => Changed = null;

        public static int Next(int[] steps, int cur, int dir) { int i = Array.IndexOf(steps, cur); if (i < 0) i = 1; return steps[Mathf.Clamp(i + dir, 0, steps.Length - 1)]; }
        public static float Next(float[] steps, float cur, int dir)
        {
            int best = 0; for (int i = 1; i < steps.Length; i++) if (Mathf.Abs(steps[i] - cur) < Mathf.Abs(steps[best] - cur)) best = i;
            return steps[Mathf.Clamp(best + dir, 0, steps.Length - 1)];
        }
    }

    /// <summary>
    /// Controller rumble through the Input System: the OpenXR controller layouts (Index, Touch, Touch Pro, Vive, WMR, Reverb G2, KHR
    /// simple) derive from XRControllerWithRumble, whose SendImpulse issues SendHapticImpulseCommand — the OpenXR plugin's own tests
    /// check that this reaches the runtime as a haptic impulse (com.unity.xr.openxr 1.10 Tests/Runtime/OpenXRInputTests.cs
    /// ValidateHaptic). Tracked hands (HandInteraction, HoloLensHand) are plain XRControllers: no rumble. "Main" is the laser hand
    /// (right, or left in left-handed mode). Client wave 4 (w4vr): every request is counted by kind (smoke, and the Settings page's
    /// "test" button), the strength setting scales it and "off" stops it.
    /// Client wave 13, item 4 (w13ring; design note: "separate the different rumble options. So that I can choose what triggers it and at
    /// what intensity"): EVERY call names its kind (the kind-less overloads are gone) and each kind a player can tell apart has its
    /// own switch and strength (Settings → Rumble; VR/Comfort.cs RumbleOn / RumbleStrength). The amplitude sent = the call's own ×
    /// the master strength / 0.6 (wave 4's rule) × the kind's strength, never above 1 (what the controller accepts). The kinds and
    /// the calls each covers (the audit: 30 call sites in 14 files):
    ///   Ui         Buttons: the laser or the look onto a button, a press (VR/DioramaRig.cs).
    ///   Menu       Menu ring: each step of the ring's twist (UI/MenuRing.cs), Horde's card highlight moved by the stick (Horde/Client/HordeHud.cs).
    ///   Casting    Casting: the pad / flick opening, a pad cell, a flick step (LokiGame.W12Cast.cs), the wand wheel's segments and
    ///              strokes (LokiGame.W12Wand.cs), the left-trigger skill wheels opening and changing rows (LokiGame.Wheels.cs).
    ///   Wand       Wand: a hand reaching the holster, the wand taken, a wand button's quick skill, a pull at the sky (LokiGame.W12Wand.cs).
    ///   ItemButton RETIRED in client wave 14 (w14cast) with the item buttons: no call uses it and no row shows it (kept so that the
    ///              kinds keep their numbers).
    ///   Grab       Moving the table: a board taken with the grip (VR/DioramaRig.Ui.cs), resize mode on / off (LokiGame.W13Resize.cs).
    ///   HitDealt   Hits you deal (LokiGame.W3Combat.cs).
    ///   HitTaken   Hits you take (LokiGame.W3Combat.cs; Horde's: Horde/Client/HordeGame.cs).
    ///   Pickup     Item pickup: an item gained (LokiGame.cs Inventory.Gained).
    ///   LevelUp    Level up: base and job level (LokiGame.cs, VR/VrFeedback.cs), Horde's level-up cards (HordeGame.cs).
    ///   LowHp      Low HP heartbeat (VR/VrFeedback.cs, both beats).
    ///   Death      Death: yours (LokiGame.cs) and in Horde (HordeGame.cs).
    ///   Test       the Rumble page's master test (not a kind a player switches; the master switch and strength apply).
    ///   Other      no call uses it (kept so that the kinds keep their numbers).
    /// </summary>
    public static class Haptics
    {
        public enum Kind { Other, Ui, HitDealt, HitTaken, Pickup, LevelUp, LowHp, Death, Test, Menu, Casting, Wand, ItemButton, Grab }
        public static readonly int KindCount = Enum.GetValues(typeof(Kind)).Length;
        /// <summary>The kinds a player switches, in the Rumble page's order.</summary>
        public static readonly Kind[] Named = { Kind.Ui, Kind.Menu, Kind.Casting, Kind.Wand, Kind.Grab, Kind.HitDealt, Kind.HitTaken, Kind.Pickup, Kind.LevelUp, Kind.LowHp, Kind.Death };
        /// <summary>A kind's name as the Rumble page shows it (English: the page translates it).</summary>
        public static string NameOf(Kind k) => k switch
        {
            Kind.Ui => Lang.N("Buttons"), Kind.Menu => Lang.N("Menu ring"), Kind.Casting => Lang.N("Casting"), Kind.Wand => Lang.N("Wand"),
            Kind.Grab => Lang.N("Moving the table"), Kind.HitDealt => Lang.N("Hits you deal"),
            Kind.HitTaken => Lang.N("Hits you take"), Kind.Pickup => Lang.N("Item pickup"), Kind.LevelUp => Lang.N("Level up"),
            Kind.LowHp => Lang.N("Low HP heartbeat"), Kind.Death => Lang.N("Death"), _ => k.ToString(),
        };
        /// <summary>What a kind covers, where its name alone is unclear (English; null: none).</summary>
        public static string HelpOf(Kind k) => k switch
        {
            Kind.Ui => Lang.N("Pointing at or pressing"), Kind.Menu => Lang.N("Each step of the ring"), Kind.Casting => Lang.N("Pad, flick and wheel steps"),
            Kind.Wand => Lang.N("Holster, gripping it, its buttons"), Kind.Grab => Lang.N("Grabbing and resizing"),
            _ => null,
        };
        /// <summary>The rumble a kind plays when its row is changed: one of its own calls (amplitude, seconds, both hands, else the main hand or the other).</summary>
        public static (float amplitude, float seconds, bool both, bool main) SampleOf(Kind k) => k switch
        {
            Kind.Ui => (0.25f, 0.03f, false, true), Kind.Menu => (0.12f, 0.015f, false, false), Kind.Casting => (0.15f, 0.02f, false, true),
            Kind.Wand => (0.25f, 0.04f, false, true), Kind.Grab => (0.2f, 0.03f, false, true),

            Kind.HitDealt => (0.25f, 0.04f, false, true), Kind.HitTaken => (0.4f, 0.08f, true, true), Kind.Pickup => (0.2f, 0.05f, false, false),
            Kind.LevelUp => (0.7f, 0.3f, true, true), Kind.LowHp => (0.35f, 0.06f, true, true), Kind.Death => (0.9f, 0.5f, true, true),
            _ => (0.6f, 0.12f, true, true),
        };

        /// <summary>Requests per kind (made whether or not a controller could rumble).</summary>
        public static readonly int[] Requests = new int[KindCount];
        /// <summary>Client wave 13, item 4: pulses per kind that the settings let through (what the rig hands a controller when one is
        /// there), and the amplitude of each kind's last one.</summary>
        public static readonly int[] Passed = new int[KindCount];
        public static readonly float[] PassedAmplitude = new float[KindCount];
        /// <summary>Impulses handed to a rumble-capable device.</summary>
        public static int Sent { get; private set; }
        /// <summary>The amplitude of the last impulse sent (after the strength settings).</summary>
        public static float LastAmplitude { get; private set; }

        public static void Both(Kind kind, float amplitude, float seconds) { Pulse(kind, true, amplitude, seconds); Pulse(kind, false, amplitude, seconds); }

        /// <summary>The amplitude a call of this kind would be sent with now (0: none: rumble off, the kind off, or its strength 0).</summary>
        public static float AmplitudeFor(Kind kind, float amplitude)
        {
            if (!Comfort.Haptics || amplitude <= 0f) return 0f;
            int k = (int)kind; bool own = kind != Kind.Test && kind != Kind.Other && k < Comfort.RumbleOn.Length;
            if (own && (!Comfort.RumbleOn[k] || Comfort.RumbleStrength[k] <= 0f)) return 0f;
            return Mathf.Clamp01(amplitude * Comfort.HapticStrength / 0.6f * (own ? Comfort.RumbleStrength[k] : 1f));
        }

        public static void Pulse(Kind kind, bool mainHand, float amplitude, float seconds)
        {
            Requests[(int)kind]++;
            float a = AmplitudeFor(kind, amplitude);
            if (a <= 0f) return;
            Passed[(int)kind]++; PassedAmplitude[(int)kind] = a;
            bool right = mainHand != Comfort.LeftHanded;
            var dev = (right ? XRController.rightHand : XRController.leftHand) as XRControllerWithRumble;
            if (dev == null) return;
            try { LastAmplitude = a; dev.SendImpulse(LastAmplitude, seconds); Sent++; }
            catch (Exception e) { Debug.LogWarning("[Haptics] " + e.Message); }
        }

        /// <summary>A kind's own rumble once, as its settings are now (the Rumble page plays it when its row changes).</summary>
        public static void Sample(Kind kind)
        {
            var (a, s, both, main) = SampleOf(kind);
            if (both) Both(kind, a, s); else Pulse(kind, main, a, s);
        }
    }
}
