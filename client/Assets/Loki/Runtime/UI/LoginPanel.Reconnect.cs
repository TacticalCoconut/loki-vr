using System.Collections.Generic;
using Loki.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 4 (w4account): getting back in after a lost server, and the map choice.
    ///  • A lost char server on the character screens (the link closed, or a code that allows it) and a lost map server in the
    ///    world (LokiGame goes through a fresh scene with a <see cref="ReconnectTicket"/>) end on the character list again: the
    ///    panel logs in with the same server and account a few times, backing off (2, 4, 8, 15, 30, 30 s), and stops at the list,
    ///    at the PIN pad (asked again after a new login), at a refusal, or when the player presses Stop. The account's
    ///    password lives in memory only (this panel and the ticket), never on disk.
    ///  • Port wave I (ionline): the same for Loki Online, in the world and on the character screens. A server that keeps this
    ///    PC's login (<see cref="IAccountSession.CanResume"/>) is reconnected to through that saved login
    ///    (<see cref="IAccountSession.Resume"/>): the ticket then holds no password at all, and a login made with "Continue as
    ///    before" reconnects too. The connection is dropped with <see cref="IAccountSession.Drop"/>, never with Reset (a log out
    ///    there). A server that no longer knows this PC ends the reconnect on the login screen with the reason.
    ///  • Nothing reconnects after SC_NOTIFY_BAN 2 (someone else logged in with the account: logging in again would kick them —
    ///    the login server kicks the older session, rA:login/loginclif.cpp:84-96) or a GM's kick (15): <see cref="RefusalText.MayReconnect"/>.
    ///  • HC_NOTIFY_ACCESSIBLE_MAPNAME 0x0840: the character's map has no map server; the player picks an open map
    ///    (CH_SELECT_ACCESSIBLE_MAPNAME 0x0841), or reads "there is no available map" (msgstringtable 1811) when none is open.
    /// All of it goes through <see cref="IAccountSession"/> (Login, SelectAccessibleMap, LastBanCode): the same for Loki Online.
    /// </summary>
    public sealed partial class LoginPanel
    {
        /// <summary>What a fresh scene needs to log in again after a lost map server (memory only; the old scene sets it).</summary>
        public sealed class ReconnectTicket
        {
            public string Server, User, Pass, Why;
            /// <summary>Log in again with this PC's saved login instead of a password (a server with <see cref="IAccountSession.CanResume"/>).</summary>
            public bool Resume;
        }
        public static ReconnectTicket PendingReconnect;

        private static readonly float[] ReconnectDelays = { 2f, 4f, 8f, 15f, 30f, 30f };
        private bool _reconnecting, _reachedChars; private int _reAttempt; private float _reNextAt = -1f; private string _reWhy = "";
        private ServerEntry _reServer; private string _reUser = "", _rePass = ""; private bool _reResume;
        public bool Reconnecting => _reconnecting;
        public int ReconnectAttempts => _reAttempt;
        /// <summary>How the last reconnect ended ("", "reached", "refused: …", "gave up: …", "stopped").</summary>
        public string ReconnectOutcome { get; private set; } = "";

        /// <summary>A ticket from the last login this panel made (null when it made none: loki.json's auto-login).</summary>
        public ReconnectTicket MakeReconnectTicket(string why)
        {
            if (string.IsNullOrEmpty(_lastLogin.server) || (!_lastLogin.resume && string.IsNullOrEmpty(_lastLogin.user))) return null;
            return new ReconnectTicket { Server = _lastLogin.server, User = _lastLogin.user, Pass = _lastLogin.pass, Why = why, Resume = _lastLogin.resume };
        }

        /// <summary>The new scene after a lost map server: fill the form from the ticket and log in again.</summary>
        public bool ResumeFromTicket(ReconnectTicket t)
        {
            if (t == null) return false;
            int i = _servers.FindIndex(e => ServerKey(e) == t.Server);
            if (i < 0) { Status(Lang.F("The connection was lost ({0}) and the server isn't in the list any more: log in again.", t.Why)); return false; }
            _server = i; RefreshServers();
            if (!t.Resume) { _user.SetText(t.User); _pass.SetText(t.Pass); }
            else if (!string.IsNullOrEmpty(t.User)) _user.SetText(t.User); // (a saved login has no password to put back)
            _lastLogin = (t.Server, t.User, t.Pass, t.Resume);
            return BeginReconnect(t.Why);
        }

        /// <summary>Starts logging in again with the last login's server and account (false when there was none).</summary>
        public bool BeginReconnect(string why)
        {
            if (string.IsNullOrEmpty(_lastLogin.server)) return false;
            var sv = _servers.Find(e => ServerKey(e) == _lastLogin.server); if (sv == null) return false;
            _reServer = sv; _reUser = _lastLogin.user; _rePass = _lastLogin.pass; _reResume = _lastLogin.resume; _reWhy = why ?? "";
            _reconnecting = true; _reAttempt = 0; _reNextAt = Time.time + ReconnectDelays[0]; ReconnectOutcome = "";
            UseServer(sv); // (a fresh scene starts on the rAthena session)
            _s.Drop();     // the connection goes, a saved login stays (rAthena: the same as Reset)
            Debug.Log($"[Login] reconnecting to {sv.name} ({sv.Address}) {(_reResume ? "with this PC's saved login" : "as " + _reUser)}: {_reWhy}");
            Show(Screen.Connecting); Status(Lang.F("Connection lost: {0}", _reWhy));
            return true;
        }

        private void StopReconnect(string outcome)
        {
            if (!_reconnecting) return;
            _reconnecting = false; _reNextAt = -1f; ReconnectOutcome = outcome ?? "stopped";
            Debug.Log($"[Login] reconnect ended after {_reAttempt} attempt(s): {ReconnectOutcome}");
        }

        private void TickReconnect()
        {
            if (!_reconnecting || _reNextAt < 0 || Time.time < _reNextAt) return;
            _reNextAt = -1f; _reAttempt++;
            string status = Lang.F("Reconnecting to {0} (attempt {1} of {2})…", _reServer.name, _reAttempt, ReconnectDelays.Length);
            if (_reResume) StartResume(_reServer, _reUser, status); else StartLogin(_reServer, _reUser, _rePass, status);
            RefreshConnecting();
        }

        /// <summary>A reconnect by the saved login that the server answered with a notice and no login (it no longer knows this PC:
        /// its data was reset, the account's password was changed on another PC, the saved login was rejected and removed): the
        /// reconnect ends on the login screen with why the connection was lost and what the server said.</summary>
        private bool ReconnectNotice(string text, bool notLoggedIn)
        {
            if (!_reconnecting || !_reResume || !notLoggedIn) return false;
            return ReconnectRefused((_reWhy.Length > 0 ? _reWhy + " " : "") + text);
        }

        /// <summary>The list or the PIN pad after a login: a reconnect is done.</summary>
        private void ReconnectReached()
        {
            _reachedChars = true;
            if (!_reconnecting) return;
            StopReconnect("reached");
            Status(Lang.F("Reconnected to {0}.", _reServer?.name));
        }

        /// <summary>A login refusal while reconnecting ends it (the text shows on the login screen).</summary>
        private bool ReconnectRefused(string text)
        {
            if (!_reconnecting) return false;
            StopReconnect("refused: " + text);
            Status(text); Show(Screen.Login);
            return true;
        }

        /// <summary>A failure: the next attempt while reconnecting (or the end), or — on the character screens — the start of one.</summary>
        private bool ReconnectFailed(string why)
        {
            int ban = _s.LastBanCode;
            if (_reconnecting)
            {
                if (!RefusalText.MayReconnect(ban)) { StopReconnect("refused: " + why); Status(why); Show(Screen.Login); return true; }
                if (_reAttempt >= ReconnectDelays.Length)
                {
                    StopReconnect("gave up: " + why);
                    Status(Lang.F("Couldn't reconnect ({0}). Log in again when the server is back.", why)); Show(Screen.Login);
                    return true;
                }
                _reNextAt = Time.time + ReconnectDelays[_reAttempt];
                Debug.Log($"[Login] reconnect attempt {_reAttempt} failed ({why}): next in {ReconnectDelays[_reAttempt]} s");
                RefreshConnecting();
                return true;
            }
            // the char server went away while the player was past the login: back to the list by itself (not after a refusal code)
            bool pastLogin = _reachedChars && (_screen == Screen.Chars || _screen == Screen.Pin || _screen == Screen.Create || _screen == Screen.Manage || _screen == Screen.MapChoice || _screen == Screen.Connecting || _screen == Screen.Password);
            if (pastLogin && RefusalText.MayReconnect(ban)) { _reachedChars = false; return BeginReconnect(why); }
            return false;
        }

        private void RefreshConnecting()
        {
            if (_connectingText == null) return;
            if (!_reconnecting) { _connectingText.text = Lang.T("Connecting…"); _connectingCancel.SetLabel(Lang.T("Cancel")); return; }
            string next = _reNextAt > 0 ? Lang.F("next try in {0:0} s", Mathf.Max(0f, _reNextAt - Time.time)) : Lang.T("trying…");
            _connectingText.text = Lang.F("The connection was lost:\n<color=#ff9a7a>{0}</color>\n\nReconnecting to {1} — attempt {2} of {3}, {4}.", Esc(_reWhy), Esc(_reServer?.name), Mathf.Max(1, _reAttempt), ReconnectDelays.Length, next);
            _connectingCancel.SetLabel(Lang.T("Stop"));
        }

        // ---------------------------------------------------------------- the accessible-map choice (0x0840 / 0x0841)
        private Text _mapChoiceInfo; private readonly List<UiButton> _mapButtons = new List<UiButton>();
        private List<(string map, bool open)> _mapChoice = new List<(string, bool)>();
        private const int MapChoiceRows = 10; // rAthena offers ten in Renewal (accessible_maps, rA:char/char_clif.cpp:32-45)

        private void BuildMapChoice(RectTransform p)
        {
            float top = (H - 180) / 2;
            _mapChoiceInfo = UiKit.Label(p, "", new Vector2(0, top - 50), new Vector2(W - 100, 90), 22, TextAnchor.MiddleCenter);
            for (int i = 0; i < MapChoiceRows; i++)
            {
                int k = i; int col = i % 2, row = i / 2;
                var b = UiKit.Button(p, "", new Vector2(-230 + col * 460, top - 150 - row * 80), new Vector2(440, 66), () => PickMap(k), 22);
                _mapButtons.Add(b);
            }
            UiKit.Button(p, Lang.T("Back"), new Vector2(0, -top + 40), new Vector2(240, 56), () => Show(Screen.Chars), 24);
        }

        private void OnMapChoice(List<(string map, bool open)> maps)
        {
            _mapChoice = maps ?? new List<(string, bool)>();
            bool any = _mapChoice.Exists(m => m.open);
            if (!any)
            {
                // rB Engine/CharEngine.js:294-306: message 1811 and back to the list
                Status(Lang.F("{0} — the map server isn't running: try again in a moment.", RefusalText.NoMap().Trim()));
                Show(Screen.Chars);
                return;
            }
            string who = Esc(_s.Characters.Find(c => c.Slot == LastSelectedSlot)?.Name ?? "");
            _mapChoiceInfo.text = Lang.F("{0}'s map has no server right now. Go to one of these maps instead:", who);
            for (int i = 0; i < _mapButtons.Count; i++)
            {
                var b = _mapButtons[i]; bool show = i < _mapChoice.Count; b.gameObject.SetActive(show);
                if (!show) continue;
                var m = _mapChoice[i];
                string name = Loki.Game.ClientText.MapName(Loki.Game.Navigation.Bare(m.map));
                b.SetLabel(m.open ? Esc(name) : Lang.F("{0} (closed)", Esc(name)));
                b.Interactable = m.open; b.SetHover(false);
            }
            Show(Screen.MapChoice);
        }

        private int LastSelectedSlot => _lastPlaySlot;
        private int _lastPlaySlot = -1;

        private void PickMap(int index)
        {
            if (index < 0 || index >= _mapChoice.Count || !_mapChoice[index].open) return;
            Status(Lang.F("Going to {0}…", Loki.Game.ClientText.MapName(Loki.Game.Navigation.Bare(_mapChoice[index].map))));
            Show(Screen.Connecting);
            _s.SelectAccessibleMap(_lastPlaySlot, index);
        }

        // ---- smoke hooks
        public IReadOnlyList<(string map, bool open)> MapChoiceList => _mapChoice;
    }
}
