#nullable disable
using System;

namespace Loki.Voice
{
    /// <summary>
    /// A voice codec, one frame at a time (client wave 6, w6voice). The voice path only knows this interface, so another codec
    /// (Opus, once a library may be added) can replace IMA ADPCM: the wire carries the codec's <see cref="Id"/> in every frame.
    /// No Unity dependency: Loki Host's tests compile this file too.
    /// </summary>
    public interface IVoiceCodec
    {
        /// <summary>The codec byte of a VOICE frame (never 0).</summary>
        byte Id { get; }
        int SampleRate { get; }
        /// <summary>Samples per frame (mono).</summary>
        int FrameSamples { get; }
        /// <summary>The most bytes one encoded frame takes.</summary>
        int MaxFrameBytes { get; }
        /// <summary>Encodes <see cref="FrameSamples"/> samples from <paramref name="pcm"/>; returns the bytes written.</summary>
        int Encode(short[] pcm, int pcmOffset, byte[] dst, int dstOffset);
        /// <summary>Decodes one frame into <see cref="FrameSamples"/> samples; -1 when the frame is malformed (nothing is written).</summary>
        int Decode(byte[] src, int srcOffset, int length, short[] pcm, int pcmOffset);
        /// <summary>A new talk spurt begins: the encoder forgets the last one.</summary>
        void ResetEncoder();
    }

    /// <summary>
    /// IMA ADPCM (the Interactive Multimedia Association's 4-bit ADPCM, also called DVI or Intel ADPCM): 4 bits per 16-bit sample.
    /// The algorithm is the IMA reference one ("Recommended Practices for Enhancing Digital Audio Compatibility in Multimedia
    /// Systems", revision 3.00, 1992): the 89-entry step table, the index adjustment table, and the encoder / decoder arithmetic
    /// below. It was checked here byte for byte against an independent implementation of that reference, CPython 3.12's
    /// <c>audioop.lin2adpcm</c> / <c>adpcm2lin</c> (Modules/audioop.c, "Intel/DVI ADPCM coder/decoder" after Jack Jansen's adpcm.c):
    /// <c>client/tools/voice/adpcm_reference.py</c> makes the vectors, <c>tools/lokihost/Tests/VoiceTests.cs</c> compares them.
    ///
    /// A frame is 20 ms of 16 kHz mono (320 samples) in 163 bytes = 8,150 bytes a second while a player talks:
    ///   i16 little-endian predictor, u8 step index (0..88): the coder's state BEFORE the frame's first sample,
    ///   160 bytes: 320 codes of 4 bits, the earlier sample in the LOW nibble (the order of Microsoft's IMA ADPCM WAVE blocks).
    /// Every frame carries its own state, so a frame decodes on its own: a dropped or late frame cannot corrupt the next one. The
    /// encoder keeps its state from frame to frame inside a talk spurt (no re-convergence at frame borders); a new spurt starts
    /// from the first sample itself with step index 0.
    /// </summary>
    public sealed class ImaAdpcmCodec : IVoiceCodec
    {
        public const byte CodecId = 1;
        public const int Rate = 16000, Samples = 320, HeaderBytes = 3, FrameBytes = HeaderBytes + Samples / 2;

        public byte Id => CodecId;
        public int SampleRate => Rate;
        public int FrameSamples => Samples;
        public int MaxFrameBytes => FrameBytes;

        /// <summary>The IMA step size table (89 entries) and the index adjustment by code (IMA Recommended Practices 3.00; the same
        /// numbers as CPython audioop.c's stepsizeTable / indexTable).</summary>
        public static readonly int[] StepTable =
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17,
            19, 21, 23, 25, 28, 31, 34, 37, 41, 45,
            50, 55, 60, 66, 73, 80, 88, 97, 107, 118,
            130, 143, 157, 173, 190, 209, 230, 253, 279, 307,
            337, 371, 408, 449, 494, 544, 598, 658, 724, 796,
            876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066,
            2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358,
            5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899,
            15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
        };
        public static readonly int[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };

        private int _pred, _index; private bool _fresh = true;

        public void ResetEncoder() { _fresh = true; }

        /// <summary>One sample → one 4-bit code; the state moves as the decoder's will (the reference encoder step).</summary>
        public static int EncodeSample(int sample, ref int pred, ref int index)
        {
            int step = StepTable[index];
            int diff = sample - pred;
            int sign = diff < 0 ? 8 : 0;
            if (sign != 0) diff = -diff;
            int delta = 0, vpdiff = step >> 3;
            if (diff >= step) { delta = 4; diff -= step; vpdiff += step; }
            step >>= 1;
            if (diff >= step) { delta |= 2; diff -= step; vpdiff += step; }
            step >>= 1;
            if (diff >= step) { delta |= 1; vpdiff += step; }
            if (sign != 0) pred -= vpdiff; else pred += vpdiff;
            if (pred > 32767) pred = 32767; else if (pred < -32768) pred = -32768;
            delta |= sign;
            index += IndexTable[delta];
            if (index < 0) index = 0; else if (index > 88) index = 88;
            return delta;
        }

        /// <summary>One 4-bit code → one sample (the reference decoder step).</summary>
        public static int DecodeSample(int delta, ref int pred, ref int index)
        {
            int step = StepTable[index];
            index += IndexTable[delta];
            if (index < 0) index = 0; else if (index > 88) index = 88;
            int vpdiff = step >> 3;
            if ((delta & 4) != 0) vpdiff += step;
            if ((delta & 2) != 0) vpdiff += step >> 1;
            if ((delta & 1) != 0) vpdiff += step >> 2;
            if ((delta & 8) != 0) pred -= vpdiff; else pred += vpdiff;
            if (pred > 32767) pred = 32767; else if (pred < -32768) pred = -32768;
            return pred;
        }

        public int Encode(short[] pcm, int pcmOffset, byte[] dst, int dstOffset)
        {
            if (_fresh) { _pred = pcm[pcmOffset]; _index = 0; _fresh = false; }
            dst[dstOffset] = (byte)_pred; dst[dstOffset + 1] = (byte)(_pred >> 8); dst[dstOffset + 2] = (byte)_index;
            int pred = _pred, index = _index, o = dstOffset + HeaderBytes;
            for (int i = 0; i < Samples; i += 2)
            {
                int lo = EncodeSample(pcm[pcmOffset + i], ref pred, ref index);
                int hi = EncodeSample(pcm[pcmOffset + i + 1], ref pred, ref index);
                dst[o++] = (byte)(lo | hi << 4);
            }
            _pred = pred; _index = index;
            return FrameBytes;
        }

        public int Decode(byte[] src, int srcOffset, int length, short[] pcm, int pcmOffset)
        {
            if (length != FrameBytes) return -1;
            int pred = (short)(src[srcOffset] | src[srcOffset + 1] << 8), index = src[srcOffset + 2];
            if (index > 88) return -1;
            int o = srcOffset + HeaderBytes;
            for (int i = 0; i < Samples; i += 2)
            {
                int b = src[o++];
                pcm[pcmOffset + i] = (short)DecodeSample(b & 15, ref pred, ref index);
                pcm[pcmOffset + i + 1] = (short)DecodeSample(b >> 4, ref pred, ref index);
            }
            return Samples;
        }

        /// <summary>The codec for a VOICE frame's codec byte, or null when this build does not know it (the frame is skipped).</summary>
        public static IVoiceCodec ForId(byte id) => id == CodecId ? new ImaAdpcmCodec() : null;
    }
}
