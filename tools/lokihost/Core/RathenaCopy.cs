namespace LokiHost.Core;

/// <summary>
/// The host's own copy of a built rAthena (as testrig/oracle/private.sh makes an oracle copy): the programs and their
/// DLLs (a Windows build puts login-server.exe, char-server.exe, map-server.exe and libmysql.dll / zlib.dll /
/// pcre8.dll in the solution folder: src/map/map-server.vcxproj OutDir $(SolutionDir)), plus conf, db, npc and
/// sql-files. The source is only read.
/// </summary>
public static class RathenaCopy
{
    public static readonly string[] Servers = { "login-server", "char-server", "map-server" };
    static readonly string[] Dirs = { "conf", "db", "npc", "sql-files" };

    public static string ExeName(string server) => OperatingSystem.IsWindows() ? server + ".exe" : server;

    /// <summary>What is missing from a source folder (empty = usable).</summary>
    public static List<string> Missing(string dir)
    {
        var m = new List<string>();
        if (!Directory.Exists(dir)) { m.Add(dir); return m; }
        foreach (var s in Servers)
            if (!File.Exists(Path.Combine(dir, ExeName(s)))) m.Add(ExeName(s));
        foreach (var d in Dirs)
            if (!Directory.Exists(Path.Combine(dir, d))) m.Add(d + "/");
        return m;
    }

    /// <summary>The file a Loki Host copy starts with (so a later setup can tell its own unfinished copy from anything else).</summary>
    public const string Marker = "lokihost-copy.txt";

    /// <summary>
    /// Does this folder hold a copy Loki Host's setup made? The marker, or (a copy made before the marker existed: the design's first
    /// setup, 2026-09-30) what only RemoveTestSettings writes: groups.yml.loki-test, or its "(Loki Host: test NPC, off)" lines.
    /// A folder that merely holds an rAthena (a standard build!) never qualifies.
    /// </summary>
    public static bool IsOwnCopy(string dst)
    {
        if (File.Exists(Path.Combine(dst, Marker))) return true;
        if (File.Exists(Path.Combine(dst, "conf", "import", "groups.yml.loki-test"))) return true;
        var sc = Path.Combine(dst, "npc", "scripts_custom.conf");
        return File.Exists(sc) && File.ReadAllText(sc).Contains("// (Loki Host: test NPC, off)");
    }

    /// <summary>
    /// Copies a source build into an empty (or new) host folder. Refuses a folder that already has files, unless
    /// <paramref name="resume"/>: the folder is this setup's own unfinished copy (a setup that stopped at a later step, e.g. a
    /// refused database login); then only the files still missing are copied and nothing already there is overwritten (the
    /// test settings RemoveTestSettings changed stay changed).
    /// </summary>
    public static void Create(string src, string dst, Action<string>? log = null, bool resume = false)
    {
        var missing = Missing(src);
        if (missing.Count > 0) throw new InvalidOperationException("Not a built rAthena folder (missing: " + string.Join(", ", missing) + ")");
        bool used = Directory.Exists(dst) && Directory.EnumerateFileSystemEntries(dst).Any();
        if (used && !resume)
            throw new InvalidOperationException($"{dst} is not empty; choose a new folder (nothing is overwritten)");
        if (used) log?.Invoke($"{dst} holds the copy an earlier, unfinished setup made: only missing files are copied");
        Directory.CreateDirectory(dst);
        if (!File.Exists(Path.Combine(dst, Marker))) File.WriteAllText(Path.Combine(dst, Marker), $"A copy of {src} made by Loki Host's setup ({DateTime.Now:yyyy-MM-dd HH:mm}).{Environment.NewLine}");
        CopyPrograms(src, dst, log, used);
        foreach (var d in Dirs)
        {
            log?.Invoke($"Copying {d}/ …");
            int n = CopyTree(Path.Combine(src, d), Path.Combine(dst, d), used);
            if (used) log?.Invoke($"  {n} missing file(s) copied");
        }
        Directory.CreateDirectory(Path.Combine(dst, "log"));
    }

    /// <summary>
    /// Copies the programs again (after rebuilding rAthena). The host's current ones are moved to
    /// _to_delete\&lt;time&gt;\ first, never deleted.
    /// </summary>
    public static void UpdatePrograms(string src, string dst, Action<string>? log = null)
    {
        var old = TopLevelPrograms(dst).ToList();
        if (old.Count > 0)
        {
            var keep = Path.Combine(dst, "_to_delete", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(keep);
            foreach (var f in old) File.Move(f, Path.Combine(keep, Path.GetFileName(f)));
            log?.Invoke($"Moved {old.Count} old program files to {keep}");
        }
        CopyPrograms(src, dst, log);
    }

    static IEnumerable<string> TopLevelPrograms(string dir) =>
        Directory.EnumerateFiles(dir).Where(f =>
        {
            var n = Path.GetFileName(f);
            var e = Path.GetExtension(f).ToLowerInvariant();
            return e is ".exe" or ".dll" || (!OperatingSystem.IsWindows() && Servers.Contains(n));
        });

    static void CopyPrograms(string src, string dst, Action<string>? log, bool skipExisting = false)
    {
        int n = 0;
        foreach (var f in TopLevelPrograms(src))
        {
            var to = Path.Combine(dst, Path.GetFileName(f));
            if (skipExisting && File.Exists(to)) continue;
            File.Copy(f, to);
            n++;
        }
        log?.Invoke($"Copied {n} program files");
    }
    static int CopyTree(string src, string dst, bool skipExisting = false)
    {
        int n = 0;
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src))
        {
            var to = Path.Combine(dst, Path.GetFileName(f));
            if (skipExisting && File.Exists(to)) continue;
            File.Copy(f, to); n++;
        }
        foreach (var d in Directory.EnumerateDirectories(src)) n += CopyTree(d, Path.Combine(dst, Path.GetFileName(d)), skipExisting);
        return n;
    }

    /// <summary>
    /// Takes the parity rig's test settings out of the copy (server/data/overlay): the groups import that makes every
    /// account a GM goes back to rAthena's empty one (the old file is kept as groups.yml.loki-test), and the test NPC
    /// lines of npc/scripts_custom.conf are commented out. Game settings (rates, sit regen, pincode) stay.
    /// </summary>
    public static List<string> RemoveTestSettings(string dst)
    {
        var done = new List<string>();
        var groups = Path.Combine(dst, "conf", "import", "groups.yml");
        if (Conf.GroupsImportElevatesPlayers(groups))
        {
            var keep = groups + ".loki-test";
            if (File.Exists(keep)) keep += "." + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Move(groups, keep);
            File.WriteAllText(groups, Conf.DefaultGroupsImport);
            done.Add("conf/import/groups.yml gave every account GM commands: reset to rAthena's default (old file kept as groups.yml.loki-test)");
        }
        int n = Conf.DisableTestNpcs(Path.Combine(dst, "npc", "scripts_custom.conf"));
        if (n > 0) done.Add($"npc/scripts_custom.conf: {n} test NPC files turned off");
        return done;
    }
}
