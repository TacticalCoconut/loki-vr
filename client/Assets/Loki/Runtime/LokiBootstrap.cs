using Loki.IO;
using Loki.World;
using UnityEngine;

namespace Loki
{
    /// <summary>Scene entry point: opens the client data, loads the start map into a diorama root.</summary>
    public sealed class LokiBootstrap : MonoBehaviour
    {
        public static LokiBootstrap Instance { get; private set; }
        public LokiConfig Config { get; set; }
        public RoAssets Assets { get; private set; }
        public MapLoader Map { get; private set; }

        [Tooltip("Diorama scale: Loki units (1 GAT cell) → metres. 0.05 makes a 300-cell map 15 m across.")]
        public float DioramaScale = 0.05f;
        public bool LoadStartMapOnAwake = true;

        public VR.DioramaRig Rig { get; private set; }
        public float RigTableScale = 0.02f;

        /// <summary>Why the game data could not be used (null when it could): the missing-data panel shows it.</summary>
        public string DataProblem { get; private set; }

        private void Awake()
        {
            Instance = this;
            VR.Comfort.ClearListeners(); // listeners of a previous scene (character select / log out reloads it)
            Lang.Init(); Lang.ClearListeners(); // client wave 4b (w4lang): the language, before any panel (Lang/Lang.cs)
            Config = LokiConfig.Load();
            // wave 28 (wtextc): the text pack (loki.json "textPack", a folder outside the build; Data/TextPack.cs), read by the loaders
            Data.TextPack.Log = s => Debug.Log(s);
            Data.TextPack.Configure(Config.textPack, System.IO.Path.GetDirectoryName(Application.dataPath));
            // First run: without the RO game data the map loader throws (MapLoader.Load → ReadOrThrow) before any UI exists.
            // Check the folder first and catch a failing start map, then show a panel that needs no game data.
            if (_kept == null && !LokiConfig.HasGameData(Config.clientRoot, Config.grfs))
                DataProblem = !System.IO.Directory.Exists(Config.clientRoot ?? "") ? "the folder does not exist" : "no " + string.Join(" / ", Config.grfs ?? new string[0]) + " and no data\\ folder in it";
            if (Config.LoadError != null) DataProblem = $"the configuration file could not be read: {Config.LoadError}"; // the panel says which file
            // client wave 7 (w7data): game files that are still being written (a patch in progress) are not opened: the start waits
            // until they are still (Data/FirstRunData.WaitForInstall), then this scene starts over
            Data.InstallWatch.Busy busy = DataProblem == null && _kept == null && Application.isPlaying ? Data.FirstRunData.InstallBusy(Config) : null;
            if (DataProblem == null && busy == null)
            {
                try
                {
                    Open();
                    if (LoadStartMapOnAwake) LoadBackdrop();
                }
                catch (System.Exception e)
                {
                    DataProblem = $"the start map '{Config.startMap}' could not be read ({e.Message})"; Debug.LogException(e);
                    // w7data: an archive that was skipped (unknown version, damaged, half-written) is the likelier reason: say it first, and
                    // let go of the opened data so that "Try again" reads the folder afresh
                    if (Assets != null && Assets.Fs.Skipped.Count > 0) DataProblem = Assets.Fs.Skipped[0].Reason + " — " + DataProblem;
                    if (_kept != null && (Map == null || Map.Current == null)) { _kept.Dispose(); _kept = null; Assets = null; }
                }
            }
            if (DataProblem != null) Debug.LogError(Config.LoadError != null ? $"[Loki] cannot start: {DataProblem}" : $"[Loki] no usable game data in '{Config.clientRoot}': {DataProblem}");
            if (Application.isPlaying)
            {
                DioramaScale = RigTableScale;
                if (Map != null)
                {
                    Map.transform.localScale = Vector3.one * DioramaScale;
                    if (Map.Current != null) Map.transform.localPosition = -new Vector3(Map.Current.Gnd.Width, 0, Map.Current.Gnd.Height) * DioramaScale;
                    if (Map.Current != null && _backdropCell.HasValue) FocusBackdrop(); // client wave 24 (w24): the login backdrop's cell at the table's centre
                }
                var rigGo = new GameObject("XR Rig");
                Rig = rigGo.AddComponent<VR.DioramaRig>();
                Rig.Build(transform); // the bootstrap object itself is the diorama pivot; MapLoader sits under it, centred
                StartCoroutine(RecentreSoon());
                if (busy != null) { Data.FirstRunData.WaitForInstall(this, busy); return; } // client wave 7 (w7data): the waiting panel
                if (DataProblem != null)
                {
                    UI.NoDataPanel.Show(Rig.transform, Rig.Head, Config, DataProblem);
                    return;
                }
                // client wave 3b (w3data): the tables made from the player's own game files come first (Data/FirstRunData)
                if (Data.FirstRunData.StartIfNeeded(this, () => gameObject.AddComponent<LokiGame>().Begin(this))) return;
                gameObject.AddComponent<LokiGame>().Begin(this);
            }
        }


        // ------------------------------------------------------------------ client wave 24 (w24): the login backdrop
        // design note (2026-10-12): "ideally the login screen should not be in Pronto, it should be somewhere that is
        // significantly clearer and cleaner". The scene the screens before the world stand in was the start map (loki.json
        // "startMap", "prontera": World/RoAssets.cs), centred on its middle: Prontera's main square, buildings all round. It is
        // now LoginBackdrop: the Valkyrie's hall (the tutorial's map of wave 21: one level stone floor in the sky, nothing in the
        // way; design notes), its level square's middle at the table's centre. Plain scenery: nothing of a server is
        // drawn before the world (units, NPCs and warps only ever come from a map server's packets). A game folder without the
        // map: the start map, as before.
        /// <summary>The login backdrop: the map and the cell at the table's centre (FLAGGED: specified as "somewhere that is
        /// significantly clearer and cleaner"; "" = the start map as before).</summary>
        public static readonly (string Map, int X, int Y) LoginBackdrop = ("valkyrie", 49, 36);
        /// <summary>The map the scene started on (the backdrop, else the start map; "" none), and how long its load took.</summary>
        public string BackdropMap { get; private set; } = "";
        public long BackdropLoadMs { get; private set; }
        private Vector2? _backdropCell;

        private void LoadBackdrop()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (!string.IsNullOrEmpty(LoginBackdrop.Map))
            {
                try
                {
                    LoadMap(LoginBackdrop.Map);
                    _backdropCell = new Vector2(LoginBackdrop.X, LoginBackdrop.Y); FocusBackdrop();
                    BackdropMap = LoginBackdrop.Map; BackdropLoadMs = sw.ElapsedMilliseconds;
                    Debug.Log($"[Loki] login backdrop '{BackdropMap}' cell {LoginBackdrop.X},{LoginBackdrop.Y}: loaded in {BackdropLoadMs} ms");
                    return;
                }
                catch (System.Exception e) { _backdropCell = null; Debug.Log($"[Loki] login backdrop '{LoginBackdrop.Map}' is not in this game data ({e.Message}): the start map '{Config.startMap}' instead"); }
            }
            if (string.IsNullOrEmpty(Config.startMap)) return;
            sw.Restart(); LoadMap(Config.startMap);
            BackdropMap = Config.startMap; BackdropLoadMs = sw.ElapsedMilliseconds;
            Debug.Log($"[Loki] start map '{BackdropMap}': loaded in {BackdropLoadMs} ms");
        }

        /// <summary>The backdrop's cell at the table's centre, its ground at the table's level (the ground's height is read from the
        /// map's ground mesh under the cell's middle).</summary>
        private void FocusBackdrop()
        {
            if (Map == null || Map.Current == null || !_backdropCell.HasValue) return;
            var cell = _backdropCell.Value; float y = 0f;
            var col = Map.Current.Ground != null ? Map.Current.Ground.GetComponent<Collider>() : null;
            if (col != null)
            {
                Physics.SyncTransforms();
                var top = Map.transform.TransformPoint(new Vector3(cell.x + 0.5f, 0f, cell.y + 0.5f));
                if (col.Raycast(new Ray(top + Vector3.up * 500f, Vector3.down), out var hit, 1000f)) y = Map.transform.InverseTransformPoint(hit.point).y;
            }
            FocusCell(cell, y);
        }

        private System.Collections.IEnumerator RecentreSoon()
        {
            yield return new WaitForSeconds(1.5f);
            Rig.RecentreDiorama();
        }

        /// <summary>The client data outlives a scene reload (character select / log out): the GRF index stays open and the textures stay
        /// cached — static caches (item / skill / buff icons, effect sprites) hold sprites made from those textures, so disposing them
        /// with the old scene would leave white icons in the new one. Released when the application quits.</summary>
        private static RoAssets _kept;

        public void Open()
        {
            Instance = this;
            if (Assets != null) return;
            if (_kept != null) { Assets = _kept; Debug.Log("[Loki] client data kept from the previous scene"); }
            else
            {
                if (!GrfCrypt.SelfTest()) Debug.LogError("[Loki] GrfCrypt self-test FAILED");
                var fs0 = new RoFileSystem(Config.clientRoot, Config.grfs);
                Assets = _kept = new RoAssets(fs0);
            }
            var fs = Assets.Fs;
            var mapGo = new GameObject("diorama");
            mapGo.transform.SetParent(transform, false);
            mapGo.transform.localScale = Vector3.one * DioramaScale;
            Map = mapGo.AddComponent<MapLoader>();
            Map.Init(Assets);
            Debug.Log($"[Loki] opened {Config.clientRoot}: {fs.Archives.Count} GRF(s), " + string.Join(", ", System.Linq.Enumerable.Select(fs.Archives, a => System.IO.Path.GetFileName(a.Path) + "=" + a.Count + " files")));
        }

        public MapLoader.LoadedMap LoadMap(string name)
        {
            var m = Map.Load(name);
            // Centre the diorama on the map so the parent transform is its middle.
            Map.transform.localPosition = -new Vector3(m.Gnd.Width, 0, m.Gnd.Height) * DioramaScale;
            return m;
        }

        /// <summary>Shifts the map inside the diorama so this cell (Loki units, fractional ok) sits at the table's centre.</summary>
        public void FocusCell(Vector2 cell, float groundY)
        {
            if (Map == null) return;
            Map.transform.localPosition = -new Vector3(cell.x + 0.5f, groundY, cell.y + 0.5f) * DioramaScale;
        }

        /// <summary>Starts the scene over (character select and log out): the game, the rig and the map are rebuilt from scratch.
        /// Static data (item / skill / effect tables, Comfort settings) stays loaded.</summary>
        public static void Reload()
        {
            Debug.Log("[Loki] reloading the scene");
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        private void OnDestroy()
        {
            Assets = null; // kept for the next scene (see _kept)
        }

        private void OnApplicationQuit()
        {
            _kept?.Dispose(); _kept = null;
        }
    }
}
