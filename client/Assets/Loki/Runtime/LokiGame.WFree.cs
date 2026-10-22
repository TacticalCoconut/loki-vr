using Loki.Net;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Free movement (design note 2026-10-14): "client-authoritative sub-tile stop" (the player stops anywhere and is drawn there; rAthena
    /// keeps the character on the nearest cell: Sprites/Unit.Smooth.cs, Game/StickSteer.cs settle, the laser's exact point in
    /// LokiGame.W3Walk.cs) "but with friends seeing other player's exact spots too": once the headsets' channel is up (LokiGame.W3Presence.cs)
    /// and it is Loki Host's relay, my spot goes out and the others' are drawn (Game/FreeSpots.cs over Net/ISpotChannel.cs; frames
    /// Net/SpotWire.cs). Loki Online's module has no such table yet: there everyone is drawn on their cells.
    /// Hook in LokiGame.W3Presence.cs: OnMapChangeW3Presence (every map entry; it starts once).
    /// </summary>
    public sealed partial class LokiGame
    {
        public Game.FreeSpots FreeSpots { get; private set; }

        private void StartFreeSpots()
        {
            if (FreeSpots != null || Headsets == null || !(_presence is ISpotChannel ch)) return;
            FreeSpots = Game.FreeSpots.Create(gameObject, ch, () => Entities?.Self, id => Entities?.Get(id), () => Headsets?.CurrentMap,
                () => Session != null && Session.State == RoSession.Stage.InGame && Session.Leaving == RoSession.Leave.None);
            Debug.Log("[Free] exact spots: mine sent and the others' drawn when the relay forwards them (free movement, Smooth)");
        }
    }
}
