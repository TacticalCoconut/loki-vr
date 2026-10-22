using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Loki.VrMaps
{
    /// <summary>One finding: a region (8-connected cells) of one kind and one severity (design notes section 3).</summary>
    public sealed class Finding
    {
        public string Kind, Severity; public string[] Views; public int Count;
        public Region Region; public Dictionary<string, float[]> World;
        public Dictionary<string, object> Measure; public List<string> Cause; public string Fix; public string Basis = "measured"; public string Note;
    }
    /// <summary>A region's cells, all of them, as runs [y, x0, x1] (x0..x1 inclusive on row y); its box [x0, y0, x1, y1] and worst cell.</summary>
    public sealed class Region
    {
        public int[] Box; public int[] At; public List<int[]> Runs;
        public bool Contains(int x, int y) => Runs != null && Runs.Any(r => r[0] == y && x >= r[1] && x <= r[2]);
        public IEnumerable<int[]> Cells() { if (Runs == null) yield break; foreach (var r in Runs) for (int x = r[1]; x <= r[2]; x++) yield return new[] { x, r[0] }; }
        public static List<int[]> FromCells(IEnumerable<int> cells, int w)
        {
            var runs = new List<int[]>();
            foreach (var c in cells.OrderBy(c => c / w).ThenBy(c => c % w))
            {
                int x = c % w, y = c / w; var last = runs.Count > 0 ? runs[runs.Count - 1] : null;
                if (last != null && last[0] == y && last[2] == x - 1) last[2] = x; else runs.Add(new[] { y, x, x });
            }
            return runs;
        }
    }

    public sealed class MapReport
    {
        /// <summary>Format 2 (Architect, 2026-10-14): back.gnd_wall on the design's outer-wall rule (the D4 metrics' counter), void.hole info on
        /// a map with its own sky, back.model_face without outer faces seen from across the void.</summary>
        public string Tool = "architect"; public int Format = 2; public string Map;
        public Dictionary<string, object> Source = new Dictionary<string, object>();
        public Dictionary<string, object> Views = new Dictionary<string, object>();
        public Dictionary<string, object> MapFacts = new Dictionary<string, object>();
        public List<Finding> Findings = new List<Finding>();
        public SortedDictionary<string, Dictionary<string, object>> Metrics = new SortedDictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        public List<string> Notes = new List<string>();

        public Dictionary<string, object> M(string detector)
        {
            if (!Metrics.TryGetValue(detector, out var d)) { d = new Dictionary<string, object>(); Metrics[detector] = d; }
            return d;
        }

        public static readonly string[] SeverityOrder = { "high", "medium", "low", "info" };
        public static int Rank(string s) { int i = Array.IndexOf(SeverityOrder, s); return i < 0 ? 9 : i; }

        public string Json()
        {
            var o = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
            o.Converters.Add(new RoundFloats());
            return JsonSerializer.Serialize(this, o);
        }

        /// <summary>At most ~40 lines: the facts, counts per kind and severity, the ten worst findings.</summary>
        public string Markdown()
        {
            var sb = new StringBuilder(); var inv = CultureInfo.InvariantCulture;
            sb.Append("# ").Append(Map).Append(": VR map report (Architect format ").Append(Format).Append(")\n\n");
            foreach (var kv in MapFacts) sb.Append("- ").Append(kv.Key).Append(": ").Append(Fmt(kv.Value)).Append('\n');
            sb.Append('\n');
            var byKind = Findings.GroupBy(f => f.Kind).OrderBy(g => g.Min(f => Rank(f.Severity))).ThenBy(g => g.Key, StringComparer.Ordinal);
            sb.Append("| kind | high | medium | low | info | cells |\n|---|---|---|---|---|---|\n");
            foreach (var g in byKind)
            {
                int C(string s) => g.Where(f => f.Severity == s).Sum(f => 1);
                sb.Append($"| {g.Key} | {C("high")} | {C("medium")} | {C("low")} | {C("info")} | {g.Sum(f => f.Count)} |\n");
            }
            sb.Append("\nWorst findings (regions):\n\n");
            foreach (var f in Findings.OrderBy(f => Rank(f.Severity)).ThenByDescending(f => f.Count).Take(10))
            {
                string at = f.Region?.At != null ? $"cell {f.Region.At[0]},{f.Region.At[1]}" : "map";
                string box = f.Region?.Box != null ? $" (box {f.Region.Box[0]},{f.Region.Box[1]}-{f.Region.Box[2]},{f.Region.Box[3]})" : "";
                sb.Append($"- **{f.Severity}** {f.Kind}: {f.Count} cells at {at}{box}");
                if (f.Measure != null && f.Measure.TryGetValue("value", out var v)) sb.Append($", {Fmt(v)} {(f.Measure.TryGetValue("unit", out var u) ? u : "")}");
                if (f.Cause != null && f.Cause.Count > 0) sb.Append("; ").Append(f.Cause[0]);
                sb.Append(". Fix: ").Append(f.Fix).Append('\n');
            }
            if (Notes.Count > 0) { sb.Append("\nNotes: ").Append(string.Join("; ", Notes.Take(6))).Append('\n'); }
            return sb.ToString();
        }

        private static string Fmt(object v)
        {
            if (v is float f) return f.ToString("0.###", CultureInfo.InvariantCulture);
            if (v is double d) return d.ToString("0.###", CultureInfo.InvariantCulture);
            if (v is System.Collections.IDictionary dict) { var parts = new List<string>(); foreach (System.Collections.DictionaryEntry e in dict) parts.Add(e.Key + " " + Fmt(e.Value)); return string.Join(", ", parts); }
            if (v is System.Collections.IEnumerable en && !(v is string)) { var parts = new List<string>(); foreach (var x in en) parts.Add(Fmt(x)); return "[" + string.Join(", ", parts) + "]"; }
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        /// <summary>Floats with 4 significant decimals at most (the measures are not more precise than that).</summary>
        private sealed class RoundFloats : JsonConverter<float>
        {
            public override float Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => r.GetSingle();
            public override void Write(Utf8JsonWriter w, float v, JsonSerializerOptions o)
            {
                if (float.IsNaN(v) || float.IsInfinity(v)) { w.WriteNullValue(); return; }
                w.WriteNumberValue(Math.Round((double)v, 4));
            }
        }
    }

    /// <summary>Cells flagged by a detector, with a severity and a value each, grouped into 8-connected regions per severity.</summary>
    public sealed class CellSet
    {
        public readonly int W, H; public readonly sbyte[] Sev; public readonly float[] Val; public readonly int[] Cause;
        /// <summary>The worst value of a region is the one farthest from 0 (signed measures).</summary>
        public bool AbsWorst;
        public CellSet(int w, int h) { W = w; H = h; Sev = new sbyte[w * h]; Val = new float[w * h]; Cause = new int[w * h]; for (int i = 0; i < Sev.Length; i++) { Sev[i] = -1; Cause[i] = -1; } }
        public void Set(int x, int y, int sev, float val, int cause = -1) { int i = x + y * W; if (Sev[i] < 0 || sev < Sev[i]) { Sev[i] = (sbyte)sev; Val[i] = val; Cause[i] = cause; } }
        public int CountOf(int sev) { int n = 0; foreach (var s in Sev) if (s == sev) n++; return n; }
        public int Total { get { int n = 0; foreach (var s in Sev) if (s >= 0) n++; return n; } }

        /// <summary>Regions: 8-connected cells of the same severity. worstLow: the worst value is the lowest (else the highest).</summary>
        public List<(int sev, List<int> cells, int worst)> Regions(bool worstLow)
        {
            var seen = new bool[W * H]; var outL = new List<(int, List<int>, int)>(); var q = new Queue<int>();
            for (int i = 0; i < Sev.Length; i++)
            {
                if (Sev[i] < 0 || seen[i]) continue;
                int sev = Sev[i]; var cells = new List<int>(); seen[i] = true; q.Enqueue(i); int worst = i;
                while (q.Count > 0)
                {
                    int c = q.Dequeue(); cells.Add(c);
                    if (AbsWorst ? Math.Abs(Val[c]) > Math.Abs(Val[worst]) : worstLow ? Val[c] < Val[worst] : Val[c] > Val[worst]) worst = c;
                    int cx = c % W, cy = c / W;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx, y = cy + dy; if (x < 0 || y < 0 || x >= W || y >= H) continue;
                        int j = x + y * W; if (seen[j] || Sev[j] != sev) continue; seen[j] = true; q.Enqueue(j);
                    }
                }
                outL.Add((sev, cells, worst));
            }
            return outL;
        }
    }
}
