using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace LokiHost.Core;

/// <summary>
/// rAthena's map cache file (db/import/map_cache.dat, db/re/map_cache.dat, db/map_cache.dat), read and written as the Python
/// tools do (client/tools/move/mapcache_vs_grf.py read_cache / cache_cells, tools/mapcache/build_import_cache.py write_cache;
/// their lines are quoted beside each method). Client wave 7 (w7host): Loki Host rebuilds the import cache itself, no Python
/// at run time.
///
/// The layout is rAthena's (rA = C:\rAthena, e985006):
///   rA src/map/map.cpp:156-159  struct map_cache_main_header { uint32 file_size; uint16 map_count; }   (8 bytes with padding)
///   rA src/map/map.cpp:162-167  struct map_cache_map_info { char name[MAP_NAME_LENGTH]; int16 xs; int16 ys; int32 len; }
///   then len bytes: the map's cells (one type byte each, xs × ys), zlib-compressed.
/// How the map-server reads it:
///   rA src/map/map.cpp:3919-3925  the files in the order db/import/, db/re/ (DBPATH), db/;
///   rA src/map/map.cpp:3967-3971  each map from the first of them that holds it ("for (const auto &amp;cache : map_cache_buffer)
///                                 if ((success = map_readfromcache(mapdata, cache, …)) != 0) break;");
///   rA src/map/map.cpp:3672-3713  map_readfromcache: walks the entries by their len, strcmp on the name, refuses xs / ys ≤ 0
///                                 and more than MAX_MAP_SIZE cells (map.hpp:82, 512*512), then "decode_zip(decode_buffer,
///                                 &amp;size, p+sizeof(struct map_cache_map_info), info->len)" and map_gat2cell per byte;
///   rA src/common/grfio.cpp:245-248  decode_zip is zlib's uncompress.
/// So the server takes ANY valid zlib stream that inflates to the cells, and it does not check the result (map.cpp:3701 "TO-DO:
/// Maybe handle the scenario, if the decoded buffer isn't the same size as expected?"): the builder reads its own file back.
///
/// The compressed bytes depend on the zlib that writes them, not on the cells alone: Python's zlib.compress(cells) gives
/// different streams with CPython 3.14 on Windows (zlib-ng) and CPython 3.12 on Linux (zlib 1.3), and .NET's own zlib differs
/// between .NET 8, 9 and 10 (measured on this install's 57 maps, design notes). So two caches are compared by
/// <see cref="ContentDigest"/> (names, sizes and inflated cells, in file order): equal digests mean the server reads the same
/// maps. Run on the .NET 9 runtime, whose zlib is the one CPython 3.14.3 has, this code writes the Python tool's file byte for
/// byte.
/// </summary>
public static class MapCache
{
    public sealed record Entry(string Name, int Xs, int Ys, byte[] Compressed);
    public sealed record Map(string Name, int Xs, int Ys, byte[] Cells);

    /// <summary>map_gat2cell (rA src/map/map.cpp:3280-3299): types 0, 2, 3, 4, 6 are walkable; the Python tools' WALKABLE.</summary>
    public static bool Walkable(byte type) => type == 0 || type == 2 || type == 3 || type == 4 || type == 6;

    /// <summary>
    /// mapcache_vs_grf.py read_cache:
    ///   file_size, count = struct.unpack_from('&lt;IH', b, 0); p = 8
    ///   for _ in range(count):
    ///       name = b[p:p + 12].split(b'\0', 1)[0].decode('latin-1')
    ///       xs, ys, ln = struct.unpack_from('&lt;hhi', b, p + 12)
    ///       comp = b[p + 20:p + 20 + ln]; maps[name] = (xs, ys, comp); p += 20 + ln
    /// A name met twice keeps its first place and takes the later entry (a Python dict). Where Python would fail later or read
    /// nonsense (a negative or overlong len), this throws at once: a damaged cache is never used.
    /// </summary>
    public static PyDict<Entry> Read(byte[] b, string what)
    {
        var maps = new PyDict<Entry>();
        if (b.Length < 6) throw new InvalidDataException($"{what}: {b.Length} bytes, shorter than a map cache header");
        int count = BitConverter.ToUInt16(b, 4);
        long p = 8;
        for (int i = 0; i < count; i++)
        {
            if (p + 20 > b.Length) throw new InvalidDataException($"{what}: entry {i} of {count} starts past the end of the file");
            int end = Array.IndexOf(b, (byte)0, (int)p, 12);
            string name = Encoding.Latin1.GetString(b, (int)p, (end < 0 ? (int)p + 12 : end) - (int)p);
            int xs = BitConverter.ToInt16(b, (int)p + 12), ys = BitConverter.ToInt16(b, (int)p + 14), ln = BitConverter.ToInt32(b, (int)p + 16);
            if (ln < 0 || p + 20 + ln > b.Length) throw new InvalidDataException($"{what}: the map '{name}' has {ln} bytes of cells, more than the file holds");
            maps[name] = new Entry(name, xs, ys, b.AsSpan((int)p + 20, ln).ToArray());
            p += 20 + ln;
        }
        return maps;
    }

    public static PyDict<Entry> Read(string path) => Read(File.ReadAllBytes(path), path);

    /// <summary>
    /// mapcache_vs_grf.py cache_cells:
    ///   cells = zlib.decompress(comp)
    ///   if len(cells) != xs * ys: raise ValueError('cache cells %d != %d x %d' % (len(cells), xs, ys))
    /// </summary>
    public static byte[] Cells(Entry e)
    {
        long want = (long)e.Xs * e.Ys;
        byte[] cells;
        try { cells = Inflate(e.Compressed, want < 0 ? 0 : want); }
        catch (InvalidDataException x) { throw new InvalidDataException($"the cells of '{e.Name}' cannot be inflated: {x.Message}", x); }
        if (cells.Length != want) throw new InvalidDataException($"cache cells {cells.Length} != {e.Xs} x {e.Ys} ('{e.Name}')");
        return cells;
    }

    /// <summary>The whole zlib stream, inflated (read to its end, so its checksum is verified as zlib.decompress does).</summary>
    static byte[] Inflate(byte[] comp, long expected)
    {
        using var z = new ZLibStream(new MemoryStream(comp, false), CompressionMode.Decompress);
        using var o = new MemoryStream((int)Math.Min(expected + 1, 1 << 24));
        z.CopyTo(o);
        return o.ToArray();
    }

    /// <summary>
    /// Python's zlib.compress(cells): a zlib stream at the default level (6), 32 K window (.NET's CompressionLevel.Optimal is
    /// zlib's level 6 with the same window and memory level). Which bytes come out is the runtime's zlib's business (see the
    /// class summary); any of them inflates to the same cells.
    /// </summary>
    public static byte[] Deflate(byte[] cells)
    {
        var o = new MemoryStream();
        using (var z = new ZLibStream(o, CompressionLevel.Optimal, true)) z.Write(cells, 0, cells.Length);
        return o.ToArray();
    }

    /// <summary>
    /// build_import_cache.py write_cache:
    ///   for name, (xs, ys, cells) in maps.items():
    ///       comp = zlib.compress(bytes(cells)); nm = name.encode('latin-1'); assert len(nm) &lt; 12, name
    ///       body += nm.ljust(12, b'\0') + struct.pack('&lt;hhi', xs, ys, len(comp)) + comp
    ///   head = struct.pack('&lt;IHxx', 8 + len(body), len(maps))
    /// struct.pack refuses a size outside int16 and more than 65535 maps: so does this.
    /// </summary>
    public static byte[] Write(IEnumerable<Map> maps)
    {
        var body = new MemoryStream();
        int n = 0;
        var info = new byte[20];
        foreach (var m in maps)
        {
            var comp = Deflate(m.Cells);
            foreach (char c in m.Name) if (c > 0xFF) throw new InvalidDataException($"map name '{m.Name}' is not Latin-1");
            var nm = Encoding.Latin1.GetBytes(m.Name);
            if (nm.Length >= 12) throw new InvalidDataException($"map name '{m.Name}' is longer than 11 bytes (MAP_NAME_LENGTH 12)");
            if (m.Xs < short.MinValue || m.Xs > short.MaxValue || m.Ys < short.MinValue || m.Ys > short.MaxValue)
                throw new InvalidDataException($"map '{m.Name}' is {m.Xs} x {m.Ys}: not an int16 size");
            Array.Clear(info);
            nm.CopyTo(info, 0);
            BitConverter.TryWriteBytes(info.AsSpan(12), (short)m.Xs);
            BitConverter.TryWriteBytes(info.AsSpan(14), (short)m.Ys);
            BitConverter.TryWriteBytes(info.AsSpan(16), comp.Length);
            body.Write(info);
            body.Write(comp);
            n++;
        }
        if (n > ushort.MaxValue) throw new InvalidDataException($"{n} maps: more than a map cache header can count");
        var file = new byte[8 + body.Length];
        BitConverter.TryWriteBytes(file.AsSpan(0), (uint)(8 + body.Length));
        BitConverter.TryWriteBytes(file.AsSpan(4), (ushort)n);
        body.Position = 0;
        body.ReadExactly(file.AsSpan(8));
        return file;
    }

    /// <summary>
    /// What the server reads from a cache, whatever zlib wrote it: SHA-256 over every entry in file order: its 12 name bytes
    /// (NUL-padded), int16 xs, int16 ys, then the inflated cells. tools/mapcache/cache_digest.py prints the same value.
    /// </summary>
    public static string ContentDigest(byte[] file, string what)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var head = new byte[16];
        foreach (var (_, e) in Read(file, what))
        {
            Array.Clear(head);
            Encoding.Latin1.GetBytes(e.Name).CopyTo(head, 0);
            BitConverter.TryWriteBytes(head.AsSpan(12), (short)e.Xs);
            BitConverter.TryWriteBytes(head.AsSpan(14), (short)e.Ys);
            sha.AppendData(head);
            sha.AppendData(Cells(e));
        }
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    public static string Sha256(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
}

/// <summary>A Python dict's order: keys in the order they were first set; setting a key again keeps its place.</summary>
public sealed class PyDict<T> : IEnumerable<(string Key, T Value)>
{
    readonly List<string> _keys = new();
    readonly Dictionary<string, T> _d = new(StringComparer.Ordinal);
    public int Count => _keys.Count;
    public IReadOnlyList<string> Keys => _keys;
    public bool ContainsKey(string k) => _d.ContainsKey(k);
    public bool TryGetValue(string k, out T v) => _d.TryGetValue(k, out v!);
    public T this[string k]
    {
        get => _d[k];
        set { if (!_d.ContainsKey(k)) _keys.Add(k); _d[k] = value; }
    }
    public IEnumerator<(string Key, T Value)> GetEnumerator() { foreach (var k in _keys) yield return (k, _d[k]); }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
