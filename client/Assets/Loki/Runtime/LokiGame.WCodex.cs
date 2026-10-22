using System;
using System.Collections.Generic;
using Loki.Game;
using Loki.Net;
using Loki.Net.Packets;
using Loki.Sprites;
using Loki.UI;
using Loki.World;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Wave 28 (wcodex): the Codex in the game (design note, 2026-10-15). The book (UI/CodexBook.cs) opens from the wrist menu (the ring's own
    /// "Codex" group: "its own menu on the wrist menu, along with Character, Settings, Items"; on the classic wrist menu a button on the
    /// Achievements page); what is in it is the account's journal
    /// (Game/CodexJournal.cs). How things get written down:
    ///   CREATURES (no kill needed: "Looking at the creature for a bit fills a circle much like the tutorial's", "stronger enemies need more
    ///   time"): while the look holds a monster (Game/Targeting.cs Locked, within the lock's own cone: the tutorial's own rule,
    ///   LokiGame.W20Tut.cs W20LookTick) the scholar's ring (UI/CodexInkRing.cs) fills around it in CodexData.LookSeconds(level, boss)
    ///   seconds, and drains at twice that speed when the look leaves; a creature kind keeps its own fill (another Poring continues it).
    ///   Full: the creature is written. A creature already written (or a boss already seen) gets no ring.
    ///   BOSSES ("Boss enemies need to be killed for the full information to be added to the bestiarium. Looking at them only writes down
    ///   limited data"): a boss (rAthena's Class Boss, MVPs included: mob.cpp:377-384) looked at is written SEEN ONLY: name, picture,
    ///   level, size, race, element, where it lives and how it behaves; its numbers, weaknesses, skills and drops stay "???" until it is
    ///   defeated (FLAGGED: limited vs full).
    ///   KILLS ("if you do kill it before the circle fills somehow, it goes in the bestiarium automatically"): a monster that dies
    ///   (ZC_NOTIFY_VANISH type 1: EntityManager.Vanished) after the player or the player's own homunculus / mercenary / elemental / pet hit
    ///   it (EntityManager.Damage, CombatFx.SkillDamage with damage > 0) is written in full (FLAGGED: a kill by a party member who
    ///   alone hit it does not count).
    ///   ITEMS (FLAGGED rule): written the moment the game shows the item to the player: in the bag (worn gear and the cards in it
    ///   included), the cart, the storage; on the ground in sight; in an NPC's shop, market or barter list, the cash shop, a point shop,
    ///   another player's shop or buying store, a shop search; in a trade (either side); in a mail opened.
    /// Hooks (marked "wave 28 (wcodex)"): LokiGame.cs (BeginWCodex, UpdateWCodex, OnApplicationQuit, the smoke's dispatch), LokiGame.W18Account.cs
    /// (the account's settings came in), UI/WristMenu.Board.cs (the Quests group's tab), UI/WristMenu.cs (the classic button), UI/UiKit.cs
    /// (PanelKinds: a menu), UI/PanelGrab.cs (taken by its edge), UI/ComfortWindows.cs + UI/SettingHelp.cs ("Codex flip-through", "Codex book"),
    /// LokiGame.W13Grab.cs + LokiGame.W15Grab.cs (the hand-held book: its tilt, the pass to the other hand, how near it may be let go),
    /// World/UserSettings.cs (the saved fields).
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>Seconds after the map login after which the book is written into the account's settings even if the account's sync
        /// gave no answer (a server without Loki Host's relay keeps its settings on this PC only).</summary>
        public const float CodexSettingsWait = 45f, CodexPollSeconds = 0.5f;
        /// <summary>The Codex's Settings rows, English in every language (design note, 2026-10-15: "English-only UI text policy").</summary>
        public static readonly HashSet<string> WCodexEnglishOnlyRows = new HashSet<string> { "Codex scan", "Codex flip-through", "Codex book" };
        public CodexJournal Codex { get; private set; }
        public CodexBook CodexBook { get; private set; }
        public CodexInkRing CodexRing { get; private set; }
        /// <summary>The "New entry" card a newly written creature gets (UI/CodexFlair.cs).</summary>
        public CodexFlair CodexFlair { get; private set; }
        /// <summary>A boss's look shortened by hits (smoke, log): hits counted, seconds taken off in all; when a kind's fill last grew by a hit.</summary>
        public int CodexBossHits { get; private set; }
        public float CodexHitSecondsOff { get; private set; }
        /// <summary>Creatures an NPC's line named and the book took as "heard of" (smoke, log).</summary>
        public int CodexHeardWrites { get; private set; }
        private readonly Dictionary<int, float> _codexHitFill = new Dictionary<int, float>();
        private MenuNav.Menu _codexNav;
        private readonly Dictionary<int, float> _codexFill = new Dictionary<int, float>();
        private readonly List<int> _codexKeys = new List<int>();
        private Unit _codexRingUnit; private int _codexRingMob;
        private readonly Dictionary<uint, float> _codexHit = new Dictionary<uint, float>();
        private readonly Dictionary<uint, (int job, int level, int boss)> _codexUnits = new Dictionary<uint, (int, int, int)>();
        private readonly HashSet<uint> _codexGround = new HashSet<uint>();
        private float _codexPollAt, _codexLoginAt = -1f; private int _codexStorageSeen = -1, _codexSearchSeen = -1; private string _codexMap = "";
        /// <summary>Creatures written by looking / by a kill, the last one and how long its ring took (smoke, log).</summary>
        public int CodexLookWrites { get; private set; }
        public int CodexKillWrites { get; private set; }
        public float CodexLastFillSeconds { get; private set; }
        /// <summary>How long the last ring that wrote a creature took from empty to full (seconds; the look's own time when it never left).</summary>
        public float CodexLastFillTook { get; private set; }
        private readonly Dictionary<int, float> _codexFillFrom = new Dictionary<int, float>();
        public int CodexLastMob { get; private set; }
        public float CodexFillOf(int mob) => _codexFill.TryGetValue(mob, out var f) ? f : 0f;

        private void BeginWCodex(LokiBootstrap boot)
        {
            var rig = boot.Rig; if (rig == null || Hud == null) return;
            // (waves 27-28 merge: the text pack is wtextc's Data/TextPack.cs, loki.json "textPack"; a test run sets it in its loki.json: smokes.sh "wcodex" TEXTPACKDIR)
            Codex = new CodexJournal();
            Codex.SettingsGate = () => (AccountSync != null && AccountSync.Answers > 0) || (_codexLoginAt > 0f && Time.unscaledTime - _codexLoginAt > CodexSettingsWait); // (no relay answers on a server without Loki Host: then after the wait)
            CodexBook = CodexBook.Create(rig.transform, () => rig.Head, Codex);
            CodexBook.ClosedNow = () => Codex.SaveNow("the book closed");
            Codex.CreatureAdded += (id, tier, how) => CodexBook.JournalChanged();
            Codex.ItemAdded += (id, how) => CodexBook.JournalChanged();
            CodexRing = CodexInkRing.Create(rig.transform, () => rig.Head, "CodexInkRing");
            CodexFlair = CodexFlair.Create(rig.transform, () => rig.Head);
            CodexBook.PlayerLevel = () => Player != null ? Player.BaseLevel : 1;
            Hud.Wrist.OpenCodex = () => WCodexOpen("the wrist menu");
            // one menu at a time (UI/MenuNav.cs): the book is the player's menu; opened from the board it closes the board and shows the
            // back arrow; the menu button closes it with the rest
            _codexNav = new MenuNav.Menu { Name = "CodexBook", Panel = CodexBook.transform, IsOpen = () => CodexBook != null && CodexBook.IsOpen,
                                           Close = () => CodexBook.Close("one menu at a time"), Capture = () => CodexBook.Current,
                                           Reopen = (o, st) => WCodexOpen("its back arrow"), Arrow = CodexBook.SetBackArrow };
            MenuNav.Register(_codexNav);
            Hud.Wrist.CodexNav = _codexNav; // (the ring's Codex group: a step back to the ring, UI/WristMenu.Board.cs OpenCodexHere)
            // the menu animations' registry: the book plays its own opening (the cover), under the same switch and 5 s rule
            MenuAnim.Register(CodexBook.AnimKey, "Codex");
            MenuAnim.SetStyle(CodexBook.AnimKey, new MenuAnim.Style { Seconds = CodexBook.OpenSeconds, StartScale = 0.55f, Fold = new Vector3(0, -171, 0) });
            MenuAnim.Own(CodexBook.AnimKey);
            // what writes things down
            Entities.Damage += WCodexDamage;
            if (Combat != null) Combat.SkillDamage += (src, dst, dmg, skill) => WCodexDamage(src, dst, dmg, false);
            Entities.Vanished += WCodexVanished;

            Session.OnMapPacket += WCodexPeek;
            Inventory.Changed += () => { foreach (var e in Inventory.Items.Values) WCodexEntry(e, "the bag"); };
            Inventory.Gained += (id, n) => WCodexItem(id, "picked up");
            if (Cart != null) Cart.Changed += () => { foreach (var e in Cart.Items.Values) WCodexEntry(e, "the cart"); };
            if (Trade != null) Trade.Changed += () => { foreach (var o in Trade.Mine) WCodexCards(o.ItemId, o.Cards, "a trade"); foreach (var o in Trade.Theirs) WCodexCards(o.ItemId, o.Cards, "a trade"); };
            if (Market != null) Market.Changed += () => { foreach (var v in Market.OtherShop) WCodexCards(v.ItemId, v.Cards, "a player's shop"); foreach (var w in Market.OtherWants) WCodexItem(w.ItemId, "a buying store"); };
            if (Mail != null) Mail.Changed += () => { var r = Mail.Reading; if (r != null) foreach (var a in r.Items) WCodexCards(a.ItemId, a.Cards, "a mail"); };
            if (CashShop != null) CashShop.Changed += () => { foreach (var kv in CashShop.Tabs) foreach (var (item, _) in kv.Value) WCodexItem(item, "the cash shop"); };
            Debug.Log($"[Codex] ready: the book, the ring; look times {CodexData.LookSeconds(1, 0):0.00} s (level 1) to {CodexData.LookMax:0} s; the text pack {(CodexData.HasPack ? CodexData.PackRoot : "none")}");
        }

        /// <summary>Opens the book in front of the player (the wrist menu's entry).</summary>
        public void WCodexOpen(string why)
        {
            if (CodexBook == null) return;
            if (Session == null || Session.State != RoSession.Stage.InGame || Codex == null || Codex.Account.Length == 0) { Hud?.Notify("Enter the world first.", 2f); return; }
            if (CodexBook.IsOpen) return;
            CodexBook.JournalChanged();
            CodexBook.Open(why);
        }

        /// <summary>Hook: OnW18AccountApplied (the account's settings came in from Loki Host).</summary>
        private void WCodexAccountApplied() { Codex?.SettingsArrived(); CodexBook?.JournalChanged(); }

        /// <summary>Hook: Update.</summary>
        private void UpdateWCodex()
        {
            if (Codex == null) return;
            bool inGame = Session != null && Session.State == RoSession.Stage.InGame && Session.AccountId != 0;
            if (inGame)
            {
                string key = AccountSyncRules.ServerKey(Session.Host, Session.Port) + "#" + Session.AccountId;
                if (key != Codex.Account) { _codexLoginAt = Time.unscaledTime; Codex.Use(key); CodexBook?.JournalChanged(); _codexStorageSeen = -1; WCodexScanAll("logged in"); }
                string map = _boot.Map != null && _boot.Map.Current != null ? _boot.Map.Current.Name : "";
                if (map != _codexMap) { _codexMap = map; _codexGround.Clear(); _codexHit.Clear(); _codexFill.Clear(); _codexFight.Clear(); _codexLookAt.Clear(); _codexRingUnit = null; _codexRingMob = 0; if (CodexRing != null && !CodexRing.Finishing) CodexRing.Hide(); }
            }
            Codex.Tick();
            Hud?.Wrist?.SetCodexAura(inGame && Codex.PendingHeard.Count > 0);
            if (!inGame) { if (CodexRing != null && CodexRing.Shown && !CodexRing.Finishing) CodexRing.Hide(); return; }
            UpdateWCodexLook(Time.unscaledDeltaTime);
            UpdateWCodexTouch();
            if (Time.unscaledTime >= _codexPollAt) { _codexPollAt = Time.unscaledTime + CodexPollSeconds; WCodexPoll(); }
        }

        // ------------------------------------------------------------------ the hand-held book (design note, 2026-10-15: "Provide the option to spawn
        // the Codex as an actual book-sized book that I grab with my hands. This should be the default version.")
        /// <summary>The panel is the hand-held Codex (held, it takes the hand's full turn: LokiGame.W13Grab.cs W13TiltFree; a shake gives it
        /// Upright, as any panel's shake gives the other tilt).</summary>
        private bool WCodexHandBook(Transform panel) => panel != null && CodexBook != null && panel == CodexBook.Root && CodexBook.HandMode;
        /// <summary>Times the hand-held book went from one hand to the other (smoke, log).</summary>
        public int WCodexPasses { get; private set; }
        /// <summary>The other hand holds the hand-held book and this hand's grip point is at its edge band (UI/PanelGrab.cs, the same band
        /// and reach as any panel's): a grip of this hand takes it over (LokiGame.W13Grab.cs W13GrabStart lets the other hand go).</summary>
        private bool WCodexPassAt(int hand, Vector3 p, out Vector3 rim, out float dist)
        {
            rim = default; dist = 0f;
            var b = CodexBook; if (b == null || !b.HandMode || !b.IsOpen) return false;
            int other = 1 - hand; if (_grabHold[other] == null || _grabHold[other].Panel != b.Root) return false;
            dist = PanelGrab.BandDistance(b.Root, p, out rim);
            return dist <= PanelGrab.HandReach;
        }

        /// <summary>The fingertip on the book (FLAGGED: Loki's numbers): a hand that does not hold the book (nor the wand, nor a pad / flick
        /// gesture open) presses the button its tip touches: the tip (LokiGame.W12Cast.cs W12Tip: a controller's point ahead of the grip, a
        /// tracked hand's index tip) within <see cref="CodexTouchDepth"/> m of the button's face, inside its rectangle grown by
        /// <see cref="CodexTouchMargin"/> m. Once: again only after the tip left by <see cref="CodexTouchRearm"/> m and
        /// <see cref="CodexTouchLockout"/> s passed. The corner arrows ‹ › sit in the pages' bottom corners: touching a corner turns the page;
        /// a row, a part, Index, X the same as the laser's click. Either size of the book (the large one is in reach too).</summary>
        public const float CodexTouchDepth = 0.015f, CodexTouchMargin = 0.006f, CodexTouchRearm = 0.03f, CodexTouchLockout = 0.35f;
        public int CodexTouches { get; private set; }
        public string CodexLastTouch { get; private set; } = "";
        private readonly UiButton[] _codexTouching = new UiButton[2];
        private readonly float[] _codexTouchAt = { -9f, -9f };
        private readonly List<UiButton> _codexButtons = new List<UiButton>();
        private void UpdateWCodexTouch()
        {
            var b = CodexBook;
            if (b == null || !b.IsOpen || b.Opening || _w12 == null || _boot.Rig == null) { _codexTouching[0] = _codexTouching[1] = null; return; }
            bool listed = false;
            for (int i = 0; i < 2; i++)
            {
                var h = _w12[i]; var tip = W12Tip(h);
                var was = _codexTouching[i];
                if (was != null)
                {
                    if (!was.isActiveAndEnabled || !WCodexTipOn(was, tip, CodexTouchRearm, CodexTouchRearm, out _)) _codexTouching[i] = null;
                    continue;
                }
                bool free = !(_grabHold[i] != null && _grabHold[i].Panel == b.Root) && !h.In.WandHeld && !h.Open && _wandHand != h;
                if (!free || Time.unscaledTime - _codexTouchAt[i] < CodexTouchLockout || Vector3.Distance(tip, b.transform.position) > 0.9f) continue;
                if (!listed) { b.GetComponentsInChildren(false, _codexButtons); listed = true; }
                UiButton best = null; float bestD = float.MaxValue;
                foreach (var btn in _codexButtons)
                {
                    if (btn == null || !btn.Interactable || !btn.isActiveAndEnabled) continue;
                    if (WCodexTipOn(btn, tip, CodexTouchDepth, CodexTouchMargin, out float d) && d < bestD) { bestD = d; best = btn; }
                }
                if (best == null) continue;
                _codexTouching[i] = best; _codexTouchAt[i] = Time.unscaledTime; CodexTouches++;
                CodexLastTouch = $"the {(i == 0 ? "left" : "right")} fingertip on '{(best.Text != null && best.Text.text.Length > 0 ? best.Text.text : best.name)}' ({bestD * 100f:0.0} cm off its face)";
                Debug.Log($"[Codex] {CodexLastTouch}");
                VR.Haptics.Pulse(VR.Haptics.Kind.Ui, h.In.IsMain, 0.12f, 0.02f);
                best.Click();
            }
        }
        /// <summary>The tip is within <paramref name="depth"/> m of the button's face and inside its rectangle grown by <paramref name="margin"/> m.</summary>
        private static bool WCodexTipOn(UiButton btn, Vector3 tip, float depth, float margin, out float off)
        {
            var rt = (RectTransform)btn.transform; var l = rt.InverseTransformPoint(tip); var r = rt.rect;
            float s = Mathf.Max(Mathf.Abs(rt.lossyScale.x), 1e-6f), m = margin / s;
            off = Mathf.Abs(l.z) * s;
            return off <= depth && l.x >= r.xMin - m && l.x <= r.xMax + m && l.y >= r.yMin - m && l.y <= r.yMax + m;
        }

        // ------------------------------------------------------------------ creatures: the look
        /// <summary>The creature's boss type: the Codex data's (rAthena's mob_db), else what the server sent with the monster (isBoss).</summary>
        public int WCodexBossType(Unit u)
        {
            var c = CodexData.GetCreature(u.Job); if (c != null) return c.BossType;
            return _codexUnits.TryGetValue(u.Id, out var i) ? i.boss : 0;
        }
        /// <summary>Seconds of looking that write this monster down (CodexData.LookSeconds of its level and boss type).</summary>
        public float WCodexLookSeconds(Unit u)
        {
            var c = CodexData.GetCreature(u.Job);
            int level = c != null ? c.Level : _codexUnits.TryGetValue(u.Id, out var i) ? i.level : 1;
            return CodexData.LookSeconds(level, WCodexBossType(u));
        }
        private float WCodexLookSecondsOf(int mob)
        {
            var c = CodexData.GetCreature(mob);
            return c != null ? CodexData.LookSeconds(c.Level, c.BossType) : CodexData.LookSeconds(1, 0);
        }

        // ------------------------------------------------------------------ the scan's calm and its fights (design note, 2026-10-16; CodexData.LookDwell)
        private readonly Dictionary<int, float> _codexLookAt = new Dictionary<int, float>(); // (a kind: when the look last held it, filling)
        private readonly Dictionary<uint, float> _codexFight = new Dictionary<uint, float>(); // (a unit: when a hit or a miss last passed between it and the player's side)
        private Unit _codexDwellUnit; private float _codexDwellFrom;
        private float _codexLift; // (0: the ring round the body; 1: above the head, a fight)
        /// <summary>Settings → Comfort → Effects "Codex scan" (World/UserSettings.cs codexScanOff).</summary>
        public static bool WCodexScanOn => !UserSettings.Prefs.codexScanOff;
        /// <summary>How far the ring stands lifted above its creature's head now (0 round the body, 1 above the head; smoke).</summary>
        public float CodexLift => _codexLift;
        /// <summary>Seconds a ring went on filling while the look was off its creature in a fight (smoke, log).</summary>
        public float CodexFightGapSeconds { get; private set; }
        /// <summary>The fill the hits on bosses added, all told (smoke: each hit adds its seconds off, CodexData.HitOff, over the 20 s).</summary>
        public float CodexHitFillAdded { get; private set; }
        /// <summary>The player's side and this monster are fighting: a hit or a miss either way within CodexData.FightSeconds, or the player's
        /// attack set on it.</summary>
        public bool WCodexInFight(Unit u) => u != null && (_attackTarget == u.Id || (_codexFight.TryGetValue(u.Id, out var at) && Time.unscaledTime - at < CodexData.FightSeconds));

        /// <summary>A creature the scan takes now: one whose kind the look can still write (not in the book; a boss: not yet seen), with the
        /// scan on; during the tutorial only the practice creature its Codex step asks for, whatever the book holds (LokiGame.WCodexTut.cs).</summary>
        private bool WCodexScanTarget(Unit u)
        {
            if (u == null || CodexData.IsExcluded(u.Job)) return false;
            if (W18TutRunning) return WCodexTutScans(u);
            if (!WCodexScanOn) return false;
            int want = WCodexBossType(u) > 0 ? CodexJournal.TierSeen : CodexJournal.TierFull;
            return Codex.Tier(u.Job) < want || CxpScanWanted(u.Job); // (wave 28, wcxp: also a creature written but with no Codex bounty yet: LokiGame.Wcxp.cs)
        }

        private void UpdateWCodexLook(float dt)
        {
            var rig = _boot.Rig; if (rig == null || Target == null || CodexRing == null) return;
            float now = Time.unscaledTime;
            if (!WCodexScanOn && !W18TutRunning && _codexFill.Count > 0) { _codexFill.Clear(); _codexFillFrom.Clear(); _codexLookAt.Clear(); if (CodexRing.Shown && !CodexRing.Finishing) CodexRing.Hide(); } // (switched off: nothing waits)
            Unit looked = null; int mob = 0; float secs = 1f;
            var t = Target.Locked; var u = t != null ? t.Unit : null;
            if (u != null && u.Kind == Unit.UnitKind.Monster && !u.IsSelf && u.gameObject.activeInHierarchy && WCodexScanTarget(u))
            {
                var c = WCodexCentre(u); var ray = rig.GazeRay;
                if (Vector3.Angle(ray.direction, c - ray.origin) <= Target.ConeDegrees) { looked = u; mob = u.Job; secs = WCodexLookSeconds(u); }
            }
            // the rest (CodexData.LookDwell): a ring starts only once the look has rested on its creature; a kind whose ring is going goes on
            if (looked != _codexDwellUnit) { _codexDwellUnit = looked; _codexDwellFrom = now; }
            bool going = looked != null && _codexFill.TryGetValue(mob, out var had) && had > 0f;
            Unit active = looked != null && (going || (now - _codexDwellFrom >= CodexData.LookDwell && !CodexRing.Finishing)) ? looked : null; // (a new ring waits for the last one's flourish: it starts empty)
            if (active != null) _codexLookAt[mob] = now;
            // the ring's own creature, the look off it for less than CodexData.LookGrace s (and on no other creature): it waits, or in a fight
            // with it goes on filling
            Unit bridged = null;
            if (active == null && _codexRingMob != 0 && _codexRingUnit != null && _codexRingUnit.gameObject.activeInHierarchy && (!W18TutRunning || WCodexTutScans(_codexRingUnit))
                && _codexFill.TryGetValue(_codexRingMob, out var rf) && rf > 0f && _codexLookAt.TryGetValue(_codexRingMob, out var la) && now - la < CodexData.LookGrace)
                bridged = _codexRingUnit;
            // every other kind's fill drains at twice its fill speed once the look has been off it CodexData.LookGrace s
            _codexKeys.Clear(); _codexKeys.AddRange(_codexFill.Keys);
            foreach (var k in _codexKeys)
            {
                if (active != null && k == mob) continue;
                if (_codexLookAt.TryGetValue(k, out var seen) && now - seen < CodexData.LookGrace) continue; // (a glance away empties nothing)
                if (_codexHitFill.TryGetValue(k, out var hitAt) && now - hitAt < CodexData.HitHoldSeconds) continue; // (a boss's fill grown by a hit holds a while)
                float f = _codexFill[k] - dt / (0.5f * WCodexLookSecondsOf(k));
                if (f <= 0f) { _codexFill.Remove(k); _codexFillFrom.Remove(k); } else _codexFill[k] = f;
            }
            if (CodexRing.Finishing && active == null) return;
            if (active != null)
            {
                if (!_codexFill.ContainsKey(mob)) _codexFillFrom[mob] = now;
                float f = (_codexFill.TryGetValue(mob, out var v) ? v : 0f) + dt / secs;
                _codexFill[mob] = Mathf.Min(1f, f);
                if (!CodexRing.Finishing) WCodexRingOn(active, mob, dt);
                if (f >= 1f) WCodexFilled(active, mob, secs, "looking");
                return;
            }
            if (bridged != null)
            {
                int bm = _codexRingMob;
                if (WCodexInFight(bridged))
                {
                    float bs = WCodexLookSeconds(bridged), f = _codexFill[bm] + dt / bs;
                    _codexFill[bm] = Mathf.Min(1f, f); CodexFightGapSeconds += dt;
                    WCodexRingOn(bridged, bm, dt);
                    if (f >= 1f) WCodexFilled(bridged, bm, bs, "looking, a fight's glances away included");
                }
                else WCodexRingOn(bridged, bm, dt);
                return;
            }
            // nothing looked at: the last ring drains where its monster is, then goes
            float left = _codexRingMob != 0 && _codexFill.TryGetValue(_codexRingMob, out var lf) ? lf : 0f;
            if (left > 0f && _codexRingUnit != null && _codexRingUnit.gameObject.activeInHierarchy) WCodexRingOn(_codexRingUnit, _codexRingMob, dt);
            else if (CodexRing.Shown) CodexRing.Hide();
        }

        /// <summary>The ring on <paramref name="u"/> showing its kind's fill: round its body, or above its head in a fight (lifted there in
        /// CodexData.FightRingLiftSeconds; another creature's ring starts where its own fight puts it, its ink from its own value).</summary>
        private void WCodexRingOn(Unit u, int mob, float dt)
        {
            bool fight = WCodexInFight(u);
            CodexRing.Fill = CodexFillOf(mob);
            if (u != _codexRingUnit || mob != _codexRingMob || !CodexRing.Shown) { _codexRingUnit = u; _codexRingMob = mob; _codexLift = fight ? 1f : 0f; CodexRing.Snap(); }
            else _codexLift = Mathf.MoveTowards(_codexLift, fight ? 1f : 0f, dt / CodexData.FightRingLiftSeconds);
            WCodexRingAt(u, _codexLift, out var at, out float r);
            CodexRing.Show(at, r);
        }
        /// <summary>Where the ring stands for <paramref name="lift"/>: 0 round the body (its middle, 1.3 x its half size), 1 above its head
        /// (CodexData.FightRingScale of that size, its lower edge a little over the head's top).</summary>
        public static void WCodexRingAt(Unit u, float lift, out Vector3 at, out float radius)
        {
            var c = WCodexCentre(u); float r = WCodexRadius(u), rh = r * CodexData.FightRingScale;
            var col = u.GetComponent<Collider>(); float top = col != null ? col.bounds.max.y : c.y + r;
            var above = new Vector3(c.x, top + rh * 1.15f, c.z);
            at = Vector3.Lerp(c, above, lift); radius = Mathf.Lerp(r, rh, lift);
        }
        /// <summary>A kind's fill is full (the look, or a boss's look shortened by hits): written down, a boss SEEN ONLY.</summary>
        private void WCodexFilled(Unit u, int mob, float secs, string how)
        {
            _codexFill.Remove(mob); _codexHitFill.Remove(mob);
            bool boss = WCodexBossType(u) > 0;
            CodexLastFillSeconds = secs; CodexLastMob = mob; CodexLastFillTook = _codexFillFrom.TryGetValue(mob, out var from) ? Time.unscaledTime - from : -1f; _codexFillFrom.Remove(mob);
            int was = Codex.Tier(mob);
            int targetTier = boss ? CodexJournal.TierSeen : CodexJournal.TierFull;
            bool wrote = Codex.AddCreature(mob, targetTier, "look");
            bool practice = W18TutRunning; // (the tutorial's Codex step: its practice creature, in the book already or not: LokiGame.WCodexTut.cs)
            CodexBook?.NoteNew(mob);
            if (wrote || practice)
            {
                if (wrote) CodexLookWrites++;
                CodexRing.Done(!boss);
                string name = CodexBook.NameOf(mob);
                if (!practice && was < targetTier) Hud?.Notify(boss ? $"Codex: {name} (seen)" : $"Codex: {name}", 3f);
                if (was == CodexJournal.TierNone) WCodexNew(mob, WCodexCentre(u), WCodexRadius(u), name);
                Debug.Log($"[Codex] {name} ({mob}) {(wrote ? "written" : "scanned again (already in the book)")} by {how}: {secs:0.00} s ({(boss ? "a boss: seen only" : "in full")}{(practice ? "; the tutorial's practice creature" : "")})");
            }
            else
            {
                CodexRing.Done(!boss);
            }
            if (practice) WCodexTutScanned(mob, wrote);
            // wave 28 (wcxp): a finished scan asks the server for the creature's Codex bounty (LokiGame.Wcxp.cs). Not the tutorial's practice
            // creatures: they are this client's alone, and the server places a bounty only for a living one it sees near the character
            // (loki_codex.txt "n#" / "b#"): the book has them, and the first real one looked at runs the ring again for its bounty (CxpScanWanted)
            else CxpScanned(mob, u, wrote);
        }
        /// <summary>A creature NEWLY in the book: its "New entry" card, and the book opens on its page (UI/CodexBook.cs).</summary>
        private void WCodexNew(int mob, Vector3 at, float radius, string name)
        {
            CodexFlair?.Play(at, radius, name, mob);
            CodexBook?.NoteNew(mob);
        }

        private static Vector3 WCodexCentre(Unit u) { var col = u.GetComponent<Collider>(); return col != null ? col.bounds.center : u.transform.position; }
        private static float WCodexRadius(Unit u) { var col = u.GetComponent<Collider>(); return col != null ? Mathf.Max(0.03f, Mathf.Max(col.bounds.extents.x, col.bounds.extents.y) * 1.3f) : 0.06f; }

        // ------------------------------------------------------------------ creatures: kills
        private void WCodexDamage(Unit src, Unit dst, int amount, bool crit)
        {
            if (dst == null || src == null) return;
            // a fight (CodexData.FightSeconds): a hit or a miss between the player's side and a monster, either way
            bool Ours(Unit x) => x.IsSelf || (Companions != null && Companions.Owns(x.Id));
            if (dst.Kind == Unit.UnitKind.Monster && Ours(src)) _codexFight[dst.Id] = Time.unscaledTime;
            else if (src.Kind == Unit.UnitKind.Monster && Ours(dst)) _codexFight[src.Id] = Time.unscaledTime;
            if (_codexFight.Count > 256) { var old = new List<uint>(); foreach (var kv in _codexFight) if (Time.unscaledTime - kv.Value > CodexData.FightSeconds) old.Add(kv.Key); foreach (var k in old) _codexFight.Remove(k); }
            if (amount <= 0 || dst.Kind != Unit.UnitKind.Monster || CodexData.IsExcluded(dst.Job) || !Ours(src)) return;
            _codexHit[dst.Id] = Time.unscaledTime;
            // design note (2026-10-15): "Increase the time to scan a boss to 20s, and make attacking it lower that amount" (CodexData.HitOff)
            int mob = dst.Job; var cd = CodexData.GetCreature(mob);
            if (Codex == null || Codex.Tier(mob) != CodexJournal.TierNone || WCodexBossType(dst) <= 0 || cd == null) return;
            float off = CodexData.HitOff(amount, cd.Hp), secs = CodexData.LookBossSeconds;
            if (!_codexFill.ContainsKey(mob)) _codexFillFrom[mob] = Time.unscaledTime;
            float was = _codexFill.TryGetValue(mob, out var v) ? v : 0f, f = was + off / secs;
            _codexFill[mob] = Mathf.Min(1f, f); _codexHitFill[mob] = Time.unscaledTime;
            CodexBossHits++; CodexHitSecondsOff += off; CodexHitFillAdded += _codexFill[mob] - was;
            if (CodexRing != null && !CodexRing.Finishing) WCodexRingOn(dst, mob, 0f); // (the real progress jumps; the drawn ink catches up: UI/CodexInkRing.cs)
            Debug.Log($"[Codex] a hit of {amount} on {dst.Name} ({mob}, a boss, {cd.Hp} HP): {off:0.00} s off its look, {(1f - Mathf.Min(1f, f)) * secs:0.0} s left");
            if (f >= 1f) WCodexFilled(dst, mob, secs, "attacks and looks");
        }
        private void WCodexVanished(uint gid, int type)
        {
            if (type == 1 && _codexHit.ContainsKey(gid) && Codex != null)
            {
                var u = Entities.Get(gid);
                int mob = u != null ? u.Job : _codexUnits.TryGetValue(gid, out var i) ? i.job : 0;
                if (mob > 0 && !CodexData.IsExcluded(mob))
                {
                    int was = Codex.Tier(mob);
                    if (Codex.AddCreature(mob, CodexJournal.TierFull, "kill"))
                    {
                        CodexKillWrites++; CodexLastMob = mob; _codexFill.Remove(mob); _codexHitFill.Remove(mob);
                        if (was == CodexJournal.TierNone && u != null) WCodexNew(mob, WCodexCentre(u), WCodexRadius(u), CodexBook.NameOf(mob));
                        else CodexBook?.NoteNew(mob);
                        if (_codexRingMob == mob && CodexRing != null && !CodexRing.Finishing) CodexRing.Hide();
                        string name = CodexBook.NameOf(mob);
                        if (was < CodexJournal.TierFull) Hud?.Notify(was == CodexJournal.TierSeen ? $"Codex: {name} complete" : $"Codex: {name}", 3f);
                    }
                }
            }
            _codexHit.Remove(gid); _codexUnits.Remove(gid);
        }

        // ------------------------------------------------------------------ items
        private void WCodexItem(int id, string how) { if (id > 0) Codex?.AddItem(id, how); }
        private void WCodexEntry(Inventory.Entry e, string how) { if (e != null) WCodexCards(e.ItemId, e.Cards, how); }
        /// <summary>An item and the cards in it (Game/Inventory.cs FullName's rule: a forged / named item's slots hold no cards).</summary>
        private void WCodexCards(int item, int[] cards, string how)
        {
            WCodexItem(item, how);
            if (cards == null || cards.Length == 0 || cards[0] == 0x00FF || cards[0] == 0x00FE || cards[0] == 0xFF00) return;
            foreach (var c in cards) if (c > 0 && GameDb.GetItem(c).Type == 6) WCodexItem(c, how + " (a card in it)");
        }
        private void WCodexScanAll(string why)
        {
            foreach (var e in Inventory.Items.Values) WCodexEntry(e, "the bag");
            if (Cart != null) foreach (var e in Cart.Items.Values) WCodexEntry(e, "the cart");
        }
        private void WCodexPoll()
        {
            if (Storage != null && Storage.IsOpen && Storage.Items.Count != _codexStorageSeen) { _codexStorageSeen = Storage.Items.Count; foreach (var e in Storage.Items.Values) WCodexEntry(e, "the storage"); }
            if (Storage != null && !Storage.IsOpen) _codexStorageSeen = -1;
            if (Ground != null) foreach (var kv in Ground.Items) if (kv.Value != null && _codexGround.Add(kv.Key)) WCodexItem(kv.Value.ItemId, "on the ground");
            if (SearchStore != null && SearchStore.Hits.Count != _codexSearchSeen) { _codexSearchSeen = SearchStore.Hits.Count; foreach (var h in SearchStore.Hits) WCodexCards(h.ItemId, h.Cards, "a shop search"); }
            // the monsters' boss marks and levels of units gone (a long session)
            if (_codexUnits.Count > 512) { var gone = new List<uint>(); foreach (var k in _codexUnits.Keys) if (Entities.Get(k) == null) gone.Add(k); foreach (var k in gone) _codexUnits.Remove(k); }
        }

        /// <summary>Packets read here, never taken (their owners handle them): the units' level and boss mark (ZC_NOTIFY_STANDENTRY /
        /// NEWENTRY / MOVEENTRY: clif.cpp:1161, 1174-1180), and the item ids of the shop lists the owners keep to themselves (Game/Shops.cs:
        /// 0x0b77 buy list, 0x0b7a market, 0x0b78 / 0x0b79 barter; Game/PointShop.cs 0x0287).</summary>
        private void WCodexPeek(RoSession s, ushort id, byte[] b, int o, int n)
        {
            if (Codex == null) return;
            try
            {
                var r = new PacketReader(b, o, n);
                switch (id)
                {
                    case PacketTable.Map.idle_unitType: { var p = packet_idle_unit.Parse(ref r, o + n); _codexUnits[p.AID] = ((ushort)p.job, p.clevel, p.isBoss); return; }
                    case PacketTable.Map.spawn_unitType: { var p = packet_spawn_unit.Parse(ref r, o + n); _codexUnits[p.AID] = ((ushort)p.job, p.clevel, p.isBoss); return; }
                    case PacketTable.Map.unit_walkingType: { var p = packet_unit_walking.Parse(ref r, o + n); _codexUnits[p.AID] = ((ushort)p.job, p.clevel, p.isBoss); return; }
                    case 0x00b4: // ZC_SAY_DIALOG len.W npc.L text: a creature an NPC names is "heard of" (Game/CodexHeard.cs)
                    {
                        r.Skip(4); r.U32(); string text = r.Text(n - 8);
                        // (design note, 2026-10-15: "The moment you see the name it is revealed": heard of when the line naming it is shown, the
                        // dialogue finished or not; waiting for the book: the Codex button's green aura, the next opening's page)
                        bool any = false;
                        foreach (int mob in CodexHeard.Find(text))
                            if (Codex.AddHeard(mob, "an NPC's line")) { CodexHeardWrites++; Codex.AddPending(mob); CodexBook?.NoteHeard(); any = true; }
                        if (any) { CodexBook?.JournalChanged(); Codex.SaveNow("a creature heard of"); }
                        return;
                    }
                    case 0x0b77: // ZC_PC_PURCHASE_ITEMLIST len.W {itemId.L price.L discountPrice.L type.B viewSprite.W location.L}
                        r.Skip(4); while (r.Remaining >= 19) { int item = (int)r.U32(); r.Skip(15); WCodexItem(item, "an NPC's shop"); }
                        return;
                    case 0x0b7a: // ZC_NPC_MARKET_OPEN len.W {nameid.L type.B price.L qty.L weight.W location.L}
                        r.Skip(4); while (r.Remaining >= 19) { int item = (int)r.U32(); r.Skip(15); WCodexItem(item, "an NPC's market"); }
                        return;
                    case 0x0b78: // ZC_NPC_BARTER_MARKET_ITEMINFO len.W {nameid.L type.B amount.L currencyNameid.L currencyAmount.L weight.L index.L viewSprite.W location.L}
                        r.Skip(4); while (r.Remaining >= 31) { int item = (int)r.U32(); r.Skip(5); int cur = (int)r.U32(); r.Skip(18); WCodexItem(item, "a barter"); WCodexItem(cur, "a barter"); }
                        return;
                    case 0x0b79: // ZC_NPC_EXPANDED_BARTER_MARKET_ITEMINFO len.W count.L {nameid.L type.W amount.L weight.L index.L zeny.L viewSprite.W location.L currency_count.L {nameid.L refine.W amount.L type.W}×n}
                    {
                        r.Skip(4); int count = r.S32();
                        for (int i = 0; i < count && r.Remaining >= 32; i++)
                        {
                            int item = (int)r.U32(); r.Skip(24); int cc = r.S32(); WCodexItem(item, "a barter");
                            for (int c = 0; c < cc && r.Remaining >= 12; c++) { WCodexItem((int)r.U32(), "a barter"); r.Skip(8); }
                        }
                        return;
                    }
                    case 0x0287: // ZC_PC_CASH_POINT_ITEMLIST len.W cash.L kafra.L {price.L discount.L type.B itemId.L}
                        r.Skip(12); while (r.Remaining >= 13) { r.Skip(9); WCodexItem((int)r.U32(), "a point shop"); }
                        return;
                }
            }
            catch (Exception e) { Debug.LogWarning($"[Codex] packet 0x{id:x4} not read: {e.Message}"); }
        }
    }
}
