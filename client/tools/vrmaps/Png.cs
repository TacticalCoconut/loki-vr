using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;

namespace Loki.VrMaps
{
    /// <summary>The report's top-down picture (plan section 3): 4 pixels per GAT cell, north up; the drawn ground shaded by height (dark
    /// blue where nothing is drawn), walkable cells lighter, every finding's cells in its kind's colour (high opaque, medium 70 %, low
    /// 40 %; info not drawn). GDI+ of the installed Windows Desktop runtime.</summary>
    public static class Png
    {
        public static readonly Dictionary<string, Color> Colours = new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["fp.head_clip"] = Color.FromArgb(230, 40, 40), ["fp.low_ceiling"] = Color.FromArgb(255, 140, 0), ["fp.floor_gap"] = Color.FromArgb(220, 0, 220),
            ["fp.underwater"] = Color.FromArgb(30, 90, 255), ["walk.invisible_wall"] = Color.FromArgb(255, 235, 0), ["walk.walk_through"] = Color.FromArgb(0, 230, 230),
            ["walk.wall_on_path"] = Color.FromArgb(150, 60, 255), ["void.hole"] = Color.FromArgb(255, 255, 255), ["water.gat_mismatch"] = Color.FromArgb(0, 160, 140),
            ["sound.through_rooms"] = Color.FromArgb(255, 120, 180), ["room.open_ceiling"] = Color.FromArgb(160, 110, 60), ["room.low_walls"] = Color.FromArgb(140, 160, 40),
            ["light.popping"] = Color.FromArgb(255, 200, 80), ["void.edge_seen"] = Color.FromArgb(120, 200, 255), ["back.model_face"] = Color.FromArgb(255, 80, 120),
            ["back.gnd_wall"] = Color.FromArgb(200, 255, 120), ["void.hole_through_wall"] = Color.FromArgb(170, 170, 200), ["view.popin"] = Color.FromArgb(180, 180, 255), ["follow.head_clip"] = Color.FromArgb(255, 60, 160), ["flat.card"] = Color.FromArgb(255, 170, 255),
        };

        public static void Write(string path, Scene s, MapReport r)
        {
            const int K = 4;
            int W = s.W, H = s.H;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < W * H; i++) { lo = Math.Min(lo, s.Floor[i]); hi = Math.Max(hi, s.Floor[i]); }
            float span = Math.Max(1e-3f, hi - lo);
            var px = new int[W * K * H * K];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = x + y * W, tx = x / 2, ty = y / 2;
                    bool drawn = tx < s.Gnd.Width && ty < s.Gnd.Height && s.TopDrawn[tx + ty * s.Gnd.Width];
                    int c;
                    if (!drawn) c = Argb(255, 10, 14, 40);
                    else
                    {
                        int g = 50 + (int)(110 * (s.Floor[i] - lo) / span);
                        c = s.Walk[i] ? Argb(255, g + 40, g + 70, g + 40) : Argb(255, g, g, g);
                    }
                    Fill(px, W * K, x, H - 1 - y, K, c);
                }
            foreach (var f in r.Findings.OrderBy(f => -MapReport.Rank(f.Severity)))
            {
                if (f.Region?.Runs == null || f.Severity == "info" || !Colours.TryGetValue(f.Kind, out var col)) continue;
                int a = f.Severity == "high" ? 255 : f.Severity == "medium" ? 180 : 100;
                foreach (var c in f.Region.Cells())
                {
                    int x = c[0], y = c[1]; if (x < 0 || y < 0 || x >= W || y >= H) continue;
                    for (int dy = 0; dy < K; dy++) for (int dx = 0; dx < K; dx++)
                    {
                        int j = (x * K + dx) + ((H - 1 - y) * K + dy) * W * K;
                        px[j] = Blend(px[j], col, a);
                    }
                }
            }
            using (var bmp = new Bitmap(W * K, H * K, PixelFormat.Format32bppArgb))
            {
                var bd = bmp.LockBits(new Rectangle(0, 0, W * K, H * K), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                System.Runtime.InteropServices.Marshal.Copy(px, 0, bd.Scan0, px.Length);
                bmp.UnlockBits(bd);
                bmp.Save(path, ImageFormat.Png);
            }
        }

        private static int Argb(int a, int r, int g, int b) => (a << 24) | (Math.Min(255, r) << 16) | (Math.Min(255, g) << 8) | Math.Min(255, b);
        private static void Fill(int[] px, int stride, int cx, int cy, int k, int c) { for (int dy = 0; dy < k; dy++) for (int dx = 0; dx < k; dx++) px[(cx * k + dx) + (cy * k + dy) * stride] = c; }
        private static int Blend(int under, Color c, int a)
        {
            int ur = (under >> 16) & 255, ug = (under >> 8) & 255, ub = under & 255;
            return Argb(255, (c.R * a + ur * (255 - a)) / 255, (c.G * a + ug * (255 - a)) / 255, (c.B * a + ub * (255 - a)) / 255);
        }
    }
}
