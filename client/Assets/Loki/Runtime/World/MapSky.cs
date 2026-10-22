using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Loki.World
{
    /// <summary>
    /// Client wave 4 (w4world): the client's own per-map sky, data\luafiles514\lua files\mapskydata\mapskydata.lub (MapSkyData, 80 maps
    /// in the 2026-09-30 LATAM GRF), made into data/mapsky.tsv on first run (Data/WorldTools.cs = client/tools/clientdata/
    /// build_world_tables.py). korangar reads the same file (world/library/map_sky_data.rs) but draws none of it; roBrowser keeps a
    /// hand-made table of 12 of these maps (DB/Effects/WeatherEffect.js:18-31) whose sky colours this file supersedes (yuno there is
    /// 0.4,0.6,0.8; here BG_Color 153,204,255; thana_boss / 5@tower are the same).
    /// Fields: BG_Color (the colour the screen is cleared to), Star_Effect, BG_Fog, Old_Cloud_Effect (a cloud type), Cloud_Effect (layers
    /// with Num, CullDist, Color, Size, … — the client's own renderer is not available, so only what roBrowser draws is drawn: see
    /// <see cref="SkyController"/>).
    /// </summary>
    public static class MapSky
    {
        public sealed class CloudLayer { public int Num, CullDist, Size, SizeExtra, Height, HeightExtra; public Color Color; public string Raw; }
        public sealed class Entry
        {
            public string Map; public Color? Background; public bool? Stars, Fog; public int? OldCloud;
            public readonly List<CloudLayer> Clouds = new List<CloudLayer>();
        }

        private static Dictionary<string, Entry> _t;
        public static int Count { get { Load(); return _t.Count; } }

        public static bool TryGet(string map, out Entry e)
        {
            Load(); e = null;
            if (string.IsNullOrEmpty(map)) return false;
            string k = map.ToLowerInvariant(); if (k.EndsWith(".gat") || k.EndsWith(".rsw")) k = k.Substring(0, k.Length - 4);
            return _t.TryGetValue(k, out e);
        }

        private static void Load()
        {
            if (_t != null) return;
            _t = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            string p = Loki.Data.ClientData.File("mapsky.tsv");
            if (!File.Exists(p)) { Debug.LogWarning("[Sky] missing " + p + " (made on first run: Data/WorldTools.cs)"); return; }
            int I(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
            foreach (var line in File.ReadLines(p, System.Text.Encoding.UTF8))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var c = line.Split('\t');
                if (c[0] == "S" && c.Length >= 6)
                {
                    var e = new Entry { Map = c[1] };
                    if (c[2] != "-") { var rgb = c[2].Split(','); if (rgb.Length >= 3) e.Background = new Color(I(rgb[0]) / 255f, I(rgb[1]) / 255f, I(rgb[2]) / 255f); }
                    if (c[3] != "-") e.Stars = c[3] == "1";
                    if (c[4] != "-") e.Fog = c[4] == "1";
                    if (c[5] != "-") e.OldCloud = I(c[5]);
                    _t[e.Map] = e;
                }
                else if (c[0] == "C" && c.Length >= 19 && _t.TryGetValue(c[1], out var e))
                    e.Clouds.Add(new CloudLayer { Num = I(c[3]), CullDist = I(c[4]), Color = new Color(I(c[5]) / 255f, I(c[6]) / 255f, I(c[7]) / 255f), Size = I(c[8]), SizeExtra = I(c[9]), Height = I(c[17]), HeightExtra = I(c[18]), Raw = line });
            }
            Debug.Log($"[Sky] mapsky.tsv: {_t.Count} maps with the client's own sky ({p})");
        }
    }
}
