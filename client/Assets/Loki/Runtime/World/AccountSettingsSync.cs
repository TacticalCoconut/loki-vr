using System;
using Loki.Net;
using UnityEngine;

namespace Loki.World
{
    /// <summary>
    /// Client wave 18 (w18acct), the design's point 1 ("even if I start Loki on a completely different computer, it will still hold all of my
    /// repositioned UI elements in their specific positions" + "This includes every other setting, related to comfort, controls,
    /// casting modes, etc."): this PC's settings file (World/UserSettings.cs) kept in step with the account's copy on the server,
    /// through <see cref="IAccountSettingsChannel"/> only (nothing here knows rAthena, the relay or SpacetimeDB).
    ///   - When the link comes up the server answers with the account's save (or none); <see cref="AccountSyncRules.Decide"/> picks:
    ///     take it (the account's settings replace this PC's, but Audio and Graphics stay this PC's: UserSettings.PerPcFields), send
    ///     this PC's (they become the account's), or nothing.
    ///   - Every later change of the account's part (UserSettings.Save stamps it) is sent <see cref="PushDelay"/> after the last one,
    ///     while the link is up; a change made while it is down is sent at the next link (unless another PC saved later).
    ///   - The server holds a newer save than one sent (another PC saved meanwhile): it is asked again and its save is taken.
    /// Taking the account's settings re-reads everything that read them (VR/Comfort.cs Reapply; the game's <see cref="Applied"/>:
    /// look select, the language, the view's pitch, the Reposition places shown now).
    /// </summary>
    public sealed class AccountSettingsSync : IDisposable
    {
        /// <summary>Seconds after the last change before it is sent (a slider dragged, a place moved several times in a row).</summary>
        public const float PushDelay = 1f;
        /// <summary>Seconds without an answer after which a save counts as lost (it is sent again with the next change or link).</summary>
        public const float AnswerWait = 10f;

        public readonly IAccountSettingsChannel Channel;
        public readonly string Server; public readonly uint Aid;
        /// <summary>The account's settings were taken and they differ from what this PC had: the game re-reads what Comfort does not hold.</summary>
        public Action Applied;

        /// <summary>What happened last ("took the account's", "sent this PC's", "in step", "refused" …), for the log and the smoke.</summary>
        public string Last { get; private set; } = "nothing yet";
        public int Answers, Takes, TakesChanged, Pushes, Stored, Older, Refusals;
        /// <summary>The time of the server's save as last heard (0 none).</summary>
        public long HostSavedAt { get; private set; }

        private float _pushDue = -1f, _inFlightSince;
        private long _seenSavedAt, _inFlight;

        public AccountSettingsSync(IAccountSettingsChannel channel, string server, uint aid)
        {
            Channel = channel; Server = server ?? ""; Aid = aid;
            channel.SettingsLoaded += OnLoaded;
            channel.SettingsSaved += OnSaved;
        }

        public void Dispose() { Channel.SettingsLoaded -= OnLoaded; Channel.SettingsSaved -= OnSaved; }

        private bool Mine(UserSettings.AccountSync a) => a.server == Server && a.aid == Aid;

        private void OnLoaded(byte state, long hostAt, string json)
        {
            Answers++;
            var a = UserSettings.Account;
            bool same = Mine(a);
            if (state == PresenceWire.SettingsFound) HostSavedAt = hostAt;
            var act = AccountSyncRules.Decide(same, a.savedAt, a.pushed, state, hostAt);
            switch (act)
            {
                case AccountSyncRules.Action.Take:
                    if (!Take(hostAt, json)) Push("the server's copy could not be read");
                    break;
                case AccountSyncRules.Action.Push:
                    Push(state == PresenceWire.SettingsNone ? "the server has none for this account yet" : same ? "this PC's change is newer" : "");
                    break;
                case AccountSyncRules.Action.InStep:
                    if (!a.pushed) { a.pushed = true; UserSettings.Save(); }
                    Last = "in step";
                    Debug.Log($"[Settings] in step with the account's on the server (saved {Stamp(hostAt)})");
                    break;
                default:
                    Refusals++;
                    Last = state == PresenceWire.SettingsRefused ? "refused" : "server store failed";
                    Debug.Log(state == PresenceWire.SettingsRefused
                        ? "[Settings] the server did not take this login's proof: this PC keeps its own settings (an rAthena without web tokens?)"
                        : "[Settings] the server could not read its settings store: this PC keeps its own settings");
                    break;
            }
        }

        /// <summary>The account's settings replace this PC's (but this PC's Audio and Graphics); false when the JSON is not settings.</summary>
        private bool Take(long hostAt, string json)
        {
            string before = UserSettings.AccountHash();
            if (!UserSettings.ApplyAccountJson(json)) return false;
            var a = UserSettings.Account;
            a.server = Server; a.aid = Aid; a.savedAt = hostAt; a.pushed = true; a.hash = UserSettings.AccountHash();
            bool changed = a.hash != before;
            UserSettings.Save(); // (its hash is the record's: nothing is stamped, nothing sent back)
            _seenSavedAt = a.savedAt; _pushDue = -1f;
            Takes++; Last = "took the account's";
            Debug.Log($"[Settings] took the account's settings from the server (saved {Stamp(hostAt)}, {json.Length} characters): {(changed ? "they differ from this PC's: applied" : "the same as this PC's")}; Audio and Graphics stay this PC's");
            if (changed)
            {
                TakesChanged++;
                VR.Comfort.Reapply();
                try { Applied?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            }
            return true;
        }

        /// <summary>This PC's settings as the account's: the record names this account from now on.</summary>
        private void Push(string why)
        {
            // a file Comfort never wrote (a new PC) holds its fields as read, not as Comfort keeps them (its first save fills the rumble kinds,
            // the bar's mode, the ring's step …): written first, so what goes up is what this PC's next save would hold (else that save
            // would look like a change of the account's part and go up again)
            if (!UserSettings.Prefs.saved) VR.Comfort.Save();
            if (UserSettings.Prefs.codex == null) UserSettings.Prefs.codex = new Game.CodexSave();
            if (string.IsNullOrEmpty(UserSettings.Prefs.codex.acct) && Aid != 0) UserSettings.Prefs.codex.acct = Server + "#" + Aid;
            Game.CodexJournal.MergeArchiveInto(UserSettings.Prefs.codex);
            var a = UserSettings.Account;
            string json = UserSettings.AccountJson();
            if (!Mine(a) || a.savedAt <= 0)
            {
                a.server = Server; a.aid = Aid; if (a.savedAt <= 0) a.savedAt = AccountSyncRules.NowMs();
                a.hash = AccountSyncRules.Hash(json); a.pushed = false;
                UserSettings.Save();
            }
            Channel.SaveSettings(a.savedAt, json);
            _inFlight = a.savedAt; _inFlightSince = Time.unscaledTime; _seenSavedAt = a.savedAt; _pushDue = -1f;
            Pushes++; Last = "sent this PC's";
            Debug.Log($"[Settings] sent this PC's settings as the account's ({json.Length} characters, changed {Stamp(a.savedAt)}){(string.IsNullOrEmpty(why) ? "" : ": " + why)}");
        }

        private void OnSaved(byte state, long hostAt)
        {
            var a = UserSettings.Account;
            long sent = _inFlight; _inFlight = 0;
            if (hostAt > 0) HostSavedAt = hostAt;
            if (state == PresenceWire.SettingsStored)
            {
                Stored++; Last = "stored";
                if (Mine(a) && a.savedAt == hostAt) { a.pushed = true; UserSettings.Save(); }
                Debug.Log($"[Settings] the server stored them (saved {Stamp(hostAt)}){(Mine(a) && a.savedAt != hostAt ? "; a newer change waits" : "")}");
            }
            else if (state == PresenceWire.SettingsOlder)
            {
                Older++; Last = "older";
                Debug.Log($"[Settings] the server holds a newer save ({Stamp(hostAt)}, after this PC's {Stamp(sent)}): asking for it");
                Channel.LoadSettings();
            }
            else
            {
                Refusals++; Last = state == PresenceWire.SettingsRefused ? "refused" : "server store failed";
                Debug.Log($"[Settings] not stored ({Last}): this PC keeps them and sends them at the next link");
            }
        }

        /// <summary>Main thread, every frame: a change of the account's part is sent once the player stopped changing it.</summary>
        public void Update()
        {
            if (_inFlight != 0 && Time.unscaledTime - _inFlightSince > AnswerWait) { Debug.Log("[Settings] no answer from the server: sent again with the next change or link"); _inFlight = 0; }
            if (Channel.SettingsLink != AccountSettingsLink.Ready || Answers <= 0) return;
            var a = UserSettings.Account;
            if (!Mine(a)) return; // (the file mirrors another account until the server answered: OnLoaded decides)
            if (a.savedAt != _seenSavedAt) { _seenSavedAt = a.savedAt; if (!a.pushed) _pushDue = Time.unscaledTime + PushDelay; }
            if (!a.pushed && _inFlight == 0 && _pushDue >= 0f && Time.unscaledTime >= _pushDue) Push("");
        }

        /// <summary>One line for the log and the smoke.</summary>
        public string Summary()
        {
            var a = UserSettings.Account;
            return $"link {Channel.SettingsLink}, last '{Last}', answers {Answers}, taken {Takes} ({TakesChanged} changed), sent {Pushes}, stored {Stored}, older {Older}, refused/failed {Refusals}; " +
                   $"this PC mirrors {(Mine(a) ? "this account" : $"'{a.server}' AID {a.aid}")}, changed {Stamp(a.savedAt)}, {(a.pushed ? "on the server" : "NOT on the server yet")}; the server's save {Stamp(HostSavedAt)}";
        }

        public static string Stamp(long ms) => ms <= 0 ? "never" : DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");
    }
}
