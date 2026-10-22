using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 8 (w8game): the wrist menu's entry to the Horde side mode, a one-button strip under w4world's strip (outside
    /// the pages: the five rows of tabs are full). LokiGame.W8Horde sets the action (the mode's panel). Hook in WristMenu.cs:
    /// Build (after BuildW4World).
    /// </summary>
    public sealed partial class WristMenu
    {
        public System.Action OpenHorde;
        private UiButton _w8Horde;

        private void BuildW8Horde()
        {
            float y = -H / 2 - 40 - 80 - UiKit.OutsetOf(_rt); // (client wave 21: under the menu's wood frame)
            var strip = UiKit.Box(_rt, "w8horde-strip", new Vector2(0, y), new Vector2(W, 72), new Color(1, 1, 1, 0));
            strip.GetComponent<UnityEngine.UI.Image>().enabled = false; UiKit.Dress(strip, new Vector2(W, 72), ClassicMetresPerPx, PanelLook.Card); // client wave 21 (w21kit): a parchment card hanging under the menu's frame
            var block = strip.gameObject.AddComponent<BoxCollider>(); block.size = new Vector3(W, 72, 4f); block.center = new Vector3(0, 0, 3f);
            strip.gameObject.AddComponent<UiBlocker>();
            _w8Horde = UiKit.Button(_rt, Lang.T("Horde (co-op side mode)"), new Vector2(0, y), new Vector2(W - 20, 58), () => OpenHorde?.Invoke(), 24);
            _w8Horde.Normal = new Color(0.32f, 0.16f, 0.14f); _w8Horde.SetHover(false);
        }

    }
}
