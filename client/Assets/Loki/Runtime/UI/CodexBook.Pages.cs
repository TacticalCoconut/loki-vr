using System;
using System.Collections.Generic;
using System.Linq;
using Loki.Game;
using Loki.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>Wave 28 (wcodex): the Codex's pages (what each spread shows), its covers and its turning sheets (UI/CodexBook.cs).</summary>
    public sealed partial class CodexBook
    {
        private static float Top => PageH / 2f;
        private static float CW => PageW - 2f * Margin;
        // the theme's own inks (UI/Theme.cs: the kit's 7 : 1 contrast on a card), never colours of the book's own (design note, 2026-10-15: "keep the entire
        // slot themed with the exact type of theming that the entire UI still uses, including the variations based on the player's class": the
        // pages are the kit's cards and the rows its buttons, so the job variant (UI/JobTheme.cs) dresses them as every other panel)
        private static readonly Color Heading = Theme.Accent, Faint = Theme.InkFaint, WeakInk = Theme.Good, StrongInk = Theme.Bad;

        // ------------------------------------------------------------------ small pieces
        private static Text Txt(RectTransform p, string s, float x, float y, float w, float h, int size, TextAnchor a = TextAnchor.UpperLeft, Color? c = null, bool bold = false, int fitMin = 0)
        {
            var t = UiKit.Label(p, "", new Vector2(x, y), new Vector2(w, h), size, a, c);
            t.text = s ?? "";
            if (bold) t.fontStyle = FontStyle.Bold;
            if (fitMin > 0) { t.resizeTextForBestFit = true; t.resizeTextMinSize = fitMin; t.resizeTextMaxSize = size; }
            return t;
        }
        private static UiButton Btn(RectTransform p, string label, float x, float y, float w, float h, Action a, int size = 26)
        {
            var b = UiKit.Button(p, label, new Vector2(x, y), new Vector2(w, h), a, size);
            b.Text.resizeTextMinSize = Mathf.Max(24, size - 6);
            return b;
        }
        private static Image Pic(RectTransform p, Sprite s, float x, float y, float w, float h)
        {
            var i = UiKit.Icon(p, new Vector2(x, y), new Vector2(w, h));
            i.sprite = s; i.enabled = s != null; i.preserveAspect = true;
            if (s != null && s.texture != null && (s.texture.width < 64 || s.texture.height < 64)) s.texture.filterMode = FilterMode.Point; // (small game art drawn large: crisp)
            return i;
        }
        private static void Rule(RectTransform p, float y) { UiKit.StitchLine(p, new Vector2(-CW / 2, y), new Vector2(CW / 2, y)); }
        /// <summary>The mark of a picture the player's game data does not have: a framed "?" (an item rAthena knows that the player's
        /// iteminfo does not list, e.g. First aid Box (5) #23484: no sprite name, so no icon; a source creature not yet in the book).</summary>
        private static void NoPicture(RectTransform p, float x, float y, float size, float width = 0f)
        {
            float w = width > 0f ? width : size;
            var b = UiKit.Soft(p, "nopicture", new Vector2(x, y), new Vector2(w, size), Theme.Row);
            Txt(b, "?", 0f, 0f, w, size, Mathf.Max(24, Mathf.RoundToInt(size * 0.62f)), TextAnchor.MiddleCenter, Faint, true);
        }

        /// <summary>A row of a list (an index, the recent entries, a drop, a source): icon, name, a note on the right; the whole row a button.</summary>
        private UiButton Row(RectTransform p, float y, float h, Sprite icon, string name, string note, Action a, int size = 26, Color? noteColor = null, int silhouetteOf = 0)
        {
            var b = Btn(p, "", 0f, y, CW, h, a, size);
            var rt = (RectTransform)b.transform;
            // the picture box: wider than tall (most creatures are), the picture cut to the creature and filling it (UI/CodexBook.Pages.cs
            // CreatureFrame; pixel art drawn with nearest-neighbour: Pic)
            float ph = h - 8f, pw = Mathf.Round(ph * 1.35f), ix = -CW / 2 + 6f + pw / 2f;
            if (silhouetteOf > 0) { var img = Pic(rt, null, ix, 0f, pw, ph); img.name = "silhouette"; AddPortrait(img, silhouetteOf, true); }
            else if (icon != null) Pic(rt, icon, ix, 0f, pw, ph); else NoPicture(rt, ix, 0f, ph, pw);
            float noteW = string.IsNullOrEmpty(note) ? 0f : 150f, nx = -CW / 2 + pw + 16f, nw = CW - pw - 28f - noteW;
            var n = Txt(rt, name, nx + nw / 2f, 0f, nw, h - 4, size, TextAnchor.MiddleLeft, null, false, 24);
            n.horizontalOverflow = HorizontalWrapMode.Wrap; n.name = "rowname";
            if (noteW > 0) Txt(rt, note, CW / 2 - noteW / 2 - 8, 0f, noteW, h - 4, Mathf.Max(24, size - 2), TextAnchor.MiddleRight, noteColor ?? Faint, false, 24).name = "rownote";
            if (a == null) b.Interactable = false;
            return b;
        }
        /// <summary>The item page shown last took its "about" text from the text pack's lore (smoke).</summary>
        public bool AboutFromPack { get; private set; }
        /// <summary>The rows made on the pages shown (smoke: what the index lists, which row opens what).</summary>
        public readonly List<(UiButton button, PageRef to)> RowTargets = new List<(UiButton, PageRef)>();

        // ------------------------------------------------------------------ the book's words: English (design note, 2026-10-15: "English-only UI text policy")
        public static string ElementName(int e) => CodexData.Elements[Mathf.Clamp(e, 0, CodexData.Elements.Length - 1)];
        public static string RaceName(int r) => CodexData.Races[Mathf.Clamp(r, 0, CodexData.Races.Length - 1)];
        public static string SizeName(int s) => CodexData.Sizes[Mathf.Clamp(s, 0, CodexData.Sizes.Length - 1)];
        private static readonly Dictionary<string, string> SubKeys = new Dictionary<string, string>
        {
            { "Dagger", ("Dagger") }, { "1hSword", ("One-handed sword") }, { "2hSword", ("Two-handed sword") },
            { "1hSpear", ("One-handed spear") }, { "2hSpear", ("Two-handed spear") }, { "1hAxe", ("One-handed axe") },
            { "2hAxe", ("Two-handed axe") }, { "Mace", ("Mace") }, { "2hMace", ("Two-handed mace") },
            { "Staff", ("Staff") }, { "2hStaff", ("Two-handed staff") }, { "Bow", ("Bow") }, { "Knuckle", ("Knuckle") },
            { "Musical", ("Instrument") }, { "Whip", ("Whip") }, { "Book", ("Book") }, { "Katar", ("Katar") },
            { "Revolver", ("Revolver") }, { "Rifle", ("Rifle") }, { "Gatling", ("Gatling gun") }, { "Shotgun", ("Shotgun") },
            { "Grenade", ("Grenade launcher") }, { "Huuma", ("Huuma shuriken") },
            { "Arrow", ("Arrow") }, { "Bullet", ("Bullet") }, { "Shell", ("Shell") }, { "Shuriken", ("Shuriken") },
            { "Kunai", ("Kunai") }, { "Cannonball", ("Cannonball") }, { "Throwweapon", ("Throwing weapon") },
        };
        /// <summary>The item's kind by the game's type number (Game/GameDb.cs: rAthena's item types as the client numbers them).</summary>
        public static string TypeName(int type)
        {
            switch (type)
            {
                case 0: return "Healing item"; case 2: case 11: return "Usable item"; case 3: return "Etc"; case 4: return "Armor"; case 5: return "Weapon"; case 6: return "Card";
                case 7: return "Pet egg"; case 8: return "Pet equipment"; case 10: return "Ammunition"; case 12: return "Shadow equipment"; case 18: return "Cash item"; default: return "Item";
            }
        }
        // the places an item is worn (rAthena src/common/mmo.hpp:336-358 equip_pos)
        private static readonly (uint bit, string name)[] Places =
        {
            (0x22, "Both hands"), (0x2, "Right hand"), (0x20, "Left hand"), (0x100, "Upper head"), (0x200, "Middle head"), (0x1, "Lower head"), (0x10, "Body"), (0x4, "Garment"),
            (0x40, "Shoes"), (0x88, "Accessory"), (0x8, "Accessory"), (0x80, "Accessory"), (0x400, "Costume upper head"), (0x800, "Costume middle head"), (0x1000, "Costume lower head"),
            (0x2000, "Costume garment"), (0x8000, "Ammunition"), (0x10000, "Shadow armor"), (0x20000, "Shadow weapon"), (0x40000, "Shadow shield"), (0x80000, "Shadow shoes"),
            (0x300000, "Shadow accessory"), (0x100000, "Shadow accessory"), (0x200000, "Shadow accessory"),
        };
        public static string SlotNames(uint mask)
        {
            var l = new List<string>(); uint left = mask;
            foreach (var (bit, name) in Places) if ((left & bit) == bit && bit != 0) { if (!l.Contains(name)) l.Add(name); left &= ~bit; }
            return string.Join(" + ", l);
        }
        private static string SubName(CodexData.Item it)
        {
            if (it == null || string.IsNullOrEmpty(it.Sub)) return "";
            if (it.Type == "Ammo" && (it.Sub == "Dagger" || it.Sub == "Grenade")) return it.Sub == "Dagger" ? ("Throwing dagger") : ("Grenade");
            return SubKeys.TryGetValue(it.Sub, out var k) ? k : it.Sub;
        }
        // rAthena's item_db "Jobs" keys and the job ids the game's job table names them by (SkillTree.JobName: the player's language)
        private static readonly Dictionary<string, int> JobIds = new Dictionary<string, int>
        {
            { "Novice", 0 }, { "Swordman", 1 }, { "Mage", 2 }, { "Archer", 3 }, { "Acolyte", 4 }, { "Merchant", 5 }, { "Thief", 6 }, { "Knight", 7 }, { "Priest", 8 },
            { "Wizard", 9 }, { "Blacksmith", 10 }, { "Hunter", 11 }, { "Assassin", 12 }, { "Crusader", 14 }, { "Monk", 15 }, { "Sage", 16 }, { "Rogue", 17 },
            { "Alchemist", 18 }, { "BardDancer", 19 }, { "SuperNovice", 23 }, { "Gunslinger", 24 }, { "Ninja", 25 }, { "Taekwon", 4046 }, { "StarGladiator", 4047 },
            { "SoulLinker", 4049 }, { "KagerouOboro", 4211 }, { "Rebellion", 4215 }, { "Summoner", 4218 },
        };
        private static string JobsText(string jobs)
        {
            if (string.IsNullOrEmpty(jobs)) return "";
            if (jobs == "All") return ("All classes");
            bool but = jobs.StartsWith("All-"); var keys = (but ? jobs.Substring(4) : jobs).Split(',');
            var names = keys.Where(k => k.Length > 0).Select(k => JobIds.TryGetValue(k, out int id) && SkillTreeDb.GetJob(id) != null && SkillTreeDb.GetJob(id).Name.Length > 0 ? SkillTreeDb.GetJob(id).Name : k).Distinct().ToList();
            string l = string.Join(", ", names);
            return but ? string.Format("All but {0}", l) : l;
        }

        /// <summary>A creature's ways (rAthena's mode bits: doc/mob_db_mode_list.txt), a few plain words each.</summary>
        public static List<string> Traits(CodexData.Creature c)
        {
            var l = new List<string>(); if (c == null) return l;
            int m = c.Mode;
            bool move = (m & 0x1) != 0, attack = (m & 0x80) != 0;
            if (!move && !attack) l.Add(("Never moves or attacks"));
            else
            {
                if ((m & 0x4) != 0) l.Add((m & 0x4000) != 0 ? ("Attacks weaker players on sight") : ("Attacks on sight"));
                else if (attack) l.Add(("Fights back when hit"));
                if (!move) l.Add(("Never moves"));
            }
            if ((m & 0x2) != 0) l.Add(("Picks up items"));
            if ((m & 0x8) != 0) l.Add(("Helps its own kind"));
            if ((m & 0x210) != 0) l.Add(("Goes after spellcasters"));
            if ((m & 0x2000000) != 0) l.Add(("Sees hidden players"));
            if ((m & 0x70000) == 0x70000) l.Add(("Takes 1 damage a hit"));
            if ((m & 0x4000000) != 0) l.Add(("Immune to status effects"));
            return l;
        }

        // ------------------------------------------------------------------ the index
        // Every text is 24 px or more: 1.34 cm, at least 1.35° from the eyes at the book's farthest corner from its usual place (0.57 m);
        // the bottom 110 px of each page are the turning buttons' and the eye's.
        private static void DrawGoldPageHighlight(RectTransform c)
        {
            var g = UiKit.Soft(c, "bounty:gold:page", Vector2.zero, new Vector2(PageW - 14f, PageH - 14f), BountyGoldPage);
            foreach (var img in g.GetComponentsInChildren<Image>(true)) img.raycastTarget = false;
        }

        private void DrawTitle(RectTransform c, Section sec)
        {
            Txt(c, "Codex", 0f, Top - 60f, CW, 76f, 60, TextAnchor.MiddleCenter, Heading, true);
            Rule(c, Top - 112f);
            float bw = (CW - 16f) / 2f;
            bool anyBestiariumGold = BountiesShown && CreatureList().Any(id => HasUnclaimedBounty(id) && Discovered(id));
            foreach (var s in new[] { Section.Bestiarium, Section.Itemarium })
            {
                var ss = s; float x = s == Section.Bestiarium ? -bw / 2 - 8 : bw / 2 + 8;
                string name = s == Section.Bestiarium ? "Bestiarium" : "Itemarium";
                string count = s == Section.Bestiarium ? $"{CreaturesWritten} / {CreatureList().Count}" : Count(s).ToString();
                var b = Btn(c, name + "\n" + count, x, Top - 196f, bw, 116f, () => Jump(PageRef.Index(ss, 0), "the part " + ss), 30);
                if (s == Section.Bestiarium && anyBestiariumGold) { b.Normal = BountyGoldRow; b.Hover = BountyGoldHover; b.SetHover(false); }
                else if (s == sec) { b.Normal = Theme.Lit; b.SetHover(false); }
                b.name = "btn:part:" + s;
                RowTargets.Add((b, PageRef.Index(s, 0)));
            }
            Txt(c, "Recently written", 0f, Top - 290f, CW, 44f, 30, TextAnchor.MiddleLeft, Heading, true);
            var rawRec = _journal != null ? _journal.Recent : (IReadOnlyList<string>)new string[0];
            bool levels = sec == Section.Bestiarium;
            // design note (2026-10-17 item 50): "The first page of the bestiarium shouldn't show recent entries from the itemarium"
            var filtered = new List<(bool cre, int id)>();
            var seenCre = new HashSet<int>();
            for (int i = 0; i < rawRec.Count; i++)
            {
                string r = rawRec[i];
                if (r.Length < 2 || !int.TryParse(r.Substring(1), out int id)) continue;
                bool cre = r[0] == 'c';
                if (levels && !cre) continue;
                if (cre)
                {
                    if (CodexData.IsExcluded(id)) continue;
                    id = CodexData.CanonicalId(id);
                    if (!seenCre.Add(id)) continue;
                }
                filtered.Add((cre, id));
            }
            if (filtered.Count == 0) Txt(c, "Nothing written yet.", 0f, Top - 340f, CW, 40f, 26, TextAnchor.MiddleLeft, Faint);
            if (levels) DrawLevelJumps(c);
            for (int i = 0; i < filtered.Count && i < (levels ? 3 : 6); i++)
            {
                var (cre, id) = filtered[i]; var to = cre ? PageRef.Creature(id) : PageRef.Item(id);
                string note = cre ? "Bestiarium" + BountyMark(id) : "Itemarium";
                var b = Row(c, Top - 342f - i * 64f, 58f, cre ? CreatureIcon(id) : WristMenu.ItemIcon(GameDb.GetItem(id)), cre ? NameOf(id) : ItemName(id), note, () => Jump(to, "a recent entry"), 26);
                if (cre && HasUnclaimedBounty(id)) { b.Normal = BountyGoldRow; b.Hover = BountyGoldHover; b.SetHover(false); }
                RowTargets.Add((b, to));
            }
        }

        private void DrawList(RectTransform c, Section sec, int listPage, bool right)
        {
            var ids = sec == Section.Bestiarium ? CreatureList() : ItemList();
            if (!right) HintsDrawn.Clear(); // (wave 28, wcodex: the hints of the spread drawn now)
            int pages = ListPages(sec);
            if (listPage >= pages) return; // (the last spread's empty right page)
            bool pageGold = sec == Section.Bestiarium && ListPageHasUnclaimedBounty(ids, listPage * Rows, Rows);
            if (pageGold) DrawGoldPageHighlight(c);
            // on a left list page (!right), the top-left holds the fixed Index/Bestiarium buttons, so right-align the page title
            Txt(c, sec == Section.Bestiarium ? "Bestiarium" : "Itemarium", !right ? 90f : -60f, Top - 54f, CW - (!right ? 180f : 120f), 50f, 32, !right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, pageGold ? BountyGold : Heading, true);
            Rule(c, Top - 92f);
            if (ids.Count == 0)
            {
                Txt(c, "Nothing written yet.", 0f, Top - 140f, CW, 40f, 26, TextAnchor.MiddleLeft, Faint);
                if (sec == Section.Bestiarium) Txt(c, "Look at a creature to write it down.", 0f, Top - 200f, CW, 80f, 26, TextAnchor.UpperLeft, Faint);
            }
            for (int i = 0; i < Rows; i++)
            {
                int k = listPage * Rows + i; if (k >= ids.Count) break;
                int id = ids[k]; float y = Top - 136f - i * RowPitch;
                UiButton b;
                if (sec == Section.Bestiarium)
                {
                    var cr = CodexData.GetCreature(id); int tier = _journal != null ? _journal.Tier(id) : 0;
                    if (tier != CodexJournal.TierNone)
                    {
                        string note = (cr != null ? string.Format("Lvl: {0}", cr.Level) : "") + (tier == CodexJournal.TierSeen ? " ?" : "") + BountyMark(id); // (wave 28, wcxp: the bounty's mark: UI/CodexBook.Bounty.cs)
                        b = Row(c, y, RowH, CreatureIcon(id), NameOf(id), note, () => Jump(PageRef.Creature(id), "the index"), 26);
                        if (HasUnclaimedBounty(id)) { b.Normal = BountyGoldRow; b.Hover = BountyGoldHover; b.SetHover(false); }
                        RowTargets.Add((b, PageRef.Creature(id)));
                    }
                    else
                    {
                        // not written down (design note, 2026-10-15): 10 levels or more above the player, or a boss: "?", "?????", "Lvl: ??"; else its
                        // black silhouette playing its idle animation, "?????" and its level; never opened (FLAGGED)
                        // heard of (an NPC named it: design note, "its silhouette, name and level is revealed regardless of how much stronger than you
                        // it is"): the silhouette, its name and level, still never opened
                        bool heard = _journal != null && _journal.HeardOf(id), hidden = !heard && Obscured(cr), mark = heard && _markedNew.Contains(id);
                        if (mark) _markDrawn.Add(id);
                        string shown = heard ? NameOf(id) + (mark ? "  <color=#" + Theme.Hex(Theme.Good) + "><b>New</b></color>" : "") : Unmet;
                        b = Row(c, y, RowH, null, shown, hidden ? "Lvl: ??" : string.Format("Lvl: {0}", cr.Level), null, 26, null, hidden ? 0 : id);
                        var hint = BracketHint(id); if (hint != null) AddRowHint(b, id, hint); // (wave 28, wcodex: the bracket hints, UI/CodexBook.Hints.cs)
                    }
                    b.name = "creature:" + id;
                }
                else
                {
                    var gi = GameDb.GetItem(id);
                    b = Row(c, y, RowH, WristMenu.ItemIcon(gi), ItemName(id), TypeName(gi.Type), () => Jump(PageRef.Item(id), "the index"), 26);
                    RowTargets.Add((b, PageRef.Item(id)));
                }
            }
            if (sec == Section.Bestiarium) DrawBracketFoot(c, ids, listPage * Rows, Rows); // wave 28 (wcxp): the page's brackets (UI/CodexBook.Bounty.cs)
            Txt(c, (listPage + 1) + " / " + pages, 0f, -Top + 60f, 150f, 40f, 24, TextAnchor.MiddleCenter, pageGold ? BountyGold : Faint);
        }

        // ------------------------------------------------------------------ a creature
        private void DrawCreatureLeft(RectTransform c, int id)
        {
            id = CodexData.CanonicalId(id);
            int vid = DisplayedVersion(id);
            var cr = CodexData.GetCreature(vid) ?? CodexData.GetCreature(id);
            int tier = _journal != null ? _journal.Tier(id) : 0;
            bool gold = HasUnclaimedBounty(id);
            if (gold) DrawGoldPageHighlight(c);
            Txt(c, "#" + vid, CW / 2 - 70f, Top - 46f, 140f, 40f, 24, TextAnchor.MiddleRight, Faint);
            DrawBountyLine(c, id); // wave 28 (wcxp): the creature's Codex bounty, right of portrait (UI/CodexBook.Bounty.cs)
            Txt(c, NameOf(vid), 0f, Top - 110f, CW, 62f, 44, TextAnchor.MiddleCenter, gold ? BountyGold : Heading, true, 30);
            if (cr != null)
            {
                Txt(c, string.Format("Lv {0}", cr.Level) + " · " + SizeName(cr.Size) + " · " + RaceName(cr.Race), 0f, Top - 160f, CW, 38f, 27, TextAnchor.MiddleCenter, null, false, 24);
                string badge = cr.BossType == 2 ? "  <color=#8c1d12><b>MVP</b></color>" : cr.BossType == 1 ? "  <color=#8c1d12><b>Boss</b></color>" : "";
                Txt(c, ElementName(cr.Element) + " " + cr.ElementLevel + badge, 0f, Top - 196f, CW, 38f, 27, TextAnchor.MiddleCenter, null, false, 24);
            }
            // the picture: the creature as the player's own game data draws it, standing (its idle frames, animated)
            var frame = UiKit.Soft(c, "portrait", new Vector2(0f, Top - 302f), new Vector2(CW, 170f), Theme.Row);
            AddPortrait(Pic(frame, null, 0f, 0f, 210f, 164f), vid);
            // design note (2026-10-17 item 44): in-page Versions button & submenu for creatures with multiple versions
            var allVers = CodexData.VersionsOf(id);
            var encVers = _journal != null ? _journal.EncounteredVersions(id) : new List<int> { id };
            if (encVers.Count == 0) encVers.Add(id);
            if (allVers.Count > 1)
            {
                bool open = _versionsOpenFor == id;
                var vb = Btn(c, open ? "Versions ▲" : $"Versions\n{encVers.Count} / {allVers.Count}", -CW / 2 + 70f, Top - 262f, 128f, 76f, () => ToggleVersionsMenu(id), 24);
                vb.name = "btn:versions";
                if (open) { vb.Normal = Theme.Lit; vb.SetHover(false); }
            }
            if (_versionsOpenFor == id && allVers.Count > 1)
            {
                Txt(c, $"Encountered versions ({encVers.Count} / {allVers.Count})", 0f, Top - 416f, CW, 36f, 26, TextAnchor.MiddleLeft, Heading, true);
                for (int i = 0; i < encVers.Count && i < 5; i++)
                {
                    int v = encVers[i];
                    var vcr = CodexData.GetCreature(v);
                    string vnote = v == vid ? "Selected" : (vcr != null ? string.Format("Lv {0}", vcr.Level) : "");
                    var rb = Row(c, Top - 462f - i * 54f, 48f, CreatureIcon(v), CodexData.VersionLabel(v), vnote, () => SelectVersion(id, v), 24, v == vid ? Heading : (Color?)null);
                    rb.name = "btn:version:" + v;
                    if (v == vid) { rb.Normal = Theme.Lit; rb.SetHover(false); }
                }
                return;
            }
            if (cr != null)
            {
                // how it behaves: the export's sentence for its AI type when the converter found it true of this creature, then what
                // rAthena's mode bits add (sees hidden players, 1 damage a hit, immune to status effects); else the bits alone
                var extra = Traits(cr).Where(t => t == "Sees hidden players" || t == "Takes 1 damage a hit" || t == "Immune to status effects").ToList();
                string how = cr.Behavior.Length > 0 ? cr.Behavior + (extra.Count > 0 ? " " + string.Join(". ", extra) + "." : "") : string.Join(" · ", Traits(cr));
                Txt(c, how, 0f, Top - 490f, CW, 190f, 24, TextAnchor.UpperCenter, null, false, 24); // (six lines: the longest sentence and its additions)
                Txt(c, "Lives in", 0f, Top - 604f, CW, 36f, 27, TextAnchor.MiddleLeft, Heading, true);
                string hab = cr.Habitats.Count == 0 ? OriginText(cr) : string.Join(" · ", cr.Habitats.Take(4).Select(h => ShortMap(h.map) + " ×" + h.count));
                Txt(c, hab, 0f, Top - 648f, CW, 56f, 24, TextAnchor.UpperLeft, null, false, 24);
                if (cr.TameItem > 0 && tier == CodexJournal.TierFull) Txt(c, string.Format("Tame with {0}", ItemName(cr.TameItem)), 0f, Top - 700f, CW, 34f, 24, TextAnchor.MiddleLeft, Faint, false, 24);
            }
            else Txt(c, "No Codex data found.", 0f, Top - 560f, CW, 60f, 24, TextAnchor.MiddleCenter, Faint);
        }

        private void DrawCreatureRight(RectTransform c, int id)
        {
            id = CodexData.CanonicalId(id);
            int vid = DisplayedVersion(id);
            var cr = CodexData.GetCreature(vid) ?? CodexData.GetCreature(id);
            int tier = _journal != null ? _journal.Tier(id) : 0;
            if (HasUnclaimedBounty(id)) DrawGoldPageHighlight(c);
            if (cr == null) { Txt(c, "No Codex data found.", 0f, Top - 160f, CW, 60f, 26, TextAnchor.MiddleLeft, Faint); return; }
            if (tier != CodexJournal.TierFull)
            {
                // a boss looked at: its numbers stay unknown until it is defeated
                Txt(c, "???", -60f, Top - 60f, CW - 120f, 70f, 54, TextAnchor.MiddleLeft, Faint, true);
                Txt(c, "Defeat it to complete.", 0f, Top - 150f, CW, 40f, 28, TextAnchor.MiddleLeft, StrongInk, true);
                Txt(c, "Weak to  ???\nSkills  ???\nDrops  ???", 0f, Top - 290f, CW, 180f, 28, TextAnchor.UpperLeft, Faint);
                return;
            }
            // the numbers as one table (the orchestrator, 19:30: "one clean aligned table"): three columns of equal width, in every cell the
            // name at the column's left edge and the number at its right edge; HP, Base EXP and Job EXP (up to 2,000,000,000) on a row of
            // their own, each number under its name; then the physical row, the magic row and the six stats; every other row shaded
            StatBand(c, Top - 54f, 60f);
            StatCell(c, 0, Top - 40f, "HP", cr.Hp.ToString("N0"), true);
            StatCell(c, 1, Top - 40f, "Base EXP", cr.BaseExp.ToString("N0"), true);
            StatCell(c, 2, Top - 40f, "Job EXP", cr.JobExp.ToString("N0"), true);
            var table = new[]
            {
                new[] { ("ATK", cr.Atk.ToString("N0")), ("DEF", cr.Def.ToString("N0")), ("Range", cr.Range.ToString()) },
                new[] { ("MATK", cr.Matk.ToString("N0")), ("MDEF", cr.Mdef.ToString("N0")) },
                new[] { ("STR", cr.Stats[0].ToString()), ("AGI", cr.Stats[1].ToString()), ("VIT", cr.Stats[2].ToString()) },
                new[] { ("INT", cr.Stats[3].ToString()), ("DEX", cr.Stats[4].ToString()), ("LUK", cr.Stats[5].ToString()) },
            };
            for (int r = 0; r < table.Length; r++)
            {
                float y = Top - 102f - StatRowH * r;
                if (r % 2 == 1) StatBand(c, y, StatRowH);
                for (int k = 0; k < table[r].Length; k++) StatCell(c, k, y, table[r][k].Item1, table[r][k].Item2, false);
            }
            Rule(c, Top - 216f);
            // weaknesses and resistances: rAthena's attr_fix for its element and level, the attacking elements that hurt it more / less
            var weak = Enumerable.Range(0, 10).Where(e => cr.Attr[e] > 100).OrderByDescending(e => cr.Attr[e]).Take(4).Select(e => ElementName(e) + " " + cr.Attr[e] + "%");
            var res = Enumerable.Range(0, 10).Where(e => cr.Attr[e] < 100).OrderBy(e => cr.Attr[e]).Take(4).Select(e => ElementName(e) + " " + cr.Attr[e] + "%");
            Txt(c, "<b>Weak to</b>  " + (weak.Any() ? string.Join(" · ", weak) : "—"), 0f, Top - 246f, CW, 56f, 24, TextAnchor.UpperLeft, WeakInk, false, 24);
            Txt(c, "<b>Resists</b>  " + (res.Any() ? string.Join(" · ", res) : "—"), 0f, Top - 304f, CW, 56f, 24, TextAnchor.UpperLeft, StrongInk, false, 24);
            Rule(c, Top - 338f);
            Txt(c, "Skills", 0f, Top - 362f, CW, 36f, 27, TextAnchor.MiddleLeft, Heading, true);
            string sk = cr.Skills.Count == 0 ? "—" : string.Join(" · ", cr.Skills.Take(6).Select(s => SkillName(s.id, s.name) + (s.level > 1 ? " " + s.level : "")));
            Txt(c, sk, 0f, Top - 408f, CW, 60f, 24, TextAnchor.UpperLeft, null, false, 24);
            Txt(c, cr.Mvp && cr.MvpDrops.Count > 0 ? "Drops · MVP prize ★" : "Drops", 0f, Top - 462f, CW, 36f, 27, TextAnchor.MiddleLeft, Heading, true);
            // the drops: one per row (icon, name, chance), the card and the MVP prizes marked; a drop already in the book opens its page
            var drops = new List<(int item, int rate, bool card, bool mvp)>();
            foreach (var d in cr.MvpDrops.Take(2)) drops.Add((d.item, d.rate, d.card, true));
            foreach (var d in cr.Drops) if (!drops.Any(x => x.item == d.item)) drops.Add((d.item, d.rate, d.card, false));
            for (int i = 0; i < drops.Count && i < 5; i++)
            {
                var d = drops[i]; int item = d.item; var to = PageRef.Item(item);
                bool known = _journal != null && _journal.HasItem(item);
                string rate = (d.rate / 100f).ToString(d.rate % 100 == 0 ? "0" : d.rate % 10 == 0 ? "0.0" : "0.00") + "%";
                var b = Row(c, Top - 504f - i * 46f, 44f, WristMenu.ItemIcon(GameDb.GetItem(item)), (d.mvp ? "★ " : "") + ItemName(item), rate, known ? () => Jump(to, "a drop") : (Action)null, 24, d.card ? StrongInk : (Color?)null);
                if (known) RowTargets.Add((b, to));
            }
        }

        public const float StatRowH = 30f;
        /// <summary>A creature not written down shows no picture and no level when it is a boss or <see cref="ObscureLevels"/> levels or more
        /// above the player's base level now (design note, 2026-10-15: "If the creature is 10 levels or more above the player, or is a boss").</summary>
        public const int ObscureLevels = 10;
        public bool Obscured(CodexData.Creature cr) => cr == null || cr.BossType > 0 || cr.Level >= (PlayerLevel != null ? PlayerLevel() : 1) + ObscureLevels;

        /// <summary>The Bestiarium's level jumps (FLAGGED: Loki's ranges): a button per range on its title page goes to the list spread of the
        /// first creature of that level or more (1,800 creatures are about 100 spreads).</summary>
        public static readonly int[] LevelJumps = { 1, 20, 40, 60, 80, 100, 130, 160 };
        private void DrawLevelJumps(RectTransform c)
        {
            Txt(c, "Jump to level", 0f, Top - 548f, CW, 40f, 28, TextAnchor.MiddleLeft, Heading, true);
            DrawBracketSum(c); // wave 28 (wcxp): the brackets done, on this line (UI/CodexBook.Bounty.cs)
            float bw = (CW - 3 * 8f) / 4f;
            for (int i = 0; i < LevelJumps.Length; i++)
            {
                int from = LevelJumps[i]; string label = i + 1 < LevelJumps.Length ? $"{from}-{LevelJumps[i + 1] - 1}" : $"{from}+";
                var b = Btn(c, label, -CW / 2 + bw / 2 + (i % 4) * (bw + 8f), Top - 604f - (i / 4) * 70f, bw, 62f, () => JumpToLevel(from), 26);
                b.name = "btn:levels:" + from;
            }
        }
        /// <summary>The list spread holding the first creature of level <paramref name="level"/> or more (the last spread when none is).</summary>
        public void JumpToLevel(int level)
        {
            var l = CreatureList(); int i = l.FindIndex(id => { var c = CodexData.GetCreature(id); return c != null && c.Level >= level; });
            if (i < 0) i = Mathf.Max(0, l.Count - 1);
            Jump(PageRef.Index(Section.Bestiarium, SpreadOfRow(i)), "the level jump " + level);
        }

        private static readonly Dictionary<string, Sprite> _silhouettes = new Dictionary<string, Sprite>();
        /// <summary>A creature's frame ENTIRELY black (design note, 2026-10-15: "The silhouettes should be ENTIRELY black"): every pixel of the frame
        /// that is not the background around it, its half-see-through edge and any hole the outline encloses (eyes, gaps) included, is
        /// solid black; only what the frame's border reaches through see-through pixels stays see-through.</summary>
        public static Sprite Silhouette(string path, int frame)
        {
            string key = path + "|" + frame;
            if (_silhouettes.TryGetValue(key, out var s)) return s;
            s = null;
            var src = SpritePortrait.Frame(path, 0, frame);
            if (src != null && src.texture != null && src.texture.isReadable)
            {
                var t = src.texture; int w = t.width, h = t.height;
                var px = t.GetPixels32(); BlackOut(px, w, h);
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "silhouette" };
                tex.SetPixels32(px); tex.Apply(false, false);
                s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            }
            _silhouettes[key] = s;
            return s;
        }
        /// <summary>See-through only where the border reaches through see-through pixels; every other pixel opaque black.</summary>
        public static void BlackOut(Color32[] px, int w, int h)
        {
            int n = w * h; var outside = new bool[n]; var q = new Queue<int>();
            void Seed(int i) { if (!outside[i] && px[i].a == 0) { outside[i] = true; q.Enqueue(i); } }
            for (int x = 0; x < w; x++) { Seed(x); Seed((h - 1) * w + x); }
            for (int y = 0; y < h; y++) { Seed(y * w); Seed(y * w + w - 1); }
            while (q.Count > 0) { int i = q.Dequeue(), x = i % w; if (x > 0) Seed(i - 1); if (x < w - 1) Seed(i + 1); if (i >= w) Seed(i - w); if (i + w < n) Seed(i + w); }
            for (int i = 0; i < n; i++) px[i] = outside[i] ? new Color32(0, 0, 0, 0) : new Color32(0, 0, 0, 255);
        }

        /// <summary>A creature source the player has not met yet (not in the Bestiarium at any tier): its name on an item's page.</summary>
        public const string Unmet = "?????";
        /// <summary>A cell of the creature's table: the name at the column's left edge ("label:&lt;name&gt;"), the number at its right edge
        /// ("stat:&lt;name&gt;"), on the same line, or (<paramref name="under"/>) on the line below in a larger hand.</summary>
        private static void StatCell(RectTransform c, int col, float y, string name, string value, bool under)
        {
            float third = CW / 3f, w = third - 14f, x = -CW / 2 + third * (col + 0.5f);
            Txt(c, name, x, y, w, StatRowH, 24, TextAnchor.MiddleLeft, Faint).name = "label:" + name;
            var v = Txt(c, value, x, under ? y - 28f : y, w, StatRowH, under ? 26 : 24, TextAnchor.MiddleRight, null, true);
            v.name = "stat:" + name; v.horizontalOverflow = HorizontalWrapMode.Overflow; // (the training dummies' 2,000,000,000 HP reach into the margin)
        }
        private static void StatBand(RectTransform c, float y, float h) => UiKit.Soft(c, "band", new Vector2(0f, y), new Vector2(CW, h), Theme.Row);

        /// <summary>"Lives in" for a creature with no map spawn (the orchestrator, 19:30: never a dash): what the Codex data says instead.
        /// "Summoned by" the monsters whose skills call it and the items that call up its summon group (two at most), "Quests, events or
        /// instances" when a loaded script spawns it, else "No regular spawn" (Gullinbursti #1828, EVENT_GULLINBURSTI: nothing in rAthena
        /// spawns it; the field's Gullinbursti is #1311).</summary>
        public string OriginText(CodexData.Creature cr)
        {
            var who = new List<string>(); bool script = false;
            foreach (var part in (cr != null ? cr.Origin : "").Split('|'))
            {
                if (part.StartsWith("mob:")) { foreach (var s in part.Substring(4).Split(',')) if (int.TryParse(s, out int m)) who.Add(NameOf(m)); }
                else if (part.StartsWith("item:")) { foreach (var s in part.Substring(5).Split(',')) if (int.TryParse(s, out int i)) who.Add(ItemName(i)); }
                else if (part == "script") script = true;
            }
            var l = new List<string>();
            who = who.Distinct().Take(2).ToList();
            if (who.Count > 0) l.Add("Summoned by " + string.Join(", ", who));
            if (script) l.Add("Quests, events or instances");
            return l.Count > 0 ? string.Join(" · ", l) : "No regular spawn";
        }
        /// <summary>A "Dropped by" source that lives on no map, marked: "(summoned)", "(quest/event)" or "(no spawn)"; "" when it has a map
        /// spawn. Tells namesakes apart: the Stone's 100 % "Poring" is #1725 (the Monster Race's and the Novice Academy's), not the field's.</summary>
        public static string SourceTag(int mob)
        {
            var cr = CodexData.GetCreature(mob);
            if (cr == null || cr.Habitats.Count > 0) return "";
            if (cr.Origin.Contains("mob:") || cr.Origin.Contains("item:")) return " (summoned)";
            return cr.Origin.Contains("script") ? " (quest/event)" : " (no spawn)";
        }

        /// <summary>A monster skill's name: rAthena's own (skill_db.yml Description, in the Codex data: "Water Attribute Attack"), else the
        /// game's English name (the client's table names the monsters' own skills by their codes: "NPC_WATERATTACK").</summary>
        private static string SkillName(int id, string data)
        {
            if (!string.IsNullOrEmpty(data)) return data;
            if (SkillDb.Has(id)) { var n = SkillDb.Get(id).NameEn; if (!string.IsNullOrEmpty(n) && !n.StartsWith("Skill #")) return n; }
            return "#" + id;
        }
        private static string ShortMap(string map)
        {
            string n = ClientText.MapName(map); if (string.IsNullOrEmpty(n)) n = map;
            int cut = n.IndexOfAny(new[] { ',', '(' }); if (cut > 0) n = n.Substring(0, cut).Trim();
            return n;
        }

        // ------------------------------------------------------------------ an item
        private void DrawItemLeft(RectTransform c, int id)
        {
            var gi = GameDb.GetItem(id); var it = CodexData.GetItem(id);
            Txt(c, "#" + id, CW / 2 - 70f, Top - 46f, 140f, 40f, 24, TextAnchor.MiddleRight, Faint);
            Txt(c, ItemName(id), 0f, Top - 110f, CW, 62f, 42, TextAnchor.MiddleCenter, Heading, true, 28);
            string sub = SubName(it);
            Txt(c, TypeName(gi.Type) + (sub.Length > 0 ? " · " + sub : ""), 0f, Top - 160f, CW, 38f, 27, TextAnchor.MiddleCenter, null, false, 24);
            // the picture: the item's large art from the player's own data (its collection picture), else its icon drawn large
            var frame = UiKit.Soft(c, "picture", new Vector2(0f, Top - 320f), new Vector2(CW, 260f), Theme.Row);
            var art = CollectionArt(gi);
            var icon = WristMenu.ItemIcon(gi);
            if (art != null) Pic(frame, art, 0f, 0f, 192f, 252f); else if (icon != null) Pic(frame, icon, 0f, 0f, 168f, 168f); else NoPicture(frame, 0f, 0f, 168f);
            var lines = new List<string>();
            int weight = it != null ? it.Weight : gi.Weight; int slots = it != null ? it.Slots : gi.Slots;
            lines.Add(string.Format("Weight {0}", (weight / 10f).ToString(weight % 10 == 0 ? "0" : "0.0")) + (slots > 0 ? " · " + string.Format("Slots {0}", slots) : ""));
            if (it != null)
            {
                var eq = new List<string>();
                if (it.Atk > 0) eq.Add("ATK " + it.Atk);
                if (it.Matk > 0) eq.Add("MATK " + it.Matk);
                if (it.Def > 0) eq.Add("DEF " + it.Def);
                if (it.WeaponLevel > 0) eq.Add(string.Format("Weapon Lv {0}", it.WeaponLevel));
                if (it.ArmorLevel > 0) eq.Add(string.Format("Armor Lv {0}", it.ArmorLevel));
                if (eq.Count > 0) lines.Add(string.Join(" · ", eq));
                var wear = new List<string>();
                if (it.ReqLevel > 0) wear.Add(string.Format("Lv {0}+", it.ReqLevel));
                if (it.Locations != 0) wear.Add(string.Format("Worn at {0}", SlotNames(it.Locations)));
                if (wear.Count > 0) lines.Add(string.Join(" · ", wear));
                string jobs = it.Locations != 0 ? JobsText(it.Jobs) : "";
                if (jobs.Length > 0) lines.Add(jobs);
                int sell = it.Sell; if (sell > 0) lines.Add(string.Format("Sells for {0} z", sell.ToString("N0")));
            }
            Txt(c, string.Join("\n", lines), 0f, Top - 580f, CW, 200f, 25, TextAnchor.UpperCenter, null, false, 24);
        }

        private void DrawItemRight(RectTransform c, int id)
        {
            var gi = GameDb.GetItem(id); var it = CodexData.GetItem(id);
            // about the item: the text pack's English lore and effect lines (checked against rAthena's numbers by the converter), else the
            // game's own description from the player's RO data (as the item panel shows it)
            var box = UiScrollText.Create(c, new Vector2(-40f, Top - 186f), new Vector2(CW - 80f, 280f), 24, Theme.Ink);
            var lore = CodexData.Lore(id);
            if (lore.HasValue && (lore.Value.lore.Length > 0 || lore.Value.lines.Length > 0))
                box.Text.text = UiKit.RoColors(lore.Value.lore + (lore.Value.lore.Length > 0 && lore.Value.lines.Length > 0 ? "\n\n" : "") + string.Join("\n", lore.Value.lines));
            else box.Text.text = UiKit.RoColors(gi.Description ?? "");
            AboutFromPack = lore.HasValue;
            float y = Top - 356f;
            // (design note, 2026-10-15: "Creatures I have not yet encountered should not show up on an item's list of creature sources. They should
            // apear as '?????'": a source not in the Bestiarium keeps its row and its chance, its name is "?????", no picture (the "?"
            // mark), no link; written down at any tier, a boss only seen included, it shows its name)
            var sources = new List<(int mob, Sprite icon, string name, string note, Action a, PageRef? to)>();
            if (it != null)
            {
                foreach (var d in it.DroppedBy.Take(5))
                {
                    int mob = d.mob; bool known = _journal != null && _journal.Tier(mob) != CodexJournal.TierNone;
                    var to = PageRef.Creature(mob);
                    sources.Add((mob, known ? CreatureIcon(mob) : null, known ? NameOf(mob) + SourceTag(mob) : Unmet, (d.rate / 100f).ToString(d.rate % 100 == 0 ? "0" : "0.0#") + "%", known ? () => Jump(to, "a source") : (Action)null, known ? to : (PageRef?)null));
                }
            }
            Txt(c, "Dropped by", 0f, y, CW, 36f, 27, TextAnchor.MiddleLeft, Heading, true); y -= 44f;
            if (sources.Count == 0) { Txt(c, "—", 0f, y, CW, 36f, 24, TextAnchor.MiddleLeft, Faint); y -= 42f; }
            foreach (var s in sources.Take(it != null && it.SoldBy.Count > 0 ? 3 : 5))
            {
                var b = Row(c, y, 44f, s.icon, s.name, s.note, s.a, 24); b.name = "source:" + s.mob; if (s.to.HasValue) RowTargets.Add((b, s.to.Value)); y -= 48f;
            }
            if (it != null && it.SoldBy.Count > 0)
            {
                Txt(c, "Sold by", 0f, y, CW, 36f, 27, TextAnchor.MiddleLeft, Heading, true); y -= 42f;
                foreach (var s in it.SoldBy)
                {
                    if (y < -Top + 130f) break;
                    Txt(c, s.npc + " · " + ShortMap(s.map), 0f, y, CW, 38f, 24, TextAnchor.MiddleLeft, null, false, 24); y -= 42f;
                }
            }
        }

        private static readonly Dictionary<int, Sprite> _art = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, float> _artCleared = new Dictionary<int, float>();
        /// <summary>The item's large picture from the player's own game data (유저인터페이스\collection\&lt;its resource&gt;.bmp), its white
        /// backdrop made see-through (the orchestrator, 19:30: the Stone sat on a white square); null: none. Read here, not through
        /// RoAssets.Texture: that one's textures are shared and stay as they are.</summary>
        public static Sprite CollectionArt(GameDb.Item info)
        {
            if (info == null) return null;
            if (_art.TryGetValue(info.Id, out var s)) return s;
            s = null;
            var a = LokiBootstrap.Instance != null ? LokiBootstrap.Instance.Assets : null;
            if (a != null && !string.IsNullOrEmpty(info.Resource))
            {
                string key = RoPath.Normalize("À¯ÀúÀÎÅÍÆäÀÌ½º\\collection\\" + info.Resource + ".bmp"); // 유저인터페이스\collection
                var bytes = a.Fs.Read("data\\texture\\" + key);
                try
                {
                    if (bytes != null && ImageDecoders.TryDecode(key, bytes, out var img))
                    {
                        _artCleared[info.Id] = ClearBackdrop(img);
                        var tex = ImageDecoders.ToTexture(img, false, key); tex.wrapMode = TextureWrapMode.Clamp;
                        s = UiKit.ToSprite(tex);
                    }
                }
                catch (Exception e) { Loki.World.RoFormatLog.Report("texture", key, e); s = null; }
            }
            _art[info.Id] = s;
            return s;
        }
        /// <summary>The share of an item picture's pixels its backdrop clearing made see-through (0 none; smoke).</summary>
        public static float ArtCleared(int item) => _artCleared.TryGetValue(item, out var f) ? f : 0f;

        /// <summary>The backdrop clearing's numbers (FLAGGED): a pixel is backdrop paper when its channels are all <see cref="PaperMin"/> or
        /// more and differ by <see cref="PaperSpread"/> or less (white to light grey), and the picture's edges reach it through such pixels.</summary>
        public const int PaperMin = 150, PaperSpread = 14;
        /// <summary>Makes a picture's backdrop see-through: the white to light grey pixels its edges reach. Each one keeps the darkness it has
        /// against white as see-through ink (white becomes clear, a soft grey drop shadow a light shadow on the page); the art itself (any
        /// colour, or any outline darker than <see cref="PaperMin"/>) stops the clearing. A picture that would be cleared almost whole (95 %)
        /// is left as it is. Clear pixels beside the art take its colour (no seam where the picture is drawn larger). Returns the share of
        /// pixels made fully clear.</summary>
        public static float ClearBackdrop(ImageDecoders.Image img)
        {
            int w = img.Width, h = img.Height, n = w * h; var px = img.Pixels;
            if (px == null || w <= 0 || h <= 0 || px.Length < n) return 0f;
            var orig = (Color32[])px.Clone(); var reached = new bool[n]; var q = new Queue<int>();
            void Seed(int i)
            {
                if (reached[i]) return;
                var p = px[i]; int lo = Mathf.Min(p.r, Mathf.Min(p.g, p.b)), hi = Mathf.Max(p.r, Mathf.Max(p.g, p.b));
                if (p.a != 0 && (lo < PaperMin || hi - lo > PaperSpread)) return;
                reached[i] = true; q.Enqueue(i);
            }
            for (int x = 0; x < w; x++) { Seed(x); Seed((h - 1) * w + x); }
            for (int y = 0; y < h; y++) { Seed(y * w); Seed(y * w + w - 1); }
            while (q.Count > 0)
            {
                int i = q.Dequeue(), x = i % w;
                if (x > 0) Seed(i - 1); if (x < w - 1) Seed(i + 1); if (i >= w) Seed(i - w); if (i + w < n) Seed(i + w);
            }
            int clear = 0;
            for (int i = 0; i < n; i++)
            {
                if (!reached[i]) continue;
                var p = px[i]; if (p.a == 0) { clear++; continue; }
                float k = Mathf.Clamp01((255f - (p.r + p.g + p.b) / 3f) / (255f - PaperMin));
                if (k < 0.03f) { px[i] = new Color32(p.r, p.g, p.b, 0); clear++; continue; }
                float off = (1f - k) * 255f;
                px[i] = new Color32((byte)Mathf.Clamp(Mathf.RoundToInt((p.r - off) / k), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt((p.g - off) / k), 0, 255),
                                    (byte)Mathf.Clamp(Mathf.RoundToInt((p.b - off) / k), 0, 255), (byte)Mathf.RoundToInt(255f * k));
            }
            if (clear > 0.95f * n) { Array.Copy(orig, px, n); return 0f; }
            var src = (Color32[])px.Clone();
            for (int i = 0; i < n; i++)
            {
                if (src[i].a != 0) continue; int x = i % w;
                int nb = x > 0 && src[i - 1].a != 0 ? i - 1 : x < w - 1 && src[i + 1].a != 0 ? i + 1 : i >= w && src[i - w].a != 0 ? i - w : i + w < n && src[i + w].a != 0 ? i + w : -1;
                if (nb >= 0) px[i] = new Color32(src[nb].r, src[nb].g, src[nb].b, 0);
            }
            return clear / (float)n;
        }

        // ------------------------------------------------------------------ the creatures' pictures (the player's own sprites, animated)
        private sealed class Portrait { public Image Image; public int Mob; public int Frames; public float Next; public int Frame; public bool Black; }
        private readonly List<Portrait> _portraits = new List<Portrait>();
        public const float PortraitFrameSeconds = 0.15f;
        private static readonly Dictionary<int, Sprite> _icons = new Dictionary<int, Sprite>();
        /// <summary>A creature's small picture for a list row (its standing frame); null when the data has none.</summary>
        public static Sprite CreatureIcon(int mob)
        {
            if (_icons.TryGetValue(mob, out var s)) return s;
            s = CreatureFrame(mob, 0, false);
            _icons[mob] = s; return s;
        }

        private static readonly Dictionary<string, Sprite> _cut = new Dictionary<string, Sprite>();
        private static readonly Dictionary<int, Vector2Int> _frameBox = new Dictionary<int, Vector2Int>();
        /// <summary>A creature's idle frame (coloured, or its black silhouette) CUT TO THE CREATURE: the frame's see-through margin trimmed
        /// (the game's frames stand in a canvas much larger than a small creature: drawn whole in a picture box it was a few specks), every
        /// frame of its idle the size of the largest (no pulsing), so an Image that keeps the aspect fills its box with the creature.</summary>
        public static Sprite CreatureFrame(int mob, int frame, bool black)
        {
            string key = mob + "|" + frame + "|" + (black ? "b" : "c");
            if (_cut.TryGetValue(key, out var s)) return s;
            string path = Sprites.RoNames.BodyPath(mob, 0);
            var src = string.IsNullOrEmpty(path) ? null : black ? Silhouette(path, frame) : SpritePortrait.Frame(path, 0, frame);
            s = src;
            if (src != null && src.texture != null && src.texture.isReadable)
            {
                var t = src.texture; var bb = Opaque(t); var box = FrameBox(mob, path);
                if (bb.width > 0f)
                {
                    float w = Mathf.Min(t.width, Mathf.Max(bb.width, box.x)), h = Mathf.Min(t.height, Mathf.Max(bb.height, box.y));
                    float x = Mathf.Clamp(bb.center.x - w / 2f, 0f, t.width - w), y = Mathf.Clamp(bb.center.y - h / 2f, 0f, t.height - h);
                    s = Sprite.Create(t, new Rect(Mathf.Round(x), Mathf.Round(y), Mathf.Round(w), Mathf.Round(h)), new Vector2(0.5f, 0.5f), 100f);
                }
            }
            _cut[key] = s;
            return s;
        }
        /// <summary>The rectangle of a picture's pixels that are not see-through (texture pixels, bottom-left origin; width 0: none).</summary>
        private static Rect Opaque(Texture2D t)
        {
            var px = t.GetPixels32(); int w = t.width, h = t.height, x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (px[y * w + x].a != 0) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
            return x1 < 0 ? new Rect(0, 0, 0, 0) : new Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        }
        /// <summary>The largest creature rectangle over its idle frames (the size every frame is cut to).</summary>
        private static Vector2Int FrameBox(int mob, string path)
        {
            if (_frameBox.TryGetValue(mob, out var v)) return v;
            v = Vector2Int.zero; int frames = 1;
            var assets = LokiBootstrap.Instance != null ? LokiBootstrap.Instance.Assets : null;
            if (assets != null && assets.Sprite(path, out _, out var act) && act.Actions.Length > 0) frames = Mathf.Max(1, act.Actions[0].Frames.Length);
            for (int f = 0; f < frames; f++)
            {
                var sp = SpritePortrait.Frame(path, 0, f); if (sp == null || sp.texture == null || !sp.texture.isReadable) continue;
                var r = Opaque(sp.texture); v = new Vector2Int(Mathf.Max(v.x, (int)r.width), Mathf.Max(v.y, (int)r.height));
            }
            _frameBox[mob] = v; return v;
        }
        /// <summary>The creature's picture is shown on the page now (smoke: it came from the player's data).</summary>
        public bool PortraitShown => _portraits.Any(p => p.Image != null && p.Image.enabled && p.Image.sprite != null);
        private void AddPortrait(Image img, int mob, bool black = false)
        {
            string path = Sprites.RoNames.BodyPath(mob, 0); int frames = 1;
            var assets = LokiBootstrap.Instance != null ? LokiBootstrap.Instance.Assets : null;
            if (!string.IsNullOrEmpty(path) && assets != null && assets.Sprite(path, out _, out var act) && act.Actions.Length > 0) frames = Mathf.Max(1, act.Actions[0].Frames.Length);
            var p = new Portrait { Image = img, Mob = mob, Frames = frames, Next = Time.unscaledTime, Black = black };
            _portraits.Add(p); SetPortrait(p);
        }
        private void SetPortrait(Portrait p)
        {
            var s = CreatureFrame(p.Mob, p.Frame, p.Black);
            if (p.Black) p.Image.color = Color.white; // (the black is the picture's own: no tint, no theme colour over it)
            p.Image.sprite = s; p.Image.enabled = s != null; p.Image.preserveAspect = true;
            if (s != null && s.texture != null) s.texture.filterMode = FilterMode.Point;
        }
        private void TickPortraits()
        {
            for (int i = _portraits.Count - 1; i >= 0; i--)
            {
                var p = _portraits[i];
                if (p.Image == null) { _portraits.RemoveAt(i); continue; }
                if (p.Frames <= 1 || Time.unscaledTime < p.Next) continue;
                p.Next = Time.unscaledTime + PortraitFrameSeconds; p.Frame = (p.Frame + 1) % p.Frames; SetPortrait(p);
            }
        }

        // ------------------------------------------------------------------ the covers (made here: an ornate leather texture of Loki's own)
        private readonly List<MeshRenderer> _coverParts = new List<MeshRenderer>();
        private MeshRenderer _pageBlockL, _pageBlockR;
        private static Material _leatherMat, _spineMat, _stackMat, _sheetMat;
        private void BuildCovers()
        {
            const float rim = 18f, stack = 7f;
            var leather = LeatherMaterial(); var spine = SpineMaterial(); var pages = StackMaterial();
            // each half: the cover (leather, larger than the page, front and back faces) and the edge of the page block under the page
            foreach (var (hinge, side) in new[] { (_hingeL, -1), (_hingeR, 1) })
            {
                float cx = side * (Gutter / 2 + PageW / 2 + rim / 2);
                var covSz = new Vector2(PageW + rim + 4f, PageH + 2 * rim);
                _coverParts.Add(Flat(hinge, "cover", leather, new Vector3(cx, 0f, 6f), covSz, 7));
                var back = Flat(hinge, "cover:back", leather, new Vector3(cx, 0f, 6.2f), covSz, 7);
                back.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                _coverParts.Add(back);
                var pb = Flat(hinge, "pageblock", pages, new Vector3(side * (Gutter / 2 + PageW / 2 + stack / 2), -stack / 2, 3f), new Vector2(PageW + stack, PageH + stack), 8);
                if (side < 0) _pageBlockL = pb; else _pageBlockR = pb;
                _coverParts.Add(pb);
            }
            var spSz = new Vector2(Gutter + 2 * 22f, PageH + 2 * rim);
            _coverParts.Add(Flat(_body, "spine", spine, new Vector3(0f, 0f, 7f), spSz, 7));
            var spBack = Flat(_body, "spine:back", spine, new Vector3(0f, 0f, 7.2f), spSz, 7);
            spBack.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            _coverParts.Add(spBack);
        }
        private void UpdateCoverFacing(bool frontL, bool frontR)
        {
            if (_pageBlockL != null && _pageBlockL.enabled != frontL) _pageBlockL.enabled = frontL;
            if (_pageBlockR != null && _pageBlockR.enabled != frontR) _pageBlockR.enabled = frontR;
        }
        private void SetCoverAlpha(float a) { }

        private static MeshRenderer Flat(Transform parent, string name, Material mat, Vector3 pos, Vector2 size, int order)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = name; go.layer = UiKit.PanelLayer;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.sortingOrder = order;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            return mr;
        }
        private static Material Overlay(string name, Texture2D tex, int queue)
        {
            var sh = Shader.Find("Loki/UIOverlay");
            return new Material(sh != null ? sh : Shader.Find("Sprites/Default")) { name = name, mainTexture = tex, renderQueue = queue };
        }
        public static Material LeatherMaterial()
        {
            if (_leatherMat != null) return _leatherMat;
            const int w = 256, h = 360; var px = new Color32[w * h]; var rnd = new System.Random(28);
            var baseC = new Color(0.34f, 0.14f, 0.08f); var dark = new Color(0.18f, 0.07f, 0.04f);
            var gold = new Color(0.92f, 0.74f, 0.28f); var brightGold = new Color(1.00f, 0.88f, 0.48f);
            float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float n = (float)rnd.NextDouble() * 0.09f + 0.04f * Mathf.Sin(x * 0.29f + y * 0.19f) + 0.03f * Mathf.Sin(x * 0.09f - y * 0.23f);
                    var c = Color.Lerp(dark, baseC, 0.74f + n);
                    float ex = Mathf.Min(x, w - 1 - x), ey = Mathf.Min(y, h - 1 - y), e = Mathf.Min(ex, ey);
                    // beveled dark leather edge and stitched border
                    if (e < 6f) c = Color.Lerp(c, dark, 0.78f);
                    if (Mathf.Abs(e - 8f) < 0.9f && (((x + y) / 4) % 2 == 0)) c = Color.Lerp(c, gold, 0.55f);
                    // double gold-leaf border fillets
                    bool outerFillet = Mathf.Abs(e - 15f) < 1.3f;
                    bool innerFillet = Mathf.Abs(e - 24f) < 1.1f;
                    if (outerFillet || innerFillet) c = outerFillet ? brightGold : gold;
                    // ornate gold corner rosettes and diagonal corner brackets
                    float cdx = Mathf.Min(x - 24f, (w - 1 - 24f) - x), cdy = Mathf.Min(y - 24f, (h - 1 - 24f) - y);
                    if (cdx >= -9f && cdx < 26f && cdy >= -9f && cdy < 26f)
                    {
                        float cr = Mathf.Sqrt(cdx * cdx + cdy * cdy);
                        if (Mathf.Abs(cr - 14f) < 1.4f || Mathf.Abs(cr - 7f) < 1.3f || (cr < 3.5f)) c = brightGold;
                    }
                    // central gold-tooled Codex compass rose medallion
                    float dx = x - cx, dy = (y - cy) * 0.88f, r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);
                    float star8 = 36f + 12f * Mathf.Cos(ang * 8f);
                    float diamond = Mathf.Abs(dx) * 0.85f + Mathf.Abs(dy) * 0.65f;
                    if (Mathf.Abs(r - 54f) < 1.6f || Mathf.Abs(r - 47f) < 1.1f || Mathf.Abs(r - 22f) < 1.4f) c = brightGold;
                    else if (r > 47f && r < 54f && (((int)(Mathf.Abs(ang) * 18f)) % 2 == 0)) c = Color.Lerp(c, gold, 0.7f);
                    else if (Mathf.Abs(r - star8) < 1.6f || Mathf.Abs(diamond - 38f) < 1.4f) c = gold;
                    else if (r < 9f) c = r < 4.5f ? brightGold : gold;
                    px[y * w + x] = c;
                }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "CodexLeather", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px); tex.Apply(false, true);
            return _leatherMat = Overlay("CodexLeather", tex, 3098);
        }
        public static Material SpineMaterial()
        {
            if (_spineMat != null) return _spineMat;
            const int w = 96, h = 360; var px = new Color32[w * h]; var rnd = new System.Random(29);
            var baseC = new Color(0.30f, 0.12f, 0.07f); var dark = new Color(0.15f, 0.06f, 0.03f);
            var gold = new Color(0.92f, 0.74f, 0.28f); var brightGold = new Color(1.00f, 0.88f, 0.48f);
            float[] ribs = { 0.14f, 0.32f, 0.50f, 0.68f, 0.86f };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)(w - 1), v = y / (float)(h - 1);
                    float arch = Mathf.Sin(u * Mathf.PI); // cylindrical shading across spine
                    float n = (float)rnd.NextDouble() * 0.07f;
                    var c = Color.Lerp(dark, baseC, 0.45f + 0.48f * arch + n);
                    if (x < 4 || x >= w - 4 || y < 5 || y >= h - 5) c = Color.Lerp(c, dark, 0.75f);
                    if (Mathf.Abs(x - 8f) < 1.2f || Mathf.Abs(x - (w - 9f)) < 1.2f) c = gold;
                    // raised spine ribs with flanking gold fillets
                    foreach (float ry in ribs)
                    {
                        float dy = Mathf.Abs(v - ry) * h;
                        if (dy < 4.5f) c = Color.Lerp(c, brightGold, dy < 1.6f ? 0.85f : 0.35f);
                        else if (Mathf.Abs(dy - 7.5f) < 1.1f && x > 8 && x < w - 8) c = brightGold;
                    }
                    // gold lozenge stamp in each spine compartment between ribs
                    float[] panels = { 0.23f, 0.41f, 0.59f, 0.77f };
                    foreach (float py in panels)
                    {
                        float dx = Mathf.Abs(x - w * 0.5f), dy = Mathf.Abs(v - py) * h;
                        float dm = dx * 0.9f + dy * 0.75f;
                        if (Mathf.Abs(dm - 12f) < 1.4f || dm < 3.2f) c = brightGold;
                    }
                    px[y * w + x] = c;
                }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "CodexSpine", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px); tex.Apply(false, true);
            return _spineMat = Overlay("CodexSpine", tex, 3098);
        }
        public static Material StackMaterial()
        {
            if (_stackMat != null) return _stackMat;
            const int w = 8, h = 64; var px = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = (y % 4 == 0) ? new Color(0.70f, 0.62f, 0.46f, 1f) : new Color(0.90f, 0.84f, 0.68f, 1f);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "CodexPageBlock", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px); tex.Apply(false, true);
            return _stackMat = Overlay("CodexPageBlock", tex, 3099);
        }

        // ------------------------------------------------------------------ the turning sheets
        private sealed class Sheet { public Transform T; public Mesh Mesh; public MeshRenderer R; public Vector3[] V; }
        private readonly List<Sheet> _sheets = new List<Sheet>();
        private const int SheetCols = 12;
        private void BuildSheets()
        {
            var mat = SheetMaterial();
            for (int i = 0; i < RiffleSheets; i++)
            {
                var go = new GameObject("sheet" + i); go.layer = UiKit.PanelLayer; go.transform.SetParent(_body, false);
                var s = new Sheet { T = go.transform, Mesh = new Mesh { name = "CodexSheet" }, V = new Vector3[(SheetCols + 1) * 2] };
                s.Mesh.MarkDynamic();
                var uv = new Vector2[s.V.Length]; var tri = new int[SheetCols * 6];
                for (int c = 0; c <= SheetCols; c++) { uv[2 * c] = new Vector2(c / (float)SheetCols, 0f); uv[2 * c + 1] = new Vector2(c / (float)SheetCols, 1f); }
                for (int c = 0; c < SheetCols; c++) { int a = 2 * c; tri[6 * c] = a; tri[6 * c + 1] = a + 1; tri[6 * c + 2] = a + 3; tri[6 * c + 3] = a; tri[6 * c + 4] = a + 3; tri[6 * c + 5] = a + 2; }
                LaySheet(s, 0f, 0f); s.Mesh.uv = uv; s.Mesh.triangles = tri;
                go.AddComponent<MeshFilter>().sharedMesh = s.Mesh;
                s.R = go.AddComponent<MeshRenderer>(); s.R.sharedMaterial = mat; s.R.sortingOrder = 11;
                s.R.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; s.R.receiveShadows = false;
                go.SetActive(false);
                _sheets.Add(s);
            }
        }
        /// <summary>A sheet at angle <paramref name="deg"/> about the spine (the right page's place: +fold; the left's: 180 - fold), bowed by
        /// <paramref name="bow"/> (0 flat, 1 the most: in the middle of its turn).</summary>
        private static void LaySheet(Sheet s, float deg, float bow)
        {
            s.T.localRotation = Quaternion.Euler(0f, deg, 0f);
            for (int c = 0; c <= SheetCols; c++)
            {
                float u = c / (float)SheetCols, x = Gutter / 2 + u * PageW;
                float z = -bow * PageW * 0.16f * Mathf.Sin(Mathf.PI * u) + bow * PageW * 0.10f * u * u; // (its middle leads, its free edge lags)
                s.V[2 * c] = new Vector3(x, -PageH / 2, z); s.V[2 * c + 1] = new Vector3(x, PageH / 2, z);
            }
            s.Mesh.vertices = s.V; s.Mesh.RecalculateBounds();
        }
        private void HideSheets() { foreach (var s in _sheets) if (s.T.gameObject.activeSelf) s.T.gameObject.SetActive(false); }
        private void TickTurn()
        {
            var t = _turn; float now = Time.unscaledTime - t.At;
            float each = t.Sheets > 1 ? RiffleSheet : TurnSeconds, gap = t.Sheets > 1 ? RiffleStagger : 0f;
            for (int i = 0; i < _sheets.Count; i++)
            {
                var s = _sheets[i];
                if (i >= t.Sheets) { if (s.T.gameObject.activeSelf) s.T.gameObject.SetActive(false); continue; }
                float k = Mathf.Clamp01((now - i * gap) / each);
                bool on = k > 0f && k < 1f;
                if (s.T.gameObject.activeSelf != on) s.T.gameObject.SetActive(on);
                if (!on) continue;
                float e = k * k * (3f - 2f * k);
                float from = t.Dir > 0 ? FoldDeg : 180f - FoldDeg, to = t.Dir > 0 ? 180f - FoldDeg : FoldDeg;
                LaySheet(s, Mathf.Lerp(from, to, e), Mathf.Sin(Mathf.PI * k) * (t.Dir > 0 ? 1f : -1f));
            }
            if (now >= t.Seconds) FinishTurn();
        }
        private static Material SheetMaterial()
        {
            if (_sheetMat != null) return _sheetMat;
            const int w = 64, h = 96; var px = new Color32[w * h];
            var paper = new Color(0.96f, 0.91f, 0.78f, 1f); var line = new Color(0.62f, 0.55f, 0.45f, 1f); var edge = new Color(0.80f, 0.70f, 0.50f, 1f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = paper;
                    bool written = x > 6 && x < w - 6 && y > 8 && y < h - 10 && y % 6 == 0 && ((x * 7 + y * 3) % 23) > 3;
                    if (written) c = Color.Lerp(paper, line, 0.55f);
                    if (x < 2 || x >= w - 2 || y < 2 || y >= h - 2) c = edge;
                    px[y * w + x] = c;
                }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "CodexSheet", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px); tex.Apply(false, true);
            return _sheetMat = Overlay("CodexSheet", tex, 3101);
        }
    }
}
