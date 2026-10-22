using System;
using System.Collections.Generic;
using System.Linq;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 11 (w11menu), the second half of the design's 2026-10-06 request for the console commands ("one that lists out the IDs
    /// for all the items in the game ..."): pickers that fill a command's parameters without typing. One list window, four tabs:
    ///   Items     every item of the player's own game data (GameDb.AllItems: the tables made from their GRF) with its ID; a search
    ///             box (the VR keyboard): digits find an ID, anything else the name (the data's name or its English name); a type
    ///             filter; "x1" / "x10" send @item ‹id› ‹amount›;
    ///   Jobs      the job table the skill window uses (SkillTreeDb): "Change" sends @job ‹id›;
    ///   Places    @go's towns (conf/atcommands.yml:474-496: "Go" sends @go ‹number›), and with a search the maps of the client's
    ///             own map list (Navigation.Maps): "Warp" sends @warp ‹map›;
    ///   Monsters  the monsters of the client's navigation data (one row per monster): "x1" / "x5" send @monster ‹id or name› ‹amount›.
    /// Only the rows of the page shown are drawn (UI/ListWindow.cs pages 7 at a time); a search that matches more than
    /// <see cref="MaxRows"/> lists the first of them and says so. The commands go the chat line's own way (as Game/GmCommandList.cs
    /// sends): the server answers, and refuses them for an account without the rights.
    /// </summary>
    public sealed class GmPickers
    {
        public enum Mode { Items, Jobs, Places, Monsters }
        public const int MaxRows = 140;
        private static readonly string[] TabNames = { Lang.N("Items"), Lang.N("Jobs"), Lang.N("Places"), Lang.N("Monsters") };
        private static readonly string[] TypeNames = { Lang.N("~pick~every type"), Lang.N("~pick~usable"), Lang.N("~pick~equipment"), Lang.N("~pick~cards"), Lang.N("~pick~other") };
        /// <summary>@go's towns in rAthena's own order (conf/atcommands.yml:474-496, numbers 0-36).</summary>
        public static readonly string[] GoTowns =
        {
            "prontera", "morocc", "geffen", "payon", "alberta", "izlude", "aldebaran", "xmas (lutie)", "comodo", "yuno", "amatsu", "gonryun", "umbala", "niflheim", "louyang",
            "start point", "prison", "jawaii", "ayothaya", "einbroch", "lighthalzen", "einbech", "hugel", "rachel", "veins", "moscovia", "midgard camp", "manuk", "splendide",
            "brasilis", "el dicastes", "mora", "dewata", "malangdo", "malaya port", "eclage", "lasagna",
        };

        private readonly ListWindow _win; private readonly Func<Camera> _head;
        public Action<string> Send;
        public Mode Current { get; private set; }
        private int _type; private string _query = ""; private Dictionary<string, int> _mobIds;
        /// <summary>How many entries the search matched (before the cap), and the commands sent (smoke, log).</summary>
        public int Matches { get; private set; }
        public int Sent { get; private set; }
        public string LastSent { get; private set; } = "";
        public bool Visible => _win.Visible;
        public int RowCount => _win.RowCount;
        public string RowText(int i) => _win.RowText(i);
        public void Press(int i, bool a = true) => _win.Press(i, a);
        public string Query => _query;

        public GmPickers(ListWindow win, Func<Camera> head) { _win = win; _head = head; _win.OnCloseClicked = Hide; }

        // ------------------------------------------------------------------ client wave 14 (w14play), point 30 of the design's headset list
        // "Those classes should not be offered on the commands tab ... when selected, they end up just looking like the apprentice" and
        // "there are substantial number of jobs on the commands tab that do not do anything ... remove them". The Jobs tab offers a job
        // only when rAthena's @job really changes to it (RathenaJob) AND the player's own GRF has its body (HasBody, both sexes; a job
        // of one sex: that sex's), so a later RO patch that adds a body brings the job back.
        /// <summary>The player's sex (rAthena's: 0 female, 1 male).</summary>
        public Func<int> Sex { get; set; }
        /// <summary>Whether the player's GRF has the body sprite of a job for a sex (set by the game: Sprites/RoNames.cs + the assets).</summary>
        public Func<int, int, bool> HasBody { get; set; }

        /// <summary>The job ids rAthena's @job &lt;number&gt; changes to (the oracle's src, read 2026-10-07): ACMD_FUNC(jobchange)
        /// (atcommand.cpp) refuses the dummy jobs it names (KNIGHT2 13, CRUSADER2 21, WEDDING 22, XMAS 26, SUMMER 27, HANBOK 28,
        /// OKTOBERFEST 29, SUMMER2 30, LORD_KNIGHT2 4014, PALADIN2 4022, BABY_KNIGHT2 4036, BABY_CRUSADER2 4044, STAR_GLADIATOR2 4048,
        /// RUNE_KNIGHT2 … MECHANIC_T2 4080-4087, BABY_RUNE_KNIGHT2 … BABY_MECHANIC2 4109-4112, BABY_STAR_GLADIATOR2 4238,
        /// STAR_EMPEROR2 4243, BABY_STAR_EMPEROR2 4244, WINDHAWK2 … IMPERIAL_GUARD2 4278-4281, SKY_EMPEROR2 4316) and every id
        /// pcdb_checkid (pc.hpp:1219-1235) rejects (4053, 4113-4189, 4192-4210, 4213-4214, 4216-4217, 4219, 4221, 4245-4251,
        /// 4265-4277, 4282-4301, 4309-4315 and up); pc_jobchange (pc.cpp) also needs a class pc_jobid2mapid knows and a row in the
        /// job database (db/re/job_stats.yml and its imports): every id left here has both. Checked id by id on the w14play rAthena
        /// copy by the smoke w14play (LokiGame.W14PlaySmoke.cs).</summary>
        private static readonly HashSet<int> RathenaJobIds = Ids((0, 12), (14, 20), (23, 25), (4001, 4013), (4015, 4021), (4023, 4035), (4037, 4043), (4045, 4047),
            (4049, 4052), (4054, 4079), (4096, 4108), (4190, 4191), (4211, 4212), (4215, 4215), (4218, 4218), (4220, 4220), (4222, 4229), (4239, 4242),
            (4252, 4264), (4302, 4308));
        /// <summary>The jobs of one sex: pc_mapid2jobid (pc.cpp) gives the male one to a man and the female one to a woman whichever of the
        /// two is asked for (Bard / Dancer, Clown / Gypsy, Baby Bard / Baby Dancer, Minstrel / Wanderer and their transcendent and baby
        /// forms, Kagerou / Oboro and the babies, Troubadour / Trouvere, Shinkiro / Shiranui).</summary>
        private static readonly (int male, int female)[] SexPairs = { (19, 20), (4020, 4021), (4042, 4043), (4068, 4069), (4075, 4076), (4104, 4105), (4211, 4212), (4223, 4224), (4263, 4264), (4304, 4305) };
        private static HashSet<int> Ids(params (int a, int b)[] ranges) { var s = new HashSet<int>(); foreach (var (a, b) in ranges) for (int i = a; i <= b; i++) s.Add(i); return s; }

        /// <summary>The job "@job <paramref name="id"/>" gives a character of <paramref name="sex"/> on rAthena, or -1 when it refuses.</summary>
        public static int RathenaJob(int id, int sex)
        {
            if (!RathenaJobIds.Contains(id)) return -1;
            foreach (var (m, f) in SexPairs) if (id == m || id == f) return sex == 0 ? f : m;
            return id;
        }

        /// <summary>The sexes a job is held by (both, or the one of a sex-bound job).</summary>
        public static int[] SexesOf(int job)
        {
            foreach (var (m, f) in SexPairs) { if (job == m) return new[] { 1 }; if (job == f) return new[] { 0 }; }
            return new[] { 0, 1 };
        }

        /// <summary>One row of the job table the Jobs tab looks at: what @job gives, and why a row is left out ("" when offered).</summary>
        public struct JobRow { public int Row, Job; public string Name, LeftOut; }

        /// <summary>Every named row of the job table (SkillTreeDb) for a character of <paramref name="sex"/>: offered or left out, and why.</summary>
        public List<JobRow> JobRows(int sex)
        {
            var named = new HashSet<int>(SkillTreeDb.AllJobs().Where(j => j != null && j.Id >= 0 && j.Name.Length > 0).Select(j => j.Id));
            var list = new List<JobRow>();
            foreach (var j in SkillTreeDb.AllJobs().Where(j => j != null && j.Id >= 0 && j.Name.Length > 0).OrderBy(j => j.Id))
            {
                var r = new JobRow { Row = j.Id, Name = j.Name, Job = RathenaJob(j.Id, sex), LeftOut = "" };
                if (r.Job < 0) r.LeftOut = "@job refuses it";
                else if (r.Job != j.Id && named.Contains(r.Job)) r.LeftOut = $"@job gives this sex {r.Job}, its own row";
                else if (HasBody != null) { foreach (int s in SexesOf(r.Job)) if (!HasBody(r.Job, s)) { r.LeftOut = $"no body in the GRF ({(s == 0 ? "female" : "male")})"; break; } }
                list.Add(r);
            }
            return list;
        }
        public void Hide() => _win.Hide();

        public void Show(Mode m)
        {
            Current = m; _query = "";
            _win.Open(_head());
            Refresh(true);
        }

        /// <summary>The search box's text (the smoke types here; a player types on the VR keyboard and presses Enter or "Search").</summary>
        public void Search(string text) { _query = (text ?? "").Trim(); if (_win.Input != null) _win.Input.SetText(_query); Refresh(true); }

        private void Do(string line)
        {
            Sent++; LastSent = line;
            Debug.Log($"[Commands] picker sent: '{line}'");
            Send?.Invoke(line);
        }

        private static bool Has(string s, string q) => !string.IsNullOrEmpty(s) && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

        public void Refresh(bool top = false)
        {
            if (!_win.Visible) return;
            _win.SetTitle(Lang.T("Console pickers"));
            _win.SetTabs(Array.ConvertAll(TabNames, Lang.T), (int)Current, t => { Current = (Mode)t; _query = ""; Refresh(true); });
            _win.SetInput(true, Current == Mode.Places ? Lang.T("a map's name") : Lang.T("a name or an ID"), null, () => { _query = (_win.Input.Text ?? "").Trim(); Refresh(true); }, _query, 40);
            string q = _query; bool digits = q.Length > 0 && q.All(char.IsDigit);
            var rows = new List<ListWindow.Row>(); int total = 0;
            switch (Current)
            {
                case Mode.Items:
                {
                    IEnumerable<GameDb.Item> items = GameDb.AllItems().Where(i => i != null && i.Id > 0);
                    if (_type == 1) items = items.Where(i => i.IsConsumable); else if (_type == 2) items = items.Where(i => i.Type == 4 || i.Type == 5 || i.Type == 12);
                    else if (_type == 3) items = items.Where(i => i.Type == 6); else if (_type == 4) items = items.Where(i => !i.IsConsumable && i.Type != 4 && i.Type != 5 && i.Type != 6 && i.Type != 12);
                    if (digits) items = items.Where(i => i.Id.ToString().StartsWith(q)); else if (q.Length > 0) items = items.Where(i => Has(i.Name, q) || Has(i.English, q));
                    var list = (digits ? items.OrderBy(i => i.Id.ToString().Length).ThenBy(i => i.Id) : items.OrderBy(i => i.Id)).ToList();
                    total = list.Count;
                    foreach (var it in list.Take(MaxRows))
                    {
                        int id = it.Id;
                        rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(it), Text = $"<color=#ffd060>{id}</color>   {it.Display}   <color=#9aa3b5>{it.TypeName}</color>", A = "x1", OnA = () => Do($"@item {id} 1"), B = "x10", OnB = () => Do($"@item {id} 10") });
                    }
                    break;
                }
                case Mode.Jobs:
                {
                    // client wave 14 (w14play), point 30: only the jobs @job really gives this character, with a body in its GRF (JobRows)
                    int sex = Sex != null ? Sex() : 1;
                    var list = JobRows(sex).Where(r => r.LeftOut.Length == 0 && (q.Length == 0 || (digits ? r.Job.ToString().StartsWith(q) : Has(r.Name, q) || Has(SkillTreeDb.JobName(r.Row, sex), q)))).ToList();
                    total = list.Count;
                    foreach (var r in list.Take(MaxRows))
                    {
                        int id = r.Job; string shown = SkillTreeDb.JobName(r.Row, sex);
                        rows.Add(new ListWindow.Row { Text = $"<color=#ffd060>{id}</color>   {shown}{(shown != r.Name ? $"   <color=#9aa3b5>({r.Name})</color>" : "")}", A = Lang.T("~pick~Change"), OnA = () => Do($"@job {id}") });
                    }
                    break;
                }
                case Mode.Places:
                {
                    const string academy = "Criatura Academy (iz_ac01)";
                    if (q.Length == 0 || Has(academy, q))
                    {
                        total++;
                        rows.Add(new ListWindow.Row { Text = $"<color=#ffd060>@warp iz_ac01 102 44</color>   {Lang.T("Criatura Academy")}", A = Lang.T("~pick~Go"), OnA = () => Do("@warp iz_ac01 102 44") });
                    }
                    for (int i = 0; i < GoTowns.Length; i++)
                    {
                        if (q.Length > 0 && !Has(GoTowns[i], q)) continue;
                        int n = i; total++;
                        rows.Add(new ListWindow.Row { Text = $"<color=#ffd060>@go {n}</color>   {GoTowns[n]}", A = Lang.T("~pick~Go"), OnA = () => Do($"@go {n}") });
                    }
                    if (q.Length > 0)
                    {
                        var maps = Navigation.Maps.Values.Where(m => m != null && (Has(m.Map, q) || Has(m.Name, q) || Has(ClientText.MapName(m.Map), q))).OrderBy(m => m.Map, StringComparer.OrdinalIgnoreCase).ToList();
                        total += maps.Count;
                        foreach (var m in maps.Take(Mathf.Max(0, MaxRows - rows.Count)))
                        {
                            string map = m.Map;
                            rows.Add(new ListWindow.Row { Text = $"<color=#ffd060>{map}</color>   {ClientText.MapName(map) ?? m.Name}", A = Lang.T("~pick~Warp"), OnA = () => Do($"@warp {map}") });
                        }
                    }
                    break;
                }
                default:
                {
                    // the navigation data names a monster by its server (Aegis) name and level, not by its id; @monster takes that
                    // name (atcommand.cpp monster: mobdb_search_aegisname first). Its id is shown and searched where the client's
                    // identity table has a sprite of the same name (Sprites/RoNames.cs)
                    if (_mobIds == null)
                    {
                        _mobIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kv in Sprites.RoNames.NpcSprites()) if (kv.Key > 1000 && !string.IsNullOrEmpty(kv.Value) && !_mobIds.ContainsKey(kv.Value)) _mobIds[kv.Value] = kv.Key;
                    }
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var list = new List<(string aegis, int level, int id)>();
                    foreach (var m in Navigation.AllMobs())
                    {
                        if (m == null || string.IsNullOrEmpty(m.Aegis) || !seen.Add(m.Aegis)) continue;
                        int id = _mobIds.TryGetValue(m.Aegis, out int v) ? v : 0;
                        if (q.Length > 0 && !(digits ? id > 0 && id.ToString().StartsWith(q) : Has(m.Aegis, q) || Has(m.Name, q))) continue;
                        list.Add((m.Aegis, m.Level, id));
                    }
                    list.Sort((a, b) => digits && a.id != b.id ? a.id.CompareTo(b.id) : string.Compare(a.aegis, b.aegis, StringComparison.OrdinalIgnoreCase));
                    total = list.Count;
                    foreach (var m in list.Take(MaxRows))
                    {
                        string arg = m.id > 0 ? m.id.ToString() : m.aegis;
                        rows.Add(new ListWindow.Row { Text = $"<color=#ffd060>{(m.id > 0 ? m.id.ToString() : "—")}</color>   {m.aegis}   <color=#9aa3b5>Lv {m.level}</color>", A = "x1", OnA = () => Do($"@monster {arg} 1"), B = "x5", OnB = () => Do($"@monster {arg} 5") });
                    }
                    break;
                }
            }
            Matches = total;
            string count = total > MaxRows ? Lang.F("{0} found, the first {1} shown: type more of the name", total, MaxRows) : total == 1 ? Lang.T("1 found") : Lang.F("{0} found", total);
            if (Current == Mode.Places && q.Length == 0) count = Lang.T("@go's towns; type a name to search every map");
            _win.SetInfo(count);
            _win.SetRows(rows, top);
            if (Current == Mode.Items) _win.SetFooter((Lang.T("Close"), Hide), (Lang.T("Search"), () => { _query = (_win.Input.Text ?? "").Trim(); Refresh(true); }), (Lang.T(TypeNames[_type]), () => { _type = (_type + 1) % TypeNames.Length; Refresh(true); }));
            else _win.SetFooter((Lang.T("Close"), Hide), (Lang.T("Search"), () => { _query = (_win.Input.Text ?? "").Trim(); Refresh(true); }));
        }

    }
}
