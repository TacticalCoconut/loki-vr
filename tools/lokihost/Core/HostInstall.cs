using System.Text;
using Loki.Data;
using Loki.IO;

namespace LokiHost.Core;

/// <summary>
/// The host's own Ragnarok Online install (client wave 7, w7host), only ever READ: which files it offers and what they are,
/// so Loki Host can build the server's map cache from the maps the host's players draw, and notice when the official patcher
/// changed them.
///
/// THE ONE PLACE that decides which files the install offers and in which order, and it decides nothing itself: it opens the
/// install through the Loki client's own code (tools/lokihost/RoData links client/Assets/Loki/Runtime/Data/RoInstall.cs and
/// IO/RoLookup.cs, GrfArchive.cs, GrfCrypt.cs: client wave 7, w7data), so host and client follow an install by the same rule:
///  • the archives: the install's DATA.INI order when it has one, else data.grf (the client's default list, the archive the
///    official patcher keeps current); the first archive that holds a file wins (<see cref="RoInstall"/>);
///  • the loose data\ folder: before the archives when the install's clientinfo carries the readfolder tag (or the install
///    has no clientinfo), after them otherwise (<see cref="RoLookup"/>).
/// The LATAM install of 2026-09-30 has no DATA.INI and no loose data\ folder: it is read from data.grf alone.
///
/// Where the host is stricter than the client (w7host's merge notes, COORD 2026-10-04 13:15): the client skips an archive it
/// cannot open and a damaged entry, and plays on with what it has; the host must not build the server's maps from half an
/// install. So <see cref="Open"/> throws when an archive of the order is damaged, cut short, not a GRF or held open by
/// another program (the map cache in place is kept), and <see cref="Read"/> (the builder's read) throws for a damaged entry.
/// An archive of a version the reader does not know (the LATAM install's event.grf, GRF 0x102, when a DATA.INI names it) and
/// one that is not in the folder are passed over as the client passes over them. <see cref="DataStamp"/> reads exactly as the
/// client's file system does (a damaged entry: the next source), because it is compared with the players' stamps.
/// </summary>
public sealed class HostInstall : IDisposable
{
    /// <summary>The client's default archive list (client World/RoAssets.cs LokiConfig.grfs), used when the install has no DATA.INI.</summary>
    static readonly string[] Configured = { "data.grf" };

    public string Root { get; }
    readonly RoOpened _opened;
    /// <summary>What the install says about itself (its archive order and where it comes from, the patch marker).</summary>
    public RoInstall Install => _opened.Install;
    /// <summary>The client's lookup over the open archives and the loose folder.</summary>
    public RoLookup Lookup => _opened.Lookup;
    /// <summary>The archives that are read, in the install's order.</summary>
    public IList<GrfArchive> Grfs => _opened.Lookup.Grfs;
    /// <summary>The archives of the order that are not read (an unknown version, not in the folder), and why.</summary>
    public IReadOnlyList<SkippedGrf> Skipped => _opened.Skipped;
    public bool HasLooseFolder => _opened.Lookup.HasLooseFolder;

    /// <summary>The GRFs the install is read from, in order: DATA.INI's when the install has one, else data.grf (the client's
    /// rule, <see cref="RoInstall.QuickOrder"/>: the same list as RoInstall.Open's Install.GrfNames, without opening anything).</summary>
    public static IReadOnlyList<string> GrfNames(string root) => RoInstall.QuickOrder(root, Configured);

    HostInstall(string root, RoOpened opened) { Root = root; _opened = opened; }

    /// <summary>Opens the install as the client does (<see cref="RoInstall.Open"/>). Throws with the reason when the folder
    /// does not exist, when none of its archives is there, or when an archive of the order cannot be read (damaged,
    /// half-written, not a GRF, held open by another program): the caller keeps what it has. <paramref name="log"/> gets
    /// what differs from a plain install (a DATA.INI's order, an archive passed over, the loose folder's place).</summary>
    public static HostInstall Open(string root, Action<string>? log = null)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"the Ragnarok Online folder {root} does not exist");
        var o = RoInstall.Open(root, Configured);
        var inst = new HostInstall(root, o);
        try
        {
            foreach (var k in o.Skipped)
            {
                // the client skips these two and reads the rest: so does the host
                if (k.Kind == nameof(GrfOpenException.Kind.UnknownVersion) || k.Kind == "missing") continue;
                // damaged / cut short / not a GRF / busy: never build the server's maps from part of an install
                string why = k.Reason.StartsWith(k.Name + " ", StringComparison.Ordinal) ? k.Reason.Substring(k.Name.Length + 1) : k.Reason;
                string text = $"{k.Name} cannot be read (it {why.Trim()})";
                if (k.Kind == nameof(GrfOpenException.Kind.Busy)) throw new IOException(text);
                throw new InvalidDataException(text);
            }
            if (o.Lookup.Grfs.Count == 0)
            {
                var gone = o.Skipped.FirstOrDefault(k => k.Kind == "missing");
                if (gone != null) throw new FileNotFoundException($"no {gone.Name} in {root}: not a Ragnarok Online install folder?");
                throw new InvalidDataException($"none of the archives of {root} can be read ({string.Join("; ", o.Skipped.Select(k => k.Reason))})");
            }
            if (log != null)
            {
                if (o.Install.HasDataIni) log($"your install's {RoInstall.DataIniName} lists {o.Install.GrfNames.Count} archive(s), read in this order: {string.Join(", ", o.Install.GrfNames)}");
                foreach (var k in o.Skipped) log("not read: " + k.Reason);
                if (o.Lookup.HasLooseFolder)
                    log("loose files in the install's data\\ folder are read " + (o.Lookup.LooseFirst ? "before" : "after") + " the archives ("
                        + (o.ClientInfoName == null ? "the game data has no clientinfo" : o.ClientInfoName + (o.Lookup.LooseFirst ? " has the readfolder tag" : " has no readfolder tag")) + ")");
            }
            return inst;
        }
        catch { inst.Dispose(); throw; }
    }

    static string Normalize(string roPath) => RoPath.Normalize(roPath);

    /// <summary>The file's bytes, or null when the install has none, from the source the client would take it from
    /// (<see cref="RoLookup.Read"/>). A damaged entry or an unreadable loose file THROWS (no handler is given): the map
    /// cache is then not rebuilt, as the Python tool stops there.</summary>
    public byte[]? Read(string roPath) => _opened.Lookup.Read(Normalize(roPath), out _, null);

    /// <summary>Every map file the install offers ("data\NAME.gat", normalized): the open archives' and the loose folder's
    /// (what the client's stamp lists: client LokiGame.W7Host.cs W7HostData.MadeFrom).</summary>
    public List<string> MapGatNames()
    {
        var names = new List<string>();
        foreach (var g in _opened.Lookup.Grfs) foreach (var n in g.Names) if (MapDataStamp.IsMapGat(n)) names.Add(n);
        if (_opened.Lookup.HasLooseFolder)
            foreach (var f in Directory.EnumerateFiles(Path.Combine(Root, "data"), "*.gat"))
            {
                string n = Normalize("data\\" + Path.GetFileName(f));
                if (MapDataStamp.IsMapGat(n)) names.Add(n);
            }
        return names;
    }

    /// <summary>The data stamp players' clients are compared with (client IO/MapDataStamp.cs: the same code on both ends),
    /// each map read as the client's file system reads it (client IO/RoFileSystem.cs Read: a damaged entry is passed over and
    /// the next source read; a map is "unreadable" only when no source gives it).</summary>
    public MapDataStamp.Result DataStamp() => MapDataStamp.Compute(MapGatNames(), n => _opened.Lookup.Read(n, out _, _ => { }));

    public void Dispose() { foreach (var g in _opened.Lookup.Grfs) g.Dispose(); _opened.Lookup.Grfs.Clear(); }

    // ------------------------------------------------------------------------------------------------ the install's stamp

    /// <summary>The official patcher's marker (patch.inf: the client's own reading, IO/MapDataStamp.cs PatchNumber, so host and
    /// players label their data alike), or null when the install has none.</summary>
    public static long? PatchNumber(string root) => MapDataStamp.PatchNumber(root) is var n && n >= 0 ? n : null;

    /// <summary>"patch 1486", or "" when the install has no marker.</summary>
    public static string PatchLabel(string root) => PatchNumber(root) is long n ? "patch " + n : "";

    /// <summary>
    /// What the map data is made from, as text (the same kind of stamp as the client's first-run builder keeps, client
    /// Data/ClientDataBuilder.cs CurrentStamp): every GRF in the install's order with its size, time and version, the loose
    /// map files (and a loose clientinfo, which can decide the loose folder's place), the patch marker. Cheap (file sizes
    /// and times, 46 header bytes per GRF): compared at every Start; equal text means nothing the map cache is made from
    /// changed. Nothing is opened for writing or locked against the patcher.
    /// </summary>
    public static string StampText(string root)
    {
        var sb = new StringBuilder();
        sb.Append("install ").Append(root).Append('\n');
        foreach (var name in GrfNames(root))
        {
            var fi = new FileInfo(Path.Combine(root, name));
            if (!fi.Exists) { sb.Append("grf ").Append(name).Append(" missing\n"); continue; }
            sb.Append("grf ").Append(name).Append(' ').Append(fi.Length).Append(' ').Append(fi.LastWriteTimeUtc.Ticks).Append(' ').Append(GrfVersion(fi.FullName)).Append('\n');
        }
        var data = Path.Combine(root, "data");
        if (Directory.Exists(data))
        {
            // only what the map cache reads: the loose map files (cells, water level), and the loose clientinfo files (when no
            // archive has one, theirs is the readfolder tag that puts the loose folder before or after the archives)
            var loose = Directory.EnumerateFiles(data, "*.gat").Concat(Directory.EnumerateFiles(data, "*.rsw"))
                .Concat(new[] { "sclientinfo.xml", "clientinfo.xml" }.Select(n => Path.Combine(data, n)).Where(File.Exists))
                .Select(f => new FileInfo(f)).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
            sb.Append("loose ").Append(loose.Count).Append('\n');
            foreach (var f in loose) sb.Append("loose ").Append(f.Name.ToLowerInvariant()).Append(' ').Append(f.Length).Append(' ').Append(f.LastWriteTimeUtc.Ticks).Append('\n');
        }
        else sb.Append("loose none\n");
        sb.Append("patch ").Append(PatchNumber(root)?.ToString() ?? "none").Append('\n');
        return sb.ToString();
    }

    /// <summary>"0x300" from the GRF's 46-byte header (the version is the uint32 at 42, client IO/GrfArchive.cs ReadTable), or
    /// why it could not be read. The file is not locked.</summary>
    static string GrfVersion(string path)
    {
        try
        {
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var h = new byte[46];
            int n = 0, k;
            while (n < h.Length && (k = f.Read(h, n, h.Length - n)) > 0) n += k;
            if (n < h.Length) return "short";
            return "0x" + (BitConverter.ToUInt32(h, 42) & 0xFF00).ToString("x");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return "unreadable"; }
    }

    /// <summary>The newest write time among the files the stamp covers (a patch in progress shows as "written just now").</summary>
    public static DateTime NewestWriteUtc(string root)
    {
        var t = DateTime.MinValue;
        foreach (var name in GrfNames(root))
        {
            var fi = new FileInfo(Path.Combine(root, name));
            if (fi.Exists && fi.LastWriteTimeUtc > t) t = fi.LastWriteTimeUtc;
        }
        return t;
    }
}
