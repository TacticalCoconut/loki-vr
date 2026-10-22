using System;
using System.Collections.Generic;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 22 (w22tree), the design's point 64: the mesh builder of the 3D skill tree (UI/SkillTree3D.cs). All of the tree's art is made
    /// in code with it: no model file, nothing from the game's data. Units are the tree's own: MILLIMETRES (the tree's root is scaled
    /// 0.001), x to the player's right, y up, z AWAY from the player (the front of a thing is its -z side).
    /// Every vertex carries its material for Resources/Shaders/LokiTree.shader: its colour (<see cref="Col"/>), gloss and glow
    /// (<see cref="Gloss"/>, <see cref="Glow"/>), the point it grows out of and when (the pivot and the birth, 0-1 of the opening).
    /// The parts are drawn in the order they are built (no depth test: see the shader), so a builder makes what is behind first.
    /// A triangle's winding is set from its vertices' normals (<see cref="Tri"/>), so a part only has to give outward normals.
    /// </summary>
    public sealed class TreeMeshBuilder
    {
        public readonly List<Vector3> V = new List<Vector3>(8192), N = new List<Vector3>(8192);
        public readonly List<Color32> C = new List<Color32>(8192);
        public readonly List<Vector4> Uv = new List<Vector4>(8192), Mat = new List<Vector4>(8192);
        public readonly List<Vector4> Grow = new List<Vector4>(8192);
        public readonly List<int> T = new List<int>(24576);
        /// <summary>The pen: the colour, gloss (0 matt - 1 polished) and glow (0 lit - 1 its own light) of what is built next.</summary>
        public Color32 Col = new Color32(255, 255, 255, 255);
        public float Gloss, Glow;
        /// <summary>LIVELINESS (design note, 2026-10-11: "As you level up through your job, the tree should become more lively"): how much of the
        /// tree's liveliness this part takes (0 none - 1: it turns to the theme's lively colour and its own steady light as the job level
        /// rises), from which liveliness on (0: all the way; 0.4: it lights once the tree is 40 % alive: panes lit one by one), whether it
        /// is a BUD (small at liveliness 0, its full size at 1: leaves, flowers) and how much of the family's slow BREATH it takes.
        /// A STATIC look: nothing here moves by itself but the breath (Resources/Shaders/LokiTree.shader).</summary>
        /// With <c>own</c> the part does not turn to the theme's colour: its OWN colour brightens (a pane of coloured glass, a gem).
        public float Life, LifeFrom, Breath; public bool Bud;
        public void Alive(float life, float from = 0f, bool bud = false, float breath = 0f, bool own = false) { Life = own ? -life : life; LifeFrom = from; Bud = bud; Breath = breath; }
        public void Plain() { Life = 0f; LifeFrom = 0f; Bud = false; Breath = 0f; }
        /// <summary>The vertices made now belong to the TRUNK object: the family's rise moves them as one thing (a sword that drops, a
        /// bow that is drawn: UI/SkillTree3D.Intro.cs; the shader's _Trunk* values). After the rise they stand still as everything else.</summary>
        public bool TrunkPart;
        /// <summary>Ornament pieces built so far (leaves, banners, curls, studs ...: what makes a tree ornate; the smoke compares stages).</summary>
        public int Pieces;
        public int Triangles => T.Count / 3;
        public int Vertices => V.Count;

        public void Pen(Color32 col, float gloss = 0f, float glow = 0f) { Col = col; Gloss = gloss; Glow = glow; }

        public int Vert(Vector3 p, Vector3 n, Vector3 pivot, float birth, Vector2 uv = default)
        {
            V.Add(p); N.Add(n); C.Add(Col); Uv.Add(new Vector4(uv.x, uv.y, Breath, (Bud ? 1f : 0f) + (TrunkPart ? 2f : 0f))); Mat.Add(new Vector4(Gloss, Glow, Life, LifeFrom)); Grow.Add(new Vector4(pivot.x, pivot.y, pivot.z, birth));
            return V.Count - 1;
        }

        /// <summary>A triangle, wound so that its front is the side its vertices' normals point to (Unity: clockwise seen from the front,
        /// i.e. cross(b - a, c - a) points at the viewer).</summary>
        public void Tri(int a, int b, int c)
        {
            var f = Vector3.Cross(V[b] - V[a], V[c] - V[a]);
            if (Vector3.Dot(f, N[a] + N[b] + N[c]) < 0f) { int t = b; b = c; c = t; }
            T.Add(a); T.Add(b); T.Add(c);
        }
        public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

        // ------------------------------------------------------------------ tubes
        /// <summary>A round tube along <paramref name="p"/> (a branch, a pole, a string): <paramref name="radius"/> by the share of its
        /// length (0 at its start), <paramref name="sides"/> round; it grows from <paramref name="b0"/> (start) to <paramref name="b1"/>
        /// (end), each ring out of its own middle. <paramref name="flat"/> squeezes it front to back (a ribbon-like branch).</summary>
        public void Tube(IList<Vector3> p, Func<float, float> radius, int sides, float b0, float b1, bool capStart = false, bool capEnd = true, float flat = 1f)
        {
            int n = p.Count; if (n < 2) return;
            float total = 0f; var len = new float[n];
            for (int i = 1; i < n; i++) { total += (p[i] - p[i - 1]).magnitude; len[i] = total; }
            if (total < 1e-4f) return;
            int first = V.Count;
            for (int i = 0; i < n; i++)
            {
                var t = (i == 0 ? p[1] - p[0] : i == n - 1 ? p[n - 1] - p[n - 2] : p[i + 1] - p[i - 1]).normalized;
                Frame(t, out var u, out var w);
                float s = len[i] / total, r = Mathf.Max(0.01f, radius(s)), birth = Mathf.Lerp(b0, b1, s);
                for (int k = 0; k < sides; k++)
                {
                    float a = k * Mathf.PI * 2f / sides;
                    var dir = Mathf.Cos(a) * u + Mathf.Sin(a) * flat * w;
                    var nrm = (Mathf.Cos(a) * u * flat + Mathf.Sin(a) * w).normalized;
                    Vert(p[i] + dir * r, nrm, p[i], birth);
                }
            }
            for (int i = 0; i < n - 1; i++)
                for (int k = 0; k < sides; k++)
                {
                    int a = first + i * sides + k, b = first + i * sides + (k + 1) % sides, c = a + sides, d = b + sides;
                    Tri(a, c, d); Tri(a, d, b);
                }
            if (capEnd) Cap(p[n - 1], (p[n - 1] - p[n - 2]).normalized, radius(1f), first + (n - 1) * sides, sides, b1);
            if (capStart) Cap(p[0], (p[0] - p[1]).normalized, radius(0f), first, sides, b0);
        }
        public void Tube(IList<Vector3> p, float r0, float r1, int sides, float b0, float b1, bool capStart = false, bool capEnd = true, float flat = 1f)
            => Tube(p, s => Mathf.Lerp(r0, r1, s), sides, b0, b1, capStart, capEnd, flat);

        private void Cap(Vector3 at, Vector3 dir, float r, int ring, int sides, float birth)
        {
            int tip = Vert(at + dir * (r * 0.7f), dir, at, birth);
            for (int k = 0; k < sides; k++) Tri(ring + k, ring + (k + 1) % sides, tip);
        }

        /// <summary>Two axes across a direction: u in the tree's plane where it can be (so a tube's first side faces sideways), w the other.</summary>
        public static void Frame(Vector3 t, out Vector3 u, out Vector3 w)
        {
            var r = Mathf.Abs(t.z) > 0.9f ? Vector3.up : Vector3.forward;
            u = Vector3.Cross(r, t).normalized; w = Vector3.Cross(t, u).normalized;
        }

        // ------------------------------------------------------------------ turned shapes
        /// <summary>A shape turned on a lathe about <paramref name="axis"/> from <paramref name="origin"/> (a pillar, a pommel, a staff's
        /// knob, a vial): the profile's points are (distance along the axis, radius), the distances never going back (the outward side of
        /// a band is found from that). <paramref name="smooth"/>: one rounded surface;
        /// else every band has its own flat-shaded edge (rings, steps). It grows along the axis from b0 to b1, each ring out of its
        /// middle.</summary>
        public void Lathe(Vector3 origin, Vector3 axis, IList<Vector2> profile, int sides, float b0, float b1, bool smooth = true)
        {
            int n = profile.Count; if (n < 2) return;
            axis.Normalize(); Frame(axis, out var u, out var w);
            float h0 = profile[0].x, h1 = profile[n - 1].x, span = Mathf.Max(1e-4f, Mathf.Abs(h1 - h0));
            Vector2 SegNormal(int i) { var d = profile[i + 1] - profile[i]; var m = new Vector2(-d.y, d.x); return m.sqrMagnitude > 1e-10f ? m.normalized : new Vector2(0f, 1f); } // (along the axis, outward)
            int Ring(int i, Vector2 nrm2)
            {
                int first = V.Count; float birth = Mathf.Lerp(b0, b1, Mathf.Abs(profile[i].x - h0) / span);
                var mid = origin + axis * profile[i].x;
                for (int k = 0; k < sides; k++)
                {
                    float a = k * Mathf.PI * 2f / sides; var rad = Mathf.Cos(a) * u + Mathf.Sin(a) * w;
                    Vert(mid + rad * profile[i].y, (axis * nrm2.x + rad * nrm2.y).normalized, mid, birth);
                }
                return first;
            }
            void Band(int r0, int r1)
            {
                for (int k = 0; k < sides; k++)
                {
                    int a = r0 + k, b = r0 + (k + 1) % sides, c = r1 + k, d = r1 + (k + 1) % sides;
                    if ((V[a] - V[b]).sqrMagnitude > 1e-8f) Tri(a, c, b); // (a ring of radius 0 is a point: no sliver)
                    if ((V[c] - V[d]).sqrMagnitude > 1e-8f) Tri(b, c, d);
                }
            }
            if (smooth)
            {
                var rings = new int[n];
                for (int i = 0; i < n; i++)
                {
                    var m = i == 0 ? SegNormal(0) : i == n - 1 ? SegNormal(n - 2) : (SegNormal(i - 1) + SegNormal(i)).normalized;
                    rings[i] = Ring(i, m);
                }
                for (int i = 0; i < n - 1; i++) Band(rings[i], rings[i + 1]);
            }
            else for (int i = 0; i < n - 1; i++) { var m = SegNormal(i); int r0 = Ring(i, m), r1 = Ring(i + 1, m); Band(r0, r1); }
        }

        /// <summary>A ball (a gem, a pommel, a bead): a lathe of <paramref name="rings"/> bands.</summary>
        public void Ball(Vector3 c, float r, int sides, int rings, float birth, float squash = 1f)
        {
            var prof = new List<Vector2>(rings + 1);
            for (int i = 0; i <= rings; i++) { float a = Mathf.PI * i / rings; prof.Add(new Vector2(-Mathf.Cos(a) * r * squash, Mathf.Sin(a) * r)); }
            int first = V.Count;
            Lathe(c, Vector3.up, prof, sides, birth, birth);
            for (int i = first; i < V.Count; i++) Grow[i] = new Vector4(c.x, c.y, c.z, birth); // out of its own middle, all at once
        }

        // ------------------------------------------------------------------ plates
        /// <summary>A flat plate cut along <paramref name="outline"/> (a leaf, a banner, a shield, a wing, a blade's guard): the outline in
        /// its own flat frame (x right, y up), put into the tree by <paramref name="m"/>, <paramref name="thick"/> front to back. The
        /// outline must be seen whole from <paramref name="centre"/> (its faces are fans from there). It pops out of
        /// <paramref name="pivot"/> (the outline's frame) at <paramref name="birth"/>. <paramref name="edge"/>: the rim's own colour.</summary>
        public void Plate(IList<Vector2> outline, Matrix4x4 m, float thick, float birth, Vector2 pivot, Vector2 centre, Color32? edge = null)
        {
            int n = outline.Count; if (n < 3) return;
            var piv = m.MultiplyPoint3x4(new Vector3(pivot.x, pivot.y, 0f));
            Vector3 front = m.MultiplyVector(Vector3.back).normalized, back = -front;
            float h = thick * 0.5f;
            for (int side = 0; side < 2; side++)
            {
                var nrm = side == 0 ? back : front; float z = side == 0 ? h : -h; // the far face first, the near face over it
                int c0 = Vert(m.MultiplyPoint3x4(new Vector3(centre.x, centre.y, z)), nrm, piv, birth), first = V.Count;
                for (int i = 0; i < n; i++) Vert(m.MultiplyPoint3x4(new Vector3(outline[i].x, outline[i].y, z)), nrm, piv, birth);
                for (int i = 0; i < n; i++) Tri(c0, first + i, first + (i + 1) % n);
                if (side == 0 && thick > 0.05f)
                {
                    // the rim between the two faces
                    var keep = Col; if (edge.HasValue) Col = edge.Value;
                    for (int i = 0; i < n; i++)
                    {
                        Vector2 a = outline[i], b = outline[(i + 1) % n], d = b - a; if (d.sqrMagnitude < 1e-8f) continue;
                        var out2 = new Vector2(d.y, -d.x).normalized; if (Vector2.Dot(out2, (a + b) * 0.5f - centre) < 0f) out2 = -out2;
                        var on = m.MultiplyVector(new Vector3(out2.x, out2.y, 0f)).normalized;
                        int v0 = Vert(m.MultiplyPoint3x4(new Vector3(a.x, a.y, h)), on, piv, birth), v1 = Vert(m.MultiplyPoint3x4(new Vector3(b.x, b.y, h)), on, piv, birth);
                        int v2 = Vert(m.MultiplyPoint3x4(new Vector3(b.x, b.y, -h)), on, piv, birth), v3 = Vert(m.MultiplyPoint3x4(new Vector3(a.x, a.y, -h)), on, piv, birth);
                        Quad(v0, v1, v2, v3);
                    }
                    Col = keep;
                }
            }
        }

        /// <summary>A plate's place: at <paramref name="at"/>, turned <paramref name="turnDeg"/> in the tree's plane (0: its y is up),
        /// <paramref name="scale"/> times its outline, leaning <paramref name="leanDeg"/> out of the plane about its own y.</summary>
        public static Matrix4x4 At(Vector3 at, float turnDeg, float scale = 1f, float leanDeg = 0f, float flipX = 1f)
            => Matrix4x4.TRS(at, Quaternion.Euler(0f, 0f, turnDeg) * Quaternion.Euler(0f, leanDeg, 0f), new Vector3(scale * flipX, scale, scale));

        /// <summary>A blade (or any bar of diamond section): from <paramref name="from"/> along <paramref name="dir"/>, its width across
        /// <paramref name="side"/>; half its width and half its thickness by the share of its length (a point where both are 0).</summary>
        public void Blade(Vector3 from, Vector3 dir, Vector3 side, float length, Func<float, float> halfWidth, Func<float, float> halfThick, int segs, float b0, float b1, Color32? shade = null)
        {
            dir.Normalize(); side.Normalize(); var up = Vector3.Cross(dir, side).normalized; // (the ridge's direction: front / back)
            // four faces between the edges (left, front ridge, right, back ridge), each with its own flat normal
            var lightCol = Col;
            for (int face = 0; face < 4; face++)
            {
                Col = shade.HasValue && (face == 1 || face == 3) ? shade.Value : lightCol; // (the faces towards +side in the second tone: a blade's two planes)
                int prev0 = -1, prev1 = -1;
                for (int i = 0; i <= segs; i++)
                {
                    float s = i / (float)segs, hw = Mathf.Max(0f, halfWidth(s)), ht = Mathf.Max(0f, halfThick(s));
                    var mid = from + dir * (length * s);
                    Vector3 e0, e1;
                    switch (face)
                    {
                        case 0: e0 = mid + up * ht; e1 = mid - side * hw; break;     // back ridge → left edge
                        case 1: e0 = mid + side * hw; e1 = mid + up * ht; break;     // right edge → back ridge
                        case 2: e0 = mid - side * hw; e1 = mid - up * ht; break;     // left edge → front ridge
                        default: e0 = mid - up * ht; e1 = mid + side * hw; break;    // front ridge → right edge
                    }
                    var fn = (face == 0 ? -side * ht + up * hw : face == 1 ? side * ht + up * hw : face == 2 ? -side * ht - up * hw : side * ht - up * hw);
                    fn = fn.sqrMagnitude > 1e-10f ? fn.normalized : (face < 2 ? up : -up);
                    float birth = Mathf.Lerp(b0, b1, s);
                    int v0 = Vert(e0, fn, mid, birth), v1 = Vert(e1, fn, mid, birth);
                    if (prev0 >= 0)
                    {
                        if ((V[prev0] - V[prev1]).sqrMagnitude > 1e-8f) Tri(prev0, prev1, v1);
                        if ((V[v0] - V[v1]).sqrMagnitude > 1e-8f) Tri(prev0, v1, v0);
                    }
                    prev0 = v0; prev1 = v1;
                }
            }
            Col = lightCol;
        }

        /// <summary>A box (a crossguard, an anvil's block, a strap).</summary>
        public void Box(Vector3 centre, Vector3 size, Quaternion rot, float birth, Vector3? pivot = null)
        {
            var piv = pivot ?? centre; var h = size * 0.5f;
            Vector3[] ax = { Vector3.right, Vector3.up, Vector3.forward };
            // a face per signed axis (1 x, 2 y, 3 z): the far side (+z) first, the near side (-z) last
            foreach (int code in new[] { 3, -2, 2, -1, 1, -3 })
            {
                int a = Mathf.Abs(code) - 1; float sg = Mathf.Sign(code);
                var nrm = rot * (ax[a] * sg);
                var uu = rot * ax[(a + 1) % 3] * h[(a + 1) % 3]; var vv = rot * ax[(a + 2) % 3] * h[(a + 2) % 3];
                var mid = centre + nrm * h[a];
                Quad(Vert(mid - uu - vv, nrm, piv, birth), Vert(mid + uu - vv, nrm, piv, birth), Vert(mid + uu + vv, nrm, piv, birth), Vert(mid - uu + vv, nrm, piv, birth));
            }
        }

        // ------------------------------------------------------------------ flat rings (the medallions' faces)
        /// <summary>A flat ring's part facing the player at <paramref name="c"/> (z as given): from angle a0 to a1 (degrees, 0 = up,
        /// clockwise as the player sees it), <paramref name="segs"/> pieces; rIn 0: a disc's part. It pops out of
        /// <paramref name="pivot"/>.</summary>
        public void Arc(Vector3 c, float rIn, float rOut, float a0, float a1, int segs, float birth, Vector3 pivot)
        {
            var nrm = Vector3.back; int prevIn = -1, prevOut = -1;
            for (int i = 0; i <= segs; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)segs) * Mathf.Deg2Rad; var d = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f);
                int vo = Vert(c + d * rOut, nrm, pivot, birth), vi = rIn > 0.001f ? Vert(c + d * rIn, nrm, pivot, birth) : -1;
                if (i > 0)
                {
                    if (rIn > 0.001f) Quad(prevIn, prevOut, vo, vi);
                    else { int mid = Vert(c, nrm, pivot, birth); Tri(mid, prevOut, vo); }
                }
                prevIn = vi; prevOut = vo;
            }
        }
        /// <summary>A whole flat ring, or a disc (rIn 0).</summary>
        public void Ring(Vector3 c, float rIn, float rOut, int segs, float birth, Vector3 pivot)
        {
            if (rIn > 0.001f) { Arc(c, rIn, rOut, 0f, 360f, segs, birth, pivot); return; }
            var nrm = Vector3.back; int mid = Vert(c, nrm, pivot, birth), first = V.Count;
            for (int i = 0; i < segs; i++) { float a = i * Mathf.PI * 2f / segs; Vert(c + new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f) * rOut, nrm, pivot, birth); }
            for (int i = 0; i < segs; i++) Tri(mid, first + i, first + (i + 1) % segs);
        }
        /// <summary>A raised round rim facing the player (a medallion's bezel): a slope from rOut at the face up to its ridge and down to
        /// rIn, <paramref name="rise"/> towards the player.</summary>
        public void Bezel(Vector3 c, float rIn, float rOut, float rise, int segs, float birth, Vector3 pivot)
        {
            float rm = (rIn + rOut) * 0.5f, half = (rOut - rIn) * 0.5f;
            var nOut = new Vector2(rise, half).normalized; // (radial, towards the player)
            for (int pass = 0; pass < 2; pass++)
            {
                float ra = pass == 0 ? rOut : rIn, sg = pass == 0 ? 1f : -1f;
                int first = V.Count;
                for (int i = 0; i < segs; i++)
                {
                    float a = i * Mathf.PI * 2f / segs; var d = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f);
                    var nrm = (d * (nOut.x * sg) + Vector3.back * nOut.y).normalized;
                    Vert(c + d * ra, nrm, pivot, birth); Vert(c + d * rm + Vector3.back * rise, nrm, pivot, birth);
                }
                for (int i = 0; i < segs; i++) { int a = first + i * 2, b = first + ((i + 1) % segs) * 2; Quad(a, b, b + 1, a + 1); }
            }
        }
        /// <summary>A flat picture's place on the icon sheet (the icons' own mesh): a square of side <paramref name="size"/> facing the player.</summary>
        public void Picture(Vector3 c, float size, Rect uv, float birth, Vector3 pivot)
        {
            float h = size * 0.5f; var nrm = Vector3.back;
            int a = Vert(c + new Vector3(-h, -h, 0f), nrm, pivot, birth, new Vector2(uv.xMin, uv.yMin)), b = Vert(c + new Vector3(-h, h, 0f), nrm, pivot, birth, new Vector2(uv.xMin, uv.yMax));
            int d = Vert(c + new Vector3(h, h, 0f), nrm, pivot, birth, new Vector2(uv.xMax, uv.yMax)), e = Vert(c + new Vector3(h, -h, 0f), nrm, pivot, birth, new Vector2(uv.xMax, uv.yMin));
            Quad(a, b, d, e);
        }
        /// <summary>A flat line facing the player (a prerequisite's line, a string a medallion hangs by): <paramref name="width"/> wide.</summary>
        public void Line(Vector3 a, Vector3 b, float width, float ba, float bb)
        {
            var d = b - a; d.z = 0f; if (d.sqrMagnitude < 1e-6f) return;
            var side = new Vector3(-d.y, d.x, 0f).normalized * (width * 0.5f); var nrm = Vector3.back;
            Quad(Vert(a - side, nrm, a, ba), Vert(a + side, nrm, a, ba), Vert(b + side, nrm, a, bb), Vert(b - side, nrm, a, bb));
        }

        // ------------------------------------------------------------------ paths
        /// <summary>A smooth path through three points (a quadratic curve from a to c, pulled towards b), <paramref name="n"/> pieces.</summary>
        public static List<Vector3> Curve(Vector3 a, Vector3 b, Vector3 c, int n)
        {
            var l = new List<Vector3>(n + 1);
            for (int i = 0; i <= n; i++) { float t = i / (float)n, u = 1f - t; l.Add(u * u * a + 2f * u * t * b + t * t * c); }
            return l;
        }
        /// <summary>A scroll: a spiral in the tree's plane that starts at <paramref name="from"/> going along <paramref name="dir"/> and
        /// winds <paramref name="turns"/> times inwards (<paramref name="clockwise"/> as the player sees it), its first radius
        /// <paramref name="radius"/>.</summary>
        public static List<Vector3> Scroll(Vector3 from, Vector3 dir, float radius, float turns, bool clockwise, int n)
        {
            dir.z = 0f; dir.Normalize();
            var side = clockwise ? new Vector3(dir.y, -dir.x, 0f) : new Vector3(-dir.y, dir.x, 0f); // towards the spiral's middle
            var mid = from + side * radius; var l = new List<Vector3>(n + 1);
            float a0 = Mathf.Atan2(-side.y, -side.x), sg = clockwise ? -1f : 1f;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n, a = a0 + sg * t * turns * Mathf.PI * 2f, r = radius * (1f - 0.78f * t);
                l.Add(mid + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * r + Vector3.back * (t * radius * 0.25f)); // (it comes a little towards the player as it winds)
            }
            return l;
        }

        // ------------------------------------------------------------------ the mesh
        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name, indexFormat = V.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            Fill(m);
            return m;
        }
        /// <summary>Writes this builder's parts into <paramref name="m"/> (cleared first): a mesh redrawn when a state changes.</summary>
        public void Fill(Mesh m)
        {
            m.Clear();
            if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(V); m.SetNormals(N); m.SetColors(C); m.SetUVs(0, Uv); m.SetUVs(1, Mat); m.SetUVs(2, Grow);
            m.SetTriangles(T, 0, true);
            // the opening moves vertices in the shader: the bounds are the grown tree's, a little generous
            var b = m.bounds; b.Expand(40f); m.bounds = b;
        }
        public void Clear() { V.Clear(); N.Clear(); C.Clear(); Uv.Clear(); Mat.Clear(); Grow.Clear(); T.Clear(); Pieces = 0; Plain(); TrunkPart = false; }
    }
}
