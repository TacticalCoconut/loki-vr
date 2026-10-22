using System.Text;
using System.Text.RegularExpressions;

namespace LokiHost.Core;

/// <summary>
/// rAthena's text configuration files (conf/*.conf, conf/import/*.txt): "key: value" lines, "//" comments. The host
/// copy is configured the way testrig/oracle/private.sh configures an oracle copy: every key it owns is removed from
/// the import file and appended once, so the files the source rAthena brought along keep all their other lines.
/// </summary>
public static class Conf
{
    /// <summary>Sets <paramref name="key"/> in an import file: removes every active "key:" line, appends "key: value".</summary>
    public static void SetKey(string file, string key, string value)
    {
        var lines = File.Exists(file) ? File.ReadAllLines(file).ToList() : new List<string>();
        lines.RemoveAll(l => IsKeyLine(l, key));
        lines.Add($"{key}: {value}");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllLines(file, lines);
    }

    /// <summary>The value of the last active "key:" line of a file, or null.</summary>
    public static string? ReadKey(string file, string key)
    {
        if (!File.Exists(file)) return null;
        string? v = null;
        foreach (var l in File.ReadLines(file))
            if (IsKeyLine(l, key)) v = l[(l.IndexOf(':') + 1)..].Trim();
        return v;
    }

    /// <summary>A key as rAthena sees it: the import file (read after the base file) wins over the base file.</summary>
    public static string? ReadEffective(string baseFile, string importFile, string key) =>
        ReadKey(importFile, key) ?? ReadKey(baseFile, key);

    static bool IsKeyLine(string line, string key)
    {
        var t = line.TrimStart();
        if (t.StartsWith("//")) return false;
        int c = t.IndexOf(':');
        return c > 0 && string.Equals(t[..c].Trim(), key, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// conf/subnet_athena.conf (common/cli.cpp LAN_CONF_NAME; read by the login and char servers: char.cpp
    /// char_lan_subnetcheck, login.cpp lan_subnetcheck): "subnet: mask:char_ip:map_ip". A client whose address is in
    /// a listed subnet is sent that subnet's char / map address instead of the public one, so players on the host PC
    /// (127.x) and on the LAN connect locally and the router needs no NAT loopback.
    /// </summary>
    public static string SubnetText(string lanIp, string lanMask)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Written by Loki Host at every start. Players from these networks get these addresses;");
        sb.AppendLine("// everyone else gets the public address (char_ip / map_ip in conf/import).");
        sb.AppendLine("subnet: 255.0.0.0:127.0.0.1:127.0.0.1");
        if (!string.IsNullOrEmpty(lanIp) && !string.IsNullOrEmpty(lanMask))
            sb.AppendLine($"subnet: {lanMask}:{lanIp}:{lanIp}");
        return sb.ToString();
    }

    /// <summary>The test NPC files Loki's parity rig adds to npc/scripts_custom.conf (server/data/overlay).</summary>
    static readonly Regex TestNpc = new(@"^\s*npc:\s*npc/custom/(\w+_tests|loki_smoke)\.txt\s*$", RegexOptions.IgnoreCase);

    /// <summary>Comments out the parity rig's test NPC lines (files are left in place). Returns how many.</summary>
    public static int DisableTestNpcs(string scriptsCustomConf)
    {
        if (!File.Exists(scriptsCustomConf)) return 0;
        var lines = File.ReadAllLines(scriptsCustomConf);
        int n = 0;
        for (int i = 0; i < lines.Length; i++)
            if (TestNpc.IsMatch(lines[i])) { lines[i] = "// (Loki Host: test NPC, off) " + lines[i].Trim(); n++; }
        File.WriteAllLines(scriptsCustomConf, lines);
        return n;
    }

    /// <summary>rAthena's empty groups import (conf/import-tmpl/groups.yml): no permission changes.</summary>
    public const string DefaultGroupsImport = "Header:\n  Type: PLAYER_GROUP_DB\n  Version: 1\n";

    /// <summary>
    /// The only commands Loki Host gives every account (the design's decision, 2026-09-30): duels and chat channels, which the Loki
    /// client's duel and channel windows send as @commands (client wave 4, w4social) and stock rAthena gives group 0 none of.
    /// </summary>
    public static readonly string[] FriendCommands = { "duel", "invite", "accept", "reject", "leave", "join", "channel" };
    public const string FriendCommandsMarker = "# Loki Host: friends' commands";

    /// <summary>conf/import/groups.yml with <see cref="FriendCommands"/> added to rAthena's group 0 (pc_groups.cpp:125: an import
    /// entry of an existing group adds its commands; a command the group already had would make the entry fail, and stock
    /// group 0 has only changedress and resurrect).</summary>
    public static string FriendCommandsImport =>
        DefaultGroupsImport + "\n" + FriendCommandsMarker + ": every account may duel and use chat channels.\nBody:\n  - Id: 0\n    Commands:\n"
        + string.Concat(Array.ConvertAll(FriendCommands, c => $"      {c}: true\n"));

    /// <summary>
    /// True when conf/import/groups.yml gives the default group (Id 0, every new account) extra permissions, as the
    /// parity rig's overlay does ("every account is a GM"). Loki Host's own friends' commands (the marker, and group 0 given
    /// nothing but <see cref="FriendCommands"/>) are not that: a setup run again keeps them.
    /// </summary>
    public static bool GroupsImportElevatesPlayers(string groupsYml)
    {
        if (!File.Exists(groupsYml)) return false;
        var text = File.ReadAllText(groupsYml);
        if (!Regex.IsMatch(text, @"^\s*-\s*Id:\s*0\s*$", RegexOptions.Multiline)) return false;
        return !IsFriendCommandsOnly(text);
    }

    static bool IsFriendCommandsOnly(string text)
    {
        if (!text.Contains(FriendCommandsMarker)) return false;
        int body = text.IndexOf("\nBody:", StringComparison.Ordinal);
        if (body < 0) return false;
        foreach (var raw in text.Substring(body + 6).Split('\n'))
        {
            var l = raw.Trim();
            if (l.Length == 0 || l.StartsWith("#")) continue;
            if (Regex.IsMatch(l, @"^-\s*Id:\s*0$") || l == "Commands:") continue;
            var m = Regex.Match(l, @"^(\w+):\s*true$");
            if (!m.Success || Array.IndexOf(FriendCommands, m.Groups[1].Value.ToLowerInvariant()) < 0) return false;
        }
        return true;
    }
}
