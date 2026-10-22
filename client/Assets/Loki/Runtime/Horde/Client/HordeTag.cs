using System.Collections.Generic;
using Loki.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 15 (w15hfx), point 16 of the design's wave 14 list ("the HP bar should be visible above the character's head as should any
    /// other important stats that require to be immediately aware of"): the tag over a run character, in place of wave 8's name plate.
    /// FOUND: wave 8 attached a UI/Overhead.cs plate (name + HP bar) but set the HP through Unit.Overhead, which a unit made with
    /// Unit.Create never has (Sprites/Unit.cs sets it only in SetupInteraction): the HP bar was never shown and the run characters'
    /// damage numbers never popped. This tag is the run character's own, set every frame by HordePlayers.
    ///   Mine:    the HP bar (<see cref="MyBarPx"/>, its numbers inside "812 / 1000"), the party's EXP bar under it (the next card), and a
    ///            status word when it matters (fallen with the rise's progress, paused). No name (it is me).
    ///   Allies:  the name, a smaller HP bar (<see cref="AllyBarPx"/>), and the status word (fallen / choosing / away / paused).
    /// Every HP bar: green over half, yellow over a quarter, red below and pulsing; a white trail shows the HP just lost (it drains in
    /// <see cref="TrailSeconds"/>); a fallen character's bar is grey and fills with the rise's progress.
    /// Size: UI/Overhead.cs's (0.35 mm a canvas pixel at arm's length, times Settings → Comfort's name size, growing with distance beyond
    /// 1.2 m so it keeps its apparent size): my bar is 63 × 7 mm at the table, an ally's 49 × 5 mm (the Horde table: 1 cell ≈ 2 cm, a
    /// character ≈ 5 cm tall). Loki's own numbers.
    /// </summary>
    public sealed class HordeTag : MonoBehaviour
    {
        public const float MetresPerPx = 0.00035f, HeightCells = 2.7f, TrailSeconds = 0.6f;
        public static readonly Vector2 MyBarPx = new Vector2(180, 20), AllyBarPx = new Vector2(140, 14);
        private RectTransform _root, _hpBg, _hpFill, _trail, _expBg, _expFill;
        private Text _name, _hpText, _status;
        private Image _fillImg;
        private bool _mine; private Vector2 _bar;
        private float _shown = -1f, _trailFrac = -1f, _trailHold;
        private readonly List<(Text t, float born)> _pops = new List<(Text, float)>();
        /// <summary>What the tag shows now (the smoke, the log).</summary>
        public float HpShown => _shown;
        public bool ExpShown => _expBg != null && _expBg.gameObject.activeSelf;
        public string StatusShown => _status != null && _status.gameObject.activeSelf ? _status.text : "";
        public string HpTextShown => _hpText != null && _hpText.gameObject.activeSelf ? _hpText.text : "";
        public bool Mine => _mine;
        public float BarWidthMetres => _root != null ? _bar.x * _root.lossyScale.x : 0f;
        public int Pops { get; private set; }

        public static HordeTag Attach(Transform unit, string name, bool mine)
        {
            var t = unit.gameObject.AddComponent<HordeTag>();
            t.Build(name, mine);
            return t;
        }

        private void Build(string name, bool mine)
        {
            _mine = mine; _bar = mine ? MyBarPx : AllyBarPx;
            var go = new GameObject("horde-tag", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 5;
            _root = (RectTransform)go.transform; _root.sizeDelta = new Vector2(400, 200);
            // (bottom to top) the status word, the EXP bar (mine), the HP bar, the name (allies)
            _status = UiKit.Label(_root, "", new Vector2(0, mine ? -26 : -20), new Vector2(360, 30), mine ? 22 : 20, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.5f));
            Outlined(_status);
            _hpBg = UiKit.Box(_root, "hpbg", Vector2.zero, _bar, new Color(0.02f, 0.02f, 0.04f, 0.88f));
            HordeLook.GoldRim(_hpBg, 2f, !mine); // client wave 21 (w21horde), THE PANEL THEME: the theme's thin gold edge (mine full, an ally's faint: mine stands out)
            _trail = Fill(_hpBg, "trail", new Color(1f, 1f, 1f, 0.85f));
            _hpFill = Fill(_hpBg, "hp", new Color(0.25f, 0.9f, 0.35f));
            _fillImg = _hpFill.GetComponent<Image>();
            if (mine)
            {
                _hpText = UiKit.Label(_hpBg, "", Vector2.zero, new Vector2(_bar.x, _bar.y + 4), 16, TextAnchor.MiddleCenter, Color.white);
                Outlined(_hpText);
                _expBg = UiKit.Box(_root, "expbg", new Vector2(0, -(_bar.y / 2 + 6)), new Vector2(_bar.x, 7), new Color(0.02f, 0.02f, 0.04f, 0.85f));
                _expFill = Fill(_expBg, "exp", new Color(0.35f, 0.65f, 1f));
            }
            else
            {
                _name = UiKit.Label(_root, name, new Vector2(0, _bar.y / 2 + 16), new Vector2(400, 30), 22, TextAnchor.MiddleCenter, new Color(0.85f, 0.92f, 1f));
                Outlined(_name);
            }
            _status.gameObject.SetActive(false);
        }

        private static void Outlined(Text t) { var o = t.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0, 0, 0, 0.9f); o.effectDistance = new Vector2(2, -2); }

        private static RectTransform Fill(RectTransform bg, string name, Color c)
        {
            var size = bg.sizeDelta;
            var f = UiKit.Box(bg, name, Vector2.zero, new Vector2(size.x - 4, size.y - 4), c);
            f.anchorMin = f.anchorMax = new Vector2(0, 0.5f); f.pivot = new Vector2(0, 0.5f); f.anchoredPosition = new Vector2(2, 0);
            return f;
        }

        /// <summary>This frame's values. <paramref name="status"/>: "" for none.</summary>
        public void Set(string name, int hp, int maxHp, bool fallen, float revive, float exp01, string status)
        {
            if (_root == null) return;
            if (_name != null && _name.text != name) _name.text = name;
            float w = _bar.x - 4, h = _bar.y - 4;
            float frac = fallen ? Mathf.Clamp01(revive) : maxHp > 0 ? Mathf.Clamp01(hp / (float)maxHp) : 0f;
            // the white trail: HP just lost stays white for a moment, then drains to the bar
            if (_trailFrac < 0f || fallen || frac >= _trailFrac) _trailFrac = frac; // the first frame, a rise, a heal: no trail
            else
            {
                if (frac < _shown - 1e-4f) _trailHold = Time.time + 0.25f; // a new loss: the trail waits a moment, then drains
                if (Time.time > _trailHold) _trailFrac = Mathf.MoveTowards(_trailFrac, frac, Time.deltaTime / TrailSeconds);
            }
            _shown = frac;
            _hpFill.sizeDelta = new Vector2(w * frac, h);
            _trail.sizeDelta = new Vector2(w * Mathf.Max(_trailFrac, frac), h);
            Color c = fallen ? new Color(0.6f, 0.62f, 0.68f) : frac > 0.5f ? new Color(0.25f, 0.9f, 0.35f) : frac > 0.25f ? new Color(0.98f, 0.8f, 0.2f) : new Color(1f, 0.25f, 0.2f);
            if (!fallen && frac <= 0.25f) c = Color.Lerp(c, Color.white, 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.time * 12f))); // low HP: the bar pulses
            if (!_mine && !fallen) c = Color.Lerp(c, new Color(0.55f, 0.85f, 1f), 0.2f); // an ally's a little cooler (mine stands out)
            _fillImg.color = c;
            if (_hpText != null)
            {
                string t = fallen ? "" : hp + " / " + maxHp;
                if (_hpText.text != t) _hpText.text = t;
            }
            if (_expFill != null) _expFill.sizeDelta = new Vector2((_bar.x - 4) * Mathf.Clamp01(exp01), 3);
            bool st = !string.IsNullOrEmpty(status);
            if (_status.gameObject.activeSelf != st) _status.gameObject.SetActive(st);
            if (st && _status.text != status) _status.text = status;
        }

        // ---- client wave 17 (h17read), the readability proposal's D4: an ally's HP bar flashes red when it is hurt (HordeReadability.AllyHit)
        public const float HurtFlashSeconds = 0.3f;
        private float _hurtFrom = -9f;
        /// <summary>Flashes of the bar's back so far (the smoke).</summary>
        public int Hurts { get; private set; }
        /// <summary>The bar's back flashes red for <see cref="HurtFlashSeconds"/>.</summary>
        public void Hurt() { _hurtFrom = Time.time; Hurts++; }
        private Image _bgImg;
        private void HurtFlash()
        {
            if (_hpBg == null) return;
            if (_bgImg == null) _bgImg = _hpBg.GetComponent<Image>();
            if (_bgImg == null) return;
            float u = (Time.time - _hurtFrom) / HurtFlashSeconds;
            var back = new Color(0.02f, 0.02f, 0.04f, 0.88f);
            _bgImg.color = u >= 0f && u < 1f ? Color.Lerp(new Color(1f, 0.12f, 0.08f, 0.95f), back, u * u) : back;
        }

        // ---- client wave 18 (h18feel), the design's point 26: a claimed objective's gift said over my runner, in the design's style ("4% Heal/s for 15
        // seconds"; HordeObjectives.GiftText): over the HP bar, on a dark plate, it pops in (1.25 → 1 in GiftPopSeconds), stays GiftSeconds,
        // then fades over GiftFadeSeconds (Loki's own numbers: the design's "a small text that quickly explains what it does")
        public const float GiftSeconds = 3f, GiftFadeSeconds = 0.6f, GiftPopSeconds = 0.15f;
        public const int GiftFontPx = 26;
        private Text _gift; private RectTransform _giftBack; private Image _giftBackImg, _giftRim; private float _giftFrom = -9f;
        /// <summary>The gift text shown now ("" when none), and how many were shown (the smoke).</summary>
        public string GiftShown => _gift != null && _gift.gameObject.activeSelf ? _gift.text : "";
        public int Gifts { get; private set; }
        /// <summary>The gift's text and its plate (the smoke reads their place and size).</summary>
        public RectTransform GiftRect => _gift != null ? _gift.rectTransform : null;

        /// <summary>Shows <paramref name="text"/> over the bar in <paramref name="color"/> (a new gift replaces the one shown).</summary>
        public void Gift(string text, Color color)
        {
            if (_root == null || string.IsNullOrEmpty(text)) return;
            float y = _bar.y / 2f + (_name != null ? 36f : 10f) + (GiftFontPx + 12f) / 2f;
            if (_gift == null)
            {
                _giftBack = UiKit.Soft(_root, "gift-back", new Vector2(0, y), new Vector2(200, GiftFontPx + 12), new Color(0.02f, 0.02f, 0.04f, 0.62f)); // (client wave 21: the theme's soft strip with its thin gold edge)
                _giftBackImg = _giftBack.GetComponent<Image>();
                _giftRim = HordeLook.GoldRim(_giftBack, 0f);
                _gift = UiKit.Label(_root, "", new Vector2(0, y), new Vector2(760, GiftFontPx + 10), GiftFontPx, TextAnchor.MiddleCenter, Color.white);
                Outlined(_gift);
            }
            _gift.text = text; _gift.color = new Color(color.r, color.g, color.b, 1f); _giftFrom = Time.time; Gifts++;
            _giftBack.sizeDelta = new Vector2(Mathf.Min(760f, _gift.preferredWidth + 28f), GiftFontPx + 12);
            _gift.gameObject.SetActive(true); _giftBack.gameObject.SetActive(true);
            GiftStep();
        }

        private void GiftStep()
        {
            if (_gift == null || !_gift.gameObject.activeSelf) return;
            float age = Time.time - _giftFrom;
            if (age > GiftSeconds + GiftFadeSeconds || age < 0f) { _gift.gameObject.SetActive(false); _giftBack.gameObject.SetActive(false); return; }
            float a = 1f - Mathf.Clamp01((age - GiftSeconds) / GiftFadeSeconds), pop = 1f + 0.25f * Mathf.Clamp01(1f - age / GiftPopSeconds);
            var c = _gift.color; c.a = a; _gift.color = c;
            _giftBackImg.color = new Color(0.02f, 0.02f, 0.04f, 0.62f * a);
            if (_giftRim != null) _giftRim.color = new Color(HordeLook.Edge.r, HordeLook.Edge.g, HordeLook.Edge.b, HordeLook.Edge.a * a);
            _gift.rectTransform.localScale = _giftBack.localScale = Vector3.one * pop;
        }

        /// <summary>A number over the character (hurt red, healed green: HitNumbers' colours), in Overhead's look.</summary>
        public void Pop(string text, Color color)
        {
            if (_root == null) return;
            var t = UiKit.Label(_root, text, new Vector2(Random.Range(-40f, 40f), 40), new Vector2(300, 60), 40, TextAnchor.MiddleCenter, color);
            Outlined(t);
            _pops.Add((t, Time.time)); Pops++;
        }

        private void LateUpdate()
        {
            if (_root == null) return;
            float ps = transform.lossyScale.x; if (ps < 1e-6f) return;
            _root.position = transform.position + Vector3.up * (HeightCells * ps);
            float k = VR.Comfort.NameScale;
            var head = Camera.main;
            if (head != null)
            {
                var d = _root.position - head.transform.position; float dist = d.magnitude;
                if (dist > 1e-3f) _root.rotation = Quaternion.LookRotation(d / dist, Vector3.up);
                k *= Mathf.Clamp(dist / Overhead.FarMetres, 1f, 4f);
            }
            _root.localScale = Vector3.one * (MetresPerPx * k / ps);
            HurtFlash(); // client wave 17 (h17read), D4
            GiftStep(); // client wave 18 (h18feel), point 26
            for (int i = _pops.Count - 1; i >= 0; i--)
            {
                var (t, born) = _pops[i];
                float age = Time.time - born;
                if (t == null || age > 1.2f) { if (t != null) Destroy(t.gameObject); _pops.RemoveAt(i); continue; }
                var rt = (RectTransform)t.transform; rt.anchoredPosition += new Vector2(0, 90f * Time.deltaTime);
                var c = t.color; c.a = 1f - Mathf.Clamp01((age - 0.6f) / 0.6f); t.color = c;
            }
        }
    }
}
