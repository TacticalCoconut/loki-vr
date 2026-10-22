using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 20 (w20tut), points 42 and 65 of the design's notes after wave 17: the FILLING RING of looking at something (design note, 65: "play the
    /// same circle filling animation when the player looks at the circles, and use the same animation on the pourings so that the player does
    /// that mental connection"). One look for both: the tutorial's look step draws it round the practice Poring (LokiGame.W18Tutorial.cs), the
    /// look calibration round each dot (UI/LookCalibrator.cs).
    ///   Picture  a flat ring facing the eyes (turned to the head every frame): a dark see-through track all round, and over it a gold arc that
    ///            grows clockwise from the top as <see cref="Fill"/> goes from 0 to 1, a bright cap at its leading end. Its inner edge at
    ///            <see cref="Inner"/> of its radius. Drawn over everything (Loki/UIOverlay: no depth test, the laser's queue, under the laser
    ///            and over the panels: sorting order <see cref="Order"/>), on the panels' layer.
    ///   Size     a radius in metres (<see cref="Radius"/>): the Poring's size round the Poring, <see cref="LookCalibrator.RingDeg"/>° round a dot.
    ///   Time     both fill in <see cref="FillSeconds"/> s of looking (Loki's number: nothing in the game waits on a look before this, the
    ///            look locks at once) and drain twice as fast when the look leaves (<see cref="DrainSeconds"/>).
    /// </summary>
    public sealed class LookFillRing : MonoBehaviour
    {
        public const float FillSeconds = 1.0f, DrainSeconds = 0.5f, Inner = 0.78f;
        public const int Segments = 64, Order = 29;
        public static readonly Color Track = new Color(0.06f, 0.07f, 0.11f, 0.62f), Arc = new Color(1f, 0.82f, 0.38f, 0.97f), Cap = new Color(1f, 0.97f, 0.82f, 1f);
        private Mesh _mesh; private MeshRenderer _mr; private MaterialPropertyBlock _mpb; private float _drawn = -1f, _alpha = 1f;
        private Vector3[] _v; private Color[] _c; private int[] _t;
        private System.Func<Camera> _head;
        private static Material _mat;
        /// <summary>0 (empty) to 1 (full).</summary>
        public float Fill { get; set; }
        /// <summary>The ring's outer radius, metres.</summary>
        public float Radius { get => transform.localScale.x; set => transform.localScale = Vector3.one * Mathf.Max(1e-4f, value); }
        public bool Shown => gameObject.activeSelf;
        /// <summary>The whole ring's opacity (0-1), for fading in / out.</summary>
        public float Alpha { get => _alpha; set { _alpha = Mathf.Clamp01(value); Tint(); } }

        public static LookFillRing Create(Transform parent, System.Func<Camera> head, string name)
        {
            var go = new GameObject(name);
            go.layer = UiKit.PanelLayer; // (like the panels and the look ring: out of the desktop spectator's view)
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<LookFillRing>(); r._head = head;
            r._mesh = new Mesh { name = "LookFillRing" }; r._mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = r._mesh;
            r._mr = go.AddComponent<MeshRenderer>();
            if (_mat == null)
            {
                var sh = Shader.Find("Loki/UIOverlay");
                _mat = new Material(sh != null ? sh : Shader.Find("Sprites/Default")) { name = "LookFillRing", renderQueue = VR.DioramaRig.LaserQueue };
            }
            r._mr.sharedMaterial = _mat; r._mr.sortingOrder = Order;
            r._mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r._mr.receiveShadows = false;
            r._mpb = new MaterialPropertyBlock();
            int n = Segments;
            r._v = new Vector3[2 * n + 2 * (n + 1) + 4]; r._c = new Color[r._v.Length]; r._t = new int[6 * n + 6 * n + 6]; // (the track, the arc, the cap)
            r.Rebuild(); r.Tint();
            go.SetActive(false);
            return r;
        }

        public void Show(Vector3 at, float radius) { transform.position = at; Radius = radius; if (!gameObject.activeSelf) gameObject.SetActive(true); Face(); }
        public void Hide() { if (gameObject.activeSelf) gameObject.SetActive(false); }

        private void LateUpdate()
        {
            Face();
            if (Mathf.Abs(_drawn - Fill) > 1e-4f) Rebuild();
        }

        private void Face()
        {
            var cam = _head != null ? _head() : null; if (cam == null) return;
            var d = transform.position - cam.transform.position;
            if (d.sqrMagnitude > 1e-8f) transform.rotation = Quaternion.LookRotation(d, cam.transform.up);
        }

        private void Tint() { if (_mr == null) return; _mpb.SetColor("_Color", new Color(1f, 1f, 1f, _alpha)); _mr.SetPropertyBlock(_mpb); }

        /// <summary>The mesh for the current fill: the track (n quads all round), then the arc (as many quads as the fill covers, the last one
        /// cut at the fill), then a cap at the arc's end. In the ring's own plane, radius 1 (the object's scale is the radius); clockwise as
        /// seen from the eyes (+z points away from them: x right, y up).</summary>
        private void Rebuild()
        {
            float f = Mathf.Clamp01(Fill); _drawn = f; int n = Segments; int vi = 0, ti = 0;
            Vector3 P(float turn, float r) { float a = turn * 2f * Mathf.PI; return new Vector3(Mathf.Sin(a) * r, Mathf.Cos(a) * r, 0f); }
            // the track: a closed annulus
            for (int i = 0; i < n; i++) { float tu = i / (float)n; _v[vi] = P(tu, Inner); _c[vi++] = Track; _v[vi] = P(tu, 1f); _c[vi++] = Track; }
            for (int i = 0; i < n; i++)
            {
                int a = 2 * i, b = 2 * ((i + 1) % n);
                _t[ti++] = a; _t[ti++] = a + 1; _t[ti++] = b + 1; _t[ti++] = a; _t[ti++] = b + 1; _t[ti++] = b;
            }
            // the arc: from the top, clockwise, to the fill (slightly inside the track's edges so the track rims it)
            int arcStart = vi; int steps = Mathf.CeilToInt(f * n);
            for (int i = 0; i <= steps && f > 0f; i++)
            {
                float tu = Mathf.Min(i / (float)n, f);
                _v[vi] = P(tu, Inner + 0.03f); _c[vi++] = Arc; _v[vi] = P(tu, 0.97f); _c[vi++] = Arc;
            }
            for (int i = 0; i < steps && f > 0f; i++)
            {
                int a = arcStart + 2 * i, b = a + 2;
                _t[ti++] = a; _t[ti++] = a + 1; _t[ti++] = b + 1; _t[ti++] = a; _t[ti++] = b + 1; _t[ti++] = b;
            }
            // the cap: a small bright square at the arc's end
            if (f > 0f && f < 1f)
            {
                float tu = f, w = 0.012f; int c0 = vi;
                _v[vi] = P(tu - w, Inner + 0.01f); _c[vi++] = Cap; _v[vi] = P(tu - w, 0.99f); _c[vi++] = Cap;
                _v[vi] = P(tu + w * 0.3f, Inner + 0.01f); _c[vi++] = Cap; _v[vi] = P(tu + w * 0.3f, 0.99f); _c[vi++] = Cap;
                _t[ti++] = c0; _t[ti++] = c0 + 1; _t[ti++] = c0 + 3; _t[ti++] = c0; _t[ti++] = c0 + 3; _t[ti++] = c0 + 2;
            }
            for (int i = vi; i < _v.Length; i++) { _v[i] = Vector3.zero; _c[i] = Color.clear; }
            for (int i = ti; i < _t.Length; i++) _t[i] = 0;
            _mesh.Clear(); _mesh.vertices = _v; _mesh.colors = _c; _mesh.triangles = _t;
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(2.2f, 2.2f, 0.2f));
        }
    }
}
