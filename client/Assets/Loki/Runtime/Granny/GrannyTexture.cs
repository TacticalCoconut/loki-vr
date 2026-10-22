// =====================================================================================================================
// Project Loki, client wave 4b (w4granny): a C# port, step for step, of granny-ro-js 1.5.0 (npm, MIT;
// https://github.com/stitchuuuu/granny-ro-js), the Granny3D (.gr2) decoder roBrowserLegacy uses (src/Loaders/GR2Loader.js).
// Source read: package/dist/granny-ro.esm.js (a minified bundle; its pure-JS texture decoder, not the optional WASM
// build). Comments name the package's exported functions and, in brackets, the bundle's own minified names; "l. N" are
// the lines of that bundle reformatted by esbuild (client/tools/w4granny/README.md).
// JavaScript semantics kept: the coefficient planes are Int16Array (every store wraps to 16 bits), the filter scratch
// rows Int32Array, sums that the package keeps as doubles are longs here (exact: they stay far below 2^53), `| 0` and
// `>>> 0` are applied where the package applies them.
//
// This file: the texture walk (walkTextureImages [Zt], extractTextures [qt]), raw BGRA textures [Yt] and the
// wavelet-compressed ("IGC", Bink-family) textures (decodeIGCTexture [Gt]: the adaptive arithmetic decoder, the band
// decoders, the inverse wavelet, YCoCg-style colour conversion).
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
    /// <summary>One decoded texture image (RGBA, top row first as stored).</summary>
    public sealed class GrTexture
    {
        public int TexIdx, ImgIdx, MipIdx, Width, Height, Encoding, SubFormat, Alpha;
        public string Name, FromFileName;
        public byte[] Pixels;
    }

    public static class GrannyTexture
    {
        private sealed class WalkRecord
        {
            public int TexIdx, ImgIdx, MipIdx, Width, Height, Encoding, SubFormat, Alpha;
            public string FromFileName; public byte[] PixelBytes; public long PixelCount;
        }

        /// <summary>extractTextures [qt] l. 1509-1516 (maxTextures 256, maxImages 8, maxMips 32). An unsupported or
        /// undecodable image throws, and the whole call with it, as in the package.</summary>
        public static List<GrTexture> ExtractTextures(GrLoaded e2, int maxTextures = 256, int maxImages = 8, int maxMips = 32)
        {
            var r2 = new List<GrTexture>();
            foreach (var t3 in Walk(e2, maxTextures, maxImages, maxMips))
            {
                var a2 = Decode(t3);
                if (a2 == null) continue;
                r2.Add(new GrTexture
                {
                    TexIdx = t3.TexIdx, ImgIdx = t3.ImgIdx, MipIdx = t3.MipIdx, Name = !string.IsNullOrEmpty(t3.FromFileName) ? t3.FromFileName : "tex" + t3.TexIdx,
                    FromFileName = t3.FromFileName, Width = t3.Width, Height = t3.Height, Encoding = t3.Encoding, SubFormat = t3.SubFormat, Alpha = t3.Alpha, Pixels = a2,
                });
            }
            return r2;
        }

        /// <summary>[Jt] l. 1517-1522.</summary>
        private static byte[] Decode(WalkRecord e2)
        {
            if (e2.Encoding == 1) return Raw(e2.PixelBytes, e2.Width, e2.Height);
            if (e2.Encoding == 3) return DecodeIgc(e2.Width, e2.Height, e2.Alpha, e2.PixelBytes);
            throw new GrannyException(e2.Encoding == 2
                ? $"extractTextures: S3TC textures (encoding=2) not yet supported in granny-ro-js — file an issue if an iRO asset hits this. Texture: tex{e2.TexIdx}-img{e2.ImgIdx}-mip{e2.MipIdx}"
                : $"extractTextures: unknown encoding={e2.Encoding} for tex{e2.TexIdx}-img{e2.ImgIdx}-mip{e2.MipIdx}");
        }

        /// <summary>[Yt] l. 1523-1529: raw BGRA → RGBA.</summary>
        private static byte[] Raw(byte[] e2, int t2, int n2)
        {
            long r2 = (long)t2 * n2 * 4;
            if (e2 == null || e2.Length != r2) throw new GrannyException($"extractTextures: Raw texture byte count {(e2 == null ? "undefined" : e2.Length.ToString())} != W*H*4 ({r2})");
            var i2 = new byte[r2];
            for (int t3 = 0; t3 < r2; t3 += 4) { i2[t3] = e2[t3 + 2]; i2[t3 + 1] = e2[t3 + 1]; i2[t3 + 2] = e2[t3]; i2[t3 + 3] = e2[t3 + 3]; }
            return i2;
        }

        /// <summary>walkTextureImages [Zt] l. 1533-1556.</summary>
        private static List<WalkRecord> Walk(GrLoaded e2, int n2, int r2, int i2)
        {
            var u2 = new List<WalkRecord>();
            var root = ParseObject(e2, ParseTypeTree(e2, e2.File.Header.RootType), new GrRef((int)e2.File.Header.RootObject[0], (int)e2.File.Header.RootObject[1]), n2);
            var o2 = F(root, "Textures");
            if (o2 == null || o2.ReferenceType == null) return u2;
            var c2 = o2.ElementRefs ?? new List<GrRef>();
            if (c2.Count == 0) return u2;
            var l2 = ParseTypeTree(e2, o2.ReferenceType);
            int d2 = Math.Min(c2.Count, n2);
            for (int t3 = 0; t3 < d2; t3++)
            {
                var n3 = c2[t3];
                if (n3 == null) continue;
                var a3 = ParseObject(e2, l2, n3, 64);
                int o3 = (int)NumOr(a3, "Width", 0), s3 = (int)NumOr(a3, "Height", 0), d3 = (int)NumOr(a3, "Encoding", 0), f2 = (int)NumOr(a3, "SubFormat", 0);
                var ffn = F(a3, "FromFileName"); string p2 = ffn != null && ffn.Str != null ? ffn.Str : "";
                int m2 = AlphaOf(e2, l2, a3, n3);
                var h2 = F(a3, "Images"); var g2 = h2?.ReferenceType;
                var _2 = g2 != null ? ReadReferenceArrayObjects(e2, h2.Target, CountOr0(h2), g2, r2) : new List<GrObjectRecord>();
                for (int n4 = 0; n4 < _2.Count; n4++)
                {
                    var r3 = F(_2[n4].Fields, "MIPLevels"); var a4 = r3?.ReferenceType;
                    var c3 = a4 != null ? ReadReferenceArrayObjects(e2, r3.Target, CountOr0(r3), a4, i2) : new List<GrObjectRecord>();
                    for (int r4 = 0; r4 < c3.Count; r4++)
                    {
                        var i3 = c3[r4].Fields;
                        var a5 = F(i3, "Pixels") ?? F(i3, "PixelBytes");
                        long l3 = CountOr0(a5); var h3 = a5?.Target; byte[] g3 = null;
                        if (h3 != null && l3 > 0) g3 = Slice(e2, h3.Section, h3.Offset, l3);
                        u2.Add(new WalkRecord { TexIdx = t3, ImgIdx = n4, MipIdx = r4, Width = o3, Height = s3, Encoding = d3, SubFormat = f2, Alpha = m2, FromFileName = p2, PixelBytes = g3, PixelCount = l3 });
                    }
                }
            }
            return u2;
        }

        /// <summary>[Qt] l. 1557-1574: 0 when the texture's Layout says 3 bytes per pixel, else 1. The package reads the Layout
        /// at the texture's offset + the field's (already absolute) offset; kept as is (every RO texture here comes out 1).</summary>
        private static int AlphaOf(GrLoaded e2, List<GrMember> t2, Dictionary<string, GrField> n2, GrRef r2)
        {
            try
            {
                var i2 = F(n2, "Layout");
                if (i2 == null || i2.Type != "inline") return 1;
                GrMember a2 = null;
                foreach (var m in t2) if (m.Name == "Layout") { a2 = m; break; }
                if (a2 == null || a2.ReferenceType == null) return 1;
                int o2 = a2.ReferenceType.Section;
                if (o2 >= e2.SectionsOriginal.Length || e2.SectionsOriginal[o2] == null) return 1;
                var s2 = ParseTypeTree(e2, new GrRef(o2, a2.ReferenceType.Offset));
                int c2 = i2.Offset;
                var bpp = F(ParseObject(e2, s2, new GrRef(r2.Section, r2.Offset + c2)), "BytesPerPixel");
                return bpp != null && bpp.HasNum && bpp.Num == 3 ? 0 : 1;
            }
            catch (Exception) { return 1; }
        }

        // ============================================================================================ decodeIGCTexture
        /// <summary>decodeIGCTexture [Gt] l. 1495-1506.</summary>
        public static byte[] DecodeIgc(int t2, int n2, int r2, byte[] i2)
        {
            if (i2 == null) throw new GrannyException("Cannot read properties of null (reading 'byteLength')");
            if ((long)t2 * n2 <= 256)
            {
                long e3 = (long)t2 * n2 * 4;
                if (i2.Length < e3) throw new GrannyException($"decodeIGCTexture: small-image fallback expects {e3} RGBA bytes, got {i2.Length} (W={t2}, H={n2})");
                var copy = new byte[e3]; Buffer.BlockCopy(i2, 0, copy, 0, (int)e3); return copy;
            }
            if (t2 < 16 || n2 < 16 || (t2 & 15) != 0 || (n2 & 15) != 0)
                throw new GrannyException($"decodeIGCTexture: shouldBink fallback not supported (W={t2}, H={n2}). iRO corpus walker should filter these out.");
            long a2 = (long)t2 * n2;
            if (a2 > 4096 * 4096 || a2 > (long)i2.Length * 4096)
                throw new GrannyException($"decodeIGCTexture: {t2}×{n2} ({a2} px) exceeds cap ({4096 * 4096} px / 4096× of {i2.Length} B)");
            return Planes(i2, t2, n2, r2 != 0);
        }

        /// <summary>[Ct] l. 1286-1296 (via [wt]): 3 or 4 planes, each band-decoded then inverse-transformed level by level.</summary>
        private static byte[] Planes(byte[] e2, int t2, int n2, bool alpha)
        {
            int s2 = alpha ? 4 : 3;
            var c2 = new short[4][];
            for (int e3 = 0; e3 < 4; e3++) c2[e3] = new short[t2 * n2];
            var l2 = new byte[n2]; var u2 = new short[t2 * n2]; int d2 = 4;
            for (int r3 = 0; r3 < s2; r3++)
            {
                int o3 = DecodePlane(e2, d2, c2[r3], 0, t2, n2, r3 == 0 ? l2 : null);
                d2 += o3;
                Inverse(c2[r3], t2 * 8, t2 >> 3, n2 >> 3, null, u2);
                Inverse(c2[r3], t2 * 4, t2 >> 2, n2 >> 2, null, u2);
                Inverse(c2[r3], t2 * 2, t2 >> 1, n2 >> 1, null, u2);
                Inverse(c2[r3], t2, t2, n2, r3 == 0 ? l2 : null, u2);
            }
            if (!alpha) for (int e3 = 0; e3 < t2 * n2; e3++) c2[3][e3] = 255;
            return ToRgba(c2[0], c2[1], c2[2], c2[3], t2, n2);
        }

        /// <summary>[$e] l. 877-884: planes → RGBA bytes (clamped).</summary>
        private static byte[] ToRgba(short[] e2, short[] t2, short[] n2, short[] r2, int i2, int a2)
        {
            int o2 = i2 * a2; var s2 = new byte[o2 * 4]; int c2 = 0;
            for (int i3 = 0; i3 < o2; i3++)
            {
                int a3 = t2[i3], o3 = e2[i3], l2 = n2[i3], u2 = r2[i3];
                o3 -= (a3 + l2) / 4; // (a3 + l2) / 4 | 0: truncation towards zero
                a3 += o3; l2 += o3;
                a3 = a3 < 0 ? 0 : a3 > 255 ? 255 : a3; o3 = o3 < 0 ? 0 : o3 > 255 ? 255 : o3;
                l2 = l2 < 0 ? 0 : l2 > 255 ? 255 : l2; u2 = u2 < 0 ? 0 : u2 > 255 ? 255 : u2;
                s2[c2] = (byte)a3; s2[c2 + 1] = (byte)o3; s2[c2 + 2] = (byte)l2; s2[c2 + 3] = (byte)u2; c2 += 4;
            }
            return s2;
        }

        // -------------------------------------------------------------------------------------------- bit reader [et] [V] [H]
        private sealed class Bits { public byte[] Buf; public int Cur; public uint Bits_; public int Bitlen; }

        /// <summary>[tt] l. 888-890: a little-endian u32; bytes past the end read as 0 (undefined in bitwise operations).</summary>
        private static uint U32(byte[] e2, int t2)
        {
            if (t2 >= 0 && t2 + 4 <= e2.Length) return Js.U32(e2, t2);
            uint v = 0;
            for (int k = 0; k < 4; k++) { int o = t2 + k; if (o >= 0 && o < e2.Length) v |= (uint)e2[o] << (8 * k); }
            return v;
        }

        private static int Bit(Bits e2) // [V] l. 891-898
        {
            int t2 = e2.Bitlen;
            if (t2 == 0)
            {
                uint r2 = U32(e2.Buf, e2.Cur);
                e2.Cur = e2.Cur + 4; e2.Bits_ = r2 >> 1; e2.Bitlen = 31;
                return (int)(r2 & 1);
            }
            uint n2 = e2.Bits_;
            e2.Bits_ = n2 >> 1; e2.Bitlen = t2 - 1;
            return (int)(n2 & 1);
        }

        private static uint BitsN(Bits e2, int t2) // [H] l. 899-909
        {
            uint n2 = t2 == 32 ? 4294967295 : (1u << t2) - 1;
            if (e2.Bitlen < t2)
            {
                uint r3 = U32(e2.Buf, e2.Cur);
                e2.Cur = e2.Cur + 4;
                uint i2 = (e2.Bits_ | r3 << e2.Bitlen) & n2;
                e2.Bits_ = r3 >> (t2 - e2.Bitlen); e2.Bitlen = e2.Bitlen + 32 - t2;
                return i2;
            }
            uint r2 = e2.Bits_ & n2;
            e2.Bits_ = e2.Bits_ >> t2; e2.Bitlen -= t2;
            return r2;
        }

        private static int Sign16(uint e2) => (e2 & 32768) != 0 ? (int)(e2 | 0xFFFF0000) : (int)e2; // [nt] l. 910-912

        private static readonly int[] LitLong = { 128, 256, 512, 1024 }, ZeroLong = { 512, 1024, 2048, 3072 }; // [rt] [it] l. 913

        /// <summary>[at] l. 914-921: the bit length of a context sum, capped at 15.</summary>
        private static int Ctx(uint t2)
        {
            if (t2 == 0) return 0;
            if (t2 >= 16384) return 15;
            int n2 = 0;
            while (t2 > 0) { n2++; t2 >>= 1; }
            return n2;
        }

        /// <summary>[ot] l. 922-928: a band filled with one value.</summary>
        private static void Fill(short[] e2, int t2, int n2, int r2, int i2, int a2)
        {
            int o2 = n2 - r2, s2 = t2;
            for (int t3 = 0; t3 < i2; t3++) { for (int t4 = 0; t4 < r2; t4++) e2[s2++] = (short)a2; s2 += o2; }
        }

        /// <summary>[st] l. 929-932: the per-row "has detail" flags of plane 0.</summary>
        private static void RowFlags(Arith e2, byte[] t2, int n2)
        {
            int r2 = GetValue(e2, n2 + 1);
            for (int i2 = 0; i2 < n2; i2++)
            {
                if (GetCount(e2, n2) >= r2) { t2[i2] = 1; Remove(e2, r2, n2 - r2, n2); }
                else { t2[i2] = 0; Remove(e2, 0, r2, n2); }
            }
        }

        private static int Negate(int v, Bits b) { int e3 = -Bit(b); return (v ^ e3) - e3; } // `let e3 = -V(t2) | 0; v = (v ^ e3) - e3`

        /// <summary>[ct] l. 933-977: the low-pass (LL) band — DPCM against the left / upper neighbours.</summary>
        private static void DecodeLowBand(Arith e2, Bits t2, short[] n2, int r2, int i2, int a2, int o2)
        {
            if (Bit(t2) != 0) { uint e3 = BitsN(t2, 16); Fill(n2, r2, i2, a2, o2, Sign16(e3)); return; }
            int s2 = (int)BitsN(t2, 16), c2 = s2 + 1;
            var l2 = NewModel(c2);
            int u2 = i2 - a2;
            uint d0 = BitsN(t2, 16);
            n2[r2++] = (short)Sign16(d0);
            int d2 = Sign16(d0);
            for (int i3 = 0; i3 < a2 - 1; i3++)
            {
                int i4 = Symbol(l2, e2);
                if (i4 < 0) { int t3 = GetValue(e2, c2); SetEscaped(i4, t3, l2); i4 = t3; }
                if (i4 != 0) i4 = Negate(i4, t2);
                d2 = unchecked(i4 + d2); n2[r2++] = (short)d2;
            }
            for (int s3 = 0; s3 < o2 - 1; s3++)
            {
                r2 += u2;
                int o3 = r2 - i2, s4 = Symbol(l2, e2);
                if (s4 < 0) { int t3 = GetValue(e2, c2); SetEscaped(s4, t3, l2); s4 = t3; }
                if (s4 != 0) s4 = Negate(s4, t2);
                d2 = unchecked(s4 + n2[o3]); n2[r2++] = (short)d2; ++o3;
                for (int i3 = 0; i3 < a2 - 1; i3++)
                {
                    s4 = Symbol(l2, e2);
                    if (s4 < 0) { int t3 = GetValue(e2, c2); SetEscaped(s4, t3, l2); s4 = t3; }
                    if (s4 != 0) s4 = Negate(s4, t2);
                    d2 = Js.ToInt32(s4 + ((long)d2 + n2[o3]) / 2); n2[r2++] = (short)d2; ++o3;
                }
            }
        }

        private static long Abs(long v) => v < 0 ? -v : v; // [U]

        /// <summary>[W] l. 986-1062: a high-pass band — run lengths of literals and zeros, each literal coded in a context of
        /// its neighbours' magnitudes, then scaled by the band's quantiser.</summary>
        private static void DecodeHighBand(Arith e2, Bits t2, short[] n2, int r2, int i2, int a2, int o2)
        {
            int s2 = (int)BitsN(t2, 16);
            if (Bit(t2) != 0) { int e3 = Sign16(BitsN(t2, 16)); Fill(n2, r2, i2, a2, o2, unchecked(e3 * s2)); return; }
            int c2 = (int)BitsN(t2, 16), l2 = c2 + 1;
            int u2 = Ctx(Js.ToUint32((long)c2 * s2)) + 1;
            var d2 = new Model[u2];
            for (int k = 0; k < u2; k++) d2[k] = NewModel(l2);
            var f2 = NewModel(64); var p2 = NewModel(256);
            int m2 = i2 - a2, h2 = o2;
            int g2 = GetValue(e2, l2);
            if (g2 != 0) { g2 = Negate(g2, t2); g2 = unchecked(g2 * s2); }
            n2[r2] = (short)g2;
            int _2 = g2, v2 = g2, y2 = r2;
            ++r2;
            if (a2 == 1) { DecodeHighBandColumn(e2, t2, n2, r2, i2, a2, o2, d2, f2, p2, l2, s2, m2, h2, g2, y2); return; }
            int b2 = a2 - 1, x2, S2, C2 = 0;
            for (; ; )
            {
                int c3 = Symbol(f2, e2);
                if (c3 < 0) { int e3 = (int)BitsN(t2, 6); SetEscaped(c3, e3, f2); x2 = e3; } else x2 = c3;
                if (x2 >= 60) x2 = LitLong[x2 - 60];
                int u3 = Symbol(p2, e2);
                if (u3 < 0) { int e3 = (int)BitsN(t2, 8); SetEscaped(u3, e3, p2); S2 = e3; } else S2 = u3;
                if (S2 >= 252) S2 = ZeroLong[S2 - 252] + 3 - 1; else if (S2 != 0) S2 += 2;
                if (x2 == 0 && S2 == 0)
                {
                    if (++C2 > 64) throw new GrannyException($"decodeHigh1: stuck at h={h2} w={b2} after {C2} consecutive arith reads with litLen=0/zeroLen=0 — bitstream likely off-corpus (granny2.dll hangs on the same input). Encoding={a2}x{o2}.");
                }
                else C2 = 0;
                while (x2 > 0)
                {
                    if (b2 <= 1)
                    {
                        if (b2 != 0)
                        {
                            int i3 = Ctx(Js.ToUint32(Abs(unchecked(v2 * 2)) + Abs(_2) + Abs(g2)) / 4);
                            int a3 = Symbol(d2[i3], e2);
                            if (a3 < 0) { int t3 = GetValue(e2, l2); SetEscaped(a3, t3, d2[i3]); a3 = t3; }
                            if (a3 != 0) { a3 = Negate(a3, t2); a3 = unchecked(a3 * s2); }
                            n2[r2++] = (short)a3; --x2;
                        }
                        if (--h2 == 0) return;
                        b2 = a2; r2 += m2; y2 = r2 - i2; g2 = n2[y2++]; _2 = g2; v2 = g2;
                    }
                    else
                    {
                        int i3 = n2[y2];
                        int a3 = Ctx(Js.ToUint32(Abs(v2) + Abs(_2) + Abs(g2) + Abs(i3)) / 4);
                        int o3 = Symbol(d2[a3], e2);
                        if (o3 < 0) { int t3 = GetValue(e2, l2); SetEscaped(o3, t3, d2[a3]); o3 = t3; }
                        if (o3 != 0) { o3 = Negate(o3, t2); o3 = unchecked(o3 * s2); }
                        n2[r2] = (short)o3; _2 = g2; g2 = i3; v2 = o3; ++r2; ++y2; --b2; --x2;
                    }
                }
                while (S2 > 0)
                {
                    if (S2 >= b2)
                    {
                        S2 -= b2; y2 += b2;
                        while (b2-- > 0) n2[r2++] = 0;
                        if (--h2 == 0) return;
                        b2 = a2; r2 += m2; y2 = r2 - i2; g2 = n2[y2++]; _2 = g2; v2 = g2;
                    }
                    else
                    {
                        b2 -= S2; y2 += S2;
                        do n2[r2++] = 0; while (--S2 > 0);
                        v2 = 0; g2 = n2[y2 - 1]; _2 = n2[y2 - 2];
                    }
                }
            }
        }

        /// <summary>[ut] l. 1063-1127: the same for a band one coefficient wide (from its second row on).</summary>
        private static void DecodeHighBandColumn(Arith e2, Bits t2, short[] n2, int r2, int i2, int a2, int o2, Model[] s2, Model c2, Model l2,
            int u2, int f2, int p2, int m2, int h2, int v2)
        {
            int y2 = m2, b2 = h2, x2, S2, C2;
            if (--y2 == 0) return;
            int w2 = a2;
            r2 += p2; C2 = r2 - i2; b2 = n2[C2++]; x2 = b2; S2 = b2;
            int T2, E2, D2 = 0;
            for (; ; )
            {
                int d3 = Symbol(c2, e2);
                if (d3 < 0) { int e3 = (int)BitsN(t2, 6); SetEscaped(d3, e3, c2); T2 = e3; } else T2 = d3;
                if (T2 >= 60) T2 = LitLong[T2 - 60];
                int m3 = Symbol(l2, e2);
                if (m3 < 0) { int e3 = (int)BitsN(t2, 8); SetEscaped(m3, e3, l2); E2 = e3; } else E2 = m3;
                if (E2 >= 252) E2 = ZeroLong[E2 - 252] + 3 - 1; else if (E2 != 0) E2 += 2;
                if (T2 == 0 && E2 == 0)
                {
                    if (++D2 > 64) throw new GrannyException($"decodeHigh1AfterFirst: stuck at h={y2} w={w2} after {D2} consecutive arith reads with litLen=0/zeroLen=0 — bitstream likely off-corpus. Encoding={a2}x{o2}.");
                }
                else D2 = 0;
                while (T2 > 0)
                {
                    if (w2 <= 1)
                    {
                        if (w2 != 0)
                        {
                            // (U(S2 * 2) + U(x2) + U(b2) | 0) / 4 | 0: here a double sum, ToInt32, a truncating quarter
                            int i3 = Js.ToInt32(Abs((long)S2 * 2) + Abs(x2) + Abs(b2)) / 4;
                            i3 = Ctx(unchecked((uint)i3));
                            int a3 = Symbol(s2[i3], e2);
                            if (a3 < 0) { int t3 = GetValue(e2, u2); SetEscaped(a3, t3, s2[i3]); a3 = t3; }
                            if (a3 != 0) { a3 = Negate(a3, t2); a3 = unchecked(a3 * f2); }
                            n2[r2++] = (short)a3; --T2;
                        }
                        if (--y2 == 0) return;
                        w2 = a2; r2 += p2; C2 = r2 - i2; b2 = n2[C2++]; x2 = b2; S2 = b2;
                    }
                    else
                    {
                        int i3 = n2[C2];
                        int a3 = Ctx(Js.ToUint32(Abs(S2) + Abs(x2) + Abs(b2) + Abs(i3)) / 4);
                        int o3 = Symbol(s2[a3], e2);
                        if (o3 < 0) { int t3 = GetValue(e2, u2); SetEscaped(o3, t3, s2[a3]); o3 = t3; }
                        if (o3 != 0) { o3 = Negate(o3, t2); o3 = unchecked(o3 * f2); }
                        n2[r2] = (short)o3; x2 = b2; b2 = i3; S2 = o3; ++r2; ++C2; --w2; --T2;
                    }
                }
                while (E2 > 0)
                {
                    if (E2 >= w2)
                    {
                        E2 -= w2; C2 += w2;
                        while (w2-- > 0) n2[r2++] = 0;
                        if (--y2 == 0) return;
                        w2 = a2; r2 += p2; C2 = r2 - i2; b2 = n2[C2++]; x2 = b2; S2 = b2;
                    }
                    else
                    {
                        w2 -= E2; C2 += E2;
                        do n2[r2++] = 0; while (--E2 > 0);
                        S2 = 0; b2 = n2[C2 - 1]; x2 = n2[C2 - 2];
                    }
                }
            }
        }

        /// <summary>[dt] l. 1128-1131: one plane's bands, coarsest level first; returns the bytes it used.</summary>
        private static int DecodePlane(byte[] e2, int t2, short[] n2, int r2, int i2, int a2, byte[] o2)
        {
            uint s2 = U32(e2, t2);
            int c2 = (int)(t2 + 8 + s2);
            var l2 = NewArith(e2, t2 + 8);
            var u2 = new Bits { Buf = e2, Cur = c2 };
            DecodeLowBand(l2, u2, n2, r2, i2 * 16, i2 >> 4, a2 >> 4);
            DecodeHighBand(l2, u2, n2, r2 + (i2 >> 4), i2 * 16, i2 >> 4, a2 >> 4);
            DecodeHighBand(l2, u2, n2, r2 + i2 * 8, i2 * 16, i2 >> 4, a2 >> 4);
            DecodeHighBand(l2, u2, n2, r2 + (i2 >> 4) + i2 * 8, i2 * 16, i2 >> 4, a2 >> 4);
            DecodeHighBand(l2, u2, n2, r2 + (i2 >> 3), i2 * 8, i2 >> 3, a2 >> 3);
            DecodeHighBand(l2, u2, n2, r2 + i2 * 4, i2 * 8, i2 >> 3, a2 >> 3);
            DecodeHighBand(l2, u2, n2, r2 + (i2 >> 3) + i2 * 4, i2 * 8, i2 >> 3, a2 >> 3);
            DecodeHighBand(l2, u2, n2, r2 + (i2 >> 2), i2 * 4, i2 >> 2, a2 >> 2);
            DecodeHighBand(l2, u2, n2, r2 + i2 * 2, i2 * 4, i2 >> 2, a2 >> 2);
            DecodeHighBand(l2, u2, n2, r2 + (i2 >> 2) + i2 * 2, i2 * 4, i2 >> 2, a2 >> 2);
            DecodeHighBand(l2, u2, n2, r2 + (i2 >> 1), i2 * 2, i2 >> 1, a2 >> 1);
            DecodeHighBand(l2, u2, n2, r2 + i2, i2 * 2, i2 >> 1, a2 >> 1);
            DecodeHighBand(l2, u2, n2, r2 + (i2 >> 1) + i2, i2 * 2, i2 >> 1, a2 >> 1);
            if (o2 != null) RowFlags(l2, o2, a2);
            return Js.ToInt32((long)s2 + U32(e2, t2 + 4) + 8);
        }

        // -------------------------------------------------------------------------------------------- inverse wavelet
        /// <summary>[G] l. 1132-1134: a rounded division by 65536 (the sum's sign read through ToInt32, as `e2 >> 31` does).</summary>
        private static int Round16(long e2) => Js.ToInt32((e2 + (32767 ^ (Js.ToInt32(e2) >> 31))) / 65536);

        private static int M(long v) => Js.ToInt32(v); // `x * k | 0`

        // the two output taps of the 9/7-style synthesis filter (even / odd sample), shared by the four filter loops
        private static long Even(long m0, long m1, long m2, long h0, long h1, long h2, long h3) =>
            m1 * 51674 - M((m0 + m2) * 2667) - M((h0 + h3) * 1563) + M((h1 + h2) * 24733);
        private static long Odd(long m0, long m1, long m2, long m3, long h0, long h1, long h2, long h3, long h4) =>
            M((m1 + m2) * 27400) - M((m0 + m3) * 4230) - M(h2 * 55882) - M((h0 + h4) * 2479) + M((h1 + h3) * 7250);
        private static long EvenLow(long m0, long m1, long m2) => m1 * 51674 - M((m0 + m2) * 2667);
        private static long OddLow(long m0, long m1, long m2, long m3) => M((m1 + m2) * 27400) - M((m0 + m3) * 4230);

        private static readonly int[] Mt = new int[80], Ht = new int[84], Gt = new int[4], Tt = new int[5], Ft = new int[8], Pt = new int[8]; // [mt ht gt _t ft pt] l. 1135
        private static readonly object FilterLock = new object();

        /// <summary>[vt] l. 1136-1167: horizontal synthesis, width ≥ 16 (low half + high half → interleaved).</summary>
        private static void RowsWide(short[] e2, int t2, short[] n2, int r2, int i2, int a2, byte[] o2, int s2, int c2)
        {
            int l2 = i2 >> 1, u2 = s2 * t2, d2 = s2 * r2, f2 = s2 * r2 + l2, p2 = s2;
            int[] m2 = Ft, h2 = Pt;
            for (int i3 = 0; i3 < c2; i3++)
            {
                int i4 = 1, a3 = u2, s3 = d2, c3 = f2, g2 = d2 + l2;
                m2[1] = n2[s3 + 0]; m2[0] = m2[2] = n2[s3 + 1]; m2[3] = n2[s3 + 2]; m2[4] = n2[s3 + 3]; m2[5] = n2[s3 + 4]; m2[6] = n2[s3 + 5]; s3 += 6;
                h2[2] = n2[c3 + 0]; h2[3] = n2[c3 + 1]; h2[4] = n2[c3 + 2]; h2[5] = n2[c3 + 3]; h2[6] = n2[c3 + 4]; h2[7] = n2[c3 + 5]; h2[0] = h2[3]; h2[1] = h2[2]; c3 += 6;
                bool _2 = o2 == null || o2[p2] != 0;
                int v2 = l2 < 8 ? 0 : (l2 - 8) / 4;
                if (_2)
                {
                    while (v2-- > 0)
                    {
                        int t4 = Round16(Even(m2[0], m2[1], m2[2], h2[0], h2[1], h2[2], h2[3]));
                        int r3 = Round16(Odd(m2[0], m2[1], m2[2], m2[3], h2[0], h2[1], h2[2], h2[3], h2[4]));
                        int i5 = Round16(Even(m2[1], m2[2], m2[3], h2[1], h2[2], h2[3], h2[4]));
                        int o3 = Round16(Odd(m2[1], m2[2], m2[3], m2[4], h2[1], h2[2], h2[3], h2[4], h2[5]));
                        int l3 = Round16(Even(m2[2], m2[3], m2[4], h2[2], h2[3], h2[4], h2[5]));
                        int u3 = Round16(Odd(m2[2], m2[3], m2[4], m2[5], h2[2], h2[3], h2[4], h2[5], h2[6]));
                        int d3 = Round16(Even(m2[3], m2[4], m2[5], h2[3], h2[4], h2[5], h2[6]));
                        int f3 = Round16(Odd(m2[3], m2[4], m2[5], m2[6], h2[3], h2[4], h2[5], h2[6], h2[7]));
                        e2[a3 + 0] = (short)t4; e2[a3 + 1] = (short)r3; e2[a3 + 2] = (short)i5; e2[a3 + 3] = (short)o3;
                        e2[a3 + 4] = (short)l3; e2[a3 + 5] = (short)u3; e2[a3 + 6] = (short)d3; e2[a3 + 7] = (short)f3; a3 += 8;
                        m2[0] = m2[4]; m2[1] = m2[5]; m2[2] = m2[6]; m2[3] = n2[s3 + 0]; m2[4] = n2[s3 + 1]; m2[5] = n2[s3 + 2]; m2[6] = n2[s3 + 3];
                        h2[0] = h2[4]; h2[1] = h2[5]; h2[2] = h2[6]; h2[3] = h2[7]; h2[4] = n2[c3 + 0]; h2[5] = n2[c3 + 1]; h2[6] = n2[c3 + 2]; h2[7] = n2[c3 + 3];
                        s3 += 4; c3 += 4;
                    }
                    int t3 = l2 < 8 ? l2 : (l2 & 3) + 8;
                    while (t3-- > 0)
                    {
                        if (s3 == g2) { s3 -= i4; c3 -= i4 + i4; i4 = -i4; }
                        int t4 = Round16(Even(m2[0], m2[1], m2[2], h2[0], h2[1], h2[2], h2[3]));
                        int r3 = Round16(Odd(m2[0], m2[1], m2[2], m2[3], h2[0], h2[1], h2[2], h2[3], h2[4]));
                        e2[a3 + 0] = (short)t4; e2[a3 + 1] = (short)r3; a3 += 2;
                        m2[0] = m2[1]; m2[1] = m2[2]; m2[2] = m2[3]; m2[3] = m2[4]; m2[4] = m2[5]; m2[5] = m2[6]; m2[6] = n2[s3];
                        h2[0] = h2[1]; h2[1] = h2[2]; h2[2] = h2[3]; h2[3] = h2[4]; h2[4] = h2[5]; h2[5] = h2[6]; h2[6] = h2[7]; h2[7] = n2[c3];
                        s3 += i4; c3 += i4;
                    }
                }
                else
                {
                    while (v2-- > 0)
                    {
                        int t4 = Round16(EvenLow(m2[0], m2[1], m2[2])), r3 = Round16(OddLow(m2[0], m2[1], m2[2], m2[3]));
                        int i5 = Round16(EvenLow(m2[1], m2[2], m2[3])), o3 = Round16(OddLow(m2[1], m2[2], m2[3], m2[4]));
                        int c4 = Round16(EvenLow(m2[2], m2[3], m2[4])), l3 = Round16(OddLow(m2[2], m2[3], m2[4], m2[5]));
                        int u3 = Round16(EvenLow(m2[3], m2[4], m2[5])), d3 = Round16(OddLow(m2[3], m2[4], m2[5], m2[6]));
                        e2[a3 + 0] = (short)t4; e2[a3 + 1] = (short)r3; e2[a3 + 2] = (short)i5; e2[a3 + 3] = (short)o3;
                        e2[a3 + 4] = (short)c4; e2[a3 + 5] = (short)l3; e2[a3 + 6] = (short)u3; e2[a3 + 7] = (short)d3; a3 += 8;
                        m2[0] = m2[4]; m2[1] = m2[5]; m2[2] = m2[6]; m2[3] = n2[s3 + 0]; m2[4] = n2[s3 + 1]; m2[5] = n2[s3 + 2]; m2[6] = n2[s3 + 3];
                        s3 += 4;
                    }
                    int t3 = l2 < 8 ? l2 : (l2 & 3) + 8;
                    while (t3-- > 0)
                    {
                        if (s3 == g2) { --s3; i4 = -1; }
                        int t4 = Round16(EvenLow(m2[0], m2[1], m2[2])), r3 = Round16(OddLow(m2[0], m2[1], m2[2], m2[3]));
                        e2[a3 + 0] = (short)t4; e2[a3 + 1] = (short)r3; a3 += 2;
                        m2[0] = m2[1]; m2[1] = m2[2]; m2[2] = m2[3]; m2[3] = m2[4]; m2[4] = m2[5]; m2[5] = m2[6]; m2[6] = n2[s3];
                        s3 += i4;
                    }
                }
                u2 += t2; d2 += r2; f2 += r2; ++p2;
            }
        }

        /// <summary>A read of the 80 / 84-entry ring buffers where the package may index past them (Int32Array: undefined → 0).</summary>
        private static int Ring(int[] a, int i) => i >= 0 && i < a.Length ? a[i] : 0;

        /// <summary>[yt] l. 1168-1251: vertical synthesis, height ≥ 16, four columns at a time (a ring of rows), then the rest one by one.</summary>
        private static void ColumnsTall(short[] e2, int t2, short[] n2, int r2, int i2, int a2, int o2, int s2)
        {
            int c2 = s2 >> 1, l2 = o2 * t2, u2 = 0, d2 = r2, f2 = r2 * a2, p2 = r2 * 2;
            if (o2 != 0) { u2 += (o2 / 2 - 1) * p2; d2 += (o2 / 2 - 2) * p2; }
            int m2 = 0, h2 = i2 / 4;
            for (int r3 = 0; r3 < h2; r3++)
            {
                int r4 = p2, i3 = l2 + m2, a3 = u2 + m2, s3 = d2 + m2, h3 = f2 + m2;
                int[] g3 = Mt, _2 = Ht;
                if (o2 != 0)
                {
                    for (int e3 = 0; e3 < 4; e3++) g3[0 + e3] = n2[a3 + e3]; a3 += r4;
                    for (int e3 = 0; e3 < 4; e3++) g3[4 + e3] = n2[a3 + e3]; a3 += r4;
                    for (int e3 = 0; e3 < 4; e3++) g3[8 + e3] = n2[a3 + e3]; a3 += r4;
                    for (int e3 = 0; e3 < 4; e3++) g3[12 + e3] = n2[a3 + e3]; a3 += r4;
                    for (int e3 = 0; e3 < 4; e3++) _2[0 + e3] = n2[s3 + e3]; s3 += r4;
                    for (int e3 = 0; e3 < 4; e3++) _2[4 + e3] = n2[s3 + e3]; s3 += r4;
                    for (int e3 = 0; e3 < 4; e3++) _2[8 + e3] = n2[s3 + e3]; s3 += r4;
                    for (int e3 = 0; e3 < 4; e3++) _2[12 + e3] = n2[s3 + e3]; s3 += r4;
                    for (int e3 = 0; e3 < 4; e3++) _2[16 + e3] = n2[s3 + e3]; s3 += r4;
                }
                else
                {
                    for (int e3 = 0; e3 < 4; e3++) g3[4 + e3] = n2[a3 + e3]; a3 += r4;
                    for (int e3 = 0; e3 < 4; e3++) { int t3 = n2[a3 + e3]; g3[0 + e3] = t3; g3[8 + e3] = t3; } a3 += r4;
                    for (int e3 = 0; e3 < 4; e3++) g3[12 + e3] = n2[a3 + e3]; a3 += r4;
                    for (int e3 = 0; e3 < 4; e3++) { int t3 = n2[s3 + e3]; _2[4 + e3] = t3; _2[8 + e3] = t3; } s3 += r4;
                    for (int e3 = 0; e3 < 4; e3++) { int t3 = n2[s3 + e3]; _2[0 + e3] = t3; _2[12 + e3] = t3; } s3 += r4;
                    for (int e3 = 0; e3 < 4; e3++) _2[16 + e3] = n2[s3 + e3]; s3 += r4;
                }
                int v2 = 0, y2 = 0;
                int l3 = 0, u3 = 0, d3 = 0, f3 = 0, p3 = 0, m3 = 0, b2 = 0, x2 = 0;
                for (int o3 = 0; o3 < c2; o3++)
                {
                    if (a3 == h3) { a3 -= r4; s3 -= r4 + r4; r4 = -r4; }
                    int o4 = v2, c3 = y2;
                    for (int e3 = 0; e3 < 4; e3++)
                    {
                        int t3 = g3[(o4 + 0) * 4 + e3], n3 = g3[(o4 + 1) * 4 + e3], r5 = g3[(o4 + 2) * 4 + e3], i4 = g3[(o4 + 3) * 4 + e3];
                        int a4 = _2[(c3 + 0) * 4 + e3], s4 = _2[(c3 + 1) * 4 + e3], h4 = _2[(c3 + 2) * 4 + e3], v3 = _2[(c3 + 3) * 4 + e3], y3 = _2[(c3 + 4) * 4 + e3];
                        int w2 = Round16(Even(t3, n3, r5, a4, s4, h4, v3)), T2 = Round16(Odd(t3, n3, r5, i4, a4, s4, h4, v3, y3));
                        if (e3 == 0) { l3 = w2; p3 = T2; } else if (e3 == 1) { u3 = w2; m3 = T2; } else if (e3 == 2) { d3 = w2; b2 = T2; } else { f3 = w2; x2 = T2; }
                    }
                    e2[i3 + 0] = (short)l3; e2[i3 + 1] = (short)u3; e2[i3 + 2] = (short)d3; e2[i3 + 3] = (short)f3;
                    e2[i3 + t2 + 0] = (short)p3; e2[i3 + t2 + 1] = (short)m3; e2[i3 + t2 + 2] = (short)b2; e2[i3 + t2 + 3] = (short)x2;
                    i3 += t2 + t2; ++v2; ++y2;
                    if (v2 + 3 == 20)
                    {
                        // never reached by the client's strips (≤ 6 row pairs per call); kept with the package's reads, 80.. → 0
                        for (int e3 = 0; e3 < 4; e3++) g3[0 + e3] = Ring(g3, (v2 + 1) * 4 + e3);
                        for (int e3 = 0; e3 < 4; e3++) g3[4 + e3] = Ring(g3, (v2 + 2) * 4 + e3);
                        for (int e3 = 0; e3 < 4; e3++) g3[8 + e3] = Ring(g3, (v2 + 3) * 4 + e3);
                        for (int e3 = 0; e3 < 4; e3++) _2[0 + e3] = Ring(_2, (y2 + 1) * 4 + e3);
                        for (int e3 = 0; e3 < 4; e3++) _2[4 + e3] = Ring(_2, (y2 + 2) * 4 + e3);
                        for (int e3 = 0; e3 < 4; e3++) _2[8 + e3] = Ring(_2, (y2 + 3) * 4 + e3);
                        for (int e3 = 0; e3 < 4; e3++) _2[12 + e3] = Ring(_2, (y2 + 4) * 4 + e3);
                        v2 = 0; y2 = 0;
                    }
                    for (int e3 = 0; e3 < 4; e3++) g3[(v2 + 3) * 4 + e3] = n2[a3 + e3];
                    for (int e3 = 0; e3 < 4; e3++) _2[(y2 + 4) * 4 + e3] = n2[s3 + e3];
                    a3 += r4; s3 += r4;
                }
                m2 += 4;
            }
            int gRest = i2 & 3;
            for (int r3 = 0; r3 < gRest; r3++)
            {
                int r4 = p2, i3 = l2 + m2, a3 = u2 + m2, s3 = d2 + m2, h3 = f2 + m2;
                int[] g3 = Gt, _2 = Tt;
                if (o2 != 0)
                {
                    g3[0] = n2[a3]; a3 += r4; g3[1] = n2[a3]; a3 += r4; g3[2] = n2[a3]; a3 += r4; g3[3] = n2[a3]; a3 += r4;
                    _2[0] = n2[s3]; s3 += r4; _2[1] = n2[s3]; s3 += r4; _2[2] = n2[s3]; s3 += r4; _2[3] = n2[s3]; s3 += r4; _2[4] = n2[s3]; s3 += r4;
                }
                else
                {
                    g3[1] = n2[a3]; a3 += r4; g3[0] = g3[2] = n2[a3]; a3 += r4; g3[3] = n2[a3]; a3 += r4;
                    _2[1] = _2[2] = n2[s3]; s3 += r4; _2[0] = _2[3] = n2[s3]; s3 += r4; _2[4] = n2[s3]; s3 += r4;
                }
                for (int o3 = 0; o3 < c2; o3++)
                {
                    if (a3 == h3) { a3 -= r4; s3 -= r4 + r4; r4 = -r4; }
                    long o4 = Even(g3[0], g3[1], g3[2], _2[0], _2[1], _2[2], _2[3]);
                    long c3 = Odd(g3[0], g3[1], g3[2], g3[3], _2[0], _2[1], _2[2], _2[3], _2[4]);
                    e2[i3] = (short)Round16(o4); e2[i3 + t2] = (short)Round16(c3); i3 += t2 + t2;
                    g3[0] = g3[1]; g3[1] = g3[2]; g3[2] = g3[3]; g3[3] = n2[a3];
                    _2[0] = _2[1]; _2[1] = _2[2]; _2[2] = _2[3]; _2[3] = _2[4]; _2[4] = n2[s3];
                    a3 += r4; s3 += r4;
                }
                m2 += 1;
            }
        }

        /// <summary>[bt] l. 1252-1266: horizontal synthesis, width &lt; 16 (a lifting pair per sample).</summary>
        private static void RowsNarrow(short[] e2, int t2, short[] n2, int r2, int i2, int a2, byte[] o2, int s2, int c2)
        {
            int l2 = i2 >> 1, u2 = s2 * t2, d2 = s2 * r2, f2 = s2 * r2 + l2, p2 = s2;
            for (int i3 = 0; i3 < c2; i3++)
            {
                int i4 = u2, a3 = d2, s3 = f2;
                if (o2 == null || o2[p2] != 0)
                    for (int t3 = 0; t3 < l2; t3++)
                    {
                        int t4 = n2[a3], r3 = n2[s3], o3 = t4 * 2 + r3, c3 = t4 * 2 - r3;
                        o3 = (o3 + (1 ^ o3 >> 31)) / 2; c3 = (c3 + (1 ^ c3 >> 31)) / 2;
                        e2[i4 + 0] = (short)o3; e2[i4 + 1] = (short)c3; i4 += 2; a3 += 1; s3 += 1;
                    }
                else
                    for (int t3 = 0; t3 < l2; t3++) { short t4 = n2[a3]; e2[i4 + 0] = t4; e2[i4 + 1] = t4; i4 += 2; a3 += 1; }
                u2 += t2; d2 += r2; f2 += r2; ++p2;
            }
        }

        /// <summary>[xt] l. 1267-1277: vertical synthesis, height &lt; 16.</summary>
        private static void ColumnsShort(short[] e2, int t2, short[] n2, int r2, int i2, int a2, int o2, int s2)
        {
            int c2 = s2 >> 1, l2 = o2 * t2, u2 = 0, d2 = r2, f2 = r2 * 2;
            if (o2 != 0) { u2 += o2 / 2 * f2; d2 += o2 / 2 * f2; }
            for (int r3 = 0; r3 < i2; r3++)
            {
                int i3 = l2 + r3, a3 = u2 + r3, o3 = d2 + r3;
                for (int r4 = 0; r4 < c2; r4++)
                {
                    int r5 = n2[a3], s3 = n2[o3], c3 = r5 * 2 + s3, l3 = r5 * 2 - s3;
                    c3 = (c3 + (1 ^ c3 >> 31)) / 2; l3 = (l3 + (1 ^ l3 >> 31)) / 2;
                    e2[i3] = (short)c3; e2[i3 + t2] = (short)l3; i3 += t2 + t2; a3 += f2; o3 += f2;
                }
            }
        }

        /// <summary>[St] l. 1278-1285: one level of the inverse transform, horizontal and vertical passes interleaved by strips.</summary>
        private static void Inverse(short[] e2, int t2, int n2, int r2, byte[] i2, short[] a2)
        {
            lock (FilterLock) // the package's filter scratch rows are module-level; two decodes at once must not share them
            {
                bool wide = n2 >= 16, tall = r2 >= 16;
                int c2 = r2 <= 16 ? r2 : 12, l2 = r2 - c2, u2 = 0, d2 = r2;
                if (wide) RowsWide(a2, t2, e2, t2, n2, r2, i2, 0, c2); else RowsNarrow(a2, t2, e2, t2, n2, r2, i2, 0, c2);
                do
                {
                    int f2 = d2 <= 12 ? d2 : 8;
                    if (tall) ColumnsTall(e2, t2, a2, t2, n2, r2, u2, f2); else ColumnsShort(e2, t2, a2, t2, n2, r2, u2, f2);
                    u2 += f2; d2 -= f2;
                    if (l2 != 0)
                    {
                        f2 = l2 <= 12 ? l2 : 8;
                        if (wide) RowsWide(a2, t2, e2, t2, n2, r2, i2, c2, f2); else RowsNarrow(a2, t2, e2, t2, n2, r2, i2, c2, f2);
                        c2 += f2; l2 -= f2;
                    }
                } while (d2 != 0);
            }
        }

        // -------------------------------------------------------------------------------------------- arithmetic decoder
        private static readonly byte[] K = { 0, 8, 4, 12, 2, 10, 6, 14, 1, 9, 5, 13, 3, 11, 7, 15 }; // 4-bit reversal [K] l. 1305
        private static readonly byte[] Ot = { 0, 4, 2, 6, 1, 5, 3, 7 };                                 // 3-bit reversal [Ot]
        private static readonly byte[] Kt = MakeKt();                                                  // 8-bit reversal [kt]
        private static byte[] MakeKt() { var e2 = new byte[256]; for (int t2 = 0; t2 < 256; t2++) e2[t2] = (byte)(K[t2 & 15] << 4 | K[t2 >> 4 & 15]); return e2; }

        private sealed class Arith { public byte[] Buf; public int Ptr; public uint Accum; public int BitsLeft; public uint High, Low, Target; }

        /// <summary>[At] l. 1310-1313.</summary>
        private static Arith NewArith(byte[] e2, int t2)
        {
            uint o2 = U32(e2, t2);
            uint s2 = o2 >> 31 & 1, c2 = o2 & 2147483647;
            uint l2 = K[c2 >> 4 & 15];
            l2 |= (uint)K[c2 & 15] << 4;
            l2 = l2 << 4 | K[c2 >> 8 & 15];
            l2 = l2 << 4 | K[c2 >> 12 & 15];
            l2 = l2 << 4 | K[c2 >> 16 & 15];
            l2 = l2 << 4 | K[c2 >> 20 & 15];
            l2 = l2 << 4 | K[c2 >> 24 & 15];
            l2 = l2 << 3 | Ot[c2 >> 28 & 7];
            return new Arith { Buf = e2, Ptr = t2 + 4, Accum = s2, BitsLeft = 1, High = 2147483647, Low = 0, Target = l2 };
        }

        /// <summary>[jt] l. 1314-1321: n bits LSB-first; 8- and 4-bit reads come back bit-reversed.</summary>
        private static uint ReadBits(Arith e2, int t2)
        {
            int n2 = e2.BitsLeft; uint r2 = e2.Accum, i2;
            if (n2 < t2)
            {
                uint s2 = U32(e2.Buf, e2.Ptr), c2 = n2 == 0 ? s2 : s2 << n2 | r2;
                e2.Accum = s2 >> (t2 - n2); e2.BitsLeft = n2 + 32 - t2; e2.Ptr = e2.Ptr + 4;
                i2 = c2 & (1u << t2) - 1;
            }
            else { i2 = r2 & (1u << t2) - 1; e2.Accum = r2 >> t2; e2.BitsLeft = n2 - t2; }
            return t2 == 8 ? Kt[i2] : t2 == 4 ? K[i2] : i2;
        }

        /// <summary>[Mt] l. 1322-1347: renormalisation.</summary>
        private static void Renorm(Arith e2)
        {
            const uint Mask = 2147483647;
            uint t2 = e2.High, n2 = e2.Low, r2 = e2.Target, i2 = n2 ^ t2;
            if ((i2 & 1073741824) == 0)
            {
                if ((i2 & 2139095040) == 0)
                    do
                    {
                        t2 = (t2 << 8 | 255) & Mask; n2 = n2 << 8 & Mask;
                        uint a2 = ReadBits(e2, 8);
                        r2 = (r2 << 8 | a2) & Mask; i2 = n2 ^ t2;
                    } while ((i2 & 2139095040) == 0);
                if ((i2 & 2013265920) == 0)
                {
                    t2 = (t2 << 4 | 15) & Mask; n2 = n2 << 4 & Mask;
                    uint a2 = ReadBits(e2, 4);
                    r2 = (r2 << 4 | a2) & Mask; i2 = n2 ^ t2;
                }
                if ((i2 & 1073741824) == 0)
                    do
                    {
                        t2 = (t2 << 1 | 1) & Mask; n2 = n2 << 1 & Mask;
                        uint a2 = ReadBits(e2, 1);
                        r2 = (r2 << 1 | a2) & Mask; i2 = n2 ^ t2;
                    } while ((i2 & 1073741824) == 0);
            }
            if ((n2 & 536870912) != 0)
                while ((t2 & 536870912) == 0)
                {
                    n2 = (n2 & 536870911) << 1 & Mask; t2 = (t2 << 1 | 1073741825) & Mask; r2 ^= 536870912;
                    uint i3 = ReadBits(e2, 1);
                    r2 = (r2 << 1 | i3) & Mask;
                    if ((n2 & 536870912) == 0) break;
                }
            e2.High = t2; e2.Low = n2; e2.Target = r2;
        }

        /// <summary>[Nt] l. 1348-1351.</summary>
        private static int GetCount(Arith e2, int t2)
        {
            uint n2 = e2.High - e2.Low + 1, r2 = e2.Target - e2.Low + 1;
            int i2 = Js.ToInt32(((double)r2 * t2 - 1) / n2);
            return i2 >= t2 ? t2 - 1 : i2;
        }

        /// <summary>[Pt] l. 1352-1360.</summary>
        private static void Remove(Arith e2, int t2, int n2, int r2)
        {
            uint i2 = e2.High - e2.Low + 1;
            double a2 = Math.Floor((double)i2 * ((double)t2 + n2) / r2), o2 = Math.Floor((double)i2 * t2 / r2);
            uint s2 = e2.Low;
            e2.High = Js.ToUint32(s2 + a2 - 1); e2.Low = Js.ToUint32(s2 + o2);
            Renorm(e2);
        }

        /// <summary>[q] l. 1361-1364.</summary>
        private static int GetValue(Arith e2, int t2)
        {
            uint n2 = e2.High - e2.Low + 1, r2 = e2.Target - e2.Low + 1;
            int i2 = Js.ToInt32(((double)r2 * t2 - 1) / n2);
            if (i2 >= t2) i2 = t2 - 1;
            Remove(e2, i2, 1, t2);
            return i2;
        }

        // -------------------------------------------------------------------------------------------- adaptive model [Bt]
        private sealed class Model
        {
            public ushort[] CumCounts = new ushort[16], SingleCounts, Values;
            public int SinglesLength, BandBoundary, ShiftDepth, BucketSize, UniqueCount;
        }

        /// <summary>[Ft] l. 1365-1378.</summary>
        private static void Buckets(Model e2, int t2)
        {
            if (t2 < 6) { e2.BucketSize = 0; e2.ShiftDepth = 15; e2.BandBoundary = 0; return; }
            int n2 = 0; uint r2 = 4294967295;
            for (int e3 = 0; e3 < 16; e3++)
            {
                long i2 = 1L << e3;
                uint a2 = Js.ToUint32(Js.ToInt32((i2 + t2 - 1) / i2)); // ((i2 + t2 - 1) / i2 | 0) >>> 0
                if (a2 > 16) a2 = 16;
                uint o2 = Js.ToUint32(t2 - (a2 - 1L) * i2);
                if (o2 < i2) o2 = Js.ToUint32(i2);
                if (o2 < r2) { n2 = e3; r2 = o2; }
                if (i2 > t2) break;
            }
            e2.BucketSize = 1 << n2 & 65535; e2.ShiftDepth = n2 & 255; e2.BandBoundary = 15 * e2.BucketSize & 65535;
        }

        /// <summary>[It] l. 1379-1394: add a packed pair of 16-bit increments.</summary>
        private static void Increment(Model e2, int t2, uint n2)
        {
            int r2 = (int)(n2 & 65535);
            if (t2 >= e2.BandBoundary)
            {
                e2.CumCounts[15] = (ushort)(e2.CumCounts[15] + r2); e2.SingleCounts[t2] = (ushort)(e2.SingleCounts[t2] + r2);
                return;
            }
            int i2 = (int)((uint)t2 >> e2.ShiftDepth);
            if ((i2 & 1) != 0) { e2.CumCounts[i2] = (ushort)(e2.CumCounts[i2] + r2); i2++; }
            i2 = (int)((uint)i2 >> 1);
            if (i2 <= 7)
                for (int n3 = i2; n3 <= 7; n3++)
                {
                    int r3 = n3 * 2;
                    uint i3 = unchecked((uint)(e2.CumCounts[r3 + 1] * 65536 + e2.CumCounts[r3]) + n2);
                    e2.CumCounts[r3] = (ushort)(i3 & 65535); e2.CumCounts[r3 + 1] = (ushort)(i3 >> 16 & 65535);
                }
            e2.SingleCounts[t2] = (ushort)(e2.SingleCounts[t2] + r2);
        }

        /// <summary>[Lt] l. 1395-1406: the symbol slot for a count (bumping the cumulative counts from its bucket on).</summary>
        private static int FindSymbol(Model e2, int t2, out int low)
        {
            var n2 = e2.CumCounts; int r2 = 0, i2 = 0;
            if (t2 >= n2[7]) { i2 = n2[7]; r2 = 8; }
            if (t2 >= n2[r2 + 3]) { i2 = n2[r2 + 3]; r2 += 4; }
            if (t2 >= n2[r2 + 1]) { i2 = n2[r2 + 1]; r2 += 2; }
            if (t2 >= n2[r2]) { i2 = n2[r2]; r2 += 1; }
            for (int e3 = r2; e3 < 16; e3++) n2[e3] = (ushort)(n2[e3] + 1);
            int a2 = r2 * e2.BucketSize, o2 = i2; var s2 = e2.SingleCounts;
            int c2 = Math.Min(s2.Length, e2.UniqueCount + 1);
            while (a2 < c2)
            {
                int e3 = o2 + s2[a2] & 65535;
                if (t2 < e3) { low = o2; return a2; }
                o2 = e3; a2++;
            }
            low = o2;
            return Math.Min(Math.Max(a2 - 1, 0), Math.Max(e2.UniqueCount, 0));
        }

        /// <summary>[Rt] l. 1407-1415.</summary>
        private static void Totals(Model e2, uint[] t2)
        {
            if (e2.SinglesLength != e2.UniqueCount && e2.SingleCounts[0] == 0)
            {
                e2.SingleCounts[0] = 2;
                int n3 = 0 >= e2.BandBoundary ? 15 : 0;
                t2[n3] = t2[n3] + 2;
            }
            int n2 = 0;
            for (int r2 = 0; r2 < 16; r2++) { n2 = (int)(n2 + t2[r2] & 65535); e2.CumCounts[r2] = (ushort)n2; }
        }

        /// <summary>[zt] l. 1416-1448: halve every count, drop the dead symbols, move the most frequent to its bucket's start.</summary>
        private static void Rescale(Model e2)
        {
            Buckets(e2, e2.SinglesLength + 1);
            var t2 = new uint[16];
            e2.SingleCounts[0] = (ushort)(e2.SingleCounts[0] >> 1);
            int n2 = 0 >= e2.BandBoundary ? 15 : 0;
            t2[n2] = e2.SingleCounts[0];
            int r2 = 0, i2 = 0, a2 = 1;
            while (a2 <= e2.SinglesLength)
            {
                while (e2.SingleCounts[a2] <= 1)
                {
                    if (a2 >= e2.SinglesLength) { e2.SingleCounts[a2] = 0; e2.SinglesLength = e2.SinglesLength - 1; break; }
                    e2.SingleCounts[a2] = e2.SingleCounts[e2.SinglesLength]; e2.SingleCounts[e2.SinglesLength] = 0;
                    e2.Values[a2] = e2.Values[e2.SinglesLength]; e2.SinglesLength = e2.SinglesLength - 1;
                }
                if (a2 > e2.SinglesLength) break;
                e2.SingleCounts[a2] = (ushort)(e2.SingleCounts[a2] >> 1);
                int n3 = e2.SingleCounts[a2];
                if (n3 > r2) { r2 = n3; i2 = a2; }
                int o2 = a2 >= e2.BandBoundary ? 15 : (int)((uint)a2 >> e2.ShiftDepth);
                t2[o2] = t2[o2] + (uint)n3; a2++;
            }
            if (r2 > 0)
            {
                int n3 = e2.SinglesLength >= e2.BandBoundary ? e2.BandBoundary : (int)((uint)e2.SinglesLength >> e2.ShiftDepth) << e2.ShiftDepth;
                if (n3 == 0) n3 = 1;
                if (i2 != n3)
                {
                    uint r3 = e2.SingleCounts[n3], a3 = e2.SingleCounts[i2];
                    int o2 = unchecked((int)(a3 - r3));
                    int s2 = n3 >= e2.BandBoundary ? 15 : (int)((uint)n3 >> e2.ShiftDepth), c2 = i2 >= e2.BandBoundary ? 15 : (int)((uint)i2 >> e2.ShiftDepth);
                    t2[s2] = unchecked(t2[s2] + (uint)o2); t2[c2] = unchecked(t2[c2] - (uint)o2);
                    e2.SingleCounts[n3] = (ushort)a3; e2.SingleCounts[i2] = (ushort)r3;
                    ushort l2 = e2.Values[n3]; e2.Values[n3] = e2.Values[i2]; e2.Values[i2] = l2;
                }
            }
            Totals(e2, t2);
        }

        /// <summary>[Bt] l. 1449-1453 (the package's first argument is unused: the alphabet is the second).</summary>
        private static Model NewModel(int t2)
        {
            if (t2 > 8192) throw new GrannyException($"igc arith alphabet {t2} exceeds cap 8192");
            int n2 = t2 + 5 & -4;
            var r2 = new Model { SingleCounts = new ushort[n2], Values = new ushort[n2], UniqueCount = t2 };
            Buckets(r2, t2 + 1);
            Increment(r2, 0, 196611);
            return r2;
        }

        private static void SetEscaped(int escape, int t2, Model n2) { n2.Values[~escape] = (ushort)t2; } // [Y] l. 1460-1462

        /// <summary>[X] l. 1463-1490: a symbol, or an escape (~slot, negative) for a value not seen yet. (The package's
        /// __igcTrace debugging branch is a no-op unless a global trace table is installed, and is left out.)</summary>
        private static int Symbol(Model e2, Arith t2)
        {
            if (e2.CumCounts[15] >= 16384) Rescale(e2);
            int n2 = e2.CumCounts[15];
            int r2 = FindSymbol(e2, GetCount(t2, n2), out int i2);
            int a2 = e2.SingleCounts[r2];
            Remove(t2, i2, a2, n2);
            e2.SingleCounts[r2] = (ushort)(e2.SingleCounts[r2] + 1);
            if (r2 != 0) return e2.Values[r2];
            e2.SinglesLength = e2.SinglesLength + 1 & 65535;
            Increment(e2, e2.SinglesLength, 131074);
            if (e2.SinglesLength == e2.UniqueCount)
            {
                int t3 = e2.SingleCounts[0], n3 = -t3 & 65535;
                Increment(e2, 0, unchecked((uint)((-t3 - 1 & 65535) << 16 | n3)));
            }
            return ~e2.SinglesLength;
        }
    }
}
