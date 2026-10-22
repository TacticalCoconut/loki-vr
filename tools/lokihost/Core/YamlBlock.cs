using System.Text.RegularExpressions;

namespace LokiHost.Core;

/// <summary>
/// ONE block of entries that Loki Host owns in one of the copy's rAthena YAML import files (db/import/*.yml), between two marker
/// lines, rewritten at every start: whatever else the file holds (the build's own entries) stays as it is, and entries that come
/// after the block in the file win over it, as in any rAthena import. First written for the skill ranges (<see cref="SkillImport"/>,
/// 2026-10-06); client wave 21 (w21map) made it a helper of its own for the tutorial's instance (<see cref="TutorialInstance"/>).
/// </summary>
public static class YamlBlock
{
    /// <summary>The file's text with the block between <paramref name="begin"/> and <paramref name="end"/> replaced by one holding
    /// <paramref name="entries"/> (none: the block is taken out). An entry is its lines without indentation, the first one starting
    /// with "- ". <paramref name="headerType"/> / <paramref name="headerVersion"/> are only used when the text has no header at all (a
    /// missing or empty file).</summary>
    public static string Apply(string text, string begin, string end, IReadOnlyList<IReadOnlyList<string>> entries, string headerType, int headerVersion)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        // the old block out (both markers and everything between them)
        int b = lines.FindIndex(l => l.TrimEnd() == begin);
        if (b >= 0)
        {
            int e = lines.FindIndex(b, l => l.TrimEnd() == end);
            lines.RemoveRange(b, (e >= b ? e : lines.Count - 1) - b + 1);
            while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        }
        if (entries.Count > 0)
        {
            if (!lines.Any(l => Regex.IsMatch(l, @"^Header\s*:")))
            {
                var head = new List<string> { "Header:", "  Type: " + headerType, $"  Version: {headerVersion}" };
                if (lines.Count > 0) head.Add(""); // a blank line before whatever the file held
                lines.InsertRange(0, head);
            }
            var block = new List<string> { begin };
            // the file's own "Body:" (a top-level key at column 0) takes our entries as its first ones; without one the block
            // brings its own, inside the markers, so that taking the block out leaves no empty "Body:" behind
            int body = lines.FindIndex(l => Regex.IsMatch(l, @"^Body\s*:\s*(#.*)?$"));
            if (body < 0) block.Add("Body:");
            // our entries are indented as the file's own first entry is (rAthena's files use two spaces)
            string indent = "  ";
            if (body >= 0)
                for (int i = body + 1; i < lines.Count; i++)
                {
                    var m = Regex.Match(lines[i], @"^(\s*)- ");
                    if (m.Success) { indent = m.Groups[1].Value; break; }
                    if (Regex.IsMatch(lines[i], @"^\S") && !lines[i].StartsWith("#")) break; // the next top-level key: an empty Body
                }
            foreach (var entry in entries)
                foreach (var l in entry) block.Add(indent + l);
            block.Add(end);
            if (body < 0) { if (lines.Count > 0) lines.Add(""); lines.AddRange(block); }
            else lines.InsertRange(body + 1, block);
        }
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>The "Version:" of a rAthena YAML database's header (0: not found).</summary>
    public static int HeaderVersion(string file)
    {
        if (!File.Exists(file)) return 0;
        bool header = false;
        foreach (var l in File.ReadLines(file))
        {
            if (Regex.IsMatch(l, @"^Header\s*:")) { header = true; continue; }
            if (header && Regex.IsMatch(l, @"^\S")) break; // the next top-level key
            var m = header ? Regex.Match(l, @"^\s+Version\s*:\s*(\d+)") : Match.Empty;
            if (m.Success) return int.Parse(m.Groups[1].Value);
        }
        return 0;
    }
}
