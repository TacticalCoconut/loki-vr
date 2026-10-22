using System.Collections.Generic;
using Loki.Game;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>A skill's medallion on a 3D skill tree: where it hangs (millimetres in the tree's own frame: UI/TreeMesh.cs).</summary>
    public sealed class TreeNode
    {
        public int Skill, Col, Row, Branch = -1, Index;
        /// <summary>The medallion's middle, and the point of its branch (or of the trunk) it hangs from.</summary>
        public Vector3 Pos, Hang;
        /// <summary>When it pops out while the tree rises (0-1).</summary>
        public float Birth;
        public SkillTreeModel.Cell Cell;
    }
    /// <summary>A branch: one row of one side of a tree.</summary>
    public sealed class TreeBranch
    {
        public int Row, Side, Index;
        public List<Vector3> Path = new List<Vector3>();
        public float B0, B1, Radius;
        /// <summary>The share of its length (0 at the trunk) where each of its medallions hangs.</summary>
        public List<float> HangsAt = new List<float>();
    }
    public struct TreeLink { public int From, To, Level; public bool Met; }

    /// <summary>One job's tree: its medallions, branches and the prerequisite lines between its own skills.</summary>
    public sealed class TreeShape
    {
        public readonly List<TreeNode> Nodes = new List<TreeNode>();
        public readonly List<TreeBranch> Branches = new List<TreeBranch>();
        public readonly List<TreeLink> Links = new List<TreeLink>();
        public readonly Dictionary<int, int> NodeOf = new Dictionary<int, int>();
        /// <summary>The trunk's foot and top, the widest branch's reach, the columns and rows it has, the trunk's time to grow (0-1 of the rise).</summary>
        public float Bottom, Top, HalfWidth, TrunkGrown = 0.42f;
        public int Columns, Rows, Ornament, Job;
        /// <summary>The rows' pitch this tree has (mm), and whether its few skills were put two to a row (not at the grid's own places).</summary>
        public float RowPitch; public bool Packed;
        /// <summary>The tree "Other": its trunk has no crown (the art: UI/TreeArt.cs Sapling).</summary>
        public bool Bare;
        /// <summary>Where the job's name stands: on the trunk under the lowest row.</summary>
        public Vector3 LabelAt;
        /// <summary>When the trunk has grown up to height y.</summary>
        public float TrunkBirth(float y) => Mathf.Clamp01(Mathf.InverseLerp(Bottom, Top, y)) * TrunkGrown;
    }

    /// <summary>
    /// Client wave 22 (w22tree), the design's point 64: where everything of ONE job's tree goes (design note, 2026-10-11: "the tall stack does not look
    /// good ... you have multiple trees that you can flip through, each one representing the skills from each job"; "The skills are also
    /// too bunched up together. Give them a bit more space"). The client data's own grid is kept (skilltreeview.lub: 7 columns, row =
    /// pos / 7, the grid the flat page draws) but only the columns and rows THIS job uses, so a tree is as wide as it needs: its first row
    /// lowest; with an odd number of columns the middle one hangs from the trunk, in front of it, the others from a branch to the left
    /// and one to the right; with an even number half hang on each side. One branch per row and side that has a skill. The tree's own
    /// origin is the middle of its medallions (it stands with that at the eyes' height less 10 cm).
    /// SPACING: columns <see cref="ColPitch"/> mm apart (8.4 deg from 0.65 m; 80 mm = 7.0 deg before), rows <see cref="RowPitch"/> mm
    /// (11.7 deg; 122 mm = 10.6 deg before): 32 mm of air between two medallions on a branch (16 before), 48 mm between a medallion's
    /// level plaque and the medallion under it (36 before); a medallion is 64 mm as before. The widest tree (7 columns) is 75 cm: 60 deg.
    /// </summary>
    public static class TreeLayout
    {
        /// <summary>Millimetres: a medallion's radius, the columns' and the rows' pitch (rows of a tree with six rows or more:
        /// <see cref="RowPitchTall"/>; eight or more, the Super Novice's nine and the Summoner's ten: <see cref="RowPitchGiant"/>), how far the innermost medallions of a tree without a middle column stand from the trunk's axis,
        /// how far a medallion hangs under its branch, the plaque with its level under it, how far in front of the tree's plane the
        /// medallions hang (the middle column: in front of the trunk), the trunk under the lowest row and over the highest.</summary>
        public const float NodeR = 32f, ColPitch = 96f, RowPitch = 134f, RowPitchTall = 118f, RowPitchGiant = 110f, InnerEven = 76f, Hang = 20f, PlaqueH = 19f, PlaqueGap = 3f,
                           NodeZ = -16f, MidZ = -40f, TrunkBelow = 200f, TrunkAbove = 190f, SmallBelow = 104f, SmallAbove = 74f, SmallPitch = 84f;
        public const int Cols = 7;

        /// <summary>A job's tree from its cells (their grid positions as the client data has them).</summary>
        public static TreeShape Make(IList<SkillTreeModel.Cell> cells, int ornament, float branchRadius, bool small = false)
        {
            var s = new TreeShape { Ornament = ornament };
            // (the Novice's sapling: a small tree: a short foot and a short top)
            float below = small ? SmallBelow : TrunkBelow, above = small ? SmallAbove : TrunkAbove;
            if (cells == null || cells.Count == 0) { s.Bottom = -below; s.Top = above; s.HalfWidth = 160f; s.LabelAt = new Vector3(0f, -60f, -44f); return s; }
            // the rows and columns this job uses, squeezed (an empty row or column of the grid is none of the tree)
            var rowSet = new SortedSet<int>(); var colSet = new SortedSet<int>();
            foreach (var c in cells) { rowSet.Add(c.Pos / Cols); colSet.Add(c.Pos % Cols); }
            var rowAt = new Dictionary<int, int>(); foreach (int r in rowSet) rowAt[r] = rowAt.Count;
            var colAt = new Dictionary<int, int>(); foreach (int c in colSet) colAt[c] = colAt.Count;
            int rows = rowAt.Count, cols = colAt.Count;
            // A job with a handful of skills of its own (a transcendent class: its four to six skills stand in the gaps of its second
            // job's grid, which is another tree now; a Novice's three): TWO TO A ROW, left and right of the trunk, in the grid's order;
            // an odd last one hangs from the trunk. (FLAGGED: these few do not keep the grid's own places.)
            // A SMALL tree (the Novice's sapling, the stake "Other"): three to a row, the middle one on the trunk: a low, narrow tree.
            int per = small ? 3 : 2;
            var packed = small || (cells.Count <= 6 && rows > (cells.Count + 1) / 2) ? new List<SkillTreeModel.Cell>(cells) : null;
            if (packed != null) { packed.Sort((a, b) => a.Pos.CompareTo(b.Pos)); rows = (packed.Count + per - 1) / per; cols = Mathf.Min(per, packed.Count); }
            s.Rows = rows; s.Columns = cols; s.Packed = packed != null;
            float pitch = small || rows >= 8 ? RowPitchGiant : rows >= 6 ? RowPitchTall : RowPitch, y0 = -(rows - 1) * pitch * 0.5f;
            s.RowPitch = pitch;
            for (int i = 0; i < cells.Count; i++)
            {
                var c = packed != null ? packed[i] : cells[i]; int row, col; float x;
                if (packed != null && small)
                {
                    row = i / 3; col = i % 3; int inRow = Mathf.Min(3, packed.Count - row * 3);
                    x = (col - (inRow - 1) * 0.5f) * SmallPitch;
                }
                else if (packed != null)
                {
                    row = i / 2; col = i % 2;
                    x = col == 0 ? -InnerEven : InnerEven;
                }
                else
                {
                    row = rowAt[c.Pos / Cols]; col = colAt[c.Pos % Cols];
                    if (cols == 1) x = (row % 2 == 0 ? -1f : 1f) * InnerEven;                         // one column: left and right of the trunk in turn
                    else
                    {
                        int leftCols = cols / 2;
                        x = col < leftCols
                            ? -(InnerEven + (leftCols - 1 - col) * ColPitch)
                            : +(InnerEven + (col - leftCols) * ColPitch);                             // parted around the central trunk sculpture
                    }
                }
                var n = new TreeNode { Skill = c.Skill, Col = col, Row = row, Cell = c, Index = s.Nodes.Count };
                n.Pos = new Vector3(x, y0 + row * pitch, Mathf.Abs(x) < 1f ? MidZ : NodeZ);
                s.Nodes.Add(n); s.NodeOf[c.Skill] = n.Index;
            }
            float lowest = y0, highest = y0 + (rows - 1) * pitch;
            s.Bottom = lowest - below; s.Top = highest + NodeR + Hang + above;
            s.LabelAt = new Vector3(0f, lowest - NodeR - PlaqueGap - PlaqueH - 26f, -44f);

            // the branches: one per row and side that holds a medallion, from the trunk out over its medallions and a little beyond
            for (int row = 0; row < rows; row++)
                for (int side = -1; side <= 1; side += 2)
                {
                    float reach = 0f; var mine = new List<TreeNode>();
                    foreach (var n in s.Nodes) if (n.Row == row && n.Pos.x * side > 1f) { mine.Add(n); reach = Mathf.Max(reach, n.Pos.x * side); }
                    if (mine.Count == 0) continue;
                    float yNode = y0 + row * pitch, yBase = yNode + NodeR + Hang, len = reach + ColPitch * 0.55f;
                    var b = new TreeBranch { Row = row, Side = side, Index = s.Branches.Count, Radius = branchRadius };
                    const int segs = 14;
                    for (int i = 0; i <= segs; i++) { float u = i / (float)segs; b.Path.Add(new Vector3(side * len * u, BranchY(yBase, u, len), 0f)); }
                    b.B0 = s.TrunkBirth(b.Path[0].y); b.B1 = Mathf.Min(0.92f, b.B0 + 0.16f + 0.26f * Mathf.Clamp01(len / 420f));
                    foreach (var n in mine)
                    {
                        float u = Mathf.Clamp01(n.Pos.x * side / len);
                        n.Branch = b.Index; n.Hang = new Vector3(n.Pos.x, BranchY(yBase, u, len) - branchRadius * 0.6f, 0f);
                        n.Birth = Mathf.Lerp(b.B0, b.B1, u) + 0.02f; b.HangsAt.Add(u);
                    }
                    s.Branches.Add(b); s.HalfWidth = Mathf.Max(s.HalfWidth, len);
                }
            // the middle column hangs from the trunk
            foreach (var n in s.Nodes)
                if (n.Branch < 0) { n.Hang = new Vector3(0f, n.Pos.y + NodeR + Hang * 0.7f, n.Pos.z * 0.5f); n.Birth = s.TrunkBirth(n.Hang.y) + 0.04f; }
            s.HalfWidth = Mathf.Max(s.HalfWidth, small ? 130f : 150f);
            // the prerequisites' lines: from the skill needed to the skill that needs it, where both hang on this tree
            foreach (var n in s.Nodes)
                foreach (var (need, lv, met) in n.Cell.Needs)
                    if (s.NodeOf.TryGetValue(need, out int from)) s.Links.Add(new TreeLink { From = from, To = n.Index, Level = lv, Met = met });
            return s;
        }

        /// <summary>A branch's height at share u of its length (0 at the trunk): it leaves the trunk a little higher, runs level over its
        /// medallions and sweeps up at its end (the design's concept: knight-3-simpler.jpg), the more the longer it is.</summary>
        public static float BranchY(float yBase, float u, float len)
        {
            float k = Mathf.Clamp01(len / 400f);
            return yBase + 22f * (1f - u) * (1f - u) + (18f + 26f * k) * u * u * u;
        }
    }
}
