using Loki.UI;
using Loki.VR;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 14 (w14menu), deploy B: points of the design's headset test of wave 13 (design notes) and the design's answers.
    ///   Points 2, 3, 25 + answer 3   the twist ring: all eight groups on the arch, no wrap, it always opens on Settings, the icons still
    ///             with a moving selector (or sliding), the arch or the circle, a rotation sensitivity (UI/MenuRing.cs,
    ///             UI/WristMenu.Board.cs ShowRing, Settings → Controls → Menus: UI/ComfortWindows.cs).
    ///   Point 17 + answer 13   Settings → Comfort and Controls in sections (UI/ComfortWindows.cs SectionsOf, UI/ListWindow.cs SetSubTabs).
    ///   Point 9 + answer 7   the costume switch is Settings → Graphics "Costume looks" (UI/ComfortWindows.cs; LokiGame.cs hooks).
    ///   Point 8   the overhead menu is one of Reposition's things (below; UI/OverheadMenu.cs CarryTo).
    ///   Point 7 + answer 6   a tip over an icon-only button after the LASER rests on it 0.6 s, never by the look (UI/ButtonTip.cs;
    ///             VR/DioramaRig.cs LaserButton).
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>Point 7: the button tip (null before the game's rig).</summary>
        public ButtonTip W14Tip { get; private set; }
        /// <summary>Point 7 at the merge with w14laser (point 10): the walking hand's laser (panels only) shows its own tip, the same rule.</summary>
        public ButtonTip W14Tip2 { get; private set; }
        private Placeable _w14OverheadPlace;
        /// <summary>Point 8: the overhead menu is carried by Reposition now (smoke, log).</summary>
        public bool W14OverheadHeld => _w14OverheadPlace != null && _w14OverheadPlace.By >= 0;

        /// <summary>Called by InitW13Resize after InitW13View (Reposition's list exists) and the grab's init.</summary>
        private void InitW14Menu(LokiBootstrap boot)
        {
            var rig = boot.Rig; var m = W13Overhead;
            if (rig == null || m == null) return;
            // point 8: the overhead menu, taken by the hand at it or the pointing laser + grip, resized by the other grip
            _w14OverheadPlace = new Placeable
            {
                Name = "the overhead menu", Shown = () => W13Reposition && W13Overhead != null, Centre = () => W13Overhead.transform.position,
                Near = p => W13InRect(W13Overhead.FrameWorld, p, PlaceDepth, PlaceMargin), Laser = ray => W13RayRect(W13Overhead.FrameWorld, ray),
                Factor = () => W13Overhead.SizeFactor, Resizable = true, Carry = (w, f) => W13Overhead.CarryTo(w, f),
                Release = () => { Comfort.Save(); Debug.Log($"[Place] the overhead menu: {W13Cm(Comfort.OverheadOffset)} from the eyes in the body's frame ({W13Deg(Comfort.OverheadOffset)}), size x{Comfort.OverheadScale:0.00}"); },
            };
            _w13Places.Add(_w14OverheadPlace);
            // point 7: the tip, and the tips of the icon-only buttons whose glyph alone does not say it (UI/ButtonTip.cs GlyphTip has the rest)
            W14Tip = ButtonTip.Create(rig.transform, rig.Head); W14Tip2 = ButtonTip.Create(rig.transform, rig.Head);
            if (_w13ElbowBtn != null) _w13ElbowBtn.Tip = () => Lang.T("Forearm bar: show / hide");
            InitW15Menu(boot); // client wave 15 (w15menu): the Settings window's help panel (LokiGame.W15Menu.cs)
        }

        /// <summary>Once a frame (from UpdateW13Resize, after Reposition's grips).</summary>
        private void UpdateW14Menu()
        {
            var rig = _boot.Rig; if (rig == null) return;
            if (W13Overhead != null) { W13Overhead.Placing = W13Reposition; W13Overhead.Held = W14OverheadHeld; }
            // point 7 (answer 6): the LASER's button only; never while the look chooses a button the laser is not on
            W14Tip?.Tick(rig.LaserButton);
            var off = rig.OffLaserButton; W14Tip2?.Tick(off != null && off != rig.LaserButton ? off : null); // (the second laser, client wave 14 w14laser: one tip a button)
            UpdateW15Menu(); // client wave 15 (w15menu), point 7
        }
    }
}
