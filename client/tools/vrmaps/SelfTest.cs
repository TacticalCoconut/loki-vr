using System;
using System.Collections.Generic;
using System.Linq;
using Loki.Formats;
using Loki.World;
using UnityEngine;

namespace Loki.VrMaps
{
    /// <summary>
    /// `architect selftest`: the stand-in's math, the small geometry, and one synthetic map per detector whose answer is known (built
    /// by <see cref="Synth"/>, read back by the client's own readers and mesh builders). Exit code 0 when every check passes.
    /// </summary>
    public static class SelfTest
    {
        private static int _pass, _fail;
        private static void Check(string name, bool ok, string detail = "")
        {
            if (ok) _pass++; else { _fail++; Console.WriteLine("FAIL " + name + (detail.Length > 0 ? ": " + detail : "")); }
            if (ok && Environment.GetEnvironmentVariable("ARCHITECT_SELFTEST_VERBOSE") == "1") Console.WriteLine("ok   " + name + (detail.Length > 0 ? ": " + detail : ""));
        }
        private static bool Near(float a, float b, float e = 1e-4f) => Math.Abs(a - b) <= e;

        public static int Run()
        {
            _pass = _fail = 0;
            Math3();
            Geometry();
            MainMap();
            IndoorMap();
            FixMap();
            RefineMap();
            ModelMap();
            NameTable();
            Console.WriteLine($"selftest: {_pass} passed, {_fail} failed");
            return _fail == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------ warch2: the game's name table (IO/RoLookup.cs)
        // A loose install in a folder of the OS temp folder (its files rewritten each run): data\resnametable.txt with a comment, an alias
        // to a real map, an alias whose own file exists (the exact file wins), a row naming itself, a repeated name (the first row wins),
        // a minimap alias (relative to data\texture\).
        // ------------------------------------------------------------------ wave 27 (wmodel): the fix kind M (single-sided model faces)
        // A flat walkable map (40 x 40 cells) with: an eave (a single-sided roof slab facing up, 3 cells over walkable cells: its underside is
        // seen from below), a low banner across a walkable path (seen from behind but in the way), a slab under the floor (its underside seen
        // only from below the map), a slab facing down lying 0.01 cells over the ground (seen from behind, but on the ground: z-fighting), a
        // facade at the edge of a 6-cell void whose back faces the void (seen from across it: the outer-wall rule), a two-sided slab (drawn
        // from both sides by the file already), a closed single-sided box standing on walkable cells (a walk-through solid).
        private static void ModelMap()
        {
            const int gw = 20, gh = 20;
            var f = new MemoryFiles();
            var gat = new Synth.Gat(gw * 2, gh * 2); var gnd = new Synth.Gnd(gw, gh); var rsw = new Synth.Rsw { Gnd = "synthm.gnd", Gat = "synthm.gat" };
            f.Add("data\\texture\\synth\\floor.bmp", Synth.Bmp(120, 100, 80)); f.Add("data\\texture\\synth\\wood.bmp", Synth.Bmp(150, 110, 60));
            Vector3 At(float x, float y, float z) => Synth.Rsw.RoAt(x, y, z, gw, gh);
            // the void: tiles 12-14 (cells 24-29) over z cells 10-29, no top, blocked; walkable on both sides of it
            for (int ty = 5; ty < 15; ty++) for (int tx = 12; tx < 15; tx++) { gnd.Top[tx + ty * gw] = -1; for (int k = 0; k < 4; k++) gat.Set(tx * 2 + (k & 1), ty * 2 + (k >> 1), 1, 0); }
            // RO model space (y down, 5 units a cell); RsmMeshBuilder: (-x,-z) (+x,-z) (+x,+z) (-x,+z) faces UP in Loki; (-x,0) (+x,0) (+x,-15) (-x,-15) at z 0 faces SOUTH
            byte[] Flat(bool up, bool two) => up ? Synth.Quad(new Vector3(-10, 0, -10), new Vector3(10, 0, -10), new Vector3(10, 0, 10), new Vector3(-10, 0, 10), two)
                                                  : Synth.Quad(new Vector3(-10, 0, 10), new Vector3(10, 0, 10), new Vector3(10, 0, -10), new Vector3(-10, 0, -10), two);
            f.Add("data\\model\\synth\\eave.rsm", Flat(true, false)); f.Add("data\\model\\synth\\mat.rsm", Flat(false, false)); f.Add("data\\model\\synth\\two.rsm", Flat(true, true));
            f.Add("data\\model\\synth\\banner.rsm", Synth.Quad(new Vector3(-10, 0, 0), new Vector3(10, 0, 0), new Vector3(10, -7.5f, 0), new Vector3(-10, -7.5f, 0), false));
            f.Add("data\\model\\synth\\facade.rsm", Synth.Quad(new Vector3(0, 0, 20), new Vector3(0, 0, -20), new Vector3(0, -15, -20), new Vector3(0, -15, 20), false));
            // a crate 2 x 3 x 2 cells, every face single-sided and facing OUT (Synth.Box is made for two-sided use: its sides face in)
            var cv = new List<Vector3>(); var cf = new List<int[]>();
            void Q(params float[] c) { int b = cv.Count; for (int k = 0; k < 12; k += 3) cv.Add(new Vector3(c[k], c[k + 1], c[k + 2])); cf.Add(new[] { b, b + 1, b + 2 }); cf.Add(new[] { b, b + 2, b + 3 }); }
            Q(-5, 0, -5, 5, 0, -5, 5, -15, -5, -5, -15, -5); Q(-5, -15, 5, 5, -15, 5, 5, 0, 5, -5, 0, 5);       // south, north
            Q(-5, 0, 5, -5, 0, -5, -5, -15, -5, -5, -15, 5); Q(5, -15, 5, 5, -15, -5, 5, 0, -5, 5, 0, 5);       // west, east
            Q(-5, -15, -5, 5, -15, -5, 5, -15, 5, -5, -15, 5); Q(-5, 0, 5, 5, 0, 5, 5, 0, -5, -5, 0, -5);       // top, bottom
            f.Add("data\\model\\synth\\crate.rsm", Synth.Rsm(cv.ToArray(), cf, false, "synth\\wood.bmp"));
            rsw.Models.Add(("synth\\eave.rsm", At(10, 3f, 10), Vector3.zero, Vector3.one));     // 0 the eave
            rsw.Models.Add(("synth\\banner.rsm", At(16, 0.5f, 10), Vector3.zero, Vector3.one)); // 1 the banner across a path (faces south)
            rsw.Models.Add(("synth\\eave.rsm", At(36, -5f, 36), Vector3.zero, Vector3.one));    // 2 under the floor
            rsw.Models.Add(("synth\\mat.rsm", At(10, 0.01f, 30), Vector3.zero, Vector3.one));   // 3 on the ground, facing down
            rsw.Models.Add(("synth\\facade.rsm", At(24f, 0f, 20), Vector3.zero, Vector3.one));  // 4 at the void's west edge
            rsw.Models.Add(("synth\\two.rsm", At(4, 3f, 20), Vector3.zero, Vector3.one));       // 5 two-sided
            rsw.Models.Add(("synth\\crate.rsm", At(16, 0f, 30), Vector3.zero, Vector3.one));    // 6 a walk-through box
            f.Add("data\\synthm.gat", gat.Bytes()); f.Add("data\\synthm.gnd", gnd.Bytes()); f.Add("data\\synthm.rsw", rsw.Bytes());
            var s0 = Scene.Build(f, "synthm", new Scene.Options { Rooms = false });
            int Tri(int inst, int k = 0) => Enumerable.Range(0, s0.Count).Where(t => s0.Kind[t] == TriKind.Model && s0.Src[t] == inst).Skip(k).DefaultIfEmpty(-1).First();
            Check("M: the builder's face record: every model triangle names its node and face; the two-sided slab's copies are marked",
                Enumerable.Range(0, s0.Count).Where(t => s0.Kind[t] == TriKind.Model).All(t => s0.Node[t] == 0 && s0.Face[t] >= 0)
                && Enumerable.Range(0, s0.Count).Count(t => s0.Kind[t] == TriKind.Model && s0.Src[t] == 5 && s0.Side[t] == Scene.SideTwoBack) == 2
                && Enumerable.Range(0, s0.Count).Count(t => s0.Kind[t] == TriKind.Model && s0.Src[t] == 0 && s0.Side[t] == Scene.SideSingle) == 2);
            Check("M: the synthetic faces face the way they are meant to", Tri(0) >= 0 && s0.Normal(Tri(0)).y > 0.99f && s0.Normal(Tri(3)).y < -0.99f && s0.Normal(Tri(1)).z < -0.99f && s0.Normal(Tri(4)).x < -0.99f,
                $"eave {s0.Normal(Tri(0))}, mat {s0.Normal(Tri(3))}, banner {s0.Normal(Tri(1))}, facade {s0.Normal(Tri(4))}");
            var crateC = new Vector3(16f, 1.5f, 30f);
            Check("M: the crate's faces all face out", Enumerable.Range(0, 12).All(k => Vector3.Dot(s0.Normal(Tri(6, k)), ModelFixer.Centre(s0, Tri(6, k)) - crateC) > 0.5f));
            Check("M: an eye inside the crate is inside a solid; an eye under the eave is not", ModelFixer.InsideSolid(s0, new Grid(s0), new Grid.RayState(s0.Count), new Vector3(15.5f, Views.EyeCells, 29.5f), 6)
                && !ModelFixer.InsideSolid(s0, new Grid(s0), new Grid.RayState(s0.Count), new Vector3(10.5f, Views.EyeCells, 10.5f), 0));
            Check("M: the outer-wall rule on a face: the facade's back is the void, the eave's is not", ModelFixer.BackIsVoid(s0, Tri(4)) && !ModelFixer.BackIsVoid(s0, Tri(0)));
            Check("M: a model's whole box (the client's test for a Follow eye inside it)", ModelFixer.InBox(s0.Instances[6], crateC) && !ModelFixer.InBox(s0.Instances[6], crateC + new Vector3(3f, 0f, 0f))
                && Near(s0.Instances[6].BoxMin.x, 15f, 1e-3f) && Near(s0.Instances[6].BoxMax.y, 3f, 1e-3f), $"{s0.Instances[6].BoxMin} {s0.Instances[6].BoxMax}");
            Check("M: the banner stands in a walkable passage at body height; the eave over the heads does not", ModelFixer.InTheWay(s0, Tri(1)) && !ModelFixer.InTheWay(s0, Tri(0)));
            var fix = new MapFix { Map = s0.Map, Width = gw, GndBytes = 1, GatBytes = 1, GndHash = "x", GatHash = "y" };
            var st = new ModelFixer.Stats();
            var patches = ModelFixer.Generate(f, "synthm", null, fix, s0, st);
            bool Added(int inst) => fix.Models.ContainsKey(inst);
            Check("M: the eave's underside is drawn (seen from below by the first-person eye)", Added(0) && fix.Models[0].Count == 2 && patches.Any(p => p.Inst == 0 && p.Level == FixCheck.SeenFirstPerson && p.Kind == "under"),
                string.Join(", ", fix.Models.Keys));
            Check("M: the banner seen from behind is left as it is: it stands in the way", !Added(1) && patches.Any(p => p.Inst == 1 && p.Level >= FixCheck.SeenFollow && p.InTheWay == 2));
            Check("M: the slab under the floor is left as it is: no player sees its underside", !Added(2) && patches.Where(p => p.Inst == 2).All(p => p.Level < FixCheck.SeenFollow));
            Check("M: the slab on the ground is left as it is: its back copy would z-fight with the ground", !Added(3) && patches.Any(p => p.Inst == 3 && p.Level >= FixCheck.SeenFollow && p.Coplanar == 2));
            Check("M: the facade seen from across the void is left as it is (the outer-wall rule)", !Added(4) && patches.Any(p => p.Inst == 4 && p.Level >= FixCheck.SeenFollow && p.Void == 2));
            Check("M: the two-sided slab is never a candidate", !Added(5) && !patches.Any(p => p.Inst == 5));
            Check("M: the walk-through box gets no back copy (seen from behind only from inside it)", !Added(6) && patches.Where(p => p.Inst == 6).All(p => p.Level < FixCheck.SeenFollow) && st.InsideSolidEyes >= 1, $"{(Added(6) ? fix.Models[6].Count : 0)} faces; its patches: " + string.Join("; ", patches.Where(p => p.Inst == 6).Select(p => $"n {p.N} level {p.Level} eye {p.EyeX},{p.EyeY} {p.Eye} in the way {p.InTheWay}")));
            Check("M: the counts", st.AddedTris == 2 && st.AddedPatches == 1 && st.ExInTheWay == 2 && st.ExVoid == 2 && st.ExCoplanar == 2, $"added {st.AddedTris} in {st.AddedPatches}, in the way {st.ExInTheWay}, void {st.ExVoid}, z-fight {st.ExCoplanar}");
            // the file: written and read back by the client's own reader; its guards
            string text = fix.ToText(new[] { "test" });
            var back = MapFix.Parse(text, gw);
            Check("M: the fix file reads back as written (R and M lines)", back.ToText(new[] { "test" }) == text && back.ModelFaceCount == 2 && back.RswHash == s0.RswHash && !back.HasGround && text.Contains("\nR\trsw:") && text.Contains("\nM\t0\t"), text);
            Check("M: runs of face indices", MapFix.Runs(new[] { 0, 1, 2, 3, 7, 9, 10 }) == "0-3,7,9-10" && MapFix.Parse("architect-fix\t1\tm\tgnd:1:a\tgat:1:b\nR\trsw:5:c\nM\t2\t9:d\t1\t0-3,7\tunder\n", 4).Models[2].Count == 5);
            bool threw = false; try { MapFix.Parse("architect-fix\t1\tm\tgnd:1:a\tgat:1:b\nM\t2\t9:d\t1\t0-3\tunder\n", 4); } catch (FormatException) { threw = true; }
            Check("M: M lines without the R line are refused", threw);
            Check("M: the RSW guard", back.MatchesRsw(f.Read("data\\synthm.rsw")) && !back.MatchesRsw(new byte[] { 1, 2, 3 }));
            var s1 = Scene.Build(f, "synthm", new Scene.Options { Rooms = false, Fix = back });
            var addedT = Enumerable.Range(0, s1.Count).Where(t => s1.Kind[t] == TriKind.Model && s1.Side[t] == Scene.SideAdded).ToList();
            Check("M: the client's builder adds the back copies: reversed, lit from their own side", s1.FixModelFaces == 2 && s1.FixModelInstances == 1 && addedT.Count == 2 && addedT.All(t => s1.Normal(t).y < -0.99f) && s1.AddedNormalsAgree == 2 && s1.AddedNormalsDisagree == 0,
                $"faces {s1.FixModelFaces}, instances {s1.FixModelInstances}, normals agree {s1.AddedNormalsAgree} / disagree {s1.AddedNormalsDisagree}");
            var g1 = new Grid(s1); var st1 = new Grid.RayState(s1.Count);
            var eye = new Vector3(10.5f, Views.EyeCells, 10.5f); var h = g1.Ray(st1, eye, Vector3.up, 0f, 10f); var h0 = new Grid(s0).Ray(new Grid.RayState(s0.Count), eye, Vector3.up, 0f, 10f);
            Check("M: under the eave the eye now sees its underside (the game's map: see-through)", h0.Tri < 0 && h0.BackTri >= 0 && h.Tri >= 0 && s1.Side[h.Tri] == Scene.SideAdded && Math.Abs(h.T - (3f - Views.EyeCells)) < 0.01f, $"before {h0.Tri} / {h0.BackTri}; after {h.Tri} at {h.T}");
            var bad = MapFix.Parse(text.Replace(fix.Models[0].Hash, "0000000000000000"), gw);
            var s2 = Scene.Build(f, "synthm", new Scene.Options { Rooms = false, Fix = bad });
            Check("M: the RSM guard: lines made for another model file are not applied", s2.FixModelFaces == 0 && s2.FixModelSkipped == 1);
            var badRsw = MapFix.Parse(text.Replace(s0.RswHash, "0000000000000000"), gw);
            var s3 = Scene.Build(f, "synthm", new Scene.Options { Rooms = false, Fix = badRsw });
            Check("M: the RSW guard in the build: lines made for another RSW are not applied", s3.FixModelFaces == 0 && !s3.FixRswMatches);
            var chk = ModelCheck.Run(s0, s1, back, patches, st, null, null);
            Check("M: the check on the synthetic map", !chk.Fail && chk.AddedTris == 2 && chk.Floating == 0 && chk.ZFight == 0 && chk.InTheWay == 0 && chk.Outer == 0 && chk.InsideOut == 0, chk.Line());
            // the check catches each catastrophic class when it is there: a file naming the banner, the mat and the facade
            var wrong = MapFix.Parse(text, gw);
            void Name(int inst, int faces = 2) { var i = s0.Instances[inst]; var m = new MapFix.ModelFaces { Instance = inst, Bytes = i.RsmBytes, Hash = i.RsmHash }; for (int k = 0; k < faces; k++) m.Add(0, k); wrong.Models[inst] = m; }
            Name(1); Name(3); Name(4); Name(2); Name(6, 12);
            var sw = Scene.Build(f, "synthm", new Scene.Options { Rooms = false, Fix = wrong });
            var allP = ModelFixer.Patches(s0, t => ModelFixer.Candidate(s0, t)); foreach (var p in allP) { p.Added.Clear(); p.Added.AddRange(p.Tris); }
            var cw = ModelCheck.Run(s0, sw, wrong, allP, st, null, null);
            Check("M: the check FAILS a file that names faces in the way, on the ground, toward the void, seen by no player, boxing an eye in", cw.Fail && cw.InTheWay >= 2 && cw.ZFight >= 2 && cw.Outer == 2 && cw.Floating >= 1 && cw.BoxedEyes >= 1, cw.Line());
        }

        private static void NameTable()
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "architect-selftest-nametable");
            string data = System.IO.Path.Combine(root, "data"), map = System.IO.Path.Combine(data, "texture", "map");
            System.IO.Directory.CreateDirectory(map);
            void W(string path, string text) => System.IO.File.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes(text));
            W(System.IO.Path.Combine(data, "real.rsw"), "REAL"); W(System.IO.Path.Combine(data, "own.rsw"), "OWN"); W(System.IO.Path.Combine(data, "other.rsw"), "OTHER");
            W(System.IO.Path.Combine(map, "real.bmp"), "BMP");
            W(System.IO.Path.Combine(data, "resnametable.txt"), "//comment#x#\r\nalias.rsw#real.rsw#\r\nOwn.RSW#real.rsw#\r\n\r\nself.rsw#self.rsw#\r\ntwice.rsw#real.rsw#\r\ntwice.rsw#other.rsw#\r\nmap\\alias.bmp#map\\real.bmp#\r\n");
            var lk = new Loki.IO.RoLookup(root, new List<Loki.IO.GrfArchive>());
            int n = lk.LoadNameTable();
            string R(string path) { var b = lk.Read(Loki.IO.RoPath.Normalize(path), out _); return b == null ? null : System.Text.Encoding.ASCII.GetString(b); }
            Check("name table: rows kept (a row naming itself left out, a repeated name once)", n == 4, n.ToString());
            Check("name table: an alias reads the real file; the exact file wins over its alias", R("data\\alias.rsw") == "REAL" && R("data\\own.rsw") == "OWN", R("data\\alias.rsw") + " " + R("data\\own.rsw"));
            Check("name table: the first row of a repeated name wins; a row naming itself changes nothing", R("data\\twice.rsw") == "REAL" && R("data\\self.rsw") == null);
            Check("name table: a minimap alias relative to data\\texture\\; RealPath and Exists follow it",
                R("data\\texture\\map\\alias.bmp") == "BMP" && lk.RealPath("data\\alias.rsw") == "data\\real.rsw" && lk.Exists("data\\alias.rsw") && !lk.Exists("data\\nothing.rsw"));
        }

        // ------------------------------------------------------------------ the stand-in's math (Unity's documented semantics)
        private static void Math3()
        {
            var a = Matrix4x4.identity; a.m03 = 1; a.m13 = 2; a.m23 = 3;            // T(1, 2, 3)
            var s = Matrix4x4.Scale(new Vector3(2, 3, 4));
            var p = (a * s).MultiplyPoint3x4(new Vector3(1, 1, 1));                 // T * S * p = (3, 5, 7)
            Check("matrix T*S*p", Near(p.x, 3) && Near(p.y, 5) && Near(p.z, 7), p.ToString());
            Check("matrix MultiplyVector ignores translation", (a * s).MultiplyVector(new Vector3(1, 1, 1)) == new Vector3(2, 3, 4));
            Check("matrix determinant", Near(s.determinant, 24f) && Near(RoSpace.F.determinant, -0.008f, 1e-6f));
            var r = RoMat.RotY(0.7f) * RoMat.RotX(0.3f);
            Check("rotation determinant 1", Near(r.determinant, 1f, 1e-5f));
            var rt = r.transpose * r; Check("R^T R = I", Near(rt.m00, 1) && Near(rt.m11, 1) && Near(rt.m22, 1) && Near(rt.m01, 0) && Near(rt.m12, 0));
            var c = (a * s).GetColumn(3); Check("GetColumn(3) = translation", Near(c.x, 1) && Near(c.y, 2) && Near(c.z, 3) && Near(c.w, 1));
            var q0 = new Quaternion(0, 0, 0, 1); var q1 = new Quaternion(0, (float)Math.Sin(0.5), 0, (float)Math.Cos(0.5)); // 1 rad about y
            var qh = Quaternion.Slerp(q0, q1, 0.5f);
            Check("slerp halfway", Near(qh.y, (float)Math.Sin(0.25), 1e-5f) && Near(qh.w, (float)Math.Cos(0.25), 1e-5f));
            Check("Mathf.Repeat", Near(Mathf.Repeat(7.5f, 2f), 1.5f) && Near(Mathf.Repeat(-0.5f, 2f), 1.5f));
            Check("Vector3.normalized of zero", Vector3.zero.normalized == Vector3.zero);
            // RoSpace / RoMat as the game uses them
            var lp = RoSpace.ToLoki(new Vector3(5, -10, 15)); Check("RoSpace.ToLoki", lp == new Vector3(1, 2, 3));
            var m = RsmMeshBuilder.InstanceMatrix(new Vector3(0, -5, 0), Vector3.zero, Vector3.one, new Vector3(10, 0, 10));
            var mp = m.MultiplyPoint3x4(Vector3.zero); Check("InstanceMatrix places at centre + ToLoki(pos)", mp == new Vector3(10, 1, 10), mp.ToString());
        }

        private static void Geometry()
        {
            Vector3 a = new Vector3(0, 0, 0), b = new Vector3(1, 0, 0), c = new Vector3(0, 0, 1);
            Check("closest point above the triangle", Geo.ClosestOnTriangle(new Vector3(0.2f, 1, 0.2f), a, b, c) == new Vector3(0.2f, 0, 0.2f));
            Check("closest point beyond a vertex", Geo.ClosestOnTriangle(new Vector3(-1, 0, -1), a, b, c) == a);
            Check("segment crossing a triangle", Geo.SegmentTriangleDistance(new Vector3(0.2f, -1, 0.2f), new Vector3(0.2f, 1, 0.2f), a, b, c) == 0f);
            Check("segment beside a triangle", Near(Geo.SegmentTriangleDistance(new Vector3(2, -1, 0), new Vector3(2, 1, 0), a, b, c), 1f));
            Check("ray hits a triangle", Geo.RayTriangle(new Vector3(0.2f, 1, 0.2f), new Vector3(0, -1, 0), a, b, c, out float t) && Near(t, 1f));
            Check("segment-segment", Near(Geo.SegmentSegment(new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0.5f, 1, -1), new Vector3(0.5f, 1, 1)), 1f));
            var poly = new List<Vector3> { new Vector3(-1, 0, 0.5f), new Vector3(3, 4, 0.5f), new Vector3(3, 0, 0.5f) }; var tmp = new List<Vector3>();
            Geo.ClipXZ(poly, tmp, 0, 1, 0, 1); Check("clip to the cell keeps the part inside", poly.Count >= 3 && Near(poly.Max(p => p.y), 2f, 1e-4f), string.Join(" ", poly));
        }

        // ------------------------------------------------------------------ the outdoor test map (40 x 40 cells)
        private const int GW = 20, GH = 20;
        private static Vector3 At(float x, float y, float z) => Synth.Rsw.RoAt(x, y, z, GW, GH);

        private static void MainMap()
        {
            var f = new MemoryFiles();
            var gat = new Synth.Gat(GW * 2, GH * 2); var gnd = new Synth.Gnd(GW, GH); var rsw = new Synth.Rsw();
            gnd.Textures.Add("synth\\black.bmp"); f.Add("data\\texture\\synth\\black.bmp", Synth.Bmp(0, 0, 0)); f.Add("data\\texture\\synth\\floor.bmp", Synth.Bmp(120, 100, 80));
            // C1: a blocked, flat, empty strip (30, 4..8); a blocked cell under a tall box (30, 20): not an invisible wall
            for (int y = 4; y <= 8; y++) gat.Set(30, y, 1, 0);
            gat.Set(30, 20, 1, 0); f.Add("data\\model\\synth\\pillar.rsm", Synth.Box(1, 3, 1)); rsw.Models.Add(("synth\\pillar.rsm", At(30.5f, 0, 20.5f), Vector3.zero, Vector3.one));
            // B3 / C3: a raised tile (10, 15) = cells 20-21, 30-31, 3 cells up, with its four walls; the GAT there stays at 0
            gnd.SetHeight(10, 15, -15f); gnd.Front[10 + 14 * GW] = 1; gnd.Front[10 + 15 * GW] = 1; gnd.Right[9 + 15 * GW] = 1; gnd.Right[10 + 15 * GW] = 1;
            // B1 / B2 high: a slab 3 x 0.2 x 3 at 2.0 cells over cells 9-11, 9-11; medium: the same at 2.35 over cells 24-26, 9-11
            f.Add("data\\model\\synth\\slab.rsm", Synth.Box(3, 0.2f, 3));
            rsw.Models.Add(("synth\\slab.rsm", At(10.5f, 2.0f, 10.5f), Vector3.zero, Vector3.one));
            rsw.Models.Add(("synth\\slab.rsm", At(25.5f, 2.35f, 10.5f), Vector3.zero, Vector3.one));
            // C2: a post 0.4 x 3 x 0.4 at cell (5, 25)
            f.Add("data\\model\\synth\\post.rsm", Synth.Box(0.4f, 3, 0.4f)); rsw.Models.Add(("synth\\post.rsm", At(5.5f, 0, 25.5f), Vector3.zero, Vector3.one));
            // D1: a tile with no top (2, 18) = cells 4-5, 36-37, and a tile with the black void filler (17, 2) = cells 34-35, 4-5; both blocked
            gnd.Top[2 + 18 * GW] = -1; gnd.TopTexture[17 + 2 * GW] = 1;
            for (int y = 36; y <= 37; y++) for (int x = 4; x <= 5; x++) gat.Set(x, y, 1, 0);
            for (int y = 4; y <= 5; y++) for (int x = 34; x <= 35; x++) gat.Set(x, y, 1, 0);
            // B4: a pool, tiles 3-4 x 3-4 = cells 6-9, 6-9, 4 cells down, water 1 cell down (GND 1.8 water)
            gnd.Water = true; gnd.WaterLevel = 5f; gnd.WaveHeight = 1f;
            for (int ty = 3; ty <= 4; ty++) for (int tx = 3; tx <= 4; tx++) gnd.SetHeight(tx, ty, 20f);
            for (int y = 6; y <= 9; y++) for (int x = 6; x <= 9; x++) gat.Set(x, y, 3, 20f);
            // F1: 20 lights in one spot; H1: a sound nobody hears; J2: a 40-cell tower at (35, 35)
            for (int i = 0; i < 20; i++) rsw.Lights.Add((At(20, 1, 20), 10f));
            rsw.Sounds.Add(("synth.wav", At(-30, 0, -30), 5f)); rsw.Sounds.Add(("heard.wav", At(20, 0, 20), 50f));
            f.Add("data\\model\\synth\\tower.rsm", Synth.Box(1, 40, 1)); rsw.Models.Add(("synth\\tower.rsm", At(35.5f, 0, 35.5f), Vector3.zero, Vector3.one));
            // enclosure: a shed 4 x 3 x 4 cells with no floor (as RO's buildings) over cells 14-17, 20-23, single-sided
            f.Add("data\\model\\synth\\shed.rsm", Synth.Box(4, 3, 4, false, bottom: false)); rsw.Models.Add(("synth\\shed.rsm", At(16f, 0, 22f), Vector3.zero, Vector3.one));
            // D1 line of sight: a hole (tile 7, 7 = cells 14-15) inside a blocked courtyard (cells 13-17, 13-17) walled 5 cells high
            gnd.Top[7 + 7 * GW] = -1;
            for (int y = 13; y <= 17; y++) for (int x = 13; x <= 17; x++) gat.Set(x, y, 1, 0);
            f.Add("data\\model\\synth\\wallx.rsm", Synth.Box(5.2f, 5, 0.2f)); f.Add("data\\model\\synth\\wallz.rsm", Synth.Box(0.2f, 5, 5.2f));
            rsw.Models.Add(("synth\\wallx.rsm", At(15.5f, 0, 13f), Vector3.zero, Vector3.one)); rsw.Models.Add(("synth\\wallx.rsm", At(15.5f, 0, 18f), Vector3.zero, Vector3.one));
            rsw.Models.Add(("synth\\wallz.rsm", At(13f, 0, 15.5f), Vector3.zero, Vector3.one)); rsw.Models.Add(("synth\\wallz.rsm", At(18f, 0, 15.5f), Vector3.zero, Vector3.one));
            // D3: a single-sided facade, 4 cells wide and 3 high, standing in the open at x = 26, z 20-24 (seen from both sides by the survey)
            f.Add("data\\model\\synth\\facade.rsm", Synth.Quad(new Vector3(0, 0, -10), new Vector3(0, -15, -10), new Vector3(0, -15, 10), new Vector3(0, 0, 10), false));
            rsw.Models.Add(("synth\\facade.rsm", At(26f, 0, 22f), Vector3.zero, Vector3.one));
            // texture cut-out: a two-sided grille 4 x 3 cells at x = 33, z 26-30 whose picture's top half is magenta (cut) and bottom half wood
            f.Add("data\\texture\\synth\\halfgrille.bmp", Synth.Bmp2(new byte[] { 255, 0, 255 }, new byte[] { 120, 90, 60 }));
            f.Add("data\\model\\synth\\grille.rsm", Synth.Quad(new Vector3(0, 0, -10), new Vector3(0, -15, -10), new Vector3(0, -15, 10), new Vector3(0, 0, 10), true, "synth\\halfgrille.bmp",
                new[] { new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1) }));
            rsw.Models.Add(("synth\\grille.rsm", At(33f, 0, 28f), Vector3.zero, Vector3.one));
            // F4: a map effect in a corner, in sight from the far side of the map
            rsw.Effects.Add((47, At(2.5f, 0.5f, 2.5f)));
            f.Add("data\\synth.gat", gat.Bytes()); f.Add("data\\synth.gnd", gnd.Bytes()); f.Add("data\\synth.rsw", rsw.Bytes());

            var s = Scene.Build(f, "synth", new Scene.Options { Rooms = true });
            var g = new Grid(s); var r = new MapReport { Map = "synth" };
            var an = new Analysis(s, g, r); an.RunStep1(); an.RunSurvey(); an.RunStep3();

            // the scene: the client's readers and builders read what Synth wrote
            Check("scene: GAT size", s.W == 40 && s.H == 40);
            Check("scene: the black texture is void filler", s.GroundCulledSurfaces == 1 && s.BlackTextures.Count == 1, $"{s.GroundCulledSurfaces} {string.Join(",", s.BlackTextures)}");
            var slab = s.Instances[1];
            Check("scene: a model is placed as the map loader places it", Near(slab.Min.x, 9f, 1e-3f) && Near(slab.Max.x, 12f, 1e-3f) && Near(slab.Min.y, 2.0f, 1e-3f) && Near(slab.Max.y, 2.2f, 1e-3f) && Near(slab.Min.z, 9f, 1e-3f),
                $"{slab.Min} {slab.Max}");
            Check("scene: the water", s.HasWater && s.WaterTiles == 4 && Near(s.WaterY, -1f), $"{s.WaterTiles} {s.WaterY}");
            bool frontOk = true, rightOk = true, topOk = true; int fronts = 0, rights = 0;
            for (int t = 0; t < s.Count; t++)
            {
                var n = s.Normal(t);
                if (s.Kind[t] == TriKind.WallFront) { fronts++; frontOk &= n == Vector3.forward; }
                else if (s.Kind[t] == TriKind.WallRight) { rights++; rightOk &= n == Vector3.right; }
                else if (s.Kind[t] == TriKind.Top) topOk &= n.y > 0.5f;
            }
            Check("scene: a Front wall's drawn side faces +z (north), a Right wall's +x (east), tops up", frontOk && rightOk && topOk && fronts == 4 && rights == 4, $"{fronts} {rights} {frontOk} {rightOk} {topOk}");
            Check("scene: a two-sided box face is drawn both ways", s.Kind.Where((k, t) => k == TriKind.Model && s.Src[t] == 1).Count() == 24);

            Finding One(string kind, int x, int y) => r.Findings.FirstOrDefault(fd => fd.Kind == kind && fd.Region != null && fd.Region.Contains(x, y));
            string Sev(string kind, int x, int y) => One(kind, x, y)?.Severity ?? "-";
            // B1 / B2
            Check("B1 head inside the slab: high", Sev("fp.head_clip", 10, 10) == "high", Sev("fp.head_clip", 10, 10));
            Check("B2 ceiling at 2.0 cells: high", Sev("fp.low_ceiling", 10, 10) == "high");
            Check("B1 slab 0.21 cells above the eye: medium", Sev("fp.head_clip", 25, 10) == "medium", Sev("fp.head_clip", 25, 10));
            Check("B2 ceiling at 2.35 cells: medium (the sprite's head)", Sev("fp.low_ceiling", 25, 10) == "medium", Sev("fp.low_ceiling", 25, 10));
            Check("B1 nothing in the open", Sev("fp.head_clip", 12, 33) == "-" && Sev("fp.low_ceiling", 12, 33) == "-");
            Check("B1 a wall 0.5 cells from the eye: low", Sev("fp.head_clip", 19, 30) == "low", Sev("fp.head_clip", 19, 30));
            // B3 / C3
            Check("B3 feet 3 cells under the drawn top: high", Sev("fp.floor_gap", 20, 30) == "high");
            var gapF = One("fp.floor_gap", 20, 30); Check("B3 value = GAT floor - drawn surface = -3", gapF != null && Near(Convert.ToSingle(gapF.Measure["value"]), -3f, 1e-3f));
            Check("B3 none on the flat", Sev("fp.floor_gap", 12, 33) == "-");
            Check("C3 a 3-cell wall between walkable cells: high", Sev("walk.wall_on_path", 20, 29) == "high" && Sev("walk.wall_on_path", 22, 31) == "high" && Sev("walk.wall_on_path", 21, 31) == "high");
                        // D4: the raised tile's south and west walls rise toward +z / +x: drawn only from inside the tile, missing from the south / west
            Check("D4 the south wall is missing from the south: high", Sev("back.gnd_wall", 20, 29) == "high" && Sev("back.gnd_wall", 21, 29) == "high");
            Check("D4 the west wall is missing from the west: high", Sev("back.gnd_wall", 19, 30) == "high" && Sev("back.gnd_wall", 19, 31) == "high");
            Check("D4 the north and east walls face their low side", Sev("back.gnd_wall", 20, 32) == "-" && Sev("back.gnd_wall", 22, 30) == "-");
            // C1
            var inv = One("walk.invisible_wall", 30, 6);
            Check("C1 the blocked empty strip is an invisible wall (medium, 5 cells)", inv != null && inv.Severity == "medium" && inv.Count == 5, inv == null ? "none" : inv.Severity + " " + inv.Count);
            Check("C1 not under the pillar", One("walk.invisible_wall", 30, 20) == null);
            // C2
            Check("C2 the post in the body: high", Sev("walk.walk_through", 5, 25) == "high");
            Check("C2 none beside it", Sev("walk.walk_through", 3, 25) == "-");
            Check("C2 inside the floorless shed: high", Sev("walk.walk_through", 15, 21) == "high", Sev("walk.walk_through", 15, 21));
            Check("B1 the eye inside the shed: high", Sev("fp.head_clip", 15, 21) == "high", Sev("fp.head_clip", 15, 21));
            Check("the drawn side agrees with the builder's normals", s.NormalsAgree > 0 && s.NormalsDisagree == 0, $"{s.NormalsAgree} / {s.NormalsDisagree}");
            // D1
            Check("D1 the hole beside walkable cells: high", Sev("void.hole", 4, 36) == "high");
            Check("D1 the black void filler is a hole too", Sev("void.hole", 34, 4) == "high");
            Check("D1 a hole behind 5-cell walls is not seen", Sev("void.hole", 14, 14) == "-" && Convert.ToInt32(r.Metrics["D1 void.hole"]["hiddenByGeometry"]) >= 4, Sev("void.hole", 14, 14));
            Check("C1 not behind a model wall on the shared edge", One("walk.invisible_wall", 13, 15) == null);
            // B4
            Check("B4 eye under the pool's water: high", Sev("fp.underwater", 7, 7) == "high");
            // F1, H1, J2
            Check("F1 20 lights: popping (low)", Sev("light.popping", 20, 20) == "low");
            Check("H1 the unheard sound", r.Findings.Any(fd => fd.Kind == "sound.unheard" && fd.Count == 1));
            Check("J2 the 40-cell tower reaches the head on the table", r.Findings.Any(fd => fd.Kind == "table.tall" && fd.Count == 1));
            Check("not indoor", s.Indoor == "no");
            // texture cut-out: the grille's top half is see-through, its bottom half stops the eye (the shader's clip, the picture's rows top-down)
            var stc = new Grid.RayState(s.Count);
            var lowRay = g.Ray(stc, new Vector3(31f, 0.8f, 28.3f), new Vector3(1, 0, 0), 0f, 5f, t => s.Kind[t] != TriKind.Water);
            var highRay = g.Ray(stc, new Vector3(31f, 2.2f, 28.3f), new Vector3(1, 0, 0), 0f, 5f, t => s.Kind[t] != TriKind.Water);
            Check("cut-out: the wood half stops a ray", lowRay.Tri >= 0 && Near(lowRay.T, 2f, 1e-3f), lowRay.Tri + " " + lowRay.T);
            Check("cut-out: the magenta half lets it through", highRay.Tri < 0 || highRay.T > 2.01f, highRay.Tri + " " + highRay.T);
            Check("cut-out: the grille's materials know the cut", s.Mats.Any(mt => mt.Texture != null && mt.Texture.EndsWith("halfgrille.bmp") && Near(mt.CutShare, 0.5f)));
            // the eye survey
            int facade = s.Instances.FindIndex(i => i.File.EndsWith("facade.rsm"));
            Check("D3 the single-sided facade is seen from behind", r.Findings.Any(fd => fd.Kind == "back.model_face" && fd.Cause != null && fd.Cause[0].StartsWith("model #" + facade + " ")), string.Join(";", r.Findings.Where(fd => fd.Kind == "back.model_face").Select(fd => fd.Cause[0])));
            Check("D3 the two-sided boxes are never seen from behind", !r.Findings.Any(fd => fd.Kind == "back.model_face" && fd.Cause != null && (fd.Cause[0].Contains("slab") || fd.Cause[0].Contains("tower"))));
            Check("E1 the facade is a flat card seen edge-on", r.Findings.Any(fd => fd.Kind == "flat.card" && fd.Cause[0].StartsWith("model #" + facade + " ")));
            Check("E1 a box is not a card", !r.Findings.Any(fd => fd.Kind == "flat.card" && (fd.Cause[0].Contains("slab") || fd.Cause[0].Contains("shed"))));
            Check("L2 model heights", r.Metrics.ContainsKey("L2 scale.stats") && r.MapFacts.ContainsKey("scale"));
            Check("D2 the map's edge is in sight", r.Findings.Any(fd => fd.Kind == "void.edge_seen"));
            Check("K1 long sight lines on the open map", r.Findings.Any(fd => fd.Kind == "view.popin"));
            Check("F4 the corner effect is seen from beyond 25 cells", r.Findings.Any(fd => fd.Kind == "fx.popin" && fd.Count == 1));
            Check("J3 the Follow ring measured", Convert.ToSingle(r.Metrics["survey"]["followRingNearShare"]) >= 0f && Convert.ToInt32(r.Metrics["survey"]["samples"]) > 100);
            Check("report serializes", r.Json().Length > 100 && r.Markdown().Length > 50);
        }

        // ------------------------------------------------------------------ Architect's fix mode (synthetic map, 40 x 40 cells, walkable at 0)
        private static void FixMap()
        {
            const int gw = 20, gh = 20;
            var f = new MemoryFiles();
            var gat = new Synth.Gat(gw * 2, gh * 2); var gnd = new Synth.Gnd(gw, gh); var rsw = new Synth.Rsw { Gnd = "synthfix.gnd", Gat = "synthfix.gat" };
            f.Add("data\\texture\\synth\\floor.bmp", Synth.Bmp(120, 100, 80));
            gnd.Textures.Add("synth\\tinted.bmp"); gnd.BlackTint.Add(1); f.Add("data\\texture\\synth\\tinted.bmp", Synth.Bmp(120, 100, 80));
            void Block(int tx, int ty) { for (int k = 0; k < 4; k++) gat.Set(tx * 2 + (k & 1), ty * 2 + (k >> 1), 1, 0); }
            // 1 steps: tile (5, 5) raised 3 cells, walkable all round and on top (the GAT stays at 0): four walls, P | P, each with one face
            //   under the raised top; interior: tile (12, 5) raised 3 cells WITHOUT a top (walkable): the wall to it from (12, 4) shows both faces
            gnd.SetHeight(5, 5, -15f); gnd.Front[5 + 4 * gw] = 1; gnd.Front[5 + 5 * gw] = 1; gnd.Right[4 + 5 * gw] = 1; gnd.Right[5 + 5 * gw] = 1;
            gnd.SetHeight(12, 5, -15f); gnd.Top[12 + 5 * gw] = -1; gnd.Front[12 + 4 * gw] = 1;
            // 2 outer, drawn toward the void: tiles 15-19 (cells 30-39) the void, 3 cells UP; the Right walls of tiles (14, 8..9)
            for (int ty = 0; ty < gh; ty++) for (int tx = 15; tx < gw; tx++) { gnd.Top[tx + ty * gw] = -1; gnd.SetHeight(tx, ty, -15f); Block(tx, ty); }
            gnd.Right[14 + 8 * gw] = 1; gnd.Right[14 + 9 * gw] = 1;
            // 3 outer, drawn toward the play area already (correct by design): tiles 0-1 the void, 3 cells up; the Right walls of tiles (1, 8..9)
            for (int ty = 0; ty < gh; ty++) for (int tx = 0; tx < 2; tx++) { gnd.Top[tx + ty * gw] = -1; gnd.SetHeight(tx, ty, -15f); Block(tx, ty); }
            gnd.Right[1 + 8 * gw] = 1; gnd.Right[1 + 9 * gw] = 1;
            // 4 ledge: tiles (2..14, 0..1) the void 3 cells DOWN; the Front walls of tiles (6..7, 1): the walkable side (north) is the high one
            for (int ty = 0; ty < 2; ty++) for (int tx = 2; tx < 15; tx++) { gnd.Top[tx + ty * gw] = -1; gnd.SetHeight(tx, ty, 15f); Block(tx, ty); }
            gnd.Front[6 + 1 * gw] = 1; gnd.Front[7 + 1 * gw] = 1;
            // 5 edge: tile (17, 15) drawn but blocked, far from any walkable cell, its Right wall onto the void
            gnd.Top[17 + 15 * gw] = 1; gnd.Right[17 + 15 * gw] = 1; gnd.SetHeight(18, 15, 0f); // (warch2) the void tile beside it lower: a wall with a height (a wall with none draws nothing: Walls.cs)
            // 6 a hole beside the walkable floor: tile (8, 8) without a top, blocked, a pit 3 cells down (warch2: its fill gets skirts)
            gnd.Top[8 + 8 * gw] = -1; Block(8, 8); gnd.SetHeight(8, 8, 15f);
            // 7 a black-tinted top under walkable cells: tile (10, 12), the tinted texture
            gnd.TopTexture[10 + 12 * gw] = 1;
            // 8 (warch2) a black-tinted strip under walkable cells along the map's north edge, tiles (3..14, 19): the map's border, not a
            //   pocket in the floor (mjolnir_04: the whole border ring), left as Loki's void
            for (int tx = 3; tx < 15; tx++) gnd.TopTexture[tx + 19 * gw] = 1;
            // 9 (warch2) a black-tinted top over walkable cells, 2 cells ABOVE their floor (the GAT stays at 0), tile (12, 14): drawn black
            //   it would float over the players' heads (K10: ama_dun01, cave), left as it is
            gnd.TopTexture[12 + 14 * gw] = 1; gnd.SetHeight(12, 14, -10f);
            // 10 (warch2) a walkable strip on the map's first row, cells (6..20, 0), joined to nothing (the ledge's void around it): within
            //    5 cells of the edge, no player gets there (rAthena map_search_freecell): not walkable for Architect (iz_dun00, pay_dun03)
            for (int x = 6; x <= 20; x++) gat.Set(x, 0, 0, 0);
            f.Add("data\\synthfix.gat", gat.Bytes()); f.Add("data\\synthfix.gnd", gnd.Bytes()); f.Add("data\\synthfix.rsw", rsw.Bytes());

            var s0 = Scene.Build(f, "synthfix", new Scene.Options { Rooms = false });
            Check("edge band: the walkable strip on the first row, joined to nothing further in, is not walkable; the floor is", s0.EdgeOnlyCells == 15 && !s0.Walkable(10, 0) && s0.Walkable(10, 10), $"{s0.EdgeOnlyCells}");
            var r0 = new MapReport { Map = "synthfix" }; var a0 = new Analysis(s0, new Grid(s0), r0); a0.RunStep1();
            var ws = a0.WallSet;
            Walls.Wall W(int tx, int ty, bool front) => ws.All.FirstOrDefault(w => w.Tx == tx && w.Ty == ty && w.Front == front);
            Check("walls: the raised tile's four walls are steps, wanted toward their low side", new[] { W(5, 4, true), W(4, 5, false) }.All(w => w != null && w.Cls == Walls.Class.Step && w.Want == Walls.Faces.TowardA)
                && new[] { W(5, 5, true), W(5, 5, false) }.All(w => w != null && w.Cls == Walls.Class.Step && w.Want == Walls.Faces.TowardB && !w.Problem),
                string.Join(" ", new[] { W(5, 4, true), W(5, 5, true), W(4, 5, false), W(5, 5, false) }.Select(w => w == null ? "null" : w.Cls + "/" + w.Want)));
            var i1 = W(12, 4, true);
            Check("walls: a wall whose two faces both show is interior, wanted from both sides", i1 != null && i1.Cls == Walls.Class.Interior && i1.Want == Walls.Faces.Both && i1.Problem, i1 == null ? "null" : $"{i1.Cls} {i1.Want}");
            var o1 = W(14, 8, false); var o2 = W(1, 8, false); var l1 = W(6, 1, true); var e1 = W(17, 15, false);
            Check("walls: a wall up to the void east of the floor is OUTER, drawn toward the void, wanted toward the floor", o1 != null && o1.Cls == Walls.Class.Outer && o1.Drawn == Walls.Faces.TowardB && o1.Want == Walls.Faces.TowardA && o1.Extra == Walls.Faces.TowardB, o1 == null ? "null" : $"{o1.Cls} {o1.Drawn} {o1.Want}");
            Check("walls: a wall up to the void west of the floor is OUTER and already right (see-through from the void)", o2 != null && o2.Cls == Walls.Class.Outer && o2.Want == o2.Drawn && !o2.Problem, o2 == null ? "null" : $"{o2.Cls} {o2.Drawn} {o2.Want}");
            Check("walls: a drop from the floor into the void is a LEDGE, wanted toward its low side (rule 'low')", l1 != null && l1.Cls == Walls.Class.Ledge && l1.Want == Walls.Faces.TowardA, l1 == null ? "null" : $"{l1.Cls} {l1.Want}");
            Check("walls: drawn scenery against the void, nobody near: EDGE, left as it is", e1 != null && e1.Cls == Walls.Class.Edge && e1.Want == e1.Drawn, e1 == null ? "null" : $"{e1.Cls} {e1.A} {e1.B}");
            Check("walls: the counter", ws.Count(Walls.Class.Step) == 4 && ws.Count(Walls.Class.Interior) == 1 && ws.Count(Walls.Class.Outer) == 4 && ws.Count(Walls.Class.Ledge) == 2 && ws.Count(Walls.Class.Edge) == 1,
                string.Join(", ", ws.Counter().Select(kv => kv.Key + " " + (kv.Value is Dictionary<string, object> d ? d["walls"] : kv.Value))));
            Check("D4: an outer wall correct by design is not a problem; the wrong one is", r0.Findings.Any(fd => fd.Kind == "back.gnd_wall" && fd.Region.Contains(29, 16)) && !r0.Findings.Any(fd => fd.Kind == "back.gnd_wall" && fd.Region.Contains(4, 16)),
                string.Join("; ", r0.Findings.Where(fd => fd.Kind == "back.gnd_wall").Select(fd => $"{fd.Region.Box[0]},{fd.Region.Box[1]}-{fd.Region.Box[2]},{fd.Region.Box[3]}")));
            var prev = Walls.LedgeRule;
            Walls.LedgeRule = "rule"; var lr = new Walls(s0).All.First(w => w.Tx == 6 && w.Ty == 1 && w.Front);
            Walls.LedgeRule = "keep"; var lk = new Walls(s0).All.First(w => w.Tx == 6 && w.Ty == 1 && w.Front);
            Walls.LedgeRule = prev;
            Check("walls: the ledge rule 'rule' wants the play side, 'keep' what is drawn", lr.Want == Walls.Faces.TowardB && lk.Want == lk.Drawn);

            // the fix
            byte[] gndB = f.Read("data\\synthfix.gnd"), gatB = f.Read("data\\synthfix.gat");
            var fix = Fixer.Generate(s0, a0, gndB, gatB, out var skipped);
            Check("fix: the interior wall both sides; the steps turned to their low side where the game draws the covered face, else untouched",
                fix.FrontMode(12 + 4 * gw) == MapFix.WallMode.Both && fix.FrontMode(5 + 4 * gw) == MapFix.WallMode.Flip && fix.RightMode(4 + 5 * gw) == MapFix.WallMode.Flip
                && fix.FrontMode(5 + 5 * gw) == MapFix.WallMode.Keep && fix.RightMode(5 + 5 * gw) == MapFix.WallMode.Keep);
            Check("fix: the outer wall drawn toward the void is turned; the right one is not touched", fix.RightMode(14 + 8 * gw) == MapFix.WallMode.Flip && fix.RightMode(1 + 8 * gw) == MapFix.WallMode.Keep);
            Check("fix: the ledge turned to its low side; the edge untouched", fix.FrontMode(6 + 1 * gw) == MapFix.WallMode.Flip && fix.RightMode(17 + 15 * gw) == MapFix.WallMode.Keep);
            Check("fix: the black-tinted top under walkable cells is drawn (a pocket in the drawn floor); the strip along the map's edge is not",
                fix.DrawTops.Count == 1 && fix.DrawTops.Contains(10 + 12 * gw) && skipped.Any(x => x.StartsWith("T: 1 black regions") && x.Contains("12 tiles")), string.Join(",", fix.DrawTops) + " | " + string.Join("; ", skipped));
            Check("fix: a black-tinted top floating 2 cells over the walkable floor is left as it is (K10)", !fix.DrawTops.Contains(12 + 14 * gw) && skipped.Any(x => x.StartsWith("T: 1 pockets left as they are (1 tiles)")), string.Join("; ", skipped));
            Check("fix: the pit beside the floor is filled at the lower of its own corners and the walkable floor (3 cells down)", fix.Fills.TryGetValue(8 + 8 * gw, out float fy) && Near(fy, -3f), string.Join(",", fix.Fills.Select(kv => kv.Key + ":" + kv.Value)));
            Check("fix: only the pit is filled; the open void beside the floor (the map's edge strips: not a pocket) stays Loki's sky",
                fix.Fills.Count == 1 && skipped.Any(x => x.StartsWith("F: ")), string.Join(",", fix.Fills.Keys.Select(t => (t % gw) + "/" + (t / gw))) + " | " + string.Join("; ", skipped));
            // the file: written, read back by the client's reader, applied only to the same files
            string text = fix.ToText(new[] { "selftest" });
            var back = MapFix.Parse(text, gw);
            Check("fix file: reads back as written", back.ToText(new[] { "selftest" }) == text && back.Count == fix.Count && back.Matches(gndB, gatB));
            var other = (byte[])gatB.Clone(); other[other.Length - 1] ^= 1;
            Check("fix file: another version of the map's files is refused", !back.Matches(gndB, other));
            bool broken = false; try { MapFix.Parse("architect-fix\t1\tsynthfix\tgnd:1:00\tgat:1:00\nW\t99\t0\tN\tboth\n", gw); } catch (FormatException) { broken = true; }
            Check("fix file: a tile outside the map is refused", broken);
            // the ground built with the fix, by the client's own builder (as World/MapLoader.cs builds it)
            var s1 = Scene.Build(f, "synthfix", new Scene.Options { Rooms = false, Fix = back });
            Check("builder: what it applied is what the file names", s1.FixBoth == 1 && s1.FixFlipped == 6 && s1.FixTops == 1 && s1.FixFills == back.Fills.Count && s1.FixSkirts > 0, $"{s1.FixBoth} {s1.FixFlipped} {s1.FixTops} {s1.FixFills} {s1.FixSkirts}");
            bool fwd = false, bck = false, east = false, west = false;
            for (int t = 0; t < s1.Count; t++)
            {
                if (s1.Kind[t] == TriKind.WallFront && s1.Src[t] == 12 + 4 * gw) { fwd |= s1.N[t * 3 + 2] > 0.5f; bck |= s1.N[t * 3 + 2] < -0.5f; }
                if (s1.Kind[t] == TriKind.WallRight && s1.Src[t] == 14 + 8 * gw) { east |= s1.N[t * 3] > 0.5f; west |= s1.N[t * 3] < -0.5f; }
            }
            Check("builder: an interior wall is drawn facing both ways; the outer one only toward the floor (west)", fwd && bck && west && !east, $"{fwd} {bck} {west} {east}");
            Check("builder: the tinted top and the fill are drawn", s1.TopDrawn[10 + 12 * gw] && s1.TopDrawn[8 + 8 * gw] && !s0.TopDrawn[10 + 12 * gw] && !s0.TopDrawn[8 + 8 * gw]);
            var r1 = new MapReport { Map = "synthfix" }; var a1 = new Analysis(s1, new Grid(s1), r1); a1.RunStep1();
            Check("after: no ground wall missing from a side players see", !r1.Findings.Any(fd => fd.Kind == "back.gnd_wall"), string.Join("; ", r1.Findings.Where(fd => fd.Kind == "back.gnd_wall").Select(fd => $"{fd.Region.At[0]},{fd.Region.At[1]}")));
            Check("after: the hole and the floor gap are gone", !r1.Findings.Any(fd => fd.Kind == "void.hole" && fd.Region.Contains(16, 16)) && !r1.Findings.Any(fd => fd.Kind == "fp.floor_gap" && fd.Region.Contains(20, 24))
                && r0.Findings.Any(fd => fd.Kind == "void.hole" && fd.Region.Contains(16, 16)) && r0.Findings.Any(fd => fd.Kind == "fp.floor_gap" && fd.Region.Contains(20, 24)));
            // a map with its own sky (mapsky.tsv): the void shows the designed sky, never a hole to fill
            var s2 = Scene.Build(f, "synthfix", new Scene.Options { Rooms = false, OwnSkyTable = "S\tsynthfix\t153,204,255\t-\t-\t2\n" });
            var r2 = new MapReport { Map = "synthfix" }; var a2 = new Analysis(s2, new Grid(s2), r2); a2.RunStep1();
            var fix2 = Fixer.Generate(s2, a2, gndB, gatB, out _);
            Check("own sky: holes are info and nothing is filled", s2.OwnSky != null && fix2.Fills.Count == 0 && r2.Findings.Where(fd => fd.Kind == "void.hole").All(fd => fd.Severity == "info") && r2.Findings.Any(fd => fd.Kind == "void.hole"));
            // the sections and the showcase's cameras
            var secs = Fixer.Sections(s0, a0, fix);
            Check("sections: ranked by surface, every change in one", secs.Count >= 2 && secs.Sum(x => x.Walls) == fix.Front.Count + fix.Right.Count && secs.Sum(x => x.Tops + x.Fills) == fix.DrawTops.Count + fix.Fills.Count && secs[0].Area >= secs[secs.Count - 1].Area,
                string.Join("; ", secs.Select(x => $"{x.Id}: {x.Walls} walls {x.Area:0}")));
            Check("showcase: two cameras per section", Fixer.Showcase(s0, secs).Split('\n').Count(l => l.StartsWith("section")) >= 2 * Math.Min(6, secs.Count));
        }

        // ------------------------------------------------------------------ warch2: the ray refinement and the catastrophic-mistake check
        // A raised block (tile 7, 7: 3 cells up, its top drawn, blocked) walled only on its south and west: open on its north and east, so
        // the face of its south wall that the rule calls covered (under the block's top) is seen from the walkable floor to the north,
        // under the top's open edge. The rule turns that wall (a step); the check finds the hole where players look; the refinement keeps
        // the game's face (both sides). And a hole (tile 3, 3) on a day-sky map is filled; on a dungeon-themed name it is not.
        private static void RefineMap()
        {
            const int gw = 15, gh = 15;
            var f = new MemoryFiles();
            var gat = new Synth.Gat(gw * 2, gh * 2); var gnd = new Synth.Gnd(gw, gh);
            f.Add(@"data\texture\synth\floor.bmp", Synth.Bmp(120, 100, 80));
            void Block(int tx, int ty) { for (int k = 0; k < 4; k++) gat.Set(tx * 2 + (k & 1), ty * 2 + (k >> 1), 1, 0); }
            gnd.SetHeight(7, 7, -15f); Block(7, 7); gnd.Front[7 + 6 * gw] = 1; gnd.Right[6 + 7 * gw] = 1;
            gnd.Top[3 + 3 * gw] = -1; Block(3, 3);
            // an outer wall the game draws toward the void: the E edge of tile (10, 10), a void tile (no top, blocked) 2.6 cells up at its
            // north end and level with the floor at its south end: the quad's first triangle has no area, the game's facing test sees
            // only the second, and the wall faces west, into the void (ein_dun01, ra_san02: K8)
            for (int ty = 9; ty <= 11; ty++) for (int tx = 7; tx <= 10; tx++) { Block(tx, ty); gnd.Top[tx + ty * gw] = -1; gnd.SetHeight(tx, ty, -13f); }
            gnd.Right[10 + 10 * gw] = 1; gnd.H[(10 + 10 * gw) * 4 + 1] = 0f;
            foreach (var name in new[] { "synthref", "synthref_dun" })
            {
                var rsw = new Synth.Rsw { Gnd = name + ".gnd", Gat = name + ".gat" };
                f.Add("data\\" + name + ".gat", gat.Bytes()); f.Add("data\\" + name + ".gnd", gnd.Bytes()); f.Add("data\\" + name + ".rsw", rsw.Bytes());
            }
            var s0 = Scene.Build(f, "synthref", new Scene.Options { Rooms = true });
            var r0 = new MapReport { Map = "synthref" }; var a0 = new Analysis(s0, new Grid(s0), r0); a0.RunStep1();
            byte[] gndB = f.Read(@"data\synthref.gnd"), gatB = f.Read(@"data\synthref.gat");
            var rule = Fixer.Generate(s0, a0, gndB, gatB, out _);
            var sw = a0.WallSet.All.FirstOrDefault(w => w.Tx == 7 && w.Ty == 6 && w.Front);
            Check("refine: the rule calls the open block's south wall a step and turns it", sw != null && sw.Cls == Walls.Class.Step && rule.FrontMode(7 + 6 * gw) == MapFix.WallMode.Flip, sw == null ? "null" : $"{sw.Cls} {rule.FrontMode(7 + 6 * gw)}");
            var s1 = Scene.Build(f, "synthref", new Scene.Options { Rooms = true, Fix = rule });
            var r1 = new MapReport { Map = "synthref" }; var a1 = new Analysis(s1, new Grid(s1), r1); a1.RunStep1();
            var c1 = FixCheck.Run(s0, a0, r0, s1, a1, r1, rule);
            Check("check: the rule's turn leaves a see-through hole where players look (K2), nothing else breaks", c1.Regressions + c1.TableOnlyHoles > 0 && c1.LostTops == 0 && c1.ZFight == 0 && c1.OuterTowardVoid == 0 && c1.DoubledFloating == 0, c1.Line());
            var fix = Fixer.Generate(s0, a0, gndB, gatB, out _); var stats = new Fixer.RefineStats();
            for (int round = 0; round < 8; round++) if (!Fixer.Refine(s0, a0, Scene.Build(f, "synthref", new Scene.Options { Rooms = true, Fix = fix }), fix, stats, round)) break;
            Check("refine: the game's face is kept, the low side's added (both sides)", fix.FrontMode(7 + 6 * gw) == MapFix.WallMode.Both, $"{fix.FrontMode(7 + 6 * gw)}; {stats.Rounds} rounds");
            var s2 = Scene.Build(f, "synthref", new Scene.Options { Rooms = true, Fix = fix });
            var r2 = new MapReport { Map = "synthref" }; var a2 = new Analysis(s2, new Grid(s2), r2); a2.RunStep1();
            var c2 = FixCheck.Run(s0, a0, r0, s2, a2, r2, fix);
            Check("check: after the refinement nothing fails", !c2.Fail && c2.Regressions == 0 && c2.TableOnlyHoles == 0, c2.Line());
            Check("check: K9 counts the face missing from the walkable side before, none after", c2.InnerBefore > 0 && c2.InnerAfter == 0, c2.Line());
            var ow = a0.WallSet.All.FirstOrDefault(w => w.Tx == 10 && w.Ty == 10 && !w.Front);
            Check("front: the wall the game draws toward the void is OUTER, drawn west, wanted east; the fix turns it with 'front'",
                ow != null && ow.Cls == Walls.Class.Outer && ow.Drawn == Walls.Faces.TowardA && ow.Want == Walls.Faces.TowardB && rule.RightMode(10 + 10 * gw) == MapFix.WallMode.Front,
                ow == null ? "null" : $"{ow.Cls} drawn {ow.Drawn} want {ow.Want} mode {rule.RightMode(10 + 10 * gw)}");
            bool east = false, west = false;
            for (int t = 0; t < s1.Count; t++) if (s1.Kind[t] == TriKind.WallRight && s1.Src[t] == 10 + 10 * gw) { east |= s1.N[t * 3] > 0.5f; west |= s1.N[t * 3] < -0.5f; }
            Check("front: the client's builder draws it facing east only (the play area), and the file reads back", east && !west && MapFix.Parse(rule.ToText(), gw).RightMode(10 + 10 * gw) == MapFix.WallMode.Front, $"east {east} west {west}");
            Check("fills: the hole is filled on a day-sky map", s0.SkyTheme == "day" && rule.Fills.ContainsKey(3 + 3 * gw), $"{s0.SkyTheme} {rule.Fills.Count}");
            // K10 (warch2): a black floor beside the raised block (tile 7, 8: the open north side) gets a skirt up to the block's top, 3
            // cells, across the eye of the walkable floor there: a black wall in front of the player's face (dicastes01, mjolnir_04)
            var bad = MapFix.Parse(fix.ToText(), gw); bad.Fills[7 + 8 * gw] = 0f; bad.Why[7 + 8 * gw] = "hole";
            var s3 = Scene.Build(f, "synthref", new Scene.Options { Rooms = true, Fix = bad });
            var r3 = new MapReport { Map = "synthref" }; var a3 = new Analysis(s3, new Grid(s3), r3); a3.RunStep1();
            var c3 = FixCheck.Run(s0, a0, r0, s3, a3, r3, bad);
            Check("check: K10 finds the black skirt standing across the eye (FAIL); the refined fix has none", c3.BlackWalls > 0 && c3.Fail && c2.BlackWalls == 0, c3.Line());
            var sd = Scene.Build(f, "synthref_dun", new Scene.Options { Rooms = true });
            var rd = new MapReport { Map = "synthref_dun" }; var ad = new Analysis(sd, new Grid(sd), rd); ad.RunStep1();
            var fd = Fixer.Generate(sd, ad, f.Read(@"data\synthref_dun.gnd"), f.Read(@"data\synthref_dun.gat"), out _);
            Check("fills: not on a dungeon-themed map (it shows near-black there already)", sd.SkyTheme == "dungeon" && fd.Fills.Count == 0, $"{sd.SkyTheme} {fd.Fills.Count}");
        }

        // ------------------------------------------------------------------ the indoor test map (two rooms, 20 x 20 cells)
        private static void IndoorMap()
        {
            var f = new MemoryFiles();
            int gw = 10, gh = 10;
            var gat = new Synth.Gat(gw * 2, gh * 2); var gnd = new Synth.Gnd(gw, gh); var rsw = new Synth.Rsw();
            for (int y = 0; y < 20; y++) for (int x = 0; x < 20; x++) gat.Set(x, y, (x >= 1 && x <= 8 || x >= 12 && x <= 18) && y >= 1 && y <= 18 ? 0 : 1, 0);
            rsw.Sounds.Add(("room1.wav", Synth.Rsw.RoAt(7.5f, 0, 10.5f, gw, gh), 30f)); // 6 cells: reaches room 2 (x 12-13)
            f.Add("data\\synthin.gat", gat.Bytes()); f.Add("data\\synthin.gnd", gnd.Bytes());
            rsw.Gnd = "synthin.gnd"; rsw.Gat = "synthin.gat"; f.Add("data\\synthin.rsw", rsw.Bytes());
            f.Add("data\\indoorrswtable.txt", System.Text.Encoding.ASCII.GetBytes("// test\r\nsynthin.rsw#\r\n"));
            var s = Scene.Build(f, "synthin", new Scene.Options { Rooms = true });
            var g = new Grid(s); var r = new MapReport { Map = "synthin" };
            var an = new Analysis(s, g, r); an.RunStep1(); an.RunSurvey();
            Check("indoor by the client's list", s.Indoor == "table", s.Indoor);
            Check("I3 the open rooms leak the view (the room mask culls the other room)", r.Findings.Any(fd => fd.Kind == "room.view_leak"));
            Check("I3 replaces D2 indoors", !r.Findings.Any(fd => fd.Kind == "void.edge_seen"));
            Check("RoomTools re-run: two rooms", s.Rooms != null && s.Rooms.Labels != null && s.Rooms.Rooms.Count == 2, s.Rooms == null ? "null" : s.Rooms.Rooms.Count.ToString());
            Check("I1 no ceiling: medium", r.Findings.Any(fd => fd.Kind == "room.open_ceiling" && fd.Severity == "medium"));
            Check("I2 walls below the eye: medium", r.Findings.Any(fd => fd.Kind == "room.low_walls" && fd.Severity == "medium"));
            Check("H2 the sound reaches the other room", r.Findings.Any(fd => fd.Kind == "sound.through_rooms" && fd.Region.Cells().Any(c => c[0] == 12)));
            Check("H2 not in its own room", !r.Findings.Any(fd => fd.Kind == "sound.through_rooms" && fd.Region.Cells().Any(c => c[0] <= 8)));
            Check("C1 the empty strip between the rooms", r.Findings.Any(fd => fd.Kind == "walk.invisible_wall" && fd.Region.Cells().Any(c => c[0] == 9)));
        }
    }
}
