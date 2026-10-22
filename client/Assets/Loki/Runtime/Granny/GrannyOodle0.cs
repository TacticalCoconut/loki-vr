// =====================================================================================================================
// Project Loki, client wave 4b (w4granny): a C# port, step for step, of granny-ro-js 1.5.0 (npm, MIT;
// https://github.com/stitchuuuu/granny-ro-js), the Granny3D (.gr2) decoder roBrowserLegacy uses (src/Loaders/GR2Loader.js).
// Source read: package/dist/granny-ro.esm.js (a minified bundle). Comments name the package's exported functions and, in
// brackets, the bundle's own minified names; "l. N" are the lines of that bundle reformatted by esbuild
// (client/tools/w4granny/README.md). The JavaScript number rules are kept where they decide a result.
//
// This file: Oodle0 decompression (decompressOodle0 [M], parseOodle0Plan [C], the block loop [ne], the adaptive models [A],
// the range decoder [D], the bit reader [E]) — every section of the RO client's .gr2 files is compressed with it.
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
// PORTIONS DERIVED FROM Rasetsuu/blendergranny: the package's Oodle0 decompressor (this file) is its port of the clean-room
// Python implementation in https://github.com/Rasetsuu/blendergranny, MIT License, Copyright (c) 2026 ciupix and
// contributors (the same permission notice as above; the full text is in THIRD_PARTY.md and GrannyLicense.cs).
// =====================================================================================================================
using System;

namespace Loki.Granny
{
    public static class Oodle0
    {
        private const uint RangeMax = 2147483647;            // [v] l. 68
        private static readonly int[] LongLengths = { 128, 192, 256, 512 }; // [_] l. 68

        /// <summary>One block header of the 36-byte plan [b] l. 73-93.</summary>
        private sealed class BlockHeader
        {
            public readonly uint MaxOffsetAndByte, UniqOffsetAndByte, UniqLens;
            public BlockHeader(uint a, uint b, uint c) { MaxOffsetAndByte = a; UniqOffsetAndByte = b; UniqLens = c; }
            public uint MaxByteValue => MaxOffsetAndByte & 511;
            public uint MaxOffset => MaxOffsetAndByte >> 9;
            public uint UniqueByteValues => UniqOffsetAndByte & 511;
            public uint UniqueOffsets => UniqOffsetAndByte >> 9;
            public uint LengthUnique(int e2) { int t2 = Math.Min(e2 / (64 / 4), 3); return UniqLens >> ((3 - t2) * 8) & 255; }
        }

        /// <summary>u32lePadded [w] l. 112-117: a little-endian u32, zero past the end.</summary>
        private static uint U32Padded(byte[] e2, int t2)
        {
            int n2 = e2.Length;
            if (t2 + 4 <= n2) return Js.U32(e2, t2);
            uint r2 = 0;
            if (t2 < n2) r2 |= e2[t2];
            if (t2 + 1 < n2) r2 |= (uint)e2[t2 + 1] << 8;
            if (t2 + 2 < n2) r2 |= (uint)e2[t2 + 2] << 16;
            if (t2 + 3 < n2) r2 |= (uint)e2[t2 + 3] << 24;
            return r2;
        }

        /// <summary>bitReverse [T] l. 118-122.</summary>
        public static uint BitReverse(uint e2, int t2)
        {
            uint n2 = 0;
            for (int r2 = 0; r2 < t2; r2++) n2 = n2 << 1 | e2 >> r2 & 1;
            return n2;
        }

        /// <summary>The LSB-first bit reader [E] l. 123-146.</summary>
        private sealed class BitReader
        {
            private readonly byte[] _data; private int _cur; private uint _bits; private int _bitlen;
            public BitReader(byte[] data, int cur) { _data = data; _cur = cur; }
            public uint Get(int e2)
            {
                if (e2 == 0) return 0;
                uint t2 = (1u << e2) - 1; // (1 << e2 >>> 0) - 1: e2 ≤ 31 here
                if (_bitlen >= e2)
                {
                    uint n3 = _bits & t2;
                    _bits >>= e2; _bitlen -= e2;
                    return n3;
                }
                uint n2 = U32Padded(_data, _cur);
                _cur += 4;
                int r2 = e2 - _bitlen;
                uint i2 = n2 & (1u << r2) - 1, a2 = _bits | i2 << _bitlen;
                _bits = n2 >> r2; _bitlen = _bitlen + 32 - e2;
                return a2;
            }
            public uint Get1()
            {
                if (_bitlen != 0) { uint e3 = _bits & 1; _bits >>= 1; --_bitlen; return e3; }
                uint e2 = U32Padded(_data, _cur);
                _cur += 4; _bits = e2 >> 1; _bitlen = 31;
                return e2 & 1;
            }
        }

        /// <summary>The range decoder [D] l. 147-174. high / low / code stay below 2^31 (masked by RangeMax).</summary>
        private sealed class RangeDecoder
        {
            private readonly BitReader _v; private uint _high, _low, _code;
            public RangeDecoder(byte[] data, int cur)
            {
                _v = new BitReader(data, cur); _high = RangeMax; _low = 0; _code = BitReverse(_v.Get(31), 31);
            }
            public int GetCount(int e2)
            {
                if (e2 <= 0) return 0;
                double t2 = (double)_high - _low + 1;
                return (int)Math.Floor((((double)_code - _low + 1) * e2 - 1) / t2);
            }
            public int GetValue(int e2)
            {
                int t2 = GetCount(e2);
                if (t2 >= e2) t2 = e2 - 1;
                Remove(t2, 1, e2);
                return t2;
            }
            public void Remove(int e2, int t2, int n2)
            {
                if (n2 <= 0) return;
                uint r2 = _high, i2 = _low, a2 = _code;
                double o2 = (double)r2 - i2 + 1;
                r2 = Js.ToUint32(i2 + Math.Floor(o2 * ((double)e2 + t2) / n2) - 1);
                i2 = Js.ToUint32(i2 + Math.Floor(o2 * e2 / n2));
                if (((r2 ^ i2) & 1073741824) == 0)
                {
                    while (((r2 ^ i2) & 2139095040) == 0)
                    {
                        i2 = i2 << 8; r2 = r2 << 8 | 255;
                        uint e3 = _v.Get(8);
                        a2 = a2 << 8 | BitReverse(e3 & 15, 4) << 4 | BitReverse(e3 >> 4, 4);
                    }
                    if (((r2 ^ i2) & 2013265920) == 0) { i2 = i2 << 4; r2 = r2 << 4 | 15; a2 = a2 << 4 | BitReverse(_v.Get(4), 4); }
                    while (((r2 ^ i2) & 1073741824) == 0) { i2 = i2 << 1; r2 = r2 << 1 | 1; a2 = a2 << 1 | _v.Get1(); }
                }
                while ((i2 & 536870912) != 0 && (r2 & 536870912) == 0)
                {
                    a2 ^= 536870912; i2 = (i2 & 536870911) << 1; r2 = r2 << 1 | 1073741825; a2 = a2 << 1 | _v.Get1();
                }
                _high = r2 & RangeMax; _low = i2 & RangeMax; _code = a2 & RangeMax;
            }
        }

        /// <summary>alignedCount [ee] l. 175-177.</summary>
        private static int AlignedCount(int e2) => e2 + 5 & -4;

        /// <summary>bestShift [O] l. 178-187: [bin_size, bin_shift, last_bin_start].</summary>
        private static void BestShift(long e2, out int size, out int shift, out int lastStart)
        {
            if (e2 < 6) { size = 0; shift = 15; lastStart = 0; return; }
            long t2 = 4294967295; int n2 = 0;
            for (int r3 = 0; r3 < 16; r3++)
            {
                long i2 = 1L << r3;
                long a2 = e2 - i2 * (Math.Min((e2 + i2 - 1) / i2, 16) - 1);
                if (a2 < i2) a2 = i2;
                if (a2 < t2) { n2 = r3; t2 = a2; }
                if (i2 > e2) break;
            }
            size = 1 << n2; shift = n2; lastStart = 15 * size;
        }

        /// <summary>The adaptive frequency model [A] l. 192-280. An escape (a new symbol) is returned as ~slot (negative).</summary>
        private sealed class Model
        {
            private readonly int _unique;
            private readonly int[] _totals = new int[16], _counts, _values;
            private int _number, _binSize, _binShift, _lastBinStart;
            public Model(uint e2)
            {
                if (e2 > 2097152) throw new GrannyException($"oodle0 arith alphabet {e2} exceeds cap 2097152");
                _unique = (int)e2;
                int t2 = AlignedCount(_unique);
                _counts = new int[t2]; _values = new int[t2]; _number = 0;
                BestShift(_unique + 1, out _binSize, out _binShift, out _lastBinStart);
                QuickIncrement(0, 3);
            }
            public int Decompress(RangeDecoder e2)
            {
                if (_totals[15] >= 16384) Rescale();
                int t2 = _totals[15], n2 = e2.GetCount(t2);
                FindPos(n2, out int r2, out int i2);
                int a2 = _counts[r2];
                IncrementTotals(r2, 1);
                e2.Remove(i2, a2, _totals[15] - 1);
                _counts[r2] = _counts[r2] + 1 & 65535;
                if (r2 == 0)
                {
                    _number += 1;
                    if (_number >= _counts.Length) throw new GrannyException("oodle0 escape exceeded model capacity");
                    QuickIncrement(_number, 2);
                    if (_number == _unique) DecrementCounts(0, _counts[0]);
                    return ~_number;
                }
                return _values[r2];
            }
            public void SetEscaped(int escape, int t2) { _values[~escape] = t2 & 65535; }
            private void FindPos(int e2, out int pos, out int low)
            {
                int[] t2 = _totals; int n2 = 0, r2 = 16;
                while (n2 < r2) { int i3 = (int)((uint)(n2 + r2) >> 1); if (e2 < t2[i3]) r2 = i3; else n2 = i3 + 1; }
                int i2 = n2, s2, c2; int[] a2 = _counts; int o2 = _binShift;
                if (i2 < 15) { s2 = i2 << o2; c2 = s2 + (1 << o2); if (c2 > a2.Length) c2 = a2.Length; }
                else { s2 = _lastBinStart; c2 = a2.Length; }
                int l2 = i2 > 0 ? t2[i2 - 1] : 0; // i2 == 16 reads totals[15], as the package does
                while (s2 < c2)
                {
                    int t3 = a2[s2];
                    if (e2 < l2 + t3) { pos = s2; low = l2; return; }
                    l2 += t3; s2++;
                }
                throw new GrannyException($"oodle0 model count {e2} outside total {l2}");
            }
            private void QuickIncrement(int e2, int t2) { IncrementTotals(e2, t2); _counts[e2] = _counts[e2] + t2 & 65535; }
            private void IncrementTotals(int e2, int t2)
            {
                if (e2 >= _lastBinStart) { _totals[15] = _totals[15] + t2 & 65535; return; }
                for (int r2 = (int)((uint)e2 >> _binShift); r2 < 16; r2++) _totals[r2] = _totals[r2] + t2 & 65535;
            }
            private void DecrementCounts(int e2, int t2)
            {
                _counts[e2] = _counts[e2] - t2 & 65535;
                if (e2 >= _lastBinStart) { _totals[15] = _totals[15] - t2 & 65535; return; }
                for (int r2 = (int)((uint)e2 >> _binShift); r2 < 16; r2++) _totals[r2] = _totals[r2] - t2 & 65535;
            }
            private void Rescale()
            {
                BestShift(_number + 1, out _binSize, out _binShift, out _lastBinStart);
                var r2 = new long[16];
                _counts[0] = (int)((uint)_counts[0] >> 1);
                r2[0 < _lastBinStart ? 0 : 15] += _counts[0];
                int i2 = 0, a2 = 0, o2 = 1; bool s2 = false;
                while (o2 <= _number && !s2)
                {
                    while (_counts[o2] <= 1)
                    {
                        if (o2 < _number)
                        {
                            _counts[o2] = _counts[_number]; _values[o2] = _values[_number]; _counts[_number] = 0; --_number;
                        }
                        else { _counts[o2] = 0; --_number; s2 = true; break; }
                    }
                    if (s2) break;
                    _counts[o2] = (int)((uint)_counts[o2] >> 1);
                    if (_counts[o2] > i2) { i2 = _counts[o2]; a2 = o2; }
                    int e3 = o2 < _lastBinStart ? (int)((uint)o2 >> _binShift) : 15;
                    r2[e3] += _counts[o2]; o2 += 1;
                }
                if (i2 != 0)
                {
                    int e3 = _number < _lastBinStart ? (int)((uint)_number >> _binShift) << _binShift : _lastBinStart;
                    if (e3 == 0) e3 = 1;
                    if (a2 != e3)
                    {
                        int t3 = _counts[e3];
                        _counts[e3] = _counts[a2];
                        int n3 = e3 < _lastBinStart ? (int)((uint)e3 >> _binShift) : 15, i3 = a2 < _lastBinStart ? (int)((uint)a2 >> _binShift) : 15;
                        r2[n3] += -t3 + _counts[e3]; r2[i3] += t3 - _counts[e3]; _counts[a2] = t3;
                        int o3 = _values[e3]; _values[e3] = _values[a2]; _values[a2] = o3;
                    }
                }
                if (_number != _unique && _counts[0] == 0) { _counts[0] = _counts[0] + 2 & 65535; r2[0 < _lastBinStart ? 0 : 15] += 2; }
                long c2 = 0;
                for (int e3 = 0; e3 < 16; e3++) { c2 += r2[e3]; _totals[e3] = Js.ToInt32(c2) & 65535; }
            }
        }

        /// <summary>The per-block model set [te] l. 280-287.</summary>
        private sealed class BlockModels
        {
            public readonly int MaxBytes, MaxOffsets, MaxOffsetLow;
            public readonly Model Bytes, OffsetLow, OffsetHigh;
            public readonly Model[] Lengths = new Model[65];
            public int BytesDecompressed, LastLength;
            public BlockModels(BlockHeader e2)
            {
                MaxBytes = (int)e2.MaxByteValue; MaxOffsets = (int)e2.MaxOffset; MaxOffsetLow = Math.Min(MaxOffsets, 4);
                Bytes = new Model(e2.UniqueByteValues);
                for (int n2 = 0; n2 <= 64; n2++) Lengths[n2] = new Model(e2.LengthUnique(n2));
                OffsetLow = new Model((uint)MaxOffsetLow); OffsetHigh = new Model(e2.UniqueOffsets);
            }
        }

        /// <summary>[j] l. 288-295: a symbol, or an escape read as a plain value below escapeRange and remembered.</summary>
        private static int Symbol(Model e2, RangeDecoder t2, int n2)
        {
            int r2 = e2.Decompress(t2);
            if (r2 < 0) { int i2 = t2.GetValue(n2); e2.SetEscaped(r2, i2); return i2; }
            return r2;
        }

        /// <summary>decompressOodle0 [M] l. 296-308 (with parseOodle0Plan [C] l. 101-111 and blockStops [x] l. 94-100).</summary>
        public static byte[] Decompress(GrSection e2, byte[] t2)
        {
            if (e2.ExpandedSize == 0) return new byte[0];
            if (t2.Length < 36) throw new GrannyException("oodle0 data too short for 3 block headers");
            long n2 = e2.ExpandedSize;
            if (n2 > 268435456 || n2 > (long)t2.Length * 1024)
                throw new GrannyException($"oodle0 expanded_size {n2} exceeds cap (268435456 B / 1024× of {t2.Length} B)");
            // parseOodle0Plan: three block headers, the block ends from first_16bit / first_8bit (clampStop)
            var hdr = new BlockHeader[3];
            for (int k = 0; k < 3; k++) hdr[k] = new BlockHeader(Js.U32(t2, k * 12), Js.U32(t2, k * 12 + 4), Js.U32(t2, k * 12 + 8));
            uint exp = e2.ExpandedSize;
            uint s16 = Clamp(e2.First16Bit, exp), s8 = Clamp(e2.First8Bit, exp);
            if (s8 < s16) s8 = s16;
            uint[] stops = { 0, s16, s8, exp };
            var i2 = new RangeDecoder(t2, 36);
            var a2 = new byte[exp];
            int o2 = 0;
            for (int e3 = 0; e3 < 3; e3++)
            {
                if (stops[e3 + 1] <= stops[e3]) continue; // is_empty
                o2 = DecodeBlock(new BlockModels(hdr[e3]), i2, a2, o2, (int)stops[e3 + 1]);
            }
            if (o2 != exp) throw new GrannyException($"oodle0 decompressed {o2}, expected {exp}");
            return a2;
        }

        private static uint Clamp(uint e2, uint t2) => e2 <= 0 ? 0 : e2 >= t2 ? t2 : e2; // clampStop [S] l. 98-100

        /// <summary>The block loop [ne] l. 309-325. Writes past the output are dropped, as a Uint8Array drops them.</summary>
        private static int DecodeBlock(BlockModels e2, RangeDecoder t2, byte[] n2, int r2, int i2)
        {
            int len = n2.Length;
            while (r2 < i2)
            {
                int i3 = e2.LastLength, a2 = Symbol(e2.Lengths[i3], t2, 65);
                e2.LastLength = a2;
                if (a2 != 0)
                {
                    int i4 = a2 >= 61 ? LongLengths[a2 - 61] : a2 + 1;
                    int o2 = Symbol(e2.OffsetLow, t2, e2.MaxOffsetLow);
                    int s2 = (int)((uint)Math.Min(e2.MaxOffsets, e2.BytesDecompressed) >> 2) + 1;
                    int c2 = Symbol(e2.OffsetHigh, t2, s2);
                    int l2 = o2 + 1 + (c2 << 2);
                    if (l2 <= 0 || l2 > r2) throw new GrannyException($"oodle0 invalid copy distance {l2}");
                    int u2 = r2 - l2;
                    for (int e3 = 0; e3 < i4; e3++) if (r2 + e3 < len) n2[r2 + e3] = n2[u2 + e3];
                    r2 += i4; e2.BytesDecompressed += i4;
                }
                else
                {
                    int i4 = Symbol(e2.Bytes, t2, e2.MaxBytes);
                    if (i4 < 0 || i4 > 255) throw new GrannyException($"oodle0 invalid literal {i4}");
                    if (r2 < len) n2[r2] = (byte)i4;
                    r2++; e2.BytesDecompressed += 1;
                }
            }
            return r2;
        }
    }
}
