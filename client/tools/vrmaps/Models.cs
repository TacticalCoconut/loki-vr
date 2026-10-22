using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Loki.World;
using UnityEngine;

namespace Loki.VrMaps
{
    /// <summary>
    /// Wave 27 (wmodel), next-list item 16 (design note, 2026-10-15: "Single-sided model (RSM) faces are see-through from below or behind
    /// (eaves, balconies, arches): decide per case with the design's outer-wall rule in mind (what faces the playable area must be solid from
    /// there)"): Architect's fix kind M (World/MapFix.cs M / R lines, applied by World/MapLoader.cs AddBackFaces).
    ///
    /// The facts (design notes): an RSM face carries a two-sided flag (Formats/RsmFile.cs:21, :193, every version 1.1-2.3); the
    /// builder draws a flagged face twice, the second copy reversed with negated normals (World/RsmMeshBuilder.cs, the TwoSided path, as
    /// korangar loaders/model/mod.rs:132-142); every other face is drawn once and the model shader culls its back
    /// (Shaders/LokiModel.shader:8 `_Cull` = 2 = Back, :18 `Cull [_Cull]`; nothing sets `_Cull` on map models), as korangar culls opaque
    /// models (graphics/passes/forward/model.rs:562). roBrowser never enables face culling (no CULL_FACE in its renderer), so RO's look in
    /// it had every face from both sides. Seen from behind or below (an eave's underside, a balcony's floor from under it, an arch's
    /// inside) such a face is see-through.
    ///
    /// The decision PER FACE, with the design's outer-wall rule (a face that faces the playable area must be solid from there, the
    /// void side stays see-through): the single-sided faces of a model are grouped into PATCHES (edge-connected faces of one node of one
    /// instance, normals within ~25 degrees: an eave's underside strip, a balcony floor, an arch's soffit); a patch's back side is drawn
    /// (its faces get a back copy) only where
    ///   - a PLAYER VIEW sees its back: the first-person eye over a walkable cell on its back side (up to 40 cells away: the nearest 24,
    ///     then every few to 64 eyes) or that cell's Follow camera, by rays with the renderer's rules (Grid.Ray: culled backs passed,
    ///     cut-out texels passed, an indoor map's room mask), the eye not inside a solid of that same model (a walk-through prop: the
    ///     eye's level rays pass that model's faces from behind in 7 of 10 directions). A face seen only from the Table, from outside the
    ///     map or from below the map stays as the game draws it (the outer-wall rule: the map stays see-through from outside);
    ///   - and per face: its back side is not the void (no walkable cell and no drawn ground within 0.5-4 cells straight out behind it:
    ///     Walls.PlayCells, as the walls' rule); it does not stand in a walkable passage at body height (between two adjacent walkable
    ///     cells, or across a walkable cell's body, 0.3 cells to the eye: drawn it would block a view where the game showed open space the
    ///     player walks into); no other drawn surface lies within 0.03 cells of it facing the way its back copy would (z-fighting).
    /// Models that move, or whose texture moves, are left as they are (their back copy would stand still). A patch drawn that, once the
    /// others are drawn too, no player view sees any more is pruned (it would only cost).
    /// Changes drawing only: no collider, no shadow (LokiModel.shader's shadow pass draws both sides already: Cull Off), no GAT.
    /// </summary>
    public static class ModelFixer
    {
        public sealed class Patch
        {
            public int Id, Inst, Node; public readonly List<int> Tris = new List<int>();
            public Vector3 C, N; public float Area;
            /// <summary>The best view that sees its back (FixCheck levels: 4 first person, 3 Follow, 2 the Table, 1 below / outside, 0 none).</summary>
            public int Level;
            public Vector3 Eye; public int EyeX = -1, EyeY = -1;
            public readonly List<int> Added = new List<int>();
            public int Void, InTheWay, Coplanar, Unseen;
            /// <summary>"under": its back faces down (seen from below: eaves, balconies, ceilings); "behind": seen from behind.</summary>
            public string Kind => N.y > 0.5f ? "under" : "behind";
        }

        public sealed class Stats
        {
            public int Candidates, Patches, Witnessed, WitnessedFp, WitnessedFollow, AddedPatches, AddedTris, Pruned, PrunedTris;
            public int ExVoid, ExInTheWay, ExCoplanar, ExUnseen, SkippedMoving, SkippedLitBackwards, InsideSolidEyes;
            public int ModelTris, SingleTris, TwoSidedTris;
            public long Rays; public double Seconds;
        }

        public const float PatchNormalDot = 0.9f, BackOffset = 0.02f, CoplanarCells = 0.03f;
        public const int EyeRadius = 40, EyesNear = 24, EyesMax = 64, PointsPerPatch = 6;

        // ------------------------------------------------------------------ geometry helpers
        public static Vector3 Centre(Scene s, int t) => (s.V(t, 0) + s.V(t, 1) + s.V(t, 2)) / 3f;
        public static float Area(Scene s, int t) => Vector3.Cross(s.V(t, 1) - s.V(t, 0), s.V(t, 2) - s.V(t, 0)).magnitude * 0.5f;
        private static int RoomAt(Scene s, int x, int y) => s.Rooms?.Labels == null ? 1 : s.InMap(x, y) ? s.Rooms.Labels[x + y * s.W] : 0;
        private static int RoomAtPoint(Scene s, Vector3 p) => RoomAt(s, Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z));

        /// <summary>The single-sided faces that may get a back copy: drawn model faces the file does not mark two-sided, of a model that
        /// does not move (nor its texture), with a normal (not degenerate) and some texel the shader keeps.</summary>
        public static bool Candidate(Scene s, int t)
        {
            if (s.Kind[t] != TriKind.Model || s.Side == null || s.Side[t] != Scene.SideSingle) return false;
            var inst = s.Instances[s.Src[t]];
            if (inst.Animated || inst.TextureAnimated) return false;
            if (s.N[t * 3] == 0f && s.N[t * 3 + 1] == 0f && s.N[t * 3 + 2] == 0f) return false;
            // a face the builder lights from its other side (a smoothing group's average pointing away): its back copy would be lit
            // inside-out too (Architect's check M2); left as the game draws it
            if (s.Lit != null && s.Lit[t] == 2) return false;
            return s.Opaque7[t] > 0;
        }

        /// <summary>The patches: candidate faces of one node of one instance joined across shared edges (both ends within 0.001 cells)
        /// when their normals agree (dot over <see cref="PatchNormalDot"/>).</summary>
        public static List<Patch> Patches(Scene s, Func<int, bool> candidate)
        {
            var tris = new List<int>(); for (int t = 0; t < s.Count; t++) if (candidate(t)) tris.Add(t);
            var parent = new Dictionary<int, int>(); foreach (var t in tris) parent[t] = t;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            (int, int, int) Q(Vector3 v) => ((int)Math.Round(v.x * 1000), (int)Math.Round(v.y * 1000), (int)Math.Round(v.z * 1000));
            var edges = new Dictionary<(int, int, (int, int, int), (int, int, int)), List<int>>();
            foreach (var t in tris)
                for (int k = 0; k < 3; k++)
                {
                    var a = Q(s.V(t, k)); var b = Q(s.V(t, (k + 1) % 3));
                    if (a.CompareTo(b) > 0) { var x = a; a = b; b = x; }
                    var key = (s.Src[t], s.Node[t], a, b);
                    if (!edges.TryGetValue(key, out var l)) edges[key] = l = new List<int>();
                    l.Add(t);
                }
            foreach (var l in edges.Values)
                for (int i = 0; i < l.Count; i++) for (int j = i + 1; j < l.Count; j++)
                {
                    int a = l[i], b = l[j];
                    if (Vector3.Dot(s.Normal(a), s.Normal(b)) < PatchNormalDot) continue;
                    int ra = Find(a), rb = Find(b); if (ra != rb) parent[ra] = rb;
                }
            var by = new Dictionary<int, Patch>();
            foreach (var t in tris)
            {
                int r = Find(t);
                if (!by.TryGetValue(r, out var p)) by[r] = p = new Patch { Inst = s.Src[t], Node = s.Node[t] };
                p.Tris.Add(t);
            }
            var list = by.Values.ToList(); int id = 0;
            foreach (var p in list)
            {
                p.Id = id++; var c = Vector3.zero; var n = Vector3.zero; float area = 0f;
                foreach (var t in p.Tris) { float a = Math.Max(Area(s, t), 1e-6f); c += Centre(s, t) * a; n += s.Normal(t) * a; area += a; }
                p.C = c / area; p.N = n.normalized; p.Area = area;
                if (p.N == Vector3.zero) p.N = s.Normal(p.Tris[0]);
            }
            return list;
        }

        /// <summary>Up to <see cref="PointsPerPatch"/> faces spread over the patch (the largest first, then the farthest from those).</summary>
        public static List<int> Spread(Scene s, List<int> tris)
        {
            if (tris.Count <= PointsPerPatch) return tris.ToList();
            var pick = new List<int> { tris.OrderByDescending(t => Area(s, t)).First() };
            while (pick.Count < PointsPerPatch)
            {
                int best = -1; float bd = -1f;
                foreach (var t in tris) { if (pick.Contains(t)) continue; var c = Centre(s, t); float d = pick.Min(q => (Centre(s, q) - c).sqrMagnitude); if (d > bd) { bd = d; best = t; } }
                if (best < 0) break; pick.Add(best);
            }
            return pick;
        }

        /// <summary>Walkable cells within <paramref name="radius"/> cells (horizontally) of c whose first-person eye is on the back side
        /// of a face with normal n (more than 0.05 cells behind its plane), nearest first.</summary>
        private static List<(float d, int x, int y)> BackCells(Scene s, Vector3 c, Vector3 n, int radius)
        {
            var cells = new List<(float d, int x, int y)>();
            int cx = Mathf.FloorToInt(c.x), cz = Mathf.FloorToInt(c.z);
            for (int y = Math.Max(0, cz - radius); y <= Math.Min(s.H - 1, cz + radius); y++)
                for (int x = Math.Max(0, cx - radius); x <= Math.Min(s.W - 1, cx + radius); x++)
                {
                    if (!s.Walk[x + y * s.W]) continue;
                    float dx = x + 0.5f - c.x, dz = y + 0.5f - c.z, d = (float)Math.Sqrt(dx * dx + dz * dz); if (d > radius) continue;
                    var eye = new Vector3(x + 0.5f, s.Floor[x + y * s.W] + Views.EyeCells, y + 0.5f);
                    if (Vector3.Dot(eye - c, n) > -0.05f) continue;
                    cells.Add((d, x, y));
                }
            cells.Sort((a, b) => a.d.CompareTo(b.d));
            return cells;
        }

        /// <summary>Whether a ray from the eye reaches the point just behind a face (the eye on the face's back side, in the point's room on
        /// an indoor map with rooms, nothing drawn between).</summary>
        public static bool SeesBack(Scene s, Grid g, Grid.RayState st, Vector3 nb, Vector3 eye, int eyeRoom, Vector3 p, ref long rays)
        {
            var d = p - eye; float len = d.magnitude; if (len < 1e-3f) return true;
            if (Vector3.Dot(eye - p, nb) <= 0.01f) return false;
            int room = RoomAtPoint(s, p); if (room != eyeRoom) return false;
            rays++;
            var h = g.Ray(st, eye, d / len, 0f, len, t => s.Kind[t] != TriKind.Water, s.Rooms?.Labels == null ? (Func<Vector3, bool>)null : q => RoomAtPoint(s, q) == room);
            return h.Tri < 0;
        }

        /// <summary>The eye stands inside a solid of this model (a walk-through prop: a pillar, a crate on walkable cells, a closed tower's
        /// base): all 8 level rays and the ray up meet the model's faces from behind first (the ray down may meet nothing: RO's models are
        /// open at the bottom). An arch or a gate the player walks through is open at its ends (its level rays along the passage leave the
        /// model): its inside is seen by the eye, not a solid's (run 4: prontera's clock towers, the design's "arches").</summary>
        public static bool InsideSolid(Scene s, Grid g, Grid.RayState st, Vector3 eye, int inst)
        {
            var box = s.Instances[inst]; if (box.Tris == 0) return false;
            if (eye.x < box.Min.x - 0.01f || eye.x > box.Max.x + 0.01f || eye.z < box.Min.z - 0.01f || eye.z > box.Max.z + 0.01f || eye.y < box.Min.y - 0.01f || eye.y > box.Max.y + 0.01f) return false;
            float reach = (box.Max - box.Min).magnitude + 0.5f;
            for (int k = 0; k < 9; k++)
            {
                Vector3 d = k < 8 ? new Vector3((float)Math.Cos(k * Math.PI / 4), 0f, (float)Math.Sin(k * Math.PI / 4)) : Vector3.up;
                var h = g.Ray(st, eye, d, 0f, reach, t => s.Kind[t] == TriKind.Model && s.Src[t] == inst);
                if (!(h.BackTri >= 0 && (h.Tri < 0 || h.BackT < h.T))) return false;
            }
            return true;
        }

        /// <summary>The best player view of the patch's back: <see cref="FixCheck.SeenFirstPerson"/> (an eye over a walkable cell on its back
        /// side, not inside a solid of the same model), <see cref="FixCheck.SeenFollow"/> (the nearest 12 cells' Follow cameras), else
        /// <see cref="FixCheck.SeenNowhere"/> (the Table and below: see <see cref="OtherViews"/>).</summary>
        public static int Witness(Scene s, Grid g, Grid.RayState st, Patch p, List<int> sample, ref long rays, ConcurrentDictionary<(int, int), bool> solid, out Vector3 eyeAt, out int ex, out int ey)
        {
            eyeAt = default; ex = ey = -1;
            // the points: each sampled face's centre, and on a face larger than a cell (a roof's triangle that runs out over its eave) three
            // more, 80 % of the way to its corners (prontera's warehouses: the eave seen from the street, the centre over the closed walls)
            var nb = -p.N; var pts = new List<Vector3>(); var ptTri = new List<int>();
            foreach (var t in sample)
            {
                var c = Centre(s, t); var off = s.Normal(t) * BackOffset;
                pts.Add(c - off); ptTri.Add(t);
                if (Area(s, t) > 1f) for (int k = 0; k < 3; k++) { pts.Add(c + (s.V(t, k) - c) * 0.8f - off); ptTri.Add(t); }
            }
            var cells = BackCells(s, p.C, p.N, EyeRadius);
            int step = cells.Count <= EyesMax ? 1 : Math.Max(1, (cells.Count - EyesNear) / (EyesMax - EyesNear));
            for (int i = 0, tests = 0; i < cells.Count && tests < EyesMax; i += i < EyesNear ? 1 : step, tests++)
            {
                var (_, x, y) = cells[i]; var eye = new Vector3(x + 0.5f, s.Floor[x + y * s.W] + Views.EyeCells, y + 0.5f); int room = RoomAt(s, x, y);
                for (int k = 0; k < pts.Count; k++)
                {
                    if (!SeesBack(s, g, st, s.Normal(ptTri[k]) * -1f, eye, room, pts[k], ref rays)) continue;
                    if (solid.GetOrAdd((x + y * s.W, p.Inst), _ => InsideSolid(s, g, st, eye, p.Inst))) break; // that eye is inside this model
                    eyeAt = eye; ex = x; ey = y; return FixCheck.SeenFirstPerson;
                }
            }
            Views.FollowEye(out float fb, out float fu);
            for (int i = 0; i < Math.Min(12, cells.Count); i++)
            {
                var (_, x, y) = cells[i]; var feet = new Vector3(x + 0.5f, s.Floor[x + y * s.W], y + 0.5f); int room = RoomAt(s, x, y);
                var away = new Vector3(feet.x - p.C.x, 0, feet.z - p.C.z); if (away.sqrMagnitude < 1e-4f) away = new Vector3(nb.x, 0, nb.z); if (away.sqrMagnitude < 1e-4f) away = Vector3.forward; away.Normalize();
                var eye = feet + away * fb + Vector3.up * fu;
                // the client does not draw a model's back copies in Follow while the eye is inside the model's box (LokiModel.shader)
                if (InBox(s.Instances[p.Inst], eye)) continue;
                for (int k = 0; k < pts.Count; k++)
                    if (SeesBack(s, g, st, s.Normal(ptTri[k]) * -1f, eye, room, pts[k], ref rays)) { eyeAt = eye; ex = x; ey = y; return FixCheck.SeenFollow; }
            }
            return FixCheck.SeenNowhere;
        }

        /// <summary>The eye inside a model's whole placed box, 0.25 cells out (Shaders/LokiModel.shader's test for its back copies).</summary>
        public static bool InBox(Scene.Instance i, Vector3 e) =>
            e.x > i.BoxMin.x - 0.25f && e.y > i.BoxMin.y - 0.25f && e.z > i.BoxMin.z - 0.25f && e.x < i.BoxMax.x + 0.25f && e.y < i.BoxMax.y + 0.25f && e.z < i.BoxMax.z + 0.25f;

        /// <summary>Views that are not a player's: <see cref="FixCheck.SeenTable"/> (from outside and above: 160 cells out, 15-75 degrees up)
        /// or <see cref="FixCheck.SeenBelow"/> (from below the map or level from outside), toward the patch's back side.</summary>
        public static int OtherViews(Scene s, Grid g, Grid.RayState st, Patch p, List<int> sample, ref long rays)
        {
            var nb = -p.N; int best = FixCheck.SeenNowhere;
            foreach (var d in Analysis.SurveyDirections)
            {
                if (Vector3.Dot(d, nb) < 0.2f) continue;
                int lv = d.y >= 0.26f ? FixCheck.SeenTable : FixCheck.SeenBelow; if (lv <= best) continue;
                foreach (var t in sample)
                {
                    var pt = Centre(s, t) - s.Normal(t) * BackOffset; int room = RoomAtPoint(s, pt);
                    if (SeesBack(s, g, st, s.Normal(t) * -1f, pt + d * 160f, room, pt, ref rays)) { best = lv; break; }
                }
                if (best == FixCheck.SeenTable) break;
            }
            return best;
        }

        /// <summary>the design's outer-wall rule on a face's BACK side (the side a back copy would show): the void when no cell straight out behind
        /// it (0.5 to <see cref="Walls.PlayCells"/> cells along its back normal, the 2-D cell under each point) is walkable or has a drawn
        /// ground top. The map's ground as the GAME draws it (a hole the fix fills black stays the void), plus the fix's own drawn tops.</summary>
        public static bool BackIsVoid(Scene s, int t)
        {
            var c = Centre(s, t); var nb = -s.Normal(t);
            foreach (float d in new[] { 0.5f, 1f, 2f, 3f, (float)Walls.PlayCells })
            {
                var p = c + nb * d; int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.z);
                if (!s.InMap(x, y)) continue;
                int tile = (x / 2) + (y / 2) * s.Gnd.Width;
                if (s.Walk[x + y * s.W] || (tile < s.TopDrawn.Length && s.TopDrawn[tile])) return false;
            }
            return true;
        }

        /// <summary>The face stands in a walkable passage at body height: it crosses the segment between two adjacent (8-way) walkable cells'
        /// centres at 0.3, 0.9, 1.5 or 2.1 cells over their floors, or a walkable cell's body (its centre's vertical line from 0.3 cells to
        /// the eye). Drawn from behind it would block a view where the game showed open space the player walks into.</summary>
        public static bool InTheWay(Scene s, int t)
        {
            Vector3 a = s.V(t, 0), b = s.V(t, 1), c = s.V(t, 2);
            int x0 = Mathf.FloorToInt(Math.Min(a.x, Math.Min(b.x, c.x))) - 1, x1 = Mathf.FloorToInt(Math.Max(a.x, Math.Max(b.x, c.x))) + 1;
            int z0 = Mathf.FloorToInt(Math.Min(a.z, Math.Min(b.z, c.z))) - 1, z1 = Mathf.FloorToInt(Math.Max(a.z, Math.Max(b.z, c.z))) + 1;
            float ylo = Math.Min(a.y, Math.Min(b.y, c.y)), yhi = Math.Max(a.y, Math.Max(b.y, c.y));
            for (int y = Math.Max(0, z0); y <= Math.Min(s.H - 1, z1); y++)
                for (int x = Math.Max(0, x0); x <= Math.Min(s.W - 1, x1); x++)
                {
                    if (!s.Walk[x + y * s.W]) continue;
                    float f0 = s.Floor[x + y * s.W];
                    if (f0 + Views.EyeCells < ylo - 0.05f || f0 + 0.3f > yhi + 0.05f) continue;
                    var c0 = new Vector3(x + 0.5f, 0, y + 0.5f);
                    if (Crosses(new Vector3(c0.x, f0 + 0.3f, c0.z), new Vector3(c0.x, f0 + Views.EyeCells, c0.z), a, b, c)) return true;
                    foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (1, 1), (1, -1) })
                    {
                        int nx = x + dx, ny = y + dy; if (!s.Walkable(nx, ny)) continue;
                        float f1 = s.Floor[nx + ny * s.W]; var c1 = new Vector3(nx + 0.5f, 0, ny + 0.5f);
                        foreach (var h in new[] { 0.3f, 0.9f, 1.5f, Views.EyeCells })
                            if (Crosses(new Vector3(c0.x, f0 + h, c0.z), new Vector3(c1.x, f1 + h, c1.z), a, b, c)) return true;
                    }
                }
            return false;
        }
        private static bool Crosses(Vector3 p, Vector3 q, Vector3 a, Vector3 b, Vector3 c)
        {
            var d = q - p; float len = d.magnitude; if (len < 1e-6f) return false;
            return Geo.RayTriangle(p, d / len, a, b, c, out float t) && t >= 0f && t <= len;
        }

        /// <summary>Another drawn surface within <see cref="CoplanarCells"/> of the face, facing the way its back copy would (or water), at
        /// two or more of four points inside it: drawn, the back copy would z-fight with it. Each point is tested along the face's normal
        /// (a short ray through it, both ways), so a surface that only touches the face's edge (its neighbour) is not counted: an overlap is.</summary>
        public static bool Coplanar(Scene s, Grid g, int t, Func<int, bool> ignore, Grid.RayState st = null) => Coplanar(s, g, t, ignore, st, out _);
        public static bool Coplanar(Scene s, Grid g, int t, Func<int, bool> ignore, Grid.RayState st, out int other)
        {
            var nb = -s.Normal(t);
            return Overlap(s, g, t, u => u != t && !ignore(u) && (s.Kind[u] == TriKind.Water || Vector3.Dot(s.Normal(u), nb) > 0.7f), st, out other);
        }

        /// <summary>A surface <paramref name="accept"/> takes within <see cref="CoplanarCells"/> of the face at two or more of four points
        /// inside it, tested along the face's normal (a short ray through each point, both ways).</summary>
        public static bool Overlap(Scene s, Grid g, int t, Func<int, bool> accept, Grid.RayState st, out int other)
        {
            var c = Centre(s, t); var nb = -s.Normal(t); int close = 0; other = -1;
            st = st ?? new Grid.RayState(s.Count);
            var pts = new[] { c, c + (s.V(t, 0) - c) * 0.6f, c + (s.V(t, 1) - c) * 0.6f, c + (s.V(t, 2) - c) * 0.6f };
            foreach (var p in pts)
            {
                // the segment through p along the normal, CoplanarCells each way: any surface the filter takes on it counts (the ray keeps
                // only the nearest of a kind, so it starts at the window's edge: a surface beyond the window can never hide one inside it)
                var h = g.Ray(st, p + nb * CoplanarCells, -nb, 0f, 2f * CoplanarCells, accept);
                int o = h.Tri >= 0 ? h.Tri : h.BackTri >= 0 ? h.BackTri : h.WaterTri;
                if (o >= 0) { close++; other = o; if (close >= 2) return true; }
            }
            return false;
        }

        // ------------------------------------------------------------------ the generator
        /// <summary>The model faces to draw from both sides, decided per patch and per face (the class comment), added to
        /// <paramref name="fix"/> (its R line from the scene's RSW).</summary>
        public static List<Patch> Generate(IGameFiles files, string map, string userData, MapFix fix, Scene s0, Stats st, Action<string> log = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var g0 = new Grid(s0);
            for (int t = 0; t < s0.Count; t++)
            {
                if (s0.Kind[t] != TriKind.Model) continue; st.ModelTris++;
                if (s0.Side[t] == Scene.SideSingle) st.SingleTris++; else st.TwoSidedTris++;
                if (s0.Side[t] == Scene.SideSingle && (s0.Instances[s0.Src[t]].Animated || s0.Instances[s0.Src[t]].TextureAnimated)) st.SkippedMoving++;
                else if (s0.Side[t] == Scene.SideSingle && s0.Lit[t] == 2) st.SkippedLitBackwards++;
            }
            var patches = Patches(s0, t => Candidate(s0, t));
            st.Candidates = patches.Sum(p => p.Tris.Count); st.Patches = patches.Count;
            var solid = new ConcurrentDictionary<(int, int), bool>();
            long rays = 0;
            Parallel.For(0, patches.Count, () => (new Grid.RayState(s0.Count), 0L), (i, ls, acc) =>
            {
                var p = patches[i]; long r = 0;
                p.Level = Witness(s0, g0, acc.Item1, p, Spread(s0, p.Tris), ref r, solid, out p.Eye, out p.EyeX, out p.EyeY);
                if (p.Level >= FixCheck.SeenFollow)
                    foreach (var t in p.Tris)
                    {
                        if (BackIsVoid(s0, t)) { p.Void++; continue; }
                        if (InTheWay(s0, t)) { p.InTheWay++; continue; }
                        if (Coplanar(s0, g0, t, _ => false, acc.Item1)) { p.Coplanar++; continue; }
                        p.Added.Add(t);
                    }
                return (acc.Item1, acc.Item2 + r);
            }, acc => System.Threading.Interlocked.Add(ref rays, acc.Item2));
            st.InsideSolidEyes = solid.Count(kv => kv.Value);
            // faces that lie on one another facing the same way (doubled layers of a model: the game draws their fronts on one another
            // already): only the first (the lowest in the scene) gets a back copy, else the copies would z-fight
            var addedSet = new HashSet<int>(patches.SelectMany(p => p.Added));
            {
                // both ways: a face overlapped at two of its points by a lower one goes; a face that overlaps two points of a lower one goes
                var stq = new Grid.RayState(s0.Count); var dropped = new HashSet<int>();
                foreach (var t in addedSet.OrderBy(x => x))
                {
                    var n = s0.Normal(t);
                    if (Overlap(s0, g0, t, u => u < t && addedSet.Contains(u) && !dropped.Contains(u) && Vector3.Dot(s0.Normal(u), n) > 0.7f, stq, out _)) { dropped.Add(t); continue; }
                    for (int k = 0; k < 8 && Overlap(s0, g0, t, u => u > t && addedSet.Contains(u) && !dropped.Contains(u) && Vector3.Dot(s0.Normal(u), n) > 0.7f, stq, out int o) && o >= 0; k++) dropped.Add(o);
                }
                foreach (var p in patches)
                {
                    if (p.Added.Count == 0) continue;
                    int before = p.Added.Count;
                    p.Added.RemoveAll(t => dropped.Contains(t));
                    p.Coplanar += before - p.Added.Count;
                }
            }
            // a patch partly left out: a player must still see the back of what is drawn (the faces added), else nothing is drawn
            Parallel.For(0, patches.Count, () => (new Grid.RayState(s0.Count), 0L), (i, ls, acc) =>
            {
                var p = patches[i]; long r = 0;
                if (p.Added.Count == 0 || p.Added.Count == p.Tris.Count) return acc;
                if (Witness(s0, g0, acc.Item1, p, Spread(s0, p.Added), ref r, solid, out p.Eye, out p.EyeX, out p.EyeY) < FixCheck.SeenFollow) { p.Unseen = p.Added.Count; p.Added.Clear(); }
                return (acc.Item1, acc.Item2 + r);
            }, acc => System.Threading.Interlocked.Add(ref rays, acc.Item2));
            // z-fighting on the map drawn with the back copies (the check's own test, M3): a copy with another surface facing its way
            // within 0.03 cells at two of its points, whichever surfaces they are (copies included) is left out
            var chosen = patches.Where(p => p.Added.Count > 0).ToList();
            SetModels(fix, s0, chosen);
            if (chosen.Count > 0)
            {
                var sz = Scene.Build(files, map, new Scene.Options { UserData = userData, Fix = fix });
                var gz = new Grid(sz); var mapz = Map(s0, sz);
                var drop = new System.Collections.Concurrent.ConcurrentBag<int>();
                Parallel.For(0, chosen.Count, () => new Grid.RayState(sz.Count), (i, ls, rs) =>
                {
                    foreach (var t in chosen[i].Added)
                    {
                        if (!mapz.TryGetValue((s0.Src[t], s0.Node[t], s0.Face[t], s0.Side[t]), out int o) || !mapz.TryGetValue((s0.Src[t], s0.Node[t], s0.Face[t], Scene.SideAdded), out int c)) continue;
                        if (Coplanar(sz, gz, o, u => u == o || u == c, rs)) drop.Add(t);
                    }
                    return rs;
                }, _ => { });
                if (!drop.IsEmpty)
                {
                    var dset = new HashSet<int>(drop);
                    foreach (var p in chosen)
                    {
                        int before = p.Added.Count; p.Added.RemoveAll(t => dset.Contains(t)); int gone = before - p.Added.Count;
                        if (gone == 0) continue;
                        p.Coplanar += gone;
                        long r = 0;
                        if (p.Added.Count > 0 && Witness(s0, g0, new Grid.RayState(s0.Count), p, Spread(s0, p.Added), ref r, solid, out p.Eye, out p.EyeX, out p.EyeY) < FixCheck.SeenFollow) { p.Unseen += p.Added.Count; p.Added.Clear(); }
                        rays += r;
                    }
                    chosen = chosen.Where(p => p.Added.Count > 0).ToList();
                    SetModels(fix, s0, chosen);
                }
            }
            // the prune pass: on the map drawn with every chosen back copy, a patch no player view sees any more is left out (hidden by others)
            if (chosen.Count > 0)
            {
                var s1 = Scene.Build(files, map, new Scene.Options { UserData = userData, Fix = fix });
                var g1 = new Grid(s1); var keep = new bool[chosen.Count];
                var map1 = Map(s0, s1);
                Parallel.For(0, chosen.Count, () => (new Grid.RayState(s1.Count), 0L), (i, ls, acc) =>
                {
                    var p = chosen[i]; long r = 0;
                    var q = Translate(s0, p, map1); var lv = Witness(s1, g1, acc.Item1, q, Spread(s1, q.Added), ref r, solid, out _, out _, out _);
                    keep[i] = lv >= FixCheck.SeenFollow;
                    return (acc.Item1, acc.Item2 + r);
                }, acc => System.Threading.Interlocked.Add(ref rays, acc.Item2));
                for (int i = 0; i < chosen.Count; i++) if (!keep[i]) { st.Pruned++; st.PrunedTris += chosen[i].Added.Count; chosen[i].Added.Clear(); }
                chosen = chosen.Where(p => p.Added.Count > 0).ToList();
                SetModels(fix, s0, chosen);
            }
            foreach (var p in patches)
            {
                if (p.Level >= FixCheck.SeenFollow) { st.Witnessed++; if (p.Level == FixCheck.SeenFirstPerson) st.WitnessedFp++; else st.WitnessedFollow++; }
                st.ExVoid += p.Void; st.ExInTheWay += p.InTheWay; st.ExCoplanar += p.Coplanar; st.ExUnseen += p.Unseen;
            }
            st.AddedPatches = chosen.Count; st.AddedTris = chosen.Sum(p => p.Added.Count); st.Rays = rays; st.Seconds = sw.Elapsed.TotalSeconds;
            return patches;
        }

        /// <summary>Quick facts (the measuring step): the generator on the named maps, the counts and the time, nothing written.</summary>
        public static int Measure(IGameFiles files, string map, string userData, string baseDir)
        {
            MapFix fix = null; string bp = baseDir == null ? null : Path.Combine(baseDir, map.ToLowerInvariant() + ".txt");
            var s00 = Scene.Build(files, map, new Scene.Options { UserData = userData, Rooms = false });
            if (bp != null && File.Exists(bp)) fix = MapFix.Parse(File.ReadAllText(bp), s00.Gnd.Width);
            var s0 = Scene.Build(files, map, new Scene.Options { UserData = userData, Fix = fix });
            var work = fix != null ? MapFix.Parse(File.ReadAllText(bp), s0.Gnd.Width) : new MapFix { Map = s0.Map, Width = s0.Gnd.Width };
            var st = new Stats();
            var patches = Generate(files, map, userData, work, s0, st);
            Console.WriteLine($"{map}: model tris {st.ModelTris} (single {st.SingleTris}, two-sided copies {st.TwoSidedTris}, single on moving models {st.SkippedMoving}); candidates {st.Candidates} in {st.Patches} patches; " +
                $"witnessed {st.Witnessed} (fp {st.WitnessedFp}, follow {st.WitnessedFollow}); added {st.AddedPatches} patches / {st.AddedTris} faces; pruned {st.Pruned} ({st.PrunedTris}); " +
                $"excluded void {st.ExVoid}, in the way {st.ExInTheWay}, coplanar {st.ExCoplanar}; eyes inside solids {st.InsideSolidEyes}; rays {st.Rays}; {st.Seconds:0.0} s");
            var dbg = Environment.GetEnvironmentVariable("ARCHITECT_MODEL_FACES"); // "inst:node:face,inst:node:face": the faces' geometry and overlaps
            if (!string.IsNullOrEmpty(dbg))
            {
                var g0 = new Grid(s0); var stq = new Grid.RayState(s0.Count);
                foreach (var f in dbg.Split(','))
                {
                    var q = f.Split(':'); int fi = int.Parse(q[0]), fn = int.Parse(q[1]), ff = int.Parse(q[2]);
                    for (int t = 0; t < s0.Count; t++)
                    {
                        if (s0.Kind[t] != TriKind.Model || s0.Src[t] != fi || s0.Node[t] != fn || s0.Face[t] != ff) continue;
                        Console.WriteLine($"  tri {t} side {s0.Side[t]}: {s0.V(t, 0)} {s0.V(t, 1)} {s0.V(t, 2)} n {s0.Normal(t)} opaque7 {s0.Opaque7[t]}; added {patches.Any(p => p.Added.Contains(t))}");
                        var n = s0.Normal(t);
                        bool o1 = Overlap(s0, g0, t, u => u != t && s0.Kind[u] == TriKind.Model && Vector3.Dot(s0.Normal(u), n) > 0.7f, stq, out int oo);
                        Console.WriteLine($"    same-facing overlap {o1} (other {oo}: {(oo >= 0 ? s0.Owner(oo) + " face " + s0.Node[oo] + ":" + s0.Face[oo] : "-")})");
                    }
                }
            }
            var dbgInst = Environment.GetEnvironmentVariable("ARCHITECT_MODEL_INST"); // an instance's patches: why each is drawn or not
            if (!string.IsNullOrEmpty(dbgInst))
                foreach (var p in patches.Where(p => p.Inst.ToString() == dbgInst).OrderByDescending(p => p.Tris.Count))
                    Console.WriteLine($"  #{p.Inst} node {p.Node} patch {p.Id}: {p.Tris.Count} faces (first {s0.Face[p.Tris[0]]}) centre {p.C} normal {p.N}: seen {FixCheck.LevelName[p.Level]} from {p.EyeX},{p.EyeY}; added {p.Added.Count}, void {p.Void}, in the way {p.InTheWay}, z-fight {p.Coplanar}, unseen where drawn {p.Unseen}");
            if (Environment.GetEnvironmentVariable("ARCHITECT_MODEL_BATCHES") == "1")
            {
                // the client's batches (World/MapLoader.cs: per material and block of the instance's position), for block sizes
                foreach (int bs in new[] { 32, 64, 128, 256, 100000 })
                {
                    var keys = new HashSet<(int, string, int, int)>();
                    foreach (var p in patches) foreach (var t in p.Added)
                    {
                        var inst = s0.Instances[s0.Src[t]]; var M = RsmMeshBuilder.InstanceMatrix(inst.RoPos, inst.RoRot, inst.RoScale, s0.Centre);
                        keys.Add((0, s0.Mats[s0.MatOf[t]].Texture + "|" + inst.Alpha.ToString("F2"), Mathf.FloorToInt(M.m03 / bs), Mathf.FloorToInt(M.m23 / bs)));
                    }
                    Console.WriteLine($"  batches with blocks of {bs} cells: {keys.Count}");
                }
            }
            if (Environment.GetEnvironmentVariable("ARCHITECT_MODEL_FILES") == "1")
            {
                foreach (var (what, sel) in new (string, Func<Patch, int>)[] { ("added", p => p.Added.Count), ("in the way", p => p.InTheWay), ("void", p => p.Void), ("coplanar", p => p.Coplanar) })
                    Console.WriteLine($"  {what}: " + string.Join(", ", patches.GroupBy(p => Names.Display(s0.Instances[p.Inst].File)).Select(gq => (gq.Key, n: gq.Sum(sel), k: gq.Count(p => sel(p) > 0))).Where(x => x.n > 0).OrderByDescending(x => x.n).Take(10).Select(x => $"{x.Key} {x.n} ({x.k} patches)")));
                foreach (var p in patches.Where(p => p.Added.Count > 0).OrderByDescending(p => p.Added.Sum(t => Area(s0, t))).Take(8))
                    Console.WriteLine($"  added: #{p.Inst} {Names.Display(s0.Instances[p.Inst].File)} node {p.Node} centre {p.C} normal {p.N} faces {p.Added.Count} area {p.Added.Sum(t => Area(s0, t)):0.#}, seen ({FixCheck.LevelName[p.Level]}) from {p.EyeX},{p.EyeY} eye {p.Eye} (floor {(p.EyeX >= 0 ? s0.Floor[p.EyeX + p.EyeY * s0.W] : 0):0.##})");
                foreach (var p in patches.Where(p => p.InTheWay > 0).OrderByDescending(p => p.InTheWay).Take(5))
                    Console.WriteLine($"  in the way: #{p.Inst} {Names.Display(s0.Instances[p.Inst].File)} node {p.Node} centre {p.C} normal {p.N} faces {p.Tris.Count}, eye at {p.EyeX},{p.EyeY}");
            }
            return 0;
        }

        /// <summary>The fix's M lines (and its R line) from the chosen patches' faces.</summary>
        public static void SetModels(MapFix fix, Scene s, IEnumerable<Patch> chosen)
        {
            fix.Models.Clear(); fix.RswBytes = s.RswBytes; fix.RswHash = s.RswHash;
            foreach (var p in chosen)
                foreach (var t in p.Added)
                {
                    var inst = s.Instances[s.Src[t]];
                    if (!fix.Models.TryGetValue(inst.Index, out var m)) fix.Models[inst.Index] = m = new MapFix.ModelFaces { Instance = inst.Index, Bytes = inst.RsmBytes, Hash = inst.RsmHash };
                    m.Add(s.Node[t], s.Face[t]);
                    string cls = p.Kind; if (m.Class.TryGetValue(s.Node[t], out var was) && was != cls) cls = "under+behind"; m.Class[s.Node[t]] = cls;
                }
        }

        /// <summary>The triangle of the fixed scene that is the same face (instance, node, face, side) as one of the plain scene.</summary>
        public static Dictionary<(int, int, int, byte), int> Map(Scene s0, Scene s1)
        {
            var m = new Dictionary<(int, int, int, byte), int>();
            for (int t = 0; t < s1.Count; t++) if (s1.Kind[t] == TriKind.Model) m[(s1.Src[t], s1.Node[t], s1.Face[t], s1.Side[t])] = t;
            return m;
        }
        /// <summary>A patch of the plain scene in the fixed one (the fixed scene inserts each fixed instance's back copies after its own
        /// faces, so the indices move: mapped by instance, node, face and side).</summary>
        public static Patch Translate(Scene s0, Patch p, Dictionary<(int, int, int, byte), int> map1)
        {
            var q = new Patch { Id = p.Id, Inst = p.Inst, Node = p.Node, C = p.C, N = p.N, Area = p.Area, Level = p.Level };
            foreach (var t in p.Tris) if (map1.TryGetValue((s0.Src[t], s0.Node[t], s0.Face[t], s0.Side[t]), out int u)) q.Tris.Add(u);
            foreach (var t in p.Added) if (map1.TryGetValue((s0.Src[t], s0.Node[t], s0.Face[t], s0.Side[t]), out int u)) q.Added.Add(u);
            return q;
        }
    }
}
