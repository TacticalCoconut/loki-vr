using System.Collections.Generic;
using Loki.Formats;
using UnityEngine;

namespace Loki.World
{
    /// <summary>Turns a GND into Unity meshes (one submesh per ground texture) plus a lightmap atlas.</summary>
    public static class GroundMeshBuilder
    {
        public sealed class Result
        {
            public Mesh Mesh;                 // submesh i uses Textures[i]
            public List<string> Textures;
            public Texture2D LightmapAtlas;   // rgb = colour, a = shadow
            public Vector3[] TopNormals;      // debug
            public int Culled;                // surfaces left out by the skip predicate
            public int CulledTinted;          // of those, top surfaces tinted black by their tile colour
            /// <summary>Architect's fix (World/MapFix.cs), when one was given: what it changed, and its black hole fills / skirts (a
            /// separate mesh, drawn with its own black material; null when the fix fills nothing).</summary>
            public Mesh VoidMesh;
            public int FixBoth, FixFlipped, FixTops, FixFills, FixSkirts;
        }

        private struct Buf
        {
            public List<Vector3> V; public List<Vector3> N; public List<Vector2> UV; public List<Vector2> LM; public List<Color32> C;
            public List<int>[] Tris;
        }

        /// <param name="skipTexture">Surfaces whose texture index it accepts are left out of the mesh (and the collider).</param>
        /// <param name="fix">Architect's fix for this map (World/MapFix.cs), or null: the ground exactly as before. With a fix, a wall it
        /// names is drawn from both sides or from its other side, a void-filler top it names is drawn, and its hole fills go into
        /// <see cref="Result.VoidMesh"/>.</param>
        /// <param name="atlas">The lightmap atlas of an earlier build of the same GND, to share (null: made here).</param>
        public static Result Build(GndFile gnd, bool magentaKeyed = true, System.Func<int, bool> skipTexture = null, MapFix fix = null, Texture2D atlas = null)
        {
            int w = gnd.Width, h = gnd.Height;
            var res = new Result { Textures = gnd.Textures };
            int lmCols, lmRows;
            if (atlas != null) { LmGrid(gnd, out lmCols, out lmRows); res.LightmapAtlas = atlas; }
            else res.LightmapAtlas = BuildLightmapAtlas(gnd, out lmCols, out lmRows);

            var b = new Buf
            {
                V = new List<Vector3>(w * h * 6), N = new List<Vector3>(w * h * 6), UV = new List<Vector2>(w * h * 6),
                LM = new List<Vector2>(w * h * 6), C = new List<Color32>(w * h * 6), Tris = new List<int>[gnd.Textures.Count]
            };
            for (int i = 0; i < b.Tris.Length; i++) b.Tris[i] = new List<int>();

            // Smoothed per-corner normals for the top surfaces (roBrowser getSmoothNormal).
            var faceN = new Vector3[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var t = gnd.Tiles[x + y * w];
                    if (t.Top < 0) continue;
                    Vector3 p00 = P(x, y, t.H0), p10 = P(x + 1, y, t.H1), p01 = P(x, y + 1, t.H2), p11 = P(x + 1, y + 1, t.H3);
                    // quad normal = normalised cross of the diagonals (matches vec4.calcNormal up to sign)
                    var n = Vector3.Cross(p01 - p10, p11 - p00);
                    if (n.y < 0) n = -n;
                    faceN[x + y * w] = n.normalized;
                }
            Vector3 CornerN(int x, int y) // normal at grid corner (x, y): average of the up-to-4 surrounding tiles
            {
                var n = Vector3.zero;
                for (int dy = -1; dy <= 0; dy++)
                    for (int dx = -1; dx <= 0; dx++)
                    {
                        int tx = x + dx, ty = y + dy;
                        if (tx < 0 || ty < 0 || tx >= w || ty >= h) continue;
                        n += faceN[tx + ty * w];
                    }
                return n.sqrMagnitude > 0 ? n.normalized : Vector3.up;
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var a = gnd.Tiles[x + y * w];
                    if (a.Top >= 0)
                    {
                        var s = gnd.Surfaces[a.Top];
                        // Architect's fix: a void-filler top under a walkable cell is drawn as it is (black, as RO drew it)
                        bool forced = fix != null && s.TextureIndex >= 0 && fix.DrawTops.Contains(x + y * w);
                        if (forced) res.FixTops++;
                        if (!forced && s.TextureIndex >= 0 && skipTexture != null && skipTexture(s.TextureIndex)) res.Culled++;
                        // the same void trick done with the tile colour instead of the texture: a black-tinted top renders black
                        else if (!forced && s.TextureIndex >= 0 && skipTexture != null && s.R <= 8 && s.G <= 8 && s.B <= 8) { res.Culled++; res.CulledTinted++; }
                        else if (s.TextureIndex >= 0)
                        {
                            Lm(s.LightmapIndex, lmCols, lmRows, out var l0, out var l1);
                            var col = new Color32(s.R, s.G, s.B, 255);
                            Vector3 p00 = P(x, y, a.H0), p10 = P(x + 1, y, a.H1), p01 = P(x, y + 1, a.H2), p11 = P(x + 1, y + 1, a.H3);
                            AddQuad(ref b, s.TextureIndex,
                                p00, p10, p01, p11,
                                CornerN(x, y), CornerN(x + 1, y), CornerN(x, y + 1), CornerN(x + 1, y + 1),
                                UV(s.U0, s.V0), UV(s.U1, s.V1), UV(s.U2, s.V2), UV(s.U3, s.V3),
                                new Vector2(l0.x, l0.y), new Vector2(l1.x, l0.y), new Vector2(l0.x, l1.y), new Vector2(l1.x, l1.y),
                                col, Vector3.up);
                        }
                    }
                    if (a.Front >= 0 && y + 1 < h)
                    {
                        var s = gnd.Surfaces[a.Front];
                        var bb = gnd.Tiles[x + (y + 1) * w];
                        if (s.TextureIndex >= 0 && skipTexture != null && skipTexture(s.TextureIndex)) res.Culled++;
                        else if (s.TextureIndex >= 0)
                        {
                            Lm(s.LightmapIndex, lmCols, lmRows, out var l0, out var l1);
                            var col = new Color32(255, 255, 255, 255);
                            // A=(x, ha2, y+1) B=(x+1, ha3, y+1) C=(x, hb0, y+1) D=(x+1, hb1, y+1)
                            Vector3 A = P(x, y + 1, a.H2), B = P(x + 1, y + 1, a.H3), C = P(x, y + 1, bb.H0), D = P(x + 1, y + 1, bb.H1);
                            // Architect's fix: this wall from both sides (an interior wall) or from its south side only
                            var mode = fix != null ? fix.FrontMode(x + y * w) : MapFix.WallMode.Keep;
                            for (int side = 0; side < 2; side++)
                            {
                                if (side == 0 ? mode == MapFix.WallMode.Flip : mode == MapFix.WallMode.Keep || mode == MapFix.WallMode.Front) continue; // the game's face unless turned; the other face when doubled or turned
                                var n = side == 0 ? Vector3.forward : Vector3.back;
                                AddQuad(ref b, s.TextureIndex, A, B, C, D, n, n, n, n,
                                    UV(s.U0, s.V0), UV(s.U1, s.V1), UV(s.U2, s.V2), UV(s.U3, s.V3),
                                    new Vector2(l0.x, l0.y), new Vector2(l1.x, l0.y), new Vector2(l0.x, l1.y), new Vector2(l1.x, l1.y),
                                    col, null, mode != MapFix.WallMode.Keep);
                            }
                            if (mode == MapFix.WallMode.Both) res.FixBoth++; else if (mode == MapFix.WallMode.Flip || mode == MapFix.WallMode.Front) res.FixFlipped++;
                        }
                    }
                    if (a.Right >= 0 && x + 1 < w)
                    {
                        var s = gnd.Surfaces[a.Right];
                        var bb = gnd.Tiles[x + 1 + y * w];
                        if (s.TextureIndex >= 0 && skipTexture != null && skipTexture(s.TextureIndex)) res.Culled++;
                        else if (s.TextureIndex >= 0)
                        {
                            Lm(s.LightmapIndex, lmCols, lmRows, out var l0, out var l1);
                            var col = new Color32(255, 255, 255, 255);
                            // A=(x+1, ha1, y) uv1 lm(u2,v1); B=(x+1, ha3, y+1) uv0 lm(u1,v1); C=(x+1, hb0, y) uv3 lm(u2,v2); D=(x+1, hb2, y+1) uv2 lm(u1,v2)
                            Vector3 A = P(x + 1, y, a.H1), B = P(x + 1, y + 1, a.H3), C = P(x + 1, y, bb.H0), D = P(x + 1, y + 1, bb.H2);
                            // Architect's fix: this wall from both sides (an interior wall) or from its west side only
                            var mode = fix != null ? fix.RightMode(x + y * w) : MapFix.WallMode.Keep;
                            for (int side = 0; side < 2; side++)
                            {
                                if (side == 0 ? mode == MapFix.WallMode.Flip : mode == MapFix.WallMode.Keep || mode == MapFix.WallMode.Front) continue; // the game's face unless turned; the other face when doubled or turned
                                var n = side == 0 ? Vector3.right : Vector3.left;
                                AddQuad(ref b, s.TextureIndex, A, B, C, D, n, n, n, n,
                                    UV(s.U1, s.V1), UV(s.U0, s.V0), UV(s.U3, s.V3), UV(s.U2, s.V2),
                                    new Vector2(l1.x, l0.y), new Vector2(l0.x, l0.y), new Vector2(l1.x, l1.y), new Vector2(l0.x, l1.y),
                                    col, null, mode != MapFix.WallMode.Keep);
                            }
                            if (mode == MapFix.WallMode.Both) res.FixBoth++; else if (mode == MapFix.WallMode.Flip || mode == MapFix.WallMode.Front) res.FixFlipped++;
                        }
                    }
                }
            }

            var mesh = new Mesh { name = "ground", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(b.V);
            mesh.SetNormals(b.N);
            mesh.SetUVs(0, b.UV);
            mesh.SetUVs(1, b.LM);
            mesh.SetColors(b.C);
            mesh.subMeshCount = b.Tris.Length;
            for (int i = 0; i < b.Tris.Length; i++) mesh.SetTriangles(b.Tris[i], i, true);
            mesh.RecalculateBounds();
            res.Mesh = mesh;
            res.TopNormals = faceN;
            if (fix != null && fix.Fills.Count > 0) res.VoidMesh = BuildFills(gnd, fix, skipTexture, res);
            return res;
        }

        /// <summary>
        /// Architect's hole fills (World/MapFix.cs "F" lines): a black floor over each named tile at the fix's height, facing up, and a
        /// black skirt (a wall face, level normal: the shader's cutaway treats it as a wall) from that floor up to the edge of every
        /// neighbouring tile whose top is drawn and stands higher, where no drawn GND wall already closes the gap. Black: RO showed black
        /// where nothing was drawn on these maps (they have no sky of their own); the map loader draws this mesh unlit.
        /// </summary>
        private static Mesh BuildFills(GndFile gnd, MapFix fix, System.Func<int, bool> skip, Result res)
        {
            int w = gnd.Width, h = gnd.Height;
            bool TopDrawn(int tx, int ty)
            {
                if (tx < 0 || ty < 0 || tx >= w || ty >= h) return false;
                var t = gnd.Tiles[tx + ty * w]; if (t.Top < 0) return false;
                if (fix.DrawTops.Contains(tx + ty * w)) return true;
                var s = gnd.Surfaces[t.Top];
                if (s.TextureIndex < 0) return false;
                if (skip != null && (skip(s.TextureIndex) || (s.R <= 8 && s.G <= 8 && s.B <= 8))) return false;
                return true;
            }
            bool WallDrawn(int surface)
            {
                if (surface < 0 || surface >= gnd.Surfaces.Length) return false;
                var s = gnd.Surfaces[surface];
                return s.TextureIndex >= 0 && (skip == null || !skip(s.TextureIndex));
            }
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color32>(); var tris = new List<int>();
            var black = new Color32(0, 0, 0, 255);
            void Quad(Vector3 a, Vector3 bq, Vector3 cq, Vector3 d, Vector3 normal)
            {
                int i0 = v.Count;
                v.Add(a); v.Add(bq); v.Add(cq); v.Add(d);
                for (int k = 0; k < 4; k++) { n.Add(normal); c.Add(black); }
                var face = Vector3.Cross(cq - a, d - a);
                if (Vector3.Dot(face, normal) >= 0) { tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 3); tris.Add(i0); tris.Add(i0 + 3); tris.Add(i0 + 1); }
                else { tris.Add(i0); tris.Add(i0 + 3); tris.Add(i0 + 2); tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 3); }
            }
            foreach (var kv in fix.Fills)
            {
                int tx = kv.Key % w, ty = kv.Key / w; if (ty >= h) continue;
                float fy = kv.Value;
                float x0 = tx * 2f, x1 = x0 + 2f, z0 = ty * 2f, z1 = z0 + 2f;
                Quad(new Vector3(x0, fy, z0), new Vector3(x1, fy, z0), new Vector3(x0, fy, z1), new Vector3(x1, fy, z1), Vector3.up);
                res.FixFills++;
                var t = gnd.Tiles[kv.Key];
                void Skirt(float roH0, float roH1, Vector3 e0, Vector3 e1, Vector3 facing)
                {
                    float y0 = -roH0 * RoSpace.Scale, y1 = -roH1 * RoSpace.Scale;
                    if (y0 <= fy + 1e-3f && y1 <= fy + 1e-3f) return; // the neighbour's edge is not above the fill: nothing to close
                    Quad(new Vector3(e0.x, fy, e0.z), new Vector3(e1.x, fy, e1.z), new Vector3(e0.x, Mathf.Max(y0, fy), e0.z), new Vector3(e1.x, Mathf.Max(y1, fy), e1.z), facing);
                    res.FixSkirts++;
                }
                // the four edges: the drawn neighbour's edge corners (RO heights), the edge's two ends, the skirt facing the hole
                if (TopDrawn(tx, ty + 1) && !WallDrawn(t.Front)) Skirt(gnd.Tiles[tx + (ty + 1) * w].H0, gnd.Tiles[tx + (ty + 1) * w].H1, new Vector3(x0, 0, z1), new Vector3(x1, 0, z1), Vector3.back);
                if (TopDrawn(tx, ty - 1) && !WallDrawn(gnd.Tiles[tx + (ty - 1) * w].Front)) Skirt(gnd.Tiles[tx + (ty - 1) * w].H2, gnd.Tiles[tx + (ty - 1) * w].H3, new Vector3(x0, 0, z0), new Vector3(x1, 0, z0), Vector3.forward);
                if (TopDrawn(tx + 1, ty) && !WallDrawn(t.Right)) Skirt(gnd.Tiles[tx + 1 + ty * w].H0, gnd.Tiles[tx + 1 + ty * w].H2, new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), Vector3.left);
                if (TopDrawn(tx - 1, ty) && !WallDrawn(gnd.Tiles[tx - 1 + ty * w].Right)) Skirt(gnd.Tiles[tx - 1 + ty * w].H1, gnd.Tiles[tx - 1 + ty * w].H3, new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), Vector3.right);
            }
            if (v.Count == 0) return null;
            var mesh = new Mesh { name = "ground-fix-fills", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetColors(c);
            var uv = new List<Vector2>(v.Count); for (int k = 0; k < v.Count; k++) uv.Add(Vector2.zero);
            mesh.SetUVs(0, uv); mesh.SetUVs(1, uv);
            mesh.subMeshCount = 1; mesh.SetTriangles(tris, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Builds a downward-facing ceiling mesh for open-top indoor GND rooms (prt_in, payon_in01, geffen_in, new_zone01, etc.).
        /// Each 4-connected room component of drawn floor tiles gets a ceiling at the 75th-percentile height of its surrounding GND walls,
        /// textured and lightmapped to match the room's tiles. Because its normals point down (Vector3.down), LokiGround.shader renders it
        /// when looking up from underneath and clips/culls it when the camera is at or above the ceiling height.
        /// </summary>
        public static Mesh BuildIndoorCeiling(GndFile gnd, System.Func<int, bool> skipTexture = null)
        {
            int w = gnd.Width, h = gnd.Height;
            if (w <= 0 || h <= 0 || gnd.Textures.Count == 0) return null;
            LmGrid(gnd, out int lmCols, out int lmRows);
            bool FloorDrawn(int tx, int ty, out GndFile.Tile tile, out GndFile.Surface surf, out float fy)
            {
                tile = default; surf = default; fy = 0f;
                if (tx < 0 || ty < 0 || tx >= w || ty >= h) return false;
                tile = gnd.Tiles[tx + ty * w];
                if (tile.Top < 0 || tile.Top >= gnd.Surfaces.Length) return false;
                surf = gnd.Surfaces[tile.Top];
                if (surf.TextureIndex < 0 || surf.TextureIndex >= gnd.Textures.Count) return false;
                if (skipTexture != null && skipTexture(surf.TextureIndex)) return false;
                if (surf.R <= 8 && surf.G <= 8 && surf.B <= 8) return false;
                fy = -(tile.H0 + tile.H1 + tile.H2 + tile.H3) * 0.25f * RoSpace.Scale;
                return true;
            }
            bool WallDrawn(int sIdx)
            {
                if (sIdx < 0 || sIdx >= gnd.Surfaces.Length) return false;
                var s = gnd.Surfaces[sIdx];
                return s.TextureIndex >= 0 && s.TextureIndex < gnd.Textures.Count && (skipTexture == null || !skipTexture(s.TextureIndex));
            }
            var drawn = new bool[w * h];
            var floorY = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    drawn[x + y * w] = FloorDrawn(x, y, out _, out _, out floorY[x + y * w]);

            var visited = new bool[w * h];
            var queue = new Queue<int>();
            var comp = new List<int>();
            var wallTops = new List<float>();
            var b = new Buf
            {
                V = new List<Vector3>(), N = new List<Vector3>(), UV = new List<Vector2>(),
                LM = new List<Vector2>(), C = new List<Color32>(), Tris = new List<int>[gnd.Textures.Count]
            };
            for (int i = 0; i < b.Tris.Length; i++) b.Tris[i] = new List<int>();

            int[] dx4 = { 1, -1, 0, 0 }, dy4 = { 0, 0, 1, -1 };
            for (int sy = 0; sy < h; sy++)
            {
                for (int sx = 0; sx < w; sx++)
                {
                    int start = sx + sy * w;
                    if (!drawn[start] || visited[start]) continue;
                    comp.Clear(); wallTops.Clear();
                    visited[start] = true; queue.Enqueue(start);
                    float maxFloorY = floorY[start];
                    while (queue.Count > 0)
                    {
                        int cur = queue.Dequeue();
                        comp.Add(cur);
                        int cx = cur % w, cy = cur / w;
                        float cfy = floorY[cur];
                        if (cfy > maxFloorY) maxFloorY = cfy;
                        var ct = gnd.Tiles[cur];
                        for (int d = 0; d < 4; d++)
                        {
                            int nx = cx + dx4[d], ny = cy + dy4[d];
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int ni = nx + ny * w;
                            if (drawn[ni] && Mathf.Abs(floorY[ni] - cfy) < 1.6f)
                            {
                                if (!visited[ni]) { visited[ni] = true; queue.Enqueue(ni); }
                                continue;
                            }
                            var nt = gnd.Tiles[ni];
                            float edgeTop = float.MinValue;
                            if (d == 0 && WallDrawn(ct.Right))
                                edgeTop = Mathf.Max(Mathf.Max(-ct.H1, -ct.H3), Mathf.Max(-nt.H0, -nt.H2)) * RoSpace.Scale;
                            else if (d == 1 && WallDrawn(nt.Right))
                                edgeTop = Mathf.Max(Mathf.Max(-nt.H1, -nt.H3), Mathf.Max(-ct.H0, -ct.H2)) * RoSpace.Scale;
                            else if (d == 2 && WallDrawn(ct.Front))
                                edgeTop = Mathf.Max(Mathf.Max(-ct.H2, -ct.H3), Mathf.Max(-nt.H0, -nt.H1)) * RoSpace.Scale;
                            else if (d == 3 && WallDrawn(nt.Front))
                                edgeTop = Mathf.Max(Mathf.Max(-nt.H2, -nt.H3), Mathf.Max(-ct.H0, -ct.H1)) * RoSpace.Scale;
                            if (edgeTop >= cfy + 2.0f && edgeTop <= cfy + 16.0f)
                                wallTops.Add(edgeTop);
                        }
                    }
                    if (comp.Count < 4) continue;
                    float ceilY;
                    if (wallTops.Count >= 2)
                    {
                        wallTops.Sort();
                        ceilY = Mathf.Clamp(wallTops[(wallTops.Count * 3) / 4], maxFloorY + 3.2f, maxFloorY + 12.0f);
                    }
                    else
                    {
                        ceilY = maxFloorY + 6.0f;
                    }
                    foreach (int idx in comp)
                    {
                        int tx = idx % w, ty = idx / w;
                        var t = gnd.Tiles[idx];
                        var s = gnd.Surfaces[t.Top];
                        Lm(s.LightmapIndex, lmCols, lmRows, out var l0, out var l1);
                        var col = new Color32((byte)(s.R * 195 / 255), (byte)(s.G * 195 / 255), (byte)(s.B * 195 / 255), 255);
                        float x0 = tx * 2f, x1 = x0 + 2f, z0 = ty * 2f, z1 = z0 + 2f;
                        AddQuad(ref b, s.TextureIndex,
                            new Vector3(x0, ceilY, z0), new Vector3(x1, ceilY, z0), new Vector3(x0, ceilY, z1), new Vector3(x1, ceilY, z1),
                            Vector3.down, Vector3.down, Vector3.down, Vector3.down,
                            UV(s.U0, s.V0), UV(s.U1, s.V1), UV(s.U2, s.V2), UV(s.U3, s.V3),
                            new Vector2(l0.x, l0.y), new Vector2(l1.x, l0.y), new Vector2(l0.x, l1.y), new Vector2(l1.x, l1.y),
                            col, Vector3.down);
                    }
                }
            }
            if (b.V.Count == 0) return null;
            var mesh = new Mesh { name = "ground-indoor-ceiling", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(b.V); mesh.SetNormals(b.N); mesh.SetUVs(0, b.UV); mesh.SetUVs(1, b.LM); mesh.SetColors(b.C);
            mesh.subMeshCount = b.Tris.Length;
            for (int i = 0; i < b.Tris.Length; i++) mesh.SetTriangles(b.Tris[i], i, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The lightmap atlas's grid, as <see cref="BuildLightmapAtlas"/> lays it out, without building it.</summary>
        private static void LmGrid(GndFile gnd, out int cols, out int rows)
        {
            int count = Mathf.Max(gnd.LightmapCount, 1);
            cols = Mathf.CeilToInt(Mathf.Sqrt(count));
            rows = Mathf.CeilToInt(count / (float)cols);
        }

        // GND grid corner (gx, gy) with RO height → Loki position. One GND tile = 2 cells.
        private static Vector3 P(int gx, int gy, float roHeight) => new Vector3(gx * 2f, -roHeight * RoSpace.Scale, gy * 2f);
        private static Vector2 UV(float u, float v) => new Vector2(u, 1f - v);

        private static void Lm(int index, int cols, int rows, out Vector2 l0, out Vector2 l1)
        {
            int cx = index % cols, cy = index / cols;
            l0 = new Vector2((cx + 0.125f) / cols, (cy + 0.125f) / rows);
            l1 = new Vector2((cx + 0.875f) / cols, (cy + 0.875f) / rows);
        }

        /// <summary>Quad with corners a=(0,0) b=(1,0) c=(0,1) d=(1,1). Winding chosen so the face normal agrees with `wantNormal` (or the given vertex normal).</summary>
        private static void AddQuad(ref Buf b, int tex, Vector3 a, Vector3 bb, Vector3 c, Vector3 d,
            Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd,
            Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud,
            Vector2 la, Vector2 lb, Vector2 lc, Vector2 ld,
            Color32 col, Vector3? wantNormal, bool robust = false)
        {
            int i0 = b.V.Count;
            b.V.Add(a); b.V.Add(bb); b.V.Add(c); b.V.Add(d);
            b.N.Add(na); b.N.Add(nb); b.N.Add(nc); b.N.Add(nd);
            b.UV.Add(ua); b.UV.Add(ub); b.UV.Add(uc); b.UV.Add(ud);
            b.LM.Add(la); b.LM.Add(lb); b.LM.Add(lc); b.LM.Add(ld);
            b.C.Add(col); b.C.Add(col); b.C.Add(col); b.C.Add(col);
            var want = wantNormal ?? na;
            var n = Vector3.Cross(c - a, d - a);
            // Architect's fix (warch2): a wall the fix names is turned by both triangles' facing, so a quad with no height at one end (its
            // first triangle degenerate) faces the way asked; without a fix every wall is built exactly as before
            if (robust) n += Vector3.Cross(d - a, bb - a);
            var t = b.Tris[tex];
            if (Vector3.Dot(n, want) >= 0)
            {
                t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
                t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 1);
            }
            else
            {
                t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 2);
                t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 3);
            }
        }

        private static Texture2D BuildLightmapAtlas(GndFile gnd, out int cols, out int rows)
        {
            int count = Mathf.Max(gnd.LightmapCount, 1);
            cols = Mathf.CeilToInt(Mathf.Sqrt(count));
            rows = Mathf.CeilToInt(count / (float)cols);
            int cw = gnd.LightmapW, ch = gnd.LightmapH; // 8x8
            int perCell = gnd.LightmapBytesPerCell;
            int W = cols * cw, H = rows * ch;
            var px = new Color32[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 255);
            for (int i = 0; i < gnd.LightmapCount; i++)
            {
                int pos = i * perCell * 4;
                int ox = (i % cols) * cw, oy = (i / cols) * ch;
                for (int y = 0; y < ch; y++)
                    for (int x = 0; x < cw; x++)
                    {
                        int k = x + y * cw;
                        byte shadow = gnd.LightmapData[pos + k];
                        int cp = pos + perCell + k * 3;
                        // Store rows top-down in atlas space then flip once for Unity (bottom-up) below.
                        px[(ox + x) + (oy + y) * W] = new Color32(gnd.LightmapData[cp], gnd.LightmapData[cp + 1], gnd.LightmapData[cp + 2], shadow);
                    }
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false, false) { name = "lightmap", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            // Our LM uvs use v growing with row index; Unity's v=0 is the bottom row, so upload as-is (row 0 = bottom = v 0).
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }
    }
}
