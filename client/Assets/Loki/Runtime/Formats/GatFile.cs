using System.IO;
using Loki.IO;
using Loki.Sprites;

namespace Loki.Formats
{
    /// <summary>GRAT: walkability grid, one cell = 5 RO units = 1 Loki world unit.</summary>
    public sealed class GatFile : RoPathSearch.ICells
    {
        public enum CellType : byte { Walkable = 0, NoWalk = 1, Water = 2, WaterWalkable = 3, WaterSnipable = 4, CliffSnipable = 5, Cliff = 6 }

        public struct Cell
        {
            public float H0, H1, H2, H3; // SW, SE, NW, NE corner heights (RO units, y-down)
            public CellType Type;
            /// <summary>The server's rule, map_gat2cell (rA map.cpp:3280-3299): types 0, 2, 3, 4, 6 walkable, 1 and 5 not (client wave
            /// 3; before it the client took 0 and 3 only). The low byte of the type is kept, as rAthena's map cache stores
            /// (unsigned char)type (rA src/tool/mapcache.cpp:110). Map edges: see <see cref="GatFile.Check"/>.</summary>
            public bool Walkable => RoPathSearch.GatWalkable((int)Type);
            public bool Water => Type == CellType.Water || Type == CellType.WaterWalkable || Type == CellType.WaterSnipable;
            public float AverageHeight => (H0 + H1 + H2 + H3) * 0.25f;
        }

        public RoVersion Version;
        public int Width, Height;
        public Cell[] Cells;

        /// <summary>Client wave 7 (w7data): a version newer than 1.3 or a damaged file throws <see cref="RoFormatException"/>
        /// (Formats/RoFormat.cs: the known versions and their sources) — the caller gives a safe result.</summary>
        public static GatFile Parse(byte[] data) => RoFormat.Guard("GAT", () => ParseCore(data));

        private static GatFile ParseCore(byte[] data)
        {
            using (var r = new RoReader(data))
            {
                RoFormat.Signature("GAT", r.Signature(4), "GRAT");
                var g = new GatFile { Version = RoVersion.MajorFirst(r) };
                RoFormat.CheckVersion("GAT", g.Version, 1, 3);
                g.Width = r.S32();
                g.Height = r.S32();
                if (g.Width <= 0 || g.Height <= 0) throw new RoFormatException("GAT", $"damaged: size {g.Width} x {g.Height}", false);
                RoFormat.Count("GAT", r, (long)g.Width * g.Height, 20, "cells");
                g.Cells = new Cell[g.Width * g.Height];
                for (int i = 0; i < g.Cells.Length; i++)
                {
                    var c = new Cell { H0 = r.F32(), H1 = r.F32(), H2 = r.F32(), H3 = r.F32() };
                    uint t = r.U32();
                    c.Type = (CellType)(t & 0xFF);
                    g.Cells[i] = c;
                }
                return g;
            }
        }

        public Cell Get(int x, int y) => Cells[x + y * Width];
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        // RoPathSearch.ICells: the map as the server's path search sees it
        public int Xs => Width;
        public int Ys => Height;
        /// <summary>map_getcellp(m, x, y, CELL_CHKNOPASS): outside the map and its last column / row are blocked (rA
        /// map.cpp:3329-3331), else the cell is blocked when not walkable. ZC_UPDATE_MAPINFO 0x0192 changes (walls, Ice Wall)
        /// are applied to <see cref="Cells"/> by LokiGame, so they count too.</summary>
        public bool Check(int x, int y) => RoPathSearch.NoPass(Width, Height, x, y, InBounds(x, y) && Cells[x + y * Width].Walkable);
    }
}
