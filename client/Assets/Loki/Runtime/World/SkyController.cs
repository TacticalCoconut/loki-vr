using System.Collections.Generic;
using UnityEngine;

namespace Loki.World
{
    /// <summary>
    /// Picks a sky per map. RO itself has no skybox (it clears to a colour), so the look is derived from the map:
    ///  • interiors (client's data\indoorrswtable.txt) → a dim room tone;
    ///  • dungeons (name contains "dun", plus a few known cave maps) → darkness tinted by the map's fog colour;
    ///  • haunted / night maps (Niflheim, Glast Heim, Abyss, Thanatos) → night sky with stars;
    ///  • everything else → day sky, horizon tinted by the map's fog colour from data\fogparametertable.txt
    ///    (blocks of "map.rsw#", near#, far#, 0xAARRGGBB#, density#), brightness from the RSW sun colour.
    /// The sun sits where the map's RSW light comes from and turns with the diorama.
    /// </summary>
    public sealed class SkyController : MonoBehaviour
    {
        private Material _mat; private Transform _mapRoot; private Vector3 _lightLocal = new Vector3(0.3f, 0.7f, 0.3f);
        private readonly Dictionary<string, Color> _fog = new Dictionary<string, Color>();
        private readonly Dictionary<string, (float near, float far)> _fogRange = new Dictionary<string, (float, float)>();
        public string Theme { get; private set; } = "";
        /// <summary>Client wave 2: the map's fog from data\fogparametertable.txt (near / far as the file gives them, colour); false = no fog.</summary>
        public bool FogOf(string map, out float near, out float far, out Color color)
        {
            map = (map ?? "").ToLowerInvariant(); near = far = 0; color = Color.gray;
            if (!_fog.TryGetValue(map, out color) || !_fogRange.TryGetValue(map, out var r)) return false;
            near = r.near; far = r.far; return far > near;
        }
        /// <summary>Makes the horizon exactly the fog colour on a fogged map (the fogged ground must meet the sky) — or back to the theme.</summary>
        public void SetHorizon(Color? c) { _fogHorizon = c; WriteColors(); }
        /// <summary>Night (0 day … 1 night) darkens the sky with the same multiplier the ground gets (roBrowser has no sky; Loki does).</summary>
        public void SetNight(Color env) { _env = env; WriteColors(); }
        /// <summary>Client wave 24 (w24), item 12: the sky's colours as drawn now, overhead and at the horizon (false: none yet).</summary>
        public bool SkyColors(out Color zenith, out Color horizon) { zenith = _zen * _env; horizon = (_fogHorizon ?? _hor) * _env; return _haveColors; }
        private Color? _fogHorizon; private Color _env = Color.white; private Color _zen, _hor, _gnd, _cloud = Color.white; private bool _haveColors;
        private void WriteColors()
        {
            if (_mat == null || !_haveColors) return;
            _mat.SetColor("_Zenith", _zen * _env); _mat.SetColor("_Horizon", (_fogHorizon ?? _hor) * _env); _mat.SetColor("_Ground", _gnd * _env); _mat.SetColor("_CloudColor", _cloud * _env);
        }

        private static readonly string[] Caves = { "anthell", "prt_sewb", "moc_pryd", "orcsdun", "iz_dun", "pay_dun", "gef_dun", "mjo_dun", "treasure", "tur_dun", "alde_dun", "c_tower", "in_sphinx", "beach_dun", "ama_dun", "gon_dun", "um_dun", "ein_dun", "lhz_dun", "juperos", "thor_v", "ice_dun", "kh_dun", "mag_dun", "odin_tem", "ra_san", "xmas_dun", "lou_dun", "dew_dun", "mal_dun", "ecl_tdun", "bra_dun", "dic_dun", "iz_ac" };
        private static readonly string[] Night = { "niflheim", "nif_", "gl_", "abyss", "thana", "1@", "2@", "gld_dun" };

        public void Init(Loki.IO.RoFileSystem fs, Transform mapRoot)
        {
            _mapRoot = mapRoot;
            var sh = Shader.Find("Loki/Sky");
            if (sh == null) { Debug.LogWarning("[Sky] Loki/Sky shader missing"); return; }
            _mat = new Material(sh) { name = "LokiSky" };
            RenderSettings.skybox = _mat;
            var data = fs?.Read("data\\fogparametertable.txt");
            if (data != null)
            {
                var parts = System.Text.Encoding.GetEncoding(28591).GetString(data).Split('#');
                // tokens: map.rsw, near, far, color, density, map.rsw, … (comments are on their own lines before names)
                for (int i = 0; i + 4 < parts.Length; i++)
                {
                    string name = LastLine(parts[i]);
                    if (!name.EndsWith(".rsw")) continue;
                    string col = parts[i + 3].Trim();
                    if (col.StartsWith("0x") && uint.TryParse(col.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out uint argb))
                    {
                        string key = name.Substring(0, name.Length - 4).ToLowerInvariant();
                        _fog[key] = new Color(((argb >> 16) & 255) / 255f, ((argb >> 8) & 255) / 255f, (argb & 255) / 255f, 1f);
                        // client wave 2: near and far too (roBrowser DBManager.js:4123-4153 parseFloat; MapRenderer.js:503-508 multiplies them by 240)
                        var inv = System.Globalization.CultureInfo.InvariantCulture;
                        if (float.TryParse(parts[i + 1].Trim(), System.Globalization.NumberStyles.Float, inv, out float near) && float.TryParse(parts[i + 2].Trim(), System.Globalization.NumberStyles.Float, inv, out float far))
                            _fogRange[key] = (near, far);
                    }
                    i += 4;
                }
            }
            Debug.Log($"[Sky] fog colours for {_fog.Count} maps");
        }

        private static string LastLine(string s)
        {
            var lines = s.Replace("\r", "").Split('\n');
            for (int k = lines.Length - 1; k >= 0; k--) { var l = lines[k].Trim(); if (l.Length > 0 && !l.StartsWith("//")) return l.ToLowerInvariant(); }
            return "";
        }

        public void Apply(string map, bool indoor, Formats.RswFile rsw, Vector3 lightDirLocal)
        {
            if (_mat == null) return;
            _lightLocal = lightDirLocal;
            map = map.ToLowerInvariant();
            bool hasFog = _fog.TryGetValue(map, out var fog);
            Color sun = rsw != null ? rsw.LightDiffuse : Color.white;
            float bright = Mathf.Clamp(0.45f + 0.55f * Mathf.Max(sun.r, Mathf.Max(sun.g, sun.b)), 0.35f, 1.1f);

            Color zen, hor, gnd, cloud = Color.white; float clouds, stars = 0f, sunSize = 0.035f;
            if (indoor)
            {
                Theme = "indoor"; zen = new Color(0.03f, 0.03f, 0.045f); hor = new Color(0.07f, 0.065f, 0.07f); gnd = new Color(0.02f, 0.02f, 0.02f); clouds = 0; sunSize = 0;
            }
            else if (Contains(map, Night))
            {
                Theme = "night"; var t = hasFog ? fog : new Color(0.4f, 0.25f, 0.5f);
                zen = new Color(0.02f, 0.02f, 0.06f); hor = Color.Lerp(new Color(0.12f, 0.08f, 0.18f), t * 0.5f, 0.5f); gnd = new Color(0.05f, 0.04f, 0.06f);
                clouds = 0.25f; cloud = new Color(0.25f, 0.2f, 0.3f); stars = 1f; sunSize = 0.02f;
            }
            else if (map.Contains("dun") || Contains(map, Caves))
            {
                Theme = "dungeon"; var t = hasFog ? fog : new Color(0.3f, 0.25f, 0.2f);
                zen = new Color(0.02f, 0.02f, 0.025f); hor = t * 0.22f; gnd = t * 0.08f; clouds = 0; sunSize = 0;
            }
            else
            {
                Theme = "day";
                var tint = hasFog ? fog : new Color(0.85f, 0.9f, 1f);
                zen = new Color(0.22f, 0.45f, 0.88f) * bright;
                hor = Color.Lerp(new Color(0.78f, 0.87f, 0.97f), tint, 0.35f) * bright;
                gnd = Color.Lerp(new Color(0.42f, 0.4f, 0.36f), tint, 0.25f) * bright;
                clouds = 0.5f; cloud = Color.Lerp(Color.white, tint, 0.15f) * Mathf.Min(1f, bright + 0.1f);
                if (map.Contains("moc") || map.Contains("cmd") || map.Contains("veins") || map.Contains("rachel") || map.Contains("ra_fild")) { hor = Color.Lerp(hor, new Color(1f, 0.85f, 0.6f), 0.4f); clouds = 0.25f; Theme = "day-desert"; }
                if (map.Contains("xmas") || map.Contains("lutie") || map.Contains("ice")) { zen = Color.Lerp(zen, new Color(0.6f, 0.7f, 0.85f), 0.5f); clouds = 0.7f; Theme = "day-snow"; }
            }
            // client wave 4 (w4world): the map's own sky (mapskydata.lub → data/mapsky.tsv) wins over the themes above
            string own = ApplyMapSky(map, indoor, ref zen, ref hor, ref gnd, ref clouds, ref stars, ref sunSize);
            _zen = zen; _hor = hor; _gnd = gnd; _cloud = cloud; _haveColors = true; _fogHorizon = null; WriteColors();
            _mat.SetFloat("_Clouds", clouds); _mat.SetFloat("_Stars", stars); _mat.SetFloat("_SunSize", sunSize);
            _mat.SetColor("_SunColor", Color.Lerp(new Color(1f, 0.95f, 0.8f), sun, 0.3f));
            var cam = Camera.main; if (cam != null) cam.clearFlags = CameraClearFlags.Skybox;
            Debug.Log($"[Sky] {map}: {Theme}{(hasFog ? " (fog " + ColorUtility.ToHtmlStringRGB(fog) + ")" : "")}{own}");
        }

        // ------------------------------------------------------------------ client wave 4 (w4world): the client's per-map sky and map weather
        /// <summary>The weather system (World/Weather.cs), set by LokiGame.W4World: the sky's clouds and roBrowser's map weather go there.</summary>
        public Weather Weather;
        /// <summary>What the last map's own sky set (smoke / log): background, stars, cloud type, the clouds drawn and the map weather.</summary>
        public string OwnSky { get; private set; } = "";

        /// <summary>
        /// roBrowser clears the screen to the map's sky colour and draws Sky.js's clouds under the map for the 12 maps of its table, black
        /// and nothing else everywhere else (Renderer/Effects/Sky.js:51-80, DB/Effects/WeatherEffect.js). Loki takes the client's own
        /// table instead (80 maps, <see cref="MapSky"/>) and, for the maps it lacks, keeps the themes above (a black void around a VR table
        /// would hide the diorama's edges; roBrowser's camera never shows the sky there).
        ///  • BG_Color → the whole sky (zenith, horizon and below: one colour, as a clear colour is), no sun (RO draws none).
        ///  • Star_Effect → the sky shader's stars. BG_Fog is only logged (what the client does with it is not known).
        ///  • Old_Cloud_Effect → Sky.js's 150 clouds under the map for the cloud types roBrowser's table shows (types 1, 2 and 4 white:
        ///    yuno / valkyrie / airplane …; 5 dark red: thana_boss; 8 pink: 5@tower); type 3 is einbroch's, which roBrowser draws as
        ///    EF_CLOUD3 (WeatherEffect.js:36); types 0, 7, 10, 11 and 15 have no roBrowser counterpart and are not drawn.
        ///  • Cloud_Effect layers (16 maps, instances mostly): not drawn — no reference client draws them and their Num / CullDist /
        ///    Size / Expand_Rate / alpha times / Height cannot be read without the client's renderer (listed in design notes).
        ///  • roBrowser's map weather table (WeatherEffect.js:34-36): xmas snow, comodo fireworks, einbroch EF_CLOUD3 — client-side, on
        ///    top of whatever the server's map flags send.
        /// The weather drawn so far goes first, at every map load — a teleport inside the same map and /refresh too: rAthena's own words
        /// for the client, "Weather effects will dispell on warp/refresh" (map_msg.conf:291, said by @clearweather atcommand.cpp:7380-7396;
        /// clif_refresh reloads the map, clif.cpp:9844), and the server sends the flags again once the map is loaded (clif.cpp:11148,
        /// 9887). roBrowser keeps it on a teleport inside the same map.
        /// </summary>
        private string ApplyMapSky(string map, bool indoor, ref Color zen, ref Color hor, ref Color gnd, ref float clouds, ref float stars, ref float sunSize)
        {
            if (Weather != null) Weather.Clear();
            string log = "";
            if (!indoor && MapSky.TryGet(map, out var e))
            {
                Theme = "map sky";
                if (e.Background.HasValue) { zen = hor = gnd = e.Background.Value; clouds = 0f; sunSize = 0f; }
                if (e.Stars.HasValue) stars = e.Stars.Value ? 1f : 0f;
                Color? cloudColor = null; string cloudWhy = "none";
                if (e.OldCloud.HasValue)
                {
                    switch (e.OldCloud.Value)
                    {
                        case 1: case 2: case 4: cloudColor = new Color(1f, 1f, 1f); cloudWhy = "white (roBrowser WeatherEffect.js:18-27)"; break;
                        case 5: cloudColor = new Color(0.37f, 0f, 0f); cloudWhy = "dark red (WeatherEffect.js:31)"; break;
                        case 8: cloudColor = new Color(1f, 0.7f, 0.7f); cloudWhy = "pink (WeatherEffect.js:30)"; break;
                        case 3: cloudWhy = "EF_CLOUD3 (WeatherEffect.js:36)"; break;
                        default: cloudWhy = "type " + e.OldCloud.Value + " not drawn (no reference)"; break;
                    }
                }
                if (Weather != null) Weather.SetSkyClouds(cloudColor, cloudColor.HasValue ? 150 : 0);
                log = $" — its own sky: background {(e.Background.HasValue ? ColorUtility.ToHtmlStringRGB(e.Background.Value) : "-")}, stars {(e.Stars.HasValue ? e.Stars.Value.ToString() : "-")}, " +
                      $"fog {(e.Fog.HasValue ? e.Fog.Value.ToString() : "-")}, old cloud {(e.OldCloud.HasValue ? e.OldCloud.Value.ToString() : "-")} → {cloudWhy}, {e.Clouds.Count} cloud layers (not drawn)";
            }
            else if (Weather != null) Weather.SetSkyClouds(null, 0);
            // roBrowser's client-side map weather (DB/Effects/WeatherEffect.js:34-36)
            if (Weather != null && !indoor)
            {
                int ef = map == "xmas" ? Weather.EF_SNOW : map == "comodo" ? Weather.EF_POKJUK : map == "einbroch" ? Weather.EF_CLOUD3 : 0;
                if (ef != 0) { Weather.Play(ef); log += $"; map weather {Weather.Name(ef)} (roBrowser WeatherEffect.js)"; }
            }
            OwnSky = log;
            return log;
        }

        private static bool Contains(string map, string[] keys) { foreach (var k in keys) if (map.Contains(k)) return true; return false; }

        private void LateUpdate()
        {
            if (_mat == null) return;
            var d = _mapRoot != null ? _mapRoot.TransformDirection(_lightLocal) : _lightLocal;
            if (d.sqrMagnitude > 1e-6f) _mat.SetVector("_SunDir", d.normalized);
        }
    }
}
