using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace Loki.Net
{
    /// <summary>
    /// Automatic Friends Package updater: reads the local <c>StreamingAssets/version.json</c> stamp, reports <c>pkg:&lt;version&gt;</c>
    /// in the SpacetimeDB <c>hello</c> handshake on standalone builds, continuously polls GitHub's <c>version.json</c> manifest
    /// even while playing in-game, and when either SpacetimeDB (<c>#update:&lt;version&gt;:&lt;bytes&gt;:&lt;sha256&gt;:&lt;commit&gt;:&lt;url&gt;</c>)
    /// or GitHub reports a newer version, immediately disconnects from the server and launches the graphical <c>LokiLauncher.exe</c>
    /// updater to download, verify, extract (preserving <c>loki.json</c>), and relaunch <c>Loki.exe</c>.
    /// </summary>
    public sealed class ClientUpdater : MonoBehaviour
    {
        public const string DefaultManifestUrl = "";
        public const float PollIntervalSeconds = 25f;

        [Serializable]
        private sealed class VersionStamp
        {
            public uint version;
            public string commit = "";
            public string sha256 = "";
            public long sizeBytes;
            public string downloadUrl = "";
            public string manifestUrl = "";
            public string githubRepo = "";
            public bool autoUpdate = false;
        }

        private static uint? _localVersion;
        private static string _manifestUrl;
        private static bool _autoUpdateEnabled;
        private static ClientUpdater _instance;
        private bool _running, _monitoring;
        private bool _forcePollNow;
        private Action<string> _notifyCallback;

        /// <summary>True while an update handoff or download is in progress.</summary>
        public static bool IsUpdating => _instance != null && _instance._running;

        /// <summary>True when the player is currently in the world so we pause ~1.8s to show the in-game disconnect card before opening the Launcher.</summary>
        public static bool InGameContext { get; set; }

        /// <summary>Fired immediately when a new version is detected (before closing Loki), allowing LokiGame and LoginPanel to disconnect and show a notice.</summary>
        public static event Action<uint, string> UpdateStarting;

        public static uint LocalVersion
        {
            get
            {
                if (_localVersion.HasValue) return _localVersion.Value;
                LoadLocalStamp();
                return _localVersion.Value;
            }
        }

        private static void LoadLocalStamp()
        {
            _localVersion = 0;
            _manifestUrl = DefaultManifestUrl;
            _autoUpdateEnabled = false;
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, "version.json");
                if (File.Exists(path))
                {
                    var stamp = JsonUtility.FromJson<VersionStamp>(File.ReadAllText(path, Encoding.UTF8));
                    if (stamp != null)
                    {
                        _localVersion = stamp.version;
                        _autoUpdateEnabled = stamp.autoUpdate;
                        if (!string.IsNullOrEmpty(stamp.manifestUrl))
                            _manifestUrl = stamp.manifestUrl.Trim();
                        else if (!string.IsNullOrEmpty(stamp.githubRepo))
                            _manifestUrl = $"https://github.com/{stamp.githubRepo.Trim().Trim('/')}/releases/download/latest/version.json";
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[Updater] version.json read: " + e.Message); }
        }

        public static string BuildHelloLabel(bool isAutomatedRun)
        {
            string baseLabel = Application.version + " " + Application.platform;
            if (Application.isEditor || isAutomatedRun) return baseLabel;
            return $"pkg:{LocalVersion} {baseLabel}";
        }

        public static void CheckOnStartup(Action<string> notify)
        {
            if (Application.isEditor || Stdb.StdbSession.IsAutomatedRun()) return;
            EnsureInstance();
            if (notify != null) _instance._notifyCallback = notify;
            if (_instance._running) return;
            if (!_instance._monitoring)
            {
                _instance._monitoring = true;
                _instance.StartCoroutine(_instance.MonitorManifestLoop());
            }
            else
            {
                _instance._forcePollNow = true;
            }
        }

        public static void RequestImmediateCheck()
        {
            if (Application.isEditor || Stdb.StdbSession.IsAutomatedRun()) return;
            EnsureInstance();
            if (!_instance._monitoring)
            {
                _instance._monitoring = true;
                _instance.StartCoroutine(_instance.MonitorManifestLoop());
            }
            else
            {
                _instance._forcePollNow = true;
            }
        }

        private static void EnsureInstance()
        {
            if (_instance != null) return;
            var go = new GameObject("ClientUpdater");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ClientUpdater>();
        }

        private IEnumerator MonitorManifestLoop()
        {
            while (!_running)
            {
                _forcePollNow = false;
                yield return CheckManifestOnce();
                if (_running) yield break;

                float elapsed = 0f;
                while (elapsed < PollIntervalSeconds && !_forcePollNow && !_running)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
        }

        private IEnumerator CheckManifestOnce()
        {
            uint local = LocalVersion;
            if (!_autoUpdateEnabled) yield break;
            string baseUrl = !string.IsNullOrEmpty(_manifestUrl) ? _manifestUrl : DefaultManifestUrl;
            if (string.IsNullOrEmpty(baseUrl) || baseUrl.Contains("YOUR_GITHUB_USER")) yield break;
            long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string sep = baseUrl.Contains("?") ? "&" : "?";
            string url = baseUrl + sep + "t=" + ts;

            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 8;
                req.SetRequestHeader("Cache-Control", "no-cache, no-store");
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success || _running) yield break;
                VersionStamp remote = null;
                try { remote = JsonUtility.FromJson<VersionStamp>(req.downloadHandler.text); }
                catch { }
                if (remote != null && remote.version > local && !string.IsNullOrEmpty(remote.downloadUrl) && !string.IsNullOrEmpty(remote.sha256))
                {
                    Debug.Log($"[Updater] GitHub manifest reports new version v{remote.version} (local v{local}).");
                    yield return RunUpdate(
                        remote.version,
                        remote.sizeBytes,
                        remote.sha256.Trim().ToLowerInvariant(),
                        remote.commit ?? "",
                        remote.downloadUrl.Trim(),
                        _notifyCallback);
                }
            }
        }

        public static bool TryParseUpdateNotice(
            string raw,
            out uint version,
            out long sizeBytes,
            out string sha256,
            out string commit,
            out string downloadUrl)
        {
            version = 0; sizeBytes = 0; sha256 = ""; commit = ""; downloadUrl = "";
            if (string.IsNullOrEmpty(raw) || !raw.StartsWith("#update:", StringComparison.Ordinal)) return false;
            var parts = raw.Split(new[] { ':' }, 6);
            if (parts.Length < 6) return false;
            if (!uint.TryParse(parts[1], out version) || version == 0) return false;
            long.TryParse(parts[2], out sizeBytes);
            sha256 = parts[3].Trim().ToLowerInvariant();
            commit = parts[4].Trim();
            downloadUrl = parts[5].Trim();
            return !string.IsNullOrEmpty(downloadUrl) && !string.IsNullOrEmpty(sha256);
        }

        public static bool TryStartFromNotice(string raw, Action<string> notify)
        {
            if (!TryParseUpdateNotice(raw, out uint version, out long sizeBytes, out string sha256, out string commit, out string url))
                return false;
            if (Application.isEditor)
            {
                Debug.Log($"[Updater] Server has package v{version} ({commit}), skipped in Unity Editor.");
                return false;
            }
            EnsureInstance();
            if (notify != null) _instance._notifyCallback = notify;
            if (_instance._running) return true;
            _instance.StartCoroutine(_instance.RunUpdate(version, sizeBytes, sha256, commit, url, _instance._notifyCallback));
            return true;
        }

        private static string FindLauncherExecutable(string installDir, string parentDir)
        {
            string inInstall = Path.Combine(installDir, "LokiLauncher.exe");
            if (File.Exists(inInstall)) return inInstall;
            if (!string.IsNullOrEmpty(parentDir))
            {
                string inParent = Path.Combine(parentDir, "LokiLauncher.exe");
                if (File.Exists(inParent)) return inParent;
            }
            return null;
        }

        private IEnumerator RunUpdate(uint version, long sizeBytes, string expectedSha256, string commit, string url, Action<string> notify)
        {
            if (_running) yield break;
            _running = true;

            string shortCommit = string.IsNullOrEmpty(commit) ? "" : $" ({commit})";
            string startMsg = $"A new version of Loki (v{version}{shortCommit}) is available! Disconnecting and opening Loki Launcher to update...";
            Debug.Log($"[Updater] Triggering update to v{version}{shortCommit} from {url}");

            try { UpdateStarting?.Invoke(version, startMsg); }
            catch (Exception e) { Debug.LogWarning("[Updater] UpdateStarting listener: " + e.Message); }
            notify?.Invoke(startMsg);

            string installDir = Directory.GetParent(Application.dataPath).FullName;
            string parentDir = Directory.GetParent(installDir)?.FullName ?? installDir;
            string tempRoot = Path.Combine(Path.GetTempPath(), $"loki-update-v{version}");

            try
            {
                if (!Directory.Exists(tempRoot)) Directory.CreateDirectory(tempRoot);
            }
            catch (Exception e) { Debug.LogWarning("[Updater] temp dir init: " + e.Message); }

            string launcherSrc = FindLauncherExecutable(installDir, parentDir);
            if (!string.IsNullOrEmpty(launcherSrc))
            {
                // Give the player a brief moment to read the disconnection / update message before LokiLauncher.exe opens
                float waitSec = InGameContext ? 1.8f : 0.45f;
                float until = Time.realtimeSinceStartup + waitSec;
                while (Time.realtimeSinceStartup < until)
                    yield return null;

                try
                {
                    string tempLauncher = Path.Combine(tempRoot, "LokiLauncher.exe");
                    File.Copy(launcherSrc, tempLauncher, true);
                    int pid = Process.GetCurrentProcess().Id;
                    string args = $"--update --pid {pid} --install-dir \"{installDir}\" --version {version} --size {sizeBytes} --sha256 \"{expectedSha256}\" --commit \"{commit}\" --url \"{url}\"";
                    var psi = new ProcessStartInfo
                    {
                        FileName = tempLauncher,
                        Arguments = args,
                        WorkingDirectory = tempRoot,
                        UseShellExecute = false,
                    };
                    Process.Start(psi);
                    Application.Quit();
                    yield break;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Updater] Could not start LokiLauncher.exe, falling back to in-app download: " + e.Message);
                }
            }

            // Fallback path for older installs that do not yet have LokiLauncher.exe on disk (e.g. upgrading from v4 -> v5)
            string zipPath = Path.Combine(tempRoot, "Loki-VR.zip");
            string stageDir = Path.Combine(tempRoot, "stage");

            using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
            {
                req.downloadHandler = new DownloadHandlerFile(zipPath) { removeFileOnAbort = true };
                req.redirectLimit = 10;
                var op = req.SendWebRequest();
                float nextReport = 0f;
                while (!op.isDone)
                {
                    if (Time.unscaledTime >= nextReport)
                    {
                        nextReport = Time.unscaledTime + 0.1f;
                        ulong dl = req.downloadedBytes;
                        if (sizeBytes > 0)
                        {
                            int pct = Mathf.Clamp(Mathf.RoundToInt((float)((double)dl * 100.0 / sizeBytes)), 0, 100);
                            notify?.Invoke($"Updating Loki to v{version}{shortCommit}: {dl / (1024f * 1024f):0.0} / {sizeBytes / (1024f * 1024f):0.0} MB ({pct}%)...");
                        }
                        else
                        {
                            notify?.Invoke($"Updating Loki to v{version}{shortCommit}: {dl / (1024f * 1024f):0.0} MB downloaded...");
                        }
                    }
                    yield return null;
                }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[Updater] Download failed: {req.error}");
                    notify?.Invoke($"Automatic update download failed ({req.error}). Restart Loki to retry.");
                    _running = false;
                    yield break;
                }
            }

            notify?.Invoke($"Verifying update v{version}{shortCommit}...");
            yield return null;

            string actualSha = "";
            string extractErr = null;
            string existingLokiJson = Path.Combine(Application.streamingAssetsPath, "loki.json");
            string savedLokiJsonText = File.Exists(existingLokiJson) ? File.ReadAllText(existingLokiJson, Encoding.UTF8) : null;

            var prepTask = Task.Run(() =>
            {
                try
                {
                    using (var sha = SHA256.Create())
                    using (var fs = File.OpenRead(zipPath))
                    {
                        actualSha = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                    }
                    if (!string.Equals(actualSha, expectedSha256, StringComparison.OrdinalIgnoreCase))
                        return;

                    if (Directory.Exists(stageDir)) Directory.Delete(stageDir, true);
                    ZipFile.ExtractToDirectory(zipPath, stageDir);

                    if (!string.IsNullOrEmpty(savedLokiJsonText))
                    {
                        string stagedLokiJson = Path.Combine(stageDir, "Loki", "Loki_Data", "StreamingAssets", "loki.json");
                        if (File.Exists(stagedLokiJson))
                            File.WriteAllText(stagedLokiJson, savedLokiJsonText, new UTF8Encoding(true));
                    }
                }
                catch (Exception e) { extractErr = e.Message; }
            });

            while (!prepTask.IsCompleted)
            {
                notify?.Invoke($"Extracting & verifying update v{version}{shortCommit}...");
                yield return null;
            }

            if (extractErr != null)
            {
                Debug.LogWarning($"[Updater] Extract failed: {extractErr}");
                notify?.Invoke($"Update extraction failed ({extractErr}).");
                _running = false;
                yield break;
            }

            if (!string.Equals(actualSha, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[Updater] SHA-256 mismatch: expected {expectedSha256}, got {actualSha}");
                notify?.Invoke("Update verification failed (SHA-256 mismatch). Restart Loki to retry.");
                _running = false;
                yield break;
            }

            // If the newly extracted stage contains LokiLauncher.exe, use it to finish installation graphically!
            string stagedLauncher = Path.Combine(stageDir, "Loki", "LokiLauncher.exe");
            if (File.Exists(stagedLauncher))
            {
                try
                {
                    string tempLauncher = Path.Combine(tempRoot, "LokiLauncher.exe");
                    File.Copy(stagedLauncher, tempLauncher, true);
                    int pid = Process.GetCurrentProcess().Id;
                    string args = $"--update --pid {pid} --install-dir \"{installDir}\" --version {version} --size {sizeBytes} --sha256 \"{expectedSha256}\" --commit \"{commit}\" --url \"{url}\" --zip \"{zipPath}\"";
                    var psi = new ProcessStartInfo
                    {
                        FileName = tempLauncher,
                        Arguments = args,
                        WorkingDirectory = tempRoot,
                        UseShellExecute = false,
                    };
                    Process.Start(psi);
                    Application.Quit();
                    yield break;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Updater] Staged launcher handoff failed: " + e.Message);
                }
            }

            notify?.Invoke($"Restarting Loki on v{version}{shortCommit}...");
            yield return null;
            Application.Quit();
        }
    }
}
