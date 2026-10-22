using LokiHost.Core;
using Xunit;

namespace LokiHost.Tests;

/// <summary>The pure parts, with the repo's own overlay files (server/data/overlay) as fixtures.</summary>
public class CoreTests
{
    static readonly string Repo = FindRepo();
    static readonly string Overlay = Path.Combine(Repo, "server", "data", "overlay");

    static string FindRepo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "server", "data", "overlay"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    static string Temp()
    {
        var d = Path.Combine(Path.GetTempPath(), "lokihost-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>a test PC on 2026-09-29, in the order .NET listed the adapters (WireGuard VPN on): the Wi-Fi must win.</summary>
    static readonly Net.Adapter[] SamplePc =
    {
        new("Ethernet 2", System.Net.NetworkInformation.NetworkInterfaceType.Ethernet, false, "169.254.10.20", "255.255.0.0", new string[0]),
        new("WgTunnel", (System.Net.NetworkInformation.NetworkInterfaceType)53, true, "10.8.0.2", "255.255.0.0", new[] { "10.8.0.1" }),
        new("Virtual Loopback", System.Net.NetworkInformation.NetworkInterfaceType.Ethernet, true, "198.51.100.42", "0.0.0.0", new[] { "0.0.0.0" }),
        new("Wi-Fi 2", System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211, true, "192.168.1.15", "255.255.255.0", new[] { "192.168.1.1" }),
        new("Loopback Pseudo-Interface 1", System.Net.NetworkInformation.NetworkInterfaceType.Loopback, true, "127.0.0.1", "255.0.0.0", new string[0]),
        new("vEthernet (WSL (Hyper-V firewall))", System.Net.NetworkInformation.NetworkInterfaceType.Ethernet, true, "172.20.0.1", "255.255.240.0", new string[0]),
    };

    [Fact]
    public void The_lan_address_is_the_wifi_not_the_vpn_and_the_vpn_is_named()
    {
        Assert.Equal(("192.168.1.15", "255.255.255.0"), Net.PickLan(SamplePc));
        // Windows sends internet traffic from WgTunnel's address (UDP connect to 1.1.1.1 measured 10.8.0.2)
        Assert.Equal("WgTunnel", Net.VpnAdapter(SamplePc, "192.168.1.15", "10.8.0.2"));
        // VPN off: the internet leaves through the Wi-Fi
        var noVpn = SamplePc.Where(a => a.Name != "WgTunnel").ToArray();
        Assert.Equal(("192.168.1.15", "255.255.255.0"), Net.PickLan(noVpn));
        Assert.Null(Net.VpnAdapter(noVpn, "192.168.1.15", "192.168.1.15"));
        Assert.Null(Net.VpnAdapter(noVpn, "192.168.1.15", ""));
        // nothing with a router: no LAN address
        Assert.Equal(("", ""), Net.PickLan(SamplePc.Where(a => a.Name != "Wi-Fi 2" && a.Name != "WgTunnel")));
        Assert.True(Net.IsPrivate("172.16.5.1") && Net.IsPrivate("10.0.0.1") && Net.IsPrivate("192.168.1.2"));
        Assert.False(Net.IsPrivate("172.32.0.1") || Net.IsPrivate("198.51.100.42"));
    }

    [Fact]
    public async Task A_listening_port_is_seen_without_connecting_to_it()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        int port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        Assert.True(await Net.ListeningLocallyAsync(port));
        Assert.False(l.Pending()); // nobody connected
        l.Stop();
        Assert.False(await Net.ListeningLocallyAsync(port));
    }

    [Fact]
    public void SetKey_replaces_every_active_line_and_keeps_the_rest()
    {
        var f = Path.Combine(Temp(), "char_conf.txt");
        File.WriteAllLines(f, new[] { "// comment char_ip: 1.1.1.1", "login_ip: 127.0.0.1", "char_ip: 127.0.0.1", "pincode_enabled: no", "CHAR_IP: 9.9.9.9" });
        Conf.SetKey(f, "char_ip", "203.0.113.7");
        var lines = File.ReadAllLines(f);
        Assert.Equal(new[] { "// comment char_ip: 1.1.1.1", "login_ip: 127.0.0.1", "pincode_enabled: no", "char_ip: 203.0.113.7" }, lines);
        Assert.Equal("203.0.113.7", Conf.ReadKey(f, "char_ip"));
    }

    [Fact]
    public void ReadEffective_prefers_the_import_file()
    {
        var d = Temp();
        string b = Path.Combine(d, "login_athena.conf"), i = Path.Combine(d, "login_conf.txt");
        File.WriteAllLines(b, new[] { "new_account: no", "//use_MD5_passwords: yes", "use_MD5_passwords: no" });
        File.WriteAllLines(i, new[] { "new_account: yes" });
        Assert.Equal("yes", Conf.ReadEffective(b, i, "new_account"));
        Assert.Equal("no", Conf.ReadEffective(b, i, "use_MD5_passwords"));
        Assert.Null(Conf.ReadEffective(b, i, "acc_name_min_length"));
    }

    /// <summary>char.cpp's parser: sscanf "%63[^:]: %63[^:]:%63[^:]:%63[^\r\n]" and both addresses in the mask's subnet.</summary>
    [Fact]
    public void SubnetText_matches_rathenas_format()
    {
        var t = Conf.SubnetText("192.168.1.20", "255.255.255.0");
        var subnets = t.Split('\n').Where(l => l.StartsWith("subnet:")).Select(l => l.Trim()).ToArray();
        Assert.Equal(new[] { "subnet: 255.0.0.0:127.0.0.1:127.0.0.1", "subnet: 255.255.255.0:192.168.1.20:192.168.1.20" }, subnets);
        Assert.Single(Conf.SubnetText("", "").Split('\n'), l => l.StartsWith("subnet:"));
    }

    [Fact]
    public void The_overlays_test_settings_are_detected_and_turned_off()
    {
        var d = Temp();
        Directory.CreateDirectory(Path.Combine(d, "conf", "import"));
        Directory.CreateDirectory(Path.Combine(d, "npc"));
        File.Copy(Path.Combine(Overlay, "conf", "import", "groups.yml"), Path.Combine(d, "conf", "import", "groups.yml"));
        File.Copy(Path.Combine(Overlay, "npc", "scripts_custom.conf"), Path.Combine(d, "npc", "scripts_custom.conf"));
        int testLines = File.ReadAllLines(Path.Combine(d, "npc", "scripts_custom.conf")).Count(l => l.TrimStart().StartsWith("npc:"));
        Assert.True(testLines > 0);
        Assert.True(Conf.GroupsImportElevatesPlayers(Path.Combine(d, "conf", "import", "groups.yml")));

        var done = RathenaCopy.RemoveTestSettings(d);
        Assert.Equal(2, done.Count);
        Assert.Equal(Conf.DefaultGroupsImport, File.ReadAllText(Path.Combine(d, "conf", "import", "groups.yml")));
        Assert.True(File.Exists(Path.Combine(d, "conf", "import", "groups.yml.loki-test")));
        Assert.False(Conf.GroupsImportElevatesPlayers(Path.Combine(d, "conf", "import", "groups.yml")));
        // every test NPC's line is off; the lines left on are the players' own Codex bounties and tutorial instance
        // (the overlay's start-up set carries them for the module, and Loki Host installs the same lines itself), never a test NPC
        var kept = File.ReadAllLines(Path.Combine(d, "npc", "scripts_custom.conf")).Where(l => l.TrimStart().StartsWith("npc:")).Select(l => l.Trim()).ToArray();
        Assert.Equal(new[] { CodexBounty.NpcLine, TutorialInstance.NpcLine }, kept);
        Assert.Equal(testLines - kept.Length, File.ReadAllLines(Path.Combine(d, "npc", "scripts_custom.conf")).Count(l => l.Contains("(Loki Host: test NPC, off) npc:")));
        Assert.Empty(RathenaCopy.RemoveTestSettings(d)); // idempotent
    }

    [Fact]
    public void Loki_Hosts_friends_commands_are_kept_by_a_setup_run_again()
    {
        // the file on a local Loki Host since 2026-09-30 (C:\LokiHost\rathena\conf\import\groups.yml) is exactly this text
        Assert.Equal("Header:\n  Type: PLAYER_GROUP_DB\n  Version: 1\n\n# Loki Host: friends' commands: every account may duel and use chat channels.\n"
            + "Body:\n  - Id: 0\n    Commands:\n      duel: true\n      invite: true\n      accept: true\n      reject: true\n      leave: true\n"
            + "      join: true\n      channel: true\n", Conf.FriendCommandsImport);
        var d = Temp();
        Directory.CreateDirectory(Path.Combine(d, "conf", "import"));
        Directory.CreateDirectory(Path.Combine(d, "npc"));
        File.WriteAllText(Path.Combine(d, "npc", "scripts_custom.conf"), "");
        var groups = Path.Combine(d, "conf", "import", "groups.yml");
        File.WriteAllText(groups, Conf.FriendCommandsImport);
        Assert.False(Conf.GroupsImportElevatesPlayers(groups));
        Assert.Empty(RathenaCopy.RemoveTestSettings(d));
        Assert.Equal(Conf.FriendCommandsImport, File.ReadAllText(groups));
        // one command more than duels and channels is the test rig's kind again, marker or not
        File.WriteAllText(groups, Conf.FriendCommandsImport + "      allskill: true\n");
        Assert.True(Conf.GroupsImportElevatesPlayers(groups));
        File.WriteAllText(groups, Conf.FriendCommandsImport.Replace("  - Id: 0\n", "  - Id: 0\n    Permissions:\n      all_commands: true\n"));
        Assert.True(Conf.GroupsImportElevatesPlayers(groups));
    }

    [Fact]
    public void A_copy_takes_the_programs_and_the_four_folders_and_refuses_a_used_folder()
    {
        var src = Temp();
        foreach (var s in RathenaCopy.Servers) File.WriteAllText(Path.Combine(src, RathenaCopy.ExeName(s)), s);
        File.WriteAllText(Path.Combine(src, "libmysql.dll"), "dll");
        File.WriteAllText(Path.Combine(src, "README.md"), "not copied");
        foreach (var dir in new[] { "conf/import", "db/re", "npc/custom", "sql-files" }) Directory.CreateDirectory(Path.Combine(src, dir));
        File.WriteAllText(Path.Combine(src, "npc", "custom", "a.txt"), "npc");
        Directory.CreateDirectory(Path.Combine(src, "src")); // sources: not copied

        var dst = Path.Combine(Temp(), "host");
        RathenaCopy.Create(src, dst);
        Assert.True(File.Exists(Path.Combine(dst, RathenaCopy.ExeName("map-server"))));
        Assert.True(File.Exists(Path.Combine(dst, "libmysql.dll")));
        Assert.True(File.Exists(Path.Combine(dst, "npc", "custom", "a.txt")));
        Assert.False(File.Exists(Path.Combine(dst, "README.md")));
        Assert.False(Directory.Exists(Path.Combine(dst, "src")));
        Assert.Throws<InvalidOperationException>(() => RathenaCopy.Create(src, dst));

        RathenaCopy.UpdatePrograms(src, dst);
        var kept = Directory.GetDirectories(Path.Combine(dst, "_to_delete"));
        Assert.Single(kept);
        Assert.True(File.Exists(Path.Combine(kept[0], "libmysql.dll")));
        Assert.True(File.Exists(Path.Combine(dst, "libmysql.dll")));

        Assert.NotEmpty(RathenaCopy.Missing(Temp()));
    }

    [Fact]
    public void ApplyConf_writes_ports_addresses_accounts_and_subnets()
    {
        var d = Temp();
        Directory.CreateDirectory(Path.Combine(d, "conf", "import"));
        // the overlay's imports, as a PC copy may carry them (char_ip / map_ip 127.0.0.1)
        foreach (var f in new[] { "char_conf.txt", "map_conf.txt", "login_conf.txt" })
            File.Copy(Path.Combine(Overlay, "conf", "import", f), Path.Combine(d, "conf", "import", f));
        var s = new HostSettings { HostDir = d, DbPass = "dbpw", ServerUser = "srvX", ServerPass = "spw", AllowRegistration = true };
        HostSetup.ApplyConf(s, "203.0.113.7", "192.168.1.20", "255.255.255.0");
        string I(string f) => Path.Combine(d, "conf", "import", f);
        Assert.Equal("203.0.113.7", Conf.ReadKey(I("char_conf.txt"), "char_ip"));
        Assert.Equal("127.0.0.1", Conf.ReadKey(I("char_conf.txt"), "login_ip"));
        Assert.Equal("7901", Conf.ReadKey(I("char_conf.txt"), "char_port"));
        Assert.Equal("no", Conf.ReadKey(I("char_conf.txt"), "pincode_enabled")); // the overlay's game setting stays
        Assert.Equal("203.0.113.7", Conf.ReadKey(I("map_conf.txt"), "map_ip"));
        Assert.Equal("127.0.0.1", Conf.ReadKey(I("map_conf.txt"), "char_ip"));
        Assert.Equal("srvX", Conf.ReadKey(I("map_conf.txt"), "userid"));
        Assert.Equal("7900", Conf.ReadKey(I("login_conf.txt"), "login_port"));
        Assert.Equal("yes", Conf.ReadKey(I("login_conf.txt"), "new_account"));
        // client wave 18 (w18acct): settings per account (HostSettings.AccountSettings, on by default) need rAthena's web token of each
        // login, the relay's proof: the overlay's "no" becomes "yes"; with them off the build's line is left as it is
        Assert.Equal("yes", Conf.ReadKey(I("login_conf.txt"), "use_web_auth_token"));
        Assert.Single(File.ReadAllLines(I("login_conf.txt")), l => l.StartsWith("use_web_auth_token:"));
        foreach (var p in new[] { "login_server", "ipban_db", "char_server", "map_server", "web_server", "log_db" })
            Assert.Equal("loki_host", Conf.ReadKey(I("inter_conf.txt"), p + "_db"));
        Assert.Single(File.ReadAllLines(I("char_conf.txt")), l => l.StartsWith("char_ip:"));
        Assert.Contains("subnet: 255.255.255.0:192.168.1.20:192.168.1.20", File.ReadAllText(Path.Combine(d, "conf", "subnet_athena.conf")));
        // client wave 18 (w18acct): settings per account off: the token line is not written (a fresh copy keeps the overlay's "no")
        var d2 = Temp();
        Directory.CreateDirectory(Path.Combine(d2, "conf", "import"));
        foreach (var f in new[] { "char_conf.txt", "map_conf.txt", "login_conf.txt" })
            File.Copy(Path.Combine(Overlay, "conf", "import", f), Path.Combine(d2, "conf", "import", f));
        HostSetup.ApplyConf(new HostSettings { HostDir = d2, AccountSettings = false }, "203.0.113.7", "192.168.1.20", "255.255.255.0");
        Assert.Equal("no", Conf.ReadKey(Path.Combine(d2, "conf", "import", "login_conf.txt"), "use_web_auth_token"));
    }

    [Fact]
    public void ApplyConf_sets_the_view_range_and_keeps_the_builds_battle_lines()
    {
        var d = Temp();
        Directory.CreateDirectory(Path.Combine(d, "conf", "import"));
        string bc = Path.Combine(d, "conf", "import", "battle_conf.txt");
        // the build's import, as specified's PC build carries it (rates, Loki's sit regen), and an older area_size
        File.WriteAllLines(bc, new[] { "base_exp_rate: 500", "loki_sit_regen_rate: 25", "area_size: 14" });
        var s = new HostSettings { HostDir = d };
        Assert.Equal(60, s.ViewRange); // the design's choice: 30 on 2026-10-01, doubled on 2026-10-05
        HostSetup.ApplyConf(s, "203.0.113.7", "192.168.1.20", "255.255.255.0");
        Assert.Equal("60", Conf.ReadKey(bc, "area_size"));
        Assert.Single(File.ReadAllLines(bc), l => l.StartsWith("area_size:"));
        Assert.Equal("500", Conf.ReadKey(bc, "base_exp_rate"));
        Assert.Equal("25", Conf.ReadKey(bc, "loki_sit_regen_rate"));
        s.ViewRange = 0; // 0: leave it as the build has it
        HostSetup.ApplyConf(s, "203.0.113.7", "192.168.1.20", "255.255.255.0");
        Assert.Equal("60", Conf.ReadKey(bc, "area_size"));
    }

    [Fact]
    public void Load_doubles_the_view_range_of_a_settings_file_from_before_version_2_once()
    {
        string f = Path.Combine(Temp(), "lokihost.json");
        // a file as Loki Host wrote it before 2026-10-05: the old default, no SettingsVersion line
        File.WriteAllText(f, "{ \"HostDir\": \"C:\\\\LokiHost\\\\rathena\", \"ViewRange\": 30, \"SetUp\": true }");
        var s = HostSettings.Load(f);
        Assert.Null(s.LoadError);
        Assert.Equal(60, s.ViewRange);
        Assert.Equal(HostSettings.CurrentVersion, s.SettingsVersion);
        Assert.True(s.SetUp);
        Assert.Equal("C:\\LokiHost\\rathena", s.HostDir);
        // written back at once: the file says so itself now
        Assert.Contains("\"ViewRange\": 60", File.ReadAllText(f));
        Assert.Contains("\"SettingsVersion\": 2", File.ReadAllText(f));
        // the host goes back to 30 later: it stays (the upgrade happens once)
        s.ViewRange = 30; s.Save();
        Assert.Equal(30, HostSettings.Load(f).ViewRange);
        // a value that was the host's own choice before version 2 is not touched
        File.WriteAllText(f, "{ \"ViewRange\": 20 }");
        Assert.Equal(20, HostSettings.Load(f).ViewRange);
        File.WriteAllText(f, "{ \"ViewRange\": 0 }");
        Assert.Equal(0, HostSettings.Load(f).ViewRange);
        // no file yet: the new default, nothing written
        string none = Path.Combine(Temp(), "none.json");
        var n = HostSettings.Load(none);
        Assert.Equal(60, n.ViewRange);
        Assert.Equal(HostSettings.CurrentVersion, n.SettingsVersion);
        Assert.False(File.Exists(none));
        // a file that cannot be read is never overwritten by the upgrade
        File.WriteAllText(f, "{ not json");
        Assert.NotNull(HostSettings.Load(f).LoadError);
        Assert.Equal("{ not json", File.ReadAllText(f));
    }

    [Fact]
    public void LoginRules_follow_the_conf_with_rathenas_defaults()
    {
        var d = Temp();
        Directory.CreateDirectory(Path.Combine(d, "conf", "import"));
        File.WriteAllLines(Path.Combine(d, "conf", "login_athena.conf"), new[] { "//acc_name_min_length: 6", "use_MD5_passwords: no" });
        Assert.Equal(new Database.LoginRules(false, 6, 6), Database.LoginRules.Read(d));
        File.WriteAllLines(Path.Combine(d, "conf", "import", "login_conf.txt"), new[] { "use_MD5_passwords: yes", "acc_name_min_length: 4" });
        Assert.Equal(new Database.LoginRules(true, 4, 6), Database.LoginRules.Read(d));
        Assert.Throws<ArgumentException>(() => Database.CheckCredentials("abc", "secret1", new Database.LoginRules(false, 4, 6)));
        Assert.Throws<ArgumentException>(() => Database.CheckCredentials("abcd", "short", new Database.LoginRules(false, 4, 6)));
        Database.CheckCredentials("abcd", "secret", new Database.LoginRules(false, 4, 6));
    }

    [Fact]
    public void Passwords_are_stored_as_rathena_expects()
    {
        Assert.Equal("p1", Database.StoredPassword("p1", md5: false));
        Assert.Equal("ec6ef230f1828039ee794566b9c58adc", Database.StoredPassword("p1", md5: true)); // md5("p1")
        var s = Database.RandomSecret();
        Assert.Equal(20, s.Length);
        Assert.All(s, ch => Assert.True(char.IsAsciiLetterOrDigit(ch)));
    }

    [Fact]
    public void Port_checker_answers_are_read_leniently()
    {
        int[] ports = { 7900, 7901, 7902 };
        var r = Net.ParseChecker("{\"error\":false,\"host\":\"203.0.113.7\",\"check\":[{\"port\":7900,\"status\":true},{\"port\":7901,\"status\":false}]}", ports)!;
        Assert.Equal(Net.PortState.Open, r[7900]);
        Assert.Equal(Net.PortState.Closed, r[7901]);
        Assert.Equal(Net.PortState.Unknown, r[7902]);
        Assert.Equal(Net.PortState.Open, Net.ParseChecker("[{\"port\":7902,\"open\":true}]", ports)![7902]);
        Assert.Null(Net.ParseChecker("{\"message\":\"rate limited\"}", ports));
        Assert.Null(Net.ParseChecker("<html>", ports));
    }

    [Fact]
    public void Client_entry_and_firewall_commands()
    {
        Assert.Equal("{\"name\":\"Loki\",\"host\":\"203.0.113.7\",\"port\":7900,\"kind\":\"rathena\"}", Net.LokiJsonEntry("Loki", "203.0.113.7", 7900));
        var s = new HostSettings();
        // the headset relay's port (7903) is part of the rule since w3presence (PresenceTests)
        Assert.Equal("advfirewall firewall add rule name=\"Loki Host (TCP 7900-7903)\" dir=in action=allow protocol=TCP localport=7900,7901,7902,7903", Net.FirewallAddArgs(s));
        Assert.Equal("advfirewall firewall delete rule name=\"Loki Host (TCP 7900-7903)\"", Net.FirewallDeleteArgs(s));
    }

    /// <summary>A settings file that exists but cannot be read must not quietly become the defaults (the usual MariaDB and
    /// C:\LokiHost) nor be overwritten by them (found 2026-09-30 with a scratch file whose backslashes were not escaped).</summary>
    [Fact]
    public void An_unreadable_settings_file_is_reported_and_never_overwritten()
    {
        var f = Path.Combine(Temp(), "lokihost.json");
        var broken = "{ \"HostDir\": \"C:\\LokiVR\\tmp\\x\", \"DbPort\": 3319 }"; // \l and \t unescaped: not valid JSON
        File.WriteAllText(f, broken);
        var s = HostSettings.Load(f);
        Assert.NotNull(s.LoadError);
        Assert.Throws<InvalidOperationException>(() => s.Save());
        Assert.Equal(broken, File.ReadAllText(f));
        File.WriteAllText(f, "null");
        Assert.NotNull(HostSettings.Load(f).LoadError);
        Assert.Null(HostSettings.Load(Path.Combine(Temp(), "none.json")).LoadError); // no file yet: the defaults, no error
        File.WriteAllText(f, "{ \"HostDir\": \"C:\\\\LokiVR\\\\tmp\\\\x\", \"DbPort\": 3319 }");
        var ok = HostSettings.Load(f);
        Assert.Null(ok.LoadError);
        Assert.Equal((@"C:\LokiVR\tmp\x", 3319, 7903), (ok.HostDir, ok.DbPort, ok.PresencePort));
    }

    [Fact]
    public void Ansi_colours_are_removed()
    {
        Assert.Equal("[Status]: Map Server is now online.", Ansi.Strip("\u001b[1;32m[Status]\u001b[0m: Map Server is now online."));
    }

    [Fact]
    public async Task Temp_listeners_answer_and_close()
    {
        int port;
        using (var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0)) { probe.Start(); port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port; }
        using (var t = await Net.TempListeners.OpenAsync(new[] { port }))
        {
            Assert.Equal(new[] { port }, t.Opened);
            Assert.True(await Net.CanConnectAsync("127.0.0.1", port));
        }
        Assert.False(await Net.CanConnectAsync("127.0.0.1", port, 500));
    }
}
