using LokiHost.Core;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>Loki Host's own MariaDB (PrivateDb) and the resumable setup.</summary>
public class PrivateDbTests
{
    readonly ITestOutputHelper _out;
    public PrivateDbTests(ITestOutputHelper o) { _out = o; }

    static string Temp()
    {
        var d = Path.Combine(Path.GetTempPath(), "lokihost-pdb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void The_private_server_listens_on_127_0_0_1_only()
    {
        // my.ini as MariaDB 13.0.2's mariadb-install-db wrote it on a test PC (C:\LokiVR\tmp\lhdb\data\my.ini)
        var ini = "[mysqld]\r\ndatadir=C:/LokiVR/tmp/lhdb/data\r\nnamed-pipe=ON\r\nsocket=lokihost_scratch\r\nport=3307\r\n[client]\r\nsocket=lokihost_scratch\r\nport=3307\r\n";
        var outIni = PrivateDb.WithLocalBind(ini);
        var lines = outIni.Replace("\r\n", "\n").Split('\n');
        Assert.Equal("[mysqld]", lines[0]);
        Assert.Equal("bind-address=127.0.0.1", lines[1]);
        Assert.Contains("port=3307", lines);
        Assert.Equal(outIni, PrivateDb.WithLocalBind(outIni)); // idempotent
    }

    [Fact]
    public void A_services_program_is_read_from_its_ImagePath()
    {
        // a local MariaDB service (HKLM\SYSTEM\CurrentControlSet\Services\MariaDB ImagePath, 2026-09-29)
        Assert.Equal(@"C:\Program Files\MariaDB 13.0\bin\mysqld.exe",
            PrivateDb.ExeFromImagePath("\"C:\\Program Files\\MariaDB 13.0\\bin\\mysqld.exe\" \"--defaults-file=C:\\Program Files\\MariaDB 13.0\\data\\my.ini\" \"MariaDB\""));
        Assert.Equal(@"C:\x\mysqld.exe", PrivateDb.ExeFromImagePath(@"C:\x\mysqld.exe --defaults-file=c:\x\my.ini MariaDB"));
        Assert.Null(PrivateDb.ExeFromImagePath("\""));
    }

    [Fact]
    public void A_server_is_known_by_the_defaults_file_on_its_command_line()
    {
        // Windows' quoting rules (the private server as StartAsync starts it; the installed service's ImagePath on a test PC)
        Assert.Equal(new[] { @"C:\Program Files\MariaDB 13.0\bin\mariadbd.exe", @"--defaults-file=C:\LokiHost\mariadb\data\my.ini", "--console" },
            CommandLine.Split("\"C:\\Program Files\\MariaDB 13.0\\bin\\mariadbd.exe\" --defaults-file=C:\\LokiHost\\mariadb\\data\\my.ini --console"));
        Assert.Equal(new[] { @"a\\b", "c\"d", @"e\", "f g" }, CommandLine.Split(@"a\\b c\""d e\ ""f g"""));
        Assert.Equal(new[] { "x", "y" }, CommandLine.Split("x\0y\0")); // Linux /proc/<pid>/cmdline
        var own = @"C:\LokiHost\mariadb\data\my.ini";
        if (OperatingSystem.IsWindows())
        {
            Assert.True(CommandLine.HasDefaultsFile("\"C:\\Program Files\\MariaDB 13.0\\bin\\mariadbd.exe\" --defaults-file=C:\\LokiHost\\mariadb\\data\\my.ini --console", own, true));
            Assert.True(CommandLine.HasDefaultsFile("mariadbd.exe \"--defaults-file=c:/lokihost/MariaDB/data/MY.INI\" --console", own, true)); // quoted, other case and slashes
            // since wfollow (2026-10-14) StartAsync adds its own temporary folder after them: still found
            Assert.True(CommandLine.HasDefaultsFile("\"C:\\Program Files\\MariaDB 13.0\\bin\\mariadbd.exe\" --defaults-file=C:\\LokiHost\\mariadb\\data\\my.ini --console --tmpdir=C:\\LokiHost\\mariadb\\tmp", own, true));
            // the installed MariaDB service (HKLM\SYSTEM\CurrentControlSet\Services\MariaDB ImagePath, 2026-09-29) is never taken
            Assert.False(CommandLine.HasDefaultsFile("\"C:\\Program Files\\MariaDB 13.0\\bin\\mysqld.exe\" \"--defaults-file=C:\\Program Files\\MariaDB 13.0\\data\\my.ini\" \"MariaDB\"", own, true));
            Assert.False(CommandLine.HasDefaultsFile("mariadbd.exe --console", own, true));
            Assert.False(CommandLine.HasDefaultsFile("mariadbd.exe --defaults-file=C:\\LokiHost\\mariadb\\data\\my.ini.old", own, true));
        }
        Assert.True(CommandLine.HasDefaultsFile("/usr/sbin/mariadbd\0--defaults-file=/tmp/h/data/my.ini\0--console\0", "/tmp/h/data/my.ini", false));
        Assert.False(CommandLine.HasDefaultsFile("/usr/sbin/mariadbd\0--defaults-file=/tmp/h/data/MY.INI\0", "/tmp/h/data/my.ini", false));
    }

    /// <summary>
    /// The StopAsync gap (client wave 5): a Loki Host that did not start its database (it found it running) stopped waiting as
    /// soon as the database stopped answering, while MariaDB still shuts InnoDB down. Now it waits for the program itself. A real
    /// private database in a temporary folder, on its own free port (from 17307), with the MariaDB programs installed on this PC: one
    /// instance starts it, a second one (a window that did not start it) stops it; when StopAsync returns, the program must have
    /// ended. Runs when MariaDB's programs are found (LOKIHOST_TEST_MARIADB_BIN or PrivateDb.FindBin); no admin password needed.
    /// </summary>
    /// <summary>1,000 rows doubled this many times (with three indexes), then all deleted before the stop.</summary>
    const int SlowRowsDoublings = 8;

    [Fact]
    public async Task A_window_that_did_not_start_the_database_waits_for_its_program_to_end()
    {
        var bin = PrivateDb.FindBin(Environment.GetEnvironmentVariable("LOKIHOST_TEST_MARIADB_BIN"));
        if (bin == null) { _out.WriteLine("MariaDB's programs not found: skipped"); return; }
        var root = Temp();
        var s = new HostSettings
        {
            HostDir = Path.Combine(root, "rathena"), FilePath = Path.Combine(root, "lokihost.json"), PrivateDb = true,
            PrivateDbDir = Path.Combine(root, "mariadb"), PrivateDbPort = await PrivateDb.FreePortAsync(17307), MariaDbBin = bin,
        };
        var log = new List<string>();
        void Log(string l) { lock (log) log.Add(l); _out.WriteLine(l); }
        var starter = new PrivateDb(s);
        starter.Init(Log);
        // nothing else runs on this data folder yet: the installed service and any other private database are never taken
        Assert.Null(PrivateDb.FindServerProcess(s));
        await starter.StartAsync(Log);
        int pid = starter.StartedProcessId ?? throw new InvalidOperationException("the database did not start");
        try
        {
            using (var found = PrivateDb.FindServerProcess(s)) Assert.Equal(pid, found?.Id);
            // work for the shutdown, as a played database has: InnoDB's slow shutdown (innodb_fast_shutdown 0: the purge of every
            // deleted row finishes before the files close) right after many rows were deleted. An empty database shuts down within
            // the ~2 s a refused connection takes on Windows, which hid the gap in the first runs against the original code.
            var hold = new MySqlConnector.MySqlConnection(starter.RootConnStr());
            await hold.OpenAsync();
            async Task Sql(string sql) => await new MySqlConnector.MySqlCommand(sql, hold) { CommandTimeout = 300 }.ExecuteNonQueryAsync();
            await Sql("CREATE DATABASE w5slow");
            await Sql("CREATE TABLE w5slow.t (id INT PRIMARY KEY AUTO_INCREMENT, pad CHAR(255), k INT, KEY (k), KEY (pad)) ENGINE=InnoDB");
            await Sql("INSERT INTO w5slow.t (pad, k) SELECT REPEAT('x', 255), 1 FROM information_schema.columns LIMIT 1000");
            for (int i = 0; i < SlowRowsDoublings; i++) await Sql("INSERT INTO w5slow.t (pad, k) SELECT LEFT(CONCAT(id, pad), 255), id FROM w5slow.t");
            await Sql("SET GLOBAL innodb_fast_shutdown = 0");
            await Sql("DELETE FROM w5slow.t");
            hold.Dispose();
            var other = new PrivateDb(s); // a second window: it finds the database running, it did not start it
            Assert.Null(other.StartedProcessId);
            Assert.True(other.Answers());
            var t0 = DateTime.Now;
            await other.StopAsync(Log);
            var returned = DateTime.Now;
            bool ended; DateTime? exitAt = null;
            try { using var p = System.Diagnostics.Process.GetProcessById(pid); ended = p.HasExited; if (ended) exitAt = p.ExitTime; } catch (ArgumentException) { ended = true; }
            _out.WriteLine($"StopAsync took {(returned - t0).TotalSeconds:0.0} s; the program had ended when it returned: {ended}{(exitAt != null ? $" (exit {(exitAt.Value - t0).TotalSeconds:0.0} s after the stop began)" : "")}");
            if (!ended)
                using (var p = System.Diagnostics.Process.GetProcessById(pid))
                {
                    p.WaitForExit(120_000);
                    _out.WriteLine($"… it ended {(DateTime.Now - returned).TotalSeconds:0.0} s after StopAsync had returned");
                }
            Assert.True(ended,$"mariadbd {pid} was still running when StopAsync returned");
            Assert.False(other.Answers());
            lock (log)
            {
                Assert.Contains(log, l => l.Contains($"program {pid}, which this window had not started, has ended"));
                Assert.DoesNotContain(log, l => l.Contains("by force"));
            }
        }
        finally
        {
            // never leave the test's server running
            try { using var p = System.Diagnostics.Process.GetProcessById(pid); if (!p.HasExited) { await starter.StopAsync(Log); } } catch (ArgumentException) { }
        }
    }

    /// <summary>
    /// wfollow (2026-10-14), the parallel run's failure (w26int): every private database kept its temporary tables in %TEMP%, and a
    /// MariaDB that starts deletes every "#sql*" file in its temporary folder, so one test database starting took another's
    /// temporary table away mid-query. Now each has its own folder (PrivateDb.TmpDir, beside "data"). Two real private databases,
    /// started at once: each one's @@tmpdir is its own folder (not %TEMP%), its @@datadir where it was, its my.ini byte for byte as
    /// Init wrote it, and a file already in the temporary folder is left. Then one holds a temporary table (its files in its own
    /// folder) while the other is stopped and started again: the table, its rows and its files are still there.
    /// </summary>
    [Fact]
    public async Task Two_private_databases_started_at_once_keep_their_own_temporary_files()
    {
        var bin = PrivateDb.FindBin(Environment.GetEnvironmentVariable("LOKIHOST_TEST_MARIADB_BIN"));
        if (bin == null) { _out.WriteLine("MariaDB's programs not found: skipped"); return; }
        void Log(string l) { lock (_out) _out.WriteLine(l); }
        async Task<HostSettings> Make(int from)
        {
            var root = Temp();
            return new HostSettings
            {
                HostDir = Path.Combine(root, "rathena"), FilePath = Path.Combine(root, "lokihost.json"), PrivateDb = true,
                PrivateDbDir = Path.Combine(root, "mariadb"), PrivateDbPort = await PrivateDb.FreePortAsync(from), MariaDbBin = bin,
            };
        }
        var sa = await Make(17307);
        var sb = await Make(sa.PrivateDbPort + 1);
        Assert.NotEqual(sa.PrivateDbPort, sb.PrivateDbPort);
        var a = new PrivateDb(sa);
        var b = new PrivateDb(sb);
        a.Init(Log);
        b.Init(Log);
        var iniA = File.ReadAllBytes(PrivateDb.IniPath(sa));
        var iniB = File.ReadAllBytes(PrivateDb.IniPath(sb));
        // A: as an install from before this (no temporary folder yet); B: its folder exists already with a file in it
        Assert.False(Directory.Exists(PrivateDb.TmpDir(sa)));
        Directory.CreateDirectory(PrivateDb.TmpDir(sb));
        var keep = Path.Combine(PrivateDb.TmpDir(sb), "keep.txt");
        File.WriteAllText(keep, "not MariaDB's");
        await Task.WhenAll(a.StartAsync(Log), b.StartAsync(Log));
        try
        {
            Assert.NotNull(a.StartedProcessId);
            Assert.NotNull(b.StartedProcessId);
            static string Norm(string p) => Path.GetFullPath(p).TrimEnd('\\', '/');
            async Task<string> Var(PrivateDb d, string name)
            {
                await using var c = new MySqlConnector.MySqlConnection(d.RootConnStr());
                await c.OpenAsync();
                await using var cmd = new MySqlConnector.MySqlCommand("SELECT @@" + name, c);
                return Convert.ToString(await cmd.ExecuteScalarAsync()) ?? "";
            }
            foreach (var (s, d) in new[] { (sa, a), (sb, b) })
            {
                var tmp = await Var(d, "tmpdir");
                _out.WriteLine($"port {s.PrivateDbPort}: @@tmpdir {tmp}");
                Assert.Equal(Norm(PrivateDb.TmpDir(s)), Norm(tmp), ignoreCase: true);
                Assert.NotEqual(Norm(Path.GetTempPath()), Norm(tmp), StringComparer.OrdinalIgnoreCase);
                Assert.Equal(Norm(PrivateDb.DataDir(s)), Norm(await Var(d, "datadir")), ignoreCase: true);
            }
            Assert.NotEqual(Norm(PrivateDb.TmpDir(sa)), Norm(PrivateDb.TmpDir(sb)), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(iniA, File.ReadAllBytes(PrivateDb.IniPath(sa)));
            Assert.Equal(iniB, File.ReadAllBytes(PrivateDb.IniPath(sb)));
            Assert.Equal("not MariaDB's", File.ReadAllText(keep));

            // A holds a temporary table on disk (MyISAM: its files are made in the temporary folder) ...
            await using var hold = new MySqlConnector.MySqlConnection(a.RootConnStr());
            await hold.OpenAsync();
            async Task<object?> Sql(string q) { await using var cmd = new MySqlConnector.MySqlCommand(q, hold); return await cmd.ExecuteScalarAsync(); }
            await Sql("CREATE DATABASE wfollow");
            await Sql("CREATE TEMPORARY TABLE wfollow.t (id INT PRIMARY KEY, pad CHAR(200)) ENGINE=MyISAM");
            await Sql("INSERT INTO wfollow.t VALUES (1, 'a'), (2, 'b'), (3, 'c')");
            var files = Directory.GetFiles(PrivateDb.TmpDir(sa), "#sql*");
            _out.WriteLine("A's temporary files: " + string.Join(", ", files.Select(Path.GetFileName)));
            Assert.NotEmpty(files);
            // ... while B stops and starts again (a MariaDB that starts deletes the "#sql*" files of ITS temporary folder)
            await b.StopAsync(Log);
            await b.StartAsync(Log);
            Assert.All(files, f => Assert.True(File.Exists(f), f + " was deleted"));
            Assert.Equal(3L, Convert.ToInt64(await Sql("SELECT COUNT(*) FROM wfollow.t")));
            await Sql("DROP TEMPORARY TABLE wfollow.t");
            Assert.Equal("not MariaDB's", File.ReadAllText(keep));
        }
        finally
        {
            // never leave the test's servers running
            await a.StopAsync(Log);
            await b.StopAsync(Log);
        }
        Assert.False(a.Answers());
        Assert.False(b.Answers());
    }

    static string FakeBuild(string root)
    {
        var src = Path.Combine(root, "src");
        foreach (var d in new[] { "conf/import", "db", "npc", "sql-files" }) Directory.CreateDirectory(Path.Combine(src, d));
        foreach (var s in RathenaCopy.Servers) File.WriteAllText(Path.Combine(src, RathenaCopy.ExeName(s)), "exe");
        File.WriteAllText(Path.Combine(src, "conf", "a.conf"), "a");
        File.WriteAllText(Path.Combine(src, "db", "b.yml"), "b");
        File.WriteAllText(Path.Combine(src, "npc", "scripts_custom.conf"), "npc: npc/custom/loki_smoke.txt\n");
        File.WriteAllText(Path.Combine(src, "sql-files", "main.sql"), "select 1;");
        return src;
    }

    [Fact]
    public void Only_a_copy_this_setup_made_is_resumed_and_nothing_in_it_is_overwritten()
    {
        var root = Temp();
        var src = FakeBuild(root);
        // a folder that merely holds an rAthena (as specified's own build) is never taken as ours
        Assert.False(RathenaCopy.IsOwnCopy(src));
        Assert.Throws<InvalidOperationException>(() => RathenaCopy.Create(src, src, null, resume: false));

        var dst = Path.Combine(root, "host");
        RathenaCopy.Create(src, dst);
        Assert.True(File.Exists(Path.Combine(dst, RathenaCopy.Marker)));
        Assert.True(RathenaCopy.IsOwnCopy(dst));
        RathenaCopy.RemoveTestSettings(dst);

        // the unfinished setup's copy: a file went missing, one was changed; resume adds the missing one only
        File.Move(Path.Combine(dst, "db", "b.yml"), Path.Combine(root, "b.yml.moved"));
        File.WriteAllText(Path.Combine(dst, "conf", "a.conf"), "changed by the host");
        Assert.Throws<InvalidOperationException>(() => RathenaCopy.Create(src, dst)); // not without resume
        RathenaCopy.Create(src, dst, null, resume: true);
        Assert.Equal("b", File.ReadAllText(Path.Combine(dst, "db", "b.yml")));
        Assert.Equal("changed by the host", File.ReadAllText(Path.Combine(dst, "conf", "a.conf")));
        Assert.Contains("(Loki Host: test NPC, off)", File.ReadAllText(Path.Combine(dst, "npc", "scripts_custom.conf")));

        // a copy made before the marker existed (the design's first setup, 2026-09-30) is told by what RemoveTestSettings wrote
        var old = Path.Combine(root, "old");
        RathenaCopy.Create(src, old);
        RathenaCopy.RemoveTestSettings(old);
        File.Move(Path.Combine(old, RathenaCopy.Marker), Path.Combine(root, "marker.moved"));
        Assert.True(RathenaCopy.IsOwnCopy(old));
    }

    /// <summary>
    /// The whole private path against a real rAthena build and the MariaDB programs installed on this PC: setup into a temporary
    /// folder (its own database made, started, filled, stopped), start (the database first), a login with an account the tool
    /// made accepted and a wrong password refused, a clean stop of the servers and the database. Runs when
    /// LOKIHOST_TEST_RATHENA_BUILD is set and MariaDB's programs are found; no admin password is needed.
    /// </summary>
    [Fact]
    public async Task Private_database_setup_start_login_stop()
    {
        var build = Environment.GetEnvironmentVariable("LOKIHOST_TEST_RATHENA_BUILD");
        var bin = PrivateDb.FindBin(Environment.GetEnvironmentVariable("LOKIHOST_TEST_MARIADB_BIN"));
        if (string.IsNullOrEmpty(build) || bin == null) return;
        var root = Temp();
        var s = new HostSettings
        {
            SourceDir = build, HostDir = Path.Combine(root, "rathena"), FilePath = Path.Combine(root, "lokihost.json"),
            PrivateDb = true, PrivateDbDir = Path.Combine(root, "mariadb"), PrivateDbPort = await PrivateDb.FreePortAsync(17307), MariaDbBin = bin,
            LoginPort = 17900, CharPort = 17901, MapPort = 17902, PresencePort = 17903, ServerName = "E2E",
        };
        s.DbHost = "127.0.0.1"; s.DbPort = s.PrivateDbPort;
        var log = new List<string>();
        void Log(string l) { lock (log) log.Add(l); _out.WriteLine(l); }

        HostSetup.Run(s, "", "", Log);
        Assert.True(s.SetUp);
        Assert.False(new PrivateDb(s).Answers()); // setup stops its database again

        var host = new HostController(s);
        var online = new TaskCompletionSource();
        bool zenyLogged = false;
        host.Output += (n, l) =>
        {
            if (l.Contains("Map Server is now online")) online.TrySetResult();
            if (l.Contains("Logging Zeny transactions")) zenyLogged = true; // log.cpp: the map-server read log_zeny 1 (design note, 2026-10-07)
        };
        try
        {
            Assert.True(await host.EnsureDatabaseAsync(Log));
            Database.AddAccount(Database.ConnStr(s), "friend01", "friendpw1", 'M', 0, Database.LoginRules.Read(s.HostDir));
            await host.StartAsync(Log, publicIpOverride: "203.0.113.7", waitSeconds: 300);
            Assert.Same(online.Task, await Task.WhenAny(online.Task, Task.Delay(TimeSpan.FromMinutes(5))));
            Assert.Equal(0x0ac4, await EndToEndTests.LoginReply("127.0.0.1", s.LoginPort, "friend01", "friendpw1"));
            Assert.Equal(0x083e, await EndToEndTests.LoginReply("127.0.0.1", s.LoginPort, "friend01", "wrongpass"));
            // design note, 2026-10-07: the zeny log is on, and the database was backed up after it answered and before the servers started
            Assert.True(zenyLogged, "the map-server did not say it logs zeny");
            Assert.Equal("1", Conf.ReadKey(Path.Combine(s.HostDir, "conf", "import", "log_conf.txt"), "log_zeny"));
            Assert.True(host.LastBackup is { Ok: true }, host.LastBackup?.Error);
            Assert.True(File.Exists(host.LastBackup!.Path));
            Assert.Equal(DbBackup.Folder(s), Path.GetDirectoryName(host.LastBackup.Path));
            lock (log)
            {
                int backup = log.FindIndex(l => l.StartsWith("Database backup: "));
                Assert.True(backup >= 0 && backup < log.FindIndex(l => l.StartsWith("Starting login-server")), "the backup was not made before the servers started");
            }
            _out.WriteLine(host.LastBackup.Summary);
        }
        finally { await host.StopAsync(Log); }
        Assert.False(host.AnyRunning);
        Assert.False(new PrivateDb(s).Answers());
        // nothing from the host folder survives (a first run of this test on 2026-09-30 left a char-server behind)
        var left = System.Diagnostics.Process.GetProcesses().Where(p => { try { return p.MainModule?.FileName?.StartsWith(root, StringComparison.OrdinalIgnoreCase) == true; } catch { return false; } }).Select(p => p.ProcessName).ToList();
        Assert.Empty(left);
        lock (log) Assert.DoesNotContain(log, l => l.Contains("by force"));
    }
}
