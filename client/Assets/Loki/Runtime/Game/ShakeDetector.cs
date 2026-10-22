using System.Collections.Generic;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 13 (w13resize): a deliberate SHAKE of a controller (design note, 2026-10-07: "they can tap the button again to end the
    /// resizing mode. Or they can shake the right controller"). Fed the hand's position once a frame, in the rig's own space (turning
    /// or moving the table means nothing to it).
    ///   A stroke    frames where the hand moves at <see cref="Speed"/> m/s or faster (and under <see cref="GlitchSpeed"/>: a
    ///               tracking jump is not read). The stroke's direction is the direction of its fastest frame.
    ///   A reversal  a stroke frame pointing more than 120 degrees away from the stroke before it (cosine at or under
    ///               <see cref="Opposed"/>), when that stroke covered at least <see cref="MinStroke"/>. A hand that does not move
    ///               fast for <see cref="StaleSeconds"/> s forgets its direction (a reach, a pause longer than that and a return are
    ///               two strokes with no reversal between them).
    ///   A shake     <see cref="Reversals"/> reversals within <see cref="Window"/> s. Four reversals are three half-swings, so a
    ///               to-and-fro slower than 1.5 / 0.8 = 1.875 swings a second NEVER makes one, however large and fast; at 2 Hz the
    ///               hand needs +-4.8 cm (0.6 m/s at the middle of a swing), at 3 Hz +-3.2 cm, at 4 Hz +-2.4 cm, at 5 Hz +-1.9 cm.
    /// The numbers were chosen against made-up movements played through this same code (LokiGame.W13ResizeSmoke.cs; no headset here:
    /// nothing was measured on a person): resizing at 1.0 / 1.5 Hz with 15 cm swings and 1.2 / 1.5 Hz with 25 / 30 cm swings (at most
    /// 3 reversals within the window: one short of a shake), a board swung 40 cm and back, two quick 30 cm reaches 0.3 s apart (3), a
    /// trembling hand (5 mm at 10 Hz: 0.3 m/s, nothing); shakes of 2 to 5 Hz, 2.5 to 6 cm either side, sideways, up / down and to and
    /// fro all end the mode within 0.5 to 1.0 s. Every resize mode logs the most reversals seen within the window and the fastest
    /// frame (and once a minute outside it, LokiGame.W13Resize.cs), so a headset session measures ordinary play for real.
    /// </summary>
    public sealed class ShakeDetector
    {
        public const float Speed = 0.6f, Window = 0.8f, Opposed = -0.5f, StaleSeconds = 0.5f, GlitchSpeed = 15f, MinStroke = 0.02f;
        public const int Reversals = 4;
        private Vector3 _prev, _dir; private bool _has, _hasDir; private float _t, _lastFast, _peak, _path;
        private readonly Queue<float> _rev = new Queue<float>();
        /// <summary>Reversals inside the window now; the most there were at once since the last reset; the fastest frame since then
        /// (m/s, glitches left out); shakes found since then (smoke, log).</summary>
        public int Count => _rev.Count;
        public int MostAtOnce { get; private set; }
        public float Fastest { get; private set; }
        public int Shakes { get; private set; }

        public void Reset() { _has = false; _hasDir = false; _rev.Clear(); MostAtOnce = 0; Fastest = 0f; Shakes = 0; _peak = 0f; _path = 0f; }

        /// <summary>One frame. True: this frame completed a shake (the count starts again).</summary>
        public bool Update(Vector3 p, float dt)
        {
            _t += dt;
            if (!_has || dt <= 1e-5f || dt > 0.1f) { _prev = p; _has = true; return false; } // (the first frame, or a hitch: no speed to read)
            var v = (p - _prev) / dt; _prev = p;
            float sp = v.magnitude;
            while (_rev.Count > 0 && _t - _rev.Peek() > Window) _rev.Dequeue();
            if (_hasDir && _t - _lastFast > StaleSeconds) { _hasDir = false; _peak = 0f; _path = 0f; }
            if (sp <= GlitchSpeed && sp > Fastest) Fastest = sp;
            if (sp < Speed || sp > GlitchSpeed) return false;
            var d = v / sp; _lastFast = _t;
            if (!_hasDir) { _dir = d; _hasDir = true; _peak = sp; _path = sp * dt; return false; }
            if (Vector3.Dot(d, _dir) > Opposed)
            {
                _path += sp * dt;
                if (sp > _peak) { _peak = sp; _dir = d; }
                return false;
            }
            // back the way it came: a reversal, if the stroke that ends here was long enough
            bool counts = _path >= MinStroke;
            _dir = d; _peak = sp; _path = sp * dt;
            if (!counts) return false;
            _rev.Enqueue(_t);
            if (_rev.Count > MostAtOnce) MostAtOnce = _rev.Count;
            if (_rev.Count < Reversals) return false;
            _rev.Clear(); _hasDir = false; Shakes++;
            return true;
        }
    }
}
