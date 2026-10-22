using System;
using System.Collections.Generic;
using System.Net;
using UnityEngine;

namespace Loki.Net
{
    /// <summary>Character summary as sent by the char server (CHARACTER_INFO for PACKETVER 20211103).</summary>
    public sealed class CharInfo
    {
        public uint CharId; public long BaseExp, JobExp; public int Zeny, JobLevel, BodyState, HealthState, EffectState, Virtue, Honor;
        public short JobPoint; public long Hp, MaxHp, Sp, MaxSp; public short Speed, Job, Head, Body, Weapon, BaseLevel, StatPoint, Accessory, Shield, Accessory2, Accessory3, HeadPalette, BodyPalette;
        public string Name; public byte Str, Agi, Vit, Int, Dex, Luk, Slot, HairColor; public short IsChangedName; public string MapName; public int DelRevDate, RobePalette, SlotChangeCount, NameChangeCount; public byte Sex;
        /// <summary>Client wave 4 (w4account): when the scheduled deletion falls due, in Unix seconds (0 = none). CHARACTER_INFO's
        /// DelRevDate is the seconds LEFT for PACKETVER 20211103 (PACKETVER_CHAR_DELETEDATE, rA:config/packets.hpp:86;
        /// rA:char/char.cpp:1845-1848: delete_date - time(nullptr)), so it is turned into a date when the list arrives. Loki
        /// Online's session fills it from its own table.</summary>
        public long DeleteAt;
        /// <summary>HC_BLOCK_CHARACTER 0x020d: the character is blocked until this date ("YYYY-MM-DD HH:MM:SS"), null when not.</summary>
        public string BlockedUntil;

        public static CharInfo Parse(ref PacketReader r)
        {
            var c = new CharInfo
            {
                CharId = r.U32(), BaseExp = r.S64(), Zeny = r.S32(), JobExp = r.S64(), JobLevel = r.S32(), BodyState = r.S32(), HealthState = r.S32(), EffectState = r.S32(),
                Virtue = r.S32(), Honor = r.S32(), JobPoint = r.S16(), Hp = r.S64(), MaxHp = r.S64(), Sp = r.S64(), MaxSp = r.S64(), Speed = r.S16(), Job = r.S16(), Head = r.S16(),
                Body = r.S16(), Weapon = r.S16(), BaseLevel = r.S16(), StatPoint = r.S16(), Accessory = r.S16(), Shield = r.S16(), Accessory2 = r.S16(), Accessory3 = r.S16(),
                HeadPalette = r.S16(), BodyPalette = r.S16(), Name = r.Str(24), Str = r.U8(), Agi = r.U8(), Vit = r.U8(), Int = r.U8(), Dex = r.U8(), Luk = r.U8(),
                Slot = r.U8(), HairColor = r.U8(), IsChangedName = r.S16(), MapName = r.Str(16), DelRevDate = r.S32(), RobePalette = r.S32(), SlotChangeCount = r.S32(), NameChangeCount = r.S32(), Sex = r.U8()
            };
            c.DeleteAt = c.DelRevDate != 0 ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() + c.DelRevDate : 0; // seconds left → a date (w4account)
            return c;
        }
    }

    public sealed class CharServerInfo { public string Ip; public int Port; public string Name; public int Users, Type, New; }

    /// <summary>
    /// Login → char → map handshake for rAthena (PACKETVER 20211103, no obfuscation, web token off).
    /// Drive with <see cref="Update"/> every frame. Raises events; the game layer decides what to show.
    /// Login is the client's challenge–response by default: CA_REQ_HASH 0x01db → AC_ACK_HASH 0x01dc (random salt, 1..20 bytes)
    /// → CA_LOGIN2 0x01dd with md5(salt ‖ password) — the password never crosses the wire (login.cpp login_check_password,
    /// passwdenc mode 1; works while the server keeps plain passwords, `use_MD5_passwords: no`). Account creation must use
    /// the plain CA_LOGIN 0x0064 with rAthena's _M/_F suffix (login.cpp:323-331 refuses registration in passwdenc mode).
    /// PIN codes (char_clif.cpp / char.cpp char_pincode_decrypt): HC_SECOND_PASSWD_LOGIN 0x08b9 seed.L aid.L state.W;
    /// the client answers 0x08b8 (check), 0x08ba (create) or 0x08be (change) with digits scrambled by the seed.
    /// </summary>
    public sealed class RoSession
    {
        public enum Stage { Idle, LoginConnecting, LoginSent, CharServerList, CharConnecting, CharList, CharSelected, MapConnecting, MapEntering, InGame, Failed }
        public Stage State { get; private set; } = Stage.Idle;
        public string FailReason { get; private set; }
        /// <summary>PIN prompt from the char server: 0/7 passed, 1 ask, 2/4 create, 3 expired (change), 5 illegal new PIN, 8 wrong.</summary>
        public int PinState { get; private set; } = -1; public uint PinSeed { get; private set; }
        public bool PinPending => PinState == 1 || PinState == 2 || PinState == 3 || PinState == 4 || PinState == 5 || PinState == 8;
        public event Action<RoSession, int> OnPinState;
        /// <summary>Login refused (0x083e): the server's error code, and the text for it.</summary>
        public event Action<RoSession, int, string> OnLoginRefused;
        /// <summary>Character creation refused (0x006e): text.</summary>
        public event Action<RoSession, string> OnCreateRefused;
        /// <summary>Character deletion: (charId, deleteDate unix or 0, message) after 0x0828 / 0x082a / 0x082c.</summary>
        public event Action<RoSession, uint, long, string> OnDeleteResult;

        public uint AccountId, LoginId1, LoginId2; public byte Sex;
        /// <summary>Client wave 18 (w18acct): the web token the login server gave this login (AC_ACCEPT_LOGIN's token[WEB_AUTH_TOKEN_LENGTH 17],
        /// rA:common/packets.hpp:198-209, filled at rA:login/loginclif.cpp:127; "" when the server makes none: use_web_auth_token off,
        /// login.cpp:426-428). Loki Host's relay takes it as the proof that settings are this account's (Net/IAccountSettingsChannel.cs).</summary>
        public string WebAuthToken = "";
        public List<CharServerInfo> CharServers = new List<CharServerInfo>();
        public List<CharInfo> Characters = new List<CharInfo>();
        public int TotalSlots, PremiumStart, PremiumEnd;
        public CharInfo SelectedChar; public string MapName; public string MapHost; public int MapPort;
        public uint ServerTick; public int StartX, StartY, StartDir;

        public event Action<RoSession> OnCharList;
        public event Action<RoSession> OnEnteredMap;
        public event Action<RoSession, string> OnFailed;
        public event Action<RoSession, ushort, byte[], int, int> OnMapPacket; // for the entity layer

        private RoConnection _login, _char, _map;
        private string _user, _pass;
        private string _loginHost; private int _loginPort;
        private float _nextTick;

        public RoSession(string loginHost, int loginPort, string user, string pass)
        {
            _loginHost = loginHost; _loginPort = loginPort; _user = user ?? ""; _pass = pass ?? "";
        }

        public string Host => _loginHost; public int Port => _loginPort; public string User => _user;

        private bool _triedRegister;
        private string LoginName => _triedRegister && !_user.EndsWith("_M") && !_user.EndsWith("_F") ? _user + "_" + (RegisterSex == 'F' ? "F" : "M") : _user;
        /// <summary>Sex used when the account does not exist yet and gets auto-created (rAthena _M/_F suffix).</summary>
        public char RegisterSex = 'M';
        /// <summary>Register the account on this login (plain CA_LOGIN with the _M/_F suffix) instead of logging in. Off after use.</summary>
        public bool RegisterAccount;
        /// <summary>Unknown accounts are auto-registered on first login (the old dev behaviour; the login screen asks instead).</summary>
        public bool AutoRegister = true;
        /// <summary>Send md5(salt ‖ password) instead of the password (CA_LOGIN2). Registration always goes plain.</summary>
        public bool HashedLogin = true;
        private byte[] _salt;

        /// <summary>Starts (or restarts, after a failure or a logout) a login with new credentials.</summary>
        public void Login(string host, int port, string user, string pass)
        {
            Reset();
            _loginHost = host; _loginPort = port; _user = user ?? ""; _pass = pass ?? "";
            Start();
        }

        /// <summary>Drops every connection and goes back to Idle so <see cref="Start"/> can run again.</summary>
        public void Reset()
        {
            _login?.Close(); _char?.Close(); _map?.Close(); _login = _char = _map = null;
            State = Stage.Idle; FailReason = null; _retryAt = -1f; _retries = 0; _triedRegister = false; _salt = null; Leaving = Leave.None; LastBanCode = -1;
            PinState = -1; Characters.Clear(); CharServers.Clear(); SelectedChar = null; _lastPageCount = -1;
            WebAuthToken = ""; // client wave 18 (w18acct)
        }

        public void Start()
        {
            if (string.IsNullOrEmpty(_user)) { Fail(Lang.T("no account name")); return; }
            State = Stage.LoginConnecting;
            _login = new RoConnection("login", _loginHost, _loginPort, PacketTable.CommonLengths);
            _login.OnPacket += OnLoginPacket;
            // only the current link's close is a failure: the "already online" retry and the _M/_F registration close theirs on purpose
            // and open a new one, and RoConnection.Pump still reports the old one's close afterwards (it set "login server closed" and
            // the login screen flashed it before the retry went through: smoke-w3login-chars.log, 2026-09-30)
            _login.OnClosed += (c, err) => { if (c != _login) return; if (State < Stage.CharConnecting) Fail(Lang.F("login server closed: {0}", err)); };
            try { _login.Connect(); } catch (Exception e) { Fail(Lang.F("cannot reach login server: {0}", e.Message)); return; }
            if (RegisterAccount) { _triedRegister = true; RegisterAccount = false; }
            if (HashedLogin && !_triedRegister)
            {
                _login.Send(new PacketWriter(PacketTable.Common.CA_REQ_HASH).ToArray()); // the salt comes back in 0x01dc, then CA_LOGIN2
                Debug.Log("[Session] requesting the login salt (hashed login)");
            }
            else
            {
                // CA_LOGIN: version, username[24], password[24], clienttype — plain; only for registration (or HashedLogin off)
                var w = new PacketWriter(PacketTable.Common.CA_LOGIN).U32(20).Str(LoginName, 24).Str(_pass, 24).U8(0);
                _login.Send(w.ToArray());
                Debug.Log("[Session] " + (_triedRegister ? "registering " + LoginName : "plain login as " + LoginName));
            }
            State = Stage.LoginSent;
        }

        /// <summary>CA_LOGIN2 0x01dd: version.L username[24] md5(salt ‖ password)[16] clienttype.B (packets.hpp:347-354).</summary>
        private void SendHashedLogin()
        {
            var pw = RoText.Encode(_pass); // the same bytes the plain registration stored (PacketWriter.Str → RoText.Encode)
            var input = new byte[_salt.Length + pw.Length];
            Buffer.BlockCopy(_salt, 0, input, 0, _salt.Length); Buffer.BlockCopy(pw, 0, input, _salt.Length, pw.Length);
            byte[] digest;
            using (var md5 = System.Security.Cryptography.MD5.Create()) digest = md5.ComputeHash(input);
            var w = new PacketWriter(PacketTable.Common.CA_LOGIN2).U32(20).Str(_user, 24).Bytes(digest).U8(0);
            _login.Send(w.ToArray());
            Debug.Log($"[Session] hashed login as {_user} (salt {_salt.Length} bytes)");
        }

        /// <summary>SC_NOTIFY_BAN 0x0081 result codes as rAthena documents them (char_clif.cpp:492-512 chclif_send_auth_result; the map
        /// server's clif_authfail_fd sends the same codes, e.g. 15 for a GM kick).</summary>
        public static string BanText(int code)
        {
            switch (code)
            {
                case 1: return Lang.T("the server closed (1).");
                case 2: return Lang.T("someone has already logged in with this account (2).");
                case 3: return Lang.T("the time gap between client and server is too large (3).");
                case 4: case 7: return Lang.F("the server is overpopulated ({0}).", code);
                case 5: return Lang.T("underage accounts cannot join this server (5).");
                case 6: return Lang.T("this account has not been paid for (6).");
                case 8: return Lang.T("the account is already online (8).");
                case 9: return Lang.T("the internet cafe's IP capacity is full (9).");
                case 10: return Lang.T("out of available playing time (10).");
                case 11: return Lang.T("the account is suspended (11).");
                case 12: return Lang.T("terminated by a change of the billing policy (12).");
                case 13: return Lang.T("terminated: the IP does not match the authorised one (13).");
                case 14: return Lang.T("terminated to protect the account's play time (14).");
                case 15: return Lang.T("disconnected from the server (15), e.g. by a GM.");
                default: return Lang.F("disconnected (code {0}).", code);
            }
        }

        /// <summary>The text rAthena documents for AC_REFUSE_LOGIN error codes (loginclif.cpp:171-192).</summary>
        public static string RefuseText(int err)
        {
            switch (err)
            {
                case 0: return Lang.T("Unknown account.");
                case 1: return Lang.T("Incorrect password.");
                case 2: return Lang.T("This account has expired.");
                case 3: return Lang.T("Rejected by the server.");
                case 4: return Lang.T("This account is blocked by the GM team.");
                case 5: return Lang.T("Client version mismatch.");
                case 6: return Lang.T("This account is banned for now.");
                case 7: return Lang.T("The server is full.");
                case 8: return Lang.T("Too many connections from here.");
                case 99: return Lang.T("This account has been erased.");
                default: return Lang.F("Login refused (error {0}).", err);
            }
        }

        /// <summary>What a refused registration means. rAthena answers a CA_LOGIN with the _M/_F suffix through login_mmo_auth
        /// (rA:login/login.cpp:323-338): with `new_account: no` (the stock conf/login_athena.conf:56) the suffix is not stripped and
        /// "name_M" is an unknown account → 0; login_mmo_auth_new (login.cpp:224-249) answers 3 for the registration flood limit
        /// (allowed_regs per time_allowed), 1 for a name or password shorter than acc_name_min_length / password_min_length
        /// (6 each for PACKETVER ≥ 20181114, login.cpp:759-761) or a name that exists already.</summary>
        public static string RegisterRefuseText(int err)
        {
            switch (err)
            {
                case 0: return Lang.T("This server doesn't create accounts from the game. Ask the server's owner to make one for you, then log in.");
                case 1: return Lang.T("The account couldn't be created: that name is taken, or the name or password is too short for this server (often 6 characters at least).");
                case 3: return Lang.T("The server isn't taking new accounts this moment (too many at once): wait a few seconds and try again.");
                default: return Lang.F("The account couldn't be created. {0}", RefuseText(err));
            }
        }

        /// <summary>Client wave 4 (w4account): the last SC_NOTIFY_BAN 0x0081 code from any of the three servers (-1 none since the last
        /// <see cref="Reset"/>): the game decides from it whether to log in again by itself (<see cref="RefusalText.MayReconnect"/>).</summary>
        public int LastBanCode { get; private set; } = -1;

        private void Fail(string why)
        {
            if (State == Stage.Failed) return;
            State = Stage.Failed; FailReason = why;
            Debug.LogError("[Session] " + why);
            OnFailed?.Invoke(this, why);
        }

        private int _retries; private float _retryAt = -1f;
        /// <summary>Char-server keep-alive period, the official client's 12 s (char_clif.cpp:1393).</summary>
        public const float CharPingSeconds = 12f;
        private float _nextCharPing; private bool _charAuthed;
        /// <summary>Test switch (-lokiNoCharPing 1): no keep-alive, to show the server dropping a silent character-select client.</summary>
        public static bool DisableCharPing;
        /// <summary>Keep-alives sent on the char link so far (smoke test).</summary>
        public int CharPings { get; private set; }

        public void Update()
        {
            if (_retryAt > 0 && Time.time >= _retryAt) { _retryAt = -1f; Start(); }
            _login?.Pump(); _char?.Pump(); _map?.Pump();
            // Char-server keep-alive: PING 0x0187 AID.L "every 12 seconds" (char_clif.cpp:1393-1403, allowed even before the PIN,
            // :1661-1662). Without it socket.cpp drops a silent client after stall_time (conf/packet_athena.conf: 60 s) while the
            // player reads the character list or types a PIN.
            if (!DisableCharPing && _char != null && _char.Connected && _charAuthed && State >= Stage.CharConnecting && State < Stage.MapConnecting && Time.time >= _nextCharPing)
            {
                _nextCharPing = Time.time + CharPingSeconds;
                _char.Send(new PacketWriter(PacketTable.Common.PING).U32(AccountId).ToArray());
                CharPings++;
            }
            if (State == Stage.InGame && Time.time >= _nextTick)
            {
                _nextTick = Time.time + 10f;
                _map?.Send(new PacketWriter(0x0360).U32((uint)Environment.TickCount).ToArray()); // CZ_REQUEST_TIME
                Loki.Sprites.ServerClock.RequestSent(); // client wave 3 (w3move): the answer 0x007f is timed against this
            }
        }

        public void Dispose() { _login?.Close(); _char?.Close(); _map?.Close(); }

        // ------------------------------------------------------------------ login server
        private void OnLoginPacket(RoConnection c, ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x0ac4: // AC_ACCEPT_LOGIN
                {
                    r.Skip(4);
                    LoginId1 = r.U32(); AccountId = r.U32(); LoginId2 = r.U32(); r.U32(); r.Skip(26); Sex = r.U8(); WebAuthToken = r.Str(17); // (client wave 18, w18acct: the token, until then skipped)
                    CharServers.Clear();
                    while (r.Remaining >= 160)
                    {
                        var s = new CharServerInfo { Ip = new IPAddress(r.U32()).ToString(), Port = r.U16(), Name = r.Str(20), Users = r.U16(), Type = r.U16(), New = r.U16() };
                        r.Skip(128);
                        CharServers.Add(s);
                    }
                    Debug.Log($"[Session] login ok: AID {AccountId}, {CharServers.Count} char server(s): {string.Join(", ", CharServers.ConvertAll(s => s.Name + "@" + s.Ip + ":" + s.Port))}");
                    State = Stage.CharServerList;
                    _login.Close();
                    if (CharServers.Count > 0) ConnectChar(CharServers[0]);
                    else Fail(Lang.T("no char servers"));
                    break;
                }
                case 0x01dc: // AC_ACK_HASH len.W salt[] (loginclif.cpp:362-369; MD5_Salt fills 20 random bytes 1..255)
                {
                    r.Skip(4); _salt = new byte[Math.Max(0, n - 4)];
                    for (int i = 0; i < _salt.Length; i++) _salt[i] = r.U8();
                    // the server hashes the C string, so a NUL would cut the salt: it never sends one (rnd_value(1,255)), but be exact
                    int cut = Array.IndexOf(_salt, (byte)0); if (cut >= 0) Array.Resize(ref _salt, cut);
                    SendHashedLogin();
                    break;
                }
                case 0x083e: // AC_REFUSE_LOGIN error.L unblock_time[20]
                {
                    r.Skip(2); uint err = r.U32(); string until = r.Remaining >= 20 ? r.Str(20) : "";
                    if (err == 0 && AutoRegister && !_triedRegister && !_user.EndsWith("_M") && !_user.EndsWith("_F"))
                    {
                        // Unknown account: register it once through rAthena's _M/_F auto-creation, then log in normally next time.
                        _triedRegister = true;
                        Debug.Log("[Session] account unknown, registering as " + LoginName);
                        _login.Close(); _login = null;
                        State = Stage.Idle;
                        Start();
                        break;
                    }
                    // a registration (CA_LOGIN with the _M/_F suffix) is refused with the login codes of login.cpp:324-338 / 224-249;
                    // a login refusal shows the official client's own text for the code (client wave 4, Net/RefusalText.cs)
                    string text = _triedRegister ? RegisterRefuseText((int)err) : RefusalText.Login((int)err, until);
                    OnLoginRefused?.Invoke(this, (int)err, text);
                    Fail("login refused, error " + err + ": " + text);
                    break;
                }
                case 0x0081: // SC_NOTIFY_BAN — 8 = "already online" (the previous session is being kicked): retry shortly
                {
                    r.Skip(2); byte code = r.U8();
                    if (code == 8 && _retries++ < 3)
                    {
                        Debug.Log("[Session] account still online, retrying login in 3 s");
                        _login.Close(); _login = null; State = Stage.Idle; _retryAt = Time.time + 3f;
                        break;
                    }
                    LastBanCode = code;
                    Fail(Lang.F("The login server disconnected you: {0}", RefusalText.Ban(code)));
                    break;
                }
                default:
                    Debug.Log($"[Session:login] unhandled 0x{id:x4} {PacketTable.NameOf(id)} ({n} bytes)");
                    break;
            }
        }

        // ------------------------------------------------------------------ char server
        private void ConnectChar(CharServerInfo s)
        {
            State = Stage.CharConnecting;
            string host = s.Ip == "0.0.0.0" ? _loginHost : s.Ip;
            _char = new RoConnection("char", host, s.Port, PacketTable.CommonLengths);
            _char.OnPacket += OnCharPacket;
            _char.OnClosed += (c, err) => { if (State < Stage.MapConnecting) Fail(Lang.F("char server closed: {0}", err)); };
            _charAuthed = false;
            try { _char.Connect(); } catch (Exception e) { Fail(Lang.F("cannot reach char server: {0}", e.Message)); return; }
            var w = new PacketWriter(0x0065).U32(AccountId).U32(LoginId1).U32(LoginId2).U16(0).U8(Sex);
            // the echoed AID means chclif_parse_reqtoconnect accepted us: from then on the keep-alive runs
            _char.ExpectRaw(4, raw => { Debug.Log("[Session] char server echoed AID " + BitConverter.ToUInt32(raw, 0)); _charAuthed = true; _nextCharPing = Time.time + CharPingSeconds; });
            _char.Send(w.ToArray());
        }

        private void OnCharPacket(RoConnection c, ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x082d: // HC_ACCEPT_ENTER2: normal.B premium.B billing.B producible.B total.B (chclif_mmo_send082d; producible = sd.char_slots)
                    r.Skip(4); r.U8(); r.U8(); r.U8(); ProducibleSlots = r.U8(); TotalSlots = r.U8();
                    break;
                case 0x028e: // HC_ACK_IS_VALID_CHARNAME result.W (1 = the new name is free and valid; char_clif.cpp:1405-1458)
                {
                    r.Skip(2); int ok = r.U16();
                    Debug.Log($"[Session] rename check: {(ok == 1 ? "name accepted" : "name refused")}");
                    OnRenameCheck?.Invoke(this, ok == 1);
                    break;
                }
                case 0x08fd: // HC_ACK_CHANGE_CHARNAME result.L (0 done; 1-10 char_clif.cpp:1520-1534); on success the list is sent again
                {
                    r.Skip(2); int res = r.S32();
                    Debug.Log($"[Session] rename result {res}");
                    OnRenameResult?.Invoke(this, res, RenameText(res));
                    break;
                }
                case 0x0b70: // HC_ACK_CHANGE_CHARACTER_SLOT len.W reason.W moves.W (0 moved, 1 refused; chclif_moveCharSlotReply); then the list again
                {
                    r.Skip(4); int reason = r.U16(); int moves = r.U16();
                    Debug.Log($"[Session] slot move: {(reason == 0 ? "done" : "refused")}, moves left {moves}");
                    OnSlotMoved?.Invoke(this, reason == 0, moves);
                    break;
                }
                case 0x006b: // HC_ACCEPT_ENTER: characters
                {
                    Debug.Log($"[Session] HC_ACCEPT_ENTER raw ({n} bytes): {RoConnection.Hex(b, o, Math.Min(n, 64))}");
                    r.Skip(4); TotalSlots = r.U8(); PremiumStart = r.U8(); PremiumEnd = r.U8(); r.Skip(20);
                    Characters.Clear();
                    while (r.Remaining >= PacketTable.CharacterInfoSize) Characters.Add(CharInfo.Parse(ref r));
                    Debug.Log($"[Session] {Characters.Count} character(s): {string.Join(", ", Characters.ConvertAll(ch => $"{ch.Name} (slot {ch.Slot}, lv {ch.BaseLevel}/{ch.JobLevel} job {ch.Job} @ {ch.MapName})"))}");
                    State = Stage.CharList;
                    OnCharList?.Invoke(this);
                    break;
                }
                case 0x0b72: // HC_ACK_CHARINFO_PER_PAGE: the answer to CH_CHARLIST_REQ 0x09a1 (the client doesn't send it; 0x006b carries the list)
                {
                    // chclif_mmo_send099d (rA:char/char_clif.cpp:453-471) puts EVERY character in one packet, and repeats it empty when
                    // there were exactly 3 ("the client triggers some finalization code only if count is != 3"): the list is the
                    // packet, the empty repeat is not an empty account
                    r.Skip(4); var page = new List<CharInfo>();
                    while (r.Remaining >= PacketTable.CharacterInfoSize) page.Add(CharInfo.Parse(ref r));
                    bool repeat = page.Count == 0 && _lastPageCount == 3; _lastPageCount = page.Count;
                    if (repeat) break;
                    Characters.Clear(); Characters.AddRange(page);
                    State = Stage.CharList;
                    OnCharList?.Invoke(this);
                    break;
                }
                case 0x08b9: // HC_SECOND_PASSWD_LOGIN seed.L aid.L state.W
                {
                    r.Skip(2); PinSeed = r.U32(); r.U32(); PinState = r.U16();
                    Debug.Log($"[Session] pincode state {PinState} (seed {PinSeed})");
                    OnPinState?.Invoke(this, PinState);
                    break;
                }
                case 0x09a0:
                    break; // HC_CHARLIST_NOTIFY: the list is in 0x006b already
                case 0x020d: // HC_BLOCK_CHARACTER len.W {CID.L unblock_time[20]}: sent after every list, again every 10 s while one is blocked
                {            // (chclif_block_character, rA:char/char_clif.cpp:1480-1518; a blocked slot's select is refused, :936-966)
                    r.Skip(4);
                    foreach (var ch in Characters) ch.BlockedUntil = null;
                    while (r.Remaining >= 24) { uint cid = r.U32(); string until = r.Str(20); var ch = Characters.Find(x => x.CharId == cid); if (ch != null) ch.BlockedUntil = until; }
                    break;
                }
                case 0x0840: // HC_NOTIFY_ACCESSIBLE_MAPNAME len.W {status.L map[16]} (client wave 4)
                {
                    // chclif_accessible_maps (rA:char/char_clif.cpp:1045-1072): the answer to a select when no map server runs, or none holds
                    // the character's map (chclif_parse_charselect :1078-1084, :1141-1149); status 0 = a map server has it. The player may pick
                    // an open one: CH_SELECT_ACCESSIBLE_MAPNAME 0x0841 (chclif_parse_select_accessible_map :923-1043). rB Engine/CharEngine.js:294-306 only shows message 1811.
                    r.Skip(4); var maps = new List<(string map, bool open)>();
                    while (r.Remaining >= 20) { int st = r.S32(); string m = r.Str(16); maps.Add((m, st == 0)); }
                    Debug.Log($"[Session] no map server for the character's map: {maps.Count} accessible map(s) offered: {string.Join(", ", maps.ConvertAll(x => x.map + (x.open ? "" : " (closed)")))}");
                    State = Stage.CharList;
                    OnMapChoice?.Invoke(this, maps);
                    break;
                }
                case 0x006c: // HC_REFUSE_ENTER error.B (chclif_reject, rA:char/char_clif.cpp:1585-1592: always 0)
                {
                    r.Skip(2); int err = r.U8();
                    string text = RefusalText.CharRefused(err);
                    // with the list shown it answers a select (a deleted / blocked / forged slot, char_clif.cpp:1090-1122; the link stays open):
                    // back to the list. Before the list it refuses the connection (not running, the login server doesn't know us: :856, :886).
                    if (State == Stage.CharList || State == Stage.CharSelected) { SelectedChar = null; State = Stage.CharList; Debug.LogWarning("[Session] select refused: " + text); OnSelectRefused?.Invoke(this, text); }
                    else Fail(Lang.F("The character server refused the connection: {0}", text));
                    break;
                }
                case 0x0828: // HC_DELETE_CHAR3_RESERVED charId.L result.L deleteDate.L (chclif_char_delete2_ack, rA:char/char_clif.cpp:522-543)
                {
                    // result 1 scheduled, 0 unknown / already scheduled, 3 database, 4 guild, 5 party; the date is the seconds LEFT for this
                    // PACKETVER (PACKETVER_CHAR_DELETEDATE: delete_date - time(nullptr)), so it is turned into a Unix date here
                    r.Skip(2); uint cid = r.U32(); int res = r.S32(); long left = r.S32();
                    long due = res == 1 ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() + left : 0;
                    var ch = Characters.Find(x => x.CharId == cid); if (ch != null && res == 1) ch.DeleteAt = due;
                    string msg = res == 1 ? Lang.T("Deletion scheduled.") : RefusalText.DeleteReserve(res);
                    Debug.Log($"[Session] delete reserve {cid}: {res}, due in {left} s ({due})");
                    OnDeleteResult?.Invoke(this, cid, due, msg);
                    break;
                }
                case 0x082a: // HC_DELETE_CHAR3 charId.L result.L (chclif_char_delete2_accept_ack, rA:char/char_clif.cpp:545-570; on success the
                {            // list comes first, :557-562): 1 deleted, 2 the server's rules, 3 database, 4 not due yet, 5 the birth date doesn't match
                    r.Skip(2); uint cid = r.U32(); int res = r.S32();
                    string msg = res == 1 ? Lang.T("Character deleted.") : RefusalText.DeleteConfirm(res);
                    if (res == 1) Characters.RemoveAll(ch => ch.CharId == cid);
                    Debug.Log($"[Session] delete {cid}: {res}");
                    OnDeleteResult?.Invoke(this, cid, 0, msg);
                    if (res == 1) OnCharList?.Invoke(this);
                    break;
                }
                case 0x082c: // HC_DELETE_CHAR3_CANCEL charId.L result.L (chclif_char_delete2_cancel_ack, rA:char/char_clif.cpp:571-585: 1 cancelled, 2 database)
                {
                    r.Skip(2); uint cid = r.U32(); int res = r.S32();
                    var ch = Characters.Find(x => x.CharId == cid); if (ch != null && res == 1) ch.DeleteAt = 0;
                    Debug.Log($"[Session] delete cancel {cid}: {res}");
                    OnDeleteResult?.Invoke(this, cid, 0, res == 1 ? Lang.T("Deletion cancelled.") : RefusalText.DeleteCancel(res));
                    break;
                }
                case 0x0b6f: // HC_ACCEPT_MAKECHAR
                {
                    Debug.Log($"[Session] HC_ACCEPT_MAKECHAR raw ({n} bytes): {RoConnection.Hex(b, o, Math.Min(n, 180))}");
                    r.Skip(2);
                    var ch = CharInfo.Parse(ref r);
                    Characters.Add(ch);
                    Debug.Log($"[Session] character created: {ch.Name} slot {ch.Slot}");
                    OnCharList?.Invoke(this);
                    break;
                }
                case 0x006e: // HC_REFUSE_MAKECHAR error.B (chclif_createnewchar_refuse: 0x00 name taken, 0xff denied/invalid, 0x01 underage, 0x03 slot not available)
                {
                    r.Skip(2); int err = r.U8();
                    string msg = CreateRefusedText(err, _lastCreateName);
                    Debug.LogError($"[Session] character creation refused, error {err} (name '{_lastCreateName}': {RoText.Hex(_lastCreateName ?? "")})");
                    OnCreateRefused?.Invoke(this, msg);
                    OnCharList?.Invoke(this);
                    break;
                }
                case 0x0ac5: // HC_NOTIFY_ZONESVR
                {
                    r.Skip(2); uint cid = r.U32(); MapName = r.Str(16); var ip = new IPAddress(r.U32()).ToString(); int port = r.U16(); r.Skip(128);
                    MapHost = ip == "0.0.0.0" ? _loginHost : ip; MapPort = port;
                    Debug.Log($"[Session] zone server for {cid}: {MapName} @ {MapHost}:{MapPort}");
                    State = Stage.CharSelected;
                    _char.Close();
                    ConnectMap();
                    break;
                }
                case 0x0081:
                    r.Skip(2); LastBanCode = r.U8(); Fail(Lang.F("The character server disconnected you: {0}", RefusalText.Ban(LastBanCode)));
                    break;
                default:
                    Debug.Log($"[Session:char] unhandled 0x{id:x4} {PacketTable.NameOf(id)} ({n} bytes)");
                    break;
            }
        }

        public void SelectCharacter(int slot)
        {
            SelectedChar = Characters.Find(ch => ch.Slot == slot);
            _lastSelectSlot = slot;
            _char?.Send(new PacketWriter(PacketTable.Common.CH_SELECT_CHAR).U8((byte)slot).ToArray());
        }

        // ---- client wave 4 (w4account): refused selects and the accessible-map choice
        private int _lastSelectSlot = -1;
        /// <summary>HC_REFUSE_ENTER 0x006c answering a select: the list stays; the text is the client's own (<see cref="RefusalText.CharRefused"/>).</summary>
        public event Action<RoSession, string> OnSelectRefused;
        /// <summary>HC_NOTIFY_ACCESSIBLE_MAPNAME 0x0840: the character's map has no map server; the maps the player may go to instead
        /// (open = a map server has it). Answer with <see cref="SelectAccessibleMap"/>, or pick another character.</summary>
        public event Action<RoSession, List<(string map, bool open)>> OnMapChoice;
        /// <summary>CH_SELECT_ACCESSIBLE_MAPNAME 0x0841 slot.B mapnumber.B (rA:common/packets.hpp:544-549; chclif_parse_select_accessible_map,
        /// rA:char/char_clif.cpp:923-1043: the character goes to that entry of the 0x0840 list, then HC_NOTIFY_ZONESVR as for a select).</summary>
        public void SelectAccessibleMap(int slot, int mapIndex)
        {
            if (slot < 0) slot = _lastSelectSlot;
            SelectedChar = Characters.Find(ch => ch.Slot == slot);
            _char?.Send(new PacketWriter(0x0841).U8((byte)slot).U8((byte)mapIndex).ToArray());
            Debug.Log($"[Session] going to accessible map #{mapIndex} with slot {slot}");
        }
        /// <summary>The slot of the last select (0x0840 answers it).</summary>
        public int LastSelectSlot => _lastSelectSlot;

        /// <param name="job">0 = Novice, 4218 = Summoner (Doram)</param>
        public void CreateCharacter(string name, int slot, int hairStyle = 1, int hairColor = 0, uint job = 0, int sex = -1)
        {
            _lastCreateName = name;
            var w = new PacketWriter(PacketTable.Common.CH_MAKE_CHAR).Str(name, 24).U8((byte)slot).U16((ushort)hairColor).U16((ushort)hairStyle).U32(job).U8((byte)(sex < 0 ? Sex : sex));
            _char.Send(w.ToArray());
        }

        private string _lastCreateName; private int _lastPageCount = -1;

        /// <summary>HC_REFUSE_MAKECHAR 0x006e error.B as chclif_createnewchar_refuse writes it (rA:char/char_clif.cpp:1208-1235):
        /// 0x00 the name exists (-1), 0x01 underage (-3), 0x03 the slot can't be used (-4), 0xFF everything else (-2): creation off
        /// (char_new), a name shorter than char_name_min_length, control characters, a leading '#', a letter outside
        /// char_name_letters (char_name_option 1), a full account or a used slot (rA:char/char.cpp:1334-1462). The packet can't say
        /// which, so the text names what the client can see: letters a stock server refuses — conf/char_athena.conf:155-164 ships
        /// char_name_option 1 with only a-z, A-Z, 0-9 and the space, so every byte ≥ 0x80 (ã, ç, é…) is refused there.</summary>
        public static string CreateRefusedText(int err, string name)
        {
            // client wave 4: the official client's own text first (Net/RefusalText.cs), then what Loki can add for 0xFF
            string client = RefusalText.CreateRefused(err);
            if (err == 0x00 || err == 0x01 || err == 0x03) return client;
            string odd = NameLettersOutsideStock(name);
            if (odd.Length > 0)
                return client + " " + Lang.F("The server refused the name: it doesn't accept {0} in names. Stock rAthena servers take only the letters A–Z and a–z, digits and spaces (its owner can add more in char_name_letters). Try the name without them.", odd);
            return client + " " + Lang.T("The name may be too short or not allowed there, or the account has no free slot.");
        }

        /// <summary>The characters of a name outside stock rAthena's char_name_letters (conf/char_athena.conf:164:
        /// a-z, space, A-Z, 0-9), quoted and listed once each ("'ã', 'ç'"); empty when there are none.</summary>
        public static string NameLettersOutsideStock(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var seen = new List<char>();
            foreach (char c in name)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == ' ') && !seen.Contains(c)) seen.Add(c);
            return string.Join(", ", seen.ConvertAll(c => "'" + c + "'"));
        }

        // ---- character rename and slot move (the official client's "Add-Ons" side bar; CHARACTER_INFO chr_name_changeCnt / chr_slot_changeCnt)
        /// <summary>Slots this account may use (HC_ACCEPT_ENTER2 producible = char_slots): a slot move must stay below it.</summary>
        public int ProducibleSlots;
        public event Action<RoSession, bool> OnRenameCheck;
        public event Action<RoSession, int, string> OnRenameResult;
        public event Action<RoSession, bool, int> OnSlotMoved;
        /// <summary>CH_REQ_IS_VALID_CHARNAME 0x028d AID.L CID.L name[24]: is the new name free (char_check_char_name)? → 0x028e.</summary>
        public void RenameCheck(uint charId, string newName) => _char?.Send(new PacketWriter(PacketTable.Common.CH_REQ_IS_VALID_CHARNAME).U32(AccountId).U32(charId).Str(newName, 24).ToArray());
        /// <summary>CH_REQ_CHANGE_CHARNAME 0x08fc CID.L name[24] (PACKETVER ≥ 20111101 carries the name) → 0x08fd, and on success the list.</summary>
        public void Rename(uint charId, string newName) => _char?.Send(new PacketWriter(PacketTable.Common.CH_REQ_CHANGE_CHARNAME).U32(charId).Str(newName, 24).ToArray());
        /// <summary>CH_REQ_CHANGE_CHARACTER_SLOT 0x08d4 from.W to.W remaining.W → 0x0b70, and on success the list (chclif_parse_moveCharSlot;
        /// a used target slot swaps the two when char_movetoused is on).</summary>
        public void MoveSlot(int from, int to, int remaining) => _char?.Send(new PacketWriter(PacketTable.Common.CH_REQ_CHANGE_CHARACTER_SLOT).U16((ushort)from).U16((ushort)to).U16((ushort)remaining).ToArray());
        /// <summary>HC_ACK_CHANGE_CHARNAME results, as char_clif.cpp:1520-1534 lists them.</summary>
        public static string RenameText(int res)
        {
            switch (res)
            {
                case 0: return Lang.T("The character was renamed.");
                case 1: return Lang.T("This character's name has already been changed.");
                case 2: return Lang.T("Rename failed: the character information is not correct.");
                case 3: return Lang.T("Rename failed.");
                case 4: return Lang.T("Another character already uses that name.");
                case 5: return Lang.T("Leave the guild before renaming.");
                case 6: return Lang.T("Leave the party before renaming.");
                case 7: return Lang.T("That name is too long.");
                case 8: return Lang.T("That name has characters that aren't allowed.");
                case 9: return Lang.T("Renaming isn't allowed.");
                default: return Lang.F("Rename failed ({0}).", res);
            }
        }

        /// <summary>CH_DELETE_CHAR3_RESERVED 0x0827 charId.L: schedules the deletion (char_del_delay, 24 h by default).</summary>
        public void DeleteReserve(uint charId) => _char?.Send(new PacketWriter(0x0827).U32(charId).ToArray());
        /// <summary>CH_DELETE_CHAR3 0x0829 charId.L birthdate[6] "YYMMDD": deletes once the reservation is due.
        /// Client wave 4 (w4account): the six bytes go as they are, with no terminator (PacketWriter.Str(s, 6) kept a byte for a NUL
        /// and sent five digits), and an account without a birth date needs six NUL bytes: chclif_parse_char_delete2_accept builds
        /// "YY-MM-DD" from them and chclif_delchar_check accepts an empty stored birth date only with an empty code (rA:char/
        /// char_clif.cpp:697-716, :672-694). _M/_F registration stores none (login.cpp:260 birthdate ""; NULL in SQL, account.cpp:540
        /// reads it back as ""), so the old "000000" answered 5 ("the birth date doesn't match") on every Loki-made account.</summary>
        public void DeleteConfirm(uint charId, string birthdate)
        {
            var code = new byte[6]; string b = (birthdate ?? "").Trim();
            if (b.Length == 6) for (int i = 0; i < 6; i++) code[i] = (byte)b[i];
            _char?.Send(new PacketWriter(0x0829).U32(charId).Bytes(code).ToArray());
        }
        /// <summary>CH_DELETE_CHAR3_CANCEL 0x082b charId.L.</summary>
        public void DeleteCancel(uint charId) => _char?.Send(new PacketWriter(0x082b).U32(charId).ToArray());

        // ---- PIN code (char.cpp char_pincode_decrypt) ----
        /// <summary>The server's digit table for a seed: it maps each received digit d to tab[d]. The client must therefore send,
        /// for each real digit, the index whose tab entry equals it (the inverse permutation).</summary>
        public static byte[] PinTable(uint seed)
        {
            var tab = new byte[10]; for (int i = 0; i < 10; i++) tab[i] = (byte)i;
            for (int i = 1; i < 10; i++)
            {
                unchecked { seed = 0x881234u + seed * 0x3498u; }
                int pos = (int)(seed % (uint)(i + 1));
                if (i != pos) { byte t = tab[i]; tab[i] = tab[pos]; tab[pos] = t; }
            }
            return tab;
        }
        public static string ScramblePin(uint seed, string pin)
        {
            var tab = PinTable(seed);
            var sb = new System.Text.StringBuilder();
            foreach (char c in pin)
            {
                int real = c - '0', send = 0;
                for (int d = 0; d < 10; d++) if (tab[d] == real) { send = d; break; }
                sb.Append((char)('0' + send));
            }
            return sb.ToString();
        }
        /// <summary>The four scrambled digits as the four bytes of a pin[4] field. The field has no terminator
        /// (rA:common/packets.hpp:551-579 char pin[4]; char_pincode_decrypt needs exactly PINCODE_LENGTH digits, rA:char/char.cpp:2153-2165):
        /// PacketWriter.Str(s, 4) keeps a byte for a NUL and sent three digits, so every PIN packet failed and the char server closed
        /// the link (set_eof) — the first live run against pincode_enabled: yes found it (smoke-w3login-pin.log, 2026-09-30).</summary>
        private static byte[] PinField(uint seed, string pin)
        {
            string s = ScramblePin(seed, pin ?? ""); var b = new byte[4];
            for (int i = 0; i < 4; i++) b[i] = (byte)(i < s.Length ? s[i] : '0');
            return b;
        }
        /// <summary>CH_SECOND_PASSWD_ACK 0x08b8 aid.L pin[4].</summary>
        public void PinCheck(string pin) { _char?.Send(new PacketWriter(0x08b8).U32(AccountId).Bytes(PinField(PinSeed, pin)).ToArray()); Debug.Log("[Session] pin sent"); }
        /// <summary>CH_MAKE_SECOND_PASSWD 0x08ba aid.L pin[4].</summary>
        public void PinCreate(string pin) { _char?.Send(new PacketWriter(0x08ba).U32(AccountId).Bytes(PinField(PinSeed, pin)).ToArray()); Debug.Log("[Session] new pin sent"); }
        /// <summary>CH_EDIT_SECOND_PASSWD 0x08be aid.L old[4] new[4].</summary>
        public void PinChange(string oldPin, string newPin) { _char?.Send(new PacketWriter(0x08be).U32(AccountId).Bytes(PinField(PinSeed, oldPin)).Bytes(PinField(PinSeed, newPin)).ToArray()); Debug.Log("[Session] pin change sent"); }
        /// <summary>The server allows selecting a character without a PIN when none is set (char_clif.cpp:1683); "skip" is that.</summary>
        public void PinSkip() { PinState = 0; OnPinState?.Invoke(this, 0); }

        // ------------------------------------------------------------------ map server
        /// <summary>True while the map link is Loki Online's (SpacetimeDB) rather than an rAthena socket.</summary>
        public bool OnlineLink => _map != null && _map.IsVirtual;

        /// <summary>Loki Online: the character picked on the SpacetimeDB character screen enters the world over a virtual map link.
        /// Everything after this is the rAthena map protocol (CZ_ENTER, ZC_ACCEPT_ENTER, LoadEndAck, …), so the game layer is the
        /// same for both servers. <paramref name="send"/> carries packets to the server, <paramref name="close"/> closes the link;
        /// the returned connection receives the server's bytes through <see cref="RoConnection.Feed"/>.</summary>
        public RoConnection EnterWorldOnline(CharInfo c, uint accountId, byte sex, Action<byte[]> send, Action close)
        {
            _login?.Close(); _char?.Close(); _map?.Close(); _login = _char = _map = null;
            FailReason = null; Leaving = Leave.None;
            SelectedChar = c; AccountId = accountId; Sex = sex; LoginId1 = 0; LoginId2 = 0;
            MapName = c.MapName; MapHost = "spacetimedb"; MapPort = 0;
            State = Stage.MapConnecting;
            _map = new RoConnection("map", PacketTable.MapLengths, send, close);
            _map.OnPacket += OnMapPacketInternal;
            _map.OnClosed += (conn, err) => { if (Leaving != Leave.None) { Debug.Log("[Session] map link closed while leaving (" + Leaving + ")"); return; } Fail(Lang.F("map link closed: {0}", err)); };
            _map.Connect();
            // CZ_ENTER (0x0436): the server checks the account and character against this client's SpacetimeDB login
            var w = new PacketWriter(0x0436).U32(AccountId).U32(c.CharId).U32(LoginId1).U32((uint)Environment.TickCount).Zero(4).U8(Sex);
            _map.Send(w.ToArray());
            State = Stage.MapEntering;
            Debug.Log($"[Session] entering the world on Loki Online as {c.Name} ({c.CharId}), account {AccountId}");
            return _map;
        }

        private void ConnectMap()
        {
            State = Stage.MapConnecting;
            _map = new RoConnection("map", MapHost, MapPort, PacketTable.MapLengths);
            _map.OnPacket += OnMapPacketInternal;
            _map.OnClosed += (c, err) => { if (Leaving != Leave.None) { Debug.Log("[Session] map connection closed while leaving (" + Leaving + ")"); return; } Fail(Lang.F("map server closed: {0}", err)); };
            try { _map.Connect(); } catch (Exception e) { Fail(Lang.F("cannot reach map server: {0}", e.Message)); return; }
            // CZ_ENTER (0x0436, 23 bytes for 20211103): AID, CID, login_id1, client_tick, 4 bytes unknown, sex
            var w = new PacketWriter(0x0436).U32(AccountId).U32(SelectedChar.CharId).U32(LoginId1).U32((uint)Environment.TickCount).Zero(4).U8(Sex);
            // PACKETVER >= 20070521: the map server answers with packet 0x0283 (ZC_AID, 6 bytes), not 4 raw bytes.
            _map.Send(w.ToArray());
            State = Stage.MapEntering;
        }

        private void OnMapPacketInternal(RoConnection c, ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x02eb: // ZC_ACCEPT_ENTER
                {
                    r.Skip(2); ServerTick = r.U32(); var (x, y, d) = r.Pos3(); StartX = x; StartY = y; StartDir = d;
                    Debug.Log($"[Session] entered {MapName} at {x},{y} dir {d}");
                    State = Stage.InGame;
                    _map.Send(new PacketWriter(0x007d).ToArray()); // CZ_NOTIFY_ACTORINIT (LoadEndAck)
                    _nextTick = Time.time + 10f;
                    Loki.Sprites.ServerClock.OnEnter(ServerTick); // client wave 3 (w3move): a first server-clock sample (clif_authok startTime = gettick())
                    OnEnteredMap?.Invoke(this);
                    break;
                }
                case 0x0283: r.Skip(2); Debug.Log("[Session] map server acknowledged AID " + r.U32()); break;
                case 0x0074: r.Skip(2); Fail(Lang.F("The map server refused entry: {0}", RefusalText.MapRefused(r.U8()))); break;
                // SC_NOTIFY_BAN from the map server (clif_authfail_fd, rA:map/clif.cpp:825-838): the client's own text, and the code for the
                // game's reconnect decision (15 is also every player's code when the map server shuts down: rA:map/map.cpp:5315-5324)
                case 0x0081: r.Skip(2); LastBanCode = r.U8(); Fail(Lang.F("The map server disconnected you: {0}", RefusalText.Ban(LastBanCode))); break;
                case 0x0187: break; // PING
                case 0x00b3: // ZC_RESTART_ACK type.B — 1: the char server holds a re-entry ticket for us (clif_charselectok after chrif 0x2b03)
                {
                    r.Skip(2); int type = r.U8();
                    Debug.Log($"[Session] restart ack {type} (leaving: {Leaving})");
                    if (type == 1 && Leaving == Leave.CharSelect)
                    {
                        PendingCharSelect = new CharSelectTicket { LoginHost = _loginHost, LoginPort = _loginPort, User = _user, Pass = _pass, AccountId = AccountId, LoginId1 = LoginId1, LoginId2 = LoginId2, Sex = Sex, Server = CharServers.Count > 0 ? CharServers[0] : null, Online = OnlineLink };
                        OnLeaveAccepted?.Invoke(this, Leave.CharSelect);
                    }
                    break;
                }
                case 0x018b: // ZC_ACK_REQ_DISCONNECT result.W — 0: you may go (quit); 1: not yet (clif_disconnect_ack; also the refusal of CZ_RESTART 1)
                {
                    r.Skip(2); int result = r.U16();
                    Debug.Log($"[Session] disconnect ack {result} (leaving: {Leaving})");
                    if (result == 0 && Leaving == Leave.Quit) OnLeaveAccepted?.Invoke(this, Leave.Quit);
                    else if (result != 0) { var was = Leaving; Leaving = Leave.None; OnLeaveRefused?.Invoke(this, was); }
                    break;
                }
                default:
                    OnMapPacket?.Invoke(this, id, b, o, n);
                    break;
            }
        }

        public void SendPacket(byte[] p) => _map?.Send(p);
        public void BeginPacketBatch() => _map?.BeginBatch();
        public void EndPacketBatch() => _map?.EndBatch();

        // ------------------------------------------------------------------ leaving the game
        public enum Leave { None, CharSelect, Quit }
        /// <summary>What we asked the map server for; while not None a closing map connection is expected, not a failure.</summary>
        public Leave Leaving { get; private set; }
        /// <summary>The map server agreed: CharSelect (0x00b3 {1}, <see cref="PendingCharSelect"/> is set) or Quit (0x018b {0}).</summary>
        public event Action<RoSession, Leave> OnLeaveAccepted;
        /// <summary>0x018b {1}: not now — rAthena's prevent_logout keeps you in for 10 s after attacking, casting or being hit.</summary>
        public event Action<RoSession, Leave> OnLeaveRefused;

        /// <summary>What a character-select re-entry needs after the scene reloads: CH_ENTER with the same account and login ids finds
        /// the auth node the char server stored when the map server forwarded our CZ_RESTART (chmapif_parse_authok, pincode_success set,
        /// so no PIN is asked again) — chclif_parse_reqtoconnect.</summary>
        public sealed class CharSelectTicket
        {
            public string LoginHost, User, Pass; public int LoginPort;
            public uint AccountId, LoginId1, LoginId2; public byte Sex; public CharServerInfo Server;
            /// <summary>Loki Online: the SpacetimeDB connection stays open across the scene reload and already shows the character list.</summary>
            public bool Online;
        }
        /// <summary>Set by an accepted character select; the next game instance (after the scene reload) takes it and resumes there.</summary>
        public static CharSelectTicket PendingCharSelect;

        /// <summary>CZ_RESTART {1} (0x00b2): back to the character list. The map server asks the char server for a ticket (chrif_charselectreq
        /// 0x2b02) and answers ZC_RESTART_ACK {1}; or 0x018b {1} while prevent_logout runs (clif.cpp clif_parse_Restart).</summary>
        public void RequestCharSelect() { if (State != Stage.InGame) return; Leaving = Leave.CharSelect; Restart(1); Debug.Log("[Session] asking for character select"); }
        /// <summary>CZ_REQ_DISCONNECT {type.W 0} (0x018a): answered by 0x018b {0} (saved; the server may drop the connection) or {1}
        /// (clif.cpp clif_parse_QuitGame).</summary>
        public void RequestQuit() { if (State != Stage.InGame) return; Leaving = Leave.Quit; _map?.Send(new PacketWriter(0x018a).U16(0).ToArray()); Debug.Log("[Session] asking to disconnect"); }

        /// <summary>Gives up waiting for an answer to a leave request.</summary>
        public void CancelLeave() => Leaving = Leave.None;

        /// <summary>Continues a character select in a fresh session: straight to the char server with the ticket's ids.</summary>
        public void ResumeCharSelect(CharSelectTicket t)
        {
            Reset();
            _loginHost = t.LoginHost; _loginPort = t.LoginPort; _user = t.User ?? ""; _pass = t.Pass ?? "";
            AccountId = t.AccountId; LoginId1 = t.LoginId1; LoginId2 = t.LoginId2; Sex = t.Sex;
            if (t.Server == null) { Fail(Lang.T("no char server to return to")); return; }
            CharServers.Add(t.Server);
            Debug.Log($"[Session] back to the character list on {t.Server.Name} (AID {AccountId})");
            ConnectChar(t.Server);
        }

        /// <summary>CZ_REQUEST_MOVE2 (0x035f): walk to a cell.</summary>
        public void WalkTo(int x, int y)
        {
            var w = new PacketWriter(0x035f);
            w.U8((byte)(x >> 2)); w.U8((byte)(((x & 3) << 6) | ((y >> 4) & 0x3F))); w.U8((byte)(((y & 0xF) << 4)));
            _map?.Send(w.ToArray());
        }

        /// <summary>CZ_REQUEST_ACT2 (0x0437): action 0 = attack once, 7 = continuous attack, 2 = sit, 3 = stand.</summary>
        public void Action(uint targetId, byte action) => _map?.Send(new PacketWriter(0x0437).U32(targetId).U8(action).ToArray());

        // ---- client → map server requests. Ids/offsets from rAthena clif_packetdb.hpp (final entries for PACKETVER 20211103). ----

        /// <summary>Attack a monster continuously (CZ_REQUEST_ACT2 action 7 = DMG_REPEAT); the server walks into range itself (unit_attack).</summary>
        public void Attack(uint targetId) => Action(targetId, 7);
        /// <summary>CZ_CANCEL_LOCKON 0x0118: stop the continuous attack (clif_parse_StopAttack).</summary>
        public void StopAttack() => _map?.Send(new PacketWriter(0x0118).ToArray());
        public void Sit() => Action(0, 2);
        public void Stand() => Action(0, 3);
        /// <summary>CZ_CONTACTNPC (0x0090): id.L, type.B (1 = click).</summary>
        public void ContactNpc(uint npcId) => _map?.Send(new PacketWriter(0x0090).U32(npcId).U8(1).ToArray());
        /// <summary>CZ_REQ_NEXT_SCRIPT (0x00b9): npc id.L.</summary>
        public void NpcNext(uint npcId) => _map?.Send(new PacketWriter(0x00b9).U32(npcId).ToArray());
        /// <summary>CZ_CHOOSE_MENU (0x00b8): npc id.L, choice.B — 1-based, 0xff = cancel (clif_parse_NpcSelectMenu).</summary>
        public void NpcMenu(uint npcId, byte choice) => _map?.Send(new PacketWriter(0x00b8).U32(npcId).U8(choice).ToArray());
        /// <summary>CZ_CLOSE_DIALOG (0x0146): npc id.L.</summary>
        public void NpcClose(uint npcId) => _map?.Send(new PacketWriter(0x0146).U32(npcId).ToArray());
        /// <summary>CZ_INPUT_EDITDLG (0x0143): npc id.L, value.L.</summary>
        public void NpcInputNumber(uint npcId, int value) => _map?.Send(new PacketWriter(0x0143).U32(npcId).S32(value).ToArray());
        /// <summary>CZ_INPUT_EDITDLGSTR (0x01d5): len.W, npc id.L, text (no terminator for PACKETVER ≥ 20151001, the server adds one).</summary>
        public void NpcInputString(uint npcId, string text)
        {
            var bytes = RoText.Encode(text ?? ""); // cp1252 (RoText): the server stores the bytes as they are
            _map?.Send(new PacketWriter(0x01d5).U16(0).U32(npcId).Bytes(bytes).ToArrayWithLength());
        }
        /// <summary>CZ_RESTART (0x00b2): 0 = respawn at the save point, 1 = back to character select.</summary>
        public void Restart(byte type = 0) => _map?.Send(new PacketWriter(0x00b2).U8(type).ToArray());
        /// <summary>Local Time.time when the player last sent CZ_ITEM_THROW (0x0363), used to avoid auto-picking up a self-dropped item before stepping off.</summary>
        public static float LastSelfDropAt = -10f;
        /// <summary>CZ_ITEM_PICKUP2 (0x0362): floor item id.L. Server requires ≤ 2 cells distance (pc_takeitem).</summary>
        public void TakeItem(uint floorItemId) => _map?.Send(new PacketWriter(0x0362).U32(floorItemId).ToArray());
        /// <summary>CZ_ITEM_THROW (0x0363, rAthena parseable_packet(0x0363,6,clif_parse_DropItem,2,4)): client index.W, amount.W.</summary>
        public void DropItem(int clientIndex, int amount) { LastSelfDropAt = UnityEngine.Time.time; _map?.Send(new PacketWriter(0x0363).U16((ushort)clientIndex).U16((ushort)amount).ToArray()); }
        /// <summary>CZ_REQ_EMOTION 0x00bf type.B (clif_parse_Emotion: needs Basic Skill 2, 1 per second, ids &lt; 88; dice are re-rolled by the server).</summary>
        public void Emotion(int type) => _map?.Send(new PacketWriter(0x00bf).U8((byte)type).ToArray());
        /// <summary>CZ_SHORTCUT_KEY_CHANGE2 0x0b21 tab.W index.W isSkill.B id.L count.W — stored verbatim in hotkeys[index + tab*38], no ack
        /// (clif_parse_Hotkey, research/qol_packets.md §5). isSkill 0 + id 0 clears a slot.</summary>
        public void SetHotkey(int tab, int index, bool isSkill, int id, int count) =>
            _map?.Send(new PacketWriter(0x0b21).U16((ushort)tab).U16((ushort)index).U8((byte)(isSkill ? 1 : 0)).U32((uint)id).U16((ushort)count).ToArray());
        /// <summary>CZ_SHORTCUTKEYBAR_ROTATE2 0x0b22 tab.W rowshift.B.</summary>
        public void HotkeyRowShift(int tab, int shift) => _map?.Send(new PacketWriter(0x0b22).U16((ushort)tab).U8((byte)shift).ToArray());
        /// <summary>Client wave 13 (w13items): use / equip / take-off requests sent so far (the bag's lock sends none on its first pull: smoke, log).</summary>
        public int ItemRequestsSent { get; private set; }
        /// <summary>CZ_USE_ITEM2 (0x0439): client index.W, AID.L.</summary>
        public void UseItem(int clientIndex) { ItemRequestsSent++; _map?.Send(new PacketWriter(0x0439).U16((ushort)clientIndex).U32(AccountId).ToArray()); }
        /// <summary>CZ_REQ_WEAR_EQUIP_V5 (0x0998): client index.W, position.L (the item's equip location mask).</summary>
        public void Equip(int clientIndex, uint location) { ItemRequestsSent++; _map?.Send(new PacketWriter(0x0998).U16((ushort)clientIndex).U32(location).ToArray()); }
        /// <summary>CZ_REQ_TAKEOFF_EQUIP (0x00ab): client index.W.</summary>
        public void Unequip(int clientIndex) { ItemRequestsSent++; _map?.Send(new PacketWriter(0x00ab).U16((ushort)clientIndex).ToArray()); }

        /// <summary>CZ_STATUS_CHANGE (0x00bb): stat id.W (13 STR … 18 LUK), amount.B (points to add; the server caps it).</summary>
        public void StatusUp(int statIndex0to5, int amount = 1) => _map?.Send(new PacketWriter(0x00bb).U16((ushort)(13 + statIndex0to5)).U8((byte)amount).ToArray());
        /// <summary>CZ_ADVANCED_STATUS_CHANGE (0x0b24): trait type.W (219 POW … 224 CRT), amount.W (clif_parse_traitstatus_up → pc_traitstatusup).</summary>
        public void TraitUp(int type, int amount = 1) => _map?.Send(new PacketWriter(0x0b24).U16((ushort)type).U16((ushort)amount).ToArray());
        /// <summary>CZ_REMEMBER_WARPPOINT (0x011d): memo the current map for Warp Portal (the official client's /memo; pc_memo → 0x011e).</summary>
        public void RememberWarpPoint() => _map?.Send(new PacketWriter(0x011d).ToArray());
        /// <summary>CZ_UPGRADE_SKILLLEVEL (0x0112): skill id.W.</summary>
        public void SkillUp(int skillId) => _map?.Send(new PacketWriter(0x0112).U16((ushort)skillId).ToArray());
        /// <summary>CZ_USE_SKILL2 (0x0438): level.W skill id.W target.L — attack, self, support and trap skills.</summary>
        /// <summary>The last skill we asked to use (crafting lists do not say which skill opened them).</summary>
        public int LastSkillId { get; private set; }
        /// <summary>Client wave 12 (w12cast): how many skill requests (0x0438 and 0x0366) this session has sent (the smoke counts
        /// that a called-off cast sends none).</summary>
        public int SkillRequestsSent { get; private set; }
        public void UseSkill(int skillId, int level, uint target) { LastSkillId = skillId; SkillRequestsSent++; _map?.Send(new PacketWriter(0x0438).U16((ushort)level).U16((ushort)skillId).U32(target).ToArray()); }
        /// <summary>CZ_USE_SKILL_TOGROUND2 (0x0366): level.W skill id.W x.W y.W — ground skills (inf & 2).</summary>
        public void UseSkillOnGround(int skillId, int level, int x, int y) => UseSkillOnGroundRaw(skillId, level, x, y);
        private void UseSkillOnGroundRaw(int skillId, int level, int x, int y) { SkillRequestsSent++; _map?.Send(new PacketWriter(0x0366).U16((ushort)level).U16((ushort)skillId).U16((ushort)x).U16((ushort)y).ToArray()); }

        // ---- shops (research/shops_storage_packets.md; rAthena e985006, PACKETVER 20211103) ----
        /// <summary>CZ_ACK_SELECT_DEALTYPE 0x00c5: npcId.L type.B (0 buy, 1 sell).</summary>
        public void ShopDealType(uint npcId, bool sell) => _map?.Send(new PacketWriter(0x00c5).U32(npcId).U8((byte)(sell ? 1 : 0)).ToArray());
        /// <summary>CZ_PC_PURCHASE_ITEMLIST 0x00c8: len.W {amount.W itemId.L}.</summary>
        public void ShopBuy(IList<(int itemId, int amount)> items)
        {
            var w = new PacketWriter(0x00c8).U16(0);
            foreach (var (id, amt) in items) w.U16((ushort)amt).U32((uint)id);
            _map?.Send(w.ToArrayWithLength());
        }
        /// <summary>CZ_PC_SELL_ITEMLIST 0x00c9: len.W {inventory client index.W amount.W}.</summary>
        public void ShopSell(IList<(int index, int amount)> items)
        {
            var w = new PacketWriter(0x00c9).U16(0);
            foreach (var (idx, amt) in items) w.U16((ushort)idx).U16((ushort)amt);
            _map?.Send(w.ToArrayWithLength());
        }
        /// <summary>CZ_NPC_TRADE_QUIT 0x09d4: releases a plain shop (sd->npc_shopid). Harmless when nothing is open.</summary>
        public void ShopClose() => _map?.Send(new PacketWriter(0x09d4).ToArray());
        /// <summary>CZ_NPC_MARKET_PURCHASE 0x09d6: len.W {itemId.L qty.L}.</summary>
        public void MarketBuy(IList<(int itemId, int amount)> items)
        {
            var w = new PacketWriter(0x09d6).U16(0);
            foreach (var (id, amt) in items) w.U32((uint)id).U32((uint)amt);
            _map?.Send(w.ToArrayWithLength());
        }
        /// <summary>CZ_NPC_MARKET_CLOSE 0x09d8: the only thing that clears state.trading after a market opened.</summary>
        public void MarketClose() => _map?.Send(new PacketWriter(0x09d8).ToArray());
        /// <summary>CZ_NPC_BARTER_MARKET_CLOSE 0x0b12 / CZ_NPC_EXPANDED_BARTER_MARKET_CLOSE 0x0b58.</summary>
        public void BarterClose(bool expanded) => _map?.Send(new PacketWriter((ushort)(expanded ? 0x0b58 : 0x0b12)).ToArray());
        /// <summary>CZ_NPC_BARTER_MARKET_PURCHASE 0x0b0f: len.W {itemId.L amount.L invIndex.W shopIndex.L} (packets_struct.hpp:3932-3948).
        /// rAthena only reads amount and shopIndex (clif.cpp:23310-23346); invIndex is what the client passes for the currency stack.</summary>
        public void BarterBuy(IList<(int itemId, int amount, int invIndex, int shopIndex)> items)
        {
            var w = new PacketWriter(0x0b0f).U16(0);
            foreach (var (id, amt, inv, shop) in items) w.U32((uint)id).U32((uint)amt).U16((ushort)inv).U32((uint)shop);
            _map?.Send(w.ToArrayWithLength());
        }
        /// <summary>CZ_NPC_EXPANDED_BARTER_MARKET_PURCHASE 0x0b57: len.W {itemId.L shopIndex.L amount.L} (packets_struct.hpp:4513-4528).</summary>
        public void BarterExpandedBuy(IList<(int itemId, int shopIndex, int amount)> items)
        {
            var w = new PacketWriter(0x0b57).U16(0);
            foreach (var (id, shop, amt) in items) w.U32((uint)id).U32((uint)shop).U32((uint)amt);
            _map?.Send(w.ToArrayWithLength());
        }

        // ---- Kafra storage ----
        /// <summary>CZ_MOVE_ITEM_FROM_BODY_TO_STORE2 0x0364: inventory client index.W amount.L.</summary>
        public void StorageAdd(int invIndex, int amount) => _map?.Send(new PacketWriter(0x0364).U16((ushort)invIndex).U32((uint)amount).ToArray());
        /// <summary>CZ_MOVE_ITEM_FROM_STORE_TO_BODY2 0x0365: storage client index.W amount.L.</summary>
        public void StorageGet(int storageIndex, int amount) => _map?.Send(new PacketWriter(0x0365).U16((ushort)storageIndex).U32((uint)amount).ToArray());
        /// <summary>CZ_CLOSE_STORE 0x0193 (not 0x00f7, which is a take-item variant in this build).</summary>
        public void StorageClose() => _map?.Send(new PacketWriter(0x0193).ToArray());

        // ---- skill / item menus (research/menus_items_msgs.md) ----
        /// <summary>CZ_SELECT_WARPPOINT 0x011b: skillId.W mapName[16] ("cancel" cancels).</summary>
        public void SelectWarpPoint(int skillId, string map) => _map?.Send(new PacketWriter(0x011b).U16((ushort)skillId).Str(map ?? "cancel", 16).ToArray());
        /// <summary>CZ_REQ_ITEMIDENTIFY 0x0178: inventory client index.W, 0xFFFF cancels.</summary>
        public void Identify(int invIndex) => _map?.Send(new PacketWriter(0x0178).U16((ushort)invIndex).ToArray());
        /// <summary>CZ_REQ_ITEMCOMPOSITION_LIST 0x017a: card client index.W (using a card through 0x0439 fails).</summary>
        public void CardList(int cardIndex) => _map?.Send(new PacketWriter(0x017a).U16((ushort)cardIndex).ToArray());
        /// <summary>CZ_REQ_ITEMCOMPOSITION 0x017c: card client index.W, equipment client index.W.</summary>
        public void InsertCard(int cardIndex, int equipIndex) => _map?.Send(new PacketWriter(0x017c).U16((ushort)cardIndex).U16((ushort)equipIndex).ToArray());

        /// <summary>CZ_WHISPER 0x0096: len.W name[24] message (no terminator for PACKETVER ≥ 20151001; clif_process_message whisperFormat).</summary>
        public void Whisper(string targetName, string message)
        {
            var bytes = RoText.Encode(message ?? ""); // the name goes through Str (RoText.Encode): the same bytes the server sent us
            _map?.Send(new PacketWriter(0x0096).U16(0).Str(targetName, 24).Bytes(bytes).ToArrayWithLength());
        }
        /// <summary>CZ_REQUEST_CHAT_PARTY 0x0108 / CZ_GUILD_CHAT 0x017e: len.W "Name : message" (same validator as global chat).</summary>
        public void PartyChat(string charName, string message) { string t = charName + " : " + message; _map?.Send(new PacketWriter(0x0108).U16(0).Str(t, t.Length + 1).ToArrayWithLength()); }
        public void GuildChat(string charName, string message) { string t = charName + " : " + message; _map?.Send(new PacketWriter(0x017e).U16(0).Str(t, t.Length + 1).ToArrayWithLength()); }
        /// <summary>CZ_ADD_FRIENDS 0x0202 name[24] (clif_parse_FriendsListAdd: the target must be online and gets 0x0207 to answer).</summary>
        public void FriendAdd(string name) => _map?.Send(new PacketWriter(0x0202).Str(name, 24).ToArray());
        /// <summary>CZ_DELETE_FRIENDS 0x0203 aid.L cid.L.</summary>
        public void FriendRemove(uint aid, uint cid) => _map?.Send(new PacketWriter(0x0203).U32(aid).U32(cid).ToArray());
        /// <summary>CZ_ACK_REQ_ADD_FRIENDS 0x0208 aid.L cid.L reply.L (14 bytes on this build; 1 = accept, 0 = reject).</summary>
        public void FriendReply(uint aid, uint cid, bool accept) => _map?.Send(new PacketWriter(0x0208).U32(aid).U32(cid).U32(accept ? 1u : 0u).ToArray());

        /// <summary>CZ_REQUEST_CHAT (0x008c... for this version see table): "Name : message". One byte per character through
        /// PacketWriter.Str (RoText.Encode): the name is the Latin-1 string the server sent, so its bytes come back exactly — the
        /// prefix check is byte for byte (rA:map/clif.cpp:10499-10508) — and the message is cp1252.</summary>
        public void Chat(string charName, string message)
        {
            string text = charName + " : " + message;
            var w = new PacketWriter(0x00f3).U16(0).Str(text, text.Length + 1); // CZ_REQUEST_CHAT is 0x00f3 for >20180307 (GlobalMessage)
            _map?.Send(w.ToArrayWithLength());
        }
    }
}
