using System;
using System.Collections.Generic;
using Loki.Data;
using Loki.IO;

namespace Loki.VrMaps
{
    /// <summary>Where the game files come from: the player's install (read-only, through the client's own lookup) or, for the
    /// self-test, a dictionary of synthetic files.</summary>
    public interface IGameFiles
    {
        /// <summary>The file at an RO path ("data\\prontera.rsw", any case / slash style), or null when absent.</summary>
        byte[] Read(string roPath);
        bool Exists(string roPath);
        /// <summary>Every file name the data holds (normalized), for <c>list</c>.</summary>
        IEnumerable<string> Names { get; }
        string Describe { get; }
    }

    /// <summary>The player's install, opened as the game opens it: Data/RoInstall.cs (the install's own archive order, DATA.INI, the
    /// clientinfo's loose-folder rule) and IO/RoLookup.cs. GRFs are opened with FileAccess.Read (IO/GrfArchive.cs:55).</summary>
    public sealed class InstallFiles : IGameFiles, IDisposable
    {
        public readonly RoOpened Opened;
        public InstallFiles(string root, string[] grfs, Action<string> log = null)
        {
            Opened = RoInstall.Open(root, grfs == null || grfs.Length == 0 ? new[] { "data.grf" } : grfs, log);
        }
        public byte[] Read(string roPath) => Opened.Lookup.Read(RoPath.Normalize(roPath), out _, e => Damaged.Add(roPath + ": " + e.Message));
        public bool Exists(string roPath) => Opened.Lookup.Exists(RoPath.Normalize(roPath));
        public readonly List<string> Damaged = new List<string>();
        public IEnumerable<string> Names
        {
            get
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var g in Opened.Lookup.Grfs) foreach (var n in g.Names) if (seen.Add(n)) yield return n;
            }
        }
        public string Describe => Opened.Install.Root + " (" + string.Join(", ", Opened.Install.GrfNames) + ")";
        public void Dispose() { foreach (var g in Opened.Lookup.Grfs) g.Dispose(); }
    }

    /// <summary>Synthetic files (self-test).</summary>
    public sealed class MemoryFiles : IGameFiles
    {
        public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        public void Add(string roPath, byte[] data) => Files[RoPath.Normalize(roPath)] = data;
        public byte[] Read(string roPath) => Files.TryGetValue(RoPath.Normalize(roPath), out var b) ? b : null;
        public bool Exists(string roPath) => Files.ContainsKey(RoPath.Normalize(roPath));
        public IEnumerable<string> Names => Files.Keys;
        public string Describe => "synthetic";
    }

    /// <summary>Data/ToolKit.cs's inputs over the same files, for Data/RoomTools.cs (the room table, re-run as the first-run builder runs it).</summary>
    public sealed class ToolInputs : IToolInputs
    {
        private readonly IGameFiles _f;
        public ToolInputs(IGameFiles f) { _f = f; }
        public byte[] Grf(string roPath) => _f.Read(roPath);
        public byte[] RoFile(string relPath) => null;
        public byte[] Part(string name) => null;
        public string GrfName => "data.grf";
        public int GrfVersion => 0x200;
        public bool Exists(string roPath) => _f.Exists(roPath);
    }
}
