using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>Wave 28 (wcodex): one account's Codex as it is saved: the creatures written down in full and the ones seen only (a boss
    /// looked at), the items, and the last few entries written (for the index's "Recently written"). Each list is
    /// <see cref="CodexData.Encode"/>'s short string. In the account's settings (World/UserSettings.cs Settings.codex: it goes with the
    /// account through Loki Host like every setting not in PerPcFields) and in this PC's own file (codex.json beside loki-user.json).</summary>
    [Serializable]
    public sealed class CodexSave
    {
        /// <summary>The account: AccountSyncRules.ServerKey(login host, port) + "#" + the account id ("" none yet).</summary>
        public string acct = "";
        public string full = "", seen = "", items = "";
        /// <summary>Creatures an NPC named in a dialogue (design note, 2026-10-15: "If you ever hear an NPC talk about a specific creature during
        /// dialogue, its silhouette, name and level is revealed"): "heard of", below written down.</summary>
        public string heard = "";
        /// <summary>Heard of since the book was last opened, oldest first ("1039,1272"): the Codex button's green aura and the next opening's
        /// page (design note: "If the Player logs out before checking, that still continues there until opened").</summary>
        public string pending = "";
        /// <summary>The last entries written, newest first: "c1002" (a creature), "i501" (an item).</summary>
        public string[] recent = new string[0];
        /// <summary>The items list was cut to the settings' budget (the rest is in this PC's file only).</summary>
        public bool cut;
    }

    /// <summary>
    /// Wave 28 (wcodex): what the player has written in the Codex, PER ACCOUNT (the design's design, point 6; FLAGGED: per account, not per
    /// character: every character of the account shares one book). Discoveries only grow, so two copies of an account's book are always
    /// joined (a union), never replaced: the account's settings that come in from Loki Host (whole file, newest wins) lose nothing written
    /// on this PC since.
    ///   Where       the account's settings (synced through Loki Host: World/AccountSettingsSync.cs) hold the account's book while it is
    ///               the one logged in; this PC's codex.json (in the per-user folder) holds every account's book that played here, so a
    ///               second account on this PC never inherits or overwrites the first one's (the settings file is one per Windows user).
    ///   Budget      the account's whole settings must stay under 60,000 bytes (Net/PresenceWire.cs). The book's share is capped at
    ///               <see cref="SyncBudget"/> characters; past it the newest items stay in this PC's file only (logged; a full Bestiarium
    ///               is ~4,000 characters, 10,000 items ~20,000).
    ///   Saving      at most every <see cref="SaveEvery"/> s while new entries come (a town's shops can bring dozens at once), and at once
    ///               when the book closes or the game quits.
    /// </summary>
    public sealed class CodexJournal
    {
        public const int TierNone = 0, TierSeen = 1, TierFull = 2;
        public const int RecentMax = 8, SyncBudget = 30000;
        public const float SaveEvery = 3f;
        private readonly HashSet<int> _full = new HashSet<int>(), _seen = new HashSet<int>(), _items = new HashSet<int>(), _heard = new HashSet<int>();
        /// <summary>An NPC named it (unknown &lt; heard of &lt; written: seen only &lt; full). A creature written down is not "heard of" any more.</summary>
        public bool HeardOf(int mob) => _heard.Contains(mob) && Tier(mob) == TierNone;
        public int Heard => _heard.Count;
        /// <summary>An NPC's line named it: true when it is new to the book (not written down, not heard of before).</summary>
        public bool AddHeard(int mob, string how)
        {
            if (mob <= 0 || CodexData.IsExcluded(mob)) return false;
            mob = CodexData.CanonicalId(mob);
            if (Account.Length == 0 || Tier(mob) != TierNone || !_heard.Add(mob)) return false;
            Changed(); Debug.Log($"[Codex] creature {mob} heard of ({how}): {_heard.Count} heard of");
            HeardAdded?.Invoke(mob);
            return true;
        }
        public event Action<int> HeardAdded;
        private readonly List<int> _pending = new List<int>();
        /// <summary>Creatures heard of since the book was last opened on them, oldest first.</summary>
        public IReadOnlyList<int> PendingHeard => _pending;
        public void AddPending(int mob) { if (mob <= 0 || CodexData.IsExcluded(mob)) return; mob = CodexData.CanonicalId(mob); _pending.Remove(mob); _pending.Add(mob); Changed(); }
        public void ClearPending() { if (_pending.Count == 0) return; _pending.Clear(); Changed(); }
        private static string Ids(List<int> l) => string.Join(",", l);
        private void JoinPending(string s) { if (string.IsNullOrEmpty(s)) return; foreach (var p in s.Split(',')) if (int.TryParse(p, out int id) && id > 0 && !CodexData.IsExcluded(id)) { id = CodexData.CanonicalId(id); if (!_pending.Contains(id)) _pending.Add(id); } }
        private readonly List<string> _recent = new List<string>();
        public string Account { get; private set; } = "";
        /// <summary>A creature was written down or completed (id, tier, how: "look", "kill"), an item written down (id, how).</summary>
        public event Action<int, int, string> CreatureAdded;
        public event Action<int, string> ItemAdded;
        public int Creatures => _full.Count + _seen.Count;
        public int Items => _items.Count;
        public IEnumerable<int> CreatureIds { get { foreach (var i in _full) yield return i; foreach (var i in _seen) yield return i; } }
        public IEnumerable<int> ItemIds => _items;
        public IReadOnlyList<string> Recent => _recent;
        public int Tier(int mob) => _full.Contains(mob) ? TierFull : _seen.Contains(mob) ? TierSeen : TierNone;
        public bool HasItem(int item) => _items.Contains(item);
        /// <summary>All versions of <paramref name="mob"/>'s canonical creature that the player has encountered (seen or full).</summary>
        public List<int> EncounteredVersions(int mob)
        {
            var res = new List<int>();
            foreach (var v in CodexData.VersionsOf(mob))
                if (Tier(v) != TierNone) res.Add(v);
            return res;
        }
        private bool _dirty; private float _dirtySince = -1f;
        public int Saves { get; private set; }
        public bool Cut { get; private set; }

        public static string FilePath => Path.Combine(World.UserSettings.Dir, "codex.json");
        [Serializable] private sealed class Archive { public CodexSave[] accounts = new CodexSave[0]; }

        public static string AccountIdPart(string acct)
        {
            if (string.IsNullOrEmpty(acct)) return "";
            int h = acct.LastIndexOf('#');
            return h >= 0 ? acct.Substring(h + 1).Trim() : "";
        }

        public static bool SameAccount(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            if (a == b) return true;
            string ia = AccountIdPart(a), ib = AccountIdPart(b);
            return ia.Length > 0 && ia == ib;
        }

        /// <summary>Ensures UserSettings.Prefs.codex never carries another account's book into AccountSettingsSync.Push when switching accounts on the same PC, while unifying same-account books across server address changes.</summary>
        public static void ArchiveIfOtherAccount(string account)
        {
            if (string.IsNullOrEmpty(account)) return;
            var s = World.UserSettings.Prefs.codex;
            if (s != null && SameAccount(s.acct, account))
            {
                KeepInArchive(s);
                s.acct = account;
                return;
            }
            if (s != null && !string.IsNullOrEmpty(s.acct)) KeepInArchive(s);
            World.UserSettings.Prefs.codex = new CodexSave { acct = account };
        }

        /// <summary>Merges all archive entries for <paramref name="s"/>'s account from codex.json into <paramref name="s"/> in place (used before AccountSettingsSync.Push so an immediate push on login never sends an unmerged or empty Codex).</summary>
        public static void MergeArchiveInto(CodexSave s)
        {
            if (s == null || string.IsNullOrEmpty(s.acct)) return;
            var j = new CodexJournal { Account = s.acct };
            var arc = ReadArchive();
            if (arc.accounts != null)
                foreach (var a in arc.accounts)
                    if (a != null && SameAccount(a.acct, s.acct))
                        j.Join(a);
            j.Join(s);
            j.WriteInto(s);
        }

        /// <summary>The account now logged in: its book from the account's settings (when they are its) and from this PC's file, joined.</summary>
        public void Use(string account)
        {
            if (string.IsNullOrEmpty(account) || account == Account) return;
            if (Account.Length > 0 && _dirty) Save("the account changed");
            Account = account; _full.Clear(); _seen.Clear(); _items.Clear(); _heard.Clear(); _pending.Clear(); _recent.Clear(); _dirty = false;
            var arc = ReadArchive();
            int fromFile = 0;
            if (arc.accounts != null)
                foreach (var a in arc.accounts)
                    if (a != null && SameAccount(a.acct, account))
                        fromFile += Join(a);
            var s = World.UserSettings.Prefs.codex;
            bool same = s != null && (SameAccount(s.acct, account) || string.IsNullOrEmpty(s.acct));
            int fromSettings = same ? Join(s) : 0;
            Debug.Log($"[Codex] account {account}: {_full.Count} creatures in full, {_seen.Count} seen only, {_items.Count} items (this PC's file {fromFile}, the account's settings {fromSettings}{(s != null && s.acct.Length > 0 && !same ? $"; the settings held '{s.acct}''s book: kept in this PC's file" : "")})");
            if (s != null && s.acct.Length > 0 && !same) KeepInArchive(s);
            if (s == null || !same) World.UserSettings.Prefs.codex = new CodexSave { acct = account };
            else s.acct = account;
            SaveArchiveOnly();
            _settingsStale = true; // (written into the account's settings once the account's sync has answered: SettingsGate)
        }

        /// <summary>True once the book may be written into the account's settings: the account's sync gave its first answer (it took the
        /// host's copy, sent this PC's, or found them in step), or there is no sync to wait for. Before it, a save of the settings would
        /// make this PC's copy the newer one and the sync would send it instead of taking another PC's newer settings (World/
        /// AccountSyncRules.cs Decide): until then the book is kept in this PC's file only.</summary>
        public Func<bool> SettingsGate;
        private bool _settingsStale;
        private bool GateOpen => SettingsGate == null || SettingsGate();

        /// <summary>The account's settings came in from the host (they replace this PC's whole file): what they hold is joined in, and
        /// what this PC wrote since goes back with the next save.</summary>
        public void SettingsArrived()
        {
            var s = World.UserSettings.Prefs.codex; if (Account.Length == 0) return;
            int before = _full.Count + _seen.Count + _items.Count;
            bool same = s != null && (SameAccount(s.acct, Account) || string.IsNullOrEmpty(s.acct));
            string incomingEncoded = same ? (s.full + "|" + s.seen + "|" + s.items + "|" + s.heard + "|" + s.pending) : "";
            int got = same ? Join(s) : 0;
            if (s != null && !string.IsNullOrEmpty(s.acct) && !same) KeepInArchive(s);
            var arc = ReadArchive();
            if (arc.accounts != null)
                foreach (var a in arc.accounts)
                    if (a != null && SameAccount(a.acct, Account))
                        Join(a);
            if (s == null || !same) World.UserSettings.Prefs.codex = new CodexSave { acct = Account };
            else s.acct = Account;
            bool more = Encode(false) != incomingEncoded;
            SaveArchiveOnly();
            Debug.Log($"[Codex] the account's settings came in: {got} entries in them, {(_full.Count + _seen.Count + _items.Count) - before} new here; this PC has more than they do: {more}");
            if (more) _settingsStale = true; else _settingsStale = false;
        }

        private int Join(CodexSave s)
        {
            if (s == null) return 0;
            int n = 0;
            foreach (var i in CodexData.Decode(s.full))
            {
                if (CodexData.IsExcluded(i)) continue;
                _seen.Remove(i); if (_full.Add(i)) n++;
                int c = CodexData.CanonicalId(i);
                if (c != i) { _seen.Remove(c); _full.Add(c); }
            }
            foreach (var i in CodexData.Decode(s.seen))
            {
                if (CodexData.IsExcluded(i)) continue;
                if (!_full.Contains(i) && _seen.Add(i)) n++;
                int c = CodexData.CanonicalId(i);
                if (c != i && !_full.Contains(c)) _seen.Add(c);
            }
            foreach (var i in CodexData.Decode(s.items)) if (_items.Add(i)) n++;
            foreach (var i in CodexData.Decode(s.heard)) if (!CodexData.IsExcluded(i) && _heard.Add(CodexData.CanonicalId(i))) n++;
            JoinPending(s.pending);
            if (s.recent != null) foreach (var r in s.recent) if (!string.IsNullOrEmpty(r) && !_recent.Contains(r) && _recent.Count < RecentMax) _recent.Add(r);
            return n;
        }

        private string Encode(bool _) => CodexData.Encode(_full) + "|" + CodexData.Encode(_seen) + "|" + CodexData.Encode(_items) + "|" + CodexData.Encode(_heard) + "|" + Ids(_pending);

        /// <summary>A creature into the book: <paramref name="tier"/> Seen (a boss looked at) or Full. True: new, or seen-only made full.</summary>
        /// <summary>A creature NEWLY written down (it was in neither list: not for a boss made complete by its kill, not for "heard of"): its id
        /// and whether a kill wrote it (false: a look, a boss's look shortened by hits included). For others to hook (wave 28, wcxp: the
        /// Base EXP for a discovery is granted by the server; this only tells when).</summary>
        public static event Action<int, bool> NewlyWritten;
        public bool AddCreature(int mob, int tier, string how)
        {
            if (mob <= 0 || CodexData.IsExcluded(mob) || Account.Length == 0) return false;
            bool fresh = !_full.Contains(mob) && !_seen.Contains(mob);
            if (tier >= TierFull) { if (_full.Contains(mob)) return false; _seen.Remove(mob); _full.Add(mob); }
            else { if (_full.Contains(mob) || _seen.Contains(mob)) return false; _seen.Add(mob); }
            int canon = CodexData.CanonicalId(mob);
            if (canon != mob)
            {
                if (tier >= TierFull) { _seen.Remove(canon); _full.Add(canon); }
                else if (!_full.Contains(canon)) _seen.Add(canon);
            }
            PushRecent("c" + canon); Changed();
            Debug.Log($"[Codex] creature {mob} written {(tier >= TierFull ? "in full" : "(seen only)")} by {how}: {Creatures} creatures");
            CreatureAdded?.Invoke(mob, tier, how);
            if (fresh) { try { NewlyWritten?.Invoke(mob, how == "kill"); } catch (Exception e) { Debug.LogException(e); } }
            return true;
        }
        public bool AddItem(int item, string how)
        {
            if (item <= 0 || Account.Length == 0 || !_items.Add(item)) return false;
            PushRecent("i" + item); Changed();
            if (_items.Count <= 40 || _items.Count % 50 == 0) Debug.Log($"[Codex] item {item} written ({how}): {Items} items");
            ItemAdded?.Invoke(item, how);
            return true;
        }
        private void PushRecent(string key) { _recent.Remove(key); _recent.Insert(0, key); if (_recent.Count > RecentMax) _recent.RemoveAt(_recent.Count - 1); }
        private void Changed() { if (!_dirty) _dirtySince = Time.unscaledTime; _dirty = true; }

        /// <summary>Every frame: saves when entries are waiting and <see cref="SaveEvery"/> s have passed since the first of them.</summary>
        public void Tick()
        {
            if (_dirty && Time.unscaledTime - _dirtySince >= SaveEvery) Save("new entries");
            else if (_settingsStale && Account.Length > 0 && GateOpen) { SettingsArrived(); if (_settingsStale) { WriteSettings(); World.UserSettings.Prefs.saved = true; World.UserSettings.Save(); _settingsStale = false; Debug.Log("[Codex] the book written into the account's settings (their sync has answered)"); } }
        }
        public void SaveNow(string why) { if (_dirty) Save(why); }

        private void WriteSettings()
        {
            var s = World.UserSettings.Prefs.codex ?? (World.UserSettings.Prefs.codex = new CodexSave());
            WriteInto(s);
        }

        private void WriteInto(CodexSave s)
        {
            s.acct = Account; s.full = CodexData.Encode(_full); s.seen = CodexData.Encode(_seen); s.heard = CodexData.Encode(_heard); s.pending = Ids(_pending); s.recent = _recent.ToArray();
            string items = CodexData.Encode(_items);
            Cut = s.full.Length + s.seen.Length + s.heard.Length + items.Length > SyncBudget;
            if (Cut)
            {
                // the newest items past the budget stay in this PC's file only: the settings keep the lowest ids that fit
                var l = new List<int>(_items); l.Sort(); int lo = 0, hi = l.Count;
                while (lo < hi) { int mid = (lo + hi + 1) / 2; if (s.full.Length + s.seen.Length + s.heard.Length + CodexData.Encode(l.GetRange(0, mid)).Length <= SyncBudget) lo = mid; else hi = mid - 1; }
                items = CodexData.Encode(l.GetRange(0, lo));
                Debug.LogWarning($"[Codex] the book is over the account settings' budget ({SyncBudget} characters): {l.Count - lo} items stay in this PC's file only");
            }
            s.items = items; s.cut = Cut;
        }

        private void SaveArchiveOnly()
        {
            if (Account.Length == 0) return;
            var arc = ReadArchive(); var l = new List<CodexSave>(arc.accounts ?? new CodexSave[0]);
            foreach (var a in l) if (a != null && SameAccount(a.acct, Account)) Join(a);
            l.RemoveAll(a => a == null || SameAccount(a.acct, Account));
            l.Add(new CodexSave { acct = Account, full = CodexData.Encode(_full), seen = CodexData.Encode(_seen), items = CodexData.Encode(_items), heard = CodexData.Encode(_heard), pending = Ids(_pending), recent = _recent.ToArray() });
            arc.accounts = l.ToArray(); WriteArchive(arc);
        }

        private void Save(string why)
        {
            if (Account.Length == 0) return;
            SaveArchiveOnly();
            bool settings = GateOpen;
            if (settings) { WriteSettings(); World.UserSettings.Prefs.saved = true; World.UserSettings.Save(); _settingsStale = false; }
            else _settingsStale = true;
            _dirty = false; Saves++;
            var c = World.UserSettings.Prefs.codex;
            Debug.Log($"[Codex] saved ({why}): {_full.Count} creatures in full, {_seen.Count} seen only, {_items.Count} items; this PC's file{(settings ? $" and the account's settings ({c.full.Length + c.seen.Length + c.items.Length} characters)" : " only: the account's sync has not answered yet")}");
        }

        public static void KeepInArchive(CodexSave s)
        {
            if (s == null || string.IsNullOrEmpty(s.acct)) return;
            var arc = ReadArchive(); var l = new List<CodexSave>(arc.accounts ?? new CodexSave[0]);
            var j = new CodexJournal { Account = s.acct };
            foreach (var a in l) if (a != null && SameAccount(a.acct, s.acct)) j.Join(a);
            j.Join(s);
            l.RemoveAll(a => a == null || SameAccount(a.acct, s.acct));
            l.Add(new CodexSave { acct = s.acct, full = CodexData.Encode(j._full), seen = CodexData.Encode(j._seen), items = CodexData.Encode(j._items), heard = CodexData.Encode(j._heard), pending = Ids(j._pending), recent = j._recent.ToArray() });
            arc.accounts = l.ToArray(); WriteArchive(arc);
        }

        private static Archive ReadArchive()
        {
            try { if (File.Exists(FilePath)) return JsonUtility.FromJson<Archive>(File.ReadAllText(FilePath)) ?? new Archive(); }
            catch (Exception e) { Debug.LogWarning("[Codex] " + FilePath + " could not be read: " + e.Message); }
            return new Archive();
        }
        private static void WriteArchive(Archive a)
        {
            try
            {
                Directory.CreateDirectory(World.UserSettings.Dir);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(a));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
            }
            catch (Exception e) { Debug.LogWarning("[Codex] " + FilePath + " could not be written: " + e.Message); }
        }
    }
}
