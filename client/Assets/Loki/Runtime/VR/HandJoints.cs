using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Gestures;
using Profile = Loki.VR.ControllerProfiles.Profile;
using TrackedPoseDriver = UnityEngine.InputSystem.XR.TrackedPoseDriver;

namespace Loki.VR
{
    /// <summary>
    /// Client wave 4b (w4xr): tracked hands from XR Hands (com.unity.xr.hands 1.5.1, client/Packages/local), under w4vr's pinch / aim /
    /// grab. Every package file:line below is the vendored 1.5.1 (com.unity.xr.hands-1.5.1.tgz, package/Runtime/...).
    ///   joints   the XRHandSubsystem the build's "Hand Tracking Subsystem" OpenXR feature creates when the runtime offers
    ///            XR_EXT_hand_tracking (OpenXR/HandTracking.cs:127-152, :184-191; the provider registers only while the feature is
    ///            enabled, OpenXR/OpenXRHandProvider.cs:326-357). Its updater reads the hands in EarlyUpdate.XRUpdate and again before
    ///            rendering (XRHandProviderUtility.cs:102-160, :253-279), so they are fresh when Update runs. Found through
    ///            SubsystemManager, not HandTracking.subsystem, so a test subsystem (the smoke's) is read the same way. Joint poses are
    ///            in the XR origin's space (Documentation~/hand-data/xr-hand-data-model.md), which is the rig's local space here: the
    ///            head and the controllers are the rig's children driven by TrackedPoseDriver in that space (VR/DioramaRig.cs).
    ///   visual   26 joints and the bones of each finger, drawn for a hand that is tracked and holds no tracked controller (a bare
    ///            hand read from its joints, or w4vr's hand interaction profile); the hand's controller cube is hidden meanwhile.
    ///   input    a hand with no OpenXR device, or with one that stopped tracking (controllers put down), is read from its joints
    ///            (LokiInput.Hand.JointMode, profile HandJoints): pinch = XR Hands' own index-thumb pinch (XRFingerShapeMath.cs:272-290
    ///            and :431-446, 1 cm = 1, 4.5 cm = 0: the provider's defaults, XRHandSubsystemProvider.cs:306-328); fist = the full curl
    ///            of the middle, ring and little fingers (XRFingerShapeMath.cs:176-199, :337-360), 0 while pinching; the pointing ray =
    ///            Meta's aim pose when the runtime computes one (XR_FB_hand_tracking_aim: MetaAimHand, the "Meta Hand Tracking Aim"
    ///            feature, OpenXR/MetaHandTrackingAim.cs:79-147, MetaAim.cs:113-460), else the hand interaction profile's aim pose
    ///            through the subsystem (XRCommonHandGestures, XRHandSubsystem.cs:315-328), else Loki's own estimate: from a shoulder
    ///            under the head (15 cm down, 17 cm to the hand's side, the head's yaw) through the point between the index and thumb
    ///            knuckles (a design choice, not a reference: the headset shows if it points well). The hand's transform (wrist menu,
    ///            quick bar, button strip, the fallback laser, table drag / zoom) follows the palm joint (+Z towards the fingers, +Y the
    ///            back of the hand: XRHandOrientationUtility.cs:63-73) while its TrackedPoseDriver is paused.
    ///   Meta's menu gesture (MetaAimFlags.MenuPressed, MetaAim.cs:85) on the walking hand opens the wrist menu (LokiInput.Hand).
    /// A tracked controller always wins over the joints (SteamVR also reports joints for hands holding Index controllers).
    /// </summary>
    [DefaultExecutionOrder(-100)] // before DioramaRig / LokiHud read the hands this frame
    public sealed class HandJoints : MonoBehaviour
    {
        /// <summary>The OpenXR features (com.unity.xr.hands) Editor/LokiSetup.cs enables for Standalone builds.</summary>
        public static readonly string[] FeatureNames = { "HandTracking", "MetaHandTrackingAim" };
        public const int JointCount = 26; // XRHandJointID.EndMarker.ToIndex() (Enums.cs:51-201)

        /// <summary>One hand: joints read from the subsystem, what Loki makes of them, and its visual.</summary>
        public sealed class Side : LokiInput.IJointHand
        {
            public readonly bool IsLeft;
            public bool Tracked { get; private set; }
            public float Pinch { get; private set; }
            public float Grasp { get; private set; }
            public bool MenuDown { get; private set; }
            // ---- client wave 12 (w12cast): the poses the full grip and the casting gesture read. XR Hands' own "full curl" of each
            // finger (XRFingerShapeMath.cs:176-199 and :337-360: 0 straight, 1 fully curled; the thumb from two joints with its own
            // ranges, XRHandSubsystemProvider.cs:284-289). A fist = the middle, ring and little fingers, the index AND the thumb
            // curled (the tracked hand's full grip); pointing = the index extended and the other three curled (the thumb free).
            // Each pose is entered at its "on" levels and left at the "off" levels (no flicker at a boundary). The levels are a
            // design, not measured on a real hand (no headset here): for visual inspection.
            public const float CurlOn = 0.6f, CurlOff = 0.45f, ThumbCurlOn = 0.35f, ThumbCurlOff = 0.2f, IndexStraightOn = 0.3f, IndexStraightOff = 0.45f;
            /// <summary>The mean full curl of the middle, ring and little fingers (unlike Grasp, not zeroed by a pinch), the index
            /// finger's and the thumb's. (Curl3 is also the tracked hand's plain grip: LokiInput.IJointHand.)</summary>
            public float Curl3 { get; private set; }
            public float IndexCurl { get; private set; }
            public float ThumbCurl { get; private set; }
            public bool Fist { get; private set; }
            public bool Pointing { get; private set; }
            /// <summary>Where the ray came from: "meta aim", "hand interaction", "joints" (smoke, log).</summary>
            public string AimSource { get; private set; } = "none";
            public string PinchSource { get; private set; } = "none";
            internal readonly Pose[] Poses = new Pose[JointCount];
            internal readonly bool[] Valid = new bool[JointCount];
            internal readonly float[] Radius = new float[JointCount];
            private Pose _aim; private bool _aimValid, _menuHeld;
            internal Side(bool left) { IsLeft = left; }

            public bool TryAim(out Vector3 pos, out Quaternion rot) { pos = _aim.position; rot = _aim.rotation; return Tracked && _aimValid; }
            public bool TryJoint(XRHandJointID id, out Pose pose) { int i = id.ToIndex(); pose = Poses[i]; return Tracked && Valid[i]; }

            /// <summary>The pose the hand's transform takes in joint mode: the palm (else the wrist).</summary>
            public bool TryHandPose(out Pose pose) => TryJoint(XRHandJointID.Palm, out pose) || TryJoint(XRHandJointID.Wrist, out pose);

            internal void Read(XRHandSubsystem sub, XRHand hand, Pose head, bool edges = true)
            {
                bool menuNow = false;
                Tracked = hand.isTracked;
                if (!Tracked) { Pinch = Grasp = 0f; Curl3 = IndexCurl = ThumbCurl = 0f; Fist = Pointing = false; _aimValid = false; AimSource = PinchSource = "none"; for (int i = 0; i < JointCount; i++) Valid[i] = false; }
                else
                {
                    for (int i = 0; i < JointCount; i++)
                    {
                        var j = hand.GetJoint(XRHandJointIDUtility.FromIndex(i));
                        Valid[i] = j.TryGetPose(out Poses[i]);
                        Radius[i] = j.TryGetRadius(out var r) ? r : 0f;
                    }
                    // the runtime's own values first: Meta's aim extension, then the hand interaction profile through the subsystem
                    var meta = IsLeft ? MetaAimHand.left : MetaAimHand.right;
                    var flags = meta != null ? (MetaAimFlags)meta.aimFlags.ReadValue() : MetaAimFlags.None;
                    var common = IsLeft ? sub.leftHandCommonGestures : sub.rightHandCommonGestures;
                    float pinch;
                    if ((flags & MetaAimFlags.Computed) != 0) { pinch = meta.pinchStrengthIndex.ReadValue(); PinchSource = "meta aim"; }
                    else if (common != null && common.TryGetPinchValue(out var cp)) { pinch = cp; PinchSource = "hand interaction"; }
                    else
                    {
                        var shape = hand.CalculateFingerShape(XRHandFingerID.Index, XRFingerShapeTypes.Pinch);
                        pinch = shape.TryGetPinch(out var jp) ? jp : 0f; PinchSource = "joints";
                    }
                    float curl = 0f; int n = 0;
                    foreach (var f in new[] { XRHandFingerID.Middle, XRHandFingerID.Ring, XRHandFingerID.Little })
                        if (hand.CalculateFingerShape(f, XRFingerShapeTypes.FullCurl).TryGetFullCurl(out var c)) { curl += c; n++; }
                    Pinch = Mathf.Clamp01(pinch);
                    Grasp = n > 0 && Pinch <= 0.5f ? Mathf.Clamp01(curl / n) : 0f; // a pinch is never a fist
                    // client wave 12 (w12cast): the fist and the pointing pose (see the constants above)
                    Curl3 = n > 0 ? Mathf.Clamp01(curl / n) : 0f;
                    IndexCurl = hand.CalculateFingerShape(XRHandFingerID.Index, XRFingerShapeTypes.FullCurl).TryGetFullCurl(out var ic) ? ic : 0f;
                    ThumbCurl = hand.CalculateFingerShape(XRHandFingerID.Thumb, XRFingerShapeTypes.FullCurl).TryGetFullCurl(out var tc) ? tc : 0f;
                    Fist = n > 0 && (Fist ? Curl3 >= CurlOff && IndexCurl >= CurlOff && ThumbCurl >= ThumbCurlOff : Curl3 >= CurlOn && IndexCurl >= CurlOn && ThumbCurl >= ThumbCurlOn);
                    Pointing = n > 0 && !Fist && (Pointing ? Curl3 >= CurlOff && IndexCurl <= IndexStraightOff : Curl3 >= CurlOn && IndexCurl <= IndexStraightOn);
                    if ((flags & (MetaAimFlags.Computed | MetaAimFlags.Valid)) == (MetaAimFlags.Computed | MetaAimFlags.Valid))
                    { _aim = new Pose(meta.devicePosition.ReadValue(), meta.deviceRotation.ReadValue()); _aimValid = true; AimSource = "meta aim"; }
                    else if (common != null && common.TryGetAimPose(out var ca)) { _aim = ca; _aimValid = true; AimSource = "hand interaction"; }
                    else { _aimValid = EstimateAim(head, out _aim); AimSource = "joints"; }
                    menuNow = (flags & MetaAimFlags.MenuPressed) != 0;
                }
                if (edges) { MenuDown = menuNow && !_menuHeld; _menuHeld = menuNow; } // the pre-render read keeps the frame's edge
            }

            /// <summary>Loki's own pointing ray from the joints (no reference: see the class comment).</summary>
            private bool EstimateAim(Pose head, out Pose aim)
            {
                aim = default;
                if (!TryJoint(XRHandJointID.IndexProximal, out var ip) || !TryJoint(XRHandJointID.ThumbProximal, out var tp)) return false;
                var knuckles = (ip.position + tp.position) * 0.5f;
                var yaw = Quaternion.Euler(0f, head.rotation.eulerAngles.y, 0f);
                var shoulder = head.position + Vector3.down * ShoulderDrop + yaw * Vector3.right * (IsLeft ? -ShoulderSide : ShoulderSide);
                var dir = knuckles - shoulder;
                if (dir.sqrMagnitude < 1e-6f) return false;
                aim = new Pose(knuckles, Quaternion.LookRotation(dir.normalized, yaw * Vector3.up));
                return true;
            }
        }

        /// <summary>The shoulder estimate of the joints' ray (metres under / beside the head).</summary>
        public const float ShoulderDrop = 0.15f, ShoulderSide = 0.17f;

        public Side Left { get; } = new Side(true);
        public Side Right { get; } = new Side(false);
        public XRHandSubsystem Subsystem => _sub;

        private DioramaRig _rig;
        private XRHandSubsystem _sub;
        private float _nextLook;
        private static readonly List<XRHandSubsystem> s_Subs = new List<XRHandSubsystem>();
        private Visual _visL, _visR;
        private bool _drivingL, _drivingR, _tpdWasL, _tpdWasR, _trackedLogL, _trackedLogR;

        public void Init(DioramaRig rig)
        {
            _rig = rig;
            rig.Input.Left.Joints = Left; rig.Input.Right.Joints = Right;
            _visL = new Visual(transform, true); _visR = new Visual(transform, false);
            LogFeatures();
        }

        private static void LogFeatures()
        {
            try
            {
                var s = UnityEngine.XR.OpenXR.OpenXRSettings.Instance;
                var ht = s != null ? s.GetFeature<UnityEngine.XR.Hands.OpenXR.HandTracking>() : null;
                var ma = s != null ? s.GetFeature<UnityEngine.XR.Hands.OpenXR.MetaHandTrackingAim>() : null;
                Debug.Log($"[HandJoints] XR Hands OpenXR features in this build: HandTracking {(ht == null ? "missing" : ht.enabled ? "on" : "off")}, MetaHandTrackingAim {(ma == null ? "missing" : ma.enabled ? "on" : "off")}; a subsystem appears when a runtime offers XR_EXT_hand_tracking");
            }
            catch (System.Exception e) { Debug.LogWarning("[HandJoints] features: " + e.Message); }
        }

        private void FindSubsystem()
        {
            if (_sub != null && _sub.running) return;
            if (_sub != null) { _sub.updatedHands -= OnUpdatedHands; Debug.Log("[HandJoints] XR Hands subsystem stopped"); _sub = null; }
            if (Time.unscaledTime < _nextLook) return;
            _nextLook = Time.unscaledTime + 0.5f;
            s_Subs.Clear(); SubsystemManager.GetSubsystems(s_Subs);
            foreach (var s in s_Subs)
                if (s != null && s.running)
                {
                    _sub = s; _sub.updatedHands += OnUpdatedHands;
                    int layout = 0; foreach (var b in s.jointsInLayout) if (b) layout++;
                    var d = s.subsystemDescriptor;
                    Debug.Log($"[HandJoints] XR Hands subsystem '{d?.id}' running: {layout} joints in its layout, runtime aim pose {d != null && d.supportsAimPose}, pinch value {d != null && d.supportsPinchValue}");
                    break;
                }
        }

        private Pose HeadPose => new Pose(_rig.Head.transform.localPosition, _rig.Head.transform.localRotation);

        private void Update()
        {
            if (_rig == null || _rig.Input == null) return;
            FindSubsystem();
            if (_sub != null) { Left.Read(_sub, _sub.leftHand, HeadPose); Right.Read(_sub, _sub.rightHand, HeadPose); }
            else { Left.Read(null, default, HeadPose); Right.Read(null, default, HeadPose); }
            LogTracking(Left, ref _trackedLogL); LogTracking(Right, ref _trackedLogR);
            _rig.Input.BeginFrame(); // decides each hand's profile (LokiInput.Hand.BeginFrame reads Joints.Tracked)
            Apply();
        }

        private static void LogTracking(Side s, ref bool was)
        {
            if (s.Tracked == was) return;
            was = s.Tracked;
            Debug.Log($"[HandJoints] {(s.IsLeft ? "left" : "right")} hand {(s.Tracked ? "tracked" : "lost")}{(s.Tracked ? $" (pinch from {s.PinchSource}, ray from {s.AimSource})" : "")}");
        }

        /// <summary>Before rendering: the subsystem read the hands again (XRHandProviderUtility.cs:261-267); move what shows them.</summary>
        private void OnUpdatedHands(XRHandSubsystem sub, XRHandSubsystem.UpdateSuccessFlags flags, XRHandSubsystem.UpdateType type)
        {
            if (type != XRHandSubsystem.UpdateType.BeforeRender || _rig == null || _rig.Input == null) return;
            Left.Read(sub, sub.leftHand, HeadPose, false); Right.Read(sub, sub.rightHand, HeadPose, false);
            Apply();
        }

        private void Apply()
        {
            ApplyHand(Left, _rig.Input.Left, _rig.LeftHand, _visL, ref _drivingL, ref _tpdWasL);
            ApplyHand(Right, _rig.Input.Right, _rig.RightHand, _visR, ref _drivingR, ref _tpdWasR);
        }

        private static void ApplyHand(Side s, LokiInput.Hand h, Transform hand, Visual vis, ref bool driving, ref bool tpdWas)
        {
            if (hand == null) return;
            var tpd = hand.GetComponent<TrackedPoseDriver>();
            var prof = h.Profile;
            if (prof == Profile.HandJoints)
            {
                if (!driving) { driving = true; tpdWas = tpd != null && tpd.enabled; if (tpd != null) tpd.enabled = false; }
                if (s.TryHandPose(out var p)) hand.SetLocalPositionAndRotation(p.position, p.rotation);
            }
            else if (driving) { driving = false; if (tpd != null) tpd.enabled = tpdWas; }
            // drawn for a bare hand: joints, or w4vr's hand interaction profile with the joints on top; never over a held controller
            bool show = s.Tracked && ControllerProfiles.IsHand(prof);
            vis.Set(s, show, h.TriggerValue > 0.7f);
            var cube = hand.Find("vis");
            if (cube != null && cube.gameObject.activeSelf == show) cube.gameObject.SetActive(!show);
        }

        private void OnDestroy()
        {
            if (_sub != null) _sub.updatedHands -= OnUpdatedHands;
            if (_rig != null && _rig.Input != null) { _rig.Input.Left.Joints = null; _rig.Input.Right.Joints = null; }
        }

        /// <summary>A hand drawn from its joints: a sphere per joint (the joint's radius when the runtime gives one) and a line along
        /// each finger's bones, in the rig's space; the index and thumb tips light up while pinching.</summary>
        private sealed class Visual
        {
            public readonly Transform Root;
            public readonly Transform[] Spheres = new Transform[JointCount];
            private readonly Renderer[] _rends = new Renderer[JointCount];
            private readonly LineRenderer[] _lines;
            private readonly Material _mat, _lit;
            public bool Shown { get; private set; }
            public int Drawn { get; private set; }
            private bool _litNow;

            // each finger from the wrist to its tip, and the knuckles across the palm (joint ids: Enums.cs:51-201)
            private static readonly XRHandJointID[][] Chains =
            {
                new[] { XRHandJointID.Wrist, XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip },
                new[] { XRHandJointID.Wrist, XRHandJointID.IndexMetacarpal, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip },
                new[] { XRHandJointID.Wrist, XRHandJointID.MiddleMetacarpal, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip },
                new[] { XRHandJointID.Wrist, XRHandJointID.RingMetacarpal, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip },
                new[] { XRHandJointID.Wrist, XRHandJointID.LittleMetacarpal, XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, XRHandJointID.LittleTip },
                new[] { XRHandJointID.IndexProximal, XRHandJointID.MiddleProximal, XRHandJointID.RingProximal, XRHandJointID.LittleProximal },
            };

            public Visual(Transform rig, bool left)
            {
                var go = new GameObject(left ? "TrackedHandLeft" : "TrackedHandRight");
                Root = go.transform; Root.SetParent(rig, false);
                var col = left ? new Color(0.45f, 0.7f, 1f, 0.85f) : new Color(1f, 0.65f, 0.45f, 0.85f); // the controller cubes' colours
                var shader = Shader.Find("Sprites/Default");
                // (client wave 14, w14ui, point 1: drawn after the panels, as the controller cubes: VR/DioramaRig.cs MakeHand)
                _mat = new Material(shader) { color = col, renderQueue = DioramaRig.HandQueue };
                _lit = new Material(shader) { color = new Color(1f, 1f, 0.6f, 0.95f), renderQueue = DioramaRig.HandQueue };
                for (int i = 0; i < JointCount; i++)
                {
                    var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    s.name = XRHandJointIDUtility.FromIndex(i).ToString();
                    Object.Destroy(s.GetComponent<Collider>()); // never in the laser's way
                    s.transform.SetParent(Root, false);
                    _rends[i] = s.GetComponent<Renderer>(); _rends[i].sharedMaterial = _mat;
                    _rends[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; _rends[i].receiveShadows = false; _rends[i].sortingOrder = DioramaRig.LaserSortingOrder;
                    Spheres[i] = s.transform;
                }
                _lines = new LineRenderer[Chains.Length];
                for (int c = 0; c < Chains.Length; c++)
                {
                    var lg = new GameObject("bones" + c); lg.transform.SetParent(Root, false);
                    var lr = lg.AddComponent<LineRenderer>();
                    lr.useWorldSpace = false; lr.positionCount = Chains[c].Length; lr.sharedMaterial = _mat;
                    lr.startWidth = 0.010f; lr.endWidth = 0.007f; lr.numCapVertices = 2;
                    lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false; lr.sortingOrder = DioramaRig.LaserSortingOrder;
                    _lines[c] = lr;
                }
                go.SetActive(false);
            }

            public void Set(Side s, bool show, bool pinching)
            {
                if (show != Shown) { Shown = show; Root.gameObject.SetActive(show); }
                if (!show) { Drawn = 0; return; }
                int drawn = 0;
                for (int i = 0; i < JointCount; i++)
                {
                    bool v = s.Valid[i];
                    if (Spheres[i].gameObject.activeSelf != v) Spheres[i].gameObject.SetActive(v);
                    if (!v) continue;
                    drawn++;
                    var id = XRHandJointIDUtility.FromIndex(i);
                    float r = s.Radius[i] > 0f ? s.Radius[i] : DefaultRadius(id);
                    Spheres[i].SetLocalPositionAndRotation(s.Poses[i].position, s.Poses[i].rotation);
                    Spheres[i].localScale = Vector3.one * (2f * r);
                }
                Drawn = drawn;
                if (pinching != _litNow)
                {
                    _litNow = pinching;
                    _rends[XRHandJointID.IndexTip.ToIndex()].sharedMaterial = pinching ? _lit : _mat;
                    _rends[XRHandJointID.ThumbTip.ToIndex()].sharedMaterial = pinching ? _lit : _mat;
                }
                for (int c = 0; c < Chains.Length; c++)
                {
                    var chain = Chains[c]; bool ok = true;
                    for (int k = 0; k < chain.Length; k++) { int i = chain[k].ToIndex(); if (!s.Valid[i]) { ok = false; break; } _lines[c].SetPosition(k, s.Poses[i].position); }
                    if (_lines[c].enabled != ok) _lines[c].enabled = ok;
                }
            }

            /// <summary>When the runtime gives no radius: a finger joint about 8 mm, a tip 6 mm, the wrist and palm 1.2 cm (Loki's own
            /// sizes, for the look only).</summary>
            private static float DefaultRadius(XRHandJointID id)
            {
                if (id == XRHandJointID.Wrist || id == XRHandJointID.Palm) return 0.012f;
                return id.ToString().EndsWith("Tip") ? 0.006f : 0.008f;
            }
        }
    }
}
