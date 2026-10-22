using System;
using System.Collections.Generic;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>What the skill tree's hover diorama plays for a skill (client wave 22, point 64): derived from rAthena's skill database by
    /// client/tools/w22/build_skill_tags.py (the rule of each kind is in the table's header).</summary>
    public enum SkillKind { MeleeHit, Bolt, Projectile, GroundArea, SelfArea, Buff, Debuff, Heal, Trap, Summon, Passive, Movement, Other }

    /// <summary>
    /// Client wave 22 (w22data), point 64 of the design's wave 17 list (2026-10-10: "each skill carries a few tag keywords naming what it is good
    /// at ... each TAG has its own colour ... a filter toggle per tag"): the tags, each skill's 1-3 tags (the most characteristic first)
    /// and each skill's kind.
    ///   The data   Resources/Skills/skill_tags.txt, made by client/tools/w22/build_skill_tags.py from rAthena's own files (the oracle's
    ///              e985006: db/re/skill_db.yml, skill_tree.yml, status.yml and src/map: each skill's implementation class, battle.cpp,
    ///              status.cpp). Every rule is in the tool and, in short, in the table's header; every row says which rule gave each
    ///              tag. Nothing comes from the game client's texts, and nothing is sent to or asked from the server.
    ///   The tags   1-14 are the design's list; Buff, Debuff, Support and Utility are the orchestrator's additions in the notes' row 64 ("the
    ///              list to settle"); Single Target CC is w22data's (FLAGGED: a stun / freeze / root on ONE target fits none of the
    ///              others; drop it in the tool's tags_of() and those skills fall back to their next tag).
    ///   The names  <see cref="Tag.NameKey"/> is a Lang key (the English text; pt-BR in Resources/Lang/pt-BR.txt): show it with Lang.T.
    ///   The colours <see cref="Tag.Color"/>: FLAGGED, see design notes (a set chosen by a script for colour-blind players,
    ///              the method of wave 20's UI/RowColours.cs).
    /// The interface is the one the 3D skill tree calls (its stub of 2026-10-10 23:27): <see cref="All"/>, <see cref="For"/>,
    /// <see cref="Tag"/>'s three fields, and <see cref="SkillQuests"/>; plus <see cref="KindOf"/>.
    /// </summary>
    public static class SkillTags
    {
        public sealed class Tag
        {
            /// <summary>The tag's id (never changes: saved filters may hold it), its name's Lang key, its colour.</summary>
            public string Id = "", NameKey = "";
            public Color Color = Color.white;
            /// <summary>The tag's number in skill_tags.txt (T rows).</summary>
            public int Number;
        }

        // The filter buttons' order: the design's list as written (Single Target CC beside Area CC), then the additions.
        // Two NAMES changed on 2026-10-11 (design note: "Rename consistent to sustained. Rename reliable to something else as well."):
        // "Sustained" and "Sure Hit" (the orchestrator's choice, FLAGGED). Their ids and numbers stay: only the NameKey here, the
        // tool's TAG_NAMES line and the pt-BR line change with a name.
        // The colours (FLAGGED: default them) are client/tools/w22/pick_tag_colours.py's result: Okabe & Ito's eight (as
        // wave 20's UI/RowColours.cs) plus eleven picked from an sRGB grid, each the farthest from the set by the SMALLEST CIEDE2000
        // distance over normal vision and simulated protanopia / deuteranopia / tritanopia (Machado et al. 2009, severity 1); five
        // tags keep the colour a player expects (Area Damage vermillion, Heal green, Shield blue, Mobility sky blue, Buff yellow) and
        // the others are assigned so the tags that share a skill are the easiest to tell apart: of the 108 tag pairs that share a
        // skill in the table the closest is 10.2 apart (Sustained / Buff, protanopia); the closest pair of the whole set is 6.2
        // (Elemental Resist / Single Target CC, deuteranopia), two tags that are never on one skill. A tag's name is always written
        // beside its colour: nineteen colours cannot all stay far apart for every kind of colour vision.
        private static readonly List<Tag> _all = new List<Tag>
        {
            new Tag { Number = 1,  Id = "area",       NameKey = Lang.N("Area Damage"),         Color = new Color32(0xD5, 0x5E, 0x00, 255) },
            new Tag { Number = 2,  Id = "cc",         NameKey = Lang.N("Area CC"),             Color = new Color32(0x55, 0x77, 0x55, 255) },
            new Tag { Number = 19, Id = "cc1",        NameKey = Lang.N("Single Target CC"),    Color = new Color32(0xCC, 0x79, 0xA7, 255) },
            new Tag { Number = 3,  Id = "burst",      NameKey = Lang.N("Single Target Burst"), Color = new Color32(0x99, 0x99, 0x55, 255) },
            new Tag { Number = 4,  Id = "dot",        NameKey = Lang.N("DoT"),                 Color = new Color32(0xFF, 0xBB, 0xDD, 255) },
            new Tag { Number = 5,  Id = "consistent", NameKey = Lang.N("Sustained"),           Color = new Color32(0x55, 0xFF, 0x99, 255) },
            new Tag { Number = 6,  Id = "reliable",   NameKey = Lang.N("Sure Hit"),            Color = new Color32(0xBB, 0x33, 0x99, 255) },
            new Tag { Number = 7,  Id = "allround",   NameKey = Lang.N("All-Rounder"),         Color = new Color32(0x99, 0xBB, 0x99, 255) },
            new Tag { Number = 8,  Id = "specialist", NameKey = Lang.N("Specialist"),          Color = new Color32(0x77, 0x77, 0xFF, 255) },
            new Tag { Number = 9,  Id = "heal",       NameKey = Lang.N("Heal"),                Color = new Color32(0x00, 0x9E, 0x73, 255) },
            new Tag { Number = 10, Id = "shield",     NameKey = Lang.N("Shield"),              Color = new Color32(0x00, 0x72, 0xB2, 255) },
            new Tag { Number = 11, Id = "survive",    NameKey = Lang.N("Survivability"),       Color = new Color32(0x99, 0x55, 0x77, 255) },
            new Tag { Number = 12, Id = "eleresist",  NameKey = Lang.N("Elemental Resist"),    Color = new Color32(0x99, 0x99, 0x99, 255) },
            new Tag { Number = 13, Id = "mobility",   NameKey = Lang.N("Mobility"),            Color = new Color32(0x56, 0xB4, 0xE9, 255) },
            new Tag { Number = 14, Id = "evasion",    NameKey = Lang.N("Evasion"),             Color = new Color32(0xE6, 0x9F, 0x00, 255) },
            new Tag { Number = 15, Id = "buff",       NameKey = Lang.N("Buff"),                Color = new Color32(0xF0, 0xE4, 0x42, 255) },
            new Tag { Number = 16, Id = "debuff",     NameKey = Lang.N("Debuff"),              Color = new Color32(0xBB, 0xDD, 0xBB, 255) },
            new Tag { Number = 17, Id = "support",    NameKey = Lang.N("Support"),             Color = new Color32(0xBB, 0xBB, 0x77, 255) },
            new Tag { Number = 18, Id = "utility",    NameKey = Lang.N("Utility"),             Color = new Color32(0xFF, 0x99, 0xBB, 255) },
        };

        private sealed class Entry { public string[] Tags = None; public SkillKind Kind = SkillKind.Other; public string Why = ""; }
        private static readonly string[] None = new string[0];
        private static Dictionary<int, Entry> _of;
        private static Dictionary<string, Tag> _byId;
        private static int _bad;

        /// <summary>Every tag, in the order the filter buttons show them.</summary>
        public static IReadOnlyList<Tag> All => _all;

        /// <summary>A skill's tag ids, the most characteristic first (1-3 for a skill of the table; an unknown skill: an empty list).</summary>
        public static IReadOnlyList<string> For(int skillId) { Load(); return _of.TryGetValue(skillId, out var e) ? e.Tags : None; }

        /// <summary>What the hover diorama plays for a skill (an unknown skill: Other).</summary>
        public static SkillKind KindOf(int skillId) { Load(); return _of.TryGetValue(skillId, out var e) ? e.Kind : SkillKind.Other; }

        /// <summary>The tag with this id, or null.</summary>
        public static Tag Get(string id) { Load(); return id != null && _byId.TryGetValue(id, out var t) ? t : null; }

        /// <summary>Whether the table has this skill.</summary>
        public static bool Has(int skillId) { Load(); return _of.ContainsKey(skillId); }

        /// <summary>The table's own words for why a skill has its tags (English, for the log and for reading the table; not shown to players).</summary>
        public static string WhyOf(int skillId) { Load(); return _of.TryGetValue(skillId, out var e) ? e.Why : ""; }

        /// <summary>Skills in the table, and rows the loader could not read (0 on a good table).</summary>
        public static int Count { get { Load(); return _of.Count; } }
        public static int BadRows { get { Load(); return _bad; } }
        /// <summary>Every skill id of the table.</summary>
        public static IEnumerable<int> Skills { get { Load(); return _of.Keys; } }

        private static void Load()
        {
            if (_of != null) return;
            var of = new Dictionary<int, Entry>();
            _byId = new Dictionary<string, Tag>(StringComparer.Ordinal);
            var byNumber = new Dictionary<int, Tag>();
            foreach (var t in _all) { _byId[t.Id] = t; byNumber[t.Number] = t; }
            _bad = 0;
            var ta = Resources.Load<TextAsset>("Skills/skill_tags");
            if (ta == null) { Debug.LogWarning("[SkillTags] Resources/Skills/skill_tags is missing: no skill has a tag"); _of = of; return; }
            int tagRows = 0;
            foreach (var raw in ta.text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                var c = line.Split('\t');
                if (c[0] == "T" && c.Length >= 3)
                {
                    tagRows++;
                    if (!int.TryParse(c[1], out int n) || !byNumber.ContainsKey(n)) { _bad++; Debug.LogWarning("[SkillTags] the table's tag " + c[1] + " (" + c[2] + ") is not a tag of the client"); }
                    continue;
                }
                if (c[0] != "S" || c.Length < 5 || !int.TryParse(c[1], out int id) || !Enum.TryParse(c[3], out SkillKind kind)) { _bad++; continue; }
                var tags = new List<string>(3);
                foreach (var p in c[4].Split(','))
                    if (int.TryParse(p, out int n) && byNumber.TryGetValue(n, out var t)) { if (!tags.Contains(t.Id)) tags.Add(t.Id); }
                    else _bad++;
                of[id] = new Entry { Tags = tags.ToArray(), Kind = kind, Why = c.Length > 5 ? c[5] : "" };
            }
            _of = of;
            Debug.Log($"[SkillTags] {of.Count} skills, {tagRows} tags in the table ({_all.Count} in the client){(_bad > 0 ? $", {_bad} unreadable rows / tag ids" : "")}");
        }
    }
}
