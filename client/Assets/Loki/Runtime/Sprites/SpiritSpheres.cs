using System.Collections.Generic;
using Loki.World;
using UnityEngine;

namespace Loki.Sprites
{
    /// <summary>
    /// Client wave 4 (w4units): spirit spheres (and a Gunslinger's coins) circling a unit, drawn as roBrowserLegacy draws the spheres of
    /// ZC_SPIRITS 0x01d0 (Engine/MapEngine/Skill.js:864-887 onSpiritSphere → EF_CHOOKGI 228 / EF_CHOOKGI2 504 Monk / EF_CHOOKGI3 629
    /// Gunslinger / EF_CHOOKGI_N 833 Royal Guard, all the FUNC renderer Renderer/Effects/SpiritSphere.js + SpiritSphere.vs/.fs; a
    /// reimplementation, the official client could not be checked). What that renderer does, and Loki with it:
    ///  • texture data\texture\effect\thunder_center.bmp, times a colour, blended additively (SRC_ALPHA, ONE) — Loki/Glow;
    ///  • five slots round the unit (sphere i uses slot i % 5, 72° apart), 1 cell from its centre and 2 cells above the ground
    ///    (SpiritSphere.vs: position.y = −z − 2, pos2.x += 1), turning at 1° per 64 ms (rotRad = offset − tick / 64 / 180 · π);
    ///  • each sphere a camera-facing quad (the vertex shader's texture rotation) — spheres 1-5 two quads, half-size 0.25 blue
    ///    (0, 0, 1, 1) and 0.15 white-blue (0.8, 0.8, 1, 1); spheres 6-10 one quad 0.35 (0, 0, 1, 0.6); spheres 11+ one quad 0.55
    ///    (0, 0, 1, 0.2) — so a sixth sphere shares slot 1 as a halo; coins (Gunslinger classes, rB DB.isGunslinger) gold and smaller:
    ///    0.1 (1, 0.9, 0.4, 0.6) + 0.05 (1, 1, 0.7, 1), 0.2 (1, 0.9, 0.4, 0.4), 0.3 (1, 0.9, 0.4, 0.2);
    ///  • fading in: alpha + 0.005 per frame up to 1 (Loki: + 0.3 per second, the same at 60 fps).
    /// Client wave 5 (w5looks): a Warlock's elemental spheres (EFST_SUMMON1..5 363-367, rAthena SC_SPHERE_1..5: DisplayPc + SendVal1, val1 = the
    /// element WLS_FIRE 0x44 … WLS_STONE 0x47, status.yml:3394-3423, skill.hpp:2820-2823, skills/mage/summonfireball.cpp:30-47) are a second
    /// instance (<see cref="OfWarlock"/>): roBrowserLegacy keeps each slot's val1 (Engine/MapEngine/Entity.js:2168-2211) and draws them with
    /// Renderer/Effects/WarlockSphere.js, a copy of SpiritSphere.js without coins — the same blue thunder_center.bmp quads, one per non-zero
    /// slot (WarlockSphere.js:187), whatever the element: its fireorb / lightningorb / waterorb / stoneorb table (:52-56) is never used — but
    /// with the depth test off (runWithDepth(false, …) :186 against SpiritSphere.js:172's true): Loki/Glow with ZTest Always.
    /// rB's updateWarlockSpheres (Entity.js:2417-2432) spams a new 'temporary_warlock_sphere' (EffectTable.js:16193-16221) on every change and
    /// removes them only when every slot is empty, so a released sphere goes on being drawn by the older renderers; Loki draws the current
    /// slots once (what the function is named for), fading in only when the first sphere appears.
    /// </summary>
    public sealed class SpiritSpheres : MonoBehaviour
    {
        public int Count { get; private set; }
        public bool Coins { get; private set; }
        /// <summary>Client wave 5 (w5looks): this instance draws a Warlock's elemental spheres (EFST_SUMMON1..5).</summary>
        public bool Warlock { get; private set; }
        private float _alpha;
        private readonly List<(Transform t, MeshRenderer r, int sphere, float size, Color c)> _quads = new List<(Transform, MeshRenderer, int, float, Color)>();
        private static Mesh _quad;
        private static Texture2D _tex;
        private static readonly Dictionary<Color, Material> _mats = new Dictionary<Color, Material>(), _warlockMats = new Dictionary<Color, Material>();
        public const float Radius = 1f, Height = 2f, DegPerMs = 1f / 64f;
        private static readonly HashSet<int> GunslingerJobs = new HashSet<int> { 24, 4215, 4306, 4228, 4229, 4115, 4216, 4313, 4236, 4237 };
        public static bool IsGunslinger(int job) => GunslingerJobs.Contains(job);

        public static SpiritSpheres Of(Unit u) => Find(u, false) ?? u.gameObject.AddComponent<SpiritSpheres>();
        /// <summary>Client wave 5 (w5looks): the unit's Warlock spheres (a second instance).</summary>
        public static SpiritSpheres OfWarlock(Unit u) { var s = Find(u, true); if (s == null) { s = u.gameObject.AddComponent<SpiritSpheres>(); s.Warlock = true; } return s; }
        public static SpiritSpheres Find(Unit u, bool warlock) { if (u == null) return null; foreach (var s in u.GetComponents<SpiritSpheres>()) if (s.Warlock == warlock) return s; return null; }

        public void Set(int count, bool coins, RoAssets assets)
        {
            count = Mathf.Clamp(count, 0, 32);
            if (count == Count && coins == Coins) return;
            // rB removes the old effect and spawns a new SpiritSphere on every packet (Skill.js:865-886): it fades in from 0; w5looks: the
            // Warlock's spheres fade in when the first appears (see the summary)
            if (!Warlock || Count == 0) _alpha = 0f;
            Count = count; Coins = coins;
            if (_tex == null && assets != null) _tex = assets.Texture("effect\\thunder_center.bmp");
            Rebuild();
        }

        private void Rebuild()
        {
            foreach (var q in _quads) if (q.t != null) Destroy(q.t.gameObject);
            _quads.Clear();
            for (int i = Count; i > 0; i--)
            {
                if (i > 10) Add(i, Coins ? 0.3f : 0.55f, Coins ? new Color(1f, 0.9f, 0.4f, 0.2f) : new Color(0f, 0f, 1f, 0.2f));
                else if (i > 5) Add(i, Coins ? 0.2f : 0.35f, Coins ? new Color(1f, 0.9f, 0.4f, 0.4f) : new Color(0f, 0f, 1f, 0.6f));
                else
                {
                    Add(i, Coins ? 0.1f : 0.25f, Coins ? new Color(1f, 0.9f, 0.4f, 0.6f) : new Color(0f, 0f, 1f, 1f));
                    Add(i, Coins ? 0.05f : 0.15f, Coins ? new Color(1f, 1f, 0.7f, 1f) : new Color(0.8f, 0.8f, 1f, 1f));
                }
            }
            LateUpdate();
        }

        private void Add(int sphere, float size, Color c)
        {
            if (_quad == null)
            {
                _quad = new Mesh { name = "sphere-quad" };
                _quad.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
                _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                _quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 }; _quad.RecalculateBounds();
            }
            var go = new GameObject("sphere");
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * size;
            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.sharedMaterial = Mat(c);
            _quads.Add((go.transform, r, sphere, size, c));
        }

        private Material Mat(Color c)
        {
            var cache = Warlock ? _warlockMats : _mats;
            if (cache.TryGetValue(c, out var m) && m != null) return m;
            m = new Material(Fx.Glow) { name = Warlock ? "warlock-sphere" : "spirit-sphere", mainTexture = _tex };
            m.color = c;
            if (Warlock) m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always); // w5looks: rB WarlockSphere.js:186
            cache[c] = m;
            return m;
        }

        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private void LateUpdate()
        {
            if (Count == 0) return;
            if (_alpha < 1f) _alpha = Mathf.Min(1f, _alpha + 0.3f * Time.deltaTime);
            var cam = Camera.main;
            float turn = Time.time * 1000f * DegPerMs;
            foreach (var q in _quads)
            {
                if (q.t == null) continue;
                float a = (q.sphere % 5) * 72f - turn;
                float rad = a * Mathf.Deg2Rad;
                // in the map's frame (the unit's own transform turns with its billboard: ActorSprite faces the camera)
                var map = transform.parent;
                var local = transform.localPosition + new Vector3(Mathf.Cos(rad) * Radius, Height, Mathf.Sin(rad) * Radius);
                q.t.position = map != null ? map.TransformPoint(local) : local;
                if (cam != null) q.t.rotation = Quaternion.LookRotation(q.t.position - cam.transform.position, cam.transform.up);
                var c = q.c; c.a *= _alpha;
                _block.SetColor("_Color", c);
                q.r.SetPropertyBlock(_block);
            }
        }

        /// <summary>The colours and sizes drawn (smoke): "n×(size,rgba) …".</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var q in _quads) sb.Append($"{q.sphere}:{q.size:0.##}({q.c.r:0.#},{q.c.g:0.#},{q.c.b:0.#},{q.c.a:0.#}) ");
            return sb.ToString().TrimEnd();
        }
    }
}
