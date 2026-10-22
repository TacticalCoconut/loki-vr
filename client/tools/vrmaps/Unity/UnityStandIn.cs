// vrmaps: a stand-in for the parts of UnityEngine that the client's own map code uses (Formats/*, IO/*, World/RsmMeshBuilder.cs,
// World/GroundMeshBuilder.cs, World/RoMat.cs, World/RoSpace.cs, Data/RoomTools.cs), so this offline tool compiles those very files
// (no copies) and reads and builds every map exactly as the game does. The math follows Unity's documented semantics (Matrix4x4 is
// mRC = row R, column C; M * v; MultiplyPoint3x4 ignores the bottom row; Vector3.normalized is zero below 1e-5; Lerp clamps t).
// Mesh does not draw: it RECORDS the vertices and the triangles (per submesh, in the builder's index order = its winding) that the
// game would upload, and the analysis reads them back (Scene.cs). Texture2D keeps its pixels; LoadImage decodes JPG / PNG with the
// installed Windows Desktop runtime (GDI+), as Unity's LoadImage does for the game (IO/ImageDecoders.cs:20-27).
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 one => new Vector2(1, 1);
        public float magnitude => (float)Math.Sqrt(x * x + y * y);
        public float sqrMagnitude => x * x + y * y;
        public static Vector2 Scale(Vector2 a, Vector2 b) => new Vector2(a.x * b.x, a.y * b.y);
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator *(float d, Vector2 a) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator /(Vector2 a, float d) => new Vector2(a.x / d, a.y / d);
        public override string ToString() => $"({x:F2}, {y:F2})";
    }

    public struct Vector2Int
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public override string ToString() => $"({x}, {y})";
    }

    public struct Vector3
    {
        public const float kEpsilon = 0.00001F;
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 down => new Vector3(0, -1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 back => new Vector3(0, 0, -1);
        public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 left => new Vector3(-1, 0, 0);
        public float this[int i] { get => i == 0 ? x : i == 1 ? y : i == 2 ? z : throw new IndexOutOfRangeException(); set { if (i == 0) x = value; else if (i == 1) y = value; else if (i == 2) z = value; else throw new IndexOutOfRangeException(); } }
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public float sqrMagnitude => x * x + y * y + z * z;
        public Vector3 normalized { get { float m = magnitude; return m > kEpsilon ? this / m : zero; } }
        public void Normalize() { this = normalized; }
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Min(Vector3 a, Vector3 b) => new Vector3(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Min(a.z, b.z));
        public static Vector3 Max(Vector3 a, Vector3 b) => new Vector3(Math.Max(a.x, b.x), Math.Max(a.y, b.y), Math.Max(a.z, b.z));
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { t = Mathf.Clamp01(t); return new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t); }
        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        // Unity: equal when the squared distance is below 1e-5 squared
        public static bool operator ==(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < kEpsilon * kEpsilon;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public override bool Equals(object o) => o is Vector3 v && x == v.x && y == v.y && z == v.z;
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2);
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0);
        public override string ToString() => $"({x:F2}, {y:F2}, {z:F2})";
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Vector4 zero => new Vector4(0, 0, 0, 0);
        public static implicit operator Vector3(Vector4 v) => new Vector3(v.x, v.y, v.z);
        public static implicit operator Vector4(Vector3 v) => new Vector4(v.x, v.y, v.z, 0f);
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity => new Quaternion(0, 0, 0, 1);
        public static float Dot(Quaternion a, Quaternion b) => a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
        /// <summary>Unity's Slerp: t clamped to 0..1, the shorter arc, normalized.</summary>
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t)
        {
            t = Mathf.Clamp01(t);
            float d = Dot(a, b);
            if (d < 0f) { b = new Quaternion(-b.x, -b.y, -b.z, -b.w); d = -d; }
            float ka, kb;
            if (d > 0.9995f) { ka = 1f - t; kb = t; }
            else
            {
                double th = Math.Acos(d), s = Math.Sin(th);
                ka = (float)(Math.Sin((1 - t) * th) / s); kb = (float)(Math.Sin(t * th) / s);
            }
            var q = new Quaternion(a.x * ka + b.x * kb, a.y * ka + b.y * kb, a.z * ka + b.z * kb, a.w * ka + b.w * kb);
            float m = (float)Math.Sqrt(Dot(q, q));
            return m > 0 ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : identity;
        }
    }

    /// <summary>Unity's Matrix4x4: fields mRC (row R, column C), column vectors (M * v), identity by default construction is ZERO
    /// (as Unity's struct): use <see cref="identity"/>.</summary>
    public struct Matrix4x4
    {
        public float m00, m10, m20, m30, m01, m11, m21, m31, m02, m12, m22, m32, m03, m13, m23, m33;

        public float this[int row, int col]
        {
            get => this[row + col * 4];
            set => this[row + col * 4] = value;
        }
        public float this[int i]
        {
            get
            {
                switch (i)
                {
                    case 0: return m00; case 1: return m10; case 2: return m20; case 3: return m30;
                    case 4: return m01; case 5: return m11; case 6: return m21; case 7: return m31;
                    case 8: return m02; case 9: return m12; case 10: return m22; case 11: return m32;
                    case 12: return m03; case 13: return m13; case 14: return m23; case 15: return m33;
                    default: throw new IndexOutOfRangeException();
                }
            }
            set
            {
                switch (i)
                {
                    case 0: m00 = value; break; case 1: m10 = value; break; case 2: m20 = value; break; case 3: m30 = value; break;
                    case 4: m01 = value; break; case 5: m11 = value; break; case 6: m21 = value; break; case 7: m31 = value; break;
                    case 8: m02 = value; break; case 9: m12 = value; break; case 10: m22 = value; break; case 11: m32 = value; break;
                    case 12: m03 = value; break; case 13: m13 = value; break; case 14: m23 = value; break; case 15: m33 = value; break;
                    default: throw new IndexOutOfRangeException();
                }
            }
        }

        public static Matrix4x4 identity { get { var m = new Matrix4x4(); m.m00 = m.m11 = m.m22 = m.m33 = 1f; return m; } }
        public static Matrix4x4 zero => new Matrix4x4();

        public static Matrix4x4 Scale(Vector3 v) { var m = new Matrix4x4(); m.m00 = v.x; m.m11 = v.y; m.m22 = v.z; m.m33 = 1f; return m; }

        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b)
        {
            var r = new Matrix4x4();
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    r[i, j] = a[i, 0] * b[0, j] + a[i, 1] * b[1, j] + a[i, 2] * b[2, j] + a[i, 3] * b[3, j];
            return r;
        }

        public static Vector4 operator *(Matrix4x4 m, Vector4 v) => new Vector4(
            m.m00 * v.x + m.m01 * v.y + m.m02 * v.z + m.m03 * v.w,
            m.m10 * v.x + m.m11 * v.y + m.m12 * v.z + m.m13 * v.w,
            m.m20 * v.x + m.m21 * v.y + m.m22 * v.z + m.m23 * v.w,
            m.m30 * v.x + m.m31 * v.y + m.m32 * v.z + m.m33 * v.w);

        public Vector3 MultiplyPoint3x4(Vector3 p) => new Vector3(
            m00 * p.x + m01 * p.y + m02 * p.z + m03,
            m10 * p.x + m11 * p.y + m12 * p.z + m13,
            m20 * p.x + m21 * p.y + m22 * p.z + m23);

        public Vector3 MultiplyPoint(Vector3 p)
        {
            var v = MultiplyPoint3x4(p);
            float w = m30 * p.x + m31 * p.y + m32 * p.z + m33;
            w = 1f / w;
            return new Vector3(v.x * w, v.y * w, v.z * w);
        }

        public Vector3 MultiplyVector(Vector3 v) => new Vector3(
            m00 * v.x + m01 * v.y + m02 * v.z,
            m10 * v.x + m11 * v.y + m12 * v.z,
            m20 * v.x + m21 * v.y + m22 * v.z);

        public Vector4 GetColumn(int c) => new Vector4(this[0, c], this[1, c], this[2, c], this[3, c]);
        public Vector4 GetRow(int r) => new Vector4(this[r, 0], this[r, 1], this[r, 2], this[r, 3]);
        public void SetColumn(int c, Vector4 v) { this[0, c] = v.x; this[1, c] = v.y; this[2, c] = v.z; this[3, c] = v.w; }
        public void SetRow(int r, Vector4 v) { this[r, 0] = v.x; this[r, 1] = v.y; this[r, 2] = v.z; this[r, 3] = v.w; }

        public Matrix4x4 transpose
        {
            get { var r = new Matrix4x4(); for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) r[i, j] = this[j, i]; return r; }
        }

        public float determinant
        {
            get
            {
                // cofactor expansion along the first row (double precision)
                var m = this;
                double Det3(int r0, int r1, int r2, int c0, int c1, int c2) =>
                    (double)m[r0, c0] * ((double)m[r1, c1] * m[r2, c2] - (double)m[r1, c2] * m[r2, c1])
                  - (double)m[r0, c1] * ((double)m[r1, c0] * m[r2, c2] - (double)m[r1, c2] * m[r2, c0])
                  + (double)m[r0, c2] * ((double)m[r1, c0] * m[r2, c1] - (double)m[r1, c1] * m[r2, c0]);
                double d = m[0, 0] * Det3(1, 2, 3, 1, 2, 3) - m[0, 1] * Det3(1, 2, 3, 0, 2, 3)
                         + m[0, 2] * Det3(1, 2, 3, 0, 1, 3) - m[0, 3] * Det3(1, 2, 3, 0, 1, 2);
                return (float)d;
            }
        }

        /// <summary>The rotation of a TRS matrix (only for compiling World/RsmMeshBuilder.cs ApplyInstanceTransform, which the tool does
        /// not call): the normalized columns' quaternion.</summary>
        public Quaternion rotation
        {
            get
            {
                var c0 = ((Vector3)GetColumn(0)).normalized; var c1 = ((Vector3)GetColumn(1)).normalized; var c2 = ((Vector3)GetColumn(2)).normalized;
                float tr = c0.x + c1.y + c2.z; double qw, qx, qy, qz;
                if (tr > 0) { double s = Math.Sqrt(tr + 1.0) * 2; qw = 0.25 * s; qx = (c1.z - c2.y) / s; qy = (c2.x - c0.z) / s; qz = (c0.y - c1.x) / s; }
                else if (c0.x > c1.y && c0.x > c2.z) { double s = Math.Sqrt(1.0 + c0.x - c1.y - c2.z) * 2; qw = (c1.z - c2.y) / s; qx = 0.25 * s; qy = (c1.x + c0.y) / s; qz = (c2.x + c0.z) / s; }
                else if (c1.y > c2.z) { double s = Math.Sqrt(1.0 + c1.y - c0.x - c2.z) * 2; qw = (c2.x - c0.z) / s; qx = (c1.x + c0.y) / s; qy = 0.25 * s; qz = (c2.y + c1.z) / s; }
                else { double s = Math.Sqrt(1.0 + c2.z - c0.x - c1.y) * 2; qw = (c0.y - c1.x) / s; qx = (c2.x + c0.z) / s; qy = (c2.y + c1.z) / s; qz = 0.25 * s; }
                return new Quaternion((float)qx, (float)qy, (float)qz, (float)qw);
            }
        }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1, 1);
        public static Color black => new Color(0, 0, 0, 1);
        public static Color gray => new Color(0.5f, 0.5f, 0.5f, 1);
        public static Color operator *(Color c, float d) => new Color(c.r * d, c.g * d, c.b * d, c.a * d);
        public static Color operator *(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public const float Deg2Rad = PI * 2f / 360f;
        public const float Rad2Deg = 1f / Deg2Rad;
        public const float Infinity = float.PositiveInfinity;
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Tan(float f) => (float)Math.Tan(f);
        public static float Asin(float f) => (float)Math.Asin(f);
        public static float Acos(float f) => (float)Math.Acos(f);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Abs(float f) => Math.Abs(f);
        public static int Abs(int f) => Math.Abs(f);
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Floor(float f) => (float)Math.Floor(f);
        public static float Round(float f) => (float)Math.Round(f);
        public static float Ceil(float f) => (float)Math.Ceiling(f);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static int CeilToInt(float f) => (int)Math.Ceiling(f);
        public static int RoundToInt(float f) => (int)Math.Round(f);
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float Repeat(float t, float length) => Clamp(t - Floor(t / length) * length, 0f, length);
        public static float MoveTowards(float cur, float target, float maxDelta) => Math.Abs(target - cur) <= maxDelta ? target : cur + Math.Sign(target - cur) * maxDelta;
        public static bool Approximately(float a, float b) => Math.Abs(b - a) < Math.Max(1E-06f * Math.Max(Math.Abs(a), Math.Abs(b)), float.Epsilon * 8f);
    }

    public class Object
    {
        public string name;
        public static void Destroy(Object o) { }
        public static void DestroyImmediate(Object o) { }
    }

    public class Component : Object { public Transform transform; }
    public class Behaviour : Component { public bool enabled = true; }
    public class MonoBehaviour : Behaviour { }
    public class Transform : Component { public Vector3 localPosition, localScale = Vector3.one; public Quaternion localRotation = Quaternion.identity; }

    public static class Time { public static float time, deltaTime, unscaledTime; }
    public static class Application { public static bool isPlaying => false; public static string streamingAssetsPath => ""; }

    public static class Debug
    {
        /// <summary>The client code's log lines (a texture that does not decode, ...): kept for the report's notes.</summary>
        public static readonly List<string> Lines = new List<string>();
        public static void Log(object m) { lock (Lines) Lines.Add("" + m); }
        public static void LogWarning(object m) { lock (Lines) Lines.Add("warning: " + m); }
        public static void LogError(object m) { lock (Lines) Lines.Add("error: " + m); }
        public static void LogException(Exception e) { lock (Lines) Lines.Add("exception: " + e.Message); }
    }

    public enum TextureFormat { Alpha8 = 1, RGB24 = 3, RGBA32 = 4, ARGB32 = 5, R8 = 63 }
    public enum TextureWrapMode { Repeat = 0, Clamp = 1 }
    public enum FilterMode { Point = 0, Bilinear = 1, Trilinear = 2 }

    /// <summary>Records what the game's mesh builders hand to Unity.</summary>
    public class Mesh : Object
    {
        public Rendering.IndexFormat indexFormat;
        public Vector3[] Vertices = new Vector3[0];
        public Vector3[] Normals = new Vector3[0];
        public Vector2[] Uv0 = new Vector2[0], Uv1 = new Vector2[0];
        public Color32[] Colors = new Color32[0];
        private List<int[]> _sub = new List<int[]>();
        public int subMeshCount
        {
            get => _sub.Count;
            set { var n = new List<int[]>(); for (int i = 0; i < value; i++) n.Add(i < _sub.Count ? _sub[i] : new int[0]); _sub = n; }
        }
        public int[] Triangles(int submesh) => _sub[submesh];
        public void SetVertices(List<Vector3> v) => Vertices = v.ToArray();
        public void SetNormals(List<Vector3> v) => Normals = v.ToArray();
        public void SetUVs(int channel, List<Vector2> v) { if (channel == 0) Uv0 = v.ToArray(); else if (channel == 1) Uv1 = v.ToArray(); }
        public void SetUVs(int channel, List<Vector4> v) { }
        public void SetColors(List<Color32> c) => Colors = c.ToArray();
        public void SetTriangles(List<int> t, int submesh, bool calculateBounds = true) { while (_sub.Count <= submesh) _sub.Add(new int[0]); _sub[submesh] = t.ToArray(); }
        public void RecalculateBounds() { }
        public void RecalculateNormals() { }
        public void MarkDynamic() { }
    }

    public class Texture2D : Object
    {
        public int width, height; public TextureWrapMode wrapMode, wrapModeU, wrapModeV; public FilterMode filterMode;
        private Color32[] _px;
        public Texture2D(int w, int h, TextureFormat f, bool mipmaps) : this(w, h, f, mipmaps, false) { }
        public Texture2D(int w, int h, TextureFormat f, bool mipmaps, bool linear) { width = w; height = h; _px = new Color32[w * h]; }
        public void SetPixels32(Color32[] px) => _px = (Color32[])px.Clone();
        /// <summary>Rows bottom-up, as Unity's.</summary>
        public Color32[] GetPixels32() => (Color32[])_px.Clone();
        public void Apply(bool updateMipmaps = true, bool makeNoLongerReadable = false) { }
        /// <summary>JPG / PNG through GDI+ (the installed Windows Desktop runtime); false when it cannot decode.</summary>
        public bool LoadImage(byte[] data, bool markNonReadable = false)
        {
            try
            {
                using (var ms = new System.IO.MemoryStream(data))
                using (var bmp = new System.Drawing.Bitmap(ms))
                {
                    int w = bmp.Width, h = bmp.Height;
                    var rect = new System.Drawing.Rectangle(0, 0, w, h);
                    var bd = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    var raw = new byte[bd.Stride * h];
                    System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, raw, 0, raw.Length);
                    bmp.UnlockBits(bd);
                    var px = new Color32[w * h];
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int o = y * bd.Stride + x * 4; // B G R A, top-down
                            px[x + (h - 1 - y) * w] = new Color32(raw[o + 2], raw[o + 1], raw[o], raw[o + 3]);
                        }
                    width = w; height = h; _px = px;
                    return true;
                }
            }
            catch (Exception) { return false; }
        }
    }
}

namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt16 = 0, UInt32 = 1 }
}
