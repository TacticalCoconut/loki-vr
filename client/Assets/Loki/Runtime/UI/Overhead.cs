using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Name plate, HP bar, quest marker and floating damage numbers above a unit or ground item. A tiny world-space canvas
    /// that billboards to the head and keeps a constant real-world size however far the diorama is scaled.
    /// </summary>
    public sealed class Overhead : MonoBehaviour
    {
        public float MetresPerPx = 0.00035f;
        public float HeightCells = 2.6f;        // above the unit's feet, in map cells
        public bool AlwaysShowName = true;
        public bool Hovered;
        /// <summary>Distance from the head beyond which tags grow to keep their apparent size.</summary>
        public const float FarMetres = 1.2f;
        /// <summary>Size factor used last frame (Comfort.NameScale × distance growth) — smoke test.</summary>
        public float SizeFactor { get; private set; } = 1f;
        /// <summary>The name's font size in canvas pixels (Build).</summary>
        public const int NamePx = 26;
        /// <summary>
        /// Client wave 14 (w14play), point 21 of the design's headset list ("when in first person, the names of NPCs are too small"): the
        /// least angle a name's text (<see cref="NamePx"/>, the measure wave 11's boards use: font pixels x scale) fills seen from the
        /// eyes, times Comfort.NameScale; 0 = none. VR/ViewController.cs sets it: <see cref="FirstPersonNameDegrees"/> in first person,
        /// 0 in the table and follow views. Before this a tag grew with distance only up to 4.8 m (0.43° from 1.2 m on), so a name
        /// 10 m away in first person was 0.21°.
        /// </summary>
        public static float MinNameDegrees { get; set; }
        /// <summary>Wave 11's smallest board text (CLIENT_WAVE11.md: "text at least ~1.3 degrees tall").</summary>
        public const float FirstPersonNameDegrees = 1.3f;
        public Vector3 TagPosition => _root != null ? _root.position : transform.position;

        private RectTransform _root;
        private Text _name, _marker;
        private RectTransform _hpBg, _hpFill, _hpShield, _spBg, _spFill, _castBg, _castFill; private Text _castText; private float _castStart, _castEnd;
        private float _hpFrac = -1f, _spFrac = -1f, _shieldFrac;
        public float ShieldFraction => Mathf.Max(0f, _shieldFrac);
        public bool ShieldVisible => _hpShield != null && _hpShield.gameObject.activeSelf;
        private readonly List<(Text t, float born)> _pops = new List<(Text, float)>();
        private RectTransform _speech; private Text _speechText; private float _speechUntil;
        private static Camera Head => Camera.main;

        public static Overhead Attach(Transform unit, string name, Color nameColor, float heightCells)
        {
            var o = unit.gameObject.AddComponent<Overhead>();
            o.HeightCells = heightCells;
            o.Build(name, nameColor);
            return o;
        }

        private void Build(string name, Color color)
        {
            var go = new GameObject("overhead", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 5; // below the panels (10): both draw over the world
            _root = (RectTransform)go.transform; _root.sizeDelta = new Vector2(400, 200);
            _name = UiKit.Label(_root, name, new Vector2(0, 10), new Vector2(400, 40), NamePx, TextAnchor.MiddleCenter, color);
            var sh = _name.gameObject.AddComponent<Outline>(); sh.effectColor = new Color(0, 0, 0, 0.9f); sh.effectDistance = new Vector2(2, -2);
            _nameOutline = sh; // client wave 6 (w6voice): SetVoiceGlow
            _hpBg = UiKit.Box(_root, "hpbg", new Vector2(0, -18), new Vector2(120, 12), new Color(0, 0, 0, 0.85f));
            _hpFill = UiKit.Box(_hpBg, "hp", Vector2.zero, new Vector2(116, 8), new Color(0.2f, 0.9f, 0.3f, 1f));
            _hpFill.anchorMin = _hpFill.anchorMax = new Vector2(0, 0.5f); _hpFill.pivot = new Vector2(0, 0.5f); _hpFill.anchoredPosition = new Vector2(2, 0);
            _hpShield = UiKit.Box(_hpBg, "shield", Vector2.zero, new Vector2(0, 8), new Color(0.45f, 0.90f, 1f, 0.95f));
            _hpShield.anchorMin = _hpShield.anchorMax = new Vector2(0, 0.5f); _hpShield.pivot = new Vector2(0, 0.5f); _hpShield.anchoredPosition = new Vector2(2, 0);
            _hpShield.gameObject.SetActive(false);
            _hpBg.gameObject.SetActive(false);
            _spBg = UiKit.Box(_root, "spbg", new Vector2(0, -32), new Vector2(120, 10), new Color(0, 0, 0, 0.85f));
            _spFill = UiKit.Box(_spBg, "sp", Vector2.zero, new Vector2(116, 6), new Color(0.22f, 0.52f, 0.95f, 1f));
            _spFill.anchorMin = _spFill.anchorMax = new Vector2(0, 0.5f); _spFill.pivot = new Vector2(0, 0.5f); _spFill.anchoredPosition = new Vector2(2, 0);
            _spBg.gameObject.SetActive(false);
            _castBg = UiKit.Box(_root, "castbg", new Vector2(0, -50), new Vector2(160, 14), new Color(0, 0, 0, 0.85f));
            _castFill = UiKit.Box(_castBg, "cast", Vector2.zero, new Vector2(156, 10), new Color(0.35f, 0.65f, 1f, 1f));
            _castFill.anchorMin = _castFill.anchorMax = new Vector2(0, 0.5f); _castFill.pivot = new Vector2(0, 0.5f); _castFill.anchoredPosition = new Vector2(2, 0);
            _castText = UiKit.Label(_castBg, "", new Vector2(0, -20), new Vector2(300, 28), 20, TextAnchor.MiddleCenter, new Color(0.7f, 0.85f, 1f));
            _castBg.gameObject.SetActive(false);
            _marker = UiKit.Label(_root, "", new Vector2(0, 62), new Vector2(120, 70), 64, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.2f));
            var mo = _marker.gameObject.AddComponent<Outline>(); mo.effectColor = Color.black; mo.effectDistance = new Vector2(3, -3);
            _speech = UiKit.Box(_root, "speech", new Vector2(0, 110), new Vector2(380, 60), new Color(0.05f, 0.06f, 0.1f, 0.85f));
            _speechText = UiKit.Label(_speech, "", Vector2.zero, new Vector2(364, 56), 22, TextAnchor.MiddleCenter, Color.white);
            _speech.gameObject.SetActive(false);
        }

        /// <summary>Chat bubble over the head, as the client draws for ZC_NOTIFY_CHAT: wrapped text on a dark box, gone after a while.</summary>
        public void SetSpeech(string text, float seconds = 5f)
        {
            if (_speech == null) return;
            if (string.IsNullOrEmpty(text)) { _speech.gameObject.SetActive(false); return; }
            if (text.Length > 160) text = text.Substring(0, 160) + "…";
            int lines = Mathf.Clamp(Mathf.CeilToInt(text.Length / 32f), 1, 5);
            float h = 20 + 26 * lines;
            _speech.sizeDelta = new Vector2(380, h); ((RectTransform)_speechText.transform).sizeDelta = new Vector2(364, h - 4);
            _speech.anchoredPosition = new Vector2(0, 80 + h / 2);
            _speechText.text = UiKit.RoColors(text);
            _speech.gameObject.SetActive(true);
            _speechUntil = Time.time + seconds;
        }

        public bool SpeechVisible => _speech != null && _speech.gameObject.activeSelf;
        /// <summary>A vending / buying-store board above the name (ZC_STORE_ENTRY / ZC_BUYING_STORE_ENTRY); null or empty removes it.</summary>
        public void SetBoard(string text)
        {
            if (_board == null)
            {
                if (string.IsNullOrEmpty(text)) return;
                _board = UiKit.Label(_root, "", new Vector2(0, BoardY), new Vector2(420, 40), 22, TextAnchor.MiddleCenter, new Color(1f, 0.88f, 0.45f));
                var sh = _board.gameObject.AddComponent<Outline>(); sh.effectColor = new Color(0, 0, 0, 0.9f); sh.effectDistance = new Vector2(2, -2);
            }
            _board.text = text ?? ""; _board.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }
        public string Board => _board != null && _board.gameObject.activeSelf ? _board.text : "";

        /// <summary>A chat room sign above the name (ZC_ROOM_NEWENTRY, client wave 2): a button the laser can press to join; null / empty
        /// text removes it; a null action draws it greyed (NPC rooms without a limit are not clickable).</summary>
        public void SetChatSign(string text, System.Action onClick, bool isPrivate)
        {
            if (_root == null) return;
            bool show = !string.IsNullOrEmpty(text);
            if (_sign == null)
            {
                if (!show) return;
                _sign = UiKit.Button(_root, "", new Vector2(0, BoardY + 6), new Vector2(440, 44), null, 21);
            }
            _sign.gameObject.SetActive(show);
            if (!show) return;
            _sign.SetLabel(text); _sign.SetAction(onClick); _sign.Interactable = onClick != null;
            _sign.Normal = onClick == null ? new Color(0.25f, 0.25f, 0.28f, 0.9f) : isPrivate ? new Color(0.35f, 0.22f, 0.12f, 0.95f) : new Color(0.12f, 0.3f, 0.22f, 0.95f);
            _sign.Text.color = new Color(1f, 0.9f, 0.55f); _sign.SetHover(false);
            ((RectTransform)_sign.transform).anchoredPosition = new Vector2(0, BoardY + 6);
        }
        public string ChatSign => _sign != null && _sign.gameObject.activeSelf ? _sign.Text.text : "";
        private UiButton _sign;

        /// <summary>The guild line above the name: "[emblem] Guild [Title]" (empty text and no emblem removes it). Pushes a shop board up.</summary>
        public void SetGuildLine(string text, Sprite emblem, Color color)
        {
            if (_root == null) return;
            bool show = !string.IsNullOrEmpty(text) || emblem != null;
            if (_guild == null)
            {
                if (!show) return;
                _guild = UiKit.Label(_root, "", new Vector2(0, 38), new Vector2(400, 30), 20, TextAnchor.MiddleCenter, color);
                var sh = _guild.gameObject.AddComponent<Outline>(); sh.effectColor = new Color(0, 0, 0, 0.9f); sh.effectDistance = new Vector2(2, -2);
                _emblem = UiKit.Icon(_root, new Vector2(0, 38), new Vector2(26, 26));
            }
            _guild.text = text ?? ""; _guild.color = color; _guild.gameObject.SetActive(show);
            _emblem.sprite = emblem; _emblemOn = show && emblem != null; _emblem.enabled = _emblemOn;
            float w = string.IsNullOrEmpty(text) ? 0f : Mathf.Min(_guild.preferredWidth, 380f);
            ((RectTransform)_emblem.transform).anchoredPosition = new Vector2(w > 0 ? -w / 2 - 17 : 0, 38);
            Relayout();
        }
        /// <summary>The achievement title (0x0a30 title_id) as a small gold line above the name — and above the guild line when there is one.</summary>
        public void SetTitleLine(string title)
        {
            if (_root == null) return;
            bool show = !string.IsNullOrEmpty(title);
            if (_titleLine == null)
            {
                if (!show) return;
                _titleLine = UiKit.Label(_root, "", new Vector2(0, 38), new Vector2(400, 28), 19, TextAnchor.MiddleCenter, new Color(1f, 0.82f, 0.4f));
                var sh = _titleLine.gameObject.AddComponent<Outline>(); sh.effectColor = new Color(0, 0, 0, 0.9f); sh.effectDistance = new Vector2(2, -2);
            }
            _titleLine.text = show ? "«" + title + "»" : ""; _titleLine.gameObject.SetActive(show);
            Relayout();
        }
        public string TitleLine => _titleLine != null && _titleLine.gameObject.activeSelf ? _titleLine.text : "";
        private Text _titleLine;
        private bool GuildShown => (_guild != null && _guild.gameObject.activeSelf) || _emblemOn;
        private bool TitleShown => _titleLine != null && _titleLine.gameObject.activeSelf;
        private float BoardY => (GuildShown ? 66 : 52) + (TitleShown ? 24 : 0);
        private void Relayout()
        {
            if (_titleLine != null) ((RectTransform)_titleLine.transform).anchoredPosition = new Vector2(0, GuildShown ? 62 : 38);
            if (_board != null) ((RectTransform)_board.transform).anchoredPosition = new Vector2(0, BoardY);
        }
        public string GuildLine => _guild != null && _guild.gameObject.activeSelf ? _guild.text : "";
        public bool HasEmblem => _emblem != null && _emblemOn;
        private Text _guild; private Image _emblem; private bool _emblemOn;
        private Text _board;
        public bool HpVisible => _hpBg != null && _hpBg.gameObject.activeSelf;
        public bool SpVisible => _spBg != null && _spBg.gameObject.activeSelf;
        public Color NameColor => _name != null ? _name.color : Color.clear;
        /// <summary>Client wave 6 (w6voice): the name glows while this player's voice is heard — its outline turns from black to a
        /// warm yellow (the name's own colour stays whatever the party / guild / PvP code made it).</summary>
        public void SetVoiceGlow(bool on)
        {
            if (_nameOutline == null || VoiceGlow == on) return;
            VoiceGlow = on;
            _nameOutline.effectColor = on ? new Color(1f, 0.8f, 0.15f, 1f) : new Color(0, 0, 0, 0.9f);
            _nameOutline.effectDistance = on ? new Vector2(3, -3) : new Vector2(2, -2);
        }
        public bool VoiceGlow { get; private set; }
        private Outline _nameOutline;
        public void SetName(string s) { if (_name != null) _name.text = s; }
        public void SetNameColor(Color c) { if (_name != null) _name.color = c; }

        /// <summary>HP fraction 0..1; negative hides the bar.</summary>
        public void SetHp(float frac, bool self = false)
        {
            _hpFrac = frac;
            if (_hpBg == null) return;
            _hpBg.gameObject.SetActive(frac >= 0f);
            if (frac >= 0f)
            {
                _hpFill.sizeDelta = new Vector2(116f * Mathf.Clamp01(frac), 8);
                _hpFill.GetComponent<Image>().color = self ? new Color(0.2f, 0.9f, 0.3f) : frac > 0.5f ? new Color(0.95f, 0.75f, 0.15f) : new Color(0.95f, 0.25f, 0.2f);
            }
        }

        /// <summary>Item 13: sets the percentage-HP shield bar indicator (0..1 of MaxHP) on top of the HP bar.</summary>
        public void SetShield(float frac)
        {
            _shieldFrac = Mathf.Clamp01(frac);
            if (_hpShield == null) return;
            bool show = _shieldFrac > 0.001f;
            if (_hpShield.gameObject.activeSelf != show) _hpShield.gameObject.SetActive(show);
            if (show) _hpShield.sizeDelta = new Vector2(116f * _shieldFrac, 8);
        }

        /// <summary>SP fraction 0..1; negative hides the bar.</summary>
        public void SetSp(float frac, bool self = false)
        {
            _spFrac = frac;
            if (_spBg == null) return;
            _spBg.gameObject.SetActive(frac >= 0f);
            if (frac >= 0f)
            {
                _spFill.sizeDelta = new Vector2(116f * Mathf.Clamp01(frac), 6);
                _spFill.GetComponent<Image>().color = new Color(0.22f, 0.52f, 0.95f, 1f);
            }
        }

        /// <summary>Quest marker from ZC_QUEST_NOTIFY_EFFECT: effect (0..10, 9999 = none) and colour.</summary>
        public void SetQuestMarker(int effect, int color)
        {
            if (_marker == null) return;
            if (effect == 9999 || effect < 0) { _marker.text = ""; return; }
            _marker.text = (effect == 1 || effect == 3 || effect == 5) ? "?" : "!"; // rAthena doc/script_commands.txt: QUEST2/JOB2/EVENT2 (1,3,5) are the "?" icons, the rest "!"
            // e_questinfo_markcolor (script.hpp): 0 none, 1 yellow, 2 green, 3 purple
            _marker.color = color == 2 ? new Color(0.3f, 1f, 0.4f) : color == 3 ? new Color(0.8f, 0.4f, 1f) : new Color(1f, 0.88f, 0.2f);
        }

        /// <summary>Cast bar from ZC_USESKILL_ACK: fills over the cast time; 0 hides it.</summary>
        public void SetCast(string skill, float seconds) => SetCast(skill, seconds, new Color(0.35f, 0.65f, 1f, 1f));
        /// <summary>The same bar in another colour — NPC progress bars (ZC_PROGRESS / ZC_PROGRESS_ACTOR) draw green like the official client.</summary>
        public void SetCast(string skill, float seconds, Color fill)
        {
            if (_castBg == null) return;
            if (seconds <= 0.05f) { _castBg.gameObject.SetActive(false); return; }
            _castStart = Time.time; _castEnd = Time.time + seconds; _castText.text = skill;
            _castFill.GetComponent<Image>().color = fill;
            _castBg.gameObject.SetActive(true);
        }
        /// <summary>Removes the cast / progress bar at once (ZC_PROGRESS_CANCEL).</summary>
        public void ClearCast() { if (_castBg != null) _castBg.gameObject.SetActive(false); }
        /// <summary>Whether a cast / progress bar is shown, and how full it is (0..1) — smoke tests.</summary>
        public bool CastShown => _castBg != null && _castBg.gameObject.activeSelf;
        public float CastFraction => Mathf.InverseLerp(_castStart, _castEnd, Time.time);

        public void PopNumber(string text, Color color)
        {
            if (_root == null) return;
            var t = UiKit.Label(_root, text, new Vector2(Random.Range(-40f, 40f), 30), new Vector2(300, 60), 40, TextAnchor.MiddleCenter, color);
            var o = t.gameObject.AddComponent<Outline>(); o.effectColor = Color.black; o.effectDistance = new Vector2(2, -2);
            _pops.Add((t, Time.time));
        }

        private void LateUpdate()
        {
            if (_root == null) return;
            var parentScale = transform.lossyScale.x;
            if (parentScale < 1e-6f) return;
            // Place above the unit (in its local cells) and keep a fixed real-world pixel size.
            _root.position = transform.position + Vector3.up * (HeightCells * parentScale);
            // Comfort name-tag size; beyond FarMetres the tag also grows with distance (up to 4×) so it keeps its apparent size —
            // in Follow / First person units stand metres away, where a fixed real-world size would be unreadable.
            float k = VR.Comfort.NameScale;
            var head = Head;
            if (head != null)
            {
                var d = _root.position - head.transform.position; float dist = d.magnitude;
                if (dist > 1e-3f) _root.rotation = Quaternion.LookRotation(d / dist, Vector3.up);
                k *= Mathf.Clamp(dist / FarMetres, 1f, 4f);
                // client wave 14 (w14play), point 21: never under MinNameDegrees (first person)
                if (MinNameDegrees > 0f) k = Mathf.Max(k, VR.Comfort.NameScale * Mathf.Tan(MinNameDegrees * Mathf.Deg2Rad) * dist / (NamePx * MetresPerPx));
            }
            SizeFactor = k;
            _root.localScale = Vector3.one * (MetresPerPx * k / parentScale);
            _name.enabled = AlwaysShowName || Hovered;
            if (_guild != null) { _guild.enabled = _name.enabled; _emblem.enabled = _emblemOn && _name.enabled; }
            if (_titleLine != null) _titleLine.enabled = _name.enabled;
            if (_castBg != null && _castBg.gameObject.activeSelf)
            {
                float t = Mathf.InverseLerp(_castStart, _castEnd, Time.time);
                _castFill.sizeDelta = new Vector2(156f * t, 10);
                if (Time.time >= _castEnd + 0.15f) _castBg.gameObject.SetActive(false);
            }
            if (_speech != null && _speech.gameObject.activeSelf && Time.time > _speechUntil) _speech.gameObject.SetActive(false);
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
