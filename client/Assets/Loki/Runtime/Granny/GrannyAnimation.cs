// =====================================================================================================================
// Project Loki, client wave 4b (w4granny): a C# port, step for step, of granny-ro-js 1.5.0 (npm, MIT;
// https://github.com/stitchuuuu/granny-ro-js), the Granny3D (.gr2) decoder roBrowserLegacy uses (src/Loaders/GR2Loader.js).
// Source read: package/dist/granny-ro.esm.js (a minified bundle). Comments name the package's exported functions and, in
// brackets, the bundle's own minified names; "l. N" are the lines of that bundle reformatted by esbuild
// (client/tools/w4granny/README.md). The JavaScript number rules are kept where they decide a result: curve values are
// Float32Array stores (rounded to float) of expressions computed in double.
//
// This file: animations (extractAnimations [Cn]: animations, track groups, transform tracks) and their curves ([gn]:
// the legacy Curve32f layout [hn], the packed codecs D3K16uC16u / D3I1K16uC16u [dn], D4nK8uC7u [fn], D4nK16uC15u [pn],
// the constants / identity [mn]). The RO client's banks use only LegacyCurve32f (degree 0 and 2); the other codecs are
// ported for completeness and are not exercised by its files.
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
using static Loki.Granny.GrannyTypeTree;

namespace Loki.Granny
{
    public sealed class GrCurve
    {
        public string Codec;
        public int Format, Degree, Dimension;
        public long KnotControlCount;
        public float[] SampleValue, Knots, Controls;
    }

    public sealed class GrTransformTrack
    {
        public int Index, Flags;
        public string Name;
        public GrCurve OrientationCurve, PositionCurve, ScaleShearCurve;
    }

    public sealed class GrTrackGroup
    {
        public int Index;
        public string Name;
        public long VectorTrackCount, TransformTrackCount, TextTrackCount;
        public double AccumulationFlags, LoopTranslation;
        public List<string> VectorTrackNames;
        public List<GrTransformTrack> TransformTracks;
    }

    public sealed class GrAnimation
    {
        public int Index;
        public string Name;
        public double Duration, TimeStep, Oversampling, DefaultLoopCount, Flags;
        public List<string> TrackGroupNames;
        public List<GrTrackGroup> TrackGroups;
    }

    public static class GrannyAnimation
    {
        private static readonly float[] Empty = new float[0]; // [Z] l. 1575
        private const double Q = 0.707106781;
        // the scale / offset table of the packed quaternion codecs [tn] l. 1575
        private static readonly double[][] ScaleOffsetTable =
        {
            new[] { Q * 2, -0.707106781 }, new[] { Q * 1, -0.707106781 * 0.5 }, new[] { Q * 0.5, -0.707106781 * 0.75 },
            new[] { Q * 0.5, -0.707106781 * 0.25 }, new[] { Q * 0.5, Q * 0.25 }, new[] { Q * 0.25, -0.707106781 * 0.25 },
            new[] { Q * 0.25, -0.707106781 * 0.125 }, new[] { Q * 0.25, Q * 0 }, new[] { -0.707106781 * 2, Q },
            new[] { -0.707106781 * 1, Q * 0.5 }, new[] { -0.707106781 * 0.5, Q * 0.75 }, new[] { -0.707106781 * 0.5, Q * 0.25 },
            new[] { -0.707106781 * 0.5, -0.707106781 * 0.25 }, new[] { -0.707106781 * 0.25, Q * 0.25 },
            new[] { -0.707106781 * 0.25, Q * 0.125 }, new[] { -0.707106781 * 0.25, -0.0 },
        };

        /// <summary>readPointer, always little-endian [nn] l. 1577-1582 (DataView: out of range throws).</summary>
        private static double Ptr(byte[] e2, int t2, int n2)
        {
            if (n2 == 4) return Js.U32(e2, t2, true);
            double i2 = Js.U32(e2, t2, true);
            return Js.U32(e2, t2 + 4, true) * 4294967296.0 + i2;
        }

        /// <summary>[an] l. 1587-1597: member name → byte offset in the struct (inline members sized with objectStorageSize).</summary>
        private static Dictionary<string, long> MemberOffsets(GrLoaded e2, List<GrMember> t2)
        {
            int n2 = e2.PointerSize; var r2 = new Dictionary<string, long>(); long i2 = 0;
            foreach (var o2 in t2)
            {
                r2[o2.Name] = i2;
                if (o2.MemberType == 1 && o2.ReferenceType != null) i2 += ObjectStorageSize(e2, ParseTypeTree(e2, o2.ReferenceType), n2, new HashSet<long>());
                else i2 += MemberSize(o2, n2);
            }
            return r2;
        }

        /// <summary>[on] l. 1598-1601: a u16 as the high half of a float.</summary>
        private static float HalfHigh(ushort e2) => BitConverter.Int32BitsToSingle((e2 & 65535) << 16);

        /// <summary>[sn] l. 1602-1608: the dimension a "D&lt;n&gt;…" codec name gives (0 otherwise).</summary>
        private static int DimensionOf(string e2)
        {
            if (e2.Length >= 2 && e2[0] == 'D') { int t2 = e2[1] - 48; if (t2 >= 0 && t2 <= 9) return t2; }
            return 0;
        }

        /// <summary>[cn] l. 1609-1618: four scale / offset pairs selected by the nibbles of a u16 (Float32Array stores).</summary>
        private static void ScalesOffsets(uint e2, int t2, out float[] scales, out float[] offsets)
        {
            scales = new float[4]; offsets = new float[4]; uint i2 = e2;
            for (int e3 = 0; e3 < 4; e3++)
            {
                uint a2 = i2 & 15; i2 >>= 4;
                var o2 = ScaleOffsetTable[a2];
                scales[e3] = (float)(o2[0] / t2); offsets[e3] = (float)o2[1];
            }
        }

        private static readonly (float[] knots, float[] controls) None = (Empty, Empty);

        /// <summary>[ln] l. 1619-1622: the KnotsControls array pointer (4 bytes after its count).</summary>
        private static GrRef KnotsPointer(GrLoaded e2, byte[] t2, long n2)
        {
            int r2 = e2.PointerSize;
            return n2 + 4 + r2 > t2.Length ? null : DecodeFakePointer(Ptr(t2, (int)n2 + 4, r2), e2.SectionsFixed.Length);
        }

        /// <summary>[un] l. 1623-1629: knots and controls of a packed curve.</summary>
        private static (float[] knots, float[] controls) KnotsControls(GrLoaded e2, string t2, int n2, byte[] r2, byte[] i2, int a2, Dictionary<string, long> o2, long s2)
        {
            if (s2 <= 0 || n2 <= 0) return None;
            if (!o2.TryGetValue("KnotsControls", out long c2)) return None;
            var l2 = KnotsPointer(e2, i2, a2 + c2);
            if (l2 == null) return None;
            if (t2 == "D3K16uC16u" || t2 == "D3I1K16uC16u") return D3K16(e2, t2, n2, r2, a2, o2, s2, l2);
            if (t2 == "D4nK8uC7u") return D4nK8(e2, r2, a2, o2, s2, l2);
            if (t2 == "D4nK16uC15u") return D4nK16(e2, r2, a2, o2, s2, l2);
            return None;
        }

        private static long Off(Dictionary<string, long> d, string k, long def) => d.TryGetValue(k, out long v) ? v : def;

        /// <summary>[dn] l. 1630-1656.</summary>
        private static (float[], float[]) D3K16(GrLoaded e2, string t2, int n2, byte[] r2, int i2, Dictionary<string, long> a2, long o2, GrRef s2)
        {
            long c2 = Js.ToInt32(Math.Truncate((double)o2 / (t2 == "D3I1K16uC16u" ? 2 : n2 + 1))); // `o2 / (…) | 0`
            if (c2 <= 0) return None;
            long l2 = i2 + Off(a2, "OneOverKnotScaleTrunc", 2), u2 = i2 + Off(a2, "ControlScales", 4), d2 = i2 + Off(a2, "ControlOffsets", 16);
            if (d2 + n2 * 4 > r2.Length) return None;
            float p2 = HalfHigh(U16(r2, l2));
            if (p2 == 0) return None;
            var m2 = new float[n2]; var h2 = new float[n2];
            for (int e3 = 0; e3 < n2; e3++) { m2[e3] = F32(r2, u2 + e3 * 4); h2[e3] = F32(r2, d2 + e3 * 4); }
            var g2 = e2.SectionsFixed[s2.Section]; long _2 = o2 * 2;
            if (s2.Offset + _2 > g2.Length) return None;
            double y2 = 1 / (double)p2;
            var b2 = new float[c2];
            for (int e3 = 0; e3 < c2; e3++) b2[e3] = (float)(y2 * Js.U16(g2, s2.Offset + e3 * 2));
            var x2 = new float[c2 * n2];
            if (t2 == "D3I1K16uC16u")
                for (int e3 = 0; e3 < c2; e3++)
                {
                    int t3 = Js.U16(g2, s2.Offset + (int)((c2 + e3) * 2)); long r3 = (long)e3 * n2;
                    for (int e4 = 0; e4 < n2; e4++) x2[r3 + e4] = (float)(h2[e4] + (double)m2[e4] * t3);
                }
            else
                for (int e3 = 0; e3 < c2; e3++)
                {
                    long t3 = (long)e3 * n2, r3 = (c2 + (long)e3 * n2) * 2;
                    for (int e4 = 0; e4 < n2; e4++)
                    {
                        int n3 = Js.U16(g2, s2.Offset + (int)(r3 + e4 * 2));
                        x2[t3 + e4] = (float)(h2[e4] + (double)m2[e4] * n3);
                    }
                }
            return (b2, x2);
        }

        /// <summary>[fn] l. 1657-1682: quaternions, 8-bit knots and 3 x 7-bit components (the fourth rebuilt).</summary>
        private static (float[], float[]) D4nK8(GrLoaded e2, byte[] t2, int n2, Dictionary<string, long> r2, long i2, GrRef a2)
        {
            long o2 = i2 / 4;
            if (o2 <= 0) return None;
            long s2 = n2 + Off(r2, "ScaleOffsetTableEntries", 2), c2 = n2 + Off(r2, "OneOverKnotScale", 4);
            if (c2 + 4 > t2.Length) return None;
            float u2 = F32(t2, c2);
            if (u2 == 0) return None;
            var d2 = e2.SectionsFixed[a2.Section];
            if (a2.Offset + i2 > d2.Length) return None;
            double f2 = 1 / (double)u2;
            var p2 = new float[o2];
            for (int e3 = 0; e3 < o2; e3++) p2[e3] = (float)(f2 * d2[a2.Offset + e3]);
            ScalesOffsets(U16(t2, s2), 127, out var m2, out var h2);
            var g2 = new float[o2 * 4]; long _2 = a2.Offset + o2;
            for (int e3 = 0; e3 < o2; e3++)
            {
                int t3 = d2[_2 + e3 * 3], n3 = d2[_2 + e3 * 3 + 1], r3 = d2[_2 + e3 * 3 + 2];
                bool i3 = (t3 & 128) != 0; int a3 = ((n3 >> 6 & 2) | r3 >> 7) & 3;
                var o3 = new double[4]; int s3 = a3; double c3 = 0; int[] l3 = { t3, n3, r3 };
                for (int e4 = 0; e4 < 3; e4++)
                {
                    s3 = s3 + 1 & 3;
                    double t4 = h2[s3] + (double)m2[s3] * (l3[e4] & 127);
                    o3[s3] = t4; c3 += t4 * t4;
                }
                double u3 = Math.Sqrt(Math.Max(0, 1 - c3));
                o3[a3] = i3 ? -u3 : u3;
                long f3 = e3 * 4;
                g2[f3] = (float)o3[0]; g2[f3 + 1] = (float)o3[1]; g2[f3 + 2] = (float)o3[2]; g2[f3 + 3] = (float)o3[3];
            }
            return (p2, g2);
        }

        /// <summary>[pn] l. 1683-1708: quaternions, 16-bit knots and 3 x 15-bit components.</summary>
        private static (float[], float[]) D4nK16(GrLoaded e2, byte[] t2, int n2, Dictionary<string, long> r2, long i2, GrRef a2)
        {
            long o2 = i2 / 4;
            if (o2 <= 0) return None;
            long s2 = n2 + Off(r2, "ScaleOffsetTableEntries", 2), c2 = n2 + Off(r2, "OneOverKnotScale", 4);
            if (c2 + 4 > t2.Length) return None;
            float u2 = F32(t2, c2);
            if (u2 == 0) return None;
            var d2 = e2.SectionsFixed[a2.Section]; long f2 = i2 * 2;
            if (a2.Offset + f2 > d2.Length) return None;
            int p0 = a2.Offset; double m2 = 1 / (double)u2;
            var h2 = new float[o2];
            for (int e3 = 0; e3 < o2; e3++) h2[e3] = (float)(m2 * Js.U16(d2, p0 + e3 * 2));
            ScalesOffsets(U16(t2, s2), 32767, out var g2, out var _2);
            var v2 = new float[o2 * 4]; long y2 = o2;
            for (int e3 = 0; e3 < o2; e3++)
            {
                int t3 = Js.U16(d2, p0 + (int)((y2 + e3 * 3 + 0) * 2)), n3 = Js.U16(d2, p0 + (int)((y2 + e3 * 3 + 1) * 2)), r3 = Js.U16(d2, p0 + (int)((y2 + e3 * 3 + 2) * 2));
                bool i3 = (t3 & 32768) != 0; int a3 = ((n3 >> 14 & 2) | r3 >> 15) & 3;
                var o3 = new double[4]; int s3 = a3; double c3 = 0; int[] l3 = { t3, n3, r3 };
                for (int e4 = 0; e4 < 3; e4++)
                {
                    s3 = s3 + 1 & 3;
                    double t4 = _2[s3] + (double)g2[s3] * (l3[e4] & 32767);
                    o3[s3] = t4; c3 += t4 * t4;
                }
                double u3 = Math.Sqrt(Math.Max(0, 1 - c3));
                o3[a3] = i3 ? -u3 : u3;
                long d3 = e3 * 4;
                v2[d3] = (float)o3[0]; v2[d3 + 1] = (float)o3[1]; v2[d3 + 2] = (float)o3[2]; v2[d3 + 3] = (float)o3[3];
            }
            return (h2, v2);
        }

        private static ushort U16(byte[] b, long o) { if (o < 0 || o + 2 > b.Length) throw new GrannyException("Offset is outside the bounds of the DataView"); return Js.U16(b, (int)o); }
        private static float F32(byte[] b, long o) { if (o < 0 || o + 4 > b.Length) throw new GrannyException("Offset is outside the bounds of the DataView"); return Js.F32(b, (int)o); }

        /// <summary>[mn] l. 1709-1719: the value of a constant curve, or the identity.</summary>
        private static float[] SampleValue(string e2, int t2, byte[] n2, int r2)
        {
            if (t2 <= 0) return Empty;
            if (e2.EndsWith("Constant32f", StringComparison.Ordinal))
            {
                long e3 = r2 + 4;
                if (e3 + t2 * 4 > n2.Length) return Empty;
                var a2 = new float[t2];
                for (int n3 = 0; n3 < t2; n3++) a2[n3] = Js.F32(n2, (int)e3 + n3 * 4);
                return a2;
            }
            if (e2 == "DaIdentity")
                return t2 == 9 ? new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 } : t2 == 4 ? new float[] { 0, 0, 0, 1 } : new float[t2];
            return Empty;
        }

        /// <summary>[hn] l. 1720-1739: the old Curve layout — int32 degree, u32 knot count, knots pointer, u32 control count,
        /// controls pointer — read when the bytes look like one (else null: a variant curve).</summary>
        private static GrCurve LegacyCurve(GrLoaded e2, int t2, int n2)
        {
            int r2 = e2.PointerSize;
            var i2 = t2 < e2.SectionsOriginal.Length ? e2.SectionsOriginal[t2] : null; var a2 = e2.SectionsFixed[t2];
            if (i2 == null || n2 + 4 + (4 + r2) * 2 > i2.Length) return null;
            int s2 = unchecked((int)Js.U32(i2, n2));
            if (s2 < 0 || s2 > 3) return null;
            uint c2 = Js.U32(i2, n2 + 4), l2 = Js.U32(i2, n2 + 4 + r2 + 4);
            if ((c2 == 0 && l2 == 0) || c2 <= 0 || l2 <= 0 || l2 % c2 != 0) return null;
            uint u2 = l2 / c2;
            if (u2 <= 0 || u2 > 16) return null;
            var d2 = DecodeFakePointer(Ptr(a2, n2 + 4 + 4, r2), e2.SectionsFixed.Length);
            var f2 = DecodeFakePointer(Ptr(a2, n2 + 4 + r2 + 4 + 4, r2), e2.SectionsFixed.Length);
            if (d2 == null || f2 == null) return null;
            var p2 = e2.SectionsFixed[d2.Section]; var m2 = e2.SectionsFixed[f2.Section];
            if (d2.Offset + (long)c2 * 4 > p2.Length || f2.Offset + (long)l2 * 4 > m2.Length) return null;
            var _2 = new float[c2];
            for (int e3 = 0; e3 < c2; e3++) _2[e3] = Js.F32(p2, d2.Offset + e3 * 4);
            var v2 = new float[l2];
            for (int e3 = 0; e3 < l2; e3++) v2[e3] = Js.F32(m2, f2.Offset + e3 * 4);
            float[] y2 = Empty;
            if (c2 == 1) { y2 = new float[u2]; Array.Copy(v2, y2, (int)Math.Min(u2, (uint)v2.Length)); }
            return new GrCurve { Codec = "LegacyCurve32f", Format = -1, Degree = s2, Dimension = (int)u2, KnotControlCount = (long)c2 + l2, SampleValue = y2, Knots = _2, Controls = v2 };
        }

        /// <summary>[gn] l. 1740-1760: the curve stored at (section, offset).</summary>
        private static GrCurve Curve(GrLoaded e2, int t2, int n2)
        {
            int r2 = e2.PointerSize; var i2 = e2.SectionsFixed[t2];
            if (i2 == null || n2 + r2 * 2 > i2.Length) return null;
            var a2 = LegacyCurve(e2, t2, n2);
            if (a2 != null) return a2;
            var o2 = DecodeFakePointer(Ptr(i2, n2, r2), e2.SectionsFixed.Length);
            var s2 = DecodeFakePointer(Ptr(i2, n2 + r2, r2), e2.SectionsFixed.Length);
            if (o2 == null || s2 == null) return null;
            var c2 = ParseTypeTree(e2, o2);
            if (c2.Count == 0) return null;
            string l2 = c2[0].Name.StartsWith("CurveDataHeader_", StringComparison.Ordinal) ? c2[0].Name.Substring("CurveDataHeader_".Length) : c2[0].Name;
            var u2 = MemberOffsets(e2, c2);
            var d2 = e2.SectionsOriginal[s2.Section];
            if (s2.Offset + 2 > d2.Length) return null;
            int p2 = d2[s2.Offset], m2 = d2[s2.Offset + 1], h2 = DimensionOf(l2);
            if (l2 == "DaIdentity")
            {
                long e3 = s2.Offset + Off(u2, "Dimension", 2);
                if (e3 + 2 <= d2.Length) h2 = unchecked((short)Js.U16(d2, (int)e3));
            }
            long g2 = 0;
            if (u2.TryGetValue("KnotsControls", out long _2) && s2.Offset + _2 + 4 <= d2.Length) g2 = Js.U32(d2, (int)(s2.Offset + _2));
            var kc = KnotsControls(e2, l2, h2, d2, e2.SectionsFixed[s2.Section], s2.Offset, u2, g2);
            return new GrCurve { Codec = l2, Format = p2, Degree = m2, Dimension = h2, KnotControlCount = g2, SampleValue = SampleValue(l2, h2, d2, s2.Offset), Knots = kc.knots, Controls = kc.controls };
        }

        /// <summary>[_n] l. 1761-1766.</summary>
        private static GrTransformTrack TransformTrack(GrLoaded e2, int t2, GrObjectRecord n2)
        {
            var r2 = n2.Ref; var i2 = n2.Fields;
            var o2 = F(i2, "OrientationCurve"); var s2 = F(i2, "PositionCurve"); var c2 = F(i2, "ScaleShearCurve");
            int l2 = r2.Offset + 4; var u2 = F(i2, "Flags");
            if (u2 != null) l2 = u2.Offset;
            int d2 = 0; var f2 = e2.SectionsOriginal[r2.Section];
            if (f2 != null && l2 + 4 <= f2.Length) d2 = unchecked((int)Js.U32(f2, l2));
            return new GrTransformTrack
            {
                Index = t2, Name = NameOr(i2, "Name", "TransformTrack_" + t2), Flags = d2,
                OrientationCurve = o2 != null ? Curve(e2, r2.Section, o2.Offset) : null,
                PositionCurve = s2 != null ? Curve(e2, r2.Section, s2.Offset) : null,
                ScaleShearCurve = c2 != null ? Curve(e2, r2.Section, c2.Offset) : null,
            };
        }

        /// <summary>[bn] l. 1784-1787 (with [vn] l. 1767-1773 and [yn] l. 1774-1783).</summary>
        private static GrTrackGroup TrackGroup(GrLoaded e2, int t2, GrRef n2, List<GrMember> r2, int i2)
        {
            var a2 = ParseObject(e2, r2, n2, i2);
            var tt = F(a2, "TransformTracks");
            var o2 = new List<GrTransformTrack>();
            if (tt != null && tt.Target != null && tt.ReferenceType != null && CountOr0(tt) > 0)
            {
                var recs = ReadReferenceArrayObjects(e2, tt.Target, CountOr0(tt), tt.ReferenceType, i2, 64);
                for (int k = 0; k < recs.Count; k++) o2.Add(TransformTrack(e2, k, recs[k]));
            }
            var vt = F(a2, "VectorTracks");
            var names = new List<string>();
            if (vt != null && vt.Target != null && vt.ReferenceType != null && CountOr0(vt) > 0)
            {
                var recs = ReadReferenceArrayObjects(e2, vt.Target, CountOr0(vt), vt.ReferenceType, i2, 64);
                for (int k = 0; k < recs.Count; k++) names.Add(NameOr(recs[k].Fields, "Name", "Track_" + k));
            }
            return new GrTrackGroup
            {
                Index = t2, Name = NameOr(a2, "Name", "TrackGroup_" + t2), VectorTrackCount = CountOr0(vt), TransformTrackCount = CountOr0(tt),
                TextTrackCount = CountOr0(F(a2, "TextTracks")), AccumulationFlags = NumOr(a2, "AccumulationFlags", 0),
                LoopTranslation = NumOr(a2, "LoopTranslation", 0), VectorTrackNames = names, TransformTracks = o2,
            };
        }

        /// <summary>[xn] l. 1788-1802: the names of an animation's track groups.</summary>
        private static List<string> TrackGroupNames(GrLoaded e2, GrField t2, int n2)
        {
            var s2 = new List<string>();
            if (t2 == null || t2.Target == null || CountOr0(t2) <= 0) return s2;
            var o2 = t2.ElementRefs ?? new List<GrRef>();
            if (t2.ReferenceType == null) { for (int e3 = 0; e3 < o2.Count; e3++) s2.Add("TrackGroup_" + e3); return s2; }
            var c2 = ParseTypeTree(e2, t2.ReferenceType);
            int l2 = Math.Min(o2.Count, n2);
            for (int t3 = 0; t3 < l2; t3++) s2.Add(NameOr(ParseObject(e2, c2, o2[t3], 0), "Name", "TrackGroup_" + t3));
            return s2;
        }

        /// <summary>[Sn] l. 1803-1810.</summary>
        private static GrAnimation Animation(GrLoaded e2, int t2, GrRef n2, List<GrMember> r2, Dictionary<string, GrTrackGroup> i2)
        {
            var a2 = ParseObject(e2, r2, n2, 64);
            var s2 = TrackGroupNames(e2, F(a2, "TrackGroups"), 64);
            var c2 = new List<GrTrackGroup>();
            foreach (var nm in s2) if (i2.TryGetValue(nm, out var g)) c2.Add(g);
            return new GrAnimation
            {
                Index = t2, Name = NameOr(a2, "Name", "Animation_" + t2), Duration = NumOr(a2, "Duration", 0), TimeStep = NumOr(a2, "TimeStep", 0),
                Oversampling = NumOr(a2, "Oversampling", 0), DefaultLoopCount = NumOr(a2, "DefaultLoopCount", 0), Flags = NumOr(a2, "Flags", 0),
                TrackGroupNames = s2, TrackGroups = c2,
            };
        }

        /// <summary>extractAnimations [Cn] l. 1811-1831 (maxTrackGroups 64, maxTracksPerGroup 512, maxAnimations 64).</summary>
        public static List<GrAnimation> ExtractAnimations(GrLoaded e2, int maxTrackGroups = 64, int maxTracksPerGroup = 512, int maxAnimations = 64)
        {
            var root = ParseObject(e2, ParseTypeTree(e2, e2.File.Header.RootType),
                new GrRef((int)e2.File.Header.RootObject[0], (int)e2.File.Header.RootObject[1]), Math.Max(maxTrackGroups, maxAnimations));
            var c2 = new List<GrTrackGroup>();
            var s2 = F(root, "TrackGroups");
            if (s2 != null && s2.ReferenceType != null && s2.ElementRefs != null && s2.ElementRefs.Count > 0)
            {
                var a3 = ParseTypeTree(e2, s2.ReferenceType);
                int o3 = Math.Min(s2.ElementRefs.Count, maxTrackGroups);
                for (int t4 = 0; t4 < o3; t4++) c2.Add(TrackGroup(e2, t4, s2.ElementRefs[t4], a3, maxTracksPerGroup));
            }
            var u2 = new List<GrAnimation>();
            var l2 = F(root, "Animations");
            if (l2 != null && l2.ReferenceType != null && l2.ElementRefs != null && l2.ElementRefs.Count > 0)
            {
                var r3 = ParseTypeTree(e2, l2.ReferenceType);
                var a3 = new Dictionary<string, GrTrackGroup>();
                foreach (var g in c2) a3[g.Name] = g;
                int o3 = Math.Min(l2.ElementRefs.Count, maxAnimations);
                for (int t4 = 0; t4 < o3; t4++) u2.Add(Animation(e2, t4, l2.ElementRefs[t4], r3, a3));
            }
            return u2;
        }
    }
}
