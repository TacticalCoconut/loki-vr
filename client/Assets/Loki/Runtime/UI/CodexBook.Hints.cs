using System.Collections.Generic;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Wave 28 (wcodex), design note (2026-10-16): "The closer you are to completing a bracket, more specific tips get unlocked." A member of a
    /// Codex bounty bracket (the server's 10-level brackets: the text pack's codex/brackets.tsv, written with the server's own script by
    /// client/tools/codex/build_bounty_script.py; their counts the server's state, Game/CodexBounty.cs) that the book does not have yet
    /// shows a hint of where it lives on its obscured list row, more precise as the character claims more of that bracket (FLAGGED
    /// steps, the share of the bracket's names claimed): from <see cref="RegionShare"/> the region (the kind of map and its nearest town:
    /// "A field near Payon"), from <see cref="MapShare"/> the map ("pay_fild03 (Payon Forest)"), from <see cref="AreaShare"/> the map and
    /// where on it ("pay_fild03, its north-east" / "pay_fild03, all over it"). The place: the creature's most populous spawn map (the
    /// pack's codex/places.tsv, client/tools/codex/build_codex_pack.py). English only, as the whole Codex.
    /// </summary>
    public sealed partial class CodexBook
    {
        public const float RegionShare = 0.5f, MapShare = 0.75f, AreaShare = 0.9f;
        /// <summary>The longest hint drawn in full on a row (characters): a map's own name is left out of a longer one.</summary>
        public const int HintMaxChars = 30;
        /// <summary>The hints on the rows drawn last (smoke): creature id → its text.</summary>
        public readonly Dictionary<int, string> HintsDrawn = new Dictionary<int, string>();

        /// <summary>The hint of an unfound bracket member's row now (null: none: no bounties on this server, not a member, no spawn map,
        /// less than <see cref="RegionShare"/> of its bracket claimed).</summary>
        public string BracketHint(int id)
        {
            if (!BountiesShown) return null;
            var cr = CodexData.GetCreature(id); if (cr == null || cr.Places.Count == 0) return null;
            int b = CodexData.BracketOf(id); if (b <= 0) return null;
            var v = Bounties.Bracket(b); if (v.members <= 0) return null;
            float share = v.claimed / (float)v.members;
            var (map, town, kind, area) = cr.Places[0];
            if (share >= AreaShare) return map + ", " + AreaText(area);
            if (share >= MapShare) { string n = MapTitle(map), s = n.Length > 0 && n != map ? $"{map} ({n})" : map; return s.Length <= HintMaxChars ? s : map; }
            if (share >= RegionShare) return RegionText(town, kind);
            return null;
        }
        private static string RegionText(string town, string kind)
        {
            bool far = string.IsNullOrEmpty(town);
            switch (kind)
            {
                case "field": return far ? "A field far from the towns" : "A field near " + town;
                case "dungeon": return far ? "A dungeon far from the towns" : "A dungeon near " + town;
                case "town": return far ? "In a town" : "In " + town;
                default: return far ? "Far from the towns" : "Somewhere near " + town;
            }
        }
        private static string AreaText(string area)
        {
            if (string.IsNullOrEmpty(area) || area == "all") return "all over it";
            if (area == "middle") return "its middle";
            return "its " + area.Replace("+", " and ");
        }
        /// <summary>A map's own name from the player's data, its first part ("Amatsu, the Land of Destiny" → "Amatsu"); "" none.</summary>
        private static string MapTitle(string map)
        {
            string n = ClientText.MapName(map); if (string.IsNullOrEmpty(n) || n == map) return "";
            int comma = n.IndexOf(','); return (comma > 0 ? n.Substring(0, comma) : n).Trim();
        }

        /// <summary>The row's name and note move up to its top half, the hint takes its bottom half across both (24 px, the theme's
        /// Accent ink).</summary>
        private void AddRowHint(UiButton b, int id, string hint)
        {
            var rt = (RectTransform)b.transform; float h = rt.rect.height, half = h * 0.5f - 2f;
            Text name = null, note = null;
            foreach (var t in rt.GetComponentsInChildren<Text>(true)) { if (t.name == "rowname") name = t; else if (t.name == "rownote") note = t; }
            float left = -CW / 2f, right = CW / 2f - 8f;
            if (name != null) { var n = name.rectTransform; left = n.anchoredPosition.x - n.sizeDelta.x / 2f; n.anchoredPosition = new Vector2(n.anchoredPosition.x, h * 0.25f); n.sizeDelta = new Vector2(n.sizeDelta.x, half); }
            if (note != null) { var n = note.rectTransform; n.anchoredPosition = new Vector2(n.anchoredPosition.x, h * 0.25f); n.sizeDelta = new Vector2(n.sizeDelta.x, half); }
            var ht = Txt(rt, hint, (left + right) / 2f, -h * 0.25f, right - left, half, 24, TextAnchor.MiddleLeft, Theme.Accent, false, 24);
            ht.name = "rowhint";
            HintsDrawn[id] = hint;
        }
    }
}
