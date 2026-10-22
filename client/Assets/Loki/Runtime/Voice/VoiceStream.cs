#nullable disable
using System;
using System.Threading;

namespace Loki.Voice
{
    /// <summary>
    /// One speaker's incoming voice (client wave 6, w6voice): decoded frames in a jitter buffer between the thread that receives
    /// them (<see cref="Push"/>: the relay link's thread) and the thread that plays them (<see cref="Read"/>: Unity's audio thread).
    /// One writer, one reader, no lock and no allocation after the constructor: the writer fills a frame slot and then publishes
    /// it by advancing <c>_write</c>; the reader alone advances <c>_read</c>. No Unity dependency (Loki Host's tests compile it).
    ///
    /// Playing: a talk spurt starts once <see cref="TargetFrames"/> frames wait (80 ms at the start: frames leave the speaker in
    /// bursts of a rendered frame and cross a TCP relay), or at once when the spurt's last frame is already here, with a 5 ms
    /// fade-in. When the buffer runs dry inside a spurt (the frames are late or lost), the last 20 ms are repeated once, fading to
    /// silence (no click, no stuck sound), the target grows by one frame (up to <see cref="MaxTargetFrames"/>), and playing starts
    /// again, faded in, when the buffer has refilled. A spurt that ends with its marked last frame simply ends. After
    /// <see cref="CalmFramesToShrink"/> frames without an underrun the target shrinks by one frame (down to
    /// <see cref="MinTargetFrames"/>). More than <see cref="LateSlackFrames"/> frames above the target (a burst after the speaker's
    /// PC stalled) are skipped: being late is worse than a 20 ms cut.
    /// </summary>
    public sealed class VoiceStream
    {
        public const int FrameSamples = ImaAdpcmCodec.Samples, SampleRate = ImaAdpcmCodec.Rate;
        public const int MinTargetFrames = 3, StartTargetFrames = 4, MaxTargetFrames = 10, LateSlackFrames = 6;
        public const int CalmFramesToShrink = 1500; // 30 s of speech
        public const int FadeInSamples = 80;         // 5 ms
        private const int Capacity = 32, Mask = Capacity - 1; // frames (640 ms)

        public readonly uint Aid;
        private readonly short[] _pcm = new short[Capacity * FrameSamples];
        private readonly byte[] _flags = new byte[Capacity];
        private volatile int _write, _read; // frames published / frames consumed (they wrap as ints do; only their difference is used)

        // ---- writer side (the receiving thread)
        private IVoiceCodec _codec;
        private ushort _lastSeq; private bool _haveSeq;
        private int _pushed, _refused, _bad, _gaps, _unknownCodec;
        /// <summary>Frames decoded into the buffer; frames refused because it was full; malformed frames; sequence gaps (frames the
        /// relay or the link dropped before us); frames of a codec this build does not know.</summary>
        public int Pushed => Volatile.Read(ref _pushed);
        public int Refused => Volatile.Read(ref _refused);
        public int Bad => Volatile.Read(ref _bad);
        public int Gaps => Volatile.Read(ref _gaps);
        public int UnknownCodec => Volatile.Read(ref _unknownCodec);
        /// <summary>Environment.TickCount of the last frame that arrived (0: none yet).</summary>
        public int LastPushTick => Volatile.Read(ref _lastPushTick);
        private int _lastPushTick;
        /// <summary>False: arriving frames are thrown away and nothing plays (muted, "Hear friends' voices" off, another map with
        /// that switch off). Set by the main thread.</summary>
        public volatile bool Accept = true;
        /// <summary>The playback gain (voice volume × this speaker's volume × the other-map factor). Set by the main thread.</summary>
        public volatile float Gain = 1f;

        // ---- reader side (the audio thread)
        private enum State { Idle, Playing, Conceal }
        private State _state = State.Idle;
        private int _pos, _fade, _concealPos, _idleWithFrames, _calm;
        private bool _lastWasLast, _afterUnderrun;
        private readonly short[] _last = new short[FrameSamples];
        private volatile int _target = StartTargetFrames;
        private int _minTarget = MinTargetFrames;
        /// <summary>Port wave H (hpresence): the transport's own lateness, in frames, on top of the start and the least target
        /// (IVoiceChannel.ExtraJitterFrames: 0 for Loki Host's relay). Set once, before the first frame.</summary>
        public void SetExtraTarget(int frames)
        {
            if (frames <= 0) return;
            _minTarget = Math.Min(MaxTargetFrames, MinTargetFrames + frames);
            _target = Math.Min(MaxTargetFrames, StartTargetFrames + frames);
        }
        private int _underruns, _lateSkipped, _played, _spurts;
        private float _level;
        public int TargetFrames => _target;
        public int Underruns => Volatile.Read(ref _underruns);
        public int LateSkipped => Volatile.Read(ref _lateSkipped);
        /// <summary>Frames played to the end; talk spurts started.</summary>
        public int Played => Volatile.Read(ref _played);
        public int Spurts => Volatile.Read(ref _spurts);
        /// <summary>Frames waiting to be played.</summary>
        public int Buffered => _write - _read;
        /// <summary>True while sound of this speaker is being played (or its fade-out).</summary>
        public bool Playing => Volatile.Read(ref _playingFlag) != 0;
        private int _playingFlag;
        /// <summary>The RMS (0..1, before the gain) of the last block the audio thread took: the "is talking" light.</summary>
        public float Level => Volatile.Read(ref _level);

        public VoiceStream(uint aid)
        {
            Aid = aid;
        }


        /// <summary>
        /// A frame from the network (the receiving thread): decoded straight into the buffer. Returns false when it was not taken
        /// (not accepted, unknown codec, malformed, or the buffer is full: the reader is not running or far behind).
        /// </summary>
        public bool Push(ushort seq, byte codec, byte flags, byte[] data, int offset, int length)
        {
            Volatile.Write(ref _lastPushTick, Environment.TickCount == 0 ? 1 : Environment.TickCount);
            if (!Accept) { _haveSeq = false; return false; }
            if (_codec == null || _codec.Id != codec)
            {
                var c = ImaAdpcmCodec.ForId(codec);
                if (c == null) { Interlocked.Increment(ref _unknownCodec); return false; }
                _codec = c;
            }
            if (_haveSeq && seq != (ushort)(_lastSeq + 1)) Interlocked.Increment(ref _gaps);
            _lastSeq = seq; _haveSeq = true;
            int w = _write;
            if (w - _read >= Capacity) { Interlocked.Increment(ref _refused); return false; }
            int slot = w & Mask;
            if (_codec.Decode(data, offset, length, _pcm, slot * FrameSamples) != FrameSamples) { Interlocked.Increment(ref _bad); return false; }
            _flags[slot] = flags;
            _write = w + 1; // published: the reader may take it now
            Interlocked.Increment(ref _pushed);
            return true;
        }

        /// <summary>
        /// The next <paramref name="count"/> samples (16 kHz mono, −1..1, the gain applied) for the audio thread; silence when there
        /// is nothing to play. Never blocks, never allocates.
        /// </summary>
        public void Read(float[] dst, int offset, int count)
        {
            float gain = Gain; double sum = 0;
            if (!Accept)
            {
                // muted or not listened to: drop what waits and stay silent
                _read = _write; _state = State.Idle; _pos = 0; _idleWithFrames = 0; _lastWasLast = false; _afterUnderrun = false;
                Array.Clear(dst, offset, count);
                Volatile.Write(ref _playingFlag, 0); Volatile.Write(ref _level, 0f);
                return;
            }
            for (int i = 0; i < count; i++)
            {
                float s = 0f;
                if (_state == State.Idle)
                {
                    int avail = _write - _read;
                    if (avail > 0)
                    {
                        // start when the target is reached, when the spurt's last frame is already here (a short word), or when
                        // frames have waited for twice the target without more arriving (the last frame was lost)
                        _idleWithFrames++;
                        bool lastHere = (_flags[(_write - 1) & Mask] & 2) != 0;
                        if (avail >= _target || lastHere || _idleWithFrames >= 2 * _target * FrameSamples)
                        {
                            _state = State.Playing; _pos = 0; _fade = 0; _idleWithFrames = 0;
                            if (!_afterUnderrun) Interlocked.Increment(ref _spurts);
                            _afterUnderrun = false;
                        }
                    }
                    else _idleWithFrames = 0;
                }
                if (_state == State.Playing)
                {
                    if (_pos == 0)
                    {
                        int avail = _write - _read;
                        if (avail <= 0)
                        {
                            if (_lastWasLast) { _state = State.Idle; _lastWasLast = false; }
                            else
                            {
                                // an underrun inside a talk spurt: conceal, then wait for the buffer to refill, a frame deeper
                                _state = State.Conceal; _concealPos = 0; _afterUnderrun = true; _calm = 0;
                                Interlocked.Increment(ref _underruns);
                                if (_target < MaxTargetFrames) _target = _target + 1;
                            }
                        }
                        else if (avail > _target + LateSlackFrames)
                        {
                            int skip = avail - _target;
                            _read = _read + skip; Interlocked.Add(ref _lateSkipped, skip);
                            _fade = 0; // soften the cut
                        }
                    }
                    if (_state == State.Playing)
                    {
                        int slot = _read & Mask;
                        short v = _pcm[slot * FrameSamples + _pos];
                        _last[_pos] = v;
                        s = v * (1f / 32768f);
                        if (_fade < FadeInSamples) { s *= _fade / (float)FadeInSamples; _fade++; }
                        if (++_pos == FrameSamples)
                        {
                            _pos = 0;
                            _lastWasLast = (_flags[slot] & 2) != 0;
                            _read = _read + 1;
                            Interlocked.Increment(ref _played);
                            if (++_calm >= CalmFramesToShrink) { _calm = 0; if (_target > _minTarget) _target = _target - 1; }
                        }
                    }
                }
                if (_state == State.Conceal)
                {
                    // the last 20 ms once more, fading to silence
                    s = _last[_concealPos] * (1f / 32768f) * (1f - _concealPos / (float)FrameSamples);
                    if (++_concealPos == FrameSamples) { _state = State.Idle; _idleWithFrames = 0; }
                }
                sum += (double)s * s;
                dst[offset + i] = s * gain;
            }
            Volatile.Write(ref _playingFlag, _state == State.Idle ? 0 : 1);
            Volatile.Write(ref _level, count > 0 ? (float)Math.Sqrt(sum / count) : 0f);
        }
    }
}
