using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 11 (w11menu): a text that scrolls (specified after the 2026-10-06 playtest: "When I'm inspecting an item that
    /// has a long description, I can't scroll down to see the stats": the item details had one text box that cut what did not fit,
    /// and Ragnarok Online's descriptions carry their stat lines at the end). A clipped view (RectMask2D, as the chat log and the NPC
    /// dialog use) with the whole text behind it, moved up and down by:
    ///   the stick:   the pointer hand's stick pushed up / down while the laser or the gaze is on the text (the world does not turn
    ///                meanwhile: UiButton.Stick, VR/DioramaRig.Ui.cs);
    ///   the buttons: ▲ / ▼ at its right edge, most of a view at a time;
    ///   the trigger: on the text itself, the next part (after the end, the top again) — for choosing by looking.
    /// A mark between the two buttons shows where the view is and how much of the text it shows; with a text that fits, the buttons
    /// and the mark are hidden and nothing moves. A new text starts at its top.
    /// </summary>
    public sealed class UiScrollText : MonoBehaviour, IUiStick
    {
        public const float BarW = 46f, StickSpeed = 620f;
        public Text Text { get; private set; }
        private RectTransform _view, _content, _thumb, _rail; private UiButton _up, _down, _area;
        private float _viewH, _contentH, _offset; private string _last = ""; private bool _withButtons = true;
        /// <summary>How far the view is from the text's top, in pixels, and how far it can go.</summary>
        public float Offset => _offset;
        public float MaxOffset => Mathf.Max(0f, _contentH - _viewH);
        public float ViewHeight => _viewH;
        public float ContentHeight => _contentH;
        public bool Scrollable => MaxOffset > 0.5f;
        /// <summary>The button over the text (the laser / gaze target the stick scrolls).</summary>
        public UiButton Area => _area;

        /// <summary>pos / size: the whole piece in the parent's pixels (the text's view is size.x - <see cref="BarW"/> wide).</summary>
        public static UiScrollText Create(RectTransform parent, Vector2 pos, Vector2 size, int fontSize, Color? color = null, bool buttons = true)
        {
            var root = UiKit.Box(parent, "scrolltext", pos, size, new Color(0, 0, 0, 0));
            var s = root.gameObject.AddComponent<UiScrollText>();
            float vw = size.x - BarW; s._viewH = size.y;
            // the target under the text: the lowest priority, so a button drawn over a scrolling text is still the one chosen
            s._area = UiKit.Button(root, "", new Vector2(-BarW / 2, 0), new Vector2(vw, size.y), () => s.Page(), 12);
            s._area.name = "scrollarea"; s._area.Priority = -10; s._area.Stick = s;
            s._area.Normal = new Color(1, 1, 1, 0f); s._area.Hover = new Color(0.35f, 0.45f, 0.7f, 0.10f); s._area.SetHover(false);
            var viewGo = new GameObject("view", typeof(RectTransform));
            viewGo.transform.SetParent(root, false); viewGo.layer = root.gameObject.layer;
            s._view = (RectTransform)viewGo.transform;
            s._view.anchorMin = s._view.anchorMax = new Vector2(0.5f, 0.5f); s._view.pivot = new Vector2(0.5f, 0.5f);
            s._view.anchoredPosition = new Vector2(-BarW / 2, 0); s._view.sizeDelta = new Vector2(vw, size.y);
            viewGo.AddComponent<RectMask2D>();
            s.Text = UiKit.Label(s._view, "", Vector2.zero, new Vector2(vw - 12, size.y), fontSize, TextAnchor.UpperLeft, color);
            s.Text.verticalOverflow = VerticalWrapMode.Overflow;
            s._content = (RectTransform)s.Text.transform;
            s._content.anchorMin = s._content.anchorMax = new Vector2(0.5f, 1f); s._content.pivot = new Vector2(0.5f, 1f);
            s._content.anchoredPosition = Vector2.zero;
            float bx = size.x / 2 - BarW / 2 + 2, bh = Mathf.Min(54f, size.y * 0.28f);
            s._up = UiKit.Button(root, "▲", new Vector2(bx, size.y / 2 - bh / 2), new Vector2(BarW - 6, bh), () => s.ScrollBy(-s._viewH * 0.8f), 22);
            s._down = UiKit.Button(root, "▼", new Vector2(bx, -size.y / 2 + bh / 2), new Vector2(BarW - 6, bh), () => s.ScrollBy(s._viewH * 0.8f), 22);
            s._up.Stick = s; s._down.Stick = s; s._withButtons = buttons;
            if (!buttons) { s._up.gameObject.SetActive(false); s._down.gameObject.SetActive(false); } // the mark alone: the stick and the trigger on the text scroll it
            float railH = Mathf.Max(8f, buttons ? size.y - 2 * bh - 12 : size.y - 8);
            s._rail = UiKit.Box(root, "rail", new Vector2(bx, 0), new Vector2(6, railH), new Color(1, 1, 1, 0.10f));
            var onTheme = Theme.SurfaceOf(root); // client wave 21 (w21kit): on canvas or parchment the mark is leather brown (gold is faint on cream)
            s._thumb = UiKit.Box(s._rail, "thumb", Vector2.zero, new Vector2(10, railH), onTheme != null && onTheme.Light ? Theme.Leather : new Color(0.85f, 0.7f, 0.35f, 0.95f));
            s.Layout();
            return s;
        }

        /// <summary>Scrolls by pixels (+ = further down the text).</summary>
        public void ScrollBy(float px) { Measure(); _offset = Mathf.Clamp(_offset + px, 0f, MaxOffset); Layout(); }
        public void ScrollTo(float px) { Measure(); _offset = Mathf.Clamp(px, 0f, MaxOffset); Layout(); }
        /// <summary>The next part of the text; from the end, the top again.</summary>
        public void Page() { Measure(); if (_offset >= MaxOffset - 0.5f) _offset = 0f; else _offset = Mathf.Min(MaxOffset, _offset + _viewH * 0.8f); Layout(); }

        public bool OnStick(Vector2 stick, float dt)
        {
            if (Mathf.Abs(stick.y) < 0.3f || Mathf.Abs(stick.y) < Mathf.Abs(stick.x)) return false; // sideways: the world turns as always
            if (Scrollable) ScrollBy(-stick.y * StickSpeed * dt); // stick up = towards the top
            return true;
        }

        /// <summary>The text's full height at the view's width (and a new text starts at its top).</summary>
        public void Measure()
        {
            if (Text == null) return;
            string t = Text.text ?? "";
            if (!ReferenceEquals(t, _last) && t != _last) _offset = 0f;
            _last = t;
            var settings = Text.GetGenerationSettings(new Vector2(_content.rect.width, 0f));
            settings.verticalOverflow = VerticalWrapMode.Overflow; settings.resizeTextForBestFit = false;
            float ppu = Text.pixelsPerUnit > 0 ? Text.pixelsPerUnit : 1f;
            _contentH = t.Length == 0 ? 0f : Text.cachedTextGeneratorForLayout.GetPreferredHeight(t, settings) / ppu + 12f;
            _offset = Mathf.Clamp(_offset, 0f, MaxOffset);
        }

        private void Layout()
        {
            if (_content == null) return;
            _content.sizeDelta = new Vector2(_content.sizeDelta.x, Mathf.Max(_contentH, _viewH));
            _content.anchoredPosition = new Vector2(0, _offset);
            bool can = Scrollable;
            bool canB = can && _withButtons;
            if (_up.gameObject.activeSelf != canB) { _up.gameObject.SetActive(canB); _down.gameObject.SetActive(canB); }
            if (_rail.gameObject.activeSelf != can) _rail.gameObject.SetActive(can);
            if (!can) return;
            float railH = _rail.rect.height, share = Mathf.Clamp(_viewH / Mathf.Max(_contentH, 1f), 0.08f, 1f);
            float th = railH * share, t = MaxOffset > 0 ? _offset / MaxOffset : 0f;
            _thumb.sizeDelta = new Vector2(10, th);
            _thumb.anchoredPosition = new Vector2(0, (railH - th) * (0.5f - t));
            bool top = _offset <= 0.5f, end = _offset >= MaxOffset - 0.5f;
            _up.Normal = top ? new Color(0.28f, 0.28f, 0.3f) : UiKit.ButtonBg; _down.Normal = end ? new Color(0.28f, 0.28f, 0.3f) : UiKit.ButtonBg;
            if (!_up.Hovered) _up.SetHover(false); if (!_down.Hovered) _down.SetHover(false);
        }

        private void LateUpdate()
        {
            if (Text == null) return;
            string t = Text.text ?? "";
            if (ReferenceEquals(t, _last)) return;
            Measure(); Layout();
        }

        private void OnEnable() { if (Text != null) { Measure(); Layout(); } }
    }
}
