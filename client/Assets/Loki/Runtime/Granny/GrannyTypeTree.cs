// =====================================================================================================================
// Project Loki, client wave 4b (w4granny): a C# port, step for step, of granny-ro-js 1.5.0 (npm, MIT;
// https://github.com/stitchuuuu/granny-ro-js), the Granny3D (.gr2) decoder roBrowserLegacy uses (src/Loaders/GR2Loader.js).
// Source read: package/dist/granny-ro.esm.js (a minified bundle). Comments name the package's exported functions and, in
// brackets, the bundle's own minified names; "l. N" are the lines of that bundle reformatted by esbuild
// (client/tools/w4granny/README.md). The JavaScript number rules are kept where they decide a result.
//
// This file: the loaded file (loadGR2 [fe]: every section decompressed, the pointer fixups rewritten as fake pointers
// [ce]/[I]), the Granny type tree (parseTypeTree [R]), objects read through it (parseObject [z], objectStorageSize [be],
// readReferenceArrayObjects [B]), strings [_e]/[ve] and transforms (readTransform [P]).
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
using System.Text;

namespace Loki.Granny
{
    /// <summary>A place in a section: { section, offset } (the package's RefDict / SectionRef).</summary>
    public sealed class GrRef
    {
        public readonly int Section, Offset;
        public GrRef(int section, int offset) { Section = section; Offset = offset; }
        public string Key => Section + ":" + Offset; // the package's `${section}:${offset}` cache keys
    }

    /// <summary>A Granny transform (flags, position, orientation quaternion x y z w, 3x3 scale-shear row-major).</summary>
    public sealed class GrTransform
    {
        public uint Flags;
        public double[] Position, Orientation, ScaleShear;
        public static GrTransform Identity() => new GrTransform
        {
            Flags = 0, Position = new double[] { 0, 0, 0 }, Orientation = new double[] { 0, 0, 0, 1 },
            ScaleShear = new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 },
        };
    }

    /// <summary>One member of a type tree (32 bytes each in the file).</summary>
    public sealed class GrMember
    {
        public uint MemberType;
        public string MemberTypeName, Name;
        public GrRef ReferenceType; // null when the pointer is not a section pointer
        public uint ArrayWidth;
        public uint[] Extra;
        public int Offset;
    }

    /// <summary>
    /// One field of a parsed object (parseObject's per-member record). Value: a number (<see cref="Num"/>), a string
    /// (<see cref="Str"/>) or a transform (<see cref="Xform"/>); <see cref="HasValue"/> says whether the package sets `value`.
    /// </summary>
    public sealed class GrField
    {
        public string Type;
        public int Offset;
        public GrRef ReferenceType, Target, VariantType;
        public bool HasTarget; // `target` set (possibly null)
        public double Num; public bool HasNum;
        public string Str;
        public GrTransform Xform;
        public long Count; public bool HasCount;
        public bool Truncated;
        public List<GrRef> ElementRefs;
        public Dictionary<string, GrField> Inline;
        public bool HasValue => HasNum || Str != null || Xform != null;
    }

    public sealed class GrObjectRecord
    {
        public GrRef Ref;
        public Dictionary<string, GrField> Fields;
    }

    /// <summary>loadGR2's result: the decompressed sections as stored, and a copy with every pointer rewritten.</summary>
    public sealed class GrLoaded
    {
        public GrFile File;
        public byte[][] SectionsOriginal, SectionsFixed;
        public int PointerSize;
    }

    public static class GrannyTypeTree
    {
        // member types [ie] l. 348, storage sizes of the scalar ones [ae] l. 348
        public static readonly Dictionary<uint, string> TypeNames = new Dictionary<uint, string>
        {
            { 0, "end" }, { 1, "inline" }, { 2, "reference" }, { 3, "reference_to_array" }, { 4, "array_of_references" },
            { 5, "variant_reference" }, { 6, "unsupported" }, { 7, "reference_to_variant_array" }, { 8, "string" },
            { 9, "transform" }, { 10, "real32" }, { 11, "int8" }, { 12, "uint8" }, { 13, "binormal_int8" }, { 14, "normal_uint8" },
            { 15, "int16" }, { 16, "uint16" }, { 17, "binormal_int16" }, { 18, "normal_uint16" }, { 19, "int32" }, { 20, "uint32" },
            { 21, "real16" }, { 22, "empty_reference" },
        };
        public static int ScalarSize(uint t)
        {
            switch (t)
            {
                case 10: case 19: case 20: return 4;
                case 11: case 12: case 13: case 14: return 1;
                case 15: case 16: case 17: case 18: case 21: return 2;
                default: return -1;
            }
        }

        private const long FakeBase = 268435456, FakeSection = 1048576; // [oe] [se] l. 348

        /// <summary>makeFakePointer [ce] l. 349-351.</summary>
        public static uint MakeFakePointer(long section, long offset) => Js.ToUint32(FakeBase + section * FakeSection + offset);

        /// <summary>decodeFakePointer [I] l. 352-356: [section, offset], or null for anything that is not one.</summary>
        public static GrRef DecodeFakePointer(double e2, int sectionCount)
        {
            if (e2 < 268435456) return null;
            uint n2 = Js.ToUint32(e2 - FakeBase);
            int r2 = (int)(n2 / FakeSection);
            long i2 = n2 - (long)r2 * FakeSection;
            return r2 < 0 || r2 >= sectionCount ? null : new GrRef(r2, (int)i2);
        }

        /// <summary>The type tree's u32 reader [ue] l. 360-366 (range-checked).</summary>
        private static uint Read(byte[] e3, int n2, bool byteReversed)
        {
            if (n2 < 0 || n2 + 4 > e3.Length) throw new GrannyException($"gr2 type-tree read out of range at 0x{n2:x} (cap 0x{e3.Length:x})");
            return byteReversed ? Js.U32BE(e3, n2) : Js.U32(e3, n2);
        }

        /// <summary>readPointer [L] l. 367-372 (DataView: a read past the end throws).</summary>
        private static double Pointer(byte[] e2, int t2, int n2, bool byteReversed)
        {
            bool le = !byteReversed;
            if (n2 == 4) return Js.U32(e2, t2, le);
            double o2 = Js.U32(e2, t2, le);
            return Js.U32(e2, t2 + 4, le) * 4294967296.0 + o2;
        }

        /// <summary>writePointer [de] l. 373-381.</summary>
        private static void WritePointer(byte[] e2, int t2, uint n2, int r2, bool byteReversed)
        {
            void W(int o, uint v)
            {
                if (o < 0 || o + 4 > e2.Length) throw new GrannyException("Offset is outside the bounds of the DataView");
                if (byteReversed) { e2[o] = (byte)(v >> 24); e2[o + 1] = (byte)(v >> 16); e2[o + 2] = (byte)(v >> 8); e2[o + 3] = (byte)v; }
                else { e2[o] = (byte)v; e2[o + 1] = (byte)(v >> 8); e2[o + 2] = (byte)(v >> 16); e2[o + 3] = (byte)(v >> 24); }
            }
            if (r2 == 4) { W(t2, n2); return; }
            W(t2, n2); W(t2 + 4, 0); // Math.floor(n2 / 2^32) >>> 0 = 0 for a u32 fake pointer
        }

        private struct Fixup { public int SourceSection; public uint SourceOffset, TargetSection, TargetOffset; }

        /// <summary>loadGR2 [fe] l. 382-401 (pointer fixups [pe] l. 402-415, mixed-marshalling table [me] l. 416-429).</summary>
        public static GrLoaded Load(GrFile e2)
        {
            var t2 = e2.Sections; int n2 = t2.Length;
            var r2 = new byte[n2][]; var i2 = new byte[n2][];
            for (int a3 = 0; a3 < n2; a3++)
            {
                var o3 = GrannyFileParser.DecompressSection(t2[a3], e2.SectionBytes(t2[a3]));
                r2[a3] = o3; i2[a3] = (byte[])o3.Clone();
            }
            int a2 = (int)((uint)e2.Header.PointerSize >> 3);
            if (a2 != 4 && a2 != 8) throw new GrannyException($"unsupported pointer size {e2.Header.PointerSize}");
            bool rev = e2.Header.ByteReversed;
            byte[] data = e2.Data;
            var fix = new List<Fixup>();
            for (int e3 = 0; e3 < t2.Length; e3++)
            {
                uint o2 = t2[e3].PointerFixupCount, s2 = t2[e3].PointerFixupOffset;
                if (o2 == 0 || s2 == 0) continue;
                if ((long)s2 + (long)o2 * 12 > data.Length)
                    throw new GrannyException($"section {e3} pointer fixup table at 0x{s2:x} ({o2} entries) escapes file (size 0x{data.Length:x})");
                for (uint k = 0; k < o2; k++)
                {
                    int a3 = (int)(s2 + k * 12);
                    fix.Add(new Fixup { SourceSection = e3, SourceOffset = Read(data, a3, rev), TargetSection = Read(data, a3 + 4, rev), TargetOffset = Read(data, a3 + 8, rev) });
                }
            }
            int mixed = 0;
            for (int e3 = 0; e3 < t2.Length; e3++)
            {
                uint o2 = t2[e3].MixedMarshallingCount, s2 = t2[e3].MixedMarshallingOffset;
                if (o2 == 0 || s2 == 0) continue;
                if ((long)s2 + (long)o2 * 16 > data.Length)
                    throw new GrannyException($"section {e3} mixed-marshalling table at 0x{s2:x} ({o2} entries) escapes file (size 0x{data.Length:x})");
                mixed += (int)o2; // the entries are only counted: the package reads them but uses none
            }
            if (rev && mixed > 0) throw new GrannyException("cross-endian asset (byte_reversed + mixed-marshalling table) is not supported — iRO corpus is all little-endian");
            foreach (var r3 in fix)
            {
                if (r3.SourceSection >= n2) continue;
                var s3 = i2[r3.SourceSection];
                if ((long)r3.SourceOffset + a2 > s3.Length) continue;
                uint c2 = MakeFakePointer(r3.TargetSection, r3.TargetOffset);
                WritePointer(s3, (int)r3.SourceOffset, c2, a2, rev);
            }
            return new GrLoaded { File = e2, SectionsOriginal = r2, SectionsFixed = i2, PointerSize = a2 };
        }

        /// <summary>isPrintable [he] l. 430-438.</summary>
        private static bool IsPrintable(byte[] a, int start, int end)
        {
            if (end - start == 0) return false;
            for (int n2 = start; n2 < end; n2++)
            {
                int t3 = a[n2];
                if (!(t3 == 9 || t3 == 10 || t3 == 13) && (t3 < 32 || t3 == 127)) return false;
            }
            return true;
        }

        // TextDecoder('utf-8', { fatal: false }) [ge] l. 439: invalid sequences become U+FFFD
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, false);

        /// <summary>readString [_e] l. 440-459: a length-prefixed string if it looks like one, else a NUL-terminated one.</summary>
        private static string ReadStringAt(GrLoaded e2, GrRef t2, int n2 = 1024)
        {
            int r2 = t2.Section, i2 = t2.Offset;
            if (r2 >= e2.SectionsOriginal.Length) return "";
            var a2 = e2.SectionsOriginal[r2];
            if (i2 < 0 || i2 >= a2.Length) return "";
            if (i2 + 4 <= a2.Length)
            {
                uint t3 = Read(a2, i2, e2.File.Header.ByteReversed);
                long r3 = i2 + 4L + t3;
                if (t3 > 0 && t3 <= n2 && r3 <= a2.Length && IsPrintable(a2, i2 + 4, (int)r3))
                    return Utf8.GetString(a2, i2 + 4, (int)t3).TrimEnd('\0'); // .replace(/\0+$/, '')
            }
            int o2 = Math.Min(a2.Length, i2 + n2), s2 = o2;
            for (int e3 = i2; e3 < o2; e3++) if (a2[e3] == 0) { s2 = e3; break; }
            return IsPrintable(a2, i2, s2) ? Utf8.GetString(a2, i2, s2 - i2) : "";
        }

        /// <summary>[ve] l. 460-463: the string a fake pointer points at ("" for none).</summary>
        public static string StringAt(GrLoaded e2, double ptr)
        {
            var n2 = DecodeFakePointer(ptr, e2.SectionsOriginal.Length);
            return n2 == null ? "" : ReadStringAt(e2, n2);
        }

        /// <summary>parseTypeTree [R] l. 464-475.</summary>
        public static List<GrMember> ParseTypeTree(GrLoaded e2, GrRef t2, int maxMembers = 512)
        {
            var l2 = new List<GrMember>();
            int i2 = t2.Section, a2 = t2.Offset;
            if (i2 >= e2.SectionsFixed.Length) return l2;
            var o2 = e2.SectionsFixed[i2]; bool rev = e2.File.Header.ByteReversed; int c2 = e2.SectionsFixed.Length;
            int u2 = a2;
            for (int t3 = 0; t3 < maxMembers && !(u2 + 32 > o2.Length); t3++)
            {
                uint t4 = Read(o2, u2, rev);
                if (t4 == 0) break;
                uint n3 = Read(o2, u2 + 4, rev), r3 = Read(o2, u2 + 8, rev), i3 = Read(o2, u2 + 12, rev);
                uint a3 = Read(o2, u2 + 16, rev), d2 = Read(o2, u2 + 20, rev), f2 = Read(o2, u2 + 24, rev);
                string p2 = StringAt(e2, n3); if (p2.Length == 0) p2 = "member_" + l2.Count;
                l2.Add(new GrMember
                {
                    MemberType = t4, MemberTypeName = TypeNames.TryGetValue(t4, out var nm) ? nm : "type_" + t4, Name = p2,
                    ReferenceType = DecodeFakePointer(r3, c2), ArrayWidth = i3 != 0 ? i3 : 1, Extra = new[] { a3, d2, f2 }, Offset = u2,
                });
                u2 += 32;
            }
            return l2;
        }

        public static List<GrMember> ParseTypeTree(GrLoaded e2, uint[] rootType) => ParseTypeTree(e2, new GrRef((int)rootType[0], (int)rootType[1]));

        /// <summary>memberStorageSize [ye] l. 476-479.</summary>
        public static long MemberSize(GrMember e2, int t2)
        {
            long n2 = e2.ArrayWidth != 0 ? e2.ArrayWidth : 1; uint r2 = e2.MemberType; int i2 = ScalarSize(r2);
            if (i2 >= 0) return i2 * n2;
            if (r2 == 2 || r2 == 22 || r2 == 8) return t2;
            if (r2 == 3 || r2 == 4) return 4 + t2;
            if (r2 == 5) return t2 * 2;
            if (r2 == 7) return t2 + 4 + t2;
            if (r2 == 9) return 68;
            if (r2 == 1) return 0;
            return t2;
        }

        private static long Key(GrRef r) => (long)r.Section * FakeSection + r.Offset;

        /// <summary>objectStorageSize [be] l. 480-498.</summary>
        public static long ObjectStorageSize(GrLoaded e2, List<GrMember> t2, int n2, HashSet<long> r2, Dictionary<long, long> i2 = null)
        {
            i2 ??= new Dictionary<long, long>();
            long a2 = 0;
            foreach (var s2 in t2)
            {
                if (s2.MemberType == 1 && s2.ReferenceType != null)
                {
                    long t3 = Key(s2.ReferenceType);
                    if (r2.Contains(t3)) continue;
                    if (i2.TryGetValue(t3, out long memo)) { a2 += memo; continue; }
                    var o3 = new HashSet<long>(r2) { t3 };
                    long c2 = ObjectStorageSize(e2, ParseTypeTree(e2, s2.ReferenceType), n2, o3, i2);
                    i2[t3] = c2; a2 += c2;
                }
                else a2 += MemberSize(s2, n2);
            }
            return a2;
        }

        /// <summary>parseObject [z] l. 499-545.</summary>
        public static Dictionary<string, GrField> ParseObject(GrLoaded e2, List<GrMember> t2, GrRef n2, int maxArrayRefs = 256, int i2 = 0, HashSet<long> a2 = null)
        {
            var _2 = new Dictionary<string, GrField>();
            int o2 = maxArrayRefs, s2 = e2.PointerSize; bool c2 = e2.File.Header.ByteReversed;
            int l2 = n2.Section, u2 = n2.Offset;
            if (l2 >= e2.SectionsFixed.Length) return _2;
            var d2 = e2.SectionsFixed[l2]; var f2 = e2.SectionsOriginal[l2]; int p2 = e2.SectionsFixed.Length;
            bool g2 = !c2;
            long v2 = u2;
            foreach (var u3 in t2)
            {
                long y2 = MemberSize(u3, s2);
                if (v2 + y2 > d2.Length) break;
                int at = (int)v2;
                var b2 = new GrField { Type = u3.MemberTypeName, Offset = at };
                if (u3.ReferenceType != null) b2.ReferenceType = u3.ReferenceType;
                uint x2 = u3.MemberType;
                if (x2 == 2 || x2 == 22) { b2.Target = DecodeFakePointer(Pointer(d2, at, s2, c2), p2); b2.HasTarget = true; }
                else if (x2 == 8)
                {
                    double t3 = Pointer(d2, at, s2, c2);
                    b2.Str = StringAt(e2, t3); b2.Target = DecodeFakePointer(t3, p2); b2.HasTarget = true;
                }
                else if (x2 == 3 || x2 == 4)
                {
                    uint t3 = Read(f2, at, c2);
                    var n4 = DecodeFakePointer(Pointer(d2, at + 4, s2, c2), p2);
                    b2.Count = t3; b2.HasCount = true; b2.Target = n4; b2.HasTarget = true; b2.Truncated = t3 > o2;
                    if (x2 == 4) b2.ElementRefs = ElementRefs(e2, n4, t3, o2);
                }
                else if (x2 == 7)
                {
                    double e3 = Pointer(d2, at, s2, c2); uint t3 = Read(f2, at + s2, c2); double n4 = Pointer(d2, at + s2 + 4, s2, c2);
                    b2.VariantType = DecodeFakePointer(e3, p2); b2.Count = t3; b2.HasCount = true;
                    b2.Target = DecodeFakePointer(n4, p2); b2.HasTarget = true; b2.Truncated = t3 > o2;
                }
                else if (x2 == 5)
                {
                    double e3 = Pointer(d2, at, s2, c2), t3 = Pointer(d2, at + s2, s2, c2);
                    b2.VariantType = DecodeFakePointer(e3, p2); b2.Target = DecodeFakePointer(t3, p2); b2.HasTarget = true;
                }
                else if (x2 == 19) { b2.Num = (int)Js.U32(f2, at, g2); b2.HasNum = true; }
                else if (x2 == 20) { b2.Num = Js.U32(f2, at, g2); b2.HasNum = true; }
                else if (x2 == 15) { b2.Num = (short)U16(f2, at, g2); b2.HasNum = true; }
                else if (x2 == 16) { b2.Num = U16(f2, at, g2); b2.HasNum = true; }
                else if (x2 == 11) { b2.Num = (sbyte)f2[at]; b2.HasNum = true; }
                else if (x2 == 12) { b2.Num = f2[at]; b2.HasNum = true; }
                else if (x2 == 10) { b2.Num = F32(f2, at, g2); b2.HasNum = true; }
                else if (x2 == 9) b2.Xform = ReadTransform(e2, l2, at);
                else if (x2 == 1 && u3.ReferenceType != null)
                {
                    long t3 = Key(u3.ReferenceType);
                    if (i2 >= 64) throw new GrannyException($"inline nesting exceeds MAX_INLINE_DEPTH (64) at section {l2} offset 0x{v2:x}");
                    if (a2 != null && a2.Contains(t3))
                        throw new GrannyException($"self-referential inline type [{u3.ReferenceType.Section}, {u3.ReferenceType.Offset}] at section {l2} offset 0x{v2:x}");
                    var n4 = a2 == null ? new HashSet<long>() : new HashSet<long>(a2);
                    n4.Add(t3);
                    var o3 = ParseTypeTree(e2, u3.ReferenceType);
                    b2.Inline = ParseObject(e2, o3, new GrRef(l2, at), maxArrayRefs, i2 + 1, n4);
                    long c3 = ObjectStorageSize(e2, o3, s2, new HashSet<long>());
                    v2 += c3; _2[u3.Name] = b2;
                    continue;
                }
                _2[u3.Name] = b2; v2 += y2;
            }
            return _2;
        }

        private static ushort U16(byte[] b, int o, bool le)
        {
            if (o < 0 || o + 2 > b.Length) throw new GrannyException("Offset is outside the bounds of the DataView");
            return le ? Js.U16(b, o) : (ushort)(b[o] << 8 | b[o + 1]);
        }
        private static float F32(byte[] b, int o, bool le) => BitConverter.Int32BitsToSingle((int)Js.U32(b, o, le));

        /// <summary>readArrayOfReferences [xe] l. 546-558: the element pointers of an array of references (nulls dropped).</summary>
        private static List<GrRef> ElementRefs(GrLoaded e2, GrRef t2, long n2, int r2)
        {
            var l2 = new List<GrRef>();
            if (t2 == null || n2 <= 0) return l2;
            int i2 = e2.PointerSize; bool a2 = e2.File.Header.ByteReversed;
            if (t2.Section >= e2.SectionsFixed.Length) return l2;
            var o2 = e2.SectionsFixed[t2.Section];
            int s2 = e2.SectionsFixed.Length; long c2 = n2 < r2 ? n2 : r2;
            for (long e3 = 0; e3 < c2; e3++)
            {
                long n3 = t2.Offset + e3 * i2;
                if (n3 + i2 > o2.Length) break;
                var r3 = DecodeFakePointer(Pointer(o2, (int)n3, i2, a2), s2);
                if (r3 != null) l2.Add(r3);
            }
            return l2;
        }

        /// <summary>readReferenceArrayObjects [B] l. 559-569: count objects of one type laid out one after the other.</summary>
        public static List<GrObjectRecord> ReadReferenceArrayObjects(GrLoaded e2, GrRef t2, long n2, GrRef r2, int maxCount = 64, int maxArrayRefs = -1)
        {
            var u2 = new List<GrObjectRecord>();
            if (t2 == null || r2 == null || n2 <= 0) return u2;
            int a2 = maxCount, o2 = maxArrayRefs >= 0 ? maxArrayRefs : a2;
            var s2 = ParseTypeTree(e2, r2);
            long c2 = ObjectStorageSize(e2, s2, e2.PointerSize, new HashSet<long>());
            if (c2 <= 0) return u2;
            long l2 = n2 < a2 ? n2 : a2;
            for (long n3 = 0; n3 < l2; n3++)
            {
                long r3 = t2.Offset + n3 * c2;
                var at = new GrRef(t2.Section, (int)r3);
                u2.Add(new GrObjectRecord { Ref = at, Fields = ParseObject(e2, s2, at, o2) });
            }
            return u2;
        }

        /// <summary>readTransform [P] l. 327-333: flags + 16 floats (position 3, orientation 4, scale-shear 9); identity when out of range.</summary>
        public static GrTransform ReadTransform(GrLoaded e2, int t2, int n2)
        {
            var r2 = t2 >= 0 && t2 < e2.SectionsOriginal.Length ? e2.SectionsOriginal[t2] : null;
            if (r2 == null || n2 < 0 || n2 + 68 > r2.Length) return GrTransform.Identity();
            var o2 = new double[16];
            for (int e3 = 0; e3 < 16; e3++) o2[e3] = Js.F32(r2, n2 + 4 + e3 * 4);
            return new GrTransform
            {
                Flags = Js.U32(r2, n2),
                Position = new[] { o2[0], o2[1], o2[2] }, Orientation = new[] { o2[3], o2[4], o2[5], o2[6] },
                ScaleShear = new[] { o2[7], o2[8], o2[9], o2[10], o2[11], o2[12], o2[13], o2[14], o2[15] },
            };
        }

        /// <summary>[Te] l. 593-599: count floats (little-endian) from the stored section, or none when out of range.</summary>
        public static double[] ReadFloats(GrLoaded e2, int t2, int n2, int r2)
        {
            var i2 = t2 >= 0 && t2 < e2.SectionsOriginal.Length ? e2.SectionsOriginal[t2] : null;
            if (i2 == null || n2 < 0 || n2 + r2 * 4 > i2.Length) return new double[0];
            var o2 = new double[r2];
            for (int e3 = 0; e3 < r2; e3++) o2[e3] = Js.F32(i2, n2 + e3 * 4);
            return o2;
        }

        /// <summary>[Ye] l. 839-842: a slice of the stored section, or null when out of range.</summary>
        public static byte[] Slice(GrLoaded e2, int t2, long n2, long r2)
        {
            var i2 = t2 >= 0 && t2 < e2.SectionsOriginal.Length ? e2.SectionsOriginal[t2] : null;
            if (i2 == null || n2 < 0 || n2 + r2 > i2.Length) return null;
            var b = new byte[r2]; Buffer.BlockCopy(i2, (int)n2, b, 0, (int)r2); return b;
        }

        // ------------------------------------------------------------------ small accessors for `x?.value` / `x?.count` / …
        public static GrField F(Dictionary<string, GrField> d, string name) => d != null && d.TryGetValue(name, out var f) ? f : null;
        /// <summary>`typeof x?.value == 'string' && x.value ? x.value : fallback`.</summary>
        public static string NameOr(Dictionary<string, GrField> d, string name, string fallback)
        {
            var f = F(d, name);
            return f != null && !string.IsNullOrEmpty(f.Str) ? f.Str : fallback;
        }
        /// <summary>`x?.value ?? fallback` for a numeric member (a string / transform value is not a number: fallback).</summary>
        public static double NumOr(Dictionary<string, GrField> d, string name, double fallback)
        {
            var f = F(d, name);
            return f != null && f.HasNum ? f.Num : fallback;
        }
        public static long CountOr0(GrField f) => f != null && f.HasCount ? f.Count : 0;
    }
}
