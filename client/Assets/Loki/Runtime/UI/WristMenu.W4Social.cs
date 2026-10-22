using System.Collections.Generic;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 4 (w4social): the wrist menu's Social tab — the friend list complete. rAthena e985006 keeps up to MAX_FRIENDS 40
    /// (mmo.hpp:168) and sends the list with ZC_FRIENDS_LIST 0x0201, then ZC_FRIENDS_STATE 0x0206 for every friend online and on every
    /// login / logout (clif.cpp:15303-15391); requests, answers and removals are Game/SocialHub.cs. Per friend: online state, Whisper
    /// (the console with "/w Name : "), Party (an invitation by name, CZ_PARTY_JOIN_REQ 0x02c4, for a party leader), Remove (two presses:
    /// CZ_DELETE_FRIENDS 0x0203 removes the pair on both lists at once, clif.cpp:15556-15612). Under the list: "Say hi" (the official /hi,
    /// a whisper to every friend online), the /loginout switch, adding by name or by gaze, and a trade with the one looked at.
    /// </summary>
    public sealed partial class WristMenu
    {
        private const int W4FriendsPerPage = 6;
        private readonly List<(RectTransform row, Text label, UiButton whisper, UiButton party, UiButton remove)> _w4FriendRows = new List<(RectTransform, Text, UiButton, UiButton, UiButton)>();
        private UiButton _w4LoginOut; private uint _w4RemoveArmed; private float _w4RemoveUntil;
        /// <summary>The party invitation by name (set by the game).</summary>
        public System.Action<string> W4SocialPartyInvite;
        /// <summary>Whether we may invite into a party now (a leader).</summary>
        public System.Func<bool> W4SocialCanInvite;
        /// <summary>Client wave 20 (w20pen), the design's point 57 ("On the social menu, allow the player to spawn a simple pen object"): the page's
        /// "Take pen" / "Put pen away" button (top right, beside the title): the game's toggle, and whether a hand holds the pen (LokiGame.W20Pen.cs).</summary>
        public System.Action TogglePen;
        public System.Func<bool> PenHeld;
        private UiButton _w20Pen;
        public UiButton PenButton => _w20Pen;

        private void BuildW4SocialFriends(RectTransform p)
        {
            float top = PH / 2;
            _socialInfo = UiKit.Label(p, Lang.T("Friends"), new Vector2(0, top - 30), new Vector2(W - 70, 40), 28, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            _requestText = UiKit.Label(p, "", new Vector2(-70, top - 76), new Vector2(W - 260, 46), 20, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.5f));
            _requestText.resizeTextForBestFit = true; _requestText.resizeTextMinSize = 14; _requestText.resizeTextMaxSize = _requestText.fontSize; // w4lang: "Last whisper from … — type /r text to answer" was cut in English too
            _accept = UiKit.Button(p, Lang.T("Accept"), new Vector2(W / 2 - 165, top - 76), new Vector2(120, 44), () => { Social?.AnswerRequest(true); _dirty = true; }, 20);
            _decline = UiKit.Button(p, Lang.T("Decline"), new Vector2(W / 2 - 45, top - 76), new Vector2(110, 44), () => { Social?.AnswerRequest(false); _dirty = true; }, 20);
            _w20Pen = UiKit.Button(p, Lang.T("Take pen"), new Vector2(W / 2 - 105, top - 30), new Vector2(170, 44), () => { TogglePen?.Invoke(); _dirty = true; }, 20); // client wave 20 (w20pen), point 57
            for (int i = 0; i < W4FriendsPerPage; i++)
            {
                float y = top - 128 - i * 60;
                var row = UiKit.Box(p, "friend", new Vector2(0, y), new Vector2(W - 50, 56), new Color(1, 1, 1, 0.04f));
                var label = UiKit.Label(row, "", new Vector2(-130, 0), new Vector2(W - 380, 54), 21, TextAnchor.MiddleLeft);
                int slot = i;
                var whisper = UiKit.Button(row, Lang.T("Whisper"), new Vector2((W - 50) / 2 - 205, 0), new Vector2(112, 48), () => { var f = W4FriendAt(slot); if (f != null) PrefillChat?.Invoke("/w " + f.Name + " : "); }, 18);
                var party = UiKit.Button(row, Lang.T("Party"), new Vector2((W - 50) / 2 - 105, 0), new Vector2(80, 48), () => { var f = W4FriendAt(slot); if (f != null) W4SocialPartyInvite?.Invoke(f.Name); }, 18);
                var remove = UiKit.Button(row, "x", new Vector2((W - 50) / 2 - 32, 0), new Vector2(56, 48), () => W4RemovePressed(slot), 19);
                _w4FriendRows.Add((row, label, whisper, party, remove));
            }
            _friendPageText = UiKit.Label(p, "", new Vector2(0, top - 494), new Vector2(300, 40), 21, TextAnchor.MiddleCenter, UiKit.Dim);
            UiKit.Button(p, "<", new Vector2(-W / 2 + 70, top - 494), new Vector2(90, 44), () => { _friendPage = Mathf.Max(0, _friendPage - 1); _dirty = true; }, 26);
            UiKit.Button(p, ">", new Vector2(W / 2 - 70, top - 494), new Vector2(90, 44), () => { _friendPage++; _dirty = true; }, 26);
            UiKit.Button(p, Lang.T("Say hi to friends"), new Vector2(-(W - 100) / 4 - 5, top - 548), new Vector2((W - 100) / 2, 48), () => Social?.SayHi(""), 19);
            _w4LoginOut = UiKit.Button(p, Lang.T("Login lines: on"), new Vector2((W - 100) / 4 + 5, top - 548), new Vector2((W - 100) / 2, 48), () => { if (Social != null) { Social.ShowLogins = !Social.ShowLogins; _dirty = true; } }, 19);
            _friendName = VrTextField.Create(p, new Vector2(-80, top - 606), new Vector2(W - 270, 52), 23);
            _friendName.Placeholder = Lang.T("add a friend: character name"); _friendName.OnFocus = f => RequestKeyboard?.Invoke(f); _friendName.OnSubmit = () => { Social?.AddFriend(_friendName.Text); _friendName.SetText(""); };
            UiKit.Button(p, Lang.T("Add"), new Vector2(W / 2 - 100, top - 606), new Vector2(150, 52), () => { Social?.AddFriend(_friendName.Text); _friendName.SetText(""); }, 22);
            _addGazed = UiKit.Button(p, Lang.T("Add the player I'm looking at"), new Vector2(-(W - 100) / 4 - 5, top - 666), new Vector2((W - 100) / 2, 52), () => { var n = GazedPlayerName?.Invoke(); if (n != null) Social?.AddFriend(n); else Toast(Lang.T("Look at a player first (gaze lock).")); }, 19);
            UiKit.Button(p, Lang.T("Trade with them"), new Vector2((W - 100) / 4 + 5, top - 666), new Vector2((W - 100) / 2, 52), () => TradeGazed?.Invoke(), 19);
        }

        private SocialHub.Friend W4FriendAt(int slot) { if (Social == null) return null; int i = _friendPage * W4FriendsPerPage + slot; return i < Social.Friends.Count ? Social.Friends[i] : null; }

        /// <summary>The first press arms the removal (the button reads "Sure?" for 4 s), the second sends it.</summary>
        private void W4RemovePressed(int slot)
        {
            var f = W4FriendAt(slot); if (f == null) return;
            if (_w4RemoveArmed == f.Cid && Time.time <= _w4RemoveUntil) { _w4RemoveArmed = 0; Social?.RemoveFriend(f); Toast(Lang.F("Removing {0} from your friends…", f.Name)); }
            else { _w4RemoveArmed = f.Cid; _w4RemoveUntil = Time.time + 4f; Toast(Lang.F("Press again to remove {0}.", f.Name)); }
            _dirty = true;
        }

        private void RefreshW4SocialFriends()
        {
            var friends = Social != null ? Social.Friends : new List<SocialHub.Friend>();
            int online = 0; foreach (var f in friends) if (f.Online) online++;
            _socialInfo.text = Lang.F("Friends  <color=#9aa3b5>{0}/40 · {1} online</color>", friends.Count, online);
            var req = Social?.PendingRequest;
            _requestText.text = req.HasValue ? Lang.F("{0} wants to be your friend", req.Value.name) : (Social != null && Social.LastWhisperFrom.Length > 0 ? Lang.F("Last whisper from {0} — type /r text to answer", Social.LastWhisperFrom) : "");
            _accept.gameObject.SetActive(req.HasValue); _decline.gameObject.SetActive(req.HasValue);
            int pages = Mathf.Max(1, (friends.Count + W4FriendsPerPage - 1) / W4FriendsPerPage);
            _friendPage = Mathf.Clamp(_friendPage, 0, pages - 1);
            _friendPageText.text = friends.Count == 0 ? Lang.T("no friends yet") : $"{_friendPage + 1} / {pages}";
            bool leader = W4SocialCanInvite != null && W4SocialCanInvite();
            for (int r = 0; r < W4FriendsPerPage; r++)
            {
                int i = _friendPage * W4FriendsPerPage + r;
                var (row, label, whisper, party, remove) = _w4FriendRows[r];
                bool show = i < friends.Count;
                row.gameObject.SetActive(show);
                if (!show) continue;
                var f = friends[i];
                label.text = $"{(f.Online ? "<color=#7fd67f>●</color>" : "<color=#6a7080>○</color>")} {f.Name}  <size=17><color=#9aa3b5>{(f.Online ? Lang.T("online") : Lang.T("offline"))}</color></size>";
                whisper.gameObject.SetActive(f.Online);
                party.gameObject.SetActive(f.Online && leader);
                bool armed = _w4RemoveArmed == f.Cid && Time.time <= _w4RemoveUntil;
                remove.SetLabel(armed ? Lang.T("Sure?") : "x"); remove.Resize(new Vector2(armed ? 80 : 56, 48));
            }
            if (_w4LoginOut != null) _w4LoginOut.SetLabel(Social == null || Social.ShowLogins ? Lang.T("Login lines: on") : Lang.T("Login lines: off"));
            if (_w20Pen != null) _w20Pen.SetLabel(PenHeld != null && PenHeld() ? Lang.T("Put pen away") : Lang.T("Take pen")); // client wave 20 (w20pen)
            var gazed = GazedPlayerName?.Invoke();
            _addGazed.SetLabel(gazed != null ? Lang.F("Add {0}", gazed) : Lang.T("Add the player I'm looking at"));
        }
    }
}
