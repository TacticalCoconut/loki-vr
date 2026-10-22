namespace LokiHost.Core;

/// <summary>
/// Wave 28 (wcxp): the Codex's bounties on Loki Host's own rAthena copy. design note, 2026-10-15..17: a SCAN places a 'Codex Bounty' (the server
/// times the scan from its own first sight of the creature near the character); a KILL completes it with a flat Base EXP reward determined
/// solely by the creature's level and danger rank (0 normal, 1 boss, 2 MVP); the CHARACTER claims it with a button in the Codex (Zeny instead at
/// the job's highest level at the claim); a 10-level bracket whose every bounty is claimed has its own flat EXP bonus (5x the normal bounty
/// at the bracket's top level) and button; quest and event creatures are placed the moment they are seen and complete
/// when they are gone, whoever killed them; party members within sight and not more than 10 levels below the creature complete too.
///
/// The SERVER decides and grants; the client only asks. The rules and tables are ONE rAthena NPC script, generated from the data by
/// client/tools/codex/build_bounty_script.py into server/data/overlay/npc/custom/loki_codex.txt (the SpacetimeDB module runs the same
/// file from its overlay) and embedded here, so Loki Host and the module can never disagree. At every start
/// (<see cref="HostSetup.ApplyConf"/>) it is written into the copy's npc/custom with its line in npc/scripts_custom.conf (or the line
/// commented out when the host's setting <see cref="HostSettings.CodexBounties"/> is off; the characters' bounties stay in their
/// character variables and come back with the line).
///
/// What the script does, in rAthena's terms (every buildin checked in rAthena e985006, C:\rAthena; the script's header has
/// the whole protocol):
///   - a Loki client whispers "NPC:LokiCodex" (clif.cpp clif_parse_WisMessage → the NPC's OnWhisperGlobal, '#' splits @whispervarN$):
///     "s" the character's state, "n#&lt;id&gt;" a creature in sight, "b#&lt;id&gt;" a finished scan, "x#&lt;unit&gt;" a death it saw,
///     "c#&lt;id&gt;" / "k#&lt;b&gt;" the Claim buttons, "p" a look at the watched quest / event creatures. A living monster of that kind
///     within the character's sight (getareaunits BL_MOB, area_size + 4 cells; getunitdata UMOB_CLASS / UMOB_HP) is checked by the
///     server every time; anything else is refused without a word.
///   - kept in CHARACTER variables (char-server's char_reg_num), packed: 2 bits per creature in LOKI_CXF[id / 31], 2 bits per bracket
///     in LOKI_CXB, an amount only while it waits (LOKI_CXE[id], LOKI_CXK[b]), a watch list of at most 16 units.
///   - a KILL: OnNPCKillEvent (the player with the most damage, its homunculus / mercenary / pet / elemental included: mob.cpp:3046-3057,
///     3605-3608) or, for a monster with its own event label (mob.cpp:3591-3604), the character's own last kill (killedgid).
///   - pays with getexp2 (script.cpp:24713-24735: pc_gainexp flag 2: no EXP rate, no guild tax, no EXP boost, no max_exp_gain_rate:
///     the exact number), or at the job's highest base level its Zeny (the Zeny parameter, never over MAX_ZENY); a level crossed by the
///     grant carries over as any EXP does (pc.cpp pc_checkbaselevelup; multi_level_up off in rAthena's conf/battle/exp.conf).
/// On a server without this NPC (any other rAthena) the client's whisper is answered "not online" (ZC_ACK_WHISPER 1): the client then
/// shows no bounty at all.
/// </summary>
public static class CodexBounty
{
    /// <summary>The NPC the client whispers ("NPC:" + this; client: Game/CodexBounty.cs Npc). The two must stay equal.</summary>
    public const string Npc = "LokiCodex";
    public const string ScriptFile = "loki_codex.txt";
    public const string NpcLine = "npc: npc/custom/" + ScriptFile;
    const string NpcLineOff = "// (Loki Host: Codex bounties off) " + NpcLine;
    const string Resource = "LokiHost.Core.CodexBounty.loki_codex.txt";

    /// <summary>The script as generated (server/data/overlay/npc/custom/loki_codex.txt, embedded at build time), "\n" line ends.</summary>
    public static string Script()
    {
        using var s = typeof(CodexBounty).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException("the Codex bounty script is not embedded (" + Resource + ")");
        using var r = new StreamReader(s);
        return r.ReadToEnd().Replace("\r\n", "\n");
    }

    /// <summary>npc/scripts_custom.conf's text with the script's line on (once) or off (commented out, never removed).</summary>
    public static string ApplyScripts(string text, bool on)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        bool seen = false;
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            string t = lines[i].Trim();
            if (t != NpcLine && t != NpcLineOff) continue;
            if (seen) { lines.RemoveAt(i); continue; } // (a second line of ours: one is enough)
            seen = true; lines[i] = on ? NpcLine : NpcLineOff;
        }
        if (!seen && on) lines.Add(NpcLine);
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>
    /// Writes the script and its line into the copy (each only when its text changes) and says what it did. Off: the line is
    /// commented out (the file stays). A copy without npc/scripts_custom.conf (not set up yet) is left alone.
    /// </summary>
    public static List<string> Write(string hostDir, bool on)
    {
        var said = new List<string>();
        string conf = Path.Combine(hostDir, "npc", "scripts_custom.conf"), script = Path.Combine(hostDir, "npc", "custom", ScriptFile);
        if (!File.Exists(conf)) return said;
        string oldConf = File.ReadAllText(conf);
        if (on || oldConf.Contains(NpcLine))
        {
            string text = ApplyScripts(oldConf, on);
            if (text.Replace("\r\n", "\n") != oldConf.Replace("\r\n", "\n")) File.WriteAllText(conf, text);
        }
        if (on)
        {
            string text = Script();
            if (!File.Exists(script) || File.ReadAllText(script).Replace("\r\n", "\n") != text) { Directory.CreateDirectory(Path.GetDirectoryName(script)!); File.WriteAllText(script, text); }
            said.Add("Codex bounties: on (a scan places a bounty, a kill completes it, the Codex claims it).");
        }
        else if (oldConf.Contains(NpcLine)) said.Add("Codex bounties: off.");
        return said;
    }
}
