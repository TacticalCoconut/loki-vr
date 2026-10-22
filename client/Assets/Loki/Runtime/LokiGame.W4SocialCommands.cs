using System.IO;
using Loki.Game;
using Loki.Net;
using Loki.Sprites;
using Loki.UI;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 4, w4social: the official client's slash commands (Game/SlashCommands.cs). Each one names the request rAthena parses
    /// for it (the parser for PACKETVER 20211103 from client/tools/packetgen/PacketTable.g.cs, "last registration wins") or says it is
    /// answered by the client alone. The emote commands (/lv, /! …, w3social) and /bt (wave 2), /guild and /trade (wave 1) keep their
    /// own handlers; the whisper prefixes /w Name, /whisper, /r stay SocialHub's.
    /// </summary>
    public sealed partial class LokiGame
    {
        private bool _w4Sitting;
        /// <summary>Smoke: the last direction pair sent by /doridori, /bangbang, /bingbing (server dir, head dir).</summary>
        public (int dir, int head) W4SocialLastDir { get; private set; } = (-1, -1);
        public string W4SocialLastSavedChat { get; private set; } = "";

        private void W4SocialRegisterCommands()
        {
            var S = W4SocialSlash;
            Entities.SelfSitting += s => _w4Sitting = s;
            void Say(string text, string color = "9ad1ff") => W4SocialChatAs(text, color, ChatKind.System);

            S.Add(33, null, a => { W4SocialHelp(); return true; }, "h");
            // CZ_REQ_USER_COUNT 0x00c1 → ZC_USER_COUNT 0x00c2 (clif_parse_HowManyConnections clif.cpp:11689-11696). "/w Name text" is a whisper.
            S.Add(34, null, a => { if (a.Length > 0) return false; Session.SendPacket(new PacketWriter(0x00c1).ToArray()); return true; }, "w", "who", "player");
            // the client alone: map name (mapnametable) and cell, as roBrowserLegacy prints it (ProcessCommand.js "where")
            S.Add(38, null, a =>
            {
                var c = Entities.Self != null ? Vector2Int.FloorToInt(Entities.Self.Cell) : Vector2Int.zero; string map = _boot.Map.Current?.Name ?? "";
                Say($"{ClientText.MapName(map)} ({map}) : {c.x}, {c.y}"); return true;
            }, "where");
            // CZ_REMEMBER_WARPPOINT 0x011d (clif_parse_RequestMemo)
            S.Add(211, null, a => { Session.RememberWarpPoint(); return true; }, "memo");
            // CZ_REQUEST_ACT 0x0437 action 2 sit / 3 stand (clif_parse_ActionRequest); "If you are sitting, you will stand instead" (msgstringtable 132-133)
            S.Add(132, null, a => { if (_w4Sitting) Session.Stand(); else Session.Sit(); return true; }, "sit");
            S.Add(133, null, a => { if (_w4Sitting) Session.Stand(); else Session.Sit(); return true; }, "stand");
            // chat rooms (Game/ChatRooms.cs): the create window; CZ_EXIT_ROOM 0x00e3 (clif_parse_ChatLeave)
            S.Add(134, null, a => { ChatRooms?.ShowCreate(); return true; }, "chat");
            S.Add(135, null, a => { if (ChatRooms != null && ChatRooms.InRoom) ChatRooms.Leave(); else Say(Lang.T("You aren't in a chat room."), "ffb070"); return true; }, "q");
            // CZ_REQ_EXCHANGE_ITEM 0x00e4 aid.L (Game/Trade.cs): the server needs the account id, so the player must be in view
            S.Add(136, null, a =>
            {
                string name = SlashCommands.Unquote(a); if (name.Length == 0) return false;
                var u = FindPlayer(name);
                if (u == null) Say(Lang.F("{0} isn't in view — you have to be next to someone to trade.", name), "ffb070"); else Trade?.Request(u.Id, u.Name);
                return true;
            }, "deal");
            // party (Game/Party.cs): CZ_MAKE_GROUP2 0x01e8, CZ_REQ_LEAVE_GROUP 0x0100, CZ_REQ_EXPEL_GROUP_MEMBER 0x0103 aid.L name[24] (clif.cpp
            // clif_parse_RemovePartyMember), CZ_PARTY_JOIN_REQ 0x02c4 name[24]
            S.Add(137, null, a => { string n = SlashCommands.Unquote(a); if (n.Length == 0) return false; Party.Create(n); return true; }, "organize");
            S.Add(138, null, a => { if (!Party.InParty) { Say(Lang.T("You aren't in a party."), "ffb070"); return true; } Party.Leave(); return true; }, "leave");
            S.Add(139, null, a =>
            {
                string n = SlashCommands.Unquote(a); if (n.Length == 0) return false;
                Party.Member m = null; foreach (var x in Party.Members.Values) if (x.Name == n) m = x;
                if (m == null) Say(Lang.F("{0} isn't in your party.", n), "ffb070"); else Party.Expel(m);
                return true;
            }, "expel");
            S.Add(-1, Lang.N("/invite ''Name'': invites a character to your party (roBrowserLegacy's command; the official help has no line for it)"), a => { string n = SlashCommands.Unquote(a); if (n.Length == 0) return false; Party.InviteByName(n); return true; }, "invite");
            // the whisper block list (Game/Whispers.cs): CZ_SETTING_WHISPER_PC 0x00cf name[24] type.B, CZ_SETTING_WHISPER_STATE 0x00d0,
            // CZ_REQ_WHISPER_LIST 0x00d3 (msgstringtable 42-46, 3422-3426)
            S.Add(3422, null, a => { string n = SlashCommands.Unquote(a); if (n.Length == 0) Whispers.ShowIgnore(); else Whispers.Block(n); return true; }, "ex", "block");
            S.Add(3423, null, a => { string n = SlashCommands.Unquote(a); if (n.Length == 0) return false; Whispers.Allow(n); return true; }, "in", "unblock");
            S.Add(3425, null, a => { Whispers.SetBlockAll(true); return true; }, "exall", "blockall");
            S.Add(3426, null, a => { Whispers.SetBlockAll(false); return true; }, "inall", "unblockall");
            // CZ_WHISPER 0x0096 to every friend online (Game/SocialHub.cs SayHi)
            S.Add(918, null, a => { Social.SayHi(a); return true; }, "hi");
            // CZ_REQ_PVPPOINT 0x020f cid.L aid.L → ZC_ACK_PVPPOINT 0x0210 (Game/PvpState.cs)
            S.Add(852, null, a => { W4SocialPvp.RequestInfo(); return true; }, "pvpinfo");
            // CZ_REQ_RANKING 0x097c type.W (clif_parse_ranklist clif.cpp:20271-20291): 0 blacksmith, 1 alchemist, 2 taekwon, 3 pk (e_rank, mmo.hpp:1150-1155)
            S.Add(923, null, a => { W4SocialRanking(0); return true; }, "blacksmith");
            S.Add(924, null, a => { W4SocialRanking(1); return true; }, "alchemist");
            S.Add(-1, Lang.N("/taekwon: Shows top 10 Taekwon (roBrowserLegacy's command; the official help has no line for it)"), a => { W4SocialRanking(2); return true; }, "taekwon");
            S.Add(952, null, a => { W4SocialRanking(3); return true; }, "pk");
            // the client alone: the chat log to a text file in the player's folder (msgstringtable 312-313, 757-758)
            S.Add(313, null, a => { W4SocialSaveChat(); return true; }, "savechat", "report");
            // the client alone: friends' and guild-mates' login lines (msgstringtable 1043-1045)
            S.Add(1043, null, a => { Social.ShowLogins = !Social.ShowLogins; Say(ClientText.Msg(Social.ShowLogins ? 1044 : 1045)); return true; }, "loginout", "li");
            // /notrade: the client declines every trade offer with CZ_ACK_EXCHANGE_ITEM 0x00e6 type 4 (Game/Trade.cs AutoDecline; msgstringtable 804-806)
            S.Add(804, null, a => { W4SocialNoTrade = !W4SocialNoTrade; Say(ClientText.Msg(W4SocialNoTrade ? 805 : 806)); return true; }, "notrade", "nt");
            S.Add(784, null, a => { Hud?.Wrist.Show(WristMenu.Tab.Emotes); return true; }, "emotion");
            S.Add(1191, null, a => { Hud?.Wrist.Show(WristMenu.Tab.Quests); return true; }, "hunting");
            // CZ_CLAN_CHAT 0x098d (Game/Clan.cs)
            S.Add(-1, Lang.N("/cl text: says it to your clan (roBrowserLegacy's command; the official help has no line for it)"), a => { if (a.Length == 0) return false; W4SocialClan.Say(a); return true; }, "cl");
            // guild (Game/Guild.cs): CZ_REQ_JOIN_GUILD2 0x0916 name[24]; CZ_REQ_DISORGANIZE_GUILD 0x015d key[40] = the guild's name (clif_parse_GuildBreak)
            S.Add(-1, Lang.N("/guildinvite Name: invites a character to your guild (roBrowserLegacy's command)"), a => { string n = SlashCommands.Unquote(a); if (n.Length == 0) return false; Guild.InviteByName(n); return true; }, "guildinvite");
            S.Add(-1, Lang.N("/breakguild ''Guild Name'': disbands your guild (the master, with every other member gone; roBrowserLegacy's command)"), a =>
            {
                string n = SlashCommands.Unquote(a);
                if (!Guild.InGuild) Say(Lang.T("You are not in a guild."), "ffb070"); else if (n != Guild.Name) Say(Lang.F("Type your guild's name exactly: /breakguild {0}", Guild.Name), "ffb070"); else Guild.Disband();
                return true;
            }, "breakguild");
            // CZ_CHANGE_DIRECTION2 0x0361 headDir.W dir.B (clif_parse_ChangeDir): /doridori shakes the head (1 / 2), /bangbang turns the body
            // clockwise (server direction − 1: rAthena's dirs count counter-clockwise from north, unit.cpp:70-71 dirx/diry), /bingbing the other way
            // (msgstringtable 724, 727, 728). Sitting Super Novices and Taekwon / Star Gladiator / Soul Linker get the doridori regen bonus
            // with CZ_DORIDORI 0x01e7 (clif_parse_NoviceDoriDori): roBrowserLegacy (a reimplementation) sends it after five shakes within
            // 1.5-3 s; no official rule is on record, so Loki does the same.
            S.Add(724, null, a => { W4SocialDoriDori(); return true; }, "doridori");
            S.Add(728, null, a => { W4SocialTurn(-1); return true; }, "bangbang");
            S.Add(727, null, a => { W4SocialTurn(+1); return true; }, "bingbing");
            // CZ_LESSEFFECT 0x021d state.L (clif_parse_LessEffect: 0 full, 1 reduced) + the Comfort "Skill effects" level (all / quiet);
            // msgstringtable 23 "Effects On" / 24 "Effects Off"
            S.Add(37, null, a =>
            {
                bool off = VR.Comfort.EffectsLevel != 2;
                VR.Comfort.EffectsLevel = off ? 2 : 0; VR.Comfort.Save();
                Session.SendPacket(new PacketWriter(0x021d).S32(off ? 1 : 0).ToArray());
                Say(ClientText.Msg(off ? 24 : 23)); return true;
            }, "effect");
            // /navi (msgstringtable 2412): navigation is w4world's (LokiGame.W4World.cs TryW4WorldCommand); listed here for /help,
            // handed over with its arguments (merge of client wave 4: the w4social stub said "not in this build yet")
            S.Add(2412, null, a => TryW4WorldCommand(string.IsNullOrWhiteSpace(a) ? "/navi" : "/navi " + a.Trim()), "navi", "navigation");
        }

        private void W4SocialHelp()
        {
            W4SocialChatAs(ClientText.Msg(22), "ffe070", ChatKind.System);
            foreach (var c in W4SocialSlash.All) W4SocialChatAs(SlashCommands.HelpOf(c), "c8cbd6", ChatKind.System);
            W4SocialChatAs(Lang.T("%text: party chat · $text: guild chat · #channel text: a chat channel · /w Name text: whisper · /r text: answer · @command: server commands"), "c8cbd6", ChatKind.System);
        }

        private void W4SocialRanking(int type) { Session.SendPacket(new PacketWriter(0x097c).U16((ushort)type).ToArray()); Debug.Log($"[W4Social] ranking {type}"); }

        private void W4SocialSaveChat()
        {
            string dir = Path.Combine(Loki.World.UserSettings.Dir, "Chat");
            string path = Path.Combine(dir, $"chat_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt");
            string err = Hud?.Chat.SaveTo(path);
            if (err == null) { W4SocialLastSavedChat = path; W4SocialChatAs(Lang.F("Chat log saved: {0}", path), "a8e6a3", ChatKind.System); }
            else W4SocialChatAs(Lang.F("The chat log could not be saved: {0}", err), "ff7a6a", ChatKind.System);
        }

        private int _w4Head = 1; private readonly float[] _w4Dori = new float[5];
        private void W4SocialDoriDori()
        {
            var self = Entities?.Self; if (self == null) return;
            _w4Head = _w4Head == 1 ? 2 : 1;
            int dir = Unit.ToServerDir(self.Sprite.Direction);
            Session.SendPacket(new PacketWriter(0x0361).U16((ushort)_w4Head).U8((byte)dir).ToArray());
            self.Sprite.HeadDir = _w4Head; W4SocialLastDir = (dir, _w4Head);
            if (!_w4Sitting) return;
            System.Array.Copy(_w4Dori, 1, _w4Dori, 0, 4); _w4Dori[4] = Time.time;
            float span = _w4Dori[4] - _w4Dori[0];
            if (_w4Dori[0] > 0 && span > 1.5f && span < 3f) { Session.SendPacket(new PacketWriter(0x01e7).ToArray()); System.Array.Clear(_w4Dori, 0, 5); Debug.Log("[W4Social] doridori bonus asked (0x01e7)"); }
        }
        private void W4SocialTurn(int step)
        {
            var self = Entities?.Self; if (self == null) return;
            int dir = (Unit.ToServerDir(self.Sprite.Direction) + step + 8) & 7;
            Session.SendPacket(new PacketWriter(0x0361).U16(0).U8((byte)dir).ToArray());
            self.Sprite.Direction = Unit.FromServerDir(dir); self.Sprite.HeadDir = 0; W4SocialLastDir = (dir, 0);
        }
    }
}
