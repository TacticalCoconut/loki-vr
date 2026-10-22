using LokiHost.Core;
using Xunit;

namespace LokiHost.Tests;

/// <summary>
/// Client wave 5 (w5friends): "What to send your friends" and the Accounts tab's rules, checked against the friends' guide and the
/// Loki client's own login screen (its button names in English and in its pt-BR table, what its fields accept).
/// </summary>
public class FriendsTests
{
    static readonly string Repo = FindRepo();

    static string FindRepo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "client", "Assets", "Loki", "Resources", "Lang", "pt-BR.txt"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    static HostSettings Host(bool registration = false, int presence = 7903, bool presenceOn = true) =>
        new() { ServerName = "Loki", LoginPort = 7900, CharPort = 7901, MapPort = 7902, PresencePort = presence, PresenceEnabled = presenceOn, AllowRegistration = registration };

    [Fact]
    public void The_share_text_says_what_a_friend_does_in_Loki_in_English()
    {
        var t = ShareText.Build(Host(), "203.0.113.7", "192.168.1.15", null, ShareText.English);
        Assert.Contains("Over the internet: address 203.0.113.7, port 7900.", t);
        Assert.Contains("In Loki, on the login screen: Servers… → Add a server → Name Loki, Address 203.0.113.7, Port 7900 → Test connection → Save.", t);
        // the form gives a new entry the login port + 3 for the headsets (7903): the loki.json line is only the other way
        Assert.Contains("(Or add this line to \"servers\" in Loki_Data\\StreamingAssets\\loki.json: {\"name\":\"Loki\",\"host\":\"203.0.113.7\",\"port\":7900,\"kind\":\"rathena\",\"presencePort\":7903})", t);
        Assert.Contains("On my home network (LAN): address 192.168.1.15, port 7900.", t);
        Assert.EndsWith("Your account: ask me for a name and a password (I make it in Loki Host).", t);
        Assert.DoesNotContain("Accounts tab", t); // the text goes to friends: nothing addressed to the host
    }

    [Fact]
    public void The_share_text_in_Portuguese_uses_the_clients_own_button_names()
    {
        var en = ShareText.Build(Host(registration: true), "203.0.113.7", "", null, ShareText.English);
        var pt = ShareText.Build(Host(registration: true), "203.0.113.7", "", null, ShareText.Portuguese);
        // every button the English text names, and its Portuguese line in the client's table (client/Assets/Loki/Resources/Lang/pt-BR.txt)
        var table = File.ReadAllLines(Path.Combine(Repo, "client", "Assets", "Loki", "Resources", "Lang", "pt-BR.txt"))
            .Where(l => l.Contains('\t') && !l.StartsWith("#")).Select(l => l.Split('\t', 2)).GroupBy(p => p[0]).ToDictionary(g => g.Key, g => g.First()[1]);
        foreach (var label in new[] { "Servers…", "Add a server", "Name", "Address", "Port", "Test connection", "Save", "Create account ♂" })
        {
            Assert.Contains(label, en);
            Assert.True(table.ContainsKey(label), $"the client has no pt-BR line for '{label}'");
            var ptLabel = table[label].Replace(" ♂", "");
            Assert.Contains(ptLabel, pt);
        }
        Assert.Contains("No Loki, na tela de login: Servidores… → Adicionar um servidor → Nome Loki, Endereço 203.0.113.7, Porta 7900 → Testar conexão → Salvar.", pt);
        Assert.DoesNotContain("LAN", pt); // no home network found: no LAN line
    }

    [Fact]
    public void With_self_registration_the_text_gives_the_hosts_limits_and_the_clear_text_warning()
    {
        var en = ShareText.Build(Host(registration: true), "203.0.113.7", "", null, ShareText.English, nameMin: 6, passMin: 8);
        // 21: the client's registration limit (the _M / _F suffix fills the 23-character name); 23: password[24] of CA_LOGIN
        Assert.Contains("type a name (6 to 21 letters or digits) and a password (8 to 23 letters, digits or symbols, no spaces or accents), then press Create account ♂ or ♀ twice", en);
        Assert.Contains("This once the password travels unencrypted", en);
        var pt = ShareText.Build(Host(registration: true), "203.0.113.7", "", null, ShareText.Portuguese, nameMin: 6, passMin: 8);
        Assert.Contains("aperte Criar conta ♂ ou ♀ duas vezes", pt);
        Assert.Contains("um nome (6 a 21 letras ou números) e uma senha (8 a 23", pt);
    }

    [Fact]
    public void A_vpn_a_moved_headset_port_and_a_host_not_started_yet_are_said()
    {
        var vpn = ShareText.Build(Host(), "198.51.100.9", "192.168.1.15", "WgTunnel", ShareText.Portuguese);
        Assert.Contains("Over the internet: not possible now.", vpn); // for the host: the warning in the window's language
        Assert.DoesNotContain("198.51.100.9", vpn);
        var moved = ShareText.Build(Host(presence: 7910), "203.0.113.7", "", null, ShareText.English);
        Assert.Contains("To see each other's headsets (port 7910), add this line to \"servers\" in Loki_Data\\StreamingAssets\\loki.json instead:", moved);
        var off = ShareText.Build(Host(presenceOn: false), "203.0.113.7", "", null, ShareText.English);
        Assert.Contains("{\"name\":\"Loki\",\"host\":\"203.0.113.7\",\"port\":7900,\"kind\":\"rathena\"})", off);
        Assert.Contains(ShareText.NoAddressYet, ShareText.Build(Host(), "", "", null, ShareText.English));
    }

    [Fact]
    public void The_accounts_tab_makes_only_accounts_a_friend_can_log_in_with()
    {
        var r = new Database.LoginRules(false, 6, 6);
        Database.CheckCredentials("w5friend01", "senha123!", r);
        Database.CheckCredentials("ana.b-c_d", new string('x', 32), r);
        // names Loki's login screen cannot type (UI/LoginPanel.cs: letters, digits, _ - .) or reaching rAthena as other bytes
        foreach (var bad in new[] { "ana silva", "joão123", "friend!1", "amigo@casa" })
            Assert.Throws<ArgumentException>(() => Database.CheckCredentials(bad, "senha123", r));
        // _M / _F: rAthena takes such a login for a registration while new_account is on (login.cpp:324-338)
        foreach (var bad in new[] { "friend_M", "friend_f", "amigo1_F" })
            Assert.Throws<ArgumentException>(() => Database.CheckCredentials(bad, "senha123", r));
        Database.CheckCredentials("friend_Mx", "senha123", r);
        foreach (var bad in new[] { "senha 123", "senhaç123", "señor1234", "tab\tpass1", new string('x', 33) })
            Assert.Throws<ArgumentException>(() => Database.CheckCredentials("w5friend01", bad, r));
        Assert.Throws<ArgumentException>(() => Database.CheckCredentials("w5frie", "12345", r)); // the host's minimum (6)
    }
}
