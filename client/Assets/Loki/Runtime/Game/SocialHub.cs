using System;
using System.Collections.Generic;
using Loki.Net;
using Loki.Sprites;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Other players as people: chat bubbles and channels, whispers, the friends list (rAthena e985006, PACKETVER 20211103):
    ///   0x008d ZC_NOTIFY_CHAT len.W gid.L "Name : text" (area chat, also NPC talk) → chat line + bubble over the speaker;
    ///   0x008e ZC_NOTIFY_PLAYERCHAT len.W text — our own chat ("Name : text", bubble) and server replies (no name, no bubble);
    ///   0x0109 ZC_NOTIFY_CHAT_PARTY len.W aid.L "Name : text"; 0x017f ZC_GUILD_CHAT len.W "Name : text";
    ///   0x09de ZC_WHISPER len.W senderGID.L sender[24] isAdmin.B text; 0x09df ZC_ACK_WHISPER result.B cid.L (0 ok, 1 offline, 2/3 ignored);
    ///   friends: 0x0201 list {aid.L cid.L name[24]}, 0x0206 state aid.L cid.L offline.B name[24], 0x0207 request aid.L cid.L name[24],
    ///   0x0209 add result type.W aid.L cid.L name[24] (0 ok, 1 their list full, 2 already, 3 ours full), 0x020a removed aid.L cid.L.
    /// Console prefixes: "/w Name text" or "/whisper", "/r text" (reply to the last whisper), "%text" party, "$text" guild; "#chan text"
    /// goes to a server chat channel the player joined (Game/Channels.cs, client wave 4).
    /// Client wave 4 (w4social): every line carries its <see cref="UI.ChatKind"/> (the chat window's tabs) through <see cref="ChatAs"/>;
    /// the friend results use the client's own texts (msgstringtable 818-822, the ids rAthena names at clif.cpp:15393-15397); a
    /// friend's login / logout line follows the official /loginout switch (<see cref="ShowLogins"/>).
    /// </summary>
    public sealed class SocialHub
    {
        public sealed class Friend { public uint Aid, Cid; public string Name; public bool Online; }
        public readonly List<Friend> Friends = new List<Friend>();
        /// <summary>A friend request waiting for our answer (aid, cid, name); null when none.</summary>
        public (uint aid, uint cid, string name)? PendingRequest;
        public string LastWhisperFrom { get; private set; } = "";
        public string LastWhisperTo { get; private set; } = "";
        public event Action Changed;
        /// <summary>Client wave 2 hooks (chat rooms, whisper windows): an area / room line 0x008d (speaker gid, "Name : text"), our own
        /// echo 0x008e ("Name : text"), a whisper received (from, text, GM), a whisper sent (to, text) and its ZC_ACK_WHISPER result.</summary>
        public event Action<uint, string> AreaLine;
        public event Action<string> OwnLine;
        /// <summary>Client wave 4: a 0x008e line that is not our own chat (a server reply: @command answers, duel / channel notices).</summary>
        public event Action<string> ServerLine;
        public event Action<string, string, bool> WhisperIn;
        public event Action<string, string> WhisperOut;
        public event Action<int> WhisperAck;
        private readonly RoSession _s; private readonly EntityManager _ents; private readonly Action<string, string> _chat; private readonly Func<string> _selfName;

        public SocialHub(RoSession s, EntityManager ents, Func<string> selfName, Action<string, string> chat) { _s = s; _ents = ents; _selfName = selfName; _chat = chat; }

        /// <summary>Client wave 4: prints a line of a given kind (the chat window's tabs); unset → the plain chat action.</summary>
        public Action<string, string, UI.ChatKind> ChatAs;
        private void Line(UI.ChatKind kind, string text, string color) { if (ChatAs != null) ChatAs(text, color, kind); else _chat(text, color); }
        /// <summary>The official client's /loginout: friends' (and guild-mates') login / logout lines on or off. On by default.</summary>
        public bool ShowLogins = true;
        /// <summary>Client wave 4: is this character in our guild (the whisper's "[ Member ]" tag)?</summary>
        public Func<string, bool> IsGuildMate;
        /// <summary>Client wave 14 (w14kit), point 24: lines the Character presets keep out of the chat (true: not shown, only logged): a
        /// server line to me (0x008e, not my own chat: the GM commands' answers while a preset is set up), a whisper (from, text, is
        /// admin: the GM check's whisper to myself) and a whisper's result (the GM check's).</summary>
        public Func<string, bool> TakeServerLine;
        public Func<string, string, bool, bool> TakeWhisper;
        public Func<int, bool> TakeWhisperAck;
        /// <summary>A friend's state changed (aid, online) — the smoke listens.</summary>
        public event Action<uint, bool> FriendState;

        public void Reset() { }

        /// <summary>"Name : text" → (Name, text); null name when there is no separator.</summary>
        public static (string name, string text) Split(string line)
        {
            int i = line.IndexOf(" : ", StringComparison.Ordinal);
            return i > 0 ? (line.Substring(0, i), line.Substring(i + 3)) : (null, line);
        }

        private void Bubble(uint gid, string text)
        {
            var u = _ents.Get(gid);
            u?.Overhead?.SetSpeech(text, Mathf.Clamp(2.5f + text.Length * 0.08f, 3f, 9f));
        }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x008d:
                {
                    r.Skip(4); uint gid = r.U32(); string line = r.ChatLine(n - 8); // cp1252 text, Latin-1 name (Net/RoText.cs, w3login)
                    var (name, text) = Split(line);
                    Line(UI.ChatKind.Public, line, "e8e8e8");
                    if (name != null) Bubble(gid, text);
                    AreaLine?.Invoke(gid, line);
                    return true;
                }
                case 0x008e:
                {
                    r.Skip(4); string line = r.ChatLine(n - 4);
                    var (name, text) = Split(line);
                    bool own = name != null && name == _selfName();
                    if (!own && TakeServerLine != null && TakeServerLine(line)) { Debug.Log("[Social] (kept out of the chat) " + line); ServerLine?.Invoke(line); return true; }
                    Line(own ? UI.ChatKind.Public : UI.ChatKind.System, line, own ? "ffffff" : null);
                    if (own) { Bubble(_s.AccountId, text); OwnLine?.Invoke(line); }
                    else ServerLine?.Invoke(line);
                    return true;
                }
                case 0x0109:
                {
                    r.Skip(4); uint aid = r.U32(); string line = r.ChatLine(n - 8);
                    var (name, text) = Split(line);
                    Line(UI.ChatKind.Party, Lang.F("[Party] {0}", line), "ffd9a8");
                    if (name != null) Bubble(aid, text);
                    return true;
                }
                case 0x017f:
                {
                    r.Skip(4); string line = r.ChatLine(n - 4);
                    var (name, text) = Split(line);
                    Line(UI.ChatKind.Guild, Lang.F("[Guild] {0}", line), "b8f0b8");
                    if (name != null) foreach (var u in _ents.Units.Values) if (u != null && u.IsPlayerType && u.Name == name) { u.Overhead?.SetSpeech(text); break; }
                    return true;
                }
                case 0x09de:
                {
                    r.Skip(4); uint gid = r.U32(); string from = r.Str(24); bool admin = r.U8() != 0; string text = r.Text(n - 33);
                    if (TakeWhisper != null && TakeWhisper(from, text, admin)) return true;
                    LastWhisperFrom = from;
                    // msgstringtable 921: "Whispers from friends are displayed as [ Friend ], and ones from guildsmen are displayed as [ Member ]."
                    string rel = Friends.Exists(x => x.Name == from) ? Lang.T("[ Friend ]") + " " : IsGuildMate != null && IsGuildMate(from) ? Lang.T("[ Member ]") + " " : "";
                    Line(UI.ChatKind.Whisper, rel + (admin ? Lang.F("[from {0} (GM)] {1}", from, text) : Lang.F("[from {0}] {1}", from, text)), "ffb3ff");
                    WhisperIn?.Invoke(from, text, admin);
                    Changed?.Invoke();
                    return true;
                }
                case 0x09df:
                {
                    r.Skip(2); int res = r.U8(); r.U32();
                    if (TakeWhisperAck != null && TakeWhisperAck(res)) return true;
                    string m = res == 0 ? null : res == 1 ? Lang.F("{0} isn't online.", LastWhisperTo) : res == 2 ? Lang.F("{0} is ignoring you.", LastWhisperTo) : Lang.F("{0} is ignoring everyone.", LastWhisperTo);
                    if (m != null) Line(UI.ChatKind.Whisper, m, "ffb070");
                    WhisperAck?.Invoke(res);
                    return true;
                }
                case 0x0201:
                {
                    r.Skip(4); Friends.Clear();
                    while (r.Remaining >= 32) Friends.Add(new Friend { Aid = r.U32(), Cid = r.U32(), Name = r.Str(24) });
                    Debug.Log($"[Social] friends: {Friends.Count}");
                    Changed?.Invoke();
                    return true;
                }
                case 0x0206: // ZC_FRIENDS_STATE aid.L cid.L offline.B name[24] (clif.cpp:15303-15321; after the list, then on every login / logout)
                {
                    r.Skip(2); uint aid = r.U32(), cid = r.U32(); bool offline = r.U8() != 0; string name = r.Remaining >= 24 ? r.Str(24) : "";
                    var f = Friends.Find(x => x.Cid == cid);
                    if (f == null) { f = new Friend { Aid = aid, Cid = cid, Name = name }; Friends.Add(f); }
                    bool was = f.Online;
                    f.Online = !offline; if (name.Length > 0) f.Name = name;
                    // msgstringtable 1041 "%s has logged in." / 1042 "%s has logged out." — the lines the official /loginout switch
                    // (msgstringtable 1043-1045, "Shows guildsmen and friends online status") turns on and off
                    if (ShowLogins && was != f.Online) Line(UI.ChatKind.System, ClientText.Fill(ClientText.Msg(offline ? 1042 : 1041), f.Name), "a8d8ff");
                    FriendState?.Invoke(aid, f.Online);
                    Changed?.Invoke();
                    return true;
                }
                case 0x0207: // ZC_REQ_ADD_FRIENDS aid.L cid.L name[24] (clif.cpp:15416-15428)
                {
                    r.Skip(2); uint aid = r.U32(), cid = r.U32(); string name = r.Str(24);
                    PendingRequest = (aid, cid, name);
                    // msgstringtable 818 "(%s) wishes to be friends with you. Would you like to accept?" — answered on the wrist's Social tab
                    Line(UI.ChatKind.System, ClientText.Fill(ClientText.Msg(818), name), "ffd060");
                    Changed?.Invoke();
                    return true;
                }
                case 0x0209: // ZC_ADD_FRIENDS_LIST result.W aid.L cid.L name[24] (clif.cpp:15389-15413)
                {
                    // result 0 = msgstringtable 821 "You have become friends with (%s).", 1 = 822 "(%s) does not want to be friends with you."
                    // (a refusal: clif_parse_FriendsListReply sends 1 when the reply is 0, clif.cpp:15510-15511), 2 = 819 "Your Friend List is
                    // full." (sent for either side's full list, clif.cpp:15454-15456, 15517-15519), 3 = 820 "(%s)'s Friend List is full."
                    r.Skip(2); int type = r.U16(); uint aid = r.U32(), cid = r.U32(); string name = r.Str(24);
                    LastAddResult = type;
                    if (type == 0 && !Friends.Exists(x => x.Cid == cid)) Friends.Add(new Friend { Aid = aid, Cid = cid, Name = name, Online = true });
                    int msg = type == 0 ? 821 : type == 1 ? 822 : type == 2 ? 819 : 820;
                    Line(UI.ChatKind.System, ClientText.Fill(ClientText.Msg(msg), name).TrimEnd(), type == 0 ? "a8e6a3" : "ffb070");
                    Changed?.Invoke();
                    return true;
                }
                case 0x020a:
                {
                    r.Skip(2); uint aid = r.U32(), cid = r.U32();
                    var f = Friends.Find(x => x.Cid == cid);
                    if (f != null) { Friends.Remove(f); Line(UI.ChatKind.System, Lang.F("{0} was removed from your friends.", f.Name), "c8cbd6"); }
                    Changed?.Invoke();
                    return true;
                }
                default: return false;
            }
        }

        // ---- sending
        /// <summary>A console line: routes whispers, party and guild chat by prefix; everything else is public chat (and @commands).</summary>
        public void Say(string line)
        {
            string me = _selfName();
            if (line.StartsWith("/w ", StringComparison.OrdinalIgnoreCase) || line.StartsWith("/whisper ", StringComparison.OrdinalIgnoreCase))
            {
                string rest = line.Substring(line.IndexOf(' ') + 1).TrimStart();
                // the name may contain spaces: "/w Some Name : text" or "/w Name text" (first word)
                string target, text; int sep = rest.IndexOf(" : ", StringComparison.Ordinal);
                if (sep > 0) { target = rest.Substring(0, sep); text = rest.Substring(sep + 3); }
                else { int sp = rest.IndexOf(' '); if (sp < 0) { _chat(Lang.T("Use: /w Name text"), "ffb070"); return; } target = rest.Substring(0, sp); text = rest.Substring(sp + 1); }
                Whisper(target, text);
                return;
            }
            if (line.StartsWith("/r ", StringComparison.OrdinalIgnoreCase)) { if (LastWhisperFrom.Length == 0) { _chat(Lang.T("Nobody has whispered you yet."), "ffb070"); return; } Whisper(LastWhisperFrom, line.Substring(3)); return; }
            if (line.StartsWith("%") && line.Length > 1) { _s.PartyChat(me, line.Substring(1)); return; }
            if (line.StartsWith("$") && line.Length > 1) { _s.GuildChat(me, line.Substring(1)); return; }
            _s.Chat(me, line);
        }

        public void Whisper(string target, string text)
        {
            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrEmpty(text)) return;
            LastWhisperTo = target;
            _s.Whisper(target, text);
            Line(target.StartsWith("#") ? UI.ChatKind.Channel : UI.ChatKind.Whisper, Lang.F("[to {0}] {1}", target, text), "ffb3ff");
            WhisperOut?.Invoke(target, text);
        }

        /// <summary>The last ZC_ADD_FRIENDS_LIST result (0 friends, 1 refused, 2 / 3 a list is full); -1 none yet (smoke).</summary>
        public int LastAddResult = -1;
        public void AddFriend(string name) { if (!string.IsNullOrWhiteSpace(name)) { _s.FriendAdd(name.Trim()); _chat(Lang.F("Friend request sent to {0}.", name.Trim()), "c8cbd6"); } }
        /// <summary>The official /hi: the same whisper to every friend who is online (msgstringtable 918). An empty text sends "Hi!" (Loki's own
        /// default: no official default text is on record; roBrowserLegacy, a reimplementation, sends "(Hi) *^_^*").</summary>
        public int SayHi(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) text = Lang.T("Hi!");
            int n = 0;
            foreach (var f in Friends) if (f.Online) { LastWhisperTo = f.Name; _s.Whisper(f.Name, text); n++; }
            Line(UI.ChatKind.Whisper, n > 0 ? (n == 1 ? Lang.F("[to 1 friend] {0}", text) : Lang.F("[to {0} friends] {1}", n, text)) : Lang.T("None of your friends is online."), n > 0 ? "ffb3ff" : "ffb070");
            return n;
        }
        public void RemoveFriend(Friend f) { if (f != null) _s.FriendRemove(f.Aid, f.Cid); }
        public void AnswerRequest(bool accept)
        {
            if (PendingRequest == null) return;
            var p = PendingRequest.Value; PendingRequest = null;
            _s.FriendReply(p.aid, p.cid, accept);
            _chat(accept ? Lang.F("You accepted {0}.", p.name) : Lang.F("You declined {0}.", p.name), "c8cbd6");
            Changed?.Invoke();
        }
    }
}
