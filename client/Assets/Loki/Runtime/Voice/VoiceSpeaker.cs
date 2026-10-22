using System;
using System.Threading;
using UnityEngine;

namespace Loki.Voice
{
    /// <summary>
    /// One friend's voice in the scene (client wave 6, w6voice): an AudioSource that plays that speaker's <see cref="VoiceStream"/>.
    ///
    /// How it plays (a change from the brief's "a 16 kHz clip filled by callback", forced by a measurement: design notes
    /// "Playback"): a streamed AudioClip's PCMReaderCallback is asked for its samples in large blocks well ahead of the moment they
    /// are heard, which alone costs more than the whole delay budget. So the source loops a clip of constant 1.0 and this
    /// component's OnAudioFilterRead — which Unity calls on its audio thread for each block the mixer is about to mix
    /// (docs.unity3d.com/2022.3/Documentation/ScriptReference/MonoBehaviour.OnAudioFilterRead.html: "every time a chunk of audio is
    /// sent to the filter ... every ~20ms", the channels interleaved) — multiplies that block by the voice, brought from 16 kHz to
    /// the mixer's rate by <see cref="VoiceResampler"/>. Multiplying (not overwriting) keeps whatever gains Unity already put on
    /// the block for this source's volume, pan and distance, wherever in its chain it applies them, so the voice is spatialised as
    /// any 3-D source: at the speaker's floating headset, or at the RO character. <see cref="UseStreamClip"/> keeps the brief's
    /// way for the measurement.
    /// The audio thread takes no lock and allocates nothing; a voice never waits for the main thread.
    /// </summary>
    public sealed class VoiceSpeaker : MonoBehaviour
    {
        /// <summary>Measurement only (-lokiVoiceClip 1 in development builds): play through a streamed 16 kHz AudioClip instead.</summary>
        public static bool UseStreamClip;
        /// <summary>How much of the voice is placed in space (the rest stays centred): a friend's headset 30 cm to the left must not
        /// be silent in the right ear.</summary>
        public const float AnchoredBlend = 0.8f;
        /// <summary>Full level within this distance; then down to <see cref="FarGain"/> at <see cref="FarMetres"/> and no further:
        /// a friend across the room (first person: a life-size map) stays understandable.</summary>
        public const float NearMetres = 1.5f, FarMetres = 25f, FarGain = 0.5f;

        public VoiceStream Stream { get; private set; }
        public AudioSource Source { get; private set; }
        /// <summary>Blocks the audio thread has asked this speaker for (it is being played).</summary>
        public int Blocks => Volatile.Read(ref _blocks);
        /// <summary>The largest block the audio thread asked for, in output samples per channel (the stream clip: in 16 kHz samples).</summary>
        public int LargestBlock => Volatile.Read(ref _largest);
        public bool Anchored { get; private set; }

        private VoiceResampler _up; private float[] _in, _mono; private int _outRate;
        private int _blocks, _largest;
        private static AudioClip _ones;

        public static VoiceSpeaker Create(Transform parent, VoiceStream stream)
        {
            var go = new GameObject("voice:" + stream.Aid);
            go.transform.SetParent(parent, false);
            go.SetActive(false);
            var v = go.AddComponent<VoiceSpeaker>();
            v.Stream = stream;
            var src = v.Source = go.AddComponent<AudioSource>();
            src.playOnAwake = false; src.loop = true; src.volume = 1f; src.dopplerLevel = 0f; src.spatialBlend = 0f;
            src.priority = 0; // a friend's voice is never the source Unity silences when a fight plays more sounds than it has voices for
            src.rolloffMode = AudioRolloffMode.Custom; src.minDistance = NearMetres; src.maxDistance = FarMetres;
            src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(NearMetres / FarMetres, 1f), new Keyframe(1f, FarGain)));
            v._outRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            if (UseStreamClip)
                src.clip = AudioClip.Create("voice-stream:" + stream.Aid, VoiceStream.SampleRate, 1, VoiceStream.SampleRate, true, v.OnPcmRead);
            else
            {
                v._up = new VoiceResampler(VoiceStream.SampleRate, v._outRate);
                v._in = new float[8192]; v._mono = new float[8192];
                src.clip = Ones(v._outRate);
            }
            go.SetActive(true);
            src.Play();
            return v;
        }

        /// <summary>One second of 1.0 at the mixer's rate, shared by every speaker.</summary>
        private static AudioClip Ones(int rate)
        {
            if (_ones != null && _ones.frequency == rate) return _ones;
            var data = new float[rate];
            for (int i = 0; i < data.Length; i++) data[i] = 1f;
            _ones = AudioClip.Create("voice-ones", rate, 1, rate, false);
            _ones.SetData(data, 0);
            return _ones;
        }

        /// <summary>Where the voice comes from this frame: a place in the room (the headset, the character), or nowhere (plain).</summary>
        public void Place(bool anchored, Vector3 position)
        {
            Anchored = anchored;
            transform.position = position;
            float blend = anchored ? AnchoredBlend : 0f;
            if (Source.spatialBlend != blend) Source.spatialBlend = blend;
            if (UseStreamClip)
            {
                // the measurement: how many of the clip's samples have been played (its play position, added up over its loops)
                int pos = Source.timeSamples, d = pos - _lastPos; if (d < 0) d += VoiceStream.SampleRate;
                Played += d; _lastPos = pos;
            }
            // an audio device change stops every source (the mixer restarts): start again
            if (!Source.isPlaying) Source.Play();
        }

        /// <summary>The stream clip only: 16 kHz samples Unity has asked the callback for so far (against what the source has played,
        /// AudioSource.timeSamples: how far ahead of the sound Unity reads).</summary>
        public long Pulled => Interlocked.Read(ref _pulled);
        private long _pulled;
        /// <summary>The stream clip only: 16 kHz samples the source has played so far (main thread, each frame).</summary>
        public long Played { get; private set; }
        private int _lastPos;

        private void OnPcmRead(float[] data)
        {
            Interlocked.Add(ref _pulled, data.Length);
            if (data.Length > _largest) Volatile.Write(ref _largest, data.Length);
            var s = Stream;
            if (s != null) s.Read(data, 0, data.Length); else Array.Clear(data, 0, data.Length);
            Interlocked.Increment(ref _blocks);
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_up == null || channels <= 0) return; // the stream clip plays by itself
            int frames = data.Length / channels, done = 0;
            if (frames > _largest) Volatile.Write(ref _largest, frames);
            var s = Stream;
            while (done < frames)
            {
                int part = Math.Min(frames - done, 2048);
                int need = _up.InputNeeded(part);
                if (s != null) s.Read(_in, 0, need); else Array.Clear(_in, 0, need);
                _up.Process(_in, 0, need, _mono, 0, part, out _);
                for (int i = 0; i < part; i++)
                {
                    float v = _mono[i]; int o = (done + i) * channels;
                    for (int c = 0; c < channels; c++) data[o + c] *= v;
                }
                done += part;
            }
            Interlocked.Increment(ref _blocks);
        }

        private void OnDestroy()
        {
            if (Source != null && Source.clip != null && Source.clip != _ones) Destroy(Source.clip);
        }
    }
}
