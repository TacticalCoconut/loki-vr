using System.Text.RegularExpressions;

namespace LokiHost.Core;

/// <summary>
/// Client wave 21 (w21map), the design's point 47: the controls tutorial runs on a map of its own, a PRIVATE DUPLICATE (an rAthena
/// instance, one per character) of a mythology map, with no NPC, no warp, no monster and nobody else on it; when it ends the
/// character is back where it came from (a new character: its start point). design note, 2026-10-10: "I agree with the Valkyrie's Hall
/// choice. Just make sure that you use a duplicate copy of it with no other NPCs there other than the poring present".
///
/// What the copy gets, written again at every start (<see cref="HostSetup.ApplyConf"/>), only in Loki Host's own rAthena copy:
/// - db/import/instance_db.yml: ONE entry (<see cref="Id"/>, <see cref="Name"/>) in a block Loki Host owns (<see cref="YamlBlock"/>):
///   the map and the entry cell (the host's setting TutorialMap, <see cref="DefaultMap"/>), NoNpc (rAthena then copies none of the
///   source map's NPCs and warps: instance.cpp:766-767; monsters are spawned on the source map only, never in a copy), a time limit
///   and an idle time-out (rAthena destroys the copy then, instance.cpp instance_startkeeptimer / instance_startidletimer).
///   NOT NoMapFlag: with it this rAthena (e985006) crashes at the first flag read or written on the copy. A copy made in a map slot
///   never used before has no flag list at all (map.cpp map_addinstancemap fills it only through map_data_copy, which NoMapFlag
///   skips; initMapFlags runs only for the maps read at start, map.cpp:3771, and when a copy is deleted, :2973), so getMapFlag /
///   setMapFlag index an empty vector (map.cpp:5284-5304). Found by the client smoke w21map, 2026-10-11: the map server died right
///   after "[Instance] Created". So the copy starts with the source map's own flags (the Valkyrie's hall: nomemo) and the script adds its own;
/// - npc/custom/loki_tutorial.txt and its line in npc/scripts_custom.conf: the NPC "LokiTutorial" (no body on any map). A Loki client
///   whispers "NPC:LokiTutorial" (rAthena runs the NPC's OnWhisperGlobal: clif.cpp clif_parse_WisMessage): "start" when its tutorial
///   begins, "end" when it is over. On a server without this NPC the whisper is answered "not online" and the client runs the
///   tutorial where the character stands, as before.
///
/// The script (<see cref="Script"/>), in rAthena's terms:
///   start  the character's own instance (IM_CHAR) is made if it has none (a live one, after a relog, is used again); the cell the
///          character stands on is kept in its own variables (LOKITUT_M$ / _X / _Y) and set as the copy's "nosave" return point, so a
///          logout, a crash or a lost connection inside saves the character THERE, never in the copy (pc.cpp pc_makesavestatus); the
///          copy also gets nomemo, noteleport, nowarpto and nobranch; then instance_enter (the entry cell).
///   end    inside the copy: warped back to the kept cell; the instance destroyed at once; the variables cleared.
///   Never stuck: a copy that rAthena destroys by itself (the time limit) sends whoever is inside to their save point
///   (map.cpp map_instancemap_leave); a copy nobody is in goes after the idle time-out (a logout: <see cref="IdleTimeOut"/> s).
///   Refused, nothing changed (the client then runs the tutorial where the character stands): the character owns another instance, or
///   stands in one.
/// </summary>
public static class TutorialInstance
{
    /// <summary>THE line to change for another map: the instance's source map and the cell the character enters at ("map,x,y").
    /// It is the default of the host's setting TutorialMap (lokihost.json); "" there = no private map (the tutorial runs where the
    /// character stands). valkyrie = Valhalla, the Valkyrie's hall: the design's choice of 2026-10-10 among the measured candidates
    /// (design notes); 49,36 = the middle of its level 12 x 12 square south of the round emblem.</summary>
    public const string DefaultMap = "valkyrie,49,36";

    /// <summary>The instance's name in instance_db: what the script creates, and what rAthena tells the character's client
    /// (ZC_MEMORIALDUNGEON_* 0x02cb / 0x02cd carry it): the Loki client knows its tutorial's map by this name
    /// (client: LokiGame.W21Map.cs W21TutInstance).</summary>
    public const string Name = "Loki Tutorial";
    /// <summary>The NPC the client whispers ("NPC:" + this; client: LokiGame.W21Map.cs W21TutNpc).</summary>
    public const string Npc = "LokiTutorial";
    /// <summary>instance_db Id: rAthena's own are 1-79 (db/re/instance_db.yml); far above them.</summary>
    public const int Id = 9047;
    /// <summary>Seconds: the copy's whole life (2 h: a long, interrupted tutorial still fits) and how long it stays once nobody is
    /// in it (1 min: a logout or a crash; a relog within it uses the same copy again).</summary>
    public const int TimeLimit = 7200, IdleTimeOut = 60;

    public const string ScriptFile = "loki_tutorial.txt";
    public const string NpcLine = "npc: npc/custom/" + ScriptFile;
    const string NpcLineOff = "// (Loki Host: no tutorial map) " + NpcLine;
    public const string Begin = "# >>> Loki Host: the controls tutorial's private map (the host's setting TutorialMap; this block is rewritten at every start) >>>";
    public const string End = "# <<< Loki Host <<<";

    /// <summary>The setting "map,x,y" read; null when it is empty or not of that form (then there is no private map).</summary>
    public static (string Map, int X, int Y)? Parse(string? setting)
    {
        var m = Regex.Match(setting ?? "", @"^\s*([A-Za-z0-9_@\-]{1,11})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*$");
        if (!m.Success) return null;
        int x = int.Parse(m.Groups[2].Value), y = int.Parse(m.Groups[3].Value);
        return x > 0 && y > 0 ? (m.Groups[1].Value, x, y) : null; // (instance.cpp:197 / :218: X and Y must be greater than zero)
    }

    /// <summary>The instance_db entry's lines (without indentation: <see cref="YamlBlock"/> indents them as the file's own).</summary>
    public static IReadOnlyList<string> Entry(string map, int x, int y) => new[]
    {
        $"- Id: {Id}",
        $"  Name: {Name}",
        $"  TimeLimit: {TimeLimit}",
        $"  IdleTimeOut: {IdleTimeOut}",
        "  NoNpc: true",
        "  Destroyable: false",
        "  Enter:",
        $"    Map: {map}",
        $"    X: {x}",
        $"    Y: {y}",
    };

    /// <summary>db/import/instance_db.yml's text with the host's block for <paramref name="map"/> (null: the block taken out).</summary>
    public static string ApplyDb(string text, (string Map, int X, int Y)? map, int headerVersion) =>
        YamlBlock.Apply(text, Begin, End, map is { } m ? new[] { Entry(m.Map, m.X, m.Y) } : Array.Empty<IReadOnlyList<string>>(), "INSTANCE_DB", headerVersion);

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

    /// <summary>The NPC script, as written to npc/custom/loki_tutorial.txt (the map and the cell are not in it: it reads them from
    /// instance_db, so the setting is the only place they stand).</summary>
    public static string Script() => string.Join("\n", new[]
    {
        "//===== Loki Host ============================================",
        "//= The controls tutorial's private map (client wave 21, the design's point 47).",
        "//= Written by Loki Host at every start (tools/lokihost/Core/TutorialInstance.cs):",
        "//= changes made here are lost. The map and its entry cell are the host's setting",
        "//= TutorialMap (db/import/instance_db.yml, the entry \"" + Name + "\").",
        "//============================================================",
        "-\tscript\t" + Npc + "\t-1,{",
        "\tend;",
        "",
        "// A Loki client whispers \"NPC:" + Npc + "\": \"start\" when its tutorial begins, \"end\" when it is over.",
        "OnWhisperGlobal:",
        "\t.@name$ = \"" + Name + "\";",
        "\t.@id = instance_id(IM_CHAR);",
        "\tif (.@id > 0 && instance_live_info(ILI_NAME, .@id) != .@name$)",
        "\t\tend; // the character owns another instance: nothing is touched",
        "\tgetmapxy(.@m$, .@x, .@y, BL_PC);",
        "\t.@src$ = instance_info(.@name$, IIT_ENTER_MAP);",
        "\t.@ex = instance_info(.@name$, IIT_ENTER_X);",
        "\t.@ey = instance_info(.@name$, IIT_ENTER_Y);",
        "\t.@inside = (.@id > 0 && .@m$ == instance_mapname(.@src$, .@id));",
        "\tif (@whispervar0$ == \"start\" || @whispervar0$ == \"enter\") {",
        "\t\tif (!.@inside) {",
        "\t\t\tif (compare(.@m$, \"#\"))",
        "\t\t\t\tend; // standing in an instance: not from here",
        "\t\t\t// where the tutorial gives the character back: where it stands now (a new character: its start point)",
        "\t\t\tLOKITUT_M$ = .@m$; LOKITUT_X = .@x; LOKITUT_Y = .@y;",
        "\t\t}",
        "\t\tif (.@id <= 0) {",
        "\t\t\t.@id = instance_create(.@name$, IM_CHAR);",
        "\t\t\tif (.@id <= 0) {",
        "\t\t\t\tif (.@src$ != \"\") warp .@src$, .@ex, .@ey;",
        "\t\t\t\tend;",
        "\t\t\t}",
        "\t\t}",
        "\t\t.@im$ = instance_mapname(.@src$, .@id);",
        "\t\tif (.@im$ == \"\") { // the map could not be made",
        "\t\t\tinstance_destroy .@id;",
        "\t\t\tif (.@src$ != \"\") warp .@src$, .@ex, .@ey;",
        "\t\t\tend;",
        "\t\t}",
        "\t\t// a logout, a crash or a lost connection inside saves the character where it came from, never in the copy",
        "\t\tsetmapflagnosave .@im$, LOKITUT_M$, LOKITUT_X, LOKITUT_Y;",
        "\t\tsetmapflag .@im$, mf_nomemo;",
        "\t\tsetmapflag .@im$, mf_noteleport;",
        "\t\tsetmapflag .@im$, mf_nowarpto;",
        "\t\tsetmapflag .@im$, mf_nobranch;",
        "\t\tinstance_enter(.@name$, -1, -1, getcharid(0), .@id);",
        "\t\tend;",
        "\t}",
        "\tif (@whispervar0$ == \"end\" || @whispervar0$ == \"leave\") {",
        "\t\tif (.@inside || (LOKITUT_M$ != \"\" && .@src$ != \"\" && .@m$ == .@src$)) {",
        "\t\t\tif (LOKITUT_M$ == \"\")",
        "\t\t\t\twarp \"SavePoint\", 0, 0;",
        "\t\t\telse",
        "\t\t\t\twarp LOKITUT_M$, LOKITUT_X, LOKITUT_Y;",
        "\t\t}",
        "\t\tif (.@id > 0)",
        "\t\t\tinstance_destroy .@id;",
        "\t\tLOKITUT_M$ = \"\"; LOKITUT_X = 0; LOKITUT_Y = 0;",
        "\t}",
        "\tend;",
        "}",
        "",
    });

    /// <summary>True when the copy's conf/maps_athena.conf loads <paramref name="map"/> (an active "map: name" line).</summary>
    public static bool MapLoaded(string hostDir, string map)
    {
        string f = Path.Combine(hostDir, "conf", "maps_athena.conf");
        return File.Exists(f) && File.ReadLines(f).Any(l => Regex.IsMatch(l, @"^\s*map\s*:\s*" + Regex.Escape(map) + @"\s*(//.*)?$"));
    }

    /// <summary>
    /// Writes the three files into the copy (each only when its text changes) and says what it did. An empty setting, a setting that
    /// is not "map,x,y" or a map the copy does not load: the entry is taken out and the script's line commented out (the tutorial
    /// then runs where the character stands). A copy without its instance database or its custom script list (not set up yet) is
    /// left alone.
    /// </summary>
    public static List<string> Write(string hostDir, string? setting)
    {
        var said = new List<string>();
        string db = Path.Combine(hostDir, "db", "import", "instance_db.yml"), main = Path.Combine(hostDir, "db", "instance_db.yml");
        string conf = Path.Combine(hostDir, "npc", "scripts_custom.conf"), script = Path.Combine(hostDir, "npc", "custom", ScriptFile);
        if (!File.Exists(main) || !File.Exists(conf)) return said;
        var map = Parse(setting);
        if (map == null && !string.IsNullOrWhiteSpace(setting)) said.Add($"The tutorial's map \"{setting}\" is not \"map,x,y\": no private tutorial map.");
        if (map is { } m && !MapLoaded(hostDir, m.Map)) { said.Add($"The tutorial's map \"{m.Map}\" is not in conf/maps_athena.conf: no private tutorial map."); map = null; }

        string oldDb = File.Exists(db) ? File.ReadAllText(db) : "";
        if (map != null || oldDb.Contains(Begin))
        {
            int version = YamlBlock.HeaderVersion(db); if (version == 0) version = YamlBlock.HeaderVersion(main);
            string text = ApplyDb(oldDb, map, version);
            if (text.Replace("\r\n", "\n") != oldDb.Replace("\r\n", "\n")) { Directory.CreateDirectory(Path.GetDirectoryName(db)!); File.WriteAllText(db, text); }
        }
        string oldConf = File.ReadAllText(conf);
        if (map != null || oldConf.Contains(NpcLine))
        {
            string text = ApplyScripts(oldConf, map != null);
            if (text.Replace("\r\n", "\n") != oldConf.Replace("\r\n", "\n")) File.WriteAllText(conf, text);
        }
        if (map != null)
        {
            string text = Script();
            if (!File.Exists(script) || File.ReadAllText(script).Replace("\r\n", "\n") != text) { Directory.CreateDirectory(Path.GetDirectoryName(script)!); File.WriteAllText(script, text); }
            said.Add($"The tutorial's private map: {map.Value.Map} at {map.Value.X},{map.Value.Y} (one copy per character, no NPCs).");
        }
        return said;
    }
}
