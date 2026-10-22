using System;
using System.Collections.Generic;
using Loki.Formats;
using Loki.Sprites;
using UnityEngine;

namespace Loki.World
{
    /// <summary>
    /// Client wave 4 (w4world): the weather rAthena's map flags send, and the sky's clouds of the maps that have them.
    ///
    /// The server side (rAthena e985006): the flags snow / clouds / clouds2 / fog / fireworks / sakura / leaves become special effects
    /// sent to the player alone with 0x01f3 ZC_NOTIFY_EFFECT2 (clif_weather_check clif.cpp:1588-1618: EF_SNOW 162, EF_CLOUD3 233,
    /// EF_CLOUD5 516, EF_CLOUD4 515, EF_POKJUK 297 + EF_THROWITEM2 299 + EF_POKJUK_SOUND 301, EF_SAKURA 163, EF_MAPLE 333), when the map
    /// is entered (clif_parse_LoadEndAck clif.cpp:11148), on /refresh (clif.cpp:9887) and to everyone on the map when @snow, @sakura,
    /// @clouds, @clouds2, @fog, @fireworks or @leaves toggle a flag (clif_weather clif.cpp:1623-1635, atcommand.cpp:7225-7320; @rain is
    /// commented out). Nothing is sent when a flag is switched off: what is drawn stays until the next map load, a teleport or /refresh
    /// included ("Weather effects will dispell on warp/refresh", map_msg.conf:291 via @clearweather atcommand.cpp:7380-7396), and the
    /// server sends the flags still set again after the load (SkyController.ApplyMapSky clears; roBrowser drops them only when the map
    /// name changes, SnowWeather.js:147-150 …). A script may send any of them (specialeffect).
    /// EF_RAIN 161 and the clouds 229 / 230 / 592 / 697 / 698 come only from scripts.
    ///
    /// What is drawn is roBrowserLegacy's (a reimplementation: no official client source exists), each effect's numbers quoted where
    /// they are used; where roBrowser cites the official client (CEffectPrim::PrimSakura, PrimPokJuk, the 25 ms "rag tick") that is said.
    /// Distances are cells (roBrowser: 1 unit = 1 cell, heights ÷ 5), sprites are roBrowser's px / 35 cells (SpriteRenderer.js:84-87).
    /// Two VR changes, both listed in design notes: particles are depth-tested (roBrowser draws snow, rain, leaves and
    /// fireworks over everything for its fixed top-down camera; in a diorama they would show through the ground), and everything is
    /// placed around our character in the map's own frame, so it scales and turns with the table.
    /// </summary>
    public sealed class Weather : MonoBehaviour
    {
        public const float RagTickMs = 25f; // "The official client uses 25ms rag ticks for weather effects." (roBrowser SnowWeather.js:17)
        public const int EF_RAIN = 161, EF_SNOW = 162, EF_SAKURA = 163, EF_CLOUD = 229, EF_CLOUD2 = 230, EF_CLOUD3 = 233, EF_POKJUK = 297,
            EF_THROWITEM2 = 299, EF_POKJUK_SOUND = 301, EF_MAPLE = 333, EF_CLOUD4 = 515, EF_CLOUD5 = 516, EF_CLOUD6 = 592, EF_CLOUD7 = 697, EF_CLOUD8 = 698;

        /// <summary>The effect ids this system takes from 0x01f3 / 0x0284 (the rest go to the effect player).</summary>
        public static bool IsWeather(int ef) => ef == EF_RAIN || ef == EF_SNOW || ef == EF_SAKURA || ef == EF_MAPLE || ef == EF_POKJUK || ef == EF_THROWITEM2 || ef == EF_POKJUK_SOUND || CloudProfile.Of(ef) != null;

        private RoAssets _assets; private Func<Vector3?> _self; private Func<GatFile> _gat;
        private readonly Billboards _bb = new Billboards();
        private SpriteFall _snow, _leaves; private Clouds _sky; private readonly Dictionary<int, Clouds> _clouds = new Dictionary<int, Clouds>();
        private Fireworks _fireworks; private Rain _rain;
        /// <summary>Smoke / log: what runs now (names), and how many particles were drawn last frame.</summary>
        public int Drawn => _bb.Quads;
        public string Active
        {
            get
            {
                var l = new List<string>();
                if (_snow != null) l.Add("snow"); if (_leaves != null) l.Add(_leaves.Maple ? "maple" : "sakura"); if (_rain != null) l.Add("rain");
                if (_fireworks != null) l.Add("fireworks"); foreach (var k in _clouds.Keys) l.Add("clouds " + k); if (_sky != null) l.Add("sky clouds");
                return string.Join(", ", l);
            }
        }
        public int Count(string what) => what == "snow" ? _snow?.Count ?? 0 : what == "leaves" ? _leaves?.Count ?? 0 : what == "rain" ? _rain?.Count ?? 0 :
            what == "splashes" ? _rain?.SplashCount ?? 0 : what == "puddles" ? _rain?.PuddleCount ?? 0 :
            what == "fireworks" ? _fireworks?.Count ?? 0 : what == "sky" ? _sky?.Count ?? 0 : what.StartsWith("clouds ") && int.TryParse(what.Substring(7), out int id) && _clouds.TryGetValue(id, out var c) ? c.Count : 0;
        /// <summary>Client wave 5 (w5weather): the running rain's state (null when none), and its sound player.</summary>
        public RainState RainNow => _rain?.State;
        /// <summary>The lightning overlay's alpha <paramref name="ms"/> after a flash began, single (1) or double (2) (RainWeather.js:805-854).</summary>
        public static float LightningAlpha(float ms, int multi, out bool on) => Rain.FlashAlpha(ms, multi, out on);
        public RainAudio RainSound => _rainAudio;
        private RainAudio _rainAudio;
        private readonly PuddleMesh _puddles = new PuddleMesh();

        /// <param name="self">our character's map-local position (cells, y up), null when not on a map</param>
        public void Init(RoAssets assets, Func<Vector3?> self, Func<GatFile> gat) { _assets = assets; _self = self; _gat = gat; _bb.Root = transform; _puddles.Root = transform; }

        /// <summary>A weather effect id from the server (0x01f3, or 0x0284 with num &gt; 0). EF_THROWITEM2 / EF_POKJUK_SOUND come with
        /// EF_POKJUK (clif.cpp:1605-1607): roBrowser has no entry for them (EffectTable.js:6363 and 6372 are commented out), neither has Loki.</summary>
        public void Play(int ef) // not "Start": Unity would take it for its message ("Start() can not take parameters")
        {
            float now = Now;
            switch (ef)
            {
                case EF_SNOW: if (_snow == null || _snow.Done) _snow = new SpriteFall(this, SpriteFall.Kind.Snow, now); else _snow.Revive(now); break;
                case EF_SAKURA: case EF_MAPLE:
                {
                    bool maple = ef == EF_MAPLE;
                    // SakuraWeatherEffect.js:170-179: the other kind replaces it, the same kind is revived
                    if (_leaves == null || _leaves.Done || _leaves.Maple != maple) _leaves = new SpriteFall(this, maple ? SpriteFall.Kind.Maple : SpriteFall.Kind.Sakura, now); else _leaves.Revive(now);
                    break;
                }
                case EF_RAIN:
                    if (_rain == null || _rain.Done)
                    {
                        if (_rainAudio == null) _rainAudio = RainAudio.Create(transform);
                        _rain = new Rain(this, now); _rainAudio.StartRain(); // RainWeather.js:381-394: the noise starts with the instance
                    }
                    else _rain.Revive(now);
                    break;
                case EF_POKJUK: if (_fireworks == null) _fireworks = new Fireworks(this, now); break; // PokJukWeatherEffect.js:66-71: a re-send changes nothing
                case EF_THROWITEM2: case EF_POKJUK_SOUND: break;
                default:
                {
                    var p = CloudProfile.Of(ef); if (p == null) return;
                    // CloudWeatherEffect.js:78-83 revives the running instance whatever the id (and keeps its profile): an roBrowser shortcut;
                    // here every cloud id is its own layer, so EF_CLOUD3 and EF_CLOUD5 on one map (@clouds + @clouds2) both show
                    if (!_clouds.TryGetValue(ef, out var c) || c.Done) _clouds[ef] = new Clouds(this, p, now); else c.Revive(now);
                    break;
                }
            }
            Debug.Log($"[Weather] start {Name(ef)} ({ef}); running: {Active}");
        }

        /// <summary>0x0284 with num ≤ 0 (roBrowser Engine/MapEngine/Skill.js:93-121: snow, rain, fireworks, sakura / maple and only
        /// EF_CLOUD / EF_CLOUD2 stop that way; rAthena sends it for none of them by default).</summary>
        public void Stop(int ef)
        {
            float now = Now;
            switch (ef)
            {
                case EF_SNOW: _snow?.Stop(now); break;
                case EF_SAKURA: case EF_MAPLE: _leaves?.Stop(now); break;
                case EF_RAIN: _rain?.Stop(now); break;
                case EF_POKJUK: _fireworks = null; break; // PokJukWeatherEffect.js:302-305: gone at once
                case EF_CLOUD: case EF_CLOUD2: if (_clouds.TryGetValue(ef, out var c)) c.Stop(now); break;
            }
            Debug.Log($"[Weather] stop {Name(ef)} ({ef})");
        }

        /// <summary>A new map: every weather effect goes at once (roBrowser drops each instance when MapRenderer.currentMap changes);
        /// the server sends the new map's weather after the map is loaded.</summary>
        public void Clear()
        {
            _snow = null; _leaves = null; _rain = null; _fireworks = null; _clouds.Clear(); _sky = null;
            _bb.Clear(); _puddles.Clear(); _rainAudio?.StopRain();
        }

        /// <summary>The map's own sky clouds (World/SkyController.cs from mapsky.tsv): colour and count; null = none.</summary>
        public void SetSkyClouds(Color? color, int count)
        {
            _sky = color.HasValue && count > 0 ? new Clouds(this, CloudProfile.Sky(color.Value, count), Now) : null;
        }

        public static string Name(int ef)
        {
            switch (ef)
            {
                case EF_RAIN: return "rain"; case EF_SNOW: return "snow"; case EF_SAKURA: return "sakura"; case EF_MAPLE: return "maple leaves";
                case EF_POKJUK: return "fireworks"; case EF_THROWITEM2: return "fireworks (throw item 2)"; case EF_POKJUK_SOUND: return "fireworks sound";
                default: return CloudProfile.Of(ef) != null ? "clouds" : "effect";
            }
        }

        private static float Now => Time.time * 1000f;
        private Vector3? SelfPos => _self?.Invoke();
        /// <summary>
        /// The ground's height (cells, y up) under a map-local point: roBrowser's Altitude.getCellHeight (Renderer/Map/Altitude.js:125-152),
        /// which every weather effect of roBrowser uses (SnowWeather.js:226, SakuraWeatherEffect.js:236, RainWeather.js:652): the cell under
        /// the point (roBrowser adds the half cell Loki's coordinates already hold), its four GAT corner heights interpolated across it
        /// (south edge, north edge, then between them), 0 outside the map. Client wave 5 (w5weather); before, the cell's average height
        /// (clamped to the map), which left the rain's puddles floating over or sunk into a sloped cell.
        /// </summary>
        private float GroundY(float x, float z)
        {
            var gat = _gat?.Invoke();
            if (gat == null) return 0f;
            int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
            if (ix < 0 || iz < 0 || ix >= gat.Width || iz >= gat.Height) return 0f;
            var c = gat.Get(ix, iz);
            float fx = x - ix, fz = z - iz;
            float x1 = c.H0 + (c.H1 - c.H0) * fx, x2 = c.H2 + (c.H3 - c.H2) * fx;
            return -(x1 + (x2 - x1) * fz) * RoSpace.Scale;
        }

        private void LateUpdate()
        {
            _bb.Begin(); _puddles.Begin();
            float now = Now; var me = SelfPos;
            if (me.HasValue)
            {
                if (_snow != null) { _snow.Tick(now, me.Value); if (_snow.Done) _snow = null; }
                if (_leaves != null) { _leaves.Tick(now, me.Value); if (_leaves.Done) _leaves = null; }
                if (_rain != null) { _rain.Tick(now, me.Value); if (_rain.Done) { Debug.Log("[Weather] rain over: " + _rain.State); _rain = null; _rainAudio?.StopRain(); } }
                if (_fireworks != null) _fireworks.Tick(now, me.Value);
                if (_sky != null) _sky.Tick(now, me.Value);
                List<int> gone = null;
                foreach (var kv in _clouds) { kv.Value.Tick(now, me.Value); if (kv.Value.Done) (gone ??= new List<int>()).Add(kv.Key); }
                if (gone != null) foreach (var k in gone) _clouds.Remove(k);
            }
            _bb.End(); _puddles.End();
        }

        // ================================================================== snow, sakura, maple: sprites that fall
        /// <summary>
        /// Snow (roBrowser SnowWeather.js) and sakura / maple (SakuraWeatherEffect.js, "Adapted from C++ CRagEffect::Sakura/Maple and
        /// CEffectPrim::PrimSakura"). Every number is roBrowser's, per 25 ms tick; the leaves' fall speed and sway are the C++ values it
        /// quotes (SakuraWeatherEffect.js:241-253) turned into cells with its snow's rule "÷ 5" (SnowWeather.js:32-41), not its own
        /// "× 0.5 to taste" (:247), which lets every leaf fall 20-50 cells through the ground.
        /// </summary>
        private sealed class SpriteFall
        {
            public enum Kind { Snow, Sakura, Maple }
            private sealed class P { public float Spawn, X, Y, Z, BaseX, BaseZ, Size, Speed, FacX, FacZ; public float AngX, AngZ, TgtX, TgtZ; public float LastTick; }
            private readonly Weather _w; private readonly Kind _k; private readonly List<P> _ps = new List<P>();
            private float _end = -1f, _lastEmit; private bool _stopping; private Frames _frames;
            public bool Maple => _k == Kind.Maple; public bool Done { get; private set; } public int Count => _ps.Count;
            private float Life => _k == Kind.Snow ? 320 * RagTickMs : 600 * RagTickMs;         // SnowWeather.js:27; Sakura…js:33
            private float FadeIn => _k == Kind.Snow ? 10 * RagTickMs : 20 * RagTickMs;        // :28; :34
            private float FadeOutStart => Life * 4f / 5f;                                        // :29; :35

            public SpriteFall(Weather w, Kind k, float now)
            {
                _w = w; _k = k; _lastEmit = now;
                // SnowWeather.js:45 'data/sprite/이펙트/ef_snow'; SakuraWeatherEffect.js:47-48 'sakura01' / '단풍'
                string name = k == Kind.Snow ? "ef_snow" : k == Kind.Sakura ? "sakura01" : "´ÜÇ³";
                _frames = Frames.Load(w._assets, "data\\sprite\\" + RoNames.Effect + "\\" + name);
            }
            public void Revive(float now) { if (_end > 0) { _end = -1f; _lastEmit = now; } _stopping = false; } // SnowWeather.js:151-159
            public void Stop(float now) { if (_end < 0) { _end = now + 1000 * RagTickMs; _stopping = true; } }   // :194-205 (1000-tick tail)

            private void SpawnOne(float at, Vector3 me)
            {
                float radius = _k == Kind.Snow ? 60f : 70f;                                      // SnowWeather.js:35; Sakura…js:38
                float th = UnityEngine.Random.value * Mathf.PI * 2f, r = UnityEngine.Random.value * radius;
                float x = me.x + Mathf.Cos(th) * r, z = me.z + Mathf.Sin(th) * r;
                float h = _k == Kind.Snow ? UnityEngine.Random.Range(18f, 22f) : UnityEngine.Random.Range(32f, 42f); // :36-37; :39-40
                var p = new P { Spawn = at, X = x, Z = z, BaseX = x, BaseZ = z, Y = _w.GroundY(x, z) + h, Size = UnityEngine.Random.Range(0.5f, 0.8f), LastTick = at }; // SnowWeather.js:237
                if (_k == Kind.Snow) p.Speed = 0.1f;                                              // "original -0.5 internal units/tick => 0.1 cells/tick" (:39-40)
                else if (_k == Kind.Sakura) { p.Speed = ((UnityEngine.Random.Range(0, 2) + 2) * 0.1f) / 5f; p.FacX = 0.24f; p.FacZ = 0.30f; }   // "(random(2)+2)*0.1f", X 0.24 Z 0.30
                else { p.Speed = ((UnityEngine.Random.Range(0, 4) + 2) * 0.03f) / 5f; p.FacX = 0.12f; p.FacZ = 0.15f; }                       // "(random(4)+2)*0.03f", X 0.12 Z 0.15
                p.AngX = UnityEngine.Random.Range(0f, 360f); p.AngZ = UnityEngine.Random.Range(0f, 360f); p.TgtX = UnityEngine.Random.Range(0f, 360f); p.TgtZ = UnityEngine.Random.Range(0f, 360f); // :271-274
                _ps.Add(p);
            }

            /// <summary>CEffectPrim::PrimSakura's angle walk as roBrowser quotes it (SakuraWeatherEffect.js:55-77), once per 25 ms tick.</summary>
            private static void Sway(ref float cur, ref float tgt)
            {
                if (tgt > cur) { cur = Mathf.Min(cur + 3f, 359f); if (cur >= tgt) tgt = UnityEngine.Random.value * cur; }
                else { cur = Mathf.Max(cur - 2f, 0f); if (cur <= tgt) tgt = 359f - UnityEngine.Random.value * cur; }
            }

            public void Tick(float now, Vector3 me)
            {
                float remaining = _end > 0 ? _end - now : float.MaxValue;
                if (_end > 0 && remaining <= 0) { Done = true; return; }
                bool emit = remaining > 160 * RagTickMs && !_stopping;                            // :24, 269 / :30
                if (_k == Kind.Snow)
                {
                    // 2 flakes per tick, missed ticks back-filled with their own times (SnowWeather.js:271-282)
                    int ticks = Mathf.FloorToInt((now - _lastEmit) / RagTickMs);
                    if (ticks > 0) { if (emit) for (int i = 0; i < ticks && i < 200; i++) { float at = _lastEmit + i * RagTickMs; SpawnOne(at, me); SpawnOne(at, me); } _lastEmit += ticks * RagTickMs; }
                }
                else if (now - _lastEmit >= 150f) { if (emit) SpawnOne(now, me); _lastEmit = now; } // one leaf every 150 ms, not back-filled (Sakura…js:29, 306-312)
                float life = Life, fin = FadeIn, fout = FadeOutStart;
                float alphaCap = _k == Kind.Sakura ? 0.5f : 1f; // Sakura…js:367-370 (roBrowser caps only the fades — a slip; the cap holds here)
                var act = _frames?.Act;
                for (int i = _ps.Count - 1; i >= 0; i--)
                {
                    var p = _ps[i];
                    float age = now - p.Spawn;
                    if (age >= life) { _ps.RemoveAt(i); continue; }
                    // per tick: fall, and for the leaves the sway (the C++ adds factor · sin(angle) to the position every tick, ÷ 5 → cells)
                    int steps = Mathf.FloorToInt((now - p.LastTick) / RagTickMs);
                    for (int s = 0; s < steps && s < 400; s++)
                    {
                        p.Y -= p.Speed;
                        if (_k != Kind.Snow) { Sway(ref p.AngX, ref p.TgtX); Sway(ref p.AngZ, ref p.TgtZ); p.X += p.FacX * Mathf.Sin(p.AngX * Mathf.Deg2Rad) / 5f; p.Z += p.FacZ * Mathf.Sin(p.AngZ * Mathf.Deg2Rad) / 5f; }
                    }
                    p.LastTick += steps * RagTickMs;
                    float a = 1f;
                    if (age < fin) a = age / fin; else if (age > fout) a = Mathf.Max(0f, 1f - (age - fout) / (life - fout));
                    a *= alphaCap;
                    if (_frames == null || act == null || act.Actions.Length == 0) continue;
                    var action = act.Actions[0];
                    float delay = Mathf.Max(action.Delay * RagTickMs, 1f);                       // ACT delay × 25 ms (roBrowser Loaders/Action.js:60)
                    int f = action.Frames.Length > 0 ? (int)(age / delay) % action.Frames.Length : 0;
                    _frames.Draw(_w._bb, f, new Vector3(p.X, p.Y, p.Z), p.Size, a, Billboards.Mode.Alpha);
                }
            }
        }

        // ================================================================== clouds (EF_CLOUD…, and the sky's own clouds)
        public sealed class CloudProfile
        {
            public int Max; public bool Overlay; public float Speed, Area, Size = 1000f; public Color Color; public string[] Textures; public float Z = -10f, VzScale = 0.05f;
            /// <summary>roBrowser CloudWeatherEffect.js:25-34 PROFILE_MAP (no source is given for these values), textures :127, size 1000 px :193-202.</summary>
            public static CloudProfile Of(int ef)
            {
                string[] clouds = { "cloud4", "cloud1", "cloud2" }, fog = { "fog1", "fog2", "fog3" };
                switch (ef)
                {
                    case EF_CLOUD: return new CloudProfile { Max = 40, Overlay = true, Speed = 0.05f, Area = 35, Color = new Color(1f, 1f, 1f, .18f), Textures = clouds };
                    case EF_CLOUD2: return new CloudProfile { Max = 60, Overlay = false, Speed = 0.05f, Area = 35, Color = new Color(1f, 1f, 1f, .58f), Textures = clouds };
                    case EF_CLOUD3: return new CloudProfile { Max = 40, Overlay = true, Speed = 0.015f, Area = 45, Color = new Color(.47f, .43f, .39f, .78f), Textures = fog };
                    case EF_CLOUD4: return new CloudProfile { Max = 80, Overlay = false, Speed = 0.05f, Area = 35, Color = new Color(1f, 1f, 1f, .58f), Textures = clouds };
                    case EF_CLOUD5: return new CloudProfile { Max = 80, Overlay = false, Speed = 0.2f, Area = 50, Color = new Color(.88f, .83f, .76f, .7f), Textures = clouds };
                    case EF_CLOUD6: return new CloudProfile { Max = 80, Overlay = false, Speed = 0.035f, Area = 35, Color = new Color(1f, 1f, 1f, .58f), Textures = clouds };
                    case EF_CLOUD7: return new CloudProfile { Max = 80, Overlay = false, Speed = 0.05f, Area = 35, Color = new Color(.2f, .31f, .63f, .55f), Textures = clouds };
                    case EF_CLOUD8: return new CloudProfile { Max = 80, Overlay = false, Speed = 0.05f, Area = 35, Color = new Color(1f, .55f, .2f, .62f), Textures = clouds };
                }
                return null;
            }
            /// <summary>roBrowser Renderer/Effects/Sky.js: 150 clouds cloud1..7.tga, ± 35 cells, 500 px, speed ± 0.01 a frame, opacity to 1,
            /// depth-tested; the count and colour come from the map's sky data (mapsky.tsv).</summary>
            public static CloudProfile Sky(Color c, int count) => new CloudProfile
            {
                Max = count, Overlay = false, Speed = 0.01f, Area = 35, Size = 500f, Color = new Color(c.r, c.g, c.b, 1f), VzScale = 0.1f,
                Textures = new[] { "cloud1", "cloud2", "cloud3", "cloud4", "cloud5", "cloud6", "cloud7" },
            };
        }

        /// <summary>
        /// roBrowser CloudWeatherEffect.js / Sky.js: each cloud fades in over 1 s to the profile's alpha, holds for 6 s (the first round a
        /// random 0-8 s so they start staggered), fades out over 2 s and comes back around our character; it drifts a little every frame
        /// (roBrowser moves them per rendered frame, taken here as 60 a second). Height: 10 cells below the map's zero (z = −10), under
        /// the floating sky maps. The "overlay" profiles (229, 233) are drawn without depth test, as roBrowser does.
        /// </summary>
        private sealed class Clouds
        {
            private sealed class C { public Vector3 Pos, Dir; public float Born, Death; public int Tex; }
            private readonly Weather _w; private readonly CloudProfile _p; private readonly List<C> _cs = new List<C>(); private readonly Texture2D[] _tex;
            private float _end = -1f, _lastMs; private bool _init;
            public bool Done { get; private set; } public int Count => _cs.Count;
            public Clouds(Weather w, CloudProfile p, float now)
            {
                _w = w; _p = p; _lastMs = now;
                _tex = new Texture2D[p.Textures.Length];
                for (int i = 0; i < _tex.Length; i++) _tex[i] = w._assets.Texture("effect\\" + p.Textures[i] + ".tga", true); // data/texture/effect/*.tga
            }
            public void Revive(float now) { _end = -1f; }
            public void Stop(float now) { if (_end < 0) _end = now + 2000f; } // CloudWeatherEffect.js:110-119: gone 2 s later, no fade

            private void Place(C c, Vector3 me, float now, bool first)
            {
                c.Pos = new Vector3(me.x + (int)(UnityEngine.Random.value * _p.Area) * (UnityEngine.Random.value > 0.5f ? 1 : -1), _p.Z,
                                    me.z + (int)(UnityEngine.Random.value * _p.Area) * (UnityEngine.Random.value > 0.5f ? 1 : -1));   // CW :171-173 / Sky.js :130-132
                c.Dir = new Vector3(UnityEngine.Random.Range(-1f, 1f) * _p.Speed, UnityEngine.Random.Range(-1f, 1f) * _p.Speed * _p.VzScale, UnityEngine.Random.Range(-1f, 1f) * _p.Speed); // :175-177
                if (first) { c.Born = now; c.Death = c.Born + UnityEngine.Random.value * 8000f; c.Born -= 2000f; }                                  // :145-159
                else { c.Born = c.Death + 2000f; c.Death = c.Born + 6000f; }                                                                          // :179-181
            }

            public void Tick(float now, Vector3 me)
            {
                if (_end > 0 && now >= _end) { Done = true; return; }
                if (!_init)
                {
                    _init = true;
                    for (int i = 0; i < _p.Max; i++) { var c = new C { Tex = _tex.Length > 1 ? UnityEngine.Random.Range(0, _tex.Length) : 0 }; Place(c, me, now, true); _cs.Add(c); }
                }
                float frames = Mathf.Clamp((now - _lastMs) / (1000f / 60f), 0f, 30f); _lastMs = now;
                float size = _p.Size / 35f;
                var mode = _p.Overlay ? Billboards.Mode.AlphaNoDepth : Billboards.Mode.Alpha;
                foreach (var c in _cs)
                {
                    float a;
                    if (c.Born + 1000f > now) a = Mathf.Min((now - c.Born) / 1000f, _p.Color.a);
                    else if (c.Death + 2000f < now) { Place(c, me, now, false); a = 0f; }
                    else if (c.Death < now) a = _p.Color.a - (now - c.Death) / 2000f;
                    else a = _p.Color.a;
                    c.Pos += c.Dir * frames;
                    if (a <= 0f) continue;
                    _w._bb.Add(_tex[c.Tex], mode, c.Pos, new Vector2(size, size) * 0.5f, 0f, new Color(_p.Color.r, _p.Color.g, _p.Color.b, a), new Rect(0, 0, 1, 1));
                }
            }
        }

        // ================================================================== fireworks
        /// <summary>
        /// roBrowser PokJukWeatherEffect.js ("adapted from C++ CEffectPrim::PrimPokJuk"): 1-3 rockets launched 10-17 s apart around us
        /// (± 10 cells), rising 0.04 cells a frame until 8 cells up, then bursts of coloured and white sparks under gravity; each rocket
        /// re-arms at our position when its sparks are gone — endless until the map changes. roBrowser counts in rendered frames (taken
        /// as 60 a second here); it means additive blending (:54) but its sprite call resets it to alpha (a slip): additive here.
        /// Sound effect\폭죽.wav at the launch (:156-158).
        /// </summary>
        private sealed class Fireworks
        {
            private sealed class Spark { public Vector3 Pos, Vel; public float Alpha, Fade; public Color Col; }
            private sealed class Rocket { public float Process; public Vector3 Start, Pos; public float Side; public bool Exploded; public float Timer, NextWave; public readonly List<Spark> Sparks = new List<Spark>(); }
            private static readonly Color[] Cols = { new Color(100 / 255f, 160 / 255f, 1f), new Color(1f, 100 / 255f, 100 / 255f), new Color(100 / 255f, 1f, 100 / 255f), new Color(1f, 1f, 130 / 255f), new Color(1f, 130 / 255f, 1f) }; // :250-281
            private readonly Weather _w; private readonly List<Rocket> _rs = new List<Rocket>(); private float _lastMs; private static Texture2D _dot;
            public int Count { get { int n = 0; foreach (var r in _rs) n += r.Exploded ? r.Sparks.Count : 1; return n; } }
            public Fireworks(Weather w, float now)
            {
                _w = w; _lastMs = now;
                int n = UnityEngine.Random.Range(1, 4);                                           // :114
                for (int i = 0; i < n; i++) _rs.Add(new Rocket { Process = -UnityEngine.Random.Range(600, 1000) - 100 * i }); // :117, 125
                if (_dot == null) _dot = MakeDot();
            }
            /// <summary>The 6 × 6 radial gradient roBrowser draws every spark with (:74-105).</summary>
            private static Texture2D MakeDot()
            {
                var t = new Texture2D(6, 6, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "pokjuk-dot" };
                for (int y = 0; y < 6; y++) for (int x = 0; x < 6; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(3f, 3f)) / 2f; // radius 2 px = 0
                    float a = d <= 0f ? 1f : d < 0.7f ? Mathf.Lerp(1f, 0.8f, d / 0.7f) : d < 1f ? Mathf.Lerp(0.8f, 0f, (d - 0.7f) / 0.3f) : 0f;
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
                t.Apply(false, true); return t;
            }
            private void Arm(Rocket r, Vector3 me)
            {
                r.Start = new Vector3(me.x + UnityEngine.Random.Range(-10f, 10f), me.y, me.z + UnityEngine.Random.Range(-10f, 10f)); // :127
                r.Pos = r.Start; r.Side = UnityEngine.Random.Range(3f, 5f) * (UnityEngine.Random.value > 0.5f ? 1 : -1); r.Exploded = false; r.Timer = 0; r.NextWave = 0; r.Sparks.Clear();
            }
            private static Spark MakeSpark(Vector3 at, bool coloured, Color col)
            {
                var dir = UnityEngine.Random.onUnitSphere;
                float sp = coloured ? UnityEngine.Random.Range(0.08f, 0.15f) : UnityEngine.Random.Range(0.02f, 0.06f); // :217-235
                var v = dir * sp; if (!coloured) v.y += 0.5f * sp;
                return new Spark { Pos = at, Vel = v, Alpha = 255f, Fade = coloured ? 3f : 6f, Col = coloured ? col : Color.white };
            }
            public void Tick(float now, Vector3 me)
            {
                int frames = Mathf.Clamp(Mathf.RoundToInt((now - _lastMs) / (1000f / 60f)), 0, 30); if (frames > 0) _lastMs += frames * (1000f / 60f);
                for (int k = 0; k < frames; k++)
                    foreach (var r in _rs)
                    {
                        r.Process += 1;
                        if (r.Process < 0) continue;
                        if (r.Process == 1) { Arm(r, me); Audio.LokiAudio.Instance?.PlayWav("effect\\ÆøÁ×.wav"); } // effect/폭죽.wav
                        if (!r.Exploded)
                        {
                            float p = Mathf.Clamp01((r.Pos.y - r.Start.y) / 8f);
                            r.Pos.y += 0.04f;                                                         // :163
                            if (p > 0.3f) r.Pos.x += ((p - 0.3f) / 0.7f) * ((p - 0.3f) / 0.7f) * r.Side * 0.02f; // :164-166
                            if (r.Pos.y - r.Start.y > 8f)                                             // :26, 170
                            {
                                r.Exploded = true;
                                for (int i = 0; i < 30; i++) r.Sparks.Add(MakeSpark(r.Pos, false, Color.white)); // :176-178
                            }
                            continue;
                        }
                        r.Timer += 16;                                                                // :182-193
                        if (r.Timer < 1000 && r.Timer >= r.NextWave)
                        {
                            r.NextWave = r.Timer + 50; var col = Cols[UnityEngine.Random.Range(0, Cols.Length)];
                            for (int i = 0; i < 15; i++) r.Sparks.Add(MakeSpark(r.Pos, true, col));
                            for (int i = 0; i < 60; i++) r.Sparks.Add(MakeSpark(r.Pos, false, Color.white));
                        }
                        for (int i = r.Sparks.Count - 1; i >= 0; i--)
                        {
                            var s = r.Sparks[i];
                            s.Vel.y -= 0.003f; s.Pos += s.Vel; s.Alpha -= s.Fade;                    // :202-206
                            if (s.Alpha <= 0) r.Sparks.RemoveAt(i);
                        }
                        if (r.Sparks.Count == 0 && r.Timer > 1000) { r.Process = 0; }                 // :210-212: re-arms (Process 1 next frame)
                    }
                foreach (var r in _rs)
                {
                    if (r.Process < 1) continue;
                    var half = new Vector2(6f / 35f, 6f / 35f) * 0.5f;                                // 6 px (:27, 282)
                    if (!r.Exploded)
                    {
                        float p = Mathf.Clamp01((r.Pos.y - r.Start.y) / 8f);
                        _w._bb.Add(_dot, Billboards.Mode.AdditiveNoDepth, r.Pos, half, 0f, new Color(1f, 1f, 1f, Mathf.Min(p * 510f, 255f) / 255f), new Rect(0, 0, 1, 1)); // :167
                    }
                    else foreach (var s in r.Sparks) _w._bb.Add(_dot, Billboards.Mode.AdditiveNoDepth, s.Pos, half, 0f, new Color(s.Col.r, s.Col.g, s.Col.b, s.Alpha / 255f), new Rect(0, 0, 1, 1));
                }
            }
        }

        // ================================================================== rain
        /// <summary>What the rain is doing now (log, smoke): its particles and what it has made so far.</summary>
        public sealed class RainState
        {
            /// <summary>Strikes: lightning (each with its thunder); Flashes / DoubleFlashes: the flashes drawn (Comfort.LightningFlashes on).</summary>
            public int Drops, Splashes, Puddles, Landed, SplashesMade, PuddlesMade, PuddleHits, Strikes, Flashes, DoubleFlashes, Plinks, Thunders;
            public float BiggestPuddle, VeilAlpha, FirstStrikeAfterMs = -1f; public bool Flashing, Stopping; public Color Veil; public Vector2 WindTick;
            public override string ToString() => $"{Drops} drops ({Landed} landed), {Splashes} splashes ({SplashesMade} made), {Puddles} puddles ({PuddlesMade} made, {PuddleHits} drops into one, biggest {BiggestPuddle:0.#} px), {Strikes} lightning strikes ({Flashes} flashes drawn, {DoubleFlashes} double), {Plinks} drop sounds, {Thunders} thunders{(Stopping ? ", stopping" : "")}";
        }

        /// <summary>
        /// roBrowserLegacy's rain, Renderer/Effects/RainWeather.js (EF_RAIN 161; roBrowser calls it "Procedural multi-layer rain designed
        /// for aesthetic depth": a reimplementation with no official asset — rAthena sends EF_RAIN only from scripts and @effect 161
        /// (atcommand.cpp:6030-6049), @rain is commented out). Every number below is that file's, cited by line. Distances are cells, roBrowser's px are px / 35 cells.
        ///  • drops: 10 a 25 ms tick (:29, 895-910), at most 1100 (the oldest go: :31, 680-682), within 70 cells of us, 22-42 cells above
        ///    the ground (:34-36), in three depth layers (far 55 %, mid 30 %, near 15 %: speed, width, length, alpha, brightness :52-77),
        ///    each spawned upwind by exactly its drift (:642-650), with a wind jitter (:655-657), a tilt jitter of ±4° (:660) and a shimmer
        ///    (:972); they fall and drift with the slowly turning wind (:303-326) and fade by how far they have fallen (:953-969). The streak
        ///    (a 1 × 16 gradient, :121-155) is turned along its motion as the eye sees it (:337-360, 986: Resources/Shaders/LokiBillboard
        ///    .shader's _Align). The wind is the camera's in roBrowser (it turns with Camera.angle[1]); its default outdoor yaw is 0
        ///    (Camera.js:120, 276), at which camera and map axes agree: Loki keeps the map's axes (a headset has no one camera yaw).
        ///  • a drop that reached the ground (:916): a splash with the chance alphaScale × 0.6 (:918-932; 16 × 16 soft disc with a ring
        ///    :174-217, 220 ms, 10-18 px growing from 0.6 to 1.4 times, alpha 0.45 → 0, colour 0.75 / 0.8 / 0.9 :995-1027, the newest 300
        ///    kept) and its "plink" (:931), then a puddle hit (:935, updatePuddle :685-761: grow the nearest puddle within its impact radius,
        ///    else with 6 % a new one unless one is within √57.5 cells, the 600 most visible kept), then the drop goes.
        ///  • puddles: dry once nothing hit them for their time (each drop adds 1.5 s up to 10 s; 3.5 s for a new one), fading and
        ///    shrinking (:1067-1087); drawn flat on the ground with roBrowser's ripple shader (Resources/Shaders/LokiRainPuddle.shader).
        ///  • the grey veil (0.65, 0.67, 0.70, 0.14, 6000 px around us, :800-803, 857-883), which a lightning flash replaces: every 5-60 s
        ///    (:44-45, 377, 790-797), 40 % of them double (:795), white-blue (0.9, 0.95, 1.0) at the alpha of :805-854; thunder with it
        ///    (:791). While stopping, the veil fades over the 1000-tick tail unless a flash is on (:859-862).
        ///    The flash is drawn only with the Comfort option "Lightning flashes" (VR/Comfort.cs LightningFlashes, off by default: the design's
        ///    rule, no full-view flashes in a headset unless asked for). Off, every strike still comes on roBrowser's schedule with its
        ///    thunder, and the veil stays grey as between flashes; switched off during a flash, the flash ends at once.
        ///  • the sounds: World/RainAudio.cs.
        /// VR changes (design notes): drops and splashes are depth-tested like the other weather (roBrowser draws them over
        /// everything, :454), and a splash stands on the ground (its centre lifted by half its size) where roBrowser centres it on the
        /// ground: depth-tested, the lower half would vanish into the ground.
        /// </summary>
        private sealed class Rain
        {
            private const int EmitPerTick = 10, MaxDrops = 1100, MaxSplashes = 300, MaxPuddles = 600;                    // :29, 31, 928, 91
            private const float Scatter = 70f, HMin = 22f, HMax = 42f;                                                     // :34-36
            private const float WindBase = 0.14f, WindVar = 0.06f, WindAngleMax = 0.55f;                                   // :39-41
            private const float ThunderMin = 5000f, ThunderMax = 60000f, FlashIn = 50f, FlashOut = 300f;                   // :44-47
            private const float PuddleInitial = 20f, PuddleMax = 230f, PuddleGrowth = 4.5f, PuddleAlphaMax = 0.45f;        // :92-95
            private const float PuddleFade = 0.00004f, PuddleSeed = 0.06f;                                                 // :96-97
            private const float SplashLife = 220f, SplashMin = 10f, SplashMax = 18f, SplashAlpha = 0.45f;                  // :105-107
            private static float Tail => 1000 * RagTickMs;                                                                 // FADEOUT_TAIL_MS :26
            private static float StopEmitBefore => 160 * RagTickMs;                                                        // :30
            private static readonly (float w, float s0, float s1, float w0, float w1, float l0, float l1, float a, float b0, float b1)[] Layers =
            {
                (0.55f, 0.35f, 0.55f, 1.0f, 1.8f, 22f, 36f, 0.12f, 0.45f, 0.65f), // far   (:53-60)
                (0.30f, 0.55f, 0.85f, 1.6f, 2.6f, 36f, 55f, 0.22f, 0.65f, 0.85f), // mid   (:61-68)
                (0.15f, 0.85f, 1.25f, 2.3f, 3.4f, 55f, 78f, 0.35f, 0.8f, 1.0f),   // near  (:69-76)
            };
            private sealed class D { public float X, Y, Z, SpawnZ, Ground, SpeedMs, JitX, JitY, Tilt, W, L, Alpha, Bright, Flicker, Spawn, LastTick; }
            private sealed class S { public float X, Y, Z, Start, MaxPx; }
            public sealed class P { public float X, Y, Z, Size, Alpha, Angle, LastUpdate, NextDry, LastTick; }
            // positions are roBrowser's (x, y on the map, z up) = Loki's (x, z, y)
            private readonly Weather _w; private readonly List<D> _ds = new List<D>(); private readonly List<S> _ss = new List<S>(); private readonly List<P> _ps = new List<P>();
            private float _end = -1f, _lastEmit, _born; private bool _stopping;
            private float _nextLightning, _flashStart; private bool _flashing; private int _flashMulti = 1;
            private (float xTick, float yTick, float xMs, float yMs) _wind;
            private static Texture2D _streak, _white, _splash;
            public readonly RainState State = new RainState();
            public bool Done { get; private set; } public int Count => _ds.Count; public int SplashCount => _ss.Count; public int PuddleCount => _ps.Count;

            public Rain(Weather w, float now)
            {
                _w = w; _lastEmit = now; _born = now;
                _nextLightning = now + Range(ThunderMin, ThunderMax);                                                       // :377
                if (_streak == null)
                {
                    _streak = new Texture2D(1, 16, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, name = "rain-drop" };
                    for (int y = 0; y < 16; y++) { float t = y / 15f; float a = Mathf.Min(1f, t / 0.7f); if (t > 0.7f) a *= 1f - 0.35f * (t - 0.7f) / 0.3f; _streak.SetPixel(0, y, new Color(1, 1, 1, Mathf.Floor(a * 255f) / 255f)); } // :130-146
                    _streak.Apply(false, true);
                    _white = Texture2D.whiteTexture;
                    // the splash: a 16 × 16 soft disc with a slight ring (:180-206), linear filtering
                    _splash = new Texture2D(16, 16, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "rain-splash" };
                    float c = 7.5f;
                    for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
                    {
                        float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c, a = 0f;
                        if (r <= 1f) { a = Mathf.Exp(-r * r * 2.8f); float ring = Mathf.Exp(-Mathf.Pow((r - 0.65f) / 0.12f, 2f)); a = Mathf.Max(a, ring * 0.9f); }
                        _splash.SetPixel(x, y, new Color(1, 1, 1, Mathf.Floor(Mathf.Clamp01(a) * 255f) / 255f));
                    }
                    _splash.Apply(false, true);
                }
            }
            private static float Range(float a, float b) => a + UnityEngine.Random.value * (b - a);
            public void Revive(float now) { if (_end > 0) { _end = -1f; _lastEmit = now; } _stopping = false; State.Stopping = false; } // startOrRestart :428-433
            public void Stop(float now) { if (_end < 0) { _end = now + Tail; _stopping = true; State.Stopping = true; } }             // stop :476-486

            /// <summary>The lightning overlay's alpha <paramref name="ms"/> after a flash began (:805-854); false once it is over.</summary>
            public static float FlashAlpha(float ms, int multi, out bool on)
            {
                on = true;
                if (multi == 2)
                {
                    if (ms < 80f) return 0.6f * (1f - ms / 80f);                    // the first, faster flash
                    if (ms < 160f) return 0f;                                       // the dark pause
                    float e2 = ms - 160f;
                    if (e2 < FlashIn) return e2 / FlashIn * 0.8f;
                    if (e2 < FlashIn + FlashOut) return 0.8f * (1f - (e2 - FlashIn) / FlashOut);
                    on = false; return 0f;
                }
                if (ms < FlashIn) return ms / FlashIn * 0.8f;
                if (ms < FlashIn + FlashOut) return 0.8f * (1f - (ms - FlashIn) / FlashOut);
                on = false; return 0f;
            }

            /// <summary>computeGlobalWind (:303-326) at the outdoor camera's default yaw 0: cells per tick and per ms on the map's x / y.</summary>
            public static (float xTick, float yTick, float xMs, float yMs) Wind(float tick)
            {
                float t = tick * 0.00012f;
                float strength = WindBase + WindVar * Mathf.Sin(t * 2.3f), angle = Mathf.Sin(t * 1.1f) * WindAngleMax;
                float x = Mathf.Cos(angle) * strength, y = Mathf.Sin(angle) * strength * 0.35f;
                return (x, y, x / RagTickMs, y / RagTickMs);
            }

            private void Spawn(float at, Vector3 me)
            {
                float pick = UnityEngine.Random.value, acc = 0; var ly = Layers[Layers.Length - 1];                         // pickLayerIndex :293-301
                foreach (var l in Layers) { acc += l.w; if (pick <= acc) { ly = l; break; } }
                float th = UnityEngine.Random.value * Mathf.PI * 2f, r = UnityEngine.Random.value * Scatter;
                float speedTick = Range(ly.s0, ly.s1), h = Range(HMin, HMax), drift = h / speedTick;                         // :637-645
                float x = me.x + Mathf.Cos(th) * r - _wind.xTick * drift, y = me.z + Mathf.Sin(th) * r - _wind.yTick * drift; // :646-650
                float g = _w.GroundY(x, y);
                _ds.Add(new D
                {
                    Spawn = at, X = x, Y = y, Z = g + h, SpawnZ = g + h, Ground = g, SpeedMs = speedTick / RagTickMs,
                    JitX = (UnityEngine.Random.value - 0.5f) * 0.002f, JitY = (UnityEngine.Random.value - 0.5f) * 0.002f, Tilt = Range(-4f, 4f),
                    W = Range(ly.w0, ly.w1), L = Range(ly.l0, ly.l1), Alpha = ly.a, Bright = Range(ly.b0, ly.b1), Flicker = UnityEngine.Random.value * 1000f,
                });
                if (_ds.Count > MaxDrops) _ds.RemoveRange(0, _ds.Count - MaxDrops);
            }

            /// <summary>updatePuddle (:685-761), a drop hitting the ground at (x, y), height z.</summary>
            private void Hit(float x, float y, float z, float now)
            {
                P best = null; float bestD = float.MaxValue;
                foreach (var p in _ps)
                {
                    float dx = p.X - x, dy = p.Y - y, d2 = dx * dx + dy * dy, impact = Mathf.Max(1.5f, p.Size / 35f * 0.8f);
                    if (d2 < impact * impact && d2 < bestD) { bestD = d2; best = p; }
                }
                if (best != null)
                {
                    best.Size = Mathf.Min(PuddleMax, best.Size + PuddleGrowth);
                    best.Alpha = Mathf.Min(PuddleAlphaMax, best.Alpha + 0.02f);
                    float life = Mathf.Max(0f, best.NextDry - now);
                    best.NextDry = now + Mathf.Min(10000f, life + 1500f);
                    best.LastUpdate = now; State.PuddleHits++;
                    return;
                }
                if (UnityEngine.Random.value >= PuddleSeed) return;
                foreach (var p in _ps)
                {
                    float dx = p.X - x, dy = p.Y - y;
                    if (dx * dx + dy * dy < PuddleMax / 4f) { p.Size = Mathf.Min(PuddleMax, p.Size + PuddleGrowth * 0.5f); return; } // :725-731
                }
                if (_ps.Count >= MaxPuddles)
                {
                    int worst = 0; float min = float.MaxValue;
                    for (int i = 0; i < _ps.Count; i++) { float s = _ps[i].Size * _ps[i].Alpha; if (s < min) { min = s; worst = i; } }
                    _ps.RemoveAt(worst);
                }
                _ps.Add(new P { X = x, Y = y, Z = z, Size = PuddleInitial, Alpha = 0.12f, Angle = UnityEngine.Random.value * Mathf.PI * 2f, LastUpdate = now, NextDry = now + 3500f, LastTick = -1f });
                State.PuddlesMade++;
            }

            public void Tick(float now, Vector3 me)
            {
                // the puddles first: roBrowser renders them before the entities, the drops after (ScreenEffectManager.js:196-227)
                float biggest = 0f;
                for (int i = _ps.Count - 1; i >= 0; i--)
                {
                    var p = _ps[i];
                    if (now > p.NextDry)                                                                                       // :1071-1080
                    {
                        float dt = p.LastTick < 0 ? 0f : now - p.LastTick;
                        float fade = PuddleFade / (1f + p.Size * 0.003f) * dt;
                        p.Alpha -= fade; p.Size -= fade * 8500f;
                    }
                    p.LastTick = now;
                    if (p.Size <= 0.1f || p.Alpha <= 0.0001f) { _ps.RemoveAt(i); continue; }                                   // :1084-1087
                    biggest = Mathf.Max(biggest, p.Size);
                    _w._puddles.Add(new Vector3(p.X, p.Z + 0.01f, p.Y), p.Size / 35f, p.Angle, p.Alpha);                        // :1090-1094
                }
                _wind = Wind(now);
                // lightning (:786-797) and the veil it replaces (:799-883)
                if (!_flashing && now >= _nextLightning)
                {
                    _w._rainAudio?.Thunder(); State.Thunders++; State.Strikes++;
                    _flashMulti = UnityEngine.Random.value > 0.6f ? 2 : 1;
                    _nextLightning = now + Range(ThunderMin, ThunderMax);
                    bool show = VR.Comfort.LightningFlashes;
                    if (show) { _flashing = true; _flashStart = now; State.Flashes++; if (_flashMulti == 2) State.DoubleFlashes++; }
                    if (State.FirstStrikeAfterMs < 0f) State.FirstStrikeAfterMs = now - _born;
                    Debug.Log($"[Weather] rain: lightning, thunder, {(show ? (_flashMulti == 2 ? "a double flash" : "a single flash") : "no flash (Comfort: lightning flashes off)")}; the next in {(_nextLightning - now) / 1000f:0.0} s");
                }
                if (_flashing && !VR.Comfort.LightningFlashes) _flashing = false; // switched off during a flash: it ends at once
                var veil = new Color(0.65f, 0.67f, 0.70f, 0.14f);
                if (_flashing)
                {
                    float fa = FlashAlpha(now - _flashStart, _flashMulti, out bool on);
                    if (!on) _flashing = false; else veil = new Color(0.9f, 0.95f, 1.0f, fa);
                }
                if (_end > 0 && !_flashing) veil.a *= Mathf.Clamp01((_end - now) / Tail);
                _w._bb.Add(_white, Billboards.Mode.AlphaNoDepth, me + Vector3.up * 1f, new Vector2(6000f / 35f, 6000f / 35f) * 0.5f, 0f, veil, new Rect(0, 0, 1, 1));
                State.Flashing = _flashing; State.Veil = veil; State.VeilAlpha = veil.a; State.WindTick = new Vector2(_wind.xTick, _wind.yTick);

                float remaining = _end > 0 ? _end - now : float.MaxValue;
                if (_end > 0 && remaining <= 0) { Done = true; _ps.Clear(); return; }                                         // :887-893, free() :1122-1124
                // emission (:895-910): every missed tick back-filled with its own time; only the newest MaxDrops can survive the cap
                if (remaining > StopEmitBefore)
                {
                    int ticks = Mathf.FloorToInt((now - _lastEmit) / RagTickMs);
                    if (ticks > 0)
                    {
                        if (!_stopping)
                            for (int i = Mathf.Max(0, ticks - MaxDrops / EmitPerTick); i < ticks; i++)
                                for (int k = 0; k < EmitPerTick; k++) Spawn(_lastEmit + i * RagTickMs, me);
                        _lastEmit += ticks * RagTickMs;
                    }
                }
                // the drops (:912-993)
                for (int i = _ds.Count - 1; i >= 0; i--)
                {
                    var d = _ds[i];
                    if (d.Z <= d.Ground + 0.05f)
                    {
                        if (UnityEngine.Random.value < d.Alpha * 0.6f)
                        {
                            _ss.Add(new S { X = d.X, Y = d.Y, Z = d.Ground + 0.02f, Start = now, MaxPx = Range(SplashMin, SplashMax) });
                            if (_ss.Count > MaxSplashes) _ss.RemoveRange(0, _ss.Count - MaxSplashes);
                            _w._rainAudio?.Plink(); State.SplashesMade++; State.Plinks++;
                        }
                        Hit(d.X, d.Y, d.Ground, now); State.Landed++;
                        _ds.RemoveAt(i); continue;
                    }
                    float dt = now - (d.LastTick > 0f ? d.LastTick : d.Spawn); d.LastTick = now;
                    float wx = _wind.xMs + d.JitX, wy = _wind.yMs + d.JitY;
                    d.Z -= d.SpeedMs * dt; d.X += wx * dt; d.Y += wy * dt;
                    float den = d.SpawnZ - d.Ground, p = den > 0.0001f ? Mathf.Clamp01((d.SpawnZ - d.Z) / den) : 0f;
                    float a = d.Alpha; if (p < 0.08f) a *= p / 0.08f; else if (p > 0.85f) a *= (1f - p) / 0.15f;
                    a *= 0.9f + 0.1f * Mathf.Sin((now + d.Flicker) * 0.015f);
                    var col = new Color(0.6f * d.Bright, 0.72f * d.Bright, 1.0f * d.Bright, a);
                    _w._bb.Add(_streak, Billboards.Mode.AlphaAligned, new Vector3(d.X, d.Z, d.Y), new Vector2(d.W / 35f, d.L / 35f) * 0.5f, d.Tilt * Mathf.Deg2Rad, col, new Rect(0, 0, 1, 1), default, new Vector3(wx, -d.SpeedMs, wy));
                }
                // the splashes (:995-1027)
                for (int i = _ss.Count - 1; i >= 0; i--)
                {
                    var s = _ss[i]; float age = now - s.Start;
                    if (age >= SplashLife) { _ss.RemoveAt(i); continue; }
                    float sp = age / SplashLife, size = s.MaxPx * (0.6f + sp * 0.8f) / 35f;
                    _w._bb.Add(_splash, Billboards.Mode.Alpha, new Vector3(s.X, s.Z + size * 0.5f, s.Y), new Vector2(size, size) * 0.5f, 0f, new Color(0.75f, 0.8f, 0.9f, SplashAlpha * (1f - sp)), new Rect(0, 0, 1, 1));
                }
                State.Drops = _ds.Count; State.Splashes = _ss.Count; State.Puddles = _ps.Count; State.BiggestPuddle = biggest;
            }
        }

        /// <summary>The rain's puddles as one dynamic mesh of flat quads (Loki/RainPuddle), under the map loader like the billboards.</summary>
        private sealed class PuddleMesh
        {
            public Transform Root; private GameObject _go; private Mesh _mesh; private static Texture2D _tex; private bool _used;
            private readonly List<Vector3> _v = new List<Vector3>(); private readonly List<Vector2> _uv = new List<Vector2>(); private readonly List<Color> _c = new List<Color>(); private readonly List<int> _i = new List<int>();
            public void Begin() { _used = false; _v.Clear(); _uv.Clear(); _c.Clear(); _i.Clear(); }
            /// <summary>One puddle: centre (map cells, y up), side in cells, turned by angle about the vertical (RainWeather.vs:19-39).</summary>
            public void Add(Vector3 c, float side, float angle, float alpha)
            {
                if (Root == null) return;
                _used = true; int i0 = _v.Count; float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
                // RainWeather.js:278-283: (−0.5, −0.5) uv (0, 0), (0.5, −0.5) (1, 0), (−0.5, 0.5) (0, 1), (0.5, 0.5) (1, 1) on x / z
                for (int k = 0; k < 4; k++)
                {
                    float px = (k == 1 || k == 3 ? 0.5f : -0.5f) * side, pz = (k >= 2 ? 0.5f : -0.5f) * side;
                    _v.Add(new Vector3(c.x + px * ca - pz * sa, c.y, c.z + px * sa + pz * ca));
                    _uv.Add(new Vector2(k == 1 || k == 3 ? 1f : 0f, k >= 2 ? 1f : 0f));
                    _c.Add(new Color(1f, 1f, 1f, alpha));
                }
                _i.Add(i0); _i.Add(i0 + 1); _i.Add(i0 + 2); _i.Add(i0 + 1); _i.Add(i0 + 3); _i.Add(i0 + 2);
            }
            public void End()
            {
                if (!_used) { if (_go != null && _go.activeSelf) _go.SetActive(false); return; }
                if (_go == null) Make();
                if (!_go.activeSelf) _go.SetActive(true);
                _mesh.Clear();
                _mesh.indexFormat = _v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                _mesh.SetVertices(_v); _mesh.SetUVs(0, _uv); _mesh.SetColors(_c); _mesh.SetTriangles(_i, 0, false);
                _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            }
            public void Clear() { if (_go != null) _go.SetActive(false); }
            public int Quads => _i.Count / 6;

            /// <summary>The puddle texture of RainWeather.js:219-269: 64 × 64, a disc deformed by three sines, alpha (1 − r)^0.7, colour 18 / 32 / 55.</summary>
            public static Texture2D Texture()
            {
                if (_tex != null) return _tex;
                const int W = 64, H = 64; float cx = (W - 1) / 2f, cy = (H - 1) / 2f, maxR = Mathf.Min(cx, cy) * 0.85f;
                _tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "rain-puddle" };
                for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                {
                    float dx = x - cx, dy = y - cy, dist = Mathf.Sqrt(dx * dx + dy * dy), ang = Mathf.Atan2(dy, dx);
                    float deform = 1f + 0.15f * Mathf.Sin(ang * 5f) + 0.08f * Mathf.Sin(ang * 3f + 1.5f) + 0.05f * Mathf.Cos(ang * 11f);
                    float r = dist / (maxR * deform), a = r <= 1f ? Mathf.Pow(1f - r, 0.7f) : 0f;
                    _tex.SetPixel(x, y, new Color32(18, 32, 55, (byte)Mathf.Clamp(Mathf.FloorToInt(a * 255f), 0, 255)));
                }
                _tex.Apply(false, true);
                return _tex;
            }

            private void Make()
            {
                _go = new GameObject("weather:rain-puddles");
                _go.transform.SetParent(Root, false);
                _mesh = new Mesh { name = "rain-puddles" }; _mesh.MarkDynamic();
                _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var mr = _go.AddComponent<MeshRenderer>();
                var sh = Shader.Find("Loki/RainPuddle");
                if (sh == null) Debug.LogWarning("[Weather] Loki/RainPuddle shader missing");
                mr.sharedMaterial = new Material(sh != null ? sh : Shader.Find("Loki/Glow")) { name = "rain-puddles", mainTexture = Texture() };
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            }
        }

        // ================================================================== an ACT action's frames as composited textures
        private sealed class Frames
        {
            public SprFile Spr; public ActFile Act; private string _key;
            private readonly Dictionary<int, (Texture2D tex, Vector2Int center, int w, int h)> _cache = new Dictionary<int, (Texture2D, Vector2Int, int, int)>();
            private readonly List<SpriteFrameCache.CompositeLayer> _layers = new List<SpriteFrameCache.CompositeLayer>();
            public static Frames Load(RoAssets a, string path)
            {
                if (a == null || !a.Sprite(path, out var spr, out var act) || spr == null || act == null) { Debug.LogWarning("[Weather] sprite missing: " + path); return null; }
                return new Frames { Spr = spr, Act = act, _key = "w4weather:" + path };
            }
            public void Draw(Billboards bb, int frame, Vector3 pos, float scale, float alpha, Billboards.Mode mode)
            {
                if (!_cache.TryGetValue(frame, out var c))
                {
                    c = (null, Vector2Int.zero, 0, 0);
                    var a = Act.Actions[0];
                    if (frame < a.Frames.Length)
                    {
                        _layers.Clear();
                        foreach (var layer in a.Frames[frame].Layers)
                        {
                            if (layer.SpriteNumber < 0) continue;
                            int available = layer.SpriteType == 1 ? Spr.RgbaFrames.Length : Spr.IndexedFrames.Length;
                            if (layer.SpriteNumber >= available || layer.Color.a == 0 || layer.Scale.x == 0f || layer.Scale.y == 0f) continue;
                            var gpu = SpriteFrameCache.Get(Spr, layer.SpriteNumber, layer.SpriteType);
                            if (gpu.Width == 0 || gpu.Height == 0) continue;
                            _layers.Add(new SpriteFrameCache.CompositeLayer { Frame = gpu, X = layer.X, Y = layer.Y, Mirror = layer.Mirror, Color = layer.Color, ScaleX = layer.Scale.x, ScaleY = layer.Scale.y, Angle = layer.Angle });
                        }
                        if (_layers.Count > 0)
                        {
                            var comp = SpriteFrameCache.GetComposite(_key + ":" + frame, _layers, out var center);
                            if (comp != null) { comp.Texture.filterMode = FilterMode.Bilinear; c = (comp.Texture, center, comp.Width, comp.Height); }
                        }
                    }
                    _cache[frame] = c;
                }
                if (c.tex == null) return;
                float px = scale / 35f; // roBrowser: px / 35 cells at scale 1 (SpriteRenderer.js:84-87)
                // the composite's centre sits (cx, cy) px from the sprite origin (y down): the quad is offset in the view plane
                var offset = new Vector2(c.center.x * px, -c.center.y * px);
                bb.Add(c.tex, mode, pos, new Vector2(c.w * px, c.h * px) * 0.5f, 0f, new Color(1f, 1f, 1f, alpha), new Rect(0, 0, 1, 1), offset);
            }
        }
    }

    /// <summary>
    /// Camera-facing quads batched by texture and blend (Loki/Billboard shader): the four vertices of a quad share its centre and carry
    /// their corner in the second uv set, turned towards each eye in the vertex shader. One dynamic mesh per (texture, mode), rebuilt
    /// every frame, under the map loader so it follows the diorama.
    /// </summary>
    public sealed class Billboards
    {
        /// <summary>AlphaAligned (client wave 5, w5weather): alpha, depth-tested, each quad turned along its direction (the shader's _Align).</summary>
        public enum Mode { Alpha, AlphaNoDepth, AdditiveNoDepth, AlphaAligned }
        private sealed class Batch
        {
            public GameObject Go; public Mesh Mesh; public Material Mat; public bool Used;
            public readonly List<Vector3> V = new List<Vector3>(), Dir = new List<Vector3>(); public readonly List<Vector2> Uv = new List<Vector2>(), Corner = new List<Vector2>();
            public readonly List<Color> C = new List<Color>(); public readonly List<int> I = new List<int>();
        }
        private readonly Dictionary<(Texture, Mode), Batch> _batches = new Dictionary<(Texture, Mode), Batch>();
        public Transform Root; public int Quads { get; private set; }
        private int _quads;
        private static Shader _shader;

        public void Begin() { _quads = 0; foreach (var b in _batches.Values) { b.Used = false; b.V.Clear(); b.Dir.Clear(); b.Uv.Clear(); b.Corner.Clear(); b.C.Clear(); b.I.Clear(); } }

        /// <param name="dir">AlphaAligned only: the direction the quad's −y side points to (object space; e.g. a raindrop's velocity)</param>
        public void Add(Texture tex, Mode mode, Vector3 center, Vector2 half, float angle, Color col, Rect uv, Vector2 offset = default, Vector3 dir = default)
        {
            if (tex == null || Root == null) return;
            if (!_batches.TryGetValue((tex, mode), out var b)) _batches[(tex, mode)] = b = Make(tex, mode);
            b.Used = true;
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            int i0 = b.V.Count;
            for (int k = 0; k < 4; k++)
            {
                float cx = (k == 1 || k == 2) ? half.x : -half.x, cy = k >= 2 ? half.y : -half.y;
                b.V.Add(center);
                b.Corner.Add(new Vector2(cx * c - cy * s + offset.x, cx * s + cy * c + offset.y));
                b.Uv.Add(new Vector2(k == 1 || k == 2 ? uv.xMax : uv.xMin, k >= 2 ? uv.yMax : uv.yMin));
                b.C.Add(col);
                if (mode == Mode.AlphaAligned) b.Dir.Add(dir);
            }
            b.I.Add(i0); b.I.Add(i0 + 1); b.I.Add(i0 + 2); b.I.Add(i0); b.I.Add(i0 + 2); b.I.Add(i0 + 3);
            _quads++;
        }

        public void End()
        {
            Quads = _quads;
            foreach (var b in _batches.Values)
            {
                if (b.Go == null) continue;
                if (!b.Used) { if (b.Go.activeSelf) b.Go.SetActive(false); continue; }
                if (!b.Go.activeSelf) b.Go.SetActive(true);
                b.Mesh.Clear();
                b.Mesh.indexFormat = b.V.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                b.Mesh.SetVertices(b.V); b.Mesh.SetUVs(0, b.Uv); b.Mesh.SetUVs(1, b.Corner); b.Mesh.SetColors(b.C);
                if (b.Dir.Count == b.V.Count) b.Mesh.SetUVs(2, b.Dir);
                b.Mesh.SetTriangles(b.I, 0, false);
                // the quads open up to ~90 cells around their centres: generous bounds so a corner never gets culled
                b.Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            }
        }

        public void Clear() { foreach (var b in _batches.Values) if (b.Go != null) b.Go.SetActive(false); }

        private Batch Make(Texture tex, Mode mode)
        {
            if (_shader == null) _shader = Shader.Find("Loki/Billboard");
            var b = new Batch();
            b.Go = new GameObject("weather:" + tex.name + ":" + mode);
            b.Go.transform.SetParent(Root, false);
            b.Mesh = new Mesh { name = "weather" }; b.Mesh.MarkDynamic();
            b.Go.AddComponent<MeshFilter>().sharedMesh = b.Mesh;
            var mr = b.Go.AddComponent<MeshRenderer>();
            b.Mat = new Material(_shader != null ? _shader : Shader.Find("Loki/Glow")) { name = "weather" };
            b.Mat.mainTexture = tex;
            b.Mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            b.Mat.SetFloat("_DstBlend", (float)(mode == Mode.AdditiveNoDepth ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            b.Mat.SetFloat("_ZTest", (float)(mode == Mode.Alpha || mode == Mode.AlphaAligned ? UnityEngine.Rendering.CompareFunction.LessEqual : UnityEngine.Rendering.CompareFunction.Always));
            b.Mat.SetFloat("_Align", mode == Mode.AlphaAligned ? 1f : 0f);
            mr.sharedMaterial = b.Mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            return b;
        }
    }
}
