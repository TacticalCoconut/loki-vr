using System.Diagnostics;
using System.Runtime.Versioning;
using MySqlConnector;

namespace LokiHost.Core;

/// <summary>
/// Loki Host's own MariaDB instance (the design's choice, 2026-09-30: nobody knew the installed MariaDB's root password). It is a
/// separate server process on the MariaDB programs already installed on the PC: its own data folder, its own port on
/// 127.0.0.1 only, its own random root password kept in the settings file, started and stopped with the rAthena servers
/// (and on demand for the Accounts tab). The installed MariaDB service and its data are never touched. No service, no
/// scheduled task: the process lives only while Loki Host needs it. Tested this way on a test PC with MariaDB 13.0.2
/// (mariadb-install-db + mariadbd, 2026-09-30, the scratch host of the Windows verification).
/// </summary>
public sealed class PrivateDb
{
    readonly HostSettings _s;
    Process? _p;
    public event Action<string>? Log;

    public PrivateDb(HostSettings s) { _s = s; }

    public static string DataDir(HostSettings s) => Path.Combine(s.PrivateDbDir, "data");
    public static string IniPath(HostSettings s) => Path.Combine(DataDir(s), "my.ini");

    /// <summary>
    /// The server's own temporary folder, beside "data" in its folder (wfollow, 2026-10-14). Without one MariaDB uses %TEMP%, which
    /// every MariaDB of the same Windows user shares, and a MariaDB that starts deletes every "#sql*" file in its temporary folder:
    /// one database starting took another's temporary tables away mid-query (w26int: the parallel test run; measured with probe
    /// files: a plain start's @@tmpdir is %TEMP% and its probe there was gone; with --tmpdir the %TEMP% probe stayed and the one in
    /// this folder went). Given on the command line, so the my.ini of an existing install is never rewritten; created when
    /// missing, nothing in it or beside it removed by Loki Host. Not inside "data": a folder there would be a database.
    /// </summary>
    public static string TmpDir(HostSettings s) => Path.Combine(s.PrivateDbDir, "tmp");

    /// <summary>A folder holding the MariaDB server and its install tool (mariadbd.exe / mysqld.exe, mariadb-install-db.exe /
    /// mysql_install_db.exe on Windows), or null.</summary>
    public static bool IsBinDir(string? dir) =>
        !string.IsNullOrEmpty(dir) && Directory.Exists(dir) && ServerExe(dir) != null && InstallExe(dir) != null;

    static string? First(string dir, params string[] names) =>
        names.Select(n => Path.Combine(dir, n)).FirstOrDefault(File.Exists);

    public static string? ServerExe(string dir) => OperatingSystem.IsWindows() ? First(dir, "mariadbd.exe", "mysqld.exe") : First(dir, "mariadbd", "mysqld");
    public static string? InstallExe(string dir) => OperatingSystem.IsWindows() ? First(dir, "mariadb-install-db.exe", "mysql_install_db.exe") : First(dir, "mariadb-install-db", "mysql_install_db");

    /// <summary>
    /// Where the installed MariaDB's programs are: the settings' own choice when it holds them, else the folder of a Windows
    /// service running mariadbd / mysqld (its ImagePath), else the newest "MariaDB *" folder under Program Files.
    /// </summary>
    public static string? FindBin(string? preferred)
    {
        if (IsBinDir(preferred)) return preferred;
        if (OperatingSystem.IsWindows())
        {
            foreach (var dir in ServiceBinDirs()) if (IsBinDir(dir)) return dir;
            foreach (var pf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
            {
                if (string.IsNullOrEmpty(pf) || !Directory.Exists(pf)) continue;
                foreach (var d in Directory.EnumerateDirectories(pf, "MariaDB *").OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase))
                    if (IsBinDir(Path.Combine(d, "bin"))) return Path.Combine(d, "bin");
            }
        }
        return null;
    }

    [SupportedOSPlatform("windows")]
    static IEnumerable<string> ServiceBinDirs()
    {
        var found = new List<string>();
        try
        {
            using var services = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services == null) return found;
            foreach (var name in services.GetSubKeyNames())
            {
                using var k = services.OpenSubKey(name);
                var image = k?.GetValue("ImagePath") as string;
                if (image == null) continue;
                var exe = ExeFromImagePath(image);
                if (exe == null) continue;
                var file = Path.GetFileName(exe).ToLowerInvariant();
                if (file is "mariadbd.exe" or "mysqld.exe") found.Add(Path.GetDirectoryName(exe)!);
            }
        }
        catch (Exception) { /* no access: fall back to Program Files */ }
        return found;
    }

    /// <summary>The program of a service's ImagePath: "C:\x\mysqld.exe" --opt … (quoted) or C:\x\mysqld.exe --opt (unquoted).</summary>
    public static string? ExeFromImagePath(string image)
    {
        image = image.Trim();
        if (image.StartsWith('"')) { int e = image.IndexOf('"', 1); return e > 1 ? image[1..e] : null; }
        int sp = image.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return sp > 0 ? image[..(sp + 4)] : null;
    }

    /// <summary>The first free TCP port from <paramref name="from"/> (3307: next to MariaDB's usual 3306).</summary>
    public static async Task<int> FreePortAsync(int from)
    {
        for (int p = from; p < from + 50; p++) if (!await Net.ListeningLocallyAsync(p)) return p;
        throw new InvalidOperationException($"no free port between {from} and {from + 49} for Loki Host's database");
    }

    /// <summary>
    /// my.ini as mariadb-install-db writes it, with bind-address=127.0.0.1 added under [mysqld] (the install tool makes the
    /// server listen on every address; rAthena connects on 127.0.0.1, nobody else needs it). Idempotent.
    /// </summary>
    public static string WithLocalBind(string ini)
    {
        var lines = ini.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Any(l => l.Trim().StartsWith("bind-address", StringComparison.OrdinalIgnoreCase))) return ini;
        int i = lines.FindIndex(l => l.Trim().Equals("[mysqld]", StringComparison.OrdinalIgnoreCase));
        if (i < 0) { lines.Insert(0, "[mysqld]"); i = 0; }
        lines.Insert(i + 1, "bind-address=127.0.0.1");
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Is the data folder already made (by an earlier setup that stopped later on)?</summary>
    public static bool Initialized(HostSettings s) => File.Exists(IniPath(s)) && Directory.Exists(Path.Combine(DataDir(s), "mysql"));

    /// <summary>
    /// Makes the data folder once: mariadb-install-db with the private port, a named pipe of its own and the root password;
    /// then the local-only bind. Refuses a data folder that exists with other files (nothing is overwritten).
    /// </summary>
    public void Init(Action<string> log)
    {
        var bin = FindBin(_s.MariaDbBin) ?? throw new InvalidOperationException("MariaDB's programs were not found (an installed MariaDB is needed; choose its bin folder in Setup)");
        _s.MariaDbBin = bin;
        var data = DataDir(_s);
        if (Initialized(_s)) { log($"Loki Host's database folder is already made: {data}"); return; }
        if (Directory.Exists(data) && Directory.EnumerateFileSystemEntries(data).Any())
            throw new InvalidOperationException($"{data} is not empty and is not a database Loki Host made; choose another folder (nothing is overwritten)");
        if (string.IsNullOrEmpty(_s.PrivateDbRootPass)) _s.PrivateDbRootPass = Database.RandomSecret();
        Directory.CreateDirectory(_s.PrivateDbDir);
        log($"Making Loki Host's own database in {data} (MariaDB programs: {bin}) …");
        var psi = new ProcessStartInfo(InstallExe(bin)!)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        psi.ArgumentList.Add($"--datadir={data}");
        psi.ArgumentList.Add($"--port={_s.PrivateDbPort}");
        psi.ArgumentList.Add($"--socket=lokihost_{_s.PrivateDbPort}");
        psi.ArgumentList.Add($"--password={_s.PrivateDbRootPass}");
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0 || !File.Exists(IniPath(_s)))
            throw new InvalidOperationException($"MariaDB's install tool failed (exit code {p.ExitCode}): {output.Trim()}");
        File.WriteAllText(IniPath(_s), WithLocalBind(File.ReadAllText(IniPath(_s))));
        log("Loki Host's database is made (it listens on 127.0.0.1 only).");
    }

    public string RootConnStr(string? db = null) => Database.ConnStr("127.0.0.1", _s.PrivateDbPort, "root", _s.PrivateDbRootPass, db);

    /// <summary>Does our own database answer (with our root password)? Also true for one a closed Loki Host left running.</summary>
    public bool Answers() => Database.Probe(RootConnStr()) == null;

    public bool Running => _p is { HasExited: false } || Answers();

    /// <summary>Starts the private server unless it already answers; waits until it accepts our root login.</summary>
    public async Task StartAsync(Action<string> log, int waitSeconds = 60)
    {
        if (Answers()) return;
        if (!Initialized(_s)) throw new InvalidOperationException("Loki Host's database is not made yet: run the setup.");
        if (await Net.ListeningLocallyAsync(_s.PrivateDbPort))
            throw new InvalidOperationException($"Port {_s.PrivateDbPort} is in use by another program, not Loki Host's database.");
        var bin = FindBin(_s.MariaDbBin) ?? throw new InvalidOperationException("MariaDB's programs were not found.");
        log($"Starting Loki Host's database (port {_s.PrivateDbPort}) …");
        var tmp = TmpDir(_s);
        Directory.CreateDirectory(tmp); // made when missing; an existing folder is left as it is
        var psi = new ProcessStartInfo(ServerExe(bin)!)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        psi.ArgumentList.Add($"--defaults-file={IniPath(_s)}"); // MariaDB wants it first
        psi.ArgumentList.Add("--console");
        psi.ArgumentList.Add($"--tmpdir={tmp}");
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) Log?.Invoke(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Log?.Invoke(e.Data); };
        p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
        _p = p;
        var deadline = DateTime.UtcNow.AddSeconds(waitSeconds);
        while (!Answers())
        {
            if (p.HasExited) throw new InvalidOperationException($"Loki Host's database stopped while starting (exit code {p.ExitCode}): see the log.");
            if (DateTime.UtcNow > deadline) throw new InvalidOperationException($"Loki Host's database did not answer within {waitSeconds} s.");
            await Task.Delay(300);
        }
        log("Loki Host's database is running.");
    }

    /// <summary>The id of the server process this instance started (null when it started none, or found one already running).</summary>
    public int? StartedProcessId => _p is { HasExited: false } ? _p.Id : null;

    /// <summary>
    /// A clean shutdown (the SHUTDOWN statement: MariaDB flushes and closes its files), forced only after 60 s.
    /// It waits for the server PROGRAM to end, not only for it to stop answering: MariaDB closes its connections first and
    /// only then shuts InnoDB down (seen on a test PC, 2026-09-30: 3 s between "Normal shutdown" and "Shutdown complete"),
    /// and a Start in that gap can meet the old process's file locks. When this instance did not start the server (a Loki Host
    /// that found it running: left by a closed or crashed window, or started by the Accounts tab of another one), the program is
    /// found by its command line (<see cref="FindServerProcess"/>, as ServerProcess.Adopt finds rAthena's servers by their path)
    /// before the SHUTDOWN is sent. If it cannot be found (its command line unreadable), the old rule stays: no answer = stopped,
    /// and the log says the program was not waited for. (Client wave 5, w5friends: the StopAsync gap.)
    /// </summary>
    public async Task StopAsync(Action<string> log, int waitSeconds = 60)
    {
        if (!Answers() && _p is not { HasExited: false }) return;
        var proc = _p is { HasExited: false } ? _p : FindServerProcess(_s);
        bool started = proc != null && proc == _p;
        log("Stopping Loki Host's database …");
        try
        {
            using var c = new MySqlConnection(RootConnStr());
            await c.OpenAsync();
            using var cmd = new MySqlCommand("SHUTDOWN", c);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception e) when (e is MySqlException or InvalidOperationException or System.IO.IOException) { /* the connection closes as it shuts down */ }
        var deadline = DateTime.UtcNow.AddSeconds(waitSeconds);
        while (DateTime.UtcNow < deadline && (Answers() || Alive(proc))) await Task.Delay(300);
        if (Alive(proc))
        {
            log($"Loki Host's database had to be ended by force (program {proc!.Id} still running {waitSeconds} s after SHUTDOWN).");
            try { proc.Kill(); } catch (InvalidOperationException) { }
        }
        else if (proc == null) log("Loki Host's database stopped answering (its program was not found, so its last steps were not waited for).");
        else log(started ? "Loki Host's database stopped." : $"Loki Host's database stopped (program {proc.Id}, which this window had not started, has ended).");
        if (!started) proc?.Dispose();
        _p = null;
    }

    static bool Alive(Process? p)
    {
        if (p == null) return false;
        try { return !p.HasExited; } catch (InvalidOperationException) { return false; } catch (System.ComponentModel.Win32Exception) { return false; }
    }

    /// <summary>
    /// The running MariaDB server of this data folder: a mariadbd / mysqld process whose command line has
    /// <c>--defaults-file=</c> this folder's my.ini (StartAsync starts it so; the installed MariaDB service runs with its own
    /// my.ini, so it is never taken). Null when none runs or no command line could be read.
    /// </summary>
    public static Process? FindServerProcess(HostSettings s)
    {
        var ini = IniPath(s);
        foreach (var name in new[] { "mariadbd", "mysqld" })
            foreach (var p in Process.GetProcessesByName(name))
            {
                string? cmd = null;
                try { cmd = CommandLine.Of(p.Id); } catch (Exception) { /* another user's process, or it ended */ }
                if (cmd != null && CommandLine.HasDefaultsFile(cmd, ini, OperatingSystem.IsWindows())) return p;
                p.Dispose();
            }
        return null;
    }
}

/// <summary>Another process's command line (Windows: NtQueryInformationProcess ProcessCommandLineInformation; Linux: /proc), and
/// the parts of it PrivateDb needs.</summary>
public static class CommandLine
{
    /// <summary>The command line of process <paramref name="pid"/> as one string (Linux: the arguments joined by NUL), or null.</summary>
    public static string? Of(int pid)
    {
        if (OperatingSystem.IsWindows()) return OfWindows(pid);
        var f = $"/proc/{pid}/cmdline";
        return File.Exists(f) ? File.ReadAllText(f) : null;
    }

    /// <summary>The arguments of a command line: Linux's NUL-separated /proc form, or Windows' quoting rules (Microsoft's "Parsing
    /// C++ command-line arguments": white space separates; "…" groups and the quotes go; 2n backslashes + quote = n backslashes and
    /// the quote toggles, 2n+1 backslashes + quote = n backslashes and a literal quote; other backslashes are literal).</summary>
    public static List<string> Split(string cmd)
    {
        if (cmd.Contains('\0')) return cmd.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
        var args = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool quoted = false, any = false;
        for (int i = 0; i < cmd.Length; i++)
        {
            char c = cmd[i];
            if (c == '\\')
            {
                int n = 0; while (i < cmd.Length && cmd[i] == '\\') { n++; i++; }
                if (i < cmd.Length && cmd[i] == '"')
                {
                    cur.Append('\\', n / 2);
                    if (n % 2 == 1) cur.Append('"'); else quoted = !quoted;
                    any = true;
                }
                else { cur.Append('\\', n); i--; any = true; }
                continue;
            }
            if (c == '"') { quoted = !quoted; any = true; continue; }
            if (!quoted && (c == ' ' || c == '\t'))
            {
                if (any) { args.Add(cur.ToString()); cur.Clear(); any = false; }
                continue;
            }
            cur.Append(c); any = true;
        }
        if (any) args.Add(cur.ToString());
        return args;
    }

    /// <summary>Does this command line run with <c>--defaults-file=</c><paramref name="iniPath"/> (the same file, after full-path
    /// normalisation; case-insensitive on Windows)?</summary>
    public static bool HasDefaultsFile(string cmd, string iniPath, bool windows)
    {
        const string opt = "--defaults-file=";
        string want;
        try { want = Path.GetFullPath(iniPath); } catch (Exception) { return false; }
        foreach (var a in Split(cmd))
        {
            if (!a.StartsWith(opt, StringComparison.OrdinalIgnoreCase)) continue;
            string got;
            try { got = Path.GetFullPath(a[opt.Length..]); } catch (Exception) { continue; }
            if (string.Equals(got, want, windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return true;
        }
        return false;
    }

    [SupportedOSPlatform("windows")]
    static string? OfWindows(int pid)
    {
        const uint QueryLimited = 0x1000; // PROCESS_QUERY_LIMITED_INFORMATION
        const int ProcessCommandLineInformation = 60; // Windows 8.1 and later
        var h = OpenProcess(QueryLimited, false, (uint)pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            NtQueryInformationProcess(h, ProcessCommandLineInformation, IntPtr.Zero, 0, out uint need);
            if (need == 0 || need > 1 << 20) return null;
            var buf = System.Runtime.InteropServices.Marshal.AllocHGlobal((int)need);
            try
            {
                if (NtQueryInformationProcess(h, ProcessCommandLineInformation, buf, need, out _) != 0) return null;
                // a UNICODE_STRING (Length, MaximumLength, Buffer) at the start, its characters after it in the same buffer
                int bytes = System.Runtime.InteropServices.Marshal.ReadInt16(buf);
                var text = System.Runtime.InteropServices.Marshal.ReadIntPtr(buf, IntPtr.Size);
                return text == IntPtr.Zero ? null : System.Runtime.InteropServices.Marshal.PtrToStringUni(text, (ushort)bytes / 2);
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buf); }
        }
        finally { CloseHandle(h); }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
    [System.Runtime.InteropServices.DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr h, int cls, IntPtr info, uint len, out uint ret);
}
