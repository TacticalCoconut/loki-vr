using System;
using UnityEngine;

namespace Loki.Voice
{
    /// <summary>Where my voice comes from: 20 ms frames of 16 kHz mono (the real microphone; a generated signal in the smokes, which
    /// never open the microphone).</summary>
    public interface IVoiceInput : IDisposable
    {
        string Describe { get; }
        /// <summary>The device is open and delivering.</summary>
        bool Running { get; }
        /// <summary>Main thread: fills <paramref name="frame"/> (320 samples, −1..1) when a whole frame is ready; false when not yet.</summary>
        bool TryRead(float[] frame);
    }

    /// <summary>
    /// The microphone through Unity's Microphone class (client wave 6, w6voice). What Unity documents and this relies on
    /// (docs.unity3d.com/2022.3/Documentation/ScriptReference/Microphone.Start.html, .GetPosition.html, .GetDeviceCaps.html,
    /// AudioClip.GetData.html, read 2026-10-04):
    ///   • Microphone.Start(device, loop, lengthSec, frequency) records into an AudioClip of lengthSec seconds and returns it, or
    ///     null "if the recording fails to start"; with loop it wraps round and keeps recording; a null or empty device name is
    ///     the default microphone (Windows' default recording device);
    ///   • Microphone.GetPosition(device) is "the current position in the recording buffer, in samples";
    ///   • Microphone.GetDeviceCaps gives the device's lowest and highest sampling frequency, both 0 when it "supports any frequency";
    ///   • AudioClip.GetData(array, offset) fills the array from that position and "wraps around" at the clip's end.
    /// So: a looping one-second clip, read each frame from the last read position up to GetPosition in whole 20 ms pieces. At
    /// 16 kHz when the device offers it (Unity then delivers 16 kHz: no conversion here); otherwise at 48 kHz, or the device's own
    /// highest rate, brought down to 16 kHz by <see cref="VoiceResampler"/>. After a stall of the game (a map loading) only the
    /// newest <see cref="MaxBacklogFrames"/> frames are kept: speech that is already late is dropped, not sent in a burst.
    /// NOT verified by any automated run: no smoke opens a microphone (they use a generated signal: LokiGame.W6VoiceSmoke.cs).
    /// </summary>
    public sealed class MicrophoneInput : IVoiceInput
    {
        public const int MaxBacklogFrames = 5;
        private readonly string _device; // null = the default microphone
        private AudioClip _clip; private int _rate, _chunk, _readPos;
        private float[] _in; private VoiceResampler _down;
        private readonly float[] _fifo = new float[4 * VoiceStream.FrameSamples]; private int _fifoCount;
        public string Describe { get; private set; } = "";
        public bool Running => _clip != null && Microphone.IsRecording(_device);
        /// <summary>Why the microphone could not be opened (null when it runs).</summary>
        public string Error { get; private set; }
        public int Rate => _rate;

        /// <summary>The names Unity lists (Microphone.devices): listing opens nothing.</summary>
        public static string[] Devices() { try { return Microphone.devices ?? new string[0]; } catch (Exception e) { Debug.LogWarning("[Voice] listing the microphones: " + e.Message); return new string[0]; } }

        public MicrophoneInput(string device) { _device = string.IsNullOrEmpty(device) ? null : device; }

        public bool Start()
        {
            try
            {
                // (the reasons are shown to the player, in the chat line and on the Voice page: through Lang)
                if (_device != null && Array.IndexOf(Devices(), _device) < 0) { Error = Lang.T("that microphone is not connected"); return false; }
                if (_device == null && Devices().Length == 0) { Error = Lang.T("no microphone found"); return false; }
                Microphone.GetDeviceCaps(_device, out int min, out int max);
                bool any = min == 0 && max == 0;
                _rate = any || (min <= VoiceStream.SampleRate && VoiceStream.SampleRate <= max) ? VoiceStream.SampleRate
                      : (min <= 48000 && 48000 <= max) ? 48000 : max;
                _clip = Microphone.Start(_device, true, 1, _rate);
                if (_clip == null) { Error = Lang.T("the recording did not start"); return false; }
                _rate = _clip.frequency;
                _chunk = _rate / 50; // 20 ms of the device's rate
                _in = new float[_chunk * Mathf.Max(1, _clip.channels)];
                _down = _rate == VoiceStream.SampleRate ? null : new VoiceResampler(_rate, VoiceStream.SampleRate);
                _readPos = Microphone.GetPosition(_device); _fifoCount = 0;
                Describe = $"'{(_device ?? "(Windows' default)")}' at {_rate} Hz{(_down != null ? " (resampled to 16000)" : "")}, {_clip.channels} channel(s); the device offers {(any ? "any rate" : min + "-" + max + " Hz")}";
                Error = null;
                return true;
            }
            catch (Exception e) { Error = e.Message; _clip = null; return false; }
        }

        public bool TryRead(float[] frame)
        {
            if (_clip == null) return false;
            int n = VoiceStream.FrameSamples;
            while (_fifoCount < n)
            {
                int total = _clip.samples, pos = Microphone.GetPosition(_device);
                int avail = pos - _readPos; if (avail < 0) avail += total;
                if (avail < _chunk) return false;
                // the game stalled: keep only the newest frames
                if (avail > (MaxBacklogFrames + 1) * _chunk) { _readPos = (pos - MaxBacklogFrames * _chunk + total) % total; _fifoCount = 0; _down?.Reset(); }
                if (!_clip.GetData(_in, _readPos)) return false;
                _readPos = (_readPos + _chunk) % total;
                int ch = _clip.channels;
                if (ch > 1) for (int i = 0; i < _chunk; i++) _in[i] = _in[i * ch]; // the first channel
                if (_down == null) { Array.Copy(_in, 0, _fifo, _fifoCount, _chunk); _fifoCount += _chunk; }
                else _fifoCount += _down.Process(_in, 0, _chunk, _fifo, _fifoCount, _fifo.Length - _fifoCount, out _);
            }
            Array.Copy(_fifo, 0, frame, 0, n);
            _fifoCount -= n;
            if (_fifoCount > 0) Array.Copy(_fifo, n, _fifo, 0, _fifoCount);
            return true;
        }

        public void Dispose()
        {
            if (_clip == null) return;
            try { Microphone.End(_device); } catch (Exception e) { Debug.LogWarning("[Voice] closing the microphone: " + e.Message); }
            UnityEngine.Object.Destroy(_clip); _clip = null;
        }
    }
}
