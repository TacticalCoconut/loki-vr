using System.Collections.Generic;
using System.Text;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 13 (w13items), the brief's eighth item (design note, 2026-10-07: "the entire description and stats needs to be visible, so
    /// just use a panel on the right to list out exactly what the item is and what it does ... Both of them need to be visible in their
    /// entirety"): one item shown WHOLE, with no scrolling and nothing cut. Used beside the new menu's bag (UI/WristMenu.Board.cs): the
    /// item panel, and the comparison panel(s) of what is worn where it would go.
    /// What it holds, top to bottom: a head line (what the panel is: "In your bag:", "You wear at …:", "Locked · …"), the icon and the
    /// full name (refine, cards, options), the "type · weight · slots · level" line, an optional note line, an optional row of buttons
    /// (the owner's: Equip / Use, Details, ★, Unlock), a rule, then the body: the equipment block and the whole description (its stat
    /// lines first, as wave 11's cards).
    /// THE FIT (<see cref="Fit"/>): the body is measured with Unity's own text generator, source line by source line, and packed into
    /// columns of <see cref="TextW"/> px:
    ///   1. one column, the panel growing in height up to <see cref="MaxH"/> px (the board is 830 px tall), the font from
    ///      <see cref="FontMax"/> down to <see cref="FontFloor"/> px;
    ///   2. else two columns, then three (each column count tried from the largest font down to the floor), the columns balanced;
    ///   3. past <see cref="MaxColumns"/> columns at the floor the panel grows taller than <see cref="MaxH"/>: never cut.
    /// A column after the first is a box of its own (a FACET): the owner stands each facet on the circle around the eyes, facing them,
    /// so a wide panel bends round the player instead of running away flat (the letters of a flat panel's far edge would be a third
    /// further away). The first facet holds the header and the first column.
    /// The floor: 21 px at the board's 0.00074 m per pixel is 1.55 cm: 1.30° from 0.685 m (wave 11's legibility number, measured as
    /// wave 11 measures: the letters' height over the distance from the eyes to the text block's middle; a full-height column's middle
    /// is 0.68 m from the eyes at the default place). The background is the board's (UiKit.PanelBg in a gold border) and only as
    /// large as the text: no empty column. A collider with a UiBlocker covers each facet: the laser stops on it, the look does not
    /// reach through it.
    /// </summary>
    public sealed class ItemPanel
    {
        public const float TextW = 408f, Pad = 16f, ColGap = 22f, IconPx = 64f, ButtonH = 96f, MaxH = 1110f, HeadFont = 22, NameFont = 28, InfoFont = 22;
        public const int FontMax = 24, FontFloor = 21, MaxColumns = 3;
        /// <summary>This panel's smallest body font (px): <see cref="FontFloor"/> unless its owner stands it farther from the eyes than the
        /// boards' cards (client wave 23, the merge: the upright inventory's cards, UI/InvPanel.cs CardFontFloor).</summary>
        public int Floor { get; set; } = FontFloor;
        /// <summary>One facet's width in px (a column and its margins).</summary>
        public const float BoxW = TextW + 2 * Pad;
        public RectTransform Root { get; private set; }
        /// <summary>The owner's buttons go here (a row under the info line).</summary>
        public RectTransform ButtonRow { get; private set; }
        private RectTransform _parent, _bg; private BoxCollider _col;
        private Image _icon; private Text _head, _name, _info, _note; private RectTransform _rule;
        private readonly List<Text> _cols = new List<Text>();
        private readonly List<RectTransform> _facets = new List<RectTransform>(); // facet 0 = Root
        private bool _buttons; private string _key = "", _name0 = "";

        /// <summary>The inventory index shown (-1: none), the texts (smoke, log), the fit's outcome.</summary>
        public int Index { get; private set; } = -1;
        public string HeadText => _head != null ? _head.text : "";
        public string NameText => _name != null ? _name.text : "";
        public string NoteText => _note != null ? _note.text : "";
        /// <summary>The whole body as it was given (the columns' texts are its lines, in order, with their colour tags balanced).</summary>
        public string BodyText { get; private set; } = "";
        public bool Shown => Root != null && Root.gameObject.activeInHierarchy;
        /// <summary>All facets side by side, in px (the owner adds the gaps on the circle).</summary>
        public float Width => Columns * BoxW;
        public float Height { get; private set; }
        public int Columns { get; private set; }
        public int FontUsed { get; private set; }
        /// <summary>The fit went past <see cref="MaxH"/> (more than <see cref="MaxColumns"/> columns would have been needed).</summary>
        public bool Overgrown { get; private set; }
        public IReadOnlyList<Text> ColumnTexts => _cols;
        /// <summary>Facet i's box (0 = the header's; null past <see cref="Columns"/>).</summary>
        public RectTransform Facet(int i) => i >= 0 && i < Columns && i < _facets.Count ? _facets[i] : null;

        public static ItemPanel Create(RectTransform parent, string name, bool buttons)
        {
            var p = new ItemPanel { _buttons = buttons, _parent = parent, _name0 = name };
            p.Root = p.NewBox(name);
            p._bg = (RectTransform)p.Root.Find("bg"); p._col = p.Root.GetComponent<BoxCollider>();
            p._head = UiKit.Label(p.Root, "", Vector2.zero, new Vector2(TextW, 30), (int)HeadFont, TextAnchor.UpperLeft, UiKit.Dim);
            p._icon = UiKit.Icon(p.Root, Vector2.zero, new Vector2(IconPx, IconPx));
            p._name = UiKit.Label(p.Root, "", Vector2.zero, new Vector2(TextW - IconPx - 12, 40), (int)NameFont, TextAnchor.UpperLeft, new Color(1f, 0.85f, 0.45f));
            p._info = UiKit.Label(p.Root, "", Vector2.zero, new Vector2(TextW, 30), (int)InfoFont, TextAnchor.UpperLeft, UiKit.Dim);
            p._note = UiKit.Label(p.Root, "", Vector2.zero, new Vector2(TextW, 30), (int)InfoFont, TextAnchor.UpperLeft, new Color(0.5f, 0.84f, 0.5f));
            p.ButtonRow = UiKit.Box(p.Root, "buttons", Vector2.zero, new Vector2(TextW, ButtonH), new Color(0, 0, 0, 0));
            p.ButtonRow.gameObject.SetActive(buttons);
            p._rule = UiKit.Box(p.Root, "rule", Vector2.zero, new Vector2(TextW, 2), UiKit.PanelBorder * 0.7f);
            foreach (var t in new[] { p._head, p._name, p._info, p._note }) { t.verticalOverflow = VerticalWrapMode.Overflow; t.horizontalOverflow = HorizontalWrapMode.Wrap; }
            p._facets.Add(p.Root);
            p.Root.gameObject.SetActive(false);
            return p;
        }

        /// <summary>A facet's box: the board's background in a gold border, a collider and a UiBlocker (the laser stops, the look too).</summary>
        private RectTransform NewBox(string name)
        {
            // client wave 21 (w21kit): a facet is a parchment card of the theme (UiKit.Dress: its "border" is the paper, its "bg" an
            // unused inner box kept for the sizes)
            var b = UiKit.Box(_parent, name, Vector2.zero, new Vector2(BoxW, 300), new Color(1, 1, 1, 0));
            b.GetComponent<UnityEngine.UI.Image>().enabled = false;
            UiKit.Dress(b, new Vector2(BoxW, 300), Mathf.Max(1e-5f, b.lossyScale.x), PanelLook.Card);
            var col = b.gameObject.AddComponent<BoxCollider>(); col.center = new Vector3(0, 0, 3f); col.size = new Vector3(BoxW, 300, 4f);
            b.gameObject.AddComponent<UiBlocker>();
            return b;
        }

        private Text Column(int i)
        {
            while (_cols.Count <= i)
            {
                int k = _cols.Count;
                RectTransform box = k == 0 ? Root : null;
                if (box == null) { box = NewBox(_name0 + "+" + k); box.gameObject.SetActive(false); _facets.Add(box); }
                var t = UiKit.Label(box, "", Vector2.zero, new Vector2(TextW, 100), FontMax, TextAnchor.UpperLeft, UiKit.TextColor);
                t.name = "col" + k;
                t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow; // never cut: the box is sized to it
                t.resizeTextForBestFit = false;
                _cols.Add(t);
            }
            return _cols[i];
        }

        public void Clear()
        {
            Index = -1; _key = "";
            foreach (var f in _facets) if (f.gameObject.activeSelf) f.gameObject.SetActive(false);
        }

        /// <summary>The item, whole: <paramref name="head"/> says what the panel is; <paramref name="note"/> an extra line under the info
        /// line (null: none). Laid out at once (<see cref="Fit"/>); the same item with the same texts is not laid out again.</summary>
        public void Set(Inventory.Entry e, string head, string note = null)
        {
            if (e == null) { Clear(); return; }
            Index = e.Index;
            string name = Inventory.FullName(e) + (e.Amount > 1 ? $"  <color=#ffd060>x{e.Amount}</color>" : "");
            string info = ItemCard.InfoLine(e), body = ItemCard.BodyOf(e, true);
            string key = head + "\u0001" + name + "\u0001" + info + "\u0001" + note + "\u0001" + body;
            if (!Root.gameObject.activeSelf) Root.gameObject.SetActive(true);
            if (key != _key)
            {
                _key = key;
                _icon.sprite = WristMenu.ItemIcon(e.Info); _icon.enabled = _icon.sprite != null;
                _head.text = head ?? ""; _name.text = name; _info.text = info; _note.text = note ?? ""; BodyText = body;
                Fit();
            }
            for (int i = 1; i < _facets.Count; i++) { bool on = i < Columns; if (_facets[i].gameObject.activeSelf != on) _facets[i].gameObject.SetActive(on); }
        }

        // ------------------------------------------------------------------ measuring
        private static readonly TextGenerator Gen = new TextGenerator();
        /// <summary>The height in px that <paramref name="s"/> takes at <paramref name="width"/> px and <paramref name="font"/> px, as
        /// <paramref name="like"/> would draw it (its font, rich text, wrapping; nothing cut).</summary>
        public static float Measure(Text like, string s, float width, int font)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            var set = Settings(like, width, font);
            float ppu = like.pixelsPerUnit > 0 ? like.pixelsPerUnit : 1f;
            return Gen.GetPreferredHeight(s, set) / ppu;
        }
        /// <summary>The lines the generator makes of <paramref name="s"/> at that width and font (the smoke compares them with the text's).</summary>
        public static int LineCount(Text like, string s, float width, int font)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            Gen.Populate(s, Settings(like, width, font));
            return Gen.lineCount;
        }
        private static TextGenerationSettings Settings(Text like, float width, int font)
        {
            var set = like.GetGenerationSettings(new Vector2(width, 100000f));
            set.fontSize = font; set.resizeTextForBestFit = false; set.verticalOverflow = VerticalWrapMode.Overflow; set.horizontalOverflow = HorizontalWrapMode.Wrap;
            set.generationExtents = new Vector2(width, 100000f);
            return set;
        }

        private static readonly System.Text.RegularExpressions.Regex Tag = new System.Text.RegularExpressions.Regex(@"<(/?)(color|b|i|size)(=[^>]*)?>", System.Text.RegularExpressions.RegexOptions.Compiled);
        /// <summary>The body's source lines, each with the rich-text tags open at its start re-opened before it and closed after it, so
        /// any run of lines is a balanced text on its own (a colour of the game data may run over several lines).</summary>
        public static List<string> BalancedLines(string body)
        {
            var src = (body ?? "").Replace("\r", "").Split('\n');
            var open = new List<string>(); var outl = new List<string>(src.Length);
            foreach (var line in src)
            {
                var sb = new StringBuilder();
                foreach (var o in open) sb.Append(o);
                sb.Append(line);
                foreach (System.Text.RegularExpressions.Match m in Tag.Matches(line))
                {
                    if (m.Groups[1].Value == "/") { for (int i = open.Count - 1; i >= 0; i--) if (open[i].StartsWith("<" + m.Groups[2].Value)) { open.RemoveAt(i); break; } }
                    else open.Add(m.Value);
                }
                for (int i = open.Count - 1; i >= 0; i--) sb.Append("</").Append(TagName(open[i])).Append('>');
                outl.Add(sb.ToString());
            }
            return outl;
        }
        private static string TagName(string open) { int e = open.IndexOfAny(new[] { '=', '>' }); return open.Substring(1, e - 1); }

        // ------------------------------------------------------------------ the fit
        private float _headH;
        /// <summary>The header's height in px (head line to rule; the first facet's body starts under it).</summary>
        public float HeaderHeight => _headH;

        /// <summary>Greedy packing of line heights into columns: the first under the header (panel height h), the others full height (a
        /// line taller than its column stands alone): the start index of each column.</summary>
        private List<int> Pack(float[] hs, float h)
        {
            var starts = new List<int> { 0 }; float acc = 0f, limit = h - _headH - Pad;
            for (int i = 0; i < hs.Length; i++)
            {
                if (acc > 0f && acc + hs[i] > limit + 0.5f) { starts.Add(i); acc = 0f; limit = h - 2 * Pad; }
                acc += hs[i];
            }
            return starts;
        }

        /// <summary>Measures and lays out the panel (see the class): <see cref="Columns"/> facets of <see cref="BoxW"/> × <see cref="Height"/> px.</summary>
        public void Fit()
        {
            // the header: head line, icon + name, info line, note, buttons, rule
            float y = Pad;
            float hh = Measure(_head, _head.text, TextW, (int)HeadFont); Place(_head, Pad, y, TextW, hh); y += hh > 0 ? hh + 4 : 0;
            float nh = Mathf.Max(Measure(_name, _name.text, TextW - IconPx - 12, (int)NameFont), 34f);
            Place(_icon.rectTransform, Pad, y, IconPx, IconPx); Place(_name, Pad + IconPx + 12, y + Mathf.Max(0f, (IconPx - nh) / 2f), TextW - IconPx - 12, nh);
            y += Mathf.Max(IconPx, nh) + 6;
            float ih = Measure(_info, _info.text, TextW, (int)InfoFont); Place(_info, Pad, y, TextW, ih); y += ih + 4;
            float th = Measure(_note, _note.text, TextW, (int)InfoFont); Place(_note, Pad, y, TextW, th); y += th > 0 ? th + 4 : 0;
            if (_buttons) { y += 6; Place(ButtonRow, Pad, y, TextW, ButtonH); y += ButtonH + 8; }
            y += 4; Place(_rule, Pad, y, TextW, 2); y += 12;
            _headH = y;

            var lines = BalancedLines(BodyText);
            var like = Column(0);
            int cols = 0, font = Floor; List<int> starts = null; float[] hs = null;
            var byFont = new Dictionary<int, float[]>(); // a line's height does not depend on the column count
            for (int c = 1; c <= MaxColumns && cols == 0; c++)
                for (int f = FontMax; f >= Floor; f--)
                {
                    if (!byFont.TryGetValue(f, out hs)) byFont[f] = hs = Heights(like, lines, f);
                    if (Pack(hs, MaxH).Count > c) continue;
                    // the lowest panel that still packs into c columns: the columns balanced
                    float lo = _headH + Pad, hi = MaxH;
                    for (int k = 0; k < 24; k++) { float mid = (lo + hi) / 2f; if (Pack(hs, mid).Count <= c) hi = mid; else lo = mid; }
                    starts = Pack(hs, hi); cols = starts.Count; font = f;
                    break;
                }
            Overgrown = cols == 0;
            if (cols == 0)
            {
                // past MaxColumns at the floor: MaxColumns balanced columns, as tall as they need (never cut)
                font = Floor; if (!byFont.TryGetValue(font, out hs)) hs = Heights(like, lines, font);
                float total = 0f; foreach (var v in hs) total += v;
                float lo = MaxH, hi = MaxH + total + _headH;
                for (int k = 0; k < 30; k++) { float mid = (lo + hi) / 2f; if (Pack(hs, mid).Count <= MaxColumns) hi = mid; else lo = mid; }
                starts = Pack(hs, hi); cols = starts.Count;
            }
            // the columns' texts, measured again as they will be drawn; every facet as tall as the tallest need
            float need = 0f; var heights = new float[cols];
            for (int c = 0; c < cols; c++)
            {
                var t = Column(c); t.fontSize = font;
                int a = starts[c], b = c + 1 < starts.Count ? starts[c + 1] : lines.Count;
                t.text = string.Join("\n", lines.GetRange(a, b - a));
                heights[c] = Measure(t, t.text, TextW, font) + 8f; // (a margin: the drawn lines never reach past the box)
                need = Mathf.Max(need, (c == 0 ? _headH : Pad) + heights[c] + Pad);
            }
            Columns = cols; FontUsed = font; Height = need;
            for (int c = 0; c < _cols.Count; c++)
            {
                bool on = c < cols; var t = _cols[c];
                if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
                if (!on) continue;
                Place(t, Pad, c == 0 ? _headH : Pad, TextW, heights[c]);
                var box = _facets[c];
                box.sizeDelta = new Vector2(BoxW, Height);
                ((RectTransform)box.Find("bg")).sizeDelta = new Vector2(BoxW - 6, Height - 6);
                var paper = box.Find("border") as RectTransform; if (paper != null) paper.sizeDelta = new Vector2(BoxW, Height); // (client wave 21: the card's paper)
                box.GetComponent<BoxCollider>().size = new Vector3(BoxW, Height, 4f);
            }
            _rule.sizeDelta = new Vector2(TextW, 2);
        }

        private static float[] Heights(Text like, List<string> lines, int font)
        {
            var h = new float[lines.Count];
            float one = Measure(like, "Ag", TextW, font);
            for (int i = 0; i < lines.Count; i++) { float v = Measure(like, lines[i], TextW, font); h[i] = v > 0.5f ? v : one; }
            return h;
        }

        /// <summary>Puts a child with its top left corner at (x, y) from its box's top left corner (y downwards), w × h px; its own pivot
        /// is its middle (so its position is the text block's middle, where wave 11's legibility is measured).</summary>
        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            h = Mathf.Max(h, 1f);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x + w / 2f, -(y + h / 2f)); rt.sizeDelta = new Vector2(w, h);
        }
        private static void Place(Text t, float x, float y, float w, float h) => Place((RectTransform)t.transform, x, y, w, h);

    }
}
