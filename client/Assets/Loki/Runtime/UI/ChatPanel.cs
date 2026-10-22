using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>What a chat line is, for the chat window's tabs (client wave 4, w4social). The kind comes from the packet the line
    /// arrived in (Game/SocialHub.cs, Game/Clan.cs, Game/Channels.cs, Game/Modes.cs), never from its text.</summary>
    public enum ChatKind { System, Public, Party, Guild, Whisper, Channel, Clan, Battle }

    /// <summary>
    /// Chat log + console line typed on the real keyboard (the Loki desktop window must have focus).
    /// Enter sends the line as chat — "@warp prontera", "@go 0" and every other @command are just chat lines the map server
    /// intercepts; "/…" lines are the official client's slash commands (LokiGame.W4Social.cs). Up/Down walk the history, Esc clears.
    /// While an NPC asks for a number or text, typing goes to that box.
    /// Client wave 4 (w4social): the log keeps <see cref="MaxKept"/> lines with their <see cref="ChatKind"/>; tabs along the top filter
    /// it (All, Public, Party, Guild, Whisper, Channel, System — a tab that got lines while another was shown is marked with a dot);
    /// ▲ ▼ scroll the shown tab 5 lines at a time (Page Up / Page Down on the keyboard), "End" (or "+n" new lines) goes back to the
    /// newest line; <see cref="SaveTo"/> writes the whole log to a text file (/savechat, the official client's "Save a Chat Log").
    /// </summary>
    public sealed class ChatPanel : MonoBehaviour
    {
        private const float W = 980, H = 470;
        private const int MaxLines = 11;
        private RectTransform _rt;
        private Text _log, _input;
        private sealed class Line { public string Rich, Plain; public ChatKind Kind; public DateTime At; }
        private readonly List<Line> _lines = new List<Line>();
        private readonly List<string> _history = new List<string>();
        private int _historyPos = -1;
        private string _text = "";
        /// <summary>Where the next character goes in the console line (client wave 3, w3login: ◄ ► on the VR keyboard, arrows here).</summary>
        private int _caret;
        private bool _hooked;

        /// <summary>Sends a finished console line. Set by the game.</summary>
        public Action<string> Submit;
        /// <summary>When non-null and returns true, typed characters go to the NPC input instead (char, backspace, enter).</summary>
        public Func<bool> NpcWantsKeyboard; public Action<char> NpcChar; public Action NpcBackspace; public Action NpcEnter;
        /// <summary>Caret moves for the NPC text prompt (w3login).</summary>
        public Action<int> NpcCaret;
        /// <summary>Same for the login screen's text fields (they win over everything while one is focused).</summary>
        public Func<bool> LoginWantsKeyboard; public Action<char> LoginChar; public Action LoginBackspace; public Action LoginEnter;
        /// <summary>Caret moves for the login fields (−1 / +1, int.MinValue home, int.MaxValue end) and the Delete key.</summary>
        public Action<int> LoginCaret; public Action LoginDelete;
        /// <summary>The ⌨ button: the game shows the VR keyboard pointed at this console.</summary>
        public Action KeyboardRequested;

        // ---- tabs (filters)
        /// <summary>The tab labels, left to right.</summary>
        public static readonly string[] TabNames = { Lang.N("All"), Lang.N("Public"), Lang.N("Party"), Lang.N("Guild"), Lang.N("~chat~Whisper"), Lang.N("Channel"), Lang.N("System") }; // w4lang: table keys, translated where drawn
        private static bool TabShows(int tab, ChatKind k)
        {
            switch (tab)
            {
                case 0: return true;
                case 1: return k == ChatKind.Public || k == ChatKind.Battle;
                case 2: return k == ChatKind.Party;
                case 3: return k == ChatKind.Guild;
                case 4: return k == ChatKind.Whisper;
                case 5: return k == ChatKind.Channel || k == ChatKind.Clan;
                default: return k == ChatKind.System;
            }
        }
        private int _tab;
        private readonly bool[] _tabUnread = new bool[TabNames.Length];
        private readonly List<UiButton> _tabButtons = new List<UiButton>();
        /// <summary>The filter tab shown (0 = All).</summary>
        public int Tab => _tab;

        public static ChatPanel Create(Transform parent)
        {
            var rt = UiKit.Panel("Chat", parent, new Vector2(W, H), 0.00055f, true, PanelKind.ByName, PanelLook.SeeThrough); HudLook.Backing(rt); // client wave 21 (w21tools): see-through HUD: the theme's gold line and corner scrolls, its dark backing kept (UI/HudLook.cs)
            var p = rt.gameObject.AddComponent<ChatPanel>();
            p._rt = rt;
            // Log area between the tab strip (top) and the input box (bottom), clipped so long lines never spill out.
            var areaGo = new GameObject("logArea", typeof(RectTransform));
            areaGo.transform.SetParent(rt, false);
            var area = (RectTransform)areaGo.transform;
            area.anchorMin = area.anchorMax = new Vector2(0.5f, 0.5f);
            area.anchoredPosition = new Vector2(-40, 12); area.sizeDelta = new Vector2(W - 120, H - 160);
            areaGo.AddComponent<UnityEngine.UI.RectMask2D>();
            p._log = UiKit.Label(area, "", new Vector2(0, 3f), new Vector2(W - 120, H - 166), 24, TextAnchor.LowerLeft);
            p._log.verticalOverflow = VerticalWrapMode.Overflow;
            // scrolling: ▲ older, ▼ newer, "End" back to the newest line (the log keeps MaxKept lines, shows MaxLines)
            p._up = UiKit.Button(rt, "▲", new Vector2(W / 2 - 45, 110), new Vector2(66, 72), () => p.Scroll(+ScrollStep), 30);
            p._down = UiKit.Button(rt, "▼", new Vector2(W / 2 - 45, 30), new Vector2(66, 72), () => p.Scroll(-ScrollStep), 30);
            p._end = UiKit.Button(rt, Lang.T("~chat~End"), new Vector2(W / 2 - 45, -50), new Vector2(66, 60), () => p.Scroll(-100000), 20);
            var box = UiKit.Box(rt, "inputBg", new Vector2(0, -H / 2 + 50), new Vector2(W - 30, 56), new Color(0, 0, 0, 0.65f));
            p._input = UiKit.Label(box, "", new Vector2(-40, 0), new Vector2(W - 130, 52), 26, TextAnchor.MiddleLeft, Color.white);
            // client wave 24 (w24), item 9 (design note, 2026-10-12: "The close and open button directly on the chat box"): the chat box's own
            // close button, on its top right corner above the tab strip (the kit's close button as the windows have it: the same
            // letter and no smaller a hit area; on this see-through panel the kit draws it in the HUD's plain style). It hides the
            // chat exactly as the elbow's chat button does (UI/LokiHud.cs ToggleChat). While the chat is hidden an open tab stands
            // at this very spot (LokiGame.W24Hud.cs).
            p.CloseButton = UiKit.RoundButton(rt, "X", ClosePx, new Vector2(64, 56), () => p.CloseRequested?.Invoke(), 30);
            p.CloseButton.gameObject.name = "btn:chat close"; p.CloseButton.Tip = () => Lang.T("Close chat");
            // "Keys": the OS font (Segoe UI) has no ⌨ (U+2328), which drew an empty button
            UiKit.Button(rt, Lang.T("Keys"), new Vector2(W / 2 - 55, -H / 2 + 50), new Vector2(64, 52), () => p.KeyboardRequested?.Invoke(), 20);
            // the tab strip where the hint line used to be (the hint now shows inside the empty console line)
            float tw = (W - 40) / TabNames.Length;
            for (int i = 0; i < TabNames.Length; i++)
            {
                int t = i;
                p._tabButtons.Add(UiKit.Button(rt, Lang.T(TabNames[i]), new Vector2(-W / 2 + 20 + tw * (i + 0.5f), H / 2 - 28), new Vector2(tw - 6, 42), () => p.SetTab(t), 19));
            }
            // client wave 13 (w13look), item 24: the look's eye at the bottom right, over the console line and under End (the console line
            // runs along the bottom, too near the rim): 68 px (3.7 cm: 2.65° from its 0.81 m), 28 px (1.5 cm) inside the right rim
            p.Eye = LookEye.Add(rt, () => "Chat", new Vector2(W / 2 - 28f - 34f, -118f), 68f);
            p.Redraw();
            return p;
        }
        /// <summary>Client wave 13 (w13look), item 24: the look's eye (UI/LookSelect.cs).</summary>
        public LookEye Eye { get; private set; }

        /// <summary>Puts text in the console line (e.g. "/w Name " to answer a whisper).</summary>
        public void SetInput(string text) { _text = text ?? ""; _caret = _text.Length; _historyPos = -1; Redraw(); }
        /// <summary>The console line and its caret (smoke tests).</summary>
        public string InputText => _text;
        public int InputCaret => Mathf.Clamp(_caret, 0, _text.Length);

        /// <summary>Client wave 24 (w24), item 9: the close button on the box's top right corner, its middle in the panel's px, and
        /// what it asks for (the HUD's chat switch).</summary>
        public UiButton CloseButton { get; private set; }
        public static readonly Vector2 ClosePx = new Vector2(W / 2 - 32, H / 2 + 30);
        public Action CloseRequested;

        /// <summary>Hides the window but keeps the keyboard console working.</summary>
        public bool Visible { get; private set; } = true;
        public void SetVisible(bool v)
        {
            Visible = v;
            var c = GetComponent<Canvas>(); if (c != null) c.enabled = v;
            var col = GetComponent<BoxCollider>(); if (col != null) col.enabled = v;
            // client wave 13 (the orchestrator, found by w13look): the buttons inside (▲ ▼ End, Keys, the tabs, the eye ...) carry their own
            // colliders (UI/UiKit.cs Button), so a hidden chat still caught the laser and the look where it had been. Hiding turns off every
            // collider that is on and remembers them; showing turns exactly those back on (a collider another rule keeps off stays off).
            if (!v) { foreach (var cc in GetComponentsInChildren<Collider>(true)) if (cc != col && cc.enabled) { cc.enabled = false; _hiddenColliders.Add(cc); } }
            else { foreach (var cc in _hiddenColliders) if (cc != null) cc.enabled = true; _hiddenColliders.Clear(); }
        }
        private readonly List<Collider> _hiddenColliders = new List<Collider>();

        /// <summary>A line of the "System" kind (server replies, notices, Loki's own messages).</summary>
        public void Add(string line, string colorHex = null) => Add(line, colorHex, ChatKind.System);

        /// <summary>A line of a given kind; it shows in the "All" tab and in its own tab.</summary>
        public void Add(string line, string colorHex, ChatKind kind)
        {
            if (string.IsNullOrEmpty(line)) return;
            string esc = UiKit.RoColors(line);
            _lines.Add(new Line { Rich = colorHex != null ? $"<color=#{colorHex}>{esc}</color>" : esc, Plain = line, Kind = kind, At = DateTime.Now });
            bool shown = TabShows(_tab, kind);
            if (shown && _scroll > 0) { _scroll++; _unseen++; } // scrolled back: the lines on screen stay put
            for (int t = 0; t < TabNames.Length; t++) if (t != _tab && t != 0 && TabShows(t, kind)) _tabUnread[t] = true;
            while (_lines.Count > MaxKept) _lines.RemoveAt(0); // the view counts from the newest line: dropping the oldest never moves it
            _scroll = Mathf.Clamp(_scroll, 0, MaxScroll);
            Redraw();
        }

        /// <summary>Lines kept in the log, and how far back the view is (0 = the newest line at the bottom).</summary>
        public const int MaxKept = 1000, ScrollStep = 5;
        private int _scroll, _unseen; private UiButton _up, _down, _end;
        private int Shown { get { int n = 0; foreach (var l in _lines) if (TabShows(_tab, l.Kind)) n++; return n; } }
        private int MaxScroll => Mathf.Max(0, Shown - MaxLines);
        public int ScrollOffset => _scroll;
        /// <summary>Lines in the log (every kind).</summary>
        public int LineCount => _lines.Count;
        /// <summary>Lines the shown tab lists.</summary>
        public int ShownCount => Shown;
        /// <summary>Lines of one kind in the log (smoke).</summary>
        public int CountOf(ChatKind k) { int n = 0; foreach (var l in _lines) if (l.Kind == k) n++; return n; }
        /// <summary>The newest line of a kind, without tags ("" when none) — smoke.</summary>
        public string LastOf(ChatKind k) { for (int i = _lines.Count - 1; i >= 0; i--) if (_lines[i].Kind == k) return _lines[i].Plain; return ""; }
        /// <summary>The newest <paramref name="n"/> lines of a kind, newest last, without tags — smoke.</summary>
        public List<string> RecentOf(ChatKind k, int n)
        {
            var list = new List<string>();
            for (int i = _lines.Count - 1; i >= 0 && list.Count < n; i--) if (_lines[i].Kind == k) list.Add(_lines[i].Plain);
            list.Reverse(); return list;
        }
        /// <summary>Whether a tab carries the "new lines" dot.</summary>
        public bool TabUnread(int t) => t >= 0 && t < _tabUnread.Length && _tabUnread[t];
        /// <summary>The lines on screen now (tags included) — smoke test.</summary>
        public string VisibleText => _log != null ? _log.text : "";
        /// <summary>Moves the view back (+) or forward (−) by whole lines.</summary>
        public void Scroll(int lines)
        {
            _scroll = Mathf.Clamp(_scroll + lines, 0, MaxScroll);
            if (_scroll == 0) _unseen = 0;
            Redraw();
        }
        /// <summary>Shows one tab (a filter); the view goes to its newest line.</summary>
        public void SetTab(int t)
        {
            _tab = Mathf.Clamp(t, 0, TabNames.Length - 1);
            _tabUnread[_tab] = false; _scroll = 0; _unseen = 0;
            Redraw();
        }

        /// <summary>Writes every line kept (all tabs, oldest first, "[hh:mm:ss] text") to a UTF-8 text file; null on success, else why not.</summary>
        public string SaveTo(string path)
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                var sb = new System.Text.StringBuilder();
                foreach (var l in _lines) sb.Append('[').Append(l.At.ToString("HH:mm:ss")).Append("] ").Append(l.Kind == ChatKind.System ? "" : "(" + l.Kind + ") ").Append(l.Plain.Replace("\n", " ")).Append("\r\n");
                System.IO.File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));
                return null;
            }
            catch (Exception e) { return e.Message; }
        }

        public void PlaceInFront(Camera head)
        {
            if (head == null) return;
            var f = head.transform.forward; f.y = 0; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
            var right = Vector3.Cross(Vector3.up, f);
            transform.position = head.transform.position + f * 0.55f - right * 0.38f + Vector3.up * -0.38f;
            UiKit.FaceHead(transform, head);
        }

        private void OnEnable() { Hook(); }
        private void Start() { Hook(); }
        private void Hook()
        {
            if (_hooked || Keyboard.current == null) return;
            Keyboard.current.onTextInput += OnText;
            _hooked = true;
        }
        private void OnDestroy() { if (_hooked && Keyboard.current != null) Keyboard.current.onTextInput -= OnText; }

        private void OnText(char c) => TypeChar(c);

        /// <summary>A typed character from either keyboard: login field → NPC prompt → console line.</summary>
        public void TypeChar(char c)
        {
            if (c < 0x20 || c == 0x7f) return; // control keys are handled in Update
            if (LoginWantsKeyboard != null && LoginWantsKeyboard()) { LoginChar?.Invoke(c); return; }
            if (NpcWantsKeyboard != null && NpcWantsKeyboard()) { NpcChar?.Invoke(c); return; }
            if (_text.Length < 200) { int at = InputCaret; _text = _text.Insert(at, c.ToString()); _caret = at + 1; _historyPos = -1; Redraw(); }
        }
        /// <summary>Moves the caret: −1 / +1 one character, int.MinValue home, int.MaxValue end (login fields first, like typing).</summary>
        public void MoveCaret(int delta)
        {
            if (LoginWantsKeyboard != null && LoginWantsKeyboard()) { LoginCaret?.Invoke(delta); return; }
            if (NpcWantsKeyboard != null && NpcWantsKeyboard()) { NpcCaret?.Invoke(delta); return; }
            int at = InputCaret;
            _caret = delta == int.MinValue ? 0 : delta == int.MaxValue ? _text.Length : Mathf.Clamp(at + delta, 0, _text.Length);
            Redraw();
        }
        /// <summary>Deletes the character after the caret (the physical Delete key).</summary>
        public void DeleteForward()
        {
            if (LoginWantsKeyboard != null && LoginWantsKeyboard()) { LoginDelete?.Invoke(); return; }
            if (NpcWantsKeyboard != null && NpcWantsKeyboard()) return;
            int at = InputCaret; if (at < _text.Length) { _text = _text.Remove(at, 1); _caret = at; Redraw(); }
        }
        public void Backspace()
        {
            if (LoginWantsKeyboard != null && LoginWantsKeyboard()) { LoginBackspace?.Invoke(); return; }
            if (NpcWantsKeyboard != null && NpcWantsKeyboard()) { NpcBackspace?.Invoke(); return; }
            int back = InputCaret; if (back > 0) { _text = _text.Remove(back - 1, 1); _caret = back - 1; Redraw(); }
        }
        public void Enter()
        {
            if (LoginWantsKeyboard != null && LoginWantsKeyboard()) { LoginEnter?.Invoke(); return; }
            if (NpcWantsKeyboard != null && NpcWantsKeyboard()) { NpcEnter?.Invoke(); return; }
            if (_text.Trim().Length > 0)
            {
                string line = _text.Trim();
                _history.Remove(line); _history.Add(line); if (_history.Count > 50) _history.RemoveAt(0);
                _text = ""; _caret = 0; _historyPos = -1; Redraw();
                Submit?.Invoke(line);
            }
        }

        private void Update()
        {
            Hook();
            var kb = Keyboard.current;
            if (kb == null) return;
            bool npc = (NpcWantsKeyboard != null && NpcWantsKeyboard()) || (LoginWantsKeyboard != null && LoginWantsKeyboard());
            if (kb.backspaceKey.wasPressedThisFrame) Backspace();
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Enter();
            if (kb.escapeKey.wasPressedThisFrame && !npc) { _text = ""; _caret = 0; _historyPos = -1; Redraw(); }
            if (kb.leftArrowKey.wasPressedThisFrame) MoveCaret(-1);
            if (kb.rightArrowKey.wasPressedThisFrame) MoveCaret(+1);
            if (kb.homeKey.wasPressedThisFrame) MoveCaret(int.MinValue);
            if (kb.endKey.wasPressedThisFrame) MoveCaret(int.MaxValue);
            if (kb.deleteKey.wasPressedThisFrame) DeleteForward();
            if (kb.pageUpKey.wasPressedThisFrame) Scroll(+ScrollStep);
            if (kb.pageDownKey.wasPressedThisFrame) Scroll(-ScrollStep);
            if (!npc && _history.Count > 0)
            {
                if (kb.upArrowKey.wasPressedThisFrame) { _historyPos = _historyPos < 0 ? _history.Count - 1 : Mathf.Max(0, _historyPos - 1); _text = _history[_historyPos]; _caret = _text.Length; Redraw(); }
                if (kb.downArrowKey.wasPressedThisFrame && _historyPos >= 0) { _historyPos++; if (_historyPos >= _history.Count) { _historyPos = -1; _text = ""; } else _text = _history[_historyPos]; _caret = _text.Length; Redraw(); }
            }
        }

        private readonly List<string> _view = new List<string>();
        private void Redraw()
        {
            if (_log == null) return;
            // the newest MaxLines lines of the shown tab, _scroll lines back from the end
            _view.Clear();
            int skip = _scroll;
            for (int i = _lines.Count - 1; i >= 0 && _view.Count < MaxLines; i--)
            {
                if (!TabShows(_tab, _lines[i].Kind)) continue;
                if (skip > 0) { skip--; continue; }
                _view.Add(_lines[i].Rich);
            }
            _view.Reverse();
            _log.text = string.Join("\n", _view);
            if (_up != null)
            {
                bool older = _scroll < MaxScroll, newer = _scroll > 0;
                _up.Interactable = older; _up.Normal = older ? UiKit.ButtonBg : new Color(0.25f, 0.25f, 0.27f); _up.SetHover(false);
                _down.Interactable = newer; _down.Normal = newer ? UiKit.ButtonBg : new Color(0.25f, 0.25f, 0.27f); _down.SetHover(false);
                _end.gameObject.SetActive(newer);
                _end.SetLabel(_unseen > 0 ? "+" + Mathf.Min(_unseen, 99) : Lang.T("~chat~End"));
            }
            for (int t = 0; t < _tabButtons.Count; t++)
            {
                var b = _tabButtons[t];
                b.Normal = t == _tab ? new Color(0.45f, 0.35f, 0.15f) : UiKit.ButtonBg; b.SetHover(false);
                b.SetLabel(_tabUnread[t] && t != _tab ? Lang.T(TabNames[t]) + " •" : Lang.T(TabNames[t]));
            }
            bool focused = Application.isFocused;
            if (_text.Length == 0)
                _input.text = "<color=#ffcc55>></color> " + (focused ? Lang.T("<color=#8a90a0>type, Enter to send</color>")
                                                                    : Lang.T("<color=#ff9a6a>Click the Loki window to type.</color>"));
            else _input.text = "<color=#ffcc55>></color> " + VrTextField.WithCaret(_text, InputCaret, false, true);
        }

        private bool _lastFocus;
        private void LateUpdate() { if (Application.isFocused != _lastFocus) { _lastFocus = Application.isFocused; Redraw(); } }
    }
}
