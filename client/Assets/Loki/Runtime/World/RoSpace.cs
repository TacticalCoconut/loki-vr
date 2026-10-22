using UnityEngine;

namespace Loki.World
{
    /// <summary>
    /// Coordinate conventions.
    /// RO world space: x east, y DOWN (negative = higher), z north; 5 RO units = 1 GAT cell; a GND tile = 2 GAT cells = 10 RO units.
    /// Loki world space (Unity): x east, y up, z north; 1 unit = 1 GAT cell.
    /// </summary>
    public static class RoSpace
    {
        public const float UnitsPerCell = 5f;
        public const float Scale = 1f / UnitsPerCell;

        /// <summary>RO world position → Loki world position.</summary>
        public static Vector3 ToLoki(Vector3 ro) => new Vector3(ro.x * Scale, -ro.y * Scale, ro.z * Scale);
        /// <summary>RO direction/normal → Loki (y flipped, no scaling).</summary>
        public static Vector3 DirToLoki(Vector3 ro) => new Vector3(ro.x, -ro.y, ro.z);

        /// <summary>The linear map F = diag(s, -s, s) as a matrix, plus its inverse.</summary>
        public static readonly Matrix4x4 F = Matrix4x4.Scale(new Vector3(Scale, -Scale, Scale));
        public static readonly Matrix4x4 FInv = Matrix4x4.Scale(new Vector3(UnitsPerCell, -UnitsPerCell, UnitsPerCell));

        /// <summary>Loki world position of the centre of GAT cell (cx, cy) at height h (RO units, y-down).</summary>
        public static Vector3 CellCenter(int cx, int cy, float roHeight) => new Vector3(cx + 0.5f, -roHeight * Scale, cy + 0.5f);

        /// <summary>Direction *towards* the RSW light, in Loki space. Mirrors roBrowser's MapRenderer (Ry(long)·Rx(lat)·(0,1,0), negated), then y-flipped.</summary>
        public static Vector3 LightDirection(int longitudeDeg, int latitudeDeg)
        {
            float lon = longitudeDeg * Mathf.Deg2Rad, lat = latitudeDeg * Mathf.Deg2Rad;
            var ro = new Vector3(-Mathf.Sin(lat) * Mathf.Sin(lon), -Mathf.Cos(lat), -Mathf.Sin(lat) * Mathf.Cos(lon));
            return DirToLoki(ro).normalized;
        }
    }
}
