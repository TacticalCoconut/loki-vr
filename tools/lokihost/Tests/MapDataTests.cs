using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Loki.IO;
using Loki.Net;
using LokiHost.Core;
using Xunit;

namespace LokiHost.Tests;

/// <summary>
/// Client wave 7 (w7host): Loki Host follows the host's Ragnarok Online install (the map cache built in C# as the Python tools
/// build it, the safety checks, the rebuild at Start with the old file kept) and compares players' game data with the host's
/// over the relay. Everything here runs on made-up maps in temporary folders; the comparison with the Python tools on the
/// real install is in design notes (LokiMapTool), and <see cref="The_real_install_gives_the_python_tools_cache"/>
/// repeats it when its environment variables point at the files.
/// </summary>
public class MapDataTests
{
    // ------------------------------------------------------------------------------------------------ made-up game data

    static string Temp()
    {
        var d = Path.Combine(Path.GetTempPath(), "lokihost-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>A GAT: 14 header bytes (xs at 6, ys at 10), then per cell four float heights and the uint32 type.</summary>
    static byte[] Gat(int xs, int ys, Func<int, int, uint> type, float height = 0f)
    {
        var b = new byte[14 + 20 * xs * ys];
        Encoding.ASCII.GetBytes("GRAT").CopyTo(b, 0); b[4] = 1; b[5] = 2;
        BitConverter.TryWriteBytes(b.AsSpan(6), xs); BitConverter.TryWriteBytes(b.AsSpan(10), ys);
        for (int y = 0; y < ys; y++)
            for (int x = 0; x < xs; x++)
            {
                int p = 14 + 20 * (x + y * xs);
                for (int k = 0; k < 4; k++) BitConverter.TryWriteBytes(b.AsSpan(p + 4 * k), height);
                BitConverter.TryWriteBytes(b.AsSpan(p + 16), type(x, y));
            }
        return b;
    }

    /// <summary>An RSW of that version with its water level where rAthena reads it (166, 167 from 2.2, 171 from 2.5).</summary>
    static byte[] Rsw(byte major, byte minor, float level)
    {
        var b = new byte[200];
        Encoding.ASCII.GetBytes("GRSW").CopyTo(b, 0); b[4] = major; b[5] = minor;
        int v = major << 8 | minor, at = v >= 0x205 ? 171 : v >= 0x202 ? 167 : 166;
        BitConverter.TryWriteBytes(b.AsSpan(at), level);
        return b;
    }

    static byte[] Zlib(byte[] b)
    {
        var o = new MemoryStream();
        using (var z = new ZLibStream(o, CompressionLevel.Optimal, true)) z.Write(b);
        return o.ToArray();
    }

    /// <summary>
    /// A GRF 0x200 as the client's reader takes it (client IO/GrfArchive.cs ReadTable): 46 header bytes ("Master of Magic", the
    /// table's offset at 30, a seed at 34, the file count + seed + 7 at 38, the version at 42), each file's zlib stream, then
    /// the table (packed size, real size, zlib of: name NUL, packed, aligned, real, flags 1, offset).
    /// </summary>
    static void WriteGrf(string path, IEnumerable<(string Name, byte[] Data)> files)
    {
        var data = new MemoryStream(); var table = new MemoryStream();
        int count = 0;
        foreach (var (name, bytes) in files)
        {
            var comp = Zlib(bytes);
            int aligned = (comp.Length + 7) / 8 * 8;
            table.Write(Encoding.Latin1.GetBytes(name)); table.WriteByte(0);
            table.Write(BitConverter.GetBytes((uint)comp.Length)); table.Write(BitConverter.GetBytes((uint)aligned)); table.Write(BitConverter.GetBytes((uint)bytes.Length));
            table.WriteByte(1);
            table.Write(BitConverter.GetBytes((uint)data.Length));
            data.Write(comp); data.Write(new byte[aligned - comp.Length]);
            count++;
        }
        var packed = Zlib(table.ToArray());
        using var f = File.Create(path);
        var h = new byte[46];
        Encoding.ASCII.GetBytes("Master of Magic").CopyTo(h, 0);
        BitConverter.TryWriteBytes(h.AsSpan(30), (uint)data.Length);
        BitConverter.TryWriteBytes(h.AsSpan(34), 0u);
        BitConverter.TryWriteBytes(h.AsSpan(38), (uint)(count + 7));
        BitConverter.TryWriteBytes(h.AsSpan(42), 0x200u);
        f.Write(h); f.Write(data.ToArray());
        f.Write(BitConverter.GetBytes((uint)packed.Length)); f.Write(BitConverter.GetBytes((uint)table.Length)); f.Write(packed);
    }

    static byte[] Cells(int xs, int ys, Func<int, int, byte> type)
    {
        var c = new byte[xs * ys];
        for (int y = 0; y < ys; y++) for (int x = 0; x < xs; x++) c[x + y * xs] = type(x, y);
        return c;
    }

    static void WriteCache(string path, params MapCache.Map[] maps)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, MapCache.Write(maps));
    }

    /// <summary>
    /// A made-up host: rAthena's two caches (alpha in db/re; beta, gamma, delta, eps in db: all 10 × 10 open ground), the empty
    /// import template, one NPC file with a warp on beta and a script point, and an install whose data.grf has alpha as the
    /// cache has it, beta with a wall cell at (5,5), gamma under water (cells 3 where the cache has 0), delta 12 cells wide,
    /// no eps, and zeta, a map rAthena does not know.
    /// </summary>
    sealed class World
    {
        public readonly string Host = Temp(), Ro = Temp();
        public string Cache => MapDataWatch.ImportCachePath(Host);
        public string Grf => Path.Combine(Ro, "data.grf");
        public static readonly MapDataWatch.Options NoWait = new() { Quiet = TimeSpan.Zero };

        public World(Func<int, int, uint>? beta = null, string npc = "beta,5,2,0\twarp\tw1\t1,1,alpha,2,2\n-\tscript\tHelper\t-1,{\n\twarp \"beta\",2,7;\n}\n")
        {
            byte open(int x, int y) => 0;
            WriteCache(Path.Combine(Host, "db", "re", "map_cache.dat"), new MapCache.Map("alpha", 10, 10, Cells(10, 10, open)));
            WriteCache(Path.Combine(Host, "db", "map_cache.dat"), new MapCache.Map("beta", 10, 10, Cells(10, 10, open)), new MapCache.Map("gamma", 10, 10, Cells(10, 10, open)),
                new MapCache.Map("delta", 10, 10, Cells(10, 10, open)), new MapCache.Map("eps", 10, 10, Cells(10, 10, open)));
            WriteCache(Path.Combine(Host, "db", "import", "map_cache.dat")); // rAthena's template: a header, no map
            Directory.CreateDirectory(Path.Combine(Host, "npc", "re")); Directory.CreateDirectory(Path.Combine(Host, "conf"));
            File.WriteAllText(Path.Combine(Host, "npc", "re", "scripts_main.conf"), "// test\nnpc: npc/test.txt\nimport: npc/more.conf\n");
            File.WriteAllText(Path.Combine(Host, "npc", "more.conf"), "npc: npc/missing.txt // not there: skipped\n");
            File.WriteAllText(Path.Combine(Host, "npc", "test.txt"), npc);
            File.WriteAllText(Path.Combine(Host, "conf", "char_athena.conf"), "start_point: beta,1,1\n");
            WriteInstall(beta ?? ((x, y) => x == 5 && y == 5 ? 1u : 0u));
        }

        public void WriteInstall(Func<int, int, uint> beta, DateTime? written = null)
        {
            WriteGrf(Grf, new (string, byte[])[]
            {
                ("data\\alpha.gat", Gat(10, 10, (x, y) => 0)),
                ("data\\BETA.GAT", Gat(10, 10, beta)),                                   // entry names are found whatever their case
                ("data\\gamma.gat", Gat(10, 10, (x, y) => 0, height: 5f)), ("data\\gamma.rsw", Rsw(2, 1, 1f)),   // height 5 > water level 1: under water
                ("data\\delta.gat", Gat(12, 10, (x, y) => 0)),
                ("data\\zeta.gat", Gat(4, 4, (x, y) => 1)),
                ("data\\texture\\not_a_map.gat", new byte[] { 1, 2, 3 }),
            });
            File.SetLastWriteTimeUtc(Grf, written ?? DateTime.UtcNow.AddHours(-1));
        }
    }

    // ------------------------------------------------------------------------------------------------ the cache file

    [Fact]
    public void A_map_cache_is_written_and_read_as_rathena_lays_it_out()
    {
        var a = Cells(3, 2, (x, y) => (byte)(x + y)); var b = Cells(400, 400, (x, y) => (byte)((x * 7 + y) % 7));
        var file = MapCache.Write(new[] { new MapCache.Map("prontera", 3, 2, a), new MapCache.Map("1@tower", 400, 400, b) });
        // map.cpp:156-167: uint32 file_size, uint16 map_count, 2 bytes of padding; then char name[12], int16 xs, int16 ys, int32 len
        Assert.Equal((uint)file.Length, BitConverter.ToUInt32(file, 0));
        Assert.Equal(2, BitConverter.ToUInt16(file, 4));
        Assert.Equal("prontera\0\0\0\0", Encoding.ASCII.GetString(file, 8, 12));
        Assert.Equal(3, BitConverter.ToInt16(file, 20)); Assert.Equal(2, BitConverter.ToInt16(file, 22));
        int len = BitConverter.ToInt32(file, 24);
        Assert.Equal(0x78, file[28]); // a zlib stream, as rAthena's decode_zip (uncompress) wants
        Assert.Equal("1@tower", Encoding.ASCII.GetString(file, 28 + len, 7));
        var back = MapCache.Read(file, "test");
        Assert.Equal(new[] { "prontera", "1@tower" }, back.Keys);
        Assert.Equal(a, MapCache.Cells(back["prontera"])); Assert.Equal(b, MapCache.Cells(back["1@tower"]));
        // the content digest is what the server reads: the same for another zlib's stream of the same cells
        var stored = new MemoryStream();
        using (var z = new ZLibStream(stored, CompressionLevel.NoCompression, true)) z.Write(a);
        var other = MapCache.Write(new[] { new MapCache.Map("prontera", 3, 2, a), new MapCache.Map("1@tower", 400, 400, b) }).ToList();
        var alt = file.Take(28).Concat(stored.ToArray()).Concat(file.Skip(28 + len)).ToArray();
        BitConverter.TryWriteBytes(alt.AsSpan(24), (int)stored.Length);
        Assert.NotEqual(file, alt);
        Assert.Equal(MapCache.ContentDigest(file, "a"), MapCache.ContentDigest(alt, "b"));
        Assert.NotEqual(MapCache.ContentDigest(file, "a"), MapCache.ContentDigest(MapCache.Write(new[] { new MapCache.Map("prontera", 3, 2, Cells(3, 2, (x, y) => 1)) }), "c"));
    }

    [Fact]
    public void A_damaged_map_cache_is_refused_not_guessed_at()
    {
        var file = MapCache.Write(new[] { new MapCache.Map("alpha", 4, 4, Cells(4, 4, (x, y) => 0)) });
        Assert.Throws<InvalidDataException>(() => MapCache.Read(file.Take(20).ToArray(), "cut"));                 // an entry header cut off
        Assert.Throws<InvalidDataException>(() => MapCache.Read(file.Take(file.Length - 3).ToArray(), "cut"));    // its cells cut off
        var badSize = (byte[])file.Clone(); badSize[20] = 5;                                                       // 5 x 4 in the header, 16 cells inside
        Assert.Throws<InvalidDataException>(() => MapCache.Cells(MapCache.Read(badSize, "size")["alpha"]));
        var badStream = (byte[])file.Clone(); badStream[^1] ^= 0xFF;                                               // the stream's checksum
        Assert.Throws<InvalidDataException>(() => MapCache.Cells(MapCache.Read(badStream, "sum")["alpha"]));
        Assert.Throws<InvalidDataException>(() => MapCache.Write(new[] { new MapCache.Map("a_name_too_long", 1, 1, new byte[1]) })); // 12 bytes with the NUL
        Assert.Empty(MapCache.Read(new byte[] { 8, 0, 0, 0, 0, 0, 0, 0 }, "template"));                           // rAthena's import template
    }

    // ------------------------------------------------------------------------------------------------ the builder (build_import_cache.py)

    [Fact]
    public void The_builder_takes_from_the_install_exactly_the_maps_whose_walkability_differs()
    {
        var w = new World();
        // an import cache that already has an entry: kept, in its place, before the new ones
        WriteCache(w.Cache, new MapCache.Map("kept", 2, 2, new byte[] { 1, 1, 1, 1 }));
        MapCacheBuilder.Result r;
        using (var install = HostInstall.Open(w.Ro)) r = MapCacheBuilder.Build(w.Host, install.Read);
        Assert.Equal(new[] { "kept", "beta", "delta" }, r.Maps.Select(m => m.Name));
        Assert.Equal(new[] { ("beta", 1), ("delta", -1) }, r.Replaced);
        Assert.Equal(1, r.WaterOnly);  // gamma: 0 in the cache, 3 from the install: both walkable, left alone
        Assert.Equal(2, r.Missing);    // eps and kept: no map file in the install
        Assert.Equal(new[]
        {
            "wrote the new cache: 3 maps (1 kept from the import cache, 2 from the GRF); maps differing only in water flags (left alone): 1; maps not in the GRF: 2",
            "  beta: 1 cells change walkability",
            "  delta: size differs",
        }, r.Report);
        var back = MapCache.Read(r.File, "new");
        var beta = MapCache.Cells(back["beta"]);
        Assert.Equal(1, beta[5 + 5 * 10]); Assert.Equal(99, beta.Count(c => c == 0));
        Assert.Equal((12, 10), (back["delta"].Xs, back["delta"].Ys));
        Assert.Equal(new byte[] { 1, 1, 1, 1 }, MapCache.Cells(back["kept"]));
        Assert.False(back.ContainsKey("alpha") || back.ContainsKey("gamma") || back.ContainsKey("zeta"));

        // run again on its own output (the next patch day): beta and delta now equal the import cache's, nothing is taken anew
        File.WriteAllBytes(w.Cache, r.File);
        using (var install = HostInstall.Open(w.Ro))
        {
            var again = MapCacheBuilder.Build(w.Host, install.Read);
            Assert.Empty(again.Replaced);
            Assert.Equal(new[] { "kept", "beta", "delta" }, again.Maps.Select(m => m.Name));
            Assert.Equal(MapCache.ContentDigest(r.File, "a"), MapCache.ContentDigest(again.File, "b"));
        }
    }

    [Fact]
    public void Cells_follow_rathenas_own_conversion()
    {
        // mapcache.cpp:101-110: type 0 with a first corner height above the water level becomes 3; the low byte of the type is kept
        byte[]? read(string n) => n switch
        {
            "data\\m.gat" => Gat(2, 2, (x, y) => (uint)(x == 0 && y == 0 ? 0 : x == 1 && y == 0 ? 1 : x == 0 ? 0x105 : 5), height: 10f),
            "data\\m.rsw" => Rsw(2, 1, 3.9f),
            _ => null,
        };
        Assert.Equal(new byte[] { 3, 1, 5, 5 }, MapCacheBuilder.GrfCells(read, "m")!.Cells);
        Assert.Null(MapCacheBuilder.GrfCells(_ => null, "nowhere"));
        // grfio.cpp:473-490: the level is read for RSW 1.4 to 2.5 only, at 166 / 167 (2.2+) / 171 (2.5), cut to a whole number
        Assert.Equal(3, MapCacheBuilder.WaterLevel(Rsw(1, 4, 3.9f), "m"));
        Assert.Equal(-3, MapCacheBuilder.WaterLevel(Rsw(2, 2, -3.9f), "m"));
        Assert.Equal(7, MapCacheBuilder.WaterLevel(Rsw(2, 5, 7f), "m"));
        Assert.Equal(MapCacheBuilder.RswNoWater, MapCacheBuilder.WaterLevel(Rsw(1, 3, 3f), "m"));
        Assert.Equal(MapCacheBuilder.RswNoWater, MapCacheBuilder.WaterLevel(Rsw(2, 6, 3f), "m"));
        Assert.Equal(MapCacheBuilder.RswNoWater, MapCacheBuilder.WaterLevel(null, "m"));
        Assert.Equal(MapCacheBuilder.RswNoWater, MapCacheBuilder.WaterLevel(Encoding.ASCII.GetBytes("nope, not an rsw"), "m"));
        // height 3.5 against level 3 (3.9 cut): under water; height 3 is not above 3
        byte[]? at(float h) => MapCacheBuilder.GrfCells(n => n.EndsWith(".gat") ? Gat(1, 1, (x, y) => 0, h) : Rsw(2, 1, 3.9f), "m")!.Cells;
        Assert.Equal(new byte[] { 3 }, at(3.5f)); Assert.Equal(new byte[] { 0 }, at(3f));
        // a GAT cut short: whole cells that are there count, the rest stay 0 (Python's slice); a cell cut in the middle is an error
        var whole = Gat(2, 2, (x, y) => 1);
        Assert.Equal(new byte[] { 1, 1, 0, 0 }, MapCacheBuilder.GrfCells(n => n.EndsWith(".gat") ? whole.Take(14 + 40).ToArray() : null, "m")!.Cells);
        Assert.Throws<InvalidDataException>(() => MapCacheBuilder.GrfCells(n => n.EndsWith(".gat") ? whole.Take(14 + 47).ToArray() : null, "m"));
        Assert.Throws<InvalidDataException>(() => MapCacheBuilder.GrfCells(n => n.EndsWith(".gat") ? whole.Take(9).ToArray() : null, "m"));
        Assert.Throws<InvalidDataException>(() => MapCacheBuilder.GrfCells(n => n.EndsWith(".gat") ? whole : Rsw(2, 1, float.NaN), "m"));
        Assert.Contains("MAX_MAP_SIZE", MapCacheBuilder.ServerWouldRefuse(new[] { new MapCache.Map("big", 600, 600, new byte[360000]) }).Single());
    }

    // ------------------------------------------------------------------------------------------------ the checks (check_points.py)

    [Fact]
    public void The_points_check_finds_what_a_new_cache_would_lose_or_cut_off()
    {
        var w = new World(npc:
            "beta,5,2,0\twarp\tw1\t1,1,alpha,2,2\n" +             // a warp whose touch area (4..6, 1..3) the wall crosses: smaller, not lost
            "beta,5,8,0\twarp2\tw2\t0,0,beta,8,5\n" +             // a warp exactly on the wall: area lost; it lands beyond the wall: cut off
            "// beta,1,1,0\twarp\tignored\t1,1,beta,5,5\n" +      // a comment line
            "beta,2,2,4\tscript\tTouchy#1\t4_M_01,2,2,{\n" +      // an OnTouch area, untouched
            "\tsavepoint \"beta\",5,5; warp \"Beta\",5,6; warp \"gamma\",1,1;\n" + // a script point on the wall; \"Beta\" is no map; gamma is not replaced
            "beta,7,7,2,2\tmonster\tPoring\t1002,5,0,0\n");       // a spawn area beyond the wall
        // the new cache: beta with a wall down the middle (x = 5), 5 columns left of it and 4 right
        var cache = MapCache.Write(new[] { new MapCache.Map("beta", 10, 10, Cells(10, 10, (x, y) => (byte)(x == 5 ? 1 : 0))) });
        var r = PointsCheck.Run(w.Host, cache, null);
        Assert.Equal(new[]
        {
            "(no rAthena source dir given: the @go points are not checked)",
            "2 NPC files; 7 points, 4 areas in all maps",           // (the tool counts the listed files, also one that is not there)
            "on the replaced maps: 1 OnTouch, 1 script, 1 spawn, 1 start_point, 3 warp",
            "NEWLY BLOCKED point beta 5,5: script \"beta\",5,5 (npc/test.txt:5)",
            "  (area smaller beta 5,2 +-1,1: 9 -> 6: warp w1 touch area (npc/test.txt:1))",
            "AREA LOST beta 5,8 +-0,0: walkable cells 1 -> 0: warp w2 touch area (npc/test.txt:2)",
            "  (area smaller beta 7,7 +-2,2: 25 -> 20: spawn Poring (npc/test.txt:6))",
            "checked on the 1 replaced maps: 7; unreachable with the new cache (and reachable before): 2",
            "CUT OFF from the main region beta 8,5 +-1,1: warp w2 lands (npc/test.txt:2)",
            "CUT OFF from the main region beta 5,8 +-0,0: warp w2 touch area (npc/test.txt:2)",
            "CUT OFF from the main region beta 7,7 +-2,2: spawn Poring (npc/test.txt:6)",
            "reachability: 7 checked; reachable from the main region before and not with the new cache: 3",
            "  main region of beta: 100 -> 50 cells",
        }, r.Lines);
        Assert.Equal((2, 3, false), (r.Bad, r.Lost, r.Clean));

        // the same map with one blocked cell nothing stands on: clean
        var fine = MapCache.Write(new[] { new MapCache.Map("beta", 10, 10, Cells(10, 10, (x, y) => (byte)(x == 9 && y == 9 ? 1 : 0))) });
        var ok = PointsCheck.Run(w.Host, fine, null);
        Assert.True(ok.Clean, string.Join("\n", ok.Lines));
        Assert.Equal(7, ok.Checked);
        // a point on a map that only the new cache has (rAthena's own caches do not know it) cannot be compared: an error, not a
        // pass (the Python tool stops there with a KeyError)
        File.AppendAllText(Path.Combine(w.Host, "npc", "test.txt"), "\twarp \"nowhere\",1,1;\n");
        var unknown = MapCache.Write(new[] { new MapCache.Map("beta", 10, 10, new byte[100]), new MapCache.Map("nowhere", 2, 2, new byte[4]) });
        Assert.Throws<InvalidDataException>(() => PointsCheck.Run(w.Host, unknown, null));
    }

    // ------------------------------------------------------------------------------------------------ the install and the data stamp

    [Fact]
    public void The_data_stamp_is_of_the_cells_not_of_the_files()
    {
        var w = new World();
        MapDataStamp.Result a;
        using (var install = HostInstall.Open(w.Ro))
        {
            Assert.Equal(new[] { "data\\alpha.gat", "data\\beta.gat", "data\\delta.gat", "data\\gamma.gat", "data\\zeta.gat" }, install.MapGatNames().OrderBy(n => n, StringComparer.Ordinal));
            a = install.DataStamp();
        }
        Assert.Matches("^g1-[0-9a-f]{16}$", a.Stamp);
        Assert.True(PresenceWire.ValidStamp(a.Stamp));
        Assert.Equal((5, 0), (a.Maps, a.Unreadable));
        Assert.Equal(100 + 100 + 100 + 120 + 16, a.Cells);

        // the same maps in another folder, written at another time, in another order, at another height: the same stamp
        var other = Temp();
        WriteGrf(Path.Combine(other, "data.grf"), new (string, byte[])[]
        {
            ("data\\zeta.gat", Gat(4, 4, (x, y) => 1, height: -30f)), ("data\\delta.gat", Gat(12, 10, (x, y) => 0)), ("data\\gamma.gat", Gat(10, 10, (x, y) => 0)),
            ("data\\beta.gat", Gat(10, 10, (x, y) => x == 5 && y == 5 ? 1u : 0u)), ("data\\alpha.gat", Gat(10, 10, (x, y) => 0)), ("data\\sprite\\x.spr", new byte[40]),
        });
        using (var install = HostInstall.Open(other)) Assert.Equal(a.Stamp, install.DataStamp().Stamp);
        Assert.NotEqual(HostInstall.StampText(w.Ro), HostInstall.StampText(other));

        // one cell of one map differs: another stamp; so does a map more, and a map that cannot be read
        w.WriteInstall((x, y) => x == 5 && y == 6 ? 1u : 0u);
        string changed;
        using (var install = HostInstall.Open(w.Ro)) changed = install.DataStamp().Stamp;
        Assert.NotEqual(a.Stamp, changed);
        Assert.Equal(a.Stamp, MapDataStamp.Compute(new[] { "data\\m.gat", "data\\m.gat", "data\\other\\m.gat", "data\\m.rsw" }, _ => Gat(1, 1, (x, y) => 0)).Stamp == a.Stamp ? "" : a.Stamp);
        var one = MapDataStamp.Compute(new[] { "data\\m.gat", "data\\m.gat", "data\\texture\\m.gat", "data\\m.rsw" }, _ => Gat(1, 1, (x, y) => 0));
        Assert.Equal((1, 0), (one.Maps, one.Unreadable)); // repeats and other files are not maps
        var broken = MapDataStamp.Compute(new[] { "data\\m.gat" }, _ => throw new InvalidDataException("damaged"));
        Assert.Equal((1, 1), (broken.Maps, broken.Unreadable));
        Assert.Equal("m", broken.UnreadableNames.Single());
        Assert.NotEqual(one.Stamp, broken.Stamp);
        Assert.Equal(broken.Stamp, MapDataStamp.Compute(new[] { "data\\m.gat" }, _ => new byte[13]).Stamp); // too short to be a map: the same "unreadable"

        // the patch marker: patch.inf, 4 bytes, little-endian (the LATAM install: ce 05 00 00 = 1486)
        Assert.Equal("", HostInstall.PatchLabel(w.Ro));
        File.WriteAllBytes(Path.Combine(w.Ro, "patch.inf"), new byte[] { 0xce, 0x05, 0, 0 });
        Assert.Equal("patch 1486", HostInstall.PatchLabel(w.Ro));
        Assert.Equal(1486, MapDataStamp.PatchOf("patch 1486"));
        Assert.Equal(-1, MapDataStamp.PatchOf("1486")); Assert.Equal(-1, MapDataStamp.PatchOf(""));
        Assert.Contains("\npatch 1486\n", HostInstall.StampText(w.Ro));
        File.WriteAllBytes(Path.Combine(w.Ro, "patch.inf"), new byte[] { 1, 2, 3 });
        Assert.Equal("", HostInstall.PatchLabel(w.Ro));
    }

    [Fact]
    public void A_loose_map_file_wins_over_the_grf_as_in_the_client()
    {
        var w = new World();
        string before;
        using (var install = HostInstall.Open(w.Ro)) before = install.DataStamp().Stamp;
        Directory.CreateDirectory(Path.Combine(w.Ro, "data"));
        File.WriteAllBytes(Path.Combine(w.Ro, "data", "beta.gat"), Gat(10, 10, (x, y) => 1));
        File.WriteAllBytes(Path.Combine(w.Ro, "data", "theta.gat"), Gat(3, 3, (x, y) => 0));
        using var loose = HostInstall.Open(w.Ro);
        Assert.Equal(100, MapCacheBuilder.GrfCells(loose.Read, "beta")!.Cells.Count(c => c == 1));
        Assert.Contains("data\\theta.gat", loose.MapGatNames());
        Assert.NotEqual(before, loose.DataStamp().Stamp);
        Assert.Contains("\nloose beta.gat ", HostInstall.StampText(w.Ro));
    }

    /// <summary>The stamp of maps whose bytes the test knows, each given once: what the client's rule must come to.</summary>
    static string StampOf(params (string Name, byte[] Gat)[] maps) =>
        MapDataStamp.Compute(maps.Select(m => "data\\" + m.Name + ".gat"), n => maps.First(m => "data\\" + m.Name + ".gat" == n).Gat).Stamp;

    [Fact]
    public void The_host_reads_the_install_by_the_clients_own_rule()
    {
        // Client wave 7, the merge of w7data and w7host: Loki Host opens the install through the client's RoInstall / RoLookup
        // (Core/HostInstall.cs). The archives in DATA.INI's order, the loose folder where the clientinfo's readfolder tag puts
        // it, an unknown-version or absent archive passed over as the client passes over it; a damaged, foreign or busy
        // archive refuses the whole install, and a damaged entry stops the builder.
        var w = new World(); // data.grf: alpha, beta (one wall cell at 5,5), gamma, delta (12 wide), zeta (4 x 4, all wall)
        string first = Path.Combine(w.Ro, "first.grf"), ev = Path.Combine(w.Ro, "event.grf");
        byte[] alpha = Gat(10, 10, (x, y) => 0), betaData = Gat(10, 10, (x, y) => x == 5 && y == 5 ? 1u : 0u), gamma = Gat(10, 10, (x, y) => 0),
            delta = Gat(12, 10, (x, y) => 0), zeta = Gat(4, 4, (x, y) => 1), theta = Gat(3, 3, (x, y) => 0),
            betaFirst = Gat(10, 10, (x, y) => (x == 5 && y == 5) || (x == 0 && y == 9) ? 1u : 0u);
        WriteGrf(first, new (string, byte[])[] { ("data\\beta.gat", betaFirst), ("data\\theta.gat", theta) });
        WriteGrf(ev, new (string, byte[])[] { ("data\\beta.gat", Gat(2, 2, (x, y) => 5)) });
        var raw = File.ReadAllBytes(ev); BitConverter.TryWriteBytes(raw.AsSpan(42), 0x102u); File.WriteAllBytes(ev, raw); // the LATAM install's event.grf is GRF 0x102

        // no DATA.INI: data.grf alone, whatever else lies in the folder
        Assert.Equal(new[] { "data.grf" }, HostInstall.GrfNames(w.Ro));
        using (var install = HostInstall.Open(w.Ro))
        {
            Assert.Equal(new[] { "data.grf" }, install.Grfs.Select(g => Path.GetFileName(g.Path)));
            Assert.Empty(install.Skipped);
            Assert.Equal(1, MapCacheBuilder.GrfCells(install.Read, "beta")!.Cells.Count(c => c == 1));
            Assert.Equal(StampOf(("alpha", alpha), ("beta", betaData), ("gamma", gamma), ("delta", delta), ("zeta", zeta)), install.DataStamp().Stamp);
        }

        // a DATA.INI: its numbers ascending, the first archive that holds a file wins; event.grf (a version the client's reader
        // does not know) and gone.grf (not in the folder) are passed over, as the client passes over them
        File.WriteAllText(Path.Combine(w.Ro, "DATA.INI"), "[Data]\r\n2=event.grf\r\n0=first.grf\r\n1=data.grf\r\n3=gone.grf\r\n");
        Assert.Equal(new[] { "first.grf", "data.grf", "event.grf", "gone.grf" }, HostInstall.GrfNames(w.Ro));
        var log = new List<string>();
        using (var install = HostInstall.Open(w.Ro, log.Add))
        {
            Assert.Equal("DATA.INI", install.Install.OrderSource);
            Assert.Equal(HostInstall.GrfNames(w.Ro), install.Install.GrfNames);
            Assert.Equal(new[] { "first.grf", "data.grf" }, install.Grfs.Select(g => Path.GetFileName(g.Path)));
            Assert.Equal(new[] { "event.grf UnknownVersion", "gone.grf missing" }, install.Skipped.Select(k => k.Name + " " + k.Kind));
            Assert.Equal(2, MapCacheBuilder.GrfCells(install.Read, "beta")!.Cells.Count(c => c == 1)); // first.grf's beta, not data.grf's
            Assert.Contains("data\\theta.gat", install.MapGatNames());
            Assert.Equal(StampOf(("alpha", alpha), ("beta", betaFirst), ("gamma", gamma), ("delta", delta), ("zeta", zeta), ("theta", theta)), install.DataStamp().Stamp);
        }
        Assert.Contains(log, l => l.Contains("DATA.INI lists 4 archive(s), read in this order: first.grf, data.grf, event.grf, gone.grf"));
        Assert.Contains(log, l => l.StartsWith("not read: event.grf is GRF version 0x102"));
        Assert.Contains(log, l => l.StartsWith("not read: gone.grf is not in the install folder"));
        Assert.Matches(@"\ngrf first\.grf \d+ \d+ 0x200\ngrf data\.grf \d+ \d+ 0x200\ngrf event\.grf \d+ \d+ 0x100\ngrf gone\.grf missing\n", HostInstall.StampText(w.Ro));

        // the check at Start follows that order: the cache gets first.grf's beta
        log.Clear();
        var start = MapDataWatch.Check(w.Host, w.Ro, log.Add, World.NoWait);
        Assert.True(start.Kind == MapDataWatch.Kind.Rebuilt, start.Summary);
        Assert.Equal(new[] { "beta", "delta" }, start.Changed);
        Assert.Equal(1, MapCache.Cells(MapCache.Read(File.ReadAllBytes(w.Cache), "built")["beta"])[0 + 9 * 10]);
        Assert.Contains(log, l => l.StartsWith("Map data: your install's DATA.INI lists 4 archive(s)"));

        // a damaged ENTRY in the first archive: the builder's read throws (the rebuild then keeps the old cache), while the data
        // stamp reads on as the client's file system does (the next source that has the map)
        WriteGrf(first, new (string, byte[])[] { ("data\\zeta.gat", Gat(10, 10, (x, y) => 0)), ("data\\beta.gat", betaFirst), ("data\\theta.gat", theta) });
        raw = File.ReadAllBytes(first);
        for (int i = 46 + 4; i < 46 + 24; i++) raw[i] ^= 0x5A; // inside zeta.gat's zlib stream
        File.WriteAllBytes(first, raw);
        using (var install = HostInstall.Open(w.Ro))
        {
            Assert.Contains("data\\zeta.gat in first.grf is damaged", Assert.Throws<GrfEntryException>(() => install.Read("data\\zeta.gat")).Message);
            var s = install.DataStamp();
            Assert.Equal((6, 0), (s.Maps, s.Unreadable));
            Assert.Equal(StampOf(("alpha", alpha), ("beta", betaFirst), ("gamma", gamma), ("delta", delta), ("zeta", zeta), ("theta", theta)), s.Stamp); // data.grf's zeta
        }

        // the loose data\ folder. No clientinfo in the game data: loose files first (as before the wave)
        WriteGrf(first, new (string, byte[])[] { ("data\\beta.gat", betaFirst), ("data\\theta.gat", theta) });
        Directory.CreateDirectory(Path.Combine(w.Ro, "data"));
        byte[] betaLoose = Gat(10, 10, (x, y) => x == 0 ? 1u : 0u), iota = Gat(2, 2, (x, y) => 0);
        File.WriteAllBytes(Path.Combine(w.Ro, "data", "beta.gat"), betaLoose);
        File.WriteAllBytes(Path.Combine(w.Ro, "data", "iota.gat"), iota);
        using (var install = HostInstall.Open(w.Ro))
        {
            Assert.True(install.Lookup.LooseFirst);
            Assert.Equal(10, MapCacheBuilder.GrfCells(install.Read, "beta")!.Cells.Count(c => c == 1));
        }
        // a clientinfo without the readfolder tag: the archives first, a loose file only for what no archive has
        WriteGrf(first, new (string, byte[])[] { ("data\\beta.gat", betaFirst), ("data\\theta.gat", theta), ("data\\clientinfo.xml", Encoding.ASCII.GetBytes("<clientinfo><desc>test</desc></clientinfo>")) });
        log.Clear();
        using (var install = HostInstall.Open(w.Ro, log.Add))
        {
            Assert.False(install.Lookup.LooseFirst);
            Assert.Equal(2, MapCacheBuilder.GrfCells(install.Read, "beta")!.Cells.Count(c => c == 1));
            Assert.Equal(iota, install.Read("data\\iota.gat"));
            Assert.Equal(StampOf(("alpha", alpha), ("beta", betaFirst), ("gamma", gamma), ("delta", delta), ("zeta", zeta), ("theta", theta), ("iota", iota)), install.DataStamp().Stamp);
        }
        Assert.Contains(log, l => l.Contains("are read after the archives (data\\clientinfo.xml has no readfolder tag)"));
        // with the tag (the LATAM install's clientinfo has it): loose files first
        WriteGrf(first, new (string, byte[])[] { ("data\\beta.gat", betaFirst), ("data\\theta.gat", theta), ("data\\clientinfo.xml", Encoding.ASCII.GetBytes("<clientinfo><readfolder></readfolder></clientinfo>")) });
        log.Clear();
        using (var install = HostInstall.Open(w.Ro, log.Add))
        {
            Assert.True(install.Lookup.LooseFirst);
            Assert.Equal(10, MapCacheBuilder.GrfCells(install.Read, "beta")!.Cells.Count(c => c == 1));
            Assert.Equal(StampOf(("alpha", alpha), ("beta", betaLoose), ("gamma", gamma), ("delta", delta), ("zeta", zeta), ("theta", theta), ("iota", iota)), install.DataStamp().Stamp);
        }
        Assert.Contains(log, l => l.Contains("are read before the archives (data\\clientinfo.xml has the readfolder tag)"));
        // a loose clientinfo is part of the install's stamp (it can move the loose folder's place)
        string before = HostInstall.StampText(w.Ro);
        File.WriteAllText(Path.Combine(w.Ro, "data", "sclientinfo.xml"), "<clientinfo></clientinfo>");
        Assert.NotEqual(before, HostInstall.StampText(w.Ro));
        Assert.Contains("\nloose sclientinfo.xml ", HostInstall.StampText(w.Ro));

        // a damaged archive of the order refuses the whole install (the client would skip it and play on with the rest: the
        // host must not build the server's maps from part of an install), with the client's own reason …
        var good = File.ReadAllBytes(first);
        File.WriteAllBytes(first, good.Take(good.Length / 2).ToArray());
        var cut = Assert.Throws<InvalidDataException>(() => HostInstall.Open(w.Ro));
        Assert.StartsWith("first.grf cannot be read (it has ", cut.Message);
        Assert.Contains("cut short or still being written", cut.Message);
        // … and nothing holds the file afterwards: the official patcher could write it at once. (main's GrfArchive left it
        // open until the garbage collector ran, and w7host's HostInstall forced a collection; w7data's GrfArchive closes the
        // file itself when its constructor fails, so no collection is forced any more.)
        using (new FileStream(first, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        using (new FileStream(w.Grf, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } // nor the good archive opened beside it
        // not a GRF at all
        File.WriteAllText(first, "this is not a GRF, it only has the name, and it is long enough to hold a header of forty-six bytes");
        Assert.StartsWith("first.grf cannot be read (it is not a GRF", Assert.Throws<InvalidDataException>(() => HostInstall.Open(w.Ro)).Message);
        using (new FileStream(first, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        // held open for writing by another program (the patcher at work)
        File.WriteAllBytes(first, good);
        using (new FileStream(first, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            Assert.StartsWith("first.grf cannot be read (it cannot be opened", Assert.Throws<IOException>(() => HostInstall.Open(w.Ro)).Message);
        using (new FileStream(w.Grf, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        // the check at Start says so and keeps the cache
        File.WriteAllBytes(first, good.Take(good.Length / 2).ToArray());
        var kept = File.ReadAllBytes(w.Cache);
        var failed = MapDataWatch.Check(w.Host, w.Ro, log.Add, World.NoWait);
        Assert.True(failed.Kind == MapDataWatch.Kind.Failed, failed.Summary);
        Assert.Contains("first.grf cannot be read", failed.Reason);
        Assert.Equal(kept, File.ReadAllBytes(w.Cache));
        // whole again: the install opens
        File.WriteAllBytes(first, good);
        using (var install = HostInstall.Open(w.Ro)) Assert.Equal(2, install.Grfs.Count);
    }

    // ------------------------------------------------------------------------------------------------ the check at Start

    [Fact]
    public void Start_rebuilds_the_cache_when_the_install_changed_and_only_then()
    {
        var w = new World();
        var template = File.ReadAllBytes(w.Cache);
        var log = new List<string>();

        // no folder set: nothing is done, nothing is compared
        var off = MapDataWatch.Check(w.Host, "", log.Add, World.NoWait);
        Assert.Equal((MapDataWatch.Kind.Off, ""), (off.Kind, off.DataStamp));
        Assert.Equal(template, File.ReadAllBytes(w.Cache));

        // the first Start with the folder: the cache is made, the template kept beside it
        var first = MapDataWatch.Check(w.Host, w.Ro, log.Add, World.NoWait);
        Assert.True(first.Kind == MapDataWatch.Kind.Rebuilt, first.Summary);
        Assert.Equal(new[] { "beta", "delta" }, first.Changed);
        Assert.NotNull(first.KeptOldAs);
        Assert.Equal(template, File.ReadAllBytes(first.KeptOldAs!));
        Assert.StartsWith(w.Cache + ".before-", first.KeptOldAs);
        var built = File.ReadAllBytes(w.Cache);
        Assert.Equal(new[] { "beta", "delta" }, MapCache.Read(built, "built").Keys);
        Assert.Matches("^g1-[0-9a-f]{16}$", first.DataStamp);
        Assert.Contains("check: checked on the 2 replaced maps: 3; unreachable with the new cache (and reachable before): 0", string.Join("\n", log));
        Assert.Contains("REBUILT", first.Summary);
        Assert.True(File.Exists(MapDataWatch.StampPath(w.Host)));

        // (a) nothing changed: no rebuild, not a byte touched
        var time = File.GetLastWriteTimeUtc(w.Cache); var stampTime = File.GetLastWriteTimeUtc(MapDataWatch.StampPath(w.Host));
        log.Clear();
        var second = MapDataWatch.Check(w.Host, w.Ro, log.Add, World.NoWait);
        Assert.True(second.Kind == MapDataWatch.Kind.Unchanged, second.Summary);
        Assert.Empty(log);
        Assert.Equal((time, stampTime), (File.GetLastWriteTimeUtc(w.Cache), File.GetLastWriteTimeUtc(MapDataWatch.StampPath(w.Host))));
        Assert.Equal(first.DataStamp, second.DataStamp);
        Assert.Equal(first.DataStamp, MapDataWatch.KeptDataStamp(w.Host).Stamp);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(w.Cache)!, "map_cache.dat.before-*"));

        // the patcher ran, no map changed (the GRF has a new time): the file stays, the stamp is renewed
        File.SetLastWriteTimeUtc(w.Grf, DateTime.UtcNow.AddMinutes(-30));
        var same = MapDataWatch.Check(w.Host, w.Ro, log.Add, World.NoWait);
        Assert.True(same.Kind == MapDataWatch.Kind.SameMaps, same.Summary);
        Assert.Equal(built, File.ReadAllBytes(w.Cache));
        Assert.Equal(time, File.GetLastWriteTimeUtc(w.Cache));
        Assert.Equal(MapDataWatch.Kind.Unchanged, MapDataWatch.Check(w.Host, w.Ro, log.Add, World.NoWait).Kind);

        // (b) a patch changed a map: rebuilt, the old cache kept beside the new one, the players' reference stamp follows
        w.WriteInstall((x, y) => (x == 5 && y == 5) || (x == 0 && y == 9) ? 1u : 0u, DateTime.UtcNow.AddMinutes(-10));
        log.Clear();
        var patched = MapDataWatch.Check(w.Host, w.Ro, log.Add, World.NoWait);
        Assert.True(patched.Kind == MapDataWatch.Kind.Rebuilt, patched.Summary);
        Assert.Equal(new[] { "beta" }, patched.Changed);               // delta is as the cache already had it
        Assert.Equal(built, File.ReadAllBytes(patched.KeptOldAs!));
        Assert.NotEqual(first.DataStamp, patched.DataStamp);
        var rebuilt = File.ReadAllBytes(w.Cache);
        Assert.Equal(1, MapCache.Cells(MapCache.Read(rebuilt, "rebuilt")["beta"])[0 + 9 * 10]);
        Assert.Equal(2, Directory.GetFiles(Path.GetDirectoryName(w.Cache)!, "map_cache.dat.before-*").Length);
        Assert.Contains("Ragnarok Online install changed since the map cache was made", log[0]);

        // a file put there by hand is noticed too (it is not the one the stamp describes)
        File.WriteAllBytes(w.Cache, template);
        var byHand = MapDataWatch.Check(w.Host, w.Ro, log.Add, World.NoWait);
        Assert.True(byHand.Kind == MapDataWatch.Kind.Rebuilt, byHand.Summary);
        Assert.Equal(MapCache.ContentDigest(rebuilt, "a"), MapCache.ContentDigest(File.ReadAllBytes(w.Cache), "b"));
    }

    [Fact]
    public void A_failed_rebuild_keeps_the_old_cache_and_says_why()
    {
        var w = new World();
        var log = new List<string>();
        var first = MapDataWatch.Check(w.Host, w.Ro, log.Add, World.NoWait);
        Assert.Equal(MapDataWatch.Kind.Rebuilt, first.Kind);
        var good = File.ReadAllBytes(w.Cache); var stamp = File.ReadAllText(MapDataWatch.StampPath(w.Host));
        var goodGrf = File.ReadAllBytes(w.Grf);
        void Unchanged() { Assert.Equal(good, File.ReadAllBytes(w.Cache)); Assert.Equal(stamp, File.ReadAllText(MapDataWatch.StampPath(w.Host))); }

        // (c) a damaged GRF: cut short (its file table is gone)
        File.WriteAllBytes(w.Grf, goodGrf.Take(goodGrf.Length / 2).ToArray());
        File.SetLastWriteTimeUtc(w.Grf, DateTime.UtcNow.AddMinutes(-5));
        var cut = MapDataWatch.Check(w.Host, w.Ro, log.Add, World.NoWait);
        Assert.True(cut.Kind == MapDataWatch.Kind.Failed, cut.Summary);
        Assert.Contains("data.grf cannot be read", cut.Reason);
        Assert.Contains("NOT rebuilt, the map cache in place is kept", cut.Summary);
        Assert.Equal(first.DataStamp, cut.DataStamp); // players are still compared with what the cache in place was made from
        Unchanged();

        // not a GRF at all; no GRF; no folder
        File.WriteAllText(w.Grf, "this is not a GRF, it only has the name, and it is long enough to hold a header of forty-six bytes");
        File.SetLastWriteTimeUtc(w.Grf, DateTime.UtcNow.AddMinutes(-5));
        Assert.Equal(MapDataWatch.Kind.Failed, MapDataWatch.Check(w.Host, w.Ro, log.Add, World.NoWait).Kind); Unchanged();
        File.Move(w.Grf, w.Grf + ".away");
        var none = MapDataWatch.Check(w.Host, w.Ro, log.Add, World.NoWait);
        Assert.Equal(MapDataWatch.Kind.Failed, none.Kind); Assert.Contains("no data.grf", none.Reason); Unchanged();
        Assert.Contains("does not exist", MapDataWatch.Check(w.Host, Path.Combine(w.Ro, "nowhere"), log.Add, World.NoWait).Reason); Unchanged();

        // a damaged map inside a whole GRF: the entry's stream is broken (Python's tool raises there too)
        var files = new List<(string, byte[])> { ("data\\alpha.gat", Gat(10, 10, (x, y) => 0)), ("data\\beta.gat", Gat(10, 10, (x, y) => 1)) };
        WriteGrf(w.Grf, files);
        var raw = File.ReadAllBytes(w.Grf);
        for (int i = 46 + 4; i < 46 + 24; i++) raw[i] ^= 0x5A; // inside alpha.gat's zlib stream
        File.WriteAllBytes(w.Grf, raw); File.SetLastWriteTimeUtc(w.Grf, DateTime.UtcNow.AddMinutes(-5));
        var entry = MapDataWatch.Check(w.Host, w.Ro, log.Add, World.NoWait);
        Assert.True(entry.Kind == MapDataWatch.Kind.Failed, entry.Summary); Unchanged();

        // maps that would lose a warp: the checks refuse them (beta all wall: its warp area and the script point are gone)
        WriteGrf(w.Grf, new (string, byte[])[] { ("data\\alpha.gat", Gat(10, 10, (x, y) => 0)), ("data\\beta.gat", Gat(10, 10, (x, y) => 1)) });
        File.SetLastWriteTimeUtc(w.Grf, DateTime.UtcNow.AddMinutes(-5));
        log.Clear();
        var lost = MapDataWatch.Check(w.Host, w.Ro, log.Add, World.NoWait);
        Assert.True(lost.Kind == MapDataWatch.Kind.Failed, lost.Summary);
        Assert.Contains("would lose", lost.Reason);
        Assert.Contains(log, l => l.Contains("AREA LOST beta 5,2"));
        Unchanged();

        // the good file back: the next Start goes through (the stamp was never renewed by a failure)
        File.WriteAllBytes(w.Grf, goodGrf); File.SetLastWriteTimeUtc(w.Grf, DateTime.UtcNow.AddMinutes(-5));
        var again = MapDataWatch.Check(w.Host, w.Ro, log.Add, World.NoWait);
        Assert.True(again.Kind == MapDataWatch.Kind.SameMaps, again.Summary);
        Assert.Equal(good, File.ReadAllBytes(w.Cache));
    }

    [Fact]
    public void A_patch_in_progress_is_waited_for_and_never_built_from()
    {
        var w = new World();
        var log = new List<string>();
        // written 10 s ago, "quiet" asks for 60 s: the check waits (the clock here is the test's), then builds
        var now = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(w.Grf, now.AddSeconds(-10));
        var slept = TimeSpan.Zero;
        var opt = new MapDataWatch.Options { Quiet = TimeSpan.FromSeconds(60), MaxWait = TimeSpan.FromSeconds(120), UtcNow = () => now + slept, Sleep = t => slept += t };
        var o = MapDataWatch.Check(w.Host, w.Ro, log.Add, opt);
        Assert.True(o.Kind == MapDataWatch.Kind.Rebuilt, o.Summary);
        Assert.InRange(slept.TotalSeconds, 50, 56);
        Assert.Contains(log, l => l.Contains("the official patcher may still be at work"));

        // a patcher that keeps writing: after the longest wait the old cache stays, and the next Start tries again
        var good = File.ReadAllBytes(w.Cache);
        w.WriteInstall((x, y) => 1, now);
        slept = TimeSpan.Zero;
        var busy = new MapDataWatch.Options
        {
            Quiet = TimeSpan.FromSeconds(60), MaxWait = TimeSpan.FromSeconds(30), UtcNow = () => now + slept,
            Sleep = t => { slept += t; File.SetLastWriteTimeUtc(w.Grf, now + slept); }, // written again during every nap
        };
        var f = MapDataWatch.Check(w.Host, w.Ro, log.Add, busy);
        Assert.True(f.Kind == MapDataWatch.Kind.Failed, f.Summary);
        Assert.Contains("still being written", f.Reason);
        Assert.Equal(good, File.ReadAllBytes(w.Cache));
    }

    [Fact]
    public async Task The_host_checks_the_maps_before_its_servers_and_hands_the_relay_the_stamp()
    {
        var w = new World();
        File.WriteAllBytes(Path.Combine(w.Ro, "patch.inf"), new byte[] { 0xce, 0x05, 0, 0 });
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0)) { probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; }
        Directory.CreateDirectory(Path.Combine(w.Host, "log"));
        var h = new HostController(new HostSettings { HostDir = w.Host, RoDir = w.Ro, PresencePort = port }) { MapDataOptions = World.NoWait };
        var log = new List<string>();
        await h.CheckMapDataAsync(log.Add);
        Assert.Equal(MapDataWatch.Kind.Rebuilt, h.MapData!.Kind);
        Assert.Contains(log, l => l.StartsWith("Map data: REBUILT"));
        Assert.Contains("REBUILT", File.ReadAllText(Path.Combine(w.Host, "log", "lokihost-mapdata.log")));
        await h.StartPresenceAsync(_ => { });
        Assert.Equal((h.MapData.DataStamp, "patch 1486"), (h.Relay!.HostDataStamp, h.Relay.HostDataLabel));
        using (var c = new RelayClient(port).Hello(71, "Host test")) Assert.Equal(PresenceWire.CapVoice | PresenceWire.CapData | Loki.Horde.Net.HordeWire.CapHorde | PresenceWire.CapSettings | GestureWire.CapGesture | DrawWire.CapDraw | SpotWire.CapSpot, c.Caps); // (client wave 8: Loki Host runs Horde by default; client wave 18, w18acct: and keeps settings per account) (w18share: and forwards casting menus; both at the wave 18 merge) (client wave 20, w20pen: and the drawings) (wfree: and the exact spots)
        await h.StopAsync(_ => { });
        Assert.Contains($"players' game data is compared with the host's ({h.MapData.DataStamp}, patch 1486)", File.ReadAllText(Path.Combine(w.Host, "log", "lokihost-presence.log")));

        // a new window over servers that kept running: no check now, the stamp from the file
        var h2 = new HostController(new HostSettings { HostDir = w.Host, RoDir = w.Ro, PresencePort = port });
        await h2.StartPresenceAsync(_ => { });
        Assert.Equal(h.MapData.DataStamp, h2.Relay!.HostDataStamp);
        await h2.StopAsync(_ => { });

        // a settings file from before this existed: no folder, nothing compared, the relay as it was
        var old = JsonSerializer.Deserialize<HostSettings>("{\"LoginPort\":7900,\"PresencePort\":7903,\"PresenceEnabled\":true,\"VoiceEnabled\":true,\"SetUp\":true}")!;
        Assert.Equal("", old.RoDir);
        var h3 = new HostController(new HostSettings { HostDir = w.Host, PresencePort = port });
        await h3.CheckMapDataAsync(_ => { });
        Assert.Equal(MapDataWatch.Kind.Off, h3.MapData!.Kind);
        await h3.StartPresenceAsync(_ => { });
        Assert.Equal("", h3.Relay!.HostDataStamp);
        using (var c = new RelayClient(port).Hello(71, "Host test")) Assert.Equal(PresenceWire.CapVoice | Loki.Horde.Net.HordeWire.CapHorde | PresenceWire.CapSettings | GestureWire.CapGesture | DrawWire.CapDraw | SpotWire.CapSpot, c.Caps); // (client wave 18, w18acct: settings per account by default) (w18share: and forwards casting menus; both at the wave 18 merge) (client wave 20, w20pen: and the drawings) (wfree: and the exact spots)
        await h3.StopAsync(_ => { });
    }

    // ------------------------------------------------------------------------------------------------ the wire

    [Fact]
    public void The_data_frames_round_trip_and_refuse_what_is_malformed()
    {
        var d = PresenceWire.EncodeData("g1-0123456789abcdef", "patch 1486")!;
        Assert.Equal(1 + 1 + 19 + 1 + 10, d[0] | d[1] << 8);
        Assert.True(PresenceWire.TryDecodeData(d, 2, d.Length - 2, out var stamp, out var label));
        Assert.Equal(("g1-0123456789abcdef", "patch 1486"), (stamp, label));
        var e = PresenceWire.EncodeData("x", "")!;
        Assert.True(PresenceWire.TryDecodeData(e, 2, e.Length - 2, out stamp, out label));
        Assert.Equal(("x", ""), (stamp, label));
        // the longest: 32 stamp bytes, a label cut to 48 UTF-8 bytes without splitting a character; well inside 200
        var big = PresenceWire.EncodeData(new string('a', 32), new string('ã', 40))!;
        Assert.True(PresenceWire.TryDecodeData(big, 2, big.Length - 2, out stamp, out label));
        Assert.Equal((32, new string('ã', 24)), (stamp.Length, label));
        Assert.True(big.Length - 2 <= PresenceWire.MaxPayload);
        Assert.Null(PresenceWire.EncodeData("", "x")); Assert.Null(PresenceWire.EncodeData(new string('a', 33), "")); Assert.Null(PresenceWire.EncodeData("has space", "")); Assert.Null(PresenceWire.EncodeData("é", ""));

        var h = PresenceWire.EncodeDataHost(PresenceWire.DataDiffers, "g1-ffffffffffffffff", "patch 1490")!;
        Assert.True(PresenceWire.TryDecodeDataHost(h, 2, h.Length - 2, out var verdict, out var hs, out var hl));
        Assert.Equal((PresenceWire.DataDiffers, "g1-ffffffffffffffff", "patch 1490"), (verdict, hs, hl));
        Assert.Null(PresenceWire.EncodeDataHost(PresenceWire.DataSame, "", ""));

        byte[] P(params byte[] b) => b;
        foreach (var bad in new[]
        {
            P(PresenceWire.Data), P(PresenceWire.Data, 0, 0), P(PresenceWire.Data, 1), P(PresenceWire.Data, 1, (byte)'a'), P(PresenceWire.Data, 1, (byte)' ', 0),
            P(PresenceWire.Data, 1, (byte)'a', 1), P(PresenceWire.Data, 1, (byte)'a', 0, 0), P(PresenceWire.Data, 1, (byte)'a', 1, 7), P(PresenceWire.Data, 1, (byte)'a', 2, 0xC3, 0x28),
            P(PresenceWire.Data, 33).Concat(new byte[34]).ToArray(), P(PresenceWire.Voice, 1, (byte)'a', 0),
        })
            Assert.False(PresenceWire.TryDecodeData(bad, 0, bad.Length, out _, out _), BitConverter.ToString(bad));
        Assert.False(PresenceWire.TryDecodeDataHost(P(PresenceWire.DataHost, 2, 1, (byte)'a', 0), 0, 5, out _, out _, out _)); // a verdict that is neither
        Assert.False(PresenceWire.TryDecodeDataHost(P(PresenceWire.DataHost), 0, 1, out _, out _, out _));

        // HELLO and WELCOME are byte for byte what they were (the bytes of client wave 6's own tests)
        Assert.Equal(new byte[] { 0x0F, 0x00, 0x01, (byte)'L', (byte)'K', (byte)'P', (byte)'R', 2, 0x40, 0x88, 0x1E, 0x00, 4, (byte)'A', (byte)'l', (byte)'e', (byte)'x' }, PresenceWire.EncodeHello(2000960, "Alex", PresenceWire.VersionVoice));
        Assert.Equal(new byte[] { 0x07, 0x00, 0x81, (byte)'L', (byte)'K', (byte)'P', (byte)'R', 2, 1 }, PresenceWire.EncodeWelcome(2, PresenceWire.CapVoice));
        Assert.Equal(new byte[] { 0x06, 0x00, 0x81, (byte)'L', (byte)'K', (byte)'P', (byte)'R', 1 }, PresenceWire.EncodeWelcome(1, PresenceWire.CapVoice | PresenceWire.CapData));
        // what a client of wave 6 does with the new capability bit: its WELCOME reader takes any capability byte, and it tests bit 0 only
        var w3 = PresenceWire.EncodeWelcome(2, PresenceWire.CapVoice | PresenceWire.CapData);
        Assert.True(PresenceWire.TryDecodeWelcome(w3, 2, w3.Length - 2, out var ver, out var caps));
        Assert.Equal((2, 3, true), (ver, caps, (caps & PresenceWire.CapVoice) != 0));
    }

    // ------------------------------------------------------------------------------------------------ the relay

    /// <summary>A raw relay client (as VoiceTests' own): HELLO 1 or 2, then whatever the test sends.</summary>
    sealed class RelayClient : IDisposable
    {
        public readonly TcpClient Tcp = new() { NoDelay = true };
        readonly PresenceWire.FrameReader _fr;
        readonly byte[] _buf = new byte[8192];
        public byte Caps; public byte[] Welcome = Array.Empty<byte>();
        public RelayClient(int port, bool voice = true)
        {
            Tcp.Connect(IPAddress.Loopback, port);
            _fr = new PresenceWire.FrameReader(voice ? PresenceWire.MaxPayloadVoice : PresenceWire.MaxPayload);
        }
        public void Send(byte[] frame) => Tcp.GetStream().Write(frame);
        public RelayClient Hello(uint aid, string name, byte version = PresenceWire.VersionVoice)
        {
            Send(PresenceWire.EncodeHello(aid, name, version));
            Welcome = Next(2000) ?? throw new Exception("no WELCOME");
            Assert.True(PresenceWire.TryDecodeWelcome(Welcome, 0, Welcome.Length, out var ver, out Caps), "not a WELCOME");
            Assert.Equal(version, ver);
            return this;
        }
        public byte[]? Next(int ms)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(ms);
            while (true)
            {
                int k = _fr.Next(out var p);
                if (k == 1) return p;
                if (k < 0) throw new InvalidDataException("a frame longer than this client accepts");
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) return null;
                Tcp.Client.ReceiveTimeout = Math.Max(1, (int)left.TotalMilliseconds);
                int n;
                try { n = Tcp.GetStream().Read(_buf, 0, _buf.Length); }
                catch (IOException e) when (e.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut }) { return null; }
                if (n <= 0) throw new EndOfStreamException("closed by the relay");
                _fr.Append(_buf, 0, n);
            }
        }
        public bool ClosedWithin(int ms)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(ms);
            try { while (DateTime.UtcNow < deadline) Next((int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds)); }
            catch (Exception e) when (e is EndOfStreamException or IOException) { return true; }
            return false;
        }
        public (byte verdict, string stamp, string label) DataHost(int ms = 2000)
        {
            var p = Next(ms) ?? throw new Exception("no DATA_HOST");
            Assert.True(PresenceWire.TryDecodeDataHost(p, 0, p.Length, out var v, out var s, out var l), $"not a DATA_HOST: 0x{p[0]:x2}");
            return (v, s, l);
        }
        public void Dispose() => Tcp.Dispose();
    }

    static PoseRelay Relay(List<string> log, string stamp = "g1-00000000000000aa", string label = "patch 1486", bool voice = true, PoseRelay.Limits? limits = null)
    {
        var r = new PoseRelay(0, limits, voice, stamp, label);
        r.Log += l => { lock (log) log.Add(l); };
        r.Start(IPAddress.Loopback);
        return r;
    }

    static async Task Until(Func<bool> cond, int ms = 3000)
    {
        var t = DateTime.UtcNow.AddMilliseconds(ms);
        while (!cond() && DateTime.UtcNow < t) await Task.Delay(10);
        Assert.True(cond(), "not within " + ms + " ms");
    }

    [Fact]
    public async Task A_player_whose_data_differs_is_told_and_listed_one_with_the_hosts_is_not()
    {
        var log = new List<string>();
        await using var relay = Relay(log);
        using var same = new RelayClient(relay.Port).Hello(101, "Same");
        using var other = new RelayClient(relay.Port).Hello(102, "Other");
        using var silent = new RelayClient(relay.Port).Hello(103, "Silent");   // a wave-6 client: HELLO 2, never DATA
        using var v1 = new RelayClient(relay.Port, voice: false).Hello(104, "Wave3", PresenceWire.Version);
        Assert.Equal(PresenceWire.CapVoice | PresenceWire.CapData, same.Caps);
        Assert.Equal(new byte[] { 0x81, (byte)'L', (byte)'K', (byte)'P', (byte)'R', 1 }, v1.Welcome); // WELCOME 1: no capability byte, exactly as before

        same.Send(PresenceWire.EncodeData("g1-00000000000000aa", "patch 1486")!);
        Assert.Equal((PresenceWire.DataSame, "g1-00000000000000aa", "patch 1486"), same.DataHost());
        other.Send(PresenceWire.EncodeData("g1-00000000000000bb", "patch 1480")!);
        Assert.Equal((PresenceWire.DataDiffers, "g1-00000000000000aa", "patch 1486"), other.DataHost());
        Assert.Null(silent.Next(200)); Assert.Null(v1.Next(100)); // nobody else hears of it

        var players = relay.Players().OrderBy(p => p.Aid).ToList();
        Assert.Equal(new[] { PoseRelay.DataState.Same, PoseRelay.DataState.Differs, PoseRelay.DataState.NoStamp, PoseRelay.DataState.NoStamp }, players.Select(p => p.Data));
        Assert.Equal(("Other", "g1-00000000000000bb", "patch 1480", (byte)2), (players[1].Name, players[1].DataStamp, players[1].DataLabel, players[1].Version));
        Assert.Equal((byte)1, players[3].Version);
        Assert.Equal("players: Same (AID 101) the host's game data (patch 1486); Other (AID 102) OTHER game data than the host (g1-00000000000000bb, patch 1480); "
            + "Silent (AID 103) no data stamp (an older Loki, or still reading its maps); Wave3 (AID 104) no data stamp (an older Loki, or still reading its maps)", relay.PlayersLine());
        Assert.Equal((2L, 1L), (relay.DataIn, relay.DataDiffers));
        lock (log)
        {
            Assert.Contains("players' game data is compared with the host's (g1-00000000000000aa, patch 1486)", log);
            Assert.Contains("Same (AID 101) has the host's game data (g1-00000000000000aa, patch 1486)", log);
            Assert.Contains("Other (AID 102) has OTHER game data than the host (g1-00000000000000bb, patch 1480; the host's: g1-00000000000000aa, patch 1486): walking may not match on some maps; they were told", log);
        }

        // headsets and voice go on as before, between every kind of client
        var pose = new PresenceWire.PoseBody { Map = "prontera", MetresPerCell = 0.01f };
        pose.V[6] = pose.V[13] = pose.V[20] = 1f;
        silent.Send(PresenceWire.EncodePose(pose)); v1.Send(PresenceWire.EncodePose(pose)); other.Send(PresenceWire.EncodePose(pose));
        await Until(() => relay.Players().Count(p => p.Map == "prontera") == 3);
        pose.Seq = 7; other.Send(PresenceWire.EncodePose(pose));
        for (int i = 0; i < 2; i++) { var p = v1.Next(2000)!; Assert.Equal(PresenceWire.PoseOf, p[0]); }
        Assert.Equal(PresenceWire.PoseOf, silent.Next(2000)![0]);
        // a player who leaves is no longer listed
        other.Dispose();
        await Until(() => relay.Players().All(p => p.Aid != 102));
    }

    [Fact]
    public async Task The_check_changes_nothing_for_older_clients_and_without_a_host_stamp()
    {
        var log = new List<string>();
        // a relay without a host stamp (a standalone relay; a Loki Host with no Ragnarok Online folder): the bit is not set,
        // a stamp sent anyway is kept for the list and not answered
        await using (var plain = Relay(log, stamp: ""))
        {
            using var c = new RelayClient(plain.Port).Hello(201, "New");
            Assert.Equal(PresenceWire.CapVoice, c.Caps);
            Assert.Equal(new byte[] { 0x81, (byte)'L', (byte)'K', (byte)'P', (byte)'R', 2, 1 }, c.Welcome); // as client wave 6's relay sent it
            c.Send(PresenceWire.EncodeData("g1-00000000000000bb", "")!);
            Assert.Null(c.Next(300));
            Assert.Equal((PoseRelay.DataState.NoStamp, "g1-00000000000000bb"), (plain.Players().Single().Data, plain.Players().Single().DataStamp));
            lock (log) Assert.Contains("players' game data is not compared (no host data stamp)", log);
        }
        // a stamp that is not one (a settings mistake) is no stamp
        await using (var bad = Relay(log, stamp: "not a stamp"))
        {
            Assert.Equal("", bad.HostDataStamp);
            using var c = new RelayClient(bad.Port).Hello(202, "New");
            Assert.Equal(PresenceWire.CapVoice, c.Caps);
        }
        await using var relay = Relay(log, voice: false);
        // voice off, the check on: bit 1 alone
        using (var c = new RelayClient(relay.Port).Hello(203, "New")) Assert.Equal(PresenceWire.CapData, c.Caps);
        // a version-1 connection (a client of wave 3-5) that sends DATA: an unknown message, closed, as any was before
        using (var v1 = new RelayClient(relay.Port, voice: false).Hello(204, "Wave3", PresenceWire.Version))
        {
            v1.Send(PresenceWire.EncodeData("g1-00000000000000aa", "")!);
            Assert.True(v1.ClosedWithin(2000));
        }
        // malformed DATA closes the connection
        using (var m = new RelayClient(relay.Port).Hello(205, "Bad"))
        {
            m.Send(new byte[] { 3, 0, PresenceWire.Data, 1, (byte)' ' });
            Assert.True(m.ClosedWithin(2000));
        }
        lock (log) Assert.Contains(log, l => l.Contains("Bad (AID 205) disconnected: invalid DATA"));
        // DATA before HELLO: not a HELLO, closed
        using (var early = new RelayClient(relay.Port))
        {
            early.Send(PresenceWire.EncodeData("g1-00000000000000aa", "")!);
            Assert.True(early.ClosedWithin(2000));
        }
        // a client that repeats itself: four are answered, the rest dropped, the connection stays
        using (var f = new RelayClient(relay.Port).Hello(206, "Flood"))
        {
            for (int i = 0; i < 10; i++) f.Send(PresenceWire.EncodeData("g1-00000000000000bb", "")!);
            int answers = 0;
            while (f.Next(400) is { } p) { Assert.Equal(PresenceWire.DataHost, p[0]); answers++; }
            Assert.Equal(4, answers);
            f.Send(PresenceWire.EncodePing());
            Assert.Null(f.Next(200)); // still connected (a closed connection would throw)
        }
    }

    // ------------------------------------------------------------------------------------------------ the text for friends

    [Fact]
    public void The_text_for_friends_says_what_they_need_of_ragnarok_online()
    {
        var s = new HostSettings { ServerName = "Loki" };
        var en = ShareText.Build(s, "203.0.113.7", "", null, ShareText.English);
        var pt = ShareText.Build(s, "203.0.113.7", "", null, ShareText.Portuguese);
        Assert.Contains("You need your own Ragnarok Online installed and kept up to date with its official patcher", en);
        Assert.Contains("Você precisa do seu próprio Ragnarok Online instalado e atualizado pelo patcher oficial", pt);
        // the lines of before are all still there, in the same order
        Assert.StartsWith("Ragnarok Online in VR with Loki, on my server \"Loki\".", en);
        Assert.Contains("Over the internet: address 203.0.113.7, port 7900.", en);
        Assert.EndsWith("Your account: ask me for a name and a password (I make it in Loki Host).", en);
        Assert.Contains("Ragnarok Online folder", ShareText.GameDataHint(new HostSettings()));
        Assert.Contains(@"D:\RO", ShareText.GameDataHint(new HostSettings { RoDir = @"D:\RO" }));
    }

    // ------------------------------------------------------------------------------------------------ the real install (when given)

    /// <summary>
    /// With LOKIHOST_TEST_RO (the Ragnarok Online folder), LOKIHOST_TEST_RATHENA_DB (a folder holding rAthena's db/) and
    /// LOKIHOST_TEST_MAPCACHE_REF (the file python tools/mapcache/build_import_cache.py wrote from the same two): the C# builder
    /// gives the same maps with the same cells (the content digest), and on a runtime whose zlib is the reference's (.NET 9 for
    /// CPython 3.14.3 on Windows) the same bytes. Without them the test does nothing.
    /// </summary>
    [Fact]
    public void The_real_install_gives_the_python_tools_cache()
    {
        string? ro = Environment.GetEnvironmentVariable("LOKIHOST_TEST_RO"), ra = Environment.GetEnvironmentVariable("LOKIHOST_TEST_RATHENA_DB"), reference = Environment.GetEnvironmentVariable("LOKIHOST_TEST_MAPCACHE_REF");
        if (string.IsNullOrEmpty(ro) || string.IsNullOrEmpty(ra) || string.IsNullOrEmpty(reference)) return;
        var want = File.ReadAllBytes(reference);
        MapCacheBuilder.Result r;
        using (var install = HostInstall.Open(ro)) r = MapCacheBuilder.Build(ra, install.Read);
        Assert.Equal(MapCache.Read(want, reference).Keys, r.Maps.Select(m => m.Name));
        Assert.Equal(MapCache.ContentDigest(want, reference), MapCache.ContentDigest(r.File, "built"));
        if (Environment.Version.Major == 9) Assert.Equal(want, r.File);
        Assert.True(PointsCheck.Run(ra, r.File, null).Clean);
    }
}
