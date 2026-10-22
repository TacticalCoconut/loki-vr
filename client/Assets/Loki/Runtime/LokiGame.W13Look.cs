using Loki.UI;
using Loki.VR;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 13 (w13look), deploy step 3: items 22, 23 and 24 of design notes (design note, 2026-10-07 ~17:15).
    ///   Item 22  the see-through minimap is clickable by the LASER only: the laser on its map + the trigger walks to that cell exactly
    ///            as w3walk's minimap click did (UI/Minimap.W3Walk.cs W3WalkEnableClicks → LokiGame.W3Walk.cs W3WalkFromMinimap →
    ///            OnCellClicked → Game/RouteWalker.cs); its colliders are on the layer the look skips (UI/LookSelect.cs).
    ///   Item 23  "Look select: off / on" on the overhead menu (after its other controls) and as Settings → Comfort's row after the look
    ///            rows: one stored value (UI/LookSelect.cs, loki-user.json lookSelect, default on). Off: nothing is chosen by looking
    ///            (no panel button, no unit, no ground cell; the look ring hidden; "Wand aim: look" aims with the wand: <see cref="W13AimLook"/>).
    ///            The overhead menu still appears when looked up at (that is not a choice) and its buttons are pressed by the laser:
    ///            the way back on is the laser on its "On", or the Settings row.
    ///   Item 24  the eye at the bottom of every panel the player opens (UI/LookSelect.cs LookEye; each panel places its own).
    ///   Item 25  the item buttons' crosses head-locked low in the view: REMOVED in client wave 14 (w14cast) with the item buttons
    ///            (the design's answer 5); Reposition no longer carries them.
    /// A short note says each change ("Look select off." / "Look select off here.").
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>"Wand aim: look" or "Aim spells with vision" in force: the setting, and the look may choose in the world now.</summary>
        private bool W13AimLook => (Comfort.VisionAimSpells || Comfort.WandAimLook) && (LookSelect.WorldOn || Comfort.VisionAimSpells);

        /// <summary>Item 5: sets "Aim spells with vision" from the overhead menu or code.</summary>
        public void W13SetVisionAim(bool on, string why = "the overhead menu")
        {
            if (Comfort.VisionAimSpells == on) return;
            Comfort.VisionAimSpells = on;
            Comfort.Save();
            W13Overhead?.Refresh();
            if (ComfortUi != null && ComfortUi.Visible) ComfortUi.Refresh();
            W13LastNote = on ? Lang.T("Aim spells with vision on.") : Lang.T("Aim spells with vision off.");
            Hud?.Notify(W13LastNote, 3f);
            Haptics.Pulse(Haptics.Kind.Ui, true, 0.2f, 0.03f);
            Debug.Log($"[View] Aim spells with vision {(on ? "on" : "off")} ({why})");
        }

        /// <summary>Called by InitW13Resize after the other controls (step 2's five).</summary>
        private void InitW13Look(LokiBootstrap boot, OverheadMenu m)
        {
            var rig = boot.Rig;
            m.AddSwitch("look", () => Lang.T("Look select"), 2, () => LookSelect.On ? 1 : 0, k => LookSelect.SetOn(k == 1, "the overhead menu"),
                k => k == 0 ? Lang.T("~switch~Off") : Lang.T("~switch~On"));
            LookSelect.Changed += (on, why) =>
            {
                W13LastNote = on ? Lang.T("Look select on.") : Lang.T("Look select off."); Hud?.Notify(W13LastNote, 3f);
                Haptics.Pulse(Haptics.Kind.Ui, true, 0.2f, 0.03f);
            };
            LookSelect.PanelChanged += (key, off, why) => { W13LastNote = off ? Lang.T("Look select off here.") : Lang.T("Look select on here."); Hud?.Notify(W13LastNote, 2.5f); };
            // item 22: the minimap's map takes the laser's clicks again (w3walk's click-to-walk); the point is where the laser's ray meets
            // the picture's plane (the collider's own face would be a little in front of it)
            if (MiniMap != null)
            {
                var mm = MiniMap;
                mm.W3WalkEnableClicks(() =>
                {
                    var r = new Ray(rig.PointerOrigin, rig.PointerDirection); var map = mm.MapRect;
                    var plane = new Plane(map.forward, map.position);
                    return plane.Raycast(r, out float t) ? r.GetPoint(t) : rig.PointedWorld;
                });
            }
            // (item 25's item crosses were one more thing Reposition placed: gone with the item buttons in client wave 14, w14cast)

            Debug.Log($"[Look] look select {(LookSelect.On ? "on" : "off")}; panels out of the look's choices: {(LookSelect.ExcludedKeys.Length > 0 ? string.Join(", ", LookSelect.ExcludedKeys) : "none")}; the minimap clickable by the laser {MiniMap != null && MiniMap.LaserMapButton != null}");
        }
    }
}
