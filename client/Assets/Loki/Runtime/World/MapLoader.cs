using System.Collections.Generic;
using System.Diagnostics;
using Loki.Formats;
using Loki.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Loki.World
{
    /// <summary>
    /// Loads an RO map (rsw + gnd + gat) from the client data and builds it under this transform in Loki space.
    /// Scale the parent transform to turn it into a diorama.
    /// </summary>
    public sealed class MapLoader : MonoBehaviour
    {
        public sealed class LoadedMap
        {
            public string Name;
            /// <summary>warch2: the map whose files these are: <see cref="Name"/> itself, or the real map the game's name table names for
            /// it (new_1-1 -> new_zone01: IO/RoLookup.cs). Architect's fix is the real map's (its GND / GAT hashes guard it).</summary>
            public string FileName;
            public RswFile Rsw; public GndFile Gnd; public GatFile Gat;
            public GameObject Root, Ground, Models, Water;
            public int ModelInstances, ModelsMissing, CulledSurfaces, CulledModelParts;
            public Vector3 LightDirection;
            /// <summary>Client wave 2: the animated model files and their instances on this map (RsmAnimator under Root).</summary>
            public RsmAnimator Animator; public int AnimatedInstances;
            /// <summary>Client wave 4 (w4world): the water as drawn (from the GND or the RSW) — the smoke and the log read these.</summary>
            public string WaterSource; public int WaterType, WaterAnimSpeed, WaterTiles; public float WaterLevel, WaveHeight, WaveSpeed, WavePitch, WaterOpacity;
            public WaterAnimator WaterAnim;
            /// <summary>Client wave 4: placed models whose textures move (RSM 2.3 texture keyframes).</summary>
            public int TextureAnimatedInstances;
            /// <summary>Architect's fix for this map (World/MapFix.cs; Resources/MapFixes/&lt;map&gt;.txt), or null: drawn as before. The
            /// ground mesh as the game builds it (<see cref="PlainGround"/>: always the collider) and with the fix (<see cref="FixedGround"/>,
            /// drawn while <see cref="FixShown"/>); the black hole fills, a child of the ground (null: none). FixNote: one line for the log.</summary>
            public MapFix Fix; public Mesh PlainGround, FixedGround; public GameObject FixFills; public string FixNote = "";
            public bool FixShown { get; internal set; }
            public int FixBoth, FixFlipped, FixTops, FixFillCount, FixSkirts;
            /// <summary>Wave 27 (wmodel), Architect's fix kind M: the back copies of single-sided model faces, baked into map space and
            /// merged per material and 256-cell block (objects "architect-back" under <see cref="Models"/>, drawn while <see cref="FixShown"/>;
            /// no collider, no shadow), the triangles they add, the instances fixed, and the instances whose M lines were NOT applied
            /// (another RSW / RSM than the file was made for, a model that moves, a face count that does not match the file).</summary>
            public readonly List<GameObject> FixBackFaces = new List<GameObject>();
            /// <summary>The fixed models' placed boxes (map cells): in the table and follow views their back copies are not drawn while the eye
            /// is inside (Shaders/LokiModel.shader).</summary>
            public readonly List<Bounds> FixBackBoxes = new List<Bounds>();
            public int FixModelFaces, FixModelInstances, FixModelSkipped;
        }

        public Shader GroundShader, ModelShader, WaterShader;
        private static Material NoDrawMaterial;
        public LoadedMap Current { get; private set; }

        private RoAssets _assets;

        public void Init(RoAssets assets)
        {
            _assets = assets;
            if (GroundShader == null) GroundShader = Shader.Find("Loki/Ground");
            if (ModelShader == null) ModelShader = Shader.Find("Loki/Model");
            if (WaterShader == null) WaterShader = Shader.Find("Loki/Water");
        }

        public void Unload()
        {
            if (Current?.Root != null) LokiUtil.Destroy(Current.Root);
            Current = null;
        }

        /// <summary>Client wave 7 (w7data): the new map is read and built completely before the current one is taken down. A map
        /// that is missing (FileNotFoundException), of a version Loki does not know or damaged (Formats.RoFormatException), or
        /// that fails while its meshes are built, throws with the current map still standing and its light put back: the caller
        /// keeps the old scene and tells the player (LokiGame.EnterMap).</summary>
        public LoadedMap Load(string mapName)
        {
            var sw = Stopwatch.StartNew();
            var fs = _assets.Fs;
            // read everything first: a map missing from the client data throws before the current map is taken down (client wave 2)
            RswFile rsw; GndFile gnd; GatFile gat; byte[] gndBytes, gatBytes, rswBytes; string fileName;
            try
            {
                rsw = RswFile.Parse(rswBytes = fs.ReadOrThrow("data\\" + mapName + ".rsw"));
                fileName = FileMapName(fs.RealPath("data\\" + mapName + ".rsw"), mapName);
                gnd = GndFile.Parse(gndBytes = fs.ReadOrThrow("data\\" + rsw.GndFile));
                gat = GatFile.Parse(gatBytes = fs.ReadOrThrow("data\\" + rsw.GatFile));
            }
            catch (RoFormatException e) { RoFormatLog.Report("map", mapName, e); throw; }
            long tParse = sw.ElapsedMilliseconds;
            var old = Current;
            LoadedMap built;
            try { built = Build(mapName, rsw, gnd, gat, sw, tParse, LoadFix(fileName, gnd, gndBytes, gatBytes, out string fixNote), fixNote, fileName, rswBytes); }
            catch (System.Exception e)
            {
                // half a map: take it away again and put the standing map's light back
                foreach (Transform c in transform) if (c != null && (old == null || c.gameObject != old.Root) && c.name == "map:" + mapName) LokiUtil.Destroy(c.gameObject);
                if (old != null && old.Rsw != null)
                {
                    Shader.SetGlobalVector("_LightDir", old.LightDirection);
                    Shader.SetGlobalColor("_LightDiffuse", old.Rsw.LightDiffuse);
                    Shader.SetGlobalColor("_LightAmbient", old.Rsw.LightAmbient);
                }
                Current = old;
                var wrapped = e is RoFormatException ? e : new RoFormatException("MAP", "the map's files were read but it could not be built (" + e.GetType().Name + ": " + e.Message.Trim() + ")", false, e);
                RoFormatLog.Report("map", mapName, wrapped);
                if (wrapped == e) throw;
                throw wrapped;
            }
            if (old?.Root != null && old.Root != built.Root) LokiUtil.Destroy(old.Root);
            Current = built;
            return built;
        }

        private LoadedMap Build(string mapName, RswFile rsw, GndFile gnd, GatFile gat, Stopwatch sw, long tParse, MapFix fix = null, string fixNote = "", string fileName = null, byte[] rswBytes = null)
        {
            var map = new LoadedMap { Name = mapName, FileName = fileName ?? mapName, Rsw = rsw, Gnd = gnd, Gat = gat, FixNote = fixNote ?? "" };
            if (!string.Equals(map.FileName, mapName, System.StringComparison.OrdinalIgnoreCase))
                Debug.Log($"[MapLoader] {mapName}: the files of {map.FileName} (the game's name table, data\\resnametable.txt)");
            map.Root = new GameObject("map:" + mapName);
            map.Root.transform.SetParent(transform, false);

            // --- global light ---
            map.LightDirection = RoSpace.LightDirection(rsw.LightLongitude, rsw.LightLatitude);
            Shader.SetGlobalVector("_LightDir", map.LightDirection);
            Shader.SetGlobalColor("_LightDiffuse", rsw.LightDiffuse);
            Shader.SetGlobalColor("_LightAmbient", rsw.LightAmbient);
            var lightGo = new GameObject("sun");
            lightGo.transform.SetParent(map.Root.transform, false);
            var sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = rsw.LightDiffuse;
            sun.intensity = 1f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.6f;
            lightGo.transform.rotation = Quaternion.LookRotation(-map.LightDirection, Vector3.up);

            // --- ground ---
            // Pure-black "void filler" textures (BLACK.BMP, BACKSIDE.BMP, sgid2-side2.bmp …) are culled: see RoAssets.IsBlackTexture.
            var blackTex = new bool[gnd.Textures.Count]; var blackNames = new List<string>();
            for (int i = 0; i < blackTex.Length; i++) { blackTex[i] = _assets.IsBlackTexture(gnd.Textures[i]); if (blackTex[i]) blackNames.Add(gnd.Textures[i]); }
            var g = GroundMeshBuilder.Build(gnd, true, ti => ti >= 0 && ti < blackTex.Length && blackTex[ti]);
            map.CulledSurfaces = g.Culled;
            // Architect's fix (World/MapFix.cs): the same ground built again with the fix, drawn instead of the game's; the collider stays
            // the game's own ground (the fix changes only what is drawn)
            GroundMeshBuilder.Result gf = null;
            if (fix != null) map.Fix = fix;
            // wave 27 (wmodel): a fix file with model faces only (kind M) has no ground lines: the game's ground is drawn as it is
            if (fix != null && fix.HasGround)
            {
                gf = GroundMeshBuilder.Build(gnd, true, ti => ti >= 0 && ti < blackTex.Length && blackTex[ti], fix, g.LightmapAtlas);
                map.FixBoth = gf.FixBoth; map.FixFlipped = gf.FixFlipped; map.FixTops = gf.FixTops; map.FixFillCount = gf.FixFills; map.FixSkirts = gf.FixSkirts;
                map.FixNote = $"Architect's fix applied: {gf.FixBoth} ground walls drawn from both sides, {gf.FixFlipped} turned to their other side, {gf.FixTops} floors drawn, {gf.FixFills} holes filled ({gf.FixSkirts} skirts)";
            }
            map.PlainGround = g.Mesh; map.FixedGround = gf?.Mesh;
            map.Ground = new GameObject("ground");
            map.Ground.transform.SetParent(map.Root.transform, false);
            var mf = map.Ground.AddComponent<MeshFilter>(); mf.sharedMesh = gf != null ? gf.Mesh : g.Mesh; map.FixShown = gf != null || fix != null; // wave 27 (wmodel): or model faces only
            var mr = map.Ground.AddComponent<MeshRenderer>();
            var mats = new Material[g.Textures.Count];
            for (int i = 0; i < mats.Length; i++)
            {
                var m = new Material(GroundShader) { name = "ground:" + g.Textures[i] };
                m.SetTexture("_MainTex", _assets.Texture(g.Textures[i]));
                m.SetTexture("_Lightmap", g.LightmapAtlas);
                mats[i] = m;
            }
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
            map.Ground.AddComponent<MeshCollider>().sharedMesh = g.Mesh;
            if (gf?.VoidMesh != null)
            {
                // the hole fills: black and unlit (a white texture times the black vertex colour, no lightmap), still fogged, room-masked
                // and cut away like the ground (the same shader)
                map.FixFills = new GameObject("ground-fix-fills");
                map.FixFills.transform.SetParent(map.Ground.transform, false);
                map.FixFills.AddComponent<MeshFilter>().sharedMesh = gf.VoidMesh;
                var fr = map.FixFills.AddComponent<MeshRenderer>();
                var fm = new Material(GroundShader) { name = "ground:architect-fill" };
                fm.SetTexture("_MainTex", Texture2D.whiteTexture); fm.SetFloat("_UseLightmap", 0f);
                fr.sharedMaterial = fm; fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; fr.receiveShadows = false;
            }
            var roomMask = FindObjectOfType<RoomMask>();
            bool isIndoorMap = (roomMask != null && (roomMask.IsIndoor(mapName) || roomMask.IsIndoor(map.FileName))) ||
                               map.FileName.EndsWith("_in", System.StringComparison.OrdinalIgnoreCase) ||
                               map.FileName.IndexOf("_in0", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                               string.Equals(map.FileName, "new_zone01", System.StringComparison.OrdinalIgnoreCase);
            if (isIndoorMap)
            {
                var ceilMesh = GroundMeshBuilder.BuildIndoorCeiling(gnd, ti => ti >= 0 && ti < blackTex.Length && blackTex[ti]);
                if (ceilMesh != null)
                {
                    var holder = map.Root.GetComponent<FixMeshes>(); if (holder == null) holder = map.Root.AddComponent<FixMeshes>();
                    holder.Meshes.Add(ceilMesh);
                    var ceilGo = new GameObject("ground-indoor-ceiling");
                    ceilGo.transform.SetParent(map.Ground.transform, false);
                    ceilGo.AddComponent<MeshFilter>().sharedMesh = ceilMesh;
                    var cr = ceilGo.AddComponent<MeshRenderer>();
                    cr.sharedMaterials = mats;
                    cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    cr.receiveShadows = false;
                }
            }
            long tGround = sw.ElapsedMilliseconds;

            // --- models ---
            map.Models = new GameObject("models");
            map.Models.transform.SetParent(map.Root.transform, false);
            var matCache = new Dictionary<string, Material>();
            // RO model positions are relative to the map centre (in RO units): +width*5, +height*5 → Loki: +cells/2
            var centerOffset = new Vector3(gnd.Width, 0, gnd.Height); // GND tiles * 2 cells / 2
            map.Animator = map.Root.AddComponent<RsmAnimator>();
            // wave 27 (wmodel), Architect's fix kind M: applied only to the RSW it was made for (the instances are its list, in order)
            var modelFix = fix != null && fix.Models.Count > 0 ? fix : null;
            if (modelFix != null && !modelFix.MatchesRsw(rswBytes))
            {
                map.FixModelSkipped = modelFix.Models.Count;
                map.FixNote += (map.FixNote.Length > 0 ? "; " : "") + $"model faces NOT applied: made for another RSW (rsw {modelFix.RswBytes} bytes {modelFix.RswHash}; this one {rswBytes?.LongLength ?? 0} bytes {MapFix.Hash(rswBytes)})";
                modelFix = null;
            }
            var backCache = new Dictionary<string, RsmMeshBuilder.Buffers>(); var rsmHash = new Dictionary<string, bool>();
            var backBatches = new Dictionary<(Material, int, int), BackBatch>();
            int instIndex = -1;
            foreach (var mo in rsw.Models)
            {
                instIndex++;
                if (Mathf.Abs(mo.Scale.x) < 1e-4f || Mathf.Abs(mo.Scale.y) < 1e-4f || Mathf.Abs(mo.Scale.z) < 1e-4f) { map.CulledModelParts++; continue; }
                var mesh = _assets.ModelMesh(mo.FileName, out var rsm);
                if (mesh == null) { map.ModelsMissing++; continue; }
                var animated = map.Animator.MeshFor(Loki.IO.RoPath.Normalize(mo.FileName), rsm); // windmills, flags, wheels (client wave 2)
                if (animated != null) { mesh = animated; if (RsmAnimator.Moving(rsm)) map.AnimatedInstances++; if (RsmMeshBuilder.HasTextureAnims(rsm)) map.TextureAnimatedInstances++; } // + RSM 2.3 texture keyframes (client wave 4)
                var go = new GameObject(mo.Name ?? mo.FileName);
                go.transform.SetParent(map.Models.transform, false);
                RsmMeshBuilder.ApplyInstanceTransform(go.transform, mo.Position, mo.Rotation, mo.Scale, centerOffset);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                var ms = new Material[Mathf.Max(rsm.Textures.Count, 1)];
                for (int i = 0; i < ms.Length; i++)
                {
                    string tn = i < rsm.Textures.Count ? rsm.Textures[i] : "";
                    string key = tn + (rsm.Alpha < 0.999f ? "|a" + rsm.Alpha.ToString("F2") : "");
                    if (_assets.IsBlackTexture(tn))
                    {
                        if (NoDrawMaterial == null) NoDrawMaterial = new Material(Shader.Find("Loki/NoDraw")) { name = "nodraw" };
                        ms[i] = NoDrawMaterial; map.CulledModelParts++;
                        continue;
                    }
                    if (!matCache.TryGetValue(key, out var mat))
                    {
                        mat = new Material(ModelShader) { name = "model:" + tn };
                        mat.SetTexture("_MainTex", _assets.Texture(tn));
                        mat.SetFloat("_Alpha", rsm.Alpha);
                        matCache[key] = mat;
                    }
                    ms[i] = mat;
                }
                r.sharedMaterials = ms;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                map.ModelInstances++;
                if (modelFix != null && modelFix.Models.TryGetValue(instIndex, out var mfix))
                    AddBackFaces(map, RsmMeshBuilder.InstanceMatrix(mo.Position, mo.Rotation, mo.Scale, centerOffset), mesh.bounds, ms, mo.FileName, rsm, mfix, animated != null, backCache, rsmHash, backBatches);
            }
            if (backBatches.Count > 0) FinishBackFaces(map, backBatches);
            if (modelFix != null)
                map.FixNote += (map.FixNote.Length > 0 ? "; " : "Architect's fix applied: ") + $"{map.FixModelFaces} model faces drawn from both sides on {map.FixModelInstances} models" +
                               (map.FixModelSkipped > 0 ? $" ({map.FixModelSkipped} models NOT fixed: their file or faces differ from the fix's, or they move)" : "");
            long tModels = sw.ElapsedMilliseconds;

            // --- water ---
            BuildWater(map);

            Current = map;
            Debug.Log($"[MapLoader] {mapName}: rsw {rsw.Version} gnd {gnd.Version} {gnd.Width}x{gnd.Height} tiles, {gnd.Textures.Count} textures, {gnd.LightmapCount} lightmaps, " +
                      $"{rsw.Models.Count} model objects ({map.ModelInstances} placed, {map.ModelsMissing} missing, {map.AnimatedInstances} animated from {map.Animator.Count} files, {map.TextureAnimatedInstances} with moving textures from {map.Animator.TextureCount} files), {rsw.Lights.Count} lights, {rsw.Effects.Count} effects. " +
                      $"parse {tParse} ms, ground {tGround - tParse} ms, models {tModels - tGround} ms, total {sw.ElapsedMilliseconds} ms");
            if (map.CulledSurfaces > 0 || map.CulledModelParts > 0)
                Debug.Log($"[MapLoader] {mapName}: culled {map.CulledSurfaces} black void ground surfaces (textures: {string.Join(", ", blackNames)}; {g.CulledTinted} black-tinted tops) and {map.CulledModelParts} black model parts");
            if (map.FixNote.Length > 0) Debug.Log($"[MapLoader] {mapName}: {map.FixNote}");
            return map;
        }

        /// <summary>Wave 27 (wmodel): the back copies of one material in one 256-cell block of the map, in map space.</summary>
        private sealed class BackBatch
        {
            public readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>(); public readonly List<Vector2> Uv = new List<Vector2>();
            public readonly List<Color32> C = new List<Color32>(); public readonly List<int> T = new List<int>();
            /// <summary>Per vertex its model's box (map cells): min with w = 2 (LokiModel.shader: a back copy), max.</summary>
            public readonly List<Vector4> Lo = new List<Vector4>(), Hi = new List<Vector4>();
        }
        /// <summary>The batches' block (cells): dev 1 used 64 (1@infi: 769 objects, its render 10.2 -> 11.8 ms); 256 makes 103 there (Architect,
        /// ARCHITECT_MODEL_BATCHES=1: lasagna 93, prontera 144), most maps one to four blocks.</summary>
        public const int BackBlockCells = 256;

        /// <summary>
        /// Wave 27 (wmodel), Architect's fix kind M on one model instance: the back copies of the faces the fix names (RsmMeshBuilder.FillBack:
        /// reversed winding, negated normals, as the builder draws a two-sided face's other side), baked into map space by the instance's own
        /// placement (RsmMeshBuilder.InstanceMatrix = ApplyInstanceTransform; a mirrored placement keeps the copy's drawn side by reversing
        /// the triangle, as Unity reverses culling for it; normals by the inverse transpose, as Unity carries them) and added to the batch of
        /// its material and 256-cell block: a map draws a handful of extra objects, not one per model. It changes drawing only: no collider
        /// (the ground keeps the only one), no shadow (Shaders/LokiModel.shader's shadow pass already draws both sides: Cull Off). Not applied
        /// to a model file other than the one the fix was made for (size + FNV-1a 64 of its bytes), to a model that moves or whose texture
        /// moves (its back copy would stand still), or when the faces built differ in number from the file's.
        /// </summary>
        private void AddBackFaces(LoadedMap map, Matrix4x4 place, Bounds meshBox, Material[] ms, string file, RsmFile rsm, MapFix.ModelFaces mfix, bool animated,
                                  Dictionary<string, RsmMeshBuilder.Buffers> cache, Dictionary<string, bool> fileOk, Dictionary<(Material, int, int), BackBatch> batches)
        {
            string key = RoPath.Normalize(file);
            if (animated) { map.FixModelSkipped++; return; }
            if (!fileOk.TryGetValue(key, out bool ok)) { ok = MapFix.MatchesModel(mfix, _assets.Fs.Read("data\\model\\" + key)); fileOk[key] = ok; }
            if (!ok) { map.FixModelSkipped++; return; }
            var sig = new System.Text.StringBuilder(key);
            foreach (var kv in mfix.Nodes) sig.Append('|').Append(kv.Key).Append(':').Append(MapFix.Runs(kv.Value));
            string ck = sig.ToString();
            if (!cache.TryGetValue(ck, out var b))
            {
                try { b = RsmMeshBuilder.FillBack(rsm, (nd, fc) => mfix.Has(nd, fc)); }
                catch (System.Exception e) { Debug.LogWarning($"[MapLoader] {map.Name}: the back faces of {key} could not be built ({e.Message})"); b = null; }
                cache[ck] = b;
            }
            if (b == null || b.FaceOf.Count != mfix.Count) { map.FixModelSkipped++; return; }
            var nrm = place.inverse.transpose; bool mirrored = place.determinant < 0f;
            // the model's placed box (map cells): the shader leaves its back copies out in the table and follow views while the eye is inside
            Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int c = 0; c < 8; c++)
            {
                var p = place.MultiplyPoint3x4(new Vector3((c & 1) == 0 ? meshBox.min.x : meshBox.max.x, (c & 2) == 0 ? meshBox.min.y : meshBox.max.y, (c & 4) == 0 ? meshBox.min.z : meshBox.max.z));
                lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
            }
            map.FixBackBoxes.Add(new Bounds((lo + hi) * 0.5f, hi - lo));
            var lo4 = new Vector4(lo.x, lo.y, lo.z, 2f); var hi4 = new Vector4(hi.x, hi.y, hi.z, 0f);
            int bx = Mathf.FloorToInt(place.m03 / BackBlockCells), bz = Mathf.FloorToInt(place.m23 / BackBlockCells);
            for (int sm = 0; sm < b.Tris.Length; sm++)
            {
                var t = b.Tris[sm]; if (t.Count == 0) continue;
                var mat = sm < ms.Length ? ms[sm] : null;
                if (mat == null || mat == NoDrawMaterial) continue; // a black texture: the model's own part is not drawn either
                if (!batches.TryGetValue((mat, bx, bz), out var bb)) batches[(mat, bx, bz)] = bb = new BackBatch();
                for (int k = 0; k + 2 < t.Count; k += 3)
                {
                    int j0 = bb.V.Count;
                    for (int c = 0; c < 3; c++)
                    {
                        int vi = t[k + c];
                        bb.V.Add(place.MultiplyPoint3x4(b.Verts[vi])); bb.N.Add(nrm.MultiplyVector(b.Norms[vi]).normalized);
                        bb.Uv.Add(b.Uvs[vi]); bb.C.Add(b.Cols[vi]); bb.Lo.Add(lo4); bb.Hi.Add(hi4);
                    }
                    if (!mirrored) { bb.T.Add(j0); bb.T.Add(j0 + 1); bb.T.Add(j0 + 2); } else { bb.T.Add(j0); bb.T.Add(j0 + 2); bb.T.Add(j0 + 1); }
                }
            }
            map.FixModelFaces += b.FaceOf.Count; map.FixModelInstances++;
        }

        /// <summary>Wave 27 (wmodel): one object per batch under the map's models, destroyed with the map (its meshes too: FixMeshes).</summary>
        private void FinishBackFaces(LoadedMap map, Dictionary<(Material, int, int), BackBatch> batches)
        {
            var holder = map.Root.GetComponent<FixMeshes>(); if (holder == null) holder = map.Root.AddComponent<FixMeshes>();
            foreach (var kv in batches)
            {
                var bb = kv.Value; if (bb.T.Count == 0) continue;
                var mesh = new Mesh { name = $"architect-back {kv.Key.Item1.name} {kv.Key.Item2},{kv.Key.Item3}", indexFormat = bb.V.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                mesh.SetVertices(bb.V); mesh.SetNormals(bb.N); mesh.SetUVs(0, bb.Uv); mesh.SetColors(bb.C); mesh.SetUVs(2, bb.Lo); mesh.SetUVs(3, bb.Hi); mesh.SetTriangles(bb.T, 0, true); mesh.RecalculateBounds();
                holder.Meshes.Add(mesh);
                var go = new GameObject("architect-back");
                go.transform.SetParent(map.Models.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var br = go.AddComponent<MeshRenderer>();
                br.sharedMaterial = kv.Key.Item1;
                br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                map.FixBackFaces.Add(go);
            }
        }

        // ------------------------------------------------------------------ Architect's fixes (World/MapFix.cs)
        /// <summary>warch2: the map name of a resolved rsw path ("data\\new_zone01.rsw" -> "new_zone01"), or the server's name when
        /// the path is not an rsw under data\\.</summary>
        public static string FileMapName(string rswPath, string mapName)
        {
            const string pre = "data\\", ext = ".rsw";
            if (rswPath == null || !rswPath.StartsWith(pre, System.StringComparison.Ordinal) || !rswPath.EndsWith(ext, System.StringComparison.Ordinal)) return mapName;
            return rswPath.Substring(pre.Length, rswPath.Length - pre.Length - ext.Length);
        }

        /// <summary>Where the fix files are: Resources/MapFixes/&lt;map&gt;.txt (Architect writes them: client/tools/vrmaps "fix"), the real
        /// map's name (<see cref="LoadedMap.FileName"/>).</summary>
        public const string FixFolder = "MapFixes/";

        /// <summary>The map's fix file, when there is one and it was made for these very files (else null, with the reason in the note:
        /// the map then draws exactly as before).</summary>
        private static MapFix LoadFix(string mapName, GndFile gnd, byte[] gndBytes, byte[] gatBytes, out string note)
        {
            note = "";
            var ta = Resources.Load<TextAsset>(FixFolder + mapName.ToLowerInvariant());
            if (ta == null) return null;
            try
            {
                var fix = MapFix.Parse(ta.text, gnd.Width);
                if (!fix.Matches(gndBytes, gatBytes))
                {
                    note = $"Architect's fix NOT applied: it was made for other files (gnd {fix.GndBytes} bytes {fix.GndHash}, gat {fix.GatBytes} bytes {fix.GatHash}; " +
                           $"these: gnd {gndBytes.LongLength} bytes {MapFix.Hash(gndBytes)}, gat {gatBytes.LongLength} bytes {MapFix.Hash(gatBytes)})";
                    return null;
                }
                return fix;
            }
            catch (System.Exception e) { note = "Architect's fix NOT applied: the file could not be read (" + e.Message + ")"; return null; }
        }

        /// <summary>Draws the current map with Architect's fix (true) or as the game builds it (false), for comparing the two; the
        /// collider never changes. No-op on a map without a fix.</summary>
        public void ShowFix(bool on, bool log = true)
        {
            var m = Current; if (m == null || m.Ground == null || (m.FixedGround == null && m.FixBackFaces.Count == 0)) return;
            var mf = m.Ground.GetComponent<MeshFilter>(); if (mf == null) return;
            if (m.FixedGround != null) mf.sharedMesh = on ? m.FixedGround : m.PlainGround;
            if (m.FixFills != null) m.FixFills.SetActive(on);
            foreach (var b in m.FixBackFaces) if (b != null) b.SetActive(on); // wave 27 (wmodel): the model faces' back copies
            m.FixShown = on;
            if (log) Debug.Log($"[MapLoader] {m.Name}: Architect's fix {(on ? "shown" : "hidden (the game's own ground)")}");
        }

        private void BuildWater(LoadedMap map)
        {
            var rsw = map.Rsw; var gnd = map.Gnd;
            // the GND's water block wins from GND 1.8 on (roBrowser Loaders/MapLoader.js:201-203), else the RSW's (World.js:120-144)
            float level, waveHeight, waveSpeed, wavePitch; int type, animSpeed;
            if (gnd.Water != null) { level = gnd.Water.Level; waveHeight = gnd.Water.WaveHeight; waveSpeed = gnd.Water.WaveSpeed; wavePitch = gnd.Water.WavePitch; type = gnd.Water.Type; animSpeed = gnd.Water.AnimSpeed; map.WaterSource = "gnd"; }
            else if (rsw.HasWater) { level = rsw.WaterLevel; waveHeight = rsw.WaveHeight; waveSpeed = rsw.WaveSpeed; wavePitch = rsw.WavePitch; type = rsw.WaterType; animSpeed = rsw.WaterAnimSpeed; map.WaterSource = "rsw"; }
            else return;
            map.WaterLevel = level; map.WaterType = type; map.WaveHeight = waveHeight; map.WaveSpeed = waveSpeed; map.WavePitch = wavePitch; map.WaterAnimSpeed = animSpeed;

            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            int w = gnd.Width, h = gnd.Height;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var t = gnd.Tiles[x + y * w];
                    if (t.Top < 0) continue;
                    // y-down: ground "below" the water surface has a larger y than the level
                    if (!(t.H0 > level - waveHeight || t.H1 > level - waveHeight || t.H2 > level - waveHeight || t.H3 > level - waveHeight)) continue;
                    int i0 = verts.Count;
                    float ly = -level * RoSpace.Scale;
                    verts.Add(new Vector3(x * 2f, ly, y * 2f)); verts.Add(new Vector3(x * 2f + 2, ly, y * 2f));
                    verts.Add(new Vector3(x * 2f, ly, y * 2f + 2)); verts.Add(new Vector3(x * 2f + 2, ly, y * 2f + 2));
                    uvs.Add(new Vector2((x % 5) / 5f, (y % 5) / 5f)); uvs.Add(new Vector2((x % 5 + 1) / 5f, (y % 5) / 5f));
                    uvs.Add(new Vector2((x % 5) / 5f, (y % 5 + 1) / 5f)); uvs.Add(new Vector2((x % 5 + 1) / 5f, (y % 5 + 1) / 5f));
                    tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 3); tris.Add(i0); tris.Add(i0 + 3); tris.Add(i0 + 1);
                }
            if (verts.Count == 0) return;
            map.WaterTiles = verts.Count / 4;
            var mesh = new Mesh { name = "water", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            // the vertex shader lifts / lowers the surface by up to the wave height: keep that inside the culling bounds
            var bounds = mesh.bounds; bounds.Expand(new Vector3(0f, 2f * Mathf.Abs(waveHeight) * RoSpace.Scale + 0.01f, 0f)); mesh.bounds = bounds;
            map.Water = new GameObject("water");
            map.Water.transform.SetParent(map.Root.transform, false);
            map.Water.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = map.Water.AddComponent<MeshRenderer>();
            var mat = new Material(WaterShader != null ? WaterShader : ModelShader) { name = "water" };
            // data\texture\워터\water{type}{00..31}.jpg (folder name is EUC-KR bytes BF F6 C5 CD)
            string folder = "¿öÅÍ";
            var frames = new Texture2D[32];
            for (int i = 0; i < 32; i++) frames[i] = _assets.Texture($"{folder}\\water{type}{i:00}.jpg", true);
            mat.SetTexture("_MainTex", frames[0]);
            // roBrowser Water.js:81: opacity 1.0 for the types 4 and 6 (lava), else 0.8 — which Water.fs:283-290 applies twice
            // (a = uOpacity; a *= uOpacity), so 0.64 on screen. korangar water_plane.rs agrees on 4 / 6 opaque, 144/255 for the rest.
            float opacity = type == 4 || type == 6 ? 1f : 0.8f * 0.8f;
            mat.SetFloat("_Opacity", opacity);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var anim = map.Water.AddComponent<WaterAnimator>();
            anim.Frames = frames; anim.Material = mat;
            anim.WaveHeightCells = waveHeight * RoSpace.Scale; anim.WaveSpeed = waveSpeed; anim.WavePitch = wavePitch; anim.AnimSpeed = animSpeed;
            map.WaterAnim = anim; map.WaterOpacity = opacity;
            Debug.Log($"[MapLoader] {map.Name}: water from the {map.WaterSource}: type {type}, level {level}, wave height {waveHeight}, speed {waveSpeed}, pitch {wavePitch}, " +
                      $"animation speed {animSpeed} ({(animSpeed > 0 ? (60f / animSpeed).ToString("0.##") + " textures a second" : "one still texture")}), opacity {opacity:0.##}, {map.WaterTiles} tiles");
        }
    }

    /// <summary>Wave 27 (wmodel): the meshes a map's fix builds at run time (the back copies of model faces), destroyed with the map.</summary>
    public sealed class FixMeshes : MonoBehaviour
    {
        public readonly List<Mesh> Meshes = new List<Mesh>();
        private void OnDestroy() { foreach (var m in Meshes) if (m != null) Destroy(m); Meshes.Clear(); }
    }

    /// <summary>
    /// Client wave 4 (w4world): the water's texture cycle and wave from the map's own values (the RSW, or the GND from 1.8 on), drawn
    /// as roBrowser Renderer/Map/Water.js:118-161 does (korangar world/map/mod.rs:360-369 computes the same three values):
    ///   frame   = ms / (1000 / 60)                     a 60 Hz frame counter (Water.js:126)
    ///   texture = floor(frame / animSpeed) % 32        water{type}00..31.jpg (Water.js:161; animSpeed 3 = 20 textures a second; 0 keeps
    ///                                                  the first one, as roBrowser's NaN index does)
    ///   offset  = ((frame * waveSpeed) % 360) - 180    degrees, into LokiWater.shader's wave (Water.js:158; pitch degrees per GND tile)
    /// Before wave 4 the texture cycled at a fixed 8 a second and the surface never moved.
    /// </summary>
    public sealed class WaterAnimator : MonoBehaviour
    {
        public Texture2D[] Frames; public Material Material;
        public float WaveHeightCells, WaveSpeed, WavePitch; public int AnimSpeed = 3;
        /// <summary>The texture index and wave offset set this frame, and the time they were computed for (smoke checks).</summary>
        public int TextureIndex { get; private set; } public float WaveOffset { get; private set; } public float LastMs { get; private set; }

        public static int TextureAt(float ms, int animSpeed, int count) => animSpeed > 0 && count > 0 ? (int)(ms / (1000f / 60f) / animSpeed) % count : 0;
        public static float OffsetAt(float ms, float waveSpeed) => ((ms / (1000f / 60f) * waveSpeed) % 360f) - 180f;

        private void Update()
        {
            if (Frames == null || Frames.Length == 0 || Material == null) return;
            float ms = Time.time * 1000f; LastMs = ms;
            TextureIndex = TextureAt(ms, AnimSpeed, Frames.Length);
            WaveOffset = OffsetAt(ms, WaveSpeed);
            Material.SetTexture("_MainTex", Frames[TextureIndex]);
            Material.SetFloat("_WaveOffset", WaveOffset);
            Material.SetFloat("_WaveHeight", WaveHeightCells);
            Material.SetFloat("_WavePitch", WavePitch);
        }
    }
}
