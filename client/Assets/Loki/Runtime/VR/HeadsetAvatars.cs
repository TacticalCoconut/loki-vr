using System;
using System.Collections.Generic;
using Loki.Net;
using Loki.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Loki.VR
{
    /// <summary>The two presence choices, kept with the other settings (client wave 4: Comfort.ShowHeadsets / ShareHeadset in the
    /// per-user loki-user.json, the Settings window's "Headsets" page; before wave 4 PlayerPrefs "loki.presence.*", imported once).</summary>
    public static class PresenceSettings
    {
        /// <summary>Draw the other players' floating headsets and hands.</summary>
        public static bool ShowOthers { get => Comfort.ShowHeadsets; set => Comfort.ShowHeadsets = value; }
        /// <summary>Send my own head and hands (off: the others stop seeing my headset at once).</summary>
        public static bool ShareMine { get => Comfort.ShareHeadset; set => Comfort.ShareHeadset = value; }

        public static void Load() => Comfort.Load();

        /// <summary>Comfort.Save (the Settings window's Set) writes them.</summary>
        public static void SetShowOthers(bool v) { ShowOthers = v; }
        public static void SetShareMine(bool v) { ShareMine = v; }
    }

    /// <summary>
    /// Floating headsets (client wave 3, w3presence): friends in VR see each other both as their RO characters on the diorama and as
    /// a stylised headset with two controllers above it, where the other player's head and hands are over THEIR table, mapped into
    /// this table through the map's own frame (<see cref="PresencePose"/>): each player's table scale, pan, turn and view mode
    /// (Table, Follow, First person) cancel out. Each headset is tinted per player, carries the character's name, and a faint beam
    /// joins it to that character (paired by account id: an RO player's unit id is its AID, map/pc.cpp:1444). A headset hides when
    /// its player is on another map, left, or has sent nothing for <see cref="SilentHideSeconds"/>; the Comfort option hides them all.
    /// A desktop client (no headset) sends nothing. Poses go out <see cref="SendHz"/> times a second through the
    /// <see cref="IPresenceChannel"/>.
    /// </summary>
    [DefaultExecutionOrder(600)] // after ViewController (500): the diorama is where this frame draws it (Follow / First person move it in LateUpdate)
    public sealed class HeadsetAvatars : MonoBehaviour
    {
        public const float SilentHideSeconds = 2f;
        /// <summary>A headset is drawn at its real size times (my metres per cell / the sender's): the size it would have on my table.
        /// Clamped so a zoomed-in friend's headset stays visible and a first-person friend's doesn't fill the room.</summary>
        public const float MinSize = 0.2f, MaxSize = 2.5f;
        public float SendHz = 15f;
        /// <summary>Smokes: send although no headset is active (desktop windows).</summary>
        public bool ForceSend;
        /// <summary>Smokes: stop sending without saying LEAVE (the "silent for 2 s" path).</summary>
        public bool PauseSending;
        /// <summary>The character is in the world (not disconnected, not leaving): only then does my headset go out.</summary>
        public Func<bool> Playing;

        public IPresenceChannel Channel { get; private set; }
        public string CurrentMap { get; private set; }
        public int Sent { get; private set; }
        public bool Sharing { get; private set; }

        private DioramaRig _rig; private MapLoader _map; private Func<uint, Sprites.Unit> _unit; private uint _self;
        private readonly Dictionary<uint, Avatar> _avatars = new Dictionary<uint, Avatar>();
        private readonly PresencePose _mine = new PresencePose();
        private float _nextSend;
        private InputAction _leftTracked, _rightTracked;
        private static Material _dark;

        public sealed class Avatar
        {
            public uint Aid; public string Name = ""; public Color Tint;
            public readonly PresencePose Target = new PresencePose();
            /// <summary>Smoothed, in the map's frame.</summary>
            public Vector3 HeadPos, LeftPos, RightPos; public Quaternion HeadRot, LeftRot, RightRot;
            public bool HasPose; public float LastPoseTime; public int PosesSeen;
            public string Hidden = "no pose yet"; public float HiddenAt;
            public Transform Head, Left, Right, LabelRoot; public Text Label; public LineRenderer Beam;
            public float Size = 1f;
            public bool Visible => Head != null && Head.gameObject.activeSelf;
            public bool BeamShown => Beam != null && Beam.enabled;
            internal Material Body, Trim, Glow;
            /// <summary>Client wave 6 (w6voice): its voice is being heard (the lights and the name are bright).</summary>
            public bool Talking { get; internal set; }
        }

        public static HeadsetAvatars Create(IPresenceChannel channel, DioramaRig rig, MapLoader map, Func<uint, Sprites.Unit> unitByAid, uint selfAid)
        {
            // a scene root of its own: under the diorama pivot the headsets would inherit its scale
            var h = new GameObject("HeadsetAvatars").AddComponent<HeadsetAvatars>();
            h.Channel = channel; h._rig = rig; h._map = map; h._unit = unitByAid; h._self = selfAid;
            PresenceSettings.Load();
            channel.PoseReceived += h.OnPose;
            channel.Gone += aid => { if (h._avatars.TryGetValue(aid, out var a)) h.Drop(a, "gone (the relay: left this map or the game)"); };
            channel.Lost += () => { foreach (var a in h._avatars.Values) h.Drop(a, "my link to the relay dropped"); };
            h._leftTracked = new InputAction("presenceLeftTracked", InputActionType.Value, "<XRController>{LeftHand}/isTracked");
            h._rightTracked = new InputAction("presenceRightTracked", InputActionType.Value, "<XRController>{RightHand}/isTracked");
            h._leftTracked.Enable(); h._rightTracked.Enable();
            return h;
        }

        public bool TryGet(uint aid, out Avatar a) => _avatars.TryGetValue(aid, out a);

        /// <summary>Client wave 6 (w6voice): that player's headset glows while its voice is heard — the two lights on the visor, the
        /// controllers' rings and the name turn bright (Voice/VoiceChat.cs calls this every frame while it talks, once when it stops).</summary>
        public void SetTalking(uint aid, bool talking)
        {
            if (!_avatars.TryGetValue(aid, out var a) || a.Talking == talking) return;
            a.Talking = talking;
            if (a.Glow != null) a.Glow.color = talking ? new Color(1f, 1f, 0.75f) : Color.Lerp(a.Tint, Color.white, 0.6f);
            if (a.Label != null) a.Label.color = talking ? new Color(1f, 0.95f, 0.5f) : Color.Lerp(a.Tint, Color.white, 0.55f);
        }
        public IEnumerable<Avatar> All => _avatars.Values;

        /// <summary>We entered a map (first login, a warp, a map-server change): every headset seen so far belonged to the old map.</summary>
        public void OnMapChanged(string map)
        {
            CurrentMap = map;
            foreach (var a in _avatars.Values) Drop(a, "I changed map");
            _nextSend = 0f; // tell the others at once
        }

        /// <summary>My pose in the map's frame, from the rig's head and hands.</summary>
        public PresencePose MyPose()
        {
            var m = _map.transform;
            var inv = Quaternion.Inverse(m.rotation);
            _mine.Map = CurrentMap; _mine.MetresPerCell = m.lossyScale.x;
            _mine.HeadPos = m.InverseTransformPoint(_rig.Head.transform.position); _mine.HeadRot = inv * _rig.Head.transform.rotation;
            _mine.LeftPos = m.InverseTransformPoint(_rig.LeftHand.position); _mine.LeftRot = inv * _rig.LeftHand.rotation;
            _mine.RightPos = m.InverseTransformPoint(_rig.RightHand.position); _mine.RightRot = inv * _rig.RightHand.rotation;
            _mine.LeftTracked = ForceSend || _leftTracked.ReadValue<float>() > 0.5f;
            _mine.RightTracked = ForceSend || _rightTracked.ReadValue<float>() > 0.5f;
            return _mine;
        }

        /// <summary>This client draws the map it was sent to (a map missing from the game data keeps the old scene: no poses then).</summary>
        private bool OnMap => CurrentMap != null && _map != null && _map.Current != null && string.Equals(_map.Current.Name, CurrentMap, StringComparison.OrdinalIgnoreCase);

        private void LateUpdate()
        {
            if (Channel == null || _rig == null || _map == null) return;
            Channel.Poll();
            float now = Time.unscaledTime;

            // ---- send
            bool share = PresenceSettings.ShareMine && OnMap && (Playing == null || Playing()) && (ForceSend || UnityEngine.XR.XRSettings.isDeviceActive);
            if (share && !PauseSending)
            {
                if (now >= _nextSend) { _nextSend = now + 1f / Mathf.Max(1f, SendHz); Channel.Send(MyPose()); Sent++; }
                Sharing = true;
            }
            else if (Sharing && !PauseSending) { Sharing = false; Channel.Leave(); Debug.Log("[Presence] not sharing my headset any more (" + (OnMap ? "switched off" : "off the map") + ")"); }

            // ---- draw
            var m = _map.transform;
            float mine = m.lossyScale.x;
            var cam = _rig.Head.transform;
            foreach (var a in _avatars.Values)
            {
                string why = !a.HasPose ? a.Hidden
                    : !PresenceSettings.ShowOthers ? "headsets switched off (Comfort)"
                    : !OnMap || !string.Equals(a.Target.Map, CurrentMap, StringComparison.OrdinalIgnoreCase) ? "on another map"
                    : now - a.LastPoseTime > SilentHideSeconds ? "silent"
                    : null;
                SetShown(a, why == null, why, now);
                if (why != null) continue;
                float t = 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.06f); // ~60 ms smoothing between poses (15 per second)
                var g = a.Target;
                a.HeadPos = Vector3.Lerp(a.HeadPos, g.HeadPos, t); a.HeadRot = Quaternion.Slerp(a.HeadRot, g.HeadRot, t);
                a.LeftPos = Vector3.Lerp(a.LeftPos, g.LeftPos, t); a.LeftRot = Quaternion.Slerp(a.LeftRot, g.LeftRot, t);
                a.RightPos = Vector3.Lerp(a.RightPos, g.RightPos, t); a.RightRot = Quaternion.Slerp(a.RightRot, g.RightRot, t);
                a.Size = Mathf.Clamp(mine / Mathf.Max(g.MetresPerCell, PresenceWire.MinMetresPerCell), MinSize, MaxSize);
                Place(a.Head, m, a.HeadPos, a.HeadRot, a.Size);
                a.Left.gameObject.SetActive(g.LeftTracked); if (g.LeftTracked) Place(a.Left, m, a.LeftPos, a.LeftRot, a.Size);
                a.Right.gameObject.SetActive(g.RightTracked); if (g.RightTracked) Place(a.Right, m, a.RightPos, a.RightRot, a.Size);
                // the name above the headset, facing me, readable at any distance (as Overhead does: grows past 1.2 m)
                var top = a.Head.position + Vector3.up * (0.16f * a.Size); // clear of the strap when the head looks down
                float dist = Vector3.Distance(cam.position, top);
                a.LabelRoot.position = top;
                if (dist > 1e-3f) a.LabelRoot.rotation = Quaternion.LookRotation(top - cam.position, Vector3.up);
                a.LabelRoot.localScale = Vector3.one * (0.00045f * Comfort.NameScale * Mathf.Max(1f, dist / UI.Overhead.FarMetres)); // a bit larger than a unit's tag (Overhead: 0.00035)
                // the beam down to the character, when the RO server shows it here (in view range)
                var u = _unit?.Invoke(a.Aid);
                bool beam = u != null && u.isActiveAndEnabled;
                a.Beam.enabled = beam;
                if (beam)
                {
                    var charTop = u.transform.position + m.up * (2.3f * mine); // about the top of a standing character sprite (Overhead: 2.6 cells for the tag)
                    a.Beam.SetPosition(0, a.Head.position - a.Head.up * (0.06f * a.Size));
                    a.Beam.SetPosition(1, charTop);
                    a.Beam.widthMultiplier = 0.004f * Mathf.Max(a.Size, 0.5f);
                }
            }
        }

        private static void Place(Transform t, Transform map, Vector3 pos, Quaternion rot, float size)
        {
            t.position = map.TransformPoint(pos);
            t.rotation = map.rotation * rot;
            t.localScale = Vector3.one * size;
        }

        private void OnPose(uint aid, string name, PresencePose p)
        {
            if (aid == _self) return;
            if (!_avatars.TryGetValue(aid, out var a)) { a = Build(aid, name); _avatars[aid] = a; }
            if (a.Name != name) { a.Name = name ?? ""; a.Label.text = a.Name; }
            // (re)appearing, another map, or a jump no head makes in one pose (a warp on the same map: the relay sends no GONE):
            // start where it is, not gliding in from where it was
            bool snap = !a.HasPose || !string.Equals(a.Target.Map, p.Map, StringComparison.OrdinalIgnoreCase) || (p.HeadPos - a.Target.HeadPos).sqrMagnitude > 30f * 30f;
            a.Target.CopyFrom(p);
            if (snap)
            {
                a.HeadPos = p.HeadPos; a.HeadRot = p.HeadRot; a.LeftPos = p.LeftPos; a.LeftRot = p.LeftRot; a.RightPos = p.RightPos; a.RightRot = p.RightRot;
                a.HasPose = true;
            }
            a.LastPoseTime = Time.unscaledTime; a.PosesSeen++;
        }

        private void Drop(Avatar a, string why)
        {
            if (!a.HasPose) return;
            a.HasPose = false; a.Hidden = why;
            SetShown(a, false, why, Time.unscaledTime);
        }

        private void SetShown(Avatar a, bool show, string why, float now)
        {
            if (a.Head.gameObject.activeSelf == show) { if (!show) a.Hidden = why; return; }
            a.Head.gameObject.SetActive(show); a.LabelRoot.gameObject.SetActive(show); a.Beam.gameObject.SetActive(show);
            if (!show) { a.Left.gameObject.SetActive(false); a.Right.gameObject.SetActive(false); a.Hidden = why; a.HiddenAt = now; }
            Debug.Log(show ? $"[Presence] headset of '{a.Name}' (AID {a.Aid}) shown on {a.Target.Map}"
                           : $"[Presence] headset of '{a.Name}' (AID {a.Aid}) hidden: {why} (last pose {now - a.LastPoseTime:F2} s ago)");
        }

        /// <summary>A colour per player from the AID (golden-ratio hues: neighbours differ), the same on every client.</summary>
        public static Color TintFor(uint aid)
        {
            // in double: RO account ids start at 2,000,000, where a float keeps only 1/8 of a unit (8 hues in all)
            double h = aid * 0.6180339887498949 % 1.0;
            return Color.HSVToRGB((float)h, 0.6f, 0.95f);
        }

        // ---------------------------------------------------------------------------------------------- the models

        private Avatar Build(uint aid, string name)
        {
            var a = new Avatar { Aid = aid, Name = name ?? "", Tint = TintFor(aid) };
            var shader = Shader.Find("Sprites/Default");
            a.Body = new Material(shader) { color = a.Tint, name = "presence body" };
            a.Trim = new Material(shader) { color = Color.Lerp(a.Tint, Color.black, 0.45f), name = "presence trim" };
            if (_dark == null) _dark = new Material(shader) { color = new Color(0.04f, 0.045f, 0.07f), name = "presence visor" };
            var glow = a.Glow = new Material(shader) { color = Color.Lerp(a.Tint, Color.white, 0.6f), name = "presence glow" };

            // the headset: a rounded-looking shell around the eyes (the pose is the point between the eyes), a dark visor in front,
            // two lights on it that show where it looks, and a strap round the back of the head
            a.Head = new GameObject($"headset:{aid}").transform; a.Head.SetParent(transform, false);
            Part(a.Head, PrimitiveType.Cube, a.Body, new Vector3(0, 0.005f, 0.045f), new Vector3(0.19f, 0.105f, 0.095f));
            Part(a.Head, PrimitiveType.Cube, _dark, new Vector3(0, 0, 0.094f), new Vector3(0.172f, 0.072f, 0.012f));
            Part(a.Head, PrimitiveType.Cube, glow, new Vector3(-0.042f, 0.004f, 0.1015f), new Vector3(0.036f, 0.012f, 0.004f));
            Part(a.Head, PrimitiveType.Cube, glow, new Vector3(0.042f, 0.004f, 0.1015f), new Vector3(0.036f, 0.012f, 0.004f));
            Part(a.Head, PrimitiveType.Cube, a.Trim, new Vector3(-0.092f, 0.01f, -0.035f), new Vector3(0.012f, 0.03f, 0.13f));
            Part(a.Head, PrimitiveType.Cube, a.Trim, new Vector3(0.092f, 0.01f, -0.035f), new Vector3(0.012f, 0.03f, 0.13f));
            Part(a.Head, PrimitiveType.Cube, a.Trim, new Vector3(0, 0.01f, -0.098f), new Vector3(0.18f, 0.03f, 0.012f));
            Part(a.Head, PrimitiveType.Cube, a.Trim, new Vector3(0, 0.062f, -0.02f), new Vector3(0.03f, 0.012f, 0.14f)); // top strap
            a.Left = Controller(aid, "left", a.Body, glow);
            a.Right = Controller(aid, "right", a.Body, glow);

            var lr = new GameObject("label", typeof(RectTransform));
            lr.transform.SetParent(transform, false);
            var canvas = lr.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 5;
            a.LabelRoot = lr.transform; ((RectTransform)a.LabelRoot).sizeDelta = new Vector2(420, 60);
            a.Label = UI.UiKit.Label((RectTransform)a.LabelRoot, a.Name, Vector2.zero, new Vector2(420, 56), 30, TextAnchor.MiddleCenter, Color.Lerp(a.Tint, Color.white, 0.55f));
            var ol = a.Label.gameObject.AddComponent<Outline>(); ol.effectColor = new Color(0, 0, 0, 0.9f); ol.effectDistance = new Vector2(2, -2);

            var bg = new GameObject($"beam:{aid}"); bg.transform.SetParent(transform, false);
            a.Beam = bg.AddComponent<LineRenderer>();
            a.Beam.useWorldSpace = true; a.Beam.positionCount = 2; a.Beam.numCapVertices = 2;
            a.Beam.material = new Material(shader) { name = "presence beam" };
            a.Beam.startColor = new Color(a.Tint.r, a.Tint.g, a.Tint.b, 0.55f); a.Beam.endColor = new Color(a.Tint.r, a.Tint.g, a.Tint.b, 0.12f);
            a.Beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; a.Beam.receiveShadows = false;
            a.Beam.enabled = false;

            a.Head.gameObject.SetActive(false); a.Left.gameObject.SetActive(false); a.Right.gameObject.SetActive(false);
            a.LabelRoot.gameObject.SetActive(false); bg.SetActive(false);
            Debug.Log($"[Presence] first pose from '{a.Name}' (AID {aid})");
            return a;
        }

        private Transform Controller(uint aid, string side, Material body, Material glow)
        {
            var t = new GameObject($"controller:{aid}:{side}").transform; t.SetParent(transform, false);
            Part(t, PrimitiveType.Cube, body, new Vector3(0, -0.012f, -0.02f), new Vector3(0.034f, 0.036f, 0.11f)); // the grip
            Part(t, PrimitiveType.Cube, _dark, new Vector3(0, 0.008f, 0.025f), new Vector3(0.04f, 0.012f, 0.05f));    // the face with the buttons
            var ring = Part(t, PrimitiveType.Cylinder, glow, new Vector3(0, 0.025f, 0.055f), new Vector3(0.07f, 0.003f, 0.07f)); // the tracking ring
            ring.localRotation = Quaternion.Euler(-35f, 0, 0);
            return t;
        }

        private static Transform Part(Transform parent, PrimitiveType kind, Material mat, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(kind);
            Destroy(go.GetComponent<Collider>()); // never in the laser's way
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go.transform;
        }

        /// <summary>The Settings window's rows ("Headsets" page since client wave 4): <paramref name="set"/> saves and refreshes (ComfortWindow.Set).</summary>
        public static void ComfortRows(List<UI.ListWindow.Row> rows, Action<Action> set)
        {
            PresenceSettings.Load();
            // client wave 13, item 5 (w13ring): rows by UI/SettingRow.cs (the current state lit, both choices shown); item 17: short texts
            rows.Add(UI.SettingRow.Toggle(Lang.T("Friends' headsets"), Lang.T("Their heads and hands"), () => PresenceSettings.ShowOthers, v => set(() => PresenceSettings.SetShowOthers(v))));
            rows.Add(UI.SettingRow.Toggle(Lang.T("Share my headset"), Lang.T("Friends see your head and hands"), () => PresenceSettings.ShareMine, v => set(() => PresenceSettings.SetShareMine(v))));
            // client wave 18 (w18share), the design's point 33 and its addition ("can be toggled for privacy to only be visible to friends, family,
            // or clan members", "and/or"; "I meant my Guild"): my casting menus, and who sees them (the relay sends them to nobody else: UI/GestureShare.cs)
            rows.Add(UI.SettingRow.Toggle(Lang.T("Casting menus shown"), Lang.T("Friends see your casting menus"), () => Comfort.MenusShared, v => set(() => Comfort.MenusShared = v)));
            if (Comfort.MenusShared)
            {
                rows.Add(UI.SettingRow.Choice(Lang.T("Seen by"), null, new[] { Lang.T("~seen~Everyone"), Lang.T("~seen~Only these") }, () => Comfort.MenusEveryone ? 0 : 1, i => set(() => Comfort.MenusEveryone = i == 0)));
                if (!Comfort.MenusEveryone)
                {
                    rows.Add(UI.SettingRow.Toggle(Lang.T("Friends"), null, () => Comfort.MenusFriends, v => set(() => Comfort.MenusFriends = v)));
                    rows.Add(UI.SettingRow.Toggle(Lang.T("Family"), Lang.T("Spouse, parents, child"), () => Comfort.MenusFamily, v => set(() => Comfort.MenusFamily = v)));
                    rows.Add(UI.SettingRow.Toggle(Lang.T("Guild"), null, () => Comfort.MenusGuild, v => set(() => Comfort.MenusGuild = v)));
                }
            }
            AirPen.ComfortRows(rows, set); // client wave 20 (w20pen), point 57: "Drawings seen by", "Drawings I see" (VR/AirPen.cs)
        }

        private void OnDestroy()
        {
            try { Channel?.Dispose(); } catch (Exception e) { Debug.LogException(e); }
            _leftTracked?.Disable(); _leftTracked?.Dispose(); _rightTracked?.Disable(); _rightTracked?.Dispose();
            foreach (var a in _avatars.Values)
            {
                if (a.Body != null) Destroy(a.Body);
                if (a.Trim != null) Destroy(a.Trim);
                if (a.Glow != null) Destroy(a.Glow);
                if (a.Beam != null && a.Beam.material != null) Destroy(a.Beam.material);
            }
        }
    }
}
