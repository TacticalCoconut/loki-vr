using System.Collections.Generic;
using UnityEngine;

namespace Loki.Effects
{
    /// <summary>
    /// Client wave 5 (w5looks): the camera quakes of roBrowserLegacy's effects (Camera.setQuake from the FUNC parts listed in Fx/BodyFx.cs),
    /// played only when the player turns on Settings → Comfort → "Camera shake" (VR/Comfort.cs CameraShake, off by default: the design's comfort
    /// rule, design notes wave 4c). roBrowserLegacy (a reimplementation; the official client could not be checked) is the only
    /// source, Renderer/Camera.js:
    ///  • setQuake(start, duration = 650, xAmt = 1.0, yAmt = 0.2, zAmt = 0.24) (:212-222) — every caller passes start and at most a duration;
    ///  • processQuake (:233-264), every rendered frame while start ≤ now &lt; start + duration, for each quake: the camera moves sideways by
    ///    ((random·5 − 2.5)/10 + side) · 0.1 (C_QUAKE_MULT, :39) cells along its right axis (cos / −sin of its yaw), its zoom by
    ///    ((random·5 − 2.5)/10 + zoom) · 0.1 and its latitude by ((random·5 − 2.5)/15 + latitude) · 0.1 degrees, each amount changing sign
    ///    after use; quakes run before the camera follows its target (update :490-514): the position then moves back by
    ///    lerp = min(dt · 0.006, 1) of the way (smooth camera, rB's default: Preferences/Camera.js:19) and the zoom by lerp · 2 of the way;
    ///    the zoom is a distance along the view, half of which reaches the eye (translateZ((altitudeFrom − zoom) / 2), :538).
    /// In VR the eye is the player's head and the world is a table: Loki moves the rig (the head, the hands and their panels move with it)
    /// by the same amounts — cells turned into metres by the map's scale, so a quake looks as big in every view (a few millimetres on the
    /// table, centimetres life size) — along the head's horizontal right axis and its view axis, and turns it about the head for the
    /// latitude. rB's latitude has nothing that pulls it back (a few hundredths of a degree stay after each quake); Loki puts the rig back
    /// exactly when no quake runs and the offsets have relaxed.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class CameraQuake : MonoBehaviour
    {
        private const float QuakeMult = 0.1f; // C_QUAKE_MULT (Camera.js:39)
        private sealed class Quake { public float Start, End; public float Side = 1f, Lat = 0.2f, Zoom = 0.24f; public string Key; }
        private static readonly List<Quake> _quakes = new List<Quake>();
        /// <summary>Quakes asked for by effects (whether played or not), those started while the option was on, and the last effect key (smoke).</summary>
        public static int Requested { get; private set; }
        public static int Played { get; private set; }
        public static string LastKey { get; private set; } = "";
        /// <summary>The largest rig displacement (metres) and turn (degrees) applied since <see cref="ResetStats"/> (smoke).</summary>
        public static float MaxOffsetMetres { get; private set; }
        public static float MaxTurnDegrees { get; private set; }
        /// <summary>The rig's displacement now (metres) and whether it is moved at all.</summary>
        public static float OffsetMetres { get; private set; }
        public static bool Shaking { get; private set; }
        public static void ResetStats() { MaxOffsetMetres = 0f; MaxTurnDegrees = 0f; }

        /// <summary>Camera.setQuake(start, duration) — start in local time (Time.time), duration in ms (0 = rB's default 650).</summary>
        public static void Request(float start, int durationMs, string key)
        {
            Requested++; LastKey = key ?? "";
            if (!VR.Comfort.CameraShake) return;
            Played++;
            _quakes.Add(new Quake { Start = start, End = start + (durationMs > 0 ? durationMs : 650) / 1000f, Key = key });
        }

        private VR.DioramaRig _rig;
        private float _side, _zoom, _lat; // the camera's offsets from where it follows its target: cells, cells, degrees
        private Vector3 _basePos; private Quaternion _baseRot; private bool _moved;

        /// <summary>Adds the quake to the rig (once).</summary>
        public static CameraQuake Attach(VR.DioramaRig rig)
        {
            if (rig == null) return null;
            var q = rig.GetComponent<CameraQuake>();
            if (q == null) { q = rig.gameObject.AddComponent<CameraQuake>(); q._rig = rig; }
            return q;
        }

        private void OnDestroy() { Restore(true); _quakes.Clear(); }

        // the rig is back at rest for the frame's own work (DioramaRig.Update, ViewController's LateUpdate at order 500 places the table under
        // the head in Follow / first person); LateUpdate below moves it again after them, before rendering
        private void Update() => Restore(false);

        private void LateUpdate()
        {
            float now = Time.time, dtMs = Time.deltaTime * 1000f;
            if (!VR.Comfort.CameraShake) _quakes.Clear();
            // processQuake (Camera.js:233-264)
            bool any = false;
            for (int i = _quakes.Count - 1; i >= 0; i--)
            {
                var q = _quakes[i];
                if (now >= q.End) { _quakes.RemoveAt(i); continue; }
                any = true;
                if (q.Start > now) continue;
                _side += ((Random.value * 5f - 2.5f) / 10f + q.Side) * QuakeMult; q.Side = -q.Side;
                _zoom += ((Random.value * 5f - 2.5f) / 10f + q.Zoom) * QuakeMult; q.Zoom = -q.Zoom;
                _lat += ((Random.value * 5f - 2.5f) / 15f + q.Lat) * QuakeMult; q.Lat = -q.Lat;
            }
            // the camera follows its target (Camera.js:503-514): the offsets relax
            float lerp = Mathf.Min(dtMs * 0.006f, 1f);
            _side -= _side * lerp;
            _zoom -= _zoom * lerp * 2f;
            if (!any && Mathf.Abs(_side) < 1e-4f && Mathf.Abs(_zoom) < 1e-4f) { _side = _zoom = _lat = 0f; Restore(true); return; }
            Apply();
        }

        private void Apply()
        {
            if (_rig == null || _rig.Head == null) return;
            var map = LokiBootstrap.Instance != null ? LokiBootstrap.Instance.Map : null;
            float metresPerCell = map != null ? map.transform.lossyScale.x : 1f;
            var t = _rig.transform;
            if (!_moved) { _basePos = t.localPosition; _baseRot = t.localRotation; _moved = true; } // the rest pose (Update put it back)
            var head = _rig.Head.transform;
            var right = head.right; right.y = 0f; if (right.sqrMagnitude < 1e-6f) right = Vector3.right; right.Normalize();
            var fwd = head.forward;
            // latitude: a turn about the head's right axis; the side and the zoom: moves of the eye (the rig carries the head)
            t.RotateAround(head.position, right, _lat);
            var move = right * (_side * metresPerCell) - fwd * (_zoom * 0.5f * metresPerCell);
            t.position += move;
            OffsetMetres = move.magnitude; Shaking = true;
            MaxOffsetMetres = Mathf.Max(MaxOffsetMetres, OffsetMetres);
            MaxTurnDegrees = Mathf.Max(MaxTurnDegrees, Mathf.Abs(_lat));
        }

        /// <summary>The rig back at rest; <paramref name="done"/>: the shake is over (else only for this frame's own work, Update).</summary>
        private void Restore(bool done)
        {
            if (_moved && _rig != null) { _rig.transform.localPosition = _basePos; _rig.transform.localRotation = _baseRot; }
            _moved = false;
            if (done) { OffsetMetres = 0f; Shaking = false; }
        }
    }
}
