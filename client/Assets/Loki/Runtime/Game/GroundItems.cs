using System.Collections.Generic;
using Loki.Net;
using Loki.Sprites;
using Loki.UI;
using Loki.World;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Items lying on the ground:
    ///   0x0add packet_dropflooritem  ITAID.L ITID.L type.W IsIdentified.B x.W y.W subX.B subY.B count.W showdropeffect.B dropeffectmode.W
    ///   0x009d ZC_ITEM_ENTRY         AID.L itemId.L identify.B x.W y.W amount.W subX.B subY.B   (already on the ground when you arrive)
    ///   0x00a1 ZC_ITEM_DISAPPEAR     itemAid.L
    /// subX/subY are 3, 6, 9 or 12 (rAthena map_addflooritem: rnd_value(1, 4) * 3, map.cpp:2065-2066), twelfths of a cell: roBrowserLegacy
    /// puts the item at xPos - 0.5 + subX / 12 (Engine/MapEngine/Item.js:38-58, its positions are cell centres; Loki's are cell corners, so
    /// x + subX / 12 here) on the ground's height at that very point (Renderer/Map/Altitude.js:125-152: the cell's four corners blended).
    /// A fresh drop (0x0add) falls as roBrowserLegacy's does (Renderer/ItemObject.js:60-65, Item.js:56): from 5 cells above the ground,
    /// straight down at a cell per 40 ms, so it lands after 200 ms, no bounce (specified after the 2026-10-01 playtest: "drop on the
    /// ground just like the game"; Loki's own pop-and-bounce before it). An item already lying there (0x009d) does not fall.
    /// Sprites: data\sprite\아이템\&lt;identifiedResourceName&gt; from the client item table.
    /// </summary>
    public sealed class GroundItems
    {
        public readonly Dictionary<uint, GroundItem> Items = new Dictionary<uint, GroundItem>();
        /// <summary>How many drop animations have started (for the smoke test).</summary>
        public int FallsStarted { get; private set; }
        private readonly RoAssets _assets; private readonly MapLoader _map;
        public const string ItemSpriteDir = "data\\sprite\\¾ÆÀÌÅÛ\\"; // 아이템 (CP949 bytes as Latin-1)

        public GroundItems(RoAssets assets, MapLoader map) { _assets = assets; _map = map; }

        public void Clear() { foreach (var g in Items.Values) if (g != null) Object.Destroy(g.gameObject); Items.Clear(); }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x0add:
                {
                    r.Skip(2); uint aid = r.U32(); int itemId = (int)r.U32(); r.Skip(2); bool ident = r.U8() != 0;
                    int x = r.S16(), y = r.S16(); int sx = r.U8(), sy = r.U8(); int count = r.S16(); int effect = r.S8(); int mode = r.S16();
                    var g = Spawn(aid, itemId, ident, x, y, sx, sy, count, effect > 0 ? mode : -1);
                    if (g != null)
                    {
                        g.WaitStepOff = Time.time - RoSession.LastSelfDropAt < 1.5f;
                        g.StartFall();
                        FallsStarted++;
                    } // freshly dropped (0x0add), not an item already lying there (0x009d)
                    return true;
                }
                case 0x009d:
                {
                    r.Skip(2); uint aid = r.U32(); int itemId = (int)r.U32(); bool ident = r.U8() != 0; int x = r.U16(), y = r.U16(); int count = r.U16(); int sx = r.U8(), sy = r.U8();
                    var g = Spawn(aid, itemId, ident, x, y, sx, sy, count, -1);
                    if (g != null) g.WaitStepOff = true;
                    return true;
                }
                case 0x00a1:
                {
                    r.Skip(2); uint aid = r.U32();
                    if (Items.TryGetValue(aid, out var g)) { if (g != null) Object.Destroy(g.gameObject); Items.Remove(aid); }
                    return true;
                }
                default: return false;
            }
        }

        private GroundItem Spawn(uint aid, int itemId, bool identified, int x, int y, int subX, int subY, int count, int dropEffect)
        {
            if (_map.Current == null) return null;
            if (Items.TryGetValue(aid, out var old) && old != null) Object.Destroy(old.gameObject);
            var info = GameDb.GetItem(itemId);
            var go = new GameObject($"item:{aid}:{itemId}");
            go.transform.SetParent(_map.transform, false);
            var g = go.AddComponent<GroundItem>();
            g.Id = aid; g.ItemId = itemId; g.Amount = count;
            g.Cell = new Vector2Int(x, y);
            float px = x + subX / 12f, py = y + subY / 12f;
            go.transform.localPosition = new Vector3(px, GroundHeight(_map.Current.Gat, px, py), py);
            var spr = go.AddComponent<ActorSprite>();
            spr.IsPlayerType = false;
            if (!string.IsNullOrEmpty(info.Resource) && _assets.Sprite(ItemSpriteDir + info.Resource, out var s, out var a)) spr.SetBody(s, a);
            g.Sprite = spr;
            g.Overhead = Overhead.Attach(go.transform, count > 1 ? $"{info.Name} ×{count}" : info.Name, dropEffect >= 0 ? new Color(1f, 0.85f, 0.4f) : new Color(0.95f, 0.95f, 0.8f), 1.1f);
            g.Overhead.MetresPerPx = 0.00028f;
            var col = go.AddComponent<BoxCollider>(); col.size = new Vector3(1.15f, 1.15f, 1.15f); col.center = new Vector3(0, 0.5f, 0);
            g.Collider = col;
            go.AddComponent<Targetable>().Item = g;
            Items[aid] = g;
            return g;
        }

        /// <summary>The ground's height (Loki units, y up) at a point of the map (Loki units: cell x spans x .. x + 1), the four corners of
        /// the cell under it blended as roBrowserLegacy's Altitude.getCellHeight does (Renderer/Map/Altitude.js:125-152; its heights are the
        /// GAT's × 0.2 = RoSpace.Scale, Loaders/Altitude.js:87-90, corners in the file's order SW, SE, NW, NE = GatFile.Cell H0..H3).</summary>
        public static float GroundHeight(Formats.GatFile gat, float px, float py)
        {
            int cx = Mathf.FloorToInt(px), cy = Mathf.FloorToInt(py);
            if (gat == null || !gat.InBounds(cx, cy)) return 0f;
            var c = gat.Get(cx, cy);
            float fx = px - cx, fy = py - cy;
            float x1 = c.H0 + (c.H1 - c.H0) * fx, x2 = c.H2 + (c.H3 - c.H2) * fx;
            return -(x1 + (x2 - x1) * fy) * RoSpace.Scale;
        }
    }

    public sealed class GroundItem : MonoBehaviour
    {
        public uint Id; public int ItemId, Amount; public Vector2Int Cell;
        public ActorSprite Sprite; public Overhead Overhead; public BoxCollider Collider;
        public bool WaitStepOff; public float NextAutoPickupAt;
        public void SetHover(bool h) { if (Sprite != null) Sprite.Tint = h ? new Color(1f, 1f, 0.6f) : Color.white; if (Overhead != null) Overhead.Hovered = h; }

        // Drop animation, roBrowserLegacy's (Renderer/ItemObject.js:60-65 with Item.js:56): from 5 cells above the ground straight down,
        // z = max(ground, start - ms / 40), so it lands after 200 ms.
        public const float FallFrom = 5f, MsPerCell = 40f;
        private float _fallStart = -1f; private float _restY;
        /// <summary>True while the item is still falling; its height above the ground now (cells).</summary>
        public bool Falling => _fallStart >= 0f;
        public float HeightAboveGround => transform.localPosition.y - _restY;
        public float RestY => _restY;
        public void StartFall() { _restY = transform.localPosition.y; _fallStart = Time.time; Update(); }
        private void Update()
        {
            if (_fallStart < 0) return;
            float y = Mathf.Max(0f, FallFrom - (Time.time - _fallStart) * 1000f / MsPerCell);
            var p = transform.localPosition;
            p.y = _restY + y;
            transform.localPosition = p;
            if (Collider != null) Collider.center = new Vector3(0f, 0.5f - y, 0f);
            if (y <= 0f) { _fallStart = -1f; if (Collider != null) Collider.center = new Vector3(0f, 0.5f, 0f); }
        }
    }
}
