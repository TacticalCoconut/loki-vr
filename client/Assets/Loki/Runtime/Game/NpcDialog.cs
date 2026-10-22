using System;
using System.Collections.Generic;
using System.Text;
using Loki.Net;

namespace Loki.Game
{
    /// <summary>
    /// NPC script dialog state machine, following the client behaviour documented in rAthena clif.cpp:
    ///   0x00b4 ZC_SAY_DIALOG   len.W npc.L text   — append a line (after 'next' was pressed, the window clears first)
    ///   0x00b5 ZC_WAIT_DIALOG  npc.L              — show 'next'  → CZ_REQ_NEXT_SCRIPT 0x00b9
    ///   0x00b6 ZC_CLOSE_DIALOG npc.L              — show 'close' → CZ_CLOSE_DIALOG 0x0146 (closes at once if no window)
    ///   0x08d6 ZC_CLEAR_DIALOG npc.L              — script ended by idle timeout: close
    ///   0x00b7 ZC_MENU_LIST    len.W npc.L "a:b:c" — choose → CZ_CHOOSE_MENU 0x00b8 (1-based index, 0xff = cancel)
    ///   0x0142 ZC_OPEN_EDITDLG npc.L              — number input → CZ_INPUT_EDITDLG 0x0143
    ///   0x01d4 ZC_OPEN_EDITDLGSTR npc.L           — text input   → CZ_INPUT_EDITDLGSTR 0x01d5
    ///   0x01b3 ZC_SHOW_IMAGE   image.64B type.B   — cut-in illustration (type 255 = remove)
    /// </summary>
    public sealed class NpcDialog
    {
        public enum Mode { Closed, Talking, Next, Close, Menu, InputNumber, InputString }
        public Mode State { get; private set; } = Mode.Closed;
        public uint NpcId { get; private set; }
        public string NpcName = "";
        public readonly List<string> Lines = new List<string>();
        /// <summary>Menu options with their 1-based index as the server counts them (empty entries are hidden but still counted).</summary>
        public readonly List<KeyValuePair<int, string>> Options = new List<KeyValuePair<int, string>>();
        public string CutIn { get; private set; } // image name or null
        public int CutInPosition { get; private set; }
        public string Input = "";
        public bool IsOpen => State != Mode.Closed;
        public event Action Changed;

        private bool _clearOnNextSay;
        private bool _saidSinceStep;
        private readonly RoSession _s;
        private readonly Func<uint, string> _nameOf;

        public NpcDialog(RoSession s, Func<uint, string> nameOf) { _s = s; _nameOf = nameOf; }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x00b4:
                {
                    r.Skip(4); uint npc = r.U32(); string text = r.Text(n - 8); // cp1252 (Net/RoText.cs)
                    Open(npc);
                    if (_clearOnNextSay) { Lines.Clear(); _clearOnNextSay = false; }
                    Lines.Add(text);
                    _saidSinceStep = true;
                    if (State == Mode.Closed || State == Mode.Next || State == Mode.Close) State = Mode.Talking;
                    Changed?.Invoke();
                    return true;
                }
                case 0x00b5: { r.Skip(2); Open(r.U32()); State = Mode.Next; Changed?.Invoke(); return true; }
                case 0x00b6:
                {
                    r.Skip(2); uint npc = r.U32();
                    if (State == Mode.Closed || !_saidSinceStep) { _s.NpcClose(npc); CloseLocal(); return true; } // no window or no new mes since last step: acknowledge and close
                    NpcId = npc; State = Mode.Close; Changed?.Invoke();
                    return true;
                }
                case 0x08d6: { CloseLocal(); return true; }
                case 0x00b7:
                {
                    r.Skip(4); uint npc = r.U32(); string menu = r.Text(n - 8);
                    Open(npc);
                    Options.Clear();
                    var parts = menu.Split(':');
                    for (int i = 0; i < parts.Length; i++) if (parts[i].Length > 0) Options.Add(new KeyValuePair<int, string>(Options.Count + 1, parts[i]));
                    State = Mode.Menu; Changed?.Invoke();
                    return true;
                }
                case 0x0142: { r.Skip(2); Open(r.U32()); Input = ""; State = Mode.InputNumber; Changed?.Invoke(); return true; }
                case 0x01d4: { r.Skip(2); Open(r.U32()); Input = ""; _caretFromEnd = 0; State = Mode.InputString; Changed?.Invoke(); return true; }
                case 0x01b3:
                {
                    r.Skip(2); string img = r.Str(64); int type = r.U8();
                    if (type == 255 || string.IsNullOrEmpty(img)) CutIn = null; else { CutIn = img; CutInPosition = type; }
                    Changed?.Invoke();
                    return true;
                }
                default: return false;
            }
        }

        private void Open(uint npc)
        {
            if (State == Mode.Closed || npc != NpcId) { Lines.Clear(); Options.Clear(); _clearOnNextSay = false; _saidSinceStep = false; }
            if (State == Mode.Closed) State = Mode.Talking;
            NpcId = npc;
            NpcName = CleanName(_nameOf?.Invoke(npc) ?? "");
        }

        /// <summary>rAthena NPC names can carry a hidden part after '#' (unique id) — the client shows only the part before it.</summary>
        public static string CleanName(string name) { int h = name.IndexOf('#'); return h >= 0 ? name.Substring(0, h) : name; }

        // ---- player actions ----
        public void PressNext() { if (State != Mode.Next) return; _s.NpcNext(NpcId); _clearOnNextSay = true; _saidSinceStep = false; State = Mode.Talking; Changed?.Invoke(); }
        public void PressClose()
        {
            if (State == Mode.Closed) return;
            if (State == Mode.Menu) { CancelMenu(); return; }
            _s.NpcClose(NpcId); CloseLocal();
        }
        public void Choose(int index1Based)
        {
            if (State != Mode.Menu || index1Based <= 0 || index1Based >= 0xff) return;
            _s.NpcMenu(NpcId, (byte)index1Based);
            Options.Clear(); _saidSinceStep = false; State = Mode.Talking; Changed?.Invoke(); // per rAthena's notes only 'next' makes the following mes clear the window
        }
        public void CancelMenu()
        {
            if (State != Mode.Menu) return;
            _s.NpcMenu(NpcId, 0xff); // the server ends the script on cancel
            CloseLocal();
        }
        public void SubmitInput()
        {
            if (State == Mode.InputNumber)
            {
                int v = 0; int.TryParse(Input, out v);
                _s.NpcInputNumber(NpcId, v);
            }
            else if (State == Mode.InputString)
            {
                if (Input.Length == 0) return; // the client only sends when there is text
                _s.NpcInputString(NpcId, Input);
            }
            else return;
            Input = ""; _saidSinceStep = false; State = Mode.Talking; Changed?.Invoke();
        }
        public void TypeChar(char c)
        {
            if (State == Mode.InputNumber) { if ((c >= '0' && c <= '9') || (c == '-' && Input.Length == 0)) { if (Input.Length < 10) Input += c; Changed?.Invoke(); } }
            else if (State == Mode.InputString) { if (Input.Length < 70) { int at = Caret; Input = Input.Insert(at, c.ToString()); _caretFromEnd = Input.Length - (at + 1); Changed?.Invoke(); } }
        }
        public void Backspace() { int at = Caret; if (at > 0) { Input = Input.Remove(at - 1, 1); _caretFromEnd = Input.Length - (at - 1); Changed?.Invoke(); } }
        /// <summary>Where the next character of a text prompt goes (client wave 3, w3login: the VR keyboard's ◄ ►). Kept as the
        /// distance from the end, so a prompt that resets <see cref="Input"/> puts it back at the end by itself.</summary>
        public int Caret => State == Mode.InputString ? Math.Max(0, Input.Length - _caretFromEnd) : Input.Length;
        private int _caretFromEnd;
        /// <summary>Moves the text prompt's caret: −1 / +1, int.MinValue home, int.MaxValue end (number prompts keep it at the end).</summary>
        public void MoveCaret(int delta)
        {
            if (State != Mode.InputString) return;
            int at = delta == int.MinValue ? 0 : delta == int.MaxValue ? Input.Length : Math.Max(0, Math.Min(Input.Length, Caret + delta));
            _caretFromEnd = Input.Length - at; Changed?.Invoke();
        }
        public void AddNumber(int delta)
        {
            if (State != Mode.InputNumber) return;
            long v = 0; long.TryParse(Input, out v); v = Math.Max(0, Math.Min(int.MaxValue, v + delta)); Input = v.ToString(); Changed?.Invoke();
        }
        public bool WantsKeyboard => State == Mode.InputNumber || State == Mode.InputString;
        /// <summary>Hides the window without telling the server — for scripts that hand over to a shop or storage window
        /// (callshop suppresses the close packet, rAthena script.cpp:10512-10517).</summary>
        public void DismissLocal() { if (State != Mode.Closed) CloseLocal(); }

        private void CloseLocal()
        {
            State = Mode.Closed; Lines.Clear(); Options.Clear(); Input = ""; CutIn = null; _clearOnNextSay = false; _saidSinceStep = false;
            Changed?.Invoke();
        }

        public string Text()
        {
            var sb = new StringBuilder();
            foreach (var l in Lines) sb.Append(l).Append('\n');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ links in an NPC's text
        /// <summary>
        /// A link rAthena's scripts write into mes / select text (specified after the 2026-10-01 playtest: the "directional icons"
        /// showed as their raw tags; rAthena's own scripts use them in 15 files, 72 in the Academy, npc/re/jobs/novice/academy.txt):
        ///   &lt;NAVI&gt;[name]&lt;INFO&gt;map,x,y[,…]&lt;/INFO&gt;&lt;/NAVI&gt;   a place: the official client and roBrowserLegacy show the name as a link that starts
        ///                                               the navigation there (rB UI/Components/NpcBox/NpcBox.js:48-56 and :146-160,
        ///                                               Navigation.js setNaviInfo: map, x, y from the first three fields)
        ///   &lt;ITEM&gt;name&lt;INFO&gt;id&lt;/INFO&gt;&lt;/ITEM&gt; (or ITEMLINK)   an item: the name as a link to its description (NpcBox.js:62-75)
        ///   &lt;URL&gt;name&lt;INFO&gt;address,w,h&lt;/INFO&gt;&lt;/URL&gt;      a web page: rB does not read it; Loki shows only the name (no browser in VR)
        /// </summary>
        public sealed class Link
        {
            public enum Type { Navi, Item, Url }
            public Type Kind; public string Name = "", Map = "", Info = ""; public int X, Y, ItemId;
            public override string ToString() => Kind == Type.Navi ? $"{Name} → {Map} {X},{Y}" : Kind == Type.Item ? $"{Name} (item {ItemId})" : $"{Name} ({Info})";
        }

        private static readonly System.Text.RegularExpressions.Regex NaviTag = new System.Text.RegularExpressions.Regex(@"<NAVI>([^<]+)<INFO>([^<]+)</INFO></NAVI>");
        private static readonly System.Text.RegularExpressions.Regex ItemTag = new System.Text.RegularExpressions.Regex(@"<(ITEMLINK|ITEM)>([\s\S]*?)<INFO>([\s\S]*?)</INFO></\1>");
        private static readonly System.Text.RegularExpressions.Regex UrlTag = new System.Text.RegularExpressions.Regex(@"<URL>([\s\S]*?)<INFO>([\s\S]*?)</INFO></URL>");
        private static readonly System.Text.RegularExpressions.Regex Placeholder = new System.Text.RegularExpressions.Regex("(\\d+)");
        /// <summary>The links' colours: roBrowserLegacy's hover shades (NpcBox.css: .navi-link #c00000 / hover #ff0000, .item-link #0070c0 /
        /// hover #00a0ff), its plain ones being made for a white box and Loki's panel being dark; a place gets a ► in front (Unity's
        /// text cannot underline, rB's other mark).</summary>
        public const string NaviColour = "ff0000", ItemColour = "00a0ff", UrlColour = "c8cbd6";

        /// <summary>An NPC's raw text as the panel's rich text: the links as their coloured names (collected into <paramref name="links"/>
        /// when given), then the ^RRGGBB colours (UI.UiKit.RoColors, which neutralises every other '&lt;' so no other tag can act).</summary>
        public static string RichText(string raw, List<Link> links)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var shown = new List<string>();
            string Hold(string colour, string label) { shown.Add("<color=#" + colour + ">" + label.Replace("<", "<​") + "</color>"); return "" + (shown.Count - 1) + ""; }
            string s = NaviTag.Replace(raw, m =>
            {
                var link = new Link { Kind = Link.Type.Navi, Name = DataHex.Replace(m.Groups[1].Value, ""), Info = m.Groups[2].Value };
                var f = link.Info.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (f.Length >= 3 && int.TryParse(f[1].Trim(), out int x) && int.TryParse(f[2].Trim(), out int y)) { link.Map = f[0].Trim(); link.X = x; link.Y = y; }
                else if (f.Length >= 1) link.Map = f[0].Trim();
                links?.Add(link);
                return Hold(NaviColour, "► " + link.Name);
            });
            s = ItemTag.Replace(s, m =>
            {
                var link = new Link { Kind = Link.Type.Item, Name = m.Groups[2].Value, Info = m.Groups[3].Value };
                int.TryParse(link.Info.Trim(), out link.ItemId);
                links?.Add(link);
                return Hold(ItemColour, link.Name);
            });
            s = UrlTag.Replace(s, m =>
            {
                var link = new Link { Kind = Link.Type.Url, Name = m.Groups[1].Value, Info = m.Groups[2].Value };
                links?.Add(link);
                return Hold(UrlColour, link.Name);
            });
            s = UI.UiKit.RoColors(s);
            return Placeholder.Replace(s, m => int.TryParse(m.Groups[1].Value, out int i) && i < shown.Count ? shown[i] : "");
        }

        // ------------------------------------------------------------------ the same links in the game data's own texts (waves 27-28 merge)
        /// <summary>Waves 27-28 merge (main): the links of the GAME DATA's texts. Item descriptions (the install's iteminfo, both
        /// languages: the player's items.tsv holds 2,347 of them: &lt;NAVI&gt;, &lt;TIPBOX&gt; (the official client's tip window), &lt;URL&gt;) and quest
        /// summaries / descriptions (4,755: &lt;NAVI&gt;, &lt;ITEM&gt;, &lt;TIPBOX&gt;) went through UI.UiKit.RoColors alone, which shows every '&lt;'
        /// as itself: their tags were read raw ("&lt;NAVI&gt;[Refining Master]&lt;INFO&gt;prontera,184,177,0,000,0&lt;/INFO&gt;&lt;/NAVI&gt;"; older than
        /// the text overhaul, whose pack writes every link in the strict form). Gravity's own slips are read as meant: &lt;INFO&gt; twice,
        /// closers doubled, swapped ("&lt;/NAVI&gt;&lt;/INFO&gt;") or cut short ("&lt;/NAVI."), a tag with no &lt;INFO&gt; (its label), a stray tag (dropped).</summary>
        private static readonly System.Text.RegularExpressions.Regex DataLink = new System.Text.RegularExpressions.Regex(
            @"<(NAVI|ITEMLINK|ITEM|URL|TIPBOX)>([^<]*)(?:<INFO>)+([^<]*)(?:</(?:INFO|\1)>)*(?:</\1(?![A-Za-z>]))?");
        private static readonly System.Text.RegularExpressions.Regex DataLabel = new System.Text.RegularExpressions.Regex(
            @"<(NAVI|ITEMLINK|ITEM|URL|TIPBOX)>([^<]*)</\1>");
        private static readonly System.Text.RegularExpressions.Regex DataStray = new System.Text.RegularExpressions.Regex(
            @"</?(?:NAVI|ITEMLINK|ITEM|URL|TIPBOX|INFO)>");
        private static readonly System.Text.RegularExpressions.Regex DataHex = new System.Text.RegularExpressions.Regex(@"\^[0-9A-Fa-f]{6}");

        /// <summary>A text of the game data (an item's description, a quest's summary or description) as rich text: its links as their
        /// coloured names (a place with ►, as <see cref="RichText"/> shows them in a dialog; links of the data are not buttons), then the
        /// ^RRGGBB colours (UI.UiKit.RoColors, which neutralises every other '&lt;'). No link tag is left raw.</summary>
        public static string DataText(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var shown = new List<string>();
            string Hold(string kind, string label)
            {
                string colour = kind == "NAVI" ? NaviColour : kind.StartsWith("ITEM", StringComparison.Ordinal) ? ItemColour : UrlColour;
                shown.Add("<color=#" + colour + ">" + (kind == "NAVI" ? "► " : "") + DataHex.Replace(label, "") + "</color>"); // (a label holds no '<')
                return "" + (shown.Count - 1) + "";
            }
            string s = DataLink.Replace(raw, m => Hold(m.Groups[1].Value, m.Groups[2].Value));
            s = DataLabel.Replace(s, m => Hold(m.Groups[1].Value, m.Groups[2].Value));
            s = DataStray.Replace(s, "");
            s = UI.UiKit.RoColors(s);
            return Placeholder.Replace(s, m => int.TryParse(m.Groups[1].Value, out int i) && i < shown.Count ? shown[i] : "");
        }
    }
}
