using System.Collections.Generic;
using Loki.Game;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 3, w3social (the re-audit's §6 "next tier"): the world map window and the minimap's zoom / "World" button, the
    /// wrist menu's 12-member party page and full emote palette (UI/WristMenu.W3Social.cs), emote chat commands, and the server
    /// messages the client used to drop — each read in rAthena e985006 (clif.cpp) first:
    ///   0x0215 ZC_SKILLMSG     Gospel's party buffs (clif_gospel_info clif.cpp:10170-10194, skill.cpp:7176-7256)
    ///   0x07fd ZC_BROADCASTING_SPECIAL_ITEM_OBTAIN   a box / MVP / monster item with the announce flag (clif.cpp:21349-21409)
    ///   0x0b67 ZC_ITEM_PICKUP_PARTY  a party member on this map picked up a weapon / armour / card (clif.cpp:18933-18961, party.cpp:1359-1360)
    ///   0x010a ZC_MVP_GETTING_ITEM, 0x010c ZC_MVP, 0x010d ZC_THROW_MVPITEM   the MVP reward (clif.cpp:8569-8625, mob.cpp:3437-3533)
    ///   0x01b0 ZC_NPCSPRITE_CHANGE   an NPC / monster / homunculus changes class (EntityManager.W3SocialReclass)
    /// Smoke: LokiGame.W3SocialSmoke.cs (-lokiSmokeOnly w3social-host / w3social-guest).
    /// </summary>
    public sealed partial class LokiGame
    {
        public WorldMapWindow W3SocialWorldMap { get; private set; }
        /// <summary>Smoke: every message these arms printed, in order (packet id, text).</summary>
        private readonly List<(int id, string text)> _w3socialSeen = new List<(int, string)>();

        private void BeginW3Social(LokiBootstrap boot)
        {
            W3SocialWorldMap = WorldMapWindow.Create(boot.Rig.transform, boot.Rig.Head, () => boot.Rig.PointedWorld);
            W3SocialWorldMap.PartyMaps = W3SocialPartyMaps;
            if (Party != null) Party.Changed += () => W3SocialWorldMap.Refresh();
            Hud.Wrist.OpenWorldMap = () => W3SocialWorldMap.Toggle();
            if (MiniMap != null) MiniMap.OpenWorldMap = () => W3SocialWorldMap.Toggle();
            Hud.Wrist.EmoteIcon = id => Emotes.IconOf(id);
            Hud.Wrist.EmoteWait = () => Emotes.Wait;
        }

        /// <summary>The other party members and their maps, for the world map.</summary>
        private IEnumerable<(string name, string map, bool online)> W3SocialPartyMaps()
        {
            if (Party == null || !Party.InParty || Session == null) yield break;
            foreach (var m in Party.Members.Values) if (m.Aid != Session.AccountId && m.Map.Length > 0) yield return (m.Name, m.Map, m.Online);
        }

        private void OnMapChangeW3Social(string map) { W3SocialWorldMap?.SetHere(map); }

        /// <summary>A chat line that is an emote command ("/lv", "/!" … from emotionlist.lub, Game/Emotes.cs) sends that emote, as the
        /// official client's chat box does.</summary>
        private bool TryW3SocialCommand(string line)
        {
            int e = Emotes.ByCommand(line);
            if (e < 0 || Emotes == null) return false;
            Debug.Log($"[W3Social] chat command {line.Trim()} → emote {e}");
            Emotes.Send(e);
            return true;
        }

        private void W3SocialChat(int id, string text, string color, bool toast = false)
        {
            _w3socialSeen.Add((id, text));
            Debug.Log($"[W3Social] 0x{id:x4}: {text}");
            Chat(text, color);
            if (toast) Hud?.Wrist.Toast(text, 6f);
        }

        /// <summary>ZC_SKILLMSG's message ids → the client's msgstringtable lines (0-based, data/msgstrings.tsv). rAthena documents the
        /// ids (clif.cpp:10170-10183: 0x15 end all negative status … 0x20 HIT/Flee +50; 0x1a / 0x1b are never sent: Gospel's Blessing and
        /// Increase AGI cases send nothing, skill.cpp:7219-7224) and the table holds those ten texts in the same order at 889-898
        /// ("All abnormal status effects have been removed." … "Your Accuracy and Flee Rate will be increased for the next minute.").
        /// Anything else shows nothing ("? = nothing"). 0x28 (Full Strip failed) is documented but has no sender in e985006.</summary>
        public static int W3SocialSkillMsgLine(int msgId) => msgId >= 0x15 && msgId <= 0x19 ? 889 + (msgId - 0x15) : msgId >= 0x1c && msgId <= 0x20 ? 894 + (msgId - 0x1c) : -1;

        /// <summary>An item's name as the inventory shows it (the client's own name, then the English one: GameDb.Item.Display), untagged for the chat.</summary>
        private static string W3SocialItemName(int id) => UiKit.StripTags(GameDb.GetItem(id).Display);

        private bool HandleW3Social(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x0215: // ZC_SKILLMSG msgId.L (packets.hpp:2027-2031), to the buffed party member alone (SELF)
                {
                    r.Skip(2); int msg = r.S32();
                    int line = W3SocialSkillMsgLine(msg);
                    if (line >= 0) W3SocialChat(id, ClientText.Msg(line), "cdcdff");
                    else Debug.Log($"[W3Social] 0x0215 skill message 0x{msg:x2}: nothing to show");
                    return true;
                }
                case 0x07fd:
                {
                    // ZC_BROADCASTING_SPECIAL_ITEM_OBTAIN to every client (clif.cpp:21355-21409); for PACKETVER 20211103 (RE 20211103):
                    //   type 0 box:          len.W type.B itemId.L nameLen.B name[24] boxLen.B boxItemId.L   (packets_struct.hpp:1043-1061)
                    //   type 1 monster, 2 NPC: len.W type.B itemId.L nameLen.B name[24] srcLen.B src[srcLen] (packets_struct.hpp:1065-1078;
                    //                        PacketLength = 12 + 24 + srcLen, one byte past the fields, clif.cpp:21401-21405)
                    // The name may be masked with '*' (broadcast_hide_name). The client's lines, by their placeholders: type 0 / 1
                    // "[%s] gave [%s] '%s'." (msgstringtable 1628: player, source, item — the Portuguese column reads "[%s] abriu [%s] e
                    // recebeu o item [%s]."), type 2 "Congratulation %s,  Acquired '%s' !" (1868: player, item). rAthena's comment
                    // (clif.cpp:21352-21353) says MsgStringTable[1629] / [1870]: 1-based for the first; this table's 1869 (1-based) is the
                    // second, so it is chosen by its text and placeholders, not by that number.
                    r.Skip(4); int type = r.U8(); int item = (int)r.U32();
                    int nl = r.U8(); string who = r.Str(Mathf.Min(nl, r.Remaining));
                    int sl = r.Remaining > 0 ? r.U8() : 0;
                    string src;
                    if (type == 0) { int box = sl >= 4 && r.Remaining >= 4 ? (int)r.U32() : sl >= 2 && r.Remaining >= 2 ? r.U16() : 0; src = box != 0 ? W3SocialItemName(box) : "?"; }
                    else src = sl > 0 ? r.Str(Mathf.Min(sl, r.Remaining)) : "";
                    string itemName = W3SocialItemName(item);
                    string text = type == 2 ? ClientText.Fill(ClientText.Fill(ClientText.Msg(1868), who), itemName)
                                            : ClientText.Fill(ClientText.Fill(ClientText.Fill(ClientText.Msg(1628), who), src), itemName);
                    W3SocialChat(id, text, "ffe066", true);
                    return true;
                }
                case 0x0b67:
                {
                    // ZC_ITEM_PICKUP_PARTY AID.L itemId.L identified.B damaged.B card[4].L location.W itemType.B refine.B grade.B
                    // (packets_struct.hpp:2674-2690): sent to the party members on the picker's map but not the picker (PARTY_SAMEMAP_WOS)
                    // when show_party_share_picker is on and the item's type is in show_picker.item_type (weapons, armour, cards by default,
                    // conf/battle/party.conf:27-34). The line: msgstringtable 1276 "%s has acquired %s." — chosen from the table (it sits
                    // next to the chat option 1284 "Party Member's Obtained Item Message Display"); no official source was available to
                    // confirm it, and roBrowserLegacy (a reimplementation) parses the packet without showing anything.
                    r.Skip(2); uint aid = r.U32(); int item = (int)r.U32(); bool ident = r.U8() != 0, damaged = r.U8() != 0;
                    var cards = new int[4]; for (int i = 0; i < 4; i++) cards[i] = (int)r.U32();
                    uint loc = r.U16(); int itype = r.U8(); int refine = r.U8(); int grade = r.U8();
                    string who = Party != null && Party.Members.TryGetValue(aid, out var m) ? m.Name : Entities.Get(aid)?.Name ?? $"#{aid}";
                    var e = new Inventory.Entry { ItemId = item, Identified = ident, Damaged = damaged, Refine = refine, Grade = grade, Location = loc, Type = itype, Cards = cards };
                    string name = UiKit.StripTags(Inventory.FullName(e));
                    W3SocialChat(id, ClientText.Fill(ClientText.Fill(ClientText.Msg(1276), who), name), "a8e6a3");
                    return true;
                }
                case 0x010a:
                {
                    // ZC_MVP_GETTING_ITEM itemId.L (packets_struct.hpp:2439-2446), SELF: msgstringtable 143 "Congratulations! You are the MVP! Your
                    // reward item is" + the item + 144 " !!" (the table splits the sentence there; roBrowserLegacy prints 143 and the item
                    // name on two lines, Entity.js:2686-2690)
                    r.Skip(2); int item = (int)r.U32();
                    W3SocialChat(id, ClientText.Msg(143).TrimEnd() + " " + W3SocialItemName(item) + ClientText.Msg(144), "9ad1ff", true);
                    return true;
                }
                case 0x010c:
                {
                    // ZC_MVP AID.L (packets.hpp:1773-1777), AREA: the MVP banner on that player — EF_MVP 68 (mvp.str, effect\st_mvp.wav),
                    // as roBrowserLegacy spams it (Entity.js:2670-2681; the effect table is its data)
                    r.Skip(2); uint aid = r.U32();
                    int parts = Effects != null ? Effects.Spam("68", aid, null, Time.time, -1f) : 0;
                    Debug.Log($"[W3Social] 0x010c MVP {aid}{(aid == Session.AccountId ? " (us)" : "")}: EF_MVP, {parts} drawable part(s)");
                    return true;
                }
                case 0x010d:
                {
                    // ZC_THROW_MVPITEM (packets.hpp:1785-1788): msgstringtable 147 "You are the MVP, but you can't take the reward because you are
                    // over your weight limit." rAthena e985006 defines clif_mvp_noitem (clif.cpp:8615-8625) but never calls it (an MVP reward
                    // that doesn't fit goes to the floor: mob.cpp:3520-3523): this arm is for servers that do send it.
                    W3SocialChat(id, ClientText.Msg(147), "ffb070", true);
                    return true;
                }
            }
            return false;
        }
    }
}
