using UnityEngine;

namespace Loki.VR
{
    /// <summary>
    /// Comfort vignette: a head-locked dark ring that narrows the view while the world moves around you (smooth turning, panning the table,
    /// walking in Follow / First person) and flashes briefly on a snap turn. Off, or its strength, in the Comfort settings.
    /// It is one quad 12 cm in front of the eyes with a radial texture (clear inside 0.10 of the texture, dark from 0.20 out); the quad is
    /// scaled so the clear circle spans from 60° (barely there) down to 30° (strongest), while its dark rim still covers the whole view.
    /// </summary>
    public sealed class ComfortVignette : MonoBehaviour
    {
        public System.Func<bool> Moving;
        private Material _mat; private GameObject _quad; private float _level, _flashUntil;
        private const float Dist = 0.12f;

        public static ComfortVignette Attach(Camera head)
        {
            var go = new GameObject("ComfortVignette");
            go.transform.SetParent(head.transform, false);
            var v = go.AddComponent<ComfortVignette>();
            v._quad = go;
            go.transform.localPosition = new Vector3(0, 0, Dist); go.transform.localRotation = Quaternion.identity;
            var mesh = new Mesh { name = "vignette" };
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            v._mat = new Material(Shader.Find("Sprites/Default")) { mainTexture = MakeTexture(), color = new Color(0, 0, 0, 0), renderQueue = 4000 };
            mr.sharedMaterial = v._mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            go.SetActive(true); mr.enabled = false;
            return v;
        }

        private static Texture2D MakeTexture()
        {
            const int N = 256;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "vignette" };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N - 0.5f, dy = (y + 0.5f) / N - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.10f, 0.20f, r));
                    px[x + y * N] = new Color32(0, 0, 0, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply(false, true);
            return t;
        }

        /// <summary>A snap turn: dim for a moment.</summary>
        public void Flash() { if (Comfort.Vignette) _flashUntil = Time.time + 0.18f; }
        public float Level => _level;

        private void LateUpdate()
        {
            bool moving = Moving != null && Moving();
            float target = !Comfort.Vignette ? 0f : Time.time < _flashUntil ? 1f : moving ? Comfort.VignetteStrength : 0f;
            _level = Mathf.MoveTowards(_level, target, Time.deltaTime * (target > _level ? 5f : 2.5f));
            var mr = _quad.GetComponent<MeshRenderer>();
            mr.enabled = _level > 0.01f;
            if (!mr.enabled) return;
            float clear = Mathf.Lerp(60f, 30f, _level) * Mathf.Deg2Rad;          // half-angle of the clear circle
            float scale = Mathf.Tan(clear) * Dist / 0.10f;                        // texture clear radius is 0.10 of the quad
            _quad.transform.localScale = Vector3.one * scale;
            _mat.color = new Color(0, 0, 0, Mathf.Clamp01(_level * 2.5f));
        }
    }
}
