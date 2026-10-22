using System;
using System.Collections.Generic;
using System.IO;
using Loki.IO;
using Loki.Net;
using Loki.Sprites;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Memorial dungeons, battlegrounds and duels (client wave 2; rAthena e985006, research/instances_bg_duel_night.md).
    /// Memorial dungeons (to every party member): ZC_MEMORIALDUNGEON_SUBSCRIPTION_INFO 0x02cb name[61] queuePos.W; _SUBSCRIPTION_NOTIFY 0x02cc
    ///   queuePos.W (0xFFFF = none / removed); ZC_MEMORIALDUNGEON_INFO 0x02cd name[61] keepLimit.L idleLimit.L (UNIX times, 0 = none; at
    ///   creation a first one has keep 0, then the real one); ZC_MEMORIALDUNGEON_NOTIFY 0x02ce type.L limit.L (0 someone is inside: the idle
    ///   timer stopped, 1 time limit over, 2 closed for being empty, 3 destroyed on request); CZ_MEMORIALDUNGEON_COMMAND 0x02cf command.L
    ///   (3 = destroy: the leader, not standing in the dungeon). The info comes again after CZ_BLOCKING_PLAY_CANCEL 0x0447 at map load.
    /// Battlegrounds: queue CZ_REQ_ENTRY_QUEUE_APPLY 0x08d7 type.W (1 solo, 2 party, 4 guild) name[24] → ZC_ACK_ENTRY_QUEUE_APPLY 0x08d8
    ///   result.B name[24] (1 queued; 0 refused, 2 time out, 3 bad name, 4 invalid, 5 too many, 6 level, 7 already queued, 8 no queue,
    ///   9 not the leader, 10 job) + ZC_NOTIFY_ENTRY_QUEUE_APPLY 0x08d9 name[24] queued.L (both teams together); ready: ZC_NOTIFY_LOBBY_ADMISSION
    ///   0x08df name[24] lobby[24] → CZ_REPLY_LOBBY_ADMISSION 0x08e0 result.B (1 accept, 2 decline) name[24] lobby[24] (within 20 s) →
    ///   ZC_REPLY_ACK_LOBBY_ADMISSION 0x08e1 result.B map[24] map[24]; ZC_ENTRY_QUEUE_INIT 0x090e resets the queue; cancel CZ 0x08da name[24]
    ///   → ZC_ACK_ENTRY_QUEUE_CANCEL 0x08db ok.B name[24]; CZ 0x090a name[24] asks for the count again. The names come from the client
    ///   (data/account/battlegrounds.json, made from db/battleground_db.yml).
    ///   On the field: ZC_BATTLEFIELD_NOTIFY_CAMPINFO 0x02dd aid.L name[24] camp.W (the team id: the unit packets don't carry it; anyone in
    ///   another camp is an enemy), ZC_BATTLEFIELD_NOTIFY_POINT 0x02de lion.W eagle.W, _POSITION 0x02df aid.L name[24] class.W x.W y.W
    ///   (teammates; x = y = 0xFFFF removes), _HP2 0x0a0e aid.L hp.L maxHp.L (teammates), ZC_BATTLEFIELD_CHAT 0x02dc len.W aid.L name[24]
    ///   "name : text"; our team chat: CZ_BATTLEFIELD_CHAT 0x02db len.W "own name : text" (a wrong name gets the player kicked).
    /// Duels (client wave 4, w4social — the re-audit's §3 leak closed): whether we are in one is read from ZC_MAPPROPERTY_R2 0x099b alone
    ///   (type 1 with the "attack non-party players" bit and without the PvP counter bit: <see cref="PvpState.Duel"/>, fed by
    ///   <see cref="SetDuel"/>); everything the protocol has no packet for (create / invite / accept / reject / leave, and the invitation
    ///   itself) goes through <see cref="Net.IDuelChannel"/> — rAthena's @commands and its one invitation text today (Net/IDuelChannel.cs).
    ///   Attacking is the normal attack. No server text is parsed here any more.
    /// </summary>
    public sealed class Modes
    {
        // ---- memorial dungeon
        public string Dungeon { get; private set; } = "";
        public int QueuePos { get; private set; } = -1;
        public long KeepUntil, IdleUntil; public int LastNotify = -1; public int InfoCount;
        public bool HasDungeon => Dungeon.Length > 0;
        /// <summary>Client wave 21 (w21map), point 47: a dungeon the client keeps to itself (the tutorial's private map, made and closed by
        /// the client's own requests): no notice about it, and the Modes page does not list it.</summary>
        public Func<string, bool> Quiet;
        public bool QuietDungeon => HasDungeon && Quiet != null && Quiet(Dungeon);
        public bool HasShownDungeon => HasDungeon && !QuietDungeon;
        // ---- battleground
        public sealed class Battleground { public int Id; public string Name; public int MinPlayers, MinLevel, MaxLevel; }
        public readonly List<Battleground> Battlegrounds = new List<Battleground>();
        public string Queued { get; private set; } = ""; public int QueuedCount; public int LastApply = -1, LastCancel = -1, LastAdmission = -1;
        public string LobbyOffer { get; private set; } = "";
        private float _lobbyUntil;
        public readonly Dictionary<uint, int> Camps = new Dictionary<uint, int>();
        public readonly Dictionary<uint, (string name, int job, int x, int y)> Mates = new Dictionary<uint, (string, int, int, int)>();
        public readonly Dictionary<uint, (int hp, int max)> MateHp = new Dictionary<uint, (int, int)>();
        public int ScoreLion = -1, ScoreEagle = -1; public readonly List<string> BgChat = new List<string>();
        public int MyCamp => Camps.TryGetValue(_self(), out int c) ? c : 0;
        public bool OnBattlefield => MyCamp != 0;
        // ---- duel
        public bool InDuel { get; private set; }
        public string DuelInviter { get; private set; } = "";
        public string LastDuelText = "";
        /// <summary>The names in our duel that we know of (the one who invited us, the ones we invited): their names turn red.</summary>
        public readonly List<string> DuelNames = new List<string>();
        public event Action Changed;
        public Action<string> Toast; public Action<string> Say; public Func<uint> SelfAid; public Func<string> SelfName;
        /// <summary>Client wave 4: the duel adapter (Net/IDuelChannel.cs) and the chat line of a kind (the battlefield chat's tab).</summary>
        public Net.IDuelChannel Duels; public Action<string, string, ChatKind> ChatAs;

        private readonly RoSession _s; private readonly EntityManager _ents; private readonly Action<string, string> _chat;
        private readonly Func<uint> _self;

        public Modes(RoSession s, EntityManager ents, Func<uint> self, Action<string, string> chat)
        {
            _s = s; _ents = ents; _self = self; _chat = chat;
            try
            {
                foreach (var o in MiniJson.AsList(MiniJson.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "data", "account", "battlegrounds.json")))))
                {
                    var d = MiniJson.AsObject(o);
                    Battlegrounds.Add(new Battleground { Id = MiniJson.Int(d["id"]), Name = MiniJson.Str(d["name"], ""), MinPlayers = MiniJson.Int(d["minPlayers"]), MinLevel = MiniJson.Int(d["minLevel"]), MaxLevel = MiniJson.Int(d["maxLevel"]) });
                }
            }
            catch (Exception e) { Debug.LogWarning("[Modes] data/account/battlegrounds.json: " + e.Message); }
            _ents.UnitAdded += Decorate;
        }

        public void ResetForCharacter() { Dungeon = ""; QueuePos = -1; KeepUntil = IdleUntil = 0; Queued = ""; LobbyOffer = ""; InDuel = false; DuelInviter = ""; DuelNames.Clear(); Camps.Clear(); Mates.Clear(); MateHp.Clear(); ScoreLion = ScoreEagle = -1; Changed?.Invoke(); }
        /// <summary>Map change: camps are sent again on the new map; leaving the field ends the team. The duel state is not touched here:
        /// rAthena ends a duel on any warp, even on the same map (unit_remove_map_ calls duel_leave, unit.cpp:3687-3688), and the new map's
        /// 0x099b at LoadEndAck (clif.cpp:10828-10847) says so — the client follows that packet only.</summary>
        public void OnMapChange() { Camps.Clear(); Mates.Clear(); MateHp.Clear(); ScoreLion = ScoreEagle = -1; Changed?.Invoke(); }

        private static string Name61(ref PacketReader r) => r.Str(61);

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x02cb: { r.Skip(2); Dungeon = Name61(ref r); QueuePos = r.U16(); if (!QuietDungeon) Note(Lang.F("Memorial dungeon '{0}' is being prepared (queue {1}).", Dungeon, QueuePos)); Log(); Changed?.Invoke(); return true; }
                case 0x02cc:
                {
                    r.Skip(2); int pos = r.U16();
                    if (pos == 0xFFFF) { if (!QuietDungeon) Note(Dungeon.Length > 0 ? Lang.F("The memorial dungeon '{0}' could not be made.", Dungeon) : Lang.T("No memorial dungeon.")); Dungeon = ""; QueuePos = -1; }
                    else QueuePos = pos;
                    Log(); Changed?.Invoke(); return true;
                }
                case 0x02cd:
                {
                    r.Skip(2); Dungeon = Name61(ref r); KeepUntil = r.U32(); IdleUntil = r.U32(); QueuePos = -1; InfoCount++;
                    Log(); Changed?.Invoke(); return true;
                }
                case 0x02ce:
                {
                    r.Skip(2); LastNotify = r.S32(); long limit = r.U32();
                    if (LastNotify == 0) { IdleUntil = 0; } // someone went in: the idle timer stopped
                    else
                    {
                        string why = LastNotify == 1 ? Lang.F("The memorial dungeon '{0}' is gone: its time ran out.", Dungeon) : LastNotify == 2 ? Lang.F("The memorial dungeon '{0}' is gone: nobody was inside for too long.", Dungeon)
                                   : LastNotify == 3 ? Lang.F("The memorial dungeon '{0}' is gone: it was closed on request.", Dungeon) : Lang.F("The memorial dungeon '{0}' is gone: it could not be made.", Dungeon); // w4lang: a whole sentence per reason
                        if (!QuietDungeon) Note(why);
                        Dungeon = ""; QueuePos = -1; KeepUntil = IdleUntil = 0;
                    }
                    Log(); Changed?.Invoke(); return true;
                }
                // ---- battleground queue
                case 0x08d8:
                {
                    r.Skip(2); LastApply = r.U8(); string nm = r.Str(24);
                    if (LastApply == 1) { Queued = nm; Note(Lang.F("Queued for {0}.", nm)); }
                    else if (LastApply == 2) { Queued = ""; LobbyOffer = ""; Note(Lang.F("{0}: not everyone accepted in time.", nm)); }
                    else Note($"{nm}: {ApplyText(LastApply)}");
                    Debug.Log($"[Modes] queue apply '{nm}' → {LastApply}"); Changed?.Invoke(); return true;
                }
                case 0x08d9: { r.Skip(2); string nm = r.Str(24); QueuedCount = r.S32(); Queued = nm; Debug.Log($"[Modes] queue '{nm}': {QueuedCount} waiting"); Changed?.Invoke(); return true; }
                case 0x08db: { r.Skip(2); LastCancel = r.U8(); string nm = r.Str(24); if (LastCancel != 0) { Queued = ""; Note(Lang.F("You left the queue for {0}.", nm)); } Debug.Log($"[Modes] queue cancel '{nm}' → {LastCancel}"); Changed?.Invoke(); return true; }
                case 0x08df:
                {
                    r.Skip(2); LobbyOffer = r.Str(24); r.Str(24); _lobbyUntil = Time.time + 20f;
                    Note(Lang.F("{0} is ready — accept within 20 seconds (wrist menu, Modes).", LobbyOffer));
                    Debug.Log($"[Modes] lobby admission for '{LobbyOffer}'"); Changed?.Invoke(); return true;
                }
                case 0x08e1: { r.Skip(2); LastAdmission = r.U8(); string map = r.Str(24); Debug.Log($"[Modes] admission ack {LastAdmission} ({map})"); Changed?.Invoke(); return true; }
                case 0x090e: { Queued = ""; LobbyOffer = ""; QueuedCount = 0; Debug.Log("[Modes] queue reset"); Changed?.Invoke(); return true; }
                // ---- battleground field
                case 0x02dd:
                {
                    r.Skip(2); uint aid = r.U32(); string nm = r.Str(24); int camp = r.U16();
                    Camps[aid] = camp; Decorate(_ents.Get(aid));
                    if (aid == _self()) { foreach (var u in _ents.Units.Values) Decorate(u); Note(Lang.F("You are in camp {0}.", camp)); }
                    Debug.Log($"[Modes] camp of {nm} ({aid}) = {camp}"); Changed?.Invoke(); return true;
                }
                case 0x02de: { r.Skip(2); ScoreLion = r.U16(); ScoreEagle = r.U16(); Debug.Log($"[Modes] score {ScoreLion} : {ScoreEagle}"); Changed?.Invoke(); return true; }
                case 0x02df:
                {
                    r.Skip(2); uint aid = r.U32(); string nm = r.Str(24); int job = r.U16(); int x = r.U16(), y = r.U16();
                    if (x == 0xFFFF && y == 0xFFFF) Mates.Remove(aid); else Mates[aid] = (nm, job, x, y);
                    Changed?.Invoke(); return true;
                }
                case 0x0a0e: { r.Skip(2); uint aid = r.U32(); int hp = r.S32(), max = r.S32(); MateHp[aid] = (hp, max); var u = _ents.Get(aid); if (u?.Overhead != null && !u.IsSelf) u.Overhead.SetHp(max > 0 ? Mathf.Clamp01(hp / (float)max) : -1f, true); return true; }
                case 0x02dc:
                {
                    r.Skip(4); uint aid = r.U32(); string nm = r.Str(24); string text = r.ChatLine(n - 32);
                    BgChat.Add(text); if (BgChat.Count > 40) BgChat.RemoveAt(0);
                    if (ChatAs != null) ChatAs(Lang.F("[Battle] {0}", text), "ffc080", ChatKind.Battle); else _chat(Lang.F("[Battle] {0}", text), "ffc080");
                    var (who, said) = SocialHub.Split(text); if (who != null) _ents.Get(aid)?.Overhead?.SetSpeech(said);
                    return true;
                }
                default: return false;
            }
        }

        private void Log() => Debug.Log($"[Modes] dungeon '{Dungeon}' queue {QueuePos} keep {KeepUntil} idle {IdleUntil} notify {LastNotify}");
        private void Note(string s) { _chat(s, "ffd9a8"); Toast?.Invoke(s); }

        public static string ApplyText(int r)
        {
            switch (r)
            {
                case 0: return Lang.T("you can't apply right now.");
                case 3: return Lang.T("no battleground by that name.");
                case 4: return Lang.T("that kind of application isn't possible.");
                case 5: return Lang.T("too many members.");
                case 6: return Lang.T("your level doesn't fit.");
                case 7: return Lang.T("you are already queued.");
                case 8: return Lang.T("no queue to join.");
                case 9: return Lang.T("only the party leader / guild master can apply.");
                case 10: return Lang.T("your job can't join.");
                default: return Lang.F("refused ({0}).", r);
            }
        }

        /// <summary>Teams on the field: our camp green, the other red on the name plate (the official client colours them the same way).</summary>
        private void Decorate(Unit u)
        {
            if (u == null || u.Overhead == null || u.IsSelf || !Camps.TryGetValue(u.Id, out int camp) || MyCamp == 0) return;
            u.Overhead.SetNameColor(camp == MyCamp ? new Color(0.55f, 1f, 0.6f) : new Color(1f, 0.45f, 0.4f));
        }

        /// <summary>On a battlefield the camp decides who can be hit (battle_check_target: another bg_id is an enemy); null elsewhere.</summary>
        public bool? BgHostile(Unit u)
        {
            if (u == null || !u.IsPlayerType || u.IsSelf || MyCamp == 0) return null;
            return Camps.TryGetValue(u.Id, out int c) && c != MyCamp;
        }

        // ---- requests
        public void DestroyDungeon() { _s.SendPacket(new PacketWriter(0x02cf).U32(3).ToArray()); Debug.Log("[Modes] destroy the memorial dungeon"); }
        public void Apply(string bg, int type = 1) { _s.SendPacket(new PacketWriter(0x08d7).U16((ushort)type).Str(bg, 24).ToArray()); Debug.Log($"[Modes] apply {bg} type {type}"); }
        public void CancelQueue() { if (Queued.Length > 0) { _s.SendPacket(new PacketWriter(0x08da).Str(Queued, 24).ToArray()); Debug.Log("[Modes] cancel queue " + Queued); } }
        public void AskQueueCount() { if (Queued.Length > 0) _s.SendPacket(new PacketWriter(0x090a).Str(Queued, 24).ToArray()); }
        public void AnswerLobby(bool accept)
        {
            if (LobbyOffer.Length == 0) return;
            _s.SendPacket(new PacketWriter(0x08e0).U8((byte)(accept ? 1 : 2)).Str(LobbyOffer, 24).Str(LobbyOffer, 24).ToArray());
            Debug.Log($"[Modes] lobby {(accept ? "accept" : "decline")} {LobbyOffer}");
            LobbyOffer = ""; Changed?.Invoke();
        }
        public bool LobbyPending => LobbyOffer.Length > 0 && Time.time < _lobbyUntil;
        /// <summary>Team chat on the field: "own name : text" (clif.cpp:10442-10507 kicks anything else).</summary>
        public void TeamSay(string text)
        {
            string t = (SelfName?.Invoke() ?? "") + " : " + text;
            _s.SendPacket(new PacketWriter(0x02db).U16(0).Str(t, RoText.Encode(t).Length + 1).ToArrayWithLength());
        }

        // ---- duels (client wave 4: the protocol's 0x099b for the state, Net/IDuelChannel for the rest)
        /// <summary>0x099b told whether we are in a duel (PvpState.Duel).</summary>
        public void SetDuel(bool inDuel)
        {
            if (inDuel == InDuel) return;
            InDuel = inDuel;
            if (inDuel) DuelInviter = ""; else DuelNames.Clear();
            Debug.Log($"[Modes] in a duel: {inDuel}");
            Changed?.Invoke();
        }
        /// <summary>The adapter heard an invitation.</summary>
        public void OnInvited(string who)
        {
            DuelInviter = who ?? "?"; LastDuelText = $"{DuelInviter} challenges you to a duel.";
            if (!DuelNames.Contains(DuelInviter)) DuelNames.Add(DuelInviter);
            Toast?.Invoke(Lang.F("{0} challenges you to a duel (wrist menu, Modes).", DuelInviter)); Changed?.Invoke();
        }
        /// <summary>A duel with just this player: rAthena "@duel Name" creates a two-player duel and invites them (atcommand.cpp @duel).</summary>
        public void DuelChallenge(string name) { if (string.IsNullOrWhiteSpace(name) || Duels == null) return; if (!DuelNames.Contains(name)) DuelNames.Add(name); if (InDuel) Duels.Invite(name); else Duels.Invite(name, true); }
        public void DuelStart() { Duels?.Create(); }
        public void DuelInvite(string name) { if (!string.IsNullOrEmpty(name) && Duels != null) { if (!DuelNames.Contains(name)) DuelNames.Add(name); Duels.Invite(name); } }
        public void DuelAccept() { Duels?.Accept(); }
        public void DuelReject() { Duels?.Reject(); if (DuelInviter.Length > 0) DuelNames.Remove(DuelInviter); DuelInviter = ""; Changed?.Invoke(); }
        public void DuelLeave() { Duels?.Leave(); }

        public string Summary()
        {
            var sb = new System.Text.StringBuilder();
            if (HasShownDungeon)
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                // w4lang: every line is a whole text (the queue clause, the plural and the score are separate versions or " · " parts)
                sb.Append(QueuePos >= 0 ? Lang.F("Memorial dungeon <color=#ffd060>{0}</color> — queued ({1})", Dungeon, QueuePos) : Lang.F("Memorial dungeon <color=#ffd060>{0}</color>", Dungeon));
                if (KeepUntil > 0) { long l = KeepUntil - now; sb.Append("\n  ").Append(Lang.F("closes in {0} min", Mathf.Max(0, (int)(l / 60)))); }
                if (IdleUntil > 0) { long l = IdleUntil - now; sb.Append("\n  ").Append(Lang.F("<color=#ff8a7a>empty: goes away in {0}:{1:00}</color>", Mathf.Max(0, (int)l / 60), Mathf.Max(0, (int)(l % 60)))); }
                sb.Append('\n');
            }
            else sb.Append(Lang.T("No memorial dungeon.")).Append('\n');
            if (Queued.Length > 0) sb.Append('\n').Append(Lang.F("Queued for <color=#ffd060>{0}</color> ({1} waiting)", Queued, QueuedCount)).Append('\n');
            if (LobbyPending) sb.Append('\n').Append(Lang.F("<color=#ffd060>{0} is ready!</color> Accept within {1} s.", LobbyOffer, Mathf.CeilToInt(_lobbyUntil - Time.time))).Append('\n');
            if (OnBattlefield) sb.Append('\n').Append(Lang.F("On the battlefield: camp {0}", MyCamp)).Append(ScoreLion >= 0 ? " · " + Lang.F("score {0} : {1}", ScoreLion, ScoreEagle) : "")
                                 .Append(" · ").Append(Mates.Count == 1 ? Lang.T("1 teammate here") : Lang.F("{0} teammates here", Mates.Count)).Append('\n');
            if (InDuel) sb.Append('\n').Append(Lang.T("<color=#ff8a7a>In a duel</color> — the laser on a duelist attacks.")).Append('\n');
            if (DuelInviter.Length > 0) sb.Append('\n').Append(Lang.F("<color=#ffd060>{0} challenges you to a duel.</color>", DuelInviter)).Append('\n');
            return sb.ToString();
        }
    }
}
