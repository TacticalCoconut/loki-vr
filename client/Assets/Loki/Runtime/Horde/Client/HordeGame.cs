using System;
using System.Collections.Generic;
using Loki.Effects;
using Loki.Net;
using Loki.Sprites;
using Loki.UI;
using Loki.VR;
using UnityEngine;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 8 (w8game): the Horde side mode in the client. Talks to the server through ONE interface,
    /// <see cref="IHordeChannel"/> (w8core's Net/IHordeChannel.cs; today Loki Host's relay, or the scripted fake of the offline
    /// smoke), and draws what it says: the prompt, the arena on the table, the run characters, the enemies and everything else
    /// of the run, the HUD, the card and job choices, the results. Nothing here reads or writes an RO packet; the real character
    /// is left to the RO session (LokiGame.W8Horde.cs hides the world while the arena is shown and gives it back afterwards).
    /// Client wave 9 (h9flow): the run's FLOW: a 3-2-1 count-in with sound when the run begins (the stick is ignored meanwhile),
    /// a late joiner's entry effect, the job change and a boss's arrival as banners, a 1 s visual slow-down of the drawing on
    /// the host's final event before the results, "Play again" from the results, the stand-in no longer offered (smoke only).
    /// The hit / death / pickup feedback is h9feel's (<see cref="HordeFeel"/>).
    /// </summary>
    public sealed class HordeGame : MonoBehaviour
    {
        public IHordeChannel Channel { get; private set; }
        public HordeContent Content { get; private set; }
        public HordeArena Arena { get; private set; }
        public HordeWindows Windows { get; private set; }
        public HordeHud Hud { get; private set; }
        public HordeChoice Choice { get; private set; }
        public HordeWorldDraw Draw { get; private set; }
        /// <summary>Client wave 17 (h17pass), point 22: the passives' bodies and their HUD meters (HordePassivesDraw.cs).</summary>
        public HordePassivesDraw Passives { get; private set; }
        /// <summary>Client wave 17 (h17cards): the new cards' look (marks, guards, the falcon, dashes, parries, evolutions).</summary>
        public HordeCardsDraw CardsDraw { get; private set; }
        public HordePlayers Players { get; private set; }
        public HordeNumbers Numbers { get; } = new HordeNumbers();
        public HordeFeel Feel { get; private set; }
        /// <summary>Client wave 17 (h17read): the readability marks, rumbles and the "Combat effects" size (Horde/Client/HordeReadability.cs).</summary>
        public HordeReadability Read { get; private set; }
        /// <summary>Client wave 17 (h17boss), point 23: the bosses' warnings (HordeTelegraphs.cs).</summary>
        public HordeTelegraphs Telegraphs { get; private set; }
        /// <summary>Client wave 17 (h17obj): the map objectives as the player sees them (Horde/Client/HordeObjectives.cs).</summary>
        public HordeObjectives Objectives { get; private set; }
        /// <summary>Client wave 18 (h18rules): the co-op pause's countdown and Back Slide's look (Horde/Client/HordeRulesDraw.cs).</summary>
        public HordeRulesDraw RulesDraw { get; private set; }
        /// <summary>Client wave 18 (h18rules): the HP bars over the elites and bosses (Horde/Client/HordeEliteBars.cs).</summary>
        public HordeEliteBars EliteBars { get; private set; }
        /// <summary>Client wave 9 (h9feel): the arena's sprite frames (HordeFeel pre-builds them during the lobby).</summary>
        public HordeSprites Sprites => _sprites;
        public HordeRunInfo Run { get; private set; }
        public bool InArena { get; private set; }
        /// <summary>The arena's own effect system (a second EffectManager on the arena's map: the GRF's effects by skill / effect id).</summary>
        public EffectManager Fx { get; private set; }

        /// <summary>Set by LokiGame.W8Horde: hide / give back the RO world (true = the arena is shown), a chat line, my map, a look by AID.</summary>
        public Action<bool> Veil;
        public Action<string, string> Say;
        public Func<string> MyMap;
        public Func<uint, PlayerLook> LookOf;
        public uint MyAid;

        private LokiBootstrap _boot; private HordeSprites _sprites;
        private GameObject _fxHost;
        private float _fxWindow; private int _fxInWindow; private float _hudAt;
        public const int MaxEffectsPerSecond = 14;
        /// <summary>The count-in's length (seconds) and the end's visual slow-down (seconds, and how fast the drawing runs meanwhile): Loki's own numbers.</summary>
        public const float CountInSeconds = 3f, EndSlowSeconds = 1f, EndSlowRate = 0.15f;
        // counters for the log and the smoke
        /// <summary>Client wave 15 (w15hfx), point 19: Pause / Resume asked from the run menu; the holder of the pause as last seen (-1 none).</summary>
        public int PauseAsks, PausesSeen;
        private int _heldBy = -1;
        /// <summary>Client wave 18 (h18rules), point 38: invitations received (shown or not), for the relay smoke.</summary>
        public int InvitesGot;
        public int Hits, Kills, LevelUps, JobChanges, Falls, Rises, OffersShown, OffersAnswered, EffectsSpawned, EffectsCapped, CountIns, Bosses, LateJoins, EndSlows;
        /// <summary>Client wave 18 merge (point 24): my level-ups drawn with RO's level-up angel.</summary>
        public int LevelUpAngels;
        /// <summary>Client wave 18 (h18feel), point 35: souls split, shards taken, rises by a gathered soul (seen by this client).</summary>
        public int SoulSplits, SoulsTaken, SoulRises;
        /// <summary>Client wave 18 (h18feel), point 37: lives the Yggdrasil Berry saved (seen by this client).</summary>
        public int LifeSaves;
        public int LastJob { get; private set; } = -1;
        /// <summary>The count-in is running (the stick is ignored).</summary>
        public bool CountingIn => InArena && Time.unscaledTime < _countUntil;
        /// <summary>design note (2026-10-05 00:05): the run PAUSES while any player has an open card / job offer. The host's simulation stops
        /// (h9move, protocol 2); here the stick is dropped, the clock stands (w.Seconds stops with the host's tick) and the HUD says who
        /// is choosing. Read from the players' Choosing flags (every chooser carries one) until h9move's own Paused field lands: ONE place.</summary>
        public static bool IsPaused(HordeWorldView w)
        {
            if (w == null) return false;
            if (w.Paused) return true; // h9move's protocol 2 (007461ae): the host says so
            foreach (var p in w.Players) if (p.Present && (p.Flags & HordePlayerFlags.Choosing) != 0) return true;
            return false;
        }
        public bool Paused => InArena && Channel != null && IsPaused(Channel.World);
        /// <summary>The names of the players choosing now, mine left out ("" when none).</summary>
        public static string Choosers(HordeWorldView w, int mySlot)
        {
            var sb = new System.Text.StringBuilder();
            if (w != null) foreach (var p in w.Players) if (p.Present && p.Slot != mySlot && (p.Flags & HordePlayerFlags.Choosing) != 0) { if (sb.Length > 0) sb.Append(", "); sb.Append(p.Name); }
            return sb.ToString();
        }
        /// <summary>The end's slow-down is running (the arena stays for a moment after the host's final event).</summary>
        public bool Ending => _ending;
        private float _countUntil; private int _countStep = -1;
        private bool _ending; private float _endStart; private double _endNow; private HordeResults _endResults;

        public static HordeGame Create(GameObject host, LokiBootstrap boot, IHordeChannel channel, HordeContent content)
        {
            var g = host.AddComponent<HordeGame>();
            g._boot = boot; g.Channel = channel; g.Content = content;
            g.Arena = HordeArena.Create(boot);
            g._sprites = new HordeSprites(boot.Assets);
            g.Draw = new HordeWorldDraw(g._sprites, g.Arena);
            g.Passives = new HordePassivesDraw(g._sprites, g.Arena); HordePassivesDraw.Content = content; // client wave 17 (h17pass)
            g.CardsDraw = new HordeCardsDraw(g.Arena); // client wave 17 (h17cards)
            g.Players = new HordePlayers(g.Arena, boot.Assets, aid => g.LookOf?.Invoke(aid));
            g.Feel = new HordeFeel(g);
            g.Read = new HordeReadability(g); // client wave 17 (h17read)
            g.Telegraphs = new HordeTelegraphs(g); // client wave 17 (h17boss)
            g.Telegraphs.InDanger = g.Read.InDanger; // client wave 17 (merge h17int): h17read's C5 tick and rumble for h17boss's shapes too
            g.Objectives = new HordeObjectives(g); // client wave 17 (h17obj)
            g.RulesDraw = new HordeRulesDraw(g.Arena); // client wave 18 (h18rules)
            g.EliteBars = new HordeEliteBars(g.Arena.transform); // client wave 18 (h18rules): the elites' HP bars
            System.Func<Camera> head = () => boot.Rig != null ? boot.Rig.Head : Camera.main;
            g.Windows = new HordeWindows(boot.Rig != null ? boot.Rig.transform : boot.transform, head)
            {
                Channel = () => g.Channel, MyMap = () => g.MyMap?.Invoke(),
                Started = () => g.Channel?.RequestStart(), LeaveRun = () => g.Channel?.LeaveRun(), Answered = (id, join) => g.Channel?.Answer(id, join),
                StartWith = list => { if (g.Channel is Loki.Horde.Net.HordeClient hc) hc.RequestStart(list); else g.Channel?.RequestStart(); }, // client wave 18 (h18rules), point 38: only the chosen are invited
                EndRun = id => g.Channel?.EndRun(id), // client wave 14 (w14kit), point 15
                PlayAgain = () => { Debug.Log("[Horde] play again: a new run is asked for"); if (g.Channel is Loki.Horde.Net.HordeClient hc) hc.RequestStart(g.Windows.Chosen()); else g.Channel?.RequestStart(); }, // (client wave 18, h18rules: the same chosen players)
                Prewarm = () => g.Feel?.Prewarm(),
                Pause = on => { g.PauseAsks++; g.Channel?.Pause(on); }, Content = () => g.Content, // client wave 15 (w15hfx), point 19
            };
            g.Hud = HordeHud.Create(boot.Rig != null ? boot.Rig.transform : boot.transform, head());
            g.Choice = HordeChoice.Create(boot.Rig != null ? boot.Rig.transform : boot.transform, boot.Rig);
            g.Choice.Chosen = (offer, idx) => { g.OffersAnswered++; g.Channel?.Choose(offer, idx); };
            g.Hook();
            return g;
        }

        private void Hook() => Hook(Channel, true);

        /// <summary>Subscribes to (or leaves) a channel's events: the mode can change channel while no run is shown (Loki Host's
        /// relay, or the stand-in for playing alone).</summary>
        private void Hook(IHordeChannel c, bool on)
        {
            if (c == null) return;
            if (on) { c.Invited += OnInvited; c.RunStarted += OnRunStarted; c.Happened += OnEvent; c.Offered += OnOffered; c.OfferClosed += OnOfferClosed; c.Ended += OnEnded; c.Rewarded += OnRewarded; c.StatusChanged += OnStatus; c.Lost += OnLost; c.FxEnded += OnFxEnded; c.FxBegan += OnFxBegan; }
            else { c.Invited -= OnInvited; c.RunStarted -= OnRunStarted; c.Happened -= OnEvent; c.Offered -= OnOffered; c.OfferClosed -= OnOfferClosed; c.Ended -= OnEnded; c.Rewarded -= OnRewarded; c.StatusChanged -= OnStatus; c.Lost -= OnLost; c.FxEnded -= OnFxEnded; c.FxBegan -= OnFxBegan; }
        }

        /// <summary>Another channel (only while no run is shown): false when a run is on screen.</summary>
        public bool SetChannel(IHordeChannel c, string why)
        {
            if (InArena || c == null || ReferenceEquals(c, Channel)) return false;
            Hook(Channel, false);
            Channel = c;
            Hook(Channel, true);
            Debug.Log($"[Horde] channel: {c.GetType().Name} ({why})");
            Windows.RefreshPanel();
            return true;
        }

        private void OnInvited(HordeInvite inv)
        {
            InvitesGot++; // (client wave 18, h18rules)
            Feel?.Prewarm(); // client wave 9 (h9feel): the sprites are built during the countdown
            if (Channel.InRun) return;
            if (inv.StarterAid == MyAid) { Debug.Log("[Horde] my own run is gathering: no prompt for me (I am in)"); return; }
            if (!HordeWindows.InvitesShown) { Debug.Log($"[Horde] invitation from {inv.StarterName} not shown (invitations are hidden in the Horde panel)"); return; }
            Windows.ShowInvite(inv);
        }
        private void OnOffered(HordeOffer o) { OffersShown++; { var lk = LookOf?.Invoke(MyAid); if (lk != null) { Choice.Hair = lk.Hair; Choice.HairColor = lk.HairColor; } } // (w15hfx, point 18: the portraits' heads)
            Feel?.OnOffered(o); Choice.Show(o, Content); Choice.SetParty(Channel?.World, Run != null ? Run.Slot : -1); Haptics.Both(Haptics.Kind.LevelUp, 0.5f, 0.15f); } // (h9feel: the card sound)
        private void OnOfferClosed(int id) => Choice.Close(id);
        private void OnRewarded(HordeRewardNote note) { Debug.Log($"[Horde] reward for run {note.RunId}: {note.BaseExp} Base EXP, {note.JobExp} Job EXP"); Windows.SetReward(note, Content); }
        private void OnStatus()
        {
            if (Channel != null && Channel.Status.Phase == HordePhase.Gathering) Feel?.Prewarm(); // (h9feel: the countdown builds the sprites)
            Windows.RefreshPanel();
            if (Windows.ResultsOpen) Windows.RefreshResults(Content); // (the results' "Play again" needs the host's Idle)
        }
        /// <summary>A shot that ended on a hit: its weapon's skill effect where it struck (within the effect budget).</summary>
        private void OnFxEnded(HordeFxView fx, bool hit)
        {
            Feel?.OnFxEnded(fx, hit);
            Telegraphs?.OnFxEnded(fx, hit); // client wave 17 (h17boss): a struck warning flashes
            if (!InArena || !hit || fx == null || fx.Kind != HordeFxKind.Shot) return;
            var w = Channel.World; float t = (w.Tick - fx.StartTick) / (float)Mathf.Max(1, w.TickRate);
            int me = Run != null ? Run.Slot : -1;
            SkillEffect(fx.DefId, fx.X + fx.VX * t, fx.Y + fx.VY * t, 1f, fx.OwnerSlot == me);
        }
        /// <summary>Client wave 15 (w15hfx), point 20: a weapon fired a shot, an area's warning or a zone: its owner's attack animation
        /// (HordePlayers.Attack decides whether it plays), plus base-game ground skill effects for zones and traps.</summary>
        private void OnFxBegan(HordeFxView fx)
        {
            if (InArena) Feel?.OnFxBegan(fx); // client wave 17 (h17read): an enemy's shot / an elite's telegraph sound (E2, E3)
            if (!InArena || fx == null || fx.OwnerSlot < 0) return;
            int me = Run != null ? Run.Slot : -1;
            if (fx.Kind == HordeFxKind.Zone || fx.Kind == HordeFxKind.Trap)
            {
                bool mine = fx.OwnerSlot == me;
                var wd = Content?.Weapon(fx.DefId);
                if (mine && fx.Kind == HordeFxKind.Zone && wd != null && (wd.Zone & ZoneFlags.BlocksEnemies) != 0)
                {
                    float rad = fx.Angle * Mathf.Deg2Rad;
                    float halfLen = fx.Range > 0.1f ? fx.Range : fx.Radius * 1.35f;
                    float dx = Mathf.Cos(rad) * halfLen * 0.55f, dy = Mathf.Sin(rad) * halfLen * 0.55f;
                    Effect("49", fx.X - dx, fx.Y - dy);
                    Effect("49", fx.X, fx.Y);
                    Effect("49", fx.X + dx, fx.Y + dy);
                }
                else
                {
                    SkillEffect(fx.DefId, fx.X, fx.Y, fx.Radius, mine);
                }
            }
            if (fx.Kind != HordeFxKind.Shot && fx.Kind != HordeFxKind.Warning && fx.Kind != HordeFxKind.Zone) return;
            AttackAnim(fx.OwnerSlot, fx.DefId);
        }

        /// <summary>The attack animation of weapon <paramref name="weaponId"/> for <paramref name="slot"/>, at the level it holds it.</summary>
        private void AttackAnim(int slot, int weaponId)
        {
            var wd = Content?.Weapon(weaponId); var w = Channel?.World;
            if (wd == null || w == null || slot < 0 || slot >= w.Players.Length) return;
            int level = 1; foreach (var o in w.Players[slot].Weapons) if (o.DefId == weaponId) { level = o.Level; break; }
            Players.Attack(slot, wd, level);
        }

        private void OnLost() => Say?.Invoke(Lang.T("Horde: the link to the host dropped; joining again when it is back."), "ffb070");

        // ---- wave 27 (wfix): the run's world is first drawn once the arena's load has passed. Measured (w9feel, Unity profiler captures of a
        // development build, design notes): the arena opens in one long frame (the map's load, 350-490 ms) and the next one is long
        // too (110-150 ms: the arena music decoded, LokiAudio.LoadBgm); in the 2-3 frames after it every dynamic-batching job of the
        // renderer runs 10-30 times slower than later (Batch.ProcessGeometryJob: the same ~250 jobs a frame take 22 ms in all with no Horde
        // drawing, 112 ms with the run's world, 3.5 ms a few frames later). The stand-in and the engine spawn the first enemies on the first
        // tick, so their first draw landed in that tail: the frame after the first enemies was 13-28 ms. Drawn two calm frames later, the same
        // first appearance costs the next frame 10.6-12.2 ms (profiled development build), 9.6 ms with HordeFeel's code compiled ahead (normal
        // build). Nothing moves before "Go" (the count-in, CountInSeconds): the enemies appear about two frames later than before, never more
        // than WorldHoldMaxSeconds after the arena opened.
        /// <summary>The run's world (enemies, pickups, rings, bars, readability marks) waits until this many frames in a row took at most
        /// <see cref="HordeFeel.PrewarmHitchMs"/>, and never longer than <see cref="WorldHoldMaxSeconds"/> from the arena's opening.</summary>
        public const int WorldCalmFrames = 2;
        public const float WorldHoldMaxSeconds = 1f;
        private bool _worldHeld; private int _worldCalm; private float _worldHeldSince;
        /// <summary>Frames the run's world waited at the start of the last run (the w9feel smoke).</summary>
        public int WorldHeldFrames { get; private set; }

        private void OnRunStarted(HordeRunInfo run)
        {
            if (_ending) FinishEnd(); // a new run before the last one's slow-down ended (play again at once): the results show now
            Run = run;
            Windows.HideAll();
            if (!Arena.Enter(run.Map))
            {
                Say?.Invoke(Lang.F("Horde: your game data has no map '{0}' (the arena): leaving the run.", run.Map), "ff7a6a");
                Channel.LeaveRun();
                return;
            }
            InArena = true;
            _worldHeld = true; _worldCalm = 0; _worldHeldSince = Time.unscaledTime; WorldHeldFrames = 0; // wave 27 (wfix)
            Veil?.Invoke(true);
            Draw.Clear(); Draw.Run = run; Players.Clear(); Choice.ResetRun(); Read?.Clear(); // (client wave 17, h17read)
            Telegraphs?.Clear(); // client wave 17 (h17boss)
            Passives?.Clear(); // client wave 17 (h17pass)
            CardsDraw?.Clear(); // client wave 17 (h17cards)
            RulesDraw?.Clear(); // client wave 18 (h18rules)
            EliteBars?.Clear(); // client wave 18 (h18rules)
            EliteBars?.Prewarm(Content); // client wave 18 merge (w9feel): the bars' first-use setup in this, the arena's opening frame
            Numbers.Attach(Arena.Root);
            _fxHost = new GameObject("horde-fx"); _fxHost.transform.SetParent(Arena.transform, false);
            var ents = _fxHost.AddComponent<EntityManager>(); ents.Init(_boot.Assets, Arena.Map);
            Fx = _fxHost.AddComponent<EffectManager>(); Fx.Init(_boot.Assets, Arena.Map, ents, null, () => _boot.Rig != null ? _boot.Rig.Head : Camera.main);
            Feel?.OnRunStarted(run);
            Objectives?.OnRunStarted(run); // client wave 17 (h17obj)
            Arena.Focus = new Vector2(Channel.MyX, Channel.MyY);
            Arena.FocusFeet = () => { var u = Players.UnitOf(Run != null ? Run.Slot : -1); return u != null ? u.transform.position : (Vector3?)null; };
            Hud.Show(true);
            var me = run.Slot >= 0 && run.Slot < Channel.World.Players.Length ? Channel.World.Players[run.Slot] : null;
            if (run.Late)
            {
                // dropped into a run in progress at the party's lowest level: a short entry effect at my place, a banner with the level
                LateJoins++;
                Entry(Channel.MyX, Channel.MyY);
                Hud.ShowBanner(Lang.F("Joined at level {0}", me != null ? me.Level : 1), 1.6f, new Color(0.7f, 0.9f, 1f), 48);
                Say?.Invoke(Lang.F("Horde: you joined the run in progress at level {0}.", me != null ? me.Level : 1), "ffd060");
                _countUntil = 0f;
            }
            else
            {
                // the count-in: 3 - 2 - 1 - Go over CountInSeconds, a tick per step; the stick does nothing until "Go"
                CountIns++;
                _countUntil = Time.unscaledTime + CountInSeconds; _countStep = -1;
                Say?.Invoke(Lang.T("Horde: the run begins! Move with the walking stick; your weapons fire by themselves."), "ffd060");
            }
            Debug.Log($"[Horde] run {run.RunId} started for me: slot {run.Slot}, arena {run.Map} cells {run.X0},{run.Y0}-{run.X1},{run.Y1}, {run.RunLength:0} s{(run.Late ? ", joined late" : ", count-in " + CountInSeconds + " s")}");
        }

        /// <summary>A short entry effect where a player drops into the run (the level-up light and a burst: Loki's own choice).</summary>
        private void Entry(float x, float y)
        {
            Effect("371", x, y);
            if (Fx != null && Arena.Root != null) World.Fx.Burst(Arena.Root, Arena.Local(x, y) + Vector3.up * 0.6f, new Color(0.7f, 0.9f, 1f), 1.4f, 24);
        }

        private void OnEnded(HordeResults r)
        {
            Debug.Log($"[Horde] run {r.RunId} over for me: {r.Outcome}, {r.Seconds:0} s, {r.Players.Count} player(s); mine: {(r.Mine() != null ? $"score {r.Mine().Score}, base share {r.Mine().BaseShare}, job share {r.Mine().JobShare}, reward {r.Mine().Reward}" : "none")}");
            if (!InArena || _ending) { if (_ending) FinishEnd(); LeaveArena(); Windows.ShowResults(r, Content); return; }
            // the end: the drawing slows down for a second on the host's final event (purely visual: the host is done), with the outcome as a banner
            _ending = true; _endStart = Time.unscaledTime; _endNow = Channel.Now; _endResults = r; EndSlows++;
            Choice.Close(-1);
            string banner = r.Outcome == HordeOutcome.Victory ? Lang.T("Victory!") : r.Outcome == HordeOutcome.TimeUp ? Lang.T("Time is up") : r.Outcome == HordeOutcome.Defeat ? Lang.T("Everyone fell") : Lang.T("The run was stopped");
            Hud.ShowBanner(banner, EndSlowSeconds + 0.5f, r.Outcome == HordeOutcome.Victory ? new Color(1f, 0.9f, 0.4f) : new Color(1f, 0.75f, 0.6f), 72);
        }

        /// <summary>The slow-down is over: the arena down, the world back, the results (they stay until dismissed).</summary>
        private void FinishEnd()
        {
            if (!_ending) return;
            _ending = false;
            var r = _endResults; _endResults = null;
            LeaveArena();
            if (r != null) Windows.ShowResults(r, Content);
        }

        /// <summary>The arena down, the world back (the run ended, I left it, or the game is leaving the world).</summary>
        public void LeaveArena()
        {
            if (_ending) { _ending = false; var r = _endResults; _endResults = null; if (r != null) Windows.ShowResults(r, Content); }
            if (!InArena) return;
            InArena = false; _countUntil = 0f;
            Choice.Close(-1); Hud.Show(false); Windows.HideRunMenu(); _heldBy = -1;
            Players.Clear(); Draw.Clear(); Draw.Run = null; Numbers.Attach(null); Read?.Clear(); // (client wave 17, h17read)
            Telegraphs?.Clear(); // client wave 17 (h17boss)
            Passives?.Clear(); // client wave 17 (h17pass)
            CardsDraw?.Clear(); // client wave 17 (h17cards)
            RulesDraw?.Clear(); // client wave 18 (h18rules)
            EliteBars?.Clear(); // client wave 18 (h18rules)
            Feel?.OnLeave();
            Objectives?.OnLeave(); // client wave 17 (h17obj)
            if (_fxHost != null) { Fx.Clear(); Destroy(_fxHost); _fxHost = null; Fx = null; }
            Arena.Leave();
            Veil?.Invoke(false);
            Run = null;
        }

        private bool EffectBudget()
        {
            if (Time.time > _fxWindow) { _fxWindow = Time.time + 1f; _fxInWindow = 0; }
            if (_fxInWindow >= MaxEffectsPerSecond || (Fx != null && Fx.LiveCount > 80)) { EffectsCapped++; return false; }
            _fxInWindow++; return true;
        }

        /// <summary>A Ragnarok effect (by effect id) at a place of the arena, within the budget.</summary>
        private void Effect(string key, float x, float y)
        {
            if (Fx == null || string.IsNullOrEmpty(key) || !EffectBudget()) return;
            if (Fx.Spam(key, 0, Arena.Local(x, y), Time.time, -1f) > 0) EffectsSpawned++;
        }

        private static string ResolveGroundUnitEffectKey(int skillId)
        {
            switch (skillId)
            {
                case 18: return "49";   // MG_FIREWALL -> EF_FIREWALL
                case 12: return "315";  // MG_SAFETYWALL -> EF_GLASSWALL
                case 25: return "141";  // AL_PNEUMA -> EF_PNEUMA
                case 70: return "83";   // AL_SANCTUARY -> EF_SANCTUARY
                case 79: return "113";  // PR_MAGNUS -> EF_MAGNUSEXORCISMUS
                case 80: return "96";   // WZ_FIREPILLAR -> EF_FIREPILLAR
                case 83: return "92";   // WZ_METEOR -> EF_METEORSTORM
                case 85: return "90";   // WZ_VERMILION -> EF_LORD
                case 89: return "89";   // WZ_STORMGUST -> EF_STORMGUST
                case 91: return "142";  // WZ_HEAVENDRIVE -> EF_HEAVENSDRIVE
                case 92: return "95";   // WZ_QUAGMIRE -> EF_QUAGMIRE
                case 115: return "102"; // HT_LANDMINE -> EF_CLAYMORE
                case 122: return "106"; // HT_BLASTMINE -> EF_BLASTMINEBOMB
                case 123: return "102"; // HT_CLAYMORETRAP -> EF_CLAYMORE
                case 285: return "225"; // SA_VOLCANO -> EF_VOLCANO
                case 286: return "236"; // SA_DELUGE -> EF_DELUGE
                case 287: return "237"; // SA_VIOLENTGALE -> EF_VIOLENTGALE
                default: return null;
            }
        }

        private static string ResolveSkillEffectKey(int skillId)
        {
            if (skillId <= 0) return null;
            string groundKey = ResolveGroundUnitEffectKey(skillId);
            if (groundKey != null && EffectDb.Effect(groundKey) != null) return groundKey;
            var ids = EffectDb.EffectIdFor(skillId, -1);
            if (ids != null)
                for (int i = 0; i < ids.Length; i++)
                    if (!string.IsNullOrEmpty(ids[i]) && EffectDb.Effect(ids[i]) != null) return ids[i];
            var sk = EffectDb.Skill(skillId);
            if (sk != null)
            {
                var groups = new[] { sk.EffectId, sk.GroundEffectId, sk.HitEffectId, sk.SuccessEffectId, sk.EffectIdOnCaster, sk.BeforeHitEffectId, sk.ReleaseEffectId, sk.BeginCastEffectId };
                for (int g = 0; g < groups.Length; g++)
                {
                    var cand = groups[g];
                    if (cand == null) continue;
                    for (int i = 0; i < cand.Length; i++)
                        if (!string.IsNullOrEmpty(cand[i]) && EffectDb.Effect(cand[i]) != null) return cand[i];
                }
            }
            return groundKey;
        }

        private readonly Dictionary<int, float> _zoneNextFxAt = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _hitNextFxAt = new Dictionary<int, float>();

        private void TickZoneEffects(HordeWorldView w, int me)
        {
            if (w == null || w.Fx == null || Fx == null) return;
            float t = Time.time;
            if (_zoneNextFxAt.Count > 96) _zoneNextFxAt.Clear();
            for (int i = 0; i < w.Fx.Count; i++)
            {
                var fx = w.Fx[i];
                if (fx.Kind != HordeFxKind.Zone && fx.Kind != HordeFxKind.Trap) continue;
                bool hadPrev = _zoneNextFxAt.TryGetValue(fx.Id, out float nextAt);
                if (hadPrev && t < nextAt) continue;
                _zoneNextFxAt[fx.Id] = t + (fx.Kind == HordeFxKind.Zone ? 3.5f : 2.5f);
                bool mine = fx.OwnerSlot == me;
                var wd = Content?.Weapon(fx.DefId);
                if (wd != null && (wd.SkillId == 18 || (wd.Zone & ZoneFlags.BlocksEnemies) != 0) && mine)
                {
                    // MG_FIREWALL / linear wall: spawn 3 clean flame pillars along the wall's linear angle
                    float rad = fx.Angle * Mathf.Deg2Rad;
                    float halfLen = fx.Range > 0.1f ? fx.Range : Mathf.Max(0.8f, fx.Radius * 1.35f);
                    float dx = Mathf.Cos(rad) * halfLen * 0.55f, dy = Mathf.Sin(rad) * halfLen * 0.55f;
                    Effect("49", fx.X - dx, fx.Y - dy);
                    Effect("49", fx.X, fx.Y);
                    Effect("49", fx.X + dx, fx.Y + dy);
                }
                else if (wd != null && (wd.SkillId == 31 || wd.SkillId == 70 || wd.Id == 4 || wd.ItemId == 523))
                {
                    // Aqua Benedicta / Holy Water / Sanctuary: HordeWorldDraw already renders the ground ring; only play a subtle ripple on initial placement
                    if (!hadPrev)
                        SkillEffect(fx.DefId, fx.X, fx.Y, fx.Radius, mine);
                }
                else
                {
                    SkillEffect(fx.DefId, fx.X, fx.Y, fx.Radius, mine);
                }
            }
        }

        /// <summary>The weapon's skill effect (EffectDb by skill id, as the world draws that skill), else a plain burst.
        /// Ally effects (!mine) are drawn very faintly so only your own skills stand out.</summary>
        private void SkillEffect(int weaponId, float x, float y, float radius, bool mine = true)
        {
            var w = Content?.Weapon(weaponId);
            if (!mine)
            {
                if (!EffectBudget() || Arena.Root == null) return;
                var c = HordeReadability.AllyTint(World.Fx.ElementColor(w != null ? (int)w.Element : 0));
                c.a = 0.16f;
                World.Fx.Burst(Arena.Root, Arena.Local(x, y) + Vector3.up * 0.4f, c, Mathf.Clamp(radius * 0.35f, 0.4f, 1.5f), 6);
                return;
            }
            if (w != null && (w.SkillId == 31 || w.Id == 4 || w.ItemId == 523))
            {
                // Tone down Aqua Benedicta / Holy Water: subtle low-profile water splash instead of tall blinding geyser
                if (!EffectBudget() || Arena.Root == null) return;
                World.Fx.Burst(Arena.Root, Arena.Local(x, y) + Vector3.up * 0.15f, new Color(0.45f, 0.85f, 1f, 0.35f), Mathf.Clamp(radius * 0.32f, 0.35f, 1.0f), 6);
                return;
            }
            string fxKey = w != null ? ResolveSkillEffectKey(w.SkillId) : null;
            if (fxKey != null) { Effect(fxKey, x, y); return; }
            if (!EffectBudget()) return;
            World.Fx.Burst(Arena.Root, Arena.Local(x, y) + Vector3.up * 0.5f, World.Fx.ElementColor(w != null ? (int)w.Element : 0), Mathf.Clamp(radius * 0.5f, 0.6f, 3f), 18);
        }

        private void OnEvent(HordeEvent e)
        {
            if (!InArena) return;
            Feel?.OnEvent(e);
            Read?.OnEvent(e); // client wave 17 (h17read): the readability marks and rumbles (D5 is the hurt rumble now)
            Telegraphs?.OnEvent(e); // client wave 17 (h17boss): a boss's strike, its second phase, a slowed player
            int me = Run != null ? Run.Slot : -1;
            Passives?.OnEvent(e, Content, Channel.World, me); // client wave 17 (h17pass): the passives' triggers, dodges, criticals, swings
            Objectives?.OnEvent(e, me); // client wave 17 (h17obj): the map objectives' banners
            CardsDraw?.OnEvent(this, e, me); // client wave 17 (h17cards): a dash, an evolution, a parry
            RulesDraw?.OnEvent(e, Content, me); // client wave 18 (h18rules): Back Slide's trail explodes
            switch (e.Kind)
            {
                case HordeEventKind.Hit:
                    Hits++;
                    if (e.Slot == me)
                    {
                        var hw = e.DefId > 0 ? Content?.Weapon(e.DefId) : null;
                        if (hw != null && hw.SkillId > 0 && (!_hitNextFxAt.TryGetValue(e.DefId, out float hNext) || Time.time >= hNext))
                        {
                            _hitNextFxAt[e.DefId] = Time.time + 1.25f;
                            if (hw.SkillId == 18 || hw.SkillId == 31 || hw.Kind == WeaponKind.GroundZone || hw.Kind == WeaponKind.AreaAtTarget || hw.Kind == WeaponKind.Nova)
                            {
                                // Ground zones, AreaAtTarget, and Novas already play their cast/strike effect; avoid stacking per-enemy skill VFX on Hit
                                if ((Hits % 8) == 0)
                                    Effect(EffectManager.EF_HIT1.ToString(), e.X, e.Y);
                            }
                            else
                            {
                                string hfx = ResolveSkillEffectKey(hw.SkillId);
                                if (hfx != null)
                                    Effect(hfx, e.X, e.Y);
                                else if ((Hits % 6) == 0)
                                    Effect(EffectManager.EF_HIT1.ToString(), e.X, e.Y);
                            }
                        }
                        else if ((Hits % 8) == 0)
                        {
                            Effect(EffectManager.EF_HIT1.ToString(), e.X, e.Y);
                        }
                    }
                    break;
                case HordeEventKind.PlayerHit:
                    Players.Pop(e.Slot, (e.Flags & HordeEventFlags.Dodged) != 0 ? Lang.T("Miss") : e.Value.ToString(), HitNumbers.Red);
                    if (e.Slot == me && (e.Flags & HordeEventFlags.Dodged) == 0 && Read == null) Haptics.Both(Haptics.Kind.HitTaken, 0.4f, 0.06f); // (client wave 17, h17read: HordeReadability rumbles by the hit's side)
                    break;
                case HordeEventKind.Heal: Players.Pop(e.Slot, "+" + e.Value, new Color(0.4f, 1f, 0.5f)); break;
                case HordeEventKind.Swing:
                {
                    // client wave 15 (w15hfx), point 14: a melee swing is a SLICE over its real arc and reach (HordeWorldDraw.Slice), the
                    // skill's own RO effect on top when it has one; no plain burst any more (design note: "instead of just a little burst"); point 20:
                    // the swinger's attack animation
                    Draw.Slice(e.Slot, e.X, e.Y, e.A, e.B, e.C, e.Slot == me);
                    if (e.Slot == me)
                    {
                        var sw = Content?.Weapon(e.DefId); string sfx = sw != null ? ResolveSkillEffectKey(sw.SkillId) : null;
                        if (sfx != null) Effect(sfx, e.X, e.Y);
                    }
                    AttackAnim(e.Slot, e.DefId);
                    break;
                }
                case HordeEventKind.Nova: case HordeEventKind.Strike:
                    if (HordeRulesDraw.OwnsStrike(e, Content)) break; // client wave 18 (h18rules): Back Slide's trail explosion is HordeRulesDraw's look
                    SkillEffect(e.DefId, e.X, e.Y, e.A, e.Slot == me);
                    if (e.Kind == HordeEventKind.Nova && Content?.Weapon(e.DefId)?.Kind == WeaponKind.Nova) AttackAnim(e.Slot, e.DefId); // (w15hfx, point 20: not an aura's pulse)
                    break;
                case HordeEventKind.Chain:
                    SkillEffect(e.DefId, e.X2, e.Y2, 1f, e.Slot == me);
                    AttackAnim(e.Slot, e.DefId); // (w15hfx, point 20: every link asks; one plays at most)
                    break;
                case HordeEventKind.EnemyDied:
                    Kills++;
                    break;
                case HordeEventKind.BossAppeared:
                {
                    Bosses++;
                    var d = Content?.Enemy(e.DefId);
                    string name = d != null ? MobName(d.MobId) : "?";
                    bool final = (e.Flags & HordeEventFlags.Final) != 0, boss = (e.Flags & HordeEventFlags.Boss) != 0;
                    Say?.Invoke(final ? Lang.F("Horde: the final boss, {0}, is here!", name) : boss ? Lang.F("Horde: a boss appears: {0}!", name) : Lang.F("Horde: an elite appears: {0}.", name), "ff9a6a");
                    Hud.ShowBanner(final ? Lang.F("{0} · the final boss!", name) : boss ? Lang.F("{0} · boss", name) : Lang.F("{0} · elite", name), 1.8f, new Color(1f, 0.55f, 0.45f), 48);
                    break;
                }
                case HordeEventKind.LevelUp:
                    // design note, 2026-10-04: a run has dozens of level-ups and RO's fanfare "gets tiring fast": a short ding here
                    // (data\wav\se_ding2.wav of the install), the fanfare only at a job change below
                    if (e.Slot == me) { LevelUps++; Audio.LokiAudio.Instance?.PlayWav("se_ding2.wav"); }
                    // client wave 18 merge, point 24 (design note: "Test corpse persistence first."): RO's level-up angel (EF_ANGEL 371) is BACK,
                    // to test whether h18feel's dead-body fix alone ends the "ghost" over the monsters; h18feel's gold ground ring
                    // (HordeReadability.LevelUp) is kept for a switch back: Read?.LevelUp(e.Slot) in place of these two lines
                    { var p = Channel.World.Players[Mathf.Clamp(e.Slot, 0, 7)]; int fx0 = EffectsSpawned; Effect("371", p.X, p.Y); if (e.Slot == me && EffectsSpawned > fx0) LevelUpAngels++; }
                    break;
                case HordeEventKind.JobChanged:
                {
                    JobChanges++;
                    var p = Channel.World.Players[Mathf.Clamp(e.Slot, 0, 7)];
                    if (e.Slot == me) LastJob = e.Value;
                    Effect("158", p.X, p.Y);
                    Say?.Invoke(Lang.F("Horde: {0} is now a {1}.", p.Name, HordeText.Job(e.Value)), "ffd060");
                    if (e.Slot == me)
                    {
                        Audio.LokiAudio.Instance?.PlayWav("levelup.wav"); // RO's level-up fanfare, kept for job changes
                        Hud.ShowBanner(Lang.F("You are now a {0}!", HordeText.Job(e.Value)), 1.5f, new Color(1f, 0.9f, 0.55f), 52); // the ceremony's banner (h9flow)
                    }
                    break;
                }
                case HordeEventKind.Fell:
                {
                    Falls++; var p = Channel.World.Players[Mathf.Clamp(e.Slot, 0, 7)];
                    Say?.Invoke(e.Slot == me ? Lang.T("Horde: you fell!") : Lang.F("Horde: {0} fell!", p.Name), "ff7a6a"); // (client wave 18, h18feel, point 35: how one rises is said by SoulSplit)
                    if (e.Slot == me) Haptics.Both(Haptics.Kind.Death, 0.8f, 0.4f);
                    break;
                }
                case HordeEventKind.Rose:
                {
                    Rises++; var p = Channel.World.Players[Mathf.Clamp(e.Slot, 0, 7)];
                    // client wave 18 (h18feel), point 35: Value 1 = its soul gathered (Target: by whom; it is unhurt for a while), 2 = a class-change level
                    var by = e.Target >= 0 && e.Target < Channel.World.Players.Length ? Channel.World.Players[e.Target] : null; float unhurt = Content?.Souls != null ? Content.Souls.InvulnerableSeconds : 5f;
                    if (e.Value == 1 && by != null)
                    {
                        Say?.Invoke(e.Slot == me ? Lang.F("Horde: {0} brought you back: unhurt for {1} s.", by.Name, Mathf.RoundToInt(unhurt)) : Lang.F("Horde: {0} brought {1} back.", e.Target == me ? Lang.T("~soul~you") : by.Name, p.Name), "a8e6a3");
                        SoulRises++;
                    }
                    else Say?.Invoke(e.Slot == me ? Lang.T("Horde: you are back on your feet.") : Lang.F("Horde: {0} is back on their feet.", p.Name), "a8e6a3");
                    // client wave 19 (h18feel), the revive follow-up: BOTH rises (1 soul shards, 2 the class-change level) give the rule's
                    // seconds unhurt and the speed: the same banner for me ("Back! 5s unhurt, faster") and a speed trail on the runner
                    if (e.Value == 1 || e.Value == 2)
                    {
                        if (Objectives != null) Objectives.Risen(e, me);
                        else if (e.Slot == me) Hud.ShowBanner(HordeObjectives.RiseText(Content), 2f, HordeObjectives.SoulColor, 50);
                    }
                    Read?.Pulse(e.Slot, HordeObjectives.SoulColor); // (client wave 18, h18feel: a ring on the ground, not RO's angel over the monsters: point 24)
                    break;
                }
                // ---- client wave 18 (h18feel), point 37: the Yggdrasil Berry saved a life (full HP, 4 s invulnerable, the enemies round it cleared)
                case HordeEventKind.LifeSaved:
                {
                    LifeSaves++; var p = Channel.World.Players[Mathf.Clamp(e.Slot, 0, 7)];
                    if (e.Slot == me) { Hud.ShowBanner(Lang.T("Saved by Yggdrasil!"), 2f, new Color(0.6f, 1f, 0.6f), 56); Haptics.Both(Haptics.Kind.LevelUp, 0.7f, 0.25f); }
                    Say?.Invoke(e.Slot == me ? (e.Value > 0 ? Lang.F("Horde: the Yggdrasil Berry saved you. It rests {0} s.", e.Value) : Lang.T("Horde: the Yggdrasil Berry saved you. It is spent.")) : Lang.F("Horde: the Yggdrasil Berry saved {0}.", p.Name), "9ef0a0");
                    var heal = EffectDb.EffectIdFor(28, -1); // AL_HEAL's effect (rAthena skill 28): the berry's heal, as the world draws it
                    if (heal != null && heal.Length > 0 && EffectDb.Effect(heal[0]) != null) Effect(heal[0], e.X, e.Y);
                    if (Arena?.Root != null) World.Fx.Burst(Arena.Root, Arena.Local(e.X, e.Y) + Vector3.up * 0.6f, new Color(0.6f, 1f, 0.55f), (Content?.Souls != null ? Content.Souls.SaveClearCells : 4f) * 0.6f, 32);
                    Read?.Pulse(e.Slot, new Color(0.6f, 1f, 0.55f));
                    break;
                }
                // ---- client wave 18 (h18feel), point 35: soul retrieval (HordeSim.Souls.cs)
                case HordeEventKind.SoulSplit:
                {
                    SoulSplits++; var p = Channel.World.Players[Mathf.Clamp(e.Slot, 0, 7)];
                    if (e.Slot == me) { Say?.Invoke(Lang.F("Horde: your soul split into {0} shards: allies can gather them.", e.Value), "c9c0ff"); Hud.ShowBanner(Lang.F("Soul {0} / {1}", 0, e.Value), 2f, HordeObjectives.SoulColor, 50); }
                    else { Say?.Invoke(Lang.F("Horde: {0}'s soul split into {1} shards: stand in them to bring {0} back.", p.Name, e.Value), "c9c0ff"); Hud.ShowBanner(Lang.F("{0} fell · {1} soul shards", p.Name, e.Value), 2f, HordeObjectives.SoulColor, 50); }
                    if (Arena?.Root != null) World.Fx.Burst(Arena.Root, Arena.Local(e.X, e.Y) + Vector3.up * 0.6f, HordeObjectives.SoulColor, 1.2f, 18);
                    break;
                }
                case HordeEventKind.SoulTaken:
                {
                    SoulsTaken++;
                    if (Arena?.Root != null) World.Fx.Burst(Arena.Root, Arena.Local(e.X, e.Y) + Vector3.up * 0.5f, HordeObjectives.SoulColor, 0.9f, 12);
                    if (me >= 0 && (e.DefId & (1 << me)) != 0) { var sc = Channel.World.SoulCountOf(me); if (sc.Total > 0) Hud.ShowBanner(Lang.F("Soul {0} / {1}", sc.Total - sc.Need, sc.Total), 1.2f, HordeObjectives.SoulColor, 50); }
                    break;
                }
                case HordeEventKind.FinalPhase:
                    Say?.Invoke(Lang.T("Horde: the run's time is over: the final boss comes!"), "ff9a6a");
                    Hud.ShowBanner(Lang.T("The final boss comes!"), 1.8f, new Color(1f, 0.55f, 0.45f), 48);
                    break;
                case HordeEventKind.Incoming:
                {
                    // h9move 5a8420af / h9content: an elite or boss announced WarnSeconds before it comes (the arrival is an event)
                    var d = Content?.Enemy(e.DefId);
                    string name = d != null ? MobName(d.MobId) : "?";
                    Say?.Invoke(Lang.F("Horde: {0} is coming in {1} s!", name, e.Value), "ff9a6a");
                    Hud.ShowBanner(Lang.F("{0} is coming in {1} s!", name, e.Value), 2f, new Color(1f, 0.75f, 0.45f), 44);
                    break;
                }
                case HordeEventKind.PlayerJoined:
                    if (e.Slot != me)
                    {
                        var p = Channel.World.Players[Mathf.Clamp(e.Slot, 0, 7)];
                        Say?.Invoke((e.Flags & HordeEventFlags.Late) != 0 ? Lang.F("Horde: {0} joined the run at level {1}.", p.Name, p.Level) : Lang.F("Horde: {0} joined the run.", p.Name), "9ad1ff");
                        if ((e.Flags & HordeEventFlags.Late) != 0) { LateJoins++; Entry(p.X, p.Y); }
                    }
                    break;
                case HordeEventKind.PlayerLeft: if (e.Slot != me) Say?.Invoke(Lang.F("Horde: {0} left the run.", Channel.World.Players[Mathf.Clamp(e.Slot, 0, 7)].Name), "9ad1ff"); break;
                case HordeEventKind.EnemyAbility: case HordeEventKind.EnemyBurst:
                    if (e.DefId > 0 && e.Kind == HordeEventKind.EnemyAbility) { var ids = EffectDb.EffectIdFor(e.DefId, -1); if (ids != null && ids.Length > 0) Effect(ids[0], e.X, e.Y); }
                    else if (EffectBudget()) World.Fx.Burst(Arena.Root, Arena.Local(e.X, e.Y) + Vector3.up * 0.5f, new Color(1f, 0.35f, 0.25f), Mathf.Clamp(e.A * 0.5f, 0.8f, 3f), 20);
                    break;
            }
        }

        /// <summary>A monster's name from the client's own sprite table (the install's npc identity names), made readable.</summary>
        public static string MobName(int mobId)
        {
            string n = RoNames.MobName(mobId);
            if (string.IsNullOrEmpty(n)) return "#" + mobId;
            n = n.Replace('_', ' ').ToLowerInvariant();
            return n.Length > 0 ? char.ToUpperInvariant(n[0]) + n.Substring(1) : n;
        }

        private void Update()
        {
            var c = Channel; if (c == null) return;
            try { c.Poll(); } catch (Exception e) { Debug.LogException(e); }
            Windows.Tick();
            if (_ending && Time.unscaledTime - _endStart >= EndSlowSeconds) { FinishEnd(); return; }
            if (!InArena) return;
            // the count-in's steps: "3", "2", "1", "Go!" with a tick each (the UI click, the ding at Go: the install's own sounds)
            if (Time.unscaledTime < _countUntil)
            {
                int step = Mathf.CeilToInt(_countUntil - Time.unscaledTime); // 3, 2, 1
                if (step != _countStep)
                {
                    _countStep = step;
                    Hud.ShowBanner(step.ToString(), 1.1f, new Color(1f, 0.9f, 0.55f), 120);
                    Audio.LokiAudio.Instance?.PlayUi();
                }
            }
            else if (_countStep > 0) { _countStep = 0; Hud.ShowBanner(Lang.T("Go!"), 0.9f, new Color(0.7f, 1f, 0.6f), 110); Audio.LokiAudio.Instance?.PlayWav("se_ding2.wav"); }
            // my stick, relative to where I look, into the map's own directions (x east, y north); nothing during the count-in or a choice
            var rig = _boot.Rig;
            Vector2 v = rig != null && !Choice.Open && !rig.Off.CameraGrip ? rig.Off.StickValue : Vector2.zero; // (client wave 13: in resize mode grip + stick moves the table; outside it a grip does not stop the run character)
            if (CountingIn || _ending || Paused) v = Vector2.zero; // paused: the stick does nothing (the design's rule, 2026-10-05)
            if (v.magnitude < 0.15f) v = Vector2.zero;
            Vector2 mv = Vector2.zero;
            if (v != Vector2.zero && Arena.Root != null)
            {
                var head = rig != null ? rig.Head.transform : Camera.main.transform;
                var dirWorld = Quaternion.Euler(0, head.eulerAngles.y, 0) * new Vector3(v.x, 0, v.y);
                var dirMap = Arena.Root.InverseTransformDirection(dirWorld); dirMap.y = 0;
                if (dirMap.sqrMagnitude > 1e-8f) mv = new Vector2(dirMap.x, dirMap.z).normalized * Mathf.Clamp01(v.magnitude);
            }
            c.SetMove(mv.x, mv.y);
            UpdateActiveSkills(c, rig);
        }

        private readonly float[] _activeReadyAt = new float[2];
        private void UpdateActiveSkills(IHordeChannel c, VR.DioramaRig rig)
        {
            Draw.SetActiveAim(0, false, Vector2.zero, 0f, false, 0f, null);
            Draw.SetActiveAim(1, false, Vector2.zero, 0f, false, 0f, null);
            int me = Run != null ? Run.Slot : -1;
            var w = c?.World;
            if (w == null || me < 0 || me >= w.Players.Length || !w.Players[me].Present || w.Players[me].Fallen) return;
            if (CountingIn || _ending || Paused || Choice.Open || Windows.PanelOpen || Windows.RunMenuOpen)
            {
                if (Time.deltaTime > 0f)
                {
                    if (_activeReadyAt[0] > Time.time) _activeReadyAt[0] += Time.deltaTime;
                    if (_activeReadyAt[1] > Time.time) _activeReadyAt[1] += Time.deltaTime;
                }
                return;
            }
            var p = w.Players[me];
            // Up to 2 active skills: index 0 -> Right/Main hand, index 1 -> Left/Off hand.
            // Only dedicated active skills (IsActiveWeapon) are equipped to hand triggers; players start with no active skill.
            int slot0 = -1, slot1 = -1;
            for (int i = 0; i < p.Weapons.Count; i++)
            {
                if (!HordeSim.IsActiveWeapon(p.Weapons[i].DefId)) continue;
                if (slot0 < 0) slot0 = i;
                else if (slot1 < 0) { slot1 = i; break; }
            }
            for (int hand = 0; hand < 2; hand++)
            {
                int wIdx = hand == 0 ? slot0 : slot1;
                if (wIdx < 0) continue;
                var owned = p.Weapons[wIdx];
                var wd = Content?.Weapon(owned.DefId);
                if (wd == null || wd.Levels == null || wd.Levels.Length == 0) continue;
                var lv = wd.Levels[Mathf.Clamp(owned.Level, 1, wd.Levels.Length) - 1];
                var handInput = rig != null ? (hand == 0 ? rig.Main : rig.Off) : null;
                if (rig == null || handInput == null) continue;
                bool uiHit = hand == 0 ? rig.LaserButton != null : rig.OffLaserButton != null;
                if (uiHit) continue;
                var ray = hand == 0 ? rig.PointerRayNow() : rig.OffRay;
                if (!Arena.GroundCell(ray, out var cell))
                {
                    if (Arena.Root == null) continue;
                    var dirLocal = Arena.Root.InverseTransformDirection(ray.direction);
                    var flat = new Vector2(dirLocal.x, dirLocal.z);
                    if (flat.sqrMagnitude < 1e-4f)
                    {
                        float faceRad = p.Facing * Mathf.Deg2Rad;
                        flat = new Vector2(Mathf.Cos(faceRad), Mathf.Sin(faceRad));
                    }
                    else flat.Normalize();
                    float fallbackDist = Mathf.Clamp(lv.Range > 0f ? lv.Range * 0.6f : 3.5f, 2f, 5.5f);
                    cell = new Vector2(p.X + flat.x * fallbackDist, p.Y + flat.y * fallbackDist);
                }
                if (Run != null && Run.X1 > Run.X0 && Run.Y1 > Run.Y0)
                {
                    cell.x = Mathf.Clamp(cell.x, Run.X0 + 0.5f, Run.X1 + 0.5f);
                    cell.y = Mathf.Clamp(cell.y, Run.Y0 + 0.5f, Run.Y1 + 0.5f);
                }
                float cdTotal = HordeSim.ActiveCooldownSeconds(owned.DefId);
                float rem = Mathf.Max(0f, _activeReadyAt[hand] - Time.time);
                bool ready = rem <= 0.01f;
                float radius = Mathf.Max(1.6f, (lv.Area > 0f ? lv.Area : 2.2f) * 1.45f);
                bool isWall = (wd.Zone & ZoneFlags.BlocksEnemies) != 0;
                float wallAngle = 0f;
                if (isWall)
                {
                    float dx = cell.x - p.X, dy = cell.y - p.Y;
                    float aimAng = dx * dx + dy * dy > 1e-4f ? Mathf.Atan2(dy, dx) * Mathf.Rad2Deg : p.Facing;
                    wallAngle = Mathf.Repeat(aimAng + 90f, 360f);
                }
                Draw.SetActiveAim(hand, true, cell, radius, ready, Mathf.Clamp01(rem / cdTotal), HordeText.Weapon(Content, owned.DefId), isWall, wallAngle);
                if (handInput.TriggerDown || handInput.PullDown || handInput.ClickDown)
                {
                    if (ready)
                    {
                        c.CastActive(owned.DefId, cell.x, cell.y);
                        _activeReadyAt[hand] = Time.time + cdTotal;
                        VR.Haptics.Pulse(VR.Haptics.Kind.Ui, hand == 0, 0.45f, 0.05f);
                    }
                    else
                    {
                        Audio.LokiAudio.Instance?.PlayUi();
                    }
                }
            }
        }

        private void LateUpdate()
        {
            if (!InArena || Channel == null) { Feel?.LateUpdate(); return; } // (h9feel: the sprite pre-build runs in the lobby too)
            var w = Channel.World; int me = Run != null ? Run.Slot : -1;
            // the end's slow-down: the drawing's clock runs at EndSlowRate from the final event on (shots, orbits, walks between the last two snapshots)
            double now = _ending ? _endNow + (Channel.Now - _endNow) * EndSlowRate : Channel.Now;
            var my = new Vector2(Channel.MyX, Channel.MyY);
            Arena.Focus = my;
            // client wave 15 (w15hfx), point 19: the menu's pause, as everyone sees it (the holder is marked in the snapshot)
            var holder = HordeWindows.Holder(w); int heldBy = holder != null ? holder.Slot : -1;
            if (heldBy != _heldBy)
            {
                bool alone = HordeWindows.Alone(w, me); // client wave 18 (h18rules), point 23: alone the menu pauses at each opening: no chat line for it
                if (heldBy >= 0) { PausesSeen++; Hud.ShowBanner(Lang.T("Paused"), 1.4f, new Color(0.75f, 0.9f, 1f), 72); if (!alone) Say?.Invoke(Lang.F("Horde: {0} paused the run.", holder.Name), "9ad1ff"); }
                else if (_heldBy >= 0) { Hud.ShowBanner(Lang.T("Go!"), 0.9f, new Color(0.7f, 1f, 0.6f), 90); if (!alone) Say?.Invoke(Lang.T("Horde: the run goes on."), "9ad1ff"); }
                Debug.Log($"[Horde] the menu's pause: {(heldBy >= 0 ? "held by slot " + heldBy + " (" + holder.Name + ")" : "released")}");
                _heldBy = heldBy;
                Windows.RefreshRunMenu();
            }
            // h9move's table follow (protocol 2): the mode from Settings → Horde, my velocity for its lead
            Arena.Follow.Mode = (HordeFollowMode)HordeHud.FollowMode;
            Arena.FocusVel = me >= 0 && me < w.Players.Length ? new Vector2(w.Players[me].Vx, w.Players[me].Vy) : Vector2.zero;
            if (_worldHeld) // wave 27 (wfix): the arena's load first (WorldCalmFrames)
            {
                _worldCalm = Time.unscaledDeltaTime * 1000f <= HordeFeel.PrewarmHitchMs ? _worldCalm + 1 : 0;
                if (_worldCalm >= WorldCalmFrames || Time.unscaledTime - _worldHeldSince >= WorldHoldMaxSeconds) _worldHeld = false; else WorldHeldFrames++;
            }
            Draw.Hold = _worldHeld;
            Players.Update(w, now, me, my, MyAid, CountingIn || _ending || Paused); // (w15hfx: no attack animation while still)
            Draw.Draw(w, Content, now, me, my); // (h9feel: my predicted place, for the pickups' flights)
            if (!_worldHeld) TickZoneEffects(w, me);
            if (!_worldHeld) EliteBars?.Update(w, Content); // client wave 18 (h18rules): the elites' and bosses' HP bars where the world drew them
            Passives?.Draw(w, Content, now, me, my); // client wave 17 (h17pass): the passives' bodies
            if (!_worldHeld) CardsDraw?.Draw(w, Content, now, me, my); // client wave 17 (h17cards): marks, guards, the falcon, dash streaks
            Objectives?.Draw(w, Content, now, me, my); // client wave 17 (h17obj): the map objectives (table, HUD map, edge arrows)
            RulesDraw?.Draw(w, Content, now, me, my); // client wave 18 (h18rules): Back Slide's speed trail and explosions
            RulesDraw?.Tick(this, w); // client wave 18 (h18rules), point 23: the co-op pause's countdown banner and lines
            HordeTelegraphs.Scale = HordeReadability.Size; // client wave 17 (merge h17int): "Combat effects: Large" widens the bosses' warnings' edges too (1.5)
            if (!_worldHeld) Telegraphs?.Draw(w, Content, now, me, my); // client wave 17 (h17boss): the bosses' warnings (first: they report my danger to Read's C5 of this frame)
            if (!_worldHeld) Read?.Draw(w, now, me, my); // client wave 17 (h17read): wind-ups' arrows and lines, rings, links, the hit's wedge and arc, my flash
            Feel?.LateUpdate();
            Numbers.LateUpdate();
            if (Time.unscaledTime < _hudAt) return; // the HUD's texts ten times a second (strings are made for it)
            _hudAt = Time.unscaledTime + 0.1f;
            string status = "";
            if (me >= 0 && me < w.Players.Length)
            {
                var p = w.Players[me];
                string others = Choosers(w, me);
                if (CountingIn) status = Lang.T("Get ready…");
                else if (HordeRulesDraw.CountdownLine(w) != null) status = HordeRulesDraw.CountdownLine(w); // client wave 18 (h18rules), point 23
                else if (HordeWindows.Holder(w) != null) status = Lang.F("Paused by {0}.", HordeWindows.Holder(w).Name); // (w15hfx, point 19: first: everything waits)
                else if (p.Fallen) { var sc = w.SoulCountOf(me); status = sc.Total > 0 ? Lang.F("Soul {0} / {1}", sc.Total - sc.Need, sc.Total) : Lang.F("You fell: rising… {0} %", Mathf.RoundToInt(p.Revive * 100)); } // (client wave 18, h18feel, point 35: the soul's count; the stand-in's timed rise as before)
                else if (Choice.Open) status = Lang.T("Choosing: the run is paused while the cards are open.");
                else if (others.Length > 0) status = Lang.F("Paused: {0} is choosing…", others);
                else if (w.FinalPhase) status = Lang.T("The final boss is here!");
            }
            if (Choice.Open) Choice.SetParty(w, me);
            Hud.Refresh(w, Content, Run, me, status);
            Passives?.Hud(Hud, w, Content, me); // client wave 17 (h17pass): the passives' meters, the shell under the HP bar
        }

        private void OnDestroy() { LeaveArena(); }
    }
}
