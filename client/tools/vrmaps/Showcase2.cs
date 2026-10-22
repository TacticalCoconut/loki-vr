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
    /// The showcase of a fixed map (2026-10-15; design note: "render verification screenshots"): where the fix changes the most
    /// (HOTSPOTS: 16-cell windows ranked by the wall surface the fix adds or turns and the floors it draws, the most changed first,
    /// apart from each other), the SPOT for the game's own views at each (the walkable cell whose first-person eye sees the most of the
    /// hotspot's added faces, and the way to look), and the cameras: per hotspot a three-quarter overview, a player's eye from the spot,
    /// a close-up of its tallest changed wall; then the map from outside its edge on four sides and from below. Written next to the fix
    /// file: &lt;map&gt;.showcase2.tsv (name, from x y z, at x y z, fov: map cells, y up) and &lt;map&gt;.spots.tsv (name, cell x, cell y,
    /// look x z, faces seen); the showcase smoke warch2 (LokiGame.WArch2Smoke.cs) reads both.
    /// </summary>
    public static class Showcase2
    {
        public sealed class Hot
        {
            public int Id; public float Score; public Vector3 Centre, Facing; public float Y0 = float.MaxValue, Y1 = float.MinValue;
            public readonly List<Walls.Wall> Walls = new List<Walls.Wall>(); public int Floors;
            public int SpotX = -1, SpotY = -1, SpotSeen; public Vector3 Look = new Vector3(0, 0, 1), LookAt;
            /// <summary>The overview's and the close-up's cameras, chosen by rays (null: the plain three-quarter rule).</summary>
            public Vector3? OverFrom, WallFrom; public Vector3 WallAt; public int OverSeen, WallSeen;
        }

        private struct Item { public float X, Z, Area, Y0, Y1; public Vector3 N; public Walls.Wall W; }

        public static List<Hot> Hotspots(Scene s0, Analysis a0, MapFix fix, int n = 4, int win = 16)
        {
            var items = new List<Item>();
            foreach (var w in a0.WallSet.All)
            {
                var m = w.Front ? fix.FrontMode(w.Tile) : fix.RightMode(w.Tile); if (m == MapFix.WallMode.Keep) continue;
                float lo = Math.Min(Math.Min(w.A0, w.A1), Math.Min(w.B0, w.B1)), hi = Math.Max(Math.Max(w.A0, w.A1), Math.Max(w.B0, w.B1));
                float sg = w.Drawn == Walls.Faces.TowardA ? 1f : -1f; // the added face looks away from the game face
                items.Add(new Item { X = w.Cx, Z = w.Cy, Area = Math.Max(w.Span, 0.1f) * 2f, Y0 = lo, Y1 = hi, N = w.Front ? new Vector3(0, 0, sg) : new Vector3(sg, 0, 0), W = w });
            }
            int gw = s0.Gnd.Width;
            foreach (var t in fix.DrawTops.Concat(fix.Fills.Keys))
            {
                float y = fix.Fills.TryGetValue(t, out var fy) ? fy : s0.Floor[Math.Min(s0.W * s0.H - 1, (t % gw) * 2 + (t / gw) * 2 * s0.W)];
                items.Add(new Item { X = (t % gw) * 2 + 1, Z = (t / gw) * 2 + 1, Area = 4f, Y0 = y, Y1 = y, N = Vector3.up });
            }
            var hots = new List<Hot>(); if (items.Count == 0) return hots;
            int step = win / 2, nx = s0.W / step + 1, nz = s0.H / step + 1;
            var score = new float[nx * nz];
            foreach (var it in items)
                for (int bz = Math.Max(0, (int)(it.Z / step) - 1); bz <= Math.Min(nz - 1, (int)(it.Z / step)); bz++)
                    for (int bx = Math.Max(0, (int)(it.X / step) - 1); bx <= Math.Min(nx - 1, (int)(it.X / step)); bx++)
                        score[bx + bz * nx] += it.Area;
            var used = new List<(float x, float z)>();
            for (int k = 0; k < n; k++)
            {
                int best = -1; float bs = 0f;
                for (int i = 0; i < score.Length; i++)
                {
                    if (score[i] <= bs) continue;
                    float cx = (i % nx) * step + win * 0.5f, cz = (i / nx) * step + win * 0.5f;
                    if (used.Any(u => Math.Abs(u.x - cx) < win * 1.5f && Math.Abs(u.z - cz) < win * 1.5f)) continue;
                    best = i; bs = score[i];
                }
                if (best < 0 || (bs < 4f && hots.Count > 0)) break; // a map whose whole change is small still gets its one hotspot
                float x0 = (best % nx) * step, z0 = (best / nx) * step;
                used.Add((x0 + win * 0.5f, z0 + win * 0.5f));
                var h = new Hot { Id = k + 1, Score = bs }; float wsum = 0f; var c = Vector3.zero;
                foreach (var it in items)
                {
                    if (it.X < x0 || it.X >= x0 + win || it.Z < z0 || it.Z >= z0 + win) continue;
                    c += new Vector3(it.X, (it.Y0 + it.Y1) * 0.5f, it.Z) * it.Area; wsum += it.Area;
                    h.Facing += it.N * it.Area; h.Y0 = Math.Min(h.Y0, it.Y0); h.Y1 = Math.Max(h.Y1, it.Y1);
                    if (it.W != null) h.Walls.Add(it.W); else h.Floors++;
                }
                if (wsum <= 0f) break;
                h.Centre = c / wsum; hots.Add(h);
            }
            return hots;
        }

        /// <summary>For each hotspot, the walkable cell (within 18 cells of it, every second cell) whose first-person eye sees the most of
        /// its added faces, and the way to look (toward the faces it sees).</summary>
        public static void Spots(Scene s1, List<Hot> hots)
        {
            var g = new Grid(s1);
            foreach (var h in hots)
            {
                var cand = new List<(int x, int y)>();
                int cx = Mathf.FloorToInt(h.Centre.x), cz = Mathf.FloorToInt(h.Centre.z);
                for (int y = cz - 18; y <= cz + 18; y += 2) for (int x = cx - 18; x <= cx + 18; x += 2) if (s1.Walkable(x, y)) cand.Add((x, y));
                // (all-maps pass: 8 maps had no walkable cell within 18 cells of a hotspot, e.g. moc_ruins) the nearest walkable cell anywhere
                if (cand.Count == 0)
                {
                    int bx = -1, by = -1; long bd = long.MaxValue;
                    for (int y = 0; y < s1.H; y++) for (int x = 0; x < s1.W; x++)
                        if (s1.Walkable(x, y)) { long dd = (long)(x - cx) * (x - cx) + (long)(y - cz) * (y - cz); if (dd < bd) { bd = dd; bx = x; by = y; } }
                    if (bx >= 0) cand.Add((bx, by));
                }
                cand = cand.OrderBy(p => (p.x - cx) * (p.x - cx) + (p.y - cz) * (p.y - cz)).Take(140).ToList();
                var walls = h.Walls.Take(400).ToList();
                var seen = new int[cand.Count]; var look = new Vector3[cand.Count];
                Parallel.For(0, cand.Count, () => new Grid.RayState(s1.Count), (i, ls, st) =>
                {
                    var (x, y) = cand[i]; var eye = new Vector3(x + 0.5f, s1.Floor[x + y * s1.W] + Views.EyeCells, y + 0.5f);
                    int room = FixCheck.RoomOf(s1, x, y); var sum = Vector3.zero;
                    foreach (var w in walls)
                        if (FixCheck.SeenFromEye(s1, g, st, w, w.Drawn != Walls.Faces.TowardA, eye, room, out var at)) { seen[i]++; var d = at - eye; d.y = 0; if (d.sqrMagnitude > 1e-4f) sum += d.normalized; }
                    look[i] = sum;
                    return st;
                }, _ => { });
                int best = -1;
                for (int i = 0; i < cand.Count; i++) if (best < 0 || seen[i] > seen[best]) best = i;
                if (best < 0) continue;
                h.SpotX = cand[best].x; h.SpotY = cand[best].y; h.SpotSeen = seen[best];
                var lk = look[best]; if (lk.sqrMagnitude < 1e-4f) { lk = h.Centre - new Vector3(h.SpotX + 0.5f, 0, h.SpotY + 0.5f); lk.y = 0; }
                if (lk.sqrMagnitude < 1e-4f) lk = new Vector3(0, 0, 1);
                h.Look = lk.normalized;
                float ey = s1.Floor[h.SpotX + h.SpotY * s1.W] + Views.EyeCells;
                h.LookAt = new Vector3(h.SpotX + 0.5f, Math.Min(ey, (h.Y0 + h.Y1) * 0.5f + 1f), h.SpotY + 0.5f) + h.Look * 10f;
            }
            // run 1 (c_tower4): a three-quarter camera 20 cells out on the faces' side stood inside the maze's wall blocks and showed black;
            // now the overview and the close-up are chosen among candidates by how many of the hotspot's added faces they see (rays)
            foreach (var h in hots)
            {
                var walls = h.Walls.Take(80).ToList(); if (walls.Count == 0) continue;
                var f = h.Facing; f.y = 0; if (f.sqrMagnitude < 1e-4f) f = new Vector3(0, 0, -1); f.Normalize();
                int room = FixCheck.RoomOf(s1, Mathf.FloorToInt(h.Centre.x), Mathf.FloorToInt(h.Centre.z));
                var cands = new List<Vector3>();
                foreach (var d in new[] { 18f, 28f })
                    foreach (var el in new[] { 35f, 55f, 75f })
                        foreach (var az in new[] { 0f, 35f, -35f, 70f, -70f, 180f })
                        {
                            float a = az * Mathf.Deg2Rad, e = el * Mathf.Deg2Rad;
                            var hd = new Vector3(f.x * (float)Math.Cos(a) - f.z * (float)Math.Sin(a), 0, f.x * (float)Math.Sin(a) + f.z * (float)Math.Cos(a));
                            cands.Add(h.Centre + hd * (d * (float)Math.Cos(e)) + Vector3.up * (d * (float)Math.Sin(e)));
                        }
                var score = new int[cands.Count];
                Parallel.For(0, cands.Count, () => new Grid.RayState(s1.Count), (i, ls, st) =>
                {
                    int n = 0; foreach (var w in walls) if (FixCheck.SeenFromEye(s1, g, st, w, w.Drawn != Walls.Faces.TowardA, cands[i], room, out _)) n++;
                    score[i] = n; return st;
                }, _ => { });
                int bi = 0; for (int i = 1; i < cands.Count; i++) if (score[i] > score[bi]) bi = i;
                if (score[bi] > 0) { h.OverFrom = cands[bi]; h.OverSeen = score[bi]; }
                // the close-up: the tallest changed wall that some candidate 6-10 cells out in front of it sees
                foreach (var w in h.Walls.OrderByDescending(x => x.Span).Take(6))
                {
                    float sg2 = w.Drawn == Walls.Faces.TowardA ? 1f : -1f;
                    var n = w.Front ? new Vector3(0, 0, sg2) : new Vector3(sg2, 0, 0);
                    float lo = Math.Min(Math.Min(w.A0, w.A1), Math.Min(w.B0, w.B1)), hi = Math.Max(Math.Max(w.A0, w.A1), Math.Max(w.B0, w.B1));
                    var mid = new Vector3(w.Cx, (lo + hi) * 0.5f, w.Cy);
                    var st2 = new Grid.RayState(s1.Count); Vector3? best = null;
                    foreach (var dist in new[] { 7f, 10f, 5f })
                        foreach (var up in new[] { 2f, 5f, 0.5f })
                            foreach (var side in new[] { 0f, 2.5f, -2.5f })
                            {
                                var c = mid + n * dist + new Vector3(n.z * side, hi - mid.y + up, -n.x * side);
                                if (FixCheck.SeenFromEye(s1, g, st2, w, w.Drawn != Walls.Faces.TowardA, c, room, out _)) { best = c; break; }
                            }
                    if (best != null) { h.WallFrom = best; h.WallAt = mid; break; }
                }
            }
        }

        public static string SpotsTsv(Scene s, List<Hot> hots)
        {
            var inv = CultureInfo.InvariantCulture; var sb = new StringBuilder();
            sb.Append("# Architect showcase spots for ").Append(s.Map).Append(": name, cell x, cell y, look x z (map: x east, z north), added faces the eye sees there; the most changed hotspot first\n");
            foreach (var h in hots.Where(x => x.SpotX >= 0))
                sb.Append($"spot{h.Id}\t{h.SpotX}\t{h.SpotY}\t{h.Look.x.ToString("0.###", inv)} {h.Look.z.ToString("0.###", inv)}\t{h.SpotSeen}\n");
            return sb.ToString();
        }

        public static string Cameras(Scene s, List<Hot> hots)
        {
            var sb = new StringBuilder();
            sb.Append("# Architect showcase cameras (warch2) for ").Append(s.Map).Append(": name, from x y z, at x y z (map cells, y up), fov; the most changed hotspot first, then outside / below\n");
            foreach (var h in hots)
            {
                var f = h.Facing; f.y = 0; if (f.sqrMagnitude < 1e-4f) f = new Vector3(0, 0, -1); f.Normalize();
                var side = new Vector3(-f.z, 0, f.x);
                float hgt = Math.Max(h.Y1 - h.Y0, 2f);
                Line(sb, $"hot{h.Id}", h.OverFrom ?? (h.Centre + f * 20f + side * 8f + Vector3.up * (12f + hgt * 0.5f)), h.Centre, 55f);
                if (h.SpotX >= 0)
                {
                    var eye = new Vector3(h.SpotX + 0.5f, s.Floor[h.SpotX + h.SpotY * s.W] + Views.EyeCells, h.SpotY + 0.5f);
                    Line(sb, $"hot{h.Id}_eye", eye, h.LookAt, 80f);
                }
                var tall = h.WallFrom == null ? h.Walls.OrderByDescending(w => w.Span).FirstOrDefault() : null;
                if (h.WallFrom != null) Line(sb, $"hot{h.Id}_wall", h.WallFrom.Value, h.WallAt, 60f);
                else if (tall != null)
                {
                    var n = tall.Front ? new Vector3(0, 0, -1) : new Vector3(-1, 0, 0);
                    float lo = Math.Min(Math.Min(tall.A0, tall.A1), Math.Min(tall.B0, tall.B1)), hi = Math.Max(Math.Max(tall.A0, tall.A1), Math.Max(tall.B0, tall.B1));
                    var mid = new Vector3(tall.Cx, (lo + hi) * 0.5f, tall.Cy);
                    Line(sb, $"hot{h.Id}_wall", mid + n * 8f + new Vector3(n.z * 2.5f, hi - mid.y + 3f, -n.x * 2.5f), mid, 60f);
                }
            }
            // the map from outside its walkable area's edge, four sides, and from below
            int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue; var ys = new List<float>();
            for (int y = 0; y < s.H; y += 2) for (int x = 0; x < s.W; x += 2) if (s.Walkable(x, y)) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); z0 = Math.Min(z0, y); z1 = Math.Max(z1, y); ys.Add(s.Floor[x + y * s.W]); }
            if (ys.Count == 0) return sb.ToString();
            ys.Sort(); float fy = ys[ys.Count / 2], ytop = ys[ys.Count * 9 / 10];
            var c = new Vector3((x0 + x1) * 0.5f, fy, (z0 + z1) * 0.5f); float ext = Math.Max(x1 - x0, z1 - z0), r = ext * 0.75f + 25f;
            Line(sb, "outside_south", new Vector3(c.x, ytop + r * 0.45f, z0 - r), c, 50f);
            Line(sb, "outside_north", new Vector3(c.x, ytop + r * 0.45f, z1 + r), c, 50f);
            Line(sb, "outside_east", new Vector3(x1 + r, ytop + r * 0.45f, c.z), c, 50f);
            Line(sb, "outside_west", new Vector3(x0 - r, ytop + r * 0.45f, c.z), c, 50f);
            Line(sb, "below", new Vector3(c.x, ys[0] - r * 0.55f, z0 - r * 0.35f), c, 60f);
            if (hots.Count > 0) Line(sb, "below_hot1", hots[0].Centre + new Vector3(-6f, -24f, -10f), hots[0].Centre, 60f);
            return sb.ToString();
        }

        private static void Line(StringBuilder sb, string name, Vector3 from, Vector3 at, float fov)
        {
            var inv = CultureInfo.InvariantCulture;
            sb.Append(name).Append('\t').Append(string.Join(" ", new[] { from.x, from.y, from.z }.Select(v => v.ToString("0.##", inv)))).Append('\t')
              .Append(string.Join(" ", new[] { at.x, at.y, at.z }.Select(v => v.ToString("0.##", inv)))).Append('\t').Append(fov.ToString("0", inv)).Append('\n');
        }
    }
}
