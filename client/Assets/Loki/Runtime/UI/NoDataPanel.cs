using System;
using System.Collections.Generic;
using System.IO;
using Loki.World;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// First run without the Ragnarok Online game data (loki.json "clientRoot" missing, no GRF, or the start map unreadable):
    /// LokiBootstrap builds only the VR rig and this panel, which needs nothing from the game data (OS font, built-in shaders).
    /// It says which folder was looked at and why, offers every RO install it finds in the usual places, and a folder browser
    /// (drives → folders, laser only) for anywhere else. "Use" stores the choice in the per-user file (<see cref="UserSettings"/>,
    /// read by LokiConfig.Load when the configured folder has no data) and starts again. "Try again" and "Quit".
    /// Client wave 4 (w4account): the same panel opens from the login screen ("Game data…", <see cref="ShowChange"/>) to pick
    /// another folder while one works. The choice is saved and applies from the next start of Loki: the open GRF, the tables
    /// made from it (Data/FirstRunData, checked once a process) and about eighty static caches built from them live for the
    /// whole run, so switching under a running game could mix two installations. "Use loki.json's folder" forgets the pick.
    /// </summary>
    public sealed class NoDataPanel : MonoBehaviour
    {
        private const float W = 1200, H = 860;
        private Camera _head; private Text _status; private bool _placedTracked;
        private LokiConfig _cfg;
        private RectTransform _main, _browse;
        public string Reason { get; private set; }
        public readonly List<string> Candidates = new List<string>();
        public static NoDataPanel Current { get; private set; }
        /// <summary>Client wave 4: opened from the login screen to change a working folder (the choice applies from the next start).</summary>
        public bool ChangeMode { get; private set; }
        private Action _back;
        /// <summary>What the last Use / Forget did in change mode ("" before one).</summary>
        public string Saved { get; private set; } = "";

        public static NoDataPanel Show(Transform parent, Camera head, LokiConfig cfg, string reason)
        {
            var rt = UiKit.Panel("NoDataPanel", parent, new Vector2(W, H), 0.0007f);
            var p = rt.gameObject.AddComponent<NoDataPanel>();
            p._head = head; p.Reason = reason; p._cfg = cfg; Current = p;
            p.Build(rt, cfg);
            p.PlaceInFront();
            return p;
        }

        /// <summary>Client wave 4: "Game data…" on the login screen. <paramref name="back"/> runs when the player leaves the panel.</summary>
        public static NoDataPanel ShowChange(Transform parent, Camera head, LokiConfig cfg, Action back)
        {
            var rt = UiKit.Panel("NoDataPanel", parent, new Vector2(W, H), 0.0007f);
            var p = rt.gameObject.AddComponent<NoDataPanel>();
            p._head = head; p.Reason = ""; p._cfg = cfg; p.ChangeMode = true; p._back = back; Current = p;
            p.BuildChange(rt, cfg);
            p.PlaceInFront();
            return p;
        }

        private UiButton _quitNow;
        private void BuildChange(RectTransform rt, LokiConfig cfg)
        {
            float top = H / 2;
            _main = UiKit.Box(rt, "main", Vector2.zero, new Vector2(W, H), new Color(0, 0, 0, 0));
            _browse = UiKit.Box(rt, "browse", Vector2.zero, new Vector2(W, H), new Color(0, 0, 0, 0));
            UiKit.Label(_main, Lang.T("Ragnarok Online game data"), new Vector2(0, top - 50), new Vector2(W - 60, 60), 38, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.45f));
            string from = cfg.ClientRootSource == "loki.json" ? Lang.T("Loki's configuration, loki.json") : cfg.ClientRootSource == "your choice" ? Lang.T("your choice") : cfg.ClientRootSource == "your earlier choice" ? Lang.T("your earlier choice") : cfg.ClientRootSource; // w4lang: LokiConfig's source names stay English (World/RoAssets.cs)
            UiKit.Label(_main,
                Lang.T("Loki draws the world with the files of your own Ragnarok Online installation; it never copies them.") + "\n\n" +
                Lang.F("In use:  <color=#ffd060>{0}</color>  ({1})", Esc(cfg.clientRoot), Esc(from)) + "\n\n" +
                Lang.T("Pick another installation found below, or <b>Browse…</b> to it. Loki uses the new folder from its next start (the game files stay open while it runs)."),
                new Vector2(0, top - 240), new Vector2(W - 80, 260), 24, TextAnchor.UpperLeft);
            FindCandidates(cfg);
            string current = (cfg.clientRoot ?? "").Replace('/', '\\').TrimEnd('\\');
            Candidates.RemoveAll(c => string.Equals(c.Replace('/', '\\').TrimEnd('\\'), current, StringComparison.OrdinalIgnoreCase));
            float y = top - 420;
            if (Candidates.Count == 0) UiKit.Label(_main, Lang.T("No other installation was found in the usual folders."), new Vector2(0, y), new Vector2(W - 80, 50), 24, TextAnchor.MiddleCenter, UiKit.Dim);
            for (int i = 0; i < Candidates.Count && i < 3; i++)
            {
                string c = Candidates[i];
                UiKit.Button(_main, Lang.F("Use {0}", c), new Vector2(0, y - i * 70), new Vector2(W - 160, 60), () => Use(c), 24);
            }
            _status = UiKit.Label(rt, "", new Vector2(0, -top + 150), new Vector2(W - 80, 60), 22, TextAnchor.MiddleCenter, new Color(1f, 0.8f, 0.5f));
            UiKit.Button(_main, Lang.T("Browse…"), new Vector2(-420, -top + 60), new Vector2(240, 66), () => OpenBrowser(null), 26);
            _forget = UiKit.Button(_main, Lang.T("Use loki.json's folder"), new Vector2(-120, -top + 60), new Vector2(330, 66), ForgetChoice, 22);
            _forget.gameObject.SetActive(!string.IsNullOrEmpty(UserSettings.Current.dataFolder));
            _quitNow = UiKit.Button(_main, Lang.T("Quit Loki now"), new Vector2(175, -top + 60), new Vector2(230, 66), () => { Debug.Log("[NoData] quit to use the new folder"); Application.Quit(); }, 22);
            _quitNow.gameObject.SetActive(false);
            UiKit.Button(_main, Lang.T("Back"), new Vector2(430, -top + 60), new Vector2(220, 66), Close, 26);
            BuildBrowser(_browse);
            _browse.gameObject.SetActive(false);
        }
        private UiButton _forget;

        /// <summary>Change mode: forget the folder picked in game (LokiConfig.Load then uses loki.json's clientRoot).</summary>
        public void ForgetChoice()
        {
            UserSettings.SetDataFolder("");
            bool saved = UserSettings.Save();
            Saved = saved ? "(loki.json's folder)" : "not saved: " + UserSettings.Error;
            Debug.Log($"[NoData] forgot the picked folder (saved {saved})");
            SetStatus(saved ? Lang.T("Loki uses loki.json's folder from its next start.") : Lang.F("Couldn't save: {0}", UserSettings.Error));
            if (_quitNow != null) _quitNow.gameObject.SetActive(saved);
            if (_forget != null && saved) _forget.gameObject.SetActive(false);
        }

        /// <summary>Change mode: back to the login screen.</summary>
        public void Close()
        {
            Debug.Log("[NoData] back to the login screen");
            var back = _back; Destroy(gameObject); back?.Invoke();
        }

        private void Build(RectTransform rt, LokiConfig cfg)
        {
            float top = H / 2;
            _main = UiKit.Box(rt, "main", Vector2.zero, new Vector2(W, H), new Color(0, 0, 0, 0));
            _browse = UiKit.Box(rt, "browse", Vector2.zero, new Vector2(W, H), new Color(0, 0, 0, 0));
            bool badConfig = cfg.LoadError != null;
            UiKit.Label(_main, badConfig ? Lang.T("Loki's configuration file could not be read") : Lang.T("Ragnarok Online game data not found"), new Vector2(0, top - 50), new Vector2(W - 60, 60), 38, TextAnchor.MiddleCenter, new Color(1f, 0.6f, 0.45f));
            string cfgFile = string.IsNullOrEmpty(cfg.LoadedFrom) ? "Loki_Data\\StreamingAssets\\loki.json" : cfg.LoadedFrom;
            string grfs = cfg.grfs != null && cfg.grfs.Length > 0 ? string.Join(", ", cfg.grfs) : "data.grf";
            if (badConfig)
                UiKit.Label(_main,
                    Lang.F("File:  <color=#ffd060>{0}</color>", Esc(cfgFile)) + "\n" + Lang.F("Problem:  <color=#ff9a7a>{0}</color>", Esc(cfg.LoadError)) + "\n\n" +
                    Lang.T("Fix the file in a text editor, then Try again. A Windows path needs doubled backslashes (\"D:\\\\Ragnarok Online\") or forward slashes (\"D:/Ragnarok Online\")."),
                    new Vector2(0, top - 250), new Vector2(W - 80, 300), 24, TextAnchor.UpperLeft);
            else
            UiKit.Label(_main,
                Lang.T("Loki draws the world with the files of your own Ragnarok Online installation; it never copies them.") + "\n\n" +
                Lang.F("Folder looked at:  <color=#ffd060>{0}</color>", Esc(cfg.ConfiguredClientRoot)) + "\n" +
                Lang.F("Problem:  <color=#ff9a7a>{0}</color>", Esc(Reason)) + "\n\n" +
                Lang.F("The folder must hold {0} (or a data\\ folder). Pick an installation found below, or <b>Browse…</b> to it (the Latin American installer puts the game in a \"Ragnarok\" folder inside the one you chose).", Esc(grfs)),
                new Vector2(0, top - 250), new Vector2(W - 80, 300), 24, TextAnchor.UpperLeft);

            if (!badConfig) FindCandidates(cfg); // a picked folder would not help while the file itself cannot be read
            float y = top - 440;
            if (Candidates.Count == 0 && !badConfig) UiKit.Label(_main, Lang.T("No Ragnarok Online installation was found in the usual folders."), new Vector2(0, y), new Vector2(W - 80, 50), 24, TextAnchor.MiddleCenter, UiKit.Dim);
            for (int i = 0; i < Candidates.Count && i < 3; i++)
            {
                string c = Candidates[i];
                UiKit.Button(_main, Lang.F("Use {0}", c), new Vector2(0, y - i * 70), new Vector2(W - 160, 60), () => Use(c), 24);
            }
            _status = UiKit.Label(rt, "", new Vector2(0, -top + 130), new Vector2(W - 80, 44), 22, TextAnchor.MiddleCenter, new Color(1f, 0.8f, 0.5f));
            if (!badConfig) UiKit.Button(_main, Lang.T("Browse…"), new Vector2(-380, -top + 60), new Vector2(300, 66), () => OpenBrowser(null), 28);
            UiKit.Button(_main, Lang.T("Try again"), new Vector2(badConfig ? -200 : 0, -top + 60), new Vector2(300, 66), TryAgain, 28);
            UiKit.Button(_main, Lang.T("Quit"), new Vector2(badConfig ? 200 : 380, -top + 60), new Vector2(300, 66), () => { Debug.Log("[NoData] quit"); Application.Quit(); }, 28);
            BuildBrowser(_browse);
            _browse.gameObject.SetActive(false);
        }

        private static string Esc(string s) => (s ?? "").Replace("<", "<\u200B");

        /// <summary>RO installs in the usual places: every fixed drive's root, \Gravity, \Games, Program Files (x86)\Gravity;
        /// and the "Ragnarok" folder the LATAM installer creates inside the folder it was given (seen 2026-09-30:
        /// C:\Ragnarok Online).</summary>
        private void FindCandidates(LokiConfig cfg)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tries = new List<string>();
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    string r = d.RootDirectory.FullName;
                    tries.Add(Path.Combine(r, "Ragnarok Online"));
                    tries.Add(Path.Combine(r, "Ragnarok Online", "Ragnarok"));
                    tries.Add(Path.Combine(r, "Ragnarok"));
                    tries.Add(Path.Combine(r, "Gravity", "Ragnarok Online"));
                    tries.Add(Path.Combine(r, "Games", "Ragnarok Online"));
                    tries.Add(Path.Combine(r, "Program Files (x86)", "Gravity", "Ragnarok Online"));
                    tries.Add(Path.Combine(r, "Program Files", "Gravity", "Ragnarok Online"));
                }
            }
            catch (Exception e) { Debug.LogWarning("[NoData] drive scan: " + e.Message); }
            foreach (var t in tries)
            {
                try { if (seen.Add(t) && !string.Equals(t, cfg.ConfiguredClientRoot, StringComparison.OrdinalIgnoreCase) && LokiConfig.HasGameData(t, cfg.grfs)) Candidates.Add(t); }
                catch { }
            }
            Debug.Log($"[NoData] {Candidates.Count} installation(s) found: {string.Join(", ", Candidates)}");
        }

        public void Use(string folder)
        {
            UserSettings.SetDataFolder(folder);
            bool saved = UserSettings.Save();
            Debug.Log($"[NoData] using {folder} (saved to {UserSettings.FilePath}: {saved})");
            if (!saved) { SetStatus(Lang.F("Couldn't save the choice: {0}", UserSettings.Error)); return; }
            if (ChangeMode)
            {
                // client wave 4: a working folder is changed from the next start (the class summary says why)
                Saved = folder;
                _browse.gameObject.SetActive(false); _main.gameObject.SetActive(true);
                SetStatus(Lang.F("Saved: Loki uses {0} from its next start.", folder));
                if (_quitNow != null) _quitNow.gameObject.SetActive(true);
                if (_forget != null) _forget.gameObject.SetActive(true);
                return;
            }
            SetStatus(Lang.F("Starting again with {0}…", folder));
            LokiBootstrap.Reload();
        }

        private string _lastValidBrowse;
        private bool _placedLow;

        public void TryAgain()
        {
            bool skipAuto = false;
            if (_cfg != null && LokiConfig.ResolveGameDataRoot(_cfg.clientRoot, _cfg.grfs) == null && !skipAuto)
            {
                if (!string.IsNullOrEmpty(_lastValidBrowse)) { Use(_lastValidBrowse); return; }
                if (Candidates.Count > 0) { Use(Candidates[0]); return; }
            }
            SetStatus(Lang.T("Looking again…"));
            Debug.Log("[NoData] try again");
            LokiBootstrap.Reload();
        }


        public void SetStatus(string s) { if (_status != null) _status.text = s ?? ""; }

        // ---------------------------------------------------------------- folder browser (drives → folders)
        private const int Rows = 7;
        private Text _path, _verdict; private UiButton _up, _use, _useChild, _prev, _next;
        private readonly List<UiButton> _rowButtons = new List<UiButton>();
        private readonly List<(string label, string path, bool hasData)> _entries = new List<(string, string, bool)>();
        private int _page;
        /// <summary>The folder shown (null: the list of drives).</summary>
        public string BrowsePath { get; private set; }
        public bool Browsing => _browse != null && _browse.gameObject.activeSelf;
        /// <summary>A "Ragnarok" folder inside the one shown that holds the game data (the LATAM installer's layout), or null.</summary>
        public string BrowseChildWithData { get; private set; }
        public bool BrowseHasData { get; private set; }
        public IReadOnlyList<(string label, string path, bool hasData)> BrowseEntries => _entries;

        private void BuildBrowser(RectTransform p)
        {
            float top = H / 2;
            UiKit.Label(p, Lang.T("Where is Ragnarok Online?"), new Vector2(0, top - 50), new Vector2(W - 60, 56), 36, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.45f));
            _path = UiKit.Label(p, "", new Vector2(0, top - 110), new Vector2(W - 80, 44), 24, TextAnchor.MiddleLeft, new Color(1f, 0.82f, 0.4f));
            _up = UiKit.Button(p, Lang.T("Up"), new Vector2(-W / 2 + 110, top - 170), new Vector2(160, 54), () => { var parent = ParentOf(BrowsePath); OpenBrowser(parent); }, 24);
            _prev = UiKit.Button(p, Lang.T("< Prev"), new Vector2(W / 2 - 290, top - 170), new Vector2(160, 54), () => { _page = Mathf.Max(0, _page - 1); RefreshBrowser(); }, 22);
            _next = UiKit.Button(p, Lang.T("Next >"), new Vector2(W / 2 - 110, top - 170), new Vector2(160, 54), () => { _page++; RefreshBrowser(); }, 22);
            for (int i = 0; i < Rows; i++)
            {
                int row = i;
                _rowButtons.Add(UiKit.Button(p, "", new Vector2(0, top - 240 - i * 62), new Vector2(W - 120, 56), () => OpenRow(row), 23));
            }
            _verdict = UiKit.Label(p, "", new Vector2(0, -top + 200), new Vector2(W - 80, 60), 22, TextAnchor.MiddleCenter);
            _use = UiKit.Button(p, Lang.T("Use this folder"), new Vector2(-300, -top + 60), new Vector2(360, 66), () => { if (BrowseHasData) Use(BrowsePath); }, 26);
            _useChild = UiKit.Button(p, Lang.T("Use …\\Ragnarok"), new Vector2(-300, -top + 60), new Vector2(360, 66), () => { if (BrowseChildWithData != null) Use(BrowseChildWithData); }, 24);
            UiKit.Button(p, Lang.T("Back"), new Vector2(300, -top + 60), new Vector2(300, 66), () => { _browse.gameObject.SetActive(false); _main.gameObject.SetActive(true); }, 26);
        }

        private static string ParentOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try { var d = Directory.GetParent(path.TrimEnd('\\', '/')); return d?.FullName; } catch { return null; }
        }

        /// <summary>Shows a folder (null: the drives) in the browser.</summary>
        public void OpenBrowser(string path)
        {
            _main.gameObject.SetActive(false); _browse.gameObject.SetActive(true);
            BrowsePath = path; _page = 0; _entries.Clear(); BrowseChildWithData = null; BrowseHasData = false;
            string problem = null;
            try
            {
                if (path == null)
                {
                    foreach (var d in DriveInfo.GetDrives())
                    {
                        if (!d.IsReady || (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable)) continue;
                        string label = d.RootDirectory.FullName + (string.IsNullOrEmpty(d.VolumeLabel) ? "" : "  (" + d.VolumeLabel + ")");
                        _entries.Add((label, d.RootDirectory.FullName, false));
                    }
                }
                else
                {
                    var dirs = new List<string>(Directory.GetDirectories(path));
                    dirs.Sort(StringComparer.OrdinalIgnoreCase);
                    foreach (var d in dirs)
                    {
                        string name = Path.GetFileName(d);
                        if (name.StartsWith("$") || name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)) continue;
                        _entries.Add((name, d, false));
                    }
                    BrowseHasData = LokiConfig.HasGameData(path, _cfg.grfs);
                    string child = Path.Combine(path, "Ragnarok");
                    if (!BrowseHasData && LokiConfig.HasGameData(child, _cfg.grfs)) BrowseChildWithData = child;
                    if (BrowseHasData) _lastValidBrowse = path;
                    else if (BrowseChildWithData != null) _lastValidBrowse = BrowseChildWithData;
                }
            }
            catch (Exception e) { problem = e.Message; }
            Debug.Log($"[NoData] browse '{path ?? "(drives)"}': {_entries.Count} entries, game data here {BrowseHasData}, in Ragnarok\\ {BrowseChildWithData != null}{(problem != null ? ", problem: " + problem : "")}");
            string grfs = _cfg.grfs != null && _cfg.grfs.Length > 0 ? string.Join(" / ", _cfg.grfs) : "data.grf";
            _verdict.text = problem != null ? Lang.F("<color=#ff9a7a>This folder can't be read: {0}</color>", Esc(problem))
                          : path == null ? Lang.T("Choose the drive Ragnarok Online is installed on.")
                          : BrowseHasData ? Lang.F("<color=#a8e6a3>The game data is here ({0}).</color>", Esc(grfs))
                          : BrowseChildWithData != null ? Lang.T("<color=#a8e6a3>The game data is in the \"Ragnarok\" folder inside this one.</color>")
                          : Lang.F("<color=#9aa3b5>No {0} here: open the folder you installed Ragnarok Online in.</color>", Esc(grfs));
            RefreshBrowser();
        }

        private void RefreshBrowser()
        {
            _path.text = BrowsePath == null ? Lang.T("This PC") : Esc(BrowsePath);
            int pages = Mathf.Max(1, (_entries.Count + Rows - 1) / Rows);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            _up.gameObject.SetActive(BrowsePath != null);
            _prev.gameObject.SetActive(pages > 1); _next.gameObject.SetActive(pages > 1);
            _prev.Interactable = _page > 0; _next.Interactable = _page < pages - 1;
            for (int i = 0; i < Rows; i++)
            {
                int k = _page * Rows + i; bool show = k < _entries.Count;
                _rowButtons[i].gameObject.SetActive(show);
                if (!show) continue;
                var e = _entries[k];
                bool data = BrowsePath != null && SafeHasData(e.path);
                _entries[k] = (e.label, e.path, data);
                _rowButtons[i].SetLabel(Esc(e.label) + (data ? "   " + Lang.T("<color=#a8e6a3>(game data)</color>") : ""));
            }
            _use.gameObject.SetActive(BrowseHasData);
            _useChild.gameObject.SetActive(!BrowseHasData && BrowseChildWithData != null);
        }

        private bool SafeHasData(string path) { try { return LokiConfig.HasGameData(path, _cfg.grfs); } catch { return false; } }

        private void OpenRow(int row) { int k = _page * Rows + row; if (k < _entries.Count) OpenBrowser(_entries[k].path); }


        public void PlaceInFront()
        {
            if (_head == null) return;
            var f = _head.transform.forward; f.y = 0; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
            float rigBaseY = _head.transform.parent != null ? _head.transform.parent.position.y : 0f;
            bool low = _head.transform.localPosition.y < 0.5f;
            _placedLow = low;
            var eyePos = _head.transform.position;
            if (low) eyePos.y = Mathf.Max(eyePos.y, rigBaseY + 1.35f);
            transform.position = eyePos + f * 0.95f + Vector3.up * 0.02f;
            UiKit.FaceHead(transform, _head);
        }

        private void LateUpdate()
        {
            // placed before the headset reports a pose (or while sitting low on the floor/desk): place it again once the head is worn
            if (_head == null) return;
            if (!_placedTracked && _head.transform.localPosition.sqrMagnitude > 0.01f) { _placedTracked = true; PlaceInFront(); }
            else if (_placedLow && _head.transform.localPosition.y > 0.85f) { _placedLow = false; _placedTracked = true; PlaceInFront(); }
        }

        private void OnDestroy() { if (Current == this) Current = null; }
    }
}
