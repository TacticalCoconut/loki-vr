using System.Collections.Generic;
using Loki.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

namespace Loki.VR
{
    /// <summary>
    /// Room-scale rig built at runtime: head camera + two hands (Input System TrackedPoseDriver, OpenXR),
    /// the map as a tabletop diorama in front of the player, and the first controller interactions:
    ///   right trigger  = click what the ray points at: UI button, monster (attack), NPC (talk), item (pick up), ground (walk)
    ///   right stick X  = smooth turn around the head's vertical axis
    ///   right stick click = turn your character in place towards what the laser points at (client wave 2)
    ///   left stick     = pan the diorama, left stick click = recentre
    ///   both grips     = grab-scale the diorama (pull hands apart / together)
    /// Client wave 13 (w13resize): the table pans and resizes only in RESIZE MODE (the overhead menu's button: VR/OverheadMenu.cs,
    /// LokiGame.W13Resize.cs; design note, 2026-10-07), by a plain grip then (LokiInput.Hand.CameraGrip); outside it no grip moves anything
    /// (wave 12's full grip is gone). The click is LokiInput.Hand.ClickDown: one per trigger pull.
    /// Client wave 4 (w4vr): these are the Index / Touch names; every other OpenXR profile's stand-ins are LokiInput.Hand's (the
    /// Settings window's Controls page lists them), the laser leaves from the device's aim pose, and tracked hands (no stick) move the
    /// table by grabbing it with the walking hand (a fist) and moving it.
    /// Client wave 4b (w4xr): hands tracked by XR Hands are drawn from their joints and, without a tracked controller, read from them
    /// (VR/HandJoints.cs moves the hand transform to the palm then); the rest of the rig reads them like any other hand.
    /// The desktop window shows the left eye (Unity's default XR mirror).
    /// </summary>
    public sealed partial class DioramaRig : MonoBehaviour
    {
        public float TableHeight = 0.95f;
        public float TableDistance = 0.7f;
        public float MinScale = 0.15f, MaxScale = 8f; // relative to the initial table scale
        public float TurnSpeed = 90f;       // degrees per second at full stick
        public float TurnDeadzone = 0.15f;
        public bool PanEnabled = true, ScaleEnabled = true;
        public float PanSpeed = 1.2f; // metres per second at full stick

        public Camera Head { get; private set; }
        public Transform LeftHand { get; private set; }
        public Transform RightHand { get; private set; }
        public Transform Diorama { get; private set; }
        public Vector2Int? PointedCell { get; private set; }
        /// <summary>Free movement (wfree): the exact ground point under the laser, in cell coordinates (the centre of cell (x, y) is (x, y),
        /// as Sprites/Unit.cs DrawnCell): a laser / trigger walk ends there. Null whenever <see cref="PointedCell"/> is.</summary>
        public Vector2? PointedLocal { get; private set; }
        public Vector3? PointedWorld { get; private set; }

        private LokiInput _input;
        private LineRenderer _ray;
        private Transform _cursor;
        private UI.UiButton _hoverButton; private Sprites.Targetable _hoverTarget;
        public Sprites.Targetable HoveredTarget { get; private set; }
        public LokiInput Input => _input;
        /// <summary>The laser / trigger / turning hand: the right one, or the left in left-handed mode (Comfort.LeftHanded).</summary>
        public Transform PointerHand => Comfort.LeftHanded ? LeftHand : RightHand;
        /// <summary>The walking / wrist-menu / minimap hand.</summary>
        public Transform OffHand => Comfort.LeftHanded ? RightHand : LeftHand;
        public LokiInput.Hand Main => Comfort.LeftHanded ? _input.Left : _input.Right;
        public LokiInput.Hand Off => Comfort.LeftHanded ? _input.Right : _input.Left;
        /// <summary>Left-handed mode was switched: panels that float by the off hand re-attach.</summary>
        public event System.Action HandsSwapped;
        /// <summary>The world is turning this frame (smooth turn) — the comfort vignette uses it.</summary>
        public bool TurningNow { get; private set; }
        public bool PanningNow { get; private set; }
        /// <summary>Client wave 25 (w25tut), item 3: both grips are resizing the table now (the grab that began when the second grip
        /// closed; LokiGame.W25Tut.cs counts a resize only from one that begins in its own tutorial part).</summary>
        public bool ScalingNow => _grabbing;
        public event System.Action SnapTurned;
        private bool _snapArmed = true, _leftHandedSeen;
        public event System.Action<Sprites.Targetable> TargetClicked;
        /// <summary>Right trigger pressed while the laser was not on a UI button or a unit/item: (ground cell or null).</summary>
        public event System.Action<Vector2Int?> FreeTrigger;
        public event System.Action ViewCycleRequested;
        /// <summary>Client wave 19 (w19small), point 41: the walking hand's stick pressed and let go before <see cref="ViewHoldSeconds"/>: a
        /// tap (the diorama recentres; client wave 19, w19view41: nothing else, the chat is the elbow's chat button's). Held that long it asks
        /// for the next view instead, and no tap follows.</summary>
        public event System.Action StickTapped;
        public const float ViewHoldSeconds = 0.8f;
        /// <summary>Pointer-hand stick click (client wave 2): turn the character in place towards what the laser points at — the unit
        /// under it, else the ground cell, else (laser on nothing) the head's gaze. Arguments: the pointed unit / item, the pointed cell.</summary>
        public event System.Action<Sprites.Targetable, Vector2Int?> TurnRequested;
        private float _stickClickStart;
        private bool _handDragging; private Vector3 _handDragLast;
        /// <summary>Client wave 4 (w4vr): where the laser left from this frame, and whether that was the device's aim pose.</summary>
        public Vector3 PointerOrigin { get; private set; }
        public Vector3 PointerDirection { get; private set; }
        public bool LaserFromAimPose { get; private set; }
        /// <summary>Pointer-hand clicks seen (trigger, select or pinch; smoke).</summary>
        public int TriggerClicks { get; private set; }
        /// <summary>Client wave 13 (w13resize): pulls of the wand hand that did nothing in the world because a menu was open (smoke, log).</summary>
        public int WandMenuBlockedClicks { get; private set; }
        /// <summary>Client wave 13 (w13grab), item 19: the panel whose edge band the laser is on this frame (null: none), the laser's point on
        /// it, the trigger pulls that did nothing there; panels not to offer (held already: LokiGame.W13Grab.cs).</summary>
        public RectTransform LaserEdgePanel { get; private set; }
        public Vector3 LaserEdgePoint { get; private set; }
        public int EdgeClicks { get; private set; }
        public System.Func<RectTransform, bool> GrabSkip;
        /// <summary>Client wave 13 (w13grab): the pointer's ray as the laser leaves the hand at this moment (Update's own rule: the aim pose
        /// when the runtime gives one and the setting says so, else along the hand), whatever ran first this frame (a panel held by the
        /// laser rides it: LokiGame.W13Grab.cs).</summary>
        public Ray PointerRayNow()
        {
            if (Comfort.LaserFromAim && _input != null && Main.TryAim(out var ap, out var ar)) return new Ray(transform.TransformPoint(ap), transform.rotation * (ar * Vector3.forward));
            return new Ray(PointerHand.position, PointerHand.forward);
        }
        /// <summary>The laser's render queue: after the panels (Loki/UIOverlay, Transparent+100 = 3100).</summary>
        public const int LaserQueue = 3200;
        /// <summary>Client wave 14 (w14ui), point 1: the hands' render queue (controller cubes, tracked-hand joints): after the panels, as the laser.</summary>
        public const int HandQueue = LaserQueue;
        /// <summary>Buttons the eyes can choose (specified after the 2026-10-01 playtest: "choose text options in dialogue, such as
        /// close or responses by looking at them as well", then "add Gaze Choosing to all UI elements", the laser on a button winning):
        /// when the laser is on no button, the button the head looks at — if this accepts it (LokiGame: any shown button of any panel) —
        /// is hovered, and the trigger clicks it before anything the laser points at in the world. Null: no gaze choosing.</summary>
        public System.Func<UI.UiButton, bool> GazeChoosable;
        /// <summary>The radial skill wheels are open (LokiGame.Wheels.cs): the sticks pick slots and the pointer hand's trigger uses them,
        /// so here they do nothing — no click, no turning, no table move, no stick-click recentre / view switch / face. The laser still
        /// points (what it is on is where a target skill from the wheel goes).</summary>
        public bool InputHeld { get; set; }
        /// <summary>The button chosen by gaze this frame (null: none), and the clicks it got (smoke, log).</summary>
        public UI.UiButton GazedButton { get; private set; }
        /// <summary>Client wave 14 (w14menu), point 7: the button the pointing hand's LASER is on this frame (never the look's: UI/ButtonTip.cs).</summary>
        public UI.UiButton LaserButton { get; private set; }
        public int GazeClicks { get; private set; }
        /// <summary>Tests and the button strip: the view switch / recentre that holding / clicking the walking stick does.</summary>
        public void RequestViewCycle() => ViewCycleRequested?.Invoke();
        private float _grabStartDist, _grabStartScale;
        private Vector3 _grabStartMid, _grabStartPos;
        private bool _grabbing;

        public void Build(Transform diorama)
        {
            Diorama = diorama;
            _input = new LokiInput();
            _input.SetMainHand(() => Comfort.LeftHanded); // client wave 4 (w4vr): buttons mapped per hand role (LokiInput.Hand)
            Comfort.Load(); ApplyComfort(); _leftHandedSeen = Comfort.LeftHanded;
            Comfort.Changed += () => { ApplyComfort(); if (_leftHandedSeen != Comfort.LeftHanded) { _leftHandedSeen = Comfort.LeftHanded; HandsSwapped?.Invoke(); } };

            // Try to get floor-level tracking so the table height is real-world height.
            var subsystems = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            foreach (var s in subsystems) s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);

            var camGo = new GameObject("Head");
            camGo.transform.SetParent(transform, false);
            camGo.tag = "MainCamera";
            Head = camGo.AddComponent<Camera>();
            Head.nearClipPlane = 0.02f; Head.farClipPlane = 200f;
            Head.clearFlags = CameraClearFlags.SolidColor;
            Head.backgroundColor = new Color(0.05f, 0.06f, 0.09f);
            camGo.AddComponent<AudioListener>();
            var tpd = camGo.AddComponent<TrackedPoseDriver>();
            tpd.positionInput = new InputActionProperty(_input.HeadPosition);
            tpd.rotationInput = new InputActionProperty(_input.HeadRotation);
            tpd.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            tpd.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

            LeftHand = MakeHand("LeftHand", _input.Left, new Color(0.3f, 0.6f, 1f));
            RightHand = MakeHand("RightHand", _input.Right, new Color(1f, 0.5f, 0.3f));

            var rayGo = new GameObject("PointerRay");
            rayGo.transform.SetParent(RightHand, false);
            _ray = rayGo.AddComponent<LineRenderer>();
            _ray.useWorldSpace = true; _ray.positionCount = 2; _ray.startWidth = 0.004f; _ray.endWidth = 0.002f;
            // drawn after the panels and with no depth test (specified after the 2026-10-01 playtest: the laser "doesn't actually show up
            // in front of UI components"): the panels use Loki/UIOverlay — ZTest Always, queue Transparent+100 (Resources/Shaders/
            // LokiUIOverlay.shader) — so they painted over a Sprites/Default line (queue Transparent, depth-tested) even where it ran in
            // front of them. The line ends where the ray hits (below), so drawing it on top shows nothing past what it points at.
            var raySh = Shader.Find("Loki/UIOverlay");
            _ray.material = new Material(raySh != null ? raySh : Shader.Find("Sprites/Default")) { name = "LokiLaser", renderQueue = LaserQueue };
            // client wave 11 (w11menu): and AFTER the panels in Unity's own order: transparent renderers are sorted by sorting order before
            // the render queue, the panels' canvases have order 10 (UI/UiKit.cs), so at order 0 the queue above changed nothing over a
            // panel (design note, 2026-10-06: "the laser only disappears when I point at UI elements"; LaserSortingOrder, VR/DioramaRig.Ui.cs)
            _ray.sortingOrder = LaserSortingOrder;
            _ray.startColor = new Color(1f, 0.9f, 0.4f, 0.9f); _ray.endColor = new Color(1f, 0.9f, 0.4f, 0.1f);

            _cursor = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
            _cursor.name = "CellCursor";
            Object.Destroy(_cursor.GetComponent<Collider>());
            _cursor.GetComponent<Renderer>().material = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.4f, 1f, 0.5f, 0.6f) };
            _cursor.gameObject.SetActive(false);

            Hands = gameObject.AddComponent<HandJoints>(); Hands.Init(this); // client wave 4b (w4xr): XR Hands joints, visual, joint-read hands
            BuildOffLaser(raySh); // client wave 14 (w14laser), point 10: the walking hand's laser, on panels only (VR/DioramaRig.Laser.cs)
            RecentreDiorama();
        }

        /// <summary>Client wave 4b (w4xr): tracked hands from XR Hands (VR/HandJoints.cs).</summary>
        public HandJoints Hands { get; private set; }

        private void ApplyComfort()
        {
            float h = Comfort.EffectiveTableHeight;
            if (Diorama != null && Mathf.Abs(h - TableHeight) > 1e-3f) Diorama.position += Vector3.up * (h - TableHeight);
            TableHeight = h; TurnSpeed = Comfort.TurnSpeed;
        }

        private Transform MakeHand(string name, LokiInput.Hand hand, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var tpd = go.AddComponent<TrackedPoseDriver>();
            tpd.positionInput = new InputActionProperty(hand.Position);
            tpd.rotationInput = new InputActionProperty(hand.Rotation);
            tpd.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            tpd.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            var vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vis.name = "vis";
            Object.Destroy(vis.GetComponent<Collider>());
            vis.transform.SetParent(go.transform, false);
            vis.transform.localScale = new Vector3(0.03f, 0.03f, 0.08f);
            // client wave 14 (w14ui), point 1 (design note: "on the login screen, only the dominant hand is visible. Both of them should be visible"):
            // the panels are drawn over everything with no depth test (Loki/UIOverlay, queue Transparent+100, canvases of sorting order 10),
            // so a hand in front of the login panel (1100 x 900 px at 0.95 m) was painted over: only the pointing hand's laser, drawn after
            // the panels, showed. The hands are drawn after the panels too (the laser's queue and order), still depth-tested against the world
            var vr = vis.GetComponent<Renderer>();
            vr.material = new Material(Shader.Find("Sprites/Default")) { color = color, renderQueue = HandQueue };
            vr.sortingOrder = LaserSortingOrder;
            return go.transform;
        }

        public void RecentreDiorama()
        {
            if (Diorama == null) return;
            float yaw = Head != null ? Head.transform.eulerAngles.y : 0f;
            var fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            var headPos = Head != null ? Head.transform.position : Vector3.zero;
            Diorama.position = new Vector3(headPos.x, 0, headPos.z) + fwd * TableDistance + Vector3.up * TableHeight;
            Diorama.rotation = Quaternion.Euler(0, yaw, 0);
            Recentred?.Invoke();
        }

        /// <summary>After a recentre (left stick click) — the game uses it to bring the player's character to the table centre.</summary>
        public event System.Action Recentred;

        private void Update()
        {
            if (_input == null || Diorama == null) return;
            _input.BeginFrame();
            // client wave 11 (w11menu): half the distance between the eyes as the headset reports it (the gaze calibration's sideways travel)
            float eyes = Vector3.Distance(_input.LeftEyePosition.ReadValue<Vector3>(), _input.RightEyePosition.ReadValue<Vector3>());
            HalfEyeDistance = eyes > 0.04f && eyes < 0.09f ? eyes * 0.5f : DefaultHalfEyeDistance;
            // "R" is the pointer / turning hand and "L" the walking / panning one; left-handed mode swaps them (Comfort.LeftHanded)
            var L = Off; var R = Main;

            // --- pointer ray: UI buttons > units / ground items > ground cells ---
            // client wave 4 (w4vr): from the device's aim pose (OpenXR /input/aim/pose: the pointing ray each runtime defines for its
            // controllers and for tracked hands, Documentation~/input.md:150-159), else along the controller (grip pose), as before
            var origin = PointerHand.position;
            var dir = PointerHand.forward;
            LaserFromAimPose = false;
            if (Comfort.LaserFromAim && R.TryAim(out var aimPos, out var aimRot))
            {
                origin = transform.TransformPoint(aimPos); dir = transform.rotation * (aimRot * Vector3.forward);
                LaserFromAimPose = true;
            }
            PointerOrigin = origin; PointerDirection = dir;
            PointedCell = null; PointedWorld = null; PointedLocal = null;
            UI.UiButton button = null; Sprites.Targetable target = null; bool onBlocker = false; Transform laserPanel = null;
            // ClickDown: one click per trigger pull (VR/LokiInput.cs; client wave 13: whatever the grip does)
            bool click = R.ClickDown && !InputHeld; // the radial wheels take the trigger while open
            if (click) TriggerClicks++;
            // client wave 11 (w11menu): every collider on the ray, not only the first: on the panel the laser meets first, the button on
            // top wins (a row and the Use / Equip button on it have their faces in one plane: VR/DioramaRig.Ui.cs)
            // client wave 14 (w14ui), point 18 (design note: "the laser gets interrupted by the ground and I can't click on the menu"): the PANELS win over
            // the world: the nearest panel collider on the ray (a button or a panel's background), whatever of the world lies in front of it,
            // else the nearest collider (VR/DioramaRig.Ui.cs LaserHit; the panels are drawn over the world, so this is what the eye sees there)
            int hitCount = Physics.RaycastNonAlloc(origin, dir, UiHits, 50f, ~0, QueryTriggerInteraction.Ignore), nearest = LaserHit(hitCount, out bool panelOverWorld);
            LaserPanelOverWorld = panelOverWorld;
            if (nearest >= 0)
            {
                var hit = UiHits[nearest];
                _ray.SetPosition(0, origin); _ray.SetPosition(1, hit.point);
                PointedWorld = hit.point;
                button = hit.collider.GetComponent<UI.UiButton>();
                onBlocker = hit.collider.GetComponent<UI.UiBlocker>() != null;
                if (button != null || onBlocker) { laserPanel = UI.UiButton.PanelOf(hit.collider.transform); var top = TopButton(hitCount, laserPanel); if (top != null) button = top; }
                if (button == null && !onBlocker) target = hit.collider.GetComponentInParent<Sprites.Targetable>();
                var map = LokiBootstrap.Instance != null ? LokiBootstrap.Instance.Map : null;
                bool isGround = button == null && target == null && hit.collider.GetComponent<UI.UiBlocker>() == null;
                if (isGround && map != null && map.Current != null)
                {
                    var local = map.transform.InverseTransformPoint(hit.point); // Loki units = GAT cells
                    int cx = Mathf.FloorToInt(local.x), cy = Mathf.FloorToInt(local.z);
                    if (map.Current.Gat.InBounds(cx, cy))
                    {
                        PointedCell = new Vector2Int(cx, cy);
                        PointedLocal = new Vector2(local.x - 0.5f, local.z - 0.5f); // free movement (wfree)
                        _cursor.gameObject.SetActive(true);
                        float s = map.transform.lossyScale.x;
                        _cursor.position = map.transform.TransformPoint(new Vector3(cx + 0.5f, local.y, cy + 0.5f));
                        _cursor.rotation = map.transform.rotation;
                        _cursor.localScale = new Vector3(s * 0.9f, s * 0.05f, s * 0.9f);
                    }
                    else _cursor.gameObject.SetActive(false);
                }
                else _cursor.gameObject.SetActive(false);
            }
            else
            {
                _ray.SetPosition(0, origin); _ray.SetPosition(1, origin + dir * 3f);
                _cursor.gameObject.SetActive(false);
            }
            // client wave 13 (w13grab), item 19: the laser on a panel's EDGE BAND (UI/PanelGrab.cs: a grip now takes the panel there,
            // LokiGame.W13Grab.cs): the line ends on the band, nothing of the world is pointed at, and a trigger pull there does nothing
            // (no button, no use of the bag's locked item, nothing in the world: the design's "grabbing the edges", never a click)
            LaserEdgePanel = null;
            if (!InputHeld && !HidePointerRay)
            {
                bool firstUi = nearest >= 0 && (button != null || onBlocker);
                // (client wave 14, point 18: a band beyond the GROUND counts too, as the panels win over the ground; a unit nearer on the laser
                // still hides a band behind it: the band is outside the panel, where the unit is what the eye sees)
                float bandLimit = firstUi || target != null ? UiHits[nearest].distance : float.MaxValue;
                var edge = UI.PanelGrab.OnLaser(new Ray(origin, dir), bandLimit, firstUi, laserPanel, button != null,
                    nearest >= 0 ? UiHits[nearest].point : default, GrabSkip, out var edgeAt, out _);
                if (edge != null)
                {
                    LaserEdgePanel = edge; LaserEdgePoint = edgeAt;
                    target = null; PointedCell = null; PointedLocal = null; _cursor.gameObject.SetActive(false);
                    _ray.SetPosition(1, edgeAt);
                    if (click) { click = false; EdgeClicks++; Debug.Log($"[Grab] trigger on the edge of '{edge.name}': nothing (an edge is grabbed, never clicked)"); }
                }
            }
            // gaze choosing (GazeChoosable): with the laser on no button, the button the eyes look at, if accepted, is hovered and clicked.
            // Client wave 11 (w11menu): the calibrated gaze ray, the first panel it meets whatever of the world is in front of it, and
            // on that panel the button under it or the nearest one within a small angle, kept with a margin (VR/DioramaRig.Ui.cs)
            UI.UiButton gazed = null;
            var gaze = GazeRay;
            bool gazeUi = RayOnUi(gaze, 50f, out var gazeHit);
            GazeOnPanel = gazeUi; GazePanelPoint = gazeUi ? gazeHit.point : default; GazePanel = gazeUi ? UI.UiButton.PanelOf(gazeHit.collider.transform) : null;
            if (button == null && GazeChoosable != null) gazed = PickGazed(gaze, gazeUi, GazePanel, GazePanelPoint, gazeUi ? gazeHit.distance : 0f);
            else _gazeKeep = null;
            GazedButton = gazed; LaserButton = button;
            var chosen = button != null ? button : gazed; // the laser on a button wins
            if (chosen != _hoverButton)
            {
                if (_hoverButton != null && _hoverButton != _offHover) _hoverButton.SetHover(false); // (client wave 14, w14laser: the walking hand's laser may be on it too)
                _hoverButton = chosen;
                if (chosen != null) { chosen.SetHover(true); if (button != null) Haptics.Pulse(Haptics.Kind.Ui, true, 0.08f, 0.015f); }
            }
            if (target != _hoverTarget) { if (_hoverTarget != null) _hoverTarget.SetHover(false); _hoverTarget = target; if (target != null) target.SetHover(true); }
            HoveredTarget = target;
            UpdateDrag(R, origin, dir); // a slider's knob follows the laser while the trigger is held (client wave 11)
            LaserPanel = laserPanel; UpdateGrab(L, R, origin, dir); // a board held by the grip follows the laser (client wave 11, part 2)
            ClickedThisFrame = click; PressedThisFrame = click ? chosen : null;
            if (click && chosen != null)
            {
                if (button == null) { GazeClicks++; Debug.Log($"[Diorama] trigger on the gazed button '{gazed.name}'"); }
                Press(chosen, button != null, button != null ? PointedWorld.Value : GazePanelPoint); Haptics.Pulse(Haptics.Kind.Ui, true, 0.25f, 0.03f);
            }
            // client wave 11 (w11menu): while the gaze is on a panel, a pull that presses no button does nothing in the world (design note,
            // 2026-10-06: "if I'm looking at the menu and there's something behind it, that thing will get chosen instead", and the
            // same day, after a version that let the laser through while the look rested on the chat: "require any UI Panel to block
            // gaze selection"): ANY panel with a background (chat, the forearm bar, boards, the wrist menu, windows) hides what is
            // behind it, and a hand resting with its laser on the ground or on a unit acts on nothing meanwhile
            // client wave 13 (w13items), item 12: a pull that pressed no button, on a panel (the laser's, else the look's), may be that panel's
            // own (the bag's: it uses the locked item); taken, it does nothing else
            // client wave 13 (w13look), items 23 and 24: the look's panel takes a pull only while the look may choose there ("Look select" on,
            // the panel's eye open); while "Look select" is off the look holds no laser pull back either: the laser alone decides
            var pullPanel = laserPanel != null ? laserPanel : GazeOnPanel && UI.LookSelect.PanelChoosable(GazePanel) ? GazePanel : null;
            // client wave 23 (w23tree): first of all, a pull that pressed no button may be the skill trees' (it takes the locked skill's
            // lock off when it landed on nothing: LokiGame.W22Tree.cs W23TreeEmptyPull); taken, it does nothing else
            if (click && chosen == null && EmptyPull != null && EmptyPull()) { EmptyPulls++; Debug.Log("[Diorama] trigger on no button: taken by the skill trees (the lock is off), nothing in the world"); }
            else if (click && chosen == null && pullPanel != null && PanelPull != null && PanelPull(pullPanel)) { PanelPulls++; Debug.Log($"[Diorama] trigger on the panel '{pullPanel.name}' taken by it"); }
            else if (click && chosen == null && GazeOnPanel && GazeChoosable != null && UI.LookSelect.UiOn)
            {
                GazeBlockedClicks++;
                Debug.Log($"[Diorama] trigger ignored: the gaze is on the panel '{(GazePanel != null ? GazePanel.name : "?")}' and on no button of it");
            }
            else if (click && chosen == null && BlockWorldClicks != null && BlockWorldClicks()) { } // the launcher ring is open: the pull is its own (UI/MenuRing.cs)
            else if (click && chosen == null && MenuClicksOnly) { WandMenuBlockedClicks++; Debug.Log("[Diorama] trigger of the wand hand while a menu is open: nothing in the world"); } // client wave 13 (w13resize)
            else
            {
                if (click && chosen == null && target != null) TargetClicked?.Invoke(target);
                if (click && chosen == null && target == null && !onBlocker)
                {
                    if (PointedCell.HasValue) Debug.Log($"[Diorama] trigger on cell {PointedCell.Value.x},{PointedCell.Value.y}");
                    FreeTrigger?.Invoke(PointedCell);
                }
            }
            // the pointer hand's stick: the slider / scrolling text under the laser or the gaze takes it (client wave 11)
            var pstick = PointerStick(R);
            StickTakenByUi = chosen != null && chosen.Stick != null && !InputHeld && chosen.Stick.OnStick(pstick, Time.unscaledDeltaTime);
            UpdateGazeDot(gaze);
            _laserCream = OnCream(LaserPanel); // client wave 21 (w21kit): over the theme's cream the line is drawn in colours that show on it (UI/Theme.cs LaserIdle / LaserOnButton)
            _ray.startColor = LaserEdgePanel != null ? UI.PanelGrab.Ready : button != null ? (_laserCream ? UI.Theme.LaserOnButton : new Color(0.5f, 0.9f, 1f, 0.95f)) : target != null ? new Color(1f, 0.55f, 0.4f, 0.95f) : _laserCream ? UI.Theme.LaserIdle : new Color(1f, 0.9f, 0.4f, 0.9f); // (client wave 13, w13grab: the grab's colour on an edge)
            // client wave 14 (w14laser), point 20: drawn on what it can act on (panels, units, the ground while "Laser walking" is on), faded in
            // near a panel or a unit, hidden elsewhere (VR/DioramaRig.Laser.cs)
            UpdateLaserShown(origin, dir, button != null || onBlocker || LaserEdgePanel != null, target != null);
            bool drawRay = !HidePointerRay && LaserAlpha > LaserAlphaMin;
            if (_ray.enabled != drawRay) _ray.enabled = drawRay; // client wave 12 (w12cast): the wand's own line instead (VR/DioramaRig.Ui.cs)
            UpdateOffLaser(L); // client wave 14 (w14laser), point 10: the walking hand's laser presses panel buttons with its own trigger
            // --- turn in place (pointer-hand stick click): CZ_CHANGE_DIRECTION towards the pointed unit / cell (client wave 2) ---
            if (R.StickClickDown && !InputHeld && button == null && !onBlocker) TurnRequested?.Invoke(target, PointedCell);

            // --- turning (pointer-hand stick X): the diorama turns around the vertical line through the head — smoothly, or in snaps ---
            float rx = InputHeld || StickTakenByUi ? 0f : pstick.x;
            TurningNow = false;
            if (Comfort.SnapTurn)
            {
                if (_snapArmed && Mathf.Abs(rx) > 0.7f && !_grabbing)
                {
                    _snapArmed = false;
                    var hp = Head.transform.position;
                    Diorama.RotateAround(new Vector3(hp.x, Diorama.position.y, hp.z), Vector3.up, -Mathf.Sign(rx) * Comfort.SnapAngle);
                    SnapTurned?.Invoke();
                }
                else if (Mathf.Abs(rx) < 0.3f) _snapArmed = true;
            }
            else if (Mathf.Abs(rx) > TurnDeadzone && !_grabbing)
            {
                float t = (Mathf.Abs(rx) - TurnDeadzone) / (1f - TurnDeadzone);
                var hp = Head.transform.position;
                Diorama.RotateAround(new Vector3(hp.x, Diorama.position.y, hp.z), Vector3.up, -Mathf.Sign(rx) * t * t * TurnSpeed * Time.deltaTime);
                TurningNow = true;
            }

            // the grips for the table (moving, resizing): on the Valve Index a firm squeeze (LokiInput.Hand.TableGripHeld: its grip
            // force, specified after the 2026-10-01 playtest — a hand only holding it reads as gripping), elsewhere over 0.6 as before.
            // Client wave 13 (w13resize): and only in RESIZE MODE (LokiInput.ResizeMode; design note, 2026-10-07: "they can tap on a button
            // to toggle the resize mode. At which point they just have to grip the controller to control everything"): CameraGrip is
            // that grip while the mode is on, and false whenever it is off
            bool lHeld = L.CameraGrip, rHeld = R.CameraGrip;
            if (Grabbed != null) { lHeld = false; rHeld = false; } // client wave 11: the grips hold a menu board, not the table (VR/DioramaRig.Ui.cs)
            // client wave 14 (w14laser), point 11: exactly one camera grip (resize mode; the Table view's free grab) and no board held: that hand's
            // height steers the Follow view's pitch (VR/ViewController.cs); both grips resize, as before
            PitchHand = lHeld != rHeld ? (lHeld ? OffHand : PointerHand) : null;

            // --- pan (resize mode): the walking hand's grip and its stick (outside the mode the stick walks, grip or no grip) ---
            var ls = InputHeld || WalkStickTaken ? Vector2.zero : L.StickValue;
            PanningNow = false;
            if (PanEnabled && lHeld && !rHeld && ls.sqrMagnitude > 0.04f)
            {
                PanningNow = true;
                var yawRot = Quaternion.Euler(0, Head.transform.eulerAngles.y, 0);
                var move = yawRot * new Vector3(ls.x, 0, ls.y) * (PanSpeed * Time.deltaTime);
                Diorama.position -= move; // push the world the other way = "walk over the table"
            }
            // client wave 4 (w4vr): hands have no stick — a fist (grasp) with the walking hand drags the table instead
            // (the fist: every finger and the thumb curled, VR/HandJoints.cs; client wave 13: in resize mode only)
            bool handDrag = PanEnabled && ControllerProfiles.IsHand(L.Profile) && L.CameraGrip && !R.CameraGrip;
            if (handDrag)
            {
                var hp = OffHand.position;
                if (_handDragging) { var d = hp - _handDragLast; d.y = 0; if (d.sqrMagnitude > 1e-8f) { Diorama.position += d; PanningNow = true; } }
                _handDragLast = hp; _handDragging = true;
            }
            else _handDragging = false;
            if (L.StickClickDown && !InputHeld) _stickClickStart = Time.time;
            if (InputHeld) _stickClickStart = 0;
            // (client wave 19, w19small, point 41: the press is decided at its release, a tap, or at the hold time, the view: StickTapped)
            if (_stickClickStart > 0 && L.StickClickHeld && Time.time - _stickClickStart > ViewHoldSeconds) { _stickClickStart = 0; ViewCycleRequested?.Invoke(); }
            if (_stickClickStart > 0 && !L.StickClickHeld) { _stickClickStart = 0; RecentreDiorama(); StickTapped?.Invoke(); }

            // --- grab-scale with both grips, in resize mode (lHeld / rHeld above) ---
            bool bothGrips = ScaleEnabled && lHeld && rHeld;
            if (bothGrips)
            {
                float dist = Vector3.Distance(LeftHand.position, RightHand.position);
                var mid = (LeftHand.position + RightHand.position) * 0.5f;
                if (!_grabbing)
                {
                    _grabbing = true; _grabStartDist = Mathf.Max(dist, 0.05f); _grabStartScale = Diorama.localScale.x;
                    _grabStartMid = mid; _grabStartPos = Diorama.position;
                }
                else
                {
                    float k = dist / _grabStartDist;
                    float newScale = Mathf.Clamp(_grabStartScale * k, MinScale, MaxScale);
                    // scale about the midpoint between the hands
                    var pivot = _grabStartMid;
                    var rel = (_grabStartPos - pivot) * (newScale / _grabStartScale);
                    Diorama.localScale = Vector3.one * newScale;
                    Diorama.position = pivot + rel + (mid - _grabStartMid);
                }
            }
            else _grabbing = false;

            _input.EndFrame();
        }

        private void OnCellClicked(Vector2Int cell, Formats.GatFile.Cell gat)
        {
            Debug.Log($"[Diorama] clicked cell {cell.x},{cell.y} type={gat.Type} walkable={gat.Walkable}");
            CellClicked?.Invoke(cell);
        }

        public event System.Action<Vector2Int> CellClicked;

        private void OnDestroy() { _input?.Dispose(); }
    }
}
