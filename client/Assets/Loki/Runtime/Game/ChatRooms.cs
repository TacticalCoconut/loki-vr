using System;
using System.Collections.Generic;
using Loki.Net;
using Loki.Sprites;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Chat rooms (client wave 2; rAthena e985006 chat.cpp + clif.cpp, PACKETVER 20211103, research/chat_ignore_whisper.md):
    ///   create CZ_CREATE_CHATROOM 0x00d5 len.W limit.W type.B(0 private / 1 public) password[8] title (no NUL, ≤ 36) → ZC_ACK_CREATE_CHATROOM
    ///     0x00d6 flag.B (0 ok, 1 limit exceeded, 2 same room) — the creator never gets the chat id nor its own 0x00d7 (clif.cpp:4506-4513);
    ///   signs: ZC_ROOM_NEWENTRY 0x00d7 len.W owner.L chatId.L limit.W users.W type.B title (no NUL) to the owner's area except the room's
    ///     members, re-sent on every join / leave / change (update in place); type 0 private, 1 public, 2 NPC room, 3 NPC room without a
    ///     limit (not clickable) (clif.cpp:4521-4561); ZC_DESTROY_ROOM 0x00d8 chatId.L; no 0x00d8 when the owner leaves sight (drop the
    ///     sign with the unit);
    ///   join CZ_REQ_ENTER_ROOM 0x00d9 chatId.L password[8] → ZC_ENTER_ROOM 0x00db len.W chatId.L {role.L name[24]}* (role 0 owner / 1
    ///     normal; an NPC room lists the NPC first) or ZC_REFUSE_ENTER_ROOM 0x00da result.B (0 full, 1 password, 2 kicked, 4 zeny, 5 level
    ///     too low, 6 too high); the join check reads 9 password bytes, so passwords stay ≤ 7 characters here;
    ///   members: ZC_MEMBER_NEWENTRY 0x00dc users.W name[24]; ZC_MEMBER_EXIT 0x00dd users.W name[24] kicked.B (also to the leaver);
    ///     ZC_ROLE_CHANGE 0x00e1 role.L name[24]; ZC_CHANGE_CHATROOM 0x00df (as 0x00d7, to the members — the id arrives here);
    ///   owner: CZ_CHANGE_CHATROOM 0x00de (as 0x00d5), CZ_REQ_ROLE_CHANGE 0x00e0 role.L name[24], CZ_REQ_EXPEL_MEMBER 0x00e2 name[24];
    ///   leave: CZ_EXIT_ROOM 0x00e3. Talking in a room is the normal CZ_REQUEST_CHAT: the server sends it to the members only.
    /// While in a room the server refuses walking, skills and items (pc_cant_act); warping or logging out leaves the room.
    /// </summary>
    public sealed class ChatRooms
    {
        public sealed class Room { public uint Id, OwnerId; public int Limit, Users, Type; public string Title = ""; public bool Private => Type == 0; public bool Npc => Type >= 2; public bool Clickable => Type != 3; }
        public sealed class Member { public string Name; public bool Owner; }

        /// <summary>Rooms of other units in view, by chat id.</summary>
        public readonly Dictionary<uint, Room> Rooms = new Dictionary<uint, Room>();
        public bool InRoom { get; private set; }
        public uint MyChatId { get; private set; }
        public string MyTitle { get; private set; } = "";
        public int MyLimit { get; private set; }
        public bool MyPublic { get; private set; } = true;
        public bool MyNpcRoom { get; private set; }
        public readonly List<Member> Members = new List<Member>();
        public readonly List<string> Lines = new List<string>();
        public int LastRefusal = -1, LastCreateResult = -1;
        public event Action Changed;
        public Action<string> Toast;
        public bool AmOwner => InRoom && Members.Exists(m => m.Owner && m.Name == _self());

        private readonly RoSession _s; private readonly EntityManager _ents; private readonly ListWindow _win; private readonly Func<Camera> _head;
        private readonly Action<string, string> _chat; private readonly Func<string> _self; private readonly Action<string> _say;
        private enum Page { Nearby, Create, Members, Messages, Settings }
        private Page _page = Page.Nearby;
        // the create / settings form
        private string _fTitle = "", _fPass = ""; private int _fLimit = 5; private bool _fPublic = true;
        private enum Field { None, Title, Pass, Say, JoinPass }
        private Field _edit; private uint _joinId;
        private (string title, int limit, bool pub, string pass)? _pendingCreate;

        public ChatRooms(RoSession s, EntityManager ents, ListWindow win, Func<Camera> head, Func<string> self, Action<string, string> chat, Action<string> say)
        {
            _s = s; _ents = ents; _win = win; _head = head; _self = self; _chat = chat; _say = say;
            _win.OnCloseClicked = Hide;
            _ents.Vanished += (id, type) => { var gone = new List<uint>(); foreach (var r in Rooms.Values) if (r.OwnerId == id) gone.Add(r.Id); foreach (var g in gone) Rooms.Remove(g); if (gone.Count > 0) Refresh(); };
            _ents.UnitAdded += u => { foreach (var r in Rooms.Values) if (r.OwnerId == u.Id) Sign(r); };
        }

        /// <summary>Map change: the server took us out of any room (unit_remove_map → chat_leavechat); signs belong to the old map.</summary>
        public void OnMapChange() { Rooms.Clear(); if (InRoom) LeftRoom(false, true); Refresh(); }

        // ------------------------------------------------------------------ packets
        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x00d6:
                {
                    r.Skip(2); int flag = r.U8(); LastCreateResult = flag;
                    if (flag == 0 && _pendingCreate.HasValue)
                    {
                        var p = _pendingCreate.Value; _pendingCreate = null;
                        InRoom = true; MyChatId = 0; MyTitle = p.title; MyLimit = p.limit; MyPublic = p.pub; MyNpcRoom = false;
                        Members.Clear(); Members.Add(new Member { Name = _self(), Owner = true }); Lines.Clear();
                        Note(ClientText.Msg(64)); _page = Page.Members; Open();
                    }
                    else if (flag != 0) { Note(ClientText.Msg(flag == 1 ? 65 : 66)); _pendingCreate = null; }
                    Debug.Log($"[ChatRoom] create result {flag}");
                    Changed?.Invoke(); return true;
                }
                case 0x00d7:
                case 0x00df:
                {
                    r.Skip(4); uint owner = r.U32(), chat = r.U32(); int limit = r.U16(), users = r.U16(), type = r.U8();
                    string title = r.Text(n - 17); // cp1252 (Net/RoText.cs)
                    if (id == 0x00df && InRoom)
                    {
                        MyChatId = chat; MyTitle = title; MyLimit = limit; MyPublic = type != 0; MyNpcRoom = type >= 2;
                        Debug.Log($"[ChatRoom] my room changed: #{chat} '{title}' {users}/{limit} type {type}");
                        Refresh(); Changed?.Invoke(); return true;
                    }
                    if (!Rooms.TryGetValue(chat, out var room)) Rooms[chat] = room = new Room { Id = chat };
                    room.OwnerId = owner; room.Limit = limit; room.Users = users; room.Type = type; room.Title = title;
                    Sign(room);
                    Debug.Log($"[ChatRoom] sign #{chat} by {owner}: '{title}' {users}/{limit} type {type}");
                    Refresh(); Changed?.Invoke(); return true;
                }
                case 0x00d8:
                {
                    r.Skip(2); uint chat = r.U32();
                    if (Rooms.TryGetValue(chat, out var room)) { Rooms.Remove(chat); _ents.Get(room.OwnerId)?.Overhead?.SetChatSign(null, null, false); Debug.Log($"[ChatRoom] #{chat} closed"); }
                    Refresh(); Changed?.Invoke(); return true;
                }
                case 0x00da:
                {
                    r.Skip(2); int res = r.U8(); LastRefusal = res;
                    string m = res == 0 ? ClientText.Msg(67) : res == 1 ? Lang.T("Wrong password.") : res == 2 ? Lang.T("You were kicked out of that room and can't come back in.")
                             : res == 4 ? Lang.T("You don't have enough zeny to enter.") : res == 5 ? Lang.T("Your level is too low for that room.") : res == 6 ? Lang.T("Your level is too high for that room.")
                             : res == 7 ? Lang.T("Your job can't enter that room.") : Lang.F("You can't enter that room ({0}).", res);
                    Note(m); Debug.Log($"[ChatRoom] join refused {res}");
                    Changed?.Invoke(); return true;
                }
                case 0x00db:
                {
                    r.Skip(4); uint chat = r.U32();
                    Members.Clear(); Lines.Clear();
                    while (r.Remaining >= 28) { int role = r.S32(); string nm = r.Str(24); Members.Add(new Member { Name = nm, Owner = role == 0 }); }
                    InRoom = true; MyChatId = chat;
                    if (Rooms.TryGetValue(chat, out var room)) { MyTitle = room.Title; MyLimit = room.Limit; MyPublic = room.Type != 0; MyNpcRoom = room.Npc; }
                    Debug.Log($"[ChatRoom] entered #{chat} '{MyTitle}': {string.Join(", ", Members.ConvertAll(x => (x.Owner ? "*" : "") + x.Name))}");
                    Note(Lang.F("You entered the chat room '{0}'.", MyTitle));
                    _page = Page.Members; Open(); Changed?.Invoke(); return true;
                }
                case 0x00dc:
                {
                    r.Skip(2); int users = r.U16(); string nm = r.Str(24);
                    if (InRoom && !Members.Exists(x => x.Name == nm)) Members.Add(new Member { Name = nm });
                    Line(ClientText.Fill(ClientText.Msg(179), nm));
                    Refresh(); Changed?.Invoke(); return true;
                }
                case 0x00dd:
                {
                    r.Skip(2); int users = r.U16(); string nm = r.Str(24); bool kicked = r.U8() != 0;
                    if (nm == _self()) { LeftRoom(kicked, false); return true; }
                    Members.RemoveAll(x => x.Name == nm);
                    Line(ClientText.Fill(ClientText.Msg(kicked ? 181 : 180), nm));
                    Refresh(); Changed?.Invoke(); return true;
                }
                case 0x00e1:
                {
                    r.Skip(2); int role = r.S32(); string nm = r.Str(24);
                    var m = Members.Find(x => x.Name == nm); if (m != null) m.Owner = role == 0;
                    if (role == 0) Line(Lang.F("{0} is the room's owner now.", nm));
                    Debug.Log($"[ChatRoom] role {nm} = {role}");
                    Refresh(); Changed?.Invoke(); return true;
                }
                default: return false;
            }
        }

        /// <summary>A 0x008d line while we are in a room (the server sends room talk to the members only): kept for the Messages page.</summary>
        public void OnAreaLine(string line) { if (InRoom) Line(line); }
        public void OnOwnLine(string line) { if (InRoom) Line(line); }

        private void LeftRoom(bool kicked, bool silent)
        {
            InRoom = false; MyChatId = 0; Members.Clear();
            if (!silent) Note(kicked ? ClientText.Msg(68) : Lang.T("You left the chat room."));
            Debug.Log($"[ChatRoom] left (kicked {kicked})");
            _page = Page.Nearby; if (_edit == Field.Say) { _edit = Field.None; _win.SetInput(false); }
            Refresh(); Changed?.Invoke();
        }

        private void Sign(Room room)
        {
            var u = _ents.Get(room.OwnerId);
            if (u == null || u.Overhead == null) return;
            string text = room.Private ? Lang.F("{0}  ({1}/{2})  · private", room.Title, room.Users, room.Limit) : $"{room.Title}  ({room.Users}/{room.Limit})";
            var rr = room;
            u.Overhead.SetChatSign(text, room.Clickable ? () => AskJoin(rr) : (Action)null, room.Private);
        }

        private void Line(string s) { Lines.Add(s); if (Lines.Count > 60) Lines.RemoveAt(0); if (_page == Page.Messages) Refresh(); }
        private void Note(string s) { _chat(s, "ffd9a8"); Toast?.Invoke(s); }

        // ------------------------------------------------------------------ requests
        /// <summary>The laser on a sign, "Join" on the Nearby page or the player menu: public rooms join at once, private ones ask for the password.</summary>
        public void AskJoin(Room room)
        {
            if (room == null) return;
            if (InRoom) { Note(Lang.T("Leave your chat room first.")); return; }
            if (room.Private) { _joinId = room.Id; _page = Page.Nearby; Open(); Edit(Field.JoinPass); return; }
            Join(room.Id, "");
        }
        public void Join(uint chatId, string password)
        {
            _s.SendPacket(new PacketWriter(0x00d9).U32(chatId).Str(password ?? "", 8).ToArray());
            Debug.Log($"[ChatRoom] join #{chatId}{(string.IsNullOrEmpty(password) ? "" : " with a password")}");
        }
        public void Create(string title, int limit, bool pub, string password)
        {
            title = (title ?? "").Trim(); if (title.Length == 0) { Note(ClientText.Msg(13)); return; }
            if (title.Length > 36) title = title.Substring(0, 36);
            limit = Mathf.Clamp(limit, 2, 20);
            _pendingCreate = (title, limit, pub, pub ? "" : password ?? "");
            _s.SendPacket(RoomPacket(0x00d5, title, limit, pub, pub ? "" : password));
            Debug.Log($"[ChatRoom] create '{title}' limit {limit} {(pub ? "public" : "private")}");
        }
        public void ChangeSettings(string title, int limit, bool pub, string password)
        {
            if (!AmOwner) return;
            title = (title ?? "").Trim(); if (title.Length == 0) return;
            _s.SendPacket(RoomPacket(0x00de, title.Length > 36 ? title.Substring(0, 36) : title, Mathf.Clamp(limit, 2, 20), pub, pub ? "" : password));
            Debug.Log($"[ChatRoom] change '{title}' limit {limit} {(pub ? "public" : "private")}");
        }
        private static byte[] RoomPacket(ushort id, string title, int limit, bool pub, string password)
        {
            var w = new PacketWriter(id).U16(0).U16((ushort)limit).U8((byte)(pub ? 1 : 0)).Str(password ?? "", 8);
            var t = RoText.Encode(title); w.Bytes(t); // the title is not NUL-terminated (clif.cpp:12405)
            return w.ToArrayWithLength();
        }
        public void MakeOwner(string name) { if (AmOwner && name != _self()) { _s.SendPacket(new PacketWriter(0x00e0).U32(0).Str(name, 24).ToArray()); Debug.Log("[ChatRoom] give owner to " + name); } }
        public void Kick(string name) { if (AmOwner && name != _self()) { _s.SendPacket(new PacketWriter(0x00e2).Str(name, 24).ToArray()); Debug.Log("[ChatRoom] kick " + name); } }
        public void Leave() { if (InRoom) { _s.SendPacket(new PacketWriter(0x00e3).ToArray()); Debug.Log("[ChatRoom] leave"); } }
        /// <summary>The owner of a unit's room, for the player menu ("Join the chat room").</summary>
        public Room RoomOf(uint ownerId) { foreach (var r in Rooms.Values) if (r.OwnerId == ownerId) return r; return null; }

        // ------------------------------------------------------------------ window
        public bool Visible => _win.Visible;
        public ListWindow Window => _win;
        public void Show() { _page = InRoom ? Page.Members : Page.Nearby; Open(); }
        public void ShowCreate() { if (InRoom) { Show(); return; } _page = Page.Create; Open(); }
        private void Open() { _win.Open(_head()); Refresh(); }
        public void Hide() { CommitEdit(false); _win.SetInput(false); _win.Hide(); }

        private void Edit(Field f)
        {
            CommitEdit(false); _edit = f;
            string cur = f == Field.Title ? _fTitle : f == Field.Pass ? _fPass : "";
            string ph = f == Field.Title ? Lang.T("room title") : f == Field.Pass || f == Field.JoinPass ? Lang.T("password (up to 7)") : Lang.T("say something");
            _win.SetInput(true, ph, null, () => CommitEdit(true), cur, f == Field.Title ? 36 : f == Field.Pass || f == Field.JoinPass ? 7 : 200);
            _win.Input.Focus();
            Refresh();
        }
        private void CommitEdit(bool submit)
        {
            if (_edit == Field.None) return;
            string t = _win.Input.Text ?? ""; var f = _edit; _edit = Field.None; _win.SetInput(false);
            switch (f)
            {
                case Field.Title: _fTitle = t.Trim(); break;
                case Field.Pass: _fPass = t; break;
                case Field.JoinPass: if (submit && _joinId != 0) Join(_joinId, t); _joinId = 0; break;
                case Field.Say: if (submit && t.Trim().Length > 0) _say(t); break;
            }
            Refresh();
        }

        public void Refresh()
        {
            if (!_win.Visible) return;
            if (InRoom && (_page == Page.Nearby || _page == Page.Create)) _page = Page.Members;
            if (!InRoom && (_page == Page.Members || _page == Page.Messages || _page == Page.Settings)) _page = Page.Nearby;
            var rows = new List<ListWindow.Row>();
            if (InRoom)
            {
                bool owner = AmOwner;
                var tabs = owner && !MyNpcRoom ? new[] { Lang.T("Members"), Lang.T("Messages"), Lang.T("Settings") } : new[] { Lang.T("Members"), Lang.T("Messages") };
                _win.SetTabs(tabs, _page == Page.Members ? 0 : _page == Page.Messages ? 1 : 2, t => { CommitEdit(false); _page = t == 0 ? Page.Members : t == 1 ? Page.Messages : Page.Settings; if (_page == Page.Settings) { _fTitle = MyTitle; _fLimit = Mathf.Max(MyLimit, 2); _fPublic = MyPublic; } Refresh(); });
                _win.SetTitle(Lang.F("Chat room: {0}", MyTitle));
                if (_edit == Field.None) _win.SetInfo((MyLimit > 0 ? Lang.F("{0}/{1} here", Members.Count, MyLimit) : Lang.F("{0} here", Members.Count)) + "  ·  " + (MyPublic ? Lang.T("~room~public") : Lang.T("~room~private")) + (owner ? "  ·  " + Lang.T("you are the owner") : "")); // w4lang: separate texts joined by "  ·  "
                else _win.SetInfo(Lang.T("Type, then Enter:"));
                if (_page == Page.Members)
                    foreach (var m in Members)
                    {
                        var mm = m; bool me = m.Name == _self();
                        rows.Add(new ListWindow.Row { Text = $"{(m.Owner ? "<color=#ffd060>★</color> " : "")}{m.Name}{(me ? "  " + Lang.T("<color=#9aa3b5>(you)</color>") : "")}", Highlight = m.Owner,
                            A = owner && !me && !MyNpcRoom ? Lang.T("Owner") : null, OnA = () => MakeOwner(mm.Name), B = owner && !me && !MyNpcRoom ? Lang.T("Kick") : null, OnB = () => Kick(mm.Name) });
                    }
                else if (_page == Page.Messages)
                {
                    int from = Mathf.Max(0, Lines.Count - 21);
                    for (int i = from; i < Lines.Count; i++) rows.Add(new ListWindow.Row { Text = $"<size=20>{UiKit.RoColors(Lines[i])}</size>" });
                    if (rows.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>Nothing said yet. Only the people in the room hear you.</color>") });
                }
                else FormRows(rows);
                if (_page == Page.Settings) _win.SetFooter((Lang.T("Close"), Hide), (Lang.T("Apply"), () => { CommitEdit(false); ChangeSettings(_fTitle, _fLimit, _fPublic, _fPass); }), (Lang.T("Leave"), Leave));
                else _win.SetFooter((Lang.T("Close"), Hide), (Lang.T("Leave"), Leave), (Lang.T("Say…"), () => Edit(Field.Say)));
                _win.SetRows(rows);
                if (_page == Page.Messages) _win.ShowRow(rows.Count - 1);
                return;
            }
            _win.SetTabs(new[] { Lang.T("Rooms nearby"), Lang.T("Create") }, _page == Page.Create ? 1 : 0, t => { CommitEdit(false); _page = t == 1 ? Page.Create : Page.Nearby; Refresh(); });
            _win.SetTitle(Lang.T("Chat rooms"));
            if (_page == Page.Create)
            {
                if (_edit == Field.None) _win.SetInfo(Lang.T("Only the people inside hear what you say. You can't walk while you're in a room."));
                FormRows(rows);
                _win.SetFooter((Lang.T("Close"), Hide), (Lang.T("Create"), () => { CommitEdit(false); Create(_fTitle, _fLimit, _fPublic, _fPass); }));
            }
            else
            {
                if (_edit == Field.None) _win.SetInfo(Rooms.Count == 0 ? Lang.T("No chat rooms in sight.") : Lang.T("Pick a room, or point the laser at a room's sign."));
                else if (_edit == Field.JoinPass) _win.SetInfo(Lang.T("Password:"));
                var self = _ents.Self;
                var list = new List<Room>(Rooms.Values);
                if (self != null) list.Sort((a, c) => Dist(a).CompareTo(Dist(c)));
                foreach (var room in list)
                {
                    var rr = room; var owner = _ents.Get(room.OwnerId);
                    string who = owner != null ? owner.DisplayName : "?";
                    string kind = room.Npc ? "NPC" : room.Private ? Lang.T("~room~private") : Lang.T("~room~public");
                    bool full = room.Limit > 0 && room.Users >= room.Limit;
                    rows.Add(new ListWindow.Row { Text = $"{room.Title}\n<size=19><color=#9aa3b5>{room.Users}/{room.Limit} · {kind} · {who}{(self != null && owner != null ? " · " + Lang.F("{0} cells", Mathf.RoundToInt(Dist(room))) : "")}</color></size>",
                        A = room.Clickable ? Lang.T("Join") : null, OnA = () => AskJoin(rr), AEnabled = !full });
                }
                _win.SetFooter((Lang.T("Close"), Hide), (Lang.T("Create…"), () => { _page = Page.Create; Refresh(); }));
            }
            _win.SetRows(rows);
        }

        private float Dist(Room r) { var u = _ents.Get(r.OwnerId); var s = _ents.Self; return u != null && s != null ? Vector2.Distance(u.Cell, s.Cell) : 999f; }

        private void FormRows(List<ListWindow.Row> rows)
        {
            if (_edit != Field.None) _win.SetInfo(Lang.T("Type, then Enter:"));
            rows.Add(new ListWindow.Row { Text = Lang.F("Title: {0}", _fTitle.Length > 0 ? _fTitle : "<color=#9aa3b5>—</color>"), A = Lang.T("Edit"), OnA = () => Edit(Field.Title) });
            rows.Add(new ListWindow.Row { Text = Lang.F("People: <color=#ffd060>{0}</color>  <color=#9aa3b5>(2 – 20)</color>", _fLimit), A = "−", OnA = () => { _fLimit = Mathf.Max(2, _fLimit - 1); Refresh(); }, B = "+", OnB = () => { _fLimit = Mathf.Min(20, _fLimit + 1); Refresh(); } });
            rows.Add(new ListWindow.Row { Text = _fPublic ? Lang.T("Anyone can come in (public)") : Lang.T("Only with the password (private)"), A = _fPublic ? Lang.T("~room~Private") : Lang.T("~room~Public"), OnA = () => { _fPublic = !_fPublic; Refresh(); } });
            if (!_fPublic) rows.Add(new ListWindow.Row { Text = Lang.F("Password: {0}", _fPass.Length > 0 ? new string('•', _fPass.Length) : "<color=#9aa3b5>—</color>"), A = Lang.T("Edit"), OnA = () => Edit(Field.Pass) });
        }

    }
}
