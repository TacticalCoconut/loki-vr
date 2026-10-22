using Loki.Game;
using Loki.Sprites;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// The damage numbers of one landed hit (client wave 3, w3combat): the pop over the target through <see cref="Overhead.PopNumber"/>,
    /// and a monster's running combo total. Colours follow roBrowserLegacy Renderer/Effects/Damage.js:338-420 (the reference; rB is a
    /// reimplementation, not the official client): SP damage blue, a hit on a player red, a combo total yellow, a critical yellow (rB
    /// draws it on a grey "crit" badge; Loki appends "!"), anything else white. "Miss" is drawn over the target (rB draws it over the
    /// attacker, Entity.js:700 — see the re-audit §7). The lucky dodge reads "Lucky!".
    /// </summary>
    public static class HitNumbers
    {
        public static readonly Color White = Color.white, Red = new Color(1f, 0.35f, 0.3f), Yellow = new Color(1f, 0.85f, 0.2f), Blue = new Color(0.45f, 0.65f, 1f);
        /// <summary>The last text shown per target (smoke).</summary>
        public static string LastText { get; private set; }
        public static int Pops { get; private set; }

        public static void Show(ScheduledHit h)
        {
            var dst = h.Dst; if (dst == null || dst.Overhead == null) return;
            string text; Color c;
            if (h.Has(HitFlags.Lucky)) { text = "Lucky!"; c = dst.IsPlayerType ? Red : White; }
            else if (h.Has(HitFlags.Miss)) { text = "Miss"; c = dst.IsPlayerType ? Red : White; }
            else
            {
                bool crit = h.Has(HitFlags.Crit);
                text = crit ? h.Amount + "!" : h.Amount.ToString();
                // Damage.js order: SP, (heal), ENEMY (a player target), COMBO, CRIT
                c = h.Has(HitFlags.Sp) ? Blue : dst.IsPlayerType ? Red : crit ? Yellow : White;
            }
            dst.Overhead.PopNumber(text, c);
            LastText = text; Pops++;
            if (h.Combo > 0) ComboLabel.For(dst).Show(h.Combo, h.Has(HitFlags.ComboFinal));
        }
    }

    /// <summary>
    /// A monster's combo total above its damage numbers: each hit of a multi-hit attack or skill raises it, the final total stays
    /// 1.5 s and fades (rB Damage.js:566-585: a new combo value replaces the previous one; a non-final one goes after 15 % of its
    /// 1.5 s, a final one runs its whole life). Lives under the unit's Overhead canvas, so it billboards and scales with it.
    /// </summary>
    public sealed class ComboLabel : MonoBehaviour
    {
        private Text _text; private float _born, _life; private bool _final;
        public static int Shown { get; private set; }
        public string Text => _text != null && _text.gameObject.activeSelf ? _text.text : "";

        public static ComboLabel For(Unit u)
        {
            var c = u.GetComponent<ComboLabel>();
            return c != null ? c : u.gameObject.AddComponent<ComboLabel>();
        }

        public void Show(int total, bool final)
        {
            if (_text == null)
            {
                var ov = transform.Find("overhead") as RectTransform; // the Overhead's world-space canvas (Overhead.Build)
                if (ov == null) return;
                _text = UiKit.Label(ov, "", new Vector2(0, 120), new Vector2(360, 70), 52, TextAnchor.MiddleCenter, HitNumbers.Yellow);
                var o = _text.gameObject.AddComponent<Outline>(); o.effectColor = Color.black; o.effectDistance = new Vector2(3, -3);
            }
            _text.text = total.ToString(); _text.color = HitNumbers.Yellow; _text.gameObject.SetActive(true);
            _born = Time.time; _final = final; _life = final ? 1.5f : 1.5f * 0.15f; // Damage.js:577: a non-final value goes after 15 % of its 1.5 s
            ((RectTransform)_text.transform).anchoredPosition = new Vector2(0, 120);
            Shown++;
        }

        private void LateUpdate()
        {
            if (_text == null || !_text.gameObject.activeSelf) return;
            float age = Time.time - _born;
            if (age > _life) { _text.gameObject.SetActive(false); return; }
            if (_final)
            {
                var c = _text.color; c.a = 1f - Mathf.Clamp01(age / _life); _text.color = c;
                ((RectTransform)_text.transform).anchoredPosition = new Vector2(0, 120 + 40f * age); // rB lifts it by perc (Damage.js:584)
            }
        }
    }
}
