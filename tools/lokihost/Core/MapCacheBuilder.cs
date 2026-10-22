using System.Text;

namespace LokiHost.Core;

/// <summary>
/// tools/mapcache/build_import_cache.py in C# (client wave 7, w7host), step for step: an IMPORT map cache
/// (db/import/map_cache.dat) holding the maps whose walkability differs between rAthena's map cache and the host's own
/// Ragnarok Online install, with their cells taken from the install by rAthena's own rules. rAthena's map-server reads
/// db/import/map_cache.dat first and takes each map from the first cache that has it (rA src/map/map.cpp:3919-3925,
/// 3967-3971), so the file overrides exactly those maps. The Python tool's lines are quoted beside the code that follows them;
/// only its "every map whose walkability differs" mode is ported (Loki Host never names maps).
///
/// One difference in what is read, none in what is done with it: the Python tool reads one GRF given on its command line;
/// this reads the install through <see cref="HostInstall.Read"/> (the files the client itself would take). With an install
/// of one data.grf and no loose map files (the LATAM install) the two read the same bytes.
/// </summary>
public static class MapCacheBuilder
{
    public const int RswNoWater = 1000000; // rA src/common/grfio.hpp:9 RSW_NO_WATER; mapcache_vs_grf.py RSW_NO_WATER

    public sealed class Result
    {
        /// <summary>The new db/import/map_cache.dat.</summary>
        public byte[] File = Array.Empty<byte>();
        /// <summary>Its maps in file order.</summary>
        public List<MapCache.Map> Maps = new();
        /// <summary>Entries the import cache already had (kept unless replaced).</summary>
        public int Kept;
        /// <summary>(map, cells whose walkability changes; -1: the size differs) for every map taken from the install.</summary>
        public List<(string Name, int WalkDiff)> Replaced = new();
        public int WaterOnly, Missing;
        /// <summary>The Python tool's own report lines (its print calls), with "the new cache" where it names its output file.</summary>
        public List<string> Report = new();
    }

    /// <summary>The three caches in the server's order (build_import_cache.py: order = [db/import, db/re, db]).</summary>
    public static string[] CacheOrder(string rathenaDir) => new[]
    {
        Path.Combine(rathenaDir, "db", "import", "map_cache.dat"),
        Path.Combine(rathenaDir, "db", "re", "map_cache.dat"),
        Path.Combine(rathenaDir, "db", "map_cache.dat"),
    };

    /// <summary>
    /// build_import_cache.py main(), with wanted = set():
    ///   caches = [(p, read_cache(p)) for p in order if os.path.exists(p)]
    ///   imp = dict(caches[0][1]) if caches and caches[0][0] == order[0] else {}
    ///   known = sorted({m for _, c in caches for m in c})
    ///   result = {name: cache_cells(entry) for name, entry in imp.items()}  # kept as they are
    ///   for name in known:
    ///       src = next((c[name] for _, c in caches if name in c))
    ///       xs, ys, cc = cache_cells(src)
    ///       g = grf_cells(grf, name)
    ///       if g is None: missing += 1; continue
    ///       gx, gy, gc = g[0], g[1], g[2]
    ///       if (gx, gy) != (xs, ys): walk_diff = -1
    ///       else: walk_diff = sum(1 for i in range(xs * ys) if (cc[i] in WALKABLE) != (gc[i] in WALKABLE))
    ///       if walk_diff == 0 and not wanted:
    ///           if cc != gc: water_only += 1
    ///           continue
    ///       result[name] = (gx, gy, gc); replaced.append((name, walk_diff))
    ///   write_cache(out, result)
    ///   # verify: read back, every replaced map equals the GRF's cells
    ///   back = read_cache(out)
    ///   for name, _ in replaced:
    ///       xs, ys, cells = cache_cells(back[name]); g = grf_cells(grf, name)
    ///       assert (xs, ys, cells) == (g[0], g[1], g[2]), name
    /// <paramref name="read"/>: an RO path ("data\prontera.gat") to its bytes, or null (the install's own lookup).
    /// Anything Python would raise on (a damaged cache, a damaged GRF entry, a map file cut short) throws here too: no file is
    /// produced from data that could not be read whole.
    /// </summary>
    public static Result Build(string rathenaDir, Func<string, byte[]?> read)
    {
        var order = CacheOrder(rathenaDir);
        var caches = new List<(string Path, PyDict<MapCache.Entry> Maps)>();
        foreach (var p in order) if (File.Exists(p)) caches.Add((p, MapCache.Read(p)));
        var imp = caches.Count > 0 && caches[0].Path == order[0] ? caches[0].Maps : new PyDict<MapCache.Entry>();

        // sorted(set of str): by code point; the names are Latin-1, so ordinal order
        var known = caches.SelectMany(c => c.Maps.Keys).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList();

        var result = new PyDict<MapCache.Map>();
        foreach (var (name, entry) in imp) result[name] = new MapCache.Map(name, entry.Xs, entry.Ys, MapCache.Cells(entry));

        var r = new Result { Kept = imp.Count };
        foreach (var name in known)
        {
            var src = caches.First(c => c.Maps.ContainsKey(name)).Maps[name];
            var cc = MapCache.Cells(src);
            var g = GrfCells(read, name);
            if (g == null) { r.Missing++; continue; }
            int walkDiff;
            if (g.Xs != src.Xs || g.Ys != src.Ys) walkDiff = -1;
            else
            {
                walkDiff = 0;
                // range(xs * ys): the sizes are equal here, so both arrays hold xs * ys cells (cache_cells checked cc's; grf_cells
                // allocates bytearray(xs * ys))
                for (int i = 0; i < cc.Length; i++) if (MapCache.Walkable(cc[i]) != MapCache.Walkable(g.Cells[i])) walkDiff++;
            }
            if (walkDiff == 0)
            {
                if (!cc.AsSpan().SequenceEqual(g.Cells)) r.WaterOnly++;
                continue;
            }
            result[name] = g;
            r.Replaced.Add((name, walkDiff));
        }

        r.Maps = result.Select(kv => kv.Value).ToList();
        r.File = MapCache.Write(r.Maps);

        // verify: read back, every replaced map equals the install's cells
        var back = MapCache.Read(r.File, "the new map cache");
        foreach (var (name, _) in r.Replaced)
        {
            var e = back[name];
            var cells = MapCache.Cells(e);
            var g = GrfCells(read, name) ?? throw new InvalidDataException($"data\\{name}.gat was there a moment ago and is gone now");
            if (e.Xs != g.Xs || e.Ys != g.Ys || !cells.AsSpan().SequenceEqual(g.Cells))
                throw new InvalidDataException($"the new map cache's '{name}' does not read back as the install's map");
        }

        r.Report.Add($"wrote the new cache: {result.Count} maps ({imp.Count} kept from the import cache, {r.Replaced.Count} from the GRF); "
            + $"maps differing only in water flags (left alone): {r.WaterOnly}; maps not in the GRF: {r.Missing}");
        // sorted(replaced, key=lambda x: -x[1])[:12]: a stable sort, the largest change first
        foreach (var (name, d) in r.Replaced.Select((x, i) => (x, i)).OrderBy(t => -(long)t.x.WalkDiff).ThenBy(t => t.i).Select(t => t.x).Take(12))
            r.Report.Add(d >= 0 ? $"  {name}: {d} cells change walkability" : $"  {name}: size differs");
        return r;
    }

    /// <summary>
    /// mapcache_vs_grf.py water_level (rA src/common/grfio.cpp:459-494 grfio_read_rsw_water_level):
    ///   if rsw is None or rsw[:4] != b'GRSW': return RSW_NO_WATER, None
    ///   version = (rsw[4] &lt;&lt; 8) | rsw[5]
    ///   if version &lt; 0x104 or version > 0x205: return RSW_NO_WATER, version
    ///   at = 171 if version >= 0x205 else 167 if version >= 0x202 else 166
    ///   return int(struct.unpack_from('&lt;f', rsw, at)[0]), version   # (int32) cast truncates toward zero, as int() does
    /// The level is returned as a double holding Python's int exactly (every float32 truncates to a double exactly).
    /// </summary>
    public static double WaterLevel(byte[]? rsw, string name)
    {
        if (rsw == null || rsw.Length < 4 || rsw[0] != (byte)'G' || rsw[1] != (byte)'R' || rsw[2] != (byte)'S' || rsw[3] != (byte)'W') return RswNoWater;
        if (rsw.Length < 6) throw new InvalidDataException($"data\\{name}.rsw is cut short ({rsw.Length} bytes)"); // Python: IndexError
        int version = (rsw[4] << 8) | rsw[5];
        if (version < 0x104 || version > 0x205) return RswNoWater;
        int at = version >= 0x205 ? 171 : version >= 0x202 ? 167 : 166;
        if (at + 4 > rsw.Length) throw new InvalidDataException($"data\\{name}.rsw is cut short ({rsw.Length} bytes, water level at {at})"); // struct.error
        float f = BitConverter.ToSingle(rsw, at);
        if (float.IsNaN(f) || float.IsInfinity(f)) throw new InvalidDataException($"data\\{name}.rsw has no number as its water level"); // int(nan) / int(inf) raise
        return Math.Truncate((double)f);
    }

    /// <summary>
    /// mapcache_vs_grf.py grf_cells (rA src/tool/mapcache.cpp:66-113 read_map):
    ///   gat = grf.read('data\\%s.gat' % name)
    ///   if gat is None: return None
    ///   rsw = grf.read('data\\%s.rsw' % name); wl, rv = water_level(rsw)
    ///   xs = struct.unpack_from('&lt;i', gat, 6)[0]; ys = struct.unpack_from('&lt;i', gat, 10)[0]
    ///   out = bytearray(xs * ys)
    ///   # each cell: 4 corner heights (float) then the uint32 type; the first height is the one mapcache.cpp:102 reads
    ///   for i, (height, _h1, _h2, _h3, t) in enumerate(struct.iter_unpack('&lt;ffffI', gat[14:14 + 20 * xs * ys])):
    ///       if t == 0 and wl != RSW_NO_WATER and height > wl: t = 3
    ///       out[i] = t &amp; 0xFF
    /// A GAT shorter than its size says: Python fills the cells it finds and leaves the rest 0 when what is there is a whole
    /// number of cells, and raises struct.error otherwise; the same here.
    /// </summary>
    public static MapCache.Map? GrfCells(Func<string, byte[]?> read, string name)
    {
        var gat = read("data\\" + name + ".gat");
        if (gat == null) return null;
        var rsw = read("data\\" + name + ".rsw");
        double wl = WaterLevel(rsw, name);
        if (gat.Length < 14) throw new InvalidDataException($"data\\{name}.gat is cut short ({gat.Length} bytes)"); // struct.error
        int xs = BitConverter.ToInt32(gat, 6), ys = BitConverter.ToInt32(gat, 10);
        long n = (long)xs * ys;
        if (n < 0) throw new InvalidDataException($"data\\{name}.gat says {xs} x {ys} cells"); // bytearray(negative): ValueError
        if (n > int.MaxValue / 20) throw new InvalidDataException($"data\\{name}.gat says {xs} x {ys} cells"); // Python: MemoryError at best
        var cells = new byte[n];
        long have = Math.Max(0, Math.Min(gat.Length - 14L, 20 * n)); // len(gat[14:14 + 20 * xs * ys])
        if (have % 20 != 0) throw new InvalidDataException($"data\\{name}.gat is cut short (its cells end inside a cell)"); // struct.error
        bool water = wl != RswNoWater;
        int p = 14;
        for (long i = 0; i < have / 20; i++, p += 20)
        {
            uint t = BitConverter.ToUInt32(gat, p + 16);
            if (t == 0 && water && (double)BitConverter.ToSingle(gat, p) > wl) t = 3;
            cells[i] = (byte)(t & 0xFF);
        }
        return new MapCache.Map(name, xs, ys, cells);
    }

    /// <summary>What rAthena would refuse or misread in a cache it loads (rA src/map/map.cpp:3690-3699): a size of 0 or less,
    /// more than MAX_MAP_SIZE (map.hpp:82, 512*512) cells. The server then takes the map from its own cache; said, not fatal.</summary>
    public static List<string> ServerWouldRefuse(IEnumerable<MapCache.Map> maps)
    {
        var l = new List<string>();
        foreach (var m in maps)
        {
            if (m.Xs <= 0 || m.Ys <= 0) l.Add($"{m.Name}: {m.Xs} x {m.Ys} cells (rAthena refuses a size of 0 or less and keeps its own version of the map)");
            else if ((long)m.Xs * m.Ys > 512 * 512) l.Add($"{m.Name}: {m.Xs} x {m.Ys} cells, more than rAthena's MAX_MAP_SIZE 512*512 (it keeps its own version of the map)");
        }
        return l;
    }
}
