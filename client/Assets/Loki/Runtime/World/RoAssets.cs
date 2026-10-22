using System.Collections.Generic;
using System.IO;
using Loki.Formats;
using Loki.IO;
using UnityEngine;

namespace Loki.World
{
    /// <summary>Client data access with caches: file system + textures + parsed models/sprites. One per running client.</summary>
    public sealed class RoAssets : System.IDisposable
    {
        public readonly RoFileSystem Fs;
        private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, RsmFile> _models = new Dictionary<string, RsmFile>();
        private readonly Dictionary<string, Mesh> _modelMeshes = new Dictionary<string, Mesh>();
        private readonly Dictionary<string, SprFile> _sprs = new Dictionary<string, SprFile>();
        private readonly Dictionary<string, ActFile> _acts = new Dictionary<string, ActFile>();
        public Texture2D MissingTexture { get; private set; }

        public RoAssets(RoFileSystem fs)
        {
            Fs = fs;
            MissingTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            MissingTexture.SetPixels32(new[] { new Color32(255, 0, 255, 255), new Color32(0, 0, 0, 255), new Color32(0, 0, 0, 255), new Color32(255, 0, 255, 255) });
            MissingTexture.Apply();
            MissingTexture.name = "missing";
        }

        /// <summary>data\texture\{name}, decoded and cached. Magenta-keyed for BMP/JPG like the client.</summary>
        private readonly HashSet<string> _black = new HashSet<string>();

        /// <summary>True for a texture whose every opaque pixel is black (channels ≤ 8/255): the "void filler" that map
        /// makers put on ground tiles, walls and model faces around floating interiors (BLACK.BMP, BACKSIDE.BMP,
        /// sgid2-side2.bmp, …). The original client clears the screen to black, so these vanish there; Loki has a sky.</summary>
        public bool IsBlackTexture(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            Texture(name);
            return _black.Contains(RoPath.Normalize(name));
        }

        private static bool IsAllBlack(in ImageDecoders.Image img) => ImageDecoders.IsAllBlack(img); // (client wave 20, w20rooms: the test moved to IO/ImageDecoders.cs for the room table)

        public Texture2D Texture(string name, bool mipmaps = true)
        {
            string key = RoPath.Normalize(name);
            if (_textures.TryGetValue(key, out var t)) return t;
            var bytes = Fs.Read("data\\texture\\" + key);
            t = null;
            if (bytes != null)
            {
                // client wave 7 (w7data): a picture that does not decode (damaged, a kind the decoders do not know) is the missing texture
                try
                {
                    if (ImageDecoders.TryDecode(key, bytes, out var img))
                    {
                        t = ImageDecoders.ToTexture(img, mipmaps, key);
                        if (IsAllBlack(img)) _black.Add(key);
                    }
                }
                catch (System.Exception e) { RoFormatLog.Report("texture", key, e); t = null; }
            }
            if (t == null)
            {
                Debug.LogWarning("[RoAssets] texture missing: " + key);
                t = MissingTexture;
            }
            _textures[key] = t;
            return t;
        }

        public RsmFile Model(string fileName)
        {
            string key = RoPath.Normalize(fileName);
            if (_models.TryGetValue(key, out var m)) return m;
            var bytes = Fs.Read("data\\model\\" + key);
            // client wave 7 (w7data): a model of an unknown version or a damaged one is left out of the map (it threw before: the
            // map load stopped half-way with the old map already gone)
            m = null;
            if (bytes != null) { try { m = RsmFile.Parse(bytes); } catch (System.Exception e) { RoFormatLog.Report("model", key, e); } }
            if (m == null) Debug.LogWarning("[RoAssets] model missing: " + key);
            _models[key] = m;
            return m;
        }

        /// <summary>Static (t=0) mesh for a model file, cached. Callers must not modify it.</summary>
        public Mesh ModelMesh(string fileName, out RsmFile rsm)
        {
            string key = RoPath.Normalize(fileName);
            rsm = Model(key);
            if (rsm == null) return null;
            if (_modelMeshes.TryGetValue(key, out var mesh)) return mesh;
            try { mesh = RsmMeshBuilder.Build(rsm, 0f); }
            catch (System.Exception e) { RoFormatLog.Report("model", key, e); _models[key] = null; _modelMeshes[key] = null; rsm = null; return null; } // w7data: indices that point nowhere
            mesh.name = key;
            _modelMeshes[key] = mesh;
            return mesh;
        }

        /// <summary>SPR + ACT pair by path without extension (e.g. data\sprite\몬스터\poring). Null parts when missing.</summary>
        public bool Sprite(string basePath, out SprFile spr, out ActFile act)
        {
            string key = RoPath.Normalize(basePath);
            if (!_sprs.TryGetValue(key, out spr))
            {
                var b = Fs.Read(key + ".spr");
                spr = null;
                if (b != null) { try { spr = SprFile.Parse(b); } catch (System.Exception e) { RoFormatLog.Report("sprite", key + ".spr", e); } }
                else Debug.LogWarning("[RoAssets] sprite missing: " + key + ".spr");
                _sprs[key] = spr;
            }
            if (!_acts.TryGetValue(key, out act))
            {
                var b = Fs.Read(key + ".act");
                act = null;
                if (b != null) { try { act = ActFile.Parse(b); } catch (System.Exception e) { RoFormatLog.Report("sprite", key + ".act", e); } }
                _acts[key] = act;
            }
            return spr != null && act != null;
        }

        private readonly Dictionary<string, bool> _exists = new Dictionary<string, bool>();
        private readonly Dictionary<string, Color32[]> _palettes = new Dictionary<string, Color32[]>();

        /// <summary>True when the client data holds this exact file (loose file or GRF entry). Cached per path, no log.</summary>
        public bool Exists(string path)
        {
            string key = RoPath.Normalize(path);
            if (!_exists.TryGetValue(key, out bool e)) { e = Fs.Exists(key); _exists[key] = e; }
            return e;
        }

        /// <summary>True when both basePath.spr and basePath.act exist.</summary>
        public bool SpriteExists(string basePath) => Exists(basePath + ".spr") && Exists(basePath + ".act");

        /// <summary>Only the SPR of a sprite pair (same cache as <see cref="Sprite"/>). Null when missing or unreadable.</summary>
        public SprFile SpriteSpr(string basePath)
        {
            string key = RoPath.Normalize(basePath);
            if (_sprs.TryGetValue(key, out var spr)) return spr;
            spr = null;
            var b = Fs.Read(key + ".spr");
            if (b != null) { try { spr = SprFile.Parse(b); } catch (System.Exception e) { RoFormatLog.Report("sprite", key + ".spr", e); } }
            _sprs[key] = spr;
            return spr;
        }

        /// <summary>Only the ACT of a sprite pair (same cache as <see cref="Sprite"/>). Null when missing or unreadable.</summary>
        public ActFile SpriteAct(string basePath)
        {
            string key = RoPath.Normalize(basePath);
            if (_acts.TryGetValue(key, out var act)) return act;
            act = null;
            var b = Fs.Read(key + ".act");
            if (b != null) { try { act = ActFile.Parse(b); } catch (System.Exception e) { RoFormatLog.Report("sprite", key + ".act", e); } }
            _acts[key] = act;
            return act;
        }

        /// <summary>
        /// A .pal file (full path, e.g. data\palette\머리\머리4_남_3.pal): 256 × 4 bytes R,G,B,unused. Index 0 is transparent,
        /// every other index opaque (roBrowserLegacy SpriteRenderer.fs treats LUT index 0 as transparent and forces alpha 1).
        /// Cached per path, null (cached too) when missing or shorter than 1024 bytes.
        /// </summary>
        public Color32[] Palette(string path)
        {
            string key = RoPath.Normalize(path);
            if (_palettes.TryGetValue(key, out var pal)) return pal;
            pal = null;
            var b = Fs.Read(key);
            if (b != null && b.Length >= 1024)
            {
                pal = new Color32[256];
                for (int i = 0; i < 256; i++) pal[i] = new Color32(b[i * 4], b[i * 4 + 1], b[i * 4 + 2], (byte)(i == 0 ? 0 : 255));
            }
            else if (b != null) Debug.LogWarning($"[RoAssets] bad palette {key}: {b.Length} bytes");
            _palettes[key] = pal;
            return pal;
        }

        public void Dispose()
        {
            foreach (var t in _textures.Values) if (t != MissingTexture) LokiUtil.Destroy(t);
            foreach (var m in _modelMeshes.Values) LokiUtil.Destroy(m);
            _textures.Clear(); _modelMeshes.Clear(); _models.Clear(); _black.Clear();
            Fs.Dispose();
        }
    }

    /// <summary>StreamingAssets/loki.json</summary>
    [System.Serializable]
    public sealed class LokiConfig
    {
        public string clientRoot = "C:\\Ragnarok Online";
        public string[] grfs = { "data.grf" };
        public string startMap = "prontera";
        public string loginHost = "127.0.0.1";
        public int loginPort = 6900;
        /// <summary>Dev / test auto-login (username + password skip the login screen). The shipped file has none: the build helper
        /// (Editor/LokiSetup.cs BuildWindows) blanks them in the build's copy, or every friend would share one account.</summary>
        public string username = "";   // plain account name (≥ 6 chars); auto-registered via rAthena's _M/_F suffix on first login
        public string password = "";
        public string charName = "Loki";
        /// <summary>Old switch for the headless smoke test; ignored unless the command line has <c>-lokiSmoke</c> (LokiGame.Begin).</summary>
        public bool debugAutoWalk = false;
        /// <summary>Chat lines sent once, 2 s after entering the map — the way to run @commands until VR text entry exists.</summary>
        public string[] startupCommands = new string[0];
        /// <summary>Music replacements per map, played instead of the client's mp3nametable entry. file is relative to
        /// StreamingAssets (.ogg, .mp3 or .wav). The client install is never modified.</summary>
        public BgmOverride[] bgmOverrides = new BgmOverride[0];
        /// <summary>Language of client texts (system messages, map names): "en" or "pt".</summary>
        public string textLanguage = "en";
        /// <summary>Wave 28 (wtextc): the text pack — the overhaul's rewritten game texts, a folder read at run time over the tables made
        /// from the player's data (Data/TextPack.cs). An absolute folder, or one relative to the game's folder (where Loki.exe is);
        /// the default "textpack" is the folder next to the game, where the friends' package puts it; "" turns it off.</summary>
        public string textPack = "textpack";
        /// <summary>Servers offered on the login screen (name, host, port). Empty = loginHost/loginPort as "Local".</summary>
        public ServerEntry[] servers = new ServerEntry[0];
        /// <summary>Log in with md5(salt ‖ password) (CA_LOGIN2) instead of the plain password. Needs `use_MD5_passwords: no` on the server (the default).</summary>
        public bool hashedLogin = true;
        [System.Serializable] public sealed class BgmOverride { public string map = ""; public string file = ""; }
        /// <summary>A login-screen server: `kind` "rathena" (default) or "spacetimedb" (Loki Online; `database` is the SpacetimeDB database name).
        /// `scheme` (Loki Online only): "ws" (default, plain) or "wss" (TLS, e.g. local on port 443); a host written as
        /// "wss://name" or "https://name" sets it too.</summary>
        [System.Serializable] public sealed class ServerEntry
        {
            public string name = ""; public string host = ""; public int port = 6900; public string kind = "rathena"; public string database = "loki"; public string scheme = "";
            /// <summary>The headset pose relay's port (w3presence; Loki Host: 7903); 0 = the login port + 3.</summary>
            public int presencePort = 0;
        }

        /// <summary>Where the file came from (the command-line <c>-lokiConfig</c> file or StreamingAssets/loki.json).</summary>
        [System.NonSerialized] public string LoadedFrom = "";
        /// <summary>Why the file could not be parsed (null when it could): the missing-data panel shows it.</summary>
        [System.NonSerialized] public string LoadError;
        /// <summary>The configured data folder, before a stored choice (see <see cref="DataFolderKey"/>) replaced a missing one.</summary>
        [System.NonSerialized] public string ConfiguredClientRoot = "";
        /// <summary>PlayerPrefs key where client wave 1 stored a data folder picked on the "no game data" panel; read when the per-user
        /// file (<see cref="UserSettings"/>) has none. New choices go to that file.</summary>
        public const string DataFolderKey = "loki.clientRoot";
        /// <summary>Where the folder in use came from: "loki.json", "your choice" (the per-user file) or "your earlier choice" (PlayerPrefs).</summary>
        [System.NonSerialized] public string ClientRootSource = "loki.json";

        /// <summary>StreamingAssets/loki.json, or the file named by <c>-lokiConfig &lt;path&gt;</c> (tests point a build at their own
        /// servers without touching the shipped file).</summary>
        public static LokiConfig Load()
        {
            string p = Path.Combine(Application.streamingAssetsPath, "loki.json");
            bool customConfig = false;
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-lokiConfig" && !string.IsNullOrWhiteSpace(a[i + 1])) { p = a[i + 1]; customConfig = true; }
            LokiConfig c;
            if (File.Exists(p))
            {
                // a broken file must not stop start-up before any UI exists: defaults, and the reason for the no-data panel
                try { c = JsonUtility.FromJson<LokiConfig>(File.ReadAllText(p)) ?? new LokiConfig(); }
                catch (System.Exception e) { Debug.LogError($"[Loki] {p} could not be read: {e.Message}"); c = new LokiConfig(); c.LoadError = e.Message; }
                c.LoadedFrom = p;
            }
            else { Debug.LogWarning("[Loki] no " + p + ", using defaults"); c = new LokiConfig(); }
            if (!customConfig) EnsureLokiHostServer(c);
            c.ConfiguredClientRoot = c.clientRoot;
            // a folder picked in game (the per-user file, UserSettings) comes first when it holds the game data (client wave 4: the login
            // screen's "Game data…" changes a working folder too); then loki.json's; then client wave 1's PlayerPrefs key
            string picked = UserSettings.Current.dataFolder, legacy = null;
            string pickedResolved = ResolveGameDataRoot(picked, c.grfs);
            string cfgResolved = ResolveGameDataRoot(c.clientRoot, c.grfs);
            if (pickedResolved != null)
            {
                if (!string.Equals(pickedResolved, c.clientRoot, System.StringComparison.OrdinalIgnoreCase)) Debug.Log($"[Loki] using the folder picked in game, {pickedResolved} ({UserSettings.FilePath}), instead of loki.json's {c.clientRoot}");
                c.clientRoot = pickedResolved; c.ClientRootSource = "your choice";
            }
            else if (cfgResolved != null)
            {
                if (!string.Equals(cfgResolved, c.clientRoot, System.StringComparison.OrdinalIgnoreCase)) Debug.Log($"[Loki] {c.clientRoot} has game data in its Ragnarok subfolder: using {cfgResolved}");
                c.clientRoot = cfgResolved;
            }
            else
            {
                try { legacy = PlayerPrefs.GetString(DataFolderKey, ""); } catch { }
                if (!string.IsNullOrEmpty(picked)) Debug.LogWarning($"[Loki] the folder picked in game, {picked}, has no game data any more");
                string legacyResolved = ResolveGameDataRoot(legacy, c.grfs);
                if (legacyResolved != null) { Debug.Log($"[Loki] {c.clientRoot} has no game data: using the folder chosen earlier, {legacyResolved}"); c.clientRoot = legacyResolved; c.ClientRootSource = "your earlier choice"; }
            }
            return c;
        }

        private static void EnsureLokiHostServer(LokiConfig c)
        {
            if (c == null) return;
            var list = new System.Collections.Generic.List<ServerEntry>(c.servers ?? new ServerEntry[0]);
            if (list.Count == 0)
            {
                list.Add(new ServerEntry { name = "Loki Online", host = "127.0.0.1", port = 3000, kind = "spacetimedb", database = "loki" });
                list.Add(new ServerEntry { name = "Loki Host (local)", host = "127.0.0.1", port = 7900, kind = "rathena", presencePort = 7903 });
                list.Add(new ServerEntry { name = "rAthena (local)", host = "127.0.0.1", port = 6900, kind = "rathena" });
                c.servers = list.ToArray();
                return;
            }
            int stdbIdx = list.FindIndex(s => s != null && s.kind == "spacetimedb");
            if (stdbIdx < 0)
            {
                list.Insert(0, new ServerEntry { name = "Loki Online", host = "127.0.0.1", port = 3000, kind = "spacetimedb", database = "loki" });
            }
            else if (stdbIdx > 0)
            {
                var stdb = list[stdbIdx];
                list.RemoveAt(stdbIdx);
                list.Insert(0, stdb);
            }
            bool hasLokiHost = list.Exists(s => s != null && s.port == 7900 && (s.host == "127.0.0.1" || s.host == "localhost"));
            if (!hasLokiHost)
            {
                list.Add(new ServerEntry { name = "Loki Host (local)", host = "127.0.0.1", port = 7900, kind = "rathena", presencePort = 7903 });
            }
            c.servers = list.ToArray();
        }

        /// <summary>Removes what must never ship: the auto-login account, the character to pick, the smoke switch and the startup
        /// commands (the build helper writes the build's loki.json through this; re-audit 2026-09-29 §4).</summary>
        public void StripForShipping()
        {
            username = ""; password = ""; charName = ""; debugAutoWalk = false; startupCommands = new string[0];
        }

        /// <summary>Returns <paramref name="root"/> when it holds game data directly, or its <c>Ragnarok</c> subfolder when the
        /// LATAM installer placed the archives there; null otherwise.</summary>
        public static string ResolveGameDataRoot(string root, string[] grfs)
        {
            if (string.IsNullOrWhiteSpace(root)) return null;
            string clean = root.Trim().Trim('"');
            if (HasGameData(clean, grfs)) return clean;
            try
            {
                string sub = Path.Combine(clean, "Ragnarok");
                if (HasGameData(sub, grfs)) return sub;
            }
            catch { }
            return null;
        }

        /// <summary>True when the folder exists and holds one of its archives or a loose data\ folder (RoFileSystem's two sources).
        /// Client wave 7 (w7data): the archives are the install's own list when it has a DATA.INI, else the configured ones (Data/RoInstall.cs).</summary>
        public static bool HasGameData(string root, string[] grfs)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return false;
            foreach (var g in Loki.Data.RoInstall.QuickOrder(root, grfs)) { try { if (!string.IsNullOrEmpty(g) && File.Exists(Path.Combine(root, g))) return true; } catch { } }
            return Directory.Exists(Path.Combine(root, "data"));
        }
    }
}
