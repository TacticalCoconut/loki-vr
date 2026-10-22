using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace Loki.Data
{
    /// <summary>
    /// Client wave 3b (w3data): runs <see cref="ClientDataBuilder"/> before the game starts when the per-user tables are missing
    /// or were made from other game files (LokiBootstrap calls <see cref="StartIfNeeded"/> once the RO folder opened, before
    /// LokiGame.Begin: every table loads lazily after that). The builder works on a thread; the VR panel shows its steps.
    /// Nothing here depends on the server: the tables are the client's own (protocol-generic).
    ///
    /// Client wave 7 (w7data): the start follows the player's install and never breaks on it.
    ///  • A patch in progress: before the game data is opened at all, LokiBootstrap asks <see cref="InstallBusy"/>; when a game
    ///    file was written in the last minute, changes between two looks or is held open by another program,
    ///    <see cref="WaitForInstall"/> shows the waiting panel, looks again twice a second and starts the scene over once the
    ///    files have been still for the quiet period (Data/InstallWatch.cs). After the bound the panel offers Try again / Quit
    ///    and keeps looking. The builder checks once more: files that changed while it read them give nothing (it runs again
    ///    after another quiet period, three times at most; then the last good tables stay).
    ///  • Last good tables: a tool that fails or a table that does not pass its checks keeps the last good version; the panel
    ///    and the log name the table and the reason and say that Loki tries again at the next start (<see cref="Problems"/>).
    ///  • The install's patch marker ("Ragnarok Online data: patch N") is on the panel, in the log and in the stamp.
    /// </summary>
    public static class FirstRunData
    {
        /// <summary>Done for this process (a scene reload — character select, log out — does not check again).</summary>
        private static bool _checked;
        public static ClientDataBuilder Last { get; private set; }

        // ------------------------------------------------------------------ what the start found (the login screen, the smokes)
        /// <summary>The install's own description (order, patch marker), read at this process's first start.</summary>
        public static RoInstall Install { get; private set; }
        /// <summary>The tables that are not current after this process's build: kept from the last good set, suspect or absent.</summary>
        public static readonly List<ClientDataBuilder.TableOutcome> Problems = new List<ClientDataBuilder.TableOutcome>();
        /// <summary>How often the builder ran in this process, and why the first time ("the game data changed since …"); null when the tables were current.</summary>
        public static int Builds { get; private set; }
        public static string BuildReason { get; private set; }
        /// <summary>True when the tables were found current at this process's start (no build).</summary>
        public static bool WereCurrent { get; private set; }
        /// <summary>Seconds this process waited for the game files to be still, and the first reason.</summary>
        public static float WaitedSeconds { get; private set; }
        public static string WaitedFor { get; private set; }
        /// <summary>How often a game file was seen to change between two looks while this process waited.</summary>
        public static int WaitChanges { get; private set; }
        /// <summary>True when the files kept changing through every attempt of the builder: the last good tables are in use.</summary>
        public static bool GaveUpChanging { get; private set; }

        /// <summary>"Ragnarok Online data: patch 1486" in the player's language (or what is known instead), for the panels.</summary>
        public static string PatchLine(RoInstall install)
        {
            var p = install?.Patch;
            if (p == null) return "";
            if (p.Number < 0) return Lang.T("Ragnarok Online data: no patch number in this install");
            return p.Behind ? Lang.F("Ragnarok Online data: patch {0} (the patch list goes to {1}: not fully patched)", p.Number, p.ListLast)
                            : Lang.F("Ragnarok Online data: patch {0}", p.Number);
        }

        // ------------------------------------------------------------------ a patch in progress
        public static int QuietSeconds => InstallWatch.QuietSeconds;
        public static int MaxWaitSeconds => InstallWatch.MaxWaitSeconds;
        private const int MaxBuildAttempts = 3;

        private static List<string> _watch, _archives;
        private static float _waitBegan = -1f;

        /// <summary>Why the game files must not be read yet, or null when they are still (LokiBootstrap asks before it opens them).</summary>
        public static InstallWatch.Busy InstallBusy(World.LokiConfig cfg)
        {
            try
            {
                var install = RoInstall.Describe(cfg.clientRoot, cfg.grfs);
                _watch = ClientDataBuilder.WatchedPaths(cfg.clientRoot, install);
                _archives = ClientDataBuilder.ArchivePaths(cfg.clientRoot, install);
                return InstallWatch.Check(null, InstallWatch.Take(_watch), QuietSeconds, _archives);
            }
            catch (Exception e) { Debug.LogWarning("[DataGen] could not look at the game files' times: " + e.Message); return null; }
        }

        public static string BusyText(InstallWatch.Busy b)
        {
            if (b == null) return "";
            switch (b.Why)
            {
                case InstallWatch.Busy.Kind.InUse: return Lang.F("{0} is held open by another program (the patcher?).", b.File);
                case InstallWatch.Busy.Kind.Changed: return Lang.F("{0} changed while Loki was looking.", b.File);
                default: return Lang.F("{0} was written {1:0} s ago: {2:0} s to go.", b.File, b.SecondsAgo, Mathf.Max(0f, (float)b.SecondsLeft));
            }
        }

        /// <summary>Shows the waiting panel and starts the scene over once the game files are still (see the class comment).</summary>
        public static void WaitForInstall(LokiBootstrap boot, InstallWatch.Busy first)
        {
            boot.StartCoroutine(WaitRun(boot, first));
        }

        private static IEnumerator WaitRun(LokiBootstrap boot, InstallWatch.Busy first)
        {
            if (_waitBegan < 0f) _waitBegan = Time.realtimeSinceStartup;
            if (WaitedFor == null) WaitedFor = first.English;
            int quiet = QuietSeconds, max = MaxWaitSeconds;
            Debug.Log($"[DataGen] the game files in {boot.Config.clientRoot} are not still ({first.English}): waiting until nothing was written for {quiet} s (asking the player after {max} s)");
            var panel = boot.Rig != null ? UI.DataSetupPanel.ShowWait(boot.Rig.transform, boot.Rig.Head, boot.Config.clientRoot,
                () => { Debug.Log("[DataGen] wait: try again"); _waitBegan = -1f; LokiBootstrap.Reload(); },
                () => { Debug.Log("[DataGen] wait: quit"); Application.Quit(); }) : null;
            InstallWatch.Sample prev = null; bool asked = false; var busy = first;
            while (true)
            {
                var now = InstallWatch.Take(_watch);
                busy = InstallWatch.Check(prev, now, quiet, _archives);
                prev = now;
                float waited = Time.realtimeSinceStartup - _waitBegan;
                if (busy == null)
                {
                    WaitedSeconds += waited; _waitBegan = -1f;
                    Debug.Log($"[DataGen] the game files have been still for {quiet} s (waited {waited:0.0} s): starting");
                    if (panel != null) panel.Close();
                    LokiBootstrap.Reload();
                    yield break;
                }
                if (busy.Why == InstallWatch.Busy.Kind.Changed) { WaitChanges++; Debug.Log($"[DataGen] wait: {busy.File} changed while Loki was looking ({waited:0.0} s into the wait)"); }
                if (!asked && waited > max)
                {
                    asked = true;
                    Debug.LogWarning($"[DataGen] the game files are still changing after {waited:0} s ({busy.English}): asking the player (Loki keeps looking)");
                    if (panel != null) panel.ShowWaitButtons();
                }
                if (panel != null)
                    panel.SetWait(asked ? Lang.F("Still changing after {0:0} minutes: {1}", waited / 60f, BusyText(busy)) + "\n" + Lang.T("Let the official patcher finish. Loki starts by itself when the files are still, or press Try again.")
                                        : BusyText(busy));
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        // ------------------------------------------------------------------ the tables
        /// <summary>True when the builder has to run: it then shows the panel, runs and calls <paramref name="then"/> (on the main
        /// thread); false when the tables are current (<paramref name="then"/> is not called: the caller goes on at once).</summary>
        public static bool StartIfNeeded(LokiBootstrap boot, Action then)
        {
            if (_checked) { ClientData.Ready = true; return false; }
            _checked = true;
            var fs = boot.Assets.Fs;
            var src = fs.Source;
            string root = boot.Config.clientRoot;
            src.PartsDir = Path.Combine(ClientData.ShippedDir, "firstrun");
            src.EffectTable = Path.Combine(ClientData.ShippedDir, "fx", "effect_table.json");
            Install = src.Install;
            string outDir = ClientData.UserDataDir;
            var pm = src.Install.Patch;
            Debug.Log($"[DataGen] Ragnarok Online data: {(pm.Short ?? "no patch number (" + pm.Problem + ")")}{(pm.Exists ? $" ({pm.File}{(pm.ListLast >= 0 ? $"; {pm.ListFile} goes to {pm.ListLast}" : "")})" : "")}; archives in the install's order ({src.Install.OrderSource}): {string.Join(", ", src.Install.GrfNames)}; loose data\\ folder {(!src.Lookup.HasLooseFolder ? "none" : src.Lookup.LooseFirst ? "read first" : "read last")}");
            string stamp;
            try { stamp = ClientDataBuilder.CurrentStamp(src); }
            catch (Exception e) { Debug.LogError("[DataGen] cannot read the game files' stamp: " + e.Message); ClientData.Ready = true; return false; }
            if (src.Grfs.Count == 0 && !src.Lookup.HasLooseFolder) { Debug.LogWarning("[DataGen] no GRF open: the tables cannot be made (the build's copies are used where it has them)"); ClientData.Ready = true; return false; }
            if (ClientDataBuilder.UpToDate(outDir, stamp, src, out string why))
            {
                Debug.Log($"[DataGen] the tables in {outDir} are current (made from these game files): nothing to do");
                WereCurrent = true;
                ClientData.Ready = true;
                return false;
            }
            Debug.Log($"[DataGen] making the tables in {outDir} from {root} ({why})");
            BuildReason = why;
            boot.StartCoroutine(Run(boot, src, outDir, stamp, why, then));
            return true;
        }

        private static IEnumerator Run(LokiBootstrap boot, ClientDataBuilder.Source src, string outDir, string stamp, string why, Action then)
        {
            string root = src.Root;
            var panel = boot.Rig != null ? UI.DataSetupPanel.Show(boot.Rig.transform, boot.Rig.Head, root, WhyText(why), PatchLine(src.Install)) : null;
            ClientDataBuilder b = null;
            for (int attempt = 1; attempt <= MaxBuildAttempts; attempt++)
            {
                b = new ClientDataBuilder(src, outDir, s => Debug.Log(s));
                Last = b; Builds++;
                var builder = b; string st = stamp;
                var th = new Thread(() => builder.Run(st)) { IsBackground = true, Name = "Loki data builder" };
                th.Start();
                while (!b.Done)
                {
                    if (panel != null) { lock (b.Failed) panel.Refresh(b.StepIndex, Lang.F("Working on: {0}", UI.DataSetupPanel.StepName(b.StepName)), b.Failed.ToList(), false); }
                    yield return null;
                }
                if (!b.InstallChanged || b.Fatal != null) break;
                // the game files changed while the tools read them: nothing was kept; wait until they are still, then again
                Debug.LogWarning($"[DataGen] attempt {attempt} of {MaxBuildAttempts}: the game files changed while the tables were being made");
                if (attempt == MaxBuildAttempts) { GaveUpChanging = true; break; }
                var watch = ClientDataBuilder.WatchedPaths(root, src.Install);
                InstallWatch.Sample prev = null; float began = Time.realtimeSinceStartup; int quiet = QuietSeconds;
                while (true)
                {
                    var now = InstallWatch.Take(watch);
                    var busy = InstallWatch.Check(prev, now, quiet); prev = now;
                    if (busy == null) break;
                    if (WaitedFor == null) WaitedFor = busy.English;
                    if (panel != null) panel.Refresh(-1, Lang.T("The game files changed while Loki read them: waiting until they are still.") + " " + BusyText(busy), new List<string>(), false);
                    if (Time.realtimeSinceStartup - began > MaxWaitSeconds) break;
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                WaitedSeconds += Time.realtimeSinceStartup - began;
                // the marker and the order may be what changed: look at the install again (the open archives stay the ones opened at the start)
                var again = RoInstall.Describe(root, boot.Config.grfs);
                src.Install = again; Install = again;
                try { stamp = ClientDataBuilder.CurrentStamp(src); } catch (Exception e) { Debug.LogError("[DataGen] cannot read the game files' stamp: " + e.Message); break; }
            }

            var failed = b.Failed.ToList();
            Problems.Clear();
            lock (b.Tables) Problems.AddRange(b.Tables.Where(t => t.State != ClientDataBuilder.TableState.Current));
            int kept = Problems.Count(t => t.State == ClientDataBuilder.TableState.Kept), none = Problems.Count(t => t.State == ClientDataBuilder.TableState.None), suspect = Problems.Count(t => t.State == ClientDataBuilder.TableState.Suspect);
            string msg = b.Fatal != null ? "Could not make the tables: " + b.Fatal.Message
                       : GaveUpChanging ? $"Stopped after {b.ElapsedMs / 1000f:0.0} s: the game files kept changing; the tables from before are used."
                       : failed.Count > 0 ? $"Done in {b.ElapsedMs / 1000f:0.0} s, but {failed.Count} step(s) failed: {kept} table(s) kept from the last good set, {suspect} in use without passing the checks, {none} without any."
                       : $"Done in {b.ElapsedMs / 1000f:0.0} s.";
            Debug.Log($"[DataGen] {msg} {b.Written.Count} files written to {outDir}");
            foreach (var t in Problems)
                Debug.LogWarning($"[DataGen] NOTICE: {t.File} ({t.StepName}) {(t.State == ClientDataBuilder.TableState.Kept ? "is the last good version" : t.State == ClientDataBuilder.TableState.Suspect ? "did not pass its checks and there is no earlier version: it is used for now" : "could not be made and there is no earlier version")}: {t.Reason}. Loki will try again at the next start.");
            // w4lang: the log above stays English (release checks read "[DataGen] Done in"); the panel says it in the player's language
            string shown = b.Fatal != null ? Lang.F("Could not make the tables: {0}", b.Fatal.Message)
                         : GaveUpChanging ? Lang.T("The game files kept changing while Loki read them: the tables from before are used. Loki will try again at the next start.")
                         : failed.Count > 0 && none + suspect == 0 ? Lang.F("Done in {0:0.0} s. {1} table(s) could not be renewed: the last good ones are used.", b.ElapsedMs / 1000f, kept)
                         : failed.Count > 0 ? Lang.F("Done in {0:0.0} s, but {1} step(s) failed: some names or icons may be missing.", b.ElapsedMs / 1000f, failed.Count)
                         : Lang.F("Done in {0:0.0} s.", b.ElapsedMs / 1000f);
            if (panel != null) { panel.Refresh(ClientDataBuilder.Steps.Length, shown, failed, true); panel.SetNotice(NoticeText(3)); }
            ClientData.Ready = true;
            yield return new WaitForSeconds(Problems.Count > 0 || failed.Count > 0 || b.Fatal != null || GaveUpChanging ? 6f : 1f);
            if (panel != null) panel.Close();
            then();
        }

        /// <summary>Why the tables are made, in the player's language (the builder's reasons are English: it has no Lang; the log
        /// keeps them as they are).</summary>
        private static string WhyText(string why)
        {
            const string loose = "a loose game file changed (", missing = "missing ";
            if (why == "no tables made yet") return Lang.T("no tables made yet");
            if (why == "the game data changed since the tables were made") return Lang.T("the game data changed since the tables were made");
            if (why == "a table failed last time") return Lang.T("a table could not be made last time");
            if (why != null && why.StartsWith(loose, StringComparison.Ordinal) && why.EndsWith(")", StringComparison.Ordinal)) return Lang.F("a loose game file changed: {0}", why.Substring(loose.Length, why.Length - loose.Length - 1));
            if (why != null && why.StartsWith(missing, StringComparison.Ordinal)) return Lang.F("the table {0} is missing", why.Substring(missing.Length));
            return why ?? "";
        }

        /// <summary>The notice in the player's language: one line per step whose tables are not current (the table, what Loki uses
        /// instead, the reason), at most <paramref name="maxLines"/>, and that Loki tries again at the next start. "" when all are current.</summary>
        public static string NoticeText(int maxLines)
        {
            if (Problems.Count == 0) return "";
            var lines = new List<string>(); var seen = new HashSet<string>();
            foreach (var t in Problems)
            {
                if (!seen.Add(t.Tool + "|" + t.State)) continue;
                string files = string.Join(", ", Problems.Where(x => x.Tool == t.Tool && x.State == t.State).Select(x => x.File));
                string step = UI.DataSetupPanel.StepName(t.StepName ?? "");
                lines.Add(t.State == ClientDataBuilder.TableState.Kept ? Lang.F("{0} ({1}): the last good table is used. Reason: {2}", step, files, t.Reason)
                        : t.State == ClientDataBuilder.TableState.Suspect ? Lang.F("{0} ({1}): made, but it did not pass the checks and there is no earlier table; it is used for now. Reason: {2}", step, files, t.Reason)
                        : Lang.F("{0} ({1}): could not be made and there is no earlier table. Reason: {2}", step, files, t.Reason));
            }
            int more = lines.Count - maxLines;
            if (more > 0) { lines.RemoveRange(maxLines, more); lines.Add(Lang.F("… and {0} more (see Player.log).", more)); }
            lines.Add(Lang.T("Loki will try again at the next start."));
            return string.Join("\n", lines);
        }

    }
}
