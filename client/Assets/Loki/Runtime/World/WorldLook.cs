using System.Collections.Generic;
using Loki.Formats;
using UnityEngine;

namespace Loki.World
{
    /// <summary>
    /// Client wave 2 world rendering (research/world_rendering.md): map fog, the night tint, the map's point lights and its effect objects,
    /// fed to LokiWorldLight.cginc as globals.
    ///  • Fog: data\fogparametertable.txt near / far × 240 cells (roBrowser MapRenderer.js:503-508), smoothstep on the distance to the eye,
    ///    maps without an entry have none; only in the Follow / First-person views (in the table view the eye is outside the map).
    ///  • Night: EFST_SKE (160) on ourselves (0x0983 / 0x0196) — roBrowser ScreenEffectManager.setNight lowers the diffuse red and green
    ///    to 0.5 (never raises them) at 0.005 per 8 ms and multiplies ground and models by env = 1-(1-diffuse)(1-ambient); here the ratio
    ///    env(night)/env(day) keeps the day look unchanged. The server does not re-send it between two night maps: the state is kept.
    ///  • Point lights (RSW type 2): position as a model's, range / 5 in cells, colour 0..1; the 16 nearest to our character, on models and
    ///    sprites with korangar's attenuation (the ground keeps its GND lightmap, which may already hold them).
    ///  • Effects (RSW type 4): the same EF_* ids as skills (roBrowser Renderer/Map/Effects.js:56-76): started within 25 cells of us as
    ///    persistent effects, removed beyond; delay and parameters are unused there too.
    /// </summary>
    public sealed class WorldLook : MonoBehaviour
    {
        private sealed class MapLight { public Vector3 Local; public float RangeCells; public Color Color; }
        private sealed class MapFx { public string Key; public Vector3 Local; public uint Owner; public bool On; }
        private readonly List<MapLight> _lights = new List<MapLight>();
        private readonly List<MapFx> _fx = new List<MapFx>();
        private Transform _mapRoot; private SkyController _sky; private System.Func<Vector3?> _selfLocal; private System.Func<bool> _fogView;
        private Loki.Effects.EffectManager _effects;
        private bool _hasFog; private float _fogNear, _fogFar; private Color _fogColor;
        private Color _dayDiffuse = Color.white, _ambient = new Color(0.3f, 0.3f, 0.3f), _curDiffuse = Color.white;
        private bool _night; private float _nextFx;
        private readonly Vector4[] _plPos = new Vector4[16], _plCol = new Vector4[16];
        public bool Night => _night; public int Lights => _lights.Count; public int MapEffects => _fx.Count; public int EffectsOn { get { int n = 0; foreach (var f in _fx) if (f.On) n++; return n; } }
        public bool FogAvailable => _hasFog; public (float near, float far) FogRange => (_fogNear, _fogFar); public bool FogShown { get; private set; }
        public int LightsShown { get; private set; }
        public Color Env { get; private set; } = Color.white;
        /// <summary>Smoke diagnostics: the map-local position of effect i and the distance of the nearest one to us.</summary>
        public Vector3 FxLocal(int i) => i >= 0 && i < _fx.Count ? _fx[i].Local : Vector3.zero;
        public float NearestFx { get; private set; } = -1f;
        public int NearestFxIndex { get; private set; } = -1;
        public string FxKey(int i) => i >= 0 && i < _fx.Count ? _fx[i].Key : null;
        /// <summary>Point-light strength (korangar uses 10 in linear light; Loki's shaders work in gamma, so this is a tuned look).</summary>
        public static float LightStrength = 1.0f;

        public void Init(Transform mapRoot, SkyController sky, Loki.Effects.EffectManager effects, System.Func<Vector3?> selfLocal, System.Func<bool> fogView)
        {
            _mapRoot = mapRoot; _sky = sky; _effects = effects; _selfLocal = selfLocal; _fogView = fogView;
            Shader.SetGlobalVector("_LokiEnv", new Vector4(1, 1, 1, 1));
            Shader.SetGlobalVector("_LokiFog", Vector4.zero); Shader.SetGlobalFloat("_LokiPLCount", 0);
        }

        /// <summary>A new map is standing (after EnterMap cleared the effects).</summary>
        public void OnMap(string map, MapLoader.LoadedMap m, bool indoor)
        {
            _lights.Clear(); _fx.Clear(); NearestFxIndex = -1; NearestFx = -1f;
            if (m == null) return;
            var offset = new Vector3(m.Gnd.Width, 0, m.Gnd.Height);
            foreach (var l in m.Rsw.Lights) _lights.Add(new MapLight { Local = RoSpace.ToLoki(l.Position) + offset, RangeCells = l.Range * RoSpace.Scale, Color = l.Color });
            for (int i = 0; i < m.Rsw.Effects.Count; i++)
            {
                var e = m.Rsw.Effects[i];
                _fx.Add(new MapFx { Key = e.Id.ToString(), Local = RoSpace.ToLoki(e.Position) + offset, Owner = 0xFE000000u | (uint)i });
            }
            _hasFog = _sky != null && _sky.FogOf(map, out _fogNear, out _fogFar, out _fogColor) && !indoor;
            _dayDiffuse = m.Rsw.LightDiffuse; _ambient = m.Rsw.LightAmbient;
            _curDiffuse = _night ? NightDiffuse() : _dayDiffuse;
            _nextFx = 0;
            var ids = new SortedDictionary<int, int>(); foreach (var e in m.Rsw.Effects) { ids.TryGetValue(e.Id, out int c); ids[e.Id] = c + 1; }
            Debug.Log($"[World] {map}: {_lights.Count} point lights, {_fx.Count} map effects ({string.Join(", ", System.Linq.Enumerable.Select(ids, kv => kv.Key + "×" + kv.Value))}), fog {(_hasFog ? $"near {_fogNear} far {_fogFar} colour {ColorUtility.ToHtmlStringRGB(_fogColor)}" : "none")}, night {_night}, animated models {m.AnimatedInstances} ({m.Animator?.Count ?? 0} files)");
        }

        public void SetNight(bool night) { if (_night != night) Debug.Log("[World] night " + night); _night = night; }
        private Color NightDiffuse() => new Color(Mathf.Min(_dayDiffuse.r, 0.5f), Mathf.Min(_dayDiffuse.g, 0.5f), _dayDiffuse.b);

        private static Color EnvOf(Color d, Color a) => new Color(1 - (1 - d.r) * (1 - a.r), 1 - (1 - d.g) * (1 - a.g), 1 - (1 - d.b) * (1 - a.b));

        private void Update()
        {
            if (_mapRoot == null) return;
            float scale = _mapRoot.lossyScale.x;
            // ---- night ramp (0.005 per 8 ms = 0.625 per second), as a multiplier on the day's env
            var target = _night ? NightDiffuse() : _dayDiffuse;
            float step = 0.625f * Time.deltaTime;
            _curDiffuse.r = Mathf.MoveTowards(_curDiffuse.r, target.r, step); _curDiffuse.g = Mathf.MoveTowards(_curDiffuse.g, target.g, step); _curDiffuse.b = target.b;
            var envDay = EnvOf(_dayDiffuse, _ambient); var envNow = EnvOf(_curDiffuse, _ambient);
            var env = new Color(envDay.r > 1e-4f ? envNow.r / envDay.r : 1, envDay.g > 1e-4f ? envNow.g / envDay.g : 1, envDay.b > 1e-4f ? envNow.b / envDay.b : 1);
            if (env != Env) { Env = env; Shader.SetGlobalVector("_LokiEnv", new Vector4(env.r, env.g, env.b, 1)); _sky?.SetNight(env); }
            // ---- fog
            bool fog = _hasFog && _fogView != null && _fogView();
            FogShown = fog;
            Shader.SetGlobalVector("_LokiFog", fog ? new Vector4(_fogNear * 240f * scale, _fogFar * 240f * scale, 1, 0) : Vector4.zero);
            Shader.SetGlobalColor("_LokiFogColor", _fogColor);
            _sky?.SetHorizon(fog ? _fogColor : (Color?)null);
            // ---- point lights nearest to us
            var me = _selfLocal?.Invoke();
            int n = 0;
            if (me.HasValue && _lights.Count > 0)
            {
                _lights.Sort((a, b) => (a.Local - me.Value).sqrMagnitude.CompareTo((b.Local - me.Value).sqrMagnitude));
                for (int i = 0; i < _lights.Count && n < 16; i++)
                {
                    var l = _lights[i]; if (l.RangeCells <= 0) continue;
                    var w = _mapRoot.TransformPoint(l.Local);
                    _plPos[n] = new Vector4(w.x, w.y, w.z, l.RangeCells * scale);
                    _plCol[n] = new Vector4(l.Color.r * LightStrength, l.Color.g * LightStrength, l.Color.b * LightStrength, 1);
                    n++;
                }
            }
            LightsShown = n;
            Shader.SetGlobalVectorArray("_LokiPL", _plPos); Shader.SetGlobalVectorArray("_LokiPLColor", _plCol); Shader.SetGlobalFloat("_LokiPLCount", n);
            // ---- map effects within 25 cells (checked twice a second)
            if (_effects != null && me.HasValue && Time.time >= _nextFx)
            {
                _nextFx = Time.time + 0.5f; float best = float.MaxValue;
                for (int i = 0; i < _fx.Count; i++)
                {
                    var f = _fx[i];
                    var d = f.Local - me.Value; d.y = 0; bool near = d.magnitude < 25f; if (d.magnitude < best) { best = d.magnitude; NearestFxIndex = i; }
                    if (near && !f.On) { f.On = true; _effects.Spam(f.Key, f.Owner, f.Local, Time.time, -1f, 0, persistent: true); }
                    else if (!near && f.On) { f.On = false; _effects.Remove(f.Owner); }
                }
                NearestFx = _fx.Count > 0 ? best : -1f;
            }
        }
    }
}
