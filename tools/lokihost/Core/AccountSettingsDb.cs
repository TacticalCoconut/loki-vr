using MySqlConnector;

namespace LokiHost.Core;

/// <summary>
/// Client wave 18 (w18acct), the design's point 1 ("The reposition locations for every element needs to be stored on an account basis
/// ... even if I start Loki on a completely different computer" + "This includes every other setting"): where the relay keeps each
/// account's settings, and how it knows a connection really is that account. The relay (PoseRelay) talks to this interface only,
/// so a test or another host can keep them elsewhere (an in-memory store in the tests).
/// </summary>
public interface IAccountSettingsStore
{
    /// <summary>Is <paramref name="proof"/> the proof of a login of account <paramref name="aid"/> that is in force now?</summary>
    Task<bool> VerifyAsync(uint aid, string proof);
    /// <summary>The account's stored settings (null: none yet).</summary>
    Task<(long savedAt, string json)?> LoadAsync(uint aid);
    /// <summary>Stores them unless a NEWER save is stored (the newest save wins, the whole file): (stored, the save held now).</summary>
    Task<(bool stored, long savedAt)> SaveAsync(uint aid, long savedAt, string json);
}

/// <summary>
/// <see cref="IAccountSettingsStore"/> in the host's rAthena database (Loki Host's own: HostSettings.DbName; the copy's login-server
/// uses the same one, HostSetup.ApplyConf inter_conf.txt login_server_db):
///   - the settings: one row per account in `loki_account_settings` (made by <see cref="EnsureTableAsync"/> at every Start);
///   - the proof: rAthena's web token. With use_web_auth_token on (HostSetup.ApplyConf writes it into the copy's
///     conf/import/login_conf.txt), every login gets a new random token (rA:login/account.cpp 651-710: LEFT(SHA2(CONCAT(UUID(),
///     RAND()), 256), 16) into login.web_auth_token, web_auth_token_enabled = 1), the login server sends it to that client in
///     AC_ACCEPT_LOGIN (rA:login/loginclif.cpp:127), enables it while the account is online (login.cpp:105) and disables it
///     disable_webtoken_delay (10 s, conf/login_athena.conf:179) after the account went offline (login.cpp:127, account.cpp
///     935-957). The check is rAthena's own web server's (rA:web/auth.cpp:16-49 isAuthorized): a login row of that account with
///     that token, enabled. So a friend's client cannot read or write another account's settings: it never sees that token.
/// </summary>
public sealed class AccountSettingsDb : IAccountSettingsStore
{
    public const string Table = "loki_account_settings";
    readonly string _conn;

    public AccountSettingsDb(string connStr) { _conn = connStr; }
    public AccountSettingsDb(HostSettings s) : this(Database.ConnStr(s)) { }

    public const string CreateSql = "CREATE TABLE IF NOT EXISTS `" + Table + "` (" +
        "`account_id` INT UNSIGNED NOT NULL," +
        "`saved_at` BIGINT NOT NULL COMMENT 'ms since 1970 UTC: when the player changed them (the saving client''s clock)'," +
        "`data` MEDIUMTEXT NOT NULL COMMENT 'the client''s settings JSON without its per-PC part (Audio, Graphics): World/UserSettings.cs'," +
        "`updated_at` DATETIME NOT NULL," +
        "PRIMARY KEY (`account_id`)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4";

    public async Task EnsureTableAsync()
    {
        await using var c = new MySqlConnection(_conn);
        await c.OpenAsync();
        await using var cmd = new MySqlCommand(CreateSql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> VerifyAsync(uint aid, string proof)
    {
        if (aid == 0 || !Loki.Net.PresenceWire.ValidProof(proof)) return false;
        await using var c = new MySqlConnection(_conn);
        await c.OpenAsync();
        // rA:web/auth.cpp 36-38: WHERE account_id = ? AND web_auth_token = ? AND web_auth_token_enabled = '1'; the servers' own
        // account (sex 'S') never has a token (account.cpp 651: acc->sex != 'S'), and is excluded here too
        await using var q = new MySqlCommand("SELECT COUNT(*) FROM `login` WHERE `account_id` = @a AND `web_auth_token` = @t AND `web_auth_token_enabled` = '1' AND `sex` <> 'S'", c);
        q.Parameters.AddWithValue("@a", aid); q.Parameters.AddWithValue("@t", proof);
        return Convert.ToInt64(await q.ExecuteScalarAsync()) == 1;
    }

    public async Task<(long savedAt, string json)?> LoadAsync(uint aid)
    {
        await using var c = new MySqlConnection(_conn);
        await c.OpenAsync();
        await using var q = new MySqlCommand("SELECT `saved_at`, `data` FROM `" + Table + "` WHERE `account_id` = @a", c);
        q.Parameters.AddWithValue("@a", aid);
        await using var r = await q.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return (r.GetInt64(0), r.GetString(1));
    }

    public async Task<(bool stored, long savedAt)> SaveAsync(uint aid, long savedAt, string json)
    {
        await using var c = new MySqlConnection(_conn);
        await c.OpenAsync();
        await using (var cur = new MySqlCommand("SELECT `saved_at`, `data` FROM `" + Table + "` WHERE `account_id` = @a", c))
        {
            cur.Parameters.AddWithValue("@a", aid);
            await using var r = await cur.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                long oldAt = r.GetInt64(0);
                string oldData = r.GetString(1);
                if (oldAt <= savedAt)
                    json = MergeCodexProgress(oldData, json);
            }
        }
        // one statement, so two saves at once cannot both win: a new row, or the old one replaced only when this save is newer
        // (the parameters again rather than VALUES(): MariaDB keeps VALUES() there, MySQL 8.0.20 deprecated it; the assignments run left to
        // right, so `saved_at` is still the stored one in the first two)
        await using var ins = new MySqlCommand("INSERT INTO `" + Table + "` (`account_id`, `saved_at`, `data`, `updated_at`) VALUES (@a, @s, @d, NOW()) " +
            "ON DUPLICATE KEY UPDATE `data` = IF(@s >= `saved_at`, @d, `data`), `updated_at` = IF(@s >= `saved_at`, NOW(), `updated_at`), " +
            "`saved_at` = GREATEST(`saved_at`, @s)", c);
        ins.Parameters.AddWithValue("@a", aid); ins.Parameters.AddWithValue("@s", savedAt); ins.Parameters.AddWithValue("@d", json);
        await ins.ExecuteNonQueryAsync();
        await using var q = new MySqlCommand("SELECT `saved_at` FROM `" + Table + "` WHERE `account_id` = @a", c);
        q.Parameters.AddWithValue("@a", aid);
        long held = Convert.ToInt64(await q.ExecuteScalarAsync());
        return (held == savedAt, held);
    }

    /// <summary>Ensures that if the existing account settings JSON contains non-empty "codex" Bestiary progress and the incoming
    /// JSON omits "codex" or has an empty "codex", the existing Bestiary progress is preserved.</summary>
    public static string MergeCodexProgress(string? oldJson, string newJson)
    {
        if (string.IsNullOrEmpty(oldJson) || string.IsNullOrEmpty(newJson)) return newJson;
        if (!oldJson.Contains("\"codex\"", StringComparison.Ordinal)) return newJson;
        try
        {
            using var oldDoc = System.Text.Json.JsonDocument.Parse(oldJson);
            if (oldDoc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return newJson;
            if (!oldDoc.RootElement.TryGetProperty("codex", out var oldCodex)) return newJson;
            if (oldCodex.ValueKind != System.Text.Json.JsonValueKind.Object) return newJson;

            using var newDoc = System.Text.Json.JsonDocument.Parse(newJson);
            if (newDoc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return newJson;

            bool newHasCodex = newDoc.RootElement.TryGetProperty("codex", out var newCodex) &&
                               newCodex.ValueKind == System.Text.Json.JsonValueKind.Object;
            if (!newHasCodex)
            {
                var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(newJson);
                if (dict != null)
                {
                    dict["codex"] = oldCodex.Clone();
                    string merged = System.Text.Json.JsonSerializer.Serialize(dict);
                    if (System.Text.Encoding.UTF8.GetByteCount(merged) <= Loki.Net.PresenceWire.MaxSettingsBytes)
                        return merged;
                }
            }
        }
        catch { }
        return newJson;
    }
}

/// <summary>The store in memory (the relay's tests): the proofs that count are given by the caller.</summary>
public sealed class MemoryAccountSettings : IAccountSettingsStore
{
    readonly object _gate = new();
    readonly Dictionary<uint, (long savedAt, string json)> _rows = new();
    /// <summary>The proof that counts for each account (as a login would set it).</summary>
    public readonly System.Collections.Concurrent.ConcurrentDictionary<uint, string> Proofs = new();
    /// <summary>Tests: every call fails (a database that went away).</summary>
    public volatile bool Broken;
    public int Saves, Loads, Verifies;

    public Task<bool> VerifyAsync(uint aid, string proof)
    {
        Interlocked.Increment(ref Verifies);
        if (Broken) throw new InvalidOperationException("the store is broken (test)");
        return Task.FromResult(Proofs.TryGetValue(aid, out var p) && p == proof);
    }

    public Task<(long savedAt, string json)?> LoadAsync(uint aid)
    {
        Interlocked.Increment(ref Loads);
        if (Broken) throw new InvalidOperationException("the store is broken (test)");
        lock (_gate) return Task.FromResult(_rows.TryGetValue(aid, out var r) ? r : ((long, string)?)null);
    }

    public Task<(bool stored, long savedAt)> SaveAsync(uint aid, long savedAt, string json)
    {
        Interlocked.Increment(ref Saves);
        if (Broken) throw new InvalidOperationException("the store is broken (test)");
        lock (_gate)
        {
            if (_rows.TryGetValue(aid, out var r))
            {
                if (r.savedAt > savedAt) return Task.FromResult((false, r.savedAt));
                json = AccountSettingsDb.MergeCodexProgress(r.json, json);
            }
            _rows[aid] = (savedAt, json);
            return Task.FromResult((true, savedAt));
        }
    }
}
