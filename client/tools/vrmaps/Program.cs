using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Loki.VrMaps
{
    // Architect (the design's name for it, 2026-10-14; built as "vrmaps"): the offline VR map analyzer and its fix mode (design notes,
    // design notes). Read-only over the RO install.
    //   architect analyze --root <RO install> --out <folder> [--grf data.grf] [--userdata <folder>|--no-userdata] [--no-survey] <map> [<map> ...]
    //       the named maps only (there is deliberately no "every map" switch: design note, 2026-10-14, "Don't use it"); writes
    //       <map>.vrmap.json / .md / .png into --out (never into the RO install)
    //   architect fix --root <RO install> --out <folder> [--userdata <folder>] [--ledge low|keep|rule] <map> [<map> ...]
    //       Architect's fix mode (Architect.cs): <map>.txt (the fix file the client applies: World/MapFix.cs), <map>.fix.md (what it
    //       changes and why, before / after, the most changed sections), <map>.showcase.tsv, the before / after reports
    //   architect list --root <RO install> [--grf ...]        the maps the install holds (data\*.rsw); nothing is analyzed
    //   architect selftest                                     synthetic maps, one per detector
    //   architect constants --client <repo>\client             the restated client numbers against the client's source
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0) return Usage();
                var opts = Parse(args.Skip(1).ToArray(), out var rest);
                switch (args[0])
                {
                    case "analyze": return Analyze(opts, rest);
                    case "list": return List(opts);
                    case "aliases": return Aliases(opts);
                    case "read": return ReadFiles(opts, rest);
                    case "selftest": return SelfTest.Run();
                    case "constants": return Constants(opts);
                    case "probe": return Probe(opts, rest);
                    case "grid": return Grid0(opts, rest);
                    case "walls": return Walls0(opts, rest);
                    case "fix": return Fix(opts, rest);
                    case "models-measure": return ModelsMeasure(opts, rest);
                    case "models": return Models(opts, rest);
                    default: return Usage();
                }
            }
            catch (Exception e) { Console.Error.WriteLine("architect: " + e); return 1; }
        }

        private static int Usage()
        {
            Console.Error.WriteLine("usage: architect analyze --root <RO install> --out <folder> [--grf data.grf] [--userdata <folder> | --no-userdata] [--no-survey] <map> [<map> ...]");
            Console.Error.WriteLine("       architect fix --root <RO install> --out <folder> [--userdata <folder>] [--ledge low|keep|rule] [--fill day|all] [--rule-only] <map> [<map> ...]   (the fix mode: per-map fix files; --rule-only: without the ray refinement and the inner walls)");
            Console.Error.WriteLine("       architect list --root <RO install> [--grf data.grf]");
            Console.Error.WriteLine("       architect read --root <RO install> --out <folder> <RO path> [<RO path> ...]   (copies game files out, read as the client reads them: the name table included)");
            Console.Error.WriteLine("       architect aliases --root <RO install> [--grf data.grf]   (the maps of the game's name table, data\\resnametable.txt, as the client resolves them)");
            Console.Error.WriteLine("       architect selftest");
            Console.Error.WriteLine("       architect constants --client <repo>\\client");
            Console.Error.WriteLine("       architect probe --root <RO install> <map> <x> <y> [<x> <y> ...]   (what the tool sees at a cell, for hand checks)");
            Console.Error.WriteLine("       architect grid --root <RO install> <map> [x0 y0 x1 y1]   (the map's cells, heights and walls as text)");
            Console.Error.WriteLine("       architect walls --root <RO install> <map> [list]   (every ground wall by the design's outer-wall rule)");
            Console.Error.WriteLine("       architect models --root <RO install> --out <folder> --base <the shipped MapFixes folder> [--maps-file <file>] [<map> ...]   (wave 27: single-sided model faces drawn from both sides where players see their back; the base files' ground lines kept)");
            return 2;
        }

        private static Dictionary<string, string> Parse(string[] a, out List<string> rest)
        {
            var o = new Dictionary<string, string>(StringComparer.Ordinal); rest = new List<string>();
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].StartsWith("--"))
                {
                    string k = a[i].Substring(2);
                    if (k == "no-userdata" || k == "no-survey" || k == "rule-only") o[k] = "1";
                    else if (i + 1 < a.Length) o[k] = a[++i];
                    else throw new ArgumentException("--" + k + " needs a value");
                }
                else rest.Add(a[i]);
            }
            return o;
        }

        private static string[] Grfs(Dictionary<string, string> o) => o.TryGetValue("grf", out var g) ? g.Split(',') : new[] { "data.grf" };

        /// <summary>The player's data folder (Data/ClientData.cs:11: %USERPROFILE%\AppData\LocalLow\Project Loki\Loki\data), read only.</summary>
        private static string UserData(Dictionary<string, string> o)
        {
            if (o.ContainsKey("no-userdata")) return null;
            if (o.TryGetValue("userdata", out var u)) return u;
            string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Project Loki", "Loki", "data");
            return Directory.Exists(d) ? d : null;
        }

        private static int Analyze(Dictionary<string, string> o, List<string> maps)
        {
            if (!o.TryGetValue("root", out var root) || !o.TryGetValue("out", out var outDir) || maps.Count == 0) return Usage();
            string fullOut = Path.GetFullPath(outDir), fullRoot = Path.GetFullPath(root);
            if (fullOut.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) { Console.Error.WriteLine("architect: --out must not be inside the RO install (it is read-only)"); return 2; }
            Directory.CreateDirectory(fullOut);
            string user = UserData(o);
            using (var files = new InstallFiles(root, Grfs(o), s => Console.WriteLine("[install] " + s)))
            {
                int bad = 0;
                foreach (var map in maps)
                {
                    try { AnalyzeOne(files, map, fullOut, user, !o.ContainsKey("no-survey")); }
                    catch (Exception e) when (e is FileNotFoundException || e is Formats.RoFormatException) { Console.Error.WriteLine($"{map}: {e.Message}"); bad++; }
                }
                foreach (var d in files.Damaged) Console.Error.WriteLine("damaged: " + d);
                return bad == 0 ? 0 : 1;
            }
        }

        public static MapReport AnalyzeOne(IGameFiles files, string map, string outDir, string userData, bool survey) => AnalyzeOne(files, map, outDir, userData, survey, null, "", out _, out _);

        /// <param name="fix">Architect's fix to build the ground with (as the client applies it), or null.</param>
        /// <param name="suffix">Inserted before ".vrmap" in the written names (".after": the map with its fix).</param>
        public static MapReport AnalyzeOne(IGameFiles files, string map, string outDir, string userData, bool survey, Loki.World.MapFix fix, string suffix, out Scene scene, out Analysis analysis)
        {
            var sw = Stopwatch.StartNew();
            scene = Scene.Build(files, map, new Scene.Options { UserData = userData, Fix = fix });
            long tScene = sw.ElapsedMilliseconds;
            var grid = new Grid(scene);
            long tGrid = sw.ElapsedMilliseconds;
            var r = new MapReport { Map = scene.Map };
            r.Source["install"] = files.Describe;
            foreach (var (k, p) in new[] { ("rsw", "data\\" + map + ".rsw"), ("gnd", "data\\" + scene.Rsw.GndFile), ("gat", "data\\" + scene.Rsw.GatFile) })
            {
                var b = files.Read(p);
                r.Source[k] = new Dictionary<string, object> { ["path"] = p, ["bytes"] = b?.Length ?? 0, ["sha256"] = b == null ? "" : Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant() };
            }
            if (userData != null) r.Source["userData"] = userData;
            Views.FollowEye(out float fb, out float fu);
            r.Views["firstPerson"] = new Dictionary<string, object> { ["metresPerCell"] = Views.FirstPersonMetresPerCell, ["eyeCells"] = Views.EyeCells, ["eyeMetres"] = Views.EyeHeightMetres };
            r.Views["follow"] = new Dictionary<string, object> { ["metresPerCell"] = Views.FollowMetresPerCell, ["eyeBackCells"] = fb, ["eyeUpCells"] = fu };
            r.Views["table"] = new Dictionary<string, object> { ["metresPerCellAtZoom1"] = Views.TableMetresPerCell, ["zoom"] = new[] { Views.TableMinScale, Views.TableMaxScale }, ["tableHeight"] = Views.TableHeight };
            var a = new Analysis(scene, grid, r); analysis = a;
            if (fix != null) r.MapFacts["Architect's fix"] = $"applied ({fix.Summary()})";
            a.RunStep1();
            long tStep1 = sw.ElapsedMilliseconds;
            if (survey) a.RunSurvey();
            a.RunStep3();
            long tAll = sw.ElapsedMilliseconds;
            r.MapFacts["seconds"] = $"{tAll / 1000f:0.0} (scene {tScene / 1000f:0.0}, grid {(tGrid - tScene) / 1000f:0.0}, column / distance measures {(tStep1 - tGrid) / 1000f:0.0}, eye survey {(tAll - tStep1) / 1000f:0.0})";
            r.Findings = r.Findings.OrderBy(f => MapReport.Rank(f.Severity)).ThenBy(f => f.Kind, StringComparer.Ordinal).ThenByDescending(f => f.Count).ToList();
            if (outDir != null)
            {
                string stem = Path.Combine(outDir, scene.Map + suffix);
                File.WriteAllText(stem + ".vrmap.json", r.Json());
                File.WriteAllText(stem + ".vrmap.md", r.Markdown());
                Png.Write(stem + ".vrmap.png", scene, r);
                Console.WriteLine($"{scene.Map}{suffix}: {r.Findings.Count} findings ({string.Join(", ", MapReport.SeverityOrder.Select(s => s + " " + r.Findings.Count(f => f.Severity == s)))}) in {tAll / 1000f:0.0} s -> {stem}.vrmap.json / .md / .png");
            }
            return r;
        }

        private static int List(Dictionary<string, string> o)
        {
            if (!o.TryGetValue("root", out var root)) return Usage();
            using (var files = new InstallFiles(root, Grfs(o)))
            {
                var maps = files.Names.Where(n => n.StartsWith("data\\", StringComparison.Ordinal) && n.EndsWith(".rsw", StringComparison.Ordinal) && n.IndexOf('\\', 5) < 0)
                    .Select(n => n.Substring(5, n.Length - 9)).OrderBy(n => n, StringComparer.Ordinal).ToList();
                foreach (var m in maps) Console.WriteLine(m);
                Console.Error.WriteLine($"{maps.Count} maps (data\\*.rsw) in {files.Describe}");
            }
            return 0;
        }

        /// <summary>warch2: every map name the game's name table gives files of another map (IO/RoLookup.cs, as the client resolves
        /// them), one tab-separated line each: "alias", the name, the real rsw / gnd / gat map names (from the table's rows; "-" when the
        /// table has no row for that file and the exact file stands), whether the real map's rsw is in the game data; then "self" lines for
        /// rsw rows naming themselves (they change nothing).</summary>
        private static int Aliases(Dictionary<string, string> o)
        {
            if (!o.TryGetValue("root", out var root)) return Usage();
            using (var files = new InstallFiles(root, Grfs(o)))
            {
                var lk = files.Opened.Lookup; int n = 0, present = 0;
                string Base(string v, string ext) => v != null && v.EndsWith(ext, StringComparison.Ordinal) ? v.Substring(0, v.Length - ext.Length) : v ?? "-";
                foreach (var kv in lk.Aliases.Where(kv => kv.Key.EndsWith(".rsw", StringComparison.Ordinal) && kv.Key.IndexOf('\\') < 0).OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    string name = Base(kv.Key, ".rsw");
                    lk.Aliases.TryGetValue(name + ".gnd", out var gnd); lk.Aliases.TryGetValue(name + ".gat", out var gat);
                    string real = "data\\" + kv.Value; bool has = lk.RealPath(real) == real && lk.Exists(real);
                    Console.WriteLine($"alias\t{name}\t{Base(kv.Value, ".rsw")}\t{Base(gnd, ".gnd")}\t{Base(gat, ".gat")}\t{(has ? "present" : "absent")}");
                    n++; if (has) present++;
                }
                var raw = lk.Read(Loki.IO.RoLookup.NameTable, out _, _ => { });
                var self = new SortedSet<string>(StringComparer.Ordinal);
                if (raw != null)
                    foreach (var line in Loki.IO.RoReader.Latin1.GetString(raw).Split('\n'))
                    {
                        var c = line.Trim().Split('#'); if (c.Length < 2 || line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                        string a = Loki.IO.RoPath.Normalize(c[0].Trim()), r = Loki.IO.RoPath.Normalize(c[1].Trim());
                        if (a == r && a.EndsWith(".rsw", StringComparison.Ordinal) && a.IndexOf('\\') < 0) self.Add(a.Substring(0, a.Length - 4));
                    }
                foreach (var m in self) Console.WriteLine($"self\t{m}\t{m}\t-\t-\t{(lk.Exists("data\\" + m + ".rsw") ? "present" : "absent")}");
                Console.Error.WriteLine($"{n} map aliases ({present} with the real map's rsw in the game data), {self.Count} rsw rows naming themselves, in {files.Describe}");
            }
            return 0;
        }

        /// <summary>warch2: copies game files out (facts by hand), each read as the client reads it (IO/RoLookup.cs).</summary>
        private static int ReadFiles(Dictionary<string, string> o, List<string> rest)
        {
            if (!o.TryGetValue("root", out var root) || !o.TryGetValue("out", out var dir) || rest.Count == 0) return Usage();
            System.IO.Directory.CreateDirectory(dir);
            using (var files = new InstallFiles(root, Grfs(o)))
                foreach (var p in rest)
                {
                    var n = Loki.IO.RoPath.Normalize(p); var b = files.Opened.Lookup.Read(n, out var src, _ => { });
                    if (b == null) { Console.WriteLine($"{p}: not in the game data"); continue; }
                    string real = files.Opened.Lookup.RealPath(n), name = n.Substring(n.LastIndexOf('\\') + 1);
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name), b);
                    Console.WriteLine($"{p}: {b.Length} bytes from {src}{(real != n ? " (the name table: " + real + ")" : "")}");
                }
            return 0;
        }

        /// <summary>Everything the tool sees at a cell, for checking a finding by hand against the raw data.</summary>
        private static int Probe(Dictionary<string, string> o, List<string> rest)
        {
            if (!o.TryGetValue("root", out var root) || rest.Count < 3 || (rest.Count - 1) % 2 != 0) return Usage();
            using (var files = new InstallFiles(root, Grfs(o)))
            {
                var s = Scene.Build(files, rest[0], new Scene.Options { UserData = UserData(o) });
                var g = new Grid(s); var st = new Grid.RayState(s.Count); var col = new List<(float y, int t)>();
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                for (int k = 1; k + 1 < rest.Count; k += 2)
                {
                    int x = int.Parse(rest[k], inv), y = int.Parse(rest[k + 1], inv);
                    if (!s.InMap(x, y)) { Console.WriteLine($"{x},{y}: outside the map"); continue; }
                    var c = s.Gat.Get(x, y); int i = x + y * s.W;
                    Console.WriteLine($"cell {x},{y} of {s.Map}: GAT type {(int)c.Type} ({c.Type}), passable {s.Walk[i]}, corners SW SE NW NE {c.H0} {c.H1} {c.H2} {c.H3} (RO, y-down) -> floor y {s.Floor[i]:0.###} cells; room {(s.Rooms?.Labels != null ? s.Rooms.Labels[i].ToString() : s.Indoor == "no" ? "-" : "1")}");
                    int tx = x / 2, ty = y / 2;
                    if (tx < s.Gnd.Width && ty < s.Gnd.Height)
                    {
                        var t = s.Gnd.Tiles[tx + ty * s.Gnd.Width];
                        string Surf(int si)
                        {
                            if (si < 0 || si >= s.Gnd.Surfaces.Length) return "none";
                            var sf = s.Gnd.Surfaces[si];
                            string raw = sf.TextureIndex >= 0 && sf.TextureIndex < s.Gnd.Textures.Count ? s.Gnd.Textures[sf.TextureIndex] : "";
                            return $"surface {si} texture '{Names.Display(raw)}'{(s.BlackTextures.Contains(raw) ? " (BLACK: not drawn)" : "")} colour {sf.R},{sf.G},{sf.B}";
                        }
                        Console.WriteLine($"  GND tile {tx},{ty}: corners {t.H0} {t.H1} {t.H2} {t.H3} (RO); top {Surf(t.Top)} (drawn {s.TopDrawn[tx + ty * s.Gnd.Width]}); front wall {Surf(t.Front)}; right wall {Surf(t.Right)}");
                    }
                    g.Column(x + 0.5f, y + 0.5f, col);
                    Console.WriteLine($"  column at the centre: {col.Count} surface(s)" + (col.Count == 0 ? "" : ": " + string.Join("; ", col.OrderBy(h => h.y).Select(h => $"y {h.y:0.###} ({h.y - s.Floor[i]:+0.###;-0.###} from the floor) {s.Owner(h.t)} facing {(s.N[h.t * 3 + 1] >= 0 ? "up" : "down")}"))));
                    var eye = new UnityEngine.Vector3(x + 0.5f, s.Floor[i] + Views.EyeCells, y + 0.5f);
                    float d = g.Nearest(eye, 3f, out int tri, t => s.Kind[t] != TriKind.Water);
                    Console.WriteLine($"  first-person eye at y {eye.y:0.###}: nearest drawn surface {(tri >= 0 ? $"{d:0.###} cells = {d * Views.FirstPersonMetresPerCell:0.###} m: {s.Owner(tri)}" : "none within 3 cells")}");
                    int enc = g.Enclosing(st, eye, col);
                    Console.WriteLine($"  the eye is {(enc >= 0 ? "INSIDE model #" + enc + " " + Names.Display(s.Instances[enc].File) : "not enclosed by a model")}");
                    bool body = g.Capsule(new UnityEngine.Vector3(x + 0.5f, s.Floor[i] + Views.Thresholds.BodyFrom + 0.25f, y + 0.5f), new UnityEngine.Vector3(x + 0.5f, s.Floor[i] + Views.Thresholds.BodyTo - 0.25f, y + 0.5f), 0.25f, out int bt, t => s.Kind[t] != TriKind.Water);
                    Console.WriteLine($"  body capsule (0.6-2.0 cells, radius 0.25): {(body ? "meets " + s.Owner(bt) : "free")}");
                    // the four level looks from the eye (east, north, west, south): what is seen first, and what is passed from behind
                    string[] dirs = { "east", "north", "west", "south" };
                    for (int dk = 0; dk < 4; dk++)
                    {
                        var dv = new UnityEngine.Vector3(dk == 0 ? 1 : dk == 2 ? -1 : 0, 0, dk == 1 ? 1 : dk == 3 ? -1 : 0);
                        var h = g.Ray(st, eye, dv, 0f, 60f, t => s.Kind[t] != TriKind.Water);
                        Console.WriteLine($"  look {dirs[dk]}: {(h.Tri >= 0 ? $"{h.T:0.##} cells: {s.Owner(h.Tri)}" : "nothing within 60 cells")}{(h.BackTri >= 0 ? $"; passed from behind at {h.BackT:0.##}: {s.Owner(h.BackTri)}" : "")}");
                    }
                    // the survey's 192 rays from this eye: the faces passed from behind (with the triangle, to check by hand), the void rays
                    int backs = 0, voids = 0, shown = 0;
                    foreach (var dv in Analysis.SurveyDirections)
                    {
                        var h = g.Ray(st, eye, dv, 0f, 400f, t => s.Kind[t] != TriKind.Water);
                        if (h.Tri < 0 && dv.y < -0.02f) voids++;
                        if (h.BackTri < 0 || s.Kind[h.BackTri] != TriKind.Model) continue;
                        backs++;
                        if (shown++ >= 3) continue;
                        int t = h.BackTri; var n = s.Normal(t); var p = eye + dv * h.BackT;
                        Console.WriteLine($"  survey ray {dv} passes from behind at {h.BackT:0.##} cells (point {p}): {s.Owner(t)}; triangle {s.V(t, 0)} {s.V(t, 1)} {s.V(t, 2)}, drawn-side normal {n}; (eye - point) . normal = {UnityEngine.Vector3.Dot(eye - p, n):0.###} (< 0: the eye is behind it)");
                    }
                    Console.WriteLine($"  survey: {backs} of {Analysis.SurveyRays} rays pass a model face from behind; {voids} rays below the horizon meet nothing");
                    foreach (var m in s.Instances.Where(m => !m.Missing && m.Tris > 0 && m.Min.x <= x + 1 && m.Max.x >= x && m.Min.z <= y + 1 && m.Max.z >= y))
                        Console.WriteLine($"  model #{m.Index} {Names.Display(m.File)} '{Names.Display(m.Name)}': RSW position {m.RoPos} rotation {m.RoRot} scale {m.RoScale}; placed box x {m.Min.x:0.##}..{m.Max.x:0.##} y {m.Min.y:0.##}..{m.Max.y:0.##} z {m.Min.z:0.##}..{m.Max.z:0.##}; {m.Tris} triangles{(m.Animated ? ", animated" : "")}");
                }
            }
            return 0;
        }

        /// <summary>architect fix: Architect's fix mode (Architect.cs Fixer) for the named maps only.</summary>
        private static int Fix(Dictionary<string, string> o, List<string> maps)
        {
            if (!o.TryGetValue("root", out var root) || !o.TryGetValue("out", out var outDir) || maps.Count == 0) return Usage();
            string fullOut = Path.GetFullPath(outDir), fullRoot = Path.GetFullPath(root);
            if (fullOut.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) { Console.Error.WriteLine("architect: --out must not be inside the RO install (it is read-only)"); return 2; }
            if (o.TryGetValue("ledge", out var lr)) { if (lr != "low" && lr != "keep" && lr != "rule") return Usage(); Walls.LedgeRule = lr; }
            if (o.TryGetValue("fill", out var fl)) { if (fl != "day" && fl != "all") return Usage(); Fixer.FillThemes = fl; }
            Directory.CreateDirectory(fullOut);
            using (var files = new InstallFiles(root, Grfs(o), s => Console.WriteLine("[install] " + s)))
            {
                int bad = 0;
                foreach (var map in maps)
                {
                    try { if (Fixer.Run(files, map, fullOut, UserData(o), !o.ContainsKey("rule-only")) != 0) bad++; }
                    catch (Exception e) when (e is FileNotFoundException || e is Formats.RoFormatException) { Console.Error.WriteLine($"{map}: {e.Message}"); bad++; }
                }
                return bad == 0 ? 0 : 1;
            }
        }

        /// <summary>wave 27 (wmodel): `architect models --root R --out O --base &lt;shipped MapFixes folder&gt; [--maps-file F] [map ...]`: the fix
        /// kind M (single-sided model faces) on the named maps (ModelRun.cs). Exit 3 when a map's check FAILs.</summary>
        private static int Models(Dictionary<string, string> o, List<string> maps)
        {
            if (!o.TryGetValue("root", out var root) || !o.TryGetValue("out", out var outDir) || !o.TryGetValue("base", out var baseDir)) return Usage();
            if (o.TryGetValue("maps-file", out var mf)) maps.AddRange(File.ReadAllLines(mf).Select(x => x.Trim()).Where(x => x.Length > 0 && x[0] != '#'));
            if (maps.Count == 0) return Usage();
            string fullOut = Path.GetFullPath(outDir), fullRoot = Path.GetFullPath(root);
            if (fullOut.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) { Console.Error.WriteLine("architect: --out must not be inside the RO install (it is read-only)"); return 2; }
            Directory.CreateDirectory(fullOut);
            using (var files = new InstallFiles(root, Grfs(o), s => Console.WriteLine("[install] " + s)))
            {
                int bad = 0;
                foreach (var map in maps)
                {
                    try { if (ModelRun.Run(files, map, fullOut, UserData(o), baseDir) != 0) bad++; }
                    catch (Exception e) when (e is FileNotFoundException || e is Formats.RoFormatException) { Console.Error.WriteLine($"{map}: {e.Message}"); bad++; }
                    catch (Exception e) { Console.WriteLine($"ERROR {map}: {e}"); bad++; } // one map's fault never stops the others
                }
                return bad == 0 ? 0 : 3;
            }
        }

        /// <summary>wave 27 (wmodel): the model-face generator's counts and time on the named maps (nothing written).</summary>
        private static int ModelsMeasure(Dictionary<string, string> o, List<string> maps)
        {
            if (!o.TryGetValue("root", out var root) || maps.Count == 0) return Usage();
            o.TryGetValue("base", out var baseDir);
            using (var files = new InstallFiles(root, Grfs(o)))
                foreach (var m in maps) ModelFixer.Measure(files, m, UserData(o), baseDir);
            return 0;
        }

        private static int Grid0(Dictionary<string, string> o, List<string> rest)
        {
            if (!o.TryGetValue("root", out var root) || rest.Count < 1) return Usage();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            int x0 = rest.Count > 4 ? int.Parse(rest[1], inv) : 0, y0 = rest.Count > 4 ? int.Parse(rest[2], inv) : 0;
            int x1 = rest.Count > 4 ? int.Parse(rest[3], inv) : 9999, y1 = rest.Count > 4 ? int.Parse(rest[4], inv) : 9999;
            using (var files = new InstallFiles(root, Grfs(o))) return GridDump.Run(files, rest[0], x0, y0, x1, y1);
        }

        private static int Walls0(Dictionary<string, string> o, List<string> rest)
        {
            if (!o.TryGetValue("root", out var root) || rest.Count < 1) return Usage();
            if (o.TryGetValue("ledge", out var lr)) Walls.LedgeRule = lr;
            using (var files = new InstallFiles(root, Grfs(o)))
            {
                var s = Scene.Build(files, rest[0], new Scene.Options { UserData = UserData(o), Rooms = false });
                var w = new Walls(s);
                foreach (var kv in w.Counter()) Console.WriteLine(kv.Key + ": " + (kv.Value is Dictionary<string, object> d ? string.Join(", ", d.Select(x => x.Key + " " + x.Value)) : kv.Value));
                if (rest.Count > 1)
                    foreach (var x in w.All.OrderBy(x => x.Ty).ThenBy(x => x.Tx))
                        Console.WriteLine($"{x.Name}: {x.Cls} A {x.A} ({x.PlayA}) h {x.HA:0.#} | B {x.B} ({x.PlayB}) h {x.HB:0.#}; span {x.Span:0.#}; drawn {x.Drawn} want {x.Want}{(x.Problem ? " PROBLEM" : x.Wrong ? " (changed, low)" : "")}");
            }
            return 0;
        }

        private static int Constants(Dictionary<string, string> o)
        {
            if (!o.TryGetValue("client", out var client)) return Usage();
            var bad = Views.Check(Path.Combine(client, "Assets", "Loki", "Runtime"));
            Views.FollowEye(out float fb, out float fu);
            Console.WriteLine($"first person: {Views.FirstPersonMetresPerCell} m per cell, eye {Views.EyeHeightMetres} m = {Views.EyeCells:0.###} cells; follow: {Views.FollowMetresPerCell} m per cell, eye {fb:0.#} cells back and {fu:0.#} up; table: {Views.TableMetresPerCell} m per cell at zoom 1");
            foreach (var b in bad) Console.WriteLine("CHANGED: " + b);
            Console.WriteLine(bad.Count == 0 ? $"all {Views.SourceCount} restated client numbers still match the source" : $"{bad.Count} of {Views.SourceCount} changed: update Views.cs");
            return bad.Count == 0 ? 0 : 1;
        }
    }
}
