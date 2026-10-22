using System;
using System.Collections.Generic;
using Loki.Game;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>The job families of the 3D skill tree: each has its own trunk, branches, ornaments and colours (<see cref="TreeTheme"/>).</summary>
    public enum TreeFamily { Novice, Swordsman, Mage, Acolyte, Archer, Thief, Merchant, Gunslinger, Ninja, Taekwon, Summoner }

    /// <summary>
    /// Client wave 22 (w22tree), the design's point 64. HOW ORNATE a job's tree is: ONE FIXED LOOK PER JOB, by the job's own place in its line,
    /// never by the character's current job or level: <see cref="OrnamentOf"/> = <see cref="ClassStep"/>[the job's rank]; what an ornament
    /// level shows is <see cref="Stages"/>.
    /// HOW ALIVE it looks: <see cref="Liveliness"/>, 0-1 with the job level in THAT job.
    /// </summary>
    public static class TreeGrowth
    {
        public static readonly int[] ClassStep = { 0, 2, 5, 8, 11, 14 };
        public const int Max = 14;

        public struct Stage
        {
            public int Leaves, Twigs, Banners, Trunk, Frame; public bool Finials, GoldEdge, Curls, DoubleCurls, Base; public float Radius;
            public Stage(int leaves, int twigs, int banners, int trunk, int frame, bool finials, bool goldEdge, bool curls, bool doubleCurls, bool @base, float radius)
            { Leaves = leaves; Twigs = twigs; Banners = banners; Trunk = trunk; Frame = frame; Finials = finials; GoldEdge = goldEdge; Curls = curls; DoubleCurls = doubleCurls; Base = @base; Radius = radius; }
        }
        public static readonly Stage[] Stages =
        {
            //        leaves twigs banners trunk frame finials gold   curls  double base   radius
            new Stage(0,     0,    0,      0,    0,    false,  false, false, false, false, 7.2f),  //  0 a Novice's tree
            new Stage(1,     0,    1,      0,    0,    false,  false, false, false, false, 7.5f),  //  1
            new Stage(2,     1,    2,      1,    1,    true,   false, false, false, true,  8.0f),  //  2 a FIRST job's tree: rich detail & personality
            new Stage(2,     1,    2,      1,    1,    true,   false, false, false, true,  8.4f),  //  3
            new Stage(2,     1,    3,      2,    1,    true,   false, false, false, true,  8.8f),  //  4
            new Stage(3,     2,    4,      2,    2,    true,   true,  true,  false, true,  9.4f),  //  5 a SECOND job's tree: bespoke sculpture & scrollwork
            new Stage(3,     2,    4,      2,    2,    true,   true,  true,  false, true,  9.7f),  //  6
            new Stage(4,     2,    5,      2,    2,    true,   true,  true,  false, true,  10.0f), //  7
            new Stage(5,     3,    6,      3,    3,    true,   true,  true,  true,  true,  10.5f), //  8 a TRANSCENDENT second job's tree
            new Stage(5,     3,    6,      3,    3,    true,   true,  true,  true,  true,  10.8f), //  9
            new Stage(6,     3,    7,      3,    3,    true,   true,  true,  true,  true,  11.1f), // 10
            new Stage(7,     4,    8,      3,    3,    true,   true,  true,  true,  true,  11.5f), // 11 a THIRD job's tree
            new Stage(8,     4,    8,      4,    3,    true,   true,  true,  true,  true,  11.8f), // 12
            new Stage(9,     5,    9,      4,    3,    true,   true,  true,  true,  true,  12.0f), // 13
            new Stage(10,    6,    10,     4,    3,    true,   true,  true,  true,  true,  12.4f), // 14 a FOURTH job's tree (or a line's last class)
        };
        public static Stage StageOf(int ornament) => Stages[Mathf.Clamp(ornament, 0, Stages.Length - 1)];

        public static int Rank(int job, out bool reborn, out bool last)
        {
            reborn = false; last = false;
            var chain = SkillTreeDb.Chain(job); chain.Reverse(); // root first
            int tabs = 0;
            for (int i = 0; i < chain.Count; i++)
            {
                var cj = SkillTreeDb.GetJob(chain[i]); if (cj == null) continue;
                if (cj.Const.EndsWith("_H")) reborn = true;
                bool same = false;
                if (i > 0)
                {
                    var pj = SkillTreeDb.GetJob(chain[i - 1]);
                    same = chain[i - 1] == 0 || (pj != null && (cj.Const == pj.Const + "_H" || cj.Const == pj.Const + "_B"));
                }
                if (!same) tabs++;
            }
            var me = SkillTreeDb.GetJob(job);
            bool novice = me == null || chain.Count <= 1 && me.Const.StartsWith("JT_NOVICE") || me.Const == "JT_NOVICE_H" || me.Const == "JT_NOVICE_B";
            int rank = novice ? 0 : Mathf.Clamp(tabs, 1, 4);
            if (rank >= 2)
            {
                last = true;
                foreach (var j in SkillTreeDb.AllJobs())
                    if ((j.Parent == job || j.Parent2 == job) && !j.Const.EndsWith("_B")) { last = false; break; }
            }
            return rank;
        }

        public static int OrnamentOf(int job)
        {
            int rank = Rank(job, out bool reborn, out bool last);
            if (rank == 0) return ClassStep[0];
            if (rank >= 4 || (last && rank >= 2)) return ClassStep[5];
            if (rank == 3) return ClassStep[4];
            if (rank == 2) return reborn ? ClassStep[3] : ClassStep[2];
            return ClassStep[1];
        }

        public static int MaxJobLevel(int job)
        {
            var j = SkillTreeDb.GetJob(job); string c = j != null ? j.Const : "";
            if (c.StartsWith("JT_NOVICE")) return 10;
            if (c.StartsWith("JT_SUPERNOVICE2")) return 70;
            if (c.StartsWith("JT_SUPERNOVICE")) return 99;
            if (c.StartsWith("JT_DO_SUMMONER")) return 60;
            if (c.StartsWith("JT_GUNSLINGER") || c.StartsWith("JT_NINJA") || c.StartsWith("JT_KAGEROU") || c.StartsWith("JT_OBORO") || c.StartsWith("JT_REBELLION") || c.StartsWith("JT_STAR_EMPEROR") || c.StartsWith("JT_SOUL_REAPER")) return 70;
            int rank = Rank(job, out bool reborn, out bool last);
            if (rank >= 4 || (last && rank >= 2 && !c.EndsWith("_H"))) return 60;
            if (rank == 3) return 70;
            if (rank == 2) return reborn ? 70 : 50;
            return 50;
        }

        public static float Liveliness(int job, int jobLevel, bool atMaximum)
        {
            if (atMaximum) return 1f;
            int max = Mathf.Max(2, MaxJobLevel(job));
            return Mathf.Clamp01((jobLevel - 1) / (float)(max - 1));
        }

        /// <summary>Maximum skill points threshold required for a tree at <paramref name="ornament"/> level to reach 100% beauty/liveliness:
        /// Novice/Basic = 9, 1st/2nd/Trans-2nd/3rd Class = 35, 4th/Expanded Class = 30.</summary>
        public static int MaxBeautyThreshold(int ornament)
        {
            if (ornament <= ClassStep[0]) return 9;
            if (ornament >= ClassStep[5]) return 30;
            return 35;
        }

        /// <summary>Liveliness (0-1) driven by skill points spent (or planned) in a specific job tree.</summary>
        public static float LivelinessByPoints(int ornament, int spentPoints, int maxPossiblePoints)
        {
            int threshold = MaxBeautyThreshold(ornament);
            int denom = maxPossiblePoints > 0 ? Mathf.Min(threshold, maxPossiblePoints) : threshold;
            return denom > 0 ? Mathf.Clamp01(spentPoints / (float)denom) : 1f;
        }
    }

    /// <summary>A job line's 3D skill tree look: bespoke sculpture, colours, branches, leaves, hangings, animation note, and synthesized sound.</summary>
    public sealed class TreeTheme
    {
        public enum BranchKind { Arc, Antler, Gothic, Twig, Thorn, Pipe, Rod, Bamboo, FeatherWing, KatarBlade, GlassHelix, RosaryChain }
        public enum LeafKind { Steel, Shard, Pane, Green, Spike, Rivet, Round, Bullet, Maple, Star, Feather, VenomShard, MusicNote, AlchDrop }
        public enum HangKind { Flag, Crystal, GlassPane, Feather, Cloth, Wares, Ribbon, Pennant, Talisman, Belt, Fish, PoisonVial, Scroll, Rosary, PotionFlask }

        public TreeFamily Family;
        public JobLine Line;
        public bool IsDancer;
        public string Name = "";
        public BranchKind Branch; public LeafKind Leaf; public HangKind Hang;
        public Color32 Trunk, Trunk2, Wood, Gold, LeafA, LeafB, ClothA, ClothB, Glow, Metal;
        public float BranchGloss, LeafGloss, LeafGlow;
        public Color32 Rim, RimOrnate, Face;
        public Color LifeColor = new Color(1f, 1f, 1f, 0.6f); public float Breath, BreathPeriod = 7f;
        public Color LifeColorAt(int ornament) => Family == TreeFamily.Swordsman && ornament >= 5 ? new Color(1f, 0.52f, 0.18f, 0.92f) : LifeColor;
        public string LifeNote = "";
        public string RiseNote = ""; public string[] RiseSounds = new string[0];

        private static Color32 C(int r, int g, int b, int a = 255) => new Color32((byte)r, (byte)g, (byte)b, (byte)a);
        private static readonly Dictionary<TreeFamily, TreeTheme> _byFamily = new Dictionary<TreeFamily, TreeTheme>();
        private static readonly Dictionary<JobLine, TreeTheme> _byLine = new Dictionary<JobLine, TreeTheme>();
        private static TreeTheme _dancer;

        public static JobLine LineForFamily(TreeFamily f)
        {
            switch (f)
            {
                case TreeFamily.Swordsman: return JobLine.Swordsman;
                case TreeFamily.Mage: return JobLine.Mage;
                case TreeFamily.Acolyte: return JobLine.Acolyte;
                case TreeFamily.Archer: return JobLine.Archer;
                case TreeFamily.Thief: return JobLine.Thief;
                case TreeFamily.Merchant: return JobLine.Merchant;
                case TreeFamily.Gunslinger: return JobLine.Gunslinger;
                case TreeFamily.Ninja: return JobLine.Ninja;
                case TreeFamily.Taekwon: return JobLine.Taekwon;
                case TreeFamily.Summoner: return JobLine.Summoner;
                default: return JobLine.Novice;
            }
        }

        public static TreeTheme Of(TreeFamily f)
        {
            if (_byFamily.TryGetValue(f, out var t)) return t;
            t = OfLine(LineForFamily(f));
            _byFamily[f] = t;
            return t;
        }

        public static bool IsDancerJob(int job)
        {
            job = JobTheme.Canonical(job);
            if (job < 0) return false;
            foreach (int j in SkillTreeDb.Chain(job))
                if (j == 20) return true;
            return false;
        }

        public static Func<int, bool, TreeTheme, TreeTheme> PreviewThemeHook;

        public static TreeTheme OfJob(int job, bool other = false)
        {
            TreeTheme res;
            if (other || job <= 0) res = OfLine(JobLine.Novice);
            else if (IsDancerJob(job))
            {
                if (_dancer == null)
                {
                    _dancer = new TreeTheme
                    {
                        Family = TreeFamily.Archer, Line = JobLine.Bard, IsDancer = true, Name = "Dancer",
                        Gold = C(242, 198, 92), Metal = C(212, 206, 224), Face = C(232, 220, 198),
                        Rim = C(188, 134, 156), RimOrnate = C(244, 198, 96), BranchGloss = 0.9f, LeafGloss = 0.88f, LeafGlow = 0.45f,
                        LifeColor = new Color(1f, 0.68f, 0.84f, 0.9f), Breath = 0.06f, BreathPeriod = 5.4f,
                        LifeNote = "the pleated rose-silk dance fans, coiled ribbon whip and lotus tambourine shimmer with enchanting grace",
                        RiseNote = "twin pleated silk dance fans unfurl in a graceful spiral of rose and gold ribbons",
                        RiseSounds = new[] { "synth:bard", "se_ding2.wav" },
                        Branch = BranchKind.Arc, Leaf = LeafKind.MusicNote, Hang = HangKind.Ribbon,
                        Trunk = C(228, 72, 128), Trunk2 = C(244, 194, 88), Wood = C(224, 168, 86),
                        LeafA = C(248, 122, 168), LeafB = C(246, 208, 102),
                        ClothA = C(236, 68, 122), ClothB = C(168, 112, 238), Glow = C(255, 196, 224)
                    };
                }
                res = _dancer;
            }
            else res = OfLine(JobTheme.LineOf(job));
            return PreviewThemeHook != null ? (PreviewThemeHook(job, other, res) ?? res) : res;
        }

        public static TreeTheme OfLine(JobLine line)
        {
            if (_byLine.TryGetValue(line, out var t)) return t;
            var fam = FamilyOfLine(line);
            t = new TreeTheme
            {
                Family = fam, Line = line, Name = line.ToString(),
                Gold = C(224, 180, 84), Metal = C(192, 198, 210), Face = C(228, 216, 192),
                Rim = C(152, 156, 166), RimOrnate = C(224, 180, 84), BranchGloss = 0.35f, LeafGloss = 0.25f
            };
            switch (line)
            {
                // ==================== SWORDSMAN CHAIN ====================
                case JobLine.Swordsman:
                    t.LifeColor = new Color(0.96f, 0.98f, 1f, 0.88f);
                    t.LifeNote = "the forged broadsword blade polishes to mirror-steel and the warrior's crimson wraps glow warm";
                    t.RiseNote = "the forged broadsword plunges point-first into a cracked stone pedestal with a ring of dust and forge sparks";
                    t.RiseSounds = new[] { "synth:swordsman", "effect\\ef_bash.wav", "_hit_sword.wav" };
                    t.Branch = BranchKind.Arc; t.Leaf = LeafKind.Steel; t.Hang = HangKind.Flag;
                    t.Trunk = C(196, 204, 216); t.Trunk2 = C(168, 38, 34); t.Wood = C(152, 160, 174); t.LeafA = C(118, 128, 146); t.LeafB = C(148, 158, 176);
                    t.ClothA = C(176, 36, 32); t.ClothB = C(230, 182, 74); t.Glow = C(255, 224, 148); t.BranchGloss = 0.85f; t.LeafGloss = 0.82f; break;

                case JobLine.Knight:
                    t.LifeColor = new Color(1f, 0.76f, 0.28f, 0.92f); t.Breath = 0.04f; t.BreathPeriod = 6.5f;
                    t.LifeNote = "the spiraled cavalry lance, royal heater shield and valkyrie wings blaze with chivalric gold and cobalt";
                    t.RiseNote = "the royal heater shield locks forward with a heraldic flash as the spiraled cavalry lance thrusts skyward and valkyrie wings unfurl";
                    t.RiseSounds = new[] { "synth:knight", "effect\\ef_bash.wav" };
                    t.Branch = BranchKind.Arc; t.Leaf = LeafKind.Steel; t.Hang = HangKind.Pennant;
                    t.Trunk = C(214, 222, 236); t.Trunk2 = C(36, 78, 172); t.Wood = C(176, 186, 204); t.LeafA = C(196, 206, 224); t.LeafB = C(232, 190, 88);
                    t.ClothA = C(34, 74, 168); t.ClothB = C(240, 196, 86); t.Glow = C(130, 190, 255); t.Rim = C(138, 152, 182); t.RimOrnate = C(236, 192, 88); t.BranchGloss = 0.92f; t.LeafGloss = 0.9f; break;

                case JobLine.Crusader:
                    t.LifeColor = new Color(1f, 0.94f, 0.68f, 0.92f); t.Breath = 0.05f; t.BreathPeriod = 6f;
                    t.LifeNote = "the radiant templar grand-cross, winged aegis shield and halo shine with consecrated sapphire and gold";
                    t.RiseNote = "the radiant grand-cross shield descends in a cross-shaped beam of holy light, expanding a consecrated barrier ring";
                    t.RiseSounds = new[] { "synth:crusader", "effect\\ef_angelus.wav" };
                    t.Branch = BranchKind.Gothic; t.Leaf = LeafKind.Pane; t.Hang = HangKind.Flag;
                    t.Trunk = C(242, 238, 228); t.Trunk2 = C(232, 188, 82); t.Wood = C(218, 182, 92); t.LeafA = C(110, 178, 248); t.LeafB = C(248, 224, 136);
                    t.ClothA = C(238, 234, 222); t.ClothB = C(54, 112, 206); t.Glow = C(255, 238, 168); t.Rim = C(196, 178, 128); t.RimOrnate = C(244, 204, 98); t.BranchGloss = 0.92f; t.LeafGloss = 0.88f; t.LeafGlow = 0.45f; break;

                // ==================== MAGE CHAIN ====================
                case JobLine.Mage:
                    t.LifeColor = new Color(0.72f, 0.92f, 1f, 0.84f); t.Breath = 0.06f; t.BreathPeriod = 7f;
                    t.LifeNote = "the twisted elderwood wand and its three orbiting fire, ice and lightning spheres glow brighter";
                    t.RiseNote = "an arcane rune circle spins on the floor, the twisted elderwood staff spirals up and its tri-elemental orbs flare";
                    t.RiseSounds = new[] { "synth:mage", "effect\\ef_frostdiver2.wav" };
                    t.Branch = BranchKind.Antler; t.Leaf = LeafKind.Shard; t.Hang = HangKind.Crystal;
                    t.Trunk = C(108, 76, 54); t.Trunk2 = C(124, 194, 255); t.Wood = C(98, 70, 54); t.LeafA = C(144, 208, 255); t.LeafB = C(255, 136, 96);
                    t.ClothA = C(96, 70, 190); t.ClothB = C(150, 220, 255); t.Glow = C(140, 215, 255); t.LeafGloss = 0.9f; t.LeafGlow = 0.55f; t.Rim = C(120, 132, 160); t.RimOrnate = C(150, 190, 240); break;

                case JobLine.Wizard:
                    t.LifeColor = new Color(0.84f, 0.64f, 1f, 0.92f); t.Breath = 0.08f; t.BreathPeriod = 5.5f;
                    t.LifeNote = "the obsidian astral spire, interlocking golden celestial orrery rings and supernova crown blaze with cosmic power";
                    t.RiseNote = "three counter-rotating astrological circles ignite as the obsidian astral spire and celestial orrery rings snap into orbit with a supernova flash";
                    t.RiseSounds = new[] { "synth:wizard", "effect\\ef_soulstrike.wav" };
                    t.Branch = BranchKind.Antler; t.Leaf = LeafKind.Shard; t.Hang = HangKind.Crystal;
                    t.Trunk = C(46, 38, 68); t.Trunk2 = C(196, 132, 255); t.Wood = C(68, 52, 98); t.LeafA = C(186, 128, 255); t.LeafB = C(110, 220, 255);
                    t.ClothA = C(78, 42, 148); t.ClothB = C(236, 190, 92); t.Glow = C(198, 142, 255); t.Gold = C(238, 194, 96); t.LeafGloss = 0.95f; t.LeafGlow = 0.7f; t.Rim = C(128, 108, 168); t.RimOrnate = C(212, 164, 255); break;

                case JobLine.Sage:
                    t.LifeColor = new Color(0.64f, 0.98f, 0.86f, 0.88f); t.Breath = 0.06f; t.BreathPeriod = 6.5f;
                    t.LifeNote = "the levitating ancient grimoire's open rune pages, golden quill and sacred-geometry elemental prisms illuminate";
                    t.RiseNote = "the ancient grimoire flips open in mid-air with glowing rune pages fanning out as geometric elemental crystals spiral upward";
                    t.RiseSounds = new[] { "synth:sage", "effect\\ef_frostdiver2.wav" };
                    t.Branch = BranchKind.Gothic; t.Leaf = LeafKind.Shard; t.Hang = HangKind.Scroll;
                    t.Trunk = C(124, 82, 52); t.Trunk2 = C(96, 228, 196); t.Wood = C(148, 112, 74); t.LeafA = C(96, 226, 192); t.LeafB = C(244, 198, 96);
                    t.ClothA = C(238, 228, 204); t.ClothB = C(48, 138, 126); t.Glow = C(132, 246, 214); t.LeafGloss = 0.9f; t.LeafGlow = 0.55f; t.Rim = C(148, 138, 116); t.RimOrnate = C(228, 190, 96); break;

                // ==================== ARCHER CHAIN ====================
                case JobLine.Archer:
                    t.LifeColor = new Color(0.68f, 0.96f, 0.46f, 0.82f);
                    t.LifeNote = "the recurve yew longbow, nocked horizontal arrow and woodland boughs gleam with emerald wind-light";
                    t.RiseNote = "the recurve bow draws a nocked arrow horizontally back across its riser and fires it forward perpendicular to the bow shaft through wind rings";
                    t.RiseSounds = new[] { "synth:archer", "attack_bow1.wav", "attack_bow.wav" };
                    t.Branch = BranchKind.Twig; t.Leaf = LeafKind.Green; t.Hang = HangKind.Feather;
                    t.Trunk = C(214, 178, 126); t.Trunk2 = C(48, 116, 64); t.Wood = C(96, 134, 72); t.LeafA = C(98, 168, 74); t.LeafB = C(156, 202, 84);
                    t.ClothA = C(144, 88, 48); t.ClothB = C(240, 234, 218); t.Glow = C(186, 244, 146); t.Rim = C(138, 152, 118); break;

                case JobLine.Hunter:
                    t.LifeColor = new Color(1f, 0.86f, 0.44f, 0.9f); t.Breath = 0.05f; t.BreathPeriod = 6f;
                    t.LifeNote = "the sculpted hunting falcon's spread wing feathers, golden talons and amber raptor eyes gleam with wind-spirit light";
                    t.RiseNote = "the majestic hunting falcon swoops down with spread wings beating open, unleashing wind streams and drifting feathers";
                    t.RiseSounds = new[] { "synth:hunter", "attack_bow1.wav" };
                    t.Branch = BranchKind.FeatherWing; t.Leaf = LeafKind.Feather; t.Hang = HangKind.Feather;
                    t.Trunk = C(168, 124, 82); t.Trunk2 = C(238, 228, 208); t.Wood = C(138, 98, 64); t.LeafA = C(228, 216, 194); t.LeafB = C(184, 132, 78);
                    t.ClothA = C(196, 142, 76); t.ClothB = C(246, 238, 220); t.Glow = C(255, 224, 136); t.Gold = C(236, 190, 78); t.Rim = C(164, 142, 108); t.RimOrnate = C(236, 192, 84); t.BranchGloss = 0.55f; t.LeafGloss = 0.45f; break;

                case JobLine.Bard:
                    t.LifeColor = new Color(1f, 0.82f, 0.52f, 0.88f); t.Breath = 0.06f; t.BreathPeriod = 5.5f;
                    t.LifeNote = "the golden concert lyre's swan-neck arms, harmonic strings and floating musical notes shimmer with melody";
                    t.RiseNote = "the golden concert lyre rises with a graceful sway as its strings pluck shimmering harmonic rings and musical notes into the air";
                    t.RiseSounds = new[] { "synth:bard", "se_ding2.wav" };
                    t.Branch = BranchKind.Arc; t.Leaf = LeafKind.MusicNote; t.Hang = HangKind.Ribbon;
                    t.Trunk = C(232, 186, 84); t.Trunk2 = C(186, 52, 92); t.Wood = C(214, 166, 74); t.LeafA = C(246, 206, 98); t.LeafB = C(238, 118, 154);
                    t.ClothA = C(212, 64, 108); t.ClothB = C(116, 196, 236); t.Glow = C(255, 226, 148); t.Rim = C(184, 152, 98); t.RimOrnate = C(242, 198, 92); t.BranchGloss = 0.9f; t.LeafGloss = 0.85f; t.LeafGlow = 0.4f; break;

                // ==================== ACOLYTE CHAIN ====================
                case JobLine.Acolyte:
                    t.LifeColor = new Color(1f, 0.97f, 0.8f, 0.82f);
                    t.LifeNote = "the sanctuary marble column, swinging golden censer and stained-glass leaves warm with candle-light";
                    t.RiseNote = "the sanctuary pillar descends gently in a warm shaft of light as golden incense motes rise";
                    t.RiseSounds = new[] { "synth:acolyte", "effect\\ef_blessing.wav" };
                    t.Branch = BranchKind.Gothic; t.Leaf = LeafKind.Pane; t.Hang = HangKind.GlassPane;
                    t.Trunk = C(238, 232, 222); t.Trunk2 = C(218, 174, 82); t.Wood = C(214, 172, 82); t.LeafA = C(240, 152, 172); t.LeafB = C(138, 192, 242);
                    t.ClothA = C(152, 216, 162); t.ClothB = C(246, 216, 122); t.Glow = C(255, 236, 172); t.BranchGloss = 0.88f; t.LeafGloss = 0.72f; t.LeafGlow = 0.38f; t.Rim = C(200, 180, 130); break;

                case JobLine.Priest:
                    t.LifeColor = new Color(1f, 0.96f, 0.78f, 0.95f); t.Breath = 0.06f; t.BreathPeriod = 6.5f;
                    t.LifeNote = "the six spread seraphim angel wings, cathedral rose-window and blazing golden monstrance halo shine with sacred radiance";
                    t.RiseNote = "six feathered seraphim wings unfurl around the cathedral spire as a golden sunburst monstrance and triple shafts of light shine forth";
                    t.RiseSounds = new[] { "synth:priest", "effect\\ef_angelus.wav" };
                    t.Branch = BranchKind.Gothic; t.Leaf = LeafKind.Pane; t.Hang = HangKind.GlassPane;
                    t.Trunk = C(248, 244, 236); t.Trunk2 = C(240, 198, 92); t.Wood = C(234, 194, 96); t.LeafA = C(132, 206, 255); t.LeafB = C(255, 226, 138);
                    t.ClothA = C(246, 164, 188); t.ClothB = C(255, 238, 164); t.Glow = C(255, 244, 188); t.Gold = C(244, 204, 96); t.BranchGloss = 0.94f; t.LeafGloss = 0.88f; t.LeafGlow = 0.55f; t.Rim = C(212, 190, 136); t.RimOrnate = C(248, 212, 108); break;

                case JobLine.Monk:
                    t.LifeColor = new Color(1f, 0.78f, 0.32f, 0.92f); t.Breath = 0.07f; t.BreathPeriod = 5f;
                    t.LifeNote = "the golden vajra gauntlet, giant prayer rosary beads and five orbiting Zen spirit spheres blaze with inner ki";
                    t.RiseNote = "the golden vajra slams into its lotus pedestal with a martial shockwave as five glowing Zen spirit spheres ignite in orbit";
                    t.RiseSounds = new[] { "synth:monk", "_hit_fist1.wav" };
                    t.Branch = BranchKind.RosaryChain; t.Leaf = LeafKind.Star; t.Hang = HangKind.Rosary;
                    t.Trunk = C(208, 154, 68); t.Trunk2 = C(172, 54, 38); t.Wood = C(148, 96, 52); t.LeafA = C(255, 214, 96); t.LeafB = C(120, 218, 255);
                    t.ClothA = C(184, 58, 38); t.ClothB = C(244, 198, 84); t.Glow = C(255, 212, 118); t.Rim = C(168, 136, 92); t.RimOrnate = C(238, 190, 82); t.BranchGloss = 0.82f; t.LeafGloss = 0.88f; t.LeafGlow = 0.5f; break;

                // ==================== THIEF CHAIN ====================
                case JobLine.Thief:
                    t.LifeColor = new Color(0.86f, 0.62f, 1f, 0.82f);
                    t.LifeNote = "the crossed street stilettos, lockpick ring and stolen coin pouch glint through nocturnal violet haze";
                    t.RiseNote = "a dark violet smoke bomb bursts at the base as twin stilettos slash into an X-cross";
                    t.RiseSounds = new[] { "synth:thief", "effect\\ef_hiding.wav", "_attack_dagger.wav" };
                    t.Branch = BranchKind.Thorn; t.Leaf = LeafKind.Spike; t.Hang = HangKind.Cloth;
                    t.Trunk = C(64, 50, 56); t.Trunk2 = C(132, 68, 184); t.Wood = C(56, 44, 54); t.LeafA = C(78, 64, 82); t.LeafB = C(136, 68, 188);
                    t.ClothA = C(108, 52, 156); t.ClothB = C(58, 42, 72); t.Glow = C(204, 144, 255); t.Rim = C(112, 102, 124); t.RimOrnate = C(174, 124, 224); t.Metal = C(166, 172, 188); break;

                case JobLine.Assassin:
                    t.LifeColor = new Color(0.38f, 1f, 0.62f, 0.94f); t.Breath = 0.07f; t.BreathPeriod = 5.2f;
                    t.LifeNote = "the crossed tri-bladed Jur katars, spiked shadow chakram wheel and deadly poison vials pulse with lethal emerald-crimson venom";
                    t.RiseNote = "crimson and emerald shadow-slashes rip an X through the air as the crossed Jur katars snap outward and deadly poison sparks burst around the chakram";
                    t.RiseSounds = new[] { "synth:assassin", "_attack_dagger.wav" };
                    t.Branch = BranchKind.KatarBlade; t.Leaf = LeafKind.VenomShard; t.Hang = HangKind.PoisonVial;
                    t.Trunk = C(38, 34, 48); t.Trunk2 = C(76, 232, 138); t.Wood = C(48, 42, 62); t.LeafA = C(72, 228, 134); t.LeafB = C(214, 44, 78);
                    t.ClothA = C(178, 28, 56); t.ClothB = C(64, 214, 126); t.Glow = C(96, 248, 156); t.Metal = C(196, 204, 220); t.Rim = C(92, 108, 104); t.RimOrnate = C(92, 232, 148); t.BranchGloss = 0.9f; t.LeafGloss = 0.92f; t.LeafGlow = 0.6f; break;

                case JobLine.Rogue:
                    t.LifeColor = new Color(1f, 0.74f, 0.36f, 0.88f); t.Breath = 0.04f; t.BreathPeriod = 7f;
                    t.LifeNote = "the three-pronged grappling hook, coiled iron chain, phantom masquerade mask and stolen chalice gleam with gold and crimson";
                    t.RiseNote = "the three-pronged grappling hook launches skyward trailing coiled chain links and latches at the crown with a phantom mask flash";
                    t.RiseSounds = new[] { "synth:rogue", "effect\\ef_hiding.wav" };
                    t.Branch = BranchKind.Thorn; t.Leaf = LeafKind.Spike; t.Hang = HangKind.Wares;
                    t.Trunk = C(74, 52, 48); t.Trunk2 = C(214, 62, 58); t.Wood = C(82, 58, 52); t.LeafA = C(196, 62, 58); t.LeafB = C(232, 184, 78);
                    t.ClothA = C(172, 38, 44); t.ClothB = C(236, 190, 84); t.Glow = C(255, 196, 112); t.Metal = C(168, 174, 186); t.Rim = C(132, 108, 98); t.RimOrnate = C(228, 178, 78); break;

                // ==================== MERCHANT CHAIN ====================
                case JobLine.Merchant:
                    t.LifeColor = new Color(1f, 0.86f, 0.42f, 0.86f); t.Breath = 0.04f; t.BreathPeriod = 8f;
                    t.LifeNote = "the wooden kafra cart wheel, overflowing Zeny treasure chest and golden balance scales gleam with warm brass and gold";
                    t.RiseNote = "the cart wheel bounces in, popping open the treasure chest with a fountain of glittering Zeny coins as the golden balance scales settle";
                    t.RiseSounds = new[] { "synth:merchant", "se_ding2.wav" };
                    t.Branch = BranchKind.Pipe; t.Leaf = LeafKind.Rivet; t.Hang = HangKind.Wares;
                    t.Trunk = C(138, 94, 56); t.Trunk2 = C(228, 182, 74); t.Wood = C(178, 132, 68); t.LeafA = C(232, 188, 82); t.LeafB = C(164, 118, 64);
                    t.ClothA = C(156, 98, 52); t.ClothB = C(255, 206, 92); t.Glow = C(255, 214, 116); t.BranchGloss = 0.78f; t.LeafGloss = 0.82f; t.Rim = C(174, 138, 76); t.Metal = C(142, 146, 156); break;

                case JobLine.Blacksmith:
                    t.LifeColor = new Color(1f, 0.56f, 0.22f, 0.94f); t.Breath = 0.07f; t.BreathPeriod = 5.5f;
                    t.LifeNote = "the horned iron anvil, giant two-handed runed war-hammer, clockwork brass gears and magma forge hearth glow white-hot";
                    t.RiseNote = "the giant war-hammer slams down onto the horned iron anvil with a blinding forge flash, showering molten orange-gold sparks";
                    t.RiseSounds = new[] { "synth:blacksmith", "_hit_mace.wav", "_hit_axe.wav" };
                    t.Branch = BranchKind.Pipe; t.Leaf = LeafKind.Rivet; t.Hang = HangKind.Wares;
                    t.Trunk = C(88, 92, 104); t.Trunk2 = C(255, 128, 44); t.Wood = C(196, 144, 68); t.LeafA = C(224, 168, 78); t.LeafB = C(255, 116, 42);
                    t.ClothA = C(168, 64, 36); t.ClothB = C(255, 164, 64); t.Glow = C(255, 168, 78); t.BranchGloss = 0.88f; t.LeafGloss = 0.85f; t.Rim = C(164, 122, 72); t.RimOrnate = C(244, 162, 68); t.Metal = C(112, 118, 132); break;

                case JobLine.Alchemist:
                    t.LifeColor = new Color(0.46f, 1f, 0.72f, 0.92f); t.Breath = 0.07f; t.BreathPeriod = 5.8f;
                    t.LifeNote = "the boiling glass alembic flask, double-helix potion tubes and homunculus philosopher-stone core effervesce with luminous elixir";
                    t.RiseNote = "emerald and ruby potion bubbles effervesce rapidly up the double-helix glass tubes and burst in an alchemical transmutation star";
                    t.RiseSounds = new[] { "synth:alchemist", "_heal_effect.wav" };
                    t.Branch = BranchKind.GlassHelix; t.Leaf = LeafKind.AlchDrop; t.Hang = HangKind.PotionFlask;
                    t.Trunk = C(168, 224, 216); t.Trunk2 = C(72, 238, 154); t.Wood = C(186, 148, 84); t.LeafA = C(78, 238, 156); t.LeafB = C(244, 78, 112);
                    t.ClothA = C(238, 74, 108); t.ClothB = C(78, 236, 164); t.Glow = C(108, 252, 184); t.BranchGloss = 0.95f; t.LeafGloss = 0.94f; t.LeafGlow = 0.6f; t.Rim = C(132, 176, 158); t.RimOrnate = C(112, 238, 176); break;

                // ==================== EXPANDED & NOVICE LINES ====================
                case JobLine.Gunslinger:
                    t.LifeColor = new Color(1f, 0.86f, 0.5f, 0.85f);
                    t.LifeNote = "the six-shooter revolver cylinder, lever-action rifle, brass cartridges and winged sheriff star polish to a bright shine";
                    t.RiseNote = "the revolver cylinder spins and locks as the rifle twirls up and fires a blazing muzzle flash";
                    t.RiseSounds = new[] { "synth:gunslinger", "weapon:18", "weapon:17", "_hit_pistol.wav" };
                    t.Branch = BranchKind.Rod; t.Leaf = LeafKind.Bullet; t.Hang = HangKind.Pennant;
                    t.Trunk = C(84, 88, 98); t.Trunk2 = C(124, 80, 50); t.Wood = C(96, 100, 112); t.LeafA = C(214, 168, 84); t.LeafB = C(178, 126, 62);
                    t.ClothA = C(132, 34, 36); t.ClothB = C(224, 178, 82); t.Glow = C(255, 196, 122); t.BranchGloss = 0.85f; t.LeafGloss = 0.88f; t.Rim = C(124, 128, 138); break;

                case JobLine.Ninja:
                    t.LifeColor = new Color(1f, 0.34f, 0.24f, 0.85f);
                    t.LifeNote = "the crossed katanas, giant Fuuma shuriken, crimson blood-moon disc and autumn maple leaves blaze deep scarlet";
                    t.RiseNote = "a puff of white smoke parts to reveal the crossed katanas and spinning Fuuma shuriken before a crimson moon";
                    t.RiseSounds = new[] { "synth:ninja", "effect\\ef_hiding.wav", "_attack_dagger.wav" };
                    t.Branch = BranchKind.Bamboo; t.Leaf = LeafKind.Maple; t.Hang = HangKind.Talisman;
                    t.Trunk = C(36, 34, 42); t.Trunk2 = C(184, 36, 42); t.Wood = C(124, 154, 82); t.LeafA = C(198, 52, 46); t.LeafB = C(232, 124, 52);
                    t.ClothA = C(240, 234, 222); t.ClothB = C(184, 36, 42); t.Glow = C(255, 152, 132); t.Rim = C(114, 114, 120); t.RimOrnate = C(204, 62, 56); break;

                case JobLine.Taekwon:
                case JobLine.StarGladiator:
                case JobLine.SoulLinker:
                    t.LifeColor = new Color(1f, 0.93f, 0.6f, 0.88f); t.Breath = line != JobLine.Taekwon ? 0.06f : 0f; t.BreathPeriod = 6f;
                    t.LifeNote = line == JobLine.StarGladiator ? "the celestial sun, crescent moon and constellation astrolabe rings blaze with starlight"
                               : line == JobLine.SoulLinker ? "the twin linked spirit totems and ethereal soul-lanterns pulse with ancestral light"
                               : "the makiwara striking post, ranked belt and star charms shine brighter";
                    t.RiseNote = line == JobLine.StarGladiator ? "the crescent moon and blazing sun align in a spinning star-chart astrolabe"
                               : line == JobLine.SoulLinker ? "twin spirit totems rise linked by glowing cyan-violet soul threads"
                               : "the post drops and lands with a martial stomp ring as the stars pop out";
                    t.RiseSounds = new[] { line == JobLine.StarGladiator ? "synth:stargladiator" : line == JobLine.SoulLinker ? "synth:soullinker" : "synth:taekwon", "_hit_fist1.wav" };
                    t.Branch = BranchKind.Rod; t.Leaf = LeafKind.Star; t.Hang = HangKind.Belt;
                    t.Trunk = line == JobLine.SoulLinker ? C(92, 74, 138) : C(172, 132, 92);
                    t.Trunk2 = line == JobLine.StarGladiator ? C(248, 204, 78) : line == JobLine.SoulLinker ? C(124, 224, 248) : C(240, 240, 236);
                    t.Wood = C(48, 66, 128); t.LeafA = C(244, 214, 112); t.LeafB = C(148, 224, 255);
                    t.ClothA = C(240, 240, 236); t.ClothB = C(220, 176, 82); t.Glow = line == JobLine.SoulLinker ? C(144, 236, 255) : C(188, 208, 255);
                    t.BranchGloss = 0.75f; t.LeafGloss = 0.85f; t.LeafGlow = 0.4f; t.Rim = C(124, 134, 174); break;

                case JobLine.Summoner:
                    t.LifeColor = new Color(0.72f, 1f, 0.76f, 0.82f); t.Breath = 0.05f; t.BreathPeriod = 8f;
                    t.LifeNote = "the blooming catnip stalk, golden cat-bell collar, jumping koi fish and spirit orb glow with nature light";
                    t.RiseNote = "the catnip stalk wriggles up with a jingle of its golden bell as flowers and koi fish pop open";
                    t.RiseSounds = new[] { "synth:summoner", "se_ding1.wav", "se_ding2.wav" };
                    t.Branch = BranchKind.Twig; t.Leaf = LeafKind.Round; t.Hang = HangKind.Fish;
                    t.Trunk = C(112, 164, 86); t.Trunk2 = C(230, 194, 152); t.Wood = C(122, 168, 92); t.LeafA = C(124, 188, 98); t.LeafB = C(174, 214, 112);
                    t.ClothA = C(122, 174, 224); t.ClothB = C(246, 162, 184); t.Glow = C(184, 252, 214); t.Rim = C(152, 172, 132); t.RimOrnate = C(242, 172, 192); break;

                default: // Novice / SuperNovice
                    t.LifeColor = new Color(0.76f, 1f, 0.56f, 0.8f);
                    t.LifeNote = "the sapling's leaves open from buds and turn a fresh sunlit green";
                    t.RiseNote = "the sapling sprouts out of the earth with a cheerful rustle and nature chime";
                    t.RiseSounds = new[] { line == JobLine.SuperNovice ? "synth:supernovice" : "synth:novice", "se_ding2.wav" };
                    t.Branch = BranchKind.Twig; t.Leaf = LeafKind.Round; t.Hang = HangKind.Ribbon;
                    t.Trunk = C(152, 114, 78); t.Trunk2 = C(198, 162, 112); t.Wood = C(152, 114, 78); t.LeafA = C(128, 188, 92); t.LeafB = C(178, 216, 112);
                    t.ClothA = C(246, 172, 192); t.ClothB = C(152, 202, 246); t.Glow = C(255, 242, 192); t.Rim = C(162, 152, 132); break;
            }
            _byLine[line] = t;
            return t;
        }

        public static TreeFamily FamilyOfLine(JobLine l)
        {
            switch (l)
            {
                case JobLine.Swordsman: case JobLine.Knight: case JobLine.Crusader: return TreeFamily.Swordsman;
                case JobLine.Mage: case JobLine.Wizard: case JobLine.Sage: return TreeFamily.Mage;
                case JobLine.Archer: case JobLine.Hunter: case JobLine.Bard: return TreeFamily.Archer;
                case JobLine.Acolyte: case JobLine.Priest: case JobLine.Monk: return TreeFamily.Acolyte;
                case JobLine.Merchant: case JobLine.Blacksmith: case JobLine.Alchemist: return TreeFamily.Merchant;
                case JobLine.Thief: case JobLine.Assassin: case JobLine.Rogue: return TreeFamily.Thief;
                case JobLine.Gunslinger: return TreeFamily.Gunslinger;
                case JobLine.Ninja: return TreeFamily.Ninja;
                case JobLine.Taekwon: case JobLine.StarGladiator: case JobLine.SoulLinker: return TreeFamily.Taekwon;
                case JobLine.Summoner: return TreeFamily.Summoner;
                default: return TreeFamily.Novice;
            }
        }

        /// <summary>A job's family: the first job its line grows from (Knight, Lord Knight, Rune Knight, Dragon Knight: Swordsman).</summary>
        public static TreeFamily FamilyOf(int job)
        {
            foreach (int j in SkillTreeDb.Chain(job))
                switch (j)
                {
                    case 1: return TreeFamily.Swordsman; case 2: return TreeFamily.Mage; case 3: return TreeFamily.Archer; case 4: return TreeFamily.Acolyte;
                    case 5: return TreeFamily.Merchant; case 6: return TreeFamily.Thief; case 23: return TreeFamily.Novice;
                    case 24: return TreeFamily.Gunslinger; case 25: return TreeFamily.Ninja; case 4046: return TreeFamily.Taekwon; case 4218: return TreeFamily.Summoner;
                }
            return TreeFamily.Novice;
        }
    }

    /// <summary>
    /// Custom procedural sound synthesizer for the 3D skill trees ('synth:*').
    /// Generates distinct, bespoke 44.1 kHz AudioClips in code for every class tree so no tree reuses generic in-game sound files.
    /// </summary>
    public static class TreeSynthAudio
    {
        private const int Rate = 44100;

        private static float HashNoise(int seed)
        {
            uint n = (uint)seed;
            n = (n << 13) ^ n;
            uint m = (n * (n * n * 15731u + 789221u) + 1376312589u) & 0x7fffffffu;
            return (m / 1073741824f) - 1f;
        }

        private static float Env(float t, float start, float attack, float decay)
        {
            float dt = t - start;
            if (dt < 0f) return 0f;
            float a = attack > 1e-4f ? Mathf.Clamp01(dt / attack) : 1f;
            return a * Mathf.Exp(-dt / Mathf.Max(0.005f, decay));
        }

        private static float Bell(float t, float start, float freq, float decay, float amp = 1f)
        {
            float dt = t - start;
            if (dt < 0f) return 0f;
            float e = Env(t, start, 0.004f, decay);
            return amp * e * (
                0.65f * Mathf.Sin(2f * Mathf.PI * freq * dt) +
                0.24f * Mathf.Sin(2f * Mathf.PI * freq * 2.0f * dt) * Mathf.Exp(-dt * 4f) +
                0.11f * Mathf.Sin(2f * Mathf.PI * freq * 3.01f * dt) * Mathf.Exp(-dt * 8f)
            );
        }

        public static AudioClip Make(string name)
        {
            string key = (name ?? "").StartsWith("synth:", StringComparison.OrdinalIgnoreCase)
                ? name.Substring(6).Trim().ToLowerInvariant()
                : (name ?? "").Trim().ToLowerInvariant();
            float duration = 0.95f;
            int n = Mathf.RoundToInt(Rate * duration);
            var s = new float[n];

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float nz = HashNoise(i * 17 + 31);
                float v = 0f;

                switch (key)
                {
                    case "swordsman":
                    {
                        // Downward blade whoosh -> heavy stone thud + forged steel ring
                        if (t < 0.22f)
                        {
                            float f = Mathf.Lerp(900f, 260f, t / 0.22f);
                            v += 0.25f * Env(t, 0f, 0.03f, 0.12f) * (nz * 0.5f + Mathf.Sin(2f * Mathf.PI * f * t) * 0.5f);
                        }
                        v += 0.55f * Env(t, 0.14f, 0.002f, 0.09f) * Mathf.Sin(2f * Mathf.PI * 92f * Mathf.Max(0f, t - 0.14f));
                        v += 0.32f * Env(t, 0.14f, 0.002f, 0.04f) * nz;
                        v += Bell(t, 0.14f, 440f, 0.42f, 0.34f) + Bell(t, 0.14f, 659.25f, 0.35f, 0.24f) + Bell(t, 0.14f, 1108f, 0.25f, 0.16f);
                        break;
                    }
                    case "knight":
                    {
                        // Regal chivalric brass fifth + heavy shield lock & lance ring
                        float Brass(float st, float f, float d, float a)
                        {
                            float dt = t - st; if (dt < 0f) return 0f;
                            float e = Env(t, st, 0.025f, d);
                            return a * e * (0.5f * Mathf.Sin(2f * Mathf.PI * f * dt) + 0.35f * Mathf.Sin(4f * Mathf.PI * f * dt) + 0.15f * Mathf.Sin(6f * Mathf.PI * f * dt));
                        }
                        v += Brass(0.02f, 196f, 0.48f, 0.32f) + Brass(0.08f, 293.66f, 0.52f, 0.34f) + Brass(0.16f, 392f, 0.58f, 0.38f);
                        v += Bell(t, 0.18f, 587.33f, 0.45f, 0.28f) + Bell(t, 0.18f, 880f, 0.38f, 0.2f);
                        v += 0.3f * Env(t, 0.16f, 0.002f, 0.06f) * Mathf.Sin(2f * Mathf.PI * 110f * Mathf.Max(0f, t - 0.16f));
                        break;
                    }
                    case "crusader":
                    {
                        // Deep cathedral bell strike + shimmering golden choir chord
                        v += Bell(t, 0.02f, 261.63f, 0.65f, 0.38f) + Bell(t, 0.02f, 392f, 0.6f, 0.3f) + Bell(t, 0.06f, 523.25f, 0.58f, 0.32f);
                        v += Bell(t, 0.12f, 659.25f, 0.55f, 0.26f) + Bell(t, 0.18f, 1046.5f, 0.5f, 0.22f);
                        break;
                    }
                    case "mage":
                    {
                        // Arcane crystalline arpeggio + elemental crackle
                        float[] notes = { 523.25f, 659.25f, 783.99f, 987.77f, 1318.5f };
                        for (int k = 0; k < notes.Length; k++) v += Bell(t, k * 0.065f, notes[k], 0.36f, 0.26f);
                        v += 0.12f * Env(t, 0.05f, 0.04f, 0.25f) * nz * Mathf.Sin(2f * Mathf.PI * 1600f * t);
                        break;
                    }
                    case "wizard":
                    {
                        // Deep astral sub-bass swell + spinning celestial orrery harmonic chord + supernova shimmer
                        v += 0.38f * Env(t, 0f, 0.12f, 0.55f) * Mathf.Sin(2f * Mathf.PI * 110f * t);
                        float[] chord = { 220f, 329.63f, 440f, 554.37f, 659.25f, 880f, 1318.5f };
                        for (int k = 0; k < chord.Length; k++) v += Bell(t, 0.04f + k * 0.045f, chord[k], 0.48f, 0.2f);
                        break;
                    }
                    case "sage":
                    {
                        // Crisp fluttering grimoire pages + mystical ancient modal bell triad
                        for (int p = 0; p < 4; p++) v += 0.18f * Env(t, p * 0.045f, 0.004f, 0.025f) * nz;
                        v += Bell(t, 0.14f, 440f, 0.45f, 0.3f) + Bell(t, 0.20f, 523.25f, 0.45f, 0.28f) + Bell(t, 0.26f, 659.25f, 0.5f, 0.3f) + Bell(t, 0.32f, 880f, 0.48f, 0.24f);
                        break;
                    }
                    case "archer":
                    {
                        // Taut bowstring twang + high-velocity arrow air-slice whoosh
                        float twang = Env(t, 0.01f, 0.002f, 0.24f);
                        float fStr = 148f + 42f * Mathf.Exp(-t * 18f);
                        v += 0.45f * twang * (Mathf.Sin(2f * Mathf.PI * fStr * t) + 0.5f * Mathf.Sin(4f * Mathf.PI * fStr * t) + 0.25f * Mathf.Sin(6f * Mathf.PI * fStr * t));
                        // Arrow whoosh cutting horizontally across the air
                        float whoosh = Env(t, 0.03f, 0.04f, 0.18f);
                        float sweep = Mathf.Lerp(1400f, 420f, Mathf.Clamp01((t - 0.03f) / 0.28f));
                        v += 0.38f * whoosh * (nz * 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * sweep * t));
                        break;
                    }
                    case "hunter":
                    {
                        // Double wing-flap whoosh + majestic piercing Falcon/Hawk cry!
                        for (int w = 0; w < 2; w++)
                        {
                            float wt = 0.02f + w * 0.14f;
                            v += 0.26f * Env(t, wt, 0.025f, 0.07f) * (nz * 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 220f * t));
                        }
                        // Synthesized raptor/falcon cry ('kree-ee-ar!') from t = 0.12 to 0.72
                        if (t >= 0.12f && t < 0.75f)
                        {
                            float dt = t - 0.12f;
                            float u = dt / 0.58f;
                            float cryEnv = Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI) * Mathf.Exp(-dt * 2.1f);
                            float f0 = (u < 0.22f ? Mathf.Lerp(2350f, 3250f, u / 0.22f) : Mathf.Lerp(3250f, 1980f, (u - 0.22f) / 0.78f))
                                       + 65f * Mathf.Sin(2f * Mathf.PI * 28f * dt);
                            float phase = 2f * Mathf.PI * f0 * dt + 0.85f * Mathf.Sin(2f * Mathf.PI * 190f * dt);
                            v += 0.48f * cryEnv * (0.68f * Mathf.Sin(phase) + 0.22f * Mathf.Sin(phase * 2f) + 0.1f * nz);
                        }
                        break;
                    }
                    case "bard":
                    {
                        // Lush 6-string golden concert lyre / harp glissando
                        float[] harp = { 392f, 493.88f, 587.33f, 783.99f, 987.77f, 1174.66f, 1567.98f };
                        for (int k = 0; k < harp.Length; k++) v += Bell(t, k * 0.055f, harp[k], 0.45f, 0.24f);
                        break;
                    }
                    case "acolyte":
                    {
                        // Warm sanctuary bronze hand-bell chime
                        v += Bell(t, 0.02f, 587.33f, 0.55f, 0.36f) + Bell(t, 0.08f, 880f, 0.52f, 0.32f) + Bell(t, 0.14f, 1174.66f, 0.48f, 0.24f);
                        break;
                    }
                    case "priest":
                    {
                        // Cathedral pipe-organ & seraphim choir chord + high celesta sparkles
                        float[] organ = { 349.23f, 440f, 523.25f, 698.46f, 880f, 1046.5f };
                        for (int k = 0; k < organ.Length; k++)
                        {
                            float e = Env(t, 0.02f, 0.08f, 0.55f);
                            v += 0.16f * e * (Mathf.Sin(2f * Mathf.PI * organ[k] * t) + 0.35f * Mathf.Sin(4f * Mathf.PI * organ[k] * t));
                        }
                        v += Bell(t, 0.18f, 1396.9f, 0.38f, 0.2f) + Bell(t, 0.26f, 1760f, 0.36f, 0.2f) + Bell(t, 0.34f, 2093f, 0.35f, 0.18f);
                        break;
                    }
                    case "monk":
                    {
                        // Deep martial taiko drum stomp + 5 rapid ascending Zen spirit-sphere harmonic hums
                        float dEnv = Env(t, 0.01f, 0.002f, 0.18f);
                        float dFreq = 78f + 65f * Mathf.Exp(-t * 28f);
                        v += 0.58f * dEnv * Mathf.Sin(2f * Mathf.PI * dFreq * t);
                        float[] orbs = { 330f, 392f, 440f, 523.25f, 659.25f };
                        for (int k = 0; k < orbs.Length; k++) v += Bell(t, 0.10f + k * 0.065f, orbs[k], 0.28f, 0.22f);
                        break;
                    }
                    case "thief":
                    {
                        // Smoke-puff hiss + twin rapid stiletto blade cross-slashes
                        v += 0.24f * Env(t, 0f, 0.01f, 0.09f) * nz;
                        for (int sl = 0; sl < 2; sl++)
                        {
                            float st = 0.08f + sl * 0.11f;
                            float dt = t - st;
                            if (dt >= 0f)
                            {
                                float f = Mathf.Lerp(1800f, 620f, Mathf.Clamp01(dt / 0.09f));
                                v += 0.32f * Env(t, st, 0.002f, 0.055f) * (0.4f * nz + 0.6f * Mathf.Sin(2f * Mathf.PI * f * dt));
                                v += Bell(t, st + 0.01f, 1320f + sl * 240f, 0.16f, 0.22f);
                            }
                        }
                        break;
                    }
                    case "assassin":
                    {
                        // Razor-sharp twin Jur Katar scissor-snap + venomous acid sizzle + shadow sub-drop
                        v += 0.42f * Env(t, 0.02f, 0.003f, 0.22f) * Mathf.Sin(2f * Mathf.PI * (120f * Mathf.Exp(-t * 6f) + 55f) * t);
                        v += Bell(t, 0.04f, 940f, 0.18f, 0.34f) + Bell(t, 0.04f, 1480f, 0.15f, 0.3f) + Bell(t, 0.11f, 1860f, 0.22f, 0.32f);
                        // Venom acid sizzle
                        if (t >= 0.12f)
                        {
                            float dt = t - 0.12f;
                            v += 0.22f * Env(t, 0.12f, 0.02f, 0.28f) * nz * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 38f * dt));
                        }
                        break;
                    }
                    case "rogue":
                    {
                        // Rattling iron chain-whip launch + sharp 3-pronged grapple latch clink
                        for (int c = 0; c < 5; c++) v += Bell(t, c * 0.038f, 1600f + (c % 2) * 380f, 0.05f, 0.2f);
                        v += Bell(t, 0.22f, 740f, 0.32f, 0.38f) + Bell(t, 0.22f, 1180f, 0.28f, 0.3f);
                        break;
                    }
                    case "merchant":
                    {
                        // Wooden cart-wheel thud + cascading shower of 8 clinking golden Zeny coins
                        v += 0.42f * Env(t, 0.01f, 0.003f, 0.09f) * Mathf.Sin(2f * Mathf.PI * 105f * t);
                        float[] coins = { 2093f, 2349f, 2637f, 2217f, 2793f, 2489f, 2960f, 3136f };
                        for (int c = 0; c < coins.Length; c++) v += Bell(t, 0.08f + c * 0.048f, coins[c], 0.18f, 0.22f);
                        break;
                    }
                    case "blacksmith":
                    {
                        // Heavy blacksmith war-hammer striking horned iron anvil + sizzling forge embers
                        v += 0.45f * Env(t, 0.02f, 0.001f, 0.05f) * nz;
                        // Inharmonic iron anvil ring
                        v += Bell(t, 0.02f, 820f, 0.48f, 0.36f) + Bell(t, 0.02f, 1290f, 0.42f, 0.3f) + Bell(t, 0.02f, 1940f, 0.32f, 0.24f);
                        // Sizzling embers
                        v += 0.16f * Env(t, 0.08f, 0.03f, 0.35f) * nz;
                        break;
                    }
                    case "alchemist":
                    {
                        // Bubbling glass alembic potion fizz + crystalline transmutation chord
                        for (int b = 0; b < 7; b++)
                        {
                            float bt = b * 0.045f;
                            float dt = t - bt;
                            if (dt >= 0f && dt < 0.07f)
                            {
                                float bf = Mathf.Lerp(380f, 980f, dt / 0.07f) + (b % 3) * 110f;
                                v += 0.26f * Env(t, bt, 0.005f, 0.035f) * Mathf.Sin(2f * Mathf.PI * bf * dt);
                            }
                        }
                        v += Bell(t, 0.26f, 659.25f, 0.45f, 0.28f) + Bell(t, 0.30f, 830.61f, 0.45f, 0.26f) + Bell(t, 0.34f, 987.77f, 0.48f, 0.28f) + Bell(t, 0.38f, 1318.5f, 0.48f, 0.24f);
                        break;
                    }
                    case "gunslinger":
                    {
                        // Revolver cylinder spin clicks + punchy shot & metallic ricochet whine
                        for (int c = 0; c < 4; c++) v += Bell(t, c * 0.042f, 1900f, 0.025f, 0.22f);
                        v += 0.55f * Env(t, 0.20f, 0.001f, 0.08f) * (0.6f * nz + 0.4f * Mathf.Sin(2f * Mathf.PI * 130f * Mathf.Max(0f, t - 0.20f)));
                        if (t >= 0.24f)
                        {
                            float dt = t - 0.24f;
                            float rf = Mathf.Lerp(2400f, 780f, Mathf.Clamp01(dt / 0.38f));
                            v += 0.28f * Env(t, 0.24f, 0.005f, 0.22f) * Mathf.Sin(2f * Mathf.PI * rf * dt);
                        }
                        break;
                    }
                    case "ninja":
                    {
                        // Katana unsheathe metallic shing + spinning Fuuma shuriken wind-slice
                        if (t >= 0.02f)
                        {
                            float dt = t - 0.02f;
                            float sf = Mathf.Lerp(1100f, 2600f, Mathf.Clamp01(dt / 0.25f));
                            v += 0.28f * Env(t, 0.02f, 0.01f, 0.24f) * (0.7f * Mathf.Sin(2f * Mathf.PI * sf * dt) + 0.3f * nz);
                        }
                        v += Bell(t, 0.04f, 1480f, 0.35f, 0.28f) + Bell(t, 0.14f, 2217f, 0.3f, 0.22f);
                        break;
                    }
                    case "taekwon":
                    case "stargladiator":
                    case "soullinker":
                    {
                        // Martial makiwara strike snap + celestial star chimes
                        v += 0.48f * Env(t, 0.01f, 0.001f, 0.07f) * (0.4f * nz + 0.6f * Mathf.Sin(2f * Mathf.PI * 125f * t));
                        float[] stars = { 587.33f, 783.99f, 880f, 1174.66f, 1567.98f };
                        for (int k = 0; k < stars.Length; k++) v += Bell(t, 0.08f + k * 0.055f, stars[k], 0.35f, 0.22f);
                        break;
                    }
                    case "summoner":
                    {
                        // Playful cat-collar jingle bell + bouncy nature harp
                        v += Bell(t, 0.02f, 2637f, 0.16f, 0.26f) + Bell(t, 0.09f, 2793f, 0.18f, 0.26f);
                        float[] notes = { 392f, 523.25f, 659.25f, 783.99f, 1046.5f };
                        for (int k = 0; k < notes.Length; k++) v += Bell(t, 0.04f + k * 0.055f, notes[k], 0.32f, 0.24f);
                        break;
                    }
                    default: // novice / supernovice
                    {
                        // Gentle earth sprout rustle + cheerful 3-note wooden marimba/flute chime
                        v += 0.18f * Env(t, 0f, 0.02f, 0.12f) * nz;
                        v += Bell(t, 0.04f, 523.25f, 0.32f, 0.32f) + Bell(t, 0.12f, 659.25f, 0.34f, 0.32f) + Bell(t, 0.20f, 783.99f, 0.42f, 0.36f);
                        if (key == "supernovice") v += Bell(t, 0.28f, 1046.5f, 0.45f, 0.3f) + Bell(t, 0.34f, 1318.5f, 0.45f, 0.26f);
                        break;
                    }
                }

                s[i] = Mathf.Clamp(v * 0.85f, -0.98f, 0.98f);
            }

            var clip = AudioClip.Create("synth:" + key, n, 1, Rate, false);
            clip.SetData(s, 0);
            return clip;
        }
    }
}
