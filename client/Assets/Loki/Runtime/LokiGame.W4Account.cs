using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 4, w4account: getting in and staying in. The login screen's data-folder button (UI/LoginPanel.Account.cs and
    /// UI/NoDataPanel.cs change mode), and the way back after a lost map server: instead of the login screen, a fresh scene logs
    /// in again with the same server and account and lands on the character list (UI/LoginPanel.Reconnect.cs). Not after a code
    /// that forbids it (someone else logged in with the account: <see cref="RefusalText.MayReconnect"/>), not after @autotrade (15
    /// with the player's shop open: a new login would close it), not for loki.json's auto-login (it made no login on the
    /// panel) — those go to the login screen with the reason, as before. The smokes are in LokiGame.W4AccountSmoke.cs.
    /// Port wave I (ionline): Loki Online reconnects the same way (it was left out: "its SDK identity resumes"). The code of a
    /// kick is the map protocol's on both servers (Session.LastBanCode: the module sends the same SC_NOTIFY_BAN over its link;
    /// -1 when the SpacetimeDB connection itself closed), and the panel's ticket logs in again through IAccountSession: with
    /// the password on rAthena, with this PC's saved login on a server that keeps one (UI/LoginPanel.Reconnect.cs).
    /// </summary>
    public sealed partial class LokiGame
    {
        private void BeginW4Account(LokiBootstrap boot)
        {
            if (Login == null) return;
            var cfg = boot.Config;
            Login.DataFolderText = () => string.Format("{0} ({1})", cfg.clientRoot, cfg.ClientRootSource == "your choice" ? Lang.T("your choice") : cfg.ClientRootSource == "your earlier choice" ? Lang.T("your earlier choice") : cfg.ClientRootSource); // w4lang: LokiConfig's source names stay English (World/RoAssets.cs)
            Login.OpenDataFolder = () => NoDataPanel.ShowChange(boot.Rig.transform, boot.Rig.Head, cfg, () => { Login.PlaceInFront(); Login.Show(LoginPanel.Screen.Login); });
            Login.RefreshLoginInfo(); // the data line needs the text set above
            var t = LoginPanel.PendingReconnect; LoginPanel.PendingReconnect = null;
            if (t != null)
            {
                bool started = Login.ResumeFromTicket(t);
                Debug.Log($"[Game] after the lost connection ({t.Why}): logging in again as {t.User}: {started}");
            }
        }

        /// <summary>LokiGame.Wave1.ConnectionLost's first line: true when this took over (a reconnect through a fresh scene).</summary>
        private bool W4AccountConnectionLost(string why)
        {
            if (_goingToLogin || Login == null || Session == null) return false;
            int ban = Session.LastBanCode;
            // code 15 with a vending / buying shop open is @autotrade (rA:map/atcommand.cpp:6859): a new login of the account would end the
            // shop the player left open (the login server kicks the older session), so no reconnect then
            bool shopKept = ban == 15 && Market != null && (Market.Vending || Market.Buying);
            var t = RefusalText.MayReconnect(ban) && !shopKept ? Login.MakeReconnectTicket(why) : null;
            if (t == null) { Debug.Log($"[Game] connection lost ({why}, code {ban}): no reconnect — {(shopKept ? "the shop stays open (autotrade)" : RefusalText.MayReconnect(ban) ? "no login made on the login screen" : "the code forbids it")}"); return false; }
            _goingToLogin = true;
            LoginPanel.PendingReconnect = t;
            Debug.LogWarning("[Game] connection lost in game: " + why + " — reconnecting");
            Chat(why, "ff7a6a");
            // 15 reads "disconnected by a Game Master" (the client's own text) but is also what everyone gets when the map server stops
            ShowNotice(Lang.T("Connection lost"), why + (ban == 15 ? "\n" + Lang.T("The server may be restarting.") : "") + "\n" + Lang.T("Reconnecting in a moment…"));
            _showLoginOnBoot = true;
            StartCoroutine(ReloadAfter(2f));
            return true;
        }
    }
}
