using LokiHost.Core;
using Xunit;

namespace LokiHost.Tests;

/// <summary>Client wave 21 (w21map), the design's point 47: what Loki Host writes into its rAthena copy for the controls tutorial's private
/// map (Core/TutorialInstance.cs): the instance_db entry in its block, the NPC script and its line, and the setting that is the ONE
/// place the map stands in. (That rAthena reads these files and does what they say is the client smoke w21map's part.)</summary>
public class TutorialInstanceTests
{
    /// <summary>The import file as rAthena ships it (db/import-tmpl/instance_db.yml): comments and a header, no Body.</summary>
    const string Template = "# This file is a part of rAthena.\n#\n###########\n# Instance Database\n###########\n\nHeader:\n  Type: INSTANCE_DB\n  Version: 2\n";
    const string MainDb = "Header:\n  Type: INSTANCE_DB\n  Version: 2\n\nFooter:\n  Imports:\n  - Path: db/re/instance_db.yml\n    Mode: Renewal\n  - Path: db/import/instance_db.yml\n";

    static string Temp()
    {
        var d = Path.Combine(Path.GetTempPath(), "lokihost-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>A copy as the setup leaves it, as far as the tutorial's files go.</summary>
    static string Copy(string scriptsCustom = "// custom scripts\nnpc: npc/custom/other.txt\n", string? import = Template)
    {
        var d = Temp();
        Directory.CreateDirectory(Path.Combine(d, "db", "import")); Directory.CreateDirectory(Path.Combine(d, "npc", "custom")); Directory.CreateDirectory(Path.Combine(d, "conf", "import"));
        File.WriteAllText(Path.Combine(d, "db", "instance_db.yml"), MainDb);
        if (import != null) File.WriteAllText(Path.Combine(d, "db", "import", "instance_db.yml"), import);
        File.WriteAllText(Path.Combine(d, "npc", "scripts_custom.conf"), scriptsCustom);
        File.WriteAllText(Path.Combine(d, "conf", "maps_athena.conf"), "// maps\nmap: prontera\nmap: valkyrie\n//map: tha_t10\nmap: job_star // a comment\n");
        return d;
    }

    [Fact]
    public void The_setting_is_map_x_y_and_the_default_is_Default_valkyrie()
    {
        Assert.Equal("valkyrie,49,36", TutorialInstance.DefaultMap);
        Assert.Equal(TutorialInstance.DefaultMap, new HostSettings().TutorialMap); // a settings file from before the line gets it
        Assert.Equal(("valkyrie", 49, 36), TutorialInstance.Parse(TutorialInstance.DefaultMap));
        Assert.Equal(("1@tower", 50, 355), TutorialInstance.Parse(" 1@tower , 50 ,355 "));
        foreach (var bad in new[] { "", "  ", null, "valkyrie", "valkyrie,49", "valkyrie,0,36", "valkyrie,49,0", "valkyrie,-1,36", "val kyrie,1,1", "valkyrie,49,36,7", "a_map_name_too_long,1,1", "valkyrie;1;1", "x,1,1\n  - Id: 1" })
            Assert.Null(TutorialInstance.Parse(bad));
    }

    [Fact]
    public void The_entry_is_a_private_copy_without_NPCs_and_with_rAthenas_own_cleanup()
    {
        string t = TutorialInstance.ApplyDb(Template, ("valkyrie", 49, 36), 2);
        Assert.StartsWith(Template, t); // nothing of the file changed
        Assert.Equal(new[]
        {
            "", TutorialInstance.Begin, "Body:",
            "  - Id: 9047", "    Name: Loki Tutorial", "    TimeLimit: 7200", "    IdleTimeOut: 60", "    NoNpc: true", "    Destroyable: false",
            "    Enter:", "      Map: valkyrie", "      X: 49", "      Y: 36",
            TutorialInstance.End, "",
        }, t[Template.Length..].Split('\n'));
        // NoMapFlag crashes this rAthena (a copy in a new map slot has no flag list: TutorialInstance's summary): never written
        Assert.DoesNotContain("NoMapFlag", t);
        // again: the same text; another map replaces the entry (never two); none takes the block out and leaves rAthena's file
        Assert.Equal(t, TutorialInstance.ApplyDb(t, ("valkyrie", 49, 36), 2));
        string other = TutorialInstance.ApplyDb(t, ("tha_t10", 152, 132), 2);
        Assert.Contains("      Map: tha_t10\n      X: 152\n      Y: 132\n", other); Assert.DoesNotContain("valkyrie", other);
        Assert.Single(other.Split('\n'), l => l.Trim() == "- Id: 9047");
        Assert.Equal(Template, TutorialInstance.ApplyDb(t, null, 2));
        // a file with its own Body keeps its entries; a missing file gets the database's header
        string own = "Header:\n  Type: INSTANCE_DB\n  Version: 2\n\nBody:\n  - Id: 500\n    Name: Mine\n    Enter:\n      Map: prontera\n      X: 1\n      Y: 1\n";
        string withOwn = TutorialInstance.ApplyDb(own, ("valkyrie", 49, 36), 2);
        Assert.Contains("Body:\n" + TutorialInstance.Begin + "\n  - Id: 9047\n", withOwn); Assert.EndsWith("  - Id: 500\n    Name: Mine\n    Enter:\n      Map: prontera\n      X: 1\n      Y: 1\n", withOwn);
        Assert.Equal(own, TutorialInstance.ApplyDb(withOwn, null, 2));
        Assert.StartsWith("Header:\n  Type: INSTANCE_DB\n  Version: 2\n\n" + TutorialInstance.Begin + "\nBody:\n  - Id: 9047\n", TutorialInstance.ApplyDb("", ("valkyrie", 49, 36), 2));
    }

    [Fact]
    public void The_script_is_one_NPC_that_answers_the_clients_whisper()
    {
        string s = TutorialInstance.Script();
        Assert.Contains("-\tscript\tLokiTutorial\t-1,{", s); // no body on any map: nothing of it is ever seen
        Assert.Contains("OnWhisperGlobal:", s);
        // the instance: per character, by the name instance_db has; the entry cell is instance_db's (-1, -1)
        Assert.Contains(".@name$ = \"Loki Tutorial\";", s);
        Assert.Contains("instance_create(.@name$, IM_CHAR)", s);
        Assert.Contains("instance_enter(.@name$, -1, -1, getcharid(0), .@id)", s);
        Assert.DoesNotContain("valkyrie", s); // the map stands in the setting only
        // never stuck in a dead copy: the copy's return point is where the character came from; the end warps back and destroys
        Assert.Contains("setmapflagnosave .@im$, LOKITUT_M$, LOKITUT_X, LOKITUT_Y;", s);
        Assert.Contains("warp LOKITUT_M$, LOKITUT_X, LOKITUT_Y;", s);
        Assert.Contains("instance_destroy .@id;", s);
        // every "{" closed, tabs for indentation, Unix line ends (the copy may be read by a Linux server)
        Assert.Equal(s.Count(c => c == '{'), s.Count(c => c == '}'));
        Assert.DoesNotContain("\r", s);
        Assert.EndsWith("}\n", s);
    }

    [Fact]
    public void Write_puts_the_three_files_in_the_copy_and_changes_nothing_the_second_time()
    {
        string d = Copy();
        string db = Path.Combine(d, "db", "import", "instance_db.yml"), conf = Path.Combine(d, "npc", "scripts_custom.conf"), script = Path.Combine(d, "npc", "custom", "loki_tutorial.txt");
        var said = TutorialInstance.Write(d, "valkyrie,49,36");
        Assert.Equal(new[] { "The tutorial's private map: valkyrie at 49,36 (one copy per character, no NPCs)." }, said);
        Assert.Equal(TutorialInstance.ApplyDb(Template, ("valkyrie", 49, 36), 2), File.ReadAllText(db));
        Assert.Equal("// custom scripts\nnpc: npc/custom/other.txt\nnpc: npc/custom/loki_tutorial.txt\n", File.ReadAllText(conf));
        Assert.Equal(TutorialInstance.Script(), File.ReadAllText(script));
        // the second start: not a byte written
        var t0 = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var f in new[] { db, conf, script }) File.SetLastWriteTimeUtc(f, t0);
        TutorialInstance.Write(d, "valkyrie,49,36");
        foreach (var f in new[] { db, conf, script }) Assert.Equal(t0, File.GetLastWriteTimeUtc(f));
        // a script someone edited is put back; another map: only the entry changes
        File.AppendAllText(script, "// edited\n");
        TutorialInstance.Write(d, "job_star,165,23");
        Assert.Equal(TutorialInstance.Script(), File.ReadAllText(script));
        Assert.Contains("      Map: job_star\n      X: 165\n      Y: 23\n", File.ReadAllText(db));
        Assert.Single(File.ReadAllLines(conf), l => l == TutorialInstance.NpcLine);
        // the test settings' NPC switch-off (RathenaCopy.RemoveTestSettings) leaves our line alone
        Assert.Equal(0, Conf.DisableTestNpcs(conf));
    }

    [Fact]
    public void No_setting_or_a_map_the_server_does_not_load_means_no_private_map()
    {
        string d = Copy();
        string db = Path.Combine(d, "db", "import", "instance_db.yml"), conf = Path.Combine(d, "npc", "scripts_custom.conf");
        // never switched on: an empty setting touches nothing
        Assert.Empty(TutorialInstance.Write(d, ""));
        Assert.Equal(Template, File.ReadAllText(db));
        Assert.Equal("// custom scripts\nnpc: npc/custom/other.txt\n", File.ReadAllText(conf));
        // on, then off: the entry out, the line commented out (rAthena's file and the other lines as they were)
        TutorialInstance.Write(d, "valkyrie,49,36");
        Assert.Empty(TutorialInstance.Write(d, ""));
        Assert.Equal(Template, File.ReadAllText(db));
        Assert.Equal("// custom scripts\nnpc: npc/custom/other.txt\n// (Loki Host: no tutorial map) npc: npc/custom/loki_tutorial.txt\n", File.ReadAllText(conf));
        // on again: the same line, once
        TutorialInstance.Write(d, "valkyrie,49,36");
        Assert.Equal("// custom scripts\nnpc: npc/custom/other.txt\nnpc: npc/custom/loki_tutorial.txt\n", File.ReadAllText(conf));
        // a map the copy does not load (commented out in maps_athena.conf, or unknown), or a setting that is no "map,x,y": said, and off
        Assert.Equal(new[] { "The tutorial's map \"tha_t10\" is not in conf/maps_athena.conf: no private tutorial map." }, TutorialInstance.Write(d, "tha_t10,152,132"));
        Assert.Equal(Template, File.ReadAllText(db));
        Assert.DoesNotContain(File.ReadAllLines(conf), l => l == TutorialInstance.NpcLine);
        Assert.Equal(new[] { "The tutorial's map \"valkyrie\" is not \"map,x,y\": no private tutorial map." }, TutorialInstance.Write(d, "valkyrie"));
        Assert.True(TutorialInstance.MapLoaded(d, "job_star")); Assert.False(TutorialInstance.MapLoaded(d, "job"));
        // a folder that is not a set-up copy (no instance database): nothing is made
        string empty = Temp();
        Assert.Empty(TutorialInstance.Write(empty, "valkyrie,49,36"));
        Assert.False(Directory.Exists(Path.Combine(empty, "npc")));
    }

    [Fact]
    public void ApplyConf_writes_the_tutorials_map_at_every_start()
    {
        string d = Copy(import: null); // a copy whose db/import has no instance_db.yml yet
        var said = new List<string>();
        HostSetup.ApplyConf(new HostSettings { HostDir = d }, "203.0.113.7", "192.168.1.20", "255.255.255.0", said.Add);
        Assert.Contains(said, l => l.StartsWith("The tutorial's private map: valkyrie at 49,36"));
        string db = File.ReadAllText(Path.Combine(d, "db", "import", "instance_db.yml"));
        Assert.StartsWith("Header:\n  Type: INSTANCE_DB\n  Version: 2\n", db); // the header of the copy's own db/instance_db.yml
        Assert.Contains("      Map: valkyrie\n", db);
        Assert.True(File.Exists(Path.Combine(d, "npc", "custom", "loki_tutorial.txt")));
        // the host's setting off: gone at the next start
        HostSetup.ApplyConf(new HostSettings { HostDir = d, TutorialMap = "" }, "203.0.113.7", "192.168.1.20", "255.255.255.0");
        Assert.DoesNotContain("Loki Tutorial", File.ReadAllText(Path.Combine(d, "db", "import", "instance_db.yml")));
        Assert.DoesNotContain(File.ReadAllLines(Path.Combine(d, "npc", "scripts_custom.conf")), l => l == TutorialInstance.NpcLine);
    }
}
