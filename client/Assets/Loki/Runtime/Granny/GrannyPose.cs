// =====================================================================================================================
// Project Loki, client wave 4b (w4granny): a C# port, step for step, of granny-ro-js 1.5.0 (npm, MIT;
// https://github.com/stitchuuuu/granny-ro-js), the Granny3D (.gr2) decoder roBrowserLegacy uses (src/Loaders/GR2Loader.js).
// Source read: package/dist/granny-ro.esm.js (a minified bundle). Comments name the package's exported functions and, in
// brackets, the bundle's own minified names; "l. N" are the lines of that bundle reformatted by esbuild
// (client/tools/w4granny/README.md). Numbers: the package samples curves and chains the bone matrices in doubles
// (plain Arrays) and rounds only the world and skinning matrices it returns (Float32Array); so does this port.
//
// This file: sampling an animation (evaluateAnimation [Nn], evaluateTransformTrack [Mn], the B-spline sampler [An] with the
// DLL-style quaternion renormalisation [On]) and composing a pose (poseSkeletonAt [zn], poseAt [Qn]: local matrices [Bn],
// world matrices composeWorldPose [Ln], skinning matrices composeSkinningMatrices [Rn], multiplyMat4 [In]).
//
// MIT License
//
// Copyright (c) 2026 Stitchuuuu <stitchuuuu@icloud.com>
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// =====================================================================================================================
using System;
using System.Collections.Generic;

namespace Loki.Granny
{
    /// <summary>A pose: per bone its local transform, world matrix and skinning matrix (column-major 4x4, float).</summary>
    public sealed class GrPose
    {
        public GrTransform[] LocalTransforms;
        public float[][] WorldMatrices, SkinningMatrices;
    }

    public static class GrannyPose
    {
        private static readonly double[] IdentityMat = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 }; // [Pn] l. 1904

        /// <summary>[wn] l. 1832-1836: the first knot after t (the last knot when none).</summary>
        private static int KnotIndex(float[] e2, double t2)
        {
            int n2 = e2.Length;
            for (int r2 = 0; r2 < n2; r2++) if (e2[r2] > t2) return r2;
            return n2 > 0 ? n2 - 1 : 0;
        }

        private static double SafeDiv(double e2, double t2) => Math.Abs(t2) < 1e-12 ? 0 : e2 / t2; // [Tn] l. 1837-1839

        /// <summary>[On] l. 1848-1851: granny2.dll's one-Newton-step renormalisation q *= (3 - |q|²) / 2.</summary>
        private static double[] Renormalize(double[] e2)
        {
            double t2 = e2[0], n2 = e2[1], r2 = e2[2], i2 = e2[3], a2 = (3 - (t2 * t2 + n2 * n2 + r2 * r2 + i2 * i2)) * 0.5;
            return new[] { t2 * a2, n2 * a2, r2 * a2, i2 * a2 };
        }

        /// <summary>[kn] l. 1852-1856: control point i (clamped to the last).</summary>
        private static double[] Control(float[] e2, int t2, int n2)
        {
            double r2 = (double)e2.Length / t2;
            long i2 = (long)((n2 < r2 ? n2 : r2 - 1) * t2);
            var a2 = new double[t2];
            for (int n3 = 0; n3 < t2; n3++) a2[n3] = e2[i2 + n3];
            return a2;
        }

        /// <summary>[An] l. 1857-1886: a curve's value at t (null when it has none).</summary>
        public static double[] SampleCurve(GrCurve e2, double t2)
        {
            if (e2 == null) return null;
            var n2 = e2.Knots; var r2 = e2.Controls; int i2 = e2.Dimension;
            if (n2.Length == 0 || r2.Length == 0)
            {
                if (e2.SampleValue.Length > 0) { var t3 = new double[e2.SampleValue.Length]; for (int k = 0; k < t3.Length; k++) t3[k] = e2.SampleValue[k]; return t3; }
                return null;
            }
            int a2 = e2.Degree > 0 ? e2.Degree : 0;
            if (a2 == 0 || n2.Length == 1) return Control(r2, i2, KnotIndex(n2, t2));
            int o2 = KnotIndex(n2, t2), s2 = o2 < n2.Length ? o2 : n2.Length - 1, c2 = 2 * a2, l2 = s2 - a2;
            var u2 = new double[c2]; var d2 = new double[c2][];
            for (int e3 = 0; e3 < c2; e3++)
            {
                int t3 = l2 + e3;
                if (t3 < 0) t3 = 0;
                if (t3 > n2.Length - 1) t3 = n2.Length - 1;
                u2[e3] = n2[t3]; d2[e3] = Control(r2, i2, t3);
            }
            int f2 = a2; double[] p2;
            if (a2 == 1)
            {
                double r = SafeDiv(t2 - u2[f2 - 1], u2[f2] - u2[f2 - 1]), e3 = 1 - r, n3 = r; // [En] l. 1840-1843
                p2 = new double[i2];
                for (int t3 = 0; t3 < i2; t3++) p2[t3] = e3 * d2[f2 - 1][t3] + n3 * d2[f2][t3];
            }
            else if (a2 == 2)
            {
                // [Dn] l. 1844-1847 with (u0, u1, u2, u3, t)
                double k0 = u2[f2 - 2], k1 = u2[f2 - 1], k2 = u2[f2], k3 = u2[f2 + 1];
                double a = SafeDiv(t2 - k1, k2 - k1), o = SafeDiv(t2 - k0, k2 - k0), s = SafeDiv(t2 - k1, k3 - k1);
                double c = o + a - a * o, l = a * s, u = c - l;
                double e3 = 1 - c, n3 = u, r3 = l;
                p2 = new double[i2];
                for (int t3 = 0; t3 < i2; t3++) p2[t3] = e3 * d2[f2 - 2][t3] + n3 * d2[f2 - 1][t3] + r3 * d2[f2][t3];
            }
            else p2 = d2[f2];
            return i2 == 4 ? Renormalize(p2) : p2;
        }

        /// <summary>[jn] l. 1887-1889.</summary>
        private static double[] Fit(double[] e2, int t2, double[] n2)
        {
            if (e2 == null) return n2;
            if (e2.Length == t2) return e2;
            if (t2 == 9 && e2.Length == 3) return new[] { e2[0], 0, 0, 0, e2[1], 0, 0, 0, e2[2] };
            return n2;
        }

        /// <summary>evaluateTransformTrack [Mn] l. 1890-1893.</summary>
        public static GrTransform EvaluateTransformTrack(GrTransformTrack e2, double t2) => new GrTransform
        {
            Position = Fit(SampleCurve(e2?.PositionCurve, t2), 3, new double[] { 0, 0, 0 }),
            Orientation = Fit(SampleCurve(e2?.OrientationCurve, t2), 4, new double[] { 0, 0, 0, 1 }),
            ScaleShear = Fit(SampleCurve(e2?.ScaleShearCurve, t2), 9, new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }),
        };

        /// <summary>evaluateAnimation [Nn] l. 1894-1903: track name → sampled transform (a later track of the same name wins).</summary>
        public static Dictionary<string, GrTransform> EvaluateAnimation(GrAnimation e2, double t2)
        {
            var n2 = new Dictionary<string, GrTransform>();
            if (e2 == null) return n2;
            foreach (var g in e2.TrackGroups ?? new List<GrTrackGroup>())
                foreach (var tr in g.TransformTracks ?? new List<GrTransformTrack>()) n2[tr.Name] = EvaluateTransformTrack(tr, t2);
            return n2;
        }

        /// <summary>The local matrix T·R·S, column-major, in doubles [Bn] l. 1944-1947.</summary>
        public static double[] LocalMatrix(GrTransform e2)
        {
            var n2 = e2?.Position ?? new double[] { 0, 0, 0 }; var r2 = e2?.Orientation ?? new double[] { 0, 0, 0, 1 };
            var i2 = e2?.ScaleShear ?? new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
            double a2 = n2[0], o2 = n2[1], s2 = n2[2], c2 = r2[0], l2 = r2[1], u2 = r2[2], d2 = r2[3];
            double f2 = c2 * c2, p2 = l2 * l2, m2 = u2 * u2, h2 = c2 * l2, g2 = c2 * u2, _2 = l2 * u2, v2 = d2 * c2, y2 = d2 * l2, b2 = d2 * u2;
            double x2 = 1 - 2 * (p2 + m2), S2 = 2 * (h2 - b2), C2 = 2 * (g2 + y2), w2 = 2 * (h2 + b2), T2 = 1 - 2 * (f2 + m2), E2 = 2 * (_2 - v2);
            double D2 = 2 * (g2 - y2), ee2 = 2 * (_2 + v2), O2 = 1 - 2 * (f2 + p2);
            double k2 = i2[0], A2 = i2[1], te2 = i2[2], j2 = i2[3], M2 = i2[4], ne2 = i2[5], N2 = i2[6], P2 = i2[7], F2 = i2[8];
            double re2 = x2 * k2 + S2 * j2 + C2 * N2, ie2 = x2 * A2 + S2 * M2 + C2 * P2, ae2 = x2 * te2 + S2 * ne2 + C2 * F2;
            double oe2 = w2 * k2 + T2 * j2 + E2 * N2, se2 = w2 * A2 + T2 * M2 + E2 * P2, ce2 = w2 * te2 + T2 * ne2 + E2 * F2;
            return new[] { re2, oe2, D2 * k2 + ee2 * j2 + O2 * N2, 0, ie2, se2, D2 * A2 + ee2 * M2 + O2 * P2, 0, ae2, ce2, D2 * te2 + ee2 * ne2 + O2 * F2, 0, a2, o2, s2, 1 };
        }

        /// <summary>multiplyMat4 [In] l. 1909-1916: out = a × b (column-major), in doubles.</summary>
        public static double[] Multiply(double[] e2, double[] t2)
        {
            var r2 = new double[16];
            double i2 = e2[0], a2 = e2[1], o2 = e2[2], s2 = e2[3], c2 = e2[4], l2 = e2[5], u2 = e2[6], d2 = e2[7];
            double f2 = e2[8], p2 = e2[9], m2 = e2[10], h2 = e2[11], g2 = e2[12], _2 = e2[13], v2 = e2[14], y2 = e2[15];
            for (int e3 = 0; e3 < 4; e3++)
            {
                double n3 = t2[e3 * 4 + 0], b2 = t2[e3 * 4 + 1], x2 = t2[e3 * 4 + 2], S2 = t2[e3 * 4 + 3];
                r2[e3 * 4 + 0] = i2 * n3 + c2 * b2 + f2 * x2 + g2 * S2;
                r2[e3 * 4 + 1] = a2 * n3 + l2 * b2 + p2 * x2 + _2 * S2;
                r2[e3 * 4 + 2] = o2 * n3 + u2 * b2 + m2 * x2 + v2 * S2;
                r2[e3 * 4 + 3] = s2 * n3 + d2 * b2 + h2 * x2 + y2 * S2;
            }
            return r2;
        }

        private static float[] ToFloat(double[] d) { var f = new float[16]; for (int i = 0; i < 16; i++) f[i] = (float)d[i]; return f; }
        private static double[] ToDouble(float[] f) { var d = new double[16]; for (int i = 0; i < 16; i++) d[i] = f[i]; return d; }

        /// <summary>poseSkeletonAt [zn] l. 1935-1943 (composeWorldPose [Ln] l. 1917-1926, composeSkinningMatrices [Rn] l. 1927-1934):
        /// animation null = the bind pose.</summary>
        public static GrPose PoseSkeletonAt(GrSkeleton e2, GrAnimation t2, double n2)
        {
            var r2 = e2.Bones; int i2 = r2.Count;
            var a2 = t2 != null ? EvaluateAnimation(t2, n2) : null;
            var o2 = new GrTransform[i2]; var s2 = new double[i2][];
            for (int e3 = 0; e3 < i2; e3++)
            {
                var t3 = r2[e3];
                GrTransform n3 = a2 != null && a2.TryGetValue(t3.Name, out var ev) ? ev : t3.Transform;
                o2[e3] = n3; s2[e3] = LocalMatrix(n3);
            }
            // composeWorldPose: the cascade in doubles, the result rounded to float
            var w = new double[i2][]; var world = new float[i2][];
            for (int e3 = 0; e3 < i2; e3++)
            {
                int p = r2[e3].ParentIndex;
                w[e3] = p < 0 || p >= e3 ? (double[])s2[e3].Clone() : Multiply(w[p], s2[e3]);
            }
            for (int e3 = 0; e3 < i2; e3++) world[e3] = ToFloat(w[e3]);
            // composeSkinningMatrices: world (as rounded) × inverse world transform (identity when not 16 floats)
            var skin = new float[i2][];
            for (int e3 = 0; e3 < i2; e3++)
            {
                var iwt = r2[e3].InverseWorldTransform;
                double[] r3 = iwt != null && iwt.Length == 16 ? (double[])iwt.Clone() : (double[])IdentityMat.Clone();
                skin[e3] = ToFloat(Multiply(ToDouble(world[e3]), r3));
            }
            return new GrPose { LocalTransforms = o2, WorldMatrices = world, SkinningMatrices = skin };
        }

        /// <summary>poseAt [Qn] l. 1994-1998: the first skeleton against animations[index] (out of range: the bind pose).</summary>
        public static GrPose PoseAt(GrParsed e2, int t2, double n2)
        {
            var r2 = e2.Skeletons != null && e2.Skeletons.Count > 0 ? e2.Skeletons[0] : null;
            if (r2 == null) throw new GrannyException("poseAt: parsed.skeletons[0] is missing");
            var anims = e2.Animations ?? new List<GrAnimation>();
            return PoseSkeletonAt(r2, t2 >= 0 && t2 < anims.Count ? anims[t2] : null, n2);
        }
    }
}
