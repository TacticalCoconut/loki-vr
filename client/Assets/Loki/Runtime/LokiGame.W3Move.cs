using System.Collections.Generic;
using Loki.Sprites;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 3, w3move: the glue between the game and the unit movement in Sprites/ (Unit.WalkTo on the server's path,
    /// slides, ServerClock). Called once from LokiGame's setup, next to the comfort vignette.
    /// </summary>
    public sealed partial class LokiGame
    {
        private void InitW3Move()
        {
            // a pet's loot walk is the one pet walk rAthena makes with the easy path (EntityManager.IsPetLootWalk)
            Entities.FloorItemCells = W3MoveFloorItemCells;
            // our own character knocked back, jumping or pulled in the Follow / first-person views: the world moves under the
            // player, so the vignette closes for a moment (as for a snap turn), and the Comfort option can make it a snap
            Entities.SnapSelfSlides = () => VR.Comfort.SnapKnockback && View != null && View.Mode != VR.ViewController.ViewMode.Table;
            Entities.SelfSlide += packet =>
            {
                if (Vignette != null && View != null && View.Mode != VR.ViewController.ViewMode.Table) Vignette.Flash();
            };
            // the server's clock sampled every 2 s between the session's own 10 s requests (ServerClock)
            var pinger = gameObject.AddComponent<ServerClockPinger>();
            pinger.Ready = () => Session != null && Session.State == Net.RoSession.Stage.InGame;
            pinger.Send = () => Session.SendPacket(new Net.PacketWriter(0x0360).U32((uint)System.Environment.TickCount).ToArray()); // CZ_REQUEST_TIME2
        }

        private IEnumerable<Vector2Int> W3MoveFloorItemCells()
        {
            if (Ground == null) yield break;
            foreach (var g in Ground.Items.Values) if (g != null) yield return g.Cell;
        }
    }
}
