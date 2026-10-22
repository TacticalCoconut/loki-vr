// =====================================================================================================================
// Project Loki, client wave 4b (w4granny): a C# port, step for step, of granny-ro-js 1.5.0 (npm, MIT;
// https://github.com/stitchuuuu/granny-ro-js), the Granny3D (.gr2) decoder roBrowserLegacy uses (src/Loaders/GR2Loader.js).
// Source read: package/dist/granny-ro.esm.js (a minified bundle). Comments name the package's exported functions and, in
// brackets, the bundle's own minified names ([g], [f] …); "l. N" are the lines of that bundle reformatted by esbuild
// (client/tools/w4granny/README.md says how to make the same copy). The JavaScript number rules are kept where they
// decide a result: doubles where the package computes with Numbers, ToInt32 / ToUint32 where it applies | 0 / >>> 0.
//
// This file: the GR2 header and section table (parseGR2File [g], detectGR2 [f]) and the section decompression dispatcher
// (decompressSection [re] / [Kn]); the JavaScript number helpers shared by the port (Js).
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
//
// PORTIONS DERIVED FROM Rasetsuu/blendergranny: the package's GR2 header / section table parser (this file) is its port of
// https://github.com/Rasetsuu/blendergranny, MIT License, Copyright (c) 2026 ciupix and contributors (the same permission
// notice as above; the full text is in THIRD_PARTY.md and GrannyLicense.cs).
// =====================================================================================================================
using System;

namespace Loki.Granny
{
    /// <summary>A .gr2 that the port refuses (the package throws Error / RangeError / TypeError at the same places).</summary>
    public sealed class GrannyException : Exception
    {
        public GrannyException(string message) : base(message) { }
    }

    /// <summary>ECMAScript number conversions (ES2023 §7.1.6 ToInt32, §7.1.7 ToUint32) and little-endian reads.</summary>
    internal static class Js
    {
        public static int ToInt32(double d)
        {
            if (d >= int.MinValue && d <= int.MaxValue) return (int)d; // truncation towards zero, the common case
            if (double.IsNaN(d) || double.IsInfinity(d)) return 0;
            double t = Math.Truncate(d) % 4294967296.0;
            if (t < 0) t += 4294967296.0;
            return unchecked((int)(uint)t);
        }
        public static uint ToUint32(double d) => unchecked((uint)ToInt32(d));
        public static int ToInt32(long v) => unchecked((int)v); // an integral value: its low 32 bits
        public static uint ToUint32(long v) => unchecked((uint)v);

        public static uint U32(byte[] b, int o) => (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24);
        public static uint U32BE(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
        public static ushort U16(byte[] b, int o) => (ushort)(b[o] | b[o + 1] << 8);
        public static float F32(byte[] b, int o) => BitConverter.Int32BitsToSingle((int)U32(b, o));
        /// <summary>DataView.getUint32(o, littleEndian) with DataView's own range check.</summary>
        public static uint U32(byte[] b, int o, bool littleEndian)
        {
            if (o < 0 || o + 4 > b.Length) throw new GrannyException("Offset is outside the bounds of the DataView");
            return littleEndian ? U32(b, o) : U32BE(b, o);
        }
    }

    /// <summary>One entry of the section table (44 bytes each).</summary>
    public sealed class GrSection
    {
        public int Index;
        public uint Compression, DataOffset, DataSize, ExpandedSize, InternalAlignment, First16Bit, First8Bit,
            PointerFixupOffset, PointerFixupCount, MixedMarshallingOffset, MixedMarshallingCount;
    }

    public sealed class GrHeader
    {
        public uint Version, TotalSize, Crc, SectionArrayOffset, SectionCount, TypeTag, StringDbCrc;
        public uint[] RootType, RootObject, ExtraTags, Reserved;
        public int PointerSize;
        public bool ByteReversed;
    }

    /// <summary>parseGR2File's result: the header, the section table and the file bytes.</summary>
    public sealed class GrFile
    {
        public GrHeader Header;
        public GrSection[] Sections;
        public byte[] Data;

        /// <summary>sectionBytes(section) [g] l. 62-66: the section's stored bytes (a copy of data[data_offset, +data_size)).</summary>
        public byte[] SectionBytes(GrSection s)
        {
            long t3 = s.DataOffset, r3 = t3 + s.DataSize;
            if (t3 < 0 || r3 > Data.Length) throw new GrannyException($"section {s.Index} points outside file");
            var b = new byte[s.DataSize];
            Buffer.BlockCopy(Data, (int)t3, b, 0, (int)s.DataSize);
            return b;
        }
    }

    public static class GrannyFileParser
    {
        // the magic values of the four header layouts [e t n r i] l. 1
        private static readonly uint[] MagicLe32 = { 3400558520, 263286264, 2123133572, 503322974 };
        private static readonly uint[] MagicLe32b = { 3228360233, 726901946, 2780296485, 4007814902 };
        private static readonly uint[] MagicBe32 = { 3046445326, 1270195562, 1344809195, 78870565 };
        private static readonly uint[] MagicLe64 = { 1581882341, 337601391, 2850755358, 3303915152 };
        private static readonly uint[] MagicBe64 = { 3822359857, 1649400864, 986724044, 2315223729 };

        private static uint Swap(uint t) => (t & 255) << 24 | (t & 65280) << 8 | (t & 16711680) >> 8 | (t & 4278190080) >> 24; // [l]
        private static bool Eq(uint[] a, uint[] b) { for (int i = 0; i < 4; i++) if (a[i] != b[i]) return false; return true; } // [u]

        /// <summary>detectGR2 [f] l. 18-26: the byte order and pointer size from the 16-byte magic.</summary>
        public static bool Detect(byte[] data, out bool byteReversed, out int pointerSize)
        {
            byteReversed = false; pointerSize = 0;
            if (data.Length < 32) return false;
            var s = new[] { Js.U32(data, 0), Js.U32(data, 4), Js.U32(data, 8), Js.U32(data, 12) };
            if (Eq(s, MagicLe32) || Eq(s, MagicLe32b)) { pointerSize = 32; return true; }
            if (Eq(s, MagicLe64)) { pointerSize = 64; return true; }
            var c = new[] { Swap(s[0]), Swap(s[1]), Swap(s[2]), Swap(s[3]) };
            if (Eq(c, MagicLe32) || Eq(c, MagicBe32)) { byteReversed = true; pointerSize = 32; return true; }
            if (Eq(c, MagicBe64)) { byteReversed = true; pointerSize = 64; return true; }
            return false;
        }

        /// <summary>parseGR2File [g] l. 46-67.</summary>
        public static GrFile Parse(byte[] data)
        {
            if (!Detect(data, out bool rev, out int ptr)) throw new GrannyException("not a Granny2 file");
            bool le = !rev;
            uint R(int o) // [h] l. 39-45
            {
                if (o < 0 || o + 4 > data.Length) throw new GrannyException($"gr2 read out of range at 0x{o:x} (cap 0x{data.Length:x})");
                return le ? Js.U32(data, o) : Js.U32BE(data, o);
            }
            var h = new GrHeader
            {
                Version = R(32), TotalSize = R(36), Crc = R(40), SectionArrayOffset = R(44), SectionCount = R(48),
                RootType = new[] { R(52), R(56) }, RootObject = new[] { R(60), R(64) }, TypeTag = R(68),
                ExtraTags = new[] { R(72), R(76), R(80), R(84) }, StringDbCrc = 0, Reserved = new uint[0],
                PointerSize = ptr, ByteReversed = rev,
            };
            if (h.Version >= 7) { h.StringDbCrc = R(88); h.Reserved = new[] { R(92), R(96), R(100) }; }
            long c2 = 32L + h.SectionArrayOffset;
            // the package allocates Array(section_count) and throws a RangeError at the first entry outside the file; a count
            // that cannot fit is refused here before allocating (same outcome on any input, no 4-GB array)
            if (h.SectionCount > 0 && c2 + (long)(h.SectionCount - 1) * 44 + 4 > data.Length)
                throw new GrannyException($"gr2 read out of range at 0x{c2 + (long)(h.SectionCount - 1) * 44:x} (cap 0x{data.Length:x})");
            var secs = new GrSection[h.SectionCount];
            for (int e3 = 0; e3 < secs.Length; e3++)
            {
                int t3 = (int)(c2 + e3 * 44);
                secs[e3] = new GrSection
                {
                    Index = e3, Compression = R(t3), DataOffset = R(t3 + 4), DataSize = R(t3 + 8), ExpandedSize = R(t3 + 12),
                    InternalAlignment = R(t3 + 16), First16Bit = R(t3 + 20), First8Bit = R(t3 + 24), PointerFixupOffset = R(t3 + 28),
                    PointerFixupCount = R(t3 + 32), MixedMarshallingOffset = R(t3 + 36), MixedMarshallingCount = R(t3 + 40),
                };
            }
            return new GrFile { Header = h, Sections = secs, Data = data };
        }

        /// <summary>decompressSection [re] l. 339-347 (= [Kn] l. 1965-1973): stored (0) or Oodle0 (1); anything else is refused.</summary>
        public static byte[] DecompressSection(GrSection s, byte[] stored)
        {
            if (s.ExpandedSize == 0) return new byte[0];
            if (s.Compression == 0)
            {
                if (stored.Length < s.ExpandedSize) throw new GrannyException($"section {s.Index} short: {stored.Length} < {s.ExpandedSize}");
                if (stored.Length == s.ExpandedSize) return stored;
                var b = new byte[s.ExpandedSize]; Buffer.BlockCopy(stored, 0, b, 0, b.Length); return b;
            }
            if (s.Compression == 1) return Oodle0.Decompress(s, stored);
            throw new GrannyException($"unsupported compression {s.Compression}");
        }
    }
}
