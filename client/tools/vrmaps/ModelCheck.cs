using System;
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
    /// Wave 27 (wmodel): Architect's catastrophic-mistake check for the fix kind M (model faces drawn from both sides), run by
    /// `architect models` on every map it fixes, on the map as the game draws it (with its ground fix as shipped) and with the M lines
    /// (built by the client's own builder from the fix file's text: RsmMeshBuilder.BuildBack through Scene.BuildModels, as
    /// World/MapLoader.cs AddBackFaces applies it). Every added face is traced with rays (Grid.Ray: the renderer's rules).
    ///   M1 floating / out of the play: an added patch no player view (first person, Follow) sees on the fixed map: FAIL.
    ///   M2 inside-out: an added face whose drawn side is not the reverse of its game face, or whose lighting normal (the builder's,
    ///      carried by the instance as Unity carries normals) points away from its drawn side: FAIL.
    ///   M3 z-fighting: an added face with another drawn surface within 0.03 cells facing the same way at two or more of four points: FAIL.
    ///   M4 blocking: (a) an added face standing in a walkable passage at body height (between two adjacent walkable cells, or across a
    ///      walkable cell's body): FAIL; (b) walkable cells whose first-person eye is boxed in by added faces (the ray up and 7 of 8 level
    ///      rays meet an added face first, within 2.5 cells: the player inside a solid): FAIL.
    ///   M5 the outer-wall rule: an added face whose shown side is the void (see-through from the void by design): FAIL.
    ///   M6 the file as the client applies it: the builder adds exactly the faces the file names, on the instances it names: FAIL else.
    ///   M7 facts: model faces seen from behind by the survey before -> after (back.model_face), witnessed patches left as they are and
    ///      why (the void behind, in the way, z-fighting); LOOK when some witnessed faces are left see-through as being in the way.
    ///   M8 cost: back copies added, against the map's drawn model triangles.
    /// The black scan (blackscan.py on the in-game pictures) is the in-game half of the black-scan checks.
    /// </summary>
    public static class ModelCheck
    {
        public sealed class Result
        {
            public string Map;
            public int AddedPatches, AddedTris, Floating, InsideOut, NormalsAgree, NormalsDisagree, NormalsLoose, ZFight, InTheWay, BoxedEyes, BoxedSamples, Outer;
            public int FileFaces, BuiltFaces, BuiltInstances, BuiltSkipped, FileInstances;
            public int SeenTableToo; public int LeftVoid, LeftInTheWay, LeftCoplanar, Pruned;
            public int ModelTris; public string BackBefore = "", BackAfter = "";
            public readonly List<string> Examples = new List<string>();
            public bool Fail => Floating > 0 || InsideOut > 0 || NormalsDisagree > 0 || ZFight > 0 || InTheWay > 0 || BoxedEyes > 0 || Outer > 0 || BuiltFaces != FileFaces || BuiltSkipped > 0 || BuiltInstances != FileInstances;
            public bool Look => LeftInTheWay > 0;
            public string Verdict => Fail ? "FAIL" : Look ? "LOOK" : "OK";
            public string Line() =>
                $"MCHECK {Map}: {Verdict}: added {AddedTris} faces in {AddedPatches} patches ({(ModelTris > 0 ? AddedTris * 100.0 / ModelTris : 0):0.0} % of {ModelTris} model triangles); M1 not seen by a player {Floating}; " +
                $"M2 inside-out {InsideOut}, lighting normals agree {NormalsAgree} / disagree {NormalsDisagree} / loose {NormalsLoose}; M3 z-fighting {ZFight}; M4 in the way {InTheWay}, boxed-in eyes {BoxedEyes} of {BoxedSamples}; " +
                $"M5 shown toward the void {Outer}; M6 the file {FileFaces} faces / {FileInstances} models, built {BuiltFaces} / {BuiltInstances} (not applied {BuiltSkipped}); " +
                $"M7 back.model_face {BackBefore} -> {BackAfter}, left: void {LeftVoid}, in the way {LeftInTheWay}, z-fight {LeftCoplanar}, pruned {Pruned}";
        }

        public static Result Run(Scene s0, Scene s1, MapFix fix, List<ModelFixer.Patch> patches, ModelFixer.Stats st, MapReport before, MapReport after)
        {
            var r = new Result { Map = s0.Map, ModelTris = st.ModelTris, LeftVoid = st.ExVoid, LeftInTheWay = st.ExInTheWay, LeftCoplanar = st.ExCoplanar, Pruned = st.PrunedTris };
            var g1 = new Grid(s1);
            var map1 = ModelFixer.Map(s0, s1);
            // the added patches as the fixed scene holds them (the file's faces: patches keep only the faces the file names)
            var added = new List<ModelFixer.Patch>();
            foreach (var p in patches)
            {
                if (!fix.Models.TryGetValue(p.Inst, out var mf)) continue;
                var q = ModelFixer.Translate(s0, p, map1); q.Added.Clear();
                foreach (var t in p.Tris) if (mf.Has(s0.Node[t], s0.Face[t]) && map1.TryGetValue((s0.Src[t], s0.Node[t], s0.Face[t], s0.Side[t]), out int u)) q.Added.Add(u);
                if (q.Added.Count > 0) added.Add(q);
            }
            r.AddedPatches = added.Count; r.AddedTris = added.Sum(p => p.Added.Count);
            // ---- M6 the file as applied
            r.FileFaces = fix.ModelFaceCount; r.FileInstances = fix.Models.Count;
            r.BuiltFaces = s1.FixModelFaces; r.BuiltInstances = s1.FixModelInstances; r.BuiltSkipped = s1.FixModelSkipped;
            if (r.BuiltFaces != r.FileFaces || r.BuiltSkipped > 0 || !s1.FixRswMatches && fix.Models.Count > 0) r.Examples.Add($"M6 THE FILE IS NOT APPLIED AS WRITTEN: file {r.FileFaces} faces on {r.FileInstances} models, built {r.BuiltFaces} on {r.BuiltInstances}, not applied {r.BuiltSkipped}, RSW matches {s1.FixRswMatches}");
            // ---- M1 a player view sees every added patch's back on the fixed map
            var solid = new System.Collections.Concurrent.ConcurrentDictionary<(int, int), bool>();
            var lv = new int[added.Count]; var other = new int[added.Count];
            Parallel.For(0, added.Count, () => new Grid.RayState(s1.Count), (i, ls, rs) =>
            {
                long n = 0; var q = added[i]; var sample = ModelFixer.Spread(s1, q.Added);
                lv[i] = ModelFixer.Witness(s1, g1, rs, q, sample, ref n, solid, out _, out _, out _);
                other[i] = ModelFixer.OtherViews(s1, g1, rs, q, sample.Take(2).ToList(), ref n);
                return rs;
            }, _ => { });
            for (int i = 0; i < added.Count; i++)
            {
                if (lv[i] < FixCheck.SeenFollow) { r.Floating++; if (r.Examples.Count < 20) r.Examples.Add($"M1 NOT SEEN BY A PLAYER: {s1.Owner(added[i].Added[0])} node {added[i].Node}, {added[i].Added.Count} faces at {added[i].C} (best view: {FixCheck.LevelName[Math.Max(lv[i], other[i])]})"); }
                if (other[i] == FixCheck.SeenTable) r.SeenTableToo++;
            }
            // ---- M2 inside-out
            r.NormalsAgree = s1.AddedNormalsAgree; r.NormalsDisagree = s1.AddedNormalsDisagree; r.NormalsLoose = s1.AddedNormalsLoose;
            var src = new Dictionary<(int, int, int), int>();
            for (int t = 0; t < s1.Count; t++) if (s1.Kind[t] == TriKind.Model && s1.Side[t] == Scene.SideSingle) src[(s1.Src[t], s1.Node[t], s1.Face[t])] = t;
            var addedTris = new List<int>(); for (int t = 0; t < s1.Count; t++) if (s1.Kind[t] == TriKind.Model && s1.Side[t] == Scene.SideAdded) addedTris.Add(t);
            foreach (var t in addedTris)
            {
                // the back copy is its game face with corners 1 and 2 swapped (RsmMeshBuilder.Fill: the reversed winding)
                bool ok = src.TryGetValue((s1.Src[t], s1.Node[t], s1.Face[t]), out int o) && Vector3.Dot(s1.Normal(t), s1.Normal(o)) < -0.99f
                          && (s1.V(t, 0) - s1.V(o, 0)).sqrMagnitude + (s1.V(t, 1) - s1.V(o, 2)).sqrMagnitude + (s1.V(t, 2) - s1.V(o, 1)).sqrMagnitude < 1e-8f;
                if (!ok) { r.InsideOut++; if (r.Examples.Count < 30) r.Examples.Add($"M2 INSIDE-OUT: {s1.Owner(t)} face {s1.Node[t]}:{s1.Face[t]} is not its game face reversed"); }
            }
            if (r.NormalsDisagree > 0) r.Examples.Add($"M2 INSIDE-OUT LIGHTING: {r.NormalsDisagree} added faces lit from the other side");
            // ---- M3 z-fighting, M4a in the way, M5 the void (on the fixed map, every added face)
            var zf = new bool[addedTris.Count]; var zo = new int[addedTris.Count]; var way = new bool[addedTris.Count]; var outer = new bool[addedTris.Count];
            Parallel.For(0, addedTris.Count, () => new Grid.RayState(s1.Count), (i, ls, rs) =>
            {
                int t = addedTris[i]; if (!src.TryGetValue((s1.Src[t], s1.Node[t], s1.Face[t]), out int o)) o = t;
                // z-fighting: another surface facing the way the added copy faces (= its game face's back), not the copy's own game face
                zf[i] = ModelFixer.Coplanar(s1, g1, o, u => u == t || u == o, rs, out zo[i]);
                way[i] = ModelFixer.InTheWay(s1, t);
                outer[i] = ModelFixer.BackIsVoid(s1, o);
                return rs;
            }, _ => { });
            for (int i = 0; i < addedTris.Count; i++)
            {
                int t = addedTris[i];
                if (zf[i]) { r.ZFight++; int u = zo[i]; if (r.Examples.Count < 40) r.Examples.Add($"M3 Z-FIGHT: {s1.Owner(t)} face {s1.Node[t]}:{s1.Face[t]} at {ModelFixer.Centre(s1, t)} with {(u >= 0 ? s1.Owner(u) + (s1.Kind[u] == TriKind.Model ? $" face {s1.Node[u]}:{s1.Face[u]} side {s1.Side[u]}" : "") : "?")}"); }
                if (way[i]) { r.InTheWay++; if (r.Examples.Count < 50) r.Examples.Add($"M4 IN THE WAY: {s1.Owner(t)} face {s1.Node[t]}:{s1.Face[t]} at {ModelFixer.Centre(s1, t)}"); }
                if (outer[i]) { r.Outer++; if (r.Examples.Count < 60) r.Examples.Add($"M5 SHOWN TOWARD THE VOID: {s1.Owner(t)} face {s1.Node[t]}:{s1.Face[t]} at {ModelFixer.Centre(s1, t)}"); }
            }
            // ---- M4b boxed-in eyes: a lattice of walkable cells (as the survey's), 8 level rays from the eye
            if (addedTris.Count > 0)
            {
                var isAdded = new HashSet<int>(addedTris);
                var walk = Enumerable.Range(0, s1.W * s1.H).Where(i => s1.Walk[i]).ToList();
                int step = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(walk.Count / 6000.0)));
                var cells = walk.Where(i => (i % s1.W) % step == 0 && (i / s1.W) % step == 0).ToList();
                r.BoxedSamples = cells.Count; int boxed = 0; var boxedAt = new System.Collections.Concurrent.ConcurrentBag<int>();
                Parallel.For(0, cells.Count, () => new Grid.RayState(s1.Count), (k, ls, rs) =>
                {
                    int i = cells[k], x = i % s1.W, y = i / s1.W; var eye = new Vector3(x + 0.5f, s1.Floor[i] + Views.EyeCells, y + 0.5f);
                    // inside a solid made of added faces: the ray up and 7 of 8 level rays meet an added face first, all within 2.5 cells (a
                    // corridor is open at its ends; a room is wider)
                    bool Hit(Vector3 d) { var h = g1.Ray(rs, eye, d, 0f, 2.5f, t => s1.Kind[t] != TriKind.Water); return h.Tri >= 0 && isAdded.Contains(h.Tri); }
                    int n = 0;
                    if (Hit(Vector3.up)) for (int a = 0; a < 8; a++) if (Hit(new Vector3((float)Math.Cos(a * Math.PI / 4), 0f, (float)Math.Sin(a * Math.PI / 4)))) n++;
                    if (n >= 7) { System.Threading.Interlocked.Increment(ref boxed); boxedAt.Add(i); }
                    return rs;
                }, _ => { });
                r.BoxedEyes = boxed;
                foreach (var i in boxedAt.Take(5)) r.Examples.Add($"M4 BOXED IN BY ADDED FACES: the eye over cell {i % s1.W},{i / s1.W}");
            }
            // ---- M7 the survey's model faces seen from behind, before -> after
            r.BackBefore = BackFaces(before); r.BackAfter = BackFaces(after);
            return r;
        }

        private static string BackFaces(MapReport rep)
        {
            if (rep == null) return "-";
            var fs = rep.Findings.Where(f => f.Kind == "back.model_face").ToList();
            object m = rep.Metrics.TryGetValue("survey", out var sv) && sv is Dictionary<string, object> d && d.TryGetValue("modelsSeenFromBehind", out var v) ? v : null;
            return $"{fs.Count} listed ({fs.Count(f => f.Severity == "medium")} medium), {m ?? "?"} models";
        }

        public static string Markdown(Result r)
        {
            var sb = new StringBuilder();
            sb.Append($"# {r.Map}: Architect's check of the model faces (kind M): {r.Verdict}\n\n");
            sb.Append("Made by `architect models` (client/tools/vrmaps/ModelCheck.cs) on the map as the game draws it (with its ground fix) and with the M lines, built by the client's own builder (RsmMeshBuilder.BuildBack); every added face traced with rays.\n\n");
            sb.Append("| check | result |\n|---|---|\n");
            sb.Append($"| M1 added patches no player view (first person, Follow) sees on the fixed map | **{r.Floating}** of {r.AddedPatches} (also seen from the Table: {r.SeenTableToo}) |\n");
            sb.Append($"| M2 added faces not the reverse of their game face; lighting normals pointing away from the drawn side | **{r.InsideOut}**; normals agree {r.NormalsAgree}, disagree **{r.NormalsDisagree}**, loose (smoothing groups) {r.NormalsLoose} |\n");
            sb.Append($"| M3 added faces z-fighting (another surface within 0.03 cells facing the same way) | **{r.ZFight}** of {r.AddedTris} |\n");
            sb.Append($"| M4 added faces in a walkable passage at body height; eyes boxed in by added faces | **{r.InTheWay}**; **{r.BoxedEyes}** of {r.BoxedSamples} eyes |\n");
            sb.Append($"| M5 added faces whose shown side is the void (the design's outer-wall rule) | **{r.Outer}** |\n");
            sb.Append($"| M6 the file as the client applies it | file {r.FileFaces} faces on {r.FileInstances} models; built {r.BuiltFaces} on {r.BuiltInstances}; not applied **{r.BuiltSkipped}** |\n");
            sb.Append($"| M7 back.model_face (the survey) before -> after; witnessed faces left as the game draws them | {r.BackBefore} -> {r.BackAfter}; the void behind {r.LeftVoid}, in the way {r.LeftInTheWay}, z-fighting {r.LeftCoplanar}; pruned (hidden by other added faces) {r.Pruned} |\n");
            sb.Append($"| M8 cost | {r.AddedTris} back copies = {(r.ModelTris > 0 ? r.AddedTris * 100.0 / r.ModelTris : 0):0.0} % of the map's {r.ModelTris} drawn model triangles |\n");
            if (r.Examples.Count > 0) { sb.Append("\nExamples:\n\n"); foreach (var e in r.Examples) sb.Append("- ").Append(e).Append('\n'); }
            return sb.ToString();
        }
    }
}
