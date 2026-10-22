using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 3b, w3walk: a laser click on the minimap walks there (LokiGame.W3Walk.cs → Game/RouteWalker.cs, in legs when
    /// far). The map viewport (<c>MapRect</c>) gets a pointer target like a button's (UiKit.Button: a box collider in front of the
    /// panel's blocker); the click reads where the laser hit (DioramaRig.PointedWorld, set before a button's Click runs) and
    /// turns it into the cell under it with the minimap's own mapping (<c>LocalToCell</c>, w3social's, zoom included; this branch
    /// has a stand-in, UI/Minimap.W3WalkMapping.cs). (The official client's minimap takes no walk clicks; the VR diorama's map is
    /// the whole table, and the wrist minimap is the quick way across it.)
    /// Client wave 13 (w13look), the brief's item 22 (design note, 2026-10-07: "Yes, make the minimap clickable by laser only"): the
    /// see-through minimap in front of the eyes takes these clicks again, by the LASER ONLY. Its two colliders are on
    /// <see cref="LookSelect.LaserOnlyLayer"/>, which the laser's ray takes and every ray of the look skips (VR/DioramaRig.Ui.cs): the
    /// map viewport is a button (a collider 0.5 px deep, its face in the picture's plane) and the whole display stops the laser behind
    /// it (a blocker, as a panel's background): the laser ends on it and never reaches the world behind; the look never stops on it,
    /// never chooses it and is never blocked by it (a monster behind it is still locked). The cell under the laser is marked while
    /// the laser is on the map (<see cref="LaserCursorShown"/>). It stays a HUD panel (UI/UiKit.cs PanelKinds: it never switches the
    /// casting off). The click's point is where the laser's ray meets the picture's plane (the pointer the game gives).
    /// </summary>
    public sealed partial class Minimap
    {
        /// <summary>A click on the map image: the cell under the laser.</summary>
        public System.Action<Vector2Int> W3WalkCellClicked;
        private System.Func<Vector3?> _w3walkPointer;
        private UiButton _laserMap; private RectTransform _laserCursor;
        /// <summary>Client wave 13 (w13look): the map viewport's button (laser only; null until <see cref="W3WalkEnableClicks"/>), and
        /// whether the cell under the laser is marked now.</summary>
        public UiButton LaserMapButton => _laserMap;
        public bool LaserCursorShown => _laserCursor != null && _laserCursor.gameObject.activeSelf;
        /// <summary>The cell the laser's mark is on (when shown).</summary>
        public Vector2Int LaserCursorCell { get; private set; }

        /// <summary>Makes the map viewport clickable by the laser; <paramref name="pointer"/> = where the laser meets the picture.</summary>
        public void W3WalkEnableClicks(System.Func<Vector3?> pointer)
        {
            _w3walkPointer = pointer;
            var rect = MapRect;
            if (rect == null || _laserMap != null) return;
            var map = LaserOnly(rect, "laser-map", rect.sizeDelta, 0.5f, 0f);
            _laserMap = map.gameObject.AddComponent<UiButton>();
            _laserMap.Init(null, null, () =>
            {
                var p = _w3walkPointer?.Invoke();
                if (p.HasValue) W3WalkClickAt(p.Value);
            });
            var block = LaserOnly(_rt, "laser-block", _rt.sizeDelta, 4f, 3f); // behind the picture, as UiKit.Panel's background collider
            block.gameObject.AddComponent<UiBlocker>();
            _laserCursor = UiKit.Box(rect, "laser-cursor", Vector2.zero, new Vector2(14f, 14f) * _dotScale, new Color(1f, 0.92f, 0.45f, 0.95f));
            UiKit.Box(_laserCursor, "laser-cursor-core", Vector2.zero, new Vector2(6f, 6f) * _dotScale, new Color(0.05f, 0.05f, 0.08f, 0.95f));
            _laserCursor.gameObject.SetActive(false);
        }

        /// <summary>A collider (and nothing drawn) on the layer only the laser meets, over <paramref name="parent"/>'s middle.</summary>
        private static RectTransform LaserOnly(RectTransform parent, string name, Vector2 size, float depthPx, float centreZ)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false); go.layer = LookSelect.LaserOnlyLayer;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero; rt.sizeDelta = size;
            var col = go.AddComponent<BoxCollider>(); col.size = new Vector3(size.x, size.y, depthPx); col.center = new Vector3(0f, 0f, centreZ);
            return rt;
        }

        /// <summary>Client wave 13 (w13look): the mark on the cell under the laser, while the laser is on the map (from Minimap.LateUpdate).</summary>
        private void UpdateLaserCursor()
        {
            if (_laserCursor == null) return;
            bool on = _laserMap != null && _laserMap.Hovered && _w3walkPointer != null;
            Vector2Int cell = default;
            if (on) { var p = _w3walkPointer(); on = p.HasValue && CellAt(p.Value, out cell); }
            if (_laserCursor.gameObject.activeSelf != on) _laserCursor.gameObject.SetActive(on);
            if (!on) return;
            LaserCursorCell = cell; _laserCursor.anchoredPosition = CellToLocal(cell); _laserCursor.SetAsLastSibling();
        }

        /// <summary>The click itself, at a world point on the map viewport (the smoke calls it with <see cref="W3WalkWorldOf"/>).</summary>
        public bool W3WalkClickAt(Vector3 world)
        {
            if (!CellAt(world, out var cell)) return false;
            W3WalkCellClicked?.Invoke(cell);
            return true;
        }

        /// <summary>The map cell drawn under a world point of the map viewport; false outside the viewport or the map.</summary>
        public bool CellAt(Vector3 world, out Vector2Int cell)
        {
            cell = default;
            var rect = MapRect;
            if (rect == null) return false;
            return LocalToCell((Vector2)rect.InverseTransformPoint(world), out cell);
        }

        /// <summary>The world point where a cell is drawn on the map viewport.</summary>
        public Vector3 W3WalkWorldOf(Vector2 cell)
        {
            var p = CellToLocal(cell);
            return MapRect.TransformPoint(new Vector3(p.x, p.y, 0f));
        }
    }
}
