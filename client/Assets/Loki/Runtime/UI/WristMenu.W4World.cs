using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 4 (w4world): a strip of three buttons under the wrist menu (outside its pages, so no tab changes): "Screenshot" (the
    /// headset's view as a PNG in Pictures\Loki), the desktop window's view (eye / overview / third person: World/SpectatorView.cs) and
    /// "Guide" (the navigation window). LokiGame.W4World sets the actions and the label source.
    /// </summary>
    public sealed partial class WristMenu
    {
        public System.Action TakeScreenshot, CycleDesktopView, OpenGuide;
        public System.Func<string> DesktopViewLabel;
        private UiButton _w4Shot, _w4Desk, _w4Guide;

        private void BuildW4World()
        {
            float bw = (W - 40) / 3f, y = -H / 2 - 40 - UiKit.OutsetOf(_rt); // (client wave 21: under the menu's wood frame)
            var strip = UiKit.Box(_rt, "w4world-strip", new Vector2(0, y), new Vector2(W, 72), new Color(1, 1, 1, 0));
            strip.GetComponent<UnityEngine.UI.Image>().enabled = false; UiKit.Dress(strip, new Vector2(W, 72), ClassicMetresPerPx, PanelLook.Card); // client wave 21 (w21kit): a parchment card hanging under the menu's frame
            var block = strip.gameObject.AddComponent<BoxCollider>(); block.size = new Vector3(W, 72, 4f); block.center = new Vector3(0, 0, 3f); // the laser stops on the strip (UiKit.Panel does the same)
            strip.gameObject.AddComponent<UiBlocker>();
            _w4Shot = UiKit.Button(_rt, Lang.T("Screenshot"), new Vector2(-bw - 5, y), new Vector2(bw - 6, 58), () => TakeScreenshot?.Invoke(), 22);
            _w4Desk = UiKit.Button(_rt, Lang.T("Desktop: eye view"), new Vector2(0, y), new Vector2(bw - 6, 58), () => { CycleDesktopView?.Invoke(); RefreshW4World(); }, 19);
            _w4Guide = UiKit.Button(_rt, Lang.T("Guide"), new Vector2(bw + 5, y), new Vector2(bw - 6, 58), () => OpenGuide?.Invoke(), 22);
        }

        /// <summary>The desktop view button names the current view.</summary>
        public void RefreshW4World() { if (_w4Desk != null && DesktopViewLabel != null) _w4Desk.SetLabel(DesktopViewLabel()); }

    }
}
