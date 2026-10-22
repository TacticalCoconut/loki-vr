using System.Collections.Generic;
using Loki.Formats;
using UnityEngine;

namespace Loki.World
{
    /// <summary>
    /// Builds a Unity mesh for an RSM at animation time t (ms), in Loki model-local space
    /// (RO model space with root centering applied, then F = diag(1/5, -1/5, 1/5)).
    /// Submesh i uses RsmFile.Textures[i].
    /// Client wave 2: animated models (research/world_rendering.md §A) — the vertex order does not depend on t, so an animated model keeps
    /// one dynamic mesh whose positions / normals are rewritten from the same node chain at a later t (<see cref="RsmAnimator"/>); the
    /// RSM1 centring is taken once from the static pose (recomputing it per frame made the model slide).
    /// </summary>
    public static class RsmMeshBuilder
    {
        private sealed class NodeXf { public RsmFile.Node Node; public Matrix4x4 World; public Matrix4x4 Main; }

        /// <summary>Reusable buffers for one model's mesh data.</summary>
        public sealed class Buffers
        {
            public readonly List<Vector3> Verts = new List<Vector3>(), Norms = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>(); public readonly List<Color32> Cols = new List<Color32>();
            public List<int>[] Tris;
            /// <summary>Client wave 4 (w4world), RSM 2.3 texture animation: per vertex, the texture group it belongs to (-1 = none) and its
            /// texture coordinate as the file stores it (before the v flip); the groups (a node's texture with keyframes).</summary>
            public readonly List<int> UvGroup = new List<int>(); public readonly List<Vector2> BaseUv = new List<Vector2>();
            public readonly List<TexGroup> Groups = new List<TexGroup>();
            /// <summary>Wave 27 (wmodel), Architect's fix kind M: with <see cref="RecordFaces"/> set, per emitted triangle (every three
            /// vertices of <see cref="Verts"/>, in order) the face it comes from: its node's index in <see cref="RsmFile.Nodes"/> (file
            /// order), the face's index in that node, and whether it is the reversed copy of a two-sided face. Off in the game.</summary>
            public bool RecordFaces;
            public readonly List<(int node, int face, bool back)> FaceOf = new List<(int, int, bool)>();
            public readonly List<Vector4> Uv2 = new List<Vector4>(), Uv3 = new List<Vector4>();
        }

        /// <summary>A node's texture (its local index, as the faces and the keyframes name it) with its texture keyframes.</summary>
        public sealed class TexGroup { public RsmFile.Node Node; public int LocalTex; public readonly List<RsmFile.TextureAnim> Anims = new List<RsmFile.TextureAnim>(); }

        public static Mesh Build(RsmFile rsm, float timeMs = 0f) => Build(rsm, timeMs, null, null);

        /// <summary>Wave 27 (wmodel): <paramref name="faces"/> (when not null) receives, per vertex triple, the face it comes from
        /// (<see cref="Buffers.FaceOf"/>: Architect reads it); <paramref name="backOnly"/> (when not null) builds ONLY the reversed copies
        /// of the single-sided faces it selects (node index in file order, face index): Architect's fix kind M, see <see cref="BuildBack"/>.</summary>
        public static Mesh Build(RsmFile rsm, float timeMs, List<(int node, int face, bool back)> faces, System.Func<int, int, bool> backOnly)
        {
            var rootFix = StaticRootFix(rsm);
            var b = new Buffers { RecordFaces = faces != null };
            Fill(rsm, timeMs, rootFix, b, true, backOnly);
            if (faces != null) { faces.Clear(); faces.AddRange(b.FaceOf); }
            var mesh = new Mesh { name = "rsm", indexFormat = b.Verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(b.Verts); mesh.SetNormals(b.Norms); mesh.SetUVs(0, b.Uvs); mesh.SetColors(b.Cols);
            if (b.Uv2.Count == b.Verts.Count) { mesh.SetUVs(2, b.Uv2); mesh.SetUVs(3, b.Uv3); }
            mesh.subMeshCount = b.Tris.Length;
            for (int i = 0; i < b.Tris.Length; i++) mesh.SetTriangles(b.Tris[i], i, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Wave 27 (wmodel), Architect's fix kind M (World/MapFix.cs, applied by World/MapLoader.cs): the BACK sides of chosen single-sided
        /// faces of a model, as their own mesh. Each selected face (node index in <see cref="RsmFile.Nodes"/>, face index in that node) is
        /// emitted exactly as the builder emits the reversed copy of a face the file marks two-sided (Fill's TwoSided path, as korangar
        /// loaders/model/mod.rs:132-142): the same positions and texture coordinates, the winding reversed (so the face is drawn from its
        /// other side) and the normals negated (so that side is lit as its own, never inside-out). The submeshes match the model's
        /// (texture i), so the instance's materials serve it unchanged. Faces the file already marks two-sided are left out (already drawn
        /// from both sides). Null when nothing is selected. <paramref name="count"/>: the triangles emitted.
        /// </summary>
        /// <summary>Wave 27 (wmodel): <see cref="BuildBack"/>'s faces as buffers (model space; per submesh the triangles; FaceOf the faces),
        /// for World/MapLoader.cs, which bakes them into map space and merges them per material.</summary>
        public static Buffers FillBack(RsmFile rsm, System.Func<int, int, bool> select)
        {
            var b = new Buffers { RecordFaces = true };
            Fill(rsm, 0f, StaticRootFix(rsm), b, true, select);
            return b;
        }

        public static Mesh BuildBack(RsmFile rsm, System.Func<int, int, bool> select, out int count)
        {
            count = 0;
            if (rsm == null || select == null) return null;
            var faces = new List<(int node, int face, bool back)>();
            var mesh = Build(rsm, 0f, faces, select);
            count = faces.Count;
            if (count == 0) return null;
            mesh.name = "rsm-back";
            return mesh;
        }

        /// <summary>RSM1: centre on the static (t = 0) bounding box (x/z centre, y = max i.e. the lowest point in y-down space); RSM2 is not centred.</summary>
        public static Matrix4x4 StaticRootFix(RsmFile rsm)
        {
            if (rsm.IsRsm2) return Matrix4x4.identity;
            var xfs = ComputeNodeTransforms(rsm, 0f);
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (var x in xfs)
            {
                var m = x.World * x.Main;
                foreach (var v in x.Node.Vertices)
                {
                    var p = m.MultiplyPoint3x4(v);
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                }
            }
            return min.x < float.MaxValue ? RoMat.Translate(new Vector3(-(min.x + max.x) * 0.5f, -max.y, -(min.z + max.z) * 0.5f)) : Matrix4x4.identity;
        }

        /// <summary>Positions / normals (and with <paramref name="topology"/> UVs, colours, triangles) of the model at t. The vertex order is
        /// the same for every t as long as no node's handedness flips (a negative animated scale), which no reference handles either.</summary>
        /// <param name="backOnly">Wave 27 (wmodel): null in the game's own build (every face, two-sided ones twice). Not null: ONLY the
        /// reversed copies of the single-sided faces it selects (node index in file order, face index): Architect's fix kind M
        /// (<see cref="BuildBack"/>).</param>
        public static void Fill(RsmFile rsm, float timeMs, Matrix4x4 rootFix, Buffers b, bool topology, System.Func<int, int, bool> backOnly = null)
        {
            var xfs = ComputeNodeTransforms(rsm, timeMs);
            int texCount = Mathf.Max(rsm.Textures.Count, 1);
            b.Verts.Clear(); b.Norms.Clear(); b.FaceOf.Clear();
            if (topology) { b.Uvs.Clear(); b.Cols.Clear(); b.UvGroup.Clear(); b.BaseUv.Clear(); b.Groups.Clear(); b.Uv2.Clear(); b.Uv3.Clear(); b.Tris = new List<int>[texCount]; for (int i = 0; i < texCount; i++) b.Tris[i] = new List<int>(); }
            bool shadeNone = rsm.ShadeType == RsmFile.Shading.None;
            bool smooth = rsm.ShadeType == RsmFile.Shading.Smooth;
            byte shadeFlag = (byte)(shadeNone ? 0 : 255);
            // wave 27 (wmodel): a node's index in the file (the key of Architect's fix kind M), only when faces are recorded or selected
            Dictionary<RsmFile.Node, int> nodeIndex = null;
            if (backOnly != null || b.RecordFaces) { nodeIndex = new Dictionary<RsmFile.Node, int>(); for (int i = 0; i < rsm.Nodes.Length; i++) if (!nodeIndex.ContainsKey(rsm.Nodes[i])) nodeIndex[rsm.Nodes[i]] = i; }

            bool checkAutoCeiling = backOnly == null && !b.RecordFaces && topology && !IsAnimated(rsm) && !HasTextureAnims(rsm);
            var boxMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var boxMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            if (checkAutoCeiling)
            {
                var toLoki = ToLoki(rsm) * rootFix;
                foreach (var x in xfs)
                {
                    var full = toLoki * x.World * x.Main;
                    foreach (var v in x.Node.Vertices)
                    {
                        var p = full.MultiplyPoint3x4(v);
                        boxMin = Vector3.Min(boxMin, p); boxMax = Vector3.Max(boxMax, p);
                    }
                }
                if (boxMin.x >= float.MaxValue || (boxMax.y - boxMin.y) < 2.2f || (boxMax.x - boxMin.x) < 2.0f || (boxMax.z - boxMin.z) < 2.0f)
                    checkAutoCeiling = false;
            }
            var ceilLo = new Vector4(boxMin.x, boxMin.y, boxMin.z, 3f);
            var ceilHi = new Vector4(boxMax.x, boxMax.y, boxMax.z, 0f);

            foreach (var x in xfs)
            {
                var n = x.Node;
                int ni = nodeIndex != null && nodeIndex.TryGetValue(n, out int nix) ? nix : -1;
                var full = ToLoki(rsm) * rootFix * x.World * x.Main; // model vertex → Loki local
                var pos = new Vector3[n.Vertices.Length];
                for (int i = 0; i < pos.Length; i++) pos[i] = full.MultiplyPoint3x4(n.Vertices[i]);

                // Face normals in Loki space (winding-independent: oriented by the transformed geometry itself).
                var faceN = new Vector3[n.Faces.Length];
                for (int f = 0; f < n.Faces.Length; f++)
                {
                    var fc = n.Faces[f];
                    var a = pos[fc.V0]; var bb = pos[fc.V1]; var c = pos[fc.V2];
                    faceN[f] = Vector3.Cross(bb - a, c - a).normalized;
                }
                // Determinant sign of the full transform tells us whether the stored winding was mirrored.
                bool mirrored = full.determinant < 0;
                // RO (left-handed, y-down) → Loki (left-handed, y-up) is a single reflection, so RSM1 faces need their winding reversed
                // once. Client wave 4: RSM2 is stored y-up and gets no reflection (ToLoki), so the same rule holds for it: korangar reverses
                // RSM2's order (`reverse_order ^ version>=2.2`, loaders/model/mod.rs:578) only because it mirrors RSM1 alone in its model
                // matrix (world/model/mod.rs:57-60: −scale.y below 2.2) — net, both versions keep one orientation there as here. (Before
                // wave 4 RSM2 was reflected like RSM1 and its winding XOR'd: every RSM2 model hung upside down under the ground.)
                bool flipWinding = mirrored;

                Dictionary<(int, int), Vector3> smoothAcc = null;
                if (smooth)
                {
                    smoothAcc = new Dictionary<(int, int), Vector3>();
                    for (int f = 0; f < n.Faces.Length; f++)
                    {
                        var fc = n.Faces[f];
                        Acc(smoothAcc, fc.V0, fc.SmoothGroup, faceN[f]); Acc(smoothAcc, fc.V1, fc.SmoothGroup, faceN[f]); Acc(smoothAcc, fc.V2, fc.SmoothGroup, faceN[f]);
                    }
                }

                // RSM 2.3 texture keyframes are per node and per the node's own texture index (the one the faces carry)
                Dictionary<int, int> groupOf = null;
                if (topology && n.TextureAnims.Count > 0)
                {
                    groupOf = new Dictionary<int, int>();
                    foreach (var ta in n.TextureAnims)
                    {
                        if (!groupOf.TryGetValue(ta.TextureIndex, out int gi)) { gi = b.Groups.Count; groupOf[ta.TextureIndex] = gi; b.Groups.Add(new TexGroup { Node = n, LocalTex = ta.TextureIndex }); }
                        b.Groups[gi].Anims.Add(ta);
                    }
                }

                for (int f = 0; f < n.Faces.Length; f++)
                {
                    var fc = n.Faces[f];
                    // wave 27 (wmodel): Architect's fix kind M builds only the back copies of the single-sided faces it selects
                    bool backSel = backOnly != null;
                    if (backSel && (fc.TwoSided != 0 || ni < 0 || !backOnly(ni, f))) continue;
                    int grp = groupOf != null && groupOf.TryGetValue(fc.TextureIndex, out int gg) ? gg : -1;
                    var fn = faceN[f];
                    if (flipWinding) fn = -fn;
                    Vector3 n0 = fn, n1 = fn, n2 = fn;
                    if (smooth)
                    {
                        n0 = smoothAcc[(fc.V0, fc.SmoothGroup)].normalized; n1 = smoothAcc[(fc.V1, fc.SmoothGroup)].normalized; n2 = smoothAcc[(fc.V2, fc.SmoothGroup)].normalized;
                        if (flipWinding) { n0 = -n0; n1 = -n1; n2 = -n2; }
                    }
                    var col = new Color32(255, 255, 255, shadeFlag);
                    List<int> t = null;
                    if (!backSel)
                    {
                        int i0 = b.Verts.Count;
                        b.Verts.Add(pos[fc.V0]); b.Verts.Add(pos[fc.V1]); b.Verts.Add(pos[fc.V2]);
                        b.Norms.Add(n0); b.Norms.Add(n1); b.Norms.Add(n2);
                        if (b.RecordFaces) b.FaceOf.Add((ni, f, false));
                        if (topology)
                        {
                            int localTex = fc.TextureIndex < n.TextureIndices.Length ? n.TextureIndices[fc.TextureIndex] : 0;
                            if (localTex < 0 || localTex >= texCount) localTex = 0;
                            b.Uvs.Add(UV(n.TexCoords, fc.T0)); b.Uvs.Add(UV(n.TexCoords, fc.T1)); b.Uvs.Add(UV(n.TexCoords, fc.T2));
                            b.Cols.Add(col); b.Cols.Add(col); b.Cols.Add(col);
                            if (checkAutoCeiling) { b.Uv2.Add(Vector4.zero); b.Uv2.Add(Vector4.zero); b.Uv2.Add(Vector4.zero); b.Uv3.Add(Vector4.zero); b.Uv3.Add(Vector4.zero); b.Uv3.Add(Vector4.zero); }
                            AddUvGroup(b, n.TexCoords, fc, grp);
                            t = b.Tris[localTex];
                            if (!flipWinding) { t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); }
                            else { t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1); }
                        }
                    }
                    else if (topology)
                    {
                        int localTex = fc.TextureIndex < n.TextureIndices.Length ? n.TextureIndices[fc.TextureIndex] : 0;
                        if (localTex < 0 || localTex >= texCount) localTex = 0;
                        t = b.Tris[localTex];
                    }
                    bool isOverheadCeiling = checkAutoCeiling && fc.TwoSided == 0 && fn.y > 0.35f &&
                                             Mathf.Min(pos[fc.V0].y, Mathf.Min(pos[fc.V1].y, pos[fc.V2].y)) - boxMin.y >= 1.8f;
                    if (fc.TwoSided != 0 || backSel || isOverheadCeiling)
                    {
                        if (b.RecordFaces) b.FaceOf.Add((ni, f, true));
                        int j0 = b.Verts.Count;
                        b.Verts.Add(pos[fc.V0]); b.Verts.Add(pos[fc.V1]); b.Verts.Add(pos[fc.V2]);
                        b.Norms.Add(-n0); b.Norms.Add(-n1); b.Norms.Add(-n2);
                        if (topology)
                        {
                            b.Uvs.Add(UV(n.TexCoords, fc.T0)); b.Uvs.Add(UV(n.TexCoords, fc.T1)); b.Uvs.Add(UV(n.TexCoords, fc.T2));
                            b.Cols.Add(col); b.Cols.Add(col); b.Cols.Add(col);
                            if (checkAutoCeiling)
                            {
                                var lo = isOverheadCeiling ? ceilLo : Vector4.zero;
                                var hi = isOverheadCeiling ? ceilHi : Vector4.zero;
                                b.Uv2.Add(lo); b.Uv2.Add(lo); b.Uv2.Add(lo);
                                b.Uv3.Add(hi); b.Uv3.Add(hi); b.Uv3.Add(hi);
                            }
                            AddUvGroup(b, n.TexCoords, fc, grp);
                            if (flipWinding) { t.Add(j0); t.Add(j0 + 1); t.Add(j0 + 2); }
                            else { t.Add(j0); t.Add(j0 + 2); t.Add(j0 + 1); }
                        }
                    }
                }
            }
        }

        /// <summary>A model animates when some node has two or more keys of a kind (one key is a constant pose) — roBrowser Model.js
        /// hasAnimation, korangar is_static.</summary>
        public static bool IsAnimated(RsmFile rsm)
        {
            if (rsm == null || rsm.Nodes == null) return false;
            foreach (var n in rsm.Nodes) if (n.RotKeys.Length > 1 || n.PosKeys.Length > 1 || n.ScaleKeys.Length > 1) return true;
            return false;
        }

        /// <summary>Length of one loop in ms: RSM1 AnimationLength is ms, RSM2 is frames at Fps; 0 falls back to the last key + 1 (roBrowser
        /// AnimatedModels.js:211-240).</summary>
        public static float LoopMs(RsmFile rsm)
        {
            float f = rsm.IsRsm2 ? 1000f / Mathf.Max(rsm.Fps, 1f) : 1f;
            float len = rsm.AnimationLength * f;
            if (len > 0) return len;
            int last = 0;
            foreach (var n in rsm.Nodes)
            {
                if (n.RotKeys.Length > 0) last = Mathf.Max(last, n.RotKeys[n.RotKeys.Length - 1].Frame);
                if (n.PosKeys.Length > 0) last = Mathf.Max(last, n.PosKeys[n.PosKeys.Length - 1].Frame);
                if (n.ScaleKeys.Length > 0) last = Mathf.Max(last, n.ScaleKeys[n.ScaleKeys.Length - 1].Frame);
            }
            return (last + 1) * f;
        }

        /// <summary>Client wave 4 (w4world): a model whose nodes carry RSM 2.3 texture keyframes (49 models of the 2026-09-30 LATAM GRF:
        /// fountains, waterfalls, lava, waves).</summary>
        public static bool HasTextureAnims(RsmFile rsm)
        {
            if (rsm == null || rsm.Nodes == null) return false;
            foreach (var n in rsm.Nodes) if (n.TextureAnims.Count > 0) return true;
            return false;
        }

        private static void AddUvGroup(Buffers b, RsmFile.TexCoord[] tc, RsmFile.Face fc, int grp)
        {
            b.UvGroup.Add(grp); b.UvGroup.Add(grp); b.UvGroup.Add(grp);
            b.BaseUv.Add(RawUV(tc, fc.T0)); b.BaseUv.Add(RawUV(tc, fc.T1)); b.BaseUv.Add(RawUV(tc, fc.T2));
        }
        private static Vector2 RawUV(RsmFile.TexCoord[] tc, int i) => i < 0 || i >= tc.Length ? Vector2.zero : tc[i].UV;

        /// <summary>
        /// Client wave 4 (w4world): the texture coordinates of the animated texture groups at time t. No reference client draws RSM 2.3
        /// texture keyframes (roBrowser Loaders/Model.js:190-225 and korangar ragnarok-formats/src/model.rs:103-138 only parse them);
        /// korangar names the five kinds from the rathena.org RSM2 format thread: 0 translation U, 1 translation V ("the texture is
        /// tiled"), 2 scale U, 3 scale V, 4 rotation around (0, 0) (radians: lava_s_04 turns 0 → 2π). What Loki does, and why:
        ///  • frames are RSM2 frames at the model's fps, looped over the animation length, linearly interpolated and held outside the
        ///    keys — exactly as the node keys (the keys of every animated texture run from frame 0 to the animation's end, e.g.
        ///    lava_s_01 V 1 → 0 over its 100 frames: one whole texture per loop, seamless only when interpolated);
        ///  • the values transform the texture COORDINATES, as a Direct3D texture transform does (coordinate' = T · R · S · coordinate: scale,
        ///    then rotation about (0, 0), then translation). The GRF decides the sign: RSM2 is stored y-up (ToLoki), and every falling-water
        ///    V translation (ilusion/waterfall_s_01 and _02, ilusion/fountain_s_01, prt/fountain_s_01's waterfall node) runs 0 → negative
        ///    on faces whose file V shrinks with height (V grows downward: correlation −0.77 … −1.0 against the stored y), so only
        ///    "uv + t" makes that water fall; "uv − t" would make every waterfall climb.
        ///  • the order only matters for the 3 models that combine scale or rotation with translation (graywolf/lava_s_04, lava_s_05,
        ///    hyper/suntree_e_01); it is not documented anywhere Loki can read.
        /// </summary>
        public static void AnimateUvs(RsmFile rsm, float timeMs, Buffers b)
        {
            if (b.Groups.Count == 0) return;
            float frameToMs = 1000f / Mathf.Max(rsm.Fps, 1f);
            float t = Loop(timeMs, LoopMs(rsm));
            var xf = new (Vector2 tr, Vector2 sc, float rot)[b.Groups.Count];
            for (int g = 0; g < b.Groups.Count; g++)
            {
                Vector2 tr = Vector2.zero, sc = Vector2.one; float rot = 0f;
                foreach (var a in b.Groups[g].Anims)
                {
                    float v = SampleTex(a, t, frameToMs);
                    switch (a.Type)
                    {
                        case 0: tr.x = v; break;
                        case 1: tr.y = v; break;
                        case 2: sc.x = v; break;
                        case 3: sc.y = v; break;
                        case 4: rot = v; break;
                    }
                }
                xf[g] = (tr, sc, rot);
            }
            for (int i = 0; i < b.UvGroup.Count && i < b.Uvs.Count; i++)
            {
                int g = b.UvGroup[i]; if (g < 0) continue;
                var (tr, sc, rot) = xf[g];
                var p = Vector2.Scale(b.BaseUv[i], sc);
                float c = Mathf.Cos(rot), s = Mathf.Sin(rot);
                p = new Vector2(p.x * c - p.y * s, p.x * s + p.y * c) + tr;
                b.Uvs[i] = new Vector2(p.x, 1f - p.y); // the file's v runs down the texture (UV())
            }
        }

        /// <summary>One texture keyframe track at t (ms): linear between keys, the first key before it, the last after it.</summary>
        public static float SampleTex(RsmFile.TextureAnim a, float t, float frameToMs)
        {
            var k = a.Frames; if (k.Count == 0) return a.Type == 2 || a.Type == 3 ? 1f : 0f;
            if (k.Count == 1) return k[0].value;
            int i = Interval(k.Count, j => k[j].frame * frameToMs, t, out float u);
            if (i < 0) return k[0].value;
            if (i >= k.Count - 1) return k[k.Count - 1].value;
            return Mathf.Lerp(k[i].value, k[i + 1].value, u);
        }

        private static void Acc(Dictionary<(int, int), Vector3> acc, int v, int g, Vector3 n)
        {
            var k = (v, g);
            acc.TryGetValue(k, out var cur);
            acc[k] = cur + n;
        }

        private static Vector2 UV(RsmFile.TexCoord[] tc, int i)
        {
            if (i < 0 || i >= tc.Length) return Vector2.zero;
            var uv = tc[i].UV;
            return new Vector2(uv.x, 1f - uv.y);
        }

        /// <summary>Per-node world (model-space) transform at time t, plus the vertex-space matrix (offset · mat3).</summary>
        private static List<NodeXf> ComputeNodeTransforms(RsmFile rsm, float timeMs)
        {
            var list = new List<NodeXf>();
            float loop = LoopMs(rsm);
            bool arm = rsm.IsRsm2 && HasKeys(rsm);
            foreach (var root in rsm.RootNodes)
                if (arm) WalkRsm2Arm(rsm, root, null, Matrix4x4.identity, Matrix4x4.identity, timeMs, loop, list); else Walk(rsm, root, Matrix4x4.identity, timeMs, loop, list);
            // Nodes unreachable from roots (broken parent names) still get rendered at identity.
            var seen = new HashSet<RsmFile.Node>(); foreach (var x in list) seen.Add(x.Node);
            foreach (var n in rsm.Nodes)
                if (!seen.Contains(n)) { if (arm) WalkRsm2Arm(rsm, n, null, Matrix4x4.identity, Matrix4x4.identity, timeMs, loop, list); else Walk(rsm, n, Matrix4x4.identity, timeMs, loop, list); }
            return list;
        }

        /// <summary>Client wave 4 (w4world): model space → Loki. RSM1 is stored y-down like the maps (RoSpace.F reflects it); RSM2 is stored
        /// y-up: korangar mirrors y for RSM1 only (world/model/mod.rs:57-60), roBrowser scales RSM2 instances by (1, −1, 1) back into its
        /// y-down world (Loaders/Model.js:80-91, 889-891). So RSM2 only scales (1 cell = 5 units).</summary>
        public static Matrix4x4 ToLoki(RsmFile rsm) => rsm.IsRsm2 ? Matrix4x4.Scale(Vector3.one * RoSpace.Scale) : RoSpace.F;

        /// <summary>Any key at all on any node: korangar's "is_static" is false then (loaders/model/mod.rs:423-428) and RSM2 nodes follow the arm
        /// chain; a static RSM2 model places every node absolutely at T(position) · offset matrix (mod.rs:175-182) — the same result.</summary>
        public static bool HasKeys(RsmFile rsm)
        {
            foreach (var n in rsm.Nodes) if (n.RotKeys.Length > 0 || n.PosKeys.Length > 0 || n.ScaleKeys.Length > 0) return true;
            return false;
        }

        /// <summary>
        /// Client wave 4 (w4world): RSM2 animation as korangar world/model/node.rs:180-225 computes it ("similar to the rathena.org RSM2 format
        /// thread": each node a rotary joint, keys REPLACE the node's own position / rotation). Before wave 4 Loki stacked the keys on the
        /// node's offset and matrix (roBrowser AnimatedModels.js), which moves a keyed node by its offset twice — prt\fountain_s_01's
        /// Object702 carries position keys equal to its offset (-11.91, 7.31, -5.96).
        ///   t = position key, else parentRot0ᵀ · (position − parentPosition)      rot0 = the node's stored matrix, position = its offset
        ///   R = rotation key, else parentRot0ᵀ · rot0;   S = scale key, else I;   C = R · S
        ///   arm = parentPrefix · [C | t],  arm.t += parentArm.t;   prefix = parentPrefix · C;   vertices go through arm (no main matrix)
        /// Keys are sampled as for every model here (held outside the keys, slerp), at the model's fps.
        /// </summary>
        private static void WalkRsm2Arm(RsmFile rsm, RsmFile.Node n, RsmFile.Node parent, Matrix4x4 parentPrefix, Matrix4x4 parentArm, float timeMs, float loopMs, List<NodeXf> outList)
        {
            float frameToMs = 1000f / Mathf.Max(rsm.Fps, 1f);
            var rot0 = RoMat.FromMat3ColumnMajor(n.Mat3);
            var parentRot0T = parent != null ? RoMat.FromMat3ColumnMajor(parent.Mat3).transpose : Matrix4x4.identity;
            var parentPos = parent != null ? parent.Offset : Vector3.zero;
            Vector3 t = n.PosKeys.Length > 0 ? SamplePos(n.PosKeys, timeMs, loopMs, frameToMs) : parentRot0T.MultiplyVector(n.Offset - parentPos);
            var R = n.RotKeys.Length > 0 ? RoMat.FromQuat(SampleRot(n.RotKeys, timeMs, loopMs, frameToMs)) : parentRot0T * rot0;
            var S = n.ScaleKeys.Length > 0 ? RoMat.Scale(SampleScale(n.ScaleKeys, timeMs, loopMs, frameToMs)) : Matrix4x4.identity;
            var C = R * S;
            var arm = parentPrefix * C;
            var at = parentPrefix.MultiplyVector(t) + (Vector3)parentArm.GetColumn(3);
            arm.SetColumn(3, new Vector4(at.x, at.y, at.z, 1f));
            var prefix = parentPrefix * C;
            outList.Add(new NodeXf { Node = n, World = arm, Main = Matrix4x4.identity });
            foreach (var c in n.Children) WalkRsm2Arm(rsm, c, n, prefix, arm, timeMs, loopMs, outList);
        }

        private static void Walk(RsmFile rsm, RsmFile.Node n, Matrix4x4 parent, float timeMs, float loopMs, List<NodeXf> outList)
        {
            Matrix4x4 local;
            if (!rsm.IsRsm2)
            {
                // roBrowser AnimatedModels.js:615-660: T(posKey || pos) · (rotKey ? quat : axis-angle) · S(scaleKey || scale); its static
                // compile (Model.js:257-267) takes rotation key 0 but the node's own position and scale, so single pos / scale keys are ignored
                var T = RoMat.Translate(n.PosKeys.Length > 1 ? SamplePos(n.PosKeys, timeMs, loopMs, 1f) : n.Position);
                Matrix4x4 R = n.RotKeys.Length > 0 ? RoMat.FromQuat(SampleRot(n.RotKeys, timeMs, loopMs, 1f)) : RoMat.AxisAngle(n.RotationAxis, n.RotationAngle);
                var S = RoMat.Scale(n.ScaleKeys.Length > 1 ? SampleScale(n.ScaleKeys, timeMs, loopMs, 1f) : n.Scale);
                local = T * R * S;
            }
            else
            {
                float frameToMs = 1000f / Mathf.Max(rsm.Fps, 1f);
                var T = n.PosKeys.Length > 0 ? RoMat.Translate(SamplePos(n.PosKeys, timeMs, loopMs, frameToMs)) : Matrix4x4.identity;
                var R = n.RotKeys.Length > 0 ? RoMat.FromQuat(SampleRot(n.RotKeys, timeMs, loopMs, frameToMs)) : Matrix4x4.identity;
                var S = n.ScaleKeys.Length > 0 ? RoMat.Scale(SampleScale(n.ScaleKeys, timeMs, loopMs, frameToMs)) : Matrix4x4.identity;
                local = T * R * S;
            }
            var world = parent * local;
            var main = (rsm.SingleNode && !rsm.IsRsm2 ? Matrix4x4.identity : RoMat.Translate(n.Offset)) * RoMat.FromMat3ColumnMajor(n.Mat3);
            outList.Add(new NodeXf { Node = n, World = world, Main = main });
            foreach (var c in n.Children) Walk(rsm, c, world, timeMs, loopMs, outList);
        }

        /// <summary>The key interval holding t: before the first key that key is held (before wave 2 the last one was returned), after the
        /// last key the last is held (roBrowser AnimatedModels.js:398-476). -1 = before the first key.</summary>
        private static int Interval(int count, System.Func<int, float> frameMs, float t, out float u)
        {
            u = 0f;
            if (t <= frameMs(0)) return -1;
            for (int i = 0; i < count - 1; i++)
            {
                float a = frameMs(i), b = frameMs(i + 1);
                if (t >= a && t < b) { u = (t - a) / Mathf.Max(b - a, 1e-4f); return i; }
            }
            return count - 1;
        }
        private static float Loop(float t, float loopMs) => loopMs > 0 ? Mathf.Repeat(t, loopMs) : 0f;

        private static Quaternion SampleRot(RsmFile.RotKey[] keys, float t, float loopMs, float frameToMs)
        {
            if (keys.Length == 1) return keys[0].Rotation;
            int i = Interval(keys.Length, k => keys[k].Frame * frameToMs, Loop(t, loopMs), out float u);
            if (i < 0) return keys[0].Rotation;
            if (i >= keys.Length - 1) return keys[keys.Length - 1].Rotation;
            return Quaternion.Slerp(keys[i].Rotation, keys[i + 1].Rotation, u);
        }
        private static Vector3 SamplePos(RsmFile.PosKey[] keys, float t, float loopMs, float frameToMs)
        {
            if (keys.Length == 1) return keys[0].Position;
            int i = Interval(keys.Length, k => keys[k].Frame * frameToMs, Loop(t, loopMs), out float u);
            if (i < 0) return keys[0].Position;
            if (i >= keys.Length - 1) return keys[keys.Length - 1].Position;
            return Vector3.Lerp(keys[i].Position, keys[i + 1].Position, u);
        }
        private static Vector3 SampleScale(RsmFile.ScaleKey[] keys, float t, float loopMs, float frameToMs)
        {
            if (keys.Length == 1) return keys[0].Scale;
            int i = Interval(keys.Length, k => keys[k].Frame * frameToMs, Loop(t, loopMs), out float u);
            if (i < 0) return keys[0].Scale;
            if (i >= keys.Length - 1) return keys[keys.Length - 1].Scale;
            return Vector3.Lerp(keys[i].Scale, keys[i + 1].Scale, u);
        }

        /// <summary>Instance transform: RO placement (position / rotation degrees / scale) → Unity TRS in Loki space.</summary>
        public static void ApplyInstanceTransform(Transform t, Vector3 roPos, Vector3 roRotDeg, Vector3 roScale, Vector3 lokiOffset)
        {
            var Ru = InstanceRotation(roRotDeg);
            t.localPosition = RoSpace.ToLoki(roPos) + lokiOffset;
            t.localRotation = Ru.rotation;
            t.localScale = roScale;
        }

        /// <summary>The instance's rotation in Loki space: RO's Z · X · Y (degrees) conjugated by F (still a proper rotation).</summary>
        private static Matrix4x4 InstanceRotation(Vector3 roRotDeg)
        {
            var R = RoMat.RotZ(roRotDeg.z * Mathf.Deg2Rad) * RoMat.RotX(roRotDeg.x * Mathf.Deg2Rad) * RoMat.RotY(roRotDeg.y * Mathf.Deg2Rad);
            return RoSpace.F * R * RoSpace.FInv;
        }

        /// <summary>Client wave 20 (w20rooms), point 54: <see cref="ApplyInstanceTransform"/>'s placement as one matrix (T · R · S, what Unity
        /// makes of localPosition / localRotation / localScale), model-local → map-local. Pure matrix math: the first-run builder's worker
        /// thread uses it (Data/RoomTools.cs).</summary>
        public static Matrix4x4 InstanceMatrix(Vector3 roPos, Vector3 roRotDeg, Vector3 roScale, Vector3 lokiOffset)
            => RoMat.Translate(RoSpace.ToLoki(roPos) + lokiOffset) * InstanceRotation(roRotDeg) * RoMat.Scale(roScale);

        /// <summary>Client wave 20 (w20rooms), point 54: the model's static (t = 0) box in Loki model-local space, from the very vertices
        /// <see cref="Build"/> puts in its mesh (every face's corners through ToLoki · root fix · node world · node main). False for a model
        /// without faces. Pure matrix math, no Mesh: the first-run builder's worker thread calls it (Data/RoomTools.cs).</summary>
        public static bool StaticBounds(RsmFile rsm, out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            var rootFix = StaticRootFix(rsm);
            foreach (var x in ComputeNodeTransforms(rsm, 0f))
            {
                var full = ToLoki(rsm) * rootFix * x.World * x.Main;
                var verts = x.Node.Vertices;
                foreach (var f in x.Node.Faces)
                    for (int k = 0; k < 3; k++)
                    {
                        int vi = k == 0 ? f.V0 : k == 1 ? f.V1 : f.V2;
                        if (vi >= verts.Length) continue;
                        var p = full.MultiplyPoint3x4(verts[vi]);
                        min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                    }
            }
            return min.x <= max.x;
        }
    }

    /// <summary>
    /// Animated map models (client wave 2): one dynamic mesh per animated model file on the current map, shared by its instances (one global
    /// clock, as roBrowser / korangar / unityro do), re-posed about Fps/1.14 times a second (roBrowser AnimatedModels.js:596-601), between 5
    /// and 30. RoAssets' static meshes stay untouched; the animator lives under the map root and goes with it.
    /// </summary>
    public sealed class RsmAnimator : MonoBehaviour
    {
        private sealed class Anim { public RsmFile Rsm; public Mesh Mesh; public Matrix4x4 RootFix; public RsmMeshBuilder.Buffers Buf; public float Period, Next; public int Updates, UvUpdates; public bool Moves, Textures; }
        private readonly Dictionary<string, Anim> _anims = new Dictionary<string, Anim>();
        /// <summary>Model files with moving nodes (client wave 2's count: the texture-only ones of wave 4 are counted apart).</summary>
        public int Count { get { int n = 0; foreach (var a in _anims.Values) if (a.Moves) n++; return n; } }
        public int Updates { get { int n = 0; foreach (var a in _anims.Values) n += a.Updates; return n; } }
        public IEnumerable<string> Files { get { foreach (var kv in _anims) if (kv.Value.Moves) yield return kv.Key; } }
        /// <summary>Client wave 4 (w4world): model files with RSM 2.3 texture keyframes on this map, and how often their texture
        /// coordinates were rewritten.</summary>
        public int TextureCount { get { int n = 0; foreach (var a in _anims.Values) if (a.Textures) n++; return n; } }
        public int UvUpdates { get { int n = 0; foreach (var a in _anims.Values) n += a.UvUpdates; return n; } }
        public IEnumerable<string> TextureFiles { get { foreach (var kv in _anims) if (kv.Value.Textures) yield return kv.Key; } }
        /// <summary>Smoke: a texture-animated file's live mesh and its buffers (null when not on this map).</summary>
        public Mesh MeshOf(string key) => _anims.TryGetValue(key, out var a) ? a.Mesh : null;
        public RsmMeshBuilder.Buffers BuffersOf(string key) => _anims.TryGetValue(key, out var a) ? a.Buf : null;
        public RsmFile ModelOf(string key) => _anims.TryGetValue(key, out var a) ? a.Rsm : null;

        /// <summary>True when the model file has moving nodes (client wave 2) — a model with texture keyframes only is not "animated" there.</summary>
        public static bool Moving(RsmFile rsm) => RsmMeshBuilder.IsAnimated(rsm);

        /// <summary>The dynamic mesh for an animated model file (created on first use), or null for a static model. Client wave 4: also for a
        /// model whose only animation is its texture keyframes (RSM 2.3).</summary>
        public Mesh MeshFor(string key, RsmFile rsm)
        {
            bool moves = RsmMeshBuilder.IsAnimated(rsm), tex = RsmMeshBuilder.HasTextureAnims(rsm);
            if (!moves && !tex) return null;
            if (_anims.TryGetValue(key, out var a)) return a.Mesh;
            a = new Anim { Rsm = rsm, RootFix = RsmMeshBuilder.StaticRootFix(rsm), Buf = new RsmMeshBuilder.Buffers(), Moves = moves, Textures = tex };
            RsmMeshBuilder.Fill(rsm, 0f, a.RootFix, a.Buf, true);
            var m = new Mesh { name = key + " (animated)", indexFormat = a.Buf.Verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            m.MarkDynamic();
            m.SetVertices(a.Buf.Verts); m.SetNormals(a.Buf.Norms); m.SetUVs(0, a.Buf.Uvs); m.SetColors(a.Buf.Cols);
            m.subMeshCount = a.Buf.Tris.Length;
            for (int i = 0; i < a.Buf.Tris.Length; i++) m.SetTriangles(a.Buf.Tris[i], i, true);
            m.RecalculateBounds();
            a.Mesh = m;
            float fps = rsm.IsRsm2 ? rsm.Fps : 30f; // RSM1 has no fps: roBrowser MapLoader.js:477 defaults to 30
            a.Period = 1f / Mathf.Clamp(fps / 1.14f, 5f, 30f);
            _anims[key] = a;
            return m;
        }

        private void Update()
        {
            float now = Time.time, ms = now * 1000f;
            foreach (var a in _anims.Values)
            {
                if (now < a.Next) continue;
                a.Next = now + a.Period;
                if (a.Moves)
                {
                    int before = a.Buf.Verts.Count;
                    RsmMeshBuilder.Fill(a.Rsm, ms, a.RootFix, a.Buf, false);
                    if (a.Buf.Verts.Count == before) // topology changed (never expected): keep the last good pose
                    {
                        a.Mesh.SetVertices(a.Buf.Verts); a.Mesh.SetNormals(a.Buf.Norms); a.Mesh.RecalculateBounds();
                        a.Updates++;
                    }
                }
                if (a.Textures)
                {
                    RsmMeshBuilder.AnimateUvs(a.Rsm, ms, a.Buf);
                    a.Mesh.SetUVs(0, a.Buf.Uvs);
                    a.UvUpdates++;
                }
            }
        }

        private void OnDestroy() { foreach (var a in _anims.Values) if (a.Mesh != null) Destroy(a.Mesh); _anims.Clear(); }
    }
}
