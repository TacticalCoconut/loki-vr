using System;
using System.Collections.Generic;
using UnityEngine;

namespace Loki.VrMaps
{
    /// <summary>
    /// The scene's triangles binned on a 1-cell grid in x / z (each bin keeps its height range), and the four queries of the plan:
    /// Q1 <see cref="Column"/> (every surface a vertical line crosses), Q2 <see cref="Nearest"/> (the nearest surface to a point within r),
    /// Q3 <see cref="Capsule"/> (a segment with a radius against the surfaces), Q4 <see cref="Ray"/> (the first surface the eye sees, by
    /// the renderer's rules: a single-sided triangle seen from behind is culled and passed, water is transparent and passed).
    /// Read-only after construction: queries may run on several threads, each with its own <see cref="RayState"/>.
    /// </summary>
    public sealed class Grid
    {
        public readonly Scene S;
        public readonly int X0, Z0, NX, NZ;
        private readonly int[] _start, _items;
        public readonly float[] MinY, MaxY;
        public const int Margin = 256;

        public Grid(Scene s)
        {
            S = s;
            int gx0 = int.MaxValue, gz0 = int.MaxValue, gx1 = int.MinValue, gz1 = int.MinValue;
            var bx0 = new int[s.Count]; var bx1 = new int[s.Count]; var bz0 = new int[s.Count]; var bz1 = new int[s.Count];
            for (int t = 0; t < s.Count; t++)
            {
                float mnx = float.MaxValue, mxx = float.MinValue, mnz = float.MaxValue, mxz = float.MinValue;
                for (int k = 0; k < 3; k++) { float x = s.P[t * 9 + k * 3], z = s.P[t * 9 + k * 3 + 2]; if (x < mnx) mnx = x; if (x > mxx) mxx = x; if (z < mnz) mnz = z; if (z > mxz) mxz = z; }
                Span(mnx, mxx, out bx0[t], out bx1[t]); Span(mnz, mxz, out bz0[t], out bz1[t]);
                bx0[t] = Math.Max(bx0[t], -Margin); bz0[t] = Math.Max(bz0[t], -Margin); bx1[t] = Math.Min(bx1[t], s.W + Margin); bz1[t] = Math.Min(bz1[t], s.H + Margin);
                if (bx1[t] < bx0[t] || bz1[t] < bz0[t]) continue;
                gx0 = Math.Min(gx0, bx0[t]); gz0 = Math.Min(gz0, bz0[t]); gx1 = Math.Max(gx1, bx1[t]); gz1 = Math.Max(gz1, bz1[t]);
            }
            gx0 = Math.Min(gx0, 0); gz0 = Math.Min(gz0, 0); gx1 = Math.Max(gx1, s.W - 1); gz1 = Math.Max(gz1, s.H - 1);
            X0 = gx0; Z0 = gz0; NX = gx1 - gx0 + 1; NZ = gz1 - gz0 + 1;
            int nb = NX * NZ;
            var count = new int[nb + 1];
            for (int t = 0; t < s.Count; t++)
                for (int z = bz0[t]; z <= bz1[t]; z++) for (int x = bx0[t]; x <= bx1[t]; x++) count[(x - X0) + (z - Z0) * NX]++;
            _start = new int[nb + 1];
            for (int b = 0; b < nb; b++) _start[b + 1] = _start[b] + count[b];
            _items = new int[_start[nb]];
            var fill = new int[nb]; Array.Copy(_start, fill, nb);
            MinY = new float[nb]; MaxY = new float[nb];
            for (int b = 0; b < nb; b++) { MinY[b] = float.MaxValue; MaxY[b] = float.MinValue; }
            for (int t = 0; t < s.Count; t++)
            {
                float mny = Math.Min(s.P[t * 9 + 1], Math.Min(s.P[t * 9 + 4], s.P[t * 9 + 7])), mxy = Math.Max(s.P[t * 9 + 1], Math.Max(s.P[t * 9 + 4], s.P[t * 9 + 7]));
                for (int z = bz0[t]; z <= bz1[t]; z++)
                    for (int x = bx0[t]; x <= bx1[t]; x++)
                    {
                        int b = (x - X0) + (z - Z0) * NX;
                        _items[fill[b]++] = t;
                        if (mny < MinY[b]) MinY[b] = mny; if (mxy > MaxY[b]) MaxY[b] = mxy;
                    }
            }
            GlobalMaxY = float.MinValue; GlobalMinY = float.MaxValue;
            for (int b = 0; b < nb; b++) if (_start[b + 1] > _start[b]) { GlobalMaxY = Math.Max(GlobalMaxY, MaxY[b]); GlobalMinY = Math.Min(GlobalMinY, MinY[b]); }
        }
        public readonly float GlobalMinY, GlobalMaxY;
        public int Items => _items.Length;

        /// <summary>The bins a [min, max] extent covers: a cell's interval is [b, b + 1); a zero-width extent on a bin border goes to
        /// both bins it touches.</summary>
        private static void Span(float mn, float mx, out int b0, out int b1)
        {
            b0 = Mathf.FloorToInt(mn); b1 = Math.Max(b0, Mathf.CeilToInt(mx) - 1);
            if (mx - mn < 1e-5f && Math.Abs(mn - Mathf.Round(mn)) < 1e-5f) { b0 = Mathf.RoundToInt(mn) - 1; b1 = b0 + 1; }
        }

        private int BinIndex(int x, int z) => x < X0 || z < Z0 || x >= X0 + NX || z >= Z0 + NZ ? -1 : (x - X0) + (z - Z0) * NX;

        // ------------------------------------------------------------------ Q1: the column
        /// <summary>Every triangle (passing <paramref name="filter"/>) the vertical line through (x, z) crosses, with its height there.
        /// Vertical triangles (no area seen from above) are not crossed.</summary>
        public void Column(float x, float z, List<(float y, int t)> hits, Func<int, bool> filter = null)
        {
            hits.Clear();
            int b = BinIndex(Mathf.FloorToInt(x), Mathf.FloorToInt(z)); if (b < 0) return;
            var P = S.P;
            for (int i = _start[b]; i < _start[b + 1]; i++)
            {
                int t = _items[i]; if (filter != null && !filter(t)) continue;
                int o = t * 9;
                float ax = P[o], az = P[o + 2], bx = P[o + 3], bz = P[o + 5], cx = P[o + 6], cz = P[o + 8];
                float d = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz);
                if (Math.Abs(d) < 1e-9f) continue;
                float l1 = ((bz - cz) * (x - cx) + (cx - bx) * (z - cz)) / d, l2 = ((cz - az) * (x - cx) + (ax - cx) * (z - cz)) / d, l3 = 1f - l1 - l2;
                const float e = -1e-5f;
                if (l1 < e || l2 < e || l3 < e) continue;
                if (!S.OpaqueAt(t, l2, l3)) continue; // a texel the shader cuts out (leaves, grilles)
                hits.Add((l1 * P[o + 1] + l2 * P[o + 4] + l3 * P[o + 7], t));
            }
        }

        /// <summary>The highest point of the drawn surfaces over the cell's square [x, x+1] x [z, z+1] (each triangle clipped to the
        /// square, so a thin vertical wall on the cell counts; float.MinValue when none).</summary>
        public float MaxYInCell(int x, int z, Func<int, bool> filter = null)
        {
            float top = float.MinValue;
            var poly = new List<Vector3>(8); var tmp = new List<Vector3>(8);
            for (int bz = z - 1; bz <= z + 1; bz++)
                for (int bx = x - 1; bx <= x + 1; bx++)
                {
                    int b = BinIndex(bx, bz); if (b < 0 || _start[b + 1] == _start[b] || MaxY[b] <= top) continue;
                    for (int i = _start[b]; i < _start[b + 1]; i++)
                    {
                        int t = _items[i]; if (filter != null && !filter(t)) continue;
                        if (Math.Max(S.P[t * 9 + 1], Math.Max(S.P[t * 9 + 4], S.P[t * 9 + 7])) <= top) continue;
                        poly.Clear(); poly.Add(S.V(t, 0)); poly.Add(S.V(t, 1)); poly.Add(S.V(t, 2));
                        Geo.ClipXZ(poly, tmp, x, x + 1, z, z + 1);
                        foreach (var p in poly) if (p.y > top) top = p.y;
                    }
                }
            return top;
        }

        /// <summary>The model instance that ENCLOSES the point (-1: none): one with a surface above it in its column and whose surfaces
        /// stop at least 7 of 8 level rays from the point (either side counts) within its own box. RO's buildings are mostly open at the
        /// bottom (the camera never saw it), so a closed-mesh parity test would miss them; walls round and a roof above do not.</summary>
        public int Enclosing(RayState st, Vector3 p, List<(float y, int t)> col)
        {
            Column(p.x, p.z, col, t => S.Kind[t] == TriKind.Model);
            int tried0 = -1, tried1 = -1;
            foreach (var (hy, t) in col)
            {
                if (hy <= p.y) continue;
                int inst = S.Src[t]; if (inst == tried0 || inst == tried1) continue;
                if (tried0 < 0) tried0 = inst; else tried1 = inst;
                var box = S.Instances[inst]; float reach = (box.Max - box.Min).magnitude + 0.1f;
                int hits = 0;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * (float)Math.PI / 4f; var d = new Vector3((float)Math.Cos(a), 0f, (float)Math.Sin(a));
                    var h = Ray(st, p, d, 0f, reach, tt => S.Kind[tt] == TriKind.Model && S.Src[tt] == inst);
                    if (h.Tri >= 0 || h.BackTri >= 0) hits++;
                    if (hits + (7 - k) < 7) break;
                }
                if (hits >= 7) return inst;
                if (tried1 >= 0) break;
            }
            return -1;
        }

        // ------------------------------------------------------------------ Q2: the nearest surface
        /// <summary>The distance from <paramref name="p"/> to the nearest triangle within <paramref name="r"/> (r when none), and that triangle (-1).</summary>
        public float Nearest(Vector3 p, float r, out int tri, Func<int, bool> filter = null)
        {
            tri = -1; float best = r, best2 = r * r;
            int x0 = Mathf.FloorToInt(p.x - r), x1 = Mathf.FloorToInt(p.x + r), z0 = Mathf.FloorToInt(p.z - r), z1 = Mathf.FloorToInt(p.z + r);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int b = BinIndex(x, z); if (b < 0 || _start[b + 1] == _start[b]) continue;
                    if (p.y + r < MinY[b] || p.y - r > MaxY[b]) continue;
                    for (int i = _start[b]; i < _start[b + 1]; i++)
                    {
                        int t = _items[i]; if (filter != null && !filter(t)) continue;
                        var q = Geo.ClosestOnTriangle(p, S.V(t, 0), S.V(t, 1), S.V(t, 2));
                        float d2 = (q - p).sqrMagnitude;
                        if (d2 < best2) { best2 = d2; tri = t; }
                    }
                }
            return tri >= 0 ? (float)Math.Sqrt(best2) : best;
        }

        // ------------------------------------------------------------------ Q3: the capsule
        /// <summary>True when some triangle comes within <paramref name="r"/> of the segment a-b; the first such triangle found.</summary>
        public bool Capsule(Vector3 a, Vector3 b, float r, out int tri, Func<int, bool> filter = null)
        {
            tri = -1;
            float mnx = Math.Min(a.x, b.x) - r, mxx = Math.Max(a.x, b.x) + r, mnz = Math.Min(a.z, b.z) - r, mxz = Math.Max(a.z, b.z) + r;
            float mny = Math.Min(a.y, b.y) - r, mxy = Math.Max(a.y, b.y) + r;
            for (int z = Mathf.FloorToInt(mnz); z <= Mathf.FloorToInt(mxz); z++)
                for (int x = Mathf.FloorToInt(mnx); x <= Mathf.FloorToInt(mxx); x++)
                {
                    int bi = BinIndex(x, z); if (bi < 0 || _start[bi + 1] == _start[bi]) continue;
                    if (mxy < MinY[bi] || mny > MaxY[bi]) continue;
                    for (int i = _start[bi]; i < _start[bi + 1]; i++)
                    {
                        int t = _items[i]; if (filter != null && !filter(t)) continue;
                        if (Geo.SegmentTriangleDistance(a, b, S.V(t, 0), S.V(t, 1), S.V(t, 2)) < r) { tri = t; return true; }
                    }
                }
            return false;
        }

        // ------------------------------------------------------------------ Q4: the eye's ray
        public sealed class RayState { public int[] Stamp; public int Id; public RayState(int n) { Stamp = new int[n]; } }

        public struct RayHit
        {
            public int Tri; public float T;           // the first surface drawn and seen (-1: none)
            public int BackTri; public float BackT;   // the nearest single-sided surface passed because it was seen from behind (-1: none)
            public int WaterTri; public float WaterT; // the nearest water passed (-1: none)
        }

        /// <summary>The first drawn surface along o + t d (t in (tMin, tMax]), as the renderer shows it: a single-sided triangle whose back
        /// faces the eye is culled (passed, recorded), water is passed (recorded); <paramref name="visible"/> (the room mask) may
        /// refuse a hit point.</summary>
        public RayHit Ray(RayState st, Vector3 o, Vector3 d, float tMin, float tMax, Func<int, bool> filter = null, Func<Vector3, bool> visible = null)
        {
            var h = new RayHit { Tri = -1, T = tMax, BackTri = -1, BackT = tMax, WaterTri = -1, WaterT = tMax };
            if (++st.Id == int.MaxValue) { Array.Clear(st.Stamp, 0, st.Stamp.Length); st.Id = 1; }
            // the grid's box in x / z
            float bx0 = X0, bx1 = X0 + NX, bz0 = Z0, bz1 = Z0 + NZ;
            float t0 = tMin, t1 = tMax;
            if (!Clip(o.x, d.x, bx0, bx1, ref t0, ref t1) || !Clip(o.z, d.z, bz0, bz1, ref t0, ref t1)) return h;
            var start = o + d * (t0 + 1e-5f);
            int cx = Mathf.FloorToInt(start.x), cz = Mathf.FloorToInt(start.z);
            cx = Mathf.Clamp(cx, X0, X0 + NX - 1); cz = Mathf.Clamp(cz, Z0, Z0 + NZ - 1);
            int sx = d.x > 0 ? 1 : d.x < 0 ? -1 : 0, sz = d.z > 0 ? 1 : d.z < 0 ? -1 : 0;
            float tdx = sx != 0 ? Math.Abs(1f / d.x) : float.MaxValue, tdz = sz != 0 ? Math.Abs(1f / d.z) : float.MaxValue;
            float tnx = sx > 0 ? (cx + 1 - o.x) / d.x : sx < 0 ? (cx - o.x) / d.x : float.MaxValue;
            float tnz = sz > 0 ? (cz + 1 - o.z) / d.z : sz < 0 ? (cz - o.z) / d.z : float.MaxValue;
            float tEnter = t0;
            var P = S.P;
            while (true)
            {
                float tExit = Math.Min(Math.Min(tnx, tnz), t1);
                int b = BinIndex(cx, cz);
                if (b >= 0 && _start[b + 1] > _start[b])
                {
                    float ya = o.y + d.y * tEnter, yb = o.y + d.y * tExit;
                    if (!(Math.Max(ya, yb) < MinY[b] - 1e-4f || Math.Min(ya, yb) > MaxY[b] + 1e-4f))
                        for (int i = _start[b]; i < _start[b + 1]; i++)
                        {
                            int t = _items[i];
                            if (st.Stamp[t] == st.Id) continue; st.Stamp[t] = st.Id;
                            if (filter != null && !filter(t)) continue;
                            if (!Geo.RayTriangle(o, d, S.V(t, 0), S.V(t, 1), S.V(t, 2), out float tt, out float bu, out float bv)) continue;
                            if (tt <= tMin || tt >= h.T) continue;
                            if (!S.OpaqueAt(t, bu, bv)) continue; // a texel the shader cuts out: the eye sees through it
                            if (visible != null && !visible(o + d * tt)) continue;
                            var kind = S.Kind[t];
                            if (kind == TriKind.Water) { if (tt < h.WaterT) { h.WaterT = tt; h.WaterTri = t; } continue; }
                            float facing = d.x * S.N[t * 3] + d.y * S.N[t * 3 + 1] + d.z * S.N[t * 3 + 2];
                            if (!S.TwoSided[t] && facing > 0f) { if (tt < h.BackT) { h.BackT = tt; h.BackTri = t; } continue; }
                            h.T = tt; h.Tri = t;
                        }
                }
                if (h.Tri >= 0 && h.T <= tExit) break;
                if (tExit >= t1) break;
                if (tnx < tnz) { cx += sx; tEnter = tnx; tnx += tdx; } else { cz += sz; tEnter = tnz; tnz += tdz; }
                if (cx < X0 || cz < Z0 || cx >= X0 + NX || cz >= Z0 + NZ) break;
                // above everything and climbing: nothing more to hit
                if (d.y > 0 && o.y + d.y * tEnter > GlobalMaxY) break;
            }
            if (h.Tri < 0) h.T = float.PositiveInfinity;
            if (h.BackTri >= 0 && h.BackT >= h.T - 1e-4f) { h.BackTri = -1; } // a two-sided face: its front copy lies at the same point
            if (h.WaterTri >= 0 && h.WaterT > h.T) { h.WaterTri = -1; }
            return h;
        }

        private static bool Clip(float o, float d, float lo, float hi, ref float t0, ref float t1)
        {
            if (Math.Abs(d) < 1e-12f) return o >= lo && o <= hi;
            float a = (lo - o) / d, b = (hi - o) / d; if (a > b) { var x = a; a = b; b = x; }
            t0 = Math.Max(t0, a); t1 = Math.Min(t1, b);
            return t0 <= t1;
        }
    }

    /// <summary>Exact small geometry (Ericson, Real-Time Collision Detection, 5.1.5 / 5.1.9; Moller-Trumbore).</summary>
    public static class Geo
    {
        /// <summary>Sutherland-Hodgman: clips the polygon (in place) to x0 &lt;= x &lt;= x1, z0 &lt;= z &lt;= z1 (heights interpolated);
        /// a vertical polygon standing on the square's edge is kept.</summary>
        public static void ClipXZ(List<Vector3> poly, List<Vector3> tmp, float x0, float x1, float z0, float z1)
        {
            Clip(poly, tmp, p => p.x - x0, 1e-5f); Clip(poly, tmp, p => x1 - p.x, 1e-5f); Clip(poly, tmp, p => p.z - z0, 1e-5f); Clip(poly, tmp, p => z1 - p.z, 1e-5f);
        }
        private static void Clip(List<Vector3> poly, List<Vector3> tmp, Func<Vector3, float> inside, float eps)
        {
            tmp.Clear();
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                float da = inside(a), db = inside(b);
                if (da >= -eps) tmp.Add(a);
                if ((da >= -eps) != (db >= -eps) && Math.Abs(da - db) > 1e-12f) tmp.Add(a + (b - a) * (da / (da - db)));
            }
            poly.Clear(); poly.AddRange(tmp);
        }

        public static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) { float v = d1 / (d1 - d3); return a + ab * v; }
            Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) { float w = d2 / (d2 - d6); return a + ac * w; }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f) { float w = (d4 - d3) / ((d4 - d3) + (d5 - d6)); return b + (c - b) * w; }
            float den = 1f / (va + vb + vc);
            float vv = vb * den, ww = vc * den;
            return a + ab * vv + ac * ww;
        }

        /// <summary>Moller-Trumbore, both sides; false for a ray parallel to the triangle.</summary>
        public static bool RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t) => RayTriangle(o, d, a, b, c, out t, out _, out _);
        /// <summary>... with the barycentric weights of corners b (u) and c (v).</summary>
        public static bool RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t, out float u, out float v)
        {
            t = 0f; u = 0f; v = 0f;
            Vector3 e1 = b - a, e2 = c - a, pv = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, pv);
            if (Math.Abs(det) < 1e-12f) return false;
            float inv = 1f / det; Vector3 tv = o - a;
            u = Vector3.Dot(tv, pv) * inv; if (u < -1e-6f || u > 1f + 1e-6f) return false;
            Vector3 qv = Vector3.Cross(tv, e1);
            v = Vector3.Dot(d, qv) * inv; if (v < -1e-6f || u + v > 1f + 1e-6f) return false;
            t = Vector3.Dot(e2, qv) * inv;
            return true;
        }

        /// <summary>Closest distance between segments p1-q1 and p2-q2 (Ericson 5.1.9).</summary>
        public static float SegmentSegment(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            const float Eps = 1e-12f;
            if (a <= Eps && e <= Eps) return (p1 - p2).magnitude;
            if (a <= Eps) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= Eps) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), den = a * e - b * b;
                    s = den != 0f ? Mathf.Clamp01((b * f - c * e) / den) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            return ((p1 + d1 * s) - (p2 + d2 * t)).magnitude;
        }

        /// <summary>The distance between segment a-b and triangle (0 when they cross).</summary>
        public static float SegmentTriangleDistance(Vector3 a, Vector3 b, Vector3 t0, Vector3 t1, Vector3 t2)
        {
            var d = b - a;
            if (RayTriangle(a, d, t0, t1, t2, out float t) && t >= 0f && t <= 1f) return 0f;
            float m = (ClosestOnTriangle(a, t0, t1, t2) - a).magnitude;
            m = Math.Min(m, (ClosestOnTriangle(b, t0, t1, t2) - b).magnitude);
            m = Math.Min(m, SegmentSegment(a, b, t0, t1));
            m = Math.Min(m, SegmentSegment(a, b, t1, t2));
            m = Math.Min(m, SegmentSegment(a, b, t2, t0));
            return m;
        }
    }
}
