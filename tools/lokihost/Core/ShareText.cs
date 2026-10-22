using System.Text;

namespace LokiHost.Core;

/// <summary>
/// "What to send your friends" (the Server tab), in English or Brazilian Portuguese (client wave 5, w5friends: checked against the
/// friends' guide in the release package, client/release/). It says what a friend does in Loki itself — the login screen's
/// Servers… → Add a server form (client UI/LoginPanel.Servers.cs: Name, Address, Port, Test connection, Save; a new entry's
/// headset port is the login port + 3, LoginPanel.ServerEntry.presencePort) — with the loki.json line as the other way, and how the
/// friend gets an account: from the host (the Accounts tab), or on the login screen when self-registration is on (Create account
/// ♂ / ♀, pressed twice for a server on another PC: the client's clear-text warning, UI/LoginPanel.Account.cs RegisterConfirmed).
/// The button names are the client's own (its pt-BR table, Resources/Lang/pt-BR.txt, for Portuguese).
/// </summary>
public static class ShareText
{
    public const string English = "en", Portuguese = "pt-BR";
    public static readonly string[] Languages = { English, Portuguese };
    /// <summary>The language's name in the window's chooser.</summary>
    public static string LanguageName(string lang) => lang == Portuguese ? "Português (Brasil)" : "English";

    /// <summary>The Ports tab's sentence about the relay's port, for the host: the headsets, and the voices while voice is relayed
    /// (client wave 6, w6voice). The text for friends below stays as it was: voice needs nothing from them but the same port.</summary>
    public static string RelayPortHint(HostSettings s) => s.VoiceEnabled
        ? $"({s.PresencePort} carries the players' headsets and voices: without it friends play, but don't see each other's head and hands or hear each other.)"
        : $"({s.PresencePort} carries the players' headsets: without it friends play, but don't see each other's head and hands.)";

    /// <summary>Client wave 7 (w7host): the Server tab's line about game data before the first Start, for the host: what Loki Host
    /// will do with the host's Ragnarok Online install, or that it has none to follow.</summary>
    public static string GameDataHint(HostSettings s) => s.RoDir.Trim() == ""
        ? "Map data: no Ragnarok Online folder is set (Setup tab): the server walks on the map data it has, and players' game data is not compared with yours."
        : $"Map data: your Ragnarok Online folder {s.RoDir.Trim()} is looked at when you press Start: if its official patcher changed the maps, the server's map data is rebuilt from them (the old file kept). Keep it patched; friends whose game data differs are told and listed below.";

    /// <summary>Shown instead of the public address before the first Start (for the host, not the friends).</summary>
    public const string NoAddressYet = "(your public address: shown after Start)";

    /// <summary>
    /// The text. <paramref name="publicIp"/> "" before the first start; <paramref name="lanIp"/> "" when there is no home network;
    /// <paramref name="vpn"/> the VPN adapter carrying the internet traffic (then the internet part is replaced by the warning, which is
    /// for the host: in English); <paramref name="nameMin"/> / <paramref name="passMin"/> the host's acc_name_min_length /
    /// password_min_length (Database.LoginRules), used when friends make their own account.
    /// </summary>
    public static string Build(HostSettings s, string publicIp, string lanIp, string? vpn, string lang, int nameMin = 6, int passMin = 6)
    {
        bool pt = lang == Portuguese;
        string pub = publicIp == "" ? NoAddressYet : publicIp;
        int presence = s.PresenceEnabled ? s.PresencePort : 0;
        // the in-game form cannot set the headset port: a new entry uses the login port + 3 (client LoginPanel.ServerEntry.presencePort)
        bool formCoversPresence = presence == 0 || presence == s.LoginPort + 3;
        var sb = new StringBuilder();
        sb.AppendLine(pt ? $"Ragnarok Online em VR com o Loki, no meu servidor \"{s.ServerName}\"."
                         : $"Ragnarok Online in VR with Loki, on my server \"{s.ServerName}\".");
        // client wave 7 (w7host): what a friend needs of the game itself (the friends' guide says the same: client/release/READ-ME.txt,
        // LEIA-ME.txt "instalado e atualizado pelo patcher"); the last sentence only when this host compares game data
        bool compares = s.RoDir.Trim() != "" && s.PresenceEnabled;
        sb.AppendLine(pt ? "Você precisa do seu próprio Ragnarok Online instalado e atualizado pelo patcher oficial: o Loki lê o jogo da sua instalação e acompanha as atualizações dela."
                           + (compares ? " Quando o jogo atualizar, atualize o seu: o Loki avisa no chat se os seus dados do jogo forem diferentes dos meus." : "")
                         : "You need your own Ragnarok Online installed and kept up to date with its official patcher: Loki reads the game from your install and follows its updates."
                           + (compares ? " When the game is patched, patch yours: Loki tells you in the chat if your game data differs from mine." : ""));
        if (vpn != null) sb.AppendLine("Over the internet: not possible now. " + Net.VpnWarning(vpn));
        else
        {
            sb.AppendLine(pt ? $"Pela internet: endereço {pub}, porta {s.LoginPort}."
                             : $"Over the internet: address {pub}, port {s.LoginPort}.");
            sb.AppendLine(pt ? $"No Loki, na tela de login: Servidores… → Adicionar um servidor → Nome {s.ServerName}, Endereço {pub}, Porta {s.LoginPort} → Testar conexão → Salvar."
                             : $"In Loki, on the login screen: Servers… → Add a server → Name {s.ServerName}, Address {pub}, Port {s.LoginPort} → Test connection → Save.");
            string entry = Net.LokiJsonEntry(s.ServerName, pub, s.LoginPort, presence);
            if (formCoversPresence)
                sb.AppendLine(pt ? $"(Ou acrescente esta linha em \"servers\" no arquivo Loki_Data\\StreamingAssets\\loki.json: {entry})"
                                 : $"(Or add this line to \"servers\" in Loki_Data\\StreamingAssets\\loki.json: {entry})");
            else
                sb.AppendLine(pt ? $"Para ver os headsets dos outros (porta {presence}), acrescente em vez disso esta linha em \"servers\" no arquivo Loki_Data\\StreamingAssets\\loki.json: {entry}"
                                 : $"To see each other's headsets (port {presence}), add this line to \"servers\" in Loki_Data\\StreamingAssets\\loki.json instead: {entry}");
        }
        if (lanIp != "")
            sb.AppendLine(pt ? $"Na mesma rede de casa que eu (LAN): endereço {lanIp}, porta {s.LoginPort}."
                             : $"On my home network (LAN): address {lanIp}, port {s.LoginPort}.");
        if (s.AllowRegistration)
            sb.Append(pt ? $"Sua conta: na tela de login digite um nome ({nameMin} a 21 letras ou números) e uma senha ({passMin} a 23 letras, números ou símbolos, sem espaços nem acentos) e aperte Criar conta ♂ ou ♀ duas vezes. Só desta vez a senha viaja sem criptografia: use uma que você não usa em nenhum outro lugar."
                         : $"Your account: on the login screen type a name ({nameMin} to 21 letters or digits) and a password ({passMin} to 23 letters, digits or symbols, no spaces or accents), then press Create account ♂ or ♀ twice. This once the password travels unencrypted: use one you use nowhere else.");
        else
            sb.Append(pt ? "Sua conta: me peça um nome e uma senha (eu crio no Loki Host)."
                         : "Your account: ask me for a name and a password (I make it in Loki Host).");
        return sb.ToString();
    }
}
