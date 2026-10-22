using System;
using System.Collections.Generic;
using Loki.Net;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Party state and packets (research/party_packets.md; rAthena e985006, PACKETVER 20211103). Everything is keyed by account id.
    ///   0x0ae4 member added / self on login (89 B): aid.L cid.L leader.L(0 = leader) class.W lv.W x.W y.W offline.B party[24] name[24] map[16] pickup.B division.B
    ///   0x0ae5 member list: len.W party[24] then 54-byte members {aid.L cid.L name[24] map[16] leader.B offline.B class.W lv.W} — replaces the list
    ///   0x0105 member left (aid.L name[24] result.B: 0 left, 1 expelled, 2 can't leave); our own aid = we are out. The leader leaving dissolves the party
    ///   0x02c6 invitation (partyId.L name[24]) → 0x02c7 (partyId.L flag.B 1 = accept); 0x02c5 invite result to the inviter (name[24] result.L)
    ///   0x00fa create result (0 ok, 1 name taken, 2 already in a party); 0x07d8 options (exp.L: 0/1, 2 = refused; pickup.B division.B)
    ///   0x080e member HP (aid hp maxhp; only from same-map members in range), 0x0107 position (aid x.W y.W; -1 = gone), 0x0abd job/level,
    ///   0x0ab2 dead flag, 0x07fc leader change (old.L new.L), 0x02c9 refuse-invites flag.
    /// Sends: create 0x01e8, invite 0x02c4 (name) / 0x00fc (aid), answer 0x02c7, leave 0x0100, expel 0x0103, options 0x07d7, leader 0x07da, refuse 0x02c8.
    /// </summary>
    public sealed class Party
    {
        public sealed class Member
        {
            public uint Aid, Cid; public string Name = "", Map = ""; public bool Leader, Online, Dead; public int Job, Level;
            public int X = -1, Y = -1; public long Hp = -1, MaxHp = -1;
            public bool HasPosition => X >= 0 && Y >= 0;
        }
        public string Name { get; private set; } = "";
        public readonly Dictionary<uint, Member> Members = new Dictionary<uint, Member>();
        public bool InParty => Members.Count > 0;
        public bool ExpShare, ItemPickup, ItemDivision, RefuseInvites;
        /// <summary>A pending invitation: party id and name (null when none).</summary>
        public (uint id, string name)? Invite { get; private set; }
        public bool IsLeader => Members.TryGetValue(_selfAid, out var m) && m.Leader;
        public event Action Changed;
        private readonly RoSession _s; private readonly Action<string, string> _chat; private readonly Func<string> _selfName;
        private uint _selfAid;
        public string LastInviteResult { get; private set; } = "";

        public Party(RoSession s, Func<string> selfName, Action<string, string> chat) { _s = s; _selfName = selfName; _chat = chat; }

        public void SetSelf(uint aid) { _selfAid = aid; }
        /// <summary>Map change: positions and HP of others are stale until the server re-sends them.</summary>
        public void OnMapChange() { foreach (var m in Members.Values) { if (m.Aid != _selfAid) { m.X = m.Y = -1; } } Changed?.Invoke(); }

        private Member Upsert(uint aid)
        {
            if (!Members.TryGetValue(aid, out var m)) { m = new Member { Aid = aid }; Members[aid] = m; }
            return m;
        }

        public static string BareMap(string m) => m != null && m.EndsWith(".gat", StringComparison.OrdinalIgnoreCase) ? m.Substring(0, m.Length - 4) : m ?? "";

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x00fa:
                {
                    r.Skip(2); int res = r.U8();
                    _chat(res == 0 ? Lang.F("Party '{0}' created.", Name) : res == 1 ? Lang.T("That party name is taken or not allowed.") : Lang.T("You are already in a party."), res == 0 ? "a8e6a3" : "ffb070");
                    Changed?.Invoke();
                    return true;
                }
                case 0x02c6:
                {
                    r.Skip(2); uint pid = r.U32(); string name = r.Str(24);
                    Invite = (pid, name);
                    _chat(Lang.F("You are invited to the party '{0}' — answer in the wrist menu's Party tab.", name), "ffd060");
                    Debug.Log($"[Party] invited to {pid} '{name}'");
                    Changed?.Invoke();
                    return true;
                }
                case 0x02c5:
                {
                    r.Skip(2); string name = r.Str(24); int res = r.S32();
                    string m = res == 2 ? Lang.F("{0} joined the party.", name) : res == 0 ? Lang.F("{0} is already in a party.", name) : res == 1 ? Lang.F("{0} declined.", name) : res == 3 ? Lang.T("The party is full.") :
                               res == 4 ? Lang.T("Characters of the same account can't share a party.") : res == 5 ? Lang.F("{0} refuses party invitations.", name) : res == 7 ? Lang.T("That player isn't online.") : res == 10 ? Lang.T("Can't invite inside a memorial dungeon.") : Lang.F("Invitation failed ({0}).", res);
                    LastInviteResult = m; _chat(m, res == 2 ? "a8e6a3" : "ffb070");
                    Debug.Log($"[Party] invite result {res} for '{name}'");
                    Changed?.Invoke();
                    return true;
                }
                case 0x0ae4:
                {
                    r.Skip(2); uint aid = r.U32(), cid = r.U32(); bool leader = r.S32() == 0; int job = r.U16(), lv = r.U16(); int x = r.S16(), y = r.S16(); bool offline = r.U8() != 0;
                    string party = r.Str(24), name = r.Str(24), map = BareMap(r.Str(16)); bool pickup = r.U8() != 0, division = r.U8() != 0;
                    bool isNew = !Members.ContainsKey(aid);
                    var m = Upsert(aid); m.Cid = cid; m.Leader = leader; m.Job = job; m.Level = lv; m.Online = !offline; m.Name = name; m.Map = map;
                    if (x >= 0 && y >= 0 && aid != _selfAid) { m.X = x; m.Y = y; }
                    Name = party; ItemPickup = pickup; ItemDivision = division; Invite = null;
                    if (isNew && aid != _selfAid) _chat(Lang.F("{0} joined the party.", name), "a8e6a3");
                    Debug.Log($"[Party] member {name} ({aid}) leader {leader} lv {lv} job {job} on {map} {(offline ? "offline" : "online")}");
                    Changed?.Invoke();
                    return true;
                }
                case 0x0ae5:
                {
                    r.Skip(4); Name = r.Str(24);
                    var seen = new HashSet<uint>();
                    while (r.Remaining >= 54)
                    {
                        uint aid = r.U32(), cid = r.U32(); string name = r.Str(24), map = BareMap(r.Str(16)); bool leader = r.U8() == 0, offline = r.U8() != 0; int job = r.U16(), lv = r.U16();
                        var m = Upsert(aid); m.Cid = cid; m.Name = name; m.Leader = leader; m.Online = !offline; m.Job = job; m.Level = lv;
                        if (m.Map != map) { m.Map = map; if (aid != _selfAid) m.X = m.Y = -1; }
                        seen.Add(aid);
                    }
                    var gone = new List<uint>(); foreach (var k in Members.Keys) if (!seen.Contains(k)) gone.Add(k);
                    foreach (var k in gone) Members.Remove(k);
                    Invite = null;
                    Debug.Log($"[Party] '{Name}': {Members.Count} members");
                    Changed?.Invoke();
                    return true;
                }
                case 0x0105:
                {
                    r.Skip(2); uint aid = r.U32(); string name = r.Str(24); int res = r.U8();
                    if (aid == _selfAid)
                    {
                        if (res == 2) { _chat(Lang.T("You can't leave the party here."), "ffb070"); Changed?.Invoke(); return true; }
                        Members.Clear(); Name = ""; ExpShare = false;
                        _chat(res == 1 ? Lang.T("You were expelled from the party.") : Lang.T("You left the party."), "ffb070");
                    }
                    else
                    {
                        Members.Remove(aid);
                        _chat(res == 1 ? Lang.F("{0} was expelled from the party.", name) : Lang.F("{0} left the party.", name), "c8cbd6");
                    }
                    Changed?.Invoke();
                    return true;
                }
                case 0x07d8:
                {
                    r.Skip(2); int exp = r.S32(); bool pickup = r.U8() != 0, division = r.U8() != 0;
                    if (exp == 2) _chat(Lang.T("EXP sharing was refused: the level range is too wide."), "ffb070"); else ExpShare = exp == 1;
                    ItemPickup = pickup; ItemDivision = division;
                    Debug.Log($"[Party] options exp {exp} pickup {pickup} division {division}");
                    Changed?.Invoke();
                    return true;
                }
                case 0x080e: { r.Skip(2); uint aid = r.U32(); long hp = r.S32(), max = r.S32(); if (Members.TryGetValue(aid, out var m)) { m.Hp = hp; m.MaxHp = max; HpChanged?.Invoke(aid, hp, max); Changed?.Invoke(); } return true; }
                case 0x0107: { r.Skip(2); uint aid = r.U32(); int x = r.S16(), y = r.S16(); if (Members.TryGetValue(aid, out var m)) { m.X = x; m.Y = y; Changed?.Invoke(); } return true; }
                case 0x0abd: { r.Skip(2); uint aid = r.U32(); int job = r.U16(), lv = r.U16(); if (Members.TryGetValue(aid, out var m)) { m.Job = job; m.Level = lv; Changed?.Invoke(); } return true; }
                case 0x0ab2: { r.Skip(2); uint aid = r.U32(); bool dead = r.U8() != 0; if (Members.TryGetValue(aid, out var m)) { m.Dead = dead; Changed?.Invoke(); } return true; }
                case 0x07fc:
                {
                    r.Skip(2); uint oldL = r.U32(), newL = r.U32();
                    if (Members.TryGetValue(oldL, out var a)) a.Leader = false; if (Members.TryGetValue(newL, out var c)) { c.Leader = true; _chat(Lang.F("{0} is now the party leader.", c.Name), "c8cbd6"); }
                    Changed?.Invoke();
                    return true;
                }
                case 0x02c9: { r.Skip(2); RefuseInvites = r.U8() != 0; Changed?.Invoke(); return true; }
                default: return false;
            }
        }
        /// <summary>A party member's HP arrived (aid, hp, max) — for the overhead bar of that unit.</summary>
        public event Action<uint, long, long> HpChanged;

        // ---- sending
        public void Create(string name, bool pickup = true, bool division = false)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            Name = name.Trim();
            _s.SendPacket(new PacketWriter(0x01e8).Str(Name, 24).U8((byte)(pickup ? 1 : 0)).U8((byte)(division ? 1 : 0)).ToArray());
            Debug.Log("[Party] create " + Name);
        }
        public void InviteByName(string name) { if (!string.IsNullOrWhiteSpace(name)) { _s.SendPacket(new PacketWriter(0x02c4).Str(name.Trim(), 24).ToArray()); _chat(Lang.F("Inviting {0}…", name.Trim()), "c8cbd6"); } }
        public void InviteByAid(uint aid) { _s.SendPacket(new PacketWriter(0x00fc).U32(aid).ToArray()); }
        public void Answer(bool accept)
        {
            if (Invite == null) return;
            var inv = Invite.Value; Invite = null;
            _s.SendPacket(new PacketWriter(0x02c7).U32(inv.id).U8((byte)(accept ? 1 : 0)).ToArray());
            _chat(accept ? Lang.F("Joining '{0}'…", inv.name) : Lang.F("You declined '{0}'.", inv.name), "c8cbd6");
            Changed?.Invoke();
        }
        public void Leave() { _s.SendPacket(new PacketWriter(0x0100).ToArray()); }
        public void Expel(Member m) { if (m != null) _s.SendPacket(new PacketWriter(0x0103).U32(m.Aid).Str(m.Name, 24).ToArray()); }
        public void SetOptions(bool exp, bool pickup, bool division) { _s.SendPacket(new PacketWriter(0x07d7).U32(exp ? 1u : 0u).U8((byte)(pickup ? 1 : 0)).U8((byte)(division ? 1 : 0)).ToArray()); }
        public void MakeLeader(Member m) { if (m != null) _s.SendPacket(new PacketWriter(0x07da).U32(m.Aid).ToArray()); }
        public void SetRefuseInvites(bool refuse) { _s.SendPacket(new PacketWriter(0x02c8).U8((byte)(refuse ? 1 : 0)).ToArray()); }
    }
}
