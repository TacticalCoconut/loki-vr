using System;
using System.Collections.Generic;
using System.Linq;
using Loki.Formats;
using Loki.World;

namespace Loki.VrMaps
{
    /// <summary>
    /// the design's OUTER-WALL RULE (2026-10-14) applied to every drawn GND wall of a map: "an OUTER wall (one side walkable
    /// play area, the other the out-of-bounds void) must be see-through from the void side so players can see into the map from
    /// outside (vision, comfort). Only interior walls (playable / visible on both sides) are drawn from both sides. Floors not
    /// affected." The game draws every GND wall from ONE side, a Front wall (a tile's north edge) facing north, a Right wall (east
    /// edge) facing east, whatever its heights (World/GroundMeshBuilder.cs, the walls' fixed normals; Shaders/LokiGround.shader
    /// "Cull Back"): so a wall can be missing from the side players see it from.
    ///
    /// Each wall is one quad on a tile edge, two cells long. Its two SIDES (A: south / west, the tile that owns it; B: north / east):
    ///   playable  a walkable cell (the server's GAT) within <see cref="PlayCells"/> cells straight out from the wall on that side,
    ///             over the wall's two cells and one more each way along it;
    ///   drawn     not playable, but the side's tile has a drawn top (scenery the players see: a dais, a rim, a roof);
    ///   void      neither: nothing is drawn there (the out-of-bounds void).
    /// The classes:
    ///   interior  no side is the void (playable or drawn on both sides) and BOTH its faces can be seen ("playable / visible on both
    ///             sides"): drawn from BOTH sides;
    ///   step      no side is the void, but one face lies under the higher side's own drawn floor (a step, a dais's front, a platform's
    ///             rim): only its other face can be seen from anywhere above the floors: drawn toward that (low) side only. (Showcase 1,
    ///             2026-10-14: drawing such a wall from both sides added nothing seen from above and hung the covered faces in the sky as
    ///             floating ribbons seen from below the map: the catastrophic mistake this class fixes.)
    ///   outer     one side playable, the other the void, and the wall stands up from the playable floor (the void side is as high or
    ///             higher): drawn toward the playable side ONLY, see-through from the void (the design's rule);
    ///   ledge     one side playable, the other the void, and the wall drops from the playable floor (the void side is lower: a platform's
    ///             rim, a cliff edge): FLAGGED, see <see cref="LedgeRule"/>;
    ///   edge      one side drawn scenery (nobody walks near it), the other the void: the rule does not name it; left as the game draws it;
    ///   hidden    the void on both sides: left as it is.
    /// A wall under <see cref="MinSpan"/> cells high on both its cells is classified and fixed like the others (drawing a low step
    /// from both sides costs nothing), but it is not counted as a problem players see.
    /// </summary>
    public sealed class Walls
    {
        public enum Side : byte { Void = 0, Drawn = 1, Playable = 2 }
        public enum Class : byte { Interior, Step, Outer, Ledge, Edge, Hidden }
        /// <summary>Which faces a wall shows: toward B (the game's default: north / east), toward A (south / west), or both.</summary>
        public enum Faces : byte { None = 0, TowardB = 1, TowardA = 2, Both = 3 }

        /// <summary>How far (cells) from a wall a walkable cell makes that side playable: RoomTools.WallCells, the floor's rim and walls
        /// (Data/RoomTools.cs: "non-walkable cells within this many cells of a room's walkable cells").</summary>
        public const int PlayCells = Loki.Data.RoomTools.WallCells;
        /// <summary>A wall lower than this (cells) on both its cells is not seen as a wall (the analyzer's own threshold: walk.wall_on_path,
        /// back.gnd_wall: 0.3 cells = 21 cm in first person).</summary>
        public const float MinSpan = Views.Thresholds.WallOnPathMedium;

        /// <summary>
        /// FLAGGED (the design's rule does not name this case): a LEDGE is an outer wall that drops from the playable floor into the void (the
        /// walkable side is the high one). Its face toward the playable side lies UNDER that side's own floor, where no view reaches; its
        /// only face anyone sees is the one toward the void (from outside, from the Table view, from below). Drawing it "see-through
        /// from the void side" would leave it invisible from everywhere (the platform or cliff hollow), and it hides nothing of the play
        /// area (it is below it), so the rule's purpose (seeing into the map from outside) is not at stake.
        ///   "low"  (default) drawn toward the void (its low side) only;  "keep"  left as the game draws it;  "rule"  the literal rule:
        ///   toward the playable side only.
        /// </summary>
        public static string LedgeRule = "low";

        public sealed class Wall
        {
            public int Tile, Tx, Ty; public bool Front;
            public Side A, B;
            /// <summary>The wall's height (cells) on each side's edge, the mean of its two corners (Loki y), and the largest height
            /// difference along it.</summary>
            public float HA, HB, Span;
            public Faces Drawn;     // as the scene draws it now
            public Class Cls;
            public Faces Want;      // what the rule wants
            /// <summary>The walkable cells on each side within reach (for the report's "most changed" ranking).</summary>
            public int PlayA, PlayB;
            /// <summary>At least <see cref="MinSpan"/> high somewhere: a wall players see as one.</summary>
            public bool Seen => Span >= MinSpan;
            /// <summary>Drawn differently from what the rule wants: missing from a side players see, or an outer wall opaque from the void.</summary>
            public bool Wrong => Want != Drawn;
            /// <summary>A problem players see: a seen wall missing a face the rule wants drawn, where that face can be seen at all.</summary>
            public bool Problem => Seen && ((Missing & Faces.TowardA) != 0 && FaceVisible(Faces.TowardA) || (Missing & Faces.TowardB) != 0 && FaceVisible(Faces.TowardB));
            /// <summary>Whether the wall's face toward a side can be seen from anywhere above the floors: not when that side stands higher
            /// over the whole wall and its top is drawn (the face is then under that side's own floor).</summary>
            public bool FaceVisible(Faces side)
            {
                bool towardA = side == Faces.TowardA;
                float mine0 = towardA ? A0 : B0, mine1 = towardA ? A1 : B1, other0 = towardA ? B0 : A0, other1 = towardA ? B1 : A1;
                bool covered = (towardA ? TopA : TopB) && mine0 > other0 + 1e-3f && mine1 > other1 + 1e-3f;
                return !covered;
            }
            /// <summary>The edge's corner heights (cells, Loki y) on each side, and whether each side's tile has a drawn top.</summary>
            public float A0, A1, B0, B1; public bool TopA, TopB;
            /// <summary>The side the wall is missing from although players see it (Want minus Drawn).</summary>
            public Faces Missing => Want & ~Drawn;
            /// <summary>Faces drawn that the rule does not want (an outer wall opaque from the void side).</summary>
            public Faces Extra => Cls == Class.Outer || Cls == Class.Ledge ? Drawn & ~Want : Faces.None;
            /// <summary>The wall's middle in map cells (x east, y north).</summary>
            public float Cx => Front ? Tx * 2 + 1f : Tx * 2 + 2f;
            public float Cy => Front ? Ty * 2 + 2f : Ty * 2 + 1f;
            public string Name => $"{(Front ? "N" : "E")} edge of tile {Tx},{Ty}";
        }

        public readonly List<Wall> All = new List<Wall>();

        /// <summary>(warch2) The faces a fix mode draws for a wall the game draws toward <paramref name="drawn"/> (World/GroundMeshBuilder.cs:
        /// Keep the game's own, Both both, Flip the south / west face, Front the north / east one), and the mode that draws a set of faces
        /// (Keep when it is the game's own; null when no mode draws it).</summary>
        public static Faces FacesOf(Loki.World.MapFix.WallMode m, Faces drawn) =>
            m == Loki.World.MapFix.WallMode.Keep ? drawn : m == Loki.World.MapFix.WallMode.Both ? Faces.Both : m == Loki.World.MapFix.WallMode.Flip ? Faces.TowardA : Faces.TowardB;
        public static Loki.World.MapFix.WallMode? ModeFor(Faces want, Faces drawn) =>
            want == drawn ? Loki.World.MapFix.WallMode.Keep : want == Faces.Both ? Loki.World.MapFix.WallMode.Both
            : want == Faces.TowardA ? Loki.World.MapFix.WallMode.Flip : want == Faces.TowardB ? Loki.World.MapFix.WallMode.Front : (Loki.World.MapFix.WallMode?)null;

        public Walls(Scene s)
        {
            var gnd = s.Gnd; int gw = gnd.Width, gh = gnd.Height;
            // the faces the scene draws: a ground wall triangle's normal points to its drawn side (+z / +x toward B)
            var front = new Faces[gw * gh]; var right = new Faces[gw * gh];
            for (int t = 0; t < s.Count; t++)
            {
                // (warch2) a degenerate triangle (no area: the wall has no height at that end) has no facing: it says nothing
                if (s.Kind[t] == TriKind.WallFront) { float nz = s.N[t * 3 + 2]; if (nz != 0f) front[s.Src[t]] |= nz > 0 ? Faces.TowardB : Faces.TowardA; }
                else if (s.Kind[t] == TriKind.WallRight) { float nx = s.N[t * 3]; if (nx != 0f) right[s.Src[t]] |= nx > 0 ? Faces.TowardB : Faces.TowardA; }
            }
            for (int ty = 0; ty < gh; ty++)
                for (int tx = 0; tx < gw; tx++)
                    for (int e = 0; e < 2; e++)
                    {
                        bool isFront = e == 0; int k = tx + ty * gw;
                        var drawn = isFront ? front[k] : right[k];
                        if (drawn == Faces.None) continue;
                        if (isFront ? ty + 1 >= gh : tx + 1 >= gw) continue;
                        var a = gnd.Tiles[k]; var b = isFront ? gnd.Tiles[k + gw] : gnd.Tiles[k + 1];
                        // RO heights (y-down) of the shared edge: A's north / east corners, B's south / west corners
                        float a0 = isFront ? a.H2 : a.H1, a1 = a.H3, b0 = b.H0, b1 = isFront ? b.H1 : b.H2;
                        float ha = -(a0 + a1) * 0.5f * RoSpace.Scale, hb = -(b0 + b1) * 0.5f * RoSpace.Scale;
                        float span = Math.Max(Math.Abs(a0 - b0), Math.Abs(a1 - b1)) * RoSpace.Scale;
                        var w = new Wall { Tile = k, Tx = tx, Ty = ty, Front = isFront, Drawn = drawn, HA = ha, HB = hb, Span = span,
                            A0 = -a0 * RoSpace.Scale, A1 = -a1 * RoSpace.Scale, B0 = -b0 * RoSpace.Scale, B1 = -b1 * RoSpace.Scale };
                        w.PlayA = Playable(s, w, -1); w.PlayB = Playable(s, w, +1);
                        w.A = w.PlayA > 0 ? Side.Playable : s.TopDrawn[k] ? Side.Drawn : Side.Void;
                        int kb = isFront ? k + gw : k + 1;
                        w.B = w.PlayB > 0 ? Side.Playable : s.TopDrawn[kb] ? Side.Drawn : Side.Void;
                        w.TopA = s.TopDrawn[k]; w.TopB = s.TopDrawn[kb];
                        Classify(w);
                        All.Add(w);
                    }
        }

        /// <summary>Walkable cells straight out from the wall on one side (-1: A, south / west; +1: B, north / east), up to
        /// <see cref="PlayCells"/> deep, over the wall's two cells and one more each way along it.</summary>
        private static int Playable(Scene s, Wall w, int side)
        {
            int n = 0;
            for (int d = 1; d <= PlayCells; d++)
                for (int along = -1; along <= 2; along++)
                {
                    int x, y;
                    if (w.Front) { x = w.Tx * 2 + along; y = side > 0 ? w.Ty * 2 + 1 + d : w.Ty * 2 + 2 - d; }
                    else { y = w.Ty * 2 + along; x = side > 0 ? w.Tx * 2 + 1 + d : w.Tx * 2 + 2 - d; }
                    if (s.Walkable(x, y)) n++;
                }
            return n;
        }

        private static void Classify(Wall w)
        {
            if (w.A != Side.Void && w.B != Side.Void)
            {
                bool va = w.FaceVisible(Faces.TowardA), vb = w.FaceVisible(Faces.TowardB);
                if (va && vb) { w.Cls = Class.Interior; w.Want = Faces.Both; return; }
                w.Cls = Class.Step; w.Want = va ? Faces.TowardA : Faces.TowardB; return;
            }
            if (w.A == Side.Void && w.B == Side.Void) { w.Cls = Class.Hidden; w.Want = w.Drawn; return; }
            bool pa = w.A == Side.Playable, pb = w.B == Side.Playable;
            if (!pa && !pb) { w.Cls = Class.Edge; w.Want = w.Drawn; return; }
            // one side playable, the other the void
            var toPlay = pa ? Faces.TowardA : Faces.TowardB; var toVoid = pa ? Faces.TowardB : Faces.TowardA;
            float hp = pa ? w.HA : w.HB, hv = pa ? w.HB : w.HA;
            if (hv >= hp) { w.Cls = Class.Outer; w.Want = toPlay; return; }
            w.Cls = Class.Ledge;
            w.Want = LedgeRule == "keep" ? w.Drawn : LedgeRule == "rule" ? toPlay : toVoid;
        }

        public int Count(Class c) => All.Count(w => w.Cls == c);
        /// <summary>One line for the report's facts.</summary>
        public string Summary()
        {
            string Part(Class c) { var ws = All.Where(w => w.Cls == c).ToList(); return $"{ws.Count} {c.ToString().ToLowerInvariant()} ({ws.Count(w => w.Problem)} missing from a side players see{(c == Class.Outer || c == Class.Ledge ? $", {ws.Count(w => w.Extra != Faces.None)} drawn toward the void" : "")})"; }
            return $"{All.Count} drawn walls (the design's outer-wall rule): {Part(Class.Interior)}, {Part(Class.Step)}, {Part(Class.Outer)}, {Part(Class.Ledge)}, {Count(Class.Edge)} edge, {Count(Class.Hidden)} hidden; ledge rule '{LedgeRule}'";
        }
        public int Problems(Class c) => All.Count(w => w.Cls == c && w.Problem);

        /// <summary>The counter (design note, 2026-10-14: "apply the outer wall counter"): walls per class, how many are drawn as the rule wants,
        /// how many are missing from a side players see, and how many outer walls are drawn toward the void.</summary>
        public Dictionary<string, object> Counter()
        {
            var d = new Dictionary<string, object>();
            foreach (Class c in Enum.GetValues(typeof(Class)))
            {
                var ws = All.Where(w => w.Cls == c).ToList();
                d[c.ToString().ToLowerInvariant()] = new Dictionary<string, object>
                {
                    ["walls"] = ws.Count,
                    ["asTheRuleWants"] = ws.Count(w => w.Drawn == w.Want),
                    ["seen"] = ws.Count(w => w.Seen),
                    ["missingFromASideSeen"] = ws.Count(w => w.Problem),
                    ["drawnTowardTheVoid"] = ws.Count(w => w.Extra != Faces.None),
                };
            }
            d["ledgeRule"] = LedgeRule;
            return d;
        }
    }
}
