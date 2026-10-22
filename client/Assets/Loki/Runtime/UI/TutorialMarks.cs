using System;
using System.Collections.Generic;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 25 (w25tut): the tutorial's GREEN MARKS, items 2, 4 and 5 of the design's next list (playtest notes).
    ///   Item 2   (design note: "on the tutorial add uh, floating green arrow pointing down at the pouring until the player first runs their laser
    ///            over the pouring ... also during any other moments where the player needs to interact with the pouring. The moment that
    ///            they do, the arrow needs to disappear") the ARROW: a green arrow pointing down, floating over the practice Poring and
    ///            bobbing gently (<see cref="Arrow"/>). LokiGame.W25Tut.cs says when and where.
    ///   Item 4   (design note: "whenever I am meant to click on a button on the menu above me during the tutorial, make the button glow with a
    ///            green outline"; "when require to drag the minimap also make it glow with a green outline"; "whenever I meant to press a
    ///            specific button on the tutorial ... that should be highlighted") the OUTLINE: a green line round a panel's rectangle (a
    ///            button, a panel, the minimap ...) with a soft green glow outside it and a thin dark rim inside it (it reads on the
    ///            cream panels and on a bright sky alike), pulsing slowly (<see cref="Outline(RectTransform)"/>).
    ///   Item 5   (design note: "when it's time to use the wand to cast, make the wand also have a glowing green outline too") the same outline
    ///            round any four corners (<see cref="Outline(Vector3, Vector3, Vector3, Vector3)"/>): LokiGame.W25Tut.cs gives the wand's,
    ///            a band along it that faces the eyes.
    ///   Drawn    over everything (Loki/UIOverlay: no depth test; the laser's queue, just under the laser: sorting order <see cref="Order"/>),
    ///            on the panels' layer (out of the desktop spectator's view and out of the tutorial clips' own camera: UI/TutorialClip.cs),
    ///            every size an ANGLE from the eyes (the same thickness on a button 0.6 m away and on a Poring 1.6 m away). The owner fills
    ///            the marks once a frame in <see cref="Fill"/>, called here in LateUpdate (after the panels were placed for the frame).
    /// </summary>
    public sealed class TutorialMarks : MonoBehaviour
    {
        /// <summary>The green and the dark rim (a dark green). FLAGGED: Loki's colours.</summary>
        public static readonly Color Green = new Color(0.30f, 0.96f, 0.40f, 1f), Rim = new Color(0.02f, 0.22f, 0.05f, 1f);
        /// <summary>The outline, degrees from the eyes: its gap outside the thing, the dark rim's width (inside the gap's end), the green
        /// line's width, the glow's width; its slow pulse (Hz) and the lowest share of its opacity. FLAGGED: Loki's numbers.</summary>
        public const float GapDeg = 0.30f, RimDeg = 0.10f, LineDeg = 0.32f, GlowDeg = 0.80f, PulseHz = 1.0f, PulseLow = 0.6f;
        /// <summary>The arrow, degrees from the eyes: its height, the gap under its tip (over the Poring's top), its bob (up and down) and
        /// the bob's rate (Hz). FLAGGED: Loki's numbers.</summary>
        public const float ArrowDeg = 4.5f, ArrowGapDeg = 1.0f, ArrowBobDeg = 0.7f, ArrowBobHz = 1.2f, ArrowEdgeDeg = 0.18f;
        /// <summary>Under the laser (VR/DioramaRig.Ui.cs LaserSortingOrder 30) and the Poring's filling ring (UI/LookFillRing.cs 29).</summary>
        public const int Order = 28;

        /// <summary>Called once a frame (LateUpdate) to say what is marked now: <see cref="Outline(RectTransform)"/>, <see cref="Arrow"/>.</summary>
        public Action<TutorialMarks> Fill;
        /// <summary>Outlines drawn at the last frame, the arrow drawn, its tip (smoke, log).</summary>
        public int Outlines { get; private set; }
        public bool ArrowShown { get; private set; }
        public Vector3 ArrowTip { get; private set; }
        /// <summary>What the last frame outlined (the marked things' names; smoke, log).</summary>
        public readonly List<string> Marked = new List<string>();

        private Func<Camera> _head; private Mesh _mesh; private MeshRenderer _mr;
        private readonly List<Vector3> _v = new List<Vector3>(); private readonly List<Color> _c = new List<Color>(); private readonly List<int> _t = new List<int>();
        private readonly Vector3[] _corners = new Vector3[4];
        private Vector3? _arrowAt; private float _alpha = 1f;
        private static Material _mat;

        /// <summary>Made at the scene's root (its vertices are in the world: its own transform stays the identity; the scene's reload
        /// takes it with it).</summary>
        public static TutorialMarks Create(Func<Camera> head)
        {
            var go = new GameObject("TutorialMarks"); go.layer = UiKit.PanelLayer;
            var m = go.AddComponent<TutorialMarks>(); m._head = head;
            m._mesh = new Mesh { name = "TutorialMarks" }; m._mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = m._mesh;
            m._mr = go.AddComponent<MeshRenderer>();
            if (_mat == null)
            {
                var sh = Shader.Find("Loki/UIOverlay");
                _mat = new Material(sh != null ? sh : Shader.Find("Sprites/Default")) { name = "TutorialMarks", renderQueue = VR.DioramaRig.LaserQueue };
            }
            m._mr.sharedMaterial = _mat; m._mr.sortingOrder = Order;
            m._mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; m._mr.receiveShadows = false;
            return m;
        }

        /// <summary>A green outline round a panel's rectangle (a button, a panel, the minimap ...).</summary>
        public void Outline(RectTransform rt)
        {
            if (rt == null) return;
            rt.GetWorldCorners(_corners); // (bottom left, top left, top right, bottom right)
            Outline(_corners[0], _corners[1], _corners[2], _corners[3]);
            Marked.Add(rt.name);
        }

        /// <summary>A green outline round four corners (bottom left, top left, top right, bottom right) of a rectangle.</summary>
        public void Outline(Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br)
        {
            var cam = _head != null ? _head() : null; if (cam == null) return;
            var c = (bl + tl + tr + br) * 0.25f; var right = br - bl; var up = tl - bl;
            float hw = right.magnitude * 0.5f, hh = up.magnitude * 0.5f; if (hw < 1e-5f || hh < 1e-5f) return;
            right /= hw * 2f; up /= hh * 2f;
            float m = Vector3.Distance(cam.transform.position, c) * Mathf.Deg2Rad; // (metres a degree, near enough at these small angles)
            float gap = GapDeg * m, rim = RimDeg * m, line = LineDeg * m, glow = GlowDeg * m;
            Color g = Green; g.a = _alpha; Color g0 = Green; g0.a = 0f; Color gh = Green; gh.a = _alpha * 0.45f; Color r = Rim; r.a = _alpha * 0.85f;
            Ring(c, right, up, hw, hh, gap - rim, gap, r, r);
            Ring(c, right, up, hw, hh, gap, gap + line, g, g);
            Ring(c, right, up, hw, hh, gap + line, gap + line + glow, gh, g0);
            Outlines++;
        }

        /// <summary>The arrow over <paramref name="over"/> (the point its tip stands above: the Poring's top).</summary>
        public void Arrow(Vector3 over) { _arrowAt = over; }

        /// <summary>A frame between the rectangle grown by <paramref name="e1"/> and by <paramref name="e2"/> (metres), coloured from
        /// <paramref name="inner"/> to <paramref name="outer"/>.</summary>
        private void Ring(Vector3 c, Vector3 right, Vector3 up, float hw, float hh, float e1, float e2, Color inner, Color outer)
        {
            int b = _v.Count;
            for (int k = 0; k < 2; k++)
            {
                float e = k == 0 ? e1 : e2; var col = k == 0 ? inner : outer; float w = hw + e, h = hh + e;
                _v.Add(c - right * w - up * h); _v.Add(c - right * w + up * h); _v.Add(c + right * w + up * h); _v.Add(c + right * w - up * h);
                for (int i = 0; i < 4; i++) _c.Add(col);
            }
            for (int i = 0; i < 4; i++)
            {
                int a = b + i, a2 = b + (i + 1) % 4, o = b + 4 + i, o2 = b + 4 + (i + 1) % 4;
                _t.Add(a); _t.Add(o); _t.Add(o2); _t.Add(a); _t.Add(o2); _t.Add(a2);
            }
        }

        /// <summary>The arrow: a dark edge, then the green arrow over it (one draw: the later triangles are blended over the earlier).
        /// Upright, turned to the eyes about the vertical, its tip <see cref="ArrowGapDeg"/> over <paramref name="over"/>, bobbing.</summary>
        private void BuildArrow(Vector3 over, Camera cam)
        {
            var eye = cam.transform.position; var to = over - eye; float d = to.magnitude; if (d < 0.05f) return;
            var flat = new Vector3(to.x, 0f, to.z); if (flat.sqrMagnitude < 1e-6f) flat = cam.transform.forward; flat.y = 0f; flat.Normalize();
            var right = Vector3.Cross(Vector3.up, flat); var up = Vector3.up;
            float m = d * Mathf.Deg2Rad, h = ArrowDeg * m, edge = ArrowEdgeDeg * m;
            float bob = (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI * ArrowBobHz)) * ArrowBobDeg * m;
            var tip = over + up * (ArrowGapDeg * m + bob);
            ArrowTip = tip; ArrowShown = true;
            // the shape in its own plane: the head (a triangle, 0.62 h wide, 0.48 h tall) under the shaft (0.24 h wide)
            float headW = 0.31f * h, headH = 0.48f * h, shaftW = 0.12f * h;
            Color dark = Rim; dark.a = 0.9f; Color g = Green; g.a = 0.95f + 0.05f * _alpha;
            for (int pass = 0; pass < 2; pass++)
            {
                float e = pass == 0 ? edge : 0f; var col = pass == 0 ? dark : g; int b = _v.Count;
                Vector3 P(float x, float y) => tip + right * x + up * y;
                // the head: the tip (pushed down by the edge's width), its two corners
                _v.Add(P(0f, -e * 1.6f)); _v.Add(P(-headW - e * 1.4f, headH + e)); _v.Add(P(headW + e * 1.4f, headH + e));
                // the shaft: a rectangle from inside the head up to the top
                _v.Add(P(-shaftW - e, headH - e)); _v.Add(P(-shaftW - e, h + e)); _v.Add(P(shaftW + e, h + e)); _v.Add(P(shaftW + e, headH - e));
                for (int i = 0; i < 7; i++) _c.Add(col);
                _t.Add(b); _t.Add(b + 1); _t.Add(b + 2);
                _t.Add(b + 3); _t.Add(b + 4); _t.Add(b + 5); _t.Add(b + 3); _t.Add(b + 5); _t.Add(b + 6);
            }
        }

        private void LateUpdate()
        {
            _v.Clear(); _c.Clear(); _t.Clear(); Outlines = 0; ArrowShown = false; _arrowAt = null; Marked.Clear();
            _alpha = Mathf.Lerp(PulseLow, 1f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI * PulseHz));
            try { Fill?.Invoke(this); } catch (Exception e) { Debug.LogException(e); }
            var cam = _head != null ? _head() : null;
            if (_arrowAt.HasValue && cam != null) BuildArrow(_arrowAt.Value, cam);
            _mesh.Clear();
            if (_v.Count == 0) { if (_mr.enabled) _mr.enabled = false; return; }
            if (!_mr.enabled) _mr.enabled = true;
            _mesh.SetVertices(_v); _mesh.SetColors(_c); _mesh.SetTriangles(_t, 0, false);
            _mesh.bounds = new Bounds(cam != null ? cam.transform.position : Vector3.zero, Vector3.one * 2000f); // (always drawn: never culled away)
        }
    }
}
