using System;
using System.Collections.Generic;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 5 (w5ui): Frost Joke (BA_FROSTJOKER 318, "Unbarring Octave") and Scream (DC_SCREAM 326, "Dazzler") make the caster say a
    /// line of the game's joke lists in public chat. rAthena sends nothing for it: the skill only freezes / stuns around the caster
    /// (skills/archer/unbarringoctave.cpp, dazzler.cpp; skill.cpp:3738-3742) — the line is the client's. roBrowserLegacy (a reimplementation;
    /// the only source for when and how) loads data/ba_frostjoke.txt and data/dc_scream.txt (DB/DBManager.js:799-819, loadTable with a TAB
    /// separator :4123-4151), ties EF_TALK_FROSTJOKE 295 / EF_TALK_SCREAM 296 to the skills' cast start (DB/Skills/SkillEffect.js:350, 358:
    /// beginCastEffectId, spammed on ZC_USESKILL_ACK, Engine/MapEngine/Entity.js:1817, Renderer/EffectManager.js:1030-1052), and those
    /// effects, on the caster's own client only, send a random line as the caster's public chat (DB/Effects/EffectTable.js:6295-6329:
    /// ChatBox.onRequestTalk(..., TYPE.PUBLIC) when ownerAID is the player). rAthena sends ZC_USESKILL_ACK 0x0b1a for every skill use, with or
    /// without a cast time (unit.cpp:2485-2486, "In official this is triggered even if no cast time"; clif_skillcasting clif.cpp:5932-5965).
    /// The files: the player's GRF — data\ba_frostjoke.txt / dc_scream.txt are the LATAM client's Portuguese lines (cp1252), data\english\
    /// the English ones; the language setting picks them, as it picks msgstringtable's column. Their layout: a first line that is a title
    /// ("UILHG QLPV", "Dancer's Scream~ []"), then one line per joke, each starting with a TAB. rB splits the whole file at TABs and keeps
    /// every piece but the last (its loadTable calls back a piece only when the next one starts), the title included; Loki keeps every
    /// TAB-started line and not the title (the title is not a joke; the last joke is one). A Settings switch (Language page) turns it off;
    /// on by default, as official.
    /// </summary>
    public static class JokeLines
    {
        public const int SkillFrostJoke = 318, SkillScream = 326;
        private static readonly Dictionary<string, List<string>> _cache = new Dictionary<string, List<string>>();

        public static bool On
        {
            get => World.UserSettings.Prefs.jokeLines;
            set => World.UserSettings.Prefs.jokeLines = value;
        }

        public static bool IsJokeSkill(int skill) => skill == SkillFrostJoke || skill == SkillScream;

        /// <summary>The GRF file of a joke skill in the language the player chose.</summary>
        public static string FileOf(int skill, bool portuguese)
        {
            string name = skill == SkillScream ? "dc_scream.txt" : "ba_frostjoke.txt";
            return portuguese ? "data\\" + name : "data\\english\\" + name;
        }

        /// <summary>The lines of a joke skill's list (empty when the player's data has no such file).</summary>
        public static List<string> Lines(int skill, bool portuguese)
        {
            string path = FileOf(skill, portuguese);
            if (_cache.TryGetValue(path, out var l)) return l;
            l = new List<string>();
            var fs = LokiBootstrap.Instance != null && LokiBootstrap.Instance.Assets != null ? LokiBootstrap.Instance.Assets.Fs : null;
            byte[] b = fs?.Read(Loki.IO.RoPath.Normalize(path));
            if (b != null)
            {
                string t = Loki.Net.RoText.Decode(b); // cp1252, as the client decodes chat (Net/RoText.cs)
                var pieces = t.Split('\t');
                for (int i = 1; i < pieces.Length; i++) // pieces[0] = the title line
                {
                    string s = pieces[i].Replace('\r', ' ').Replace('\n', ' ').Trim();
                    if (s.Length > 0) l.Add(s);
                }
            }
            Debug.Log($"[Jokes] {path}: {l.Count} lines{(b == null ? " (not in the game data)" : "")}");
            _cache[path] = l;
            return l;
        }

        /// <summary>A random line of the skill's list in the current language (null when there is none).</summary>
        public static string Pick(int skill)
        {
            var l = Lines(skill, ClientText.Portuguese);
            if (l.Count == 0 && ClientText.Portuguese) l = Lines(skill, false); // the English list rather than nothing (wave 28: English never takes the Portuguese one)
            if (l.Count == 0) return null;
            return l[UnityEngine.Random.Range(0, l.Count)];
        }

        /// <summary>The Settings window's row (Language page): the switch, saved per Windows user (loki-user.json).</summary>
        /// Client wave 13, item 5 (w13ring): made by UI/SettingRow.cs (both choices shown, the current one lit); item 17: short text.
        public static ListWindow.Row SettingsRow(Action<Action> set) => SettingRow.Toggle(Lang.T("Bard and Dancer jokes"), Lang.T("Joke lines in chat"), () => On, v => set(() => On = v));
    }
}
