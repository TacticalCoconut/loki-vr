using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Loki.World;

namespace Loki.VrMaps
{
    /// <summary>Architect's look at one map as text (for hand checks): every cell as one character, north up.</summary>
    internal static class GridDump
    {
        //  '.' walkable cell (its GND tile's top drawn)   ',' walkable, no top drawn   '#' blocked, top drawn   ' ' blocked, nothing drawn
        //  the tile's walls: a '|' / '_' marks a cell on the east / north edge of a tile that has a drawn Right / Front wall (printed in a
        //  second grid)
        public static int Run(IGameFiles files, string map, int x0, int y0, int x1, int y1)
        {
            var s = Scene.Build(files, map, new Scene.Options { Rooms = false });
            x0 = Math.Max(0, x0); y0 = Math.Max(0, y0); x1 = Math.Min(s.W - 1, x1); y1 = Math.Min(s.H - 1, y1);
            int gw = s.Gnd.Width;
            var front = new bool[gw * s.Gnd.Height]; var right = new bool[gw * s.Gnd.Height];
            for (int t = 0; t < s.Count; t++) { if (s.Kind[t] == TriKind.WallFront) front[s.Src[t]] = true; else if (s.Kind[t] == TriKind.WallRight) right[s.Src[t]] = true; }
            var sb = new StringBuilder();
            sb.Append($"{s.Map}: cells {x0}..{x1} x {y0}..{y1}, north up. '.' walkable+top  ',' walkable, no top  '#' blocked+top  ' ' nothing\n");
            for (int y = y1; y >= y0; y--)
            {
                sb.Append(y.ToString("000", CultureInfo.InvariantCulture)).Append(' ');
                for (int x = x0; x <= x1; x++)
                {
                    int i = x + y * s.W; bool top = s.TopDrawn[(x / 2) + (y / 2) * gw];
                    sb.Append(s.Walk[i] ? (top ? '.' : ',') : (top ? '#' : ' '));
                }
                sb.Append('\n');
            }
            sb.Append("\nheights (the GAT floor, cells, rounded; '-' blocked and nothing drawn):\n");
            for (int y = y1; y >= y0; y--)
            {
                sb.Append(y.ToString("000", CultureInfo.InvariantCulture)).Append(' ');
                for (int x = x0; x <= x1; x++)
                {
                    int i = x + y * s.W; bool top = s.TopDrawn[(x / 2) + (y / 2) * gw];
                    if (!s.Walk[i] && !top) { sb.Append("  -"); continue; }
                    sb.Append(((int)Math.Round(s.Floor[i])).ToString(CultureInfo.InvariantCulture).PadLeft(3));
                }
                sb.Append('\n');
            }
            sb.Append("\nwalls ('|' a drawn Right wall on the tile's east edge, '_' a drawn Front wall on its north edge, '+' both; per TILE, north up):\n");
            for (int ty = (y1 / 2); ty >= y0 / 2; ty--)
            {
                sb.Append(ty.ToString("000", CultureInfo.InvariantCulture)).Append(' ');
                for (int tx = x0 / 2; tx <= x1 / 2; tx++)
                {
                    int k = tx + ty * gw; char c = front[k] && right[k] ? '+' : front[k] ? '_' : right[k] ? '|' : (s.TopDrawn[k] ? 'o' : ' ');
                    sb.Append(c);
                }
                sb.Append('\n');
            }
            Console.Write(sb.ToString());
            return 0;
        }
    }
    /// <summary>
    /// Architect's FIX MODE (design notes): for one map, the per-map FIX FILE the client applies when it builds that map
    /// (World/MapFix.cs, World/MapLoader.cs), made from the analysis; then the map analyzed again WITH the fix, built by the very code
    /// the client runs (World/GroundMeshBuilder.cs with the fix read back from the file's text), so the report says what the fix changed.
    /// Fix kinds (each says what it changes and why; a finding Architect cannot fix safely is only reported):
    ///   W  ground walls, the design's outer-wall rule (Walls.cs): an INTERIOR wall (both faces seen) drawn from both sides; a STEP (one face
    ///      under the higher side's floor) toward its low side only; an OUTER wall toward the play area only (see-through from the void);
    ///      a LEDGE by <see cref="Walls.LedgeRule"/> (FLAGGED). Edge / hidden walls: unchanged.
    ///   T  a top the loader culls as black void filler (World/GroundMeshBuilder.cs, the skip rule) under a walkable cell: drawn as it is
    ///      (black, as RO drew it): the player otherwise stands over nothing.
    ///   F  a hole within <see cref="Views.Thresholds.HoleHigh"/> cells of the walkable floor that the first-person eye sees (void.hole
    ///      high), on a map WITHOUT its own sky (RO showed black there; a map with its own sky shows that sky by design and is never
    ///      filled): a black floor at the lower of the tile's own corners and the nearest walkable floor, black skirts up to its drawn
    ///      neighbours.
    ///   report only: fp.head_clip, walk.wall_on_path, walk.walk_through, fp.low_ceiling, back.model_face (a model's faces: the fix file
    ///      changes no model), void.edge_seen, the rest: not clearly safe to change from a table of cells.
    /// </summary>
    public static class Fixer
    {
        public sealed class Section
        {
            public int Id; public readonly List<string> Items = new List<string>(); public readonly List<Walls.Wall> WallList = new List<Walls.Wall>();
            public int Walls, Tops, Fills; public float Area;          // cells² of surface the fix adds or turns
            public float X0 = float.MaxValue, Z0 = float.MaxValue, X1 = float.MinValue, Z1 = float.MinValue, Y0 = float.MaxValue, Y1 = float.MinValue;
            public UnityEngine.Vector3 Facing;                           // the summed normal of the faces the fix adds (where to look from)
            public UnityEngine.Vector3 Centre => new UnityEngine.Vector3((X0 + X1) / 2, (Y0 + Y1) / 2, (Z0 + Z1) / 2);
            public void Add(float x0, float z0, float x1, float z1, float y0, float y1)
            {
                X0 = Math.Min(X0, x0); Z0 = Math.Min(Z0, z0); X1 = Math.Max(X1, x1); Z1 = Math.Max(Z1, z1); Y0 = Math.Min(Y0, y0); Y1 = Math.Max(Y1, y1);
            }
        }

        /// <summary>What the ray refinement changed (<see cref="Refine"/>), for the report.</summary>
        public sealed class RefineStats
        {
            public int Rounds, DroppedBelow, KeptGameFace, Inner, InnerBoth, Unchanged, PrunedCount;
            public readonly HashSet<(int, bool)> Pruned = new HashSet<(int, bool)>();
            public readonly List<string> Examples = new List<string>();
        }

        /// <summary>
        /// The RAY REFINEMENT (2026-10-15): the rule's choice per wall (Walls.cs, by heights and drawn tops only) checked with
        /// rays on the map drawn WITH the fix (Check.cs: first person, Follow, the Table, below), and the design's INNER WALLS added:
        ///   - a face the fix would add that no PLAYER view sees (first person, Follow) is not added: seen only from the Table it looks out of
        ///     the map (c_tower4's outer facade; the design's outer-wall rule: the map stays see-through from outside), seen only from below it
        ///     would hang in the sky (showcase 1's floating faces; on geffen's bridges 284 turned steps were seen only from below);
        ///   - a turned wall whose game face is still seen from a view (its spot, traced on the fixed map: a step whose "covered" face shows
        ///     through an open side of its block, c_tower3's parapets) keeps the game face too (drawn from both sides): never a see-through
        ///     hole where players look;
        ///   - INNER (design note, 2026-10-15: "identify inner areas that SHOULD have walls visible from the inside but that don't, such as two of
        ///     the walls of the very first room"): a wall the rule leaves alone (edge / hidden: nobody walkable within 4 cells) whose face
        ///     toward a side that is not the void is seen from the first-person eye over walkable cells on that side (up to 40 cells) gets
        ///     that face; the game face stays unless it looks onto the void (the outer-wall rule: see-through from the void) or no view
        ///     sees it.
        /// The outer-wall rule's walls (outer) and the FLAGGED ledge rule's (ledge) are as the rule says (binding). Repeated on the
        /// rebuilt map until nothing changes (a face drawn can hide another's spot).
        /// </summary>
        public static bool Refine(Scene s0, Analysis a0, Scene s1, MapFix fix, RefineStats stats, int round, bool prune = false)
        {
            var g1 = new Grid(s1);
            // the game face G (the side the game draws the wall toward) and the other face X; the walls the game draws either way
            var walls = a0.WallSet.All.Where(w => w.Cls != Walls.Class.Outer && w.Span >= 1e-3f && (w.Drawn == Walls.Faces.TowardB || w.Drawn == Walls.Faces.TowardA)).ToList();
            var mode = new MapFix.WallMode[walls.Count]; var note = new string[walls.Count]; var inner = new bool[walls.Count];
            System.Threading.Tasks.Parallel.For(0, walls.Count, () => new Grid.RayState(s1.Count), (i, ls, st) =>
            {
                var w = walls[i]; var cur = w.Front ? fix.FrontMode(w.Tile) : fix.RightMode(w.Tile); mode[i] = cur;
                var G = w.Drawn; var X = G == Walls.Faces.TowardB ? Walls.Faces.TowardA : Walls.Faces.TowardB;
                bool xA = X == Walls.Faces.TowardA;
                var have = Walls.FacesOf(cur, G);
                bool hasX = (have & X) != 0, hasG = (have & G) != 0;
                bool ledge = w.Cls == Walls.Class.Ledge;
                string xName = w.Front ? (xA ? "south" : "north") : (xA ? "west" : "east"), gName = w.Front ? (xA ? "north" : "south") : (xA ? "east" : "west");
                if (prune)
                {
                    // the PRUNE pass: a face the fix added that no player sees any more (faces added since hid it) goes again (the wall
                    // back as the game draws it); a ledge's face toward the void stays (the FLAGGED ledge rule)
                    if (!hasX || ledge) return st;
                    int lp = FixCheck.Level(s1, g1, st, w, xA);
                    if (lp >= FixCheck.SeenFollow) return st;
                    mode[i] = MapFix.WallMode.Keep;
                    note[i] = $"{w.Name} ({w.Cls.ToString().ToLowerInvariant()}): left as the game draws it again: the added face toward {xName} is seen {FixCheck.LevelName[lp]} once the other faces are drawn";
                    return st;
                }
                if (stats.Pruned.Contains((w.Tile, w.Front))) return st;
                // after the first round faces are only ever added (X drawn, or the game face G put back): the rounds converge
                if (round > 0 && hasX && hasG) return st;
                bool ruleX = (w.Want & X) != 0;
                bool addX = hasX && round > 0; int lx = FixCheck.SeenNowhere;
                if (!addX && (ledge ? ruleX : FixCheck.NotVoid(s0, w, xA)))
                {
                    // added only where PLAYERS see it (first person or Follow): a face seen only from the Table, from outside the play area,
                    // looks out of the map (c_tower4, showcase run 1: the tower's 26-cell outer facade toward the map's edge strip, seen
                    // only from beyond the map's west edge): the design's outer-wall rule keeps the map see-through from outside. The FLAGGED
                    // ledge rule draws a drop toward the void to be seen from outside: a ledge's face counts from the Table.
                    if (ruleX) { lx = FixCheck.Level(s1, g1, st, w, xA); addX = lx >= (ledge ? FixCheck.SeenTable : FixCheck.SeenFollow); }
                    // the design's inner walls: a face the rule leaves out, seen from the walkable side it is missing from
                    if (!addX && !ledge && w.Seen && FixCheck.SeenFromWalkable(s1, g1, st, w, xA)) { addX = true; inner[i] = !ruleX; }
                }
                if (!addX) { mode[i] = MapFix.WallMode.Keep; if (ruleX) note[i] = $"{w.Name} ({w.Cls.ToString().ToLowerInvariant()}): not drawn: its face toward {xName} is seen {FixCheck.LevelName[lx]}"; return st; }
                bool keepG = hasG && round > 0;
                int lg = FixCheck.SeenNowhere;
                if (!keepG) { lg = FixCheck.Level(s1, g1, st, w, !xA); keepG = FixCheck.NotVoid(s0, w, !xA) && lg >= FixCheck.SeenTable; }
                mode[i] = Walls.ModeFor(keepG ? Walls.Faces.Both : X, G) ?? MapFix.WallMode.Keep;
                if (keepG && !hasG) note[i] = $"{w.Name} ({w.Cls.ToString().ToLowerInvariant()}): drawn from both sides, not turned: its game face toward {gName} is still seen in {FixCheck.LevelName[lg]}";
                return st;
            }, _ => { });
            bool changed = false; stats.Rounds++;
            for (int i = 0; i < walls.Count; i++)
            {
                var w = walls[i]; var cur = w.Front ? fix.FrontMode(w.Tile) : fix.RightMode(w.Tile);
                if (mode[i] == cur) continue;
                changed = true;
                var d = w.Front ? fix.Front : fix.Right; var cd = w.Front ? fix.FrontClass : fix.RightClass;
                if (mode[i] == MapFix.WallMode.Keep) { d.Remove(w.Tile); cd.Remove(w.Tile); if (prune) { stats.Pruned.Add((w.Tile, w.Front)); stats.PrunedCount++; } }
                else { d[w.Tile] = mode[i]; cd[w.Tile] = inner[i] || (cd.TryGetValue(w.Tile, out var was) && was == "inner") ? "inner" : w.Cls.ToString().ToLowerInvariant(); }
                if (note[i] != null && stats.Examples.Count < 40) stats.Examples.Add(note[i]);
            }
            return changed;
        }

        /// <summary>The refinement's counts against the rule's own choice (for the report).</summary>
        public static void CountRefinement(Analysis a0, MapFix rule, MapFix fix, RefineStats stats)
        {
            foreach (var w in a0.WallSet.All)
            {
                var r = w.Front ? rule.FrontMode(w.Tile) : rule.RightMode(w.Tile); var f = w.Front ? fix.FrontMode(w.Tile) : fix.RightMode(w.Tile);
                if (r == f) { if (r != MapFix.WallMode.Keep) stats.Unchanged++; continue; }
                if (r != MapFix.WallMode.Keep && f == MapFix.WallMode.Keep) stats.DroppedBelow++;
                else if (r != MapFix.WallMode.Keep && f == MapFix.WallMode.Both) stats.KeptGameFace++;
                else if (r == MapFix.WallMode.Keep) { stats.Inner++; if (f == MapFix.WallMode.Both) stats.InnerBoth++; }
            }
        }

        /// <summary>Where F fills are made: "day" (default, warch2) or "all" (--fill all).</summary>
        public static string FillThemes = "day";

        public static MapFix Generate(Scene s, Analysis a, byte[] gndBytes, byte[] gatBytes, out List<string> skipped)
        {
            skipped = new List<string>();
            var fix = new MapFix { Map = s.Map, Width = s.Gnd.Width, GndBytes = gndBytes.LongLength, GatBytes = gatBytes.LongLength, GndHash = MapFix.Hash(gndBytes), GatHash = MapFix.Hash(gatBytes) };
            // W: the walls
            foreach (var w in a.WallSet.All)
            {
                if (w.Cls == Walls.Class.Edge || w.Cls == Walls.Class.Hidden || w.Want == w.Drawn) continue;
                if (w.Span < 1e-3f) { skipped.Add($"{w.Name}: no height (nothing to draw)"); continue; }
                if (w.Drawn != Walls.Faces.TowardB && w.Drawn != Walls.Faces.TowardA) { skipped.Add($"{w.Name}: drawn {w.Drawn} (a twisted quad: its two triangles face opposite ways), left as it is"); continue; }
                // warch2: a wall the game draws toward A (a quad with no height at one end) is turned by "front" (MapFix.WallMode.Front)
                MapFix.WallMode mode = Walls.ModeFor(w.Want, w.Drawn) ?? MapFix.WallMode.Keep;
                if (mode == MapFix.WallMode.Keep) continue;
                string cls = w.Cls.ToString().ToLowerInvariant();
                if (w.Front) { fix.Front[w.Tile] = mode; fix.FrontClass[w.Tile] = cls; } else { fix.Right[w.Tile] = mode; fix.RightClass[w.Tile] = cls; }
            }
            // T: void-filler tops under walkable cells
            var gnd = s.Gnd; var black = new HashSet<string>(s.BlackTextures, StringComparer.Ordinal);
            for (int ty = 0; ty < gnd.Height; ty++)
                for (int tx = 0; tx < gnd.Width; tx++)
                {
                    var t = gnd.Tiles[tx + ty * gnd.Width]; if (t.Top < 0) continue;
                    var sf = gnd.Surfaces[t.Top]; if (sf.TextureIndex < 0) continue;
                    bool culled = black.Contains(gnd.Textures[sf.TextureIndex]) || (sf.R <= 8 && sf.G <= 8 && sf.B <= 8);
                    if (!culled) continue;
                    bool walk = false; for (int k = 0; k < 4; k++) walk |= s.Walkable(tx * 2 + (k & 1), ty * 2 + (k >> 1));
                    // warch2: not where a model's floor (or water) lies on it: drawn there it would z-fight (K5); the model is the floor
                    if (walk && CoplanarAt(s, a, tx, ty, (fx, fz) => TopY(t, fx, fz)) == null) fix.DrawTops.Add(tx + ty * gnd.Width);
                }
            KeepPockets(s, fix.DrawTops, "T", skipped, comp => comp.Any(k => TopFloats(s, k)));
            // F: seen holes beside the walkable floor, on a map without its own sky
            // warch2: only where Loki draws a bright sky behind the map ("day": World/SkyController.cs); indoors, at night and in dungeons
            // what is not drawn already shows near-black, as RO's black, and a black floor there only adds plates seen from the Table
            // (prt_in: 773 fills, walk.invisible_wall medium cells 7 -> 253): FillThemes ("day"; "all": every theme, as warch built it)
            if (s.OwnSky == null && a.HoleCells != null && (FillThemes == "all" || s.SkyTheme == "day"))
            {
                for (int i = 0; i < a.HoleCells.Sev.Length; i++)
                {
                    if (a.HoleCells.Sev[i] != 0) continue; // high: within HoleHigh cells
                    int x = i % s.W, y = i / s.W, tile = (x / 2) + (y / 2) * gnd.Width;
                    if (fix.Fills.ContainsKey(tile) || fix.DrawTops.Contains(tile) || x / 2 >= gnd.Width || y / 2 >= gnd.Height) continue;
                    bool walkTile = false; for (int k = 0; k < 4; k++) walkTile |= s.Walkable((x / 2) * 2 + (k & 1), (y / 2) * 2 + (k >> 1));
                    if (walkTile) continue; // a walkable tile with no top: a floor gap (fp.floor_gap: report only), not a hole to black out
                    var t = gnd.Tiles[tile];
                    float own = Math.Min(Math.Min(-t.H0, -t.H1), Math.Min(-t.H2, -t.H3)) * RoSpace.Scale;
                    float floor = float.MaxValue;
                    for (int dy = -3; dy <= 4; dy++) for (int dx = -3; dx <= 4; dx++) { int cx = (x / 2) * 2 + dx, cy = (y / 2) * 2 + dy; if (s.Walkable(cx, cy)) floor = Math.Min(floor, s.Floor[cx + cy * s.W]); }
                    float fy = Math.Min(own, floor == float.MaxValue ? own : floor);
                    // warch2: a model's floor (or water) at the fill's height would z-fight (K5): the black floor goes 0.1 cells under it
                    for (int k = 0; k < 3; k++) { var hit = CoplanarAt(s, a, x / 2, y / 2, (fx, fz) => fy); if (hit == null) break; fy = hit.Value - 0.1f; }
                    fix.Fills[tile] = fy;
                    fix.Why[tile] = "hole";
                }
                var keep = new HashSet<int>(fix.Fills.Keys); KeepPockets(s, keep, "F", skipped, comp => comp.Any(k => SkirtInView(s, fix, black, k, fix.Fills[k])));
                foreach (var k in fix.Fills.Keys.ToList()) if (!keep.Contains(k)) { fix.Fills.Remove(k); fix.Why.Remove(k); }
            }
            return fix;
        }

        /// <summary>The largest pocket (tiles, 4-connected) a T top or an F fill may belong to, and the share of its border tiles that must
        /// have a top the GAME draws (FLAGGED numbers, warch2).</summary>
        public const int PocketMaxTiles = 24; public const float PocketBorderDrawn = 0.75f;

        /// <summary>
        /// warch2 (the all-maps pass, a catastrophic class: dicastes01, mjolnir_04): black void-filler tops (T) and black hole floors (F)
        /// only where they close a POCKET in a floor the game draws: a group of tiles (4-connected) of at most <see cref="PocketMaxTiles"/>
        /// tiles whose border tiles have a top the game draws for at least <see cref="PocketBorderDrawn"/> of them (iz_int's cabin floor). A
        /// large black region is the map's void: Loki shows its sky there by design (the black-tinted / black-textured tops the loader leaves
        /// out); drawn black it stood as black slabs and walls in the sky (dicastes01: 82 % of the first-person view black; mjolnir_04: a
        /// black wall filling the view, its skirts up to black tops). the design's outer-wall rule: the void stays see-through.
        /// </summary>
        private static void KeepPockets(Scene s, HashSet<int> tiles, string kind, List<string> skipped, Func<List<int>, bool> inView)
        {
            int gw = s.Gnd.Width, gh = s.Gnd.Height; var seen = new HashSet<int>(); var drop = new List<int>(); int pockets = 0, dropped = 0, viewed = 0, viewedTiles = 0;
            foreach (int start in tiles.ToList())
            {
                if (seen.Contains(start)) continue;
                var comp = new List<int>(); var q = new Queue<int>(); q.Enqueue(start); seen.Add(start);
                while (q.Count > 0)
                {
                    int t = q.Dequeue(); comp.Add(t); int tx = t % gw, ty = t / gw;
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int x = tx + dx, y = ty + dy; if (x < 0 || y < 0 || x >= gw || y >= gh) continue;
                        int u = x + y * gw; if (tiles.Contains(u) && seen.Add(u)) q.Enqueue(u);
                    }
                }
                var set = new HashSet<int>(comp); var border = new HashSet<int>();
                foreach (int t in comp) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int x = t % gw + dx, y = t / gw + dy; if (x < 0 || y < 0 || x >= gw || y >= gh) { border.Add(-1 - border.Count); continue; } // the map's edge: not drawn
                    int u = x + y * gw; if (!set.Contains(u)) border.Add(u);
                }
                int drawn = border.Count(u => u >= 0 && s.TopDrawn[u]);
                if (comp.Count > PocketMaxTiles || drawn < PocketBorderDrawn * border.Count) { drop.AddRange(comp); dropped++; continue; }
                if (inView(comp)) { drop.AddRange(comp); viewed++; viewedTiles += comp.Count; continue; }
                pockets++;
            }
            foreach (int t in drop) tiles.Remove(t);
            if (dropped > 0) skipped.Add($"{kind}: {dropped} black regions left as the game's void ({drop.Count - viewedTiles} tiles: larger than {PocketMaxTiles} tiles or not enclosed by the drawn floor); {pockets} pockets kept");
            if (viewed > 0) skipped.Add($"{kind}: {viewed} pockets left as they are ({viewedTiles} tiles): drawn black they would stand in front of a player's face (K10: {(kind == "T" ? "a top floating above the walkable floor" : "a skirt across the eye of a walkable cell within 3 cells")})");
        }

        /// <summary>warch2 (K10): a black-tinted top drawn over walkable cells that would float more than 0.9 cells above the floor players
        /// walk on there (at the centre of any walkable cell of the tile): a black slab over their heads (ama_dun01, cave, aldeba_in).</summary>
        private static bool TopFloats(Scene s, int k)
        {
            var t = s.Gnd.Tiles[k]; int tx = k % s.Gnd.Width, ty = k / s.Gnd.Width;
            for (int c = 0; c < 4; c++)
            {
                int x = tx * 2 + (c & 1), y = ty * 2 + (c >> 1);
                if (s.Walkable(x, y) && TopY(t, (c & 1) * 0.5f + 0.25f, (c >> 1) * 0.5f + 0.25f) > s.Floor[x + y * s.W] + 0.9f) return true;
            }
            return false;
        }

        /// <summary>warch2 (K10): would a skirt World/GroundMeshBuilder.BuildFills draws for the fill of tile k at height fy (from the fill
        /// up to each neighbour whose top is drawn and higher, where no drawn GND wall closes the gap) cross the first-person eye of a
        /// walkable cell within 4 cells? (abbey03, prtg_cas01, man_fild02: a black wall in front of the player's face)</summary>
        private static bool SkirtInView(Scene s, MapFix fix, HashSet<string> black, int k, float fy)
        {
            var gnd = s.Gnd; int w = gnd.Width, h = gnd.Height, tx = k % w, ty = k / w; var t = gnd.Tiles[k];
            bool TopOn(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && (s.TopDrawn[x + y * w] || fix.DrawTops.Contains(x + y * w));
            bool WallOn(int surface) => surface >= 0 && surface < gnd.Surfaces.Length && gnd.Surfaces[surface].TextureIndex >= 0 && !black.Contains(gnd.Textures[gnd.Surfaces[surface].TextureIndex]);
            bool Crosses(float ex, float ez, float h0, float h1)
            {
                float top = Math.Max(-h0, -h1) * RoSpace.Scale; if (top <= fy + 1e-3f) return false;
                for (int dy = -4; dy <= 4; dy++) for (int dx = -4; dx <= 4; dx++)
                {
                    int x = (int)Math.Floor(ex) + dx, y = (int)Math.Floor(ez) + dy;
                    if (!s.Walkable(x, y)) continue;
                    float eye = s.Floor[x + y * s.W] + Views.EyeCells;
                    if (fy < eye - 0.05f && top > eye + 0.05f) return true;
                }
                return false;
            }
            if (TopOn(tx, ty + 1) && !WallOn(t.Front) && Crosses(tx * 2 + 1, ty * 2 + 2, gnd.Tiles[tx + (ty + 1) * w].H0, gnd.Tiles[tx + (ty + 1) * w].H1)) return true;
            if (TopOn(tx, ty - 1) && !WallOn(gnd.Tiles[tx + (ty - 1) * w].Front) && Crosses(tx * 2 + 1, ty * 2, gnd.Tiles[tx + (ty - 1) * w].H2, gnd.Tiles[tx + (ty - 1) * w].H3)) return true;
            if (TopOn(tx + 1, ty) && !WallOn(t.Right) && Crosses(tx * 2 + 2, ty * 2 + 1, gnd.Tiles[tx + 1 + ty * w].H0, gnd.Tiles[tx + 1 + ty * w].H2)) return true;
            if (TopOn(tx - 1, ty) && !WallOn(gnd.Tiles[tx - 1 + ty * w].Right) && Crosses(tx * 2, ty * 2 + 1, gnd.Tiles[tx - 1 + ty * w].H1, gnd.Tiles[tx - 1 + ty * w].H3)) return true;
            return false;
        }

        /// <summary>A GND tile's top height (Loki y, cells) at a point of the tile (fx, fz in 0..1), its four corners interpolated.</summary>
        private static float TopY(Loki.Formats.GndFile.Tile t, float fx, float fz)
        {
            // the top's two triangles as World/GroundMeshBuilder.cs builds them (p00 p01 p11 and p00 p11 p10: the diagonal p00-p11)
            float h = fz >= fx ? t.H0 + (t.H2 - t.H0) * fz + (t.H3 - t.H2) * fx : t.H0 + (t.H1 - t.H0) * fx + (t.H3 - t.H1) * fz;
            return -h * RoSpace.Scale;
        }

        /// <summary>warch2 (K5): the height of a model surface facing up within 0.05 cells, or of the water within its wave height + 0.05
        /// cells (World/MapLoader.cs animates the water up and down by it), of the surface the fix would draw over a tile at two or more of
        /// five points of the tile (an overlap; one point is a crossing, a shore), or null.</summary>
        private static float? CoplanarAt(Scene s, Analysis a, int tx, int ty, Func<float, float, float> y)
        {
            var col = new List<(float y, int t)>(); int close = 0; float at = 0f;
            float wave = Math.Abs(s.WaveHeightRo) * RoSpace.Scale;
            foreach (var (fx, fz) in new[] { (0.25f, 0.25f), (0.75f, 0.25f), (0.25f, 0.75f), (0.75f, 0.75f), (0.5f, 0.5f) })
            {
                a.G.Column(tx * 2 + fx * 2, ty * 2 + fz * 2, col, t => s.Kind[t] == TriKind.Model || s.Kind[t] == TriKind.Water);
                float h = y(fx, fz);
                foreach (var (hy, t) in col)
                {
                    bool water = s.Kind[t] == TriKind.Water;
                    if (Math.Abs(hy - h) < (water ? wave + 0.05f : 0.05f) && (water || Math.Abs(s.N[t * 3 + 1]) > 0.7f)) { close++; at = water ? hy - wave : hy; break; }
                }
                if (close >= 2) return at;
            }
            return null;
        }

        /// <summary>The fix's changes grouped into SECTIONS (changed tiles within 2 tiles of each other), ranked by the surface they add
        /// or turn (cells²): what the showcase looks at first.</summary>
        public static List<Section> Sections(Scene s, Analysis a, MapFix fix)
        {
            int gw = s.Gnd.Width, gh = s.Gnd.Height;
            var items = new List<(int tile, Action<Section> add)>();
            foreach (var w in a.WallSet.All)
            {
                var mode = w.Front ? fix.FrontMode(w.Tile) : fix.RightMode(w.Tile); if (mode == MapFix.WallMode.Keep) continue;
                var ww = w;
                items.Add((w.Tile, sec =>
                {
                    sec.Walls++; sec.Area += ww.Span * 2f; sec.WallList.Add(ww); sec.Items.Add($"{ww.Name} {ww.Cls.ToString().ToLowerInvariant()} -> {(mode == MapFix.WallMode.Both ? "both sides" : "turned")}, {ww.Span:0.#} cells high");
                    float lo = Math.Min(Math.Min(ww.A0, ww.A1), Math.Min(ww.B0, ww.B1)), hi = Math.Max(Math.Max(ww.A0, ww.A1), Math.Max(ww.B0, ww.B1));
                    if (ww.Front) sec.Add(ww.Tx * 2, ww.Ty * 2 + 2, ww.Tx * 2 + 2, ww.Ty * 2 + 2, lo, hi); else sec.Add(ww.Tx * 2 + 2, ww.Ty * 2, ww.Tx * 2 + 2, ww.Ty * 2 + 2, lo, hi);
                    // the added face looks away from the game face (south / west for a wall drawn north / east); weighted by its size
                    float sg = ww.Drawn == Walls.Faces.TowardA ? 1f : -1f;
                    sec.Facing += (ww.Front ? new UnityEngine.Vector3(0, 0, sg) : new UnityEngine.Vector3(sg, 0, 0)) * Math.Max(ww.Span, 0.1f);
                }));
            }
            foreach (var t in fix.DrawTops) { int tt = t; items.Add((t, sec => { sec.Tops++; sec.Area += 4f; sec.Items.Add($"top of tile {tt % gw},{tt / gw} drawn"); float y = s.Floor[Math.Min(s.W * s.H - 1, (tt % gw) * 2 + (tt / gw) * 2 * s.W)]; sec.Add((tt % gw) * 2, (tt / gw) * 2, (tt % gw) * 2 + 2, (tt / gw) * 2 + 2, y, y); sec.Facing += new UnityEngine.Vector3(0, 1, 0); })); }
            foreach (var kv in fix.Fills) { var f = kv; items.Add((f.Key, sec => { sec.Fills++; sec.Area += 4f; sec.Items.Add($"hole tile {f.Key % gw},{f.Key / gw} filled at y {f.Value:0.#}"); sec.Add((f.Key % gw) * 2, (f.Key / gw) * 2, (f.Key % gw) * 2 + 2, (f.Key / gw) * 2 + 2, f.Value, f.Value); sec.Facing += new UnityEngine.Vector3(0, 1, 0); })); }
            // union of tiles within 2 tiles (Chebyshev)
            var tiles = items.Select(i => i.tile).Distinct().ToList(); var parent = tiles.ToDictionary(t => t, t => t);
            int Find(int t) { while (parent[t] != t) { parent[t] = parent[parent[t]]; t = parent[t]; } return t; }
            var set = new HashSet<int>(tiles);
            foreach (var t in tiles)
                for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
                {
                    int x = t % gw + dx, y = t / gw + dy; if (x < 0 || y < 0 || x >= gw || y >= gh) continue;
                    int u = x + y * gw; if (!set.Contains(u)) continue;
                    int ra = Find(t), rb = Find(u); if (ra != rb) parent[ra] = rb;
                }
            var bySet = new Dictionary<int, Section>();
            foreach (var (tile, add) in items) { int r = Find(tile); if (!bySet.TryGetValue(r, out var sec)) bySet[r] = sec = new Section(); add(sec); }
            var list = bySet.Values.OrderByDescending(x => x.Area).ToList();
            for (int i = 0; i < list.Count; i++) list[i].Id = i + 1;
            return list;
        }

        /// <summary>architect fix: the fix file and its report for each named map (never every map: the design's rule; the tool applies nothing
        /// to the game: the fix file is copied into client/Assets/Loki/Resources/MapFixes/ by hand, reviewed, committed).</summary>
        public static int Run(IGameFiles files, string map, string outDir, string userData, bool refine = true)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var before = Program.AnalyzeOne(files, map, outDir, userData, true, null, "", out var s0, out var a0);
            byte[] gndB = files.Read("data\\" + s0.Rsw.GndFile), gatB = files.Read("data\\" + s0.Rsw.GatFile);
            var fix = Generate(s0, a0, gndB, gatB, out var skipped);
            // warch2: the ray refinement and the design's inner walls (Refine), on the map rebuilt with the fix until nothing changes
            var stats = new RefineStats();
            if (refine)
            {
                var rule = Generate(s0, a0, gndB, gatB, out _);
                // add rounds until nothing changes, then the prune pass; again (at most three cycles), never re-adding a pruned wall
                for (int cycle = 0, round = 0; cycle < 8; cycle++)
                {
                    for (int k = 0; k < 8; k++, round++)
                        if (!Refine(s0, a0, Scene.Build(files, map, new Scene.Options { UserData = userData, Fix = fix }), fix, stats, round)) break;
                    if (!Refine(s0, a0, Scene.Build(files, map, new Scene.Options { UserData = userData, Fix = fix }), fix, stats, round, true)) break;
                    round = Math.Max(round, 1);
                }
                CountRefinement(a0, rule, fix, stats);
            }
            var sections = Sections(s0, a0, fix);
            var comments = new List<string>
            {
                $"Architect's fix file for {s0.Map} (client/tools/vrmaps 'fix', {DateTime.Now:yyyy-MM-dd}): read by World/MapFix.cs, applied by World/MapLoader.cs when it builds {s0.Map}",
                "derived data only (tile numbers, wall sides, flags, heights in cells); it changes only what is drawn: the collider and the GAT stay the game's",
                $"W: ground walls by the design's outer-wall rule (tools/vrmaps/Walls.cs): interior -> both sides, step -> its low side only, outer -> toward the play area only, ledge -> '{Walls.LedgeRule}'" +
                    (refine ? "; checked with rays (a face no view sees is not added; a game face still seen is kept); inner -> a wall seen from the walkable side it is missing from (the design's inner walls)" : ""),
                "T: a void-filler top under a walkable cell, drawn; F: a seen hole beside the walkable floor on a map without its own sky, filled black",
                $"{fix.Summary()}; {sections.Count} sections",
            };
            string text = fix.ToText(comments);
            var back = MapFix.Parse(text, s0.Gnd.Width); // the client's own reader: what the game will apply
            if (back.ToText(comments) != text) throw new InvalidOperationException("the fix file does not read back as written");
            File.WriteAllText(Path.Combine(outDir, s0.Map + ".txt"), text);
            var after = Program.AnalyzeOne(files, map, outDir, userData, true, back, ".after", out var s1, out var a1);
            if (s1.FixBoth + s1.FixFlipped != back.Front.Count + back.Right.Count || s1.FixTops != back.DrawTops.Count || s1.FixFills != back.Fills.Count)
                throw new InvalidOperationException($"the builder applied {s1.FixBoth} + {s1.FixFlipped} walls, {s1.FixTops} tops, {s1.FixFills} fills; the file names {back.Front.Count + back.Right.Count}, {back.DrawTops.Count}, {back.Fills.Count}");
            string refineMd = !refine ? "" :
                $"\n## The ray refinement and the design's inner walls ({stats.Rounds} rounds)\n\nAgainst the rule's own choice: {stats.Unchanged} walls as the rule chose; " +
                $"{stats.DroppedBelow} left as the game draws them (the face the rule adds is seen by no view: only from below, or nowhere); " +
                $"{stats.KeptGameFace} drawn from both sides instead of turned (the game face is still seen); {stats.Inner} inner walls added ({stats.InnerBoth} of them from both sides).\n" +
                string.Concat(stats.Examples.Take(12).Select(x => "\n- " + x)) + "\n";
            File.WriteAllText(Path.Combine(outDir, s0.Map + ".fix.md"), Markdown(s0, a0, a1, before, after, fix, sections, skipped) + refineMd);
            File.WriteAllText(Path.Combine(outDir, s0.Map + ".showcase.tsv"), Showcase(s0, sections));
            // warch2: the showcase's hotspots, spots for the game's views, cameras (Showcase2.cs)
            var hots = Showcase2.Hotspots(s0, a0, back); Showcase2.Spots(s1, hots);
            File.WriteAllText(Path.Combine(outDir, s0.Map + ".showcase2.tsv"), Showcase2.Cameras(s0, hots));
            File.WriteAllText(Path.Combine(outDir, s0.Map + ".spots.tsv"), Showcase2.SpotsTsv(s0, hots));
            // warch2: the catastrophic-mistake check (Check.cs) on every fixed map
            var check = FixCheck.Run(s0, a0, before, s1, a1, after, back);
            File.WriteAllText(Path.Combine(outDir, s0.Map + ".check.md"), FixCheck.Markdown(check));
            File.AppendAllText(Path.Combine(outDir, s0.Map + ".fix.md"), $"\n## The catastrophic-mistake check: {check.Verdict}\n\n{check.Line()} (`{s0.Map}.check.md`)\n");
            Console.WriteLine($"{s0.Map}: fix file {Path.Combine(outDir, s0.Map + ".txt")}: {fix.Summary()}; {sections.Count} sections; report {s0.Map}.fix.md; {sw.ElapsedMilliseconds / 1000f:0.0} s" +
                (refine ? $"; refinement ({stats.Rounds} rounds): as the rule {stats.Unchanged}, not drawn (seen by no view) {stats.DroppedBelow}, both sides instead of turned {stats.KeptGameFace}, inner walls {stats.Inner}" : "; the rule only (--rule-only)"));
            Console.WriteLine(check.Line());
            return check.Fail ? 3 : 0;
        }

        private static string Counts(MapReport r, string kind)
        {
            var fs = r.Findings.Where(f => f.Kind == kind).ToList();
            if (fs.Count == 0) return "0";
            return string.Join(" / ", MapReport.SeverityOrder.Select(sv => fs.Count(f => f.Severity == sv))) + $" ({fs.Sum(f => f.Count)} cells)";
        }

        public static string Markdown(Scene s, Analysis a0, Analysis a1, MapReport before, MapReport after, MapFix fix, List<Section> sections, List<string> skipped)
        {
            var sb = new StringBuilder(); var inv = CultureInfo.InvariantCulture;
            sb.Append($"# {s.Map}: Architect's fix\n\n");
            sb.Append($"The fix file `{s.Map}.txt` (copy it to `client/Assets/Loki/Resources/MapFixes/{s.Map}.txt`): {fix.Summary()}. Made for gnd {fix.GndBytes} bytes `{fix.GndHash}`, gat {fix.GatBytes} bytes `{fix.GatHash}` (FNV-1a 64): the client applies it only to these very files.\n\n");
            sb.Append("## What it changes, and why\n\n");
            var w0 = a0.WallSet;
            int both = fix.Front.Values.Concat(fix.Right.Values).Count(m => m == MapFix.WallMode.Both), flip = fix.Front.Values.Concat(fix.Right.Values).Count(m => m == MapFix.WallMode.Flip);
            sb.Append($"- **Ground walls** (W, {both} both sides, {flip} turned): the game draws every GND wall from one side (north / east: `World/GroundMeshBuilder.cs`, `Shaders/LokiGround.shader` Cull Back). the design's outer-wall rule: {w0.Summary()}. Interior walls (both faces seen) are drawn from both sides; steps (one face under the higher side's floor) toward their low side only; outer walls toward the play area only (see-through from the void, by design); ledges by the FLAGGED ledge rule '{Walls.LedgeRule}'.\n");
            sb.Append($"- **Floors culled as void** (T, {fix.DrawTops.Count}): tops the loader leaves out as black void filler under walkable cells, drawn black as RO drew them.\n");
            sb.Append($"- **Holes** (F, {fix.Fills.Count}; Loki's sky here: {s.SkyTheme}): " + (s.OwnSky != null ? $"none: the map has its own sky ({s.OwnSky}); RO cleared the screen to it and drew its clouds under the map, so what is not drawn shows the designed sky, never a hole to fill.\n" : "seen holes within 2 cells of the walkable floor, filled black (RO showed black there).\n"));
            sb.Append("- **Report only** (not clearly safe from a table of cells): head clips, walls on paths, walking through models, low ceilings, model faces seen from behind (the fix file changes no model), the void seen at the map's edge.\n");
            if (skipped.Count > 0) sb.Append($"- Left as they are: {skipped.Count} ({string.Join("; ", skipped.Take(5))}).\n");
            sb.Append("\n## Before / after (the same analysis, the ground built with the fix by the client's own builder)\n\n| kind | before (high / medium / low / info) | after |\n|---|---|---|\n");
            foreach (var k in before.Findings.Select(f => f.Kind).Concat(after.Findings.Select(f => f.Kind)).Distinct().OrderBy(k => k, StringComparer.Ordinal))
                sb.Append($"| {k} | {Counts(before, k)} | {Counts(after, k)} |\n");
            var w1 = a1.WallSet;
            sb.Append($"\nGround walls before: {w0.Summary()}.\nGround walls after: {w1.Summary()}.\n");
            sb.Append($"\n## The most changed sections ({sections.Count}; ranked by the wall / floor surface added or turned, cells²)\n\n| # | cells (x0,y0 - x1,y1) | height (cells) | walls | tops | fills | surface | look from |\n|---|---|---|---|---|---|---|---|\n");
            foreach (var sec in sections.Take(12))
                sb.Append($"| {sec.Id} | {sec.X0:0},{sec.Z0:0} - {sec.X1:0},{sec.Z1:0} | {sec.Y0:0.#} .. {sec.Y1:0.#} | {sec.Walls} | {sec.Tops} | {sec.Fills} | {sec.Area:0} | {Dir(sec.Facing)} |\n");
            return sb.ToString();
        }

        private static string Dir(UnityEngine.Vector3 v)
        {
            if (v.sqrMagnitude < 1e-6f) return "above";
            string ns = v.z < -0.3f * v.magnitude ? "south" : v.z > 0.3f * v.magnitude ? "north" : "", ew = v.x < -0.3f * v.magnitude ? "west" : v.x > 0.3f * v.magnitude ? "east" : "";
            return (ns + (ns.Length > 0 && ew.Length > 0 ? "-" : "") + ew).Length == 0 ? "above" : ns + (ns.Length > 0 && ew.Length > 0 ? "-" : "") + ew;
        }

        /// <summary>The showcase's cameras for the most changed sections (the client's showcase smoke reads them): one line per shot,
        /// name, from x y z, at x y z (map cells, y up), field of view: a three-quarter look from the side the fix added faces on,
        /// framing the section, and a closer one at a player's eye height.</summary>
        public static string Showcase(Scene s, List<Section> sections)
        {
            var sb = new StringBuilder(); var inv = CultureInfo.InvariantCulture;
            sb.Append("# Architect showcase cameras for ").Append(s.Map).Append(": name, from x y z, at x y z (map cells, y up), fov; the most changed sections first\n");
            foreach (var sec in sections.Take(6))
            {
                var c = sec.Centre; var f = sec.Facing; f.y = 0; if (f.sqrMagnitude < 1e-6f) f = new UnityEngine.Vector3(0, 0, -1); f = f.normalized;
                float size = Math.Max(Math.Max(sec.X1 - sec.X0, sec.Z1 - sec.Z0), 4f);
                float d = size * 0.9f + 6f;
                var side = new UnityEngine.Vector3(-f.z, 0, f.x) * (d * 0.45f);
                var from = c + f * d + side + new UnityEngine.Vector3(0, d * 0.45f + (sec.Y1 - sec.Y0) * 0.5f, 0);
                Line(sb, $"section{sec.Id}", from, c, 50f);
                // a player's look: eye height over the section's low side, 8 cells out
                var eye = c + f * Math.Max(6f, size * 0.5f + 4f); eye.y = sec.Y0 + Views.EyeCells;
                Line(sb, $"section{sec.Id}_eye", eye, new UnityEngine.Vector3(c.x, (sec.Y0 + sec.Y1) * 0.5f, c.z), 75f);
                // close-ups of its two tallest changed walls: 7 cells out on the side the fix added a face on, 3 cells over its top
                int k = 0;
                foreach (var w in sec.WallList.OrderByDescending(x => x.Span).Take(2))
                {
                    var n = w.Front ? new UnityEngine.Vector3(0, 0, -1) : new UnityEngine.Vector3(-1, 0, 0);
                    float lo = Math.Min(Math.Min(w.A0, w.A1), Math.Min(w.B0, w.B1)), hi = Math.Max(Math.Max(w.A0, w.A1), Math.Max(w.B0, w.B1));
                    var mid = new UnityEngine.Vector3(w.Cx, (lo + hi) * 0.5f, w.Cy);
                    Line(sb, $"section{sec.Id}_wall{++k}", mid + n * 7f + new UnityEngine.Vector3(n.z * 2f, hi - mid.y + 3f, -n.x * 2f), mid, 60f);
                }
            }
            return sb.ToString();
        }

        private static void Line(StringBuilder sb, string name, UnityEngine.Vector3 from, UnityEngine.Vector3 at, float fov)
        {
            var inv = CultureInfo.InvariantCulture;
            sb.Append(name).Append('\t').Append(string.Join(" ", new[] { from.x, from.y, from.z }.Select(v => v.ToString("0.##", inv)))).Append('\t')
              .Append(string.Join(" ", new[] { at.x, at.y, at.z }.Select(v => v.ToString("0.##", inv)))).Append('\t').Append(fov.ToString("0", inv)).Append('\n');
        }
    }
}
