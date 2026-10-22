using System;
using System.Collections.Generic;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 8 (w8game) / client wave 9 (h9flow): the mode's three windows, made with the game's own ListWindow
    /// (UI/ListWindow.cs: laser or gaze on any button):
    ///   the panel (wrist menu → Horde): while no run is on, "Start a run" / "Play alone" (both on the REAL engine: the host runs
    ///     the simulation; alone, a run begins at once); while one gathers, the LOBBY: who is in, who declined, who has not answered,
    ///     the host's run settings (length, reward, arena) and the countdown; while one is played, who plays (level, job) and "Join";
    ///   the prompt: "&lt;name&gt; is starting a Horde run. Join?" with its countdown, the run's settings, that the real character
    ///     stays where it is, and a warning when it is not standing in a town (the host's town list);
    ///   the results: time, kills, damage, level and job reached, score, the real EXP earned (the line updates when rAthena's side
    ///     grants it); they stay until dismissed; "Play again" starts a new run (an invitation to everyone) in one press.
    /// The scripted stand-in is never offered here any more (it is the smoke's: -lokiHorde fake).
    /// </summary>
    public sealed class HordeWindows
    {
        private readonly ListWindow _panel, _prompt, _results, _runMenu;
        private readonly Func<Camera> _head;
        public Func<IHordeChannel> Channel;
        public Func<string> MyMap;
        public Action Started, LeaveRun, PlayAgain;
        /// <summary>Client wave 18 (h18rules), point 38: "Start a run" / "Play alone" with the players chosen to be invited (empty: alone).</summary>
        public Action<uint[]> StartWith;
        public Action<uint, bool> Answered;
        /// <summary>Client wave 14 (w14kit), point 15: "End the run" (H_END: the run ends for everyone, each gets the EXP so far).</summary>
        public Action<uint> EndRun;
        /// <summary>The first press of "End the run" arms it until this time (Time.realtimeSinceStartup); a second press within it ends the run.</summary>
        private float _endArmedUntil; private uint _endArmedRun;
        /// <summary>Client wave 14 (w14kit), point 15: seconds the first press of "End the run" stays armed.</summary>
        public const float EndArmSeconds = 4f;
        /// <summary>Called once when a run starts gathering (the lobby is seen, or the prompt opens): h9feel builds the run's sprites meanwhile.</summary>
        public Action Prewarm;
        /// <summary>An extra footer button of the panel, or null (kept for callers; nothing sets it since client wave 9).</summary>
        public Func<(string label, Action act)?> Alternative;
        private HordeInvite _invite; private float _inviteAt; private uint _prewarmedRun;
        public bool PromptOpen => _prompt.Visible;
        public bool PanelOpen => _panel.Visible;
        public bool ResultsOpen => _results.Visible;
        public ListWindow PanelWindow => _panel;
        public ListWindow PromptWindow => _prompt;
        public ListWindow ResultsWindow => _results;
        public HordeResults LastResults { get; private set; }
        public HordeRewardNote LastReward { get; private set; }
        public int PlayAgainPresses { get; private set; }

        public HordeWindows(Transform parent, Func<Camera> head)
        {
            _head = head;
            _panel = ListWindow.Create(parent, "HordePanel"); _panel.OnCloseClicked = () => _panel.Hide();
            _prompt = ListWindow.Create(parent, "HordePrompt"); _prompt.OnCloseClicked = () => AnswerInvite(false);
            _results = ListWindow.Create(parent, "HordeResults"); _results.OnCloseClicked = () => _results.Hide();
            _runMenu = ListWindow.Create(parent, "HordeRunMenu"); _runMenu.OnCloseClicked = () => _runMenu.Hide(); // (w15hfx, point 19)
        }

        public static bool InvitesShown { get => World.UserSettings.Prefs.hordeInvites; set { World.UserSettings.Prefs.hordeInvites = value; World.UserSettings.Save(); } }

        private static string RewardName(HordeRewardSize r) => r switch { HordeRewardSize.Off => Lang.T("~reward~off"), HordeRewardSize.Low => Lang.T("~reward~low"), HordeRewardSize.High => Lang.T("~reward~high"), _ => Lang.T("~reward~normal") };
        /// <summary>The host's run settings in one line: length, real EXP, arena (the install's map name).</summary>
        private static string SettingsLine(float runLength, HordeRewardSize reward, string arena)
        {
            string map = string.IsNullOrEmpty(arena) ? "" : Game.ClientText.MapName(arena);
            string s = Lang.F("Run of {0} minutes · real EXP at the end: {1}", Mathf.RoundToInt(runLength / 60f), RewardName(reward));
            return string.IsNullOrEmpty(map) ? s : s + " · " + map;
        }

        // ---------------------------------------------------------------- the panel
        public void ShowPanel() { _panel.Open(_head()); RefreshPanel(); }

        /// <summary>Client wave 14 (w14kit), point 15: "End the run" pressed (also the smoke): arms it, or ends the run when armed.</summary>
        public void PressEnd(uint run)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (_endArmedRun == run && now < _endArmedUntil)
            {
                _endArmedUntil = 0f; _endArmedRun = 0;
                UnityEngine.Debug.Log($"[Horde] end the run {run} for everyone (pressed twice)");
                EndRun?.Invoke(run);
            }
            else { _endArmedRun = run; _endArmedUntil = now + EndArmSeconds; UnityEngine.Debug.Log($"[Horde] end the run {run}: armed for {EndArmSeconds:0} s"); }
            RefreshPanel(); RefreshRunMenu();
        }

        public void RefreshPanel()
        {
            if (!_panel.Visible) return;
            var ch = Channel?.Invoke();
            _panel.SetTitle(Lang.T("Horde"));
            _panel.SetTabs(null, 0, null); _panel.SetInput(false);
            var rows = new List<ListWindow.Row>();
            if (ch == null) { _panel.SetInfo(Lang.T("Not in the world yet.")); _panel.SetRows(rows); _panel.SetFooter((Lang.T("Close"), _panel.Hide)); return; }
            var st = ch.Status;
            bool ready = ch.LinkState == HordeLinkState.Ready;
            switch (ch.LinkState)
            {
                case HordeLinkState.NoLink: _panel.SetInfo(Lang.T("Connecting to this server's Loki Host…")); break;
                case HordeLinkState.OldHost: _panel.SetInfo(Lang.T("This Loki Host is from before Horde: ask the host to update.")); break;
                case HordeLinkState.SwitchedOffByHost: _panel.SetInfo(Lang.T("The host has Horde switched off.")); break;
                case HordeLinkState.OtherVersion: _panel.SetInfo(Lang.T("Your Horde version differs from the host's: update Loki.")); break;
                case HordeLinkState.OtherProtocol: _panel.SetInfo(Lang.T("The host runs another version of Horde: one of you must update Loki.")); break; // h9move's protocol 2 (007461ae)
                default:
                    _panel.SetInfo(st.Phase == HordePhase.Idle ? Lang.T("No run now: start one and choose who is invited.") // (client wave 18, h18rules: point 38)
                        : st.Phase == HordePhase.Gathering ? Lang.F("{0} is starting a run: it begins in {1} s.", st.StarterName, Mathf.CeilToInt(st.SecondsLeft))
                        : Lang.F("A run is being played: {0} of {1}.", HordeText.Clock(st.RunSeconds), HordeText.Clock(st.RunLength)));
                    break;
            }
            if (ready && st.Phase == HordePhase.Gathering && _prewarmedRun != st.RunId) { _prewarmedRun = st.RunId; Prewarm?.Invoke(); }
            if (ready && st.Phase != HordePhase.Idle)
            {
                // the lobby: the host's settings, then everyone the host asked, one row each, with their answer
                rows.Add(new ListWindow.Row { Highlight = true, Text = SettingsLine(st.RunLength, st.Reward, st.ArenaMap) });
                int inCount = 0, declined = 0, asked = 0;
                foreach (var m in st.Members)
                {
                    string line;
                    switch (m.State)
                    {
                        case HordeMemberState.Joined:
                            inCount++;
                            line = st.Phase == HordePhase.Running && m.Slot >= 0
                                ? (m.Fallen ? Lang.F("<color=#7fd67f>✔</color> {0} · Lv {1} {2} · <color=#ff8a7a>fallen</color>", m.Name, m.Level, HordeText.Job(m.JobId)) : Lang.F("<color=#7fd67f>✔</color> {0} · Lv {1} {2}", m.Name, m.Level, HordeText.Job(m.JobId)))
                                : Lang.F("<color=#7fd67f>✔</color> {0} · in", m.Name);
                            break;
                        case HordeMemberState.Declined: declined++; line = Lang.F("<color=#9aa3b5>✘ {0} · not now</color>", m.Name); break;
                        case HordeMemberState.Left: line = Lang.F("<color=#9aa3b5>✘ {0} · left</color>", m.Name); break;
                        default: asked++; line = Lang.F("<color=#ffd080>… {0} · asked, no answer yet</color>", m.Name); break;
                    }
                    if (m.Aid == st.StarterAid) line += " " + Lang.T("<color=#ffd080>(started it)</color>");
                    rows.Add(new ListWindow.Row { Text = line });
                }
                if (st.Phase == HordePhase.Gathering) rows.Add(new ListWindow.Row { Text = Lang.F("{0} in · {1} declined · {2} asked. Starts when the countdown ends or all answer.", inCount, declined, asked) });
                else if (CanJoin(st, out _)) rows.Add(new ListWindow.Row { Text = Lang.F("You can still join, as a Novice at level {0}.", st.LowestLevel) }); // (client wave 18, h18rules: invited, a seat left)
            }
            else
            {
                rows.Add(new ListWindow.Row { Highlight = true, Text = Lang.T("Co-op side mode: walk with the stick; your weapons fire by themselves. You start as a Novice.") });
                rows.Add(new ListWindow.Row { Text = Lang.T("Your real character stays where it is, unchanged. It gains EXP at the end.") });
                // client wave 13, item 5 (w13ring): the same switch as Settings → Horde, made by UI/SettingRow.cs (both choices shown, the current one lit)
                rows.Add(SettingRow.Toggle(Lang.T("Horde invitations"), Lang.T("When others start a run"), () => InvitesShown, v => { InvitesShown = v; RefreshPanel(); }, Lang.T("~invites~Hidden"), Lang.T("~invites~Shown")));
                if (ready) InviteRows(st, rows); // client wave 18 (h18rules), point 38: whom to invite, above "Start a run" (was: "Play alone: starts at once if nobody else is online.")
            }
            _panel.SetRows(rows);
            if (ch.InRun) _panel.SetFooter((Lang.T("Close"), _panel.Hide), (Lang.T("Leave the run"), () => { LeaveRun?.Invoke(); RefreshPanel(); }));
            else if (ready && st.Phase == HordePhase.Idle) _panel.SetFooter((Lang.T("Close"), _panel.Hide), (Lang.T("Start a run"), () => { Start(Chosen()); RefreshPanel(); }), (Lang.T("Play alone"), () => { Start(new uint[0]); RefreshPanel(); })); // (client wave 18, h18rules: the chosen players, or nobody)
            else if (ready && st.Phase == HordePhase.Running && st.Member(Me) != null && st.Member(Me).State == HordeMemberState.Left)
            {
                // client wave 14 (w14kit), point 15 of the design's headset list: a run I left can be joined again or ENDED, for everyone (the design's
                // answer 12); the first press arms the button (its label asks), a second within EndArmSeconds ends it
                bool armed = _endArmedRun == st.RunId && UnityEngine.Time.realtimeSinceStartup < _endArmedUntil;
                if (armed) _panel.SetInfo(Lang.T("Ends it for everyone. All get the EXP so far."));
                uint run = st.RunId;
                _panel.SetFooter((Lang.T("Close"), _panel.Hide), (Lang.T("Join the run"), () => { Answered?.Invoke(run, true); _panel.Hide(); }),
                    (armed ? Lang.T("Sure? End it") : Lang.T("End the run"), () => PressEnd(run)));
            }
            else if (ready && st.Phase != HordePhase.Idle && !CanJoin(st, out string why)) { _panel.SetInfo(why); _panel.SetFooter((Lang.T("Close"), _panel.Hide)); } // client wave 18 (h18rules), point 38: not invited, or full
            else if (ready && st.Phase == HordePhase.Running) _panel.SetFooter((Lang.T("Close"), _panel.Hide), (Lang.T("Join the run"), () => { Answered?.Invoke(st.RunId, true); _panel.Hide(); }));
            else if (ready && st.Phase == HordePhase.Gathering && (st.Member(Me) == null || st.Member(Me).State != HordeMemberState.Joined)) _panel.SetFooter((Lang.T("Close"), _panel.Hide), (Lang.T("Join"), () => { Answered?.Invoke(st.RunId, true); RefreshPanel(); }), (Lang.T("Not now"), () => { Answered?.Invoke(st.RunId, false); RefreshPanel(); }));
            else _panel.SetFooter((Lang.T("Close"), _panel.Hide));
            var alt = Alternative?.Invoke();
            if (alt.HasValue && !ch.InRun && !(ready && st.Phase != HordePhase.Idle))
            {
                var a = alt.Value;
                _panel.SetFooter((Lang.T("Close"), _panel.Hide), (a.label, () => { a.act(); RefreshPanel(); }));
            }
        }
        public uint Me;

        // ---------------------------------------------------------------- client wave 18 (h18rules), the design's point 38
        // "allow the player who is starting the run to choose who is being invited" and "a maximum limitation of four players at the same
        // time" (in a Horde run): the panel lists everyone the host could invite (HordeStatus.Online: the host's rule since wave 8, every
        // player connected with Horde and the same tables), each with a switch (Off at first); at most HordeLimits.MaxRunPlayers - 1 on;
        // "Start a run" invites only them (none: alone), "Play alone" nobody, "Play again" the same ones. A run already on: only its
        // invited players may join, while a seat is left (the host decides: HordeService.Answer; a seat stays its player's).
        private readonly HashSet<uint> _chosen = new HashSet<uint>();
        private string _inviteNote; private float _inviteNoteUntil;
        /// <summary>The players chosen to be invited who are still online, in the list's order.</summary>
        public uint[] Chosen()
        {
            var ch = Channel?.Invoke(); var l = new List<uint>();
            if (ch != null) foreach (var m in ch.Status.Online) if (m.Aid != Me && _chosen.Contains(m.Aid) && !l.Contains(m.Aid)) l.Add(m.Aid);
            return l.ToArray();
        }
        /// <summary>Switches a player's invitation; false when refused (the run would be over HordeLimits.MaxRunPlayers).</summary>
        public bool SetInvited(uint aid, bool on)
        {
            if (!on) { _chosen.Remove(aid); RefreshPanel(); return true; }
            if (_chosen.Contains(aid)) return true;
            if (Chosen().Length >= HordeLimits.MaxRunPlayers - 1)
            {
                _inviteNote = Lang.F("Up to {0} others.", HordeLimits.MaxRunPlayers - 1); _inviteNoteUntil = Time.time + 3f;
                Debug.Log($"[Horde] invite {aid}: refused, {HordeLimits.MaxRunPlayers - 1} chosen already");
                RefreshPanel(); return false;
            }
            _chosen.Add(aid); RefreshPanel(); return true;
        }
        /// <summary>Players listed to be invited in the panel the last time it was drawn (the smoke).</summary>
        public int InviteRowsShown { get; private set; }

        private void Start(uint[] invite)
        {
            Debug.Log($"[Horde] start a run inviting {invite.Length}: {string.Join(", ", invite)}");
            if (StartWith != null) StartWith(invite); else Started?.Invoke();
        }

        private void InviteRows(HordeStatus st, List<ListWindow.Row> rows)
        {
            int shown = 0;
            foreach (var m in st.Online) if (m.Aid != Me) shown++;
            InviteRowsShown = shown;
            if (shown == 0) { rows.Add(new ListWindow.Row { Text = Lang.T("Nobody else is online: the run is yours alone.") }); return; }
            rows.Add(new ListWindow.Row { Highlight = true, Text = Lang.F("Invite (up to {0}):", HordeLimits.MaxRunPlayers - 1) });
            foreach (var m in st.Online)
            {
                if (m.Aid == Me) continue;
                uint aid = m.Aid;
                rows.Add(SettingRow.Toggle(m.Name, Lang.T("Asked to join your run."), () => _chosen.Contains(aid), v => SetInvited(aid, v)));
            }
            if (_inviteNote != null && Time.time < _inviteNoteUntil) _panel.SetInfo(_inviteNote);
        }

        /// <summary>A run being gathered or played: may I join it (invited, and a seat left or my own)? <paramref name="why"/>: why not.</summary>
        private bool CanJoin(HordeStatus st, out string why)
        {
            why = null;
            var me = st.Member(Me);
            if (me == null) { why = Lang.T("Only the players its starter invited can join."); return false; }
            if (st.Phase == HordePhase.Gathering && me.State == HordeMemberState.Joined) return true;
            if (st.Phase == HordePhase.Running && me.Slot >= 0) return true; // (my seat: point 25's rejoin)
            int seats = 0;
            foreach (var m in st.Members) if (st.Phase == HordePhase.Gathering ? m.State == HordeMemberState.Joined : m.Slot >= 0) seats++;
            if (seats >= HordeLimits.MaxRunPlayers) { why = Lang.F("The run is full ({0} players).", HordeLimits.MaxRunPlayers); return false; }
            return true;
        }

        // ---------------------------------------------------------------- client wave 15 (w15hfx), point 19: the run's own menu
        // design note (wave 14 notes, point 19): "while I am in a run when I press the button to open up the menu, it has its own Horde-specific
        // menu that temporarily takes the place of the rotating wrist or the classic panel menu ... You can still access those original
        // menus by quick clicking and holding the menu button". A SHORT press of the menu button in a run opens this window (UI/LokiHud.cs
        // decides the press at its release; a HOLD opens the usual menu). Pause / Resume (for everyone; anyone may resume: the
        // orchestrator's proposal, flagged), Leave the run, End the run (two presses, as the panel's), Main menu (the usual menu, for hands
        // and controllers without a hold); the rows: the run's time and level, my job, weapons and kills, the party's HP.
        /// <summary>Pause (true) / resume (false) the run for everyone; the usual menu (ring or wrist).</summary>
        public Action<bool> Pause;
        public Action MainMenu;
        /// <summary>The run's content (weapon names) for the run menu's rows.</summary>
        public Func<HordeContent> Content;
        public bool RunMenuOpen => _runMenu.Visible;
        public ListWindow RunMenuWindow => _runMenu;
        public int RunMenuShown { get; private set; }

        public void ShowRunMenu() { _runMenu.Open(_head()); RunMenuShown++; MenuOpenedPause(); RefreshRunMenu(); } // (client wave 18, h18rules: + the pause alone)
        public void HideRunMenu() => _runMenu.Hide();

        // ---------------------------------------------------------------- client wave 18 (h18rules), the design's point 23
        // "When I open up the horde menu ... that should pause the game. I should not need to press a pause button on that menu." and with
        // others: "I have to click a pause button which then does a countdown of three seconds before pausing the game" ("yes, to both":
        // pressing it again cancels; resuming counts down too). ALONE: opening this menu pauses at once, closing it resumes (only a pause it
        // made itself; it holds while a menu opened from it, "Main menu", is still open: OtherMenusOpen). WITH OTHERS: the footer's Pause
        // asks the host for the countdown, the same button reads "Cancel pause" / "Cancel resume" while one runs (the same request again
        // cancels it: HordeSim.Rules.cs), Resume when paused. Alone or not is the engine's rule (HordeSim.PauseCountsDown): another player
        // present whose link is up.
        /// <summary>True while the player's other menus are open (LokiGame.W8Horde: the usual menu opened from here).</summary>
        public Func<bool> OtherMenusOpen;
        /// <summary>This menu paused the run when it opened (alone); it resumes it when it and the menus opened from it are closed.</summary>
        public bool MenuPaused { get; private set; }
        public int MenuPauses { get; private set; }
        public int MenuResumes { get; private set; }
        private int _countShown = -1; private bool _countWay;

        /// <summary>No other player in the run whose link is up (the engine's HordeSim.PauseCountsDown, as this client sees it).</summary>
        public static bool Alone(HordeWorldView w, int mySlot)
        {
            if (w == null) return true;
            foreach (var p in w.Players) if (p.Present && p.Slot != mySlot && (p.Flags & HordePlayerFlags.Away) == 0) return false;
            return true;
        }

        private void MenuOpenedPause()
        {
            var ch = Channel?.Invoke();
            if (ch == null || !ch.InRun || ch.Run == null || MenuPaused) return;
            var w = ch.World;
            if (!Alone(w, ch.Run.Slot) || Holder(w) != null) return;
            MenuPaused = true; MenuPauses++;
            Debug.Log("[Horde] the run's menu opened alone: the run pauses");
            Pause?.Invoke(true);
        }

        /// <summary>Once a frame (Tick): the pause this menu made ends when it is closed (and nothing opened from it is still open).</summary>
        private void MenuClosedResume()
        {
            if (!MenuPaused || _runMenu.Visible || (OtherMenusOpen != null && OtherMenusOpen())) return;
            MenuPaused = false; MenuResumes++;
            var ch = Channel?.Invoke();
            Debug.Log("[Horde] the run's menu closed: the run goes on");
            if (ch != null && ch.InRun) Pause?.Invoke(false);
        }

        /// <summary>The player who holds the run paused (HordePlayerFlags.Held), or null.</summary>
        public static HordePlayerView Holder(HordeWorldView w)
        {
            if (w == null) return null;
            foreach (var p in w.Players) if (p.Present && (p.Flags & HordePlayerFlags.Held) != 0) return p;
            return null;
        }

        public void RefreshRunMenu()
        {
            if (!_runMenu.Visible) return;
            var ch = Channel?.Invoke();
            if (ch == null || !ch.InRun || ch.Run == null) { _runMenu.Hide(); return; }
            var w = ch.World; int me = ch.Run.Slot; var c = Content?.Invoke();
            var my = me >= 0 && me < w.Players.Length ? w.Players[me] : null;
            var holder = Holder(w);
            _runMenu.SetTitle(Lang.T("Horde"));
            _runMenu.SetTabs(null, 0, null); _runMenu.SetInput(false);
            _runMenu.SetInfo(holder != null ? Lang.F("Paused by {0}.", holder.Name) : Lang.F("Time {0} / {1}", HordeText.Clock(w.Seconds), HordeText.Clock(ch.Run.RunLength)));
            var countLine = HordeRulesDraw.CountdownLine(w); if (countLine != null) _runMenu.SetInfo(countLine); // client wave 18 (h18rules), point 23
            else if (holder != null && Alone(w, me)) _runMenu.SetInfo(Lang.T("Paused")); // client wave 18 (h18rules): alone, not "Paused by" myself
            var rows = new List<ListWindow.Row>();
            if (my != null)
            {
                rows.Add(new ListWindow.Row { Highlight = true, Text = Lang.F("Party level {0} · {1}", w.PartyLevel, HordeText.Job(my.JobId)) + " · " + Lang.F("{0} kills", my.Kills) });
                var names = new List<string>();
                foreach (var o in my.Weapons) names.Add(HordeText.Weapon(c, o.DefId) + " " + o.Level);
                if (names.Count > 0) rows.Add(new ListWindow.Row { Text = string.Join(" · ", names) });
            }
            foreach (var p in w.Players)
            {
                if (!p.Present) continue;
                string st = p.Fallen ? Lang.F("<color=#ff8a7a>fallen</color> {0} %", Mathf.RoundToInt(p.Revive * 100)) : Lang.F("HP {0} / {1}", p.Hp, p.MaxHp);
                rows.Add(new ListWindow.Row { Text = p.Name + " · " + st });
            }
            _runMenu.SetRows(rows);
            uint run = ch.Status.RunId;
            bool armed = _endArmedRun == run && UnityEngine.Time.realtimeSinceStartup < _endArmedUntil;
            if (armed) _runMenu.SetInfo(Lang.T("Ends it for everyone. All get the EXP so far."));
            (string, Action) pause = holder != null ? (Lang.T("Resume"), (Action)(() => Pause?.Invoke(false))) : (Lang.T("Pause"), (Action)(() => Pause?.Invoke(true)));
            // client wave 18 (h18rules), point 23: alone the menu itself pauses (Resume closes it); with others a countdown, cancelled by the same button
            if (Alone(w, me)) pause = (Lang.T("Resume"), () => { if (holder != null && !MenuPaused) Pause?.Invoke(false); _runMenu.Hide(); });
            else if (w.PauseCountBy >= 0) { bool way = w.PauseCountToPause; pause = (way ? Lang.T("Cancel pause") : Lang.T("Cancel resume"), () => Pause?.Invoke(way)); }
            _countShown = w.PauseCountBy; _countWay = w.PauseCountToPause;
            _runMenu.SetFooter(pause,
                (Lang.T("Leave the run"), () => { _runMenu.Hide(); LeaveRun?.Invoke(); }),
                (armed ? Lang.T("Sure? End it") : Lang.T("End the run"), () => PressEnd(run)),
                (Lang.T("Main menu"), () => { _runMenu.Hide(); MainMenu?.Invoke(); }));
        }

        // ---------------------------------------------------------------- the prompt
        public void ShowInvite(HordeInvite inv)
        {
            _invite = inv; _inviteAt = Time.time;
            _prompt.Open(_head());
            RefreshPrompt();
            if (_prewarmedRun != inv.RunId) { _prewarmedRun = inv.RunId; Prewarm?.Invoke(); }
            Debug.Log($"[Horde] invitation to run {inv.RunId} from {inv.StarterName} (AID {inv.StarterAid}), {inv.Seconds:0} s to answer, arena {inv.ArenaMap}");
        }

        public void RefreshPrompt()
        {
            if (!_prompt.Visible || _invite == null) return;
            float left = _invite.Seconds - (Time.time - _inviteAt);
            if (left <= 0f) { _prompt.Hide(); Debug.Log("[Horde] the invitation lapsed (no answer)"); return; }
            var ch = Channel?.Invoke();
            _prompt.SetTitle(Lang.T("Horde"));
            _prompt.SetInfo(Lang.F("{0} is starting a Horde run. Join?", _invite.StarterName));
            _prompt.SetTabs(null, 0, null); _prompt.SetInput(false);
            var rows = new List<ListWindow.Row>
            {
                new ListWindow.Row { Highlight = true, Text = Lang.F("The run begins in {0} s ({1} minutes, co-op).", Mathf.CeilToInt(left), Mathf.RoundToInt(_invite.RunLength / 60f)) },
                new ListWindow.Row { Text = SettingsLine(_invite.RunLength, _invite.Reward, _invite.ArenaMap) },
                new ListWindow.Row { Text = Lang.T("Your real character stays where it is. Chat and voice keep working.") },
            };
            string map = MyMap?.Invoke();
            var st = ch?.Status;
            if (st != null && st.Towns.Count > 0 && !st.IsTown(map))
                rows.Add(new ListWindow.Row { Text = Lang.T("<color=#ff9a7a>Your character is not in a town: monsters can attack it.</color>") });
            else if (st == null || st.Towns.Count == 0)
                rows.Add(new ListWindow.Row { Text = Lang.T("<color=#ffd080>Leave your character somewhere safe.</color>") });
            _prompt.SetRows(rows);
            _prompt.SetFooter((Lang.T("Join"), () => AnswerInvite(true)), (Lang.T("Not now"), () => AnswerInvite(false)));
        }

        public void AnswerInvite(bool join)
        {
            if (_invite == null) { _prompt.Hide(); return; }
            var id = _invite.RunId; _invite = null; _prompt.Hide();
            Debug.Log($"[Horde] answered the invitation to run {id}: {(join ? "join" : "not now")}");
            Answered?.Invoke(id, join);
        }

        // ---------------------------------------------------------------- the results
        public void ShowResults(HordeResults r, HordeContent c)
        {
            LastResults = r; LastReward = null;
            _results.Open(_head());
            RefreshResults(c);
        }

        public void SetReward(HordeRewardNote note, HordeContent c) { LastReward = note; RefreshResults(c); }

        public void RefreshResults(HordeContent c)
        {
            var r = LastResults; if (r == null || !_results.Visible) return;
            _results.SetTitle(r.Outcome == HordeOutcome.Victory ? Lang.T("Horde: victory!") : r.Outcome == HordeOutcome.TimeUp ? Lang.T("Horde: the time is up") : r.Outcome == HordeOutcome.Defeat ? Lang.T("Horde: everyone fell") : Lang.T("Horde: the run was stopped"));
            int partyLevel = 0; foreach (var p in r.Players) partyLevel = Mathf.Max(partyLevel, p.Level); // the party's level (everyone shares one: design note, 2026-10-05)
            _results.SetInfo(Lang.F("The run lasted {0} · party level {1}.", HordeText.Clock(r.Seconds), partyLevel));
            _results.SetTabs(null, 0, null); _results.SetInput(false);
            var rows = new List<ListWindow.Row>();
            var mine = r.Mine();
            if (mine != null)
            {
                string reward;
                if (LastReward != null) reward = "<color=#7fd67f>" + Lang.F("Your character gained {0} Base EXP and {1} Job EXP.", LastReward.BaseExp, LastReward.JobExp) + "</color>";
                else if (mine.Reward == HordeRewardState.None) reward = Lang.T("No EXP for this run (reward off, or too short).");
                else reward = Lang.F("Your character gets {0} % of its next Base level and {1} % of its next Job level{2}.", (mine.BaseShare / 100f).ToString("0.##"), (mine.JobShare / 100f).ToString("0.##"),
                    mine.Reward == HordeRewardState.Pending ? Lang.T(" (given as soon as rAthena's side has it)") : "");
                rows.Add(new ListWindow.Row { Highlight = true, Text = reward });
            }
            foreach (var p in r.Players)
            {
                rows.Add(new ListWindow.Row
                {
                    Highlight = p.Slot == r.MySlot,
                    Text = Lang.F("<b>{0}</b> · Lv {1} {2} · score {3}\n<size=19>{4} kills ({5} elites, {6} bosses) · damage {7} · healing {8} · falls {9} · raised {10} · {11}{12}</size>",
                        p.Name, p.Level, HordeText.Job(p.JobId), p.Score, p.Kills, p.EliteKills, p.BossKills, p.Damage, p.Healing, p.Falls, p.Revives, HordeText.Clock(p.Seconds), p.Left ? Lang.T(" · left early") : ""),
                });
            }
            _results.SetRows(rows, true);
            var ch = Channel?.Invoke();
            bool again = ch != null && ch.LinkState == HordeLinkState.Ready && !ch.InRun && ch.Status.Phase == HordePhase.Idle && PlayAgain != null;
            if (again) _results.SetFooter((Lang.T("Close"), _results.Hide), (Lang.T("Play again"), () => { PlayAgainPresses++; _results.Hide(); PlayAgain?.Invoke(); }));
            else _results.SetFooter((Lang.T("Close"), _results.Hide));
        }

        /// <summary>A run begins: the panel, the prompt and a previous run's results go.</summary>
        public void HideAll() { _panel.Hide(); _prompt.Hide(); _results.Hide(); _runMenu.Hide(); }

        private int _promptSecond = -1; private float _panelAt, _runMenuAt;
        /// <summary>Once a frame: the prompt's countdown (redrawn when its second changes, so a hovered button keeps its hover), the
        /// panel twice a second.</summary>
        public void Tick()
        {
            if (_prompt.Visible && _invite != null)
            {
                int sec = Mathf.CeilToInt(_invite.Seconds - (Time.time - _inviteAt));
                if (sec != _promptSecond) { _promptSecond = sec; RefreshPrompt(); }
            }
            if (_panel.Visible && Time.time > _panelAt) { _panelAt = Time.time + 0.5f; RefreshPanel(); }
            if (_runMenu.Visible && Time.time > _runMenuAt) { _runMenuAt = Time.time + 0.5f; RefreshRunMenu(); }
            // client wave 18 (h18rules), point 23: the menu's pause alone ends with it; a countdown that began or went redraws the buttons at once
            MenuClosedResume();
            var cw = _runMenu.Visible ? Channel?.Invoke()?.World : null;
            if (cw != null && (cw.PauseCountBy != _countShown || (cw.PauseCountBy >= 0 && cw.PauseCountToPause != _countWay))) RefreshRunMenu();
        }
    }
}
