using System.Collections.Generic;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 22 (w22tree), the design's point 64: the 3D skill tree's art, all made in code (UI/TreeMesh.cs): the trunk object of the job
    /// family, the branches, their leaves, twigs, scrolls and tips, and what hangs from them, as plain or as ornate as the ornament
    /// level says (UI/TreeThemes.cs TreeGrowth.Stages: the one table). the design's concepts (the UI theme palette): Swordsman a
    /// sword with banners, Mage a wood-and-crystal staff with rune rings, Acolyte a marble-and-gold pillar with stained-glass leaves,
    /// Archer a longbow with a quiver and green twigs, Thief crossed daggers bound in purple cloth with thorns and smoke, Merchant an
    /// anvil and cart pole with brass pipes and scales. LOKI'S PROPOSALS in the same grammar (flagged): Novice / Super Novice a staked
    /// sapling with a sign (ribbons and an angel's wings when grown), Gunslinger a rifle with bullets and pennants, Ninja a sheathed
    /// katana with bamboo, maple leaves and paper talismans, Taekwon a training post tied with a belt whose colour rises with the tree,
    /// Summoner a catnip stalk with a fishing rod, fish and flowers.
    /// Built back to front (the shader tests no depth): the light on the floor, what stands behind, the branches, the strings, the
    /// trunk, what hangs in front.
    /// </summary>
    public static class TreeArt
    {
        /// <summary>What a built tree holds (the smoke compares the stages by these).</summary>
        public sealed class Counts { public int Leaves, Twigs, Hangings, Curls, Finials, TrunkParts, Branches, Pieces, Triangles; }

        public static System.Func<TreeMeshBuilder, TreeShape, TreeTheme, Counts, bool> PreviewPlinthHook;
        public static System.Func<TreeMeshBuilder, TreeShape, TreeTheme, TreeGrowth.Stage, Counts, bool> PreviewBehindHook;
        public static System.Func<TreeMeshBuilder, TreeShape, TreeTheme, TreeGrowth.Stage, Counts, bool> PreviewTrunkHook;

        private static Color32 C(int r, int g, int b, int a = 255) => new Color32((byte)Mathf.Clamp(r, 0, 255), (byte)Mathf.Clamp(g, 0, 255), (byte)Mathf.Clamp(b, 0, 255), (byte)a);
        private static Color32 Shade(Color32 c, float k) => C(Mathf.RoundToInt(c.r * k), Mathf.RoundToInt(c.g * k), Mathf.RoundToInt(c.b * k), c.a);
        private static Color32 Mix(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, t);
        private static Color32 Alpha(Color32 c, int a) => new Color32(c.r, c.g, c.b, (byte)a);
        private static Vector3 V(float x, float y, float z = 0f) => new Vector3(x, y, z);

        /// <summary>Builds one job's tree into <paramref name="b"/> (back to front) and the light on its floor into <paramref name="glow"/>.
        /// The trunk object's vertices are marked (TreeMeshBuilder.TrunkPart): the family's rise moves them as one thing.</summary>
        public static Counts Build(TreeMeshBuilder b, TreeMeshBuilder glow, TreeShape s, TreeTheme th)
        {
            var st = TreeGrowth.StageOf(s.Ornament); var k = new Counts();
            Floor(glow, s, th);
            if (st.Base) Plinth(b, s, th, k);
            Behind(b, s, th, st, k);
            foreach (var br in s.Branches) { Branch(b, s, br, th, st, k); k.Branches++; }
            // the strings the medallions hang by
            b.Plain(); b.Pen(Shade(th.Wood, 0.8f), 0.2f);
            foreach (var n in s.Nodes) b.Line(n.Hang, V(n.Pos.x, n.Pos.y + TreeLayout.NodeR - 2f, n.Hang.z), 2.2f, n.Birth - 0.02f, n.Birth);
            b.TrunkPart = true; Trunk(b, s, th, st, k); b.TrunkPart = false;
            Hangings(b, s, th, st, k);
            b.Plain();
            k.Pieces = b.Pieces; k.Triangles = b.Triangles + glow.Triangles;
            return k;
        }

        // ------------------------------------------------------------------ outlines (a plate's shape in its own flat frame)
        private static List<Vector2> LeafShape(float len, float w) => new List<Vector2> { new Vector2(0, 0), new Vector2(w * 0.42f, len * 0.22f), new Vector2(w * 0.5f, len * 0.48f), new Vector2(w * 0.3f, len * 0.78f), new Vector2(0, len), new Vector2(-w * 0.3f, len * 0.78f), new Vector2(-w * 0.5f, len * 0.48f), new Vector2(-w * 0.42f, len * 0.22f) };
        private static List<Vector2> Diamond(float w, float h) => new List<Vector2> { new Vector2(0, 0), new Vector2(w * 0.5f, h * 0.5f), new Vector2(0, h), new Vector2(-w * 0.5f, h * 0.5f) };
        private static List<Vector2> Banner(float w, float h, float notch) => new List<Vector2> { new Vector2(-w * 0.5f, 0), new Vector2(w * 0.5f, 0), new Vector2(w * 0.5f, -h), new Vector2(0, -h + notch), new Vector2(-w * 0.5f, -h) };
        private static List<Vector2> Pennant(float w, float h) => new List<Vector2> { new Vector2(-w * 0.5f, 0), new Vector2(w * 0.5f, 0), new Vector2(0, -h) };
        private static List<Vector2> Torn(float w, float h) => new List<Vector2> { new Vector2(-w * 0.5f, 0), new Vector2(w * 0.5f, 0), new Vector2(w * 0.42f, -h * 0.8f), new Vector2(w * 0.2f, -h * 0.66f), new Vector2(w * 0.05f, -h), new Vector2(-w * 0.2f, -h * 0.72f), new Vector2(-w * 0.45f, -h * 0.9f) };
        private static List<Vector2> RectShape(float w, float h) => new List<Vector2> { new Vector2(-w * 0.5f, -h * 0.5f), new Vector2(w * 0.5f, -h * 0.5f), new Vector2(w * 0.5f, h * 0.5f), new Vector2(-w * 0.5f, h * 0.5f) };
        private static List<Vector2> ShieldShape(float w, float h) => new List<Vector2> { new Vector2(-w * 0.5f, h * 0.5f), new Vector2(w * 0.5f, h * 0.5f), new Vector2(w * 0.5f, 0), new Vector2(w * 0.3f, -h * 0.3f), new Vector2(0, -h * 0.5f), new Vector2(-w * 0.3f, -h * 0.3f), new Vector2(-w * 0.5f, 0) };
        private static List<Vector2> Star(int points, float rOut, float rIn)
        {
            var l = new List<Vector2>(points * 2);
            for (int i = 0; i < points * 2; i++) { float a = i * Mathf.PI / points, r = i % 2 == 0 ? rOut : rIn; l.Add(new Vector2(Mathf.Sin(a) * r, Mathf.Cos(a) * r)); }
            return l;
        }
        private static List<Vector2> Gear(int teeth, float rOut, float rIn)
        {
            var l = new List<Vector2>(teeth * 4);
            for (int i = 0; i < teeth; i++)
            {
                float a = i * Mathf.PI * 2f / teeth, d = Mathf.PI * 2f / teeth;
                foreach (var (f, r) in new[] { (0.05f, rIn), (0.2f, rOut), (0.45f, rOut), (0.6f, rIn) }) l.Add(new Vector2(Mathf.Sin(a + d * f) * r, Mathf.Cos(a + d * f) * r));
            }
            return l;
        }
        private static List<Vector2> Fish(float a, float h) => new List<Vector2> { new Vector2(-a, 0), new Vector2(-a * 0.35f, h), new Vector2(a * 0.45f, h * 0.35f), new Vector2(a, h * 0.85f), new Vector2(a * 0.8f, 0), new Vector2(a, -h * 0.85f), new Vector2(a * 0.45f, -h * 0.35f), new Vector2(-a * 0.35f, -h) };

        /// <summary>A plate hung so that its local +y points along <paramref name="leanDeg"/> (degrees clockwise from up, as the player sees it).</summary>
        private static Matrix4x4 Lean(Vector3 at, float leanDeg, float scale = 1f) => TreeMeshBuilder.At(at, -leanDeg, scale);

        // ------------------------------------------------------------------ small parts
        private static void LeafPlate(TreeMeshBuilder b, Vector3 at, float lean, float len, float w, Color32 col, float gloss, float glow, float birth, Color32? edge = null, float life = 0f, bool bud = false, float lifeFrom = 0f, bool own = false)
        { b.Alive(life, lifeFrom, bud, 0f, own); b.Pen(col, gloss, glow); b.Plate(LeafShape(len, w), Lean(at, lean), 1.6f, birth, Vector2.zero, new Vector2(0, len * 0.45f), edge); b.Plain(); b.Pieces++; }
        private static void Shard(TreeMeshBuilder b, Vector3 at, float lean, float len, float hw, Color32 col, float glow, float birth, float breath = 0f, bool bud = false, float lifeFrom = 0f)
        {
            float r = lean * Mathf.Deg2Rad; var d = V(Mathf.Sin(r), Mathf.Cos(r));
            b.Alive(0.85f, lifeFrom, bud, breath, true); b.Pen(Shade(col, 0.7f), 0.95f, glow * 0.6f); int first = b.V.Count; // (a crystal: dim, then its own colour and light, steadily brighter with the tree's liveliness)
            b.Blade(at, d, V(d.y, -d.x), len, x => x < 0.3f ? hw * (0.5f + x / 0.6f) : hw * (1f - (x - 0.3f) / 0.7f), x => hw * 0.75f * (1f - x * 0.8f), 3, birth, birth);
            for (int i = first; i < b.V.Count; i++) b.Grow[i] = new Vector4(at.x, at.y, at.z, birth); // out of its foot
            b.Plain(); b.Pieces++;
        }
        private static void Bead(TreeMeshBuilder b, Vector3 at, float r, Color32 col, float gloss, float glow, float birth)
        {
            // (a gem, an orb: dim, then its own colour and light; a lantern-bright one breathes where its family does)
            if (glow > 0.01f) { b.Alive(0.9f, 0f, false, glow >= 0.4f ? 1f : 0f, true); b.Pen(Shade(col, 0.72f), gloss, glow * 0.6f); } else b.Pen(col, gloss, glow);
            b.Ball(at, r, 8, 4, birth); b.Plain(); b.Pieces++;
        }
        private static void StarPlate(TreeMeshBuilder b, Vector3 at, int points, float rOut, float rIn, float lean, Color32 col, float gloss, float glow, float birth, float lifeFrom = 0f, bool bud = false, bool own = false)
        { b.Alive(0.7f, lifeFrom, bud, 0f, own); b.Pen(col, gloss, glow); b.Plate(Star(points, rOut, rIn), Lean(at, lean), 1.8f, birth, Vector2.zero, Vector2.zero); b.Plain(); b.Pieces++; }
        private static void Tip(TreeMeshBuilder b, Vector3 at, Vector3 dir, float len, float hw, Color32 col, float gloss, float birth)
        {
            dir.z = 0f; dir.Normalize(); b.Pen(col, gloss);
            b.Blade(at, dir, V(dir.y, -dir.x), len, x => x < 0.25f ? hw * (0.35f + x / 0.385f) : hw * (1f - (x - 0.25f) / 0.75f), x => hw * 0.4f * (1f - x), 3, birth, birth + 0.03f);
            b.Pieces++;
        }
        private static void Tassel(TreeMeshBuilder b, Vector3 top, float len, Color32 col, float birth)
        { b.Pen(col, 0.1f); b.Lathe(top, Vector3.down, new[] { new Vector2(0, 0.6f), new Vector2(len * 0.2f, 2.6f), new Vector2(len, 3.4f) }, 6, birth, birth + 0.02f); b.Pieces++; }
        /// <summary>A hoop round the trunk at a height, tilted: its far half (drawn before the trunk) or its near half (after it).</summary>
        private static void Hoop(TreeMeshBuilder b, Vector3 mid, float rx, float rz, float tiltDeg, float tube, bool near, Color32 col, float gloss, float glow, float birth)
        {
            var p = new List<Vector3>(13); var q = Quaternion.Euler(26f, 0f, tiltDeg);
            for (int i = 0; i <= 12; i++) { float a = (near ? Mathf.PI : 0f) + i * Mathf.PI / 12f; p.Add(mid + q * V(Mathf.Cos(a) * rx, 0f, Mathf.Sin(a) * rz)); }
            b.Alive(1f); b.Pen(col, gloss, glow); b.Tube(p, tube, tube, 5, birth, birth + 0.03f, false, false); b.Plain();
            if (near) b.Pieces++;
        }
        private static Vector3 PathAt(List<Vector3> p, float u, out Vector3 tangent)
        {
            float f = Mathf.Clamp01(u) * (p.Count - 1); int i = Mathf.Min((int)f, p.Count - 2);
            tangent = (p[i + 1] - p[i]).normalized;
            return Vector3.Lerp(p[i], p[i + 1], f - i);
        }

        // ------------------------------------------------------------------ the floor and the foot
        private static void Floor(TreeMeshBuilder g, TreeShape s, TreeTheme th)
        {
            // a pool of light on the floor under the tree (every stage: the concept's glow), seen from above as a flat oval
            float y = s.Bottom - 14f, rx = Mathf.Min(260f, s.HalfWidth * 0.62f), rz = rx * 0.5f; var mid = V(0, y, 0);
            g.Pen(Alpha(th.Glow, 110), 0f, 1f); int c = g.Vert(mid, Vector3.up, mid, 0f);
            g.Pen(Alpha(th.Glow, 0), 0f, 1f); int first = g.V.Count; const int n = 20;
            for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2f / n; g.Vert(mid + V(Mathf.Cos(a) * rx, 0f, Mathf.Sin(a) * rz), Vector3.up, mid, 0.02f); }
            for (int i = 0; i < n; i++) g.Tri(c, first + i, first + (i + 1) % n);
        }
        private static void Plinth(TreeMeshBuilder b, TreeShape s, TreeTheme th, Counts k)
        {
            if (PreviewPlinthHook != null && PreviewPlinthHook(b, s, th, k)) return;
            if (th.Family == TreeFamily.Merchant || th.Family == TreeFamily.Thief) return; // (the anvil / the crossed daggers are the foot)
            b.Pen(Shade(th.Metal, 0.62f), 0.35f);
            b.Lathe(V(0, s.Bottom - 22f, 0), Vector3.up, new[] { new Vector2(0, 0), new Vector2(0, 74), new Vector2(9, 70), new Vector2(9, 52), new Vector2(17, 48), new Vector2(17, 0) }, 14, 0f, 0.03f, false);
            if (TreeGrowth.StageOf(s.Ornament).GoldEdge) { b.Pen(th.Gold, 0.9f); b.Lathe(V(0, s.Bottom - 14.5f, 0), Vector3.up, new[] { new Vector2(0, 71.5f), new Vector2(1.5f, 73), new Vector2(3, 71.5f) }, 14, 0.02f, 0.03f); }
            b.Pieces++; k.TrunkParts++;
        }

        // ------------------------------------------------------------------ a branch
        private static void Branch(TreeMeshBuilder b, TreeShape s, TreeBranch br, TreeTheme th, TreeGrowth.Stage st, Counts k)
        {
            var p = br.Path; int side = br.Side; float r0 = st.Radius, b0 = br.B0, b1 = br.B1;
            float len = Mathf.Abs(p[p.Count - 1].x - p[0].x);
            var kind = th.Branch; var curlCol = th.Gold;
            switch (kind)
            {
                case TreeTheme.BranchKind.Twig:
                {
                    var w = new List<Vector3>(p.Count);
                    for (int i = 0; i < p.Count; i++) { float u = i / (float)(p.Count - 1); w.Add(p[i] + V(0, Mathf.Sin(u * 9f + br.Index) * 3.2f * u, 0)); }
                    b.Pen(th.Wood, th.BranchGloss); b.Tube(w, x => r0 * (1f - 0.62f * x), 5, b0, b1); curlCol = th.LeafA; break;
                }
                case TreeTheme.BranchKind.Thorn:
                    b.Pen(th.Wood, 0.25f); b.Tube(p, x => r0 * (0.95f - 0.6f * x + 0.12f * Mathf.Sin(x * 22f + br.Index)), 5, b0, b1); curlCol = th.LeafB; break;
                case TreeTheme.BranchKind.Antler:
                {
                    // wood out of the trunk, then crystal: straight pieces with a kink between them
                    var a = new List<Vector3>(); for (int i = 0; i <= 4; i++) a.Add(p[i]);
                    b.Pen(th.Wood, 0.2f); b.Tube(a, r0, r0 * 0.8f, 5, b0, Mathf.Lerp(b0, b1, 0.3f), false, false);
                    var c = new List<Vector3>(); for (int i = 4; i < p.Count; i += 2) c.Add(p[i] + V(0, (i / 2) % 2 == 0 ? 3f : -2f, 0)); c[c.Count - 1] = p[p.Count - 1];
                    b.Pen(th.LeafA, 0.95f, 0.45f); b.Tube(c, x => r0 * 0.85f * (1f - 0.6f * x), 4, Mathf.Lerp(b0, b1, 0.3f), b1); curlCol = th.LeafB; break;
                }
                case TreeTheme.BranchKind.Gothic:
                    b.Pen(th.Wood, th.BranchGloss); b.Tube(p, x => r0 * 0.9f * (1f - 0.5f * x), 6, b0, b1, false, true, 0.6f);
                    // tracery: a small pointed arch under the branch between the trunk and each medallion's string
                    if (st.Trunk >= 1)
                    {
                        float prev = 0f;
                        foreach (float u in br.HangsAt)
                        {
                            if (u - prev > 0.12f)
                            {
                                var a0 = PathAt(p, prev + 0.02f, out _); var a1 = PathAt(p, u - 0.02f, out _); var mid = (a0 + a1) * 0.5f + V(0, -5f - 0.16f * Mathf.Abs(a1.x - a0.x), 0);
                                b.Pen(th.Gold, 0.9f); b.Tube(TreeMeshBuilder.Curve(a0, mid, a1, 6), 1.3f, 1.3f, 4, Mathf.Lerp(b0, b1, u), Mathf.Lerp(b0, b1, u) + 0.03f, false, false); b.Pieces++;
                            }
                            prev = u;
                        }
                    }
                    break;
                case TreeTheme.BranchKind.Pipe:
                case TreeTheme.BranchKind.Rod:
                case TreeTheme.BranchKind.Bamboo:
                {
                    float rr = r0 * (kind == TreeTheme.BranchKind.Pipe ? 0.92f : 0.74f);
                    b.Pen(th.Wood, th.BranchGloss); b.Tube(p, x => rr * (kind == TreeTheme.BranchKind.Bamboo ? 1f - 0.35f * x : 1f), 6, b0, b1);
                    // its joints: a pipe's flanges, a rod's knuckles, the bamboo's rings
                    float pitch = kind == TreeTheme.BranchKind.Pipe ? 72f : kind == TreeTheme.BranchKind.Rod ? 96f : 54f;
                    int joints = Mathf.Max(1, Mathf.FloorToInt(len / pitch));
                    for (int j = 1; j <= joints; j++)
                    {
                        float u = j / (joints + 0.6f); var at = PathAt(p, u, out var tan); float jb = Mathf.Lerp(b0, b1, u);
                        float jr = rr * (kind == TreeTheme.BranchKind.Bamboo ? (1f - 0.35f * u) : 1f);
                        b.Pen(kind == TreeTheme.BranchKind.Bamboo ? Shade(th.Wood, 0.72f) : kind == TreeTheme.BranchKind.Rod ? th.Gold : Shade(th.Wood, 1.12f), 0.8f);
                        if (kind == TreeTheme.BranchKind.Rod) b.Ball(at, jr * 1.5f, 6, 3, jb);
                        else b.Lathe(at - tan * 2.5f, tan, new[] { new Vector2(0, jr), new Vector2(0, jr * 1.42f), new Vector2(5, jr * 1.42f), new Vector2(5, jr) }, 6, jb, jb + 0.02f, false);
                        b.Pieces++;
                    }
                    if (kind == TreeTheme.BranchKind.Bamboo) curlCol = th.Wood;
                    break;
                }
                default: // Arc: the swordsman's smooth steel
                    b.Pen(th.Wood, th.BranchGloss); b.Tube(p, x => r0 * (1f - 0.5f * x), 7, b0, b1); break;
            }

            // a gold line along its top (the higher stages' scrollwork edge)
            if (st.GoldEdge)
            {
                var e = new List<Vector3>(p.Count);
                for (int i = 0; i < p.Count; i++) { float u = i / (float)(p.Count - 1); e.Add(p[i] + V(0, r0 * (1f - 0.5f * u) * 0.9f, -r0 * 0.45f)); }
                b.Pen(th.Family == TreeFamily.Acolyte ? th.Trunk : th.Gold, 0.95f); b.Tube(e, r0 * 0.3f, r0 * 0.12f, 4, b0, b1, false, true); b.Pieces++;
            }

            // twigs: a short shoot off its top with a leaf
            for (int t = 0; t < st.Twigs; t++)
            {
                float u = t == 0 ? 0.44f : t == 1 ? 0.74f : 0.2f; var at = PathAt(p, u, out _) + V(0, r0 * 0.6f, 0); float tb = Mathf.Lerp(b0, b1, u) + 0.03f;
                var end = at + V(side * 24f, 38f, 0);
                b.Pen(kind == TreeTheme.BranchKind.Antler ? th.LeafB : th.Wood, th.BranchGloss, kind == TreeTheme.BranchKind.Antler ? 0.4f : 0f);
                b.Tube(TreeMeshBuilder.Curve(at, at + V(side * 5f, 24f, 0), end, 5), r0 * 0.42f, r0 * 0.2f, 4, tb, tb + 0.05f);
                Leaf(b, th, end, side * 28f, 1.05f, t + br.Index, tb + 0.06f); k.Twigs++; k.Leaves++; b.Pieces++;
            }
            // leaves along its top, leaning outwards
            for (int i = 0; i < st.Leaves; i++)
            {
                float u = (i + 0.75f) / (st.Leaves + 0.5f); var at = PathAt(p, u, out _) + V(0, r0 * (1f - 0.5f * u) * 0.7f, 0);
                Leaf(b, th, at, side * (24f + 26f * ((i + br.Index) % 2)), 0.9f + 0.18f * ((i * 7 + br.Index) % 3), i + br.Index * 3, Mathf.Lerp(b0, b1, u) + 0.04f); k.Leaves++;
            }
            var tip = PathAt(p, 1f, out var tipDir);
            // scrolls: one off the tip, a second off the branch's top (unfurling as skill points are spent in this tree)
            if (st.Curls)
            {
                float curlFrom = 0.22f + 0.54f * ((br.Index % 5) / 4f);
                b.Alive(0.85f, curlFrom, true, 0f, true);
                b.Pen(curlCol, 0.9f, kind == TreeTheme.BranchKind.Antler ? 0.4f : 0f);
                var start = PathAt(p, 0.9f, out var d9) + V(0, r0 * 0.4f, 0);
                b.Tube(TreeMeshBuilder.Scroll(start, (d9 + V(0, 0.9f, 0)).normalized, 15f + 0.5f * s.Ornament, 1.3f, side < 0, 16), r0 * 0.42f, r0 * 0.14f, 5, b1 - 0.02f, b1 + 0.06f); b.Pieces++; k.Curls++;
                if (st.DoubleCurls)
                {
                    b.Alive(0.85f, Mathf.Min(0.88f, curlFrom + 0.14f), true, 0f, true);
                    var f2 = PathAt(p, 0.56f, out _) + V(0, r0 * 0.6f, 0);
                    b.Tube(TreeMeshBuilder.Scroll(f2, V(side * 0.45f, 0.9f).normalized, 11f, 1.25f, side < 0, 14), r0 * 0.34f, r0 * 0.12f, 5, Mathf.Lerp(b0, b1, 0.56f), Mathf.Lerp(b0, b1, 0.56f) + 0.08f); b.Pieces++; k.Curls++;
                }
                b.Plain();
            }
            // its tip
            if (st.Finials) { Finial(b, th, tip, tipDir, side, b1, br.Index); k.Finials++; }
        }

        private static void Leaf(TreeMeshBuilder b, TreeTheme th, Vector3 at, float lean, float size, int n, float birth)
        {
            var col = n % 2 == 0 ? th.LeafA : th.LeafB;
            float mark = 0.08f + 0.075f * (((n % 11) + 11) % 11); // (when this one buds & pops in as skill points are spent: one by one, 0.08 to 0.83)
            switch (th.Leaf)
            {
                case TreeTheme.LeafKind.Shard: Shard(b, at, lean, 34f * size, 5.2f, col, th.LeafGlow, birth, 0f, true, mark); break;
                case TreeTheme.LeafKind.Pane:
                {
                    var cols = new[] { th.LeafA, th.LeafB, th.ClothA, th.ClothB };
                    // (a stained-glass pane: buds and lights up pane by pane as skill points are spent)
                    b.Alive(1f, mark, true, 0f, true); b.Pen(Shade(cols[((n % 4) + 4) % 4], 0.62f), th.LeafGloss, 0.05f); b.Plate(Diamond(15f * size, 30f * size), Lean(at, lean), 2.4f, birth, Vector2.zero, new Vector2(0, 15f * size), th.Gold); b.Plain(); b.Pieces++; break;
                }
                case TreeTheme.LeafKind.Spike: b.Alive(0.65f, mark, true, 0f, true); Tip(b, at, V(Mathf.Sin(lean * Mathf.Deg2Rad), Mathf.Cos(lean * Mathf.Deg2Rad)), 19f * size, 3.4f, n % 3 == 0 ? th.LeafB : th.LeafA, 0.4f, birth); b.Plain(); break;
                case TreeTheme.LeafKind.Rivet:
                    b.Alive(0.75f, mark, true, 0f, true);
                    if (n % 3 == 0) { b.Pen(Shade(th.LeafA, 0.8f), 0.8f); b.Plate(Gear(8, 10f * size, 7.4f * size), Lean(at + V(0, 9f, -2f), n * 13f), 2.2f, birth, Vector2.zero, Vector2.zero, Shade(th.LeafA, 0.6f)); b.Pieces++; }
                    else { b.Pen(Shade(th.LeafA, 0.8f), 0.85f); b.Ball(at + V(0, 1.5f, 0), 4.4f, 8, 4, birth); b.Pieces++; }
                    b.Plain();
                    break;
                case TreeTheme.LeafKind.Round: LeafPlate(b, at, lean, 25f * size, 19f * size, Shade(col, 0.8f), th.LeafGloss, 0f, birth, null, 0.85f, true, mark); break;
                case TreeTheme.LeafKind.Bullet:
                {
                    float r = lean * 0.4f * Mathf.Deg2Rad; var d = V(Mathf.Sin(r), Mathf.Cos(r));
                    b.Alive(0.8f, mark, true, 0f, true); b.Pen(Shade(col, 0.75f), 0.9f); b.Lathe(at, d, new[] { new Vector2(0, 3.6f), new Vector2(11f * size, 3.6f), new Vector2(16f * size, 2.4f), new Vector2(19f * size, 0.2f) }, 6, birth, birth + 0.02f); b.Plain(); b.Pieces++; break;
                }
                case TreeTheme.LeafKind.Maple: StarPlate(b, at + V(Mathf.Sin(lean * Mathf.Deg2Rad) * 10f, 9f * size, 0), 5, 13f * size, 5.6f * size, lean, Mix(col, C(150, 132, 112), 0.6f), 0.2f, 0f, birth, mark, true); break; // (buds & turns red as points are spent)
                case TreeTheme.LeafKind.Star: StarPlate(b, at + V(0, 9f * size, 0), 5, 9f * size, 3.8f * size, lean * 0.3f, Shade(col, 0.7f), th.LeafGloss, 0.05f, birth, mark, true); break; // (star by star)
                case TreeTheme.LeafKind.Green: LeafPlate(b, at, lean, 30f * size, 14f * size, Shade(col, 0.78f), th.LeafGloss, 0f, birth, null, 0.85f, true, mark); break; // (a bud that leafs)
                default: LeafPlate(b, at, lean, 30f * size, 11.5f * size, Shade(col, 0.82f), th.LeafGloss, 0f, birth, Shade(col, 0.6f), 0.7f, true, mark, true); break; // steel laurel (buds & polishes: its own colour)
            }
        }

        private static void Finial(TreeMeshBuilder b, TreeTheme th, Vector3 tip, Vector3 dir, int side, float birth, int n)
        {
            switch (th.Family)
            {
                case TreeFamily.Swordsman: Bead(b, tip, 4.2f, th.Gold, 0.9f, 0f, birth); Tip(b, tip + dir * 2f, dir, 30f, 7.5f, th.Metal, 0.9f, birth + 0.02f); break;
                case TreeFamily.Mage: Shard(b, tip, Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg, 40f, 6.5f, th.LeafA, 0.7f, birth + 0.02f); break;
                case TreeFamily.Acolyte: Bead(b, tip, 3.6f, th.Gold, 0.9f, 0f, birth); b.Pen(th.Gold, 0.9f); b.Plate(Diamond(11f, 26f), Lean(tip + dir * 2f, Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg), 2.4f, birth + 0.02f, Vector2.zero, new Vector2(0, 13f)); b.Pieces++; break;
                case TreeFamily.Archer: Tip(b, tip, dir, 22f, 6.5f, th.Metal, 0.85f, birth + 0.02f); break;
                case TreeFamily.Thief: Tip(b, tip, dir, 34f, 4.2f, th.LeafA, 0.5f, birth + 0.02f); break;
                case TreeFamily.Merchant: b.Pen(th.LeafA, 0.85f); b.Plate(Gear(8, 13f, 9.5f), Lean(tip + V(0, 0, -3f), n * 17f), 2.6f, birth + 0.02f, Vector2.zero, Vector2.zero, Shade(th.LeafA, 0.7f)); b.Pieces++; Bead(b, tip + V(0, 0, -5f), 3.4f, th.Trunk, 0.4f, 0f, birth + 0.03f); break;
                case TreeFamily.Gunslinger: b.Pen(th.LeafA, 0.9f); b.Lathe(tip, dir, new[] { new Vector2(0, 4.6f), new Vector2(12f, 4.6f), new Vector2(19f, 3f), new Vector2(23f, 0.2f) }, 6, birth, birth + 0.03f); b.Pieces++; break;
                case TreeFamily.Ninja: StarPlate(b, tip + dir * 9f + V(0, 0, -3f), 4, 14f, 4.6f, n * 22f, Shade(th.Metal, 0.8f), 0.9f, 0f, birth + 0.02f, 0f, false, true); break;
                case TreeFamily.Taekwon: StarPlate(b, tip + dir * 8f + V(0, 0, -3f), 5, 13f, 5.4f, 0f, th.Gold, 0.9f, 0.3f, birth + 0.02f); break;
                default: // a flower (the Novice's sapling, the Summoner's stalk)
                    StarPlate(b, tip + dir * 7f + V(0, 0, -3f), 5, 12f, 7.5f, n * 31f, Shade(th.Family == TreeFamily.Summoner ? th.ClothB : th.ClothA, 0.8f), 0.2f, 0f, birth + 0.02f, 0f, true, true); // (a bud that opens, in its own colour)
                    Bead(b, tip + dir * 7f + V(0, 0, -5f), 3.2f, C(250, 220, 110), 0.3f, 0.2f, birth + 0.03f); break;
            }
        }

        // ------------------------------------------------------------------ what hangs from the branch tips
        private static void Hangings(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st, Counts k)
        {
            int want = Mathf.Min(st.Banners, s.Branches.Count); if (want <= 0) return;
            for (int i = 0; i < want; i++)
            {
                var br = s.Branches[(i * s.Branches.Count) / want];
                var tip = br.Path[br.Path.Count - 1]; var top = tip + V(0, -st.Radius * 0.5f, -4f); float birth = Mathf.Min(0.97f, br.B1 + 0.04f);
                float hangFrom = 0.14f + 0.72f * (i / (float)Mathf.Max(1, want));
                b.Alive(0.85f, hangFrom, true, 0f, true);
                b.Pen(Shade(th.Wood, 0.8f), 0.2f); b.Line(top, top + V(0, -9f, 0), 1.8f, birth, birth);
                var at = top + V(0, -9f, 0);
                switch (th.Hang)
                {
                    case TreeTheme.HangKind.Flag:
                        b.Pen(th.Gold, 0.9f); b.Tube(new[] { at + V(-16f, 0, 0), at + V(16f, 0, 0) }, 1.8f, 1.8f, 4, birth, birth, true, true);
                        b.Pen(th.ClothA, 0.1f); b.Plate(Banner(27f, 58f, 13f), Lean(at, 0f), 1.4f, birth + 0.01f, Vector2.zero, new Vector2(0, -20f), th.ClothB);
                        b.Pen(th.ClothB, 0.5f); b.Plate(ShieldShape(13f, 16f), Lean(at + V(0, -22f, -1.4f), 0f), 0.8f, birth + 0.03f, Vector2.zero, Vector2.zero);
                        break;
                    case TreeTheme.HangKind.Crystal:
                        Shard(b, at, 180f, 34f, 6f, i % 2 == 0 ? th.LeafA : th.LeafB, 0.75f, birth + 0.01f, 0f, true, hangFrom);
                        b.Alive(0.85f, hangFrom, true, 0f, true);
                        b.Pen(Alpha(th.Glow, 150), 0f, 1f); b.Arc(at + V(0, -15f, -1f), 11f, 12.6f, 0f, 360f, 14, birth + 0.03f, at);
                        break;
                    case TreeTheme.HangKind.GlassPane:
                    {
                        var cols = new[] { th.LeafA, th.LeafB, th.ClothA, th.ClothB };
                        Bead(b, at, 3f, th.Gold, 0.9f, 0f, birth);
                        b.Alive(0.9f, hangFrom, true, 0f, true);
                        b.Pen(cols[i % 4], 0.7f, 0.4f); b.Plate(Diamond(20f, 44f), Lean(at + V(0, -46f, 0), 0f), 2.4f, birth + 0.01f, new Vector2(0, 44f), new Vector2(0, 22f), th.Gold);
                        Tassel(b, at + V(0, -47f, 0), 12f, th.Gold, birth + 0.04f);
                        break;
                    }
                    case TreeTheme.HangKind.Feather:
                        LeafPlate(b, at, 172f, 46f, 12f, th.ClothB, 0.1f, 0f, birth + 0.01f, Shade(th.ClothB, 0.8f), 0.85f, true, hangFrom, true);
                        LeafPlate(b, at + V(0, 0, -1.5f), 190f, 38f, 10f, th.ClothA, 0.1f, 0f, birth + 0.03f, Shade(th.ClothA, 0.8f), 0.85f, true, hangFrom, true);
                        Bead(b, at, 2.6f, th.Trunk2, 0.2f, 0f, birth);
                        break;
                    case TreeTheme.HangKind.Cloth:
                        b.Pen(i % 2 == 0 ? th.ClothA : th.ClothB, 0.05f); b.Plate(Torn(22f, 66f), Lean(at, i % 2 == 0 ? 4f : -5f), 1.2f, birth + 0.01f, Vector2.zero, new Vector2(0, -20f));
                        break;
                    case TreeTheme.HangKind.Wares:
                        if (i % 3 == 0) // a coin bag
                        { b.Pen(th.ClothA, 0.15f); b.Ball(at + V(0, -17f, 0), 12f, 8, 4, birth + 0.01f, 1.1f); b.Lathe(at + V(0, -8f, 0), Vector3.up, new[] { new Vector2(0, 4f), new Vector2(4f, 2.6f), new Vector2(8f, 5f) }, 6, birth + 0.01f, birth + 0.02f); Bead(b, at + V(0, -6.5f, -3.5f), 1.8f, th.Gold, 0.9f, 0f, birth + 0.03f); }
                        else if (i % 3 == 1) // a lantern
                        {
                            b.Pen(Shade(th.Wood, 0.8f), 0.8f); b.Box(at + V(0, -3f, 0), V(15f, 3f, 15f), Quaternion.identity, birth + 0.01f, at); b.Box(at + V(0, -29f, 0), V(15f, 3f, 15f), Quaternion.identity, birth + 0.01f, at);
                            b.Alive(1f, hangFrom, true, 1f); b.Pen(Alpha(Shade(th.ClothB, 0.55f), 235), 0.2f, 0.35f); b.Ball(at + V(0, -16f, 0), 8.6f, 8, 4, birth + 0.02f, 1.2f);
                            b.Alive(0.85f, hangFrom, true, 0f, true);
                            b.Pen(Shade(th.Wood, 0.8f), 0.8f); foreach (float x in new[] { -6.5f, 6.5f }) b.Tube(new[] { at + V(x, -3f, -6.5f), at + V(x, -29f, -6.5f) }, 0.9f, 0.9f, 4, birth + 0.03f, birth + 0.03f, false, false);
                        }
                        else // scales
                        {
                            b.Pen(th.Gold, 0.9f); b.Tube(new[] { at + V(-22f, -3f, 0), at + V(22f, -3f, 0) }, 1.6f, 1.6f, 4, birth + 0.01f, birth + 0.01f, true, true);
                            foreach (float x in new[] { -20f, 20f })
                            {
                                b.Pen(Shade(th.Wood, 0.8f), 0.3f); b.Line(at + V(x, -3f, -1f), at + V(x - 7f, -26f, -1f), 1f, birth + 0.02f, birth + 0.02f); b.Line(at + V(x, -3f, -1f), at + V(x + 7f, -26f, -1f), 1f, birth + 0.02f, birth + 0.02f);
                                b.Pen(th.Gold, 0.9f); b.Lathe(at + V(x, -30f, 0), Vector3.up, new[] { new Vector2(0, 0.5f), new Vector2(2.5f, 7f), new Vector2(4f, 9f) }, 8, birth + 0.03f, birth + 0.03f);
                            }
                        }
                        break;
                    case TreeTheme.HangKind.Pennant:
                        b.Pen(th.Gold, 0.9f); b.Tube(new[] { at + V(-15f, 0, 0), at + V(15f, 0, 0) }, 1.6f, 1.6f, 4, birth, birth, true, true);
                        b.Pen(th.ClothA, 0.1f); b.Plate(Pennant(27f, 52f), Lean(at, 0f), 1.4f, birth + 0.01f, Vector2.zero, new Vector2(0, -14f), th.ClothB);
                        StarPlate(b, at + V(0, -14f, -1.4f), 5, 7f, 3f, 0f, th.ClothB, 0.8f, 0.2f, birth + 0.03f, hangFrom, true);
                        break;
                    case TreeTheme.HangKind.Talisman:
                        b.Pen(th.ClothA, 0.05f); b.Plate(RectShape(16f, 46f), Lean(at + V(0, -23f, 0), i % 2 == 0 ? 3f : -4f), 1f, birth + 0.01f, new Vector2(0, 23f), Vector2.zero);
                        b.Pen(th.ClothB, 0.05f); b.Plate(RectShape(3.4f, 30f), Lean(at + V(0, -24f, -1.2f), i % 2 == 0 ? 3f : -4f), 0.6f, birth + 0.03f, new Vector2(0, 23f), Vector2.zero);
                        break;
                    case TreeTheme.HangKind.Belt:
                        b.Pen(BeltColour(s.Ornament), 0.1f); b.Plate(RectShape(11f, 70f), Lean(at + V(0, -35f, 0), i % 2 == 0 ? 5f : -6f), 1.4f, birth + 0.01f, new Vector2(0, 35f), Vector2.zero);
                        b.Pen(th.Gold, 0.8f); b.Plate(RectShape(11.6f, 5f), Lean(at + V(0, -63f, -1.4f), i % 2 == 0 ? 5f : -6f), 0.8f, birth + 0.03f, new Vector2(0, 63f), Vector2.zero);
                        break;
                    case TreeTheme.HangKind.Fish:
                        if (i % 2 == 0) { b.Pen(th.ClothA, 0.6f); b.Plate(Fish(17f, 8f), Lean(at + V(0, -19f, 0), -90f), 2.4f, birth + 0.01f, new Vector2(-17f, 0), Vector2.zero, Shade(th.ClothA, 0.7f)); Bead(b, at + V(1.8f, -8f, -2f), 1.3f, C(30, 30, 40), 0.9f, 0f, birth + 0.03f); }
                        else
                        {
                            b.Pen(th.ClothB, 0.2f); b.Ring(at + V(0, -16f, 0), 0f, 9f, 12, birth + 0.01f, at);
                            foreach (var o in new[] { V(-8f, -4f), V(0f, -1f), V(8f, -4f) }) b.Ring(at + o + V(0, 0, -0.5f), 0f, 3.6f, 8, birth + 0.03f, at);
                        }
                        break;
                    default: // Ribbon
                    {
                        var cols = new[] { th.ClothA, th.ClothB, C(250, 226, 130) };
                        b.Pen(cols[i % 3], 0.1f); b.Plate(Banner(13f, 62f, 9f), Lean(at, i % 2 == 0 ? 6f : -7f), 1.2f, birth + 0.01f, Vector2.zero, new Vector2(0, -20f));
                        break;
                    }
                }
                b.Plain();
                b.Pieces++; k.Hangings++;
            }
        }

        /// <summary>The Taekwon's belt: its colour by the job's tree (first job white, second yellow, transcendent blue, third red, the last black).</summary>
        public static Color32 BeltColour(int ornament)
            => ornament <= 3 ? C(240, 240, 236) : ornament <= 5 ? C(240, 204, 60) : ornament <= 8 ? C(52, 96, 200) : ornament <= 11 ? C(196, 44, 44) : C(30, 30, 34);

        // ------------------------------------------------------------------ behind the trunk
        private static void Behind(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st, Counts k)
        {
            if (PreviewBehindHook != null && PreviewBehindHook(b, s, th, st, k)) return;
            float top = s.Top, bot = s.Bottom, midY = (bot + top) * 0.5f;
            if (th.Line == JobLine.Crusader)
            {
                // Radiant golden sun-rays and halo behind the Grand Cross tower shield
                b.Alive(0.9f);
                for (int i = 0; i < 8; i++)
                {
                    float a = (i * 45f + 22.5f) * Mathf.Deg2Rad;
                    var d = V(Mathf.Sin(a), Mathf.Cos(a));
                    b.Pen(Alpha(th.Gold, 190), 0.8f, 0.7f);
                    Tip(b, V(0, midY, 8f) + d * 42f, d, 58f + (i % 2) * 24f, 6.5f, th.Gold, 0.9f, 0.1f);
                }
                b.Plain(); k.TrunkParts++;
                return;
            }
            if (th.Line == JobLine.Hunter)
            {
                // Radiant wind-Current halo behind the soaring falcon
                b.Alive(0.85f); b.Pen(Alpha(th.Glow, 145), 0f, 1f);
                b.Arc(V(0, midY + 24f, 8f), 64f, 70f, 0f, 360f, 24, 0.1f, V(0, midY + 24f, 8f));
                b.Plain(); k.TrunkParts++;
                return;
            }
            if (th.IsDancer)
            {
                // Flowing golden-silk veil halo behind the dancer's crossed fans
                b.Alive(0.85f); b.Pen(Alpha(th.Glow, 150), 0f, 1f);
                b.Arc(V(0, midY + 8f, 8f), 62f, 68f, 0f, 360f, 24, 0.1f, V(0, midY + 8f, 8f));
                b.Plain(); k.TrunkParts++;
                return;
            }
            if (th.Line == JobLine.Wizard)
            {
                float wMid = (bot + (top - 110f)) * 0.5f;
                Hoop(b, V(0, wMid, 0), 68f, 44f, 22f, 2.4f, false, th.Gold, 0.95f, 0.5f, s.TrunkBirth(wMid) + 0.04f);
                Hoop(b, V(0, wMid, 0), 62f, 40f, -26f, 2.2f, false, th.Trunk2, 0.8f, 0.85f, s.TrunkBirth(wMid) + 0.06f);
                Hoop(b, V(0, wMid + 110f, 0), 52f, 32f, 12f, 2.0f, false, th.LeafA, 0.8f, 0.75f, s.TrunkBirth(wMid + 110f) + 0.05f);
                return;
            }
            if (th.Line == JobLine.Sage)
            {
                float sMid = (bot + (top - 80f)) * 0.5f, mb = s.TrunkBirth(sMid);
                Hoop(b, V(0, sMid, 0), 74f, 42f, 16f, 2.5f, false, th.Gold, 0.92f, 0.4f, mb + 0.03f);
                Hoop(b, V(0, sMid, 0), 66f, 38f, -18f, 2.0f, false, th.LeafA, 0.85f, 0.7f, mb + 0.05f);
                return;
            }
            if (th.Line == JobLine.Alchemist)
            {
                float aMid = (bot + (top - 40f)) * 0.5f, mb = s.TrunkBirth(aMid);
                Hoop(b, V(0, aMid, -6f), 49f, 49f, 12f, 3.2f, false, th.Gold, 0.95f, 0f, mb + 0.04f);
                Hoop(b, V(0, aMid, -6f), 49f, 49f, -14f, 2.8f, false, th.Trunk, 0.9f, 0f, mb + 0.05f);
                return;
            }
            if (th.Line == JobLine.Bard)
            {
                // The 5 golden harmonic strings of the concert lyre
                for (int i = -2; i <= 2; i++)
                {
                    float x = i * 9.5f;
                    b.Alive(0.95f, 0f, false, 0.5f); b.Pen(th.Gold, 0.95f, 0.8f);
                    b.Line(V(x * 0.65f, bot + 135f, 2f), V(x, top - 82f, 2f), 1.9f, 0.05f, s.TrunkGrown);
                }
                b.Plain(); k.TrunkParts++;
                return;
            }
            switch (th.Family)
            {
                case TreeFamily.Swordsman:
                    if (st.Trunk >= 4) // the crest's crossed spears behind the hilt
                        foreach (int sg in new[] { -1, 1 })
                        {
                            Vector3 a = V(-sg * 46f, top - 150f, 6f), c = V(sg * 74f, top + 66f, 6f); float bb = s.TrunkGrown + 0.05f;
                            b.Pen(Shade(th.Trunk2, 0.55f), 0.3f); b.Tube(new[] { a, c }, 2.6f, 2.6f, 5, bb, bb + 0.1f, true, false);
                            Tip(b, c, (c - a).normalized, 30f, 7f, th.Metal, 0.9f, bb + 0.1f); k.TrunkParts++;
                        }
                    break;
                case TreeFamily.Mage:
                    for (int i = 0; i < st.Trunk - 1; i++) { float y = Mathf.Lerp(bot + 150f, top - 230f, st.Trunk <= 2 ? 0.4f : i / (float)(st.Trunk - 2 + 0.001f) * 0.7f + 0.15f); Hoop(b, V(0, y, 0), 46f + 6f * i, 30f, i % 2 == 0 ? 9f : -11f, 1.7f, false, th.Trunk2, 0.2f, 1f, s.TrunkBirth(y) + 0.05f); }
                    break;
                case TreeFamily.Archer:
                    // the bow's string
                    b.Pen(C(236, 230, 214), 0.2f); b.Line(V(-17f, bot + 34f, 3f), V(-17f, top - 44f, 3f), 2.2f, 0.02f, s.TrunkGrown);
                    break;
                case TreeFamily.Taekwon:
                    for (int i = 0; i < Mathf.Max(0, st.Trunk - 2); i++) { float y = Mathf.Lerp(bot + 220f, top - 160f, i * 0.6f + 0.2f); Hoop(b, V(0, y, 0), 52f, 34f, i % 2 == 0 ? 14f : -12f, 1.4f, false, th.Gold, 0.9f, 0.3f, s.TrunkBirth(y) + 0.05f); }
                    break;
                case TreeFamily.Ninja:
                    if (st.Trunk >= 4) { b.Alive(0.8f); b.Pen(Alpha(Shade(th.Trunk2, 0.7f), 235), 0.1f, 0.3f); b.Ring(V(0, top - 96f, 9f), 0f, 62f, 24, s.TrunkGrown, V(0, top - 96f, 9f)); b.Plain(); b.Pieces++; k.TrunkParts++; } // a red sun behind the hilt
                    break;
                case TreeFamily.Novice:
                    if (st.Trunk >= 2) { b.Pen(Shade(th.Trunk, 0.8f), 0.1f); b.Tube(new[] { V(26f, bot, 5f), V(20f, bot + 250f, 5f) }, 4.2f, 3.6f, 5, 0f, 0.1f); b.Pieces++; k.TrunkParts++; } // the stake
                    break;
            }
        }

        // ------------------------------------------------------------------ the trunks
        private static void Trunk(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st, Counts k)
        {
            int p0 = b.Pieces;
            if (PreviewTrunkHook != null && PreviewTrunkHook(b, s, th, st, k)) { k.TrunkParts += b.Pieces - p0; return; }
            if (th.IsDancer) { DancerFans(b, s, th, st); k.TrunkParts += b.Pieces - p0; return; }
            switch (th.Line)
            {
                case JobLine.Knight: KnightLance(b, s, th, st); k.TrunkParts += b.Pieces - p0; return;
                case JobLine.Crusader: CrusaderShieldCross(b, s, th, st); k.TrunkParts += b.Pieces - p0; return;
                case JobLine.Wizard: WizardOrrery(b, s, th, st); k.TrunkParts += b.Pieces - p0; return;
                case JobLine.Sage: SageGrimoire(b, s, th, st); k.TrunkParts += b.Pieces - p0; return;
                case JobLine.Priest: PriestSanctuary(b, s, th, st); k.TrunkParts += b.Pieces - p0; return;
                case JobLine.Monk: MonkVajra(b, s, th, st); k.TrunkParts += b.Pieces - p0; return;
                case JobLine.Hunter: HunterFalcon(b, s, th, st); k.TrunkParts += b.Pieces - p0; return;
                case JobLine.Bard: BardLyre(b, s, th, st); k.TrunkParts += b.Pieces - p0; return;
                case JobLine.Assassin: AssassinKatars(b, s, th, st); k.TrunkParts += b.Pieces - p0; return;
                case JobLine.Rogue: RogueGrapple(b, s, th, st); k.TrunkParts += b.Pieces - p0; return;
                case JobLine.Blacksmith: BlacksmithForge(b, s, th, st); k.TrunkParts += b.Pieces - p0; return;
                case JobLine.Alchemist: AlchemistAlembic(b, s, th, st); k.TrunkParts += b.Pieces - p0; return;
            }
            switch (th.Family)
            {
                case TreeFamily.Swordsman: Sword(b, s, th, st); break;
                case TreeFamily.Mage: Staff(b, s, th, st); break;
                case TreeFamily.Acolyte: Pillar(b, s, th, st); break;
                case TreeFamily.Archer: Bow(b, s, th, st); break;
                case TreeFamily.Thief: Daggers(b, s, th, st); break;
                case TreeFamily.Merchant: AnvilPole(b, s, th, st); break;
                case TreeFamily.Gunslinger: Rifle(b, s, th, st); break;
                case TreeFamily.Ninja: Katana(b, s, th, st); break;
                case TreeFamily.Taekwon: Post(b, s, th, st); break;
                case TreeFamily.Summoner: Stalk(b, s, th, st); break;
                default: Sapling(b, s, th, st); break;
            }
            k.TrunkParts += b.Pieces - p0;
        }

        private static void Sword(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            // (the trunk is the main thing of the tree: a blade 6 to 7.6 cm wide, as wide as a medallion)
            float bot = s.Bottom, guard = s.Top - 150f, len = guard - bot, w = 30f + 2f * st.Trunk, tg = s.TrunkBirth(guard);
            // (a DULL grey blade at job level 1: it takes its polish as the tree comes alive)
            b.Pen(Shade(th.Trunk, 0.74f), 0.75f);
            b.Alive(0.55f, 0f, false, 0f, true);
            b.Blade(V(0, bot, 0), Vector3.up, Vector3.right, len, x => x < 0.09f ? w * x / 0.09f : w * (1f + 0.14f * (x - 0.09f)), x => x < 0.09f ? 8f * x / 0.09f : 8f, 12, 0f, tg, Shade(th.Trunk, 0.46f)); b.Pieces++;
            // its edges: a dull line that turns keen and bright with the job level (design note: "a swordsman's tree's sword looking sharper")
            b.Alive(1f, 0f, false, 0f, st.Trunk >= 2); b.Pen(Shade(th.Trunk, st.Trunk >= 2 ? 0.6f : 0.5f), 0.9f);
            foreach (int sg in new[] { -1, 1 })
            {
                b.Line(V(0, bot + 2f, -1f), V(sg * (w - 1.2f), bot + len * 0.09f, -1f), 2.6f, 0f, s.TrunkBirth(bot + len * 0.09f));
                b.Line(V(sg * (w - 1.2f), bot + len * 0.09f, -1f), V(sg * (w * 1.127f - 1.2f), guard - 10f, -1f), 2.6f, s.TrunkBirth(bot + len * 0.09f), tg);
            }
            // the fuller: engraved, then gold; it fills with colour with the job level (design note: "a knight's sword's fuller groove becoming colorful")
            if (st.Trunk >= 2) { b.Alive(1f); b.Pen(st.Trunk >= 3 ? Shade(th.Gold, 0.7f) : Shade(th.Trunk, 0.45f), 0.9f); b.Line(V(0, bot + 90f, -8.4f), V(0, guard - 22f, -8.4f), st.Trunk >= 3 ? 7f : 5f, s.TrunkBirth(bot + 90f), tg); b.Pieces++; }
            b.Plain();
            if (st.Trunk >= 3) for (int i = 0; i < 4; i++) { float y = Mathf.Lerp(bot + 160f, guard - 100f, i / 3f); b.Pen(th.Gold, 0.9f); b.Plate(Diamond(17f, 32f), Lean(V(0, y, -9f), 0f), 0.8f, s.TrunkBirth(y) + 0.02f, Vector2.zero, new Vector2(0, 16f)); b.Pieces++; }
            // the guard
            float gw = 150f + 22f * st.Trunk;
            b.Pen(st.Trunk >= 3 ? th.Gold : Shade(th.Trunk, 0.72f), 0.85f); b.Box(V(0, guard, 0), V(gw, 19f, 17f), Quaternion.identity, tg, V(0, guard, 0)); b.Pieces++;
            if (st.Trunk >= 1) foreach (int sg in new[] { -1, 1 }) Bead(b, V(sg * gw * 0.5f, guard, 0), 12f, st.Trunk >= 3 ? th.Gold : Shade(th.Trunk, 0.85f), 0.9f, 0f, tg + 0.02f);
            if (st.Trunk >= 3) // the winged guard and its gem
            {
                foreach (int sg in new[] { -1, 1 })
                    for (int f = 0; f < 3; f++) LeafPlate(b, V(sg * (16f + 5f * f), guard + 6f, -9f - f), sg * (62f - 22f * f), 72f - 12f * f, 19f, C(232, 236, 244), 0.5f, 0f, tg + 0.03f + 0.01f * f, C(180, 186, 200));
                Bead(b, V(0, guard, -11f), 12f, C(214, 40, 52), 0.95f, 0.45f, tg + 0.05f);
            }
            // the grip, its wrap, the pommel
            b.Pen(st.Trunk >= 1 ? th.Trunk2 : C(104, 70, 50), 0.15f);
            b.Lathe(V(0, guard + 9.5f, 0), Vector3.up, new[] { new Vector2(0, 10.5f), new Vector2(40f, 12.5f), new Vector2(84f, 10.5f) }, 9, tg, s.TrunkBirth(guard + 94f)); b.Pieces++;
            if (st.Trunk >= 1) { b.Pen(th.Trunk2, 0.1f); b.Plate(Banner(17f, 92f, 12f), Lean(V(22f, guard + 4f, -10f), -8f), 1.2f, tg + 0.04f, Vector2.zero, new Vector2(0, -24f)); b.Pieces++; } // the tail of the ribbon
            Bead(b, V(0, guard + 106f, 0), 16f, st.Trunk >= 3 ? th.Gold : Shade(th.Trunk, 0.85f), 0.9f, 0f, s.TrunkBirth(guard + 106f));
            if (st.Trunk >= 2) // a shield hung on the blade
            {
                float y = guard - 88f; b.Pen(C(140, 96, 60), 0.2f); b.Plate(ShieldShape(52f, 64f), Lean(V(34f, y, -12f), -6f), 3f, s.TrunkBirth(y) + 0.03f, Vector2.zero, Vector2.zero, st.Trunk >= 3 ? th.Gold : th.Metal);
                b.Pen(th.ClothA, 0.2f); b.Plate(ShieldShape(34f, 42f), Lean(V(34f, y, -13.8f), -6f), 0.8f, s.TrunkBirth(y) + 0.05f, Vector2.zero, Vector2.zero); b.Pieces++;
            }
            if (st.Trunk >= 4) // the crest: a helm with its plume over the pommel
            {
                float y = s.Top - 22f, tb = s.TrunkGrown + 0.08f;
                b.Pen(th.Metal, 0.9f); b.Lathe(V(0, y, -2f), Vector3.up, new[] { new Vector2(0, 27f), new Vector2(30f, 30f), new Vector2(50f, 22f), new Vector2(62f, 4f) }, 10, tb, tb + 0.05f); b.Pieces++;
                b.Pen(C(40, 40, 48), 0.3f); b.Plate(RectShape(32f, 6f), Lean(V(0, y + 30f, -31f), 0f), 1f, tb + 0.05f, Vector2.zero, Vector2.zero);
                b.Pen(th.Gold, 0.9f); b.Lathe(V(0, y - 2f, -2f), Vector3.up, new[] { new Vector2(0, 28.5f), new Vector2(2.5f, 30.5f), new Vector2(5f, 28.5f) }, 10, tb, tb + 0.02f);
                LeafPlate(b, V(0, y + 60f, -2f), 14f, 58f, 24f, th.ClothA, 0.1f, 0f, tb + 0.08f, Shade(th.ClothA, 0.7f));
            }
        }

        /// <summary>Two strands wound round each other from yA to yB (the staff's wood): each half turn drawn far strand first.</summary>
        private static void Twist(TreeMeshBuilder b, TreeShape s, float yA, float yB, float r0, float r1, float amp, float period, Color32 c1, Color32 c2, float gloss)
        {
            int halves = Mathf.Max(2, Mathf.RoundToInt((yB - yA) / (period * 0.5f))); float span = yB - yA;
            for (int h = 0; h < halves; h++)
            {
                var pa = new List<Vector3>(6); var pb = new List<Vector3>(6); float zMid = 0f;
                for (int i = 0; i <= 5; i++)
                {
                    float t = (h + i / 5f) / halves, y = yA + span * t, a = t * halves * Mathf.PI, am = amp * (1f - 0.35f * t);
                    pa.Add(V(Mathf.Sin(a) * am, y, Mathf.Cos(a) * am * 0.7f)); pb.Add(V(-Mathf.Sin(a) * am, y, -Mathf.Cos(a) * am * 0.7f));
                    if (i == 2) zMid = Mathf.Cos(a);
                }
                float t0 = h / (float)halves, t1 = (h + 1f) / halves, ra = Mathf.Lerp(r0, r1, t0), rb = Mathf.Lerp(r0, r1, t1), b0 = s.TrunkBirth(yA + span * t0), b1 = s.TrunkBirth(yA + span * t1);
                bool aFar = zMid > 0f; // (+z is away from the player)
                b.Pen(aFar ? c1 : c2, gloss); b.Tube(aFar ? pa : pb, ra, rb, 6, b0, b1, h == 0, h == halves - 1);
                b.Pen(aFar ? c2 : c1, gloss); b.Tube(aFar ? pb : pa, ra, rb, 6, b0, b1, h == 0, h == halves - 1);
            }
            b.Pieces++;
        }

        private static void Staff(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom, top = s.Top - 120f, tt = s.TrunkBirth(top);
            if (st.Trunk >= 1) // roots
                foreach (var (dx, dz) in new[] { (-1f, 0.3f), (1f, 0.2f), (-0.5f, -0.8f), (0.6f, -0.7f) })
                { var a = V(dx * 5f, bot + 46f, dz * 5f); b.Pen(Shade(th.Trunk, 0.85f), 0.15f); b.Tube(TreeMeshBuilder.Curve(a, a + V(dx * 26f, -26f, dz * 14f), V(dx * 62f, bot, dz * 30f), 6), 10f, 3.4f, 5, 0.04f, 0f); b.Pieces++; }
            Twist(b, s, bot + 8f, top, 15f, 11f, 12.5f, 260f, th.Trunk, Shade(th.Trunk, 0.72f), 0.15f);
            if (st.Trunk >= 2) // crystals at its foot
                foreach (var (x, lean, len) in new[] { (-30f, -24f, 44f), (26f, 20f, 52f), (-12f, -6f, 34f), (44f, 34f, 30f) }) Shard(b, V(x, bot + 2f, -8f), lean, len, 7f, x > 0 ? th.LeafA : th.LeafB, 0.6f, 0.05f);
            // the rune rings' near halves (the far halves are behind the trunk: Behind)
            for (int i = 0; i < st.Trunk - 1; i++) { float y = Mathf.Lerp(bot + 150f, top - 230f, st.Trunk <= 2 ? 0.4f : i / (float)(st.Trunk - 2 + 0.001f) * 0.7f + 0.15f); Hoop(b, V(0, y, 0), 46f + 6f * i, 30f, i % 2 == 0 ? 9f : -11f, 1.7f, true, th.Trunk2, 0.2f, 1f, s.TrunkBirth(y) + 0.05f); }
            // the prongs and the great crystal
            foreach (int sg in new[] { -1, 1 }) { b.Pen(th.Trunk, 0.15f); b.Tube(TreeMeshBuilder.Curve(V(sg * 6f, top - 6f, 0), V(sg * 42f, top + 20f, 0), V(sg * 22f, top + 70f, 0), 8), 8.5f, 3f, 5, tt, tt + 0.05f); b.Pieces++; }
            Shard(b, V(0, top + 8f, -2f), 0f, 104f + 9f * st.Trunk, 22f + 2f * st.Trunk, th.Trunk2, 0.5f, tt + 0.04f, 1f); // (the great crystal: the one part that breathes)
            if (st.Trunk >= 3) foreach (int sg in new[] { -1, 1 }) Shard(b, V(sg * 15f, top + 16f, -6f), sg * 24f, 52f, 8f, th.LeafB, 0.7f, tt + 0.07f);
            if (st.Trunk >= 4) { b.Pen(Alpha(th.Glow, 200), 0f, 1f); b.Arc(V(0, top + 70f, -3f), 46f, 49f, 0f, 360f, 28, tt + 0.1f, V(0, top + 70f, -3f)); b.Arc(V(0, top + 70f, -3f), 58f, 59.6f, 0f, 360f, 28, tt + 0.12f, V(0, top + 70f, -3f)); b.Pieces += 2; }
        }

        private static void Pillar(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom, top = s.Top - 56f, h = top - bot, tt = s.TrunkBirth(top);
            b.Pen(th.Trunk, 0.35f);
            b.Lathe(V(0, bot, 0), Vector3.up, new[] { new Vector2(0, 52f), new Vector2(12f, 52f), new Vector2(12f, 45f), new Vector2(24f, 45f), new Vector2(30f, 37f), new Vector2(h * 0.5f, 33f), new Vector2(h - 34f, 30f), new Vector2(h - 22f, 38f), new Vector2(h - 10f, 46f), new Vector2(h, 46f), new Vector2(h, 0f) }, 14, 0f, tt); b.Pieces++;
            // its gold: bands at the foot and under the capital, then a middle band, tracery on the shaft, a jewel, the halo
            void Band(float y, float r) { b.Pen(th.Trunk2, 0.92f); b.Lathe(V(0, y, 0), Vector3.up, new[] { new Vector2(0, r), new Vector2(2.5f, r + 2.4f), new Vector2(5f, r) }, 14, s.TrunkBirth(y) + 0.02f, s.TrunkBirth(y) + 0.03f); b.Pieces++; }
            Band(bot + 28f, 38f); Band(top - 38f, 31f);
            if (st.Trunk >= 1) Band(bot + h * 0.5f, 33.2f);
            if (st.Trunk >= 2)
                for (int i = 0; i < 2; i++)
                {
                    float y0 = bot + (i == 0 ? 44f : h * 0.5f + 12f), y1 = y0 + Mathf.Min(150f, h * 0.3f);
                    b.Pen(th.Trunk2, 0.92f);
                    b.Tube(TreeMeshBuilder.Curve(V(-17f, y0, -30f), V(-17f, y1, -31f), V(0, y1 + 20f, -33f), 6), 1.5f, 1.5f, 4, s.TrunkBirth(y0), s.TrunkBirth(y1), false, false);
                    b.Tube(TreeMeshBuilder.Curve(V(17f, y0, -30f), V(17f, y1, -31f), V(0, y1 + 20f, -33f), 6), 1.5f, 1.5f, 4, s.TrunkBirth(y0), s.TrunkBirth(y1), false, false); b.Pieces++;
                }
            if (st.Trunk >= 3) { float y = bot + h * 0.5f + 40f; b.Pen(th.LeafB, 0.8f, 0.4f); b.Plate(Diamond(15f, 30f), Lean(V(0, y, -34f), 0f), 2f, s.TrunkBirth(y) + 0.04f, Vector2.zero, new Vector2(0, 15f), th.Trunk2); b.Pieces++; Band(top - 4f, 46.2f); }
            if (st.Trunk >= 4)
            {
                b.Pen(th.Trunk2, 0.92f); b.Lathe(V(0, top, 0), Vector3.up, new[] { new Vector2(0, 16f), new Vector2(26f, 6f), new Vector2(54f, 0.4f) }, 8, tt, tt + 0.05f); b.Pieces++;
                b.Pen(Alpha(th.Glow, 220), 0f, 1f); b.Arc(V(0, top + 40f, -4f), 40f, 43.5f, 0f, 360f, 28, tt + 0.08f, V(0, top + 40f, -4f)); b.Pieces++;
            }
        }

        private static void Bow(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom + 30f, top = s.Top - 40f, h = top - bot, midY = (bot + top) * 0.5f;
            var p = new List<Vector3>(21); for (int i = 0; i <= 20; i++) { float t = i / 20f; p.Add(V(34f * Mathf.Sin(t * Mathf.PI) - 17f, bot + h * t, 0)); }
            b.Pen(th.Trunk, 0.3f); b.Tube(p, t => 11f + 5f * Mathf.Sin(t * Mathf.PI), 8, 0f, s.TrunkGrown, true, true); b.Pieces++;
            // the grip's wrap, the limbs' bindings
            void Wrap(float t, float len, Color32 col) { int i = Mathf.Clamp(Mathf.RoundToInt(t * 20f), 1, 19); var d = (p[i + 1] - p[i - 1]).normalized; float r = 11f + 5f * Mathf.Sin(t * Mathf.PI) + 1.1f; b.Pen(col, 0.15f); b.Lathe(p[i] - d * len * 0.5f, d, new[] { new Vector2(0, r - 0.6f), new Vector2(1.5f, r), new Vector2(len - 1.5f, r), new Vector2(len, r - 0.6f) }, 7, s.TrunkBirth(p[i].y), s.TrunkBirth(p[i].y) + 0.02f); b.Pieces++; }
            Wrap(0.5f, 46f, th.Trunk2);
            if (st.Trunk >= 1) { Wrap(0.12f, 16f, th.Trunk2); Wrap(0.88f, 16f, th.Trunk2); }
            if (st.Trunk >= 3) { Wrap(0.3f, 9f, th.Gold); Wrap(0.7f, 9f, th.Gold); }
            // Horizontal nocked arrow resting on the bowstring perpendicular to the vertical bow shaft!
            {
                float ab = s.TrunkBirth(midY) + 0.04f;
                b.Pen(th.Gold, 0.9f); b.Tube(new[] { V(-22f, midY, -8f), V(52f, midY, -8f) }, 2.2f, 2.2f, 5, ab, ab + 0.04f, true, false); b.Pieces++;
                Tip(b, V(52f, midY, -8f), Vector3.right, 26f, 8f, th.Metal, 0.95f, ab + 0.04f);
                foreach (int sg in new[] { -1, 1 }) LeafPlate(b, V(-18f, midY, -8f), -90f + sg * 24f, 24f, 8.5f, th.ClothB, 0.15f, 0f, ab + 0.05f);
            }
            if (st.Trunk >= 1) // the quiver with its arrows, leaning at the foot
            {
                var q = Quaternion.Euler(0, 0, -16f); var at = V(34f, s.Bottom + 6f, -12f); var axis = q * Vector3.up; float qb = 0.03f;
                for (int i = 0; i < 3 + st.Trunk; i++)
                {
                    var a0 = at + axis * 96f + q * V((i - 2f) * 5f, 0, (i % 2) * 3f); var a1 = a0 + q * V((i - 2f) * 3.5f, 34f + 5f * (i % 3), 0);
                    b.Pen(C(206, 186, 150), 0.2f); b.Tube(new[] { a0, a1 }, 1.2f, 1.2f, 4, qb + 0.05f, qb + 0.08f, false, false);
                    LeafPlate(b, a1 - (a1 - a0).normalized * 12f, -16f + (i - 2f) * 6f, 15f, 7f, i % 2 == 0 ? th.ClothB : C(190, 60, 50), 0.1f, 0f, qb + 0.08f);
                }
                b.Pen(th.ClothA, 0.25f); b.Lathe(at, axis, new[] { new Vector2(0, 0.5f), new Vector2(0, 13f), new Vector2(60f, 16f), new Vector2(104f, 17.5f) }, 9, qb, qb + 0.06f); b.Pieces++;
                if (st.Trunk >= 2) { b.Pen(th.Trunk2, 0.2f); b.Lathe(at + axis * 84f, axis, new[] { new Vector2(0, 17.4f), new Vector2(3f, 18.6f), new Vector2(14f, 18.9f), new Vector2(17f, 18f) }, 9, qb + 0.05f, qb + 0.06f); b.Pieces++; }
                if (st.Trunk >= 2) { LeafPlate(b, at + axis * 60f + V(15f, 0, -6f), 168f, 44f, 11f, th.ClothB, 0.1f, 0f, qb + 0.08f, Shade(th.ClothB, 0.8f)); LeafPlate(b, at + axis * 60f + V(19f, 0, -7f), 186f, 36f, 9f, C(190, 60, 50), 0.1f, 0f, qb + 0.09f); }
            }
            if (st.Trunk >= 4) // the crest: eagle feathers at the upper limb
            {
                float tb = s.TrunkGrown + 0.06f;
                foreach (int sg in new[] { -1, 0, 1 }) LeafPlate(b, V(sg * 8f - 8f, top + 4f, -4f), sg * 20f, 50f - Mathf.Abs(sg) * 8f, 13f, sg == 0 ? th.ClothB : th.LeafA, 0.1f, 0f, tb + 0.09f);
            }
        }

        private static void Daggers(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom, top = s.Top - 30f, cross = bot + 96f;
            // the gnarled trunk rising out of the binding (behind the daggers)
            var p = new List<Vector3>(25); for (int i = 0; i <= 24; i++) { float t = i / 24f; p.Add(V(9f * Mathf.Sin(t * 9.4f) * (1f - t), Mathf.Lerp(cross - 20f, top, t), 0)); }
            b.Pen(th.Trunk, 0.2f); b.Tube(p, t => (22f - 13f * t) * (1f + 0.16f * Mathf.Sin(t * 37f)), 7, s.TrunkBirth(cross), s.TrunkGrown); b.Pieces++;
            if (st.Trunk >= 1) for (int i = 0; i < 4 + 2 * st.Trunk; i++) { float t = (i + 0.6f) / (4.6f + 2 * st.Trunk); var at = PathAt(p, t, out _); int sg = i % 2 == 0 ? -1 : 1; Tip(b, at + V(sg * (19f - 11f * t), 0, 0), V(sg * 0.85f, 0.5f), 22f, 4f, i % 3 == 0 ? th.Trunk2 : th.LeafA, 0.4f, s.TrunkBirth(at.y) + 0.03f); }
            // the crossed daggers, points down
            foreach (int sg in new[] { -1, 1 })
            {
                var dir = V(sg * 0.42f, -0.908f).normalized; var hilt = V(-sg * 44f, cross + 96f, sg < 0 ? 2f : -3f); float db = 0.02f;
                b.Alive(0.6f); b.Pen(th.Metal, 0.92f); b.Blade(hilt + dir * 34f, dir, V(dir.y, -dir.x), 176f, x => 11f * (1f - x * x), x => 3.6f * (1f - x), 6, db, db + 0.08f); b.Plain();
                b.Pen(st.Trunk >= 3 ? th.Gold : Shade(th.Metal, 0.7f), 0.85f); b.Box(hilt + dir * 32f, V(40f, 7f, 8f), Quaternion.FromToRotation(Vector3.up, -dir), db, hilt);
                b.Pen(C(40, 34, 44), 0.2f); b.Lathe(hilt, dir, new[] { new Vector2(0, 5f), new Vector2(16f, 6.2f), new Vector2(30f, 5f) }, 6, db, db + 0.02f);
                Bead(b, hilt - dir * 3f, 6.5f, st.Trunk >= 3 ? th.Trunk2 : th.Metal, 0.9f, st.Trunk >= 3 ? 0.4f : 0f, db);
            }
            // the purple cloth that binds them, and its tails
            b.Pen(th.ClothA, 0.08f); b.Lathe(V(0, cross - 15f, 0), Vector3.up, new[] { new Vector2(0, 18f), new Vector2(6f, 21f), new Vector2(24f, 21.5f), new Vector2(30f, 18f) }, 9, 0.03f, 0.06f); b.Pieces++;
            b.Pen(Shade(th.ClothA, 0.8f), 0.08f); b.Lathe(V(0, cross - 6f, 0), Vector3.up, new[] { new Vector2(0, 21.8f), new Vector2(4f, 22.6f), new Vector2(8f, 21.8f) }, 9, 0.04f, 0.06f);
            b.Pen(th.ClothA, 0.05f); b.Plate(Torn(17f, 92f), Lean(V(-13f, cross - 12f, -21f), -6f), 1.2f, 0.07f, Vector2.zero, new Vector2(0, -24f)); b.Plate(Torn(13f, 70f), Lean(V(10f, cross - 12f, -22f), 9f), 1.2f, 0.08f, Vector2.zero, new Vector2(0, -20f)); b.Pieces++;
            if (st.Trunk >= 2) // hanging cloth over the binding's shoulders
                foreach (int sg in new[] { -1, 1 }) { b.Pen(th.ClothB, 0.05f); b.Plate(Torn(30f, 74f), Lean(V(sg * 52f, cross + 62f, -4f), sg * 7f), 1.2f, 0.09f, Vector2.zero, new Vector2(0, -24f)); b.Pieces++; }
            if (st.Trunk >= 3) // chains from the hilts to the binding
                foreach (int sg in new[] { -1, 1 }) { b.Pen(Shade(th.Metal, 0.7f), 0.8f); b.Tube(TreeMeshBuilder.Curve(V(sg * 44f, cross + 92f, -6f), V(sg * 46f, cross + 6f, -10f), V(sg * 16f, cross + 4f, -22f), 9), 1.4f, 1.4f, 4, 0.1f, 0.14f, false, false); b.Pieces++; }
            // smoke: thin see-through wisps that wind upwards
            for (int i = 0; i < st.Trunk * 2; i++)
            {
                float y = Mathf.Lerp(cross + 120f, top - 40f, (i + 0.5f) / (st.Trunk * 2f)); int sg = i % 2 == 0 ? -1 : 1;
                var w = new List<Vector3>(9); for (int j = 0; j <= 8; j++) { float t = j / 8f; w.Add(V(sg * (26f + 44f * t + 12f * Mathf.Sin(t * 6f + i)), y + 60f * t + 10f * Mathf.Sin(t * 5f), -6f)); }
                b.Alive(1f); b.Pen(Alpha(th.Trunk2, 90), 0f, 0.8f); b.Tube(w, t => 5f * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) + 0.6f, 5, s.TrunkBirth(y) + 0.08f, s.TrunkBirth(y) + 0.2f, false, false); b.Plain(); b.Pieces++; // (the smoke thickens with the tree's liveliness; it does not drift)
            }
            if (st.Trunk >= 4) Bead(b, V(0, top + 6f, -2f), 10f, th.Trunk2, 0.95f, 0.6f, s.TrunkGrown + 0.04f); // the eye over the thorns
        }

        private static void AnvilPole(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom, top = s.Top - 30f; var steel = th.Metal;
            if (st.Trunk >= 2) foreach (var (x, y, r, n) in new[] { (-50f, bot + 62f, 22f, 10), (-22f, bot + 86f, 14f, 8) }) { b.Pen(th.LeafA, 0.8f); b.Plate(Gear(n, r, r * 0.76f), Lean(V(x, y, 6f), n * 4f), 3f, 0.05f, Vector2.zero, Vector2.zero, Shade(th.LeafA, 0.7f)); Bead(b, V(x, y, 4f), r * 0.24f, th.Trunk, 0.4f, 0f, 0.06f); }
            // the pole behind the anvil's top
            b.Pen(th.Trunk, 0.2f); b.Tube(new[] { V(0, bot + 40f, 0), V(0, top, 0) }, 16f, 13f, 9, 0.03f, s.TrunkGrown); b.Pieces++;
            float bandEvery = st.Trunk >= 2 ? 150f : 260f;
            for (float y = bot + 120f; y < top - 20f; y += bandEvery) { b.Pen(th.Trunk2, 0.85f); b.Lathe(V(0, y, 0), Vector3.up, new[] { new Vector2(0, 16.4f), new Vector2(2f, 18.4f), new Vector2(11f, 18.4f), new Vector2(13f, 16.4f) }, 9, s.TrunkBirth(y) + 0.02f, s.TrunkBirth(y) + 0.03f); b.Pieces++; }
            // the anvil: its foot, waist, body and horn
            b.Pen(Shade(steel, 0.8f), 0.6f); b.Box(V(0, bot + 7f, 0), V(96f, 14f, 50f), Quaternion.identity, 0f); b.Box(V(0, bot + 24f, 0), V(52f, 22f, 34f), Quaternion.identity, 0.01f);
            b.Pen(steel, 0.75f); b.Box(V(-6f, bot + 47f, 0), V(104f, 24f, 42f), Quaternion.identity, 0.02f);
            b.Lathe(V(46f, bot + 49f, 0), Vector3.right, new[] { new Vector2(0, 12f), new Vector2(30f, 7f), new Vector2(50f, 0.6f) }, 8, 0.03f, 0.05f); b.Pieces++;
            if (st.Trunk >= 1) // the hammer leaning on it
            {
                var q = Quaternion.Euler(0, 0, 24f); var foot = V(-66f, bot, -20f);
                b.Pen(C(140, 100, 66), 0.2f); b.Tube(new[] { foot, foot + q * V(0, 92f, 0) }, 3.6f, 3.6f, 5, 0.03f, 0.07f, true, false);
                b.Pen(Shade(steel, 0.9f), 0.7f); b.Box(foot + q * V(0, 96f, 0), V(34f, 18f, 18f), q, 0.07f, foot); b.Pieces++;
            }
            if (st.Trunk >= 3) // a lantern on a hook and the great scales
            {
                float y = bot + 190f; b.Pen(th.Trunk2, 0.85f); b.Tube(TreeMeshBuilder.Curve(V(10f, y, -4f), V(40f, y + 16f, -8f), V(44f, y - 6f, -10f), 6), 2f, 1.6f, 4, s.TrunkBirth(y), s.TrunkBirth(y) + 0.04f);
                b.Alive(1f, 0f, false, 1f); b.Pen(Alpha(Shade(th.ClothB, 0.55f), 240), 0.2f, 0.35f); b.Ball(V(44f, y - 22f, -10f), 10f, 8, 4, s.TrunkBirth(y) + 0.05f, 1.2f); b.Plain();
                b.Pen(Shade(th.Wood, 0.75f), 0.8f); b.Box(V(44f, y - 8.5f, -10f), V(16f, 3f, 16f), Quaternion.identity, s.TrunkBirth(y) + 0.05f); b.Box(V(44f, y - 35.5f, -10f), V(16f, 3f, 16f), Quaternion.identity, s.TrunkBirth(y) + 0.05f); b.Pieces++;
            }
            // the top: a brass cap, then a great coin
            b.Pen(th.Trunk2, 0.9f); b.Lathe(V(0, top - 4f, 0), Vector3.up, new[] { new Vector2(0, 15f), new Vector2(4f, 19f), new Vector2(16f, 12f), new Vector2(28f, 0.5f) }, 9, s.TrunkGrown, s.TrunkGrown + 0.03f); b.Pieces++;
            if (st.Trunk >= 4) { var c = V(0, top + 48f, -3f); float tb = s.TrunkGrown + 0.06f; b.Pen(th.Gold, 0.95f); b.Ring(c, 0f, 30f, 24, tb, c); b.Pen(Shade(th.Gold, 0.78f), 0.9f); b.Arc(c + V(0, 0, -0.6f), 22f, 25f, 0f, 360f, 24, tb + 0.02f, c); b.Plate(RectShape(13f, 13f), Lean(c + V(0, 0, -1.2f), 45f), 0.6f, tb + 0.03f, Vector2.zero, Vector2.zero); b.Pieces++; }
        }

        private static void Rifle(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom, top = s.Top - 20f, rec = bot + 150f, midY = (bot + top) * 0.5f;
            // the stock (a plate), the receiver, the barrel and the tube under it
            b.Pen(th.Trunk2, 0.25f); b.Plate(new List<Vector2> { new Vector2(-30f, 0), new Vector2(22f, 0), new Vector2(16f, 70f), new Vector2(12f, 150f), new Vector2(-12f, 150f), new Vector2(-14f, 78f) }, Lean(V(0, bot, 0), 0f), 15f, 0.02f, new Vector2(0, 150f), new Vector2(0, 70f), Shade(th.Trunk2, 0.75f)); b.Pieces++;
            b.Pen(th.Trunk, 0.8f); b.Box(V(0, rec + 36f, 0), V(42f, 80f, 24f), Quaternion.identity, s.TrunkBirth(rec), V(0, rec, 0)); b.Pieces++;
            b.Tube(new[] { V(-10f, rec + 70f, 0), V(-10f, top - 110f, 0) }, 7.4f, 7.4f, 7, s.TrunkBirth(rec + 70f), s.TrunkBirth(top - 110f));
            b.Tube(new[] { V(4f, rec + 70f, 0), V(4f, top, 0) }, 11f, 9.6f, 9, s.TrunkBirth(rec + 70f), s.TrunkGrown); b.Pieces++;
            b.Pen(Shade(th.Trunk, 0.7f), 0.8f); b.Lathe(V(4f, top - 26f, 0), Vector3.up, new[] { new Vector2(0, 10f), new Vector2(2f, 12.6f), new Vector2(24f, 12.6f), new Vector2(26f, 10f) }, 9, s.TrunkGrown - 0.02f, s.TrunkGrown);
            b.Pen(th.Gold, 0.85f); b.Tube(TreeMeshBuilder.Curve(V(-14f, rec + 14f, -3f), V(-34f, rec + 30f, -3f), V(-14f, rec + 50f, -3f), 6), 1.8f, 1.8f, 4, s.TrunkBirth(rec), s.TrunkBirth(rec) + 0.03f, false, false); // the trigger guard
            for (float y = rec + 190f; y < top - 130f; y += st.Trunk >= 2 ? 170f : 300f) { b.Pen(th.Gold, 0.85f); b.Lathe(V(0, y, 0), Vector3.up, new[] { new Vector2(0, 17f), new Vector2(2f, 19f), new Vector2(9f, 19f), new Vector2(11f, 17f) }, 9, s.TrunkBirth(y) + 0.02f, s.TrunkBirth(y) + 0.03f, false); b.Pieces++; }
            if (st.Trunk >= 1) { b.Pen(C(96, 62, 40), 0.15f); b.Tube(TreeMeshBuilder.Curve(V(-16f, bot + 40f, -9f), V(-58f, (bot + top) * 0.5f - 60f, -9f), V(-8f, top - 150f, -9f), 14), 4.4f, 4.4f, 4, 0.05f, s.TrunkGrown, false, false, 0.25f); b.Pieces++; } // the sling
            if (st.Trunk >= 2) // the scope
            { float y = rec + 96f; b.Pen(th.Gold, 0.9f); b.Lathe(V(4f, y, -15f), Vector3.up, new[] { new Vector2(0, 0.5f), new Vector2(0, 7.5f), new Vector2(14f, 5f), new Vector2(64f, 5f), new Vector2(78f, 8f), new Vector2(78f, 0.5f) }, 8, s.TrunkBirth(y), s.TrunkBirth(y + 78f)); b.Pieces++; }
            if (st.Trunk >= 2) // a belt of bullets slung across
                for (int i = 0; i < 7; i++) { var at = V(-26f + i * 9f, rec + 4f + i * 5.5f, -11f); b.Pen(th.LeafA, 0.9f); b.Lathe(at, Vector3.up, new[] { new Vector2(0, 3f), new Vector2(11f, 3f), new Vector2(16f, 0.3f) }, 5, 0.08f + i * 0.004f, 0.09f + i * 0.004f); b.Pieces++; }
            if (st.Trunk >= 3) StarPlate(b, V(0, rec + 36f, -10f), 5, 13f, 5.4f, 0f, th.Gold, 0.95f, 0.2f, s.TrunkBirth(rec) + 0.05f); // the badge
            // Rebellion (Ornament >= 8): Twin Crossed Six-Shooter Revolvers & Rotary Gatling Drum
            if (s.Ornament >= 8)
            {
                float rb = s.TrunkBirth(midY) + 0.04f;
                b.Pen(th.Metal, 0.92f); b.Lathe(V(0, midY - 10f, -12f), Vector3.forward, new[] { new Vector2(0, 26f), new Vector2(18f, 26f) }, 10, rb, rb + 0.03f); b.Pieces++;
                for (int c = 0; c < 6; c++) { float a = c * 60f * Mathf.Deg2Rad; Bead(b, V(Mathf.Sin(a) * 16f, midY - 10f + Mathf.Cos(a) * 16f, -21f), 4.5f, th.Gold, 0.95f, 0.3f, rb + 0.04f); }
                foreach (int sg in new[] { -1, 1 })
                {
                    var d = V(sg * 0.55f, 0.83f).normalized;
                    b.Pen(th.Metal, 0.9f); b.Tube(new[] { V(-sg * 24f, midY - 26f, -14f), V(sg * 58f, midY + 46f, -14f) }, 5.5f, 4.5f, 6, rb + 0.02f, rb + 0.06f); b.Pieces++;
                    b.Pen(th.Gold, 0.95f); b.Box(V(-sg * 12f, midY - 14f, -14f), V(16f, 22f, 10f), Quaternion.FromToRotation(Vector3.up, d), rb + 0.03f); b.Pieces++;
                }
            }
            if (st.Trunk >= 4)
            {
                var c = V(4f, top + 30f, -3f); float tb = s.TrunkGrown + 0.06f;
                foreach (int sg in new[] { -1, 1 }) for (int f = 0; f < 3; f++) LeafPlate(b, c + V(sg * (8f + 4f * f), -10f, -f), sg * (70f - 20f * f), 50f - 8f * f, 14f, C(226, 226, 232), 0.4f, 0f, tb + 0.01f * f, C(180, 180, 192));
                StarPlate(b, c, 5, 22f, 9f, 0f, th.Gold, 0.95f, 0.3f, tb + 0.04f);
            }
            // Night Watch (Ornament >= 14): Orbital Tactical Laser Sight Ring & Twin Heavy Artillery Shells
            if (s.Ornament >= 14)
            {
                float nb = s.TrunkGrown + 0.05f;
                Hoop(b, V(0, midY + 55f, -6f), 68f, 38f, -16f, 2.6f, true, th.Glow, 0.95f, 0.9f, nb);
                foreach (int sg in new[] { -1, 1 })
                {
                    b.Pen(th.Gold, 0.95f);
                    b.Lathe(V(sg * 54f, midY - 50f, -12f), Vector3.up, new[] { new Vector2(0, 7f), new Vector2(28f, 7f), new Vector2(44f, 4f), new Vector2(56f, 0.5f) }, 8, nb + 0.02f, nb + 0.05f);
                    b.Pieces++;
                }
            }
        }

        private static void Katana(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom, guard = s.Top - 170f, h = guard - bot, midY = (bot + guard) * 0.5f;
            // the sheath, a little curved, its foot cap; the guard; the grip with its wrap
            var p = new List<Vector3>(17); for (int i = 0; i <= 16; i++) { float t = i / 16f; p.Add(V(-12f * Mathf.Sin(t * Mathf.PI) + 4f, bot + h * t, 0)); }
            b.Pen(th.Trunk, 0.85f); b.Tube(p, t => 15f + 4f * t, 9, 0f, s.TrunkBirth(guard), true, false, 0.6f); b.Pieces++;
            b.Pen(th.Gold, 0.9f); b.Lathe(V(4f, bot - 2f, 0), Vector3.up, new[] { new Vector2(0, 0.5f), new Vector2(3f, 12f), new Vector2(22f, 16f), new Vector2(24f, 14.6f) }, 9, 0f, 0.02f);
            if (st.Trunk >= 2) for (int i = 1; i <= 3; i++) { var at = PathAt(p, i * 0.24f, out var d); float r = 16.2f + 4f * i * 0.24f; b.Pen(th.Gold, 0.9f); b.Lathe(at, d, new[] { new Vector2(0, r), new Vector2(2f, r + 1.2f), new Vector2(7f, r + 1.2f), new Vector2(9f, r) }, 8, s.TrunkBirth(at.y) + 0.02f, s.TrunkBirth(at.y) + 0.03f); b.Pieces++; }
            if (st.Trunk >= 1) // the cord tied round the sheath, and its tails
            {
                var at = PathAt(p, 0.8f, out var d); b.Pen(th.Trunk2, 0.15f); b.Lathe(at, d, new[] { new Vector2(0, 18.4f), new Vector2(3f, 19.8f), new Vector2(11f, 19.8f), new Vector2(14f, 18.4f) }, 9, s.TrunkBirth(at.y) + 0.02f, s.TrunkBirth(at.y) + 0.03f);
                foreach (int sg in new[] { -1, 1 }) b.Tube(TreeMeshBuilder.Curve(at + V(0, 4f, -9f), at + V(sg * 14f, -20f, -10f), at + V(sg * 9f, -66f, -10f), 7), 2f, 1.6f, 4, s.TrunkBirth(at.y) + 0.04f, s.TrunkBirth(at.y) + 0.09f); b.Pieces++;
                if (st.Trunk >= 2) foreach (int sg in new[] { -1, 1 }) Tassel(b, at + V(sg * 9f, -66f, -10f), 20f, th.Trunk2, s.TrunkBirth(at.y) + 0.1f);
            }
            float tg = s.TrunkBirth(guard);
            b.Pen(C(150, 120, 70), 0.85f); b.Lathe(V(4f, guard - 3f, 0), Vector3.up, new[] { new Vector2(0, 0.5f), new Vector2(0, 38f), new Vector2(6f, 38f), new Vector2(6f, 0.5f) }, 12, tg, tg + 0.02f, false); b.Pieces++;
            for (int i = 0; i < 7; i++) { b.Pen(i % 2 == 0 ? C(34, 32, 38) : th.ClothA, 0.2f); b.Lathe(V(4f, guard + 4f + i * 18f, 0), Vector3.up, new[] { new Vector2(0, 12f), new Vector2(18f, 12f) }, 8, s.TrunkBirth(guard + i * 18f), s.TrunkBirth(guard + i * 18f + 18f)); }
            b.Pen(th.Gold, 0.9f); b.Lathe(V(4f, guard + 130f, 0), Vector3.up, new[] { new Vector2(0, 12.6f), new Vector2(8f, 13f), new Vector2(13f, 0.5f) }, 8, s.TrunkBirth(guard + 130f), s.TrunkBirth(guard + 142f)); b.Pieces++;
            // Kagerou / Oboro (Ornament >= 8): Giant Four-Pointed Fuuma Shuriken Windmill + Twin Crossed Kunai + Unfurling Ninjutsu Scroll
            if (s.Ornament >= 8)
            {
                float y = midY, kb = s.TrunkBirth(y) + 0.04f;
                foreach (int sg in new[] { -1, 1 }) { var d = V(sg * 0.5f, -0.866f); var a = V(-sg * 26f, y + 44f, -9f); b.Pen(th.Metal, 0.9f); b.Blade(a, d, V(d.y, -d.x), 78f, x => 9f * (x < 0.2f ? x / 0.2f : 1f - (x - 0.2f) / 0.8f), x => 2.6f * (1f - x), 4, kb, kb + 0.04f); b.Pen(th.Trunk2, 0.2f); b.Lathe(a - d * 26f, d, new[] { new Vector2(0, 2.4f), new Vector2(26f, 2.8f) }, 5, kb, kb + 0.02f); b.Pen(th.Metal, 0.9f); b.Arc(a - d * 32f + V(0, 0, -0.5f), 4f, 6.4f, 0f, 360f, 10, kb, a); b.Pieces++; }
                StarPlate(b, V(4f, y, -14f), 4, 48f, 14f, 22f, th.Metal, 0.95f, 0.2f, kb + 0.05f);
                b.Pen(th.Gold, 0.95f); b.Ring(V(4f, y, -16f), 6f, 13f, 14, kb + 0.06f, V(4f, y, -16f)); b.Pieces++;
            }
            // Shinkiro / Shiranui (Ornament >= 14): Twin Shadow-Clone Wakizashi Blades + Blazing Kitsune Fox-Fire Orbs
            if (s.Ornament >= 14)
            {
                float sb = s.TrunkGrown + 0.05f;
                for (int i = 0; i < 4; i++)
                {
                    float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                    Bead(b, V(Mathf.Cos(a) * 62f, midY + 40f + Mathf.Sin(a) * 42f, -16f), 9.5f, th.Glow, 0.95f, 0.95f, sb + i * 0.01f);
                }
            }
        }

        private static void Post(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom, top = s.Top - 40f, h = top - bot, midY = (bot + top) * 0.5f, belt = bot + Mathf.Min(260f, h * 0.36f); var beltCol = BeltColour(s.Ornament);
            b.Pen(Shade(th.Trunk, 0.7f), 0.2f); b.Box(V(0, bot + 6f, 0), V(120f, 12f, 70f), Quaternion.identity, 0f); b.Pieces++;
            b.Pen(th.Trunk, 0.2f); b.Lathe(V(0, bot + 12f, 0), Vector3.up, new[] { new Vector2(0, 24f), new Vector2(h * 0.5f, 22f), new Vector2(h - 16f, 21f), new Vector2(h - 12f, 14f), new Vector2(h - 12f, 0.5f) }, 12, 0.01f, s.TrunkGrown); b.Pieces++;
            if (st.Trunk >= 2) // the padded striking parts
                foreach (float f in new[] { 0.62f, 0.82f }) { float y = bot + h * f; b.Pen(C(170, 50, 46), 0.1f); b.Lathe(V(0, y, 0), Vector3.up, new[] { new Vector2(0, 22.4f), new Vector2(5f, 27f), new Vector2(47f, 27f), new Vector2(52f, 22f) }, 12, s.TrunkBirth(y), s.TrunkBirth(y + 52f)); b.Pieces++; }
            // the belt, its knot and tails
            b.Pen(beltCol, 0.1f); b.Lathe(V(0, belt, 0), Vector3.up, new[] { new Vector2(0, 23.6f), new Vector2(2f, 25f), new Vector2(24f, 25f), new Vector2(26f, 23.4f) }, 12, s.TrunkBirth(belt), s.TrunkBirth(belt) + 0.03f); b.Pieces++;
            b.Box(V(0, belt + 13f, -26f), V(20f, 16f, 8f), Quaternion.identity, s.TrunkBirth(belt) + 0.04f);
            foreach (int sg in new[] { -1, 1 }) b.Plate(RectShape(13f, 74f), Lean(V(sg * 8f, belt - 22f, -27f), sg * 14f), 2f, s.TrunkBirth(belt) + 0.06f, new Vector2(0, 37f), Vector2.zero, Shade(beltCol, 0.75f));
            if (s.Ornament >= 12) foreach (int sg in new[] { -1, 1 }) { b.Pen(th.Gold, 0.9f); b.Plate(RectShape(13.6f, 5f), Lean(V(sg * 15f, belt - 50f, -29.2f), sg * 14f), 0.8f, s.TrunkBirth(belt) + 0.08f, Vector2.zero, Vector2.zero); b.Pieces++; }
            // Star Gladiator / Sky Emperor: Solar, Lunar & Stellar Astrolabe Rings + Blazing Sunburst & Crescent Moon
            if (th.Line == JobLine.StarGladiator)
            {
                float mb = s.TrunkBirth(midY) + 0.03f;
                Hoop(b, V(0, midY, 0), 74f, 44f, 22f, 2.8f, true, th.Gold, 0.95f, 0.65f, mb);
                Hoop(b, V(0, midY, 0), 66f, 40f, -24f, 2.4f, true, th.LeafA, 0.9f, 0.75f, mb + 0.02f);
                // Blazing Solar Emblem & Silver Crescent Moon flanking the martial pillar
                StarPlate(b, V(-52f, midY + 18f, -14f), 8, 28f, 12f, 0f, th.Gold, 0.95f, 0.85f, mb + 0.03f);
                b.Pen(C(220, 235, 255), 0.95f, 0.75f);
                b.Arc(V(52f, midY + 18f, -14f), 14f, 24f, -40f, 220f, 18, mb + 0.04f, V(52f, midY + 18f, -14f)); b.Pieces++;
                if (s.Ornament >= 14) // Sky Emperor: Grand Constellation Zodiac Crown & Twin Celestial Dragon-Stars
                {
                    float tb = s.TrunkGrown + 0.05f;
                    Hoop(b, V(0, top - 10f, 0), 86f, 50f, 8f, 3.2f, true, th.Glow, 0.95f, 0.9f, tb);
                    foreach (int sg in new[] { -1, 1 })
                        StarPlate(b, V(sg * 68f, top + 12f, -12f), 6, 24f, 9f, sg * 15f, th.Glow, 0.95f, 0.9f, tb + 0.02f);
                }
            }
            // Soul Linker / Soul Ascetic: Twin Ethereal Ancestral Spirit Wisps + Ritual Soul-Mirror Talisman Ring
            else if (th.Line == JobLine.SoulLinker)
            {
                float mb = s.TrunkBirth(midY) + 0.03f;
                Twist(b, s, bot + 70f, top - 20f, 6.5f, 4.2f, 36f, 180f, th.LeafA, th.ClothB, 0.9f);
                // Floating Eight-Sided Ritual Soul-Mirror & Paper Ofuda Talismans at midY
                b.Pen(th.Gold, 0.95f, 0.5f);
                b.Ring(V(0, midY + 20f, -16f), 22f, 34f, 8, mb + 0.03f, V(0, midY + 20f, -16f)); b.Pieces++;
                Bead(b, V(0, midY + 20f, -18f), 14f, th.Glow, 0.95f, 0.95f, mb + 0.04f);
                foreach (int sg in new[] { -1, 1 })
                {
                    b.Pen(th.ClothA, 0.2f);
                    b.Plate(RectShape(16f, 54f), Lean(V(sg * 48f, midY + 8f, -14f), sg * 16f), 1.4f, mb + 0.04f, Vector2.zero, Vector2.zero, th.Gold);
                    b.Pieces++;
                }
                if (s.Ornament >= 14) // Soul Ascetic: Eight-Trigram Taoist Halo & Yin-Yang Spirit Orbs
                {
                    float tb = s.TrunkGrown + 0.05f;
                    b.Pen(th.Gold, 0.95f, 0.7f);
                    b.Arc(V(0, top + 18f, -8f), 44f, 50f, 0f, 360f, 16, tb, V(0, top + 18f, -8f)); b.Pieces++;
                    foreach (int sg in new[] { -1, 1 })
                        Bead(b, V(sg * 28f, top + 18f, -12f), 11f, sg < 0 ? th.LeafA : th.ClothB, 0.95f, 0.95f, tb + 0.02f);
                }
            }
            // the rings' near halves, with their charms
            for (int i = 0; i < Mathf.Max(0, st.Trunk - 2); i++)
            {
                float y = Mathf.Lerp(bot + 220f, top - 160f, i * 0.6f + 0.2f), yb = s.TrunkBirth(y) + 0.05f;
                Hoop(b, V(0, y, 0), 52f, 34f, i % 2 == 0 ? 14f : -12f, 1.4f, true, th.Gold, 0.9f, 0.3f, yb);
                StarPlate(b, V(i % 2 == 0 ? -44f : 46f, y - 4f, -22f), 5, 10f, 4.2f, 0f, th.LeafA, 0.9f, 0.4f, yb + 0.02f);
                b.Pen(C(250, 244, 220), 0.4f, 0.5f); b.Ring(V(i % 2 == 0 ? 30f : -28f, y - 10f, -30f), 0f, 7f, 12, yb + 0.03f, V(0, y, 0)); b.Pieces++;
            }
            if (st.Trunk >= 1) { b.Pen(th.Trunk2, 0.1f); b.Lathe(V(0, top - 16f, 0), Vector3.up, new[] { new Vector2(0, 21.4f), new Vector2(3f, 23f), new Vector2(13f, 23f), new Vector2(16f, 15f) }, 12, s.TrunkGrown - 0.02f, s.TrunkGrown); b.Pieces++; }
            if (st.Trunk >= 4) // a crown of stars joined by light
            {
                float tb = s.TrunkGrown + 0.05f; Vector3 prev = default;
                for (int i = 0; i < 5; i++)
                {
                    float a = (i - 2f) * 0.42f; var c = V(Mathf.Sin(a) * 92f, top + 22f + Mathf.Cos(a) * 46f, -4f);
                    if (i > 0) { b.Pen(Alpha(th.Glow, 150), 0f, 1f); b.Line(prev, c, 1.4f, tb + i * 0.01f, tb + i * 0.01f + 0.02f); }
                    StarPlate(b, c + V(0, 0, -1f), 5, i == 2 ? 17f : 12f, i == 2 ? 7f : 5f, 0f, th.Gold, 0.95f, 0.4f, tb + i * 0.012f); prev = c;
                }
            }
        }

        private static void Stalk(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom, top = s.Top - 110f, h = top - bot, midY = (bot + top) * 0.5f;
            if (st.Trunk >= 1) // the fishing rod leaning behind, its line and a fish
            {
                Vector3 a = V(40f, bot, 6f), c = V(96f, bot + Mathf.Min(420f, h * 0.6f), 6f);
                b.Pen(C(150, 112, 76), 0.25f); b.Tube(new[] { a, c }, 3f, 1.2f, 5, 0.02f, 0.2f);
                b.Pen(C(236, 236, 240), 0.2f); b.Line(c, c + V(0, -70f, 0), 0.9f, 0.2f, 0.26f);
                b.Pen(th.ClothA, 0.6f); b.Plate(Fish(18f, 8.5f), Lean(c + V(0, -90f, -2f), -90f), 2.4f, 0.27f, new Vector2(-18f, 0), Vector2.zero, Shade(th.ClothA, 0.7f)); b.Pieces += 2;
            }
            foreach (int sg in new[] { -1, 1 }) LeafPlate(b, V(sg * 4f, bot + 4f, -3f), sg * 62f, 96f, 46f, sg < 0 ? th.LeafA : th.LeafB, 0.2f, 0f, 0.03f, Shade(th.LeafA, 0.75f));
            if (st.Trunk >= 2) LeafPlate(b, V(0, bot + 4f, -5f), 8f, 70f, 34f, th.LeafB, 0.2f, 0f, 0.05f, Shade(th.LeafA, 0.75f));
            var p = new List<Vector3>(21); for (int i = 0; i <= 20; i++) { float t = i / 20f; p.Add(V(9f * Mathf.Sin(t * 5.2f) * (1f - t * 0.6f), bot + h * t, 0)); }
            b.Pen(th.Trunk, 0.25f); b.Tube(p, t => 17f - 7f * t, 8, 0f, s.TrunkBirth(top)); b.Pieces++;
            // the cattail's head
            b.Pen(th.Trunk2, 0.05f); b.Lathe(V(p[20].x, top - 4f, 0), Vector3.up, new[] { new Vector2(0, 9f), new Vector2(12f, 22f), new Vector2(60f, 25f), new Vector2(92f, 16f), new Vector2(106f, 2f) }, 10, s.TrunkBirth(top), s.TrunkGrown); b.Pieces++;
            if (st.Trunk >= 2) // flowers up the stalk
                for (int i = 0; i < 2 + st.Trunk; i++) { var at = PathAt(p, (i + 0.8f) / (3.4f + st.Trunk), out _); int sg = i % 2 == 0 ? -1 : 1; StarPlate(b, at + V(sg * 15f, 0, -9f), 5, 11f, 6.8f, i * 23f, th.ClothB, 0.2f, 0f, s.TrunkBirth(at.y) + 0.04f); Bead(b, at + V(sg * 15f, 0, -11f), 2.8f, C(250, 220, 110), 0.3f, 0.2f, s.TrunkBirth(at.y) + 0.05f); }
            if (st.Trunk >= 3) // a paw charm and a carrot
            {
                float y = bot + h * 0.3f; b.Pen(th.ClothB, 0.2f); b.Ring(V(-30f, y, -10f), 0f, 11f, 12, s.TrunkBirth(y) + 0.05f, V(0, y, 0)); foreach (var o in new[] { V(-10f, 12f), V(0f, 16f), V(10f, 12f) }) b.Ring(V(-30f, y, -10.5f) + o, 0f, 4.2f, 8, s.TrunkBirth(y) + 0.06f, V(0, y, 0)); b.Pieces++;
                b.Pen(C(236, 140, 60), 0.2f); b.Lathe(V(34f, y + 70f, -10f), Vector3.down, new[] { new Vector2(0, 8f), new Vector2(20f, 6f), new Vector2(44f, 0.5f) }, 7, s.TrunkBirth(y + 70f) + 0.05f, s.TrunkBirth(y + 70f) + 0.08f); LeafPlate(b, V(34f, y + 70f, -10f), 10f, 20f, 10f, th.LeafA, 0.2f, 0f, s.TrunkBirth(y + 70f) + 0.08f);
            }
            if (st.Trunk >= 4) // a spirit orb over the head, leaves circling it
            {
                var c = V(p[20].x, top + 150f, -3f); float tb = s.TrunkGrown + 0.06f;
                b.Pen(Alpha(th.Glow, 70), 0f, 1f); b.Ring(c + V(0, 0, 1f), 0f, 38f, 20, tb, c);
                Bead(b, c, 17f, th.Glow, 0.9f, 0.7f, tb + 0.02f);
                for (int i = 0; i < 5; i++) { float a = i * 72f; LeafPlate(b, c + Quaternion.Euler(0, 0, -a) * V(0, 30f, -2f), a + 70f, 18f, 9f, i % 2 == 0 ? th.LeafA : th.LeafB, 0.2f, 0.2f, tb + 0.04f + i * 0.008f); }
            }
            // Spirit Handler (Ornament >= 14): Three Guardian Spirit Totem Orbs (Chul Ho Tiger, Hyun Rok Deer, Diabolic Cat) & Sacred Bell Ring
            if (s.Ornament >= 14)
            {
                float sb = s.TrunkGrown + 0.05f;
                Hoop(b, V(0, midY + 40f, 0), 68f, 40f, 14f, 2.6f, true, th.Gold, 0.95f, 0.6f, sb);
                var spCols = new[] { C(255, 168, 64), C(96, 240, 175), C(210, 120, 255) };
                for (int i = 0; i < 3; i++)
                {
                    float a = (i * 120f - 90f) * Mathf.Deg2Rad;
                    Bead(b, V(Mathf.Cos(a) * 56f, midY + 40f + Mathf.Sin(a) * 32f, -14f), 12f, spCols[i], 0.95f, 0.9f, sb + 0.02f + i * 0.01f);
                }
            }
        }

        private static void Sapling(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom, top = s.Top - 70f, h = top - bot;
            var p = new List<Vector3>(17); for (int i = 0; i <= 16; i++) { float t = i / 16f; p.Add(V(7f * Mathf.Sin(t * 4.4f) * (1f - t * 0.5f), bot + h * t, 0)); }
            b.Pen(th.Trunk, 0.15f); b.Tube(p, t => 18f - 9f * t, 8, 0f, s.TrunkGrown); b.Pieces++;
            if (st.Trunk >= 2) { Hoop(b, V(13f, bot + 190f, 2f), 23f, 13f, 4f, 1.6f, true, C(226, 214, 190), 0.1f, 0f, s.TrunkBirth(bot + 190f) + 0.02f); } // the rope to the stake
            if (st.Trunk >= 1) // the wooden sign at its foot
            {
                b.Pen(Shade(th.Trunk, 0.8f), 0.1f); b.Tube(new[] { V(-46f, bot, -8f), V(-46f, bot + 96f, -8f) }, 3.6f, 3.6f, 5, 0.01f, 0.06f, true, true);
                b.Pen(th.Trunk2, 0.15f); b.Plate(RectShape(58f, 34f), Lean(V(-46f, bot + 84f, -12.5f), 4f), 3.4f, 0.07f, Vector2.zero, Vector2.zero, Shade(th.Trunk, 0.8f)); b.Pieces++;
                b.Pen(Shade(th.Trunk, 0.6f), 0.1f); foreach (float y in new[] { 6f, -4f }) b.Plate(RectShape(38f, 2.6f), Lean(V(-46f, bot + 84f + y, -14.6f), 4f), 0.5f, 0.09f, Vector2.zero, Vector2.zero);
            }
            // the crown at its top: BUDS that open into leaves one after the other as the tree comes alive (design note: "a simple budding
            // sapling"; the Novice's own tree has three, nothing else)
            int crown = s.Bare ? 0 : 3 + st.Trunk * 2; // (the stake of the skills from outside the trees has none)
            for (int i = 0; i < crown; i++)
            {
                float a = (i - (crown - 1) * 0.5f) * (150f / crown), from = 0.05f + 0.8f * (((i * 2) % crown) / (float)crown);
                LeafPlate(b, V(p[16].x, top - 4f, -1f - (i % 2)), a, 44f + 5f * (i % 3), 24f, Shade(i % 2 == 0 ? th.LeafA : th.LeafB, 0.8f), 0.2f, 0f, s.TrunkGrown + 0.01f * i, Shade(th.LeafA, 0.7f), 0.8f, true, from);
            }
            if (st.Trunk >= 3) // the Super Novice's charms: a little sword, staff and bow tied to the trunk
            {
                float y = bot + h * 0.42f, cb = s.TrunkBirth(y) + 0.05f;
                b.Pen(C(190, 196, 206), 0.9f); b.Blade(V(-22f, y, -12f), V(-0.2f, -1f).normalized, Vector3.right, 44f, x => 4.4f * (1f - x * x), x => 1.6f * (1f - x), 3, cb, cb + 0.02f); b.Box(V(-22f, y + 1f, -12f), V(15f, 3f, 3.6f), Quaternion.identity, cb); b.Pieces++;
                b.Pen(C(104, 74, 54), 0.2f); b.Tube(new[] { V(20f, y - 40f, -12f), V(26f, y + 16f, -12f) }, 2f, 2f, 5, cb, cb + 0.02f); Bead(b, V(26.5f, y + 21f, -12f), 5.2f, C(128, 190, 255), 0.9f, 0.5f, cb + 0.03f);
                b.Pen(C(216, 198, 160), 0.3f); b.Tube(TreeMeshBuilder.Curve(V(0, y + 96f, -13f), V(22f, y + 118f, -13f), V(0, y + 140f, -13f), 8), 2f, 2f, 5, cb + 0.04f, cb + 0.06f); b.Pieces++;
            }
            if (st.Trunk >= 4) // the guardian angel's wings and halo
            {
                float tb = s.TrunkGrown + 0.07f; var c = V(p[16].x, top + 6f, 3f);
                foreach (int sg in new[] { -1, 1 }) for (int f = 0; f < 4; f++) LeafPlate(b, c + V(sg * (10f + 5f * f), 6f - 5f * f, f * 0.6f), sg * (58f + 14f * f), 86f - 13f * f, 22f, C(250, 250, 252), 0.3f, 0.15f, tb + 0.012f * f, C(214, 222, 236));
                b.Pen(Alpha(C(255, 236, 150), 235), 0.3f, 1f); b.Arc(V(p[16].x, top + 86f, -4f), 26f, 30f, 0f, 360f, 22, tb + 0.06f, V(p[16].x, top + 86f, -4f)); b.Pieces++;
            }
            // Hyper Novice (Ornament >= 14): Radiant Star-Crown & Floating Guardian Angel Scepter Halo
            if (s.Ornament >= 14)
            {
                float hb = s.TrunkGrown + 0.08f; var c = V(p[16].x, top + 86f, -6f);
                StarPlate(b, c, 8, 24f, 10f, 0f, th.Gold, 0.95f, 0.85f, hb);
                Hoop(b, V(0, bot + h * 0.65f, 0), 54f, 32f, -12f, 2.2f, true, th.Gold, 0.95f, 0.6f, hb);
            }
        }

        // ==================================================================
        // Bespoke 3D Sculptures per Second/Trans-Second/Third/Fourth Class Line
        // ==================================================================

        /// <summary>Knight (5) / Lord Knight (8) / Rune Knight (11) / Dragon Knight (14).</summary>
        private static void KnightLance(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom, top = s.Top - 30f, vamplate = bot + 165f, midY = (bot + top) * 0.5f;
            // Counterweight grip & pommel below the vamplate
            b.Pen(th.Trunk2, 0.3f);
            b.Lathe(V(0, bot, 0), Vector3.up, new[] { new Vector2(0, 18f), new Vector2(18f, 13f), new Vector2(140f, 14f), new Vector2(165f, 20f) }, 10, 0f, s.TrunkBirth(vamplate)); b.Pieces++;
            // Conical steel vamplate (hand guard cone) with gold rim
            b.Pen(th.Metal, 0.9f);
            b.Lathe(V(0, vamplate, 0), Vector3.up, new[] { new Vector2(0, 48f), new Vector2(6f, 48f), new Vector2(44f, 34f), new Vector2(52f, 28f) }, 12, s.TrunkBirth(vamplate), s.TrunkBirth(vamplate + 52f)); b.Pieces++;
            b.Pen(th.Gold, 0.95f);
            b.Lathe(V(0, vamplate - 1f, 0), Vector3.up, new[] { new Vector2(0, 49f), new Vector2(4f, 51f), new Vector2(8f, 49f) }, 12, s.TrunkBirth(vamplate), s.TrunkBirth(vamplate + 10f)); b.Pieces++;
            // Fluted cavalry lance cone tapering up to the spearhead
            float lanceTop = top - 70f, lanceH = lanceTop - (vamplate + 48f);
            b.Alive(0.75f, 0f, false, 0f, true); b.Pen(th.Trunk, 0.88f);
            b.Lathe(V(0, vamplate + 48f, 0), Vector3.up, new[] { new Vector2(0, 29f), new Vector2(lanceH * 0.35f, 23f), new Vector2(lanceH * 0.75f, 15f), new Vector2(lanceH, 8f) }, 10, s.TrunkBirth(vamplate + 48f), s.TrunkBirth(lanceTop)); b.Plain(); b.Pieces++;
            // Spiraling golden dragon-ribbon around the lance shaft
            Twist(b, s, vamplate + 54f, lanceTop - 10f, 5.5f, 3.2f, 24f, 210f, th.Gold, th.Trunk2, 0.9f);
            // Diamond-forged halberd/lance tip at the crown
            Tip(b, V(0, lanceTop - 6f, -2f), Vector3.up, 115f, 18f, th.Metal, 0.95f, s.TrunkGrown);
            // Royal Heater Shield mounted over the vamplate; its central vertical fuller/cross bar fills with warm orange-red colour as _Life increases!
            float sy = midY - 35f, sb = s.TrunkBirth(sy) + 0.04f;
            b.Pen(th.Trunk2, 0.35f); b.Plate(ShieldShape(84f, 108f), Lean(V(0, sy, -11f), 0f), 4f, sb, Vector2.zero, Vector2.zero, th.Gold); b.Pieces++;
            b.Alive(1f); b.Pen(Shade(th.Trunk, 0.45f), 0.9f);
            b.Plate(RectShape(14f, 84f), Lean(V(0, sy + 2f, -14f), 0f), 1.4f, sb + 0.02f, Vector2.zero, Vector2.zero);
            b.Plain(); b.Pieces++;
            b.Pen(th.Gold, 0.95f);
            b.Plate(RectShape(56f, 12f), Lean(V(0, sy - 12f, -14f), 0f), 1.4f, sb + 0.02f, Vector2.zero, Vector2.zero);
            Bead(b, V(0, sy - 12f, -15.5f), 7.5f, th.LeafB, 0.95f, 0.55f, sb + 0.03f);
            // Tier 3 (Lord Knight, Ornament >= 8): Valkyrie Wings spreading from the shield shoulders + Twin Crossed Crimson Pennant Lances
            if (s.Ornament >= 8)
            {
                foreach (int sg in new[] { -1, 1 })
                {
                    for (int f = 0; f < 4; f++)
                        LeafPlate(b, V(sg * (34f + 7f * f), sy + 24f - 8f * f, -9f - f * 0.5f), sg * (54f + 14f * f), 84f - 12f * f, 21f, C(238, 242, 250), 0.6f, 0.1f, sb + 0.03f + 0.01f * f, th.Gold);
                    var a0 = V(-sg * 28f, sy - 46f, -6f); var a1 = V(sg * 78f, sy + 96f, -6f);
                    b.Pen(th.Gold, 0.92f); b.Tube(new[] { a0, a1 }, 3.2f, 2.2f, 6, sb + 0.02f, sb + 0.06f);
                    Tip(b, a1, (a1 - a0).normalized, 32f, 8f, th.Metal, 0.95f, sb + 0.06f);
                }
            }
            // Tier 4 (Rune Knight, Ornament >= 11): Swept Fertera Dragon Wings & Horns + 4 Orbiting Glowing Elder Runestones
            if (s.Ornament >= 11)
            {
                var runeCols = new[] { C(88, 210, 255), C(255, 110, 70), C(120, 250, 160), C(255, 215, 90) };
                for (int r = 0; r < 4; r++)
                {
                    float ry = Mathf.Lerp(vamplate + 70f, lanceTop - 30f, r / 3f); int sg = r % 2 == 0 ? -1 : 1;
                    var rAt = V(sg * 58f, ry, -16f);
                    b.Alive(1f, 0f, false, 0.8f); b.Pen(runeCols[r], 0.95f, 0.9f);
                    b.Plate(Diamond(22f, 36f), Lean(rAt, sg * 14f), 3.5f, s.TrunkBirth(ry) + 0.04f, Vector2.zero, new Vector2(0, 18f), th.Gold);
                    b.Plain(); b.Pieces++;
                }
                foreach (int sg in new[] { -1, 1 })
                {
                    b.Pen(th.Trunk2, 0.85f);
                    b.Tube(TreeMeshBuilder.Curve(V(sg * 16f, lanceTop - 22f, -4f), V(sg * 64f, lanceTop + 18f, -8f), V(sg * 38f, lanceTop + 74f, -10f), 8), 8.5f, 2.5f, 6, s.TrunkGrown, s.TrunkGrown + 0.05f);
                    b.Pieces++;
                }
            }
            // Tier 5 (Dragon Knight, Ornament >= 14): Twin Colossal Sovereign Dragon-Fang Greatswords & Blazing Astral Dragon Halo
            if (s.Ornament >= 14)
            {
                float db = s.TrunkGrown + 0.05f;
                Hoop(b, V(0, lanceTop - 10f, 0), 82f, 48f, 14f, 3.2f, true, th.Gold, 0.95f, 0.8f, db);
                foreach (int sg in new[] { -1, 1 })
                {
                    var d = V(sg * 0.42f, 0.91f).normalized;
                    b.Alive(0.9f, 0f, false, 0.5f, true); b.Pen(th.Glow, 0.95f, 0.8f);
                    b.Blade(V(sg * 24f, midY + 10f, -8f), d, V(d.y, -d.x), 170f, x => 15f * (1f - x * 0.7f), x => 4.5f * (1f - x), 6, db, db + 0.04f, th.Gold);
                    b.Plain(); b.Pieces++;
                }
            }
        }

        /// <summary>Crusader (5) / Paladin (8) / Royal Guard (11) / Imperial Guard (14).</summary>
        private static void CrusaderShieldCross(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom, top = s.Top - 24f, midY = (bot + top) * 0.5f;
            // Holy Avenger blade running full height through the center
            b.Alive(0.8f, 0f, false, 0f, true); b.Pen(th.Trunk, 0.92f);
            b.Blade(V(0, bot, 0), Vector3.up, Vector3.right, top - bot - 90f, x => x < 0.08f ? 32f * x / 0.08f : 32f, x => 9f, 10, 0f, s.TrunkBirth(top - 90f), th.Gold); b.Plain(); b.Pieces++;
            // Swept golden Crossguard near the top
            float gy = top - 92f, gb = s.TrunkBirth(gy);
            b.Pen(th.Gold, 0.95f); b.Box(V(0, gy, -2f), V(190f, 22f, 18f), Quaternion.identity, gb, V(0, gy, -2f)); b.Pieces++;
            // Ivory & gold grip + radiant halo pommel
            b.Pen(th.Trunk2, 0.5f); b.Lathe(V(0, gy + 11f, 0), Vector3.up, new[] { new Vector2(0, 12f), new Vector2(38f, 14f), new Vector2(76f, 12f) }, 10, gb, s.TrunkGrown); b.Pieces++;
            Bead(b, V(0, top + 4f, -2f), 18f, th.Gold, 0.95f, 0.6f, s.TrunkGrown);
            // Monumental Sacred Tower Shield + embossed glowing Grand Cross at the heart of the tree
            float sb = s.TrunkBirth(midY);
            b.Pen(th.Trunk, 0.75f); b.Plate(ShieldShape(112f, 168f), Lean(V(0, midY, -8f), 0f), 5f, sb, Vector2.zero, Vector2.zero, th.Gold); b.Pieces++;
            b.Pen(th.Trunk2, 0.5f); b.Plate(ShieldShape(92f, 142f), Lean(V(0, midY, -10.5f), 0f), 2f, sb + 0.02f, Vector2.zero, Vector2.zero); b.Pieces++;
            b.Alive(1f, 0f, false, 0.6f); b.Pen(th.Gold, 0.95f, 0.45f);
            b.Plate(RectShape(22f, 132f), Lean(V(0, midY + 2f, -12.5f), 0f), 2.5f, sb + 0.03f, Vector2.zero, Vector2.zero);
            b.Plate(RectShape(84f, 22f), Lean(V(0, midY + 18f, -12.5f), 0f), 2.5f, sb + 0.03f, Vector2.zero, Vector2.zero);
            b.Plain(); b.Pieces += 2;
            Bead(b, V(0, midY + 18f, -14.5f), 12f, C(255, 248, 200), 0.95f, 0.85f, sb + 0.05f);
            // Tier 3 (Paladin, Ornament >= 8): Swept Golden Seraphic Shield-Wings & Radiant Halo Ring
            if (s.Ornament >= 8)
            {
                foreach (int sg in new[] { -1, 1 })
                    for (int f = 0; f < 3; f++)
                        LeafPlate(b, V(sg * (48f + 12f * f), gy + 8f, -6f), sg * (42f + 16f * f), 68f - 10f * f, 18f, th.Gold, 0.9f, 0.25f, gb + 0.02f);
                b.Pen(Alpha(th.Glow, 220), 0f, 1f); b.Arc(V(0, top + 4f, -4f), 38f, 43f, 0f, 360f, 28, s.TrunkGrown + 0.04f, V(0, top + 4f, -4f)); b.Pieces++;
            }
            // Tier 4 (Royal Guard, Ornament >= 11): Twin Crossed Imperial Halberds & Royal Griffin Winged Crest
            if (s.Ornament >= 11)
            {
                foreach (int sg in new[] { -1, 1 })
                {
                    var h0 = V(-sg * 38f, midY - 88f, -6f); var h1 = V(sg * 86f, midY + 104f, -6f); var hd = (h1 - h0).normalized;
                    b.Pen(th.Gold, 0.95f); b.Tube(new[] { h0, h1 }, 3.4f, 2.6f, 6, sb + 0.03f, sb + 0.07f);
                    Tip(b, h1, hd, 38f, 9f, th.Metal, 0.95f, sb + 0.07f);
                    LeafPlate(b, h1 - hd * 18f, sg * 68f, 44f, 22f, th.Gold, 0.95f, 0.35f, sb + 0.07f, th.Metal);
                }
            }
            // Tier 5 (Imperial Guard, Ornament >= 14): Sovereign Sun-Emperor Aegis Halo & Floating Spear-of-Genesis Pillars
            if (s.Ornament >= 14)
            {
                float ib = s.TrunkGrown + 0.05f;
                Hoop(b, V(0, midY + 18f, 0), 92f, 52f, -12f, 3.4f, true, th.Gold, 0.95f, 0.85f, ib);
                foreach (int sg in new[] { -1, 1 })
                    Shard(b, V(sg * 78f, midY + 10f, -16f), sg * 8f, 82f, 11f, th.Glow, 0.95f, ib + 0.02f, 0.8f);
            }
        }

        /// <summary>Wizard (5) / High Wizard (8) / Warlock (11) / Arch Mage (14).</summary>
        private static void WizardOrrery(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom, top = s.Top - 110f, midY = (bot + top) * 0.5f, tt = s.TrunkBirth(top);
            // Jagged obsidian crystal roots erupting at the base
            foreach (var (x, lean, len, col) in new[] { (-42f, -28f, 68f, th.LeafA), (38f, 25f, 74f, th.LeafB), (-18f, -12f, 54f, th.Trunk2), (24f, 14f, 58f, th.LeafA) })
                Shard(b, V(x, bot, -6f), lean, len, 9.5f, col, 0.75f, 0.03f);
            // Triple-twisted dark obsidian & gold arcane spire
            Twist(b, s, bot + 12f, top, 17f, 11f, 15f, 210f, th.Trunk, th.Gold, 0.65f);
            // 3D Celestial Armillary Orrery at midY (three intersecting gyroscope rings)
            Hoop(b, V(0, midY, 0), 68f, 44f, 22f, 2.4f, true, th.Gold, 0.95f, 0.5f, s.TrunkBirth(midY) + 0.04f);
            Hoop(b, V(0, midY, 0), 62f, 40f, -26f, 2.2f, true, th.Trunk2, 0.8f, 0.85f, s.TrunkBirth(midY) + 0.06f);
            Hoop(b, V(0, midY + 110f, 0), 52f, 32f, 12f, 2.0f, true, th.LeafA, 0.8f, 0.75f, s.TrunkBirth(midY + 110f) + 0.05f);
            // Crown: Swept dragon-horn prongs cradling a massive levitating Astral Meteor Crystal
            foreach (int sg in new[] { -1, 1 })
            {
                b.Pen(th.Gold, 0.9f);
                b.Tube(TreeMeshBuilder.Curve(V(sg * 8f, top - 10f, 0), V(sg * 56f, top + 28f, 0), V(sg * 26f, top + 92f, 0), 9), 9.5f, 3.2f, 6, tt, tt + 0.05f);
                b.Pieces++;
            }
            Shard(b, V(0, top + 12f, -3f), 0f, 126f, 28f, th.Trunk2, 0.85f, tt + 0.04f, 1f);
            // Tier 3 (High Wizard, Ornament >= 8): Four Elemental Crystal Satellites orbiting the spire + Arcane Halo
            var elemCols = new[] { C(255, 82, 68), C(88, 195, 255), C(98, 245, 150), C(255, 205, 82) };
            if (s.Ornament >= 8)
            {
                for (int i = 0; i < 4; i++)
                {
                    float y = Mathf.Lerp(bot + 140f, top - 40f, i / 3f);
                    int sg = i % 2 == 0 ? -1 : 1;
                    Shard(b, V(sg * 52f, y, -10f), sg * 18f, 52f, 8.5f, elemCols[i], 0.85f, s.TrunkBirth(y) + 0.05f, 0.8f);
                }
                b.Pen(Alpha(th.Glow, 220), 0f, 1f); b.Arc(V(0, top + 76f, -4f), 54f, 58f, 0f, 360f, 28, tt + 0.08f, V(0, top + 76f, -4f)); b.Pieces++;
            }
            // Tier 4 (Warlock, Ornament >= 11): Tetra-Vortex Diamond of 4 Large Elemental Spheres linked by Arcane Lightning Arcs
            if (s.Ornament >= 11)
            {
                float wb = s.TrunkBirth(midY) + 0.05f;
                var tvPos = new[] { V(0, midY + 86f, -18f), V(68f, midY + 12f, -18f), V(0, midY - 62f, -18f), V(-68f, midY + 12f, -18f) };
                for (int i = 0; i < 4; i++)
                {
                    Bead(b, tvPos[i], 13.5f, elemCols[i], 0.95f, 0.95f, wb + i * 0.01f);
                    b.Alive(1f, 0f, false, 0.9f); b.Pen(Alpha(elemCols[i], 210), 0.9f, 1f);
                    b.Line(tvPos[i], tvPos[(i + 1) % 4], 2.4f, wb + 0.02f, wb + 0.05f);
                    b.Plain(); b.Pieces++;
                }
            }
            // Tier 5 (Arch Mage, Ornament >= 14): Cosmic Astral Singularity Crown & Twin Eclipse Rings
            if (s.Ornament >= 14)
            {
                float ab = tt + 0.06f;
                Hoop(b, V(0, top + 76f, -4f), 76f, 44f, 28f, 3.0f, true, th.Gold, 0.95f, 0.9f, ab);
                Hoop(b, V(0, top + 76f, -4f), 76f, 44f, -28f, 3.0f, true, th.Glow, 0.95f, 0.95f, ab + 0.02f);
                StarPlate(b, V(0, top + 76f, -12f), 8, 36f, 12f, 22.5f, th.Gold, 0.95f, 0.9f, ab + 0.03f);
            }
        }

        /// <summary>Sage (5) / Scholar (8) / Sorcerer (11) / Elemental Master (14).</summary>
        private static void SageGrimoire(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom, top = s.Top - 80f, midY = (bot + top) * 0.5f, mb = s.TrunkBirth(midY);
            // Ornate scholar's lectern column below & above the grimoire
            b.Pen(th.Trunk, 0.35f);
            b.Lathe(V(0, bot, 0), Vector3.up, new[] { new Vector2(0, 32f), new Vector2(30f, 22f), new Vector2(top - bot, 14f) }, 10, 0f, s.TrunkGrown); b.Pieces++;
            // Brass astrolabe rings encircling the lectern
            Hoop(b, V(0, midY, 0), 74f, 42f, 16f, 2.5f, true, th.Gold, 0.92f, 0.4f, mb + 0.03f);
            Hoop(b, V(0, midY, 0), 66f, 38f, -18f, 2.0f, true, th.LeafA, 0.85f, 0.7f, mb + 0.05f);
            // Open 3D Arcane Grimoire at midY: left and right leather covers + fanned cream parchment pages
            foreach (int sg in new[] { -1, 1 })
            {
                b.Pen(th.Trunk, 0.4f);
                b.Plate(RectShape(62f, 88f), Lean(V(sg * 34f, midY, -10f), sg * 12f), 4.5f, mb + 0.02f, Vector2.zero, Vector2.zero, th.Gold); b.Pieces++;
                for (int p = 0; p < 3; p++)
                {
                    b.Pen(th.ClothA, 0.2f);
                    b.Plate(RectShape(54f - p * 3f, 78f - p * 2f), Lean(V(sg * (30f - p * 2f), midY + 2f + p * 1.5f, -13f - p * 1.2f), sg * (9f - p * 2.5f)), 1.8f, mb + 0.03f + p * 0.01f, Vector2.zero, Vector2.zero, Shade(th.ClothA, 0.8f));
                    b.Pieces++;
                }
                b.Alive(0.95f, 0f, false, 0.6f); b.Pen(th.LeafA, 0.9f, 0.85f);
                for (int ln = -1; ln <= 1; ln++)
                    b.Plate(RectShape(34f, 3.4f), Lean(V(sg * 26f, midY + 5f + ln * 15f, -19.5f), sg * 4f), 0.8f, mb + 0.06f, Vector2.zero, Vector2.zero);
                b.Plain();
            }
            // Spine clasp & central emerald philosopher's stone
            b.Pen(th.Gold, 0.95f); b.Box(V(0, midY, -12f), V(14f, 92f, 10f), Quaternion.identity, mb + 0.02f); b.Pieces++;
            Bead(b, V(0, midY, -17f), 11f, th.LeafA, 0.95f, 0.85f, mb + 0.06f);
            // Crown Astrolabe Sphere at the top
            var topC = V(0, top + 34f, -4f); float tb = s.TrunkGrown + 0.04f;
            Bead(b, topC, 18f, th.LeafA, 0.95f, 0.8f, tb);
            b.Pen(th.Gold, 0.95f); b.Arc(topC, 32f, 36f, 0f, 360f, 24, tb + 0.02f, topC); b.Pieces++;
            // Tier 3 (Scholar / Professor, Ornament >= 8): Floating Golden Quill Pen + Chronos Hourglass below the Grimoire
            if (s.Ornament >= 8)
            {
                var qAt = V(28f, midY + 68f, -16f);
                b.Pen(th.Gold, 0.9f); Tip(b, qAt, V(-0.45f, -0.89f).normalized, 28f, 4.5f, th.Gold, 0.95f, mb + 0.07f);
                LeafPlate(b, qAt, 28f, 72f, 22f, th.ClothB, 0.35f, 0.2f, mb + 0.08f, th.Gold);
                b.Pen(th.Gold, 0.95f);
                b.Lathe(V(0, midY - 94f, -12f), Vector3.up, new[] { new Vector2(0, 18f), new Vector2(18f, 3f), new Vector2(36f, 18f) }, 8, mb + 0.04f, mb + 0.07f); b.Pieces++;
            }
            // Tier 4 (Sorcerer, Ornament >= 11): Four Summoned Elemental Spirit Totem Crystals (Agni, Aqua, Ventus, Terra)
            if (s.Ornament >= 11)
            {
                var spCols = new[] { C(255, 92, 64), C(78, 195, 255), C(110, 250, 160), C(245, 195, 78) };
                for (int i = 0; i < 4; i++)
                {
                    float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                    var spAt = V(Mathf.Cos(a) * 76f, midY + Mathf.Sin(a) * 58f, -16f);
                    Shard(b, spAt, (i % 2 == 0 ? -1 : 1) * 16f, 46f, 9f, spCols[i], 0.9f, mb + 0.05f + i * 0.01f, 0.9f);
                }
            }
            // Tier 5 (Elemental Master, Ornament >= 14): Grand Primordial Conflux Crown & Astral Zodiac Halo
            if (s.Ornament >= 14)
            {
                Hoop(b, topC, 58f, 36f, -20f, 2.8f, true, th.Glow, 0.95f, 0.9f, tb + 0.04f);
                StarPlate(b, topC + V(0, 0, -8f), 8, 34f, 12f, 0f, th.Gold, 0.95f, 0.85f, tb + 0.05f);
            }
        }

        /// <summary>Priest (5) / High Priest (8) / Archbishop (11) / Cardinal (14).</summary>
        private static void PriestSanctuary(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom, top = s.Top - 52f, h = top - bot, midY = (bot + top) * 0.5f, tt = s.TrunkBirth(top);
            // Fluted white-marble & gold cathedral column
            Pillar(b, s, th, st);
            float wb = s.TrunkBirth(midY) + 0.04f;
            // Golden Sunburst Monstrance at the heart of the sanctuary
            var mon = V(0, midY + 14f, -16f);
            b.Alive(1f, 0f, false, 0.7f);
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f * Mathf.Deg2Rad; var d = V(Mathf.Sin(a), Mathf.Cos(a));
                Tip(b, mon + d * 15f, d, i % 2 == 0 ? 38f : 24f, 5f, th.Gold, 0.95f, wb + 0.03f);
            }
            Bead(b, mon, 16f, C(255, 250, 220), 0.95f, 0.9f, wb + 0.05f);
            b.Plain();
            // Crown: Grand Cathedral Rose-Window Halo & Golden Papal Cross Finial at the apex
            var crown = V(0, top + 36f, -6f);
            b.Pen(th.Gold, 0.95f);
            b.Arc(crown, 34f, 40f, 0f, 360f, 24, tt + 0.02f, crown);
            b.Plate(RectShape(13f, 76f), Lean(crown, 0f), 2.2f, tt + 0.03f, Vector2.zero, Vector2.zero);
            b.Plate(RectShape(52f, 12f), Lean(crown + V(0, 10f, -1f), 0f), 2.2f, tt + 0.04f, Vector2.zero, Vector2.zero);
            b.Plate(RectShape(36f, 10f), Lean(crown + V(0, 24f, -1f), 0f), 2.2f, tt + 0.04f, Vector2.zero, Vector2.zero);
            Bead(b, crown + V(0, 10f, -3f), 9f, th.LeafA, 0.95f, 0.8f, tt + 0.05f);
            b.Pieces += 3;
            // Tier 3 (High Priest, Ornament >= 8): Six-Winged Seraphim + Twin Suspended Golden Incense Censers
            if (s.Ornament >= 8)
            {
                foreach (int sg in new[] { -1, 1 })
                {
                    LeafPlate(b, V(sg * 26f, midY + 46f, -12f), sg * 52f, 114f, 26f, C(252, 248, 236), 0.7f, 0.35f, wb, th.Gold);
                    LeafPlate(b, V(sg * 30f, midY + 14f, -13f), sg * 82f, 102f, 24f, th.LeafA, 0.75f, 0.3f, wb + 0.02f, th.Gold);
                    LeafPlate(b, V(sg * 24f, midY - 18f, -12f), sg * 114f, 84f, 21f, C(248, 236, 206), 0.7f, 0.25f, wb + 0.04f, th.Gold);
                    var hook = V(sg * 48f, midY - 26f, -14f); var cen = hook + V(0, -46f, 0);
                    b.Pen(th.Gold, 0.9f); b.Line(hook, cen, 1.8f, wb + 0.04f, wb + 0.06f);
                    b.Lathe(cen + V(0, -12f, 0), Vector3.up, new[] { new Vector2(0, 2f), new Vector2(6f, 11f), new Vector2(12f, 12f), new Vector2(18f, 5f) }, 8, wb + 0.06f, wb + 0.08f);
                    Bead(b, cen + V(0, -4f, 0), 6.5f, th.Glow, 0.5f, 0.85f, wb + 0.07f);
                    b.Pieces++;
                }
            }
            // Tier 4 (Arch Bishop, Ornament >= 11): Floating Golden Gospel Scripture Tablets & Triple Cathedral Arch Halo
            if (s.Ornament >= 11)
            {
                b.Pen(th.Gold, 0.95f, 0.5f);
                b.Arc(crown, 52f, 57f, 0f, 360f, 28, tt + 0.05f, crown); b.Pieces++;
                foreach (int sg in new[] { -1, 1 })
                {
                    b.Pen(th.ClothA, 0.4f);
                    b.Plate(RectShape(28f, 44f), Lean(V(sg * 68f, midY + 34f, -15f), sg * -12f), 2.5f, wb + 0.05f, Vector2.zero, Vector2.zero, th.Gold);
                    b.Pieces++;
                }
            }
            // Tier 5 (Cardinal, Ornament >= 14): Twelve-Rayed Celestial Sanctum Crown & Seraphic Choir Rings
            if (s.Ornament >= 14)
            {
                Hoop(b, V(0, midY + 14f, 0), 86f, 48f, 16f, 3.0f, true, th.Gold, 0.95f, 0.8f, wb + 0.06f);
                Hoop(b, V(0, midY + 14f, 0), 86f, 48f, -16f, 3.0f, true, th.LeafA, 0.95f, 0.85f, wb + 0.07f);
            }
        }

        /// <summary>Monk (5) / Champion (8) / Sura (11) / Inquisitor (14).</summary>
        private static void MonkVajra(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom, top = s.Top - 50f, h = top - bot, midY = (bot + top) * 0.5f, tt = s.TrunkBirth(top);
            // Ironwood & bronze octagonal martial staff
            b.Pen(th.Trunk, 0.45f);
            b.Lathe(V(0, bot, 0), Vector3.up, new[] { new Vector2(0, 22f), new Vector2(h * 0.45f, 19f), new Vector2(h * 0.5f, 26f), new Vector2(h * 0.55f, 19f), new Vector2(h, 17f) }, 8, 0f, tt); b.Pieces++;
            // Heavy bronze bands along the staff
            for (int i = 0; i <= 4; i++)
            {
                float y = Mathf.Lerp(bot + 36f, top - 24f, i / 4f);
                b.Pen(th.Gold, 0.92f);
                b.Lathe(V(0, y, 0), Vector3.up, new[] { new Vector2(0, 21f), new Vector2(4f, 24.5f), new Vector2(12f, 24.5f), new Vector2(16f, 21f) }, 10, s.TrunkBirth(y), s.TrunkBirth(y) + 0.02f);
                b.Pieces++;
            }
            // Giant Crimson & Gold Prayer Mala (Rosary Beads) draped diagonally across the staff
            int beads = 14;
            for (int i = 0; i < beads; i++)
            {
                float a = i * Mathf.PI * 2f / beads;
                var bp = V(Mathf.Cos(a) * 44f, midY + Mathf.Sin(a) * 68f, Mathf.Sin(a) * 14f - 8f);
                Bead(b, bp, i % 7 == 0 ? 11.5f : 8.5f, i % 7 == 0 ? th.Gold : th.ClothA, 0.85f, i % 7 == 0 ? 0.4f : 0f, s.TrunkBirth(bp.y) + 0.03f);
            }
            // Crown: Golden Thunderbolt Vajra (Dorje) at the apex
            b.Pen(th.Gold, 0.95f);
            Bead(b, V(0, top + 6f, 0), 16f, th.Gold, 0.95f, 0.3f, tt);
            Tip(b, V(0, top + 14f, 0), Vector3.up, 72f, 11f, th.Gold, 0.95f, tt + 0.03f);
            // Tier 3 (Champion, Ornament >= 8): Five Levitating Zen Spirit Spheres + Four-Pronged Vajra Claws
            if (s.Ornament >= 8)
            {
                for (int i = 0; i < 5; i++)
                {
                    float a = (i * 72f - 90f) * Mathf.Deg2Rad;
                    var sp = V(Mathf.Cos(a) * 68f, midY + 35f + Mathf.Sin(a) * 38f, -14f);
                    Bead(b, sp, 11f, th.Glow, 0.95f, 0.95f, s.TrunkBirth(midY) + 0.05f + i * 0.01f);
                }
                foreach (int sg in new[] { -1, 1 })
                {
                    b.Pen(th.Gold, 0.95f);
                    b.Tube(TreeMeshBuilder.Curve(V(sg * 10f, top + 8f, 0), V(sg * 42f, top + 42f, 0), V(sg * 12f, top + 78f, 0), 8), 6.5f, 2.8f, 6, tt + 0.02f, tt + 0.06f);
                    b.Pieces++;
                }
            }
            // Tier 4 (Sura, Ornament >= 11): Six-Rayed Asura Chakra Halo & Twin Blazing Dragon-Fist Knuckle Crests
            if (s.Ornament >= 11)
            {
                float sb = s.TrunkBirth(midY) + 0.06f;
                b.Pen(th.Gold, 0.95f, 0.65f);
                b.Plate(Gear(12, 76f, 62f), Lean(V(0, midY + 35f, -6f), 15f), 3.2f, sb, Vector2.zero, Vector2.zero, th.Glow); b.Pieces++;
                foreach (int sg in new[] { -1, 1 })
                {
                    b.Pen(th.Gold, 0.95f);
                    b.Box(V(sg * 58f, midY - 12f, -14f), V(26f, 34f, 18f), Quaternion.Euler(0, 0, -sg * 18f), sb + 0.02f);
                    b.Pieces++;
                }
            }
            // Tier 5 (Inquisitor, Ornament >= 14): Verdict Flame-Wheel & Twin Chains of Judgment
            if (s.Ornament >= 14)
            {
                float ib = tt + 0.05f;
                Hoop(b, V(0, midY + 35f, 0), 94f, 52f, 18f, 3.2f, true, th.Glow, 0.95f, 0.95f, ib);
                Hoop(b, V(0, midY + 35f, 0), 94f, 52f, -18f, 3.2f, true, th.Gold, 0.95f, 0.85f, ib + 0.02f);
            }
        }

        /// <summary>Hunter (5) / Sniper (8) / Ranger (11) / Windhawk (14).</summary>
        private static void HunterFalcon(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom, top = s.Top - 36f, midY = (bot + top) * 0.5f + 16f, mb = s.TrunkBirth(midY);
            // Carved oak falconer's perch pillar rising from the base
            var perchPath = new List<Vector3>(15);
            for (int i = 0; i <= 14; i++) { float t = i / 14f; perchPath.Add(V(6f * Mathf.Sin(t * 4f), Mathf.Lerp(bot, top - 40f, t), 4f)); }
            b.Pen(th.Wood, 0.25f); b.Tube(perchPath, t => 15f - 4f * t, 8, 0f, s.TrunkGrown); b.Pieces++;
            // Horizontal brass-bound crossbar perch at midY - 32f
            b.Pen(th.Gold, 0.9f); b.Tube(new[] { V(-54f, midY - 34f, -4f), V(54f, midY - 34f, -4f) }, 6.5f, 6.5f, 7, mb, mb + 0.02f, true, true); b.Pieces++;
            // Curved Ivory & Gold Hunting Horn slung below the perch
            b.Pen(th.Trunk2, 0.7f);
            b.Tube(TreeMeshBuilder.Curve(V(-38f, bot + 75f, -10f), V(0f, bot + 35f, -12f), V(46f, bot + 88f, -10f), 10), 4.5f, 13.5f, 8, 0.04f, 0.1f, true, true); b.Pieces++;
            // Fanned Golden-Brown Falcon Tail Feathers sweeping downward behind the perch
            for (int i = -3; i <= 3; i++)
            {
                float ang = 180f + i * 11f;
                LeafPlate(b, V(i * 5.5f, midY - 30f, -6f), ang, 88f - Mathf.Abs(i) * 6f, 18f, i % 2 == 0 ? th.Trunk : th.Trunk2, 0.35f, 0f, mb + 0.02f, th.Gold);
            }
            // Sculpted 3D Falcon Torso & Breast (layered feathered chest)
            b.Alive(0.85f, 0f, false, 0.3f, true);
            b.Pen(th.Trunk, 0.55f);
            b.Lathe(V(0, midY - 32f, -10f), Vector3.up, new[] { new Vector2(0, 12f), new Vector2(22f, 28f), new Vector2(54f, 32f), new Vector2(86f, 22f), new Vector2(104f, 12f) }, 10, mb + 0.02f, mb + 0.06f);
            b.Plain(); b.Pieces++;
            // Golden breast plumage scales
            for (int row = 0; row < 3; row++)
                for (int col = -1; col <= 1; col++)
                    LeafPlate(b, V(col * 11f, midY + 8f + row * 16f, -22f), 180f + col * 12f, 24f, 14f, th.Trunk2, 0.5f, 0.1f, mb + 0.05f, th.Gold);
            // Golden Talons gripping the crossbar perch
            foreach (int sg in new[] { -1, 1 })
                for (int c = -1; c <= 1; c++)
                    Tip(b, V(sg * 20f + c * 4.5f, midY - 32f, -10f), V(c * 0.2f, -0.95f, -0.2f).normalized, 18f, 3.2f, th.Gold, 0.95f, mb + 0.03f);
            // Swept Multi-Layered 3D Falcon Wings (Primary, Secondary & Covert flight feathers!)
            int wingFeathers = s.Ornament >= 8 ? 7 : 5;
            foreach (int sg in new[] { -1, 1 })
            {
                var w0 = V(sg * 18f, midY + 34f, -10f);
                var w1 = V(sg * 72f, midY + 74f, -12f);
                var w2 = V(sg * 126f, midY + 118f, -8f);
                b.Pen(th.Gold, 0.85f);
                b.Tube(TreeMeshBuilder.Curve(w0, w1, w2, 8), 7.5f, 3.2f, 6, mb + 0.03f, mb + 0.08f); b.Pieces++;
                for (int f = 0; f < wingFeathers; f++)
                {
                    float u = f / (float)(wingFeathers - 1);
                    var fAt = Vector3.Lerp(w0, w2, u) + V(0, -4f, 1f);
                    float lean = sg * (58f + f * 11f);
                    float len = Mathf.Lerp(78f, 112f, Mathf.Sin(u * Mathf.PI * 0.85f + 0.2f));
                    var fCol = f >= 4 ? th.Gold : f % 2 == 0 ? th.Trunk2 : th.Trunk;
                    LeafPlate(b, fAt, lean, len, 21f, fCol, 0.5f, f >= 5 ? 0.2f : 0f, mb + 0.04f + f * 0.008f, th.Gold);
                }
            }
            // Falcon Head, Royal Crest Feathers, Hooked Golden Beak & Glowing Amber Eyes
            var head = V(0, midY + 86f, -14f);
            b.Pen(th.Trunk, 0.55f); b.Ball(head, 20f, 10, 6, mb + 0.06f, 1.15f); b.Pieces++;
            b.Pen(th.Gold, 0.95f);
            Tip(b, head + V(0, 2f, -18f), V(0f, -0.38f, -0.92f).normalized, 26f, 7.5f, th.Gold, 0.95f, mb + 0.07f);
            foreach (int sg in new[] { -1, 1 })
                Bead(b, head + V(sg * 11f, 5f, -15f), 4.2f, th.Glow, 0.95f, 0.95f, mb + 0.07f);
            for (int c = -1; c <= 1; c++)
                LeafPlate(b, head + V(c * 6f, 14f, -4f), c * 22f, 54f - Mathf.Abs(c) * 8f, 14f, c == 0 ? th.Gold : th.ClothA, 0.7f, 0.25f, mb + 0.08f, th.Gold);
            // Tier 3 (Sniper, Ornament >= 8): Golden True-Sight Sniper Crosshair Halo behind the Falcon Head
            if (s.Ornament >= 8)
            {
                b.Pen(th.Gold, 0.95f, 0.55f);
                b.Arc(head + V(0, 8f, 4f), 44f, 49f, 0f, 360f, 24, mb + 0.08f, head); b.Pieces++;
            }
            // Tier 4 (Ranger, Ornament >= 11): Sculpted 3D Silver-Fur Warg Wolf Head (snout, fangs, glowing cyan eyes, swept wolf ears) + Feathered Storm-Bow Arc
            if (s.Ornament >= 11)
            {
                var warg = V(0, bot + 105f, -18f); float wb = 0.06f;
                var silver = C(210, 222, 238); var slate = C(110, 124, 146);
                // Wolf skull & projecting muzzle snout
                b.Pen(silver, 0.65f); b.Ball(warg, 28f, 10, 8, wb, 1.1f); b.Pieces++;
                b.Pen(slate, 0.7f); b.Lathe(warg + V(0, -6f, -16f), new Vector3(0f, -0.25f, -0.97f).normalized, new[] { new Vector2(0, 18f), new Vector2(22f, 12f), new Vector2(30f, 5f) }, 8, wb + 0.01f, wb + 0.03f); b.Pieces++;
                // Twin swept wolf ears & fierce glowing cyan Warg eyes + white fangs
                foreach (int sg in new[] { -1, 1 })
                {
                    Tip(b, warg + V(sg * 18f, 18f, -4f), V(sg * 0.42f, 0.9f, -0.1f).normalized, 42f, 10f, silver, 0.75f, wb + 0.02f);
                    Bead(b, warg + V(sg * 12f, 6f, -22f), 5.2f, C(80, 235, 255), 0.95f, 0.95f, wb + 0.03f);
                    Tip(b, warg + V(sg * 8f, -14f, -28f), Vector3.down, 16f, 3.6f, C(250, 250, 255), 0.95f, wb + 0.03f);
                }
                // Giant Feathered Storm-Bow Arc spanning behind the Falcon & Warg
                b.Pen(th.Gold, 0.95f);
                b.Tube(TreeMeshBuilder.Curve(V(-96f, midY - 40f, -2f), V(0, top + 28f, -4f), V(96f, midY - 40f, -2f), 12), 6f, 6f, 6, mb + 0.04f, mb + 0.09f); b.Pieces++;
            }
            // Tier 5 (Windhawk, Ornament >= 14): Tempest Gale-Hawk Crown & Swirling Emerald Wind-Vortex Rings
            if (s.Ornament >= 14)
            {
                float tb = s.TrunkGrown + 0.05f;
                Hoop(b, V(0, midY + 40f, 0), 96f, 54f, 16f, 3.0f, true, th.Glow, 0.95f, 0.9f, tb);
                StarPlate(b, head + V(0, 56f, -6f), 8, 32f, 11f, 0f, th.Gold, 0.95f, 0.85f, tb + 0.02f);
            }
        }

        /// <summary>Bard (5) / Clown-Minstrel (8) / Maestro (11) / Troubadour (14).</summary>
        private static void BardLyre(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom, top = s.Top - 44f, soundTop = bot + 145f, yokeY = top - 34f, midY = (bot + top) * 0.5f;
            // Carved mahogany & gold acoustic soundbox cradle at the base
            b.Pen(th.Trunk2, 0.45f);
            b.Lathe(V(0, bot, 0), Vector3.up, new[] { new Vector2(0, 34f), new Vector2(24f, 56f), new Vector2(88f, 62f), new Vector2(135f, 44f), new Vector2(148f, 22f) }, 12, 0f, s.TrunkBirth(soundTop)); b.Pieces++;
            // Golden acoustic rosette & bridge on the soundbox face
            b.Pen(th.Gold, 0.95f);
            b.Ring(V(0, bot + 78f, -46f), 12f, 24f, 18, 0.05f, V(0, bot + 78f, -46f));
            StarPlate(b, V(0, bot + 78f, -47f), 8, 18f, 7f, 0f, th.Gold, 0.95f, 0.4f, 0.06f);
            // Twin sweeping Golden Swan-Neck Lyre Arms curving out and up from the soundbox to the top yoke
            foreach (int sg in new[] { -1, 1 })
            {
                var arm = new List<Vector3>(17);
                for (int i = 0; i <= 16; i++)
                {
                    float t = i / 16f;
                    float x = sg * (36f + 46f * Mathf.Sin(t * Mathf.PI * 1.15f) - 18f * t * t);
                    float y = Mathf.Lerp(soundTop - 16f, yokeY + 18f, t);
                    arm.Add(V(x, y, -4f));
                }
                b.Alive(0.85f, 0f, false, 0f, true); b.Pen(th.Trunk, 0.92f);
                b.Tube(arm, t => 12.5f - 4.5f * t + 2.5f * Mathf.Sin(t * Mathf.PI * 2f), 8, s.TrunkBirth(soundTop), s.TrunkBirth(yokeY), true, true);
                b.Plain(); b.Pieces++;
                b.Pen(th.Gold, 0.95f);
                b.Tube(TreeMeshBuilder.Scroll(arm[16], V(-sg * 0.5f, 0.86f).normalized, 22f, 1.35f, sg > 0, 14), 5.5f, 2.2f, 5, s.TrunkGrown, s.TrunkGrown + 0.04f);
                b.Pieces++;
            }
            // Ornate golden crossbar (yoke) with tuning pegs at the top
            b.Pen(th.Gold, 0.95f);
            b.Tube(new[] { V(-64f, yokeY, -4f), V(64f, yokeY, -4f) }, 7.5f, 7.5f, 8, s.TrunkBirth(yokeY), s.TrunkGrown, true, true); b.Pieces++;
            for (int i = -2; i <= 2; i++)
                Bead(b, V(i * 11f, yokeY, -11f), 4.8f, th.LeafB, 0.95f, 0.5f, s.TrunkGrown + 0.02f);
            // Tier 3 (Clown / Minstrel, Ornament >= 8): Fluttering Silk Ribbons & Golden Treble Sun-Star
            if (s.Ornament >= 8)
            {
                foreach (int sg in new[] { -1, 1 })
                {
                    b.Pen(sg < 0 ? th.ClothA : th.ClothB, 0.2f);
                    b.Plate(Banner(16f, 78f, 12f), Lean(V(sg * 68f, (soundTop + yokeY) * 0.5f, -10f), sg * 12f), 1.4f, 0.25f, Vector2.zero, new Vector2(0, -24f), th.Gold);
                    b.Pieces++;
                }
                StarPlate(b, V(0, top + 16f, -6f), 8, 28f, 11f, 22.5f, th.Gold, 0.95f, 0.6f, s.TrunkGrown + 0.05f);
            }
            // Tier 4 (Maestro, Ornament >= 11): Grand Resonant Pipe-Organ Tubes & Floating Harmonic Halo
            if (s.Ornament >= 11)
            {
                float mb = s.TrunkBirth(midY) + 0.04f;
                foreach (int sg in new[] { -1, 1 })
                    for (int p = 0; p < 3; p++)
                    {
                        float px = sg * (22f + p * 12f), ph = 110f - p * 22f;
                        b.Pen(th.Gold, 0.95f);
                        b.Tube(new[] { V(px, yokeY, -8f), V(px, yokeY + ph, -8f) }, 4.8f, 5.5f, 6, mb + p * 0.01f, mb + 0.05f, true, true);
                        b.Pieces++;
                    }
            }
            // Tier 5 (Troubadour, Ornament >= 14): Celestial Symphony Sun-Crown & Twin Harmonic Choir Rings
            if (s.Ornament >= 14)
            {
                float tb = s.TrunkGrown + 0.05f;
                Hoop(b, V(0, midY + 20f, 0), 88f, 48f, 16f, 2.8f, true, th.Gold, 0.95f, 0.8f, tb);
                Hoop(b, V(0, midY + 20f, 0), 88f, 48f, -16f, 2.8f, true, th.Glow, 0.95f, 0.85f, tb + 0.02f);
            }
        }

        /// <summary>Dancer (5) / Gypsy (8) / Wanderer (11) / Trouvere (14).</summary>
        private static void DancerFans(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom, top = s.Top - 40f, midY = (bot + top) * 0.5f, mb = s.TrunkBirth(midY), tt = s.TrunkBirth(top);
            // Golden Lotus Tambourine Drum at the base with brass jingle discs
            b.Pen(th.Gold, 0.92f);
            b.Lathe(V(0, bot, 0), Vector3.up, new[] { new Vector2(0, 48f), new Vector2(18f, 54f), new Vector2(44f, 46f), new Vector2(58f, 24f) }, 12, 0f, 0.06f); b.Pieces++;
            for (int j = -2; j <= 2; j++)
            {
                float a = j * 28f * Mathf.Deg2Rad;
                Bead(b, V(Mathf.Sin(a) * 52f, bot + 24f, -Mathf.Cos(a) * 48f), 6.5f, th.LeafB, 0.95f, 0.4f, 0.05f);
            }
            // Spiraling Rose-Silk & Gold Ribbon-Whip Column rising to the crown
            Twist(b, s, bot + 52f, top, 12f, 9f, 20f, 180f, th.Trunk, th.Gold, 0.85f);
            // Twin Giant Pleated Rose-Magenta & Gold Peacock Silk Dance Fans unfurled at midY
            foreach (int sg in new[] { -1, 1 })
            {
                var pivot = V(sg * 10f, midY - 22f, -12f);
                for (int r = 0; r < 7; r++)
                {
                    float ang = sg * (28f + r * 14f);
                    float len = Mathf.Lerp(82f, 112f, Mathf.Sin((r / 6f) * Mathf.PI));
                    var bladeCol = r % 2 == 0 ? th.Trunk : th.ClothB;
                    LeafPlate(b, pivot, ang, len, 26f, bladeCol, 0.65f, 0.25f, mb + 0.02f + r * 0.008f, th.Gold);
                }
            }
            // Central Ruby-Gold Lotus Brooch clasping the crossed fans
            StarPlate(b, V(0, midY - 14f, -18f), 8, 26f, 12f, 22.5f, th.Gold, 0.95f, 0.6f, mb + 0.06f);
            Bead(b, V(0, midY - 14f, -20f), 11f, th.Trunk, 0.95f, 0.85f, mb + 0.07f);
            // Tier 3 (Gypsy, Ornament >= 8): Trailing Silk Veil Ribbons & Golden Lotus Tiara
            if (s.Ornament >= 8)
            {
                foreach (int sg in new[] { -1, 1 })
                {
                    b.Pen(sg < 0 ? th.ClothA : th.ClothB, 0.35f);
                    b.Plate(Banner(18f, 88f, 14f), Lean(V(sg * 82f, midY - 12f, -14f), sg * 16f), 1.6f, mb + 0.06f, Vector2.zero, new Vector2(0, -28f), th.Gold);
                    b.Pieces++;
                }
                for (int c = -2; c <= 2; c++)
                    LeafPlate(b, V(c * 9f, top - 4f, -6f), c * 24f, 62f - Mathf.Abs(c) * 8f, 18f, c == 0 ? th.Gold : th.LeafA, 0.85f, 0.4f, tt + 0.03f, th.Gold);
                Bead(b, V(0, top + 14f, -8f), 11f, th.Gold, 0.95f, 0.8f, tt + 0.05f);
            }
            // Tier 4 (Wanderer, Ornament >= 11): Coiled Golden Rose-Whip Loop & Blooming Moonlight Datura Flowers
            if (s.Ornament >= 11)
            {
                Hoop(b, V(0, midY + 28f, -6f), 78f, 44f, -18f, 2.8f, true, th.Gold, 0.95f, 0.6f, mb + 0.07f);
                foreach (int sg in new[] { -1, 1 })
                    StarPlate(b, V(sg * 72f, midY + 38f, -16f), 6, 22f, 10f, sg * 18f, th.ClothA, 0.85f, 0.65f, mb + 0.08f);
            }
            // Tier 5 (Trouvere, Ornament >= 14): Starlight Swan-Prima Halo & Twin Floating Ribbon Rings
            if (s.Ornament >= 14)
            {
                Hoop(b, V(0, top + 14f, -4f), 64f, 36f, 14f, 2.6f, true, th.Glow, 0.95f, 0.9f, tt + 0.06f);
                StarPlate(b, V(0, top + 44f, -10f), 8, 30f, 11f, 0f, th.Gold, 0.95f, 0.9f, tt + 0.07f);
            }
        }

        /// <summary>Assassin (5) / Assassin Cross (8) / Guillotine Cross (11) / Shadow Cross (14).</summary>
        private static void AssassinKatars(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom, top = s.Top - 30f, midY = (bot + top) * 0.5f, mb = s.TrunkBirth(midY);
            // Dark shadow-steel spine rising through the center
            var spine = new List<Vector3>(17);
            for (int i = 0; i <= 16; i++) { float t = i / 16f; spine.Add(V(6f * Mathf.Sin(t * 9f), Mathf.Lerp(bot, top, t), 2f)); }
            b.Pen(th.Trunk, 0.6f); b.Tube(spine, t => 16f - 6f * t, 7, 0f, s.TrunkGrown); b.Pieces++;
            // Giant 8-Bladed Spiked Chakram Death-Wheel mounted at midY
            b.Pen(th.Metal, 0.95f);
            b.Plate(Gear(8, 72f, 52f), Lean(V(0, midY, -6f), 22.5f), 4f, mb, Vector2.zero, Vector2.zero, th.Trunk2); b.Pieces++;
            for (int i = 0; i < 8; i++)
            {
                float a = (i * 45f + 22.5f) * Mathf.Deg2Rad; var d = V(Mathf.Sin(a), Mathf.Cos(a));
                Tip(b, V(0, midY, -7f) + d * 64f, d, 32f, 7.5f, i % 2 == 0 ? th.Metal : th.Trunk2, 0.95f, mb + 0.03f);
            }
            // Twin Crossed Three-Bladed Jur Katars slashing diagonally across the tree
            foreach (int sg in new[] { -1, 1 })
            {
                var dir = V(sg * 0.36f, 0.93f).normalized;
                var perp = V(dir.y, -dir.x);
                var hilt = V(-sg * 58f, midY - 120f, sg < 0 ? -10f : -14f);
                b.Pen(th.Gold, 0.9f);
                b.Box(hilt, V(44f, 10f, 10f), Quaternion.FromToRotation(Vector3.up, dir), mb, hilt);
                b.Box(hilt + dir * 36f, V(48f, 12f, 12f), Quaternion.FromToRotation(Vector3.up, dir), mb + 0.01f, hilt); b.Pieces++;
                b.Alive(0.85f, 0f, false, 0f, true); b.Pen(th.Metal, 0.95f);
                b.Blade(hilt + dir * 42f, dir, perp, 240f, x => 16f * (1f - x * x * 0.85f), x => 5f * (1f - x), 8, mb + 0.02f, mb + 0.08f, th.Trunk2);
                foreach (int side in new[] { -1, 1 })
                    b.Blade(hilt + dir * 40f + perp * (side * 16f), (dir + perp * (side * 0.08f)).normalized, perp, 155f, x => 9f * (1f - x), x => 3.5f * (1f - x), 5, mb + 0.03f, mb + 0.07f, th.LeafA);
                b.Plain(); b.Pieces += 2;
            }
            // Tier 3 (Assassin Cross, Ornament >= 8): Twin Glowing Emerald Deadly-Poison Vials + Crimson Assassin Cross Apex
            if (s.Ornament >= 8)
            {
                foreach (int sg in new[] { -1, 1 })
                {
                    var vAt = V(sg * 26f, midY - 18f, -18f);
                    b.Alive(1f, 0f, false, 0.8f); b.Pen(th.LeafA, 0.95f, 0.85f);
                    b.Lathe(vAt, Vector3.up, new[] { new Vector2(0, 4f), new Vector2(8f, 11f), new Vector2(22f, 11f), new Vector2(28f, 4.5f), new Vector2(34f, 5.5f) }, 8, mb + 0.05f, mb + 0.07f);
                    b.Plain(); b.Pieces++;
                }
                StarPlate(b, V(0, top + 14f, -8f), 4, 34f, 9f, 0f, th.Trunk2, 0.95f, 0.7f, s.TrunkGrown + 0.04f);
            }
            // Tier 4 (Guillotine Cross, Ornament >= 11): Towering Executioner's Guillotine Frame + Diagonal Weighted Drop-Blade + Twin Reaper Crescent Scythes
            if (s.Ornament >= 11)
            {
                float gy = top - 24f, gb = s.TrunkBirth(gy);
                // Guillotine upper crossbeam & twin side pillars
                b.Pen(th.Trunk, 0.75f); b.Box(V(0, gy + 16f, -4f), V(136f, 16f, 18f), Quaternion.identity, gb); b.Pieces++;
                foreach (int sg in new[] { -1, 1 })
                {
                    b.Pen(th.Gold, 0.9f);
                    b.Tube(new[] { V(sg * 56f, midY - 40f, -4f), V(sg * 56f, gy + 16f, -4f) }, 5.5f, 5.5f, 6, mb, gb); b.Pieces++;
                    // Swept Reaper Crescent Scythe blades flaring outward from the chakram hub
                    var s0 = V(sg * 38f, midY + 24f, -12f); var s1 = V(sg * 108f, midY + 68f, -14f); var s2 = V(sg * 82f, midY - 34f, -14f);
                    b.Alive(0.9f, 0f, false, 0.5f, true); b.Pen(th.Metal, 0.95f);
                    b.Tube(TreeMeshBuilder.Curve(s0, s1, s2, 9), 9.5f, 1.5f, 5, mb + 0.04f, mb + 0.08f);
                    b.Plain(); b.Pieces++;
                }
                // Heavy angled Executioner's Guillotine Drop-Blade suspended under the crossbeam
                var gBlade = new List<Vector2> { new Vector2(-46f, 0f), new Vector2(46f, 0f), new Vector2(46f, -38f), new Vector2(-46f, -68f) };
                b.Alive(0.95f, 0f, false, 0.4f, true); b.Pen(th.Metal, 0.95f);
                b.Plate(gBlade, Lean(V(0, gy + 8f, -10f), 0f), 4.5f, gb + 0.03f, Vector2.zero, new Vector2(0, -34f), th.Trunk2);
                b.Plain(); b.Pieces++;
            }
            // Tier 5 (Shadow Cross, Ornament >= 14): Twin Abyssal Phantom Wings & Eclipse Crescent Halo
            if (s.Ornament >= 14)
            {
                float sb = s.TrunkGrown + 0.05f;
                Hoop(b, V(0, midY + 20f, 0), 92f, 52f, -16f, 3.2f, true, th.Trunk2, 0.95f, 0.9f, sb);
                foreach (int sg in new[] { -1, 1 })
                    LeafPlate(b, V(sg * 64f, top - 10f, -12f), sg * 62f, 96f, 24f, th.Trunk2, 0.9f, 0.75f, sb + 0.02f, th.Gold);
            }
        }

        /// <summary>Rogue (5) / Stalker (8) / Shadow Chaser (11) / Abyss Chaser (14).</summary>
        private static void RogueGrapple(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom, top = s.Top - 36f, midY = (bot + top) * 0.5f, mb = s.TrunkBirth(midY);
            // Braided taut grappling cable & dark timber column rising from a coiled rope base
            for (int c = 0; c < 3; c++)
                Hoop(b, V(0, bot + 10f + c * 9f, 0), 42f - c * 5f, 24f - c * 3f, c * 4f, 4.5f, true, C(196, 174, 132), 0.25f, 0f, 0.02f + c * 0.01f);
            Twist(b, s, bot + 28f, top, 13f, 10f, 9f, 150f, th.Trunk, C(196, 174, 132), 0.35f);
            // Crossed Golden Stiletto Dagger behind midY
            {
                var dDir = V(0.48f, 0.88f).normalized;
                b.Pen(th.Gold, 0.92f);
                b.Blade(V(-54f, midY - 82f, -8f), dDir, V(dDir.y, -dDir.x), 185f, x => 12f * (1f - x * x), x => 4f * (1f - x), 6, mb, mb + 0.05f, th.Metal);
                b.Pieces++;
            }
            // Crown: Giant Three-Pronged Steel & Gold Grappling Hook at the apex
            float tt = s.TrunkBirth(top);
            Bead(b, V(0, top, -2f), 14f, th.Gold, 0.95f, 0f, tt);
            for (int p = -1; p <= 1; p++)
            {
                var p0 = V(0, top + 12f, -2f);
                var p1 = V(p * 48f, top + 46f, -6f - Mathf.Abs(p) * 4f);
                var p2 = V(p * 62f, top - 4f, -10f);
                if (p == 0) { p1 = V(0, top + 58f, -14f); p2 = V(0, top + 16f, -28f); }
                b.Pen(th.Metal, 0.95f);
                b.Tube(TreeMeshBuilder.Curve(p0, p1, p2, 8), 8f, 3f, 6, tt + 0.02f, tt + 0.06f);
                Tip(b, p2, (p2 - p1).normalized, 24f, 6.5f, th.Gold, 0.95f, tt + 0.06f);
                b.Pieces++;
            }
            // Tier 3 (Stalker, Ornament >= 8): Sculpted Phantom Masquerade Mask & Sweeping Crimson Plume Feather
            if (s.Ornament >= 8)
            {
                b.Pen(C(242, 236, 224), 0.75f);
                b.Plate(ShieldShape(78f, 64f), Lean(V(0, midY + 12f, -14f), -5f), 4.5f, mb + 0.03f, Vector2.zero, Vector2.zero, th.Gold); b.Pieces++;
                foreach (int sg in new[] { -1, 1 })
                {
                    b.Pen(th.Trunk, 0.9f);
                    b.Plate(Diamond(22f, 11f), Lean(V(sg * 18f, midY + 14f, -19.5f), sg * 14f - 5f), 1.2f, mb + 0.05f, Vector2.zero, Vector2.zero, th.Trunk2);
                    Bead(b, V(sg * 18f, midY + 19f, -21f), 3.6f, th.Trunk2, 0.95f, 0.9f, mb + 0.06f);
                }
                LeafPlate(b, V(26f, midY + 38f, -15f), 32f, 76f, 22f, th.Trunk2, 0.4f, 0.2f, mb + 0.06f, th.Gold);
            }
            // Tier 4 (Shadow Chaser, Ornament >= 11): Swirling Abyssal Void-Portal Rings & Chaos Graffiti Paint-Brush Spear
            if (s.Ornament >= 11)
            {
                Hoop(b, V(0, midY + 12f, -6f), 74f, 44f, 22f, 2.8f, true, th.Trunk2, 0.9f, 0.85f, mb + 0.06f);
                Hoop(b, V(0, midY + 12f, -6f), 64f, 38f, -24f, 2.4f, true, th.LeafA, 0.9f, 0.85f, mb + 0.07f);
                var bDir = V(-0.48f, 0.88f).normalized;
                b.Pen(th.ClothA, 0.6f); b.Tube(new[] { V(54f, midY - 82f, -10f), V(-38f, midY + 76f, -10f) }, 5f, 6.5f, 6, mb + 0.04f, mb + 0.08f); b.Pieces++;
                Tip(b, V(-38f, midY + 76f, -10f), bDir, 42f, 13f, th.LeafA, 0.9f, mb + 0.08f);
            }
            // Tier 5 (Abyss Chaser, Ornament >= 14): Omega Void-Rift Halo & Twin Abyssal Fangs
            if (s.Ornament >= 14)
            {
                float ab = tt + 0.06f;
                Hoop(b, V(0, top + 12f, -4f), 82f, 46f, 14f, 3.2f, true, th.Glow, 0.95f, 0.95f, ab);
                foreach (int sg in new[] { -1, 1 })
                    Shard(b, V(sg * 72f, midY + 28f, -16f), sg * -18f, 68f, 11f, th.Trunk2, 0.95f, ab + 0.02f, 0.8f);
            }
        }

        /// <summary>Blacksmith (5) / Whitesmith (8) / Mechanic (11) / Meister (14).</summary>
        private static void BlacksmithForge(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom, top = s.Top - 36f, midY = (bot + top) * 0.5f, mb = s.TrunkBirth(midY), tt = s.TrunkBirth(top);
            // Heavy Horned Master-Forge Anvil at the base with glowing molten forge-fire core
            b.Pen(Shade(th.Metal, 0.78f), 0.75f);
            b.Box(V(0, bot + 10f, 0), V(118f, 18f, 54f), Quaternion.identity, 0f);
            b.Box(V(0, bot + 32f, 0), V(68f, 28f, 38f), Quaternion.identity, 0.01f);
            b.Pen(th.Metal, 0.88f);
            b.Box(V(-8f, bot + 58f, 0), V(124f, 28f, 46f), Quaternion.identity, 0.02f);
            b.Lathe(V(54f, bot + 60f, 0), Vector3.right, new[] { new Vector2(0, 14f), new Vector2(36f, 8f), new Vector2(62f, 0.8f) }, 8, 0.03f, 0.05f); b.Pieces += 2;
            // Reinforced iron-and-brass War-Hammer Haft rising up from the anvil to the crown
            b.Pen(th.Wood, 0.35f);
            b.Tube(new[] { V(0, bot + 72f, 0), V(0, top, 0) }, 17f, 15f, 10, 0.04f, tt); b.Pieces++;
            for (float y = bot + 135f; y < top - 50f; y += 115f)
            {
                b.Pen(th.Gold, 0.92f);
                b.Lathe(V(0, y, 0), Vector3.up, new[] { new Vector2(0, 17.5f), new Vector2(3f, 20.5f), new Vector2(14f, 20.5f), new Vector2(17f, 17.5f) }, 10, s.TrunkBirth(y), s.TrunkBirth(y) + 0.02f);
                b.Pieces++;
            }
            // Colossal Two-Handed Master-Smith War-Hammer Head crowning the top of the tree!
            float hy = top - 28f;
            b.Alive(0.85f, 0f, false, 0f, true); b.Pen(th.Metal, 0.92f);
            b.Box(V(0, hy, -4f), V(156f, 64f, 52f), Quaternion.identity, tt, V(0, hy, -4f)); b.Plain(); b.Pieces++;
            foreach (int sg in new[] { -1, 1 })
            {
                b.Pen(th.Gold, 0.95f);
                b.Box(V(sg * 80f, hy, -4f), V(10f, 70f, 56f), Quaternion.identity, tt + 0.02f);
                b.Pieces++;
            }
            Tip(b, V(0, hy + 32f, -4f), Vector3.up, 52f, 14f, th.Gold, 0.95f, tt + 0.04f);
            Bead(b, V(0, hy, -31f), 14f, th.Glow, 0.95f, 0.9f, tt + 0.04f);
            // Tier 3 (Whitesmith / Mastersmith, Ornament >= 8): Three Interlocking Brass & Steel Clockwork Gears + Molten Ingot on Anvil
            if (s.Ornament >= 8)
            {
                b.Alive(1f, 0f, false, 0.8f); b.Pen(th.Glow, 0.9f, 0.95f);
                b.Box(V(0, bot + 76f, -6f), V(46f, 10f, 22f), Quaternion.identity, 0.04f); b.Plain(); b.Pieces++;
                foreach (var (gx, gy, gr, teeth, col) in new[] { (-36f, midY + 18f, 38f, 12, th.Gold), (34f, midY - 16f, 32f, 10, th.Trunk2), (-18f, midY - 54f, 24f, 8, th.Metal) })
                {
                    b.Pen(col, 0.92f);
                    b.Plate(Gear(teeth, gr, gr * 0.74f), Lean(V(gx, gy, -10f), teeth * 7f), 4f, mb + 0.02f, Vector2.zero, Vector2.zero, Shade(col, 0.65f));
                    Bead(b, V(gx, gy, -13f), gr * 0.25f, th.Glow, 0.9f, 0.6f, mb + 0.04f);
                    b.Pieces++;
                }
            }
            // Tier 4 (Mechanic, Ornament >= 11): Twin Shoulder-Mounted Madogear Vulcan & Pile-Bunker Mecha Cannons + Glowing Reactor Core
            if (s.Ornament >= 11)
            {
                Bead(b, V(0, midY + 6f, -20f), 18f, C(80, 235, 255), 0.95f, 0.95f, mb + 0.05f);
                foreach (int sg in new[] { -1, 1 })
                {
                    var cAt = V(sg * 64f, midY + 22f, -12f);
                    b.Pen(th.Metal, 0.92f); b.Box(cAt, V(34f, 42f, 32f), Quaternion.Euler(0, 0, -sg * 16f), mb + 0.05f); b.Pieces++;
                    b.Pen(th.Gold, 0.95f); b.Lathe(cAt + V(sg * 10f, 18f, -4f), V(sg * 0.28f, 0.96f, 0f).normalized, new[] { new Vector2(0, 10f), new Vector2(54f, 9f), new Vector2(62f, 12f), new Vector2(62f, 4f) }, 8, mb + 0.06f, mb + 0.09f); b.Pieces++;
                }
            }
            // Tier 5 (Meister, Ornament >= 14): Orbital Automated Drone Turrets & Sovereign Forge-Star Halo
            if (s.Ornament >= 14)
            {
                Hoop(b, V(0, hy, -4f), 96f, 52f, 14f, 3.4f, true, th.Gold, 0.95f, 0.85f, tt + 0.05f);
                foreach (int sg in new[] { -1, 1 })
                    StarPlate(b, V(sg * 88f, hy + 28f, -14f), 6, 24f, 11f, 0f, th.Glow, 0.95f, 0.9f, tt + 0.06f);
            }
        }

        /// <summary>Alchemist (5) / Creator (8) / Geneticist (11) / Biolo (14).</summary>
        private static void AlchemistAlembic(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st)
        {
            float bot = s.Bottom, top = s.Top - 40f, midY = (bot + top) * 0.5f, mb = s.TrunkBirth(midY);
            // Bronze alchemical burner tripod & arcane flame at the base
            b.Pen(th.Trunk, 0.85f);
            b.Lathe(V(0, bot, 0), Vector3.up, new[] { new Vector2(0, 48f), new Vector2(18f, 38f), new Vector2(54f, 44f), new Vector2(64f, 26f) }, 12, 0f, 0.06f); b.Pieces++;
            // Double-Helix copper & emerald glass distillation tubes spiraling up the column
            Twist(b, s, bot + 60f, top - 20f, 11f, 8.5f, 22f, 190f, th.Trunk, th.LeafA, 0.9f);
            // Monumental Spherical Alchemical Retort / Incubation Vessel at midY
            b.Alive(0.95f, 0f, false, 0.85f);
            b.Pen(Alpha(th.LeafA, 210), 0.95f, 0.65f);
            b.Ball(V(0, midY, -6f), 46f, 14, 10, mb, 1.08f);
            b.Plain(); b.Pieces++;
            // Brass equator bands & pressure gauges clamping the glass vessel
            Hoop(b, V(0, midY, -6f), 49f, 49f, 12f, 3.2f, true, th.Gold, 0.95f, 0f, mb + 0.04f);
            Hoop(b, V(0, midY, -6f), 49f, 49f, -14f, 2.8f, true, th.Trunk, 0.9f, 0f, mb + 0.05f);
            // Crown: Swan-Neck Alembic Condenser Dome & Glowing Emerald Philosopher's Drop at the apex
            float tt = s.TrunkBirth(top);
            b.Pen(th.Gold, 0.95f);
            b.Lathe(V(0, top - 16f, 0), Vector3.up, new[] { new Vector2(0, 14f), new Vector2(14f, 26f), new Vector2(34f, 18f), new Vector2(46f, 4f) }, 10, tt, tt + 0.04f); b.Pieces++;
            Shard(b, V(0, top + 28f, -4f), 0f, 58f, 14f, th.LeafA, 0.9f, tt + 0.05f, 1f);
            // Tier 3 (Creator / Biochemist, Ornament >= 8): Glowing Golden Homunculus Embryo Core + Twin Acid-Terror & Bottle-Grenade Flasks
            if (s.Ornament >= 8)
            {
                Bead(b, V(0, midY, -10f), 22f, th.Gold, 0.95f, 0.95f, mb + 0.03f);
                foreach (int sg in new[] { -1, 1 })
                {
                    var fAt = V(sg * 58f, midY - 58f, -12f);
                    var fCol = sg < 0 ? th.ClothA : th.Gold;
                    b.Alive(0.9f, 0f, false, 0.5f); b.Pen(fCol, 0.92f, 0.6f);
                    b.Lathe(fAt, Vector3.up, new[] { new Vector2(0, 6f), new Vector2(12f, 20f), new Vector2(28f, 18f), new Vector2(38f, 6f), new Vector2(48f, 8f) }, 9, mb + 0.03f, mb + 0.06f);
                    b.Plain(); b.Pieces++;
                }
            }
            // Tier 4 (Geneticist, Ornament >= 11): Carnivorous Hell-Plant / Mandragora Tendrils & Jaws Embracing the Retort
            if (s.Ornament >= 11)
            {
                foreach (int sg in new[] { -1, 1 })
                {
                    var v0 = V(sg * 24f, bot + 75f, -10f); var v1 = V(sg * 86f, midY + 10f, -16f); var v2 = V(sg * 54f, midY + 82f, -14f);
                    b.Pen(C(58, 175, 88), 0.55f);
                    b.Tube(TreeMeshBuilder.Curve(v0, v1, v2, 9), 7.5f, 3.2f, 6, mb + 0.04f, mb + 0.08f); b.Pieces++;
                    // Carnivorous Hell-Plant blossom head with white fangs at the tendril tip
                    LeafPlate(b, v2, sg * 48f, 46f, 24f, C(195, 42, 82), 0.6f, 0.35f, mb + 0.08f, th.Gold);
                    Tip(b, v2 + V(0, 8f, -4f), V(-sg * 0.5f, 0.86f).normalized, 22f, 5f, C(248, 248, 240), 0.9f, mb + 0.09f);
                }
            }
            // Tier 5 (Biolo, Ornament >= 14): World-Tree Yggdrasil Gene-Helix Crown & Bio-Luminescent Canopy Ring
            if (s.Ornament >= 14)
            {
                Hoop(b, V(0, midY + 18f, -6f), 88f, 50f, 18f, 3.0f, true, th.LeafA, 0.95f, 0.9f, tt + 0.06f);
                StarPlate(b, V(0, top + 68f, -8f), 6, 30f, 12f, 0f, th.Gold, 0.95f, 0.85f, tt + 0.07f);
            }
        }
    }
}
