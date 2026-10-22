using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Loki.World;
using UnityEngine;

namespace Loki.VrMaps
{
    /// <summary>
    /// Architect's CATASTROPHIC-MISTAKE CHECK (2026-10-15; design note: "Verify across additional maps"): run by the fix mode on
    /// every map it fixes, on the map as the game draws it (the plain scene) and with the fix (the fixed scene, built by the client's own
    /// builder from the fix file's text). It looks for the kinds of mistake warch's showcase 1 caught by eye (design notes: faces
    /// that hang in the sky seen from below) and the others the design's task names: walls blocking walkable paths or views, missing floors,
    /// z-fighting; the design's OUTER-WALL RULE (an outer wall stays see-through from the void side); and the INNER-WALL rule (a wall that
    /// borders a walkable area is drawn toward the walkable side, as the start ship's cabin walls).
    /// Every face is traced with rays (Grid.Ray: the renderer's rules, culled backs passed, cut-out texels passed, the room mask of an
    /// indoor map) from the views players have: first person (the eye over walkable cells in front of it), Follow (that eye's camera,
    /// back and up), the Table (the diorama from outside and above: five headings by four heights); then from below the map.
    ///   K1 every face the fix ADDS: a face of a wall drawn from both sides whose added half is seen only from below is FLOATING
    ///      (showcase 1's mistake): FAIL.
    ///   K2 every face the fix REMOVES (a turned wall's game face), traced on the FIXED map: a spot players still see shows the wall's
    ///      back (a see-through hole where the game drew a wall): FAIL, unless the wall is OUTER (the design's rule: see-through from the void).
    ///   K3 walls on walkable paths: an added face standing on an edge between two walkable cells, higher than the step the server
    ///      lets a unit take there (the GAT): LOOK (counted, the worst listed; the game draws such walls from its own side too).
    ///   K4 missing floors: a ground top the game draws that the fixed map does not: FAIL.
    ///   K5 z-fighting: a surface the fix adds (a void-filler top drawn, a hole's black floor) within 0.03 cells of another surface facing
    ///      the same way (models, water, the ground) at two or more of five points of its tile (one point is a crossing, a shore): FAIL.
    ///   K6 holes: seen holes beside the walkable floor before -> after (void.hole high + medium cells).
    ///   K7 findings that got WORSE with the fix (high + medium cells of a kind up by more than 10 % and 20 cells): LOOK.
    ///   K8 the outer-wall rule after the fix: outer walls still drawn toward the void: FAIL (unless the fix cannot turn them: listed).
    ///   K9 the design's inner walls: walls with a face missing toward a side that is not the void, seen from the first-person eye over walkable
    ///      cells on that side (up to 40 cells away): before -> after (after the fix: LOOK when any is left).
    ///   K10 black walls in the view (warch2, the all-maps pass: dicastes01, mjolnir_04): a black surface the fix adds (a void-filler top
    ///      drawn, a hole's black floor or its skirt) standing in front of a player's face: a skirt crossing the first-person eye of a
    ///      walkable cell within 3 cells (below the eye at one end, above it at the other), a top floating more than a cell above the
    ///      walkable floor under it: FAIL.
    /// </summary>
    public static class FixCheck
    {
        /// <summary>"tx,ty,N|E,A|B": print how that wall face is seen (hand checks; ARCHITECT_DEBUG_FACE).</summary>
        public static string Debug = Environment.GetEnvironmentVariable("ARCHITECT_DEBUG_FACE");

        public sealed class Result
        {
            public string Map;
            public int Added, AddedNormal, AddedFp, AddedBelowOnly, AddedUnseen, Doubled, DoubledFloating, TableOnlyAdded;
            public int Removed, RemovedOuter, RemovedHidden, Regressions, TableOnlyHoles;
            public int PathWalls, PathWallsBefore; public float PathWorst;
            public int LostTops, ZFight, NewSurfaces;
            public long HolesBefore, HolesAfter;
            public readonly List<string> Worse = new List<string>();
            public int OuterTowardVoid; public readonly List<string> OuterLeft = new List<string>();
            public int InnerBefore, InnerAfter;
            public int BlackWalls;
            public readonly List<string> Examples = new List<string>();
            /// <summary>FAIL: a face drawn twice whose added half is seen only from below (showcase 1's mistake), a removed face whose spot
            /// players still see (a see-through hole), a floor lost, z-fighting, an outer wall left toward the void.</summary>
            public bool Fail => DoubledFloating > 0 || Regressions > 0 || LostTops > 0 || ZFight > 0 || OuterTowardVoid > 0 || BlackWalls > 0;
            public bool Look => PathWalls > 0 || Worse.Count > 0 || InnerAfter > 0 || TableOnlyHoles > 0 || TableOnlyAdded > 0;
            public string Verdict => Fail ? "FAIL" : Look ? "LOOK" : "OK";
            public string Line() =>
                $"CHECK {Map}: {Verdict}: K1 added faces {Added} (seen in the views {AddedNormal}, in first person {AddedFp}; only from below {AddedBelowOnly}, nowhere {AddedUnseen}; both sides {Doubled}, FLOATING {DoubledFloating}; seen only from the Table, out of the map {TableOnlyAdded}); " +
                $"K2 removed faces {Removed} (SEE-THROUGH where players play {Regressions}, from the Table only {TableOnlyHoles}; toward the void by the rule {RemovedOuter}; hidden {RemovedHidden}); K3 walls standing on walkable paths {PathWalls} (the game's own {PathWallsBefore}; worst {PathWorst:0.#} cells above the floor); " +
                $"K4 floors lost {LostTops}; K5 z-fighting {ZFight} of {NewSurfaces}; K6 seen holes {HolesBefore} -> {HolesAfter} cells; K7 worse {Worse.Count}; K8 outer walls toward the void {OuterTowardVoid}; K9 walls invisible from their walkable side {InnerBefore} -> {InnerAfter}; K10 black walls in the view {BlackWalls}";
        }

        /// <summary>Visibility levels of a face: where it can be seen from at best.</summary>
        public const int SeenNowhere = 0, SeenBelow = 1, SeenTable = 2, SeenFollow = 3, SeenFirstPerson = 4;
        public static readonly string[] LevelName = { "nowhere", "only from below / outside, low", "the Table", "Follow", "first person" };

        public static Result Run(Scene s0, Analysis a0, MapReport before, Scene s1, Analysis a1, MapReport after, MapFix fix)
        {
            var r = new Result { Map = s0.Map };
            var g0 = new Grid(s0); var g1 = new Grid(s1);
            // ---- K1 / K2: the faces the fix adds and removes, traced on the fixed map
            var changed = a0.WallSet.All.Where(w => (w.Front ? fix.FrontMode(w.Tile) : fix.RightMode(w.Tile)) != MapFix.WallMode.Keep).ToList();
            var addLv = new int[changed.Count]; var remLv = new int[changed.Count]; var addA = new bool[changed.Count];
            Parallel.For(0, changed.Count, () => new Grid.RayState(s1.Count), (i, ls, st) =>
            {
                // the face the fix adds is the one the game does not draw; it removes the game face when it turns the wall
                var w = changed[i]; var m = w.Front ? fix.FrontMode(w.Tile) : fix.RightMode(w.Tile);
                bool gameA = w.Drawn == Walls.Faces.TowardA; addA[i] = !gameA;
                addLv[i] = Level(s1, g1, st, w, !gameA);
                remLv[i] = (Walls.FacesOf(m, w.Drawn) & w.Drawn) == 0 ? Level(s1, g1, st, w, gameA) : -1;
                return st;
            }, _ => { });
            for (int i = 0; i < changed.Count; i++)
            {
                var w = changed[i]; int lv = addLv[i]; r.Added++;
                if (lv >= SeenTable) r.AddedNormal++; if (lv == SeenFirstPerson) r.AddedFp++;
                if (lv == SeenBelow) r.AddedBelowOnly++; if (lv == SeenNowhere) r.AddedUnseen++;
                if (lv == SeenTable && w.Cls != Walls.Class.Ledge && w.Cls != Walls.Class.Outer) { r.TableOnlyAdded++; if (r.Examples.Count < 15) r.Examples.Add($"K1 seen only from the Table (out of the map): {w.Name} ({w.Cls.ToString().ToLowerInvariant()})"); }
                if (remLv[i] < 0)
                {
                    r.Doubled++;
                    if (lv == SeenBelow) { r.DoubledFloating++; if (r.Examples.Count < 12) r.Examples.Add($"K1 FLOATING: {w.Name} ({w.Cls.ToString().ToLowerInvariant()}) drawn from both sides: the added face toward {(w.Front ? (addA[i] ? "south" : "north") : (addA[i] ? "west" : "east"))} is seen only from below"); }
                    continue;
                }
                r.Removed++;
                if (remLv[i] < SeenTable) { r.RemovedHidden++; continue; }
                if (w.Cls == Walls.Class.Outer || !NotVoid(s0, w, !addA[i])) { r.RemovedOuter++; continue; } // the outer-wall rule: see-through from the void
                if (remLv[i] == SeenTable) { r.TableOnlyHoles++; if (r.Examples.Count < 18) r.Examples.Add($"K2 see-through from the Table only: {w.Name} ({w.Cls.ToString().ToLowerInvariant()})"); continue; }
                r.Regressions++;
                if (r.Examples.Count < 24) r.Examples.Add($"K2 SEE-THROUGH: {w.Name} ({w.Cls.ToString().ToLowerInvariant()}) turned: its game face is gone, and its spot is seen in {LevelName[remLv[i]]} (the new face: {LevelName[lv]})");
            }
            // ---- K3 walls on walkable paths (added faces only; and the game's own drawn faces, for scale)
            foreach (var w in changed)
            {
                float span = PathSpan(s0, w);
                if (span > 0f) { r.PathWalls++; if (span > r.PathWorst) r.PathWorst = span; if (r.Examples.Count < 30) r.Examples.Add($"K3 wall on a walkable path: {w.Name}, {span:0.#} cells above the higher floor"); }
            }
            foreach (var w in a0.WallSet.All) if (PathSpan(s0, w) > 0f) r.PathWallsBefore++;
            // ---- K4 floors
            int gw = s0.Gnd.Width;
            for (int k = 0; k < s0.TopDrawn.Length; k++) if (s0.TopDrawn[k] && !s1.TopDrawn[k]) { r.LostTops++; if (r.Examples.Count < 36) r.Examples.Add($"K4 FLOOR LOST: tile {k % gw},{k / gw}"); }
            // ---- K5 z-fighting of the surfaces the fix adds (tops drawn, hole floors)
            var newTiles = new HashSet<int>(fix.DrawTops); foreach (var k in fix.Fills.Keys) newTiles.Add(k);
            r.NewSurfaces = newTiles.Count;
            var col = new List<(float y, int t)>();
            foreach (int k in newTiles)
            {
                int tx = k % gw, ty = k / gw; bool fight = false; string what = ""; int close = 0;
                foreach (var (fx, fz) in new[] { (0.25f, 0.25f), (0.75f, 0.25f), (0.25f, 0.75f), (0.75f, 0.75f), (0.5f, 0.5f) })
                {
                    float x = tx * 2 + fx * 2, z = ty * 2 + fz * 2;
                    g1.Column(x, z, col);
                    // the fix's surface here: a ground top of this very tile (the drawn void-filler top or the hole's black floor)
                    foreach (var (y, t) in col)
                    {
                        if (s1.Kind[t] != TriKind.Top || s1.Src[t] != k) continue;
                        foreach (var (y2, t2) in col)
                        {
                            if (t2 == t || (s1.Kind[t2] == TriKind.Top && s1.Src[t2] == k)) continue;
                            if (Math.Abs(y2 - y) > 0.03f) continue;
                            float ny = s1.N[t2 * 3 + 1];
                            if (s1.Kind[t2] == TriKind.Water || (Math.Abs(ny) > 0.7f && Math.Sign(ny) == Math.Sign(s1.N[t * 3 + 1]))) { fight = true; what = s1.Owner(t2); break; }
                        }
                        if (fight) break;
                    }
                    // a z-fight is an OVERLAP: the two surfaces within 0.03 cells at two or more of the five points (a single point is a
                    // crossing: a floor going into water at a shore, as the game's own tops do)
                    if (fight) { close++; fight = false; if (close >= 2) { fight = true; break; } }
                }
                if (fight) { r.ZFight++; if (r.Examples.Count < 42) r.Examples.Add($"K5 Z-FIGHT: tile {tx},{ty} ({(fix.DrawTops.Contains(k) ? "top drawn" : "hole floor")}) with {what}"); }
            }
            // ---- K6 holes, K7 worse kinds
            r.HolesBefore = Cells(before, "void.hole", 2); r.HolesAfter = Cells(after, "void.hole", 2);
            foreach (var kind in before.Findings.Select(f => f.Kind).Concat(after.Findings.Select(f => f.Kind)).Distinct().OrderBy(x => x, StringComparer.Ordinal))
            {
                long b = Cells(before, kind, 2), a = Cells(after, kind, 2);
                if (a > b + 20 && a > b * 1.1) r.Worse.Add($"{kind} high + medium cells {b} -> {a}");
            }
            // ---- K10 black surfaces the fix adds, standing in front of a player's face
            {
                var tiles = new HashSet<int>();
                for (int t = 0; t < s1.Count; t++)
                {
                    bool fixBlack = s1.Kind[t] == TriKind.Top && fix.DrawTops.Contains(s1.Src[t])
                        || (s1.Kind[t] != TriKind.Model && s1.Kind[t] != TriKind.Water && s1.MatOf[t] >= 0 && string.IsNullOrEmpty(s1.Mats[s1.MatOf[t]].Texture));
                    if (!fixBlack) continue;
                    float lo = float.MaxValue, hi = float.MinValue, cx = 0f, cz = 0f;
                    for (int v = 0; v < 3; v++) { float y = s1.P[t * 9 + v * 3 + 1]; lo = Math.Min(lo, y); hi = Math.Max(hi, y); cx += s1.P[t * 9 + v * 3] / 3f; cz += s1.P[t * 9 + v * 3 + 2] / 3f; }
                    bool front = false;
                    // a top (a void-filler top drawn: a floor under walkable cells, ramps included; a hole's black floor: level): only where
                    // it floats above the floor players walk on there by more than a cell (a slab between their feet and their eyes)
                    if (s1.Kind[t] == TriKind.Top)
                    {
                        int x = (int)Math.Floor(cx), y = (int)Math.Floor(cz);
                        front = s1.Walkable(x, y) && (lo + hi) / 2f > s1.Floor[x + y * s1.W] + 1f;
                    }
                    else for (int dy = -3; dy <= 3 && !front; dy++) for (int dx = -3; dx <= 3 && !front; dx++)
                    {
                        int x = (int)Math.Floor(cx) + dx, y = (int)Math.Floor(cz) + dy;
                        if (!s1.Walkable(x, y)) continue;
                        float eye = s1.Floor[x + y * s1.W] + Views.EyeCells;
                        front = lo < eye - 0.05f && hi > eye + 0.05f;
                    }
                    if (front && tiles.Add(s1.Src[t]) && r.Examples.Count < 60) r.Examples.Add($"K10 BLACK WALL IN THE VIEW: {s1.Owner(t)} ({(s1.Kind[t] == TriKind.Top ? "top" : "skirt")}, {lo:0.#} to {hi:0.#} cells)");
                }
                r.BlackWalls = tiles.Count;
            }
            // ---- K8 the outer-wall rule after the fix
            foreach (var w in a1.WallSet.All.Where(x => x.Cls == Walls.Class.Outer && x.Extra != Walls.Faces.None))
            {
                if (w.Span < 1e-3f || (w.Drawn != Walls.Faces.TowardB && w.Drawn != Walls.Faces.TowardA)) { r.OuterLeft.Add($"{w.Name} (drawn {w.Drawn}, span {w.Span:0.##}: the fix cannot turn it)"); continue; }
                r.OuterTowardVoid++; if (r.Examples.Count < 48) r.Examples.Add($"K8 OUTER WALL TOWARD THE VOID: {w.Name}");
            }
            // ---- K9 the design's inner walls: before (the game's map) and after (the fixed map)
            var innerB = InnerMissing(s0, g0, a0.WallSet, s0); var innerA = InnerMissing(s1, g1, a1.WallSet, s0); // the void is what the GAME leaves undrawn: a hole the fix fills black stays the void (the outer-wall rule)
            r.InnerBefore = innerB.Count; r.InnerAfter = innerA.Count;
            foreach (var x in innerA.Take(6)) if (r.Examples.Count < 54) r.Examples.Add($"K9 still invisible from its walkable side: {x}");
            return r;
        }

        /// <summary>K9: the walls of a scene with a face missing toward a side that is not the void (its tile's top drawn, or a walkable
        /// cell beside the wall) that the first-person eye over walkable cells on that side sees (up to 40 cells away).</summary>
        public static List<string> InnerMissing(Scene s, Grid g, Walls ws, Scene voidRef)
        {
            var list = ws.All.Where(w => w.Seen).ToList(); var found = new string[list.Count];
            Parallel.For(0, list.Count, () => new Grid.RayState(s.Count), (i, ls, st) =>
            {
                var w = list[i];
                foreach (var towardA in new[] { true, false })
                {
                    var side = towardA ? Walls.Faces.TowardA : Walls.Faces.TowardB;
                    if ((w.Drawn & side) != 0 || !NotVoid(voidRef, w, towardA)) continue;
                    if (SeenFromWalkable(s, g, st, w, towardA)) { found[i] = $"{w.Name} toward {(w.Front ? (towardA ? "south" : "north") : (towardA ? "west" : "east"))} ({w.Cls.ToString().ToLowerInvariant()})"; break; }
                }
                return st;
            }, _ => { });
            return found.Where(x => x != null).ToList();
        }

        /// <summary>A wall's side is not the out-of-bounds void: the tile on that side has a drawn top, or a cell beside the wall on that
        /// side is walkable (the design's outer-wall rule: the side facing the void stays see-through). Always asked of the map as the GAME draws
        /// it: a hole the fix fills black is still the void.</summary>
        public static bool NotVoid(Scene s, Walls.Wall w, bool towardA)
        {
            int gw = s.Gnd.Width; int tile = towardA ? w.Tile : (w.Front ? w.Tile + gw : w.Tile + 1);
            if (tile >= 0 && tile < s.TopDrawn.Length && s.TopDrawn[tile]) return true;
            for (int k = 0; k < 2; k++)
            {
                int x, y;
                if (w.Front) { x = w.Tx * 2 + k; y = towardA ? w.Ty * 2 + 1 : w.Ty * 2 + 2; }
                else { y = w.Ty * 2 + k; x = towardA ? w.Tx * 2 + 1 : w.Tx * 2 + 2; }
                if (s.Walkable(x, y)) return true;
            }
            return false;
        }

        /// <summary>The cells of a kind's findings of severity high (and medium: <paramref name="upTo"/> 2).</summary>
        private static long Cells(MapReport r, string kind, int upTo) => r.Findings.Where(f => f.Kind == kind && MapReport.Rank(f.Severity) < upTo).Sum(f => (long)f.Count);

        /// <summary>K3: how far a wall rises above the higher of the two floors the server lets a unit walk across its edge (0: not a wall
        /// in the way): an edge whose two cells (on either side, for either of the wall's two cells) are both walkable.</summary>
        public static float PathSpan(Scene s, Walls.Wall w)
        {
            float worst = 0f;
            for (int k = 0; k < 2; k++)
            {
                int ax, ay, bx, by;
                if (w.Front) { ax = bx = w.Tx * 2 + k; ay = w.Ty * 2 + 1; by = w.Ty * 2 + 2; }
                else { ay = by = w.Ty * 2 + k; ax = w.Tx * 2 + 1; bx = w.Tx * 2 + 2; }
                if (!s.Walkable(ax, ay) || !s.Walkable(bx, by)) continue;
                float hA = k == 0 ? w.A0 : w.A1, hB = k == 0 ? w.B0 : w.B1;
                float fa = s.Floor[ax + ay * s.W], fb = s.Floor[bx + by * s.W];
                // the wall STANDS ON the path only when its top rises above the higher of the two floors the server walks on: a top at or
                // below it is a riser (a step in the floor, as stairs draw them) or lies under a bridge (geffen, aldebaran: the GND's
                // canal under a walkway): not in the way
                float over = Math.Max(hA, hB) - Math.Max(fa, fb);
                if (over > Views.Thresholds.WallOnPathMedium && over > worst) worst = over;
            }
            return worst;
        }

        /// <summary>A face's sample points (3 along x 3 up, just in front of it) and its normal; empty when the wall has no height.</summary>
        private static List<Vector3> FacePoints(Walls.Wall w, bool towardA, out Vector3 n)
        {
            n = w.Front ? new Vector3(0, 0, towardA ? -1 : 1) : new Vector3(towardA ? -1 : 1, 0, 0);
            float lo0 = Math.Min(w.A0, w.B0), hi0 = Math.Max(w.A0, w.B0), lo1 = Math.Min(w.A1, w.B1), hi1 = Math.Max(w.A1, w.B1);
            var pts = new List<Vector3>(9);
            foreach (var u in new[] { 0.5f, 0.2f, 0.8f })
            {
                float lo = lo0 + (lo1 - lo0) * u, hi = hi0 + (hi1 - hi0) * u; if (hi - lo < 0.05f) continue;
                foreach (var v in new[] { 0.5f, 0.85f, 0.2f })
                {
                    float y = lo + (hi - lo) * v;
                    var p = w.Front ? new Vector3(w.Tx * 2 + u * 2f, y, w.Ty * 2 + 2) : new Vector3(w.Tx * 2 + 2, y, w.Ty * 2 + u * 2f);
                    pts.Add(p + n * 0.03f);
                }
            }
            return pts;
        }

        /// <summary>Whether a ray from the eye reaches the point in front of the face (the eye on the face's front side, in the face's
        /// room on an indoor map with rooms, nothing drawn between).</summary>
        private static bool Sees(Scene s, Grid g, Grid.RayState st, Vector3 n, int faceRoom, Vector3 eye, Vector3 p, int room, string dbg)
        {
            var d = p - eye; float len = d.magnitude; if (len < 1e-3f) return true;
            if (Vector3.Dot(eye - p, n) <= 0.01f) return false; // behind the face
            if (room != faceRoom) return false;                 // the eye's room does not draw the face
            var h = g.Ray(st, eye, d / len, 0f, len, t => s.Kind[t] != TriKind.Water, s.Rooms?.Labels == null ? (Func<Vector3, bool>)null : q => RoomAt(s, Mathf.FloorToInt(q.x), Mathf.FloorToInt(q.z)) == room);
            if (dbg != null && h.Tri < 0) Console.WriteLine($"  debug {dbg}: seen from {eye} at {p}; passed from behind: {(h.BackTri >= 0 ? s.Owner(h.BackTri) + " at " + h.BackT.ToString("0.##") : "none")}");
            return h.Tri < 0;
        }

        /// <summary>Walkable cells in front of a face within a radius, nearest first.</summary>
        private static List<(float d, int x, int y)> FrontCells(Scene s, Vector3 c, Vector3 n, int radius)
        {
            var cells = new List<(float d, int x, int y)>();
            int cx = Mathf.FloorToInt(c.x), cz = Mathf.FloorToInt(c.z);
            for (int y = cz - radius; y <= cz + radius; y++)
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    if (!s.Walkable(x, y)) continue;
                    var dd = new Vector3(x + 0.5f - c.x, 0, y + 0.5f - c.z);
                    if (Vector3.Dot(dd, n) < 0.5f) continue;
                    float m = dd.magnitude; if (m > radius) continue;
                    cells.Add((m, x, y));
                }
            cells.Sort((a, b) => a.d.CompareTo(b.d));
            return cells;
        }

        /// <summary>Where a wall face can be seen from at best: <see cref="SeenFirstPerson"/> (the eye over the nearest 12 walkable cells in
        /// front of it within 14 cells), <see cref="SeenFollow"/> (those eyes' cameras), <see cref="SeenTable"/> (the diorama from outside
        /// and above), <see cref="SeenBelow"/> (only from below the map, or outside it, level), <see cref="SeenNowhere"/>.</summary>
        public static int Visibility(Scene s, Grid g, Grid.RayState st, Walls.Wall w, bool towardA)
        {
            var pts = FacePoints(w, towardA, out var n); if (pts.Count == 0) return SeenNowhere;
            var c = pts[0];
            string dbg = Debug != null && Debug == $"{w.Tx},{w.Ty},{(w.Front ? "N" : "E")},{(towardA ? "A" : "B")}" ? $"{w.Name} toward {(towardA ? "A" : "B")}" : null;
            int faceRoom = RoomAt(s, Mathf.FloorToInt(c.x), Mathf.FloorToInt(c.z));
            var cells = FrontCells(s, c, n, 14); if (cells.Count > 12) cells.RemoveRange(12, cells.Count - 12);
            foreach (var (_, x, y) in cells)
            {
                var eye = new Vector3(x + 0.5f, s.Floor[x + y * s.W] + Views.EyeCells, y + 0.5f);
                foreach (var p in pts) if (Sees(s, g, st, n, faceRoom, eye, p, RoomAt(s, x, y), dbg)) return SeenFirstPerson;
            }
            Views.FollowEye(out float fb, out float fu);
            foreach (var (_, x, y) in cells)
            {
                var feet = new Vector3(x + 0.5f, s.Floor[x + y * s.W], y + 0.5f);
                var away = new Vector3(feet.x - c.x, 0, feet.z - c.z); if (away.sqrMagnitude < 1e-4f) away = n; away.Normalize();
                var eye = feet + away * fb + Vector3.up * fu;
                foreach (var p in pts) if (Sees(s, g, st, n, faceRoom, eye, p, RoomAt(s, x, y), dbg)) return SeenFollow;
            }
            foreach (var el in new[] { 15f, 35f, 55f, 75f })
                foreach (var az in new[] { 0f, -30f, 30f, -60f, 60f })
                {
                    var h = RotY(n, az);
                    var dir = h * Mathf.Cos(el * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(el * Mathf.Deg2Rad);
                    foreach (var p in pts) if (Sees(s, g, st, n, faceRoom, p + dir * 160f, p, faceRoom, dbg)) return SeenTable;
                }
            foreach (var el in new[] { -20f, -45f, -70f, 0f })
                foreach (var az in new[] { 0f, -35f, 35f })
                {
                    var h = RotY(n, az);
                    var dir = h * Mathf.Cos(el * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(el * Mathf.Deg2Rad);
                    foreach (var p in pts) if (Sees(s, g, st, n, faceRoom, p + dir * 160f, p, faceRoom, dbg)) return SeenBelow;
                }
            return SeenNowhere;
        }

        /// <summary>The level a face is seen at, the far first-person eyes included: <see cref="Visibility"/>, raised to first person when
        /// the eye over a walkable cell up to 40 cells away sees it (<see cref="SeenFromWalkable"/>). Every decision and check uses it.</summary>
        public static int Level(Scene s, Grid g, Grid.RayState st, Walls.Wall w, bool towardA)
        {
            int lv = Visibility(s, g, st, w, towardA);
            if (lv < SeenFirstPerson && SeenFromWalkable(s, g, st, w, towardA)) lv = SeenFirstPerson;
            return lv;
        }

        /// <summary>the design's inner walls: whether the first-person eye over a walkable cell in front of the face (up to 40 cells away: the
        /// nearest 24, then every few to 64 eyes) sees it.</summary>
        public static bool SeenFromWalkable(Scene s, Grid g, Grid.RayState st, Walls.Wall w, bool towardA)
        {
            var pts = FacePoints(w, towardA, out var n); if (pts.Count == 0) return false;
            var c = pts[0];
            int faceRoom = RoomAt(s, Mathf.FloorToInt(c.x), Mathf.FloorToInt(c.z));
            var cells = FrontCells(s, c, n, 40);
            int step = cells.Count <= 64 ? 1 : Math.Max(1, (cells.Count - 24) / 40);
            for (int i = 0, tests = 0; i < cells.Count && tests < 64; i += i < 24 ? 1 : step, tests++)
            {
                var (_, x, y) = cells[i];
                var eye = new Vector3(x + 0.5f, s.Floor[x + y * s.W] + Views.EyeCells, y + 0.5f);
                foreach (var p in pts) if (Sees(s, g, st, n, faceRoom, eye, p, RoomAt(s, x, y), null)) return true;
            }
            return false;
        }

        /// <summary>Whether an eye (map cells) at a walkable cell sees a face, and the point it sees (the showcase's spots).</summary>
        public static bool SeenFromEye(Scene s, Grid g, Grid.RayState st, Walls.Wall w, bool towardA, Vector3 eye, int eyeRoom, out Vector3 seenAt)
        {
            seenAt = default;
            var pts = FacePoints(w, towardA, out var n); if (pts.Count == 0) return false;
            int faceRoom = RoomAt(s, Mathf.FloorToInt(pts[0].x), Mathf.FloorToInt(pts[0].z));
            foreach (var p in pts) if (Sees(s, g, st, n, faceRoom, eye, p, eyeRoom, null)) { seenAt = p; return true; }
            return false;
        }
        public static int RoomOf(Scene s, int x, int y) => RoomAt(s, x, y);

        /// <summary>The room a cell is drawn in (indoor maps with rooms; 1 everywhere else, 0 outside the map).</summary>
        private static int RoomAt(Scene s, int x, int y) => s.Rooms?.Labels == null ? 1 : s.InMap(x, y) ? s.Rooms.Labels[x + y * s.W] : 0;

        /// <summary>A horizontal direction turned about the vertical by degrees.</summary>
        private static Vector3 RotY(Vector3 v, float deg)
        {
            float a = deg * Mathf.Deg2Rad, c = (float)Math.Cos(a), sn = (float)Math.Sin(a);
            return new Vector3(v.x * c - v.z * sn, 0f, v.x * sn + v.z * c);
        }

        public static string Markdown(Result r)
        {
            var sb = new StringBuilder();
            sb.Append($"# {r.Map}: Architect's catastrophic-mistake check: {r.Verdict}\n\n");
            sb.Append("Made by `architect fix` (client/tools/vrmaps/Check.cs) on the map as the game draws it and with its fix (the client's own builder); every face traced with rays from first person, Follow, the Table and below.\n\n");
            sb.Append("| check | result |\n|---|---|\n");
            sb.Append($"| K1 faces the fix adds | {r.Added}: seen in the views {r.AddedNormal} (in first person {r.AddedFp}); only from below {r.AddedBelowOnly}; nowhere {r.AddedUnseen}. Walls drawn from both sides {r.Doubled}: **floating (the added half seen only from below: showcase 1's mistake) {r.DoubledFloating}**; seen only from the Table, looking out of the map (not ledges) {r.TableOnlyAdded} |\n");
            sb.Append($"| K2 faces the fix removes (a turned wall's game face), their spots traced on the fixed map | {r.Removed}: **see-through where players play (first person, Follow) {r.Regressions}**; from the Table only {r.TableOnlyHoles}; toward the void, see-through by the outer-wall rule {r.RemovedOuter}; hidden from every view {r.RemovedHidden} |\n");
            sb.Append($"| K3 added faces standing on an edge between two walkable cells, rising above the higher floor there | {r.PathWalls} (worst {r.PathWorst:0.#} cells; the game already draws {r.PathWallsBefore} such walls from one side) |\n");
            sb.Append($"| K4 ground tops the game draws and the fixed map does not | **{r.LostTops}** |\n");
            sb.Append($"| K5 surfaces the fix adds within 0.03 cells of another surface facing the same way | **{r.ZFight}** of {r.NewSurfaces} |\n");
            sb.Append($"| K6 seen holes beside the walkable floor (void.hole high + medium cells) | {r.HolesBefore} -> {r.HolesAfter} |\n");
            sb.Append($"| K7 kinds whose high + medium cells grew | {(r.Worse.Count == 0 ? "none" : string.Join("; ", r.Worse))} |\n");
            sb.Append($"| K8 outer walls still drawn toward the void (the design's rule) | **{r.OuterTowardVoid}**{(r.OuterLeft.Count > 0 ? $" (+ {r.OuterLeft.Count} the fix cannot turn: {string.Join("; ", r.OuterLeft.Take(4))})" : "")} |\n");
            sb.Append($"| K9 walls invisible from their walkable side (the design's inner walls: a face missing toward a side that is not the void, seen from the first-person eye over walkable cells there) | {r.InnerBefore} -> {r.InnerAfter} |\n");
            sb.Append($"| K10 black surfaces the fix adds standing in front of a first-person eye (a walkable cell within 3 cells) | **{r.BlackWalls}** tiles |\n");
            if (r.Examples.Count > 0) { sb.Append("\nExamples:\n\n"); foreach (var e in r.Examples) sb.Append("- ").Append(e).Append('\n'); }
            return sb.ToString();
        }
    }
}
