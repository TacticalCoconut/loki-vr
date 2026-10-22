using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Loki.World
{
    /// <summary>
    /// Architect's FIX FILE for one map (the map analyzer's fix mode: client/tools/vrmaps, design notes): what the client
    /// draws differently on that map so it reads right in VR. DERIVED DATA ONLY (tile numbers, wall sides, flags, heights in cells):
    /// never a piece of Gravity's files. The map loader applies it while it builds that map (World/MapLoader.cs), and only when the
    /// map's GND and GAT are the very files the fix was made for (their sizes and FNV-1a 64 hashes): with no fix file, or another
    /// version of the map, the map draws exactly as before. A fix changes ONLY what is drawn: the ground's collider (the laser, the
    /// clicks) stays the game's own ground, and the GAT (where units stand and walk) stays the server's.
    ///
    /// The file (UTF-8, tab-separated, '#' comments), as Architect writes it:
    ///   architect-fix  1  &lt;map&gt;  gnd:&lt;bytes&gt;:&lt;hash&gt;  gat:&lt;bytes&gt;:&lt;hash&gt;
    ///   W  tx  ty  N|E  both|flip  &lt;class&gt;    a GND wall: the north edge (Front, "N") or east edge (Right, "E") of tile (tx, ty). The game
    ///                                         draws a Front wall facing north and a Right wall facing east whatever its heights
    ///                                         (GroundMeshBuilder.cs, the walls' fixed normals), and the ground shader culls back faces
    ///                                         (Shaders/LokiGround.shader "Cull Back"): "both" draws it from both sides, "flip" only from its
    ///                                         other side (south / west). The class is the report's (interior, outer, ledge): not read here.
    ///   T  tx  ty  draw  &lt;why&gt;                a top the loader leaves out as black void filler (GroundMeshBuilder.cs, the skip rule) under a
    ///                                         walkable cell: drawn as it is (black, as RO drew it).
    ///   F  tx  ty  &lt;y&gt;  &lt;why&gt;                  a hole beside the walkable floor (no top, nothing drawn in its column) on a map that has no sky of
    ///                                         its own (RO showed black there): a black floor over the tile at height y (cells, Loki y up),
    ///                                         and a black skirt up to every drawn neighbour top's edge that stands higher with no drawn wall
    ///                                         between them.
    /// Wave 27 (wmodel), next-list item 16, single-sided model faces:
    ///   R  rsw:&lt;bytes&gt;:&lt;hash&gt;                    the map's RSW (its model instances, in file order) the M lines were made for: with another
    ///                                         RSW no M line is applied (the ground lines still are: they hang on the GND / GAT).
    ///   M  &lt;inst&gt;  &lt;bytes&gt;:&lt;hash&gt;  &lt;node&gt;  &lt;faces&gt;  &lt;class&gt;
    ///                                         single-sided faces of the RSW's model instance &lt;inst&gt; (its index in the RSW's model list)
    ///                                         drawn from their other side too: &lt;node&gt; is the node's index in the RSM file (file order),
    ///                                         &lt;faces&gt; the faces' indices in that node ("0-3,7"); &lt;bytes&gt;:&lt;hash&gt; the instance's RSM file
    ///                                         (another file: that instance's lines are not applied). The model shader culls back faces
    ///                                         (Shaders/LokiModel.shader Cull Back, as korangar does for opaque models), so a face the file
    ///                                         does not mark two-sided is see-through from behind; the back copy is built as the builder
    ///                                         builds a two-sided face's (World/RsmMeshBuilder.cs BuildBack). The class is the report's.
    /// </summary>
    public sealed class MapFix
    {
        public const int Format = 1;
        public const string Magic = "architect-fix";
        public enum WallMode : byte { Keep = 0, Both = 1, Flip = 2, Front = 3 }
        // Front (warch2, 2026-10-15): only the wall's nominal face (north for an N edge, east for an E edge), for a wall the game draws the
        // other way (a quad with no height at one end: World/GroundMeshBuilder.cs's facing test sees one degenerate triangle)

        public string Map = "";
        public long GndBytes, GatBytes; public string GndHash = "", GatHash = "";
        /// <summary>Per GND tile index (tx + ty * width): its Front (north edge) / Right (east edge) wall's mode.</summary>
        public readonly Dictionary<int, WallMode> Front = new Dictionary<int, WallMode>(), Right = new Dictionary<int, WallMode>();
        public readonly Dictionary<int, string> FrontClass = new Dictionary<int, string>(), RightClass = new Dictionary<int, string>();
        /// <summary>Tops to draw although the void-filler rule leaves them out (tile index).</summary>
        public readonly HashSet<int> DrawTops = new HashSet<int>();
        /// <summary>Holes to fill: tile index → the black floor's height (cells).</summary>
        public readonly SortedDictionary<int, float> Fills = new SortedDictionary<int, float>();
        public readonly Dictionary<int, string> Why = new Dictionary<int, string>();
        /// <summary>The GND's width (tiles), to turn (tx, ty) into an index; set by <see cref="Parse"/> from the map's GND.</summary>
        public int Width;

        public WallMode FrontMode(int tile) => Front.TryGetValue(tile, out var m) ? m : WallMode.Keep;
        public WallMode RightMode(int tile) => Right.TryGetValue(tile, out var m) ? m : WallMode.Keep;
        public int Count => Front.Count + Right.Count + DrawTops.Count + Fills.Count;

        // ------------------------------------------------------------------ wave 27 (wmodel): model faces (kind M)
        /// <summary>One RSW model instance's faces drawn from both sides: the RSM file's identity, and per node (file order) the faces.</summary>
        public sealed class ModelFaces
        {
            public int Instance; public long Bytes; public string Hash = "";
            public readonly SortedDictionary<int, SortedSet<int>> Nodes = new SortedDictionary<int, SortedSet<int>>();
            public readonly Dictionary<int, string> Class = new Dictionary<int, string>();
            public int Count { get { int n = 0; foreach (var s in Nodes.Values) n += s.Count; return n; } }
            public bool Has(int node, int face) => Nodes.TryGetValue(node, out var s) && s.Contains(face);
            public void Add(int node, int face) { if (!Nodes.TryGetValue(node, out var s)) Nodes[node] = s = new SortedSet<int>(); s.Add(face); }
        }
        /// <summary>Per RSW model instance index: its faces drawn from both sides (kind M).</summary>
        public readonly SortedDictionary<int, ModelFaces> Models = new SortedDictionary<int, ModelFaces>();
        /// <summary>The RSW the M lines were made for (R line): its size and FNV-1a 64 hash.</summary>
        public long RswBytes; public string RswHash = "";
        /// <summary>The ground lines (W, T, F): the loader builds the fixed ground only when there are some.</summary>
        public bool HasGround => Count > 0;
        /// <summary>Model faces the M lines name (back copies the loader adds).</summary>
        public int ModelFaceCount { get { int n = 0; foreach (var m in Models.Values) n += m.Count; return n; } }
        /// <summary>Whether the M lines were made for this RSW (the instances they name are this file's).</summary>
        public bool MatchesRsw(byte[] rsw) => rsw != null && RswHash.Length > 0 && rsw.LongLength == RswBytes && Hash(rsw) == RswHash;
        /// <summary>Whether an instance's lines were made for this RSM file.</summary>
        public static bool MatchesModel(ModelFaces m, byte[] rsm) => m != null && rsm != null && rsm.LongLength == m.Bytes && Hash(rsm) == m.Hash;

        /// <summary>One line for the log: what the fix changes.</summary>
        public string Summary()
        {
            int both = 0, flip = 0;
            foreach (var m in Front.Values) if (m == WallMode.Both) both++; else if (m == WallMode.Flip || m == WallMode.Front) flip++;
            foreach (var m in Right.Values) if (m == WallMode.Both) both++; else if (m == WallMode.Flip || m == WallMode.Front) flip++;
            return $"{both} ground walls drawn from both sides, {flip} turned to face the other side, {DrawTops.Count} floors drawn, {Fills.Count} holes filled";
        }

        // ------------------------------------------------------------------ the files' identity
        /// <summary>FNV-1a 64 of the bytes, 16 hex digits.</summary>
        public static string Hash(byte[] b)
        {
            if (b == null) return "";
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < b.Length; i++) { h ^= b[i]; h *= 1099511628211UL; }
            return h.ToString("x16", CultureInfo.InvariantCulture);
        }

        /// <summary>Whether this fix was made for these very files (the map's GND and GAT as the game read them).</summary>
        public bool Matches(byte[] gnd, byte[] gat) =>
            gnd != null && gat != null && gnd.LongLength == GndBytes && gat.LongLength == GatBytes && Hash(gnd) == GndHash && Hash(gat) == GatHash;

        // ------------------------------------------------------------------ text
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Reads a fix file. <paramref name="gndWidth"/>: the map's GND width (tiles). Throws FormatException on a broken file.</summary>
        public static MapFix Parse(string text, int gndWidth)
        {
            var f = new MapFix { Width = gndWidth };
            bool head = false; int n = 0;
            foreach (var raw in (text ?? "").Split('\n'))
            {
                n++;
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                var c = line.Split('\t');
                if (!head)
                {
                    if (c.Length < 5 || c[0] != Magic) throw new FormatException("not an Architect fix file (line " + n + ")");
                    if (int.Parse(c[1], Inv) != Format) throw new FormatException("fix file format " + c[1] + ", this client reads " + Format);
                    f.Map = c[2];
                    ReadId(c[3], "gnd", out f.GndBytes, out f.GndHash); ReadId(c[4], "gat", out f.GatBytes, out f.GatHash);
                    head = true; continue;
                }
                switch (c[0])
                {
                    case "W":
                    {
                        if (c.Length < 5) throw new FormatException("line " + n + ": W needs tx ty N|E both|flip");
                        int t = Tile(c[1], c[2], gndWidth, n);
                        var mode = c[4] == "both" ? WallMode.Both : c[4] == "flip" ? WallMode.Flip : c[4] == "front" ? WallMode.Front : throw new FormatException("line " + n + ": wall mode " + c[4]);
                        if (c[3] == "N") { f.Front[t] = mode; if (c.Length > 5) f.FrontClass[t] = c[5]; }
                        else if (c[3] == "E") { f.Right[t] = mode; if (c.Length > 5) f.RightClass[t] = c[5]; }
                        else throw new FormatException("line " + n + ": wall edge " + c[3]);
                        break;
                    }
                    case "T":
                        if (c.Length < 4 || c[3] != "draw") throw new FormatException("line " + n + ": T needs tx ty draw");
                        f.DrawTops.Add(Tile(c[1], c[2], gndWidth, n));
                        break;
                    case "F":
                    {
                        if (c.Length < 4) throw new FormatException("line " + n + ": F needs tx ty y");
                        int t = Tile(c[1], c[2], gndWidth, n);
                        f.Fills[t] = float.Parse(c[3], NumberStyles.Float, Inv);
                        if (c.Length > 4) f.Why[t] = c[4];
                        break;
                    }
                    // wave 27 (wmodel): model faces drawn from both sides, and the RSW they were made for
                    case "R":
                        if (c.Length < 2) throw new FormatException("line " + n + ": R needs rsw:<bytes>:<hash>");
                        ReadId(c[1], "rsw", out f.RswBytes, out f.RswHash);
                        break;
                    case "M":
                    {
                        if (c.Length < 5) throw new FormatException("line " + n + ": M needs inst bytes:hash node faces");
                        int inst = int.Parse(c[1], Inv), node = int.Parse(c[3], Inv);
                        if (inst < 0 || node < 0) throw new FormatException("line " + n + ": M instance / node below 0");
                        var id = c[2].Split(':');
                        if (id.Length != 2) throw new FormatException("line " + n + ": M model identity " + c[2]);
                        long bytes = long.Parse(id[0], Inv);
                        if (!f.Models.TryGetValue(inst, out var mf)) f.Models[inst] = mf = new ModelFaces { Instance = inst, Bytes = bytes, Hash = id[1] };
                        else if (mf.Bytes != bytes || mf.Hash != id[1]) throw new FormatException("line " + n + ": instance " + inst + " named with two model files");
                        foreach (var part in c[4].Split(','))
                        {
                            var r = part.Split('-');
                            int a = int.Parse(r[0], Inv), b = r.Length > 1 ? int.Parse(r[1], Inv) : a;
                            if (a < 0 || b < a || r.Length > 2) throw new FormatException("line " + n + ": M faces " + c[4]);
                            for (int k = a; k <= b; k++) mf.Add(node, k);
                        }
                        if (c.Length > 5) mf.Class[node] = c[5];
                        break;
                    }
                    default: throw new FormatException("line " + n + ": unknown line kind " + c[0]);
                }
            }
            if (!head) throw new FormatException("empty fix file");
            if (f.Models.Count > 0 && f.RswHash.Length == 0) throw new FormatException("M lines without the R line (the RSW they were made for)");
            return f;
        }

        /// <summary>A sorted set of indices as runs: "0-3,7,9-12".</summary>
        public static string Runs(IEnumerable<int> sorted)
        {
            var sb = new StringBuilder(); int a = -1, b = -1;
            void Flush() { if (a < 0) return; if (sb.Length > 0) sb.Append(','); sb.Append(a.ToString(Inv)); if (b > a) sb.Append('-').Append(b.ToString(Inv)); }
            foreach (int v in sorted) { if (a >= 0 && v == b + 1) { b = v; continue; } Flush(); a = b = v; }
            Flush();
            return sb.ToString();
        }

        private static void ReadId(string s, string what, out long bytes, out string hash)
        {
            var p = s.Split(':');
            if (p.Length != 3 || p[0] != what) throw new FormatException("the fix file's " + what + " identity: " + s);
            bytes = long.Parse(p[1], Inv); hash = p[2];
        }

        private static int Tile(string sx, string sy, int width, int line)
        {
            int x = int.Parse(sx, Inv), y = int.Parse(sy, Inv);
            if (x < 0 || y < 0 || x >= width) throw new FormatException("line " + line + ": tile " + sx + "," + sy + " is outside the map");
            return x + y * width;
        }

        /// <summary>The file's text (Architect writes it; the comment lines say what each kind of line changes and why).</summary>
        public string ToText(IEnumerable<string> comments = null)
        {
            var sb = new StringBuilder();
            if (comments != null) foreach (var c in comments) sb.Append("# ").Append(c).Append('\n');
            sb.Append(Magic).Append('\t').Append(Format.ToString(Inv)).Append('\t').Append(Map)
              .Append("\tgnd:").Append(GndBytes.ToString(Inv)).Append(':').Append(GndHash)
              .Append("\tgat:").Append(GatBytes.ToString(Inv)).Append(':').Append(GatHash).Append('\n');
            var walls = new SortedSet<(int ty, int tx, int edge)>();
            foreach (var k in Front.Keys) walls.Add((k / Width, k % Width, 0));
            foreach (var k in Right.Keys) walls.Add((k / Width, k % Width, 1));
            foreach (var (ty, tx, edge) in walls)
            {
                int k = tx + ty * Width; var m = edge == 0 ? Front[k] : Right[k];
                string cls = edge == 0 ? (FrontClass.TryGetValue(k, out var a) ? a : "") : (RightClass.TryGetValue(k, out var b) ? b : "");
                sb.Append("W\t").Append(tx.ToString(Inv)).Append('\t').Append(ty.ToString(Inv)).Append('\t').Append(edge == 0 ? "N" : "E").Append('\t')
                  .Append(m == WallMode.Both ? "both" : m == WallMode.Front ? "front" : "flip").Append('\t').Append(cls).Append('\n');
            }
            var tops = new List<int>(DrawTops); tops.Sort();
            foreach (var k in tops) sb.Append("T\t").Append((k % Width).ToString(Inv)).Append('\t').Append((k / Width).ToString(Inv)).Append("\tdraw\twalkable\n");
            foreach (var kv in Fills)
                sb.Append("F\t").Append((kv.Key % Width).ToString(Inv)).Append('\t').Append((kv.Key / Width).ToString(Inv)).Append('\t')
                  .Append(kv.Value.ToString("0.###", Inv)).Append('\t').Append(Why.TryGetValue(kv.Key, out var w) ? w : "hole").Append('\n');
            // wave 27 (wmodel): the model faces, after the ground's lines (a file without them reads as before)
            if (Models.Count > 0)
            {
                sb.Append("R\trsw:").Append(RswBytes.ToString(Inv)).Append(':').Append(RswHash).Append('\n');
                foreach (var m in Models.Values)
                    foreach (var kv in m.Nodes)
                    {
                        if (kv.Value.Count == 0) continue;
                        sb.Append("M\t").Append(m.Instance.ToString(Inv)).Append('\t').Append(m.Bytes.ToString(Inv)).Append(':').Append(m.Hash).Append('\t')
                          .Append(kv.Key.ToString(Inv)).Append('\t').Append(Runs(kv.Value)).Append('\t').Append(m.Class.TryGetValue(kv.Key, out var cl) ? cl : "").Append('\n');
                    }
            }
            return sb.ToString();
        }
    }
}
