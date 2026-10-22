using System.Collections.Generic;
using Loki.Game;
using Loki.Net;
using Loki.Sprites;
using Loki.UI;
using Loki.World;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Wave 28 (wcxp): the Codex's bounties. design note, 2026-10-15 (through the orchestrator, all binding): "Scanning a creature gives it a
    /// 'Codex Bounty'"; "Killing a creature does not scan. Scanning it after killing it does give you the bounty. To claim all bounties
    /// you have to open the codex and click a button on each entry that has been completed"; "the % is linked to the level you were at
    /// when you killed it" (2-4 % of the Base EXP bar by the creature's level and danger); at the job's highest level "The EXP is
    /// converted to an appropriate amount of Zennys instead"; a 10-level bracket whose every bounty is claimed has its own bonus (75 % of
    /// the top level's requirement + 5 % of the player's own); "Event and quest monsters do have entries and exp rewards, but they don't
    /// count towards bracket rewards". design note, 2026-10-16: bounties belong to the CHARACTER (the Codex's entries stay the account's);
    /// the server enforces the scan's time; "whoever was RESPONSIBLE for spawning a quest / event monster gets it SCANNED no matter how
    /// it dies", with "a client-side net: any event / quest-only creature that comes into the player's view is written at once"; a
    /// "Claim all on this page" button.
    ///
    /// The SERVER holds the bounties, fixes and pays the amounts (the NPC script server/data/overlay/npc/custom/loki_codex.txt, which
    /// Loki Host installs into its rAthena copy: tools/lokihost/Core/CodexBounty.cs; Net/CodexBountyWire.cs the words). The client only:
    ///   - asks for the character's state each time it enters the world and after a base level-up ("s" to "NPC:LokiCodex"); a server
    ///     without the NPC answers "not online" (ZC_ACK_WHISPER 0x09df result 1): kept out of the chat, and the book shows no bounty;
    ///   - tells the server each kind of creature that comes into sight ("n#id", <see cref="CxpSeen"/>: the server's scan clock starts
    ///     from its own check of a living one near; a quest / event creature's bounty it places at once: "P");
    ///   - writes a quest / event creature into the book the moment it is seen (the server's "P", or a creature the text pack gives no
    ///     regular spawn: <see cref="CxpAtSightKind"/>), the client-side net, any server;
    ///   - reports a FINISHED SCAN (LokiGame.WCodex.cs WCodexFilled → <see cref="CxpScanned"/>) of a creature with no bounty ("b#id"); the
    ///     ring runs again for a written creature with no bounty (<see cref="CxpScanWanted"/>: a creature killed first);
    ///   - asks the server to look at its watched quest / event creatures ("p") when one of them dies in sight and when the book opens;
    ///   - reports each death of a monster it hit whose bounty is open ("x#unit id"): the server completes it only when that unit is the
    ///     player's own last kill as rAthena recorded it (killedgid), the path for monsters with their own event label, which rAthena
    ///     gives that label instead of OnNPCKillEvent (mob.cpp:3591-3604); the others it completes from OnNPCKillEvent itself;
    ///   - sends the Codex's Claim buttons ("c#id", "k#bracket": UI/CodexBook.Bounty.cs);
    ///   - reads the server's lines (ZC_NPC_CHAT 0x02c1 to the player, "LCX1 ..."), keeps them out of the chat and shows them.
    /// Hooks (marked "wave 28 (wcxp)"): LokiGame.cs (BeginWcxp after BeginWCodex; UpdateWcxp after UpdateWCodex; HandleWcxp before Social),
    /// LokiGame.WCodex.cs (the ring's condition, WCodexFilled), UI/CodexBook.Pages.cs (the list's note, the creature page, the list's foot).
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>This character's bounties as the server tells them.</summary>
        public CodexBounty CodexBounties { get; private set; }
        /// <summary>How long a kind's "in sight" is not told again; quest / event creatures written at first sight (smoke, log).</summary>
        public const float CxpSeenAgainSeconds = 30f;
        public int CxpSeenSent { get; private set; }
        public int CxpPings { get; private set; }
        public int CxpAtSightWrites { get; private set; }
        private readonly Dictionary<int, float> _cxpSeen = new Dictionary<int, float>();
        private readonly HashSet<int> _cxpAtSight = new HashSet<int>(), _cxpFilling = new HashSet<int>();
        private readonly Dictionary<uint, int> _cxpSightUnits = new Dictionary<uint, int>();
        private readonly List<int> _cxpKeys = new List<int>();
        private bool _cxpBookOpen; private float _cxpPingAt = -10f;
        /// <summary>Seconds after entering the world before the state is asked (the login's own second map entry first); the window in which
        /// a "not online" answer is the NPC's; how long a scan's request is not sent again for the same creature; how long a hit makes a
        /// monster's death the player's to report.</summary>
        public const float CxpAskDelay = 1.5f, CxpAckSeconds = 4f, CxpAgainSeconds = 20f, CxpHitSeconds = 60f;
        /// <summary>Whispers sent (state / scans / kill reports / claims), lines taken out of the chat, "not online" answers taken (smoke, log).</summary>
        public int CxpStateAsks { get; private set; }
        public int CxpScanAsks { get; private set; }
        public int CxpReports { get; private set; }
        public int CxpClaimsSent { get; private set; }
        public int CxpLinesTaken { get; private set; }
        public int CxpAcksTaken { get; private set; }
        /// <summary>The last completion (creature, fixed EXP, share), claim (creature, EXP, share, Zeny) and bracket paid (bracket, EXP,
        /// Zeny) as the server told them (smoke).</summary>
        public (int mob, long exp, int share) CxpLastCompleted { get; private set; }
        public (int mob, long exp, int share, long zeny) CxpLastPaid { get; private set; }
        public (int bracket, long exp, long zeny) CxpLastBracket { get; private set; }
        public string CxpLastNotice { get; private set; } = "";
        public CodexBountyFanfare CxpFanfare { get; private set; }
        private float _cxpSuppressAngelUntil = -1f;
        public bool CxpSuppressingLevelUpEffect => Time.unscaledTime <= _cxpSuppressAngelUntil;
        private readonly Dictionary<int, float> _cxpAsked = new Dictionary<int, float>();
        private readonly Dictionary<uint, (int job, float at)> _cxpHit = new Dictionary<uint, (int, float)>();
        private string _cxpAccount = ""; private float _cxpInGameAt = -1f, _cxpAckUntil = -1f; private bool _cxpStateSent;

        /// <summary>Hook in LokiGame.cs, after BeginWCodex (the Codex's book, ring and card exist).</summary>
        private void BeginWcxp()
        {
            CodexBounties = new CodexBounty();
            if (_boot != null && _boot.Rig != null)
                CxpFanfare = CodexBountyFanfare.Create(_boot.Rig.transform, () => _boot.Rig.Head);
            CodexBounties.Changed += () => CodexBook?.BountiesChanged();
            CodexBounties.Placed += CxpPlaced;
            CodexBounties.PlacedAtSight += CxpPlacedAtSight;
            CodexBounties.Completed += CxpCompleted;
            CodexBounties.Paid += CxpPaid;
            CodexBounties.BracketReady += CxpBracketReady;
            CodexBounties.BracketPaid += CxpBracketPaid;
            if (CodexBook != null)
            {
                CodexBook.Bounties = CodexBounties;
                CodexBook.ClaimBounty = mob => { _cxpSuppressAngelUntil = Time.unscaledTime + 1.2f; CxpSend(CodexBountyWire.Claim(mob), "claim " + mob); };
                CodexBook.ClaimBracket = b => { _cxpSuppressAngelUntil = Time.unscaledTime + 1.2f; CxpSend(CodexBountyWire.ClaimBracket(b), "claim bracket " + b); };
            }
            // the monsters the player (or its own homunculus / mercenary / elemental / pet) hit: their deaths are reported
            Entities.Damage += (src, dst, amount, crit) =>
            {
                if (amount <= 0 || src == null || dst == null || dst.Kind != Unit.UnitKind.Monster) return;
                if (src.IsSelf || (Companions != null && Companions.Owns(src.Id))) _cxpHit[dst.Id] = (dst.Job, Time.unscaledTime);
            };
            Entities.Vanished += CxpVanished;
            Entities.UnitAdded += CxpUnitAdded;
            if (Player != null) Player.BaseLevelUp += lv => { if (CodexBounties.Supported) CxpAskState("a base level up (Zeny at the highest level)"); };
            if (Social != null)
            {
                var next = Social.TakeWhisperAck;
                Social.TakeWhisperAck = res =>
                {
                    // the answer to our whisper: rAthena sends none when the NPC took it, "not online" (1) when there is no such NPC
                    if (_cxpAckUntil > 0f && Time.unscaledTime <= _cxpAckUntil && res == 1)
                    {
                        _cxpAckUntil = -1f; CxpAcksTaken++;
                        CodexBounties.MarkUnsupported();
                        Debug.Log("[CodexBounty] this server has no Codex bounties (the whisper's answer: nobody of that name): none shown");
                        return true;
                    }
                    return next != null && next(res);
                };
            }
        }

        /// <summary>Hook in LokiGame.cs's packet dispatch, before Social: the server's lines to us ("LCX1 ..."), out of the chat.</summary>
        private bool HandleWcxp(ushort id, byte[] b, int o, int n)
        {
            if (id != 0x02c1 || CodexBounties == null || n < 13) return false;
            var r = new PacketReader(b, o, n);
            r.Skip(4); uint gid = r.U32(); r.Skip(4);
            if (gid != Session.AccountId) return false;
            string text = r.Text(n - 12);
            if (!CodexBountyWire.TryParse(text, out var line)) return false;
            CxpLinesTaken++;
            _cxpAckUntil = -1f; // (the NPC answered: no "not online" can follow)
            Debug.Log("[CodexBounty] " + text);
            if (line.Kind == CodexBountyWire.Kind.StateBegin) { _cxpAsked.Clear(); _cxpSeen.Clear(); } // (the server's whole state again: a scan may ask anew, every kind in sight is told again)
            CodexBounties.Take(line);
            if (Codex != null && !W18TutRunning)
            {
                if (line.Kind == CodexBountyWire.Kind.Open)
                    foreach (int mob in line.Ids) { var cr = CodexData.GetCreature(mob); Codex.AddCreature(mob, cr != null && cr.BossType > 0 ? CodexJournal.TierSeen : CodexJournal.TierFull, "server bounty"); }
                else if (line.Kind == CodexBountyWire.Kind.Ready || line.Kind == CodexBountyWire.Kind.Claimed)
                    foreach (int mob in line.Ids) Codex.AddCreature(mob, CodexJournal.TierFull, "server bounty");
            }
            if (line.Kind == CodexBountyWire.Kind.StateEnd) CxpSeenAll("the state read");
            return true;
        }

        /// <summary>Hook in LokiGame.cs's Update, after UpdateWCodex: the state, asked once each time this character enters the world.</summary>
        private void UpdateWcxp()
        {
            if (CodexBounties == null || Session == null) return;
            bool inGame = Session.State == RoSession.Stage.InGame && Session.AccountId != 0 && Entities.Self != null;
            if (!inGame) { _cxpInGameAt = -1f; _cxpStateSent = false; _cxpSeen.Clear(); return; } // (the server's scan clocks last a session)
            // design note (2026-10-16): the bounties are the CHARACTER's: another character of the account starts from nothing
            string key = AccountSyncRules.ServerKey(Session.Host, Session.Port) + "#" + Session.AccountId + "#" + (Session.SelectedChar?.CharId ?? 0u);
            if (key != _cxpAccount) { _cxpAccount = key; CodexBounties.Reset(); _cxpAsked.Clear(); _cxpHit.Clear(); _cxpAtSight.Clear(); _cxpSightUnits.Clear(); _cxpStateSent = false; }
            if (_cxpInGameAt < 0f) _cxpInGameAt = Time.unscaledTime;
            if (!_cxpStateSent && Time.unscaledTime - _cxpInGameAt >= CxpAskDelay)
            {
                _cxpStateSent = true;
                CxpAskState("entered the world");
            }
            // a look that starts on a kind tells the server too (a first sight it missed: a relog, a dropped whisper)
            _cxpKeys.Clear(); _cxpKeys.AddRange(_codexFill.Keys);
            foreach (var k in _cxpKeys) if (!_cxpFilling.Contains(k)) CxpSeen(k, "a look");
            _cxpFilling.Clear(); foreach (var k in _cxpKeys) _cxpFilling.Add(k);
            // the book opens: the server looks at its watched quest / event creatures first
            bool open = CodexBook != null && CodexBook.IsOpen;
            if (open && !_cxpBookOpen && CodexBounties.Supported && CodexBounties.OpenCount > 0) CxpPing("the book opened");
            _cxpBookOpen = open;
        }

        /// <summary>A unit came into sight (spawn / appear / walking in): a monster's kind is told to the server, and a quest / event
        /// creature is written into the book at once.</summary>
        private void CxpUnitAdded(Unit u)
        {
            if (u == null || u.IsSelf || u.Kind != Unit.UnitKind.Monster || u.Job <= 0 || CodexBounties == null) return;
            if (CxpAtSightKind(u.Job)) { _cxpSightUnits[u.Id] = u.Job; CxpWriteAtSight(u.Job, u, "first sight"); }
            CxpSeen(u.Job, "in sight");
        }
        /// <summary>Every monster in sight now (the state was just read: the server's clocks start for what is already here).</summary>
        private void CxpSeenAll(string why)
        {
            if (Entities == null) return;
            foreach (var u in Entities.Units.Values) if (u != null && !u.IsSelf && u.Kind == Unit.UnitKind.Monster && u.Job > 0) CxpSeen(u.Job, why);
        }
        /// <summary>"n#id": a kind with no bounty (or a quest / event creature's open one: the server watches the new ones too) is in sight;
        /// once per kind per <see cref="CxpSeenAgainSeconds"/>.</summary>
        private void CxpSeen(int mob, string why)
        {
            if (CodexBounties == null || !CodexBounties.Supported || CodexBounties.Reading || mob <= 0 || Session == null) return;
            if (CodexBounties.IsReady(mob) || CodexBounties.IsClaimed(mob) || CodexBounties.IsOpen(mob) && !_cxpAtSight.Contains(mob)) return;
            if (!CodexData.Metable(mob)) return;
            if (_cxpSeen.TryGetValue(mob, out var at) && Time.unscaledTime - at < CxpSeenAgainSeconds) return;
            _cxpSeen[mob] = Time.unscaledTime; CxpSeenSent++;
            Session.Whisper(CodexBountyWire.Npc, CodexBountyWire.Seen(mob));
        }
        private void CxpPing(string why)
        {
            if (Session == null || CodexBounties == null || !CodexBounties.Supported || Time.unscaledTime - _cxpPingAt < 1f) return;
            _cxpPingAt = Time.unscaledTime; CxpPings++;
            Session.Whisper(CodexBountyWire.Npc, CodexBountyWire.Ping());
            Debug.Log($"[CodexBounty] {why}: the server looks at its watched quest / event creatures (\"p\")");
        }
        /// <summary>A quest / event creature (the client-side net, any server): the server said so this session ("P"), or the text pack
        /// gives it no regular spawn (a script, a summon or an item brings it: CodexData.Creature.Habitats empty).</summary>
        public bool CxpAtSightKind(int mob)
        {
            if (_cxpAtSight.Contains(mob)) return true;
            var cr = CodexData.GetCreature(mob);
            return cr != null && cr.Habitats.Count == 0 && CodexData.Metable(mob);
        }
        /// <summary>design note: "those who spawn them will NEVER not get their codex entries": written the moment it is seen, not on a look (a
        /// boss "seen", as its look would; its kill completes it).</summary>
        private void CxpWriteAtSight(int mob, Unit u, string how)
        {
            if (Codex == null) return;
            var cr = CodexData.GetCreature(mob); bool boss = cr != null && cr.BossType > 0;
            int was = Codex.Tier(mob);
            if (!Codex.AddCreature(mob, boss ? CodexJournal.TierSeen : CodexJournal.TierFull, how)) return;
            CxpAtSightWrites++;
            string name = CodexBook.NameOf(mob);
            if (was == CodexJournal.TierNone && u != null) WCodexNew(mob, WCodexCentre(u), WCodexRadius(u), name);
            else Hud?.Notify("Codex: " + name, 3f);
            Debug.Log($"[CodexBounty] {name} ({mob}) written at {how} (a quest / event creature)");
        }

        /// <summary>Asks the server for this character's bounties ("s"): every login, each base level-up, and the smoke after a GM helper.</summary>
        public void CxpAskState(string why)
        {
            if (Session == null || CodexBounties == null) return;
            CxpStateAsks++;
            _cxpAckUntil = Time.unscaledTime + CxpAckSeconds;
            Session.Whisper(CodexBountyWire.Npc, CodexBountyWire.AskState());
            Debug.Log($"[CodexBounty] asking the server for this character's bounties ({why})");
        }
        private void CxpSend(string what, string why)
        {
            if (Session == null || CodexBounties == null || !CodexBounties.Supported) return;
            if (what.StartsWith("c#") || what.StartsWith("k#")) CxpClaimsSent++;
            Session.Whisper(CodexBountyWire.Npc, what);
            Debug.Log($"[CodexBounty] {why}: \"{what}\"");
        }

        /// <summary>Hook in LokiGame.WCodex.cs UpdateWCodexLook: the ring also runs for a creature already written that has no bounty yet
        /// (a creature killed first, a boss seen before), while this server has bounties.</summary>
        private bool CxpScanWanted(int mob)
        {
            if (CodexBounties == null || !CodexBounties.Supported || CodexBounties.Reading) return false;
            if (CodexBounties.Any(mob) || !CodexData.Metable(mob)) return false;
            return !(_cxpAsked.TryGetValue(mob, out var at) && Time.unscaledTime - at < CxpAgainSeconds);
        }

        /// <summary>Hook in LokiGame.WCodex.cs WCodexFilled: a scan finished (EVERY full ring: a new entry or a creature already written).
        /// <paramref name="wrote"/>: the ring wrote something into the book (else it ran only for the bounty).</summary>
        private void CxpScanned(int mob, Unit u, bool wrote)
        {
            if (CodexBounties == null || CodexBounties.Unsupported || mob <= 0) return;
            if (CodexBounties.Any(mob)) return;
            if (_cxpAsked.TryGetValue(mob, out var at) && Time.unscaledTime - at < CxpAgainSeconds) return;
            _cxpAsked[mob] = Time.unscaledTime; CxpScanAsks++;
            if (!CodexBounties.Supported) _cxpAckUntil = Time.unscaledTime + CxpAckSeconds;
            Session.Whisper(CodexBountyWire.Npc, CodexBountyWire.Place(mob));
            if (!wrote && CodexRing != null && !CodexRing.Finishing) CodexRing.Done(true); // (a ring run for the bounty alone still ends as a finished one)
            Debug.Log($"[CodexBounty] scan of {mob} finished ({(wrote ? "written" : "already written")}): asking for its bounty");
        }

        /// <summary>A unit left the view; type 1 = it died. A monster the player hit within <see cref="CxpHitSeconds"/> whose bounty is
        /// open: its death is reported (the server's killedgid decides).</summary>
        private void CxpVanished(uint gid, int type)
        {
            // a quest / event creature died in sight: whoever killed it, the server's watch completes its open bounty
            if (_cxpSightUnits.TryGetValue(gid, out var sj))
            {
                _cxpSightUnits.Remove(gid);
                if (type == 1 && CodexBounties != null && CodexBounties.IsOpen(sj)) CxpPing("a quest / event creature died in sight");
            }
            if (!_cxpHit.TryGetValue(gid, out var h)) return;
            _cxpHit.Remove(gid);
            if (type != 1 || CodexBounties == null || !CodexBounties.Supported || Time.unscaledTime - h.at > CxpHitSeconds) return;
            if (!CodexBounties.IsOpen(h.job)) return;
            CxpReports++;
            Session.Whisper(CodexBountyWire.Npc, CodexBountyWire.Report(gid));
            Debug.Log($"[CodexBounty] a monster with an open bounty ({h.job}) died after our hits: reported ({gid})");
        }

        /// <summary>"+1,165 EXP", or "+485 Zeny" when a claim pays Zeny now.</summary>
        public string CxpAmountText(long exp) => CodexBounties != null && CodexBounties.ZenyPerMillion > 0
            ? string.Format("+{0:N0} Zeny", CodexBountyWire.Zeny(exp, CodexBounties.ZenyPerMillion)) : string.Format("+{0:N0} EXP", exp);
        private static string CxpPaidText(long exp, long zeny) => zeny > 0 ? string.Format("+{0:N0} Zeny", zeny) : string.Format("+{0:N0} EXP", exp);

        private void CxpPlaced(int mob)
        {
            _cxpSeen.Remove(mob); // (the server's scan clock of this kind ended with the placement)
            var cr = CodexData.GetCreature(mob);
            long flatExp = CodexBountyWire.FlatExp(cr != null ? cr.Level : 1, cr != null ? cr.BossType : 0);
            string extra = "Bounty " + CxpAmountText(flatExp);
            if (CodexFlair == null || !CodexFlair.SetExtra(mob, extra)) CxpSay("Codex Bounty: " + CodexBook.NameOf(mob) + " (" + CxpAmountText(flatExp) + ")", 3f);
        }
        /// <summary>The server placed a quest / event creature's bounty at first sight ("P"): its units are watched here too (their death
        /// asks the server to look), and its entry is written if the client's own net had not done it.</summary>
        private void CxpPlacedAtSight(int mob)
        {
            _cxpAtSight.Add(mob);
            Unit seen = null;
            if (Entities != null) foreach (var u in Entities.Units.Values) if (u != null && !u.IsSelf && u.Kind == Unit.UnitKind.Monster && u.Job == mob) { _cxpSightUnits[u.Id] = mob; seen = seen ?? u; }
            CxpWriteAtSight(mob, seen, "first sight (the server)");
            CxpSay("Codex Bounty: " + CodexBook.NameOf(mob), 3f);
        }
        private void CxpCompleted(int mob, long exp, int share)
        {
            CxpLastCompleted = (mob, exp, share);
            string name = CodexBook.NameOf(mob);
            string amt = CxpAmountText(exp);
            CxpSay(string.Format("Bounty ready: {0} {1}", name, amt), 3f);
            CxpPlayFanfare("Bounty Complete!", name + "  ·  " + amt, CodexBountyFanfare.Kind.Kill);
        }
        private void CxpPaid(int mob, long exp, int share, long zeny)
        {
            CxpLastPaid = (mob, exp, share, zeny);
            _cxpSuppressAngelUntil = Time.unscaledTime + 1.2f;
            string paid = CxpPaidText(exp, zeny);
            string name = CodexBook.NameOf(mob);
            CxpSay(paid + " · Codex Bounty", 3f);
            Hud?.Chat.Add(string.Format("Codex Bounty: {0}, {1}", name, zeny > 0 ? string.Format("+{0:N0} Zeny", zeny) : share > 0 ? string.Format("+{0:N0} Base EXP ({1})", exp, CodexBountyWire.Percent(share)) : string.Format("+{0:N0} Base EXP", exp)), "9ad1ff");
            CxpPlayFanfare("Bounty Claimed!", name + "  ·  " + paid, CodexBountyFanfare.Kind.Claim);
        }
        private void CxpBracketReady(int bracket, long bonus)
        {
            string range = CodexBountyWire.BracketFrom(bracket) + "-" + CodexBountyWire.BracketTo(bracket);
            CxpSay(string.Format("Codex: Lv {0} complete · bonus ready", range), 4f);
            CxpPlayFanfare("Codex Bracket Complete!", "Lv " + range + "  ·  " + CxpAmountText(bonus), CodexBountyFanfare.Kind.Kill);
        }
        private void CxpBracketPaid(int bracket, long exp, long zeny)
        {
            CxpLastBracket = (bracket, exp, zeny);
            _cxpSuppressAngelUntil = Time.unscaledTime + 1.2f;
            string range = CodexBountyWire.BracketFrom(bracket) + "-" + CodexBountyWire.BracketTo(bracket);
            string paid = CxpPaidText(exp, zeny);
            CxpSay(string.Format("{0} · Codex: Lv {1} bonus", paid, range), 4f);
            Hud?.Chat.Add(string.Format("Codex: the Lv {0} bonus, {1}", range, zeny > 0 ? string.Format("+{0:N0} Zeny", zeny) : string.Format("+{0:N0} Base EXP", exp)), "9ad1ff");
            CxpPlayFanfare("Bracket Bonus Claimed!", "Lv " + range + "  ·  " + paid, CodexBountyFanfare.Kind.Claim);
        }
        private void CxpPlayFanfare(string title, string sub, CodexBountyFanfare.Kind kind)
        {
            Audio?.PlayBountyFanfare();
            var me = Entities != null ? Entities.Self : null;
            if (me != null)
            {
                if (_boot != null && _boot.Map != null)
                {
                    var burstCol = kind == CodexBountyFanfare.Kind.Claim ? new Color(0.38f, 0.96f, 0.68f) : new Color(1.0f, 0.45f, 0.22f);
                    Fx.Burst(_boot.Map.transform, me.transform.localPosition + Vector3.up * 0.9f, burstCol, 1.35f, 28);
                }
                CxpFanfare?.Play(WCodexCentre(me), WCodexRadius(me), title, sub, kind);
            }
        }
        private void CxpSay(string s, float seconds) { CxpLastNotice = s; Hud?.Notify(s, seconds); Debug.Log("[CodexBounty] " + s); }
    }
}
