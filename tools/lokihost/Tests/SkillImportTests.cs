using LokiHost.Core;
using Xunit;

namespace LokiHost.Tests;

/// <summary>The host's block in db/import/skill_db.yml (Core/SkillImport.cs): the design's "increase the range of steal to two", 2026-10-06.</summary>
public class SkillImportTests
{
    static readonly SkillRange[] Steal = { new(50, "TF_STEAL", 2) };

    /// <summary>The import file as rAthena ships it (db/import-tmpl/skill_db.yml): comments and a header, no Body.</summary>
    const string Template = "# This file is a part of rAthena.\n#\n###########\n# Skill Database\n###########\n\nHeader:\n  Type: SKILL_DB\n  Version: 4\n";

    static string Temp()
    {
        var d = Path.Combine(Path.GetTempPath(), "lokihost-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void A_template_file_gets_the_block_with_its_own_Body()
    {
        string t = SkillImport.Apply(Template, Steal, 4);
        Assert.StartsWith(Template, t); // nothing of the file changed
        string[] tail = t[Template.Length..].Split('\n');
        Assert.Equal(new[] { "", SkillImport.Begin, "Body:", "  - Id: 50   # TF_STEAL", "    Range: 2", SkillImport.End, "" }, tail);
        // again: the same text (the block is replaced, never added twice)
        Assert.Equal(t, SkillImport.Apply(t, Steal, 4));
        Assert.Single(t.Split('\n'), l => l == SkillImport.Begin);
        // another value replaces it; an empty list takes the block out and leaves the file as rAthena shipped it
        Assert.Contains("    Range: 3", SkillImport.Apply(t, new[] { new SkillRange(50, "TF_STEAL", 3) }, 4));
        Assert.DoesNotContain("Range: 2", SkillImport.Apply(t, new[] { new SkillRange(50, "TF_STEAL", 3) }, 4));
        Assert.Equal(Template, SkillImport.Apply(t, Array.Empty<SkillRange>(), 4));
    }

    [Fact]
    public void A_file_with_its_own_Body_keeps_its_entries_and_takes_ours_first()
    {
        string own = "Header:\n  Type: SKILL_DB\n  Version: 4\n\nBody:\n  - Id: 5\n    Name: SM_BASH\n    Range: 2\n";
        string t = SkillImport.Apply(own, Steal, 4);
        Assert.Equal("Header:\n  Type: SKILL_DB\n  Version: 4\n\nBody:\n" + SkillImport.Begin + "\n  - Id: 50   # TF_STEAL\n    Range: 2\n" + SkillImport.End
            + "\n  - Id: 5\n    Name: SM_BASH\n    Range: 2\n", t);
        Assert.Equal(t, SkillImport.Apply(t, Steal, 4));
        Assert.Equal(own, SkillImport.Apply(t, Array.Empty<SkillRange>(), 4));
        // the file's own indentation is followed
        string four = "Header:\n  Type: SKILL_DB\n  Version: 4\nBody:\n    - Id: 5\n      Range: 2\n";
        Assert.Contains("\n    - Id: 50   # TF_STEAL\n      Range: 2\n", SkillImport.Apply(four, Steal, 4));
        // Windows line ends are read; what is not a usable entry is left out; a name cannot break the comment
        Assert.Equal(SkillImport.Apply(Template, Steal, 4), SkillImport.Apply(Template.Replace("\n", "\r\n"), Steal, 4));
        string odd = SkillImport.Apply(Template, new[] { new SkillRange(0, "X", 2), new SkillRange(51, "TF_HIDING", 0), new SkillRange(50, "TF: {STEAL}\n- Id: 1", 2) }, 4);
        Assert.Contains("  - Id: 50   # TF STEAL Id 1\n    Range: 2\n", odd);
        Assert.DoesNotContain("Id: 51", odd); Assert.DoesNotContain("Id: 0", odd);
    }

    [Fact]
    public void An_empty_or_missing_file_gets_a_header_with_the_databases_version()
    {
        Assert.Equal("Header:\n  Type: SKILL_DB\n  Version: 7\n\n" + SkillImport.Begin + "\nBody:\n  - Id: 50   # TF_STEAL\n    Range: 2\n" + SkillImport.End + "\n",
            SkillImport.Apply("", Steal, 7));
        string d = Temp();
        // a copy that is not set up (no skill database at all): nothing is written
        SkillImport.Write(d, Steal);
        Assert.False(File.Exists(SkillImport.ImportPath(d)));
        // the database is there, the import file is not: it is made with the database's header version
        Directory.CreateDirectory(Path.Combine(d, "db", "re"));
        File.WriteAllText(Path.Combine(d, "db", "re", "skill_db.yml"), "# comment\nHeader:\n  Type: SKILL_DB\n  Version: 4\n\nBody:\n  - Id: 50\n    Name: TF_STEAL\n    Range: 1\n");
        Assert.Equal(4, SkillImport.HeaderVersion(Path.Combine(d, "db", "re", "skill_db.yml")));
        SkillImport.Write(d, Steal);
        string f = SkillImport.ImportPath(d);
        Assert.Contains("  Version: 4\n", File.ReadAllText(f));
        Assert.Contains("  - Id: 50   # TF_STEAL\n    Range: 2\n", File.ReadAllText(f));
        // a second start writes nothing (the file's time stays)
        var when = File.GetLastWriteTimeUtc(f);
        File.SetLastWriteTimeUtc(f, when.AddDays(-1));
        SkillImport.Write(d, Steal);
        Assert.Equal(when.AddDays(-1), File.GetLastWriteTimeUtc(f));
        // a file with nothing of ours and nothing to put is not touched at all (not even its line ends)
        File.WriteAllText(f, "Header:\r\n  Type: SKILL_DB\r\n  Version: 4");
        SkillImport.Write(d, Array.Empty<SkillRange>());
        Assert.Equal("Header:\r\n  Type: SKILL_DB\r\n  Version: 4", File.ReadAllText(f));
    }

    [Fact]
    public void The_setting_defaults_to_Steal_2_and_ApplyConf_writes_it()
    {
        var s = new HostSettings();
        Assert.Equal(new[] { new SkillRange(50, "TF_STEAL", 2) }, s.SkillRanges);
        // a settings file from before the line existed gets the default; an empty list in the file stays empty
        string f = Path.Combine(Temp(), "lokihost.json");
        File.WriteAllText(f, "{ \"ViewRange\": 60, \"SettingsVersion\": 2 }");
        Assert.Equal(new[] { new SkillRange(50, "TF_STEAL", 2) }, HostSettings.Load(f).SkillRanges);
        File.WriteAllText(f, "{ \"SettingsVersion\": 2, \"SkillRanges\": [] }");
        Assert.Empty(HostSettings.Load(f).SkillRanges);
        // it survives a save and a load
        var h = HostSettings.Load(f); h.SkillRanges.Add(new SkillRange(51, "TF_HIDING", 3)); h.Save();
        Assert.Equal(new[] { new SkillRange(51, "TF_HIDING", 3) }, HostSettings.Load(f).SkillRanges);

        // the start's configuration step writes the block into the copy
        string d = Temp();
        Directory.CreateDirectory(Path.Combine(d, "conf", "import"));
        Directory.CreateDirectory(Path.Combine(d, "db", "import"));
        File.WriteAllText(SkillImport.ImportPath(d), Template);
        HostSetup.ApplyConf(new HostSettings { HostDir = d }, "203.0.113.7", "192.168.1.20", "255.255.255.0");
        Assert.Contains("\nBody:\n  - Id: 50   # TF_STEAL\n    Range: 2\n", File.ReadAllText(SkillImport.ImportPath(d)));
    }
}
