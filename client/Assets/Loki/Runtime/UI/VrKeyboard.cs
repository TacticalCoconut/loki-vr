using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// A text box for the VR pointer: shows its text (or dots when masked) with a caret while focused. Click it to focus;
    /// the <see cref="VrKeyboard"/> types into whichever field is focused, at the caret (◄ ► move it). A physical keyboard types
    /// into it too when the window has focus (the owner routes ChatPanel-style key events here).
    /// </summary>
    public sealed class VrTextField : MonoBehaviour
    {
        public string Text = ""; public bool Masked; public int MaxLength = 23; public string Placeholder = "";
        public Func<char, bool> Allow;
        public Action OnSubmit; public Action<VrTextField> OnFocus;
        /// <summary>Where the next character goes: 0 = before the first, Text.Length = after the last.</summary>
        public int Caret { get => Mathf.Clamp(_caret, 0, Text?.Length ?? 0); private set => _caret = value; }
        private int _caret;
        private Text _label; private Image _bg; private bool _focused;
        public bool Focused => _focused;

        public static VrTextField Create(RectTransform parent, Vector2 pos, Vector2 size, int fontSize = 26)
        {
            var box = UiKit.Box(parent, "field", pos, size, new Color(0, 0, 0, 0.6f));
            var f = box.gameObject.AddComponent<VrTextField>();
            f._bg = box.GetComponent<Image>();
            f._label = UiKit.Label(box, "", Vector2.zero, size - new Vector2(20, 4), fontSize, TextAnchor.MiddleLeft, Color.white);
            var col = box.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(size.x, size.y, 8f); col.center = new Vector3(0, 0, -2f);
            var b = box.gameObject.AddComponent<UiButton>();
            b.Normal = new Color(0, 0, 0, 0.6f); b.Hover = new Color(0.15f, 0.2f, 0.35f, 0.8f);
            b.Init(f._bg, null, () => f.Press()); // (client wave 24: a press, not Focus: see Press)
            f.Redraw();
            return f;
        }

        public void Focus() { if (_focused) { Redraw(); return; } _focused = true; OnFocus?.Invoke(this); Redraw(); } // OnFocus handlers may call Focus back: the guard stops the loop
        /// <summary>Client wave 24 (w24), the design's bug (2026-10-12): "once text has been placed inside of a name box. If I click away or
        /// close the keyboard, I can't click back onto the name box to continue typing." THE CAUSE: the field's button called
        /// <see cref="Focus"/>, whose guard returns when the field is focused already, and nothing takes the focus away when the
        /// keyboard is closed (its Close key hides it; so does the login screen's "Keyboard" button): the field stayed focused,
        /// so a second press never raised <see cref="OnFocus"/> and the keyboard never came back (with or without text in it).
        /// A PRESS on the field (laser, look, fingertip) now always asks for the keyboard: the field is focused, the caret goes to
        /// the end of its text, and OnFocus is raised every time. The text is never touched.</summary>
        public void Press() { _focused = true; _caret = (Text ?? "").Length; Presses++; OnFocus?.Invoke(this); Redraw(); }
        /// <summary>Presses on this field so far (smoke, log).</summary>
        public int Presses { get; private set; }
        public void Blur() { _focused = false; Redraw(); }
        /// <summary>Replaces the text; the caret goes to the end.</summary>
        public void SetText(string s) { Text = s ?? ""; _caret = Text.Length; Redraw(); }
        public void Type(char c)
        {
            if (Text == null) Text = "";
            if (c < 0x20 || c == 0x7f || Text.Length >= MaxLength) return;
            if (Allow != null && !Allow(c)) return;
            int at = Caret; Text = Text.Insert(at, c.ToString()); _caret = at + 1; Redraw();
        }
        /// <summary>Deletes the character before the caret.</summary>
        public void Backspace() { int at = Caret; if (at > 0) { Text = Text.Remove(at - 1, 1); _caret = at - 1; Redraw(); } }
        /// <summary>Deletes the character after the caret (the physical Delete key).</summary>
        public void Delete() { int at = Caret; if (at < Text.Length) { Text = Text.Remove(at, 1); _caret = at; Redraw(); } }
        /// <summary>Moves the caret: −1 / +1 one character, <see cref="int.MinValue"/> home, <see cref="int.MaxValue"/> end.</summary>
        public void MoveCaret(int delta)
        {
            int at = Caret;
            _caret = delta == int.MinValue ? 0 : delta == int.MaxValue ? Text.Length : Mathf.Clamp(at + delta, 0, Text.Length);
            Redraw();
        }
        public void Submit() => OnSubmit?.Invoke();

        /// <summary>The text with the caret drawn in it, rich-text safe (smoke tests read it).</summary>
        public static string WithCaret(string text, int caret, bool masked, bool focused)
        {
            text = text ?? ""; caret = Mathf.Clamp(caret, 0, text.Length);
            string shown = masked ? new string('●', text.Length) : text;
            string a = shown.Substring(0, caret).Replace("<", "<​"), b = shown.Substring(caret).Replace("<", "<​");
            return focused ? a + "<color=#ffcc55>|</color>" + b : a + b;
        }

        /// <summary>Client wave 4b (w4lang): a change of language redraws the placeholder in the new language.</summary>
        public void Relabel() => Redraw();

        private void Redraw()
        {
            if (_label == null) return;
            if ((Text ?? "").Length == 0 && !_focused) _label.text = $"<color=#7a8090>{Lang.Again(Placeholder)}</color>";
            else _label.text = WithCaret(Text, Caret, Masked, _focused);
            if (_bg != null) _bg.color = _focused ? new Color(0.08f, 0.12f, 0.22f, 0.9f) : new Color(0, 0, 0, 0.6f);
        }
    }

    /// <summary>
    /// A laser-pointable keyboard: letters with ç, digits, symbols (€ £ ¡ ¿ « » º ª °), Shift (one tap: the next letter; two taps:
    /// caps lock; three: off), the five accents as dead keys (´ ` ^ ~ ¨: tap the accent, then the letter — á à â ã ä, é è ê ë,
    /// í ì î ï, ó ò ô õ ö, ú ù û ü, ý ÿ Ÿ, ñ, and ´ c = ç as on US-International; the letter keys show what they will type
    /// while an accent waits; the accent then space, or twice, types the accent itself), ◄ ► to move the caret, space,
    /// backspace, enter and close. Sits low in front of the head, tilted up. It types into a target (a <see cref="VrTextField"/>
    /// or any char / backspace / enter / caret callbacks), so the login screen, character names, the chat console and NPC
    /// text prompts all share it. Every character it types is in cp1252, the code page the server's text travels in
    /// (<see cref="Net.RoText"/>).
    /// </summary>
    public sealed class VrKeyboard : MonoBehaviour
    {
        private const float KeyW = 78, KeyH = 70, Gap = 6, U = KeyW + Gap;
        /// <summary>Client wave 21 (w21tools): the keyboard takes THE PANEL THEME (design note: "Yes"): a key is the theme's soft cell with dark ink
        /// (<see cref="KeyOff"/>); Shift once, the symbol layer and a waiting accent are the theme's lit gold (<see cref="KeyOn"/>); CAPS its deeper
        /// gold with bold letters (<see cref="KeyCaps"/>). Key sizes and laser colliders are wave 4's.</summary>
        public static readonly Color KeyOff = Theme.Cell, KeyOn = Theme.Lit, KeyCaps = Theme.Gold;
        /// <summary>A character key's size in px (smoke).</summary>
        public static Vector2 KeySizePx => new Vector2(KeyW, KeyH);
        // 39 character slots in four rows; each layer fills them (a shorter string hides the rest of its row)
        private static readonly string[] RowsLower = { "1234567890", "qwertyuiop", "asdfghjklç", "zxcvbnm,." };
        private static readonly string[] RowsSymbol = { "!@#$%&*()?", "-_=+[]{}\\|", ";:'\"/<>^~`", "€£¡¿«»ºª°" };
        /// <summary>The dead keys, in the order of their keys on the bottom row.</summary>
        public const string DeadKeys = "´`^~¨";
        private RectTransform _rt; private Camera _head;
        private readonly List<UiButton> _keys = new List<UiButton>();
        /// <summary>What each character slot types now (layer and Shift applied, before an accent composes it).</summary>
        private readonly List<char> _keyChars = new List<char>();
        private readonly int[] _rowLen = new int[4];
        private readonly List<UiButton> _deadButtons = new List<UiButton>();
        private UiButton _shift, _sym, _left, _right;
        private enum ShiftMode { Off, Once, Caps }
        private ShiftMode _shiftMode; private bool _symbols; private char _dead;
        /// <summary>Where keys go. Set by the owner; null types nowhere. <see cref="OnCaret"/> null hides ◄ ►.</summary>
        public Action<char> OnChar { get => _onChar; set { _onChar = value; OnCaret = null; } } // a new target forgets the old caret
        private Action<char> _onChar;
        public Action OnBackspace; public Action OnEnter; public Action OnClose;
        public Action<int> OnCaret { get => _onCaret; set { _onCaret = value; RefreshCaretKeys(); } }
        private Action<int> _onCaret;
        public bool Visible => gameObject.activeSelf;
        public bool CapsLock => _shiftMode == ShiftMode.Caps;
        public bool ShiftOnce => _shiftMode == ShiftMode.Once;
        public bool SymbolsShown => _symbols;
        /// <summary>The accent waiting for its letter ('\0' when none).</summary>
        public char PendingAccent => _dead;

        public static VrKeyboard Create(Transform parent, Camera head)
        {
            float w = 13.2f * U + 40, h = 5 * (KeyH + Gap) + 40;
            var rt = UiKit.Panel("VrKeyboard", parent, new Vector2(w, h), 0.00048f, true, PanelKind.ByName, PanelLook.Framed); // client wave 21 (w21tools): the wood frame (outside this rectangle) and the cream canvas
            var k = rt.gameObject.AddComponent<VrKeyboard>();
            k._rt = rt; k._head = head;
            k.Build(w, h);
            rt.gameObject.SetActive(false);
            return k;
        }

        // ---------------------------------------------------------------- layout
        private float _x; private float _y;
        private UiButton Key(string label, float units, Action a, int font = 30)
        {
            float wpx = units * U - Gap;
            var b = UiKit.Button(_rt, label, new Vector2(_x + wpx / 2, _y), new Vector2(wpx, KeyH), a, font);
            b.LaserOnly = true;
            _x += units * U;
            return b;
        }
        private void Row(int r, float units) { _y = _top - r * (KeyH + Gap); _x = -units * U / 2 + Gap / 2; }
        private float _top;

        private void Build(float w, float h)
        {
            _top = h / 2 - 20 - KeyH / 2;
            // row 0: digits + Back; row 1: q…p + Enter; row 2: a…l ç; row 3: Shift + z…m , .; row 4: #+= ´ ` ^ ~ ¨ space ◄ ► Close
            for (int r = 0; r < 4; r++)
            {
                int n = RowsLower[r].Length; _rowLen[r] = n;
                float units = n + (r == 0 || r == 1 || r == 3 ? 1.6f : 0f);
                Row(r, units);
                if (r == 3) _shift = Key(Lang.T("Shift"), 1.6f, PressShift, 22);
                for (int i = 0; i < n; i++) { int row = r, col = i; _keys.Add(Key(RowsLower[r][i].ToString(), 1f, () => Press(row, col))); _keyChars.Add(RowsLower[r][i]); }
                if (r == 0) Key(Lang.T("~key~Back"), 1.6f, PressBackspace, 24); // the OS font has no ⌫ (U+232B): it drew a blank key
                if (r == 1) Key(Lang.T("Enter"), 1.6f, PressEnter, 24);
            }
            Row(4, 13.2f);
            _sym = Key("#+=", 1.6f, () => { HideVariants(); _holdKey = -1; _symbols = !_symbols; Relabel(); }, 22);
            foreach (char d in DeadKeys) { char dk = d; _deadButtons.Add(Key(d.ToString(), 1f, () => PressDead(dk), 32)); }
            Key(Lang.T("space"), 3f, () => PressChar(' '), 22);
            // ◄ ► (U+25C4 / U+25BA) are in WGL4, so Segoe UI and Arial both draw them
            _left = Key("◄", 1f, () => PressCaret(-1), 26);
            _right = Key("►", 1f, () => PressCaret(+1), 26);
            Key(Lang.T("Close"), 1.6f, () => { _dead = '\0'; Hide(); OnClose?.Invoke(); Relabel(); }, 22);
            Relabel(); RefreshCaretKeys();
        }

        // ---------------------------------------------------------------- keys
        /// <summary>Single-click Shift (<see cref="ShiftMode.Once"/>) shifts digits and punctuation to their special symbols as a standard
        /// keyboard does when holding Shift; <see cref="ShiftMode.Caps"/> only uppercases letters.</summary>
        private static char ShiftChar(char c, ShiftMode mode)
        {
            if (mode == ShiftMode.Off) return c;
            if (mode == ShiftMode.Once)
            {
                switch (c)
                {
                    case '1': return '!';
                    case '2': return '@';
                    case '3': return '#';
                    case '4': return '$';
                    case '5': return '%';
                    case '6': return '^';
                    case '7': return '&';
                    case '8': return '*';
                    case '9': return '(';
                    case '0': return ')';
                    case ',': return '<';
                    case '.': return '>';
                }
            }
            return char.ToUpperInvariant(c);
        }

        private void Press(int row, int col)
        {
            var rows = _symbols ? RowsSymbol : RowsLower;
            if (row >= rows.Length || col >= rows[row].Length) return;
            char c = rows[row][col];
            if (!_symbols && _shiftMode != ShiftMode.Off) c = ShiftChar(c, _shiftMode);
            bool plain = _dead == '\0'; // a letter an accent composed has no long-press (it is already accented)
            HideVariants();
            PressChar(c);
            if (_shiftMode == ShiftMode.Once && !_symbols) { _shiftMode = ShiftMode.Off; Relabel(); }
            // client wave 4: long-press — the letter is typed at once (the laser clicks on the trigger's press, VR/DioramaRig.cs);
            // holding the trigger on the key opens its accented forms above it (LongPress region below)
            int k = KeyIndex(row, col);
            if (plain && k >= 0 && Variants(c).Length > 0) { _holdKey = k; _holdChar = c; _holdStart = Time.time; } else _holdKey = -1;
        }
        private int KeyIndex(int row, int col) { int k = 0; for (int r = 0; r < row; r++) k += _rowLen[r]; k += col; return k < _keys.Count ? k : -1; }

        /// <summary>A character from a key (or space): composed with a waiting accent when the pair exists.</summary>
        private void PressChar(char c)
        {
            HideVariants(); _holdKey = -1;
            if (_dead != '\0')
            {
                char accent = _dead; _dead = '\0';
                char composed = Compose(accent, c);
                if (composed != '\0') OnChar?.Invoke(composed);
                else if (c == ' ') OnChar?.Invoke(accent);             // accent + space = the accent itself
                else { OnChar?.Invoke(accent); OnChar?.Invoke(c); }     // no such letter: both, as Windows' dead keys do
                Relabel();
                return;
            }
            OnChar?.Invoke(c);
        }

        private void PressDead(char accent)
        {
            HideVariants(); _holdKey = -1;
            if (_dead == accent) { _dead = '\0'; OnChar?.Invoke(accent); }         // twice: the accent itself
            else if (_dead != '\0') { OnChar?.Invoke(_dead); _dead = accent; }    // another accent: the first is typed, the second waits
            else _dead = accent;
            Relabel();
        }

        private void PressShift()
        {
            HideVariants(); _holdKey = -1;
            _shiftMode = _shiftMode == ShiftMode.Off ? ShiftMode.Once : _shiftMode == ShiftMode.Once ? ShiftMode.Caps : ShiftMode.Off;
            Relabel();
        }
        private void PressBackspace() { HideVariants(); _holdKey = -1; if (_dead != '\0') { _dead = '\0'; Relabel(); return; } OnBackspace?.Invoke(); } // first drops a waiting accent
        private void PressEnter() { HideVariants(); _holdKey = -1; if (_dead != '\0') { _dead = '\0'; Relabel(); } OnEnter?.Invoke(); }
        private void PressCaret(int d) { HideVariants(); _holdKey = -1; if (_dead != '\0') { _dead = '\0'; Relabel(); } OnCaret?.Invoke(d); }

        // ---------------------------------------------------------------- long-press accents (client wave 4, w4account)
        /// <summary>The accented forms a letter key offers when held, all in cp1252 (<see cref="Net.RoText"/>): Portuguese and Spanish
        /// first, then the other Latin letters the code page has (å æ ø œ š ž ß, € on e, ª º on a / o as the Portuguese keyboard).</summary>
        public static string Variants(char c)
        {
            switch (c)
            {
                case 'a': return "áàâãäåæª"; case 'A': return "ÁÀÂÃÄÅÆª";
                case 'e': return "éèêë€"; case 'E': return "ÉÈÊË€";
                case 'i': return "íìîï"; case 'I': return "ÍÌÎÏ";
                case 'o': return "óòôõöøœº"; case 'O': return "ÓÒÔÕÖØŒº";
                case 'u': return "úùûü"; case 'U': return "ÚÙÛÜ";
                case 'c': return "ç"; case 'C': return "Ç";
                case 'n': return "ñ"; case 'N': return "Ñ";
                case 'y': return "ýÿ"; case 'Y': return "ÝŸ";
                case 's': return "šß"; case 'S': return "Šß";
                case 'z': return "ž"; case 'Z': return "Ž";
                default: return "";
            }
        }
        /// <summary>How long the trigger stays down on a letter before its accents open (a phone keyboard's long-press, ~0.5 s).</summary>
        public const float HoldSeconds = 0.45f;
        private int _holdKey = -1; private char _holdChar; private float _holdStart;
        private RectTransform _popup; private readonly List<UiButton> _popupKeys = new List<UiButton>(); private string _popupFor = ""; private float _popupUntil;
        /// <summary>The accents on show ("" when the popup is closed): tests read it.</summary>
        public string VariantsShown => _popup != null && _popup.gameObject.activeSelf ? _popupFor : "";

        private void Update()
        {
            if (_popup != null && _popup.gameObject.activeSelf && Time.time > _popupUntil) HideVariants();
            if (_holdKey < 0) return;
            if (!TriggerHeldOn(_keys[_holdKey])) { _holdKey = -1; return; }
            if (Time.time - _holdStart >= HoldSeconds) { int k = _holdKey; _holdKey = -1; ShowVariants(k, _holdChar); }
        }

        /// <summary>The pointer hand's trigger is still down and its ray still on this key (the same ray as VR/DioramaRig's pointer).</summary>
        private static bool TriggerHeldOn(UiButton key)
        {
            var rig = LokiBootstrap.Instance != null ? LokiBootstrap.Instance.Rig : null;
            if (rig == null || rig.Input == null || key == null) return false;
            if (rig.Main.TriggerValue < 0.5f) return false;
            var hand = rig.PointerHand;
            return rig.LaserOnPanel(new Ray(hand.position, hand.forward), 50f, out var hit) && hit.collider.GetComponent<UiButton>() == key; // (client wave 14, point 18: the panels win over the world)
        }

        /// <summary>Opens the accents of the typed letter above its key; picking one replaces the letter (backspace, then the accent).</summary>
        private void ShowVariants(int keyIndex, char typed)
        {
            string v = Variants(typed); if (v.Length == 0 || keyIndex < 0) return;
            if (_popup == null)
            {
                _popup = UiKit.Card(_rt, "accents", Vector2.zero, new Vector2(100, KeyH + 16)); // (client wave 21: a parchment card)
                _popup.localPosition = new Vector3(0, 0, -12f); // in front of the keys: the pointer ray meets its buttons first
            }
            foreach (var b in _popupKeys) b.gameObject.SetActive(false);
            var keyRt = (RectTransform)_keys[keyIndex].transform;
            float w = v.Length * U + 12, half = ((RectTransform)transform).sizeDelta.x / 2 - w / 2 - 4;
            float x = Mathf.Clamp(keyRt.anchoredPosition.x, -half, half), y = keyRt.anchoredPosition.y + KeyH + Gap;
            _popup.sizeDelta = new Vector2(w, KeyH + 16);
            _popup.anchoredPosition3D = new Vector3(x, y, -12f);
            for (int i = 0; i < v.Length; i++)
            {
                char a = v[i];
                if (i >= _popupKeys.Count)
                {
                    var pk = UiKit.Button(_popup, "", Vector2.zero, new Vector2(KeyW, KeyH), null, 32);
                    pk.LaserOnly = true;
                    _popupKeys.Add(pk);
                }
                var b = _popupKeys[i];
                b.LaserOnly = true;
                ((RectTransform)b.transform).anchoredPosition = new Vector2(-w / 2 + 6 + U * i + KeyW / 2, 0);
                b.SetLabel(a.ToString()); b.SetAction(() => PickVariant(a));
                b.Normal = KeyOff; b.SetHover(false);
                b.gameObject.SetActive(true);
            }
            _popupFor = v; _popupUntil = Time.time + 6f;
            _popup.gameObject.SetActive(true);
            Debug.Log($"[Keyboard] long-press on '{typed}': {v}");
        }

        private void PickVariant(char a)
        {
            HideVariants();
            OnBackspace?.Invoke(); // the letter the key typed when it was pressed
            OnChar?.Invoke(a);
        }

        private void HideVariants() { if (_popup != null && _popup.gameObject.activeSelf) _popup.gameObject.SetActive(false); _popupFor = ""; }

        /// <summary>accent + letter → the accented letter, or '\0'. Every result is in cp1252 (Latin-1, plus Ÿ = 0x9F).</summary>
        public static char Compose(char accent, char letter)
        {
            string from, to;
            switch (accent)
            {
                case '´': from = "aeiouyAEIOUYcC"; to = "áéíóúýÁÉÍÓÚÝçÇ"; break;
                case '`': from = "aeiouAEIOU"; to = "àèìòùÀÈÌÒÙ"; break;
                case '^': from = "aeiouAEIOU"; to = "âêîôûÂÊÎÔÛ"; break;
                case '~': from = "aonAON"; to = "ãõñÃÕÑ"; break;
                case '¨': from = "aeiouyAEIOUY"; to = "äëïöüÿÄËÏÖÜŸ"; break;
                default: return '\0';
            }
            int i = from.IndexOf(letter);
            return i >= 0 ? to[i] : '\0';
        }

        private void Relabel()
        {
            var rows = _symbols ? RowsSymbol : RowsLower;
            int k = 0;
            for (int r = 0; r < 4; r++)
                for (int i = 0; i < _rowLen[r]; i++, k++)
                {
                    bool has = r < rows.Length && i < rows[r].Length;
                    _keys[k].gameObject.SetActive(has);
                    if (!has) continue;
                    char c = rows[r][i];
                    if (!_symbols && _shiftMode != ShiftMode.Off) c = ShiftChar(c, _shiftMode);
                    _keyChars[k] = c;
                    char composed = _dead != '\0' ? Compose(_dead, c) : '\0';
                    // while an accent waits, a letter key shows what it will type
                    _keys[k].SetLabel(composed != '\0' ? $"<color=#ffd060>{composed}</color>" : c == '<' ? "<​" : c.ToString());
                }
            Color on = KeyOn, caps = KeyCaps, off = KeyOff; // (client wave 21: the theme's colours)
            _shift.SetLabel(_shiftMode == ShiftMode.Caps ? Lang.T("CAPS") : _shiftMode == ShiftMode.Once ? Lang.T("SHIFT") : Lang.T("Shift"));
            _shift.Normal = _shiftMode == ShiftMode.Caps ? caps : _shiftMode == ShiftMode.Once ? on : off; _shift.SetHover(false);
            if (_shift.Text != null) _shift.Text.fontStyle = _shiftMode == ShiftMode.Caps ? FontStyle.Bold : FontStyle.Normal;
            _sym.SetLabel(_symbols ? "abc" : "#+="); _sym.Normal = _symbols ? on : off; _sym.SetHover(false);
            for (int i = 0; i < _deadButtons.Count; i++) { _deadButtons[i].Normal = DeadKeys[i] == _dead ? on : off; _deadButtons[i].SetHover(false); }
        }

        private void RefreshCaretKeys()
        {
            if (_left == null) return;
            bool can = _onCaret != null;
            _left.Interactable = can; _right.Interactable = can;
            _left.Normal = _right.Normal = KeyOff; // (client wave 21: a key that cannot be pressed is washed and its glyph faint: UI/UiKit.cs UiButton.SetHover) _left.SetHover(false); _right.SetHover(false);
        }

        /// <summary>Shows the keyboard low in front of the head, tilted up towards it. Client wave 24 (w24): or where the player
        /// put it (<see cref="PlaceKept"/>); a keyboard that shows already stays where it is (it was placed anew at every press of
        /// a text field); never in the ground (UI/PanelPlacing.cs).</summary>
        public void Show()
        {
            bool was = gameObject.activeSelf;
            if (!was && (_shiftMode != ShiftMode.Off || _symbols || _dead != '\0'))
            {
                _shiftMode = ShiftMode.Off; _symbols = false; _dead = '\0'; Relabel();
            }
            gameObject.SetActive(true);
            if (!was) Place();
        }
        /// <summary>Minimum initial spawn distance from the player's head (metres) when the VR keyboard is initially placed.</summary>
        public const float MinSpawnDistance = 0.70f;
        /// <summary>True once the player has manually grabbed and moved the keyboard during this session.</summary>
        public bool UserMoved { get; set; }
        /// <summary>The keyboard's default place from the eyes: metres right, up, ahead (75 cm ahead, 42 cm down).</summary>
        public static readonly Vector3 DefaultPlace = new Vector3(0f, -0.42f, 0.75f);
        /// <summary>The place the player gave it (null: none: the default). Set by the game (LokiGame.W24Login.cs: the login
        /// screen's, this PC's; the world's, the account's).</summary>
        public Func<Loki.World.UserSettings.PadPlace> PlaceKept;
        /// <summary>Puts the keyboard at its kept place, else its default one, from the eyes as they are now.</summary>
        public void Place()
        {
            if (_head == null) return;
            PanelPlacing.Place(transform, _head, PlaceKept != null ? PlaceKept() : null, DefaultPlace, UserMoved ? 0f : MinSpawnDistance);
        }
        public void Hide()
        {
            HideVariants(); _holdKey = -1;
            if (_shiftMode != ShiftMode.Off || _symbols || _dead != '\0')
            {
                _shiftMode = ShiftMode.Off; _symbols = false; _dead = '\0'; Relabel();
            }
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        /// <summary>Points the keyboard at a field (characters, backspace, enter and the caret keys).</summary>
        public void Attach(VrTextField f)
        {
            OnChar = c => f.Type(c); OnBackspace = () => f.Backspace(); OnEnter = () => f.Submit(); OnCaret = d => f.MoveCaret(d);
        }

        /// <summary>Points the keyboard at any text target; <paramref name="caret"/> null hides ◄ ►.</summary>
        public void Target(Action<char> onChar, Action backspace, Action enter, Action<int> caret)
        {
            OnChar = onChar; OnBackspace = backspace; OnEnter = enter; OnCaret = caret;
        }

    }
}
