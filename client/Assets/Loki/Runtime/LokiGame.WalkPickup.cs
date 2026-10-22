using System.Collections.Generic;
using Loki.Game;
using Loki.Net;
using Loki.Sprites;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Items picked up by walking over them (specified in VR playtesting: they "don't need to be clicked to be
    /// picked up. Rather, they are picked up when you walk over them"). When our character steps onto a cell — its drawn position, which
    /// follows the server's own walk step by step (Sprites/Unit.cs), passes into a new cell — every item lying on that cell is asked for
    /// once (CZ_ITEM_PICKUP 0x0362, RoSession.TakeItem). Standing still never picks anything up, so an item dropped on purpose stays
    /// where it fell until you step off and back on; a move of more than two cells at once (a warp, a teleport, being placed on a map) is
    /// not a step. A click on an item still walks there (OnTargetClicked), and the step onto it picks it up.
    /// rAthena's side, read end to end: clif_parse_TakeItem (clif.cpp:12006-12035) → pc_takeitem (pc.cpp:6189-6262): the item within 2
    /// cells (a step onto it is 0), its loot priority (the top attackers' get ticks, party sharing), then it is ours and the pickup motion
    /// is shown to everyone; the walk goes on (only an attack is stopped). It refuses while dead or pc_cant_act (pc.hpp:1156-1169: an NPC
    /// dialog, a chat room, vending, a buying store, a trade, storage, a shop and the other windows): the states the client knows are
    /// skipped here; any other refusal comes back as ZC_ITEM_PICKUP_ACK result 6 with its usual line (Game/Inventory.cs).
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>The server's view area drawn around us (World/VisionArea.cs; set up next to InitW5Move).</summary>
        public World.VisionArea Vision { get; private set; }

        private static readonly Vector2Int NoCell = new Vector2Int(int.MinValue, int.MinValue);
        private Vector2Int _walkPickupCell = NoCell; private Vector2 _walkPickupDrawn; private Unit _walkPickupSelf; private string _walkPickupMap;
        private readonly List<GroundItem> _walkPickupHere = new List<GroundItem>();
        /// <summary>Floor items asked for by steps so far, and the last one (0: none).</summary>
        public int WalkPickupsSent { get; private set; }
        public uint LastWalkPickup { get; private set; }

        private void UpdateWalkPickup()
        {
            var self = Entities?.Self;
            string map = _boot.Map.Current?.Name;
            if (self == null || Ground == null || Session == null || Session.State != RoSession.Stage.InGame) { _walkPickupCell = NoCell; return; }
            var cell = Vector2Int.RoundToInt(self.Cell);
            var drawn = self.DrawnCell;
            if (self != _walkPickupSelf || map != _walkPickupMap || _walkPickupCell == NoCell)
            {
                _walkPickupSelf = self; _walkPickupMap = map; _walkPickupCell = cell; _walkPickupDrawn = drawn;
                return;
            }
            var from = _walkPickupCell;
            int dx = cell.x - from.x, dy = cell.y - from.y, d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
            bool stepped = cell != from && d <= 2;
            float drawnDelta = Vector2.Distance(drawn, _walkPickupDrawn);
            bool drawnMoved = drawnDelta >= 0.18f && drawnDelta <= 2.5f;
            if (cell != from) _walkPickupCell = cell;
            if (drawnDelta >= 0.18f) _walkPickupDrawn = drawn;
            if (d > 2) return; // relocated, not a step
            foreach (var g in Ground.Items.Values)
            {
                if (g != null && g.WaitStepOff && g.Cell != cell && Vector2.Distance(drawn, g.Cell) > 1.35f)
                    g.WaitStepOff = false;
            }
            if (!stepped && !drawnMoved) return;
            if (Player.Dead || Dialog.IsOpen || (ChatRooms != null && ChatRooms.InRoom) || (Trade != null && Trade.Active)
                || (Market != null && (Market.Vending || Market.Buying)) || (Storage != null && Storage.IsOpen)) return;
            // a two-cell move in one frame (a slow frame) also stepped on the cell between
            var mid = stepped && d == 2 ? new Vector2Int(from.x + System.Math.Sign(dx), from.y + System.Math.Sign(dy)) : cell;
            float now = Time.time;
            _walkPickupHere.Clear();
            foreach (var g in Ground.Items.Values)
            {
                if (g == null || now < g.NextAutoPickupAt) continue;
                bool onExactStep = stepped && (g.Cell == cell || g.Cell == mid);
                bool inProximity = !g.WaitStepOff && (Mathf.Max(Mathf.Abs(cell.x - g.Cell.x), Mathf.Abs(cell.y - g.Cell.y)) <= 1 || Vector2.Distance(drawn, g.Cell) <= 1.65f);
                if (onExactStep || inProximity) _walkPickupHere.Add(g);
            }
            foreach (var g in _walkPickupHere)
            {
                g.NextAutoPickupAt = now + 0.35f;
                g.WaitStepOff = false;
                Session.TakeItem(g.Id); WalkPickupsSent++; LastWalkPickup = g.Id;
                if (_pendingPickup == g.Id) _pendingPickup = 0; // the click that walked us here is answered
                Debug.Log($"[Pickup] stepped near ({g.Cell.x},{g.Cell.y}): asking for floor item {g.Id} (item {g.ItemId} x{g.Amount})");
            }
        }
    }
}
