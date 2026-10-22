using System;

namespace Loki.Net
{
    /// <summary>Where a link stands with the account's settings (client wave 18, w18acct).</summary>
    public enum AccountSettingsLink
    {
        /// <summary>No link, or the server keeps no settings (an older Loki Host, a standalone relay, a server without the store).</summary>
        None,
        /// <summary>The server keeps them, but this login has no proof to give (an rAthena login server that makes no web tokens).</summary>
        NoProof,
        /// <summary>Asked; the answer has not come yet.</summary>
        Asking,
        /// <summary>The server took this login's proof: the account's settings may be read and written.</summary>
        Ready,
        /// <summary>The server refused the proof, or its store failed: nothing is read or written on this link.</summary>
        Refused,
    }

    /// <summary>
    /// Client wave 18 (w18acct), the design's point 1 ("The reposition locations for every element needs to be stored on an account basis
    /// ... even if I start Loki on a completely different computer" + "This includes every other setting"): the account's settings
    /// on the server, outside the RO protocol (HANDOFF.md's pivot rule: settings sit behind a client interface, never inside
    /// rAthena-specific code). The game talks to this interface only (World/AccountSettingsSync.cs). Today
    /// <see cref="PresenceRelayChannel"/>: Loki Host's relay on the presence connection (PresenceWire's SETTINGS_* frames; the host
    /// keeps them in its database, tools/lokihost/Core/AccountSettingsDb.cs; the proof is rAthena's web token of this login). On the
    /// SpacetimeDB module the same frames go up as the <c>presence</c> reducer's argument and come back as <c>presence_out</c> rows
    /// (server/src/presence.rs:87-95, 213: the module is the relay), and the module knows the caller's account from its own session
    /// (presence.rs:232 p.account_id): SETTINGS_AUTH's proof is not needed there, the rows go into a private table of the module
    /// keyed by that account (StdbPresenceChannel would implement this interface over them; the port is paused: not built).
    /// The settings are opaque text here (UTF-8 JSON, World/UserSettings.cs AccountJson); what wins is decided by the caller
    /// (World/AccountSyncRules.cs).
    /// </summary>
    public interface IAccountSettingsChannel
    {
        AccountSettingsLink SettingsLink { get; }
        /// <summary>Asks for the account's stored settings (a link asks once by itself when it comes up). Answered by <see cref="SettingsLoaded"/>.</summary>
        void LoadSettings();
        /// <summary>Stores these as the account's settings, changed by the player at <paramref name="savedAt"/> (ms since 1970 UTC); only the
        /// latest call is sent when several wait. Answered by <see cref="SettingsSaved"/>.</summary>
        void SaveSettings(long savedAt, string json);
        /// <summary>Main thread, from Poll: (state: PresenceWire.SettingsFound / None / Refused / Failed, the save's time, its JSON when found).</summary>
        event Action<byte, long, string> SettingsLoaded;
        /// <summary>Main thread, from Poll: (state: PresenceWire.SettingsStored / Older / Refused / Failed, the time of the save the server holds now).</summary>
        event Action<byte, long> SettingsSaved;
    }
}
