using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Loki.Data;
using Loki.Effects;
using Loki.Formats;
using Loki.Game;
using Loki.Sprites;
using Loki.World;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Loki
{
    /// <summary>
    /// Client wave 7 (w7data): the game follows the player's Ragnarok Online install.
    ///  • The startup health check: once per process, after the tables are ready, a fixed set of assets every session needs is
    ///    loaded the way the game loads them — the login map (the scene already standing), prontera's three map files, the
    ///    Novice's and the six first jobs' bodies and a head (both sexes), the user-interface pictures Loki takes from the game
    ///    data (an item icon, a skill icon, prontera's minimap) and one effect file. A failure is one log line each and one
    ///    line on the login screen; nothing waits for it. It reads what the session would read anyway (the caches keep it).
    ///  • The login screen's line: the install's patch ("Ragnarok Online data: patch N"), the tables kept from the last good
    ///    set, the archives that were skipped, the files of this session that could not be read (UI/LoginPanel.W7Data.cs).
    ///  • A map that cannot be read (a version Loki does not know, a damaged file) keeps the old scene, as a missing one does.
    /// Nothing here is in the RO protocol; nothing depends on the server.
    /// </summary>
    public sealed partial class LokiGame
    {
        public sealed class W7Health
        {
            public int Checks; public long Ms;
            public readonly List<string> Failures = new List<string>();
            public readonly List<string> Passed = new List<string>();
            public int Failed => Failures.Count;
        }

        /// <summary>This process's startup health check (null before the first scene's game began).</summary>
        public static W7Health W7DataHealth { get; private set; }
        /// <summary>The health check must stay under this on a test PC (the brief); slower is said in the log.</summary>
        public const int W7HealthBudgetMs = 2000;

        private void BeginW7Data(LokiBootstrap boot)
        {
            if (W7DataHealth == null)
            {
                try { W7DataHealth = W7DataRunHealth(boot); }
                catch (Exception e) { W7DataHealth = new W7Health(); W7DataHealth.Failures.Add("the check itself: " + e.Message); Debug.LogError("[Health] the check stopped: " + e); }
            }
            if (Login != null) { Login.W7DataLines = W7DataLoginLines; Login.RefreshW7Data(); }
        }

        /// <summary>The login screen's two lines: the install's patch, and what the start found (empty when nothing is wrong).</summary>
        private string[] W7DataLoginLines()
        {
            var fs = _boot.Assets != null ? _boot.Assets.Fs : null;
            var install = FirstRunData.Install ?? fs?.Install;
            var notes = new List<string>();
            if (FirstRunData.Problems.Count > 0) notes.Add(Lang.F("{0} table(s) kept from before: Loki tries again at the next start", FirstRunData.Problems.Count));
            if (FirstRunData.GaveUpChanging) notes.Add(Lang.T("the game files kept changing: the tables from before are used"));
            if (fs != null && fs.Skipped.Count > 0)
            {
                var names = new List<string>(); foreach (var k in fs.Skipped) names.Add(k.Name);
                notes.Add(Lang.F("archive(s) not read: {0}", string.Join(", ", names)));
            }
            var h = W7DataHealth;
            if (h != null && h.Failed > 0) notes.Add(Lang.F("{0} of {1} start-up checks of the game files failed", h.Failed, h.Checks));
            int skipped = RoFormatLog.UnknownVersions + RoFormatLog.Damaged + (fs != null ? fs.DamagedCount : 0);
            if (skipped > 0) notes.Add(Lang.F("{0} game file(s) could not be read", skipped));
            string note = notes.Count == 0 ? "" : string.Join("; ", notes) + " " + Lang.T("(details in Player.log)");
            return new[] { install != null ? FirstRunData.PatchLine(install) : "", note };
        }

        // ---------------------------------------------------------------- the startup health check
        private static W7Health W7DataRunHealth(LokiBootstrap boot)
        {
            var h = new W7Health();
            var sw = Stopwatch.StartNew();
            var assets = boot.Assets; var fs = assets.Fs;
            void Check(string what, Func<string> problem)
            {
                h.Checks++;
                string p;
                try { p = problem(); } catch (Exception e) { p = e.GetType().Name + ": " + e.Message; }
                if (p == null) h.Passed.Add(what);
                else { h.Failures.Add(what + ": " + p); Debug.LogWarning($"[Health] FAILED {what}: {p}"); }
            }
            string MapFiles(string map)
            {
                var rsw = RswFile.Parse(fs.ReadOrThrow("data\\" + map + ".rsw"));
                var gnd = GndFile.Parse(fs.ReadOrThrow("data\\" + rsw.GndFile));
                var gat = GatFile.Parse(fs.ReadOrThrow("data\\" + rsw.GatFile));
                return gnd.Width > 0 && gat.Width == gnd.Width * 2 ? null : $"ground {gnd.Width} x {gnd.Height} tiles against {gat.Width} x {gat.Height} cells";
            }
            // the login map: the scene the login screen stands in (LokiBootstrap loaded it before anything else)
            string start = string.IsNullOrEmpty(boot.BackdropMap) ? boot.Config.startMap : boot.BackdropMap; // (client wave 24, w24: the scene before the world is the login backdrop: LokiBootstrap.LoginBackdrop; a game folder without it: the start map, as before)
            Check($"login map '{start}'", () => boot.Map != null && boot.Map.Current != null && boot.Map.Current.Name == start ? null : "the scene is not loaded");
            Check("map 'prontera' (rsw, gnd, gat)", () => boot.Map != null && boot.Map.Current != null && boot.Map.Current.Name == "prontera" ? null : MapFiles("prontera"));
            // the Novice and the six first jobs (JT_NOVICE 0 … JT_THIEF 6), both sexes, and a head
            string[] jobs = { "Novice", "Swordman", "Mage", "Archer", "Acolyte", "Merchant", "Thief" };
            for (int job = 0; job < jobs.Length; job++)
                for (int sex = 0; sex <= 1; sex++)
                {
                    int j = job, s = sex;
                    Check($"sprite {jobs[j]} ({(s == 0 ? "female" : "male")})", () =>
                    {
                        string path = RoNames.BodyPath(j, s);
                        if (path == null) return "no sprite name in the tables";
                        return assets.Sprite(path, out var spr, out var act) && spr.FrameCount > 0 && act.Actions.Length > 0 ? null : path + " cannot be read";
                    });
                }
            for (int sex = 0; sex <= 1; sex++)
            {
                int s = sex;
                Check($"sprite head 1 ({(s == 0 ? "female" : "male")})", () =>
                {
                    string path = RoNames.HeadPath(1, 0, s);
                    return path != null && assets.Sprite(path, out var spr, out var act) && spr.FrameCount > 0 ? null : (path ?? "no head path") + " cannot be read";
                });
            }
            // the user-interface pictures Loki takes from the game data (data\texture\유저인터페이스\…: UI/WristMenu.cs, LokiGame.EnterMap)
            const string ui = "À¯ÀúÀÎÅÍÆäÀÌ½º\\";
            Check("item icon (Red Potion 501)", () =>
            {
                var it = GameDb.GetItem(501);
                if (it == null || string.IsNullOrEmpty(it.Resource)) return "the item table has no picture name for 501";
                return assets.Texture(ui + "item\\" + it.Resource + ".bmp", false) != assets.MissingTexture ? null : "the picture is missing or does not decode";
            });
            Check("skill icon (Bash)", () => assets.Texture(ui + "item\\sm_bash.bmp", false) != assets.MissingTexture ? null : "the picture is missing or does not decode");
            Check("minimap (prontera)", () => assets.Texture(ui + "map\\prontera.bmp", false) != assets.MissingTexture ? null : "the picture is missing or does not decode");
            // one effect (an STR file: Fx/StrFile.cs)
            Check("effect file (safetywall.str)", () =>
            {
                var b = fs.Read("data\\texture\\effect\\safetywall.str");
                if (b == null) return "missing";
                var str = StrFile.Parse(b);
                return str.Layers.Length > 0 ? null : "no layers";
            });
            h.Ms = sw.ElapsedMilliseconds;
            // (worded so that no runner's verdict pattern — "N checks, M failed", "summary", "PASS" — can take this line for a smoke's verdict)
            Debug.Log($"[Health] game files: {h.Checks} looked at, {h.Failed} not readable, {h.Ms} ms{(h.Ms > W7HealthBudgetMs ? $" (over the {W7HealthBudgetMs} ms budget)" : "")}{(h.Failed > 0 ? ": " + string.Join("; ", h.Failures) : "")}");
            return h;
        }

        // ---------------------------------------------------------------- a map that cannot be read
        /// <summary>LokiGame.EnterMap's second catch: the server sent us to a map this game data cannot give — a version Loki does
        /// not know, a damaged file, or (with no scene standing yet) a missing one. The old scene stays; the player is told.</summary>
        private void W7DataMapUnreadable(string map, Exception e)
        {
            _w2MapMissing = true;
            if (e is FileNotFoundException)
            {
                Debug.LogError($"[Game] map {map} is missing from the client data: {e.Message}");
                Chat(Lang.F("Your game data has no map '{0}'. Warp out or use a Butterfly Wing.", map), "ff7a6a");
                ShowNotice(Lang.T("Map missing"), Lang.F("Your game data has no map '{0}'.\nWarp out (a Butterfly Wing, or ask a GM).", map));
                return;
            }
            bool unknown = e is RoFormatException f && f.UnknownVersion;
            Debug.LogError($"[Game] map {map} cannot be read from the client data ({(unknown ? "unknown version" : "damaged")}): {e.Message} — the old scene stays");
            string why = unknown ? Lang.T("its files are of a newer kind than this Loki knows") : Lang.T("its files are damaged");
            Chat(Lang.F("Loki can't read the map '{0}' ({1}). Warp out or use a Butterfly Wing.", map, why), "ff7a6a");
            ShowNotice(Lang.T("Map not readable"), Lang.F("Loki can't read the map '{0}' ({1}).\nWarp out (a Butterfly Wing, or ask a GM).", map, why));
        }
    }
}
