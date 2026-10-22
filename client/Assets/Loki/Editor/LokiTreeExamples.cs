using System.Collections.Generic;
using Loki.Game;
using Loki.UI;
using UnityEngine;

namespace Loki.EditorTools
{
    /// <summary>
    /// Bespoke 3D procedural skill tree sculptures built with <see cref="TreeMeshBuilder"/> and rendered by <c>LokiTree.shader</c>,
    /// applying all silhouette, anatomy, and palette lessons (true |==| H-frame Katars, zero central poles, zero model reuse across tiers,
    /// and rich 3D directional/rim shading without shader color-bleaching).
    /// </summary>
    public static class LokiTreeExamples
    {
        public static void Enable()
        {
            TreeTheme.PreviewThemeHook = OverrideTheme;
            TreeArt.PreviewPlinthHook = OverridePlinth;
            TreeArt.PreviewBehindHook = OverrideBehind;
            TreeArt.PreviewTrunkHook = OverrideTrunk;
        }

        public static void Disable()
        {
            TreeTheme.PreviewThemeHook = null;
            TreeArt.PreviewPlinthHook = null;
            TreeArt.PreviewBehindHook = null;
            TreeArt.PreviewTrunkHook = null;
        }

        private static Color32 C(int r, int g, int b, int a = 255)
            => new Color32((byte)Mathf.Clamp(r, 0, 255), (byte)Mathf.Clamp(g, 0, 255), (byte)Mathf.Clamp(b, 0, 255), (byte)a);
        private static Color32 Shade(Color32 c, float k)
            => C(Mathf.RoundToInt(c.r * k), Mathf.RoundToInt(c.g * k), Mathf.RoundToInt(c.b * k), c.a);
        private static Color32 Alpha(Color32 c, int a) => new Color32(c.r, c.g, c.b, (byte)Mathf.Clamp(a, 0, 255));
        private static Vector3 V(float x, float y, float z = 0f) => new Vector3(x, y, z);
        private static Matrix4x4 Lean(Vector3 at, float leanDeg, float scale = 1f) => TreeMeshBuilder.At(at, -leanDeg, scale);

        private static string JobConst(int job)
        {
            var raw = SkillTreeDb.GetJob(job);
            if (raw != null && (raw.Const == "JT_ASSASSIN_H" || raw.Const == "JT_DANCER_H"))
                return raw.Const;
            int canon = JobTheme.Canonical(job);
            var j = SkillTreeDb.GetJob(canon >= 0 ? canon : job);
            string c = j != null ? j.Const : (raw != null ? raw.Const : "");
            if (c != "JT_ASSASSIN_H" && c != "JT_DANCER_H")
            {
                if (c.EndsWith("_H") || c.EndsWith("_B")) c = c.Substring(0, c.Length - 2);
                if (c == "JT_WINDHAWK2") c = "JT_WINDHAWK";
            }
            return c;
        }

        // ========================================================================================================
        // THEME PALETTE TUNING (prevents _LifeColor from bleaching 3D meshes into flat pastel mint/white)
        // ========================================================================================================
        private static TreeTheme OverrideTheme(int job, bool other, TreeTheme baseTheme)
        {
            if (other || baseTheme == null) return baseTheme;
            string c = JobConst(job);
            var t = CloneTheme(baseTheme);
            // Keep LifeColor alpha moderate so branches/leaves keep their rich 3D lighting and saturated vertex colors
            t.LifeColor = new Color(t.LifeColor.r, t.LifeColor.g, t.LifeColor.b, 0.32f);

            switch (c)
            {
                case "JT_THIEF":
                    t.Trunk = C(58, 46, 68); t.Trunk2 = C(168, 72, 232); t.Wood = C(68, 52, 82);
                    t.LeafA = C(148, 74, 212); t.LeafB = C(92, 224, 148);
                    t.ClothA = C(124, 48, 176); t.ClothB = C(64, 42, 88);
                    t.Gold = C(224, 178, 82); t.Metal = C(196, 206, 222);
                    t.Glow = C(188, 112, 255);
                    t.LifeColor = new Color(0.78f, 0.48f, 1f, 0.28f);
                    break;

                case "JT_ASSASSIN":
                    // Gunmetal steel, bronze-gold, deep shadow-slate, and emerald Venom Dust accents
                    t.Trunk = C(52, 56, 72); t.Trunk2 = C(64, 224, 132); t.Wood = C(58, 62, 78);
                    t.LeafA = C(64, 218, 128); t.LeafB = C(172, 36, 64);
                    t.ClothA = C(156, 28, 52); t.ClothB = C(48, 188, 112);
                    t.Gold = C(228, 182, 82); t.Metal = C(208, 216, 232);
                    t.Glow = C(82, 242, 148);
                    t.LifeColor = new Color(0.42f, 0.95f, 0.62f, 0.25f);
                    break;

                case "JT_ASSASSIN_H":
                    // Assassin Cross: Obsidian steel, antique royal gold, blood-crimson, and Enchant Deadly Poison royal amethyst!
                    t.Trunk = C(42, 38, 56); t.Trunk2 = C(196, 32, 58); t.Wood = C(58, 44, 68);
                    t.LeafA = C(176, 64, 238); t.LeafB = C(214, 38, 64);
                    t.ClothA = C(178, 24, 48); t.ClothB = C(148, 52, 216);
                    t.Gold = C(242, 194, 84); t.Metal = C(216, 224, 240);
                    t.Glow = C(198, 88, 255);
                    t.RimOrnate = C(242, 194, 84);
                    t.LifeColor = new Color(0.85f, 0.35f, 0.95f, 0.26f);
                    break;

                case "JT_GUILLOTINE_CROSS":
                    // Guillotine Cross: Dark forged executioner iron, blood-crimson, antique gold, and toxic emerald-violet
                    t.Trunk = C(46, 44, 54); t.Trunk2 = C(212, 32, 48); t.Wood = C(54, 48, 62);
                    t.LeafA = C(212, 36, 54); t.LeafB = C(82, 228, 136);
                    t.ClothA = C(168, 22, 38); t.ClothB = C(132, 54, 196);
                    t.Gold = C(232, 184, 78); t.Metal = C(204, 212, 228);
                    t.Glow = C(238, 54, 74);
                    t.LifeColor = new Color(0.95f, 0.28f, 0.36f, 0.26f);
                    break;

                case "JT_SHADOW_CROSS":
                    // Shadow Cross: Midnight raven indigo-violet, moonlight silver-steel, abyssal cyan-purple, and crimson eyes
                    t.Trunk = C(36, 28, 58); t.Trunk2 = C(138, 78, 242); t.Wood = C(48, 38, 76);
                    t.LeafA = C(132, 84, 244); t.LeafB = C(88, 218, 255);
                    t.ClothA = C(96, 48, 184); t.ClothB = C(196, 32, 64);
                    t.Gold = C(236, 196, 96); t.Metal = C(222, 230, 246);
                    t.Glow = C(156, 108, 255);
                    t.LifeColor = new Color(0.62f, 0.48f, 1f, 0.26f);
                    break;

                case "JT_WINDHAWK":
                    t.Trunk = C(156, 108, 62); t.Trunk2 = C(244, 232, 206); t.Wood = C(118, 82, 48);
                    t.LeafA = C(84, 214, 122); t.LeafB = C(242, 192, 78);
                    t.ClothA = C(48, 138, 78); t.ClothB = C(246, 236, 212);
                    t.Gold = C(240, 194, 82); t.Metal = C(212, 220, 234);
                    t.Glow = C(124, 246, 168);
                    t.LifeColor = new Color(0.55f, 0.96f, 0.68f, 0.26f);
                    break;
            }
            if (TreeTheme.IsDancerJob(job))
            {
                t.Trunk = C(218, 38, 82); t.Trunk2 = C(246, 198, 86); t.Wood = C(64, 138, 78);
                t.LeafA = C(236, 58, 104); t.LeafB = C(248, 206, 96);
                t.ClothA = C(214, 34, 78); t.ClothB = C(252, 148, 182);
                t.Gold = C(246, 202, 92); t.Glow = C(255, 168, 198);
                t.LifeColor = new Color(1f, 0.58f, 0.76f, 0.26f);
            }
            return t;
        }

        private static TreeTheme CloneTheme(TreeTheme src)
        {
            return new TreeTheme
            {
                Family = src.Family, Line = src.Line, IsDancer = src.IsDancer, Name = src.Name,
                Branch = src.Branch, Leaf = src.Leaf, Hang = src.Hang,
                Trunk = src.Trunk, Trunk2 = src.Trunk2, Wood = src.Wood, Gold = src.Gold,
                LeafA = src.LeafA, LeafB = src.LeafB, ClothA = src.ClothA, ClothB = src.ClothB,
                Glow = src.Glow, Metal = src.Metal,
                BranchGloss = src.BranchGloss, LeafGloss = src.LeafGloss, LeafGlow = src.LeafGlow,
                Rim = src.Rim, RimOrnate = src.RimOrnate, Face = src.Face,
                LifeColor = src.LifeColor, Breath = src.Breath, BreathPeriod = src.BreathPeriod,
                LifeNote = src.LifeNote, RiseNote = src.RiseNote, RiseSounds = src.RiseSounds
            };
        }

        private static bool IsHandledJob(TreeShape s, TreeTheme th, out string c)
        {
            c = JobConst(s.Job);
            if (th != null && th.IsDancer) return true;
            switch (c)
            {
                case "JT_SWORDMAN":
                case "JT_MAGICIAN":
                case "JT_ARCHER":
                case "JT_ACOLYTE":
                case "JT_MERCHANT":
                case "JT_THIEF":
                case "JT_ASSASSIN":
                case "JT_ASSASSIN_H":
                case "JT_GUILLOTINE_CROSS":
                case "JT_SHADOW_CROSS":
                case "JT_WINDHAWK":
                case "JT_TROUVERE":
                case "JT_WANDERER":
                case "JT_DANCER_H":
                case "JT_DANCER":
                    return true;
                default:
                    return false;
            }
        }

        // ========================================================================================================
        // PLINTH & BEHIND HOOKS
        // ========================================================================================================
        private static bool OverridePlinth(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeArt.Counts k)
        {
            // All 12 bespoke sculptures build their own custom themed base pedestal/plinth
            return IsHandledJob(s, th, out _);
        }

        private static bool OverrideBehind(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st, TreeArt.Counts k)
        {
            if (!IsHandledJob(s, th, out string c)) return false;
            float bot = s.Bottom, top = s.Top, midY = (bot + top) * 0.5f;
            b.Plain();

            if (th != null && th.IsDancer)
            {
                // Golden rose-window halo & 7 vertical golden harp strings behind the roses
                b.Alive(0.25f, 0f, false, 0.3f, true);
                b.Pen(Alpha(C(246, 202, 92), 185), 0.9f, 0.55f);
                b.Arc(V(0, midY + 12f, 10f), 86f, 92f, 0f, 360f, 28, 0.05f, V(0, midY + 12f, 10f));
                b.Plain();
                k.TrunkParts++;
                return true;
            }

            switch (c)
            {
                case "JT_SWORDMAN":
                    // Subtle warm forge-gold ring behind the crossed broadswords & buckler
                    b.Alive(0.2f, 0f, false, 0f, true);
                    b.Pen(Alpha(C(232, 184, 78), 160), 0.85f, 0.4f);
                    b.Arc(V(0, midY - 6f, 12f), 98f, 104f, 0f, 360f, 28, 0.04f, V(0, midY - 6f, 12f));
                    b.Plain();
                    break;

                case "JT_MAGICIAN":
                    // Far half of the golden astrological rune ring behind the Arc Wand
                    HoopPart(b, V(0, top - 42f, 0), 68f, 40f, 16f, 2.8f, false, C(238, 194, 88), 0.92f, 0.45f, 0.12f);
                    HoopPart(b, V(0, midY + 10f, 0), 82f, 46f, -14f, 2.4f, false, C(110, 198, 255), 0.9f, 0.65f, 0.10f);
                    break;

                case "JT_ARCHER":
                    // Emerald woodland wind-ring behind the crossed recurve bow & quiver
                    b.Alive(0.25f, 0f, false, 0f, true);
                    b.Pen(Alpha(C(112, 224, 126), 150), 0.8f, 0.5f);
                    b.Arc(V(0, midY, 12f), 84f, 89f, 0f, 360f, 24, 0.05f, V(0, midY, 12f));
                    b.Plain();
                    break;

                case "JT_ACOLYTE":
                    // Radiant golden cathedral halo & 8 sun-rays behind the Sanctuary Cross
                    {
                        float cy = Mathf.Lerp(bot, top, 0.64f);
                        b.Alive(0.3f, 0f, false, 0.3f, true);
                        b.Pen(Alpha(C(248, 212, 106), 195), 0.92f, 0.65f);
                        b.Arc(V(0, cy, 10f), 74f, 81f, 0f, 360f, 28, 0.06f, V(0, cy, 10f));
                        for (int i = 0; i < 8; i++)
                        {
                            float a = (i * 45f + 22.5f) * Mathf.Deg2Rad;
                            var d = V(Mathf.Sin(a), Mathf.Cos(a));
                            TipPart(b, V(0, cy, 11f) + d * 78f, d, 38f, 6f, C(248, 212, 106), 0.9f, 0.5f, 0.08f);
                        }
                        b.Plain();
                    }
                    break;

                case "JT_ASSASSIN":
                    // 8-Pointed Spiked Grimtooth Chakram Star behind the 3-pronged Specialty Jur knuckle guard
                    {
                        float cy = midY - 12f;
                        b.Plain();
                        b.Pen(C(164, 174, 192), 0.92f);
                        b.Plate(GearOutline(8, 72f, 54f), Lean(V(0, cy, 8f), 22.5f), 4f, 0.06f, Vector2.zero, Vector2.zero, C(68, 74, 90));
                        for (int i = 0; i < 8; i++)
                        {
                            float a = (i * 45f + 22.5f) * Mathf.Deg2Rad;
                            var d = V(Mathf.Sin(a), Mathf.Cos(a));
                            TipPart(b, V(0, cy, 8f) + d * 68f, d, 30f, 7.5f, i % 2 == 0 ? C(208, 216, 232) : C(64, 224, 132), 0.92f, i % 2 == 0 ? 0f : 0.45f, 0.08f);
                        }
                    }
                    break;

                case "JT_ASSASSIN_H":
                    // Crimson & Royal-Amethyst EDP Cross-Slash Aura behind the Infiltrator Katar
                    {
                        float cy = midY + 10f;
                        b.Alive(0.35f, 0f, false, 0.4f, true);
                        b.Pen(Alpha(C(186, 52, 238), 165), 0.9f, 0.7f);
                        b.Arc(V(0, cy, 10f), 78f, 84f, 0f, 360f, 28, 0.05f, V(0, cy, 10f));
                        foreach (int sg in new[] { -1, 1 })
                        {
                            var d = V(sg * 0.62f, 0.78f).normalized;
                            TipPart(b, V(0, cy, 11f) + d * 56f, d, 58f, 9f, C(214, 34, 62), 0.9f, 0.65f, 0.07f);
                            var d2 = V(sg * 0.62f, -0.78f).normalized;
                            TipPart(b, V(0, cy, 11f) + d2 * 56f, d2, 52f, 8f, C(172, 58, 236), 0.9f, 0.65f, 0.07f);
                        }
                        b.Plain();
                    }
                    break;

                case "JT_SHADOW_CROSS":
                    // Eclipse Shadow-Moon Ring behind the Fatal Shadow Crow
                    b.Alive(0.35f, 0f, false, 0.4f, true);
                    b.Pen(Alpha(C(138, 78, 242), 175), 0.9f, 0.75f);
                    b.Arc(V(0, midY + 18f, 10f), 88f, 95f, 0f, 360f, 28, 0.05f, V(0, midY + 18f, 10f));
                    b.Plain();
                    break;

                case "JT_WINDHAWK":
                    // Emerald Gale-Current Ring behind the soaring Windhawk
                    b.Alive(0.3f, 0f, false, 0.35f, true);
                    b.Pen(Alpha(C(96, 236, 152), 170), 0.9f, 0.65f);
                    b.Arc(V(0, midY + 24f, 10f), 92f, 98f, 0f, 360f, 28, 0.05f, V(0, midY + 24f, 10f));
                    b.Plain();
                    break;
            }
            k.TrunkParts++;
            return true;
        }

        // ========================================================================================================
        // MAIN TRUNK DISPATCHER
        // ========================================================================================================
        private static bool OverrideTrunk(TreeMeshBuilder b, TreeShape s, TreeTheme th, TreeGrowth.Stage st, TreeArt.Counts k)
        {
            if (!IsHandledJob(s, th, out string c)) return false;
            b.Plain();

            if (th != null && th.IsDancer)
            {
                BuildDivaRosesHarp(b, s);
                return true;
            }

            switch (c)
            {
                case "JT_SWORDMAN":         BuildSwordmanCrossedSwords(b, s); return true;
                case "JT_MAGICIAN":         BuildMageGrimoireWand(b, s); return true;
                case "JT_ARCHER":           BuildArcherRecurveQuiver(b, s); return true;
                case "JT_ACOLYTE":          BuildAcolyteChaliceCross(b, s); return true;
                case "JT_MERCHANT":         BuildMerchantPushcartZeny(b, s); return true;
                case "JT_THIEF":            BuildThiefCrossedStilettos(b, s); return true;
                case "JT_ASSASSIN":         BuildAssassinSpecialtyJur(b, s); return true;
                case "JT_ASSASSIN_H":       BuildAssassinCrossInfiltratorKatar(b, s); return true;
                case "JT_GUILLOTINE_CROSS": BuildGuillotineCrossFrame(b, s); return true;
                case "JT_SHADOW_CROSS":     BuildShadowCrossCrowScythe(b, s); return true;
                case "JT_WINDHAWK":         BuildWindhawkRaptorBow(b, s); return true;
                default:                    return false;
            }
        }

        // ========================================================================================================
        // 1. ASSASSIN CROSS (JT_ASSASSIN_H) — ORNATE "INFILTRATOR" KATAR MONUMENT (TRUE |==| H-FRAME)
        // ========================================================================================================
        private static void BuildAssassinCrossInfiltratorKatar(TreeMeshBuilder b, TreeShape s)
        {
            float bot = s.Bottom, top = s.Top, span = top - bot, midY = (bot + top) * 0.5f;
            var obsidian = C(44, 40, 58);
            var darkSteel = C(78, 84, 104);
            var brightSteel = C(218, 226, 242);
            var gold = C(242, 192, 82);
            var darkGold = C(178, 132, 46);
            var crimson = C(186, 24, 48);
            var brightCrimson = C(238, 44, 72);
            var edpPurple = C(184, 58, 248);

            // Base: Stepped Obsidian & Crimson-Gold Shadow Plinth with EDP runes
            b.Plain();
            b.Pen(obsidian, 0.75f);
            b.Lathe(V(0, bot - 18f, 0), Vector3.up, new[] { new Vector2(0, 82f), new Vector2(10f, 76f), new Vector2(12f, 58f), new Vector2(22f, 52f), new Vector2(22f, 0f) }, 12, 0f, 0.03f, false);
            b.Pen(gold, 0.92f);
            b.Lathe(V(0, bot - 8f, 0), Vector3.up, new[] { new Vector2(0, 77f), new Vector2(2.5f, 79.5f), new Vector2(5f, 77f) }, 12, 0.02f, 0.03f);
            b.Pieces += 2;

            // Katar geometry vertical landmarks
            float wristBot = bot + 14f;
            float hoodY = bot + span * 0.36f;
            float grip1Y = Mathf.Lerp(wristBot, hoodY, 0.36f);
            float grip2Y = Mathf.Lerp(wristBot, hoodY, 0.70f);
            float bladeStart = hoodY + 12f;
            float bladeTip = top + 72f;
            float bladeLen = bladeTip - bladeStart;
            float barX = 48f; // Half-spacing between the two parallel |==| forearm sidebars (96mm total gap!)

            // 1. TRUE |==| H-FRAME PARALLEL FOREARM SIDEBARS (Left bar at -48mm, Right bar at +48mm, OPEN SPACE between!)
            foreach (int sg in new[] { -1, 1 })
            {
                float x = sg * barX;
                // Main obsidian-steel longitudinal forearm bar
                b.Plain();
                b.Pen(obsidian, 0.88f);
                b.Box(V(x, (wristBot + hoodY) * 0.5f, -6f), V(16f, hoodY - wristBot + 10f, 16f), Quaternion.identity, 0.04f);
                // Outer antique-gold beveled rail trim along the forearm bar
                b.Pen(gold, 0.94f);
                b.Box(V(x + sg * 9.5f, (wristBot + hoodY) * 0.5f, -6f), V(4.5f, hoodY - wristBot + 4f, 18f), Quaternion.identity, 0.05f);
                // Flared golden wrist-pommel claw at the bottom of each sidebar
                TipPart(b, V(x, wristBot + 4f, -6f), V(sg * 0.18f, -0.98f, 0f).normalized, 34f, 11f, gold, 0.94f, 0f, 0.05f);
                // Crimson enamel rivets along the sidebar
                for (int r = 0; r < 3; r++)
                {
                    float ry = Mathf.Lerp(wristBot + 22f, hoodY - 18f, r / 2f);
                    GemBall(b, V(x, ry, -15.5f), 4.2f, brightCrimson, 0.92f, 0.45f, 0.06f);
                }
                b.Pieces += 3;
            }

            // 2. TWIN HORIZONTAL FIST-GRIP CROSSBARS (|==| H-frame rungs spanning -48mm to +48mm)
            foreach (float gy in new[] { grip1Y, grip2Y })
            {
                b.Plain();
                b.Pen(gold, 0.94f);
                b.Tube(new[] { V(-barX + 4f, gy, -6f), V(barX - 4f, gy, -6f) }, 7.5f, 7.5f, 8, 0.06f, 0.08f, false, false);
                // Crimson leather grip-wrap rings in the middle of each rung
                for (int w = -2; w <= 2; w++)
                {
                    b.Pen(w % 2 == 0 ? crimson : darkGold, 0.65f);
                    b.Lathe(V(w * 11f - 4f, gy, -6f), Vector3.right, new[] { new Vector2(0, 8.2f), new Vector2(8f, 8.2f) }, 8, 0.07f, 0.08f, false);
                }
                // Heavy golden collar flanges where the grip rungs lock into the sidebars
                foreach (int sg in new[] { -1, 1 })
                {
                    b.Pen(gold, 0.95f);
                    b.Box(V(sg * (barX - 6f), gy, -6f), V(10f, 18f, 19f), Quaternion.identity, 0.07f);
                }
                b.Pieces += 2;
            }
            // Ornate vertical golden bridge-clasp with a glowing blood-ruby linking the two horizontal grip rungs
            b.Plain();
            b.Pen(gold, 0.95f);
            b.Box(V(0, (grip1Y + grip2Y) * 0.5f, -6f), V(14f, grip2Y - grip1Y, 14f), Quaternion.identity, 0.08f);
            GemBall(b, V(0, (grip1Y + grip2Y) * 0.5f, -14f), 9.5f, brightCrimson, 0.95f, 0.8f, 0.09f);

            // 3. SWEEPING WINGED CRIMSON & GOLD KNUCKLE-HOOD GUARD (bridging the top of the sidebars)
            var hoodOutline = new List<Vector2>
            {
                new Vector2(-78f, -12f), new Vector2(-114f, 6f), new Vector2(-92f, 26f),
                new Vector2(-38f, 18f), new Vector2(0f, 32f), new Vector2(38f, 18f),
                new Vector2(92f, 26f), new Vector2(114f, 6f), new Vector2(78f, -12f),
                new Vector2(42f, -18f), new Vector2(0f, -8f), new Vector2(-42f, -18f)
            };
            b.Plain();
            b.Pen(crimson, 0.85f);
            b.Plate(hoodOutline, Lean(V(0, hoodY, -9f), 0f), 14f, 0.09f, Vector2.zero, new Vector2(0, 6f), gold);
            // Inner gold chevron plate on the knuckle hood
            var innerHood = new List<Vector2>
            {
                new Vector2(-58f, -4f), new Vector2(-82f, 8f), new Vector2(-28f, 14f),
                new Vector2(0f, 24f), new Vector2(28f, 14f), new Vector2(82f, 8f),
                new Vector2(58f, -4f), new Vector2(0f, 2f)
            };
            b.Pen(gold, 0.95f);
            b.Plate(innerHood, Lean(V(0, hoodY + 2f, -14f), 0f), 6f, 0.10f, Vector2.zero, new Vector2(0, 8f), darkGold);
            // Swept bat-wing side-barbs flaring outward from the knuckle guard
            foreach (int sg in new[] { -1, 1 })
            {
                TipPart(b, V(sg * 96f, hoodY + 12f, -9f), V(sg * 0.78f, 0.62f, 0f).normalized, 46f, 12f, gold, 0.95f, 0f, 0.11f);
                TipPart(b, V(sg * 82f, hoodY - 4f, -9f), V(sg * 0.88f, -0.46f, 0f).normalized, 34f, 9f, brightSteel, 0.92f, 0f, 0.11f);
            }
            // Central Royal-Amethyst Enchant Deadly Poison Gem in the Knuckle Hood
            StarPlatePlain(b, V(0, hoodY + 8f, -18f), 4, 24f, 10f, 45f, gold, 0.95f, 0.11f);
            GemBall(b, V(0, hoodY + 8f, -21f), 11.5f, edpPurple, 0.95f, 0.9f, 0.12f);
            b.Pieces += 4;

            // 4. WIDE TRIANGULAR PUNCH-DAGGER BLADE (with recessed dark fuller slot, wing-barbs & EDP veins)
            b.Plain();
            b.Pen(brightSteel, 0.95f);
            b.Blade(
                V(0, bladeStart, -6f), Vector3.up, Vector3.right, bladeLen,
                u =>
                {
                    if (u < 0.08f) return Mathf.Lerp(34f, 42f, u / 0.08f);
                    if (u < 0.28f) return Mathf.Lerp(42f, 32f, (u - 0.08f) / 0.20f);
                    if (u < 0.36f) return Mathf.Lerp(32f, 44f, (u - 0.28f) / 0.08f); // aggressive serration notch flare
                    return 44f * (1f - Mathf.Pow((u - 0.36f) / 0.64f, 0.88f));
                },
                u => 10f * (1f - 0.82f * u),
                18, 0.12f, s.TrunkGrown, darkSteel
            );
            b.Pieces++;

            // Aggressive Swept Katar Side-Barbs along the lower and middle blade edges
            foreach (int sg in new[] { -1, 1 })
            {
                float yBarb1 = bladeStart + bladeLen * 0.22f;
                float yBarb2 = bladeStart + bladeLen * 0.46f;
                TipPart(b, V(sg * 34f, yBarb1, -6f), V(sg * 0.68f, 0.73f, 0f).normalized, 48f, 11f, brightSteel, 0.95f, 0f, 0.18f);
                TipPart(b, V(sg * 28f, yBarb2, -6f), V(sg * 0.58f, 0.81f, 0f).normalized, 38f, 9f, gold, 0.95f, 0f, 0.22f);
            }

            // Recessed Dark Obsidian Central Fuller Slot (the hollow blood-channel of an ornate Katar)
            float slotY0 = bladeStart + bladeLen * 0.06f;
            float slotY1 = bladeStart + bladeLen * 0.62f;
            b.Plain();
            b.Pen(obsidian, 0.85f);
            b.Box(V(0, (slotY0 + slotY1) * 0.5f, -12f), V(13f, slotY1 - slotY0, 6f), Quaternion.identity, 0.16f);
            // Golden inner ribs framing the fuller slot
            foreach (int sg in new[] { -1, 1 })
            {
                b.Pen(gold, 0.95f);
                b.Tube(new[] { V(sg * 7.5f, slotY0, -14f), V(sg * 5.5f, slotY1, -12f), V(0, slotY1 + 28f, -10f) }, 2.4f, 1.2f, 5, 0.16f, 0.28f);
            }

            // Glowing Royal-Amethyst & Crimson Enchant Deadly Poison (EDP) Energy Veins inside the fuller & rising to the tip
            b.Alive(0.45f, 0f, false, 0.7f, true);
            b.Pen(edpPurple, 0.95f, 0.9f);
            b.Line(V(0, slotY0 + 6f, -15.5f), V(0, bladeTip - 18f, -9f), 3.6f, 0.18f, s.TrunkGrown);
            for (int v = 0; v < 4; v++)
            {
                float vy = Mathf.Lerp(slotY0 + 24f, slotY1 - 12f, v / 3f);
                GemBall(b, V(0, vy, -16f), 5.2f - v * 0.6f, v % 2 == 0 ? edpPurple : brightCrimson, 0.95f, 0.95f, 0.20f + v * 0.02f);
            }
            b.Plain();
        }

        // ========================================================================================================
        // 2. ASSASSIN (JT_ASSASSIN) — 3-PRONGED "SPECIALTY JUR" KATAR (TRUE |==| H-FRAME)
        // ========================================================================================================
        private static void BuildAssassinSpecialtyJur(TreeMeshBuilder b, TreeShape s)
        {
            float bot = s.Bottom, top = s.Top, span = top - bot;
            var gunmetal = C(56, 62, 78);
            var darkSlate = C(38, 42, 54);
            var steel = C(208, 218, 234);
            var bronzeGold = C(218, 172, 74);
            var crimson = C(168, 32, 54);
            var venomGreen = C(64, 232, 132);

            // Base: Dark Shadow-Stone Plinth with Flat Horizontal Emerald Venom Pool
            b.Plain();
            b.Pen(darkSlate, 0.65f);
            b.Lathe(V(0, bot - 16f, 0), Vector3.up, new[] { new Vector2(0, 74f), new Vector2(12f, 68f), new Vector2(20f, 52f), new Vector2(20f, 0f) }, 10, 0f, 0.03f, false);
            b.Alive(0.4f, 0f, false, 0.6f, true);
            b.Pen(venomGreen, 0.9f, 0.8f);
            b.Lathe(V(0, bot + 4.2f, 0), Vector3.up, new[] { new Vector2(0f, 48f), new Vector2(2.5f, 44f), new Vector2(3.5f, 0f) }, 14, 0.03f, 0.04f);
            b.Plain();

            float wristBot = bot + 16f;
            float hoodY = bot + span * 0.38f;
            float grip1Y = Mathf.Lerp(wristBot, hoodY, 0.36f);
            float grip2Y = Mathf.Lerp(wristBot, hoodY, 0.70f);
            float bladeStart = hoodY + 10f;
            float bladeTip = top + 58f;
            float bladeLen = bladeTip - bladeStart;
            float barX = 44f;

            // 1. TRUE |==| H-FRAME GUNMETAL FOREARM SIDEBARS (-44mm and +44mm, open negative space between)
            foreach (int sg in new[] { -1, 1 })
            {
                float x = sg * barX;
                b.Plain();
                b.Pen(gunmetal, 0.85f);
                b.Box(V(x, (wristBot + hoodY) * 0.5f, -6f), V(14f, hoodY - wristBot + 8f, 14f), Quaternion.identity, 0.04f);
                b.Pen(bronzeGold, 0.9f);
                b.Box(V(x + sg * 8f, (wristBot + hoodY) * 0.5f, -6f), V(3.5f, hoodY - wristBot, 16f), Quaternion.identity, 0.05f);
                TipPart(b, V(x, wristBot + 2f, -6f), Vector3.down, 26f, 8.5f, bronzeGold, 0.9f, 0f, 0.05f);
                b.Pieces += 2;
            }

            // 2. TWIN HORIZONTAL GRIP CROSSBARS
            foreach (float gy in new[] { grip1Y, grip2Y })
            {
                b.Plain();
                b.Pen(bronzeGold, 0.92f);
                b.Tube(new[] { V(-barX + 3f, gy, -6f), V(barX - 3f, gy, -6f) }, 6.8f, 6.8f, 8, 0.06f, 0.08f, false, false);
                for (int w = -2; w <= 2; w++)
                {
                    b.Pen(w % 2 == 0 ? crimson : darkSlate, 0.45f);
                    b.Lathe(V(w * 10f - 3.5f, gy, -6f), Vector3.right, new[] { new Vector2(0, 7.5f), new Vector2(7f, 7.5f) }, 8, 0.07f, 0.08f, false);
                }
                b.Pieces++;
            }

            // 3. ARCHED GUNMETAL & BRONZE KNUCKLE GUARD
            var guardShape = new List<Vector2>
            {
                new Vector2(-68f, -10f), new Vector2(-88f, 8f), new Vector2(-56f, 20f),
                new Vector2(0f, 26f), new Vector2(56f, 20f), new Vector2(88f, 8f),
                new Vector2(68f, -10f), new Vector2(0f, -6f)
            };
            b.Plain();
            b.Pen(gunmetal, 0.88f);
            b.Plate(guardShape, Lean(V(0, hoodY, -8f), 0f), 12f, 0.09f, Vector2.zero, new Vector2(0, 8f), bronzeGold);
            GemBall(b, V(0, hoodY + 6f, -16f), 9.5f, venomGreen, 0.95f, 0.85f, 0.10f);
            b.Pieces += 2;

            // 4. THREE PARALLEL UPWARD "SPECIALTY JUR" PUNCH-DAGGER BLADES (1 long center + 2 side-fang blades!)
            // Center Main Blade
            b.Plain();
            b.Pen(steel, 0.94f);
            b.Blade(
                V(0, bladeStart, -6f), Vector3.up, Vector3.right, bladeLen,
                u => u < 0.12f ? Mathf.Lerp(22f, 28f, u / 0.12f) : 28f * (1f - Mathf.Pow((u - 0.12f) / 0.88f, 0.9f)),
                u => 8.5f * (1f - 0.8f * u),
                14, 0.11f, s.TrunkGrown, gunmetal
            );
            // Emerald Venom Dust channel along the center blade
            b.Alive(0.45f, 0f, false, 0.6f, true);
            b.Pen(venomGreen, 0.95f, 0.85f);
            b.Line(V(0, bladeStart + 18f, -12.5f), V(0, bladeTip - 24f, -8f), 3.2f, 0.15f, s.TrunkGrown);
            b.Plain();

            // Left & Right Parallel Side-Fang Blades (the iconic 3-bladed Specialty Jur silhouette!)
            foreach (int sg in new[] { -1, 1 })
            {
                var sideStart = V(sg * 52f, hoodY + 8f, -7f);
                var sideDir = V(sg * 0.07f, 0.997f, 0f).normalized;
                var sidePerp = V(sideDir.y, -sideDir.x, 0f);
                float sideLen = bladeLen * 0.72f;
                b.Plain();
                b.Pen(steel, 0.94f);
                b.Blade(
                    sideStart, sideDir, sidePerp, sideLen,
                    u => u < 0.15f ? Mathf.Lerp(12f, 16f, u / 0.15f) : 16f * (1f - (u - 0.15f) / 0.85f),
                    u => 6f * (1f - 0.8f * u),
                    10, 0.12f, s.TrunkGrown * 0.9f, gunmetal
                );
                // Bronze-gold bridge strut locking each side blade to the central blade
                b.Pen(bronzeGold, 0.92f);
                b.Box(V(sg * 28f, bladeStart + 42f, -7f), V(44f, 10f, 10f), Quaternion.Euler(0, 0, -sg * 12f), 0.14f);
                // Venom groove on side blade
                b.Alive(0.45f, 0f, false, 0.6f, true);
                b.Pen(venomGreen, 0.95f, 0.85f);
                b.Line(sideStart + sideDir * 16f + V(0, 0, -5f), sideStart + sideDir * (sideLen - 18f) + V(0, 0, -3f), 2.4f, 0.16f, s.TrunkGrown * 0.9f);
                b.Plain();
                b.Pieces += 2;
            }
        }

        // ========================================================================================================
        // 3. GUILLOTINE CROSS (JT_GUILLOTINE_CROSS) — EXECUTIONER'S GUILLOTINE FRAME, DROP-BLADE & ROLLING CUTTER
        // ========================================================================================================
        private static void BuildGuillotineCrossFrame(TreeMeshBuilder b, TreeShape s)
        {
            float bot = s.Bottom, top = s.Top, span = top - bot, midY = (bot + top) * 0.5f;
            var darkIron = C(44, 42, 54);
            var slate = C(68, 66, 82);
            var steel = C(214, 222, 238);
            var gold = C(236, 188, 82);
            var crimson = C(212, 28, 48);
            var venomGreen = C(76, 236, 138);
            var venomPurple = C(178, 64, 244);

            float postX = 72f;
            float beamY = top - 16f;
            float stockY = bot + 82f;

            // 1. Stepped Dark-Iron Executioner's Scaffold Base
            b.Plain();
            b.Pen(darkIron, 0.75f);
            b.Box(V(0, bot + 4f, -4f), V(196f, 22f, 64f), Quaternion.identity, 0f);
            b.Pen(gold, 0.92f);
            b.Box(V(0, bot + 18f, -4f), V(176f, 8f, 52f), Quaternion.identity, 0.02f);
            b.Pieces += 2;

            // 2. Twin Vertical Guillotine Posts (-72mm and +72mm, leaving the central blade chute wide open!)
            foreach (int sg in new[] { -1, 1 })
            {
                float x = sg * postX;
                b.Plain();
                b.Pen(darkIron, 0.82f);
                b.Box(V(x, (bot + 20f + beamY) * 0.5f, -6f), V(20f, beamY - bot - 20f, 20f), Quaternion.identity, 0.04f);
                // Inner steel guide-rail track for the drop-blade
                b.Pen(steel, 0.92f);
                b.Box(V(x - sg * 11f, (stockY + beamY) * 0.5f, -6f), V(4f, beamY - stockY, 10f), Quaternion.identity, 0.06f);
                // Golden gothic bands clamping each vertical post
                for (int i = 0; i < 4; i++)
                {
                    float by = Mathf.Lerp(bot + 50f, beamY - 40f, i / 3f);
                    b.Pen(gold, 0.94f);
                    b.Box(V(x, by, -6f), V(26f, 10f, 24f), Quaternion.identity, 0.06f + i * 0.01f);
                }
                b.Pieces += 3;
            }

            // 3. Lower Lunette Neck-Stock (spanning between the posts at bot + 82mm with a semicircular neck-cutout)
            b.Plain();
            b.Pen(slate, 0.78f);
            b.Box(V(-42f, stockY, -6f), V(44f, 28f, 18f), Quaternion.identity, 0.06f);
            b.Box(V(42f, stockY, -6f), V(44f, 28f, 18f), Quaternion.identity, 0.06f);
            b.Pen(gold, 0.94f);
            b.Arc(V(0, stockY, -16f), 20f, 28f, 0f, 360f, 20, 0.07f, V(0, stockY, -16f));
            b.Pieces += 2;

            // 4. Gothic Upper Crossbeam & Executioner's Cross Crest at the Crown
            b.Plain();
            b.Pen(darkIron, 0.85f);
            b.Box(V(0, beamY, -6f), V(192f, 24f, 26f), Quaternion.identity, 0.12f);
            b.Pen(gold, 0.95f);
            b.Box(V(0, beamY + 14f, -6f), V(198f, 7f, 28f), Quaternion.identity, 0.13f);
            // Ornate Crimson & Gold Executioner's Cross rising above the crossbeam
            float crestY = beamY + 52f;
            b.Pen(gold, 0.95f);
            b.Box(V(0, crestY, -6f), V(18f, 68f, 12f), Quaternion.identity, 0.16f);
            b.Box(V(0, crestY + 6f, -6f), V(58f, 16f, 12f), Quaternion.identity, 0.16f);
            GemBall(b, V(0, crestY + 6f, -13f), 8.5f, crimson, 0.95f, 0.85f, 0.18f);
            b.Pieces += 4;

            // 5. SUSPENDED ANGLED EXECUTIONER'S DROP-BLADE (45-degree oblique bevel with crimson blood-edge!)
            float bladeTopY = beamY - 58f;
            float chainTopY = beamY - 12f;
            // Heavy iron hoist chain in the center
            b.Plain();
            b.Pen(steel, 0.9f);
            b.Tube(new[] { V(0, chainTopY, -8f), V(0, bladeTopY + 8f, -8f) }, 4.5f, 4.5f, 6, 0.14f, 0.18f);
            // Lead weight block atop the guillotine blade
            b.Pen(gold, 0.94f);
            b.Box(V(0, bladeTopY, -8f), V(108f, 16f, 14f), Quaternion.identity, 0.18f);
            // Oblique angled steel drop-blade
            var dropBlade = new List<Vector2>
            {
                new Vector2(-58f, 0f), new Vector2(58f, 0f),
                new Vector2(58f, -54f), new Vector2(-58f, -118f)
            };
            b.Plain();
            b.Pen(steel, 0.96f);
            b.Plate(dropBlade, Lean(V(0, bladeTopY - 6f, -8f), 0f), 8f, 0.20f, Vector2.zero, new Vector2(0, -45f), darkIron);
            // Glowing crimson bevel along the oblique cutting edge
            b.Alive(0.45f, 0f, false, 0.6f, true);
            b.Pen(crimson, 0.95f, 0.85f);
            b.Line(V(-58f, bladeTopY - 124f, -12.5f), V(58f, bladeTopY - 60f, -12.5f), 4.5f, 0.22f, 0.26f);
            b.Plain();
            b.Pieces += 3;

            // 6. 10-TOOTHED "ROLLING CUTTER" SAW-CHAKRAM WHEEL (mounted on the left post)
            var wheelAt = V(-postX, midY - 18f, -18f);
            b.Plain();
            b.Pen(steel, 0.95f);
            b.Plate(GearOutline(10, 54f, 38f), Lean(wheelAt, 18f), 6f, 0.22f, Vector2.zero, Vector2.zero, crimson);
            for (int i = 0; i < 10; i++)
            {
                float a = (i * 36f + 18f) * Mathf.Deg2Rad;
                var d = V(Mathf.Sin(a), Mathf.Cos(a));
                var tang = V(d.y, -d.x);
                TipPart(b, wheelAt + d * 48f, (d + tang * 0.45f).normalized, 24f, 7f, i % 2 == 0 ? steel : crimson, 0.95f, i % 2 == 0 ? 0f : 0.4f, 0.24f);
            }
            b.Pen(gold, 0.95f);
            b.Ring(wheelAt + V(0, 0, -4f), 8f, 18f, 16, 0.24f, wheelAt);
            GemBall(b, wheelAt + V(0, 0, -5f), 7.5f, crimson, 0.95f, 0.8f, 0.25f);

            // 7. TWIN CHAINED "NEW POISON" GLASS VIALS (hanging from the right post)
            foreach (var (vy, dx, col) in new[] { (midY + 12f, 18f, venomGreen), (midY - 38f, -14f, venomPurple) })
            {
                var vAt = V(postX + dx, vy, -18f);
                b.Plain();
                b.Pen(gold, 0.9f);
                b.Line(V(postX, vy + 36f, -16f), vAt + V(0, 26f, 0), 2.2f, 0.22f, 0.25f);
                b.Alive(0.45f, 0f, false, 0.65f, true);
                b.Pen(col, 0.95f, 0.8f);
                b.Lathe(vAt, Vector3.up, new[] { new Vector2(0, 4f), new Vector2(8f, 13f), new Vector2(20f, 13f), new Vector2(26f, 5f), new Vector2(32f, 6.5f) }, 8, 0.24f, 0.28f);
                b.Plain();
                b.Pieces++;
            }
        }

        // ========================================================================================================
        // 4. SHADOW CROSS (JT_SHADOW_CROSS) — "ETERNAL SLASH" CRESCENT REAPER SCYTHE & ABYSSAL SHADOW-CROSS CREST
        // ========================================================================================================
        private static void BuildShadowCrossCrowScythe(TreeMeshBuilder b, TreeShape s)
        {
            float bot = s.Bottom, top = s.Top, midY = (bot + top) * 0.5f;
            var obsidian = C(34, 28, 52);
            var darkIndigo = C(48, 36, 82);
            var ravenViolet = C(118, 64, 208);
            var brightViolet = C(192, 112, 255);
            var silverSteel = C(222, 230, 248);
            var shadowCyan = C(96, 224, 255);
            var gold = C(240, 196, 92);
            var crimson = C(236, 38, 68);

            // 1. Base: Abyssal Shadow Pedestal with a Radial Ring of 8 "Dancing Knives" Daggers
            b.Plain();
            b.Pen(obsidian, 0.8f);
            b.Lathe(V(0, bot - 16f, 0), Vector3.up, new[] { new Vector2(0, 72f), new Vector2(14f, 64f), new Vector2(26f, 46f), new Vector2(26f, 0f) }, 12, 0f, 0.03f, false);
            for (int i = 0; i < 8; i++)
            {
                float a = (i * 45f + 22.5f) * Mathf.Deg2Rad;
                var dir = V(Mathf.Sin(a) * 0.85f, 0.35f, -Mathf.Cos(a) * 0.4f).normalized;
                TipPart(b, V(Mathf.Sin(a) * 48f, bot + 18f, -Mathf.Cos(a) * 24f), dir, 38f, 6.5f, i % 2 == 0 ? silverSteel : ravenViolet, 0.94f, 0f, 0.04f);
            }

            // 2. Towering Diagonal "Eternal Slash" Reaper Scythe Shaft & Wide Crescent Reaper Scythe Blade
            var haftBot = V(-58f, bot + 14f, -4f);
            var haftTop = V(38f, top - 18f, -6f);
            var haftDir = (haftTop - haftBot).normalized;
            b.Plain();
            b.Pen(obsidian, 0.88f);
            b.Tube(new[] { haftBot, haftTop }, 12.5f, 10.5f, 8, 0.04f, s.TrunkGrown);
            // Golden gothic rings along the scythe shaft
            for (int r = 1; r <= 4; r++)
            {
                var p = Vector3.Lerp(haftBot, haftTop, r / 5f);
                b.Pen(gold, 0.95f);
                b.Lathe(p - haftDir * 5f, haftDir, new[] { new Vector2(0, 12.5f), new Vector2(5f, 15.5f), new Vector2(10f, 12.5f) }, 8, 0.06f, 0.20f);
            }
            // Heavy Golden & Obsidian Scythe Socket Collar at the top of the shaft
            b.Pen(gold, 0.95f);
            b.Box(haftTop, V(34f, 26f, 22f), Quaternion.Euler(0, 0, -12f), 0.20f);
            TipPart(b, haftTop + V(8f, 12f, -6f), V(0.32f, 0.95f, 0f).normalized, 46f, 12f, gold, 0.95f, 0f, 0.21f);
            // Colossal Wide Crescent Reaper Scythe Blade arching 230mm overhead across the crown!
            var scytheBladeOutline = new List<Vector2>
            {
                new Vector2(24f, -14f),
                new Vector2(46f, 12f),
                new Vector2(8f, 48f),
                new Vector2(-56f, 68f),
                new Vector2(-128f, 58f),
                new Vector2(-188f, 24f),
                new Vector2(-224f, -26f), // Razor reaper tip!
                new Vector2(-176f, 4f),
                new Vector2(-118f, 24f),
                new Vector2(-52f, 26f),
                new Vector2(-4f, 6f)
            };
            b.Plain();
            b.Pen(silverSteel, 0.96f);
            b.Plate(scytheBladeOutline, Lean(haftTop + V(0, 0, -8f), 0f), 9f, 0.22f, new Vector2(0, 0), new Vector2(-80f, 36f), ravenViolet);
            // Glowing cyan-violet shadow edge along the inner crescent cutting curve
            var scytheEdge = new List<Vector3>
            {
                haftTop + V(-4f, 6f, -13f),
                haftTop + V(-52f, 26f, -13f),
                haftTop + V(-118f, 24f, -13f),
                haftTop + V(-176f, 4f, -13f),
                haftTop + V(-224f, -26f, -13f)
            };
            b.Alive(0.45f, 0f, false, 0.65f, true);
            b.Pen(shadowCyan, 0.95f, 0.85f);
            b.Tube(scytheEdge, u => 4.2f * (1f - 0.55f * u), 6, 0.24f, s.TrunkGrown + 0.07f);
            b.Plain();
            GemBall(b, haftTop + V(0, 0, -14f), 12.5f, crimson, 0.95f, 0.85f, 0.24f);
            b.Pieces += 5;

            // 3. CENTRAL "SHADOW CROSS" ECLIPSE CREST & CROSSED SHADOW BLADES (No bird!)
            var centerAt = V(-10f, midY + 8f, -14f);
            // Twin diagonal Cross-Slash Shadow Blades forming an 'X' across the scythe shaft
            foreach (int sg in new[] { -1, 1 })
            {
                float ang = sg * 48f * Mathf.Deg2Rad;
                var d = V(Mathf.Sin(ang), Mathf.Cos(ang), 0f).normalized;
                TipPart(b, centerAt, d, 96f, 14f, silverSteel, 0.96f, 0f, 0.14f);
                TipPart(b, centerAt, -d, 96f, 14f, darkIndigo, 0.92f, 0.35f, 0.14f);
            }
            // Ornate Golden & Crimson 4-Pointed Shadow Cross Star locking the center
            StarPlatePlain(b, centerAt + V(0, 0, -3f), 4, 54f, 18f, 0f, gold, 0.96f, 0.18f);
            StarPlatePlain(b, centerAt + V(0, 0, -5f), 4, 36f, 13f, 45f, ravenViolet, 0.92f, 0.19f);
            GemBall(b, centerAt + V(0, 0, -8f), 13.5f, crimson, 0.96f, 0.9f, 0.20f);
            // Glowing Cyan-Violet Eclipse Ring around the Shadow Cross crest
            b.Alive(0.42f, 0f, false, 0.6f, true);
            b.Pen(shadowCyan, 0.95f, 0.85f);
            b.Arc(centerAt + V(0, 0, 1f), 58f, 64f, 0f, 360f, 28, 0.16f, centerAt);
            b.Plain();
            b.Pieces += 6;
        }

        // ========================================================================================================
        // 5. SWORDMAN (JT_SWORDMAN) — TWIN FULL-LENGTH X-CROSSED IRON BROADSWORD & OAK/STEEL BUCKLER SHIELD
        // ========================================================================================================
        private static void BuildSwordmanCrossedSwords(TreeMeshBuilder b, TreeShape s)
        {
            float bot = s.Bottom, top = s.Top, midY = (bot + top) * 0.5f;
            var steel = C(212, 220, 236);
            var darkSteel = C(112, 122, 142);
            var ironRim = C(84, 92, 108);
            var gold = C(236, 188, 78);
            var oak = C(148, 94, 52);
            var darkOak = C(104, 64, 34);
            var crimson = C(186, 32, 34);

            // 1. Base: Forged Iron & Stone Arena Plinth
            b.Plain();
            b.Pen(ironRim, 0.65f);
            b.Lathe(V(0, bot - 18f, 0), Vector3.up, new[] { new Vector2(0, 76f), new Vector2(12f, 70f), new Vector2(20f, 52f), new Vector2(20f, 0f) }, 12, 0f, 0.03f, false);
            b.Pen(gold, 0.9f);
            b.Lathe(V(0, bot - 8f, 0), Vector3.up, new[] { new Vector2(0, 71f), new Vector2(2.5f, 73.5f), new Vector2(5f, 71f) }, 12, 0.02f, 0.03f);

            // 2. TWIN FULL-LENGTH IRON BROADSWORD CROSSED IN A BOLD 'X'
            // Hilts & pommels rise high at upper-left & upper-right; tapered blade tips emerge below bottom-left & bottom-right!
            float swordLen = Mathf.Max(520f, (top - bot) * 0.78f);
            foreach (int sg in new[] { -1, 1 })
            {
                float ang = sg * 32f * Mathf.Deg2Rad;
                var bladeDir = V(-Mathf.Sin(ang), -Mathf.Cos(ang), 0f).normalized; // pointing down-across from upper hilt to lower tip
                var perp = V(bladeDir.y, -bladeDir.x, 0f);
                float zOff = sg < 0 ? -4f : -8f;
                var center = V(0, midY + 8f, zOff);
                var guardAt = center - bladeDir * (swordLen * 0.44f);
                var gripTop = guardAt - bladeDir * 68f;
                var pommelAt = guardAt - bladeDir * 82f;

                // Full-length diamond-section broadsword blade (with central fuller line)
                b.Plain();
                b.Pen(steel, 0.94f);
                b.Blade(
                    guardAt, bladeDir, perp, swordLen,
                    u => u < 0.78f ? Mathf.Lerp(24f, 20f, u / 0.78f) : 20f * (1f - (u - 0.78f) / 0.22f),
                    u => 7.5f * (1f - 0.75f * u),
                    14, 0.04f, s.TrunkGrown, darkSteel
                );
                // Fuller groove along the blade
                b.Pen(ironRim, 0.85f);
                b.Line(guardAt + bladeDir * 24f + V(0, 0, -5f), guardAt + bladeDir * (swordLen * 0.72f) + V(0, 0, -4f), 3.6f, 0.06f, 0.28f);

                // Wide Flared Golden Crossguard
                var rot = Quaternion.FromToRotation(Vector3.up, -bladeDir);
                b.Pen(gold, 0.94f);
                b.Box(guardAt, V(96f, 16f, 18f), rot, 0.08f);
                foreach (int side in new[] { -1, 1 })
                    GemBall(b, guardAt + perp * (side * 50f), 9.5f, gold, 0.95f, 0f, 0.09f);
                GemBall(b, guardAt + V(0, 0, -10f), 7.5f, crimson, 0.92f, 0.4f, 0.09f);

                // Crimson Leather-Wrapped Grip & Round Golden Pommel
                b.Plain();
                b.Pen(crimson, 0.35f);
                b.Lathe(guardAt - bladeDir * 8f, -bladeDir, new[] { new Vector2(0, 10f), new Vector2(30f, 12f), new Vector2(60f, 10f) }, 8, 0.09f, 0.12f);
                b.Pen(gold, 0.95f);
                b.Ball(pommelAt, 16f, 10, 6, 0.12f);
                b.Pieces += 5;
            }

            // 3. CENTRAL ROUND OAK & RIVETED-STEEL BUCKLER SHIELD (mounted over the X-intersection)
            var shAt = V(0, midY - 6f, -16f);
            float shR = 88f;
            // Thick oak-plank shield body
            b.Plain();
            b.Pen(oak, 0.35f);
            b.Lathe(shAt + V(0, 0, 6f), Vector3.back, new[] { new Vector2(0, 0f), new Vector2(0, shR - 6f), new Vector2(10f, shR - 6f), new Vector2(12f, 0f) }, 24, 0.12f, 0.16f, false);
            // Vertical dark-oak plank seams across the shield face
            b.Pen(darkOak, 0.25f);
            foreach (float px in new[] { -44f, 0f, 44f })
            {
                float py = Mathf.Sqrt(Mathf.Max(0f, (shR - 12f) * (shR - 12f) - px * px));
                b.Line(shAt + V(px, -py, -7f), shAt + V(px, py, -7f), 2.4f, 0.15f, 0.18f);
            }
            // Heavy Riveted Steel Outer Shield Rim & Golden Inner Ring
            b.Pen(steel, 0.92f);
            b.Arc(shAt + V(0, 0, -7.5f), shR - 14f, shR + 2f, 0f, 360f, 28, 0.16f, shAt);
            b.Pen(gold, 0.94f);
            b.Arc(shAt + V(0, 0, -8f), shR - 18f, shR - 13f, 0f, 360f, 28, 0.17f, shAt);
            // 12 Golden Perimeter Rivets around the steel rim
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f * Mathf.Deg2Rad;
                GemBall(b, shAt + V(Mathf.Sin(a) * (shR - 6f), Mathf.Cos(a) * (shR - 6f), -9f), 4.8f, gold, 0.95f, 0f, 0.18f);
            }
            // Raised Golden Cross-Braces (+) & Central Hemispherical Steel Shield Boss Dome
            b.Plain();
            b.Pen(gold, 0.94f);
            b.Box(shAt + V(0, 0, -8f), V((shR - 14f) * 2f, 12f, 5f), Quaternion.identity, 0.18f);
            b.Box(shAt + V(0, 0, -8f), V(12f, (shR - 14f) * 2f, 5f), Quaternion.identity, 0.18f);
            b.Pen(steel, 0.96f);
            b.Ball(shAt + V(0, 0, -10f), 28f, 14, 8, 0.20f, 0.65f);
            GemBall(b, shAt + V(0, 0, -28f), 8.5f, gold, 0.95f, 0.35f, 0.22f);

            // 4. Twin Crimson & Gold Swordman War-Banners hanging beneath the buckler
            foreach (int sg in new[] { -1, 1 })
            {
                b.Plain();
                b.Pen(crimson, 0.25f);
                b.Plate(BannerOutline(32f, 96f, 18f), Lean(V(sg * 42f, midY - shR + 8f, -12f), sg * 8f), 2.4f, 0.22f, Vector2.zero, new Vector2(0, -32f), gold);
                b.Pieces++;
            }
        }

        // ========================================================================================================
        // 6. MAGE (JT_MAGICIAN) — OPEN GRIMOIRE AT BASE + SPIRAL ELDERWOOD WAND + 3 ELEMENTAL BOLT CRYSTALS
        // ========================================================================================================
        private static void BuildMageGrimoireWand(TreeMeshBuilder b, TreeShape s)
        {
            float bot = s.Bottom, top = s.Top, midY = (bot + top) * 0.5f;
            var leather = C(118, 44, 38);
            var parchment = C(244, 234, 208);
            var elderwood = C(118, 78, 46);
            var darkWood = C(82, 52, 30);
            var gold = C(242, 196, 84);
            var manaBlue = C(76, 178, 255);
            var fireRuby = C(255, 82, 38);
            var coldCyan = C(92, 234, 255);
            var lightningGold = C(255, 224, 68);

            // 1. OPEN LEATHER-BOUND ARCANE GRIMOIRE AT THE BASE
            var bookAt = V(0, bot + 36f, -10f);
            b.Plain();
            b.Pen(gold, 0.92f);
            b.Lathe(V(0, bot - 16f, 0), Vector3.up, new[] { new Vector2(0, 68f), new Vector2(12f, 58f), new Vector2(28f, 32f) }, 10, 0f, 0.03f, false);
            // Book spine
            b.Pen(leather, 0.55f);
            b.Box(bookAt + V(0, -6f, 2f), V(26f, 22f, 78f), Quaternion.Euler(-18f, 0, 0), 0.03f);
            // Left & Right Open Angled Leather Book Covers & Thick Cream Parchment Page Blocks
            foreach (int sg in new[] { -1, 1 })
            {
                var coverRot = Quaternion.Euler(-18f, 0f, -sg * 22f);
                var coverMid = bookAt + V(sg * 52f, 12f, -2f);
                b.Plain();
                b.Pen(leather, 0.55f);
                b.Box(coverMid, V(96f, 8f, 76f), coverRot, 0.04f);
                b.Pen(gold, 0.94f);
                b.Box(coverMid + coverRot * V(sg * 44f, 0f, 0f), V(10f, 10f, 78f), coverRot, 0.05f);
                // Thick cream parchment page block
                var pageMid = coverMid + coverRot * V(-sg * 4f, 9f, -2f);
                b.Pen(parchment, 0.2f);
                b.Box(pageMid, V(84f, 12f, 68f), coverRot, 0.05f);
                // Glowing arcane rune circle on each open page
                b.Alive(0.45f, 0f, false, 0.5f, true);
                b.Pen(sg < 0 ? fireRuby : coldCyan, 0.9f, 0.8f);
                b.Ring(pageMid + V(0, 8f, -12f), 12f, 20f, 14, 0.06f, pageMid);
                b.Plain();
                b.Pieces += 3;
            }

            // 2. SPIRAL ELDERWOOD ARC WAND RISING FROM THE OPEN GRIMOIRE
            float wandBot = bot + 48f, wandTop = top - 56f;
            TwistHelix(b, s, wandBot, wandTop, 16f, 11f, 14f, 220f, elderwood, darkWood, 0.35f);
            // Golden mana-ribbon spiraling around the elderwood shaft
            TwistHelix(b, s, wandBot + 20f, wandTop - 10f, 5.5f, 4.0f, 19f, 220f, gold, manaBlue, 0.92f);

            // Bifurcated Wooden Dragon-Claw Head cradling the Levitating Sapphire Mana Orb
            foreach (int sg in new[] { -1, 1 })
            {
                var c0 = V(sg * 8f, wandTop - 8f, -2f);
                var c1 = V(sg * 52f, wandTop + 28f, -4f);
                var c2 = V(sg * 26f, wandTop + 88f, -4f);
                b.Plain();
                b.Pen(elderwood, 0.4f);
                b.Tube(TreeMeshBuilder.Curve(c0, c1, c2, 9), 10f, 3.2f, 6, s.TrunkGrown - 0.04f, s.TrunkGrown + 0.02f);
                TipPart(b, c2, V(-sg * 0.6f, 0.8f, 0f).normalized, 20f, 5.5f, gold, 0.94f, 0f, s.TrunkGrown + 0.02f);
                b.Pieces++;
            }

            // Levitating Multifaceted Sapphire Mana Orb & Interlocking Golden Astrological Rings
            var orbPos = V(0, wandTop + 44f, -6f);
            GemBall(b, orbPos, 28f, manaBlue, 0.96f, 0.85f, s.TrunkGrown + 0.03f);
            HoopPart(b, orbPos, 68f, 40f, 16f, 2.8f, true, gold, 0.94f, 0.45f, s.TrunkGrown + 0.04f);
            HoopPart(b, orbPos, 54f, 32f, -22f, 2.4f, true, coldCyan, 0.94f, 0.75f, s.TrunkGrown + 0.05f);

            // 3. THREE ORBITING ELEMENTAL BOLT CRYSTALS (Fire Bolt, Cold Bolt, Lightning Bolt!)
            // Fire Bolt (Left: blazing ruby-orange crystal cluster)
            var fireAt = V(-88f, midY + 18f, -14f);
            CrystalCluster(b, fireAt, -18f, 58f, 13f, fireRuby, C(255, 196, 68), 0.16f);
            // Cold Bolt (Right: icy cyan-blue crystal cluster)
            var coldAt = V(88f, midY + 18f, -14f);
            CrystalCluster(b, coldAt, 18f, 58f, 13f, coldCyan, C(186, 246, 255), 0.18f);
            // Lightning Bolt (Crown Apex: golden-yellow thunder crystal above the mana orb)
            var lightAt = V(0f, wandTop + 98f, -8f);
            CrystalCluster(b, lightAt, 0f, 64f, 14f, lightningGold, gold, s.TrunkGrown + 0.06f);
            // Near half of the elemental orbit ring linking the bolts
            HoopPart(b, V(0, midY + 10f, 0), 82f, 46f, -14f, 2.4f, true, coldCyan, 0.9f, 0.65f, 0.14f);
        }

        // ========================================================================================================
        // 7. ARCHER (JT_ARCHER) — CROSSED LAMINATED YEW RECURVE LONGBOW, PACKED QUIVER & TWIN DOUBLE-STRAFE ARROWS
        // ========================================================================================================
        private static void BuildArcherRecurveQuiver(TreeMeshBuilder b, TreeShape s)
        {
            float bot = s.Bottom, top = s.Top, midY = (bot + top) * 0.5f;
            var yewWood = C(196, 142, 78);
            var darkYew = C(132, 86, 42);
            var forestGreen = C(46, 128, 68);
            var brightGreen = C(98, 214, 112);
            var leather = C(138, 78, 42);
            var featherWhite = C(244, 240, 228);
            var gold = C(236, 190, 82);
            var steel = C(214, 222, 238);

            // 1. Base: Woodland Moss & Carved Timber Plinth
            b.Plain();
            b.Pen(darkYew, 0.4f);
            b.Lathe(V(0, bot - 16f, 0), Vector3.up, new[] { new Vector2(0, 72f), new Vector2(14f, 66f), new Vector2(22f, 48f), new Vector2(22f, 0f) }, 10, 0f, 0.03f, false);
            b.Pen(forestGreen, 0.35f);
            b.Lathe(V(0, bot - 4f, 0), Vector3.up, new[] { new Vector2(0, 66f), new Vector2(6f, 62f), new Vector2(6f, 0f) }, 10, 0.02f, 0.03f);

            // 2. CYLINDRICAL FOREST-GREEN & LEATHER ARROW QUIVER (angled -28 deg across the lower-center)
            var qRot = Quaternion.Euler(0f, 0f, -28f);
            var qAxis = qRot * Vector3.up;
            var qPerp = qRot * Vector3.right;
            var qBot = V(-46f, midY - 125f, -4f);
            float qLen = 215f;
            b.Plain();
            b.Pen(leather, 0.38f);
            b.Lathe(qBot, qAxis, new[] { new Vector2(0, 2f), new Vector2(4f, 22f), new Vector2(qLen * 0.55f, 25f), new Vector2(qLen, 28f) }, 12, 0.05f, 0.12f);
            // Forest-green leather collar & golden strap bands on the quiver
            b.Pen(forestGreen, 0.45f);
            b.Lathe(qBot + qAxis * (qLen * 0.68f), qAxis, new[] { new Vector2(0, 26.5f), new Vector2(42f, 28.5f), new Vector2(44f, 26.5f) }, 12, 0.09f, 0.12f);
            foreach (float f in new[] { 0.18f, 0.66f, 0.88f })
            {
                b.Pen(gold, 0.92f);
                b.Lathe(qBot + qAxis * (qLen * f), qAxis, new[] { new Vector2(0, 24f + f * 4f), new Vector2(3f, 27.5f + f * 4f), new Vector2(7f, 24f + f * 4f) }, 12, 0.10f, 0.12f);
            }
            // 6 Fletched Arrows fanning out of the quiver mouth!
            var qMouth = qBot + qAxis * qLen;
            for (int a = 0; a < 6; a++)
            {
                float spread = (a - 2.5f) * 6.5f;
                var aDir = (qAxis + qPerp * ((a - 2.5f) * 0.09f)).normalized;
                var aStart = qMouth + qPerp * spread + V(0, 0, (a % 2) * 3f - 1.5f);
                var aEnd = aStart + aDir * (74f + (a % 3) * 12f);
                b.Plain();
                b.Pen(yewWood, 0.35f);
                b.Tube(new[] { aStart - aDir * 24f, aEnd }, 2.2f, 2.2f, 5, 0.12f, 0.16f);
                float fAng = Mathf.Atan2(aDir.x, aDir.y) * Mathf.Rad2Deg;
                FeatherPlate(b, aEnd - aDir * 22f, fAng + 18f, 24f, 8.5f, featherWhite, a % 2 == 0 ? brightGreen : gold, 0.16f);
                FeatherPlate(b, aEnd - aDir * 22f, fAng - 18f, 24f, 8.5f, a % 2 == 0 ? brightGreen : featherWhite, gold, 0.16f);
            }
            b.Pieces += 5;

            // 3. SWEEPING LAMINATED YEW & HORN S-CURVED RECURVE LONGBOW (bold arched recurve silhouette!)
            var bowRot = Quaternion.Euler(0f, 0f, 12f);
            float bowH = (top - bot) * 0.92f;
            var bowMid = V(14f, midY + 8f, -12f);
            var bowPts = new List<Vector3>(25);
            for (int i = 0; i <= 24; i++)
            {
                float u = i / 24f; // 0 to 1
                float sSym = (u - 0.5f) * 2f; // -1 to +1
                // Deep C-curved recurve belly (+92mm right) with reflexed syah tips flicking outward at top & bottom!
                float arch = 92f * (1f - sSym * sSym);
                float recurveFlick = 28f * Mathf.Pow(Mathf.Abs(sSym), 3.2f);
                float xLocal = arch + recurveFlick - 24f;
                float yLocal = sSym * (bowH * 0.5f);
                bowPts.Add(bowMid + bowRot * V(xLocal, yLocal, 0f));
            }
            b.Plain();
            b.Pen(yewWood, 0.48f);
            b.Tube(bowPts, u =>
            {
                float d = Mathf.Abs(u - 0.5f) * 2f;
                return Mathf.Lerp(16.5f, 6.0f, d * d) + (d < 0.22f ? 3.5f : 0f);
            }, 9, 0.04f, s.TrunkGrown, true, true);
            // Forest-green leather riser grip & golden limb ferrules on the recurve bow
            for (int k = 0; k < 5; k++)
            {
                float u = k == 0 ? 0.5f : k == 1 ? 0.34f : k == 2 ? 0.66f : k == 3 ? 0.12f : 0.88f;
                int idx = Mathf.Clamp(Mathf.RoundToInt(u * 24f), 1, 23);
                var bAt = bowPts[idx];
                var bTan = (bowPts[idx + 1] - bowPts[idx - 1]).normalized;
                float r = k == 0 ? 19.5f : k <= 2 ? 15f : 9.5f;
                float len = k == 0 ? 58f : 15f;
                b.Pen(k == 0 ? forestGreen : gold, k == 0 ? 0.45f : 0.94f);
                b.Lathe(bAt - bTan * (len * 0.5f), bTan, new[] { new Vector2(0, r * 0.9f), new Vector2(2f, r), new Vector2(len - 2f, r), new Vector2(len, r * 0.9f) }, 9, 0.12f, 0.16f);
            }
            // Taut Silver-Hemp Bowstring drawn back to the nocked arrows!
            var nockPoint = bowMid + bowRot * V(-54f, 0f, -2f);
            b.Plain();
            b.Pen(featherWhite, 0.55f);
            b.Line(bowPts[0], nockPoint, 2.6f, 0.14f, s.TrunkGrown);
            b.Line(bowPts[24], nockPoint, 2.6f, 0.14f, s.TrunkGrown);

            // 4. TWIN NOCKED "DOUBLE STRAFE" ARROWS (drawn across the bow riser with steel broadheads!)
            foreach (int sg in new[] { -1, 1 })
            {
                var aStart = nockPoint + V(0, sg * 12f, -4f);
                var aDir = (bowRot * V(1f, sg * 0.05f, 0f)).normalized;
                var aEnd = aStart + aDir * 205f;
                b.Plain();
                b.Pen(gold, 0.92f);
                b.Tube(new[] { aStart, aEnd }, 3.2f, 3.2f, 6, 0.18f, 0.24f);
                // Diamond-section steel broadhead arrow tip
                TipPart(b, aEnd, aDir, 38f, 11f, steel, 0.96f, 0f, 0.24f);
                // Crisp white & emerald feather fletchings at the nock
                float fAng = Mathf.Atan2(aDir.x, aDir.y) * Mathf.Rad2Deg;
                FeatherPlate(b, aStart + aDir * 24f, fAng + 162f, 32f, 10f, featherWhite, brightGreen, 0.22f);
                FeatherPlate(b, aStart + aDir * 24f, fAng - 162f, 32f, 10f, brightGreen, gold, 0.22f);
            }
            b.Pieces += 6;
        }

        // ========================================================================================================
        // 8. ACOLYTE (JT_ACOLYTE) — GOLDEN "AQUA BENEDICTA" CHALICE, WINGED SANCTUARY CROSS & DRAPED ROSARY
        // ========================================================================================================
        private static void BuildAcolyteChaliceCross(TreeMeshBuilder b, TreeShape s)
        {
            float bot = s.Bottom, top = s.Top, span = top - bot;
            var marble = C(246, 242, 234);
            var gold = C(242, 196, 82);
            var darkGold = C(188, 142, 48);
            var holyWater = C(78, 218, 255);
            var sapphire = C(54, 136, 246);
            var wingWhite = C(250, 248, 242);
            var wingCream = C(236, 224, 198);
            var ruby = C(224, 42, 68);

            // 1. GOLDEN "AQUA BENEDICTA" HOLY WATER CHALICE AT THE BASE
            float chaliceHeight = Mathf.Min(165f, span * 0.26f);
            float cupRimY = bot + chaliceHeight;
            b.Plain();
            // White marble & gold altar foot
            b.Pen(marble, 0.65f);
            b.Lathe(V(0, bot - 18f, 0), Vector3.up, new[] { new Vector2(0, 76f), new Vector2(12f, 70f), new Vector2(18f, 52f), new Vector2(18f, 0f) }, 14, 0f, 0.03f, false);
            // Ornate golden chalice base, jeweled knop stem, and wide hemispherical chalice cup
            b.Pen(gold, 0.94f);
            b.Lathe(V(0, bot, 0), Vector3.up, new[]
            {
                new Vector2(0f, 52f),
                new Vector2(10f, 46f),
                new Vector2(22f, 18f),
                new Vector2(chaliceHeight * 0.36f, 14f),
                new Vector2(chaliceHeight * 0.46f, 26f), // jeweled knop
                new Vector2(chaliceHeight * 0.56f, 14f),
                new Vector2(chaliceHeight * 0.66f, 38f), // lower cup bowl
                new Vector2(chaliceHeight * 0.88f, 56f), // wide chalice cup
                new Vector2(chaliceHeight, 62f),         // flared golden lip
                new Vector2(chaliceHeight, 54f)
            }, 16, 0.02f, 0.10f);
            // 6 Ruby cabochons around the chalice stem knop
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                GemBall(b, V(Mathf.Sin(a) * 26f, bot + chaliceHeight * 0.46f, -Mathf.Cos(a) * 26f), 4.8f, ruby, 0.95f, 0.55f, 0.07f);
            }
            // Glowing Cyan-Azure "Aqua Benedicta" Holy Water brimming at the chalice rim!
            b.Alive(0.45f, 0f, false, 0.6f, true);
            b.Pen(holyWater, 0.96f, 0.85f);
            b.Lathe(V(0, cupRimY - 6f, 0), Vector3.up, new[] { new Vector2(0, 54f), new Vector2(6f, 50f), new Vector2(10f, 0f) }, 16, 0.09f, 0.11f);
            b.Plain();
            b.Pieces += 4;

            // 2. RADIANT WINGED GOLDEN SANCTUARY CROSS (rising cleanly ABOVE the chalice!)
            float crossBot = cupRimY + 18f;
            float crossTop = top + 48f;
            float crossY = Mathf.Lerp(crossBot, crossTop, 0.64f);
            float armSpan = 108f; // 216mm total horizontal crossbar width!

            // Vertical & Horizontal Golden Sanctuary Cross Beams with White-Marble Enamel Inlay
            b.Plain();
            b.Pen(gold, 0.95f);
            b.Box(V(0, (crossBot + crossTop) * 0.5f, -6f), V(26f, crossTop - crossBot, 16f), Quaternion.identity, 0.12f);
            b.Box(V(0, crossY, -6f), V(armSpan * 2f, 26f, 16f), Quaternion.identity, 0.14f);
            // White-ivory sacred inlay strips along the front face of the cross
            b.Pen(marble, 0.75f);
            b.Box(V(0, (crossBot + crossTop) * 0.5f, -15f), V(12f, crossTop - crossBot - 24f, 4f), Quaternion.identity, 0.15f);
            b.Box(V(0, crossY, -15f), V(armSpan * 2f - 24f, 12f, 4f), Quaternion.identity, 0.15f);

            // Flared Gothic Trefoil Finials at the top, left, and right cross terminals
            TipPart(b, V(0, crossTop - 4f, -6f), Vector3.up, 42f, 18f, gold, 0.95f, 0f, 0.16f);
            GemBall(b, V(0, crossTop + 4f, -6f), 12f, gold, 0.95f, 0f, 0.16f);
            foreach (int sg in new[] { -1, 1 })
            {
                TipPart(b, V(sg * (armSpan - 4f), crossY, -6f), V(sg, 0, 0), 38f, 16f, gold, 0.95f, 0f, 0.16f);
                GemBall(b, V(sg * (armSpan + 2f), crossY, -6f), 11f, gold, 0.95f, 0f, 0.16f);
            }
            // Central Sapphire Blessing Gem & Golden Sunburst Halo at the Cross Intersection
            StarPlatePlain(b, V(0, crossY, -17f), 8, 36f, 15f, 22.5f, gold, 0.96f, 0.17f);
            GemBall(b, V(0, crossY, -20f), 15f, sapphire, 0.96f, 0.85f, 0.18f);

            // 3. TWIN WIDE UNFURLED WHITE "BLESSING" ANGEL WINGS (framing the Sanctuary Cross!)
            foreach (int sg in new[] { -1, 1 })
            {
                var w0 = V(sg * 22f, crossY - 8f, -2f);
                var w1 = V(sg * 82f, crossY + 38f, -4f);
                var w2 = V(sg * 138f, crossY + 78f, -2f);
                b.Plain();
                b.Pen(gold, 0.92f);
                b.Tube(TreeMeshBuilder.Curve(w0, w1, w2, 8), 7f, 2.8f, 6, 0.15f, 0.20f);
                for (int f = 0; f < 6; f++)
                {
                    float u = f / 5f;
                    var fAt = Vector3.Lerp(w0, w2, u);
                    float lean = sg * (56f + f * 11f);
                    float len = Mathf.Lerp(74f, 108f, Mathf.Sin(u * Mathf.PI * 0.85f + 0.2f));
                    FeatherPlate(b, fAt, lean, len, 20f, f % 2 == 0 ? wingWhite : wingCream, gold, 0.16f + f * 0.01f);
                }
            }

            // 4. DRAPED PEARL & GOLD PRAYER ROSARY BEAD CHAIN
            int beads = 15;
            for (int i = 0; i < beads; i++)
            {
                float u = i / (float)(beads - 1); // 0 to 1 from left arm across lower shaft to right arm
                float rx = Mathf.Lerp(-armSpan * 0.78f, armSpan * 0.78f, u);
                float ry = crossY - 6f - 118f * Mathf.Sin(u * Mathf.PI);
                float rz = -18f - 6f * Mathf.Sin(u * Mathf.PI);
                GemBall(b, V(rx, ry, rz), i % 3 == 0 ? 6.2f : 4.6f, i % 3 == 0 ? gold : marble, 0.92f, 0.2f, 0.20f + u * 0.04f);
            }
            b.Pieces += 8;
        }

        // ========================================================================================================
        // 9. MERCHANT (JT_MERCHANT) — WOODEN RAGNAROK PUSHCART, 8-SPOKED WHEEL, ZENY MOUND & MAMMONITE BATTLE-AXE
        // ========================================================================================================
        private static void BuildMerchantPushcartZeny(TreeMeshBuilder b, TreeShape s)
        {
            float bot = s.Bottom, top = s.Top, span = top - bot;
            var oak = C(164, 108, 56);
            var darkOak = C(112, 70, 34);
            var iron = C(88, 94, 108);
            var steel = C(212, 220, 236);
            var gold = C(246, 198, 72);
            var coinGold = C(255, 214, 78);
            var sackBrown = C(142, 92, 52);
            var crimson = C(188, 36, 38);

            // 1. WOODEN RAGNAROK MERCHANT PUSHCART AT THE BASE
            float cartY = bot + 62f;
            b.Plain();
            // Cobblestone road base
            b.Pen(iron, 0.45f);
            b.Box(V(0, bot - 8f, 0), V(184f, 16f, 76f), Quaternion.identity, 0f);
            // Oak-plank cart bed & sideboards
            b.Pen(oak, 0.38f);
            b.Box(V(0, cartY, -6f), V(168f, 68f, 68f), Quaternion.identity, 0.03f);
            // Dark oak horizontal plank rails & iron corner brackets
            b.Pen(darkOak, 0.35f);
            b.Box(V(0, cartY + 34f, -6f), V(176f, 10f, 74f), Quaternion.identity, 0.04f);
            b.Box(V(0, cartY - 30f, -6f), V(174f, 10f, 72f), Quaternion.identity, 0.04f);
            foreach (int sg in new[] { -1, 1 })
            {
                b.Pen(iron, 0.82f);
                b.Box(V(sg * 82f, cartY, -6f), V(10f, 72f, 72f), Quaternion.identity, 0.05f);
                // Forward-projecting wooden pushcart shafts
                b.Pen(darkOak, 0.4f);
                b.Tube(new[] { V(sg * 68f, cartY - 12f, -36f), V(sg * 76f, cartY - 22f, -72f) }, 5.5f, 4.5f, 6, 0.05f, 0.07f);
            }

            // 2. LARGE 8-SPOKED IRON-RIMMED WOODEN CART WHEEL (mounted prominently on the front of the cart!)
            var wheelPos = V(-38f, bot + 48f, -44f);
            float wR = 54f;
            b.Plain();
            b.Pen(iron, 0.88f);
            b.Arc(wheelPos, wR - 5f, wR + 2f, 0f, 360f, 24, 0.06f, wheelPos);
            b.Pen(oak, 0.45f);
            b.Arc(wheelPos + V(0, 0, -1f), wR - 14f, wR - 5f, 0f, 360f, 24, 0.06f, wheelPos);
            // 8 Radial Wooden Spokes
            for (int sp = 0; sp < 8; sp++)
            {
                float a = sp * 45f * Mathf.Deg2Rad;
                var d = V(Mathf.Sin(a), Mathf.Cos(a));
                b.Pen(darkOak, 0.4f);
                b.Tube(new[] { wheelPos, wheelPos + d * (wR - 8f) }, 3.6f, 3.0f, 5, 0.07f, 0.08f);
            }
            // Brass wheel axle hub
            b.Pen(gold, 0.94f);
            b.Lathe(wheelPos + V(0, 0, 4f), Vector3.back, new[] { new Vector2(0, 14f), new Vector2(12f, 12f), new Vector2(16f, 0f) }, 10, 0.08f, 0.09f);

            // 3. OVERFLOWING MOUND OF GLITTERING GOLDEN ZENY COINS & TIED COIN SACK IN THE CART BED
            var moundBase = V(-18f, cartY + 38f, -8f);
            b.Plain();
            b.Pen(gold, 0.94f);
            b.Ball(moundBase, 42f, 12, 6, 0.08f, 0.55f);
            for (int c = 0; c < 14; c++)
            {
                float a = c * 137.5f * Mathf.Deg2Rad;
                float r = 12f + (c % 4) * 11f;
                var cPos = moundBase + V(Mathf.Cos(a) * r * 1.3f, 10f + (c % 3) * 8f, -Mathf.Sin(a) * r * 0.6f - 6f);
                GemBall(b, cPos, 7.5f, coinGold, 0.96f, 0.35f, 0.09f + c * 0.003f);
            }
            // Plump Tied Brown-Leather Coin Sack on the right side of the cart bed
            var sackPos = V(44f, cartY + 52f, -12f);
            b.Plain();
            b.Pen(sackBrown, 0.3f);
            b.Ball(sackPos, 28f, 10, 6, 0.09f, 1.12f);
            b.Lathe(sackPos + V(0, 22f, 0), Vector3.up, new[] { new Vector2(0, 12f), new Vector2(8f, 8f), new Vector2(18f, 16f) }, 8, 0.10f, 0.11f);
            b.Pen(crimson, 0.5f);
            b.Lathe(sackPos + V(0, 24f, 0), Vector3.up, new[] { new Vector2(0, 11f), new Vector2(5f, 11f) }, 8, 0.10f, 0.11f);

            // 4. HEAVY DOUBLE-BITTED "MAMMONITE" BATTLE-AXE RISING FROM THE PUSHCART TO THE CROWN
            float axeBot = cartY + 24f;
            float axeTop = top + 12f;
            float headY = axeTop - 52f;
            b.Plain();
            b.Pen(darkOak, 0.45f);
            b.Tube(new[] { V(0, axeBot, -4f), V(0, axeTop, -4f) }, 14f, 11.5f, 9, 0.08f, s.TrunkGrown);
            // Golden Grip Rings along the Axe Haft
            for (float y = axeBot + 65f; y < headY - 30f; y += 95f)
            {
                b.Pen(gold, 0.94f);
                b.Lathe(V(0, y, -4f), Vector3.up, new[] { new Vector2(0, 14f), new Vector2(3f, 17f), new Vector2(12f, 17f), new Vector2(15f, 14f) }, 9, 0.12f, 0.16f);
            }
            // Central Golden Axe Socket & Twin Sweeping Crescent Steel Battle-Axe Blades!
            b.Plain();
            b.Pen(gold, 0.95f);
            b.Box(V(0, headY, -6f), V(38f, 54f, 24f), Quaternion.identity, 0.20f);
            GemBall(b, V(0, headY, -19f), 11f, crimson, 0.95f, 0.55f, 0.22f);
            var axeWing = new List<Vector2>
            {
                new Vector2(16f, 18f), new Vector2(58f, 44f), new Vector2(96f, 64f),
                new Vector2(108f, 24f), new Vector2(112f, -24f), new Vector2(96f, -64f),
                new Vector2(58f, -44f), new Vector2(16f, -18f)
            };
            foreach (int sg in new[] { -1, 1 })
            {
                b.Plain();
                b.Pen(steel, 0.96f);
                b.Plate(axeWing, TreeMeshBuilder.At(V(0, headY, -6f), 0f, 1f, 0f, sg), 10f, 0.22f, new Vector2(16f, 0f), new Vector2(68f, 0f), gold);
            }
            // Top Spear-Spike & Iconic Golden Zeny Coin Emblem at the Apex!
            TipPart(b, V(0, axeTop - 4f, -4f), Vector3.up, 44f, 12f, gold, 0.95f, 0f, s.TrunkGrown);
            var zenyAt = V(0, axeTop + 56f, -8f);
            b.Alive(0.35f, 0f, false, 0.4f, true);
            b.Pen(coinGold, 0.96f, 0.55f);
            b.Arc(zenyAt, 16f, 34f, 0f, 360f, 24, s.TrunkGrown + 0.04f, zenyAt);
            b.Pen(gold, 0.96f, 0.35f);
            b.Plate(RectOutline(22f, 22f), Lean(zenyAt + V(0, 0, -2f), 45f), 4f, s.TrunkGrown + 0.05f, Vector2.zero, Vector2.zero, darkOak);
            b.Plain();
            b.Pieces += 8;
        }

        // ========================================================================================================
        // 10. THIEF (JT_THIEF) — TWIN X-CROSSED DAMASCUS STILETTO DAGGERS & CORKED "ENVENOM" POISON VIAL
        // ========================================================================================================
        private static void BuildThiefCrossedStilettos(TreeMeshBuilder b, TreeShape s)
        {
            float bot = s.Bottom, top = s.Top, midY = (bot + top) * 0.5f;
            var darkCobble = C(48, 42, 58);
            var damascusDark = C(74, 80, 98);
            var damascusBright = C(212, 222, 238);
            var brass = C(228, 182, 78);
            var plumLeather = C(96, 42, 136);
            var envenomPurple = C(184, 68, 252);
            var venomEmerald = C(78, 238, 142);
            var corkTan = C(196, 152, 96);

            // 1. Base: Cobblestone Plinth & Flat Horizontal Bubbling Violet "Envenom" Poison Pool (ZERO wooden pole!)
            b.Plain();
            b.Pen(darkCobble, 0.6f);
            b.Lathe(V(0, bot - 16f, 0), Vector3.up, new[] { new Vector2(0, 72f), new Vector2(12f, 66f), new Vector2(20f, 48f), new Vector2(20f, 0f) }, 10, 0f, 0.03f, false);
            b.Alive(0.45f, 0f, false, 0.6f, true);
            b.Pen(envenomPurple, 0.92f, 0.8f);
            b.Lathe(V(0, bot + 4.2f, 0), Vector3.up, new[] { new Vector2(0f, 46f), new Vector2(2.5f, 42f), new Vector2(3.5f, 0f) }, 14, 0.03f, 0.04f);
            for (int p = 0; p < 5; p++)
            {
                float a = p * 72f * Mathf.Deg2Rad;
                GemBall(b, V(Mathf.Cos(a) * 24f, bot + 10f + (p % 2) * 8f, -Mathf.Sin(a) * 16f), 5.5f, p % 2 == 0 ? envenomPurple : venomEmerald, 0.95f, 0.85f, 0.04f);
            }
            b.Plain();

            // 2. TWIN LONG DAMASCUS STILETTO DAGGERS CROSSED IN A SHARP 'X' (Hilts at upper-left/right, blades crossing down-across!)
            float daggerLen = Mathf.Max(460f, (top - bot) * 0.74f);
            foreach (int sg in new[] { -1, 1 })
            {
                float ang = sg * 30f * Mathf.Deg2Rad;
                var dir = V(-Mathf.Sin(ang), -Mathf.Cos(ang), 0f).normalized; // Hilts at upper-left/right, tips at bottom-right/left!
                var perp = V(dir.y, -dir.x, 0f);
                float zOff = sg < 0 ? -5f : -10f;
                var crossCenter = V(0, midY + 6f, zOff);
                var guardPos = crossCenter - dir * (daggerLen * 0.42f);
                var gripTop = guardPos - dir * 74f;
                var pommelRing = guardPos - dir * 90f;

                // Ring Pommel & Ribbed Plum-Leather Stiletto Grip at the top
                b.Plain();
                b.Pen(brass, 0.94f);
                b.Arc(pommelRing, 10f, 16f, 0f, 360f, 16, 0.05f, pommelRing);
                GemBall(b, pommelRing, 6.5f, envenomPurple, 0.95f, 0.75f, 0.06f);
                b.Pen(plumLeather, 0.38f);
                b.Lathe(guardPos, -dir, new[] { new Vector2(0, 10f), new Vector2(18f, 12.5f), new Vector2(36f, 13.5f), new Vector2(54f, 12.5f), new Vector2(74f, 10f) }, 8, 0.06f, 0.09f);

                // Wide S-Curved Antique-Brass Quillon Guard
                var s0 = guardPos - perp * 54f - dir * 10f;
                var s1 = guardPos;
                var s2 = guardPos + perp * 54f + dir * 10f;
                b.Pen(brass, 0.94f);
                b.Tube(TreeMeshBuilder.Curve(s0, s1, s2, 9), 7.5f, 7.5f, 6, 0.09f, 0.11f, true, true);
                GemBall(b, s0, 8.5f, brass, 0.95f, 0f, 0.11f);
                GemBall(b, s2, 8.5f, brass, 0.95f, 0f, 0.11f);

                // Long Tapered Two-Tone Damascus Dagger Blade with Ricasso & Barb Notch
                b.Plain();
                b.Pen(damascusBright, 0.95f);
                b.Blade(
                    guardPos, dir, perp, daggerLen,
                    u =>
                    {
                        if (u < 0.14f) return Mathf.Lerp(22f, 28f, u / 0.14f);
                        if (u < 0.24f) return Mathf.Lerp(28f, 20f, (u - 0.14f) / 0.10f); // thief dagger barb notch
                        if (u < 0.30f) return Mathf.Lerp(20f, 27f, (u - 0.24f) / 0.06f);
                        return 27f * (1f - Mathf.Pow((u - 0.30f) / 0.70f, 0.85f));
                    },
                    u => 7.5f * (1f - 0.8f * u),
                    16, 0.10f, s.TrunkGrown, damascusDark
                );
                // Glowing Violet/Emerald Poison Groove along the lower half of the blade
                b.Alive(0.45f, 0f, false, 0.6f, true);
                b.Pen(sg < 0 ? envenomPurple : venomEmerald, 0.95f, 0.85f);
                b.Line(guardPos + dir * (daggerLen * 0.35f) + V(0, 0, -4f), guardPos + dir * (daggerLen * 0.92f) + V(0, 0, -2f), 3.2f, 0.16f, s.TrunkGrown);
                b.Plain();
                b.Pieces += 4;
            }

            // 3. CORKED GLASS "ENVENOM" POISON VIAL STRAPPED AT THE CENTRAL X-INTERSECTION
            var vialCenter = V(0, midY + 6f, -18f);
            var vialRot = Quaternion.Euler(0f, 0f, -14f);
            var vialAxis = vialRot * Vector3.up;
            // Criss-crossed dark leather straps binding the blades
            b.Plain();
            b.Pen(plumLeather, 0.4f);
            b.Box(vialCenter + V(0, 0, 6f), V(48f, 14f, 12f), Quaternion.Euler(0, 0, 30f), 0.14f);
            b.Box(vialCenter + V(0, 0, 6f), V(48f, 14f, 12f), Quaternion.Euler(0, 0, -30f), 0.14f);
            // Glowing Purple-Emerald Venom Liquid inside the lower bulb of the flask
            b.Alive(0.45f, 0f, false, 0.7f, true);
            b.Pen(envenomPurple, 0.95f, 0.85f);
            b.Ball(vialCenter - vialAxis * 10f, 24f, 12, 8, 0.16f, 0.95f);
            b.Pen(venomEmerald, 0.95f, 0.85f);
            b.Lathe(vialCenter - vialAxis * 4f, vialAxis, new[] { new Vector2(0f, 22f), new Vector2(4f, 21f), new Vector2(5f, 0f) }, 12, 0.17f, 0.18f);
            // Crystal Glass Flask Neck, Brass Collar & Cork Stopper
            b.Plain();
            b.Pen(Alpha(C(210, 235, 255), 185), 0.96f, 0.25f);
            b.Lathe(vialCenter - vialAxis * 34f, vialAxis, new[]
            {
                new Vector2(0f, 8f), new Vector2(14f, 27f), new Vector2(34f, 26f),
                new Vector2(48f, 11f), new Vector2(66f, 11f), new Vector2(72f, 15f)
            }, 12, 0.16f, 0.20f);
            b.Pen(brass, 0.94f);
            b.Lathe(vialCenter + vialAxis * 22f, vialAxis, new[] { new Vector2(0, 12.5f), new Vector2(6f, 12.5f) }, 10, 0.19f, 0.20f);
            b.Pen(corkTan, 0.25f);
            b.Lathe(vialCenter + vialAxis * 36f, vialAxis, new[] { new Vector2(0, 9.5f), new Vector2(16f, 12f), new Vector2(18f, 0f) }, 10, 0.20f, 0.22f);
            b.Pieces += 4;
        }

        // ========================================================================================================
        // 11. WINDHAWK (JT_WINDHAWK) — SOARING GOLDEN-AMBER HUNTING HAWK CLUTCHING RECURVE BOW & TWIN ARROWS
        // ========================================================================================================
        private static void BuildWindhawkRaptorBow(TreeMeshBuilder b, TreeShape s)
        {
            float bot = s.Bottom, top = s.Top, midY = (bot + top) * 0.5f;
            var bark = C(108, 72, 42);
            var moss = C(62, 168, 84);
            var hawkUmber = C(138, 86, 44);
            var hawkAmber = C(196, 134, 64);
            var hawkCream = C(246, 236, 212);
            var gold = C(244, 198, 78);
            var galeGreen = C(88, 238, 148);
            var steel = C(214, 224, 240);

            float hawkY = midY + 12f;

            // 1. Base: Mossy Forest Log Perch, Carved Timber Totem Column & Swirling Emerald Gale Vortex
            b.Plain();
            b.Pen(bark, 0.35f);
            b.Tube(new[] { V(-86f, bot + 14f, -4f), V(0f, bot + 22f, -8f), V(86f, bot + 10f, -4f) }, 22f, 18f, 9, 0f, 0.04f, true, true);
            b.Pen(moss, 0.3f);
            b.Ball(V(-24f, bot + 32f, -10f), 18f, 8, 5, 0.03f, 0.45f);
            b.Ball(V(28f, bot + 28f, -10f), 22f, 8, 5, 0.03f, 0.4f);
            // Carved Wind-Totem Perch Column rising from the log to the hawk's talons, wrapped in emerald gale ribbons
            TwistHelix(b, s, bot + 18f, hawkY - 32f, 15f, 11f, 10f, 180f, bark, hawkAmber, 0.4f);
            TwistHelix(b, s, bot + 28f, hawkY - 24f, 4.5f, 3.5f, 28f, 180f, galeGreen, gold, 0.9f);
            HoopPart(b, V(0, bot + 78f, -4f), 84f, 44f, 12f, 2.8f, true, galeGreen, 0.92f, 0.75f, 0.06f);

            // 2. SOARING 3D GOLDEN-AMBER HUNTING HAWK (350mm wingspan!)
            // Fanned 7-feather barred raptor tail
            for (int t = -3; t <= 3; t++)
            {
                FeatherPlate(b, V(t * 7f, hawkY - 48f, -6f), 180f + t * 11f, 86f - Mathf.Abs(t) * 5f, 18f, t % 2 == 0 ? hawkCream : hawkUmber, gold, 0.08f);
            }
            // Sculpted Cream & Golden-Brown Raptor Breast & Torso
            b.Plain();
            b.Pen(hawkCream, 0.45f);
            b.Lathe(V(0, hawkY - 46f, -12f), Vector3.up, new[] { new Vector2(0, 14f), new Vector2(24f, 32f), new Vector2(58f, 36f), new Vector2(92f, 24f), new Vector2(108f, 12f) }, 12, 0.09f, 0.16f);

            // Massive 3-Tone V-Spread Raptor Wings (8 primary + 6 secondary + 4 covert feathers per wing!)
            foreach (int sg in new[] { -1, 1 })
            {
                var w0 = V(sg * 22f, hawkY + 22f, -12f);
                var w1 = V(sg * 94f, hawkY + 78f, -14f);
                var w2 = V(sg * 168f, hawkY + 126f, -8f);
                b.Plain();
                b.Pen(hawkAmber, 0.65f);
                b.Tube(TreeMeshBuilder.Curve(w0, w1, w2, 9), 9f, 3.5f, 6, 0.11f, 0.18f);
                // Primary flight feathers (dark umber & gold)
                for (int f = 0; f < 8; f++)
                {
                    float u = f / 7f;
                    var fAt = Vector3.Lerp(w0, w2, u) + V(0, -4f, 2f);
                    float lean = sg * (50f + f * 9.5f);
                    float len = Mathf.Lerp(86f, 124f, Mathf.Sin(u * Mathf.PI * 0.85f + 0.2f));
                    FeatherPlate(b, fAt, lean, len, 22f, f % 2 == 0 ? hawkUmber : hawkAmber, gold, 0.13f + f * 0.008f);
                }
                // Secondary inner covert feathers (warm cream & amber)
                for (int c = 0; c < 5; c++)
                {
                    float u = c / 4f;
                    var cAt = Vector3.Lerp(w0, w1, u) + V(0, 8f, -3f);
                    FeatherPlate(b, cAt, sg * (38f + c * 9f), 60f, 18f, hawkCream, hawkAmber, 0.15f + c * 0.008f);
                }
            }

            // Sculpted Hawk Head, Golden Crest Plumes, Hooked Beak & Fierce Amber Eyes
            var head = V(0, hawkY + 76f, -18f);
            b.Plain();
            b.Pen(hawkAmber, 0.55f);
            b.Ball(head, 23f, 10, 6, 0.18f, 1.15f);
            TipPart(b, head + V(0, 2f, -19f), V(0f, -0.35f, -0.94f).normalized, 30f, 8.5f, gold, 0.95f, 0f, 0.19f);
            foreach (int sg in new[] { -1, 1 })
                GemBall(b, head + V(sg * 12f, 5f, -16f), 4.8f, C(255, 214, 64), 0.95f, 0.9f, 0.20f);
            for (int cr = -1; cr <= 1; cr++)
                FeatherPlate(b, head + V(cr * 7f, 16f, -4f), cr * 22f, 56f - Mathf.Abs(cr) * 6f, 14f, cr == 0 ? gold : hawkCream, galeGreen, 0.20f);

            // 3. GOLDEN TALONS CLUTCHING A CARVED RECURVE BOW & TWIN ARROWS ACROSS THE HAWK'S BREAST
            var clutchPos = V(0, hawkY - 36f, -22f);
            var bowArc = new List<Vector3>(15);
            for (int i = 0; i <= 14; i++)
            {
                float u = (i / 14f - 0.5f) * 2f; // -1 to 1
                bowArc.Add(clutchPos + V(u * 112f, -24f * u * u + 12f, 0f));
            }
            b.Plain();
            b.Pen(gold, 0.94f);
            b.Tube(bowArc, u => 7.5f - 3.5f * Mathf.Abs(u - 0.5f) * 2f, 7, 0.18f, 0.22f, true, true);
            b.Pen(hawkCream, 0.5f);
            b.Line(bowArc[0], bowArc[14], 2.0f, 0.20f, 0.22f);
            // Golden hawk talons gripping the bow riser
            foreach (int sg in new[] { -1, 1 })
                for (int cl = -1; cl <= 1; cl++)
                    TipPart(b, clutchPos + V(sg * 22f + cl * 4.5f, 8f, -2f), V(cl * 0.2f, -0.95f, -0.2f).normalized, 20f, 3.8f, gold, 0.95f, 0f, 0.21f);
            // Twin crossed emerald-fletched arrows held in the talons
            foreach (int sg in new[] { -1, 1 })
            {
                var a0 = clutchPos + V(-sg * 72f, -28f, -4f);
                var a1 = clutchPos + V(sg * 72f, 28f, -4f);
                var aDir = (a1 - a0).normalized;
                b.Plain();
                b.Pen(hawkCream, 0.6f);
                b.Tube(new[] { a0, a1 }, 2.4f, 2.4f, 5, 0.21f, 0.24f);
                TipPart(b, a1, aDir, 26f, 7.5f, steel, 0.95f, 0f, 0.24f);
                FeatherPlate(b, a0 + aDir * 16f, sg * 68f + 180f, 22f, 8f, galeGreen, gold, 0.23f);
            }
            b.Pieces += 8;
        }

        // ========================================================================================================
        // 12. DIVA / TROUVERE (DANCER LINE) — THREE SCULPTED 3D CRIMSON ROSES, GOLDEN SWAN HARP & ROSE-RIBBON WHIP
        // ========================================================================================================
        private static void BuildDivaRosesHarp(TreeMeshBuilder b, TreeShape s)
        {
            float bot = s.Bottom, top = s.Top, midY = (bot + top) * 0.5f;
            var gold = C(246, 202, 88);
            var darkGold = C(188, 142, 48);
            var roseVelvet = C(172, 18, 52);
            var roseCrimson = C(228, 42, 92);
            var roseBlush = C(254, 138, 176);
            var vineGreen = C(54, 154, 78);
            var leafGreen = C(82, 198, 104);

            // 1. Base: Golden Lotus Concert Pedestal
            b.Plain();
            b.Pen(gold, 0.94f);
            b.Lathe(V(0, bot - 16f, 0), Vector3.up, new[] { new Vector2(0, 74f), new Vector2(14f, 64f), new Vector2(36f, 44f), new Vector2(48f, 26f) }, 14, 0f, 0.04f);

            // 2. ASYMMETRICAL GOLDEN SWAN-NECK CONCERT HARP FRAME
            float harpBot = bot + 28f, harpTop = top + 18f;
            // Left curved acoustic soundbox
            var leftArm = new List<Vector3>(15);
            for (int i = 0; i <= 14; i++)
            {
                float u = i / 14f;
                leftArm.Add(V(-18f - 58f * Mathf.Sin(u * Mathf.PI * 0.85f), Mathf.Lerp(harpBot, harpTop - 24f, u), -6f));
            }
            // Right sculpted golden pillar
            var rightArm = new List<Vector3>(15);
            for (int i = 0; i <= 14; i++)
            {
                float u = i / 14f;
                rightArm.Add(V(18f + 46f * Mathf.Sin(u * Mathf.PI * 0.95f), Mathf.Lerp(harpBot, harpTop + 12f, u), -6f));
            }
            // Upper S-curved Swan-Neck harmonic arch
            var topArch = TreeMeshBuilder.Curve(leftArm[14], V(0f, harpTop + 42f, -8f), rightArm[14], 12);
            b.Plain();
            b.Pen(gold, 0.95f);
            b.Tube(leftArm, u => 15f - 6f * u, 8, 0.04f, 0.16f, true, true);
            b.Tube(rightArm, u => 11f - 3f * u, 8, 0.04f, 0.16f, true, true);
            b.Tube(topArch, 9.5f, 7.5f, 8, 0.16f, 0.20f, true, true);
            // 7 Vertical Golden Harp Strings inside the frame
            for (int st = -3; st <= 3; st++)
            {
                float sx = st * 11f;
                float sy0 = harpBot + 36f + Mathf.Abs(st) * 14f;
                float sy1 = harpTop - 18f + st * 4f;
                b.Alive(0.35f, 0f, false, 0.4f, true);
                b.Pen(gold, 0.95f, 0.65f);
                b.Line(V(sx * 0.7f, sy0, -4f), V(sx, sy1, -6f), 2.0f, 0.10f, 0.22f);
            }
            b.Plain();

            // 3. COILED EMERALD ROSE-VINE WHIP & SERRATED ROSE LEAVES
            TwistHelix(b, s, harpBot + 10f, harpTop - 20f, 6.5f, 4.5f, 34f, 220f, vineGreen, darkGold, 0.55f);
            foreach (var (lx, ly, lean) in new[] { (-64f, midY + 42f, -62f), (58f, midY + 58f, 58f), (-56f, midY - 64f, -54f), (54f, midY - 48f, 64f) })
            {
                FeatherPlate(b, V(lx, ly, -12f), lean, 54f, 24f, leafGreen, gold, 0.16f);
            }

            // 4. THREE LUSH SCULPTED 3D MULTI-PETAL CRIMSON-PINK ROSES!
            SculptedRose3D(b, V(-38f, harpTop - 32f, -18f), 46f, roseVelvet, roseCrimson, roseBlush, gold, 0.18f);
            SculptedRose3D(b, V(26f, midY - 4f, -20f), 54f, roseVelvet, roseCrimson, roseBlush, gold, 0.20f);
            SculptedRose3D(b, V(-28f, harpBot + 82f, -18f), 42f, roseVelvet, roseCrimson, roseBlush, gold, 0.16f);

            // 5. FLOATING 3D GOLDEN MUSICAL EIGHTH-NOTES
            Note3D(b, V(68f, harpTop - 10f, -16f), 1.05f, gold, 0.24f);
            Note3D(b, V(-74f, midY + 8f, -16f), 0.95f, gold, 0.22f);
            Note3D(b, V(64f, harpBot + 96f, -16f), 0.90f, gold, 0.20f);
            b.Pieces += 12;
        }

        // ========================================================================================================
        // SHARED 3D GEOMETRY HELPERS (Plain 3D-lit plates, 3D roses, crystal clusters, 3D musical notes)
        // ========================================================================================================
        private static List<Vector2> LeafOutline(float len, float w) => new List<Vector2>
        {
            new Vector2(0, 0), new Vector2(w * 0.42f, len * 0.22f), new Vector2(w * 0.5f, len * 0.48f),
            new Vector2(w * 0.3f, len * 0.78f), new Vector2(0, len), new Vector2(-w * 0.3f, len * 0.78f),
            new Vector2(-w * 0.5f, len * 0.48f), new Vector2(-w * 0.42f, len * 0.22f)
        };

        private static List<Vector2> RosePetalOutline(float len, float w) => new List<Vector2>
        {
            new Vector2(0, 0), new Vector2(w * 0.46f, len * 0.28f), new Vector2(w * 0.56f, len * 0.64f),
            new Vector2(w * 0.38f, len * 0.92f), new Vector2(0, len), new Vector2(-w * 0.38f, len * 0.92f),
            new Vector2(-w * 0.56f, len * 0.64f), new Vector2(-w * 0.46f, len * 0.28f)
        };

        private static List<Vector2> BannerOutline(float w, float h, float notch) => new List<Vector2>
        {
            new Vector2(-w * 0.5f, 0), new Vector2(w * 0.5f, 0), new Vector2(w * 0.5f, -h),
            new Vector2(0, -h + notch), new Vector2(-w * 0.5f, -h)
        };

        private static List<Vector2> RectOutline(float w, float h) => new List<Vector2>
        {
            new Vector2(-w * 0.5f, -h * 0.5f), new Vector2(w * 0.5f, -h * 0.5f),
            new Vector2(w * 0.5f, h * 0.5f), new Vector2(-w * 0.5f, h * 0.5f)
        };

        private static List<Vector2> StarOutline(int points, float rOut, float rIn)
        {
            var l = new List<Vector2>(points * 2);
            for (int i = 0; i < points * 2; i++)
            {
                float a = i * Mathf.PI / points, r = i % 2 == 0 ? rOut : rIn;
                l.Add(new Vector2(Mathf.Sin(a) * r, Mathf.Cos(a) * r));
            }
            return l;
        }

        private static List<Vector2> GearOutline(int teeth, float rOut, float rIn)
        {
            var l = new List<Vector2>(teeth * 4);
            for (int i = 0; i < teeth; i++)
            {
                float a = i * Mathf.PI * 2f / teeth, d = Mathf.PI * 2f / teeth;
                foreach (var (f, r) in new[] { (0.05f, rIn), (0.2f, rOut), (0.45f, rOut), (0.6f, rIn) })
                    l.Add(new Vector2(Mathf.Sin(a + d * f) * r, Mathf.Cos(a + d * f) * r));
            }
            return l;
        }

        private static void FeatherPlate(TreeMeshBuilder b, Vector3 at, float leanDeg, float len, float w, Color32 col, Color32 edge, float birth)
        {
            b.Plain();
            b.Pen(col, 0.55f, 0f);
            b.Plate(LeafOutline(len, w), Lean(at, leanDeg), 2.4f, birth, Vector2.zero, new Vector2(0, len * 0.45f), edge);
            b.Pieces++;
        }

        private static void StarPlatePlain(TreeMeshBuilder b, Vector3 at, int points, float rOut, float rIn, float leanDeg, Color32 col, float gloss, float birth)
        {
            b.Plain();
            b.Pen(col, gloss, 0.1f);
            b.Plate(StarOutline(points, rOut, rIn), Lean(at, leanDeg), 3.0f, birth, Vector2.zero, Vector2.zero, Shade(col, 0.7f));
            b.Pieces++;
        }

        private static void TipPart(TreeMeshBuilder b, Vector3 at, Vector3 dir, float len, float hw, Color32 col, float gloss, float glow, float birth)
        {
            dir.Normalize();
            var side = Mathf.Abs(dir.z) > 0.9f ? Vector3.right : V(dir.y, -dir.x, 0f).normalized;
            b.Plain();
            if (glow > 0.05f) b.Alive(0.35f, 0f, false, 0f, true);
            b.Pen(col, gloss, glow);
            b.Blade(at, dir, side, len, x => x < 0.25f ? hw * (0.35f + x / 0.385f) : hw * (1f - (x - 0.25f) / 0.75f), x => hw * 0.42f * (1f - x), 4, birth, birth + 0.03f, Shade(col, 0.68f));
            b.Plain();
            b.Pieces++;
        }

        private static void GemBall(TreeMeshBuilder b, Vector3 at, float r, Color32 col, float gloss, float glow, float birth)
        {
            b.Plain();
            if (glow > 0.05f) b.Alive(0.38f, 0f, false, glow >= 0.6f ? 0.5f : 0f, true);
            b.Pen(col, gloss, glow);
            b.Ball(at, r, 10, 6, birth);
            b.Plain();
            b.Pieces++;
        }

        private static void HoopPart(TreeMeshBuilder b, Vector3 mid, float rx, float rz, float tiltDeg, float tube, bool near, Color32 col, float gloss, float glow, float birth)
        {
            var p = new List<Vector3>(15);
            var q = Quaternion.Euler(26f, 0f, tiltDeg);
            for (int i = 0; i <= 14; i++)
            {
                float a = (near ? Mathf.PI : 0f) + i * Mathf.PI / 14f;
                p.Add(mid + q * V(Mathf.Cos(a) * rx, 0f, Mathf.Sin(a) * rz));
            }
            b.Plain();
            if (glow > 0.05f) b.Alive(0.35f, 0f, false, 0f, true);
            b.Pen(col, gloss, glow);
            b.Tube(p, tube, tube, 6, birth, birth + 0.03f, false, false);
            b.Plain();
            if (near) b.Pieces++;
        }

        private static void TwistHelix(TreeMeshBuilder b, TreeShape s, float yA, float yB, float r0, float r1, float amp, float period, Color32 c1, Color32 c2, float gloss)
        {
            int halves = Mathf.Max(2, Mathf.RoundToInt((yB - yA) / (period * 0.5f)));
            float span = yB - yA;
            b.Plain();
            for (int h = 0; h < halves; h++)
            {
                var pa = new List<Vector3>(6);
                var pb = new List<Vector3>(6);
                float zMid = 0f;
                for (int i = 0; i <= 5; i++)
                {
                    float t = (h + i / 5f) / halves, y = yA + span * t, a = t * halves * Mathf.PI, am = amp * (1f - 0.3f * t);
                    pa.Add(V(Mathf.Sin(a) * am, y, Mathf.Cos(a) * am * 0.7f));
                    pb.Add(V(-Mathf.Sin(a) * am, y, -Mathf.Cos(a) * am * 0.7f));
                    if (i == 2) zMid = Mathf.Cos(a);
                }
                float t0 = h / (float)halves, t1 = (h + 1f) / halves;
                float ra = Mathf.Lerp(r0, r1, t0), rb = Mathf.Lerp(r0, r1, t1);
                float b0 = s.TrunkBirth(yA + span * t0), b1 = s.TrunkBirth(yA + span * t1);
                bool aFar = zMid > 0f;
                b.Pen(aFar ? c1 : c2, gloss); b.Tube(aFar ? pa : pb, ra, rb, 6, b0, b1, h == 0, h == halves - 1);
                b.Pen(aFar ? c2 : c1, gloss); b.Tube(aFar ? pb : pa, ra, rb, 6, b0, b1, h == 0, h == halves - 1);
            }
            b.Pieces++;
        }

        private static void CrystalCluster(TreeMeshBuilder b, Vector3 at, float leanDeg, float len, float hw, Color32 mainCol, Color32 accentCol, float birth)
        {
            foreach (var (dAng, sc, col) in new[] { (0f, 1.0f, mainCol), (-24f, 0.68f, accentCol), (24f, 0.64f, accentCol) })
            {
                float r = (leanDeg + dAng) * Mathf.Deg2Rad;
                var d = V(Mathf.Sin(r), Mathf.Cos(r), 0f).normalized;
                b.Alive(0.42f, 0f, false, 0.5f, true);
                b.Pen(col, 0.95f, 0.78f);
                b.Blade(at, d, V(d.y, -d.x, 0f), len * sc, x => x < 0.3f ? hw * sc * (0.5f + x / 0.6f) : hw * sc * (1f - (x - 0.3f) / 0.7f), x => hw * sc * 0.75f * (1f - x * 0.8f), 4, birth, birth + 0.03f, Shade(col, 0.68f));
                b.Plain();
            }
            b.Pieces += 2;
        }

        private static void SculptedRose3D(TreeMeshBuilder b, Vector3 center, float radius, Color32 innerCol, Color32 midCol, Color32 outerEdge, Color32 gold, float birth)
        {
            b.Plain();
            // 1. Outer Ring of 8 Reflexed Cupped Rose Petals (drawn first so inner petals sit in front!)
            for (int i = 0; i < 8; i++)
            {
                float ang = i * 45f + 22.5f;
                float rad = ang * Mathf.Deg2Rad;
                var pAt = center + V(Mathf.Sin(rad) * radius * 0.18f, Mathf.Cos(rad) * radius * 0.18f, 2f);
                var m = TreeMeshBuilder.At(pAt, -ang, 1f, 24f);
                b.Pen(midCol, 0.68f, 0.08f);
                b.Plate(RosePetalOutline(radius * 0.96f, radius * 0.72f), m, 3.2f, birth, Vector2.zero, new Vector2(0, radius * 0.45f), outerEdge);
            }
            // 2. Middle Ring of 6 Cupped Velvet Rose Petals
            for (int i = 0; i < 6; i++)
            {
                float ang = i * 60f;
                float rad = ang * Mathf.Deg2Rad;
                var pAt = center + V(Mathf.Sin(rad) * radius * 0.10f, Mathf.Cos(rad) * radius * 0.10f, -2f);
                var m = TreeMeshBuilder.At(pAt, -ang, 1f, 34f);
                b.Pen(innerCol, 0.72f, 0.1f);
                b.Plate(RosePetalOutline(radius * 0.74f, radius * 0.58f), m, 3.0f, birth + 0.01f, Vector2.zero, new Vector2(0, radius * 0.35f), roseBlushEdge(outerEdge));
            }
            // 3. Inner Whorl of 4 Tightly Cupped Heart Petals & Golden Stamen Core
            for (int i = 0; i < 4; i++)
            {
                float ang = i * 90f + 45f;
                var m = TreeMeshBuilder.At(center + V(0, 0, -5f), -ang, 1f, 42f);
                b.Pen(innerCol, 0.78f, 0.12f);
                b.Plate(RosePetalOutline(radius * 0.48f, radius * 0.42f), m, 2.6f, birth + 0.02f, Vector2.zero, new Vector2(0, radius * 0.22f), gold);
            }
            GemBall(b, center + V(0, 0, -8f), radius * 0.18f, gold, 0.95f, 0.65f, birth + 0.03f);
            b.Pieces += 4;
        }

        private static Color32 roseBlushEdge(Color32 c) => c;

        private static void Note3D(TreeMeshBuilder b, Vector3 at, float scale, Color32 col, float birth)
        {
            b.Plain();
            b.Alive(0.35f, 0f, false, 0.4f, true);
            b.Pen(col, 0.95f, 0.6f);
            // Tilted oval notehead
            b.Ball(at, 9.5f * scale, 8, 5, birth, 0.75f);
            // Vertical stem
            var stemTop = at + V(7f * scale, 34f * scale, 0f);
            b.Tube(new[] { at + V(7f * scale, 0f, 0f), stemTop }, 2.2f * scale, 2.2f * scale, 5, birth, birth + 0.02f);
            // Flowing eighth-note flag
            b.Tube(TreeMeshBuilder.Curve(stemTop, stemTop + V(14f * scale, -8f * scale, 0f), stemTop + V(10f * scale, -22f * scale, 0f), 6), 3.2f * scale, 1.0f * scale, 5, birth + 0.01f, birth + 0.03f);
            b.Plain();
            b.Pieces++;
        }
    }
}
