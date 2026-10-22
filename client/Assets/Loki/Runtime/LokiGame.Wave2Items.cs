using Loki.Game;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki
{
    /// <summary>Client wave 2, items: favourites / filters / sorting (Inventory.cs, WristMenu.cs), rental / bound / damaged notices
    /// (Inventory.cs), inventory expansion (InventoryExpansion.cs), viewpoint marks (ViewPoints.cs, wired in LokiGame.Wave2.cs).</summary>
    public sealed partial class LokiGame
    {
        public InventoryExpansion Expansion { get; private set; }

        private void BeginWave2Items(LokiBootstrap boot)
        {
            var w = ListWindow.Create(boot.Rig.transform, "ExpandWindow"); w.RequestKeyboard = f => { Keyboard.Attach(f); Keyboard.Show(); };
            Expansion = new InventoryExpansion(Session, Inventory, w, () => boot.Rig.Head, (t, c) => ItemChat(t, c));
        }

        private bool HandleWave2Items(ushort id, byte[] b, int o, int n)
        {
            if (Expansion != null && Expansion.Handle(id, b, o, n)) return true;
            return false;
        }
    }
}
