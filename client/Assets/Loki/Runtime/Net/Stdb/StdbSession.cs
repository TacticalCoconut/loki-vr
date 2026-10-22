using System;
using System.Collections.Generic;
using System.Linq;
using SpacetimeDB;
using SpacetimeDB.Types;
using UnityEngine;
using UnityEngine.Networking;

namespace Loki.Net.Stdb
{
    /// <summary>
    /// The login and character screens over the Loki Online SpacetimeDB module (server/loki-server). The client keeps one
    /// WebSocket (the official SpacetimeDB SDK, <c>com.clockworklabs.spacetimedbsdk</c>), subscribes to three per-player views
    /// (<c>my_account</c>, <c>my_characters</c>, <c>my_login_notice</c>) and calls reducers; every answer arrives as a reducer result
    /// or a row change. The SDK's identity token is kept per server in PlayerPrefs, so the server recognises this PC again.
    /// Call <see cref="Update"/> every frame: the SDK only processes messages inside FrameTick.
    /// In the world, the map server is the same module: <see cref="Link"/> carries rAthena's map protocol both ways (the
    /// <c>packet</c> reducer up, <c>packet_out</c> rows down), and <see cref="RoSession"/> runs on top of it unchanged.
    /// One session lives for the whole run (<see cref="Shared"/>): a scene reload (character select, log out) keeps the
    /// connection, so the server never sees this identity leave and come back in the middle of a hand-over.
    /// </summary>
    public sealed class StdbSession : IAccountSession
    {
        /// <summary>The run's session (it outlives scene reloads).</summary>
        public static StdbSession Shared { get; private set; }
        public static StdbSession GetShared() => Shared ?? (Shared = new StdbSession());

        /// <summary>The map link while a character is in the world (null otherwise).</summary>
        public RoConnection Link { get; private set; }
        /// <summary>A character was picked and the server accepted it: the game enters the world over <see cref="OpenLink"/>.</summary>
        public event Action<CharInfo> WorldReady;
        private int _packetsIn, _packetsOut;

        /// <summary>Forgets every handler (a new scene hooks its own): the connection and login stay.</summary>
        public void DetachHandlers()
        {
            PinStateChanged = null; LoginRefused = null; Failed = null; EnteredMap = null; DeleteResult = null;
            CreateRefused = null; CharListChanged = null; Notice = null; WorldReady = null; ConnectionLost = null;
            RenameChecked = null; RenameResult = null; SlotMoved = null; SelectRefused = null; MapChoice = null; PasswordChanged = null;
            if (Link != null) { Link.Close(); Link = null; }
        }
        public RoSession.Stage State { get; private set; } = RoSession.Stage.Idle;
        public string FailReason { get; private set; }
        public int PinState { get; private set; } = -1;
        public bool PinPending => false;
        public List<CharInfo> Characters { get; } = new List<CharInfo>();
        public int TotalSlots { get; private set; } = 15;
        public byte Sex { get; private set; } = 1;
        public bool RegisterAccount { get; set; }
        public bool AutoRegister { get; set; }
        public char RegisterSex { get; set; } = 'M';
        /// <summary>Wave H (haccount): the module renames characters and moves them between slots (its rename_check / rename_character /
        /// move_character_slot reducers: rA:char/char_clif.cpp:72-144, 1421-1458, 1547-1582, char.cpp:1267-1332) and changes the
        /// account's password. A module from before wave H has none of them: it also has no <c>server_info</c> view, which is how
        /// this client tells (<see cref="ServerProtocol"/> stays 0). A saved identity logs in again (<see cref="Resume"/>).</summary>
        public bool CanRename => ServerProtocol >= 1;
        public bool CanMoveSlot => ServerProtocol >= 1;
        public bool CanChangePassword => ServerProtocol >= 1;
        public bool CanResume => true;
        /// <summary>The module's rules for a new account (conf/login_athena.conf for this PACKETVER: server/src/rules.rs).</summary>
        public int AccountNameMin => 6;
        public int AccountNameMax => 23;
        public int PasswordMinLength => PasswordMin;
        /// <summary>Client wave 4: the module sends no SC_NOTIFY_BAN on its login (its refusals are LoginRefused texts).</summary>
        public int LastBanCode => -1;
        /// <summary>Client wave 4: the password is hashed on this PC before the register reducer sees it (<see cref="ClientHash"/>).</summary>
        public bool RegisterHashed => true;
        public int UsableSlots => TotalSlots;
        /// <summary>The connection (null when not connected) — for tests and, from phase 1, the game.</summary>
        public DbConnection Conn { get; private set; }
        public string ServerKey { get; private set; }
        /// <summary>Scheduled deletions (unix seconds) as the server stores them.</summary>
        public readonly Dictionary<uint, long> DeleteDue = new Dictionary<uint, long>();

        public event Action<int> PinStateChanged;
        public event Action<int, string> LoginRefused;
        public event Action<string> Failed;
        public event Action EnteredMap;
        /// <summary>RoSession reached the map: tells the login screen (it hides itself).</summary>
        public void RaiseEnteredMap() => EnteredMap?.Invoke();
        public event Action<uint, long, string> DeleteResult;
        public event Action<string> CreateRefused;
        public event Action CharListChanged;
        public event Action<string> Notice;
        public event Action<bool, string> RenameChecked;
        public event Action<int, string> RenameResult;
        public event Action<bool, int> SlotMoved;
        /// <summary>A select the server refused (its text), or a map entry it refused after the select (<see cref="MapEntryRefused"/>).</summary>
        public event Action<string> SelectRefused;
        /// <summary>Wave H: the answer to <see cref="ChangePassword"/>: done or not, and a text for the player.</summary>
        public event Action<bool, string> PasswordChanged;
#pragma warning disable 67, 414 // the module is one world, so a character's map never lacks a server (no HC_NOTIFY_ACCESSIBLE_MAPNAME); DetachHandlers clears it
        public event Action<List<(string map, bool open)>> MapChoice;
#pragma warning restore 67, 414

        // ---------------------------------------------------------------- the version handshake (wave H; server/src/release.rs)
        /// <summary>This build's side of the account / character-screen protocol, the oldest module protocol it still works with, and
        /// the RO map protocol it is built for (rAthena's PACKETVER).</summary>
        /// <remarks>Protocol 2 (port wave I): the module's refusals carry rAthena's packet and code (<see cref="Coded"/>); a
        /// protocol 1 module sends them as plain texts, which this build would show untranslated and without its select retry.</remarks>
        public const uint ClientProtocol = 2, MinServerProtocol = 2, PacketVer = 20211103;
        /// <summary>The module's protocol from its <c>server_info</c> view (0: a module from before the handshake, or not known yet).</summary>
        public uint ServerProtocol { get; private set; }
        /// <summary>The module's mode from <c>server_info</c>: 0 test (every player has the GM commands), 1 release; -1 unknown.</summary>
        public int ServerMode { get; private set; } = -1;
        private bool _infoDone;

        /// <summary>The module's own texts (reducer errors and login notices) in the player's language: the ones this client knows
        /// are table keys; any other text is shown as the server wrote it.</summary>
        public static string Tr(string serverText)
        {
            if (string.IsNullOrEmpty(serverText)) return serverText;
            return Array.IndexOf(ServerTexts, serverText) >= 0 ? Lang.T(serverText) : serverText;
        }

        /// <summary>Port wave I (ionline): a refusal the module sends with rAthena's packet and code in front
        /// (server/src/account.rs refuse_login, notify_ban, refuse_enter): "#login:1 Incorrect Password.",
        /// "#login:6[2026-10-07 12:00:00] You are prohibited …" (AC_REFUSE_LOGIN error and unblock_time), "#ban:8 …"
        /// (SC_NOTIFY_BAN), "#enter:0 …" (HC_REFUSE_ENTER). False for a plain text (the module's own refusals, and a rename's
        /// "#4 …": <see cref="RenameCode"/>).</summary>
        public static bool Coded(string raw, out string family, out int code, out string arg, out string text)
        {
            family = ""; code = -1; arg = null; text = raw ?? "";
            if (string.IsNullOrEmpty(raw) || raw[0] != '#') return false;
            int colon = raw.IndexOf(':');
            if (colon < 2) return false;
            for (int k = 1; k < colon; k++) if (raw[k] < 'a' || raw[k] > 'z') return false;
            int i = colon + 1, digits = i;
            while (i < raw.Length && raw[i] >= '0' && raw[i] <= '9') i++;
            if (i == digits || !int.TryParse(raw.Substring(digits, i - digits), out int n)) return false;
            if (i < raw.Length && raw[i] == '[')
            {
                int close = raw.IndexOf(']', i);
                if (close < 0) return false;
                arg = raw.Substring(i + 1, close - i - 1); i = close + 1;
            }
            family = raw.Substring(1, colon - 1); code = n;
            text = i < raw.Length && raw[i] == ' ' ? raw.Substring(i + 1) : raw.Substring(i);
            return true;
        }

        /// <summary>What the player reads for a refusal of the module, and rAthena's code for it (-1: the module's own refusal, which
        /// has none). A coded refusal shows the official client's message for the code, exactly as the same refusal from rAthena
        /// does (<see cref="RefusalText"/>: the player's msgstringtable, the code in brackets) — unless its text is one of the
        /// module's own that this client knows (<see cref="ServerTexts"/>: Loki's words where the module says more than the code,
        /// e.g. why refusal 3 came): that one is shown, in the player's language. A plain text goes through <see cref="Tr"/>.</summary>
        public static string Shown(string raw, out int code, out string family)
        {
            if (!Coded(raw, out family, out code, out string arg, out string text)) return Tr(raw);
            if (Array.IndexOf(ServerTexts, text) >= 0) return Lang.T(text);
            switch (family)
            {
                case "login": return RefusalText.Login(code, arg);
                case "ban": return RefusalText.Ban(code);
                case "enter": return RefusalText.CharRefused(code);
                // the character deletion's results (rA:char/char_clif.cpp:522-585; server/src/account.rs refuse_delete)
                case "delres": return RefusalText.DeleteReserve(code);
                case "delete": return RefusalText.DeleteConfirm(code);
                case "delcancel": return RefusalText.DeleteCancel(code);
                default: return text;
            }
        }
        public static string Shown(string raw) => Shown(raw, out _, out _);

        /// <summary>server/src/account.rs, character.rs, release.rs: every fixed text of Loki's own a refusal can carry. (rAthena's
        /// own texts — "Incorrect Password.", "Rejected from server.", "Server still recognizes your last login." — are not
        /// here: they come with a code and the client's own message is shown for it.)</summary>
        private static readonly string[] ServerTexts =
        {
            Lang.N("Too many wrong passwords: this account is locked for a few minutes on PCs it has not been played from."),
            Lang.N("Too many failed logins from this PC: rejected from the server for a few minutes."),
            Lang.N("Too many new accounts right now: try again in a few seconds."),
            Lang.N("That account name is taken."),
            Lang.N("Account names are 6 to 23 characters long."),
            Lang.N("Account names can't contain control characters."),
            Lang.N("Pick male or female."),
            Lang.N("This client is too old for Loki Online (the password must be sent hashed)."),
            Lang.N("This Loki build is too old for this server: update Loki."),
            Lang.N("You are not logged in."),
            Lang.N("Your account no longer exists."),
            Lang.N("The current password is wrong."),
            Lang.N("The new password is the same as the current one."),
            Lang.N("Character names need at least 4 letters."),
            Lang.N("That name is reserved."),
            Lang.N("Use only letters, digits and spaces in character names."),
            Lang.N("That name is already taken."),
            Lang.N("That character slot isn't available."),
            Lang.N("New characters start as a Novice or a Summoner."),
            Lang.N("This account has no free character slot."),
            Lang.N("That character slot is already used."),
            Lang.N("That character isn't on your account."),
            Lang.N("This character's deletion is already scheduled."), // (result 0, for which the client only has "an unknown error")
            Lang.N("Cancel this character's deletion to play it."),
            Lang.N("That name is taken or not allowed."),
            Lang.N("This character has no slot move left."),
            Lang.N("There is no character in that slot."),
            Lang.N("That slot is used."),
        };

        private string _host, _db, _user, _pass; private int _port;
        private bool _pendingLogin, _loggedIn, _resetting, _subscribed;
        /// <summary>Counts the connections this session opened: a callback of an older one (a connect error that arrives after the
        /// next attempt started) is not this attempt's.</summary>
        private int _attempt;
        /// <summary>This session was logged in on <see cref="ServerKey"/> during this run (what a refused resume says depends on it).</summary>
        private bool _wasLoggedIn;

        /// <summary>Which saved identity to use: "" for normal play; tests running two clients on one PC give each its own
        /// (PlayerPrefs are shared by every Loki process, and one identity can only be one player).</summary>
        public static string Profile = "";
        private static string TokenKey(string serverKey) => "loki.stdb.token." + serverKey + (string.IsNullOrEmpty(Profile) ? "" : "#" + Profile);

        private bool _triedFallback;
        internal static bool IsAutomatedRun()
        {
            return false;
        }

        // ---------------------------------------------------------------- connection
        public void Login(string host, int port, string database, string user, string pass)
        {
            _user = user ?? ""; _pass = pass ?? ""; _pendingLogin = true; _triedFallback = false;
            Connect(host, port, database);
        }

        /// <summary>Connects with the saved identity only: an account this PC logged in to before is recognised without a password.</summary>
        public void Resume(string host, int port, string database)
        {
            _user = null; _pass = null; _pendingLogin = false; _triedFallback = false;
            Connect(host, port, database);
        }

        /// <summary>Splits "wss://name" / "https://name" / "ws://name" / "name" into (scheme, host): "wss" is TLS (local), "ws" plain.
        /// The SDK itself maps http → ws and https → wss (SpacetimeDBClient.cs Connect), so both spellings work.</summary>
        public static (string scheme, string host) SplitHost(string host, string scheme = null) => ServerAddress.Split(host, scheme);
        /// <summary>The address the SDK is given: scheme://host:port.</summary>
        public string Uri { get; private set; } = "";
        /// <summary>The connection closed without us asking (a server close, a network failure): the reason, for the game and the login screen.</summary>
        public event Action<string> ConnectionLost;
        /// <summary>For the smoke: how the last connection ended ("" while open).</summary>
        public string LastClose { get; private set; } = "";

        private void Connect(string host, int port, string database)
        {
            string db = string.IsNullOrEmpty(database) ? "loki" : database;
            var (scheme, bare) = SplitHost(host);
            string key = $"{bare}:{port}/{db}"; // the saved identity is per server, whatever the scheme was written as
            FailReason = null; PinState = -1;
            if (Conn != null && Conn.IsActive && key == ServerKey && _subscribed && _infoDone) { OnSubscribed(); return; }
            Disconnect();
            if (key != ServerKey) _wasLoggedIn = false;
            _host = bare; _port = port; _db = db; ServerKey = key; Uri = $"{scheme}://{bare}:{port}"; LastClose = "";
            ServerProtocol = 0; ServerMode = -1; _infoDone = false;
            State = RoSession.Stage.LoginConnecting;
            string token = null;
            try { token = PlayerPrefs.GetString(TokenKey(key), ""); } catch { }
            _triedToken = token ?? "";
            Debug.Log($"[Stdb] connecting to {Uri} database {_db} ({(string.IsNullOrEmpty(token) ? "new identity" : "saved identity")})");
            int attempt = ++_attempt;
            try
            {
                var conn = DbConnection.Builder()
                    .WithUri(Uri)
                    .WithDatabaseName(_db)
                    .WithToken(string.IsNullOrEmpty(token) ? null : token)
                    .OnConnect((c, id, tok) => { if (attempt == _attempt) OnConnected(c, id, tok); })
                    .OnConnectError(e => { if (attempt == _attempt) OnConnectFailed(e); })
                    .OnDisconnect((c, e) => OnClosed(c, e))
                    .Build();
                Conn = conn;
            }
            catch (Exception e) { Fail(Lang.F("Can't connect to {0}: {1}", Uri, e.Message)); }
        }

        /// <summary>The connection could not be opened (the server is not there, the database does not exist, … or the server
        /// rejects this PC's saved identity: <see cref="StartTokenProbe"/> finds out which before the failure is reported).</summary>
        private void OnConnectFailed(Exception e)
        {
            Conn = null; _subscribed = false; _infoDone = false;
            // The SDK's reason is twenty lines of advice for developers, in English ("A WebSocketException occurred, even though the
            // WebSocketErrorCode is "Success"…"): its first line goes to the log, the player reads what can be done about it.
            Debug.Log("[Stdb] connect error: " + FirstLine(e.Message));
            string why = Lang.F("Can't reach the Loki Online server at {0}: is it running, and is the address right?", Uri);
            if (_triedToken.Length > 0 && StartTokenProbe(why)) return;
            _pendingLogin = false;
            Fail(why);
        }

        // ---------------------------------------------------------------- a saved identity the server rejects (port wave I, ionline)
        // The SDK cannot say why a connection was refused (WebSocket.cs:236-270: no HTTP status in this .NET), so a server that
        // rejects the saved token (it was reinstalled, or moved to another PC: its signing key changed; SpacetimeDB answers 401
        // "Invalid token") looked like a server that is off, on every start, for ever: the token was never removed (audit row A3).
        // After a failed connect WITH a saved token the client now asks the server over HTTP: GET /v1/ping with the token
        // (SpacetimeDB checks an Authorization header on every route: 401 for a token it does not accept) and, when that says
        // 401, the same without it (200: the server is there and takes anonymous clients, so the token is what it refuses).
        // Only then the saved identity is removed and the player told; a login typed with the password goes on at once with a
        // new identity. Any other answer (no answer, 200 with the token) is the old failure, and the token stays.
        private string _triedToken = "";
        private UnityWebRequest _probe; private int _probeStep, _probeAttempt; private string _probeWhy = ""; private float _probeUntil;
        /// <summary>For the smoke: how many saved identities this session removed because the server rejected them.</summary>
        public int IdentitiesRemoved { get; private set; }
        private string _removedNotice;

        private string HttpBase => (Uri.StartsWith("wss://", StringComparison.Ordinal) ? "https://" : "http://") + _host + ":" + _port;

        private bool StartTokenProbe(string why)
        {
            _probeWhy = why; _probeStep = 1; _probeAttempt = _attempt;
            return SendProbe(_triedToken);
        }

        private bool SendProbe(string token)
        {
            try
            {
                var req = UnityWebRequest.Get(HttpBase + "/v1/ping");
                req.timeout = 5;
                if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", "Bearer " + token);
                req.SendWebRequest();
                _probe = req; _probeUntil = Time.realtimeSinceStartup + 6f;
                return true;
            }
            catch (Exception e) { Debug.LogWarning("[Stdb] asking the server about the saved identity: " + e.Message); _probe = null; return false; }
        }

        private void TickProbe()
        {
            if (_probe == null || !(_probe.isDone || Time.realtimeSinceStartup > _probeUntil)) return;
            var req = _probe; _probe = null;
            long code = req.isDone ? req.responseCode : 0;
            try { req.Dispose(); } catch { }
            if (_probeAttempt != _attempt) return; // the attempt was dropped meanwhile
            // (401 "Invalid token" is what SpacetimeDB 2.10.1 answers; any refusal of the request WITH the token counts, since
            // the second step shows whether the same request without it is taken)
            if (_probeStep == 1 && code >= 400 && code < 500) { _probeStep = 2; if (SendProbe(null)) return; code = 0; }
            else if (_probeStep == 2 && code == 200) { TokenRejected(); return; }
            Debug.Log($"[Stdb] the saved identity is not what failed (the server's answer over HTTP: step {_probeStep}, {(code == 0 ? "none" : code.ToString())})");
            _pendingLogin = false;
            Fail(_probeWhy);
        }

        private void DropProbe()
        {
            if (_probe == null) return;
            try { _probe.Abort(); _probe.Dispose(); } catch { }
            _probe = null;
        }


        /// <summary>The server is there and refuses this PC's saved identity: it is removed (it can never work again) and the player
        /// told. A login with the password that was waiting connects again as a new PC; "Continue as before" and a reconnect end
        /// here with the notice (the panel shows the login screen, without the Continue button).</summary>
        private void TokenRejected()
        {
            Debug.LogWarning($"[Stdb] {Uri} rejects this PC's saved identity for {ServerKey}: removed");
            try { PlayerPrefs.DeleteKey(TokenKey(ServerKey)); PlayerPrefs.Save(); } catch (Exception e) { Debug.LogWarning("[Stdb] removing the saved identity: " + e.Message); }
            IdentitiesRemoved++; _triedToken = ""; _wasLoggedIn = false;
            string told = Lang.T("This server no longer accepts this PC's saved login (it was reinstalled or moved), so it was removed.");
            if (_pendingLogin && _user != null)
            {
                _removedNotice = told;
                string scheme = Uri.Substring(0, Uri.IndexOf("://", StringComparison.Ordinal));
                Connect(scheme + "://" + _host, _port, _db);
                return;
            }
            _pendingLogin = false; State = RoSession.Stage.Idle; FailReason = null;
            Notice?.Invoke(told + " " + Lang.T("Log in with your account name and password."));
        }

        /// <summary>The SDK's connect errors run to twenty lines of advice for developers: the player gets the first.</summary>
        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int nl = s.IndexOf('\n');
            return (nl >= 0 ? s.Substring(0, nl) : s).Trim();
        }

        /// <summary>The SDK's close callback (e == null for a clean close). Closes we asked for (Disconnect / log out) are quiet;
        /// any other close is a lost connection: the login is gone, the game shows why and goes back to the login screen.</summary>
        private void OnClosed(DbConnection c, Exception e)
        {
            if (_resetting || c != Conn) { Debug.Log("[Stdb] connection closed (requested)"); return; }
            // The SDK (WebSocket.cs receive loop) reports a NormalClosure (1000) as null, and a server "going away" (1001
            // EndpointUnavailable: the database was dropped or republished, the server stopped) with the text "(1000) The connection
            // has closed after the request was fulfilled." — seen when the smoke republished its database with --delete-data.
            bool goingAway = e != null && e.Message != null && e.Message.StartsWith("(1000)", StringComparison.Ordinal);
            string why = e == null ? Lang.T("The Loki Online server closed the connection.")
                       : goingAway ? Lang.T("The Loki Online server went away (it stopped, restarted or was updated).")
                       : Lang.F("The connection to Loki Online was lost: {0}", e.Message);
            LastClose = e == null ? "clean" : goingAway ? "server going away" : "error";
            Debug.LogWarning($"[Stdb] {why} ({(e != null ? "error" : "clean close")})");
            Conn = null; _loggedIn = false; _subscribed = false; _pendingLogin = false; _infoDone = false;
            Characters.Clear(); DeleteDue.Clear();
            ClientUpdater.RequestImmediateCheck();
            Fail(why);
            ConnectionLost?.Invoke(why);
        }

        /// <summary>Whether this PC holds a saved identity for the server (the login screen then offers "Continue").</summary>
        public static bool HasSavedIdentity(string host, int port, string database)
        {
            var (_, bare) = SplitHost(host);
            string key = $"{bare}:{port}/{(string.IsNullOrEmpty(database) ? "loki" : database)}";
            try { return PlayerPrefs.GetString(TokenKey(key), "").Length > 0; } catch { return false; }
        }

        public void Update()
        {
            try { Conn?.FrameTick(); } catch (Exception e) { Debug.LogException(e); }
            TickProbe();
            if (_blockEnds != 0 && _loggedIn && Conn != null && DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= _blockEnds) CharsChanged(); // a character's block ended
            // a select refused because the account's other session is still leaving the world is asked again by itself
            if (_selectRetryAt > 0f && Time.realtimeSinceStartup >= _selectRetryAt)
            {
                _selectRetryAt = -1f;
                if (Conn != null && Conn.IsActive && _loggedIn) { Debug.Log($"[Stdb] selecting character {_selectRetryChar} again (attempt {_selectRetries + 1})"); Conn.Reducers.SelectCharacter(_selectRetryChar); }
            }
        }

        /// <summary>The module's "Server still recognizes your last login" (rAthena's refusal 8: the account's other session is being
        /// kicked and has not left the world yet). rAthena's client asks again by itself (Net/RoSession.cs: SC_NOTIFY_BAN 8 on
        /// the login link, 3 s apart); here the select is asked again, up to <see cref="SelectRetries"/> times (a character
        /// that fought waits out prevent_logout's 10 s before it leaves).</summary>
        public const int SelectRetries = 5;
        private int _selectRetries; private float _selectRetryAt = -1f; private uint _selectRetryChar;
        /// <summary>The last notice <see cref="NoticeOf"/> returned: as the module wrote it, and its packet and code ("" / -1 none).</summary>
        private string _noticeRaw = "", _noticeFamily = ""; private int _noticeCode = -1;
        /// <summary>SC_NOTIFY_BAN 8, "Server still recognizes your last login" (server/src/character.rs char_auth_ok).</summary>
        private bool NoticeIsStillOnline => _noticeFamily == "ban" && _noticeCode == 8;

        /// <summary>Drops the connection without logging out (the identity stays bound to its account on the server).</summary>
        public void Disconnect()
        {
            _attempt++; // (a connection that was still opening, or the question about its saved identity, reports nothing any more)
            DropProbe();
            if (Conn == null) return;
            _resetting = true;
            try { if (Conn.IsActive) Conn.Disconnect(); } catch (Exception e) { Debug.LogWarning("[Stdb] disconnect: " + e.Message); }
            _resetting = false;
            Conn = null; _loggedIn = false; _subscribed = false; _infoDone = false; State = RoSession.Stage.Idle; Characters.Clear(); DeleteDue.Clear();
        }

        /// <summary><see cref="IAccountSession.Drop"/>: the connection goes and the session is idle again; this PC's login stays on the
        /// server, so <see cref="Resume"/> gets back in (a reconnect: UI/LoginPanel.Reconnect.cs).</summary>
        public void Drop()
        {
            FailReason = null; PinState = -1; _pendingLogin = false; _selectRetryAt = -1f; _selectRetries = 0;
            Disconnect();
            State = RoSession.Stage.Idle;
        }

        /// <summary>The login screen's Log out: forget this PC's login on the server, then disconnect.</summary>
        public void Reset()
        {
            FailReason = null; PinState = -1; _pendingLogin = false; _noticeRaw = null; _noticeCode = -1; _noticeFamily = "";
            if (Conn != null && Conn.IsActive && _loggedIn)
            {
                _loggedIn = false; State = RoSession.Stage.Idle; Characters.Clear(); DeleteDue.Clear();
                try { Conn.Reducers.Logout(); return; } catch (Exception e) { Debug.LogWarning("[Stdb] logout: " + e.Message); }
            }
            if (Conn == null || !Conn.IsActive) Disconnect();
            else { _loggedIn = false; State = RoSession.Stage.Idle; Characters.Clear(); DeleteDue.Clear(); }
        }

        private void OnConnected(DbConnection conn, Identity identity, string token)
        {
            try { PlayerPrefs.SetString(TokenKey(ServerKey), token); PlayerPrefs.Save(); } catch { }
            Debug.Log($"[Stdb] connected as {identity}");
            conn.Reducers.OnLogin += OnLoginResult;
            conn.Reducers.OnRegister += OnRegisterResult;
            conn.Reducers.OnCreateCharacter += (ctx, slot, name, hair, color, cls, sex) => { if (ctx.Event.Status is Status.Failed(var why)) { if (CheckUpdateNotice(why)) return; CreateRefused?.Invoke(Coded(why, out _, out _, out _, out _) ? Shown(why) : CreateRefusedText(why, name)); } else if (NoticeOf(ctx, out string n)) { if (CheckUpdateNotice(_noticeRaw)) return; CreateRefused?.Invoke(n); } };
            conn.Reducers.OnRequestDelete += (ctx, cid) => DeleteAnswer(ctx, cid, Lang.T("Deletion scheduled.")); // (the row counts the time down: no delay is named here, a server may set its own)
            conn.Reducers.OnConfirmDelete += (ctx, cid, birthdate) => DeleteAnswer(ctx, cid, Lang.T("Character deleted."));
            conn.Reducers.OnCancelDelete += (ctx, cid) => DeleteAnswer(ctx, cid, Lang.T("Deletion cancelled."));
            conn.Reducers.OnSelectCharacter += OnSelectResult;
            conn.Reducers.OnRenameCheck += OnRenameCheckResult;
            conn.Reducers.OnRenameCharacter += OnRenameResult;
            conn.Reducers.OnMoveCharacterSlot += OnMoveSlotResult;
            conn.Reducers.OnChangePassword += OnChangePasswordResult;
            conn.Reducers.OnLogout += ctx => { Debug.Log("[Stdb] logged out"); };
            conn.Db.PacketOut.OnInsert += (ctx, row) =>
            {
                _packetsIn++;
                var link = Link;
                if (link == null) return; // no character in the world on this client (a late packet after leaving)
                var bytes = new byte[row.Data.Count]; row.Data.CopyTo(bytes);
                link.Feed(bytes);
            };
            conn.Db.MyCharacters.OnInsert += (ctx, row) => CharsChanged();
            conn.Db.MyCharacters.OnDelete += (ctx, row) => CharsChanged();
            conn.Db.MyCharacters.OnUpdate += (ctx, a, b) => CharsChanged();
            conn.Db.MyLoginNotice.OnInsert += (ctx, row) => CheckUpdateNotice(row.Message);
            conn.Db.MyLoginNotice.OnUpdate += (ctx, a, b) => CheckUpdateNotice(b.Message);
            conn.SubscriptionBuilder()
                .OnApplied(_ => { _subscribed = true; if (_infoDone) OnSubscribed(); })
                .OnError((_, e) => Fail(Lang.F("The Loki Online server refused this client's subscription: {0}", e.Message)))
                .AddQuery(qb => qb.From.MyAccount())
                .AddQuery(qb => qb.From.MyCharacters())
                .AddQuery(qb => qb.From.MyLoginNotice())
                .AddQuery(qb => qb.From.PacketOut())
                .Subscribe();
            // The version handshake, in its own subscription: a module from before wave H has no server_info view, and its refusal
            // of this one query must not take the four above with it. Such a module is played as before (no hello, no checks).
            conn.Db.ServerInfo.OnInsert += (ctx, row) => { ServerMode = row.Mode; }; // (a view row has no key: a change is a delete and an insert)
            conn.SubscriptionBuilder()
                .OnApplied(_ => OnServerInfo(conn))
                .OnError((_, e) => { if (conn != Conn) return; Debug.Log("[Stdb] this server has no version handshake (a module from before wave H): " + e.Message); _infoDone = true; if (_subscribed) OnSubscribed(); })
                .AddQuery(qb => qb.From.ServerInfo())
                .Subscribe();
        }

        private bool CheckUpdateNotice(string raw)
        {
            if (string.IsNullOrEmpty(raw) || !raw.StartsWith("#update:", StringComparison.Ordinal)) return false;
            if (!ClientUpdater.TryStartFromNotice(raw, msg => Notice?.Invoke(msg))) return false;
            _pendingLogin = false;
            var link = Link;
            Link = null;
            if (link != null) { try { link.Close(); } catch { } }
            Disconnect();
            State = RoSession.Stage.Idle;
            return true;
        }

        /// <summary>The module said what it is: say what this build is (the <c>hello</c> reducer, before any login), or stop here with
        /// the reason when the two cannot work together.</summary>
        private void OnServerInfo(DbConnection conn)
        {
            if (conn != Conn) return;
            var info = conn.Db.ServerInfo.Iter().FirstOrDefault();
            if (info == null) { _infoDone = true; if (_subscribed) OnSubscribed(); return; }
            ServerProtocol = info.Protocol; ServerMode = info.Mode;
            Debug.Log($"[Stdb] server protocol {info.Protocol} (clients from {info.MinClientProtocol}), PACKETVER {info.Packetver}, mode {(info.Mode == 1 ? "release" : "test")}; this build: protocol {ClientProtocol}, servers from {MinServerProtocol}");
            if (info.MinClientProtocol > ClientProtocol || info.Packetver != PacketVer)
            {
                if (IsAutomatedRun()) { _pendingLogin = false; Refuse(5, Lang.T("This Loki build is too old for this server: update Loki.")); return; }
                Debug.LogWarning($"[Stdb] server min protocol {info.MinClientProtocol} / packetver {info.Packetver} differs from client ({ClientProtocol} / {PacketVer}); continuing");
            }
            if (info.Protocol < MinServerProtocol)
            {
                if (IsAutomatedRun()) { _pendingLogin = false; Refuse(5, Lang.T("This server is older than this Loki build: ask its host to update it.")); return; }
                Debug.LogWarning($"[Stdb] server protocol {info.Protocol} < client min {MinServerProtocol}; continuing");
            }
            try { conn.Reducers.Hello(ClientProtocol, PacketVer, ClientUpdater.BuildHelloLabel(IsAutomatedRun())); } catch (Exception e) { Debug.LogWarning("[Stdb] hello: " + e.Message); }
            _infoDone = true;
            if (_subscribed) OnSubscribed();
        }

        private void OnSubscribed()
        {
            if (CheckUpdateNotice(Conn.Db.MyLoginNotice.Iter().FirstOrDefault()?.Message)) return;
            if (_pendingLogin) { State = RoSession.Stage.LoginSent; SendLogin(); return; }
            // a resume: logged in already if the server still binds this identity to an account
            if (Conn.Db.MyAccount.Count > 0) LoggedIn();
            // (after a login in this run the server has dropped it since: its data was reset, the account's password was changed on
            // another PC or by the owner)
            else { State = RoSession.Stage.Idle; Notice?.Invoke(_wasLoggedIn ? Lang.T("This PC is no longer logged in to Loki Online: log in with your account name and password.") : Lang.T("This PC isn't logged in to Loki Online yet.")); _wasLoggedIn = false; }
        }

        /// <summary>Loki Online's password rule (rAthena's for this client version: login.cpp, PACKETVER >= 20181114).</summary>
        public const int PasswordMin = 6, PasswordMax = 32;

        /// <summary>What travels instead of the password: hex(SHA-256("loki-online:" + lower-case account + ":" + password)).
        /// The server stores an Argon2id hash of it (server/loki-server/src/account.rs check_client_hash).</summary>
        public static string ClientHash(string user, string pass)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("loki-online:" + (user ?? "").Trim().ToLowerInvariant() + ":" + (pass ?? "")));
                var sb = new System.Text.StringBuilder(64);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private void SendLogin()
        {
            _pendingLogin = false;
            if (_pass.Length < PasswordMin || _pass.Length > PasswordMax) { RegisterAccount = false; Refuse(1, Lang.F("Passwords are {0} to {1} characters long.", PasswordMin, PasswordMax)); return; }
            string hash = ClientHash(_user, _pass); _pass = null;
            if (RegisterAccount)
            {
                RegisterAccount = false;
                Debug.Log($"[Stdb] registering {_user}");
                Conn.Reducers.Register(_user, hash, (byte)(RegisterSex == 'F' ? 0 : 1));
            }
            else
            {
                Debug.Log($"[Stdb] logging in as {_user}");
                Conn.Reducers.Login(_user, hash);
            }
        }

        private void OnRegisterResult(ReducerEventContext ctx, string user, string pass, byte sex)
        {
            // (login_mmo_auth_new's codes, rA:login/login.cpp:215-249, come with the module's own, more exact text: that is shown)
            if (ctx.Event.Status is Status.Failed(var why)) { if (CheckUpdateNotice(why)) return; string t = Shown(why, out int code, out _); Refuse(code, t); return; }
            LoggedIn();
        }

        private void OnLoginResult(ReducerEventContext ctx, string user, string pass)
        {
            // the refusal's code is rAthena's AC_REFUSE_LOGIN error where rAthena has one (-1: the module's own, e.g. the account lock)
            if (ctx.Event.Status is Status.Failed(var why)) { if (CheckUpdateNotice(why)) return; string t = Shown(why, out int code, out _); Refuse(code, t); return; }
            // A wrong password commits (the failure counter must persist): the reason is this attempt's notice row.
            if (NoticeOf(ctx, out string refused)) { if (CheckUpdateNotice(_noticeRaw)) return; Refuse(_noticeCode, refused); return; }
            if (Conn.Db.MyAccount.Count == 0) { var notice = Conn.Db.MyLoginNotice.Iter().FirstOrDefault(); if (notice != null && CheckUpdateNotice(notice.Message)) return; int code = -1; string t = notice != null ? Shown(notice.Message, out code, out _) : Lang.T("Login refused."); Refuse(code, t); return; }
            LoggedIn();
        }

        /// <summary>The module says why it refused a new character; for a name with letters outside char_name_letters the text also
        /// names them, as the rAthena session's does (<see cref="RoSession.NameLettersOutsideStock"/>).</summary>
        public static string CreateRefusedText(string why, string name)
        {
            string text = Tr(why);
            if (why != "Use only letters, digits and spaces in character names.") return text;
            string odd = RoSession.NameLettersOutsideStock(name);
            return odd.Length > 0 ? text + " " + Lang.F("This server doesn't accept {0} in names.", odd) : text;
        }

        /// <summary>A reducer that succeeded may still have refused: what must leave a trace on the server (a counted wrong
        /// password, the kick of the account's character in the world: "Server still recognizes your last login") cannot fail the
        /// call, so the reason is the caller's <c>my_login_notice</c> row of that very call (same timestamp).</summary>
        private bool NoticeOf(ReducerEventContext ctx, out string text)
        {
            text = null;
            var notice = Conn?.Db.MyLoginNotice.Iter().FirstOrDefault();
            if (notice == null || notice.At.MicrosecondsSinceUnixEpoch != ctx.Event.Timestamp.MicrosecondsSinceUnixEpoch) return false;
            _noticeRaw = notice.Message; text = Shown(notice.Message, out _noticeCode, out _noticeFamily);
            return true;
        }

        private void Refuse(int code, string why)
        {
            Debug.Log("[Stdb] login refused: " + why);
            State = RoSession.Stage.Failed; FailReason = why;
            LoginRefused?.Invoke(code, why);
        }

        private void LoggedIn()
        {
            var acc = Conn.Db.MyAccount.Iter().FirstOrDefault();
            if (acc != null) { Sex = acc.Sex; TotalSlots = acc.CharSlots; }
            _loggedIn = true; _wasLoggedIn = true; State = RoSession.Stage.CharList; PinState = 0;
            Debug.Log($"[Stdb] logged in as {acc?.Username} (account {acc?.Id}), {Conn.Db.MyCharacters.Count} character(s)");
            RebuildChars();
            CharListChanged?.Invoke();
            PinStateChanged?.Invoke(0);
            foreach (var kv in DeleteDue) DeleteResult?.Invoke(kv.Key, kv.Value, "");
            if (_removedNotice != null) { string told = _removedNotice; _removedNotice = null; Notice?.Invoke(told); } // (the list is up: the status line is free)
        }

        private void CharsChanged()
        {
            if (!_loggedIn) return;
            RebuildChars();
            if (Link != null || State == RoSession.Stage.InGame) return;
            CharListChanged?.Invoke();
        }

        private void RebuildChars()
        {
            Characters.Clear(); DeleteDue.Clear(); _blockEnds = 0;
            foreach (var c in Conn.Db.MyCharacters.Iter())
            {
                Characters.Add(new CharInfo
                {
                    CharId = c.Id, Name = c.Name, Slot = c.Slot, Job = (short)c.Class, Body = (short)(c.Body > 4331 && c.Body < 4350 ? 1 : 0), BaseLevel = (short)c.BaseLevel, JobLevel = c.JobLevel,
                    // what the list's picture of the character wears, as CHARACTER_INFO carries it (rA:char/char.cpp:1814-1826, 1851
                    // char_mmo_char_tobuf: no weapon while riding, accessory = head_bottom, accessory2 = head_top, accessory3 =
                    // head_mid, headpalette = hair_color, robePalette = robe; body = 1 for a body in JOB_SECOND_JOB_START..END, 4331..4350,
                    // :1808-1812). Port wave I: these were left 0 online (the pictures had the default hair colour and no gear).
                    Weapon = (short)((c.Option & (0x20 | 0x80000 | 0x100000 | 0x200000 | 0x400000 | 0x800000 | 0x1000000 | 0x2000000 | 0x4000000 | 0x8000000)) != 0 ? 0 : c.Weapon),
                    Shield = (short)c.Shield, Accessory = (short)c.HeadBottom, Accessory2 = (short)c.HeadTop, Accessory3 = (short)c.HeadMid,
                    HeadPalette = (short)c.HairColor, RobePalette = c.Robe,
                    BaseExp = (long)c.BaseExp, JobExp = (long)c.JobExp, Zeny = (int)c.Zeny, StatPoint = (short)c.StatusPoint, JobPoint = (short)c.SkillPoint,
                    Hp = c.Hp, MaxHp = c.MaxHp, Sp = c.Sp, MaxSp = c.MaxSp, Head = (short)c.Hair, HairColor = (byte)c.HairColor, BodyPalette = (short)c.ClothesColor,
                    Str = (byte)c.Str, Agi = (byte)c.Agi, Vit = (byte)c.Vit, Int = (byte)c.Int, Dex = (byte)c.Dex, Luk = (byte)c.Luk, Sex = c.Sex,
                    MapName = c.LastMap + ".gat", Speed = 150,
                    // CHARACTER_INFO as rA:char/char.cpp:1839, 1856-1864 fills it: bIsChangedCharName, chr_slot_changeCnt (the moves
                    // left: char_moves_unlimited is off), chr_name_changeCnt (1 while a rename is available)
                    IsChangedName = (short)(c.Rename > 0 ? 0 : 1), SlotChangeCount = (int)Math.Min(c.Moves, int.MaxValue), NameChangeCount = c.Rename > 0 ? 1 : 0,
                });
                if (c.DeleteAt is Timestamp at) { DeleteDue[c.Id] = at.MicrosecondsSinceUnixEpoch / 1_000_000; Characters[Characters.Count - 1].DeleteAt = DeleteDue[c.Id]; } // CharInfo.DeleteAt: client wave 4
                // HC_BLOCK_CHARACTER (rA:char/char_clif.cpp:1480-1518 chclif_block_character): a character whose unban_time is still
                // to come is listed with that date, "%Y-%m-%d %H:%M:%S" (the module's row has the time itself: shown in this PC's
                // time zone); the Play button is off for it and a select is refused (server/src/character.rs select_character)
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (c.UnbanTime > now) { Characters[Characters.Count - 1].BlockedUntil = DateTimeOffset.FromUnixTimeSeconds(c.UnbanTime).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"); if (_blockEnds == 0 || c.UnbanTime < _blockEnds) _blockEnds = c.UnbanTime; }
            }
        }
        /// <summary>The earliest end of a listed character's block (unix seconds; 0 none): the list is made again when it passes
        /// (rAthena sends the block list again every 10 s while a character is blocked).</summary>
        private long _blockEnds;

        private void DeleteAnswer(ReducerEventContext ctx, uint cid, string ok)
        {
            if (ctx.Event.Status is Status.Failed(var why)) { if (CheckUpdateNotice(why)) return; DeleteResult?.Invoke(cid, DeleteDue.TryGetValue(cid, out long still) ? still : 0, Shown(why)); return; }
            if (NoticeOf(ctx, out string refused)) { if (CheckUpdateNotice(_noticeRaw)) return; DeleteResult?.Invoke(cid, DeleteDue.TryGetValue(cid, out long kept) ? kept : 0, refused); return; }
            RebuildChars();
            DeleteResult?.Invoke(cid, DeleteDue.TryGetValue(cid, out long due) ? due : 0, ok);
        }

        private void OnSelectResult(ReducerEventContext ctx, uint cid)
        {
            // (HC_REFUSE_ENTER 0 for a select the char server rejects: rA:char/char_clif.cpp:1090-1113; the list stays)
            if (ctx.Event.Status is Status.Failed(var why)) { if (CheckUpdateNotice(why)) return; State = RoSession.Stage.CharList; SelectRefused?.Invoke(Shown(why)); return; }
            // the account's character was still in the world (it is being kicked now): refused with a notice, the list stays
            if (NoticeOf(ctx, out string refused))
            {
                if (CheckUpdateNotice(_noticeRaw)) return;
                if (NoticeIsStillOnline && _selectRetries < SelectRetries)
                {
                    _selectRetries++; _selectRetryChar = cid; _selectRetryAt = Time.realtimeSinceStartup + 3f;
                    Debug.Log($"[Stdb] select refused ({_noticeRaw}): asking again in 3 s ({_selectRetries} of {SelectRetries})");
                    Notice?.Invoke(refused + " " + Lang.T("Trying again in a moment…"));
                    return;
                }
                State = RoSession.Stage.CharList; SelectRefused?.Invoke(refused); return;
            }
            _selectRetries = 0;
            var ch = Characters.Find(c => c.CharId == cid);
            Debug.Log($"[Stdb] selected {ch?.Name} ({cid})");
            if (ch == null) { Notice?.Invoke(Lang.T("That character is gone.")); CharListChanged?.Invoke(); return; }
            State = RoSession.Stage.CharSelected;
            if (WorldReady == null) { Notice?.Invoke($"{ch.Name} is ready."); return; }
            WorldReady.Invoke(ch);
        }

        /// <summary>The account id of this login (the map protocol's AID).</summary>
        public uint AccountId { get { var a = Conn?.Db.MyAccount.Iter().FirstOrDefault(); return a != null ? a.Id : 0u; } }

        /// <summary>Opens the map link for <see cref="RoSession.EnterWorldOnline"/>: what the game sends becomes <c>packet</c>
        /// reducer calls; closing it tells the server the link closed (<c>map_disconnect</c>, as a closed socket would).</summary>
        public void AttachLink(RoConnection link) { Link = link; State = RoSession.Stage.InGame; _packetsIn = 0; _packetsOut = 0; Debug.Log("[Stdb] map link attached"); }

        public void SendPacket(byte[] data)
        {
            if (Conn == null || !Conn.IsActive) { Debug.LogWarning("[Stdb] packet dropped: not connected"); return; }
            _packetsOut++;
            Conn.Reducers.Packet(new List<byte>(data));
        }

        public void CloseLink()
        {
            Debug.Log($"[Stdb] map link closed ({_packetsOut} packets sent, {_packetsIn} rows received)");
            Link = null;
            if (Conn != null && Conn.IsActive) { try { Conn.Reducers.MapDisconnect(); } catch (Exception e) { Debug.LogWarning("[Stdb] map_disconnect: " + e.Message); } }
            if (_loggedIn) State = RoSession.Stage.CharList;
        }

        /// <summary>After a scene reload for character select: the list is still here, show it again.</summary>
        public void ShowCharactersAgain()
        {
            if (!_loggedIn) return;
            State = RoSession.Stage.CharList;
            RebuildChars();
            CharListChanged?.Invoke();
            PinStateChanged?.Invoke(0);
        }

        private void Fail(string why)
        {
            Debug.LogWarning("[Stdb] " + why);
            State = RoSession.Stage.Failed; FailReason = why;
            Failed?.Invoke(why);
        }

        // ---------------------------------------------------------------- the character screen
        public void SelectCharacter(int slot)
        {
            var c = Characters.Find(x => x.Slot == slot);
            if (Conn == null) return;
            _selectRetries = 0; _selectRetryAt = -1f;
            // a slot without a character is asked for all the same, as the rAthena session does (CH_SELECT_CHAR carries the slot:
            // the char server refuses it, HC_REFUSE_ENTER 0): no character has id 0, so the module refuses in the same way
            Conn.Reducers.SelectCharacter(c != null ? c.CharId : 0u);
        }

        public void CreateCharacter(string name, int slot, int hairStyle, int hairColor, uint job, int sex = -1)
        {
            if (Conn == null) return;
            Conn.Reducers.CreateCharacter((byte)slot, name, (ushort)hairStyle, (ushort)hairColor, (ushort)job, (byte)(sex < 0 ? Sex : sex));
        }

        public void DeleteReserve(uint charId) => Conn?.Reducers.RequestDelete(charId);
        /// <summary>The birth date goes as the rAthena session sends it (CH_DELETE_CHAR3: six characters, or none for an account
        /// without a birth date, which every account made in game is): the module refuses a date that does not match, as
        /// rAthena does (server/src/character.rs confirm_delete; rA:char/char_clif.cpp:672-720).</summary>
        public void DeleteConfirm(uint charId, string birthdate)
        {
            string b = (birthdate ?? "").Trim();
            Conn?.Reducers.ConfirmDelete(charId, b.Length == 6 ? b : "");
        }
        public void DeleteCancel(uint charId) => Conn?.Reducers.CancelDelete(charId);

        // Loki Online has no PIN: the account password (Argon2id on the server) is the only secret.
        public void PinCheck(string pin) { }
        public void PinCreate(string pin) { }
        public void PinChange(string oldPin, string newPin) { }
        public void PinSkip() { PinState = 0; PinStateChanged?.Invoke(0); }

        // ---------------------------------------------------------------- rename, slot move, password (wave H)
        private string _checking = "";
        public void RenameCheck(uint charId, string newName)
        {
            if (Conn == null) return;
            if (!CanRename) { Notice?.Invoke(Lang.T("This server has no character renaming.")); return; }
            _checking = newName ?? ""; Conn.Reducers.RenameCheck(charId, _checking);
        }

        public void Rename(uint charId, string newName)
        {
            if (Conn == null) return;
            if (!CanRename) { Notice?.Invoke(Lang.T("This server has no character renaming.")); return; }
            Conn.Reducers.RenameCharacter(charId, newName ?? "");
        }

        public void MoveSlot(int fromSlot, int toSlot, int movesLeft)
        {
            if (Conn == null) return;
            if (!CanMoveSlot) { Notice?.Invoke(Lang.T("This server has no slot moves.")); return; }
            _movingTo = toSlot; Conn.Reducers.MoveCharacterSlot((byte)fromSlot, (byte)toSlot);
        }
        private int _movingTo = -1;

        /// <summary>rename_check: Ok = the name is free and allowed (HC_ACK_IS_VALID_CHARNAME 1), an error = it is not; the texts are
        /// the ones the rAthena session shows.</summary>
        private void OnRenameCheckResult(ReducerEventContext ctx, uint cid, string name)
        {
            if (NoticeOf(ctx, out string refused)) { RenameChecked?.Invoke(false, refused); return; }
            bool ok = !(ctx.Event.Status is Status.Failed);
            RenameChecked?.Invoke(ok, RoAccountSession.RenameCheckText(ok, _checking));
        }

        /// <summary>rename_character: an error is "#result text" with rAthena's HC_ACK_CHANGE_CHARNAME result
        /// (rA:char/char_clif.cpp:1520-1534); the number picks the client's own text.</summary>
        private void OnRenameResult(ReducerEventContext ctx, uint cid, string name)
        {
            if (ctx.Event.Status is Status.Failed(var why))
            {
                if (Coded(why, out _, out _, out _, out _)) { RenameResult?.Invoke(3, Shown(why)); return; } // not a rename result: the handshake's refusal
                int code = RenameCode(why);
                RenameResult?.Invoke(code, code > 0 && code != 3 ? RoSession.RenameText(code) : Tr(why));
                return;
            }
            if (NoticeOf(ctx, out string refused)) { RenameResult?.Invoke(3, refused); return; }
            RebuildChars();
            RenameResult?.Invoke(0, RoSession.RenameText(0));
        }

        /// <summary>"#4 Another user is using…" → 4; a refusal without a number (not logged in, not this account's character) → 3.</summary>
        public static int RenameCode(string why)
        {
            if (string.IsNullOrEmpty(why) || why[0] != '#') return 3;
            int sp = why.IndexOf(' ');
            return int.TryParse(sp > 1 ? why.Substring(1, sp - 1) : why.Substring(1), out int code) && code > 0 ? code : 3;
        }

        /// <summary>move_character_slot: moved or not (HC_ACK_CHANGE_CHARACTER_SLOT reason 0 / 1) and the mover's moves left.</summary>
        private void OnMoveSlotResult(ReducerEventContext ctx, byte from, byte to)
        {
            bool ok = !(ctx.Event.Status is Status.Failed) && !NoticeOf(ctx, out _);
            RebuildChars();
            var mover = Characters.Find(c => c.Slot == (ok ? to : from));
            if (!ok && ctx.Event.Status is Status.Failed(var why)) Debug.Log("[Stdb] slot move refused: " + why);
            SlotMoved?.Invoke(ok, mover != null ? mover.SlotChangeCount : 0);
        }

        /// <summary>Changes the logged-in account's password (the module's change_password; rAthena has no such packet). Both travel
        /// hashed like a login's. Every other PC's saved login to the account ends; this one stays.</summary>
        public void ChangePassword(string oldPass, string newPass)
        {
            var acc = Conn?.Db.MyAccount.Iter().FirstOrDefault();
            if (acc == null || !_loggedIn) { PasswordChanged?.Invoke(false, Lang.T("You are not logged in.")); return; }
            if (!CanChangePassword) { PasswordChanged?.Invoke(false, Lang.T("This server can't change passwords.")); return; }
            if ((newPass ?? "").Length < PasswordMin || newPass.Length > PasswordMax) { PasswordChanged?.Invoke(false, Lang.F("Passwords are {0} to {1} characters long.", PasswordMin, PasswordMax)); return; }
            Conn.Reducers.ChangePassword(ClientHash(acc.Username, oldPass ?? ""), ClientHash(acc.Username, newPass));
        }

        private void OnChangePasswordResult(ReducerEventContext ctx, string oldHash, string newHash)
        {
            if (ctx.Event.Status is Status.Failed(var why)) { PasswordChanged?.Invoke(false, Shown(why)); return; }
            if (NoticeOf(ctx, out string refused)) { PasswordChanged?.Invoke(false, refused); return; }
            PasswordChanged?.Invoke(true, Lang.T("Password changed. Other PCs have to log in again with the new one."));
        }

        /// <summary>The map server refused the entry after the select (SC_NOTIFY_BAN 8 while the account's other character is still
        /// leaving, a rejected connect packet): the map link is gone, the login is not. Back to the character list with the reason
        /// (LokiGame's Session.OnFailed hook, before the world was reached).</summary>
        public void MapEntryRefused(string why)
        {
            if (!_loggedIn) return;
            Debug.Log("[Stdb] map entry refused: " + why);
            var link = Link;
            if (link != null) { try { link.Close(); } catch (Exception e) { Debug.LogWarning("[Stdb] closing the map link: " + e.Message); } }
            Link = null; State = RoSession.Stage.CharList;
            SelectRefused?.Invoke(why);
        }
        public bool HasSavedLogin(string host, int port, string database) => HasSavedIdentity(host, port, database);
        /// <summary>Client wave 4: never asked for (no <see cref="MapChoice"/> on the module).</summary>
        public void SelectAccessibleMap(int slot, int mapIndex) => Notice?.Invoke(Lang.T("Loki Online has no map choice: select the character again."));
    }
}
