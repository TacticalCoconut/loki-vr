using System;
using System.Collections.Generic;
using UnityEngine;

namespace Loki.VR
{
    /// <summary>
    /// Client wave 20 (w20pen), the design's point 57 (the AIR PEN: "On the social menu, allow the player to spawn a simple pen object that can be used
    /// to draw on the air"; "You should also be able to pull this pen out from above you. When you look at the menu, it will show up and you can
    /// pull it out of the air"): the pen as an object, and where it waits. Who holds it and what it draws is LokiGame.W20Pen.cs; the drawings
    /// are UI/PenDrawings.cs.
    ///   The pen   a slim dark body with a band and an ink tip in the player's own colour (VR/HeadsetAvatars.cs TintFor: the colour its
    ///             strokes have), <see cref="Length"/> long, its tip <see cref="Tip"/> ahead of the hand along the hand's pointing direction
    ///             (as the wand lies in the hand). Made of Unity's primitives, no collider (never in the laser's way).
    ///   Eraser    while the pen is an eraser a see-through pink ball of <see cref="LokiGame.PenEraser"/> stands on its tip: what it touches goes.
    ///   In the air  put away, it floats beside the overhead menu (UI/OverheadMenu.cs: <see cref="SpotGap"/> beyond its right edge, level
    ///             with its middle), upright with its tip down, drawn as much as the menu is (it fades in and out with the menu, as the look
    ///             comes to it). While a hand holds the pen, the same place shows a faint ring (its slot: the pen goes back there). A ring of
    ///             light round it (the wand's cue: VR/WandHolster.cs SetCue) says a grip now takes the pen, or puts it back.
    /// </summary>
    public sealed class AirPen : MonoBehaviour
    {
        public const float Length = 0.13f, Tip = 0.12f, Body = 0.013f, SpotGap = 0.08f, SlotRadius = 0.035f, CueRadius = 0.05f, CueWidth = 0.005f;
        public static readonly Color BodyColour = new Color(0.16f, 0.17f, 0.22f, 1f), SlotColour = new Color(0.85f, 0.9f, 1f, 0.35f), CueColour = new Color(1f, 0.85f, 0.35f, 0.95f),
            EraserColour = new Color(1f, 0.55f, 0.72f, 0.35f);
        public const int RingSegments = 36;

        public Transform Pen { get; private set; }
        public Transform TipPoint { get; private set; }
        /// <summary>The pen is drawn now; it is in a hand; it floats beside the overhead menu; how much of it is drawn there (0-1).</summary>
        public bool Shown => Pen != null && Pen.gameObject.activeSelf;
        public bool InHand { get; private set; }
        public bool Floating { get; private set; }
        public float FloatAlpha { get; private set; }
        public bool EraserShown => _eraser != null && _eraser.activeSelf;
        public bool CueShown => _cue != null && _cue.enabled;
        public bool SlotShown => _slot != null && _slot.enabled;
        /// <summary>Where it floats (or its slot is) now, in the world; the slot / the floating pen are shown (the overhead menu is drawn).</summary>
        public Vector3 Spot { get; private set; }
        public bool SpotShown { get; private set; }

        private DioramaRig _rig; private readonly List<Material> _mats = new List<Material>(); private readonly List<Color> _base = new List<Color>();
        private Material _ink, _band; private GameObject _eraser; private LineRenderer _cue, _slot;

        public void Init(DioramaRig rig, Color ink)
        {
            _rig = rig;
            Pen = new GameObject("AirPen").transform; Pen.SetParent(rig.transform, false);
            var sh = Shader.Find("Sprites/Default");
            Material Mat(string name, Color c) { var m = new Material(sh) { name = name, color = c }; _mats.Add(m); _base.Add(c); return m; }
            var body = Mat("PenBody", BodyColour); _band = Mat("PenBand", ink); _ink = Mat("PenInk", ink);
            Part(PrimitiveType.Cylinder, "body", new Vector3(0, 0, 0.045f), new Vector3(Body, 0.055f, Body), body, Quaternion.Euler(90, 0, 0));
            Part(PrimitiveType.Cylinder, "band", new Vector3(0, 0, -0.004f), new Vector3(Body * 1.06f, 0.008f, Body * 1.06f), _band, Quaternion.Euler(90, 0, 0));
            Part(PrimitiveType.Cylinder, "nib", new Vector3(0, 0, 0.108f), new Vector3(Body * 0.55f, 0.008f, Body * 0.55f), body, Quaternion.Euler(90, 0, 0));
            TipPoint = Part(PrimitiveType.Sphere, "tip", new Vector3(0, 0, Tip), Vector3.one * 0.0065f, _ink, Quaternion.identity).transform;
            var er = Mat("PenEraser", EraserColour);
            _eraser = Part(PrimitiveType.Sphere, "eraser", new Vector3(0, 0, Tip), Vector3.one * (2f * LokiGame.PenEraser), er, Quaternion.identity);
            _eraser.SetActive(false);
            _cue = Ring("PenCue", CueColour); _slot = Ring("PenSlot", SlotColour);
            Pen.gameObject.SetActive(false);
        }

        private GameObject Part(PrimitiveType type, string name, Vector3 at, Vector3 scale, Material mat, Quaternion rot)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; Destroy(go.GetComponent<Collider>()); // never in the laser's way
            go.transform.SetParent(Pen, false); go.transform.localPosition = at; go.transform.localRotation = rot; go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.sortingOrder = WandHolster.SolidOrder; // (as the wand: after the panels)
            return go;
        }

        private LineRenderer Ring(string name, Color c)
        {
            var go = new GameObject(name); go.transform.SetParent(_rig.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.positionCount = RingSegments; lr.loop = true; lr.startWidth = lr.endWidth = CueWidth; lr.numCapVertices = 2;
            var sh = Shader.Find("Loki/UIOverlay");
            lr.material = new Material(sh != null ? sh : Shader.Find("Sprites/Default")) { name = name, renderQueue = DioramaRig.LaserQueue };
            lr.sortingOrder = DioramaRig.LaserSortingOrder; lr.startColor = lr.endColor = c;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false; lr.enabled = false;
            return lr;
        }

        /// <summary>The ink's colour (the player's tint: set when the account is known).</summary>
        public void SetInk(Color ink)
        {
            for (int i = 0; i < _mats.Count; i++) if (_mats[i] == _ink || _mats[i] == _band) _base[i] = ink;
            Apply(1f);
        }

        private float _alpha = -1f;
        private void Apply(float alpha)
        {
            _alpha = alpha;
            for (int i = 0; i < _mats.Count; i++) { var c = _base[i]; c.a *= alpha; _mats[i].color = c; }
        }

        /// <summary>In <paramref name="hand"/>: along the hand's pointing direction; the eraser's ball on the tip when <paramref name="eraser"/>.</summary>
        public void ShowInHand(Transform hand, bool eraser)
        {
            InHand = true; Floating = false; FloatAlpha = 0f;
            if (!Pen.gameObject.activeSelf) Pen.gameObject.SetActive(true);
            Pen.SetPositionAndRotation(hand.position, hand.rotation);
            if (_alpha != 1f) Apply(1f);
            if (_eraser.activeSelf != eraser) _eraser.SetActive(eraser);
        }

        /// <summary>Floating at <paramref name="spot"/> (beside the overhead menu), upright, tip down, leaning a little toward the eyes, drawn at
        /// <paramref name="alpha"/>.</summary>
        public void ShowFloating(Vector3 spot, float alpha)
        {
            InHand = false; Floating = alpha > 0.002f; FloatAlpha = alpha;
            if (Pen.gameObject.activeSelf != Floating) Pen.gameObject.SetActive(Floating);
            if (!Floating) return;
            var eye = _rig.Head.transform.position; var toEye = eye - spot; toEye.y = 0f; if (toEye.sqrMagnitude < 1e-6f) toEye = Vector3.back;
            var down = Quaternion.AngleAxis(-15f, Vector3.Cross(Vector3.up, toEye.normalized)) * Vector3.down; // (the tip leans 15° toward the player)
            // the pen's middle on the spot (the hand that takes it closes round it there)
            var rot = Quaternion.LookRotation(down, toEye.normalized);
            Pen.SetPositionAndRotation(spot - down * (Tip * 0.5f), rot);
            if (Mathf.Abs(_alpha - alpha) > 1e-3f) Apply(alpha);
            if (_eraser.activeSelf) _eraser.SetActive(false);
        }

        public void Hide() { InHand = false; Floating = false; FloatAlpha = 0f; if (Pen.gameObject.activeSelf) Pen.gameObject.SetActive(false); }

        /// <summary>Where the pen waits: <see cref="SpotGap"/> beyond the overhead menu's right edge, level with its middle; shown while the menu is
        /// drawn at all (alpha: how much).</summary>
        public void SetSpot(Vector3 spot, bool shown) { Spot = spot; SpotShown = shown; }

        /// <summary>The slot's faint ring at the spot (the pen is in a hand and the menu is drawn), facing the eyes, at <paramref name="alpha"/>.</summary>
        public void SetSlot(bool on, float alpha) => SetRing(_slot, on, SlotRadius, new Color(SlotColour.r, SlotColour.g, SlotColour.b, SlotColour.a * alpha));

        /// <summary>The cue: a pulsing ring of light round the spot: a grip now takes the pen (or puts it back).</summary>
        public void SetCue(bool on)
        {
            var c = CueColour; c.a *= 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.5f * 2f * Mathf.PI));
            SetRing(_cue, on, CueRadius, c);
        }

        private void SetRing(LineRenderer lr, bool on, float radius, Color c)
        {
            if (lr == null) return;
            if (lr.enabled != on) lr.enabled = on;
            if (!on) return;
            lr.startColor = lr.endColor = c;
            var eye = _rig.Head.transform.position; var n = Spot - eye; if (n.sqrMagnitude < 1e-6f) n = Vector3.forward; n.Normalize();
            var a = Vector3.Cross(n, Vector3.up); if (a.sqrMagnitude < 1e-4f) a = Vector3.right; a.Normalize(); var b = Vector3.Cross(a, n);
            for (int i = 0; i < RingSegments; i++) { float t = i * 2f * Mathf.PI / RingSegments; lr.SetPosition(i, Spot + (a * Mathf.Cos(t) + b * Mathf.Sin(t)) * radius); }
        }

        private void OnDestroy() { foreach (var m in _mats) if (m != null) Destroy(m); }

        // ------------------------------------------------------------------------------------------------ the Settings rows

        /// <summary>
        /// Client wave 20 (w20pen), the design's answers of 2026-10-10: Settings → Headsets, after the casting menus' rows: "Drawings seen by" (who sees
        /// MY drawings: Everyone, or Only these and any of the three switches; the default Only these with friends) and "Drawings I see" (whose
        /// drawings I see: the same; the default friends only). The switches say whom they are about in their names, so each reads alone on
        /// the window's second page. <paramref name="set"/> saves and refreshes (ComfortWindow.Set).
        /// </summary>
        public static void ComfortRows(List<UI.ListWindow.Row> rows, Action<Action> set)
        {
            rows.Add(UI.SettingRow.Choice(Lang.T("Drawings seen by"), null, new[] { Lang.T("~seen~Everyone"), Lang.T("~seen~Only these") }, () => Comfort.DrawSeenEveryone ? 0 : 1, i => set(() => Comfort.DrawSeenEveryone = i == 0)));
            if (!Comfort.DrawSeenEveryone)
            {
                rows.Add(UI.SettingRow.Toggle(Lang.T("Friends see mine"), null, () => Comfort.DrawSeenFriends, v => set(() => Comfort.DrawSeenFriends = v)));
                rows.Add(UI.SettingRow.Toggle(Lang.T("Family sees mine"), Lang.T("Spouse, parents, child"), () => Comfort.DrawSeenFamily, v => set(() => Comfort.DrawSeenFamily = v)));
                rows.Add(UI.SettingRow.Toggle(Lang.T("Guild sees mine"), null, () => Comfort.DrawSeenGuild, v => set(() => Comfort.DrawSeenGuild = v)));
            }
            rows.Add(UI.SettingRow.Choice(Lang.T("Drawings I see"), null, new[] { Lang.T("~seen~Everyone"), Lang.T("~seen~Only these") }, () => Comfort.DrawSeeEveryone ? 0 : 1, i => set(() => Comfort.DrawSeeEveryone = i == 0)));
            if (!Comfort.DrawSeeEveryone)
            {
                rows.Add(UI.SettingRow.Toggle(Lang.T("Friends' drawings"), null, () => Comfort.DrawSeeFriends, v => set(() => Comfort.DrawSeeFriends = v)));
                rows.Add(UI.SettingRow.Toggle(Lang.T("Family's drawings"), Lang.T("Spouse, parents, child"), () => Comfort.DrawSeeFamily, v => set(() => Comfort.DrawSeeFamily = v)));
                rows.Add(UI.SettingRow.Toggle(Lang.T("Guild's drawings"), null, () => Comfort.DrawSeeGuild, v => set(() => Comfort.DrawSeeGuild = v)));
            }
        }
    }
}
