using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Loki.Launcher
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LauncherForm(args ?? new string[0]));
        }
    }

    internal sealed class LauncherOptions
    {
        public bool IsUpdateMode;
        public bool IsRollbackMode;
        public int WaitPid;
        public string InstallDir = "";
        public uint TargetVersion;
        public long SizeBytes;
        public string ExpectedSha256 = "";
        public string Commit = "";
        public string DownloadUrl = "";
        public string ExistingZipPath = "";
        public string GitHubRepo = "";
        public bool ForceAutoUpdate;
        public bool IsHeadless;

        public static LauncherOptions Parse(string[] args)
        {
            var o = new LauncherOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i] ?? "";
                if (string.Equals(a, "--update", StringComparison.OrdinalIgnoreCase))
                {
                    o.IsUpdateMode = true;
                }
                else if (string.Equals(a, "--rollback", StringComparison.OrdinalIgnoreCase))
                {
                    o.IsRollbackMode = true;
                }
                else if (string.Equals(a, "--headless", StringComparison.OrdinalIgnoreCase))
                {
                    o.IsHeadless = true;
                }
                else if (string.Equals(a, "--pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    int.TryParse(args[++i], out o.WaitPid);
                }
                else if (string.Equals(a, "--install-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    o.InstallDir = args[++i];
                }
                else if (string.Equals(a, "--version", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    uint.TryParse(args[++i], out o.TargetVersion);
                }
                else if (string.Equals(a, "--size", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    long.TryParse(args[++i], out o.SizeBytes);
                }
                else if (string.Equals(a, "--sha256", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    o.ExpectedSha256 = (args[++i] ?? "").Trim().ToLowerInvariant();
                }
                else if (string.Equals(a, "--commit", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    o.Commit = (args[++i] ?? "").Trim();
                }
                else if (string.Equals(a, "--url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    o.DownloadUrl = (args[++i] ?? "").Trim();
                }
                else if (string.Equals(a, "--zip", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    o.ExistingZipPath = (args[++i] ?? "").Trim();
                }
                else if (string.Equals(a, "--repo", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    o.GitHubRepo = (args[++i] ?? "").Trim();
                }
                else if (string.Equals(a, "--auto-update", StringComparison.OrdinalIgnoreCase))
                {
                    o.ForceAutoUpdate = true;
                }
            }
            return o;
        }
    }

    internal sealed class LauncherForm : Form
    {
        public const string DefaultManifestUrl = "";

        private readonly LauncherOptions _opts;
        private readonly float _dpiScale;
        private readonly System.Windows.Forms.Timer _animTimer;

        private string _stageTitle = "Initializing Loki Launcher...";
        private string _detailText = "Checking local installation...";
        private string _badgeText = "LOKI ONLINE";
        private float _targetProgress;
        private float _displayProgress;
        private bool _indeterminate = true;
        private float _shimmerPhase;
        private bool _hasError;
        private bool _canLaunchFallback;
        private bool _working;

        // Ragnarok Online Folder Prompt state
        private bool _awaitingRoFolder;
        private string _roDataFolder = "";
        private string _detectedRoCandidate = "";
        private string _roValidationError = "";

        // Rollback state
        private bool _rollbackAvailable;
        private uint _rollbackVersion;
        private string _rollbackCommit = "";
        private bool _justRolledBack;

        private Rectangle _closeBtnRect;
        private Rectangle _minBtnRect;
        private Rectangle _retryBtnRect;
        private Rectangle _launchAnywayBtnRect;
        private Rectangle _errorRollbackBtnRect;
        private Rectangle _roBrowseBtnRect;
        private Rectangle _roUseDetectedBtnRect;
        private Rectangle _roRollbackBtnRect;
        private Rectangle _footerChangeRoBtnRect;
        private Rectangle _footerAutoUpdateBtnRect;
        private Rectangle _footerRollbackBtnRect;

        private bool _hoverClose, _hoverMin, _hoverRetry, _hoverLaunchAnyway, _hoverErrorRollback;
        private bool _hoverRoBrowse, _hoverRoUseDetected, _hoverRoRollback;
        private bool _hoverFooterChangeRo, _hoverFooterAutoUpdate, _hoverFooterRollback;
        private bool _dragging;
        private Point _dragStart;

        private string _resolvedInstallDir = "";
        private uint _localVersion;
        private string _manifestUrl = DefaultManifestUrl;
        private string _githubRepo = "";
        private bool _autoUpdateEnabled = false;

        public LauncherForm(string[] args)
        {
            _opts = LauncherOptions.Parse(args);
            using (Graphics g = CreateGraphics())
            {
                _dpiScale = Math.Max(1f, g.DpiX / 96f);
            }

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Loki Online Launcher";
            ClientSize = new Size(S(660), S(400));
            BackColor = Color.FromArgb(14, 16, 23);
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            try { Icon = CreateLokiIcon(); } catch { }

            _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _animTimer.Tick += OnAnimTick;
            _animTimer.Start();

            Shown += delegate { StartWorkflow(); };
        }

        private int S(float v)
        {
            return (int)Math.Round(v * _dpiScale);
        }

        private static Icon CreateLokiIcon()
        {
            using (var bmp = new Bitmap(64, 64))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var bg = new SolidBrush(Color.FromArgb(20, 23, 34)))
                using (var border = new Pen(Color.FromArgb(218, 175, 72), 3f))
                {
                    g.FillEllipse(bg, 4, 4, 56, 56);
                    g.DrawEllipse(border, 4, 4, 56, 56);
                }
                PointF[] diamond =
                {
                    new PointF(32, 11),
                    new PointF(49, 32),
                    new PointF(32, 53),
                    new PointF(15, 32),
                };
                using (var brush = new LinearGradientBrush(new Rectangle(14, 10, 36, 44), Color.FromArgb(250, 218, 120), Color.FromArgb(180, 128, 36), LinearGradientMode.Vertical))
                {
                    g.FillPolygon(brush, diamond);
                }
                using (var inner = new SolidBrush(Color.FromArgb(24, 28, 42)))
                {
                    PointF[] core =
                    {
                        new PointF(32, 21),
                        new PointF(40, 32),
                        new PointF(32, 43),
                        new PointF(24, 32),
                    };
                    g.FillPolygon(inner, core);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            _shimmerPhase = (_shimmerPhase + 0.025f) % 1f;
            float diff = _targetProgress - _displayProgress;
            if (Math.Abs(diff) > 0.001f)
                _displayProgress += diff * 0.22f;
            else
                _displayProgress = _targetProgress;
            Invalidate();
        }

        private void SetState(string title, string detail, float progress, bool indeterminate, string badge)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(delegate { SetState(title, detail, progress, indeterminate, badge); }));
                return;
            }
            if (_awaitingRoFolder) return;
            _stageTitle = title;
            _detailText = detail;
            _targetProgress = Math.Max(0f, Math.Min(1f, progress));
            _indeterminate = indeterminate;
            if (!string.IsNullOrEmpty(badge)) _badgeText = badge;
            Invalidate();
        }

        private void ShowError(string title, string detail, bool canLaunchFallback)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(delegate { ShowError(title, detail, canLaunchFallback); }));
                return;
            }
            _working = false;
            _hasError = true;
            _awaitingRoFolder = false;
            _canLaunchFallback = canLaunchFallback;
            _indeterminate = false;
            _stageTitle = title;
            _detailText = detail;
            if (_opts.IsHeadless)
            {
                Environment.ExitCode = 1;
                Close();
                return;
            }
            Invalidate();
        }

        private void ShowRoFolderPrompt(string validationErr)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(delegate { ShowRoFolderPrompt(validationErr); }));
                return;
            }
            _working = false;
            _hasError = false;
            _awaitingRoFolder = true;
            _indeterminate = false;
            _targetProgress = 1.0f;
            _displayProgress = 1.0f;
            _roValidationError = validationErr ?? "";
            _detectedRoCandidate = FindRoCandidateOnDisk();
            RefreshRollbackState();

            if (!string.IsNullOrEmpty(_roValidationError))
            {
                _stageTitle = "Select Your Ragnarok Online Folder";
                _detailText = _roValidationError;
            }
            else if (!string.IsNullOrEmpty(_roDataFolder))
            {
                if (_justRolledBack)
                {
                    _stageTitle = "Update Rolled Back Successfully";
                    _detailText = string.Format(
                        "Restored Loki Online (v{0}). Auto-Update has been turned OFF so this version is kept.\r\nPress 'Play Loki Online' to launch the game.",
                        _localVersion > 0 ? _localVersion.ToString() : "previous");
                }
                else
                {
                    _stageTitle = "Loki Online is Ready to Play";
                    string rbNote = _rollbackAvailable
                        ? string.Format(" (Rollback to v{0} available)", _rollbackVersion > 0 ? _rollbackVersion.ToString() : "previous")
                        : "";
                    _detailText = string.Format(
                        "Loki Online (v{0}){1} is ready.\r\nPress 'Play Loki Online' to launch the game, or 'Change RO Folder...' to change your Ragnarok Online path.",
                        _localVersion > 0 ? _localVersion.ToString() : "latest",
                        rbNote);
                }
            }
            else if (!string.IsNullOrEmpty(_detectedRoCandidate))
            {
                _stageTitle = "Select Your Ragnarok Online Folder";
                _detailText = string.Format("Detected Ragnarok Online at: {0}\r\nConfirm this installation or browse for another folder containing data.grf.", _detectedRoCandidate);
            }
            else
            {
                _stageTitle = "Select Your Ragnarok Online Folder";
                _detailText = "Loki requires a Ragnarok Online installation (containing data.grf) for maps, sprites, and music. Please select your Ragnarok Online folder.";
            }
            Invalidate();
        }

        private void StartWorkflow()
        {
            if (_working) return;
            _working = true;
            _hasError = false;
            _awaitingRoFolder = false;
            Task.Run(new Action(RunWorkflowAsync));
        }

        private static string GetUserSettingsFilePath()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string localLow = Path.GetFullPath(Path.Combine(localApp, "..", "LocalLow"));
            return Path.Combine(localLow, "Project Loki", "Loki", "loki-user.json");
        }

        private string GetLokiJsonFilePath()
        {
            return Path.Combine(_resolvedInstallDir, "Loki_Data", "StreamingAssets", "loki.json");
        }

        private static bool HasRoDataFiles(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            try
            {
                if (File.Exists(Path.Combine(dir, "data.grf"))) return true;
                if (File.Exists(Path.Combine(dir, "DATA.INI"))) return true;
                string dataSub = Path.Combine(dir, "data");
                if (Directory.Exists(dataSub))
                {
                    if (File.Exists(Path.Combine(dataSub, "clientinfo.xml")) ||
                        File.Exists(Path.Combine(dataSub, "sclientinfo.xml")) ||
                        File.Exists(Path.Combine(dataSub, "idnum2itemdisplaynametable.txt")))
                        return true;
                }
                string[] grfs = Directory.GetFiles(dir, "*.grf", SearchOption.TopDirectoryOnly);
                if (grfs != null && grfs.Length > 0) return true;
            }
            catch { }
            return false;
        }

        private static string ResolveValidRoDataRoot(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return "";
            string trimmed = folder.Trim().Trim('"');
            if (string.IsNullOrEmpty(trimmed) || !Directory.Exists(trimmed)) return "";

            try
            {
                string full = Path.GetFullPath(trimmed);
                if (HasRoDataFiles(full)) return full;

                // LATAM installer puts data.grf inside a "Ragnarok" subfolder
                string sub = Path.Combine(full, "Ragnarok");
                if (HasRoDataFiles(sub)) return sub;

                // In case the user picked a subfolder inside RO (such as "data", "BGM", or "System")
                DirectoryInfo parent = Directory.GetParent(full);
                if (parent != null && HasRoDataFiles(parent.FullName)) return parent.FullName;
            }
            catch { }
            return "";
        }

        private static string FindRoCandidateOnDisk()
        {
            string[] relPaths =
            {
                "Ragnarok Online",
                @"Ragnarok Online\Ragnarok",
                "Ragnarok",
                @"Gravity\Ragnarok Online",
                @"Gravity\Ragnarok",
                @"Games\Ragnarok Online",
                @"Games\Ragnarok",
                @"Program Files (x86)\Gravity\Ragnarok Online",
                @"Program Files\Gravity\Ragnarok Online",
                @"Program Files (x86)\Ragnarok Online",
                @"Program Files\Ragnarok Online",
                @"SteamLibrary\steamapps\common\Ragnarok",
                @"Program Files (x86)\Steam\steamapps\common\Ragnarok"
            };

            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                    string root = drive.RootDirectory.FullName;
                    foreach (string rel in relPaths)
                    {
                        string candidate = Path.Combine(root, rel);
                        string resolved = ResolveValidRoDataRoot(candidate);
                        if (!string.IsNullOrEmpty(resolved))
                            return resolved;
                    }
                }
            }
            catch { }
            return "";
        }

        private void LoadConfiguredRoFolder()
        {
            _roDataFolder = "";

            // 1. Check loki-user.json (AppData\LocalLow\Project Loki\Loki\loki-user.json)
            if (_opts.IsHeadless) return;
            try
            {
                string userPath = GetUserSettingsFilePath();
                if (File.Exists(userPath))
                {
                    string userJson = File.ReadAllText(userPath, Encoding.UTF8);
                    string rawUserFolder = UnescapeJsonString(ParseJsonString(userJson, "dataFolder"));
                    string valid = ResolveValidRoDataRoot(rawUserFolder);
                    if (!string.IsNullOrEmpty(valid))
                    {
                        _roDataFolder = valid;
                        return;
                    }
                }
            }
            catch { }

            // 2. Check StreamingAssets\loki.json
            try
            {
                string lokiJsonPath = GetLokiJsonFilePath();
                if (File.Exists(lokiJsonPath))
                {
                    string lokiJson = File.ReadAllText(lokiJsonPath, Encoding.UTF8);
                    string rawClientRoot = UnescapeJsonString(ParseJsonString(lokiJson, "clientRoot"));
                    string valid = ResolveValidRoDataRoot(rawClientRoot);
                    if (!string.IsNullOrEmpty(valid))
                    {
                        _roDataFolder = valid;
                        return;
                    }
                }
            }
            catch { }
        }

        private void SaveConfiguredRoFolder(string validFolder)
        {
            if (string.IsNullOrEmpty(validFolder)) return;
            _roDataFolder = validFolder;
            string escaped = EscapeJsonString(validFolder);

            // 1. Save to loki-user.json (AppData\LocalLow\Project Loki\Loki\loki-user.json)
            if (_opts.IsHeadless) return;
            try
            {
                string userPath = GetUserSettingsFilePath();
                string userDir = Path.GetDirectoryName(userPath);
                if (!string.IsNullOrEmpty(userDir)) Directory.CreateDirectory(userDir);

                if (File.Exists(userPath))
                {
                    string json = File.ReadAllText(userPath, Encoding.UTF8);
                    if (Regex.IsMatch(json, "\"dataFolder\"\\s*:\\s*\"(?:\\\\.|[^\"\\\\])*\""))
                    {
                        json = Regex.Replace(
                            json,
                            "\"dataFolder\"\\s*:\\s*\"(?:\\\\.|[^\"\\\\])*\"",
                            "\"dataFolder\": \"" + escaped + "\"");
                    }
                    else
                    {
                        int brace = json.IndexOf('{');
                        if (brace >= 0)
                        {
                            json = json.Insert(brace + 1, "\r\n  \"dataFolder\": \"" + escaped + "\",");
                        }
                    }
                    File.WriteAllText(userPath, json, new UTF8Encoding(false));
                }
                else
                {
                    string newJson = "{\r\n  \"version\": 1,\r\n  \"dataFolder\": \"" + escaped + "\"\r\n}\r\n";
                    File.WriteAllText(userPath, newJson, new UTF8Encoding(false));
                }
            }
            catch { }

            // 2. Save to StreamingAssets\loki.json ("clientRoot")
            try
            {
                string lokiJsonPath = GetLokiJsonFilePath();
                if (File.Exists(lokiJsonPath))
                {
                    string json = File.ReadAllText(lokiJsonPath, Encoding.UTF8);
                    if (Regex.IsMatch(json, "\"clientRoot\"\\s*:\\s*\"(?:\\\\.|[^\"\\\\])*\""))
                    {
                        json = Regex.Replace(
                            json,
                            "\"clientRoot\"\\s*:\\s*\"(?:\\\\.|[^\"\\\\])*\"",
                            "\"clientRoot\": \"" + escaped + "\"");
                        File.WriteAllText(lokiJsonPath, json, new UTF8Encoding(true));
                    }
                }
            }
            catch { }
        }

        private static string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string UnescapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\\\", "\\").Replace("\\\"", "\"");
        }

        private void ResolveInstallDirectory()
        {
            if (!string.IsNullOrEmpty(_opts.InstallDir) && Directory.Exists(_opts.InstallDir))
            {
                _resolvedInstallDir = Path.GetFullPath(_opts.InstallDir);
            }
            else
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (File.Exists(Path.Combine(baseDir, "Loki.exe")))
                {
                    _resolvedInstallDir = baseDir;
                }
                else if (File.Exists(Path.Combine(baseDir, "Loki", "Loki.exe")))
                {
                    _resolvedInstallDir = Path.Combine(baseDir, "Loki");
                }
                else if (Directory.Exists(Path.Combine(baseDir, "Loki_Data")))
                {
                    _resolvedInstallDir = baseDir;
                }
                else
                {
                    _resolvedInstallDir = Path.Combine(baseDir, "Loki");
                }
            }

            CleanupOldBinaries(_resolvedInstallDir);
            DirectoryInfo parentInfo = Directory.GetParent(_resolvedInstallDir);
            string parent = parentInfo != null ? parentInfo.FullName : null;
            if (!string.IsNullOrEmpty(parent)) CleanupOldBinaries(parent);

            _localVersion = 0;
            _manifestUrl = DefaultManifestUrl;
            _githubRepo = _opts.GitHubRepo ?? "";
            _autoUpdateEnabled = _opts.ForceAutoUpdate;
            try
            {
                string vPath = Path.Combine(_resolvedInstallDir, "Loki_Data", "StreamingAssets", "version.json");
                if (File.Exists(vPath))
                {
                    string json = File.ReadAllText(vPath, Encoding.UTF8);
                    _localVersion = ParseJsonUInt(json, "version");
                    string mUrl = ParseJsonString(json, "manifestUrl");
                    string repo = ParseJsonString(json, "githubRepo");
                    if (!string.IsNullOrEmpty(mUrl) && !mUrl.Contains("YOUR_GITHUB_USER")) _manifestUrl = mUrl;
                    if (string.IsNullOrEmpty(_githubRepo) && !string.IsNullOrEmpty(repo) && !repo.Contains("YOUR_GITHUB_USER")) _githubRepo = repo;
                    if (Regex.IsMatch(json, "\"autoUpdate\"\\s*:\\s*false", RegexOptions.IgnoreCase)) _autoUpdateEnabled = false;
                    else if (Regex.IsMatch(json, "\"autoUpdate\"\\s*:\\s*true", RegexOptions.IgnoreCase)) _autoUpdateEnabled = true;
                }
                string userPath = GetUserSettingsFilePath();
                if (File.Exists(userPath))
                {
                    string uJson = File.ReadAllText(userPath, Encoding.UTF8);
                    string uRepo = ParseJsonString(uJson, "githubRepo");
                    if (!string.IsNullOrEmpty(uRepo)) _githubRepo = uRepo;
                    if (Regex.IsMatch(uJson, "\"autoUpdate\"\\s*:\\s*true", RegexOptions.IgnoreCase)) _autoUpdateEnabled = true;
                    else if (Regex.IsMatch(uJson, "\"autoUpdate\"\\s*:\\s*false", RegexOptions.IgnoreCase)) _autoUpdateEnabled = false;
                }
                if (_opts.ForceAutoUpdate) _autoUpdateEnabled = true;
                if (string.IsNullOrEmpty(_githubRepo) && string.IsNullOrEmpty(_manifestUrl))
                {
                    _githubRepo = TryDetectGitRepoFromDisk(_resolvedInstallDir);
                }
                if (!string.IsNullOrEmpty(_githubRepo))
                {
                    _manifestUrl = string.Format("https://github.com/{0}/releases/download/latest/version.json", _githubRepo.Trim().Trim('/'));
                }
            }
            catch { }

            LoadConfiguredRoFolder();
            RefreshRollbackState();
        }

        private static string TryDetectGitRepoFromDisk(string startDir)
        {
            try
            {
                string dir = startDir;
                for (int depth = 0; depth < 5 && !string.IsNullOrEmpty(dir); depth++)
                {
                    string gitCfg = Path.Combine(dir, ".git", "config");
                    if (File.Exists(gitCfg))
                    {
                        string cfg = File.ReadAllText(gitCfg, Encoding.UTF8);
                        var m = Regex.Match(cfg, @"github\.com[:/]([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+?)(?:\.git)?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                        if (m.Success)
                            return m.Groups[1].Value.Trim();
                    }
                    DirectoryInfo parent = Directory.GetParent(dir);
                    dir = parent != null ? parent.FullName : null;
                }
                string exePath = Application.ExecutablePath;
                string[] adsCandidates =
                {
                    exePath + ":Zone.Identifier",
                    Path.Combine(startDir ?? "", "LokiLauncher.exe:Zone.Identifier"),
                    Path.Combine(startDir ?? "", "Loki-VR.zip:Zone.Identifier")
                };
                foreach (string ads in adsCandidates)
                {
                    try
                    {
                        if (File.Exists(ads))
                        {
                            string zoneText = File.ReadAllText(ads, Encoding.UTF8);
                            var zm = Regex.Match(zoneText, @"github\.com/([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+?)(?:/|\r|\n|$)", RegexOptions.IgnoreCase);
                            if (zm.Success)
                                return zm.Groups[1].Value.Trim();
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return "";
        }

        private string GetRollbackBackupDir()
        {
            return Path.Combine(_resolvedInstallDir, "_rollback_backup");
        }

        private void RefreshRollbackState()
        {
            _rollbackAvailable = false;
            _rollbackVersion = 0;
            _rollbackCommit = "";
            try
            {
                if (string.IsNullOrEmpty(_resolvedInstallDir)) return;
                string backupDir = GetRollbackBackupDir();
                string metaPath = Path.Combine(backupDir, "rollback-meta.json");
                string filesDir = Path.Combine(backupDir, "files");
                if (File.Exists(metaPath) && Directory.Exists(filesDir) && File.Exists(Path.Combine(filesDir, "Loki.exe")))
                {
                    string json = File.ReadAllText(metaPath, Encoding.UTF8);
                    _rollbackVersion = ParseJsonUInt(json, "previousVersion");
                    _rollbackCommit = ParseJsonString(json, "previousCommit");
                    _rollbackAvailable = true;
                }
            }
            catch { }
        }

        private static void CleanupOldBinaries(string dir)
        {
            try
            {
                string oldFile = Path.Combine(dir, "LokiLauncher.exe.old");
                if (File.Exists(oldFile)) File.Delete(oldFile);
            }
            catch { }
        }

        private void RunWorkflowAsync()
        {
            try
            {
                ResolveInstallDirectory();

                if (_opts.IsRollbackMode)
                {
                    if (!_rollbackAvailable)
                        throw new InvalidOperationException("No rollback backup is available in this installation.");
                    WaitForGameExit(_opts.WaitPid);
                    PerformRollback();
                    return;
                }

                if (_opts.IsUpdateMode && _opts.TargetVersion > 0 && (!string.IsNullOrEmpty(_opts.DownloadUrl) || !string.IsNullOrEmpty(_opts.ExistingZipPath)))
                {
                    string badge = _localVersion > 0
                        ? string.Format("UPDATE v{0} → v{1}", _localVersion, _opts.TargetVersion)
                        : string.Format("UPDATE TO v{0}", _opts.TargetVersion);
                    SetState("Preparing Loki Update...", "Waiting for Loki to close...", 0.03f, true, badge);
                    WaitForGameExit(_opts.WaitPid);
                    PerformUpdate(_opts.TargetVersion, _opts.SizeBytes, _opts.ExpectedSha256, _opts.Commit, _opts.DownloadUrl, _opts.ExistingZipPath);
                    return;
                }

                bool hasInstalledGame = !string.IsNullOrEmpty(_resolvedInstallDir) && File.Exists(Path.Combine(_resolvedInstallDir, "Loki.exe"));
                string startBadge = _localVersion > 0 ? string.Format("INSTALLED v{0}", _localVersion) : "LOKI ONLINE";

                // Check remote version.json on GitHub when Auto-Update is enabled OR when the game is not installed yet
                if ((_autoUpdateEnabled || !hasInstalledGame) && !string.IsNullOrEmpty(_manifestUrl))
                {
                    SetState(
                        hasInstalledGame ? "Checking for Updates..." : "Downloading Loki Online...",
                        "Contacting GitHub release server...",
                        0.1f,
                        true,
                        startBadge);

                    uint remoteVer;
                    long remoteBytes;
                    string remoteSha;
                    string remoteCommit;
                    string remoteUrl;
                    if (TryFetchRemoteManifest(out remoteVer, out remoteBytes, out remoteSha, out remoteCommit, out remoteUrl))
                    {
                        if ((!hasInstalledGame || remoteVer > _localVersion) && !string.IsNullOrEmpty(remoteUrl) && !string.IsNullOrEmpty(remoteSha))
                        {
                            string upBadge = _localVersion > 0
                                ? string.Format("UPDATE v{0} → v{1}", _localVersion, remoteVer)
                                : string.Format("INSTALL v{0}", remoteVer);
                            SetState(
                                string.Format(hasInstalledGame ? "New Version Available (v{0})" : "Installing Loki Online (v{0})", remoteVer),
                                hasInstalledGame ? "Creating rollback point and applying update..." : "Starting download...",
                                0.05f,
                                false,
                                upBadge);
                            Thread.Sleep(350);
                            PerformUpdate(remoteVer, remoteBytes, remoteSha, remoteCommit, remoteUrl, "");
                            return;
                        }
                    }
                }

                // Ensure Ragnarok Online folder is configured before launching
                ProceedToLaunchOrPromptRo(startBadge);
            }
            catch (Exception ex)
            {
                bool hasGame = !string.IsNullOrEmpty(_resolvedInstallDir) && File.Exists(Path.Combine(_resolvedInstallDir, "Loki.exe"));
                RefreshRollbackState();
                ShowError("Update Could Not Complete", ex.Message, hasGame);
            }
        }

        private void StartRollbackWorkflow()
        {
            if (_working) return;
            RefreshRollbackState();
            if (!_rollbackAvailable) return;

            _working = true;
            _hasError = false;
            _awaitingRoFolder = false;
            Task.Run(new Action(RunRollbackAsync));
        }

        private void RunRollbackAsync()
        {
            try
            {
                ResolveInstallDirectory();
                if (!_rollbackAvailable)
                    throw new InvalidOperationException("No rollback backup is available in this installation.");

                WaitForGameExit(_opts.WaitPid);
                PerformRollback();
            }
            catch (Exception ex)
            {
                bool hasGame = !string.IsNullOrEmpty(_resolvedInstallDir) && File.Exists(Path.Combine(_resolvedInstallDir, "Loki.exe"));
                RefreshRollbackState();
                ShowError("Rollback Could Not Complete", ex.Message, hasGame);
            }
        }

        private void PromptAndRollback()
        {
            if (_working || !_rollbackAvailable) return;
            string verLabel = _rollbackVersion > 0 ? string.Format("v{0}", _rollbackVersion) : "the previous version";
            DialogResult res = MessageBox.Show(
                this,
                string.Format(
                    "Roll back Loki Online to {0}?\r\n\r\nYour Ragnarok Online folder, saved servers, and player settings will be preserved, and Auto-Update will be turned OFF so the rolled-back version is kept until you re-enable it.",
                    verLabel),
                "Roll Back Update",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (res == DialogResult.Yes)
            {
                StartRollbackWorkflow();
            }
        }

        private void ProceedToLaunchOrPromptRo(string badge)
        {
            if (!string.IsNullOrEmpty(badge))
                _badgeText = badge;
            if (_opts.IsHeadless)
            {
                if (InvokeRequired) BeginInvoke(new Action(Close));
                else Close();
                return;
            }
            LoadConfiguredRoFolder();
            if (!string.IsNullOrEmpty(_roDataFolder))
            {
                SaveConfiguredRoFolder(_roDataFolder);
            }
            ShowRoFolderPrompt("");
        }

        private static void WaitForGameExit(int pid)
        {
            if (pid <= 0) return;
            for (int i = 0; i < 60; i++)
            {
                try
                {
                    using (var proc = Process.GetProcessById(pid))
                    {
                        if (proc.HasExited) break;
                    }
                }
                catch
                {
                    break;
                }
                Thread.Sleep(200);
            }
            Thread.Sleep(300);
        }

        private bool TryFetchRemoteManifest(out uint version, out long sizeBytes, out string sha256, out string commit, out string downloadUrl)
        {
            version = 0; sizeBytes = 0; sha256 = ""; commit = ""; downloadUrl = "";
            try
            {
                long ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                string sep = _manifestUrl.Contains("?") ? "&" : "?";
                string url = _manifestUrl + sep + "t=" + ts;
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.UserAgent = "LokiLauncher/1.0";
                req.Timeout = 8000;
                req.ReadWriteTimeout = 8000;
                req.Headers["Cache-Control"] = "no-cache, no-store";
                req.AllowAutoRedirect = true;

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string json = sr.ReadToEnd();
                    version = ParseJsonUInt(json, "version");
                    sizeBytes = ParseJsonLong(json, "sizeBytes");
                    sha256 = ParseJsonString(json, "sha256").Trim().ToLowerInvariant();
                    commit = ParseJsonString(json, "commit").Trim();
                    downloadUrl = ParseJsonString(json, "downloadUrl").Trim();
                    if (string.IsNullOrEmpty(downloadUrl))
                        downloadUrl = "Loki-VR.zip";
                    if (!downloadUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !downloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        int lastSlash = _manifestUrl.LastIndexOf('/');
                        if (lastSlash > 8)
                            downloadUrl = _manifestUrl.Substring(0, lastSlash + 1) + downloadUrl.TrimStart('/');
                    }
                    return version > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private void CreateRollbackBackup(string stagedLokiDir, uint newVersion, string badge)
        {
            if (string.IsNullOrEmpty(_resolvedInstallDir) || !File.Exists(Path.Combine(_resolvedInstallDir, "Loki.exe")))
                return;

            SetState("Creating Rollback Point...", "Backing up current version before installing update...", 0.89f, false, badge);

            string backupDir = GetRollbackBackupDir();
            string tempBackupDir = Path.Combine(_resolvedInstallDir, "_rollback_backup_tmp");
            string tempFilesDir = Path.Combine(tempBackupDir, "files");

            try
            {
                if (Directory.Exists(tempBackupDir)) Directory.Delete(tempBackupDir, true);
                Directory.CreateDirectory(tempFilesDir);

                string prevCommit = "";
                uint prevVer = _localVersion;
                string curVerPath = Path.Combine(_resolvedInstallDir, "Loki_Data", "StreamingAssets", "version.json");
                if (File.Exists(curVerPath))
                {
                    string vJson = File.ReadAllText(curVerPath, Encoding.UTF8);
                    if (prevVer == 0) prevVer = ParseJsonUInt(vJson, "version");
                    prevCommit = ParseJsonString(vJson, "commit");
                }

                var addedFiles = new List<string>();
                string[] stagedFiles = Directory.GetFiles(stagedLokiDir, "*", SearchOption.AllDirectories);
                int total = Math.Max(1, stagedFiles.Length);
                int done = 0;

                foreach (string stagedFile in stagedFiles)
                {
                    done++;
                    string rel = stagedFile.Substring(stagedLokiDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (rel.StartsWith("_rollback_backup", StringComparison.OrdinalIgnoreCase)) continue;

                    string existingFile = Path.Combine(_resolvedInstallDir, rel);
                    if (File.Exists(existingFile))
                    {
                        string backupFile = Path.Combine(tempFilesDir, rel);
                        string backupSubDir = Path.GetDirectoryName(backupFile);
                        if (!string.IsNullOrEmpty(backupSubDir)) Directory.CreateDirectory(backupSubDir);
                        CopyFileSafe(existingFile, backupFile);
                    }
                    else
                    {
                        addedFiles.Add(rel);
                    }

                    if (done % 12 == 0 || done == total)
                    {
                        float r = (float)done / total;
                        SetState(
                            "Creating Rollback Point...",
                            string.Format("Backing up previous version ({0} / {1})...", done, total),
                            0.89f + r * 0.02f,
                            false,
                            badge);
                    }
                }

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendFormat("  \"previousVersion\": {0},\r\n", prevVer);
                sb.AppendFormat("  \"previousCommit\": \"{0}\",\r\n", EscapeJsonString(prevCommit));
                sb.AppendFormat("  \"updatedToVersion\": {0},\r\n", newVersion);
                sb.AppendFormat("  \"createdAtUtc\": \"{0}\"\r\n", DateTime.UtcNow.ToString("o"));
                sb.AppendLine("}");
                File.WriteAllText(Path.Combine(tempBackupDir, "rollback-meta.json"), sb.ToString(), new UTF8Encoding(false));
                File.WriteAllLines(Path.Combine(tempBackupDir, "added-files.txt"), addedFiles.ToArray(), new UTF8Encoding(false));

                if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);
                Directory.Move(tempBackupDir, backupDir);
            }
            catch
            {
                try { if (Directory.Exists(tempBackupDir)) Directory.Delete(tempBackupDir, true); } catch { }
            }
        }

        private void PerformRollback()
        {
            string backupDir = GetRollbackBackupDir();
            string filesDir = Path.Combine(backupDir, "files");
            string addedListPath = Path.Combine(backupDir, "added-files.txt");

            string verLabel = _rollbackVersion > 0 ? string.Format("v{0}", _rollbackVersion) : "Previous Version";
            string badge = _rollbackVersion > 0 ? string.Format("ROLLBACK TO v{0}", _rollbackVersion) : "ROLLBACK";

            SetState(string.Format("Rolling Back to {0}...", verLabel), "Preserving current configuration...", 0.05f, false, badge);
            Thread.Sleep(200);

            string existingLokiJson = GetLokiJsonFilePath();
            string savedLokiJsonText = File.Exists(existingLokiJson) ? File.ReadAllText(existingLokiJson, Encoding.UTF8) : null;

            CopyDirectoryWithRetry(filesDir, _resolvedInstallDir, delegate(int done, int total)
            {
                float r = (float)done / Math.Max(1, total);
                SetState(
                    string.Format("Rolling Back to {0}...", verLabel),
                    string.Format("Restoring previous version files ({0} / {1})...", done, total),
                    0.10f + r * 0.78f,
                    false,
                    badge);
            });

            if (File.Exists(addedListPath))
            {
                try
                {
                    string[] addedFiles = File.ReadAllLines(addedListPath, Encoding.UTF8);
                    foreach (string rel in addedFiles)
                    {
                        if (string.IsNullOrWhiteSpace(rel)) continue;
                        string full = Path.Combine(_resolvedInstallDir, rel.Trim());
                        if (File.Exists(full))
                        {
                            try { File.Delete(full); } catch { }
                        }
                    }
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(savedLokiJsonText) && File.Exists(existingLokiJson))
            {
                try { File.WriteAllText(existingLokiJson, savedLokiJsonText, new UTF8Encoding(true)); } catch { }
            }

            // Pause automatic updates after rollback so the restored version is kept until re-enabled
            _autoUpdateEnabled = false;
            SaveAutoUpdatePreference(false);

            try
            {
                if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);
            }
            catch { }

            _justRolledBack = true;
            ResolveInstallDirectory();
            string doneBadge = _localVersion > 0 ? string.Format("ROLLED BACK v{0}", _localVersion) : "ROLLED BACK";
            ProceedToLaunchOrPromptRo(doneBadge);
        }

        private void PerformUpdate(uint version, long sizeBytes, string expectedSha256, string commit, string url, string existingZipPath)
        {
            _justRolledBack = false;
            string shortCommit = string.IsNullOrEmpty(commit) ? "" : string.Format(" ({0})", commit);
            string badge = _localVersion > 0 && _localVersion != version
                ? string.Format("UPDATE v{0} → v{1}", _localVersion, version)
                : string.Format("UPDATE v{0}{1}", version, shortCommit);

            string tempRoot = Path.Combine(Path.GetTempPath(), string.Format("loki-update-v{0}", version));
            bool isProvidedZip = !string.IsNullOrEmpty(existingZipPath) && File.Exists(existingZipPath);
            string zipPath = isProvidedZip
                ? existingZipPath
                : Path.Combine(tempRoot, "Loki-VR.zip");
            string stageDir = Path.Combine(tempRoot, "stage");

            Directory.CreateDirectory(tempRoot);

            bool needDownload = !isProvidedZip;
            if (File.Exists(zipPath) && !string.IsNullOrEmpty(expectedSha256))
            {
                try
                {
                    string existingHash = ComputeSha256(zipPath, null);
                    needDownload = !string.Equals(existingHash, expectedSha256, StringComparison.OrdinalIgnoreCase);
                }
                catch { }
            }

            if (needDownload)
            {
                if (File.Exists(zipPath))
                {
                    try { File.Delete(zipPath); } catch { }
                }

                SetState(string.Format("Downloading Loki v{0}{1}...", version, shortCommit), "Connecting to GitHub release server...", 0.02f, false, badge);

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.UserAgent = "LokiLauncher/1.0";
                req.Timeout = 20000;
                req.ReadWriteTimeout = 30000;
                req.AllowAutoRedirect = true;

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var netStream = resp.GetResponseStream())
                using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
                {
                    long total = sizeBytes > 0 ? sizeBytes : resp.ContentLength;
                    byte[] buffer = new byte[65536];
                    long downloaded = 0;
                    var sw = Stopwatch.StartNew();
                    long lastReportMs = 0;

                    int read;
                    while ((read = netStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        fileStream.Write(buffer, 0, read);
                        downloaded += read;

                        long elapsedMs = sw.ElapsedMilliseconds;
                        if (elapsedMs - lastReportMs >= 60 || downloaded == total)
                        {
                            lastReportMs = elapsedMs;
                            double mbDone = downloaded / (1024.0 * 1024.0);
                            double sec = Math.Max(0.1, elapsedMs / 1000.0);
                            double speedMb = mbDone / sec;

                            if (total > 0)
                            {
                                float ratio = (float)Math.Min(1.0, (double)downloaded / total);
                                int pct = (int)Math.Round(ratio * 100.0);
                                double mbTotal = total / (1024.0 * 1024.0);
                                SetState(
                                    string.Format("Downloading Loki v{0}{1}...", version, shortCommit),
                                    string.Format("{0:0.0} MB / {1:0.0} MB  ({2}%)   •   {3:0.0} MB/s", mbDone, mbTotal, pct, speedMb),
                                    0.04f + ratio * 0.68f,
                                    false,
                                    badge);
                            }
                            else
                            {
                                SetState(
                                    string.Format("Downloading Loki v{0}{1}...", version, shortCommit),
                                    string.Format("{0:0.0} MB downloaded   •   {1:0.0} MB/s", mbDone, speedMb),
                                    0.40f,
                                    true,
                                    badge);
                            }
                        }
                    }
                }
            }

            // Step 2: Verify SHA-256
            SetState(string.Format("Verifying Update v{0}{1}...", version, shortCommit), "Checking SHA-256 package signature...", 0.74f, false, badge);
            string actualSha = ComputeSha256(zipPath, delegate(float r)
            {
                SetState(
                    string.Format("Verifying Update v{0}{1}...", version, shortCommit),
                    string.Format("Checking SHA-256 integrity ({0}%)...", (int)(r * 100)),
                    0.72f + r * 0.08f,
                    false,
                    badge);
            });

            if (!string.IsNullOrEmpty(expectedSha256) && !string.Equals(actualSha, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(zipPath); } catch { }
                throw new InvalidOperationException(string.Format(
                    "Package verification failed (expected SHA-256 {0}..., got {1}...).",
                    expectedSha256.Substring(0, Math.Min(12, expectedSha256.Length)),
                    actualSha.Substring(0, Math.Min(12, actualSha.Length))));
            }

            // Step 3: Preserve existing loki.json & extract archive
            string existingLokiJson = Path.Combine(_resolvedInstallDir, "Loki_Data", "StreamingAssets", "loki.json");
            string savedLokiJsonText = File.Exists(existingLokiJson) ? File.ReadAllText(existingLokiJson, Encoding.UTF8) : null;

            if (Directory.Exists(stageDir))
            {
                try { Directory.Delete(stageDir, true); } catch { }
            }
            Directory.CreateDirectory(stageDir);

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                int totalEntries = Math.Max(1, archive.Entries.Count);
                int processed = 0;
                foreach (var entry in archive.Entries)
                {
                    processed++;
                    string relPath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    string destPath = Path.Combine(stageDir, relPath);
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destPath);
                        continue;
                    }
                    string destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                    entry.ExtractToFile(destPath, true);

                    if (processed % 8 == 0 || processed == totalEntries)
                    {
                        float r = (float)processed / totalEntries;
                        SetState(
                            string.Format("Extracting Update v{0}{1}...", version, shortCommit),
                            string.Format("Unpacking files ({0} / {1})...", processed, totalEntries),
                            0.80f + r * 0.09f,
                            false,
                            badge);
                    }
                }
            }

            if (!string.IsNullOrEmpty(savedLokiJsonText))
            {
                string stagedLokiJson = Path.Combine(stageDir, "Loki", "Loki_Data", "StreamingAssets", "loki.json");
                if (File.Exists(stagedLokiJson))
                {
                    File.WriteAllText(stagedLokiJson, savedLokiJsonText, new UTF8Encoding(true));
                }
            }

            // Step 4: Create Rollback Point before overwriting existing installation
            string stagedLokiDir = Path.Combine(stageDir, "Loki");
            if (Directory.Exists(stagedLokiDir))
            {
                CreateRollbackBackup(stagedLokiDir, version, badge);

                // Step 5: Copy staged files into installDir
                SetState(string.Format("Installing Loki v{0}{1}...", version, shortCommit), "Applying updated game files...", 0.92f, false, badge);
                Directory.CreateDirectory(_resolvedInstallDir);
                CopyDirectoryWithRetry(stagedLokiDir, _resolvedInstallDir, delegate(int done, int total)
                {
                    float r = (float)done / Math.Max(1, total);
                    SetState(
                        string.Format("Installing Loki v{0}{1}...", version, shortCommit),
                        string.Format("Updating game files ({0} / {1})...", done, total),
                        0.91f + r * 0.07f,
                        false,
                        badge);
                });
            }

            DirectoryInfo parentInfo = Directory.GetParent(_resolvedInstallDir);
            string parentDir = parentInfo != null ? parentInfo.FullName : _resolvedInstallDir;
            string[] rootDocs = { "LEIA-ME.txt", "READ-ME.txt", "THIRD_PARTY.md", "LICENSE.txt", "Loki-source.zip", "LokiLauncher.exe" };
            foreach (string doc in rootDocs)
            {
                string srcDoc = Path.Combine(stageDir, doc);
                string dstDoc = Path.Combine(parentDir, doc);
                if (File.Exists(srcDoc) && (File.Exists(dstDoc) || string.Equals(doc, "LokiLauncher.exe", StringComparison.OrdinalIgnoreCase)))
                {
                    try { CopyFileSafe(srcDoc, dstDoc); } catch { }
                }
            }

            try
            {
                if (Directory.Exists(stageDir)) Directory.Delete(stageDir, true);
                if (!isProvidedZip && File.Exists(zipPath)) File.Delete(zipPath);
            }
            catch { }

            _localVersion = version;
            RefreshRollbackState();
            ProceedToLaunchOrPromptRo(string.Format("UPDATED v{0}", version));
        }

        private static string ComputeSha256(string filePath, Action<float> onProgress)
        {
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
            {
                long len = Math.Max(1, fs.Length);
                long readTotal = 0;
                byte[] buf = new byte[65536];
                int n;
                long lastTick = 0;
                var sw = Stopwatch.StartNew();
                while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    sha.TransformBlock(buf, 0, n, null, 0);
                    readTotal += n;
                    if (onProgress != null && sw.ElapsedMilliseconds - lastTick >= 60)
                    {
                        lastTick = sw.ElapsedMilliseconds;
                        onProgress((float)((double)readTotal / len));
                    }
                }
                sha.TransformFinalBlock(buf, 0, 0);
                return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void CopyDirectoryWithRetry(string sourceDir, string targetDir, Action<int, int> onProgress)
        {
            string[] allFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
            int total = allFiles.Length;
            int done = 0;
            foreach (string srcFile in allFiles)
            {
                string rel = srcFile.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string dstFile = Path.Combine(targetDir, rel);
                string dstSubDir = Path.GetDirectoryName(dstFile);
                if (!string.IsNullOrEmpty(dstSubDir)) Directory.CreateDirectory(dstSubDir);
                CopyFileSafe(srcFile, dstFile);
                done++;
                if (onProgress != null && (done % 6 == 0 || done == total))
                    onProgress(done, total);
            }
        }

        private static void CopyFileSafe(string srcFile, string dstFile)
        {
            Exception lastErr = null;
            for (int attempt = 0; attempt < 15; attempt++)
            {
                try
                {
                    if (File.Exists(dstFile) && dstFile.EndsWith("LokiLauncher.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        string oldPath = dstFile + ".old";
                        try
                        {
                            if (File.Exists(oldPath)) File.Delete(oldPath);
                            File.Move(dstFile, oldPath);
                        }
                        catch { }
                    }
                    File.Copy(srcFile, dstFile, true);
                    return;
                }
                catch (Exception ex)
                {
                    lastErr = ex;
                    Thread.Sleep(200);
                }
            }
            if (lastErr != null) throw lastErr;
        }

        private void BrowseAndSelectRoFolder()
        {
            string initial = !string.IsNullOrEmpty(_roDataFolder)
                ? _roDataFolder
                : (!string.IsNullOrEmpty(_detectedRoCandidate) ? _detectedRoCandidate : "");

            string picked = ShowFolderPickerDialog(this.Handle, "Select your Ragnarok Online folder (containing data.grf)", initial);
            if (string.IsNullOrEmpty(picked)) return;

            string valid = ResolveValidRoDataRoot(picked);
            if (string.IsNullOrEmpty(valid))
            {
                ShowRoFolderPrompt(string.Format("No data.grf was found in \"{0}\".\r\nPlease select the folder where Ragnarok Online is installed.", picked));
                return;
            }

            SaveConfiguredRoFolder(valid);
            ShowRoFolderPrompt("");
        }

        private static string ShowFolderPickerDialog(IntPtr ownerHwnd, string title, string initialDir)
        {
            try
            {
                string modern = VistaFolderPicker.Show(ownerHwnd, title, initialDir);
                if (modern != null) return modern;
            }
            catch { }

            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = title;
                dlg.ShowNewFolderButton = false;
                if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir))
                    dlg.SelectedPath = initialDir;
                return dlg.ShowDialog() == DialogResult.OK ? dlg.SelectedPath : "";
            }
        }

        private void SaveAutoUpdatePreference(bool enabled)
        {
            string val = enabled ? "true" : "false";
            try
            {
                string vPath = Path.Combine(_resolvedInstallDir, "Loki_Data", "StreamingAssets", "version.json");
                if (File.Exists(vPath))
                {
                    string json = File.ReadAllText(vPath, Encoding.UTF8);
                    if (Regex.IsMatch(json, "\"autoUpdate\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase))
                        json = Regex.Replace(json, "\"autoUpdate\"\\s*:\\s*(true|false)", "\"autoUpdate\": " + val, RegexOptions.IgnoreCase);
                    else
                    {
                        int brace = json.IndexOf('{');
                        if (brace >= 0)
                            json = json.Insert(brace + 1, "\r\n  \"autoUpdate\": " + val + ",");
                    }
                    File.WriteAllText(vPath, json, new UTF8Encoding(false));
                }
            }
            catch { }

            if (_opts.IsHeadless) return;
            try
            {
                string userPath = GetUserSettingsFilePath();
                if (File.Exists(userPath))
                {
                    string uJson = File.ReadAllText(userPath, Encoding.UTF8);
                    if (Regex.IsMatch(uJson, "\"autoUpdate\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase))
                        uJson = Regex.Replace(uJson, "\"autoUpdate\"\\s*:\\s*(true|false)", "\"autoUpdate\": " + val, RegexOptions.IgnoreCase);
                    else
                    {
                        int brace = uJson.IndexOf('{');
                        if (brace >= 0)
                            uJson = uJson.Insert(brace + 1, "\r\n  \"autoUpdate\": " + val + ",");
                    }
                    File.WriteAllText(userPath, uJson, new UTF8Encoding(false));
                }
            }
            catch { }
        }

        private void ToggleAutoUpdatePreference()
        {
            _autoUpdateEnabled = !_autoUpdateEnabled;
            SaveAutoUpdatePreference(_autoUpdateEnabled);
            Invalidate();
            if (_autoUpdateEnabled && !string.IsNullOrEmpty(_manifestUrl))
            {
                StartWorkflow();
            }
        }

        private void LaunchGameAndExit()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(LaunchGameAndExit));
                return;
            }
            if (!string.IsNullOrEmpty(_roDataFolder))
            {
                SaveConfiguredRoFolder(_roDataFolder);
            }
            string exePath = Path.Combine(_resolvedInstallDir, "Loki.exe");
            if (File.Exists(exePath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = _resolvedInstallDir,
                    UseShellExecute = false,
                };
                Process.Start(psi);
            }
            Close();
        }

        private static uint ParseJsonUInt(string json, string key)
        {
            var m = Regex.Match(json ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*(\\d+)");
            uint v;
            return m.Success && uint.TryParse(m.Groups[1].Value, out v) ? v : 0;
        }

        private static long ParseJsonLong(string json, string key)
        {
            var m = Regex.Match(json ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*(\\d+)");
            long v;
            return m.Success && long.TryParse(m.Groups[1].Value, out v) ? v : 0;
        }

        private static string ParseJsonString(string json, string key)
        {
            var m = Regex.Match(json ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            return m.Success ? m.Groups[1].Value : "";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            int w = ClientSize.Width;
            int h = ClientSize.Height;

            // 1. Background gradient
            using (var bgBrush = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(18, 21, 31), Color.FromArgb(11, 13, 19), LinearGradientMode.Vertical))
            {
                g.FillRectangle(bgBrush, 0, 0, w, h);
            }

            // Subtle top gold ambient glow
            using (var glowPath = new GraphicsPath())
            {
                glowPath.AddEllipse(-S(80), -S(120), w + S(160), S(240));
                using (var pgb = new PathGradientBrush(glowPath))
                {
                    pgb.CenterColor = Color.FromArgb(32, 224, 178, 74);
                    pgb.SurroundColors = new[] { Color.FromArgb(0, 224, 178, 74) };
                    g.FillPath(pgb, glowPath);
                }
            }

            // 2. Window Controls (Minimize & Close)
            _closeBtnRect = new Rectangle(w - S(42), S(12), S(28), S(28));
            _minBtnRect = new Rectangle(w - S(74), S(12), S(28), S(28));

            if (_hoverClose)
            {
                using (var b = new SolidBrush(Color.FromArgb(160, 190, 50, 50)))
                    g.FillRectangle(b, _closeBtnRect);
            }
            if (_hoverMin)
            {
                using (var b = new SolidBrush(Color.FromArgb(60, 218, 175, 72)))
                    g.FillRectangle(b, _minBtnRect);
            }

            using (var iconPen = new Pen(Color.FromArgb(200, 220, 215, 200), S(1.6f)))
            {
                int cx = _closeBtnRect.X + _closeBtnRect.Width / 2;
                int cy = _closeBtnRect.Y + _closeBtnRect.Height / 2;
                int r = S(5);
                g.DrawLine(iconPen, cx - r, cy - r, cx + r, cy + r);
                g.DrawLine(iconPen, cx + r, cy - r, cx - r, cy + r);

                int mx = _minBtnRect.X + _minBtnRect.Width / 2;
                int my = _minBtnRect.Y + _minBtnRect.Height / 2 + S(2);
                g.DrawLine(iconPen, mx - r, my, mx + r, my);
            }

            // 3. Header Crest & Titles
            DrawCrest(g, S(36), S(30), S(48));

            using (var titleFont = new Font("Segoe UI", 19f, FontStyle.Bold, GraphicsUnit.Point))
            using (var subFont = new Font("Segoe UI", 9.2f, FontStyle.Regular, GraphicsUnit.Point))
            using (var goldBrush = new SolidBrush(Color.FromArgb(244, 204, 104)))
            using (var shadowBrush = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
            using (var subBrush = new SolidBrush(Color.FromArgb(156, 166, 188)))
            {
                g.DrawString("LOKI ONLINE", titleFont, shadowBrush, S(97), S(29));
                g.DrawString("LOKI ONLINE", titleFont, goldBrush, S(96), S(28));
                g.DrawString("RAGNAROK ONLINE IN VR   •   LAUNCHER & UPDATER", subFont, subBrush, S(98), S(64));
            }

            // Version Badge Pill
            using (var badgeFont = new Font("Segoe UI Semibold", 8.8f, FontStyle.Bold, GraphicsUnit.Point))
            {
                SizeF bSize = g.MeasureString(_badgeText, badgeFont);
                int bw = (int)bSize.Width + S(24);
                int bh = S(26);
                var badgeRect = new Rectangle(w - bw - S(36), S(52), bw, bh);
                using (var badgeBg = new SolidBrush(Color.FromArgb(34, 218, 175, 72)))
                using (var badgeBorder = new Pen(Color.FromArgb(140, 218, 175, 72), 1f))
                using (var badgeTextBrush = new SolidBrush(Color.FromArgb(245, 214, 128)))
                {
                    g.FillRectangle(badgeBg, badgeRect);
                    g.DrawRectangle(badgeBorder, badgeRect);
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(_badgeText, badgeFont, badgeTextBrush, badgeRect, sf);
                }
            }

            // Divider line
            using (var divPen = new Pen(Color.FromArgb(65, 212, 175, 55), 1f))
            {
                g.DrawLine(divPen, S(36), S(96), w - S(36), S(96));
            }

            // 4. Main Content Card
            var cardRect = new Rectangle(S(36), S(112), w - S(72), S(214));
            using (var cardBg = new SolidBrush(Color.FromArgb(14, 16, 24)))
            using (var cardBorder = new Pen(Color.FromArgb(48, 56, 78), 1f))
            {
                g.FillRectangle(cardBg, cardRect);
                g.DrawRectangle(cardBorder, cardRect);
            }

            bool hasRoError = _awaitingRoFolder && !string.IsNullOrEmpty(_roValidationError);
            using (var stageFont = new Font("Segoe UI Semibold", 13.5f, FontStyle.Bold, GraphicsUnit.Point))
            using (var detailFont = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point))
            using (var stageBrush = new SolidBrush((_hasError || hasRoError) ? Color.FromArgb(255, 135, 115) : Color.FromArgb(242, 236, 220)))
            using (var detailBrush = new SolidBrush(hasRoError ? Color.FromArgb(245, 180, 165) : Color.FromArgb(170, 178, 198)))
            {
                g.DrawString(_stageTitle, stageFont, stageBrush, cardRect.X + S(24), cardRect.Y + S(20));
                var detailRect = new RectangleF(cardRect.X + S(24), cardRect.Y + S(56), cardRect.Width - S(48), S(56));
                g.DrawString(_detailText, detailFont, detailBrush, detailRect);
            }

            _retryBtnRect = Rectangle.Empty;
            _launchAnywayBtnRect = Rectangle.Empty;
            _errorRollbackBtnRect = Rectangle.Empty;
            _roBrowseBtnRect = Rectangle.Empty;
            _roUseDetectedBtnRect = Rectangle.Empty;
            _roRollbackBtnRect = Rectangle.Empty;

            if (_awaitingRoFolder)
            {
                // Draw current or detected path box
                string shownPath = !string.IsNullOrEmpty(_roDataFolder)
                    ? _roDataFolder
                    : (!string.IsNullOrEmpty(_detectedRoCandidate) ? _detectedRoCandidate : "No Ragnarok Online folder selected yet");
                bool pathValid = !string.IsNullOrEmpty(_roDataFolder) || !string.IsNullOrEmpty(_detectedRoCandidate);

                var pathBox = new Rectangle(cardRect.X + S(24), cardRect.Y + S(112), cardRect.Width - S(48), S(32));
                using (var boxBg = new SolidBrush(Color.FromArgb(9, 11, 17)))
                using (var boxBorder = new Pen(pathValid ? Color.FromArgb(120, 218, 175, 72) : Color.FromArgb(70, 80, 105), 1f))
                using (var pathFont = new Font("Segoe UI", 9.2f, pathValid ? FontStyle.Regular : FontStyle.Italic, GraphicsUnit.Point))
                using (var pathBrush = new SolidBrush(pathValid ? Color.FromArgb(240, 224, 170) : Color.FromArgb(130, 138, 158)))
                {
                    g.FillRectangle(boxBg, pathBox);
                    g.DrawRectangle(boxBorder, pathBox);
                    var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisPath, FormatFlags = StringFormatFlags.NoWrap };
                    var textRect = new RectangleF(pathBox.X + S(12), pathBox.Y, pathBox.Width - S(24), pathBox.Height);
                    g.DrawString(shownPath, pathFont, pathBrush, textRect, sf);
                }

                int btnY = cardRect.Y + S(158);
                if (!string.IsNullOrEmpty(_roDataFolder))
                {
                    _roUseDetectedBtnRect = new Rectangle(cardRect.X + S(24), btnY, S(180), S(36));
                    DrawButton(g, _roUseDetectedBtnRect, "Play Loki Online", true, _hoverRoUseDetected);

                    _roBrowseBtnRect = new Rectangle(_roUseDetectedBtnRect.Right + S(10), btnY, S(165), S(36));
                    DrawButton(g, _roBrowseBtnRect, "Change RO Folder...", false, _hoverRoBrowse);

                    if (_rollbackAvailable)
                    {
                        string rbLabel = _rollbackVersion > 0
                            ? string.Format("Rollback to v{0}", _rollbackVersion)
                            : "Rollback Update";
                        _roRollbackBtnRect = new Rectangle(_roBrowseBtnRect.Right + S(10), btnY, S(165), S(36));
                        DrawButton(g, _roRollbackBtnRect, rbLabel, false, _hoverRoRollback);
                    }
                }
                else if (!string.IsNullOrEmpty(_detectedRoCandidate))
                {
                    _roUseDetectedBtnRect = new Rectangle(cardRect.X + S(24), btnY, S(210), S(36));
                    DrawButton(g, _roUseDetectedBtnRect, "Use Detected Folder", true, _hoverRoUseDetected);

                    _roBrowseBtnRect = new Rectangle(_roUseDetectedBtnRect.Right + S(12), btnY, S(190), S(36));
                    DrawButton(g, _roBrowseBtnRect, "Browse Another Folder...", false, _hoverRoBrowse);
                }
                else
                {
                    _roBrowseBtnRect = new Rectangle(cardRect.X + S(24), btnY, S(250), S(36));
                    DrawButton(g, _roBrowseBtnRect, "Browse Ragnarok Online Folder...", true, _hoverRoBrowse);
                }
            }
            else
            {
                // 5. Progress Bar
                var barRect = new Rectangle(cardRect.X + S(24), cardRect.Y + S(118), cardRect.Width - S(48), S(28));
                using (var trackBg = new SolidBrush(Color.FromArgb(8, 10, 15)))
                using (var trackBorder = new Pen(Color.FromArgb(92, 76, 42), 1f))
                {
                    g.FillRectangle(trackBg, barRect);
                    g.DrawRectangle(trackBorder, barRect);
                }

                if (_indeterminate && !_hasError)
                {
                    int segW = (int)(barRect.Width * 0.32f);
                    int pos = (int)((barRect.Width + segW) * _shimmerPhase) - segW;
                    int left = Math.Max(barRect.X + 2, barRect.X + pos);
                    int right = Math.Min(barRect.Right - 2, barRect.X + pos + segW);
                    if (right > left)
                    {
                        var fillRect = new Rectangle(left, barRect.Y + 2, right - left, barRect.Height - 4);
                        using (var fillBrush = new LinearGradientBrush(fillRect, Color.FromArgb(165, 120, 35), Color.FromArgb(242, 200, 92), LinearGradientMode.Horizontal))
                        {
                            g.FillRectangle(fillBrush, fillRect);
                        }
                    }
                }
                else
                {
                    int fillW = (int)Math.Round((barRect.Width - 4) * Math.Max(0f, Math.Min(1f, _displayProgress)));
                    if (fillW > 2)
                    {
                        var fillRect = new Rectangle(barRect.X + 2, barRect.Y + 2, fillW, barRect.Height - 4);
                        Color c1 = _hasError ? Color.FromArgb(160, 50, 45) : Color.FromArgb(168, 122, 34);
                        Color c2 = _hasError ? Color.FromArgb(220, 80, 70) : Color.FromArgb(245, 204, 96);
                        using (var fillBrush = new LinearGradientBrush(fillRect, c1, c2, LinearGradientMode.Horizontal))
                        {
                            g.FillRectangle(fillBrush, fillRect);
                        }
                        using (var glossBrush = new SolidBrush(Color.FromArgb(48, 255, 255, 255)))
                        {
                            g.FillRectangle(glossBrush, fillRect.X, fillRect.Y, fillRect.Width, Math.Max(2, fillRect.Height / 3));
                        }
                    }

                    if (!_hasError)
                    {
                        string pctStr = string.Format("{0}%", (int)Math.Round(_displayProgress * 100));
                        using (var pctFont = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point))
                        using (var pctShadow = new SolidBrush(Color.FromArgb(190, 0, 0, 0)))
                        using (var pctBrush = new SolidBrush(Color.FromArgb(250, 245, 230)))
                        {
                            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                            var shadowRect = new Rectangle(barRect.X + 1, barRect.Y + 1, barRect.Width, barRect.Height);
                            g.DrawString(pctStr, pctFont, pctShadow, shadowRect, sf);
                            g.DrawString(pctStr, pctFont, pctBrush, barRect, sf);
                        }
                    }
                }

                // 6. Error Action Buttons (Retry / Launch Anyway / Rollback)
                if (_hasError)
                {
                    _retryBtnRect = new Rectangle(cardRect.X + S(24), cardRect.Y + S(160), S(140), S(34));
                    DrawButton(g, _retryBtnRect, "Retry Update", true, _hoverRetry);

                    int nextX = _retryBtnRect.Right + S(10);
                    if (_canLaunchFallback)
                    {
                        _launchAnywayBtnRect = new Rectangle(nextX, cardRect.Y + S(160), S(150), S(34));
                        DrawButton(g, _launchAnywayBtnRect, "Launch Game", false, _hoverLaunchAnyway);
                        nextX = _launchAnywayBtnRect.Right + S(10);
                    }

                    if (_rollbackAvailable)
                    {
                        string rbLabel = _rollbackVersion > 0
                            ? string.Format("Rollback to v{0}", _rollbackVersion)
                            : "Rollback Update";
                        _errorRollbackBtnRect = new Rectangle(nextX, cardRect.Y + S(160), S(165), S(34));
                        DrawButton(g, _errorRollbackBtnRect, rbLabel, false, _hoverErrorRollback);
                    }
                }
            }

            // 7. Footer: Ragnarok Online Path + Rollback + Auto-Update Toggle + "RO Folder..." button
            int footY = h - S(48);
            _footerChangeRoBtnRect = new Rectangle(w - S(148), footY - S(2), S(112), S(26));
            DrawButton(g, _footerChangeRoBtnRect, "RO Folder...", false, _hoverFooterChangeRo);

            _footerAutoUpdateBtnRect = new Rectangle(_footerChangeRoBtnRect.Left - S(144), footY - S(2), S(136), S(26));
            DrawButton(g, _footerAutoUpdateBtnRect, _autoUpdateEnabled ? "Auto-Update: ON" : "Auto-Update: OFF", false, _hoverFooterAutoUpdate);

            int leftBtnEdge = _footerAutoUpdateBtnRect.Left;
            if (_rollbackAvailable)
            {
                string footRbLabel = _rollbackVersion > 0
                    ? string.Format("Rollback (v{0})", _rollbackVersion)
                    : "Rollback";
                _footerRollbackBtnRect = new Rectangle(_footerAutoUpdateBtnRect.Left - S(132), footY - S(2), S(124), S(26));
                DrawButton(g, _footerRollbackBtnRect, footRbLabel, false, _hoverFooterRollback);
                leftBtnEdge = _footerRollbackBtnRect.Left;
            }
            else
            {
                _footerRollbackBtnRect = Rectangle.Empty;
            }

            string roStatus = !string.IsNullOrEmpty(_roDataFolder)
                ? string.Format("Ragnarok Online: {0}", _roDataFolder)
                : "Ragnarok Online folder: Not configured yet";
            using (var footFont = new Font("Segoe UI", 8.8f, FontStyle.Regular, GraphicsUnit.Point))
            using (var footBrush = new SolidBrush(!string.IsNullOrEmpty(_roDataFolder) ? Color.FromArgb(150, 162, 185) : Color.FromArgb(218, 175, 72)))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisPath, FormatFlags = StringFormatFlags.NoWrap };
                var footRect = new RectangleF(S(36), footY - S(2), Math.Max(S(60), leftBtnEdge - S(46)), S(26));
                g.DrawString(roStatus, footFont, footBrush, footRect, sf);
            }

            // 8. Outer Burnished Gold Frame
            using (var outerPen = new Pen(Color.FromArgb(185, 148, 64), S(2f)))
            using (var innerPen = new Pen(Color.FromArgb(65, 218, 175, 72), 1f))
            {
                g.DrawRectangle(outerPen, 1, 1, w - 2, h - 2);
                g.DrawRectangle(innerPen, S(5), S(5), w - S(10) - 1, h - S(10) - 1);
            }
        }

        private void DrawCrest(Graphics g, int x, int y, int size)
        {
            int cx = x + size / 2;
            int cy = y + size / 2;

            using (var ringPen = new Pen(Color.FromArgb(140, 218, 175, 72), S(1.5f)))
            using (var darkFill = new SolidBrush(Color.FromArgb(22, 26, 38)))
            {
                g.FillEllipse(darkFill, x, y, size, size);
                g.DrawEllipse(ringPen, x, y, size, size);
            }

            PointF[] outerDiamond =
            {
                new PointF(cx, y + S(5)),
                new PointF(x + size - S(5), cy),
                new PointF(cx, y + size - S(5)),
                new PointF(x + S(5), cy),
            };
            using (var goldGrad = new LinearGradientBrush(new Rectangle(x, y, size, size), Color.FromArgb(252, 220, 128), Color.FromArgb(176, 124, 34), LinearGradientMode.Vertical))
            {
                g.FillPolygon(goldGrad, outerDiamond);
            }

            PointF[] innerDiamond =
            {
                new PointF(cx, y + S(14)),
                new PointF(x + size - S(14), cy),
                new PointF(cx, y + size - S(14)),
                new PointF(x + S(14), cy),
            };
            using (var coreBrush = new SolidBrush(Color.FromArgb(18, 22, 32)))
            {
                g.FillPolygon(coreBrush, innerDiamond);
            }
        }

        private void DrawButton(Graphics g, Rectangle rect, string label, bool primary, bool hover)
        {
            Color bg = primary
                ? (hover ? Color.FromArgb(224, 178, 72) : Color.FromArgb(188, 142, 46))
                : (hover ? Color.FromArgb(46, 54, 74) : Color.FromArgb(30, 36, 50));
            Color fg = primary ? Color.FromArgb(16, 18, 24) : Color.FromArgb(225, 220, 208);
            Color border = primary ? Color.FromArgb(250, 218, 128) : Color.FromArgb(110, 120, 145);

            using (var b = new SolidBrush(bg))
            using (var p = new Pen(border, 1f))
            using (var tb = new SolidBrush(fg))
            using (var f = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold, GraphicsUnit.Point))
            {
                g.FillRectangle(b, rect);
                g.DrawRectangle(p, rect);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(label, f, tb, rect, sf);
            }
        }

        private bool IsInteractivePoint(Point p)
        {
            if (_closeBtnRect.Contains(p) || _minBtnRect.Contains(p) || _footerChangeRoBtnRect.Contains(p) || _footerAutoUpdateBtnRect.Contains(p))
                return true;
            if (_rollbackAvailable && _footerRollbackBtnRect.Contains(p))
                return true;
            if (_hasError && (_retryBtnRect.Contains(p) || (_canLaunchFallback && _launchAnywayBtnRect.Contains(p)) || (_rollbackAvailable && _errorRollbackBtnRect.Contains(p))))
                return true;
            if (_awaitingRoFolder && (_roBrowseBtnRect.Contains(p) || _roUseDetectedBtnRect.Contains(p) || (_rollbackAvailable && _roRollbackBtnRect.Contains(p))))
                return true;
            return false;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            if (IsInteractivePoint(e.Location))
                return;

            _dragging = true;
            _dragStart = e.Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                Location = new Point(Location.X + e.X - _dragStart.X, Location.Y + e.Y - _dragStart.Y);
                return;
            }

            bool hc = _closeBtnRect.Contains(e.Location);
            bool hm = _minBtnRect.Contains(e.Location);
            bool hr = _hasError && _retryBtnRect.Contains(e.Location);
            bool hl = _hasError && _canLaunchFallback && _launchAnywayBtnRect.Contains(e.Location);
            bool her = _hasError && _rollbackAvailable && _errorRollbackBtnRect.Contains(e.Location);
            bool hrb = _awaitingRoFolder && _roBrowseBtnRect.Contains(e.Location);
            bool hru = _awaitingRoFolder && _roUseDetectedBtnRect.Contains(e.Location);
            bool hrr = _awaitingRoFolder && _rollbackAvailable && _roRollbackBtnRect.Contains(e.Location);
            bool hfc = _footerChangeRoBtnRect.Contains(e.Location);
            bool hfa = _footerAutoUpdateBtnRect.Contains(e.Location);
            bool hfr = _rollbackAvailable && _footerRollbackBtnRect.Contains(e.Location);

            if (hc != _hoverClose || hm != _hoverMin || hr != _hoverRetry || hl != _hoverLaunchAnyway || her != _hoverErrorRollback ||
                hrb != _hoverRoBrowse || hru != _hoverRoUseDetected || hrr != _hoverRoRollback ||
                hfc != _hoverFooterChangeRo || hfa != _hoverFooterAutoUpdate || hfr != _hoverFooterRollback)
            {
                _hoverClose = hc;
                _hoverMin = hm;
                _hoverRetry = hr;
                _hoverLaunchAnyway = hl;
                _hoverErrorRollback = her;
                _hoverRoBrowse = hrb;
                _hoverRoUseDetected = hru;
                _hoverRoRollback = hrr;
                _hoverFooterChangeRo = hfc;
                _hoverFooterAutoUpdate = hfa;
                _hoverFooterRollback = hfr;
                Cursor = (hc || hm || hr || hl || her || hrb || hru || hrr || hfc || hfa || hfr) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                _dragging = false;
                if (_closeBtnRect.Contains(e.Location))
                {
                    Close();
                }
                else if (_minBtnRect.Contains(e.Location))
                {
                    WindowState = FormWindowState.Minimized;
                }
                else if (_footerAutoUpdateBtnRect.Contains(e.Location))
                {
                    ToggleAutoUpdatePreference();
                }
                else if (_rollbackAvailable && _footerRollbackBtnRect.Contains(e.Location))
                {
                    PromptAndRollback();
                }
                else if (_footerChangeRoBtnRect.Contains(e.Location))
                {
                    ShowRoFolderPrompt("");
                    BrowseAndSelectRoFolder();
                }
                else if (_awaitingRoFolder && _roUseDetectedBtnRect.Contains(e.Location))
                {
                    if (!string.IsNullOrEmpty(_roDataFolder))
                    {
                        string valid = ResolveValidRoDataRoot(_roDataFolder);
                        if (!string.IsNullOrEmpty(valid))
                        {
                            SaveConfiguredRoFolder(valid);
                            _awaitingRoFolder = false;
                            LaunchGameAndExit();
                        }
                        else
                        {
                            _roDataFolder = "";
                            ShowRoFolderPrompt("The configured Ragnarok Online folder is no longer valid. Please select your Ragnarok Online folder.");
                        }
                    }
                    else
                    {
                        string valid = ResolveValidRoDataRoot(_detectedRoCandidate);
                        if (!string.IsNullOrEmpty(valid))
                        {
                            SaveConfiguredRoFolder(valid);
                            ShowRoFolderPrompt("");
                        }
                        else
                        {
                            BrowseAndSelectRoFolder();
                        }
                    }
                }
                else if (_awaitingRoFolder && _roBrowseBtnRect.Contains(e.Location))
                {
                    BrowseAndSelectRoFolder();
                }
                else if (_awaitingRoFolder && _rollbackAvailable && _roRollbackBtnRect.Contains(e.Location))
                {
                    PromptAndRollback();
                }
                else if (_hasError && _retryBtnRect.Contains(e.Location))
                {
                    StartWorkflow();
                }
                else if (_hasError && _canLaunchFallback && _launchAnywayBtnRect.Contains(e.Location))
                {
                    ProceedToLaunchOrPromptRo(_badgeText);
                }
                else if (_hasError && _rollbackAvailable && _errorRollbackBtnRect.Contains(e.Location))
                {
                    PromptAndRollback();
                }
            }
        }
    }

    internal static class VistaFolderPicker
    {
        private const uint FOS_PICKFOLDERS = 0x00000020;
        private const uint FOS_FORCEFILESYSTEM = 0x00000040;
        private const uint FOS_PATHMUSTEXIST = 0x00000800;
        private const uint SIGDN_FILESYSPATH = 0x80058000;
        private const int ERROR_CANCELLED = unchecked((int)0x800704C7);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [In, MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [Out, MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRCW { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show([In] IntPtr parent);
            void SetFileTypes([In] uint cFileTypes, [In] IntPtr rgFilterSpec);
            void SetFileTypeIndex([In] uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise([In, MarshalAs(UnmanagedType.Interface)] IntPtr pfde, out uint pdwCookie);
            void Unadvise([In] uint dwCookie);
            void SetOptions([In] uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi);
            void SetFolder([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi);
            void GetFolder([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
            void GetCurrentSelection([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
            void SetFileName([In, MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([In, MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([In, MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([In, MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler([In, MarshalAs(UnmanagedType.Interface)] IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, out IntPtr ppv);
            void GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
            void GetDisplayName([In] uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes([In] uint sfgaoMask, out uint psfgaoAttribs);
            void Compare([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi, [In] uint hint, out int piOrder);
        }

        public static string Show(IntPtr ownerHwnd, string title, string initialDir)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialogRCW();
            try
            {
                uint opts;
                dialog.GetOptions(out opts);
                dialog.SetOptions(opts | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
                if (!string.IsNullOrEmpty(title))
                    dialog.SetTitle(title);
                dialog.SetOkButtonLabel("Select RO Folder");

                if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir))
                {
                    try
                    {
                        IShellItem folderItem;
                        SHCreateItemFromParsingName(initialDir, IntPtr.Zero, typeof(IShellItem).GUID, out folderItem);
                        if (folderItem != null)
                            dialog.SetFolder(folderItem);
                    }
                    catch { }
                }

                int hr = dialog.Show(ownerHwnd);
                if (hr == ERROR_CANCELLED)
                    return "";
                if (hr != 0)
                    Marshal.ThrowExceptionForHR(hr);

                IShellItem result;
                dialog.GetResult(out result);
                if (result != null)
                {
                    string path;
                    result.GetDisplayName(SIGDN_FILESYSPATH, out path);
                    return path ?? "";
                }
                return "";
            }
            finally
            {
                Marshal.FinalReleaseComObject(dialog);
            }
        }
    }
}
