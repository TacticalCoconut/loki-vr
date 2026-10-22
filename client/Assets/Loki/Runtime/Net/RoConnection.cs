using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Loki.Net
{
    /// <summary>
    /// One TCP connection to a login/char/map server. A background thread reads bytes; the main thread
    /// calls <see cref="Pump"/> once per frame to frame packets with the length table and dispatch them.
    /// An unknown packet id is a hard error (disconnect + log), never a silent skip: that is how UnityRO desynced.
    /// </summary>
    public sealed class RoConnection : IDisposable
    {
        public delegate void PacketHandler(RoConnection c, ushort id, byte[] buf, int offset, int length);

        public readonly string Host; public readonly int Port; public readonly string Name;
        private readonly Dictionary<ushort, short> _lengths;
        private TcpClient _tcp;
        private NetworkStream _stream;
        private Thread _reader;
        private readonly ConcurrentQueue<byte[]> _inbox = new ConcurrentQueue<byte[]>();
        private readonly MemoryStream _acc = new MemoryStream();
        private volatile bool _closed;
        private string _error;

        /// <summary>Expect N raw bytes (no header) before normal framing resumes — the login-id echo char/map servers send.</summary>
        private int _rawExpect;
        private Action<byte[]> _rawHandler;

        /// <summary>A link with no socket: bytes arrive through <see cref="Feed"/> and <see cref="Send"/> hands them to a delegate
        /// (Loki Online: the SpacetimeDB <c>packet</c> reducer one way, <c>packet_out</c> rows the other). Framing, the length
        /// table and the unknown-packet rule are the same as on a TCP link.</summary>
        public bool IsVirtual => _virtualSend != null;
        private readonly Action<byte[]> _virtualSend;
        private readonly Action _virtualClose;

        public bool Connected => IsVirtual ? !_closed : _tcp != null && _tcp.Connected && !_closed;
        public string Error => _error;
        public event PacketHandler OnPacket;
        public event Action<RoConnection, string> OnClosed;

        public RoConnection(string name, string host, int port, Dictionary<ushort, short> lengths)
        {
            Name = name; Host = host; Port = port; _lengths = lengths;
        }

        /// <summary>A virtual link (see <see cref="IsVirtual"/>): <paramref name="send"/> carries what the game writes,
        /// <paramref name="close"/> tells the server the link was closed.</summary>
        public RoConnection(string name, Dictionary<ushort, short> lengths, Action<byte[]> send, Action close)
        {
            Name = name; Host = "spacetimedb"; Port = 0; _lengths = lengths; _virtualSend = send; _virtualClose = close;
        }

        /// <summary>Bytes from the server on a virtual link (any thread; framed on the next <see cref="Pump"/>).</summary>
        public void Feed(byte[] bytes)
        {
            if (_closed || bytes == null || bytes.Length == 0) return;
            _inbox.Enqueue(bytes);
        }

        public void Connect()
        {
            if (IsVirtual) { Debug.Log($"[Net:{Name}] virtual link open"); return; }
            _tcp = new TcpClient { NoDelay = true };
            _tcp.Connect(Host, Port);
            _stream = _tcp.GetStream();
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "ro-net-" + Name };
            _reader.Start();
            Debug.Log($"[Net:{Name}] connected {Host}:{Port}");
        }

        private void ReadLoop()
        {
            var buf = new byte[65536];
            try
            {
                while (!_closed)
                {
                    int n = _stream.Read(buf, 0, buf.Length);
                    if (n <= 0) { _error = "remote closed"; break; }
                    var chunk = new byte[n]; Array.Copy(buf, chunk, n);
                    _inbox.Enqueue(chunk);
                }
            }
            catch (Exception e) { if (!_closed) _error = e.Message; }
            _closed = true;
        }

        public void ExpectRaw(int bytes, Action<byte[]> handler) { _rawExpect = bytes; _rawHandler = handler; }

        /// <summary>Call from the main thread each frame.</summary>
        public void Pump()
        {
            while (_inbox.TryDequeue(out var chunk)) { _acc.Position = _acc.Length; _acc.Write(chunk, 0, chunk.Length); }
            var data = _acc.GetBuffer();
            int len = (int)_acc.Length;
            int pos = 0;
            while (true)
            {
                if (_rawExpect > 0)
                {
                    if (len - pos < _rawExpect) break;
                    var raw = new byte[_rawExpect]; Array.Copy(data, pos, raw, 0, _rawExpect);
                    pos += _rawExpect; _rawExpect = 0;
                    var h = _rawHandler; _rawHandler = null;
                    h?.Invoke(raw);
                    continue;
                }
                if (len - pos < 2) break;
                ushort id = (ushort)(data[pos] | (data[pos + 1] << 8));
                if (!_lengths.TryGetValue(id, out short plen))
                {
                    _error = $"unknown packet 0x{id:x4} (have {len - pos} bytes: {Hex(data, pos, Math.Min(32, len - pos))})";
                    Debug.LogError($"[Net:{Name}] {_error}");
                    _acc.SetLength(0);
                    Close();
                    var cb2 = OnClosed; OnClosed = null; cb2?.Invoke(this, _error);
                    return;
                }
                int size;
                if (plen == -1)
                {
                    if (len - pos < 4) break;
                    size = data[pos + 2] | (data[pos + 3] << 8);
                    if (size < 4) { _error = $"bad dynamic length {size} for 0x{id:x4}"; Debug.LogError($"[Net:{Name}] {_error}"); _acc.SetLength(0); Close(); var cb3 = OnClosed; OnClosed = null; cb3?.Invoke(this, _error); return; }
                }
                else size = plen;
                if (len - pos < size) break;
                try { OnPacket?.Invoke(this, id, data, pos, size); }
                catch (Exception e) { Debug.LogException(e); }
                pos += size;
            }
            if (pos > 0)
            {
                int rest = len - pos;
                if (rest > 0) Array.Copy(data, pos, data, 0, rest);
                _acc.SetLength(rest);
            }
            if (_closed && _inbox.IsEmpty && _acc.Length == 0) { var cb = OnClosed; OnClosed = null; cb?.Invoke(this, _error); }
        }

        private MemoryStream _outBatch;
        private int _batchDepth;

        public void BeginBatch()
        {
            if (!IsVirtual) return;
            _batchDepth++;
            if (_outBatch == null) _outBatch = new MemoryStream(2048);
        }

        public void EndBatch()
        {
            if (!IsVirtual || _batchDepth <= 0) return;
            if (--_batchDepth == 0) FlushBatch();
        }

        public void FlushBatch()
        {
            if (_outBatch == null || _outBatch.Length == 0) return;
            var copy = _outBatch.ToArray();
            _outBatch.SetLength(0);
            if (!Connected) return;
            try { _virtualSend(copy); } catch (Exception e) { _error = e.Message; Close(); }
        }

        public void Send(byte[] packet, int length = -1)
        {
            if (!Connected) return;
            if (IsVirtual)
            {
                int n = length < 0 ? packet.Length : length;
                if (_batchDepth > 0 && _outBatch != null && n <= 1500)
                {
                    if (_outBatch.Length + n > 1500) FlushBatch();
                    _outBatch.Write(packet, 0, n);
                    return;
                }
                var copy = new byte[n]; Array.Copy(packet, copy, n);
                try { _virtualSend(copy); } catch (Exception e) { _error = e.Message; Close(); }
                return;
            }
            try { _stream.Write(packet, 0, length < 0 ? packet.Length : length); }
            catch (Exception e) { _error = e.Message; Close(); }
        }

        public void Close()
        {
            if (IsVirtual)
            {
                if (_closed) return;
                _closed = true;
                try { _virtualClose?.Invoke(); } catch (Exception e) { Debug.LogWarning($"[Net:{Name}] close: {e.Message}"); }
                return;
            }
            if (_closed && _tcp == null) return;
            _closed = true;
            try { _stream?.Close(); _tcp?.Close(); } catch { }
            _tcp = null;
        }

        public void Dispose() => Close();

        public static string Hex(byte[] b, int off, int n)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++) sb.Append(b[off + i].ToString("x2")).Append(' ');
            return sb.ToString();
        }
    }

    /// <summary>Little-endian packet writer.</summary>
    public sealed class PacketWriter
    {
        private readonly MemoryStream _ms = new MemoryStream();
        public PacketWriter(ushort id) { U16(id); }
        public PacketWriter U8(byte v) { _ms.WriteByte(v); return this; }
        public PacketWriter U16(ushort v) { _ms.WriteByte((byte)v); _ms.WriteByte((byte)(v >> 8)); return this; }
        public PacketWriter S16(short v) => U16((ushort)v);
        public PacketWriter U32(uint v) { for (int i = 0; i < 4; i++) _ms.WriteByte((byte)(v >> (8 * i))); return this; }
        public PacketWriter S32(int v) => U32((uint)v);
        public PacketWriter U64(ulong v) { for (int i = 0; i < 8; i++) _ms.WriteByte((byte)(v >> (8 * i))); return this; }
        /// <summary>The string's bytes (<see cref="RoText.Encode"/>: cp1252, names byte for byte) plus a NUL terminator (variable-length
        /// text fields, e.g. RODEX title/body).</summary>
        public PacketWriter StrZ(string s) { var enc = RoText.Encode(s ?? ""); _ms.Write(enc, 0, enc.Length); _ms.WriteByte(0); return this; }
        /// <summary>A fixed-size field of <paramref name="len"/> bytes: the string's bytes (<see cref="RoText.Encode"/>), cut to len - 1, NUL-padded.</summary>
        public PacketWriter Str(string s, int len)
        {
            var b = new byte[len];
            var enc = RoText.Encode(s ?? "");
            Array.Copy(enc, b, Math.Min(enc.Length, len - 1));
            _ms.Write(b, 0, len); return this;
        }
        public PacketWriter Bytes(byte[] b) { _ms.Write(b, 0, b.Length); return this; }
        public PacketWriter Zero(int n) { for (int i = 0; i < n; i++) _ms.WriteByte(0); return this; }
        /// <summary>Patch the u16 length field at offset 2 (dynamic packets).</summary>
        public byte[] ToArrayWithLength() { var a = _ms.ToArray(); a[2] = (byte)a.Length; a[3] = (byte)(a.Length >> 8); return a; }
        public byte[] ToArray() => _ms.ToArray();
        public int Length => (int)_ms.Length;
    }

    /// <summary>Little-endian packet reader over a slice.</summary>
    public struct PacketReader
    {
        private readonly byte[] _b; private int _p; public readonly int End;
        public PacketReader(byte[] b, int offset, int length) { _b = b; _p = offset; End = offset + length; }
        public int Remaining => End - _p;
        public int Position { get => _p; set => _p = value; }
        public byte U8() => _b[_p++];
        public sbyte S8() => (sbyte)_b[_p++];
        public ushort U16() { ushort v = (ushort)(_b[_p] | (_b[_p + 1] << 8)); _p += 2; return v; }
        public short S16() => (short)U16();
        public uint U32() { uint v = (uint)(_b[_p] | (_b[_p + 1] << 8) | (_b[_p + 2] << 16) | (_b[_p + 3] << 24)); _p += 4; return v; }
        public int S32() => (int)U32();
        public long S64() { long lo = U32(); long hi = U32(); return lo | (hi << 32); }
        public ulong U64() => (ulong)S64();
        public float F32() { float v = BitConverter.ToSingle(_b, _p); _p += 4; return v; }
        /// <summary>A NUL-terminated field of <paramref name="len"/> bytes as Latin-1: names, map and file names keep every byte
        /// (<see cref="RoText"/>: written back through PacketWriter they are the same bytes).</summary>
        public string Str(int len) { int n = 0; while (n < len && _b[_p + n] != 0) n++; var s = RoText.Latin1(_b, _p, n); _p += len; return s; }
        /// <summary>The same field as text people wrote (chat, whispers, NPC dialog, broadcasts, mail, notices): cp1252
        /// (<see cref="RoText.Decode(byte[], int, int)"/>), so €, curly quotes and dashes show as the official client shows them.</summary>
        public string Text(int len) { int n = 0; while (n < len && _b[_p + n] != 0) n++; var s = RoText.Decode(_b, _p, n); _p += len; return s; }
        /// <summary>A "Name : text" chat line: the name Latin-1 (as <see cref="Str"/>), the text cp1252 (<see cref="RoText.DecodeChatLine"/>).</summary>
        public string ChatLine(int len) { int n = 0; while (n < len && _b[_p + n] != 0) n++; var s = RoText.DecodeChatLine(_b, _p, n); _p += len; return s; }
        public byte[] Bytes(int n) { var r = new byte[n]; Array.Copy(_b, _p, r, 0, n); _p += n; return r; }
        public void Skip(int n) { _p += n; }
        /// <summary>RO packed position: 3 bytes → x (10 bits), y (10 bits), dir (4 bits).</summary>
        public (int x, int y, int dir) Pos3()
        {
            int b0 = _b[_p], b1 = _b[_p + 1], b2 = _b[_p + 2]; _p += 3;
            int x = (b0 << 2) | (b1 >> 6);
            int y = ((b1 & 0x3F) << 4) | (b2 >> 4);
            int dir = b2 & 0x0F;
            return (x, y, dir);
        }
        /// <summary>RO packed move: 6 bytes → x0,y0,x1,y1 (+ cell offsets).</summary>
        public (int x0, int y0, int x1, int y1) Pos5()
        {
            int b0 = _b[_p], b1 = _b[_p + 1], b2 = _b[_p + 2], b3 = _b[_p + 3], b4 = _b[_p + 4], b5 = _b[_p + 5]; _p += 6;
            int x0 = (b0 << 2) | (b1 >> 6);
            int y0 = ((b1 & 0x3F) << 4) | (b2 >> 4);
            int x1 = ((b2 & 0x0F) << 6) | (b3 >> 2);
            int y1 = ((b3 & 0x03) << 8) | b4;
            return (x0, y0, x1, y1);
        }
    }
}
