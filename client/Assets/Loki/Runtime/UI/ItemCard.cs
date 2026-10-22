using System.Text;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 11 (w11menu): one item as a card: its picture, its full name, a line saying what the card is ("worn: armour",
    /// "in your bag"), then a scrolling text (UI/UiScrollText.cs) with the equipment block (where it goes, refine, grade, cards,
    /// random options: WristMenu.W3EquipBlock) and the client data's description. Used for the worn items beside an item's details
    /// (the design's 2026-10-06 playtest: "when comparing an item both of their stats and names and descriptions should be open
    /// simultaneously") and by the new menu's item grid. A card stops the laser (a panel background) and presses nothing.
    /// </summary>
    public sealed class ItemCard
    {
        public RectTransform Root { get; private set; }
        public UiScrollText Body { get; private set; }
        private Image _icon; private Text _name, _head;
        /// <summary>The inventory index of the item shown (-1: none) and its texts (smoke, log).</summary>
        public int Index { get; private set; } = -1;
        public string NameText => _name != null ? _name.text : "";
        public string HeadText => _head != null ? _head.text : "";
        public string BodyText => Body != null ? Body.Text.text : "";
        public bool Shown => Root != null && Root.gameObject.activeInHierarchy;

        /// <summary>large: the form for the new menu's board (seen from further away and from the side): no text under the given size less one,
        /// no small scroll buttons (the stick and the trigger on the text scroll it; the mark shows where).</summary>
        public static ItemCard Create(RectTransform parent, string name, Vector2 pos, Vector2 size, int fontSize = 20, bool large = false)
        {
            var c = new ItemCard();
            // client wave 21 (w21kit): an item card is a parchment card of the theme (UiKit.Dress: its "border" is the paper)
            c.Root = UiKit.Box(parent, name, pos, size, new Color(1, 1, 1, 0));
            c.Root.GetComponent<UnityEngine.UI.Image>().enabled = false;
            UiKit.Dress(c.Root, size, Mathf.Max(1e-5f, c.Root.lossyScale.x), PanelLook.Card);
            var col = c.Root.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(size.x, size.y, 4f); col.center = new Vector3(0, 0, 3f); // the laser stops on a card (UiKit.Panel does the same)
            c.Root.gameObject.AddComponent<UiBlocker>();
            float top = size.y / 2, w = size.x;
            c._icon = UiKit.Icon(c.Root, new Vector2(-w / 2 + 48, top - 50), new Vector2(60, 60));
            c._head = UiKit.Label(c.Root, "", new Vector2(40, top - 26), new Vector2(w - 120, 32), large ? fontSize : Mathf.RoundToInt(fontSize * 0.9f), TextAnchor.MiddleLeft, UiKit.Dim);
            c._head.resizeTextForBestFit = true; c._head.resizeTextMinSize = large ? fontSize - 1 : 13; c._head.resizeTextMaxSize = c._head.fontSize;
            c._name = UiKit.Label(c.Root, "", new Vector2(40, top - 68), new Vector2(w - 120, 56), fontSize + 4, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            c._name.resizeTextForBestFit = true; c._name.resizeTextMinSize = large ? fontSize - 1 : 14; c._name.resizeTextMaxSize = c._name.fontSize;
            UiKit.Box(c.Root, "rule", new Vector2(0, top - 100), new Vector2(w - 30, 2), UiKit.PanelBorder * 0.7f);
            c.Body = UiScrollText.Create(c.Root, new Vector2(4, -50), new Vector2(w - 28, size.y - 118), fontSize, null, !large);
            return c;
        }

        /// <summary>The equipment block first, then the description (the stat lines of a Ragnarok Online description are at its end:
        /// the text scrolls). statsFirst (the new menu's cards): the description's last part, the lines after its last rule
        /// ("-----" or "_____": type, attack / defence, weight, level, classes), is put before its first part.</summary>
        public static string BodyOf(Inventory.Entry e, bool statsFirst = false)
        {
            if (e == null) return "";
            var sb = new StringBuilder();
            sb.Append(WristMenu.W3EquipBlock(e));
            var info = e.Info;
            if (string.IsNullOrEmpty(info.Description)) { sb.Append(Lang.T("<color=#8890a0>No description in the client data.</color>")); return sb.ToString(); }
            string d = info.Description;
            if (statsFirst)
            {
                var lines = d.Replace("\r", "").Split('\n'); int rule = -1;
                for (int i = lines.Length - 1; i > 0; i--) if (IsRule(lines[i])) { rule = i; break; }
                if (rule > 0 && rule < lines.Length - 1)
                {
                    // (waves 27-28 merge: the data's links as their names, Game/NpcDialog.cs DataText; RoColors alone showed their tags)
                    sb.Append(NpcDialog.DataText(string.Join("\n", lines, rule + 1, lines.Length - rule - 1))).Append('\n');
                    sb.Append("<color=#555c6a>────────</color>\n");
                    sb.Append(NpcDialog.DataText(string.Join("\n", lines, 0, rule)));
                    return sb.ToString();
                }
            }
            sb.Append(NpcDialog.DataText(d));
            return sb.ToString();
        }

        /// <summary>A line of the description that is only a rule: dashes / underscores (colour codes aside).</summary>
        private static bool IsRule(string line)
        {
            int n = 0;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '^' && i + 6 < line.Length) { i += 6; continue; }
                if (c == '-' || c == '_' || c == '=') n++;
                else if (!char.IsWhiteSpace(c)) return false;
            }
            return n >= 6;
        }

        /// <summary>"type · weight · slots · level": the details page's own line.</summary>
        public static string InfoLine(Inventory.Entry e)
        {
            var info = e.Info;
            return info.TypeName + "   ·   " + Lang.F("weight {0:0.#}", info.Weight / 10f)
                 + (info.Slots > 0 ? "   ·   " + (info.Slots > 1 ? Lang.F("{0} slots", info.Slots) : Lang.T("1 slot")) : "") + (info.MinLevel > 0 ? "   ·   " + Lang.F("level {0}+", info.MinLevel) : "");
        }

        public void Set(Inventory.Entry e, string head, bool statsFirst = false)
        {
            if (e == null) { Clear(); return; }
            Index = e.Index;
            _icon.sprite = WristMenu.ItemIcon(e.Info); _icon.enabled = _icon.sprite != null;
            _head.text = head ?? "";
            _name.text = Inventory.FullName(e) + (e.Amount > 1 ? $"  <color=#ffd060>x{e.Amount}</color>" : "");
            Body.Text.text = InfoLine(e) + "\n" + BodyOf(e, statsFirst);
            if (!Root.gameObject.activeSelf) Root.gameObject.SetActive(true);
        }

        /// <summary>A card that only says something (nothing is worn there).</summary>
        public void SetNote(string head, string text)
        {
            Index = -1; _icon.enabled = false; _head.text = head ?? ""; _name.text = ""; Body.Text.text = text ?? "";
            if (!Root.gameObject.activeSelf) Root.gameObject.SetActive(true);
        }

        public void Clear() { Index = -1; if (Root.gameObject.activeSelf) Root.gameObject.SetActive(false); }
    }
}
