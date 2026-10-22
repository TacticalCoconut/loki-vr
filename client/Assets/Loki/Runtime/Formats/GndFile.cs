using System.Collections.Generic;
using System.IO;
using Loki.IO;

namespace Loki.Formats
{
    /// <summary>GRGN: ground mesh — textures, lightmaps, textured surfaces and the height grid (2 GAT cells per GND tile).</summary>
    public sealed class GndFile
    {
        public struct Surface
        {
            public float U0, U1, U2, U3, V0, V1, V2, V3; // corner order: (x,y) (x+1,y) (x,y+1) (x+1,y+1)
            public short TextureIndex;   // into Textures (unique list)
            public short LightmapIndex;
            public byte B, G, R, A;      // stored BGRA
        }

        public struct Tile
        {
            public float H0, H1, H2, H3; // (x,y) (x+1,y) (x,y+1) (x+1,y+1); RO units, y-down
            public int Top, Front, Right; // surface indices or -1. Front = wall toward y+1 (north), Right = wall toward x+1 (east)
        }

        public sealed class WaterInfo
        {
            public float Level, WaveHeight, WaveSpeed, WavePitch;
            public int Type, AnimSpeed, SplitWidth, SplitHeight;
        }

        public RoVersion Version;
        public int Width, Height;
        public float Zoom;
        public List<string> Textures = new List<string>();
        public int LightmapCount, LightmapW, LightmapH, LightmapCellSize;
        public byte[] LightmapData; // LightmapCount * (W*H*cell) * 4 : 64 shadow bytes then 192 RGB bytes per 8x8 lightmap
        public Surface[] Surfaces;
        public Tile[] Tiles;
        public WaterInfo Water; // null before 1.8

        public int LightmapBytesPerCell => LightmapW * LightmapH * LightmapCellSize; // 64 for 8x8

        /// <summary>Client wave 7 (w7data): a version newer than 1.9 or a damaged file throws <see cref="RoFormatException"/>
        /// (Formats/RoFormat.cs: the known versions and their sources) — the caller gives a safe result.</summary>
        public static GndFile Parse(byte[] data) => RoFormat.Guard("GND", () => ParseCore(data));

        private static GndFile ParseCore(byte[] data)
        {
            using (var r = new RoReader(data))
            {
                RoFormat.Signature("GND", r.Signature(4), "GRGN");
                var g = new GndFile { Version = RoVersion.MajorFirst(r) };
                RoFormat.CheckVersion("GND", g.Version, 1, 9);
                g.Width = r.S32();
                g.Height = r.S32();
                g.Zoom = r.F32();
                if (g.Width <= 0 || g.Height <= 0) throw new RoFormatException("GND", $"damaged: size {g.Width} x {g.Height}", false);

                int texCount = r.S32();
                int texLen = r.S32();
                if (texLen <= 0 || texLen > 4096) throw new RoFormatException("GND", $"damaged: texture name length {texLen}", false);
                RoFormat.Count("GND", r, texCount, texLen, "texture names");
                var remap = new int[texCount];
                for (int i = 0; i < texCount; i++)
                {
                    string t = r.Str(texLen);
                    int idx = g.Textures.IndexOf(t);
                    if (idx < 0) { idx = g.Textures.Count; g.Textures.Add(t); }
                    remap[i] = idx;
                }

                g.LightmapCount = r.S32();
                g.LightmapW = r.S32();
                g.LightmapH = r.S32();
                g.LightmapCellSize = r.S32();
                int perCell = g.LightmapBytesPerCell;
                if (g.LightmapW < 0 || g.LightmapH < 0 || g.LightmapCellSize < 0 || perCell < 0) throw new RoFormatException("GND", $"damaged: lightmap cells {g.LightmapW} x {g.LightmapH} x {g.LightmapCellSize}", false);
                RoFormat.Count("GND", r, g.LightmapCount, (long)perCell * 4, "lightmaps");
                g.LightmapData = r.Bytes(g.LightmapCount * perCell * 4);

                int surfCount = RoFormat.Count("GND", r, r.S32(), 40, "surfaces");
                g.Surfaces = new Surface[surfCount];
                for (int i = 0; i < surfCount; i++)
                {
                    var s = new Surface
                    {
                        U0 = r.F32(), U1 = r.F32(), U2 = r.F32(), U3 = r.F32(),
                        V0 = r.F32(), V1 = r.F32(), V2 = r.F32(), V3 = r.F32(),
                    };
                    int ti = r.U16();
                    s.TextureIndex = (short)(ti < texCount ? remap[ti] : -1);
                    s.LightmapIndex = (short)r.U16();
                    s.B = r.U8(); s.G = r.U8(); s.R = r.U8(); s.A = r.U8();
                    g.Surfaces[i] = s;
                }

                bool wide = g.Version.AtLeast(1, 7);
                RoFormat.Count("GND", r, (long)g.Width * g.Height, wide ? 28 : 22, "tiles");
                g.Tiles = new Tile[g.Width * g.Height];
                for (int i = 0; i < g.Tiles.Length; i++)
                {
                    var t = new Tile { H0 = r.F32(), H1 = r.F32(), H2 = r.F32(), H3 = r.F32() };
                    t.Top = wide ? r.S32() : r.S16();
                    t.Front = wide ? r.S32() : r.S16();
                    t.Right = wide ? r.S32() : r.S16();
                    g.Tiles[i] = t;
                }

                if (g.Version.AtLeast(1, 8) && !r.AtEnd)
                {
                    g.Water = new WaterInfo
                    {
                        Level = r.F32(), Type = r.S32(), WaveHeight = r.F32(), WaveSpeed = r.F32(), WavePitch = r.F32(),
                        AnimSpeed = r.S32(), SplitWidth = r.S32(), SplitHeight = r.S32()
                    };
                    // 1.9+: per-zone water settings follow (SplitWidth*SplitHeight entries of 24 bytes); the global block above is used.
                }
                return g;
            }
        }
    }
}
