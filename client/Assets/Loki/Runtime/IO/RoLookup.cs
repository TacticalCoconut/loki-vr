using System;
using System.Collections.Generic;
using System.IO;

namespace Loki.IO
{
    /// <summary>
    /// Client wave 7 (w7data): where one file of the game data is read from — the rule shared by the client's file system
    /// (IO/RoFileSystem.cs) and the first-run builder (Data/ClientDataBuilder.cs), so both follow the player's install the same way.
    ///
    /// The archives: in the install's own order (Data/RoInstall.cs: DATA.INI when the install has one), first hit wins
    /// (roBrowserLegacy src/Core/FileManager.js:224-231: "for i = 0 … if (fileList[i].getFile(path, callback)) return").
    ///
    /// The loose data\ folder: the install's clientinfo decides. "&lt;readfolder&gt;: when present, all files are first searched
    /// inside the /data/ folder, then inside the GRF archives. Otherwise files inside the /data/ folder are only accessed when
    /// they were not found inside the GRF archives" (korangar korangar/src/loaders/server/client_info.rs:63-68, a
    /// reimplementation's description of the official tag; the only source on this PC — Ragexe.exe is Themida-packed). The LATAM
    /// install's own data\clientinfo.xml (inside data.grf) carries &lt;readfolder&gt;&lt;/readfolder&gt;: loose files first, which
    /// is what Loki's file system did before this wave for every install.
    ///
    /// A loose file's path is the RO path under the install folder with the entry's bytes as Latin-1 characters, as before this wave.
    ///
    /// warch2 (2026-10-15, design note: "add the alias support"): the game's name table, data\resnametable.txt ("alias pathname#real
    /// pathname#", one row per line; rAthena wiki "Resnametable": "pathname aliases for client-side files, regardless of whether
    /// inside or outside a GRF archive ... available for any kind of file"; paths relative to data\ for gat / gnd / rsw and to
    /// data\texture\ for bmp). The EXACT path is read first; the alias is the fallback, one step, the first row of a name wins (the
    /// original client's CResMgr::Get / GetRealResName as goro PR #56 restates them: "Direct-resource priority", "the first
    /// definition of a duplicate key wins"); a row naming itself changes nothing. Every reader gets it: maps the server names with
    /// no files of their own (new_1-1 -> new_zone01, pvp_y_1-1 -> prontera, guild_vs1-1 -> guild_vs1), their minimaps, any file.
    /// Pure C# (no UnityEngine).
    /// </summary>
    public sealed class RoLookup
    {
        public readonly string Root;
        public readonly IList<GrfArchive> Grfs;
        /// <summary>The install's &lt;readfolder&gt;: loose data\ files before the archives (else after them).</summary>
        public bool LooseFirst = true;
        /// <summary>False when the install has no data\ folder: no loose lookup is made at all.</summary>
        public readonly bool HasLooseFolder;
        /// <summary>The name table's rows (normalized alias -> normalized real name, relative to its base folder; rows naming
        /// themselves left out), empty until <see cref="LoadNameTable"/>.</summary>
        public IReadOnlyDictionary<string, string> Aliases => _aliases;
        private readonly Dictionary<string, string> _aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>The name table's file (data\resnametable.txt) and the base folders its names relate to, the most specific first
        /// (rAthena wiki "Resnametable", Known Base Paths: data\texture\ for bmp, data\ for gat / gnd / rsw).</summary>
        public const string NameTable = "data\\resnametable.txt";
        private static readonly string[] AliasBases = { "data\\texture\\", "data\\" };

        public RoLookup(string root, IList<GrfArchive> grfs)
        {
            Root = root ?? ""; Grfs = grfs;
            try { HasLooseFolder = Directory.Exists(System.IO.Path.Combine(Root, "data")); } catch { HasLooseFolder = false; }
        }

        public string LoosePath(string normalized) => System.IO.Path.Combine(Root, normalized.Replace('\\', System.IO.Path.DirectorySeparatorChar));

        public bool LooseExists(string normalized)
        {
            if (!HasLooseFolder) return false;
            try { return File.Exists(LoosePath(normalized)); } catch { return false; } // a name Windows cannot hold is not on disk
        }

        public bool Exists(string normalized)
        {
            if (ExistsExact(normalized)) return true;
            string real = Alias(normalized);
            return real != null && ExistsExact(real);
        }

        private bool ExistsExact(string normalized)
        {
            if (LooseExists(normalized)) return true;
            foreach (var g in Grfs) if (g.Contains(normalized)) return true;
            return false;
        }

        /// <summary>The name table's real path for a normalized path (its base folder kept), or null when no row names it.</summary>
        public string Alias(string normalized)
        {
            if (_aliases.Count == 0 || normalized == null) return null;
            foreach (var b in AliasBases)
                if (normalized.StartsWith(b, StringComparison.Ordinal) && _aliases.TryGetValue(normalized.Substring(b.Length), out var real)) return b + real;
            return null;
        }

        /// <summary>The path a read of <paramref name="normalized"/> takes: itself when it exists, else the name table's real path
        /// when that exists, else itself (missing).</summary>
        public string RealPath(string normalized)
        {
            if (ExistsExact(normalized)) return normalized;
            string real = Alias(normalized);
            return real != null && ExistsExact(real) ? real : normalized;
        }

        /// <summary>Reads data\resnametable.txt by this lookup's rule (once, when the install is opened: Data/RoInstall.cs). Rows
        /// "alias#real#" (Latin-1 characters for the EUC-KR bytes, as every RO path here), "//" comment lines and blank lines skipped;
        /// the first row of a name wins; a row naming itself is left out. Returns the rows kept (0 when the install has no table).</summary>
        public int LoadNameTable(Action<string> log = null)
        {
            _aliases.Clear();
            byte[] b = null;
            try { b = Read(NameTable, out _, _ => { }); } catch { }
            if (b == null) { log?.Invoke(NameTable + ": none in the game data (no name aliases)"); return 0; }
            int rows = 0, same = 0, dup = 0;
            foreach (var raw in RoReader.Latin1.GetString(b).Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;
                var c = line.Split('#');
                if (c.Length < 2) continue;
                string a = RoPath.Normalize(c[0].Trim()), r = RoPath.Normalize(c[1].Trim());
                if (a.Length == 0 || r.Length == 0) continue;
                rows++;
                if (a == r) { same++; continue; }
                if (_aliases.ContainsKey(a)) { dup++; continue; }
                _aliases[a] = r;
            }
            log?.Invoke($"{NameTable}: {rows} rows, {_aliases.Count} aliases ({same} name themselves, {dup} repeat a name: the first row wins)");
            return _aliases.Count;
        }

        /// <summary>The file's bytes, or null when no source holds it. <paramref name="source"/>: "loose" or the archive's file
        /// name. A damaged archive entry or an unreadable loose file goes to <paramref name="onDamaged"/> and the next source is
        /// tried (an older copy is a safer result than none); with no handler the exception is thrown (the builder: its step
        /// fails with the reason and the table's last good version stays).</summary>
        public byte[] Read(string normalized, out string source, Action<Exception> onDamaged = null)
        {
            var b = ReadExact(normalized, out source, onDamaged);
            if (b != null) return b;
            string real = Alias(normalized);
            return real != null ? ReadExact(real, out source, onDamaged) : null;
        }

        private byte[] ReadExact(string normalized, out string source, Action<Exception> onDamaged)
        {
            byte[] b;
            if (LooseFirst && (b = ReadLoose(normalized, onDamaged)) != null) { source = "loose"; return b; }
            foreach (var g in Grfs)
            {
                try { b = g.Read(normalized); }
                catch (GrfEntryException e) { if (onDamaged == null) throw; onDamaged(e); continue; }
                if (b != null) { source = System.IO.Path.GetFileName(g.Path); return b; }
            }
            if (!LooseFirst && (b = ReadLoose(normalized, onDamaged)) != null) { source = "loose"; return b; }
            source = null; return null;
        }

        private byte[] ReadLoose(string normalized, Action<Exception> onDamaged)
        {
            if (!LooseExists(normalized)) return null;
            try { return File.ReadAllBytes(LoosePath(normalized)); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                if (onDamaged == null) throw;
                onDamaged(new IOException("the loose file " + normalized + " cannot be read: " + e.Message.Trim(), e));
                return null;
            }
        }

        /// <summary>The install's clientinfo, read by the default rule (archives first, then the loose folder: before the tag is
        /// known no other rule applies): data\sclientinfo.xml when it exists, else data\clientinfo.xml (korangar
        /// loaders/server/mod.rs:25-26 and client_info.rs:14-15). Null when the install has none.</summary>
        public byte[] ReadClientInfo(out string name)
        {
            bool keep = LooseFirst; LooseFirst = false;
            try
            {
                foreach (var n in new[] { "data\\sclientinfo.xml", "data\\clientinfo.xml" })
                {
                    byte[] b = null;
                    try { b = Read(n, out _, _ => { }); } catch { }
                    if (b != null) { name = n; return b; }
                }
                name = null; return null;
            }
            finally { LooseFirst = keep; }
        }

        /// <summary>True when the clientinfo text carries the readfolder tag (&lt;readfolder/&gt;, &lt;readfolder /&gt; or
        /// &lt;readfolder&gt;&lt;/readfolder&gt;: its presence is the switch, korangar client_info.rs:331-341).</summary>
        public static bool HasReadFolder(byte[] clientInfo)
        {
            if (clientInfo == null) return false;
            string s = RoReader.Latin1.GetString(clientInfo);
            int i = 0;
            while ((i = s.IndexOf("<readfolder", i, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int j = i + "<readfolder".Length;
                if (j < s.Length && (s[j] == '>' || s[j] == '/' || char.IsWhiteSpace(s[j])) && !InComment(s, i)) return true;
                i = j;
            }
            return false;
        }

        private static bool InComment(string s, int at)
        {
            int open = s.LastIndexOf("<!--", at, StringComparison.Ordinal);
            if (open < 0) return false;
            int close = s.IndexOf("-->", open, StringComparison.Ordinal);
            return close < 0 || close > at;
        }
    }
}
