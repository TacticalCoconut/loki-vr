using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// A general floating window for the VR pointer: title, info line, optional tabs, a paged list of rows (icon, text and
    /// up to two buttons each) and up to four footer buttons. Used for NPC shops, Kafra storage, the Warp Portal /
    /// Teleport destination menu, the Magnifier and card-insertion pickers. The owner rebuilds the rows whenever its data
    /// changes; the window only lays them out. Opens in front of the head; stays where it is after that.
    /// </summary>
    public sealed class ListWindow : MonoBehaviour
    {
        public sealed class Row
        {
            public Sprite Icon; public string Text;
            public string A; public Action OnA; public bool AEnabled = true;
            public string B; public Action OnB; public bool BEnabled = true;
            public bool Highlight;
            /// <summary>Client wave 5 (w5ui): the icon's box in pixels (0 = the usual 52; a picture such as a cart style may take up to 80).</summary>
            public float IconSize;
            /// <summary>Client wave 11 (w11menu): the row is a slider (UI/UiSlider.cs): its track under the text (the text keeps the upper
            /// half of the row); A / B are usually its − / + steps.</summary>
            public UiSlider.Spec Slider;
            // ---- client wave 13, item 5 (w13ring): a SETTING row, made by UI/SettingRow.cs (never by hand): its control shows the current
            // state and every choice. Key: the setting's English name (tests find the row by it in either language).
            public string Key;
            /// <summary>Choices (two or three side by side, more as a stepper "◄ ● current ►"), their English texts, the current one's
            /// index (read when the row is drawn), what a press on choice i does (not called for the current one unless Repick).</summary>
            public string[] Choices, ChoiceKeys; public Func<int> Chosen; public Action<int> OnChoose; public bool Repick, Stepper;
            /// <summary>A slider row's value as shown beside it ("65 %"); its list of values when it is a slider over a list.</summary>
            public Func<string> Value; public float[] StepValues;
            /// <summary>An action row of a settings page (A / B are verbs).</summary>
            public bool IsAction;
            public bool IsSetting => Key != null;
            public int Current => Chosen != null ? Chosen() : -1;
        }

        private const float W = 980, H = 900;
        /// <summary>Client wave 13 (w13look), item 24: the eye's side and its distance from the rim, in px (0.00052 m each: 3.1 cm, 2.55° from
        /// the eyes where it is: the window's middle 0.63 m away, its corner 0.70 m; 1.5 cm), and the eye (UI/LookSelect.cs).</summary>
        public const float EyePx = 60f, EyeRimPx = 29f;
        public LookEye Eye { get; private set; }
        private const int PerPage = 7;
        private const float RowH = 86;
        private RectTransform _rt;
        private Text _title, _info, _page;
        private UiTitlePlate _plate; // client wave 21 (w21kit): the title stands on the gold plate on the frame's top edge
        private UiButton _x, _prev, _next, _back;
        /// <summary>Client wave 13 (w13items), item 11: the back arrow (smoke).</summary>
        public UiButton BackArrow => _back;
        public UiButton CloseButton => _x;
        public void SetCloseVisible(bool on) { if (_x != null && _x.gameObject.activeSelf != on) _x.gameObject.SetActive(on); }
        private readonly List<UiButton> _tabs = new List<UiButton>();
        // client wave 14 (w14menu), point 17: a page's sections (Settings → Comfort / Controls), a second row of tabs under the first
        private readonly List<UiButton> _subTabs = new List<UiButton>();
        private Action<int> _onSubTab; private bool _subShown;
        public const int MaxSubTabs = 4;
        /// <summary>Client wave 14 (w14menu): the sections' row is shown now (smoke).</summary>
        public bool SubTabsShown => _subShown;
        private readonly List<(RectTransform bg, Image icon, Text text, UiButton a, UiButton b)> _rows = new List<(RectTransform, Image, Text, UiButton, UiButton)>();
        private readonly List<UiSlider> _sliders = new List<UiSlider>(); // client wave 11 (w11menu): one per row slot, shown for a slider row
        // client wave 13, item 5 (w13ring): per row slot, the choices drawn side by side and the box that shows a stepper's or a slider's value
        private readonly List<UiButton[]> _segs = new List<UiButton[]>();
        private readonly List<(RectTransform box, Text text)> _vals = new List<(RectTransform, Text)>();
        /// <summary>Client wave 13, item 5: a setting row's control column (choices, stepper, slider's − value +, action buttons) on the
        /// right of the row: <see cref="CtlW"/> px wide, ending <see cref="CtlRight"/>; its text column from <see cref="SetTextLeft"/>.
        /// Three choices of 170 px (the old row buttons' width) fit; a stepper's arrows are 120 px, its value 270.</summary>
        public const float CtlW = 526f, CtlRight = W / 2 - 20f, SegGap = 8f, ArrowW = 120f, CtlH = 62f, SetTextLeft = -W / 2 + 35f, SideValW = 96f;
        private readonly List<UiButton> _footer = new List<UiButton>();
        private List<Row> _data = new List<Row>();
        private int _pageIdx;
        private Action<int> _onTab;
        /// <summary>The X button. The owner decides what closing means (and which packet releases the server-side state).</summary>
        public Action OnCloseClicked;
        public bool Visible => gameObject.activeSelf;
        /// <summary>An optional text box on the info line (shop titles, prices…): shown with <see cref="SetInput"/>; typing goes through the VR keyboard the owner attaches.</summary>
        public VrTextField Input { get; private set; }
        public Action<VrTextField> RequestKeyboard;

        public static ListWindow Create(Transform parent, string name)
        {
            var rt = UiKit.Panel(name, parent, new Vector2(W, H), 0.00052f, true, PanelKind.Menu); // 51 × 47 cm (client wave 13: every list window is a menu to the new casting: UI/UiKit.cs PanelKinds)
            var w = rt.gameObject.AddComponent<ListWindow>();
            w._rt = rt;
            w.Build();
            // client wave 13 (w13look), item 24: the look's eye in the bottom left corner, EyeRimPx (1.5 cm) inside the rim; the window's kind
            // is its name (UI/LookSelect.cs)
            w.Eye = LookEye.Add(rt, () => name, new Vector2(-W / 2 + EyeRimPx + EyePx / 2, -H / 2 + EyeRimPx + EyePx / 2), EyePx);
            // client wave 13 (w13items), item 11: every list window can be the player's menu (UI/MenuNav.cs): opened from another it gets the back arrow
            MenuNav.Register(new MenuNav.Menu { Name = name, Panel = rt, IsOpen = () => w != null && w.Visible, Close = w.NavClose, Capture = w.NavCapture, Reopen = w.NavReopen,
                Arrow = on => { if (w != null && w._back.gameObject.activeSelf != on) w._back.gameObject.SetActive(on); } });
            rt.gameObject.SetActive(false);
            return w;
        }

        private void Build()
        {
            // client wave 21 (w21kit), THE PANEL THEME: the title on the gold plate (the same 34 px letters, in the middle of the frame's top
            // edge; up to W - 220 px wide), the X a round gold button (its laser collider the same 64 x 52 px)
            _plate = UiKit.TitlePlate(_rt, "", 34, W - 220); _title = _plate.Text;
            _x = UiKit.RoundButton(_rt, "X", new Vector2(W / 2 - 45, H / 2 - 40), new Vector2(64, 52), () => { if (!MenuNav.CloseButton(transform)) OnCloseClicked?.Invoke(); }, 30); // client wave 13: "back" mode: the X goes back (UI/MenuNav.cs)
            // client wave 13 (w13items), item 11: the back arrow, outside the top left corner: 140 × 128 px = 7.3 × 6.7 cm, 5.3° from its 0.72 m
            _back = MenuNav.MakeArrow(_rt, new Vector2(W, H), new Vector2(140, 128));
            _info = UiKit.Label(_rt, "", new Vector2(0, H / 2 - 92), new Vector2(W - 60, 48), 24, TextAnchor.MiddleLeft, UiKit.Dim);
            // client wave 4b (w4lang): a long title, info line or row shrinks a little instead of being cut (Portuguese runs ~20 % longer)
            _title.resizeTextForBestFit = true; _title.resizeTextMinSize = 24; _title.resizeTextMaxSize = 34;
            _info.resizeTextForBestFit = true; _info.resizeTextMinSize = 17; _info.resizeTextMaxSize = 24;
            Input = VrTextField.Create(_rt, new Vector2(W / 4 + 10, H / 2 - 92), new Vector2(W / 2 - 80, 48), 24);
            Input.OnFocus = f => RequestKeyboard?.Invoke(f);
            Input.gameObject.SetActive(false);
            for (int i = 0; i < MaxTabs; i++)
            {
                int t = i;
                var b = UiKit.Button(_rt, "", new Vector2(-W / 2 + 150 + i * 250, H / 2 - 145), new Vector2(230, 50), () => _onTab?.Invoke(t), 24);
                b.gameObject.SetActive(false);
                _tabs.Add(b);
            }
            for (int i = 0; i < MaxSubTabs; i++)
            {
                int t = i;
                var b = UiKit.Button(_rt, "", new Vector2(0, H / 2 - 145), new Vector2(200, 46), () => _onSubTab?.Invoke(t), 22);
                b.name = "subtab" + i; b.gameObject.SetActive(false);
                _subTabs.Add(b);
            }
            for (int i = 0; i < PerPage; i++)
            {
                int slot = i;
                float y = H / 2 - 215 - i * (RowH + 6);
                var bg = UiKit.Soft(_rt, "row", new Vector2(0, y), new Vector2(W - 50, RowH), Theme.Row);
                var icon = UiKit.Icon(_rt, new Vector2(-W / 2 + 62, y), new Vector2(52, 52));
                var text = UiKit.Label(_rt, "", new Vector2(-W / 2 + 100 + (W - 480) / 2, y), new Vector2(W - 480, RowH - 4), 23, TextAnchor.MiddleLeft);
                text.resizeTextForBestFit = true; text.resizeTextMinSize = 17; text.resizeTextMaxSize = 23; // w4lang: see _info
                var a = UiKit.Button(_rt, "", new Vector2(W / 2 - 290, y), new Vector2(170, 62), () => Click(slot, true), 24);
                var b = UiKit.Button(_rt, "", new Vector2(W / 2 - 105, y), new Vector2(170, 62), () => Click(slot, false), 24);
                _rows.Add((bg, icon, text, a, b));
                var sl = UiSlider.Create(_rt, new Vector2(-W / 2 + 100 + (W - 480) / 2, y - 27), new Vector2(W - 480, 28));
                sl.gameObject.SetActive(false);
                _sliders.Add(sl);
                // client wave 13, item 5: the setting row's choices and value box (made hidden; Layout places them)
                var segs = new UiButton[SettingRow.MaxSide];
                for (int k = 0; k < segs.Length; k++)
                {
                    int choice = k;
                    segs[k] = UiKit.Button(_rt, "", new Vector2(0, y), new Vector2(170, CtlH), () => Choose(slot, choice), 24);
                    segs[k].name = "choice" + k; segs[k].gameObject.SetActive(false);
                }
                _segs.Add(segs);
                var vb = UiKit.Soft(_rt, "value", new Vector2(CtlRight - CtlW / 2, y), new Vector2(CtlW - 2 * (ArrowW + SegGap), CtlH), Theme.Lit);
                var vt = UiKit.Label(vb, "", Vector2.zero, new Vector2(CtlW - 2 * (ArrowW + SegGap) - 12, CtlH - 4), 24, TextAnchor.MiddleCenter, Theme.Ink);
                vt.resizeTextForBestFit = true; vt.resizeTextMinSize = 17; vt.resizeTextMaxSize = 24; vt.fontStyle = FontStyle.Bold;
                vb.gameObject.SetActive(false);
                _vals.Add((vb, vt));
            }
            float by = -H / 2 + 48;
            // client wave 13 (w13look), item 24: the look's eye takes the bottom left corner (Create: EyePx, EyeRimPx); the page arrows and
            // number moved right and narrowed to make room (90 / 110 / 90 px wide before, at -W/2 + 70 / 175 / 280; now 70 / 76 / 70): still
            // left of the fourth footer button (11 px), the arrows as tall as before (58 px)
            float px0 = -W / 2 + EyeRimPx + EyePx + 6f;
            _prev = UiKit.RoundButton(_rt, "<", new Vector2(px0 + 35f, by), new Vector2(70, 58), () => { _pageIdx = Mathf.Max(0, _pageIdx - 1); Layout(); }, 30);
            _page = UiKit.Label(_rt, "", new Vector2(px0 + 111f, by), new Vector2(76, 50), 24, TextAnchor.MiddleCenter, UiKit.Dim);
            _next = UiKit.RoundButton(_rt, ">", new Vector2(px0 + 187f, by), new Vector2(70, 58), () => { _pageIdx++; Layout(); }, 30);
            for (int i = 0; i < 4; i++)
            {
                var f = UiKit.Button(_rt, "", new Vector2(W / 2 - 95 - i * 160, by), new Vector2(150, 60), null, 24);
                f.gameObject.SetActive(false);
                _footer.Add(f);
            }
        }

        private void Click(int slot, bool a)
        {
            int i = _pageIdx * PerPage + slot;
            if (i >= _data.Count) return;
            var r = _data[i];
            if (r.Stepper && r.Choices != null) { StepChoice(r, a ? -1 : 1); return; } // client wave 13, item 5: a stepper's ◄ / ►
            if (a) { if (r.AEnabled) r.OnA?.Invoke(); }
            else if (r.BEnabled) r.OnB?.Invoke();
        }

        /// <summary>Client wave 13, item 5: choice <paramref name="choice"/> of the setting row in <paramref name="slot"/> was pressed: it
        /// becomes the current one (nothing happens when it already is, unless the row repicks).</summary>
        private void Choose(int slot, int choice)
        {
            int i = _pageIdx * PerPage + slot;
            if (i >= _data.Count) return;
            var r = _data[i];
            if (r.Choices == null || choice < 0 || choice >= r.Choices.Length) return;
            if (choice == r.Current && !r.Repick) return;
            r.OnChoose?.Invoke(choice);
            if (gameObject.activeSelf) Layout(); // the owner usually redraws the page itself (its "set" saves and refreshes); a row that does not is redrawn here
        }

        /// <summary>A stepper's arrow: the choice before (-1) or after (+1) the current one, wrapping round.</summary>
        private void StepChoice(Row r, int dir)
        {
            if (r.Choices == null || r.Choices.Length == 0) return;
            int n = r.Choices.Length, now = Mathf.Max(0, r.Current);
            r.OnChoose?.Invoke(((now + dir) % n + n) % n);
            if (gameObject.activeSelf) Layout();
        }

        private Camera _head;
        private Vector3 _spawnPos;
        private Quaternion _spawnRot;

        /// <summary>Shows the window; the first time (or when it was hidden) it is placed in front of the head.</summary>
        public void Open(Camera head)
        {
            if (head != null) _head = head;
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
                if (_head != null)
                {
                    if (!PanelPlacing.TryPlaceMenu("ListWindow:" + name, transform, _head, out _))
                    {
                        var f = _head.transform.forward; f.y = 0; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
                        transform.position = _head.transform.position + f * 0.62f + Vector3.up * -0.1f;
                        UiKit.FaceHead(transform, _head);
                    }
                }
                _spawnPos = transform.position;
                _spawnRot = transform.rotation;
                _pageIdx = 0;
            }
            Layout();
        }

        public void Hide()
        {
            if (gameObject.activeSelf)
            {
                if ((transform.position - _spawnPos).sqrMagnitude > 1e-5f || Quaternion.Angle(transform.rotation, _spawnRot) > 0.5f)
                {
                    var cam = _head != null ? _head : Camera.main;
                    if (cam != null) PanelPlacing.SaveMenuPlace("ListWindow:" + name, transform, cam);
                }
                gameObject.SetActive(false);
            }
        }

        // ---- client wave 13 (w13items), item 11 (UI/MenuNav.cs): closed outright by its owner (the X's own close: it may release what the
        // server holds open), its place and page kept, brought back by the button that had opened it and then put where it was
        private void NavClose() { var c = OnCloseClicked; if (c != null) c(); if (gameObject.activeSelf) Hide(); }
        private object NavCapture() => new MenuNav.Pose { Position = transform.position, Rotation = transform.rotation, Page = _pageIdx };
        private void NavReopen(UiButton opener, object state)
        {
            opener?.InvokeAction();
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            if (state is MenuNav.Pose p) { transform.SetPositionAndRotation(p.Position, p.Rotation); _pageIdx = p.Page; }
            Layout();
        }

        public void SetTitle(string s) => _plate.Set(s);
        /// <summary>Shows (or hides) the input box on the info line; the info text keeps the left half while it is shown.</summary>
        public void SetInput(bool show, string placeholder = "", Func<char, bool> allow = null, Action onSubmit = null, string text = null, int maxLength = 79)
        {
            Input.gameObject.SetActive(show);
            var irt = (RectTransform)_info.transform;
            irt.sizeDelta = new Vector2(show ? W / 2 - 60 : W - 60, 48); irt.anchoredPosition = new Vector2(show ? -W / 4 : 0, H / 2 - 92);
            if (!show) { Input.Blur(); return; }
            Input.Placeholder = placeholder ?? ""; Input.Allow = allow; Input.OnSubmit = onSubmit; Input.MaxLength = maxLength;
            if (text != null) Input.SetText(text); else Input.SetText(Input.Text);
        }
        public void SetInfo(string s) => _info.text = s ?? "";

        private const int MaxTabs = 9; // client wave 4b (w4lang): 6, the Settings window has a Language page; client wave 6 (w6voice): 7, a Voice page; client wave 9 (h9flow): 8, a Horde page; client wave 13, item 4 (w13ring): 9, a Rumble page
        /// <summary>Tabs across the top (null or empty hides them). Up to three keep the classic 230-wide layout; four or five share the width.</summary>
        public void SetTabs(string[] names, int active, Action<int> onTab)
        {
            _onTab = onTab;
            int count = names != null ? Mathf.Min(names.Length, _tabs.Count) : 0;
            float tw = count > 3 ? (W - 60 - (count - 1) * 10) / count : 230;
            for (int i = 0; i < _tabs.Count; i++)
            {
                bool show = i < count;
                _tabs[i].gameObject.SetActive(show);
                if (!show) continue;
                var trt = (RectTransform)_tabs[i].transform;
                _tabs[i].Resize(new Vector2(tw, 50));
                trt.anchoredPosition = new Vector2(count > 3 ? -W / 2 + 30 + tw / 2 + i * (tw + 10) : -W / 2 + 150 + i * 250, _subShown ? H / 2 - 92 : H / 2 - 145); // (client wave 14: up while a page shows its sections)
                _tabs[i].SetLabel(names[i]);
                // (client wave 21: the shown page's tab is the lit gold one, bold; the others plain)
                _tabs[i].Normal = i == active ? Theme.Lit : Theme.Cell; _tabs[i].Hover = i == active ? Theme.LitHover : Theme.CellHover; _tabs[i].SetHover(false);
                if (_tabs[i].Text != null) { _tabs[i].Text.color = Theme.Ink; _tabs[i].Text.fontStyle = i == active ? FontStyle.Bold : FontStyle.Normal; }
            }
        }

        /// <summary>Client wave 14 (w14menu), point 17 (design note: "subdivide the controls menu on the settings tab along with the comfort tabs so
        /// that it is easier to know where require to go"): a page's SECTIONS as a second row of tabs, under the tabs (which move up into
        /// the info line's place while it is shown: the info line is hidden then). null or empty hides it and puts the tabs back.</summary>
        public void SetSubTabs(string[] names, int active, Action<int> onSub)
        {
            _onSubTab = onSub;
            int count = names != null ? Mathf.Min(names.Length, _subTabs.Count) : 0;
            _subShown = count > 0;
            float tw = count > 0 ? (W - 60 - (count - 1) * 10) / count : 0f;
            for (int i = 0; i < _subTabs.Count; i++)
            {
                bool show = i < count;
                if (_subTabs[i].gameObject.activeSelf != show) _subTabs[i].gameObject.SetActive(show);
                if (!show) continue;
                _subTabs[i].Resize(new Vector2(tw, 46));
                ((RectTransform)_subTabs[i].transform).anchoredPosition = new Vector2(-W / 2 + 30 + tw / 2 + i * (tw + 10), H / 2 - 145);
                _subTabs[i].SetLabel(names[i]);
                _subTabs[i].Normal = i == active ? Theme.Lit : Theme.CellOff; _subTabs[i].Hover = i == active ? Theme.LitHover : Theme.CellHover; _subTabs[i].SetHover(false);
                if (_subTabs[i].Text != null) { _subTabs[i].Text.color = i == active ? Theme.Ink : Theme.InkDim; _subTabs[i].Text.fontStyle = i == active ? FontStyle.Bold : FontStyle.Normal; }
            }
            // the page tabs up into the info line's place while the sections are shown
            float ty = _subShown ? H / 2 - 92 : H / 2 - 145;
            foreach (var t in _tabs) { var trt = (RectTransform)t.transform; if (Mathf.Abs(trt.anchoredPosition.y - ty) > 0.01f) trt.anchoredPosition = new Vector2(trt.anchoredPosition.x, ty); }
            if (_info.gameObject.activeSelf == _subShown) _info.gameObject.SetActive(!_subShown);
        }

        /// <summary>Jumps to the page holding row <paramref name="rowIndex"/> of the current list.</summary>
        public void ShowRow(int rowIndex) { _pageIdx = Mathf.Max(0, rowIndex) / PerPage; Layout(); }
        /// <summary>Client wave 15 (w15menu), point 7: the row of the shown page whose control <paramref name="b"/> is (a choice, a stepper's or
        /// a slider's − / + or ◄ / ►, its slider's track, an action button): its index in the list (-1: none), the choice it is (-1: not a
        /// choice) and the row's middle on the window, in px (UI/SettingHelp.cs places itself beside it).</summary>
        public int RowOfButton(UiButton b, out int choice, out float rowY)
        {
            choice = -1; rowY = 0f;
            if (b == null || !gameObject.activeSelf) return -1;
            for (int r = 0; r < PerPage && r < _rows.Count; r++)
            {
                int i = _pageIdx * PerPage + r;
                if (i >= _data.Count) break;
                var (_, _, _, a, bb) = _rows[r];
                bool hit = b == a || b == bb || (r < _sliders.Count && _sliders[r].Track == b);
                if (!hit && _segs[r] != null) for (int k = 0; k < _segs[r].Length; k++) if (_segs[r][k] == b) { hit = true; choice = k; break; }
                if (!hit) continue;
                rowY = H / 2 - 215 - r * (RowH + 6);
                return i;
            }
            return -1;
        }
        /// <summary>Client wave 15 (w15menu), point 7: the window's size in px.</summary>
        public static Vector2 SizePx => new Vector2(W, H);
        /// <summary>Test hook: presses the A (true) or B button of row <paramref name="rowIndex"/> as if the laser clicked it.</summary>
        public void Press(int rowIndex, bool a = true) { if (rowIndex < 0 || rowIndex >= _data.Count) return; var r = _data[rowIndex]; if (r.Stepper && r.Choices != null) { StepChoice(r, a ? -1 : 1); return; } if (a) { if (r.AEnabled) r.OnA?.Invoke(); } else if (r.BEnabled) r.OnB?.Invoke(); }
        public int RowCount => _data.Count;
        /// <summary>Client wave 11 (w11menu): the slider drawn for row <paramref name="rowIndex"/> (null: the row is not on the shown page or is no slider row).</summary>
        public UiSlider SliderOf(int rowIndex)
        {
            int slot = rowIndex - _pageIdx * PerPage;
            return slot >= 0 && slot < _sliders.Count && rowIndex < _data.Count && _data[rowIndex].Slider != null && _sliders[slot].gameObject.activeSelf ? _sliders[slot] : null;
        }
        public string RowText(int i) => i >= 0 && i < _data.Count ? _data[i].Text : null;
        /// <summary>Client wave 13, item 5: the row's data (null: none).</summary>
        public Row RowAt(int i) => i >= 0 && i < _data.Count ? _data[i] : null;
        /// <summary>Client wave 6 (w6voice): a new text for one row, in place (the Voice page's live level meter): no new layout, so
        /// the buttons' hover and a gaze choice in progress are left alone.</summary>
        public void SetRowText(int rowIndex, string text)
        {
            if (rowIndex < 0 || rowIndex >= _data.Count) return;
            _data[rowIndex].Text = text;
            int slot = rowIndex - _pageIdx * PerPage;
            if (slot >= 0 && slot < _rows.Count && gameObject.activeSelf) _rows[slot].text.text = text ?? "";
        }

        public void SetRows(List<Row> rows, bool resetPage = false)
        {
            _data = rows ?? new List<Row>();
            if (resetPage) _pageIdx = 0;
            Layout();
        }

        /// <summary>Footer buttons from right to left: pass (label, action) pairs; null label hides a slot.</summary>
        public void SetFooter(params (string label, Action action)[] buttons)
        {
            for (int i = 0; i < _footer.Count; i++)
            {
                bool show = buttons != null && i < buttons.Length && !string.IsNullOrEmpty(buttons[i].label);
                _footer[i].gameObject.SetActive(show);
                if (!show) continue;
                _footer[i].SetLabel(buttons[i].label);
                _footer[i].SetAction(buttons[i].action);
            }
        }

        private void Layout()
        {
            if (_rows.Count == 0) return;
            int pages = Mathf.Max(1, (_data.Count + PerPage - 1) / PerPage);
            _pageIdx = Mathf.Clamp(_pageIdx, 0, pages - 1);
            _page.text = _data.Count == 0 ? "" : $"{_pageIdx + 1} / {pages}";
            _prev.gameObject.SetActive(pages > 1); _next.gameObject.SetActive(pages > 1);
            for (int r = 0; r < PerPage; r++)
            {
                int i = _pageIdx * PerPage + r;
                var (bg, icon, text, a, b) = _rows[r];
                bool show = i < _data.Count;
                bg.gameObject.SetActive(show); text.gameObject.SetActive(show);
                var row = show ? _data[i] : null;
                // client wave 13, item 5: a setting row has its text on the left and its control column on the right (UI/SettingRow.cs)
                bool setting = show && row.IsSetting;
                bool choices = setting && row.Choices != null, stepper = choices && row.Stepper, segmented = choices && !row.Stepper;
                float ry = H / 2 - 215 - r * (RowH + 6);
                float tw = setting ? CtlRight - CtlW - 12f - SetTextLeft : W - 480, tx = setting ? SetTextLeft + tw / 2 : -W / 2 + 100 + (W - 480) / 2;
                var slider = r < _sliders.Count ? _sliders[r] : null;
                bool slides = show && row.Slider != null && slider != null;
                var trt = (RectTransform)text.transform;
                trt.anchoredPosition = new Vector2(tx, slides ? ry + 15 : ry); trt.sizeDelta = new Vector2(tw, slides ? 54 : RowH - 4); // a slider row: two lines of text over the track (client wave 11)
                if (slider != null)
                {
                    if (slider.gameObject.activeSelf != slides) slider.gameObject.SetActive(slides);
                    if (slides) { bool side = choices; slider.Place(new Vector2(side ? tx - SideValW / 2 : tx, ry - 27), side ? tw - SideValW : tw); slider.Bind(row.Slider); } // (a switch with a strength: the value beside the track)
                }
                var segs = _segs[r];
                var (vbox, vtext) = _vals[r];
                if (!show)
                {
                    icon.enabled = false; a.gameObject.SetActive(false); b.gameObject.SetActive(false);
                    if (segs != null) foreach (var sg in segs) sg.gameObject.SetActive(false);
                    if (vbox != null) vbox.gameObject.SetActive(false);
                    continue;
                }
                bg.GetComponent<Image>().color = row.Highlight ? Theme.RowLit : Theme.Row;
                icon.sprite = row.Icon; icon.enabled = row.Icon != null;
                float isz = row.IconSize > 0 ? Mathf.Min(row.IconSize, RowH - 4) : 52f; icon.rectTransform.sizeDelta = new Vector2(isz, isz); // client wave 5 (w5ui)
                text.text = row.Text ?? "";
                bool arrows = stepper || (setting && slides && !choices);
                // A / B: a row's own buttons where they always were; a stepper's ◄ / ► and a slider's − / + at the ends of the control column
                Place(a, arrows ? new Vector2(CtlRight - CtlW + ArrowW / 2, ry) : new Vector2(W / 2 - 290, ry), arrows ? new Vector2(ArrowW, CtlH) : new Vector2(170, 62));
                Place(b, arrows ? new Vector2(CtlRight - ArrowW / 2, ry) : new Vector2(W / 2 - 105, ry), arrows ? new Vector2(ArrowW, CtlH) : new Vector2(170, 62));
                if (stepper) { SetButton(a, "◄", true); SetButton(b, "►", true); }
                else if (segmented) { SetButton(a, null, false); SetButton(b, null, false); }
                else { SetButton(a, row.A, row.AEnabled); SetButton(b, row.B, row.BEnabled); }
                int cur = choices ? row.Current : -1;
                if (segs != null)
                {
                    int n = segmented ? row.Choices.Length : 0;
                    float sw = n > 0 ? (CtlW - (n - 1) * SegGap) / n : 0f;
                    for (int k = 0; k < segs.Length; k++)
                    {
                        var sg = segs[k]; bool on = k < n;
                        if (sg.gameObject.activeSelf != on) sg.gameObject.SetActive(on);
                        if (!on) continue;
                        Place(sg, new Vector2(CtlRight - CtlW + sw / 2 + k * (sw + SegGap), ry), new Vector2(sw, CtlH));
                        PaintChoice(sg, row.Choices[k], k == cur);
                    }
                }
                if (vbox != null)
                {
                    bool val = stepper || (setting && slides && row.Value != null);
                    if (vbox.gameObject.activeSelf != val) vbox.gameObject.SetActive(val);
                    if (val)
                    {
                        bool side = choices && slides; // a switch with a strength (the rumble kinds): its value at the end of the track
                        var vsz = side ? new Vector2(SideValW - 8, 30) : new Vector2(CtlW - 2 * (ArrowW + SegGap), CtlH);
                        vbox.anchoredPosition = side ? new Vector2(tx + tw / 2 - SideValW / 2 + 4, ry - 27) : new Vector2(CtlRight - CtlW / 2, ry);
                        if (vbox.sizeDelta != vsz) { vbox.sizeDelta = vsz; ((RectTransform)vtext.transform).sizeDelta = vsz - new Vector2(10, 2); }
                        string v = stepper ? (cur >= 0 && cur < row.Choices.Length ? SettingRow.Mark + row.Choices[cur] : "") : row.Value != null ? row.Value() : "";
                        if (vtext.text != v) vtext.text = v;
                    }
                }
            }
        }

        /// <summary>Client wave 13, item 5: a button's place and size (and its collider's).</summary>
        private static void Place(UiButton btn, Vector2 pos, Vector2 size)
        {
            var rt = (RectTransform)btn.transform;
            if (rt.anchoredPosition != pos) rt.anchoredPosition = pos;
            if (rt.sizeDelta != size) btn.Resize(size);
        }

        /// <summary>Client wave 13, item 5: a choice drawn lit (the current one: gold, "● ", bold dark text) or plain (client wave 21: the
        /// theme's gold and its plain button, UI/Theme.cs).</summary>
        private static void PaintChoice(UiButton btn, string label, bool lit)
        {
            btn.SetLabel(lit ? SettingRow.Mark + label : label);
            btn.Interactable = true;
            btn.Normal = lit ? Theme.Lit : Theme.Cell; btn.Hover = lit ? Theme.LitHover : Theme.CellHover;
            if (btn.Text != null) { btn.Text.color = Theme.Ink; btn.Text.fontStyle = lit ? FontStyle.Bold : FontStyle.Normal; }
            btn.SetHover(btn.Hovered);
        }

        /// <summary>Client wave 13, item 5: a slider's value as shown beside it follows a drag at once.</summary>
        private void LateUpdate()
        {
            for (int r = 0; r < PerPage && r < _vals.Count; r++)
            {
                int i = _pageIdx * PerPage + r;
                if (i >= _data.Count) break;
                var row = _data[i];
                if (row.Value == null || row.Slider == null || !row.IsSetting) continue;
                var (vbox, vtext) = _vals[r];
                if (!vbox.gameObject.activeSelf) continue;
                string v = row.Value();
                if (vtext.text != v) vtext.text = v;
            }
        }

        private static void SetButton(UiButton btn, string label, bool enabled)
        {
            bool show = !string.IsNullOrEmpty(label);
            btn.gameObject.SetActive(show);
            if (!show) return;
            btn.SetLabel(label);
            btn.Interactable = enabled;
            btn.Normal = Theme.Cell; // (client wave 21: one that cannot be pressed is washed out with faint ink: UiButton.SetHover)
            btn.SetHover(false);
        }

    }
}
