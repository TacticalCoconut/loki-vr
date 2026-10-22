using System.Collections.Generic;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// The NPC conversation window: title, cut-in illustration, scrolling text (newest at the bottom), and the buttons the
    /// script currently allows — Next, Close, a paged option list, or number / text input (physical keyboard or the pad).
    /// Placed in front of the player when a conversation starts; stays put in the room after that.
    /// </summary>
    public sealed class DialogPanel : MonoBehaviour
    {
        private const float W = 1000, H = 660;
        /// <summary>Client wave 13 (w13look), item 24: the look's eye (UI/LookSelect.cs).</summary>
        public LookEye Eye { get; private set; }
        private RectTransform _rt, _textArea;
        private Text _title, _body, _inputBox, _hint;
        private Image _cutin;
        private UiButton _next, _close, _x;
        private readonly List<UiButton> _options = new List<UiButton>();
        private UiButton _prev, _more, _cancel;
        private readonly List<UiButton> _pad = new List<UiButton>();
        private UiButton _ok, _clear;
        /// <summary>The places the text links to (&lt;NAVI&gt;, Game/NpcDialog.cs Link): a button each, at most two, in the bottom row's
        /// free left part (talking, or a menu on one page); the official client's link starts the navigation, and so does the button.</summary>
        private readonly List<UiButton> _naviButtons = new List<UiButton>();
        private const int NaviButtons = 9;
        /// <summary>The links in the text now shown, all kinds (for the buttons, the smoke and the log).</summary>
        public readonly List<NpcDialog.Link> Links = new List<NpcDialog.Link>();
        /// <summary>A place button was pressed: LokiGame starts the navigation (Game/Navigation.cs Go).</summary>
        public System.Action<NpcDialog.Link> NaviRequested;
        /// <summary>The buttons of this window (gaze choosing: VR/DioramaRig.cs GazeChoosable).</summary>
        public bool Owns(UiButton b) => b != null && b.transform.IsChildOf(transform);
        /// <summary>Wave 28 (wtexts): a page taller than the text area (a long list, a report, a merged dialog page) loses its first lines
        /// above the area (the text sits at the bottom, newest last). ▲ / ▼ beside the text then scroll it, in steps of about three lines;
        /// a new line or a new page starts at the bottom again. The buttons show only when the page does not fit.</summary>
        private UiButton _up, _down;
        private float _scroll, _overflow;
        private string _shown;
        private const float ScrollStrip = 70f;
        private int _page;
        private const int PerPage = 5;
        private NpcDialog _d;
        private string _cutinName;
        private enum Mode { Talk, Menu, Input }

        public static DialogPanel Create(Transform parent, NpcDialog d)
        {
            var rt = UiKit.Panel("NpcDialog", parent, new Vector2(W, H), 0.0007f);
            var p = rt.gameObject.AddComponent<DialogPanel>();
            p._rt = rt; p._d = d;
            p.Build();
            // client wave 13 (w13look), item 24: the look's eye in the top row to the left of X, free of every button in every mode:
            // 46 px (3.2 cm: 2.60° from its 0.71 m), 22 px (1.5 cm) inside the top rim
            p.Eye = LookEye.Add(rt, () => "NpcDialog", new Vector2(W / 2 - 105f, H / 2 - 45f), 46f);
            d.Changed += p.Refresh;
            rt.gameObject.SetActive(false);
            return p;
        }

        private void Build()
        {
            _title = UiKit.TitlePlate(_rt, "", 34, W - 220).Text; // client wave 21 (w21kit): the NPC's name on the gold plate on the frame's top edge (the same 34 px letters)
            _x = UiKit.RoundButton(_rt, "X", new Vector2(W / 2 - 45, H / 2 - 40), new Vector2(60, 50), () => { if (_d.State == NpcDialog.Mode.Menu) _d.CancelMenu(); else _d.PressClose(); }, 30);
            UiKit.Box(_rt, "rule", new Vector2(0, H / 2 - 72), new Vector2(W - 40, 2), UiKit.PanelBorder * 0.7f);

            _cutin = UiKit.Icon(_rt, new Vector2(-W / 2 + 170, -10), new Vector2(300, 460)); // clear of the pager row at the bottom

            var areaGo = new GameObject("textArea", typeof(RectTransform));
            areaGo.transform.SetParent(_rt, false);
            _textArea = (RectTransform)areaGo.transform;
            _textArea.anchorMin = _textArea.anchorMax = new Vector2(0.5f, 0.5f);
            areaGo.AddComponent<RectMask2D>();
            _body = UiKit.Label(_textArea, "", Vector2.zero, Vector2.one, 30, TextAnchor.LowerLeft);
            var brt = (RectTransform)_body.transform; brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            _body.verticalOverflow = VerticalWrapMode.Overflow; _body.lineSpacing = 1.1f;

            _next = UiKit.Button(_rt, Lang.T("Next >"), new Vector2(W / 2 - 130, -H / 2 + 50), new Vector2(210, 64), () => _d.PressNext(), 30);
            _close = UiKit.Button(_rt, Lang.T("Close"), new Vector2(W / 2 - 130, -H / 2 + 50), new Vector2(210, 64), () => _d.PressClose(), 30);

            for (int i = 0; i < PerPage; i++)
            {
                int slot = i;
                _options.Add(UiKit.Button(_rt, "", new Vector2(0, 0), new Vector2(W - 80, 52), () => ChooseSlot(slot), 26));
            }
            _prev = UiKit.Button(_rt, Lang.T("< Prev"), new Vector2(-W / 2 + 120, -H / 2 + 45), new Vector2(180, 56), () => { _page = Mathf.Max(0, _page - 1); Refresh(); }, 24);
            _more = UiKit.Button(_rt, Lang.T("More >"), new Vector2(-W / 2 + 320, -H / 2 + 45), new Vector2(180, 56), () => { _page++; Refresh(); }, 24);
            _cancel = UiKit.Button(_rt, Lang.T("Cancel"), new Vector2(W / 2 - 355, -H / 2 + 45), new Vector2(210, 56), () => _d.CancelMenu(), 26);

            _inputBox = UiKit.Label(UiKit.Box(_rt, "inputBg", new Vector2(0, -80), new Vector2(W - 120, 64), new Color(0, 0, 0, 0.6f)), "", Vector2.zero, new Vector2(W - 140, 60), 34, TextAnchor.MiddleLeft, Color.white);
            _hint = UiKit.Label(_rt, "", new Vector2(0, -130), new Vector2(W - 120, 36), 22, TextAnchor.MiddleLeft, UiKit.Dim);
            int[] deltas = { -100, -10, -1, 1, 10, 100 };
            for (int i = 0; i < deltas.Length; i++)
            {
                int d = deltas[i];
                _pad.Add(UiKit.Button(_rt, (d > 0 ? "+" : "") + d, new Vector2(-W / 2 + 110 + i * 130, -190), new Vector2(115, 56), () => _d.AddNumber(d), 26));
            }
            _clear = UiKit.Button(_rt, Lang.T("Clear"), new Vector2(-W / 2 + 140, -H / 2 + 50), new Vector2(200, 60), () => { _d.Input = ""; Refresh(); }, 26);
            _ok = UiKit.Button(_rt, "OK", new Vector2(W / 2 - 130, -H / 2 + 50), new Vector2(210, 64), () => _d.SubmitInput(), 30);
            _kbButton = UiKit.Button(_rt, Lang.T("Keyboard"), new Vector2(0, -H / 2 + 50), new Vector2(260, 64), () => RequestKeyboard?.Invoke(), 26);
            for (int i = 0; i < NaviButtons; i++)
            {
                int slot = i;
                _naviButtons.Add(UiKit.Button(_rt, "", new Vector2(-W / 2 + 170 + i * 300, -H / 2 + 50), new Vector2(280, 60), () => PressNavi(slot), 24));
            }
            // wave 28 (wtexts): the scroll buttons of a page taller than the area (placed in Refresh, at the area's right edge)
            _up = UiKit.Button(_rt, "▲", Vector2.zero, new Vector2(60, 60), () => ScrollBy(+1), 28);
            _down = UiKit.Button(_rt, "▼", Vector2.zero, new Vector2(60, 60), () => ScrollBy(-1), 28);
        }

        /// <summary>▲ (+1): towards the page's first lines; ▼ (-1): back towards the newest.</summary>
        private void ScrollBy(int dir)
        {
            float step = Mathf.Max(60f, _body.fontSize * _body.lineSpacing * 1.33f * 3f); // about three lines
            _scroll = Mathf.Clamp(_scroll + dir * step, 0f, _overflow);
            ApplyScroll();
        }

        private const float BottomPad = 8f;

        private void ApplyScroll()
        {
            var brt = (RectTransform)_body.transform;
            bool scrollable = _overflow > 0.5f;
            if (scrollable)
            {
                // the text's own rect as tall as the whole page, its bottom at the area's bottom less the scroll: the RectMask2D culls a
                // graphic by its RECT (not by the lines it overflows into), so a rect of the area's height moved down would be culled
                float h = _textArea.rect.height - BottomPad + _overflow;
                brt.anchorMin = new Vector2(0, 0); brt.anchorMax = new Vector2(1, 0); brt.pivot = new Vector2(0.5f, 0);
                brt.offsetMin = new Vector2(0, BottomPad - _scroll); brt.offsetMax = new Vector2(0, BottomPad + h - _scroll);
            }
            else
            {
                brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.pivot = new Vector2(0.5f, 0.5f);
                brt.offsetMin = new Vector2(0, BottomPad); brt.offsetMax = Vector2.zero;
            }
            _up.gameObject.SetActive(scrollable); _down.gameObject.SetActive(scrollable);
            if (scrollable) { _up.Interactable = _scroll < _overflow - 0.5f; _down.Interactable = _scroll > 0.5f; }
        }

        /// <summary>Lays the page out in the area: the full width when it fits, else a strip on the right for ▲ / ▼ and the scroll kept
        /// within the page (a changed text starts at the bottom again).</summary>
        private void FitText(float left, float top, float bottom, string text)
        {
            float right = W / 2 - 30;
            float visible = (top - bottom) - BottomPad;
            _textArea.sizeDelta = new Vector2(right - left, top - bottom);
            _textArea.anchoredPosition = new Vector2((left + right) / 2, (top + bottom) / 2);
            if (text != _shown) { _shown = text; _scroll = 0f; }
            _body.text = text;
            _overflow = Mathf.Max(0f, _body.preferredHeight - visible);
            if (_overflow > 0.5f)
            {
                right -= ScrollStrip; // narrower: the text wraps again and is taller still
                _textArea.sizeDelta = new Vector2(right - left, top - bottom);
                _textArea.anchoredPosition = new Vector2((left + right) / 2, (top + bottom) / 2);
                _overflow = Mathf.Max(0f, _body.preferredHeight - visible);
                float x = W / 2 - 30 - ScrollStrip / 2;
                ((RectTransform)_up.transform).anchoredPosition = new Vector2(x, top - 34);
                ((RectTransform)_down.transform).anchoredPosition = new Vector2(x, bottom + 34);
            }
            _scroll = Mathf.Clamp(_scroll, 0f, _overflow);
            ApplyScroll();
        }

        private void PressNavi(int slot)
        {
            int n = 0;
            foreach (var l in Links)
            {
                if (l.Kind != NpcDialog.Link.Type.Navi) continue;
                if (n++ == slot) { NaviRequested?.Invoke(l); return; }
            }
        }

        /// <summary>The ⌨ button (and a text prompt opening): the game points the VR keyboard at this dialog's input.</summary>
        public System.Action RequestKeyboard;
        private UiButton _kbButton;
        private NpcDialog.Mode _lastMode;
        /// <summary>For the smoke: whether the keyboard button is on screen.</summary>
        public bool KeyboardButtonShown => _kbButton != null && _kbButton.gameObject.activeInHierarchy;

        private void ChooseSlot(int slot)
        {
            int i = _page * PerPage + slot;
            if (i < _d.Options.Count) _d.Choose(_d.Options[i].Key);
        }

        /// <summary>Called by the HUD when the conversation opens, to put the window in front of the player.</summary>
        public void PlaceInFront(Camera head)
        {
            if (head == null) return;
            var f = head.transform.forward; f.y = 0; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
            transform.position = head.transform.position + f * 0.62f + Vector3.up * -0.05f;
            UiKit.FaceHead(transform, head);
        }

        public void Refresh()
        {
            bool open = _d.IsOpen;
            if (gameObject.activeSelf != open) gameObject.SetActive(open);
            if (!open) { _page = 0; _lastMode = NpcDialog.Mode.Closed; return; }

            _title.text = string.IsNullOrEmpty(_d.NpcName) ? "…" : _d.NpcName;
            // cut-in
            if (_d.CutIn != _cutinName)
            {
                _cutinName = _d.CutIn;
                Texture2D tex = null;
                if (!string.IsNullOrEmpty(_cutinName) && LokiBootstrap.Instance != null)
                {
                    var a = LokiBootstrap.Instance.Assets;
                    string file = _cutinName.EndsWith(".bmp", System.StringComparison.OrdinalIgnoreCase) ? _cutinName : _cutinName + ".bmp";
                    tex = a.Texture("À¯ÀúÀÎÅÍÆäÀÌ½º\\illust\\" + file, false); // 유저인터페이스\illust
                    if (tex == a.MissingTexture) tex = null;
                }
                _cutin.sprite = UiKit.ToSprite(tex); _cutin.enabled = tex != null;
            }
            float left = _cutin.enabled ? -W / 2 + 340 : -W / 2 + 30;

            var mode = _d.State == NpcDialog.Mode.Menu ? Mode.Menu : (_d.State == NpcDialog.Mode.InputNumber || _d.State == NpcDialog.Mode.InputString) ? Mode.Input : Mode.Talk;
            float top = H / 2 - 85, bottom = mode == Mode.Talk ? -H / 2 + 95 : mode == Mode.Menu ? 30 : -40;
            Links.Clear();
            string rich = NpcDialog.RichText(_d.Text().TrimEnd('\n'), Links);

            _x.gameObject.SetActive(_d.State == NpcDialog.Mode.Close || _d.State == NpcDialog.Mode.Menu);
            _next.gameObject.SetActive(_d.State == NpcDialog.Mode.Next);
            _close.gameObject.SetActive(_d.State == NpcDialog.Mode.Close);

            // menu
            int pages = Mathf.Max(1, (_d.Options.Count + PerPage - 1) / PerPage);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            for (int i = 0; i < PerPage; i++)
            {
                int idx = _page * PerPage + i;
                bool show = mode == Mode.Menu && idx < _d.Options.Count;
                _options[i].gameObject.SetActive(show);
                if (!show) continue;
                // Options sit under the text, from y = 2 down to just above the pager row (rows 54 px apart, 50 px tall).
                // With a cut-in on the left, the options sit to the right of it.
                var rt = (RectTransform)_options[i].transform;
                float ow = _cutin.enabled ? W - 400 : W - 80, ox = _cutin.enabled ? 170 : 0;
                rt.anchoredPosition = new Vector2(ox, 2 - i * 54f);
                rt.sizeDelta = new Vector2(ow, 50);
                var col = rt.GetComponent<BoxCollider>(); if (col != null) col.size = new Vector3(ow, 50, 8f);
                ((RectTransform)_options[i].Text.transform).sizeDelta = new Vector2(ow - 12, 46);
                _options[i].SetLabel(NpcDialog.RichText(_d.Options[idx].Value, null));
            }
            _prev.gameObject.SetActive(mode == Mode.Menu && _page > 0);
            _more.gameObject.SetActive(mode == Mode.Menu && _page < pages - 1);
            _cancel.Interactable = false;
            _cancel.gameObject.SetActive(false);

            // the places the text links to: a button each (talking, or a menu with no pager in that row)
            bool naviRow = mode == Mode.Talk || (mode == Mode.Menu && pages == 1);
            int maxNavi = mode == Mode.Talk ? NaviButtons : 1;
            int navi = 0;
            for (int i = 0; i < Links.Count && navi < maxNavi; i++)
            {
                var l = Links[i];
                if (l.Kind != NpcDialog.Link.Type.Navi || !naviRow) continue;
                var nrt = (RectTransform)_naviButtons[navi].transform;
                Vector2 npos, nsize;
                if (navi < 2)
                {
                    npos = new Vector2(-W / 2 + 170 + navi * 300, -H / 2 + 50);
                    nsize = new Vector2(280, 60);
                }
                else
                {
                    int r = (navi - 2) / 3, c = (navi - 2) % 3;
                    float spanW = (W / 2 - 30) - left;
                    float bw = (spanW - 16f) / 3f;
                    npos = new Vector2(left + bw * 0.5f + c * (bw + 8f), -H / 2 + 106 + r * 46f);
                    nsize = new Vector2(bw, 42);
                }
                nrt.anchoredPosition = npos;
                nrt.sizeDelta = nsize;
                var ncol = nrt.GetComponent<BoxCollider>(); if (ncol != null) ncol.size = new Vector3(nsize.x, nsize.y, 8f);
                ((RectTransform)_naviButtons[navi].Text.transform).sizeDelta = new Vector2(nsize.x - 12, nsize.y - 4);
                _naviButtons[navi].SetLabel("<color=#" + NpcDialog.NaviColour + ">►</color> " + l.Name.Replace("<", "<​"));
                _naviButtons[navi].gameObject.SetActive(true);
                navi++;
            }
            for (int i = navi; i < NaviButtons; i++) _naviButtons[i].gameObject.SetActive(false);
            if (navi > 2) bottom += ((navi - 2 + 2) / 3) * 46f;
            // its links as coloured names, ^RRGGBB as colours; a page taller than the area gets ▲ / ▼ (wave 28)
            FitText(left, top, bottom, rich);

            // input
            bool input = mode == Mode.Input, num = _d.State == NpcDialog.Mode.InputNumber;
            _inputBox.transform.parent.gameObject.SetActive(input);
            _hint.gameObject.SetActive(input);
            foreach (var b in _pad) b.gameObject.SetActive(input && num);
            _ok.gameObject.SetActive(input); _clear.gameObject.SetActive(input); _kbButton.gameObject.SetActive(input);
            if (input)
            {
                _inputBox.text = num ? (_d.Input.Length > 0 ? _d.Input : "0") + "<color=#ffcc55>|</color>" : VrTextField.WithCaret(_d.Input, _d.Caret, false, true); // text: the caret where it is (w3login)
                _hint.text = num ? Lang.T("Use the buttons or Keyboard, then OK.") : Lang.T("Type on the VR keyboard (it opens by itself; Keyboard brings it back), then OK or Enter.");
            }
            // a text prompt opens the VR keyboard at once (the official client focuses its edit box); numbers have the pad
            if (_d.State == NpcDialog.Mode.InputString && _lastMode != NpcDialog.Mode.InputString) RequestKeyboard?.Invoke();
            _lastMode = _d.State;
        }
    }
}
