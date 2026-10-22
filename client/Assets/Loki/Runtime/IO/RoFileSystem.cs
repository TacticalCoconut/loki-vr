using System;
using System.Collections.Generic;
using System.IO;
using Loki.Data;
using UnityEngine;

namespace Loki.IO
{
    /// <summary>
    /// Virtual file system over the RO client folder. All lookups take RO-style paths ("data\\texture\\...") in any case /
    /// slash style.
    /// Client wave 7 (w7data): it follows the player's install. The archives are the install's own list in its own order
    /// (Data/RoInstall.cs: DATA.INI when the install has one, else the configured list); loose files under &lt;root&gt;/data/ come
    /// first or last as the install's clientinfo says (IO/RoLookup.cs: the &lt;readfolder&gt; tag — present in the LATAM install,
    /// so loose files win there, as before this wave). An archive that cannot be opened (a version this reader does not know, a
    /// damaged or half-written file) is skipped with one log line and listed in <see cref="Skipped"/>; a damaged entry is logged
    /// once and read from the next source, or given as missing: no caller sees an exception from <see cref="Read"/>.
    /// </summary>
    public sealed class RoFileSystem : IDisposable
    {
        private readonly string _root;
        private readonly List<GrfArchive> _grfs = new List<GrfArchive>();
        public IReadOnlyList<GrfArchive> Archives => _grfs;
        public string Root => _root;

        /// <summary>What the install says about itself, its open archives and the lookup rule (what the first-run builder reads too).</summary>
        public ClientDataBuilder.Source Source { get; }
        public RoInstall Install => Source.Install;
        public RoLookup Lookup => Source.Lookup;
        /// <summary>The archives of the install's order that are not read, and why.</summary>
        public IReadOnlyList<SkippedGrf> Skipped => Source.Skipped;
        /// <summary>The entries / loose files that could not be read so far (each logged once).</summary>
        public int DamagedCount { get { lock (_damaged) return _damaged.Count; } }
        public List<string> DamagedNames { get { lock (_damaged) return new List<string>(_damaged); } }
        private readonly HashSet<string> _damaged = new HashSet<string>(StringComparer.Ordinal);

        public RoFileSystem(string clientRoot, params string[] grfNames)
        {
            _root = clientRoot;
            Source = ClientDataBuilder.Source.Open(clientRoot, grfNames, null, null, s => Debug.Log("[RoFS] " + s));
            _grfs.AddRange(Source.Grfs);
            foreach (var k in Source.Skipped) Debug.LogWarning($"[RoFS] archive not read: {k.Reason}");
        }

        public bool Exists(string roPath) => Lookup.Exists(RoPath.Normalize(roPath));

        /// <summary>warch2: the path a read of <paramref name="roPath"/> takes (normalized): itself when the game data holds it, else
        /// the real file the game's name table names for it (IO/RoLookup.cs: data\resnametable.txt), else itself.</summary>
        public string RealPath(string roPath) => Lookup.RealPath(RoPath.Normalize(roPath));

        public byte[] Read(string roPath)
        {
            string n = RoPath.Normalize(roPath);
            return Lookup.Read(n, out _, e => Damaged(n, e));
        }

        private void Damaged(string normalized, Exception e)
        {
            bool first;
            lock (_damaged) first = _damaged.Add(normalized);
            if (first) Debug.LogWarning("[RoFS] damaged game file skipped: " + e.Message);
        }

        public byte[] ReadOrThrow(string roPath)
        {
            var b = Read(roPath);
            if (b == null)
            {
                bool damaged; lock (_damaged) damaged = _damaged.Contains(RoPath.Normalize(roPath));
                throw new FileNotFoundException((damaged ? "Damaged in the client data: " : "Not in client data: ") + roPath);
            }
            return b;
        }

        public void Dispose() { foreach (var g in _grfs) g.Dispose(); _grfs.Clear(); }
    }
}
