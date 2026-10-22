using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Loki.IO;

namespace Loki.Data
{
    /// <summary>
    /// Client wave 3b (w3data): builds every table the client needs from Gravity's data on the player's own PC, into the per-user
    /// data folder — never into the game's folder, and never shipped (release builds carry none of them: Editor/LokiSetup.cs).
    /// Each tool is a step-for-step port of the Python tool that made the table before (client/tools/clientdata, appearance,
    /// combat), so the files equal the Python tools' output byte for byte (design notes: the comparison).
    /// Runs on a worker thread: no Unity API in here (Loaders see the result through <c>ClientData.Path</c>).
    ///
    /// Client wave 7 (w7data): the player always keeps a good set.
    ///  • The tools write into data\staging; every table is checked there (<see cref="TableChecks"/>) and only then moved over
    ///    the old one. A tool that fails, or a table that does not pass, leaves the old table — the last good version — in
    ///    place: the game starts with it, <see cref="Tables"/> says which and why, and the next start tries again.
    ///  • The tools read the install as the game does: the archives in the install's own order and the loose data\ folder
    ///    where the install's clientinfo says so (<see cref="RoLookup"/>, <see cref="RoInstall"/>).
    ///  • Nothing is taken from files that change while they are read: the install's stamp is taken again after the tools ran,
    ///    and when it differs nothing is moved in (<see cref="InstallChanged"/>: the caller waits and runs again).
    /// The stamp (data/stamp.txt). Its head is what the tables were made from — the builder's version, the install folder, the
    /// order's source and the loose-folder rule, every archive of the order (size, time, version; or why it was skipped),
    /// DATA.INI, every install file the tools read (size, time), the patch marker, the build's parts (a digest) — and a change of
    /// any line rebuilds. After "--": when it was made, "Ragnarok Online data: patch N", then one line per table: "current"
    /// (made by this run), "kept" (carried over from the last good set, with the reason), "suspect" (made, did not pass, no
    /// better one exists) or "none"; the tools that failed; what was refused; and where each source file came from ("src": a
    /// loose file appearing, changing or going away rebuilds too).
    /// </summary>
    public sealed class ClientDataBuilder
    {
        /// <summary>Bump when a port changes what it writes.</summary>
        public const int Version = 6; // 3: client wave 4 merged — w4world (mapsky.tsv, navigation.tsv) + w4units (hateffects.tsv); 4: client wave 5 (w5ui) — the Portuguese skill / job name columns of skills.tsv and skilltree.tsv. (Client wave 7 changed the stamp, not the tables: the new stamp lines rebuild once.) 5: client wave 20 (w20rooms) — rooms.tsv, the indoor maps' rooms (RoomTools.cs). 6: wave 28 (wtextc) — the install's English item / quest texts (items.tsv columns 9-10, quests.tsv 6-8, from System\english\), the Portuguese status titles (efst.tsv column 5), the English map names of navigation.tsv (M column 7), zero-width spaces out

        /// <summary>Every file the builder makes (paths under data/): what release builds leave out and what the loaders read
        /// from the per-user folder first.</summary>
        public static readonly string[] GeneratedFiles =
        {
            "msgstrings.tsv", "mapnames.tsv", "skilltree.tsv", "skills.tsv", "randomoptions.tsv", "worldmap.tsv", "spritenames.txt",
            "efst.tsv", "items.tsv", "quests.tsv", "account/achievements_en.json", "account/achievements_pt.json", "account/titles.tsv",
            "emotions.tsv", "stateicons.tsv", "appearance.txt", "itemviews.tsv", "fx/mob_attack_fx.json",
            "mapsky.tsv", "navigation.tsv", // client wave 4 (w4world): WorldTools.cs
            "hateffects.tsv", // client wave 4 (w4units)
#if !LOKI_HARNESS // the offline harness (client/tools/w3data/harness: no Unity) has no room tool: its builder makes the 21 tables before it
            "rooms.tsv", // client wave 20 (w20rooms): RoomTools.cs (a literal: client/tools/w3data/compare.py and release/make-friends-package.ps1 read these names from this source)
#endif
        };

        /// <summary>The files of the install folder the tools read (besides the GRF).</summary>
        public static readonly string[] InstallFiles =
        {
            "System\\iteminfo_new.lub", "System\\OngoingQuestInfoList_True.lub", "System\\achievement_list.lub",
            "System\\english\\achievement_list.lub", "System\\itemInfo.lua",
            WorldTools.TownPt, WorldTools.TownEn, // client wave 4 (w4world): the town guide
            MixedTools.EnglishItemsFile, MixedTools.EnglishQuestsFile, WorldTools.MapInfoEn, // wave 28 (wtextc): the install's English tables ("missing" in the stamp when it has none)
        };

        public sealed class Step { public string Name, Tool; public string[] Files; }

        /// <summary>The tools in the order they run (build_qol_tables.py reads the msgstrings.tsv build_texttables.py made).</summary>
        public static readonly Step[] Steps =
        {
            new Step { Name = "Messages and map names", Tool = "build_texttables.py", Files = new[] { "msgstrings.tsv", "mapnames.tsv" } },
            new Step { Name = "Skills and skill trees", Tool = "build_skilltree.py", Files = new[] { "skilltree.tsv", "skills.tsv" } },
            new Step { Name = "Random options", Tool = "build_equip_tables.py", Files = new[] { "randomoptions.tsv" } },
            new Step { Name = "World map", Tool = "build_worldmap.py", Files = new[] { "worldmap.tsv" } },
            new Step { Name = "Sprite names", Tool = "build_spritenames.py", Files = new[] { "spritenames.txt" } },
            new Step { Name = "Status icons", Tool = "build_efst.py", Files = new[] { "efst.tsv" } },
            new Step { Name = "Items and quests", Tool = "build_tables.py", Files = new[] { "items.tsv", "quests.tsv" } },
            new Step { Name = "Achievements and titles", Tool = "build_account_tables.py", Files = new[] { "account/achievements_en.json", "account/achievements_pt.json", "account/titles.tsv" } },
            new Step { Name = "Emotes and buff icons", Tool = "build_qol_tables.py", Files = new[] { "emotions.tsv", "stateicons.tsv" } },
            new Step { Name = "Equipment looks", Tool = "build_appearance.py", Files = new[] { "appearance.txt", "itemviews.tsv" } },
            new Step { Name = "Monster attack effects", Tool = "build_combat_data.py", Files = new[] { "fx/mob_attack_fx.json" } },
            new Step { Name = "Sky and navigation", Tool = "build_world_tables.py", Files = new[] { "mapsky.tsv", "navigation.tsv" } }, // client wave 4 (w4world)
            new Step { Name = "Hat effects", Tool = "build_hateffects.py", Files = new[] { "hateffects.tsv" } }, // client wave 4 (w4units)
            // client wave 20 (w20rooms), point 54: no Python tool before it (Loki's own; Data/RoomTools.cs); after "Sky and navigation": it reads navigation.tsv
#if !LOKI_HARNESS
            new Step { Name = "Indoor rooms", Tool = "RoomTools.cs", Files = new[] { "rooms.tsv" } },
#endif
        };

        // ------------------------------------------------------------------ the install as the builder sees it (client wave 7)
        /// <summary>What the tables are made from: the install's own description, its open archives in order (the client's own,
        /// thread-safe reads), the ones skipped, and the parts the build ships.</summary>
        public sealed class Source
        {
            public RoInstall Install;
            public RoLookup Lookup;
            public readonly List<SkippedGrf> Skipped = new List<SkippedGrf>();
            /// <summary>StreamingAssets/data/firstrun and StreamingAssets/data/fx/effect_table.json.</summary>
            public string PartsDir, EffectTable;
            public string Root => Lookup.Root;
            public IList<GrfArchive> Grfs => Lookup.Grfs;

            /// <summary>Opens the install for a tool run outside the game (the harness): the archives of the install's order,
            /// skipping the ones that cannot be opened, and the clientinfo's readfolder rule.</summary>
            public static Source Open(string root, string[] configuredGrfs, string partsDir, string effectTable, Action<string> log = null)
            {
                var o = RoInstall.Open(root, configuredGrfs, log); // the one place the install is opened (Data/RoInstall.cs)
                var s = new Source { Install = o.Install, Lookup = o.Lookup, PartsDir = partsDir, EffectTable = effectTable };
                s.Skipped.AddRange(o.Skipped);
                return s;
            }
        }

        // ------------------------------------------------------------------ inputs
        /// <summary>The player's install as the tools read it: entries by grf.py's normalisation (A-Z lower-cased, '/' → '\',
        /// nothing folded), looked up as the game does (<see cref="RoLookup"/>: the loose data\ folder and the archives in the
        /// install's order). The Python tools open data.grf alone: on an install with one archive and no loose data\ folder (the
        /// LATAM one) the result is the same, byte for byte.</summary>
        public sealed class RoInputs : IToolInputs
        {
            private readonly string _root, _parts; private readonly RoLookup _lookup;
            public readonly List<string> Read = new List<string>();
            /// <summary>Every game-data path a tool asked for, and where it came from ("loose", an archive's name, or null).</summary>
            public readonly Dictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.Ordinal);
            public RoInputs(string root, IList<GrfArchive> grfs, string partsDir) : this(new RoLookup(root, grfs), partsDir) { }
            public RoInputs(RoLookup lookup, string partsDir)
            {
                _lookup = lookup; _root = lookup.Root; _parts = partsDir;
                var grfs = lookup.Grfs;
                GrfName = grfs.Count > 0 ? System.IO.Path.GetFileName(grfs[0].Path) : "";
                GrfVersion = grfs.Count > 0 ? (int)grfs[0].Version : 0;
            }
            public string GrfName { get; }
            public int GrfVersion { get; }
            public static string Normalize(string p)
            {
                var c = p.ToCharArray();
                for (int i = 0; i < c.Length; i++) { if (c[i] >= 'A' && c[i] <= 'Z') c[i] = (char)(c[i] + 32); else if (c[i] == '/') c[i] = '\\'; }
                return new string(c);
            }
            public byte[] Grf(string roPath)
            {
                string n = Normalize(roPath);
                var b = _lookup.Read(n, out string src); // a damaged entry throws: the tool's step fails with its name
                lock (Sources) Sources[n] = src;
                return b;
            }
            public bool Exists(string roPath) => _lookup.Exists(Normalize(roPath)); // client wave 20 (w20rooms): RoomTools' copies (not a source of a table: not recorded)
            public byte[] RoFile(string rel)
            {
                string p = System.IO.Path.Combine(_root, rel.Replace('\\', System.IO.Path.DirectorySeparatorChar));
                if (!File.Exists(p)) return null;
                lock (Read) Read.Add(rel);
                return File.ReadAllBytes(p);
            }
            public byte[] Part(string name)
            {
                string p = System.IO.Path.Combine(_parts, name);
                return File.Exists(p) ? File.ReadAllBytes(p) : null;
            }
        }

        // ------------------------------------------------------------------ what happened to each table (client wave 7)
        public enum TableState
        {
            /// <summary>Made by this run and checked.</summary>
            Current,
            /// <summary>This run's version failed or was refused: the last good one stays.</summary>
            Kept,
            /// <summary>Made, refused by a check, and no good version exists: used until a better one can be made.</summary>
            Suspect,
            /// <summary>Not made and no earlier version exists.</summary>
            None,
        }

        public sealed class TableOutcome
        {
            public string File, Tool, StepName, Reason = "";
            public TableState State;
            public long Size; public string Sha256 = ""; public int Rows = -1;
        }

        // ------------------------------------------------------------------ state for the panel
        public volatile int StepIndex = -1;
        public volatile string StepName = "";
        public volatile bool Done;
        public readonly List<string> Log = new List<string>();
        /// <summary>The tools whose tables are not all current (failed, or made a table that was refused).</summary>
        public readonly List<string> Failed = new List<string>();
        /// <summary>The tables this run made current: size and SHA-256.</summary>
        public readonly Dictionary<string, (long size, string sha256)> Written = new Dictionary<string, (long, string)>();
        /// <summary>Every generated table, in <see cref="GeneratedFiles"/>' order: current, kept, suspect or none (after <see cref="Done"/>).</summary>
        public readonly List<TableOutcome> Tables = new List<TableOutcome>();
        /// <summary>True when the game files changed while the tools read them: nothing was moved in; wait and run again.</summary>
        public volatile bool InstallChanged;
        public long ElapsedMs;
        public Exception Fatal;
        private readonly Action<string> _log;

        private readonly Source _src; private readonly string _outDir;

        /// <param name="src">the install (its description, open archives, the build's parts)</param>
        /// <param name="outDir">the per-user data folder</param>
        public ClientDataBuilder(Source src, string outDir, Action<string> log) { _src = src; _outDir = outDir; _log = log; }

        private void L(string s) { lock (Log) Log.Add(s); _log?.Invoke("[DataGen] " + s); }

        // ------------------------------------------------------------------ the stamp
        public static string NormRoot(string root)
        {
            try { return System.IO.Path.GetFullPath(root ?? "").TrimEnd('\\', '/').ToLowerInvariant(); } catch { return (root ?? "").ToLowerInvariant(); }
        }

        /// <summary>The stamp of the current install (the head of stamp.txt): see the class comment.</summary>
        public static string CurrentStamp(Source src)
        {
            var sb = new StringBuilder();
            string root = src.Root;
            sb.Append("builder ").Append(Version).Append('\n');
            sb.Append("root ").Append(NormRoot(root)).Append('\n');
            sb.Append("order ").Append(src.Install.OrderSource).Append("; loose data folder ").Append(!src.Lookup.HasLooseFolder ? "none" : src.Lookup.LooseFirst ? "first" : "last").Append('\n');
            foreach (var name in src.Install.GrfNames)
            {
                var g = src.Grfs.FirstOrDefault(a => string.Equals(System.IO.Path.GetFileName(a.Path), System.IO.Path.GetFileName(name), StringComparison.OrdinalIgnoreCase));
                var fi = new FileInfo(System.IO.Path.Combine(root, name));
                sb.Append("grf ").Append(name).Append(' ');
                if (g != null) sb.Append(fi.Length).Append(' ').Append(fi.LastWriteTimeUtc.Ticks).Append(" 0x").Append(g.Version.ToString("x"));
                else
                {
                    var k = src.Skipped.FirstOrDefault(x => x.Name == name);
                    sb.Append("skipped ").Append(k != null ? k.Kind : "unopened").Append(' ').Append(fi.Exists ? fi.Length + " " + fi.LastWriteTimeUtc.Ticks : "missing");
                }
                sb.Append('\n');
            }
            if (src.Install.HasDataIni) sb.Append("ini ").Append(RoInstall.DataIniName).Append(' ').Append(FileLine(root, RoInstall.DataIniName)).Append('\n');
            foreach (var f in InstallFiles) sb.Append("file ").Append(f).Append(' ').Append(FileLine(root, f)).Append('\n');
            sb.Append(src.Install.Patch.StampLine()).Append('\n');
            sb.Append("parts ").Append(PartsDigest(src.PartsDir)).Append('\n');
            return sb.ToString();
        }

        private static string FileLine(string root, string rel)
        {
            var fi = new FileInfo(System.IO.Path.Combine(root, rel.Replace('\\', System.IO.Path.DirectorySeparatorChar)));
            return fi.Exists ? fi.Length + " " + fi.LastWriteTimeUtc.Ticks : "missing";
        }

        /// <summary>Every file of the install the stamp covers (full paths): what must be quiet before a build.</summary>
        public static List<string> WatchedPaths(string root, RoInstall install)
        {
            var l = new List<string>();
            foreach (var name in install.GrfNames) l.Add(System.IO.Path.Combine(root, name));
            foreach (var f in install.WatchedFiles()) l.Add(System.IO.Path.Combine(root, f));
            foreach (var f in InstallFiles) l.Add(System.IO.Path.Combine(root, f.Replace('\\', System.IO.Path.DirectorySeparatorChar)));
            return l;
        }

        /// <summary>The archives among <see cref="WatchedPaths"/> (full paths): the files another program may hold open.</summary>
        public static List<string> ArchivePaths(string root, RoInstall install)
        {
            var l = new List<string>();
            foreach (var name in install.GrfNames) l.Add(System.IO.Path.Combine(root, name));
            return l;
        }

        private static string PartsDigest(string dir)
        {
            if (!Directory.Exists(dir)) return "missing";
            using (var sha = SHA256.Create())
            {
                foreach (var f in Directory.GetFiles(dir).Where(x => !x.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)).OrderBy(x => System.IO.Path.GetFileName(x), StringComparer.Ordinal))
                {
                    var name = Encoding.UTF8.GetBytes(System.IO.Path.GetFileName(f));
                    sha.TransformBlock(name, 0, name.Length, null, 0);
                    var b = File.ReadAllBytes(f);
                    sha.TransformBlock(b, 0, b.Length, null, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return Hex(sha.Hash);
            }
        }

        public static string StampPath(string outDir) => System.IO.Path.Combine(outDir, "stamp.txt");
        public static string StagingDir(string outDir) => System.IO.Path.Combine(outDir, "staging");
        public static string RefusedDir(string outDir) => System.IO.Path.Combine(outDir, "refused");

        private static string ReadStamp(string outDir)
        {
            try { string sp = StampPath(outDir); return File.Exists(sp) ? File.ReadAllText(sp) : null; } catch { return null; }
        }

        private static IEnumerable<string> TailLines(string stampText)
        {
            if (stampText == null) yield break;
            int cut = stampText.IndexOf("\n--\n", StringComparison.Ordinal);
            if (cut < 0) yield break;
            foreach (var l in stampText.Substring(cut + 4).Split('\n')) if (l.Length > 0) yield return l;
        }

        /// <summary>True when the per-user folder holds every table, made from exactly this install (and no step failed).</summary>
        public static bool UpToDate(string outDir, string stamp, Source src, out string why)
        {
            string sp = StampPath(outDir);
            if (!File.Exists(sp)) { why = "no tables made yet"; return false; }
            string old;
            try { old = File.ReadAllText(sp); } catch (Exception e) { why = "stamp unreadable: " + e.Message; return false; }
            int cut = old.IndexOf("\n--\n", StringComparison.Ordinal);
            string head = cut >= 0 ? old.Substring(0, cut + 1) : old;
            if (head != stamp) { why = "the game data changed since the tables were made"; return false; }
            if (cut >= 0 && old.IndexOf("\nfailed ", cut, StringComparison.Ordinal) >= 0) { why = "a table failed last time"; return false; }
            foreach (var f in GeneratedFiles)
                if (!File.Exists(System.IO.Path.Combine(outDir, f.Replace('/', System.IO.Path.DirectorySeparatorChar)))) { why = "missing " + f; return false; }
            // the loose data\ folder: a source file that appeared, changed or went away since the tables were made
            foreach (var l in TailLines(old))
            {
                if (!l.StartsWith("src ", StringComparison.Ordinal)) continue;
                int bar = l.IndexOf(" | ", StringComparison.Ordinal); if (bar < 0) continue;
                string path = l.Substring(4, bar - 4), was = l.Substring(bar + 3);
                if (src != null && SourceState(src.Lookup, path) != was) { why = "a loose game file changed (" + path + ")"; return false; }
            }
            why = null; return true;
        }

        /// <summary>Where the game reads a file from, for the stamp: "loose size ticks" when a loose file is what the lookup gives
        /// (<see cref="RoLookup"/>'s rule), else "grf name" of the first archive holding it, else "missing".</summary>
        private static string SourceState(RoLookup lookup, string normalized)
        {
            string grf = null;
            foreach (var g in lookup.Grfs) if (g.Contains(normalized)) { grf = System.IO.Path.GetFileName(g.Path); break; }
            if (lookup.LooseExists(normalized) && (lookup.LooseFirst || grf == null))
            {
                try { var fi = new FileInfo(lookup.LoosePath(normalized)); return "loose " + fi.Length + " " + fi.LastWriteTimeUtc.Ticks; } catch { return "loose ?"; }
            }
            return grf != null ? "grf " + grf : "missing";
        }

        // ------------------------------------------------------------------ the run
        /// <summary>Runs every tool into the staging folder (a failing one is logged and skipped), checks every table, takes
        /// the install's stamp again, then moves the good tables in and writes the stamp. See the class comment.</summary>
        public void Run(string stamp)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                Directory.CreateDirectory(_outDir);
                // each process stages in its own folder: two clients of one Windows user (the headset and the playtest buddy share
                // the per-user folder) may both find the tables out of date and build at the same moment
                string staging = System.IO.Path.Combine(StagingDir(_outDir), Process.GetCurrentProcess().Id.ToString());
                ClearStale(StagingDir(_outDir));
                ClearFolder(staging);
                string oldStamp = ReadStamp(_outDir);
                // the last good set is compared with only when it was made from this install folder (an older stamp has no root line)
                string oldRoot = null; var refusedBefore = new Dictionary<string, string>(StringComparer.Ordinal);
                if (oldStamp != null) foreach (var l in oldStamp.Split('\n')) if (l.StartsWith("root ", StringComparison.Ordinal)) { oldRoot = l.Substring(5); break; }
                bool sameInstall = oldStamp != null && (oldRoot == null || oldRoot == NormRoot(_src.Root));
                foreach (var l in TailLines(oldStamp))
                    if (l.StartsWith("refused ", StringComparison.Ordinal)) { var c = l.Split(' '); if (c.Length >= 4 && c[3] == "relative") refusedBefore[c[1]] = c[2]; }

                var inp = new RoInputs(_src.Lookup, _src.PartsDir);
                Py.Cp949 cp949 = null;
                var cpBytes = inp.Part("cp949.bin");
                if (cpBytes != null) cp949 = new Py.Cp949(cpBytes);
                byte[] msgstrings = null, navigation = null;
                var good = new List<(Step step, List<KeyValuePair<string, byte[]>> files, List<TableChecks.Result> checks)>();
                var outcome = new Dictionary<string, TableOutcome>(StringComparer.Ordinal);
                var refused = new List<string>();
                for (int i = 0; i < Steps.Length; i++)
                {
                    var st = Steps[i];
                    StepIndex = i; StepName = st.Name;
                    var t0 = sw.ElapsedMilliseconds;
                    ToolOutput o = null; string toolProblem = null;
                    try
                    {
                        switch (st.Tool)
                        {
                            case "build_texttables.py": o = GrfTools.TextTables(inp); break;
                            case "build_skilltree.py": o = GrfTools.SkillTree(inp); break;
                            case "build_equip_tables.py": o = GrfTools.EquipTables(inp); break;
                            case "build_worldmap.py": o = GrfTools.WorldMap(inp); break;
                            case "build_spritenames.py": o = GrfTools.SpriteNames(inp); break;
                            case "build_efst.py": o = GrfTools.Efst(inp); break;
                            case "build_tables.py": o = MixedTools.ItemsQuests(inp, cp949 ?? throw new ToolError("missing part of the build: firstrun/cp949.bin")); break;
                            case "build_account_tables.py": o = MixedTools.Account(inp, cp949 ?? throw new ToolError("missing part of the build: firstrun/cp949.bin")); break;
                            case "build_qol_tables.py":
                                if (msgstrings == null)
                                {
                                    // the messages' tool did not make a good table this run: its last good one stands in, so the emotes
                                    // and buff icons are not lost with it (client wave 7)
                                    string keptMsg = System.IO.Path.Combine(_outDir, "msgstrings.tsv");
                                    if (File.Exists(keptMsg)) { msgstrings = File.ReadAllBytes(keptMsg); L("build_qol_tables.py: reading the last good msgstrings.tsv (this run made none)"); }
                                    else throw new ToolError("msgstrings.tsv was not made (build_texttables.py failed)");
                                }
                                o = MixedTools.Qol(inp, msgstrings); break;
                            case "build_appearance.py": o = MixedTools.Appearance(inp); break;
                            case "build_combat_data.py":
                                if (!File.Exists(_src.EffectTable)) throw new ToolError("missing " + _src.EffectTable);
                                o = MixedTools.CombatFx(inp, File.ReadAllBytes(_src.EffectTable)); break;
                            case "build_world_tables.py": o = WorldTools.Tables(inp); break; // client wave 4 (w4world)
                            case "build_hateffects.py": o = W4HatEffectTool.HatEffects(inp); break; // client wave 4 (w4units)
#if !LOKI_HARNESS
                            case "RoomTools.cs": // client wave 20 (w20rooms): the interiors by navigation.tsv's map types; this run's table, else the last good one (as the emotes' step above)
                                if (navigation == null)
                                {
                                    string keptNav = System.IO.Path.Combine(_outDir, "navigation.tsv");
                                    if (File.Exists(keptNav)) { navigation = File.ReadAllBytes(keptNav); L("RoomTools.cs: reading the last good navigation.tsv (this run made none)"); }
                                    else throw new ToolError("navigation.tsv was not made (build_world_tables.py failed)");
                                }
                                o = RoomTools.Rooms(inp, navigation); break;
#endif
                            default: throw new ToolError("unknown tool " + st.Tool);
                        }
                    }
                    catch (Exception e)
                    {
                        toolProblem = e.GetType().Name + ": " + OneLine(e.Message);
                        // a reader that ran off the end of its file says so in its own words ("Source array was not long enough"): name what it means
                        if (!(e is ToolError) && !(e is LuaError) && !(e is GrfEntryException)) toolProblem = "a game file this table is made from is cut off or damaged (" + toolProblem + ")";
                    }

                    if (toolProblem == null)
                    {
                        // every table of the tool goes through the staging folder and is checked as it reads back from there
                        var files = new List<KeyValuePair<string, byte[]>>(); var checks = new List<TableChecks.Result>();
                        string firstBad = null;
                        foreach (var f in st.Files)
                        {
                            byte[] made = null;
                            foreach (var kv in o.Files) if (kv.Key == f) made = kv.Value;
                            if (made == null) { firstBad = firstBad ?? f + ": the tool did not make it"; files.Add(new KeyValuePair<string, byte[]>(f, null)); checks.Add(new TableChecks.Result { Problem = "the tool did not make it" }); continue; }
                            string sp = System.IO.Path.Combine(staging, f.Replace('/', System.IO.Path.DirectorySeparatorChar));
                            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(sp));
                            File.WriteAllBytes(sp, made);
                            byte[] back = File.ReadAllBytes(sp);
                            int lastRows = -1;
                            string cur = System.IO.Path.Combine(_outDir, f.Replace('/', System.IO.Path.DirectorySeparatorChar));
                            if (sameInstall && File.Exists(cur)) { try { lastRows = TableChecks.CountRows(f, File.ReadAllBytes(cur)); } catch { lastRows = -1; } }
                            var res = TableChecks.Check(f, back, lastRows);
                            if (!res.Ok && res.OnlyRelative && refusedBefore.TryGetValue(f, out string shaBefore) && shaBefore == Sha(back))
                            {
                                L($"{f}: {res.Problem} — the same table as at the last start: accepted (it is what the game files hold)");
                                res.Ok = true; res.Problem = null;
                            }
                            if (!res.Ok) firstBad = firstBad ?? f + ": " + res.Problem;
                            files.Add(new KeyValuePair<string, byte[]>(f, back)); checks.Add(res);
                        }
                        if (firstBad == null)
                        {
                            good.Add((st, files, checks));
                            if (st.Tool == "build_texttables.py") foreach (var kv in files) if (kv.Key == "msgstrings.tsv") msgstrings = kv.Value;
                            if (st.Tool == "build_world_tables.py") foreach (var kv in files) if (kv.Key == "navigation.tsv") navigation = kv.Value; // client wave 20 (w20rooms)
                            int badRows = checks.Sum(c => c.BadRows);
                            L($"{st.Tool}: {o.Summary} ({sw.ElapsedMilliseconds - t0} ms){(badRows > 0 ? $"; {badRows} malformed row(s) the loaders skip" : "")}");
                            continue;
                        }
                        toolProblem = "refused: " + firstBad;
                        for (int k = 0; k < files.Count; k++)
                        {
                            if (files[k].Value == null) continue;
                            // what was made and refused is kept beside the tables, for a look at what went wrong
                            try
                            {
                                string rp = System.IO.Path.Combine(RefusedDir(_outDir), files[k].Key.Replace('/', System.IO.Path.DirectorySeparatorChar));
                                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(rp));
                                File.WriteAllBytes(rp, files[k].Value);
                            }
                            catch { }
                            if (!checks[k].Ok) refused.Add($"refused {files[k].Key} {Sha(files[k].Value)} {(checks[k].OnlyRelative ? "relative" : "check")} | {checks[k].Problem}");
                        }
                        // no good version of a table at all: the refused one is used when it parses (better than none), marked suspect
                        for (int k = 0; k < files.Count; k++)
                        {
                            string cur = System.IO.Path.Combine(_outDir, files[k].Key.Replace('/', System.IO.Path.DirectorySeparatorChar));
                            if (files[k].Value == null || File.Exists(cur)) continue;
                            bool parses = checks[k].Ok || checks[k].Rows > 0;
                            if (!parses) continue;
                            outcome[files[k].Key] = new TableOutcome { File = files[k].Key, Tool = st.Tool, StepName = st.Name, State = TableState.Suspect, Reason = checks[k].Ok ? "made with a table that was refused (" + firstBad + ")" : checks[k].Problem, Rows = checks[k].Rows };
                        }
                        var suspects = files.Where(kv => outcome.ContainsKey(kv.Key)).ToList();
                        if (suspects.Count > 0) good.Add((st, suspects, null));
                    }
                    lock (Failed) Failed.Add(st.Tool);
                    L($"{st.Tool} FAILED: {toolProblem}");
                    foreach (var f in st.Files)
                        if (!outcome.ContainsKey(f)) outcome[f] = new TableOutcome { File = f, Tool = st.Tool, StepName = st.Name, State = TableState.Kept, Reason = toolProblem };
                }

                // the game files must be the ones the stamp was taken from: nothing is built from files that changed meanwhile
                string after;
                try { after = CurrentStamp(_src); } catch (Exception e) { after = "unreadable: " + e.Message; }
                if (after != stamp)
                {
                    InstallChanged = true;
                    L("the game files changed while the tables were being made: nothing was kept from this run");
                    ClearFolder(staging); RemoveEmpty(staging);
                    return;
                }

                // ---- swap in: each good table moves from the staging folder over the old one
                foreach (var g in good)
                    foreach (var kv in g.files)
                    {
                        string from = System.IO.Path.Combine(staging, kv.Key.Replace('/', System.IO.Path.DirectorySeparatorChar));
                        string to = System.IO.Path.Combine(_outDir, kv.Key.Replace('/', System.IO.Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to));
                        MoveOver(from, to);
                        string sha = Sha(kv.Value);
                        if (outcome.TryGetValue(kv.Key, out var sus)) { sus.Size = kv.Value.LongLength; sus.Sha256 = sha; continue; }
                        lock (Written) Written[kv.Key] = (kv.Value.LongLength, sha);
                        int rows = g.checks != null ? g.checks[g.files.FindIndex(x => x.Key == kv.Key)].Rows : -1;
                        outcome[kv.Key] = new TableOutcome { File = kv.Key, Tool = g.step.Tool, StepName = g.step.Name, State = TableState.Current, Size = kv.Value.LongLength, Sha256 = sha, Rows = rows };
                    }
                ClearFolder(staging); RemoveEmpty(staging);

                // ---- the tables kept from the last good set
                foreach (var f in GeneratedFiles)
                {
                    if (!outcome.TryGetValue(f, out var oc)) { oc = new TableOutcome { File = f, State = TableState.Kept, Reason = "not made" }; outcome[f] = oc; }
                    if (oc.State == TableState.Kept)
                    {
                        string cur = System.IO.Path.Combine(_outDir, f.Replace('/', System.IO.Path.DirectorySeparatorChar));
                        if (!File.Exists(cur)) oc.State = TableState.None;
                        else
                        {
                            try { var b = File.ReadAllBytes(cur); oc.Size = b.LongLength; oc.Sha256 = Sha(b); oc.Rows = TableChecks.CountRows(f, b); }
                            catch (IOException) { oc.Size = -1; oc.Sha256 = "unread"; } // another client of this user is writing it this instant
                        }
                    }
                    lock (Tables) Tables.Add(oc);
                }

                var sb = new StringBuilder(stamp).Append("--\n");
                sb.Append("made ").Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")).Append(" UTC in ").Append(sw.ElapsedMilliseconds).Append(" ms\n");
                var pm = _src.Install.Patch;
                sb.Append("data Ragnarok Online data: ").Append(pm.Short ?? "no patch marker (" + (pm.Problem ?? "none") + ")");
                if (pm.Behind) sb.Append(" of ").Append(pm.ListLast).Append(" in ").Append(pm.ListFile);
                sb.Append('\n');
                foreach (var f in Failed) sb.Append("failed ").Append(f).Append('\n');
                foreach (var oc in Tables.OrderBy(t => t.File, StringComparer.Ordinal))
                {
                    if (oc.State == TableState.None) { sb.Append("none ").Append(oc.File).Append(" | ").Append(oc.Reason).Append('\n'); continue; }
                    sb.Append(oc.State == TableState.Current ? "current " : oc.State == TableState.Kept ? "kept " : "suspect ");
                    sb.Append(oc.File).Append(' ').Append(oc.Size).Append(' ').Append(oc.Sha256).Append(' ').Append(oc.Rows);
                    if (oc.State != TableState.Current) sb.Append(" | ").Append(oc.Reason);
                    sb.Append('\n');
                }
                foreach (var r in refused) sb.Append(r).Append('\n');
                lock (inp.Sources)
                    foreach (var kv in inp.Sources.OrderBy(k => k.Key, StringComparer.Ordinal))
                    {
                        // only what the loose folder can change is recorded: without a data\ folder there is nothing to watch
                        if (!_src.Lookup.HasLooseFolder) break;
                        sb.Append("src ").Append(kv.Key).Append(" | ").Append(SourceState(_src.Lookup, kv.Key)).Append('\n');
                    }
                WriteAtomic(StampPath(_outDir), Encoding.UTF8.GetBytes(sb.ToString()));
            }
            catch (Exception e) { Fatal = e; L("FATAL: " + e); }
            finally { ElapsedMs = sw.ElapsedMilliseconds; StepIndex = Steps.Length; Done = true; }
        }

        private static string OneLine(string s) => (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();

        private static string Sha(byte[] data) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(data)); }

        /// <summary>Empties the builder's own staging folder (its temporary files only).</summary>
        private static void ClearFolder(string dir)
        {
            if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); return; }
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) { try { File.Delete(f); } catch { } }
        }

        /// <summary>Files left in other processes' staging folders for more than a day (a client that was stopped mid-build).</summary>
        private static void ClearStale(string stagingRoot)
        {
            try
            {
                if (!Directory.Exists(stagingRoot)) return;
                foreach (var d in Directory.GetDirectories(stagingRoot))
                    if (Directory.GetLastWriteTimeUtc(d) < DateTime.UtcNow.AddDays(-1)) { ClearFolder(d); RemoveEmpty(d); }
            }
            catch { }
        }

        private static void RemoveEmpty(string dir)
        {
            try
            {
                foreach (var d in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length)) Directory.Delete(d, false);
                Directory.Delete(dir, false);
            }
            catch { } // not empty (a file could not be removed) or in use: it stays
        }

        /// <summary>Moves a staged file over the table in place (one step for a reader: the old file or the new one, never half).
        /// Another client of the same Windows user may be moving the same table in at this instant: a few tries.</summary>
        private static void MoveOver(string from, string to)
        {
            for (int attempt = 1; ; attempt++)
            {
                try { if (File.Exists(to)) File.Replace(from, to, null); else File.Move(from, to); return; }
                catch (IOException) when (attempt < 6) { System.Threading.Thread.Sleep(60 * attempt); }
                catch (UnauthorizedAccessException) when (attempt < 6) { System.Threading.Thread.Sleep(60 * attempt); }
            }
        }

        /// <summary>A temporary file (this process's own), then moved over the old one: a crash never leaves half a file.</summary>
        private static void WriteAtomic(string path, byte[] data)
        {
            string tmp = path + "." + Process.GetCurrentProcess().Id + ".tmp";
            File.WriteAllBytes(tmp, data);
            MoveOver(tmp, path);
        }

        public static string Hex(byte[] b) => Py.Hex(b);
    }
}
