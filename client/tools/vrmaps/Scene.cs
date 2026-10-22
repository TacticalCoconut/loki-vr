using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Loki.Data;
using Loki.Formats;
using Loki.IO;
using Loki.World;
using UnityEngine;

namespace Loki.VrMaps
{
    public enum TriKind : byte { Top = 0, WallFront = 1, WallRight = 2, Model = 3, Water = 4 }

    /// <summary>
    /// One map AS LOKI DRAWS IT, in map space (Loki units: 1 = a GAT cell, y up; cell (x, y) spans x..x+1, z = y..y+1), rebuilt with
    /// the client's own code: the readers (Formats/*), the ground mesh (World/GroundMeshBuilder.Build with the black void filler left
    /// out, as World/MapLoader.cs:115-136), every RSW model instance (World/RsmMeshBuilder.Build at t = 0, World/RoAssets.cs:86-98,
    /// placed by RsmMeshBuilder.InstanceMatrix = ApplyInstanceTransform, MapLoader.cs:143-180; all-black textures not drawn,
    /// MapLoader.cs:162-166), the water (MapLoader.cs:195-221), the lights / sounds / effects (WorldLook.cs:56-61, LokiAudio.cs:536-541),
    /// the GAT as the server sees it (GatFile.Check), and the indoor rule and rooms (Data/RoomTools.cs).
    /// Every triangle keeps the index order the builder gave it: its FRONT (drawn) side is Cross(v1 - v0, v2 - v0) (Unity's clockwise
    /// front face), times the sign of the instance matrix's determinant (Unity inverts culling for a negatively scaled object).
    /// Ground and models cull back faces (Shaders/LokiGround.shader:18, LokiModel.shader:8/18); water does not (LokiWater.shader:24).
    /// </summary>
    public sealed class Scene
    {
        public string Map;
        public RswFile Rsw; public GndFile Gnd; public GatFile Gat;
        public readonly List<string> Notes = new List<string>();

        // ---- triangles
        public int Count;
        public float[] P;        // 9 per triangle
        public float[] N;        // 3 per triangle: the unit front normal (0 for a degenerate triangle)
        public TriKind[] Kind;
        public int[] Src;        // Top / walls / water: the GND tile (tx + ty * gnd.Width); Model: the instance index
        public bool[] TwoSided;  // drawn from both sides (water only: ground and model triangles cull their backs)
        public float[] UV;       // 6 per triangle: the mesh's texture coordinates (Unity's: v = 1 - the file's v)
        public int[] MatOf;      // the triangle's material (-1: water, no texture test)
        /// <summary>Of 7 points of the triangle (its corners, edge middles, centre), how many fall on texels the shader keeps (alpha at
        /// or above its cut-off: LokiModel.shader:80 / LokiGround.shader:86). 7 = opaque; under 4 = mostly cut out (leaves, grilles).</summary>
        public byte[] Opaque7;
        /// <summary>Wave 27 (wmodel): per triangle, for a model's: the RSM node (file order) and face it comes from (-1 for the ground and
        /// water), and its side: <see cref="SideSingle"/> a face the file draws from one side, <see cref="SideTwoFront"/> /
        /// <see cref="SideTwoBack"/> the two copies of a face the file marks two-sided (RsmMeshBuilder.cs, the TwoSided path),
        /// <see cref="SideAdded"/> a back copy Architect's fix adds (kind M, World/MapFix.cs).</summary>
        public int[] Node, Face; public byte[] Side;
        /// <summary>Wave 27 (wmodel): per model triangle, how the lighting normal the builder gives its vertices (carried by the instance)
        /// sits against its drawn side: 1 agrees, 2 points away (the face is lit as if seen from its other side: a smoothing group's
        /// average), 3 grazing; 0 for the ground and water.</summary>
        public byte[] Lit;
        public const byte SideSingle = 0, SideTwoFront = 1, SideTwoBack = 2, SideAdded = 3;

        /// <summary>A texture as the shader cuts it: the decoded texels' alpha (IO/ImageDecoders.TryDecode, magenta keyed, rows top-down),
        /// and the shader's cut-off (models 0.5: LokiModel.shader:9; ground 0.1: LokiGround.shader:8). Alpha null: opaque (a texture
        /// missing from the data is drawn as the opaque "missing" picture, World/RoAssets.cs:63-67).</summary>
        public sealed class Mat { public string Texture; public int W, H; public byte[] Alpha; public byte Cutoff; public float CutShare; }
        public readonly List<Mat> Mats = new List<Mat>();
        private readonly Dictionary<string, int> _matIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        private IGameFiles _files;

        private int MatFor(string texture, float cutoff)
        {
            string key = RoPath.Normalize(texture ?? "") + "|" + cutoff.ToString(CultureInfo.InvariantCulture);
            if (_matIndex.TryGetValue(key, out int i)) return i;
            var m = new Mat { Texture = texture, Cutoff = (byte)Mathf.Clamp(Mathf.RoundToInt(cutoff * 255f), 0, 255) };
            if (!string.IsNullOrEmpty(texture))
            {
                var bytes = _files.Read("data\\texture\\" + RoPath.Normalize(texture));
                if (bytes != null)
                {
                    try
                    {
                        if (ImageDecoders.TryDecode(RoPath.Normalize(texture), bytes, out var img) && img.Pixels != null && img.Width > 0 && img.Height > 0)
                        {
                            m.W = img.Width; m.H = img.Height; m.Alpha = new byte[img.Pixels.Length]; int cut = 0;
                            for (int k = 0; k < m.Alpha.Length; k++) { m.Alpha[k] = img.Pixels[k].a; if (img.Pixels[k].a < m.Cutoff) cut++; }
                            m.CutShare = cut / (float)m.Alpha.Length;
                            if (cut == 0) m.Alpha = null; // nothing cut: opaque
                        }
                    }
                    catch (Exception) { m.Alpha = null; }
                }
            }
            i = Mats.Count; Mats.Add(m); _matIndex[key] = i;
            return i;
        }

        /// <summary>Whether the shader keeps the texel at a point of the triangle (barycentric weights of corners 1 and 2; corner 0 gets
        /// the rest). Nearest texel, wrapping (TextureWrapMode.Repeat, IO/ImageDecoders.cs ToTexture).</summary>
        public bool OpaqueAt(int t, float b1, float b2)
        {
            int mi = MatOf[t]; if (mi < 0) return true;
            var m = Mats[mi]; if (m.Alpha == null) return true;
            float b0 = 1f - b1 - b2; int o = t * 6;
            float u = UV[o] * b0 + UV[o + 2] * b1 + UV[o + 4] * b2, v = UV[o + 1] * b0 + UV[o + 3] * b1 + UV[o + 5] * b2;
            float fv = 1f - v; // the file's v: rows run down the picture (RsmMeshBuilder.cs UV, GroundMeshBuilder.cs UV)
            int col = (int)Math.Floor(u * m.W), row = (int)Math.Floor(fv * m.H);
            col %= m.W; if (col < 0) col += m.W; row %= m.H; if (row < 0) row += m.H;
            return m.Alpha[row * m.W + col] >= m.Cutoff;
        }

        // ---- models
        public sealed class Instance
        {
            /// <summary>Wave 27 (wmodel): the RSM file's size and FNV-1a 64 (World/MapFix.cs), for the fix's M lines.</summary>
            public long RsmBytes; public string RsmHash = ""; public RsmFile Rsm;
            /// <summary>The model's whole placed box (every face's corners: RsmMeshBuilder.StaticBounds through the placement), as the client
            /// gives it to the shader with the back copies (World/MapLoader.cs AddBackFaces: the mesh's bounds' eight corners).</summary>
            public Vector3 BoxMin, BoxMax;
            public int Index; public string File, Name; public Vector3 RoPos, RoRot, RoScale;
            public bool Missing, Animated, TextureAnimated, Mirrored; public float Alpha = 1f;
            public int FirstTri, Tris, NoDrawTris; public Vector3 Min, Max; // placed box (map space) of the drawn triangles
        }
        public readonly List<Instance> Instances = new List<Instance>();
        public int ModelFiles, ModelFilesMissing;
        /// <summary>Model triangles whose drawn side (this tool's winding rule) agrees / disagrees with the normal the client's builder
        /// gives their vertices for lighting (RsmMeshBuilder.cs:129-136), carried by the instance as Unity carries normals (inverse
        /// transpose). A check of the winding rule on the real data: the builder lights the side it means to be seen.</summary>
        public int NormalsAgree, NormalsDisagree;

        // ---- ground facts
        public int GroundCulledSurfaces, GroundCulledTinted; public readonly List<string> BlackTextures = new List<string>();
        /// <summary>Per GND tile: its Top is drawn (a surface, not black).</summary>
        public bool[] TopDrawn;

        // ---- the GAT
        public int W, H;
        /// <summary>Passable as the server's path search sees it (GatFile.Check: a walkable type, not the last row / column), and a
        /// place players can be: see <see cref="EdgeBand"/>.</summary>
        public bool[] Walk;
        /// <summary>warch2 (the all-maps pass): walkable cells left out of <see cref="Walk"/> because their group (8-connected) lies
        /// entirely within <see cref="EdgeBand"/> cells of the map's edge while another group reaches further in: no player gets there.
        /// rAthena never puts a unit within 5 cells of the edge (map.cpp map_search_freecell: "Cells outside the map or within 4-5 cells
        /// of the map edge are considered invalid officially"; a map-wide search keeps map_edge_size = 15 off), and nothing walkable
        /// joins them to the play area (iz_dun00, pay_dun03: a walkable ring on the first row and column; its walls were turned toward
        /// it, closing the map's outside for the Table, against the outer-wall rule's spirit).</summary>
        public int EdgeOnlyCells;
        public const int EdgeBand = 5;
        /// <summary>The floor under a unit at the cell's centre (Loki y): the cell's mean height (Sprites/Unit.cs:626; Smooth's
        /// interpolation gives the same at the centre).</summary>
        public float[] Floor;

        // ---- water (MapLoader.cs:195-221)
        public bool HasWater; public string WaterSource; public float WaterLevelRo, WaveHeightRo; public int WaterType;
        public float WaterY => -WaterLevelRo * RoSpace.Scale;
        public bool[] WaterTile;
        public int WaterTiles;

        // ---- objects
        public sealed class Light { public Vector3 Pos; public float Range; public Color Color; public string Name; }
        public sealed class Sound { public string File, Name; public Vector3 Pos; public float Range, Volume, Cycle; }
        public sealed class Effect { public int Id; public Vector3 Pos; public string Name; }
        public readonly List<Light> Lights = new List<Light>();
        public readonly List<Sound> Sounds = new List<Sound>();
        public readonly List<Effect> Effects = new List<Effect>();

        // ---- fog, indoor, rooms
        public bool HasFogEntry; public float FogNear, FogFar;
        public string Indoor = "no";
        public RoomTools.MapRooms Rooms;
        public string RoomsCheck = "";

        /// <summary>The sky Loki draws behind this map (World/SkyController.cs Apply, :89-118, and its lists :41-42; ApplyMapSky): "indoor"
        /// (a dim room tone), "own" (the map's own sky, mapsky.tsv), "night", "dungeon" (darkness tinted by the fog), or "day" (a bright
        /// sky: a hole in the ground shows it). Needs the indoor rule (<see cref="Options.Rooms"/>).</summary>
        public string SkyTheme
        {
            get
            {
                if (Indoor != "no") return "indoor";
                if (OwnSky != null) return "own";
                foreach (var k in SkyNight) if (Map.Contains(k)) return "night";
                if (Map.Contains("dun")) return "dungeon";
                foreach (var k in SkyCaves) if (Map.Contains(k)) return "dungeon";
                return "day";
            }
        }
        private static readonly string[] SkyCaves = { "anthell", "prt_sewb", "moc_pryd", "orcsdun", "iz_dun", "pay_dun", "gef_dun", "mjo_dun", "treasure", "tur_dun", "alde_dun", "c_tower", "in_sphinx", "beach_dun", "ama_dun", "gon_dun", "um_dun", "ein_dun", "lhz_dun", "juperos", "thor_v", "ice_dun", "kh_dun", "mag_dun", "odin_tem", "ra_san", "xmas_dun", "lou_dun", "dew_dun", "mal_dun", "ecl_tdun", "bra_dun", "dic_dun", "iz_ac" };
        private static readonly string[] SkyNight = { "niflheim", "nif_", "gl_", "abyss", "thana", "1@", "2@", "gld_dun" };

        public Vector3 Centre => new Vector3(Gnd.Width, 0, Gnd.Height);
        public float GatHeightY(int x, int y) => -Gat.Get(x, y).AverageHeight * RoSpace.Scale;
        /// <summary>The GAT under a point (cells), its four corners interpolated (Sprites/Unit.Smooth.cs:326-334, GroundItems.cs:91-99).</summary>
        public float GatY(float px, float py)
        {
            int cx = Mathf.FloorToInt(px), cy = Mathf.FloorToInt(py);
            if (!Gat.InBounds(cx, cy)) return 0f;
            var c = Gat.Get(cx, cy);
            float fx = px - cx, fy = py - cy;
            float x1 = c.H0 + (c.H1 - c.H0) * fx, x2 = c.H2 + (c.H3 - c.H2) * fx;
            return -(x1 + (x2 - x1) * fy) * RoSpace.Scale;
        }
        public bool InMap(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
        public bool Walkable(int x, int y) => InMap(x, y) && Walk[x + y * W];

        public Vector3 V(int t, int k) => new Vector3(P[t * 9 + k * 3], P[t * 9 + k * 3 + 1], P[t * 9 + k * 3 + 2]);
        public Vector3 Normal(int t) => new Vector3(N[t * 3], N[t * 3 + 1], N[t * 3 + 2]);
        public string Owner(int t)
        {
            switch (Kind[t])
            {
                case TriKind.Model: { var i = Instances[Src[t]]; return $"model #{i.Index} {Names.Display(i.File)}" + (string.IsNullOrEmpty(i.Name) ? "" : $" ({Names.Display(i.Name)})"); }
                case TriKind.Top: return $"ground top, tile {Src[t] % Gnd.Width},{Src[t] / Gnd.Width}";
                case TriKind.WallFront: return $"ground wall (north edge of tile {Src[t] % Gnd.Width},{Src[t] / Gnd.Width})";
                case TriKind.WallRight: return $"ground wall (east edge of tile {Src[t] % Gnd.Width},{Src[t] / Gnd.Width})";
                default: return "water";
            }
        }

        // ------------------------------------------------------------------ building
        private readonly List<float> _p = new List<float>(), _n = new List<float>(); private readonly List<TriKind> _k = new List<TriKind>();
        private readonly List<int> _s = new List<int>(); private readonly List<bool> _two = new List<bool>();

        private readonly List<float> _uv = new List<float>(); private readonly List<int> _mat = new List<int>();
        private readonly List<int> _node = new List<int>(), _face = new List<int>(); private readonly List<byte> _side = new List<byte>(), _lit = new List<byte>();
        private void AddTri(Vector3 a, Vector3 b, Vector3 c, TriKind kind, int src, bool twoSided, float sign, Vector2 ua = default, Vector2 ub = default, Vector2 uc = default, int mat = -1, int node = -1, int face = -1, byte side = 0)
        {
            _node.Add(node); _face.Add(face); _side.Add(side); _lit.Add(0);
            _uv.Add(ua.x); _uv.Add(ua.y); _uv.Add(ub.x); _uv.Add(ub.y); _uv.Add(uc.x); _uv.Add(uc.y); _mat.Add(mat);
            _p.Add(a.x); _p.Add(a.y); _p.Add(a.z); _p.Add(b.x); _p.Add(b.y); _p.Add(b.z); _p.Add(c.x); _p.Add(c.y); _p.Add(c.z);
            var n = Vector3.Cross(b - a, c - a) * sign; float m = n.magnitude;
            if (m > 1e-12f) n /= m; else n = Vector3.zero;
            _n.Add(n.x); _n.Add(n.y); _n.Add(n.z); _k.Add(kind); _s.Add(src); _two.Add(twoSided);
        }

        /// <summary>RoAssets.IsBlackTexture (World/RoAssets.cs:35-70): data\texture\&lt;name&gt; decoded by IO/ImageDecoders.TryDecode
        /// (magenta keyed) and every opaque pixel black. Cached per texture.</summary>
        private static bool IsBlack(IGameFiles f, string name, Dictionary<string, bool> cache)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string key = RoPath.Normalize(name);
            if (cache.TryGetValue(key, out bool b)) return b;
            b = false;
            var bytes = f.Read("data\\texture\\" + key);
            if (bytes != null)
            {
                try { if (ImageDecoders.TryDecode(key, bytes, out var img)) b = ImageDecoders.IsAllBlack(img); }
                catch (Exception) { b = false; }
            }
            cache[key] = b;
            return b;
        }

        public sealed class Options
        {
            /// <summary>The player's data folder (navigation.tsv for the indoor rule, rooms.tsv to check the re-run rooms); null: none.</summary>
            public string UserData;
            public bool Rooms = true;
            /// <summary>Architect's fix to draw the ground with (World/MapFix.cs, as World/MapLoader.cs applies it), or null: the ground as
            /// the game builds it.</summary>
            public MapFix Fix;
            /// <summary>mapsky.tsv's text to read instead of the player's file (the self-test).</summary>
            public string OwnSkyTable;
        }
        /// <summary>The fix the ground was built with (null: none), and what the builder reports it changed.</summary>
        public MapFix Fix; public int FixBoth, FixFlipped, FixTops, FixFills, FixSkirts;
        /// <summary>Wave 27 (wmodel): the fix's model faces as built (World/MapLoader.cs AddBackFaces' rules): back copies added, instances
        /// fixed, instances not fixed (another RSM, a moving model, a count that differs), and whether the RSW matched.</summary>
        public int FixModelFaces, FixModelInstances, FixModelSkipped; public bool FixRswMatches;
        /// <summary>The RSW's size and FNV-1a 64 (World/MapFix.cs: the R line).</summary>
        public long RswBytes; public string RswHash = "";
        /// <summary>The map's own sky (the player's mapsky.tsv: World/MapSky.cs): RO cleared the screen to this colour and drew its clouds
        /// under the map, so what is not drawn shows the map's designed sky, not a hole into black. Null: none known.</summary>
        public string OwnSky;

        public static Scene Build(IGameFiles f, string mapName, Options opt = null)
        {
            opt = opt ?? new Options();
            var s = new Scene { Map = mapName.ToLowerInvariant(), _files = f };
            // MapLoader.cs:64-66: the same three paths
            var rswB = f.Read("data\\" + mapName + ".rsw") ?? throw new System.IO.FileNotFoundException("Not in client data: data\\" + mapName + ".rsw");
            s.Rsw = RswFile.Parse(rswB); s.RswBytes = rswB.LongLength; s.RswHash = MapFix.Hash(rswB);
            s.FixRswMatches = opt.Fix != null && opt.Fix.Models.Count > 0 && opt.Fix.MatchesRsw(rswB);
            s.Gnd = GndFile.Parse(f.Read("data\\" + s.Rsw.GndFile) ?? throw new System.IO.FileNotFoundException("Not in client data: data\\" + s.Rsw.GndFile));
            s.Gat = GatFile.Parse(f.Read("data\\" + s.Rsw.GatFile) ?? throw new System.IO.FileNotFoundException("Not in client data: data\\" + s.Rsw.GatFile));
            var blackCache = new Dictionary<string, bool>(StringComparer.Ordinal);
            s.Fix = opt.Fix;
            s.ReadOwnSky(opt.UserData, opt.OwnSkyTable);
            s.BuildGround(f, blackCache);
            s.BuildModels(f, blackCache);
            s.BuildWater();
            s.BuildGat();
            s.BuildObjects();
            s.ReadFog(f);
            if (opt.Rooms) s.BuildRooms(f, opt.UserData);
            s.P = _arr(s._p); s.N = _arr(s._n); s.Kind = s._k.ToArray(); s.Src = s._s.ToArray(); s.TwoSided = s._two.ToArray();
            s.Node = s._node.ToArray(); s.Face = s._face.ToArray(); s.Side = s._side.ToArray(); s.Lit = s._lit.ToArray();
            s.UV = _arr(s._uv); s.MatOf = s._mat.ToArray();
            s.Count = s.Kind.Length;
            s.Opaque7 = new byte[s.Count];
            for (int t = 0; t < s.Count; t++)
            {
                int n = 0;
                foreach (var (b1, b2) in new[] { (0f, 0f), (1f, 0f), (0f, 1f), (0.5f, 0f), (0f, 0.5f), (0.5f, 0.5f), (1f / 3f, 1f / 3f) }) if (s.OpaqueAt(t, b1, b2)) n++;
                s.Opaque7[t] = (byte)n;
            }
            s._uv.Clear(); s._mat.Clear();
            s._p.Clear(); s._n.Clear(); s._k.Clear(); s._s.Clear(); s._two.Clear(); s._node.Clear(); s._face.Clear(); s._side.Clear(); s._lit.Clear();
            return s;
        }
        private static float[] _arr(List<float> l) => l.ToArray();

        private void BuildGround(IGameFiles f, Dictionary<string, bool> blackCache)
        {
            var gnd = Gnd;
            var blackTex = new bool[gnd.Textures.Count];
            for (int i = 0; i < blackTex.Length; i++) { blackTex[i] = IsBlack(f, gnd.Textures[i], blackCache); if (blackTex[i]) BlackTextures.Add(gnd.Textures[i]); }
            var g = GroundMeshBuilder.Build(gnd, true, ti => ti >= 0 && ti < blackTex.Length && blackTex[ti], Fix); // MapLoader.cs:119 (+ the fix, as MapLoader applies it)
            GroundCulledSurfaces = g.Culled; GroundCulledTinted = g.CulledTinted;
            FixBoth = g.FixBoth; FixFlipped = g.FixFlipped; FixTops = g.FixTops; FixFills = g.FixFills; FixSkirts = g.FixSkirts;
            TopDrawn = new bool[gnd.Width * gnd.Height];
            AddGround(g.Mesh, gnd, sm => sm < gnd.Textures.Count ? gnd.Textures[sm] : "");
            if (g.VoidMesh != null) AddGround(g.VoidMesh, gnd, sm => ""); // the fix's black fills: opaque, no texture
        }

        private void AddGround(Mesh mesh, GndFile gnd, Func<int, string> texture)
        {
            var v = mesh.Vertices; var nn = mesh.Normals; var uv = mesh.Uv0;
            for (int sm = 0; sm < mesh.subMeshCount; sm++)
            {
                var t = mesh.Triangles(sm);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]], n0 = nn[t[i]];
                    float cx = (a.x + b.x + c.x) / 3f, cz = (a.z + b.z + c.z) / 3f;
                    TriKind kind; int tx, ty;
                    // GroundMeshBuilder.cs: a wall's normal is exactly forward or right (back or left when Architect's fix turns or doubles
                    // it; a fill's skirt likewise); a top's is its smoothed slope (a fill's: straight up)
                    if (n0.x == 0f && n0.y == 0f && Math.Abs(n0.z) == 1f) { kind = TriKind.WallFront; tx = Mathf.FloorToInt(cx / 2f); ty = Mathf.RoundToInt(a.z / 2f) - 1; }
                    else if (Math.Abs(n0.x) == 1f && n0.y == 0f && n0.z == 0f) { kind = TriKind.WallRight; tx = Mathf.RoundToInt(a.x / 2f) - 1; ty = Mathf.FloorToInt(cz / 2f); }
                    else { kind = TriKind.Top; tx = Mathf.FloorToInt(cx / 2f); ty = Mathf.FloorToInt(cz / 2f); }
                    tx = Mathf.Clamp(tx, 0, gnd.Width - 1); ty = Mathf.Clamp(ty, 0, gnd.Height - 1);
                    if (kind == TriKind.Top) TopDrawn[tx + ty * gnd.Width] = true;
                    AddTri(a, b, c, kind, tx + ty * gnd.Width, false, 1f, uv[t[i]], uv[t[i + 1]], uv[t[i + 2]], MatFor(texture(sm), 0.1f));
                }
            }
        }

        /// <summary>World/MapSky.cs: the player's mapsky.tsv "S" rows (map, BG_Color, Star_Effect, BG_Fog, Old_Cloud_Effect).</summary>
        private void ReadOwnSky(string userData, string table)
        {
            IEnumerable<string> lines;
            if (table != null) lines = table.Split('\n');
            else
            {
                if (string.IsNullOrEmpty(userData)) return;
                string p = System.IO.Path.Combine(userData, "mapsky.tsv");
                if (!System.IO.File.Exists(p)) return;
                lines = System.IO.File.ReadLines(p, Encoding.UTF8);
            }
            foreach (var line in lines)
            {
                var c = line.Split('\t');
                if (c.Length >= 6 && c[0] == "S" && string.Equals(c[1], Map, StringComparison.OrdinalIgnoreCase) && c[2] != "-")
                { OwnSky = $"background {c[2]}, old cloud type {c[5]} (mapsky.tsv)"; return; }
            }
        }

        private void BuildModels(IGameFiles f, Dictionary<string, bool> blackCache)
        {
            var meshes = new Dictionary<string, (Mesh mesh, RsmFile rsm, List<(int node, int face, bool back)> faces, long bytes, string hash)>(StringComparer.Ordinal);
            var centre = Centre; // MapLoader.cs:144
            for (int i = 0; i < Rsw.Models.Count; i++)
            {
                var mo = Rsw.Models[i];
                var inst = new Instance { Index = i, File = mo.FileName, Name = mo.Name, RoPos = mo.Position, RoRot = mo.Rotation, RoScale = mo.Scale, FirstTri = _k.Count };
                Instances.Add(inst);
                if (Mathf.Abs(mo.Scale.x) < 1e-4f || Mathf.Abs(mo.Scale.y) < 1e-4f || Mathf.Abs(mo.Scale.z) < 1e-4f) continue;
                string key = RoPath.Normalize(mo.FileName ?? "");
                if (!meshes.TryGetValue(key, out var entry))
                {
                    // RoAssets.Model / ModelMesh (World/RoAssets.cs:72-98): a model that cannot be read or built is left out
                    RsmFile rsm = null; Mesh mesh = null; var faces = new List<(int node, int face, bool back)>();
                    var bytes = f.Read("data\\model\\" + key);
                    if (bytes != null) { try { rsm = RsmFile.Parse(bytes); } catch (Exception e) { Notes.Add("model " + key + ": " + e.Message); } }
                    // wave 27 (wmodel): the same build, with the face each triangle comes from (RsmMeshBuilder.Build's record)
                    if (rsm != null) { try { mesh = RsmMeshBuilder.Build(rsm, 0f, faces, null); } catch (Exception e) { Notes.Add("model " + key + ": " + e.Message); rsm = null; } }
                    entry = (mesh, rsm, faces, bytes?.LongLength ?? 0, bytes == null ? "" : MapFix.Hash(bytes)); meshes[key] = entry;
                    ModelFiles++; if (mesh == null) ModelFilesMissing++;
                }
                if (entry.mesh == null) { inst.Missing = true; continue; }
                var r = entry.rsm; var m = entry.mesh;
                inst.Animated = RsmMeshBuilder.IsAnimated(r); inst.TextureAnimated = RsmMeshBuilder.HasTextureAnims(r); inst.Alpha = r.Alpha;
                inst.Rsm = r; inst.RsmBytes = entry.bytes; inst.RsmHash = entry.hash;
                {
                    var M0 = RsmMeshBuilder.InstanceMatrix(mo.Position, mo.Rotation, mo.Scale, centre);
                    Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                    if (RsmMeshBuilder.StaticBounds(r, out var bmin, out var bmax))
                        for (int c = 0; c < 8; c++)
                        {
                            var p = M0.MultiplyPoint3x4(new Vector3((c & 1) == 0 ? bmin.x : bmax.x, (c & 2) == 0 ? bmin.y : bmax.y, (c & 4) == 0 ? bmin.z : bmax.z));
                            lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
                        }
                    inst.BoxMin = lo; inst.BoxMax = hi;
                }
                var M = RsmMeshBuilder.InstanceMatrix(mo.Position, mo.Rotation, mo.Scale, centre);
                float det = M.determinant; inst.Mirrored = det < 0; float sign = det < 0 ? -1f : 1f;
                var wv = new Vector3[m.Vertices.Length];
                for (int k = 0; k < wv.Length; k++) wv[k] = M.MultiplyPoint3x4(m.Vertices[k]);
                // normals as Unity carries them: the inverse transpose of the 3 x 3 (= cofactor / det)
                Vector3 NormalOut(Vector3 n)
                {
                    float a00 = M.m00, a01 = M.m01, a02 = M.m02, a10 = M.m10, a11 = M.m11, a12 = M.m12, a20 = M.m20, a21 = M.m21, a22 = M.m22;
                    var c0 = new Vector3(a11 * a22 - a12 * a21, a12 * a20 - a10 * a22, a10 * a21 - a11 * a20);
                    var c1 = new Vector3(a02 * a21 - a01 * a22, a00 * a22 - a02 * a20, a01 * a20 - a00 * a21);
                    var c2 = new Vector3(a01 * a12 - a02 * a11, a02 * a10 - a00 * a12, a00 * a11 - a01 * a10);
                    var o = new Vector3(Vector3.Dot(c0, n), Vector3.Dot(c1, n), Vector3.Dot(c2, n)) * sign;
                    return o.normalized;
                }
                var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                for (int sm = 0; sm < m.subMeshCount; sm++)
                {
                    string tn = sm < r.Textures.Count ? r.Textures[sm] : "";
                    bool noDraw = IsBlack(f, tn, blackCache); // MapLoader.cs:160-166
                    var t = m.Triangles(sm);
                    if (noDraw) { inst.NoDrawTris += t.Length / 3; continue; }
                    int mat = MatFor(tn, 0.5f); var muv = m.Uv0;
                    for (int k = 0; k + 2 < t.Length; k += 3)
                    {
                        Vector3 a = wv[t[k]], b = wv[t[k + 1]], c = wv[t[k + 2]];
                        var fo = entry.faces[t[k] / 3]; // every face's copy is three vertices of its own, in order (RsmMeshBuilder.Fill)
                        byte side = fo.back ? SideTwoBack : r.Nodes[fo.node].Faces[fo.face].TwoSided != 0 ? SideTwoFront : SideSingle;
                        AddTri(a, b, c, TriKind.Model, i, false, sign, muv[t[k]], muv[t[k + 1]], muv[t[k + 2]], mat, fo.node, fo.face, side);
                        {
                            // flat: the vertex normal is the face's; smooth: averaged over the smoothing group (RsmMeshBuilder.cs:102-136)
                            int ti = _k.Count - 1; var mine = new Vector3(_n[ti * 3], _n[ti * 3 + 1], _n[ti * 3 + 2]);
                            var nb = m.Normals[t[k]] + m.Normals[t[k + 1]] + m.Normals[t[k + 2]];
                            float dot = Vector3.Dot(mine, NormalOut(nb.normalized)), lim = r.ShadeType == RsmFile.Shading.Smooth ? 0.3f : 0.9f;
                            if (dot > lim) NormalsAgree++; else if (dot < -lim) NormalsDisagree++;
                            _lit[ti] = (byte)(dot > lim ? 1 : dot < -lim ? 2 : 3);
                        }
                        mn = Vector3.Min(mn, Vector3.Min(a, Vector3.Min(b, c))); mx = Vector3.Max(mx, Vector3.Max(a, Vector3.Max(b, c)));
                    }
                }
                // wave 27 (wmodel): Architect's fix kind M, as World/MapLoader.cs AddBackFaces applies it: the back copies the client's
                // builder makes (RsmMeshBuilder.BuildBack's faces), for the RSW and RSM the fix was made for, on a model that does not move
                if (FixRswMatches && Fix.Models.TryGetValue(i, out var mfix))
                {
                    if (inst.Animated || inst.TextureAnimated || mfix.Bytes != inst.RsmBytes || mfix.Hash != inst.RsmHash) FixModelSkipped++;
                    else
                    {
                        var bf = new List<(int node, int face, bool back)>();
                        var bm = RsmMeshBuilder.Build(r, 0f, bf, (nd, fc) => mfix.Has(nd, fc));
                        if (bf.Count != mfix.Count) FixModelSkipped++;
                        else
                        {
                            var bv = new Vector3[bm.Vertices.Length]; for (int k = 0; k < bv.Length; k++) bv[k] = M.MultiplyPoint3x4(bm.Vertices[k]);
                            for (int sm = 0; sm < bm.subMeshCount; sm++)
                            {
                                string tn = sm < r.Textures.Count ? r.Textures[sm] : "";
                                if (IsBlack(f, tn, blackCache)) continue; // the instance's NoDraw material serves its back copy too
                                var t = bm.Triangles(sm); int mat = MatFor(tn, 0.5f); var buv = bm.Uv0;
                                for (int k = 0; k + 2 < t.Length; k += 3)
                                {
                                    var fo = bf[t[k] / 3];
                                    AddTri(bv[t[k]], bv[t[k + 1]], bv[t[k + 2]], TriKind.Model, i, false, sign, buv[t[k]], buv[t[k + 1]], buv[t[k + 2]], mat, fo.node, fo.face, SideAdded);
                                    // the lighting normal the builder gives the back copy (negated) must point to its drawn side (never inside-out)
                                    int ti = _k.Count - 1; var mine = new Vector3(_n[ti * 3], _n[ti * 3 + 1], _n[ti * 3 + 2]);
                                    var nb = bm.Normals[t[k]] + bm.Normals[t[k + 1]] + bm.Normals[t[k + 2]];
                                    float dot = Vector3.Dot(mine, NormalOut(nb.normalized)), lim = r.ShadeType == RsmFile.Shading.Smooth ? 0.3f : 0.9f;
                                    if (dot > lim) AddedNormalsAgree++; else if (dot < -lim) AddedNormalsDisagree++; else AddedNormalsLoose++;
                                    _lit[ti] = (byte)(dot > lim ? 1 : dot < -lim ? 2 : 3);
                                    mn = Vector3.Min(mn, Vector3.Min(bv[t[k]], Vector3.Min(bv[t[k + 1]], bv[t[k + 2]]))); mx = Vector3.Max(mx, Vector3.Max(bv[t[k]], Vector3.Max(bv[t[k + 1]], bv[t[k + 2]])));
                                }
                            }
                            FixModelFaces += bf.Count; FixModelInstances++;
                        }
                    }
                }
                inst.Tris = _k.Count - inst.FirstTri; inst.Min = mn; inst.Max = mx;
            }
        }
        /// <summary>Wave 27 (wmodel): the back copies the fix adds whose lighting normal (the builder's, carried by the instance) agrees /
        /// disagrees with their drawn side, or neither (a smoothing group's average more than ~73 degrees off: Architect's check M2).</summary>
        public int AddedNormalsAgree, AddedNormalsDisagree, AddedNormalsLoose;

        private void BuildWater()
        {
            var rsw = Rsw; var gnd = Gnd;
            float level, waveHeight; int type;
            if (gnd.Water != null) { level = gnd.Water.Level; waveHeight = gnd.Water.WaveHeight; type = gnd.Water.Type; WaterSource = "gnd"; }
            else if (rsw.HasWater) { level = rsw.WaterLevel; waveHeight = rsw.WaveHeight; type = rsw.WaterType; WaterSource = "rsw"; }
            else return;
            WaterLevelRo = level; WaveHeightRo = waveHeight; WaterType = type;
            WaterTile = new bool[gnd.Width * gnd.Height];
            int w = gnd.Width, h = gnd.Height;
            float ly = -level * RoSpace.Scale;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var t = gnd.Tiles[x + y * w];
                    if (t.Top < 0) continue;
                    if (!(t.H0 > level - waveHeight || t.H1 > level - waveHeight || t.H2 > level - waveHeight || t.H3 > level - waveHeight)) continue; // MapLoader.cs:213
                    WaterTile[x + y * w] = true; WaterTiles++;
                    Vector3 p00 = new Vector3(x * 2f, ly, y * 2f), p10 = new Vector3(x * 2f + 2, ly, y * 2f), p01 = new Vector3(x * 2f, ly, y * 2f + 2), p11 = new Vector3(x * 2f + 2, ly, y * 2f + 2);
                    AddTri(p00, p01, p11, TriKind.Water, x + y * w, true, 1f); AddTri(p00, p11, p10, TriKind.Water, x + y * w, true, 1f); // MapLoader.cs:220
                }
            HasWater = WaterTiles > 0;
        }

        private void BuildGat()
        {
            W = Gat.Width; H = Gat.Height;
            Walk = new bool[W * H]; Floor = new float[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Walk[x + y * W] = !Gat.Check(x, y);
                    Floor[x + y * W] = GatHeightY(x, y);
                }
            DropEdgeOnlyGroups();
            if (W != Gnd.Width * 2 || H != Gnd.Height * 2) Notes.Add($"the GAT is {W} x {H} cells, the GND {Gnd.Width} x {Gnd.Height} tiles (= {Gnd.Width * 2} x {Gnd.Height * 2} cells)");
        }

        private void DropEdgeOnlyGroups()
        {
            var group = new int[W * H]; var inner = new List<bool>(); var q = new Queue<int>(); int n = 0;
            for (int i = 0; i < W * H; i++)
            {
                if (!Walk[i] || group[i] != 0) continue;
                n++; bool deep = false; group[i] = n; q.Enqueue(i);
                while (q.Count > 0)
                {
                    int c = q.Dequeue(), cx = c % W, cy = c / W;
                    if (Math.Min(Math.Min(cx, cy), Math.Min(W - 1 - cx, H - 1 - cy)) >= EdgeBand) deep = true;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx, y = cy + dy; if (x < 0 || y < 0 || x >= W || y >= H) continue;
                        int u = x + y * W; if (Walk[u] && group[u] == 0) { group[u] = n; q.Enqueue(u); }
                    }
                }
                inner.Add(deep);
            }
            if (!inner.Contains(true)) return; // a map too small for the band: every group stays
            for (int i = 0; i < W * H; i++) if (Walk[i] && !inner[group[i] - 1]) { Walk[i] = false; EdgeOnlyCells++; }
            if (EdgeOnlyCells > 0) Notes.Add($"{EdgeOnlyCells} walkable cells in groups within {EdgeBand} cells of the map's edge, joined to nothing further in: no player gets there (rAthena map.cpp map_search_freecell), left out of the walkable cells");
        }

        private void BuildObjects()
        {
            var c = Centre;
            foreach (var l in Rsw.Lights) Lights.Add(new Light { Name = l.Name, Pos = RoSpace.ToLoki(l.Position) + c, Range = l.Range * RoSpace.Scale, Color = l.Color }); // WorldLook.cs:57
            foreach (var s in Rsw.Sounds) Sounds.Add(new Sound { File = s.File, Name = s.Name, Pos = RoSpace.ToLoki(s.Position) + c, Range = Mathf.Max(1f, s.Range * RoSpace.Scale), Volume = s.Volume, Cycle = s.Cycle }); // LokiAudio.cs:539-541
            foreach (var e in Rsw.Effects) Effects.Add(new Effect { Id = e.Id, Name = e.Name, Pos = RoSpace.ToLoki(e.Position) + c }); // WorldLook.cs:61
        }

        /// <summary>World/SkyController.cs:51-72 (Init) and :22-27 (FogOf): "map.rsw#near#far#0xAARRGGBB#density#" blocks.</summary>
        private void ReadFog(IGameFiles f)
        {
            var data = f.Read("data\\fogparametertable.txt");
            if (data == null) return;
            var parts = RoReader.Latin1.GetString(data).Split('#');
            for (int i = 0; i + 4 < parts.Length; i++)
            {
                string name = LastLine(parts[i]);
                if (!name.EndsWith(".rsw")) continue;
                string col = parts[i + 3].Trim();
                if (col.StartsWith("0x") && uint.TryParse(col.Substring(2), NumberStyles.HexNumber, null, out _))
                {
                    string key = name.Substring(0, name.Length - 4).ToLowerInvariant();
                    if (key == Map && float.TryParse(parts[i + 1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float near) && float.TryParse(parts[i + 2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float far))
                    { FogNear = near; FogFar = far; HasFogEntry = far > near; }
                }
                i += 4;
            }
        }
        private static string LastLine(string s)
        {
            var lines = s.Replace("\r", "").Split('\n');
            for (int k = lines.Length - 1; k >= 0; k--) { var l = lines[k].Trim(); if (l.Length > 0 && !l.StartsWith("//")) return l.ToLowerInvariant(); }
            return "";
        }

        /// <summary>Data/RoomTools.cs: the indoor rule (:108-131: the client's list, navigation type 5003, copies with the same GAT) and the
        /// rooms (Build :191-202), re-run here exactly as the first-run builder runs them; checked against the player's rooms.tsv when given.</summary>
        private void BuildRooms(IGameFiles f, string userData)
        {
            var listed = RoomTools.ParseIndoorList(f.Read(RoomTools.IndoorList));
            byte[] navi = null; string roomsTsv = null;
            if (!string.IsNullOrEmpty(userData))
            {
                string np = System.IO.Path.Combine(userData, "navigation.tsv"), rp = System.IO.Path.Combine(userData, RoomTools.Table);
                if (System.IO.File.Exists(np)) navi = System.IO.File.ReadAllBytes(np);
                if (System.IO.File.Exists(rp)) roomsTsv = rp;
            }
            var naviSet = RoomTools.ParseNaviIndoor(navi);
            string rule = null;
            if (listed.Contains(Map)) rule = naviSet.Contains(Map) ? "both" : "table";
            else if (naviSet.Contains(Map)) rule = "navi";
            else
            {
                // a copy: the name + two digits or "_" + a letter, whose GAT is byte for byte the indoor map's (RoomTools.cs:117-131)
                string baseName = null;
                if (Map.Length > 2 && char.IsDigit(Map[Map.Length - 1]) && char.IsDigit(Map[Map.Length - 2])) baseName = Map.Substring(0, Map.Length - 2);
                else if (Map.Length > 2 && Map[Map.Length - 2] == '_' && Map[Map.Length - 1] >= 'a' && Map[Map.Length - 1] <= 'z') baseName = Map.Substring(0, Map.Length - 2);
                if (baseName != null && (listed.Contains(baseName) || naviSet.Contains(baseName)))
                {
                    byte[] Gat(string m) { try { var r = f.Read("data\\" + m + ".rsw"); return r == null ? null : f.Read("data\\" + RswFile.Parse(r).GatFile); } catch (Exception) { return null; } }
                    var a = Gat(baseName); var b = Gat(Map);
                    if (a != null && b != null && a.SequenceEqual(b)) rule = "copy:" + baseName;
                }
            }
            if (rule == null) { Indoor = "no"; return; }
            Indoor = rule;
            if (naviSet.Count == 0 && rule != "table") Notes.Add("no navigation.tsv given: the indoor rule saw only the client's list");
            try
            {
                Rooms = RoomTools.Build(new ToolInputs(f), Map, new Dictionary<string, RoomTools.Box?>(StringComparer.Ordinal), new Dictionary<string, bool>(StringComparer.Ordinal));
            }
            catch (Exception e) { Notes.Add("rooms: " + e.Message); Rooms = null; }
            if (Rooms != null && roomsTsv != null) RoomsCheck = CompareRooms(roomsTsv);
        }

        /// <summary>The re-run rooms against the player's own table (World/RoomMask.cs:66-98 reads it the same way).</summary>
        private string CompareRooms(string path)
        {
            string iRow = null, gRow = null;
            foreach (var line in System.IO.File.ReadLines(path, Encoding.UTF8))
            {
                if (line.StartsWith("I\t" + Map + "\t", StringComparison.Ordinal)) iRow = line;
                else if (line.StartsWith("G\t" + Map + "\t", StringComparison.Ordinal)) { gRow = line; break; }
            }
            if (iRow == null) return "not in the player's rooms.tsv";
            var c = iRow.Split('\t'); int rooms = int.Parse(c[4], CultureInfo.InvariantCulture);
            int mine = Rooms.Labels != null ? Rooms.Rooms.Count : Math.Min(Rooms.Rooms.Count, 1);
            if (rooms != mine) return $"DIFFERS: rooms.tsv has {rooms} rooms, the re-run {mine}";
            if (gRow == null) return Rooms.Labels == null ? "same (one room)" : "DIFFERS: rooms.tsv has no cells";
            var labels = RoomTools.ParseRuns(gRow.Split('\t')[2], W * H);
            if (labels == null || Rooms.Labels == null) return "DIFFERS: cells";
            int diff = 0; for (int i = 0; i < labels.Length; i++) if (labels[i] != Rooms.Labels[i]) diff++;
            return diff == 0 ? $"same ({rooms} rooms, every cell)" : $"DIFFERS in {diff} cells";
        }
    }
}
