using System;
using System.Collections.Generic;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>The job families of the 3D skill tree (client wave 22, w22tree: UI/TreeThemes.cs TreeFamily, the same eleven): a family
    /// has ONE colour idea; its lines share it.</summary>
    public enum JobFamily { Novice, Swordsman, Mage, Archer, Acolyte, Merchant, Thief, Gunslinger, Ninja, Taekwon, Summoner }

    /// <summary>A job LINE: what a panel's emblem shows. A first job has its family's emblem in a plain form (Swordsman: a plain sword);
    /// a second job and everything that grows from it has its line's (Knight: a winged sword; Crusader: a shield with a cross).</summary>
    public enum JobLine
    {
        Novice, SuperNovice,
        Swordsman, Knight, Crusader,
        Mage, Wizard, Sage,
        Archer, Hunter, Bard,
        Acolyte, Priest, Monk,
        Merchant, Blacksmith, Alchemist,
        Thief, Assassin, Rogue,
        Gunslinger, Ninja,
        Taekwon, StarGladiator, SoulLinker,
        Summoner,
    }

    /// <summary>Client wave 21 (w21jobui): marks a root whose panels belong to ANOTHER player (a friend's shared copies of their casting
    /// menus: UI/FriendGestures.cs): everything of the theme under it is dressed in THAT player's job variant, never in ours.</summary>
    public sealed class JobThemeOwner : MonoBehaviour
    {
        /// <summary>The owner's account id (a player's unit id).</summary>
        public uint Aid;
        internal JobTheme.Variant Want, Applied; internal bool Known, Dirty = true; internal float NextAsk;
        private void OnDestroy() { JobTheme.Owners.Remove(this); } // (registered by JobTheme.Own: a hidden root's copies are a friend's too)
    }

    /// <summary>
    /// Client wave 21 (w21jobui), design note 2026-10-11: "Create variations for the different panel UI themes based on each one of the jobs and
    /// their progression. You can either choose to have your panels be themed after the job (and their progression) that you are, or be
    /// the base panel UI that you have already developed. You are to use the base UI that you have developed as a reference for the
    /// different class themed ones. Do not deviate too much from the base one, just add some flair to it."
    ///
    /// A JOB VARIANT of THE PANEL THEME (UI/Theme.cs) = the base pictures with a little added, made by COMPOSITION so every job the data
    /// has is covered without a hand-drawn look per job:
    ///   the LINE gives the emblem (<see cref="JobLine"/>, 26 emblems: UI/JobTheme.Draw.cs) and its family the ONE accent colour
    ///     (<see cref="AccentOf"/>: the skill trees' colour idea per family, UI/TreeThemes.cs on w22tree);
    ///   the TIER gives how much (<see cref="TierOf"/>: the trees' own rule of a job's place in its line):
    ///     0 Novice            one small gold emblem at each end of the frame's top board and on the title plate's ends; nothing else
    ///     1 first job         + the stitching in the accent colour, a card's rim line in it, a see-through frame's outer hairline
    ///     2 second job        + CORNER ORNAMENTS: a small round enamel medallion with the emblem in each corner of the frame (where
    ///                           the base has its two scrolls), an accent piping line on the leather, the plate's engraved line in
    ///                           enamel, a faint (lighter) emblem in the canvas' two lower corners, a tiny emblem on a see-through frame
    ///     3 transcendent      + A FINER GOLD EDGE: a gold hairline round the frame, a fine gold line inside a card's rim
    ///     4 third job         + RICHER CORNERS: a short gold braid from each medallion along both boards; a CREST on the plate:
    ///                           its ends enamelled with the emblem in gold; the faint emblem in all four canvas corners
    ///     5 fourth / last     + the braid longer, with small enamel gems; a second hairline; a fine gold line in the crest
    ///   A reborn Novice / first job (High Novice, High Swordman ...) has the finer gold edge of tier 3 already.
    /// NOTHING ELSE changes: a variant's pictures have the base's own pixels wherever nothing is added, and the same outer size: the
    /// frame's outset, every layout, every collider, the cream canvas, the dark ink, the round gold buttons and the cards are the
    /// base's. Nothing moves or glows. Where a text can stand, a variant is never darker than the base (the canvas' faint emblem is
    /// LIGHTER than the canvas; the plate's and the card's additions lie outside the text's rectangle): the kit's 7 : 1 holds.
    ///
    /// HOW IT REACHES EVERY PANEL: the five pictures the kit dresses panels with are read through here (UI/Theme.cs Frame, CanvasFill,
    /// CardFill, Plate, SeeFrame -> <see cref="Pick"/>) and so is the stitching's thread (Theme.Thread -> <see cref="Thread"/>): a panel
    /// built by any code with the kit wears the variant, with no change of its own. A CHANGE (of job, a rebirth, of the setting) makes
    /// the new variant's pictures one a frame in the background, then swaps the sprites of every themed image in ONE frame
    /// (<see cref="Redress"/>): no panel is rebuilt, nothing in it is touched. Legacy panels, the ring and the casting tools' Plain
    /// look hold none of these pictures: they never change.
    /// THE CHOICE: Settings -> Controls -> Menus "Panel theme: My job / Base" (VR/Comfort.cs PanelTheme, with the account). Base =
    /// the kit's own sprites, untouched (the very objects UI/Theme.cs made).
    /// </summary>
    public static partial class JobTheme
    {
        /// <summary>FLAGGED (default): what a player who never touched the setting gets. true: "My job".</summary>
        public const bool DefaultMyJob = true;
        public const int TierMax = 5;
        /// <summary>The pictures a variant has (the kit's five that dress a panel).</summary>
        public enum Slot { Frame, Canvas, Card, Plate, SeeFrame }
        public const int Slots = 5;

        /// <summary>Our own job now (-1: no character in the game: the base look). Set by LokiGame (LokiGame.W21JobUi.cs).</summary>
        public static Func<int> Job;
        /// <summary>Another player's job by account id (-1: not known). Set by LokiGame.</summary>
        public static Func<uint, int> JobOfAccount;
        /// <summary>After a re-dress (the variant that is on now; null: Base).</summary>
        public static event Action Changed;

        /// <summary>One look: a line at a tier.</summary>
        public sealed class Variant
        {
            public JobLine Line; public int Tier; public bool Reborn;
            public JobFamily Family => FamilyOf(Line);
            /// <summary>The family's accent colour; false: none (the Novice: the base's leather brown).</summary>
            public bool HasAccent; public Color Accent;
            /// <summary>The stitching's thread (the base's leather brown at tier 0).</summary>
            public Color Thread;
            /// <summary>The finer gold edge (tier 3 and up, and a reborn Novice / first job).</summary>
            public bool Fine => Reborn || Tier >= 3;
            public int Key => ((int)Line << 4) | (Tier << 1) | (Reborn ? 1 : 0);
            internal readonly Sprite[] Sprites = new Sprite[Slots]; internal readonly bool[] Own = new bool[Slots];
            internal int Built, Prep, Stage; internal int UsedAt;
            internal Color[] Px; internal int PxW, PxH; internal Vector4 PxBorder; // (the frame's picture between its three steps)
            public bool Ready => Built >= Slots;
            /// <summary>What making its pictures took, ms (all of it; the longest single step: one step a frame), and what they hold in
            /// memory, bytes (with their smaller copies and their readable copies).</summary>
            public float BuildMs, MaxStepMs; public long Bytes;
            public override string ToString() => $"{Line} tier {Tier}{(Reborn && Tier < 2 ? " reborn" : "")}";
        }

        // ------------------------------------------------------------------ which variant a job has
        public static JobFamily FamilyOf(JobLine l)
        {
            switch (l)
            {
                case JobLine.Swordsman: case JobLine.Knight: case JobLine.Crusader: return JobFamily.Swordsman;
                case JobLine.Mage: case JobLine.Wizard: case JobLine.Sage: return JobFamily.Mage;
                case JobLine.Archer: case JobLine.Hunter: case JobLine.Bard: return JobFamily.Archer;
                case JobLine.Acolyte: case JobLine.Priest: case JobLine.Monk: return JobFamily.Acolyte;
                case JobLine.Merchant: case JobLine.Blacksmith: case JobLine.Alchemist: return JobFamily.Merchant;
                case JobLine.Thief: case JobLine.Assassin: case JobLine.Rogue: return JobFamily.Thief;
                case JobLine.Gunslinger: return JobFamily.Gunslinger;
                case JobLine.Ninja: return JobFamily.Ninja;
                case JobLine.Taekwon: case JobLine.StarGladiator: case JobLine.SoulLinker: return JobFamily.Taekwon;
                case JobLine.Summoner: return JobFamily.Summoner;
                default: return JobFamily.Novice;
            }
        }

        private static Color Rgb(int v) => new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
        /// <summary>The ONE accent colour of a line: its family's colour idea in the skill trees (UI/TreeThemes.cs on w22tree: the
        /// Swordsman's red cloth, the Mage's ice blue, the Archer's leaf green, the Acolyte's stained-glass blue, the Merchant's
        /// lantern copper, the Thief's purple, the Gunslinger's gunmetal, the Ninja's ink black, the Taekwon's navy, the Summoner's
        /// mint; the Super Novice's ribbon pink), dark enough to read as thread on cream and as enamel on gold. The Novice has none.</summary>
        public static bool AccentOf(JobLine l, out Color c)
        {
            c = Theme.Leather;
            if (l == JobLine.Novice) return false;
            if (l == JobLine.SuperNovice) { c = Rgb(0xB24A76); return true; }
            switch (FamilyOf(l))
            {
                case JobFamily.Swordsman: c = Rgb(0xA3221E); break;
                case JobFamily.Mage: c = Rgb(0x2F58BE); break;
                case JobFamily.Archer: c = Rgb(0x3C7A2A); break;
                case JobFamily.Acolyte: c = Rgb(0x2A8AB8); break;
                case JobFamily.Merchant: c = Rgb(0xB85E1A); break;
                case JobFamily.Thief: c = Rgb(0x782C9C); break;
                case JobFamily.Gunslinger: c = Rgb(0x4C5868); break;
                case JobFamily.Ninja: c = Rgb(0x2A262E); break;
                case JobFamily.Taekwon: c = Rgb(0x27356E); break;
                case JobFamily.Summoner: c = Rgb(0x1E8A70); break;
                default: return false;
            }
            return true;
        }

        private static Dictionary<string, int> _byConst; private static Dictionary<string, int> _byName; private static int _jobsSeen;
        private static readonly List<(string[] words, int id)> _lined = new List<(string[], int)>();
        private static string[] Words(string konst)
        {
            var w = (konst.StartsWith("JT_", StringComparison.Ordinal) ? konst.Substring(3) : konst).Split('_');
            for (int i = 0; i < w.Length; i++) w[i] = w[i].TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9'); // (JT_STAR2_EMPEROR, JT_SKY_EMPEROR2)
            return w;
        }
        private static void Index()
        {
            if (_byConst != null && _jobsSeen > 0) return;
            _byConst = new Dictionary<string, int>(StringComparer.Ordinal); _byName = new Dictionary<string, int>(StringComparer.Ordinal); _jobsSeen = 0; _lined.Clear();
            foreach (var j in SkillTreeDb.AllJobs())
            {
                _jobsSeen++;
                if (j.Const.Length > 0 && !_byConst.ContainsKey(j.Const)) _byConst[j.Const] = j.Id;
                bool lined = j.Parent >= 0 || j.Id == 0 || j.Const == "JT_DO_SUMMONER"; // it stands in a job line
                // a name's first job that stands in a line (JT_KNIGHT 7 before its riding look JT_CHICKEN 13, which has no parent)
                if (j.Name.Length > 0 && lined && !_byName.ContainsKey(j.Name)) _byName[j.Name] = j.Id;
                if (lined && j.Const.Length > 3) _lined.Add((Words(j.Const), j.Id));
            }
        }
        /// <summary>A class without a parent and without a name of its own (the 4th jobs' riding looks: JT_WOLF_WINDHAWK,
        /// JT_DRAGON_KNIGHT_CHICKEN, JT_MEISTER_MADOGEAR, JT_SKY_EMPEROR2 ...): the job whose constant's words stand in a row in its
        /// own (the most words win: DRAGON KNIGHT before KNIGHT). -1: none.</summary>
        private static int ByWords(string konst)
        {
            var w = Words(konst); int best = -1, bestN = 0;
            foreach (var (words, id) in _lined)
            {
                if (words.Length <= bestN || words.Length > w.Length) continue;
                for (int at = 0; at + words.Length <= w.Length; at++)
                {
                    bool same = true; for (int k = 0; k < words.Length && same; k++) same = w[at + k] == words[k];
                    if (same) { best = id; bestN = words.Length; break; }
                }
            }
            return best;
        }

        /// <summary>The job a job id stands for in its line: a riding or costume look becomes its job (Game/SkillTree.cs JobFromLook's six
        /// Peco classes; any other class without a parent by its NAME in the client's own table: JT_RUNE_CHICKEN "Rune Knight" is
        /// JT_RUNE_KNIGHT; one without a name by its constant's words: <see cref="ByWords"/>), a baby class its grown one (JT_KNIGHT_B -> JT_KNIGHT: the same look as its parents'). -1: nothing known.</summary>
        public static int Canonical(int job)
        {
            Index();
            var j = SkillTreeDb.GetJob(job); if (j == null) return -1;
            if (j.Parent < 0 && job != 0 && j.Const != "JT_DO_SUMMONER")
            {
                int look = SkillTreeDb.JobFromLook(job);
                if (look >= 0 && look != job) return Canonical(look);
                if (j.Name.Length > 0 && _byName.TryGetValue(j.Name, out int named) && named != job) return Canonical(named);
                int worded = ByWords(j.Const);
                if (worded >= 0 && worded != job) return Canonical(worded);
                return job;
            }
            if (j.Const.EndsWith("_B", StringComparison.Ordinal) && _byConst.TryGetValue(j.Const.Substring(0, j.Const.Length - 2), out int grown) && grown != job) return Canonical(grown);
            return job;
        }

        /// <summary>A job's line: the second job its inherit chain passes (Game/SkillTree.cs Chain), else its first job, else Novice.</summary>
        public static JobLine LineOf(int job)
        {
            job = Canonical(job); if (job < 0) return JobLine.Novice;
            var chain = SkillTreeDb.Chain(job); // the job first, the root last
            foreach (int j in chain)
                switch (j)
                {
                    case 7: return JobLine.Knight; case 14: return JobLine.Crusader; case 9: return JobLine.Wizard; case 16: return JobLine.Sage;
                    case 11: return JobLine.Hunter; case 19: case 20: return JobLine.Bard; case 8: return JobLine.Priest; case 15: return JobLine.Monk;
                    case 10: return JobLine.Blacksmith; case 18: return JobLine.Alchemist; case 12: return JobLine.Assassin; case 17: return JobLine.Rogue;
                    case 4047: return JobLine.StarGladiator; case 4049: return JobLine.SoulLinker;
                }
            foreach (int j in chain)
                switch (j)
                {
                    case 1: return JobLine.Swordsman; case 2: return JobLine.Mage; case 3: return JobLine.Archer; case 4: return JobLine.Acolyte;
                    case 5: return JobLine.Merchant; case 6: return JobLine.Thief; case 23: return JobLine.SuperNovice; case 24: return JobLine.Gunslinger;
                    case 25: return JobLine.Ninja; case 4046: return JobLine.Taekwon; case 4218: return JobLine.Summoner;
                }
            return JobLine.Novice;
        }

        /// <summary>A job's place in its line by the skill trees' own rule (w22tree UI/TreeThemes.cs TreeGrowth.Rank / OrnamentOf: the
        /// tabs of its inherit chain as Game/SkillTree.cs SkillTreeModel.Build splits them): 0 Novice, 1 first job, 2 second job, 3
        /// transcendent second job, 4 third job, 5 fourth job or the last class of an expanded line (Shinkiro, Night Watch, Hyper
        /// Novice, Spirit Handler). <paramref name="reborn"/>: a "_H" class is in its chain (High Novice, High Swordman, Lord Knight ...).</summary>
        public static int TierOf(int job, out bool reborn)
        {
            reborn = false;
            job = Canonical(job); if (job < 0) return 0;
            var chain = SkillTreeDb.Chain(job); chain.Reverse(); // root first
            int tabs = 0;
            for (int i = 0; i < chain.Count; i++)
            {
                var cj = SkillTreeDb.GetJob(chain[i]); if (cj == null) continue;
                if (cj.Const.EndsWith("_H", StringComparison.Ordinal)) reborn = true;
                bool same = false;
                if (i > 0)
                {
                    var pj = SkillTreeDb.GetJob(chain[i - 1]);
                    same = chain[i - 1] == 0 || (pj != null && (cj.Const == pj.Const + "_H" || cj.Const == pj.Const + "_B"));
                }
                if (!same) tabs++;
            }
            var me = SkillTreeDb.GetJob(job);
            bool novice = me == null || me.Const == "JT_NOVICE" || me.Const == "JT_NOVICE_H" || me.Const == "JT_NOVICE_B";
            if (novice) return 0;
            int rank = Mathf.Clamp(tabs, 1, 4);
            bool last = false;
            if (rank >= 2)
            {
                last = true;
                foreach (var j in SkillTreeDb.AllJobs())
                    if ((j.Parent == job || j.Parent2 == job) && !j.Const.EndsWith("_B", StringComparison.Ordinal)) { last = false; break; }
            }
            if (rank >= 4 || (last && rank >= 2)) return 5;
            if (rank == 3) return 4;
            if (rank == 2) return reborn ? 3 : 2;
            return 1;
        }

        private static readonly Dictionary<int, Variant> _variants = new Dictionary<int, Variant>();
        private static readonly Dictionary<int, Variant> _ofJob = new Dictionary<int, Variant>();
        /// <summary>The variant of a line at a tier (made once; its pictures only when it is needed).</summary>
        public static Variant Get(JobLine line, int tier, bool reborn)
        {
            tier = Mathf.Clamp(tier, 0, TierMax); if (tier >= 2) reborn = tier >= 3;
            var v = new Variant { Line = line, Tier = tier, Reborn = reborn };
            if (_variants.TryGetValue(v.Key, out var hit)) return hit;
            v.HasAccent = AccentOf(line, out v.Accent);
            v.Thread = BaseThread;
            if (v.HasAccent && tier >= 1) { var t = Color.Lerp(Theme.Leather, v.Accent, 0.8f); v.Thread = new Color(t.r, t.g, t.b, 0.9f); }
            _variants[v.Key] = v;
            return v;
        }
        /// <summary>The variant of a job (null: the client's job table is not there: the base look).</summary>
        public static Variant Of(int job)
        {
            if (job < 0 || !SkillTreeDb.Loaded) return null;
            if (_ofJob.TryGetValue(job, out var v)) return v;
            int tier = TierOf(job, out bool reborn);
            v = Get(LineOf(job), tier, reborn);
            _ofJob[job] = v;
            return v;
        }

        // ------------------------------------------------------------------ the kit reads its pictures through here
        private static readonly Sprite[] _base = new Sprite[Slots];
        private static readonly Dictionary<Sprite, Slot> _slotOf = new Dictionary<Sprite, Slot>();
        private static Variant _applied, _want; private static bool _made, _dirty; private static GameObject _driver;
        internal static readonly List<JobThemeOwner> Owners = new List<JobThemeOwner>();
        /// <summary>The base stitching's thread (UI/UiKit.cs Stitch before wave 21's job variants): leather brown, 85 %.</summary>
        public static Color BaseThread => new Color(Theme.Leather.r, Theme.Leather.g, Theme.Leather.b, 0.85f);
        /// <summary>The variant our panels wear now (null: Base).</summary>
        public static Variant Applied => _applied;
        /// <summary>The kit's own sprite of a slot (null until the kit made it).</summary>
        public static Sprite BaseOf(Slot s) => _base[(int)s];
        /// <summary>The setting: panels follow our job (false: Base).</summary>
        public static bool UseJob => VR.Comfort.PanelTheme < 0 ? DefaultMyJob : VR.Comfort.PanelTheme == 0;

        /// <summary>UI/Theme.cs: the picture a panel built NOW gets for <paramref name="slot"/>: the variant's that is on, or the kit's
        /// own <paramref name="baseSprite"/> (Base, no character yet, a slot the variant leaves as it is).</summary>
        internal static Sprite Pick(Slot slot, Sprite baseSprite)
        {
            int i = (int)slot;
            if (!ReferenceEquals(_base[i], baseSprite)) { _base[i] = baseSprite; if (baseSprite != null) _slotOf[baseSprite] = slot; }
            if (!_made) MakeDriver();
            _dirty = true;
            var v = _applied;
            if (v == null) return baseSprite;
            var sp = v.Sprites[i];
            return sp != null ? sp : baseSprite;
        }
        /// <summary>UI/Theme.cs: the stitching's thread now.</summary>
        internal static Color Thread(Color baseThread) => _applied != null ? _applied.Thread : baseThread;

        private static void MakeDriver()
        {
            _made = true;
            if (!Application.isPlaying) return;
            _driver = new GameObject("JobTheme") { hideFlags = HideFlags.DontSave };
            UnityEngine.Object.DontDestroyOnLoad(_driver);
            _driver.AddComponent<JobThemeDriver>();
        }

        /// <summary>Marks <paramref name="root"/> as another player's: its themed pictures follow THAT player's job (UI/FriendGestures.cs:
        /// a friend's shared copies of their casting menus).</summary>
        public static JobThemeOwner Own(Transform root, uint aid)
        {
            if (root == null) return null;
            var o = root.GetComponent<JobThemeOwner>(); if (o == null) o = root.gameObject.AddComponent<JobThemeOwner>();
            o.Aid = aid; o.Known = false; o.Dirty = true; o.NextAsk = 0f;
            if (!Owners.Contains(o)) Owners.Add(o);
            if (!_made) MakeDriver();
            return o;
        }

        internal static void NotifyPicture(Transform t)
        {
            var o = OwnerOf(t);
            if (o != null) o.Dirty = true;
        }

        // ------------------------------------------------------------------ following the job
        /// <summary>The variant our panels should wear by the setting and our job now (null: Base).</summary>
        public static Variant Wanted()
        {
            if (!UseJob) return null;
            int job = -1;
            try { if (Job != null) job = Job(); } catch (Exception e) { Debug.LogException(e); }
            return job < 0 ? null : Of(job);
        }

        /// <summary>Numbers of the last change (the smoke and the progress file): the pictures' making, the swap, how many images and
        /// stitchings took a new picture, how many frames the making was spread over.</summary>
        public static float LastBuildMs { get; private set; }
        public static float LastRedressMs { get; private set; }
        public static int LastRedressImages { get; private set; }
        public static int LastRedressStitches { get; private set; }
        public static int LastBuildFrames { get; private set; }
        public static float LastCollectMs { get; private set; }
        public static int Redresses { get; private set; }
        /// <summary>A new variant is made in this many steps, one a frame: the emblem's shapes (2), then the five pictures, the frame's
        /// (the largest) in three (what lies under the corner pieces, the corner pieces, the texture).</summary>
        public const int PrepSteps = 2, FrameSteps = 3, BuildFrames = PrepSteps + Slots + FrameSteps - 1;
        /// <summary>The list of themed images (<see cref="Collect"/>) may be this many frames old at the swap.</summary>
        public const int CollectFreshFrames = 8;
        private static OutsetImage[] _imgs; private static StitchGraphic[] _stitches; private static int _collectedAt = -100000;
        /// <summary>Every image of the kit's own kind and every stitching alive now, shown or hidden (two walks over all objects: the
        /// dearest part of a re-dress, so it is done one frame BEFORE the swap, in a frame that makes one of the small pictures).</summary>
        private static void Collect()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _imgs = Resources.FindObjectsOfTypeAll<OutsetImage>(); _stitches = Resources.FindObjectsOfTypeAll<StitchGraphic>();
            _collectedAt = Time.frameCount; LastCollectMs = (float)sw.Elapsed.TotalMilliseconds;
        }
        /// <summary>The check after a swap (<see cref="Stragglers"/>): its ms, the images it looked at, those it still had to change.</summary>
        public static float LastCheckMs { get; private set; }
        public static int LastCheckImages { get; private set; }
        public static int LastCheckChanged { get; private set; }
        private static int _buildFrames, _checkIn = -1;
        /// <summary>Frames between the swap and the check of every other image.</summary>
        public const int CheckAfterFrames = 3;

        internal static void Tick()
        {
            // our own: one picture a frame, then every panel at once
            var want = Wanted();
            if (!ReferenceEquals(want, _want)) { _want = want; _buildFrames = 0; }
            Variant pending = _want != null && !_want.Ready ? _want : null;
            // the others': their job is asked for now and then (a unit comes into view, changes job)
            for (int i = Owners.Count - 1; i >= 0; i--)
            {
                var o = Owners[i]; if (o == null) { Owners.RemoveAt(i); continue; }
                if (Time.unscaledTime >= o.NextAsk)
                {
                    o.NextAsk = Time.unscaledTime + OwnerAskSeconds;
                    int job = -1;
                    try { if (JobOfAccount != null) job = JobOfAccount(o.Aid); } catch (Exception e) { Debug.LogException(e); }
                    if (job >= 0) { o.Want = UseJob ? Of(job) : null; o.Known = true; }
                    else if (!o.Known || !UseJob) o.Want = null; // never seen: the base look (FLAGGED); seen before: the look it had
                }
                if (pending == null && o.Want != null && !o.Want.Ready) pending = o.Want;
            }
            if (pending != null)
            {
                BuildStep(pending);
                if (ReferenceEquals(pending, _want)) { _buildFrames++; if (pending.Built == Slots - 1) Collect(); } // (the frame of the small title plate: the next one swaps)
            }
            if (!ReferenceEquals(_want, _applied) && (_want == null || _want.Ready))
            {
                if (Time.frameCount - _collectedAt > CollectFreshFrames) { Collect(); _buildFrames++; return; } // (a variant made earlier, or Base: the list in this frame, the swap in the next)
                _applied = _want; LastBuildFrames = _buildFrames; LastBuildMs = _applied != null ? _applied.BuildMs : 0f;
                Redress(); _dirty = false; _checkIn = CheckAfterFrames;
                Debug.Log($"[JobTheme] panels now: {(_applied != null ? _applied.ToString() : "Base")} (pictures {LastBuildMs:0.0} ms over {LastBuildFrames} frames, the longest step {(_applied != null ? _applied.MaxStepMs : 0f):0.0} ms; the list of themed images {LastCollectMs:0.00} ms a frame earlier; swap {LastRedressMs:0.00} ms: {LastRedressImages} images, {LastRedressStitches} stitchings; in memory {MemoryBytes / 1024} KiB)");
                try { Changed?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
                return;
            }
            if (_checkIn >= 0 && --_checkIn < 0) { Stragglers(); Sweep(); }
            // a friend's copies: made a moment ago in our look, or their owner's look changed
            if (Owners.Count > 0)
            {
                bool any = false;
                foreach (var o in Owners)
                {
                    if (o != null && (o.Want == null || o.Want.Ready) && (o.Dirty || !ReferenceEquals(o.Want, o.Applied)))
                    {
                        RedressOwner(o);
                        any = true;
                    }
                }
                if (any) Sweep();
            }
            _dirty = false;
        }
        /// <summary>How often a friend's job is asked for, seconds.</summary>
        public const float OwnerAskSeconds = 1f;

        /// <summary>Everything at once, now (the smoke; a caller that cannot wait five frames): the wanted variant's pictures made in
        /// this call and every panel re-dressed. Returns the variant that is on (null: Base).</summary>
        public static Variant ApplyNow()
        {
            var want = Wanted(); _want = want;
            if (want != null) while (!want.Ready) BuildStep(want);
            if (!ReferenceEquals(want, _applied)) { _applied = want; LastBuildFrames = 0; LastBuildMs = want != null ? want.BuildMs : 0f; Collect(); Redress(); Stragglers(); _checkIn = -1; Sweep(); try { Changed?.Invoke(); } catch (Exception e) { Debug.LogException(e); } }
            foreach (var o in Owners) if (o != null) { o.NextAsk = 0f; }
            return _applied;
        }

        /// <summary>One more picture of a variant (the order of <see cref="Slot"/>).</summary>
        internal static void BuildStep(Variant v)
        {
            if (v.Ready) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (v.Prep < PrepSteps)
            {
                // the emblem's shapes first, in frames of their own (the pictures then find them made)
                try { Prepare(v, v.Prep); } catch (Exception e) { Debug.LogException(e); }
                v.Prep++;
                float pms = (float)sw.Elapsed.TotalMilliseconds; v.BuildMs += pms; if (pms > v.MaxStepMs) v.MaxStepMs = pms;
                return;
            }
            int i = v.Built; var slot = (Slot)i;
            var b = _base[i] != null ? _base[i] : BaseNow(slot);
            Sprite sp = null;
            if (slot == Slot.Frame && b != null && v.Stage < FrameSteps - 1)
            {
                try { FrameStage(v, b, v.Stage); } catch (Exception e) { Debug.LogException(e); v.Px = null; }
                v.Stage++;
                float fms = (float)sw.Elapsed.TotalMilliseconds; v.BuildMs += fms; if (fms > v.MaxStepMs) v.MaxStepMs = fms;
                return;
            }
            try { sp = b != null ? MakeSprite(v, slot, b) : null; } catch (Exception e) { Debug.LogException(e); }
            if (slot == Slot.Frame) { v.Px = null; v.Stage = 0; }
            if (sp != null) { v.Sprites[i] = sp; v.Own[i] = true; _slotOf[sp] = slot; v.Bytes += (long)sp.texture.width * sp.texture.height * 4 * 7 / 3; } // (the texture, its smaller copies, its readable copy)
            else { v.Sprites[i] = b; v.Own[i] = false; } // this tier leaves the slot as the base has it
            v.Built++;
            float ms = (float)sw.Elapsed.TotalMilliseconds; v.BuildMs += ms; if (ms > v.MaxStepMs) v.MaxStepMs = ms;
        }
        /// <summary>The kit's sprite of a slot, made now if no panel asked for it yet (read through the kit's own getter).</summary>
        private static Sprite BaseNow(Slot slot)
        {
            var keep = _applied; _applied = null; // the getter gives the base while nothing is applied
            try
            {
                switch (slot)
                {
                    case Slot.Frame: return Theme.Frame; case Slot.Canvas: return Theme.CanvasFill; case Slot.Card: return Theme.CardFill;
                    case Slot.Plate: return Theme.Plate; default: return Theme.SeeFrame;
                }
            }
            finally { _applied = keep; }
        }

        private static JobThemeOwner OwnerOf(Transform t) => Owners.Count == 0 || t == null ? null : t.GetComponentInParent<JobThemeOwner>(true);
        private static Sprite SpriteFor(Variant v, Slot slot)
        {
            var b = _base[(int)slot];
            if (v == null) return b;
            var sp = v.Sprites[(int)slot];
            return sp != null ? sp : b;
        }
        private static readonly HashSet<int> _threads = new HashSet<int>();
        private static int ThreadKey(Color c) { Color32 k = c; return (k.r << 24) | (k.g << 16) | (k.b << 8) | k.a; }
        private static bool IsThread(Color c)
        {
            if (_threads.Count == 0) _threads.Add(ThreadKey(BaseThread));
            return _threads.Contains(ThreadKey(c));
        }
        /// <summary>A sprite is one of the theme's five panel pictures (the kit's own or a variant's).</summary>
        internal static bool IsThemePicture(Sprite s) => s != null && _slotOf.ContainsKey(s);
        /// <summary>A stitching's colour is a variant's thread (not the base's leather brown).</summary>
        internal static bool IsVariantThread(Color c) => IsThread(c) && ThreadKey(c) != ThreadKey(BaseThread);

        /// <summary>Every themed picture of OUR panels takes the variant that is on: the sprite of each image that holds one of the
        /// theme's five pictures (any variant's), the colour of each stitching that has a thread colour. Shown or hidden; nothing is
        /// rebuilt, no rectangle, collider or child is touched. A friend's copies (under a <see cref="JobThemeOwner"/>) keep theirs.
        /// The images looked at are the kit's own kind (<see cref="OutsetImage"/>: UI/UiKit.cs Picture makes every one of the five
        /// pictures with it); any OTHER image that was given one of them by hand is found by <see cref="Stragglers"/> a few frames
        /// later, before a variant's pictures can be destroyed; so is an image made between the list (<see cref="Collect"/>) and
        /// the swap (with its stitching).</summary>
        public static void Redress()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int n = 0, st = 0;
            var v = _applied;
            if (v != null) { v.UsedAt = Time.frameCount; _threads.Add(ThreadKey(v.Thread)); }
            if (_imgs == null || Time.frameCount - _collectedAt > CollectFreshFrames) Collect();
            foreach (var img in _imgs)
            {
                if (img == null) continue; // (destroyed since the list was made)
                var sp = img.sprite; if (sp == null || !_slotOf.TryGetValue(sp, out var slot)) continue;
                if (OwnerOf(img.transform) != null) continue;
                var want = SpriteFor(v, slot);
                if (want != null && !ReferenceEquals(sp, want)) { img.sprite = want; n++; }
            }
            Color thread = v != null ? v.Thread : BaseThread;
            foreach (var s in _stitches)
            {
                if (s == null || !IsThread(s.color) || ThreadKey(s.color) == ThreadKey(thread)) continue;
                if (OwnerOf(s.transform) != null) continue;
                s.color = thread; st++;
            }
            LastRedressMs = (float)sw.Elapsed.TotalMilliseconds; LastRedressImages = n; LastRedressStitches = st; Redresses++;
        }

        /// <summary>Every image of ANY kind that still holds another variant's picture takes ours (a plain Image some code gave
        /// Theme.Plate by hand: none in the game today). Runs <see cref="CheckAfterFrames"/> frames after a swap, so the two costs
        /// never fall into one frame.</summary>
        private static void Stragglers()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int seen = 0, n = 0; var v = _applied;
            foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
            {
                seen++;
                var sp = img.sprite; if (sp == null || !_slotOf.TryGetValue(sp, out var slot)) continue;
                if (OwnerOf(img.transform) != null) continue;
                var want = SpriteFor(v, slot);
                if (want != null && !ReferenceEquals(sp, want)) { img.sprite = want; n++; }
            }
            if (n > 0)
            {
                // something was made between the list and the swap: its stitching too
                Color thread = v != null ? v.Thread : BaseThread;
                foreach (var s in Resources.FindObjectsOfTypeAll<StitchGraphic>())
                    if (s != null && IsThread(s.color) && ThreadKey(s.color) != ThreadKey(thread) && OwnerOf(s.transform) == null) s.color = thread;
            }
            LastCheckMs = (float)sw.Elapsed.TotalMilliseconds; LastCheckImages = seen; LastCheckChanged = n;
        }

        private static void RedressOwner(JobThemeOwner o)
        {
            var v = o.Want; o.Applied = v; o.Dirty = false;
            if (v != null) { v.UsedAt = Time.frameCount; _threads.Add(ThreadKey(v.Thread)); }
            foreach (var img in o.GetComponentsInChildren<Image>(true))
            {
                var sp = img.sprite; if (sp == null || !_slotOf.TryGetValue(sp, out var slot)) continue;
                var want = SpriteFor(v, slot);
                if (want != null && !ReferenceEquals(sp, want)) img.sprite = want;
            }
            Color thread = v != null ? v.Thread : BaseThread;
            foreach (var s in o.GetComponentsInChildren<StitchGraphic>(true))
                if (IsThread(s.color) && ThreadKey(s.color) != ThreadKey(thread)) s.color = thread;
        }

        /// <summary>Variants kept made although nobody wears them (a job changed back, a friend who left): the newest.</summary>
        public const int KeepSpare = 2;
        /// <summary>What every made variant's pictures hold now, bytes.</summary>
        public static long MemoryBytes { get { long b = 0; foreach (var v in _variants.Values) if (v.Built > 0) b += v.Bytes; return b; } }
        /// <summary>Variants whose pictures are made now.</summary>
        public static int MadeCount { get { int n = 0; foreach (var v in _variants.Values) if (v.Built > 0) n++; return n; } }

        /// <summary>The pictures of variants nobody wears are destroyed, but for the <see cref="KeepSpare"/> newest.</summary>
        private static void Sweep()
        {
            List<Variant> spare = null;
            foreach (var v in _variants.Values)
            {
                if (v.Built == 0 || ReferenceEquals(v, _applied) || ReferenceEquals(v, _want)) continue;
                bool used = false;
                foreach (var o in Owners) if (o != null && (ReferenceEquals(o.Want, v) || ReferenceEquals(o.Applied, v))) { used = true; break; }
                if (!used) (spare ?? (spare = new List<Variant>())).Add(v);
            }
            if (spare == null || spare.Count <= KeepSpare) return;
            spare.Sort((a, b) => b.UsedAt.CompareTo(a.UsedAt));
            for (int i = KeepSpare; i < spare.Count; i++) Drop(spare[i]);
        }
        /// <summary>Destroys a variant's own pictures (nobody may hold them).</summary>
        internal static void Drop(Variant v)
        {
            for (int i = 0; i < Slots; i++)
            {
                var sp = v.Sprites[i];
                if (sp != null && v.Own[i]) { _slotOf.Remove(sp); var tex = sp.texture; UnityEngine.Object.Destroy(sp); if (tex != null) UnityEngine.Object.Destroy(tex); }
                v.Sprites[i] = null; v.Own[i] = false;
            }
            v.Built = 0; v.Prep = 0; v.Stage = 0; v.Px = null; v.Bytes = 0; v.BuildMs = 0f; v.MaxStepMs = 0f;
        }
    }

    /// <summary>Runs <see cref="JobTheme"/>'s following of the job after everything else of the frame (a friend's copy built this frame
    /// is re-dressed before it is drawn).</summary>
    [DefaultExecutionOrder(20000)]
    public sealed class JobThemeDriver : MonoBehaviour
    {
        private void LateUpdate() { JobTheme.Tick(); }
    }
}
