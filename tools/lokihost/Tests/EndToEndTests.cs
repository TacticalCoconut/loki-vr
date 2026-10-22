using System.Net.Sockets;
using System.Text;
using LokiHost.Core;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>
/// The whole flow against a real rAthena build (Linux here; the Windows app runs the same code with *.exe files):
/// setup, start, the map server connecting through the random server account, a player login with an account the tool
/// made, and a clean stop through SIGINT (Ctrl+C on Windows). Runs only when LOKIHOST_TEST_ADMIN and
/// LOKIHOST_TEST_RATHENA_BUILD (a built rAthena folder) are set.
/// </summary>
public class EndToEndTests
{
    readonly ITestOutputHelper _out;
    public EndToEndTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public async Task Setup_start_login_stop()
    {
        var admin = Environment.GetEnvironmentVariable("LOKIHOST_TEST_ADMIN");
        var build = Environment.GetEnvironmentVariable("LOKIHOST_TEST_RATHENA_BUILD");
        if (string.IsNullOrEmpty(admin) || string.IsNullOrEmpty(build)) return;
        var (au, ap) = (admin.Split(':')[0], admin[(admin.IndexOf(':') + 1)..]);
        var root = Path.Combine(Path.GetTempPath(), "lokihost-e2e-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
        var s = new HostSettings
        {
            SourceDir = build, HostDir = Path.Combine(root, "rathena"), FilePath = Path.Combine(root, "lokihost.json"),
            DbName = "lokihost_e2e_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), DbUser = "lokihost_e2e",
            LoginPort = 17900, CharPort = 17901, MapPort = 17902, ServerName = "E2E",
        };
        var log = new List<string>();
        void Log(string l) { lock (log) log.Add(l); _out.WriteLine(l); }

        HostSetup.Run(s, au, ap, Log);
        Assert.True(s.SetUp);
        Assert.StartsWith("srv", s.ServerUser);
        Assert.False(Conf.GroupsImportElevatesPlayers(Path.Combine(s.HostDir, "conf", "import", "groups.yml")));

        var rules = Database.LoginRules.Read(s.HostDir);
        Database.AddAccount(Database.ConnStr(s), "friend01", "friendpw1", 'M', 0, rules);

        var host = new HostController(s);
        var online = new TaskCompletionSource();
        host.Output += (n, l) => { if (l.Contains("Map Server is now online")) online.TrySetResult(); };
        try
        {
            await host.StartAsync(Log, publicIpOverride: "203.0.113.7", waitSeconds: 300);
            Assert.Equal("203.0.113.7", Conf.ReadKey(Path.Combine(s.HostDir, "conf", "import", "map_conf.txt"), "map_ip"));
            // the map server reports online once the char server accepted it (the random server account works)
            Assert.Same(online.Task, await Task.WhenAny(online.Task, Task.Delay(TimeSpan.FromMinutes(5))));
            var st = await host.StatusAsync(checkDatabase: true);
            Assert.Equal(new HostController.Status(true, true, true, true), st);

            Assert.Equal(0x0ac4, await LoginReply("127.0.0.1", s.LoginPort, "friend01", "friendpw1"));   // AC_ACCEPT_LOGIN
            Assert.Equal(0x083e, await LoginReply("127.0.0.1", s.LoginPort, "friend01", "wrongpass"));   // AC_REFUSE_LOGIN
        }
        finally
        {
            await host.StopAsync(Log);
        }
        Assert.False(host.AnyRunning);
        lock (log) Assert.DoesNotContain(log, l => l.Contains("by force"));
    }

    /// <summary>CA_LOGIN (common/packets.hpp: 0x64, version, username[24], password[24], clienttype) → the reply's packet id.</summary>
    internal static async Task<int> LoginReply(string host, int port, string user, string pass)
    {
        using var c = new TcpClient();
        await c.ConnectAsync(host, port);
        var p = new byte[55];
        BitConverter.GetBytes((short)0x64).CopyTo(p, 0);
        BitConverter.GetBytes(55u).CopyTo(p, 2);
        Encoding.ASCII.GetBytes(user).CopyTo(p, 6);
        Encoding.ASCII.GetBytes(pass).CopyTo(p, 30);
        p[54] = 22;
        var st = c.GetStream();
        await st.WriteAsync(p);
        var head = new byte[2];
        int got = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (got < 2) { int n = await st.ReadAsync(head.AsMemory(got), cts.Token); if (n == 0) break; got += n; }
        return got == 2 ? BitConverter.ToUInt16(head, 0) : -1;
    }
}
