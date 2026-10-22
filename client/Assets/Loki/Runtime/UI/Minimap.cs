using System.Collections.Generic;
using Loki.Formats;
using Loki.Sprites;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Toggleable minimap floating by the left hand: the client's own data\texture\유저인터페이스\map\&lt;map&gt;.bmp (it covers the
    /// whole map, north up, like the original minimap), or the walkable grid when a map has no image. Dots: you (white,
    /// with a facing tick for where you look), monsters (red), NPCs (green), portals (cyan), items (yellow).
    /// On indoor maps only the dots of the room you are in are shown.
    /// Client wave 3 (w3social): zoom ×1 / ×2 / ×4 with the "-" / "+" buttons (the view follows the character, clamped to the map's
    /// edges: the texture's uvRect shows a part of the image, dots outside it are not drawn), and a "World" button that opens the world
    /// map (UI/WorldMapWindow.cs). <see cref="MapRect"/> / <see cref="LocalToCell"/> / <see cref="CellToLocal"/> are the one mapping
    /// between the map viewport and GAT cells (w3walk's minimap click uses them: UI/Minimap.W3Walk.cs).
    /// Client wave 13 (w13view), the brief's tenth item (design note, 2026-10-07: "remove the map button, and move it to be also on the menu
    /// that is hidden on top of the player: I click it and it toggles a transparent minimap somewhere on the player's vision, that can
    /// also be moved around with the reposition toggle"): the minimap is no longer a panel by the hand but a DISPLAY in front of the
    /// eyes, head-locked (the head's child), at <see cref="DisplayDefault"/> (a top-right corner: <see cref="DisplayRight"/> degrees
    /// right and <see cref="DisplayUp"/> up, <see cref="DisplayDistance"/> m away, <see cref="DisplayWidthDeg"/> degrees wide) or where
    /// the player put it (VR/Comfort.cs MinimapOffset / MinimapScale). See-through: its frame and backdrop at <see cref="BackAlpha"/>,
    /// the map picture at <see cref="MapAlpha"/>; the dots opaque and <see cref="DisplayDots"/> times larger, ours on a black ring. No
    /// collider the look can meet (UI/UiKit.cs PanelKinds: a HUD panel, it never switches the casting off); client wave 13 (w13look),
    /// item 22: the LASER clicks its map to walk there (colliders on the layer only the laser meets: UI/Minimap.W3Walk.cs); its
    /// backdrop is not named "bg", so the wand's ghost does not take it for a panel.
    /// The - / + / World buttons are not shown on it: they are on the overhead menu (LokiGame.W13View.cs). The hand placement stays in
    /// the code (<see cref="AtHand"/>), unused in play.
    /// </summary>
    public sealed partial class Minimap : MonoBehaviour
    {
        private const float S = 510, PW = 550, PH = 640, StripY = 272, MapY = -34; // map viewport side, panel size, the title / button strip and the viewport centre
        /// <summary>Client wave 13 (w13view): the display's default place and size, its see-through alphas, its dots' size factor.</summary>
        public const float DisplayDistance = 0.70f, DisplayRight = 22f, DisplayUp = 14f, DisplayWidthDeg = 16f, BackAlpha = 0.30f, FrameAlpha = 0.45f, MapAlpha = 0.55f, DisplayDots = 1.7f;
        /// <summary>Client wave 21 (w21tools): the frame is THE PANEL THEME's see-through one (the thin gold line with its corner scrolls: UI/Theme.cs
        /// SeeFrame) in place of wave 13's four gold bars (4 px at <see cref="FrameAlpha"/>): drawn at <see cref="FramePictureAlpha"/>, so it hides
        /// no more of the world than they did (the smoke w21tools measures both); lit in Reposition at <see cref="FrameLitAlpha"/>.</summary>
        public const float FramePictureAlpha = 0.35f, FrameLitAlpha = 0.95f;
        public static Vector3 DisplayDefault => Quaternion.Euler(-DisplayUp, DisplayRight, 0f) * Vector3.forward * DisplayDistance;
        /// <summary>Metres per panel pixel at the default size: <see cref="DisplayWidthDeg"/> across at <see cref="DisplayDistance"/>.</summary>
        public static float DisplayMetresPerPx => 2f * DisplayDistance * Mathf.Tan(DisplayWidthDeg * 0.5f * Mathf.Deg2Rad) / PW;
        /// <summary>The old panel by the walking hand (wave 3 to 12); false in play since client wave 13.</summary>
        public bool AtHand;
        private float _dotScale = 1f; private Image _selfRing, _backdrop; private Image[] _frame; private float _factor = 1f;
        /// <summary>The display's place in the head's frame and its size factor (1 = the default size).</summary>
        public Vector3 PlaceLocal => transform.localPosition;
        public float SizeFactor => _factor;
        private RectTransform _rt, _mapRect; private RawImage _img; private Text _title;
        private readonly List<Image> _dots = new List<Image>();
        private Transform _hand; private Camera _head;
        private GatFile _gat; private string _map = "";
        private System.Func<IEnumerable<Unit>> _units; private System.Func<IEnumerable<Game.GroundItem>> _items;
        private System.Func<Vector2, bool> _visible; private Transform _mapRoot;
        /// <summary>Party members on the current map (cell, dead) — magenta markers. Null when no party system.</summary>
        public System.Func<IEnumerable<(Vector2 cell, bool dead)>> PartyCells;
        /// <summary>Guild-mates on the current map (ZC_NOTIFY_POSITION_TO_GUILDM) — light blue markers, drawn under the party ones.</summary>
        public System.Func<IEnumerable<Vector2>> GuildCells;
        /// <summary>Client wave 2: marks placed by NPC scripts (viewpoint) and by the search store — drawn over everything.</summary>
        public System.Func<IEnumerable<(Vector2 cell, Color color)>> MarkCells;
        private Image _self, _facing, _marker;
        /// <summary>Navigation target on this map (null hides the marker).</summary>
        public Vector2? Marker;
        /// <summary>Client wave 3 (w3social): the "World" button (the game opens the world map window).</summary>
        public System.Action OpenWorldMap;
        private UiButton _zoomIn, _zoomOut, _world;

        public bool Visible => gameObject.activeSelf;

        public static Minimap Create(Transform parent, Transform leftHand, Camera head, System.Func<IEnumerable<Unit>> units, System.Func<IEnumerable<Game.GroundItem>> items, System.Func<Vector2, bool> visible)
        {
            // client wave 13 (w13view): a display, not a panel: no background collider / blocker (UiKit.Panel background false), a
            // see-through frame and backdrop of its own; the head's child
            var rt = UiKit.Panel("Minimap", head != null ? head.transform : parent, new Vector2(PW, PH), DisplayMetresPerPx, false);
            var m = rt.gameObject.AddComponent<Minimap>();
            m._backdrop = UiKit.Box(rt, "backdrop", Vector2.zero, new Vector2(PW, PH), new Color(UiKit.PanelBg.r, UiKit.PanelBg.g, UiKit.PanelBg.b, BackAlpha)).GetComponent<Image>();
            // client wave 21 (w21tools): the theme's see-through frame, ONE picture with an empty middle (it never tints the map); the piece is a
            // see-through surface of the theme, so its title gets the theme's lettering and outline (UI/UiKit.cs Label). Nothing here is named
            // "bg" (the wand's ghost takes a "bg" for a panel)
            float fu = Mathf.Min(0.016f / DisplayMetresPerPx / 46f, 0.35f * PW / 46f);
            var surf = rt.gameObject.AddComponent<UiSurface>(); surf.Kind = Surface.Clear; surf.Unit = fu;
            m._frame = new[] { UiKit.Picture(rt, "frame", new Vector2(PW, PH), Theme.SeeFrame, fu, 0f, false).GetComponent<Image>() };
            m._frame[0].color = new Color(1f, 1f, 1f, FramePictureAlpha);
            m._dotScale = DisplayDots;
            m._rt = rt; m._hand = leftHand; m._head = head; m._units = units; m._items = items; m._visible = visible;
            m._title = UiKit.Label(rt, "", new Vector2(-20, StripY), new Vector2(PW - 330, 44), 24, TextAnchor.MiddleCenter, HudLook.Heading);
            // w3social: zoom out / in on the left, the world map on the right (60 px buttons: ~2.5 cm at wrist scale, laser-sized)
            m._zoomOut = UiKit.Button(rt, "-", new Vector2(-PW / 2 + 48, StripY), new Vector2(64, 60), () => m.ZoomOut(), 34);
            m._zoomIn = UiKit.Button(rt, "+", new Vector2(-PW / 2 + 120, StripY), new Vector2(64, 60), () => m.ZoomIn(), 34);
            m._zoomOut.Tip = () => Lang.T("Zoom out"); m._zoomIn.Tip = () => Lang.T("Zoom in"); // client wave 14 (w14menu), point 7
            m._world = UiKit.Button(rt, Lang.T("World"), new Vector2(PW / 2 - 78, StripY), new Vector2(128, 60), () => m.OpenWorldMap?.Invoke(), 24);
            var mapGo = new GameObject("map", typeof(RectTransform));
            mapGo.transform.SetParent(rt, false);
            m._mapRect = (RectTransform)mapGo.transform;
            m._mapRect.anchorMin = m._mapRect.anchorMax = new Vector2(0.5f, 0.5f);
            m._mapRect.anchoredPosition = new Vector2(0, MapY); m._mapRect.sizeDelta = new Vector2(S, S);
            m._img = mapGo.AddComponent<RawImage>(); m._img.raycastTarget = false;
            if (UiKit.Overlay != null) m._img.material = UiKit.Overlay;
            m._img.color = new Color(1f, 1f, 1f, MapAlpha); // client wave 13 (w13view): see-through
            m._facing = m.Dot(new Color(1f, 1f, 1f, 0.9f), 6 * m._dotScale);
            m._marker = m.Dot(new Color(0.3f, 1f, 1f), 18 * m._dotScale); m._marker.transform.localRotation = Quaternion.Euler(0, 0, 45); m._marker.gameObject.SetActive(false);
            m._selfRing = m.Dot(Color.black, 24 * m._dotScale);
            m._self = m.Dot(Color.white, 16 * m._dotScale);
            // client wave 13 (w13view): zoom and the world map are on the overhead menu; the display has no button
            m._zoomOut.gameObject.SetActive(false); m._zoomIn.gameObject.SetActive(false); m._world.gameObject.SetActive(false);
            m._title.rectTransform.anchoredPosition = new Vector2(0, StripY); m._title.rectTransform.sizeDelta = new Vector2(PW - 40, 44);
            m.RefreshTitle();
            m.ApplyPlace();
            rt.gameObject.SetActive(false);
            return m;
        }

        /// <summary>Client wave 13 (w13view): the display's place in force: the player's own (VR/Comfort.cs) or the default, facing the eyes.</summary>
        public void ApplyPlace()
        {
            if (VR.Comfort.MinimapPlaced) PlaceAt(VR.Comfort.MinimapOffset, VR.Comfort.MinimapScale); else PlaceAt(DisplayDefault, 1f);
        }
        /// <summary>The display at a place of the head's frame (metres: right, up, ahead), at a size factor of the default.</summary>
        public void PlaceAt(Vector3 local, float factor)
        {
            if (AtHand) return;
            local = VR.Comfort.ClampHeadPlace(local); _factor = Mathf.Clamp(factor, VR.Comfort.PlaceMinScale, VR.Comfort.PlaceMaxScale);
            transform.localPosition = local; transform.localRotation = Quaternion.LookRotation(local.normalized, Vector3.up); transform.localScale = Vector3.one * (DisplayMetresPerPx * _factor);
        }
        /// <summary>Reposition: carried to a point of the world (the head's child: kept as a place in the head's frame), at a size factor.</summary>
        public void CarryTo(Vector3 world, float factor) { if (transform.parent != null) PlaceAt(transform.parent.InverseTransformPoint(world), factor); }
        /// <summary>Reposition: the frame lit while a hand can take it or holds it.</summary>
        public void SetPlacingLook(bool lit) { if (_frame != null) foreach (var f in _frame) f.color = new Color(1f, 1f, 1f, lit ? FrameLitAlpha : FramePictureAlpha); }
        /// <summary>The display's frame in the world: its middle, its half-width and half-height vectors (Reposition's laser test).</summary>
        public (Vector3 c, Vector3 r, Vector3 u) FrameWorld => (transform.position, transform.right * (PW * 0.5f * transform.lossyScale.x), transform.up * (PH * 0.5f * transform.lossyScale.y));
        public void Show(bool on) { if (gameObject.activeSelf != on) gameObject.SetActive(on); }
        /// <summary>Where our own mark is drawn (world), and the map picture's middle (world).</summary>
        public Vector3 SelfDotWorld => _self != null ? _self.transform.position : transform.position;
        public Vector3 MapWorld(Vector2 px) => _mapRect.TransformPoint(new Vector3(px.x, px.y, 0f));
        /// <summary>The zoom step (0: ×1, 1: ×2, 2: ×4), as the overhead menu's - / + leave it (kept per player).</summary>
        public int ZoomStep { get => _zoomIdx; set { _zoomIdx = Mathf.Clamp(value, 0, Zooms.Length - 1); RefreshTitle(); } }

        private Image Dot(Color c, float size)
        {
            var b = UiKit.Box(_mapRect, "dot", Vector2.zero, new Vector2(size, size), c);
            return b.GetComponent<Image>();
        }

        public void Toggle() { gameObject.SetActive(!gameObject.activeSelf); }

        public void SetMap(string map, GatFile gat, Texture2D image, Transform mapRoot)
        {
            _map = map; _gat = gat; _mapRoot = mapRoot;
            if (image == null && gat != null)
            {
                image = new Texture2D(gat.Width, gat.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "gatmap" };
                var px = new Color32[gat.Width * gat.Height];
                for (int y = 0; y < gat.Height; y++) for (int x = 0; x < gat.Width; x++)
                    px[x + y * gat.Width] = gat.Get(x, y).Walkable ? new Color32(150, 140, 120, 255) : new Color32(35, 35, 40, 255);
                image.SetPixels32(px); image.Apply(false);
            }
            else if (image != null)
            {
                // Item 78: RO minimap BMPs bake red portal marks into the map image, which look like monster dots.
                // Recolor baked red marks to bright cyan on a copy of the minimap texture.
                try
                {
                    var px = image.GetPixels32();
                    bool changed = false;
                    for (int i = 0; i < px.Length; i++)
                    {
                        var c = px[i];
                        if (c.a > 0 && c.r >= 150 && c.r > c.g * 2 && c.r > c.b * 2)
                        {
                            px[i] = new Color32(38, 242, 255, c.a);
                            changed = true;
                        }
                    }
                    if (changed)
                    {
                        var copy = new Texture2D(image.width, image.height, TextureFormat.RGBA32, false)
                        {
                            filterMode = image.filterMode,
                            wrapMode = image.wrapMode,
                            name = image.name + "_cyanportals"
                        };
                        copy.SetPixels32(px);
                        copy.Apply(false);
                        image = copy;
                    }
                }
                catch (System.Exception) { }
            }
            _img.texture = image;
            RefreshTitle();
        }

        /// <summary>Re-attach to another hand (left-handed mode); the map floats on the outer side of that hand.</summary>
        public void SetHand(Transform hand, bool rightSide) { _hand = hand; _side = rightSide ? -1f : 1f; }
        private float _side = 1f;

        // ---- zoom (client wave 3, w3social)
        /// <summary>The zoom levels: ×1 shows the whole map (as before), ×2 and ×4 a half / a quarter of it around the character.</summary>
        public static readonly int[] Zooms = { 1, 2, 4 };
        private int _zoomIdx;
        public int Zoom => Zooms[_zoomIdx];
        public void ZoomIn() { if (_zoomIdx < Zooms.Length - 1) { _zoomIdx++; RefreshTitle(); Debug.Log($"[Minimap] zoom ×{Zoom}"); } }
        public void ZoomOut() { if (_zoomIdx > 0) { _zoomIdx--; RefreshTitle(); Debug.Log($"[Minimap] zoom ×{Zoom}"); } }
        /// <summary>The part of the map shown, in map-normalised coordinates (x east, y north, 0…1): the texture's uvRect.</summary>
        public Rect View { get; private set; } = new Rect(0, 0, 1, 1);

        private void RefreshTitle()
        {
            if (_title != null) _title.text = (_map.Length > 0 ? _map : "") + (Zoom > 1 ? $"  <color=#9aa3b5>×{Zoom}</color>" : "");
            if (_zoomIn != null) { _zoomIn.Interactable = _zoomIdx < Zooms.Length - 1; _zoomIn.SetHover(false); }
            if (_zoomOut != null) { _zoomOut.Interactable = _zoomIdx > 0; _zoomOut.SetHover(false); }
        }

        /// <summary>The map viewport (fixed size, centred under the title strip): the laser-click area of the map.</summary>
        public RectTransform MapRect => _mapRect;

        /// <summary>Cell → a point in <see cref="MapRect"/>'s local pixel space (origin at its centre, y up), with the current zoom.</summary>
        public Vector2 CellToLocal(Vector2 cell)
        {
            if (_gat == null || _mapRect == null) return Vector2.zero;
            float w = _mapRect.sizeDelta.x, h = _mapRect.sizeDelta.y; var v = View;
            float nx = (cell.x + 0.5f) / _gat.Width, ny = (cell.y + 0.5f) / _gat.Height;
            return new Vector2((nx - v.x) / v.width * w - w / 2, (ny - v.y) / v.height * h - h / 2);
        }

        /// <summary>A point in <see cref="MapRect"/>'s local pixel space → the GAT cell under it (false outside the viewport or the map).</summary>
        public bool LocalToCell(Vector2 local, out Vector2Int cell)
        {
            cell = default;
            if (_gat == null || _mapRect == null) return false;
            float w = _mapRect.sizeDelta.x, h = _mapRect.sizeDelta.y;
            if (Mathf.Abs(local.x) > w / 2 || Mathf.Abs(local.y) > h / 2) return false;
            var v = View;
            float nx = v.x + (local.x + w / 2) / w * v.width, ny = v.y + (local.y + h / 2) / h * v.height;
            cell = new Vector2Int(Mathf.FloorToInt(nx * _gat.Width), Mathf.FloorToInt(ny * _gat.Height));
            return cell.x >= 0 && cell.y >= 0 && cell.x < _gat.Width && cell.y < _gat.Height;
        }

        /// <summary>Recentres the view on `centre` (a cell) for the current zoom, clamped so the view stays inside the map.</summary>
        private void UpdateView(Vector2? centre)
        {
            float span = 1f / Zoom;
            if (Zoom == 1 || _gat == null || !centre.HasValue) { View = new Rect(0, 0, 1, 1); }
            else
            {
                float cx = (centre.Value.x + 0.5f) / _gat.Width, cy = (centre.Value.y + 0.5f) / _gat.Height;
                View = new Rect(Mathf.Clamp(cx - span / 2, 0f, 1f - span), Mathf.Clamp(cy - span / 2, 0f, 1f - span), span, span);
            }
            _img.uvRect = View;
        }

        private void LateUpdate()
        {
            if (AtHand && _hand != null && _head != null) // (client wave 13: retired from play; the display is the head's child)
            {
                var toHead = _head.transform.position - _hand.position; toHead.y = 0;
                var right = toHead.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, toHead.normalized) : Vector3.right;
                transform.position = _hand.position + Vector3.up * 0.2f - right * ((0.3f + WristMenu.ClassicGrewHalfWidth) * _side); // client wave 11: beside the larger wrist menu, the same gap
                UiKit.FaceHead(transform, _head);
            }
            if (_gat == null) return;
            Unit self = null;
            foreach (var u in _units()) if (u != null && u.IsSelf) { self = u; break; }
            UpdateView(self != null ? self.Cell : (Vector2?)null);
            float w = _mapRect.sizeDelta.x, h = _mapRect.sizeDelta.y;
            bool Inside(Vector2 p) => Mathf.Abs(p.x) <= w / 2 && Mathf.Abs(p.y) <= h / 2;
            int used = 0;
            foreach (var u in _units())
            {
                if (u == null || u.IsSelf) continue;
                if (_visible != null && !_visible(u.Cell)) continue;
                if (u.Sprite != null && u.Sprite.Tint.a <= 0.01f) continue; // hidden / cloaked units are not drawn (EntityManager.ApplyOption, w3status)
                if (u.HiddenDoor) continue; // client wave 14 (w14play), point 22: class 139 is never drawn (Sprites/Unit.cs SetupInteraction)
                Color c = u.Kind == Unit.UnitKind.Monster ? new Color(1f, 0.3f, 0.25f) : u.Kind == Unit.UnitKind.Npc ? new Color(0.35f, 1f, 0.45f) : u.Kind == Unit.UnitKind.Warp ? new Color(0.15f, 0.95f, 1f) : u.Kind == Unit.UnitKind.Player ? new Color(0.8f, 0.8f, 1f) : Color.clear;
                if (c.a == 0) continue;
                var p = CellToLocal(u.Cell); if (!Inside(p)) continue;
                Place(ref used, p, c, u.Kind == Unit.UnitKind.Warp ? 20 : 10, u.Kind == Unit.UnitKind.Warp);
            }
            foreach (var g in _items()) { if (g != null && (_visible == null || _visible(g.Cell))) { var p = CellToLocal(g.Cell); if (Inside(p)) Place(ref used, p, new Color(1f, 0.9f, 0.3f), 8); } }
            // party members on this map (ZC_NOTIFY_POSITION_TO_GROUPM): drawn over everything, whatever room they are in — that is what the marker is for
            if (GuildCells != null) foreach (var gc in GuildCells()) { var p = CellToLocal(gc); if (Inside(p)) Place(ref used, p, new Color(0.4f, 0.8f, 1f), 12); }
            if (PartyCells != null) foreach (var pc in PartyCells()) { var p = CellToLocal(pc.cell); if (Inside(p)) Place(ref used, p, pc.dead ? new Color(0.6f, 0.4f, 0.7f) : new Color(1f, 0.45f, 0.95f), 14); }
            // client wave 2: NPC viewpoint marks (ZC_COMPASS) and a search-store shop, big and in the script's colour
            if (MarkCells != null) foreach (var mk in MarkCells()) { var p = CellToLocal(mk.cell); if (Inside(p)) { Place(ref used, p, Color.black, 22); Place(ref used, p, mk.color, 16); } }
            for (int i = used; i < _dots.Count; i++) _dots[i].gameObject.SetActive(false);
            bool markerIn = Marker.HasValue && Inside(CellToLocal(Marker.Value));
            _marker.gameObject.SetActive(markerIn);
            if (markerIn) { ((RectTransform)_marker.transform).anchoredPosition = CellToLocal(Marker.Value); _marker.transform.SetAsLastSibling(); }
            _self.gameObject.SetActive(self != null); _facing.gameObject.SetActive(self != null); _selfRing.gameObject.SetActive(self != null);
            if (self != null)
            {
                var p = CellToLocal(self.Cell);
                ((RectTransform)_self.transform).anchoredPosition = p; ((RectTransform)_selfRing.transform).anchoredPosition = p;
                // facing tick: the head's forward projected into map space
                var f = _mapRoot != null && _head != null ? _mapRoot.InverseTransformDirection(_head.transform.forward) : Vector3.forward;
                var dir = new Vector2(f.x, f.z); if (dir.sqrMagnitude > 1e-6f) dir.Normalize();
                ((RectTransform)_facing.transform).anchoredPosition = p + dir * (16f * _dotScale);
                _selfRing.transform.SetAsLastSibling(); _self.transform.SetAsLastSibling(); _facing.transform.SetAsLastSibling();
            }
            UpdateLaserCursor(); // client wave 13 (w13look), item 22: the cell under the laser (UI/Minimap.W3Walk.cs)
        }

        private void Place(ref int used, Vector2 p, Color c, float size, bool outline = false)
        {
            size *= _dotScale; // client wave 13 (w13view): the display is farther than the hand was: its dots are drawn larger
            if (used >= _dots.Count) _dots.Add(Dot(c, size));
            var d = _dots[used++];
            d.gameObject.SetActive(true); d.color = c;
            var ol = d.GetComponent<Outline>();
            if (outline)
            {
                if (ol == null) ol = d.gameObject.AddComponent<Outline>();
                ol.enabled = true;
                ol.effectColor = new Color(0.02f, 0.14f, 0.24f, 0.95f);
                float o = Mathf.Max(2f, 2.5f * _dotScale);
                ol.effectDistance = new Vector2(o, -o);
            }
            else if (ol != null && ol.enabled) ol.enabled = false;
            var rt = (RectTransform)d.transform; rt.anchoredPosition = p; rt.sizeDelta = new Vector2(size, size);
        }

        /// <summary>Smoke: the dots drawn this frame (inside the viewport) and whether the character's dot is inside it.</summary>
        public int DotsShown { get { int n = 0; foreach (var d in _dots) if (d.gameObject.activeSelf) n++; return n; } }
    }
}
