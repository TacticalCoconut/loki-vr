using System.Net;
using System.Net.Sockets;
using System.Text;
using Loki.Net;
using Loki.World;
using LokiHost.Core;
using MySqlConnector;
using Xunit;

namespace LokiHost.Tests;

/// <summary>
/// Client wave 18 (w18acct), the design's point 1: settings per account. The wire (client/Assets/Loki/Runtime/Net/PresenceWire.cs, the
/// file the client compiles), the relay with a store in memory and raw TCP clients, the client's own rules of which save wins
/// (client/Assets/Loki/Runtime/World/AccountSyncRules.cs, compiled here), and the database store against a real rAthena
/// database when LOKIHOST_TEST_SETTINGS_DB holds a connection string to one (otherwise that test passes without doing anything).
/// </summary>
public class AccountSettingsTests
{
    static byte[] Payload(byte[] frame)
    {
        Assert.Equal(frame.Length - 2, frame[0] | frame[1] << 8);
        return frame[2..];
    }

    const string Json = "{\"saved\":true,\"snapTurn\":true,\"overheadPlaced\":true,\"overheadX\":0.25}";

    // ------------------------------------------------------------------------------------------------ the wire

    [Fact]
    public void Settings_frames_round_trip()
    {
        var a = Payload(PresenceWire.EncodeSettingsAuth("0123456789abcdef"));
        Assert.True(PresenceWire.TryDecodeSettingsAuth(a, 0, a.Length, out var proof));
        Assert.Equal("0123456789abcdef", proof);

        var p = Payload(PresenceWire.EncodeSettingsPut(1760000000123, Json));
        Assert.True(PresenceWire.TryDecodeSettingsPut(p, 0, p.Length, out var at, out var json));
        Assert.Equal((1760000000123L, Json), (at, json));

        var of = Payload(PresenceWire.EncodeSettingsOf(PresenceWire.SettingsFound, 1760000000123, Json));
        Assert.True(PresenceWire.TryDecodeSettingsOf(of, 0, of.Length, out var st, out at, out json));
        Assert.Equal((PresenceWire.SettingsFound, 1760000000123L, Json), (st, at, json));
        foreach (var s in new[] { PresenceWire.SettingsNone, PresenceWire.SettingsRefused, PresenceWire.SettingsFailed })
        {
            var none = Payload(PresenceWire.EncodeSettingsOf(s, 99, "ignored"));
            Assert.True(PresenceWire.TryDecodeSettingsOf(none, 0, none.Length, out st, out at, out json));
            Assert.Equal((s, 0L, (string?)null), (st, at, json)); // no time, no data unless found
        }

        var ack = Payload(PresenceWire.EncodeSettingsAck(PresenceWire.SettingsOlder, 1760000000999));
        Assert.True(PresenceWire.TryDecodeSettingsAck(ack, 0, ack.Length, out st, out at));
        Assert.Equal((PresenceWire.SettingsOlder, 1760000000999L), (st, at));

        // the longest settings fit one frame; one byte more cannot be sent
        string big = "{\"x\":\"" + new string('é', (PresenceWire.MaxSettingsBytes - 10) / 2) + "\"}";
        Assert.True(Encoding.UTF8.GetByteCount(big) <= PresenceWire.MaxSettingsBytes);
        var bigPut = PresenceWire.EncodeSettingsPut(1, big);
        Assert.NotNull(bigPut);
        Assert.True(bigPut!.Length - 2 <= PresenceWire.MaxPayloadSettings);
        var bigOf = PresenceWire.EncodeSettingsOf(PresenceWire.SettingsFound, 1, big);
        Assert.NotNull(bigOf);
        Assert.True(PresenceWire.TryDecodeSettingsOf(Payload(bigOf!), 0, bigOf!.Length - 2, out _, out _, out json) && json == big);
        Assert.Null(PresenceWire.EncodeSettingsPut(1, "{\"x\":\"" + new string('a', PresenceWire.MaxSettingsBytes) + "\"}"));
    }

    [Fact]
    public void Malformed_settings_frames_are_refused()
    {
        Assert.Null(PresenceWire.EncodeSettingsAuth(""));
        Assert.Null(PresenceWire.EncodeSettingsAuth("has space"));
        Assert.Null(PresenceWire.EncodeSettingsAuth(new string('a', 33)));
        Assert.Null(PresenceWire.EncodeSettingsPut(0, Json));          // no time
        Assert.Null(PresenceWire.EncodeSettingsPut(5, "[1,2]"));       // not an object
        Assert.Null(PresenceWire.EncodeSettingsPut(5, ""));
        Assert.Null(PresenceWire.EncodeSettingsOf(PresenceWire.SettingsFound, 0, Json));
        Assert.Null(PresenceWire.EncodeSettingsOf(9, 0, null));
        var a = Payload(PresenceWire.EncodeSettingsAuth("abc"));
        Assert.False(PresenceWire.TryDecodeSettingsAuth(a, 0, a.Length - 1, out _)); // truncated
        var bad = (byte[])a.Clone(); bad[2] = 0x20;
        Assert.False(PresenceWire.TryDecodeSettingsAuth(bad, 0, bad.Length, out _));
        var p = Payload(PresenceWire.EncodeSettingsPut(5, Json));
        var badUtf8 = (byte[])p.Clone(); badUtf8[^2] = 0xff;
        Assert.False(PresenceWire.TryDecodeSettingsPut(badUtf8, 0, badUtf8.Length, out _, out _));
        var negative = (byte[])p.Clone(); negative[8] = 0x80; // the time's top byte: before 1970
        Assert.False(PresenceWire.TryDecodeSettingsPut(negative, 0, negative.Length, out _, out _));
        var ack = Payload(PresenceWire.EncodeSettingsAck(PresenceWire.SettingsStored, 7));
        Assert.False(PresenceWire.TryDecodeSettingsAck(ack.Concat(new byte[] { 0 }).ToArray(), 0, ack.Length + 1, out _, out _));
    }

    // ------------------------------------------------------------------------------------------------ the client's rules

    [Fact]
    public void The_newest_save_wins_the_whole_file()
    {
        // the host has nothing for this account: this PC's settings become the account's (a test PC, the first login after wave 18)
        Assert.Equal(AccountSyncRules.Action.Push, AccountSyncRules.Decide(true, 100, false, PresenceWire.SettingsNone, 0));
        Assert.Equal(AccountSyncRules.Action.Push, AccountSyncRules.Decide(false, 0, false, PresenceWire.SettingsNone, 0));
        // a new PC (its file mirrors no account, or another one): the account's settings come to it, whatever their times
        Assert.Equal(AccountSyncRules.Action.Take, AccountSyncRules.Decide(false, 9999, false, PresenceWire.SettingsFound, 5));
        // the same account: the newer save wins
        Assert.Equal(AccountSyncRules.Action.Take, AccountSyncRules.Decide(true, 100, true, PresenceWire.SettingsFound, 200));
        Assert.Equal(AccountSyncRules.Action.Push, AccountSyncRules.Decide(true, 300, false, PresenceWire.SettingsFound, 200));
        Assert.Equal(AccountSyncRules.Action.Push, AccountSyncRules.Decide(true, 300, true, PresenceWire.SettingsFound, 200)); // the host lost it
        Assert.Equal(AccountSyncRules.Action.InStep, AccountSyncRules.Decide(true, 200, false, PresenceWire.SettingsFound, 200));
        // no proof accepted, or the host's store failed: this PC keeps its own, nothing is sent
        Assert.Equal(AccountSyncRules.Action.Nothing, AccountSyncRules.Decide(true, 300, false, PresenceWire.SettingsRefused, 0));
        Assert.Equal(AccountSyncRules.Action.Nothing, AccountSyncRules.Decide(false, 300, false, PresenceWire.SettingsFailed, 0));
    }

    [Fact]
    public void The_hash_tells_settings_apart()
    {
        Assert.Equal(AccountSyncRules.Hash(Json), AccountSyncRules.Hash(string.Concat(Json.AsSpan(0, 10), Json.AsSpan(10))));
        Assert.NotEqual(AccountSyncRules.Hash(Json), AccountSyncRules.Hash(Json.Replace("0.25", "0.26")));
        Assert.Equal(16, AccountSyncRules.Hash("").Length);
    }

    // ------------------------------------------------------------------------------------------------ the relay

    sealed class Client : IDisposable
    {
        public readonly TcpClient Tcp = new() { NoDelay = true };
        readonly PresenceWire.FrameReader _fr = new(PresenceWire.MaxPayloadSettings);
        readonly byte[] _buf = new byte[8192];
        public byte Caps;
        public Client(int port) { Tcp.Connect(IPAddress.Loopback, port); }
        public void Send(byte[] frame) => Tcp.GetStream().Write(frame);
        public Client Hello(uint aid, string name)
        {
            Send(PresenceWire.EncodeHello(aid, name, PresenceWire.VersionVoice));
            var w = Next(2000) ?? throw new Exception("no WELCOME");
            Assert.True(PresenceWire.TryDecodeWelcome(w, 0, w.Length, out var ver, out Caps), "not a WELCOME");
            Assert.Equal(PresenceWire.VersionVoice, ver);
            return this;
        }
        public byte[]? Next(int ms)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(ms);
            while (true)
            {
                int k = _fr.Next(out var p);
                if (k == 1) return p;
                if (k < 0) throw new InvalidDataException("a frame longer than this client accepts");
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) return null;
                Tcp.Client.ReceiveTimeout = Math.Max(1, (int)left.TotalMilliseconds);
                int n;
                try { n = Tcp.GetStream().Read(_buf, 0, _buf.Length); }
                catch (IOException e) when (e.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut }) { return null; }
                if (n <= 0) throw new EndOfStreamException("closed by the relay");
                _fr.Append(_buf, 0, n);
            }
        }
        public bool ClosedWithin(int ms)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(ms);
            try { while (DateTime.UtcNow < deadline) Next((int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds)); }
            catch (Exception e) when (e is EndOfStreamException or IOException) { return true; }
            return false;
        }
        public (byte state, long at, string? json) Auth(string proof)
        {
            Send(PresenceWire.EncodeSettingsAuth(proof)!);
            var p = Next(3000) ?? throw new Exception("no SETTINGS_OF");
            Assert.True(PresenceWire.TryDecodeSettingsOf(p, 0, p.Length, out var st, out var at, out var json), $"not a SETTINGS_OF: 0x{p[0]:x2}");
            return (st, at, json);
        }
        public (byte state, long at) Put(long savedAt, string json)
        {
            Send(PresenceWire.EncodeSettingsPut(savedAt, json)!);
            var p = Next(3000) ?? throw new Exception("no SETTINGS_ACK");
            Assert.True(PresenceWire.TryDecodeSettingsAck(p, 0, p.Length, out var st, out var at), $"not a SETTINGS_ACK: 0x{p[0]:x2}");
            return (st, at);
        }
        public void Dispose() => Tcp.Dispose();
    }

    static PoseRelay Relay(IAccountSettingsStore? store, List<string>? log = null)
    {
        var r = new PoseRelay(0) { AccountSettings = store };
        if (log != null) r.Log += l => { lock (log) log.Add(l); };
        r.Start(IPAddress.Loopback);
        return r;
    }

    [Fact]
    public async Task A_relay_without_a_store_does_not_offer_settings_and_closes_on_them_as_before()
    {
        await using var r = Relay(null);
        using var c = new Client(r.Port).Hello(2000001, "Tester");
        Assert.Equal(0, c.Caps & PresenceWire.CapSettings);
        c.Send(PresenceWire.EncodeSettingsAuth("0123456789abcdef")!);
        Assert.True(c.ClosedWithin(2000)); // an unknown message, as any older relay treats it
    }

    [Fact]
    public async Task Only_the_logins_proof_reads_and_writes_that_accounts_settings_and_the_newest_save_wins()
    {
        var store = new MemoryAccountSettings();
        store.Proofs[2000001] = "aaaaaaaaaaaaaaaa";
        store.Proofs[2000002] = "bbbbbbbbbbbbbbbb";
        var log = new List<string>();
        await using var r = Relay(store, log);
        using var a = new Client(r.Port).Hello(2000001, "PlayerOne");
        Assert.Equal(PresenceWire.CapSettings, a.Caps & PresenceWire.CapSettings);

        // before any accepted proof: a save is refused, and a frame longer than a voice frame closes the connection
        Assert.Equal(PresenceWire.SettingsRefused, a.Put(10, Json).state);
        Assert.Equal(PresenceWire.SettingsRefused, a.Auth("bbbbbbbbbbbbbbbb").state); // another account's proof
        Assert.Equal(PresenceWire.SettingsRefused, a.Put(10, Json).state);
        Assert.Equal(0, store.Saves);

        // the login's own proof: nothing stored yet; a save is stored; a second PC (a new connection, a new login) gets it back
        Assert.Equal((PresenceWire.SettingsNone, 0L, (string?)null), a.Auth("aaaaaaaaaaaaaaaa"));
        Assert.Equal((PresenceWire.SettingsStored, 1000L), a.Put(1000, Json));
        string big = "{\"overheadX\":0.5,\"pad\":\"" + new string('x', 20000) + "\"}"; // longer than any voice frame: taken now
        Assert.Equal((PresenceWire.SettingsStored, 2000L), a.Put(2000, big));
        a.Dispose();
        store.Proofs[2000001] = "cccccccccccccccc"; // the next login's token (the old one no longer counts)
        using var a2 = new Client(r.Port).Hello(2000001, "PlayerOne");
        Assert.Equal(PresenceWire.SettingsRefused, a2.Auth("aaaaaaaaaaaaaaaa").state);
        Assert.Equal((PresenceWire.SettingsFound, 2000L, big), a2.Auth("cccccccccccccccc"));
        // an older save (a PC that changed something offline before the newer one) is not stored; the host says what it holds
        Assert.Equal((PresenceWire.SettingsOlder, 2000L), a2.Put(1500, Json));
        Assert.Equal((PresenceWire.SettingsFound, 2000L, big), a2.Auth("cccccccccccccccc"));

        // another account never sees them
        using var b = new Client(r.Port).Hello(2000002, "Friend");
        Assert.Equal((PresenceWire.SettingsNone, 0L, (string?)null), b.Auth("bbbbbbbbbbbbbbbb"));
        Assert.Equal((PresenceWire.SettingsStored, 3000L), b.Put(3000, "{\"theirs\":1}"));
        Assert.Equal((PresenceWire.SettingsFound, 2000L, big), a2.Auth("cccccccccccccccc"));
        Assert.Equal((3L, 3L, 2L), (r.SettingsLoads, r.SettingsSaves, r.SettingsRefused)); // (counted before each answer is queued)
        lock (log)
        {
            Assert.Contains(log, l => l.Contains("PlayerOne (AID 2000001): settings REFUSED"));
            Assert.Contains(log, l => l.Contains("PlayerOne (AID 2000001): settings stored"));
            Assert.Contains(log, l => l.Contains("PlayerOne (AID 2000001): settings NOT stored: the host holds a newer save"));
        }
    }

    [Fact]
    public async Task A_long_save_before_an_accepted_proof_closes_the_connection()
    {
        var store = new MemoryAccountSettings();
        store.Proofs[7] = "abcdefabcdefabcd";
        await using var r = Relay(store);
        using var c = new Client(r.Port).Hello(7, "Seven");
        c.Send(PresenceWire.EncodeSettingsPut(5, "{\"pad\":\"" + new string('x', 400) + "\"}")!);
        Assert.True(c.ClosedWithin(2000));
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task A_failing_store_is_said_and_the_connection_goes_on()
    {
        var store = new MemoryAccountSettings();
        store.Proofs[8] = "abcdefabcdefabcd";
        await using var r = Relay(store);
        using var c = new Client(r.Port).Hello(8, "Eight");
        Assert.Equal(PresenceWire.SettingsNone, c.Auth("abcdefabcdefabcd").state);
        store.Broken = true;
        Assert.Equal(PresenceWire.SettingsFailed, c.Put(5, Json).state);
        Assert.Equal(PresenceWire.SettingsFailed, c.Auth("abcdefabcdefabcd").state);
        c.Send(PresenceWire.EncodePing());
        Assert.False(c.ClosedWithin(300));
    }

    [Fact]
    public async Task Proofs_and_saves_above_their_limits_are_dropped()
    {
        var store = new MemoryAccountSettings();
        store.Proofs[9] = "abcdefabcdefabcd";
        await using var r = new PoseRelay(0, new PoseRelay.Limits { MaxSettingsAuthPerConnection = 2, MaxSettingsPutsPerMinute = 3 }) { AccountSettings = store };
        r.Start(IPAddress.Loopback);
        using var c = new Client(r.Port).Hello(9, "Nine");
        c.Auth("abcdefabcdefabcd"); c.Auth("abcdefabcdefabcd");
        c.Send(PresenceWire.EncodeSettingsAuth("abcdefabcdefabcd")!);
        Assert.Null(c.Next(400));
        for (int i = 1; i <= 3; i++) Assert.Equal(PresenceWire.SettingsStored, c.Put(i, Json).state);
        c.Send(PresenceWire.EncodeSettingsPut(4, Json)!);
        Assert.Null(c.Next(400));
        Assert.Equal(3, store.Saves);
    }

    // ------------------------------------------------------------------------------------------------ the database store

    /// <summary>Against an rAthena database (its login table): LOKIHOST_TEST_SETTINGS_DB = a connection string to one (a private test
    /// copy: this test adds one account row and its settings there, and changes nothing else).</summary>
    [Fact]
    public async Task The_database_store_checks_rathenas_web_token_and_keeps_the_newest_save()
    {
        var conn = Environment.GetEnvironmentVariable("LOKIHOST_TEST_SETTINGS_DB");
        if (string.IsNullOrEmpty(conn)) return;
        var db = new AccountSettingsDb(conn);
        await db.EnsureTableAsync();
        await db.EnsureTableAsync(); // made once, never touched again
        string user = "w18t" + DateTime.UtcNow.ToString("MMddHHmmssfff");
        string token = Convert.ToHexString(Guid.NewGuid().ToByteArray())[..16].ToLowerInvariant();
        uint aid;
        await using (var c = new MySqlConnection(conn))
        {
            await c.OpenAsync();
            await using var ins = new MySqlCommand("INSERT INTO `login` (`userid`, `user_pass`, `sex`, `email`, `web_auth_token`, `web_auth_token_enabled`) VALUES (@u, 'x', 'M', 'a@a.com', @t, 1)", c);
            ins.Parameters.AddWithValue("@u", user); ins.Parameters.AddWithValue("@t", token);
            await ins.ExecuteNonQueryAsync();
            aid = (uint)ins.LastInsertedId;
        }
        Assert.True(await db.VerifyAsync(aid, token));
        Assert.False(await db.VerifyAsync(aid, "0000000000000000"));
        Assert.False(await db.VerifyAsync(aid + 1, token));
        Assert.Null(await db.LoadAsync(aid));
        Assert.Equal((true, 1000L), await db.SaveAsync(aid, 1000, Json));
        Assert.Equal((false, 1000L), await db.SaveAsync(aid, 999, "{\"older\":1}"));
        Assert.Equal((1000L, Json), (await db.LoadAsync(aid))!.Value);
        string big = "{\"x\":\"" + new string('é', 20000) + "\"}";
        Assert.Equal((true, 2000L), await db.SaveAsync(aid, 2000, big));
        Assert.Equal((2000L, big), (await db.LoadAsync(aid))!.Value);
        // logged out for 10 s: rAthena disables the token (account.cpp 935-947); it no longer counts
        await using (var c = new MySqlConnection(conn))
        {
            await c.OpenAsync();
            await using var off = new MySqlCommand("UPDATE `login` SET `web_auth_token_enabled` = 0 WHERE `account_id` = @a", c);
            off.Parameters.AddWithValue("@a", aid);
            await off.ExecuteNonQueryAsync();
        }
        Assert.False(await db.VerifyAsync(aid, token));
    }
}
