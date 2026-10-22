using System;
using System.Collections.Generic;
using System.Linq;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 14, deploy B (w14kit), point 24 of the design's headset list: the "Presets" tab of the Commands window. design note: "a tab on the
    /// commands menu that has a few preset buttons called character presets ... leaves me with a job, equipment, skills, skills already
    /// placed in slots so that I can quickly test different abilities all in a single button press", "Provide the adequate stats ...
    /// for jobs that have multiple different options when it comes to weapons like daggers or katars, provide one option for each",
    /// "changing from one preset to another should reset the character ... take away the items of the preset that is being changed from.
    /// The items that the character already had before should be kept", answer 17: "All of the valid ones (including initial ones like
    /// thief and swordsman) ... consumables such as potions, poisons, materials and ammunition. Add no preset button."
    ///
    /// The presets are DATA: Resources/Presets/char_presets.txt (one row per job and weapon style, made from rAthena's databases by
    /// client/tools/w14/build_char_presets.py; its header says what each column is). A job is listed only when the Jobs picker offers it
    /// (Game/GmPickers.cs JobRows: rAthena's @job takes it AND the player's GRF has its body), for the player's sex. What a press does is
    /// LokiGame.W14Kit.cs (GM commands sent as chat lines, the equipment worn, the quick bar set); the record of what the active preset gave
    /// is World/UserSettings.cs CharPreset (per character). This class is the data and the list.
    /// </summary>
    public sealed class CharPresets
    {
        public sealed class Preset
        {
            /// <summary>Client wave 18 (w18gear), point 3: <see cref="BaseLevel"/> / <see cref="JobLevel"/> are the job's MAXIMUM levels
            /// (db/re/job_exp.yml): a preset keeps the character's own levels within them (never raises them to these).</summary>
            public int Job, Sex = -1, BaseLevel = 99, JobLevel = 50;
            public string Style = "", Label = "", Tier = "";
            /// <summary>STR AGI VIT INT DEX LUK POW STA WIS SPL CON CRT (the traits are 0 below the 4th jobs): the profile at the job's
            /// maximum level; client wave 18 (w18gear): a preset spends the kept level's own points towards it (<see cref="FitStats"/>).</summary>
            public int[] Stats = new int[12];
            /// <summary>Client wave 18 (w18gear), point 3 (the data's columns 14-15, rAthena's job change rules: the tool's min_base / path_of):
            /// the lowest base level a character of this job can have, the earlier jobs a character changes through to it (each with the job
            /// level it is left at; the Novice of its kind is not walked) and the skill points those earlier jobs give (-1: not in the data).</summary>
            public int MinBase = 1, PriorSkillPoints = -1;
            public readonly List<(int job, int jobLevel)> Walk = new List<(int, int)>();
            /// <summary>Client wave 18 (w18gear), point 3 (the data's column 16): the job's highest stat and trait (rAthena's pc_maxparameter:
            /// conf/battle/player.conf max_*_parameter, max_trait_parameter; 0: none / no traits). A level with more points than the profile
            /// takes raises it further, in proportion, up to these.</summary>
            public int StatCap, TraitCap;
            /// <summary>Equipment in wearing order; Location 0 = the item's own place.</summary>
            public readonly List<(int id, uint loc)> Equip = new List<(int, uint)>();
            public readonly List<(int id, int n)> Items = new List<(int, int)>();
            public string[] Extras = new string[0];
            public int[] Bar = new int[0], BarItems = new int[0];
            public string Key => Job + ":" + Style;
        }

        /// <summary>rAthena's @commands for the twelve stats, in <see cref="Preset.Stats"/>' order (atcommand.cpp ACMD_DEF2(..., param)).</summary>
        public static readonly string[] StatCommands = { "@str", "@agi", "@vit", "@int", "@dex", "@luk", "@pow", "@sta", "@wis", "@spl", "@con", "@crt" };
        /// <summary>The tabs of the list, as the data names them, and their names.</summary>
        public static readonly string[] Tiers = { "1st", "2nd", "trans", "3rd", "4th", "expanded", "baby" };
        private static readonly string[] TierNames = { Lang.N("1st jobs"), Lang.N("2nd jobs"), Lang.N("Transcendent"), Lang.N("3rd jobs"), Lang.N("4th jobs"), Lang.N("Expanded jobs"), Lang.N("Baby jobs") };
        private static readonly Dictionary<string, string> StyleNames = new Dictionary<string, string>
        {
            { "dagger", Lang.N("~style~Dagger") }, { "dual", Lang.N("~style~Two daggers") }, { "katar", Lang.N("~style~Katar") }, { "sword", Lang.N("~style~Sword, shield") },
            { "swordatk", Lang.N("~style~Sword") }, { "2hsword", Lang.N("~style~2H sword") }, { "spear", Lang.N("~style~Spear") }, { "spearshield", Lang.N("~style~Spear, shield") },
            { "staff", Lang.N("~style~Staff") }, { "support", Lang.N("~style~Staff") }, { "book", Lang.N("~style~Book") }, { "mace", Lang.N("~style~Mace") },
            { "axe", Lang.N("~style~Axe") }, { "bsaxe", Lang.N("~style~Axe") }, { "bow", Lang.N("~style~Bow") }, { "knuckle", Lang.N("~style~Knuckle") },
            { "fist", Lang.N("~style~Fists") }, { "instrument", Lang.N("~style~Instrument") }, { "whip", Lang.N("~style~Whip") }, { "performerbow", Lang.N("~style~Bow") },
            { "huuma", Lang.N("~style~Huuma") }, { "ninjadagger", Lang.N("~style~Dagger") }, { "revolver", Lang.N("~style~Pistol") }, { "rifle", Lang.N("~style~Rifle") },
            { "shotgun", Lang.N("~style~Shotgun") }, { "gatling", Lang.N("~style~Gatling") }, { "grenade", Lang.N("~style~Grenade") }, { "doram", Lang.N("~style~Staff") },
        };
        public static string StyleName(Preset p) => p == null ? "" : StyleNames.TryGetValue(p.Style, out var n) ? Lang.T(n) : p.Label;
        public static string TierName(int t) => Lang.T(TierNames[Mathf.Clamp(t, 0, TierNames.Length - 1)]);

        private static List<Preset> _all;
        /// <summary>Every row of the data file (empty when it is missing).</summary>
        public static IReadOnlyList<Preset> All { get { Load(); return _all; } }

        private static void Load()
        {
            if (_all != null) return;
            _all = new List<Preset>();
            var ta = Resources.Load<TextAsset>("Presets/char_presets");
            if (ta == null) { Debug.LogWarning("[Presets] Resources/Presets/char_presets is missing: no character presets"); return; }
            int bad = 0;
            foreach (var raw in ta.text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                var p = Parse(line);
                if (p != null) _all.Add(p); else bad++;
            }
            Debug.Log($"[Presets] {_all.Count} character presets for {_all.Select(x => x.Job).Distinct().Count()} jobs{(bad > 0 ? $" ({bad} malformed rows left out)" : "")}");
        }

        /// <summary>One row of the data file (null: malformed).</summary>
        public static Preset Parse(string line)
        {
            var c = line.Split('\t');
            if (c.Length < 13) return null;
            try
            {
                var p = new Preset { Job = int.Parse(c[0]), Sex = int.Parse(c[1]), Style = c[2], Label = c[3], Tier = c[4] };
                var lv = c[5].Split(','); p.BaseLevel = int.Parse(lv[0]); p.JobLevel = int.Parse(lv[1]);
                var st = Words(c[6]); for (int i = 0; i < 12 && i < st.Length; i++) p.Stats[i] = int.Parse(st[i]);
                foreach (var w in Words(c[7]))
                {
                    int at = w.IndexOf('@');
                    p.Equip.Add(at < 0 ? (int.Parse(w), 0u) : (int.Parse(w.Substring(0, at)), Convert.ToUInt32(w.Substring(at + 1), 16)));
                }
                foreach (var w in Words(c[8])) { int x = w.IndexOf('x'); if (x > 0) p.Items.Add((int.Parse(w.Substring(0, x)), int.Parse(w.Substring(x + 1)))); }
                p.Extras = Words(c[9]);
                p.Bar = Words(c[10]).Select(int.Parse).ToArray();
                p.BarItems = Words(c[12]).Select(int.Parse).ToArray();
                if (c.Length >= 15) // client wave 18 (w18gear), point 3
                {
                    var path = Words(c[13]);
                    if (path.Length > 0) p.MinBase = int.Parse(path[0]);
                    for (int i = 1; i < path.Length; i++) { int k = path[i].IndexOf(':'); p.Walk.Add((int.Parse(path[i].Substring(0, k)), int.Parse(path[i].Substring(k + 1)))); }
                    p.PriorSkillPoints = int.Parse(c[14]);
                }
                if (c.Length >= 16) { var cap = Words(c[15]); if (cap.Length >= 2) { p.StatCap = int.Parse(cap[0]); p.TraitCap = int.Parse(cap[1]); } } // client wave 18 (w18gear)
                return p;
            }
            catch (Exception) { return null; }
        }

        private static string[] Words(string s) => s == "-" ? new string[0] : s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>The preset with this key for a character of <paramref name="sex"/> (a row of both sexes, or that sex's own).</summary>
        public static Preset Find(string key, int sex)
        {
            Preset any = null;
            foreach (var p in All) if (p.Key == key) { if (p.Sex == sex) return p; if (p.Sex < 0) any = p; }
            return any;
        }

        /// <summary>Everything the preset gives, by item id (the equipment counted once per piece: two daggers = 2).</summary>
        public static Dictionary<int, int> Gives(Preset p)
        {
            var d = new Dictionary<int, int>();
            foreach (var (id, loc) in p.Equip) if (loc != 0x8000) d[id] = (d.TryGetValue(id, out int n) ? n : 0) + 1;
            foreach (var (id, n) in p.Items) d[id] = (d.TryGetValue(id, out int m) ? m : 0) + n;
            return d;
        }

        // ------------------------------------------------------------------ client wave 18 (w18gear), point 3: the levels kept, the points spent
        /// <summary>The levels a preset sets up for a character at base <paramref name="base0"/> / job <paramref name="job0"/> (design note: "I am
        /// supposed to keep the same level ... Or at the very least, the minimum level necessary to be the class that I choose"): the base
        /// level kept, raised only to the job's minimum (<see cref="Preset.MinBase"/>); the job level kept (a job change sets it to 1,
        /// pc_jobchange pc.cpp:10920: a job has no higher minimum of its own); both within the job's maximum (pc_jobchange caps the base
        /// level, pc.cpp:10923-10933; @joblevelup the job level, atcommand.cpp:1761-1762).</summary>
        public static (int baseLv, int jobLv) LevelsFor(Preset p, int base0, int job0) =>
            (Mathf.Clamp(Mathf.Max(base0, p.MinBase), 1, Mathf.Max(1, p.BaseLevel)), Mathf.Clamp(job0, 1, Mathf.Max(1, p.JobLevel)));

        /// <summary>The skill points a character of this job at job level <paramref name="jobLv"/> has: one a job level (pc_checkjoblevelup
        /// pc.cpp:8333-8334, @joblevelup atcommand.cpp:1764) on top of the earlier jobs' <see cref="Preset.PriorSkillPoints"/>; -1 without data.</summary>
        public static int SkillPointsFor(Preset p, int jobLv) => p.PriorSkillPoints < 0 ? -1 : p.PriorSkillPoints + Mathf.Max(0, jobLv - 1);

        /// <summary>rAthena's status point cost of raising a stat from <paramref name="from"/> to from + 1: PC_STATUS_POINT_COST (pc.cpp:8800;
        /// RENEWAL_STAT is on, src/config/renewal.hpp:74), summed by pc_need_status_point (pc.cpp:8809-8831).</summary>
        public static int StatCost(int from) => from < 100 ? 2 + (from - 1) / 10 : 16 + 4 * ((from - 100) / 5);
        /// <summary>A trait point costs 1 (pc_need_trait_point, pc.cpp:8967-8989).</summary>
        public static int TraitCost(int from) => 1;

        /// <summary>The six stats (each from 1) a character with <paramref name="points"/> status points can have, in the preset's proportions:
        /// the profile scaled evenly (down when the points do not reach it; up, each stat at most the job's <see cref="Preset.StatCap"/>, when
        /// they go beyond it) to the largest share that fits rAthena's costs, then the points left one at a time to the stat furthest behind
        /// its share. A stat the profile leaves at 1 stays 1; points that buy nothing more stay unspent.</summary>
        public static int[] FitStats(Preset p, int points) => Fit(p.Stats, 0, 1, points, StatCost, p.StatCap);
        /// <summary>The same for the six traits (each from 0, up to <see cref="Preset.TraitCap"/>) and the trait points (4th jobs).</summary>
        public static int[] FitTraits(Preset p, int points) => Fit(p.Stats, 6, 0, points, TraitCost, p.TraitCap);
        /// <summary>The points <paramref name="values"/> cost from the floor (1 for stats, 0 for traits).</summary>
        public static int CostOf(int[] values, bool traits)
        {
            int floor = traits ? 0 : 1, s = 0;
            foreach (int v in values) for (int x = floor; x < v; x++) s += traits ? TraitCost(x) : StatCost(x);
            return s;
        }

        private static int[] Fit(int[] profile, int at, int floor, int points, Func<int, int> cost, int cap)
        {
            var t = new int[6]; var top = new int[6]; double fmax = 1;
            for (int i = 0; i < 6; i++)
            {
                t[i] = Mathf.Max(floor, at + i < profile.Length ? profile[at + i] : floor);
                top[i] = cap > 0 && t[i] > floor ? Mathf.Max(cap, t[i]) : t[i]; // what a stat may grow to (the profile itself without a cap)
                if (t[i] > floor) fmax = Math.Max(fmax, (top[i] - floor) / (double)(t[i] - floor));
            }
            int Cost(int[] v) { int s = 0; for (int i = 0; i < 6; i++) for (int x = floor; x < v[i]; x++) s += cost(x); return s; }
            int[] At(double f) { var v = new int[6]; for (int i = 0; i < 6; i++) v[i] = Mathf.Min(top[i], floor + (int)Math.Floor((t[i] - floor) * f + 1e-9)); return v; }
            double lo = 0, hi = fmax;
            for (int n = 0; n < 60; n++) { double mid = (lo + hi) / 2; if (Cost(At(mid)) <= points) lo = mid; else hi = mid; }
            var r = At(lo); int left = points - Cost(r);
            while (true)
            {
                int best = -1; float share = float.MaxValue;
                for (int i = 0; i < 6; i++)
                {
                    if (t[i] <= floor || r[i] >= top[i] || cost(r[i]) > left) continue;
                    float s = (r[i] - floor) / (float)(t[i] - floor);
                    if (s < share || (s == share && best >= 0 && t[i] > t[best])) { share = s; best = i; }
                }
                if (best < 0) break;
                left -= cost(r[best]); r[best]++;
            }
            return r;
        }

        // ------------------------------------------------------------------ the list (drawn in the Commands window, its "Presets" tab)
        /// <summary>The player's sex (rAthena's: 0 female, 1 male), the job now, and whether the Jobs picker offers a job to this sex.</summary>
        public Func<int> Sex, CurrentJob;
        public Func<int, int, bool> Offered;
        /// <summary>The active preset's key ("" none), whether a preset is being set up, and what it is doing (the info line).</summary>
        public Func<string> ActiveKey, Progress;
        public Func<bool> Busy;
        /// <summary>Whether this character has a record (a preset, or one being set up): the first preset asks for a second press.</summary>
        public Func<bool> HasRecord;
        public Action<Preset> Apply;
        public Action NoPreset;
        public Action Close;
        /// <summary>Draws the window again (the tier button, an armed press).</summary>
        public Action Redraw;
        /// <summary>The tab shown (-1: the current job's), the key a first press armed and until when (Time.realtimeSinceStartup).</summary>
        private int _tier = -1; private string _armed = ""; private float _armedUntil;
        /// <summary>Client wave 14 (w14kit): seconds a first press (no preset yet: it resets the character) stays armed.</summary>
        public const float ArmSeconds = 5f;
        public int Tier => _tier;
        /// <summary>Presses (the log, the smoke) and the last row list drawn.</summary>
        public int Presses { get; private set; }

        /// <summary>The tab a job's presets are in (-1: none).</summary>
        public static int TierOf(int job)
        {
            foreach (var p in All) if (p.Job == job) return Array.IndexOf(Tiers, p.Tier);
            return -1;
        }

        /// <summary>The jobs of a tab offered to this sex, in the data's order, each with its styles.</summary>
        public List<(int job, List<Preset> styles)> JobsOf(int tier, int sex)
        {
            var list = new List<(int, List<Preset>)>(); var index = new Dictionary<int, int>();
            string t = Tiers[Mathf.Clamp(tier, 0, Tiers.Length - 1)];
            foreach (var p in All)
            {
                if (p.Tier != t || (p.Sex >= 0 && p.Sex != sex)) continue;
                if (Offered != null && !Offered(p.Job, sex)) continue;
                if (!index.TryGetValue(p.Job, out int i)) { i = list.Count; index[p.Job] = i; list.Add((p.Job, new List<Preset>())); }
                list[i].Item2.Add(p);
            }
            return list;
        }

        /// <summary>The tier button: the next tab.</summary>
        public void NextTier() { _tier = (EffectiveTier() + 1) % Tiers.Length; Redraw?.Invoke(); }
        public int EffectiveTier()
        {
            if (_tier >= 0) return _tier;
            int t = CurrentJob != null ? TierOf(CurrentJob()) : -1;
            return t >= 0 ? t : 1;
        }

        /// <summary>A style button: the first preset of a character (it resets job, stats and skills) asks for a second press within
        /// <see cref="ArmSeconds"/>; switching presets is one press (design note: "all in a single button press").</summary>
        public void Press(Preset p)
        {
            if (p == null || (Busy != null && Busy())) return;
            Presses++;
            bool first = HasRecord == null || !HasRecord();
            if (first && !(_armed == p.Key && Time.realtimeSinceStartup < _armedUntil))
            {
                _armed = p.Key; _armedUntil = Time.realtimeSinceStartup + ArmSeconds;
                Debug.Log($"[Presets] {p.Key}: the first preset of this character, armed for {ArmSeconds:0} s (press again)");
                Redraw?.Invoke();
                return;
            }
            _armed = "";
            Apply?.Invoke(p);
        }

        public void Fill(ListWindow win)
        {
            int sex = Sex != null ? Sex() : 1, tier = EffectiveTier();
            bool busy = Busy != null && Busy();
            string active = ActiveKey != null ? ActiveKey() ?? "" : "";
            win.SetTitle(Lang.T("Character presets"));
            win.SetInput(false);
            var cur = active.Length > 0 ? Find(active, sex) : null;
            bool armed = _armed.Length > 0 && Time.realtimeSinceStartup < _armedUntil;
            win.SetInfo(busy ? (Progress != null ? Progress() : Lang.T("Setting up…"))
                : armed ? Lang.T("Resets job, stats, skills. Press again.")
                : cur != null ? Lang.F("Now: {0} · {1}", SkillTreeDb.JobName(cur.Job, sex), StyleName(cur))
                : Lang.T("No preset now."));
            var rows = new List<ListWindow.Row>();
            foreach (var (job, styles) in JobsOf(tier, sex))
            {
                string name = SkillTreeDb.JobName(job, sex);
                for (int i = 0; i < styles.Count; i += 2)
                {
                    var a = styles[i]; var b = i + 1 < styles.Count ? styles[i + 1] : null;
                    bool on = cur != null && (cur.Key == a.Key || (b != null && cur.Key == b.Key));
                    string mark = on ? "  <color=#7fd67f>✔ " + StyleName(cur) + "</color>" : "";
                    string pending = armed && (_armed == a.Key || (b != null && _armed == b.Key)) ? "  <color=#ffd080>?</color>" : "";
                    rows.Add(new ListWindow.Row
                    {
                        Text = name + mark + pending, Highlight = on,
                        A = StyleName(a), OnA = () => Press(a), AEnabled = !busy,
                        B = b != null ? StyleName(b) : null, OnB = b != null ? () => Press(b) : (Action)null, BEnabled = !busy,
                    });
                }
            }
            win.SetRows(rows);
            win.SetFooter((Lang.T("Close"), () => Close?.Invoke()), (Lang.T("No preset"), () => { if (!(Busy != null && Busy())) NoPreset?.Invoke(); }), (TierName(tier), NextTier));
        }
    }
}
