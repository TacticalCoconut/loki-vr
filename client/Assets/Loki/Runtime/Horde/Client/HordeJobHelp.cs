using System.Collections.Generic;
using System.Text;
using Loki.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 18 (h18rules), the design's point 22: "When I am mousing over the different class options on Horde mode, they all tell me like,
    /// give Envenom and open new cards, but I don't know what Envenom does. When I mouse over them, allow the player to see what they do."
    /// The job choice's HELP PANEL, in the Settings help's way (UI/SettingHelp.cs: the pointing hand's LASER only, never the look; AT ONCE,
    /// the design's wave 18 point 17 for help panels):
    ///   When   the laser is on a job card of the open choice (either hand's laser: DioramaRig.LaserButton, else OffLaserButton); it follows
    ///          the laser to another card at once and goes <see cref="Grace"/> s after the laser left every job card, or with the choice.
    ///   Where  over the choice's panel, centred on the card (kept within the panel's width), in its plane, a little towards the eyes; over
    ///          the panels (sorting order 20), no collider (the laser passes through it).
    ///   What   (<see cref="Text"/>) the signature weapon's name, what it does and its numbers: the very sentences and numbers of its own
    ///          card (HordeText.CardDoes / CardNumbers of a "new weapon" card: they stay in sync with the tables), then what the job opens:
    ///          its new weapons and passives by name.
    /// </summary>
    public sealed class HordeJobHelp : MonoBehaviour
    {
        public const float Grace = 0.25f, WidthPx = 600f, PadPx = 22f, GapPx = 16f, TowardEyesM = 0.012f;
        public const int TitlePx = 32, BodyPx = 24, MaxNames = 8;
        /// <summary>Client wave 21 (w21horde): how far the choice's title plate stands over its frame's top edge, px (38 px letters: the
        /// plate is 60 px high, its middle in the middle of the wood).</summary>
        public const float PlateOverPx = 12f;
        private RectTransform _rt, _rim, _bg; private Text _text; private Camera _head;
        private int _on = -1, _job; private float _lastSeen; private bool _shown; private string _shownText;
        /// <summary>The job whose help is shown (0: none), its text, how many times it was shown (smoke, log).</summary>
        public int ShowingJob => _shown ? _job : 0;
        public string ShownText => _shown ? _shownText : null;
        public int Shows { get; private set; }
        public Transform Panel => _rt;

        public static HordeJobHelp Create(Transform parent, Camera head, float scale)
        {
            // client wave 21 (w21horde), THE PANEL THEME: a parchment card of the kit (as the Settings help, UI/SettingHelp.cs), its text
            // the darkest ink; the weapon's name and numbers in the cards' own inks (Text)
            var rt = UiKit.Panel("HordeJobHelp", parent, new Vector2(WidthPx, 200f), scale, false, PanelKind.ByName, PanelLook.Card);
            var canvas = rt.GetComponent<Canvas>(); if (canvas != null) canvas.sortingOrder = 20;
            var h = rt.gameObject.AddComponent<HordeJobHelp>();
            h._rt = rt; h._head = head;
            h._rim = (RectTransform)rt.Find("border"); h._bg = (RectTransform)rt.Find("bg"); // (the card's paper and its unused inner box: Fit sizes both)
            h._text = UiKit.Label(rt, "", Vector2.zero, new Vector2(WidthPx - 2f * PadPx, 160f), BodyPx, TextAnchor.UpperLeft, HordeLook.DeepInk);
            h._text.horizontalOverflow = HorizontalWrapMode.Wrap; h._text.verticalOverflow = VerticalWrapMode.Overflow; h._text.resizeTextForBestFit = false;
            rt.gameObject.SetActive(false);
            return h;
        }

        /// <summary>Client wave 21 (w21horde): the card's inks (rich-text colours): the weapon's name (the kit's Accent), its numbers (the
        /// cards' number ink), the lists of what the job opens (the kit's Wood: 12.2 : 1 on parchment).</summary>
        private static string TitleHex => Theme.Hex(Theme.Accent);
        private static string NumHex => Theme.Hex(HordeLook.NumberInk);
        private static string ListHex => Theme.Hex(Theme.Wood);

        /// <summary>The help of job <paramref name="jobId"/>: its signature weapon (name, what it does, its numbers), what it opens.</summary>
        public static string Text(HordeContent c, int jobId)
        {
            var j = c?.Job(jobId);
            if (j == null) return null;
            var sb = new StringBuilder();
            var sig = j.SignatureWeapon > 0 ? c.Weapon(j.SignatureWeapon) : null;
            if (sig != null && sig.Levels.Length > 0)
            {
                var k = new HordeCard { Kind = HordeCardKind.NewWeapon, DefId = sig.Id, Level = 1 };
                sb.Append("<size=").Append(TitlePx).Append("><b><color=#").Append(TitleHex).Append('>').Append(HordeText.Weapon(c, sig.Id)).Append("</color></b></size>");
                string does = HordeText.CardDoes(c, k);
                if (does.Length > 0) sb.Append('\n').Append(does);
                var nums = HordeText.CardNumbers(c, k);
                if (nums.Count > 0) sb.Append('\n').Append("<color=#").Append(NumHex).Append('>').Append(string.Join(" · ", nums)).Append("</color>");
            }
            else sb.Append("<size=").Append(TitlePx).Append("><b><color=#").Append(TitleHex).Append('>').Append(HordeText.Job(jobId)).Append("</color></b></size>");
            var weapons = new List<string>(); var passives = new List<string>();
            foreach (int id in j.Weapons) if (id != j.SignatureWeapon && c.Weapon(id) != null) weapons.Add(HordeText.Weapon(c, id));
            foreach (int id in j.Passives) if (c.Passive(id) != null) passives.Add(HordeText.Passive(c, id));
            if (weapons.Count > 0) sb.Append('\n').Append("<color=#").Append(ListHex).Append('>').Append(Lang.F("New weapons: {0}", Names(weapons))).Append("</color>");
            if (passives.Count > 0) sb.Append('\n').Append("<color=#").Append(ListHex).Append('>').Append(Lang.F("New passives: {0}", Names(passives))).Append("</color>");
            return sb.ToString();
        }

        private static string Names(List<string> n) => n.Count <= MaxNames ? string.Join(", ", n) : string.Join(", ", n.GetRange(0, MaxNames)) + " " + Lang.F("+{0} more", n.Count - MaxNames);

        /// <summary>Once a frame (HordeChoice.Update): <paramref name="card"/> is the job card under a laser (-1: none), <paramref name="cardRt"/>
        /// its button, <paramref name="panel"/> the choice's panel.</summary>
        public void Tick(HordeContent c, int card, int jobId, RectTransform cardRt, RectTransform panel)
        {
            float now = Time.unscaledTime;
            if (card < 0 || cardRt == null || panel == null)
            {
                if (_shown && now - _lastSeen > Grace) Hide();
                if (!_shown) _on = -1;
                return;
            }
            _lastSeen = now;
            if (card != _on || jobId != _job || !_shown)
            {
                string text = Text(c, jobId);
                if (string.IsNullOrEmpty(text)) { Hide(); return; }
                _on = card; _job = jobId;
                if (!_shown) { _rt.gameObject.SetActive(true); _shown = true; Shows++; Debug.Log($"[Horde] job help: job {jobId} ({HordeText.Job(jobId)}) under the laser"); }
                if (text != _shownText) Fit(text);
            }
            PlaceOver(cardRt, panel);
        }

        public void Hide() { _shown = false; _on = -1; _job = 0; if (_rt != null && _rt.gameObject.activeSelf) _rt.gameObject.SetActive(false); }

        private void Fit(string text)
        {
            _shownText = text; _text.text = text;
            float tw = WidthPx - 2f * PadPx;
            var gs = _text.GetGenerationSettings(new Vector2(tw, 10000f));
            gs.scaleFactor = 1f;
            float h = Mathf.Ceil(_text.cachedTextGeneratorForLayout.GetPreferredHeight(text, gs)) + 2f * PadPx;
            h = Mathf.Max(h, 90f);
            _rt.sizeDelta = new Vector2(WidthPx, h); _rim.sizeDelta = new Vector2(WidthPx, h); _bg.sizeDelta = new Vector2(WidthPx - 6f, h - 6f);
            ((RectTransform)_text.transform).sizeDelta = new Vector2(tw, h - 2f * PadPx);
        }

        /// <summary>Over the choice's panel, centred on the card's x (within the panel's width), in its plane, a little towards the eyes.</summary>
        private void PlaceOver(RectTransform card, RectTransform panel)
        {
            var size = panel.sizeDelta; float h = _rt.sizeDelta.y;
            float cx = panel.InverseTransformPoint(card.position).x;
            float x = Mathf.Clamp(cx, -size.x / 2 + WidthPx / 2, size.x / 2 - WidthPx / 2);
            // (client wave 21: clear of the choice's wood frame and of the gold plate that stands PlateOverPx over it)
            float frame = UiKit.OutsetOf(panel);
            var pos = panel.TransformPoint(new Vector3(x, size.y / 2 + frame + (frame > 0f ? PlateOverPx : 0f) + GapPx + h / 2, 0f));
            if (_head != null) { var toEyes = _head.transform.position - pos; if (toEyes.sqrMagnitude > 1e-6f) pos += toEyes.normalized * TowardEyesM; }
            transform.SetPositionAndRotation(pos, panel.rotation);
            transform.localScale = panel.lossyScale;
        }
    }
}
