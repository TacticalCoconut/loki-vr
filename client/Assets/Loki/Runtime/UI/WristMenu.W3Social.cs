using System.Collections.Generic;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 3 (w3social) parts of the wrist menu:
    ///  • Party: every member (a party holds up to MAX_PARTY = 12, rA mmo.hpp; the page showed 6) in a 2 × 6 grid of cells —
    ///    name, level, job, map or state, HP bar — and an action row for the member the laser picked: Whisper, Make leader,
    ///    Expel (the leader's), Leave (yourself).
    ///  • Emotes: the whole palette, 87 emotes on 5 pages of 20, each cell the emote's own picture from emotion.act with its chat
    ///    command (Game/Emotes.cs).
    ///  • View: a "World map" button beside the minimap toggle.
    /// </summary>
    public sealed partial class WristMenu
    {
        // ------------------------------------------------------------------ party: 12 members
        private const int W3SocialPartyMax = 12; // MAX_PARTY (rA src/common/mmo.hpp)
        private readonly List<(UiButton cell, Text name, Text info, RectTransform hpBg, RectTransform hpFill)> _w3socialCells = new List<(UiButton, Text, Text, RectTransform, RectTransform)>();
        private Text _w3socialSel; private UiButton _w3socialWhisper, _w3socialLead, _w3socialExpel;
        private uint _w3socialSelAid;
        private const float W3SocialCellW = (W - 60) / 2f, W3SocialCellH = 68, W3SocialPitch = 72; // 2 × 6 cells: names 21 px, details 18 px

        /// <summary>The in-party part of the Party page (called from BuildParty with its "inparty" box).</summary>
        private void BuildW3SocialParty(RectTransform p)
        {
            float top = PH / 2;
            for (int i = 0; i < W3SocialPartyMax; i++)
            {
                int slot = i, row = i / 2, col = i % 2;
                float x = col == 0 ? -(W3SocialCellW / 2 + 5) : W3SocialCellW / 2 + 5, y = top - 178 - row * W3SocialPitch;
                var cell = UiKit.Button(p, "", new Vector2(x, y), new Vector2(W3SocialCellW, W3SocialCellH), () => W3SocialSelectCell(slot), 18);
                cell.Normal = new Color(1, 1, 1, 0.05f); cell.Hover = new Color(0.3f, 0.35f, 0.5f, 0.6f); cell.SetHover(false);
                var crt = (RectTransform)cell.transform;
                var name = UiKit.Label(crt, "", new Vector2(0, 15), new Vector2(W3SocialCellW - 16, 30), 21, TextAnchor.MiddleLeft);
                var info = UiKit.Label(crt, "", new Vector2(0, -10), new Vector2(W3SocialCellW - 16, 24), 18, TextAnchor.MiddleLeft, UiKit.Dim);
                var hpBg = UiKit.Box(crt, "hpbg", new Vector2(0, -27), new Vector2(W3SocialCellW - 20, 6), new Color(0, 0, 0, 0.6f));
                var hpFill = UiKit.Box(hpBg, "hp", Vector2.zero, new Vector2(W3SocialCellW - 24, 4), new Color(0.25f, 0.85f, 0.3f));
                hpFill.anchorMin = hpFill.anchorMax = new Vector2(0, 0.5f); hpFill.pivot = new Vector2(0, 0.5f); hpFill.anchoredPosition = new Vector2(2, 0);
                _w3socialCells.Add((cell, name, info, hpBg, hpFill));
            }
            // the picked member's actions
            float ay = top - 605;
            _w3socialSel = UiKit.Label(p, "", new Vector2(-W / 2 + 25 + 105, ay), new Vector2(210, 48), 19, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.5f));
            _w3socialWhisper = UiKit.Button(p, Lang.T("Whisper"), new Vector2(-15, ay), new Vector2(130, 50), () => { var m = W3SocialSelected(); if (m != null) PrefillChat?.Invoke("/w " + m.Name + " : "); }, 20);
            _w3socialLead = UiKit.Button(p, Lang.T("Leader"), new Vector2(115, ay), new Vector2(120, 50), () => Party?.MakeLeader(W3SocialSelected()), 20);
            _w3socialExpel = UiKit.Button(p, Lang.T("Expel"), new Vector2(W / 2 - 80, ay), new Vector2(110, 50), () => Party?.Expel(W3SocialSelected()), 20);
            _pLeave = UiKit.Button(p, Lang.T("Leave the party"), new Vector2(W / 2 - 150, ay), new Vector2(250, 50), () => Party?.Leave(), 20);
            // inviting is the leader's job (party.cpp party_invite refuses everyone else with msg 282) — the rows only show for the leader
            _pInviteRow = UiKit.Box(p, "inviterow", new Vector2(0, top - 667), new Vector2(W - 30, 54), new Color(0, 0, 0, 0));
            var inviteName = VrTextField.Create(_pInviteRow, new Vector2(-190, 0), new Vector2(230, 50), 20);
            inviteName.Placeholder = Lang.T("invite by name"); inviteName.OnFocus = f => RequestKeyboard?.Invoke(f); inviteName.OnSubmit = () => { Party?.InviteByName(inviteName.Text); inviteName.SetText(""); };
            UiKit.Button(_pInviteRow, Lang.T("Invite"), new Vector2(-15, 0), new Vector2(110, 50), () => { Party?.InviteByName(inviteName.Text); inviteName.SetText(""); }, 20);
            _pInviteGazed = UiKit.Button(_pInviteRow, Lang.T("Invite the one I look at"), new Vector2(175, 0), new Vector2(260, 50), () => { var n = GazedPlayerName?.Invoke(); if (n != null) Party?.InviteByName(n); else Toast(Lang.T("Look at a player first (gaze lock).")); }, 18);
        }

        private void W3SocialSelectCell(int slot) { var l = SortedMembers(); if (slot < l.Count) { _w3socialSelAid = l[slot].Aid; _dirty = true; } }
        private Party.Member W3SocialSelected() { if (Party == null) return null; return Party.Members.TryGetValue(_w3socialSelAid, out var m) ? m : null; }

        private void RefreshW3SocialParty(bool leader)
        {
            var members = SortedMembers();
            string me = _charName?.Invoke();
            if (members.Find(m => m.Aid == _w3socialSelAid) == null) _w3socialSelAid = members.Count > 0 ? (members.Find(m => m.Name == me) ?? members[0]).Aid : 0;
            for (int i = 0; i < _w3socialCells.Count; i++)
            {
                var (cell, name, info, hpBg, hpFill) = _w3socialCells[i];
                bool show = i < members.Count; cell.gameObject.SetActive(show);
                if (!show) continue;
                var m = members[i]; bool isMe = m.Name == me; bool sel = m.Aid == _w3socialSelAid;
                string state = !m.Online ? Lang.T("<color=#6a7080>offline</color>") : m.Dead ? Lang.T("<color=#ff8a7a>dead</color>") : ShortMapName(m.Map);
                name.text = $"{(m.Leader ? "<color=#ffd060>★</color> " : "")}{m.Name}{(isMe ? " " + Lang.T("<color=#9aa3b5>(you)</color>") : "")}";
                info.text = Lang.F("Lv {0} {1} · {2}", m.Level, LoginPanel.JobName(m.Job), state);
                long mhp = isMe ? _ps.Hp : m.Hp, mmax = isMe ? _ps.MaxHp : m.MaxHp;
                bool hp = m.Online && mmax > 0 && mhp >= 0;
                hpBg.gameObject.SetActive(hp);
                if (hp) { float f = Mathf.Clamp01(mhp / (float)mmax); hpFill.sizeDelta = new Vector2((W3SocialCellW - 24) * f, 4); hpFill.GetComponent<Image>().color = f > 0.5f ? new Color(0.25f, 0.85f, 0.3f) : f > 0.25f ? new Color(0.95f, 0.75f, 0.15f) : new Color(0.95f, 0.25f, 0.2f); }
                cell.Normal = sel ? new Color(0.45f, 0.35f, 0.15f, 0.55f) : new Color(1, 1, 1, 0.05f); cell.SetHover(false);
            }
            var s = W3SocialSelected(); bool self = s != null && s.Name == me;
            _w3socialSel.text = s == null ? "" : self ? Lang.T("You") : s.Name;
            _w3socialWhisper.gameObject.SetActive(s != null && !self && s.Online);
            _w3socialLead.gameObject.SetActive(s != null && !self && leader && s.Online);
            _w3socialExpel.gameObject.SetActive(s != null && !self && leader);
            _pLeave.gameObject.SetActive(self);
        }

        // ------------------------------------------------------------------ emotes: the whole palette
        private const int W3SocialEmoteCols = 4, W3SocialEmoteRows = 5, W3SocialEmotesPerPage = W3SocialEmoteCols * W3SocialEmoteRows;
        private readonly List<(UiButton btn, Image icon, Text label)> _w3socialEmoteCells = new List<(UiButton, Image, Text)>();
        private int _w3socialEmotePage; private Text _w3socialEmotePageText, _w3socialEmoteWait;
        /// <summary>The picture of an emote (Game.Emotes.IconOf), set by the game.</summary>
        public System.Func<int, Texture2D> EmoteIcon; public System.Func<float> EmoteWait;
        private readonly Dictionary<int, Sprite> _w3socialEmoteSprites = new Dictionary<int, Sprite>();
        public int EmotePages => Mathf.Max(1, (Emotes.All.Count + W3SocialEmotesPerPage - 1) / W3SocialEmotesPerPage);

        private void BuildW3SocialEmotes(RectTransform p)
        {
            float top = PH / 2, cw = (W - 60) / W3SocialEmoteCols, ch = 100;
            UiKit.Label(p, Lang.T("Emotes"), new Vector2(-W / 2 + 130, top - 30), new Vector2(220, 40), 28, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            _w3socialEmoteWait = UiKit.Label(p, "", new Vector2(110, top - 30), new Vector2(W - 290, 40), 19, TextAnchor.MiddleRight, UiKit.Dim);
            for (int i = 0; i < W3SocialEmotesPerPage; i++)
            {
                int slot = i, col = i % W3SocialEmoteCols, row = i / W3SocialEmoteCols;
                var b = UiKit.Button(p, "", new Vector2(-W / 2 + 30 + cw * (col + 0.5f), top - 112 - row * 108), new Vector2(cw - 8, ch), () => W3SocialEmoteClick(slot), 18);
                var brt = (RectTransform)b.transform;
                var icon = UiKit.Icon(brt, new Vector2(0, 12), new Vector2(64, 64));
                var label = UiKit.Label(brt, "", new Vector2(0, -34), new Vector2(cw - 14, 28), 18, TextAnchor.MiddleCenter);
                _w3socialEmoteCells.Add((b, icon, label));
            }
            UiKit.Button(p, "<", new Vector2(-W / 2 + 70, -top + 72), new Vector2(100, 56), () => { _w3socialEmotePage = Mathf.Max(0, _w3socialEmotePage - 1); _dirty = true; }, 28);
            UiKit.Button(p, ">", new Vector2(W / 2 - 70, -top + 72), new Vector2(100, 56), () => { _w3socialEmotePage = Mathf.Min(EmotePages - 1, _w3socialEmotePage + 1); _dirty = true; }, 28);
            _w3socialEmotePageText = UiKit.Label(p, "", new Vector2(0, -top + 72), new Vector2(300, 50), 22, TextAnchor.MiddleCenter, UiKit.Dim);
            var emoteHint = UiKit.Label(p, Lang.T("One every 2 seconds · needs Basic Skill level 2 · or type the command in chat"), new Vector2(0, -top + 22), new Vector2(W - 50, 30), 17, TextAnchor.MiddleCenter, UiKit.Dim);
            emoteHint.resizeTextForBestFit = true; emoteHint.resizeTextMinSize = 13; emoteHint.resizeTextMaxSize = emoteHint.fontSize; // w4lang
        }

        private void W3SocialEmoteClick(int slot)
        {
            int i = _w3socialEmotePage * W3SocialEmotesPerPage + slot;
            var all = Emotes.All;
            if (i < all.Count) { SendEmote?.Invoke(all[i].Id); _dirty = true; }
        }

        private bool _w3socialWaiting; private float _w3socialTick;
        /// <summary>While the next emote has to wait, the page redraws 5 times a second (and once more when it may be sent).</summary>
        private void W3SocialTick()
        {
            bool waiting = EmoteWait != null && EmoteWait() > 0f;
            if (!waiting && !_w3socialWaiting) return;
            _w3socialTick += Time.deltaTime;
            if (_w3socialTick > 0.2f || !waiting) { _w3socialTick = 0; _dirty = true; }
            _w3socialWaiting = waiting;
        }

        private void RefreshW3SocialEmotes()
        {
            var all = Emotes.All; int pages = EmotePages;
            _w3socialEmotePage = Mathf.Clamp(_w3socialEmotePage, 0, pages - 1);
            _w3socialEmotePageText.text = Lang.F("{0} / {1}  ·  {2} emotes", _w3socialEmotePage + 1, pages, all.Count);
            float wait = EmoteWait != null ? EmoteWait() : 0f;
            _w3socialEmoteWait.text = wait > 0f ? Lang.F("<color=#ffd060>next in {0:0.0} s</color>", wait) : Lang.T("~emote~ready");
            for (int s = 0; s < _w3socialEmoteCells.Count; s++)
            {
                var (btn, icon, label) = _w3socialEmoteCells[s];
                int i = _w3socialEmotePage * W3SocialEmotesPerPage + s;
                bool show = i < all.Count; btn.gameObject.SetActive(show);
                if (!show) continue;
                var e = all[i];
                label.text = e.Label;
                if (!_w3socialEmoteSprites.TryGetValue(e.Id, out var sp)) { var tex = EmoteIcon?.Invoke(e.Id); sp = tex != null ? UiKit.ToSprite(tex) : null; _w3socialEmoteSprites[e.Id] = sp; }
                icon.sprite = sp; icon.enabled = sp != null;
                btn.Normal = wait > 0f ? new Color(0.24f, 0.25f, 0.3f) : UiKit.ButtonBg; btn.SetHover(false);
            }
        }

        // ------------------------------------------------------------------ view: the world map
        /// <summary>The View tab's "World map" button (the game opens UI/WorldMapWindow).</summary>
        public System.Action OpenWorldMap;
        private void BuildW3SocialView(RectTransform p, float top)
        {
            float half = (W - 90) / 2f - 5;
            _mapButton.Resize(new Vector2(half, 62)); ((RectTransform)_mapButton.transform).anchoredPosition = new Vector2(-(half / 2 + 5), top - 330);
            UiKit.Button(p, Lang.T("World map"), new Vector2(half / 2 + 5, top - 330), new Vector2(half, 62), () => OpenWorldMap?.Invoke(), 26);
        }
    }
}
