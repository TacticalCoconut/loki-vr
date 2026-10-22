#nullable disable
using System;

namespace Loki.World
{
    /// <summary>
    /// Client wave 18 (w18acct), the design's point 1: which save wins when a PC meets its account's settings on the host. No Unity
    /// dependency on purpose: Loki Host's tests compile this file (tools/lokihost/Tests/AccountSettingsTests.cs).
    ///
    /// The rule (the orchestrator's default, BRIEF-W18 P1: "the newest save wins, whole file"): every PC remembers which account
    /// (server and AID) its settings file mirrors and when the player last changed them there (World/UserSettings.cs AccountSync);
    /// the host keeps one save per account with the time it was made. When a PC links to its host:
    ///   - the host has none for the account: this PC's settings become the account's (the first login after wave 18);
    ///   - the PC mirrors ANOTHER account, or none (a new PC, a friend's PC): the account's settings come to it;
    ///   - the same account: the newer of the two wins, the whole file (a change made on a PC while it could not reach the host is
    ///     sent at its next link, unless another PC saved later);
    ///   - the host refused the proof or could not read its store: this PC keeps its own and sends nothing.
    /// Times are the saving PC's clock (ms since 1970 UTC): two PCs whose clocks differ by more than the time between two changes
    /// can pick the older one (Windows keeps clocks within seconds).
    /// </summary>
    public static class AccountSyncRules
    {
        public enum Action
        {
            /// <summary>Keep this PC's settings, send nothing (no accepted proof, or the host's store failed).</summary>
            Nothing,
            /// <summary>The host holds exactly this PC's save.</summary>
            InStep,
            /// <summary>Send this PC's settings (they become the account's).</summary>
            Push,
            /// <summary>Take the host's (the account's) settings.</summary>
            Take,
        }

        /// <param name="sameAccount">This PC's file mirrors this server's account (its AccountSync names it).</param>
        /// <param name="localSavedAt">When the player last changed this PC's settings (0: never since wave 18).</param>
        /// <param name="localPushed">The host took this PC's last change.</param>
        /// <param name="state">The host's answer: Net.PresenceWire.SettingsFound / None / Refused / Failed (0..3).</param>
        /// <param name="hostSavedAt">The host's save's time (Found only).</param>
        public static Action Decide(bool sameAccount, long localSavedAt, bool localPushed, byte state, long hostSavedAt)
        {
            const byte Found = 0, None = 1; // (Net.PresenceWire.SettingsFound / SettingsNone)
            if (state == None) return Action.Push;
            if (state != Found) return Action.Nothing;
            if (!sameAccount) return Action.Take;
            if (localSavedAt > hostSavedAt) return Action.Push; // (pushed or not: the host lost it, or never got it)
            if (hostSavedAt > localSavedAt) return Action.Take;
            return Action.InStep;
        }

        /// <summary>FNV-1a 64 of the text's UTF-16 code units, as 16 hex digits: tells whether the account's part of the settings
        /// changed between two saves (World/UserSettings.cs Save).</summary>
        public static string Hash(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in s ?? "") { h ^= c; h *= 1099511628211UL; }
            return h.ToString("x16");
        }

        /// <summary>The key of a server for <see cref="Decide"/>'s sameAccount: "host:port" of its login server, lower case.</summary>
        public static string ServerKey(string host, int port) => (host ?? "").Trim().ToLowerInvariant() + ":" + port;

        /// <summary>Now, as the saves' times are kept (ms since 1970 UTC).</summary>
        public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
