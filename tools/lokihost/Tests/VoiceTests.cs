using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Loki.Net;
using Loki.Voice;
using LokiHost.Core;
using Xunit;

namespace LokiHost.Tests;

/// <summary>
/// Voice between friends over the headset relay (client wave 6, w6voice): the client's codec, noise gate, jitter buffer and
/// resampler (the client's own files, compiled into this project), the version-2 wire format, and the relay with raw TCP clients.
/// </summary>
public class VoiceTests
{
    // ------------------------------------------------------------------------------------------------ helpers

    static short[] Pcm(string hex)
    {
        var b = Convert.FromHexString(hex);
        var s = new short[b.Length / 2];
        for (int i = 0; i < s.Length; i++) s[i] = (short)(b[2 * i] | b[2 * i + 1] << 8);
        return s;
    }

    static short[] Sine(double hz, double amp, int n, int rate = 16000, double phase = 0) =>
        Enumerable.Range(0, n).Select(i => (short)Math.Round(amp * Math.Sin(2 * Math.PI * hz * i / rate + phase))).ToArray();

    /// <summary>Encodes whole frames with one encoder (a talk spurt) and decodes each frame with a decoder of its own.</summary>
    static short[] RoundTrip(short[] pcm, out byte[][] frames)
    {
        var enc = new ImaAdpcmCodec();
        int n = pcm.Length / ImaAdpcmCodec.Samples;
        frames = new byte[n][];
        var back = new short[n * ImaAdpcmCodec.Samples];
        for (int f = 0; f < n; f++)
        {
            frames[f] = new byte[ImaAdpcmCodec.FrameBytes];
            Assert.Equal(ImaAdpcmCodec.FrameBytes, enc.Encode(pcm, f * ImaAdpcmCodec.Samples, frames[f], 0));
            Assert.Equal(ImaAdpcmCodec.Samples, new ImaAdpcmCodec().Decode(frames[f], 0, frames[f].Length, back, f * ImaAdpcmCodec.Samples));
        }
        return back;
    }

    /// <summary>Signal-to-noise ratio in dB of a decoded signal against its original (skipping the first samples: the coder's attack).</summary>
    static double Snr(short[] a, short[] b, int skip = 0)
    {
        double sig = 0, err = 0;
        for (int i = skip; i < a.Length; i++) { sig += (double)a[i] * a[i]; err += (double)(a[i] - b[i]) * (a[i] - b[i]); }
        return err == 0 ? 200 : 10 * Math.Log10(sig / err);
    }

    static double RmsDb(float[] x, int from, int to)
    {
        double s = 0; for (int i = from; i < to; i++) s += (double)x[i] * x[i];
        return 10 * Math.Log10(Math.Max(s / (to - from), 1e-20));
    }

    /// <summary>The frequency of a tone from its rising zero crossings (interpolated), in Hz.</summary>
    static double ZeroCrossHz(float[] x, int from, int to, int rate)
    {
        double first = -1, last = -1; int n = 0;
        for (int i = from + 1; i < to; i++)
            if (x[i - 1] < 0 && x[i] >= 0)
            {
                double t = i - 1 + (0 - x[i - 1]) / (double)(x[i] - x[i - 1]);
                if (first < 0) first = t; else n++;
                last = t;
            }
        return n > 0 ? n * rate / (last - first) : 0;
    }

    /// <summary>The amplitude of one frequency in a signal (a single DFT bin, Hann window, scaled so a sine of amplitude A gives A).</summary>
    static double ToneAmplitude(float[] x, int from, int to, double hz, int rate)
    {
        double re = 0, im = 0, wsum = 0; int n = to - from;
        for (int i = 0; i < n; i++)
        {
            double w = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1));
            re += w * x[from + i] * Math.Cos(2 * Math.PI * hz * i / rate); im -= w * x[from + i] * Math.Sin(2 * Math.PI * hz * i / rate); wsum += w;
        }
        return 2 * Math.Sqrt(re * re + im * im) / wsum;
    }

    // ------------------------------------------------------------------------------------------------ the codec

    [Fact]
    public void The_step_table_is_the_ima_table()
    {
        Assert.Equal(89, ImaAdpcmCodec.StepTable.Length);
        Assert.Equal((7, 32767), (ImaAdpcmCodec.StepTable[0], ImaAdpcmCodec.StepTable[88]));
        // every step is about 1.1 times the one before (the table's construction): a wrong entry would break this
        for (int i = 1; i < 89; i++)
            Assert.InRange(ImaAdpcmCodec.StepTable[i] / (double)ImaAdpcmCodec.StepTable[i - 1], 1.04, 1.16);
        Assert.Equal(new[] { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 }, ImaAdpcmCodec.IndexTable);
    }

    [Fact]
    public void Encoding_and_decoding_equal_cpythons_audioop_byte_for_byte()
    {
        Assert.Equal(6, AdpcmReference.Vectors.Length);
        foreach (var v in AdpcmReference.Vectors)
        {
            var pcm = Pcm(v.PcmHex);
            Assert.Equal(640, pcm.Length);
            var reference = Convert.FromHexString(v.AdpcmHex);   // audioop: the earlier sample in the high nibble
            var decoded = Pcm(v.DecodedHex);
            var enc = new ImaAdpcmCodec();
            var back = new short[640];
            for (int f = 0; f < 2; f++)
            {
                var frame = new byte[ImaAdpcmCodec.FrameBytes];
                enc.Encode(pcm, f * 320, frame, 0);
                // the frame's header is the coder's state before its first sample: the first frame starts at (first sample, 0)
                if (f == 0) Assert.Equal((pcm[0], 0), ((short)(frame[0] | frame[1] << 8), (int)frame[2]));
                for (int i = 0; i < 160; i++)
                {
                    byte r = reference[f * 160 + i];
                    Assert.True((byte)(r >> 4 | r << 4) == frame[3 + i], $"{v.Name}: frame {f}, byte {i}");
                }
                Assert.Equal(320, new ImaAdpcmCodec().Decode(frame, 0, frame.Length, back, f * 320));
            }
            Assert.True(decoded.SequenceEqual(back), v.Name + ": decoded samples differ from audioop.adpcm2lin");
            // the state after 640 samples, as audioop reports it: what a third frame's header would carry
            var third = new byte[ImaAdpcmCodec.FrameBytes];
            enc.Encode(new short[320], 0, third, 0);
            Assert.Equal((v.EndPredictor, v.EndIndex), ((int)(short)(third[0] | third[1] << 8), (int)third[2]));
        }
    }

    [Fact]
    public void A_frame_is_163_bytes_for_20_ms_and_8150_bytes_a_second()
    {
        var c = new ImaAdpcmCodec();
        Assert.Equal((16000, 320, 163, (byte)1), (c.SampleRate, c.FrameSamples, c.MaxFrameBytes, c.Id));
        Assert.Equal(8150, c.MaxFrameBytes * c.SampleRate / c.FrameSamples);
        Assert.True(c.MaxFrameBytes <= PresenceWire.MaxVoiceData);
    }

    [Theory]
    // speech-band tones at a talking level (-12 dB full scale = amplitude 8231) and a quiet one. 4-bit ADPCM tracks low
    // frequencies closely and high ones coarsely; measured 2026-10-04: 43.1, 36.6, 28.6, 20.6, 18.5 and 36.9 dB. The bound is
    // that minus 3 dB.
    [InlineData(200, 8231, 40)]
    [InlineData(440, 8231, 33.5)]
    [InlineData(1000, 8231, 25.5)]
    [InlineData(2000, 8231, 17.5)]
    [InlineData(3400, 8231, 15.5)]
    [InlineData(440, 800, 33.5)]
    public void A_tone_survives_the_round_trip_with_at_least_this_signal_to_noise_ratio(double hz, double amp, double minSnrDb)
    {
        var pcm = Sine(hz, amp, 16000);
        var back = RoundTrip(pcm, out _);
        double snr = Snr(pcm, back, 320);
        Assert.True(snr >= minSnrDb, $"{hz} Hz at {amp}: SNR {snr:F1} dB, expected at least {minSnrDb}");
        // the tone's own level is kept within 0.5 dB
        var f = back.Select(s => s / 32768f).ToArray(); var o = pcm.Select(s => s / 32768f).ToArray();
        Assert.InRange(RmsDb(f, 320, 16000) - RmsDb(o, 320, 16000), -0.5, 0.5);
    }

    [Fact]
    public void A_speech_like_signal_survives_the_round_trip()
    {
        // a 140 Hz voice: harmonics with a formant-like weighting under a 4 Hz syllable envelope, one second
        var pcm = new short[16000];
        (int h, double w)[] parts = { (1, 0.5), (2, 0.9), (4, 1.0), (5, 0.7), (9, 0.4), (17, 0.25), (24, 0.15) };
        for (int i = 0; i < pcm.Length; i++)
        {
            double t = i / 16000.0, env = 0.5 - 0.5 * Math.Cos(2 * Math.PI * 4 * t), s = 0;
            foreach (var (h, w) in parts) s += w * Math.Sin(2 * Math.PI * 140 * h * t);
            pcm[i] = (short)Math.Round(5000 * env * s);
        }
        double snr = Snr(pcm, RoundTrip(pcm, out _), 320);
        Assert.True(snr >= 24.5, $"speech-like: SNR {snr:F1} dB, expected at least 24.5 (measured 27.9)");
    }

    [Fact]
    public void Every_frame_decodes_on_its_own_so_a_lost_frame_does_not_touch_the_next()
    {
        var pcm = Sine(440, 9000, 320 * 6).Zip(Sine(1300, 4000, 320 * 6), (a, b) => (short)(a + b)).ToArray();
        var all = RoundTrip(pcm, out var frames);
        // frame 4 decoded alone, by a decoder that never saw frames 0-3 (they were lost), equals its part of the full decode
        var alone = new short[320];
        Assert.Equal(320, new ImaAdpcmCodec().Decode(frames[4], 0, frames[4].Length, alone, 0));
        Assert.True(alone.SequenceEqual(all.Skip(4 * 320).Take(320)));
        // and one decoder fed frames 0, 1, 3 (2 lost) gives frame 3 exactly as with frame 2 present
        var d = new ImaAdpcmCodec(); var buf = new short[320];
        d.Decode(frames[0], 0, 163, buf, 0); d.Decode(frames[1], 0, 163, buf, 0); d.Decode(frames[3], 0, 163, buf, 0);
        Assert.True(buf.SequenceEqual(all.Skip(3 * 320).Take(320)));
        // inside a talk spurt the encoder carries its state on: frame 1's header is where frame 0 ended, not a restart
        Assert.NotEqual(0, frames[1][2]);
    }

    [Fact]
    public void Overload_clips_and_never_wraps_around()
    {
        // full-scale square wave and full-scale noise: the predictor is clamped to 16 bits (it must never wrap to the other sign)
        var sq = Enumerable.Range(0, 3200).Select(i => (short)((i / 20) % 2 == 0 ? 32767 : -32768)).ToArray();
        var back = RoundTrip(sq, out _);
        for (int i = 400; i < sq.Length; i++)
            if (i % 20 >= 12) Assert.True(Math.Sign(back[i]) == Math.Sign(sq[i]) && Math.Abs((int)back[i]) > 20000, $"sample {i}: {back[i]} for {sq[i]}");
        // a full-scale sine: still a good copy (no wrap-around artefacts, which would cost tens of dB)
        var sine = Sine(300, 32767, 16000);
        Assert.True(Snr(sine, RoundTrip(sine, out _), 320) >= 36.5); // measured 39.7 dB
        // extremes at the frame start: the header holds any 16-bit predictor
        var edge = new short[320]; edge[0] = -32768; for (int i = 1; i < 320; i++) edge[i] = 32767;
        var e = RoundTrip(edge, out var ef);
        Assert.Equal(-32768, (short)(ef[0][0] | ef[0][1] << 8));
        Assert.True(e[319] > 30000);
    }

    [Fact]
    public void Malformed_frames_are_refused()
    {
        var c = new ImaAdpcmCodec(); var pcm = new short[320]; var frame = new byte[163];
        c.Encode(Sine(440, 5000, 320), 0, frame, 0);
        Assert.Equal(-1, c.Decode(frame, 0, 162, pcm, 0));          // short
        Assert.Equal(-1, c.Decode(new byte[164], 0, 164, pcm, 0));  // long
        var badIndex = (byte[])frame.Clone(); badIndex[2] = 89;
        Assert.Equal(-1, c.Decode(badIndex, 0, 163, pcm, 0));       // step index outside the table
        Assert.Null(ImaAdpcmCodec.ForId(0)); Assert.Null(ImaAdpcmCodec.ForId(2));
        Assert.NotNull(ImaAdpcmCodec.ForId(1));
    }

    // ------------------------------------------------------------------------------------------------ the noise gate

    static float[] Frame(double hz, double ampDb, int frameIndex)
    {
        double amp = Math.Pow(10, ampDb / 20) * Math.Sqrt(2); // ampDb is the RMS level of the sine
        var f = new float[320];
        for (int i = 0; i < 320; i++) f[i] = (float)(amp * Math.Sin(2 * Math.PI * hz * (frameIndex * 320 + i) / 16000.0));
        return f;
    }

    [Fact]
    public void The_gate_opens_in_one_frame_holds_300_ms_and_closes()
    {
        var g = new VoiceGate(); g.SetStep(2); // medium: opens at -40 dB, closes under -46 dB
        Assert.Equal((-40f, -46f, 15), (g.OpenDb, g.CloseDb, g.HangoverFrames));
        // 500 Hz: ten whole cycles per frame, so the level changes at a zero crossing (an abrupt change mid-wave is a click, and a
        // click is loud: the gate would rightly count that frame as sound)
        var results = new List<VoiceGate.Result>(); int k = 0;
        for (int i = 0; i < 10; i++) results.Add(g.Process(Frame(500, -60, k++), 0, 320));   // room noise: closed
        for (int i = 0; i < 20; i++) results.Add(g.Process(Frame(500, -30, k++), 0, 320));   // speech (the filter's own decay after it, about 25 dB under, stays below the close threshold)
        for (int i = 0; i < 30; i++) results.Add(g.Process(Frame(500, -60, k++), 0, 320));   // quiet again
        Assert.All(results.Take(10), r => Assert.Equal(VoiceGate.Result.Closed, r));
        Assert.Equal(VoiceGate.Result.First, results[10]);                                    // attack: the very frame that got loud
        Assert.All(results.Skip(11).Take(19 + 14), r => Assert.Equal(VoiceGate.Result.Open, r));
        Assert.Equal(VoiceGate.Result.Last, results[10 + 20 + 14]);                           // the 15th quiet frame: 300 ms of hangover
        Assert.All(results.Skip(10 + 20 + 15), r => Assert.Equal(VoiceGate.Result.Closed, r));
        Assert.Equal(20 + 15, results.Count(r => r != VoiceGate.Result.Closed));
    }

    [Fact]
    public void A_level_between_the_thresholds_keeps_an_open_gate_open_but_does_not_open_a_closed_one()
    {
        var g = new VoiceGate(); g.SetStep(2);
        for (int i = 0; i < 5; i++) Assert.Equal(VoiceGate.Result.Closed, g.Process(Frame(500, -43, i), 0, 320)); // between -46 and -40
        Assert.Equal(VoiceGate.Result.First, g.Process(Frame(500, -30, 5), 0, 320));
        for (int i = 6; i < 60; i++) Assert.Equal(VoiceGate.Result.Open, g.Process(Frame(500, -43, i), 0, 320));  // held: no chatter
        // a pause shorter than the hangover does not close it
        for (int i = 60; i < 70; i++) Assert.Equal(VoiceGate.Result.Open, g.Process(Frame(500, -70, i), 0, 320));
        Assert.Equal(VoiceGate.Result.Open, g.Process(Frame(500, -30, 70), 0, 320));
        Assert.Equal(15, g.Hang);
    }

    [Fact]
    public void The_high_pass_keeps_dc_and_rumble_from_opening_the_gate_and_leaves_speech_alone()
    {
        var g = new VoiceGate(); g.SetStep(2);
        // a DC offset of 0.2 (-14 dB): gone after the filter settles
        var dc = Enumerable.Repeat(0.2f, 320).ToArray();
        for (int i = 0; i < 20; i++) g.Process((float[])dc.Clone(), 0, 320);
        Assert.True(g.LevelDb < -80, $"DC after the high-pass: {g.LevelDb} dB");
        Assert.Equal(VoiceGate.Result.Closed, g.Process((float[])dc.Clone(), 0, 320));
        // the filter against its own transfer function, H(z) = a·(1 − z⁻¹) / (1 − a·z⁻¹) with a = RC / (RC + 1/16000), RC = 1 / (2π·100):
        // |H| = a · 2·sin(ω/2) / √(1 − 2a·cos ω + a²): about −20.3 dB at 10 Hz, −3.3 dB at 100 Hz, −0.4 dB at 440 Hz and at 1 kHz
        double rc = 1 / (2 * Math.PI * 100), a = rc / (rc + 1 / 16000.0);
        foreach (var hz in new[] { 10.0, 100.0, 440.0, 1000.0, 3000.0 })
        {
            double w = 2 * Math.PI * hz / 16000, want = 20 * Math.Log10(a * 2 * Math.Sin(w / 2) / Math.Sqrt(1 - 2 * a * Math.Cos(w) + a * a));
            var h = new VoiceGate { Bypass = true };
            double sum = 0; int n = 0;
            for (int i = 0; i < 150; i++) { h.Process(Frame(hz, -20, i), 0, 320); if (i >= 50) { sum += Math.Pow(10, h.LevelDb / 10); n++; } }
            Assert.InRange(10 * Math.Log10(sum / n) - -20.0, want - 0.1, want + 0.1);
        }
        Assert.InRange(20 * Math.Log10(a * 2 * Math.Sin(Math.PI * 100 / 16000) / Math.Sqrt(1 - 2 * a * Math.Cos(2 * Math.PI * 100 / 16000) + a * a)), -3.5, -3.0);
    }

    [Fact]
    public void Off_sends_every_frame_and_the_steps_are_ordered()
    {
        var g = new VoiceGate(); g.SetStep(0);
        Assert.True(g.Bypass);
        Assert.Equal(VoiceGate.Result.First, g.Process(new float[320], 0, 320));
        for (int i = 0; i < 50; i++) Assert.Equal(VoiceGate.Result.Open, g.Process(new float[320], 0, 320));
        Assert.Equal(VoiceGate.SilenceDb, g.LevelDb);
        Assert.True(VoiceGate.OpenDbFor(1) < VoiceGate.OpenDbFor(2) && VoiceGate.OpenDbFor(2) < VoiceGate.OpenDbFor(3));
    }

    // ------------------------------------------------------------------------------------------------ the jitter buffer

    static byte[][] Encode(short[] pcm)
    {
        RoundTrip(pcm, out var frames);
        return frames;
    }

    [Fact]
    public void A_talk_spurt_starts_after_the_target_plays_through_and_ends_without_an_underrun()
    {
        var pcm = Sine(440, 8231, 320 * 20);
        var frames = Encode(pcm);
        var s = new VoiceStream(7);
        var outp = new float[320 * 40];
        int o = 0;
        // nothing yet: silence
        s.Read(outp, o, 320); o += 320;
        Assert.All(outp.Take(320), x => Assert.Equal(0f, x));
        // three frames (under the target of four): still waiting
        for (int f = 0; f < 3; f++) Assert.True(s.Push((ushort)f, 1, (byte)(f == 0 ? 1 : 0), frames[f], 0, 163));
        s.Read(outp, o, 320); o += 320;
        Assert.False(s.Playing);
        Assert.Equal(3, s.Buffered);
        // the fourth: playing starts, faded in over 5 ms
        s.Push(3, 1, 0, frames[3], 0, 163);
        int startAt = o;
        for (int f = 4; f < 20; f++)
        {
            s.Push((ushort)f, 1, (byte)(f == 19 ? 2 : 0), frames[f], 0, 163);
            s.Read(outp, o, 320); o += 320;
        }
        while (s.Playing && o < outp.Length - 320) { s.Read(outp, o, 320); o += 320; }
        Assert.Equal((20, 0, 0, 1), (s.Played, s.Underruns, s.Gaps, s.Spurts));
        // the decoded tone, as the codec gives it: 440 Hz, its level within 0.5 dB
        Assert.InRange(ZeroCrossHz(outp, startAt + 400, startAt + 20 * 320 - 100, 16000), 439, 441);
        Assert.InRange(RmsDb(outp, startAt + 400, startAt + 20 * 320 - 100) - (20 * Math.Log10(8231 / 32768.0) - 3.01), -0.5, 0.5);
        Assert.True(Math.Abs(outp[startAt + 1]) < Math.Abs(pcm[1] / 32768f) * 0.2f + 1e-3f); // faded in
        // after the last frame: silence again, and the stream is idle (the spurt ended by its marked last frame, not by an underrun)
        Assert.All(outp.Skip(startAt + 20 * 320).Take(320), x => Assert.Equal(0f, x));
        Assert.Equal(VoiceStream.StartTargetFrames, s.TargetFrames);
    }

    [Fact]
    public void An_underrun_fades_out_once_grows_the_target_and_resumes_faded_in()
    {
        var frames = Encode(Sine(300, 12000, 320 * 30));
        var s = new VoiceStream(8);
        var block = new float[320];
        for (int f = 0; f < 6; f++) s.Push((ushort)f, 1, 0, frames[f], 0, 163);
        for (int i = 0; i < 6; i++) s.Read(block, 0, 320);          // plays all six
        Assert.Equal((6, 0), (s.Played, s.Underruns));
        s.Read(block, 0, 320);                                        // nothing left, no "last" mark: the concealment frame
        Assert.Equal(1, s.Underruns);
        Assert.True(Math.Abs(block[0]) > 0.01f || Math.Abs(block[10]) > 0.01f);   // starts as the last frame started
        Assert.True(Math.Abs(block[319]) < 0.005f);                                // and has faded to (nearly) nothing
        double head = RmsDb(block, 0, 80), tail = RmsDb(block, 240, 320);
        Assert.True(tail < head - 10, $"fade-out: first quarter {head:F1} dB, last quarter {tail:F1} dB");
        s.Read(block, 0, 320);
        Assert.All(block, x => Assert.Equal(0f, x));                  // then silence: no stuck sound
        Assert.False(s.Playing);
        Assert.Equal(VoiceStream.StartTargetFrames + 1, s.TargetFrames);
        // frames come back: playing waits for the new target (5), then fades in
        for (int f = 6; f < 10; f++) s.Push((ushort)f, 1, 0, frames[f], 0, 163);
        s.Read(block, 0, 320); Assert.False(s.Playing);
        s.Push(10, 1, 0, frames[10], 0, 163);
        s.Read(block, 0, 320); Assert.True(s.Playing);
        Assert.True(Math.Abs(block[1]) < 0.02f);
        Assert.Equal(1, s.Spurts);                                    // the same talk spurt
    }

    [Fact]
    public void A_short_word_plays_at_once_a_lost_frame_is_counted_and_a_burst_is_cut_to_the_target()
    {
        var frames = Encode(Sine(500, 9000, 320 * 40));
        var block = new float[320];
        // a two-frame spurt whose last frame is marked: it does not wait for four frames
        var s = new VoiceStream(9);
        s.Push(0, 1, 1, frames[0], 0, 163); s.Push(1, 1, 2, frames[1], 0, 163);
        s.Read(block, 0, 320); Assert.True(s.Playing);
        s.Read(block, 0, 320); s.Read(block, 0, 320);
        Assert.Equal((2, 0), (s.Played, s.Underruns));
        Assert.False(s.Playing);
        // a gap in the sequence numbers (the relay dropped frame 12) is counted, the next frame still plays
        s.Push(2, 1, 1, frames[2], 0, 163); s.Push(3, 1, 0, frames[3], 0, 163); s.Push(5, 1, 0, frames[5], 0, 163);
        Assert.Equal(1, s.Gaps);
        // a burst: 20 frames at once (the speaker's PC stalled): everything over target + 6 is skipped, the newest are kept
        var b = new VoiceStream(10);
        for (int f = 0; f < 20; f++) b.Push((ushort)f, 1, 0, frames[f], 0, 163);
        b.Read(block, 0, 320);
        Assert.Equal(20 - VoiceStream.StartTargetFrames, b.LateSkipped);
        Assert.Equal(VoiceStream.StartTargetFrames - 1, b.Buffered);
        // a full buffer (the audio thread is not running): refused, never overwritten, never blocking
        var full = new VoiceStream(11);
        int taken = 0; for (int f = 0; f < 40; f++) if (full.Push((ushort)f, 1, 0, frames[f], 0, 163)) taken++;
        Assert.Equal((32, 8), (taken, full.Refused));
    }

    [Fact]
    public void A_muted_stream_takes_nothing_and_plays_nothing_and_unknown_codecs_are_skipped()
    {
        var frames = Encode(Sine(500, 9000, 320 * 8));
        var s = new VoiceStream(12) { Accept = false };
        var block = new float[320];
        for (int f = 0; f < 8; f++) Assert.False(s.Push((ushort)f, 1, 0, frames[f], 0, 163));
        s.Read(block, 0, 320);
        Assert.All(block, x => Assert.Equal(0f, x));
        Assert.Equal(0, s.Pushed);
        s.Accept = true;
        Assert.False(s.Push(0, 9, 0, frames[0], 0, 163));   // codec 9: a newer client's
        Assert.Equal(1, s.UnknownCodec);
        Assert.False(s.Push(1, 1, 0, frames[1], 0, 100));   // a truncated frame
        Assert.Equal(1, s.Bad);
        // the gain scales what is played
        s.Gain = 0.5f;
        for (int f = 0; f < 5; f++) s.Push((ushort)f, 1, 0, frames[f], 0, 163);
        s.Read(block, 0, 320); s.Read(block, 0, 320);
        Assert.InRange(RmsDb(block, 0, 320) - (20 * Math.Log10(9000 / 32768.0) - 3.01), -6.02 - 0.5, -6.02 + 0.5);
    }

    [Fact]
    public async Task One_writer_and_one_reader_on_two_threads_lose_nothing()
    {
        var frames = Encode(Sine(700, 9000, 320 * 25));
        var s = new VoiceStream(13);
        int total = 1500;
        var writer = Task.Run(async () =>
        {
            for (int f = 0; f < total; f++)
            {
                while (s.Buffered >= 31) await Task.Yield(); // the network never retries a frame: wait for room instead
                Assert.True(s.Push((ushort)f, 1, (byte)(f == total - 1 ? 2 : 0), frames[f % 25], 0, 163));
                if (f % 7 == 0) await Task.Yield();
            }
        });
        var block = new float[256]; long nonZero = 0;
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while ((s.Played + s.LateSkipped < total || s.Playing) && DateTime.UtcNow < deadline)
        {
            s.Read(block, 0, block.Length);
            foreach (var x in block) if (x != 0) nonZero++;
            if (s.Buffered == 0) await Task.Yield();
        }
        await writer;
        Assert.Equal(total, s.Played + s.LateSkipped);
        Assert.Equal(0, s.Gaps);
        Assert.True(nonZero > (long)(s.Played - 2) * 300);
    }

    // ------------------------------------------------------------------------------------------------ the resampler

    [Theory]
    [InlineData(16000, 48000)]
    [InlineData(16000, 44100)]
    [InlineData(48000, 16000)]
    [InlineData(44100, 16000)]
    [InlineData(16000, 16000)]
    public void The_resampler_keeps_a_tones_frequency_and_level(int inRate, int outRate)
    {
        var r = new VoiceResampler(inRate, outRate);
        var src = Enumerable.Range(0, inRate).Select(i => (float)(0.25 * Math.Sin(2 * Math.PI * 1000 * i / inRate))).ToArray();
        var dst = new float[outRate + 64];
        // fed in uneven chunks, as an audio callback would
        int made = 0, used = 0, chunk = 0;
        while (used < src.Length)
        {
            int n = Math.Min(src.Length - used, 100 + (chunk++ * 37) % 400);
            made += r.Process(src, used, n, dst, made, dst.Length - made, out int c);
            Assert.Equal(n, c);
            used += n;
        }
        Assert.InRange(made, outRate - 4, outRate + 4); // one second in, one second out (give or take the first input sample's outputs)
        Assert.InRange(ZeroCrossHz(dst, 1000, made - 1000, outRate), 999.5, 1000.5);
        Assert.InRange(RmsDb(dst, 1000, made - 1000) - (20 * Math.Log10(0.25) - 3.01), -0.1, 0.1);
        Assert.InRange(ToneAmplitude(dst, 1000, made - 1000, 1000, outRate), 0.247, 0.253);
    }

    [Fact]
    public void Going_up_the_images_and_going_down_the_aliases_are_at_least_50_db_down()
    {
        // 16 kHz → 48 kHz: a 3 kHz tone has images at 13 and 19 kHz
        var up = new VoiceResampler(16000, 48000);
        var src = Enumerable.Range(0, 16000).Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * 3000 * i / 16000.0))).ToArray();
        var dst = new float[48000];
        Assert.InRange(up.Process(src, 0, src.Length, dst, 0, dst.Length, out _), 47997, 48000);
        double tone = ToneAmplitude(dst, 2000, 46000, 3000, 48000);
        foreach (var img in new[] { 13000.0, 19000.0 })
            Assert.True(20 * Math.Log10(ToneAmplitude(dst, 2000, 46000, img, 48000) / tone) < -50, $"image at {img} Hz");
        // 48 kHz → 16 kHz: a 12 kHz tone would alias to 4 kHz; a 1 kHz tone beside it passes
        var down = new VoiceResampler(48000, 16000);
        var both = Enumerable.Range(0, 48000).Select(i => (float)(0.4 * Math.Sin(2 * Math.PI * 12000 * i / 48000.0) + 0.4 * Math.Sin(2 * Math.PI * 1000 * i / 48000.0))).ToArray();
        var d2 = new float[16100];
        int made = down.Process(both, 0, both.Length, d2, 0, d2.Length, out _);
        Assert.InRange(made, 15998, 16002);
        double pass = ToneAmplitude(d2, 1000, made - 1000, 1000, 16000), alias = ToneAmplitude(d2, 1000, made - 1000, 4000, 16000);
        Assert.InRange(pass, 0.39, 0.41);
        Assert.True(20 * Math.Log10(alias / pass) < -50, $"alias {20 * Math.Log10(alias / pass):F1} dB");
    }

    [Fact]
    public void The_resampler_says_how_much_input_an_output_block_needs()
    {
        var r = new VoiceResampler(16000, 48000);
        var src = new float[4096]; var dst = new float[1024];
        int total = 0;
        for (int block = 0; block < 200; block++)
        {
            int need = r.InputNeeded(1024);
            Assert.InRange(need, 341, 342);
            Assert.Equal(1024, r.Process(src, 0, need, dst, 0, 1024, out int used));
            Assert.Equal(need, used);
            total += need;
        }
        Assert.InRange(total, 200 * 1024 / 3 - 1, 200 * 1024 / 3 + 1); // no drift
    }

    // ------------------------------------------------------------------------------------------------ the wire format, version 2

    static byte[] Payload(byte[] frame)
    {
        Assert.Equal(frame.Length - 2, frame[0] | frame[1] << 8);
        return frame[2..];
    }

    static byte[] VoiceFrame(string map, ushort seq, byte flags = 0, int dataLen = 163, byte codec = 1, byte fill = 0x5a)
    {
        var data = Enumerable.Repeat(fill, dataLen).ToArray();
        var buf = new byte[2 + PresenceWire.MaxPayloadVoice];
        int n = PresenceWire.WriteVoice(buf, 0, map, seq, codec, flags, data, 0, data.Length);
        Assert.True(n > 0);
        return buf[..n];
    }

    [Fact]
    public void Hello_and_welcome_carry_a_version_and_version_1_is_unchanged()
    {
        // version 1, byte for byte what clients before wave 6 send and expect
        var h1 = Payload(PresenceWire.EncodeHello(2000001, "Tester"));
        Assert.Equal(new byte[] { 0x01, (byte)'L', (byte)'K', (byte)'P', (byte)'R', 1, 0x81, 0x84, 0x1e, 0x00, 6, (byte)'T', (byte)'e', (byte)'s', (byte)'t', (byte)'e', (byte)'r' }, h1);
        Assert.Equal(new byte[] { 0x81, (byte)'L', (byte)'K', (byte)'P', (byte)'R', 1 }, Payload(PresenceWire.EncodeWelcome()));
        Assert.Equal(Payload(PresenceWire.EncodeWelcome()), Payload(PresenceWire.EncodeWelcome(1, PresenceWire.CapVoice)));
        // version 2: the same HELLO with a 2, and a WELCOME with the capability byte
        var h2 = Payload(PresenceWire.EncodeHello(2000001, "Tester", PresenceWire.VersionVoice));
        Assert.Equal(2, h2[5]);
        Assert.True(PresenceWire.TryDecodeHello(h2, 0, h2.Length, out var aid, out _, out var name, out var ver));
        Assert.Equal((2000001u, "Tester", (byte)2), (aid, name, ver));
        Assert.True(PresenceWire.TryDecodeHello(h1, 0, h1.Length, out _, out _, out _, out ver) && ver == 1);
        var w2 = Payload(PresenceWire.EncodeWelcome(2, PresenceWire.CapVoice));
        Assert.Equal(new byte[] { 0x81, (byte)'L', (byte)'K', (byte)'P', (byte)'R', 2, 1 }, w2);
        Assert.True(PresenceWire.TryDecodeWelcome(w2, 0, w2.Length, out ver, out var caps) && ver == 2 && caps == 1);
        var w1 = Payload(PresenceWire.EncodeWelcome());
        Assert.True(PresenceWire.TryDecodeWelcome(w1, 0, w1.Length, out ver, out caps) && ver == 1 && caps == 0);
        // what a client from before wave 6 does with a WELCOME 2 (its decoder: the 4-argument one, exact length, version 1): refuses it.
        // This is why the relay answers a HELLO 1 with a WELCOME 1.
        Assert.False(PresenceWire.TryDecodeWelcome(w2, 0, w2.Length, out byte oldVer) && oldVer == PresenceWire.Version);
        var w3 = (byte[])w2.Clone(); w3[5] = 3;
        Assert.False(PresenceWire.TryDecodeWelcome(w3, 0, w3.Length, out _, out _));
        var h3 = (byte[])h2.Clone(); h3[5] = 3;
        Assert.False(PresenceWire.TryDecodeHello(h3, 0, h3.Length, out _, out _, out _, out _));
    }

    [Fact]
    public void A_voice_frame_round_trips_and_the_forward_adds_the_hellos_aid_and_name()
    {
        var v = Payload(VoiceFrame("prontera", 0xbeef, PresenceWire.VoiceFlagFirst));
        Assert.Equal(1 + 1 + 8 + 2 + 1 + 1 + 163, v.Length);
        Assert.True(v.Length <= PresenceWire.MaxPayload); // a speaker's ADPCM frame even fits the old 200-byte bound; the forward does not
        Assert.True(PresenceWire.TryReadVoice(v, 0, v.Length, out var h));
        Assert.Equal(("prontera", (ushort)0xbeef, (byte)1, (byte)1, 163), (Encoding.ASCII.GetString(v, h.MapOffset, h.MapLength), h.Seq, h.Codec, h.Flags, h.DataLength));
        Assert.All(v.Skip(h.DataOffset).Take(h.DataLength), b => Assert.Equal(0x5a, b));

        var name = PresenceWire.NameBytes(new string('é', 40)); // 48 bytes
        var longest = Payload(VoiceFrame("0001@tower_abcd", 7, 0, PresenceWire.MaxVoiceData));
        var fwd = PresenceWire.EncodeVoiceOf(2000001, name, longest, 0, longest.Length);
        Assert.Equal(PresenceWire.MaxPayloadVoice, fwd.Length - 2);
        Assert.Equal(274, PresenceWire.MaxPayloadVoice);
        var f = Payload(fwd);
        Assert.True(PresenceWire.TryReadVoiceOf(f, 0, f.Length, out var fh));
        Assert.Equal((2000001u, new string('é', 24), "0001@tower_abcd", (ushort)7, 200),
            (fh.Aid, Encoding.UTF8.GetString(f, fh.NameOffset, fh.NameLength), Encoding.ASCII.GetString(f, fh.MapOffset, fh.MapLength), fh.Seq, fh.DataLength));

        var e = Payload(PresenceWire.EncodeVoiceEnd(42));
        Assert.True(PresenceWire.TryDecodeVoiceEnd(e, 0, e.Length, out var gone) && gone == 42);
        Assert.Equal(new byte[] { PresenceWire.VoiceStop }, Payload(PresenceWire.EncodeVoiceStop()));
    }

    [Fact]
    public void Malformed_voice_frames_are_refused_and_never_written()
    {
        var buf = new byte[400]; var data = new byte[300];
        Assert.Equal(0, PresenceWire.WriteVoice(buf, 0, "", 1, 1, 0, data, 0, 163));                 // no map
        Assert.Equal(0, PresenceWire.WriteVoice(buf, 0, "pront era", 1, 1, 0, data, 0, 163));        // a bad map name
        Assert.Equal(0, PresenceWire.WriteVoice(buf, 0, new string('a', 16), 1, 1, 0, data, 0, 163)); // too long a map name
        Assert.Equal(0, PresenceWire.WriteVoice(buf, 0, "prontera", 1, 1, 0, data, 0, 0));            // no data
        Assert.Equal(0, PresenceWire.WriteVoice(buf, 0, "prontera", 1, 1, 0, data, 0, 201));          // too much data
        Assert.Equal(0, PresenceWire.WriteVoice(buf, 0, "prontera", 1, 0, 0, data, 0, 163));          // codec 0
        Assert.Equal(0, PresenceWire.WriteVoice(new byte[50], 0, "prontera", 1, 1, 0, data, 0, 163)); // no room

        var ok = Payload(VoiceFrame("prontera", 1));
        Assert.False(PresenceWire.TryReadVoice(ok, 0, 1 + 1 + 8 + 4, out _));       // cut before the data
        Assert.False(PresenceWire.TryReadVoice(ok, 0, 5, out _));                   // cut inside the map
        var badMap = (byte[])ok.Clone(); badMap[3] = (byte)' ';
        Assert.False(PresenceWire.TryReadVoice(badMap, 0, badMap.Length, out _));
        var noCodec = (byte[])ok.Clone(); noCodec[1 + 1 + 8 + 2] = 0;
        Assert.False(PresenceWire.TryReadVoice(noCodec, 0, noCodec.Length, out _));
        var tooLong = ok.Concat(new byte[40]).ToArray(); // 203 bytes of data
        Assert.False(PresenceWire.TryReadVoice(tooLong, 0, tooLong.Length, out _));
        var pose = (byte[])ok.Clone(); pose[0] = PresenceWire.Pose;
        Assert.False(PresenceWire.TryReadVoice(pose, 0, pose.Length, out _));
        var of = Payload(PresenceWire.EncodeVoiceOf(0, new byte[0], ok, 0, ok.Length)); // AID 0
        Assert.False(PresenceWire.TryReadVoiceOf(of, 0, of.Length, out _));
    }

    [Fact]
    public void A_frame_reader_takes_274_bytes_only_when_told_to_and_reads_without_copying()
    {
        var name = PresenceWire.NameBytes(new string('n', 48));
        var longest = Payload(VoiceFrame("0001@tower_abcd", 7, 0, PresenceWire.MaxVoiceData));
        var fwd = PresenceWire.EncodeVoiceOf(9, name, longest, 0, longest.Length);
        var old = new PresenceWire.FrameReader();                 // a version-1 connection: 200 bytes at most
        old.Append(fwd, 0, fwd.Length);
        Assert.Equal(-1, old.Next(out _));
        var v2 = new PresenceWire.FrameReader(PresenceWire.MaxPayloadVoice);
        var stream = fwd.Concat(PresenceWire.EncodePing()).Concat(fwd).ToArray();
        for (int cut = 1; cut < stream.Length; cut += 7)
        {
            var fr = new PresenceWire.FrameReader(PresenceWire.MaxPayloadVoice);
            var got = new List<(byte type, int len)>();
            fr.Append(stream, 0, cut);
            while (fr.NextView(out var b, out int o, out int n) == 1) got.Add((b[o], n));
            fr.Append(stream, cut, stream.Length - cut);
            while (fr.NextView(out var b, out int o, out int n) == 1) got.Add((b[o], n));
            Assert.Equal(new[] { (PresenceWire.VoiceOf, 274), (PresenceWire.Ping, 1), (PresenceWire.VoiceOf, 274) }, got);
        }
        v2.Append(new byte[] { 0x13, 0x01 }, 0, 2); // 275: one over
        Assert.Equal(-1, v2.NextView(out _, out _, out _));
    }

    // ------------------------------------------------------------------------------------------------ the relay

    /// <summary>A raw relay client that can say HELLO 1 or 2.</summary>
    sealed class Client : IDisposable
    {
        public readonly TcpClient Tcp = new() { NoDelay = true };
        readonly PresenceWire.FrameReader _fr;
        readonly byte[] _buf = new byte[8192];
        public byte Caps;
        public Client(int port, bool voice = true)
        {
            Tcp.Connect(IPAddress.Loopback, port);
            _fr = new PresenceWire.FrameReader(voice ? PresenceWire.MaxPayloadVoice : PresenceWire.MaxPayload);
        }
        public void Send(byte[] frame) => Tcp.GetStream().Write(frame);
        public Client Hello(uint aid, string name, byte version = PresenceWire.VersionVoice)
        {
            Send(PresenceWire.EncodeHello(aid, name, version));
            var w = Next(2000) ?? throw new Exception("no WELCOME");
            Assert.True(PresenceWire.TryDecodeWelcome(w, 0, w.Length, out var ver, out Caps), "not a WELCOME");
            Assert.Equal(version, ver);
            return this;
        }
        public void Voice(string map, ushort seq, byte flags = 0, byte fill = 0x5a) => Send(VoiceFrame(map, seq, flags, fill: fill));

        /// <summary>The next payload, or null after <paramref name="ms"/>; throws when the relay closed the connection or sent a
        /// frame this client's reader refuses (a version-1 reader: anything over 200 bytes).</summary>
        public byte[]? Next(int ms)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(ms);
            while (true)
            {
                int k = _fr.Next(out var p);
                if (k == 1) return p;
                if (k < 0) throw new InvalidDataException("a frame longer than this client accepts");
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) return null;
                Tcp.Client.ReceiveTimeout = Math.Max(1, (int)left.TotalMilliseconds);
                int n;
                try { n = Tcp.GetStream().Read(_buf, 0, _buf.Length); }
                catch (IOException e) when (e.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut }) { return null; }
                if (n <= 0) throw new EndOfStreamException("closed by the relay");
                _fr.Append(_buf, 0, n);
            }
        }

        public bool ClosedWithin(int ms)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(ms);
            try { while (DateTime.UtcNow < deadline) Next((int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds)); }
            catch (Exception e) when (e is EndOfStreamException or IOException) { return true; }
            return false;
        }

        public (uint aid, string name, string map, ushort seq, byte flags, byte fill) VoiceOf(int ms = 2000)
        {
            var p = Next(ms) ?? throw new Exception("no VOICE_OF");
            Assert.True(PresenceWire.TryReadVoiceOf(p, 0, p.Length, out var h), $"not a VOICE_OF: 0x{p[0]:x2}");
            return (h.Aid, Encoding.UTF8.GetString(p, h.NameOffset, h.NameLength), Encoding.ASCII.GetString(p, h.MapOffset, h.MapLength), h.Seq, h.Flags, p[h.DataOffset]);
        }

        public uint VoiceEndOf(int ms = 2000)
        {
            var p = Next(ms) ?? throw new Exception("no VOICE_END");
            Assert.True(PresenceWire.TryDecodeVoiceEnd(p, 0, p.Length, out var aid), $"not a VOICE_END: 0x{p[0]:x2}");
            return aid;
        }

        public void Dispose() => Tcp.Dispose();
    }

    static async Task<PoseRelay> Relay(PoseRelay.Limits? limits = null, List<string>? log = null, bool voice = true)
    {
        var r = new PoseRelay(0, limits, voice);
        if (log != null) r.Log += l => { lock (log) log.Add(l); };
        r.Start(IPAddress.Loopback);
        await Task.Yield();
        return r;
    }

    static async Task Until(Func<bool> cond, int ms = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (!cond() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(cond());
    }

    static PresenceWire.PoseBody SamplePose(string map)
    {
        var p = new PresenceWire.PoseBody { Map = map, Seq = 1, MetresPerCell = 0.03f, Flags = 3 };
        float[] v = { 150, 20, 180, 0, 0, 0, 1, 146, 12, 180, 0, 0, 0, 1, 154, 12, 180, 0, 0, 0, 1 };
        v.CopyTo(p.V, 0);
        return p;
    }

    [Fact]
    public async Task Voice_reaches_every_other_voice_client_whatever_its_map_with_the_hellos_aid_and_name()
    {
        var log = new List<string>();
        await using var r = await Relay(log: log);
        using var a = new Client(r.Port).Hello(2000001, "Tester");
        using var b = new Client(r.Port).Hello(2000002, "Smoke Tester");
        using var c = new Client(r.Port).Hello(2000003, "Elsewhere");
        Assert.Equal(PresenceWire.CapVoice, a.Caps);
        a.Voice("prontera", 100, PresenceWire.VoiceFlagFirst, 0x11);
        a.Voice("prontera", 101, 0, 0x12);
        foreach (var x in new[] { b, c })
        {
            Assert.Equal((2000001u, "Tester", "prontera", (ushort)100, PresenceWire.VoiceFlagFirst, (byte)0x11), x.VoiceOf());
            Assert.Equal((2000001u, "Tester", "prontera", (ushort)101, (byte)0, (byte)0x12), x.VoiceOf());
        }
        Assert.Null(a.Next(150)); // never its own voice back
        // the claimed AID inside a frame does not exist: the relay stamps the HELLO's; a speaker on another map is heard too
        c.Voice("izlude", 5);
        Assert.Equal((2000003u, "Elsewhere", "izlude"), (a.VoiceOf() is var g ? (g.aid, g.name, g.map) : default));
        Assert.Equal(2000003u, b.VoiceOf().aid);
        Assert.Equal((3L, 6L, 0L), (r.VoiceIn, r.VoiceForwarded, r.VoiceDropped));
        Assert.Contains("voice in 3, voice forwarded 6, voice dropped 0", r.StatsLine());
        lock (log) Assert.Single(log, l => l == "Tester (AID 2000001) shares voice"); // one line per speaker, never per frame
    }

    [Fact]
    public async Task Voice_stop_and_a_disconnect_tell_the_listeners_voice_end()
    {
        await using var r = await Relay();
        using var a = new Client(r.Port).Hello(11, "A");
        using var b = new Client(r.Port).Hello(12, "B");
        a.Voice("prontera", 1); Assert.Equal(11u, b.VoiceOf().aid);
        a.Send(PresenceWire.EncodeVoiceStop());
        Assert.Equal(11u, b.VoiceEndOf());
        a.Send(PresenceWire.EncodeVoiceStop());              // twice: nothing more (it is not talking)
        a.Voice("prontera", 2); Assert.Equal(11u, b.VoiceOf().aid);
        a.Tcp.Client.LingerState = new LingerOption(true, 0); // killed: the OS resets the connection
        a.Tcp.Client.Close();
        Assert.Equal(11u, b.VoiceEndOf());
        await Until(() => r.Snapshot().All(x => x.aid != 11));
        // the relay keeps serving the one left: it can talk (to nobody), ping, and a newcomer hears it
        b.Voice("prontera", 9);
        b.Send(PresenceWire.EncodePing());
        await Until(() => r.VoiceIn == 3);
        using var c = new Client(r.Port).Hello(13, "C");
        b.Voice("prontera", 10);
        Assert.Equal((12u, (ushort)10), (c.VoiceOf() is var g ? (g.aid, g.seq) : default));
        // a speaker that never talked leaves without a VOICE_END
        c.Tcp.Client.Close();
        Assert.Null(b.Next(300));
    }

    [Fact]
    public async Task A_client_from_before_voice_gets_exactly_what_it_got_and_never_a_voice_frame()
    {
        var log = new List<string>();
        await using var r = await Relay(log: log);
        using var old = new Client(r.Port, voice: false);
        old.Send(PresenceWire.EncodeHello(21, "Old"));                       // HELLO 1
        var w = old.Next(2000)!;
        Assert.Equal(Payload(PresenceWire.EncodeWelcome()), w);              // the same six bytes as before wave 6
        using var a = new Client(r.Port).Hello(22, "New");
        using var b = new Client(r.Port).Hello(23, "New too");
        old.Send(PresenceWire.EncodePose(SamplePose("prontera")));
        await Until(() => r.Snapshot().Any(x => x.aid == 21 && x.map == "prontera"));
        a.Send(PresenceWire.EncodePose(SamplePose("prontera")));
        // the old client sees the new one's pose …
        var p = old.Next(2000)!;
        Assert.Equal(PresenceWire.PoseOf, p[0]);
        // … and the new one talks: the other new client hears it, the old one gets nothing (its reader would close on 200+ bytes:
        // Next throws for such a frame)
        for (ushort s = 0; s < 20; s++) a.Voice("prontera", s);
        for (ushort s = 0; s < 20; s++) Assert.Equal(22u, b.VoiceOf().aid);
        a.Send(PresenceWire.EncodeVoiceStop());
        Assert.Equal(22u, b.VoiceEndOf());
        Assert.Null(old.Next(300));
        // the old client's poses still reach the new ones
        old.Send(PresenceWire.EncodePose(SamplePose("prontera")));
        var q = a.Next(2000)!;
        Assert.Equal(PresenceWire.PoseOf, q[0]);
        // a version-1 connection sending a voice frame is closed, as any unknown message always was
        old.Send(VoiceFrame("prontera", 1));
        Assert.True(old.ClosedWithin(2000));
        await Until(() => { lock (log) return log.Any(l => l.Contains("Old (AID 21) disconnected: unknown message 0x05")); });
    }

    [Fact]
    public async Task With_voice_switched_off_the_welcome_says_so_and_voice_is_dropped_while_poses_go_on()
    {
        await using var r = await Relay(voice: false);
        Assert.False(r.VoiceEnabled);
        using var a = new Client(r.Port).Hello(31, "A");
        using var b = new Client(r.Port).Hello(32, "B");
        Assert.Equal(0, a.Caps);
        a.Voice("prontera", 1);
        Assert.Null(b.Next(300));
        Assert.Equal((0L, 0L, 1L), (r.VoiceIn, r.VoiceForwarded, r.VoiceDropped));
        b.Send(PresenceWire.EncodePose(SamplePose("prontera")));
        await Until(() => r.Snapshot().Any(x => x.aid == 32 && x.map == "prontera"));
        a.Send(PresenceWire.EncodePose(SamplePose("prontera")));
        Assert.Equal(PresenceWire.PoseOf, b.Next(2000)![0]);
    }

    [Fact]
    public async Task Voice_above_the_rate_limit_is_dropped_and_malformed_voice_closes_the_connection()
    {
        await using var r = await Relay(new PoseRelay.Limits { MaxVoicePerSecond = 10 });
        using var a = new Client(r.Port).Hello(41, "Flood");
        using var b = new Client(r.Port).Hello(42, "B");
        for (ushort s = 0; s < 200; s++) a.Voice("prontera", s);
        int got = 0;
        while (b.Next(400) != null) got++;
        Assert.InRange(got, 10, 20);             // one window, or the start of a second one
        Assert.True(r.VoiceDropped >= 180);
        Assert.Equal(got, (int)r.VoiceForwarded);
        // the default bound: 75 a second lets a talking client (50 a second) through whole, with the burst after a short stall
        Assert.Equal(75, new PoseRelay.Limits().MaxVoicePerSecond);
        // malformed: a voice frame with no data, one with a bad map, an oversized frame
        using (var bad = new Client(r.Port).Hello(43, "Bad"))
        {
            bad.Send(new byte[] { 14, 0, PresenceWire.Voice, 8, (byte)'p', (byte)'r', (byte)'o', (byte)'n', (byte)'t', (byte)'e', (byte)'r', (byte)'a', 1, 0, 1, 0 });
            Assert.True(bad.ClosedWithin(2000));
        }
        using (var big = new Client(r.Port).Hello(44, "Big"))
        {
            big.Send(new byte[] { 0x13, 0x01 }.Concat(new byte[275]).ToArray()); // 275 bytes: over the version-2 bound
            Assert.True(big.ClosedWithin(2000));
        }
        using (var stop = new Client(r.Port).Hello(45, "Stop"))
        {
            stop.Send(new byte[] { 2, 0, PresenceWire.VoiceStop, 0 });
            Assert.True(stop.ClosedWithin(2000));
        }
        await Until(() => r.Snapshot().Count == 2);
    }

    [Fact]
    public async Task A_slow_listeners_queue_is_bounded_and_drops_the_oldest_voice_first()
    {
        // a listener that does not read: the relay keeps at most VoiceQueueFrames for it (plus what the sockets hold) and stays up
        await using var r = await Relay(new PoseRelay.Limits { MaxVoicePerSecond = 100000, VoiceQueueFrames = 16 });
        using var a = new Client(r.Port).Hello(51, "Talker");
        using var slow = new Client(r.Port).Hello(52, "Slow");
        slow.Tcp.ReceiveBufferSize = 1024;
        using var fast = new Client(r.Port).Hello(53, "Fast");
        // the speaker talks in small batches, each heard by the fast listener before the next (so only the slow one falls behind),
        // until the slow one's sockets are full and the relay starts dropping from its queue
        int sent = 0, heard = 0;
        while (r.QueueDropped == 0 && sent < 60000)
        {
            for (int i = 0; i < 8; i++) a.Voice("prontera", (ushort)sent++);
            for (int i = 0; i < 8; i++) Assert.Equal((ushort)heard++, fast.VoiceOf(5000).seq); // every frame, in order
        }
        Assert.True(r.QueueDropped > 0, $"nothing was dropped from the slow listener's queue after {sent} frames");
        for (int batch = 0; batch < 8; batch++)
        {
            for (int i = 0; i < 8; i++) a.Voice("prontera", (ushort)sent++);
            for (int i = 0; i < 8; i++) Assert.Equal((ushort)heard++, fast.VoiceOf(5000).seq);
        }
        // the slow one, reading at last, gets the newest frames, not the oldest (the oldest were dropped)
        var seqs = new List<ushort>();
        while (true) { var p = slow.Next(300); if (p == null) break; Assert.True(PresenceWire.TryReadVoiceOf(p, 0, p.Length, out var h)); seqs.Add(h.Seq); }
        Assert.Equal((ushort)(sent - 1), seqs[^1]);
        Assert.True(seqs.Count < sent);
        Assert.True(seqs.SequenceEqual(seqs.OrderBy(x => x)));
        // and the speaker was never blocked: the relay took all its frames
        Assert.Equal((long)sent, r.VoiceIn);
    }

    [Fact]
    public async Task A_replaced_connection_hands_its_voice_to_its_successor()
    {
        await using var r = await Relay();
        using var listener = new Client(r.Port).Hello(61, "Listener");
        using var first = new Client(r.Port).Hello(62, "Twice");
        first.Voice("prontera", 1); Assert.Equal(62u, listener.VoiceOf().aid);
        using var second = new Client(r.Port).Hello(62, "Twice");
        Assert.True(first.ClosedWithin(2000));
        Assert.Equal(62u, listener.VoiceEndOf());         // the old stream is freed …
        second.Voice("prontera", 1);
        Assert.Equal(62u, listener.VoiceOf().aid);        // … and the new connection is heard
    }

    // ------------------------------------------------------------------------------------------------ Loki Host

    [Fact]
    public async Task Host_settings_carry_a_voice_switch_that_defaults_to_on_and_reaches_the_relay()
    {
        var old = JsonSerializer.Deserialize<HostSettings>("{\"LoginPort\":7900,\"PresencePort\":7903,\"PresenceEnabled\":true,\"SetUp\":true}")!; // a file from before voice
        Assert.True(old.VoiceEnabled);
        Assert.Equal(new[] { 7900, 7901, 7902, 7903 }, old.Ports);                        // the same port, the same firewall rule
        Assert.Equal("Loki Host (TCP 7900-7903)", Net.FirewallRuleName(old));
        Assert.Contains("\"VoiceEnabled\": false", JsonSerializer.Serialize(new HostSettings { VoiceEnabled = false }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.Contains("headsets and voices", ShareText.RelayPortHint(old));
        Assert.DoesNotContain("voices", ShareText.RelayPortHint(new HostSettings { VoiceEnabled = false }));
        // the text for friends is unchanged by voice (same port, nothing to do on their side)
        Assert.Equal(ShareText.Build(new HostSettings { VoiceEnabled = false }, "203.0.113.7", "", null, ShareText.English),
                     ShareText.Build(new HostSettings { VoiceEnabled = true }, "203.0.113.7", "", null, ShareText.English));

        foreach (bool voice in new[] { true, false })
        {
            int port;
            using (var probe = new TcpListener(IPAddress.Loopback, 0)) { probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; }
            var dir = Path.Combine(Path.GetTempPath(), "lokihost-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "log"));
            var h = new HostController(new HostSettings { HostDir = dir, PresencePort = port, VoiceEnabled = voice });
            await h.StartPresenceAsync(_ => { });
            Assert.Equal(voice, h.Relay!.VoiceEnabled);
            using (var c = new Client(port).Hello(71, "Host test")) Assert.Equal((voice ? PresenceWire.CapVoice : 0) | Loki.Horde.Net.HordeWire.CapHorde | PresenceWire.CapSettings | GestureWire.CapGesture | DrawWire.CapDraw | SpotWire.CapSpot, c.Caps); // (wfree: and the exact spots) (client wave 8: Loki Host runs Horde by default; client wave 18, w18acct: and keeps settings per account) (w18share: and forwards casting menus; both at the wave 18 merge) (client wave 20, w20pen: and the drawings)
            await h.StopAsync(_ => { });
            Assert.Contains($"headset relay listening on port {port} (voice {(voice ? "on" : "off")})", File.ReadAllText(Path.Combine(dir, "log", "lokihost-presence.log")));
        }
    }
}
