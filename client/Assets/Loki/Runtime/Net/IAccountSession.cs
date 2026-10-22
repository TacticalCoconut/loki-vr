using System;
using System.Collections.Generic;

namespace Loki.Net
{
    /// <summary>
    /// What the login and character screens need from a server connection. Two implementations: <see cref="RoAccountSession"/>
    /// (rAthena's login + char servers through <see cref="RoSession"/>) and <see cref="Stdb.StdbSession"/> (the Loki Online
    /// SpacetimeDB module). The login panel talks to whichever the chosen server entry names, and only through this interface:
    /// what one server can do and the other can't is a capability flag (<see cref="CanRename"/>, <see cref="CanMoveSlot"/>,
    /// <see cref="CanResume"/>), never a cast (re-audit 2026-09-29 §3).
    /// </summary>
    public interface IAccountSession
    {
        RoSession.Stage State { get; }
        string FailReason { get; }
        /// <summary>-1 unknown yet; 0 / 7 passed; other values are rAthena's HC_SECOND_PASSWD_LOGIN states (SpacetimeDB never asks for a PIN).</summary>
        int PinState { get; }
        bool PinPending { get; }
        List<CharInfo> Characters { get; }
        int TotalSlots { get; }
        /// <summary>Slots this account may fill (a slot move must stay below it); <see cref="TotalSlots"/> when the server says no more.</summary>
        int UsableSlots { get; }
        /// <summary>The account's sex: 0 female, 1 male.</summary>
        byte Sex { get; }
        bool RegisterAccount { get; set; }
        bool AutoRegister { get; set; }
        char RegisterSex { get; set; }

        /// <summary>The server can rename characters at all (each character then says whether it may: <see cref="CharInfo.NameChangeCount"/>).</summary>
        bool CanRename { get; }
        /// <summary>The server can move characters between slots (each character's <see cref="CharInfo.SlotChangeCount"/> says how often).</summary>
        bool CanMoveSlot { get; }
        /// <summary>The server can log this PC in again without the password (<see cref="Resume"/>, <see cref="HasSavedLogin"/>).</summary>
        bool CanResume { get; }
        /// <summary>Wave H: the server can change the logged-in account's password (<see cref="ChangePassword"/>). Loki Online can;
        /// rAthena has no packet for it.</summary>
        bool CanChangePassword { get; }
        /// <summary>Wave H: what this server accepts for a new account, for the login screen's hints and checks: the account name's
        /// length and the password's minimum.</summary>
        int AccountNameMin { get; }
        int AccountNameMax { get; }
        int PasswordMinLength { get; }
        /// <summary>Client wave 4: the last SC_NOTIFY_BAN code a server sent this session (-1 none): the login screen reconnects by itself
        /// only after a code that allows it (<see cref="RefusalText.MayReconnect"/>). Loki Online: -1.</summary>
        int LastBanCode { get; }
        /// <summary>Client wave 4: a new account's password leaves this PC hashed (Loki Online) rather than as it was typed (rAthena's
        /// _M/_F registration is a plain CA_LOGIN: rA:login/login.cpp:323-339). The login screen warns before a plain one to another PC.</summary>
        bool RegisterHashed { get; }

        event Action<int> PinStateChanged;
        event Action<int, string> LoginRefused;
        event Action<string> Failed;
        event Action EnteredMap;
        event Action<uint, long, string> DeleteResult;
        event Action<string> CreateRefused;
        event Action CharListChanged;
        /// <summary>A line for the status bar that is neither a failure nor a refusal.</summary>
        event Action<string> Notice;
        /// <summary>Answer to <see cref="RenameCheck"/>: the new name is free and allowed, and a text for the player.</summary>
        event Action<bool, string> RenameChecked;
        /// <summary>Answer to <see cref="Rename"/>: 0 done, otherwise the server's code; and a text for the player.</summary>
        event Action<int, string> RenameResult;
        /// <summary>Answer to <see cref="MoveSlot"/>: moved or not, and the moves left.</summary>
        event Action<bool, int> SlotMoved;
        /// <summary>Client wave 4: a <see cref="SelectCharacter"/> was refused; the list stays (the text is the client's own).</summary>
        event Action<string> SelectRefused;
        /// <summary>Client wave 4: the selected character's map has no map server (rAthena HC_NOTIFY_ACCESSIBLE_MAPNAME 0x0840): the maps
        /// it may go to instead (open = reachable now). Answer with <see cref="SelectAccessibleMap"/>, or pick another character.</summary>
        event Action<List<(string map, bool open)>> MapChoice;
        /// <summary>Wave H: the answer to <see cref="ChangePassword"/>: done or not, and a text for the player.</summary>
        event Action<bool, string> PasswordChanged;

        void Login(string host, int port, string database, string user, string pass);
        /// <summary>Wave I (ionline): drops the connection WITHOUT logging out: what a reconnect starts from, and what Cancel on the
        /// "Connecting…" screen does. rAthena keeps no login, so this is <see cref="Reset"/> there; Loki Online keeps this PC's
        /// saved login on the server (<see cref="Reset"/> would end it), so <see cref="Resume"/> gets back in afterwards.</summary>
        void Drop();
        /// <summary>Wave H: changes the logged-in account's password (only where <see cref="CanChangePassword"/>). Answered by <see cref="PasswordChanged"/>.</summary>
        void ChangePassword(string oldPass, string newPass);
        void Reset();
        void SelectCharacter(int slot);
        void CreateCharacter(string name, int slot, int hairStyle, int hairColor, uint job, int sex = -1);
        void DeleteReserve(uint charId);
        void DeleteConfirm(uint charId, string birthdate);
        void DeleteCancel(uint charId);
        void PinCheck(string pin);
        void PinCreate(string pin);
        void PinChange(string oldPin, string newPin);
        void PinSkip();
        /// <summary>Is the new name free and allowed? Answered by <see cref="RenameChecked"/> (only where <see cref="CanRename"/>).</summary>
        void RenameCheck(uint charId, string newName);
        /// <summary>Renames to the name last checked. Answered by <see cref="RenameResult"/>, then a new list.</summary>
        void Rename(uint charId, string newName);
        /// <summary>Moves a character to another slot (a used slot swaps the two where the server allows it). Answered by <see cref="SlotMoved"/>.</summary>
        void MoveSlot(int fromSlot, int toSlot, int movesLeft);
        /// <summary>This PC holds a saved login for the server (only where <see cref="CanResume"/>).</summary>
        bool HasSavedLogin(string host, int port, string database);
        /// <summary>Logs in with this PC's saved login, no password (only where <see cref="CanResume"/>; a <see cref="Notice"/> says why not).</summary>
        void Resume(string host, int port, string database);
        /// <summary>Client wave 4: goes to entry <paramref name="mapIndex"/> of the last <see cref="MapChoice"/> list with the character in
        /// <paramref name="slot"/> (-1: the last one selected).</summary>
        void SelectAccessibleMap(int slot, int mapIndex);
    }

    /// <summary>A login-screen address written the way a player types it: "name", "name:port", "wss://name", "https://name/".
    /// Only Loki Online reads the scheme ("wss" = TLS, e.g. local on 443; "ws" plain); rAthena is plain TCP.</summary>
    public static class ServerAddress
    {
        /// <summary>(scheme, host): "https" counts as "wss", "http" as "ws", anything else as "ws"; a trailing '/' goes.</summary>
        public static (string scheme, string host) Split(string host, string scheme = null)
        {
            string h = (host ?? "").Trim(), s = string.IsNullOrWhiteSpace(scheme) ? "ws" : scheme.Trim().ToLowerInvariant();
            int sep = h.IndexOf("://", StringComparison.Ordinal);
            if (sep > 0) { s = h.Substring(0, sep).ToLowerInvariant(); h = h.Substring(sep + 3); }
            if (s == "https") s = "wss"; else if (s == "http") s = "ws";
            if (s != "wss") s = "ws";
            return (s, h.TrimEnd('/'));
        }

        /// <summary>"name:1234", "wss://name:443/", "[::1]:6900" → (host without the port, 1234); no port, or an IPv6 address
        /// without brackets → (text, <paramref name="fallback"/>). The scheme, if any, stays on the host.</summary>
        public static (string host, int port) SplitPort(string text, int fallback)
        {
            string t = (text ?? "").Trim(), prefix = "";
            int sep = t.IndexOf("://", StringComparison.Ordinal);
            if (sep >= 0) { prefix = t.Substring(0, sep + 3); t = t.Substring(sep + 3); }
            t = t.TrimEnd('/');
            int colon = -1;
            if (t.StartsWith("[")) { int close = t.IndexOf(']'); if (close > 0 && close + 1 < t.Length && t[close + 1] == ':') colon = close + 1; }
            else if (t.IndexOf(':') > 0 && t.IndexOf(':') == t.LastIndexOf(':')) colon = t.IndexOf(':');
            if (colon > 0 && int.TryParse(t.Substring(colon + 1), out int p) && p > 0 && p < 65536) return (prefix + t.Substring(0, colon), p);
            return (prefix + t, fallback);
        }
    }

    /// <summary><see cref="IAccountSession"/> over rAthena (the connection the game keeps using once in the world).</summary>
    public sealed class RoAccountSession : IAccountSession
    {
        public readonly RoSession S;
        private string _checking = "";
        public RoAccountSession(RoSession s)
        {
            S = s;
            s.OnPinState += (_, st) => PinStateChanged?.Invoke(st);
            s.OnLoginRefused += (_, err, text) => LoginRefused?.Invoke(err, text);
            s.OnFailed += (_, why) => Failed?.Invoke(why);
            s.OnEnteredMap += _ => EnteredMap?.Invoke();
            s.OnDeleteResult += (_, cid, due, msg) => DeleteResult?.Invoke(cid, due, msg);
            s.OnCreateRefused += (_, text) => CreateRefused?.Invoke(text);
            s.OnCharList += _ => CharListChanged?.Invoke();
            s.OnRenameCheck += (_, ok) => RenameChecked?.Invoke(ok, RenameCheckText(ok, _checking));
            s.OnRenameResult += (_, res, text) => RenameResult?.Invoke(res, text);
            s.OnSlotMoved += (_, ok, moves) => SlotMoved?.Invoke(ok, moves);
            s.OnSelectRefused += (_, text) => SelectRefused?.Invoke(text);
            s.OnMapChoice += (_, maps) => MapChoice?.Invoke(maps);
        }

        /// <summary>HC_ACK_IS_VALID_CHARNAME 0x028e says only yes / no (rA:char/char_clif.cpp:1405-1458: char_check_char_name,
        /// the same test as creation): the text names letters a stock server refuses, as <see cref="RoSession.CreateRefusedText"/>.</summary>
        public static string RenameCheckText(bool ok, string name)
        {
            if (ok) return Lang.F("'{0}' is free: press Rename (one time only).", name);
            string odd = RoSession.NameLettersOutsideStock(name);
            return odd.Length > 0 ? Lang.F("'{0}' can't be used: this server doesn't accept {1} in names (stock rAthena takes A–Z, a–z, digits and spaces).", name, odd)
                                  : Lang.F("'{0}' can't be used: it's taken or not allowed.", name);
        }

        public RoSession.Stage State => S.State;
        public string FailReason => S.FailReason;
        public int PinState => S.PinState;
        public bool PinPending => S.PinPending;
        public List<CharInfo> Characters => S.Characters;
        public int TotalSlots => S.TotalSlots;
        /// <summary>HC_ACCEPT_ENTER2 producible (= sd.char_slots, chclif_mmo_send082d); rAthena refuses a move at or past it
        /// (chclif_parse_moveCharSlot, rA:char/char_clif.cpp:55-141).</summary>
        public int UsableSlots => S.ProducibleSlots > 0 ? S.ProducibleSlots : S.TotalSlots;
        public byte Sex => S.Sex;
        public bool RegisterAccount { get => S.RegisterAccount; set => S.RegisterAccount = value; }
        public bool AutoRegister { get => S.AutoRegister; set => S.AutoRegister = value; }
        public char RegisterSex { get => S.RegisterSex; set => S.RegisterSex = value; }
        /// <summary>rAthena renames (0x028d / 0x08fc) and moves slots (0x08d4) for this PACKETVER; it has no saved logins.</summary>
        public bool CanRename => true;
        public bool CanMoveSlot => true;
        public bool CanResume => false;
        public bool CanChangePassword => false;
        /// <summary>The login screen's rules for rAthena as client waves 1-4 set them: 4 characters at least, and a new account's
        /// name at most 21 (NAME_LENGTH 24 minus the terminator and the _M / _F suffix).</summary>
        public int AccountNameMin => 4;
        public int AccountNameMax => 21;
        public int PasswordMinLength => 4;
        public int LastBanCode => S.LastBanCode;
        public bool RegisterHashed => false;

        public event Action<int> PinStateChanged;
        public event Action<int, string> LoginRefused;
        public event Action<string> Failed;
        public event Action EnteredMap;
        public event Action<uint, long, string> DeleteResult;
        public event Action<string> CreateRefused;
        public event Action CharListChanged;
        public event Action<string> Notice;
        public event Action<bool, string> RenameChecked;
        public event Action<int, string> RenameResult;
        public event Action<bool, int> SlotMoved;
        public event Action<string> SelectRefused;
        public event Action<List<(string map, bool open)>> MapChoice;
        public event Action<bool, string> PasswordChanged;

        public void Login(string host, int port, string database, string user, string pass) => S.Login(host, port, user, pass);
        public void ChangePassword(string oldPass, string newPass) => PasswordChanged?.Invoke(false, Lang.T("This server can't change passwords."));
        public void SelectAccessibleMap(int slot, int mapIndex) => S.SelectAccessibleMap(slot, mapIndex);
        public void Reset() => S.Reset();
        public void Drop() => S.Reset();
        public void SelectCharacter(int slot) => S.SelectCharacter(slot);
        public void CreateCharacter(string name, int slot, int hairStyle, int hairColor, uint job, int sex = -1) => S.CreateCharacter(name, slot, hairStyle, hairColor, job, sex);
        public void DeleteReserve(uint charId) => S.DeleteReserve(charId);
        public void DeleteConfirm(uint charId, string birthdate) => S.DeleteConfirm(charId, birthdate);
        public void DeleteCancel(uint charId) => S.DeleteCancel(charId);
        public void PinCheck(string pin) => S.PinCheck(pin);
        public void PinCreate(string pin) => S.PinCreate(pin);
        public void PinChange(string oldPin, string newPin) => S.PinChange(oldPin, newPin);
        public void PinSkip() => S.PinSkip();
        public void RenameCheck(uint charId, string newName) { _checking = newName ?? ""; S.RenameCheck(charId, newName); }
        public void Rename(uint charId, string newName) => S.Rename(charId, newName);
        public void MoveSlot(int fromSlot, int toSlot, int movesLeft) => S.MoveSlot(fromSlot, toSlot, movesLeft);
        public bool HasSavedLogin(string host, int port, string database) => false;
        public void Resume(string host, int port, string database) => Notice?.Invoke(Lang.T("rAthena servers need the password every time."));
    }
}
