using System.Text;
using System.Text.RegularExpressions;

namespace LokiHost.Core;

/// <summary>
/// tools/mapcache/check_points.py in C# (client wave 7, w7host), step for step: every place rAthena puts a player or fires a
/// trigger on the maps an import map cache replaces, with rAthena's own cache and with the new one: warp touch areas and
/// destinations, OnTouch script areas, literal "map",x,y in scripts (warp, savepoint, areawarp, monster …), fixed monster
/// spawn areas, start points, @go, pc.cpp's fallback. Walkable = map_gat2cell (0/2/3/4/6). A touch area is the walkable cells
/// of (x-xs..x+xs, y-ys..y+ys) (npc_setcells, rA src/map/npc.cpp:4972-5001). Also: each of them is reachable from the map's
/// largest walkable region (steps as rA src/map/path.cpp:385-393) with rAthena's cache and with the new one.
///
/// <see cref="Result.Lines"/> are the Python tool's own output lines, character for character (the port is checked by
/// comparing them, design notes); <see cref="Result.Bad"/> / <see cref="Result.Lost"/> are its two verdict counts:
/// a new cache is installed only when both are 0.
///
/// Python's text handling is kept where it shows: files are Latin-1 with universal newlines; str.strip()'s whitespace;
/// \d and \s as Python's re gives them for Latin-1 text (0-9; the whitespace set below).
/// </summary>
public static class PointsCheck
{
    public sealed class Result
    {
        public List<string> Lines = new();
        /// <summary>"NEWLY BLOCKED point" + "AREA LOST" lines: unreachable with the new cache and reachable before.</summary>
        public int Bad;
        /// <summary>"CUT OFF from the main region" lines.</summary>
        public int Lost;
        public int Checked, Reachability, Maps;
        public bool Clean => Bad == 0 && Lost == 0;
    }

    // Python 3 str.isspace / re \s for the characters a Latin-1 file can hold
    const string Ws = @"\t\n\v\f\r\x1c-\x1f \x85\xa0";
    static bool IsPySpace(char c) => c == ' ' || (c >= '\t' && c <= '\r') || (c >= '\x1c' && c <= '\x1f') || c == '\x85' || c == '\xa0';
    static string PyStrip(string s)
    {
        int a = 0, b = s.Length;
        while (a < b && IsPySpace(s[a])) a++;
        while (b > a && IsPySpace(s[b - 1])) b--;
        return s.Substring(a, b - a);
    }
    static string PyLStrip(string s) { int a = 0; while (a < s.Length && IsPySpace(s[a])) a++; return s.Substring(a); }

    const RegexOptions Opt = RegexOptions.CultureInvariant;
    static readonly Regex ConfLine = new(@"\A(npc|import):[" + Ws + @"]*([^" + Ws + @"]+)", Opt);
    static readonly Regex Lit = new(@"""([A-Za-z0-9_@]+)""[" + Ws + @"]*,[" + Ws + @"]*([0-9]+)[" + Ws + @"]*,[" + Ws + @"]*([0-9]+)", Opt); // re.I on [a-z0-9_@]
    static readonly Regex WarpType = new(@"\Awarp[0-9]?$", Opt);
    static readonly Regex MapXy = new(@"\A([^,]+),([0-9]+),([0-9]+)", Opt);
    static readonly Regex WarpDest = new(@"\A(-?[0-9]+),(-?[0-9]+),([^,]+),([0-9]+),([0-9]+)", Opt);
    static readonly Regex TouchXy = new(@"\A[^,{]+,([0-9]+),([0-9]+)", Opt);
    static readonly Regex MonsterType = new(@"\A(boss_)?monster$", Opt);
    static readonly Regex MonsterArea = new(@"\A([^,]+),([0-9]+),([0-9]+)(?:,([0-9]+),([0-9]+))?", Opt);
    static readonly Regex StartPoint = new(@"([a-z0-9_@]+),([0-9]+),([0-9]+)", Opt);
    static readonly Regex MapDefine = new(@"#define[" + Ws + @"]+MAP_([A-Z0-9_]+)[" + Ws + @"]+""([^""]+)""", Opt);
    static readonly Regex GoPoint = new(@"\{[" + Ws + @"]*MAP_([A-Z0-9_]+)[" + Ws + @"]*,[" + Ws + @"]*([0-9]+)[" + Ws + @"]*,[" + Ws + @"]*([0-9]+)", Opt);

    /// <summary>int(digits): Python's is unbounded; a coordinate beyond any map is the same "not walkable" at a million.</summary>
    static int Int(string digits)
    {
        bool neg = digits.StartsWith('-');
        var d = digits.TrimStart('-').TrimStart('0');
        int v = d.Length == 0 ? 0 : d.Length > 7 ? 10_000_000 : int.Parse(d, System.Globalization.CultureInfo.InvariantCulture);
        return neg ? -v : v;
    }

    /// <summary>open(path, encoding='latin-1') read line by line: Latin-1, "\r\n" and "\r" are line ends too; the lines
    /// without their end (nothing in the tool depends on it).</summary>
    static List<string> Lines(string path)
    {
        var text = Encoding.Latin1.GetString(File.ReadAllBytes(path)).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }
    static string Text(string path) => Encoding.Latin1.GetString(File.ReadAllBytes(path)).Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>os.path.join(ra, path) as Python on Windows writes it (the tool's "seen" set compares these strings).</summary>
    static string Join(string ra, string path) => Path.Combine(ra, path);

    sealed record Point(string What, string Map, int X, int Y);
    sealed record Area(string What, string Map, int X, int Y, int Rx, int Ry);

    sealed class Grid
    {
        public int Xs, Ys; public byte[] C = Array.Empty<byte>();
        // def walk(cells, m, x, y): return 0 <= x < xs and 0 <= y < ys and c[x + y * xs] in WALKABLE
        public bool Walk(int x, int y) => x >= 0 && x < Xs && y >= 0 && y < Ys && MapCache.Walkable(C[x + y * Xs]);
        // def area(cells, m, x, y, rx, ry): return sum(walk(cells, m, i, j) for j in range(y - ry, y + ry + 1) for i in range(x - rx, x + rx + 1))
        // (cells outside the map are never walkable: only the part of the box inside the map is visited)
        public int AreaCount(int x, int y, int rx, int ry)
        {
            int n = 0;
            for (int j = Math.Max(y - ry, 0); j <= Math.Min((long)y + ry, Ys - 1); j++)
                for (int i = Math.Max(x - rx, 0); i <= Math.Min((long)x + rx, Xs - 1); i++)
                    if (MapCache.Walkable(C[i + j * Xs])) n++;
            return n;
        }
    }

    /// <summary>
    /// The check. <paramref name="rathenaDir"/>: the server folder (db/, npc/, conf/); <paramref name="newCache"/>: the bytes of
    /// the new import cache; <paramref name="sourceDir"/>: an rAthena source folder for the @go points
    /// (src/map/atcommand.cpp, src/common/mapindex.hpp), or null: they are then not checked, as with the tool run without it.
    /// </summary>
    public static Result Run(string rathenaDir, byte[] newCache, string? sourceDir)
    {
        var r = new Result();
        void Print(string s) => r.Lines.Add(s);
        string ra = rathenaDir;

        // new = {n: cache_cells(e) for n, e in read_cache(newp).items()}
        var neu = new Dictionary<string, Grid>(StringComparer.Ordinal);
        foreach (var (n, e) in MapCache.Read(newCache, "the new map cache")) neu[n] = new Grid { Xs = e.Xs, Ys = e.Ys, C = MapCache.Cells(e) };
        // old = {}; for p in (db/re/map_cache.dat, db/map_cache.dat): for n, e in read_cache(p).items(): old.setdefault(n, cache_cells(e))
        var old = new Dictionary<string, Grid>(StringComparer.Ordinal);
        foreach (var p in new[] { Path.Combine(ra, "db", "re", "map_cache.dat"), Path.Combine(ra, "db", "map_cache.dat") })
            foreach (var (n, e) in MapCache.Read(p))
            {
                var g = new Grid { Xs = e.Xs, Ys = e.Ys, C = MapCache.Cells(e) }; // (setdefault's argument is evaluated either way)
                old.TryAdd(n, g);
            }
        var maps = new HashSet<string>(neu.Keys, StringComparer.Ordinal);
        r.Maps = maps.Count;
        Grid Old(string m) => old.TryGetValue(m, out var g) ? g : throw new InvalidDataException($"the map '{m}' of the new cache is in neither of rAthena's own caches: it cannot be compared"); // Python: KeyError

        // the NPC files the map-server loads (map.cpp:4264 npc/re/scripts_main.conf; npc: / import: lines)
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Conf(string path)
        {
            string full = Join(ra, path);
            if (seen.Contains(full) || !File.Exists(full)) return;
            seen.Add(full);
            foreach (var raw in Lines(full))
            {
                // line = line.split('//')[0].strip(); m = re.match(r'(npc|import):\s*(\S+)', line)
                int cut = raw.IndexOf("//", StringComparison.Ordinal);
                string line = PyStrip(cut >= 0 ? raw.Substring(0, cut) : raw);
                var m = ConfLine.Match(line);
                if (!m.Success) continue;
                if (m.Groups[1].Value == "import") Conf(m.Groups[2].Value);
                else files.Add(m.Groups[2].Value);
            }
        }
        Conf("npc/re/scripts_main.conf");

        var points = new List<Point>();
        var areas = new List<Area>();
        foreach (var f in files)
        {
            string full = Join(ra, f);
            if (!File.Exists(full)) continue;
            int no = 0;
            foreach (var line in Lines(full))
            {
                no++;
                if (PyLStrip(line).StartsWith("//", StringComparison.Ordinal)) continue;
                string where = $"{f}:{no}";
                var cols = line.Split('\t'); // line.rstrip('\r\n').split('\t')
                if (cols.Length >= 4 && WarpType.IsMatch(PyStrip(cols[1])))
                {
                    var a = MapXy.Match(cols[0]); var b = WarpDest.Match(cols[3]);
                    if (a.Success && b.Success)
                    {
                        areas.Add(new Area($"warp {cols[2]} touch area ({where})", a.Groups[1].Value, Int(a.Groups[2].Value), Int(a.Groups[3].Value), Int(b.Groups[1].Value), Int(b.Groups[2].Value)));
                        points.Add(new Point($"warp {cols[2]} lands ({where})", b.Groups[3].Value, Int(b.Groups[4].Value), Int(b.Groups[5].Value)));
                    }
                }
                else if (cols.Length >= 4 && (PyStrip(cols[1]) == "script" || cols[1].StartsWith("duplicate(", StringComparison.Ordinal)))
                {
                    var a = MapXy.Match(cols[0]); var b = TouchXy.Match(cols[3]);
                    if (a.Success && b.Success && a.Groups[1].Value != "-" && (Int(b.Groups[1].Value) != 0 || Int(b.Groups[2].Value) != 0))
                        areas.Add(new Area($"OnTouch {cols[2]} ({where})", a.Groups[1].Value, Int(a.Groups[2].Value), Int(a.Groups[3].Value), Int(b.Groups[1].Value), Int(b.Groups[2].Value)));
                }
                else if (cols.Length >= 3 && MonsterType.IsMatch(PyStrip(cols[1])))
                {
                    var a = MonsterArea.Match(cols[0]);
                    if (a.Success && (Int(a.Groups[2].Value) != 0 || Int(a.Groups[3].Value) != 0))
                        areas.Add(new Area($"spawn {cols[2]} ({where})", a.Groups[1].Value, Int(a.Groups[2].Value), Int(a.Groups[3].Value),
                            a.Groups[4].Success ? Int(a.Groups[4].Value) : 0, a.Groups[5].Success ? Int(a.Groups[5].Value) : 0));
                }
                foreach (Match m in Lit.Matches(line))
                {
                    string x = m.Groups[2].Value, y = m.Groups[3].Value;
                    if (Int(x) != 0 || Int(y) != 0) points.Add(new Point($"script \"{m.Groups[1].Value}\",{x},{y} ({where})", m.Groups[1].Value, Int(x), Int(y)));
                }
            }
        }

        // start points (char_athena.conf / import), @go (atcommand.cpp), pc.cpp's fallback
        foreach (var p in new[] { Path.Combine(ra, "conf", "char_athena.conf"), Path.Combine(ra, "conf", "import", "char_conf.txt") })
        {
            if (!File.Exists(p)) continue;
            foreach (var line in Lines(p))
            {
                if (!line.StartsWith("start_point", StringComparison.Ordinal)) continue;
                int colon = line.IndexOf(':');
                if (colon < 0) throw new InvalidDataException($"{Path.GetFileName(p)}: a start_point line without ':'"); // Python: IndexError
                foreach (Match m in StartPoint.Matches(line.Substring(colon + 1)))
                    points.Add(new Point($"start_point ({Path.GetFileName(p)})", m.Groups[1].Value, Int(m.Groups[2].Value), Int(m.Groups[3].Value)));
            }
        }
        string acmd = sourceDir != null ? Path.Combine(sourceDir, "src", "map", "atcommand.cpp") : "";
        if (acmd != "" && File.Exists(acmd))
        {
            // MAP_* are defined in src/common/mapindex.hpp (MAP_MORROC is "morocc": the constant's name is not the map's)
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match m in MapDefine.Matches(Text(Path.Combine(sourceDir!, "src", "common", "mapindex.hpp")))) names[m.Groups[1].Value] = m.Groups[2].Value;
            foreach (Match m in GoPoint.Matches(Text(acmd)))
            {
                if (!names.TryGetValue(m.Groups[1].Value, out var map)) throw new InvalidDataException($"atcommand.cpp names MAP_{m.Groups[1].Value}, which mapindex.hpp does not define"); // KeyError
                points.Add(new Point("@go (atcommand.cpp)", map, Int(m.Groups[2].Value), Int(m.Groups[3].Value)));
            }
        }
        else Print("(no rAthena source dir given: the @go points are not checked)");
        points.Add(new Point("pc.cpp:2231 fallback", "prontera", 273, 354));

        Print($"{files.Count} NPC files; {points.Count} points, {areas.Count} areas in all maps");
        var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (what, m) in points.Select(p => (p.What, p.Map)).Concat(areas.Select(a => (a.What, a.Map))))
            if (maps.Contains(m)) { string k = what.Split(' ')[0]; kinds[k] = kinds.GetValueOrDefault(k) + 1; }
        Print("on the replaced maps: " + string.Join(", ", kinds.Select(kv => $"{kv.Value} {kv.Key}")));

        int bad = 0, check = 0;
        foreach (var p in points)
        {
            if (!maps.Contains(p.Map)) continue;
            check++;
            bool o = Old(p.Map).Walk(p.X, p.Y), n = neu[p.Map].Walk(p.X, p.Y);
            if (o && !n) { bad++; Print($"NEWLY BLOCKED point {p.Map} {p.X},{p.Y}: {p.What}"); }
            else if (!o && n) Print($"  (newly walkable point {p.Map} {p.X},{p.Y}: {p.What})");
        }
        foreach (var a in areas)
        {
            if (!maps.Contains(a.Map)) continue;
            check++;
            int o = Old(a.Map).AreaCount(a.X, a.Y, a.Rx, a.Ry), n = neu[a.Map].AreaCount(a.X, a.Y, a.Rx, a.Ry);
            if (n == 0 && o > 0) { bad++; Print($"AREA LOST {a.Map} {a.X},{a.Y} +-{a.Rx},{a.Ry}: walkable cells {o} -> {n}: {a.What}"); }
            else if (n < o) Print($"  (area smaller {a.Map} {a.X},{a.Y} +-{a.Rx},{a.Ry}: {o} -> {n}: {a.What})");
        }
        Print($"checked on the {maps.Count} replaced maps: {check}; unreachable with the new cache (and reachable before): {bad}");

        // ---- reachability: the largest walkable region of each map (steps as path.cpp:385-393: a diagonal needs both sides walkable)
        var regions = new Dictionary<(string Tag, string Map), (int[] Lab, int Main, int Xs, int Ys)>();
        bool InMain(Grid g, string tag, string m, int x, int y, int rx, int ry)
        {
            if (!regions.TryGetValue((tag, m), out var reg)) regions[(tag, m)] = reg = Regions(g);
            // any(0 <= i < xs and 0 <= j < ys and lab[i + j * xs] == main for j in range(y - ry, y + ry + 1) for i in range(x - rx, x + rx + 1))
            for (int j = Math.Max(y - ry, 0); j <= Math.Min((long)y + ry, reg.Ys - 1); j++)
                for (int i = Math.Max(x - rx, 0); i <= Math.Min((long)x + rx, reg.Xs - 1); i++)
                    if (reg.Lab[i + j * reg.Xs] == reg.Main) return true;
            return false;
        }
        int lost = 0, reach = 0;
        // a point: itself or a neighbour (you can step off it)
        foreach (var a in points.Select(p => new Area(p.What, p.Map, p.X, p.Y, 1, 1)).Concat(areas))
        {
            if (!maps.Contains(a.Map)) continue;
            reach++;
            bool o = InMain(Old(a.Map), "old", a.Map, a.X, a.Y, a.Rx, a.Ry), w = InMain(neu[a.Map], "new", a.Map, a.X, a.Y, a.Rx, a.Ry);
            if (o && !w) { lost++; Print($"CUT OFF from the main region {a.Map} {a.X},{a.Y} +-{a.Rx},{a.Ry}: {a.What}"); }
        }
        Print($"reachability: {reach} checked; reachable from the main region before and not with the new cache: {lost}");
        foreach (var m in maps.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!regions.TryGetValue(("old", m), out var a) || !regions.TryGetValue(("new", m), out var b)) continue;
            int so = a.Lab.Count(v => v == a.Main), sn = b.Lab.Count(v => v == b.Main);
            if (Math.Abs(sn - so) > 0.2 * so) Print($"  main region of {m}: {so} -> {sn} cells");
        }
        r.Bad = bad; r.Lost = lost; r.Checked = check; r.Reachability = reach;
        return r;
    }

    /// <summary>
    /// check_points.py regions(): every walkable cell labelled with its region (cells in index order start a region; steps to
    /// the 8 neighbours, a diagonal only when both orthogonal cells are walkable), and the largest region (the first of the
    /// largest: max(range(len(sizes)), key=...)), -1 when the map has no walkable cell.
    /// </summary>
    static (int[] Lab, int Main, int Xs, int Ys) Regions(Grid g)
    {
        int xs = g.Xs, ys = g.Ys;
        var c = g.C;
        bool Ok(int x, int y) => x >= 0 && x < xs && y >= 0 && y < ys && MapCache.Walkable(c[x + y * xs]);
        var lab = new int[Math.Max(0, xs * ys)];
        Array.Fill(lab, -1);
        var sizes = new List<int>();
        var q = new Queue<int>();
        ReadOnlySpan<int> dxs = stackalloc[] { 1, -1, 0, 0, 1, 1, -1, -1 };
        ReadOnlySpan<int> dys = stackalloc[] { 0, 0, 1, -1, 1, -1, 1, -1 };
        for (int s = 0; s < lab.Length; s++)
        {
            if (lab[s] != -1 || !MapCache.Walkable(c[s])) continue;
            int k = sizes.Count; lab[s] = k; int n = 1;
            q.Enqueue(s);
            while (q.Count > 0)
            {
                int i = q.Dequeue(), x = i % xs, y = i / xs;
                for (int d = 0; d < 8; d++)
                {
                    int dx = dxs[d], dy = dys[d], nx = x + dx, ny = y + dy;
                    if (!Ok(nx, ny) || lab[nx + ny * xs] != -1) continue;
                    if (dx != 0 && dy != 0 && !(Ok(x + dx, y) && Ok(x, y + dy))) continue;
                    lab[nx + ny * xs] = k; n++; q.Enqueue(nx + ny * xs);
                }
            }
            sizes.Add(n);
        }
        int main = -1;
        for (int k = 0; k < sizes.Count; k++) if (main < 0 || sizes[k] > sizes[main]) main = k;
        return (lab, main, xs, ys);
    }
}
