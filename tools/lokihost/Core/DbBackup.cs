using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace LokiHost.Core;

/// <summary>
/// A backup of the host's database at every Start (design note, 2026-10-07: "Turn on both, the zeny log and the startup backup"):
/// once the database answers and before the login / char / map servers start, MariaDB's own dump tool (mariadb-dump.exe, or
/// mysqldump.exe, from the MariaDB programs' folder) writes the database <see cref="HostSettings.DbName"/> as SQL, which is
/// gzip-compressed into <c>backups\&lt;db&gt;-&lt;yyyy-MM-dd_HH-mm-ss&gt;.sql.gz</c> beside the settings file
/// (%APPDATA%\LokiHost\backups).
/// - Consistent: --single-transaction (InnoDB tables at one moment; rAthena's MyISAM tables are quiet anyway, since no rAthena server
///   runs yet). No routines, events or tablespaces (rAthena has none; a tablespace dump would need the PROCESS privilege).
/// - The password travels in the dump tool's environment (MYSQL_PWD), never on its command line and never in a log; --no-defaults
///   keeps an option file on this PC from changing the login.
/// - No half files: the dump is written to <c>&lt;name&gt;.sql.gz.incomplete</c> and renamed only when the tool ended well and its
///   last line is "-- Dump completed"; a failed dump stays under its .incomplete name.
/// - Nothing is ever deleted or overwritten: a name that exists gets _2, _3 … Old backups are removed by hand.
/// Never fatal: the servers start whatever happens here (the caller shows the failure).
/// </summary>
public static class DbBackup
{
    public const string Extension = ".sql.gz";
    public const string IncompleteExtension = ".incomplete";
    /// <summary>mariadb-dump's last line when it finished (it is not written with --skip-comments, which is not used).</summary>
    public const string CompletedMarker = "-- Dump completed";

    /// <summary>backups\ beside the settings file: %APPDATA%\LokiHost\backups for Loki Host's own settings, the scratch folder's for a
    /// --settings file.</summary>
    public static string Folder(HostSettings s) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(s.FilePath ?? HostSettings.DefaultPath))!, "backups");

    /// <summary><c>&lt;db&gt;-&lt;yyyy-MM-dd_HH-mm-ss&gt;.sql.gz</c> (the PC's local time: what the host reads; it sorts by name).</summary>
    public static string FileName(string db, DateTime localTime) =>
        $"{db}-{localTime.ToString("yyyy-MM-dd_HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture)}{Extension}";

    /// <summary>The dump tool in MariaDB's programs' folder: mariadb-dump, else mysqldump (.exe on Windows), or null.</summary>
    public static string? DumpExe(string? binDir)
    {
        if (string.IsNullOrEmpty(binDir) || !Directory.Exists(binDir)) return null;
        var names = OperatingSystem.IsWindows() ? new[] { "mariadb-dump.exe", "mysqldump.exe" } : new[] { "mariadb-dump", "mysqldump" };
        return names.Select(n => Path.Combine(binDir, n)).FirstOrDefault(File.Exists);
    }

    /// <summary>The dump tool's arguments (no password: that one is in MYSQL_PWD).</summary>
    public static List<string> Arguments(HostSettings s) => new()
    {
        "--no-defaults", // (must come first) an option file on this PC must not change the login or the output
        "--protocol=TCP", $"--host={s.DbHost}", $"--port={s.DbPort}", $"--user={s.DbUser}",
        "--single-transaction", "--skip-routines", "--skip-events", "--no-tablespaces", "--hex-blob",
        "--default-character-set=utf8mb4",
        s.DbName,
    };

    /// <summary>What one backup did (Ok: Path / Bytes are the finished file; else Error says why).</summary>
    public sealed record Result(bool Ok, DateTime Time, string? Path, long Bytes, string Error)
    {
        public string Summary => Ok
            ? $"Database backup: {Time:yyyy-MM-dd HH:mm:ss}, {Size(Bytes)}, {System.IO.Path.GetFileName(Path)} in {System.IO.Path.GetDirectoryName(Path)}"
            : $"Database backup FAILED ({Time:yyyy-MM-dd HH:mm:ss}): {Error}. The servers start anyway.";
    }

    /// <summary>The newest finished backup in <paramref name="folder"/> (by its name's time, then the file's), or null.</summary>
    public static Result? Latest(string folder)
    {
        if (!Directory.Exists(folder)) return null;
        var f = new DirectoryInfo(folder).EnumerateFiles("*" + Extension)
            .Where(x => x.Name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault();
        return f == null ? null : new Result(true, f.LastWriteTime, f.FullName, f.Length, "");
    }

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes / (1024.0 * 1024):0.0} MB",
    };

    /// <summary>
    /// A name in <paramref name="folder"/> that neither exists nor has an .incomplete twin: <paramref name="name"/>, else
    /// name_2.sql.gz, name_3.sql.gz … (two backups in one second never overwrite each other).
    /// </summary>
    public static string FreePath(string folder, string name)
    {
        string stem = name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? name[..^Extension.Length] : name;
        for (int n = 1; ; n++)
        {
            var p = Path.Combine(folder, n == 1 ? stem + Extension : $"{stem}_{n}{Extension}");
            if (!File.Exists(p) && !File.Exists(p + IncompleteExtension)) return p;
        }
    }

    /// <summary>
    /// Writes a gzip-compressed file the safe way: <paramref name="fill"/> writes the uncompressed content into a gzip stream on
    /// <c>&lt;path&gt;.incomplete</c> (created new, never over an existing file); when it returns true the file is flushed to the disk
    /// and renamed to <paramref name="path"/> (never over an existing file). When it returns false or throws, the .incomplete file
    /// stays as it is (nothing is deleted) and nothing is called <paramref name="path"/>. Returns the size of the finished file.
    /// </summary>
    public static async Task<long> WriteGzipAsync(string path, Func<Stream, Task<bool>> fill)
    {
        var temp = path + IncompleteExtension;
        bool ok;
        await using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1 << 16, useAsync: true))
        {
            await using (var gz = new GZipStream(fs, CompressionLevel.Optimal, leaveOpen: true))
                ok = await fill(gz);
            if (ok) fs.Flush(flushToDisk: true);
        }
        if (!ok) throw new IOException($"the dump did not finish; what was written is kept as {Path.GetFileName(temp)}");
        File.Move(temp, path, overwrite: false);
        return new FileInfo(path).Length;
    }

    /// <summary>
    /// Dumps the host's database now. Never throws: a failure is the Result's Error (and the .incomplete file, if one was begun,
    /// stays). <paramref name="binDir"/>: MariaDB's programs' folder (null = <see cref="PrivateDb.FindBin"/> of the settings').
    /// </summary>
    public static async Task<Result> RunAsync(HostSettings s, string? binDir = null, DateTime? now = null, TimeSpan? timeout = null)
    {
        var time = now ?? DateTime.Now;
        string stderr = "";
        int exit = -1;
        try
        {
            binDir ??= PrivateDb.FindBin(s.MariaDbBin) ?? (string.IsNullOrEmpty(s.MariaDbBin) ? null : s.MariaDbBin);
            var exe = DumpExe(binDir) ?? throw new InvalidOperationException(
                $"MariaDB's dump tool (mariadb-dump.exe or mysqldump.exe) was not found{(binDir == null ? " (MariaDB's programs were not found)" : " in " + binDir)}");
            var folder = Folder(s);
            Directory.CreateDirectory(folder);
            var path = FreePath(folder, FileName(s.DbName, time));
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var a in Arguments(s)) psi.ArgumentList.Add(a);
            psi.Environment["MYSQL_PWD"] = s.DbPass; // never on the command line (other users' tools can read it there), never logged

            long bytes = await WriteGzipAsync(path, async gz =>
            {
                using var p = Process.Start(psi) ?? throw new InvalidOperationException("the dump tool did not start");
                var err = p.StandardError.ReadToEndAsync();
                using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(10));
                var tail = new TailBuffer(256);
                try
                {
                    // off the caller's thread (the window's): the compression is CPU work
                    await Task.Run(async () =>
                    {
                        var src = p.StandardOutput.BaseStream;
                        var buf = new byte[1 << 16];
                        int n;
                        while ((n = await src.ReadAsync(buf, cts.Token)) > 0)
                        {
                            await gz.WriteAsync(buf.AsMemory(0, n), cts.Token);
                            tail.Add(buf, n);
                        }
                    }, cts.Token);
                    await p.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    try { p.Kill(); } catch (InvalidOperationException) { }
                    stderr = "it did not finish within " + (timeout ?? TimeSpan.FromMinutes(10)).TotalMinutes + " minutes (ended)";
                    return false;
                }
                stderr = (await err).Trim();
                exit = p.ExitCode;
                return exit == 0 && tail.Text.Contains(CompletedMarker);
            });
            return new Result(true, time, path, bytes, "");
        }
        catch (Exception e)
        {
            // the dump tool's own words (MariaDB's error: never the password, which it is not given on its command line)
            var why = e.Message.TrimEnd('.');
            if (exit > 0) why += $" (the dump tool's exit code {exit})";
            // (MariaDB 11.4+ clients warn that a local login does not verify the server's certificate: not the reason, left out)
            var words = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => !l.StartsWith("WARNING: option --ssl-verify-server-cert", StringComparison.OrdinalIgnoreCase)).ToList();
            if (words.Count > 0) why += ": " + string.Join(" ", words);
            return new Result(false, time, null, 0, why);
        }
    }

    sealed class TailBuffer
    {
        readonly byte[] _b; int _len;
        public TailBuffer(int size) { _b = new byte[size]; }
        public void Add(byte[] buf, int n)
        {
            if (n >= _b.Length) { Array.Copy(buf, n - _b.Length, _b, 0, _b.Length); _len = _b.Length; return; }
            int keep = Math.Min(_len, _b.Length - n);
            Array.Copy(_b, _len - keep, _b, 0, keep);
            Array.Copy(buf, 0, _b, keep, n);
            _len = keep + n;
        }
        public string Text => Encoding.UTF8.GetString(_b, 0, _len);
    }
}
