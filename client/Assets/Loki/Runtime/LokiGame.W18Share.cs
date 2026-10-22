using Loki.Net;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 18 (w18share), the design's point 33 ("when I open the flick menu, my friend needs to also be able to see what I'm seeing") and
    /// its addition (the privacy choice: Settings → Headsets "Casting menus shown", "Seen by": Everyone, or any of Friends, Family, Guild):
    /// the game's side. Once the headsets' channel is up (LokiGame.W3Presence.cs) and it is Loki Host's relay, my casting menus go out
    /// (UI/GestureShare.cs) and my friends' are drawn at their headsets (UI/FriendGestures.cs), both through
    /// <see cref="IGestureChannel"/> (Net/RelayGestureChannel over Net/PresenceRelayChannel.cs; frames: Net/GestureWire.cs). Loki Online's
    /// module has no such table yet: there, nothing is shared (the SpacetimeDB transport later, the brief's "presence later").
    /// Hook in LokiGame.W3Presence.cs: OnMapChangeW3Presence (every map entry; it starts once).
    /// </summary>
    public sealed partial class LokiGame
    {
        public UI.GestureShare W18Share { get; private set; }
        public UI.FriendGestures W18Friends { get; private set; }

        private void StartW18Share()
        {
            if (W18Share != null || Headsets == null || _boot.Rig == null || _boot.Map == null || !(_presence is IGestureChannel ch)) return;
            W18Share = UI.GestureShare.Create(this, ch, _boot.Rig, _boot.Map, Headsets);
            W18Friends = UI.FriendGestures.Create(ch, Headsets, _boot.Map, W12Wand);
            Debug.Log($"[Menus] casting menus: mine shown {(VR.Comfort.MenusShared ? "to " + UI.GestureShare.Who(VR.Comfort.MenusAudience) : "to nobody")}; friends' drawn at their headsets (when the relay forwards them)");
        }
    }
}
