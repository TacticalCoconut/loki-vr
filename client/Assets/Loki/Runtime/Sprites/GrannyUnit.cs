using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Loki.Granny;
using Loki.World;
using UnityEngine;

namespace Loki.Sprites
{
    /// <summary>
    /// Client wave 4b (w4granny): a unit whose body is a Granny 3-D model — the classes whose job name in the player's GRF is a
    /// "*.gr2" (the jobname tables → spritenames.txt, made on first run: Emperium 1288, the castle guardians 1285-1287, the guild flag
    /// 722, the treasure boxes 1324-1363 …). The model is read from the player's own GRF when such a unit first appears, decoded by
    /// Runtime/Granny (the C# port of granny-ro-js 1.5.0, MIT) on a worker thread, and drawn as roBrowserLegacy draws it (a
    /// reimplementation, not the official client: src/Loaders/GR2Loader.js, src/Renderer/GR2/*.js):
    ///  • only the models of rB's roster (GR2ModelRenderer.js GR2_ROSTER: guildflag90_1, empelium90_0, k/a/sguardian90_7/8/9,
    ///    treasurebox_2); any other .gr2 name keeps the 2-D stand-in (RoNames.Gr2StandIns; rB draws a Poring for dragon_5 /
    ///    Hugeling90_6, which Loki's stand-in table does too). While a model loads, and if it cannot be read or decoded, the stand-in
    ///    sprite stays (the brief's fallback; rB flags such a model missing and draws a Poring);
    ///  • the mesh packing of GR2Loader.packModel (textured meshes only, bone bindings → skeleton bones by name, the four weights
    ///    normalised, a vertex without weights bound to the first binding), the InitialPlacement of GR2Loader.ipRowFromTransform
    ///    (scale-shear · rotation · the position NEGATED, each only when its flag is set), the placement of gr2World.js
    ///    (v · ipRow · Rx(+90°) · Ry(dir·45° + 360°) · S(0.2) · T(cell centre + ground offset)) and its grounding
    ///    (computeGroundOffset: the lowest bind vertex on the ground);
    ///  • the external animation banks of gr2Banks.js (data\model\3dmob_bone\&lt;set&gt;_&lt;action&gt;.gr2 for sets 2, 7, 8, 9) grafted
    ///    at the action slots of actorAction.js (stand 0, move 1, attack 2, dead 3, damage 4); rB streams them one by one, prioritised
    ///    by the current action; Loki reads them with the model (they are small) so every action has its clip from the first frame;
    ///  • the action state machine of actorAction.js (a hard cut on every action, attack and damage / a single move return to the
    ///    standby at the clip's end, dead holds its last frame, a missing bank is the bind pose; an NPC (objecttype 6, rAthena
    ///    clif.cpp:365-375 — the guild flag) is never animated: the bind pose), the action read from the unit's ACT action group as
    ///    gr2Pack.js gr2ActionFor does, and a pose computed at most once per 1/40 s bucket (gr2Pack.js quantizePoseTime);
    ///  • the pose skinned on the CPU exactly as GR2Model.vs skins it on the GPU (Σ weight · skinning matrix, normals by the 3x3), the
    ///    textures magenta-keyed as keyGr2Magenta does (only when no pixel has alpha &lt; 255), the grey stand-in texture for a mesh whose
    ///    texture is missing, the per-model shading (Resources/Shaders/LokiGranny.shader);
    ///  • the guild flag's "Plane" mesh drawn only with the owning guild's emblem (GR2ModelRenderer.js buildEmblemTexture: 24×24 in the
    ///    top-left of a 32×32 texture, 4 bits a channel); w4social's floating banner is hidden while the flag shows it.
    /// Space: rB's world is RO's /5 with y down; Loki's is the same with y up (World/RoSpace.cs), so a vertex is flipped in y after
    /// rB's chain, and the yaw about the vertical axis is the same rotation in both.
    /// Not as in rB (Loki's choices, noted): the grounding runs over the skinned bind pose, the frame that is drawn (rB grounds the raw
    /// bind vertices, which leaves the treasure box 1.10 cells up in the air: see Place); mipmapped textures like Loki's other models; the
    /// whole model box is pointable (rB's "tout le drapeau" alternative, not its default base sphere); the pointer highlight tints the
    /// model; partially transparent units are drawn opaque (only a fully hidden unit disappears). A corpse stays 5 s (rB's
    /// remove_delay, EntityManager). Kept as rB has it although measured doubtful: the InitialPlacement's position applied negated —
    /// the Knight Guardian's bind skinning already moves it by that amount, so its feet stand 1.0 cell off its cell's centre (§3 of the progress file).
    /// </summary>
    public sealed class GrannyUnit : MonoBehaviour
    {
        // ================================================================================================ configuration
        /// <summary>Set by LokiGame.W4Granny.cs: the current map (its RSW light), the guild emblem source (Game/Guild.cs EmblemOf).</summary>
        public static MapLoader Map;
        public static Func<uint, int, UnityEngine.Sprite> EmblemSource;
        /// <summary>Off: every .gr2 unit keeps its 2-D stand-in (smoke comparisons; never set in play).</summary>
        public static bool Enabled = true;

        // rB GR2ModelRenderer.js GR2_ROSTER: the lighting profile of each model the renderer supports
        private static readonly Dictionary<string, string> Roster = new Dictionary<string, string>
        {
            { "guildflag90_1", "flag" }, { "empelium90_0", "emp" }, { "kguardian90_7", "emp" }, { "aguardian90_8", "emp" },
            { "sguardian90_9", "emp" }, { "treasurebox_2", "emp" },
        };
        // rB gr2Banks.js GR2_BANKS (set id → the external banks it ships) and actorAction.js ACTION (action → animation slot)
        private static readonly Dictionary<string, string[]> Banks = new Dictionary<string, string[]>
        {
            { "2", new[] { "dead", "damage" } }, { "7", new[] { "attack", "damage", "dead", "move" } },
            { "8", new[] { "attack", "damage", "dead", "move" } }, { "9", new[] { "attack", "damage", "dead", "move" } },
        };
        public static int SlotOf(string action) => action == "move" ? 1 : action == "attack" ? 2 : action == "dead" ? 3 : action == "damage" ? 4 : 0;

        private const float PoseHz = 40f;                   // gr2Pack.js POSE_HZ
        private const float UnitScale = 0.2f;               // gr2World.js RB_UNIT_SCALE
        private const float CorpseSecondsGr2 = 5f;          // rB Entity.js:476-480 remove_delay 5000 for a dead non-PC (no fade for a 3-D actor)

        /// <summary>The lower-cased base name of a .gr2 file ("Empelium90_0.gr2" → "empelium90_0").</summary>
        public static string BaseName(string gr2) => gr2 == null ? null : System.IO.Path.GetFileNameWithoutExtension(gr2.Replace('/', '\\')).ToLowerInvariant();
        /// <summary>rB isSupported: only the roster's models have a 3-D path.</summary>
        public static bool Supported(string gr2) => gr2 != null && Roster.ContainsKey(BaseName(gr2));

        // ================================================================================================ one model (per .gr2 file)
        public enum State { Loading, Ready, Failed }

        /// <summary>rB packModel's per-mesh arrays (GR2Loader.js).</summary>
        public sealed class Sub
        {
            public string Name, TexFile; public bool Emblem; public int VCount;
            public float[] Bind, Nrm, Uv, Bw; public int[] Bidx; public int[] Indices;
        }

        public sealed class Model
        {
            public string Name, Path, Profile;
            public volatile State Status = State.Loading;
            public string Error;
            public GrParsed Parsed;
            public Sub[] Subs;
            public int BoneCount;
            public double[] IpRow;
            /// <summary>Skinned model space → the unit's space (column-vector), without the yaw; ground offset in cells.</summary>
            public Matrix4x4 ToUnit, NormalToUnit; public float Ground;
            public float Height, HalfWidth;
            public Texture2D[] Textures; public string[] TextureNames; public string[] TextureSha;
            public Dictionary<string, Texture2D> ByFile; public Material[] Materials;
            public double DecodeMs; public string BankSummary = "";
            internal byte[][] Pixels; internal int[] TexW, TexH;
            internal Task Work;
            public bool Uploaded;
        }

        private static readonly Dictionary<string, Model> Models = new Dictionary<string, Model>();
        public static IReadOnlyDictionary<string, Model> LoadedModels => Models;
        /// <summary>The live instances (smoke and diagnostics).</summary>
        public static readonly List<GrannyUnit> Live = new List<GrannyUnit>();
        private static Shader _shader;
        private static Texture2D _grey, _missing;

        /// <summary>The model for a .gr2 name: read from the GRF now (main thread: RoFileSystem's caches), decoded on a worker thread.</summary>
        private static Model Acquire(string gr2, RoAssets assets)
        {
            string name = BaseName(gr2);
            if (Models.TryGetValue(name, out var m)) return m;
            byte[] main = null; string err = null;
            try { main = assets.Fs.Read("data\\model\\3dmob\\" + name + ".gr2"); } catch (Exception e) { err = "read: " + e.Message; }
            return Start(name, main, err, assets);
        }

        /// <summary>A model from bytes the caller already holds (tests: a damaged copy proves the fallback), its banks from the GRF.</summary>
        public static Model LoadFromBytes(string gr2, byte[] main, RoAssets assets)
        {
            string name = BaseName(gr2);
            if (!Roster.ContainsKey(name)) return null;
            Models.Remove(name);
            return Start(name, main, null, assets);
        }

        /// <summary>Drops a cached model (the next unit that needs it reads it again). Units already drawing it keep their meshes.</summary>
        public static void Forget(string gr2) => Models.Remove(BaseName(gr2));

        private static Model Start(string name, byte[] main, string err, RoAssets assets)
        {
            var m = new Model { Name = name, Path = "data\\model\\3dmob\\" + name + ".gr2", Profile = Roster[name], Error = err };
            Models[name] = m;
            if (main == null) { m.Error ??= "not in the client data"; m.Status = State.Failed; Debug.LogWarning($"[W4Granny] {m.Path}: {m.Error}; the 2-D stand-in stays"); return m; }
            // rB gr2Banks.js bankPathsFor: the set id is the file name's trailing number
            var banks = new List<(string action, string path, byte[] bytes)>();
            var mm = Regex.Match(name, @"_(\d+)$");
            if (mm.Success && Banks.TryGetValue(mm.Groups[1].Value, out var list))
                foreach (var a in list)
                {
                    string bp = "data\\model\\3dmob_bone\\" + mm.Groups[1].Value + "_" + a + ".gr2";
                    byte[] bb = null; try { bb = assets.Fs.Read(bp); } catch (Exception) { }
                    banks.Add((a, bp, bb));
                }
            m.Work = Task.Run(() => Decode(m, main, banks));
            return m;
        }

        /// <summary>Everything that needs no Unity API: parse, graft the banks, pack the meshes, the placement, the texture pixels.</summary>
        private static void Decode(Model m, byte[] main, List<(string action, string path, byte[] bytes)> banks)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var p = GrannyReader.ParseAll(main);
                var sb = new System.Text.StringBuilder();
                foreach (var (action, path, bytes) in banks)
                {
                    if (bytes == null) { sb.Append(action).Append(" missing, "); continue; } // rB pumpBanks: a 404 drops the name
                    try
                    {
                        var b = GrannyReader.ParseAnimated(bytes);
                        if (b.Animations.Count == 0) { sb.Append(action).Append(" empty, "); continue; }
                        int slot = SlotOf(action);
                        while (p.Animations.Count <= slot) p.Animations.Add(null);
                        p.Animations[slot] = b.Animations[0]; // type.parsed.animations[ACTION[name].idx] = bank.animations[0]
                        sb.Append(action).Append(' ').Append(b.Animations[0].Duration.ToString("0.###")).Append(" s, ");
                    }
                    catch (Exception e) { sb.Append(action).Append(" failed (").Append(e.Message).Append("), "); }
                }
                m.BankSummary = sb.Length > 0 ? sb.ToString(0, sb.Length - 2) : "none";
                m.Parsed = p;
                Pack(m, p);
                Place(m, p);
                // textures: rB makeTypeTextures (one per decoded texture, keyed by its name) + keyGr2Magenta
                int n = p.Textures.Count;
                m.Pixels = new byte[n][]; m.TexW = new int[n]; m.TexH = new int[n]; m.TextureNames = new string[n]; m.TextureSha = new string[n];
                using (var sha = SHA256.Create())
                    for (int i = 0; i < n; i++)
                    {
                        var t = p.Textures[i];
                        m.TextureSha[i] = t.Pixels != null ? BitConverter.ToString(sha.ComputeHash(t.Pixels)).Replace("-", "").ToLowerInvariant() : null;
                        m.Pixels[i] = t.Pixels != null ? KeyMagenta(t.Pixels) : null; m.TexW[i] = t.Width; m.TexH[i] = t.Height; m.TextureNames[i] = t.Name;
                    }
                m.DecodeMs = sw.Elapsed.TotalMilliseconds;
                m.Status = State.Ready;
            }
            catch (Exception e) { m.Error = e.GetType().Name + ": " + e.Message; m.Status = State.Failed; }
        }

        /// <summary>rB GR2ModelRenderer.js keyGr2Magenta: a source with any alpha below 255 is used as is; else magenta (≥ 248, &lt; 8,
        /// ≥ 248) becomes a fully transparent black pixel.</summary>
        private static byte[] KeyMagenta(byte[] px)
        {
            for (int i = 3; i < px.Length; i += 4) if (px[i] < 255) return px;
            var o = (byte[])px.Clone();
            for (int i = 0; i < o.Length; i += 4)
                if (o[i] >= 248 && o[i + 1] < 8 && o[i + 2] >= 248) { o[i] = 0; o[i + 1] = 0; o[i + 2] = 0; o[i + 3] = 0; }
            return o;
        }

        /// <summary>rB GR2Loader.packModel.</summary>
        private static void Pack(Model m, GrParsed p)
        {
            if (p.Skeletons.Count == 0) throw new GrannyException("GR2Loader: model has no skeleton");
            var bones = p.Skeletons[0].Bones;
            var byName = new Dictionary<string, int>();
            for (int i = 0; i < bones.Count; i++) byName[bones[i].Name] = i;
            m.BoneCount = bones.Count;
            var subs = new List<Sub>();
            foreach (var mesh in p.Meshes)
            {
                if (mesh.Materials == null || mesh.Materials.Count == 0 || string.IsNullOrEmpty(mesh.Materials[0].TextureFile)) continue; // matless proxies dropped
                var l2g = new int[mesh.BoneBindings.Count];
                for (int k = 0; k < l2g.Length; k++) l2g[k] = byName.TryGetValue(mesh.BoneBindings[k], out int gi) ? gi : 0;
                int N = mesh.VertexCount;
                var s = new Sub
                {
                    Name = mesh.Name, VCount = N, Bind = new float[N * 3], Bidx = new int[N * 4], Bw = new float[N * 4], Uv = new float[N * 2],
                    Nrm = new float[N * 3], TexFile = mesh.Materials[0].TextureFile, Emblem = Regex.IsMatch(mesh.Name ?? "", "Plane", RegexOptions.IgnoreCase),
                };
                for (int i = 0; i < N; i++)
                {
                    var pp = mesh.Positions[i];
                    s.Bind[i * 3] = pp[0]; s.Bind[i * 3 + 1] = pp[1]; s.Bind[i * 3 + 2] = pp[2];
                    var nn = i < mesh.Normals.Count ? mesh.Normals[i] : new float[] { 0, 0, 1 };
                    s.Nrm[i * 3] = nn[0]; s.Nrm[i * 3 + 1] = nn[1]; s.Nrm[i * 3 + 2] = nn[2];
                    var t = i < mesh.Uvs.Count ? mesh.Uvs[i] : new float[] { 0, 0 };
                    s.Uv[i * 2] = t[0]; s.Uv[i * 2 + 1] = t[1];
                    var vw = mesh.VertexWeights != null && i < mesh.VertexWeights.Length ? mesh.VertexWeights[i] : null;
                    double sum = 0;
                    for (int j = 0; j < 4; j++)
                    {
                        if (vw == null || j >= vw.Count) continue;
                        var e = vw[j];
                        s.Bidx[i * 4 + j] = e.BoneIndex < l2g.Length ? l2g[e.BoneIndex] : 0;
                        s.Bw[i * 4 + j] = (float)e.Weight; sum += e.Weight;
                    }
                    if (sum < 1e-6) { s.Bidx[i * 4] = l2g.Length > 0 ? l2g[0] : 0; s.Bw[i * 4] = 1; sum = 1; } // a rigid vertex: the first binding
                    for (int j = 0; j < 4; j++) s.Bw[i * 4 + j] = (float)(s.Bw[i * 4 + j] / sum);
                }
                s.Indices = new int[mesh.Indices.Length];
                for (int k = 0; k < s.Indices.Length; k++) s.Indices[k] = (ushort)mesh.Indices[k]; // new Uint16Array(m.indices)
                subs.Add(s);
            }
            m.Subs = subs.ToArray();
        }

        // ---- the placement (row-vector matrices, gr2Math.js)
        private static double[] Mul(double[] a, double[] b)
        {
            var o = new double[16];
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) { double s = 0; for (int k = 0; k < 4; k++) s += a[r * 4 + k] * b[k * 4 + c]; o[r * 4 + c] = s; }
            return o;
        }
        private static double[] Identity() => new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };

        /// <summary>GR2Loader.ipRowFromTransform: models[0]'s InitialPlacement, each component only when its flag is set.</summary>
        private static double[] IpRow(GrTransform t)
        {
            if (t == null || t.Flags == 0) return Identity();
            var M = Identity();
            if ((t.Flags & 4) != 0) { var s = t.ScaleShear; M = Mul(M, new[] { s[0], s[1], s[2], 0, s[3], s[4], s[5], 0, s[6], s[7], s[8], 0, 0, 0, 0, 1 }); }
            if ((t.Flags & 2) != 0)
            {
                double x = t.Orientation[0], y = t.Orientation[1], z = t.Orientation[2], w = t.Orientation[3];
                double xx = x * x, yy = y * y, zz = z * z, xy = x * y, xz = x * z, yz = y * z, wx = w * x, wy = w * y, wz = w * z;
                M = Mul(M, new[] { 1 - 2 * (yy + zz), 2 * (xy + wz), 2 * (xz - wy), 0, 2 * (xy - wz), 1 - 2 * (xx + zz), 2 * (yz + wx), 0, 2 * (xz + wy), 2 * (yz - wx), 1 - 2 * (xx + yy), 0, 0, 0, 0, 1 });
            }
            if ((t.Flags & 1) != 0) { var q = t.Position; M = Mul(M, new[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -q[0], -q[1], -q[2], 1 }); }
            return M;
        }

        /// <summary>gr2World.js worldCore without the yaw and the translation, flipped into Loki's y-up space; the grounding
        /// (computeGroundOffset: the lowest vertex on the ground) — over the SKINNED bind pose, the frame the model is drawn in, where rB
        /// grounds the raw bind vertices. The two agree for the Emperium, the flag and the Archer / Soldier Guardians (their bind
        /// skinning is the identity); for the treasure box and the Knight Guardian the bind skinning already holds the inverse of
        /// their InitialPlacement (measured on granny-ro-js's own output: skinned − raw = 1.731 / 1.451 cells after rB's chain), so rB's
        /// rule leaves the drawn treasure box 1.10 cells above its cell. rB's comment asks for "the model's real draw frame"; Loki
        /// grounds that frame (design notes §3). The Knight Guardian's height is the same either way.</summary>
        private static void Place(Model m, GrParsed p)
        {
            m.IpRow = IpRow(p.Models != null && p.Models.Count > 0 ? p.Models[0].InitialPlacement : null);
            double a = Math.PI / 2, c = Math.Cos(a), s = Math.Sin(a);
            var rx = new[] { 1, 0, 0, 0, 0, c, s, 0, 0, -s, c, 0, 0, 0, 0, 1 };
            var sc = new double[] { UnitScale, 0, 0, 0, 0, UnitScale, 0, 0, 0, 0, UnitScale, 0, 0, 0, 0, 1 };
            var W = Mul(Mul(m.IpRow, rx), sc); // v' = v · W (rB's y down)
            // column-vector, y flipped: unit = F · Wᵀ · v
            var L = new Matrix4x4();
            for (int r = 0; r < 4; r++) for (int col = 0; col < 4; col++) L[r, col] = (float)(W[col * 4 + r] * (r == 1 ? -1 : 1));
            m.ToUnit = L;
            m.NormalToUnit = InverseTranspose3(L); // GR2Model.vs uNormalMat: the inverse-transpose
            var bind = GrannyPose.PoseAt(p, -1, 0).SkinningMatrices;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity, half = 0f;
            foreach (var sub in m.Subs)
                for (int i = 0; i < sub.VCount; i++)
                {
                    SkinVertex(sub, i, bind, out var sp, out _);
                    var v = L.MultiplyPoint3x4(sp);
                    if (v.y < minY) minY = v.y; if (v.y > maxY) maxY = v.y;
                    half = Mathf.Max(half, Mathf.Abs(v.x), Mathf.Abs(v.z));
                }
            m.Ground = float.IsInfinity(minY) ? 0f : -minY;
            m.Height = float.IsInfinity(maxY) ? 1f : maxY - minY; m.HalfWidth = half;
        }

        /// <summary>The inverse-transpose of the upper 3x3 (the normal matrix), written out (this runs on the worker thread).</summary>
        private static Matrix4x4 InverseTranspose3(Matrix4x4 L)
        {
            double a00 = L.m00, a01 = L.m01, a02 = L.m02, a10 = L.m10, a11 = L.m11, a12 = L.m12, a20 = L.m20, a21 = L.m21, a22 = L.m22;
            double c00 = a11 * a22 - a12 * a21, c01 = a12 * a20 - a10 * a22, c02 = a10 * a21 - a11 * a20;
            double det = a00 * c00 + a01 * c01 + a02 * c02;
            var n = Matrix4x4.identity;
            if (Math.Abs(det) < 1e-30) return n;
            double k = 1 / det;
            // inverse = adjugate / det; its transpose has the cofactors in place
            n.m00 = (float)(c00 * k); n.m01 = (float)(c01 * k); n.m02 = (float)(c02 * k);
            n.m10 = (float)((a02 * a21 - a01 * a22) * k); n.m11 = (float)((a00 * a22 - a02 * a20) * k); n.m12 = (float)((a01 * a20 - a00 * a21) * k);
            n.m20 = (float)((a01 * a12 - a02 * a11) * k); n.m21 = (float)((a02 * a10 - a00 * a12) * k); n.m22 = (float)((a00 * a11 - a01 * a10) * k);
            return n;
        }

        /// <summary>GR2Model.vs for one vertex: skin = Σ wᵢ·Bᵢ (column-major float[16]); position = skin·(p,1); normal = mat3(skin)·n.</summary>
        private static void SkinVertex(Sub s, int i, float[][] B, out Vector3 pos, out Vector3 nrm)
        {
            float m0 = 0, m1 = 0, m2 = 0, m4 = 0, m5 = 0, m6 = 0, m8 = 0, m9 = 0, m10 = 0, m12 = 0, m13 = 0, m14 = 0;
            for (int j = 0; j < 4; j++)
            {
                float w = s.Bw[i * 4 + j]; if (w == 0f) continue;
                int bi = s.Bidx[i * 4 + j]; if (bi < 0 || bi >= B.Length) continue;
                var b = B[bi];
                m0 += w * b[0]; m1 += w * b[1]; m2 += w * b[2]; m4 += w * b[4]; m5 += w * b[5]; m6 += w * b[6];
                m8 += w * b[8]; m9 += w * b[9]; m10 += w * b[10]; m12 += w * b[12]; m13 += w * b[13]; m14 += w * b[14];
            }
            float x = s.Bind[i * 3], y = s.Bind[i * 3 + 1], z = s.Bind[i * 3 + 2];
            pos = new Vector3(m0 * x + m4 * y + m8 * z + m12, m1 * x + m5 * y + m9 * z + m13, m2 * x + m6 * y + m10 * z + m14);
            float nx = s.Nrm[i * 3], ny = s.Nrm[i * 3 + 1], nz = s.Nrm[i * 3 + 2];
            nrm = new Vector3(m0 * nx + m4 * ny + m8 * nz, m1 * nx + m5 * ny + m9 * nz, m2 * nx + m6 * ny + m10 * nz);
        }

        /// <summary>Main thread, once per model: the textures (rB makeTypeTextures: one per texture name, a grey stand-in) and materials.</summary>
        private static void Upload(Model m)
        {
            if (m.Uploaded) return;
            m.Uploaded = true;
            if (_shader == null) _shader = Shader.Find("Loki/Granny");
            if (_grey == null) { _grey = Solid(180, 180, 185, "gr2_grey"); _missing = Solid(200, 200, 200, "gr2_missing"); } // TEX_GREY_PX / TEX_MISSING_PX
            m.ByFile = new Dictionary<string, Texture2D>();
            m.Textures = new Texture2D[m.Pixels.Length];
            for (int i = 0; i < m.Pixels.Length; i++)
            {
                Texture2D tex;
                if (m.Pixels[i] != null)
                {
                    // rows as decoded (the top row first): Unity's v = 0 is the first row uploaded, as D3D / rB's un-flipped upload
                    tex = new Texture2D(m.TexW[i], m.TexH[i], TextureFormat.RGBA32, true, false) { name = "gr2_" + m.Name + "_" + i };
                    tex.SetPixelData(m.Pixels[i], 0); tex.Apply(true, true);
                    tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
                }
                else tex = _missing;
                m.Textures[i] = tex; m.ByFile[m.TextureNames[i]] = tex;
            }
            m.Pixels = null; // uploaded
            m.Materials = new Material[m.Subs.Length];
            for (int i = 0; i < m.Subs.Length; i++)
            {
                var mat = new Material(_shader) { name = "gr2_" + m.Name + "_" + m.Subs[i].Name };
                mat.mainTexture = m.ByFile.TryGetValue(m.Subs[i].TexFile, out var t) ? t : _grey;
                m.Materials[i] = mat;
            }
        }

        private static Texture2D Solid(byte r, byte g, byte b, string name)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = name };
            t.SetPixels32(new[] { new Color32(r, g, b, 255) }); t.Apply(false, true);
            return t;
        }

        // ================================================================================================ the unit side
        /// <summary>Called at the end of Unit.ApplyLook (non-player units): attaches, keeps or drops the 3-D body.</summary>
        public static void Apply(Unit u, RoAssets assets)
        {
            if (u == null) return;
            string gr2 = Enabled ? RoNames.Gr2Name(u.Job) : null;
            var g = u.GetComponent<GrannyUnit>();
            if (gr2 == null || !Supported(gr2) || assets == null)
            {
                if (g != null) { g.Drop(); Destroy(g); }
                return;
            }
            if (g == null) { g = u.gameObject.AddComponent<GrannyUnit>(); g._unit = u; }
            var m = Acquire(gr2, assets);
            if (g._model != m) { g.Drop(); g._model = m; g._failLogged = false; }
            if (g._built) g.HideStandIn(); // a re-applied look put the stand-in back
        }

        /// <summary>A GR2 unit's corpse stays 5 s (rB Entity.js:476-480), every other unit's the caller's default.</summary>
        public static float CorpseSeconds(Unit u, float otherwise)
        {
            var g = u != null ? u.GetComponent<GrannyUnit>() : null;
            return g != null && g.Drawn ? CorpseSecondsGr2 : otherwise;
        }

        private Unit _unit;
        private Model _model;
        private bool _built;
        private GameObject _root;
        private MeshFilter[] _filters; private MeshRenderer[] _renderers; private Mesh[] _meshes;
        private Vector3[][] _pos, _nrm;
        private MaterialPropertyBlock _mpb;
        private Texture2D _emblemTex; private UnityEngine.Sprite _emblemFrom;
        private string _action = "stand"; private double _startMs; private bool _loop;
        private int _lastGroup = -1, _lastPlayCount = -1;
        private int _poseAnim = int.MinValue; private long _poseBucket = long.MinValue;

        /// <summary>True once the 3-D body is drawn (the stand-in hidden).</summary>
        public bool Drawn => _built;
        public Model ModelInfo => _model;
        public string ModelName => _model?.Name;
        public State Status => _model?.Status ?? State.Failed;
        /// <summary>The animation slot drawn (-1: the bind pose), its time (s), the action (actorAction.js names), poses computed.</summary>
        public int AnimIndex { get; private set; } = -1;
        public double AnimTime { get; private set; }
        public string Action => _action;
        public int StandbyIndex => _unit != null && _unit.ObjectType == 6 ? -1 : 0; // attach(): NPC → -1 (the bind pose), else 0
        public int PosesComputed { get; private set; }
        /// <summary>Every "action:slot" drawn so far (the smoke clears it between steps).</summary>
        public readonly HashSet<string> Seen = new HashSet<string>();
        /// <summary>The largest distance (cells) a vertex moved in the last pose update.</summary>
        public float LastMaxMove { get; private set; }
        public bool EmblemShown { get; private set; }
        public int VertexCount { get { int n = 0; if (_model?.Subs != null) foreach (var s in _model.Subs) n += s.VCount; return n; } }
        /// <summary>A vertex of the drawn mesh in the unit's space (smoke: the pose at work).</summary>
        public Vector3 Vertex(int sub, int i) => _pos != null && sub < _pos.Length && i < _pos[sub].Length ? _pos[sub][i] : Vector3.zero;

        private void OnEnable() { if (!Live.Contains(this)) Live.Add(this); }
        private void OnDisable() { Live.Remove(this); }
        private void OnDestroy() { Drop(); }

        private void Drop()
        {
            if (_meshes != null) foreach (var m in _meshes) if (m != null) Destroy(m);
            if (_root != null) Destroy(_root);
            if (_emblemTex != null) Destroy(_emblemTex);
            _meshes = null; _root = null; _emblemTex = null; _emblemFrom = null; _built = false; EmblemShown = false;
            if (_unit != null) { _unit.SizeOverride = null; SuppressBanner(false); if (_unit.Sprite != null) _unit.Sprite.ModelBody = false; }
        }

        private void HideStandIn()
        {
            if (_unit?.Sprite == null) return;
            _unit.Sprite.SetBody(null, null); _unit.Sprite.ClearAttachments();
            _unit.Sprite.ModelBody = true; // client wave 5: the sprite leaves the action's timing to this model (ActorSprite.LateUpdate)
        }

        private void Build()
        {
            var m = _model;
            Upload(m);
            _root = new GameObject("gr2:" + m.Name);
            _root.transform.SetParent(transform, false);
            int n = m.Subs.Length;
            _filters = new MeshFilter[n]; _renderers = new MeshRenderer[n]; _meshes = new Mesh[n]; _pos = new Vector3[n][]; _nrm = new Vector3[n][];
            for (int i = 0; i < n; i++)
            {
                var s = m.Subs[i];
                var go = new GameObject(s.Name); go.transform.SetParent(_root.transform, false);
                _filters[i] = go.AddComponent<MeshFilter>(); _renderers[i] = go.AddComponent<MeshRenderer>();
                _renderers[i].sharedMaterial = m.Materials[i];
                _renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; _renderers[i].receiveShadows = false;
                var mesh = new Mesh { name = "gr2_" + m.Name + "_" + s.Name };
                mesh.MarkDynamic();
                _pos[i] = new Vector3[s.VCount]; _nrm[i] = new Vector3[s.VCount];
                var uv = new Vector2[s.VCount];
                for (int k = 0; k < s.VCount; k++) uv[k] = new Vector2(s.Uv[k * 2], s.Uv[k * 2 + 1]);
                mesh.vertices = _pos[i]; mesh.normals = _nrm[i]; mesh.uv = uv;
                mesh.SetTriangles(s.Indices, 0, false);
                _meshes[i] = mesh; _filters[i].sharedMesh = mesh;
                if (s.Emblem) _renderers[i].enabled = false; // the guild emblem's plane: only with an emblem (render(): sm.emblem)
            }
            _mpb = new MaterialPropertyBlock();
            _built = true;
            HideStandIn();
            _unit.SizeOverride = new Vector2(Mathf.Clamp(m.HalfWidth * 2f, 0.8f, 4f), Mathf.Clamp(m.Height, 0.8f, 6f));
            if (_unit.Overhead != null) _unit.Overhead.HeightCells = Mathf.Max(_unit.Overhead.HeightCells, m.Height + 0.3f);
            _poseAnim = int.MinValue; _lastPlayCount = -1;
            if (LoggedModels.Add(m.Name)) // once per model and session: a guild town shows a dozen flags on every visit
                Debug.Log($"[W4Granny] unit {_unit.Id} (class {_unit.Job}, objecttype {_unit.ObjectType}): {m.Path} drawn in 3-D — {m.Subs.Length} meshes, {VertexCount} vertices, " +
                          $"{m.BoneCount} bones, {m.Textures.Length} textures, animations [{AnimList(m)}], banks: {m.BankSummary}; decoded in {m.DecodeMs:F1} ms; " +
                          $"{m.Height:F2} cells tall, ground offset {m.Ground:F3}; standby {(StandbyIndex < 0 ? "the bind pose (an NPC)" : "clip 0")}");
        }
        private static readonly HashSet<string> LoggedModels = new HashSet<string>();

        private static string AnimList(Model m)
        {
            var a = m.Parsed.Animations; var parts = new List<string>();
            for (int i = 0; i < a.Count; i++) parts.Add(i + ":" + (a[i] == null ? "-" : a[i].Duration.ToString("0.###") + "s"));
            return string.Join(" ", parts);
        }

        // ------------------------------------------------------------------ actorAction.js
        private static string ActionFor(int group)
        {
            // gr2Pack.js gr2ActionFor over the monster action groups (ActorSprite.MonsterAction): DIE, HURT, ATTACK, WALK, else stand
            switch (group)
            {
                case (int)ActorSprite.MonsterAction.Dead: return "dead";
                case (int)ActorSprite.MonsterAction.Hurt: return "damage";
                case (int)ActorSprite.MonsterAction.Attack: return "attack";
                case (int)ActorSprite.MonsterAction.Walk: return "move";
                default: return "stand";
            }
        }

        private void SetAction(string name, double nowMs) { _action = name; _startMs = nowMs; _loop = false; }

        private int Present(int idx) => idx >= 0 && idx < _model.Parsed.Animations.Count && _model.Parsed.Animations[idx] != null ? idx : -1;
        private double DurationOf(int idx) { double d = _model.Parsed.Animations[idx].Duration; return d != 0 ? d : 1; } // `.duration || 1`

        /// <summary>actorPose: (animation slot, time) for now; mutates the action on a revert, as rB does.</summary>
        private (int, double) ActorPose(double nowMs, int standbyIdx)
        {
            double elapsed = (nowMs - _startMs) / 1000.0;
            if (_action == "stand")
            {
                if (standbyIdx < 0) return (-1, 0);
                double sd = Present(standbyIdx) >= 0 ? DurationOf(standbyIdx) : 1;
                return (standbyIdx, elapsed % sd);
            }
            int idx = SlotOf(_action); bool present = Present(idx) >= 0;
            if (_action == "attack")
            {
                if (!present) return (-1, 0);
                double dur = DurationOf(idx);
                if (elapsed >= dur) { SetAction("stand", nowMs); return ActorPose(nowMs, standbyIdx); }
                return (idx, elapsed);
            }
            if (_action == "dead")
            {
                if (!present) return (-1, 0);
                return (idx, Math.Min(elapsed, DurationOf(idx)));
            }
            if (!present) { SetAction("stand", nowMs); return (-1, 0); }
            double durDM = DurationOf(idx);
            if (_loop) return (idx, elapsed % durDM);
            if (elapsed >= durDM) { SetAction("stand", nowMs); return ActorPose(nowMs, standbyIdx); }
            return (idx, elapsed);
        }

        // ------------------------------------------------------------------ per frame
        private void LateUpdate()
        {
            if (_unit == null || _model == null) return;
            if (!_built)
            {
                if (_model.Status == State.Loading) return;
                if (_model.Status == State.Failed)
                {
                    if (!_failLogged) { _failLogged = true; Debug.LogWarning($"[W4Granny] {_model.Path}: {_model.Error}; unit {_unit.Id} keeps its 2-D stand-in"); }
                    return;
                }
                Build();
            }
            // yaw: the unit's facing on the map (gr2World.js Ry((dir·45 + 360)°)); the unit's own transform turns to the camera for sprites
            var parent = transform.parent;
            float yaw = _unit.BodyYaw(); // client wave 10 (w10move): the facing x 45 as before in Classic; in Smooth mode it turns smoothly (Sprites/Unit.Smooth.cs)
            _root.transform.rotation = (parent != null ? parent.rotation : Quaternion.identity) * Quaternion.Euler(0f, yaw, 0f);
            _root.transform.localPosition = Vector3.zero;

            // the action (syncFromEntity): a new name or the same action issued again cuts the clip; an NPC is never driven
            double now = Time.timeAsDouble * 1000.0;
            int standby = StandbyIndex;
            if (standby >= 0)
            {
                int group = _unit.Sprite.ActionGroup, pc = _unit.Sprite.PlayCount;
                string name = ActionFor(group);
                if (_lastPlayCount < 0) { SetAction(name, now); _loop = name == "move"; }
                else if (group != _lastGroup || pc != _lastPlayCount) { SetAction(name, now); _loop = name == "move"; }
                _lastGroup = group; _lastPlayCount = pc;
            }
            var (idx, t) = ActorPose(now, standby);
            AnimIndex = idx; AnimTime = t; Seen.Add(_action + ":" + idx);
            long bucket = (long)Math.Floor(t * PoseHz + 0.5); // Math.round(t * 40)
            if (idx != _poseAnim || bucket != _poseBucket)
            {
                _poseAnim = idx; _poseBucket = bucket;
                Skin(GrannyPose.PoseAt(_model.Parsed, idx < 0 ? -1 : idx, idx < 0 ? 0 : t));
            }
            Shade();
        }
        private bool _failLogged;
        /// <summary>True when this unit's model could not be read or decoded: its 2-D stand-in stays.</summary>
        public bool FellBack => _model != null && _model.Status == State.Failed;

        /// <summary>GR2Model.vs on the CPU: every vertex skinned (SkinVertex), then carried into the unit's space; normals by the normal matrix.</summary>
        private void Skin(GrPose pose)
        {
            PosesComputed++;
            var B = pose.SkinningMatrices; var m = _model; var L = m.ToUnit; var NL = m.NormalToUnit;
            float maxMove = 0f; var off = new Vector3(0f, m.Ground, 0f);
            for (int si = 0; si < m.Subs.Length; si++)
            {
                var s = m.Subs[si]; var P = _pos[si]; var Nn = _nrm[si];
                for (int i = 0; i < s.VCount; i++)
                {
                    SkinVertex(s, i, B, out var sp, out var sn);
                    var np = L.MultiplyPoint3x4(sp) + off;
                    if (PosesComputed > 1) { float d = (np - P[i]).magnitude; if (d > maxMove) maxMove = d; }
                    P[i] = np;
                    Nn[i] = NL.MultiplyVector(sn).normalized;
                }
                _meshes[si].vertices = P; _meshes[si].normals = Nn; _meshes[si].RecalculateBounds();
            }
            LastMaxMove = maxMove;
        }

        /// <summary>Per-model lighting (GR2ModelRenderer.js render(): 'flag' diffuse 100/255 + the map ambient in grey, 'emp' 128/255 +
        /// 127/255), the map's light opacity, the emblem plane, visibility and the pointer highlight.</summary>
        private void Shade()
        {
            var rsw = Map != null && Map.Current != null ? Map.Current.Rsw : null;
            float opacity = rsw != null ? rsw.LightOpacity : 1f;
            Color diffuse, ambient;
            if (_model.Profile == "flag")
            {
                diffuse = new Color(100 / 255f, 100 / 255f, 100 / 255f);
                var a = rsw != null ? rsw.LightAmbient : new Color(127 / 255f, 127 / 255f, 127 / 255f);
                float g = 0.299f * a.r + 0.587f * a.g + 0.114f * a.b; // grayBroadcast (Rec.601)
                ambient = new Color(g, g, g);
            }
            else { diffuse = new Color(128 / 255f, 128 / 255f, 128 / 255f); ambient = new Color(127 / 255f, 127 / 255f, 127 / 255f); }
            var tint = _unit.Sprite.Tint; var hover = _unit.Sprite.GetTint(ActorSprite.TintFactor.Hover);
            bool visible = !_unit.Unseen && tint.a > 0.01f;
            // the guild emblem (per unit: two flags of two guilds show two emblems)
            UnityEngine.Sprite emb = null;
            bool hasPlane = false; foreach (var s in _model.Subs) if (s.Emblem) hasPlane = true;
            if (hasPlane && _unit.GuildId != 0 && EmblemSource != null) emb = EmblemSource(_unit.GuildId, _unit.GuildEmblem);
            if (emb != _emblemFrom) { _emblemFrom = emb; BuildEmblem(emb); }
            EmblemShown = _emblemTex != null && visible;
            SuppressBanner(EmblemShown);
            for (int i = 0; i < _renderers.Length; i++)
            {
                var s = _model.Subs[i];
                _renderers[i].enabled = visible && (!s.Emblem || _emblemTex != null);
                _renderers[i].GetPropertyBlock(_mpb);
                _mpb.SetColor("_GrDiffuse", diffuse); _mpb.SetColor("_GrAmbient", ambient); _mpb.SetFloat("_LightOpacity", opacity);
                _mpb.SetColor("_Hover", new Color(hover.r, hover.g, hover.b, 1f));
                if (s.Emblem && _emblemTex != null) _mpb.SetTexture("_MainTex", _emblemTex);
                _renderers[i].SetPropertyBlock(_mpb);
            }
        }

        /// <summary>buildEmblemTexture: the emblem at 24×24 (nearest) in the top-left of a 32×32 texture, the rest transparent, every
        /// channel cut to 4 bits and widened back (A4R4G4B4, ×0x11).</summary>
        private void BuildEmblem(UnityEngine.Sprite emb)
        {
            if (_emblemTex != null) { Destroy(_emblemTex); _emblemTex = null; }
            if (emb == null || emb.texture == null || !emb.texture.isReadable) return;
            var src = emb.texture; int sw = src.width, sh = src.height;
            var sp = src.GetPixels32(); // Unity rows: bottom first
            const int POT = 32, SRC = 24;
            var px = new byte[POT * POT * 4];
            for (int y = 0; y < SRC; y++)
                for (int x = 0; x < SRC; x++)
                {
                    int sx = Mathf.Min(sw - 1, (int)((x + 0.5f) * sw / SRC)), syTop = Mathf.Min(sh - 1, (int)((y + 0.5f) * sh / SRC));
                    var c = sp[(sh - 1 - syTop) * sw + sx];
                    int o = (y * POT + x) * 4; // row 0 = the top row, as a canvas's getImageData
                    px[o] = (byte)((c.r >> 4) * 0x11); px[o + 1] = (byte)((c.g >> 4) * 0x11); px[o + 2] = (byte)((c.b >> 4) * 0x11); px[o + 3] = (byte)((c.a >> 4) * 0x11);
                }
            _emblemTex = new Texture2D(POT, POT, TextureFormat.RGBA32, false, false) { name = "gr2_emblem_" + _unit.GuildId, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _emblemTex.SetPixelData(px, 0); _emblemTex.Apply(false, false);
        }

        private void SuppressBanner(bool on)
        {
            var b = _unit != null ? _unit.GetComponent<Loki.UI.EmblemBanner>() : null;
            if (b != null) b.Suppressed = on;
        }
    }
}
