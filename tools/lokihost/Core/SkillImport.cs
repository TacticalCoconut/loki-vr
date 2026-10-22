using System.Text.RegularExpressions;

namespace LokiHost.Core;

/// <summary>A skill whose cast range the host changes: rAthena's skill id, its name (for the reader of the file) and the range in cells.</summary>
public sealed record SkillRange(int Id, string Name, int Range);

/// <summary>
/// The host's skill changes in the copy's db/import/skill_db.yml. rAthena reads that file after db/re/skill_db.yml, and an
/// entry there for a skill that already exists replaces only the fields it names (skill.cpp SkillDatabase::parseBodyNode,
/// :14781-14790: Name / Description / MaxLevel are required only for a NEW id; :14931-14933: "Range" is parsed over the
/// existing one). So one entry "Id + Range" changes a skill's cast range and nothing else. The client is told the range by
/// the server's skill list, so it needs no change; `@reloadskilldb` re-reads the file while the server runs
/// (atcommand.cpp:4381 -> skill.cpp skill_reload, :16289-16296).
/// Loki Host owns ONE block of that file, between two marker lines, and rewrites it at every start
/// (<see cref="HostSetup.ApplyConf"/>): whatever else the file holds (the build's own entries) stays as it is, and entries
/// that come after the block in the file win over it, as in any rAthena import.
/// </summary>
public static class SkillImport
{
    public const string Begin = "# >>> Loki Host: skill ranges (the host's setting SkillRanges; this block is rewritten at every start) >>>";
    public const string End = "# <<< Loki Host <<<";

    public static string ImportPath(string hostDir) => Path.Combine(hostDir, "db", "import", "skill_db.yml");

    /// <summary>The file's text with the host's block replaced by one for <paramref name="ranges"/> (none: the block is taken
    /// out). <paramref name="headerVersion"/> is only used when the text has no header at all (a missing or empty file).</summary>
    public static string Apply(string text, IReadOnlyList<SkillRange> ranges, int headerVersion)
    {
        // (client wave 21, w21map: the block's handling is YamlBlock's now, shared with the tutorial's instance; the text written is the same)
        var entries = ranges.Where(r => r.Id > 0 && r.Range > 0).Select(r => (IReadOnlyList<string>)new[]
        {
            $"- Id: {r.Id}" + (string.IsNullOrWhiteSpace(r.Name) ? "" : $"   # {Regex.Replace(r.Name, @"[^A-Za-z0-9_ ]", "")}"),
            $"  Range: {r.Range}",
        }).ToList();
        return YamlBlock.Apply(text, Begin, End, entries, "SKILL_DB", headerVersion);
    }

    /// <summary>The "Version:" of a rAthena YAML database's header (0: not found).</summary>
    public static int HeaderVersion(string file) => YamlBlock.HeaderVersion(file);

    /// <summary>Writes the host's block into the copy's db/import/skill_db.yml (only when the text changes). A copy without
    /// its skill database (not set up yet) is left alone.</summary>
    public static void Write(string hostDir, IReadOnlyList<SkillRange> ranges)
    {
        string file = ImportPath(hostDir);
        string main = Path.Combine(hostDir, "db", "re", "skill_db.yml");
        if (!File.Exists(file) && !File.Exists(main)) return;
        string old = File.Exists(file) ? File.ReadAllText(file) : "";
        if (!ranges.Any(r => r.Id > 0 && r.Range > 0) && !old.Contains(Begin)) return; // nothing of ours in it and nothing to put
        int version = HeaderVersion(file); if (version == 0) version = HeaderVersion(main);
        string text = Apply(old, ranges, version);
        if (text.Replace("\r\n", "\n") == old.Replace("\r\n", "\n")) return;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }
}
