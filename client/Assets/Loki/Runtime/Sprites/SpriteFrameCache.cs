using System.Collections.Generic;
using Loki.Formats;
using UnityEngine;

namespace Loki.Sprites
{
    /// <summary>
    /// Per-frame GPU resources for SPR files: the RGBA texture (palette applied) and the "relief" mesh —
    /// a grid over the frame, inflated along -z by an alpha-distance field so units read as solid cut-outs
    /// with rounded thickness instead of paper, with normals for lighting.
    /// </summary>
    public static class SpriteFrameCache
    {
        public sealed class FrameGpu
        {
            public Texture2D Texture; public Mesh Mesh; public int Width, Height; public float DepthPx; public Color32[] Pixels; /* top-down */
            // Texture and Mesh are set for composites only (GetComposite); single frames from Get carry pixels only.
        }

        // Keyed by the palette array itself (reference equality): palettes come from RoAssets.Palette's per-path cache.
        private static readonly Dictionary<(SprFile, int, int, Color32[]), FrameGpu> _cache = new Dictionary<(SprFile, int, int, Color32[]), FrameGpu>();

        /// <summary>Pixels between mesh vertices (2 = fine, 4 = cheap).</summary>
        public static int GridStep = 2;
        /// <summary>Maximum inflation depth as a fraction of the smaller frame dimension.</summary>
        public static float ReliefDepth = 0.2f;
        /// <summary>Set false for flat quads (debug / performance).</summary>
        public static bool ReliefEnabled = true;

        /// <summary>
        /// Decoded pixels of one SPR frame (top-down rows; external palette applied to indexed frames, index 0 transparent).
        /// Only the composites below become textures/meshes, so single frames carry Pixels/Width/Height only (Texture and
        /// Mesh stay null): a unit with body, hair, three headgears, weapon, trail, shield and garment touches hundreds of
        /// frames, and building a relief mesh per frame was wasted work.
        /// </summary>
        public static FrameGpu Get(SprFile spr, int spriteNumber, int spriteType, Color32[] paletteOverride = null)
        {
            // An external palette only affects indexed frames; RGBA frames never use one (rb EntityRender.js:1108-1122).
            if (spriteType == 1) paletteOverride = null;
            var key = (spr, spriteNumber, spriteType, paletteOverride);
            if (_cache.TryGetValue(key, out var g)) return g;
            var frame = spr.GetFrame(spriteNumber, spriteType);
            var pal = paletteOverride ?? spr.Palette;
            int w = frame.Width, h = frame.Height;
            Color32[] px;
            if (frame.Indexed != null)
            {
                px = new Color32[w * h];
                for (int i = 0; i < px.Length; i++)
                {
                    byte idx = frame.Indexed[i];
                    px[i] = idx == 0 ? new Color32(0, 0, 0, 0) : pal != null ? pal[idx] : new Color32(idx, idx, idx, 255); // SPR < 1.1 has no palette
                }
            }
            else px = frame.Rgba ?? new Color32[w * h];
            g = new FrameGpu { Width = w, Height = h, DepthPx = ReliefEnabled ? ReliefDepth * Mathf.Min(w, h) : 0f, Pixels = px };
            _cache[key] = g;
            return g;
        }

        private static readonly Dictionary<string, FrameGpu> _composites = new Dictionary<string, FrameGpu>();

        /// <summary>A layer to composite: source frame, placement of its centre in pixel space (x right, y down), mirror, tint,
        /// ACT scale (0/0 = unset, treated as 1) and rotation in degrees (clockwise on screen).</summary>
        public struct CompositeLayer { public FrameGpu Frame; public int X, Y; public bool Mirror; public Color32 Color; public float ScaleX, ScaleY; public int Angle; }

        private static bool IsPlain(in CompositeLayer l) =>
            ((l.ScaleX == 0f && l.ScaleY == 0f) || (l.ScaleX == 1f && l.ScaleY == 1f)) && l.Angle % 360 == 0;

        /// <summary>Pixel bounds [x0, x1) × [y0, y1) of a layer after scale/rotation.</summary>
        private static void Bounds(in CompositeLayer l, out int x0, out int y0, out int x1, out int y1)
        {
            int w = l.Frame.Width, h = l.Frame.Height;
            if (IsPlain(l)) { x0 = l.X - w / 2; y0 = l.Y - h / 2; x1 = x0 + w; y1 = y0 + h; return; }
            // zrenderer transformOfSprite: translate(x, y) · rotate(angle) · scale(sx·mirror, sy) · translate(−size/2)
            float sx = l.Mirror ? -l.ScaleX : l.ScaleX, sy = l.ScaleY;
            float rad = l.Angle * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            int ox = w / 2, oy = h / 2;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int c = 0; c < 4; c++)
            {
                float u = ((c & 1) == 0 ? -ox : w - ox) * sx, v = ((c & 2) == 0 ? -oy : h - oy) * sy;
                float rx = u * cos - v * sin, ry = u * sin + v * cos;
                minX = Mathf.Min(minX, rx); maxX = Mathf.Max(maxX, rx); minY = Mathf.Min(minY, ry); maxY = Mathf.Max(maxY, ry);
            }
            x0 = l.X + Mathf.FloorToInt(minX); y0 = l.Y + Mathf.FloorToInt(minY);
            x1 = l.X + Mathf.CeilToInt(maxX); y1 = l.Y + Mathf.CeilToInt(maxY);
        }

        /// <summary>"Over" blend of c onto px[i] (opaque pixels simply replace: painter's order).</summary>
        private static void Put(Color32[] px, int i, Color32 c)
        {
            if (c.a == 255) { px[i] = c; return; }
            var d = px[i];
            if (d.a == 0) { px[i] = c; return; }
            float a = c.a / 255f, da = d.a / 255f * (1f - a), oa = a + da;
            px[i] = new Color32((byte)((c.r * a + d.r * da) / oa), (byte)((c.g * a + d.g * da) / oa), (byte)((c.b * a + d.b * da) / oa), (byte)Mathf.Clamp(Mathf.RoundToInt(oa * 255f), 0, 255));
        }

        private static Color32 Tinted(Color32 c, Color32 t)
        {
            if (t.r == 255 && t.g == 255 && t.b == 255 && t.a == 255) return c;
            return new Color32((byte)(c.r * t.r / 255), (byte)(c.g * t.g / 255), (byte)(c.b * t.b / 255), (byte)(c.a * t.a / 255));
        }

        /// <summary>
        /// Composites several sprite layers into one image (so body + head + gear become ONE solid relief) and caches
        /// texture + mesh by key. Layers are drawn in list order (painter's order, back to front). Returns the composite and its
        /// centre offset (pixels) relative to the actor origin.
        /// </summary>
        public static FrameGpu GetComposite(string key, List<CompositeLayer> layers, out Vector2Int centerPx)
        {
            centerPx = Vector2Int.zero;
            if (!CompositeBounds(layers, out int minX, out int minY, out int w, out int h)) return null;
            centerPx = new Vector2Int(minX + w / 2, minY + h / 2);
            if (_composites.TryGetValue(key, out var g)) return g;
            Color32[] px = null, flipped = null; MeshData md = default;
            if (_prepared.TryGetValue(key, out var p))
            {
                // (asked for before its background work is done: built here at once as before, that work's result dropped)
                _prepared.Remove(key);
                if (p.Done && p.Px != null && p.W == w && p.H == h) { px = p.Px; flipped = p.Flipped; md = p.Mesh; PreparedUsed++; }
            }
            if (px == null) { px = ComposePixels(layers, minX, minY, w, h); flipped = Flipped(px, w, h); md = BuildMeshData(px, w, h, ReliefEnabled, GridStep, ReliefDepth); }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "composite" };
            tex.SetPixels32(flipped); tex.Apply(false, false);
            g = new FrameGpu { Texture = tex, Width = w, Height = h, Mesh = MakeMesh(md), DepthPx = ReliefEnabled ? ReliefDepth * Mathf.Min(w, h) : 0f, Pixels = px };
            _composites[key] = g;
            return g;
        }

        private static Color32[] Flipped(Color32[] px, int w, int h)
        {
            var flipped = new Color32[px.Length];
            for (int y = 0; y < h; y++) System.Array.Copy(px, y * w, flipped, (h - 1 - y) * w, w);
            return flipped;
        }

        // ---- composites prepared off the main thread (client wave 18 fix, h18stutter) ----
        // Horde's sprite pre-build (HordeSprites.PrewarmSlice) makes ~18 500 composites before a run. Their pixel and relief work
        // (ComposePixels, the distance field, the blur, the grid: plain arrays, no Unity object) took 14.4 s on the main thread: more
        // than the countdown, so the rest was built inside the run's first frames (measured: design notes). Prepare
        // hands that work to PrepareThreads background threads; GetComposite then only makes the Texture2D and the Mesh from the
        // finished arrays.
        /// <summary>Background threads for <see cref="Prepare"/> (Loki's own number; below normal priority).</summary>
        public const int PrepareThreads = 4;
        /// <summary>Composites whose prepared arrays <see cref="GetComposite"/> used.</summary>
        public static int PreparedUsed { get; private set; }
        private struct MeshData { public Vector3[] Verts, Norms; public Vector2[] Uvs; public int[] Tris; }
        private sealed class Prepared
        {
            public CompositeLayer[] Layers; public int MinX, MinY, W, H; public bool Relief; public int Step; public float Depth;
            public Color32[] Px, Flipped; public MeshData Mesh; public volatile bool Done;
        }
        private static readonly Dictionary<string, Prepared> _prepared = new Dictionary<string, Prepared>(); // (the main thread's alone)
        private static System.Collections.Concurrent.BlockingCollection<Prepared> _jobs;

        /// <summary>Starts a composite's pixel and mesh work on a background thread (call from the main thread; the layers are copied).
        /// False when there is nothing to draw. <see cref="GetComposite"/> with the same key and layers finishes it.</summary>
        public static bool Prepare(string key, List<CompositeLayer> layers)
        {
            if (_composites.ContainsKey(key) || _prepared.ContainsKey(key)) return true;
            if (!CompositeBounds(layers, out int minX, out int minY, out int w, out int h)) return false;
            if (_jobs == null)
            {
                _jobs = new System.Collections.Concurrent.BlockingCollection<Prepared>();
                for (int i = 0; i < PrepareThreads; i++)
                    new System.Threading.Thread(PrepareLoop) { IsBackground = true, Name = "loki-sprite-compose", Priority = System.Threading.ThreadPriority.BelowNormal }.Start();
            }
            var p = new Prepared { Layers = layers.ToArray(), MinX = minX, MinY = minY, W = w, H = h, Relief = ReliefEnabled, Step = GridStep, Depth = ReliefDepth };
            _prepared[key] = p; _jobs.Add(p);
            return true;
        }

        /// <summary>True while a prepared composite's background work is not done yet.</summary>
        public static bool PreparePending(string key) => _prepared.TryGetValue(key, out var p) && !p.Done;

        private static void PrepareLoop()
        {
            foreach (var p in _jobs.GetConsumingEnumerable())
            {
                try
                {
                    var px = ComposePixels(p.Layers, p.MinX, p.MinY, p.W, p.H);
                    p.Flipped = Flipped(px, p.W, p.H); p.Mesh = BuildMeshData(px, p.W, p.H, p.Relief, p.Step, p.Depth); p.Px = px;
                }
                catch (System.Exception) { p.Px = null; } // (the main thread then builds it itself, and reports what fails)
                p.Done = true;
            }
        }

        /// <summary>Union of the layers' pixel bounds (origin = actor feet, y down). False when empty.</summary>
        public static bool CompositeBounds(List<CompositeLayer> layers, out int minX, out int minY, out int w, out int h)
        {
            minX = int.MaxValue; minY = int.MaxValue; int maxX = int.MinValue, maxY = int.MinValue;
            foreach (var l in layers)
            {
                Bounds(l, out int x0, out int y0, out int x1, out int y1);
                minX = Mathf.Min(minX, x0); minY = Mathf.Min(minY, y0);
                maxX = Mathf.Max(maxX, x1); maxY = Mathf.Max(maxY, y1);
            }
            w = layers.Count == 0 ? 0 : maxX - minX; h = layers.Count == 0 ? 0 : maxY - minY;
            return w > 0 && h > 0;
        }

        /// <summary>CPU compositing (top-down rows, w × h, pixel (0,0) = (minX, minY)): each layer in list order is mirrored,
        /// scaled and rotated about its centre (nearest neighbour), tinted and drawn over the previous ones.</summary>
        public static Color32[] ComposePixels(IReadOnlyList<CompositeLayer> layers, int minX, int minY, int w, int h)
        {
            var px = new Color32[w * h];
            foreach (var l in layers)
            {
                int fw = l.Frame.Width, fh = l.Frame.Height;
                var src = l.Frame.Pixels;
                if (IsPlain(l))
                {
                    int x0 = l.X - fw / 2 - minX, y0 = l.Y - fh / 2 - minY;
                    for (int y = 0; y < fh; y++)
                        for (int x = 0; x < fw; x++)
                        {
                            int sx = l.Mirror ? fw - 1 - x : x;
                            var c = Tinted(src[sx + y * fw], l.Color);
                            if (c.a < 8) continue;
                            Put(px, (x0 + x) + (y0 + y) * w, c);
                        }
                    continue;
                }
                // Scaled / rotated layer: inverse-map every destination pixel centre (nearest neighbour).
                Bounds(l, out int bx0, out int by0, out int bx1, out int by1);
                float scx = l.Mirror ? -l.ScaleX : l.ScaleX, scy = l.ScaleY;
                if (scx == 0f || scy == 0f) continue; // degenerate (zero-width) layer draws nothing
                float rad = l.Angle * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
                int ox = fw / 2, oy = fh / 2;
                for (int dy = by0; dy < by1; dy++)
                    for (int dx = bx0; dx < bx1; dx++)
                    {
                        float qx = dx + 0.5f - l.X, qy = dy + 0.5f - l.Y;
                        float ux = (qx * cos + qy * sin) / scx, uy = (-qx * sin + qy * cos) / scy; // R(−angle), then 1/scale
                        int sxi = Mathf.FloorToInt(ux + ox), syi = Mathf.FloorToInt(uy + oy);
                        if (sxi < 0 || syi < 0 || sxi >= fw || syi >= fh) continue;
                        var c = Tinted(src[sxi + syi * fw], l.Color);
                        if (c.a < 8) continue;
                        Put(px, (dx - minX) + (dy - minY) * w, c);
                    }
            }
            return px;
        }

        /// <summary>Grid mesh in pixel units, centred on the frame, xy in the frame plane (y up), z ≤ 0 = towards the viewer: its arrays
        /// (no Unity object, so any thread may run it; the relief's settings are passed in).</summary>
        private static MeshData BuildMeshData(Color32[] px, int w, int h, bool relief, int gridStep, float reliefDepth)
        {
            if (w == 0 || h == 0) return default;
            int step = relief ? Mathf.Max(1, gridStep) : Mathf.Max(w, h);
            int gx = Mathf.CeilToInt(w / (float)step) + 1, gy = Mathf.CeilToInt(h / (float)step) + 1;
            float maxDepth = relief ? reliefDepth * Mathf.Min(w, h) : 0f;

            // Alpha-distance field (chamfer 3-4), in pixels, 0 on transparent pixels.
            float[] dist = null;
            if (maxDepth > 0)
            {
                dist = new float[w * h];
                const float INF = 1e6f;
                for (int i = 0; i < dist.Length; i++) dist[i] = px[i].a > 8 ? INF : 0f;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = x + y * w; float d = dist[i];
                        if (x == 0 || y == 0 || x == w - 1 || y == h - 1) d = Mathf.Min(d, 1f); // frame border counts as edge
                        if (x > 0) d = Mathf.Min(d, dist[i - 1] + 1f);
                        if (y > 0) d = Mathf.Min(d, dist[i - w] + 1f);
                        if (x > 0 && y > 0) d = Mathf.Min(d, dist[i - w - 1] + 1.4142f);
                        if (x < w - 1 && y > 0) d = Mathf.Min(d, dist[i - w + 1] + 1.4142f);
                        dist[i] = d;
                    }
                for (int y = h - 1; y >= 0; y--)
                    for (int x = w - 1; x >= 0; x--)
                    {
                        int i = x + y * w; float d = dist[i];
                        if (x < w - 1) d = Mathf.Min(d, dist[i + 1] + 1f);
                        if (y < h - 1) d = Mathf.Min(d, dist[i + w] + 1f);
                        if (x < w - 1 && y < h - 1) d = Mathf.Min(d, dist[i + w + 1] + 1.4142f);
                        if (x > 0 && y < h - 1) d = Mathf.Min(d, dist[i + w - 1] + 1.4142f);
                        dist[i] = d;
                    }
            }
            // Height field = pillow profile of the distance, then a small box blur so the silhouette slope is smooth.
            float[] hf = null;
            if (dist != null)
            {
                hf = new float[w * h];
                for (int i = 0; i < hf.Length; i++)
                {
                    float d = Mathf.Min(dist[i], maxDepth);
                    float t = d / maxDepth;
                    hf[i] = maxDepth * Mathf.Sqrt(1f - (1f - t) * (1f - t));
                }
                var tmp = new float[w * h];
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            float sum = 0; int cnt = 0;
                            for (int dy = -1; dy <= 1; dy++)
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    int xx = x + dx, yy = y + dy;
                                    if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                                    sum += hf[xx + yy * w]; cnt++;
                                }
                            tmp[x + y * w] = sum / cnt;
                        }
                    var sw = hf; hf = tmp; tmp = sw;
                }
            }
            float Height(float fx, float fy)
            {
                if (hf == null) return 0f;
                int x = Mathf.Clamp(Mathf.RoundToInt(fx), 0, w - 1), y = Mathf.Clamp(Mathf.RoundToInt(fy), 0, h - 1);
                return hf[x + y * w];
            }

            var verts = new Vector3[gx * gy]; var uvs = new Vector2[gx * gy]; var norms = new Vector3[gx * gy];
            for (int j = 0; j < gy; j++)
                for (int i = 0; i < gx; i++)
                {
                    float fx = Mathf.Min(i * step, w), fy = Mathf.Min(j * step, h); // fy: 0 = top row
                    float z = -Height(fx - 0.5f, fy - 0.5f);
                    verts[i + j * gx] = new Vector3(fx - w * 0.5f, (h - fy) - h * 0.5f, z);
                    uvs[i + j * gx] = new Vector2(fx / w, 1f - fy / h);
                    // normal from height gradient (finite differences in pixel space); +z is away from the viewer, so bulge is -z
                    float hx = Height(fx + 1f, fy - 0.5f) - Height(fx - 2f, fy - 0.5f);
                    float hy = Height(fx - 0.5f, fy + 1f) - Height(fx - 0.5f, fy - 2f);
                    // surface z = -H(x, y_img); world y = -y_img. n = (dH/dx, -dH/dy_img, -1) normalised → pointing towards viewer (-z)
                    norms[i + j * gx] = new Vector3(hx / 3f, -hy / 3f, -1f).normalized;
                }
            var tris = new int[(gx - 1) * (gy - 1) * 6];
            int t3 = 0;
            for (int j = 0; j < gy - 1; j++)
                for (int i = 0; i < gx - 1; i++)
                {
                    int a = i + j * gx, b = a + 1, c = a + gx, d = c + 1;
                    // Facing -z (towards viewer): clockwise when seen from -z.
                    tris[t3++] = a; tris[t3++] = b; tris[t3++] = d;
                    tris[t3++] = a; tris[t3++] = d; tris[t3++] = c;
                }
            return new MeshData { Verts = verts, Uvs = uvs, Norms = norms, Tris = tris };
        }

        private static Mesh MakeMesh(in MeshData d)
        {
            var mesh = new Mesh { name = "spriteRelief" };
            if (d.Verts == null) return mesh;
            mesh.indexFormat = d.Verts.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = d.Verts; mesh.uv = d.Uvs; mesh.normals = d.Norms; mesh.triangles = d.Tris;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
