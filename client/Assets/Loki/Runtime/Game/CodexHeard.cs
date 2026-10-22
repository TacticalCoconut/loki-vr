using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Wave 28 (wcodex), design note (2026-10-15): "If you ever hear an NPC talk about a specific creature during dialogue, its silhouette, name and
    /// level is revealed regardless of how much stronger than you it is." Which creatures an NPC's line (the mes text the client shows)
    /// names. THE RULE (FLAGGED; measured on the text overhaul's 577 NPC script files, 245,964 mes strings, against the 1,782 creatures a
    /// player can meet, design notes):
    ///   a creature's full name exactly as the Codex data writes it (case and all), as whole words (no letter, digit or apostrophe
    ///     touching it), the longest name first ("Baby Desert Wolf" before "Wolf");
    ///   not inside a speaker's tag ("[Bijou]": the line's header, not a mention);
    ///   not part of a longer capitalised phrase: no capitalised word right before or after it ("Wolf Claws", "Gray Wolf", "Elder
    ///     Creek", "Bongun Card" are not the creatures);
    ///   the <see cref="Generic"/> one-word names (the lines use them in lower case at least as often as as a capitalised name: "Clock",
    ///     "Step", "Monster", "Seal" ...) only in a monster list's "Name(level)" form.
    /// Naive, case-blind: 9,491 hits, 12 of 40 sampled real; case-sensitive: 6,752, 16 of 40; the rule above (before its last line):
    /// 3,154, 54 of 60 sampled real; with its last line: 2,931 hits, 459 names.
    /// A name two creatures share (Poring #1002 and the Monster Race's #1725): the one with the most map spawns, then the lowest id
    /// (FLAGGED: the context is not read).
    /// </summary>
    public static class CodexHeard
    {
        public static readonly HashSet<string> Generic = new HashSet<string>
        {
            "Alarm", "Alligator", "Bomb", "Breeze", "Caterpillar", "Clock", "Constant", "Cookie", "Coyote", "Disguise", "Dolor", "Drum", "Dustiness",
            "Explosion", "Faceworm", "Familiar", "Gig", "Injustice", "Jewel", "Leak", "Lunatic", "Magmaring", "Mastering", "Mimic", "Mineral", "Monster",
            "Mummy", "Nightmare", "Octopus", "Pest", "Petal", "Picky", "Plate", "Poisonous", "Punk", "Seal", "Seaweed", "Seeker", "Shellfish", "Skeleton",
            "Slave", "Spark", "Step", "Swordfish", "Toad", "Vocal", "Wraith", "Zombie",
        };
        private static Regex _rx; private static Dictionary<string, int> _ids;
        private static readonly Regex Colour = new Regex(@"\^[0-9A-Fa-f]{6}", RegexOptions.CultureInvariant);
        private static readonly Regex Level = new Regex(@"^\(\d+\)", RegexOptions.CultureInvariant);
        private static readonly Regex Before = new Regex(@"([A-Za-z][\w'\-]*)\s$", RegexOptions.CultureInvariant);
        private static readonly Regex After = new Regex(@"^\s[A-Z]", RegexOptions.CultureInvariant);

        /// <summary>The creatures the line names (each once), by the rule above.</summary>
        public static List<int> Find(string line)
        {
            var found = new List<int>();
            if (string.IsNullOrEmpty(line)) return found;
            Build(); if (_rx == null) return found;
            string t = Colour.Replace(line, "");
            foreach (Match m in _rx.Matches(t))
            {
                string n = m.Groups[1].Value; int s = m.Index, e = m.Index + m.Length;
                int open = t.LastIndexOf('[', s), close = t.LastIndexOf(']', s);
                if (open > close && t.IndexOf(']', s) >= 0) continue; // (a speaker's tag)
                var b = Before.Match(t.Substring(0, s));
                if ((b.Success && char.IsUpper(b.Groups[1].Value[0])) || After.IsMatch(t.Substring(e))) continue; // (inside a longer name)
                if (Generic.Contains(n) && !Level.IsMatch(t.Substring(e))) continue;
                if (_ids.TryGetValue(n, out int id) && !found.Contains(id)) found.Add(id);
            }
            return found;
        }

        private static void Build()
        {
            if (_ids != null) return;
            _ids = new Dictionary<string, int>(); var spawns = new Dictionary<int, int>();
            foreach (int id in CodexData.MetableIds)
            {
                var c = CodexData.GetCreature(id); if (c == null || c.Name.Trim().Length < 3) continue;
                int sp = 0; foreach (var h in c.Habitats) sp += h.count; spawns[id] = sp;
                string n = c.Name.Trim();
                if (!_ids.TryGetValue(n, out int had) || sp > spawns[had] || (sp == spawns[had] && id < had)) _ids[n] = id;
            }
            if (_ids.Count == 0) return;
            var names = new List<string>(_ids.Keys); names.Sort((a, b) => b.Length.CompareTo(a.Length));
            var sb = new StringBuilder(@"(?<![\w'])(");
            for (int i = 0; i < names.Count; i++) { if (i > 0) sb.Append('|'); sb.Append(Regex.Escape(names[i])); }
            sb.Append(@")(?![\w'])");
            _rx = new Regex(sb.ToString(), RegexOptions.CultureInvariant);
            Debug.Log($"[Codex] names an NPC may say: {_ids.Count}");
        }
    }
}
