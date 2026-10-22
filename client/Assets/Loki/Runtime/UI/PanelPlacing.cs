using System;
using Loki.World;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 24 (w24): where the LOGIN PANEL and the KEYBOARD stand, and that they never stand in the ground.
    ///   design note (2026-10-12): "require the login panel to be draggable and also the keyboard panel to be draggable" and
    ///   "if the headset was close to the ground level of the game's table, when the game opens, the panel clips through the ground and
    ///   I can't see the character that I'm about to create ... the panel should not be allowed to clip through the ground of whatever
    ///   map it is placed on".
    ///   Place   a panel is placed from THE EYES, in the head's level frame (right, up, ahead; the head's heading, never its pitch or
    ///           roll): at its default (the login panel 95 cm ahead and 2 cm up, facing the head; the keyboard 50 cm ahead and 42 cm
    ///           down, tilted to the head: both as before), or where the player put it (<see cref="Capture"/>, kept in the same frame
    ///           and held within <see cref="KeptMin"/> / <see cref="KeptMax"/>, so a kept place is always in front and in reach).
    ///   Eyes    the head's own place, but never lower than <see cref="LowEyeAboveGround"/> above the ground under it (the table's
    ///           level where no ground is under the head): a headset that starts on the floor or the desk gets the panel where a
    ///           seated player's eyes would be, not in the floor. A head higher than that: its own place, nothing changes.
    ///   Guard   the panel's lowest edge, its visible frame included (UI/PanelGrab.cs FramePx), is kept <see cref="GroundMargin"/>
    ///           above the map's ground under it: when it is placed, every frame while it shows (<see cref="PanelGroundGuard"/>: a
    ///           recentre, a view change, the map moving under it), and while it is dragged: a drag downward stops at the margin
    ///           (the panel is only ever lifted, straight up). Where no ground is under the panel there is nothing to clear.
    /// The ground comes from the game (<see cref="GroundUnder"/>: the loaded map's ground mesh; LokiGame.W24Login.cs).
    /// </summary>
    public static class PanelPlacing
    {
        /// <summary>Metres the lowest edge stays above the ground under it (FLAGGED: the orchestrator's default, 5 cm).</summary>
        public const float GroundMargin = 0.05f;
        /// <summary>Metres above the ground under the head below which the panels are no longer placed from the head's own height
        /// (FLAGGED: Loki's number; a seated player's eyes are about 45 cm over the seated table, a standing one's 65 cm).</summary>
        public const float LowEyeAboveGround = 0.40f;
        /// <summary>A kept place's limits in the head's level frame (right, up, ahead), metres.</summary>
        public static readonly Vector3 KeptMin = new Vector3(-1.2f, -0.9f, 0.15f), KeptMax = new Vector3(1.2f, 0.7f, 1.6f);
        /// <summary>The height of the map's ground under a world point (null: no ground there). Set by the game.</summary>
        public static Func<Vector3, float?> GroundUnder;
        /// <summary>The table's level (the ground of the cell the table is centred on), for a head with no ground under it.</summary>
        public static Func<float?> TableLevel;
        /// <summary>Times the guard lifted a panel (smoke, log).</summary>
        public static int Lifts { get; private set; }

        /// <summary>The eyes a panel is placed from (the summary's "Eyes").</summary>
        public static Vector3 Eyes(Camera head)
        {
            var p = head.transform.position;
            float? g = GroundUnder != null ? GroundUnder(p) : null;
            float? t = TableLevel != null ? TableLevel() : null;
            if (t.HasValue && (!g.HasValue || g.Value < t.Value)) g = t;
            if (!g.HasValue) g = 0.95f;
            if (p.y < g.Value + LowEyeAboveGround) p.y = g.Value + LowEyeAboveGround;
            return p;
        }

        /// <summary>The head's heading: a turn about the vertical only.</summary>
        public static Quaternion Heading(Camera head)
        {
            var f = head.transform.forward; f.y = 0f; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            return Quaternion.LookRotation(f.normalized, Vector3.up);
        }

        private static Vector3 Clamp(Vector3 v) => new Vector3(Mathf.Clamp(v.x, KeptMin.x, KeptMax.x), Mathf.Clamp(v.y, KeptMin.y, KeptMax.y), Mathf.Clamp(v.z, KeptMin.z, KeptMax.z));

        /// <summary>Places a panel: at its kept place (<paramref name="kept"/> not null), else <paramref name="fallback"/> metres from
        /// the eyes (right, up, ahead) and facing the head; enforces <paramref name="minSpawnDist"/> on initial placement when > 0; then the ground guard.</summary>
        public static void Place(Transform panel, Camera head, UserSettings.PadPlace kept, Vector3 fallback, float minSpawnDist = 0f)
        {
            if (panel == null || head == null) return;
            var eyes = Eyes(head); var yaw = Heading(head);
            if (kept != null)
            {
                var q = new Quaternion(kept.qx, kept.qy, kept.qz, kept.qw);
                float n = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
                q = n < 0.5f || float.IsNaN(n) ? Quaternion.identity : q.normalized; // (a broken turn in the file: none)
                var off = Clamp(new Vector3(kept.x, kept.y, kept.z));
                if (minSpawnDist > 0f && off.z < minSpawnDist) off.z = minSpawnDist;
                panel.position = eyes + yaw * off;
                panel.rotation = yaw * q;
            }
            else
            {
                var off = fallback;
                if (minSpawnDist > 0f && off.z < minSpawnDist) off.z = minSpawnDist;
                panel.position = eyes + yaw * off;
                UiKit.FaceHead(panel, head);
            }
            if (minSpawnDist > 0f)
            {
                var flat = panel.position - head.transform.position; flat.y = 0f;
                float flatLen = flat.magnitude;
                if (flatLen < minSpawnDist - 1e-4f)
                {
                    var dir = flatLen > 1e-4f ? flat / flatLen : (yaw * Vector3.forward);
                    panel.position = new Vector3(head.transform.position.x + dir.x * minSpawnDist, panel.position.y, head.transform.position.z + dir.z * minSpawnDist);
                }
            }
            if (panel is RectTransform rt) Guard(rt);
        }

        /// <summary>A panel's place as it stands now, to keep (the head's level frame at the eyes).</summary>
        public static UserSettings.PadPlace Capture(Transform panel, Camera head)
        {
            var inv = Quaternion.Inverse(Heading(head));
            var l = Clamp(inv * (panel.position - Eyes(head))); var q = inv * panel.rotation;
            return new UserSettings.PadPlace { x = l.x, y = l.y, z = l.z, qx = q.x, qy = q.y, qz = q.z, qw = q.w };
        }

        /// <summary>Item 6: saves a menu window's position, rotation, and scale relative to the player's vision.</summary>
        public static void SaveMenuPlace(string key, Transform panel, Camera head, float scale = 1f)
        {
            if (string.IsNullOrEmpty(key) || panel == null || head == null) return;
            var inv = Quaternion.Inverse(Heading(head));
            var l = Clamp(inv * (panel.position - head.transform.position)); var q = inv * panel.rotation;
            UserSettings.SetMenuPlace(key, new UserSettings.PadPlace { x = l.x, y = l.y, z = l.z, qx = q.x, qy = q.y, qz = q.z, qw = q.w }, scale);
            UserSettings.Save();
        }

        /// <summary>Item 6: restores a menu window's saved position, rotation, and scale relative to the player's current vision if one was saved.</summary>
        public static bool TryPlaceMenu(string key, Transform panel, Camera head, out float scale)
        {
            scale = 1f;
            if (string.IsNullOrEmpty(key) || panel == null || head == null) return false;
            var m = UserSettings.MenuPlaceOf(key);
            if (m == null || m.place == null) return false;
            scale = m.scale > 0f ? m.scale : 1f;
            var yaw = Heading(head); var kept = m.place;
            var q = new Quaternion(kept.qx, kept.qy, kept.qz, kept.qw);
            float n = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            q = n < 0.5f || float.IsNaN(n) ? Quaternion.identity : q.normalized;
            panel.position = head.transform.position + yaw * Clamp(new Vector3(kept.x, kept.y, kept.z));
            panel.rotation = yaw * q;
            return true;
        }

        /// <summary>The panel's lowest point now, its visible frame included (world height).</summary>
        public static float LowestEdge(RectTransform rt)
        {
            float px = PanelGrab.FramePx(rt); var r = rt.rect; float low = float.MaxValue;
            for (int k = 0; k < 4; k++)
                low = Mathf.Min(low, rt.TransformPoint(new Vector3(k < 2 ? r.xMin - px : r.xMax + px, (k & 1) == 0 ? r.yMin - px : r.yMax + px, 0f)).y);
            return low;
        }

        /// <summary>How far the panel must rise so that its frame's lower and upper edge (five points each) and
        /// <paramref name="also"/> (a point that belongs to it: the creation character's feet) are <see cref="GroundMargin"/>
        /// above the ground under them (0: it is clear, or no ground is under it).</summary>
        public static float Need(RectTransform rt, Vector3? also = null)
        {
            if (rt == null || (GroundUnder == null && TableLevel == null)) return 0f;
            float px = PanelGrab.FramePx(rt); var r = rt.rect; float lift = 0f;
            float? tbl = TableLevel != null ? TableLevel() : null;
            for (int k = 0; k < 2; k++)
                for (int i = 0; i <= 4; i++)
                {
                    var p = rt.TransformPoint(new Vector3(Mathf.Lerp(r.xMin - px, r.xMax + px, i / 4f), k == 0 ? r.yMin - px : r.yMax + px, 0f));
                    float? g = GroundUnder != null ? GroundUnder(p) : null;
                    if (!g.HasValue || (tbl.HasValue && g.Value < tbl.Value)) g = tbl;
                    if (g.HasValue) lift = Mathf.Max(lift, g.Value + GroundMargin - p.y);
                }
            if (also.HasValue)
            {
                float? g = GroundUnder != null ? GroundUnder(also.Value) : null;
                if (!g.HasValue || (tbl.HasValue && g.Value < tbl.Value)) g = tbl;
                if (g.HasValue) lift = Mathf.Max(lift, g.Value + GroundMargin - also.Value.y);
            }
            return lift;
        }

        /// <summary>The guard: lifts the panel straight up by what <see cref="Need"/> says. Returns the lift (metres).</summary>
        public static float Guard(RectTransform rt, Vector3? also = null)
        {
            float lift = Need(rt, also);
            if (lift <= 1e-5f) return 0f;
            rt.position += Vector3.up * lift; Lifts++;
            return lift;
        }
    }

    /// <summary>Client wave 24 (w24): the ground guard of one panel (the login panel, the keyboard), every frame while it shows,
    /// after everything that moves it (LateUpdate: a drag's carry runs in Update). <see cref="Also"/>: a point that belongs to
    /// the panel and must be clear too.</summary>
    [DefaultExecutionOrder(900)]
    public sealed class PanelGroundGuard : MonoBehaviour
    {
        public Func<Vector3?> Also;
        /// <summary>The last frame's lift, metres (0: the panel was clear); lifts so far (smoke).</summary>
        public float LastLift { get; private set; }
        public int Lifts { get; private set; }
        private void LateUpdate()
        {
            LastLift = PanelPlacing.Guard(transform as RectTransform, Also != null ? Also() : null);
            if (LastLift > 0f) Lifts++;
        }
    }
}
