using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Loki.Formats;
using Loki.World;
using UnityEngine;

namespace Loki.VrMaps
{
    /// <summary>
    /// Wave 27 (wmodel): `architect models`: the fix kind M on named maps. Per map: the map as the game draws it with its shipped fix
    /// file (the base: client/Assets/Loki/Resources/MapFixes/&lt;map&gt;.txt, its ground lines kept byte for byte), the model faces to draw
    /// from both sides (ModelFixer.Generate), the merged fix file (the base's lines + R + M lines; read back by the client's own reader),
    /// the map analyzed again with it (the client's builder), the check (ModelCheck), and the showcase's spots and cameras for the most
    /// changed places. Writes &lt;map&gt;.txt (only when the map has a base file or model faces to add), &lt;map&gt;.models.md,
    /// &lt;map&gt;.mcheck.md, &lt;map&gt;.mspots.tsv, &lt;map&gt;.mshowcase.tsv into --out; never into the install or the repo.
    /// </summary>
    public static class ModelRun
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static int Run(IGameFiles files, string map, string outDir, string userData, string baseDir)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string name = map.ToLowerInvariant();
            var rswB = files.Read("data\\" + map + ".rsw") ?? throw new FileNotFoundException("Not in client data: data\\" + map + ".rsw");
            var rsw = RswFile.Parse(rswB);
            var gndB = files.Read("data\\" + rsw.GndFile) ?? throw new FileNotFoundException("Not in client data: data\\" + rsw.GndFile);
            var gatB = files.Read("data\\" + rsw.GatFile) ?? throw new FileNotFoundException("Not in client data: data\\" + rsw.GatFile);
            var gnd = GndFile.Parse(gndB);
            string bp = baseDir == null ? null : Path.Combine(baseDir, name + ".txt");
            string baseText = bp != null && File.Exists(bp) ? File.ReadAllText(bp) : null;
            MapFix baseFix = null;
            if (baseText != null)
            {
                baseFix = MapFix.Parse(baseText, gnd.Width);
                if (!baseFix.Matches(gndB, gatB)) throw new InvalidOperationException($"{map}: the base fix file {bp} was made for other files");
                baseFix.Models.Clear(); baseFix.RswBytes = 0; baseFix.RswHash = ""; // a re-run: the M part is made again
            }
            // the map as the game draws it (its shipped ground fix), and the survey's report (nothing written)
            var before = Program.AnalyzeOne(files, map, null, userData, true, baseFix, "", out var s0, out var a0);
            var work = baseFix != null ? MapFix.Parse(baseText, gnd.Width) : new MapFix { Map = s0.Map, Width = gnd.Width, GndBytes = gndB.LongLength, GatBytes = gatB.LongLength, GndHash = MapFix.Hash(gndB), GatHash = MapFix.Hash(gatB) };
            work.Models.Clear(); work.RswBytes = 0; work.RswHash = "";
            var st = new ModelFixer.Stats();
            var patches = ModelFixer.Generate(files, map, userData, work, s0, st);
            // ---- the file: the base's comments and lines kept, the M part added
            var comments = new List<string>();
            if (baseText != null) foreach (var l in baseText.Split('\n')) { var x = l.TrimEnd('\r'); if (x.StartsWith("# ", StringComparison.Ordinal)) comments.Add(x.Substring(2)); }
            else comments.Add($"Architect's fix file for {s0.Map} (client/tools/vrmaps 'models', {DateTime.Now:yyyy-MM-dd}): read by World/MapFix.cs, applied by World/MapLoader.cs when it builds {s0.Map}");
            comments.RemoveAll(c => c.StartsWith("M (wave 27", StringComparison.Ordinal) || c.StartsWith("M: ", StringComparison.Ordinal));
            comments.Add("M (wave 27, wmodel; next-list item 16): single-sided model faces drawn from their other side too, where a player sees them from behind or below (first person, Follow); never toward the void (the design's outer-wall rule), never in a walkable passage, never on another surface; R: the RSW the M lines were made for (tools/vrmaps/Models.cs)");
            comments.Add($"M: {work.ModelFaceCount} model faces on {work.Models.Count} models");
            // a map with a shipped file and no model face to add keeps its file byte for byte
            string text = work.Models.Count == 0 && baseText != null ? baseText : work.ToText(comments);
            var back = MapFix.Parse(text, gnd.Width);
            if (text != baseText && back.ToText(comments) != text) throw new InvalidOperationException($"{map}: the fix file does not read back as written");
            if (baseText != null)
            {
                // the ground's lines exactly as shipped (the design approved them: valkyrie's rule-only file, warch2's all-maps files)
                static IEnumerable<string> Ground(string t) => t.Split('\n').Select(x => x.TrimEnd('\r')).Where(x => x.Length > 0 && x[0] != '#' && !x.StartsWith("M\t", StringComparison.Ordinal) && !x.StartsWith("R\t", StringComparison.Ordinal));
                if (!Ground(baseText).SequenceEqual(Ground(text))) throw new InvalidOperationException($"{map}: the merged file's ground lines differ from the base file's");
            }
            bool write = baseText != null || work.Models.Count > 0;
            if (write) File.WriteAllText(Path.Combine(outDir, s0.Map + ".txt"), text);
            // ---- the map with the file, built by the client's builder; the check
            // ARCHITECT_MODEL_REPORTS=1: the full reports of the map with the M lines (<map>.mafter.vrmap.*) for hand checks
            var after = Program.AnalyzeOne(files, map, Environment.GetEnvironmentVariable("ARCHITECT_MODEL_REPORTS") == "1" ? outDir : null, userData, true, back, ".mafter", out var s1, out var a1);
            var check = ModelCheck.Run(s0, s1, back, patches, st, before, after);
            File.WriteAllText(Path.Combine(outDir, s0.Map + ".mcheck.md"), ModelCheck.Markdown(check));
            // ---- the showcase: the most changed places, a spot for the game's views, cameras
            var hots = Hotspots(s0, patches.Where(p => p.Added.Count > 0).ToList());
            Spots(s1, s0, hots);
            File.WriteAllText(Path.Combine(outDir, s0.Map + ".mspots.tsv"), SpotsTsv(s0, hots));
            File.WriteAllText(Path.Combine(outDir, s0.Map + ".mshowcase.tsv"), Cameras(s1, hots));
            File.WriteAllText(Path.Combine(outDir, s0.Map + ".models.md"), Markdown(s0, st, patches, work, check, hots, write) );
            Console.WriteLine($"{s0.Map}: {(write ? "fix file " + Path.Combine(outDir, s0.Map + ".txt") : "no fix file")}: M {work.ModelFaceCount} model faces on {work.Models.Count} models ({st.AddedPatches} patches; witnessed {st.Witnessed}, pruned {st.Pruned}; left: void {st.ExVoid}, in the way {st.ExInTheWay}, z-fight {st.ExCoplanar}); " +
                $"single-sided faces {st.SingleTris} of {st.ModelTris} model triangles ({st.SkippedMoving} on moving models); {sw.ElapsedMilliseconds / 1000f:0.0} s");
            Console.WriteLine(check.Line());
            return check.Fail ? 3 : 0;
        }

        // ------------------------------------------------------------------ the showcase
        public sealed class Hot
        {
            public int Id; public float Score; public Vector3 Centre, Back; public readonly List<ModelFixer.Patch> Patches = new List<ModelFixer.Patch>();
            public int SpotX = -1, SpotY = -1, SpotSeen; public Vector3 Look = new Vector3(0, 0, 1), LookAt; public float Pitch;
            public Vector3? OverFrom; public int OverSeen; public string Kind = "";
        }

        /// <summary>16-cell windows ranked by the surface the M lines add (cells²), apart from each other: the most changed places.</summary>
        public static List<Hot> Hotspots(Scene s, List<ModelFixer.Patch> added, int n = 4, int win = 16)
        {
            var hots = new List<Hot>(); if (added.Count == 0) return hots;
            int step = win / 2, nx = s.W / step + 2, nz = s.H / step + 2;
            var score = new float[nx * nz];
            float AreaOf(ModelFixer.Patch p) => p.Added.Sum(t => ModelFixer.Area(s, t));
            foreach (var p in added)
            {
                float a = AreaOf(p);
                for (int bz = Math.Max(0, (int)(p.C.z / step) - 1); bz <= Math.Min(nz - 1, Math.Max(0, (int)(p.C.z / step))); bz++)
                    for (int bx = Math.Max(0, (int)(p.C.x / step) - 1); bx <= Math.Min(nx - 1, Math.Max(0, (int)(p.C.x / step))); bx++)
                        score[bx + bz * nx] += a;
            }
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
                if (best < 0 || (bs < 2f && hots.Count > 0)) break;
                float x0 = (best % nx) * step, z0 = (best / nx) * step;
                used.Add((x0 + win * 0.5f, z0 + win * 0.5f));
                var h = new Hot { Id = k + 1, Score = bs }; float wsum = 0f; var c = Vector3.zero;
                foreach (var p in added)
                {
                    if (p.C.x < x0 || p.C.x >= x0 + win || p.C.z < z0 || p.C.z >= z0 + win) continue;
                    float a = AreaOf(p); c += p.C * a; wsum += a; h.Back += -p.N * a; h.Patches.Add(p);
                }
                if (wsum <= 0f) break;
                h.Centre = c / wsum; h.Kind = h.Back.y < -0.3f * h.Back.magnitude ? "under" : "behind";
                hots.Add(h);
            }
            return hots;
        }

        /// <summary>Per hotspot: the walkable cell (within 18 cells, every second cell) whose first-person eye sees the backs of the most of
        /// its added patches on the fixed map, the way to look and how far up; then an overview camera chosen by rays.</summary>
        public static void Spots(Scene s1, Scene s0, List<Hot> hots)
        {
            var g = new Grid(s1); var map1 = ModelFixer.Map(s0, s1);
            foreach (var h in hots)
            {
                var pats = h.Patches.OrderByDescending(p => p.Added.Count).Take(200).Select(p => ModelFixer.Translate(s0, p, map1)).ToList();
                var cand = new List<(int x, int y)>();
                int cx = Mathf.FloorToInt(h.Centre.x), cz = Mathf.FloorToInt(h.Centre.z);
                for (int y = cz - 18; y <= cz + 18; y += 2) for (int x = cx - 18; x <= cx + 18; x += 2) if (s1.Walkable(x, y)) cand.Add((x, y));
                cand = cand.OrderBy(p => (p.x - cx) * (p.x - cx) + (p.y - cz) * (p.y - cz)).Take(140).ToList();
                var seen = new int[cand.Count]; var look = new Vector3[cand.Count];
                Parallel.For(0, cand.Count, () => new Grid.RayState(s1.Count), (i, ls, st) =>
                {
                    var (x, y) = cand[i]; var eye = new Vector3(x + 0.5f, s1.Floor[x + y * s1.W] + Views.EyeCells, y + 0.5f);
                    int room = FixCheck.RoomOf(s1, x, y); var sum = Vector3.zero; long r = 0;
                    foreach (var p in pats)
                        foreach (var t in p.Added.Take(3))
                        {
                            var pt = ModelFixer.Centre(s1, t) - s1.Normal(t) * ModelFixer.BackOffset;
                            if (ModelFixer.SeesBack(s1, g, st, -s1.Normal(t), eye, room, pt, ref r)) { seen[i]++; var d = pt - eye; if (d.sqrMagnitude > 1e-4f) sum += d.normalized; break; }
                        }
                    look[i] = sum;
                    return st;
                }, _ => { });
                int best = -1; for (int i = 0; i < cand.Count; i++) if (seen[i] > 0 && (best < 0 || seen[i] > seen[best])) best = i;
                if (best < 0)
                {
                    // no cell near sees them: the patches' own witness cells
                    var w = h.Patches.FirstOrDefault(p => p.EyeX >= 0);
                    if (w == null) continue;
                    h.SpotX = w.EyeX; h.SpotY = w.EyeY; h.SpotSeen = 0;
                    var dv = w.C - new Vector3(w.EyeX + 0.5f, s1.Floor[w.EyeX + w.EyeY * s1.W] + Views.EyeCells, w.EyeY + 0.5f);
                    SetLook(h, dv, s1);
                }
                else { h.SpotX = cand[best].x; h.SpotY = cand[best].y; h.SpotSeen = seen[best]; SetLook(h, look[best], s1); }
            }
            // the overview: among cameras round the hotspot on its back side (18 / 28 cells out, 6 headings, 3 heights incl. one below),
            // the one that sees the backs of the most added patches
            foreach (var h in hots)
            {
                var pats = h.Patches.OrderByDescending(p => p.Added.Count).Take(60).Select(p => ModelFixer.Translate(s0, p, map1)).ToList(); if (pats.Count == 0) continue;
                var f = h.Back; var fh = new Vector3(f.x, 0, f.z); if (fh.sqrMagnitude < 1e-4f) fh = new Vector3(0, 0, -1); fh.Normalize();
                int room = FixCheck.RoomOf(s1, Mathf.FloorToInt(h.Centre.x), Mathf.FloorToInt(h.Centre.z));
                var cands = new List<Vector3>();
                foreach (var d in new[] { 12f, 20f })
                    foreach (var el in new[] { -15f, 15f, 40f })
                        foreach (var az in new[] { 0f, 35f, -35f, 70f, -70f, 180f })
                        {
                            float a = az * Mathf.Deg2Rad, e = el * Mathf.Deg2Rad;
                            var hd = new Vector3(fh.x * (float)Math.Cos(a) - fh.z * (float)Math.Sin(a), 0, fh.x * (float)Math.Sin(a) + fh.z * (float)Math.Cos(a));
                            cands.Add(h.Centre + hd * (d * (float)Math.Cos(e)) + Vector3.up * (d * (float)Math.Sin(e)));
                        }
                // run 1 (prontera hot1, payon hot3): the best-scoring camera stood INSIDE a building (it sees every inner face's back copy):
                // a camera inside a model's placed box is not an overview
                var boxes = s1.Instances.Where(x => !x.Missing && x.Tris > 0).Select(x => (x.Min, x.Max)).ToList();
                bool Inside(Vector3 c) => boxes.Any(b => c.x > b.Min.x - 0.2f && c.x < b.Max.x + 0.2f && c.y > b.Min.y - 0.2f && c.y < b.Max.y + 0.2f && c.z > b.Min.z - 0.2f && c.z < b.Max.z + 0.2f);
                var score = new int[cands.Count];
                Parallel.For(0, cands.Count, () => new Grid.RayState(s1.Count), (i, ls, st) =>
                {
                    int n = 0; long r = 0;
                    if (Inside(cands[i])) { score[i] = -1; return st; }
                    foreach (var p in pats)
                        foreach (var t in p.Added.Take(2))
                        {
                            var pt = ModelFixer.Centre(s1, t) - s1.Normal(t) * ModelFixer.BackOffset;
                            if (ModelFixer.SeesBack(s1, g, st, -s1.Normal(t), cands[i], room, pt, ref r)) { n++; break; }
                        }
                    score[i] = n; return st;
                }, _ => { });
                int bi = 0; for (int i = 1; i < cands.Count; i++) if (score[i] > score[bi]) bi = i;
                if (score[bi] > 0) { h.OverFrom = cands[bi]; h.OverSeen = score[bi]; }
            }
        }

        private static void SetLook(Hot h, Vector3 dir, Scene s1)
        {
            var flat = new Vector3(dir.x, 0, dir.z);
            if (flat.sqrMagnitude < 1e-4f) { flat = h.Centre - new Vector3(h.SpotX + 0.5f, 0, h.SpotY + 0.5f); flat.y = 0; }
            if (flat.sqrMagnitude < 1e-4f) flat = new Vector3(0, 0, 1);
            h.Look = flat.normalized;
            float up = dir.sqrMagnitude > 1e-6f ? (float)(Math.Atan2(dir.y, Math.Sqrt(dir.x * dir.x + dir.z * dir.z)) * 180.0 / Math.PI) : 0f;
            h.Pitch = Mathf.Clamp(up, -60f, 70f);
            float ey = s1.Floor[h.SpotX + h.SpotY * s1.W] + Views.EyeCells;
            var eye = new Vector3(h.SpotX + 0.5f, ey, h.SpotY + 0.5f);
            h.LookAt = eye + h.Look * 10f * (float)Math.Cos(h.Pitch * Math.PI / 180.0) + Vector3.up * 10f * (float)Math.Sin(h.Pitch * Math.PI / 180.0);
        }

        /// <summary>name, cell x, cell y, look x z, added patches the eye sees, pitch (degrees up: eaves are seen looking up), kind.</summary>
        public static string SpotsTsv(Scene s, List<Hot> hots)
        {
            var sb = new StringBuilder();
            sb.Append("# Architect model-face showcase spots (wave 27, wmodel) for ").Append(s.Map).Append(": name, cell x, cell y, look x z, added patches seen, pitch (degrees up), kind; the most changed first\n");
            foreach (var h in hots.Where(x => x.SpotX >= 0))
                sb.Append($"spot{h.Id}\t{h.SpotX}\t{h.SpotY}\t{h.Look.x.ToString("0.###", Inv)} {h.Look.z.ToString("0.###", Inv)}\t{h.SpotSeen}\t{h.Pitch.ToString("0.#", Inv)}\t{h.Kind}\n");
            return sb.ToString();
        }

        public static string Cameras(Scene s, List<Hot> hots)
        {
            var sb = new StringBuilder();
            sb.Append("# Architect model-face showcase cameras (wave 27, wmodel) for ").Append(s.Map).Append(": name, from x y z, at x y z (map cells, y up), fov; the most changed first\n");
            foreach (var h in hots)
            {
                var f = new Vector3(h.Back.x, 0, h.Back.z); if (f.sqrMagnitude < 1e-4f) f = new Vector3(0, 0, -1); f.Normalize();
                Line(sb, $"hot{h.Id}", h.OverFrom ?? (h.Centre + f * 14f + Vector3.up * (h.Kind == "under" ? -2f : 6f)), h.Centre, 55f);
                if (h.SpotX >= 0) Line(sb, $"hot{h.Id}_eye", new Vector3(h.SpotX + 0.5f, s.Floor[h.SpotX + h.SpotY * s.W] + Views.EyeCells, h.SpotY + 0.5f), h.LookAt, 80f);
            }
            return sb.ToString();
        }

        private static void Line(StringBuilder sb, string name, Vector3 from, Vector3 at, float fov)
        {
            sb.Append(name).Append('\t').Append(string.Join(" ", new[] { from.x, from.y, from.z }.Select(v => v.ToString("0.##", Inv)))).Append('\t')
              .Append(string.Join(" ", new[] { at.x, at.y, at.z }.Select(v => v.ToString("0.##", Inv)))).Append('\t').Append(fov.ToString("0", Inv)).Append('\n');
        }

        // ------------------------------------------------------------------ the report
        public static string Markdown(Scene s, ModelFixer.Stats st, List<ModelFixer.Patch> patches, MapFix fix, ModelCheck.Result check, List<Hot> hots, bool written)
        {
            var sb = new StringBuilder();
            sb.Append($"# {s.Map}: Architect's model faces (kind M, wave 27)\n\n");
            sb.Append(written ? $"The fix file `{s.Map}.txt` (the shipped ground lines kept; + R + M lines): {fix.ModelFaceCount} model faces drawn from both sides on {fix.Models.Count} models.\n\n" : "No fix file: no model face to draw from both sides, and no ground fix.\n\n");
            sb.Append("## The map's model faces\n\n");
            sb.Append($"- Drawn model triangles {st.ModelTris}: single-sided {st.SingleTris} (of them on models that move or whose texture moves, left as they are: {st.SkippedMoving}); copies of faces the file marks two-sided {st.TwoSidedTris}.\n");
            sb.Append($"- Candidates {st.Candidates} single-sided faces in {st.Patches} patches; a player sees the back of {st.Witnessed} patches (first person {st.WitnessedFp}, Follow {st.WitnessedFollow}).\n");
            sb.Append($"- Drawn from both sides: {st.AddedTris} faces in {st.AddedPatches} patches ({patches.Count(p => p.Added.Count > 0 && p.Kind == "under")} seen from below, {patches.Count(p => p.Added.Count > 0 && p.Kind == "behind")} from behind). Pruned (hidden by other added faces): {st.PrunedTris} faces in {st.Pruned} patches.\n");
            sb.Append($"- Left as the game draws them although a player sees their back: the void behind them (the outer-wall rule) {st.ExVoid}; in a walkable passage at body height {st.ExInTheWay}; on another surface (z-fighting) {st.ExCoplanar}. Eyes inside a solid of the same model (not counted as a player view): {st.InsideSolidEyes}.\n");
            sb.Append($"- Cost: {st.AddedTris} back copies = {(st.ModelTris > 0 ? st.AddedTris * 100.0 / st.ModelTris : 0):0.0} % of the map's drawn model triangles; {st.Rays} rays, {st.Seconds:0.0} s.\n\n");
            sb.Append($"## The check: {check.Verdict}\n\n{check.Line()} (`{s.Map}.mcheck.md`)\n\n");
            var byFile = patches.Where(p => p.Added.Count > 0).GroupBy(p => s.Instances[p.Inst].File).Select(g => (file: g.Key, faces: g.Sum(p => p.Added.Count), inst: g.Select(p => p.Inst).Distinct().Count(), under: g.Count(p => p.Kind == "under"))).OrderByDescending(x => x.faces).Take(12).ToList();
            if (byFile.Count > 0)
            {
                sb.Append("## The models changed most\n\n| model file | faces | instances | patches seen from below |\n|---|---|---|---|\n");
                foreach (var x in byFile) sb.Append($"| {Names.Display(x.file)} | {x.faces} | {x.inst} | {x.under} |\n");
            }
            if (hots.Count > 0)
            {
                sb.Append("\n## The most changed places (the showcase)\n\n| # | centre (cells) | kind | patches | spot | look up |\n|---|---|---|---|---|---|\n");
                foreach (var h in hots) sb.Append($"| {h.Id} | {h.Centre.x:0},{h.Centre.z:0} ({h.Centre.y:0.#} up) | {h.Kind} | {h.Patches.Count} | {(h.SpotX >= 0 ? h.SpotX + "," + h.SpotY : "-")} | {h.Pitch:0} deg |\n");
            }
            return sb.ToString();
        }
    }
}
