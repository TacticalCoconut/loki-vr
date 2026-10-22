using System.Collections.Generic;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 22 (w22tree), the design's point 64 (2026-10-11): "Also add thematic spawning animations for each tree, based on the job
    /// they represent." Each tree RISES in its family's own way (the list: <see cref="RiseNotes"/>; the same words in the progress file):
    ///   Swordsman   the sword drops point-first into the ground, a ring of dust and sparks, then the branches unfold from the foot up
    ///   Mage        a rune circle lights on the floor, the staff grows up out of it, its crystal flashes
    ///   Acolyte     the pillar descends slowly in a shaft of light; the panes come out one by one
    ///   Archer      the bow appears and is drawn (it bends), is released (it snaps back, an arrow flies up), the twigs spring out
    ///   Thief       smoke bursts at the foot, the daggers thrust up out of it
    ///   Merchant    the anvil appears and is struck (a flash, sparks, it is driven down and rings back), the pole shoots up, the pipes and wares swing out
    ///   Novice      the sapling sprouts with a little wobble
    ///   Gunslinger  the rifle twirls up, its muzzle flashes once
    ///   Ninja       a puff of white smoke, and the sheathed katana stands there with a sidestep; the leaves come out
    ///   Taekwon     the post hops down and lands with a stomp (it squashes, rings on the floor); the stars pop out one after another
    ///   Summoner    the stalk wriggles up out of the ground and its flowers pop
    /// Rules: one tree's rise lasts <see cref="RiseLength"/> (0.8 s for the plainest tree to 1.0 s for the most ornate: a later job's is
    /// grander: farther, wider, more sparks); the trees of an opening start one after the other so that the last has risen within
    /// <see cref="RiseWithin"/> (design note: "can be up to 1.5s long"); a flip's tree (one at a time) plays the same rise in
    /// <see cref="RiseShortSeconds"/>, without its sound. Only the PICTURE moves (the shader's _Grow and _Trunk* values and a small
    /// additive mesh of its own): every medallion is at its place for the laser, the look and the hand from the first frame. ONE sound
    /// per tree, once, at the rise's own moment (the first of the family's names that the player's install has: UI/TreeThemes.cs
    /// RiseSounds, Audio/LokiAudio.cs HasWav; none found: silent). After the rise NOTHING moves and nothing sounds (design note: "The skill tree
    /// should not have looping sound effects after it spawns ... It should also not have looping distracting animations"): the values are
    /// set back to rest, the rise's mesh is hidden; what stays is the slow breath of the families that have one (the shader's _Breath).
    /// No rise at all: UI/MenuAnim.cs says no (the menu's switch off, reopened within 5 s, a test run).
    /// </summary>
    public sealed partial class SkillTree3D
    {
        /// <summary>All the trees of an opening have risen within this (s); a flip's short rise (s); the gap between two trees' starts (s, at most).</summary>
        public const float RiseWithin = 1.5f, RiseShortSeconds = 0.45f, RiseStagger = 0.25f;
        /// <summary>The value of the shader's _Grow at which everything of a tree is out.</summary>
        private const float Full = 1.14f;
        /// <summary>One tree's rise in seconds: 0.8 for a Novice's tree to 1.0 for a fourth job's.</summary>
        public static float RiseLength(int ornament) => 0.8f + 0.2f * Mathf.Clamp01(ornament / (float)TreeGrowth.Max);
        /// <summary>The last opening's rise from its first tree's start to its last tree's end (s; 0: shown at once).</summary>
        public float RiseTotal { get; private set; }
        /// <summary>Sounds started by rises so far, the last one's name and when (Time.unscaledTime; -1: none yet).</summary>
        public int RiseSoundsStarted { get; private set; }
        public string LastRiseSound { get; private set; } = "";
        public float LastRiseSoundAt { get; private set; } = -1f;

        /// <summary>A tree's picture at a moment of its rise.</summary>
        public struct RisePose
        {
            /// <summary>The shader's _Grow (0: nothing out; <see cref="Full"/>: everything), the trunk object's shift (mm), scale and
            /// roll (radians) about its pivot, whether the trunk is there whole (1) or grows with _Grow (0), and the pivot's height as
            /// a share of the trunk (0: its foot, 0.5: its middle).</summary>
            public float Grow, Roll, Whole, PivotAt; public Vector3 Shift, Scale;
        }

        private static float Seg(float u, float a, float b) => Mathf.Clamp01((u - a) / Mathf.Max(1e-4f, b - a));
        private static float Ease(float x) => x * x * (3f - 2f * x);
        private static float Out(float x) => 1f - (1f - x) * (1f - x);
        private static float OutBack(float x) { const float c1 = 1.70158f, c3 = c1 + 1f; float y = x - 1f; return 1f + c3 * y * y * y + c1 * y * y; }

        /// <summary>When in its rise (0-1) a family's sound starts: the sword's landing, the bow's release, the hammer's blow ...</summary>
        public static float RiseCue(TreeFamily f)
        {
            switch (f)
            {
                case TreeFamily.Swordsman: return 0.34f; case TreeFamily.Archer: return 0.36f; case TreeFamily.Merchant: return 0.16f;
                case TreeFamily.Gunslinger: return 0.55f; case TreeFamily.Taekwon: return 0.24f; case TreeFamily.Thief: case TreeFamily.Ninja: return 0.1f;
                default: return 0.04f;
            }
        }

        public static float RiseCue(JobLine line, TreeFamily f)
        {
            switch (line)
            {
                case JobLine.Hunter: return 0.22f;
                case JobLine.Bard: return 0.18f;
                case JobLine.Monk: return 0.26f;
                case JobLine.Alchemist: return 0.12f;
                case JobLine.Assassin:
                case JobLine.Rogue: return 0.10f;
                default: return RiseCue(f);
            }
        }

        public static RisePose PoseOf(JobLine line, TreeFamily f, float u, int ornament)
        {
            float k = ornament / (float)TreeGrowth.Max;
            switch (line)
            {
                case JobLine.Hunter:
                {
                    // The falcon swoops down from above with a banked glide, lands on the perch, and spreads its wings
                    float swoop = Out(Seg(u, 0f, 0.38f)), settle = Seg(u, 0.38f, 0.68f);
                    var p = new RisePose { Whole = 1f, PivotAt = 0.5f };
                    p.Shift = new Vector3(-180f * (1f - swoop), (420f + 160f * k) * (1f - swoop) - (u >= 0.38f ? 14f * Mathf.Sin(settle * Mathf.PI) * (1f - settle) : 0f), 0f);
                    p.Roll = -0.32f * (1f - swoop);
                    float wingSpread = u < 0.28f ? Mathf.Lerp(0.45f, 1.18f, Out(Seg(u, 0f, 0.28f))) : Mathf.Lerp(1.18f, 1f, Out(Seg(u, 0.28f, 0.65f)));
                    p.Scale = new Vector3(wingSpread, Mathf.Lerp(0.7f, 1f, swoop), 1f);
                    p.Grow = u < 0.30f ? 0f : Full * Out(Seg(u, 0.30f, 0.96f));
                    return p;
                }
                case JobLine.Bard:
                {
                    // The golden lyre rises with a graceful harmonic sway as its strings strum outward
                    float rise = OutBack(Seg(u, 0f, 0.44f));
                    var p = new RisePose { Whole = 1f, PivotAt = 0.35f };
                    p.Scale = new Vector3(rise, rise, 1f);
                    p.Roll = 0.12f * Mathf.Sin(u * Mathf.PI * 4f) * (1f - u);
                    p.Grow = u < 0.22f ? 0f : Full * Ease(Seg(u, 0.22f, 0.95f));
                    return p;
                }
                case JobLine.Monk:
                {
                    // The vajra staff strikes down with a thunderous palm-impact squash and spirit-sphere burst
                    float fall = Seg(u, 0f, 0.26f), hit = Seg(u, 0.26f, 0.54f), q = u >= 0.26f ? Mathf.Sin(hit * Mathf.PI) * (1f - hit) : 0f;
                    var p = new RisePose { Whole = 1f };
                    p.Shift.y = (380f + 140f * k) * (1f - fall * fall);
                    p.Scale = new Vector3(1f + 0.14f * q, 1f - 0.16f * q, 1f);
                    p.Grow = u < 0.26f ? 0f : Full * Out(Seg(u, 0.26f, 0.96f));
                    return p;
                }
            }
            return PoseOf(f, u, ornament);
        }

        /// <summary>A family's rise at <paramref name="u"/> (0-1) for a tree of an ornament level (a later job's goes farther).</summary>
        public static RisePose PoseOf(TreeFamily f, float u, int ornament)
        {
            var p = new RisePose { Scale = Vector3.one };
            float k = ornament / (float)TreeGrowth.Max; // 0 the plainest - 1 the most ornate
            switch (f)
            {
                case TreeFamily.Swordsman:
                {
                    // it falls faster and faster from above, sinks a little as it lands and settles; then the branches from the foot up
                    float fall = Seg(u, 0f, 0.34f), land = Seg(u, 0.34f, 0.52f);
                    p.Whole = 1f; p.Shift.y = (560f + 240f * k) * (1f - fall * fall) - (u >= 0.34f ? 18f * Mathf.Sin(land * Mathf.PI) * (1f - land) : 0f);
                    p.Grow = u < 0.34f ? 0f : Full * Out(Seg(u, 0.34f, 1f));
                    break;
                }
                case TreeFamily.Mage:
                    p.Grow = Full * Ease(Seg(u, 0.08f, 0.92f)); // (out of the rune circle, from the foot up; the flash: the rise's own mesh)
                    break;
                case TreeFamily.Acolyte:
                {
                    // it comes down slower and slower in the shaft of light and stands; then the arches and their panes
                    float d = Seg(u, 0f, 0.5f);
                    p.Whole = 1f; p.Shift.y = (460f + 200f * k) * (1f - d) * (1f - d);
                    p.Grow = u < 0.42f ? 0f : Full * Ease(Seg(u, 0.42f, 1f));
                    break;
                }
                case TreeFamily.Archer:
                {
                    // it stands up, is drawn (it bends wider and a little shorter), is released (it snaps back and rings out)
                    float draw = Out(Seg(u, 0.06f, 0.36f)), rel = Seg(u, 0.36f, 0.72f);
                    float bend = u < 0.36f ? draw : Mathf.Exp(-6f * rel) * Mathf.Cos(rel * 18f);
                    p.Whole = 1f; p.Scale = new Vector3(1f + (0.42f + 0.16f * k) * bend, Out(Seg(u, 0f, 0.14f)) * (1f - 0.07f * bend), 1f);
                    p.Grow = u < 0.36f ? 0f : Full * Out(Seg(u, 0.36f, 0.96f));
                    break;
                }
                case TreeFamily.Thief:
                {
                    // nothing, then out of the smoke: the daggers thrust up past their size and settle
                    float e = u < 0.1f ? 0f : OutBack(Seg(u, 0.1f, 0.48f));
                    p.Whole = 1f; p.Scale = new Vector3(e, e, e);
                    p.Grow = u < 0.3f ? 0f : Full * Ease(Seg(u, 0.3f, 1f));
                    break;
                }
                case TreeFamily.Merchant:
                {
                    // the anvil (the trunk's foot) appears; struck: it is driven down and rings back; the pole shoots up at once; then
                    // the pipes and the wares swing out
                    float hit = Seg(u, 0.16f, 0.5f);
                    p.Grow = u < 0.16f ? 0.14f * Seg(u, 0f, 0.1f) : u < 0.33f ? Mathf.Lerp(0.14f, 0.5f, Out(Seg(u, 0.16f, 0.33f))) : Mathf.Lerp(0.5f, Full, Ease(Seg(u, 0.33f, 1f)));
                    p.Shift.y = u >= 0.16f ? -(22f + 10f * k) * Mathf.Exp(-5f * hit) * Mathf.Cos(hit * 16f) : 0f;
                    break;
                }
                case TreeFamily.Gunslinger:
                {
                    // it twirls up about its middle (one turn, slowing) from a third of its size; then its muzzle flashes and the rods come out
                    p.Whole = 1f; p.PivotAt = 0.5f;
                    p.Roll = -Mathf.PI * 2f * (1f - Out(Seg(u, 0f, 0.5f)));
                    float s = Mathf.Lerp(0.3f, 1f, Out(Seg(u, 0f, 0.4f))); p.Scale = new Vector3(s, s, s);
                    p.Grow = u < 0.5f ? 0f : Full * Ease(Seg(u, 0.5f, 1f));
                    break;
                }
                case TreeFamily.Ninja:
                {
                    // nothing; a puff; it is there, a sidestep off its place, and slides home; the leaves come out
                    float s = u < 0.12f ? 0f : 1f;
                    p.Whole = 1f; p.Scale = new Vector3(s, s, s); p.Shift.x = u >= 0.12f ? (40f + 24f * k) * (1f - Out(Seg(u, 0.12f, 0.36f))) : 0f;
                    p.Grow = u < 0.24f ? 0f : Full * Ease(Seg(u, 0.24f, 1f));
                    break;
                }
                case TreeFamily.Taekwon:
                {
                    // a short hop down, a stomp: it squashes and springs back; then the stars pop out one after another
                    float fall = Seg(u, 0f, 0.24f), sq = Seg(u, 0.24f, 0.5f), q = u >= 0.24f ? Mathf.Sin(sq * Mathf.PI) * (1f - sq) : 0f;
                    p.Whole = 1f; p.Shift.y = (220f + 80f * k) * (1f - fall * fall);
                    p.Scale = new Vector3(1f + 0.10f * q, 1f - 0.13f * q, 1f);
                    p.Grow = u < 0.26f ? 0f : Full * Ease(Seg(u, 0.26f, 1f));
                    break;
                }
                case TreeFamily.Summoner:
                    // it wriggles up out of the ground: a quick sway that dies away, its width pulsing
                    p.Grow = Full * Ease(Seg(u, 0f, 0.88f));
                    p.Roll = (8f + 3f * k) * Mathf.Deg2Rad * Mathf.Sin(u * Mathf.PI * 7f) * (1f - u);
                    p.Scale = new Vector3(1f + 0.07f * Mathf.Sin(u * Mathf.PI * 7f + 1f) * (1f - u), 1f, 1f);
                    break;
                default: // Novice / Super Novice: the sapling sprouts with a little wobble
                    p.Grow = Full * Ease(Seg(u, 0f, 0.9f));
                    p.Roll = (6f + 3f * k) * Mathf.Deg2Rad * Mathf.Sin(u * Mathf.PI * 5f) * (1f - u) * (1f - u);
                    break;
            }
            return p;
        }

        // ------------------------------------------------------------------ playing
        /// <summary>The menu opened: every tree shown rises (the first job's first), or stands at once.</summary>
        private void StartRise(bool play)
        {
            RiseTotal = 0f;
            var list = new List<JobTree>(); foreach (var t in _trees) if (t.Active) list.Add(t);
            if (!play || list.Count == 0) { foreach (var t in _trees) EndRise(t); return; }
            float cap = Mathf.Min(RiseWithin, Mathf.Max(0.2f, MenuAnim.MaxSeconds(MenuAnim.Skills))), k = Mathf.Min(1f, cap / RiseWithin);
            float longest = 0f; foreach (var t in list) longest = Mathf.Max(longest, RiseLength(t.Ornament) * k);
            float gap = list.Count > 1 ? Mathf.Min(RiseStagger * k, (cap - longest) / (list.Count - 1)) : 0f;
            for (int i = 0; i < list.Count; i++) StartRise(list[i], i * gap, false, k);
        }

        private void StartRise(JobTree t, float delay, bool isShort, float k = 1f, bool sound = false)
        {
            t.Rising = true; t.RiseShort = isShort; t.RiseT = -delay; t.RiseDelay = delay; t.RiseLen = isShort ? RiseShortSeconds : RiseLength(t.Ornament) * k;
            t.SoundDone = isShort && !sound; // (a flip's tree rises without its sound: flipping through the trees stays quiet; a tree grown out of its node has its one sound)
            RiseTotal = Mathf.Max(RiseTotal, delay + t.RiseLen);
            ApplyRise(t, 0f);
        }

        private void RiseTick(float dt)
        {
            foreach (var t in _trees)
            {
                if (t.Burying)
                {
                    // back into the ground: the rise backwards, quicker, silent; then it is gone
                    t.BuryT -= dt;
                    if (t.BuryT <= 0f || !t.Active) { t.Burying = false; EndRise(t); LayoutTrees(); LayoutPanels(); }
                    else { t.RiseT = 0f; t.SoundDone = true; ApplyRise(t, Mathf.Clamp01(t.BuryT / BurySeconds) * 0.9f); }
                    continue;
                }
                if (!t.Rising) continue;
                if (!t.Active) { EndRise(t); continue; }
                t.RiseT += dt;
                if (t.RiseT >= t.RiseLen) EndRise(t); else ApplyRise(t, Mathf.Clamp01(t.RiseT / t.RiseLen));
            }
        }

        private void ApplyRise(JobTree t, float u)
        {
            bool waiting = t.RiseT < 0f;
            var p = waiting ? new RisePose { Scale = Vector3.one } : PoseOf(t.Line, t.Family, u, t.Ornament);
            t.Grown = waiting ? 0f : u;
            var s = t.Shape; float pivotY = Mathf.Lerp(s.Bottom, s.Top, p.PivotAt);
            t.MatTree.SetFloat("_Grow", p.Grow); t.MatGlow.SetFloat("_Grow", p.Grow); t.MatMedals.SetFloat("_Grow", p.Grow); t.MatIcons.SetFloat("_Grow", p.Grow);
            t.MatTree.SetVector("_TrunkPivot", new Vector4(0f, pivotY, 0f, 0f));
            t.MatTree.SetVector("_TrunkShift", p.Shift); t.MatTree.SetVector("_TrunkScale", p.Scale);
            t.MatTree.SetVector("_TrunkTurn", new Vector4(0f, 0f, p.Roll, 0f)); t.MatTree.SetFloat("_TrunkWhole", waiting ? 0f : p.Whole);
            _bFx.Clear();
            if (!waiting) Fx(_bFx, t, u);
            _bFx.Fill(t.MeshFx);
            bool fx = _bFx.V.Count > 0; if (t.RendFx.gameObject.activeSelf != fx) t.RendFx.gameObject.SetActive(fx);
            if (!waiting && !t.SoundDone && u >= RiseCue(t.Line, t.Family)) { t.SoundDone = true; RiseSound(t); }
            ApplyTextAlpha(t);
        }

        public void PreviewRise(JobTree t, float u)
        {
            if (t == null) return;
            t.Rising = true;
            t.RiseT = u;
            t.SoundDone = true;
            ApplyRise(t, u);
        }

        public void FinishRise()
        {
            foreach (var t in _trees) EndRise(t);
        }

        /// <summary>The rise is over (or there is none): the tree at rest: everything out, the trunk at its place, the rise's mesh hidden.</summary>
        private void EndRise(JobTree t)
        {
            t.Rising = false;
            StillPose(t);
            if (t.RendFx != null && t.RendFx.gameObject.activeSelf) t.RendFx.gameObject.SetActive(false);
            for (int i = 0; i < t.Alpha.Count; i++) t.Alpha[i] = -1f;
            ApplyTextAlpha(t);
        }

        private void StillPose(JobTree t)
        {
            t.Grown = 1f;
            foreach (var m in new[] { t.MatTree, t.MatGlow, t.MatMedals, t.MatIcons }) m.SetFloat("_Grow", 1.2f);
            t.MatTree.SetVector("_TrunkShift", Vector4.zero); t.MatTree.SetVector("_TrunkScale", new Vector4(1f, 1f, 1f, 0f));
            t.MatTree.SetVector("_TrunkTurn", Vector4.zero); t.MatTree.SetFloat("_TrunkWhole", 0f);
            if (t.Shape != null) t.MatTree.SetVector("_TrunkPivot", new Vector4(0f, t.Shape.Bottom, 0f, 0f));
        }

        // ------------------------------------------------------------------ the one sound
        private readonly Dictionary<TreeFamily, string> _riseSound = new Dictionary<TreeFamily, string>();
        private readonly Dictionary<JobLine, string> _riseLineSound = new Dictionary<JobLine, string>();
        /// <summary>The family's sound: the first of its names that the player's install has ("": none, the rise is silent).</summary>
        public string RiseSoundOf(TreeFamily f)
        {
            if (_riseSound.TryGetValue(f, out string name)) return name;
            name = "";
            var th = TreeTheme.Of(f);
            if (HasSound != null) foreach (var n in th.RiseSounds) if (HasSound(n)) { name = n; break; }
            if (HasSound != null) _riseSound[f] = name; // (asked again once the game's sounds are there)
            Debug.Log($"[Tree] the {f} rise's sound: {(name.Length > 0 ? name : "none of its names is in this install: silent")} (tried {string.Join(", ", th.RiseSounds)})");
            return name;
        }
        public string RiseSoundOf(JobLine line)
        {
            if (_riseLineSound.TryGetValue(line, out string name)) return name;
            name = "";
            var th = TreeTheme.OfLine(line);
            if (HasSound != null) foreach (var n in th.RiseSounds) if (HasSound(n)) { name = n; break; }
            if (HasSound != null) _riseLineSound[line] = name;
            return name;
        }
        private void RiseSound(JobTree t)
        {
            string name = RiseSoundOf(t.Line);
            if (name.Length == 0) name = RiseSoundOf(t.Family);
            if (name.Length == 0 || PlaySound == null) return;
            PlaySound(name); RiseSoundsStarted++; LastRiseSound = name; LastRiseSoundAt = Time.unscaledTime;
        }

        // ------------------------------------------------------------------ the rise's own light (a small additive mesh, rebuilt while it plays)
        private static Color32 Lit(Color32 c, float a) => new Color32(c.r, c.g, c.b, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
        private static Color32 Mixed(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, t);

        /// <summary>A flat ring on the floor (seen from above), soft at both edges.</summary>
        private static void FxFloorRing(TreeMeshBuilder b, Vector3 c, float r, float w, Color32 col, float squash = 0.6f)
        {
            const int n = 28; var clear = Lit(col, 0f); int first = b.V.Count;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n; var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a) * squash);
                b.Pen(clear, 0f, 1f); b.Vert(c + d * Mathf.Max(0f, r - w), Vector3.up, c, 0f);
                b.Pen(col, 0f, 1f); b.Vert(c + d * r, Vector3.up, c, 0f);
                b.Pen(clear, 0f, 1f); b.Vert(c + d * (r + w), Vector3.up, c, 0f);
            }
            for (int i = 0; i < n; i++) { int a0 = first + i * 3, a1 = first + ((i + 1) % n) * 3; b.Quad(a0, a0 + 1, a1 + 1, a1); b.Quad(a0 + 1, a0 + 2, a1 + 2, a1 + 1); }
        }
        /// <summary>A soft round light facing the player.</summary>
        private static void FxBlob(TreeMeshBuilder b, Vector3 c, float rx, float ry, Color32 col)
        {
            const int n = 18; b.Pen(col, 0f, 1f); int mid = b.Vert(c, Vector3.back, c, 0f); b.Pen(Lit(col, 0f), 0f, 1f); int first = b.V.Count;
            for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2f / n; b.Vert(c + new Vector3(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry, 0f), Vector3.back, c, 0f); }
            for (int i = 0; i < n; i++) b.Tri(mid, first + i, first + (i + 1) % n);
        }
        /// <summary>A flash: thin rays from a point and a soft light in their middle.</summary>
        private static void FxStar(TreeMeshBuilder b, Vector3 c, float r, int rays, float turnDeg, Color32 col)
        {
            if (r <= 1f) return;
            var clear = Lit(col, 0f);
            for (int i = 0; i < rays; i++)
            {
                float a = (turnDeg + i * 360f / rays) * Mathf.Deg2Rad; var dir = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f); var side = new Vector3(dir.y, -dir.x, 0f);
                b.Pen(col, 0f, 1f); int v0 = b.Vert(c - side * r * 0.06f, Vector3.back, c, 0f), v1 = b.Vert(c + side * r * 0.06f, Vector3.back, c, 0f);
                b.Pen(clear, 0f, 1f); int v2 = b.Vert(c + dir * r * (i % 2 == 0 ? 1f : 0.62f), Vector3.back, c, 0f);
                b.Tri(v0, v2, v1);
            }
            FxBlob(b, c, r * 0.34f, r * 0.34f, col);
        }
        /// <summary>A shaft of light: bright along its middle, clear at its sides; fainter at its top.</summary>
        private static void FxShaft(TreeMeshBuilder b, float x, float y0, float y1, float hw, Color32 col)
        {
            var clear = Lit(col, 0f); var top = Lit(col, col.a / 255f * 0.35f); var z = new Vector3(0, 0, 6f);
            b.Pen(clear, 0f, 1f); int a0 = b.Vert(new Vector3(x - hw, y0, 0f) + z, Vector3.back, Vector3.zero, 0f), a1 = b.Vert(new Vector3(x - hw, y1, 0f) + z, Vector3.back, Vector3.zero, 0f);
            b.Pen(col, 0f, 1f); int m0 = b.Vert(new Vector3(x, y0, 0f) + z, Vector3.back, Vector3.zero, 0f); b.Pen(top, 0f, 1f); int m1 = b.Vert(new Vector3(x, y1, 0f) + z, Vector3.back, Vector3.zero, 0f);
            b.Pen(clear, 0f, 1f); int c0 = b.Vert(new Vector3(x + hw, y0, 0f) + z, Vector3.back, Vector3.zero, 0f), c1 = b.Vert(new Vector3(x + hw, y1, 0f) + z, Vector3.back, Vector3.zero, 0f);
            b.Quad(a0, m0, m1, a1); b.Quad(m0, c0, c1, m1);
        }
        private static void FxStreak(TreeMeshBuilder b, Vector3 p0, Vector3 p1, float width, Color32 col) { b.Pen(col, 0f, 1f); b.Line(p0, p1, width, 0f, 0f); }

        /// <summary>The light of a family's rise at <paramref name="u"/>: dust, runes, a shaft, an arrow, smoke, sparks, a flash.</summary>
        private static void Fx(TreeMeshBuilder b, JobTree t, float u)
        {
            var s = t.Shape; var th = t.Theme; float k = t.Ornament / (float)TreeGrowth.Max;
            var floor = new Vector3(0f, s.Bottom - 12f, 0f); var foot = new Vector3(0f, s.Bottom, -20f); var top = new Vector3(0f, s.Top, -24f);
            float midY = (s.Top + s.Bottom) * 0.5f; var glow = th.Glow;
            switch (t.Family)
            {
                case TreeFamily.Swordsman:
                {
                    // the landing: a ring of dust running out over the floor (a second one for a transcendent job's tree and later), sparks
                    if (u < 0.34f) break;
                    float e = Seg(u, 0.34f, 0.86f); var dust = Mixed(glow, new Color32(200, 180, 150, 255), 0.5f);
                    FxFloorRing(b, floor, Mathf.Lerp(30f, 210f + 90f * k, Out(e)), 26f, Lit(dust, 0.8f * (1f - e)));
                    if (t.Ornament >= 8 || t.Line == JobLine.Crusader || t.Line == JobLine.Knight)
                    {
                        float e2 = Seg(u, 0.40f, 0.94f);
                        FxFloorRing(b, floor, Mathf.Lerp(20f, 165f + 60f * k, Out(e2)), 18f, Lit(glow, 0.65f * (1f - e2)));
                    }
                    if (t.Line == JobLine.Crusader && u < 0.78f)
                    {
                        // Radiant Grand Cross beams on landing
                        float ec = Seg(u, 0.34f, 0.78f), wc = Mathf.Sin(ec * Mathf.PI);
                        var crossC = new Vector3(0f, midY, -28f);
                        FxStar(b, crossC, (160f + 60f * k) * wc, 4, 0f, Lit(new Color32(255, 240, 175, 255), 0.85f * (1f - ec)));
                    }
                    int n = 6 + t.Ornament / 2;
                    for (int i = 0; i < n; i++)
                    {
                        float a = (i * 137.5f + 20f) * Mathf.Deg2Rad; var dir = new Vector3(Mathf.Cos(a) * 0.9f, 0.35f + 0.5f * Mathf.Abs(Mathf.Sin(a * 1.7f)), 0f).normalized;
                        var p0 = foot + dir * (20f + (150f + 80f * k) * Out(e)); FxStreak(b, p0, p0 + dir * 34f * (1f - e), 3.2f, Lit(new Color32(255, 236, 190, 255), 1f - e));
                    }
                    break;
                }
                case TreeFamily.Mage:
                {
                    // the rune circle on the floor: two rings and marks between them, turning slowly; the crystal's flash
                    float a = Mathf.Min(Seg(u, 0f, 0.12f), 1f - Seg(u, 0.72f, 1f)); float r = 150f + 50f * k;
                    if (a > 0.01f)
                    {
                        FxFloorRing(b, floor, r, 9f, Lit(glow, 0.85f * a)); FxFloorRing(b, floor, r * 0.68f, 6f, Lit(th.LeafB, 0.7f * a));
                        int marks = 8 + (t.Ornament >= 8 ? 4 : 0);
                        for (int i = 0; i < marks; i++)
                        {
                            float g = (i * 360f / marks + u * 70f) * Mathf.Deg2Rad; var d = new Vector3(Mathf.Cos(g), 0f, Mathf.Sin(g) * 0.6f);
                            FxStreak(b, floor + d * r * 0.74f, floor + d * r * 0.94f, 5f, Lit(glow, 0.8f * a));
                        }
                    }
                    if (u >= 0.45f && u < 0.85f)
                    {
                        float e = Seg(u, 0.45f, 0.85f);
                        var flashAt = t.Line == JobLine.Sage ? new Vector3(0f, midY + 40f, -28f) : top + new Vector3(0f, 30f, 0f);
                        FxStar(b, flashAt, (130f + 70f * k) * Mathf.Sin(e * Mathf.PI), t.Line == JobLine.Wizard ? 12 : 8, 22.5f * e, Lit(th.Trunk2, 0.9f));
                    }
                    break;
                }
                case TreeFamily.Acolyte:
                {
                    if (t.Line == JobLine.Monk)
                    {
                        // Monk: palm-strike shockwave rings on the floor + 5 orbiting spirit spheres flashing
                        if (u >= 0.26f)
                        {
                            float e = Seg(u, 0.26f, 0.82f);
                            FxFloorRing(b, floor, Mathf.Lerp(28f, 220f + 70f * k, Out(e)), 24f, Lit(glow, 0.85f * (1f - e)));
                            for (int i = 0; i < 5; i++)
                            {
                                float ang = (i * 72f + e * 220f) * Mathf.Deg2Rad;
                                var sp = new Vector3(Mathf.Cos(ang) * (78f + 30f * e), midY + Mathf.Sin(ang) * 42f, -26f);
                                FxStar(b, sp, 34f * Mathf.Sin(e * Mathf.PI), 4, i * 18f, Lit(th.Gold, 0.9f * (1f - e)));
                            }
                        }
                        break;
                    }
                    // the shaft of light the pillar comes down in; a soft ring where it lands
                    float a = Mathf.Min(Seg(u, 0f, 0.1f), 1f - Seg(u, 0.55f, 0.92f));
                    if (a > 0.01f)
                    {
                        FxShaft(b, 0f, s.Bottom - 10f, s.Top + 520f, 74f + 30f * k, Lit(glow, 0.5f * a));
                        if (t.Ornament >= 8 || t.Line == JobLine.Priest) { FxShaft(b, -120f, s.Bottom - 10f, s.Top + 420f, 22f, Lit(glow, 0.3f * a)); FxShaft(b, 120f, s.Bottom - 10f, s.Top + 420f, 22f, Lit(glow, 0.3f * a)); }
                    }
                    if (u >= 0.46f && u < 0.9f) { float e = Seg(u, 0.46f, 0.9f); FxFloorRing(b, floor, Mathf.Lerp(40f, 170f + 60f * k, Out(e)), 30f, Lit(glow, 0.55f * (1f - e))); }
                    break;
                }
                case TreeFamily.Archer:
                {
                    if (t.Line == JobLine.Hunter)
                    {
                        // Hunter: golden falcon dive-trail & wing-gust shockwave rings with flying golden feathers
                        if (u < 0.15f || u >= 0.78f) break;
                        float e = Seg(u, 0.15f, 0.78f);
                        var perch = new Vector3(0f, midY + 20f, -26f);
                        FxFloorRing(b, perch, Mathf.Lerp(24f, 195f + 65f * k, Out(e)), 20f, Lit(glow, 0.75f * (1f - e)), 0.45f);
                        for (int i = 0; i < 6; i++)
                        {
                            float a = (i * 60f - 150f) * Mathf.Deg2Rad;
                            var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a) * 0.6f, 0f).normalized;
                            var p0 = perch + dir * (36f + (165f + 50f * k) * Out(e));
                            FxStreak(b, p0, p0 + dir * 38f * (1f - e), 4.5f, Lit(th.Gold, 0.9f * (1f - e)));
                        }
                        break;
                    }
                    if (t.Line == JobLine.Bard)
                    {
                        // Bard / Dancer: harmonic sound-wave rings expanding outward from the golden lyre strings
                        if (u < 0.18f || u >= 0.85f) break;
                        for (int r = 0; r < 3; r++)
                        {
                            float er = Seg(u, 0.18f + r * 0.10f, 0.68f + r * 0.08f);
                            if (er <= 0f || er >= 1f) continue;
                            FxBlob(b, new Vector3(0f, midY, -26f), Mathf.Lerp(26f, 170f + 50f * k, Out(er)), Mathf.Lerp(26f, 170f + 50f * k, Out(er)), Lit(r % 2 == 0 ? glow : th.ClothA, 0.35f * (1f - er)));
                        }
                        float e = Seg(u, 0.18f, 0.78f);
                        for (int i = 0; i < 6; i++)
                        {
                            float ang = (i * 60f + e * 90f) * Mathf.Deg2Rad;
                            var c = new Vector3(Mathf.Cos(ang) * (50f + 120f * Out(e)), midY + Mathf.Sin(ang) * (40f + 95f * Out(e)), -28f);
                            FxStar(b, c, 28f * Mathf.Sin(e * Mathf.PI), 4, i * 25f, Lit(th.Gold, 0.9f * (1f - e)));
                        }
                        break;
                    }
                    // Archer bow release: arrow of light shoots HORIZONTALLY (+X, perpendicular to the vertical bow shaft) from the bowstring across the bow!
                    if (u < 0.36f || u >= 0.68f) break;
                    float ea = Seg(u, 0.36f, 0.68f); int arrows = t.Ornament >= 8 ? 3 : 1;
                    for (int i = 0; i < arrows; i++)
                    {
                        float lean = (i - (arrows - 1) * 0.5f) * 0.18f;
                        var dir = new Vector3(Mathf.Cos(lean), Mathf.Sin(lean), 0f); // +X perpendicular to the vertical bow shaft!
                        var p0 = new Vector3(-17f, midY, -26f) + dir * (s.HalfWidth + 260f) * ea;
                        FxStreak(b, p0, p0 + dir * 150f, 5.5f, Lit(glow, 1f - ea));
                        FxStreak(b, p0 + dir * 150f, p0 + dir * 176f, 11.5f, Lit(Color.white, 1f - ea));
                    }
                    FxBlob(b, new Vector3(-17f, midY, -26f), 56f * (1f - ea), 56f * (1f - ea), Lit(glow, 0.7f * (1f - ea)));
                    break;
                }
                case TreeFamily.Thief:
                case TreeFamily.Ninja:
                {
                    // smoke: soft clouds that swell and thin out (the Thief's dark violet at the foot, the Ninja's white along the trunk)
                    bool ninja = t.Family == TreeFamily.Ninja; float end = ninja ? 0.56f : 0.78f; if (u >= end) break;
                    float e = Seg(u, 0f, end); int n = 5 + t.Ornament / 3;
                    var puff = ninja ? new Color32(236, 236, 240, 255) : Mixed(th.Trunk2, new Color32(120, 110, 130, 255), 0.5f);
                    for (int i = 0; i < n; i++)
                    {
                        float a = i * 2.4f, h = ninja ? Mathf.Lerp(s.Bottom + 40f, s.Top - 60f, (i + 0.5f) / n) : s.Bottom + 50f + 150f * ((i * 0.37f) % 1f) + 90f * e;
                        var c = new Vector3(Mathf.Cos(a) * (30f + (ninja ? 60f : 110f) * e), h, -30f); float r = (ninja ? 70f : 60f) + (90f + 40f * k) * e;
                        FxBlob(b, c, r, r * 0.8f, Lit(puff, (ninja ? 0.5f : 0.42f) * (1f - e) * Mathf.Min(1f, e * 8f)));
                    }
                    if (t.Line == JobLine.Assassin && u >= 0.12f && u < 0.52f)
                    {
                        // Assassin: twin crimson Sonic-Blow X-slashes across the katars
                        float es = Seg(u, 0.12f, 0.52f), ws = Mathf.Sin(es * Mathf.PI);
                        var mid = new Vector3(0f, midY, -28f);
                        FxStreak(b, mid + new Vector3(-130f * ws, -110f * ws, 0f), mid + new Vector3(130f * ws, 110f * ws, 0f), 7f * ws, Lit(th.Trunk2, 0.9f * (1f - es)));
                        FxStreak(b, mid + new Vector3(130f * ws, -110f * ws, 0f), mid + new Vector3(-130f * ws, 110f * ws, 0f), 7f * ws, Lit(th.LeafA, 0.85f * (1f - es)));
                    }
                    break;
                }
                case TreeFamily.Merchant:
                {
                    if (t.Line == JobLine.Alchemist)
                    {
                        // Alchemist: effervescent emerald & gold potion bubbles rising from the boiling flask
                        if (u < 0.10f || u >= 0.82f) break;
                        float e = Seg(u, 0.10f, 0.82f);
                        for (int i = 0; i < 8; i++)
                        {
                            float bx = Mathf.Sin(i * 2.1f + e * 5f) * (28f + 45f * ((i % 3) * 0.4f));
                            float by = s.Bottom + 80f + (s.Top - s.Bottom - 60f) * Mathf.Repeat(e * 1.1f + i * 0.13f, 1f);
                            float br = (14f + 6f * (i % 3)) * (1f - e * 0.5f);
                            FxBlob(b, new Vector3(bx, by, -28f), br, br, Lit(i % 2 == 0 ? th.LeafA : th.Gold, 0.7f * (1f - e)));
                        }
                        break;
                    }
                    // the hammer's blow on the anvil: a flash, and sparks that fly out and fall
                    if (u < 0.16f || u >= 0.66f) break;
                    var anvil = new Vector3(0f, s.Bottom + 70f, -30f);
                    if (u < 0.36f) { float f = Seg(u, 0.16f, 0.36f); FxStar(b, anvil, (110f + 60f * k) * Mathf.Sin(f * Mathf.PI), 6, 0f, Lit(new Color32(255, 240, 200, 255), 0.95f)); }
                    float em = Seg(u, 0.16f, 0.66f); int n = 9 + t.Ornament / 2;
                    for (int i = 0; i < n; i++)
                    {
                        float a = (20f + (i * 140f / Mathf.Max(1, n - 1))) * Mathf.Deg2Rad, sp = 0.7f + 0.3f * ((i * 0.618f) % 1f); var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                        var p0 = anvil + dir * (240f + 90f * k) * sp * em + new Vector3(0f, -170f * em * em, 0f); var v = (dir * sp + new Vector3(0f, -1.4f * em, 0f)).normalized;
                        FxStreak(b, p0, p0 + v * 26f, 3f, Lit(new Color32(255, 200, 110, 255), 1f - em));
                    }
                    break;
                }
                case TreeFamily.Gunslinger:
                {
                    // the muzzle's flash, once: a star and a short tongue of fire upwards
                    if (u < 0.55f || u >= 0.78f) break;
                    float e = Seg(u, 0.55f, 0.78f), w = Mathf.Sin(e * Mathf.PI);
                    FxStar(b, top + new Vector3(0f, 16f, 0f), (110f + 70f * k) * w, 6, 30f, Lit(new Color32(255, 220, 150, 255), 0.95f));
                    FxStreak(b, top + new Vector3(0f, 10f, 0f), top + new Vector3(0f, 10f + (180f + 80f * k) * w, 0f), 14f * w, Lit(new Color32(255, 190, 110, 255), 0.8f * (1f - e)));
                    break;
                }
                case TreeFamily.Taekwon:
                {
                    // the stomp: rings running out over the floor (three for a transcendent job's tree and later)
                    if (u < 0.24f) break;
                    int rings = t.Ornament >= 8 ? 3 : 2;
                    for (int i = 0; i < rings; i++)
                    {
                        float e = Seg(u, 0.24f + 0.07f * i, 0.72f + 0.07f * i); if (e <= 0f || e >= 1f) continue;
                        FxFloorRing(b, floor, Mathf.Lerp(30f, 190f + 70f * k - 30f * i, Out(e)), 18f, Lit(glow, 0.8f * (1f - e)));
                    }
                    break;
                }
                case TreeFamily.Summoner:
                {
                    // the flowers pop: small stars near the top, one after the other
                    for (int i = 0; i < 3 + (t.Ornament >= 8 ? 2 : 0); i++)
                    {
                        float e = Seg(u, 0.56f + 0.07f * i, 0.8f + 0.07f * i); if (e <= 0f || e >= 1f) continue;
                        var c = new Vector3((i % 2 == 0 ? -1f : 1f) * (50f + 22f * i), s.Top - 60f - 70f * i, -30f);
                        FxStar(b, c, 46f * Mathf.Sin(e * Mathf.PI), 5, 36f * i, Lit(th.ClothB, 0.9f));
                    }
                    break;
                }
                default:
                {
                    // the sapling breaks the ground: a small ring of earth
                    if (u >= 0.5f) break;
                    float e = Seg(u, 0f, 0.5f); FxFloorRing(b, floor, Mathf.Lerp(16f, 110f + 50f * k, Out(e)), 14f, Lit(Mixed(glow, new Color32(190, 160, 120, 255), 0.6f), 0.7f * (1f - e)));
                    break;
                }
            }
        }

        /// <summary>The families' rises in words (the progress file's list, the smoke's log).</summary>
        public static string RiseNotes(TreeFamily f) => TreeTheme.Of(f).RiseNote;
    }
}
