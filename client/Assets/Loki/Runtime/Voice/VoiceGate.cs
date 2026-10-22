#nullable disable
using System;

namespace Loki.Voice
{
    /// <summary>
    /// The noise gate of the always-open microphone (client wave 6, w6voice): one decision per 20 ms frame. Nothing leaves the PC
    /// while the gate is closed. No Unity dependency (Loki Host's tests compile this file).
    ///   • A first-order high-pass at <see cref="HighPassHz"/> runs over the frame first (y[n] = a·(y[n−1] + x[n] − x[n−1]),
    ///     a = RC / (RC + 1/rate), RC = 1 / (2π·fc): the RC high-pass's difference equation), so a microphone's DC offset, breath
    ///     pops and desk rumble neither open the gate nor reach the codec. The filtered frame is what is sent.
    ///   • The level is the frame's RMS in dB relative to full scale (a full-scale sine is −3.01 dB).
    ///   • The gate opens on the first frame at or above <see cref="OpenDb"/> (attack: that same frame is sent) and stays open
    ///     while frames are at or above <see cref="CloseDb"/> (lower: hysteresis, so a level hovering at the threshold does not
    ///     chatter); below it, <see cref="HangoverFrames"/> more frames are sent (300 ms: word endings and the short pauses inside a
    ///     sentence), the last of them marked, then the gate is closed.
    ///   • <see cref="Bypass"/> ("off" in the settings) sends every frame.
    /// The thresholds are Loki's own choice (no standard gives them): see <see cref="OpenDbFor"/>.
    /// </summary>
    public sealed class VoiceGate
    {
        public const float HighPassHz = 100f;
        public const int DefaultHangoverFrames = 15; // 15 × 20 ms = 300 ms
        /// <summary>The close threshold sits this far under the open one.</summary>
        public const float HysteresisDb = 6f;
        public const float SilenceDb = -120f;

        public enum Result { Closed, First, Open, Last }

        public float OpenDb = -40f, CloseDb = -46f;
        public int HangoverFrames = DefaultHangoverFrames;
        public bool Bypass;
        /// <summary>The level of the last frame, after the high-pass (dB full scale; <see cref="SilenceDb"/> for digital silence).</summary>
        public float LevelDb { get; private set; } = SilenceDb;
        public bool IsOpen { get; private set; }
        /// <summary>Frames of hangover left while the gate is open (the full count while the level itself holds it open; 0 closed).</summary>
        public int Hang { get; private set; }

        private readonly float _a;
        private float _x1, _y1;

        public VoiceGate(int sampleRate = ImaAdpcmCodec.Rate)
        {
            double rc = 1.0 / (2.0 * Math.PI * HighPassHz), dt = 1.0 / sampleRate;
            _a = (float)(rc / (rc + dt));
        }

        /// <summary>The settings' steps: 0 off (every frame is sent), 1 low (opens at −50 dB: a quiet room, a soft voice),
        /// 2 medium (−40 dB, the default), 3 high (−30 dB: a fan, a keyboard, an open-backed headset). Loki's own values; the Voice
        /// page's meter shows the microphone's level against them.</summary>
        public static float OpenDbFor(int step) => step <= 1 ? -50f : step == 2 ? -40f : -30f;
        public const int Steps = 4;

        public void SetStep(int step)
        {
            Bypass = step <= 0;
            OpenDb = OpenDbFor(step); CloseDb = OpenDb - HysteresisDb;
        }

        /// <summary>Closes the gate and forgets the filter's history (the microphone was restarted).</summary>
        public void Reset() { IsOpen = false; Hang = 0; _x1 = _y1 = 0f; LevelDb = SilenceDb; }

        /// <summary>RMS of samples in −1..1 as dB full scale.</summary>
        public static float Db(double meanSquare) => meanSquare <= 1e-12 ? SilenceDb : (float)(10.0 * Math.Log10(meanSquare));

        /// <summary>
        /// One frame (samples −1..1), filtered in place: what to do with it. First / Open / Last frames are sent, Closed ones are not.
        /// </summary>
        public Result Process(float[] frame, int offset, int count)
        {
            double sum = 0; float x1 = _x1, y1 = _y1, a = _a;
            for (int i = 0; i < count; i++)
            {
                float x = frame[offset + i];
                float y = a * (y1 + x - x1);
                x1 = x; y1 = y;
                frame[offset + i] = y;
                sum += (double)y * y;
            }
            _x1 = x1; _y1 = y1;
            LevelDb = Db(count > 0 ? sum / count : 0);
            if (Bypass)
            {
                bool was = IsOpen; IsOpen = true; Hang = 0;
                return was ? Result.Open : Result.First;
            }
            if (!IsOpen)
            {
                if (LevelDb < OpenDb) return Result.Closed;
                IsOpen = true; Hang = HangoverFrames;
                return Result.First;
            }
            if (LevelDb >= CloseDb) { Hang = HangoverFrames; return Result.Open; }
            if (--Hang > 0) return Result.Open;
            IsOpen = false; Hang = 0;
            return Result.Last;
        }

        /// <summary>Bypass was switched off, or sending stops for another reason, while the gate was open: the next frame starts anew.</summary>
        public void ForceClose() { IsOpen = false; Hang = 0; }
    }
}
