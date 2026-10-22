using System.Collections.Generic;
using Loki.Game;
using Loki.VR;
using UnityEngine;
using Profile = Loki.VR.ControllerProfiles.Profile;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 22 (w22clips), point 59 of the design's notes after wave 17 (design note: "add a ghostly 3D projection of the player's current
    /// controller (Valve index, Quest, etc), and have it simulate what you're meant to do, as if it was those ghostly instructors that VR
    /// games usually use"): a see-through model of ONE controller whose parts act: the trigger pulled, the grip squeezed, the stick pushed
    /// or pressed, the face buttons and the menu button pressed, the index finger on or off the trigger; a part that acts is lit
    /// (<see cref="Lit"/>), the rest glows faintly (<see cref="Base"/>; Resources/Shaders/LokiGhost.shader).
    ///   Models   Loki has no controller models (the hands are drawn as small boxes, VR/DioramaRig.cs MakeHand; the vendored OpenXR plugin
    ///            1.12.1 has no render-model feature), and none may be downloaded: each is MADE IN CODE from cylinders, boxes, balls and
    ///            rings, one per FAMILY (<see cref="FamilyOf"/>): Valve Index (the long handle, the oval trackpad, the hand strap's arc),
    ///            Touch with its tracking ring (Oculus / Meta Touch), Touch without one (Touch Pro, Touch Plus, another controller), HP
    ///            Reverb G2 (a large ring), the Vive wand (the trackpad, the ring head, two side grips), Windows MR (trackpad + stick, a
    ///            large ring), the Khronos simple controller (a trigger and a menu button), and a HAND for the three hand-tracking
    ///            profiles (pinch = trigger, fist = grip, the pointing finger). Stand-ins, not the makers' shapes: a flag for review.
    ///   Frame    the device's grip pose as OpenXR gives it (the place of DioramaRig's hands): +z along the pointing direction, +y up,
    ///            +x right; metres. Built for the RIGHT hand; the left one is its mirror image (a scale of −1 across).
    ///   Which    a clip's inputs are the game's LOGICAL ones (VR/LokiInput.cs); <see cref="Apply"/> lights the part this profile uses
    ///            for each, by the rules of LokiInput.Hand.BeginFrame and ControllerProfiles.Buttons: the stick is the Vive's trackpad
    ///            edge (its middle for the click); the upper face button is the Vive's and the simple controller's menu button, WMR's
    ///            touchpad on the walking hand and its menu button on the pointing hand; the lower one is WMR's touchpad on the pointing
    ///            hand and its menu button on the walking hand; a hand pinches for the trigger and makes a fist for the grip.
    /// Used twice: beside each tutorial clip (on the guide, in step with the clip) and for the hands inside the clip's view.
    /// </summary>
    public sealed class ControllerGhost : MonoBehaviour
    {
        public enum Family { Index, TouchRing, Touch, Reverb, Vive, Wmr, Simple, Hand }
        public enum PartId { Body, Trigger, Grip, Stick, Pad, A, B, Menu, Finger }
        public static readonly Color Base = new Color(0.55f, 0.85f, 1f, 0.62f), Lit = new Color(1f, 0.82f, 0.38f, 1f);
        /// <summary>How far the parts travel: the trigger's turn and the stick's tilt (degrees), a button's and the grip's press (metres).</summary>
        public const float TriggerDeg = 24f, StickDeg = 26f, PressM = 0.0025f, GripM = 0.004f, FingerDeg = 52f, ActAt = 0.15f;

        public static Family FamilyOf(Profile p)
        {
            switch (p)
            {
                case Profile.Index: case Profile.None: return Family.Index; // (no controller known: the Index, as the Settings page and the tutorial's lines show)
                case Profile.Touch: return Family.TouchRing;
                case Profile.TouchPro: case Profile.TouchPlus: case Profile.Other: return Family.Touch;
                case Profile.ReverbG2: return Family.Reverb;
                case Profile.Vive: return Family.Vive;
                case Profile.Wmr: return Family.Wmr;
                case Profile.KhrSimple: return Family.Simple;
                default: return Family.Hand;
            }
        }

        private sealed class Part
        {
            public PartId Id; public Transform T; public MeshRenderer R; public Vector3 Pos; public Quaternion Rot; public Vector3 Press; public bool Lit; public float Travel;
        }
        private readonly Dictionary<PartId, Part> _parts = new Dictionary<PartId, Part>();
        private readonly List<Transform> _fingers = new List<Transform>(); private Transform _thumb, _padDot;
        private MaterialPropertyBlock _mpb; private float _padRadius;
        public Profile Profile { get; private set; }
        public Family Model { get; private set; }
        public bool Left { get; private set; }
        /// <summary>The model's size (its own space, metres) and its middle: the guide fits it into its place.</summary>
        public Bounds LocalBounds { get; private set; }
        /// <summary>The names of the parts lit now, in <see cref="PartId"/>'s order ("Trigger,Grip"; "": none); how far a part has moved
        /// from rest (0-1); the model has that part.</summary>
        public string LitNames { get { var sb = new System.Text.StringBuilder(); foreach (PartId id in System.Enum.GetValues(typeof(PartId))) if (_parts.TryGetValue(id, out var p) && p.Lit) { if (sb.Length > 0) sb.Append(','); sb.Append(id); } return sb.ToString(); } }
        public float TravelOf(PartId id) => _parts.TryGetValue(id, out var p) ? p.Travel : 0f;
        public bool Has(PartId id) => _parts.ContainsKey(id);
        public bool IsLit(PartId id) => _parts.TryGetValue(id, out var p) && p.Lit;

        private static readonly Material[] Mats = new Material[2];
        private static Material Mat(bool depthTested)
        {
            int i = depthTested ? 1 : 0;
            if (Mats[i] == null)
            {
                var sh = Shader.Find("Loki/Ghost");
                if (sh == null) { Debug.LogWarning("[Clips] Loki/Ghost shader missing: the ghost is drawn flat"); sh = Shader.Find("Sprites/Default"); }
                var m = new Material(sh) { name = depthTested ? "LokiGhostDepth" : "LokiGhost", enableInstancing = true };
                if (m.HasProperty("_ZTest")) m.SetFloat("_ZTest", depthTested ? (float)UnityEngine.Rendering.CompareFunction.LessEqual : (float)UnityEngine.Rendering.CompareFunction.Always);
                Mats[i] = m;
            }
            return Mats[i];
        }

        /// <param name="depthTested">Inside the clip's view (its parts hide behind the world); false: on a panel, drawn over everything.</param>
        public static ControllerGhost Create(Transform parent, Profile profile, bool left, int layer, int sortingOrder, bool depthTested)
        {
            var go = new GameObject("ControllerGhost" + (left ? "Left" : "Right")); go.layer = layer; go.transform.SetParent(parent, false);
            var g = go.AddComponent<ControllerGhost>();
            g.Profile = profile; g.Model = FamilyOf(profile); g.Left = left; g._mpb = new MaterialPropertyBlock();
            var root = new GameObject("model"); root.layer = layer; root.transform.SetParent(go.transform, false);
            if (left) root.transform.localScale = new Vector3(-1f, 1f, 1f); // the left controller: the right one's mirror image
            g.Build(root.transform, Mat(depthTested), layer, sortingOrder);
            var b = new Bounds(Vector3.zero, Vector3.zero); bool any = false;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                var mb = mf.sharedMesh.bounds; var m = go.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(c); if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            g.LocalBounds = b;
            g.Apply(default, !left);
            return g;
        }

        /// <summary>A ghost HEADSET (a visor and its head strap, the eyes looking along +z) for the steps done with the look alone.</summary>
        public static GameObject CreateHeadset(Transform parent, int layer, int order)
        {
            var go = new GameObject("HeadsetGhost"); go.layer = layer; go.transform.SetParent(parent, false);
            var mb = new MeshBuilder();
            mb.Box(new Vector3(0f, 0f, 0.075f), new Vector3(0.170f, 0.095f, 0.070f), Quaternion.identity);
            mb.Torus(new Vector3(0f, 0.005f, -0.020f), Quaternion.LookRotation(Vector3.up, Vector3.back), 0.092f, 0.011f, 0f, 360f); // the strap round the head
            mb.Torus(new Vector3(0f, 0.005f, -0.020f), Quaternion.LookRotation(Vector3.right, Vector3.up), 0.092f, 0.009f, 20f, 160f); // and over it
            go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("GhostHeadset");
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = Mat(false); r.sortingOrder = order;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            var mpb = new MaterialPropertyBlock(); mpb.SetColor("_Color", Base); r.SetPropertyBlock(mpb);
            return go;
        }

        // ------------------------------------------------------------------ the models
        private Part Add(Transform root, Material mat, int layer, int order, PartId id, MeshBuilder mb, Vector3 pivot, Quaternion rot, Vector3 press)
        {
            var go = new GameObject(id.ToString()); go.layer = layer; go.transform.SetParent(root, false);
            go.transform.localPosition = pivot; go.transform.localRotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("Ghost" + id);
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.sortingOrder = order;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            var p = new Part { Id = id, T = go.transform, R = r, Pos = pivot, Rot = rot, Press = press };
            _parts[id] = p; return p;
        }

        private void Build(Transform root, Material mat, int layer, int order)
        {
            if (Model == Family.Hand) { BuildHand(root, mat, layer, order); return; }
            // the thumb's face: where the stick and the buttons sit, tilted back towards the thumb
            var faceN = new Vector3(0f, 0.86f, -0.51f).normalized; var faceRot = Quaternion.LookRotation(Vector3.Cross(Vector3.right, faceN), faceN); // (y = the face's normal, z = up the face, x = right)
            Vector3 faceAt = new Vector3(0f, 0.030f, 0.004f);
            Vector3 OnFace(float x, float z) => faceAt + faceRot * new Vector3(x, 0f, z);
            var body = new MeshBuilder();
            float handle = Model == Family.Index ? 0.125f : Model == Family.Vive ? 0.150f : Model == Family.Simple ? 0.120f : 0.095f;
            var handleDir = (Model == Family.Vive || Model == Family.Simple ? new Vector3(0f, -0.25f, -0.97f) : new Vector3(0.03f, -0.82f, -0.57f)).normalized;
            Vector3 h0 = new Vector3(0f, 0.004f, -0.004f), h1 = h0 + handleDir * handle;
            body.Capsule(h0, h1, 0.0175f);
            switch (Model)
            {
                case Family.Index:
                    body.Cylinder(faceAt - faceN * 0.016f, faceAt, 0.024f, new Vector3(1f, 1f, 1.35f), faceRot); // the oval head
                    body.Torus(h0 + handleDir * (handle * 0.45f), Quaternion.LookRotation(Vector3.Cross(-handleDir, Vector3.right), Vector3.right), handle * 0.5f, 0.006f, 8f, 172f); // the strap round the back of the hand
                    break;
                case Family.Vive:
                    body.Torus(new Vector3(0f, 0.030f, 0.070f), Quaternion.LookRotation(new Vector3(0f, 0.75f, 0.66f), Vector3.right), 0.042f, 0.010f, 0f, 360f); // the ring head
                    body.Cylinder(new Vector3(0f, 0.004f, 0.020f), new Vector3(0f, 0.012f, 0.050f), 0.016f, Vector3.one, Quaternion.identity);
                    faceN = new Vector3(0f, 0.97f, 0.24f).normalized; faceRot = Quaternion.LookRotation(Vector3.Cross(Vector3.right, faceN), faceN); faceAt = new Vector3(0f, 0.0215f, -0.012f);
                    break;
                case Family.Simple:
                    body.Ball(new Vector3(0f, 0.008f, 0.012f), 0.022f);
                    faceN = Vector3.up; faceRot = Quaternion.identity; faceAt = new Vector3(0f, 0.024f, 0.004f);
                    break;
                default:
                    body.Cylinder(faceAt - faceN * 0.020f, faceAt, 0.027f, Vector3.one, faceRot); // the round head
                    if (Model == Family.TouchRing) body.Torus(new Vector3(0f, 0.034f, 0.030f), Quaternion.LookRotation(new Vector3(0f, 0.42f, 0.91f), Vector3.up), 0.046f, 0.0055f, 0f, 360f);
                    else if (Model == Family.Reverb || Model == Family.Wmr) body.Torus(new Vector3(0f, 0.040f, 0.040f), Quaternion.LookRotation(new Vector3(0f, 0.30f, 0.95f), Vector3.up), 0.058f, 0.006f, 0f, 360f);
                    break;
            }
            Add(root, mat, layer, order, PartId.Body, body, Vector3.zero, Quaternion.identity, Vector3.zero);

            // the trigger: under the head, in front; it turns about its hinge towards the handle
            var trig = new MeshBuilder(); trig.Box(new Vector3(0f, -0.013f, 0.004f), new Vector3(0.015f, 0.026f, 0.011f), Quaternion.Euler(-18f, 0f, 0f));
            Add(root, mat, layer, order, PartId.Trigger, trig, Model == Family.Vive || Model == Family.Simple ? new Vector3(0f, -0.006f, 0.022f) : new Vector3(0f, 0.006f, 0.034f), Quaternion.identity, Vector3.zero);
            // the index finger: straight ahead (pointing) or bent onto the trigger
            var fin = new MeshBuilder(); fin.Capsule(Vector3.zero, new Vector3(0f, 0f, 0.058f), 0.0065f);
            Add(root, mat, layer, order, PartId.Finger, fin, (Model == Family.Vive || Model == Family.Simple ? new Vector3(0.020f, 0.004f, 0.004f) : new Vector3(0.022f, 0.016f, 0.010f)), Quaternion.identity, Vector3.zero);
            if (Model != Family.Simple)
            {
                // the grip: on the handle, under the middle finger; it presses into the handle
                var grip = new MeshBuilder(); var gAt = h0 + handleDir * (handle * (Model == Family.Vive ? 0.55f : 0.45f));
                if (Model == Family.Vive) { grip.Box(new Vector3(0.0165f, 0f, 0f), new Vector3(0.006f, 0.012f, 0.030f), Quaternion.identity); grip.Box(new Vector3(-0.0165f, 0f, 0f), new Vector3(0.006f, 0.012f, 0.030f), Quaternion.identity); }
                else grip.Box(new Vector3(-0.0135f, 0f, 0.006f), new Vector3(0.010f, 0.034f, 0.016f), Quaternion.identity);
                Add(root, mat, layer, order, PartId.Grip, grip, gAt, Quaternion.LookRotation(Vector3.Cross(Vector3.right, -handleDir), -handleDir), Model == Family.Vive ? Vector3.zero : new Vector3(1f, 0f, 0f));
            }
            bool stick = Model != Family.Vive && Model != Family.Simple;
            if (stick)
            {
                var st = new MeshBuilder(); st.Cylinder(Vector3.zero, new Vector3(0f, 0.011f, 0f), 0.0045f, Vector3.one, Quaternion.identity); st.Cylinder(new Vector3(0f, 0.011f, 0f), new Vector3(0f, 0.015f, 0f), 0.0095f, Vector3.one, Quaternion.identity);
                Vector3 at = Model == Family.Index ? OnFace(-0.010f, -0.012f) : Model == Family.Wmr ? OnFace(-0.010f, 0.008f) : OnFace(-0.008f, 0.004f);
                Add(root, mat, layer, order, PartId.Stick, st, at, faceRot, new Vector3(0f, -1f, 0f));
            }
            if (Model == Family.Index || Model == Family.Vive || Model == Family.Wmr)
            {
                // the trackpad (the Vive's stick; WMR's touchpad; the Index's, which Loki does not use) and the thumb's dot on it
                var pad = new MeshBuilder(); _padRadius = Model == Family.Vive ? 0.020f : 0.011f;
                pad.Cylinder(new Vector3(0f, -0.002f, 0f), new Vector3(0f, 0.0015f, 0f), _padRadius, Model == Family.Index ? new Vector3(0.85f, 1f, 1.5f) : Vector3.one, Quaternion.identity);
                Vector3 at = Model == Family.Index ? OnFace(0.004f, 0.004f) : Model == Family.Vive ? faceAt : OnFace(0.008f, -0.010f);
                var pp = Add(root, mat, layer, order, PartId.Pad, pad, at, faceRot, new Vector3(0f, -1f, 0f));
                var dot = new MeshBuilder(); dot.Ball(Vector3.zero, 0.0045f);
                var dgo = new GameObject("thumb"); dgo.layer = layer; dgo.transform.SetParent(pp.T, false); dgo.transform.localPosition = new Vector3(0f, 0.004f, 0f);
                dgo.AddComponent<MeshFilter>().sharedMesh = dot.ToMesh("GhostThumb"); var dr = dgo.AddComponent<MeshRenderer>(); dr.sharedMaterial = mat; dr.sortingOrder = order;
                dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; dr.receiveShadows = false;
                _mpb.SetColor("_Color", Lit); dr.SetPropertyBlock(_mpb); dgo.SetActive(false); _padDot = dgo.transform;
            }
            if (Model == Family.Index || Model == Family.TouchRing || Model == Family.Touch || Model == Family.Reverb)
            {
                // the two face buttons: the lower (A; X on a left Touch) and the upper (B; Y)
                var a = new MeshBuilder(); a.Cylinder(new Vector3(0f, -0.001f, 0f), new Vector3(0f, 0.003f, 0f), 0.0058f, Vector3.one, Quaternion.identity);
                var b = new MeshBuilder(); b.Cylinder(new Vector3(0f, -0.001f, 0f), new Vector3(0f, 0.003f, 0f), 0.0058f, Vector3.one, Quaternion.identity);
                Vector3 aAt = Model == Family.Index ? OnFace(0.012f, -0.022f) : OnFace(0.010f, -0.012f), bAt = Model == Family.Index ? OnFace(0.016f, -0.006f) : OnFace(0.015f, 0.004f);
                Add(root, mat, layer, order, PartId.A, a, aAt, faceRot, new Vector3(0f, -1f, 0f));
                Add(root, mat, layer, order, PartId.B, b, bAt, faceRot, new Vector3(0f, -1f, 0f));
            }
            if (Model != Family.Index)
            {
                // the menu button (Touch: the left controller's; the right one's is the system's own: drawn on both, lit only where Loki reads it)
                var m = new MeshBuilder(); m.Cylinder(new Vector3(0f, -0.001f, 0f), new Vector3(0f, 0.002f, 0f), 0.0036f, Vector3.one, Quaternion.identity);
                Vector3 at = Model == Family.Vive ? faceAt + faceRot * new Vector3(0f, 0f, 0.030f) : Model == Family.Simple ? faceAt : Model == Family.Wmr ? OnFace(0.004f, -0.024f) : OnFace(-0.004f, -0.018f);
                Add(root, mat, layer, order, PartId.Menu, m, at, faceRot, new Vector3(0f, -1f, 0f));
            }
        }

        private void BuildHand(Transform root, Material mat, int layer, int order)
        {
            // a right hand, palm down, the fingers along +z: a palm, four fingers (two bones each) and a thumb
            var palm = new MeshBuilder(); palm.Box(new Vector3(0f, 0f, -0.040f), new Vector3(0.078f, 0.022f, 0.085f), Quaternion.identity);
            Add(root, mat, layer, order, PartId.Body, palm, Vector3.zero, Quaternion.identity, Vector3.zero);
            float[] xs = { -0.028f, -0.009f, 0.010f, 0.029f }, len = { 0.038f, 0.044f, 0.041f, 0.032f }; // index … little (the right hand's index is on its left: x −)
            for (int i = 0; i < 4; i++)
            {
                var k = new GameObject("finger" + i); k.layer = layer; k.transform.SetParent(root, false); k.transform.localPosition = new Vector3(xs[i], 0f, 0.004f);
                var b1 = new MeshBuilder(); b1.Capsule(Vector3.zero, new Vector3(0f, 0f, len[i]), 0.0085f);
                var g1 = MakeBone(k.transform, mat, layer, order, b1, Vector3.zero);
                var b2 = new MeshBuilder(); b2.Capsule(Vector3.zero, new Vector3(0f, 0f, len[i] * 0.9f), 0.0078f);
                MakeBone(g1, mat, layer, order, b2, new Vector3(0f, 0f, len[i]));
                _fingers.Add(k.transform);
            }
            var th = new GameObject("thumb"); th.layer = layer; th.transform.SetParent(root, false); th.transform.localPosition = new Vector3(-0.036f, -0.004f, -0.050f);
            th.transform.localRotation = Quaternion.Euler(0f, -38f, 0f);
            var t1 = new MeshBuilder(); t1.Capsule(Vector3.zero, new Vector3(0f, 0f, 0.040f), 0.0095f);
            var tg = MakeBone(th.transform, mat, layer, order, t1, Vector3.zero);
            var t2 = new MeshBuilder(); t2.Capsule(Vector3.zero, new Vector3(0f, 0f, 0.030f), 0.0085f);
            MakeBone(tg, mat, layer, order, t2, new Vector3(0f, 0f, 0.040f));
            _thumb = th.transform;
        }
        private Transform MakeBone(Transform parent, Material mat, int layer, int order, MeshBuilder mb, Vector3 at)
        {
            var go = new GameObject("bone"); go.layer = layer; go.transform.SetParent(parent, false); go.transform.localPosition = at;
            go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("GhostBone"); var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.sortingOrder = order;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go.transform;
        }

        // ------------------------------------------------------------------ acting
        /// <summary>The parts as the inputs say. <paramref name="mainHand"/>: this controller is in the pointing hand (Windows MR maps its
        /// two buttons by the hand's role: VR/LokiInput.cs BeginFrame).</summary>
        public void Apply(ClipTimeline.Input inp, bool mainHand)
        {
            bool click = inp.Has(ClipTimeline.StickClick), lower = inp.Has(ClipTimeline.Primary), upper = inp.Has(ClipTimeline.Secondary), menu = inp.Has(ClipTimeline.Menu);
            bool onTrigger = inp.Has(ClipTimeline.OnTrigger) || inp.Trigger > 0.05f;
            float push = Mathf.Clamp01(inp.Stick.magnitude);
            if (Model == Family.Hand) { ApplyHand(inp, onTrigger); return; }
            // which part each logical input is on this controller
            bool padPress = false, menuPress = false, aPress = false, bPress = false, stickPress = false; Vector2 padAt = Vector2.zero; bool padThumb = false;
            switch (Model)
            {
                case Family.Vive: // the trackpad is the stick (its edge pressed) and the stick's click (its middle); the menu button is the upper face button
                    padThumb = push > 0.2f || click; padPress = padThumb; padAt = click ? Vector2.zero : inp.Stick.normalized * Mathf.Max(0.75f, push); menuPress = upper; break;
                case Family.Wmr:
                    stickPress = click; if (mainHand) { padPress = lower; menuPress = upper; } else { padPress = upper; menuPress = lower; }
                    padThumb = padPress; break;
                case Family.Simple: menuPress = upper; break;
                case Family.Index: stickPress = click; aPress = lower; bPress = upper; break; // (its system button is SteamVR's own: no Settings button)
                default: stickPress = click; aPress = lower; bPress = upper; menuPress = menu && (Model == Family.Reverb || Left); break; // (Touch: the left controller's menu button)
            }
            Set(PartId.Body, false, 0f, null, null);
            if (_parts.TryGetValue(PartId.Trigger, out var tr)) Set(PartId.Trigger, inp.Trigger > ActAt, inp.Trigger, null, tr.Rot * Quaternion.Euler(inp.Trigger * TriggerDeg, 0f, 0f));
            if (_parts.TryGetValue(PartId.Grip, out var gr)) Set(PartId.Grip, inp.Grip > ActAt, inp.Grip, gr.Pos + (gr.Rot * gr.Press) * (inp.Grip * GripM), null);
            if (_parts.TryGetValue(PartId.Stick, out var st))
                Set(PartId.Stick, push > 0.2f || stickPress, Mathf.Max(push, stickPress ? 1f : 0f), st.Pos + (st.Rot * st.Press) * (stickPress ? PressM : 0f), st.Rot * Quaternion.Euler(inp.Stick.y * StickDeg, 0f, -inp.Stick.x * StickDeg));
            if (_parts.TryGetValue(PartId.Pad, out var pd))
            {
                Set(PartId.Pad, padPress, padPress ? 1f : 0f, pd.Pos + (pd.Rot * pd.Press) * (padPress ? PressM * 0.6f : 0f), null);
                if (_padDot != null) { if (_padDot.gameObject.activeSelf != padThumb) _padDot.gameObject.SetActive(padThumb); _padDot.localPosition = new Vector3(padAt.x * _padRadius * 0.8f, 0.004f, padAt.y * _padRadius * 0.8f); }
            }
            Button(PartId.A, aPress); Button(PartId.B, bPress); Button(PartId.Menu, menuPress);
            if (_parts.TryGetValue(PartId.Finger, out var fi)) Set(PartId.Finger, !onTrigger && inp.Grip > ActAt, onTrigger ? 1f : 0f, null, fi.Rot * Quaternion.Euler(onTrigger ? FingerDeg : 0f, 0f, 0f));
        }
        private void Button(PartId id, bool pressed) { if (_parts.TryGetValue(id, out var p)) Set(id, pressed, pressed ? 1f : 0f, p.Pos + (p.Rot * p.Press) * (pressed ? PressM : 0f), null); }
        private void Set(PartId id, bool lit, float travel, Vector3? pos, Quaternion? rot)
        {
            if (!_parts.TryGetValue(id, out var p)) return;
            if (pos.HasValue) p.T.localPosition = pos.Value; if (rot.HasValue) p.T.localRotation = rot.Value;
            p.Travel = travel;
            if (p.Lit != lit || !_tinted) { p.Lit = lit; _mpb.SetColor("_Color", lit ? Lit : Base); p.R.SetPropertyBlock(_mpb); }
        }
        private bool _tinted;
        private void LateUpdate() { _tinted = true; }

        /// <summary>A tracked hand: the pinch is its trigger (the index finger and the thumb meet), the fist its grip; with the grip held and the
        /// index finger off the "trigger" it points (the casting gesture's pose).</summary>
        private void ApplyHand(ClipTimeline.Input inp, bool onTrigger)
        {
            float fist = Mathf.Clamp01(inp.Grip), pinch = Mathf.Clamp01(inp.Trigger);
            bool pointing = fist > ActAt && !onTrigger;
            for (int i = 0; i < _fingers.Count; i++)
            {
                float curl = i == 0 ? (pointing ? 0f : Mathf.Max(fist, pinch * 0.55f)) : fist;
                _fingers[i].localRotation = Quaternion.Euler(curl * 80f, 0f, 0f);
                if (_fingers[i].childCount > 0 && _fingers[i].GetChild(0).childCount > 0) _fingers[i].GetChild(0).GetChild(0).localRotation = Quaternion.Euler(curl * 85f, 0f, 0f);
            }
            if (_thumb != null) _thumb.localRotation = Quaternion.Euler(Mathf.Max(pinch, fist) * 28f, -38f + Mathf.Max(pinch, fist) * 30f, 0f);
            bool lit = pinch > ActAt || fist > ActAt;
            if (_parts.TryGetValue(PartId.Body, out var b))
            {
                b.Travel = Mathf.Max(pinch, fist);
                if (b.Lit != lit || !_tinted)
                {
                    b.Lit = lit; _mpb.SetColor("_Color", lit ? Lit : Base);
                    foreach (var r in GetComponentsInChildren<MeshRenderer>()) r.SetPropertyBlock(_mpb);
                }
            }
        }

        // ------------------------------------------------------------------ meshes made in code
        private sealed class MeshBuilder
        {
            private readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>(); private readonly List<int> _t = new List<int>();
            private const int Seg = 14;
            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name }; m.SetVertices(_v); m.SetNormals(_n); m.SetTriangles(_t, 0); m.RecalculateBounds(); return m;
            }
            private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
            {
                int i = _v.Count; _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d); for (int k = 0; k < 4; k++) _n.Add(n);
                _t.Add(i); _t.Add(i + 1); _t.Add(i + 2); _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
            }
            public void Box(Vector3 centre, Vector3 size, Quaternion rot)
            {
                var h = size * 0.5f;
                Vector3 P(float x, float y, float z) => centre + rot * new Vector3(x * h.x, y * h.y, z * h.z);
                Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), rot * Vector3.forward); Quad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), rot * Vector3.back);
                Quad(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), rot * Vector3.right); Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), rot * Vector3.left);
                Quad(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1), rot * Vector3.up); Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), rot * Vector3.down);
                // (the faces above wind towards the inside for a left-handed look; flip them all at once)
                for (int i = _t.Count - 36; i < _t.Count; i += 3) { int s = _t[i + 1]; _t[i + 1] = _t[i + 2]; _t[i + 2] = s; }
            }
            /// <summary>A capped cylinder from a to b; <paramref name="squash"/> stretches its round section in <paramref name="frame"/>'s x and z (an oval).</summary>
            public void Cylinder(Vector3 a, Vector3 b, float radius, Vector3 squash, Quaternion frame)
            {
                var axis = (b - a).normalized; if (axis.sqrMagnitude < 1e-8f) return;
                var fx = frame * Vector3.right; var fz = frame * Vector3.forward;
                // the section's two directions: the frame's x and z, made square to the axis
                var u = Vector3.ProjectOnPlane(fx, axis); if (u.sqrMagnitude < 1e-6f) u = Vector3.ProjectOnPlane(Vector3.up, axis); u.Normalize();
                var w = Vector3.Cross(axis, u);
                float su = Mathf.Abs(Vector3.Dot(u, fx)) * squash.x + Mathf.Abs(Vector3.Dot(u, fz)) * squash.z, sw = Mathf.Abs(Vector3.Dot(w, fx)) * squash.x + Mathf.Abs(Vector3.Dot(w, fz)) * squash.z;
                if (squash == Vector3.one) { su = 1f; sw = 1f; } // (round: whatever the axis)
                if (su < 0.05f) su = 1f; if (sw < 0.05f) sw = 1f;
                int i0 = _v.Count;
                for (int i = 0; i <= Seg; i++)
                {
                    float ang = i * 2f * Mathf.PI / Seg; var dir = u * (Mathf.Cos(ang) * su) + w * (Mathf.Sin(ang) * sw); var nrm = (u * (Mathf.Cos(ang) / su) + w * (Mathf.Sin(ang) / sw)).normalized;
                    _v.Add(a + dir * radius); _n.Add(nrm); _v.Add(b + dir * radius); _n.Add(nrm);
                }
                for (int i = 0; i < Seg; i++) { int k = i0 + 2 * i; _t.Add(k); _t.Add(k + 1); _t.Add(k + 3); _t.Add(k); _t.Add(k + 3); _t.Add(k + 2); }
                Cap(a, -axis, u * su, w * sw, radius, true); Cap(b, axis, u * su, w * sw, radius, false);
            }
            private void Cap(Vector3 c, Vector3 n, Vector3 u, Vector3 w, float radius, bool flip)
            {
                int i0 = _v.Count; _v.Add(c); _n.Add(n);
                for (int i = 0; i <= Seg; i++) { float ang = i * 2f * Mathf.PI / Seg; _v.Add(c + (u * Mathf.Cos(ang) + w * Mathf.Sin(ang)) * radius); _n.Add(n); }
                for (int i = 0; i < Seg; i++) { if (flip) { _t.Add(i0); _t.Add(i0 + 1 + i); _t.Add(i0 + 2 + i); } else { _t.Add(i0); _t.Add(i0 + 2 + i); _t.Add(i0 + 1 + i); } }
            }
            public void Ball(Vector3 c, float r)
            {
                const int lat = 8; int i0 = _v.Count;
                for (int y = 0; y <= lat; y++)
                    for (int x = 0; x <= Seg; x++)
                    {
                        float a = y * Mathf.PI / lat, b = x * 2f * Mathf.PI / Seg; var n = new Vector3(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Cos(a), Mathf.Sin(a) * Mathf.Sin(b));
                        _v.Add(c + n * r); _n.Add(n);
                    }
                for (int y = 0; y < lat; y++)
                    for (int x = 0; x < Seg; x++)
                    {
                        int k = i0 + y * (Seg + 1) + x, d = k + Seg + 1;
                        _t.Add(k); _t.Add(k + 1); _t.Add(d + 1); _t.Add(k); _t.Add(d + 1); _t.Add(d);
                    }
            }
            public void Capsule(Vector3 a, Vector3 b, float r) { Cylinder(a, b, r, Vector3.one, Quaternion.identity); Ball(a, r); Ball(b, r); }
            /// <summary>A ring (or an arc of it, degrees) of radius <paramref name="big"/> in <paramref name="rot"/>'s x-y plane, its tube <paramref name="small"/> thick.</summary>
            public void Torus(Vector3 c, Quaternion rot, float big, float small, float fromDeg, float toDeg)
            {
                const int tube = 8; int rings = Mathf.Max(6, Mathf.RoundToInt(Mathf.Abs(toDeg - fromDeg) / 15f)); int i0 = _v.Count;
                for (int i = 0; i <= rings; i++)
                {
                    float a = Mathf.Lerp(fromDeg, toDeg, i / (float)rings) * Mathf.Deg2Rad; var radial = rot * new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f); var up = rot * Vector3.forward;
                    for (int j = 0; j <= tube; j++)
                    {
                        float b = j * 2f * Mathf.PI / tube; var n = radial * Mathf.Cos(b) + up * Mathf.Sin(b);
                        _v.Add(c + radial * big + n * small); _n.Add(n);
                    }
                }
                for (int i = 0; i < rings; i++)
                    for (int j = 0; j < tube; j++)
                    {
                        int k = i0 + i * (tube + 1) + j, d = k + tube + 1;
                        _t.Add(k); _t.Add(d); _t.Add(d + 1); _t.Add(k); _t.Add(d + 1); _t.Add(k + 1);
                    }
            }
        }
    }
}
