using Loki.UI;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 15 (w15menu): points of the design's notes on wave 14 (design notes).
    ///   Point 1    "Ring direction: Normal / Reversed" (Reversed the default) for the twist ring (UI/MenuRing.cs, VR/Comfort.cs RingReversed).
    ///   Point 4    "Ring icons: Still / Slide" renamed "Wrist moves: Selector / Icons" (UI/ComfortWindows.cs).
    ///   Point 13   the ring opens on Character; the arch keeps its order, Character just right of its middle (UI/WristMenu.Board.cs ShowRing).
    ///   Point 6    every number in Settings is a slider in fine steps (VR/Comfort.cs Fine*, UI/ComfortWindows.cs, Voice, Horde).
    ///   Point 7    the help panel right of Settings while the laser rests on a row (UI/SettingHelp.cs; below).
    ///   Point 9    "Look ring" / "Show where you are looking" (UI/ComfortWindows.cs).
    ///   Point 10   Classic: a window opened from the wrist menu closes it, with the back arrow and "B and X" (UI/MenuNav.cs CLASSIC).
    ///   Point 31   the overhead menu's "Casting gesture" is a switch: a press picks the aimed choice (LokiGame.W13Resize.cs).
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>Point 7: the Settings window's help panel (null before the game's rig).</summary>
        public SettingHelp W15Help { get; private set; }

        /// <summary>Called by InitW14Menu.</summary>
        private void InitW15Menu(LokiBootstrap boot)
        {
            var rig = boot.Rig;
            if (rig == null || ComfortUi == null) return;
            var win = ComfortUi.Window;
            // the Rumble page's rows have keys of their own ("Menu ring" is also a row of Controls → Menus)
            W15Help = SettingHelp.Create(rig.transform, win, r => r == null || r.Key == null ? null : ComfortUi.AnimPanelShown ? "menuanim" : ComfortUi.Current == ComfortWindow.Page.Rumble ? "rumble:" + r.Key : r.Key); // (client wave 21, w21anim: one text for every switch of the menu animations' list)
        }

        /// <summary>Once a frame (from UpdateW14Menu).</summary>
        private void UpdateW15Menu()
        {
            var rig = _boot.Rig; if (rig == null || W15Help == null) return;
            // point 7: the pointing hand's laser (wave 14's tip rule), else the walking hand's laser on the window (w14laser's second laser)
            var b = rig.LaserButton; if (b == null) b = rig.OffLaserButton;
            W15Help.Tick(b);
        }
    }
}
