using Loki.Game;
using Loki.Sprites;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 3b, w3walk: far walk targets. Every walk the player asks for with the pointer (a cell, a far NPC / portal /
    /// floor item / shop / player, a monster to tame, the minimap) goes through <see cref="RouteWalker"/>, which walks there in
    /// legs rAthena accepts (Game/RouteWalker.cs, Sprites/RoRoute.cs); the stick, an attack, a skill cancel it. What happens on
    /// arrival (talk, enter, pick up, tame) stays where it was: LokiGame's pending requests (UpdatePending).
    /// </summary>
    public sealed partial class LokiGame
    {
        public RouteWalker W3WalkRoutes { get; private set; }
        private bool W3WalkActive => W3WalkRoutes != null && W3WalkRoutes.Active;

        private void InitW3Walk()
        {
            W3WalkRoutes = gameObject.AddComponent<RouteWalker>();
            W3WalkRoutes.Self = () => Entities.Self;
            W3WalkRoutes.Gat = () => _boot.Map.Current?.Gat;
            W3WalkRoutes.MapRoot = () => _boot.Map.transform;
            W3WalkRoutes.SendWalk = (x, y) => Session.WalkTo(x, y);
            W3WalkRoutes.Blocked = W3WalkBlocked;
            W3WalkRoutes.FirstPerson = () => View != null && View.Mode == VR.ViewController.ViewMode.FirstPerson;
            W3WalkRoutes.Warps = W3WalkWarpCells;
            W3WalkRoutes.Tell = m => { Hud?.Notify(m); Chat(m, "ffb070"); }; // client wave 11: in front of the player
            // a trip that could not reach its end drops what was to happen there (talk, pick up, tame)
            W3WalkRoutes.Ended += (tag, outcome) => { if (outcome.StartsWith("failed")) { _pendingPickup = _pendingNpc = _pendingCapture = 0; } };
            Entities.SelfSitting += sit => { if (sit) W3WalkCancel("sat down"); };
            Entities.Damage += (src, dst, amount, crit) => { if (dst != null && dst == Entities.Self) W3WalkRoutes.NoteHit(); };
            // client wave 13 (w13view): the minimap is a see-through display in front of the eyes now, with no collider (it must never
            // block the look or a click): its map is no longer clicked with the laser (W3WalkEnableClicks is not called). The walk a
            // click made (W3WalkFromMinimap) and the cell mapping stay; the smoke w3walk still drives them directly.
            // Client wave 13 (w13look), item 22: the laser alone clicks it again (colliders only the laser meets: LokiGame.W13Look.cs).
            if (MiniMap != null) MiniMap.W3WalkCellClicked = W3WalkFromMinimap;
        }

        /// <summary>Why our character cannot walk now (the trip ends), or null. Each is a state in which rAthena ignores a walk
        /// request (clif_parse_WalkToXY, clif.cpp:11379-11395): dead; an NPC dialog or a chat room (pc_cant_act: npc_id, chatID,
        /// pc.hpp:1166-1168); trading, the storage or an NPC shop open (pc_cant_act2: trading, storage_flag, npc_shopid,
        /// pc.hpp:1156-1164); our own shop or buying store open (vending / buyingstore, the same line and unit_can_move,
        /// unit.cpp:1819-1820). Others (a status such as stone or freeze, the refine / stylist windows …) end the trip through its
        /// refusals.</summary>
        private string W3WalkBlocked()
        {
            if (Session == null || Session.State != Net.RoSession.Stage.InGame) return "not in the game";
            if (Player.Dead) return "dead";
            if (Dialog != null && Dialog.IsOpen) return "a dialog opened";
            if (Wave2BlocksWalking) return "in a chat room";
            if (Trade != null && Trade.State == Trade.Stage.Open) return "trading";
            if (Storage != null && Storage.IsOpen) return "the storage is open";
            if (Shops != null && Shops.IsOpen) return "a shop is open";
            if (Market != null && (Market.Vending || Market.Buying)) return "your shop is open";
            return null;
        }

        /// <summary>Free movement (wfree): the exact ground point of the laser / trigger pull being handled (LokiGame.cs OnFreeTrigger), in cell
        /// coordinates; null otherwise.</summary>
        private Vector2? _freeExact;

        /// <summary>Walks to a cell (near: one request, as before; far: in legs). The one place the pointer's walks go through.</summary>
        private void W3WalkStart(Vector2Int cell, string tag)
        {
            // free movement (wfree): a walk to the ground under the laser ends on its exact point (Sprites/Unit.Smooth.cs); any other walk on
            // a cell's centre. A point in the very cell the server's unit stands on: the body goes there, nothing is asked.
            var me = Entities.Self;
            if (me != null)
            {
                W3WalkRoutes?.SrvTrack(me);
                bool exact = tag == "cell" && _freeExact.HasValue && Mathf.FloorToInt(_freeExact.Value.x + 0.5f) == cell.x && Mathf.FloorToInt(_freeExact.Value.y + 0.5f) == cell.y;
                me.FreeGoalCell = exact ? cell : (Vector2Int?)null;
                if (exact) me.FreeGoalPoint = _freeExact.Value;
                if (exact && Unit.Smooth && W3WalkRoutes != null && !me.Walking && !me.Held.HasValue && W3WalkRoutes.SrvCell == cell && Vector2Int.RoundToInt(me.Cell) == cell)
                {
                    if (Target != null && Target.Steer.Settling) Target.Steer.CancelSettle(me, "a point in the same cell");
                    me.GlideTo(_freeExact.Value);
                    Debug.Log($"[W3Walk] a point in the cell the server stands on ({cell.x},{cell.y}): the body goes to ({_freeExact.Value.x:F2},{_freeExact.Value.y:F2}), nothing asked");
                    return;
                }
            }
            if (W3WalkRoutes == null || Entities.Self == null) { Session.WalkTo(cell.x, cell.y); return; }
            if (!W3WalkRoutes.BeginTrip(cell.x, cell.y, tag)) return;
            // a far walk takes longer than the pending requests' timeouts (10-15 s): keep them alive for the walk
            var t = W3WalkRoutes.Trip;
            if (t != null && t.RouteLen > 1) _pendingUntil = Mathf.Max(_pendingUntil, Time.time + 10f + (t.RouteLen - 1) * 1.4f * Mathf.Max(Entities.Self.Speed, 1) / 1000f);
        }

        /// <summary>A new action takes over: the trip stops asking for legs (the action sends its own request).</summary>
        private void W3WalkCancel(string why) => W3WalkRoutes?.Cancel(why);

        /// <summary>Every map packet, before the game handles it (one line at the top of OnMapPacket).</summary>
        private void W3WalkPeek(ushort id, byte[] b, int o, int n) => W3WalkRoutes?.Peek(id, b, o, n, Entities != null ? Entities.SelfId : 0);

        /// <summary>The portals in view (rAthena's warp classes, Unit.WarpClass / HiddenWarpClass: Unit.Kind Warp).</summary>
        private System.Collections.Generic.IEnumerable<Vector2Int> W3WalkWarpCells()
        {
            foreach (var u in Entities.Units.Values) if (u != null && u.Kind == Unit.UnitKind.Warp) yield return Vector2Int.RoundToInt(u.Cell);
        }

        /// <summary>A click on the minimap (UI/Minimap.W3Walk.cs): walk there like a click on the ground.</summary>
        private void W3WalkFromMinimap(Vector2Int cell)
        {
            Debug.Log($"[W3Walk] minimap click on cell {cell.x},{cell.y}");
            OnCellClicked(cell, "minimap");
        }
    }
}
