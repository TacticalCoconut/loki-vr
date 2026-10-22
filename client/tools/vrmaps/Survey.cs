using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using T = Loki.VrMaps.Views.Thresholds;

namespace Loki.VrMaps
{
    /// <summary>
    /// The eye survey (plan build step 2): from first-person eyes over a lattice of walkable cells, 192 rays over the whole sphere,
    /// each traced by the renderer's rules (Grid.Ray: a single-sided triangle seen from behind is culled and passed, water passed, on an
    /// indoor map only the eye's room is drawn). What the rays meet gives:
    ///   D2 void.edge_seen     a ray below the horizon that meets nothing: the end of the drawn world (or, indoors, room.view_leak);
    ///                         its distance = where it would have met a floor at the eye's own floor height
    ///   D3 back.model_face    a ray that passes a model's single-sided face from behind before what it shows (see-through)
    ///   D4 (survey)           the same through a ground wall (counted; the per-wall finding is GroundWallSides)
    ///   D5 under.exposed      the first surface met is a model's underside above the eye (facing down more than 40 degrees)
    ///   K1 view.popin         level sight lines longer than the server's view range (area_size + 1)
    ///   F4 fx.popin           a map effect in sight farther than the 25 cells within which Loki starts it
    ///   J3 follow.head_clip   the Follow view's eye (its default ring round the character) within 0.25 m of drawn geometry
    /// </summary>
    public sealed partial class Analysis
    {
        public const int SurveyRays = 192, MaxSamples = 3000;
        /// <summary>J3: the Follow view's eye within this of drawn geometry (FLAGGED).</summary>
        public const float FollowHeadMetres = 0.15f;
        private int _step = 1;
        /// <summary>A survey sample stands for its lattice block: every walkable cell of [x, x + step) x [y, y + step).</summary>
        private void Block(CellSet set, int cell, int sev, float val)
        {
            int x0 = cell % S.W, y0 = cell / S.W;
            for (int y = y0; y < Math.Min(S.H, y0 + _step); y++) for (int x = x0; x < Math.Min(S.W, x0 + _step); x++) if (S.Walk[x + y * S.W]) set.Set(x, y, sev, val);
        }
        private IEnumerable<int> BlockCells(IEnumerable<int> cells)
        {
            foreach (var c in cells) { int x0 = c % S.W, y0 = c / S.W; for (int y = y0; y < Math.Min(S.H, y0 + _step); y++) for (int x = x0; x < Math.Min(S.W, x0 + _step); x++) if (S.Walk[x + y * S.W]) yield return x + y * S.W; }
        }
        private static Vector3[] _dirs;
        public static Vector3[] SurveyDirections => Directions();
        private static Vector3[] Directions()
        {
            if (_dirs != null) return _dirs;
            var d = new Vector3[SurveyRays]; double golden = Math.PI * (3 - Math.Sqrt(5));
            for (int i = 0; i < SurveyRays; i++)
            {
                double y = 1 - (i + 0.5) * 2.0 / SurveyRays, r = Math.Sqrt(1 - y * y), th = golden * i;
                d[i] = new Vector3((float)(Math.Cos(th) * r), (float)y, (float)(Math.Sin(th) * r));
            }
            return _dirs = d;
        }

        private sealed class Sample
        {
            public int Cell; public int Void, Sky, Level, LongLevel; public float NearestVoid = float.MaxValue;
            public readonly Dictionary<int, (float d, int tri)> BackModels = new Dictionary<int, (float, int)>();   // instance -> nearest distance seen from behind, and that face
            public readonly Dictionary<int, int> Under = new Dictionary<int, int>();             // instance -> rays
            public int BackGroundWalls;
            public int FollowNear, FollowRing, FollowBlocked;
        }

        /// <summary>
        /// the design's outer-wall rule on one model face (a triangle): what lies right in front of it and right behind it, straight out along
        /// its normal (0.5 to <see cref="Walls.PlayCells"/> cells, the 2-D cell under each point): "void" when no cell there is walkable
        /// or has drawn ground. "outer": the back side is the void (see-through from the void: by design); "outer-wrong": the front side is
        /// the void and the back is not (drawn toward the void); "interior": neither side is the void.
        /// </summary>
        public string FaceClass(int tri)
        {
            if (tri < 0) return "interior";
            var c = (S.V(tri, 0) + S.V(tri, 1) + S.V(tri, 2)) / 3f; var n = S.Normal(tri);
            bool Void(float sign)
            {
                foreach (float d in new[] { 0.5f, 1f, 2f, 3f, (float)Walls.PlayCells })
                {
                    var p = c + n * (d * sign); int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.z);
                    if (!S.InMap(x, y)) continue;
                    if (S.Walk[x + y * S.W] || S.TopDrawn[(x / 2) + (y / 2) * S.Gnd.Width]) return false;
                }
                return true;
            }
            bool front = Void(1f), back = Void(-1f);
            return back ? "outer" : front ? "outer-wrong" : "interior";
        }

        public void RunSurvey()
        {
            var m = R.M("survey"); m["built"] = true;
            int W = S.W, H = S.H;
            var walk = Enumerable.Range(0, W * H).Where(i => S.Walk[i]).ToList();
            if (walk.Count == 0) return;
            int step = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(walk.Count / (double)MaxSamples)));
            var cells = walk.Where(i => (i % W) % step == 0 && (i / W) % step == 0).ToList();
            if (cells.Count == 0) cells.Add(walk[0]);
            m["samples"] = cells.Count; m["latticeStep"] = step; m["raysPerSample"] = SurveyRays;
            var dirs = Directions();
            float maxT = (float)Math.Sqrt(W * W + H * H) + 20f;
            float eye = Views.EyeCells; int area = Views.ServerAreaSize + 1;
            Views.FollowEye(out float fBack, out float fUp); float followR = FollowHeadMetres / Views.FollowMetresPerCell;
            _step = step;
            var samples = new Sample[cells.Count];
            Parallel.For(0, cells.Count, () => new Grid.RayState(S.Count), (k, loopState, st) =>
            {
                int i = cells[k]; int x = i % W, y = i / W; float F = S.Floor[i];
                var s = new Sample { Cell = i }; samples[k] = s;
                var o = new Vector3(x + 0.5f, F + eye, y + 0.5f);
                var vis = RoomVisible(RoomAt(x, y));
                foreach (var d in dirs)
                {
                    var h = G.Ray(st, o, d, 0f, maxT, Drawn, vis);
                    if (h.BackTri >= 0)
                    {
                        var kb = S.Kind[h.BackTri];
                        if (kb == TriKind.Model) { int inst = S.Src[h.BackTri]; if (!s.BackModels.TryGetValue(inst, out var bd) || h.BackT < bd.d) s.BackModels[inst] = (h.BackT, h.BackTri); }
                        else if (kb == TriKind.WallFront || kb == TriKind.WallRight) s.BackGroundWalls++;
                    }
                    bool level = Math.Abs(d.y) < 0.25f;
                    if (level) s.Level++;
                    if (h.Tri < 0)
                    {
                        if (d.y < -0.02f) { s.Void++; float hd = eye / -d.y * (float)Math.Sqrt(d.x * d.x + d.z * d.z); if (hd < s.NearestVoid) s.NearestVoid = hd; }
                        else s.Sky++;
                        if (level) s.LongLevel++;
                        continue;
                    }
                    if (level && h.T * (float)Math.Sqrt(d.x * d.x + d.z * d.z) > area) s.LongLevel++;
                    if (S.Kind[h.Tri] == TriKind.Model && S.N[h.Tri * 3 + 1] < -0.64f && o.y + d.y * h.T > o.y)
                    {
                        int inst = S.Src[h.Tri]; s.Under.TryGetValue(inst, out int c); s.Under[inst] = c + 1;
                    }
                }
                // J3 / J4: the Follow eye on its default ring (16 headings), near geometry; and the line to the character's chest
                var chest = new Vector3(x + 0.5f, F + Views.CutChestCells, y + 0.5f);
                for (int a = 0; a < 16; a++)
                {
                    double th = a * Math.PI / 8;
                    var e = new Vector3(x + 0.5f + (float)Math.Cos(th) * fBack, F + fUp, y + 0.5f + (float)Math.Sin(th) * fBack);
                    s.FollowRing++;
                    if (G.Nearest(e, followR, out int tri, Solid) < followR && tri >= 0) s.FollowNear++;
                    var dd = chest - e; float len = dd.magnitude;
                    var hb = G.Ray(st, e, dd / len, 0f, len - 0.05f, Drawn, vis);
                    if (hb.Tri >= 0) s.FollowBlocked++;
                }
                return st;
            }, _ => { });

            bool indoor = S.Rooms?.Labels != null;
            // ---- D2 void.edge_seen (indoor: room.view_leak)
            var voidSet = new CellSet(W, H); var leakSet = new CellSet(W, H);
            int seeVoid = 0; float nearestVoid = float.MaxValue;
            foreach (var s in samples)
            {
                if (s.Void == 0) continue; seeVoid++; nearestVoid = Math.Min(nearestVoid, s.NearestVoid);
                int sev = s.NearestVoid <= 10f ? 1 : 2;
                if (indoor) Block(leakSet, s.Cell, sev, s.Void / (float)SurveyRays); else Block(voidSet, s.Cell, sev, s.NearestVoid);
            }
            Emit("void.edge_seen", voidSet, new[] { "firstPerson" }, true, "cells to the nearest void", 10f,
                (sv, t) => "the end of the drawn world is in sight below the horizon (Loki's sky shows where RO cleared to black): a skirt / backdrop at the edge, a darker sky below the horizon" + (S.HasFogEntry ? $", or fog (this map's far fog: {S.FogFar * Views.FogCellsPerUnit:0} cells)" : " (this map has no fog)"),
                $"survey samples (every {step}th walkable cell) whose eye sees, below the horizon, a ray meet nothing; value = the distance at which that ray would have met a floor at the sample's height", metresFp: true);
            Emit("room.view_leak", leakSet, new[] { "firstPerson" }, false, "share of rays", 0.05f,
                (sv, t) => "from inside the room the eye sees out of it into nothing (the other rooms are not drawn; RO's interiors stop at the camera's view): close the room in first person (walls / a dark backdrop at its edge)",
                "survey samples on an indoor map whose rays leave the room's drawn cells without meeting anything below the horizon; value = the share of rays", metresFp: false);
            m["samplesSeeingVoid"] = seeVoid; m["nearestVoidCells"] = nearestVoid == float.MaxValue ? -1f : nearestVoid;

            // ---- D3 back.model_face: per model instance, the samples that see one of its faces from behind. the design's outer-wall rule
            // (Walls.cs) on model faces: a face whose BACK side is the void right behind it (nothing walkable, no ground drawn within
            // Walls.PlayCells cells) is an OUTER face seen from across the void: see-through by design, counted, not listed.
            var byInst = new Dictionary<int, (List<int> cells, float near, int tri)>();
            foreach (var s in samples)
                foreach (var kv in s.BackModels)
                {
                    if (!byInst.TryGetValue(kv.Key, out var e)) e = (new List<int>(), float.MaxValue, -1);
                    e.cells.Add(s.Cell); if (kv.Value.d < e.near) { e.near = kv.Value.d; e.tri = kv.Value.tri; } byInst[kv.Key] = e;
                }
            int listed = 0, outerByDesign = 0, interiorFaces = 0, outerTowardVoid = 0;
            foreach (var kv in byInst.OrderBy(k => k.Value.near).ThenByDescending(k => k.Value.cells.Count))
            {
                string cls = FaceClass(kv.Value.tri);
                if (cls == "outer") { outerByDesign++; continue; }
                if (cls == "interior") interiorFaces++; else outerTowardVoid++;
                var inst = S.Instances[kv.Key]; var (cl, near, _) = kv.Value;
                string sev = near <= 10f ? "medium" : "low";
                if (listed++ >= 60) break; // the rest are in the metrics
                int x0 = cl.Min(c => c % W), x1 = cl.Max(c => c % W), y0 = cl.Min(c => c / W), y1 = cl.Max(c => c / W);
                int at = cl.OrderBy(c => (new Vector3(c % W + 0.5f, 0, c / W + 0.5f) - (inst.Min + inst.Max) * 0.5f).sqrMagnitude).First();
                R.Findings.Add(new Finding
                {
                    Kind = "back.model_face", Severity = sev, Views = new[] { "firstPerson" }, Count = cl.Count,
                    Region = new Region { Runs = Region.FromCells(BlockCells(cl), W), Box = new[] { x0, y0, Math.Min(W - 1, x1 + step - 1), Math.Min(H - 1, y1 + step - 1) }, At = new[] { at % W, at / W } },
                    World = new Dictionary<string, float[]> { ["loki"] = new[] { (inst.Min.x + inst.Max.x) / 2, (inst.Min.y + inst.Max.y) / 2, (inst.Min.z + inst.Max.z) / 2 } },
                    Measure = new Dictionary<string, object> { ["value"] = near, ["unit"] = "cells to the nearest face seen from behind", ["metresFirstPerson"] = near * Views.FirstPersonMetresPerCell, ["samples"] = cl.Count },
                    Cause = new List<string> { $"model #{inst.Index} {Names.Display(inst.File)} ({Names.Display(inst.Name)})" },
                    Fix = $"faces of this model seen from behind are not drawn (LokiModel.shader culls backs; rb draws both sides): draw {Names.Display(inst.File)} two-sided (a per-file material rule), or hide what shows through (report only: Architect's fix file changes no model)",
                    Note = $"survey samples (lattice cells) whose eye sees through one of the model's single-sided faces; the design's outer-wall rule: the nearest such face is {(cls == "interior" ? "INTERIOR (playable or drawn on both sides: it should show both sides)" : "OUTER but drawn toward the void (it should show the play-area side)")}"
                });
            }
            m["modelsSeenFromBehind"] = byInst.Count; m["modelsSeenFromBehindListed"] = Math.Min(byInst.Count - outerByDesign, 60);
            m["modelFacesInterior"] = interiorFaces; m["modelFacesOuterDrawnTowardTheVoid"] = outerTowardVoid; m["modelFacesOuterSeeThroughByDesign"] = outerByDesign;
            m["groundWallBackRays"] = samples.Sum(s => s.BackGroundWalls);

            // ---- D5 under.exposed (info, per model file)
            var under = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var s in samples) foreach (var kv in s.Under) { string f = Names.Display(S.Instances[kv.Key].File); under.TryGetValue(f, out int c); under[f] = c + kv.Value; }
            m["undersideRaysByFile"] = under.OrderByDescending(k => k.Value).Take(12).ToDictionary(k => k.Key, k => (object)k.Value);
            if (under.Count > 0)
                R.Findings.Add(new Finding { Kind = "under.exposed", Severity = "info", Views = new[] { "firstPerson" }, Count = under.Values.Sum(), Measure = new Dictionary<string, object> { ["value"] = under.Count, ["unit"] = "model files" },
                    Cause = under.OrderByDescending(k => k.Value).Take(6).Select(k => $"{k.Key} ({k.Value} rays)").ToList(),
                    Fix = "info: undersides RO's camera (always 10-90 degrees above) never showed are the first thing these eyes see above them: check them in first person" });

            // ---- K1 view.popin
            var pop = new CellSet(W, H); int popN = 0;
            foreach (var s in samples) { if (s.Level == 0) continue; float share = s.LongLevel / (float)s.Level; if (share > 0.5f) { Block(pop, s.Cell, 3, share); popN++; } }
            Emit("view.popin", pop, new[] { "firstPerson" }, false, "share of level sight lines", 0.5f,
                (sv, t) => $"info: more than half the level sight lines here run past the server's view range ({Views.ServerAreaSize} cells; Loki Host 30): units appear and vanish in plain sight",
                "survey samples whose level rays (within 14 degrees of the horizon) run farther than area_size + 1 cells", metresFp: false);
            m["samplesWithLongSightLines"] = popN;

            // ---- F4 fx.popin: map effects seen from farther than 25 cells
            int fxSeen = 0; var fxList = new List<string>();
            if (S.Effects.Count > 0)
            {
                var st = new Grid.RayState(S.Count);
                for (int e = 0; e < S.Effects.Count; e++)
                {
                    var ef = S.Effects[e]; bool seen = false; float far = 0;
                    foreach (var s in samples)
                    {
                        int x = s.Cell % W, y = s.Cell / W; var o = new Vector3(x + 0.5f, S.Floor[s.Cell] + eye, y + 0.5f);
                        var dv = ef.Pos - o; float flat = new Vector2(dv.x, dv.z).magnitude; if (flat <= Views.MapEffectCells || flat > 120f) continue;
                        float len = dv.magnitude; var h = G.Ray(st, o, dv / len, 0f, len - 0.05f, Drawn, RoomVisible(RoomAt(x, y)));
                        if (h.Tri < 0) { seen = true; far = Math.Max(far, flat); }
                    }
                    if (seen) { fxSeen++; if (fxList.Count < 8) fxList.Add($"effect {ef.Id} at cell {Mathf.FloorToInt(ef.Pos.x)},{Mathf.FloorToInt(ef.Pos.z)} seen from up to {far:0} cells"); }
                }
                if (fxSeen > 0)
                    R.Findings.Add(new Finding { Kind = "fx.popin", Severity = "low", Views = new[] { "firstPerson" }, Count = fxSeen, Measure = new Dictionary<string, object> { ["value"] = fxSeen, ["unit"] = "effects", ["threshold"] = Views.MapEffectCells },
                        Cause = fxList, Fix = "map effects start within 25 cells of the character (World/WorldLook.cs:110-122) but are in sight from farther: start them by line of sight / a larger radius in first person" });
            }
            m["effectsSeenBeyond25"] = fxSeen;

            // ---- J3 follow.head_clip, J4 view.occlusion
            var fol = new CellSet(W, H); int ringNear = 0, ringAll = 0, blocked = 0;
            foreach (var s in samples)
            {
                ringNear += s.FollowNear; ringAll += s.FollowRing; blocked += s.FollowBlocked;
                float share = s.FollowRing > 0 ? s.FollowNear / (float)s.FollowRing : 0;
                if (share > 0.25f) Block(fol, s.Cell, share > 0.5f ? 1 : 2, share);
            }
            Emit("follow.head_clip", fol, new[] { "follow" }, false, "share of headings", 0.25f,
                (sv, t) => "in Follow the player's head (11 cells back, 12 up at the default pitch) is at the map's geometry for many headings: a cutaway round the player's head too, or a higher default pitch here",
                $"survey samples (each standing for its {step} x {step} block) whose Follow eye ring ({fBack:0.#} cells out, {fUp:0.#} up, 16 headings) comes within {FollowHeadMetres} m (= {followR:0.#} cells) of drawn geometry: low for more than a quarter of the headings, medium for more than half", metresFp: false);
            m["followRingNearShare"] = ringAll > 0 ? ringNear / (float)ringAll : 0f; m["followLineToChestBlockedShare"] = ringAll > 0 ? blocked / (float)ringAll : 0f;
        }
    }
}
