using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Loki.IO;

namespace Loki.Data
{
    /// <summary>
    /// Client wave 7 (w7data): what the player's Ragnarok Online install says about itself — which archives the game reads and
    /// in which order, and the patcher's own marker of how far the install is patched. Read only: nothing is ever written
    /// into the install. Pure C# (no UnityEngine; needs Data/Lub51.cs + PyCompat.cs for the patcher's settings).
    ///
    /// WHICH ARCHIVES, IN WHICH ORDER. What could be read on 2026-10-04 (design notes §2 has the full list):
    ///  • DATA.INI in the install folder: "N=name.grf" lines, read in ascending N, the first archive that holds a file wins
    ///    (roBrowserLegacy src/Core/FileManager.js:78-108 and 224-231 — a reimplementation, and the only source: the LATAM
    ///    client's Ragexe.exe is Themida-packed, sections ".themida" / ".boot", nothing readable). When the install has one,
    ///    Loki follows it.
    ///  • The LATAM install (C:\Ragnarok Online, patch 1486) has NO DATA.INI. Its patcher's own settings
    ///    (PatchClient\LuaFiles514\Local\ServerInfo.lub, Lua 5.1 bytecode run here by Loki's own VM) name the archive the
    ///    patcher keeps current: g_grf_file_name = "data.grf". Nothing readable names another one, so Loki keeps the configured
    ///    list (loki.json "grfs", data.grf by default) — the behaviour before this wave — and logs when the patcher's name is
    ///    not in it. The install's event.grf (5.6 MB, 2010-01-27, GRF version 0x102) is named by no readable file and no reader
    ///    on this PC opens that version in the client: it is not read.
    ///  • roBrowserLegacy without a DATA.INI loads every *.grf, smallest first (FileManager.js:85, 132-136): its own
    ///    convenience, not an official rule, and not taken.
    ///
    /// THE PATCH MARKER. The patcher's settings name it: g_inf_file_name = "patch.inf", beside g_patch_list_name = "patch.txt"
    /// (the list the patcher downloads: "N file" lines, disabled ones behind "//"). On the LATAM install patch.inf is 4 bytes,
    /// CE 05 00 00 = 1486 little-endian, equal to the number of patch.txt's last line ("1486 2026-09-15_live_client_…gpf") and
    /// to what the patcher showed (HANDOFF.md: "patched to 1486"). Only that 4-byte form is read as a number; any other size is
    /// reported as an unknown marker and the stamp records its size and time instead. An install without the patcher's
    /// settings is looked at for "patch.inf" by that name (said in the log).
    /// </summary>
    public sealed class RoInstall
    {
        public const string DataIniName = "DATA.INI";
        public const string PatcherSettings = "PatchClient\\LuaFiles514\\Local\\ServerInfo.lub";

        public string Root = "";
        /// <summary>The archives in the order the game reads them (file names relative to the install folder).</summary>
        public readonly List<string> GrfNames = new List<string>();
        /// <summary>"DATA.INI" or "the configured list" (loki.json's grfs).</summary>
        public string OrderSource = ConfiguredSource;
        public const string ConfiguredSource = "the configured list";
        public bool HasDataIni;
        /// <summary>From the patcher's settings (null when the install has none or they cannot be run).</summary>
        public string PatcherGrf, PatcherInf, PatcherList;
        public string PatcherProblem;
        /// <summary>Lines for the log (what was found, what was not).</summary>
        public readonly List<string> Notes = new List<string>();
        public PatchMarker Patch = new PatchMarker();

        /// <summary>Reads the install's own description. Never throws: a file that cannot be read becomes a note.</summary>
        public static RoInstall Describe(string root, string[] configuredGrfs)
        {
            var r = new RoInstall { Root = root ?? "" };
            var configured = (configuredGrfs ?? new string[0]).Where(g => !string.IsNullOrWhiteSpace(g)).ToList();
            if (configured.Count == 0) configured.Add("data.grf");

            // ---- DATA.INI
            string ini = SafeCombine(r.Root, DataIniName);
            try
            {
                if (ini != null && File.Exists(ini))
                {
                    var list = ParseDataIni(Py.Latin1(File.ReadAllBytes(ini)));
                    if (list.Count > 0) { r.GrfNames.AddRange(list); r.HasDataIni = true; r.OrderSource = DataIniName; r.Notes.Add($"{DataIniName} lists {list.Count} archive(s): {string.Join(", ", list)}"); }
                    else r.Notes.Add($"{DataIniName} exists but lists no archive (no \"N=name\" line): the configured list is used");
                }
            }
            catch (Exception e) { r.Notes.Add($"{DataIniName} cannot be read ({e.Message.Trim()}): the configured list is used"); }

            // ---- the patcher's settings
            string ps = SafeCombine(r.Root, PatcherSettings);
            try
            {
                if (ps != null && File.Exists(ps))
                {
                    var vm = new LuaVM();
                    vm.Run(File.ReadAllBytes(ps));
                    r.PatcherGrf = Str(vm.Globals.Get("g_grf_file_name"));
                    r.PatcherInf = Str(vm.Globals.Get("g_inf_file_name"));
                    r.PatcherList = Str(vm.Globals.Get("g_patch_list_name"));
                    r.Notes.Add($"the patcher's settings ({PatcherSettings}) name the archive '{r.PatcherGrf ?? "-"}', the patch marker '{r.PatcherInf ?? "-"}' and the patch list '{r.PatcherList ?? "-"}'");
                }
                else r.Notes.Add($"no patcher settings in this install ({PatcherSettings})");
            }
            catch (Exception e) { r.PatcherProblem = e.GetType().Name + ": " + e.Message.Trim(); r.Notes.Add($"the patcher's settings ({PatcherSettings}) cannot be run: {r.PatcherProblem}"); }

            if (!r.HasDataIni)
            {
                r.GrfNames.AddRange(configured);
                r.Notes.Add($"no {DataIniName} in this install: the configured list is used ({string.Join(", ", configured)})");
                if (!string.IsNullOrEmpty(r.PatcherGrf) && !configured.Any(g => string.Equals(g, r.PatcherGrf, StringComparison.OrdinalIgnoreCase)))
                    r.Notes.Add($"WARNING: the patcher keeps '{r.PatcherGrf}' current, which is not in the configured list: add it to \"grfs\" in loki.json");
            }
            r.Patch = PatchMarker.Read(r.Root, r.PatcherInf, r.PatcherList);
            return r;
        }

        /// <summary>Only the archive list (DATA.INI's when the install has one, else the configured one): the cheap question
        /// "does this folder hold game data?" asks it for every folder it looks at (LokiConfig.HasGameData).</summary>
        public static List<string> QuickOrder(string root, string[] configuredGrfs)
        {
            try
            {
                string ini = SafeCombine(root ?? "", DataIniName);
                if (ini != null && File.Exists(ini))
                {
                    var list = ParseDataIni(Py.Latin1(File.ReadAllBytes(ini)));
                    if (list.Count > 0) return list;
                }
            }
            catch { }
            var configured = (configuredGrfs ?? new string[0]).Where(g => !string.IsNullOrWhiteSpace(g)).ToList();
            if (configured.Count == 0) configured.Add("data.grf");
            return configured;
        }

        /// <summary>Opens the install as the game reads it — the ONE place that does (the client's file system, the first-run
        /// builder, the offline harness, and Loki Host when it links this file): the archives of the install's order, the ones
        /// that cannot be opened skipped with their reason (a missing file, a version this reader does not know, a damaged or
        /// half-written archive), and the loose data\ folder's place by the clientinfo's readfolder tag (IO/RoLookup.cs; an
        /// install without any clientinfo keeps loose files first, as before client wave 7). Never throws for an archive.</summary>
        public static RoOpened Open(string root, string[] configuredGrfs, Action<string> log = null)
        {
            var o = new RoOpened { Install = Describe(root, configuredGrfs) };
            var grfs = new List<GrfArchive>();
            foreach (var name in o.Install.GrfNames)
            {
                string p = SafeCombine(root ?? "", name);
                if (p == null || !File.Exists(p)) { o.Skipped.Add(new SkippedGrf { Name = name, Kind = "missing", Reason = name + " is not in the install folder" }); continue; }
                try { grfs.Add(new GrfArchive(p)); }
                catch (GrfOpenException e) { o.Skipped.Add(new SkippedGrf { Name = name, Kind = e.Why.ToString(), Reason = e.Message }); }
                catch (IOException e) { o.Skipped.Add(new SkippedGrf { Name = name, Kind = "Busy", Reason = name + " cannot be opened (" + e.Message.Trim() + ")" }); }
            }
            o.Lookup = new RoLookup(root, grfs);
            var ci = o.Lookup.ReadClientInfo(out string ciName);
            o.ClientInfoName = ciName;
            if (ci != null) o.Lookup.LooseFirst = RoLookup.HasReadFolder(ci);
            if (log != null)
            {
                foreach (var n in o.Install.Notes) log(n);
                foreach (var k in o.Skipped) log("skipped: " + k.Reason);
                log(ci != null ? $"{ciName}: readfolder {(o.Lookup.LooseFirst ? "present: loose data\\ files first, then the archives" : "absent: the archives first, then loose data\\ files")}" : "no clientinfo in the game data: loose data\\ files first (as before)");
            }
            // warch2: the game's name table (IO/RoLookup.cs), read by the rule just set: every reader of this install gets its aliases
            o.Lookup.LoadNameTable(log);
            return o;
        }

        private static string Str(object v) => v is LuaStr s && s.B.Length > 0 ? s.Latin1 : null;

        private static string SafeCombine(string root, string rel)
        {
            try { return System.IO.Path.Combine(root, rel.Replace('\\', System.IO.Path.DirectorySeparatorChar)); } catch { return null; }
        }

        /// <summary>roBrowserLegacy FileManager.js:89-104: every "digits=name" of the text (regex /(\d+)=([^\s]+)/g, whatever the
        /// section or line), list[N] = name (a later line with the same N replaces the earlier), the empty slots removed: ascending N.</summary>
        public static List<string> ParseDataIni(string text)
        {
            var slots = new SortedDictionary<long, string>();
            foreach (Match m in Regex.Matches(text ?? "", @"(\d+)=([^\s]+)", RegexOptions.ECMAScript))
                if (long.TryParse(m.Groups[1].Value, out long n)) slots[n] = m.Groups[2].Value;
            return slots.Values.ToList();
        }

        /// <summary>The files of the install, besides the archives, whose change means the install changed (the stamp's "ini" /
        /// "patch" lines): DATA.INI and the patch marker.</summary>
        public IEnumerable<string> WatchedFiles()
        {
            if (HasDataIni) yield return DataIniName;
            if (Patch != null && Patch.Exists) yield return Patch.File;
        }
    }

    /// <summary>An archive of the install's order that is not read, and why. Kind: "missing", or GrfOpenException.Kind's name
    /// ("NotGrf", "UnknownVersion", "Damaged", "Busy").</summary>
    public sealed class SkippedGrf { public string Name, Kind, Reason; }

    /// <summary>The install as opened (<see cref="RoInstall.Open"/>): its description, the lookup over its open archives (in
    /// order) and loose folder, and what was skipped.</summary>
    public sealed class RoOpened
    {
        public RoInstall Install;
        public RoLookup Lookup;
        public readonly List<SkippedGrf> Skipped = new List<SkippedGrf>();
        /// <summary>The clientinfo that decided the loose folder's place ("data\\clientinfo.xml"), or null when the install has none.</summary>
        public string ClientInfoName;
    }

    /// <summary>The install's patch marker (see <see cref="RoInstall"/>): the number of the last patch the official patcher applied.</summary>
    public sealed class PatchMarker
    {
        public const string UsualName = "patch.inf", UsualList = "patch.txt";
        /// <summary>The marker's file name ("patch.inf"), relative to the install folder.</summary>
        public string File = UsualName;
        /// <summary>True when the patcher's own settings named the file (else it was looked for by its usual name).</summary>
        public bool NamedByPatcher;
        public bool Exists; public long Size = -1, Ticks;
        /// <summary>The patch number, or -1 when the install has no marker or its form is not known.</summary>
        public long Number = -1;
        /// <summary>The highest patch number of the patch list (the list the patcher downloaded last), or -1.</summary>
        public long ListLast = -1;
        public string ListFile = UsualList;
        /// <summary>Why the marker gives no number (null when it does).</summary>
        public string Problem;

        public static PatchMarker Read(string root, string infName, string listName)
        {
            var p = new PatchMarker { NamedByPatcher = !string.IsNullOrEmpty(infName) };
            if (!string.IsNullOrEmpty(infName)) p.File = infName;
            if (!string.IsNullOrEmpty(listName)) p.ListFile = listName;
            try
            {
                var fi = new FileInfo(System.IO.Path.Combine(root ?? "", p.File));
                if (!fi.Exists) { p.Problem = "the install has no " + p.File; }
                else
                {
                    p.Exists = true; p.Size = fi.Length; p.Ticks = fi.LastWriteTimeUtc.Ticks;
                    if (fi.Length == 4)
                    {
                        var b = System.IO.File.ReadAllBytes(fi.FullName);
                        if (b.Length == 4) p.Number = BitConverter.ToUInt32(b, 0); else p.Problem = p.File + " changed while it was read";
                    }
                    else p.Problem = $"{p.File} is {fi.Length} bytes: not the 4-byte form Loki knows";
                }
            }
            catch (Exception e) { p.Problem = p.File + " cannot be read: " + e.Message.Trim(); }
            try
            {
                string lp = System.IO.Path.Combine(root ?? "", p.ListFile);
                if (System.IO.File.Exists(lp)) p.ListLast = LastListNumber(Py.Latin1(System.IO.File.ReadAllBytes(lp)));
            }
            catch { p.ListLast = -1; }
            return p;
        }

        /// <summary>The highest number among the list's lines that start with digits ("1486 2026-09-15_live_client_….gpf"); a
        /// line behind "//" (a disabled entry) or any other text does not start with a digit and is passed over.</summary>
        public static long LastListNumber(string text)
        {
            long last = -1;
            foreach (var raw in (text ?? "").Split('\n'))
            {
                string line = raw.Trim();
                int n = 0;
                while (n < line.Length && line[n] >= '0' && line[n] <= '9') n++;
                if (n == 0 || n > 15 || (n < line.Length && line[n] != ' ' && line[n] != '\t')) continue;
                long v = long.Parse(line.Substring(0, n));
                if (v > last) last = v;
            }
            return last;
        }

        /// <summary>"patch 1486", or null when the install gives no number.</summary>
        public string Short => Number >= 0 ? "patch " + Number : null;
        /// <summary>True when the patch list goes further than the marker: the install is not fully patched (a patch stopped half-way).</summary>
        public bool Behind => Number >= 0 && ListLast > Number;

        /// <summary>The stamp's line: the number when known, else the marker's size and time (so a change still rebuilds).</summary>
        public string StampLine()
        {
            var sb = new StringBuilder("patch ");
            if (!Exists) sb.Append("none");
            else if (Number >= 0) sb.Append(Number).Append(' ').Append(File);
            else sb.Append("unknown ").Append(File).Append(' ').Append(Size).Append(' ').Append(Ticks);
            return sb.ToString();
        }
    }
}
