#nullable disable
using System;

namespace Loki.Voice
{
    /// <summary>
    /// A streaming sample-rate converter for the voice path (client wave 6, w6voice): 16 kHz voice up to the mixer's rate for
    /// playing (48 kHz on most PCs), and a microphone that cannot record at 16 kHz down to it. Band-limited interpolation: each
    /// output sample is the input convolved with a Kaiser-windowed sinc (cut-off at 0.9 × the lower Nyquist frequency, 8 zero
    /// crossings each side, β = 8), read from a table of <see cref="Phases"/> sub-sample positions with linear interpolation
    /// between neighbouring rows. Going down, the kernel is stretched by the ratio so that everything above the new Nyquist
    /// frequency is removed before samples are dropped. The kernel is symmetric, so the delay is half its length
    /// (8 input samples up: 0.5 ms at 16 kHz). No allocation after the constructor; no Unity dependency.
    /// </summary>
    public sealed class VoiceResampler
    {
        public const int Phases = 64, ZeroCrossings = 8;
        public readonly int InRate, OutRate;
        /// <summary>Input samples per output sample.</summary>
        public readonly double Step;
        public readonly int Taps;

        private readonly float[] _table;   // (Phases + 1) rows × Taps
        private readonly float[] _hist;    // the last Taps input samples twice over (no wrap inside the dot product)
        private int _histPos;
        private double _phase;             // 0..1: where the next output sample lies between the centre sample and the one after

        public VoiceResampler(int inRate, int outRate)
        {
            InRate = inRate; OutRate = outRate; Step = (double)inRate / outRate;
            double scale = Math.Min(1.0, (double)outRate / inRate);   // < 1 going down: a longer kernel, a lower cut-off
            int half = (int)Math.Ceiling(ZeroCrossings / scale);
            Taps = 2 * half;
            _table = new float[(Phases + 1) * Taps];
            _hist = new float[2 * Taps];
            double cutoff = 0.9 * scale, beta = 8.0, i0b = I0(beta);
            for (int p = 0; p <= Phases; p++)
            {
                double frac = (double)p / Phases, sum = 0;
                for (int k = 0; k < Taps; k++)
                {
                    // tap k weighs the input sample at (k − half + 1) relative to the centre sample; the output lies frac past the centre
                    double x = k - half + 1 - frac;
                    double w = Math.Abs(x) >= half ? 0.0 : I0(beta * Math.Sqrt(1.0 - x / half * (x / half))) / i0b;
                    double v = cutoff * Sinc(cutoff * x) * w;
                    _table[p * Taps + k] = (float)v; sum += v;
                }
                // unity gain at DC for every phase (a constant input stays that constant)
                for (int k = 0; k < Taps; k++) _table[p * Taps + k] = (float)(_table[p * Taps + k] / sum);
            }
        }

        private static double Sinc(double x) { if (Math.Abs(x) < 1e-9) return 1.0; double a = Math.PI * x; return Math.Sin(a) / a; }
        /// <summary>The zeroth-order modified Bessel function of the first kind (its power series), for the Kaiser window.</summary>
        private static double I0(double x)
        {
            double sum = 1.0, term = 1.0, q = x * x / 4.0;
            for (int k = 1; k < 50; k++) { term *= q / ((double)k * k); sum += term; if (term < 1e-12 * sum) break; }
            return sum;
        }

        public void Reset() { Array.Clear(_hist, 0, _hist.Length); _histPos = 0; _phase = 0; }

        /// <summary>How many input samples the next <paramref name="outCount"/> output samples will consume.</summary>
        public int InputNeeded(int outCount)
        {
            double p = _phase; int need = 0;
            for (int i = 0; i < outCount; i++) { while (p >= 1.0) { p -= 1.0; need++; } p += Step; }
            return need;
        }

        private void PushInput(float x)
        {
            _hist[_histPos] = x; _hist[_histPos + Taps] = x;
            if (++_histPos == Taps) _histPos = 0;
        }

        /// <summary>
        /// Converts: consumes input from <paramref name="src"/> and writes output to <paramref name="dst"/> until either
        /// <paramref name="outCount"/> samples are written or the input runs out. Returns the samples written;
        /// <paramref name="consumed"/> is the input used.
        /// </summary>
        public int Process(float[] src, int srcOffset, int srcCount, float[] dst, int dstOffset, int outCount, out int consumed)
        {
            int used = 0, made = 0;
            while (made < outCount)
            {
                while (_phase >= 1.0)
                {
                    if (used == srcCount) { consumed = used; return made; }
                    PushInput(src[srcOffset + used++]);
                    _phase -= 1.0;
                }
                double fp = _phase * Phases; int row = (int)fp; float t = (float)(fp - row);
                int a = row * Taps, b = a + Taps, h = _histPos; // _hist[h .. h + Taps − 1] is the window, oldest first
                float acc = 0f;
                for (int k = 0; k < Taps; k++)
                {
                    float c = _table[a + k]; c += (_table[b + k] - c) * t;
                    acc += _hist[h + k] * c;
                }
                dst[dstOffset + made++] = acc;
                _phase += Step;
            }
            consumed = used;
            return made;
        }
    }
}
