using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace Loki.EditorTools
{
    /// <summary>
    /// One-shot project configuration, runnable headless:
    ///   -executeMethod Loki.EditorTools.LokiSetup.ConfigureAll   (XR + scene + build settings)
    ///   -executeMethod Loki.EditorTools.LokiSetup.BuildWindows   (player → Build/Loki.exe)
    /// </summary>
    public static class LokiSetup
    {
        private const string ScenePath = "Assets/Loki/Scenes/Loki.unity";

        public static void ConfigureAll()
        {
            int code = 0;
            try
            {
                ConfigureXR();
                CreateScene();
                PlayerSettings.productName = "Loki";
                PlayerSettings.companyName = "Project Loki";
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
                PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Standalone, ApiCompatibilityLevel.NET_Standard);
                PlayerSettings.runInBackground = true;
                PlayerSettings.colorSpace = ColorSpace.Linear;
                QualitySettings.shadows = ShadowQuality.All;
                QualitySettings.shadowDistance = 60f;
                QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
                QualitySettings.vSyncCount = 0;
                AssetDatabase.SaveAssets();
                Debug.Log("[LokiSetup] ConfigureAll done");
            }
            catch (Exception e) { Debug.LogError("[LokiSetup] FAILED: " + e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        public static void ConfigureXR()
        {
            var group = BuildTargetGroup.Standalone;
            // General XR settings asset (XR Plug-in Management)
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget settingsPerTarget) || settingsPerTarget == null)
            {
                settingsPerTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                Directory.CreateDirectory("Assets/XR");
                AssetDatabase.CreateAsset(settingsPerTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settingsPerTarget, true);
            }
            if (!settingsPerTarget.HasManagerSettingsForBuildTarget(group)) settingsPerTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            var settings = settingsPerTarget.SettingsForBuildTarget(group);
            settings.InitManagerOnStart = true;
            var manager = settings.AssignedSettings;
            if (manager == null) throw new Exception("No XRManagerSettings for Standalone");
            if (!XRPackageMetadataStore.AssignLoader(manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group))
                Debug.LogWarning("[LokiSetup] AssignLoader(OpenXRLoader) returned false (maybe already assigned)");
            EditorUtility.SetDirty(settings); EditorUtility.SetDirty(manager);

            // OpenXR: every controller / hand interaction profile of the vendored plugin, and the standard PC render mode.
            var openXr = EnsureInteractionProfiles(group);
            openXr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            openXr.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.Depth24Bit;
            EditorUtility.SetDirty(openXr);
            Debug.Log("[LokiSetup] OpenXR configured");
        }

        /// <summary>
        /// Client wave 4 (w4vr): enables every controller and hand interaction profile the vendored OpenXR plugin has (1.12.1 since
        /// client wave 4b, w4xr) — Valve Index, Oculus Touch, Meta Quest Touch Pro, Meta Quest Touch Plus (w4xr), HP Reverb G2, HTC Vive,
        /// Windows Mixed Reality, Khronos simple, the
        /// hand interaction profile (XR_EXT_hand_interaction) and Microsoft hand interaction — and leaves the plugin's additive
        /// features (Palm Pose, Hand Interaction Poses, D-Pad) and Eye Gaze off (why: Runtime/VR/ControllerProfiles.cs). Runs before
        /// every build (BuildWindows), because the OpenXR settings asset lives in each Unity copy, not in the repo. The list is
        /// Loki.VR.ControllerProfiles.FeatureNames (the runtime's table, so both sides agree).
        /// </summary>
        public static OpenXRSettings EnsureInteractionProfiles(BuildTargetGroup group)
        {
            var openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (openXr == null) throw new Exception("No OpenXRSettings for " + group);
            UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(group);
            var wanted = new System.Collections.Generic.HashSet<string>(Loki.VR.ControllerProfiles.FeatureNames);
            var on = new System.Collections.Generic.List<string>(); var off = new System.Collections.Generic.List<string>();
            foreach (var f in openXr.GetFeatures<OpenXRInteractionFeature>())
            {
                string name = f.GetType().Name;
                bool want = wanted.Contains(name);
                if (f.enabled != want) { f.enabled = want; EditorUtility.SetDirty(f); }
                (want ? on : off).Add(name);
            }
            var missing = wanted.Where(w => !on.Contains(w)).ToList();
            // client wave 4b (w4xr): XR Hands' OpenXR features (com.unity.xr.hands 1.5.1, Loki.VR.HandJoints.FeatureNames): "Hand Tracking
            // Subsystem" (XR_EXT_hand_tracking: the joints, Runtime/OpenXR/HandTracking.cs:37-48) and "Meta Hand Tracking Aim"
            // (XR_FB_hand_tracking_aim: Meta's pointing ray, pinch strength and menu gesture, Runtime/OpenXR/MetaHandTrackingAim.cs:40-50).
            // Plain OpenXRFeatures, not interaction profiles; a runtime without the extension just does not enable it (the plugin asks
            // for the extension strings of enabled features and logs the ones it cannot enable: Documentation~/features.md:37, :80).
            var handWanted = new System.Collections.Generic.HashSet<string>(Loki.VR.HandJoints.FeatureNames);
            var handOn = new System.Collections.Generic.List<string>();
            foreach (var f in openXr.GetFeatures<OpenXRFeature>())
            {
                if (f == null || f is OpenXRInteractionFeature || !handWanted.Contains(f.GetType().Name)) continue;
                if (!f.enabled) { f.enabled = true; EditorUtility.SetDirty(f); }
                handOn.Add(f.GetType().Name);
            }
            var handMissing = handWanted.Where(w => !handOn.Contains(w)).ToList();
            EditorUtility.SetDirty(openXr);
            AssetDatabase.SaveAssets();
            // The plugin's controller layouts declare thumbsticks / trackpads as Vector2Control unless USE_STICK_CONTROL_THUMBSTICKS is
            // defined (ValveIndexControllerProfile.cs:18-22, and the Touch, Touch Pro, Reverb G2, Vive and WMR files), but their
            // FinishSetup always asks for a StickControl (ValveIndexControllerProfile.cs:231, OculusTouchControllerProfile.cs:196 …):
            // without the define, creating the device throws "Expected control 'thumbstick' to be of type 'StickControl' but is of
            // type 'Vector2Control'" and the controller never exists (found by the w4vr smoke on 2026-09-30; KHR simple and hands,
            // which have no stick, were unaffected). The define is the plugin's own switch (OpenXRProjectValidation.cs:182-209,
            // "[Optional] Switch to use StickControl thumbsticks"); Loki reads sticks as Vector2 values, which StickControl still is.
            // Client wave 4b (w4xr): still needed with OpenXR 1.12.1 — the same `#if USE_STICK_CONTROL_THUMBSTICKS` in every stick
            // profile (MetaQuestTouchPlusControllerProfile.cs:17-21 against :227, ValveIndexControllerProfile.cs:18-22 against :231) and
            // the rule is still "[Optional]", error = false (OpenXRProjectValidation.cs:185-199; its fix-it adds the define, :221-224).
            const string stickDefine = "USE_STICK_CONTROL_THUMBSTICKS";
            var nbt = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group);
            string defines = PlayerSettings.GetScriptingDefineSymbols(nbt) ?? "";
            if (!defines.Split(';').Select(d => d.Trim()).Contains(stickDefine))
            {
                PlayerSettings.SetScriptingDefineSymbols(nbt, defines.Length > 0 ? defines + ";" + stickDefine : stickDefine);
                Debug.Log($"[LokiSetup] scripting define {stickDefine} added for {group} (was '{defines}')");
            }
            else Debug.Log($"[LokiSetup] scripting define {stickDefine} present for {group}");
            Debug.Log($"[LokiSetup] OpenXR interaction profiles enabled ({on.Count}): {string.Join(", ", on)}; left off ({off.Count}): {string.Join(", ", off)}" +
                      (missing.Count > 0 ? $"; NOT FOUND in the plugin: {string.Join(", ", missing)}" : ""));
            Debug.Log($"[LokiSetup] OpenXR hand-tracking features enabled ({handOn.Count}): {string.Join(", ", handOn)}" +
                      (handMissing.Count > 0 ? $"; NOT FOUND (com.unity.xr.hands missing?): {string.Join(", ", handMissing)}" : ""));
            return openXr;
        }

        public static void CreateScene()
        {
            Directory.CreateDirectory("Assets/Loki/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Loki");
            go.AddComponent<LokiBootstrap>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[LokiSetup] scene saved " + ScenePath);
        }

        /// <summary>Builds the player into ..\Build, or into the folder given by <c>-lokiBuildOut &lt;folder&gt;</c> on the command line
        /// (so a test build never replaces the one in C:\LokiVR\client\Build).</summary>
        public static void BuildWindows()
        {
            int code = 0;
            try
            {
                if (!File.Exists(ScenePath)) CreateScene();
                string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Build"));
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-lokiBuildOut" && !string.IsNullOrWhiteSpace(args[i + 1])) outDir = Path.GetFullPath(args[i + 1]);
                Debug.Log("[LokiSetup] output folder " + outDir);
                Directory.CreateDirectory(outDir);
                var opts = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = Path.Combine(outDir, "Loki.exe"),
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None
                };
                // client wave 3b (w3data): -lokiRelease leaves every GRF-derived file out (the client makes them on the player's PC)
                bool release = args.Contains("-lokiRelease");
                EnsureInteractionProfiles(BuildTargetGroup.Standalone); // client wave 4 (w4vr): every controller / hand profile, each build
                // client wave 4b (w4release): the smoke / test code (#if LOKI_SMOKE) is compiled into development builds only. The define
                // is given to this build alone (BuildPlayerOptions.extraScriptingDefines), never stored in the project's settings.
                opts.extraScriptingDefines = release ? new string[0] : new[] { LokiRelease.SmokeDefine };
                LokiRelease.KeepSmokeOutOfProject();
                Debug.Log(release ? $"[LokiSetup] release build: scripting define {LokiRelease.SmokeDefine} off (no smoke / test code; loki.json's test fields ignored)"
                                  : $"[LokiSetup] development build: scripting define {LokiRelease.SmokeDefine} on (smoke / test code compiled in)");
                if (release) LokiRelease.MoveOut(); else LokiRelease.Restore();
                UnityEditor.Build.Reporting.BuildReport report;
                try { report = UnityEditor.BuildPipeline.BuildPlayer(opts); }
                finally { LokiRelease.Restore(); }
                Debug.Log($"[LokiSetup] build {report.summary.result}: {report.summary.totalErrors} errors, {report.summary.totalSize} bytes → {opts.locationPathName}");
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) code = 1;
                else if (!args.Contains("-lokiKeepCredentials")) ShipConfig(Path.Combine(outDir, "Loki_Data", "StreamingAssets", "loki.json"));
                if (release && code == 0 && !LokiRelease.Check(outDir)) code = 1;
                if (code == 0 && !LokiRelease.CheckSmokeCode(outDir, release)) code = 1; // client wave 4b (w4release): none in a release build
            }
            catch (Exception e) { Debug.LogError("[LokiSetup] BUILD FAILED: " + e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        /// <summary>The build's copy of loki.json as it ships to players (the project's own file is never touched; a release build
        /// ignores these fields anyway, client wave 4b w4release): no auto-login
        /// account, character, startup commands or smoke switch (re-audit 2026-09-29 §4: every friend's client would otherwise log
        /// into the same account). Servers, the data folder and the rest stay. -lokiKeepCredentials skips it for a private build.</summary>
        public static void ShipConfig(string path)
        {
            if (!File.Exists(path)) { Debug.LogWarning("[LokiSetup] no " + path + " in the build: nothing to clean"); return; }
            var cfg = JsonUtility.FromJson<Loki.World.LokiConfig>(File.ReadAllText(path)) ?? new Loki.World.LokiConfig();
            bool had = !string.IsNullOrEmpty(cfg.username) || !string.IsNullOrEmpty(cfg.password) || cfg.debugAutoWalk || (cfg.startupCommands != null && cfg.startupCommands.Length > 0);
            cfg.StripForShipping();
            File.WriteAllText(path, JsonUtility.ToJson(cfg, true));
            Debug.Log($"[LokiSetup] shipped loki.json {path}: account, password, character, startupCommands and debugAutoWalk cleared (had some: {had}); {cfg.servers?.Length ?? 0} server(s), clientRoot '{cfg.clientRoot}'");
        }
    }
}
