using System.Collections.Generic;
using System.IO;
using Loki.IO;
using UnityEngine;

namespace Loki.Formats
{
    /// <summary>
    /// GRSM: static/animated 3D model. Versions 1.1 – 2.3 (RSM2 = 2.2+).
    /// Layout after Korangar's ragnarok-formats/model.rs cross-checked with roBrowserLegacy Loaders/Model.js.
    /// </summary>
    public sealed class RsmFile
    {
        public enum Shading { None = 0, Flat = 1, Smooth = 2 }

        public struct Face
        {
            public ushort V0, V1, V2;
            public ushort T0, T1, T2;
            public ushort TextureIndex; // index into Node.TextureIndices
            public int TwoSided;
            public int SmoothGroup;
            public int[] ExtraSmoothGroups; // 2.2+: optional
        }
        public struct TexCoord { public uint Color; public Vector2 UV; }
        public struct ScaleKey { public int Frame; public Vector3 Scale; }
        public struct RotKey { public int Frame; public Quaternion Rotation; }
        public struct PosKey { public int Frame; public Vector3 Position; }
        public sealed class TextureAnim { public int TextureIndex; public int Type; public List<(int frame, float value)> Frames = new List<(int, float)>(); }

        public sealed class Node
        {
            public string Name, ParentName;
            public int[] TextureIndices;      // into RsmFile.Textures (resolved for 2.3 too)
            public float[] Mat3 = new float[9]; // column-major 3x3 as stored (m00 m01 m02 m10 ...) — see RsmMeshBuilder
            public Vector3 Offset;            // translation applied together with Mat3 to the vertices
            public Vector3 Position;          // RSM1 only
            public float RotationAngle;       // RSM1 only (radians)
            public Vector3 RotationAxis;      // RSM1 only
            public Vector3 Scale = Vector3.one; // RSM1 only
            public Vector3[] Vertices;
            public TexCoord[] TexCoords;
            public Face[] Faces;
            public ScaleKey[] ScaleKeys = new ScaleKey[0];
            public RotKey[] RotKeys = new RotKey[0];
            public PosKey[] PosKeys = new PosKey[0];
            public List<TextureAnim> TextureAnims = new List<TextureAnim>();
            public List<Node> Children = new List<Node>();
            public Node Parent;
        }

        public RoVersion Version;
        public bool IsRsm2 => Version.AtLeast(2, 2);
        public int AnimationLength;   // RSM1: milliseconds; RSM2: frames (see Fps)
        public Shading ShadeType;
        public float Alpha = 1f;
        public float Fps = 60f;       // RSM2
        public List<string> Textures = new List<string>();
        public List<string> RootNodeNames = new List<string>();
        public Node[] Nodes;
        public List<Node> RootNodes = new List<Node>();
        public bool SingleNode => Nodes.Length == 1;

        /// <summary>Client wave 7 (w7data): a version newer than 2.3 or a damaged file throws <see cref="RoFormatException"/>
        /// (Formats/RoFormat.cs: the known versions and their sources) — the caller gives a safe result.</summary>
        public static RsmFile Parse(byte[] data) => RoFormat.Guard("RSM", () => ParseCore(data));

        private static RsmFile ParseCore(byte[] data)
        {
            using (var r = new RoReader(data))
            {
                string sig = r.Signature(4);
                RoFormat.Signature("RSM", sig, "GRSM", "GRSX");
                var m = new RsmFile { Version = RoVersion.MajorFirst(r) };
                var v = m.Version;
                RoFormat.CheckVersion("RSM", v, 2, 3);
                m.AnimationLength = r.S32();
                m.ShadeType = (Shading)r.S32();
                if (v.AtLeast(1, 4)) m.Alpha = r.U8() / 255f;

                if (v.Below(2, 2))
                {
                    r.Skip(16); // reserved
                    int tc = RoFormat.Count("RSM", r, r.S32(), 40, "textures");
                    for (int i = 0; i < tc; i++) m.Textures.Add(r.Str(40));
                    m.RootNodeNames.Add(r.Str(40));
                }
                else
                {
                    m.Fps = r.F32();
                    if (v.Below(2, 3))
                    {
                        int tc = RoFormat.Count("RSM", r, r.S32(), 4, "textures");
                        for (int i = 0; i < tc; i++) m.Textures.Add(Name(r));
                    }
                    int rc = RoFormat.Count("RSM", r, r.S32(), 4, "root nodes");
                    for (int i = 0; i < rc; i++) m.RootNodeNames.Add(Name(r));
                }

                int nodeCount = RoFormat.Count("RSM", r, r.S32(), 60, "nodes");
                m.Nodes = new Node[nodeCount];
                for (int n = 0; n < nodeCount; n++) m.Nodes[n] = ParseNode(r, m);

                // 1.x < 1.6 : model-level position keyframes (rare; apply to root). Kept for completeness.
                if (v.Below(1, 6) && !r.AtEnd)
                {
                    int pc = RoFormat.Count("RSM", r, r.S32(), 20, "position keys");
                    var keys = new PosKey[pc];
                    for (int i = 0; i < pc; i++) { keys[i] = new PosKey { Frame = r.S32(), Position = r.Vec3() }; r.F32(); }
                    if (m.Nodes.Length > 0 && pc > 0) m.Nodes[0].PosKeys = keys;
                }
                // volume boxes follow; not needed for rendering.

                // Resolve hierarchy.
                var byName = new Dictionary<string, Node>();
                foreach (var n in m.Nodes) if (!byName.ContainsKey(n.Name)) byName[n.Name] = n;
                foreach (var n in m.Nodes)
                {
                    if (n.ParentName != n.Name && !string.IsNullOrEmpty(n.ParentName) && byName.TryGetValue(n.ParentName, out var p) && p != n)
                    {
                        n.Parent = p; p.Children.Add(n);
                    }
                }
                foreach (var rn in m.RootNodeNames) if (byName.TryGetValue(rn, out var root)) m.RootNodes.Add(root);
                if (m.RootNodes.Count == 0)
                    foreach (var n in m.Nodes) if (n.Parent == null) m.RootNodes.Add(n);
                if (m.RootNodes.Count == 0 && m.Nodes.Length > 0) m.RootNodes.Add(m.Nodes[0]);
                return m;
            }
        }

        /// <summary>A length-prefixed name (RSM 2.2+), its length checked against what is left of the file.</summary>
        private static string Name(RoReader r) => r.Str(RoFormat.Count("RSM", r, r.U32(), 1, "name bytes"));

        private static Node ParseNode(RoReader r, RsmFile m)
        {
            var v = m.Version;
            var n = new Node();
            if (v.AtLeast(2, 2)) { n.Name = Name(r); n.ParentName = Name(r); }
            else { n.Name = r.Str(40); n.ParentName = r.Str(40); }

            int tc = RoFormat.Count("RSM", r, r.S32(), 4, "node textures");
            n.TextureIndices = new int[tc];
            for (int i = 0; i < tc; i++)
            {
                if (v.AtLeast(2, 3))
                {
                    string name = Name(r);
                    int idx = m.Textures.IndexOf(name);
                    if (idx < 0) { idx = m.Textures.Count; m.Textures.Add(name); }
                    n.TextureIndices[i] = idx;
                }
                else n.TextureIndices[i] = r.S32();
            }

            for (int i = 0; i < 9; i++) n.Mat3[i] = r.F32();
            n.Offset = r.Vec3();
            if (v.Below(2, 2))
            {
                n.Position = r.Vec3();
                n.RotationAngle = r.F32();
                n.RotationAxis = r.Vec3();
                n.Scale = r.Vec3();
            }

            int vc = RoFormat.Count("RSM", r, r.S32(), 12, "vertices");
            n.Vertices = new Vector3[vc];
            for (int i = 0; i < vc; i++) n.Vertices[i] = r.Vec3();

            int tvc = RoFormat.Count("RSM", r, r.S32(), 8, "texture coordinates");
            n.TexCoords = new TexCoord[tvc];
            for (int i = 0; i < tvc; i++)
            {
                var t = new TexCoord();
                if (v.AtLeast(1, 2)) t.Color = r.U32();
                t.UV = r.Vec2();
                n.TexCoords[i] = t;
            }

            int fc = RoFormat.Count("RSM", r, r.S32(), 20, "faces");
            n.Faces = new Face[fc];
            for (int i = 0; i < fc; i++)
            {
                int len = -1;
                if (v.AtLeast(2, 2)) len = r.S32();
                var f = new Face
                {
                    V0 = r.U16(), V1 = r.U16(), V2 = r.U16(),
                    T0 = r.U16(), T1 = r.U16(), T2 = r.U16(),
                    TextureIndex = r.U16()
                };
                r.U16(); // padding
                f.TwoSided = r.S32();
                if (v.AtLeast(1, 2))
                {
                    f.SmoothGroup = r.S32();
                    if (len > 24)
                    {
                        int extra = RoFormat.Count("RSM", r, (len - 24) / 4, 4, "smooth groups");
                        f.ExtraSmoothGroups = new int[extra];
                        for (int k = 0; k < extra; k++) f.ExtraSmoothGroups[k] = r.S32();
                    }
                }
                n.Faces[i] = f;
            }

            if (v.AtLeast(1, 6))
            {
                int sc = RoFormat.Count("RSM", r, r.S32(), 20, "scale keys");
                n.ScaleKeys = new ScaleKey[sc];
                for (int i = 0; i < sc; i++) { n.ScaleKeys[i] = new ScaleKey { Frame = r.S32(), Scale = r.Vec3() }; r.F32(); }
            }
            int rc = RoFormat.Count("RSM", r, r.S32(), 20, "rotation keys");
            n.RotKeys = new RotKey[rc];
            for (int i = 0; i < rc; i++) n.RotKeys[i] = new RotKey { Frame = r.S32(), Rotation = r.Quat() };

            if (v.AtLeast(2, 2))
            {
                int pc = RoFormat.Count("RSM", r, r.S32(), 20, "position keys");
                n.PosKeys = new PosKey[pc];
                for (int i = 0; i < pc; i++) { n.PosKeys[i] = new PosKey { Frame = r.S32(), Position = r.Vec3() }; r.F32(); }
            }
            if (v.AtLeast(2, 3))
            {
                int gc = RoFormat.Count("RSM", r, r.S32(), 8, "texture animation groups");
                for (int g = 0; g < gc; g++)
                {
                    int texIndex = r.S32();
                    int animCount = RoFormat.Count("RSM", r, r.S32(), 8, "texture animations");
                    for (int a = 0; a < animCount; a++)
                    {
                        var ta = new TextureAnim { TextureIndex = texIndex, Type = r.S32() };
                        int frames = RoFormat.Count("RSM", r, r.S32(), 8, "texture animation frames");
                        for (int k = 0; k < frames; k++) ta.Frames.Add((r.S32(), r.F32()));
                        n.TextureAnims.Add(ta);
                    }
                }
            }
            return n;
        }
    }
}
