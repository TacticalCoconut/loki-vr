using UnityEngine;

namespace Loki.World
{
    /// <summary>
    /// Explicit 4x4 matrix helpers using the classic column-vector convention (M * v), matching glMatrix / the original
    /// client's math, so RO-space transforms can be composed without depending on Unity's handedness conventions.
    /// </summary>
    public static class RoMat
    {
        public static Matrix4x4 Translate(Vector3 t)
        {
            var m = Matrix4x4.identity; m.m03 = t.x; m.m13 = t.y; m.m23 = t.z; return m;
        }
        public static Matrix4x4 Scale(Vector3 s)
        {
            var m = Matrix4x4.identity; m.m00 = s.x; m.m11 = s.y; m.m22 = s.z; return m;
        }
        public static Matrix4x4 RotX(float rad)
        {
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            var m = Matrix4x4.identity; m.m11 = c; m.m12 = -s; m.m21 = s; m.m22 = c; return m;
        }
        public static Matrix4x4 RotY(float rad)
        {
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            var m = Matrix4x4.identity; m.m00 = c; m.m02 = s; m.m20 = -s; m.m22 = c; return m;
        }
        public static Matrix4x4 RotZ(float rad)
        {
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            var m = Matrix4x4.identity; m.m00 = c; m.m01 = -s; m.m10 = s; m.m11 = c; return m;
        }
        /// <summary>Rodrigues rotation about an arbitrary axis (glMatrix mat4.rotate).</summary>
        public static Matrix4x4 AxisAngle(Vector3 axis, float rad)
        {
            float len = axis.magnitude;
            if (len < 1e-6f || Mathf.Abs(rad) < 1e-9f) return Matrix4x4.identity;
            axis /= len;
            float x = axis.x, y = axis.y, z = axis.z;
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad), t = 1 - c;
            var m = Matrix4x4.identity;
            m.m00 = x * x * t + c;     m.m01 = x * y * t - z * s; m.m02 = x * z * t + y * s;
            m.m10 = y * x * t + z * s; m.m11 = y * y * t + c;     m.m12 = y * z * t - x * s;
            m.m20 = z * x * t - y * s; m.m21 = z * y * t + x * s; m.m22 = z * z * t + c;
            return m;
        }
        /// <summary>Rotation matrix from a (x, y, z, w) quaternion (glMatrix mat4.fromQuat convention).</summary>
        public static Matrix4x4 FromQuat(Quaternion q)
        {
            float x = q.x, y = q.y, z = q.z, w = q.w;
            float x2 = x + x, y2 = y + y, z2 = z + z;
            float xx = x * x2, yx = y * x2, yy = y * y2, zx = z * x2, zy = z * y2, zz = z * z2, wx = w * x2, wy = w * y2, wz = w * z2;
            var m = Matrix4x4.identity;
            m.m00 = 1 - yy - zz; m.m01 = yx - wz;     m.m02 = zx + wy;
            m.m10 = yx + wz;     m.m11 = 1 - xx - zz; m.m12 = zy - wx;
            m.m20 = zx - wy;     m.m21 = zy + wx;     m.m22 = 1 - xx - yy;
            return m;
        }
        /// <summary>3x3 stored column-major (9 floats) → 4x4.</summary>
        public static Matrix4x4 FromMat3ColumnMajor(float[] f)
        {
            var m = Matrix4x4.identity;
            m.m00 = f[0]; m.m10 = f[1]; m.m20 = f[2];
            m.m01 = f[3]; m.m11 = f[4]; m.m21 = f[5];
            m.m02 = f[6]; m.m12 = f[7]; m.m22 = f[8];
            return m;
        }
        public static Vector3 MulPoint(in Matrix4x4 m, Vector3 v) => m.MultiplyPoint3x4(v);
        public static Vector3 MulDir(in Matrix4x4 m, Vector3 v) => m.MultiplyVector(v);
    }
}
