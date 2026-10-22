using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Loki.VrMaps
{
    /// <summary>
    /// Build step 3: E1 flat.card (a model that is one plane: a backdrop, a sign, a cut-out bush) seen edge-on from where the player
    /// stands, and L2 scale.stats (how tall the map's models are, in cells and first-person metres). Plan section 2, E and L.
    /// </summary>
    public sealed partial class Analysis
    {
        /// <summary>A model thinner than this share of its largest extent is one plane (FLAGGED).</summary>
        public const float CardThinness = 0.02f, CardNearCells = 20f, CardEdgeOnSin = 0.26f /* 15 degrees */, BackdropCells = 10f;

        /// <summary>Principal axes of an instance's drawn vertices: the largest and smallest spread (standard deviations) and the
        /// normal (the axis of least spread). Jacobi rotations on the 3 x 3 covariance.</summary>
        private bool Plane(Scene.Instance inst, out Vector3 centre, out Vector3 normal, out float big, out float small)
        {
            centre = normal = Vector3.zero; big = small = 0f;
            int n = inst.Tris * 3; if (inst.Tris < 2) return false;
            double cx = 0, cy = 0, cz = 0;
            for (int t = inst.FirstTri; t < inst.FirstTri + inst.Tris; t++) for (int k = 0; k < 3; k++) { cx += S.P[t * 9 + k * 3]; cy += S.P[t * 9 + k * 3 + 1]; cz += S.P[t * 9 + k * 3 + 2]; }
            cx /= n; cy /= n; cz /= n;
            var a = new double[3, 3];
            for (int t = inst.FirstTri; t < inst.FirstTri + inst.Tris; t++)
                for (int k = 0; k < 3; k++)
                {
                    double x = S.P[t * 9 + k * 3] - cx, y = S.P[t * 9 + k * 3 + 1] - cy, z = S.P[t * 9 + k * 3 + 2] - cz;
                    a[0, 0] += x * x; a[0, 1] += x * y; a[0, 2] += x * z; a[1, 1] += y * y; a[1, 2] += y * z; a[2, 2] += z * z;
                }
            a[1, 0] = a[0, 1]; a[2, 0] = a[0, 2]; a[2, 1] = a[1, 2];
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) a[i, j] /= n;
            var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            for (int sweep = 0; sweep < 30; sweep++)
            {
                double off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
                if (off < 1e-12) break;
                for (int p = 0; p < 2; p++)
                    for (int q = p + 1; q < 3; q++)
                    {
                        if (Math.Abs(a[p, q]) < 1e-15) continue;
                        double th = 0.5 * Math.Atan2(2 * a[p, q], a[q, q] - a[p, p]), c = Math.Cos(th), s = Math.Sin(th);
                        for (int k = 0; k < 3; k++) { double akp = a[k, p], akq = a[k, q]; a[k, p] = c * akp - s * akq; a[k, q] = s * akp + c * akq; }
                        for (int k = 0; k < 3; k++) { double apk = a[p, k], aqk = a[q, k]; a[p, k] = c * apk - s * aqk; a[q, k] = s * apk + c * aqk; }
                        for (int k = 0; k < 3; k++) { double vkp = v[k, p], vkq = v[k, q]; v[k, p] = c * vkp - s * vkq; v[k, q] = s * vkp + c * vkq; }
                    }
            }
            int iMin = 0, iMax = 0;
            for (int i = 1; i < 3; i++) { if (a[i, i] < a[iMin, iMin]) iMin = i; if (a[i, i] > a[iMax, iMax]) iMax = i; }
            big = (float)Math.Sqrt(Math.Max(0, a[iMax, iMax])); small = (float)Math.Sqrt(Math.Max(0, a[iMin, iMin]));
            normal = new Vector3((float)v[0, iMin], (float)v[1, iMin], (float)v[2, iMin]).normalized;
            centre = new Vector3((float)cx, (float)cy, (float)cz);
            return big > 1e-4f;
        }

        public void RunStep3()
        {
            int W = S.W, H = S.H;
            // ---- E1 flat.card
            var m = R.M("E1 flat.card"); int cards = 0, edgeOn = 0; var listed = 0;
            var walk = Enumerable.Range(0, W * H).Where(i => S.Walk[i]).ToList();
            int step = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(walk.Count / 6000.0)));
            var eyes = walk.Where(i => (i % W) % step == 0 && (i / W) % step == 0).ToList();
            foreach (var inst in S.Instances)
            {
                if (inst.Missing || inst.Tris < 2) continue;
                if (!Plane(inst, out var c, out var n, out float big, out float small) || small > big * CardThinness) continue;
                cards++;
                float extent = big * 3.464f; // a uniform spread's standard deviation is its length / sqrt(12)
                var seen = new List<int>(); float minAng = 90f;
                foreach (int i in eyes)
                {
                    var e = new Vector3(i % W + 0.5f, S.Floor[i] + Views.EyeCells, i / W + 0.5f);
                    var d = c - e; float flat = new Vector2(d.x, d.z).magnitude; if (flat > CardNearCells + extent * 0.5f || d.magnitude < 1e-3f) continue;
                    float sin = Math.Abs(Vector3.Dot(d.normalized, n));
                    if (sin < CardEdgeOnSin) { seen.Add(i); minAng = Math.Min(minAng, (float)(Math.Asin(sin) * 180 / Math.PI)); }
                }
                if (seen.Count == 0) continue;
                edgeOn++;
                if (listed++ >= 40) continue;
                int x0 = seen.Min(q => q % W), x1 = seen.Max(q => q % W), y0 = seen.Min(q => q / W), y1 = seen.Max(q => q / W);
                R.Findings.Add(new Finding
                {
                    Kind = "flat.card", Severity = extent > BackdropCells ? "medium" : "low", Views = new[] { "firstPerson" }, Count = seen.Count,
                    Region = new Region { Runs = Region.FromCells(seen, W), Box = new[] { x0, y0, x1, y1 }, At = new[] { seen[0] % W, seen[0] / W } },
                    World = new Dictionary<string, float[]> { ["loki"] = new[] { c.x, c.y, c.z } },
                    Measure = new Dictionary<string, object> { ["value"] = extent, ["unit"] = "cells across", ["thickness"] = small / big, ["minAngleDegrees"] = minAng, ["samples"] = seen.Count },
                    Cause = new List<string> { $"model #{inst.Index} {Names.Display(inst.File)} ({Names.Display(inst.Name)})" },
                    Fix = extent > BackdropCells ? "a flat backdrop / large card: in first person it is paper-thin and vanishes edge-on: a 3-D stand-in, or keep the player's eye off its edge" : "a flat card seen edge-on from the walkable floor (within 15 degrees): fine far away, thin up close in stereo",
                    Note = $"the model is one plane (thinner than {CardThinness:P0} of its spread); samples every {step} cells within {CardNearCells} cells"
                });
            }
            m["planarModels"] = cards; m["seenEdgeOn"] = edgeOn;

            // ---- L2 scale.stats (info): model heights above the floor under them, in cells and first-person metres
            var heights = new List<float>();
            foreach (var inst in S.Instances)
            {
                if (inst.Missing || inst.Tris == 0) continue;
                int cx = Mathf.Clamp(Mathf.FloorToInt((inst.Min.x + inst.Max.x) * 0.5f), 0, W - 1), cz = Mathf.Clamp(Mathf.FloorToInt((inst.Min.z + inst.Max.z) * 0.5f), 0, H - 1);
                heights.Add(inst.Max.y - S.Floor[cx + cz * W]);
            }
            heights.Sort();
            var l = R.M("L2 scale.stats");
            if (heights.Count > 0)
            {
                float P(float q) => heights[Math.Min(heights.Count - 1, (int)(q * heights.Count))];
                l["modelHeightCells"] = new Dictionary<string, object> { ["p10"] = P(0.1f), ["p50"] = P(0.5f), ["p90"] = P(0.9f), ["max"] = heights[heights.Count - 1] };
                l["modelHeightMetresFirstPerson"] = new Dictionary<string, object> { ["p10"] = P(0.1f) * Views.FirstPersonMetresPerCell, ["p50"] = P(0.5f) * Views.FirstPersonMetresPerCell, ["p90"] = P(0.9f) * Views.FirstPersonMetresPerCell };
                l["playerHeightCells"] = Views.PlayerHeightCells;
                R.MapFacts["scale"] = $"models {P(0.5f):0.#} cells tall at the median ({P(0.5f) * Views.FirstPersonMetresPerCell:0.#} m in first person), 90 % under {P(0.9f):0.#} cells; the player is {Views.PlayerHeightCells} cells ({Views.PlayerHeightCells * Views.FirstPersonMetresPerCell:0.##} m)";
            }
            R.M("F2 light.double_shadow")["built"] = false;
        }
    }
}
