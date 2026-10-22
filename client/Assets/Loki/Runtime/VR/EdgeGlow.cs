using UnityEngine;

namespace Loki.VR
{
    /// <summary>
    /// Client wave 19 (w19world), point 48 (design note: "when I have the follow mode on, the screen gets a slight bright hue to the side of my view
    /// that my character is currently located when my character starts to leave my point of view ... toggled on and off as can its intensity
    /// and sensitivity be controlled by sliders"): in the Follow view, a soft warm glow at the edge of the view on the side where your own
    /// character is, once it starts to leave the view (behind you: at the nearest edge, below when straight behind). Settings → Comfort
    /// "Character edge glow" (On by default), "Glow strength" (how bright at full), "Glow sensitivity" (how early it starts). Like the
    /// comfort vignette (VR/ComfortVignette.cs) it is one head-locked quad <see cref="Dist"/> m in front of the eyes, turned about the line of
    /// sight towards the character. Only in Follow (Loki's reading of "follow mode": flagged).
    ///
    /// Client wave 25 (w25misc), item 10 (design note, 2026-10-12: "The glow is way too aggressive when I'm not looking at my character and it starts
    /// way too soon. Plus, depending on my headset, I can see the place where the glow stops existing. Like I can see a straight line where
    /// it stops to be there."). Measured (w25misc F10, before this change): the quad reached 0.85 of the view's half-width and its picture
    /// was BRIGHTEST at the quad's own edge (a blob centred on the edge, u = 1), nothing beyond: a hard line at 47.7° on the desktop, 45.9°
    /// at 108° across (Index), 42.5° at 100° (Quest), inside what the eyes see; it began at 60 % of the half-width with the default
    /// sensitivity (40 %) and was full (alpha 0.6 x the strength) from the view's edge on. Now:
    ///  - THE PICTURE never ends in a line: the quad reaches <see cref="QuadReach"/> x the view's extent along the glow's side (63-70° from
    ///    the middle, beyond any headset's edge) and the picture fades to nothing well inside it (<see cref="FadeOutAt"/>); the glow peaks at
    ///    the view's edge (<see cref="PeakAt"/>) and rises softly from <see cref="RiseFrom"/>; across, a soft fall (<see cref="Across"/>).
    ///    The picture is drawn in units of the view's own edge on that side, so it fits every field of view and both sides of the eye.
    ///  - SOFTER: the character glow's full alpha <see cref="MaxAlpha"/> 0.35 (was 0.6).
    ///  - LATER: it starts at (1 - sensitivity x <see cref="SensitivityReach"/>) of the way to the view's edge on the character's side (80 %
    ///    with the default 40 %; was 60 %) and is full only <see cref="FullAt"/> x beyond that edge (the character out of view).
    /// The tutorial guide's pointer (<see cref="Own"/>, wave 24 item 14) shares the picture (its line is gone too) and keeps its own start,
    /// end and alpha (<see cref="OwnMaxAlpha"/>).
    /// </summary>
    public sealed class EdgeGlow : MonoBehaviour
    {
        /// <summary>The character's place (world), or null: no glow (not the Follow view, no character, a loading screen).</summary>
        public System.Func<Vector3?> Target;
        /// <summary>Client wave 24 (w24), item 14: a glow of its own job (the tutorial guide's pointer): not under the Follow view's
        /// "Edge glow" setting, its own colour, its own start (the share of the view's half-width from which it shows).</summary>
        public bool Own; public Color Tint = Warm; public float OwnSensitivity = 0.4f;
        private Material _mat; private MeshRenderer _mr; private Camera _head; private float _level;
        public const float Dist = 0.12f;
        /// <summary>Client wave 25 (w25misc), item 10 (FLAGGED, the orchestrator's numbers): the character glow's full alpha (was 0.6), the
        /// guide pointer's (unchanged); how much of the sensitivity's share the start moves in (the default 40 % starts at 80 % of the way);
        /// full at this share of the view's edge (beyond it).</summary>
        public const float MaxAlpha = 0.35f, OwnMaxAlpha = 0.6f, SensitivityReach = 0.5f, FullAt = 1.1f;
        /// <summary>Client wave 25 (w25misc), item 10: the picture, in units of the view's edge along the glow's side (1 = the edge, in the
        /// plane of the quad): the quad's reach; the glow rises from RiseFrom to its peak at PeakAt, holds to HoldTo, is gone by FadeOutAt;
        /// across its side it falls off with Across (a Gaussian's width) and is gone by FadeOutAt too. FLAGGED.</summary>
        public const float QuadReach = 2f, RiseFrom = 0.7f, PeakAt = 0.92f, HoldTo = 1.15f, FadeOutAt = 1.75f, Across = 0.55f;
        public const int TextureSize = 256;
        public static readonly Color Warm = new Color(1f, 0.92f, 0.75f);
        /// <summary>The glow now (0-1, before the strength), the character's angle from the view's middle and its direction in the view plane
        /// (degrees: 0 right, 90 up), the half-width the angles are measured against (smoke, log).</summary>
        public float Level => _level;
        public float Angle { get; private set; }
        public float Direction { get; private set; }
        public float HalfWidth { get; private set; }
        /// <summary>Client wave 25 (w25misc): the view's extent from its middle to its edge along the glow's side (degrees), the quad's
        /// reach from the middle (degrees), the alpha drawn now (smoke, log).</summary>
        public float HalfAlong { get; private set; }
        public float QuadAngle { get; private set; }
        public float AlphaNow => _mr != null && _mr.enabled ? _mat.color.a : 0f;
        public bool Drawn => _mr != null && _mr.enabled;

        public static EdgeGlow Attach(Camera head)
        {
            var go = new GameObject("EdgeGlow");
            go.transform.SetParent(head.transform, false);
            go.transform.localPosition = new Vector3(0, 0, Dist); go.transform.localRotation = Quaternion.identity;
            var g = go.AddComponent<EdgeGlow>(); g._head = head;
            var mesh = new Mesh { name = "edgeGlow" };
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            g._mr = go.AddComponent<MeshRenderer>();
            g._mat = new Material(Shader.Find("Sprites/Default")) { mainTexture = Picture, color = new Color(Warm.r, Warm.g, Warm.b, 0f), renderQueue = 4001 };
            g._mr.sharedMaterial = g._mat; g._mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; g._mr.receiveShadows = false; g._mr.enabled = false;
            return g;
        }

        /// <summary>Client wave 25 (w25misc): the picture's alpha at a point of the quad, in units of the view's edge along the glow's side
        /// (x towards the side, y across it; the quad spans ±<see cref="QuadReach"/>): 0 to the middle's side of RiseFrom, a soft rise to the
        /// peak at the edge, a soft fall to nothing by FadeOutAt both outward and across. The texture is this, sampled once.</summary>
        public static float PictureAlpha(float x, float y)
        {
            float along = x <= RiseFrom ? 0f : x < PeakAt ? Mathf.SmoothStep(0f, 1f, (x - RiseFrom) / (PeakAt - RiseFrom)) : x <= HoldTo ? 1f : x < FadeOutAt ? 1f - Mathf.SmoothStep(0f, 1f, (x - HoldTo) / (FadeOutAt - HoldTo)) : 0f;
            float ay = Mathf.Abs(y), across = ay >= FadeOutAt ? 0f : Mathf.Exp(-(ay / Across) * (ay / Across)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FadeOutAt - 0.35f, FadeOutAt, ay)));
            return Mathf.Clamp01(along * across);
        }

        private static Texture2D _picture;
        /// <summary>The highest alpha (0-255) on the picture's outermost ring of pixels, and anywhere (made once; smoke).</summary>
        public static int BorderAlpha { get; private set; }
        public static int PeakAlpha { get; private set; }
        private static Texture2D Picture
        {
            get
            {
                if (_picture != null) return _picture;
                const int N = TextureSize;
                var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "edgeGlow" };
                var px = new Color32[N * N]; int border = 0, peak = 0;
                for (int j = 0; j < N; j++)
                    for (int i = 0; i < N; i++)
                    {
                        float x = ((i + 0.5f) / N * 2f - 1f) * QuadReach, y = ((j + 0.5f) / N * 2f - 1f) * QuadReach;
                        byte a = (byte)Mathf.RoundToInt(PictureAlpha(x, y) * 255f);
                        px[i + j * N] = new Color32(255, 255, 255, a);
                        if (a > peak) peak = a;
                        if ((i == 0 || j == 0 || i == N - 1 || j == N - 1) && a > border) border = a;
                    }
                t.SetPixels32(px); t.Apply(false, true);
                BorderAlpha = border; PeakAlpha = peak;
                return _picture = t;
            }
        }

        /// <summary>The view's extent from its middle along a direction of the view plane (degrees; 0 = right, 90 = up): where that line meets
        /// the view's rectangle (tangents of its half-width and half-height).</summary>
        public static float ExtentAlong(float direction, float halfWidth, float halfHeight)
        {
            float c = Mathf.Abs(Mathf.Cos(direction * Mathf.Deg2Rad)), s = Mathf.Abs(Mathf.Sin(direction * Mathf.Deg2Rad));
            float th = Mathf.Tan(halfWidth * Mathf.Deg2Rad), tv = Mathf.Tan(halfHeight * Mathf.Deg2Rad);
            float t = Mathf.Min(c > 1e-4f ? th / c : float.MaxValue, s > 1e-4f ? tv / s : float.MaxValue);
            return Mathf.Atan(t) * Mathf.Rad2Deg;
        }

        private void LateUpdate()
        {
            var at = (Own || Comfort.EdgeGlow) && Target != null ? Target() : null;
            float target = 0f;
            if (at.HasValue && _head != null)
            {
                var v = _head.transform.InverseTransformPoint(at.Value);
                float halfH = _head.fieldOfView * 0.5f;
                HalfWidth = Mathf.Atan(Mathf.Tan(halfH * Mathf.Deg2Rad) * Mathf.Max(0.1f, _head.aspect)) * Mathf.Rad2Deg;
                Angle = Vector3.Angle(Vector3.forward, v);
                Direction = new Vector2(v.x, v.y).sqrMagnitude > 1e-8f ? Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg : -90f;
                HalfAlong = ExtentAlong(Direction, HalfWidth, halfH);
                if (Own) target = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(HalfWidth * (1f - OwnSensitivity), HalfWidth, Angle)); // (the guide's pointer: wave 24's rule)
                else target = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(HalfAlong * (1f - Comfort.EdgeGlowSensitivity * SensitivityReach), HalfAlong * FullAt, Angle));
                // the quad: its half-size reaches QuadReach x the view's edge along this side, in the plane of the quad
                float edge = Mathf.Tan(HalfAlong * Mathf.Deg2Rad) * Dist, h = edge * QuadReach;
                QuadAngle = Mathf.Atan(h / Dist) * Mathf.Rad2Deg;
                transform.localScale = new Vector3(2f * h, 2f * h, 1f);
                transform.localRotation = Quaternion.Euler(0f, 0f, Direction);
            }
            _level = Mathf.MoveTowards(_level, target, Time.deltaTime * 4f);
            _mr.enabled = _level > 0.01f;
            if (_mr.enabled) _mat.color = new Color(Tint.r, Tint.g, Tint.b, (Own ? OwnMaxAlpha : MaxAlpha * Comfort.EdgeGlowStrength) * _level);
        }
    }
}
