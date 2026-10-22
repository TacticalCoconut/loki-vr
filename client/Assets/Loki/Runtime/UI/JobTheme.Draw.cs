using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 21 (w21jobui): how a job variant's pictures are MADE (UI/JobTheme.cs says which and when). Every picture starts
    /// as the kit's own pixels (the base sprite's texture, read back: UI/Theme.cs draws it in code) and only gets things ADDED, all
    /// drawn here in code: no file, nothing from the game's data. Where a picture needs room for an ornament beside a corner, the base
    /// is first re-sliced (<see cref="Reslice"/>): its corner zones grow along the boards by copies of its own stretch strip, so the
    /// 9-slice draws exactly what the base's would, with a longer unstretched corner. The outer size never changes.
    /// The emblems are small filled shapes written as text (<see cref="EmblemText"/>): S a stroke, D a disc, R a ring, A an arc, P a
    /// polygon, T a turn / scale / shift of what follows, a leading '-' cuts out of what is drawn so far; a box of -1..1, y up.
    /// </summary>
    public static partial class JobTheme
    {
        // ------------------------------------------------------------------ the emblems
        private const string Dagger = "P -0.15 0.12 0 -0.98 0.15 0.12;S -0.38 0.17 0.38 0.17 0.085;S 0 0.2 0 0.72 0.08;D 0 0.84 0.13";
        private const string Blade = "P -0.13 0.2 0 -0.98 0.13 0.2;S -0.34 0.26 0.34 0.26 0.075;S 0 0.3 0 0.82 0.075";
        /// <summary>One emblem per line. They follow the skill trees' families (w22tree UI/TreeThemes.cs): the Swordsman line's sword
        /// (the Knight's with the winged guard of the ornate tree, the Crusader's shield), the Mage's crystal staff (the Wizard's with
        /// its rune ring, the Sage's book), the Archer's bow (the Hunter's drawn, the Bard's and Dancer's lyre), the Acolyte's cross
        /// (the Priest's with a halo ring, the Monk's prayer beads), the Merchant's purse (the Blacksmith's anvil, the Alchemist's
        /// flask), the Thief's dagger (the Assassin's crossed blades, the Rogue's mask), the Gunslinger's sight, the Ninja's shuriken,
        /// the Taekwon's star (the Star Gladiator's sun, the Soul Linker's spirit flame), the Summoner's paw, the Novice's sprout
        /// (the Super Novice's with the angel's halo).</summary>
        private static readonly Dictionary<JobLine, string> EmblemText = new Dictionary<JobLine, string>
        {
            { JobLine.Novice, "S 0 -0.95 0 -0.05 0.075;P 0 -0.12 -0.3 0.02 -0.78 0.42 -0.86 0.9 -0.42 0.74 -0.1 0.36;P 0 -0.12 0.3 0.02 0.78 0.42 0.86 0.9 0.42 0.74 0.1 0.36" },
            { JobLine.SuperNovice, "S 0 -0.98 0 -0.3 0.07;P 0 -0.38 -0.26 -0.26 -0.68 0.08 -0.74 0.48 -0.36 0.36 -0.08 0.04;P 0 -0.38 0.26 -0.26 0.68 0.08 0.74 0.48 0.36 0.36 0.08 0.04;R 0 0.74 0.24 0.07" },
            { JobLine.Swordsman, "P -0.13 -0.22 -0.13 0.68 0 0.98 0.13 0.68 0.13 -0.22;S -0.42 -0.27 0.42 -0.27 0.085;S 0 -0.3 0 -0.74 0.075;D 0 -0.85 0.13" },
            { JobLine.Knight, "P -0.15 -0.2 -0.15 0.66 0 0.98 0.15 0.66 0.15 -0.2;-S 0 -0.12 0 0.5 0.035;P -0.1 -0.16 -0.52 -0.02 -0.98 0.46 -0.66 0.12 -0.82 -0.06 -0.56 -0.14 -0.66 -0.36 -0.1 -0.4;P 0.1 -0.16 0.52 -0.02 0.98 0.46 0.66 0.12 0.82 -0.06 0.56 -0.14 0.66 -0.36 0.1 -0.4;S 0 -0.4 0 -0.78 0.075;D 0 -0.87 0.13" },
            { JobLine.Crusader, "P -0.74 0.86 0.74 0.86 0.74 0.02 0.46 -0.54 0 -0.96 -0.46 -0.54 -0.74 0.02;-S 0 0.58 0 -0.56 0.11;-S -0.42 0.26 0.42 0.26 0.11" },
            { JobLine.Mage, "S 0 -0.96 0 0.16 0.07;P 0 0.98 0.3 0.56 0 0.12 -0.3 0.56" },
            { JobLine.Wizard, "S 0 -0.96 0 0.0 0.07;R 0 0.5 0.47 0.055;P 0 0.86 0.24 0.5 0 0.14 -0.24 0.5" },
            { JobLine.Sage, "P -0.06 -0.62 -0.94 -0.44 -0.94 0.36 -0.06 0.18;P 0.06 -0.62 0.94 -0.44 0.94 0.36 0.06 0.18;P 0 0.98 0.17 0.72 0 0.46 -0.17 0.72" },
            { JobLine.Archer, "A 0.86 0 1.12 134 226 0.09;S 0.08 0.81 0.08 -0.81 0.035;S 0.08 0 -0.62 0 0.045;P -0.92 0 -0.6 0.15 -0.6 -0.15" },
            { JobLine.Hunter, "A 0 -0.72 1.02 38 142 0.085;S -0.8 -0.1 0 -0.66 0.035;S 0.8 -0.1 0 -0.66 0.035;S 0 -0.7 0 0.72 0.05;P 0 0.99 -0.17 0.66 0.17 0.66" },
            { JobLine.Bard, "S -0.52 0.7 -0.52 -0.25 0.08;S 0.52 0.7 0.52 -0.25 0.08;A 0 -0.25 0.52 180 360 0.08;S -0.66 0.52 0.66 0.52 0.065;S -0.22 0.52 -0.22 -0.66 0.035;S 0 0.52 0 -0.74 0.035;S 0.22 0.52 0.22 -0.66 0.035;D -0.52 0.8 0.13;D 0.52 0.8 0.13;S 0 -0.77 0 -0.95 0.1" },
            { JobLine.Acolyte, "S 0 -0.92 0 0.92 0.13;S -0.58 0.3 0.58 0.3 0.13" },
            { JobLine.Priest, "S 0 -0.95 0 0.95 0.11;S -0.62 0.3 0.62 0.3 0.11;R 0 0.3 0.42 0.07" },
            { JobLine.Monk, "D 0 0.85 0.15;D 0.5 0.66 0.15;D 0.72 0.2 0.15;D 0.5 -0.26 0.15;D 0 -0.45 0.15;D -0.5 -0.26 0.15;D -0.72 0.2 0.15;D -0.5 0.66 0.15;S 0 -0.5 0 -0.72 0.06;P -0.14 -0.7 0.14 -0.7 0.2 -0.98 -0.2 -0.98" },
            { JobLine.Merchant, "P -0.24 0.5 0.24 0.5 0.72 -0.22 0.62 -0.78 0 -0.94 -0.62 -0.78 -0.72 -0.22;P -0.22 0.62 -0.42 0.96 0.42 0.96 0.22 0.62;S -0.3 0.56 0.3 0.56 0.07;-R 0 -0.3 0.26 0.06" },
            { JobLine.Blacksmith, "P -0.96 0.5 0.74 0.5 0.74 0.16 0.3 0.1 0.2 -0.36 0.56 -0.6 0.56 -0.86 -0.56 -0.86 -0.56 -0.6 -0.2 -0.36 -0.3 0.1 -0.58 0.1" },
            { JobLine.Alchemist, "P -0.15 0.9 0.15 0.9 0.15 0.34 0.72 -0.62 0.56 -0.92 -0.56 -0.92 -0.72 -0.62 -0.15 0.34;S -0.27 0.92 0.27 0.92 0.06;-D 0.12 -0.42 0.13;-D -0.2 -0.66 0.09" },
            { JobLine.Thief, Dagger },
            { JobLine.Assassin, "T 42 1 0 -0.06;" + Blade + ";T -42 1 0 -0.06;" + Blade },
            { JobLine.Rogue, "P -0.96 0.42 -0.5 0.56 0 0.34 0.5 0.56 0.96 0.42 0.88 -0.16 0.44 -0.42 0.14 -0.2 0 -0.02 -0.14 -0.2 -0.44 -0.42 -0.88 -0.16;-D -0.46 0.1 0.19;-D 0.46 0.1 0.19" },
            { JobLine.Gunslinger, "R 0 0 0.6 0.075;S 0 0.34 0 0.98 0.065;S 0 -0.34 0 -0.98 0.065;S 0.34 0 0.98 0 0.065;S -0.34 0 -0.98 0 0.065;D 0 0 0.1" },
            { JobLine.Ninja, "P 0 0.98 0.2 0.2 0.98 0 0.2 -0.2 0 -0.98 -0.2 -0.2 -0.98 0 -0.2 0.2;-D 0 0 0.12" },
            { JobLine.Taekwon, "P 0 0.98 0.235 0.324 0.932 0.303 0.38 -0.124 0.576 -0.793 0 -0.4 -0.576 -0.793 -0.38 -0.124 -0.932 0.303 -0.235 0.324" },
            { JobLine.StarGladiator, "D 0 0 0.4;S 0 0.6 0 0.97 0.07;S 0 -0.6 0 -0.97 0.07;S 0.6 0 0.97 0 0.07;S -0.6 0 -0.97 0 0.07;S 0.42 0.42 0.68 0.68 0.07;S -0.42 0.42 -0.68 0.68 0.07;S 0.42 -0.42 0.68 -0.68 0.07;S -0.42 -0.42 -0.68 -0.68 0.07" },
            { JobLine.SoulLinker, "P 0.05 0.98 0.3 0.5 0.62 0.05 0.56 -0.5 0.26 -0.88 -0.26 -0.88 -0.56 -0.5 -0.62 0.0 -0.36 0.4 -0.3 0.1 -0.05 0.5;-P 0.02 0.12 0.24 -0.24 0.16 -0.58 -0.16 -0.58 -0.26 -0.24" },
            { JobLine.Summoner, "D 0 -0.34 0.44;D -0.6 0.16 0.2;D -0.22 0.62 0.21;D 0.22 0.62 0.21;D 0.6 0.16 0.2" },
        };

        private struct Prim { public char Kind; public bool Cut; public float[] V; public float Cos, Sin, Scale, Tx, Ty; }
        private sealed class Shape { public Prim[] Prims; public float U0, U1, V0, V1; }
        private static readonly Dictionary<JobLine, Shape> _shapes = new Dictionary<JobLine, Shape>();
        private static readonly Dictionary<long, float[]> _masks = new Dictionary<long, float[]>();

        private static Shape ShapeOf(JobLine line)
        {
            if (_shapes.TryGetValue(line, out var s)) return s;
            var list = new List<Prim>(); float cos = 1f, sin = 0f, scale = 1f, tx = 0f, ty = 0f;
            foreach (var raw in EmblemText[line].Split(';'))
            {
                string part = raw.Trim(); if (part.Length == 0) continue;
                bool cut = part[0] == '-'; if (cut) part = part.Substring(1);
                var f = part.Substring(1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var v = new float[f.Length]; for (int i = 0; i < f.Length; i++) v[i] = float.Parse(f[i], CultureInfo.InvariantCulture);
                if (part[0] == 'T') { float a = v[0] * Mathf.Deg2Rad; cos = Mathf.Cos(a); sin = Mathf.Sin(a); scale = v[1]; tx = v[2]; ty = v[3]; continue; }
                list.Add(new Prim { Kind = part[0], Cut = cut, V = v, Cos = cos, Sin = sin, Scale = scale, Tx = tx, Ty = ty });
            }
            s = new Shape { Prims = list.ToArray(), U0 = 1f, U1 = -1f, V0 = 1f, V1 = -1f };
            // its own extent (a coarse look: 64 x 64), so it fills whatever box it is stamped into
            const int n = 64;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float u = (i + 0.5f) / n * 2f - 1f, v = (j + 0.5f) / n * 2f - 1f;
                    if (Covered(s, u, v, n * 0.5f) < 0.5f) continue;
                    if (u < s.U0) s.U0 = u; if (u > s.U1) s.U1 = u; if (v < s.V0) s.V0 = v; if (v > s.V1) s.V1 = v;
                }
            if (s.U1 < s.U0) { s.U0 = s.V0 = -1f; s.U1 = s.V1 = 1f; }
            s.U0 -= 1f / n; s.U1 += 1f / n; s.V0 -= 1f / n; s.V1 += 1f / n;
            _shapes[line] = s;
            return s;
        }

        private static float SegD(float x, float y, float x0, float y0, float x1, float y1)
        {
            float dx = x1 - x0, dy = y1 - y0, l = dx * dx + dy * dy;
            float t = l < 1e-9f ? 0f : Mathf.Clamp01(((x - x0) * dx + (y - y0) * dy) / l);
            float px = x - (x0 + t * dx), py = y - (y0 + t * dy);
            return Mathf.Sqrt(px * px + py * py);
        }

        /// <summary>The emblem's coverage at (u, v), <paramref name="perUnit"/> pixels per emblem unit (for the soft edge).</summary>
        private static float Covered(Shape s, float u, float v, float perUnit)
        {
            float acc = 0f;
            for (int k = 0; k < s.Prims.Length; k++)
            {
                var p = s.Prims[k]; var a = p.V;
                // into the primitive's own frame: back by its shift, scale and turn
                float qx = (u - p.Tx) / p.Scale, qy = (v - p.Ty) / p.Scale;
                float x = qx * p.Cos + qy * p.Sin, y = -qx * p.Sin + qy * p.Cos;
                float sd;
                switch (p.Kind)
                {
                    case 'S': sd = SegD(x, y, a[0], a[1], a[2], a[3]) - a[4]; break;
                    case 'D': sd = Mathf.Sqrt((x - a[0]) * (x - a[0]) + (y - a[1]) * (y - a[1])) - a[2]; break;
                    case 'R': sd = Mathf.Abs(Mathf.Sqrt((x - a[0]) * (x - a[0]) + (y - a[1]) * (y - a[1])) - a[2]) - a[3]; break;
                    case 'A':
                    {
                        float a0 = a[3] * Mathf.Deg2Rad, a1 = a[4] * Mathf.Deg2Rad;
                        float th = Mathf.Atan2(y - a[1], x - a[0]); while (th < a0 - 1e-5f) th += 2f * Mathf.PI;
                        if (th <= a1) sd = Mathf.Abs(Mathf.Sqrt((x - a[0]) * (x - a[0]) + (y - a[1]) * (y - a[1])) - a[2]);
                        else
                        {
                            float e0x = a[0] + a[2] * Mathf.Cos(a0), e0y = a[1] + a[2] * Mathf.Sin(a0), e1x = a[0] + a[2] * Mathf.Cos(a1), e1y = a[1] + a[2] * Mathf.Sin(a1);
                            sd = Mathf.Min(Mathf.Sqrt((x - e0x) * (x - e0x) + (y - e0y) * (y - e0y)), Mathf.Sqrt((x - e1x) * (x - e1x) + (y - e1y) * (y - e1y)));
                        }
                        sd -= a[5]; break;
                    }
                    default: // 'P'
                    {
                        int n = a.Length / 2; float d = 1e9f; bool inside = false;
                        for (int i = 0; i < n; i++)
                        {
                            int j = (i + 1) % n; float x0 = a[2 * i], y0 = a[2 * i + 1], x1 = a[2 * j], y1 = a[2 * j + 1];
                            float e = SegD(x, y, x0, y0, x1, y1); if (e < d) d = e;
                            if ((y0 > y) != (y1 > y) && x < (x1 - x0) * (y - y0) / (y1 - y0) + x0) inside = !inside;
                        }
                        sd = inside ? -d : d; break;
                    }
                }
                float c = Mathf.Clamp01(0.5f - sd * p.Scale * perUnit);
                if (p.Cut) acc *= 1f - c; else if (c > acc) acc = c;
            }
            return acc;
        }

        /// <summary>A line's emblem in a box of w x h pixels (row 0 at the bottom), as large as fits, mirrored left-right if asked
        /// (the right-hand twin of a corner). Cached.</summary>
        internal static float[] Emblem(JobLine line, int w, int h, bool flip)
        {
            long key = ((long)line << 40) | ((long)w << 24) | ((long)h << 8) | (flip ? 1L : 0L);
            if (_masks.TryGetValue(key, out var m)) return m;
            var s = ShapeOf(line);
            float per = Mathf.Min((w - 1f) / (s.U1 - s.U0), (h - 1f) / (s.V1 - s.V0)); // pixels per emblem unit
            float uc = (s.U0 + s.U1) * 0.5f, vc = (s.V0 + s.V1) * 0.5f;
            m = new float[w * h];
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    float du = (i + 0.5f - w * 0.5f) / per, dv = (j + 0.5f - h * 0.5f) / per;
                    m[j * w + i] = Covered(s, uc + (flip ? -du : du), vc + dv, per);
                }
            if (_masks.Count > 400) _masks.Clear();
            _masks[key] = m;
            return m;
        }

        /// <summary>The shapes a variant's pictures stamp, made ahead in two steps (UI/JobTheme.cs BuildStep: one step a frame): the
        /// emblem itself and its small sizes; then the canvas' large faint emblem and the braid.</summary>
        internal static void Prepare(Variant v, int step)
        {
            if (step == 0)
            {
                ShapeOf(v.Line);
                int e = v.Tier < 2 ? EmblemPx : MedalEmblemPx;
                Emblem(v.Line, e, e, false); Emblem(v.Line, e, e, true);
                Emblem(v.Line, PlateEmblemW, PlateEmblemH, false); Emblem(v.Line, PlateEmblemW, PlateEmblemH, true);
                if (v.Tier >= 2 && v.HasAccent) { Emblem(v.Line, TinyPx, TinyPx, false); Emblem(v.Line, TinyPx, TinyPx, true); }
            }
            else
            {
                if (v.Tier >= 2 && v.HasAccent) { Emblem(v.Line, MarkPx, MarkPx, false); Emblem(v.Line, MarkPx, MarkPx, true); }
                if (v.Tier >= 4) Vine(v.Tier >= 5 ? VineLong : VineShort);
            }
        }

        // ------------------------------------------------------------------ small tools
        private static float Cov(float d) => Mathf.Clamp01(0.5f - d);
        private static Color Mix(Color a, Color b, float t) { t = Mathf.Clamp01(t); return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a); }
        private static Color Mul(Color a, float k) => new Color(Mathf.Clamp01(a.r * k), Mathf.Clamp01(a.g * k), Mathf.Clamp01(a.b * k), a.a);
        private static float RoundRect(float px, float py, float hw, float hh, float r)
        {
            float qx = Mathf.Abs(px) - hw + r, qy = Mathf.Abs(py) - hh + r;
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Min(Mathf.Max(qx, qy), 0f) + Mathf.Sqrt(ox * ox + oy * oy) - r;
        }
        private static int MapAxis(int i, int big, int n, int lo, int hi, int ex)
        {
            if (ex == 0) return i;
            int mid = n - lo - hi; // the base's stretch strip
            if (i < lo) return i;
            if (i < lo + ex) return lo;            // the strip's first line, held: what the 9-slice shows where the stretch begins
            if (i < lo + ex + mid) return i - ex;
            int r = big - 1 - i; // counted from the far edge
            if (r < hi) return n - 1 - r;
            return n - 1 - hi;                     // the strip's last line, held
        }

        /// <summary>The base picture with each corner zone grown by <paramref name="exX"/> / <paramref name="exY"/> pixels along the
        /// boards: the new columns and rows are the line of the base's own stretch strip that touches the corner, held (a 9-slice
        /// stretches that strip along the board anyway: the grown zone looks as the board does where its stretch begins, without a
        /// seam and without a repeated pattern). Drawn as a 9-slice with the grown border it is what the base draws.</summary>
        internal static Color[] Reslice(Color[] src, int w, int h, Vector4 border, int exX, int exY, out int W, out int H, out Vector4 grown)
        {
            W = w + 2 * exX; H = h + 2 * exY;
            int L = Mathf.RoundToInt(border.x), B = Mathf.RoundToInt(border.y), R = Mathf.RoundToInt(border.z), T = Mathf.RoundToInt(border.w);
            grown = new Vector4(L + exX, B + exY, R + exX, T + exY);
            if (exX == 0 && exY == 0) return (Color[])src.Clone();
            var mx = new int[W]; for (int x = 0; x < W; x++) mx[x] = MapAxis(x, W, w, L, R, exX);
            var dst = new Color[W * H];
            for (int y = 0; y < H; y++)
            {
                int sy = MapAxis(y, H, h, B, T, exY) * w, dy = y * W;
                for (int x = 0; x < W; x++) dst[dy + x] = src[sy + mx[x]];
            }
            return dst;
        }

        private static void Stamp(float[] gold, int W, int H, int x0, int y0, float[] mask, int mw, int mh, float k = 1f)
        {
            for (int j = 0; j < mh; j++)
            {
                int y = y0 + j; if (y < 0 || y >= H) continue;
                for (int i = 0; i < mw; i++)
                {
                    int x = x0 + i; if (x < 0 || x >= W) continue;
                    float c = mask[j * mw + i] * k; if (c <= 0f) continue;
                    int at = y * W + x; if (c > gold[at]) gold[at] = c;
                }
            }
        }
        private static void RingMask(float[] gold, int W, int H, float cx, float cy, float r, float half, float k = 1f)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r - half - 1)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + r + half + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r - half - 1)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + r + half + 1));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float c = Cov(Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - r) - half) * k; if (c <= 0f) continue;
                    int at = y * W + x; if (c > gold[at]) gold[at] = c;
                }
        }
        private static void Disc(Color[] px, int W, int H, float cx, float cy, float r, Color c, float k)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r - 1)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + r + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r - 1)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + r + 1));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float a = Cov(Mathf.Sqrt(dx * dx + dy * dy) - r) * k; if (a <= 0f) continue;
                    int at = y * W + x; if (px[at].a <= 0.01f) continue;
                    px[at] = Mix(px[at], c, a);
                }
        }
        /// <summary>The gold of <paramref name="gold"/> laid on the picture: a dark shade down-right of it first (the kit's scrolls
        /// have the same), then the gold, lighter towards the top. The mask is cleared.</summary>
        private static void Gild(Color[] px, float[] gold, int W, int H, Color shade)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int at = y * W + x; float g = gold[at];
                    float s = x > 0 && y < H - 1 ? gold[at + W - 1] : 0f; // what stands up-left of this pixel shades it
                    if (g <= 0f && s <= 0f) continue;
                    var c = px[at]; if (c.a <= 0.01f) continue;
                    if (s > 0f) c = Mix(c, shade, 0.6f * s * (1f - g));
                    if (g > 0f) c = Mix(c, Mix(Theme.Gold, Theme.GoldLight, 0.3f + 0.3f * (y / (float)H)), g);
                    px[at] = c;
                }
            Array.Clear(gold, 0, gold.Length);
        }

        // ------------------------------------------------------------------ the braid of tiers 4 and 5
        private const int VineDeep = 30, VineTop = 26, VineShort = 40, VineLong = 62; private const float VineMid = 41.5f;
        private static readonly Dictionary<int, float[]> _vines = new Dictionary<int, float[]>();
        /// <summary>Where the braid's two strands cross (px along it): a small enamel gem on each at tier 5.</summary>
        private static readonly float[] VineGems = { 17f, 34f };
        /// <summary>A gold braid along a board, in its own frame: x along the board (0 at the medallion, <paramref name="len"/> px), y
        /// the depth into the wood from <see cref="VineTop"/>. Two strands woven through each other, calming down towards a bud at
        /// the end, a small leaf at each crest of the first. Tier 4 has <see cref="VineShort"/> px of it, tier 5 <see cref="VineLong"/>.</summary>
        private static float[] Vine(int len)
        {
            if (_vines.TryGetValue(len, out var m)) return m;
            m = new float[len * VineDeep];
            for (int strand = 0; strand < 2; strand++)
            {
                float phase = strand * Mathf.PI;
                for (int x = 0; x < len; x++)
                    for (int y = 0; y < VineDeep; y++)
                    {
                        float fx = x + 0.5f, fy = y + 0.5f + VineTop, best = 99f, half = 1f;
                        for (float t = Mathf.Max(0f, fx - 5f); t <= Mathf.Min(len - 4f, fx + 5f); t += 0.5f)
                        {
                            float amp = 7f * (1f - 0.75f * t / len);
                            float cy = VineMid + amp * Mathf.Sin(2f * Mathf.PI * t / 34f + phase);
                            float d = Mathf.Sqrt((fx - t) * (fx - t) + (fy - cy) * (fy - cy));
                            if (d < best) { best = d; half = 1.7f - 0.8f * t / len; }
                        }
                        float c = Cov(best - half);
                        // the bud at the end, a leaf at each crest of the first strand
                        float ex = len - 4f, ea = 7f * (1f - 0.75f * ex / len), ey = VineMid + ea * Mathf.Sin(2f * Mathf.PI * ex / 34f + phase);
                        c = Mathf.Max(c, Cov(Mathf.Sqrt((fx - ex) * (fx - ex) + (fy - ey) * (fy - ey)) - 2.3f));
                        if (strand == 0)
                            for (int k = 0; k < 3; k++)
                            {
                                float t = 8.5f + 17f * k; if (t > len - 8f) break;
                                float amp = 7f * (1f - 0.75f * t / len), sgn = k % 2 == 0 ? 1f : -1f;
                                float lx = t, ly = VineMid + sgn * (amp + 2.6f);
                                c = Mathf.Max(c, Cov(Mathf.Sqrt((fx - lx) * (fx - lx) + (fy - ly) * (fy - ly)) - 1.7f));
                            }
                        if (c > m[y * len + x]) m[y * len + x] = c;
                    }
            }
            _vines[len] = m;
            return m;
        }
        /// <summary>The braid laid along a board from a corner: <paramref name="along0"/> px from the corner; side: 0 the top or bottom
        /// board (x along), 1 a side board (y along); the corner by <paramref name="right"/> and <paramref name="top"/>.</summary>
        private static void LayVine(float[] gold, int W, int H, float[] vine, int len, int along0, int side, bool right, bool top)
        {
            for (int a = 0; a < len; a++)
                for (int d = 0; d < VineDeep; d++)
                {
                    float c = vine[d * len + a]; if (c <= 0f) continue;
                    int along = along0 + a, depth = VineTop + d;
                    int x = side == 0 ? (right ? W - 1 - along : along) : (right ? W - 1 - depth : depth);
                    int y = side == 0 ? (top ? H - 1 - depth : depth) : (top ? H - 1 - along : along);
                    if (x < 0 || x >= W || y < 0 || y >= H) continue;
                    int at = y * W + x; if (c > gold[at]) gold[at] = c;
                }
        }

        // ------------------------------------------------------------------ the five pictures
        /// <summary>Design pixels of the ornaments (the kit's own unit: UI/Theme.cs WoodPx 64 = the wood's width).</summary>
        public const int EmblemPx = 27, MedalEmblemPx = 32, PlateEmblemW = 15, PlateEmblemH = 24, MarkPx = 54, TinyPx = 9;
        public const float MedalAt = 34f, MedalRadius = 26f, HairlineAt = 14.5f, PlateTextFrom = 23.2f, PlateEndPx = 24f, CardTextFrom = 4.5f;

        /// <summary>A variant's picture of a slot as pixels (null: this tier leaves the slot as the base has it), its size and its
        /// 9-slice border. <paramref name="plain"/>: the base's own pixels re-sliced the same way and nothing added (what the
        /// smoke compares with).</summary>
        internal static Color[] Compose(Variant v, Slot slot, Sprite baseSprite, out int W, out int H, out Vector4 border, bool plain = false)
        {
            var tex = baseSprite.texture; int w = tex.width, h = tex.height; W = w; H = h; border = baseSprite.border;
            switch (slot)
            {
                case Slot.Frame:
                {
                    int ex = v.Tier >= 4 ? 64 : v.Tier >= 2 ? 0 : 32, ey = v.Tier >= 4 ? 64 : 0;
                    var px = Reslice(tex.GetPixels(), w, h, baseSprite.border, ex, ey, out W, out H, out border);
                    if (!plain) { DrawFrameUnder(px, W, H, v); DrawFrameOver(px, W, H, v); }
                    return px;
                }
                case Slot.Canvas:
                {
                    if (v.Tier < 2 || !v.HasAccent) return null;
                    var px = Reslice(tex.GetPixels(), w, h, baseSprite.border, 64, 64, out W, out H, out border);
                    if (!plain) DrawCanvas(px, W, H, v);
                    return px;
                }
                case Slot.Card:
                {
                    if (!(v.HasAccent && v.Tier >= 1) && !v.Fine) return null;
                    var px = tex.GetPixels();
                    if (!plain) DrawCard(px, W, H, v);
                    return px;
                }
                case Slot.Plate:
                {
                    var px = tex.GetPixels();
                    // the ends keep their shape at any title height and width: only the two middle rows stretch, and the ends are
                    // PlateEndPx wide (the kit's 22: an ornament reaching 23 px would be smeared along the plate)
                    border = new Vector4(Mathf.Max(baseSprite.border.x, PlateEndPx), H / 2 - 1, Mathf.Max(baseSprite.border.z, PlateEndPx), H / 2 - 1);
                    if (!plain) DrawPlate(px, W, H, v);
                    return px;
                }
                default:
                {
                    if (!v.HasAccent || v.Tier < 1) return null;
                    var px = tex.GetPixels();
                    if (!plain) DrawSeeFrame(px, W, H, v);
                    return px;
                }
            }
        }

        /// <summary>The frame's picture in steps (UI/JobTheme.cs BuildStep: one a frame): 0 the base re-sliced and what lies under the
        /// corner pieces (the hairlines, the braid); 1 the corner pieces. <see cref="MakeSprite"/> then only makes the texture.</summary>
        internal static void FrameStage(Variant v, Sprite baseSprite, int stage)
        {
            if (stage == 0)
            {
                var tex = baseSprite.texture;
                int ex = v.Tier >= 4 ? 64 : v.Tier >= 2 ? 0 : 32, ey = v.Tier >= 4 ? 64 : 0;
                v.Px = Reslice(tex.GetPixels(), tex.width, tex.height, baseSprite.border, ex, ey, out v.PxW, out v.PxH, out v.PxBorder);
                DrawFrameUnder(v.Px, v.PxW, v.PxH, v);
            }
            else if (v.Px != null) DrawFrameOver(v.Px, v.PxW, v.PxH, v);
        }

        private static Sprite MakeSprite(Variant v, Slot slot, Sprite baseSprite)
        {
            Color[] px; int W, H; Vector4 border;
            if (slot == Slot.Frame && v.Px != null) { px = v.Px; W = v.PxW; H = v.PxH; border = v.PxBorder; } // (made in the two steps before)
            else px = Compose(v, slot, baseSprite, out W, out H, out border);
            if (px == null) return null;
            var bt = baseSprite.texture;
            string name = $"LokiJobTheme:{slot}:{v.Line}:{v.Tier}{(v.Reborn && v.Tier < 2 ? "r" : "")}";
            // the texture keeps the kit's name and stays readable, as the kit's own (it IS the theme's picture of this slot: whoever reads
            // Theme.Frame's pixels gets pixels); the sprite's name says which variant
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = bt.name, wrapMode = bt.wrapMode, filterMode = bt.filterMode, anisoLevel = bt.anisoLevel, hideFlags = HideFlags.DontSave };
            tex.SetPixels(px); tex.Apply(true, false);
            var sp = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), baseSprite.pixelsPerUnit, 0, SpriteMeshType.FullRect, border);
            sp.name = name; sp.hideFlags = HideFlags.DontSave;
            return sp;
        }

        private static Color Enamel(Variant v, float light) => v.HasAccent ? Mix(Mul(v.Accent, 0.7f), Mul(v.Accent, 1.08f), light) : Mix(Theme.WoodDark, Theme.Wood, light);

        private static void DrawFrameUnder(Color[] px, int W, int H, Variant v)
        {
            if (!v.Fine && v.Tier < 4) return;
            var gold = new float[W * H];
            // ---- under the medallions: the finer gold edge (a hairline between the outer edge and the gold line), the braid
            if (v.Fine)
            {
                int lines = v.Tier >= 5 ? 2 : 1; int band = Mathf.CeilToInt(HairlineAt + 3f);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        if (x == band && y >= band && y < H - band) x = W - band; // (only the band along the edges is walked: the middle is skipped)
                        float fx = x + 0.5f, fy = y + 0.5f;
                        float sd = Mathf.Abs(RoundRect(fx - W * 0.5f, fy - H * 0.5f, W * 0.5f - HairlineAt, H * 0.5f - HairlineAt, 19.5f));
                        float c = Cov(sd - 0.75f) * 0.92f;
                        if (lines == 2) c = Mathf.Max(c, Cov(Mathf.Abs(RoundRect(fx - W * 0.5f, fy - H * 0.5f, W * 0.5f - 9.5f, H * 0.5f - 9.5f, 24.5f)) - 0.6f) * 0.85f);
                        if (c > 0f) gold[y * W + x] = c;
                    }
            }
            if (v.Tier >= 4)
            {
                int len = v.Tier >= 5 ? VineLong : VineShort; var vine = Vine(len);
                for (int corner = 0; corner < 4; corner++)
                {
                    bool right = (corner & 1) != 0, top = (corner & 2) != 0;
                    LayVine(gold, W, H, vine, len, 61, 0, right, top); LayVine(gold, W, H, vine, len, 61, 1, right, top);
                }
            }
            Gild(px, gold, W, H, Theme.WoodDark);
            if (v.Tier >= 5 && v.HasAccent)
            {
                for (int corner = 0; corner < 4; corner++)
                {
                    bool right = (corner & 1) != 0, top = (corner & 2) != 0;
                    foreach (float g in VineGems)
                    {
                        float along = 61f + g, depth = VineMid;
                        float ax = right ? W - along : along, ay = top ? H - depth : depth;       // on the top / bottom board
                        float bx = right ? W - depth : depth, by = top ? H - along : along;       // on the side board
                        Disc(px, W, H, ax, ay, 3.1f, Theme.WoodDark, 0.8f); Disc(px, W, H, ax, ay, 2.3f, Enamel(v, 0.8f), 1f); Disc(px, W, H, ax - 0.6f, ay + 0.6f, 0.8f, Color.white, 0.5f);
                        Disc(px, W, H, bx, by, 3.1f, Theme.WoodDark, 0.8f); Disc(px, W, H, bx, by, 2.3f, Enamel(v, 0.8f), 1f); Disc(px, W, H, bx - 0.6f, by + 0.6f, 0.8f, Color.white, 0.5f);
                    }
                }
            }
        }

        private static void DrawFrameOver(Color[] px, int W, int H, Variant v)
        {
            var gold = new float[W * H];
            if (v.Tier < 2)
            {
                // ---- Novice / first job: one small emblem beside the scroll at each end of the top board
                Stamp(gold, W, H, 73, H - 28 - EmblemPx, Emblem(v.Line, EmblemPx, EmblemPx, false), EmblemPx, EmblemPx);
                Stamp(gold, W, H, W - 73 - EmblemPx, H - 28 - EmblemPx, Emblem(v.Line, EmblemPx, EmblemPx, true), EmblemPx, EmblemPx);
                Gild(px, gold, W, H, Theme.WoodDark);
                return;
            }
            // ---- second job and up: a round enamel medallion with the emblem in each corner (where the base has its two scrolls)
            for (int corner = 0; corner < 4; corner++)
            {
                bool right = (corner & 1) != 0, top = (corner & 2) != 0;
                float cx = right ? W - MedalAt : MedalAt, cy = top ? H - MedalAt : MedalAt, R = MedalRadius;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - R - 3)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + R + 3));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - R - 3)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + R + 3));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x + 0.5f - cx, dy = y + 0.5f - cy, d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d > R + 2.5f) continue;
                        int at = y * W + x; var c = px[at]; if (c.a <= 0.01f) continue;
                        c = Mix(c, Theme.WoodDark, 0.75f * Cov(d - R - 1.3f));
                        float light = Mathf.Clamp01(0.5f + 0.45f * (dy - dx) / R); // lit from the top left, as the frame
                        c = Mix(c, Enamel(v, light), Cov(d - R + 0.3f));
                        c = Mix(c, Mul(Enamel(v, light), 0.6f), Cov(Mathf.Abs(d - (R - 4.6f)) - 0.6f) * 0.6f); // the enamel's bed
                        px[at] = c;
                    }
                RingMask(gold, W, H, cx, cy, R - 1.7f, 1.5f);
                if (v.Fine) RingMask(gold, W, H, cx, cy, R - 5.6f, 0.55f, 0.9f);
                int e = MedalEmblemPx;
                Stamp(gold, W, H, Mathf.RoundToInt(cx - e * 0.5f), Mathf.RoundToInt(cy - e * 0.5f), Emblem(v.Line, e, e, right), e, e);
            }
            Gild(px, gold, W, H, Mul(Theme.WoodDark, 0.6f));
        }

        private static void DrawCanvas(Color[] px, int W, int H, Variant v)
        {
            // ---- an accent piping line in the middle of the leather edge (the same on every side: it stretches cleanly)
            Color pipe = Mix(v.Accent, Color.white, 0.06f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (x == 16 && y >= 16 && y < H - 16) x = W - 16; // (only the leather's band is walked)
                    float fx = x + 0.5f, fy = y + 0.5f;
                    float d = -RoundRect(fx - W * 0.5f, fy - H * 0.5f, W * 0.5f, H * 0.5f, 9f);
                    float band = Cov(Mathf.Abs(d - 10.8f) - 1.5f); if (band <= 0f && Mathf.Abs(d - 10.8f) > 3.5f) continue;
                    int at = y * W + x; var c = px[at];
                    c = Mix(c, Theme.LeatherDark, 0.55f * Cov(Mathf.Abs(d - 10.8f) - 2.4f) * (1f - band)); // a dark seam on both sides
                    c = Mix(c, pipe, 0.92f * band);
                    px[at] = c;
                }
            // ---- a faint emblem in the canvas' lower corners (all four from the third job): LIGHTER than the canvas, never darker
            int m = MarkPx, at0 = 45;
            for (int corner = 0; corner < 4; corner++)
            {
                bool right = (corner & 1) != 0, top = (corner & 2) != 0;
                if (top && v.Tier < 4) continue;
                var mask = Emblem(v.Line, m, m, right);
                int x0 = right ? W - at0 - m : at0, y0 = top ? H - at0 - m : at0;
                for (int j = 0; j < m; j++)
                    for (int i = 0; i < m; i++)
                    {
                        float c = mask[j * m + i]; if (c <= 0f) continue;
                        int at = (y0 + j) * W + x0 + i;
                        px[at] = Lighter(px[at], Mix(px[at], Theme.Field, MarkStrength * c));
                    }
            }
        }
        /// <summary>How far the canvas' faint emblem goes from the canvas' cream to the lighter cream of an input box (Theme.Field).</summary>
        public const float MarkStrength = 0.55f;
        private static Color Lighter(Color a, Color b) => UiKit.Luminance(b) >= UiKit.Luminance(a) ? b : a;

        private static void DrawCard(Color[] px, int W, int H, Variant v)
        {
            float m = Theme.CardShadowPx;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float fx = x + 0.5f - W * 0.5f, fy = y + 0.5f - H * 0.5f;
                    float sd = RoundRect(fx, fy, W * 0.5f - m, H * 0.5f - m, 3.5f);
                    if (sd > 0.5f || sd < -CardTextFrom) continue; // only the rim: the paper where a text stands is the base's
                    float a = Cov(sd); int at = y * W + x; var c = px[at];
                    if (v.HasAccent && v.Tier >= 1) c = Mix(c, v.Accent, 0.62f * Cov(Mathf.Abs(sd + 0.8f) - 0.8f) * a);
                    if (v.Fine) c = Mix(c, Mix(Theme.GoldDark, Theme.Gold, 0.55f), 0.75f * Cov(Mathf.Abs(sd + 3.3f) - 0.5f));
                    px[at] = c;
                }
        }

        private static void DrawPlate(Color[] px, int W, int H, Variant v)
        {
            float hw = W * 0.5f - 1f, hh = H * 0.5f - 1f; var edge = new Color(0x4A / 255f, 0x32 / 255f, 0x09 / 255f, 1f);
            bool crest = v.Tier >= 4 && v.HasAccent, enamel = v.Tier >= 2 && v.HasAccent;
            float capTo = W * 0.5f - 22f; // the crest's inner end, from the middle (22 px from the plate's end; its divider's soft edge ends at 23.05)
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float fx = x + 0.5f - W * 0.5f, fy = y + 0.5f - H * 0.5f;
                    float sd = RoundRect(fx, fy, hw, hh, 2f);
                    float cx = Mathf.Abs(fx) - hw, cy = Mathf.Abs(fy) - hh;
                    sd = Mathf.Max(sd, 7f - Mathf.Sqrt(cx * cx + cy * cy)); // the kit's plate: its corners cut round
                    if (sd > 0.5f) continue;
                    int at = y * W + x; var c = px[at];
                    if (crest)
                    {
                        // the plate's two ends enamelled inside the engraved line, a dark divider where the gold begins again
                        float cap = Mathf.Min(Cov(sd + 6.5f), Cov(capTo - Mathf.Abs(fx)));
                        c = Mix(c, Enamel(v, Mathf.Clamp01(0.5f + fy / (2f * hh))), 0.94f * cap);
                        c = Mix(c, edge, 0.75f * Cov(Mathf.Abs(Mathf.Abs(fx) - capTo) - 0.55f) * Cov(sd + 6.5f));
                        if (v.Tier >= 5) c = Mix(c, Mix(Theme.Gold, Theme.GoldLight, 0.5f), 0.9f * Mathf.Min(Cov(Mathf.Abs(sd + 8.6f) - 0.45f), Cov(capTo + 2f - Mathf.Abs(fx)))); // a fine gold line inside the enamel (2 px inside the crest's inner end)
                    }
                    if (enamel) c = Mix(c, Mix(v.Accent, Color.white, 0.05f), 0.92f * Cov(Mathf.Abs(sd + 5.2f) - 0.95f)); // the engraved inner line, in enamel
                    px[at] = c;
                }
            // ---- the emblem on both ends: engraved (dark, a light lip down-right); on a crest in gold
            int ew = PlateEmblemW, eh = PlateEmblemH, ex0 = 7, ey0 = (H - eh) / 2;
            for (int end = 0; end < 2; end++)
            {
                var mask = Emblem(v.Line, ew, eh, end == 1);
                int x0 = end == 1 ? W - ex0 - ew : ex0;
                for (int j = 0; j < eh; j++)
                    for (int i = 0; i < ew; i++)
                    {
                        float c = mask[j * ew + i];
                        float lip = i > 0 && j < eh - 1 ? mask[(j + 1) * ew + i - 1] : 0f;
                        if (c <= 0f && lip <= 0f) continue;
                        int at = (ey0 + j) * W + x0 + i; var p = px[at]; if (p.a <= 0.01f) continue;
                        if (crest) { p = Mix(p, Mul(Enamel(v, 0f), 0.55f), 0.7f * lip * (1f - c)); p = Mix(p, Mix(Theme.Gold, Theme.GoldLight, 0.55f), c); }
                        else { p = Mix(p, Theme.GoldLight, 0.55f * lip * (1f - c)); p = Mix(p, edge, 0.9f * c); }
                        px[at] = p;
                    }
            }
        }

        private static void DrawSeeFrame(Color[] px, int W, int H, Variant v)
        {
            // ---- a hairline in the accent colour just outside the gold line (bright: it stands over the world, not over cream)
            Color line = Mix(v.Accent, Color.white, 0.35f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (x == 6 && y >= 6 && y < H - 6) x = W - 6; // (only the band along the edges is walked)
                    float fx = x + 0.5f, fy = y + 0.5f;
                    float c = Cov(Mathf.Abs(RoundRect(fx - W * 0.5f, fy - H * 0.5f, W * 0.5f - 2.7f, H * 0.5f - 2.7f, 14.3f)) - 0.85f); if (c <= 0f) continue;
                    int at = y * W + x; var p = px[at];
                    var o = Mix(p.a > 0.01f ? p : new Color(line.r, line.g, line.b, p.a), line, c);
                    o.a = Mathf.Max(p.a, 0.9f * c); px[at] = o;
                }
            if (v.Tier < 2) return;
            // ---- a tiny emblem hanging from the top line beside each top scroll
            int e = TinyPx; var dark = Mul(Theme.WoodDark, 0.6f); var gold = Mix(Theme.Gold, Theme.GoldLight, 0.4f);
            for (int end = 0; end < 2; end++)
            {
                var mask = Emblem(v.Line, e, e, end == 1);
                int x0 = end == 1 ? W - 36 - e : 36, y0 = H - 9 - e; // (with its dark edge inside the corner zone of 46 px: nothing of it is stretched)
                for (int j = -1; j <= e; j++)
                    for (int i = -1; i <= e; i++)
                    {
                        float c = i >= 0 && i < e && j >= 0 && j < e ? mask[j * e + i] : 0f, halo = 0f;
                        for (int b = -1; b <= 1; b++)
                            for (int a = -1; a <= 1; a++)
                            {
                                int ii = i + a, jj = j + b;
                                if (ii >= 0 && ii < e && jj >= 0 && jj < e && mask[jj * e + ii] > halo) halo = mask[jj * e + ii];
                            }
                        if (halo <= 0f) continue;
                        int at = (y0 + j) * W + x0 + i; var p = px[at];
                        var o = p.a > 0.01f ? p : new Color(dark.r, dark.g, dark.b, 0f);
                        float a0 = o.a; o = Mix(o, dark, a0 > 0.01f ? 0.55f * halo * (1f - c) : 1f); o = Mix(o, gold, c);
                        o.a = Mathf.Max(a0, Mathf.Max(c, 0.55f * halo)); px[at] = o;
                    }
            }
        }
    }
}
