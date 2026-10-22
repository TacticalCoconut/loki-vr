using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 20 (w20tut), point 43 of the design's notes after wave 17 (design note: "just do a little check mark to show that, yeah, that's exactly
    /// what you had to do. In fact, do that check mark effect every time that the player completes a specific section"). The CHECK MARK: a
    /// green disc with a white tick that pops up where the player's attention is (the thing just done, else along the look), stays a moment
    /// and fades as it rises, with a soft two-note tick sound and a light rumble tick in both hands.
    ///   Size     <see cref="SizeDeg"/>° across in the view wherever it stands (its radius follows its distance from the eyes).
    ///   Time     <see cref="PopSeconds"/> s growing to 120 % and settling to 100 %, <see cref="HoldSeconds"/> s still, <see cref="FadeSeconds"/>
    ///            s fading out while it rises <see cref="RiseDeg"/>° in the view; then it is gone (destroyed).
    ///   Drawn    over everything (Loki/UIOverlay: no depth test), over the laser (sorting order <see cref="Order"/>), on the panels' layer.
    ///   Sound    a soft "tick-tick" made here (two short sine notes, 1320 Hz then 1760 Hz, 45 ms each, at 30 % of the effects volume:
    ///            Audio/LokiAudio.cs PlayClip), no file. Rumble: the Buttons kind (Settings → Rumble), 0.3 for 30 ms in both hands.
    /// </summary>
    public sealed class CheckMarkFx : MonoBehaviour
    {
        public const float SizeDeg = 4.5f, PopSeconds = 0.18f, HoldSeconds = 0.45f, FadeSeconds = 0.45f, RiseDeg = 1.5f, SoundVolume = 0.3f;
        public const int Order = 32;
        public static readonly Color Disc = new Color(0.16f, 0.66f, 0.30f, 0.95f), Rim = new Color(0.05f, 0.30f, 0.12f, 0.95f), Tick = new Color(1f, 1f, 1f, 1f);
        /// <summary>Check marks shown since the start (smoke, log), and where the last one stood.</summary>
        public static int Pops { get; private set; }
        public static Vector3 LastAt { get; private set; }
        public static float LastPopTime { get; private set; } = -1f;
        private static Mesh _mesh; private static Material _mat; private static AudioClip _clip;
        private System.Func<Camera> _head; private Vector3 _at; private float _t, _dist; private MeshRenderer _mr; private MaterialPropertyBlock _mpb;

        /// <summary>A check mark at <paramref name="at"/> (world), facing the eyes; <paramref name="sound"/>: the tick and the rumble too.</summary>
        public static CheckMarkFx Pop(Transform parent, System.Func<Camera> head, Vector3 at, bool sound = true)
        {
            var go = new GameObject("CheckMark");
            go.layer = UiKit.PanelLayer;
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var fx = go.AddComponent<CheckMarkFx>(); fx._head = head; fx._at = at;
            go.AddComponent<MeshFilter>().sharedMesh = MeshOf();
            fx._mr = go.AddComponent<MeshRenderer>();
            if (_mat == null)
            {
                var sh = Shader.Find("Loki/UIOverlay");
                _mat = new Material(sh != null ? sh : Shader.Find("Sprites/Default")) { name = "CheckMark", renderQueue = VR.DioramaRig.LaserQueue + 1 };
            }
            fx._mr.sharedMaterial = _mat; fx._mr.sortingOrder = Order;
            fx._mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; fx._mr.receiveShadows = false;
            fx._mpb = new MaterialPropertyBlock();
            var cam = head != null ? head() : null;
            fx._dist = cam != null ? Mathf.Max(0.2f, Vector3.Distance(cam.transform.position, at)) : 1f;
            Pops++; LastAt = at; LastPopTime = Time.unscaledTime;
            fx.Step(0f);
            if (sound)
            {
                if (_clip == null) _clip = MakeTick();
                Audio.LokiAudio.Instance?.PlayClip(_clip, SoundVolume);
                VR.Haptics.Both(VR.Haptics.Kind.Ui, 0.3f, 0.03f);
            }
            return fx;
        }

        private void Update() { _t += Time.unscaledDeltaTime; Step(_t); }

        private void Step(float t)
        {
            float total = PopSeconds + HoldSeconds + FadeSeconds;
            if (t >= total) { Destroy(gameObject); return; }
            // the size: grows past full (120 %) and settles; the fade: the last FadeSeconds, rising meanwhile
            float k = t < PopSeconds ? EaseBack(t / PopSeconds) : 1f;
            float fade = t <= PopSeconds + HoldSeconds ? 0f : (t - PopSeconds - HoldSeconds) / FadeSeconds;
            float r = _dist * Mathf.Tan(SizeDeg * 0.5f * Mathf.Deg2Rad) * Mathf.Max(0.01f, k);
            transform.localScale = Vector3.one * r;
            var cam = _head != null ? _head() : null;
            var up = cam != null ? cam.transform.up : Vector3.up;
            transform.position = _at + up * (_dist * Mathf.Tan(RiseDeg * Mathf.Deg2Rad) * fade);
            if (cam != null) { var d = transform.position - cam.transform.position; if (d.sqrMagnitude > 1e-8f) transform.rotation = Quaternion.LookRotation(d, cam.transform.up); }
            _mpb.SetColor("_Color", new Color(1f, 1f, 1f, 1f - fade)); _mr.SetPropertyBlock(_mpb);
        }

        /// <summary>0 → 1.2 → 1: a quick pop that overshoots a little.</summary>
        private static float EaseBack(float x) { x = Mathf.Clamp01(x); return x < 0.6f ? Mathf.Lerp(0f, 1.2f, x / 0.6f) : Mathf.Lerp(1.2f, 1f, (x - 0.6f) / 0.4f); }

        /// <summary>The disc (radius 1, a darker rim) and the tick (two thick strokes), in the plane facing the eyes (x right, y up).</summary>
        private static Mesh MeshOf()
        {
            if (_mesh != null) return _mesh;
            const int n = 40; var v = new System.Collections.Generic.List<Vector3>(); var c = new System.Collections.Generic.List<Color>(); var t = new System.Collections.Generic.List<int>();
            void Ring(float r0, float r1, Color col)
            {
                int b = v.Count;
                for (int i = 0; i < n; i++) { float a = i * 2f * Mathf.PI / n; var d = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f); v.Add(d * r0); c.Add(col); v.Add(d * r1); c.Add(col); }
                for (int i = 0; i < n; i++) { int a = b + 2 * i, bb = b + 2 * ((i + 1) % n); t.AddRange(new[] { a, a + 1, bb + 1, a, bb + 1, bb }); }
            }
            Ring(0f, 0.86f, Disc); Ring(0.86f, 1f, Rim);
            void Stroke(Vector2 p0, Vector2 p1, float w)
            {
                var d = (p1 - p0).normalized; var nrm = new Vector2(-d.y, d.x) * (w * 0.5f); var e = d * (w * 0.5f); int b = v.Count;
                v.Add(p0 - e - nrm); v.Add(p0 - e + nrm); v.Add(p1 + e + nrm); v.Add(p1 + e - nrm); for (int i = 0; i < 4; i++) c.Add(Tick);
                t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            Stroke(new Vector2(-0.42f, 0.02f), new Vector2(-0.12f, -0.30f), 0.17f);
            Stroke(new Vector2(-0.12f, -0.30f), new Vector2(0.46f, 0.36f), 0.17f);
            _mesh = new Mesh { name = "CheckMark", vertices = v.ToArray(), colors = c.ToArray(), triangles = t.ToArray() };
            _mesh.RecalculateBounds();
            return _mesh;
        }

        /// <summary>Two short soft sine notes (1320 Hz, then 1760 Hz), 45 ms each with a quick fade in and out.</summary>
        private static AudioClip MakeTick()
        {
            int rate = 44100, one = rate * 45 / 1000, gap = rate * 20 / 1000, len = one * 2 + gap;
            var s = new float[len];
            void Note(int start, float hz)
            {
                for (int i = 0; i < one; i++)
                {
                    float tt = i / (float)rate, env = Mathf.Min(1f, i / (rate * 0.004f)) * Mathf.Exp(-tt * 55f);
                    s[start + i] += Mathf.Sin(2f * Mathf.PI * hz * tt) * env * 0.5f;
                }
            }
            Note(0, 1320f); Note(one + gap, 1760f);
            var clip = AudioClip.Create("check-tick", len, 1, rate, false); clip.SetData(s, 0); return clip;
        }
    }
}
