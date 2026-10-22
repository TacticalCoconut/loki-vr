using System;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 14 (w14menu), point 7 of the design's headset test of wave 13 ("When I mouse over the I symbol that turns vision select on
    /// and off, just have a pop-up that describes what that button does") and the design's answer 6 (tooltips on icon-only buttons: "Yes, but
    /// they require the user to hover with the LASER for a bit before showing up. This does not activate with the sight select").
    ///   When   the pointing hand's LASER rests on one button for <see cref="Delay"/> s (VR/DioramaRig.cs LaserButton: the laser's own
    ///          button, never the look's); it goes the moment the laser leaves that button (or the button goes). The look never shows it.
    ///   Which  a button's own <see cref="UiButton.Tip"/> (the look's eye, the back arrow, the elbow button, the minimap's zoom, the stat
    ///          and skill +, the quick bar's rows ...), else a few glyphs that mean one thing everywhere (<see cref="GlyphTip"/>: X, the
    ///          page arrows, the scroll arrows, ★). A button with a word on it has none.
    ///   Look   a small dark plate with a gold rim, one line of text (<see cref="FontPx"/> px at <see cref="Scale"/> m a px: 1.5 cm
    ///          letters, 1.3° from 0.65 m), just over the button in its panel's plane, 1 cm towards the eyes, drawn over the panels
    ///          (sorting order 20; the hands stay over it: 30). No collider: the laser passes through it.
    /// </summary>
    public sealed class ButtonTip : MonoBehaviour
    {
        public const float Delay = 0.6f, Scale = 0.00045f, PadPx = 18f, HeightPx = 58f, MaxWidthPx = 900f, GapM = 0.010f, TowardEyesM = 0.010f;
        public const int FontPx = 34;
        private RectTransform _rt, _rim, _bg; private Text _text; private Camera _head;
        private UiButton _on; private float _since; private string _shownText;
        /// <summary>The button whose tip is shown now (null: none), its text, how many times a tip was shown (smoke, log).</summary>
        public UiButton Showing => _rt != null && _rt.gameObject.activeSelf ? _on : null;
        public string ShownText => Showing != null ? _shownText : null;
        public int Shows { get; private set; }
        /// <summary>Seconds the laser has rested on the button it is on now (smoke).</summary>
        public float Resting => _on != null ? Time.unscaledTime - _since : 0f;

        public static ButtonTip Create(Transform parent, Camera head)
        {
            var rt = UiKit.Panel("ButtonTip", parent, new Vector2(300, HeightPx), Scale, false, PanelKind.ByName, PanelLook.Card); // client wave 21 (w21kit): a parchment card of the theme
            var canvas = rt.GetComponent<Canvas>(); if (canvas != null) canvas.sortingOrder = 20;
            var t = rt.gameObject.AddComponent<ButtonTip>();
            t._rt = rt; t._head = head;
            t._rim = (RectTransform)rt.Find("border"); t._bg = (RectTransform)rt.Find("bg"); // (the card's paper and its unused inner box)
            t._text = UiKit.Label(rt, "", Vector2.zero, new Vector2(300 - 2 * PadPx, HeightPx - 6), FontPx, TextAnchor.MiddleCenter, Theme.Ink);
            t._text.horizontalOverflow = HorizontalWrapMode.Overflow;
            rt.gameObject.SetActive(false);
            return t;
        }

        /// <summary>The tip of a button (null: none): its own, else the glyph's.</summary>
        public static string TipOf(UiButton b)
        {
            if (b == null) return null;
            if (b.Tip != null) { var own = b.Tip(); if (!string.IsNullOrEmpty(own)) return own; }
            return GlyphTip(b.Text != null ? b.Text.text : null);
        }

        /// <summary>The glyphs that mean one thing on every panel (a button labelled with one of them and no tip of its own).</summary>
        public static string GlyphTip(string label)
        {
            switch ((label ?? "").Trim())
            {
                case "X": return Lang.T("Close");
                case "<": return Lang.T("Previous page");
                case ">": return Lang.T("Next page");
                case "▲": return Lang.T("Scroll up");
                case "▼": return Lang.T("Scroll down");
                case "★": return Lang.T("Put on the quick bar");
                case MenuNav.ArrowGlyph: return Lang.T("~tip~Back");
                default: return null;
            }
        }

        /// <summary>Once a frame: <paramref name="laserButton"/> is the button the pointing hand's laser is on (null: none).</summary>
        public void Tick(UiButton laserButton)
        {
            float now = Time.unscaledTime;
            if (laserButton != null && !laserButton.isActiveAndEnabled) laserButton = null;
            if (laserButton != _on) { _on = laserButton; _since = now; Hide(); }
            if (_on == null) return;
            string tip = TipOf(_on);
            if (string.IsNullOrEmpty(tip)) { Hide(); return; }
            if (now - _since < Delay) return;
            if (!_rt.gameObject.activeSelf) { _rt.gameObject.SetActive(true); Shows++; _shownText = null; Debug.Log($"[Tip] '{tip}' on {_on.name} after {now - _since:0.00} s of the laser"); }
            if (tip != _shownText) Fit(tip);
            PlaceOver(_on);
        }

        public void Hide() { if (_rt != null && _rt.gameObject.activeSelf) _rt.gameObject.SetActive(false); }

        /// <summary>The plate as wide as its one line of text.</summary>
        private void Fit(string tip)
        {
            _shownText = tip; _text.text = tip;
            var gs = _text.GetGenerationSettings(new Vector2(10000f, HeightPx));
            gs.resizeTextForBestFit = false; gs.horizontalOverflow = HorizontalWrapMode.Overflow; gs.scaleFactor = 1f;
            float w = Mathf.Min(MaxWidthPx, Mathf.Ceil(_text.cachedTextGeneratorForLayout.GetPreferredWidth(tip, gs)) + 2f * PadPx);
            w = Mathf.Max(w, 120f);
            _rt.sizeDelta = new Vector2(w, HeightPx); _rim.sizeDelta = new Vector2(w, HeightPx); _bg.sizeDelta = new Vector2(w - 4f, HeightPx - 4f);
            ((RectTransform)_text.transform).sizeDelta = new Vector2(w - 2f * PadPx, HeightPx - 6f);
        }

        /// <summary>Just over the button, in its panel's plane, a little towards the eyes.</summary>
        private void PlaceOver(UiButton b)
        {
            var brt = (RectTransform)b.transform;
            var c = new Vector3[4]; brt.GetWorldCorners(c); // bottom left, top left, top right, bottom right
            var top = (c[1] + c[2]) * 0.5f; var up = (c[1] - c[0]); float upLen = up.magnitude; up = upLen > 1e-6f ? up / upLen : brt.up;
            var facet = brt.GetComponentInParent<UiFacet>(); // client wave 20 (w20char): a button on a turned facet (the wide Character board's wings) gets the facet's turn
            var rot = facet != null ? facet.transform.rotation : b.Panel != null ? b.Panel.rotation : brt.rotation;
            float half = HeightPx * Scale * 0.5f;
            var pos = top + up * (GapM + half);
            if (_head != null) { var toEyes = _head.transform.position - pos; if (toEyes.sqrMagnitude > 1e-6f) pos += toEyes.normalized * TowardEyesM; }
            transform.SetPositionAndRotation(pos, rot);
            transform.localScale = Vector3.one * Scale; // (its parent is the rig, as every panel's)
        }
    }
}
