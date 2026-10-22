using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Loki.VrMaps
{
    /// <summary>
    /// Writers of synthetic game files for the self-test, in the layouts the client's readers read (Formats/GatFile.cs 1.2,
    /// GndFile.cs 1.7 / 1.8, RswFile.cs 1.9, RsmFile.cs 1.4): what they write is read back by the game's own code, so a test map is
    /// exactly what the game would see.
    /// </summary>
    public static class Synth
    {
        private sealed class W
        {
            public readonly MemoryStream S = new MemoryStream(); public readonly BinaryWriter B;
            public W() { B = new BinaryWriter(S); }
            public W Sig(string s) { B.Write(Encoding.ASCII.GetBytes(s)); return this; }
            public W U8(int v) { B.Write((byte)v); return this; }
            public W U16(int v) { B.Write((ushort)v); return this; }
            public W I32(int v) { B.Write(v); return this; }
            public W U32(uint v) { B.Write(v); return this; }
            public W F(float v) { B.Write(v); return this; }
            public W V3(Vector3 v) { B.Write(v.x); B.Write(v.y); B.Write(v.z); return this; }
            public W Str(string s, int n) { var b = new byte[n]; var src = Encoding.ASCII.GetBytes(s ?? ""); Array.Copy(src, b, Math.Min(n - 1, src.Length)); B.Write(b); return this; }
            public byte[] Bytes() { B.Flush(); return S.ToArray(); }
        }

        // ------------------------------------------------------------------ GAT 1.2
        public sealed class Gat
        {
            public int Width, Height; public float[] H; public int[] Type; // H: 4 per cell (SW SE NW NE), RO units y-down
            public Gat(int w, int h) { Width = w; Height = h; H = new float[w * h * 4]; Type = new int[w * h]; }
            public void Set(int x, int y, int type, float h) { Type[x + y * Width] = type; for (int k = 0; k < 4; k++) H[(x + y * Width) * 4 + k] = h; }
            public byte[] Bytes()
            {
                var w = new W().Sig("GRAT").U8(1).U8(2).I32(Width).I32(Height);
                for (int i = 0; i < Width * Height; i++) { for (int k = 0; k < 4; k++) w.F(H[i * 4 + k]); w.U32((uint)Type[i]); }
                return w.Bytes();
            }
        }

        // ------------------------------------------------------------------ GND 1.7 / 1.8
        public sealed class Gnd
        {
            public int Width, Height; public List<string> Textures = new List<string> { "synth\\floor.bmp" };
            public float[] H; public int[] Top, Front, Right; public int[] TopTexture; // per tile
            public bool Water; public float WaterLevel, WaveHeight = 1f; public int WaterType;
            /// <summary>Textures whose top surface is tinted black (the tile colour 0, 0, 0: the loader's "black-tinted" void filler).</summary>
            public readonly HashSet<int> BlackTint = new HashSet<int>();
            public Gnd(int w, int h)
            {
                Width = w; Height = h; H = new float[w * h * 4]; Top = new int[w * h]; Front = new int[w * h]; Right = new int[w * h]; TopTexture = new int[w * h];
                for (int i = 0; i < w * h; i++) { Top[i] = 1; Front[i] = -1; Right[i] = -1; }
            }
            public void SetHeight(int tx, int ty, float h) { for (int k = 0; k < 4; k++) H[(tx + ty * Width) * 4 + k] = h; }
            public byte[] Bytes()
            {
                var w = new W().Sig("GRGN").U8(1).U8(Water ? 8 : 7).I32(Width).I32(Height).F(10f);
                w.I32(Textures.Count).I32(80); foreach (var t in Textures) w.Str(t, 80);
                w.I32(1).I32(8).I32(8).I32(1); for (int i = 0; i < 256; i++) w.U8(i < 64 ? 255 : 0); // one lightmap: full light, no colour
                // surfaces: one per texture for tops (texture i), then one wall surface (texture 0)
                w.I32(Textures.Count + 1);
                for (int i = 0; i <= Textures.Count; i++)
                {
                    w.F(0).F(1).F(0).F(1).F(0).F(0).F(1).F(1);
                    int c = i < Textures.Count && BlackTint.Contains(i) ? 0 : 255;
                    w.U16(i < Textures.Count ? i : 0).U16(0).U8(c).U8(c).U8(c).U8(255);
                }
                for (int i = 0; i < Width * Height; i++)
                {
                    for (int k = 0; k < 4; k++) w.F(H[i * 4 + k]);
                    w.I32(Top[i] < 0 ? -1 : TopTexture[i]); w.I32(Front[i] < 0 ? -1 : Textures.Count); w.I32(Right[i] < 0 ? -1 : Textures.Count);
                }
                if (Water) w.F(WaterLevel).I32(WaterType).F(WaveHeight).F(2f).F(50f).I32(3).I32(1).I32(1);
                return w.Bytes();
            }
        }

        // ------------------------------------------------------------------ RSW 1.9
        public sealed class Rsw
        {
            public string Gnd = "synth.gnd", Gat = "synth.gat";
            public float WaterLevel = 1000f; // y-down: far below the ground (no water from the RSW)
            public readonly List<(string file, Vector3 pos, Vector3 rot, Vector3 scale)> Models = new List<(string, Vector3, Vector3, Vector3)>();
            public readonly List<(Vector3 pos, float range)> Lights = new List<(Vector3, float)>();
            public readonly List<(string file, Vector3 pos, float range)> Sounds = new List<(string, Vector3, float)>();
            public readonly List<(int id, Vector3 pos)> Effects = new List<(int, Vector3)>();
            public byte[] Bytes()
            {
                var w = new W().Sig("GRSW").U8(1).U8(9).Str("", 40).Str(Gnd, 40).Str(Gat, 40).Str("", 40);
                w.F(WaterLevel).I32(0).F(1f).F(2f).F(50f).I32(3);
                w.I32(45).I32(45).F(1).F(1).F(1).F(0.3f).F(0.3f).F(0.3f).F(1f);
                w.I32(-500).I32(500).I32(-500).I32(500);
                w.I32(Models.Count + Lights.Count + Sounds.Count + Effects.Count);
                int n = 0;
                foreach (var m in Models) { w.I32(1).Str("model" + n++, 40).I32(0).F(1f).I32(0).Str(m.file, 80).Str("", 80).V3(m.pos).V3(m.rot).V3(m.scale); }
                foreach (var l in Lights) { w.I32(2).Str("light" + n++, 80).V3(l.pos).F(1).F(0.8f).F(0.6f).F(l.range); }
                foreach (var s in Sounds) { w.I32(3).Str("sound" + n++, 80).Str(s.file, 80).V3(s.pos).F(1f).I32(10).I32(10).F(s.range); }
                foreach (var e in Effects) { w.I32(4).Str("effect" + n++, 80).V3(e.pos).I32(e.id).F(0).F(0).F(0).F(0).F(0); }
                return w.Bytes();
            }
            /// <summary>The RSW position (RO units) that puts a model's origin at Loki map point (x, y, z) on a map of gnd tiles (MapLoader.cs:144).</summary>
            public static Vector3 RoAt(float x, float y, float z, int gndW, int gndH) => new Vector3((x - gndW) * 5f, -y * 5f, (z - gndH) * 5f);
        }

        // ------------------------------------------------------------------ RSM 1.4: one node, a box (Loki cells) with its lowest point at 0
        /// <summary>A box of size (sx, sy, sz) cells; twoSided: every face drawn from both sides.</summary>
        public static byte[] Box(float sx, float sy, float sz, bool twoSided = true, string texture = "synth\\wood.bmp", bool bottom = true)
        {
            // RO model units, y-down: the box spans x [-sx/2, sx/2] * 5, y [-sy, 0] * 5, z [-sz/2, sz/2] * 5
            float hx = sx * 2.5f, hz = sz * 2.5f, top = -sy * 5f;
            var v = new[]
            {
                new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz), new Vector3(-hx, 0, hz),
                new Vector3(-hx, top, -hz), new Vector3(hx, top, -hz), new Vector3(hx, top, hz), new Vector3(-hx, top, hz),
            };
            int[][] quads = { new[] { 0, 1, 2, 3 }, new[] { 4, 7, 6, 5 }, new[] { 0, 4, 5, 1 }, new[] { 1, 5, 6, 2 }, new[] { 2, 6, 7, 3 }, new[] { 3, 7, 4, 0 } };
            var faces = new List<int[]>();
            for (int qi = bottom ? 0 : 1; qi < quads.Length; qi++) { var q = quads[qi]; faces.Add(new[] { q[0], q[1], q[2] }); faces.Add(new[] { q[0], q[2], q[3] }); }
            return Rsm(v, faces, twoSided, texture);
        }

        /// <summary>One single-sided quad (two triangles) with the given corners (RO model units, y-down), in that winding.</summary>
        public static byte[] Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool twoSided, string texture = "synth\\wood.bmp", Vector2[] uv = null)
            => Rsm(new[] { a, b, c, d }, new List<int[]> { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } }, twoSided, texture, uv);

        /// <param name="uv">per vertex texture coordinates as the file stores them (v runs down the picture); null: one (0, 0) for all</param>
        public static byte[] Rsm(Vector3[] v, List<int[]> faces, bool twoSided, string texture, Vector2[] uv = null)
        {
            var w = new W().Sig("GRSM").U8(1).U8(4).I32(0).I32(1).U8(255);
            for (int i = 0; i < 16; i++) w.U8(0);
            w.I32(1).Str(texture, 40).Str("root", 40);
            w.I32(1); // one node
            w.Str("root", 40).Str("", 40).I32(1).I32(0);
            float[] id = { 1, 0, 0, 0, 1, 0, 0, 0, 1 }; foreach (var f in id) w.F(f);
            w.V3(Vector3.zero).V3(Vector3.zero).F(0f).V3(new Vector3(0, 1, 0)).V3(Vector3.one);
            w.I32(v.Length); foreach (var p in v) w.V3(p);
            if (uv == null) w.I32(1).U32(0xFFFFFFFF).F(0).F(0);
            else { w.I32(uv.Length); foreach (var t in uv) w.U32(0xFFFFFFFF).F(t.x).F(t.y); }
            w.I32(faces.Count);
            foreach (var f in faces)
            {
                int t0 = uv == null ? 0 : f[0], t1 = uv == null ? 0 : f[1], t2 = uv == null ? 0 : f[2];
                w.U16(f[0]).U16(f[1]).U16(f[2]).U16(t0).U16(t1).U16(t2).U16(0).U16(0).I32(twoSided ? 1 : 0).I32(0);
            }
            w.I32(0); // rotation keys
            w.I32(0); // (< 1.6) model position keys
            return w.Bytes();
        }

        /// <summary>A 2 x 2 24-bit BMP: its top row one colour, its bottom row another (BMP rows are stored bottom-up).</summary>
        public static byte[] Bmp2(byte[] top, byte[] bottom)
        {
            var w = new W(); int rowBytes = 8, size = 54 + rowBytes * 2;
            w.U8('B').U8('M').I32(size).I32(0).I32(54).I32(40).I32(2).I32(2).U16(1).U16(24).I32(0).I32(rowBytes * 2).I32(2835).I32(2835).I32(0).I32(0);
            foreach (var c in new[] { bottom, top }) { for (int x = 0; x < 2; x++) w.U8(c[2]).U8(c[1]).U8(c[0]); w.U8(0).U8(0); }
            return w.Bytes();
        }

        /// <summary>A 2 x 2 24-bit BMP of one colour.</summary>
        public static byte[] Bmp(byte r, byte g, byte b)
        {
            var w = new W(); int rowBytes = 8, size = 54 + rowBytes * 2;
            w.U8('B').U8('M').I32(size).I32(0).I32(54).I32(40).I32(2).I32(2).U16(1).U16(24).I32(0).I32(rowBytes * 2).I32(2835).I32(2835).I32(0).I32(0);
            for (int y = 0; y < 2; y++) { for (int x = 0; x < 2; x++) w.U8(b).U8(g).U8(r); w.U8(0).U8(0); }
            return w.Bytes();
        }
    }
}
