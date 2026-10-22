using System;
using System.Collections.Generic;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 22 (w22tree), point 64, design note 2026-10-11: "Add to the filter the ability to filter elemental damage types (only offer
    /// the options that a given job progression can inflict)". The element(s) of the damage each skill can inflict.
    ///   The data    Resources/Skills/skill_elements.txt, made by client/tools/w22/build_skill_tags.py from rAthena's own files
    ///               (db/re/skill_db.yml's Element and what src/map/battle.cpp, status.cpp and the skills' classes do with it): every
    ///               rule is in the tool and in the file's header, every row says which rule gave each element. Nothing is asked from
    ///               the server.
    ///   "Weapon"    one option of its own: the skill takes the element the weapon has NOW (its own, its ammunition's, an endow's),
    ///               so it is listed under no fixed element (FLAGGED in design notes).
    ///   The ids     "ele:" + the lower-case name: they share the skill tree's set of chosen filters with the tags' ids (Game/SkillTags.cs).
    ///   The names   the game's own element names (Lang keys "~elem~Fire" ..., as Horde/Client/HordeText.cs) and "Weapon".
    ///   The colours client/tools/w22/pick_element_colours.py: each element near the colour a player expects, as far as that allows
    ///               from the nineteen tag colours and from each other for normal and colour-blind vision (the method of
    ///               pick_tag_colours.py). The closest pairs: Neutral / the tag DoT 4.9 (deuteranopia), Water / Poison 6.0
    ///               (deuteranopia). An element's NAME is always written on its button.
    /// </summary>
    public static class SkillElements
    {
        public sealed class Element
        {
            /// <summary>The filter id ("ele:fire"), rAthena's name in the data ("Fire"), the name's Lang key, the colour.</summary>
            public string Id = "", Name = "", NameKey = "";
            public Color Color = Color.white;
        }

        // rAthena's order (src/map/map.hpp e_element), then the weapon's own
        private static readonly List<Element> _all = new List<Element>
        {
            new Element { Name = "Neutral", NameKey = Lang.N("~elem~Neutral"), Color = new Color32(0xDD, 0xDD, 0xDD, 255) },
            new Element { Name = "Water",   NameKey = Lang.N("~elem~Water"),   Color = new Color32(0x55, 0x99, 0xDD, 255) },
            new Element { Name = "Earth",   NameKey = Lang.N("~elem~Earth"),   Color = new Color32(0xDD, 0x77, 0x33, 255) },
            new Element { Name = "Fire",    NameKey = Lang.N("~elem~Fire"),    Color = new Color32(0xDD, 0x00, 0x33, 255) },
            new Element { Name = "Wind",    NameKey = Lang.N("~elem~Wind"),    Color = new Color32(0x77, 0xDD, 0x99, 255) },
            new Element { Name = "Poison",  NameKey = Lang.N("~elem~Poison"),  Color = new Color32(0x99, 0x77, 0xBB, 255) },
            new Element { Name = "Holy",    NameKey = Lang.N("~elem~Holy"),    Color = new Color32(0xDD, 0xFF, 0xDD, 255) },
            new Element { Name = "Dark",    NameKey = Lang.N("~elem~Dark"),    Color = new Color32(0x77, 0x55, 0xFF, 255) },
            new Element { Name = "Ghost",   NameKey = Lang.N("~elem~Ghost"),   Color = new Color32(0xFF, 0xDD, 0xFF, 255) },
            new Element { Name = "Undead",  NameKey = Lang.N("~elem~Undead"),  Color = new Color32(0xBB, 0x55, 0x55, 255) },
            new Element { Name = "Weapon",  NameKey = Lang.N("Weapon"),        Color = new Color32(0x99, 0x99, 0xBB, 255) },
        };

        private static readonly string[] None = new string[0];
        private static Dictionary<int, string[]> _of;
        private static Dictionary<int, string> _why;
        private static Dictionary<string, Element> _byId;
        private static int _bad;

        /// <summary>Every element, in the order the filter shows them.</summary>
        public static IReadOnlyList<Element> All { get { Load(); return _all; } }
        /// <summary>The filter ids of the elements a skill's damage can have (a skill that deals none: an empty list).</summary>
        public static IReadOnlyList<string> For(int skillId) { Load(); return _of.TryGetValue(skillId, out var e) ? e : None; }
        /// <summary>The element with this filter id, or null.</summary>
        public static Element Get(string id) { Load(); return id != null && _byId.TryGetValue(id, out var e) ? e : null; }
        /// <summary>The data's own words for a skill's elements (English, for the log; not shown to players).</summary>
        public static string WhyOf(int skillId) { Load(); return _why.TryGetValue(skillId, out var w) ? w : ""; }
        /// <summary>Skills with a row, and rows the loader could not read (0 on a good file).</summary>
        public static int Count { get { Load(); return _of.Count; } }
        public static int BadRows { get { Load(); return _bad; } }

        private static void Load()
        {
            if (_of != null) return;
            var of = new Dictionary<int, string[]>(); _why = new Dictionary<int, string>();
            _byId = new Dictionary<string, Element>(StringComparer.Ordinal);
            var byName = new Dictionary<string, Element>(StringComparer.Ordinal);
            foreach (var e in _all) { e.Id = "ele:" + e.Name.ToLowerInvariant(); _byId[e.Id] = e; byName[e.Name] = e; }
            _bad = 0;
            var ta = Resources.Load<TextAsset>("Skills/skill_elements");
            if (ta == null) { Debug.LogWarning("[SkillElements] Resources/Skills/skill_elements is missing: no skill has an element"); _of = of; return; }
            foreach (var raw in ta.text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                var c = line.Split('\t');
                if (c[0] != "E" || c.Length < 4 || !int.TryParse(c[1], out int id)) { _bad++; continue; }
                var ids = new List<string>(4);
                foreach (var p in c[3].Split(','))
                    if (byName.TryGetValue(p, out var e)) { if (!ids.Contains(e.Id)) ids.Add(e.Id); }
                    else _bad++;
                of[id] = ids.ToArray(); _why[id] = c.Length > 4 ? c[4] : "";
            }
            _of = of;
            Debug.Log($"[SkillElements] {of.Count} skills with an element{(_bad > 0 ? $", {_bad} unreadable rows / names" : "")}");
        }
    }
}
