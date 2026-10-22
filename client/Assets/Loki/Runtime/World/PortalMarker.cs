using UnityEngine;

namespace Loki.World
{
    /// <summary>
    /// A clear, animated marker for warp points (rAthena JT_WARPNPC, which every `warp` script line uses) and scripted
    /// touch-warps (JT_HIDDEN_WARP_NPC): two counter-rotating glowing ground rings, a pillar dressed with the client's
    /// data\texture\effect\ring_blue.tga flame strip,
    /// a soft light pillar and rising sparks. Blue = portal, gold = door/touch area. Local units are map cells.
    /// </summary>
    public sealed class PortalMarker : MonoBehaviour
    {
        private Transform _ring1, _ring2, _beam;
        private Material _ringMat, _beamMat;
        private Color _color;
        private static Texture2D _dot, _beamTex;
        private static Shader _glow;

        public static PortalMarker Attach(Transform unit, bool door)
        {
            var go = new GameObject(door ? "door-marker" : "portal-marker");
            go.transform.SetParent(unit, false);
            var m = go.AddComponent<PortalMarker>();
            m.Build(door);
            return m;
        }

        private void Build(bool door)
        {
            if (_glow == null) _glow = Shader.Find("Loki/Glow");
            _color = door ? new Color(1f, 0.78f, 0.3f, 1f) : new Color(0.35f, 0.75f, 1f, 1f);
            // Ground rings are procedural. The client's effect\ring_blue.tga is not a ring but a 64×64 flame strip meant to be
            // wrapped around a cylinder (checked: RGBA, alpha 0–255, wispy vertical flames), so it dresses the light pillar instead.
            var ring = RingTexture();
            Texture2D flames = null;
            var assets = LokiBootstrap.Instance != null ? LokiBootstrap.Instance.Assets : null;
            if (assets != null) { flames = assets.Texture("effect\\ring_blue.tga", false); if (flames == assets.MissingTexture) flames = null; else flames.wrapModeU = TextureWrapMode.Repeat; } // the pillar wraps it 3× around

            _ringMat = new Material(_glow) { mainTexture = ring, color = _color };
            _ring1 = Quad("ring1", 2.6f, _ringMat).transform;
            _ring2 = Quad("ring2", 3.4f, _ringMat).transform;
            _ring1.localPosition = new Vector3(0, 0.05f, 0); _ring2.localPosition = new Vector3(0, 0.08f, 0);

            _beamMat = new Material(_glow) { mainTexture = flames != null ? flames : BeamTexture(), color = new Color(_color.r, _color.g, _color.b, 0.55f) };
            var beam = new GameObject("beam"); beam.transform.SetParent(transform, false);
            beam.AddComponent<MeshFilter>().sharedMesh = Cylinder(1.0f, door ? 2.2f : 3.6f, 24);
            var br = beam.AddComponent<MeshRenderer>(); br.sharedMaterial = _beamMat; br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; br.receiveShadows = false;
            _beam = beam.transform;

            // Rising sparks
            var ps = gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.startLifetime = 1.6f; main.startSpeed = 1.4f; main.startSize = 0.22f;
            main.startColor = new ParticleSystem.MinMaxGradient(_color, Color.white);
            main.simulationSpace = ParticleSystemSimulationSpace.Local; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 60; main.gravityModifier = 0f;
            var em = ps.emission; em.rateOverTime = door ? 10f : 18f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 4f; sh.radius = 1.0f; sh.radiusThickness = 1f; sh.rotation = new Vector3(-90, 0, 0); // cone emits along +Z; tilted to rise along +Y
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var pr = GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = new Material(_glow) { mainTexture = DotTexture() };
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
        }

        private void Update()
        {
            float t = Time.time;
            _ring1.localRotation = Quaternion.Euler(90, t * 60f, 0);
            _ring2.localRotation = Quaternion.Euler(90, -t * 35f, 0);
            float pulse = 0.75f + 0.25f * Mathf.Sin(t * 3f);
            _ringMat.color = new Color(_ringMat.color.r, _ringMat.color.g, _ringMat.color.b, pulse);
            _beamMat.color = new Color(_color.r, _color.g, _color.b, 0.35f + 0.2f * pulse);
        }

        private GameObject Quad(string name, float size, Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var mesh = new Mesh { name = "quad" };
            float h = size * 0.5f;
            mesh.vertices = new[] { new Vector3(-h, -h, 0), new Vector3(h, -h, 0), new Vector3(h, h, 0), new Vector3(-h, h, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go;
        }

        private static Mesh Cylinder(float radius, float height, int seg)
        {
            var m = new Mesh { name = "beam" };
            var v = new Vector3[(seg + 1) * 2]; var uv = new Vector2[v.Length]; var c = new Color[v.Length];
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
                v[i * 2] = p; v[i * 2 + 1] = p + Vector3.up * height;
                uv[i * 2] = new Vector2(i / (float)seg * 3f, 0); uv[i * 2 + 1] = new Vector2(i / (float)seg * 3f, 1);
                c[i * 2] = new Color(1, 1, 1, 1); c[i * 2 + 1] = new Color(1, 1, 1, 0);
            }
            var t = new int[seg * 6];
            for (int i = 0; i < seg; i++) { int k = i * 2; t[i * 6] = k; t[i * 6 + 1] = k + 1; t[i * 6 + 2] = k + 2; t[i * 6 + 3] = k + 1; t[i * 6 + 4] = k + 3; t[i * 6 + 5] = k + 2; }
            m.vertices = v; m.uv = uv; m.colors = c; m.triangles = t; m.RecalculateBounds();
            return m;
        }

        private static Texture2D DotTexture()
        {
            if (_dot != null) return _dot;
            const int n = 32; _dot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "dot" };
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                float a = Mathf.Clamp01(1f - d); a *= a;
                _dot.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            _dot.Apply(); return _dot;
        }

        private static Texture2D BeamTexture()
        {
            if (_beamTex != null) return _beamTex;
            const int w = 64, h = 64; _beamTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, name = "beam" };
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                float stripe = 0.6f + 0.4f * Mathf.Sin(x / (float)w * Mathf.PI * 8f);
                _beamTex.SetPixel(x, y, new Color(1, 1, 1, stripe));
            }
            _beamTex.Apply(); return _beamTex;
        }

        private static Texture2D _ringTex;
        private static Texture2D RingTexture()
        {
            if (_ringTex != null) return _ringTex;
            const int n = 128; var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "ring" };
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                float ringA = Mathf.Clamp01(1f - Mathf.Abs(d - 0.82f) * 9f);          // bright rim
                float glow = d < 1f ? Mathf.Pow(1f - d, 2f) * 0.35f : 0f;           // soft inner glow
                t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(ringA + glow)));
            }
            t.Apply(true); _ringTex = t; return t;
        }
    }
}
