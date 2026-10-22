using System;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 20 (w20tut), point 65 of the design's notes after wave 17 (design note: "Before the tutorial plays, add a calibration
    /// sequence for the eyesight choosing system, where the player has to look at a sequence of dots and you'll take the average around the
    /// location that the player was looking at to calibrate where the looking circle is. Also play the same circle filling animation when the
    /// player looks at the circles"; and: "Give it about 0.5s after the player's head stabilizes towards a dot before it starts to calibrate
    /// that specific one"). The LOOK CALIBRATION's run: the dots, the waiting, the filling ring, the sampling; VR/LookCalibration.cs keeps the
    /// result (this PC's). Started by LokiGame.W20Tut.cs (the tutorial's step before the laser; Settings → Comfort → Look select "Recalibrate look").
    ///   The dots    <see cref="Dots"/>: five, one after another, at known directions from where the head faced (level) when the run began:
    ///               straight ahead, <see cref="SideDeg"/>° left, <see cref="SideDeg"/>° right, <see cref="UpDeg"/>° up, <see cref="UpDeg"/>° down,
    ///               <see cref="Distance"/> m away, fixed in the room (a dot never follows the head). Client wave 23 (w23place): ALL are
    ///               shown from the start, each a bright disc in a dark outline, <see cref="DotDeg"/>° wide: the one whose turn it is
    ///               pulses inside the filling ring of the tutorial's Poring (UI/LookFillRing.cs, <see cref="RingDeg"/>° round), the ones
    ///               that wait are still and dim, a finished one shows a check. The look ring (the gaze dot)
    ///               is hidden meanwhile (LokiGame.W20Tut.cs), so the player looks with the eyes, not by lining up Loki's ring.
    ///   Per dot     WAIT until the look (the sliders' ray, VR/DioramaRig.Ui.cs GazeRaySliders: never the old calibration) is within
    ///               <see cref="NearDeg"/>° of the dot AND the head is still (it turned less than <see cref="StillDegPerSec"/>°/s over the
    ///               last <see cref="StillWindow"/> s); then <see cref="SettleSeconds"/> s more of that (the design's 0.5 s); then the ring FILLS in
    ///               <see cref="LookFillRing.FillSeconds"/> s while every frame's sample is taken: the dot's direction in the head's frame minus
    ///               the look's there (sideways and up / down angles). Moving the head (or the look leaving the dot) during the wait or
    ///               the fill starts that dot over: the ring empties, its samples are dropped. "Moving" once the wait began is turning
    ///               faster than <see cref="MoveDegPerSec"/>°/s (twice the stillness that starts it: a head held still sways a little, and a
    ///               ring that emptied at every sway would never fill in a headset). A full ring: the dot's error is the mean of
    ///               its samples; a check mark pops on the dot (UI/CheckMarkFx.cs); the next dot comes <see cref="NextDelay"/> s later.
    ///   The end     the correction is the mean of the dots' errors (each dot weighs the same), kept on this PC (VR/LookCalibration.cs
    ///               Save); the spread (the largest distance of a dot's error from the mean) is logged.
    /// </summary>
    public sealed class LookCalibrator : MonoBehaviour
    {
        public const float Distance = 1.2f, SideDeg = 18f, UpDeg = 12f, NearDeg = 12f, StillDegPerSec = 5f, MoveDegPerSec = 10f, StillWindow = 0.15f, SettleSeconds = 0.5f, NextDelay = 0.35f;
        /// <summary>The ring's radius and the dot's width, as angles in the view. Client wave 23 (w23place), the design's answer 36 ("the calibration
        /// circles are kind of difficult to see against the environment, so they need to be more apparent either by moving or having better
        /// contrast"): the dot was 0.8° wide (its bright core 0.64°, its dark edge 0.08°: under two pixels of a headset); now
        /// <see cref="DotDeg"/>° with a dark outline from <see cref="CoreShare"/> to 1 of its radius (0.27°).</summary>
        public const float RingDeg = 2.4f, DotDeg = 1.8f;
        /// <summary>Client wave 23 (w23place): the bright core's share of the dot's radius (the rest is the dark outline); the ACTIVE dot
        /// pulses once every <see cref="PulseSeconds"/> s between its size and <see cref="PulseScale"/> times it, its brightness between
        /// <see cref="PulseDim"/> and 1; a dot that WAITS its turn is still, at <see cref="WaitBright"/> of the brightness; a dot that is DONE
        /// shows a check (a green disc, a white tick, the same dark outline) until the run's end and <see cref="DoneLinger"/> s after it.</summary>
        public const float CoreShare = 0.70f, PulseSeconds = 1.0f, PulseScale = 1.25f, PulseDim = 0.75f, WaitBright = 0.6f, DoneLinger = 1.0f;
        public static readonly Color32 CoreColour = new Color32(255, 246, 214, 255), RimColour = new Color32(20, 16, 8, 255), DoneColour = new Color32(40, 168, 76, 255);
        /// <summary>The dots: sideways (+ right) and up (+) angles from the level heading the run began with.</summary>
        public static readonly Vector2[] Dots = { new Vector2(0f, 0f), new Vector2(-SideDeg, 0f), new Vector2(SideDeg, 0f), new Vector2(0f, UpDeg), new Vector2(0f, -UpDeg) };
        public enum Stage { Off, Wait, Settle, Fill, Between, Finished }

        private Func<Camera> _head; private Func<Ray> _ray;
        private LookFillRing _ring; private static Material _dotMat, _doneMat;
        // client wave 23 (w23place): all the dots are shown (one quad each): the active one pulses, the waiting ones are dim, a done one is its check
        private Transform[] _dots; private MeshRenderer[] _dotR; private float[] _dotScale, _dotBright; private MaterialPropertyBlock _mpb;
        private bool _dotsOn; private float _hideAt = -1f;
        /// <summary>Client wave 23 (w23place): the dots are shown now; dot <paramref name="i"/>'s quad, its size against its resting size
        /// (1: still), its brightness (0-1), whether it shows its check (smoke, log).</summary>
        public bool DotsShown => _dotsOn;
        public Transform DotOf(int i) => _dots != null && i >= 0 && i < _dots.Length ? _dots[i] : null;
        public float DotScaleOf(int i) => _dotScale != null && i >= 0 && i < _dotScale.Length ? _dotScale[i] : 0f;
        public float DotBrightOf(int i) => _dotBright != null && i >= 0 && i < _dotBright.Length ? _dotBright[i] : 0f;
        public bool DotShowsCheck(int i) => _dotR != null && i >= 0 && i < _dotR.Length && _dotR[i] != null && _doneMat != null && _dotR[i].sharedMaterial == _doneMat;
        public static Material DotMaterial => _dotMat;
        private Vector3 _origin; private Quaternion _heading; private float _timer;
        private Vector2 _sum; private int _samples; private Vector2[] _perDot;
        private readonly Vector3[] _fwd = new Vector3[64]; private readonly float[] _fwdT = new float[64]; private int _fwdN, _fwdHead;

        public Stage State { get; private set; }
        public bool Running => State != Stage.Off && State != Stage.Finished;
        /// <summary>The dot shown (0-based), dots finished, the ring's fill, how long the head has been still at the dot (s), its speed (°/s).</summary>
        public int DotIndex { get; private set; }
        public int DotsDone { get; private set; }
        public float Fill => _ring != null ? _ring.Fill : 0f;
        public float StillFor { get; private set; }
        public float HeadSpeed { get; private set; }
        /// <summary>The look's angle from the dot shown now (degrees; the sliders' ray).</summary>
        public float LookOff { get; private set; }
        /// <summary>Waits started over by a move (log, checks), the sampled frames of the last dot.</summary>
        public int Restarts { get; private set; }
        public int LastSamples { get; private set; }
        /// <summary>Each finished dot's error (sideways, up / down degrees), the result (their mean), the spread; where the last finished dot was.</summary>
        public Vector2[] PerDot => _perDot;
        public Vector2 Result { get; private set; }
        public float Spread { get; private set; }
        public Vector3 LastDotAt { get; private set; }
        public LookFillRing Ring => _ring;
        /// <summary>A dot finished (its index); the run finished (the correction kept).</summary>
        public event Action<int> DotFinished;
        public event Action<Vector2> Finished;

        public static LookCalibrator Create(Transform rig, Func<Camera> head, Func<Ray> slidersRay)
        {
            var go = new GameObject("LookCalibrator"); go.transform.SetParent(rig, false);
            var c = go.AddComponent<LookCalibrator>(); c._head = head; c._ray = slidersRay;
            c._ring = LookFillRing.Create(go.transform, head, "LookCalibrationRing");
            c.MakeDots(go.transform);
            return c;
        }

        /// <summary>The world position of dot <paramref name="i"/> of the run under way.</summary>
        public Vector3 DotWorld(int i) => transform.parent.TransformPoint(_origin + _heading * VR.LookCalibration.FromAngles(Dots[Mathf.Clamp(i, 0, Dots.Length - 1)]) * Distance);

        /// <summary>Starts a run (over one under way): dot 1 at once.</summary>
        public void Begin()
        {
            var cam = _head(); if (cam == null) return;
            var h = cam.transform; var f = h.forward; f.y = 0f; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
            var p = transform.parent;
            _origin = p.InverseTransformPoint(h.position); _heading = Quaternion.Inverse(p.rotation) * Quaternion.LookRotation(f, Vector3.up);
            _perDot = new Vector2[Dots.Length]; DotIndex = 0; DotsDone = 0; Restarts = 0; Result = Vector2.zero; Spread = 0f; _fwdN = 0;
            ShowDot(); Rewait(Stage.Wait);
            Debug.Log($"[Look] calibration started: {Dots.Length} dots {Distance:0.0} m away, from a heading of {Quaternion.LookRotation(f).eulerAngles.y:0}°");
        }

        /// <summary>Stops a run under way (nothing kept).</summary>
        public void Cancel()
        {
            if (Running) Debug.Log($"[Look] calibration stopped at dot {DotIndex + 1} of {Dots.Length}: nothing kept");
            State = Stage.Off; HideDots();
        }

        private void Rewait(Stage s) { State = s; StillFor = 0f; _sum = Vector2.zero; _samples = 0; if (_ring != null) _ring.Fill = 0f; }

        private void Update()
        {
            if (State == Stage.Finished && _dotsOn && _hideAt >= 0f && Time.unscaledTime >= _hideAt) HideDots(); // (client wave 23: a finished run's checks have been seen)
            if (!Running) return;
            var cam = _head(); if (cam == null) return;
            float dt = Time.unscaledDeltaTime, now = Time.unscaledTime;
            var h = cam.transform;
            TrackSpeed(h.forward, now);
            if (State == Stage.Between)
            {
                _timer -= dt; if (_timer > 0f) return;
                DotIndex++; ShowDot(); Rewait(Stage.Wait);
                return;
            }
            var at = DotWorld(DotIndex); var ray = _ray();
            var toDot = at - ray.origin;
            LookOff = Vector3.Angle(ray.direction, toDot);
            // still: under StillDegPerSec to begin the wait; once it began, only a turn faster than MoveDegPerSec is a move (a held head sways)
            bool near = LookOff <= NearDeg, still = HeadSpeed <= (State == Stage.Wait ? StillDegPerSec : MoveDegPerSec);
            if (!near || !still)
            {
                if (State != Stage.Wait) { Restarts++; Rewait(Stage.Wait); }
                return;
            }
            if (State == Stage.Wait) { State = Stage.Settle; StillFor = 0f; }
            StillFor += dt;
            if (State == Stage.Settle) { if (StillFor >= SettleSeconds) State = Stage.Fill; else return; }
            // filling: the sample of this frame (the dot's direction minus the look's, in the head's frame)
            var d = VR.LookCalibration.Angles(h.InverseTransformDirection(toDot)) - VR.LookCalibration.Angles(h.InverseTransformDirection(ray.direction));
            _sum += d; _samples++;
            _ring.Fill = Mathf.Min(1f, _ring.Fill + dt / LookFillRing.FillSeconds);
            if (_ring.Fill < 1f) return;
            // the dot is done
            var mean = _samples > 0 ? _sum / _samples : Vector2.zero;
            _perDot[DotIndex] = mean; DotsDone++; LastDotAt = at; LastSamples = _samples;
            Debug.Log($"[Look] calibration dot {DotIndex + 1} of {Dots.Length} ({Dots[DotIndex].x:+0;-0;0}°, {Dots[DotIndex].y:+0;-0;0}°): the look was {mean.x:+0.00;-0.00;0.00}° sideways and {mean.y:+0.00;-0.00;0.00}° up / down off it ({_samples} samples, {Restarts} restarts so far)");
            _ring.Hide(); // (client wave 23: the dot stays, as its check)
            try { DotFinished?.Invoke(DotIndex); } catch (Exception e) { Debug.LogException(e); }
            if (DotsDone >= Dots.Length) { Finish(); return; }
            State = Stage.Between; _timer = NextDelay;
        }

        private void Finish()
        {
            var sum = Vector2.zero; foreach (var e in _perDot) sum += e;
            Result = sum / _perDot.Length; float spread = 0f; foreach (var e in _perDot) spread = Mathf.Max(spread, Vector2.Distance(e, Result)); Spread = spread;
            State = Stage.Finished; _hideAt = Time.unscaledTime + DoneLinger;
            Debug.Log($"[Look] calibration finished: the correction {Result.x:+0.00;-0.00;0.00}° sideways, {Result.y:+0.00;-0.00;0.00}° up / down (the dots' mean; spread {Spread:0.00}°)");
            VR.LookCalibration.Save(Result.x, Result.y, _perDot.Length);
            try { Finished?.Invoke(Result); } catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>The head's turning speed: the angle its forward turned over the last <see cref="StillWindow"/> s, per second.</summary>
        private void TrackSpeed(Vector3 fwd, float now)
        {
            _fwdHead = (_fwdHead + 1) % _fwd.Length; _fwd[_fwdHead] = fwd; _fwdT[_fwdHead] = now; if (_fwdN < _fwd.Length) _fwdN++;
            int k = _fwdHead; float span = 0f; Vector3 old = fwd;
            for (int i = 1; i < _fwdN; i++)
            {
                int j = (_fwdHead - i + _fwd.Length) % _fwd.Length;
                old = _fwd[j]; span = now - _fwdT[j]; k = j;
                if (span >= StillWindow) break;
            }
            HeadSpeed = span > 1e-4f ? Vector3.Angle(fwd, old) / span : (_fwdN > 1 ? 0f : float.MaxValue);
            if (_fwdN <= 1) HeadSpeed = float.MaxValue; // (one frame: not known yet)
        }

        private void ShowDot()
        {
            var at = DotWorld(DotIndex); var cam = _head(); float dist = cam != null ? Vector3.Distance(cam.transform.position, at) : Distance;
            _ring.Fill = 0f; _ring.Show(at, dist * Mathf.Tan(RingDeg * Mathf.Deg2Rad));
            _dotsOn = true; _hideAt = -1f; PlaceDots();
        }

        /// <summary>Client wave 23 (w23place): every dot and the ring away (a run stopped; a finished run's checks have been seen).</summary>
        public void HideDots()
        {
            _dotsOn = false; _hideAt = -1f; if (_ring != null) _ring.Hide();
            if (_dots != null) foreach (var d in _dots) if (d != null && d.gameObject.activeSelf) d.gameObject.SetActive(false);
        }

        /// <summary>Client wave 23 (w23place): every dot of the run at its place, facing the eyes, <see cref="DotDeg"/>° wide wherever it
        /// stands: the active one pulsing (size and brightness, once every <see cref="PulseSeconds"/> s), the waiting ones still and dim,
        /// a done one as its check.</summary>
        private void PlaceDots()
        {
            if (!_dotsOn || _dots == null) return;
            var cam = _head(); if (cam == null) return; var ct = cam.transform;
            float wave = 0.5f - 0.5f * Mathf.Cos(Time.unscaledTime * 2f * Mathf.PI / PulseSeconds); // 0 → 1 → 0 once every PulseSeconds
            for (int i = 0; i < _dots.Length; i++)
            {
                var d = _dots[i]; if (d == null) continue;
                var at = DotWorld(i); var to = at - ct.position; float dist = to.magnitude;
                bool done = i < DotsDone, active = !done && i == DotIndex && Running;
                float scale = active ? Mathf.Lerp(1f, PulseScale, wave) : 1f, bright = done ? 1f : active ? Mathf.Lerp(PulseDim, 1f, wave) : WaitBright;
                if (!d.gameObject.activeSelf) d.gameObject.SetActive(true);
                d.position = at; d.localScale = Vector3.one * (dist * Mathf.Tan(DotDeg * Mathf.Deg2Rad) * scale);
                if (to.sqrMagnitude > 1e-8f) d.rotation = Quaternion.LookRotation(to, ct.up);
                var mat = done ? _doneMat : _dotMat; if (_dotR[i].sharedMaterial != mat) _dotR[i].sharedMaterial = mat;
                _mpb.SetColor("_Color", new Color(bright, bright, bright, 1f)); _dotR[i].SetPropertyBlock(_mpb);
                _dotScale[i] = scale; _dotBright[i] = bright;
            }
        }

        /// <summary>The dots themselves: discs (quads with a round picture) drawn over everything like the ring (Loki/UIOverlay: no depth
        /// test, the laser's queue, after the panels). Client wave 23 (w23place): a bright core inside a dark outline (seen on a light and
        /// on a dark ground alike); a done dot's picture is a green disc with a white tick in the same outline.</summary>
        private void MakeDots(Transform parent)
        {
            var sh = Shader.Find("Loki/UIOverlay"); if (sh == null) sh = Shader.Find("Sprites/Default");
            if (_dotMat == null) _dotMat = new Material(sh) { name = "LookCalibrationDot", mainTexture = DotTexture(false), renderQueue = VR.DioramaRig.LaserQueue };
            if (_doneMat == null) _doneMat = new Material(sh) { name = "LookCalibrationDone", mainTexture = DotTexture(true), renderQueue = VR.DioramaRig.LaserQueue };
            int n = Dots.Length; _dots = new Transform[n]; _dotR = new MeshRenderer[n]; _dotScale = new float[n]; _dotBright = new float[n]; _mpb = new MaterialPropertyBlock();
            for (int i = 0; i < n; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = "LookCalibrationDot" + (i + 1); go.layer = UiKit.PanelLayer;
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(parent, false);
                var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = _dotMat; mr.sortingOrder = LookFillRing.Order + 1;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                go.SetActive(false);
                _dots[i] = go.transform; _dotR[i] = mr;
            }
        }

        /// <summary>A dot's picture, 64 x 64: the core to <see cref="CoreShare"/> of the radius (<see cref="CoreColour"/>; a done dot:
        /// <see cref="DoneColour"/> with a tick in the core's colour), then the outline (<see cref="RimColour"/>), its outer edge soft.</summary>
        private static Texture2D DotTexture(bool done)
        {
            const int n = 64; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = done ? "LookCalibrationDone" : "LookCalibrationDot", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            Vector2 a = new Vector2(-0.34f, 0.02f), b = new Vector2(-0.10f, -0.24f), c = new Vector2(0.37f, 0.29f); // the tick's two strokes
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f); float r = p.magnitude; // (the disc's radius is 1)
                    Color32 col;
                    if (r > CoreShare) col = new Color32(RimColour.r, RimColour.g, RimColour.b, (byte)(255f * Mathf.Clamp01((1f - r) / 0.06f)));
                    else if (!done) col = CoreColour;
                    else col = Mathf.Min(SegmentDistance(p, a, b), SegmentDistance(p, b, c)) <= 0.075f ? CoreColour : DoneColour;
                    px[y * n + x] = col;
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return tex;
        }
        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }

        private void LateUpdate() { PlaceDots(); }
    }
}
