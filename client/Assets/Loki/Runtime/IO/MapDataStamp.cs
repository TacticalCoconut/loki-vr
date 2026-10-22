#nullable disable
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Loki.IO
{
    /// <summary>
    /// Client wave 7 (w7host): the short "data stamp" a player's client and the host compare (the relay's DATA message,
    /// Net/PresenceWire.cs). It answers one question: do this install's maps have the same cells as the host's? The host's
    /// server walks every unit on a map cache built from the host's own map files (tools/lokihost/Core/MapCacheBuilder.cs), and
    /// each client plans and draws walks on its own; where the two differ, a walk can be refused or drawn through a wall
    /// (design notes §1).
    ///
    /// The stamp is a hash of what decides walking, read the way the game itself reads it: for every map file the install
    /// offers (data\NAME.gat, whichever source the client's own file system picks for it), the map's name, its size and each
    /// cell's type byte. Why this and not the two cheaper candidates:
    ///   - "the GRF list with sizes and times" differs between two installs at the very same patch: the official patcher
    ///     merges each patch into data.grf on the player's PC, so the file's time is when that PC patched and its size depends
    ///     on which patches it went through. Every friend would be told their data differs.
    ///   - "the install's patch number" (patch.inf) changes with every patch, though few patches touch a map, and it cannot
    ///     tell two installs apart that carry the same number with different files (a loose data\ file, another service). It is
    ///     sent beside the stamp as a label for people to read, never compared.
    /// Cell types only (not the four corner heights of a cell): heights are drawn, never walked on; rAthena's map cache keeps
    /// the type byte alone (rA src/tool/mapcache.cpp:110 "m->cells[xy] = (unsigned char)type").
    ///
    /// No Unity dependency: Loki Host compiles this same file (tools/lokihost/RoData), so both ends always hash alike.
    ///
    /// What is hashed (SHA-256, the stamp is "g1-" + its first 16 hex digits), maps in ordinal order of their names:
    ///   name (the entry's characters as Latin-1 bytes, without "data\" and ".gat"), 0x00,
    ///   then 0x01, int32 width, int32 height (little-endian), width × height bytes: each cell's type & 0xFF
    ///   or, for a map file that cannot be read (damaged entry, cut short, impossible size): 0x00 alone.
    /// GAT layout: 14 bytes of header (width at 6, height at 10), then 20 bytes per cell: four float heights and the uint32
    /// type (the client's Formats/GatFile.cs; rA src/tool/mapcache.cpp:88-108).
    /// </summary>
    public static class MapDataStamp
    {
        public const string Prefix = "g1-";
        /// <summary>A side of at most this many cells is believed (the biggest RO maps are 400 × 400; rAthena refuses a map
        /// over 512 × 512 cells, map.hpp:82 MAX_MAP_SIZE).</summary>
        public const int MaxSide = 4096;

        public sealed class Result
        {
            /// <summary>"g1-" + 16 hex digits.</summary>
            public string Stamp;
            public int Maps, Unreadable;
            /// <summary>The first few unreadable map names (for the log).</summary>
            public readonly List<string> UnreadableNames = new List<string>();
            public long Cells;
        }

        /// <summary>"data\NAME.gat" directly in data\ (a normalized entry name: lower-case, backslashes).</summary>
        public static bool IsMapGat(string normalized)
        {
            if (normalized == null || normalized.Length < 10) return false;
            if (!normalized.StartsWith("data\\", StringComparison.Ordinal) || !normalized.EndsWith(".gat", StringComparison.Ordinal)) return false;
            return normalized.IndexOf('\\', 5) < 0;
        }

        /// <summary>The map's name inside "data\NAME.gat".</summary>
        public static string MapName(string normalized) => normalized.Substring(5, normalized.Length - 9);

        /// <summary>
        /// The stamp of the maps in <paramref name="gatNames"/> (normalized "data\NAME.gat" names; repeats and other names are
        /// ignored), each read through <paramref name="read"/>: the caller's own file system, so a file is taken from the source
        /// the game would take it from. A read that returns null or throws makes that map "unreadable" (hashed as such, counted).
        /// <paramref name="cancelled"/> (optional) stops the work: null is returned.
        /// </summary>
        public static Result Compute(IEnumerable<string> gatNames, Func<string, byte[]> read, Func<bool> cancelled = null)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in gatNames) if (IsMapGat(n) && seen.Add(n)) names.Add(n);
            names.Sort(StringComparer.Ordinal);

            var r = new Result();
            var head = new byte[9];
            using (var sha = SHA256.Create())
            {
                foreach (var n in names)
                {
                    if (cancelled != null && cancelled()) return null;
                    string map = MapName(n);
                    var nameBytes = new byte[map.Length + 1];
                    for (int i = 0; i < map.Length; i++) nameBytes[i] = (byte)map[i]; // Latin-1: the entry's own bytes
                    sha.TransformBlock(nameBytes, 0, nameBytes.Length, null, 0);

                    byte[] gat = null;
                    try { gat = read(n); } catch (Exception) { gat = null; }
                    byte[] cells = gat != null ? CellTypes(gat, out int xs, out int ys) : null;
                    r.Maps++;
                    if (cells == null)
                    {
                        r.Unreadable++;
                        if (r.UnreadableNames.Count < 8) r.UnreadableNames.Add(map);
                        head[0] = 0;
                        sha.TransformBlock(head, 0, 1, null, 0);
                        continue;
                    }
                    int w = BitConverter.ToInt32(gat, 6), h = BitConverter.ToInt32(gat, 10);
                    head[0] = 1;
                    head[1] = (byte)w; head[2] = (byte)(w >> 8); head[3] = (byte)(w >> 16); head[4] = (byte)(w >> 24);
                    head[5] = (byte)h; head[6] = (byte)(h >> 8); head[7] = (byte)(h >> 16); head[8] = (byte)(h >> 24);
                    sha.TransformBlock(head, 0, 9, null, 0);
                    sha.TransformBlock(cells, 0, cells.Length, null, 0);
                    r.Cells += cells.Length;
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                var d = sha.Hash;
                var sb = new System.Text.StringBuilder(Prefix, Prefix.Length + 16);
                for (int i = 0; i < 8; i++) sb.Append(d[i].ToString("x2"));
                r.Stamp = sb.ToString();
            }
            return r;
        }

        /// <summary>
        /// The official patcher's marker of an install, for people to read beside the stamp (it is never compared): patch.inf
        /// in the install folder, 4 bytes, a little-endian number. On the LATAM install of 2026-09-30 it holds 1486, the number
        /// of the last line of the patcher's own list (patch.txt: "1486 2026-09-15_live_client_2058_…gpf"), written in the same
        /// second as data.grf; the patcher's settings (PatchClient\LuaFiles514\Local\ServerInfo.lub) name the file:
        /// g_inf_file_name = patch.inf (w7data's finding, COORD 2026-10-04). -1 when the install has no such file, or it is not
        /// 4 bytes, or it cannot be read. The file is opened shared: the patcher is never locked out.
        /// </summary>
        public static long PatchNumber(string installRoot)
        {
            try
            {
                string p = System.IO.Path.Combine(installRoot ?? "", "patch.inf");
                if (!System.IO.File.Exists(p)) return -1;
                using (var f = new System.IO.FileStream(p, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete))
                {
                    if (f.Length != 4) return -1;
                    var b = new byte[4];
                    if (f.Read(b, 0, 4) != 4) return -1;
                    return (uint)(b[0] | b[1] << 8 | b[2] << 16 | b[3] << 24);
                }
            }
            catch (Exception) { return -1; }
        }

        /// <summary>"patch 1486", or "" when the install has no marker.</summary>
        public static string PatchLabel(string installRoot)
        {
            long n = PatchNumber(installRoot);
            return n < 0 ? "" : "patch " + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>The number in a label made by <see cref="PatchLabel"/>, or -1.</summary>
        public static long PatchOf(string label)
        {
            const string head = "patch ";
            if (label == null || !label.StartsWith(head, StringComparison.Ordinal)) return -1;
            return long.TryParse(label.Substring(head.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long n) ? n : -1;
        }

        /// <summary>Each cell's type byte, or null when the bytes are not a whole GAT (too short for its own size, a size that
        /// cannot be).</summary>
        public static byte[] CellTypes(byte[] gat, out int xs, out int ys)
        {
            xs = ys = 0;
            if (gat == null || gat.Length < 14) return null;
            xs = BitConverter.ToInt32(gat, 6); ys = BitConverter.ToInt32(gat, 10);
            if (!BitConverter.IsLittleEndian) return null; // (every platform Loki runs on is little-endian)
            if (xs <= 0 || ys <= 0 || xs > MaxSide || ys > MaxSide) return null;
            long n = (long)xs * ys;
            if (14 + 20 * n > gat.Length) return null;
            var cells = new byte[n];
            int p = 14 + 16;
            for (int i = 0; i < cells.Length; i++, p += 20) cells[i] = gat[p];
            return cells;
        }
    }
}
