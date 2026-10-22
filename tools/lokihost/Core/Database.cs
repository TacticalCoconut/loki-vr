using System.Security.Cryptography;
using System.Text;
using MySqlConnector;

namespace LokiHost.Core;

/// <summary>The host's MariaDB / MySQL database: created once with an admin login, then used with its own user.</summary>
public static class Database
{
    public static string ConnStr(string host, int port, string user, string pass, string? db = null)
    {
        var b = new MySqlConnectionStringBuilder
        {
            Server = host, Port = (uint)port, UserID = user, Password = pass, ConnectionTimeout = 5,
            AllowUserVariables = true,
        };
        if (db != null) b.Database = db;
        return b.ConnectionString;
    }

    public static string ConnStr(HostSettings s) => ConnStr(s.DbHost, s.DbPort, s.DbUser, s.DbPass, s.DbName);

    /// <summary>Can the server be reached with these credentials? (null = yes, else the error)</summary>
    public static string? Probe(string connStr)
    {
        try { using var c = new MySqlConnection(connStr); c.Open(); return null; }
        catch (Exception e) { return e.Message; }
    }

    /// <summary>Number of tables in a database (0 for a missing one).</summary>
    public static int TableCount(MySqlConnection c, string db)
    {
        using var cmd = new MySqlCommand("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = @d", c);
        cmd.Parameters.AddWithValue("@d", db);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// With an admin login: creates the database (refused if it already has tables: nothing is dropped) and a user
    /// that may use only it, from localhost and 127.0.0.1 (rAthena connects over TCP to 127.0.0.1; MariaDB may see
    /// either name).
    /// </summary>
    public static void CreateDatabaseAndUser(string adminConnStr, string db, string user, string pass)
    {
        if (!IsIdent(db) || !IsIdent(user)) throw new ArgumentException("database and user names: letters, digits and _ only");
        using var c = new MySqlConnection(adminConnStr);
        c.Open();
        if (TableCount(c, db) > 0) throw new InvalidOperationException($"database '{db}' already has tables; choose another name (nothing is dropped)");
        Exec(c, $"CREATE DATABASE IF NOT EXISTS `{db}`");
        foreach (var h in new[] { "localhost", "127.0.0.1" })
        {
            using (var cmd = new MySqlCommand($"CREATE USER IF NOT EXISTS '{user}'@'{h}' IDENTIFIED BY @p", c))
            { cmd.Parameters.AddWithValue("@p", pass); cmd.ExecuteNonQuery(); }
            using (var cmd = new MySqlCommand($"ALTER USER '{user}'@'{h}' IDENTIFIED BY @p", c))
            { cmd.Parameters.AddWithValue("@p", pass); cmd.ExecuteNonQuery(); }
            Exec(c, $"GRANT ALL PRIVILEGES ON `{db}`.* TO '{user}'@'{h}'");
        }
        Exec(c, "FLUSH PRIVILEGES");
    }

    /// <summary>Runs an rAthena SQL file (sql-files/*.sql: plain statements, no DELIMITER blocks).</summary>
    public static void ImportSql(string connStr, string file)
    {
        using var c = new MySqlConnection(connStr);
        c.Open();
        using var cmd = new MySqlCommand(File.ReadAllText(file), c) { CommandTimeout = 600 };
        cmd.ExecuteNonQuery();
    }

    /// <summary>login_athena.conf use_MD5_passwords: user_pass holds md5(password) as 32 hex digits.</summary>
    public static string StoredPassword(string pass, bool md5) =>
        md5 ? Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(pass))).ToLowerInvariant() : pass;

    /// <summary>The servers' own account (account_id 1, sex 'S'; main.sql inserts it as s1 / p1).</summary>
    public static void SetServerAccount(string connStr, string user, string pass, bool md5)
    {
        using var c = new MySqlConnection(connStr);
        c.Open();
        using var cmd = new MySqlCommand("UPDATE `login` SET `userid` = @u, `user_pass` = @p WHERE `account_id` = 1 AND `sex` = 'S'", c);
        cmd.Parameters.AddWithValue("@u", user);
        cmd.Parameters.AddWithValue("@p", StoredPassword(pass, md5));
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("the server account (login account 1, sex S) was not found");
    }

    public sealed record Account(int Id, string User, string Sex, int Group, int LoginCount, string LastLogin, int State);

    /// <summary>Player accounts (not the servers' own account, sex 'S').</summary>
    public static List<Account> Accounts(string connStr)
    {
        using var c = new MySqlConnection(connStr);
        c.Open();
        using var cmd = new MySqlCommand("SELECT `account_id`, `userid`, `sex`, `group_id`, `logincount`, `lastlogin`, `state` FROM `login` WHERE `sex` <> 'S' ORDER BY `account_id`", c);
        using var r = cmd.ExecuteReader();
        var list = new List<Account>();
        while (r.Read())
            list.Add(new Account(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4),
                r.IsDBNull(5) ? "" : r.GetValue(5).ToString() ?? "", r.GetInt32(6)));
        return list;
    }

    /// <summary>
    /// A new player account, as rAthena's own account creation stores it (login.cpp:250-256 login_mmo_auth_new:
    /// userid, user_pass, sex, email "a@a.com"), with group 0 or the given one, and the same length rules.
    /// </summary>
    public static void AddAccount(string connStr, string user, string pass, char sex, int group, LoginRules rules)
    {
        CheckCredentials(user, pass, rules);
        bool md5 = rules.Md5;
        if (sex != 'M' && sex != 'F') throw new ArgumentException("sex must be M or F");
        using var c = new MySqlConnection(connStr);
        c.Open();
        using (var q = new MySqlCommand("SELECT COUNT(*) FROM `login` WHERE `userid` = @u", c))
        {
            q.Parameters.AddWithValue("@u", user);
            if (Convert.ToInt32(q.ExecuteScalar()) > 0) throw new InvalidOperationException($"the account '{user}' already exists");
        }
        using var cmd = new MySqlCommand("INSERT INTO `login` (`userid`, `user_pass`, `sex`, `email`, `group_id`) VALUES (@u, @p, @s, 'a@a.com', @g)", c);
        cmd.Parameters.AddWithValue("@u", user);
        cmd.Parameters.AddWithValue("@p", StoredPassword(pass, md5));
        cmd.Parameters.AddWithValue("@s", sex.ToString());
        cmd.Parameters.AddWithValue("@g", group);
        cmd.ExecuteNonQuery();
    }

    public static void SetPassword(string connStr, int accountId, string pass, LoginRules rules)
    {
        CheckCredentials(new string('x', rules.NameMin), pass, rules);
        Update(connStr, "UPDATE `login` SET `user_pass` = @v WHERE `account_id` = @id AND `sex` <> 'S'", accountId, StoredPassword(pass, rules.Md5));
    }

    /// <summary>group_id: 0 player, 99 admin (conf/groups.yml).</summary>
    public static void SetGroup(string connStr, int accountId, int group) =>
        Update(connStr, "UPDATE `login` SET `group_id` = @v WHERE `account_id` = @id AND `sex` <> 'S'", accountId, group);

    static void Update(string connStr, string sql, int id, object v)
    {
        using var c = new MySqlConnection(connStr);
        c.Open();
        using var cmd = new MySqlCommand(sql, c);
        cmd.Parameters.AddWithValue("@v", v);
        cmd.Parameters.AddWithValue("@id", id);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException($"account {id} not found");
    }

    /// <summary>
    /// The host's login rules (conf/login_athena.conf, conf/import/login_conf.txt): use_MD5_passwords, and
    /// acc_name_min_length / password_min_length, which login.cpp:759-761 sets to 6 / 6 for PACKETVER >= 20181114.
    /// </summary>
    public sealed record LoginRules(bool Md5, int NameMin, int PassMin)
    {
        public static LoginRules Read(string hostDir)
        {
            string b = Path.Combine(hostDir, "conf", "login_athena.conf"), i = Path.Combine(hostDir, "conf", "import", "login_conf.txt");
            static int Num(string? v, int d) => int.TryParse(v, out var n) ? n : d;
            static bool Yes(string? v) => v != null && (v.Equals("yes", StringComparison.OrdinalIgnoreCase) || v == "1" || v.Equals("on", StringComparison.OrdinalIgnoreCase));
            return new LoginRules(Yes(Conf.ReadEffective(b, i, "use_MD5_passwords")),
                Num(Conf.ReadEffective(b, i, "acc_name_min_length"), 6), Num(Conf.ReadEffective(b, i, "password_min_length"), 6));
        }
    }

    /// <summary>
    /// The login table's limits (userid varchar(23), user_pass varchar(32)) and the host's minimums, plus what a friend can type
    /// and log in with from Loki (client wave 5, w5friends: the Accounts tab checked against the friends' guide):
    /// - the name: ASCII letters, digits, _ - . only. Loki's account field takes letters, digits, _ - . (client
    ///   UI/LoginPanel.cs BuildLogin, `_user.Allow`), so a space could never be typed; accented letters are left out as for the
    ///   password (below).
    /// - not ending in _M / _F (either case): while self-registration is on, rAthena reads such a login as "create the account
    ///   without the suffix" and refuses it (rA:login/login.cpp:324-338), so that account could not log in.
    /// - the password: printable ASCII without spaces. Loki hashes the password's Windows-1252 bytes for the login (client
    ///   Net/RoSession.cs SendHashedLogin, RoText.Encode) and rAthena hashes the stored text as its database connection returns it
    ///   (login.cpp login_check_password; inter_athena.conf login_codepage unset): only ASCII is the same bytes in every case, the
    ///   rest depends on the database's character set, which was not checked.
    /// </summary>
    public static void CheckCredentials(string user, string pass, LoginRules rules)
    {
        if (user.Length < rules.NameMin || user.Length > 23) throw new ArgumentException($"the account name must be {rules.NameMin} to 23 characters");
        if (!user.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-' or '.'))
            throw new ArgumentException("the account name may use only the letters A-Z, digits and _ - . (no spaces or accents: Loki's login screen cannot type them)");
        if (user.Length > 2 && user[^2] == '_' && "FfMm".Contains(user[^1]))
            throw new ArgumentException("an account name may not end in _M or _F: rAthena reads such a login as a new account when self-registration is on, and refuses it");
        if (pass.Length < rules.PassMin || pass.Length > 32) throw new ArgumentException($"the password must be {rules.PassMin} to 32 characters");
        if (!pass.All(c => c is > ' ' and <= '~'))
            throw new ArgumentException("the password may use only letters A-Z, digits and the symbols of an English keyboard (no spaces or accents: only those reach the server as the same bytes)");
    }

    static bool IsIdent(string s) => s.Length > 0 && s.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_');

    static void Exec(MySqlConnection c, string sql)
    {
        using var cmd = new MySqlCommand(sql, c);
        cmd.ExecuteNonQuery();
    }

    /// <summary>A random password of letters and digits (no characters that conf files or SQL would treat specially).</summary>
    public static string RandomSecret(int len = 20)
    {
        const string abc = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        return new string(Enumerable.Range(0, len).Select(_ => abc[RandomNumberGenerator.GetInt32(abc.Length)]).ToArray());
    }
}
