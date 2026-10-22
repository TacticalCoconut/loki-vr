using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 3 (w3skills): the skill window's client data, <c>data/skilltree.tsv</c>, made by
    /// <c>tools/clientdata/build_skilltree.py</c> from the RO client's own Lua tables (skillinfoz\skilltreeview.lub,
    /// jobinheritlist.lub, skillinfolist_data.lub + _enus.lub, datainfo\pcjobnamegender_enus.lub). Nothing here comes from
    /// the server: rAthena never sends the tree, the positions or the prerequisites (clif_skillinfoblock, clif.cpp:5696-5750,
    /// sends only the skills a character already has or may learn now), so this works unchanged against any server that sends
    /// the RO skill packets.
    /// The prerequisite and inherit rules are the official client's own, read from its script
    /// <c>skillinfoz\skillinfo_f.lub</c> (Lua 5.1 bytecode, decompiled by hand; see <see cref="Needs"/> and <see cref="Chain"/>).
    /// </summary>
    public static class SkillTreeDb
    {
        public sealed class Job
        {
            public int Id, Parent = -1, Parent2 = -1;
            public string Const = "", Name = "";
            /// <summary>Client wave 5 (w5ui): the Portuguese names from the player's GRF (pcjobnamegender_ptbr.lub PCJobNameTableMan /
            /// PCJobNameTableWoman: "Cavaleiro" / "Cavaleira"), skilltree.tsv J columns 8 and 9; "" when the GRF has none.</summary>
            public string NamePt = "", NamePtFemale = "";
            /// <summary>JobSkillTab.ChangeSkillTabName's names for this job's tabs ('1a', '3a', '4a' …), empty when none is registered.</summary>
            public string[] TabNames = new string[0];
        }
        public sealed class Extra { public string Type = ""; public bool SeparateLevels; public int[] Range = new int[0], Ap = new int[0]; }

        private static Dictionary<int, Job> _jobs;
        private static Dictionary<int, SortedDictionary<int, int>> _tree;           // job → grid position → skill
        private static Dictionary<int, List<(int skill, int level)>> _needAll;      // skill → _NeedSkillList
        private static Dictionary<(int skill, int job), List<(int skill, int level)>> _needJob; // skill, job → NeedSkillList[job]
        private static Dictionary<int, HashSet<int>> _needJobs;                     // skill → jobs that have their own list
        private static Dictionary<int, Extra> _extra;
        private static readonly Extra NoExtra = new Extra();
        public static bool Loaded { get { Load(); return _jobs.Count > 0; } }

        private static void Load()
        {
            if (_jobs != null) return;
            _jobs = new Dictionary<int, Job>(); _tree = new Dictionary<int, SortedDictionary<int, int>>();
            _needAll = new Dictionary<int, List<(int, int)>>(); _needJob = new Dictionary<(int, int), List<(int, int)>>();
            _needJobs = new Dictionary<int, HashSet<int>>(); _extra = new Dictionary<int, Extra>();
            string path = Loki.Data.ClientData.File("skilltree.tsv");
            try
            {
                foreach (var line in File.ReadLines(path, Encoding.UTF8))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    var c = line.Split('\t');
                    switch (c[0])
                    {
                        case "J" when c.Length >= 7:
                            _jobs[Int(c[1])] = new Job { Id = Int(c[1]), Const = c[2], Parent = Int(c[3]), Parent2 = Int(c[4]), Name = c[5], TabNames = c[6].Length > 0 ? c[6].Split(',') : new string[0],
                                                         NamePt = c.Length > 7 ? c[7] : "", NamePtFemale = c.Length > 8 ? c[8] : "" }; // client wave 5 (w5ui)
                            break;
                        case "T" when c.Length >= 4:
                        {
                            int job = Int(c[1]);
                            if (!_tree.TryGetValue(job, out var t)) _tree[job] = t = new SortedDictionary<int, int>();
                            t[Int(c[2])] = Int(c[3]);
                            break;
                        }
                        case "N" when c.Length >= 4:
                        {
                            int sk = Int(c[1]), job = Int(c[2]);
                            var l = new List<(int, int)>();
                            foreach (var p in c[3].Split(',')) { int k = p.IndexOf(':'); if (k > 0) l.Add((Int(p.Substring(0, k)), Int(p.Substring(k + 1)))); }
                            if (job < 0) _needAll[sk] = l;
                            else { _needJob[(sk, job)] = l; if (!_needJobs.TryGetValue(sk, out var s)) _needJobs[sk] = s = new HashSet<int>(); s.Add(job); }
                            break;
                        }
                        case "S" when c.Length >= 6:
                            _extra[Int(c[1])] = new Extra { Type = c[2], SeparateLevels = c[3] == "1", Range = Ints(c[4]), Ap = Ints(c[5]) };
                            break;
                    }
                }
                // wave 28 (wtextc): the text pack's job names (skilltree.tsv: job, name_en, name_pt) over the player's
                var tp = Loki.Data.TextPack.Table("skilltree.tsv");
                if (tp != null)
                {
                    int applied = 0, unknown = 0; string first = null;
                    foreach (var c in tp.Rows)
                    {
                        if (!_jobs.TryGetValue(Int(c[0]), out var j)) { unknown++; if (first == null) first = c[0]; continue; }
                        j.Name = Loki.Data.TextPack.Pick(c[1], j.Name); j.NamePt = Loki.Data.TextPack.Pick(c[2], j.NamePt); applied++;
                    }
                    Loki.Data.TextPack.Report(tp, applied, unknown, first);
                }
                Debug.Log($"[SkillTree] {_jobs.Count} jobs, {_tree.Count} job trees, {_needAll.Count + _needJob.Count} prerequisite lists, {_extra.Count} skills");
            }
            catch (Exception e) { Debug.LogError("[SkillTree] load failed (" + path + "): " + e.Message); }
        }
        private static int Int(string s) => int.TryParse(s, out int v) ? v : -1;
        private static int[] Ints(string s) { var l = new List<int>(); foreach (var x in s.Split(',')) if (int.TryParse(x, out int v)) l.Add(v); return l.ToArray(); }

        public static Job GetJob(int id) { Load(); return _jobs.TryGetValue(id, out var j) ? j : null; }
        /// <summary>Client wave 11 (w11menu): every job of the table (the console commands' job picker: Game/GmPickers.cs).</summary>
        public static IEnumerable<Job> AllJobs() { Load(); return _jobs.Values; }
        public static string JobName(int id) => JobName(id, 1);
        /// <summary>Client wave 5 (w5ui): the job's name in the language the player chose — the GRF's Portuguese table (the female form
        /// for <paramref name="sex"/> 0 = rAthena's SEX_FEMALE, common/mmo.hpp:1115-1117; the male one otherwise, as the English column
        /// is PCJobNameTableMan) when Portuguese is on and the GRF has a name, else the English one.</summary>
        public static string JobName(int id, int sex)
        {
            var j = GetJob(id);
            if (j == null) return "Job " + id;
            if (ClientText.Portuguese) { string pt = JobNamePt(id, sex); if (pt.Length > 0) return pt; }
            return j.Name.Length > 0 ? j.Name : "Job " + id;
        }
        /// <summary>The GRF's Portuguese name of a job ("" when the client data has none): the female form for sex 0, else the male.</summary>
        public static string JobNamePt(int id, int sex)
        {
            var j = GetJob(id);
            if (j == null) return "";
            return sex == 0 && j.NamePtFemale.Length > 0 ? j.NamePtFemale : j.NamePt;
        }
        public static SortedDictionary<int, int> TreeOf(int job) { Load(); return _tree.TryGetValue(job, out var t) ? t : null; }
        /// <summary>
        /// The grid a job shows: its own SKILL_TREEVIEW_FOR_JOB entry, or, for the transcendent 3rd jobs that have none
        /// (JT_RUNE_KNIGHT_H 4060 …), the one of the job JOB_INHERIT_LIST2 names (JT_RUNE_KNIGHT 4054): the same mapping the
        /// client's own skillinfo_f.lub applies to their prerequisite lists; roBrowserLegacy gives them the same tree
        /// (rB: DB/Skills/SkillTreeView.js duplicateEntry(RUNE_KNIGHT, RUNE_KNIGHT_H, …)).
        /// </summary>
        public static SortedDictionary<int, int> ViewTreeOf(int job)
        {
            var t = TreeOf(job); if (t != null) return t;
            var j = GetJob(job); return j != null && j.Parent2 >= 0 ? TreeOf(j.Parent2) : null;
        }
        public static Extra ExtraOf(int skill) { Load(); return _extra.TryGetValue(skill, out var e) ? e : NoExtra; }

        /// <summary>The transcendent 2nd job corresponding to a plain 2nd job (0 when none).</summary>
        public static int TransSecondOf(int job2)
        {
            switch (job2)
            {
                case 7: return 4008;  // Knight -> Lord Knight
                case 8: return 4009;  // Priest -> High Priest
                case 9: return 4010;  // Wizard -> High Wizard
                case 10: return 4011; // Blacksmith -> Whitesmith / Mastersmith
                case 11: return 4012; // Hunter -> Sniper
                case 12: return 4013; // Assassin -> Assassin Cross
                case 14: return 4015; // Crusader -> Paladin
                case 15: return 4016; // Monk -> Champion
                case 16: return 4017; // Sage -> Professor / Scholar
                case 17: return 4018; // Rogue -> Stalker
                case 18: return 4019; // Alchemist -> Creator / Biochemist
                case 19: return 4020; // Bard -> Clown / Minstrel
                case 20: return 4021; // Dancer -> Gypsy
                default: return 0;
            }
        }

        /// <summary>skillinfo_f.lub GetInheritJob(job): JobInheritTable[1] = job, then JOB_INHERIT_LIST[job], … until nil — the job
        /// first, the root (Novice, or Summoner) last. For 3rd and 4th classes whose client/rAthena table points directly to the plain
        /// 2nd class (4054..4059, 4066..4072), the corresponding transcendent 2nd class is always included before the 2nd class.</summary>
        public static List<int> Chain(int job)
        {
            Load();
            var l = new List<int>();
            while (job >= 0 && !l.Contains(job)) { l.Add(job); job = _jobs.TryGetValue(job, out var j) ? j.Parent : -1; }
            if (l.Count > 0)
            {
                int top = l[0];
                bool is3rdOr4th = (top >= 4054 && top <= 4079) || (top >= 4252 && top <= 4264);
                if (is3rdOr4th)
                {
                    for (int i = 0; i < l.Count; i++)
                    {
                        int th = TransSecondOf(l[i]);
                        if (th > 0 && !l.Contains(th)) { l.Insert(i, th); break; }
                    }
                }
            }
            return l;
        }

        /// <summary>
        /// The prerequisites that apply to <paramref name="skill"/> for a character of <paramref name="job"/>: skillinfo_f.lub
        /// SetNeedSkillList(skill, 0) — for each job of the inherit chain (the job first) mapped through JOB_INHERIT_LIST2 (the
        /// transcendent 3rd jobs use their plain 3rd job's lists), the first NeedSkillList[job] found wins; without one, the
        /// generic _NeedSkillList.
        /// </summary>
        public static List<(int skill, int level)> Needs(int skill, int job)
        {
            Load();
            if (_needJobs.TryGetValue(skill, out var jobs))
                foreach (int j0 in Chain(job))
                {
                    int j = j0;
                    if (_jobs.TryGetValue(j0, out var jj) && jj.Parent2 != -1) j = jj.Parent2; // JOB_INHERIT_LIST2 (skillinfo_f.lub: "if j2 ~= -1 and j2 ~= nil then job = j2")
                    if (jobs.Contains(j) && _needJob.TryGetValue((skill, j), out var l)) return l;
                }
            return _needAll.TryGetValue(skill, out var g) ? g : new List<(int, int)>();
        }

        /// <summary>Every prerequisite at every depth (the official client's All_NeedSkillList, which it hands to the window to mark
        /// the cells a skill depends on), highest level kept per skill.</summary>
        public static Dictionary<int, int> AllNeeds(int skill, int job)
        {
            var all = new Dictionary<int, int>(); var todo = new Queue<int>(); todo.Enqueue(skill);
            var seen = new HashSet<int> { skill };
            while (todo.Count > 0)
                foreach (var (s, lv) in Needs(todo.Dequeue(), job))
                {
                    all[s] = all.TryGetValue(s, out int old) ? Math.Max(old, lv) : lv;
                    if (seen.Add(s)) todo.Enqueue(s);
                }
            return all;
        }

        /// <summary>
        /// The job a LOOK_BASE value (ZC_SPRITE_CHANGE 0x01d7 type 0) stands for: rAthena sends the riding sprite classes for a
        /// Peco rider (status_set_viewdata, status.cpp:9401-9423: JOB_KNIGHT 7 → JOB_KNIGHT2 13, CRUSADER 14 → 21, LORD_KNIGHT
        /// 4008 → 4014, PALADIN 4015 → 4022, BABY_KNIGHT 4030 → 4036, BABY_CRUSADER 4037 → 4044; ids from common/mmo.hpp e_job),
        /// the Star Gladiator's union (OPTION_FLYING) sends JOB_STAR_GLADIATOR2 4048 whatever the Gladiator's real class
        /// (pc_setoption, pc.cpp:11232-11248), and a disguise sends a monster class. Returns -1 — keep the job we had — for any
        /// look whose inherit chain has no skill tree in the client data (4048 JT_STAR2, the costume classes, monsters, NPCs).
        /// </summary>
        public static int JobFromLook(int look)
        {
            switch (look)
            {
                case 13: return 7; case 21: return 14; case 4014: return 4008; case 4022: return 4015; case 4036: return 4030; case 4044: return 4037;
            }
            if (GetJob(look) == null) return -1;
            foreach (int j in Chain(look)) if (ViewTreeOf(j) != null) return look;
            return -1;
        }
    }

    /// <summary>
    /// The skill window's model for one character: the tabs of its job (one per tier of the inherit chain), each cell's state
    /// from the server's skill list (<see cref="Progression.Skills"/>, ZC_SKILLINFO_LIST 0x0b32 and its updates), and the
    /// reason a cell is locked.
    /// </summary>
    public sealed class SkillTreeModel
    {
        public enum State { Learned, Learnable, Locked }
        public enum Why { None, Tier, Prerequisites, Quest, Soul, Server }

        public sealed class Cell
        {
            public int Pos, Skill, Job; public State State; public Why Why;
            public Progression.SkillEntry Entry;  // null when the server has not sent the skill
            public List<(int skill, int level, bool met)> Needs = new List<(int, int, bool)>();
        }
        public sealed class Tab
        {
            public string Label = "", Tier = ""; public bool Etc;
            public List<int> Jobs = new List<int>();
            public readonly SortedDictionary<int, Cell> Cells = new SortedDictionary<int, Cell>();
            /// <summary>Some cells are locked because an earlier tier's points are not spent yet (player_skillup_limit).</summary>
            public bool Gated;
            public int Rows { get { int m = 0; foreach (int k in Cells.Keys) if (k > m) m = k; return m / 7 + 1; } }
        }

        public int Job; public readonly List<Tab> Tabs = new List<Tab>();
        /// <summary>The job whose tree the server has opened last (the tier the server's skill-up limit stops at).</summary>
        public int OpenJob = -1;
        public bool ListReceived;

        /// <summary>
        /// Builds the tabs. Tabs: the inherit chain from the root, split into tiers — a job joins the previous one's tab when that
        /// one is JT_NOVICE (the Novice's three cells sit in the first class's grid: skilltreeview.lub places NV_BASIC / NV_FIRSTAID /
        /// NV_TRICKDEAD at 0 / 7 / 14, a column no first class uses) or when it is that job's transcendent / baby version (the client
        /// data's own names: JT_KNIGHT_H and JT_KNIGHT_B grow from JT_KNIGHT). Checked against roBrowserLegacy's job-range rule
        /// (rB: DB/DBManager.js:6631-6685) for the 145 jobs that have a tree: the same tabs except the baby Super Novices, which
        /// rB's ranges put a tier too high.
        /// Tier gating (rAthena pc_calc_skilltree_normalize_job_sub / _normalize_job, pc.cpp:2893-3005, used by pc_calc_skilltree at
        /// pc.cpp:2605 and pc_check_skilltree; player_skillup_limit, conf/battle/player.conf:35):
        /// until the points of a tier are spent (Novice: 9, then the job level each class was left at, minus one) the server
        /// builds the tree of that lower tier only, so the next tiers' skills are simply absent from 0x0b32. The client cannot see
        /// the thresholds (the job-change levels are server-side character variables), so a tier counts as open when the server
        /// sent any of its skills that points can buy, and every tier after the last open one is gated.
        /// </summary>
        public static SkillTreeModel Build(int job, Progression prog, int sex = 1)
        {
            var m = new SkillTreeModel { Job = job, ListReceived = prog.Skills.Count > 0 };
            var chain = SkillTreeDb.Chain(job); chain.Reverse(); // root first
            // tiers (for gating) and tabs (for display): the Novice is a tier of its own but shares the first class's tab
            var tiers = new List<List<int>>(); var tabJobs = new List<List<int>>();
            for (int i = 0; i < chain.Count; i++)
            {
                int j = chain[i];
                bool sameTab = false, sameTier = false;
                if (i > 0)
                {
                    int prev = chain[i - 1];
                    var pj = SkillTreeDb.GetJob(prev); var cj = SkillTreeDb.GetJob(j);
                    bool variant = pj != null && cj != null && (cj.Const == pj.Const + "_H" || cj.Const == pj.Const + "_B");
                    sameTab = prev == 0 || variant; sameTier = variant;
                }
                if (sameTab) tabJobs[tabJobs.Count - 1].Add(j); else tabJobs.Add(new List<int> { j });
                if (sameTier) tiers[tiers.Count - 1].Add(j); else tiers.Add(new List<int> { j });
            }
            // gating: the last tier with a point-bought skill in the server's list is open, the later ones are not
            int lastOpen = -1;
            for (int t = 0; t < tiers.Count; t++)
                foreach (int j in tiers[t])
                {
                    var tree = SkillTreeDb.ViewTreeOf(j); if (tree == null) continue;
                    foreach (int sk in tree.Values)
                    {
                        string type = SkillTreeDb.ExtraOf(sk).Type;
                        if (type != "Quest" && type != "Soul" && prog.Skills.ContainsKey(sk)) { lastOpen = t; m.OpenJob = j; }
                    }
                }
            var gatedJobs = new HashSet<int>();
            for (int t = lastOpen + 1; t < tiers.Count; t++) foreach (int j in tiers[t]) gatedJobs.Add(j);

            // the tab names: JobSkillTab entries, looked up from the job towards the root (skilltreeview.lub registers them for
            // JT_NOVICE, JT_NINJA, JT_GUNSLINGER, JT_SUPERNOVICE, the Taekwon line and the Summoner)
            string[] tierNames = null;
            foreach (int j in SkillTreeDb.Chain(job)) { var jj = SkillTreeDb.GetJob(j); if (jj != null && jj.TabNames.Length > 0) { tierNames = jj.TabNames; break; } }

            var inTree = new HashSet<int>();
            for (int t = 0; t < tabJobs.Count; t++)
            {
                var tab = new Tab { Jobs = tabJobs[t] };
                int labelJob = tabJobs[t][tabJobs[t].Count - 1];
                tab.Label = SkillTreeDb.JobName(labelJob, sex); // client wave 5 (w5ui): the language's name (Portuguese female forms for sex 0)
                tab.Tier = tierNames != null && t < tierNames.Length ? TierText(tierNames[t]) : TierText((t + 1) + "a");
                foreach (int j in tabJobs[t])
                {
                    var tree = SkillTreeDb.ViewTreeOf(j); if (tree == null) continue;
                    foreach (var kv in tree)
                    {
                        var c = MakeCell(kv.Key, kv.Value, j, job, prog, gatedJobs.Contains(j));
                        tab.Cells[kv.Key] = c; inTree.Add(kv.Value);
                        if (c.Why == Why.Tier) tab.Gated = true;
                    }
                }
                m.Tabs.Add(tab);
            }
            // skills the server sent that no tab shows (platinum / wedding skills, skills granted by items or @commands): "Etc"
            // (rB: UI/Components/SkillList/SkillListCommon.js:765-791 puts them in its etc tab the same way)
            var etc = new Tab { Etc = true, Label = "Other", Tier = "" };
            int pos = 0;
            foreach (var k in prog.Skills.Values)
                if (!inTree.Contains(k.Id)) etc.Cells[pos] = new Cell { Pos = pos++, Skill = k.Id, Job = job, Entry = k, State = k.Level > 0 ? State.Learned : State.Learnable };
            if (etc.Cells.Count > 0) m.Tabs.Add(etc);
            return m;
        }

        private static Cell MakeCell(int pos, int skill, int cellJob, int job, Progression prog, bool gated)
        {
            var c = new Cell { Pos = pos, Skill = skill, Job = cellJob };
            foreach (var (s, lv) in SkillTreeDb.Needs(skill, job))
            {
                int have = prog.Skills.TryGetValue(s, out var e) ? e.Level : 0;
                c.Needs.Add((s, lv, have >= lv));
            }
            if (prog.Skills.TryGetValue(skill, out var k))
            {
                c.Entry = k; c.State = k.Level > 0 ? State.Learned : State.Learnable; // rAthena lists unlocked skills at level 0 (clif.cpp:5711)
                return c;
            }
            c.State = State.Locked;
            string type = SkillTreeDb.ExtraOf(skill).Type;
            if (type == "Quest") c.Why = Why.Quest;       // pc_calc_skilltree skips INF2_ISQUEST skills that were never learned (pc.cpp:2734-2740)
            else if (type == "Soul") c.Why = Why.Soul;    // INF2_ISSPIRIT: only while a Soul Linker's spirit is on (pc.cpp:2738-2747)
            else if (gated) c.Why = Why.Tier;
            else if (c.Needs.Exists(n => !n.met)) c.Why = Why.Prerequisites;
            else c.Why = Why.Server;                       // prerequisites met in the client data, still not sent: a base / job level (skill_tree.yml BaseLevel / JobLevel) or a server tree without it
            return c;
        }

        /// <summary>'1a' → "1st" …: the tab names skilltreeview.lub gives JobSkillTab (TabName1st … TabName4th); how the
        /// official client draws them is not known here, the digit is the tier.</summary>
        public static string TierText(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            // w4lang: English here (the smoke compares Tab.Tier); Lang.N marks the table lines for where the skill window shows it
            switch (name[0]) { case '1': return Lang.N("1st"); case '2': return Lang.N("2nd"); case '3': return Lang.N("3rd"); case '4': return Lang.N("4th"); default: return name; }
        }

        public static string WhyText(Cell c, SkillTreeModel m)
        {
            switch (c.Why)
            {
                case Why.Tier: return Lang.T("Locked until the skill points of the earlier tier are spent (the server's skill-up limit).");
                case Why.Prerequisites: return Lang.T("Locked: learn the skills below first.");
                case Why.Quest: return Lang.T("Learned through a quest, not with skill points.");
                case Why.Soul: return Lang.T("Only while a Soul Linker's spirit is on you.");
                case Why.Server: return Lang.T("Prerequisites met, but the server has not unlocked it yet (a base or job level, or not in this server's tree).");
                default: return "";
            }
        }
    }

}
