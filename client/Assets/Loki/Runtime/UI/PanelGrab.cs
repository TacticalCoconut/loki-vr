using System.Collections.Generic;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 13 (w13grab), item 19 (design note, 2026-10-07: "allow the player to move around regular HUD panels (like the settings panel,
    /// inventory, quests panel, etc) by grabbing the edges with their hand or the laser. Give it a visual indicator when it a grab will
    /// lead to it being dragged"): WHICH panels can be taken by their edge, WHERE their edge band is, and the INDICATOR. Who holds what
    /// and how it moves is LokiGame.W13Grab.cs; the grip's claim (no casting from it) is VR/LokiInput.cs ClaimForGrab.
    ///   Which   every floating window the player reads (the coordinator's reading, documented): every ListWindow (Settings, shops,
    ///           storage, the pickers, Commands, the tutorial ...), the new menu's board (bag, quests, skills, status ...), the NPC
    ///           dialog, the chat, the Guide, the World map, the notice and the death panel (<see cref="Named"/>). NOT what rides on the
    ///           body or the view and has its own placing (the menu ring, the classic wrist menu on the hand, the forearm bar, the buff
    ///           bar, the see-through minimap, the wand's wheel, the pad, the overhead menu; until client wave 24 the keyboard under
    ///           its text field too: it and the login panel are taken by their edge now, see <see cref="Named"/>): a panel added
    ///           later is left out until it is named here on purpose.
    ///   Band    a frame along the panel's outer rim, from <see cref="BandInside"/> m inside the rim to (waves 13 to 22) 4 cm
    ///           outside it; since client wave 23 see the end of this summary. Inside the rim it never reaches a button (the nearest, the board's
    ///           X, starts 0.3 cm under its top rim); a button under the laser is always the button (the back arrow outside the top
    ///           left corner among them), and a panel or button the laser meets first is never another panel's band.
    ///   Hand    the controller's grip point (a tracked hand: its palm) within <see cref="HandReach"/> m of the band (in front of or
    ///           behind the panel); the nearest panel wins.
    ///   Laser   the pointing hand's laser on the band: inside the rim on the panel's own background, outside it with nothing nearer
    ///           on the laser.
    ///   Light   the band's outline (a line ON the rim, <see cref="OutlineWidth"/> m wide: half inside, half outside, never over a
    ///           button) and a small grab mark at the nearest rim point (a hand) or the laser's point (the laser's tip): cyan
    ///           (<see cref="Ready"/>) while a grip now would drag it, orange (<see cref="Held"/>) while it is held.
    /// Client wave 23 (w23inv), design note (2026-10-11): "increase the size of the grabbable edge of panels". Since client
    /// wave 21 a window's wood frame stands up to 2.7 cm OUT of its rectangle (UI/UiKit.cs OutsetOf), so of the old band's 4 cm
    /// outside the rim only about 1.3 cm lay beyond what the player sees as the panel's edge. NOW:
    ///   Band    from <see cref="BandInside"/> m inside the rectangle's rim (unchanged: it never reaches a button) across THE WHOLE
    ///           VISIBLE FRAME (<see cref="FramePx"/>) to <see cref="BandOutside"/> m beyond the frame's OUTER edge (a panel without
    ///           a frame, a card or a see-through one: beyond its rectangle), square at the corners. On the Settings window: 0.5 cm
    ///           inside to 8.7 cm outside its rim. The bent inventory strip: the same along its curve (and along what of it is there).
    ///   Hand    the grip point within <see cref="HandReach"/> m of that band (raised with it: 4 cm to 6 cm).
    ///   Light   the outline on the rim as before, and a soft glow over the WHOLE band (<see cref="GlowAlpha"/>): where the glow
    ///           is, the laser takes the panel.
    /// </summary>
    public static class PanelGrab
    {
        /// <summary>Metres: the band's part inside the rectangle's rim; how far it reaches beyond the visible frame's outer edge
        /// (until client wave 23: 0.040 beyond the rectangle's rim); a hand's reach to the band (until client wave 23: 0.040).</summary>
        public const float BandInside = 0.005f, BandOutside = 0.060f, HandReach = 0.060f, OutlineWidth = 0.0045f, MarkSize = 0.026f;
        /// <summary>The glow over the band: this share of the outline's own strength.</summary>
        public const float GlowAlpha = 0.22f;
        public static readonly Color Ready = new Color(0.35f, 0.92f, 1f, 0.95f), Held = new Color(1f, 0.6f, 0.15f, 1f);
        /// <summary>The panels taken by their edge besides every ListWindow (by their UiKit.Panel name).</summary>
        /// Client wave 24 (w24), design note (2026-10-12): "require the login panel to be draggable and also the keyboard panel to be draggable":
        /// "LoginPanel" and "VrKeyboard" (the keyboard was left out on purpose until now: the summary's "Which"). A key is a button,
        /// and a button under the laser is always the button (OnLaser's first line): a press on a key is never a grab.
        public static readonly HashSet<string> Named = new HashSet<string> { "MenuBoard", "NpcDialog", "Chat", "GuideWindow", "WorldMapWindow", "Notice", "Death", "LoginPanel", "VrKeyboard", "CodexBook" }; // (wave 28, wcodex: the Codex book is taken by its edge too)
        private static readonly Dictionary<Transform, (Canvas canvas, bool grabbable, UiSurface surface)> _cache = new Dictionary<Transform, (Canvas, bool, UiSurface)>();
        private static readonly List<RectTransform> _shown = new List<RectTransform>();

        /// <summary>Whether this panel (a UiKit.Panel root) can be taken by its edge.</summary>
        public static bool Grabbable(Transform panel) => panel != null && Entry(panel).grabbable;
        private static (Canvas canvas, bool grabbable, UiSurface surface) Entry(Transform t)
        {
            if (!_cache.TryGetValue(t, out var e))
            {
                if (_cache.Count > 512) _cache.Clear();
                e = (t.GetComponent<Canvas>(), Named.Contains(t.name) || t.GetComponent<ListWindow>() != null, t.GetComponent<UiSurface>()); // (the surface: UI/UiKit.cs Panel dresses a panel as it makes it)
                _cache[t] = e;
            }
            return e;
        }
        /// <summary>Client wave 23 (w23inv): how far the panel's visible frame stands out of its rectangle, in the panel's own px
        /// (UI/UiKit.cs Dress: the wood frame of a framed panel; 0: a card, a see-through panel, a dark one). The new inventory's
        /// bent frame is the board's own (UI/InvPanel.cs: the same width).</summary>
        public static float FramePx(Transform panel) { if (panel == null) return 0f; var s = Entry(panel).surface; return s != null ? Mathf.Max(0f, s.Outset) : 0f; }
        /// <summary>Client wave 23 (w23inv): how far outside its rectangle's rim this panel's band reaches, metres: its visible frame
        /// + <see cref="BandOutside"/>.</summary>
        public static float BandReach(RectTransform rt)
        {
            if (rt == null) return BandOutside;
            float s = Mathf.Max(Mathf.Abs(rt.lossyScale.x), 1e-6f); var bend = rt.GetComponent<UiBend>(); if (bend != null && bend.On) s *= bend.Shrink;
            return FramePx(rt) * s + BandOutside;
        }
        /// <summary>The panel is shown now: active, its canvas drawn (the chat hides by its canvas).</summary>
        public static bool IsShown(Transform t) { if (t == null || !t.gameObject.activeInHierarchy) return false; var c = Entry(t).canvas; return c != null && c.enabled; }

        /// <summary>The grabbable panels shown now (a shared list: read it before the next call).</summary>
        public static List<RectTransform> Shown()
        {
            _shown.Clear();
            var all = UiKit.Panels;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                var e = Entry(t);
                if (!e.grabbable || e.canvas == null || !e.canvas.enabled || !(t is RectTransform rt)) continue;
                _shown.Add(rt);
            }
            return _shown;
        }

        /// <summary>Where <paramref name="p"/> is against the panel's rim: in its plane, the signed distance to the rim in metres (negative
        /// inside), its distance off the plane (metres, either side), and the rim's nearest point.</summary>
        public static void Rim(RectTransform rt, Vector3 p, out float rim, out float depth, out Vector3 rimPoint) => Rim(rt, p, out rim, out depth, out rimPoint, out _);
        /// <summary>Client wave 23 (w23inv): the same, and <paramref name="beyond"/>: how far the point lies OUTSIDE the band's outer
        /// limit (the visible frame's outer edge + <see cref="BandOutside"/>; square at the corners), in the panel's plane, metres
        /// (0: on the frame, inside it, or within the band beyond it).</summary>
        public static void Rim(RectTransform rt, Vector3 p, out float rim, out float depth, out Vector3 rimPoint, out float beyond)
        {
            float s = Mathf.Max(Mathf.Abs(rt.lossyScale.x), 1e-6f);
            var r = rt.rect; var l = rt.InverseTransformPoint(p);
            var bend = rt.GetComponent<UiBend>(); bool bent = bend != null && bend.On; // client wave 21 (w21inv): a panel bent on a cylinder (the new inventory): its rim is the bent strip's (UI/InvBent.cs)
            if (bent) { l = bend.Place(l); s *= bend.Shrink; }
            float midX = bent && bend.HasSpan ? bend.SpanMid : r.center.x; // (what of a bent strip is there now: the bundle, the part unrolled)
            float hx = bent && bend.HasSpan ? bend.SpanHalf : r.width * 0.5f, hy = r.height * 0.5f, x = l.x - midX, y = l.y - r.center.y;
            float dx = Mathf.Abs(x) - hx, dy = Mathf.Abs(y) - hy;
            depth = Mathf.Abs(l.z) * s;
            Vector2 near;
            if (dx <= 0f && dy <= 0f)
            {
                rim = Mathf.Max(dx, dy) * s; // (inside: the nearer edge)
                near = dx > dy ? new Vector2(Mathf.Sign(x) * hx, y) : new Vector2(x, Mathf.Sign(y) * hy);
            }
            else
            {
                rim = new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude * s;
                near = new Vector2(Mathf.Clamp(x, -hx, hx), Mathf.Clamp(y, -hy, hy));
            }
            // the band's outer limit: the rectangle grown by the visible frame and by BandOutside (px of the panel as it is drawn)
            float grow = FramePx(rt) + BandOutside / s;
            beyond = new Vector2(Mathf.Max(dx - grow, 0f), Mathf.Max(dy - grow, 0f)).magnitude * s;
            rimPoint = rt.TransformPoint(bent ? bend.Local(near.x + midX, near.y + r.center.y) : new Vector3(near.x + midX, near.y + r.center.y, 0f));
        }

        /// <summary>A point's distance to the panel's band (0 on it), in metres.</summary>
        public static float BandDistance(RectTransform rt, Vector3 p, out Vector3 rimPoint)
        {
            Rim(rt, p, out float rim, out float depth, out rimPoint, out float beyond);
            float lat = rim < -BandInside ? -BandInside - rim : beyond; // (client wave 23: outside, the band ends BandOutside beyond the visible frame)
            return new Vector2(lat, depth).magnitude;
        }

        /// <summary>The panel whose band a hand's grip point is within <see cref="HandReach"/> of (the nearest; null: none). skip: a panel
        /// not to offer (held already).</summary>
        public static RectTransform AtHand(Vector3 p, System.Func<RectTransform, bool> skip, out Vector3 rimPoint, out float dist)
        {
            RectTransform best = null; rimPoint = default; dist = float.MaxValue;
            foreach (var rt in Shown())
            {
                if (skip != null && skip(rt)) continue;
                float d = BandDistance(rt, p, out var rp);
                if (d <= HandReach && d < dist) { best = rt; dist = d; rimPoint = rp; }
            }
            return best;
        }

        /// <summary>The panel whose band the laser is on (null: none). first: what the laser met first (its distance, null: nothing within
        /// its reach), whether that is a UI collider, its panel, whether it is a button.</summary>
        public static RectTransform OnLaser(Ray ray, float firstDist, bool firstIsUi, Transform firstPanel, bool firstIsButton, Vector3 firstPoint, System.Func<RectTransform, bool> skip, out Vector3 point, out float dist)
        {
            point = default; dist = float.MaxValue;
            if (firstIsButton) return null; // a button under the laser is the button
            if (firstIsUi)
            {
                // on a panel's background: its band's inside part (the rim's last BandInside metres)
                if (!(firstPanel is RectTransform fp) || !Grabbable(fp) || !IsShown(fp) || (skip != null && skip(fp))) return null;
                Rim(fp, firstPoint, out float rim, out _, out _);
                if (rim < -BandInside || rim > 0.002f) return null;
                point = firstPoint; dist = firstDist; return fp;
            }
            // nothing of the UI first: the band's outside part, with nothing nearer on the laser
            RectTransform best = null;
            foreach (var rt in Shown())
            {
                if (skip != null && skip(rt)) continue;
                var bend = rt.GetComponent<UiBend>();
                if (bend != null && bend.On)
                {
                    // client wave 21 (w21inv): a panel bent on a cylinder (the new inventory): the laser meets the cylinder, not the plane
                    // of its middle (its ends stand far in front of that plane)
                    int n = bend.Raycast(rt.InverseTransformPoint(ray.origin), rt.InverseTransformVector(ray.direction), out float ta, out float tb);
                    for (int k = 0; k < n; k++)
                    {
                        float tk = k == 0 ? ta : tb; if (tk >= dist || tk > firstDist + 0.01f) continue;
                        var pk = ray.GetPoint(tk); Rim(rt, pk, out float rk, out _, out _, out float bk);
                        if (rk < 0f || bk > 0f) continue; // (client wave 23: on the frame, or within BandOutside beyond its outer edge)
                        best = rt; dist = tk; point = pk; break;
                    }
                    continue;
                }
                var plane = new Plane(rt.forward, rt.position);
                if (!plane.Raycast(ray, out float t) || t <= 0f || t >= dist || t > firstDist + 0.01f) continue;
                var p = ray.GetPoint(t);
                Rim(rt, p, out float rim, out _, out _, out float past);
                if (rim < 0f || past > 0f) continue; // (client wave 23: on the frame, or within BandOutside beyond its outer edge)
                best = rt; dist = t; point = p;
            }
            return best;
        }

        // ------------------------------------------------------------------ the indicator
        private static Texture2D _markTex;
        /// <summary>The grab mark's picture: a ring with a solid middle (a handle to take), white (the material tints it).</summary>
        private static Texture2D MarkTexture()
        {
            if (_markTex != null) return _markTex;
            const int n = 64; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "GrabMark", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float r = new Vector2(x - (n - 1) * 0.5f, y - (n - 1) * 0.5f).magnitude / (n * 0.5f);
                    // four short spokes between ring and middle: "take and move"
                    float ax = Mathf.Abs(x - (n - 1) * 0.5f) / (n * 0.5f), ay = Mathf.Abs(y - (n - 1) * 0.5f) / (n * 0.5f);
                    bool spoke = r > 0.38f && r < 0.66f && (ax < 0.07f || ay < 0.07f);
                    bool ring = r > 0.70f && r <= 0.97f, mid = r <= 0.34f, rim = (r > 0.97f && r <= 1f) || (r > 0.66f && r <= 0.70f);
                    byte a = (byte)(ring || mid || spoke ? 255 : rim ? 150 : 0); byte v = (byte)(rim ? 20 : 255);
                    px[y * n + x] = new Color32(v, v, v, a);
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return _markTex = tex;
        }

        /// <summary>One indicator: an outline on a panel's rim and a grab mark (a hand's, or the laser's).</summary>
        public sealed class Light
        {
            private readonly LineRenderer _line; private readonly Transform _mark; private readonly Material _lineMat, _markMat;
            private readonly Vector3[] _corners = new Vector3[4];
            // client wave 23 (w23inv): the glow over the whole band (a flat ring; a bent strip's follows its curve)
            private readonly Transform _band; private readonly Mesh _bandMesh; private readonly Material _bandMat;
            private readonly List<Vector3> _bandVerts = new List<Vector3>(); private readonly List<int> _bandTris = new List<int>();
            public RectTransform Panel { get; private set; }
            public bool IsHeld { get; private set; }
            public bool Shown => _line != null && _line.enabled;
            public Vector3 MarkAt => _mark != null ? _mark.position : default;
            /// <summary>The glow over the band is drawn now; its corner points (world) as they were laid at the last
            /// <see cref="Show"/> (four to a column: the outer limit below, the inner one below, the inner one above, the outer
            /// one above) (smoke).</summary>
            public bool GlowShown => _band != null && _band.gameObject.activeSelf;
            public int GlowPoints => _bandVerts.Count;
            public Vector3 GlowPoint(int i) => _band.TransformPoint(_bandVerts[i]);

            public Light(Transform parent, string name)
            {
                var sh = Shader.Find("Loki/UIOverlay"); if (sh == null) sh = Shader.Find("Sprites/Default");
                var go = new GameObject(name); go.layer = UiKit.PanelLayer; go.transform.SetParent(parent, false);
                _line = go.AddComponent<LineRenderer>();
                _line.useWorldSpace = true; _line.loop = true; _line.positionCount = 4; _line.startWidth = _line.endWidth = OutlineWidth; _line.numCornerVertices = 2;
                _lineMat = new Material(sh) { name = name + "Line", renderQueue = VR.DioramaRig.LaserQueue - 1 };
                _line.material = _lineMat; _line.sortingOrder = 12; // (after the panels' 10 and the wand's 11, before the laser's 30)
                _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; _line.receiveShadows = false; _line.enabled = false;
                var m = GameObject.CreatePrimitive(PrimitiveType.Quad); m.name = name + "Mark"; m.layer = UiKit.PanelLayer;
                Object.Destroy(m.GetComponent<Collider>()); // (never in the laser's way)
                m.transform.SetParent(parent, false); m.transform.localScale = Vector3.one * MarkSize;
                _markMat = new Material(sh) { name = name + "Mark", mainTexture = MarkTexture(), renderQueue = VR.DioramaRig.LaserQueue - 1 };
                var mr = m.GetComponent<MeshRenderer>(); mr.sharedMaterial = _markMat; mr.sortingOrder = 13;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                m.SetActive(false); _mark = m.transform;
                // the glow: under the outline (the same order, an earlier queue), over the panels
                var b = new GameObject(name + "Band"); b.layer = UiKit.PanelLayer; b.transform.SetParent(parent, false);
                _bandMesh = new Mesh { name = name + "Band" }; _bandMesh.MarkDynamic();
                b.AddComponent<MeshFilter>().sharedMesh = _bandMesh;
                _bandMat = new Material(sh) { name = name + "Band", renderQueue = VR.DioramaRig.LaserQueue - 2 };
                var br = b.AddComponent<MeshRenderer>(); br.sharedMaterial = _bandMat; br.sortingOrder = 12;
                br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; br.receiveShadows = false;
                b.SetActive(false); _band = b.transform;
            }

            /// <summary>Client wave 23 (w23inv): the glow's mesh for this panel as it stands now: the ring between the band's inner
            /// limit (<see cref="BandInside"/> inside the rim) and its outer one (the visible frame + <see cref="BandOutside"/>): a
            /// column of four points at the outer left, at the inner left, (a bent strip: along its curve) at the inner right and at
            /// the outer right; between two columns the lower and the upper band, and at the two ends the whole height.</summary>
            private void LayGlow(RectTransform panel, UiBend bend)
            {
                bool bent = bend != null && bend.On;
                float s = Mathf.Max(Mathf.Abs(panel.lossyScale.x), 1e-6f) * (bent ? bend.Shrink : 1f);
                var r = panel.rect;
                float x0 = bent && bend.HasSpan ? bend.SpanMid - bend.SpanHalf : r.xMin, x1 = bent && bend.HasSpan ? bend.SpanMid + bend.SpanHalf : r.xMax;
                float inPx = BandInside / s, outPx = FramePx(panel) + BandOutside / s;
                int mid = bent ? 24 : 1, cols = mid + 3;
                _bandVerts.Clear(); _bandTris.Clear();
                for (int c = 0; c < cols; c++)
                {
                    float x = c == 0 ? x0 - outPx : c == cols - 1 ? x1 + outPx : Mathf.Lerp(x0 + inPx, x1 - inPx, (c - 1) / (float)mid);
                    for (int k = 0; k < 4; k++)
                    {
                        float y = k == 0 ? r.yMin - outPx : k == 1 ? r.yMin + inPx : k == 2 ? r.yMax - inPx : r.yMax + outPx;
                        _bandVerts.Add(_band.InverseTransformPoint(panel.TransformPoint(bent ? bend.Local(x, y) : new Vector3(x, y, 0f))));
                    }
                }
                for (int c = 0; c + 1 < cols; c++)
                    for (int k = 0; k < 3; k++)
                    {
                        if (k == 1 && c != 0 && c != cols - 2) continue; // (the middle: only at the two ends)
                        int a = c * 4 + k, b = a + 4;
                        _bandTris.Add(a); _bandTris.Add(a + 1); _bandTris.Add(b + 1); _bandTris.Add(a); _bandTris.Add(b + 1); _bandTris.Add(b);
                    }
                _bandMesh.Clear(); _bandMesh.SetVertices(_bandVerts); _bandMesh.SetTriangles(_bandTris, 0); _bandMesh.RecalculateBounds();
            }

            /// <summary>Lit on <paramref name="panel"/>: held or ready, its mark at <paramref name="markAt"/> facing <paramref name="head"/>.</summary>
            public void Show(RectTransform panel, bool held, Vector3 markAt, Camera head)
            {
                Panel = panel; IsHeld = held;
                var c = held ? Held : Ready;
                if (_lineMat.color != c) { _lineMat.color = c; _markMat.color = c; _bandMat.color = new Color(c.r, c.g, c.b, c.a * GlowAlpha); }
                var bend = panel.GetComponent<UiBend>();
                LayGlow(panel, bend); if (!_band.gameObject.activeSelf) _band.gameObject.SetActive(true); // client wave 23 (w23inv): the glow over the whole band
                if (bend != null && bend.On)
                {
                    // client wave 21 (w21inv): a panel bent on a cylinder (the new inventory): the outline follows the bent strip (UI/InvBent.cs)
                    const int n = 24; var r = panel.rect; float x0 = bend.HasSpan ? bend.SpanMid - bend.SpanHalf : r.xMin, x1 = bend.HasSpan ? bend.SpanMid + bend.SpanHalf : r.xMax;
                    if (_line.positionCount != 2 * (n + 1)) _line.positionCount = 2 * (n + 1);
                    for (int i = 0; i <= n; i++)
                    {
                        float x = Mathf.Lerp(x0, x1, i / (float)n);
                        _line.SetPosition(i, panel.TransformPoint(bend.Local(x, r.yMin))); _line.SetPosition(2 * n + 1 - i, panel.TransformPoint(bend.Local(x, r.yMax)));
                    }
                }
                else
                {
                    if (_line.positionCount != 4) _line.positionCount = 4;
                    panel.GetWorldCorners(_corners);
                    for (int i = 0; i < 4; i++) _line.SetPosition(i, _corners[i]);
                }
                if (!_line.enabled) _line.enabled = true;
                if (!_mark.gameObject.activeSelf) _mark.gameObject.SetActive(true);
                _mark.position = markAt;
                if (head != null) { var d = markAt - head.transform.position; if (d.sqrMagnitude > 1e-6f) _mark.rotation = Quaternion.LookRotation(d.normalized, Vector3.up); }
            }

            public void Hide()
            {
                Panel = null; IsHeld = false;
                if (_line.enabled) _line.enabled = false;
                if (_mark.gameObject.activeSelf) _mark.gameObject.SetActive(false);
                if (_band.gameObject.activeSelf) _band.gameObject.SetActive(false);
            }
        }
    }
}
