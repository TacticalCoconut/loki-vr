using System;
using System.Collections.Generic;
using System.IO;
using Loki.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// The screens before the map: server + account login (hashed password, or plain registration with rAthena's _M/_F rule),
    /// the PIN keypad (HC_SECOND_PASSWD_LOGIN states), character select (play / delete with its timer / cancel deletion),
    /// character creation with a live preview (LoginPanel.Create.cs), rename / slot move, the server list (add / edit / hide /
    /// remove, kept per user: LoginPanel.Servers.cs), the data folder (LoginPanel.Data.cs) and the way back after a lost server
    /// (LoginPanel.Reconnect.cs). One floating panel, one screen at a time; text goes through the <see cref="VrKeyboard"/> or a
    /// physical keyboard. It knows the server only through <see cref="IAccountSession"/> (rAthena's or Loki Online's): what one
    /// of them can't do is a capability flag on the interface, never a cast.
    /// </summary>
    public sealed partial class LoginPanel : MonoBehaviour
    {
        public enum Screen { Login, Register, Connecting, Pin, Chars, Create, Manage, Servers, ServerEdit, MapChoice, Password }
        private const float W = 1100, H = 900;
        private RectTransform _rt; private Camera _head; private VrKeyboard _kb;
        /// <summary>The session of the chosen server: rAthena's, or Loki Online's (SpacetimeDB).</summary>
        private IAccountSession _s, _ro, _online;
        private readonly Dictionary<Screen, RectTransform> _pages = new Dictionary<Screen, RectTransform>();
        private Screen _screen = Screen.Login;
        private Text _title, _status; private float _statusUntil;
        public Screen Current => _screen;
        public bool Visible => gameObject.activeSelf;
        /// <summary>Servers the player can pick: loki.json `servers` (or loginHost/loginPort) and the ones added in game.</summary>
        public sealed class ServerEntry
        {
            public string name = "Local", host = "127.0.0.1"; public int port = 6900; public string kind = "rathena", database = "loki";
            /// <summary>Loki Online: "ws" (plain) or "wss" (TLS); see <see cref="ServerAddress.Split"/>.</summary>
            public string scheme = "";
            /// <summary>The headset pose relay's port (w3presence; Loki Host: 7903); 0 = the login port + 3.</summary>
            public int presencePort;
            /// <summary>Added on the login screen (the per-user file); the others come with the game (loki.json).</summary>
            public bool userAdded;
            /// <summary>Client wave 4: a player's copy of a server that comes with Loki replaces it (the shipped entry's key); "" otherwise.</summary>
            public string replaces = "";
            public bool IsStdb => kind == "spacetimedb";
            /// <summary>The host as the session wants it: Loki Online gets "wss://host" for TLS.</summary>
            public string LoginHost { get { if (!IsStdb) return host; var (s, h) = ServerAddress.Split(host, scheme); return s + "://" + h; } }
            /// <summary>"host:port" (with the database for Loki Online), for the screens.</summary>
            public string Address => IsStdb ? $"{LoginHost}:{port}/{(string.IsNullOrEmpty(database) ? "loki" : database)}" : $"{host}:{port}";
            public string KindName => IsStdb ? "Loki Online" : "rAthena";
        }
        private readonly List<ServerEntry> _servers = new List<ServerEntry>(); private int _server;
        // login
        private VrTextField _user, _pass; private UiButton _remember; private bool _rememberOn = true; private UiButton _kbButton; private Text _regHint, _serverInfo;
        // register
        private VrTextField _regUser, _regPass, _regEmail; private Text _regServerInfo, _regPageHint;
        private readonly UiButton[] _quick = new UiButton[3]; private readonly int[] _quickIndex = new int[3];
        /// <summary>Remember the account name and server between launches (smokes switch it off: PlayerPrefs are shared with real play).</summary>
        public bool PersistChoices = true;
        // pin
        private enum PinMode { None, Check, Create, Change }
        private PinMode _pinMode; private string _pin = "", _pinFirst = "", _pinOld = ""; private Text _pinDots, _pinText; private UiButton _pinSkip; private int _pinWrong;
        // chars
        private readonly List<(RectTransform row, Text text, UiButton play, UiButton del)> _charRows = new List<(RectTransform, Text, UiButton, UiButton)>();
        private VrTextField _birth; private Text _charInfo;
        private static Dictionary<int, string> _jobNames;
        /// <summary>Every text field of the panel (focus, blur and the physical keyboard go through this list).</summary>
        private readonly List<VrTextField> _fields = new List<VrTextField>();
        /// <summary>The game's way to know a character was chosen (the session does the rest).</summary>
        public event Action<CharInfo> CharacterChosen;
        public event Action QuitRequested;

        /// <param name="ro">rAthena's session (<see cref="RoAccountSession"/> over the game's RoSession).</param>
        /// <param name="online">Loki Online's session (SpacetimeDB), or null.</param>
        public static LoginPanel Create(Transform parent, Camera head, VrKeyboard kb, IAccountSession ro, IAccountSession online, IEnumerable<ServerEntry> servers)
        {
            var rt = UiKit.Panel("LoginPanel", parent, new Vector2(W, H), 0.0007f);
            var p = rt.gameObject.AddComponent<LoginPanel>();
            p._rt = rt; p._head = head; p._kb = kb; p._ro = ro; p._online = online; p._s = p._ro;
            if (servers != null) foreach (var e in servers) if (e != null && (!e.IsStdb || online != null)) p._servers.Add(e);
            if (p._servers.Count == 0) p._servers.Add(new ServerEntry());
            p.Build();
            p.Hook();
            p.Show(Screen.Login);
            if (!Net.Stdb.StdbSession.IsAutomatedRun())
            {
                Net.ClientUpdater.CheckOnStartup(msg =>
                {
                    if (p == null) return;
                    p.StopReconnect(msg);
                    p._s?.Drop();
                    p.Show(Screen.Login);
                    if (p._status != null) p._status.text = msg;
                });
            }
            return p;
        }

        /// <summary>The session that talks to a server entry.</summary>
        private IAccountSession SessionFor(ServerEntry sv) => sv != null && sv.IsStdb && _online != null ? _online : _ro;
        private ServerEntry Selected => _server >= 0 && _server < _servers.Count ? _servers[_server] : null;

        private VrTextField Field(RectTransform parent, Vector2 pos, Vector2 size, int font)
        {
            var f = VrTextField.Create(parent, pos, size, font);
            f.OnFocus = FocusField; _fields.Add(f);
            return f;
        }

        // ---------------------------------------------------------------- build
        private void Build()
        {
            _title = UiKit.TitlePlate(_rt, Lang.T("Loki — Ragnarok Online in VR"), 40, W - 160).Text; // client wave 21 (w21kit): the title on the gold plate on the frame's top edge (the same 40 px letters)
            _status = UiKit.Label(_rt, "", new Vector2(0, -H / 2 + 40), new Vector2(W - 60, 60), 24, TextAnchor.MiddleCenter, new Color(1f, 0.8f, 0.5f));
            _status.resizeTextForBestFit = true; _status.resizeTextMinSize = 14; _status.resizeTextMaxSize = 24; // long server texts shrink instead of being cut
            foreach (Screen sc in Enum.GetValues(typeof(Screen)))
            {
                var page = UiKit.Box(_rt, "page:" + sc, new Vector2(0, -20), new Vector2(W - 40, H - 180), new Color(0, 0, 0, 0));
                _pages[sc] = page;
            }
            BuildLogin(_pages[Screen.Login]);
            BuildRegister(_pages[Screen.Register]);
            _connectingText = UiKit.Label(_pages[Screen.Connecting], Lang.T("Connecting…"), new Vector2(0, 60), new Vector2(W - 120, 200), 30, TextAnchor.MiddleCenter);
            _connectingCancel = UiKit.Button(_pages[Screen.Connecting], Lang.T("Cancel"), new Vector2(0, -120), new Vector2(240, 64), CancelConnecting, 26);
            BuildPin(_pages[Screen.Pin]);
            BuildChars(_pages[Screen.Chars]);
            BuildCreate(_pages[Screen.Create]);
            BuildManage(_pages[Screen.Manage]);
            BuildServers(_pages[Screen.Servers]);
            BuildServerEdit(_pages[Screen.ServerEdit]);
            BuildMapChoice(_pages[Screen.MapChoice]);
            BuildPassword(_pages[Screen.Password]); // wave H (haccount): the account's password (LoginPanel.Password.cs)
            if (!Lang.EnglishOnly) BuildW4Lang(); // client wave 4b (w4lang): the language button (LoginPanel.W4Lang.cs); wave 28 (wtextc): none while English only
            BuildW5Ui(); // client wave 5 (w5ui): a picture of each character in its row (LoginPanel.W5Ui.cs)
        }

        private Text _connectingText; private UiButton _connectingCancel;
        /// <summary>Cancel / Stop: the connection goes, the login does not (wave I: Reset logged a Loki Online account out when the
        /// button was pressed after the login, e.g. on "Entering the world…").</summary>
        private void CancelConnecting() { StopReconnect("Stopped."); _s.Drop(); Show(Screen.Login); }

        private Text _serverLabel; private UiButton _serversBtn;
        private static bool ShowServerPicker => true;

        private void BuildLogin(RectTransform p)
        {
            float top = (H - 180) / 2;
            _serverLabel = UiKit.Label(p, Lang.T("Server"), new Vector2(-W / 2 + 140, top - 40), new Vector2(200, 40), 26, TextAnchor.MiddleLeft, UiKit.Dim);
            _serverInfo = UiKit.Label(p, "", new Vector2(110, top - 40), new Vector2(W - 380, 40), 20, TextAnchor.MiddleLeft, UiKit.Dim);
            // three quick picks (the chosen server is always one of them) and the server list
            for (int i = 0; i < _quick.Length; i++)
            {
                int k = i;
                _quick[i] = UiKit.Button(p, "", new Vector2(-W / 2 + 140 + 230 * i + 115, top - 95), new Vector2(220, 56), () => { if (_quickIndex[k] >= 0) { _server = _quickIndex[k]; SaveServerChoice(); RefreshServers(); } }, 22);
                var qt = _quick[i].Text; qt.resizeTextForBestFit = true; qt.resizeTextMinSize = 12; qt.resizeTextMaxSize = 22; // long server names shrink instead of being cut
            }
            _serversBtn = UiKit.Button(p, Lang.T("Servers…"), new Vector2(-W / 2 + 140 + 230 * 3 + 115, top - 95), new Vector2(220, 56), () => Show(Screen.Servers), 22);
            UiKit.Label(p, Lang.T("Account"), new Vector2(-W / 2 + 140, top - 175), new Vector2(200, 40), 26, TextAnchor.MiddleLeft, UiKit.Dim);
            _user = Field(p, new Vector2(60, top - 175), new Vector2(W - 380, 64), 28);
            _user.Placeholder = Lang.T("account name (4–23 characters)"); _user.Allow = c => c > ' ' && c < 0x7f;
            UiKit.Label(p, Lang.T("Password"), new Vector2(-W / 2 + 140, top - 255), new Vector2(200, 40), 26, TextAnchor.MiddleLeft, UiKit.Dim);
            _pass = Field(p, new Vector2(60, top - 255), new Vector2(W - 380, 64), 28);
            _pass.Masked = true; _pass.Placeholder = Lang.T("password"); _pass.OnSubmit = DoLogin; _pass.MaxLength = 32; // PASSWD_LENGTH 32 + 1 (rA:common/mmo.hpp:155)
            _user.OnSubmit = () => FocusField(_pass);
            _remember = UiKit.Button(p, "", new Vector2(-W / 2 + 260, top - 335), new Vector2(400, 50), () => { _rememberOn = !_rememberOn; RefreshLogin(); }, 22);
            _kbButton = UiKit.Button(p, Lang.T("Keyboard"), new Vector2(W / 2 - 200, top - 335), new Vector2(240, 50), () => { if (_kb.Visible) _kb.Hide(); else { FocusField(_user.Focused ? _user : _pass.Focused ? _pass : (_user.Text.Length > 0 && _pass.Text.Length == 0 ? _pass : _user)); } }, 22);
            UiKit.Button(p, Lang.T("Log in"), new Vector2(0, top - 430), new Vector2(W - 400, 74), DoLogin, 30);
            // a server that keeps this PC's login (Loki Online) logs in again without the password
            _resumeBtn = UiKit.Button(p, Lang.T("Continue as before"), new Vector2(70, top - 335), new Vector2(290, 50), DoResume, 22);
            _regHint = UiKit.Label(p, "", new Vector2(0, top - 500), new Vector2(W - 200, 50), 19, TextAnchor.MiddleCenter, UiKit.Dim);
            UiKit.Button(p, Lang.T("Create an Account"), new Vector2(0, top - 560), new Vector2(W - 400, 60), OpenRegister, 26);
            UiKit.Button(p, Lang.T("Quit"), new Vector2(0, -top + 40), new Vector2(240, 56), () => QuitRequested?.Invoke(), 24);
            BuildDataButton(p, top); // client wave 4: the data folder from the login screen (LoginPanel.Data.cs)
            BuildW7Data(p, top); // client wave 7 (w7data): the install's patch and the start's notices (LoginPanel.W7Data.cs)
            try
            {
                _user.SetText(PlayerPrefs.GetString("loki.account", "")); _rememberOn = PlayerPrefs.GetInt("loki.remember", 1) == 1;
                if (!ShowServerPicker)
                {
                    _server = 0;
                }
                else
                {
                    string key = PlayerPrefs.GetString("loki.serverKeyV2", "");
                    int byKey = key.Length > 0 ? _servers.FindIndex(e => ServerKey(e) == key) : -1;
                    int stdb = _servers.FindIndex(e => e.IsStdb);
                    _server = byKey >= 0 ? byKey : stdb >= 0 ? stdb : Mathf.Clamp(PlayerPrefs.GetInt("loki.server", 0), 0, _servers.Count - 1);
                }
            }
            catch { }
            RefreshLogin(); RefreshServers();
        }
        private UiButton _regM, _regF;

        private void BuildRegister(RectTransform p)
        {
            float top = (H - 180) / 2;
            _regServerInfo = UiKit.Label(p, "", new Vector2(0, top - 40), new Vector2(W - 120, 40), 21, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.45f));
            UiKit.Label(p, Lang.T("Account"), new Vector2(-W / 2 + 140, top - 115), new Vector2(200, 40), 26, TextAnchor.MiddleLeft, UiKit.Dim);
            _regUser = Field(p, new Vector2(60, top - 115), new Vector2(W - 380, 64), 28);
            _regUser.Placeholder = Lang.T("new account name (4–23 characters)"); _regUser.Allow = c => c > ' ' && c < 0x7f;
            UiKit.Label(p, Lang.T("Password"), new Vector2(-W / 2 + 140, top - 195), new Vector2(200, 40), 26, TextAnchor.MiddleLeft, UiKit.Dim);
            _regPass = Field(p, new Vector2(60, top - 195), new Vector2(W - 380, 64), 28);
            _regPass.Masked = true; _regPass.Placeholder = Lang.T("new password (4–23 characters)"); _regPass.MaxLength = 32;
            UiKit.Label(p, Lang.T("Email"), new Vector2(-W / 2 + 140, top - 275), new Vector2(200, 40), 26, TextAnchor.MiddleLeft, UiKit.Dim);
            _regEmail = Field(p, new Vector2(60, top - 275), new Vector2(W - 380, 64), 26);
            _regEmail.Placeholder = Lang.T("email@example.com"); _regEmail.MaxLength = 64; _regEmail.Allow = c => c > ' ' && c < 0x7f;
            _regUser.OnSubmit = () => FocusField(_regPass);
            _regPass.OnSubmit = () => FocusField(_regEmail);
            _regEmail.OnSubmit = () => SubmitRegister('M');
            UiKit.Button(p, Lang.T("Keyboard"), new Vector2(W / 2 - 200, top - 350), new Vector2(240, 50), () =>
            {
                if (_kb.Visible) _kb.Hide();
                else FocusField(_regUser.Focused ? _regUser : _regPass.Focused ? _regPass : _regEmail.Focused ? _regEmail : _regUser);
            }, 22);
            _regPageHint = UiKit.Label(p, "", new Vector2(0, top - 420), new Vector2(W - 160, 56), 19, TextAnchor.MiddleCenter, UiKit.Dim);
            _regM = UiKit.Button(p, Lang.T("Create account"), new Vector2(0, top - 495), new Vector2(W - 400, 64), () => SubmitRegister('M'), 26);
            _regF = UiKit.Button(p, Lang.T("Create account"), new Vector2(0, top - 495), new Vector2(W - 400, 64), () => SubmitRegister('F'), 26);
            _regF.gameObject.SetActive(false);
            UiKit.Button(p, Lang.T("Back to login"), new Vector2(0, -top + 40), new Vector2(320, 56), () => Show(Screen.Login), 24);
        }

        private void OpenRegister()
        {
            _regUser.SetText(_user.Text.Trim());
            _regPass.SetText(_pass.Text);
            try { _regEmail.SetText(PlayerPrefs.GetString("loki.email", "")); } catch { _regEmail.SetText(""); }
            _regAsk = null;
            Show(Screen.Register);
            FocusField(_regUser.Text.Length == 0 ? _regUser : _regPass.Text.Length == 0 ? _regPass : _regEmail);
        }

        private void RefreshRegister()
        {
            var sv = Selected; var sess = SessionFor(sv);
            if (_regUser != null) _regUser.Placeholder = Lang.F("new account name ({0}–{1} letters or digits)", sess.AccountNameMin, sess.AccountNameMax);
            if (_regServerInfo != null) _regServerInfo.text = sv == null ? "" : sv.IsStdb ? Lang.F("Server: {0}  ·  {1}", Esc(sv.name), sv.KindName) : Lang.F("Server: {0}  ·  {1}  ·  {2}", Esc(sv.name), Esc(sv.Address), sv.KindName);
            if (_regPageHint != null && _regHint != null) _regPageHint.text = _regHint.text;
            RefreshRegisterButtons();
        }

        private void SubmitRegister(char sex)
        {
            string u = (_regUser.Text ?? "").Trim(), p = _regPass.Text ?? "", em = (_regEmail.Text ?? "").Trim();
            if (em.Length > 0 && (em.IndexOf('@') <= 0 || em.IndexOf('.', em.IndexOf('@')) < 0))
            {
                Status(Lang.T("Please enter a valid email address (or leave it blank)."));
                return;
            }
            if (PersistChoices && em.Length > 0)
            {
                try { PlayerPrefs.SetString("loki.email", em); PlayerPrefs.Save(); } catch { }
            }
            _user.SetText(u);
            _pass.SetText(p);
            DoRegister(sex);
        }

        private static string ServerKey(ServerEntry e) => $"{e.kind}|{e.name}|{e.Address}";

        private void BuildPin(RectTransform p)
        {
            float top = (H - 180) / 2;
            _pinText = UiKit.Label(p, "", new Vector2(0, top - 40), new Vector2(W - 100, 60), 28, TextAnchor.MiddleCenter);
            _pinText.resizeTextForBestFit = true; _pinText.resizeTextMinSize = 16; _pinText.resizeTextMaxSize = 28;
            _pinDots = UiKit.Label(p, "", new Vector2(0, top - 120), new Vector2(400, 80), 56, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.45f));
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "Clear", "0", "Back" };
            for (int i = 0; i < keys.Length; i++)
            {
                string k = keys[i]; int col = i % 3, row = i / 3;
                UiKit.Button(p, k == "Clear" ? Lang.T("Clear") : k == "Back" ? Lang.T("~key~Back") : k, new Vector2(-130 + col * 130, top - 230 - row * 90), new Vector2(120, 80), () => PinKey(k), 32); // w4lang: PinKey gets the English key
            }
            UiKit.Button(p, "OK", new Vector2(0, top - 610), new Vector2(400, 66), PinOk, 28);
            _pinSkip = UiKit.Button(p, Lang.T("Skip for now"), new Vector2(-W / 2 + 200, -top + 40), new Vector2(320, 56), () => { _s.PinSkip(); }, 22);
            UiKit.Button(p, Lang.T("Back to login"), new Vector2(W / 2 - 200, -top + 40), new Vector2(320, 56), Logout, 22);
        }

        private void BuildChars(RectTransform p)
        {
            float top = (H - 180) / 2;
            _charInfo = UiKit.Label(p, "", new Vector2(0, top - 30), new Vector2(W - 400, 40), 24, TextAnchor.MiddleCenter, UiKit.Dim);
            // rAthena lists every character in one HC_ACCEPT_ENTER (MAX_CHARS = 15 slots, chclif_mmo_send006b): 6 rows a page
            _charPrev = UiKit.Button(p, Lang.T("< Prev"), new Vector2(-W / 2 + 130, top - 30), new Vector2(170, 48), () => { _charPage = Mathf.Max(0, _charPage - 1); RefreshChars(); }, 22);
            _charNext = UiKit.Button(p, Lang.T("Next >"), new Vector2(W / 2 - 130, top - 30), new Vector2(170, 48), () => { _charPage++; RefreshChars(); }, 22);
            for (int i = 0; i < CharsPerPage; i++)
            {
                float y = top - 90 - i * 84;
                var row = UiKit.Box(p, "char", new Vector2(0, y), new Vector2(W - 120, 76), new Color(1, 1, 1, 0.05f));
                var text = UiKit.Label(row, "", new Vector2(-170, 0), new Vector2(W - 580, 72), 23, TextAnchor.MiddleLeft);
                text.resizeTextForBestFit = true; text.resizeTextMinSize = 14; text.resizeTextMaxSize = 23;
                int slot = i;
                var more = UiKit.Button(row, Lang.T("More"), new Vector2((W - 120) / 2 - 342, 0), new Vector2(90, 60), () => OpenManage(slot), 20);
                var play = UiKit.Button(row, Lang.T("Play"), new Vector2((W - 120) / 2 - 220, 0), new Vector2(150, 60), () => Play(slot), 26);
                var del = UiKit.Button(row, Lang.T("Delete"), new Vector2((W - 120) / 2 - 75, 0), new Vector2(120, 60), () => Delete(slot), 22);
                del.Text.resizeTextForBestFit = true; del.Text.resizeTextMinSize = 12; del.Text.resizeTextMaxSize = 22;
                _charRows.Add((row, text, play, del));
                _moreButtons.Add(more);
            }
            UiKit.Label(p, Lang.T("Birth date to delete for good (YYMMDD) — leave it empty when your account has none (accounts made in game have none):"), new Vector2(-140, -top + 110), new Vector2(W - 500, 56), 17, TextAnchor.MiddleLeft, UiKit.Dim);
            _birth = Field(p, new Vector2(W / 2 - 200, -top + 110), new Vector2(200, 52), 24);
            _birth.SetText(""); _birth.MaxLength = 6; _birth.Allow = char.IsDigit; _birth.Placeholder = Lang.T("~birth~none");
            UiKit.Button(p, Lang.T("New character"), new Vector2(-W / 2 + 230, -top + 40), new Vector2(380, 60), OpenCreate, 24);
            UiKit.Button(p, Lang.T("Log out"), new Vector2(W / 2 - 200, -top + 40), new Vector2(320, 60), Logout, 24);
            BuildPasswordButton(p, top); // wave H: "Password…" where the server can change it (LoginPanel.Password.cs)
        }

        // ---------------------------------------------------------------- manage: rename / move slot (rAthena's "Add-Ons")
        private const int CharsPerPage = 6;
        private int _charPage; private UiButton _charPrev, _charNext;
        private readonly List<UiButton> _moreButtons = new List<UiButton>();
        private CharInfo _manage; private Text _manageInfo, _renameNote, _moveNote; private VrTextField _newName; private UiButton _renameCheck, _renameDo;
        private readonly List<UiButton> _slotButtons = new List<UiButton>();
        private bool _nameChecked; private string _checkedName = "";

        private void BuildManage(RectTransform p)
        {
            float top = (H - 180) / 2;
            _manageInfo = UiKit.Label(p, "", new Vector2(0, top - 30), new Vector2(W - 100, 44), 26, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.45f));
            UiKit.Label(p, Lang.T("Rename"), new Vector2(-W / 2 + 120, top - 90), new Vector2(180, 40), 26, TextAnchor.MiddleLeft, UiKit.Dim);
            _newName = Field(p, new Vector2(40, top - 90), new Vector2(W - 440, 60), 26);
            _newName.Placeholder = Lang.T("new name (4–23 characters)"); _newName.OnSubmit = RenameCheck;
            _renameCheck = UiKit.Button(p, Lang.T("Check"), new Vector2(W / 2 - 160, top - 90), new Vector2(150, 58), RenameCheck, 22);
            _renameNote = UiKit.Label(p, "", new Vector2(-60, top - 155), new Vector2(W - 300, 50), 21, TextAnchor.MiddleLeft, UiKit.Dim);
            _renameDo = UiKit.Button(p, Lang.T("Rename"), new Vector2(W / 2 - 160, top - 155), new Vector2(150, 58), RenameDo, 22);
            UiKit.Label(p, Lang.T("Move to slot"), new Vector2(-W / 2 + 140, top - 230), new Vector2(220, 40), 26, TextAnchor.MiddleLeft, UiKit.Dim);
            _moveNote = UiKit.Label(p, "", new Vector2(80, top - 230), new Vector2(W - 380, 40), 21, TextAnchor.MiddleLeft, UiKit.Dim);
            for (int i = 0; i < 15; i++)
            {
                int to = i; int col = i % 5, row = i / 5;
                var b = UiKit.Button(p, (i + 1).ToString(), new Vector2(-W / 2 + 160 + col * 190, top - 300 - row * 76), new Vector2(180, 66), () => MoveTo(to), 20);
                _slotButtons.Add(b);
            }
            UiKit.Button(p, Lang.T("Back"), new Vector2(0, -top + 40), new Vector2(240, 56), () => Show(Screen.Chars), 24);
        }

        private void OpenManage(int row)
        {
            var c = CharAt(row); if (c == null) return;
            _manage = c; _nameChecked = false; _checkedName = ""; _newName.SetText("");
            Show(Screen.Manage);
        }

        private void RefreshManage()
        {
            var c = _manage; if (c == null) return;
            _manageInfo.text = Lang.F("{0}  <color=#9aa3b5>slot {1} · Lv {2} {3}</color>", Esc(c.Name), c.Slot + 1, c.BaseLevel, JobName(c.Job, c.Sex));
            // CHARACTER_INFO chr_name_changeCnt: 1 while the character may be renamed (char.cpp char_mmo_char_tobuf: rename > 0)
            bool canRename = _s.CanRename && c.NameChangeCount > 0;
            _newName.gameObject.SetActive(canRename); _renameCheck.gameObject.SetActive(canRename);
            _renameDo.gameObject.SetActive(canRename && _nameChecked && _checkedName == _newName.Text.Trim());
            if (!canRename) _renameNote.text = !_s.CanRename ? Lang.T("This server has no character renaming.") : Lang.T("This character has no rename available (a GM or an item grants one).");
            else if (!_nameChecked && _renameNote.text.Length == 0) _renameNote.text = Lang.T("Type the new name, then Check.");
            // chr_slot_changeCnt: moves left (1 = unlimited when char_moves_unlimited), 0 = moving is off or used up
            bool canMove = _s.CanMoveSlot && c.SlotChangeCount > 0;
            int usable = _s.CanMoveSlot ? Mathf.Clamp(_s.UsableSlots, 0, 15) : 0;
            _moveNote.text = !canMove ? (!_s.CanMoveSlot ? Lang.T("This server has no slot moves.") : Lang.T("No slot moves left for this character.")) : Lang.F("{0} move(s) left; a used slot swaps the two characters.", c.SlotChangeCount);
            for (int i = 0; i < _slotButtons.Count; i++)
            {
                bool show = canMove && i < usable; var b = _slotButtons[i];
                b.gameObject.SetActive(show); if (!show) continue;
                var occupant = _s.Characters.Find(x => x.Slot == i);
                b.SetLabel(occupant == null ? Lang.F("{0}: empty", i + 1) : $"{i + 1}: {Esc(occupant.Name)}");
                bool here = i == c.Slot; b.Interactable = !here; b.Normal = here ? new Color(0.45f, 0.35f, 0.15f) : UiKit.ButtonBg; b.SetHover(false);
            }
        }

        private void RenameCheck()
        {
            string n = NormalizeName(_newName.Text);
            if (_manage == null || !_s.CanRename) return;
            if (n.Length < 4 || n.Length > 23) { Status(Lang.T("Names are 4 to 23 characters.")); return; }
            _nameChecked = false; _checkedName = n; _s.RenameCheck(_manage.CharId, n);
            _renameNote.text = Lang.T("Checking…"); Status(Lang.F("Checking '{0}'…", n));
        }

        private void RenameDo()
        {
            string n = NormalizeName(_newName.Text);
            if (_manage == null || !_s.CanRename || !_nameChecked || n != _checkedName) return;
            _s.Rename(_manage.CharId, n); Status(Lang.F("Renaming {0} to {1}…", _manage.Name, n));
        }

        private void MoveTo(int to)
        {
            var c = _manage;
            if (c == null || !_s.CanMoveSlot || to == c.Slot) return;
            _s.MoveSlot(c.Slot, to, c.SlotChangeCount); Status(Lang.F("Moving {0} to slot {1}…", c.Name, to + 1));
        }

        // smoke hooks for the character screen
        public int CharPage => _charPage; public int CharPages => Mathf.Max(1, (_s.Characters.Count + CharsPerPage - 1) / CharsPerPage);
        public string CharRowText(int row) => row >= 0 && row < _charRows.Count && _charRows[row].row.gameObject.activeSelf ? _charRows[row].text.text : null;
        public bool RenameConfirmShown => _renameDo.gameObject.activeInHierarchy;
        public string RenameNote => _renameNote != null ? _renameNote.text : "";
        /// <summary>The "More" button of the character's row is offered (rename or move possible on this server).</summary>
        public bool MoreShownFor(string name) { var l = SortedChars(); int i = l.FindIndex(c => c.Name == name); if (i < 0) return false; _charPage = i / CharsPerPage; RefreshChars(); return _moreButtons[i % CharsPerPage].gameObject.activeSelf; }

        // ---------------------------------------------------------------- session events
        private int _altLocalPortTried;
        private void Hook()
        {
            foreach (var sess in new[] { _ro, _online })
            {
                if (sess == null) continue;
                var me = sess; // each handler only acts while its session is the chosen one
                me.LoginRefused += (err, text) => { if (_s != me || _inWorld) return; if (ReconnectRefused(text)) return; Status(text); Show(Screen.Login); };
                // in the world a failure is the game's to show (LokiGame.ConnectionLost goes back to a fresh login screen): no panel over the map
                me.Failed += why =>
                {
                    if (_s != me || _inWorld) return;
                    if (!_reachedChars && !_reconnecting && Selected != null && !Selected.IsStdb && IsThisPc(Selected)
                        && (Selected.port == 6900 || Selected.port == 7900) && _altLocalPortTried != Selected.port
                        && why != null && why.IndexOf("refused", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        int oldPort = Selected.port;
                        _altLocalPortTried = oldPort;
                        Selected.port = oldPort == 7900 ? 6900 : 7900;
                        Selected.presencePort = Selected.port == 7900 ? 7903 : 0;
                        RefreshServers();
                        Status(Lang.F("Port {0} wasn't open — trying local port {1}…", oldPort, Selected.port));
                        me.Login(Selected.LoginHost, Selected.port, Selected.database, _user.Text.Trim(), _pass.Text);
                        return;
                    }
                    if (ReconnectFailed(why)) return;
                    if (_screen != Screen.Login) { Status(why); Show(Screen.Login); }
                };
                me.PinStateChanged += state => { if (_s == me && !_inWorld) { ReconnectReached(); OnPin(state); } };
                // the PIN state (0x08b9) follows the character list: wait for it before offering Play (a select before it gets the client kicked)
                me.CharListChanged += () =>
                {
                    if (_s != me || _inWorld) return;
                    _charsAt = Time.time;
                    // client wave 4: a list sent while the player is on the create or manage screen (a refused creation re-sends it) doesn't pull
                    // them away; a successful creation does (the new name is in it)
                    bool created = _createPending != null && _s.Characters.Exists(c => c.Name == _createPending);
                    if (created) _createPending = null;
                    if ((_screen == Screen.Create || _screen == Screen.Manage || _screen == Screen.MapChoice) && !created) { RefreshManage(); return; }
                    if (_s.PinState >= 0 && !_s.PinPending) { ReconnectReached(); Show(Screen.Chars); } else RefreshChars();
                };
                me.CreateRefused += text => { if (_s != me || _inWorld) return; _createPending = null; Status(text); Show(Screen.Create); };
                me.DeleteResult += (cid, due, msg) => { if (_s != me || _inWorld) return; _deleteAsk = 0; if (!string.IsNullOrEmpty(msg)) Status(msg); RefreshChars(); };
                me.EnteredMap += () => { if (_s == me) { _inWorld = true; HidePreview(); Hide(); } };
                me.Notice += text =>
                {
                    if (_s != me || _inWorld) return;
                    if (ReconnectNotice(text, me.State == RoSession.Stage.Idle)) return; // wave I: a reconnect by the saved login that the server did not take
                    Status(text); if (_screen == Screen.Connecting && me.State == RoSession.Stage.Idle) Show(Screen.Login); // a resume without a saved login
                };
                // rename / slot move: on success the server sends the whole list again (rAthena: chclif_mmo_char_send)
                me.RenameChecked += (ok, text) => { if (_s != me || _inWorld) return; _nameChecked = ok; _renameNote.text = text; Status(text); RefreshManage(); };
                me.RenameResult += (res, text) => { if (_s != me || _inWorld) return; Status(text); if (res == 0) { _nameChecked = false; Show(Screen.Chars); } };
                me.SlotMoved += (ok, moves) => { if (_s != me || _inWorld) return; Status(ok ? Lang.F("Moved. {0} move(s) left.", moves) : Lang.T("The slot move was refused.")); if (ok) Show(Screen.Chars); };
                me.PasswordChanged += (ok, text) => { if (_s == me && !_inWorld) OnPasswordChanged(ok, text); };
                // client wave 4: a refused select keeps the list; a character whose map has no server may go elsewhere
                me.SelectRefused += text => { if (_s != me || _inWorld) return; Status(text); Show(Screen.Chars); };
                me.MapChoice += maps => { if (_s == me && !_inWorld) OnMapChoice(maps); };
            }
        }

        /// <summary>Makes the chosen server's session current; leaving one drops its connection.</summary>
        private void UseServer(ServerEntry sv)
        {
            var next = SessionFor(sv);
            if (next != _s) _s.Reset();
            _s = next;
        }

        // ---------------------------------------------------------------- PIN (rAthena's HC_SECOND_PASSWD_LOGIN 0x08b9)
        /// <summary>The PIN states (rA:char/char_clif.cpp:146-158, char.hpp:112-129; PACKETVER 20211103 ≥ 20180124, so "passed" is
        /// 0, never 7): 0 passed; 1 ask → 0x08b8; 2 / 4 none set → 0x08ba (char_logif.cpp:52-61: 4 when pincode_force, which the
        /// server does not enforce — any packet passes while no PIN is set, char_clif.cpp:1684-1686 — so "Skip for now");
        /// 3 expired → 0x08be old + new; 5 the new PIN is refused (pincode_allowed: repeated / sequential digits), after a
        /// create or a change, whose old PIN was right (char_clif.cpp:319-331); 8 wrong (char.cpp:2138-2150: no lock-out, the
        /// login server only logs pincode_maxtry failures, loginchrif.cpp:625-644).</summary>
        private void OnPin(int state)
        {
            if (_inWorld) return;
            if (state == 0 || state == 7) { _pinMode = PinMode.None; if (_s.State >= RoSession.Stage.CharList) Show(Screen.Chars); return; }
            _pin = "";
            switch (state)
            {
                case 1: _pinMode = PinMode.Check; break;
                case 2: case 4: _pinMode = PinMode.Create; _pinFirst = ""; break;
                case 3: _pinMode = PinMode.Change; _pinOld = ""; _pinFirst = ""; break;
                case 5: if (_pinMode != PinMode.Change) _pinMode = PinMode.Create; _pinFirst = ""; break; // keep the old PIN of a change: it was right
                case 8: _pinWrong++; if (_pinMode == PinMode.Change) { _pinOld = ""; _pinFirst = ""; } else _pinMode = PinMode.Check; break;
                default: _pinMode = PinMode.None; Status(Lang.F("The server asked for a PIN step Loki doesn't know ({0}).", state)); Show(Screen.Login); return; // 6 (KSSN) is never sent (char.hpp:120)
            }
            _pinState = state;
            if (state == 5 || state == 8) Status(RefusalText.Pin(state)); // client wave 4: the official client's own text (msgstringtable 1896 / 1892)
            Show(Screen.Pin); RefreshPin();
        }
        private int _pinState = -1;

        private void PinKey(string k)
        {
            if (k == "Clear") _pin = "";
            else if (k == "Back") { if (_pin.Length > 0) _pin = _pin.Substring(0, _pin.Length - 1); }
            else if (_pin.Length < 4) _pin += k;
            RefreshPin();
        }

        private void PinOk()
        {
            if (_pin.Length != 4) { Status(Lang.T("A PIN has 4 digits.")); return; }
            switch (_pinMode)
            {
                case PinMode.Check: _s.PinCheck(_pin); Status(Lang.T("Checking…")); break;
                case PinMode.Create:
                    if (_pinFirst.Length == 0) { _pinFirst = _pin; _pin = ""; RefreshPin(); return; } // ask twice
                    if (_pinFirst != _pin) { _pinFirst = ""; _pin = ""; Status(Lang.T("The two PINs differ — start again.")); RefreshPin(); return; }
                    _s.PinCreate(_pin); Status(Lang.T("Saving your PIN…"));
                    break;
                case PinMode.Change:
                    if (_pinOld.Length == 0) { _pinOld = _pin; _pin = ""; RefreshPin(); return; }       // the old PIN
                    if (_pinFirst.Length == 0) { _pinFirst = _pin; _pin = ""; RefreshPin(); return; }   // the new one
                    if (_pinFirst != _pin) { _pinFirst = ""; _pin = ""; Status(Lang.T("The two new PINs differ — enter the new PIN again.")); RefreshPin(); return; }
                    _s.PinChange(_pinOld, _pin); Status(Lang.T("Changing your PIN…"));
                    break;
                default: return;
            }
            _pin = "";
            RefreshPin();
        }

        private void RefreshPin()
        {
            string head;
            switch (_pinMode)
            {
                case PinMode.Check: head = _pinState == 8 ? Lang.T("Wrong PIN — try again") : Lang.T("Enter your PIN"); break;
                case PinMode.Create:
                    head = _pinFirst.Length > 0 ? Lang.T("Enter the same PIN again to confirm")
                         : _pinState == 5 ? Lang.T("The server doesn't accept that PIN (e.g. 1111 or 1234): choose another")
                         : Lang.T("Choose a 4-digit PIN for this account"); break;
                case PinMode.Change:
                    head = _pinOld.Length == 0 ? (_pinState == 8 ? Lang.T("Wrong PIN — enter your OLD PIN again") : Lang.T("Your PIN has to be changed: enter the OLD PIN"))
                         : _pinFirst.Length == 0 ? (_pinState == 5 ? Lang.T("The server doesn't accept that PIN (e.g. 1111 or 1234): choose another NEW PIN") : Lang.T("Now enter the NEW PIN"))
                         : Lang.T("Enter the NEW PIN again to confirm"); break;
                default: head = ""; break;
            }
            _pinText.text = head;
            _pinDots.text = new string('●', _pin.Length) + new string('○', 4 - _pin.Length);
            _pinSkip.gameObject.SetActive(_pinMode == PinMode.Create);
        }

        // ---------------------------------------------------------------- actions
        private void DoLogin()
        {
            string u = _user.Text.Trim(), p = _pass.Text;
            // wave H: each server's own minimum (rAthena 4 as before; Loki Online 6: server/src/rules.rs)
            var lim = SessionFor(Selected);
            if (u.Length < lim.AccountNameMin) { Status(Lang.F("The account name needs at least {0} characters.", lim.AccountNameMin)); return; }
            if (p.Length < lim.PasswordMinLength) { Status(Lang.F("The password needs at least {0} characters.", lim.PasswordMinLength)); return; }
            SaveLogin();
            var sv = Selected; if (sv == null) return;
            StopReconnect(null);
            StartLogin(sv, u, p, Lang.F("Logging in to {0} ({1})…", sv.name, sv.Address));
        }

        /// <summary>One login (the login button, and every reconnect attempt: LoginPanel.Reconnect.cs).</summary>
        private void StartLogin(ServerEntry sv, string user, string pass, string status)
        {
            UseServer(sv);
            _s.AutoRegister = false; _s.RegisterAccount = false; _pinWrong = 0; _inWorld = false; _reachedChars = false; _altLocalPortTried = 0;
            NoteLogin(sv, user, pass);
            Show(Screen.Connecting); Status(status);
            _s.Login(sv.LoginHost, sv.port, sv.database, user, pass);
        }

        /// <summary>One login with this PC's saved login, no password ("Continue as before", and every reconnect attempt on a server
        /// that keeps one: LoginPanel.Reconnect.cs).</summary>
        private void StartResume(ServerEntry sv, string user, string status)
        {
            UseServer(sv);
            _pinWrong = 0; _inWorld = false; _reachedChars = false;
            _lastLogin = (ServerKey(sv), user ?? "", "", true);
            Show(Screen.Connecting); Status(status);
            _s.Resume(sv.LoginHost, sv.port, sv.database);
        }

        /// <summary>The last login this panel started (server key, account, password, by the saved login): what a reconnect uses.
        /// Memory only. On a server that keeps this PC's login (<see cref="IAccountSession.CanResume"/>: Loki Online) a reconnect
        /// goes through that saved login, so no password is kept at all.</summary>
        private (string server, string user, string pass, bool resume) _lastLogin;
        private void NoteLogin(ServerEntry sv, string user, string pass)
        {
            bool resume = SessionFor(sv).CanResume;
            _lastLogin = (ServerKey(sv), user, resume ? "" : pass, resume);
        }

        private void DoRegister(char sex)
        {
            string u = _user.Text.Trim(), p = _pass.Text;
            var lim = SessionFor(Selected); // wave H: each server's own lengths (rAthena 4–21 as before; Loki Online 6–23)
            if (u.Length < lim.AccountNameMin || u.Length > lim.AccountNameMax) { Status(Lang.F("Account names are {0}–{1} characters (letters, digits).", lim.AccountNameMin, lim.AccountNameMax)); return; }
            if (u.EndsWith("_M") || u.EndsWith("_F")) { Status(Lang.T("Don't add _M/_F yourself — pick the sex button instead.")); return; }
            if (p.Length < lim.PasswordMinLength) { Status(Lang.F("The password needs at least {0} characters.", lim.PasswordMinLength)); return; }
            // CA_LOGIN carries password[24] (rA:common/packets.hpp:119-125): 23 characters reach the server, and a longer password would
            // be stored cut while the hashed logins after it hash all of it — that account could never log in. (Loki Online's
            // registration sends a hash of the whole password.)
            if (!lim.RegisterHashed && RoText.Encode(p).Length > 23) { Status(Lang.T("For a new account the password can have at most 23 characters.")); return; }
            var sv = Selected; if (sv == null) return;
            if (!RegisterConfirmed(sv, sex)) return; // client wave 4: the clear-text warning for a server on another PC
            SaveLogin();
            UseServer(sv);
            _s.AutoRegister = false; _s.RegisterSex = sex; _s.RegisterAccount = true; _pinWrong = 0; _reachedChars = false; _altLocalPortTried = 0; StopReconnect(null);
            NoteLogin(sv, u, p);
            Show(Screen.Connecting); Status(Lang.F("Creating account {0} on {1}…", u, sv.name));
            _s.Login(sv.LoginHost, sv.port, sv.database, u, p);
        }

        private void SaveServerChoice()
        {
            if (!PersistChoices) return;
            try
            {
                PlayerPrefs.SetInt("loki.server", _server);
                if (Selected != null)
                {
                    string key = ServerKey(Selected);
                    PlayerPrefs.SetString("loki.serverKey", key);
                    PlayerPrefs.SetString("loki.serverKeyV2", key);
                }
                PlayerPrefs.Save();
            }
            catch { }
        }

        private void SaveLogin()
        {
            if (!PersistChoices) return;
            try
            {
                PlayerPrefs.SetInt("loki.remember", _rememberOn ? 1 : 0);
                PlayerPrefs.SetString("loki.account", _rememberOn ? _user.Text.Trim() : "");
                SaveServerChoice();
            }
            catch { }
        }

        private void Logout() { StopReconnect(null); _s.Reset(); _pass.SetText(""); _lastLogin = default; _reachedChars = false; Show(Screen.Login); Status(Lang.T("Logged out.")); }

        private CharInfo CharAt(int row) { var list = SortedChars(); int i = _charPage * CharsPerPage + row; return row >= 0 && i < list.Count ? list[i] : null; }
        private List<CharInfo> SortedChars() { var l = new List<CharInfo>(_s.Characters); l.Sort((a, b) => a.Slot.CompareTo(b.Slot)); return l; }

        private void Play(int row)
        {
            var c = CharAt(row); if (c == null) return;
            // rAthena selects only characters without a deletion date (`delete_date` = 0, rA:char/char_clif.cpp:1090-1098) and not blocked
            // (sd.unban_time, :1109-1113): the client says so instead of sending a select the server rejects
            if (c.DeleteAt != 0) { Status(Lang.T("This character is scheduled for deletion — cancel that first (its Delete button).")); return; }
            if (!string.IsNullOrEmpty(c.BlockedUntil)) { Status(Lang.F("{0} is blocked until {1}.", c.Name, c.BlockedUntil)); return; }
            Status(Lang.F("Entering the world as {0}…", c.Name)); Show(Screen.Connecting);
            _lastPlaySlot = c.Slot;
            CharacterChosen?.Invoke(c);
            _s.SelectCharacter(c.Slot);
        }

        // ---------------------------------------------------------------- deletion (client wave 4: every state of the timer)
        /// <summary>The character a first press of Delete asked about, and until when the second press counts.</summary>
        private uint _deleteAsk; private float _deleteAskUntil;

        /// <summary>rAthena's deletion (PACKETVER ≥ 20100803 "delete2", rA:char/char_clif.cpp:522-780): Delete schedules it
        /// (0x0827 → 0x0828, due after char_del_delay, rA:conf/char_athena.conf:176 24 h), the row counts down, Delete then cancels
        /// it (0x082b → 0x082c) until it is due, and once due deletes the character for good (0x0829 with the birth date → 0x082a,
        /// then the list). Scheduling and the final deletion each ask for a second press within 5 s.</summary>
        private void Delete(int row)
        {
            var c = CharAt(row); if (c == null) return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            bool again = _deleteAsk == c.CharId && Time.time <= _deleteAskUntil;
            if (c.DeleteAt == 0)
            {
                if (!again) { AskDelete(c, Lang.F("Press Delete again to schedule {0} for deletion.", c.Name)); return; }
                _deleteAsk = 0; _s.DeleteReserve(c.CharId); Status(Lang.F("Scheduling {0} for deletion…", c.Name));
            }
            else if (now < c.DeleteAt) { _deleteAsk = 0; _s.DeleteCancel(c.CharId); Status(Lang.F("Cancelling the deletion of {0}…", c.Name)); }
            else
            {
                if (!again) { AskDelete(c, Lang.F("{0} will be deleted for good: press Delete again.", c.Name)); return; }
                _deleteAsk = 0; _s.DeleteConfirm(c.CharId, _birth.Text); Status(Lang.F("Deleting {0}…", c.Name));
            }
        }
        private void AskDelete(CharInfo c, string text) { _deleteAsk = c.CharId; _deleteAskUntil = Time.time + 5f; Status(text); RefreshChars(); }

        /// <summary>"23 h 59 min", "12 min 5 s", "42 s".</summary>
        public static string Left(long seconds)
        {
            if (seconds < 0) seconds = 0;
            if (seconds >= 3600) return string.Format("{0} h {1} min", seconds / 3600, seconds % 3600 / 60);
            if (seconds >= 60) return string.Format("{0} min {1} s", seconds / 60, seconds % 60);
            return string.Format("{0} s", seconds);
        }

        /// <summary>A name as the server will store it: leading / trailing blanks gone and runs of them made one space, as
        /// rAthena's normalize_name does with TRIM_CHARS (rA:common/strlib.cpp:59-92, rA:char/char.hpp:161: soft hyphen 0xAD,
        /// no-break space 0xA0, 0x1A, tab, CR, LF, space) before it checks and stores the name.</summary>
        public static string NormalizeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string trim = new string(new[] { (char)0xAD, (char)0xA0, (char)0x1A, '\t', '\n', '\r', ' ' }); // TRIM_CHARS "\255\xA0\032\t\x0A\x0D "
            var sb = new System.Text.StringBuilder(s.Length); bool gap = false;
            foreach (char c in s)
            {
                if (trim.IndexOf(c) >= 0) { gap = sb.Length > 0; continue; }
                if (gap) { sb.Append(' '); gap = false; }
                sb.Append(c);
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- view
        public void Show(Screen sc)
        {
            _screen = sc;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            foreach (var kv in _pages) kv.Value.gameObject.SetActive(kv.Key == sc);
            _kb.Hide(); foreach (var f in _fields) f.Blur();
            if (sc != Screen.Create) HidePreview();
            switch (sc)
            {
                case Screen.Login: _title.text = Lang.T("Loki — Ragnarok Online in VR"); RefreshLogin(); RefreshServers(); break;
                case Screen.Register: _title.text = Lang.T("Create an Account"); RefreshRegister(); break;
                case Screen.Connecting: _title.text = "Loki"; RefreshConnecting(); break;
                case Screen.Pin: _title.text = Lang.T("PIN code"); RefreshPin(); break;
                case Screen.Chars: _title.text = Lang.T("Choose your character"); RefreshChars(); break;
                case Screen.Create: _title.text = Lang.T("New character"); RefreshCreate(); break;
                case Screen.Manage: _title.text = Lang.T("Manage character"); _renameNote.text = ""; RefreshManage(); break;
                case Screen.Servers: _title.text = Lang.T("Servers"); RefreshServerList(); break;
                case Screen.ServerEdit: _title.text = _editing == null ? Lang.T("Add a server") : Lang.T("Edit a server"); RefreshServerEdit(); break;
                case Screen.MapChoice: _title.text = Lang.T("Where to?"); break;
                case Screen.Password: _title.text = Lang.T("Change the password"); RefreshPassword(); break;
            }
        }

        public void Hide() { _kb.Hide(); HidePreview(); if (gameObject.activeSelf) gameObject.SetActive(false); }

        /// <summary>Minimum initial spawn distance from the player's head (metres) when the login screen is generated/placed.</summary>
        public const float MinSpawnDistance = 0.85f;
        /// <summary>True once the player has manually grabbed and moved the login panel during this session.</summary>
        public bool UserMoved { get; set; }

        /// <summary>Client wave 24 (w24): where the player put it (this PC's kept place), else 95 cm ahead of the eyes and 2 cm up,
        /// facing the head (as before); never in the ground (UI/PanelPlacing.cs: the low head's rule, the ground guard).</summary>
        public void PlaceInFront()
        {
            if (_head == null) return;
            _placedLow = _head.transform.localPosition.y < 0.60f;
            var s = Loki.World.UserSettings.Prefs;
            PanelPlacing.Place(transform, _head, s.loginPlaced ? s.loginPlace : null, DefaultPlace, UserMoved ? 0f : MinSpawnDistance);
            if (_preview != null && _preview.gameObject.activeInHierarchy) PanelPlacing.Guard(_rt, _preview.transform.position); // (the creation character's feet too)
        }
        /// <summary>The login panel's default place from the eyes: metres right, up, ahead.</summary>
        public static readonly Vector3 DefaultPlace = new Vector3(0f, 0.02f, 0.95f);

        private void FocusField(VrTextField f)
        {
            foreach (var o in _fields) if (o != f) o.Blur();
            f.Focus(); _kb.Attach(f); _kb.Show();
        }

        public void Status(string s) { _status.text = s ?? ""; _statusUntil = Time.time + 12f; Debug.Log("[Login] " + s); }

        private static string Esc(string s) => (s ?? "").Replace("<", "<​");

        private UiButton _resumeBtn;
        /// <summary>"Continue as before": the server logs this PC in with its saved login (Loki Online: the SDK identity, which the
        /// server still binds to the account unless the player logged out — the `logout` reducer drops the binding).</summary>
        private void DoResume()
        {
            var sv = Selected; var sess = SessionFor(sv);
            if (sv == null || !sess.CanResume) return;
            SaveServerChoice();
            StopReconnect(null);
            StartResume(sv, "", Lang.F("Continuing on {0} with this PC's saved login…", sv.name));
        }

        private void RefreshServers()
        {
            if (_servers.Count == 0) return;
            _server = Mathf.Clamp(_server, 0, _servers.Count - 1);
            bool picker = ShowServerPicker;
            if (_serverLabel != null) _serverLabel.gameObject.SetActive(picker);
            if (_serverInfo != null) _serverInfo.gameObject.SetActive(picker);
            if (_serversBtn != null) _serversBtn.gameObject.SetActive(picker);
            // the first three, with the chosen one in the last place when it is further down the list
            for (int i = 0; i < _quick.Length; i++) _quickIndex[i] = i < _servers.Count ? i : -1;
            if (_server >= _quick.Length) _quickIndex[_quick.Length - 1] = _server;
            for (int i = 0; i < _quick.Length; i++)
            {
                int k = _quickIndex[i]; var b = _quick[i];
                bool show = picker && k >= 0;
                b.gameObject.SetActive(show); if (!show) continue;
                b.SetLabel(Esc(_servers[k].name));
                b.Normal = k == _server ? new Color(0.45f, 0.35f, 0.15f) : UiKit.ButtonBg; b.SetHover(false);
            }
            var sv = Selected; var sess = SessionFor(sv);
            if (_user != null) { _user.Placeholder = Lang.F("account name ({0}–{1} letters or digits)", sess.AccountNameMin, 23); if (_user.Text.Length == 0) _user.SetText(""); } // wave H: the chosen server's minimum (redrawn while the field is empty)
            if (_serverInfo != null && picker) _serverInfo.text = sv == null ? "" : sv.IsStdb ? $"{Esc(sv.name)}  ·  {sv.KindName}" : $"{Esc(sv.name)}  ·  {Esc(sv.Address)}  ·  {sv.KindName}";
            if (_resumeBtn != null) _resumeBtn.gameObject.SetActive(sv != null && sess.CanResume && sess.HasSavedLogin(sv.LoginHost, sv.port, sv.database));
            if (_regHint != null)
                _regHint.text = sv != null && sess.RegisterHashed
                    ? Lang.F("No account yet? Create one here: the password is hashed on this PC before it is sent to {0}.", sv.KindName)
                    : IsThisPc(sv) ? Lang.T("No account yet? Create one here if the server allows it (the password stays on this PC).")
                    : Lang.T("No account yet? Ask the host to make one in Loki Host's Accounts tab (safest), or create it here if the server allows it: this once the password travels unencrypted.");
            _regAsk = null; RefreshRegisterButtons();
            if (_screen == Screen.Register) RefreshRegister();
        }
        private void RefreshLogin() { _remember.SetLabel(_rememberOn ? Lang.T("Remember account: on") : Lang.T("Remember account: off")); RefreshDataButton(); }

        public static string JobName(int job) => JobName(job, 1);
        /// <summary>Client wave 5 (w5ui): in Portuguese the job's name comes from the player's GRF (pcjobnamegender_ptbr.lub through the
        /// first-run builder's skilltree.tsv; the female form for <paramref name="sex"/> 0 = SEX_FEMALE), else rAthena's English names
        /// (jobnames.tsv) as before.</summary>
        public static string JobName(int job, int sex)
        {
            if (Loki.Game.ClientText.Portuguese) { string pt = Loki.Game.SkillTreeDb.JobNamePt(job, sex); if (pt.Length > 0) return pt; }
            if (_jobNames == null)
            {
                _jobNames = new Dictionary<int, string>();
                try { foreach (var line in File.ReadLines(Path.Combine(Application.streamingAssetsPath, "data", "jobnames.tsv"))) { var c = line.Split('\t'); if (c.Length >= 2 && int.TryParse(c[0], out int id)) _jobNames[id] = c[1]; } }
                catch (Exception e) { Debug.LogWarning("[Login] jobnames: " + e.Message); }
            }
            return _jobNames.TryGetValue(job, out var n) ? n : Lang.F("job {0}", job);
        }

        private float _charsRefreshAt;
        private void RefreshChars()
        {
            if (_screen != Screen.Chars) return;
            _charsRefreshAt = Time.time + 1f;
            var list = SortedChars();
            int pages = Mathf.Max(1, (list.Count + CharsPerPage - 1) / CharsPerPage);
            _charPage = Mathf.Clamp(_charPage, 0, pages - 1);
            _charInfo.text = Lang.F("{0} of {1} slots used", list.Count, Mathf.Max(_s.TotalSlots, list.Count)) + (pages > 1 ? "  ·  " + Lang.F("page {0} / {1}", _charPage + 1, pages) : "");
            _charPrev.gameObject.SetActive(pages > 1); _charNext.gameObject.SetActive(pages > 1);
            RefreshPasswordButton();
            _charPrev.Interactable = _charPage > 0; _charNext.Interactable = _charPage < pages - 1;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (_deleteAsk != 0 && Time.time > _deleteAskUntil) _deleteAsk = 0;
            for (int r = 0; r < _charRows.Count; r++)
            {
                var (row, text, play, del) = _charRows[r];
                int i = _charPage * CharsPerPage + r;
                bool show = i < list.Count; row.gameObject.SetActive(show);
                if (!show) continue;
                var c = list[i];
                // More: rename / slot move, offered where the server has them and the character's CHARACTER_INFO allows them
                // (chr_name_changeCnt / chr_slot_changeCnt)
                _moreButtons[r].gameObject.SetActive((_s.CanRename && c.NameChangeCount > 0) || (_s.CanMoveSlot && c.SlotChangeCount > 0));
                string map = Loki.Game.ClientText.MapName(Loki.Game.Navigation.Bare(c.MapName));
                string state = "";
                bool asked = _deleteAsk == c.CharId;
                if (c.DeleteAt != 0)
                {
                    state = now < c.DeleteAt
                        ? "\n" + Lang.F("<color=#ff8a7a>deletion in {0} — Delete cancels it</color>", Left(c.DeleteAt - now))
                        : "\n" + Lang.T("<color=#ff8a7a>deletion due — Delete deletes it for good (birth date below)</color>");
                    del.SetLabel(now < c.DeleteAt ? Lang.T("Cancel") : asked ? Lang.T("Sure?") : Lang.T("Delete now"));
                }
                else del.SetLabel(asked ? Lang.T("Sure?") : Lang.T("Delete"));
                if (!string.IsNullOrEmpty(c.BlockedUntil)) state += "\n" + Lang.F("<color=#ffb070>blocked until {0}</color>", Esc(c.BlockedUntil));
                play.Interactable = c.DeleteAt == 0 && string.IsNullOrEmpty(c.BlockedUntil); play.SetHover(false);
                text.text = Lang.F("<b>{0}</b>   <color=#9aa3b5>Lv {1} / {2} {3} · {4} · {5:N0} z</color>", Esc(c.Name), c.BaseLevel, c.JobLevel, JobName(c.Job, c.Sex), map, c.Zeny) + state;
            }
            RefreshW5Previews(list, now); // client wave 5 (w5ui): the rows' pictures (LoginPanel.W5Ui.cs)
        }

        private float _charsAt = -1f, _connectingRefreshAt;
        private bool _placedTracked, _placedLow, _inWorld;
        private void LateUpdate()
        {
            // the panel is placed when the game starts, often before the headset reports a pose (the head still at the rig's origin,
            // on the floor): place it again, once, as soon as the head is tracked, and again if it started on the desk and was put on
            if (!_placedTracked && _head != null && _head.transform.localPosition.sqrMagnitude > 0.01f) { _placedTracked = true; PlaceInFront(); }
            else if (_placedLow && _head != null && _head.transform.localPosition.y > 0.85f) { _placedLow = false; if (!_inWorld) PlaceInFront(); }
            if (_status != null && _status.text.Length > 0 && Time.time > _statusUntil) _status.text = "";
            // no PIN packet within 3 s of the list (a server built without PACKETVER_SUPPORTS_PINCODE): show the list anyway
            if (!_inWorld && _screen == Screen.Connecting && _charsAt > 0 && _s.State == RoSession.Stage.CharList && _s.PinState < 0 && Time.time - _charsAt > 3f) { _charsAt = -1f; ReconnectReached(); Show(Screen.Chars); }
            if (_screen == Screen.Create) TickCreate();
            if (_screen == Screen.Chars && Time.time >= _charsRefreshAt) RefreshChars(); // the deletion countdown
            TickServerTest();
            TickReconnect();
            if (_regAsk != null && Time.time > _regAskUntil) { _regAsk = null; RefreshRegisterButtons(); }
            if (_screen == Screen.Connecting && _reconnecting && Time.time >= _connectingRefreshAt) { _connectingRefreshAt = Time.time + 1f; RefreshConnecting(); }
        }

        // ---- physical keyboard → the focused field (routed by the game from ChatPanel-style events)
        public bool WantsKeyboard => Visible && _fields.Exists(f => f.Focused && f.gameObject.activeInHierarchy);
        private VrTextField FocusedField => _fields.Find(f => f.Focused && f.gameObject.activeInHierarchy);
        public void KeyChar(char c) => FocusedField?.Type(c);
        public void KeyBackspace() => FocusedField?.Backspace();
        public void KeyEnter() => FocusedField?.Submit();
        public void KeyCaret(int delta) => FocusedField?.MoveCaret(delta);
        public void KeyDelete() => FocusedField?.Delete();

        // ---- smoke-test hooks
        public ServerEntry ServerAt(int index) => index >= 0 && index < _servers.Count ? _servers[index] : null;
        public int ServerIndexOf(Func<ServerEntry, bool> match) => _servers.FindIndex(e => match(e));
        public int ServerCount => _servers.Count;
        public int SelectedServer => _server;
        public string PinPrompt => _pinText != null ? _pinText.text : "";
        public int PinWrongCount => _pinWrong;
        public bool PinSkipShown => _pinSkip != null && _pinSkip.gameObject.activeInHierarchy;
        public string CreateInfo => _createInfo != null ? UiKit.StripTags(_createInfo.text) : "";
        public bool ResumeShown => _resumeBtn != null && _resumeBtn.gameObject.activeInHierarchy;
        public string RegisterHint => _regHint != null ? _regHint.text : "";
        public string ConnectingText => _connectingText != null ? _connectingText.text : "";

        /// <summary>Back from the world to the Loki Online character list (the SpacetimeDB session stayed logged in).</summary>
        public void ResumeOnline()
        {
            // the entry the session is connected to (wave H: it was the first Loki Online entry, whichever the player had chosen).
            // Several entries can name one database (ws and wss): first the one whose address the session uses.
            var st = _online as Loki.Net.Stdb.StdbSession;
            string key = st?.ServerKey, uri = st?.Uri;
            string KeyOf(ServerEntry e) => $"{ServerAddress.Split(e.host, e.scheme).host}:{e.port}/{(string.IsNullOrEmpty(e.database) ? "loki" : e.database)}";
            int i = string.IsNullOrEmpty(key) ? -1 : _servers.FindIndex(e => e.IsStdb && KeyOf(e) == key && $"{e.LoginHost}:{e.port}" == uri);
            if (i < 0 && !string.IsNullOrEmpty(key)) i = _servers.FindIndex(e => e.IsStdb && KeyOf(e) == key);
            if (i < 0) i = _servers.FindIndex(e => e.IsStdb);
            if (i < 0 || _online == null) { Show(Screen.Login); return; }
            _server = i; RefreshServers();
            _s = _online;
            _lastLogin = (ServerKey(_servers[i]), "", "", true); _reachedChars = true; // wave I: a connection lost from here on reconnects by the saved login
            Show(Screen.Chars);
        }
        public string StatusText => _status != null ? _status.text : "";
        public int CharRowsShown { get { int n = 0; foreach (var r in _charRows) if (r.row.gameObject.activeSelf) n++; return n; } }
    }
}
