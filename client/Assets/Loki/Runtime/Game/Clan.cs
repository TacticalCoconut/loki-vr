using System;
using System.Collections.Generic;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Clans (client wave 4, w4social; rAthena e985006, PACKETVER 20211103). A character joins a clan through an NPC (script
    /// clan_join, npc/re/other/clans.txt); the client then gets:
    ///   ZC_CLANINFO 0x098a len.W clanId.L name[24] master[24] map[16] allyCount.B antagonistCount.B {name[24]}* allies then antagonists
    ///     (clif_clan_basicinfo clif.cpp:20668-20701, packets_struct.hpp:2094-2104; at login and on joining),
    ///   ZC_NOTIFY_CLAN_CONNECTINFO 0x0988 online.W total.W (clif_clan_onlinecount clif.cpp:20707-20723, to the whole clan),
    ///   ZC_ACK_CLAN_LEAVE 0x0989 (clif_clan_leave clif.cpp:20729-20737),
    ///   ZC_NOTIFY_CLAN_CHAT 0x098e len.W memberName[24] message — the name field is left empty by rAthena, the message is the sender's
    ///     "Name : text" (clif_clan_message clif.cpp:20611-20641).
    /// Clan chat is sent with CZ_CLAN_CHAT 0x098d len.W "own name : text" (clif_parse_clan_chat clif.cpp:20647-20662: the same
    /// "Name : " validation as global chat, clif_process_message). The official help has no line for it; "/cl text" follows
    /// roBrowserLegacy (src/Controls/ProcessCommand.js — a reimplementation).
    /// </summary>
    public sealed class Clan
    {
        public uint Id { get; private set; }
        public string Name { get; private set; } = "";
        public string Master = "", Map = "";
        public readonly List<string> Allies = new List<string>(), Antagonists = new List<string>();
        public int Online = -1, Total = -1;
        public bool InClan => Id != 0;
        public event Action Changed;
        /// <summary>Smoke: clan chat lines seen.</summary>
        public int LinesSeen { get; private set; }

        private readonly RoSession _s; private readonly Func<string> _self; private readonly Action<string, string, ChatKind> _chat;

        public Clan(RoSession s, Func<string> selfName, Action<string, string, ChatKind> chat) { _s = s; _self = selfName; _chat = chat; }

        public void ResetForCharacter() { Id = 0; Name = Master = Map = ""; Allies.Clear(); Antagonists.Clear(); Online = Total = -1; Changed?.Invoke(); }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x098a:
                {
                    r.Skip(4); Id = r.U32(); Name = r.Str(24); Master = r.Str(24); Map = r.Str(16);
                    int allies = r.U8(), enemies = r.U8();
                    Allies.Clear(); Antagonists.Clear();
                    for (int i = 0; i < allies && r.Remaining >= 24; i++) Allies.Add(r.Str(24));
                    for (int i = 0; i < enemies && r.Remaining >= 24; i++) Antagonists.Add(r.Str(24));
                    Debug.Log($"[Clan] {Id} '{Name}' master {Master} map {Map}, {Allies.Count} allies, {Antagonists.Count} antagonists");
                    Changed?.Invoke(); return true;
                }
                case 0x0988:
                {
                    r.Skip(2); Online = r.S16(); Total = r.S16();
                    Debug.Log($"[Clan] {Online}/{Total} online");
                    Changed?.Invoke(); return true;
                }
                case 0x0989:
                {
                    string was = Name;
                    ResetForCharacter();
                    _chat(was.Length > 0 ? Lang.F("You left the clan '{0}'.", was) : Lang.T("You left your clan."), "c8cbd6", ChatKind.System);
                    return true;
                }
                case 0x098e:
                {
                    r.Skip(4); string member = r.Str(24); string text = r.ChatLine(n - 28);
                    LinesSeen++;
                    _chat(Lang.F("[Clan] {0}", (member.Length > 0 && !text.StartsWith(member + " : ") ? member + " : " : "") + text), "c8b4ff", ChatKind.Clan);
                    return true;
                }
            }
            return false;
        }

        /// <summary>CZ_CLAN_CHAT 0x098d len.W "own name : text".</summary>
        public void Say(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!InClan) { _chat(Lang.T("You are not in a clan."), "ffb070", ChatKind.System); return; }
            string t = _self() + " : " + text;
            _s.SendPacket(new PacketWriter(0x098d).U16(0).Str(t, RoText.Encode(t).Length + 1).ToArrayWithLength());
        }

        public string Summary() => !InClan ? "" : (Online >= 0 ? Lang.F("Clan <color=#c8b4ff>{0}</color> · {1}/{2} online", Name, Online, Total) : Lang.F("Clan <color=#c8b4ff>{0}</color>", Name)) +
            (Allies.Count > 0 ? "\n  " + Lang.F("allies: {0}", string.Join(", ", Allies)) : "") + (Antagonists.Count > 0 ? "\n  " + Lang.F("antagonists: {0}", string.Join(", ", Antagonists)) : "");
    }
}
