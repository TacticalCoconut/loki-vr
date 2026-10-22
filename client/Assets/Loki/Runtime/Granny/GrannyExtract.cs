// =====================================================================================================================
// Project Loki, client wave 4b (w4granny): a C# port, step for step, of granny-ro-js 1.5.0 (npm, MIT;
// https://github.com/stitchuuuu/granny-ro-js), the Granny3D (.gr2) decoder roBrowserLegacy uses (src/Loaders/GR2Loader.js).
// Source read: package/dist/granny-ro.esm.js (a minified bundle). Comments name the package's exported functions and, in
// brackets, the bundle's own minified names; "l. N" are the lines of that bundle reformatted by esbuild
// (client/tools/w4granny/README.md). The JavaScript number rules are kept where they decide a result.
//
// This file: skeletons (extractSkeletons [Ce]), meshes (extractMeshes [Ee]: vertex layout, positions, normals, UVs,
// indices, bone bindings, skin weights, triangle groups), materials (extractMaterials [Ge]/[Ue]/[Ke]) and models
// (extractModels [Ze]: InitialPlacement, mesh bindings).
//
// MIT License
//
// Copyright (c) 2026 Stitchuuuu <stitchuuuu@icloud.com>
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// =====================================================================================================================
using System;
using System.Collections.Generic;
using static Loki.Granny.GrannyTypeTree;

namespace Loki.Granny
{
    public sealed class GrBone
    {
        public int Index, ParentIndex;
        public string Name;
        public GrTransform Transform;
        /// <summary>16 floats as stored (empty when out of range).</summary>
        public double[] InverseWorldTransform;
    }

    public sealed class GrSkeleton
    {
        public string Name;
        public List<GrBone> Bones;
        public double LodType;
    }

    public sealed class GrVertexComponent { public string Name; public int Offset, Width; public uint MemberType; }
    public struct GrWeight { public int BoneIndex; public double Weight; }
    public sealed class GrTriangleGroup { public double MaterialIndex, TriFirst, TriCount; }

    public sealed class GrMaterial
    {
        public int Index;
        public string Name, TextureFile;
        public int[] TextureSize; // [w, h] or null
    }

    public sealed class GrMesh
    {
        public string Name;
        public int VertexCount, IndexCount, VertexStride;
        public List<GrVertexComponent> Components;
        /// <summary>[x, y, z] per vertex (fewer when the vertex data ends early), normals likewise, UVs [u, v].</summary>
        public List<float[]> Positions, Normals, Uvs;
        public uint[] Indices;
        public List<string> BoneBindings; // bone names, by binding index
        /// <summary>Per vertex: its (bone binding index, weight) pairs with weight &gt; 0; null when the mesh has no weights.</summary>
        public List<GrWeight>[] VertexWeights;
        public List<GrMaterial> Materials;
        public List<GrTriangleGroup> TriangleGroups;
    }

    public sealed class GrModel
    {
        public string Name;
        public int SkeletonIdx;
        public GrTransform InitialPlacement;
        public List<int> MeshBindings;
    }

    public static class GrannyExtract
    {
        private static Dictionary<string, GrField> Root(GrLoaded e2, int maxArrayRefs = 256) =>
            ParseObject(e2, ParseTypeTree(e2, e2.File.Header.RootType), new GrRef((int)e2.File.Header.RootObject[0], (int)e2.File.Header.RootObject[1]), maxArrayRefs);

        // ------------------------------------------------------------------------------------------------ skeletons
        /// <summary>extractSkeletons [Ce] l. 573-588 (maxSkeletons 16, maxBones 4096).</summary>
        public static List<GrSkeleton> ExtractSkeletons(GrLoaded e2, int maxSkeletons = 16, int maxBones = 4096)
        {
            var u2 = new List<GrSkeleton>();
            var s2 = F(Root(e2, maxSkeletons), "Skeletons");
            if (s2 == null) return u2;
            var c2 = s2.ReferenceType; var l2 = s2.ElementRefs ?? new List<GrRef>();
            if (c2 == null || l2.Count == 0) return u2;
            var d2 = ParseTypeTree(e2, c2);
            int f2 = Math.Min(l2.Count, maxSkeletons);
            for (int t3 = 0; t3 < f2; t3++)
            {
                var n3 = l2[t3];
                if (n3 == null) continue;
                var i3 = ParseObject(e2, d2, n3, maxBones);
                var a3 = F(i3, "Bones");
                var c3 = ReadReferenceArrayObjects(e2, a3?.Target, CountOr0(a3), a3?.ReferenceType, maxBones);
                var f3 = new List<GrBone>(c3.Count);
                for (int t4 = 0; t4 < c3.Count; t4++) f3.Add(Bone(e2, t4, c3[t4]));
                var lod = F(i3, "LODType");
                u2.Add(new GrSkeleton { Name = NameOr(i3, "Name", "Skeleton_" + u2.Count), Bones = f3, LodType = lod != null && lod.HasNum ? lod.Num : 0 });
            }
            return u2;
        }

        /// <summary>[we] l. 589-592.</summary>
        private static GrBone Bone(GrLoaded e2, int t2, GrObjectRecord n2)
        {
            var i2 = n2.Fields;
            var o2 = F(i2, "ParentIndex");
            int s2 = F(i2, "Transform")?.Offset ?? 0, c2 = F(i2, "InverseWorldTransform")?.Offset ?? 0;
            var l2 = GrTransform.Identity(); double[] u2 = new double[0];
            if (n2.Ref != null) { l2 = ReadTransform(e2, n2.Ref.Section, s2); u2 = ReadFloats(e2, n2.Ref.Section, c2, 16); }
            return new GrBone { Index = t2, Name = NameOr(i2, "Name", "Bone_" + t2), ParentIndex = o2 != null && o2.HasNum ? (int)o2.Num : -1, Transform = l2, InverseWorldTransform = u2 };
        }

        // ------------------------------------------------------------------------------------------------ meshes
        private sealed class MeshEntry { public int Index; public string Name; public GrRef Ref; public Dictionary<string, GrField> Fields, PrimaryVertexData, PrimaryTopology; }

        /// <summary>extractMeshes [Ee] l. 600-617 (maxMeshes 32, maxMaterials 4096, maxBones 4096).</summary>
        public static List<GrMesh> ExtractMeshes(GrLoaded e2, int maxMeshes = 32, int maxMaterials = 4096, int maxBones = 4096)
        {
            var s2 = new List<GrMesh>();
            var a2 = MeshEntries(e2, maxMeshes);
            if (a2.Count == 0) return s2;
            var o2 = MaterialsAndCache(e2, maxMaterials).cache;
            foreach (var n3 in a2)
            {
                var r3 = n3.PrimaryVertexData; var c3 = n3.PrimaryTopology;
                if (r3 == null) continue;
                var l2 = F(r3, "Vertices");
                if (l2 == null) continue;
                var u2 = l2.Target; var d2 = l2.VariantType; long f2 = CountOr0(l2);
                if (u2 == null || d2 == null || f2 <= 0) continue;
                var p2 = Layout(ParseTypeTree(e2, d2)); int m2 = Stride(p2);
                var h2 = Slice(e2, u2.Section, u2.Offset, f2 * m2);
                if (h2 == null) continue;
                GrField _2 = null; string v2 = null;
                if (c3 != null) PickIndices(c3, out v2, out _2);
                var y2 = Indices(e2, _2?.Target, CountOr0(_2), _2 != null && v2 == "Indices" ? 20u : 16u);
                var b2 = BoneBindings(e2, n3.Fields, maxBones);
                var x2 = MeshMaterials(e2, n3.Fields, o2, maxBones);
                var S2 = c3 != null ? TriangleGroups(e2, c3) : new List<GrTriangleGroup>();
                int vc = (int)f2;
                s2.Add(new GrMesh
                {
                    Name = n3.Name, VertexCount = vc, IndexCount = y2.Length, VertexStride = m2, Components = p2,
                    Positions = Floats(h2, vc, m2, p2, "Position", 3, false), Normals = Floats(h2, vc, m2, p2, "Normal", 3, false),
                    Uvs = Floats(h2, vc, m2, p2, "TextureCoordinates", 2, true), Indices = y2, BoneBindings = b2,
                    VertexWeights = Weights(h2, vc, m2, p2, b2.Count), Materials = x2, TriangleGroups = S2,
                });
            }
            return s2;
        }

        /// <summary>[De] l. 618-637 (+ [Oe] l. 638-641).</summary>
        private static List<MeshEntry> MeshEntries(GrLoaded e2, int t2)
        {
            var u2 = new List<MeshEntry>();
            var r2 = ParseTypeTree(e2, e2.File.Header.RootType);
            GrMember i2 = null;
            foreach (var m in r2) if (m.Name == "Meshes") { i2 = m; break; }
            if (i2 == null || i2.ReferenceType == null) return u2;
            var a2 = F(Root(e2, t2), "Meshes")?.ElementRefs ?? new List<GrRef>();
            if (a2.Count == 0) return u2;
            var s2 = ParseTypeTree(e2, i2.ReferenceType);
            var c2 = new Dictionary<string, GrMember>();
            foreach (var m in s2) c2[m.Name] = m;
            int l2 = Math.Min(a2.Count, t2);
            for (int t3 = 0; t3 < l2; t3++)
            {
                var n3 = a2[t3];
                if (n3 == null) continue;
                var r3 = ParseObject(e2, s2, n3);
                var o3 = new MeshEntry { Index = t3, Name = NameOr(r3, "Name", "Mesh_" + t3), Ref = n3, Fields = r3 };
                o3.PrimaryVertexData = Sub(e2, r3, c2, "PrimaryVertexData");
                o3.PrimaryTopology = Sub(e2, r3, c2, "PrimaryTopology");
                u2.Add(o3);
            }
            return u2;
        }

        private static Dictionary<string, GrField> Sub(GrLoaded e2, Dictionary<string, GrField> n2, Dictionary<string, GrMember> r2, string i2)
        {
            var o2 = F(n2, i2)?.Target;
            if (o2 == null || !r2.TryGetValue(i2, out var s2) || s2.ReferenceType == null) return null;
            return ParseObject(e2, ParseTypeTree(e2, s2.ReferenceType), o2);
        }

        /// <summary>vertex component size [je] l. 659-662 / [Me] l. 663-666.</summary>
        private static int CompSize(uint n2, int t2) =>
            n2 == 10 || n2 == 19 || n2 == 20 ? 4 * t2 : n2 == 15 || n2 == 16 || n2 == 17 || n2 == 18 ? 2 * t2 : n2 == 11 || n2 == 12 || n2 == 13 || n2 == 14 ? t2 : 4 * t2;

        /// <summary>vertexLayout [ke] l. 642-649.</summary>
        private static List<GrVertexComponent> Layout(List<GrMember> e2)
        {
            var t2 = new List<GrVertexComponent>(e2.Count); int n2 = 0;
            foreach (var i2 in e2)
            {
                int w = (int)(i2.ArrayWidth != 0 ? i2.ArrayWidth : 1);
                t2.Add(new GrVertexComponent { Name = i2.Name, Offset = n2, Width = w, MemberType = i2.MemberType });
                n2 += CompSize(i2.MemberType, w);
            }
            return t2;
        }

        /// <summary>vertexStride [Ae] l. 650-658.</summary>
        private static int Stride(List<GrVertexComponent> e2)
        {
            int t2 = 0;
            foreach (var c in e2) { int r2 = c.Offset + CompSize(c.MemberType, c.Width); if (r2 > t2) t2 = r2; }
            return t2;
        }

        /// <summary>readVertexFloats [Ne] l. 667-690: the first real32 component named n (or starting with n) at least dim wide.</summary>
        private static List<float[]> Floats(byte[] e2, int t2, int n2, List<GrVertexComponent> r2, string i2, int a2, bool o2)
        {
            var f2 = new List<float[]>();
            GrVertexComponent s2 = null;
            foreach (var t3 in r2)
                if (t3.MemberType == 10 && !(t3.Width < a2) && (o2 ? t3.Name.StartsWith(i2, StringComparison.Ordinal) : t3.Name == i2)) { s2 = t3; break; }
            if (s2 == null) return f2;
            int l2 = e2.Length, u2 = s2.Offset, d2 = a2 * 4;
            for (int e3 = 0; e3 < t2; e3++)
            {
                long t3 = (long)e3 * n2 + u2;
                if (t3 + d2 > l2) break;
                var v = new float[a2];
                for (int k = 0; k < a2; k++) v[k] = Js.F32(e2, (int)t3 + k * 4);
                f2.Add(v);
            }
            return f2;
        }

        /// <summary>[He] l. 792-795: Indices16 if it has entries, else Indices if it has entries, else whichever exists.</summary>
        private static void PickIndices(Dictionary<string, GrField> e2, out string name, out GrField field)
        {
            var t2 = F(e2, "Indices16"); var n2 = F(e2, "Indices");
            if (t2 != null && CountOr0(t2) > 0) { name = "Indices16"; field = t2; }
            else if (n2 != null && CountOr0(n2) > 0) { name = "Indices"; field = n2; }
            else if (t2 != null) { name = "Indices16"; field = t2; }
            else if (n2 != null) { name = "Indices"; field = n2; }
            else { name = null; field = null; }
        }

        /// <summary>readIndices [Pe] l. 691-705 (type 19 / 20: 32-bit, else 16-bit).</summary>
        private static uint[] Indices(GrLoaded e2, GrRef t2, long n2, uint r2)
        {
            if (t2 == null || n2 <= 0) return new uint[0];
            if (r2 == 19 || r2 == 20)
            {
                var r3 = Slice(e2, t2.Section, t2.Offset, n2 * 4);
                if (r3 == null) return new uint[0];
                var a3 = new uint[n2];
                for (int e3 = 0; e3 < n2; e3++) a3[e3] = Js.U32(r3, e3 * 4);
                return a3;
            }
            var i2 = Slice(e2, t2.Section, t2.Offset, n2 * 2);
            if (i2 == null) return new uint[0];
            var o2 = new uint[n2];
            for (int e3 = 0; e3 < n2; e3++) o2[e3] = Js.U16(i2, e3 * 2);
            return o2;
        }

        /// <summary>readSkinWeights [Fe] l. 706-720 (with [Ie] l. 721-742 and [Le] l. 743-758).</summary>
        private static List<GrWeight>[] Weights(byte[] e2, int t2, int n2, List<GrVertexComponent> r2, int i2)
        {
            GrVertexComponent a2 = null, o2 = null;
            foreach (var c in r2) { if (c.Name == "BoneWeights") a2 = c; else if (c.Name == "BoneIndices") o2 = c; }
            if (a2 == null || o2 == null || i2 <= 0) return null;
            int s2 = a2.Width < o2.Width ? a2.Width : o2.Width, l2 = e2.Length, d2 = a2.Offset, f2 = o2.Offset;
            var u2 = new List<GrWeight>[t2];
            for (int e3 = 0; e3 < t2; e3++)
            {
                long t3 = (long)e3 * n2; var r3 = new List<GrWeight>();
                for (int e4 = 0; e4 < s2; e4++)
                {
                    double n3 = Weight(e2, l2, t3 + d2, e4, a2); long s3 = BoneIndex(e2, l2, t3 + f2, e4, o2);
                    if (!(n3 <= 0 || s3 < 0 || s3 >= i2)) r3.Add(new GrWeight { BoneIndex = (int)s3, Weight = n3 });
                }
                u2[e3] = r3;
            }
            return u2;
        }

        private static double Weight(byte[] e2, int t2, long n2, int r2, GrVertexComponent i2)
        {
            uint a2 = i2.MemberType;
            if (a2 == 10)
            {
                long i3 = n2 + r2 * 4;
                if (i3 + 4 > t2) return 0;
                double a3 = Js.F32(e2, (int)i3);
                return a3 <= 0 ? 0 : a3 > 1 ? 1 : a3;
            }
            if (a2 == 16 || a2 == 18)
            {
                long i3 = n2 + r2 * 2;
                if (i3 + 2 > t2) return 0;
                double o2 = Js.U16(e2, (int)i3);
                return a2 == 18 ? o2 / 65535 : o2;
            }
            if (a2 == 12 || a2 == 14)
            {
                long i3 = n2 + r2;
                if (i3 >= t2) return 0;
                double o2 = e2[i3];
                return a2 == 14 ? o2 / 255 : o2;
            }
            return 0;
        }

        private static long BoneIndex(byte[] e2, int t2, long n2, int r2, GrVertexComponent i2)
        {
            uint a2 = i2.MemberType;
            if (a2 == 19 || a2 == 20)
            {
                long i3 = n2 + r2 * 4;
                if (i3 + 4 > t2) return -1;
                uint v = Js.U32(e2, (int)i3);
                return a2 == 19 ? (long)unchecked((int)v) : v;
            }
            if (a2 == 15 || a2 == 16)
            {
                long i3 = n2 + r2 * 2;
                if (i3 + 2 > t2) return -1;
                ushort v = Js.U16(e2, (int)i3);
                return a2 == 15 ? (long)unchecked((short)v) : v;
            }
            if (a2 == 11 || a2 == 12)
            {
                long i3 = n2 + r2;
                if (i3 >= t2) return -1;
                return a2 == 11 ? (long)unchecked((sbyte)e2[i3]) : e2[i3];
            }
            return -1;
        }

        /// <summary>[Re] l. 759-768: bone binding names.</summary>
        private static List<string> BoneBindings(GrLoaded e2, Dictionary<string, GrField> t2, int n2)
        {
            var a2 = new List<string>();
            var r2 = F(t2, "BoneBindings");
            if (r2 == null) return a2;
            var i2 = ReadReferenceArrayObjects(e2, r2.Target, CountOr0(r2), r2.ReferenceType, n2);
            for (int e3 = 0; e3 < i2.Count; e3++) a2.Add(NameOr(i2[e3].Fields, "BoneName", "Bone_" + e3));
            return a2;
        }

        /// <summary>[ze] l. 769-778: the mesh's material bindings, resolved through the material cache.</summary>
        private static List<GrMaterial> MeshMaterials(GrLoaded e2, Dictionary<string, GrField> t2, Dictionary<string, GrMaterial> n2, int r2)
        {
            var o2 = new List<GrMaterial>();
            var i2 = F(t2, "MaterialBindings");
            if (i2 == null) return o2;
            var a2 = ReadReferenceArrayObjects(e2, i2.Target, CountOr0(i2), i2.ReferenceType, r2);
            for (int e3 = 0; e3 < a2.Count; e3++)
            {
                var t3 = F(a2[e3].Fields, "Material")?.Target;
                GrMaterial m = t3 != null && n2.TryGetValue(t3.Key, out var cached) ? cached : null;
                o2.Add(m ?? new GrMaterial { Index = e3, Name = "Material_" + e3, TextureFile = "", TextureSize = null });
            }
            return o2;
        }

        /// <summary>[Be] l. 779-788.</summary>
        private static List<GrTriangleGroup> TriangleGroups(GrLoaded e2, Dictionary<string, GrField> t2)
        {
            var i2 = new List<GrTriangleGroup>();
            var n2 = F(t2, "Groups");
            if (n2 == null) return i2;
            var r2 = ReadReferenceArrayObjects(e2, n2.Target, CountOr0(n2), n2.ReferenceType, 4096);
            foreach (var r in r2)
            {
                var t3 = r.Fields;
                i2.Add(new GrTriangleGroup { MaterialIndex = NumOr(t3, "MaterialIndex", 0), TriFirst = NumOr(t3, "TriFirst", 0), TriCount = NumOr(t3, "TriCount", 0) });
            }
            return i2;
        }

        // ------------------------------------------------------------------------------------------------ materials
        /// <summary>extractMaterials [Ge] l. 809-811 (maxMaterials 4096).</summary>
        public static List<GrMaterial> ExtractMaterials(GrLoaded e2, int maxMaterials = 4096) => MaterialsAndCache(e2, maxMaterials).materials;

        /// <summary>[Ue] l. 796-805: every material of the file, and the cache keyed by "section:offset".</summary>
        private static (List<GrMaterial> materials, Dictionary<string, GrMaterial> cache) MaterialsAndCache(GrLoaded e2, int t2)
        {
            var c2 = new List<GrMaterial>(); var o2 = new Dictionary<string, GrMaterial>();
            var r2 = F(Root(e2, t2), "Materials");
            if (r2 == null) return (c2, o2);
            var i2 = r2.ReferenceType; var a2 = r2.ElementRefs ?? new List<GrRef>();
            int s2 = Math.Min(a2.Count, t2);
            for (int t3 = 0; t3 < s2; t3++) { var n3 = a2[t3]; if (n3 != null) c2.Add(Material(e2, n3, i2, t3, o2, new HashSet<string>())); }
            return (c2, o2);
        }

        /// <summary>[Ke] l. 812-820: a material's texture, directly or through its Maps (sub-materials get index -1).</summary>
        private static GrMaterial Material(GrLoaded e2, GrRef t2, GrRef n2, int r2, Dictionary<string, GrMaterial> i2, HashSet<string> a2)
        {
            string o2 = t2.Key;
            if (i2.TryGetValue(o2, out var hit)) return hit;
            if (n2 == null || a2.Contains(o2)) return new GrMaterial { Index = r2, Name = "Material_" + r2, TextureFile = "", TextureSize = null };
            var s2 = ParseObject(e2, ParseTypeTree(e2, n2), t2, 16);
            var c2 = TextureOf(e2, F(s2, "Texture"));
            if (!c2.HasValue) { var sub = new HashSet<string>(a2) { o2 }; c2 = MapsTexture(e2, F(s2, "Maps"), n2, i2, sub); }
            var u2 = new GrMaterial
            {
                Index = r2, Name = NameOr(s2, "Name", "Material_" + r2),
                TextureFile = c2.HasValue ? c2.Value.file : "", TextureSize = c2.HasValue ? c2.Value.size : null,
            };
            i2[o2] = u2;
            return u2;
        }

        /// <summary>[qe] l. 821-827: [FromFileName, [Width, Height] or null], or null without a texture reference.</summary>
        private static (string file, int[] size)? TextureOf(GrLoaded e2, GrField t2)
        {
            if (t2 == null) return null;
            var n2 = t2.Target; var r2 = t2.ReferenceType;
            if (n2 == null || r2 == null) return null;
            var i2 = ParseObject(e2, ParseTypeTree(e2, r2), n2, 8);
            var a2 = F(i2, "Width"); var o2 = F(i2, "Height");
            int[] s2 = a2 != null && a2.HasNum && o2 != null && o2.HasNum ? new[] { (int)a2.Num, (int)o2.Num } : null;
            var c2 = F(i2, "FromFileName");
            return (c2 != null && c2.Str != null ? c2.Str : "", s2);
        }

        /// <summary>[Je] l. 828-838.</summary>
        private static (string file, int[] size)? MapsTexture(GrLoaded e2, GrField t2, GrRef n2, Dictionary<string, GrMaterial> r2, HashSet<string> i2)
        {
            if (t2 == null) return null;
            var s2 = ReadReferenceArrayObjects(e2, t2.Target, CountOr0(t2), t2.ReferenceType, 16);
            foreach (var r in s2)
            {
                var a3 = F(r.Fields, "Map")?.Target;
                if (a3 == null) continue;
                var o3 = Material(e2, a3, n2, -1, r2, i2);
                if (!string.IsNullOrEmpty(o3.TextureFile)) return (o3.TextureFile, o3.TextureSize);
            }
            return null;
        }

        // ------------------------------------------------------------------------------------------------ models
        /// <summary>[Xe] l. 843-850: "section:offset" of each element reference → its index.</summary>
        private static Dictionary<string, int> RefIndex(GrField e2)
        {
            var t2 = new Dictionary<string, int>();
            var n2 = e2?.ElementRefs ?? new List<GrRef>();
            for (int e3 = 0; e3 < n2.Count; e3++) if (n2[e3] != null) t2[n2[e3].Key] = e3;
            return t2;
        }

        /// <summary>extractModels [Ze] l. 851-864 (maxModels 64, maxBindings 256).</summary>
        public static List<GrModel> ExtractModels(GrLoaded e2, int maxModels = 64, int maxBindings = 256)
        {
            var p2 = new List<GrModel>();
            var a2 = Root(e2);
            var o2 = F(a2, "Models");
            if (o2 == null) return p2;
            var s2 = o2.ReferenceType; var c2 = o2.ElementRefs ?? new List<GrRef>();
            if (s2 == null || c2.Count == 0) return p2;
            var l2 = RefIndex(F(a2, "Skeletons")); var u2 = RefIndex(F(a2, "Meshes"));
            var d2 = ParseTypeTree(e2, s2);
            int f2 = Math.Min(c2.Count, maxModels);
            for (int t3 = 0; t3 < f2; t3++)
            {
                var n3 = c2[t3];
                if (n3 == null) continue;
                var i3 = ParseObject(e2, d2, n3);
                var s3 = F(i3, "Skeleton")?.Target;
                int f3 = s3 != null && l2.TryGetValue(s3.Key, out int si) ? si : -1;
                var ipf = F(i3, "InitialPlacement");
                var h2 = ipf?.Xform ?? ReadTransform(e2, n3.Section, ipf?.Offset ?? 0);
                p2.Add(new GrModel { Name = NameOr(i3, "Name", "Model_" + p2.Count), SkeletonIdx = f3, InitialPlacement = h2, MeshBindings = MeshBindings(e2, i3, u2, maxBindings) });
            }
            return p2;
        }

        /// <summary>[Qe] l. 865-876.</summary>
        private static List<int> MeshBindings(GrLoaded e2, Dictionary<string, GrField> t2, Dictionary<string, int> n2, int r2)
        {
            var l2 = new List<int>();
            var i2 = F(t2, "MeshBindings");
            if (i2 == null) return l2;
            var a2 = i2.ReferenceType; var o2 = i2.Target; long s2 = CountOr0(i2);
            if (a2 == null || o2 == null || s2 <= 0) return l2;
            foreach (var c in ReadReferenceArrayObjects(e2, o2, s2, a2, r2))
            {
                var t3 = F(c.Fields, "Mesh")?.Target;
                l2.Add(t3 != null && n2.TryGetValue(t3.Key, out int mi) ? mi : -1);
            }
            return l2;
        }
    }
}
