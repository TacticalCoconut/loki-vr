using System;
using System.IO;
using Loki.World;
using UnityEditor;
using UnityEngine;

namespace Loki.EditorTools
{
    /// <summary>
    /// Headless checks, run with:
    ///   Unity.exe -batchmode -projectPath &lt;proj&gt; -executeMethod Loki.EditorTools.LokiBatch.RenderMap -lokiMap prontera -lokiOut D:\out.png -quit
    /// Renders the map from two viewpoints to PNG and writes stats to the log.
    /// </summary>
    public static class LokiBatch
    {
        private static string Arg(string name, string def = null)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        public static void RenderMap()
        {
            int code = 0;
            try
            {
                string mapName = Arg("-lokiMap", "prontera");
                string outPath = Arg("-lokiOut", Path.Combine(Path.GetTempPath(), mapName + ".png"));
                var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);

                var go = new GameObject("loki");
                var boot = go.AddComponent<LokiBootstrap>();
                boot.LoadStartMapOnAwake = false;
                boot.DioramaScale = 1f; // render in Loki units
                boot.Config = LokiConfig.Load();
                boot.Open();
                var map = boot.LoadMap(mapName);

                float w = map.Gnd.Width * 2f, h = map.Gnd.Height * 2f;
                var center = new Vector3(w * 0.5f, 0, h * 0.5f) + boot.Map.transform.localPosition;
                // The bootstrap centred the diorama at the origin; the map centre is therefore at (0, groundY, 0).
                float groundY = SampleGroundHeight(map, map.Gat.Width / 2, map.Gat.Height / 2);
                center = new Vector3(0, groundY, 0);

                // Optional sprite test: -lokiSprites 1 puts a Poring, a Lunatic and a novice (male + female) around the map centre.
                if (Arg("-lokiSprites", "0") == "1")
                {
                    Loki.Sprites.ActorSprite.CameraDirection = 0;
                    int cx = map.Gat.Width / 2, cy = map.Gat.Height / 2;
                    var spec = new (int job, int sex, int hair, int dx, int dy, int dir)[] { (1002, 1, 0, -3, 0, 0), (1063, 1, 0, 3, 0, 6), (0, 1, 5, 0, -3, 0), (0, 0, 2, 0, 3, 4), (1002, 1, 0, -3, 4, 2), (1002, 1, 0, 3, 4, 4) };
                    uint id = 1;
                    foreach (var sp in spec)
                    {
                        var u = Loki.Sprites.Unit.Create(boot.Map.transform, map.Gat, id++, sp.job, sp.sex, sp.hair, boot.Assets);
                        u.SetCell(cx + sp.dx, cy + sp.dy, sp.dir);
                    }
                    // Sprites face the camera in LateUpdate; in batch mode force one pass per shot camera below.
                }
                if (Arg("-lokiSprites", "0") == "2")
                {
                    // Direction test: 8 porings (dir 0..7) and 8 novices, camera due south looking north.
                    Loki.Sprites.ActorSprite.CameraDirection = 0;
                    Loki.Sprites.SpriteFrameCache.ReliefDepth = float.Parse(Arg("-lokiRelief", "0.2"), System.Globalization.CultureInfo.InvariantCulture);
                    Loki.Sprites.SpriteFrameCache.GridStep = int.Parse(Arg("-lokiGrid", "1"));
                    int cx = map.Gat.Width / 2, cy = map.Gat.Height / 2;
                    uint id = 1;
                    for (int d = 0; d < 8; d++)
                    {
                        var m = Loki.Sprites.Unit.Create(boot.Map.transform, map.Gat, id++, 1002, 1, 0, boot.Assets); m.SetCell(cx - 8 + d * 2, cy + 2, d);
                        var n = Loki.Sprites.Unit.Create(boot.Map.transform, map.Gat, id++, 0, 1, 5, boot.Assets); n.SetCell(cx - 8 + d * 2, cy - 2, d);
                    }
                    float groundY2 = SampleGroundHeight(map, cx, cy);
                    Shot(outPath.Replace(".png", "_dirs.png"), new Vector3(0, groundY2 + 6, -14), new Vector3(0, groundY2 + 1, 0), 1800, 900, 40f);
                    Debug.Log("[LokiBatch] direction test rendered");
                    if (Application.isBatchMode) EditorApplication.Exit(0);
                    return;
                }
                Shot(outPath.Replace(".png", "_iso.png"), center + new Vector3(0, 0.9f, -1f) * (Mathf.Max(w, h) * 0.55f), center, 1600, 1200, 45f);
                Shot(outPath.Replace(".png", "_top.png"), center + new Vector3(0, Mathf.Max(w, h) * 0.9f, 0.001f), center, 1200, 1200, 60f);
                Shot(outPath.Replace(".png", "_close.png"), center + new Vector3(12, 10, -14), center + new Vector3(0, 1, 0), 1600, 1200, 45f);
                Debug.Log($"[LokiBatch] rendered {mapName} → {outPath}");
            }
            catch (Exception e)
            {
                Debug.LogError("[LokiBatch] FAILED: " + e);
                code = 1;
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }


        /// <summary>-lokiPrefix "data\luafiles514\" -lokiOut list.txt : list GRF entries under a prefix (Latin-1 bytes preserved).</summary>
        public static void ListFiles()
        {
            int code = 0;
            try
            {
                string prefix = Loki.IO.RoPath.Normalize(Arg("-lokiPrefix", "data\\"));
                string outPath = Arg("-lokiOut", Path.Combine(Path.GetTempPath(), "loki_list.txt"));
                var cfg = LokiConfig.Load();
                using (var fs = new Loki.IO.RoFileSystem(cfg.clientRoot, cfg.grfs))
                {
                    var lines = new System.Collections.Generic.List<string>();
                    foreach (var a in fs.Archives) foreach (var n in a.Names) if (n.StartsWith(prefix)) lines.Add(n);
                    lines.Sort(string.CompareOrdinal);
                    File.WriteAllLines(outPath, lines, Loki.IO.RoReader.Latin1);
                    Debug.Log($"[LokiBatch] listed {lines.Count} entries under {prefix} → {outPath}");
                }
            }
            catch (Exception e) { Debug.LogError("[LokiBatch] FAILED: " + e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        /// <summary>-lokiList paths.txt (one RO path per line, Latin-1) -lokiOutDir dir : extract entries to disk (path separators kept as folders).</summary>
        public static void ExtractFiles()
        {
            int code = 0;
            try
            {
                string list = Arg("-lokiList"); string outDir = Arg("-lokiOutDir", Path.Combine(Path.GetTempPath(), "loki_extract"));
                var cfg = LokiConfig.Load();
                int ok = 0, missing = 0;
                using (var fs = new Loki.IO.RoFileSystem(cfg.clientRoot, cfg.grfs))
                {
                    foreach (var raw in File.ReadAllLines(list, Loki.IO.RoReader.Latin1))
                    {
                        string ro = raw.Trim(); if (ro.Length == 0) continue;
                        var bytes = fs.Read(ro);
                        if (bytes == null) { missing++; Debug.LogWarning("[LokiBatch] missing " + ro); continue; }
                        // Latin-1 chars ≥ 0x80 are EUC-KR halves; keep them as-is in the file name (Windows will show mojibake, fine).
                        string rel = Loki.IO.RoPath.Normalize(ro).Replace('\\', Path.DirectorySeparatorChar);
                        string dst = Path.Combine(outDir, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(dst));
                        File.WriteAllBytes(dst, bytes); ok++;
                    }
                }
                Debug.Log($"[LokiBatch] extracted {ok}, missing {missing} → {outDir}");
            }
            catch (Exception e) { Debug.LogError("[LokiBatch] FAILED: " + e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        /// <summary>-lokiMaps a,b,c : why are some ground surfaces black? Per map: every GND texture's average colour and
        /// use count, and a census of surfaces whose rendered colour (texture × surface colour × lightmap shadow + lightmap
        /// colour) is near black, split by cause and by distance to the nearest walkable GAT cell.</summary>
        public static void AnalyzeGround()
        {
            int code = 0;
            try
            {
                var cfg = LokiConfig.Load();
                using (var fs = new Loki.IO.RoFileSystem(cfg.clientRoot, cfg.grfs))
                {
                    foreach (var mapName in Arg("-lokiMaps", "iz_int01").Split(','))
                    {
                        var gnd = Loki.Formats.GndFile.Parse(fs.Read("data\\" + mapName + ".gnd"));
                        var gat = Loki.Formats.GatFile.Parse(fs.Read("data\\" + mapName + ".gat"));
                        int nt = gnd.Textures.Count;
                        var texAvg = new Color[nt]; var texUseTop = new int[nt]; var texUseWall = new int[nt];
                        for (int i = 0; i < nt; i++)
                        {
                            string key = Loki.IO.RoPath.Normalize(gnd.Textures[i]);
                            var bytes = fs.Read("data\\texture\\" + key);
                            if (bytes != null && Loki.IO.ImageDecoders.TryDecode(key, bytes, out var img))
                            {
                                double r = 0, g = 0, b = 0; int n = 0;
                                foreach (var p in img.Pixels) { if (p.a == 0) continue; r += p.r; g += p.g; b += p.b; n++; }
                                texAvg[i] = n > 0 ? new Color((float)(r / n / 255), (float)(g / n / 255), (float)(b / n / 255), n / (float)img.Pixels.Length) : Color.clear;
                            }
                            else texAvg[i] = new Color(-1, -1, -1, 0);
                        }
                        int per = gnd.LightmapBytesPerCell;
                        float Shadow(int li) { if (li < 0 || li >= gnd.LightmapCount) return 1; int s0 = 0; for (int k = 0; k < per; k++) s0 += gnd.LightmapData[li * per * 4 + k]; return s0 / (per * 255f); }
                        float LmRgb(int li) { if (li < 0 || li >= gnd.LightmapCount) return 0; int s0 = 0; for (int k = 0; k < per * 3; k++) s0 += gnd.LightmapData[li * per * 4 + per + k]; return s0 / (per * 3 * 255f); }
                        // distance (in GND tiles) from each tile to the nearest walkable GAT cell (2x2 GAT cells per tile)
                        int W = gnd.Width, H = gnd.Height;
                        var dist = new int[W * H]; var q = new System.Collections.Generic.Queue<int>();
                        for (int i = 0; i < dist.Length; i++) dist[i] = int.MaxValue;
                        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                        {
                            bool walk = false;
                            for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++) { int gx = x * 2 + dx, gy = y * 2 + dy; if (gat.InBounds(gx, gy) && gat.Get(gx, gy).Walkable) walk = true; }
                            if (walk) { dist[x + y * W] = 0; q.Enqueue(x + y * W); }
                        }
                        while (q.Count > 0) { int c = q.Dequeue(); int cx = c % W, cy = c / W; for (int k = 0; k < 4; k++) { int nx = cx + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = cy + (k == 2 ? 1 : k == 3 ? -1 : 0); if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue; int ni = nx + ny * W; if (dist[ni] != int.MaxValue) continue; dist[ni] = dist[c] + 1; q.Enqueue(ni); } }
                        int total = 0, dark = 0, darkTex = 0, darkLm = 0, darkCol = 0, darkTop = 0, darkWall = 0, darkNear = 0, darkFar = 0, noWalkMap = 0;
                        var darkByTex = new int[nt];
                        void Visit(int surf, int tile, bool top)
                        {
                            if (surf < 0) return;
                            var s = gnd.Surfaces[surf]; if (s.TextureIndex < 0) return;
                            total++;
                            var t = texAvg[s.TextureIndex];
                            if (top) texUseTop[s.TextureIndex]++; else texUseWall[s.TextureIndex]++;
                            float texL = t.r < 0 ? 0.5f : (t.r + t.g + t.b) / 3f;
                            float col = top ? (s.R + s.G + s.B) / (3f * 255f) : 1f; // walls are drawn with white vertex colour
                            float sh = Shadow(s.LightmapIndex), lm = LmRgb(s.LightmapIndex);
                            float outL = texL * col * sh + lm;
                            if (outL >= 0.06f) return;
                            dark++; darkByTex[s.TextureIndex]++;
                            if (texL < 0.06f) darkTex++; else if (sh < 0.06f && lm < 0.06f) darkLm++; else darkCol++;
                            if (top) darkTop++; else darkWall++;
                            if (dist[tile] == int.MaxValue) noWalkMap++; else if (dist[tile] <= 1) darkNear++; else darkFar++;
                        }
                        for (int i = 0; i < gnd.Tiles.Length; i++) { var tl = gnd.Tiles[i]; Visit(tl.Top, i, true); Visit(tl.Front, i, false); Visit(tl.Right, i, false); }
                        Debug.Log($"[Analyze] {mapName}: gnd {W}x{H}, {nt} textures, {gnd.LightmapCount} lightmaps, {total} drawn surfaces; near-black {dark} (texture dark {darkTex}, lightmap black {darkLm}, other {darkCol}; top {darkTop}, wall {darkWall}; tile within 1 of walkable {darkNear}, farther {darkFar}, no walkable on map {noWalkMap})");
                        for (int i = 0; i < nt; i++)
                            Debug.Log($"[Analyze] {mapName} tex {i} '{gnd.Textures[i]}' avg {texAvg[i].r:F2},{texAvg[i].g:F2},{texAvg[i].b:F2} opaque {texAvg[i].a:F2}; top {texUseTop[i]}, wall {texUseWall[i]}, near-black {darkByTex[i]}");
                    }
                }
            }
            catch (Exception e) { Debug.LogError("[LokiBatch] FAILED: " + e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        private static float SampleGroundHeight(MapLoader.LoadedMap map, int cx, int cy)
        {
            if (!map.Gat.InBounds(cx, cy)) return 0;
            return -map.Gat.Get(cx, cy).AverageHeight * RoSpace.Scale;
        }

        public static void RenderSkillTrees()
        {
            int code = 0;
            try
            {
                string outDir = Arg("-lokiOutDir", Path.Combine(Path.GetTempPath(), "loki_trees"));
                Directory.CreateDirectory(outDir);
                UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);

                var go = new GameObject("loki");
                var boot = go.AddComponent<LokiBootstrap>();
                boot.LoadStartMapOnAwake = false;
                boot.DioramaScale = 0.02f;
                boot.Config = LokiConfig.Load();
                boot.Open();
                try
                {
                    var map = boot.LoadMap("valkyrie");
                    boot.FocusCell(new Vector2(49, 36), SampleGroundHeight(map, 49, 36));
                }
                catch (Exception e) { Debug.LogWarning("[LokiBatch] valkyrie backdrop skipped: " + e.Message); }

                var camGo = new GameObject("HeadCam");
                var cam = camGo.AddComponent<Camera>();
                cam.transform.position = new Vector3(0f, 1.55f, 0f);
                cam.transform.rotation = Quaternion.identity;
                cam.fieldOfView = 50f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 500f;
                cam.cullingMask = ~0;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.05f, 0.06f, 0.10f, 1f);
                cam.tag = "MainCamera";

                var prog = new Loki.Game.Progression();
                var ps = new Loki.Game.PlayerState();
                int curJob = 1;
                var tree3D = Loki.UI.SkillTree3D.Create(go.transform, cam, prog, ps);
                tree3D.Job = () => curJob;
                tree3D.JobLevel = () => 70;
                tree3D.JobLevelMax = () => true;
                tree3D.SkillPoints = () => 3;
                tree3D.Sex = () => 1;
                tree3D.Model = () =>
                {
                    prog.Skills.Clear();
                    foreach (int j in Loki.Game.SkillTreeDb.Chain(curJob))
                    {
                        var vt = Loki.Game.SkillTreeDb.ViewTreeOf(j);
                        if (vt == null) continue;
                        foreach (int sk in vt.Values)
                        {
                            var info = Loki.Game.SkillDb.Get(sk);
                            int maxLv = Mathf.Max(1, info != null ? info.MaxLevel : 10);
                            prog.Skills[sk] = new Loki.Game.Progression.SkillEntry { Id = sk, Level = maxLv, Upgradable = false };
                        }
                    }
                    return Loki.Game.SkillTreeModel.Build(curJob, prog, 1);
                };

                void CaptureCam(string file, Vector3 from, Vector3 lookAt, float fov, int width = 1600, int height = 1200)
                {
                    cam.transform.position = from;
                    cam.transform.LookAt(lookAt, Vector3.up);
                    cam.fieldOfView = fov;
                    foreach (var cv in tree3D.GetComponentsInChildren<Canvas>(true)) cv.worldCamera = cam;
                    Canvas.ForceUpdateCanvases();
                    var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    string path = Path.Combine(outDir, file + ".png");
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex);
                    cam.targetTexture = null;
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                    Debug.Log("[LokiBatch] tree shot " + path);
                }

                LokiTreeExamples.Enable();

                int JobId(string konst, int fallback)
                {
                    foreach (var j in Loki.Game.SkillTreeDb.AllJobs())
                        if (j.Const == konst) return j.Id;
                    return fallback;
                }

                void CaptureSingleTree(string file, int parentJob, int targetJob, bool hideMedals = false, float? riseU = null)
                {
                    curJob = parentJob;
                    cam.transform.position = new Vector3(0f, 1.55f, 0f);
                    cam.transform.rotation = Quaternion.identity;
                    if (tree3D.Shown) tree3D.Close();
                    tree3D.Open();
                    tree3D.FinishRise();
                    Loki.UI.SkillTree3D.JobTree tr = null;
                    foreach (var x in tree3D.Trees) if (x.Job == targetJob) { tr = x; break; }
                    if (tr == null && tree3D.Trees.Count > 0) tr = tree3D.Trees[tree3D.Trees.Count - 1];
                    if (tr == null) return;
                    // Hide other trees and side plate for clean close-up portrait of this sculpture
                    foreach (var x in tree3D.Trees) if (x != tr && x.Holder != null) x.Holder.gameObject.SetActive(false);
                    var plate = tree3D.Root.Find("plate");
                    if (plate != null) plate.gameObject.SetActive(false);
                    var toggled = new System.Collections.Generic.List<GameObject>();
                    if (tr.Holder != null)
                    {
                        for (int i = 0; i < tr.Holder.childCount; i++)
                        {
                            var ch = tr.Holder.GetChild(i).gameObject;
                            bool isBud = ch.name.StartsWith("bud:");
                            bool isMedal = ch.name == "medallions" || ch.name == "icons" || ch.name == "lines" || ch.name.StartsWith("node:");
                            if (ch.activeSelf && (isBud || (hideMedals && isMedal)))
                            {
                                ch.SetActive(false);
                                toggled.Add(ch);
                            }
                        }
                    }
                    if (riseU.HasValue) tree3D.PreviewRise(tr, riseU.Value);
                    float s = Mathf.Abs(tree3D.transform.lossyScale.y);
                    float h = (tr.Shape.Top - tr.Shape.Bottom + 185f) * s;
                    float mid = (tr.Shape.Top + tr.Shape.Bottom) * 0.5f * s;
                    var c = tr.Holder.position + Vector3.up * mid;
                    float back = Mathf.Max(0.72f, h * 0.5f / Mathf.Tan(23f * Mathf.Deg2Rad) * 1.06f);
                    CaptureCam(file, c - tr.Holder.forward * back, c, 46f, 1400, 1300);
                    foreach (var goCh in toggled) goCh.SetActive(true);
                    if (plate != null) plate.gameObject.SetActive(true);
                }

                void CaptureArc(string file, int job)
                {
                    curJob = job;
                    cam.transform.position = new Vector3(0f, 1.55f, 0f);
                    cam.transform.rotation = Quaternion.identity;
                    if (tree3D.Shown) tree3D.Close();
                    tree3D.Open();
                    tree3D.FinishRise();
                    var plate = tree3D.Root.Find("plate");
                    if (plate != null) plate.gameObject.SetActive(false);
                    var root = tree3D.Root;
                    var from = root.position - root.forward * 1.38f + Vector3.up * 0.24f;
                    var look = root.position + root.forward * 0.42f + Vector3.up * 0.02f;
                    CaptureCam(file, from, look, 62f, 1920, 1080);
                    if (plate != null) plate.gameObject.SetActive(true);
                }

                int jSwordman  = JobId("JT_SWORDMAN", 1);
                int jMage      = JobId("JT_MAGICIAN", 2);
                int jArcher    = JobId("JT_ARCHER", 3);
                int jAcolyte   = JobId("JT_ACOLYTE", 4);
                int jMerchant  = JobId("JT_MERCHANT", 5);
                int jThief     = JobId("JT_THIEF", 6);
                int jAssassin  = JobId("JT_ASSASSIN", 12);
                int jAssaCross = JobId("JT_ASSASSIN_H", 4013);
                int jGuillotine = JobId("JT_GUILLOTINE_CROSS", 4065);
                int jShadowCross = JobId("JT_SHADOW_CROSS", 4254);
                int jWindhawk  = JobId("JT_WINDHAWK", 4257);
                int jDiva      = JobId("JT_TROUVERE", 4264);

                var examples = new (string slug, int parentJob, int targetJob)[]
                {
                    ("01_assassin_cross_katar", jAssaCross, jAssaCross),
                    ("02_assassin_specialty_jur", jAssassin, jAssassin),
                    ("03_guillotine_cross", jGuillotine, jGuillotine),
                    ("04_shadow_cross", jShadowCross, jShadowCross),
                    ("05_windhawk", jWindhawk, jWindhawk),
                    ("06_diva_roses_harp", jDiva, jDiva),
                    ("07_job1_swordman", jSwordman, jSwordman),
                    ("08_job1_mage", jMage, jMage),
                    ("09_job1_archer", jArcher, jArcher),
                    ("10_job1_acolyte", jAcolyte, jAcolyte),
                    ("11_job1_merchant", jMerchant, jMerchant),
                    ("12_job1_thief", jThief, jThief),
                };

                foreach (var ex in examples)
                {
                    CaptureSingleTree("live_tree_" + ex.slug, ex.parentJob, ex.targetJob, hideMedals: false);
                    CaptureSingleTree("live_sculpt_" + ex.slug, ex.parentJob, ex.targetJob, hideMedals: true);
                }

                // Multi-tree progression arcs ("Around You" layout)
                CaptureArc("live_arc_assassin_cross", jAssaCross);
                CaptureArc("live_arc_shadow_cross_5trees", jShadowCross);

                Debug.Log("[LokiBatch] RenderSkillTrees complete -> " + outDir);
            }
            catch (Exception e)
            {
                Debug.LogError("[LokiBatch] RenderSkillTrees FAILED: " + e);
                code = 1;
            }
            finally
            {
                LokiTreeExamples.Disable();
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        private static void Shot(string path, Vector3 from, Vector3 at, int width, int height, float fov)
        {
            var camGo = new GameObject("shotcam");
            var cam = camGo.AddComponent<Camera>();
            cam.transform.position = from;
            cam.transform.LookAt(at);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.5f; cam.farClipPlane = 5000f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.1f, 0.12f, 0.16f);
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.tag = "MainCamera";
            foreach (var a in UnityEngine.Object.FindObjectsOfType<Loki.Sprites.ActorSprite>()) a.SendMessage("LateUpdate");
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(camGo);
            Debug.Log("[LokiBatch] wrote " + path);
        }
    }
}
