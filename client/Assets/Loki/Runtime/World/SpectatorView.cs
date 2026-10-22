using System;
using UnityEngine;
using UnityEngine.XR;

namespace Loki.World
{
    /// <summary>
    /// Client wave 4 (w4world): what the desktop window shows while someone plays in the headset. "Eye view" is Unity's XR mirror of the
    /// headset (DioramaRig's note: the left eye), unchanged; "Overview" is RO's own kind of camera — high above and behind our character
    /// looking down at it (about 20 cells up and 18 back, south of it on the map, as the classic client's default camera looks north);
    /// "Third person" follows behind our character's back at 7 cells, 4 up. Both follow the character and the diorama: the offsets are
    /// cells times the map's current scale, turned with the map, so the picture frames the same area in the Table, Follow and First
    /// person views. While a spectator view is on, the XR mirror is switched off (XRSettings.gameViewRenderMode = None) so the desktop
    /// camera's picture is the window's; the headset itself is not touched. On a desktop run (no headset) the spectator camera simply
    /// draws over the head camera. Cycled by the wrist strip's "Desktop" button and by F9 on the desktop keyboard.
    /// Client wave 5 (w5weather): the spectator camera leaves out the player's own floating panels (w4world.md "Left": they live in the
    /// world and showed in its view). Every panel UI/UiKit.Panel makes is on Unity's built-in layer 5 "UI" (UiKit.PanelLayer; the layer
    /// every Unity project has, ProjectSettings/TagManager.asset of the Unity copy: "UI" at index 5; nothing else in Loki used it) and this
    /// camera's culling mask has every layer but that one (Camera.cullingMask: a camera draws only the layers in its mask). Before this
    /// camera culls (Camera.onPreCull), the panels' whole hierarchies are put on that layer, so a child made later by other code (a
    /// minimap mark, a list row) never shows for a frame; inactive branches are skipped (they draw nothing) and done when they appear.
    /// The comfort vignette, a quad 12 cm before the eyes (VR/ComfortVignette.cs), is the eyes' alone and goes with the panels, and so
    /// do our own hands' visuals (the controller cubes and tracked-hand joints float over the table as the panels do). The headset view,
    /// the screenshot camera (a copy of the head camera) and every other camera still draw everything. Kept in view on purpose: other
    /// players' headsets, name plates and damage numbers (they belong to the world), our laser and its cell cursor (where the player
    /// points is worth seeing).
    /// </summary>
    public sealed class SpectatorView : MonoBehaviour
    {
        public enum Mode { Eye, Overview, ThirdPerson }
        public Mode Current { get; private set; } = Mode.Eye;
        public Camera Camera => _cam;
        private Camera _cam; private Func<Transform> _self; private Func<Transform> _mapRoot; private Func<int> _selfDir; private Camera _head;
        private GameViewRenderMode _mirror = GameViewRenderMode.LeftEye; private bool _mirrorSaved;
        private Vector3 _pos; private Quaternion _rot; private bool _placed;
        public event Action<Mode> Changed;
        private readonly System.Collections.Generic.List<Transform> _hidden = new System.Collections.Generic.List<Transform>();
        private bool _vignetteLooked, _handsLooked;
        /// <summary>The other objects hidden from the spectator camera (the vignette, our hands' visuals), for the log and checks.</summary>
        public System.Collections.Generic.IReadOnlyList<Transform> HiddenExtra => _hidden;
        /// <summary>The last sweep before this camera culled: transforms walked (active ones) and the time it took (log, check).</summary>
        public int LastSweepCount { get; private set; }
        public double LastSweepMs { get; private set; }
        /// <summary>The culling mask of the spectator camera: every layer but the panels' (UI.UiKit.PanelLayer) and (client wave 22, w22clips)
        /// the tutorial clip's stage (UI.TutorialClip.StageLayer: the actors of the clip that plays on the tutorial's guide).</summary>
        public static int Mask => ~((1 << UI.UiKit.PanelLayer) | (1 << UI.TutorialClip.StageLayer));
        /// <summary>Another object only the player's own eyes should see (it and its children go on the panels' layer).</summary>
        public void Hide(GameObject go) { if (go != null && !_hidden.Contains(go.transform)) _hidden.Add(go.transform); }

        public static string Label(Mode m) => m == Mode.Eye ? Lang.T("Desktop: eye view") : m == Mode.Overview ? Lang.T("Desktop: overview") : Lang.T("Desktop: third person"); // w4lang: only shown (the wrist strip's button)

        public void Init(Camera head, Func<Transform> self, Func<int> selfDir, Func<Transform> mapRoot) { _head = head; _self = self; _selfDir = selfDir; _mapRoot = mapRoot; }

        public void Cycle() => Set((Mode)(((int)Current + 1) % 3));

        public void Set(Mode m)
        {
            Current = m;
            if (m == Mode.Eye)
            {
                if (_cam != null) _cam.enabled = false;
                if (_mirrorSaved && XRSettings.enabled) XRSettings.gameViewRenderMode = _mirror;
            }
            else
            {
                if (_cam == null) Make();
                if (XRSettings.enabled && XRSettings.isDeviceActive)
                {
                    if (!_mirrorSaved) { _mirror = XRSettings.gameViewRenderMode; _mirrorSaved = true; }
                    XRSettings.gameViewRenderMode = GameViewRenderMode.None;
                }
                _placed = false; _cam.enabled = true;
            }
            Debug.Log($"[Spectator] desktop view: {m}{(XRSettings.isDeviceActive ? $" (XR mirror {(m == Mode.Eye ? _mirror.ToString() : "off")})" : " (no headset: the camera draws over the head camera)")}");
            Changed?.Invoke(m);
        }

        private void Make()
        {
            var go = new GameObject("SpectatorCamera");
            go.transform.SetParent(transform, false);
            _cam = go.AddComponent<Camera>();
            if (_head != null) _cam.CopyFrom(_head);
            _cam.stereoTargetEye = StereoTargetEyeMask.None; _cam.targetTexture = null;
            _cam.depth = (_head != null ? _head.depth : 0f) + 10f;
            _cam.fieldOfView = 50f; _cam.nearClipPlane = 0.01f; _cam.farClipPlane = 2000f;
            _cam.cullingMask = Mask; // client wave 5 (w5weather): no floating panels
            _cam.enabled = false;
            Camera.onPreCull += BeforeCull;
        }

        private void OnDestroy() { Camera.onPreCull -= BeforeCull; }

        /// <summary>Puts every panel (and the other hidden objects) on the panels' layer right before the spectator camera culls.</summary>
        private void BeforeCull(Camera cam)
        {
            if (cam != _cam || _cam == null) return;
            if (!_vignetteLooked && _head != null) { var v = _head.GetComponentInChildren<VR.ComfortVignette>(true); if (v != null) { Hide(v.gameObject); _vignetteLooked = true; } }
            if (!_handsLooked && _head != null)
            {
                // our own hands: the controller cubes ("vis", VR/DioramaRig.cs MakeHand) and the tracked-hand joints ("TrackedHandLeft" /
                // "TrackedHandRight", VR/HandJoints.cs Visual) float over the table like the panels (a desktop run's overview showed the
                // left cube as a big block); the laser (a child of the right hand, not of its cube) and the cell cursor stay
                var rig = _head.GetComponentInParent<VR.DioramaRig>();
                if (rig != null && rig.LeftHand != null && rig.RightHand != null)
                {
                    _handsLooked = true;
                    foreach (var t in new[] { rig.LeftHand.Find("vis"), rig.RightHand.Find("vis"), rig.transform.Find("TrackedHandLeft"), rig.transform.Find("TrackedHandRight") })
                        if (t != null) Hide(t.gameObject);
                }
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int n = 0, layer = UI.UiKit.PanelLayer;
            var panels = UI.UiKit.Panels;
            for (int i = panels.Count - 1; i >= 0; i--) { var t = panels[i]; if (t == null) { panels.RemoveAt(i); continue; } if (t.gameObject.activeInHierarchy) n += Sweep(t, layer); }
            for (int i = _hidden.Count - 1; i >= 0; i--) { var t = _hidden[i]; if (t == null) { _hidden.RemoveAt(i); continue; } if (t.gameObject.activeInHierarchy) n += Sweep(t, layer); }
            LastSweepCount = n; LastSweepMs = sw.Elapsed.TotalMilliseconds;
        }

        private static int Sweep(Transform t, int layer)
        {
            if (t.gameObject.layer == UI.LookSelect.LaserOnlyLayer) return 0; // client wave 13 (w13look), item 22: colliders only the laser meets keep their layer (they draw nothing)
            int n = 1;
            if (t.gameObject.layer != layer) t.gameObject.layer = layer;
            for (int i = 0, c = t.childCount; i < c; i++) { var ch = t.GetChild(i); if (ch.gameObject.activeSelf) n += Sweep(ch, layer); }
            return n;
        }

        private void LateUpdate()
        {
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.f9Key.wasPressedThisFrame) Cycle();
            if (Current == Mode.Eye || _cam == null) return;
            var self = _self?.Invoke(); var map = _mapRoot?.Invoke();
            if (self == null || map == null) return;
            float s = Mathf.Abs(map.lossyScale.x);
            var target = self.position + map.up * (1.5f * s);
            Vector3 offset;
            if (Current == Mode.Overview) offset = map.TransformDirection(new Vector3(0f, 20f, -18f)) * s; // cells → world: turned with the map (TransformDirection) and scaled with it
            else
            {
                int d = _selfDir != null ? _selfDir() : 0;
                float th = d * 45f * Mathf.Deg2Rad;
                var fwd = new Vector3(-Mathf.Sin(th), 0f, -Mathf.Cos(th)); // ActorSprite direction 0 faces −y (south), 2 west, 4 north, 6 east (Unit.DirFromDelta)
                offset = map.TransformDirection(-fwd * 7f + Vector3.up * 4f) * s;
            }
            var want = target + offset;
            var look = Quaternion.LookRotation((target - want).normalized, map.up);
            if (!_placed) { _pos = want; _rot = look; _placed = true; }
            else { float k = 1f - Mathf.Exp(-4f * Time.deltaTime); _pos = Vector3.Lerp(_pos, want, k); _rot = Quaternion.Slerp(_rot, look, k); }
            _cam.transform.SetPositionAndRotation(_pos, _rot);
            _cam.nearClipPlane = Mathf.Max(0.005f, 0.05f * s); _cam.farClipPlane = Mathf.Max(50f, 600f * s);
        }

        private void OnDisable() { if (_mirrorSaved && XRSettings.enabled) XRSettings.gameViewRenderMode = _mirror; }
    }
}
