using System;
using System.Collections.Generic;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Whisper windows and the ignore list (client wave 2; rAthena e985006, research/chat_ignore_whisper.md §3-4):
    ///   whispers keep going through SocialHub (CZ_WHISPER 0x0096 → ZC_ACK_WHISPER 0x09df result.B cid.L: 0 sent, 1 offline, 2 ignored,
    ///   3 blocks everyone; ZC_WHISPER 0x09de len.W gid.L name[24] isAdmin.B text); this window keeps one conversation per partner (the
    ///   official client's 1:1 chat windows). The ack names nobody (its CID is the sender's own), so answers are matched to the whispers
    ///   sent in order; "NPC:" and "#channel" whispers get no ack at all (clif.cpp:11861-11977).
    ///   Ignore list: CZ_SETTING_WHISPER_PC 0x00cf name[24] type.B (0 block / 1 allow) → ZC_SETTING_WHISPER_PC 0x00d1 type.B result.B
    ///   (0 ok, 1 fail, 2 list full); CZ_SETTING_WHISPER_STATE 0x00d0 type.B (0 block all / 1 allow all — which lifts "block all" when it is on, else empties the list) →
    ///   ZC_SETTING_WHISPER_STATE 0x00d2 type.B result.B; CZ_REQ_WHISPER_LIST 0x00d3 → ZC_WHISPER_LIST 0x00d4 len.W name[24]* (≤ 20).
    ///   The server keeps the list in memory only (gone at the next login) and filters whispers only.
    /// </summary>
    public sealed class Whispers
    {
        public sealed class Line { public bool Mine; public string Text; public string Note; }
        public sealed class Chat { public string Partner; public readonly List<Line> Lines = new List<Line>(); public int Unread; public float Last; }
        public readonly List<Chat> Chats = new List<Chat>();
        public readonly List<string> Ignored = new List<string>();
        public bool BlockAll { get; private set; }
        public int LastIgnoreResult = -1, LastIgnoreAllResult = -1; public bool ListReceived { get; private set; }
        public int Unread { get { int n = 0; foreach (var c in Chats) n += c.Unread; return n; } }
        public event Action Changed;
        public Action<string> Toast;

        private readonly RoSession _s; private readonly SocialHub _social; private readonly ListWindow _win; private readonly Func<Camera> _head; private readonly Action<string, string> _chat;
        private readonly Queue<(Chat chat, Line line, float at)> _pending = new Queue<(Chat, Line, float)>();
        private Chat _open; private enum Page { Chats, Ignore }
        private Page _page;
        private enum Field { None, Reply, NewPartner, Block }
        private Field _edit;
        private string _pendingIgnore = "";

        public Whispers(RoSession s, SocialHub social, ListWindow win, Func<Camera> head, Action<string, string> chat)
        {
            _s = s; _social = social; _win = win; _head = head; _chat = chat;
            _win.OnCloseClicked = Hide;
            social.WhisperIn += (from, text, admin) =>
            {
                var c = ChatWith(from); c.Lines.Add(new Line { Text = text, Note = admin ? "GM" : null }); c.Last = Time.time;
                if (!(_win.Visible && _open == c)) { c.Unread++; Toast?.Invoke(Lang.F("Whisper from {0}", from)); }
                Refresh(); Changed?.Invoke();
            };
            social.WhisperOut += (to, text) =>
            {
                var c = ChatWith(to); var l = new Line { Mine = true, Text = text }; c.Lines.Add(l); c.Last = Time.time;
                if (!to.StartsWith("NPC:", StringComparison.OrdinalIgnoreCase) && !to.StartsWith("#")) _pending.Enqueue((c, l, Time.time));
                Refresh(); Changed?.Invoke();
            };
            social.WhisperAck += res =>
            {
                while (_pending.Count > 0 && Time.time - _pending.Peek().at > 15f) _pending.Dequeue(); // an answer that never came
                if (_pending.Count == 0) return;
                var (c, l, _) = _pending.Dequeue();
                if (res != 0) l.Note = res == 1 ? Lang.T("not online") : res == 2 ? Lang.T("they are ignoring you") : Lang.T("they block all whispers");
                Refresh(); Changed?.Invoke();
            };
        }

        public void ResetForCharacter() { Chats.Clear(); Ignored.Clear(); BlockAll = false; ListReceived = false; _pending.Clear(); _open = null; _win.Hide(); Changed?.Invoke(); }

        public Chat ChatWith(string partner)
        {
            var c = Chats.Find(x => string.Equals(x.Partner, partner, StringComparison.Ordinal));
            if (c == null) { c = new Chat { Partner = partner }; Chats.Add(c); }
            return c;
        }

        // ------------------------------------------------------------------ ignore list packets
        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x00d1:
                {
                    r.Skip(2); int type = r.U8(), res = r.U8(); LastIgnoreResult = res;
                    string who = _pendingIgnore; _pendingIgnore = "";
                    if (res == 0 && who.Length > 0) { if (type == 0) { if (!Ignored.Contains(who)) Ignored.Add(who); } else Ignored.Remove(who); }
                    int msg = type == 0 ? (res == 0 ? 193 : res == 1 ? 194 : 195) : (res == 0 ? 196 : res == 1 ? 197 : 198); // the official ": Whispering Blocked." lines
                    _chat(who + ClientText.Msg(msg), res == 0 ? "c8cbd6" : "ffb070");
                    Debug.Log($"[Ignore] {(type == 0 ? "block" : "allow")} '{who}' → {res}");
                    RequestList(); Refresh(); Changed?.Invoke(); return true;
                }
                case 0x00d2:
                {
                    r.Skip(2); int type = r.U8(), res = r.U8(); LastIgnoreAllResult = res;
                    // /inall only lifts "block all" when it is on; with it off it wipes the per-name list (clif.cpp:15190-15217)
                    if (res == 0) { if (type == 0) BlockAll = true; else if (BlockAll) BlockAll = false; else Ignored.Clear(); }
                    RequestList();
                    _chat(ClientText.Msg(type == 0 ? (res == 0 ? 58 : 59) : (res == 0 ? 60 : 61)), res == 0 ? "c8cbd6" : "ffb070");
                    Debug.Log($"[Ignore] {(type == 0 ? "block all" : "allow all")} → {res}");
                    Refresh(); Changed?.Invoke(); return true;
                }
                case 0x00d4:
                {
                    r.Skip(4); Ignored.Clear();
                    while (r.Remaining >= 24) { string nm = r.Str(24); if (nm.Length > 0) Ignored.Add(nm); }
                    ListReceived = true;
                    Debug.Log($"[Ignore] list: {Ignored.Count} ({string.Join(", ", Ignored)})");
                    Refresh(); Changed?.Invoke(); return true;
                }
                default: return false;
            }
        }

        public void Block(string name) { name = (name ?? "").Trim(); if (name.Length == 0) return; _pendingIgnore = name; _s.SendPacket(new PacketWriter(0x00cf).Str(name, 24).U8(0).ToArray()); Debug.Log("[Ignore] block " + name); }
        public void Allow(string name) { name = (name ?? "").Trim(); if (name.Length == 0) return; _pendingIgnore = name; _s.SendPacket(new PacketWriter(0x00cf).Str(name, 24).U8(1).ToArray()); Debug.Log("[Ignore] allow " + name); }
        public void SetBlockAll(bool block) { _s.SendPacket(new PacketWriter(0x00d0).U8((byte)(block ? 0 : 1)).ToArray()); Debug.Log("[Ignore] " + (block ? "block all" : "allow all")); }
        public void RequestList() { _s.SendPacket(new PacketWriter(0x00d3).ToArray()); }
        public bool IsIgnored(string name) => Ignored.Contains(name);

        // ------------------------------------------------------------------ window
        public bool Visible => _win.Visible;
        public ListWindow Window => _win;
        public string OpenPartner => _open?.Partner;
        /// <summary>Client wave 19 (w19world), point 46: true while the window may not open (the tutorial: LokiHud.ChatRefused says so).</summary>
        public System.Func<bool> Locked;
        public void Show() { if (Locked != null && Locked()) return; _page = Page.Chats; _open = null; _win.Open(_head()); Refresh(); }
        public void ShowIgnore() { if (Locked != null && Locked()) return; _page = Page.Ignore; _open = null; _win.Open(_head()); RequestList(); Refresh(); }
        public void OpenChat(string partner) { if (Locked != null && Locked()) return; _page = Page.Chats; _open = ChatWith(partner); _open.Unread = 0; _win.Open(_head()); Refresh(); Changed?.Invoke(); }
        public void Hide() { _edit = Field.None; _win.SetInput(false); _win.Hide(); }

        private void Edit(Field f, string placeholder, int max)
        {
            _edit = f;
            _win.SetInput(true, placeholder, null, Submit, "", max);
            _win.Input.Focus();
            Refresh();
        }
        private void Submit()
        {
            string t = (_win.Input.Text ?? "").Trim(); var f = _edit; _edit = Field.None; _win.SetInput(false);
            if (t.Length > 0)
                switch (f)
                {
                    case Field.Reply: if (_open != null) _social.Whisper(_open.Partner, t); break;
                    case Field.NewPartner: OpenChat(t); return;
                    case Field.Block: Block(t); break;
                }
            Refresh();
        }

        public void Refresh()
        {
            if (!_win.Visible) return;
            var rows = new List<ListWindow.Row>();
            _win.SetTabs(new[] { Lang.T("Whispers"), Lang.T("Ignore list") }, _page == Page.Ignore ? 1 : 0, t => { _edit = Field.None; _win.SetInput(false); _page = t == 1 ? Page.Ignore : Page.Chats; _open = null; if (_page == Page.Ignore) RequestList(); Refresh(); });
            if (_page == Page.Ignore)
            {
                _win.SetTitle(Lang.T("Ignore list"));
                if (_edit == Field.None) _win.SetInfo(BlockAll ? Lang.T("<color=#ff8a7a>You block whispers from everyone.</color>") : Ignored.Count == 0 ? ClientText.Msg(62) : Lang.F("{0} blocked (up to 20; the list lasts until you log out)", Ignored.Count));
                else _win.SetInfo(Lang.T("Name to block, then Enter:"));
                foreach (var nm in Ignored) { var n2 = nm; rows.Add(new ListWindow.Row { Text = nm, A = Lang.T("~whisper~Allow"), OnA = () => Allow(n2) }); }
                _win.SetFooter((Lang.T("Close"), Hide), (BlockAll ? Lang.T("Allow all") : Lang.T("Block all"), () => SetBlockAll(!BlockAll)), (Lang.T("Block…"), () => Edit(Field.Block, Lang.T("character name"), 23)));
                _win.SetRows(rows, true);
                return;
            }
            if (_open != null)
            {
                var c = _open; c.Unread = 0;
                _win.SetTitle(Lang.F("Whisper: {0}", c.Partner));
                if (_edit == Field.None) _win.SetInfo(IsIgnored(c.Partner) ? Lang.T("<color=#ff8a7a>You block their whispers.</color>") : Lang.T("Only the two of you see this."));
                else _win.SetInfo(Lang.T("Your message, then Enter:"));
                int from = Mathf.Max(0, c.Lines.Count - 28);
                for (int i = from; i < c.Lines.Count; i++)
                {
                    var l = c.Lines[i];
                    string who = l.Mine ? Lang.T("<color=#9ad1ff>you</color>") : $"<color=#ffb3ff>{c.Partner}</color>";
                    rows.Add(new ListWindow.Row { Text = $"<size=20>{who}: {UiKit.RoColors(l.Text)}{(l.Note != null ? $"  <color=#ff8a7a>({l.Note})</color>" : "")}</size>", Highlight = !l.Mine });
                }
                if (rows.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>No messages yet.</color>") });
                _win.SetFooter((Lang.T("Back"), () => { _open = null; _edit = Field.None; _win.SetInput(false); Refresh(); }),
                               (IsIgnored(c.Partner) ? Lang.T("~whisper~Allow") : Lang.T("Block"), () => { if (IsIgnored(c.Partner)) Allow(c.Partner); else Block(c.Partner); }),
                               (Lang.T("Reply"), () => Edit(Field.Reply, Lang.T("message"), 200)));
                _win.SetRows(rows);
                _win.ShowRow(rows.Count - 1);
                Changed?.Invoke();
                return;
            }
            _win.SetTitle(Lang.T("Whispers"));
            if (_edit == Field.None) _win.SetInfo(Chats.Count == 0 ? Lang.T("No whispers yet.") : (Chats.Count > 1 ? Lang.F("{0} conversations", Chats.Count) : Lang.T("1 conversation")) + (Unread > 0 ? ", " + (Unread == 1 ? Lang.T("1 unread") : Lang.F("{0} unread", Unread)) : "")); // w4lang: the count and the unread count are separate texts
            else _win.SetInfo(Lang.T("Who? (character name, then Enter)"));
            var list = new List<Chat>(Chats); list.Sort((a, x) => x.Last.CompareTo(a.Last));
            foreach (var c in list)
            {
                var cc = c; var last = c.Lines.Count > 0 ? c.Lines[c.Lines.Count - 1] : null;
                string preview = last == null ? "" : (last.Mine ? Lang.T("you:") + " " : "") + (last.Text.Length > 60 ? last.Text.Substring(0, 60) + "…" : last.Text);
                rows.Add(new ListWindow.Row { Text = $"{(c.Unread > 0 ? $"<color=#ffd060>● {c.Unread}</color> " : "")}{c.Partner}\n<size=19><color=#9aa3b5>{UiKit.StripTags(preview)}</color></size>", Highlight = c.Unread > 0, A = Lang.T("Open"), OnA = () => OpenChat(cc.Partner) });
            }
            _win.SetFooter((Lang.T("Close"), Hide), (Lang.T("~whisper~New…"), () => Edit(Field.NewPartner, Lang.T("character name"), 23)));
            _win.SetRows(rows);
        }

    }
}
