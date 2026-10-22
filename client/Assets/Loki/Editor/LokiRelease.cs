using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Loki.EditorTools
{
    /// <summary>
    /// Client wave 3b (w3data): a release build (<c>-lokiRelease</c> on LokiSetup.BuildWindows) carries nothing made from Gravity's
    /// game data. Before the build, every GRF-derived table (<see cref="Loki.Data.ClientDataBuilder.GeneratedFiles"/>: the client
    /// makes them on the player's PC) and Gravity's music copy are moved, each with its .meta, out of Assets/StreamingAssets into
    /// &lt;project&gt;/LokiReleaseStaging; after the build they are moved back (also at the start of the next build, should a build
    /// have stopped half-way). Nothing is deleted. A development build (no flag) keeps them.
    /// </summary>
    public static class LokiRelease
    {
        /// <summary>StreamingAssets files that are Gravity's own media: the classic Prontera theme (design notes: "music derived from Gravity's client"). loki.json's bgmOverrides names it.</summary>
        public static readonly string[] GravityMedia = { "bgm/prontera_classic.ogg" };

        private static string Sa => Path.GetFullPath(Application.streamingAssetsPath);
        private static string Staging => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "LokiReleaseStaging"));

        /// <summary>Every StreamingAssets path (relative, '/') a release build leaves out.</summary>
        public static IEnumerable<string> LeftOut() =>
            Loki.Data.ClientDataBuilder.GeneratedFiles.Select(f => "data/" + f).Concat(GravityMedia);

        private static string Native(string rel) => rel.Replace('/', Path.DirectorySeparatorChar);

        /// <summary>Moves the files (and their .meta) to the staging folder; returns what moved.</summary>
        public static List<string> MoveOut()
        {
            Restore();
            var moved = new List<string>();
            foreach (var rel in LeftOut())
            {
                foreach (var suffix in new[] { "", ".meta" })
                {
                    string from = Path.Combine(Sa, Native(rel)) + suffix, to = Path.Combine(Staging, Native(rel)) + suffix;
                    if (!File.Exists(from)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    File.Move(from, to);
                    moved.Add(rel + suffix);
                }
            }
            Debug.Log($"[LokiSetup] release: {moved.Count} files moved out of StreamingAssets for the build (to {Staging}): {string.Join(", ", moved)}");
            return moved;
        }

        /// <summary>Moves everything in the staging folder back (a file already back in StreamingAssets is left where it is).</summary>
        public static void Restore()
        {
            if (!Directory.Exists(Staging)) return;
            int n = 0;
            foreach (var f in Directory.GetFiles(Staging, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(Staging.Length).TrimStart(Path.DirectorySeparatorChar);
                string to = Path.Combine(Sa, rel);
                if (File.Exists(to)) { Debug.LogWarning($"[LokiSetup] release: {to} exists already; {f} stays in the staging folder"); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Move(f, to); n++;
            }
            if (n > 0) Debug.Log($"[LokiSetup] release: {n} files moved back into StreamingAssets");
        }

        /// <summary>After a release build: true when the build's StreamingAssets holds none of the left-out files and does hold
        /// the first-run builder's parts; its loki.json loses the music overrides whose file is not in the build.</summary>
        public static bool Check(string outDir)
        {
            string sa = Path.Combine(outDir, "Loki_Data", "StreamingAssets");
            var present = LeftOut().Where(r => File.Exists(Path.Combine(sa, Native(r)))).ToList();
            string parts = Path.Combine(sa, "data", "firstrun");
            bool hasParts = File.Exists(Path.Combine(parts, "cp949.bin")) && File.Exists(Path.Combine(parts, "items_rathena.tsv"));
            Debug.Log($"[LokiSetup] release build: GRF-derived or Gravity files in the build: {present.Count} {(present.Count > 0 ? "(" + string.Join(", ", present) + ")" : "")} (expected 0); first-run parts present: {hasParts} (expected True)");
            string cfgPath = Path.Combine(sa, "loki.json");
            if (File.Exists(cfgPath))
            {
                var cfg = JsonUtility.FromJson<Loki.World.LokiConfig>(File.ReadAllText(cfgPath));
                if (cfg != null && cfg.bgmOverrides != null)
                {
                    var keep = cfg.bgmOverrides.Where(o => File.Exists(Path.Combine(sa, o.file.Replace('\\', '/')))).ToArray();
                    if (keep.Length != cfg.bgmOverrides.Length)
                    {
                        Debug.Log($"[LokiSetup] release: loki.json music overrides {cfg.bgmOverrides.Length} \u2192 {keep.Length} (their files are not in a release build)");
                        cfg.bgmOverrides = keep;
                        File.WriteAllText(cfgPath, JsonUtility.ToJson(cfg, true));
                    }
                }
            }
            return present.Count == 0 && hasParts;
        }

        /// <summary>Client wave 4b (w4release): the scripting define of the smoke / test code (<c>#if LOKI_SMOKE</c>). BuildWindows gives it
        /// to development builds only; a release build is compiled without it. The wrap rule: design notes</summary>
        public const string SmokeDefine = "LOKI_SMOKE";

        /// <summary>A LOKI_SMOKE in the project's own scripting defines (Player Settings) would compile the smoke code into the Editor and
        /// into every build, a release build too: it is taken out (logged). Only BuildWindows sets it, for one development build.</summary>
        public static void KeepSmokeOutOfProject()
        {
            var nbt = UnityEditor.Build.NamedBuildTarget.Standalone;
            string defines = PlayerSettings.GetScriptingDefineSymbols(nbt) ?? "";
            var list = defines.Split(';').Select(d => d.Trim()).Where(d => d.Length > 0).ToList();
            if (!list.Contains(SmokeDefine)) return;
            list.RemoveAll(d => d == SmokeDefine);
            PlayerSettings.SetScriptingDefineSymbols(nbt, string.Join(";", list));
            Debug.Log($"[LokiSetup] {SmokeDefine} removed from the project's scripting defines (were '{defines}'): only BuildWindows sets it, for a development build");
        }

        /// <summary>After a build, a quick look into the built Assembly-CSharp.dll: the UTF-8 "Smoke" (type and member names live in the
        /// metadata's name heap as UTF-8) and the UTF-16 "-lokiSmoke" and "[Smoke]" (string literals live in the user-string heap as
        /// UTF-16). A release build must have none of them and a development build must have them (else the define did not reach the
        /// compiler): false fails the build. The full proof is client/tools/w4release/scan (reflection over every member and string).</summary>
        public static bool CheckSmokeCode(string outDir, bool release)
        {
            string dll = Path.Combine(outDir, "Loki_Data", "Managed", "Assembly-CSharp.dll");
            if (!File.Exists(dll)) { Debug.LogError("[LokiSetup] smoke check: no " + dll); return false; }
            var b = File.ReadAllBytes(dll);
            int names = Count(b, Encoding.UTF8.GetBytes("Smoke")), args = Count(b, Encoding.Unicode.GetBytes("-lokiSmoke")), lines = Count(b, Encoding.Unicode.GetBytes("[Smoke]"));
            bool ok = release ? names == 0 && args == 0 && lines == 0 : names > 0 && args > 0 && lines > 0;
            string msg = $"[LokiSetup] {(release ? "release" : "development")} build: smoke code in Assembly-CSharp.dll: 'Smoke' in names {names}, '-lokiSmoke' strings {args}, " +
                         $"'[Smoke]' strings {lines} (expected {(release ? "0, 0, 0" : "more than 0 each")}){(ok ? "" : " - FAILED")}";
            if (ok) Debug.Log(msg); else Debug.LogError(msg);
            return ok;
        }

        private static int Count(byte[] hay, byte[] needle)
        {
            int n = 0;
            for (int i = 0; i + needle.Length <= hay.Length; i++)
            {
                int k = 0; while (k < needle.Length && hay[i + k] == needle[k]) k++;
                if (k == needle.Length) { n++; i += needle.Length - 1; }
            }
            return n;
        }
    }
}
