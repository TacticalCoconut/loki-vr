using Loki.Net;
using Loki.World;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 18 (w18acct), the design's point 1: every setting and Reposition place kept per ACCOUNT on the server (Audio and Graphics
    /// stay per PC), so a new PC gets them. The game's side: once the presence channel is up (LokiGame.W3Presence.cs) and it can keep
    /// settings (<see cref="IAccountSettingsChannel"/>: Loki Host's relay today), a <see cref="AccountSettingsSync"/> keeps this PC's
    /// file in step with the account's copy; when the account's settings come in, what Comfort does not hold is read again here.
    /// Outside the RO protocol: nothing here reads or writes an RO packet (the login's proof is RoSession.WebAuthToken, handed to the
    /// relay channel where it is made: StartPresence). Hooks: W3Presence.cs StartPresence / StartPresenceOn, LokiGame.cs Update and
    /// the smoke dispatch.
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>The account's settings in step with this PC's (null: the channel keeps none, or no channel yet).</summary>
        public AccountSettingsSync AccountSync { get; private set; }

        /// <summary>Hook: StartPresenceOn, after Begin.</summary>
        private void StartW18Account(IPresenceChannel ch)
        {
            if (AccountSync != null || !(ch is IAccountSettingsChannel s) || Session == null) return;
            string serverKey = AccountSyncRules.ServerKey(Session.Host, Session.Port);
            Game.CodexJournal.ArchiveIfOtherAccount(serverKey + "#" + Session.AccountId);
            AccountSync = new AccountSettingsSync(s, serverKey, Session.AccountId) { Applied = OnW18AccountApplied };
            Debug.Log($"[Settings] settings per account over the {ch.Describe} for AID {Session.AccountId} on {AccountSync.Server} (this login's proof: {(PresenceWire.ValidProof(Session.WebAuthToken) ? "a web token" : "none: the server makes no web tokens")}); this PC's file mirrors {(UserSettings.Account.server == "" ? "no account yet" : $"'{UserSettings.Account.server}' AID {UserSettings.Account.aid}")}");
        }

        /// <summary>Hook: Update.</summary>
        private void UpdateW18Account() => AccountSync?.Update();

        /// <summary>The account's settings came in and differ from this PC's: Comfort was read again (VR/Comfort.cs Reapply); the rest here.</summary>
        private void OnW18AccountApplied()
        {
            UI.LookSelect.Reload();
            string lang = UserSettings.Prefs.language;
            if (!string.IsNullOrEmpty(lang) && lang != Lang.Code) Lang.Set(lang);
            View?.TakeFollowPitch(VR.Comfort.FollowPitch);
            W19ViewFromAccount(); // client wave 19 (w19view41), point 55: the account's kept view, unless the player chose one this launch
            // the Reposition places shown now (each one reads its place when it is shown; these are shown already)
            MiniMap?.ApplyPlace(); if (W12Wheel != null && W12Wheel.Front) W12Wheel.ApplyFrontPlace(); W13Overhead?.PlaceNow();
            W20AccountApplied(); // client wave 20 (w20bubbles), point 56: the bubbles read the account's layout and contents live; the editor's labels anew
            if (MiniMap != null) MiniMap.ZoomStep = VR.Comfort.MinimapZoom;
            Tutorial?.CloseIfDone(); // (a new PC opens it at the first map; the account saw it on another)
            WCodexAccountApplied(); // wave 28 (wcodex): the account's Codex joined with what this PC wrote since (LokiGame.WCodex.cs)
            if (Hotkeys != null) Hotkeys.ShowTab(UserSettings.QuickTabOf(Hotkeys.Character)); // the bar's tab the account shows for this character
            Hud?.Notify(Lang.T("Settings loaded from your account."), 4f);
            Debug.Log("[Settings] the account's settings applied: " + AccountSync?.Summary());
        }
    }
}
