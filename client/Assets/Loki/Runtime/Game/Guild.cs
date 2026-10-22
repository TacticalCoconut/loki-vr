using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Guild state, packets and the guild window (research/guild_woe_packets.md; rAthena e985006, PACKETVER 20211103).
    /// Pushed at login / create / join: 0x016c (43: gid emblem mode isMaster interSid name[24]), 0x0b7b (118: basic info), 0x016f notice,
    /// 0x014e menu flags. Everything else is asked for with 0x014f types 0-4 (0: 0x0b7b + 0x014c relations + 0x0b27 castles; 1: 0x0166
    /// titles + 0x0b7d members (58 each); 2: 0x0166 + 0x0160 rights/tax; 3: 0x0162 skills (37 each); 4: 0x0b7c expulsions (68 each)).
    /// Changes: 0x0174 rank, 0x0156 member rank, 0x0afd unit title (AREA), 0x01f2 online/offline, 0x01eb guild-mate position (-1 = gone),
    /// 0x0a83 left (cid, reason) / 0x0a82 expelled (reason, cid), 0x015e disband, 0x016a invitation → 0x016b, 0x0169 invite result,
    /// 0x0171 alliance request → 0x0172, 0x0173 / 0x0181 / 0x0184 relation results, 0x0152 emblem (zlib BMP) for 0x0151, 0x0b47 a unit's
    /// guild changed, 0x0368 asks for unit names (EntityManager parses the 0x0a30 reply), 0x0b27 / 0x0b2d / 0x0b2e castles, 0x099b map property.
    /// Sends: create 0x0165, invite 0x0916 (name) / 0x0168 (aid), leave 0x0159, expel 0x015b, disband 0x015d (key = guild name), member rank
    /// 0x0155 (rank 0 = hand over the guild), rank rights 0x0161, notice 0x016e, alliance 0x0170 / 0x0172 / 0x0180 / 0x0183, skill up
    /// 0x0112, guild skills through 0x0438, castle 0x0b2c / 0x0b28, emblem upload 0x0153 (zlib stream of a ≤1800-byte BMP).
    /// Rights come from the rank's mode bits (0x01 invite, 0x10 expel, 0x100 storage), not from being the master (guild.cpp:960, :1203).
    /// </summary>
    public sealed class Guild
    {
        public const int PermInvite = 0x01, PermExpel = 0x10, PermStorage = 0x100;
        public sealed class Member { public uint Aid, Cid; public int Hair, HairColor, Sex, Job, Level, Position; public long Tax; public bool Online; public uint LastLogin; public string Name = ""; }
        public sealed class Rank { public int Id; public string Title = ""; public int Mode, Tax; }
        public sealed class Relation { public int Kind; public uint GuildId; public string Name = ""; } // Kind 0 ally, 1 antagonist
        public sealed class GSkill { public int Id, Inf, Level, Sp, Range; public string Name = ""; public bool Upgradable; }
        public sealed class Expelled { public uint Cid; public string Reason = "", Name = ""; }
        public sealed class LogEntry { public int ItemId, Amount, Refine; public bool Put; public string Name = "", Time = ""; }
        /// <summary>The guild storage history (0x09da), shown when the Guild Warehouse NPC's "view log" answers.</summary>
        public readonly List<LogEntry> StorageLog = new List<LogEntry>();

        public uint Id { get; private set; }
        public string Name { get; private set; } = "";
        public int EmblemVersion { get; private set; }
        public int Level, OnlineCount, Capacity, AverageLevel, MenuFlag; public long Exp, NextExp; public string ManageLand = ""; public uint MasterCid; public string MasterName = "";
        public bool InGuild => Id != 0;
        public readonly List<Member> Members = new List<Member>();
        public readonly Rank[] Ranks = new Rank[20];
        public readonly List<Relation> Relations = new List<Relation>();
        public readonly List<GSkill> Skills = new List<GSkill>(); public int SkillPoints;
        public readonly List<Expelled> Expulsions = new List<Expelled>();
        public readonly List<int> Castles = new List<int>();
        public readonly Dictionary<int, (int economy, int defense)> CastleInvestment = new Dictionary<int, (int, int)>();
        public string NoticeSubject = "", NoticeText = "";
        public (uint id, string name)? Invite { get; private set; }
        public (uint aid, string guild)? AllyRequest { get; private set; }
        /// <summary>Guild-mates on this map (0x01eb, every 5 s; x = y = -1 removes).</summary>
        public readonly Dictionary<uint, Vector2Int> MatePositions = new Dictionary<uint, Vector2Int>();
        /// <summary>ZC_MAPPROPERTY_R2 0x099b: type 3 = AGITZONE (WoE running on this map), flags bit 1 GUILD, bit 2 SIEGE (GvG-tagged map).</summary>
        public int MapType, MapFlags;
        public bool WoeHere => MapType == 3;
        public bool SiegeMap => (MapFlags & 4) != 0;
        /// <summary>Last result line (create / invite / alliance …) for the wrist tab and the smoke test.</summary>
        public string LastResult { get; private set; } = "";
        public int LastCreateResult = -1, LastInviteResult = -1, LastAllyResult = -1, LastDisbandResult = -1;
        public event Action Changed;
        /// <summary>A unit's party / guild / title names arrived (0x0a30, 0x0afd, 0x0b47).</summary>
        public event Action<Sprites.Unit> UnitInfo;
        /// <summary>An emblem image arrived for this guild id.</summary>
        public event Action<uint> EmblemChanged;
        public event Action MapPropertyChanged;
        public Func<uint, Sprites.Unit> UnitById;
        /// <summary>Client wave 4: the /loginout switch (guild-mates' login / logout lines).</summary>
        public Func<bool> ShowLogins;
        /// <summary>The gaze-locked player (not ourselves), or null.</summary>
        public Func<Sprites.Unit> Gazed;
        public Action<string> Toast;

        private readonly RoSession _s; private readonly ListWindow _win; private readonly Func<Camera> _head;
        private readonly Func<uint> _selfCid, _selfAid; private readonly Action<string, string> _chat; private readonly Func<bool> _ready;
        private bool _isMasterFlag, _ranksKnown, _needLists; private int _modeFlag;
        private string _noticeShown = null;

        public Guild(RoSession s, ListWindow win, Func<Camera> head, Func<uint> selfCid, Func<uint> selfAid, Action<string, string> chat, Func<bool> ready)
        {
            _s = s; _win = win; _head = head; _selfCid = selfCid; _selfAid = selfAid; _chat = chat; _ready = ready;
            for (int i = 0; i < Ranks.Length; i++) Ranks[i] = new Rank { Id = i, Title = i == 0 ? "GuildMaster" : i == 19 ? "Newbie" : "Position " + (i + 1) };
            _win.OnCloseClicked = Hide;
        }

        public Member Me { get { uint c = _selfCid(); return Members.FirstOrDefault(m => m.Cid == c); } }
        public bool AmMaster => MasterCid != 0 ? MasterCid == _selfCid() : _isMasterFlag;
        /// <summary>This character's rank bits: from the member list + rank table when known, else 0x016c's mode.</summary>
        public int MyRights { get { var me = Me; return _ranksKnown && me != null && me.Position >= 0 && me.Position < 20 ? Ranks[me.Position].Mode : _modeFlag; } }
        public bool CanInvite => (MyRights & PermInvite) != 0;
        public bool CanExpel => (MyRights & PermExpel) != 0;
        public string MyTitle { get { var me = Me; return me != null && me.Position >= 0 && me.Position < 20 ? Ranks[me.Position].Title : ""; } }
        public int OnlineMembers => Members.Count > 0 ? Members.Count(m => m.Online) : OnlineCount;
        public Relation RelationWith(uint gid) => Relations.FirstOrDefault(r => r.GuildId == gid);

        /// <summary>Map change: mate dots are re-sent by the server after LoadEndAck (guild.cpp:1446-1454).</summary>
        public void OnMapChange() { MatePositions.Clear(); _nameAsked.Clear(); _nameQueue.Clear(); _freshMap = true; Changed?.Invoke(); }
        private bool _freshMap = true;

        private void Joined()
        {
            _needLists = true; Invite = null;
            Debug.Log($"[Guild] in guild {Id} '{Name}'");
        }

        private uint _forCid;
        /// <summary>A different character entered the game (character select): forget the previous one's guild before its packets arrive.</summary>
        public void ResetForCharacter(uint cid)
        {
            if (cid == _forCid) return;
            _forCid = cid; Invite = null; AllyRequest = null;
            if (InGuild || Members.Count > 0) LeftGuild(null);
        }

        private void LeftGuild(string msg)
        {
            Debug.Log("[Guild] out: " + msg);
            Id = 0; Name = ""; EmblemVersion = 0; Level = OnlineCount = Capacity = AverageLevel = 0; Exp = NextExp = 0; MasterCid = 0; MasterName = ""; ManageLand = "";
            _isMasterFlag = false; _modeFlag = 0; _ranksKnown = false; _needLists = false;
            Members.Clear(); Relations.Clear(); Skills.Clear(); Expulsions.Clear(); Castles.Clear(); CastleInvestment.Clear(); MatePositions.Clear(); SkillPoints = 0;
            for (int i = 0; i < Ranks.Length; i++) { Ranks[i].Title = i == 0 ? "GuildMaster" : i == 19 ? "Newbie" : "Position " + (i + 1); Ranks[i].Mode = 0; Ranks[i].Tax = 0; }
            NoticeSubject = NoticeText = ""; _noticeShown = null;
            _win.Hide(); _pickFor = null; _rankSel = -1;
            if (msg != null) { _chat(msg, "ffb070"); LastResult = msg; } else LastResult = "";
            Changed?.Invoke();
        }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x0167: // ZC_RESULT_MAKE_GUILD result.B (guild.cpp:702-733)
                {
                    r.Skip(2); int res = r.U8(); LastCreateResult = res;
                    string m = res == 0 ? Lang.T("Your guild was founded!") : res == 1 ? Lang.T("You are already in a guild.") : res == 2 ? Lang.T("That guild name is taken or not allowed.") : res == 3 ? Lang.T("Founding a guild needs an Emperium in your inventory.") : Lang.F("The guild wasn't created ({0}).", res);
                    LastResult = m; _chat(m, res == 0 ? "a8e6a3" : "ffb070"); Toast?.Invoke(m);
                    Changed?.Invoke(); return true;
                }
                case 0x016c: // ZC_UPDATE_GDID gid.L emblem.L mode.L isMaster.B interSid.L name[24]
                {
                    r.Skip(2); uint gid = r.U32(); int emb = r.S32(); int mode = r.S32(); bool master = r.U8() != 0; r.Skip(4); string name = r.Str(24);
                    bool fresh = Id != gid; Id = gid; EmblemVersion = emb; _modeFlag = mode; _isMasterFlag = master; Name = name;
                    if (fresh && gid != 0) Joined();
                    Changed?.Invoke(); return true;
                }
                case 0x014e: { r.Skip(2); MenuFlag = r.S32(); return true; } // ZC_ACK_GUILD_MENUINTERFACE (0xD7 master, 0x57 others)
                case 0x0b7b: // ZC_GUILD_INFO (118)
                {
                    r.Skip(2); uint gid = r.U32(); Level = r.S32(); OnlineCount = r.S32(); Capacity = r.S32(); AverageLevel = r.S32(); Exp = r.U32(); NextExp = r.S32();
                    r.Skip(12); int emb = r.S32(); string name = r.Str(24); ManageLand = r.Str(16); r.Skip(4); MasterCid = r.U32(); MasterName = r.Str(24);
                    bool fresh = Id != gid; Id = gid; Name = name; EmblemVersion = emb;
                    if (fresh && gid != 0) Joined();
                    Debug.Log($"[Guild] info '{Name}' lv {Level} {OnlineCount}/{Capacity} master {MasterName} ({MasterCid}) emblem {emb}");
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x014c: // ZC_MYGUILD_BASIC_INFO: 32-byte {relation.L gid.L name[24]} — always the full list
                {
                    r.Skip(4); Relations.Clear();
                    while (r.Remaining >= 32) { int kind = r.S32(); uint gid = r.U32(); string name = r.Str(24); Relations.Add(new Relation { Kind = kind, GuildId = gid, Name = name }); }
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x0b7d: // ZC_MEMBERMGR_INFO: 58-byte members in slot order (slot 0 = master)
                {
                    r.Skip(4); Members.Clear();
                    while (r.Remaining >= 58)
                    {
                        var m = new Member { Aid = r.U32(), Cid = r.U32(), Hair = r.S16(), HairColor = r.S16(), Sex = r.S16(), Job = r.S16(), Level = r.S16() };
                        m.Tax = r.S32(); m.Online = r.S32() != 0; m.Position = r.S32(); m.LastLogin = r.U32(); m.Name = r.Str(24);
                        Members.Add(m);
                    }
                    Debug.Log($"[Guild] {Members.Count} members, me {(Me != null ? "rank " + Me.Position : "not listed")}");
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x0166: // ZC_POSITION_ID_NAME_INFO: 20 × {id.L title[24]}
                {
                    r.Skip(4);
                    while (r.Remaining >= 28) { int pid = r.S32(); string t = r.Str(24); if (pid >= 0 && pid < 20) Ranks[pid].Title = t; }
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x0160: // ZC_POSITION_INFO: 20 × {id.L mode.L ranking.L payRate.L}
                {
                    r.Skip(4);
                    while (r.Remaining >= 16) { int pid = r.S32(); int mode = r.S32(); r.Skip(4); int tax = r.S32(); if (pid >= 0 && pid < 20) { Ranks[pid].Mode = mode; Ranks[pid].Tax = tax; } }
                    _ranksKnown = true;
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x0174: // ZC_ACK_CHANGE_GUILD_POSITIONINFO: {id.L mode.L ranking.L payRate.L title[24]} (one per change, to every member)
                {
                    r.Skip(4);
                    while (r.Remaining >= 40) { int pid = r.S32(); int mode = r.S32(); r.Skip(4); int tax = r.S32(); string t = r.Str(24); if (pid >= 0 && pid < 20) { Ranks[pid].Mode = mode; Ranks[pid].Tax = tax; Ranks[pid].Title = t; } Debug.Log($"[Guild] rank {pid} '{t}' mode 0x{mode:x} tax {tax}"); }
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x0156: // ZC_ACK_REQ_CHANGE_MEMBERS: {aid.L cid.L position.L}
                {
                    r.Skip(4);
                    while (r.Remaining >= 12)
                    {
                        uint aid = r.U32(), cid = r.U32(); int pos = r.S32();
                        var m = Members.FirstOrDefault(x => x.Cid == cid);
                        if (m != null) { m.Position = pos; _chat(pos >= 0 && pos < 20 ? Lang.F("{0} is now {1}.", m.Name, Ranks[pos].Title) : Lang.F("{0} is now rank {1}.", m.Name, pos), "b8f0b8"); }
                        Debug.Log($"[Guild] member {cid} → rank {pos}");
                    }
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x0afd: // ZC_GUILD_POSITION len.W aid.L title[] (AREA): the title under that player's name
                {
                    r.Skip(4); uint aid = r.U32(); string title = n > 8 ? r.Str(n - 8) : "";
                    var u = UnitById?.Invoke(aid); if (u != null) { u.GuildTitle = title; UnitInfo?.Invoke(u); }
                    return true;
                }
                case 0x0152: // ZC_GUILD_EMBLEM_IMG len.W gid.L version.L data (zlib stream of a BMP)
                {
                    r.Skip(4); uint gid = r.U32(); int ver = r.S32(); var data = r.Bytes(n - 12);
                    StoreEmblem(gid, ver, data);
                    return true;
                }
                case 0x0b47: // ZC_CHANGE_GUILD gid.L emblem.L aid.L (AREA): a unit joined / left a guild or its emblem changed
                {
                    r.Skip(2); uint gid = r.U32(); int ver = r.S32(); uint aid = r.U32();
                    if (aid == _selfAid() && gid == Id && gid != 0) EmblemVersion = ver;
                    var u = UnitById?.Invoke(aid);
                    if (u != null)
                    {
                        u.GuildId = gid; u.GuildEmblem = ver & 0xFFFF;
                        if (gid == 0) { u.GuildName = ""; u.GuildTitle = ""; } else RequestName(aid, true); // (its guild changed: an answer from before is stale)
                        UnitInfo?.Invoke(u);
                    }
                    Debug.Log($"[Guild] unit {aid} guild → {gid} emblem {ver}");
                    return true;
                }
                case 0x0162: // ZC_GUILD_SKILLINFO len.W points.W then 37-byte {id.W inf.L level.W sp.W range.W name[24] upFlag.B}
                {
                    r.Skip(4); SkillPoints = r.S16(); Skills.Clear();
                    while (r.Remaining >= 37) Skills.Add(new GSkill { Id = r.U16(), Inf = r.S32(), Level = r.U16(), Sp = r.U16(), Range = r.U16(), Name = r.Str(24), Upgradable = r.U8() != 0 });
                    Debug.Log($"[Guild] skills: {Skills.Count}, points {SkillPoints}");
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x016f: // ZC_GUILD_NOTICE subject[60] notice[120] (login, every map load, changes; nothing when both are empty)
                {
                    r.Skip(2); NoticeSubject = r.Text(60); NoticeText = r.Text(120);
                    string key = NoticeSubject + "\n" + NoticeText;
                    if (key != _noticeShown) { _noticeShown = key; if (NoticeSubject.Length + NoticeText.Length > 0) _chat(NoticeText.Length > 0 ? Lang.F("[Guild notice] {0} — {1}", NoticeSubject, NoticeText) : Lang.F("[Guild notice] {0}", NoticeSubject), "b8f0b8"); }
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x016a: // ZC_REQ_JOIN_GUILD gid.L name[24]
                {
                    r.Skip(2); uint gid = r.U32(); string name = r.Str(24);
                    Invite = (gid, name);
                    string m = Lang.F("You are invited to the guild '{0}' — answer in the wrist menu's Guild tab.", name);
                    _chat(m, "ffd060"); Toast?.Invoke(Lang.F("Guild invitation: {0}", name));
                    Debug.Log($"[Guild] invited to {gid} '{name}'");
                    Changed?.Invoke(); return true;
                }
                case 0x0169: // ZC_ACK_REQ_JOIN_GUILD result.B (guild.cpp:960-1142)
                {
                    r.Skip(2); int res = r.U8(); LastInviteResult = res;
                    string m = res == 0 ? Lang.T("They are already in a guild (or standing on a guild map).") : res == 1 ? Lang.T("They declined the invitation.") : res == 2 ? Lang.T("They joined the guild!") : res == 3 ? Lang.T("The guild is full.") : Lang.F("Invitation failed ({0}).", res);
                    LastResult = m; _chat(m, res == 2 ? "a8e6a3" : "ffb070"); Toast?.Invoke(m);
                    Changed?.Invoke(); return true;
                }
                case 0x0a83: // ZC_ACK_LEAVE_GUILD2 cid.L reason[40] (every member, the leaver included)
                {
                    r.Skip(2); uint cid = r.U32(); string reason = r.Str(40);
                    if (cid == _selfCid() && Id != 0) { LeftGuild(Lang.T("You left the guild.")); return true; }
                    var m = Members.FirstOrDefault(x => x.Cid == cid); string who = m != null ? m.Name : Lang.T("A member");
                    Members.RemoveAll(x => x.Cid == cid);
                    _chat(reason.Length > 0 ? Lang.F("{0} left the guild: {1}", who, reason) : Lang.F("{0} left the guild.", who), "c8cbd6");
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x0a82: // ZC_ACK_BAN_GUILD3 reason[40] cid.L — reason first (packets_struct.hpp:3668-3672)
                {
                    r.Skip(2); string reason = r.Str(40); uint cid = r.U32();
                    if (cid == _selfCid() && Id != 0) { LeftGuild(reason.Length > 0 ? Lang.F("You were expelled from the guild: {0}", reason) : Lang.T("You were expelled from the guild.")); return true; }
                    var m = Members.FirstOrDefault(x => x.Cid == cid); string who = m != null ? m.Name : Lang.T("A member");
                    Members.RemoveAll(x => x.Cid == cid);
                    _chat(reason.Length > 0 ? Lang.F("{0} was expelled from the guild: {1}", who, reason) : Lang.F("{0} was expelled from the guild.", who), "c8cbd6");
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x0b7c: // ZC_BAN_LIST3: 68-byte {cid.L reason[40] name[24]}
                {
                    r.Skip(4); Expulsions.Clear();
                    while (r.Remaining >= 68) Expulsions.Add(new Expelled { Cid = r.U32(), Reason = r.Str(40), Name = r.Str(24) });
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x01f2: // ZC_UPDATE_CHARSTAT2 aid.L cid.L status.L sex.W hair.W hairColor.W
                {
                    r.Skip(2); uint aid = r.U32(), cid = r.U32(); bool on = r.S32() != 0;
                    var m = Members.FirstOrDefault(x => x.Cid == cid);
                    // msgstringtable 1041 "%s has logged in." / 1042 "%s has logged out.", shown while the official /loginout switch is on (client wave 4)
                    if (m != null && m.Online != on) { m.Online = on; if (cid != _selfCid() && (ShowLogins == null || ShowLogins())) _chat(ClientText.Fill(ClientText.Msg(on ? 1041 : 1042), m.Name), "9aa3b5"); }
                    if (!on) MatePositions.Remove(aid);
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x01eb: // ZC_NOTIFY_POSITION_TO_GUILDM aid.L x.W y.W (-1,-1 = gone from this map)
                {
                    r.Skip(2); uint aid = r.U32(); int x = r.S16(), y = r.S16();
                    if (x < 0 || y < 0) MatePositions.Remove(aid); else MatePositions[aid] = new Vector2Int(x, y);
                    return true;
                }
                case 0x0171: // ZC_REQ_ALLY_GUILD requester aid.L guild name[24]
                {
                    r.Skip(2); uint aid = r.U32(); string gname = r.Str(24);
                    AllyRequest = (aid, gname);
                    _chat(Lang.F("The guild '{0}' asks for an alliance (Guild tab).", gname), "ffd060"); Toast?.Invoke(Lang.F("Alliance request: {0}", gname));
                    Changed?.Invoke(); return true;
                }
                case 0x0173: // ZC_ACK_REQ_ALLY_GUILD result.B (what guild.cpp actually sends: §2.21)
                {
                    r.Skip(2); int res = r.U8(); LastAllyResult = res;
                    string m = res == 0 ? Lang.T("You are already allied with that guild.") : res == 1 ? Lang.T("That guild already has an alliance request waiting.") : res == 2 ? Lang.T("The alliance was formed!") :
                               res == 3 ? Lang.T("The alliance was refused, or their guild has too many allies.") : res == 4 ? Lang.T("Your guild has too many allies.") : res == 5 ? Lang.T("Only a guild master can be asked.") : Lang.F("Alliance failed ({0}).", res);
                    LastResult = m; _chat(m, res == 2 ? "a8e6a3" : "ffb070"); Toast?.Invoke(m);
                    Changed?.Invoke(); return true;
                }
                case 0x0181: // ZC_ACK_REQ_HOSTILE_GUILD result.B: 0 set, 1 too many, 2 already
                {
                    r.Skip(2); int res = r.U8();
                    string m = res == 0 ? Lang.T("That guild is now your enemy.") : res == 1 ? Lang.T("Your guild has too many enemies.") : res == 2 ? Lang.T("That guild is already your enemy.") : Lang.F("Failed ({0}).", res);
                    LastResult = m; _chat(m, res == 0 ? "a8e6a3" : "ffb070");
                    Changed?.Invoke(); return true;
                }
                case 0x0184: // ZC_DELETE_RELATED_GUILD gid.L relation.L
                {
                    r.Skip(2); uint gid = r.U32(); int rel = r.S32();
                    var rr = Relations.FirstOrDefault(x => x.GuildId == gid && x.Kind == rel);
                    if (rr != null) { Relations.Remove(rr); _chat(rel == 0 ? Lang.F("The alliance with '{0}' ended.", rr.Name) : Lang.F("The rivalry with '{0}' ended.", rr.Name), "c8cbd6"); }
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x015e: // ZC_ACK_DISORGANIZE_GUILD_RESULT result.L: 0 disbanded, 2 members remain
                {
                    r.Skip(2); int res = r.S32(); LastDisbandResult = res;
                    if (res == 0) { if (Id != 0) LeftGuild(Lang.T("The guild was disbanded.")); return true; }
                    string m = res == 2 ? Lang.T("Every other member must leave (or be expelled) before the guild can be disbanded.") : Lang.F("The guild wasn't disbanded ({0}).", res);
                    LastResult = m; _chat(m, "ffb070"); Toast?.Invoke(m);
                    return true;
                }
                // 0x0a30 / 0x0adf (unit names) are parsed by EntityManager, which raises NamesChanged (it sits first in the packet chain)
                case 0x09da: // ZC_ACK_GUILDSTORAGE_LOG len.W result.W count.W then 93-byte entries, newest first (storage.cpp:709-717, clif.cpp:22163-22203)
                {
                    r.Skip(4); int result = r.U16(); r.Skip(2); StorageLog.Clear();
                    while (r.Remaining >= 93)
                    {
                        var e = new LogEntry(); r.Skip(4); e.ItemId = (int)r.U32(); e.Amount = r.S32(); e.Put = r.U8() == 1; e.Refine = r.S32(); r.Skip(8 + 1 + 2 + 16); e.Name = r.Str(24); e.Time = r.Str(24); r.Skip(1);
                        StorageLog.Add(e);
                    }
                    Debug.Log($"[Guild] storage log result {result}: {StorageLog.Count} entries");
                    if (result != 1 || StorageLog.Count == 0) _chat(result == 3 ? Lang.T("The guild storage log isn't available.") : Lang.T("The guild storage log is empty."), "c8cbd6");
                    else if (InGuild) { Current = Page.Log; _pickFor = null; _rankSel = -1; _win.Open(_head()); Refresh(); }
                    return true;
                }
                case 0x0b27: // ZC_GUILD_AGIT_INFO len.W then one ClientId.B per owned castle
                {
                    r.Skip(4); Castles.Clear(); while (r.Remaining >= 1) Castles.Add(r.U8());
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x0b2d: // ZC_REQ_ACK_AGIT_INVESTMENT clientId.B economy.L defense.L
                {
                    r.Skip(2); int cid = r.U8(); int eco = r.S32(), def = r.S32(); CastleInvestment[cid] = (eco, def);
                    _chat(Lang.F("{0}: economy {1}, defense {2}.", CastleName(cid), eco, def), "b8f0b8");
                    Refresh(); return true;
                }
                case 0x0b2e: { r.Skip(2); int res = r.S16(); _chat(res == 1 ? Lang.T("Not enough zeny to go to the castle.") : Lang.F("Castle teleport failed ({0}).", res), "ffb070"); return true; }
                case 0x099b: // ZC_MAPPROPERTY_R2 type.W flags.L (every LoadEndAck; pushed to the map when a script sets / clears gvg)
                {
                    r.Skip(2); int type = r.U16(); int flags = r.S32();
                    bool was = WoeHere; MapType = type; MapFlags = flags;
                    // the first one after a map change describes the new map; later ones are WoE starting / ending where we stand (GvgOn / GvgOff, map.cpp:4792-4808)
                    if (_freshMap) { if (WoeHere) _chat(Lang.T("The War of Emperium is on here: other guilds' players can be attacked."), "ff8a5a"); }
                    else if (was != WoeHere) _chat(WoeHere ? Lang.T("The War of Emperium has started here: other guilds' players can be attacked.") : Lang.T("The War of Emperium is over here."), WoeHere ? "ff8a5a" : "c8cbd6");
                    _freshMap = false;
                    Debug.Log($"[Guild] map property type {type} flags 0x{flags:x}");
                    MapPropertyChanged?.Invoke(); return true;
                }
                default: return false;
            }
        }

        // ------------------------------------------------------------------ sending
        public void Create(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            _s.SendPacket(new PacketWriter(0x0165).U32(_selfCid()).Str(name.Trim(), 24).ToArray());
            _chat(Lang.F("Founding the guild '{0}'…", name.Trim()), "c8cbd6"); Debug.Log("[Guild] create " + name.Trim());
        }
        public void RequestInfo(int type) { _s.SendPacket(new PacketWriter(0x014f).U32((uint)type).ToArray()); }
        public void RequestAll()
        {
            if (!InGuild) return;
            _s.SendPacket(new PacketWriter(0x014d).ToArray());
            for (int t = 0; t <= 4; t++) RequestInfo(t);
            if (EmblemVersion > 0) EmblemOf(Id, EmblemVersion, true);
        }
        public void InviteByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            _s.SendPacket(new PacketWriter(0x0916).Str(name.Trim(), 24).ToArray());
            _chat(Lang.F("Inviting {0} to the guild…", name.Trim()), "c8cbd6");
        }
        public void InviteByAid(uint aid) { _s.SendPacket(new PacketWriter(0x0168).U32(aid).U32(_selfAid()).U32(_selfCid()).ToArray()); }
        public void Answer(bool accept)
        {
            if (Invite == null) return;
            var inv = Invite.Value; Invite = null;
            _s.SendPacket(new PacketWriter(0x016b).U32(inv.id).U32(accept ? 1u : 0u).ToArray());
            _chat(accept ? Lang.F("Joining '{0}'…", inv.name) : Lang.F("You declined '{0}'.", inv.name), "c8cbd6");
            Changed?.Invoke();
        }
        public void Leave(string reason = "")
        {
            if (!InGuild) return;
            _s.SendPacket(new PacketWriter(0x0159).U32(Id).U32(_selfAid()).U32(_selfCid()).Str(reason ?? "", 40).ToArray());
        }
        public void Expel(Member m, string reason = "")
        {
            if (!InGuild || m == null) return;
            _s.SendPacket(new PacketWriter(0x015b).U32(Id).U32(m.Aid).U32(m.Cid).Str(reason ?? "", 40).ToArray());
        }
        public void Disband() { if (InGuild) _s.SendPacket(new PacketWriter(0x015d).Str(Name, 40).ToArray()); }
        /// <summary>0x0155 with one entry; rank 0 hands the guild over (refused during WoE, msg 2963, or too soon after the last change, msg 2964).</summary>
        public void SetMemberRank(Member m, int rank)
        {
            if (!InGuild || m == null || rank < 0 || rank >= 20) return;
            _s.SendPacket(new PacketWriter(0x0155).U16(0).U32(m.Aid).U32(m.Cid).S32(rank).ToArrayWithLength());
            Debug.Log($"[Guild] rank of {m.Name} → {rank}");
        }
        /// <summary>0x0161 with one 40-byte entry {id, mode, ranking, payRate, title}; the server masks mode with 0x111 and caps the tax (guild.cpp:1514-1515).</summary>
        public void SetRank(int rank, string title, int mode, int tax)
        {
            if (!InGuild || rank < 0 || rank >= 20) return;
            _s.SendPacket(new PacketWriter(0x0161).U16(0).S32(rank).S32(mode).S32(rank).S32(tax).Str(title ?? "", 24).ToArrayWithLength());
            Debug.Log($"[Guild] set rank {rank} '{title}' mode 0x{mode:x} tax {tax}");
        }
        public void SetNotice(string subject, string text)
        {
            if (!InGuild) return;
            _s.SendPacket(new PacketWriter(0x016e).U32(Id).Str(subject ?? "", 60).Str(text ?? "", 120).ToArray());
        }
        public void RequestAlly(uint aid) { _s.SendPacket(new PacketWriter(0x0170).U32(aid).U32(_selfAid()).U32(_selfCid()).ToArray()); }
        public void AnswerAlly(bool accept)
        {
            if (AllyRequest == null) return;
            var req = AllyRequest.Value; AllyRequest = null;
            _s.SendPacket(new PacketWriter(0x0172).U32(req.aid).U32(accept ? 1u : 0u).ToArray());
            _chat(accept ? Lang.F("Accepting the alliance with '{0}'…", req.guild) : Lang.F("You refused the alliance with '{0}'.", req.guild), "c8cbd6");
            Changed?.Invoke();
        }
        public void RequestHostile(uint aid) { _s.SendPacket(new PacketWriter(0x0180).U32(aid).ToArray()); }
        public void RemoveRelation(Relation rel) { if (rel != null) _s.SendPacket(new PacketWriter(0x0183).U32(rel.GuildId).U32((uint)rel.Kind).ToArray()); }
        public void SkillUp(int skill) { _s.SendPacket(new PacketWriter(0x0112).U16((ushort)skill).ToArray()); Debug.Log($"[Guild] skill up {skill}"); }
        /// <summary>Guild skills are self-targeted; the server replaces the level with the guild's (clif.cpp:12971-12976).</summary>
        public void UseSkill(int skill) { _s.UseSkill(skill, 1, _selfAid()); }
        public void CastleInfo(int clientId) { _s.SendPacket(new PacketWriter(0x0b2c).U8((byte)clientId).ToArray()); }
        public void CastleTeleport(int clientId) { _s.SendPacket(new PacketWriter(0x0b28).U8((byte)clientId).ToArray()); }

        // ------------------------------------------------------------------ castles
        private static readonly string[] CastleMaps = { "prtg_cas0", "aldeg_cas0", "gefg_cas0", "payg_cas0", "arug_cas0", "schg_cas0" };
        /// <summary>castle_db.yml ClientIds: prtg 1-5, aldeg 6-10, gefg 11-15, payg 16-20, arug 21-25, schg 26-30.</summary>
        public static string CastleMap(int clientId) => clientId >= 1 && clientId <= 30 ? CastleMaps[(clientId - 1) / 5] + ((clientId - 1) % 5 + 1) : "";
        /// <summary>db/re/castle_db.yml CastleName by ClientId 1-30 (the map name table only has the realm, e.g. "Valkyrie Realm Guild").</summary>
        private static readonly string[] CastleNames = {
            "Kriemhild", "Swanhild", "Fadhgridh", "Skoegul", "Gondul",
            "Neuschwanstein", "Hohenschwangau", "Nuernberg", "Wuerzburg", "Rothenburg",
            "Repherion", "Eeyolbriggar", "Yesnelph", "Bergel", "Mersetzdeitz",
            "Bright Arbor", "Scarlet Palace", "Holy Shadow", "Sacred Altar", "Bamboo Grove Hill",
            "Mardol", "Cyr", "Horn", "Gefn", "Bandis",
            "Himinn", "Andlangr", "Viblainn", "Hljod", "Skidbladnir" };
        public static string CastleName(int clientId) => clientId >= 1 && clientId <= 30 ? $"{CastleNames[clientId - 1]} ({CastleMap(clientId)})" : Lang.F("Castle #{0}", clientId);

        // ------------------------------------------------------------------ unit names (0x0368 → 0x0a30)
        private readonly Queue<uint> _nameQueue = new Queue<uint>();
        private readonly Dictionary<uint, float> _nameAsked = new Dictionary<uint, float>();
        private float _nextName;
        /// <summary>Asks for a unit's party / guild / title names (CZ_REQNAME2 0x0368; the unit must be within 14 cells, clif.cpp:11488).
        /// The same unit is not asked about twice within 3 s, unless <paramref name="changed"/>: its guild or the guild's roster
        /// changed since the last request, so that request's answer may be from before the change.
        /// (Port wave I, w2guild's "[Newbie]" plate: a player who joined a guild within 3 s of coming into view kept a plate
        /// without the position's name. The join's ZC_CHANGE_GUILD asked for the name, the 3 s rule dropped the request because
        /// the unit's arrival had asked 2.8 s earlier, and the name the server pushes at the join has no position: rAthena's
        /// guild_member_added calls clif_name_area before the roster with the new member has reached the map server, so
        /// guild_getposition finds nobody (rA:map/guild.cpp:592-602, 1105-1151; clif.cpp:9965-9972). The roster's own
        /// "ask again" was dropped by the same rule.) A request still waiting in the queue is enough: it is sent after
        /// the change.</summary>
        public void RequestName(uint aid, bool changed = false)
        {
            if (aid == 0 || _nameQueue.Contains(aid)) return;
            if (!changed && _nameAsked.TryGetValue(aid, out var at) && Time.time - at < 3f) return;
            _nameAsked[aid] = Time.time;
            _nameQueue.Enqueue(aid);
        }

        // ------------------------------------------------------------------ emblems
        private sealed class EmblemEntry { public int Version; public Sprite Sprite; public int AskedVersion = -1; public float AskedAt; }
        private readonly Dictionary<uint, EmblemEntry> _emblems = new Dictionary<uint, EmblemEntry>();
        /// <summary>The emblem of a guild, asking the server (0x0151) the first time a version is seen. Unit packets carry only the low
        /// 16 bits of the version (fullVersion false). Null while unknown or when the guild has none (0x0151 then gets no reply).</summary>
        public Sprite EmblemOf(uint gid, int version, bool fullVersion)
        {
            if (gid == 0) return null;
            if (version <= 0) return _emblems.TryGetValue(gid, out var known) ? known.Sprite : null; // version unknown (relations list): whatever is cached
            if (!_emblems.TryGetValue(gid, out var e)) _emblems[gid] = e = new EmblemEntry { Version = -1 };
            bool same = fullVersion ? e.Version == version : e.Version >= 0 && (e.Version & 0xFFFF) == (version & 0xFFFF);
            if (!same && (e.AskedVersion != version || Time.time - e.AskedAt > 30f) && _ready())
            {
                e.AskedVersion = version; e.AskedAt = Time.time;
                _s.SendPacket(new PacketWriter(0x0151).U32(gid).ToArray());
            }
            return e.Sprite;
        }
        public int EmblemsCached => _emblems.Values.Count(e => e.Sprite != null);

        private void StoreEmblem(uint gid, int ver, byte[] data)
        {
            try
            {
                var bmp = Unzlib(data);
                var img = IO.ImageDecoders.DecodeBmp(bmp, true);
                var tex = IO.ImageDecoders.ToTexture(img, false, "emblem_" + gid);
                tex.filterMode = FilterMode.Point; tex.wrapMode = TextureWrapMode.Clamp;
                if (!_emblems.TryGetValue(gid, out var e)) _emblems[gid] = e = new EmblemEntry();
                e.Version = ver; e.Sprite = UiKit.ToSprite(tex);
                Debug.Log($"[Guild] emblem of {gid} v{ver}: {img.Width}×{img.Height} from {data.Length} bytes");
                EmblemChanged?.Invoke(gid);
                if (gid == Id) Refresh();
            }
            catch (Exception ex) { Debug.LogWarning($"[Guild] emblem of {gid} v{ver} unreadable: {ex.Message}"); }
        }

        /// <summary>RFC 1950 zlib → bytes (DeflateStream wants the raw deflate part after the 2-byte header; the Adler-32 trailer is ignored).</summary>
        private static byte[] Unzlib(byte[] z)
        {
            using (var ms = new MemoryStream(z, 2, Math.Max(0, z.Length - 2), false))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            using (var o = new MemoryStream())
            {
                var buf = new byte[4096]; int k;
                while ((k = ds.Read(buf, 0, buf.Length)) > 0) { o.Write(buf, 0, k); if (o.Length > 65536) throw new InvalidDataException("emblem too large"); }
                return o.ToArray();
            }
        }

        /// <summary>A zlib stream made of stored (uncompressed) deflate blocks: what zlib's uncompress() on the server accepts (decode_zip, clif.cpp:14410).</summary>
        public static byte[] Zlib(byte[] data)
        {
            var o = new MemoryStream();
            o.WriteByte(0x78); o.WriteByte(0x01);
            int pos = 0;
            do
            {
                int len = Math.Min(65535, data.Length - pos); bool last = pos + len >= data.Length;
                o.WriteByte((byte)(last ? 1 : 0)); o.WriteByte((byte)len); o.WriteByte((byte)(len >> 8)); o.WriteByte((byte)~len); o.WriteByte((byte)(~len >> 8));
                o.Write(data, pos, len); pos += len;
            } while (pos < data.Length);
            uint a = 1, b = 0; foreach (var x in data) { a = (a + x) % 65521; b = (b + a) % 65521; }
            uint adler = (b << 16) | a;
            o.WriteByte((byte)(adler >> 24)); o.WriteByte((byte)(adler >> 16)); o.WriteByte((byte)(adler >> 8)); o.WriteByte((byte)adler);
            return o.ToArray();
        }

        // ---- client wave 4 (w4social): the emblem comes from the player's own folder
        /// <summary>Where the player puts emblem files: &lt;per-user folder&gt;\Emblems (World/UserSettings.Dir: %USERPROFILE%\AppData\LocalLow\Project Loki\Loki,
        /// or -lokiUserDir's) — never beside the game, which may be read-only.</summary>
        public static string EmblemFolder => Path.Combine(Loki.World.UserSettings.Dir, "Emblems");
        public sealed class EmblemFile { public string Path, Name, Problem; public byte[] Bytes; public Sprite Preview; public int Width, Height; }
        /// <summary>The BMP files in <see cref="EmblemFolder"/> (and an old emblem.bmp beside it), each checked as rAthena checks an upload:
        /// clif_validate_emblem (clif.cpp:14405-14431) inflates into an 1800-byte buffer, then wants "BM", bfSize equal to the file's
        /// length and bfOffBits inside it. The official client takes 24×24 bitmaps; the transparency limit (inter_athena.conf
        /// emblem_transparency_limit) is the server's own setting: its refusal comes back as a red message (map_msg 386 / 387).</summary>
        public List<EmblemFile> EmblemFiles()
        {
            var list = new List<EmblemFile>();
            try
            {
                var paths = new List<string>();
                if (Directory.Exists(EmblemFolder)) paths.AddRange(Directory.GetFiles(EmblemFolder, "*.bmp").OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                string single = Path.Combine(Loki.World.UserSettings.Dir, "emblem.bmp"); if (File.Exists(single)) paths.Add(single);
                foreach (var p in paths)
                {
                    var e = new EmblemFile { Path = p, Name = Path.GetFileName(p) };
                    try { e.Bytes = File.ReadAllBytes(p); e.Problem = CheckEmblem(e.Bytes); }
                    catch (Exception ex) { e.Problem = ex.Message; }
                    if (e.Bytes != null && e.Problem == null)
                    {
                        try { var img = IO.ImageDecoders.DecodeBmp(e.Bytes, true); e.Width = img.Width; e.Height = img.Height; var tex = IO.ImageDecoders.ToTexture(img, false, "emblemfile"); tex.filterMode = FilterMode.Point; e.Preview = UiKit.ToSprite(tex); if (img.Width != 24 || img.Height != 24) e.Problem = Lang.F("{0}×{1}: the official client draws 24×24 emblems", img.Width, img.Height); }
                        catch (Exception ex) { e.Problem = Lang.F("not a bitmap Loki can read: {0}", ex.Message); }
                    }
                    list.Add(e);
                }
            }
            catch (Exception ex) { Debug.LogWarning("[Guild] emblem folder: " + ex.Message); }
            return list;
        }
        /// <summary>null when rAthena's checks pass (clif.cpp:14405-14415), else why not.</summary>
        public static string CheckEmblem(byte[] f)
        {
            if (f == null || f.Length < 18) return Lang.T("too small to be a bitmap");
            if (f.Length > 1800) return Lang.F("{0} bytes: the server takes at most 1800 (a 24×24 bitmap)", f.Length);
            if (f[0] != 'B' || f[1] != 'M') return Lang.T("not a BMP file");
            if (BitConverter.ToInt32(f, 2) != f.Length) return Lang.T("the bitmap's size field doesn't match the file");
            if (BitConverter.ToInt32(f, 10) >= f.Length) return Lang.T("the bitmap's pixel offset is outside the file");
            return null;
        }

        /// <summary>CZ_REGISTER_GUILD_EMBLEM_IMG 0x0153 len.W zlib(bmp) (clif_parse_GuildChangeEmblem clif.cpp:14436-14466: the master only,
        /// not during WoE unless emblem_woe_change). Null bytes = a shield generated from the guild's name.</summary>
        public void UploadEmblem(byte[] bmp = null, string source = null)
        {
            if (!InGuild) return;
            bool shield = bmp == null; // w4lang: the generated shield has its own sentence (source stays the English note for the log and the smoke)
            if (bmp == null) { bmp = MakeShield(Name); source = "a generated shield"; }
            var z = Zlib(bmp);
            _s.SendPacket(new PacketWriter(0x0153).U16(0).Bytes(z).ToArrayWithLength());
            LastEmblemSource = source ?? "";
            _chat(shield ? Lang.T("Uploading a generated shield as the guild emblem…") : Lang.F("Uploading {0} as the guild emblem…", source), "c8cbd6"); Debug.Log($"[Guild] emblem upload {bmp.Length} bytes ({z.Length} zlib) from {source}");
        }
        /// <summary>The last emblem sent (smoke).</summary>
        public string LastEmblemSource { get; private set; } = "";

        /// <summary>A 24×24 24-bit BMP: a two-colour shield picked from the name's hash on a magenta (transparent) field.</summary>
        public static byte[] MakeShield(string name)
        {
            uint h = 2166136261; foreach (char c in name ?? "") { h ^= c; h *= 16777619; }
            Color32 a = Color.HSVToRGB((h & 0xFF) / 255f, 0.75f, 0.85f), c2 = Color.HSVToRGB(((h >> 8) & 0xFF) / 255f, 0.6f, 0.95f);
            int pattern = (int)((h >> 16) % 4);
            const int S = 24; var bmp = new byte[54 + S * S * 3];
            void W16(int o, int v) { bmp[o] = (byte)v; bmp[o + 1] = (byte)(v >> 8); }
            void W32(int o, int v) { W16(o, v); W16(o + 2, v >> 16); }
            bmp[0] = (byte)'B'; bmp[1] = (byte)'M'; W32(2, bmp.Length); W32(10, 54); W32(14, 40); W32(18, S); W32(22, S); W16(26, 1); W16(28, 24); W32(34, S * S * 3);
            for (int y = 0; y < S; y++)          // y = 0 is the top row; BMP rows are stored bottom-up
                for (int x = 0; x < S; x++)
                {
                    int half = y < 14 ? 10 : Mathf.Max(0, 10 - (y - 13)); // straight sides, then narrowing to a point
                    bool inside = y >= 1 && y <= 23 && Mathf.Abs(x - 11.5f) <= half + 0.5f;
                    bool edge = inside && (y == 1 || Mathf.Abs(x - 11.5f) > half - 0.5f || y == 23);
                    bool alt = pattern == 0 ? (x < 12) != (y < 12) : pattern == 1 ? x + y < 24 : pattern == 2 ? (x / 4) % 2 == 0 : Mathf.Abs(x - 11.5f) < 3 || Mathf.Abs(y - 10) < 3;
                    Color32 col = !inside ? new Color32(255, 0, 255, 255) : edge ? new Color32(30, 24, 20, 255) : alt ? a : c2;
                    int o = 54 + ((S - 1 - y) * S + x) * 3; bmp[o] = col.b; bmp[o + 1] = col.g; bmp[o + 2] = col.r;
                }
            return bmp;
        }

        // ------------------------------------------------------------------ per frame
        public void Tick()
        {
            if (!_ready()) return;
            if (_needLists && InGuild) { _needLists = false; RequestAll(); }
            if (_nameQueue.Count > 0 && Time.time >= _nextName)
            {
                _nextName = Time.time + 0.1f; // the map server takes at most 3 packets per socket per cycle (clif.cpp:25679)
                for (int i = 0; i < 2 && _nameQueue.Count > 0; i++) _s.SendPacket(new PacketWriter(0x0368).U32(_nameQueue.Dequeue()).ToArray());
            }
            if (_confirmKey != null && Time.time > _confirmUntil) { _confirmKey = null; Refresh(); }
        }

        // ------------------------------------------------------------------ the guild window
        public enum Page { Info, Members, Ranks, Skills, Relations, Log, Emblem }
        public Page Current { get; private set; } = Page.Info;
        public bool Visible => _win.Visible;
        private Member _pickFor; private int _rankSel = -1;
        private string _confirmKey; private float _confirmUntil;
        private static readonly string[] TabNames = { Lang.N("Info"), Lang.N("Members"), Lang.N("Ranks"), Lang.N("Skills"), Lang.N("Relations") };
        private static readonly int[] TaxSteps = { 0, 1, 2, 3, 5, 10, 15, 20, 30, 40, 50 };

        public void Show(Page page = Page.Info)
        {
            if (!InGuild) { _chat(Lang.T("You are not in a guild."), "ffb070"); Toast?.Invoke(Lang.T("You are not in a guild.")); return; }
            Current = page; _pickFor = null; _rankSel = -1; _win.Input.SetText("");
            RequestAll();
            _win.Open(_head()); Refresh();
        }
        public void Hide() { _win.SetInput(false); _win.Hide(); _pickFor = null; _rankSel = -1; }
        public void ShowPage(Page page) { Current = page; _pickFor = null; _rankSel = -1; _win.Input.SetText(""); Refresh(); }

        /// <summary>Two presses within 4 s: the first only arms the action.</summary>
        private bool Confirm(string key, string what)
        {
            if (_confirmKey == key && Time.time <= _confirmUntil) { _confirmKey = null; return true; }
            _confirmKey = key; _confirmUntil = Time.time + 4f;
            _chat(Lang.F("{0} — press again to confirm.", what), "ffd060"); Refresh();
            return false;
        }
        private string Armed(string key, string label) => _confirmKey == key ? Lang.T("Sure?") : label;

        private static string Rights(int mode) =>
            $"<color={((mode & PermInvite) != 0 ? "#7fd67f" : "#6a7080")}>{Lang.T("~right~invite")}</color> <color={((mode & PermExpel) != 0 ? "#7fd67f" : "#6a7080")}>{Lang.T("~right~expel")}</color> <color={((mode & PermStorage) != 0 ? "#7fd67f" : "#6a7080")}>{Lang.T("~right~storage")}</color>";

        public void Refresh()
        {
            if (!_win.Visible) return;
            if (!InGuild) { Hide(); return; }
            var rows = new List<ListWindow.Row>();
            _win.SetTabs(Array.ConvertAll(TabNames, Lang.T), (int)Current, t => ShowPage((Page)t));
            _win.SetTitle(Lang.F("Guild  {0}", Name));
            switch (Current)
            {
                case Page.Info: RefreshInfo(rows); break;
                case Page.Members: if (_pickFor != null) RefreshPicker(rows); else RefreshMembers(rows); break;
                case Page.Ranks: RefreshRanks(rows); break;
                case Page.Skills: RefreshSkills(rows); break;
                case Page.Relations: RefreshRelations(rows); break;
                case Page.Log: RefreshLog(rows); break;
                case Page.Emblem: RefreshEmblem(rows); break;
            }
            _win.SetRows(rows);
        }

        private void RefreshInfo(List<ListWindow.Row> rows)
        {
            _win.SetInfo(Lang.F("Level {0} · {1}/{2} online · average level {3}", Level, OnlineMembers, Math.Max(Capacity, Members.Count), AverageLevel));
            if (AmMaster) _win.SetInput(true, Lang.T("notice: subject | text"), null, SubmitNotice, _win.Input.Text.Length > 0 ? null : NoticeSubject + (NoticeText.Length > 0 ? " | " + NoticeText : ""), 181);
            else _win.SetInput(false);
            double pct = NextExp > 0 ? Exp * 100.0 / NextExp : 0;
            rows.Add(new ListWindow.Row { Icon = EmblemOf(Id, EmblemVersion, true), Text = AmMaster ? Lang.F("<color=#ffd060>{0}</color>  level {1}\n<size=19>Master {2} (you)</size>", Name, Level, MasterName) : Lang.F("<color=#ffd060>{0}</color>  level {1}\n<size=19>Master {2}</size>", Name, Level, MasterName), A = AmMaster ? Lang.T("Emblem") : null, OnA = () => ShowPage(Page.Emblem) });
            rows.Add(new ListWindow.Row { Text = Lang.F("Guild EXP {0:N0} / {1:N0}  <color=#9aa3b5>({2:0.0}%)</color>\n<size=19>Skill points {3}</size>", Exp, NextExp, pct, SkillPoints) });
            var me = Me;
            rows.Add(new ListWindow.Row { Text = me != null ? Lang.F("Your rank: <color=#ffd060>{0}</color>\n<size=19>{1} · tax paid {2:N0} EXP</size>", MyTitle.Length > 0 ? MyTitle : "?", Rights(MyRights), me.Tax) : Lang.F("Your rank: <color=#ffd060>{0}</color>\n<size=19>{1}</size>", MyTitle.Length > 0 ? MyTitle : "?", Rights(MyRights)) });
            rows.Add(new ListWindow.Row { Text = NoticeSubject.Length + NoticeText.Length > 0 ? Lang.F("Notice: <color=#b8f0b8>{0}</color>\n<size=19>{1}</size>", NoticeSubject, NoticeText) : Lang.T("<color=#9aa3b5>No guild notice.</color>"), A = AmMaster ? Lang.T("Post") : null, OnA = SubmitNotice });
            rows.Add(new ListWindow.Row { Text = Lang.F("Castles: {0}", Castles.Count == 0 ? ManageLand : string.Join(", ", Castles.Select(CastleName))) });
            foreach (var c in Castles)
            {
                int cid = c; string inv = CastleInvestment.TryGetValue(cid, out var ci) ? " · " + Lang.F("economy {0}, defense {1}", ci.economy, ci.defense) : "";
                rows.Add(new ListWindow.Row { Text = $"{CastleName(cid)}{inv}", A = Lang.T("Invest"), OnA = () => CastleInfo(cid), B = Lang.T("Go there"), OnB = () => CastleTeleport(cid) });
            }
            var footer = new List<(string, Action)> { (Lang.T("Refresh"), RequestAll), (Armed("leave", Lang.T("Leave")), () => { if (Confirm("leave", Lang.T("Leave the guild"))) Leave(); }) };
            if (AmMaster) footer.Add((Armed("disband", Lang.T("Disband")), () => { if (Confirm("disband", Lang.F("Disband '{0}'", Name))) Disband(); }));
            _win.SetFooter(footer.ToArray());
        }

        private void SubmitNotice()
        {
            if (!AmMaster) return;
            string t = _win.Input.Text ?? ""; int bar = t.IndexOf('|');
            string subject = (bar >= 0 ? t.Substring(0, bar) : t).Trim(), text = bar >= 0 ? t.Substring(bar + 1).Trim() : "";
            SetNotice(subject, text); _win.Input.SetText("");
            _chat(Lang.T("Posting the guild notice…"), "c8cbd6");
        }

        private void RefreshMembers(List<ListWindow.Row> rows)
        {
            if (CanInvite) _win.SetInput(true, Lang.T("invite: character name"), null, () => { InviteByName(_win.Input.Text); _win.Input.SetText(""); }, null, 23);
            else _win.SetInput(false);
            _win.SetInfo(CanInvite ? Lang.F("{0} members · invite:", Members.Count) : Lang.F("{0} members, {1} online", Members.Count, OnlineMembers));
            uint self = _selfCid();
            for (int i = 0; i < Members.Count; i++)
            {
                var m = Members[i]; bool me = m.Cid == self, master = m.Cid == MasterCid || (MasterCid == 0 && i == 0);
                string seen = m.Online ? Lang.T("<color=#7fd67f>online</color>") : m.LastLogin > 0 ? Lang.F("last seen {0:d MMM}", DateTimeOffset.FromUnixTimeSeconds(m.LastLogin).LocalDateTime) : Lang.T("<color=#6a7080>offline</color>");
                string title = m.Position >= 0 && m.Position < 20 ? Ranks[m.Position].Title : "";
                var mm = m;
                rows.Add(new ListWindow.Row
                {
                    Text = $"{(master ? "<color=#ffd060>★</color> " : "")}{m.Name}{(me ? " " + Lang.T("<color=#9aa3b5>(you)</color>") : "")}  " + Lang.F("<color=#9aa3b5>Lv {0} {1}</color>\n<size=19>{2} · {3} · tax {4:N0} EXP</size>", m.Level, LoginPanel.JobName(m.Job), title, seen, m.Tax),
                    Highlight = me,
                    A = AmMaster && !me ? Lang.T("Rank") : null, OnA = () => { _pickFor = mm; Refresh(); },
                    B = CanExpel && !me && !master ? Armed("expel:" + m.Cid, Lang.T("Expel")) : null, OnB = () => { if (Confirm("expel:" + mm.Cid, Lang.F("Expel {0}", mm.Name))) Expel(mm); },
                });
            }
            if (Members.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>Loading the member list…</color>") });
            if (CanInvite)
                _win.SetFooter((Lang.T("Invite"), () => { InviteByName(_win.Input.Text); _win.Input.SetText(""); }),
                               (Lang.T("Invite gazed"), () => { var u = Gazed?.Invoke(); if (u != null) InviteByAid(u.Id); else Toast?.Invoke(Lang.T("Look at a player first (gaze lock).")); }));
            else _win.SetFooter();
        }

        private void RefreshPicker(List<ListWindow.Row> rows)
        {
            var m = _pickFor; _win.SetInput(false);
            _win.SetInfo(Lang.F("Choose {0}'s rank. The first one hands the guild over to them.", m.Name));
            for (int i = 0; i < 20; i++)
            {
                int rank = i; var rk = Ranks[i];
                rows.Add(new ListWindow.Row
                {
                    Text = i == 0 ? Lang.F("{0}. {1}  <color=#ff8a7a>(new guild master)</color>\n<size=19>{2} · tax {3}%</size>", i + 1, rk.Title, Rights(rk.Mode), rk.Tax) : Lang.F("{0}. {1}\n<size=19>{2} · tax {3}%</size>", i + 1, rk.Title, Rights(rk.Mode), rk.Tax), Highlight = m.Position == i,
                    A = m.Position == i ? null : i == 0 ? Armed("master:" + m.Cid, Lang.T("Hand over")) : Lang.T("~rank~Set"),
                    OnA = () => { if (rank == 0 && !Confirm("master:" + m.Cid, Lang.F("Make {0} the guild master", m.Name))) return; SetMemberRank(m, rank); _pickFor = null; Refresh(); },
                });
            }
            _win.SetFooter((Lang.T("Back"), () => { _pickFor = null; Refresh(); }));
        }

        private void RefreshRanks(List<ListWindow.Row> rows)
        {
            bool edit = AmMaster && _rankSel >= 0 && _rankSel < 20;
            if (edit) _win.SetInput(true, Lang.T("rank title"), null, () => { var rk = Ranks[_rankSel]; SetRank(rk.Id, _win.Input.Text.Trim(), rk.Mode, rk.Tax); }, Ranks[_rankSel].Title, 23);
            else _win.SetInput(false);
            _win.SetInfo(edit ? Lang.F("Rank {0}: type a title (Enter saves)", _rankSel + 1) : AmMaster ? Lang.T("Pick a rank to rename it or change its rights and tax.") : Lang.T("Ranks, their rights and EXP tax."));
            for (int i = 0; i < 20; i++)
            {
                int rank = i; var rk = Ranks[i]; int count = Members.Count(m => m.Position == i);
                rows.Add(new ListWindow.Row
                {
                    Text = count > 0 ? Lang.F("{0}. {1}  <color=#9aa3b5>({2})</color>\n<size=19>{3} · tax {4}%</size>", i + 1, rk.Title, count, Rights(rk.Mode), rk.Tax) : Lang.F("{0}. {1}\n<size=19>{2} · tax {3}%</size>", i + 1, rk.Title, Rights(rk.Mode), rk.Tax), Highlight = i == _rankSel,
                    A = AmMaster ? (i == _rankSel ? Lang.T("Done") : Lang.T("Edit")) : null, OnA = () => { _rankSel = _rankSel == rank ? -1 : rank; _win.Input.SetText(""); Refresh(); },
                });
            }
            if (!edit) { _win.SetFooter(); return; }
            var r = Ranks[_rankSel];
            (string, Action) Toggle(string on, string off, int bit) => ((r.Mode & bit) != 0 ? on : off, () => SetRank(r.Id, r.Title, r.Mode ^ bit, r.Tax)); // w4lang: a whole label per state
            int nextTax = TaxSteps.FirstOrDefault(t => t > r.Tax); if (nextTax <= r.Tax) nextTax = 0;
            var tax = (Lang.F("Tax {0}% → {1}%", r.Tax, nextTax), (Action)(() => SetRank(r.Id, r.Title, r.Mode, nextTax)));
            // rank 0 keeps every right: the master is not special-cased for expelling (guild.cpp:1203), so taking them away would lock the master out
            if (_rankSel == 0) _win.SetFooter(tax);
            else _win.SetFooter(tax, Toggle(Lang.T("Storage on"), Lang.T("Storage off"), PermStorage), Toggle(Lang.T("Expel on"), Lang.T("Expel off"), PermExpel), Toggle(Lang.T("Invite on"), Lang.T("Invite off"), PermInvite));
        }

        private void RefreshSkills(List<ListWindow.Row> rows)
        {
            _win.SetInput(false);
            _win.SetInfo(AmMaster ? Lang.F("Skill points {0}", SkillPoints) : Lang.F("Skill points {0} · only the master raises and uses guild skills", SkillPoints));
            foreach (var sk in Skills)
            {
                var info = SkillDb.Get(sk.Id); int id = sk.Id;
                bool active = sk.Inf != 0; bool usable = active && sk.Level > 0 && (AmMaster || sk.Id == 10018); // GD_CHARGESHOUT_BEATING is the only one for members (clif.cpp:12972)
                rows.Add(new ListWindow.Row
                {
                    Icon = WristMenu.SkillIcon(info),
                    Text = $"{info.Name}  " + Lang.F("<color=#9aa3b5>Lv {0}/{1}</color>", sk.Level, info.MaxLevel) + $"\n<size=19>{(active ? Lang.T("active · guild maps only") : Lang.T("passive"))}{(sk.Sp > 0 ? $" · {sk.Sp} SP" : "")}</size>",
                    A = AmMaster && sk.Upgradable ? Lang.T("Level up") : null, AEnabled = SkillPoints > 0, OnA = () => SkillUp(id),
                    B = usable ? Lang.T("Use") : null, OnB = () => UseSkill(id),
                });
            }
            if (Skills.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>Loading the guild skills…</color>") });
            _win.SetFooter();
        }

        /// <summary>Client wave 4: the master picks the emblem among the BMP files of the player's Emblems folder (or a generated shield).</summary>
        private void RefreshEmblem(List<ListWindow.Row> rows)
        {
            _win.SetInput(false);
            _win.SetInfo(Lang.T("The guild's emblem: a 24×24 BMP file (magenta = transparent)."));
            rows.Add(new ListWindow.Row { Icon = EmblemOf(Id, EmblemVersion, true), Text = Lang.F("Now: <color=#ffd060>{0}</color>'s emblem (version {1})\n<size=17><color=#9aa3b5>Your files: {2}</color></size>", Name, EmblemVersion, EmblemFolder) });
            foreach (var e in EmblemFiles())
            {
                var ef = e;
                rows.Add(new ListWindow.Row
                {
                    Icon = e.Preview,
                    Text = $"{e.Name}\n<size=19>{(e.Problem == null ? $"<color=#7fd67f>{e.Bytes.Length} bytes, {e.Width}×{e.Height}</color>" : $"<color=#ff8a7a>{e.Problem}</color>")}</size>",
                    A = Lang.T("Use"), AEnabled = e.Problem == null && AmMaster, OnA = () => { UploadEmblem(ef.Bytes, ef.Name); ShowPage(Page.Info); },
                });
            }
            rows.Add(new ListWindow.Row { Text = Lang.T("A shield made from the guild's name"), A = Lang.T("Use"), AEnabled = AmMaster, OnA = () => { UploadEmblem(); ShowPage(Page.Info); } });
            _win.SetFooter((Lang.T("Back"), () => ShowPage(Page.Info)), (Lang.T("Open folder"), () => { try { Directory.CreateDirectory(EmblemFolder); Application.OpenURL("file:///" + EmblemFolder.Replace('\\', '/')); }
                catch (Exception ex) { _chat(Lang.F("The Emblems folder could not be opened: {0}", ex.Message), "ffb070"); } })); // w4lang: the catch on its own line (check_strings.py takes the "//" of "file:///" for a comment)
        }

        private void RefreshLog(List<ListWindow.Row> rows)
        {
            _win.SetInput(false);
            _win.SetInfo(Lang.F("Guild storage history — {0} moves, newest first", StorageLog.Count));
            foreach (var e in StorageLog)
            {
                var info = GameDb.GetItem(e.ItemId);
                rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(info), Text = $"{(e.Put ? Lang.T("<color=#7fd67f>put in</color>") : Lang.T("<color=#ffb070>took out</color>"))} {(e.Refine > 0 ? "+" + e.Refine + " " : "")}{info.Name} x{e.Amount}\n<size=19>{e.Name} · {e.Time}</size>" });
            }
            _win.SetFooter((Lang.T("Back"), () => ShowPage(Page.Info)));
        }

        private void RefreshRelations(List<ListWindow.Row> rows)
        {
            _win.SetInput(false);
            _win.SetInfo(AmMaster ? Lang.T("Allies and enemies. Look at a player of another guild to ask.") : Lang.T("Allies and enemies."));
            foreach (var rel in Relations.OrderBy(x => x.Kind))
            {
                var rr = rel;
                rows.Add(new ListWindow.Row
                {
                    Icon = EmblemOf(rel.GuildId, -1, true),
                    Text = $"{(rel.Kind == 0 ? Lang.T("<color=#7fd67f>Ally</color>") : Lang.T("<color=#ff8a7a>Enemy</color>"))}  {rel.Name}",
                    B = AmMaster ? Armed("rel:" + rel.GuildId, Lang.T("Remove")) : null, OnB = () => { if (Confirm("rel:" + rr.GuildId, rr.Kind == 0 ? Lang.F("End the alliance with {0}", rr.Name) : Lang.F("End the rivalry with {0}", rr.Name))) RemoveRelation(rr); },
                });
            }
            if (Relations.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>No allies or enemies.</color>") });
            if (Expulsions.Count > 0)
            {
                rows.Add(new ListWindow.Row { Text = Lang.T("<color=#ffd060>Expelled members</color>") });
                foreach (var e in Expulsions) rows.Add(new ListWindow.Row { Text = $"{e.Name}{(e.Reason.Length > 0 ? $"  <color=#9aa3b5>— {e.Reason}</color>" : "")}" });
            }
            if (AmMaster)
                _win.SetFooter((Lang.T("Enemy gazed"), () => { var u = Gazed?.Invoke(); if (u != null) RequestHostile(u.Id); else Toast?.Invoke(Lang.T("Look at a player of the other guild first.")); }),
                               (Lang.T("Ally gazed"), () => { var u = Gazed?.Invoke(); if (u != null) RequestAlly(u.Id); else Toast?.Invoke(Lang.T("Look at a player of the other guild first.")); }));
            else _win.SetFooter();
        }
    }
}
