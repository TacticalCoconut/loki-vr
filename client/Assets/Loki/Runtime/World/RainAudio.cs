using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Loki.World
{
    /// <summary>
    /// Client wave 5 (w5weather): the rain's sounds as roBrowserLegacy makes them — synthesised with the Web Audio API, no sound file
    /// (Renderer/Effects/RainWeather.js; roBrowser is a reimplementation). No source ties a sound file to EF_RAIN: roBrowser's entry 161
    /// is a FUNC only (DB/Effects/EffectTable.js:4021-4031), and the GRF's rain / thunder-named wavs belong to skills (effect\rainstorm.wav
    /// = EF_RAIN_PARTICLE 857 Severe Rainstorm, effect\ef_thunderstorm.wav = EF_THUNDERSTORM2 622: EffectTable.js:13858-13863,
    /// 11491-11497). Three sources, each mixed here sample by sample on Unity's
    /// audio thread (OnAudioFilterRead) as the Web Audio graph would:
    ///  • the rain: 2 s of white noise (Math.random() · 2 − 1) looped, through a low-pass at 400 Hz, gain RAIN_VOLUME 0.03 × the sound
    ///    volume taken when the rain starts (initRainSound :488-525; RAIN_VOLUME :48);
    ///  • a drop's "plink" with every splash: a sine of 1000-1200 Hz for 20 ms, gain 0 → peak in 1 ms (linear), → 0.001 at 10 ms
    ///    (exponential), peak a random value between 0.1 and RAIN_VOLUME times the volume (randRange(0.1, RAIN_VOLUME): 0.03-0.1)
    ///    (triggerRainDropSound :527-576);
    ///  • thunder with every lightning flash: 2 s of white noise × e^(−i / 2 s), through a low-pass at 300 Hz, gain 0.8 × volume falling
    ///    exponentially to 0.01 × volume over 1.5 s (triggerThunderSound :578-618).
    /// Web Audio rules ported: an exponential ramp v(t) = V0 · (V1 / V0)^((t − T0) / (T1 − T0)) from the previous automation event, the
    /// value held after the last event; the BiquadFilterNode's default Q of 1, which for a low-pass is a resonance in decibels: RBJ's
    /// cookbook low-pass with α = sin ω0 / (2 · 10^(Q / 20)) (W3C Web Audio API, BiquadFilterNode "lowpass"); a mono source sent to every
    /// output channel. A new sound starts at the next audio block (Web Audio's currentTime is block-quantised too). The volume is Loki's
    /// effects volume (LokiAudio.EffectsVolume) where roBrowser reads Preferences.Sound.volume; 0 is roBrowser's "sound off".
    /// Not ported (a roBrowser slip): its rain noise keeps playing after a map change, because the instance is dropped without free()
    /// (RainWeather.js:446-449); here the rain's sound stops whenever the rain is cleared.
    /// </summary>
    public sealed class RainAudio : MonoBehaviour
    {
        public const float RainVolume = 0.03f;                         // RAIN_VOLUME (RainWeather.js:48)
        public const float RainCutoff = 400f, ThunderCutoff = 300f;     // :514, :606

        /// <summary>A second-order low-pass as the Web Audio BiquadFilterNode computes it (Q in dB for this type).</summary>
        public sealed class Biquad
        {
            private readonly double _b0, _b1, _b2, _a1, _a2; private double _x1, _x2, _y1, _y2;
            public Biquad(double cutoff, double sampleRate, double qDb = 1.0)
            {
                double w0 = 2.0 * Math.PI * cutoff / sampleRate, cw = Math.Cos(w0);
                double alpha = Math.Sin(w0) / (2.0 * Math.Pow(10.0, qDb / 20.0));
                double a0 = 1.0 + alpha;
                _b0 = (1.0 - cw) / 2.0 / a0; _b1 = (1.0 - cw) / a0; _b2 = _b0; _a1 = -2.0 * cw / a0; _a2 = (1.0 - alpha) / a0;
            }
            public float Next(float x)
            {
                double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
                return (float)y;
            }
            /// <summary>|H(e^jω)| at a frequency (a check of the coefficients).</summary>
            public double Gain(double f, double sampleRate)
            {
                double w = 2.0 * Math.PI * f / sampleRate;
                double nr = _b0 + _b1 * Math.Cos(w) + _b2 * Math.Cos(2 * w), ni = -(_b1 * Math.Sin(w) + _b2 * Math.Sin(2 * w));
                double dr = 1.0 + _a1 * Math.Cos(w) + _a2 * Math.Cos(2 * w), di = -(_a1 * Math.Sin(w) + _a2 * Math.Sin(2 * w));
                return Math.Sqrt((nr * nr + ni * ni) / (dr * dr + di * di));
            }
        }

        private sealed class Voice
        {
            public bool Thunder; public long Pos; // samples played
            public double Freq, Peak;              // plink
            public float[] Buffer; public Biquad Filter; public double Gain0, Gain1, RampSeconds; // thunder
        }

        private int _rate = 48000;
        private float[] _noise; private int _noisePos; private Biquad _rainFilter; private float _rainGain; private volatile bool _rainOn;
        private readonly object _lock = new object();
        private readonly List<Voice> _pending = new List<Voice>(), _voices = new List<Voice>();
        private readonly System.Random _rng = new System.Random();
        private volatile float _volume = 0.9f;
        /// <summary>The listener's (master) volume, read on the main thread (port wave J): Unity applies the listener's and the
        /// source's gains to the source's own (silent) clip BEFORE this filter, so a sound the filter ADDS is not scaled by them;
        /// measured 2026-10-08: a -lokiSmoke client with AudioListener.volume 0 sent the rain to the loudspeaker (peak 0.0078).</summary>
        private volatile float _listener = 1f;
        private AudioSource _src;
        private int _blocks, _plinks, _thunders;
        /// <summary>Audio blocks mixed so far (the audio thread's own count), plinks and thunders started.</summary>
        public int Blocks => Volatile.Read(ref _blocks);
        public int Plinks => Volatile.Read(ref _plinks);
        public int Thunders => Volatile.Read(ref _thunders);
        public bool RainOn => _rainOn;
        public int SampleRate => _rate;
        /// <summary>The rain loop's gain (RAIN_VOLUME × the volume when it started).</summary>
        public float RainGain => _rainGain;

        /// <summary>Makes the player: a 2-D AudioSource looping a silent clip, which this filter adds the synthesised sound to.</summary>
        public static RainAudio Create(Transform parent)
        {
            var go = new GameObject("rain-audio");
            go.transform.SetParent(parent, false);
            go.SetActive(false);
            var a = go.AddComponent<RainAudio>();
            a._rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            a._src = go.AddComponent<AudioSource>();
            a._src.playOnAwake = false; a._src.loop = true; a._src.spatialBlend = 0f; a._src.volume = 1f;
            a._src.clip = AudioClip.Create("rain-silence", a._rate, 1, a._rate, false);
            go.SetActive(true);
            return a;
        }

        private void Update() { _volume = Audio.LokiAudio.Instance != null ? Audio.LokiAudio.Instance.EffectsVolume : 0f; _listener = AudioListener.volume; }

        /// <summary>The rain starts (initRainSound): its noise loop at RAIN_VOLUME × the volume now; nothing when the volume is 0.</summary>
        public void StartRain()
        {
            Update();
            if (_volume <= 0f) { _rainOn = false; return; }
            if (_noise == null)
            {
                _noise = new float[2 * _rate];
                for (int i = 0; i < _noise.Length; i++) _noise[i] = (float)(_rng.NextDouble() * 2.0 - 1.0);
            }
            lock (_lock) { _rainFilter = new Biquad(RainCutoff, _rate); _noisePos = 0; _rainGain = RainVolume * _volume; }
            _rainOn = true;
            if (_src != null && !_src.isPlaying) _src.Play();
        }

        /// <summary>The rain is gone (free()): the noise stops; sounds already started play out.</summary>
        public void StopRain() { _rainOn = false; }

        /// <summary>A drop splashed (triggerRainDropSound).</summary>
        public void Plink()
        {
            float vol = _volume; if (vol <= 0f) return;
            var v = new Voice { Freq = 1000.0 + _rng.NextDouble() * 200.0, Peak = (0.1 + _rng.NextDouble() * (RainVolume - 0.1)) * vol };
            lock (_lock) _pending.Add(v);
            Interlocked.Increment(ref _plinks);
            if (_src != null && !_src.isPlaying) _src.Play();
        }

        /// <summary>A lightning flash (triggerThunderSound).</summary>
        public void Thunder()
        {
            float vol = _volume; if (vol <= 0f) return;
            int n = 2 * _rate; var buf = new float[n];
            for (int i = 0; i < n; i++) buf[i] = (float)((_rng.NextDouble() * 2.0 - 1.0) * Math.Exp(-(double)i / n));
            var v = new Voice { Thunder = true, Buffer = buf, Filter = new Biquad(ThunderCutoff, _rate), Gain0 = 0.8 * vol, Gain1 = 0.01 * vol, RampSeconds = 1.5 };
            lock (_lock) _pending.Add(v);
            Interlocked.Increment(ref _thunders);
            if (_src != null && !_src.isPlaying) _src.Play();
        }

        /// <summary>A plink's gain t seconds after it started (setValueAtTime(0, now), linearRamp → peak at +1 ms, exponentialRamp → 0.001
        /// at +10 ms, then held until osc.stop at +20 ms).</summary>
        public static double PlinkGain(double t, double peak)
        {
            if (t < 0.001) return peak * t / 0.001;
            if (t < 0.010) return peak * Math.Pow(0.001 / peak, (t - 0.001) / 0.009);
            return 0.001;
        }
        public const double PlinkSeconds = 0.020;
        /// <summary>The thunder's gain t seconds in: 0.8 × volume → 0.01 × volume exponentially over 1.5 s, then held (the buffer is 2 s).</summary>
        public static double ThunderGain(double t, double g0, double g1, double ramp) => t < ramp ? g0 * Math.Pow(g1 / g0, t / ramp) : g1;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            Mix(data, channels);
            // the master volume (and a muted test client's 0) on what Mix added: see _listener
            float g = _listener;
            if (g < 1f) for (int i = 0; i < data.Length; i++) data[i] *= g;
        }

        /// <summary>Adds this block of sound to data (interleaved, channels per frame): the rain loop and every voice.</summary>
        public void Mix(float[] data, int channels)
        {
            if (channels <= 0) return;
            int frames = data.Length / channels;
            lock (_lock)
            {
                if (_pending.Count > 0) { _voices.AddRange(_pending); _pending.Clear(); }
                bool rain = _rainOn && _noise != null && _rainFilter != null;
                double dt = 1.0 / _rate;
                for (int f = 0; f < frames; f++)
                {
                    float s = 0f;
                    if (rain) { s += _rainFilter.Next(_noise[_noisePos]) * _rainGain; if (++_noisePos >= _noise.Length) _noisePos = 0; }
                    for (int k = 0; k < _voices.Count; k++)
                    {
                        var v = _voices[k];
                        double t = v.Pos * dt;
                        if (!v.Thunder)
                        {
                            if (t < PlinkSeconds) s += (float)(Math.Sin(2.0 * Math.PI * v.Freq * t) * PlinkGain(t, v.Peak));
                        }
                        else if (v.Pos < v.Buffer.Length) s += (float)(v.Filter.Next(v.Buffer[v.Pos]) * ThunderGain(t, v.Gain0, v.Gain1, v.RampSeconds));
                        v.Pos++;
                    }
                    for (int c = 0; c < channels; c++) data[f * channels + c] += s;
                }
                for (int k = _voices.Count - 1; k >= 0; k--)
                {
                    var v = _voices[k];
                    if (v.Thunder ? v.Pos >= v.Buffer.Length : v.Pos * dt >= PlinkSeconds) _voices.RemoveAt(k);
                }
            }
            Interlocked.Increment(ref _blocks);
        }
    }
}
