using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Loki.IO
{
    /// <summary>
    /// Little-endian binary reader for Gravity file formats.
    /// Strings are kept as Latin-1 (byte-exact) so EUC-KR paths survive round trips
    /// into GRF lookups without needing a code-page provider at runtime.
    /// </summary>
    public sealed class RoReader : IDisposable
    {
        public static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        private readonly BinaryReader _r;
        public readonly Stream BaseStream;

        public RoReader(byte[] data) : this(new MemoryStream(data, false)) { }
        public RoReader(Stream s)
        {
            BaseStream = s;
            _r = new BinaryReader(s);
        }

        public long Position { get => BaseStream.Position; set => BaseStream.Position = value; }
        public long Length => BaseStream.Length;
        public bool AtEnd => BaseStream.Position >= BaseStream.Length;

        public byte U8() => _r.ReadByte();
        public sbyte S8() => _r.ReadSByte();
        public ushort U16() => _r.ReadUInt16();
        public short S16() => _r.ReadInt16();
        public uint U32() => _r.ReadUInt32();
        public int S32() => _r.ReadInt32();
        public float F32() => _r.ReadSingle();
        public byte[] Bytes(int n) => _r.ReadBytes(n);
        public void Skip(int n) => BaseStream.Seek(n, SeekOrigin.Current);

        public Vector3 Vec3() => new Vector3(F32(), F32(), F32());
        public Vector2 Vec2() => new Vector2(F32(), F32());
        public Quaternion Quat() { float x = F32(), y = F32(), z = F32(), w = F32(); return new Quaternion(x, y, z, w); }

        /// <summary>Fixed-length, NUL-terminated string (Latin-1).</summary>
        public string Str(int len)
        {
            var b = _r.ReadBytes(len);
            int n = Array.IndexOf(b, (byte)0);
            if (n < 0) n = b.Length;
            return Latin1.GetString(b, 0, n);
        }

        /// <summary>Length-prefixed (u32) string, used by RSM 2.2+.</summary>
        public string StrU32() => Str((int)U32());

        public string Signature(int len) => Encoding.ASCII.GetString(_r.ReadBytes(len));

        public void Dispose() { _r.Dispose(); }
    }

    /// <summary>Two-byte version with helpers. Major-first for GRSW/GRGN/GRSM/GRAT, minor-first for SP/AC.</summary>
    public readonly struct RoVersion
    {
        public readonly byte Major, Minor;
        public RoVersion(byte major, byte minor) { Major = major; Minor = minor; }
        public static RoVersion MajorFirst(RoReader r) { byte a = r.U8(), b = r.U8(); return new RoVersion(a, b); }
        public static RoVersion MinorFirst(RoReader r) { byte b = r.U8(), a = r.U8(); return new RoVersion(a, b); }
        public bool AtLeast(int major, int minor) => Major > major || (Major == major && Minor >= minor);
        public bool Below(int major, int minor) => !AtLeast(major, minor);
        public override string ToString() => $"{Major}.{Minor}";
    }
}
