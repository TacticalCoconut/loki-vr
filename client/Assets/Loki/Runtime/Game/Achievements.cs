using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Loki.IO;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Achievements and titles (research/windows9_packets.md §2-§3; rAthena e985006, PACKETVER 20211103).
    /// The server pushes the log: 0x0a23 ZC_ALL_ACH_LIST (22 + 50n) at login and after a title reward, 0x0a24 ZC_ACH_UPDATE (66) for each
    /// change (an entry with id 0 = header only). Claim a reward with 0x0a25 → 0x0a26 {1 ok / 0 failed, id} — or, for a reward with a title,
    /// a whole new 0x0a23 instead (achievement.cpp:688-694); reward items arrive by mail. Titles: the server owns the TitleIds of achievements
    /// that were complete at login plus those claimed since (achievement.cpp:736-754, :689); change with 0x0a2e {id} → 0x0a2f {0 ok / 1 not
    /// owned, id}, sending the current one again gets no reply. Everyone reads a player's title from 0x0a30 (title_id at 102).
    /// Texts, categories and rewards: data/account/achievements_{pt,en}.json and titles.tsv (tools/clientdata/build_account_tables.py).
    /// </summary>
    public sealed class Achievements
    {
        public sealed class Entry { public int Id; public bool Completed, Rewarded; public readonly int[] Count = new int[10]; public long CompletedAt; }
        public sealed class Info
        {
            public int Id, Major, Minor, Ui, Score, Item, Amount, Title; public string Name = "", Summary = "", Details = "", Group = "";
            public readonly List<(string text, int count)> Res = new List<(string, int)>();
        }

        public readonly Dictionary<int, Entry> Log = new Dictionary<int, Entry>();
        public int TotalScore, Level, LevelPoints, LevelNeed;
        public bool ListReceived { get; private set; }
        /// <summary>Title shown to others (0 = none), from 0x0a30 about ourselves or a successful 0x0a2f.</summary>
        public int CurrentTitle;
        public readonly HashSet<int> Titles = new HashSet<int>();
        private readonly HashSet<int> _doneAtLogin = new HashSet<int>();
        private bool _loginListSeen;
        public int LastClaimResult = -1, LastClaimId, LastTitleResult = -1;
        public event Action Changed;
        public Action<string> Toast;
        /// <summary>Opens the daily check-in window (the Overview footer button).</summary>
        public Action OpenCheckIn;

        private readonly RoSession _s; private readonly ListWindow _win; private readonly Func<Camera> _head; private readonly Action<string, string> _chat;

        public Achievements(RoSession s, ListWindow win, Func<Camera> head, Action<string, string> chat)
        {
            _s = s; _win = win; _head = head; _chat = chat;
            _win.OnCloseClicked = Hide;
        }

        /// <summary>Another character entered the game (ZC_ACCEPT_ENTER): the server sends its whole log again.</summary>
        public void ResetForCharacter()
        {
            Log.Clear(); Titles.Clear(); _doneAtLogin.Clear(); _loginListSeen = false; ListReceived = false;
            TotalScore = Level = LevelPoints = LevelNeed = 0; CurrentTitle = 0; _cat = -1; _win.Hide(); Changed?.Invoke();
        }

        // ------------------------------------------------------------------ data
        private static Dictionary<int, Info> _db; private static Dictionary<int, (string pt, string en)> _titleNames;
        public static IReadOnlyDictionary<int, Info> Db { get { Load(); return _db; } }
        private static void Load()
        {
            if (_db != null) return;
            _db = new Dictionary<int, Info>(); _titleNames = new Dictionary<int, (string, string)>();
            string dir = Path.Combine(Application.streamingAssetsPath, "data", "account");
            try
            {
                var o = MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(Loki.Data.ClientData.File(ClientText.Portuguese ? "account/achievements_pt.json" : "account/achievements_en.json"))));
                foreach (var kv in o)
                {
                    var a = MiniJson.AsObject(kv.Value); if (a == null || !int.TryParse(kv.Key, out int id)) continue;
                    var i = new Info
                    {
                        Id = id, Name = MiniJson.Str(a["n"], ""), Summary = MiniJson.Str(a["s"], ""), Details = MiniJson.Str(a["d"], ""), Group = MiniJson.Str(a["g"], ""),
                        Major = MiniJson.Int(a["ma"]), Minor = MiniJson.Int(a["mi"]), Ui = MiniJson.Int(a["ui"]), Score = MiniJson.Int(a["sc"]),
                        Item = MiniJson.Int(a["item"]), Amount = MiniJson.Int(a["amt"]), Title = MiniJson.Int(a["title"]),
                    };
                    var res = MiniJson.AsList(a["res"]);
                    if (res != null) foreach (var r in res) { var p = MiniJson.AsList(r); if (p != null && p.Count >= 2) i.Res.Add((MiniJson.Str(p[0], ""), MiniJson.Int(p[1]))); }
                    _db[id] = i;
                }
                foreach (var line in File.ReadAllLines(Loki.Data.ClientData.File("account/titles.tsv")))
                {
                    var c = line.Split('\t'); if (c.Length >= 3 && int.TryParse(c[0], out int tid)) _titleNames[tid] = (c[1], c[2]);
                }
                ApplyPack();
            }
            catch (Exception e) { Debug.LogWarning("[Ach] data: " + e.Message); }
        }
        /// <summary>Wave 28 (wtextc): the text pack's account/achievements_(en|pt).json (id: n, s, d, res texts) and account/titles.tsv
        /// (id, pt, en) over the player's tables: a non-empty pack text wins.</summary>
        private static void ApplyPack()
        {
            var pj = Loki.Data.TextPack.Json(ClientText.Portuguese ? "account/achievements_pt.json" : "account/achievements_en.json");
            if (pj != null)
            {
                int applied = 0, unknown = 0;
                foreach (var kv in pj)
                {
                    var a = MiniJson.AsObject(kv.Value);
                    if (a == null || !int.TryParse(kv.Key, out int id) || !_db.TryGetValue(id, out var i)) { unknown++; continue; }
                    if (a.TryGetValue("n", out var n)) i.Name = Loki.Data.TextPack.Pick(MiniJson.Str(n, ""), i.Name);
                    if (a.TryGetValue("s", out var s)) i.Summary = Loki.Data.TextPack.Pick(MiniJson.Str(s, ""), i.Summary);
                    if (a.TryGetValue("d", out var d)) i.Details = Loki.Data.TextPack.Pick(MiniJson.Str(d, ""), i.Details);
                    var res = a.TryGetValue("res", out var r) ? MiniJson.AsList(r) : null;
                    if (res != null)
                        for (int k = 0; k < res.Count && k < i.Res.Count; k++)
                        {
                            var p = MiniJson.AsList(res[k]); string t = p != null && p.Count > 0 ? MiniJson.Str(p[0], "") : "";
                            if (t.Length > 0) i.Res[k] = (t, i.Res[k].count);
                        }
                    applied++;
                }
                Debug.Log($"[TextPack] achievements: {applied} applied, {unknown} not in your game data");
            }
            var tt = Loki.Data.TextPack.Table("account/titles.tsv");
            if (tt != null)
            {
                int applied = 0, unknown = 0; string first = null;
                foreach (var c in tt.Rows)
                {
                    if (!int.TryParse(c[0], out int tid) || !_titleNames.TryGetValue(tid, out var t)) { unknown++; if (first == null) first = c[0]; continue; }
                    _titleNames[tid] = (Loki.Data.TextPack.Pick(c[1], t.pt), Loki.Data.TextPack.Pick(c[2], t.en)); applied++;
                }
                Loki.Data.TextPack.Report(tt, applied, unknown, first);
            }
        }
        /// <summary>Wave 28: every achievement and title text in the game's language (the no-Portuguese check reads them).</summary>
        public static IEnumerable<string> AllTexts()
        {
            Load();
            foreach (var i in _db.Values) { yield return i.Name; yield return i.Summary; yield return i.Details; foreach (var r in i.Res) yield return r.text; }
            foreach (var id in _titleNames.Keys) yield return TitleName(id);
        }
        public static Info Get(int id) { Load(); return _db.TryGetValue(id, out var i) ? i : null; }
        /// <summary>Client wave 4b (w4lang): the names and descriptions come from the GRF's table of the current language
        /// (achievements_pt / _en): a change of language reads the other one at the next use.</summary>
        public static void ReloadForLanguage() { _db = null; _titleNames = null; }
        public static string TitleName(int id)
        {
            Load();
            if (id <= 0) return "";
            return _titleNames.TryGetValue(id, out var t) ? (ClientText.Portuguese && t.pt.Length > 0 ? t.pt : t.en) : Lang.F("Title {0}", id);
        }
        public string NameOf(int id) => Get(id)?.Name ?? Lang.F("Achievement {0}", id);

        private static readonly Dictionary<int, string> Majors = new Dictionary<int, string>
        { { 0, Lang.N("Other") }, { 1, Lang.N("General") }, { 2, Lang.N("Adventure") }, { 3, Lang.N("Collection") }, { 4, Lang.N("Quests") }, { 5, Lang.N("Battle") }, { 6, Lang.N("Achievements") } };
        // minor groups as the client's list numbers them; the adventure ones follow the fields they list (Prontera…, Kiel Hyre / Elmes…, Audhumbla / Ida…,
        // Eclage / Bifrost / Splendide…, Amatsu / Louyang / Ayothaya…, dungeons)
        private static readonly Dictionary<int, string> Minors = new Dictionary<int, string>
        {
            { 10, Lang.N("Character") }, { 11, Lang.N("Economy") }, { 12, Lang.N("Community") }, { 13, Lang.N("Events") },
            { 20, Lang.N("Rune-Midgards") }, { 21, Lang.N("Schwaltzvalt") }, { 22, Lang.N("Arunafeltz") }, { 23, Lang.N("New World") }, { 24, Lang.N("Other regions") }, { 25, Lang.N("Dungeons") }, { 31, Lang.N("Pets") },
        };
        public static string CategoryName(int major, int minor)
        {
            string m = Majors.TryGetValue(major, out var s) ? Lang.T(s) : Lang.T("Other"); // w4lang: the names are table keys (Lang.N above)
            return Minors.TryGetValue(major * 10 + minor, out var n) ? $"{m} · {Lang.T(n)}" : m;
        }

        // ------------------------------------------------------------------ packets
        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x0a23: // ZC_ALL_ACH_LIST len.W count.L score.L level.W left.L right.L {50}*
                {
                    r.Skip(4); int count = r.S32(); TotalScore = r.S32(); Level = r.U16(); LevelPoints = r.S32(); LevelNeed = r.S32();
                    Log.Clear();
                    for (int i = 0; i < count && r.Remaining >= 50; i++) { var e = ReadEntry(ref r); if (e.Id != 0) Log[e.Id] = e; }
                    if (!_loginListSeen) { _loginListSeen = true; foreach (var e in Log.Values) if (e.Completed) _doneAtLogin.Add(e.Id); }
                    ListReceived = true; RecalcTitles();
                    Debug.Log($"[Ach] list: {Log.Count} entries, {Log.Values.Count(e => e.Completed)} complete, score {TotalScore}, level {Level} ({LevelPoints}/{LevelNeed}), titles {Titles.Count}");
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x0a24: // ZC_ACH_UPDATE score.L level.W left.L right.L + one 50-byte entry
                {
                    r.Skip(2); TotalScore = r.S32(); Level = r.U16(); LevelPoints = r.S32(); LevelNeed = r.S32();
                    var e = ReadEntry(ref r);
                    if (e.Id != 0)
                    {
                        Log.TryGetValue(e.Id, out var old);
                        bool removed = !e.Completed && e.CompletedAt == 0 && !e.Rewarded && e.Count.All(c => c == 0) && old != null;
                        if (removed) Log.Remove(e.Id); else Log[e.Id] = e;
                        if (e.Completed && (old == null || !old.Completed))
                        {
                            var info = Get(e.Id);
                            bool reward = info != null && (info.Item > 0 || info.Title > 0); // w4lang: two whole sentences
                            _chat(reward ? Lang.F("Achievement complete: {0}. Claim it in Achievements.", NameOf(e.Id)) : Lang.F("Achievement complete: {0}", NameOf(e.Id)), "ffd060"); Toast?.Invoke(Lang.F("Achievement: {0}", NameOf(e.Id)));
                        }
                        Debug.Log($"[Ach] update {e.Id} complete {e.Completed} rewarded {e.Rewarded} counts [{string.Join(",", e.Count.Take(3))}]");
                    }
                    RecalcTitles(); Changed?.Invoke(); Refresh(); return true;
                }
                case 0x0a26: // ZC_REQ_ACH_REWARD_ACK result.B (1 ok) id.L
                {
                    r.Skip(2); LastClaimResult = r.U8(); LastClaimId = r.S32();
                    _chat(LastClaimResult == 1 ? (Get(LastClaimId)?.Item > 0 ? Lang.F("Reward claimed: {0} (the item comes by mail).", NameOf(LastClaimId)) : Lang.F("Reward claimed: {0}.", NameOf(LastClaimId))) : Lang.F("The reward for {0} can't be claimed.", NameOf(LastClaimId)), LastClaimResult == 1 ? "a8e6a3" : "ffb070");
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x0a2f: // ZC_ACK_CHANGE_TITLE result.B (0 ok, 1 not owned) title.L
                {
                    r.Skip(2); LastTitleResult = r.U8(); int t = r.S32();
                    if (LastTitleResult == 0) { CurrentTitle = Math.Max(0, t); _chat(CurrentTitle > 0 ? Lang.F("Your title is now «{0}».", TitleName(CurrentTitle)) : Lang.T("You no longer show a title."), "a8e6a3"); }
                    else _chat(Lang.T("You don't have that title yet."), "ffb070");
                    Changed?.Invoke(); Refresh(); return true;
                }
                default: return false;
            }
        }

        private static Entry ReadEntry(ref PacketReader r)
        {
            var e = new Entry { Id = r.S32(), Completed = r.U8() != 0 };
            for (int i = 0; i < 10; i++) e.Count[i] = r.S32();
            e.CompletedAt = r.U32(); e.Rewarded = r.U8() != 0;
            return e;
        }

        private void RecalcTitles()
        {
            Titles.Clear();
            foreach (var e in Log.Values)
            {
                var info = Get(e.Id);
                if (info != null && info.Title > 0 && e.Completed && (e.Rewarded || _doneAtLogin.Contains(e.Id))) Titles.Add(info.Title);
            }
        }

        /// <summary>Our own unit's 0x0a30 carried a title id (EntityManager.NamesChanged on Self).</summary>
        public void OnOwnTitle(int titleId) { if (titleId >= 0 && titleId != CurrentTitle) { CurrentTitle = titleId; Changed?.Invoke(); Refresh(); } }

        // ------------------------------------------------------------------ sending
        public void Claim(int achievementId) { LastClaimResult = -1; _s.SendPacket(new PacketWriter(0x0a25).U32((uint)achievementId).ToArray()); Debug.Log($"[Ach] claim {achievementId}"); }
        public void ChangeTitle(int titleId)
        {
            if (titleId == CurrentTitle) return; // the server answers nothing when it's the same (clif.cpp:20774-20776)
            LastTitleResult = -1; _s.SendPacket(new PacketWriter(0x0a2e).U32((uint)Math.Max(0, titleId)).ToArray()); Debug.Log($"[Ach] title → {titleId}");
        }
        public int Claimable => Log.Values.Count(e => e.Completed && !e.Rewarded && HasReward(e.Id));
        public static bool HasReward(int id) { var i = Get(id); return i != null && (i.Item > 0 || i.Title > 0); }

        public static string Progress(Entry e, Info info)
        {
            if (info == null || info.Res.Count == 0) return e != null && e.Completed ? Lang.T("~ach~done") : "";
            var parts = new List<string>();
            for (int i = 0; i < info.Res.Count && i < 10; i++)
            {
                var (text, count) = info.Res[i];
                int have = e != null ? e.Count[i] : 0;
                bool done = e != null && (e.Completed || (count > 0 && have >= count));
                string t = text.Length > 0 ? text : Lang.F("Step {0}", i + 1);
                parts.Add(count > 1 ? $"{t} {Math.Min(have, count)}/{count}" : $"{(done ? "✓" : "·")} {t}");
            }
            return string.Join("   ", parts);
        }
        private static float Fraction(Entry e, Info info)
        {
            if (e == null || info == null || info.Res.Count == 0) return 0;
            float s = 0; int k = 0;
            for (int i = 0; i < info.Res.Count && i < 10; i++) { int c = Math.Max(1, info.Res[i].count); s += Mathf.Clamp01(e.Count[i] / (float)c); k++; }
            return k > 0 ? s / k : 0;
        }
        private string RewardText(Info i)
        {
            if (i == null) return "";
            var p = new List<string>();
            if (i.Item > 0) p.Add($"{GameDb.GetItem(i.Item).Name}{(i.Amount > 1 ? " x" + i.Amount : "")}");
            if (i.Title > 0) p.Add(Lang.F("title «{0}»", TitleName(i.Title)));
            return p.Count > 0 ? Lang.F("Reward: {0}", string.Join(", ", p)) : "";
        }

        // ------------------------------------------------------------------ window
        public enum Page { Overview, InProgress, Browse, Titles }
        public Page Current { get; private set; } = Page.Overview;
        public bool Visible => _win.Visible;
        private static readonly string[] TabNames = { Lang.N("Overview"), Lang.N("In progress"), Lang.N("Browse"), Lang.N("Titles") };
        private int _cat = -1; // browse: major*10+minor, -1 = category list

        public void Show(Page p = Page.Overview) { Current = p; _cat = -1; _win.SetInput(false); _win.Open(_head()); Refresh(); }
        public void Hide() => _win.Hide();

        public void Refresh()
        {
            if (!_win.Visible) return;
            _win.SetTabs(Array.ConvertAll(TabNames, Lang.T), (int)Current, t => { Current = (Page)t; _cat = -1; Refresh(); });
            _win.SetTitle(Lang.T("Achievements"));
            var rows = new List<ListWindow.Row>();
            _win.SetInfo(ListReceived ? Lang.F("Level {0}  ·  {1:N0} / {2:N0} to the next  ·  score {3:N0}", Level, LevelPoints, LevelNeed, TotalScore) + (CurrentTitle > 0 ? $"  ·  «{TitleName(CurrentTitle)}»" : "") : Lang.T("No achievements yet."));
            switch (Current)
            {
                case Page.Overview:
                    foreach (var e in Log.Values.Where(x => x.Completed && !x.Rewarded && HasReward(x.Id)).OrderBy(x => x.CompletedAt))
                    {
                        var info = Get(e.Id); int aid = e.Id;
                        rows.Add(new ListWindow.Row { Text = Lang.F("<color=#ffd060>{0}</color>  <color=#9aa3b5>complete</color>\n<size=19>{1}</size>", NameOf(aid), RewardText(info)), A = Lang.T("Claim"), OnA = () => Claim(aid), Highlight = true });
                    }
                    foreach (var e in Log.Values.Where(x => x.Completed).OrderByDescending(x => x.CompletedAt).Take(12))
                    {
                        if (!e.Rewarded && HasReward(e.Id)) continue;
                        var when = e.CompletedAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(e.CompletedAt).LocalDateTime.ToString("yyyy-MM-dd") : "";
                        rows.Add(new ListWindow.Row { Text = $"{NameOf(e.Id)}  <color=#7fd67f>✓</color>  <color=#9aa3b5>{when} · {Get(e.Id)?.Score ?? 0} pts</color>\n<size=19>{Get(e.Id)?.Summary}</size>" });
                    }
                    if (rows.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>Nothing completed yet. Browse the list to see what there is to do.</color>") });
                    _win.SetFooter((Lang.T("Check-in"), () => OpenCheckIn?.Invoke()), (Lang.T("Close"), Hide));
                    break;
                case Page.InProgress:
                    foreach (var e in Log.Values.Where(x => !x.Completed).OrderByDescending(x => Fraction(x, Get(x.Id))))
                    {
                        var info = Get(e.Id);
                        rows.Add(new ListWindow.Row { Text = $"{NameOf(e.Id)}  <color=#9aa3b5>{CategoryName(info?.Major ?? 0, info?.Minor ?? 0)}</color>\n<size=19>{Progress(e, info)}</size>" });
                    }
                    if (rows.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>No achievement is under way.</color>") });
                    _win.SetFooter((Lang.T("Close"), Hide));
                    break;
                case Page.Browse:
                    if (_cat < 0)
                    {
                        foreach (var g in Db.Values.GroupBy(i => i.Major * 10 + i.Minor).OrderBy(g => g.Key == 0 ? 999 : g.Key))
                        {
                            int key = g.Key, done = g.Count(i => Log.TryGetValue(i.Id, out var e) && e.Completed);
                            rows.Add(new ListWindow.Row { Text = $"{CategoryName(key / 10, key % 10)}  <color=#9aa3b5>{done} / {g.Count()}</color>", A = Lang.T("Open"), OnA = () => { _cat = key; Refresh(); } });
                        }
                        _win.SetFooter((Lang.T("Close"), Hide));
                    }
                    else
                    {
                        _win.SetInfo(CategoryName(_cat / 10, _cat % 10));
                        foreach (var info in Db.Values.Where(i => i.Major * 10 + i.Minor == _cat).OrderBy(i => i.Id))
                        {
                            Log.TryGetValue(info.Id, out var e); int aid = info.Id;
                            string state = e == null ? "" : e.Completed ? (e.Rewarded || !HasReward(aid) ? "  <color=#7fd67f>✓</color>" : "  " + Lang.T("<color=#ffd060>reward waiting</color>")) : "  " + Lang.T("<color=#9aa3b5>under way</color>");
                            string detail = e != null && !e.Completed ? Progress(e, info) : info.Summary;
                            string reward = RewardText(info);
                            rows.Add(new ListWindow.Row
                            {
                                Text = $"{info.Name}{state}  <color=#9aa3b5>{info.Score} pts</color>\n<size=19>{detail}{(reward.Length > 0 ? "  ·  " + reward : "")}</size>",
                                A = e != null && e.Completed && !e.Rewarded && HasReward(aid) ? Lang.T("Claim") : null, OnA = () => Claim(aid),
                            });
                        }
                        _win.SetFooter((Lang.T("Back"), () => { _cat = -1; Refresh(); }), (Lang.T("Close"), Hide));
                    }
                    break;
                case Page.Titles:
                    rows.Add(new ListWindow.Row { Text = CurrentTitle == 0 ? Lang.T("No title  <color=#7fd67f>(in use)</color>") : Lang.T("No title"), A = CurrentTitle == 0 ? null : Lang.T("Use"), OnA = () => ChangeTitle(0) });
                    foreach (var info in Db.Values.Where(i => i.Title > 0).OrderBy(i => i.Title))
                    {
                        int t = info.Title; bool owned = Titles.Contains(t);
                        Log.TryGetValue(info.Id, out var e);
                        string how = owned ? "" : e != null && e.Completed ? Lang.F("claim «{0}» first", info.Name) : Lang.F("from «{0}»", info.Name);
                        rows.Add(new ListWindow.Row
                        {
                            Text = $"«{TitleName(t)}»{(t == CurrentTitle ? "  " + Lang.T("<color=#7fd67f>(in use)</color>") : "")}{(how.Length > 0 ? $"\n<size=19><color=#9aa3b5>{how}</color></size>" : "")}",
                            A = owned && t != CurrentTitle ? Lang.T("Use") : null, OnA = () => ChangeTitle(t),
                        });
                    }
                    _win.SetFooter((Lang.T("Close"), Hide));
                    break;
            }
            _win.SetRows(rows);
        }
    }
}
