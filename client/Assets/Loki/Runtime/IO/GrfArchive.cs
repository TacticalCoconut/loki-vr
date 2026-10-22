using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Loki.IO
{
    /// <summary>
    /// Read-only GRF 0x200 / 0x300 archive with lazy, thread-safe entry extraction.
    /// Only the file table is kept in memory; entry bytes are read on demand.
    /// 0x300 ("Event Horizon", the LATAM client installed 2026-09-30 ships it): the same 46-byte header with a 64-bit
    /// table offset at 30 and the plain file count at 38 (no seed), 4 more bytes before the table's packed / real sizes,
    /// and 64-bit entry offsets (roBrowserLegacy src/Loaders/GameFile.js load / loadEntries; checked on the real file).
    /// Client wave 7 (w7data): nothing here is fatal for the game. Opening throws <see cref="GrfOpenException"/> with one
    /// clear reason: not a GRF; a version this reader does not know (the LATAM install's event.grf is 0x102, which
    /// roBrowserLegacy's reader refuses too, GameFile.js:139-144); a file table cut off or damaged (a half-written file); a
    /// file another program holds open for writing. A damaged entry throws <see cref="GrfEntryException"/> from
    /// <see cref="Read"/>. The callers skip that archive / entry (IO/RoFileSystem.cs, IO/RoLookup.cs) and the first-run
    /// builder keeps the table's last good version.
    /// Pure C# (no UnityEngine): the first-run harness and Loki Host can share this file.
    /// </summary>
    public sealed class GrfArchive : IDisposable
    {
        public struct Entry
        {
            public string Name;              // normalized: lowercase-ASCII, backslashes, Latin-1
            public uint CompressedSize;
            public uint CompressedSizeAligned;
            public uint UncompressedSize;
            public byte Flags;
            public long Offset;
        }

        /// <summary>0x200 or 0x300.</summary>
        public uint Version { get; private set; }

        /// <summary>Size and time of the file when its table was read: what this archive's index describes.</summary>
        public long OpenedLength { get; private set; }
        public long OpenedWriteTicks { get; private set; }

        private const int HeaderSize = 46;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly FileStream _fs;
        private readonly object _ioLock = new object();

        public string Path { get; }
        public int Count => _entries.Count;
        public IEnumerable<string> Names => _entries.Keys;

        public GrfArchive(string path)
        {
            Path = path;
            // FileShare.Read: nothing can write, rename or remove the file while Loki has it open (the official patcher cannot merge
            // into data.grf under a running Loki, as it cannot under the official client), so the index read here stays true
            try { _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess); }
            catch (FileNotFoundException) { throw; }
            catch (DirectoryNotFoundException) { throw; }
            catch (IOException e) { throw new GrfOpenException(path, GrfOpenException.Kind.Busy, "cannot be opened (" + e.Message.Trim() + ")", e); }
            catch (UnauthorizedAccessException e) { throw new GrfOpenException(path, GrfOpenException.Kind.Busy, "cannot be opened (" + e.Message.Trim() + ")", e); }
            try
            {
                OpenedLength = _fs.Length;
                OpenedWriteTicks = File.GetLastWriteTimeUtc(path).Ticks;
                ReadTable();
            }
            catch (GrfOpenException) { _fs.Dispose(); throw; }
            catch (Exception e) { _fs.Dispose(); throw new GrfOpenException(path, GrfOpenException.Kind.Damaged, "has a file table that cannot be read (" + e.GetType().Name + ": " + e.Message.Trim() + ")", e); }
        }

        private void ReadTable()
        {
            if (_fs.Length < HeaderSize) throw new GrfOpenException(Path, GrfOpenException.Kind.Damaged, $"is {_fs.Length} bytes long, shorter than a GRF header");
            var hdr = new byte[HeaderSize];
            ReadExact(_fs, hdr, 0, HeaderSize);
            string sig = RoReader.Latin1.GetString(hdr, 0, 15);
            bool eh3 = RoReader.Latin1.GetString(hdr, 0, 13) == "Event Horizon";
            if (sig != "Master of Magic" && !eh3) throw new GrfOpenException(Path, GrfOpenException.Kind.NotGrf, "is not a GRF (no \"Master of Magic\" / \"Event Horizon\" signature)");
            uint version = BitConverter.ToUInt32(hdr, 42);
            Version = version & 0xFF00;
            long tableAt; int count;
            if (Version == 0x300)
            {
                tableAt = HeaderSize + (long)BitConverter.ToUInt64(hdr, 30) + 4; // 4 unknown bytes before the sizes
                count = (int)BitConverter.ToUInt32(hdr, 38);
            }
            else if (Version == 0x200)
            {
                tableAt = HeaderSize + (long)BitConverter.ToUInt32(hdr, 30);
                count = (int)(BitConverter.ToUInt32(hdr, 38) - BitConverter.ToUInt32(hdr, 34)) - 7;
            }
            else throw new GrfOpenException(Path, GrfOpenException.Kind.UnknownVersion, $"is GRF version 0x{version:X}: Loki reads 0x200 and 0x300 only");
            // a half-written file (a patch in progress, a copy cut short) shows here: the header points at a table that is not there
            if (count < 0 || tableAt < HeaderSize || tableAt + 8 > _fs.Length)
                throw new GrfOpenException(Path, GrfOpenException.Kind.Damaged, $"has its file table (offset {tableAt}, {count} files) past the end of the file ({_fs.Length} bytes): cut short or still being written");

            _fs.Seek(tableAt, SeekOrigin.Begin);
            var t = new byte[8];
            ReadExact(_fs, t, 0, 8);
            uint packed = BitConverter.ToUInt32(t, 0);
            uint unpacked = BitConverter.ToUInt32(t, 4);
            if (packed > int.MaxValue || unpacked > int.MaxValue || tableAt + 8 + packed > _fs.Length)
                throw new GrfOpenException(Path, GrfOpenException.Kind.Damaged, $"has a file table ({packed} packed bytes at {tableAt}) running past the end of the file ({_fs.Length} bytes): cut short or still being written");
            var packedBuf = new byte[packed];
            ReadExact(_fs, packedBuf, 0, (int)packed);
            var table = Inflate(packedBuf, 0, (int)packed, (int)unpacked);

            int p = 0, tail = Version == 0x300 ? 21 : 17;
            for (int i = 0; i < count && p < table.Length; i++)
            {
                int start = p;
                while (p < table.Length && table[p] != 0) p++;
                if (p + 1 + tail > table.Length) throw new GrfOpenException(Path, GrfOpenException.Kind.Damaged, $"has a file table that ends inside entry {i} of {count}");
                string name = RoReader.Latin1.GetString(table, start, p - start);
                p++;
                var e = new Entry
                {
                    CompressedSize = BitConverter.ToUInt32(table, p),
                    CompressedSizeAligned = BitConverter.ToUInt32(table, p + 4),
                    UncompressedSize = BitConverter.ToUInt32(table, p + 8),
                    Flags = table[p + 12],
                    Offset = Version == 0x300 ? (long)BitConverter.ToUInt64(table, p + 13) : BitConverter.ToUInt32(table, p + 13),
                };
                p += tail;
                if ((e.Flags & GrfCrypt.FlagFile) == 0) continue; // directory
                e.Name = RoPath.Normalize(name);
                _entries[e.Name] = e;
            }
        }

        public bool Contains(string normalizedPath) => _entries.ContainsKey(normalizedPath);
        public bool TryGetEntry(string normalizedPath, out Entry e) => _entries.TryGetValue(normalizedPath, out e);

        /// <summary>Entries stated larger than this are taken for damaged table rows (the largest file of the 2026-09-30 LATAM
        /// data.grf is 37 MB; a damaged size field would otherwise ask for gigabytes of memory).</summary>
        public const uint MaxEntryBytes = 1u << 30;

        /// <summary>Extract and return the decompressed bytes of an entry, or null if absent. A damaged entry (sizes that cannot
        /// be, data past the end of the file, a stream that does not inflate to the stated size) throws
        /// <see cref="GrfEntryException"/> naming it; the archive stays usable.</summary>
        public byte[] Read(string normalizedPath)
        {
            if (!_entries.TryGetValue(normalizedPath, out var e)) return null;
            try { return ReadEntry(e); }
            catch (GrfEntryException) { throw; }
            catch (Exception ex) { throw new GrfEntryException(Path, e.Name, ex.GetType().Name + ": " + ex.Message.Trim(), ex); }
        }

        private byte[] ReadEntry(Entry e)
        {
            if (e.CompressedSizeAligned < e.CompressedSize || e.CompressedSizeAligned > MaxEntryBytes || e.UncompressedSize > MaxEntryBytes)
                throw new GrfEntryException(Path, e.Name, $"impossible sizes (packed {e.CompressedSize}, aligned {e.CompressedSizeAligned}, real {e.UncompressedSize})");
            int len = (int)e.CompressedSizeAligned;
            if (e.Offset < 0 || HeaderSize + e.Offset + len > _fs.Length)
                throw new GrfEntryException(Path, e.Name, $"its data ({len} bytes at {HeaderSize + e.Offset}) lies past the end of the file ({_fs.Length} bytes)");
            var buf = new byte[len];
            lock (_ioLock)
            {
                _fs.Seek(HeaderSize + e.Offset, SeekOrigin.Begin);
                ReadExact(_fs, buf, 0, len);
            }
            bool headerOnly = e.Name.EndsWith(".gnd") || e.Name.EndsWith(".gat") || e.Name.EndsWith(".act") || e.Name.EndsWith(".str");
            GrfCrypt.DecryptEntry(e.Flags, e.CompressedSize, buf, len, headerOnly);
            if (e.UncompressedSize == e.CompressedSize && !LooksLikeZlib(buf))
            {
                if (buf.Length == e.UncompressedSize) return buf;
                var raw = new byte[e.UncompressedSize];
                Array.Copy(buf, raw, raw.Length);
                return raw;
            }
            return Inflate(buf, 0, (int)e.CompressedSize, (int)e.UncompressedSize);
        }

        private static bool LooksLikeZlib(byte[] b) => b.Length >= 2 && (b[0] & 0x0F) == 8 && ((b[0] << 8) | b[1]) % 31 == 0;

        internal static byte[] Inflate(byte[] src, int offset, int count, int expected)
        {
            // zlib stream = 2-byte header + raw deflate + 4-byte adler32. DeflateStream wants the raw part.
            using (var ms = new MemoryStream(src, offset + 2, Math.Max(0, count - 2), false))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            {
                var outBuf = new byte[expected];
                int total = 0;
                while (total < expected)
                {
                    int n = ds.Read(outBuf, total, expected - total);
                    if (n <= 0) break;
                    total += n;
                }
                if (total != expected) throw new InvalidDataException($"Inflate: expected {expected} bytes, got {total}");
                return outBuf;
            }
        }

        private static void ReadExact(Stream s, byte[] buf, int off, int len)
        {
            while (len > 0)
            {
                int n = s.Read(buf, off, len);
                if (n <= 0) throw new EndOfStreamException();
                off += n; len -= n;
            }
        }

        public void Dispose() => _fs.Dispose();
    }

    /// <summary>Client wave 7 (w7data): why a GRF could not be opened. <see cref="Reason"/> completes the sentence
    /// "&lt;file name&gt; …" (the message is that sentence), for the log and the panels.</summary>
    public sealed class GrfOpenException : IOException
    {
        public enum Kind { NotGrf, UnknownVersion, Damaged, Busy }
        public readonly Kind Why; public readonly string File, Reason;
        public GrfOpenException(string file, Kind why, string reason, Exception inner = null)
            : base(System.IO.Path.GetFileName(file) + " " + reason, inner) { File = file; Why = why; Reason = reason; }
    }

    /// <summary>Client wave 7 (w7data): one entry of a GRF that cannot be read (the archive itself stays usable).</summary>
    public sealed class GrfEntryException : IOException
    {
        public readonly string File, Entry, Reason;
        public GrfEntryException(string file, string entry, string reason, Exception inner = null)
            : base($"{entry} in {System.IO.Path.GetFileName(file)} is damaged: {reason}", inner) { File = file; Entry = entry; Reason = reason; }
    }

    public static class RoPath
    {
        /// <summary>Lowercase A-Z only (Latin-1 bytes ≥ 0x80 are EUC-KR halves and must not be touched), '/' → '\\', and "dir\..\" folded
        /// away: the effect tables name some sprites relative to the effect folder ("../npc/hydra_atk", rB DB/Effects/EffectTable.js),
        /// and a GRF entry never holds "..", so such a path could not be found (client wave 3, w3combat).</summary>
        public static string Normalize(string p)
        {
            var c = p.ToCharArray();
            for (int i = 0; i < c.Length; i++)
            {
                char ch = c[i];
                if (ch >= 'A' && ch <= 'Z') c[i] = (char)(ch + 32);
                else if (ch == '/') c[i] = '\\';
            }
            var s = new string(c);
            return s.IndexOf("..", StringComparison.Ordinal) < 0 ? s : Fold(s);
        }

        private static string Fold(string s)
        {
            var parts = new List<string>(s.Split('\\'));
            for (int i = 1; i < parts.Count; i++)
                if (parts[i] == ".." && parts[i - 1] != "..") { parts.RemoveRange(i - 1, 2); i = Math.Max(0, i - 2); }
            return string.Join("\\", parts);
        }
    }
}
