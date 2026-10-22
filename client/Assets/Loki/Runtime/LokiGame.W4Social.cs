using System.Collections.Generic;
using System.Linq;
using Loki.Game;
using Loki.Net;
using Loki.Sprites;
using Loki.UI;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 4, w4social: people and messages (brief design notes, "w4social"). Every packet and rule was read in rAthena
    /// e985006 (C:\rAthena, PACKETVER 20211103) first; file:line references are in the code of each system:
    ///   the chat log with tabs (UI/ChatPanel.cs, every line with its kind: Game/SocialHub.cs, Game/Modes.cs, Game/Clan.cs, Game/Channels.cs),
    ///   the official slash commands (Game/SlashCommands.cs, registered below with the packet each sends), the friend list's results and
    ///   the /loginout lines (Game/SocialHub.cs, UI/WristMenu.W4Social.cs), chat channels (Game/Channels.cs behind Net/IChatChannels.cs),
    ///   clans (Game/Clan.cs), PvP / GvG / duel map modes, the PvP counter and name colours (Game/PvpState.cs), duels through
    ///   Net/IDuelChannel.cs (the re-audit's §3 leak), guild emblems from the player's folder, over heads on siege maps and on guild
    ///   flags (Game/Guild.cs, UI/EmblemBanner.cs), and the server messages the client dropped: 0x00c2 (/who), 0x014a / 0x014b (manner),
    ///   0x0293 (Convex Mirror), 0x0af6 / 0x0af7 / 0x097e (rankings), 0x019a / 0x0210 / 0x01d6 (PvP).
    /// Smoke: LokiGame.W4SocialSmoke.cs (-lokiSmokeOnly w4social-host / w4social-guest).
    /// </summary>
    public sealed partial class LokiGame
    {
        public SlashCommands W4SocialSlash { get; } = new SlashCommands();
        public Channels W4SocialChannels { get; private set; }
        public Clan W4SocialClan { get; private set; }
        public PvpState W4SocialPvp { get; private set; }
        public PvpCounter W4SocialPvpCounter { get; private set; }
        public IDuelChannel W4SocialDuels { get; private set; }
        /// <summary>The official /notrade switch (msgstringtable 804-806).</summary>
        public bool W4SocialNoTrade { get; private set; }
        /// <summary>Smoke: every line the w4social arms printed (packet id, text).</summary>
        private readonly List<(int id, string text)> _w4socialSeen = new List<(int, string)>();

        private void BeginW4Social(LokiBootstrap boot)
        {
            // ---- the chat log: every line with its kind (the chat window's tabs)
            Social.ChatAs = W4SocialChatAs;
            if (Modes != null) Modes.ChatAs = W4SocialChatAs;
            Guild.ShowLogins = () => Social.ShowLogins;
            Trade.AutoDecline = () => W4SocialNoTrade;
            // ---- @commands the thin adapters send: a command this server doesn't let the player use comes back as the player's own public
            // chat line (atcommand.cpp:12086-12089 returns false, then clif_parse_GlobalMessage broadcasts it): the guard notices and says so
            Social.OwnLine += W4SocialOwnLine;
            // ---- duels (Net/IDuelChannel.cs): the state from 0x099b, the rest through the adapter
            var duels = new RathenaDuelChannel(W4SocialCommand);
            W4SocialDuels = duels;
            if (Modes != null)
            {
                Modes.Duels = duels;
                duels.Invited += who => { Modes.OnInvited(who); W4SocialChatAs(Lang.F("{0} challenges you to a duel — answer in the wrist menu's Modes tab.", who), "ffd060", ChatKind.System); };
                duels.Refused += m => { W4SocialChatAs(m, "ffb070", ChatKind.System); Hud?.Notify(m); }; // client wave 11: in front of the player
            }
            // ---- PvP / GvG / duel map modes (Game/PvpState.cs), fed by the 0x099b Guild.cs parses
            W4SocialPvp = new PvpState(Session, () => Session.AccountId, () => Session.SelectedChar?.CharId ?? 0u, W4SocialChatAs);
            Guild.MapPropertyChanged += () => { W4SocialPvp.SetProperty(Guild.MapType, Guild.MapFlags); Modes?.SetDuel(W4SocialPvp.Duel); W4SocialDecorateAll(); };
            W4SocialPvpCounter = PvpCounter.Create(boot.Rig.transform, boot.Rig.OffHand, boot.Rig.Head, W4SocialPvp, () => Hud.Wrist.ShownOnWrist);
            boot.Rig.HandsSwapped += () => W4SocialPvpCounter.SetHand(boot.Rig.OffHand);
            Entities.UnitAdded += u => { W4SocialDecorate(u); W4SocialEmblemFor(u); };
            Entities.GuildChanged += W4SocialEmblemFor; Guild.UnitInfo += W4SocialEmblemFor; // 0x0b47 (a flag given a guild by flagemblem, guild.cpp:1652-1657) comes this way
            Party.Changed += W4SocialDecorateAll; // after DecoratePartyUnits (subscribed first), which puts every non-member back to white
            if (Modes != null) Modes.Changed += W4SocialDecorateAll;
            Guild.EmblemChanged += gid => { foreach (var u in Entities.Units.Values) if (u != null && u.GuildId == gid) W4SocialEmblemFor(u); };
            Guild.MapPropertyChanged += () => { foreach (var u in Entities.Units.Values) W4SocialEmblemFor(u); };
            // ---- chat channels (Net/IChatChannels.cs) and clans
            var channels = new RathenaChatChannels(W4SocialCommand, (ch, text) => Session.Whisper(ch, text));
            W4SocialChannels = new Channels(channels, ListWindow.Create(boot.Rig.transform, "ChannelWindow"), () => boot.Rig.Head, W4SocialChatAs, t => Hud.Wrist.PrefillChat?.Invoke(t));
            W4SocialChannels.Window.RequestKeyboard = f => { Keyboard.Attach(f); Keyboard.Show(); };
            W4SocialClan = new Clan(Session, () => Session.SelectedChar?.Name ?? "", W4SocialChatAs);
            Social.ServerLine += W4SocialServerLine;
            // ---- the minimap: the Convex Mirror's boss on top of the viewpoint marks
            if (MiniMap != null) { var marks = MiniMap.MarkCells; MiniMap.MarkCells = () => W4SocialMarks(marks); }
            W4SocialRegisterCommands();
            // ---- the wrist menu: the Social tab's party invitation, the Community tab's channels and clan
            Hud.Wrist.W4SocialPartyInvite = n => Party.InviteByName(n);
            Hud.Wrist.W4SocialCanInvite = () => Party.InParty && Party.IsLeader;
            Social.IsGuildMate = n => Guild.InGuild && Guild.Members.Exists(m => m.Name == n);
            W4SocialChannels.Changed += () => Hud.Wrist.MarkDirty(); W4SocialClan.Changed += () => Hud.Wrist.MarkDirty();
            W4SocialPvp.Changed += () => Hud.Wrist.MarkDirty();
        }

        /// <summary>The Community tab's lines about channels and the clan.</summary>
        private string W4SocialCommunityInfo()
        {
            string s = "";
            if (W4SocialChannels != null) s += W4SocialChannels.Joined.Count > 0 ? "\n" + Lang.F("Channels: {0}", string.Join(", ", W4SocialChannels.Joined)) + "\n" : "\n" + Lang.T("No chat channel yet — \"Chat channels\" joins one.") + "\n";
            if (W4SocialClan != null && W4SocialClan.InClan) s += W4SocialClan.Summary() + "\n";
            return s;
        }

        /// <summary>A chat line of a kind (the chat window's tabs).</summary>
        private void W4SocialChatAs(string text, string color, ChatKind kind)
        {
            Debug.Log($"[Chat] ({kind}) {text}");
            Hud?.Chat.Add(text, color, kind);
        }

        private void W4SocialSays(int id, string text, string color, ChatKind kind = ChatKind.System, bool toast = false)
        {
            _w4socialSeen.Add((id, text));
            Debug.Log($"[W4Social] 0x{id:x4}: {text}");
            W4SocialChatAs(text, color, kind);
            if (toast) Hud?.Wrist.Toast(text, 6f);
        }

        // ------------------------------------------------------------------ the @command guard
        private readonly List<(string line, float at)> _w4CommandsSent = new List<(string, float)>();
        /// <summary>Commands the server let us know we may not use (their first word), and the last refusal (smoke).</summary>
        private readonly HashSet<string> _w4CommandsRefused = new HashSet<string>();
        public string W4SocialLastRefusal { get; private set; } = "";

        /// <summary>Sends an @command for an adapter (duels, channels) and remembers it for 3 s.</summary>
        private void W4SocialCommand(string line)
        {
            if (Session == null || Session.SelectedChar == null) return;
            string word = line.Split(' ')[0];
            if (_w4CommandsRefused.Contains(word)) { string m = Lang.F("This server does not let you use {0} (it answered it as public chat before).", word); if (W4SocialIsDuelWord(word)) (W4SocialDuels as RathenaDuelChannel)?.CommandRefused(m); else W4SocialChatAs(m, "ffb070", ChatKind.System); W4SocialRefusedFallback(line); return; }
            _w4CommandsSent.RemoveAll(x => Time.time - x.at > 3f);
            _w4CommandsSent.Add((line, Time.time));
            Debug.Log("[W4Social] command > " + line);
            Session.Chat(Session.SelectedChar.Name, line);
        }
        private void W4SocialOwnLine(string line)
        {
            var (_, text) = SocialHub.Split(line);
            int i = _w4CommandsSent.FindIndex(x => x.line == text && Time.time - x.at <= 3f);
            if (i < 0) return;
            _w4CommandsSent.RemoveAt(i);
            string word = text.Split(' ')[0]; _w4CommandsRefused.Add(word);
            W4SocialLastRefusal = word;
            string m = Lang.F("This server does not let you use {0}: it went out as public chat. (A server admin can allow it in rAthena's conf/groups.yml.)", word);
            Debug.Log("[W4Social] refused: " + word);
            if (W4SocialIsDuelWord(word)) (W4SocialDuels as RathenaDuelChannel)?.CommandRefused(m); // the duel adapter tells the player (Modes' toast)
            else { W4SocialChatAs(m, "ffb070", ChatKind.System); Hud?.Wrist.Toast(m); }
            W4SocialRefusedFallback(text);
        }
        private static bool W4SocialIsDuelWord(string w) => w == "@duel" || w == "@invite" || w == "@accept" || w == "@reject" || w == "@leave";
        /// <summary>What still works without the command: a channel without a password is joined by saying a line in it (the whisper to
        /// "#name", clif.cpp:11918-11926 — stock rAthena gives @join to no player group, conf/groups.yml).</summary>
        private void W4SocialRefusedFallback(string line)
        {
            var parts = line.Split(' ');
            if (parts[0] == "@join" && parts.Length >= 2 && parts.Length < 3) W4SocialChannels?.JoinBySaying(parts[1]);
        }

        /// <summary>A server line addressed to the player: the adapters read what only a text carries (Net/IDuelChannel.cs, Net/IChatChannels.cs).</summary>
        private void W4SocialServerLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (text.StartsWith("blue")) text = text.Substring(4);
            W4SocialDuels?.OnServerLine(text);
            W4SocialChannels?.Adapter.OnServerLine(text);
        }

        // ------------------------------------------------------------------ packets
        private bool HandleW4Social(ushort id, byte[] b, int o, int n)
        {
            if (W4SocialChannels != null && W4SocialChannels.Handle(id, b, o, n)) return true; // 0x02c1 with id 0: a channel line
            if (W4SocialClan != null && W4SocialClan.Handle(id, b, o, n)) return true;
            if (W4SocialPvp != null && W4SocialPvp.Handle(id, b, o, n)) return true;
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x009a: // ZC_BROADCAST (peek, the main switch prints it): a duel invitation reaches the invitee this way (duel.cpp:201-202)
                { r.Skip(4); W4SocialServerLine(r.Text(n - 4)); return false; }
                case 0x02c1: // ZC_NPC_CHAT to ourselves (clif_messagecolor SELF: the unit id is ours) — peek, the main switch prints it
                { r.Skip(4); uint gid = r.U32(); if (gid == Session.AccountId) { r.Skip(4); W4SocialServerLine(r.Text(n - 12)); } return false; }
                case 0x00c2: // ZC_USER_COUNT count.L — the answer to /who (clif_user_count clif.cpp:11675-11683): msgstringtable 178 "There are %d Players Currently Connected."
                {
                    r.Skip(2); int count = r.S32(); W4SocialUsers = count;
                    W4SocialSays(id, ClientText.Fill(ClientText.Msg(178), count.ToString()), "9ad1ff");
                    return true;
                }
                case 0x014a: // ZC_ACK_GIVE_MANNER_POINT result.L (clif_manner_message clif.cpp:9577-9597; @mute sends 0 to the GM, 5 to the player, atcommand.cpp:7932-7933)
                {
                    r.Skip(2); int res = r.S32(); W4SocialManner = res;
                    // 0 = msgstringtable 854 (the same text rAthena documents); 1 / 2 = MP_FAILURE_EXHAUST / _ALREADY_GIVING: 385 / 386 carry those
                    // meanings (a choice by meaning, no official source); 3 / 4: rAthena's documented English (no line in this client's table);
                    // 5 names the giver, which the follow-up 0x014b carries: shown there
                    string m = res == 0 ? ClientText.Msg(854) : res == 1 ? ClientText.Msg(385) : res == 2 ? ClientText.Msg(386)
                             : res == 3 ? Lang.T("Chat Block has been applied by GM due to your ill-mannerous action.") : res == 4 ? Lang.T("Automated Chat Block has been applied due to Anti-Spam System.") : null;
                    if (m != null) W4SocialSays(id, m, "ffd060");
                    return true;
                }
                case 0x014b: // ZC_NOTIFY_MANNER_POINT_GIVEN type.B name[24]: who changed our manner (clif_GM_silence clif.cpp:9604-9611): 1 muted, 0 unmuted
                {
                    r.Skip(2); int type = r.U8(); string who = r.Str(24);
                    W4SocialSays(id, type == 1 ? Lang.F("{0} has blocked your chat.", who) : Lang.F("You got a good point from {0}.", who), type == 1 ? "ff8a7a" : "a8e6a3", ChatKind.System, true); // the second is rAthena's text for 0x014a type 5 (clif.cpp:9583)
                    return true;
                }
                case 0x0293: // ZC_BOSS_INFO type.B x.L y.L minH.W minM.W maxH.W maxM.W name[51] (clif_bossmapinfo clif.cpp:17660-17700, the Convex Mirror's SC_BOSSMAPINFO)
                {
                    r.Skip(2); int type = r.U8(); int x = (int)r.U32(), y = (int)r.U32(); int h = r.U16(), mi = r.U16(); r.Skip(4); string name = r.Str(51);
                    W4SocialBoss = type == 1 || type == 2 ? (name, new Vector2Int(x, y)) : ((string, Vector2Int)?)null;
                    // e_bossmap_info (clif.hpp:175-180): 0 none here = msgstringtable 1223, 2 alive (first) = 1225, 3 dead = 1224 (%02d hours, %02d minutes);
                    // 1 alive = a position update, no line
                    if (type == 0) W4SocialSays(id, ClientText.Msg(1223), "ffd060");
                    else if (type == 2) W4SocialSays(id, ClientText.Fill(ClientText.Msg(1225), name), "ffd060");
                    else if (type == 3) W4SocialSays(id, ClientText.Fill(ClientText.Fill(ClientText.Fill(ClientText.Msg(1224), name), h.ToString("00")), mi.ToString("00")), "ffd060");
                    return true;
                }
                case 0x0af6: // ZC_ACK_RANKING2 type.W CIDs[10].L points[10].L mypoints.L (clif_ranklist clif.cpp:20097-20146; packets.hpp:1640-1647)
                {
                    r.Skip(2); int type = r.U16(); var cids = new uint[10]; var pts = new int[10];
                    for (int i = 0; i < 10; i++) cids[i] = r.U32();
                    for (int i = 0; i < 10; i++) pts[i] = r.S32();
                    int mine = r.S32();
                    // names come from CZ_REQNAME_BYGID2 0x0369 cid.L → ZC_ACK_REQNAME_BYGID 0x0af7 (clif_parse_SolveCharName clif.cpp:13553-13560)
                    foreach (var c in cids) if (c != 0 && !_w4Names.ContainsKey(c)) Session.SendPacket(new PacketWriter(0x0369).U32(c).ToArray());
                    StartCoroutine(W4SocialPrintRanking(type, cids, pts, mine));
                    return true;
                }
                case 0x0af7: // ZC_ACK_REQNAME_BYGID flag.W cid.L name[24] (packets.hpp:1735-1741)
                {
                    r.Skip(2); r.U16(); uint cid = r.U32(); string name = r.Str(24); _w4Names[cid] = name;
                    return true;
                }
                case 0x097e: // ZC_UPDATE_RANKING_POINT type.W points.L total.L (clif_update_rankingpoint clif.cpp:20295-20305)
                {
                    // msgstringtable 901 Blacksmith, 902 Alchemist, 926 Taekwon, 953 Slayer: "[Point] You have been rewarded with %d … rank points. Your point total is %d."
                    r.Skip(2); int type = r.U16(); int pts = r.S32(), total = r.S32();
                    int msg = type == 0 ? 901 : type == 1 ? 902 : type == 2 ? 926 : 953;
                    W4SocialSays(id, ClientText.Fill(ClientText.Fill(ClientText.Msg(msg), pts.ToString()), total.ToString()), "ffd060");
                    return true;
                }
            }
            return false;
        }
        /// <summary>Smoke: the last /who count, manner result, boss seen by the Convex Mirror.</summary>
        public int W4SocialUsers { get; private set; } = -1;
        public int W4SocialManner { get; private set; } = -1;
        public (string name, Vector2Int cell)? W4SocialBoss { get; private set; }
        private readonly Dictionary<uint, string> _w4Names = new Dictionary<uint, string>();
        /// <summary>Smoke: the last ranking printed (type, lines).</summary>
        public (int type, List<string> lines) W4SocialLastRanking { get; private set; } = (-1, new List<string>());

        private System.Collections.IEnumerator W4SocialPrintRanking(int type, uint[] cids, int[] pts, int mine)
        {
            float until = Time.time + 3f;
            while (Time.time < until && cids.Any(c => c != 0 && !_w4Names.ContainsKey(c))) yield return null;
            // msgstringtable 2382 "Ranking board", 2386-2388 BlackSmith / Alchemist / Taekwon; the killer list (type 3) is empty in rAthena
            // ("Currently not supported", clif.cpp:20118-20122): its title is the /pk help's
            string title = ClientText.Msg(2382) + " — " + (type == 0 ? ClientText.Msg(2386) : type == 1 ? ClientText.Msg(2387) : type == 2 ? ClientText.Msg(2388) : "PK");
            var lines = new List<string> { title };
            for (int i = 0; i < 10; i++) if (cids[i] != 0) lines.Add($"  {i + 1}. {(_w4Names.TryGetValue(cids[i], out var nm) ? nm : "#" + cids[i])}  {pts[i]:N0}");
            if (lines.Count == 1) lines.Add("  " + Lang.T("(nobody is ranked yet)"));
            if (mine > 0) lines.Add("  " + Lang.F("your points: {0:N0}", mine));
            W4SocialLastRanking = (type, lines);
            foreach (var l in lines) W4SocialSays(0x0af6, l, "ffe070");
        }

        private IEnumerable<(Vector2 cell, Color color)> W4SocialMarks(System.Func<IEnumerable<(Vector2, Color)>> others)
        {
            if (others != null) foreach (var m in others()) yield return m;
            if (W4SocialBoss.HasValue) yield return (W4SocialBoss.Value.cell, new Color(1f, 0.3f, 0.25f));
        }

        // ------------------------------------------------------------------ name colours: who can be attacked here
        private static readonly Color W4HostileName = new Color(1f, 0.45f, 0.4f);
        private readonly HashSet<uint> _w4Reddened = new HashSet<uint>();
        /// <summary>
        /// The VR stand-in for the official client's attack cursor (0x099b flag bit 0 "attack non-party players", bit 1 "non-guild",
        /// clif.cpp:6890-6891): a red name. PvP map: everyone outside the party. Duel: the players we know are in our duel (the one who
        /// invited us, the ones we invited — rAthena sends no member list, battle.cpp:8081-8086 refuses the rest). WoE: IsHostilePlayer
        /// (other guilds, not allies). A battlefield's camps are coloured by Modes (0x02dd).
        /// </summary>
        private void W4SocialDecorate(Unit u)
        {
            if (u == null || u.IsSelf || !u.IsPlayerType || u.Overhead == null || W4SocialPvp == null) return;
            if (Modes != null && Modes.OnBattlefield) return;
            bool party = Party != null && Party.Members.ContainsKey(u.Id);
            bool hostile = W4SocialPvp.PvpMap ? !party
                         : W4SocialPvp.Duel ? Modes != null && Modes.DuelNames.Contains(u.Name)
                         : W4SocialPvp.Type == 3 && IsHostilePlayer(u);
            if (hostile) { u.Overhead.SetNameColor(W4HostileName); _w4Reddened.Add(u.Id); }
            else if (_w4Reddened.Remove(u.Id)) u.Overhead.SetNameColor(party ? PartyNameColor : Color.white);
        }
        private void W4SocialDecorateAll() { if (Entities == null) return; foreach (var u in Entities.Units.Values) W4SocialDecorate(u); }

        // ------------------------------------------------------------------ emblems on flags and over heads on siege maps
        private const int GuildFlagClass = 722; // JT_GUILD_FLAG (npc.hpp:657, script_constants.hpp:7892)
        private void W4SocialEmblemFor(Unit u)
        {
            if (u == null || Guild == null) return;
            bool flag = u.Job == GuildFlagClass && !u.IsPlayerType && u.Kind != Unit.UnitKind.Monster;
            bool head = !flag && u.IsPlayerType && !u.IsSelf && W4SocialPvp != null && W4SocialPvp.Siege;
            var sprite = (flag || head) && u.GuildId != 0 ? Guild.EmblemOf(u.GuildId, u.GuildEmblem, false) : null;
            var have = u.GetComponent<EmblemBanner>();
            if (sprite == null && have == null) return;
            var b = have ?? EmblemBanner.Attach(u.transform, flag ? 1.7f : 3.4f, flag ? 72f : 44f);
            b.HeightCells = flag ? 1.7f : 3.4f;
            b.Set(sprite);
        }

        // ------------------------------------------------------------------ console
        /// <summary>"#channel text", then the slash commands (before /guild and /trade; the emote commands come first).</summary>
        private bool TryW4SocialCommand(string line)
        {
            if (W4SocialChannels != null && W4SocialChannels.TryConsole(line)) return true;
            return W4SocialSlash.TryRun(line);
        }
        /// <summary>A "/…" line no handler knew: the official client answers msgstringtable 95 "Invalid Command" and sends nothing (the
        /// whisper prefixes /w Name, /whisper, /r are SocialHub's and pass).</summary>
        private bool W4SocialUnknownSlash(string line)
        {
            if (string.IsNullOrEmpty(line) || line[0] != '/' || line.Length < 2) return false;
            string low = line.ToLowerInvariant();
            if (low.StartsWith("/w ") || low.StartsWith("/whisper ") || low.StartsWith("/r ")) return false;
            W4SocialChatAs(ClientText.Msg(95) + "  (/h: " + ClientText.Msg(22) + ")", "ffb070", ChatKind.System);
            Debug.Log("[W4Social] unknown slash command " + line);
            return true;
        }

        private void OnMapChangeW4Social(string map)
        {
            W4SocialPvp?.OnMapChange(); W4SocialChannels?.OnMapChange();
            W4SocialBoss = null; // SC_BOSSMAPINFO ends on a warp (RemoveOnMapWarp, db/re/status.yml:2745)
            _w4Reddened.Clear();
            uint cid = Session?.SelectedChar?.CharId ?? 0;
            if (cid != _w4Cid) { _w4Cid = cid; W4SocialChannels?.ResetForCharacter(); W4SocialClan?.ResetForCharacter(); }
        }
        private uint _w4Cid;
    }
}
