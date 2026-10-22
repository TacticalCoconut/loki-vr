using System.Text;

namespace LokiHost.Core;

/// <summary>
/// Client wave 7 (w7host): Loki Host follows the host's own Ragnarok Online install. At every Start, before any server
/// runs, it compares the install's stamp (<see cref="HostInstall.StampText"/>: every GRF in the install's order with its size,
/// time and version, the loose map files, the patcher's marker) with the one it kept when the map cache was last made. When
/// they differ (the official patcher ran), it rebuilds the import map cache from the install's maps
/// (<see cref="MapCacheBuilder"/>: tools/mapcache/build_import_cache.py), runs the safety checks (<see cref="PointsCheck"/>:
/// tools/mapcache/check_points.py: no warp, spawn or script point lost or cut off) and only then puts the new file in place,
/// the old one kept beside it. Anything that fails keeps the old cache and says why; the servers start either way.
///
/// The stamp file (db/import/map_cache.loki-stamp.txt in the host's copy) also keeps the data stamp of the install the
/// cache was made from (client IO/MapDataStamp.cs): what players' clients are compared with (PoseRelay, the DATA message).
/// Moving the stamp file away makes the next Start check the maps again; nothing else depends on it.
/// </summary>
public static class MapDataWatch
{
    /// <summary>Raise when the builder's output can change for the same input (every host then rebuilds once).</summary>
    public const int BuilderVersion = 2; // 2 (warch2, 2026-10-15): the alias maps of the game's name table are built from their real maps: every host rebuilds once
    public const string StampName = "map_cache.loki-stamp.txt";

    public enum Kind
    {
        /// <summary>No Ragnarok Online folder is set: nothing is checked, no version check for players.</summary>
        Off,
        /// <summary>The install is as it was when the cache was made.</summary>
        Unchanged,
        /// <summary>The install changed; the maps the cache holds did not: the file stays, the stamp is renewed.</summary>
        SameMaps,
        /// <summary>A new cache is in place, the old one beside it.</summary>
        Rebuilt,
        /// <summary>The check or the rebuild could not be done: the old cache stays.</summary>
        Failed,
        /// <summary>Servers were running: checked at the next Start.</summary>
        Skipped,
    }

    public sealed class Outcome
    {
        public Kind Kind;
        /// <summary>One line for the window and the log.</summary>
        public string Summary = "";
        /// <summary>Why it failed / was skipped / is off.</summary>
        public string Reason = "";
        /// <summary>The data stamp and label of the install the cache in place was made from ("" when unknown): what players
        /// are compared with.</summary>
        public string DataStamp = "", DataLabel = "";
        public int Maps;
        /// <summary>The maps a rebuild took from the install (changed since the cache before).</summary>
        public List<string> Changed = new();
        public string? KeptOldAs;
        public DateTime When = DateTime.Now;
    }

    public sealed class Options
    {
        /// <summary>A GRF written less than this long ago may still be being patched: wait until it is this old.</summary>
        public TimeSpan Quiet = TimeSpan.FromSeconds(60);
        /// <summary>… but no longer than this (then the old cache stays and the next Start tries again).</summary>
        public TimeSpan MaxWait = TimeSpan.FromSeconds(120);
        public Func<DateTime> UtcNow = () => DateTime.UtcNow;
        public Action<TimeSpan> Sleep = t => Thread.Sleep(t);
        /// <summary>An rAthena source folder for the @go points (src/map/atcommand.cpp), or null.</summary>
        public string? SourceDir;
    }

    public static string ImportCachePath(string hostDir) => Path.Combine(hostDir, "db", "import", "map_cache.dat");
    public static string StampPath(string hostDir) => Path.Combine(hostDir, "db", "import", StampName);

    /// <summary>What the cache is made from: the builder's version, the install's stamp, rAthena's own two caches.</summary>
    public static string Head(string hostDir, string roDir)
    {
        var sb = new StringBuilder();
        sb.Append("builder ").Append(BuilderVersion).Append('\n');
        sb.Append(HostInstall.StampText(roDir));
        foreach (var rel in new[] { "db/re/map_cache.dat", "db/map_cache.dat" })
        {
            var fi = new FileInfo(Path.Combine(hostDir, rel.Replace('/', Path.DirectorySeparatorChar)));
            sb.Append("rathena ").Append(rel).Append(' ').Append(fi.Exists ? fi.Length + " " + fi.LastWriteTimeUtc.Ticks : "missing").Append('\n');
        }
        return sb.ToString();
    }

    sealed record Kept(string Head, long CacheLength, string CacheSha, string Content, int Maps, string DataStamp, string DataLabel, string Checked);

    static Kept? ReadStamp(string hostDir)
    {
        var p = StampPath(hostDir);
        if (!File.Exists(p)) return null;
        string text;
        try { text = File.ReadAllText(p).Replace("\r\n", "\n"); } catch (IOException) { return null; }
        int start = text.IndexOf("builder ", StringComparison.Ordinal), cut = text.IndexOf("\n--\n", StringComparison.Ordinal);
        if (start < 0 || cut < start) return null;
        string head = text.Substring(start, cut + 1 - start);
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Substring(cut + 4).Split('\n'))
        {
            int sp = line.IndexOf(' ');
            if (sp > 0) d[line.Substring(0, sp)] = line.Substring(sp + 1);
        }
        long len = 0; string sha = "";
        if (d.TryGetValue("cache", out var c)) { var parts = c.Split(' '); if (parts.Length == 2 && long.TryParse(parts[0], out len)) sha = parts[1]; }
        int.TryParse(d.GetValueOrDefault("maps", "0"), out int maps);
        return new Kept(head, len, sha, d.GetValueOrDefault("content", ""), maps, d.GetValueOrDefault("data-stamp", ""), d.GetValueOrDefault("data-label", ""), d.GetValueOrDefault("checked", ""));
    }

    static void WriteStamp(string hostDir, string head, byte[] cache, string content, int maps, string dataStamp, int dataMaps, string dataLabel, string result)
    {
        var sb = new StringBuilder();
        sb.Append("Loki Host: what db/import/map_cache.dat was made from. Loki Host rewrites this file; moving it away makes the next Start check the maps again.\n");
        sb.Append(head);
        sb.Append("--\n");
        sb.Append("cache ").Append(cache.Length).Append(' ').Append(MapCache.Sha256(cache)).Append('\n');
        sb.Append("content ").Append(content).Append('\n');
        sb.Append("maps ").Append(maps).Append('\n');
        sb.Append("data-stamp ").Append(dataStamp).Append('\n');
        sb.Append("data-maps ").Append(dataMaps).Append('\n');
        sb.Append("data-label ").Append(dataLabel).Append('\n');
        sb.Append("checked ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append('\n');
        sb.Append("result ").Append(result).Append('\n');
        var tmp = StampPath(hostDir) + ".tmp";
        File.WriteAllText(tmp, sb.ToString());
        File.Move(tmp, StampPath(hostDir), true);
    }

    /// <summary>The stamp and label of the install the cache in place was made from, without checking anything (the window,
    /// before the first Start; servers taken over from an earlier run).</summary>
    public static (string Stamp, string Label) KeptDataStamp(string hostDir)
    {
        var k = ReadStamp(hostDir);
        return k == null ? ("", "") : (k.DataStamp, k.DataLabel);
    }

    /// <summary>
    /// The check at Start. NEVER call it while a server runs from <paramref name="hostDir"/> (the map-server holds the cache it
    /// read at its own start; a file swapped under it would only confuse the next person to look). Blocking: seconds when a
    /// rebuild is due (every map of the install is read).
    /// </summary>
    public static Outcome Check(string hostDir, string roDir, Action<string> log, Options? opt = null)
    {
        opt ??= new Options();
        var kept = ReadStamp(hostDir);
        var o = new Outcome { DataStamp = kept?.DataStamp ?? "", DataLabel = kept?.DataLabel ?? "", Maps = kept?.Maps ?? 0 };
        if (string.IsNullOrWhiteSpace(roDir))
        {
            o.Kind = Kind.Off; o.DataStamp = ""; o.DataLabel = "";
            o.Reason = "no Ragnarok Online folder is set (Setup tab)";
            o.Summary = "Map data: not followed (" + o.Reason + "): the server walks on the map cache it has, and players' game data is not compared.";
            return o;
        }
        try
        {
            if (!Directory.Exists(roDir)) throw new DirectoryNotFoundException($"the Ragnarok Online folder {roDir} does not exist");
            string head = Head(hostDir, roDir);
            string cachePath = ImportCachePath(hostDir);
            byte[]? current = File.Exists(cachePath) ? File.ReadAllBytes(cachePath) : null;

            if (kept != null && kept.Head == head && current != null && current.Length == kept.CacheLength && MapCache.Sha256(current) == kept.CacheSha)
            {
                o.Kind = Kind.Unchanged;
                o.Summary = $"Map data: current ({kept.Maps} maps from your Ragnarok Online install, made {kept.Checked}; {DataText(kept.DataStamp, kept.DataLabel)}). Nothing changed since: no rebuild.";
                return o;
            }
            log(kept == null ? "Map data: no stamp yet for this host's map cache: reading the maps of " + roDir + " …"
                : kept.Head != head ? "Map data: your Ragnarok Online install changed since the map cache was made (" + FirstDifference(kept.Head, head) + "): reading its maps …"
                : "Map data: db/import/map_cache.dat is not the file Loki Host made: reading the maps of " + roDir + " …");

            // a patch in progress: never build from files still being written
            var waited = TimeSpan.Zero;
            while (true)
            {
                var age = opt.UtcNow() - HostInstall.NewestWriteUtc(roDir);
                if (age >= opt.Quiet || age < TimeSpan.Zero) break;
                if (waited >= opt.MaxWait) throw new IOException($"the install's game data was still being written {age.TotalSeconds:0} s ago after {waited.TotalSeconds:0} s of waiting: the official patcher is still at work");
                if (waited == TimeSpan.Zero) log($"Map data: the install's game data was written {age.TotalSeconds:0} s ago (the official patcher may still be at work): waiting until it has been quiet for {opt.Quiet.TotalSeconds:0} s …");
                var nap = TimeSpan.FromSeconds(Math.Min(5, Math.Max(0.2, (opt.Quiet - age).TotalSeconds)));
                opt.Sleep(nap); waited += nap;
                head = Head(hostDir, roDir);
            }

            MapCacheBuilder.Result built; Loki.IO.MapDataStamp.Result stamp; string label;
            using (var install = HostInstall.Open(roDir, l => log("Map data: " + l)))
            {
                built = MapCacheBuilder.Build(hostDir, install.Read);
                stamp = install.DataStamp();
                label = HostInstall.PatchLabel(roDir);
            }
            string headAfter = Head(hostDir, roDir);
            if (headAfter != head) throw new IOException("the install changed while its maps were being read (" + FirstDifference(head, headAfter) + "): the official patcher is at work");
            foreach (var l in built.Report) log("Map data: " + l.TrimEnd());
            if (stamp.Unreadable > 0) log($"Map data: {stamp.Unreadable} map file(s) of the install cannot be read ({string.Join(", ", stamp.UnreadableNames)}{(stamp.Unreadable > stamp.UnreadableNames.Count ? ", …" : "")}).");
            foreach (var l in MapCacheBuilder.ServerWouldRefuse(built.Maps)) log("Map data: WARNING: " + l);

            var check = PointsCheck.Run(hostDir, built.File, opt.SourceDir);
            foreach (var l in check.Lines) log("Map data: check: " + l);
            if (!check.Clean)
                throw new InvalidDataException($"the new maps would lose {check.Bad} warp / spawn / script point(s) and cut {check.Lost} off (the lines above name them)");

            string content = MapCache.ContentDigest(built.File, "the new map cache");
            string? currentContent = null;
            if (current != null) { try { currentContent = MapCache.ContentDigest(current, cachePath); } catch (InvalidDataException) { } }
            o.Maps = built.Maps.Count;
            o.DataStamp = stamp.Stamp; o.DataLabel = label;
            o.Changed = built.Replaced.Select(x => x.Name).ToList();
            if (currentContent == content)
            {
                // the same maps with the same cells: the file in place stays (whatever zlib wrote it), only the stamp is renewed
                WriteStamp(hostDir, head, current!, content, o.Maps, stamp.Stamp, stamp.Maps, label, "same maps");
                o.Kind = Kind.SameMaps;
                o.Summary = $"Map data: checked against your Ragnarok Online install: the {o.Maps} maps of the map cache are unchanged (the file stays as it is; {DataText(stamp.Stamp, label)}).";
                return o;
            }

            // in place: the new file beside the old, read back, then the old one moved aside (never deleted), the new one moved in
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            string tmp = cachePath + ".new";
            File.WriteAllBytes(tmp, built.File);
            if (!File.ReadAllBytes(tmp).AsSpan().SequenceEqual(built.File)) throw new IOException("the new map cache did not read back from disk as it was written");
            string? keptAs = null;
            if (current != null)
            {
                keptAs = cachePath + ".before-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                for (int i = 2; File.Exists(keptAs); i++) keptAs = cachePath + ".before-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + i;
                File.Move(cachePath, keptAs);
            }
            try { File.Move(tmp, cachePath); }
            catch (Exception) { if (keptAs != null && !File.Exists(cachePath)) File.Move(keptAs, cachePath); throw; }
            WriteStamp(hostDir, head, built.File, content, o.Maps, stamp.Stamp, stamp.Maps, label, "rebuilt");
            o.Kind = Kind.Rebuilt; o.KeptOldAs = keptAs;
            string changed = o.Changed.Count == 0 ? "" : o.Changed.Count <= 6 ? " (" + string.Join(", ", o.Changed) + ")" : $" ({string.Join(", ", o.Changed.Take(6))}, … {o.Changed.Count} in all)";
            o.Summary = $"Map data: REBUILT from your Ragnarok Online install: {o.Maps} maps, {o.Changed.Count} taken from the install{changed}; "
                + $"checks clean ({check.Checked} warp / spawn / script points, {check.Reachability} reachability); "
                + (keptAs != null ? $"the old file is kept as {Path.GetFileName(keptAs)}; " : "") + DataText(stamp.Stamp, label) + ".";
            return o;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or ArgumentException or OverflowException or OutOfMemoryException)
        {
            o.Kind = Kind.Failed;
            o.Reason = e.Message.Trim();
            o.Summary = "Map data: NOT rebuilt, the map cache in place is kept: " + o.Reason + ". The next Start tries again.";
            return o;
        }
    }

    static string DataText(string stamp, string label) => stamp == "" ? "no data stamp" : "data stamp " + stamp + (label != "" ? ", " + label : "");

    /// <summary>The first line that differs between two stamps, old → new (for the log).</summary>
    static string FirstDifference(string a, string b)
    {
        var x = a.Split('\n'); var y = b.Split('\n');
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            string l = i < x.Length ? x[i] : "", r = i < y.Length ? y[i] : "";
            if (l != r) return $"\"{l}\" → \"{r}\"";
        }
        return "no difference";
    }
}
