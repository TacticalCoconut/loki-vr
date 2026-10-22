using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Loki.Formats;
using Loki.World;
using UnityEngine;
using T = Loki.VrMaps.Views.Thresholds;

namespace Loki.VrMaps
{
    /// <summary>
    /// The detectors of design notes section 2 that measure columns, distances and tables (build step 1): A1, B1-B4, C1-C4, D1,
    /// F1, F3, F5, G1, H1-H3, I1, I2, I4, J1, J2. Each writes its metrics (even when it finds nothing) and its findings. Severity:
    /// 0 high, 1 medium, 2 low, 3 info.
    /// </summary>
    public sealed partial class Analysis
    {
        public readonly Scene S; public readonly Grid G; public readonly MapReport R;
        private static readonly string[] Sev = { "high", "medium", "low", "info" };
        private static readonly string[] FP = { "firstPerson" };
        public Analysis(Scene s, Grid g, MapReport r) { S = s; G = g; R = r; }

        /// <summary>A drawn surface that stops a ray / a body: not water, and not mostly cut out by its texture (rays and columns test
        /// the exact texel; distance queries count a triangle when at least 4 of its 7 sample points are kept by the shader).</summary>
        private bool Solid(int t) => S.Kind[t] != TriKind.Water && S.Opaque7[t] >= 4;
        /// <summary>For rays and columns, which test the exact texel themselves (Grid.Column / Grid.Ray): every drawn surface but water.</summary>
        private bool Drawn(int t) => S.Kind[t] != TriKind.Water;

        public void RunStep1()
        {
            Facts();
            Head();
            Walkability();
            GroundWallSides();
            Holes();
            Water();
            Sounds();
            Rooms();
            Table();
            Lights();
            FogAndLightmap();
        }

        // ------------------------------------------------------------------ helpers
        private float[] Ro(float lx, float ly, float lz) => new[] { (lx - S.Gnd.Width) * 5f, -ly * 5f, (lz - S.Gnd.Height) * 5f };

        /// <summary>Turns a cell set into findings: one per 8-connected region of one severity.</summary>
        private void Emit(string kind, CellSet set, string[] views, bool worstLow, string unit, float threshold, Func<int, List<int>, string> fix, string note = null, bool metresFp = true, string basis = "measured")
        {
            foreach (var (sev, cells, worst) in set.Regions(worstLow))
            {
                int wx = worst % S.W, wy = worst / S.W;
                int x0 = cells.Min(c => c % S.W), x1 = cells.Max(c => c % S.W), y0 = cells.Min(c => c / S.W), y1 = cells.Max(c => c / S.W);
                var causes = cells.Where(c => set.Cause[c] >= 0).GroupBy(c => S.Owner(set.Cause[c])).OrderByDescending(g => g.Count()).Take(3).Select(g => $"{g.Key} ({g.Count()} cells)").ToList();
                var tris = cells.Where(c => set.Cause[c] >= 0).Select(c => set.Cause[c]).ToList();
                float val = set.Val[worst];
                var meas = new Dictionary<string, object> { ["value"] = val, ["unit"] = unit, ["threshold"] = threshold };
                if (metresFp && unit.StartsWith("cells")) meas["metresFirstPerson"] = val * Views.FirstPersonMetresPerCell;
                if (unit.Length == 0) meas.Remove("value");
                float fy = S.InMap(wx, wy) ? S.Floor[worst] : 0f;
                R.Findings.Add(new Finding
                {
                    Kind = kind, Severity = Sev[sev], Views = views, Count = cells.Count,
                    Region = new Region { Runs = Region.FromCells(cells, S.W), Box = new[] { x0, y0, x1, y1 }, At = new[] { wx, wy } },
                    World = new Dictionary<string, float[]> { ["loki"] = new[] { wx + 0.5f, fy, wy + 0.5f }, ["ro"] = Ro(wx + 0.5f, fy, wy + 0.5f) },
                    Measure = meas, Cause = causes.Count > 0 ? causes : null, Fix = fix(sev, tris), Note = note, Basis = basis
                });
            }
        }

        private static Dictionary<string, object> Counts(CellSet s) => new Dictionary<string, object> { ["high"] = s.CountOf(0), ["medium"] = s.CountOf(1), ["low"] = s.CountOf(2), ["info"] = s.CountOf(3) };

        private string ModelFix(List<int> tris, string what)
        {
            var models = tris.Where(t => S.Kind[t] == TriKind.Model).Select(t => S.Src[t]).Distinct().Take(4).ToList();
            bool ground = tris.Any(t => S.Kind[t] != TriKind.Model);
            if (models.Count > 0) return $"{what}: a per-map view override for model instance{(models.Count > 1 ? "s" : "")} {string.Join(", ", models.Select(m => "#" + m))} (hide / lift / thin it in first person)" + (ground ? ", and the ground's wall here" : "");
            return ground ? $"{what}: the ground's own surface (a per-map override cannot move GND geometry; a view rule: lower the eye or hide the wall face in first person)" : what;
        }

        // ------------------------------------------------------------------ A1 the map's facts
        private void Facts()
        {
            var f = R.MapFacts; var rsw = S.Rsw; var gnd = S.Gnd; var gat = S.Gat;
            int walk = S.Walk.Count(b => b);
            f["files"] = $"rsw {rsw.Version} ({rsw.GndFile}, {rsw.GatFile}), gnd {gnd.Version}, gat {gat.Version}";
            f["size"] = $"GAT {S.W} x {S.H} cells, GND {gnd.Width} x {gnd.Height} tiles, {walk} walkable cells";
            int miss = S.Instances.Count(i => i.Missing), anim = S.Instances.Count(i => i.Animated), texAnim = S.Instances.Count(i => i.TextureAnimated), mirr = S.Instances.Count(i => i.Mirrored), alpha = S.Instances.Count(i => !i.Missing && i.Alpha < 0.999f);
            f["models"] = $"{S.Instances.Count} instances of {S.ModelFiles} files ({S.ModelFilesMissing} files missing or unreadable: {miss} instances left out), {anim} animated (t = 0 pose measured), {texAnim} with moving textures, {mirr} mirrored, {alpha} translucent";
            f["ground"] = $"{S.GroundCulledSurfaces} surfaces left out as black void filler ({S.GroundCulledTinted} black-tinted tops; textures: {(S.BlackTextures.Count == 0 ? "none" : string.Join(", ", S.BlackTextures.Take(6)))})";
            f["triangles"] = $"{S.Count} drawn ({S.Kind.Count(k => k == TriKind.Model)} model, {S.Kind.Count(k => k == TriKind.Top)} ground tops, {S.Kind.Count(k => k == TriKind.WallFront || k == TriKind.WallRight)} ground walls, {S.Kind.Count(k => k == TriKind.Water)} water); grid {G.NX} x {G.NZ} bins, {G.Items} entries";
            f["indoor"] = S.Indoor + (S.Rooms != null ? $", {(S.Rooms.Labels != null ? S.Rooms.Rooms.Count : Math.Min(S.Rooms.Rooms.Count, 1))} room(s)" + (S.RoomsCheck.Length > 0 ? $" (against the player's rooms.tsv: {S.RoomsCheck})" : "") : "");
            f["objects"] = $"{S.Lights.Count} point lights, {S.Sounds.Count} sounds, {S.Effects.Count} effects";
            f["water"] = S.HasWater ? $"from the {S.WaterSource}: type {S.WaterType}, level {S.WaterLevelRo} (Loki y {S.WaterY:0.##}), wave height {S.WaveHeightRo}, {S.WaterTiles} tiles" : "none";
            var m = R.M("A1 map.files");
            m["drawnSideAgreesWithBuilderNormals"] = S.NormalsAgree; m["drawnSideDisagrees"] = S.NormalsDisagree;
            m["instances"] = S.Instances.Count; m["missing"] = miss;
            m["materials"] = S.Mats.Count; m["materialsWithCutOutTexels"] = S.Mats.Count(x => x.Alpha != null);
            m["trianglesMostlyCutOut"] = S.Opaque7.Count(o => o < 4); m["animated"] = anim; m["mirrored"] = mirr; m["blackSurfaces"] = S.GroundCulledSurfaces; m["walkable"] = walk;
            foreach (var n in S.Notes) R.Notes.Add(n);
        }

        // ------------------------------------------------------------------ B1-B4 the head in first person
        private void Head()
        {
            int W = S.W, H = S.H; float eye = Views.EyeCells;
            var clip = new CellSet(W, H); var ceil = new CellSet(W, H); var gap = new CellSet(W, H) { AbsWorst = true }; var wet = new CellSet(W, H); var covered = new CellSet(W, H);
            var nearest = new float[W * H]; var clearance = new float[W * H];
            float headR = T.HeadNear, off = Views.FpCells(0.10f);
            Parallel.For(0, H, () => (new List<(float y, int t)>(), new Grid.RayState(S.Count)), (y, loopState, local) =>
            {
                var (col, st) = local;
                for (int x = 0; x < W; x++)
                {
                    int i = x + y * W; if (!S.Walk[i]) continue;
                    float F = S.Floor[i], cx = x + 0.5f, cz = y + 0.5f;
                    // B1: the nearest drawn surface to the eye; an eye inside a model's walls and roof is inside it
                    var eyeP = new Vector3(cx, F + eye, cz);
                    float d = G.Nearest(eyeP, headR, out int tri, Solid);
                    nearest[i] = d;
                    if (tri >= 0) clip.Set(x, y, d < T.HeadInside ? 0 : d < T.HeadFace ? 1 : 2, d, tri);
                    if (tri < 0 || d >= T.HeadInside) { int enc = G.Enclosing(st, eyeP, col); if (enc >= 0) clip.Set(x, y, 0, 0f, S.Instances[enc].FirstTri); }
                    // B2: the lowest surface over the head (from 1.5 cells up) over the head's columns (centre and +-0.1 m)
                    float best = float.MaxValue; int bt = -1;
                    for (int k = 0; k < 5; k++)
                    {
                        float px = cx + (k == 1 ? off : k == 2 ? -off : 0), pz = cz + (k == 3 ? off : k == 4 ? -off : 0);
                        G.Column(px, pz, col, Drawn);
                        foreach (var (hy, t) in col) if (hy > F + T.CeilingFrom && hy - F < best) { best = hy - F; bt = t; }
                    }
                    clearance[i] = best;
                    if (bt >= 0)
                    {
                        if (best < T.CeilingHigh) ceil.Set(x, y, 0, best, bt);
                        else if (best < T.CeilingSprite) ceil.Set(x, y, 1, best, bt);
                        else if (best < T.CeilingCramped) ceil.Set(x, y, 3, best, bt);
                        covered.Set(x, y, 3, best, bt); // C4: rain falls through it, the laser passes it
                    }
                    // B3: the drawn surface nearest to the feet
                    G.Column(cx, cz, col, Drawn);
                    float g = float.MaxValue; int gt = -1;
                    foreach (var (hy, t) in col) if (Math.Abs(hy - F) < Math.Abs(g)) { g = hy - F; gt = t; }
                    if (gt < 0 || Math.Abs(g) > T.FloorSearch) gap.Set(x, y, 0, T.FloorSearch, -1);
                    else if (Math.Abs(g) > T.FloorGapHigh) gap.Set(x, y, 0, -g, gt);
                    else if (Math.Abs(g) > T.FloorGapMedium) gap.Set(x, y, 1, -g, gt);
                    // B4: the eye under the water
                    if (S.HasWater && S.WaterTile[(x / 2) + (y / 2) * S.Gnd.Width])
                    {
                        float depth = S.WaterY - F;
                        if (depth > eye) wet.Set(x, y, 0, depth); else if (depth > T.WadeCells) wet.Set(x, y, 3, depth);
                    }
                }
                return local;
            }, _ => { });

            Emit("fp.head_clip", clip, FP, true, "cells from the eye", T.HeadFace, (sev, tris) => ModelFix(tris, sev == 0 ? "the head is inside it" : "it is at the face"),
                "the nearest drawn surface to the first-person eye (2.143 cells above the cell centre's GAT floor)");
            Emit("fp.low_ceiling", ceil, new[] { "firstPerson", "follow", "table" }, true, "cells of clearance", T.CeilingHigh, (sev, tris) => ModelFix(tris, sev == 0 ? "the eye is above it" : sev == 1 ? "the character's head clips it" : "cramped"),
                "clearance = the lowest drawn surface from 1.5 cells above the GAT floor up, over the head's five columns (lower surfaces: walk.walk_through)");
            Emit("fp.floor_gap", gap, FP, false, "cells (feet above + / under - the drawn floor)", T.FloorGapMedium, (sev, tris) => tris.Count == 0 ? "no drawn floor under the feet: a floor surface for first person, or check the GAT here" : "the GAT and the drawn floor disagree: lift / lower the drawn surface in first person or shift the unit to the drawn floor (the server's GAT stays)",
                "value = GAT floor minus the nearest drawn surface (positive: the feet float above it)");
            Emit("fp.underwater", wet, FP, false, "cells deep", Views.EyeCells, (sev, tris) => sev == 0 ? "the eye is under the water: an underwater look (fog / tint) or keep the eye above the surface" : "wading: the surface cuts the body (as in RO)",
                "depth = water level minus the GAT floor (the surface waves by the wave height)");
            var m1 = R.M("B1 fp.head_clip"); foreach (var kv in Counts(clip)) m1[kv.Key] = kv.Value;
            var nearVals = Enumerable.Range(0, W * H).Where(i => S.Walk[i]).Select(i => nearest[i]).OrderBy(v => v).ToArray();
            if (nearVals.Length > 0) { m1["minMetres"] = nearVals[0] * Views.FirstPersonMetresPerCell; m1["medianMetres"] = nearVals[nearVals.Length / 2] * Views.FirstPersonMetresPerCell; }
            var m2 = R.M("B2 fp.low_ceiling"); foreach (var kv in Counts(ceil)) m2[kv.Key] = kv.Value;
            var clr = Enumerable.Range(0, W * H).Where(i => S.Walk[i] && clearance[i] < float.MaxValue).Select(i => clearance[i]).OrderBy(v => v).ToArray();
            m2["coveredCells"] = clr.Length; if (clr.Length > 0) { m2["minClearanceCells"] = clr[0]; m2["medianClearanceCells"] = clr[clr.Length / 2]; }
            var m3 = R.M("B3 fp.floor_gap"); foreach (var kv in Counts(gap)) m3[kv.Key] = kv.Value;
            var m4 = R.M("B4 fp.underwater"); foreach (var kv in Counts(wet)) m4[kv.Key] = kv.Value;
            var m5 = R.M("C4 walk.covered"); m5["cells"] = covered.Total;
            Emit("walk.covered", covered, new[] { "firstPerson", "follow", "table" }, true, "cells of clearance", 0f, (s, t) => "info: walkable cells under a drawn surface (rain falls through it, World/Weather.cs:582; the laser passes it to the ground)", metresFp: true);
        }

        // ------------------------------------------------------------------ C1-C3 walkability against what is drawn
        /// <summary>The height span (cells) of the drawn GND wall on the edge between two side-by-side cells (0 when no drawn wall).</summary>
        public float WallSpan(int ax, int ay, int bx, int by, out bool drawn)
        {
            drawn = false;
            var gnd = S.Gnd; int gw = gnd.Width, gh = gnd.Height;
            int tax = ax / 2, tay = ay / 2, tbx = bx / 2, tby = by / 2;
            if (tax == tbx && tay == tby) return 0f;            // the same tile: no wall between them
            if (ay == by && Math.Abs(ax - bx) == 1)             // east-west neighbours: the Right wall of the western tile
            {
                int wx = Math.Min(tax, tbx); int ty = tay; if (wx < 0 || wx + 1 >= gw || ty < 0 || ty >= gh) return 0f;
                if (!_rightDrawn[wx + ty * gw]) return 0f;
                drawn = true;
                var a = gnd.Tiles[wx + ty * gw]; var b = gnd.Tiles[wx + 1 + ty * gw];
                float k0 = (ay % 2) * 0.5f, k1 = k0 + 0.5f;     // along the edge, south to north
                float d0 = Lerp(a.H1, a.H3, k0) - Lerp(b.H0, b.H2, k0), d1 = Lerp(a.H1, a.H3, k1) - Lerp(b.H0, b.H2, k1);
                return Math.Max(Math.Abs(d0), Math.Abs(d1)) * RoSpace.Scale;
            }
            if (ax == bx && Math.Abs(ay - by) == 1)             // south-north neighbours: the Front wall of the southern tile
            {
                int sy = Math.Min(tay, tby); int tx = tax; if (sy < 0 || sy + 1 >= gh || tx < 0 || tx >= gw) return 0f;
                if (!_frontDrawn[tx + sy * gw]) return 0f;
                drawn = true;
                var a = gnd.Tiles[tx + sy * gw]; var b = gnd.Tiles[tx + (sy + 1) * gw];
                float k0 = (ax % 2) * 0.5f, k1 = k0 + 0.5f;     // along the edge, west to east
                float d0 = Lerp(a.H2, a.H3, k0) - Lerp(b.H0, b.H1, k0), d1 = Lerp(a.H2, a.H3, k1) - Lerp(b.H0, b.H1, k1);
                return Math.Max(Math.Abs(d0), Math.Abs(d1)) * RoSpace.Scale;
            }
            return 0f;
        }
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        private bool[] _frontDrawn, _rightDrawn;
        private void WallTables()
        {
            if (_frontDrawn != null) return;
            int n = S.Gnd.Width * S.Gnd.Height; _frontDrawn = new bool[n]; _rightDrawn = new bool[n];
            for (int t = 0; t < S.Count; t++) { if (S.Kind[t] == TriKind.WallFront) _frontDrawn[S.Src[t]] = true; else if (S.Kind[t] == TriKind.WallRight) _rightDrawn[S.Src[t]] = true; }
        }

        private void Walkability()
        {
            WallTables();
            int W = S.W, H = S.H;
            var inv = new CellSet(W, H); var thru = new CellSet(W, H); var wall = new CellSet(W, H);
            int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
            float r = T.InvisibleWallCapsule, br = T.BodyRadius;
            Parallel.For(0, H, () => (new List<(float y, int t)>(), new Grid.RayState(S.Count)), (y, loopState, local) =>
            {
                var (col, st) = local;
                for (int x = 0; x < W; x++)
                {
                    int i = x + y * W;
                    if (!S.Walk[i])
                    {
                        // C1: a blocked cell beside a walkable one that looks like open floor
                        if (S.HasWater && S.WaterTile[(x / 2) + (y / 2) * S.Gnd.Width]) continue;
                        bool colRead = false;
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = x + dx[k], ny = y + dy[k];
                            if (!S.Walkable(nx, ny)) continue;
                            float F = S.Floor[nx + ny * W];
                            if (!colRead) { G.Column(x + 0.5f, y + 0.5f, col, Drawn); colRead = true; }
                            bool flat = false; foreach (var (hy, t) in col) if (Math.Abs(hy - F) <= T.FlatStep) { flat = true; break; }
                            if (!flat) continue;
                            if (WallSpan(x, y, nx, ny, out _) > T.WallVisible) continue;
                            var a = new Vector3(x + 0.5f, F + T.ClearFrom + r, y + 0.5f); var b = new Vector3(x + 0.5f, F + Views.PlayerHeightCells - r, y + 0.5f);
                            if (G.Capsule(a, b, r, out _, Solid)) continue;
                            // something standing on the shared edge (a thin model wall, a fence) blocks the look as well
                            float ex = (x + nx) * 0.5f + 0.5f, ez = (y + ny) * 0.5f + 0.5f;
                            if (G.Capsule(new Vector3(ex, F + T.ClearFrom + 0.25f, ez), new Vector3(ex, F + Views.PlayerHeightCells - 0.25f, ez), 0.25f, out _, Solid)) continue;
                            if (G.Enclosing(st, new Vector3(x + 0.5f, F + 1.2f, y + 0.5f), col) >= 0) continue; // inside a model: visibly blocked
                            // padding: a drawn obstacle within a cell (a wall, a fence just beyond): low; open flat ground: medium (by length below)
                            bool padding = G.Capsule(new Vector3(x + 0.5f, F + T.ClearFrom + T.PaddingCells, y + 0.5f), new Vector3(x + 0.5f, F + Views.PlayerHeightCells, y + 0.5f), T.PaddingCells, out _, Solid);
                            inv.Set(x, y, 2, padding ? 1f : 0f); // value 1 = padding
                            break;
                        }
                        continue;
                    }
                    // C2: the body capsule (0.6 to 2.0 cells, radius 0.25) at the centre, then at the quarter points
                    {
                        float F = S.Floor[i];
                        var a = new Vector3(x + 0.5f, F + T.BodyFrom + br, y + 0.5f); var b = new Vector3(x + 0.5f, F + T.BodyTo - br, y + 0.5f);
                        if (G.Capsule(a, b, br, out int tri, Solid)) thru.Set(x, y, 0, 0f, tri);
                        else if (G.Enclosing(st, new Vector3(x + 0.5f, F + 1.2f, y + 0.5f), col) is int enc && enc >= 0) thru.Set(x, y, 0, 0f, S.Instances[enc].FirstTri);
                        else
                            for (int q = 0; q < 4; q++)
                            {
                                float px = x + (q % 2 == 0 ? 0.25f : 0.75f), pz = y + (q < 2 ? 0.25f : 0.75f), Fq = S.GatY(px, pz);
                                if (G.Capsule(new Vector3(px, Fq + T.BodyFrom + br, pz), new Vector3(px, Fq + T.BodyTo - br, pz), br, out tri, Solid)) { thru.Set(x, y, 2, 0f, tri); break; }
                            }
                    }
                    // C3: a drawn GND wall between this walkable cell and its walkable east / north neighbour
                    for (int k = 0; k < 4; k += 2)
                    {
                        int nx = x + (k == 0 ? 1 : 0), ny = y + (k == 0 ? 0 : 1);
                        if (!S.Walkable(nx, ny)) continue;
                        float span = WallSpan(x, y, nx, ny, out bool drawn);
                        if (!drawn || span <= T.WallOnPathMedium) continue;
                        int sev = span > T.WallOnPathHigh ? 0 : 1;
                        wall.Set(x, y, sev, span); wall.Set(nx, ny, sev, span);
                    }
                }
                return local;
            }, _ => { });
            // C1 severity: open (not padding) segments of 3 cells or more are medium
            foreach (var (sev, cells, worst) in inv.Regions(true)) { int open = cells.Count(c => inv.Val[c] == 0f); if (open >= 3) foreach (var c in cells) if (inv.Val[c] == 0f) inv.Sev[c] = 1; }
            Emit("walk.invisible_wall", inv, new[] { "firstPerson", "follow", "table" }, true, "", T.FlatStep,
                (s, t) => "the server blocks these cells but nothing drawn says so: a visible barrier in the client (a per-map marker, a low fence or a ground tint), since the GAT stays the server's",
                "blocked (GAT) cells beside walkable ones whose drawn floor continues flat (within 0.3 cells) with nothing drawn up to 2.4 cells and no wall on the shared edge; medium: open ground in runs of 3+, low: short runs or padding before a drawn obstacle within 1 cell", metresFp: false, basis: "heuristic");
            Emit("walk.walk_through", thru, new[] { "firstPerson", "follow", "table" }, true, "", T.BodyRadius,
                (s, tris) => ModelFix(tris, "the character walks through it"),
                "drawn geometry inside the body capsule (0.6 to 2.0 cells above the floor, radius 0.25 cells), or the body inside a model's walls and roof: high at the centre, low at a quarter point only (free movement)", metresFp: false);
            Emit("walk.wall_on_path", wall, new[] { "firstPerson", "follow", "table" }, false, "cells high", T.WallOnPathMedium,
                (s, t) => "a drawn ground wall between two walkable cells: hide the wall face in the views, or a visual step (the GAT stays the server's)",
                "the GND wall's height span on an edge whose two cells are walkable");
            var m1 = R.M("C1 walk.invisible_wall"); foreach (var kv in Counts(inv)) m1[kv.Key] = kv.Value;
            var m2 = R.M("C2 walk.walk_through"); foreach (var kv in Counts(thru)) m2[kv.Key] = kv.Value;
            var m3 = R.M("C3 walk.wall_on_path"); foreach (var kv in Counts(wall)) m3[kv.Key] = kv.Value;
        }

        // ------------------------------------------------------------------ D4 ground walls, on the design's outer-wall rule
        /// <summary>The classification of every drawn GND wall (Walls.cs: interior / outer / ledge / edge / hidden), made once.</summary>
        public Walls WallSet => _walls ?? (_walls = new Walls(S));
        private Walls _walls;

        /// <summary>
        /// D4 (plan section 2) on the design's OUTER-WALL RULE (2026-10-14; Walls.cs): every drawn GND wall is single-sided, its
        /// front fixed at +z (Front walls) or +x (Right walls) whatever the heights (World/GroundMeshBuilder.cs, the walls' fixed normals),
        /// and the ground culls back faces (Shaders/LokiGround.shader "Cull Back"). A wall is a PROBLEM only when it is missing from a
        /// side players see it from: an INTERIOR wall (playable or drawn on both sides) drawn from one side, an OUTER wall (playable on one
        /// side, the void on the other) drawn toward the void, a LEDGE drawn away from its low side (FLAGGED rule). An outer wall
        /// see-through from the void side is correct by design and is only counted. The finding's cells: those beside the wall on the
        /// side it is missing from (when that face can be seen at all: not under a higher drawn floor).
        /// </summary>
        private void GroundWallSides()
        {
            WallTables();
            int W = S.W, H = S.H;
            var set = new CellSet(W, H);
            foreach (var w in WallSet.All)
            {
                if (!w.Problem) continue;
                foreach (var side in new[] { Walls.Faces.TowardA, Walls.Faces.TowardB })
                {
                    if ((w.Missing & side) == 0 || !w.FaceVisible(side)) continue;
                    bool towardA = side == Walls.Faces.TowardA; var st = towardA ? w.A : w.B;
                    int sev = st == Walls.Side.Playable ? (w.Span > 2f ? 0 : 1) : 2;
                    for (int k = 0; k < 2; k++)
                    {
                        int x = w.Front ? w.Tx * 2 + k : (towardA ? w.Tx * 2 + 1 : w.Tx * 2 + 2);
                        int y = w.Front ? (towardA ? w.Ty * 2 + 1 : w.Ty * 2 + 2) : w.Ty * 2 + k;
                        if (S.InMap(x, y)) set.Set(x, y, sev, w.Span);
                    }
                }
            }
            Emit("back.gnd_wall", set, new[] { "firstPerson", "follow", "table" }, false, "cells high", Walls.MinSpan,
                (s, t) => "a ground wall missing from this side although players see it (the design's outer-wall rule: an interior wall is drawn from both sides, an outer wall toward the play area only): Architect's fix mode draws it (a W line of the map's fix file)",
                "the cells beside a ground wall on the side it is missing from, for walls Walls.cs classifies as interior (playable / drawn on both sides), outer drawn toward the void, or ledge; high: a playable side and over 2 cells high, medium: a playable side, low: drawn scenery; an outer wall see-through from the void side is correct and not listed", metresFp: true);
            var m = R.M("D4 back.gnd_wall");
            foreach (var kv in WallSet.Counter()) m[kv.Key] = kv.Value;
            foreach (var kv in Counts(set)) m[kv.Key] = kv.Value;
            R.MapFacts["ground walls"] = WallSet.Summary();
        }

        // ------------------------------------------------------------------ D1 holes into the sky, and the border
        /// <summary>The room a cell is drawn in on an indoor map with rooms (0: none); every cell is room 1 elsewhere.</summary>
        private int RoomAt(int x, int y) => S.Rooms?.Labels == null ? 1 : S.InMap(x, y) ? S.Rooms.Labels[x + y * S.W] : 0;
        /// <summary>The room mask as the shaders apply it (Shaders/LokiRoomMask.cginc:23-33): only the current room's cells are drawn.</summary>
        private Func<Vector3, bool> RoomVisible(int room) => S.Rooms?.Labels == null ? (Func<Vector3, bool>)null : p => RoomAt(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z)) == room;

        /// <summary>D1's seen holes (severity by distance; all info on a map with its own sky), for Architect's fix mode.</summary>
        public CellSet HoleCells;
        public int[] HoleDist;

        private void Holes()
        {
            int W = S.W, H = S.H, gw = S.Gnd.Width;
            var hole = new bool[W * H]; int holes = 0;
            var col = new List<(float y, int t)>();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int tx = x / 2, ty = y / 2;
                    bool top = tx < gw && ty < S.Gnd.Height && S.TopDrawn[tx + ty * gw];
                    if (top) continue;
                    G.Column(x + 0.5f, y + 0.5f, col, Drawn);
                    if (col.Count == 0) { hole[x + y * W] = true; holes++; }
                }
            // distance (8 directions) from the walkable cells, up to HoleLow, with the nearest walkable cell
            var dist = new int[W * H]; var src = new int[W * H]; for (int i = 0; i < dist.Length; i++) { dist[i] = int.MaxValue; src[i] = -1; }
            var q = new Queue<int>();
            for (int i = 0; i < W * H; i++) if (S.Walk[i]) { dist[i] = 0; src[i] = i; q.Enqueue(i); }
            while (q.Count > 0)
            {
                int c = q.Dequeue(); int d = dist[c] + 1; if (d > T.HoleLow) continue;
                int cx = c % W, cy = c / W;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx, y = cy + dy; if (x < 0 || y < 0 || x >= W || y >= H) continue;
                    int j = x + y * W; if (dist[j] > d) { dist[j] = d; src[j] = src[c]; q.Enqueue(j); }
                }
            }
            // seen: the first-person eye over the nearest walkable cell sees the hole's centre at that floor's height (the renderer's
            // rules: culled backs and water passed; on an indoor map only that room's cells are drawn, and a hole outside its room is
            // room.view_leak's, not this)
            var set = new CellSet(W, H); var setWall = new CellSet(W, H); int hidden = 0, otherRoom = 0;
            var cand = Enumerable.Range(0, W * H).Where(i => hole[i] && dist[i] <= T.HoleLow).ToArray();
            var seenArr = new bool[W * H]; var throughWall = new bool[W * H];
            Parallel.For(0, cand.Length, () => new Grid.RayState(S.Count), (k, loopState, st) =>
            {
                int i = cand[k], sIdx = src[i]; int sx = sIdx % W, sy = sIdx / W;
                int room = RoomAt(sx, sy); if (S.Rooms?.Labels != null && RoomAt(i % W, i / W) != room) return st;
                var eye = new Vector3(sx + 0.5f, S.Floor[sIdx] + Views.EyeCells, sy + 0.5f);
                var target = new Vector3(i % W + 0.5f, S.Floor[sIdx], i / W + 0.5f);
                var d = target - eye; float len = d.magnitude; if (len < 1e-4f) { seenArr[i] = true; return st; }
                var h = G.Ray(st, eye, d / len, 0f, len, Drawn, RoomVisible(room));
                seenArr[i] = h.Tri < 0 || h.T >= len - 1e-3f;
                throughWall[i] = seenArr[i] && h.BackTri >= 0 && (S.Kind[h.BackTri] == TriKind.WallFront || S.Kind[h.BackTri] == TriKind.WallRight);
                return st;
            }, _ => { });
            HoleDist = dist;
            foreach (int i in cand)
            {
                if (S.Rooms?.Labels != null && RoomAt(i % W, i / W) != RoomAt(src[i] % W, src[i] / W)) { otherRoom++; continue; }
                if (!seenArr[i]) { hidden++; continue; }
                (throughWall[i] ? setWall : set).Set(i % W, i / W, dist[i] <= T.HoleHigh ? 0 : dist[i] <= T.HoleMedium ? 1 : 2, dist[i]);
            }
            if (S.OwnSky != null)
            {
                // the map has its own sky (World/MapSky.cs: RO cleared the screen to its colour and drew its clouds under the map): what is
                // not drawn shows the map's designed sky, as in RO, so these are facts, never holes to fill
                for (int i = 0; i < set.Sev.Length; i++) if (set.Sev[i] >= 0) set.Sev[i] = 3;
                for (int i = 0; i < setWall.Sev.Length; i++) if (setWall.Sev[i] >= 0) setWall.Sev[i] = 3;
            }
            HoleCells = set;
            Emit("void.hole", set, new[] { "firstPerson", "follow", "table" }, true, "cells from the walkable floor", T.HoleMedium,
                (s, t) => s == 3 ? $"info: the map's own sky ({S.OwnSky}) shows here, as RO showed it: not a hole (Architect never fills it)" : s == 0 ? "a hole beside the walkable floor shows the sky (RO showed black): Architect's fix mode fills it (a black floor and skirts: an F line of the map's fix file)" : "a hole in the drawn ground in sight: fill it or hide it",
                "cells with no drawn ground and nothing drawn in their column that the first-person eye over the nearest walkable cell sees (line of sight); value = distance (cells) to that cell" + (S.OwnSky != null ? "; this map has its own sky, so every such cell is info" : ""), metresFp: true);
            Emit("void.hole_through_wall", setWall, new[] { "firstPerson", "follow", "table" }, true, "cells from the walkable floor", T.HoleMedium,
                (s, t) => "seen only THROUGH a ground wall drawn from its other side (back.gnd_wall): drawing the wall from both sides hides it",
                "as void.hole, but the line of sight passes a ground wall from behind (culled: Shaders/LokiGround.shader:18)", metresFp: true);
            // the map's border: how close the walkable area comes to the end of the drawn world (the GAT's rectangle)
            int minBorder = int.MaxValue;
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (S.Walk[x + y * W]) minBorder = Math.Min(minBorder, Math.Min(Math.Min(x, y), Math.Min(W - 1 - x, H - 1 - y)));
            var m = R.M("D1 void.hole"); m["holeCells"] = holes; m["ownSky"] = S.OwnSky ?? "none"; m["seenThroughAGroundWall"] = setWall.Total; m["withinReach"] = cand.Length; m["hiddenByGeometry"] = hidden; m["outsideTheRoom"] = otherRoom;
            foreach (var kv in Counts(set)) m[kv.Key] = kv.Value;
            m["walkableToMapEdgeMinCells"] = minBorder == int.MaxValue ? -1 : minBorder;
            R.MapFacts["map edge"] = minBorder == int.MaxValue ? "no walkable cell" : $"the walkable area comes within {minBorder} cells ({minBorder * Views.FirstPersonMetresPerCell:0.#} m in first person) of the map's edge";
        }

        // ------------------------------------------------------------------ G1 water against the GAT
        private void Water()
        {
            int W = S.W, H = S.H; var set = new CellSet(W, H); int dryWet = 0, wetDry = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var type = S.Gat.Get(x, y).Type;
                    bool gatWater = type == GatFile.CellType.Water || type == GatFile.CellType.WaterWalkable || type == GatFile.CellType.WaterSnipable;
                    bool drawn = S.HasWater && S.WaterTile[(x / 2) + (y / 2) * S.Gnd.Width] && S.WaterY > S.Floor[x + y * W];
                    if (gatWater && !drawn) { set.Set(x, y, 2, 0f); wetDry++; }
                    else if (!gatWater && drawn && S.Walk[x + y * W]) { set.Set(x, y, 3, S.WaterY - S.Floor[x + y * W]); dryWet++; }
                }
            Emit("water.gat_mismatch", set, new[] { "firstPerson", "follow", "table" }, true, "cells", 0f,
                (s, t) => s == 2 ? "the GAT says water but none is drawn (above the floor): check the water level / tiles" : "info: drawn water over cells the GAT calls dry ground (wading in RO too)",
                "low: GAT water types (2, 3, 4) without drawn water above their floor; info: drawn water above walkable dry cells", metresFp: false);
            var m = R.M("G1 water.gat_mismatch"); m["gatWaterNotDrawn"] = wetDry; m["drawnOverDryWalkable"] = dryWet;
        }

        // ------------------------------------------------------------------ H1-H3 sounds
        private void Sounds()
        {
            int W = S.W, H = S.H; var labels = S.Rooms?.Labels;
            int unheard = 0, cross = 0, high = 0; var list = new List<string>();
            var crossSet = new CellSet(W, H);
            foreach (var s in S.Sounds)
            {
                // LokiAudio.cs:576 and :339-345: the distance between the listener's cell and (pos.x - 0.5, pos.z - 0.5)
                float sx = s.Pos.x - 0.5f, sz = s.Pos.z - 0.5f;
                int x0 = Math.Max(0, Mathf.FloorToInt(sx - s.Range)), x1 = Math.Min(W - 1, Mathf.CeilToInt(sx + s.Range)), y0 = Math.Max(0, Mathf.FloorToInt(sz - s.Range)), y1 = Math.Min(H - 1, Mathf.CeilToInt(sz + s.Range));
                int own = 0; int scx = Mathf.Clamp(Mathf.FloorToInt(s.Pos.x), 0, W - 1), scz = Mathf.Clamp(Mathf.FloorToInt(s.Pos.z), 0, H - 1);
                if (labels != null) own = labels[scx + scz * W];
                int heard = 0, other = 0;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        if (!S.Walk[x + y * W]) continue;
                        float d = (float)Math.Sqrt((x - sx) * (x - sx) + (y - sz) * (y - sz)); if (d > s.Range) continue;
                        heard++;
                        if (labels != null && own > 0 && labels[x + y * W] != 0 && labels[x + y * W] != own) { other++; crossSet.Set(x, y, 1, d); }
                    }
                if (heard == 0) { unheard++; list.Add($"{s.File} at cell {scx},{scz} (range {s.Range:0.#} cells) reaches no walkable cell"); }
                if (other > 0) cross++;
                float floorY = S.InMap(scx, scz) ? S.Floor[scx + scz * W] : 0f;
                if (Math.Abs(s.Pos.y - floorY) > T.SoundHeight) high++;
            }
            Emit("sound.through_rooms", crossSet, new[] { "firstPerson", "follow", "table" }, true, "cells", 0f,
                (s, t) => "sounds have no rooms (Audio/LokiAudio.cs:339-354): limit each placed sound to its own room's cells on indoor maps",
                "walkable cells of other rooms within a placed sound's range", metresFp: false);
            var m = R.M("H sounds"); m["sources"] = S.Sounds.Count; m["unheard"] = unheard; m["heardInOtherRooms"] = cross; m["farFromFloor"] = high;
            if (unheard > 0)
                R.Findings.Add(new Finding { Kind = "sound.unheard", Severity = "low", Views = new[] { "all" }, Count = unheard, Measure = new Dictionary<string, object> { ["value"] = unheard, ["unit"] = "sources" }, Cause = list.Take(6).ToList(), Fix = "a sound no one can hear: move it or drop it (it costs a voice of the 24 every cycle, LokiAudio.cs:356-362)" });
            if (high > 0)
                R.Findings.Add(new Finding { Kind = "sound.height", Severity = "info", Views = new[] { "firstPerson" }, Count = high, Measure = new Dictionary<string, object> { ["value"] = high, ["unit"] = "sources", ["threshold"] = T.SoundHeight }, Fix = "info: the height of a placed sound is ignored (2-D distance)" });
        }

        // ------------------------------------------------------------------ I1, I2, I4 rooms
        private void Rooms()
        {
            var m = R.M("I rooms");
            if (S.Indoor == "no") { m["indoor"] = false; return; }
            m["indoor"] = S.Indoor;
            int W = S.W, H = S.H; float eye = Views.EyeCells;
            var labels = S.Rooms?.Labels;
            int roomCount = labels != null ? S.Rooms.Rooms.Count : 1;
            Func<int, int> roomOf = i => labels != null ? labels[i] : (S.Walk[i] ? 1 : 0);
            var openSet = new CellSet(W, H); var lowSet = new CellSet(W, H);
            var col = new List<(float y, int t)>();
            var open = new int[roomCount + 1]; var walk = new int[roomCount + 1]; var rim = new int[roomCount + 1]; var low = new int[roomCount + 1];
            var openCells = new List<int>[roomCount + 1]; var lowCells = new List<int>[roomCount + 1];
            for (int k = 0; k <= roomCount; k++) { openCells[k] = new List<int>(); lowCells[k] = new List<int>(); }
            var rimSeen = new bool[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = x + y * W; if (!S.Walk[i]) continue;
                    int room = roomOf(i); if (room <= 0 || room > roomCount) continue;
                    walk[room]++;
                    float F = S.Floor[i];
                    // I1: nothing drawn above the eye within 10 cells: the (indoor) sky
                    G.Column(x + 0.5f, y + 0.5f, col, Drawn);
                    bool lid = false; foreach (var (hy, t) in col) if (hy > F + eye && hy < F + eye + T.RoomCeilingSearch) { lid = true; break; }
                    if (!lid) { open[room]++; openCells[room].Add(i); }
                    // I2: the ring of non-walkable cells next to the floor: how high is what is drawn there
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy; if (!S.InMap(nx, ny)) continue;
                        int j = nx + ny * W; if (S.Walk[j] || rimSeen[j]) continue; rimSeen[j] = true;
                        rim[room]++;
                        float top = G.MaxYInCell(nx, ny, Solid); // every drawn surface over the cell's square, thin vertical walls included
                        if (top - F < eye + T.LowWallMargin) { low[room]++; lowCells[room].Add(j); }
                    }
                }
            var rows = new List<object>();
            for (int k = 1; k <= roomCount; k++)
            {
                if (walk[k] == 0) continue;
                float openShare = open[k] / (float)walk[k], lowShare = rim[k] > 0 ? low[k] / (float)rim[k] : 0f;
                rows.Add(new Dictionary<string, object> { ["room"] = k, ["walkable"] = walk[k], ["openCeilingShare"] = openShare, ["rimCells"] = rim[k], ["lowRimShare"] = lowShare });
                if (openShare > T.OpenCeilingShare) foreach (var c in openCells[k]) openSet.Set(c % W, c / W, 1, openShare);
                if (lowShare > T.LowWallShare) foreach (var c in lowCells[k]) lowSet.Set(c % W, c / W, 1, lowShare);
            }
            m["rooms"] = rows;
            m["unassignedModels"] = S.Rooms?.Unassigned ?? 0;
            Emit("room.open_ceiling", openSet, FP, false, "share", T.OpenCeilingShare,
                (s, t) => "an indoor room with no ceiling (RO's camera looks down): a ceiling lid or a dark cap over the room in first person",
                "walkable cells with nothing drawn from the eye up to 10 cells above it, in rooms where they are more than half", metresFp: false);
            Emit("room.low_walls", lowSet, FP, false, "share", T.LowWallShare,
                (s, t) => "the room's walls end below the eye: the void / the culled rooms show over them in first person: raise the walls in first person, or a dark band above them",
                "non-walkable cells next to a room's floor whose highest drawn surface is below the eye + 0.3 cells, in rooms where they are more than a quarter of the rim", metresFp: false);
            if ((S.Rooms?.Unassigned ?? 0) > 0)
                R.Findings.Add(new Finding { Kind = "room.unassigned", Severity = "low", Views = new[] { "all" }, Count = S.Rooms.Unassigned, Measure = new Dictionary<string, object> { ["value"] = S.Rooms.Unassigned, ["unit"] = "models" }, Fix = "models in no room are never drawn on this map (Data/RoomTools.cs:383-389): assign them by hand if they are seen" });
        }

        // ------------------------------------------------------------------ J1, J2 the table
        private void Table()
        {
            var m = R.M("J table");
            float mnx = float.MaxValue, mxx = float.MinValue, mnz = float.MaxValue, mxz = float.MinValue;
            for (int t = 0; t < S.Count; t++)
            {
                if (S.Kind[t] == TriKind.Water) continue;
                for (int k = 0; k < 3; k++) { float x = S.P[t * 9 + k * 3], z = S.P[t * 9 + k * 3 + 2]; mnx = Math.Min(mnx, x); mxx = Math.Max(mxx, x); mnz = Math.Min(mnz, z); mxz = Math.Max(mxz, z); }
            }
            int wx0 = int.MaxValue, wx1 = -1, wy0 = int.MaxValue, wy1 = -1;
            for (int y = 0; y < S.H; y++) for (int x = 0; x < S.W; x++) if (S.Walk[x + y * S.W]) { wx0 = Math.Min(wx0, x); wx1 = Math.Max(wx1, x); wy0 = Math.Min(wy0, y); wy1 = Math.Max(wy1, y); }
            float ex = mxx - mnx, ez = mxz - mnz, big = Math.Max(ex, ez);
            float metres = big * Views.TableMetresPerCell, fit = metres > 0 ? T.TableReachMetres / metres : 1f;
            m["drawnExtentCells"] = new[] { ex, ez }; m["walkableBox"] = new[] { wx0, wy0, wx1, wy1 }; m["metresAtZoom1"] = metres; m["zoomToFit"] = fit;
            R.MapFacts["on the table"] = $"drawn {ex:0} x {ez:0} cells = {ex * Views.TableMetresPerCell:0.##} x {ez * Views.TableMetresPerCell:0.##} m at zoom 1; fits {T.TableReachMetres} m at zoom {fit:0.##}{(fit < Views.TableMinScale ? " (below the 0.15 minimum: never fits)" : "")}";
            if (fit < Views.TableMinScale)
                R.Findings.Add(new Finding { Kind = "table.size", Severity = "low", Views = new[] { "table" }, Count = 1, Measure = new Dictionary<string, object> { ["value"] = fit, ["unit"] = "zoom", ["threshold"] = Views.TableMinScale }, Fix = "the whole map never fits the table: a lower minimum zoom for this map, or accept panning" });
            // J2: geometry taller than a standing head at zoom 1
            float limit = (T.StandingHeadMetres - Views.TableHeight) / Views.TableMetresPerCell;
            var tall = new List<string>(); int tallN = 0; float tallest = 0f;
            foreach (var inst in S.Instances)
            {
                if (inst.Missing || inst.Tris == 0) continue;
                int cx = Mathf.Clamp(Mathf.FloorToInt((inst.Min.x + inst.Max.x) * 0.5f), 0, S.W - 1), cz = Mathf.Clamp(Mathf.FloorToInt((inst.Min.z + inst.Max.z) * 0.5f), 0, S.H - 1);
                float h = inst.Max.y - S.Floor[cx + cz * S.W]; tallest = Math.Max(tallest, h);
                if (h > limit) { tallN++; if (tall.Count < 6) tall.Add($"model #{inst.Index} {inst.File} {h:0} cells = {h * Views.TableMetresPerCell:0.##} m above the table"); }
            }
            m["tallestModelCells"] = tallest; m["tallerThanHead"] = tallN;
            if (tallN > 0)
                R.Findings.Add(new Finding { Kind = "table.tall", Severity = "low", Views = new[] { "table" }, Count = tallN, Measure = new Dictionary<string, object> { ["value"] = tallest, ["unit"] = "cells", ["threshold"] = limit }, Cause = tall, Fix = "models that reach a standing player's eyes (1.6 m) at table zoom 1: clip them at a height on the table, or a lower default zoom" });
        }

        // ------------------------------------------------------------------ F1 point lights
        private void Lights()
        {
            int W = S.W, H = S.H; var set = new CellSet(W, H); int maxN = 0;
            if (S.Lights.Count > Views.MaxPointLights)
            {
                Parallel.For(0, H, y =>
                {
                    for (int x = 0; x < W; x++)
                    {
                        if (!S.Walk[x + y * W]) continue;
                        int n = 0;
                        foreach (var l in S.Lights) { float dx = l.Pos.x - (x + 0.5f), dz = l.Pos.z - (y + 0.5f); float reach = l.Range + T.LightReach; if (dx * dx + dz * dz <= reach * reach) n++; }
                        if (n > Views.MaxPointLights * 2) set.Set(x, y, 1, n); else if (n > Views.MaxPointLights) set.Set(x, y, 2, n);
                        if (n > maxN) maxN = n; // a benign race: only the metric
                    }
                });
            }
            Emit("light.popping", set, new[] { "firstPerson", "follow" }, false, "lights", Views.MaxPointLights,
                (s, t) => "more map lights reach the view here than the 16 Loki draws (World/WorldLook.cs:94-109): lights switch as you walk; more lights per frame, or fade by distance",
                "lights whose range + 30 cells reaches the cell", metresFp: false);
            var m = R.M("F1 light.popping"); m["lights"] = S.Lights.Count; foreach (var kv in Counts(set)) m[kv.Key] = kv.Value;
        }

        // ------------------------------------------------------------------ F3, F5 fog and the lightmap (info)
        private void FogAndLightmap()
        {
            var m = R.M("F3 fog.edge");
            bool indoor = S.Indoor != "no";
            m["entry"] = S.HasFogEntry; m["indoor"] = indoor;
            if (S.HasFogEntry) { m["nearCells"] = S.FogNear * Views.FogCellsPerUnit; m["farCells"] = S.FogFar * Views.FogCellsPerUnit; }
            R.MapFacts["fog"] = indoor ? "none (indoor)" : S.HasFogEntry ? $"near {S.FogNear * Views.FogCellsPerUnit:0} far {S.FogFar * Views.FogCellsPerUnit:0} cells ({S.FogFar * Views.FogCellsPerUnit * Views.FirstPersonMetresPerCell:0} m in first person), Follow and First person only" : "none (no fogparametertable.txt entry)";
            var l = R.M("F5 lightmap.scale"); l["lightmaps"] = S.Gnd.LightmapCount; l["texelsPerCell"] = S.Gnd.LightmapW / 2f; l["centimetresPerTexelFirstPerson"] = 100f * Views.FirstPersonMetresPerCell / Math.Max(1f, S.Gnd.LightmapW / 2f);
        }
    }
}
