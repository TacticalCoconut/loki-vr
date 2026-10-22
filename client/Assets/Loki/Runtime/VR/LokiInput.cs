using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using Profile = Loki.VR.ControllerProfiles.Profile;

namespace Loki.VR
{
    /// <summary>
    /// Controller-agnostic input built in code (no input-action asset). Each action binds the generic
    /// &lt;XRController&gt;{LeftHand|RightHand} control names that the OpenXR plugin's layouts share (the names and aliases of
    /// com.unity.xr.openxr 1.10's Runtime/Features/Interactions/*.cs, matched by name or alias, InputControlPath.cs:48-50), plus
    /// explicit bindings for the layouts whose controls have other names:
    ///   trigger       trigger (Index, Touch, Touch Pro, Reverb G2, Vive, WMR); KHR simple: select (KHRSimpleControllerProfile.cs:50);
    ///                 Microsoft hands: select (MicrosoftHandInteraction.cs:51); hand interaction: aim-activate and pinch values
    ///                 (HandInteractionProfile.cs:72-88 in 1.12.1, :72-83 in 1.10: the larger of the two drives the action)
    ///   grip          grip (every controller but KHR simple); Microsoft hands: squeeze (:63); hand interaction: grasp (:103 in 1.12.1)
    ///   stick         thumbstick / joystick (Index, Touch, Reverb, WMR: joystick is WMR's name and the others' alias) and
    ///                 primary2DAxis (the Vive trackpad's alias, HTCViveControllerProfile.cs:92)
    ///   stick click   thumbstickClicked, joystickClicked (WMR's name, the others' alias; MicrosoftMotionControllerProfile.cs:98)
    ///   A / X, B / Y  primaryButton, secondaryButton (Index, Touch, Touch Pro, Reverb G2)
    ///   menu          menu (Vive, WMR, Reverb G2, Touch's left hand, KHR simple); Index's system button stays SteamVR's
    ///   pad           trackpad + trackpadClicked (Vive's names, WMR's aliases: :62, :104)
    ///   aim pose      pointerPosition / pointerRotation (every layout: OpenXR's /input/aim/pose, Documentation~/input.md:150-159)
    /// Controllers without some buttons get them mapped by <see cref="Hand"/> (ControllerProfiles.Buttons is the table).
    /// Client wave 4b (w4xr): the names above were checked path by path against OpenXR 1.12.1 for Quest Touch, Touch Pro, Touch Plus
    /// and Valve Index (design notes, "Bindings audit"); a hand tracked by XR Hands with no tracked OpenXR device on it is read
    /// through <see cref="IJointHand"/> (VR/HandJoints.cs): pinch = trigger, fist = grip, the joints' ray = the aim pose.
    /// </summary>
    public sealed class LokiInput : System.IDisposable
    {
        /// <summary>Client wave 4b (w4xr): one hand as XR Hands sees it (VR/HandJoints.cs), in the rig's (tracking) space.</summary>
        public interface IJointHand
        {
            /// <summary>The hand's joints are tracked this frame.</summary>
            bool Tracked { get; }
            /// <summary>Index finger and thumb pinching, 0-1.</summary>
            float Pinch { get; }
            /// <summary>A fist, 0-1 (never while pinching).</summary>
            float Grasp { get; }
            /// <summary>The pointing ray of the hand (the runtime's, else one made from the joints).</summary>
            bool TryAim(out Vector3 pos, out Quaternion rot);
            /// <summary>Meta's menu gesture began this frame.</summary>
            bool MenuDown { get; }
            /// <summary>Client wave 12 (w12cast): every finger and the thumb curled (the full grip of a tracked hand).</summary>
            bool Fist { get; }
            /// <summary>Client wave 12 (w12cast): the index finger extended, the other three curled (the casting gesture of a tracked hand).</summary>
            bool Pointing { get; }
            /// <summary>Client wave 12 (w12cast): the mean curl of the middle, ring and little fingers, 0-1 (a tracked hand's plain grip).</summary>
            float Curl3 { get; }
        }

        // ---- client wave 12 (w12cast), parts B + C: what the game allows this frame (LokiGame.W12Cast.cs sets them)
        /// <summary>The casting gesture (grip + pointing finger) may open: the setting is pad or flick and the character can act.</summary>
        public static bool CastGestureEnabled;
        /// <summary>Client wave 14 (w14cast), point 5: how firmly a grip must be squeezed to cast (Settings → Controls "Cast grip": 0 light,
        /// the default; 1 medium; 2 firm = the resize threshold, as before wave 14). Only the Valve Index reads a squeeze force: on every
        /// other controller the grip's travel decides (<see cref="Hand.CastGripOn"/>). Set by LokiGame from VR/Comfort.cs CastGrip.</summary>
        public static int CastGripLevel;
        /// <summary>Item 43: the active casting gesture scheme (1 pad, 2 flick) and the analog grip pressure threshold for flick mode.</summary>
        public static int CastGestureScheme;
        public static float FlickGripThreshold = Comfort.FlickGripThresholdDefault;
        /// <summary>Client wave 13 (w13resize): RESIZE MODE is on (the overhead menu's button: LokiGame.W13Resize.cs): a plain grip is
        /// the camera's alone (Hand.CameraGrip): the table's pan and resize, a board's move and resize. Off (the normal state): no
        /// grip moves anything; the grips are the casting's (client wave 14: the item buttons are gone).</summary>
        public static bool ResizeMode;

        public sealed class Hand
        {
            public readonly bool IsLeft;
            public readonly InputAction Position, Rotation, AimPosition, AimRotation, Trigger, Grip, Stick, Primary, Secondary, StickClick, TriggerPressed, Menu, Pad, PadClick, GripForce, TriggerTouch, ThumbTouch, ThumbForce;
            /// <summary>Whether this hand is the laser / turning hand right now (the rig sets it from Comfort.LeftHanded).</summary>
            internal System.Func<bool> IsMainHand;
            public bool IsMain => IsMainHand != null ? IsMainHand() : !IsLeft;
            /// <summary>The OpenXR interaction profile of the device on this hand (none on a desktop run).</summary>
            public Profile Profile { get { Sync(); return _profile; } }
            public InputDevice Device { get { Sync(); return _device; } }
            private Profile _profile; private InputDevice _device;
            internal LokiInput Owner;
            private void Sync() => Owner?.BeginFrame();
            /// <summary>w4xr: this hand's joints (VR/HandJoints.cs sets it; null without XR Hands).</summary>
            public IJointHand Joints;
            /// <summary>w4xr: the hand is read from its joints this frame (tracked by XR Hands, no tracked OpenXR device on it).</summary>
            public bool JointMode { get { Sync(); return _profile == Profile.HandJoints; } }
            private bool _jointHeld, _jointDown;

            public Hand(string tag)
            {
                IsLeft = tag == "LeftHand";
                string h = $"{{{tag}}}";
                Position = new InputAction($"{tag}Pos", InputActionType.Value, $"<XRController>{h}/devicePosition");
                Rotation = new InputAction($"{tag}Rot", InputActionType.Value, $"<XRController>{h}/deviceRotation");
                AimPosition = new InputAction($"{tag}AimPos", InputActionType.Value, $"<XRController>{h}/pointerPosition");
                AimRotation = new InputAction($"{tag}AimRot", InputActionType.Value, $"<XRController>{h}/pointerRotation");
                Trigger = new InputAction($"{tag}Trigger", InputActionType.Value, $"<XRController>{h}/trigger");
                Trigger.AddBinding($"<KHRSimpleController>{h}/select");
                Trigger.AddBinding($"<HoloLensHand>{h}/select");
                Trigger.AddBinding($"<HandInteraction>{h}/pointerActivateValue");
                Trigger.AddBinding($"<HandInteraction>{h}/pinchValue");
                TriggerPressed = new InputAction($"{tag}TriggerPressed", InputActionType.Button, $"<XRController>{h}/triggerPressed");
                TriggerPressed.AddBinding($"<KHRSimpleController>{h}/select");
                TriggerPressed.AddBinding($"<HoloLensHand>{h}/selectPressed");
                Grip = new InputAction($"{tag}Grip", InputActionType.Value, $"<XRController>{h}/grip");
                Grip.AddBinding($"<HoloLensHand>{h}/squeeze");
                Grip.AddBinding($"<HandInteraction>{h}/graspValue");
                // the Valve Index's squeeze force (/input/squeeze/force: ValveIndexControllerProfile.cs:105, :227, :287), only it has one
                GripForce = new InputAction($"{tag}GripForce", InputActionType.Value, $"<ValveIndexController>{h}/gripForce");
                Stick = new InputAction($"{tag}Stick", InputActionType.Value, $"<XRController>{h}/thumbstick");
                Stick.AddBinding($"<XRController>{h}/joystick"); // WMR's name for its stick, an alias on the others
                Stick.AddBinding($"<XRController>{h}/primary2DAxis"); // the Vive trackpad (read only while pressed: see StickValue)
                Primary = new InputAction($"{tag}Primary", InputActionType.Button, $"<XRController>{h}/primaryButton");
                Secondary = new InputAction($"{tag}Secondary", InputActionType.Button, $"<XRController>{h}/secondaryButton");
                StickClick = new InputAction($"{tag}StickClick", InputActionType.Button, $"<XRController>{h}/thumbstickClicked");
                StickClick.AddBinding($"<XRController>{h}/joystickClicked");
                Menu = new InputAction($"{tag}Menu", InputActionType.Button, $"<XRController>{h}/menu");
                Pad = new InputAction($"{tag}Pad", InputActionType.Value, $"<XRController>{h}/trackpad");
                PadClick = new InputAction($"{tag}PadClick", InputActionType.Button, $"<XRController>{h}/trackpadClicked");
                // client wave 12 (w12cast): where the index finger and the thumb are, for the full grip and the casting gesture. The controls
                // each layout really has (com.unity.xr.openxr 1.12.1, Runtime/Features/Interactions; matched by name or alias):
                //   triggerTouched     /input/trigger/touch: Index (ValveIndexControllerProfile.cs:123), Touch (OculusTouchControllerProfile.cs:116),
                //                      Touch Pro (MetaQuestTouchProControllerProfile.cs:116), Touch Plus (MetaQuestTouchPlusControllerProfile.cs:116)
                //   thumbstickTouched  Index :141, Touch / Touch Pro / Touch Plus :128
                //   primaryTouched, secondaryTouched (A / B, X / Y touch)  Index :75 / :87, Touch / Touch Pro / Touch Plus :86 / :98
                //   thumbrestTouched   Touch / Touch Pro / Touch Plus :134
                //   trackpadTouched    Index :153, Vive (HTCViveControllerProfile.cs:105), WMR's alias of touchpadTouched (MicrosoftMotionControllerProfile.cs:110-111)
                //   thumbProximity     Touch Pro :224, Touch Plus :218
                //   systemTouched      Index :63
                //   thumbrestForce     Touch Pro :194;  trackpadForce  Index :159
                // HP Reverb G2 (HPReverbG2ControllerProfile.cs:57-105), the Khronos simple controller and the two hand profiles have none of them.
                // Pass-through actions: the controls are read one by one (ThumbRaw), so no control hides another.
                TriggerTouch = new InputAction($"{tag}TriggerTouch", InputActionType.PassThrough, $"<XRController>{h}/triggerTouched");
                ThumbTouch = new InputAction($"{tag}ThumbTouch", InputActionType.PassThrough, $"<XRController>{h}/thumbstickTouched");
                ThumbTouch.AddBinding($"<XRController>{h}/primaryTouched");
                ThumbTouch.AddBinding($"<XRController>{h}/secondaryTouched");
                ThumbTouch.AddBinding($"<XRController>{h}/thumbrestTouched");
                ThumbTouch.AddBinding($"<XRController>{h}/trackpadTouched");
                ThumbTouch.AddBinding($"<XRController>{h}/thumbProximity");
                ThumbTouch.AddBinding($"<XRController>{h}/systemTouched");
                ThumbForce = new InputAction($"{tag}ThumbForce", InputActionType.PassThrough, $"<XRController>{h}/thumbrestForce");
                ThumbForce.AddBinding($"<XRController>{h}/trackpadForce");
            }
            public void Enable() { foreach (var a in All) a.Enable(); }
            public void Disable() { foreach (var a in All) a.Disable(); }
            public InputAction[] All => new[] { Position, Rotation, AimPosition, AimRotation, Trigger, TriggerPressed, Grip, Stick, Primary, Secondary, StickClick, Menu, Pad, PadClick, GripForce, TriggerTouch, ThumbTouch, ThumbForce };
            public Vector3 Pos => Position.ReadValue<Vector3>();
            public Quaternion Rot => Rotation.ReadValue<Quaternion>();
            /// <summary>The aim pose (OpenXR /input/aim/pose) in the rig's space, when the device reports one.</summary>
            public bool TryAim(out Vector3 pos, out Quaternion rot)
            {
                if (JointMode) return Joints.TryAim(out pos, out rot); // w4xr: the hand's own ray
                pos = AimPosition.ReadValue<Vector3>(); rot = AimRotation.ReadValue<Quaternion>();
                return !(rot.x == 0f && rot.y == 0f && rot.z == 0f && rot.w == 0f) && AimRotation.activeControl != null;
            }
            /// <summary>w4xr: a tracked hand's pinch in joint mode.</summary>
            public float TriggerValue => JointMode ? Joints.Pinch : TriggerRaw;
            private float TriggerRaw
            {
                get
                {
                    return Trigger.ReadValue<float>();
                }
            }
            /// <summary>w4xr: in joint mode a pinch clicks once when it passes 0.7 and again only after it opened below 0.5 (joints are
            /// noisier than a trigger).</summary>
            public bool TriggerDown => JointMode ? _jointDown : TriggerPressed.WasPressedThisFrame() || (Trigger.ReadValue<float>() > 0.7f && _wasTrigger == false);
            /// <summary>w4xr: a tracked hand's fist in joint mode.</summary>
            public float GripValue => JointMode ? Joints.Grasp : Grip.ReadValue<float>();

            /// <summary>The Index's squeeze force, 0 (none) to 1 (0 on every other controller).</summary>
            public float GripForceValue => GripForce.ReadValue<float>();
            /// <summary>Squeeze force that counts as holding a grip for the table on the Valve Index (specified after the 2026-10-01
            /// playtest: resizing "requires a little bit of force", and with a hand only touching the left grip the stick should walk).
            /// The Index's grip value (/input/squeeze/value) comes from its finger sensors and is near 1 for a hand only holding the
            /// controller, so the grips read as held all the time: both together resized the world, and the walking hand's grip stopped
            /// the stick from walking (Game/Targeting.cs) without moving the table either (that wants the other grip free). Its force
            /// needs a real squeeze. On at <see cref="IndexResizeForceOn"/>, off below <see cref="IndexResizeForceOff"/> (no flicker).</summary>
            public const float IndexResizeForceOn = 0.25f, IndexResizeForceOff = 0.12f;
            private bool _resizeSqueezed;
            /// <summary>This hand holds its grip for the table this frame — moving it (grip + stick), resizing it (both grips), and the
            /// stick's walking giving way to the move: on the Index a squeeze with force, elsewhere the grip over 0.6 as before. Several
            /// reads in a frame agree (the Index's on / off step gives the same answer for the same force).</summary>
            public bool TableGripHeld()
            {
                if (Profile != Profile.Index) { _resizeSqueezed = false; return GripValue > 0.6f; }
                _resizeSqueezed = IndexSqueezeStep(_resizeSqueezed, GripForceValue);
                return _resizeSqueezed && GripValue > 0.6f;
            }
            /// <summary>One frame of the Index's squeeze state: held before → still held while the force is at least the "off" level;
            /// not held → held once it reaches the "on" level.</summary>
            public static bool IndexSqueezeStep(bool wasHeld, float force) => wasHeld ? force >= IndexResizeForceOff : force >= IndexResizeForceOn;

            // ------------------------------------------------------------------ the grip, the thumb, the trigger (client waves 12 and 13)
            // Client wave 12 read three things of a hand once a frame for its "full grip" camera rule: the grip (TableGripHeld), the
            // thumb (a touch sensor of the controller's top, or the stick / a button worked) and whether the index finger is on the
            // trigger (its touch sensor). Client wave 13 (w13resize; design note, 2026-10-07: "Instead of it being a setting that is always
            // on and available ... they can tap on a button to toggle the resize mode. At which point they just have to grip the
            // controller to control everything") took the full grip away again. The signals stay (the casting gesture and the item
            // buttons read them: CastFrame); the camera reads CameraGrip:
            //   RESIZE MODE off (the normal state): no grip moves the table or a board; a grip is the casting's (client wave 14: the item buttons are gone).
            //   RESIZE MODE on (LokiInput.ResizeMode, set by LokiGame.W13Resize.cs): a plain grip is the camera's alone, as before
            //   wave 12: the walking hand's grip + its stick pans, both grips resize, a grip with the laser on a board holds it; a
            //   tracked hand: its fist. No casting gesture, no wand meanwhile.
            // The trigger: ONE click per pull, in the frame the pull reaches its click point (the runtime's "pressed" or 0.7 of the
            // travel, whichever comes first), whatever the grip and the thumb do: wave 12's "a trigger pulled as part of a fist is no
            // click" and its 80 ms wait for a closing hand went with the full grip. The walking hand's pull is the skill wheels'
            // (Open until they claim it: ClaimTrigger, LokiGame.Wheels.cs). A hand whose runtime gives one grasp value: a pinch
            // inside the grasp is no click (the pinch of a closed fist reads anything).
            public const float TriggerPullOn = 0.5f, TriggerPullOff = 0.3f;
            public enum Pull { None, Open, Click }
            private bool _gripHeld, _thumb, _thumbSensed, _pulled, _camGrip, _click, _pressedWas, _onTrigger, _triggerSensed;
            private Pull _pull;
            /// <summary>The grip is squeezed this frame (TableGripHeld's answer, read once a frame).</summary>
            public bool GripHeld { get { Sync(); return _gripHeld; } }
            /// <summary>The thumb is on the controller's top: a touch sensor says so, or it is pushing / pressing something there; on
            /// a controller with no such sensor it counts as there (<see cref="ThumbSensed"/> false: HP Reverb G2, an unknown controller).</summary>
            public bool ThumbActive { get { Sync(); return _thumb; } }
            public bool ThumbSensed { get { Sync(); return _thumbSensed; } }
            public bool TriggerPulled { get { Sync(); return _pulled; } }
            /// <summary>The index finger rests on the trigger: its touch sensor, or the trigger moved at all; without a sensor
            /// (<see cref="TriggerTouchSensed"/> false) only the trigger's own travel says so.</summary>
            public bool IndexOnTrigger { get { Sync(); return _onTrigger; } }
            public bool TriggerTouchSensed { get { Sync(); return _triggerSensed; } }
            /// <summary>This hand's grip moves the camera now: resize mode is on and its grip is squeezed (a tracked hand: a fist).
            /// False whenever resize mode is off: no grip moves the table or a board then.</summary>
            public bool CameraGrip { get { Sync(); return _camGrip; } }
            /// <summary>The pointer's click this frame: the trigger reached its click point.</summary>
            public bool ClickDown { get { Sync(); return _click; } }
            /// <summary>Client wave 14 (w14laser), point 10 (design note: "allow the player to have both controllers pointing at things on UI panels"):
            /// this hand's trigger reached its click point this frame, whichever hand it is (a tracked hand: its pinch; none in a hand's grasp,
            /// nor while the pull is a casting gesture's, the wand's or a grab's). The walking hand's laser presses a panel's button with it
            /// (VR/DioramaRig.Laser.cs); the pointing hand's click stays <see cref="ClickDown"/>.</summary>
            public bool PullDown { get { Sync(); return _pullDown; } }
            private bool _pullDown;
            public Pull TriggerPull { get { Sync(); return _pull; } }
            /// <summary>The trigger as the skill wheels read it: 0 while the pull is the casting gesture's, the wand's or the menu ring's
            /// (client wave 13, w13grab: or while this hand's grip holds a panel: <see cref="GrabClaimed"/>; item 48: or while holding the table / in resize mode).</summary>
            public float TriggerFree { get { Sync(); return _cast || WandHeld || _menuClaim || _grabClaim || _trigGrab || _tableClaim || _camGrip || ResizeMode ? 0f : TriggerValue; } }
            /// <summary>Client wave 13 (w13resize), the brief's fourteenth item: the menu ring took this hand's trigger pull (it opened the
            /// highlighted group with it, or the ring was open while it was pulled: UI/MenuRing.cs): the skill wheels do not see it until
            /// the trigger is let go (under <see cref="MenuReleaseLevel"/>) and pulled again.</summary>
            public void ClaimForMenu() { Sync(); _menuClaim = true; }
            public bool MenuClaimed { get { Sync(); return _menuClaim; } }
            public const float MenuReleaseLevel = 0.2f;
            private bool _menuClaim;
            /// <summary>The walking hand's pull opened the skill wheels: it is theirs until the trigger is let go.</summary>
            public void ClaimTrigger() { Sync(); _pull = Pull.Click; }

            /// <summary>The thumb as the device reports it. sensed: the device has a control that can tell.</summary>
            private bool ThumbRaw(out bool sensed)
            {
                sensed = false; bool on = false;
                var touch = ThumbTouch.controls;
                for (int i = 0; i < touch.Count; i++) { sensed = true; if (touch[i].IsPressed()) on = true; }
                var force = ThumbForce.controls;
                for (int i = 0; i < force.Count; i++) { sensed = true; if (force[i].EvaluateMagnitude() > 0.05f) on = true; }
                // a thumb that works the stick or a button is there, whatever a sensor says (a runtime that fills no touch control)
                if (Primary.IsPressed() || Secondary.IsPressed() || StickClick.IsPressed() || PadClick.IsPressed()) on = true;
                if (Stick.ReadValue<Vector2>().sqrMagnitude > 0.04f) on = true;
                return on;
            }

            /// <summary>Once a frame (BeginFrame): the grip, the thumb, the trigger, the click and the camera's grip.</summary>
            private void GripFrame()
            {
                _click = false; _pullDown = false;
                if (_profile == Profile.HandJoints)
                {
                    // a tracked hand: the fist is its camera grip (in resize mode); BeginFrame gives no pinch click while or right after a fist
                    bool fist = Joints.Fist;
                    _gripHeld = Joints.Grasp > 0.6f || fist; _thumb = fist; _thumbSensed = true; _pulled = fist;
                    _onTrigger = !Joints.Pointing; _triggerSensed = true;
                    _pull = Pull.None; _pressedWas = false;
                    _click = _jointDown; _pullDown = _jointDown;
                    if (_menuClaim && Joints.Pinch < MenuReleaseLevel) _menuClaim = false;
                    if (_jointDown) ClaimForBubble(); // client wave 20 (w20bubbles), point 56
                    _camGrip = ResizeMode && fist;
                    return;
                }
                float tv = TriggerRaw;
                bool grip = TableGripHeld();
                bool thumb = ThumbRaw(out bool sensed);
                bool hand = ControllerProfiles.IsHand(_profile);
                // no thumb sensor on this controller (HP Reverb G2, an unknown one), or a hand whose runtime gives one "grasp" value for
                // the whole fist: the thumb counts as there
                if (!sensed && _profile != Profile.None) thumb = true;
                _thumbSensed = sensed;
                _pulled = hand || (_pulled ? tv >= TriggerPullOff : tv >= TriggerPullOn);
                var tt = TriggerTouch.controls; bool touchSensor = tt.Count > 0, touching = false;
                for (int i = 0; i < tt.Count; i++) if (tt[i].IsPressed()) touching = true;
                _triggerSensed = touchSensor; _onTrigger = touching || tv > 0.05f;
                // the click point: the runtime's "pressed" or 0.7 of the travel; one click per pull
                bool pressedNow = tv > 0.7f || TriggerPressed.IsPressed();
                bool down = pressedNow && !_pressedWas; _pressedWas = pressedNow;
                if (!pressedNow && tv < 0.05f) _pull = Pull.None;
                if (_menuClaim && !pressedNow && tv < MenuReleaseLevel) _menuClaim = false; // (the menu ring's pull was let go)
                if (down)
                {
                    _pullDown = !(hand && grip); // client wave 14 (w14laser), point 10: either hand's pull (the walking hand's laser clicks panels)
                    if (hand && grip) _pull = Pull.Open; // a pinch inside a hand's grasp: no click
                    else if (IsMain) { _pull = Pull.Click; _click = true; }
                    else _pull = Pull.Open;
                    if (_pullDown) ClaimForBubble(); // client wave 20 (w20bubbles), point 56: a Quick Use Bubble's pull is its own
                }
                _gripHeld = grip; _thumb = thumb;
                _camGrip = ResizeMode && grip;
            }

            // ------------------------------------------------------------------ client wave 20 (w20bubbles), point 56: a bubble's trigger
            // design note (2026-10-09): the Quick Use Bubbles "should also have a toggle that decides whether they activate upon being touched or upon the
            // trigger being pulled" and must not "interfere with any of the other casting methods". A pull of either hand whose tip is in a bubble
            // set to "Use by: Trigger" (the game's probe: <see cref="BubbleProbe"/>, LokiGame.W20Bubbles.cs, which also says no wherever the
            // bubbles are inert) is the BUBBLE's, decided in the frame the pull reaches its click point: no click of this hand (no button, no
            // unit, no walk, no panel edge), no walking hand's panel press, and no skill wheels from it until the trigger is let go (as the menu
            // ring's pull: <see cref="ClaimForMenu"/>). The wand's hand, an open casting gesture and a grab are never asked (the probe's rule).
            /// <summary>Asked in the frame a trigger pull clicks (either hand): does a Quick Use Bubble take it (LokiGame.W20Bubbles.cs)?</summary>
            public static System.Func<Hand, bool> BubbleProbe;
            /// <summary>Pulls a bubble took so far (smoke, log).</summary>
            public int BubblePulls { get; private set; }
            private void ClaimForBubble()
            {
                if (BubbleProbe == null) return;
                bool take = false;
                try { take = BubbleProbe(this); } catch (System.Exception e) { Debug.LogException(e); }
                if (!take) return;
                _click = false; _pullDown = false; _pull = Pull.Click; _menuClaim = true; BubblePulls++;
            }
            /// <summary>Walking / turning stick. A Vive trackpad counts only while pressed near its edge (a resting thumb would
            /// otherwise walk); hands and the simple controller have none.
            /// Client wave 15 (w15grab), point 5: 0 while this hand holds a panel (<see cref="GrabClaimed"/>): its stick pushes / pulls the
            /// panel then (<see cref="GrabStick"/>), so no walking, turning, panning or menu stick from that hand meanwhile.</summary>
            public Vector2 StickValue { get { Sync(); return GrabHolding ? Vector2.zero : RawStick; } }
            /// <summary>Client wave 15 (w15grab), point 5: this hand's stick while it holds a panel (LokiGame.W13Grab.cs: push / pull); 0 otherwise.</summary>
            public Vector2 GrabStick { get { Sync(); return GrabHolding ? RawStick : Vector2.zero; } }
            private Vector2 RawStick
            {
                get
                {
                    if (_profile == Profile.Vive) return _padEdge && PadClick.IsPressed() ? Pad.ReadValue<Vector2>() : Vector2.zero;
                    if (_profile == Profile.KhrSimple || ControllerProfiles.IsHand(_profile)) return Vector2.zero;
                    return Stick.ReadValue<Vector2>();
                }
            }
            /// <summary>A (Touch: A on the right, X on the left): the face button, or its stand-in on controllers without one
            /// (ControllerProfiles.Buttons): the walking hand's menu button (Vive, WMR, KHR simple), the pointer hand's WMR touchpad.</summary>
            /// Client wave 14 (w14cast), point 5 + the design's answer 5: the item buttons (wave 12's grip + a face button / the stick click) are
            /// gone: a grip no longer changes what these buttons do.
            /// Client wave 13 (w13grab): none of them either while this hand's grip holds a panel or the bag (<see cref="GrabClaimed"/>): a
            /// button pressed with that grip is no item button and does no usual job (no sitting down, no B closing the held panel).
            public bool PrimaryDown { get { Sync(); return !_wandBtn && !GrabHolding && _rawPrimary; } }
            public bool SecondaryDown { get { Sync(); return !GrabHolding && _rawSecondary; } }
            /// <summary>Client wave 15 (w15hfx), point 19: the upper face button (the walking hand's menu button; its stand-in on the other
            /// profiles, as <see cref="SecondaryDown"/> maps it) is still held. A tracked hand's menu gesture is a press only (never held).</summary>
            public bool SecondaryHeld { get { Sync(); bool h = !GrabHolding && _secHeld;
                    return h; } }
            private bool _secHeld;
            public bool StickClickDown { get { Sync(); return !_wandBtn && !GrabHolding && _rawStickClick; } }
            /// <summary>The stick click is held (the walking hand's hold switches the view).</summary>
            public bool StickClickHeld { get { Sync(); return !_wandBtn && !GrabHolding && (_profile == Profile.Vive ? _padCentre && PadClick.IsPressed() : StickClick.IsPressed()); } }
            private bool _rawPrimary, _rawSecondary, _rawStickClick, _freeHeld;
            /// <summary>Client wave 13 (w13view), the brief's eighteenth item: the freed button (the lower face button; WMR: the menu
            /// button) is held now; false while it is the wand's. Only the walking hand's is read.</summary>
            public bool FreeHeld { get { Sync(); return !_wandBtn && !GrabHolding && _freeHeld; } }
            /// <summary>The menu button opens Settings on controllers whose menu button is free (Touch's left, Reverb G2).</summary>
            public bool SettingsDown => (ControllerProfiles.IsTouch(Profile) || Profile == Profile.ReverbG2) && Menu.WasPressedThisFrame(); // w4xr: + Touch Plus

            internal bool _wasTrigger;
            private bool _padEdge, _padCentre, _padCentreDown, _mappedPrimary, _mappedSecondary;

            /// <summary>Once a frame before the game reads the hand: the device's profile and the mapped buttons.</summary>
            internal void BeginFrame()
            {
                _device = IsLeft ? XRController.leftHand : XRController.rightHand;
                _profile = ControllerProfiles.Of(_device);
                // w4xr: a hand XR Hands tracks, with no OpenXR device on it or one that is not tracked (controllers put down), is read
                // from its joints; a tracked controller always wins (SteamVR also reports joints for hands holding Index controllers)
                if (Joints != null && Joints.Tracked && (_device == null || !DeviceTracked(_device))) _profile = Profile.HandJoints;
                _jointDown = false;
                if (_profile == Profile.HandJoints)
                {
                    float p = Joints.Pinch;
                    // client wave 12 (w12cast): a fist never clicks: while every finger is curled the pinch counts as already held, so
                    // it clicks again only after the hand opened and the pinch went under 0.5 once
                    if (Joints.Fist) _jointHeld = true;
                    else if (!_jointHeld && p > 0.7f) { _jointHeld = true; _jointDown = true; }
                    else if (_jointHeld && p < 0.5f) _jointHeld = false;
                }
                else _jointHeld = false;
                _padCentreDown = false; _mappedPrimary = _mappedSecondary = false;
                bool menu = Menu.WasPressedThisFrame(), pad = PadClick.WasPressedThisFrame();
                // client wave 13 (w13view), the brief's tenth item: the menu opens with the button that opened the minimap until now: the
                // walking hand's UPPER face button ("secondary": Y on Touch, left B on Index; UI/LokiHud.cs). Where the minimap had no
                // button of its own (Vive, the simple controller, a tracked hand's menu gesture) the menu keeps the button it had, now
                // read as "secondary". WMR: the walking hand's touchpad (the minimap's) opens the menu; its menu button is the freed one.
                switch (_profile)
                {
                    case Profile.Vive:
                        if (pad) { bool centre = Pad.ReadValue<Vector2>().magnitude < 0.5f; _padCentre = centre; _padEdge = !centre; _padCentreDown = centre; }
                        if (!PadClick.IsPressed()) { _padCentre = _padEdge = false; }
                        if (menu) _mappedSecondary = true; // (the pointing hand's: B; the walking hand's: the menu)
                        break;
                    case Profile.Wmr:
                        if (menu) { if (IsMain) _mappedSecondary = true; else _mappedPrimary = true; }
                        if (pad) { if (IsMain) _mappedPrimary = true; else _mappedSecondary = true; }
                        break;
                    case Profile.KhrSimple:
                        if (menu) _mappedSecondary = true; // (the pointing hand's: B; the walking hand's: the menu)
                        break;
                    case Profile.HandJoints: // w4xr: Meta's menu gesture on the walking hand opens the menu
                        if (!IsMain && Joints.MenuDown) _mappedSecondary = true;
                        break;
                }
                // client wave 13 (w13view), the brief's eighteenth item: the freed button (the walking hand's lower face button: X on
                // Touch, left A on Index; WMR: the walking hand's menu button) is HELD for resize mode (UI/LokiHud.cs)
                _freeHeld = _profile == Profile.Wmr ? Menu.IsPressed() : _profile == Profile.Vive || _profile == Profile.KhrSimple || ControllerProfiles.IsHand(_profile) ? false : Primary.IsPressed();
                _rawPrimary = Primary.WasPressedThisFrame() || _mappedPrimary; _rawSecondary = Secondary.WasPressedThisFrame() || _mappedSecondary;
                _secHeld = _profile == Profile.Vive || _profile == Profile.KhrSimple ? Menu.IsPressed() : _profile == Profile.Wmr ? (IsMain ? Menu.IsPressed() : PadClick.IsPressed())
                         : _profile == Profile.HandJoints ? false : Secondary.IsPressed(); // (w15hfx, point 19)
                _rawStickClick = _profile == Profile.Vive ? _padCentreDown : StickClick.WasPressedThisFrame();
                GripFrame(); // client wave 12 (w12cast): the full grip, and what this trigger pull is
                CastFrame(); // parts B + C: the casting gesture (client wave 14: the item buttons' shift is gone)
            }

            // ------------------------------------------------------------------ client wave 12 (w12cast), parts B + C
            // The casting gesture's start (design note, 2026-10-06: "I would rather it trigger when the player is pointing their finger, not
            // just holding the grip. It needs to be a combination"): the grip squeezed WHILE the index finger points: off the trigger
            // (its touch sensor; on a controller without one: the trigger not pulled at all), a tracked hand: the pointing pose. Held
            // together for CastStartSeconds (a finger that leaves the trigger for an instant opens nothing), it opens and stays open
            // until the grip is let go: the finger may come back onto the trigger meanwhile. While it is open its trigger neither
            // clicks nor opens the wheels. Never in resize mode (client wave 13: the grip is the camera's then).
            // Client wave 15 (w15cast), point 32 (design note: "there is still a delay when I click the button and the flick menu shows up ... That
            // needs to go away"): NO wait any more: the gesture opens in the very frame the grip and the pointing finger are both there (wave
            // 12 waited CastStartSeconds first). The blip guard is kept the other way round: a finger back on the trigger within
            // CastStartSeconds of the opening WITHDRAWS the gesture (<see cref="CastWithdrawn"/>: closed, nothing taken), so a finger leaving
            // the trigger for an instant still takes nothing and keeps its click.
            public const float CastStartSeconds = 0.10f;
            private bool _cast, _jointGrip, _castWithdrawn; private float _pointSince = -1f;
            /// <summary>The casting gesture is open on this hand (it closes when the grip is let go).</summary>
            public bool CastHeld { get { Sync(); return _cast; } }
            /// <summary>Client wave 15 (w15cast): this frame the gesture was withdrawn as a blip (the finger back on the trigger within
            /// <see cref="CastStartSeconds"/> of the opening): its close takes nothing.</summary>
            public bool CastWithdrawn { get { Sync(); return _castWithdrawn; } }
            /// <summary>Blips withdrawn so far (smoke, log).</summary>
            public int CastBlips { get; private set; }
            // ---- client wave 15 (w15cast), point 30 (design note: "I should be able to just click grip again with a little bit of force to let go of
            // the wand and have it automatically return to the holster"): a FIRM squeeze, firmer than every "Cast grip" level: on the Valve
            // Index a squeeze force of <see cref="WandFirmForceOn"/> (off under <see cref="WandFirmForceOff"/>; the firm cast level is 0.25),
            // on the other controllers the grip's travel <see cref="WandFirmTravelOn"/> (off under <see cref="WandFirmTravelOff"/>; the firm
            // cast level is 0.60). A tracked hand (joints or a hand profile's grasp) has no force: never (the holster put-back stays).
            public const float WandFirmForceOn = 0.35f, WandFirmForceOff = 0.20f, WandFirmTravelOn = 0.90f, WandFirmTravelOff = 0.75f;
            private bool _firmGrip;
            /// <summary>The grip is squeezed FIRMLY now (client wave 15, point 30: the wand's put-back anywhere).</summary>
            public bool FirmGrip { get { Sync(); return _firmGrip; } }
            /// <summary>The grip is squeezed whatever else the hand does: a controller's grip as the CASTING reads it (<see cref="CastGripOn"/>:
            /// client wave 14, point 5: a light squeeze on the Index, not the resize threshold), a tracked hand's three fingers curled. The
            /// wand, the panel grab and Reposition read it too; resize mode's camera reads the firm grip (<see cref="GripHeld"/>).</summary>
            public bool PlainGrip { get { Sync(); return _profile == Profile.HandJoints ? _jointGrip : _castGrip; } }
            /// <summary>The index finger points: off the trigger (a tracked hand: the pointing pose).</summary>
            public bool Pointing { get { Sync(); return !_onTrigger; } }
            /// <summary>The wand is in this hand (VR/WandHolster.cs): its grip and trigger are the wand's: no click, no wheels, no
            /// item buttons, no casting gesture from them.</summary>
            public bool WandHeld;
            /// <summary>The wand's quick buttons are on for this hand (the wand is in it, it is the pointing hand, the setting says
            /// so): its lower face button and its stick click are the wand's (<see cref="WandButtonDown"/>) and do not do their own jobs.</summary>
            public bool WandButtons;
            private bool _wandBtn;
            /// <summary>0: the lower face button, 1: the stick click, pressed this frame while they are the wand's.</summary>
            public bool WandButtonDown(int i) { Sync(); return _wandBtn && (i == 0 ? _rawPrimary : _rawStickClick); }

            // ---- client wave 14 (w14cast), point 5 (design note: "the amount of pressure that require to put to open the menu for the flick is way
            // too big"): the casting's grip is no longer the table's. On the Valve Index the grip VALUE comes from the finger sensors (near 1 for
            // a hand only holding the controller) and the table needs a firm squeeze FORCE (IndexResizeForceOn, 0.25, specified on
            // 2026-10-01 for resizing); casting now asks the force of the chosen "Cast grip" level: light (the default) <see cref="CastForceOn"/>[0].
            // Other controllers: the grip's travel, light <see cref="CastTravelOn"/>[0]. On / off thresholds apart (no flicker).
            /// <summary>The Index's squeeze force that starts / keeps a casting grip, per level (light, medium, firm = the resize threshold).</summary>
            public static readonly float[] CastForceOn = { 0.08f, 0.15f, IndexResizeForceOn }, CastForceOff = { 0.04f, 0.08f, IndexResizeForceOff };
            /// <summary>The grip's travel that starts / keeps a casting grip on the other controllers, per level (firm: 0.6, as before).</summary>
            public static readonly float[] CastTravelOn = { 0.40f, 0.50f, 0.60f }, CastTravelOff = { 0.30f, 0.40f, 0.55f };
            private bool _castGrip;
            /// <summary>One frame of the casting grip (a controller): held before → kept while over the "off" level; else on at the "on" level.</summary>
            public bool CastGripOn()
            {
                if (CastGestureScheme == 2)
                {
                    float on = Mathf.Clamp(FlickGripThreshold, Comfort.FlickGripThresholdMin, Comfort.FlickGripThresholdMax);
                    float off = Mathf.Max(0.05f, on - 0.10f);
                    if (Profile == Profile.Index) return GripValue > 0.6f && (_castGrip ? GripForceValue >= Mathf.Max(0.04f, off * 0.5f) : GripForceValue >= on * 0.5f);
                    float fv = GripValue;
                    return _castGrip ? fv >= off : fv >= on;
                }
                int k = Mathf.Clamp(CastGripLevel, 0, 2);
                if (Profile == Profile.Index) return GripValue > 0.6f && (_castGrip ? GripForceValue >= CastForceOff[k] : GripForceValue >= CastForceOn[k]);
                float v = GripValue;
                return _castGrip ? v >= CastTravelOff[k] : v >= CastTravelOn[k];
            }

            private void CastFrame()
            {
                float now = Time.unscaledTime;
                bool joints = _profile == Profile.HandJoints;
                _castGrip = !joints && CastGripOn();
                bool grip = _castGrip;
                if (joints)
                {
                    // a tracked hand's plain grip: the three fingers curled (in at 0.6, out under 0.45), whatever the index and the thumb do
                    float c = Joints.Curl3;
                    _jointGrip = _jointGrip ? c >= 0.45f : c >= 0.6f;
                    grip = _jointGrip;
                }
                // client wave 15 (w15cast), point 30: the firm squeeze (on / off apart: no flicker)
                if (joints || ControllerProfiles.IsHand(_profile)) _firmGrip = false; // (a hand's grasp is no squeeze force)
                else if (Profile == Profile.Index) _firmGrip = GripValue > 0.6f && GripForceValue >= (_firmGrip ? WandFirmForceOff : WandFirmForceOn);
                else _firmGrip = GripValue >= (_firmGrip ? WandFirmTravelOff : WandFirmTravelOn);
                _castWithdrawn = false;
                ClaimForGrab(grip); // client wave 13 (w13grab), items 19 and 20: BEFORE the casting gesture opens below
                ClaimForTrigger(grip); // client wave 14 (w14ui), point 12: the trigger on a panel's edge band drags it as the grip does
                ClaimForTable(); // client wave 14 (w14laser), point 23: the Table view's free grab, after the panel grabs
                ClaimForWandSize(grip); // client wave 21 (w21wand): resize mode's grips on the wand, after the panel grabs
                if (GrabHolding) { _cast = false; _pointSince = -1f; }
                else if (!grip || WandHeld || ResizeMode || _tableClaim) { _cast = false; _pointSince = -1f; }
                else
                {
                    if (!CastGestureEnabled) { _cast = false; _pointSince = -1f; }
                    else if (!_cast)
                    {
                        // client wave 15 (w15cast), point 32: at once (_pointSince: when it opened)
                        if (!_onTrigger) { _cast = true; _pointSince = now; }
                        else _pointSince = -1f;
                    }
                    else if (_onTrigger && _pointSince >= 0f && now - _pointSince < CastStartSeconds)
                    {
                        // a blip: the finger is back on the trigger at once: withdrawn, nothing taken, the trigger's click is its own again
                        _cast = false; _pointSince = -1f; _castWithdrawn = true; CastBlips++;
                    }
                }
                if (_cast || WandHeld || GrabHolding) { _click = false; _pullDown = false; } // (merge: w14laser's pull-down; w14cast removed the item buttons' shift)
                _wandBtn = WandHeld && WandButtons;
                if (GrabHolding || _sizeClaim) _camGrip = false; // (resize mode: a grip that holds a panel moves no table and takes no board; client wave 21, w21wand: nor one that holds or resizes the wand)
                else if (_tableClaim) _camGrip = true; // client wave 14 (w14laser), point 23: the table's, as in resize mode (the item buttons keep theirs)
            }

            // ------------------------------------------------------------------ client wave 14 (w14laser), point 23: the Table view's free grab
            // design note (2026-10-07): "On the table camera mode, I can no longer move the world around without toggling the resize mode ... specifically
            // for the table mode, allow the player to grab and resize. Freely." In the TABLE view a grip that starts in empty air is the table's
            // until it is let go, exactly as a grip in resize mode is (<see cref="CameraGrip"/>: the walking hand's grip + its stick moves the
            // table, both grips resize it; a tracked hand: its fist), with no resize mode needed. Decided once, in the frame the grip starts and
            // AFTER the panel grabs (<see cref="ClaimForGrab"/>: an edge, the laser on an edge, the bag): the game's probe (<see cref="TableProbe"/>,
            // LokiGame.W14Laser.cs) says no where the grip has another job (the wand at its holster, the pointing hand's laser on a panel, the
            // casting gesture's pointing finger, Reposition, the holster's placement, the skill wheels, another view). A claimed grip opens no
            // casting gesture; the trigger keeps its jobs, and so do the face buttons (the item buttons' shift: grip + a face button).
            /// <summary>Asked once in the frame a grip starts (not in resize mode): is it the table's (the Table view's free grab)?</summary>
            public static System.Func<Hand, bool> TableProbe;
            private bool _tableClaim, _tableGripWas;
            /// <summary>This hand's grip holds the table (point 23) until it is let go.</summary>
            public bool TableClaimed { get { Sync(); return _tableClaim; } }
            /// <summary>Table grips claimed so far (smoke, log).</summary>
            public int TableClaims { get; private set; }
            private void ClaimForTable()
            {
                // the same squeeze as resize mode's camera grip (TableGripHeld: the Index's force), a tracked hand's fist
                bool held = _profile == Profile.HandJoints ? Joints.Fist : _gripHeld;
                bool down = held && !_tableGripWas; _tableGripWas = held;
                if (!held) { _tableClaim = false; return; }
                if (!down || _tableClaim || ResizeMode || GrabHolding || WandHeld || _cast || TableProbe == null) return; // (a casting gesture open already: its grip)
                bool take = false;
                try { take = TableProbe(this); } catch (System.Exception e) { Debug.LogException(e); }
                if (take) { _tableClaim = true; TableClaims++; }
            }

            // ------------------------------------------------------------------ client wave 21 (w21wand): resize mode's grips on the wand
            // design note (2026-10-11): "gives me the ability to resize the wand itself (during resize mode, by grabbing it with both hands)". In
            // resize mode a plain grip is the camera's (<see cref="CameraGrip"/>: the table's move, both grips its resize, a board's hold).
            // A grip that starts at the wand's holster there takes the wand for sizing instead, and while one hand holds it so the other
            // hand's grip resizes it (the game's probe: <see cref="WandSizeProbe"/>, LokiGame.W21Wand.cs): decided once, in the frame the
            // grip starts and after the panel grabs (<see cref="ClaimForGrab"/>), and the wand's until it is let go: no camera grip from
            // it meanwhile (the table neither moves nor resizes, no board is taken). Outside resize mode it is never asked.
            /// <summary>Asked once in the frame a grip starts in resize mode: is it the wand's (it takes the wand for sizing, or resizes it)?</summary>
            public static System.Func<Hand, bool> WandSizeProbe;
            private bool _sizeClaim, _sizeGripWas;
            /// <summary>This hand's grip holds or resizes the wand (resize mode) until it is let go.</summary>
            public bool WandSizeClaimed { get { Sync(); return _sizeClaim; } }
            /// <summary>Grips claimed so far (smoke, log).</summary>
            public int WandSizeClaims { get; private set; }
            private void ClaimForWandSize(bool grip)
            {
                bool down = grip && !_sizeGripWas; _sizeGripWas = grip;
                if (!grip) { _sizeClaim = false; return; }
                if (!down || _sizeClaim || !ResizeMode || GrabHolding || WandSizeProbe == null) return;
                bool take = false;
                try { take = WandSizeProbe(this); } catch (System.Exception e) { Debug.LogException(e); }
                if (take) { _sizeClaim = true; WandSizeClaims++; }
            }

            // ------------------------------------------------------------------ client wave 13 (w13grab), items 19 and 20: the panel grab's claim
            // design note (2026-10-07): "allow the player to move around regular HUD panels ... by grabbing the edges with their hand or the laser" and "Add
            // a 'backpack grab' ... Be careful not to allow this (or ANY other panel grab) to badly interact with the casting systems (as in, when
            // trying to grab a panel you'll accidentally open an ability casting menu)". ONE claim, decided in the frame the grip starts and
            // BEFORE the casting gesture's 0.1 s start timer: the game's probe (<see cref="GrabProbe"/>, LokiGame.W13Grab.cs) says whether this
            // hand is at a panel's edge band, its laser on one (the pointing hand), or the hand in the backpack zone behind its shoulder. Yes:
            // the grip is the GRAB's from its first frame until it is let go: no casting gesture (pad / flick, whatever the index finger does),
            // no item buttons (the face buttons keep their own jobs), no click of this hand, no skill wheels from its trigger (and, let go
            // with the trigger still held, not until that trigger is released: <see cref="ClaimForMenu"/>'s rule), no camera grip in resize
            // mode; the game takes no wand with it and Reposition / the holster's placement take nothing with it. No: the grip is whatever it
            // was before (a casting gesture, the wand, the item buttons, resize mode's camera, Reposition) and it never becomes a grab, however
            // the hand moves over an edge or the zone later: the probe is asked once, when the grip starts.
            /// <summary>Asked once in the frame a grip starts: does it take a panel by its edge or pull the bag out (LokiGame.W13Grab.cs)?</summary>
            public static System.Func<Hand, bool> GrabProbe;
            private bool _grabClaim, _grabGripWas;
            /// <summary>This hand's grip belongs to a panel grab (item 19) or the backpack's pull (item 20) until it is let go (client wave 14,
            /// point 12: or its trigger holds a panel by the laser: <see cref="TriggerGrabbing"/>).</summary>
            public bool GrabClaimed { get { Sync(); return GrabHolding; } }
            private bool GrabHolding => _grabClaim || _trigGrab;
            /// <summary>Grips claimed so far (smoke, log).</summary>
            public int GrabClaims { get; private set; }
            private void ClaimForGrab(bool grip)
            {
                bool down = grip && !_grabGripWas; _grabGripWas = grip;
                if (!grip)
                {
                    // client wave 14 (w14ui), point 12: the pointing hand's trigger still pulled: it goes on holding the panel (a held trigger
                    // drags as the grip does) until it is let go
                    if (_grabClaim && IsMain && TriggerHeldForGrab()) { _grabClaim = false; _trigGrab = true; TriggerGrabHandovers++; return; }
                    // let go: a trigger still held is not the skill wheels' until it is released (as the menu ring's pull)
                    if (_grabClaim && TriggerValue >= MenuReleaseLevel) _menuClaim = true;
                    _grabClaim = false; return;
                }
                if (!down || _grabClaim || _trigGrab || WandHeld || GrabProbe == null) return;
                bool take = false;
                try { take = GrabProbe(this); } catch (System.Exception e) { Debug.LogException(e); }
                if (take) { _grabClaim = true; GrabClaims++; }
            }

            // ------------------------------------------------------------------ client wave 14 (w14ui), point 12: the trigger drags a panel too
            // design note (2026-10-07): "When I have my laser on the side of a panel, and I'm about to drag it, I also want to be able to drag it by
            // pressing the trigger and not just the grip." A trigger pull of the POINTING hand (its click: the pull reaching its click point; a
            // tracked hand: its pinch) while its laser is on a panel's edge band (the game's probe: <see cref="TriggerGrabProbe"/>, the same
            // candidate as the grip's laser grab, LokiGame.W13Grab.cs) is the grab's: no click (a short pull on the edge still clicks nothing),
            // and the panel is held exactly as by the grip (<see cref="GrabClaimed"/>: no casting gesture, no item buttons, no face buttons, no
            // camera) until the trigger is let go (under <see cref="TriggerPullOff"/>; a tracked hand: its pinch opened under 0.5). A button
            // under the laser still wins (the laser is on no band there: VR/DioramaRig.cs). The grip and the trigger hand the hold over to each
            // other: let one go while the other is held, the panel stays held until that one is let go too.
            /// <summary>Asked in the frame the pointing hand's trigger clicks: does the laser take a panel by its edge (LokiGame.W14Ui.cs)?</summary>
            public static System.Func<Hand, bool> TriggerGrabProbe;
            private bool _trigGrab;
            /// <summary>This hand's TRIGGER holds a panel (point 12), until it is let go.</summary>
            public bool TriggerGrabbing { get { Sync(); return _trigGrab; } }
            /// <summary>Trigger grabs begun; holds handed from the grip to the trigger or back (smoke, log).</summary>
            public int TriggerGrabs { get; private set; }
            public int TriggerGrabHandovers { get; private set; }
            private bool TriggerHeldForGrab() => _profile == Profile.HandJoints ? _jointHeld && !Joints.Fist : TriggerRaw >= TriggerPullOff || TriggerPressed.IsPressed();
            private void ClaimForTrigger(bool grip)
            {
                if (_trigGrab)
                {
                    if (TriggerHeldForGrab()) return;
                    _trigGrab = false;
                    if (grip) { _grabClaim = true; TriggerGrabHandovers++; } // (the grip goes on holding it: the grip's claim from here)
                    return;
                }
                if (!_click || !IsMain || _grabClaim || _cast || WandHeld || TriggerGrabProbe == null) return;
                bool take = false;
                try { take = TriggerGrabProbe(this); } catch (System.Exception e) { Debug.LogException(e); }
                if (take) { _trigGrab = true; _click = false; TriggerGrabs++; }
            }
            internal void EndFrame() { _wasTrigger = Trigger.ReadValue<float>() > 0.7f; }

            /// <summary>w4xr: whether the controller on this hand is in use. Its isTracked / trackingState controls (every OpenXR controller
            /// layout declares them, e.g. MetaQuestTouchPlusControllerProfile.cs:151-158) are trusted only once this device has reported
            /// itself tracked: a device whose runtime never fills them stays "in use", so hand joints never take a working controller's
            /// place (no headset here to see what each runtime writes: a runtime check).</summary>
            private bool DeviceTracked(InputDevice d)
            {
                if (!(d is TrackedDevice t)) return true;
                bool now = t.isTracked.isPressed || t.trackingState.ReadValue() != 0;
                if (now) { _everTracked.Add(d.deviceId); return true; }
                return !_everTracked.Contains(d.deviceId);
            }
            private readonly System.Collections.Generic.HashSet<int> _everTracked = new System.Collections.Generic.HashSet<int>();
        }

        public readonly Hand Left = new Hand("LeftHand");
        public readonly Hand Right = new Hand("RightHand");
        public readonly InputAction HeadPosition = new InputAction("HeadPos", InputActionType.Value, "<XRHMD>/centerEyePosition");
        public readonly InputAction HeadRotation = new InputAction("HeadRot", InputActionType.Value, "<XRHMD>/centerEyeRotation");
        /// <summary>Client wave 11 (w11menu): the two eyes' positions (XRHMD's leftEyePosition / rightEyePosition controls, in the rig's
        /// space): the gaze calibration moves the gaze ray's start between them (VR/DioramaRig.Ui.cs HalfEyeDistance).</summary>
        public readonly InputAction LeftEyePosition = new InputAction("LeftEyePos", InputActionType.Value, "<XRHMD>/leftEyePosition");
        public readonly InputAction RightEyePosition = new InputAction("RightEyePos", InputActionType.Value, "<XRHMD>/rightEyePosition");

        public LokiInput()
        {
            Left.Owner = this; Right.Owner = this;
            HeadPosition.AddBinding("<XRHMD>/devicePosition");
            HeadRotation.AddBinding("<XRHMD>/deviceRotation");
            Left.Enable(); Right.Enable(); HeadPosition.Enable(); HeadRotation.Enable(); LeftEyePosition.Enable(); RightEyePosition.Enable();
        }

        /// <summary>Which hand is the laser / turning hand (Comfort.LeftHanded).</summary>
        public void SetMainHand(System.Func<bool> leftIsMain) { Left.IsMainHand = leftIsMain; Right.IsMainHand = () => !leftIsMain(); }

        private int _frame = -1;
        /// <summary>Called by whoever reads the hands first in a frame (the rig's Update runs first, LokiHud's after): idempotent per frame.</summary>
        public void BeginFrame() { if (_frame == Time.frameCount) return; _frame = Time.frameCount; Left.BeginFrame(); Right.BeginFrame(); }
        public void EndFrame() { Left.EndFrame(); Right.EndFrame(); }

        public void Dispose()
        {
            Left.Disable(); Right.Disable(); HeadPosition.Disable(); HeadRotation.Disable(); LeftEyePosition.Disable(); RightEyePosition.Disable();
        }
    }
}
