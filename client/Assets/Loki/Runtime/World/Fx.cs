using UnityEngine;

namespace Loki.World
{
    /// <summary>
    /// Small procedural effects in map space (Loki units = cells), drawn with the additive Loki/Glow shader:
    /// particle bursts for hits/heals/spells and ground rings for skill areas and targeting. Placeholders for the
    /// client's STR effects until those are ported.
    /// </summary>
    public static class Fx
    {
        private static Shader _glow; private static Texture2D _dot, _ring; private static Material _dotMat;
        public static Shader Glow => _glow != null ? _glow : (_glow = Shader.Find("Loki/Glow"));

        /// <summary>RO element colours (e_element: 0 neutral, 1 water, 2 earth, 3 fire, 4 wind, 5 poison, 6 holy, 7 dark, 8 ghost, 9 undead).</summary>
        public static Color ElementColor(int e)
        {
            switch (e)
            {
                case 1: return new Color(0.3f, 0.6f, 1f);
                case 2: return new Color(0.75f, 0.55f, 0.25f);
                case 3: return new Color(1f, 0.45f, 0.1f);
                case 4: return new Color(0.6f, 1f, 0.75f);
                case 5: return new Color(0.65f, 0.3f, 0.9f);
                case 6: return new Color(1f, 0.95f, 0.6f);
                case 7: return new Color(0.45f, 0.15f, 0.55f);
                case 8: return new Color(0.7f, 0.55f, 1f);
                case 9: return new Color(0.35f, 0.6f, 0.3f);
                default: return new Color(1f, 1f, 0.9f);
            }
        }

        /// <summary>A one-shot burst of glowing sparks at a map-local position; destroys itself.</summary>
        public static void Burst(Transform mapRoot, Vector3 local, Color color, float size = 1f, int count = 24)
        {
            if (mapRoot == null) return;
            var go = new GameObject("fx-burst");
            go.transform.SetParent(mapRoot, false);
            go.transform.localPosition = local;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.duration = 0.3f; main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * size, 4f * size); main.startSize = new ParticleSystem.MinMaxCurve(0.25f * size, 0.6f * size);
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, 0.6f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.gravityModifier = 0f; main.maxParticles = count * 2;
            var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.3f * size;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            col.color = g;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = DotMaterial();
            ps.Play();
            Object.Destroy(go, 1.5f);
        }

        /// <summary>A flat glowing ring on the ground (radius in cells). Returns it; caller destroys (or passes a lifetime).</summary>
        public static GameObject Ring(Transform mapRoot, Vector3 local, float radius, Color color, float lifetime = -1f, string name = "fx-ring")
        {
            var go = new GameObject(name);
            go.transform.SetParent(mapRoot, false);
            go.transform.localPosition = local + Vector3.up * 0.06f;
            var mesh = new Mesh { name = "ring" };
            float h = radius;
            mesh.vertices = new[] { new Vector3(-h, 0, -h), new Vector3(h, 0, -h), new Vector3(h, 0, h), new Vector3(-h, 0, h) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(Glow) { mainTexture = RingTexture(), color = color };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            if (lifetime > 0) Object.Destroy(go, lifetime);
            return go;
        }

        public static Material DotMaterial() => _dotMat != null ? _dotMat : (_dotMat = new Material(Glow) { mainTexture = DotTexture(), name = "fx-dot" });

        public static Texture2D DotTexture()
        {
            if (_dot != null) return _dot;
            const int n = 32; _dot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "fx-dot" };
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                float a = Mathf.Clamp01(1f - d); a *= a;
                _dot.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            _dot.Apply(); return _dot;
        }

        public static Texture2D RingTexture()
        {
            if (_ring != null) return _ring;
            const int n = 128; _ring = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "fx-ring" };
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                float rim = Mathf.Clamp01(1f - Mathf.Abs(d - 0.85f) * 10f);
                float fill = d < 0.85f ? 0.12f : 0f;
                _ring.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(rim + fill)));
            }
            _ring.Apply(true); return _ring;
        }
    }

    /// <summary>Spins and pulses a ring (targeting reticles, skill areas).</summary>
    public sealed class FxSpin : MonoBehaviour
    {
        public float Speed = 90f; public float Pulse = 0.15f;
        private Vector3 _base;
        private void Start() { _base = transform.localScale; }
        private void Update()
        {
            transform.Rotate(0, Speed * Time.deltaTime, 0, Space.Self);
            float k = 1f + Pulse * Mathf.Sin(Time.time * 5f);
            transform.localScale = _base * k;
        }
    }
}
