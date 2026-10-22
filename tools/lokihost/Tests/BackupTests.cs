using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using LokiHost.Core;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>
/// design note, 2026-10-07: "Turn on both, the zeny log and the startup backup". The zeny log's import line (HostSetup.ApplyConf) and the
/// database backup made at every Start (DbBackup): its name and folder, its content, no half files, nothing deleted or overwritten.
/// The backup of a real database runs when MariaDB's programs are found (LOKIHOST_TEST_MARIADB_BIN or PrivateDb.FindBin), on a
/// private MariaDB of its own in a temporary folder and a free port from 17320; no admin password is needed.
/// </summary>
public class BackupTests
{
    readonly ITestOutputHelper _out;
    public BackupTests(ITestOutputHelper o) { _out = o; }

    static string Temp()
    {
        var d = Path.Combine(Path.GetTempPath(), "lokihost-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>Every file of a folder with its bytes (to prove nothing old was changed, renamed or removed).</summary>
    static Dictionary<string, byte[]> Snapshot(string folder) =>
        Directory.Exists(folder) ? Directory.GetFiles(folder).ToDictionary(f => Path.GetFileName(f), File.ReadAllBytes) : new();

    static void AssertKept(Dictionary<string, byte[]> before, string folder)
    {
        var now = Snapshot(folder);
        foreach (var (name, bytes) in before)
        {
            Assert.True(now.ContainsKey(name), $"{name} is gone");
            Assert.Equal(bytes, now[name]);
        }
    }

    static string Gunzip(string file)
    {
        using var gz = new GZipStream(File.OpenRead(file), CompressionMode.Decompress);
        using var r = new StreamReader(gz, Encoding.UTF8);
        return r.ReadToEnd();
    }

    // ------------------------------------------------------------------------------------------------ the zeny log

    [Fact]
    public void ApplyConf_turns_the_zeny_log_on_in_log_conf_txt_and_keeps_the_builds_lines()
    {
        var d = Temp();
        Directory.CreateDirectory(Path.Combine(d, "conf", "import"));
        string lc = Path.Combine(d, "conf", "import", "log_conf.txt");
        // a build's import with its own log lines, an old log_zeny and a commented one
        File.WriteAllLines(lc, new[] { "// the build's own", "log_filter: 1", "// log_zeny: 5", "log_zeny: 0" });
        var s = new HostSettings { HostDir = d };
        Assert.True(s.LogZeny); // the design's choice: on
        HostSetup.ApplyConf(s, "203.0.113.7", "192.168.1.20", "255.255.255.0");
        Assert.Equal("1", Conf.ReadKey(lc, "log_zeny")); // rAthena: "1 - log any zeny changes"
        Assert.Single(File.ReadAllLines(lc), l => l.StartsWith("log_zeny:"));
        Assert.Equal("1", Conf.ReadKey(lc, "log_filter"));
        Assert.Contains("// log_zeny: 5", File.ReadAllLines(lc));
        // the same again at the next start: still one line
        HostSetup.ApplyConf(s, "203.0.113.7", "192.168.1.20", "255.255.255.0");
        Assert.Single(File.ReadAllLines(lc), l => l.StartsWith("log_zeny:"));
        // switched off on the Setup tab: 0 ("don't log")
        s.LogZeny = false;
        HostSetup.ApplyConf(s, "203.0.113.7", "192.168.1.20", "255.255.255.0");
        Assert.Equal("0", Conf.ReadKey(lc, "log_zeny"));

        // a copy whose build has no log_conf.txt: the file is made; rAthena's own conf/log_athena.conf is never written
        var e = Temp();
        Directory.CreateDirectory(Path.Combine(e, "conf", "import"));
        File.WriteAllText(Path.Combine(e, "conf", "log_athena.conf"), "log_zeny: 0\nimport: conf/import/log_conf.txt\n");
        HostSetup.ApplyConf(new HostSettings { HostDir = e }, "203.0.113.7", "", "");
        Assert.Equal("1", Conf.ReadKey(Path.Combine(e, "conf", "import", "log_conf.txt"), "log_zeny"));
        Assert.Equal("log_zeny: 0\nimport: conf/import/log_conf.txt\n", File.ReadAllText(Path.Combine(e, "conf", "log_athena.conf")));
        Assert.Equal("1", Conf.ReadEffective(Path.Combine(e, "conf", "log_athena.conf"), Path.Combine(e, "conf", "import", "log_conf.txt"), "log_zeny"));
    }

    [Fact]
    public void A_settings_file_from_before_both_switches_gets_them_on()
    {
        string f = Path.Combine(Temp(), "lokihost.json");
        File.WriteAllText(f, "{ \"HostDir\": \"C:\\\\LokiHost\\\\rathena\", \"ViewRange\": 60, \"SettingsVersion\": 2, \"SetUp\": true }");
        var s = HostSettings.Load(f);
        Assert.Null(s.LoadError);
        Assert.True(s.LogZeny);
        Assert.True(s.BackupOnStart);
        s.LogZeny = false; s.BackupOnStart = false; s.Save();
        var again = HostSettings.Load(f);
        Assert.False(again.LogZeny);
        Assert.False(again.BackupOnStart);
    }

    // ------------------------------------------------------------------------------------------------ the backup: names and files

    [Fact]
    public void The_backup_is_named_by_database_and_time_in_backups_beside_the_settings()
    {
        Assert.Equal("loki_host-2026-10-07_23-05-09.sql.gz", DbBackup.FileName("loki_host", new DateTime(2026, 10, 7, 23, 5, 9)));
        var root = Temp();
        Assert.Equal(Path.Combine(root, "backups"), DbBackup.Folder(new HostSettings { FilePath = Path.Combine(root, "lokihost.json") }));
        // Loki Host's own settings: %APPDATA%\LokiHost\backups
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LokiHost", "backups"), DbBackup.Folder(new HostSettings()));
    }

    [Fact]
    public void The_dump_tools_command_line_never_holds_the_password()
    {
        var s = new HostSettings { DbHost = "127.0.0.1", DbPort = 3307, DbUser = "lokihost", DbPass = "Sekret9xQ", DbName = "loki_host" };
        var a = DbBackup.Arguments(s);
        Assert.Equal("--no-defaults", a[0]); // an option file on the PC must not change the login (must be first)
        Assert.Contains("--single-transaction", a);
        Assert.Contains("--port=3307", a);
        Assert.Contains("--user=lokihost", a);
        Assert.Equal("loki_host", a[^1]);
        Assert.DoesNotContain(a, x => x.Contains("Sekret9xQ") || x.StartsWith("--password") || x.StartsWith("-p"));
    }

    [Fact]
    public async Task A_backup_never_leaves_a_half_file_that_looks_whole_and_never_overwrites()
    {
        var folder = Temp();
        var name = DbBackup.FileName("loki_host", new DateTime(2026, 10, 7, 23, 0, 0));
        // an older backup and one of the same second: kept byte for byte
        File.WriteAllText(Path.Combine(folder, "loki_host-2026-10-01_10-00-00.sql.gz"), "older backup");
        File.WriteAllText(Path.Combine(folder, name), "same second");
        var before = Snapshot(folder);

        // the same second: the next free name, never the existing file
        var p1 = DbBackup.FreePath(folder, name);
        Assert.Equal(Path.Combine(folder, "loki_host-2026-10-07_23-00-00_2.sql.gz"), p1);

        // a dump that stops half way (the tool failed): nothing is called .sql.gz, the half is kept as .incomplete
        await Assert.ThrowsAsync<IOException>(() => DbBackup.WriteGzipAsync(p1, async gz =>
        {
            await gz.WriteAsync(Encoding.UTF8.GetBytes("-- MariaDB dump\nCREATE TABLE `login` ("));
            return false;
        }));
        Assert.False(File.Exists(p1));
        Assert.True(File.Exists(p1 + DbBackup.IncompleteExtension));
        // the same when the dump throws (the tool cannot start, the disk is full …)
        var p2 = DbBackup.FreePath(folder, name);
        Assert.Equal(Path.Combine(folder, "loki_host-2026-10-07_23-00-00_3.sql.gz"), p2); // an .incomplete name is not reused either
        await Assert.ThrowsAsync<InvalidOperationException>(() => DbBackup.WriteGzipAsync(p2, _ => throw new InvalidOperationException("no tool")));
        Assert.False(File.Exists(p2));

        // a finished one: renamed, readable, no .incomplete twin
        var p3 = DbBackup.FreePath(folder, name);
        long size = await DbBackup.WriteGzipAsync(p3, async gz =>
        {
            await gz.WriteAsync(Encoding.UTF8.GetBytes("-- MariaDB dump\nSELECT 1;\n-- Dump completed on 2026-10-07 23:00:00\n"));
            return true;
        });
        Assert.Equal(new FileInfo(p3).Length, size);
        Assert.False(File.Exists(p3 + DbBackup.IncompleteExtension));
        Assert.StartsWith("-- MariaDB dump", Gunzip(p3));

        // a path that exists is never written over, whatever asks
        await Assert.ThrowsAsync<IOException>(() => DbBackup.WriteGzipAsync(Path.Combine(folder, name), _ => Task.FromResult(true)));
        AssertKept(before, folder);
        Assert.Equal("same second", File.ReadAllText(Path.Combine(folder, name)));
        // the newest finished backup (not an .incomplete one)
        Assert.Equal(p3, DbBackup.Latest(folder)?.Path);
    }

    [Fact]
    public async Task Without_the_dump_tool_the_backup_fails_plainly_and_writes_nothing()
    {
        var root = Temp();
        var s = new HostSettings { FilePath = Path.Combine(root, "lokihost.json"), MariaDbBin = Path.Combine(root, "no-such-bin"), DbPass = "Sekret9xQ" };
        var r = await DbBackup.RunAsync(s, binDir: Path.Combine(root, "no-such-bin"));
        Assert.False(r.Ok);
        Assert.Contains("dump tool", r.Error);
        Assert.StartsWith("Database backup FAILED", r.Summary);
        Assert.Contains("The servers start anyway", r.Summary);
        Assert.DoesNotContain("Sekret9xQ", r.Summary);
        Assert.False(Directory.Exists(DbBackup.Folder(s)) && Directory.EnumerateFiles(DbBackup.Folder(s), "*.sql.gz").Any());
    }

    // ------------------------------------------------------------------------------------------------ the backup of a real database

    [Fact]
    public async Task A_real_database_is_dumped_compressed_complete_restorable_and_old_backups_stay()
    {
        var bin = PrivateDb.FindBin(Environment.GetEnvironmentVariable("LOKIHOST_TEST_MARIADB_BIN"));
        if (bin == null || DbBackup.DumpExe(bin) == null) { _out.WriteLine("MariaDB's programs not found: skipped"); return; }
        var root = Temp();
        var s = new HostSettings
        {
            HostDir = Path.Combine(root, "rathena"), FilePath = Path.Combine(root, "lokihost.json"), PrivateDb = true,
            PrivateDbDir = Path.Combine(root, "mariadb"), PrivateDbPort = await PrivateDb.FreePortAsync(17320), MariaDbBin = bin,
            DbName = "loki_host", DbUser = "lokihost", DbPass = Database.RandomSecret(),
        };
        s.DbHost = "127.0.0.1"; s.DbPort = s.PrivateDbPort;
        void Log(string l) => _out.WriteLine(l);
        var db = new PrivateDb(s);
        db.Init(Log);
        await db.StartAsync(Log);
        try
        {
            Database.CreateDatabaseAndUser(db.RootConnStr(), s.DbName, s.DbUser, s.DbPass);
            // rAthena's own tables when an rAthena folder is given (LOKIHOST_TEST_RATHENA: its sql-files), else one table
            var ra = Environment.GetEnvironmentVariable("LOKIHOST_TEST_RATHENA");
            bool rathena = !string.IsNullOrEmpty(ra) && File.Exists(Path.Combine(ra, "sql-files", "main.sql"));
            var conn = Database.ConnStr(s);
            if (rathena)
                foreach (var f in new[] { "main.sql", "logs.sql" }) Database.ImportSql(conn, Path.Combine(ra!, "sql-files", f));
            using (var c = new MySqlConnector.MySqlConnection(conn))
            {
                c.Open();
                if (!rathena) new MySqlConnector.MySqlCommand("CREATE TABLE `login` (account_id INT PRIMARY KEY, userid VARCHAR(23)) ENGINE=InnoDB", c).ExecuteNonQuery();
                new MySqlConnector.MySqlCommand("CREATE TABLE w14_marker (id INT PRIMARY KEY, note VARCHAR(40)) ENGINE=InnoDB", c).ExecuteNonQuery();
                new MySqlConnector.MySqlCommand("INSERT INTO w14_marker VALUES (1, 'w14host backup marker')", c).ExecuteNonQuery();
            }

            var folder = DbBackup.Folder(s);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "loki_host-2026-10-01_10-00-00.sql.gz"), "an older backup");
            var before = Snapshot(folder);

            var at = new DateTime(2026, 10, 7, 23, 30, 0);
            var r1 = await DbBackup.RunAsync(s, now: at);
            _out.WriteLine(r1.Summary);
            Assert.True(r1.Ok, r1.Error);
            Assert.Equal(Path.Combine(folder, "loki_host-2026-10-07_23-30-00.sql.gz"), r1.Path);
            Assert.False(File.Exists(r1.Path + DbBackup.IncompleteExtension));
            var sql = Gunzip(r1.Path!);
            _out.WriteLine($"backup: {r1.Bytes} bytes compressed, {Encoding.UTF8.GetByteCount(sql)} bytes of SQL{(rathena ? " (rAthena's main.sql + logs.sql, empty tables)" : "")}");
            // the dump's head: MariaDB 11.x+ puts its "sandbox mode" line first (/*M!999999\- enable the sandbox mode */), then the title
            var head = sql[..Math.Min(200, sql.Length)];
            Assert.True(head.Contains("-- MariaDB dump") || head.Contains("-- MySQL dump"), head);
            Assert.Contains("CREATE TABLE `login`", sql);
            Assert.Contains("'w14host backup marker'", sql);
            if (rathena) Assert.Contains("CREATE TABLE `zenylog`", sql);
            Assert.Contains(DbBackup.CompletedMarker, sql[^200..]);
            Assert.DoesNotContain(s.DbPass, sql);

            // a second backup in the same second: a new file beside it, the first untouched
            var firstBytes = File.ReadAllBytes(r1.Path!);
            var r2 = await DbBackup.RunAsync(s, now: at);
            Assert.True(r2.Ok, r2.Error);
            Assert.Equal(Path.Combine(folder, "loki_host-2026-10-07_23-30-00_2.sql.gz"), r2.Path);
            Assert.Equal(firstBytes, File.ReadAllBytes(r1.Path!));

            // a refused login: plain failure, no file that looks whole, nothing old touched, the password in no message
            var bad = new HostSettings { FilePath = s.FilePath, MariaDbBin = bin, DbHost = s.DbHost, DbPort = s.DbPort, DbUser = s.DbUser, DbName = s.DbName, DbPass = "WrongPass1" };
            var r3 = await DbBackup.RunAsync(bad, now: at.AddMinutes(1));
            _out.WriteLine(r3.Summary);
            Assert.False(r3.Ok);
            Assert.Contains("Access denied", r3.Error);
            Assert.DoesNotContain("WrongPass1", r3.Summary);
            Assert.DoesNotContain("ssl-verify-server-cert", r3.Error); // (a client warning, not the reason)
            Assert.False(File.Exists(Path.Combine(folder, "loki_host-2026-10-07_23-31-00.sql.gz")));
            AssertKept(before, folder);
            Assert.True(File.Exists(r1.Path) && File.Exists(r2.Path));
            Assert.Equal(r2.Path, DbBackup.Latest(folder)?.Path);

            // the README's restore by hand (mariadb <db> < file) into another database gives the same rows
            using (var c = new MySqlConnector.MySqlConnection(db.RootConnStr()))
            {
                c.Open();
                new MySqlConnector.MySqlCommand("CREATE DATABASE w14_restored", c).ExecuteNonQuery();
            }
            var client = new[] { "mariadb.exe", "mysql.exe", "mariadb", "mysql" }.Select(n => Path.Combine(bin, n)).First(File.Exists);
            var psi = new ProcessStartInfo(client) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var a in new[] { "--no-defaults", "--protocol=TCP", "--host=127.0.0.1", $"--port={s.PrivateDbPort}", "--user=root", "w14_restored" }) psi.ArgumentList.Add(a);
            psi.Environment["MYSQL_PWD"] = s.PrivateDbRootPass;
            using (var p = Process.Start(psi)!)
            {
                var err = p.StandardError.ReadToEndAsync();
                await p.StandardInput.WriteAsync(sql);
                p.StandardInput.Close();
                await p.WaitForExitAsync();
                Assert.True(p.ExitCode == 0, await err);
            }
            using (var c = new MySqlConnector.MySqlConnection(db.RootConnStr("w14_restored")))
            {
                c.Open();
                Assert.Equal("w14host backup marker", new MySqlConnector.MySqlCommand("SELECT note FROM w14_marker WHERE id = 1", c).ExecuteScalar());
                Assert.Equal(Database.TableCount(c, s.DbName), Database.TableCount(c, "w14_restored"));
            }
        }
        finally { await db.StopAsync(Log); }
    }
}
