using LokiHost.Core;
using Xunit;

namespace LokiHost.Tests;

/// <summary>
/// Against a real MariaDB and rAthena's own sql-files. Runs only when LOKIHOST_TEST_ADMIN ("user:password" of an
/// account allowed to create databases and users on 127.0.0.1:3306) and LOKIHOST_TEST_RATHENA (an rAthena checkout)
/// are set; otherwise it passes without doing anything. Each run uses a new database; nothing is dropped.
/// </summary>
public class DatabaseTests
{
    [Fact]
    public void Setup_database_import_server_account_and_player_accounts()
    {
        var admin = Environment.GetEnvironmentVariable("LOKIHOST_TEST_ADMIN");
        var ra = Environment.GetEnvironmentVariable("LOKIHOST_TEST_RATHENA");
        if (string.IsNullOrEmpty(admin) || string.IsNullOrEmpty(ra)) return;
        var (au, ap) = (admin.Split(':')[0], admin[(admin.IndexOf(':') + 1)..]);
        var adminConn = Database.ConnStr("127.0.0.1", 3306, au, ap);
        Assert.Null(Database.Probe(adminConn));

        var s = new HostSettings { DbName = "lokihost_test_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), DbUser = "lokihost_t", DbPass = Database.RandomSecret() };
        Database.CreateDatabaseAndUser(adminConn, s.DbName, s.DbUser, s.DbPass);
        var conn = Database.ConnStr(s);
        Assert.Null(Database.Probe(conn));

        foreach (var f in new[] { "main.sql", "logs.sql", "web.sql", "roulette_default_data.sql" })
            Database.ImportSql(conn, Path.Combine(ra, "sql-files", f));
        using (var c = new MySqlConnector.MySqlConnection(conn))
        {
            c.Open();
            Assert.True(Database.TableCount(c, s.DbName) > 50);
            using var q = new MySqlConnector.MySqlCommand("SELECT COUNT(*) FROM db_roulette", c);
            Assert.Equal(42, Convert.ToInt32(q.ExecuteScalar()));
        }
        // a database that has tables is never reused (nothing is dropped)
        Assert.Throws<InvalidOperationException>(() => Database.CreateDatabaseAndUser(adminConn, s.DbName, s.DbUser, s.DbPass));

        Database.SetServerAccount(conn, "srvTest", "serverpw", md5: false);
        var rules = new Database.LoginRules(false, 6, 6);
        Database.AddAccount(conn, "friend1", "friendpw", 'M', 0, rules);
        Database.AddAccount(conn, "hostadm", "hostpw1", 'F', 99, rules);
        Assert.Throws<InvalidOperationException>(() => Database.AddAccount(conn, "friend1", "otherpw", 'M', 0, rules));
        var list = Database.Accounts(conn);
        Assert.Equal(new[] { "friend1", "hostadm" }, list.Select(a => a.User).ToArray()); // the server account (sex S) is not listed
        Assert.Equal(99, list[1].Group);

        Database.SetPassword(conn, list[0].Id, "newpass1", rules);
        Database.SetGroup(conn, list[0].Id, 99);
        using (var c = new MySqlConnector.MySqlConnection(conn))
        {
            c.Open();
            using var q = new MySqlConnector.MySqlCommand("SELECT userid, user_pass, sex FROM login WHERE account_id = 1", c);
            using var r = q.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal(("srvTest", "serverpw", "S"), (r.GetString(0), r.GetString(1), r.GetString(2)));
        }
        Assert.Equal(99, Database.Accounts(conn)[0].Group);
    }
}
