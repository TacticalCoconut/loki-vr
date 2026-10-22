using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Loki.Formats;
using Loki.World;
using UnityEngine;

namespace Loki.Data
{
    /// <summary>
    /// Client wave 20 (w20rooms), the design's point 54: the ROOM TABLE of the indoor maps (rooms.tsv), made by the first-run builder
    /// (<see cref="ClientDataBuilder"/>, step "Indoor rooms") from the player's own game files, like every other table (never shipped).
    /// The client then draws, on an indoor map, only the room the character is in (World/RoomMask.cs).
    ///
    /// Which maps are indoor (no reference client decides this for culling: the rule is Loki's, from three things the client reads):
    ///  1. the client's own indoor list, data\indoorrswtable.txt (Gravity's: the official client gives these maps its indoor camera);
    ///  2. the client's navigation data (navi_map_br.lub, navigation.tsv "M" rows): the map type 5003, which marks the interiors
    ///     (75 maps on the 2026-09-30 LATAM install, every one a house / guild / castle inside; 5001 is everything else, 5002 the
    ///     airships) — it adds the 8 interiors the first list misses, among them the new character's ship iz_int and the Izlude
    ///     academy's floors iz_ac01 / iz_ac02;
    ///  3. COPIES of such a map: its name followed by two digits or by "_" and a letter (iz_int01-04, the server's start-point copies;
    ///     iz_ac01_a-d) whose ground (the GAT) is byte for byte the indoor map's.
    ///
    /// The rooms of a map (GAT cells; a cell is passable as the server's path search sees it, rA map.cpp CELL_CHKNOPASS: a walkable
    /// type and not the map's last column / row):
    ///  • a room = one 4-connected area of passable cells (RO walks in 8 directions, but a diagonal step needs both side cells
    ///    free, so 4-connected areas are exactly the places one can walk between). Warps were measured as room borders too
    ///    (navigation.tsv's warp cells, a 3 x 3 trigger cut out of the walkable cells): of the 149 indoor maps that changes only
    ///    spl_in02, where it merely shrinks rooms; the warps stand at the rooms' edges, so the walkable areas alone are the rooms;
    ///  • an area under <see cref="MinRoomCells"/> cells (a walkable speck behind furniture) joins the nearest room within
    ///    <see cref="MergeCells"/> cells; farther away it is a room of its own;
    ///  • the room's EXTENT (where it is drawn), grown in this order (a cell, once a room's, stays that room's):
    ///    a. its walkable cells;
    ///    b. every other cell within <see cref="WallCells"/> cells of them (the floor's rim; a cell near two rooms goes to the nearer);
    ///    c. its WALLS: the cells the map draws ground on (the GND's tops and wall faces, minus the all-black void filler, as the map
    ///       loader leaves them out: <see cref="GroundCells"/>) that touch the room, spread from it (8 directions, every room at once,
    ///       so ground shared by two rooms splits between them): a wall taller than the rim, a gallery, a stair;
    ///    d. every POCKET of other cells shut in by rooms' cells (a pit, a pool, a dais in a hall), each cell to its nearest room;
    ///    e. the ground under its MODELS: every RSW model instance's box (the RSM's own vertices placed as the map loader places it)
    ///       belongs to the room with the most walkable cells under it, else to the room holding most of the cells under it, else to
    ///       the nearest room within <see cref="AssignCells"/> cells of it, else to none (143 of 73117 on the 2026-09-30 install,
    ///       nearly all the furniture of rooms no one can walk into: 52 on prt_in); a room's model claims the cells under its box
    ///       that no nearer room holds; then d again.
    ///    Cells of no room are not drawn on that map.
    /// A map with one room is listed but nothing on it is culled.
    ///
    /// rooms.tsv (UTF-8, tab-separated; "#" lines are comments):
    ///   I  map  width  height  rooms  rule  models  assigned  unassigned  missing     one per indoor map (rule: table / navi / both /
    ///                                                                                  copy:&lt;map&gt;; missing = model files not found)
    ///   R  map  id  cells  x0  y0  x1  y1  models  extent                          one per room (walkable cells and their box, models,
    ///                                                                                  cells drawn)
    ///   G  map  runs                                                                   maps with two rooms or more: the room id of every cell,
    ///                                                                                  row by row from (0, 0), "id:count" runs ("id" alone = 1)
    /// Runs on the builder's worker thread: the parsers, the BMP / TGA decoders and the model box are plain C# and matrix math.
    /// It reads, per indoor map, the RSW, GAT and GND, their RSM models and ground textures: about 2 s on this PC (2026-10-10).
    /// </summary>
    public static class RoomTools
    {
        public const string Table = "rooms.tsv";
        public const string IndoorList = "data\\indoorrswtable.txt";
        /// <summary>navigation.tsv "M" rows' type for interiors (navi_map_br.lub).</summary>
        public const int NaviIndoorType = 5003;
        /// <summary>A walkable area smaller than this (cells) is not a room of its own when a room is near.</summary>
        public const int MinRoomCells = 20;
        /// <summary>How far (cells, 8 directions) a small area may lie from a room to join it.</summary>
        public const int MergeCells = 6;
        /// <summary>The floor's rim and the walls: non-walkable cells within this many cells of a room's walkable cells (the room mask's
        /// dilation before this wave, World/RoomMask.cs WallCells 4; widened to 6 so deep shop counters/walls in prt_in and balconies are kept).</summary>
        public const int WallCells = 6;
        /// <summary>A model with no walkable cell under its box belongs to the nearest room within this many cells of the box.</summary>
        public const int AssignCells = 10;
        /// <summary>Room ids are bytes (the mask texture is R8): a map with more rooms is listed as one room (not culled).</summary>
        public const int MaxRooms = 254;

        /// <summary>The client's indoor list (data\indoorrswtable.txt): "name.rsw#" lines, "//" comments.</summary>
        public static HashSet<string> ParseIndoorList(byte[] data)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (data == null) return set;
            foreach (var raw in IO.RoReader.Latin1.GetString(data).Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;
                int h = line.IndexOf('#'); if (h > 0) line = line.Substring(0, h);
                if (line.EndsWith(".rsw", StringComparison.OrdinalIgnoreCase)) set.Add(line.Substring(0, line.Length - 4).ToLowerInvariant());
            }
            return set;
        }

        /// <summary>navigation.tsv's interiors: "M" rows (M, map, name, type, width, height) with the type 5003.</summary>
        public static HashSet<string> ParseNaviIndoor(byte[] navigation)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (navigation == null) return set;
            foreach (var line in Py.Utf8NoBom.GetString(navigation).Split('\n'))
            {
                if (!line.StartsWith("M\t", StringComparison.Ordinal)) continue;
                var c = line.TrimEnd('\r').Split('\t');
                if (c.Length >= 4 && int.TryParse(c[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type) && type == NaviIndoorType) set.Add(c[1].ToLowerInvariant());
            }
            return set;
        }

        // ------------------------------------------------------------------ the step
        /// <param name="navigation">navigation.tsv as made by this run's "Sky and navigation" step, or the last good one</param>
        public static ToolOutput Rooms(IToolInputs inp, byte[] navigation)
        {
            var listed = ParseIndoorList(inp.Grf(IndoorList));
            if (listed.Count == 0) throw new ToolError("data\\indoorrswtable.txt is missing or lists no map");
            var navi = ParseNaviIndoor(navigation);
            if (navi.Count == 0) throw new ToolError("navigation.tsv names no interior (type " + NaviIndoorType + ")");
            var rule = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var m in listed) rule[m] = navi.Contains(m) ? "both" : "table";
            foreach (var m in navi) if (!rule.ContainsKey(m)) rule[m] = "navi";
            // copies: the name + two digits or "_" + a letter, with the very same ground
            var copies = new List<KeyValuePair<string, string>>();
            foreach (var m in rule.Keys.ToList())
            {
                byte[] gat = null; bool gatRead = false;
                for (int k = 0; k < 126; k++)
                {
                    string c = k < 100 ? m + k.ToString("00", CultureInfo.InvariantCulture) : m + "_" + (char)('a' + k - 100);
                    if (rule.ContainsKey(c) || !inp.Exists("data\\" + c + ".rsw")) continue;
                    if (!gatRead) { gatRead = true; gat = GatBytes(inp, m); }
                    var other = GatBytes(inp, c);
                    if (Same(gat, other)) copies.Add(new KeyValuePair<string, string>(c, m));
                }
            }
            foreach (var kv in copies) rule[kv.Key] = "copy:" + kv.Value;

            var sb = new StringBuilder();
            sb.Append("# rooms.tsv: built by Loki (Data/RoomTools.cs) from ").Append(inp.GrfName).Append(" (GRF 0x").Append(T.Hex3(inp.GrfVersion))
              .Append("): the indoor maps (data\\indoorrswtable.txt, navigation.tsv type ").Append(NaviIndoorType).Append(", their copies), their rooms and every cell's room\n");
            sb.Append("# I map width height rooms rule models assigned unassigned missing | R map id cells x0 y0 x1 y1 models extent | G map runs\n");
            var boxes = new Dictionary<string, Box?>(StringComparer.Ordinal); var blacks = new Dictionary<string, bool>(StringComparer.Ordinal);
            int maps = 0, culled = 0, roomsTotal = 0, skipped = 0; var skippedNames = new List<string>();
            foreach (var kv in rule)
            {
                MapRooms r;
                try { r = Build(inp, kv.Key, boxes, blacks); }
                catch (Exception e) when (e is RoFormatException || e is IO.GrfEntryException) { skipped++; skippedNames.Add(kv.Key + " (" + e.Message.Trim() + ")"); continue; }
                if (r == null) { skipped++; skippedNames.Add(kv.Key + " (no map files)"); continue; }
                r.Rule = kv.Value;
                Write(sb, r);
                maps++; roomsTotal += r.Rooms.Count; if (r.Labels != null) culled++;
            }
            var o = new ToolOutput();
            o.AddUtf8(Table, sb.ToString());
            o.Summary = $"rooms.tsv: {maps} indoor maps ({listed.Count} listed, {rule.Count(x => x.Value == "navi")} more by navigation, {copies.Count} copies), {roomsTotal} rooms, {culled} maps with two rooms or more, " +
                        $"{boxes.Count} model files; {skipped} without map files{(skippedNames.Count > 0 ? ": " + string.Join(", ", skippedNames.Take(12)) : "")}";
            return o;
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static byte[] GatBytes(IToolInputs inp, string map)
        {
            try
            {
                var rsw = inp.Grf("data\\" + map + ".rsw");
                return rsw == null ? null : inp.Grf("data\\" + RswFile.Parse(rsw).GatFile);
            }
            catch (Exception e) when (e is RoFormatException || e is IO.GrfEntryException) { return null; } // a damaged map is no copy
        }

        // ------------------------------------------------------------------ one map
        public sealed class Room { public int Id, Cells, X0 = int.MaxValue, Y0 = int.MaxValue, X1 = -1, Y1 = -1, Models, Extent; }

        public sealed class MapRooms
        {
            public string Map, Rule; public int Width, Height, Models, Assigned, Unassigned, Missing;
            /// <summary>Cells of no room shut in by rooms, given to the nearest room (<see cref="FillPockets"/>).</summary>
            public int Pockets;
            /// <summary>Cells of drawn ground beyond the walls a room spread to (step 4b).</summary>
            public int Ground;
            public readonly List<Room> Rooms = new List<Room>();
            /// <summary>The room id of every cell (x + y · width; 0 = none); null when the map has one room (nothing is culled).</summary>
            public byte[] Labels;
        }

        public struct Box { public Vector3 Min, Max; }

        /// <summary>The rooms of one map, or null when its files are not in the game data.</summary>
        public static MapRooms Build(IToolInputs inp, string map, Dictionary<string, Box?> boxes, Dictionary<string, bool> blacks, List<string> notes = null)
        {
            var rswBytes = inp.Grf("data\\" + map + ".rsw");
            if (rswBytes == null) return null;
            var rsw = RswFile.Parse(rswBytes);
            var gatBytes = inp.Grf("data\\" + rsw.GatFile);
            if (gatBytes == null) return null;
            var gat = GatFile.Parse(gatBytes);
            var gndBytes = inp.Grf("data\\" + rsw.GndFile);
            var ground = gndBytes != null ? GroundCells(GndFile.Parse(gndBytes), gat.Width, gat.Height, t => Black(inp, t, blacks)) : null;
            return Build(map, rsw, gat, file => ModelBox(inp, file, boxes), notes, ground);
        }

        /// <summary>The cells the map loader draws ground on (World/MapLoader.cs + GroundMeshBuilder.cs): a tile's top unless its texture
        /// is all black (RoAssets.IsBlackTexture) or its colour is black; a wall face unless its texture is all black. A tile is 2 x 2
        /// cells; a wall stands on the edge between two tiles: the cells on both sides.</summary>
        public static bool[] GroundCells(GndFile gnd, int w, int h, Func<string, bool> black)
        {
            var cells = new bool[w * h];
            var blackIdx = new bool[gnd.Textures.Count];
            for (int i = 0; i < blackIdx.Length; i++) blackIdx[i] = black(gnd.Textures[i]);
            bool Drawn(int surf, bool top)
            {
                if (surf < 0 || surf >= gnd.Surfaces.Length) return false;
                var s = gnd.Surfaces[surf];
                if (s.TextureIndex < 0 || (s.TextureIndex < blackIdx.Length && blackIdx[s.TextureIndex])) return false;
                return !(top && s.R <= 8 && s.G <= 8 && s.B <= 8);
            }
            void Mark(int x, int y) { if (x >= 0 && y >= 0 && x < w && y < h) cells[x + y * w] = true; }
            int gw = gnd.Width, gh = gnd.Height;
            for (int ty = 0; ty < gh; ty++)
                for (int tx = 0; tx < gw; tx++)
                {
                    var t = gnd.Tiles[tx + ty * gw]; int x = tx * 2, y = ty * 2;
                    if (Drawn(t.Top, true)) { Mark(x, y); Mark(x + 1, y); Mark(x, y + 1); Mark(x + 1, y + 1); }
                    if (ty + 1 < gh && Drawn(t.Front, false)) { Mark(x, y + 1); Mark(x + 1, y + 1); Mark(x, y + 2); Mark(x + 1, y + 2); }
                    if (tx + 1 < gw && Drawn(t.Right, false)) { Mark(x + 1, y); Mark(x + 1, y + 1); Mark(x + 2, y); Mark(x + 2, y + 1); }
                }
            return cells;
        }

        /// <summary>RoAssets.IsBlackTexture's test on the builder's thread: a BMP or TGA decoded by the client's own decoders; a JPG / PNG
        /// (Unity decodes those, on the main thread only) counts as not black. Cached per texture.</summary>
        private static bool Black(IToolInputs inp, string name, Dictionary<string, bool> cache)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string key = IO.RoPath.Normalize(name);
            if (cache.TryGetValue(key, out bool b)) return b;
            b = false;
            try
            {
                var d = inp.Grf("data\\texture\\" + key);
                if (d != null && d.Length > 4)
                {
                    if (d[0] == 'B' && d[1] == 'M') b = IO.ImageDecoders.IsAllBlack(IO.ImageDecoders.DecodeBmp(d, true));
                    else if (key.EndsWith(".tga", StringComparison.Ordinal)) b = IO.ImageDecoders.IsAllBlack(IO.ImageDecoders.DecodeTga(d));
                }
            }
            catch (Exception) { b = false; } // a texture that does not decode is drawn as the missing texture: not black
            cache[key] = b;
            return b;
        }

        /// <summary>A model file's static box (Loki model-local), cached per file; null when the file is missing or damaged.</summary>
        private static Box? ModelBox(IToolInputs inp, string fileName, Dictionary<string, Box?> boxes)
        {
            string key = IO.RoPath.Normalize(fileName);
            if (boxes.TryGetValue(key, out var b)) return b;
            Box? box = null;
            try
            {
                var bytes = inp.Grf("data\\model\\" + key);
                if (bytes != null && RsmMeshBuilder.StaticBounds(RsmFile.Parse(bytes), out var mn, out var mx)) box = new Box { Min = mn, Max = mx };
            }
            catch (Exception) { box = null; } // as the map loader (World/RoAssets.cs ModelMesh): a model that cannot be read or built is left out
            boxes[key] = box;
            return box;
        }

        /// <summary>The rooms of a map from its parsed files (see the class comment); <paramref name="modelBox"/> gives a model file's box.</summary>
        /// <param name="notes">when given, one line per model left without a room (its file, box in cells, nearest room and distance)</param>
        /// <param name="ground">the cells the map draws ground on (<see cref="GroundCells"/>); null: none known</param>
        public static MapRooms Build(string map, RswFile rsw, GatFile gat, Func<string, Box?> modelBox, List<string> notes = null, bool[] ground = null)
        {
            int w = gat.Width, h = gat.Height, n = w * h;
            var r = new MapRooms { Map = map, Width = w, Height = h };
            var pass = new bool[n];
            for (int y = 0; y < h - 1; y++) for (int x = 0; x < w - 1; x++) pass[x + y * w] = gat.Cells[x + y * w].Walkable;
            // 1. the walkable areas, 4-connected
            var comp = new int[n]; var sizes = new List<int> { 0 }; var stack = new Stack<int>();
            for (int i = 0; i < n; i++)
            {
                if (!pass[i] || comp[i] != 0) continue;
                int id = sizes.Count, size = 0; comp[i] = id; stack.Push(i);
                while (stack.Count > 0)
                {
                    int c = stack.Pop(); size++;
                    int cx = c % w, cy = c / w;
                    if (cx > 0) Visit(c - 1); if (cx < w - 1) Visit(c + 1); if (cy > 0) Visit(c - w); if (cy < h - 1) Visit(c + w);
                }
                sizes.Add(size);
                void Visit(int j) { if (pass[j] && comp[j] == 0) { comp[j] = id; stack.Push(j); } }
            }
            // 2. small areas join the nearest big one within MergeCells (8 directions)
            var big = new bool[sizes.Count]; bool anyBig = false;
            for (int a = 1; a < sizes.Count; a++) if (sizes[a] >= MinRoomCells) { big[a] = true; anyBig = true; }
            var area = (int[])comp.Clone(); // area id per walkable cell after the merge
            if (anyBig)
            {
                Distance(w, h, i => pass[i] && big[comp[i]], i => comp[i], MergeCells, out var dist, out var near);
                var best = new Dictionary<int, (int d, int to)>();
                for (int i = 0; i < n; i++)
                {
                    if (!pass[i] || big[comp[i]] || dist[i] > MergeCells) continue;
                    if (!best.TryGetValue(comp[i], out var cur) || dist[i] < cur.d) best[comp[i]] = (dist[i], near[i]);
                }
                for (int i = 0; i < n; i++) if (pass[i] && !big[comp[i]] && best.TryGetValue(comp[i], out var to)) area[i] = to.to;
            }
            // 3. room ids in the order of their first cell (row by row)
            var idOf = new Dictionary<int, int>(); var label = new int[n];
            for (int i = 0; i < n; i++)
            {
                if (!pass[i]) continue;
                if (!idOf.TryGetValue(area[i], out int id)) { id = idOf.Count + 1; idOf[area[i]] = id; r.Rooms.Add(new Room { Id = id }); }
                label[i] = id;
                var room = r.Rooms[id - 1]; room.Cells++;
                int x = i % w, y = i / w;
                if (x < room.X0) room.X0 = x; if (y < room.Y0) room.Y0 = y; if (x > room.X1) room.X1 = x; if (y > room.Y1) room.Y1 = y;
            }
            r.Models = rsw.Models.Count;
            if (r.Rooms.Count < 2 || r.Rooms.Count > MaxRooms) { foreach (var room in r.Rooms) room.Extent = room.Cells; return r; }

            // 4. the walls: every cell within WallCells of a room goes to the nearest room
            Distance(w, h, i => label[i] > 0, i => label[i], int.MaxValue, out var d0, out var n0);
            var owner = new int[n]; var score = new int[n];
            for (int i = 0; i < n; i++)
            {
                if (label[i] > 0) { owner[i] = label[i]; score[i] = -1; }
                else if (d0[i] <= WallCells) { owner[i] = n0[i]; score[i] = -1; }
                else score[i] = int.MaxValue;
            }
            // 4b. the ground the map draws beyond them (a wall taller than the rim, a gallery, the ends of a wall): each room spreads
            // through the cells of drawn ground that touch it (8 directions, the nearest room first)
            if (ground != null && ground.Length == n)
            {
                var q = new Queue<int>();
                for (int i = 0; i < n; i++) if (owner[i] > 0) q.Enqueue(i);
                while (q.Count > 0)
                {
                    int c = q.Dequeue(), cx = c % w, cy = c / w;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int y = cy + dy; if (y < 0 || y >= h) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int x = cx + dx; if (x < 0 || x >= w) continue;
                            int j = x + y * w;
                            if (owner[j] != 0 || !ground[j]) continue;
                            owner[j] = owner[c]; score[j] = -1; r.Ground++; q.Enqueue(j);
                        }
                    }
                }
            }
            // 4c. a pocket of cells of no room shut in by rooms' cells (a pit, a pool, a dais in a hall) is floor of the nearest room
            int holes = FillPockets(w, h, owner, score, n0);
            // 5. the models: each to a room, then the cells under its box that no nearer room holds
            var center = new Vector3(w * 0.5f, 0f, h * 0.5f); // the map loader's centre: GND tiles (= GAT cells / 2) in cells
            var counts = new int[r.Rooms.Count + 1];
            foreach (var mo in rsw.Models)
            {
                var box = modelBox(mo.FileName);
                if (box == null) { r.Missing++; continue; }
                var m = RsmMeshBuilder.InstanceMatrix(mo.Position, mo.Rotation, mo.Scale, center);
                float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
                for (int k = 0; k < 8; k++)
                {
                    var b = box.Value;
                    var p = m.MultiplyPoint3x4(new Vector3((k & 1) == 0 ? b.Min.x : b.Max.x, (k & 2) == 0 ? b.Min.y : b.Max.y, (k & 4) == 0 ? b.Min.z : b.Max.z));
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                }
                int x0 = Mathf.Clamp(Mathf.FloorToInt(minX), 0, w - 1), x1 = Mathf.Clamp(Mathf.FloorToInt(maxX), 0, w - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(minZ), 0, h - 1), y1 = Mathf.Clamp(Mathf.FloorToInt(maxZ), 0, h - 1);
                if (maxX < 0 || maxZ < 0 || minX >= w || minZ >= h) { r.Unassigned++; notes?.Add($"{map} {mo.FileName} off the map ({minX:0},{minZ:0})-({maxX:0},{maxZ:0})"); continue; } // wholly off the map
                Array.Clear(counts, 0, counts.Length);
                int owner0 = 0, most = 0, nearD = int.MaxValue, nearR = 0;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = x + y * w;
                        if (label[i] > 0 && ++counts[label[i]] > most) { most = counts[label[i]]; owner0 = label[i]; }
                        if (d0[i] < nearD) { nearD = d0[i]; nearR = n0[i]; }
                    }
                if (owner0 == 0) // no walkable cell under it: the room holding most of its cells (its walls, a pocket)
                {
                    Array.Clear(counts, 0, counts.Length);
                    for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) { int o = owner[x + y * w]; if (o > 0 && ++counts[o] > most) { most = counts[o]; owner0 = o; } }
                }
                if (owner0 == 0 && nearR > 0 && nearD <= AssignCells) owner0 = nearR;
                if (owner0 == 0) { r.Unassigned++; notes?.Add($"{map} {mo.FileName} cells {x0},{y0}-{x1},{y1}: nearest room {nearR} {(nearD == int.MaxValue ? "-" : nearD.ToString())} cells away"); continue; }
                r.Assigned++; r.Rooms[owner0 - 1].Models++;
                var ob = r.Rooms[owner0 - 1];
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = x + y * w;
                        if (score[i] < 0) continue; // a room's own walkable cell or wall
                        // the nearest room wins a cell outright; another room's claim ranks by how far its walkable box is
                        int s = n0[i] == owner0 ? d0[i] : 100000 + Math.Max(Math.Max(ob.X0 - x, x - ob.X1), Math.Max(ob.Y0 - y, y - ob.Y1));
                        if (s < score[i]) { score[i] = s; owner[i] = owner0; }
                    }
            }
            holes += FillPockets(w, h, owner, score, n0); // the models' boxes may shut in more
            r.Pockets = holes;
            r.Labels = new byte[n];
            for (int i = 0; i < n; i++) { r.Labels[i] = (byte)owner[i]; if (owner[i] > 0) r.Rooms[owner[i] - 1].Extent++; }
            return r;
        }

        /// <summary>Cells of no room that the map's border cannot reach through other such cells (4 directions) go to their nearest
        /// room (<paramref name="near"/>) and are fixed like walls. Returns how many.</summary>
        private static int FillPockets(int w, int h, int[] owner, int[] score, int[] near)
        {
            int n = w * h; var outside = new bool[n]; var q = new Queue<int>();
            for (int x = 0; x < w; x++) { Seed(x); Seed(x + (h - 1) * w); }
            for (int y = 0; y < h; y++) { Seed(y * w); Seed(w - 1 + y * w); }
            void Seed(int i) { if (owner[i] == 0 && !outside[i]) { outside[i] = true; q.Enqueue(i); } }
            while (q.Count > 0)
            {
                int c = q.Dequeue(), cx = c % w, cy = c / w;
                if (cx > 0) Seed(c - 1); if (cx < w - 1) Seed(c + 1); if (cy > 0) Seed(c - w); if (cy < h - 1) Seed(c + w);
            }
            int filled = 0;
            for (int i = 0; i < n; i++) if (owner[i] == 0 && !outside[i] && near[i] > 0) { owner[i] = near[i]; score[i] = -1; filled++; }
            return filled;
        }

        /// <summary>Breadth-first distance in 8 directions (Chebyshev steps, through any cell) from the source cells, up to
        /// <paramref name="limit"/>, with the nearest source's id (ties: the first reached, row-by-row source order).</summary>
        private static void Distance(int w, int h, Func<int, bool> source, Func<int, int> idOf, int limit, out int[] dist, out int[] near)
        {
            int n = w * h; dist = new int[n]; near = new int[n];
            var q = new Queue<int>();
            for (int i = 0; i < n; i++) { if (source(i)) { dist[i] = 0; near[i] = idOf(i); q.Enqueue(i); } else dist[i] = int.MaxValue; }
            while (q.Count > 0)
            {
                int c = q.Dequeue(); int d = dist[c] + 1; if (d > limit) continue;
                int cx = c % w, cy = c / w;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int y = cy + dy; if (y < 0 || y >= h) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx; if (x < 0 || x >= w) continue;
                        int j = x + y * w;
                        if (dist[j] > d) { dist[j] = d; near[j] = near[c]; q.Enqueue(j); }
                    }
                }
            }
        }

        // ------------------------------------------------------------------ the rows
        private static void Write(StringBuilder sb, MapRooms r)
        {
            var inv = CultureInfo.InvariantCulture;
            sb.Append("I\t").Append(r.Map).Append('\t').Append(r.Width.ToString(inv)).Append('\t').Append(r.Height.ToString(inv)).Append('\t')
              .Append((r.Labels != null ? r.Rooms.Count : Math.Min(r.Rooms.Count, 1)).ToString(inv)).Append('\t').Append(r.Rule).Append('\t')
              .Append(r.Models.ToString(inv)).Append('\t').Append(r.Assigned.ToString(inv)).Append('\t').Append(r.Unassigned.ToString(inv)).Append('\t').Append(r.Missing.ToString(inv)).Append('\n');
            if (r.Labels == null)
            {
                // one room (or more than an R8 mask can hold): listed as one, nothing culled
                int cells = r.Rooms.Sum(x => x.Cells);
                int x0 = r.Rooms.Count > 0 ? r.Rooms.Min(x => x.X0) : 0, y0 = r.Rooms.Count > 0 ? r.Rooms.Min(x => x.Y0) : 0, x1 = r.Rooms.Count > 0 ? r.Rooms.Max(x => x.X1) : -1, y1 = r.Rooms.Count > 0 ? r.Rooms.Max(x => x.Y1) : -1;
                sb.Append("R\t").Append(r.Map).Append("\t1\t").Append(cells.ToString(inv)).Append('\t').Append(x0.ToString(inv)).Append('\t').Append(y0.ToString(inv)).Append('\t')
                  .Append(x1.ToString(inv)).Append('\t').Append(y1.ToString(inv)).Append('\t').Append(r.Models.ToString(inv)).Append('\t').Append((r.Width * r.Height).ToString(inv)).Append('\n');
                return;
            }
            foreach (var room in r.Rooms)
                sb.Append("R\t").Append(r.Map).Append('\t').Append(room.Id.ToString(inv)).Append('\t').Append(room.Cells.ToString(inv)).Append('\t')
                  .Append(room.X0.ToString(inv)).Append('\t').Append(room.Y0.ToString(inv)).Append('\t').Append(room.X1.ToString(inv)).Append('\t').Append(room.Y1.ToString(inv)).Append('\t')
                  .Append(room.Models.ToString(inv)).Append('\t').Append(room.Extent.ToString(inv)).Append('\n');
            sb.Append("G\t").Append(r.Map).Append('\t');
            var l = r.Labels; int at = 0; bool first = true;
            while (at < l.Length)
            {
                int run = 1; while (at + run < l.Length && l[at + run] == l[at]) run++;
                if (!first) sb.Append(','); first = false;
                sb.Append(l[at].ToString(inv)); if (run > 1) sb.Append(':').Append(run.ToString(inv));
                at += run;
            }
            sb.Append('\n');
        }

        /// <summary>A "G" row's runs back into one room id per cell (null when they do not fill exactly <paramref name="cells"/>).</summary>
        public static byte[] ParseRuns(string runs, int cells)
        {
            var l = new byte[cells]; int at = 0;
            foreach (var part in runs.Split(','))
            {
                int colon = part.IndexOf(':');
                if (!int.TryParse(colon < 0 ? part : part.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id < 0 || id > 255) return null;
                int count = 1;
                if (colon >= 0 && (!int.TryParse(part.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out count) || count < 1)) return null;
                if (at + count > cells) return null;
                for (int k = 0; k < count; k++) l[at++] = (byte)id;
            }
            return at == cells ? l : null;
        }
    }
}
