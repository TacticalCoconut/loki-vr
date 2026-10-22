using System;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 4 (w4account): two small things on the login screen.
    ///  • The game data folder in use, and "Game data…" to pick another one (the game hands the panel the text and the action:
    ///    LokiGame.W4Account.cs opens <see cref="NoDataPanel"/> in its change mode; the choice applies from the next start).
    ///  • Registration's clear-text password, said before it is sent. rAthena creates an account only from the plain CA_LOGIN
    ///    0x0064 with the _M/_F suffix: login_mmo_auth refuses the registration of a hashed login (rA:login/login.cpp:323-339:
    ///    "could not be created because client side password encryption is enabled"), and no other packet registers. The
    ///    protocol has no encrypted way, so for a server that is not this PC the first press says so (use a password used
    ///    nowhere else, or ask the host to make the account in Loki Host's Accounts tab, which never crosses the network) and a
    ///    second press within 8 s sends it. Logins after that are hashed (CA_LOGIN2 md5(salt ‖ password), Net/RoSession.cs).
    /// </summary>
    public sealed partial class LoginPanel
    {
        /// <summary>"folder (where it comes from)" for the login screen, set by the game.</summary>
        public Func<string> DataFolderText;
        /// <summary>Opens the data-folder chooser (the panel hides itself meanwhile), set by the game.</summary>
        public Action OpenDataFolder;
        private Text _dataInfo; private UiButton _dataBtn;

        private void BuildDataButton(RectTransform p, float top)
        {
            _dataInfo = UiKit.Label(p, "", new Vector2(40, -top + 100), new Vector2(W - 200, 36), 17, TextAnchor.MiddleCenter, UiKit.Dim);
            _dataBtn = UiKit.Button(p, Lang.T("Game data…"), new Vector2(-W / 2 + 170, -top + 40), new Vector2(260, 56), () => { if (OpenDataFolder != null) { Hide(); OpenDataFolder(); } }, 22);
        }

        private void RefreshDataButton()
        {
            if (_dataInfo == null) return;
            string t = DataFolderText != null ? DataFolderText() : null;
            _dataInfo.text = string.IsNullOrEmpty(t) ? "" : Lang.F("Game data: {0}", Esc(t));
            _dataBtn.gameObject.SetActive(OpenDataFolder != null);
        }

        // ---------------------------------------------------------------- registration over the network
        private string _regAsk; private float _regAskUntil;

        /// <summary>This PC's own addresses (loopback): the password never leaves the machine.</summary>
        public static bool IsThisPc(ServerEntry sv)
        {
            if (sv == null) return false;
            string h = (sv.host ?? "").Trim().ToLowerInvariant();
            return h == "localhost" || h == "::1" || h == "[::1]" || h.StartsWith("127.");
        }

        /// <summary>True when the registration may go now: this PC, a server whose registration is hashed here (Loki Online), or the second press.</summary>
        private bool RegisterConfirmed(ServerEntry sv, char sex)
        {
            if (SessionFor(sv).RegisterHashed || IsThisPc(sv)) return true;
            string key = ServerKey(sv) + "|" + sex;
            if (_regAsk == key && Time.time <= _regAskUntil) { _regAsk = null; RefreshRegisterButtons(); return true; }
            _regAsk = key; _regAskUntil = Time.time + 8f;
            Status(Lang.F("Creating an account sends its password to {0} unencrypted, this once (rAthena can't do otherwise). Use a password you use nowhere else, or ask the host to create the account in Loki Host's Accounts tab. Press the button again to go on.", Esc(sv.host)));
            RefreshRegisterButtons();
            return false;
        }

        private void RefreshRegisterButtons()
        {
            if (_regM == null) return;
            bool askM = _regAsk != null && _regAsk.EndsWith("|M") && Time.time <= _regAskUntil, askF = _regAsk != null && _regAsk.EndsWith("|F") && Time.time <= _regAskUntil;
            _regM.SetLabel(askM ? Lang.T("Send it ♂") : Lang.T("Create account"));
            _regF.SetLabel(askF ? Lang.T("Send it ♀") : Lang.T("Create account"));
        }

        /// <summary>Redraws the data line (the game sets <see cref="DataFolderText"/> after the panel exists).</summary>
        public void RefreshLoginInfo() => RefreshDataButton();

        // ---- smoke hooks
        public string DataInfo => _dataInfo != null ? _dataInfo.text : "";
        public bool DataButtonShown => _dataBtn != null && _dataBtn.gameObject.activeInHierarchy;
        public string RegisterButtonLabel(char sex) => sex == 'F' ? _regF.Text.text : _regM.Text.text;
    }
}
