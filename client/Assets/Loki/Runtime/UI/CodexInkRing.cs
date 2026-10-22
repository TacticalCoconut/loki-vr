using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Wave 28 (wcodex): the Codex's look circle (design note, 2026-10-15: "Looking at the creature for a bit fills a circle much like the
    /// tutorial's [but make the circle thematic around someone archiving information on a book]"). The tutorial's mechanics (UI/LookFillRing.cs:
    /// a flat ring facing the eyes, drawn over everything on the panels' layer, a fill from 0 to 1 clockwise from the top that the caller
    /// times: LokiGame.WCodex.cs fills it while the look holds the monster and drains it twice as fast when the look leaves), dressed as a
    /// scholar writing the creature down:
    ///   the ring    a strip of parchment (a cream track between two thin brown rules)
    ///   the ink     dark ink laid along the strip as it fills, its width wavering like a pen stroke
    ///   the quill   a feather pen whose nib rides the end of the ink line, tilted outward, trembling a little while it writes
    ///   the book    a small open book on top of the ring (never over the creature) that darkens in as the page fills
    ///   written     at the end the book glows (gold: written in full; silver: a boss seen only) and the ring swells and fades (<see cref="DoneSeconds"/>)
    /// </summary>
    public sealed class CodexInkRing : MonoBehaviour
    {
        public const float Inner = 0.80f, DoneSeconds = 0.7f;
        /// <summary>The DRAWN ink (design note, 2026-10-16: "the initial portions of the ring, like about 40% to 60%, it progresses faster, but then the
        /// closer it gets to the end, it slows down. This is just visual"; 2026-10-17 item 35: linger longer right at the end of the scan):
        /// a two-stage C1 ease-out of the real progress <see cref="Fill"/> that reaches 70 % of the ring at half the scan's time and 86 % at
        /// 70 % of the time, then spends the final 30 % of the scan creeping through the last 14 % of the circle right before completion;
        /// never backwards while the scan goes on. A JUMP of the real progress (a hit on a boss) is
        /// caught up at <see cref="CatchUpPerSecond"/> rings a second at most (a whole ring in 0.3 s: never a snap); a scan's own ease never
        /// needs that much. A draining ring follows its value down; another creature's ring starts from its own value (<see cref="Snap"/>).</summary>
        public const float CatchUpPerSecond = 1f / 0.3f;
        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            if (t <= 0.50f)
            {
                float u = t / 0.50f;
                return 0.75f * u + 0.19f * u * (1f - u);
            }
            if (t <= 0.68f)
            {
                float v = (t - 0.50f) / 0.18f;
                return 0.75f + 0.15f * v + 0.0516f * v * (1f - v);
            }
            float w = (t - 0.68f) / 0.32f;
            return 0.90f + 0.10f * w + 0.075f * w * (1f - w);
        }
        public const int Segments = 72, Order = 29;
        public static readonly Color Parchment = new Color(0.94f, 0.88f, 0.72f, 0.80f), Rule = new Color(0.36f, 0.24f, 0.12f, 0.95f),
                                     Ink = new Color(0.09f, 0.08f, 0.16f, 0.97f), Gold = new Color(1f, 0.82f, 0.30f, 1f), Silver = new Color(0.82f, 0.86f, 0.95f, 1f);
        private Mesh _mesh; private MeshRenderer _mr; private Vector3[] _v; private Color[] _c; private int[] _t; private float _drawn = -1f;
        private Transform _quill, _book; private Material _bookMat;
        private System.Func<Camera> _head;
        private float _doneAt = -1f; private Color _doneTint;
        private static Material _ringMat; private static Texture2D _quillTex, _bookTex;
        /// <summary>The real progress of the scan, 0 (empty) to 1 (full): what the caller times.</summary>
        public float Fill { get; set; }
        /// <summary>What the ink shows now: <see cref="Ease"/> of <see cref="Fill"/>, caught up smoothly after a jump.</summary>
        public float Drawn { get; private set; }
        /// <summary>When the drawn ink last reached the whole ring, and when the scan completed (<see cref="Done"/>): the smoke compares them.</summary>
        public float DrawnFullAt { get; private set; } = -1f;
        public float DoneAt { get; private set; } = -1f;
        /// <summary>The ink starts from this ring's own value (another creature's ring, a ring shown anew): no catch-up from the last one's.</summary>
        public void Snap() { Drawn = Ease(Fill); _fillWas = Fill; }
        public float Radius { get => transform.localScale.x; set => transform.localScale = Vector3.one * Mathf.Max(1e-4f, value); }
        public bool Shown => gameObject.activeSelf;
        /// <summary>The written flourish is playing.</summary>
        public bool Finishing => _doneAt >= 0f;
        /// <summary>The quill's nib now (world; smoke: it rides the end of the ink).</summary>
        public Vector3 NibAt => _quill != null ? _quill.TransformPoint(new Vector3(0f, -0.5f, 0f)) : transform.position;

        public static CodexInkRing Create(Transform parent, System.Func<Camera> head, string name)
        {
            var go = new GameObject(name); go.layer = UiKit.PanelLayer; go.transform.SetParent(parent, false);
            var r = go.AddComponent<CodexInkRing>(); r._head = head;
            r._mesh = new Mesh { name = "CodexInkRing" }; r._mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = r._mesh;
            r._mr = go.AddComponent<MeshRenderer>();
            r._mr.sharedMaterial = RingMaterial(); r._mr.sortingOrder = Order;
            r._mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r._mr.receiveShadows = false;
            int n = Segments;
            r._v = new Vector3[8 * n + 4]; r._c = new Color[r._v.Length]; r._t = new int[24 * n + 6]; // (three bands of 2n points and 6n indices, the ink up to 2(n+1) / 6n)
            r._quill = Quad(go.transform, "quill", QuillTexture(), Order + 1, new Vector2(0.30f, 0.95f), out _);
            r._book = Quad(go.transform, "book", BookTexture(), Order + 1, new Vector2(0.66f, 0.44f), out r._bookMat);
            r._book.localPosition = new Vector3(0f, 1.30f, 0f); // (on top of the ring, never over the creature the ring is round)
            r.Rebuild();
            go.SetActive(false);
            return r;
        }

        public void Show(Vector3 at, float radius)
        {
            transform.position = at; Radius = radius;
            if (!gameObject.activeSelf) { gameObject.SetActive(true); Snap(); }
            Face();
        }
        public void Hide() { _doneAt = -1f; if (gameObject.activeSelf) gameObject.SetActive(false); transform.localScale = Vector3.one * Mathf.Max(1e-4f, Radius); }
        /// <summary>The creature is written: the flourish, then the ring hides itself. <paramref name="full"/>: gold; else silver (seen only).
        /// The ink goes on to the whole ring (at once after a look, whose ease ends there; caught up after a hit's jump).</summary>
        public void Done(bool full) { Fill = 1f; _doneAt = Time.unscaledTime; DoneAt = _doneAt; DoneFull = full; _doneTint = full ? Gold : Silver; StepDrawn(0f); Rebuild(); }
        /// <summary>The last flourish was gold (written in full), not silver (a boss seen only).</summary>
        public bool DoneFull { get; private set; }

        /// <summary>The drawn ink one frame on (<paramref name="dt"/> s) toward <see cref="Ease"/>(<see cref="Fill"/>): up at most
        /// <see cref="CatchUpPerSecond"/> a second, a scan's own step in full (a ring that ends ends on the frame it ends); down at once.</summary>
        private void StepDrawn(float dt)
        {
            float want = Ease(Fill), was = Drawn;
            if (want <= Drawn) Drawn = want;
            else
            {
                float own = want - Ease(Mathf.Max(0f, _fillWas)); // (the step the real progress itself made since the last frame, eased)
                float step = Mathf.Max(dt * CatchUpPerSecond, _fillWas >= 0f && Mathf.Abs(Fill - _fillWas) < 0.08f ? own : 0f);
                Drawn = Mathf.Min(want, Drawn + step);
            }
            _fillWas = Fill;
            if (Drawn >= 1f - 1e-5f && was < 1f - 1e-5f) DrawnFullAt = Time.unscaledTime;
        }
        private float _fillWas = -1f;

        private float _baseRadius;
        private void LateUpdate()
        {
            Face();
            StepDrawn(Time.unscaledDeltaTime);
            if (Mathf.Abs(_drawn - Drawn) > 1e-4f) Rebuild();
            float f = Mathf.Clamp01(Drawn); // (the quill rides the DRAWN ink's end; the book glyph inks in with it)
            // the quill: its nib at the end of the ink on the strip's middle, the feather leaning outward and back along the stroke
            float turn = f, a = turn * 2f * Mathf.PI, mid = (Inner + 1f) * 0.5f;
            var nib = new Vector3(Mathf.Sin(a) * mid, Mathf.Cos(a) * mid, -0.01f);
            float writing = _doneAt < 0f && f > 0f && f < 1f ? Mathf.Sin(Time.unscaledTime * 23f) * 4f : 0f;
            float lean = -a * Mathf.Rad2Deg - 35f + writing; // (outward from the ring, tilted back against the stroke's direction)
            _quill.localRotation = Quaternion.Euler(0f, 0f, lean);
            _quill.localPosition = nib + _quill.localRotation * new Vector3(0f, 0.5f * _quill.localScale.y, 0f);
            _quill.gameObject.SetActive(_doneAt < 0f);
            // the book in the middle: faint while empty, inked in as the page fills; the written glow at the end
            Color bc = Color.Lerp(new Color(1f, 1f, 1f, 0.35f), Color.white, f);
            if (_doneAt >= 0f)
            {
                float k = Mathf.Clamp01((Time.unscaledTime - _doneAt) / DoneSeconds);
                bc = Color.Lerp(_doneTint, new Color(_doneTint.r, _doneTint.g, _doneTint.b, 0f), Mathf.Clamp01((k - 0.5f) * 2f));
                if (_baseRadius <= 0f) _baseRadius = Radius;
                transform.localScale = Vector3.one * _baseRadius * (1f + 0.25f * Mathf.Sin(k * Mathf.PI * 0.5f));
                _mr.GetPropertyBlock(_mpb ?? (_mpb = new MaterialPropertyBlock())); _mpb.SetColor("_Color", new Color(1f, 1f, 1f, 1f - k)); _mr.SetPropertyBlock(_mpb);
                if (k >= 1f) { _baseRadius = 0f; _mpb.SetColor("_Color", Color.white); _mr.SetPropertyBlock(_mpb); Hide(); return; }
            }
            _bookMat.color = bc;
        }
        private MaterialPropertyBlock _mpb;

        private void Face()
        {
            var cam = _head != null ? _head() : null; if (cam == null) return;
            var d = transform.position - cam.transform.position;
            if (d.sqrMagnitude > 1e-8f) transform.rotation = Quaternion.LookRotation(d, cam.transform.up);
        }

        /// <summary>The strip (two rules and the parchment between), then the ink from the top clockwise to the fill.</summary>
        private void Rebuild()
        {
            float f = Mathf.Clamp01(Drawn); _drawn = f; int n = Segments, vi = 0, ti = 0;
            Vector3 P(float turn, float r) { float a = turn * 2f * Mathf.PI; return new Vector3(Mathf.Sin(a) * r, Mathf.Cos(a) * r, 0f); }
            void Band(float r0, float r1, Color col)
            {
                int s0 = vi;
                for (int i = 0; i < n; i++) { float tu = i / (float)n; _v[vi] = P(tu, r0); _c[vi++] = col; _v[vi] = P(tu, r1); _c[vi++] = col; }
                for (int i = 0; i < n; i++) { int a0 = s0 + 2 * i, b0 = s0 + 2 * ((i + 1) % n); _t[ti++] = a0; _t[ti++] = a0 + 1; _t[ti++] = b0 + 1; _t[ti++] = a0; _t[ti++] = b0 + 1; _t[ti++] = b0; }
            }
            Band(Inner, 1f, Parchment);
            Band(Inner - 0.012f, Inner + 0.012f, Rule); // (the rules: drawn after the parchment, over its edges)
            // the ink: a pen stroke whose edges waver a little (two slow ripples)
            int start = vi, steps = Mathf.CeilToInt(f * n);
            for (int i = 0; i <= steps && f > 0f; i++)
            {
                float tu = Mathf.Min(i / (float)n, f);
                float w0 = 0.035f + 0.012f * Mathf.Sin(tu * 37f), w1 = 0.030f + 0.010f * Mathf.Sin(tu * 23f + 1.3f);
                _v[vi] = P(tu, Inner + w0); _c[vi++] = Ink; _v[vi] = P(tu, 1f - w1); _c[vi++] = Ink;
            }
            for (int i = 0; i < steps && f > 0f; i++) { int a0 = start + 2 * i, b0 = a0 + 2; _t[ti++] = a0; _t[ti++] = a0 + 1; _t[ti++] = b0 + 1; _t[ti++] = a0; _t[ti++] = b0 + 1; _t[ti++] = b0; }
            // the outer rule last (over the parchment's outer edge)
            int s1 = vi;
            for (int i = 0; i < n && vi + 1 < _v.Length; i++) { float tu = i / (float)n; _v[vi] = P(tu, 0.988f); _c[vi++] = Rule; _v[vi] = P(tu, 1.012f); _c[vi++] = Rule; }
            for (int i = 0; i < n && ti + 5 < _t.Length && s1 + 2 * n <= vi; i++) { int a0 = s1 + 2 * i, b0 = s1 + 2 * ((i + 1) % n); _t[ti++] = a0; _t[ti++] = a0 + 1; _t[ti++] = b0 + 1; _t[ti++] = a0; _t[ti++] = b0 + 1; _t[ti++] = b0; }
            for (int i = vi; i < _v.Length; i++) { _v[i] = Vector3.zero; _c[i] = Color.clear; }
            for (int i = ti; i < _t.Length; i++) _t[i] = 0;
            _mesh.Clear(); _mesh.vertices = _v; _mesh.colors = _c; _mesh.triangles = _t;
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(3f, 3f, 0.2f));
        }

        // ------------------------------------------------------------------ the pictures (made here: nothing of Gravity's)
        private static Material RingMaterial()
        {
            if (_ringMat != null) return _ringMat;
            var sh = Shader.Find("Loki/UIOverlay");
            return _ringMat = new Material(sh != null ? sh : Shader.Find("Sprites/Default")) { name = "CodexInkRing", renderQueue = VR.DioramaRig.LaserQueue };
        }

        private static Transform Quad(Transform parent, string name, Texture2D tex, int order, Vector2 size, out Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = name; go.layer = UiKit.PanelLayer;
            Object.Destroy(go.GetComponent<Collider>()); // (never in the laser's or the look's way)
            go.transform.SetParent(parent, false); go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sh = Shader.Find("Loki/UIOverlay");
            mat = new Material(sh != null ? sh : Shader.Find("Sprites/Default")) { name = "Codex" + name, mainTexture = tex, renderQueue = VR.DioramaRig.LaserQueue };
            var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.sortingOrder = order;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            return go.transform;
        }

        /// <summary>A feather pen, nib down: a dark nib, a pale shaft, a cream vane with grey barbs. 48 x 160 px.</summary>
        public static Texture2D QuillTexture()
        {
            if (_quillTex != null) return _quillTex;
            const int w = 48, h = 160; var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w - 0.5f, t = (y + 0.5f) / h; // t = 0 at the nib
                    float bend = 0.10f * t * t; // (the feather curves a little)
                    float dx = u - bend;
                    Color c = Color.clear;
                    if (t < 0.13f)
                    {
                        float half = 0.07f * (t / 0.13f);
                        if (Mathf.Abs(dx) <= half + 0.01f) c = Color.Lerp(new Color(0.08f, 0.07f, 0.12f, 1f), new Color(0.35f, 0.30f, 0.25f, 1f), t / 0.13f);
                    }
                    else
                    {
                        float s = (t - 0.13f) / 0.87f;
                        float vane = 0.42f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Min(1f, s * 1.05f)), 0.65f) * (s < 0.12f ? s / 0.12f : 1f);
                        bool shaft = Mathf.Abs(dx) <= 0.03f;
                        if (shaft) c = new Color(0.93f, 0.90f, 0.82f, 1f);
                        else if (Mathf.Abs(dx) <= vane * 0.5f)
                        {
                            float barb = Mathf.Repeat((Mathf.Abs(dx) * 1.6f - s) * 18f, 1f);
                            c = barb < 0.18f ? new Color(0.62f, 0.60f, 0.58f, 1f) : new Color(0.97f, 0.95f, 0.90f, 1f);
                            float edge = Mathf.Clamp01((vane * 0.5f - Mathf.Abs(dx)) * w * 0.8f); c.a = edge;
                        }
                    }
                    px[y * w + x] = c;
                }
            _quillTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "CodexQuill", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _quillTex.SetPixels32(px); _quillTex.Apply(false, true);
            return _quillTex;
        }

        /// <summary>A small open book: two cream pages bowed from a dark spine, brown edges, a few written lines. 96 x 64 px.</summary>
        public static Texture2D BookTexture()
        {
            if (_bookTex != null) return _bookTex;
            const int w = 96, h = 64; var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h; // u -1..1 (spine at 0), v 0 bottom .. 1 top
                    float bow = 0.10f * (1f - Mathf.Abs(u)) * (1f - Mathf.Abs(u)) * 1.6f; // (the pages rise toward the spine)
                    float bottom = 0.10f + bow * 0.5f, top = 0.86f + bow * 0.25f - 0.08f * Mathf.Abs(u);
                    Color c = Color.clear;
                    if (Mathf.Abs(u) <= 0.96f && v >= bottom - 0.05f && v <= bottom) c = new Color(0.30f, 0.18f, 0.08f, 1f); // the cover's edge under the pages
                    if (Mathf.Abs(u) <= 0.94f && v > bottom && v < top)
                    {
                        c = new Color(0.97f, 0.93f, 0.82f, 1f);
                        bool rim = v > top - 0.035f || Mathf.Abs(u) > 0.90f;
                        if (rim) c = new Color(0.45f, 0.30f, 0.15f, 1f);
                        float line = Mathf.Repeat((v - bottom) / (top - bottom) * 7f, 1f);
                        bool written = !rim && Mathf.Abs(u) > 0.14f && Mathf.Abs(u) < 0.82f && line > 0.42f && line < 0.58f && v < top - 0.10f && v > bottom + 0.08f;
                        if (written) c = new Color(0.25f, 0.22f, 0.30f, 1f);
                    }
                    if (Mathf.Abs(u) <= 0.035f && v > bottom - 0.05f && v < top + 0.02f) c = new Color(0.22f, 0.12f, 0.06f, 1f); // the spine
                    px[y * w + x] = c;
                }
            _bookTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "CodexBookGlyph", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _bookTex.SetPixels32(px); _bookTex.Apply(false, true);
            return _bookTex;
        }
    }
}
