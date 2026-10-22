using System;
using System.Collections.Generic;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 22 (w22tree), the design's point 64 (2026-10-09 / 10): "Keeping the current default flat display as a fallback, require the
    /// skills tab to be much more present in the 3D environment. The tree should be displayed as an elaborate, ornate, but still
    /// pragmatic and clear skill tree in front of the player." The SKILL TREES: they take the flat Skills page's place (Settings →
    /// Controls → Menus "Skill tree: 3D / Flat", VR/Comfort.cs SkillTree3D; UI/WristMenu.Tree.cs opens them).
    ///   ONE TREE PER JOB (design note, 2026-10-11: "the tall stack does not look good ... you have multiple trees that you can flip through, each
    ///             one representing the skills from each job [1st, 2nd, 3rd, etc]"): the jobs of the character's line that have skills
    ///             of their own, each its own tree (<see cref="JobTree"/>). Skills the server gave outside the trees (a card's, a
    ///             wedding's): a small bare stake "Other", low in front beside the sapling, no tree of the ring either (they come
    ///             and go with what is worn: they must not move the jobs' trees).
    ///   THE NOVICE'S SAPLING (design note, 2026-10-11: "The novice tree should look the same no matter your following job choices, and it
    ///             should be simple and look like a simple budding sapling"; "once you have reached your first job, hide the novice
    ///             skill tree inside of a node at the very base of the first job's skill tree. Pressing it grows the bud back out of
    ///             the ground and shows you the skill tree. Pressing it again buries the bud and closes the novice skill treee."):
    ///             a Novice has the sapling alone, straight ahead. From the first job on it is NOT shown when the trees rise: at the
    ///             base of the FIRST job's tree, left of the trunk's foot, is a small node (a mound with a sprout, "Novice"). A press
    ///             GROWS the sapling out of the ground left of that tree (on its floor, <see cref="SproutGapDeg"/> degrees beside its
    ///             branch tips, at the trees' distance, turned to the player: clear of its medallions and of the table), in
    ///             <see cref="RiseShortSeconds"/> s with one sound; the plate, if it serves that tree, steps aside. A press again
    ///             BURIES it (<see cref="BurySeconds"/> s, silent). Buried every time the trees open (<see cref="SproutsKept"/>). It
    ///             is no tree of the ring: it changes neither the count nor the places of the jobs' trees and is not flipped to.
    ///             The stake "Other" (skills from outside the trees) is hidden the same way in a second node right of the trunk's foot.
    ///   THE TABLE (the game's world in front of the player): a tree's floor stands at least <see cref="Clearance"/> over the ground the
    ///             character stands on (ten cells of the map as it is drawn now, 0.10 to 0.24 m): a tree whose foot would reach lower
    ///             is lifted (<see cref="JobTree.Lift"/>), so no foot and no medallion stands in the table's houses.
    ///   A FIXED LOOK PER JOB (design note: "the appearance of a swordsman's tree is fixed, as is the appearance of a knight's"): how ornate a
    ///             tree is comes from ITS job's place in the line (UI/TreeThemes.cs TreeGrowth.OrnamentOf), never from the character's
    ///             current job; how ALIVE it looks from the job level in that job (TreeGrowth.Liveliness: colours and light only).
    ///   TWO LAYOUTS (Settings → Controls → Menus "Skill trees": VR/Comfort.cs SkillTreesAround):
    ///     Around you    (the default) the trees rise at the boards' distance, each turned to the player, in ONE ARC in front of the
    ///                   player (client wave 23, w23tree, design note 2026-10-11: "when I open the skill trees they are too far apart. They
    ///                   are also out of order"): in the line's own order from LEFT to RIGHT (the client data's inherit chain,
    ///                   Game/SkillTree.cs SkillTreeModel.Build: never a job's number): the first job leftmost, the current job
    ///                   rightmost; neighbours <see cref="ArcGapDeg"/> degrees of clear air apart between their widest rows, as wide as
    ///                   each tree really is; right of the last tree the plate (two trees or more); the whole arc, trees and plate,
    ///                   centred straight ahead (<see cref="ArcAzimuth"/>). Client wave 22 put them
    ///                   on a circle a quarter turn apart (two -45 / +45, three -90 / 0 / +90, four -135 / -45 / +45 / +135, five 72
    ///                   apart): the current job's tree stood beside or behind the player and, from four trees on, the newest tree
    ///                   stood next to the oldest behind the player's back. A grip on a tree pulled sideways turns the arc.
    ///     One at a time one tree straight ahead, the current job's first; the round buttons above it (back / forward, the job's name
    ///                   and a dot per tree between them) or a grip pulled sideways flip to the next with a short slide.
    ///   Nodes     each skill a round medallion with its icon (the player's own game data, as the flat page shows it), its level / max
    ///             on a plaque under it, a "+" when a point can raise it; thin glowing lines for the prerequisites on the same tree, a
    ///             ring round a needed skill on any tree while a skill is pointed at. The ring round a medallion is split into its
    ///             tags' colours (Game/SkillTags.cs).
    ///   Use       the laser or the look + trigger, or a hand's fingertip held on a medallion, SELECTS it: its card opens beside its
    ///             tree with what the flat page's detail shows and the same buttons (UI/WristMenu.Tree.cs TreeAct). Resting on a
    ///             medallion shows its preview (UI/SkillDiorama.cs). The plate (filters, points, plan) stands left of the tree the
    ///             player faces (or whose skill is selected), the card right of it (UI/SkillTree3D.Panels.cs).
    ///   The lock  client wave 23 (w23tree), design note 2026-10-11: a selected skill is LOCKED: its preview keeps playing on a slow loop
    ///             with no pointer on it (a play, a rest, a play: UI/SkillDiorama.cs Rest), at a place where it stands in front of no
    ///             panel (UI/SkillTree3D.Panels.cs PanelsTick). Another press locks another skill; the card's X, or a trigger pull
    ///             on nothing (<see cref="Unlock"/>: LokiGame.W22Tree.cs W23TreeEmptyPull), takes the lock off: the card closes, the
    ///             preview ends.
    ///   Opening   each tree RISES in its family's own way (UI/SkillTree3D.Intro.cs; UI/MenuAnim.cs "skills": its switch, no rise within
    ///             5 s of closing), all within 1.5 s; only the picture moves: every medallion takes the laser, the look and the hand at
    ///             its place from the first frame. After the rise a tree stands STILL and silent (design note: "It should also not have looping
    ///             distracting animations"); only a family's slow breath of light stays where it has one.
    /// Units: a tree's own frame is in MILLIMETRES (the root is scaled <see cref="MetresPerUnit"/>): UI/TreeMesh.cs. The root stands at the
    /// player's eyes (the boards' height below them); every tree is a child of it, turned to it.
    /// Drawing: per tree four meshes of its own (the tree, its glow and lines, the medallions, the icons) and the level texts on the
    /// root's canvas; no depth test, as every panel (Loki/UIOverlay).
    /// </summary>
    public sealed partial class SkillTree3D : MonoBehaviour
    {
        public const float MetresPerUnit = 0.001f;
        /// <summary>A pointer rests this long on a medallion before its preview shows; the preview stays this long after it left.</summary>
        public const float HoverRest = 0.25f, HoverLinger = 0.5f;
        /// <summary>A fingertip held this long on a medallion or a button presses it.</summary>
        public const float TipHold = 0.35f;
        /// <summary>How see-through a skill outside the chosen tags is (design note: "the others become much more transparent").</summary>
        public const float DimAlpha = 0.14f;
        /// <summary>Client wave 26 (w26skill), the design's answer 33 (2026-10-11): "Make the lines on a skill tree that designate which skills are
        /// prerequisites to the one you are mousing over only show up when you are doing that (mousing over the skill you want to unlock).
        /// If the skill is not yet unlocked, other skills go a bit transparent at that moment, and the lines become animated, dotted,
        /// moving lines connecting them." THE PREREQUISITE LINES are drawn ONLY for the skill a pointer rests on (the trees' hover: the
        /// laser or a fingertip, <see cref="HoverRest"/> / <see cref="HoverLinger"/>): its chain on its tree, green met, orange not.
        /// That skill NOT YET UNLOCKED (the game's own word: the card's "Unlocked" is a learnable skill; this one is LOCKED: prerequisites,
        /// tier or quest not met): every other skill but its prerequisites is drawn at <see cref="HoverFadeAlpha"/> (medallion, icon,
        /// level), and its lines are DOTTED and MOVE from each prerequisite toward it: dashes <see cref="DashMm"/> mm, gaps
        /// <see cref="GapMm"/> mm, <see cref="DashSpeed"/> mm/s (only the lines' mesh is made again each frame, only then). FLAGGED: the
        /// numbers; a selected skill with no pointer on it draws no line (its rings round the needed skills stay).</summary>
        public const float HoverFadeAlpha = 0.45f, DashMm = 9f, GapMm = 7f, DashSpeed = 30f;
        /// <summary>The trees' renderers' order: over the panels (10, UI/UiKit.cs), under the laser (30, VR/DioramaRig.Ui.cs).</summary>
        public const int SortingOrder = 11;
        public const int QueueTree = 3090, QueueGlow = 3092, QueueMedals = 3094, QueueIcons = 3096, QueueFx = 3097, QueueMarks = 3098;
        /// <summary>With several trees shown each has its own four queues, <see cref="QueueStep"/> apart from <see cref="QueueFirst"/>: the
        /// farthest tree from the eyes first (no depth is tested: a nearer tree is drawn over a farther one where one stands before the other).</summary>
        public const int QueueFirst = 3040, QueueStep = 8;
        /// <summary>The levels' letters: millimetres tall (1.4° from the trees' 0.65 m; the boards' smallest text is 1.3°), drawn at two pixels a millimetre.</summary>
        public const float LevelTextMm = 16f, TextScale = 0.5f;
        /// <summary>One at a time: a flip's slide (seconds), from how far to the side the next tree comes in (degrees), how far a grip
        /// is pulled sideways for one flip (mm at the tree's own size).</summary>
        public const float FlipSeconds = 0.3f, FlipFromDeg = 30f, FlipPullMm = 90f;
        /// <summary>Around you: the tree the plate and the card serve changes when the head has faced another tree this much nearer (degrees) for this long (s).</summary>
        public const float FaceMarginDeg = 8f, FaceRest = 0.25f;

        /// <summary>Around you (client wave 23, w23tree): the clear air between two neighbouring trees as the eyes see them, degrees
        /// (from one tree's widest row to the next tree's). THE ONE CONSTANT (the orchestrator's 6; the design gave no number: FLAGGED).</summary>
        public const float ArcGapDeg = 6f;
        /// <summary>Around you: where the tree at place <paramref name="ring"/> of the line (0: the first job's) stands, degrees right of
        /// straight ahead: the trees side by side in the line's order, each as wide as it is from the eyes, <see cref="ArcGapDeg"/>
        /// apart, the whole arc centred straight ahead (a Knight: about -24 / +29; a Lord Knight: about -40 / +13 / +53).</summary>
        public float ArcAzimuth(int ring) => ring >= 0 && ring < _arc.Count ? _arc[ring] : 0f;
        /// <summary>Half the angle the whole arc takes from the eyes (degrees): its ends stand this far left and right of straight ahead.
        /// With two trees or more the arc's last piece is THE PLATE (<see cref="PlateAtEnd"/>): trees and plate together are centred.</summary>
        public float ArcHalf { get; private set; }
        /// <summary>Around you with two trees or more: the plate has the arc's right end, right of the current job's tree (FLAGGED;
        /// UI/SkillTree3D.Panels.cs LayoutPanels): left of the tree faced it would stand over the tree before that one.</summary>
        public bool PlateAtEnd => Around && RingCount >= 2;
        private readonly List<float> _arc = new List<float>();
        private void LayArc()
        {
            _arc.Clear(); float total = 0f;
            for (int r = 0; r < RingCount; r++) { var t = RingTree(r); total += (t != null ? 2f * t.HalfAngle(_dist) : 0f) + (r > 0 ? ArcGapDeg : 0f); }
            if (PlateAtEnd && _plate != null) total += PanelGapDeg + 2f * Mathf.Atan((PlateWidth * 0.5f + UiKit.OutsetOf(_plate)) * Px / Mathf.Max(1f, _dist)) * Mathf.Rad2Deg; // (the plate, its frame and its gap to the last tree)
            ArcHalf = total * 0.5f; float at = -ArcHalf;
            for (int r = 0; r < RingCount; r++) { var t = RingTree(r); float half = t != null ? t.HalfAngle(_dist) : 0f; _arc.Add(at + half); at += 2f * half + ArcGapDeg; }
        }

        /// <summary>Whether a sapling grown out of its node is still out the next time the trees open (the design's words name no such
        /// memory: buried at every opening; FLAGGED; the one constant).</summary>
        public const bool SproutsKept = false;
        /// <summary>The node at the first job's base: its radius, how far beside the trunk's axis it sits (mm); a grown sapling's gap
        /// to that tree's branch tips (degrees); how long burying takes (s; growing: <see cref="RiseShortSeconds"/>).</summary>
        public const float BudR = 24f, BudAside = 66f, SproutGapDeg = 2f, BurySeconds = 0.3f;
        /// <summary>THE TABLE: a tree's floor is at least this many cells of the map (as it is drawn now) over the ground the character
        /// stands on, no less and no more than these metres.</summary>
        public const float TableClearCells = 10f, TableClearMin = 0.10f, TableClearMax = 0.24f;
        /// <summary>The clearance over the ground now (m), and the ground's height in the world when the trees opened (null: not known).</summary>
        public float Clearance { get; private set; }
        public float? GroundNow { get; private set; }

        // ---- what the game gives it (UI/WristMenu.Tree.cs)
        /// <summary>The tree's model now (the flat page's own: Game/SkillTree.cs SkillTreeModel.Build for the character).</summary>
        public Func<SkillTreeModel> Model;
        public Func<int> Job, JobLevel, SkillPoints, Sex;
        /// <summary>The character's job level is its job's highest (the next level needs no EXP): the current job's tree is fully alive.</summary>
        public Func<bool> JobLevelMax;
        /// <summary>The button the pointing hand's LASER is on now (null: none). design note: "Lasering or hand-hovering over each skill spawns a small
        /// ghostly apparition": the laser and the hands rest on a medallion; the look chooses with the trigger but shows no preview.</summary>
        public Func<UiButton> LaserButton;
        /// <summary>A hand's fingertip in the world (0 left, 1 right; null: none now).</summary>
        public Func<int, Vector3?> Tip;
        /// <summary>The flat page's own action on a skill: "up" (learn a level), "pin" (the quick-slot picker), "lv" (the cast level), "use".</summary>
        public Func<int, string, bool> Act;
        public Action CloseClicked, QuickBar, FlatPage, Memo;
        /// <summary>The planned points (the flat page's Plan: Game/SkillPlan.cs): mode on, a skill's planned level, the plan's cost, a
        /// press on a node while planning, the buttons.</summary>
        public Func<bool> PlanMode; public Func<int, int> Planned; public Func<int> PlanCost; public Action<int> PlanStage; public Action PlanToggle, PlanApply, PlanReset;
        /// <summary>The preview beside a medallion (UI/SkillDiorama.cs; null: none).</summary>
        public SkillDiorama Diorama;
        /// <summary>The height in the world of the ground the character stands on (the table's top in the Table view; null: not known),
        /// and how many metres a cell of the map is drawn now (LokiGame.W22Tree.cs).</summary>
        public Func<float?> GroundY; public Func<float> MetresPerCell;
        /// <summary>A sound of the player's own install by its name under data\wav (Audio/LokiAudio.cs): whether it exists, and play it once.</summary>
        public Func<string, bool> HasSound; public Action<string> PlaySound;

        /// <summary>One job's tree.</summary>
        public sealed class JobTree
        {
            public int Index, Job; public string Name = ""; public bool Other;
            /// <summary>The Novice's sapling beside a job's trees: outside the ring (low in front of the player); its place in the ring
            /// otherwise (0: the first job's).</summary>
            public bool Extra; public int Ring = -1;
            /// <summary>Its place among the extra trees (0: the first), and how many of them there are.</summary>
            public int ExtraAt, Extras;
            /// <summary>An extra tree (the sapling, the stake): grown out of its node now; being buried (seconds left); its node on the
            /// first job's tree (the button, its name, where it sits in that tree's frame).</summary>
            public bool Out, Burying; public float BuryT; public UiButton Bud; public Text BudText; public Vector3 BudAt;
            /// <summary>How far it is lifted so that its floor clears the game's table (mm at the trees' own size; 0: not).</summary>
            public float Lift;
            /// <summary>Its place in the drawing order now (0: the farthest from the eyes; -1: not set).</summary>
            public int Rank = -1;
            public TreeFamily Family; public JobLine Line; public TreeTheme Theme; public int Ornament;
            /// <summary>How alive it looks (0-1: UI/TreeThemes.cs TreeGrowth.LivelinessByPoints), and the target liveliness it animates toward.</summary>
            public float Life, TargetLife = -1f;
            public int SpentPoints, MaxBeautyPoints;
            public TreeShape Shape; public TreeArt.Counts Counts = new TreeArt.Counts();
            public List<SkillTreeModel.Cell> Cells = new List<SkillTreeModel.Cell>();
            public RectTransform Holder, Blocker; public BoxCollider BlockCol;
            public Material MatTree, MatGlow, MatMedals, MatIcons;
            public Mesh MeshTree, MeshGlow, MeshMedals, MeshIcons, MeshLines, MeshFx;
            public MeshRenderer RendFx;
            public readonly List<UiButton> Btns = new List<UiButton>(); public readonly List<Text> Texts = new List<Text>();
            public readonly List<float> Alpha = new List<float>(); public readonly Dictionary<int, Color> Tint = new Dictionary<int, Color>();
            public Text Title; public Texture2D Atlas; public readonly Dictionary<int, Rect> AtlasAt = new Dictionary<int, Rect>();
            /// <summary>Skills whose icon the game data has; triangles drawn (its meshes); the art's own; ornament pieces on the
            /// medallions' rims; prerequisite lines drawn now (client wave 26: only the pointed skill's chain), their dashes (dotted).</summary>
            public int IconsFound, Triangles, ArtTriangles, MedalPieces, LinesDrawn, DashesDrawn;
            /// <summary>Where it stands now (degrees right of straight ahead) and how solid it is drawn (a flip's slide).</summary>
            public float Azimuth, Fade = 1f;
            /// <summary>The rise (UI/SkillTree3D.Intro.cs): playing; seconds since this tree's own start (under 0: it waits its turn);
            /// its length; how long it waited for its turn; a flip's short one; how far it is (0-1); its sound was started.</summary>
            public bool Rising, RiseShort, SoundDone; public float RiseT, RiseLen, RiseDelay, Grown = 1f;
            public bool Active => Holder != null && Holder.gameObject.activeSelf;
            /// <summary>Half the angle it takes from the eyes (degrees) at a distance (mm).</summary>
            public float HalfAngle(float dist) => Shape != null ? Mathf.Atan(Shape.HalfWidth / Mathf.Max(1f, dist)) * Mathf.Rad2Deg : 0f;
        }

        public bool Shown => gameObject.activeSelf;
        public RectTransform Root { get; private set; }
        public IReadOnlyList<JobTree> Trees => _trees;
        /// <summary>The trees of the ring (every tree but the extra sapling).</summary>
        public int RingCount { get; private set; }
        public JobTree RingTree(int ring) { if (ring >= 0) foreach (var t in _trees) if (t.Ring == ring) return t; return null; }
        /// <summary>The tree the plate and the card serve (one at a time: the tree shown).</summary>
        public int Focus { get; private set; }
        public JobTree FocusTree => Focus >= 0 && Focus < _trees.Count ? _trees[Focus] : null;
        /// <summary>The layout now: the trees around the player (true) or one at a time.</summary>
        public bool Around
        {
            get
            {
                return VR.Comfort.SkillTreesAround;
            }
        }
        /// <summary>The trees' distance from the eyes in the root's units (mm at the trees' own size).</summary>
        public float Distance => _dist;
        /// <summary>Around you: how far the ring is turned by a pull (degrees).</summary>
        public float RingTurn => _ringTurn;
        /// <summary>How often the trees were built / the menu opened (smoke, log).</summary>
        public int Builds { get; private set; }
        public int Opens { get; private set; }
        /// <summary>A tree is rising now; flips done (one at a time), flips refused at an end.</summary>
        public bool Rising { get { foreach (var t in _trees) if (t.Rising) return true; return false; } }
        public int Flips { get; private set; }
        public int FlipsRefused { get; private set; }
        public bool Flipping => _flipFrom != null;
        /// <summary>The skill under a pointer now (0: none), by what ("laser", "hand"), and the selected one.</summary>
        public int Hover { get; private set; }
        public string HoverBy { get; private set; } = "";
        public int Selected { get; private set; }
        /// <summary>Triangles drawn now (every tree shown), and the mesh draw calls of one tree.</summary>
        public int Triangles { get { int n = 0; foreach (var t in _trees) if (t.Active) n += t.Triangles; return n; } }
        public const int MeshDrawCallsPerTree = 5;

        private Camera _head; private Progression _prog; private PlayerState _ps;
        private readonly List<JobTree> _trees = new List<JobTree>();
        private Material _matMarks, _matFx; private MeshRenderer _rHover, _rSelect;
        private readonly TreeMeshBuilder _bTree = new TreeMeshBuilder(), _bGlow = new TreeMeshBuilder(), _bMedals = new TreeMeshBuilder(), _bIcons = new TreeMeshBuilder(), _bLines = new TreeMeshBuilder(), _bFx = new TreeMeshBuilder();
        private SkillTreeModel _model; private string _signature = ""; private bool _dirty = true, _wasAround;
        private float _dist = 650f, _ringTurn, _pullAcc; private bool _pulled;
        private JobTree _flipFrom; private float _flipT; private int _flipDir;
        private int _faceNew = -1; private float _faceSince;
        private int _hoverNew; private float _hoverSince, _hoverLeft; private int _shownPreview;
        private readonly float[] _tipSince = { -1f, -1f }; private readonly Component[] _tipOn = new Component[2]; private readonly bool[] _tipFired = new bool[2];
        private readonly HashSet<string> _filters = new HashSet<string>();
        private Vector3? _nextPos; private Quaternion _nextRot; private Vector3 _nextScale; private float _nextDist;

        public static SkillTree3D Create(Transform parent, Camera head, Progression prog, PlayerState ps)
        {
            var rt = UiKit.Panel("SkillTree", parent, new Vector2(10, 10), MetresPerUnit, false, PanelKind.Menu);
            rt.GetComponent<Canvas>().sortingOrder = SortingOrder;
            var t = rt.gameObject.AddComponent<SkillTree3D>();
            t.Root = rt; t._head = head; t._prog = prog; t._ps = ps;
            t.Build();
            prog.Changed += () => { if (t != null) t._dirty = true; };
            ps.Changed += () => { if (t != null) t._dirty = true; };
            rt.gameObject.SetActive(false);
            return t;
        }

        private static Material Mat(string name, int queue, bool additive, bool texture)
        {
            var sh = Shader.Find("Loki/Tree");
            if (sh == null) { Debug.LogWarning("[Tree] Loki/Tree shader missing"); sh = Shader.Find("Sprites/Default"); }
            var m = new Material(sh) { name = name, renderQueue = queue };
            m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_Cull", (float)(additive ? UnityEngine.Rendering.CullMode.Off : UnityEngine.Rendering.CullMode.Back));
            m.SetFloat("_Grow", 1.2f);
            if (texture) m.EnableKeyword("LOKITREE_TEX");
            return m;
        }

        private static MeshRenderer Layer(string name, Transform parent, Material m, out Mesh mesh)
        {
            var go = new GameObject(name); go.layer = UiKit.PanelLayer; go.transform.SetParent(parent, false);
            mesh = new Mesh { name = "SkillTree:" + name }; mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.sortingOrder = SortingOrder;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return r;
        }

        private void Build()
        {
            _matMarks = Mat("LokiTreeMarks", QueueMarks, true, false); _matFx = Mat("LokiTreeFx", QueueFx, true, false);
            _rHover = Layer("hover", Root, _matMarks, out var hoverMesh); _rSelect = Layer("selected", Root, _matMarks, out var selMesh);
            // the hover ring (soft, wide) and the selected one (a bright ring with four ticks)
            var b = new TreeMeshBuilder();
            b.Pen(new Color32(255, 244, 200, 150), 0f, 1f); b.Arc(Vector3.zero, TreeLayout.NodeR + 2.5f, TreeLayout.NodeR + 7.5f, 0f, 360f, 32, 0f, Vector3.zero);
            b.Pen(new Color32(255, 244, 200, 40), 0f, 1f); b.Arc(Vector3.zero, TreeLayout.NodeR + 7.5f, TreeLayout.NodeR + 13f, 0f, 360f, 32, 0f, Vector3.zero);
            b.Fill(hoverMesh); b.Clear();
            b.Pen(new Color32(255, 214, 110, 235), 0f, 1f); b.Arc(Vector3.zero, TreeLayout.NodeR + 3.5f, TreeLayout.NodeR + 6f, 0f, 360f, 32, 0f, Vector3.zero);
            for (int i = 0; i < 4; i++) { float a = i * 90f; var d = new Vector3(Mathf.Sin(a * Mathf.Deg2Rad), Mathf.Cos(a * Mathf.Deg2Rad), 0f); b.Line(d * (TreeLayout.NodeR + 6f), d * (TreeLayout.NodeR + 14f), 3.4f, 0f, 0f); }
            b.Fill(selMesh);
            _rHover.gameObject.SetActive(false); _rSelect.gameObject.SetActive(false);
            BuildPanels();
        }

        // ------------------------------------------------------------------ open / close
        /// <summary>Shows the trees. When they were closed the root is put at the player once (at the eyes, lowered by the height the
        /// player left the boards at; turned the way the head looks, upright; the trees at the boards' distance and size: VR/Comfort.cs
        /// BoardDistance / BoardHeight / BoardScale); it then stays where it is. Each tree rises unless UI/MenuAnim.cs says no (its
        /// switch off, closed less than 5 s ago, a test run).</summary>
        public void Open()
        {
            if (gameObject.activeSelf) return;
            gameObject.SetActive(true); Opens++;
            if (_nextPos.HasValue) { transform.SetPositionAndRotation(_nextPos.Value, _nextRot); transform.localScale = _nextScale; _dist = _nextDist; _nextPos = null; }
            else
            {
                float s = Mathf.Clamp(VR.Comfort.BoardScale, 0.5f, 2f);
                transform.localScale = Vector3.one * (MetresPerUnit * s);
                _dist = Mathf.Max(300f, VR.Comfort.BoardDistance / (MetresPerUnit * s));
                if (_head != null)
                {
                    var f = _head.transform.forward; f.y = 0; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
                    transform.position = _head.transform.position + Vector3.up * VR.Comfort.BoardHeight;
                    transform.rotation = Quaternion.LookRotation(f, Vector3.up); // upright: a tree does not lean
                }
            }
            Selected = 0; Hover = 0; _shownPreview = 0; _hoverNew = 0; _ringTurn = 0f; _pullAcc = 0f; _pulled = false; _flipFrom = null; _faceNew = -1;
            Rebuild(true);
            if (!SproutsKept) foreach (var t in _trees) { t.Out = false; t.Burying = false; }
            Focus = CurrentTree; _wasAround = Around;
            Lifts(); LayoutTrees();
            bool play = MenuAnim.ShouldPlay(MenuAnim.Skills);
            StartRise(play);
            LayoutPanels(); RefreshPanels();
            var sb = new System.Text.StringBuilder();
            foreach (var t in _trees) sb.Append(sb.Length > 0 ? "; " : "").Append($"{t.Name} ({t.Family}, ornament {t.Ornament}, alive {t.Life:0.00}, {t.Shape.Nodes.Count} skills, {(t.Extra ? (t.Out ? "grown out of its node" : "buried in its node at the first job's base") : t.Azimuth.ToString("0") + "°" + (t.Lift > 0.5f ? $", lifted {t.Lift:0} mm over the table" : ""))})");
            Debug.Log($"[Tree] opened {(Around ? "around you" : "one at a time")}: {RingCount} tree(s){(_trees.Count > RingCount ? $" and {_trees.Count - RingCount} hidden in a node" : "")}: {sb}; {(play ? $"rising {RiseTotal:0.00} s" : "shown at once")}");
        }

        public void Close()
        {
            if (!gameObject.activeSelf) return;
            MenuAnim.Closed(MenuAnim.Skills);
            if (Diorama != null) Diorama.Hide();
            foreach (var t in _trees) EndRise(t);
            gameObject.SetActive(false);
            Debug.Log("[Tree] closed");
        }

        /// <summary>The one-menu-at-a-time rule (UI/MenuNav.cs): what the trees hold when the player leaves them for another menu, and back.</summary>
        public sealed class State { public Vector3 Pos, Scale; public Quaternion Rot; public float Dist, RingTurn; public int Focus, Selected; }
        public object Capture() => new State { Pos = transform.position, Rot = transform.rotation, Scale = transform.localScale, Dist = _dist, RingTurn = _ringTurn, Focus = Focus, Selected = Selected };
        public void Reopen(object state)
        {
            var st = state as State;
            if (st != null && !gameObject.activeSelf) { _nextPos = st.Pos; _nextRot = st.Rot; _nextScale = st.Scale; _nextDist = st.Dist; }
            Open();
            if (st == null) return;
            _ringTurn = st.RingTurn; Focus = Mathf.Clamp(st.Focus, 0, Mathf.Max(0, _trees.Count - 1)); if (FocusTree != null && FocusTree.Extra) Focus = CurrentTree;
            LayoutTrees(); LayoutPanels();
            if (st.Selected != 0 && TreeOf(st.Selected) != null) Select(st.Selected); else RefreshPanels();
        }

        // ------------------------------------------------------------------ the trees of the character's line
        private sealed class Spec { public int Job; public string Name = ""; public bool Other; public List<SkillTreeModel.Cell> Cells = new List<SkillTreeModel.Cell>(); }

        /// <summary>The trees a character has: for each tab of the flat page (the client data's own tiers: Game/SkillTree.cs
        /// SkillTreeModel.Build) one tree per job of the tab that has skills of its own (a Knight's and a Lord Knight's skills share a
        /// grid in the client data: two trees here; so do the Novice's and the first job's: the sapling and the first job's
        /// tree); a skill hangs on one tree only, the
        /// earliest; the skills the server gave outside the trees: the tree "Other".</summary>
        private List<Spec> Split(SkillTreeModel model)
        {
            var l = new List<Spec>(); if (model == null) return l;
            var seen = new HashSet<int>(); int sex = Sex != null ? Sex() : 1;
            foreach (var tab in model.Tabs)
            {
                if (tab.Etc) continue;
                foreach (int j in tab.Jobs)
                {
                    var mine = new List<SkillTreeModel.Cell>();
                    foreach (var c in tab.Cells.Values) if (c.Job == j && seen.Add(c.Skill)) mine.Add(c);
                    if (mine.Count == 0) continue;
                    l.Add(new Spec { Job = j, Name = SkillTreeDb.JobName(j, sex), Cells = mine });
                }
            }
            foreach (var tab in model.Tabs)
            {
                if (!tab.Etc) continue;
                var mine = new List<SkillTreeModel.Cell>();
                foreach (var c in tab.Cells.Values) if (seen.Add(c.Skill)) mine.Add(c);
                if (mine.Count > 0) l.Add(new Spec { Job = -1, Other = true, Name = Lang.T("~skilltab~Other"), Cells = mine });
            }
            return l;
        }

        /// <summary>The current job's tree: the last job's (the character's own job, or the job whose skills it learns now: a High
        /// Swordsman learns on the Swordsman's tree).</summary>
        public int CurrentTree
        {
            get
            {
                for (int i = _trees.Count - 1; i >= 0; i--) if (!_trees[i].Other && !_trees[i].Extra) return i;
                for (int i = _trees.Count - 1; i >= 0; i--) if (!_trees[i].Extra) return i;
                return Mathf.Max(0, _trees.Count - 1);
            }
        }

        public JobTree TreeOf(int skill) { if (skill != 0) foreach (var t in _trees) if (t.Shape != null && t.Shape.NodeOf.ContainsKey(skill)) return t; return null; }
        private TreeNode NodeOf(int skill, out JobTree tree) { tree = TreeOf(skill); return tree != null ? tree.Shape.Nodes[tree.Shape.NodeOf[skill]] : null; }

        /// <summary>Builds the trees for the character as it is now, if what they are made of changed (the job, the skills on them) or
        /// <paramref name="force"/>; a change of a skill's level or of the job level only redraws (<see cref="Redraw"/>).</summary>
        public void Rebuild(bool force = false)
        {
            var model = Model != null ? Model() : null; _model = model;
            int job = Job != null ? Job() : 0;
            var specs = Split(model);
            var sb = new System.Text.StringBuilder(); sb.Append(job).Append(':');
            foreach (var s in specs) { sb.Append(s.Job).Append('['); foreach (var c in s.Cells) sb.Append(c.Skill).Append(','); sb.Append(']'); }
            string sig = sb.ToString();
            if (!force && sig == _signature && _trees.Count > 0) { Redraw(); return; }
            bool same = sig == _signature && _trees.Count == specs.Count;
            _signature = sig;
            if (!same)
            {
                if (Diorama != null) { Diorama.Hide(); Diorama.transform.SetParent(Root, false); } // (it stands in a tree's frame: not destroyed with it)
                _rHover.transform.SetParent(Root, false); _rSelect.transform.SetParent(Root, false); _rHover.gameObject.SetActive(false); _rSelect.gameObject.SetActive(false);
                foreach (var t in _trees) DestroyTree(t);
                _trees.Clear(); _flipFrom = null; _shownPreview = 0;
                for (int i = 0; i < specs.Count; i++) _trees.Add(Make(specs[i], i));
            }
            // the Novice's sapling beside a job's trees and the tree "Other" beside any other tree are no trees of the ring: each is
            // hidden in a node at the first job's base; the others take the ring's places in order
            int jobs = 0, extras = 0; foreach (var s in specs) if (!s.Other && s.Job != 0) jobs++;
            RingCount = 0;
            for (int i = 0; i < _trees.Count; i++)
            {
                var t = _trees[i]; t.Cells = specs[i].Cells;
                t.Extra = t.Other ? specs.Count > 1 : t.Job == 0 && jobs > 0;
                t.Ring = t.Extra ? -1 : RingCount++; t.ExtraAt = t.Extra ? extras++ : 0;
            }
            foreach (var t in _trees) { t.Extras = extras; BuildTree(t); }
            BuildBuds();
            Builds++;
            if (Selected != 0 && TreeOf(Selected) == null) Selected = 0;
            if (Hover != 0 && TreeOf(Hover) == null) Hover = 0;
            Redraw(force);
            if (!force && gameObject.activeSelf && !same) { Focus = CurrentTree; _ringTurn = 0f; Lifts(); LayoutTrees(); LayoutPanels(); PlaceMarks(); } // (the job changed while the menu is open: its newest tree)
            Focus = Mathf.Clamp(Focus, 0, Mathf.Max(0, _trees.Count - 1)); if (FocusTree != null && FocusTree.Extra) Focus = CurrentTree;
        }

        private static void DestroyObj(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }

        private JobTree Make(Spec s, int index)
        {
            var t = new JobTree { Index = index, Job = s.Job, Name = s.Name, Other = s.Other, Cells = s.Cells };
            t.Theme = TreeTheme.OfJob(s.Job, s.Other);
            t.Family = t.Theme.Family;
            t.Line = t.Theme.Line;
            t.Ornament = s.Other ? 0 : TreeGrowth.OrnamentOf(s.Job); // FIXED by the tree's own job (design note: "the appearance of a swordsman's tree is fixed")
            var go = new GameObject("tree:" + (s.Other ? "other" : s.Job.ToString()), typeof(RectTransform)); go.layer = UiKit.PanelLayer; go.transform.SetParent(Root, false);
            t.Holder = (RectTransform)go.transform; t.Holder.anchorMin = t.Holder.anchorMax = t.Holder.pivot = new Vector2(0.5f, 0.5f); t.Holder.sizeDelta = new Vector2(10, 10);
            // behind everything: what stops the laser and the look on the tree (a pull between two medallions does nothing in the world)
            t.Blocker = UiKit.Box(t.Holder, "blocker", Vector2.zero, new Vector2(600, 600), new Color(0, 0, 0, 0));
            DestroyObj(t.Blocker.GetComponent<Image>());
            t.BlockCol = t.Blocker.gameObject.AddComponent<BoxCollider>(); t.Blocker.gameObject.AddComponent<UiBlocker>();
            t.MatTree = Mat("LokiTree", QueueTree, false, false); t.MatGlow = Mat("LokiTreeGlow", QueueGlow, true, false);
            t.MatMedals = Mat("LokiTreeMedals", QueueMedals, false, false); t.MatIcons = Mat("LokiTreeIcons", QueueIcons, false, true);
            Layer("tree", t.Holder, t.MatTree, out t.MeshTree); Layer("glow", t.Holder, t.MatGlow, out t.MeshGlow); Layer("lines", t.Holder, t.MatGlow, out t.MeshLines);
            Layer("medallions", t.Holder, t.MatMedals, out t.MeshMedals); Layer("icons", t.Holder, t.MatIcons, out t.MeshIcons);
            t.RendFx = Layer("fx", t.Holder, _matFx, out t.MeshFx); t.RendFx.gameObject.SetActive(false);
            return t;
        }

        private void DestroyTree(JobTree t)
        {
            foreach (var m in new[] { t.MatTree, t.MatGlow, t.MatMedals, t.MatIcons }) if (m != null) DestroyObj(m);
            foreach (var m in new[] { t.MeshTree, t.MeshGlow, t.MeshMedals, t.MeshIcons, t.MeshLines, t.MeshFx }) if (m != null) DestroyObj(m);
            if (t.Atlas != null) DestroyObj(t.Atlas);
            if (t.Holder != null) { t.Holder.gameObject.SetActive(false); DestroyObj(t.Holder.gameObject); }
        }

        private void BuildTree(JobTree t)
        {
            t.Shape = TreeLayout.Make(t.Cells, t.Ornament, TreeGrowth.StageOf(t.Ornament).Radius, t.Job == 0 || t.Other); // (the Novice's: the small sapling; "Other": a small stake)
            t.Shape.Job = t.Job;
            t.Shape.Bare = t.Other; // (no crown of buds: that is the Novice's)
            _bTree.Clear(); _bGlow.Clear();
            t.Counts = TreeArt.Build(_bTree, _bGlow, t.Shape, t.Theme);
            _bTree.Fill(t.MeshTree); _bGlow.Fill(t.MeshGlow); t.ArtTriangles = _bTree.Triangles + _bGlow.Triangles;
            BuildAtlas(t);
            BuildNodes(t);
            // the blocker spans the tree; the trunk's pivot for its rise: its foot
            float w = t.Shape.HalfWidth * 2f + 30f, h = t.Shape.Top - t.Shape.Bottom + 50f, mid = (t.Shape.Top + t.Shape.Bottom) * 0.5f;
            t.Blocker.sizeDelta = new Vector2(w, h); t.Blocker.anchoredPosition3D = new Vector3(0, mid, 34f); t.BlockCol.size = new Vector3(w, h, 4f);
            // the lively colour and the slow breath (UI/TreeThemes.cs): the tree's own material only
            t.MatTree.SetColor("_LifeColor", t.Theme.LifeColorAt(t.Ornament));
            t.MatTree.SetFloat("_Breath", t.Theme.Breath); t.MatTree.SetFloat("_BreathPeriod", t.Theme.BreathPeriod);
            t.MatTree.SetVector("_TrunkPivot", new Vector4(0f, t.Shape.Bottom, 0f, 0f));
            StillPose(t);
        }

        /// <summary>The icons of a tree's skills on one sheet (so every icon is one draw call): the pictures of the player's own game
        /// data (UI/WristMenu.cs SkillIcon: data\texture\...\item\&lt;skill&gt;.bmp), copied at run time; a skill without one keeps an empty place.</summary>
        private void BuildAtlas(JobTree t)
        {
            const int cell = 32; int n = Mathf.Max(1, t.Shape.Nodes.Count), cols = Mathf.CeilToInt(Mathf.Sqrt(n)), rows = Mathf.CeilToInt(n / (float)cols);
            int w = Mathf.NextPowerOfTwo(cols * cell), h = Mathf.NextPowerOfTwo(rows * cell);
            if (t.Atlas == null || t.Atlas.width != w || t.Atlas.height != h)
            {
                if (t.Atlas != null) DestroyObj(t.Atlas);
                t.Atlas = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "SkillTreeIcons", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            }
            var px = new Color32[w * h]; t.AtlasAt.Clear(); t.IconsFound = 0;
            for (int i = 0; i < t.Shape.Nodes.Count; i++)
            {
                var info = SkillDb.Get(t.Shape.Nodes[i].Skill); Sprite s = null;
                try { s = WristMenu.SkillIcon(info); } catch (Exception e) { Debug.LogException(e); }
                if (s == null || s.texture == null) continue;
                var tex = s.texture; int tw = Mathf.Min(tex.width, cell), th = Mathf.Min(tex.height, cell), ox = (i % cols) * cell + (cell - tw) / 2, oy = (i / cols) * cell + (cell - th) / 2;
                Color32[] src; try { src = tex.GetPixels32(); } catch (Exception) { continue; } // (a picture that cannot be read: no icon)
                for (int y = 0; y < th; y++) for (int x = 0; x < tw; x++) px[(oy + y) * w + ox + x] = src[y * tex.width + x];
                t.AtlasAt[t.Shape.Nodes[i].Skill] = new Rect(ox / (float)w, oy / (float)h, tw / (float)w, th / (float)h); t.IconsFound++;
            }
            t.Atlas.SetPixels32(px); t.Atlas.Apply(false, false);
            t.MatIcons.mainTexture = t.Atlas;
        }

        private Text MmText(RectTransform parent, string text, Vector3 at, Vector2 size, float heightMm, TextAnchor align, Color col)
        {
            var t = UiKit.Label(parent, text, Vector2.zero, size / TextScale, Mathf.RoundToInt(heightMm / TextScale), align, col);
            var rt = t.rectTransform; rt.localScale = Vector3.one * TextScale; rt.anchoredPosition3D = at;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private void BuildNodes(JobTree t)
        {
            foreach (var bt in t.Btns) if (bt != null) DestroyObj(bt.gameObject);
            if (t.Title != null) DestroyObj(t.Title.gameObject);
            t.Btns.Clear(); t.Texts.Clear(); t.Alpha.Clear(); t.Tint.Clear();
            float d = TreeLayout.NodeR * 2f, plaque = TreeLayout.PlaqueH + TreeLayout.PlaqueGap;
            foreach (var n in t.Shape.Nodes)
            {
                int skill = n.Skill; var info = SkillDb.Get(skill);
                var go = new GameObject("node:" + info.Aegis, typeof(RectTransform)); go.layer = UiKit.PanelLayer; go.transform.SetParent(t.Holder, false);
                var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(d + 4f, d + 4f); rt.anchoredPosition3D = n.Pos;
                var col = go.AddComponent<BoxCollider>(); col.size = new Vector3(d + 4f, d + plaque + 4f, 14f); col.center = new Vector3(0, -plaque * 0.5f, 0f);
                var btn = go.AddComponent<UiButton>(); btn.Init(null, null, () => Press(skill));
                var text = MmText(rt, "", new Vector3(0, -TreeLayout.NodeR - TreeLayout.PlaqueGap - TreeLayout.PlaqueH * 0.5f, -1f), new Vector2(64f, TreeLayout.PlaqueH), LevelTextMm, TextAnchor.MiddleCenter, Color.white);
                text.name = "level";
                t.Btns.Add(btn); t.Texts.Add(text); t.Alpha.Add(-1f);
            }
            // the job's name on a small plate on the trunk, under the lowest row
            t.Title = MmText(t.Holder, t.Name, t.Shape.LabelAt, new Vector2(184f, 22f), 16f, TextAnchor.MiddleCenter, new Color(1f, 0.87f, 0.55f));
            t.Title.horizontalOverflow = HorizontalWrapMode.Wrap; t.Title.resizeTextForBestFit = true; t.Title.resizeTextMaxSize = t.Title.fontSize; t.Title.resizeTextMinSize = 20;
            t.Title.name = "job";
        }

        // ------------------------------------------------------------------ the medallions (redrawn when a state changes)
        private static Color32 C32(Color c, float a = 1f) { var r = (Color32)c; r.a = (byte)Mathf.RoundToInt(Mathf.Clamp01(c.a * a) * 255f); return r; }
        private static Color32 Shade(Color32 c, float k) => new Color32((byte)Mathf.Clamp(c.r * k, 0, 255), (byte)Mathf.Clamp(c.g * k, 0, 255), (byte)Mathf.Clamp(c.b * k, 0, 255), c.a);
        private static Color32 A(Color32 c, float a) => new Color32(c.r, c.g, c.b, (byte)Mathf.RoundToInt(Mathf.Clamp01(c.a / 255f * a) * 255f));

        /// <summary>The colours of a skill's chosen filters: its tags that are chosen (in the skill's own order), then the chosen
        /// elements its damage can have (Game/SkillElements.cs; design note 2026-10-11: "filter elemental damage types").</summary>
        private List<Color> ChosenColoursOf(int skill)
        {
            var l = new List<Color>();
            foreach (var id in SkillTags.For(skill)) if (_filters.Contains(id)) { var t = TagOf(id); if (t != null) l.Add(t.Color); }
            foreach (var id in SkillElements.For(skill)) if (_filters.Contains(id)) { var e = SkillElements.Get(id); if (e != null) l.Add(e.Color); }
            return l;
        }
        private static SkillTags.Tag TagOf(string id) => SkillTags.Get(id);
        /// <summary>The skill is drawn faint now: filters are chosen and it has none of them (ANY chosen tag or element lights it).</summary>
        public bool Dimmed(int skill)
        {
            if (_filters.Count == 0) return false;
            foreach (var id in SkillTags.For(skill)) if (_filters.Contains(id)) return false;
            foreach (var id in SkillElements.For(skill)) if (_filters.Contains(id)) return false;
            return true;
        }
        /// <summary>The elements this character's line can inflict, in the filter's order: of the skills of every job's tree and of
        /// the Novice's (not the stake "Other": skills from outside the trees come and go with what is worn).</summary>
        public List<SkillElements.Element> OfferedElements()
        {
            var have = new HashSet<string>();
            foreach (var t in _trees) { if (t.Other || t.Shape == null) continue; foreach (var n in t.Shape.Nodes) foreach (var id in SkillElements.For(n.Skill)) have.Add(id); }
            var l = new List<SkillElements.Element>();
            foreach (var e in SkillElements.All) if (have.Contains(e.Id)) l.Add(e);
            return l;
        }
        /// <summary>The colours of a medallion's ring now, one equal segment each: with filters chosen, the chosen tags and elements it
        /// has; without, its own tags (at most four, the most characteristic first).</summary>
        public List<Color> RingColours(int skill)
        {
            var l = new List<Color>();
            if (_filters.Count > 0) return ChosenColoursOf(skill);
            foreach (var id in SkillTags.For(skill)) { var t = TagOf(id); if (t != null) l.Add(t.Color); if (l.Count >= 4) break; }
            return l;
        }

        public const float LifeGrowSeconds = 0.45f;
        public int BloomSoundsStarted { get; private set; }

        /// <summary>Draws every tree's medallions, icons, prerequisite lines and level texts for the model as it is now, and gives each
        /// tree its liveliness driven by the skill points spent (or planned) in that specific tree.</summary>
        public void Redraw(bool snapLife = false)
        {
            if (_trees.Count == 0) { _dirty = false; return; }
            _coverDirty = true; // (client wave 23: medallions made anew are looked at against the panels again)
            var cells = new Dictionary<int, SkillTreeModel.Cell>();
            if (_model != null) foreach (var tab in _model.Tabs) foreach (var c in tab.Cells.Values) cells[c.Skill] = c;
            int job = Job != null ? Job() : 0;
            bool planning = PlanMode != null && PlanMode();
            int focus = Hover != 0 ? Hover : Selected;
            var needs = focus != 0 ? SkillTreeDb.AllNeeds(focus, job) : null;
            // client wave 26 (w26skill): the lines are the POINTED skill's only (the design's answer 33); it locked: dotted, moving, the others faded
            _cells = cells; _lineFocus = Hover; _lineNeeds = Hover != 0 ? (Hover == focus ? needs : SkillTreeDb.AllNeeds(Hover, job)) : null;
            var hn = Hover != 0 ? NodeOf(Hover, out _) : null; SkillTreeModel.Cell hc = null;
            if (Hover != 0 && !cells.TryGetValue(Hover, out hc)) hc = hn != null ? hn.Cell : null;
            _lineLocked = Hover != 0 && hc != null && hc.State == SkillTreeModel.State.Locked;
            foreach (var t in _trees)
            {
                int spent = 0, maxPts = 0;
                if (t.Shape != null)
                    foreach (var n in t.Shape.Nodes)
                    {
                        string type = SkillTreeDb.ExtraOf(n.Skill).Type;
                        if (type == "Quest" || type == "Soul") continue;
                        cells.TryGetValue(n.Skill, out var cell); cell = cell ?? n.Cell;
                        int own = cell?.Entry != null ? cell.Entry.Level : 0;
                        int planned = planning && Planned != null ? Planned(n.Skill) : 0;
                        int lv = Mathf.Max(own, planned);
                        var info = SkillDb.Get(n.Skill);
                        spent += lv;
                        maxPts += Mathf.Max(info.MaxLevel, lv);
                    }
                t.SpentPoints = spent;
                t.MaxBeautyPoints = Mathf.Min(TreeGrowth.MaxBeautyThreshold(t.Ornament), Mathf.Max(1, maxPts));
                float targetLife = t.Other ? 1f : TreeGrowth.LivelinessByPoints(t.Ornament, spent, maxPts);
                bool animLife = !snapLife && t.TargetLife >= 0f && Shown && !t.Rising && MenuAnim.IsOn(MenuAnim.Skills);
                if (t.TargetLife >= 0f && Shown && !t.Rising && targetLife > t.TargetLife + 0.001f)
                {
                    if (HasSound != null && PlaySound != null)
                        foreach (var snd in new[] { "se_ding2.wav", "effect\\ef_angelus.wav", "levelup.wav" })
                            if (HasSound(snd)) { PlaySound(snd); BloomSoundsStarted++; break; }
                }
                t.TargetLife = targetLife;
                if (!animLife) { t.Life = targetLife; t.MatTree.SetFloat("_Life", t.Life); }
                RedrawTree(t, cells, needs, focus);
            }
            _dirty = false;
        }

        private void RedrawTree(JobTree t, Dictionary<int, SkillTreeModel.Cell> cells, Dictionary<int, int> needs, int focus)
        {
            var st = TreeGrowth.StageOf(t.Ornament); var th = t.Theme; var shape = t.Shape;
            int points = SkillPoints != null ? SkillPoints() : 0; bool planning = PlanMode != null && PlanMode();
            _bMedals.Clear(); _bIcons.Clear(); _bLines.Clear(); t.LinesDrawn = 0;
            float R = TreeLayout.NodeR;
            var rim = st.Frame >= 2 ? th.RimOrnate : th.Rim;

            // (client wave 26, w26skill: the prerequisite lines are drawn by DrawLines, only for the skill a pointer rests on)
            // the plate the job's name is written on
            {
                var pc = shape.LabelAt; pc.z = -42f;
                _bMedals.Pen(new Color32(24, 26, 34, 244), 0.1f); PlaqueShape(_bMedals, pc, 190f, 25f, shape.TrunkBirth(shape.Bottom + TreeLayout.TrunkBelow) + 0.06f, new Vector3(0f, pc.y, 0f));
            }

            for (int i = 0; i < shape.Nodes.Count; i++)
            {
                var n = shape.Nodes[i]; var p = n.Pos; var info = SkillDb.Get(n.Skill);
                cells.TryGetValue(n.Skill, out var cell); cell = cell ?? n.Cell; n.Cell = cell;
                var state = cell.State; var k = cell.Entry; int own = k != null ? k.Level : 0, max = Mathf.Max(info.MaxLevel, own);
                int planned = planning && Planned != null ? Planned(n.Skill) : 0; bool plan = planned > own;
                bool dim = Dimmed(n.Skill); float al = (dim ? DimAlpha : 1f) * (Faded(n.Skill) ? HoverFadeAlpha : 1f); float birth = n.Birth;
                // a skill the one pointed at needs (on this tree or another): a ring round it, green when it is met, orange when not
                if (needs != null && n.Skill != focus && needs.TryGetValue(n.Skill, out int needLv))
                {
                    _bMedals.Pen(own >= needLv ? new Color32(140, 255, 150, 235) : new Color32(255, 170, 90, 235), 0f, 1f);
                    _bMedals.Arc(p + new Vector3(0, 0, 1f), R + 3f, R + 7f, 0f, 360f, 24, birth, p);
                }
                // the face
                Color32 face = state == SkillTreeModel.State.Learned ? th.Face : state == SkillTreeModel.State.Learnable ? new Color32(150, 196, 150, 255) : new Color32(52, 54, 62, 255);
                if (plan) face = new Color32(120, 160, 220, 255);
                _bMedals.Pen(A(Shade(rim, 0.35f), al), 0.2f); _bMedals.Ring(p + new Vector3(0, 0, 1.5f), 0f, R + 1.6f, 22, birth, p); // a dark back under the rim
                _bMedals.Pen(A(face, al), 0.15f); _bMedals.Ring(p, 0f, R - 5.5f, 22, birth, p);
                // the ring: split into the tags' colours, or plain
                var ring = RingColours(n.Skill);
                if (ring.Count == 0) { _bMedals.Pen(A(Shade(rim, state == SkillTreeModel.State.Locked ? 0.5f : 0.75f), al), 0.3f); _bMedals.Arc(p, R - 5.2f, R - 0.8f, 0f, 360f, 22, birth, p); }
                else
                {
                    float span = 360f / ring.Count, gap = ring.Count > 1 ? 5f : 0f; bool lit = _filters.Count > 0;
                    for (int s = 0; s < ring.Count; s++)
                    {
                        var c = C32(ring[s]); if (!lit && state == SkillTreeModel.State.Locked) c = Shade(c, 0.6f);
                        _bMedals.Pen(A(c, al), 0.2f, lit ? 0.75f : 0.35f); _bMedals.Arc(p, R - 5.2f, R - 0.8f, s * span + gap * 0.5f, (s + 1) * span - gap * 0.5f, Mathf.Max(3, 22 / ring.Count), birth, p);
                    }
                }
                // the rim: plain, double, studded, ornate
                var rimCol = state == SkillTreeModel.State.Locked ? Shade(rim, 0.62f) : rim;
                _bMedals.Pen(A(rimCol, al), 0.85f); _bMedals.Bezel(p, R - 1.2f, R + 1.4f, 1.8f, 22, birth, p);
                if (st.Frame >= 1) _bMedals.Bezel(p, R - 6.8f, R - 5f, 1.2f, 22, birth, p);
                if (st.Frame >= 2) for (int s = 0; s < 8; s++) { float a = (s + 0.5f) * 45f * Mathf.Deg2Rad; _bMedals.Ring(p + new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f) * (R + 0.2f) + new Vector3(0, 0, -2f), 0f, 2.3f, 6, birth + 0.01f, p); _bMedals.Pieces++; }
                if (st.Frame >= 3)
                    for (int s = 0; s < 4; s++)
                    {
                        float a = s * 90f * Mathf.Deg2Rad; var dir = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f); var side = new Vector3(dir.y, -dir.x, 0f); var b0 = p + dir * (R + 1f) + new Vector3(0, 0, -1f);
                        int v0 = _bMedals.Vert(b0 - side * 4.6f, Vector3.back, p, birth + 0.02f), v1 = _bMedals.Vert(b0 + dir * 9f, Vector3.back, p, birth + 0.02f), v2 = _bMedals.Vert(b0 + side * 4.6f, Vector3.back, p, birth + 0.02f);
                        _bMedals.Tri(v0, v1, v2); _bMedals.Pieces++;
                    }
                // the plaque its level is written on
                var pc = p + new Vector3(0, -R - TreeLayout.PlaqueGap - TreeLayout.PlaqueH * 0.5f, 0f);
                _bMedals.Pen(A(new Color32(24, 26, 34, 255), al), 0.1f); PlaqueShape(_bMedals, pc, 50f, TreeLayout.PlaqueH, birth, p);
                // a "+" where a point can raise it now
                if (k != null && k.Upgradable && points > 0 && !planning)
                {
                    var q = p + new Vector3(R * 0.72f, R * 0.72f, -2.5f);
                    _bMedals.Pen(A(new Color32(70, 190, 90, 255), al), 0.3f, 0.5f); _bMedals.Ring(q, 0f, 8.5f, 12, birth + 0.03f, p);
                    _bMedals.Pen(A(new Color32(255, 255, 255, 255), al), 0f, 1f); _bMedals.Line(q + new Vector3(-5f, 0, -0.5f), q + new Vector3(5f, 0, -0.5f), 2.4f, birth + 0.03f, birth + 0.03f); _bMedals.Line(q + new Vector3(0, -5f, -0.5f), q + new Vector3(0, 5f, -0.5f), 2.4f, birth + 0.03f, birth + 0.03f);
                }
                // the icon
                if (t.AtlasAt.TryGetValue(n.Skill, out var uv))
                {
                    float ia = state == SkillTreeModel.State.Learned || plan ? 1f : state == SkillTreeModel.State.Learnable ? 0.85f : 0.42f; byte g = (byte)(state == SkillTreeModel.State.Locked && !plan ? 170 : 255);
                    _bIcons.Pen(new Color32(g, g, g, (byte)Mathf.RoundToInt(255f * ia * al))); _bIcons.Picture(p + new Vector3(0, 0, -1.2f), 36f, uv, birth + 0.01f, p);
                }
                // the level on the plaque
                var text = t.Texts[i]; string s2; Color tc;
                if (plan) { s2 = $"{planned}/{max}"; tc = new Color(0.56f, 0.77f, 1f); }
                else if (state == SkillTreeModel.State.Learned) { s2 = $"{own}/{max}"; tc = new Color(1f, 0.88f, 0.5f); }
                else if (state == SkillTreeModel.State.Learnable) { s2 = $"0/{max}"; tc = new Color(0.55f, 0.95f, 0.55f); }
                else { s2 = cell.Why == SkillTreeModel.Why.Tier ? Lang.T("~cell~tier") : cell.Why == SkillTreeModel.Why.Quest ? Lang.T("~cell~quest") : $"0/{max}"; tc = new Color(0.62f, 0.64f, 0.7f); }
                if (text.text != s2) text.text = s2;
                t.Tint[i] = tc; t.Alpha[i] = -1f;
            }
            if (t.Ring == 0) foreach (var e in _trees) if (e.Extra && e.Bud != null) DrawBud(t, e, rim);
            _bMedals.Fill(t.MeshMedals); _bIcons.Fill(t.MeshIcons); DrawLines(t);
            t.Triangles = t.ArtTriangles + _bMedals.Triangles + _bIcons.Triangles + _bLines.Triangles;
            t.MedalPieces = _bMedals.Pieces;
            ApplyTextAlpha(t);
        }

        /// <summary>A node at the first job's base: a small mound in the tree's rim with what is hidden in it as its picture (the
        /// sapling's sprout: a stem and two leaves; the stake: a post), its name on a plate under it; lit while it is grown out.</summary>
        private void DrawBud(JobTree first, JobTree e, Color32 rim)
        {
            var p = e.BudAt; float r = BudR, birth = first.Shape.TrunkBirth(first.Shape.Bottom) + 0.06f; var n = Vector3.back;
            _bMedals.Pen(Shade(rim, 0.35f), 0.2f); _bMedals.Ring(p + new Vector3(0, 0, 1.5f), 0f, r + 1.6f, 20, birth, p);
            _bMedals.Pen(new Color32(104, 78, 56, 255), 0.1f); _bMedals.Ring(p, 0f, r - 1f, 20, birth, p);                       // the earth
            _bMedals.Pen(new Color32(84, 62, 44, 255), 0.1f); _bMedals.Arc(p + new Vector3(0, 0, -0.4f), 0f, r - 1f, 100f, 260f, 12, birth, p); // darker at its foot
            if (!e.Other)
            {
                var green = new Color32(132, 196, 96, 255); var q = p + new Vector3(0, 0, -1f);
                _bMedals.Pen(green, 0.2f, 0.3f); _bMedals.Line(q + new Vector3(0f, -9f, 0f), q + new Vector3(0f, 3f, 0f), 3f, birth, birth);
                foreach (int sg in new[] { -1, 1 })
                {
                    int v0 = _bMedals.Vert(q + new Vector3(0f, 2f, 0f), n, p, birth), v1 = _bMedals.Vert(q + new Vector3(sg * 13f, 9f, 0f), n, p, birth), v2 = _bMedals.Vert(q + new Vector3(sg * 5f, 15f, 0f), n, p, birth);
                    _bMedals.Tri(v0, v1, v2);
                }
            }
            else
            {
                var wood = new Color32(196, 160, 110, 255); var q = p + new Vector3(0, 0, -1f);
                _bMedals.Pen(wood, 0.2f); _bMedals.Line(q + new Vector3(0f, -11f, 0f), q + new Vector3(0f, 12f, 0f), 4.4f, birth, birth); _bMedals.Line(q + new Vector3(-9f, 6f, 0f), q + new Vector3(9f, 6f, 0f), 3.4f, birth, birth);
            }
            _bMedals.Pen(e.Out ? new Color32(255, 214, 110, 255) : rim, 0.85f); _bMedals.Bezel(p, r - 1.2f, r + 1.4f, 1.8f, 20, birth, p);
            if (e.Out) { _bMedals.Pen(new Color32(255, 214, 110, 200), 0f, 1f); _bMedals.Arc(p + new Vector3(0, 0, 0.8f), r + 2.5f, r + 5.5f, 0f, 360f, 24, birth, p); }
            var pc = p + new Vector3(0, -r - TreeLayout.PlaqueGap - TreeLayout.PlaqueH * 0.5f, 0f);
            _bMedals.Pen(new Color32(24, 26, 34, 255), 0.1f); PlaqueShape(_bMedals, pc, r * 2f + 26f, TreeLayout.PlaqueH, birth, p);
            if (e.BudText != null) { var c = e.BudText.color; c.a = first.Fade * (first.Rising && first.MatMedals.GetFloat("_Grow") < birth + 0.1f ? 0f : 1f); e.BudText.color = c; }
        }

        private static void PlaqueShape(TreeMeshBuilder b, Vector3 c, float w, float h, float birth, Vector3 pivot)
        {
            // a small plate with cut corners, facing the player
            float hw = w * 0.5f, hh = h * 0.5f, cut = 4f; var n = Vector3.back;
            int mid = b.Vert(c, n, pivot, birth), first = b.V.Count;
            foreach (var o in new[] { new Vector2(-hw + cut, -hh), new Vector2(hw - cut, -hh), new Vector2(hw, -hh + cut), new Vector2(hw, hh - cut), new Vector2(hw - cut, hh), new Vector2(-hw + cut, hh), new Vector2(-hw, hh - cut), new Vector2(-hw, -hh + cut) })
                b.Vert(c + new Vector3(o.x, o.y, 0f), n, pivot, birth);
            for (int i = 0; i < 8; i++) b.Tri(mid, first + i, first + (i + 1) % 8);
        }

        // ------------------------------------------------------------------ where the trees stand
        /// <summary>Puts every tree at its place for the layout now: around the player all of them, one at a time the one in focus
        /// (and, while a flip slides, the one that leaves).</summary>
        private void LayoutTrees()
        {
            bool around = Around; int n = _trees.Count;
            LayArc(); _coverDirty = true;
            for (int i = 0; i < n; i++)
            {
                var t = _trees[i]; bool on; float az; float fade = 1f;
                if (t.Extra) continue;
                if (around) { on = true; az = ArcAzimuth(t.Ring) + _ringTurn; }
                else
                {
                    float u = _flipFrom != null ? Mathf.Clamp01(_flipT / FlipSeconds) : 1f, e = u * u * (3f - 2f * u);
                    if (i == Focus) { on = true; az = _flipFrom != null ? _flipDir * FlipFromDeg * (1f - e) : _pullAcc / _dist * Mathf.Rad2Deg; fade = _flipFrom != null ? u : 1f; }
                    else if (t == _flipFrom) { on = true; az = -_flipDir * FlipFromDeg * e; fade = 1f - u; }
                    else { on = false; az = 0f; }
                }
                if (t.Holder.gameObject.activeSelf != on) t.Holder.gameObject.SetActive(on);
                t.Azimuth = az; float r = az * Mathf.Deg2Rad;
                t.Holder.localPosition = new Vector3(_dist * Mathf.Sin(r), t.Lift, _dist * Mathf.Cos(r));
                t.Holder.localRotation = Quaternion.Euler(0f, az, 0f);
                if (Mathf.Abs(t.Fade - fade) > 0.001f) { t.Fade = fade; ApplyFade(t); }
            }
            // what has grown out of a node: beside the first job's tree, on its floor, at the trees' distance, turned to the player
            var first = RingTree(0); float used = 0f;
            foreach (var t in _trees)
            {
                if (!t.Extra) continue;
                bool show = (t.Out || t.Burying) && first != null && first.Active;
                if (t.Holder.gameObject.activeSelf != show) t.Holder.gameObject.SetActive(show);
                if (!show) continue;
                float half = t.HalfAngle(_dist), az = first.Azimuth - (first.HalfAngle(_dist) + SproutGapDeg + half) - used, r = az * Mathf.Deg2Rad; used += 2f * half + SproutGapDeg;
                t.Azimuth = az;
                t.Holder.localPosition = new Vector3(_dist * Mathf.Sin(r), first.Lift + first.Shape.Bottom - t.Shape.Bottom, _dist * Mathf.Cos(r));
                t.Holder.localRotation = Quaternion.Euler(0f, az, 0f);
                if (Mathf.Abs(t.Fade - first.Fade) > 0.001f) { t.Fade = first.Fade; ApplyFade(t); }
            }
        }

        /// <summary>The angle (degrees) what has grown out of the nodes takes left of the first job's tree (the plate steps aside by it).</summary>
        public float SproutsWidth()
        {
            float w = 0f; foreach (var t in _trees) if (t.Extra && (t.Out || t.Burying) && t.Active) w += 2f * t.HalfAngle(_dist) + SproutGapDeg;
            return w;
        }

        /// <summary>The nodes at the first job's base: one for each tree hidden there (the Novice's sapling left of the trunk's foot, the
        /// stake "Other" right of it): a button as a medallion's, its name under it.</summary>
        private void BuildBuds()
        {
            foreach (var t in _trees) { if (t.Bud != null) DestroyObj(t.Bud.gameObject); t.Bud = null; t.BudText = null; }
            var first = RingTree(0); if (first == null) return;
            float plaque = TreeLayout.PlaqueH + TreeLayout.PlaqueGap;
            foreach (var e in _trees)
            {
                if (!e.Extra) continue;
                var hidden = e;
                e.BudAt = new Vector3(e.Other ? BudAside : -BudAside, first.Shape.Bottom + 4f + plaque + BudR, TreeLayout.MidZ - 4f);
                var go = new GameObject("bud:" + (e.Other ? "other" : "novice"), typeof(RectTransform)); go.layer = UiKit.PanelLayer; go.transform.SetParent(first.Holder, false);
                var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(BudR * 2f + 16f, BudR * 2f + 16f); rt.anchoredPosition3D = e.BudAt;
                var col = go.AddComponent<BoxCollider>(); col.size = new Vector3(BudR * 2f + 16f, BudR * 2f + plaque + 8f, 14f); col.center = new Vector3(0, -plaque * 0.5f, 0f);
                e.Bud = go.AddComponent<UiButton>(); e.Bud.Init(null, null, () => ToggleSprout(hidden));
                e.BudText = MmText(rt, e.Name, new Vector3(0, -BudR - TreeLayout.PlaqueGap - TreeLayout.PlaqueH * 0.5f, -1f), new Vector2(BudR * 2f + 22f, TreeLayout.PlaqueH), LevelTextMm, TextAnchor.MiddleCenter, new Color(1f, 0.87f, 0.55f));
                e.BudText.horizontalOverflow = HorizontalWrapMode.Wrap; e.BudText.resizeTextForBestFit = true; e.BudText.resizeTextMaxSize = e.BudText.fontSize; e.BudText.resizeTextMinSize = 20;
                e.BudText.name = "bud";
            }
        }

        /// <summary>A node at the first job's base was pressed: the tree hidden in it GROWS out of the ground beside that tree (the
        /// family's rise in <see cref="RiseShortSeconds"/> s, one sound; at once when UI/MenuAnim.cs says no), or, grown, is BURIED
        /// again (<see cref="BurySeconds"/> s, silent). Its medallions take the laser, the look and the hand from the first frame.</summary>
        public void ToggleSprout(JobTree e)
        {
            if (e == null || !e.Extra) return;
            bool anim = MenuAnim.IsOn(MenuAnim.Skills);
            if (!e.Out)
            {
                e.Out = true; e.Burying = false; SproutsGrown++;
                LayoutTrees();
                if (anim) StartRise(e, 0f, true, 1f, true); else EndRise(e);
            }
            else
            {
                e.Out = false; SproutsBuried++;
                if (Selected != 0 && TreeOf(Selected) == e) { Selected = 0; _cardScrollReset = true; }
                if (Hover != 0 && TreeOf(Hover) == e) { Hover = 0; _hoverNew = 0; }
                if (Diorama != null && Diorama.transform.parent == e.Holder) { Diorama.Hide(); Diorama.transform.SetParent(Root, false); _shownPreview = 0; }
                if (anim) { e.Burying = true; e.BuryT = BurySeconds; e.Rising = true; } else e.Burying = false;
                LayoutTrees();
            }
            PlaceMarks(); Redraw(); LayoutPanels(); RefreshPanels();
            Debug.Log($"[Tree] the {e.Name} node pressed: {(e.Out ? "grown out of the ground left of the " + (RingTree(0) != null ? RingTree(0).Name : "?") + "'s tree" : "buried")}{(anim ? (e.Out ? $" in {RiseShortSeconds:0.00} s" : $" in {BurySeconds:0.00} s") : " at once")}");
        }
        /// <summary>How often a hidden tree was grown out of its node / buried (smoke, log).</summary>
        public int SproutsGrown { get; private set; }
        public int SproutsBuried { get; private set; }

        /// <summary>THE TABLE: how far each tree of the ring is lifted so that its floor stands <see cref="Clearance"/> over the ground the
        /// character stands on (asked when the trees open).</summary>
        private void Lifts()
        {
            float unit = Mathf.Max(1e-6f, Mathf.Abs(transform.lossyScale.y)); GroundNow = GroundY != null ? GroundY() : null;
            Clearance = Mathf.Clamp(TableClearCells * (MetresPerCell != null ? MetresPerCell() : 0.02f), TableClearMin, TableClearMax);
            foreach (var t in _trees)
            {
                t.Lift = 0f; if (t.Extra || t.Shape == null || !GroundNow.HasValue) continue;
                float floor = transform.position.y + (t.Shape.Bottom - 14f) * unit;
                t.Lift = Mathf.Max(0f, GroundNow.Value + Clearance - floor) / unit;
            }
        }

        private void ApplyFade(JobTree t)
        {
            foreach (var m in new[] { t.MatTree, t.MatGlow, t.MatMedals, t.MatIcons }) m.SetFloat("_Fade", t.Fade);
            for (int i = 0; i < t.Alpha.Count; i++) t.Alpha[i] = -1f;
            ApplyTextAlpha(t);
        }

        /// <summary>The level texts' and the job name's alpha: 0 until their medallion has grown out, fainter outside the filters and
        /// while the tree slides in or out.</summary>
        private void ApplyTextAlpha(JobTree t)
        {
            if (t.Shape == null) return;
            float g = t.Rising ? t.MatMedals.GetFloat("_Grow") : 1.2f;
            for (int i = 0; i < t.Shape.Nodes.Count && i < t.Texts.Count; i++)
            {
                var n = t.Shape.Nodes[i];
                float a = (g >= n.Birth + 0.1f ? 1f : 0f) * t.Fade;
                if (Dimmed(n.Skill)) a *= DimAlpha * 2f;
                if (Faded(n.Skill)) a *= HoverFadeAlpha; // (client wave 26: a locked skill pointed at fades the others)
                if (Mathf.Abs(a - t.Alpha[i]) < 0.02f) continue;
                t.Alpha[i] = a; var c = t.Tint.TryGetValue(i, out var tc) ? tc : Color.white; c.a = a; t.Texts[i].color = c;
            }
            if (t.Title != null) { var c = t.Title.color; c.a = (g >= 0.5f ? 1f : 0f) * t.Fade; t.Title.color = c; }
        }

        // ------------------------------------------------------------------ client wave 26 (w26skill): the prerequisite lines on hover
        private Dictionary<int, SkillTreeModel.Cell> _cells; private Dictionary<int, int> _lineNeeds; private int _lineFocus; private bool _lineLocked; private float _dashT;
        /// <summary>The skill whose prerequisite lines are drawn now (0: none: no line at all), whether they are dotted and moving (it is
        /// LOCKED: the others fade), the dashes' clock (s).</summary>
        public int LinesFocus => _lineFocus;
        public bool LinesDotted => _lineFocus != 0 && _lineLocked;
        public float DashTime => _dashT;
        /// <summary>A skill drawn faded now: a skill with prerequisites (or a locked skill) is pointed at and this one is neither it nor one of its prerequisites.</summary>
        public bool Faded(int skill) => _lineFocus != 0 && (_lineLocked || (_lineNeeds != null && _lineNeeds.Count > 0)) && skill != _lineFocus && (_lineNeeds == null || !_lineNeeds.ContainsKey(skill));

        /// <summary>A tree's prerequisite lines: only the pointed skill's chain among this tree's own skills (from each needed skill to the
        /// one that needs it), green when met, orange when not, behind the medallions and the solid plaques; dotted and moving while that
        /// skill is locked.</summary>
        private void DrawLines(JobTree t)
        {
            _bLines.Clear(); t.LinesDrawn = 0; t.DashesDrawn = 0;
            var shape = t.Shape;
            if (_lineNeeds != null && _lineFocus != 0 && shape != null)
            {
                float R = TreeLayout.NodeR;
                foreach (var l in shape.Links)
                {
                    var a = shape.Nodes[l.From]; var b = shape.Nodes[l.To];
                    if (!_lineNeeds.ContainsKey(a.Skill) || (b.Skill != _lineFocus && !_lineNeeds.ContainsKey(b.Skill))) continue;
                    bool met = _cells != null && _cells.TryGetValue(a.Skill, out var ca) && ca.Entry != null && ca.Entry.Level >= l.Level;
                    var d = b.Pos - a.Pos; d.z = 0f; float len = d.magnitude; if (len < R * 2f + 2f) continue; d /= len;
                    var p0 = a.Pos + d * (R + 2f); var p1 = b.Pos - d * (R + 2f); p0.z = p1.z = -8f;
                    Color32 col = met ? new Color32(140, 255, 150, 255) : new Color32(255, 170, 90, 255);
                    if (Dimmed(a.Skill) || Dimmed(b.Skill)) col = A(col, DimAlpha * 1.5f);
                    float b0 = Mathf.Max(a.Birth, b.Birth) + 0.02f, b1 = Mathf.Max(a.Birth, b.Birth) + 0.05f;
                    _bLines.Pen(col, 0f, 1f);
                    if (!_lineLocked) _bLines.Line(p0, p1, 4.6f, b0, b1);
                    else
                    {
                        // dotted, moving from the needed skill toward the one that needs it
                        var seg = p1 - p0; float span = seg.magnitude; if (span < 0.5f) continue; var dir = seg / span;
                        float step = DashMm + GapMm, off = Mathf.Repeat(_dashT * DashSpeed, step);
                        for (float s0 = off - step; s0 < span; s0 += step)
                        {
                            float a0 = Mathf.Max(0f, s0), a1 = Mathf.Min(span, s0 + DashMm); if (a1 - a0 < 0.6f) continue;
                            _bLines.Line(p0 + dir * a0, p0 + dir * a1, 4.6f, b0, b1); t.DashesDrawn++;
                        }
                    }
                    t.LinesDrawn++;
                }
            }
            _bLines.Fill(t.MeshLines);
        }

        /// <summary>Once a frame while a locked skill is pointed at: its dashes move (only the lines' meshes are made again).</summary>
        private void DashTick(float dt)
        {
            if (!LinesDotted) { _dashT = 0f; return; }
            _dashT += dt;
            foreach (var t in _trees) if (t.Active && t.LinesDrawn > 0) DrawLines(t);
        }

        private void PlaceMarks()
        {
            void Put(MeshRenderer r, int skill)
            {
                var n = NodeOf(skill, out var tree); bool on = n != null && tree.Active;
                if (r.gameObject.activeSelf != on) r.gameObject.SetActive(on);
                if (!on) return;
                if (r.transform.parent != tree.Holder) r.transform.SetParent(tree.Holder, false);
                r.transform.localPosition = n.Pos + new Vector3(0, 0, -3f); r.transform.localRotation = Quaternion.identity; r.transform.localScale = Vector3.one;
            }
            Put(_rHover, Hover != Selected ? Hover : 0); Put(_rSelect, Selected);
        }

        // ------------------------------------------------------------------ one at a time: flipping; around you: turning the ring
        /// <summary>One at a time: to the next tree (<paramref name="dir"/> +1: the next job's, -1: the job before). False at an end
        /// (nothing happens). The next tree slides in over <see cref="FlipSeconds"/> with a short rise of its own.</summary>
        public bool Flip(int dir)
        {
            if (Around || RingCount < 2 || FocusTree == null) return false;
            var next = RingTree(FocusTree.Ring + (dir >= 0 ? 1 : -1)); int to = next != null ? next.Index : -1;
            if (next == null) { FlipsRefused++; Debug.Log($"[Tree] flip {(dir >= 0 ? "forward" : "back")}: at the {(dir >= 0 ? "last" : "first")} tree, nothing to flip to"); return false; }
            if (_flipFrom != null) { _flipFrom = null; LayoutTrees(); } // (a flip during a flip: the first one ends at once)
            bool slide = MenuAnim.IsOn(MenuAnim.Skills);
            var from = _trees[Focus];
            if (Selected != 0) { Selected = 0; _cardScrollReset = true; }
            Hover = 0; _hoverNew = 0;
            Focus = to; Flips++; _pullAcc = 0f;
            if (slide) { _flipFrom = from; _flipT = 0f; _flipDir = dir >= 0 ? 1 : -1; StartRise(_trees[to], 0f, true); }
            LayoutTrees(); PlaceMarks(); Redraw(); LayoutPanels(); RefreshPanels();
            Debug.Log($"[Tree] flipped {(dir >= 0 ? "forward" : "back")} to tree {next.Ring + 1} of {RingCount} ({next.Name}){(slide ? $", sliding {FlipSeconds:0.0} s" : "")}");
            return true;
        }

        /// <summary>A grip on a tree pulled sideways by <paramref name="mm"/> (to the right: positive; at the trees' own size). Around
        /// you the ring turns with the hand (as far as bringing its first or its last tree in front); one at a time the tree follows
        /// the hand and, pulled <see cref="FlipPullMm"/>, flips (to the left: forward).</summary>
        public void PullBy(float mm)
        {
            _pulled = true;
            if (Around)
            {
                float lim = Mathf.Max(Mathf.Abs(ArcAzimuth(0)), Mathf.Abs(ArcAzimuth(RingCount - 1))); // (as far as bringing the arc's first or last tree in front)
                _ringTurn = Mathf.Clamp(_ringTurn + mm / _dist * Mathf.Rad2Deg, -lim, lim);
                LayoutTrees(); LayoutPanels();
                return;
            }
            _pullAcc += mm;
            if (Mathf.Abs(_pullAcc) >= FlipPullMm)
            {
                int dir = _pullAcc < 0f ? 1 : -1;
                if (!Flip(dir)) _pullAcc = Mathf.Clamp(_pullAcc, -FlipPullMm, FlipPullMm); // (at an end: the tree leans as far as it goes and stays)
            }
            LayoutTrees(); LayoutPanels();
        }
        /// <summary>The pull's grip was let go: one at a time the tree settles straight ahead; around you the ring stays as it was turned.</summary>
        public void PullEnd()
        {
            _pulled = false; _pullAcc = 0f;
            LayoutTrees(); LayoutPanels();
            Debug.Log(Around ? $"[Tree] ring turned to {_ringTurn:0}°" : $"[Tree] pull let go on tree {(FocusTree != null ? FocusTree.Ring + 1 : 0)} of {RingCount}");
        }

        /// <summary>A point of the world is on a tree (within its width and height, <paramref name="depth"/> m in front of / behind it): a grip there pulls.</summary>
        public bool Holds(Vector3 world, float depth = 0.16f)
        {
            if (!Shown) return false;
            foreach (var t in _trees)
            {
                if (!t.Active || t.Shape == null || t.Extra) continue; // (the sapling is no tree of the ring: nothing to pull)
                var l = t.Holder.InverseTransformPoint(world);
                if (Mathf.Abs(l.x) <= t.Blocker.sizeDelta.x * 0.5f && l.y >= t.Shape.Bottom - 25f && l.y <= t.Shape.Top + 25f && Mathf.Abs(l.z) * Mathf.Abs(transform.lossyScale.x) <= depth) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ choosing
        /// <summary>A medallion was pressed (the laser or the look + trigger, a fingertip held on it): while planning, one more planned
        /// level (the flat page's rule); else it is selected: its card opens with its buttons.</summary>
        public void Press(int skill)
        {
            if (TreeOf(skill) == null) return;
            Presses++;
            if (PlanMode != null && PlanMode() && PlanStage != null) { PlanStage(skill); _dirty = true; return; }
            Select(skill);
        }
        public int Presses { get; private set; }
        public void Select(int skill)
        {
            Selected = skill; _cardScrollReset = true;
            var t = TreeOf(skill);
            if (t != null && Around && !t.Extra && t.Index != Focus) { Focus = t.Index; LayoutPanels(); } // (the plate and the card serve the tree of the skill selected)
            PlaceMarks(); Redraw(); RefreshPanels();
            if (skill != 0) Debug.Log($"[Tree] selected {skill} {SkillDb.Get(skill).Aegis}");
        }

        /// <summary>A filter on / off (the plate's buttons: a tag's id, or an element's "ele:..."); none chosen: every skill is lit.
        /// The filters hold for every tree.</summary>
        public void ToggleFilter(string tagId)
        {
            if (!_filters.Remove(tagId)) _filters.Add(tagId);
            Debug.Log($"[Tree] filters: {(_filters.Count == 0 ? "none" : string.Join(", ", _filters))}");
            Redraw(); RefreshPanels();
        }
        public void ClearFilters() { if (_filters.Count == 0) return; _filters.Clear(); Redraw(); RefreshPanels(); }
        public bool FilterOn(string tagId) => _filters.Contains(tagId);
        public int FilterCount => _filters.Count;

        // ------------------------------------------------------------------ every frame
        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            if (Around != _wasAround)
            {
                // the Setting changed while the menu is open: the other layout at once
                _wasAround = Around; _ringTurn = 0f; _pullAcc = 0f; _flipFrom = null;
                if (!Around) Focus = Selected != 0 && TreeOf(Selected) != null && !TreeOf(Selected).Extra ? TreeOf(Selected).Index : CurrentTree;
                LayoutTrees(); PlaceMarks(); LayoutPanels(); RefreshPanels();
            }
            if (_flipFrom != null)
            {
                _flipT += dt;
                if (_flipT >= FlipSeconds) { _flipFrom = null; }
                LayoutTrees();
            }
            RiseTick(dt);
            LifeTick(dt);
            SortTrees();
            UpdateFace(dt);
            UpdateHover(dt);
            DashTick(dt); // client wave 26 (w26skill): a locked skill's dotted lines move
            UpdateTips(dt);
            if (_dirty) { Rebuild(); RefreshPanels(); }
            PanelsTick(dt);
        }

        private void LifeTick(float dt)
        {
            foreach (var t in _trees)
            {
                if (t.TargetLife < 0f || Mathf.Abs(t.Life - t.TargetLife) <= 1e-4f) continue;
                t.Life = Mathf.MoveTowards(t.Life, t.TargetLife, dt / Mathf.Max(0.05f, LifeGrowSeconds));
                t.MatTree.SetFloat("_Life", t.Life);
            }
        }

        /// <summary>The trees' drawing order: the farthest from the eyes first (their materials' queues; nothing else changes).</summary>
        private void SortTrees()
        {
            if (_head == null || _trees.Count < 2) return;
            var eye = _head.transform.position; _sorted.Clear();
            foreach (var t in _trees)
            {
                if (!t.Active) continue;
                float d = (t.Holder.position - eye).sqrMagnitude; int at = _sorted.Count;
                while (at > 0 && (_sorted[at - 1].Holder.position - eye).sqrMagnitude < d) at--;
                _sorted.Insert(at, t);
            }
            for (int i = 0; i < _sorted.Count; i++)
            {
                var t = _sorted[i]; if (t.Rank == i) continue;
                t.Rank = i; int q = QueueFirst + i * QueueStep;
                t.MatTree.renderQueue = q; t.MatGlow.renderQueue = q + 2; t.MatMedals.renderQueue = q + 4; t.MatIcons.renderQueue = q + 6;
            }
        }
        private readonly List<JobTree> _sorted = new List<JobTree>();

        /// <summary>Around you: the tree the player faces (the head's direction; a tree is faced once the head has been nearer to it by
        /// <see cref="FaceMarginDeg"/> for <see cref="FaceRest"/> s) gets the plate and the card; a selected skill holds them at its
        /// tree, and so does a pointer on one of their buttons.</summary>
        private void UpdateFace(float dt)
        {
            if (!Around || RingCount < 2 || _head == null || FocusTree == null || (Selected != 0 && TreeOf(Selected) != null && !TreeOf(Selected).Extra) || _pulled) { _faceNew = -1; return; }
            foreach (var b in _panelBtns) if (b != null && b.isActiveAndEnabled && b.Hovered) { _faceNew = -1; return; }
            var d = Root.InverseTransformDirection(_head.transform.forward); float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            int best = Focus; float bd = float.MaxValue;
            for (int i = 0; i < _trees.Count; i++) { if (_trees[i].Extra) continue; float a = Mathf.Abs(Mathf.DeltaAngle(yaw, _trees[i].Azimuth)); if (a < bd) { bd = a; best = i; } }
            float mine = Mathf.Abs(Mathf.DeltaAngle(yaw, _trees[Focus].Azimuth));
            if (best == Focus || bd + FaceMarginDeg > mine) { _faceNew = -1; return; }
            if (best != _faceNew) { _faceNew = best; _faceSince = Time.unscaledTime; return; }
            if (Time.unscaledTime - _faceSince < FaceRest) return;
            Focus = best; _faceNew = -1; Faced++;
            LayoutPanels(); RefreshPanels();
            Debug.Log($"[Tree] facing tree {_trees[Focus].Ring + 1} of {RingCount} ({_trees[Focus].Name}): the plate and the card stand at it");
        }
        /// <summary>How often the faced tree changed (smoke, log).</summary>
        public int Faced { get; private set; }

        private UiButton NodeButton(int skill) { var t = TreeOf(skill); if (t == null) return null; int i = t.Shape.NodeOf[skill]; return i < t.Btns.Count ? t.Btns[i] : null; }

        private void UpdateHover(float dt)
        {
            // the medallion under the laser (the rig's own button under it), else under a fingertip
            int now = 0; string by = ""; var laser = LaserButton != null ? LaserButton() : null;
            if (laser != null)
                foreach (var t in _trees)
                {
                    if (!t.Active) continue;
                    for (int i = 0; i < t.Btns.Count; i++) if (t.Btns[i] == laser) { now = t.Shape.Nodes[i].Skill; by = "laser"; break; }
                    if (now != 0) break;
                }
            if (now == 0) for (int h = 0; h < 2; h++) { int s = TipNode(h); if (s != 0) { now = s; by = "hand"; break; } }
            float time = Time.unscaledTime;
            if (now != _hoverNew) { _hoverNew = now; _hoverSince = time; }
            if (now != 0) _hoverLeft = time;
            // it counts once the pointer has rested; it ends a little after the pointer left
            int want = now != 0 ? (time - _hoverSince >= HoverRest ? now : Hover) : (time - _hoverLeft >= HoverLinger ? 0 : Hover);
            if (want == Hover) return;
            Hover = want; HoverBy = want != 0 ? by : "";
            PlaceMarks(); Redraw(); RefreshPanels();
        }

        /// <summary>The medallion a hand's fingertip is on (0: none): inside its disc, within 3 cm in front of it or 2.5 cm behind.</summary>
        private int TipNode(int hand)
        {
            if (Tip == null) return 0;
            var tip = Tip(hand); if (!tip.HasValue) return 0;
            float r2 = TreeLayout.NodeR * TreeLayout.NodeR;
            foreach (var t in _trees)
            {
                if (!t.Active || t.Shape == null) continue;
                var l = t.Holder.InverseTransformPoint(tip.Value);
                if (Mathf.Abs(l.z) > 80f || Mathf.Abs(l.x) > t.Shape.HalfWidth + 60f) continue;
                foreach (var n in t.Shape.Nodes)
                {
                    float dx = l.x - n.Pos.x, dy = l.y - n.Pos.y, dz = l.z - n.Pos.z;
                    if (dx * dx + dy * dy > r2 || dz < -30f || dz > 25f) continue;
                    int at = t.Shape.NodeOf[n.Skill];
                    if (at < t.Btns.Count && t.Btns[at] != null && !t.Btns[at].gameObject.activeSelf) continue; // (client wave 23: behind a panel: the panel's)
                    return n.Skill;
                }
            }
            return 0;
        }

        /// <summary>The node at the first job's base a hand's fingertip is on (null: none): inside its disc, as a medallion.</summary>
        private UiButton TipBud(int hand)
        {
            var first = RingTree(0); if (Tip == null || first == null || !first.Active) return null;
            var tip = Tip(hand); if (!tip.HasValue) return null;
            var l = first.Holder.InverseTransformPoint(tip.Value);
            foreach (var e in _trees)
            {
                if (!e.Extra || e.Bud == null || !e.Bud.gameObject.activeSelf) continue;
                float dx = l.x - e.BudAt.x, dy = l.y - e.BudAt.y, dz = l.z - e.BudAt.z;
                if (dx * dx + dy * dy <= BudR * BudR && dz >= -30f && dz <= 25f) return e.Bud;
            }
            return null;
        }

        /// <summary>The fingertips press: held <see cref="TipHold"/> s on a medallion or on one of the plate's / the card's buttons, they
        /// press it once (the tip must leave before it presses again).</summary>
        private void UpdateTips(float dt)
        {
            for (int h = 0; h < 2; h++)
            {
                Component on = null; int skill = TipNode(h);
                if (skill != 0) on = NodeButton(skill); else on = (Component)TipBud(h) ?? TipButton(h);
                if (on != _tipOn[h]) { _tipOn[h] = on; _tipSince[h] = Time.unscaledTime; _tipFired[h] = false; }
                if (on == null || _tipFired[h] || Time.unscaledTime - _tipSince[h] < TipHold) continue;
                _tipFired[h] = true; TipPresses++;
                if (on is UiButton b) b.Click();
            }
        }
        public int TipPresses { get; private set; }

        private void OnDisable() { Hover = 0; _hoverNew = 0; _pulled = false; _pullAcc = 0f; }

    }
}
