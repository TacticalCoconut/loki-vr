using Loki.UI;
using Loki.VR;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 14 (w14laser), deploy B: points of the design's headset test of wave 13 (design notes) and the design's answers.
    ///   Point 10 + answer 8   the walking hand's laser, on PANELS only: its trigger presses the button under it (VR/DioramaRig.Laser.cs).
    ///   Point 20 + answer 14  the pointing hand's laser drawn only on what it acts on, faded in near a panel or a unit
    ///             (VR/DioramaRig.Laser.cs), and "Laser walking: Off / On" on the overhead menu (its last control; VR/Comfort.cs
    ///             LaserWalk, loki-user.json laserWalk, default on). Off: the trigger on the ground walks nowhere (LokiGame.cs
    ///             OnFreeTrigger: an armed skill and the look's lock are as before) and the laser is not drawn on the ground unless a
    ///             ground skill waits to be aimed (<see cref="DioramaRig.GroundAimWanted"/>). Walking by the stick and by the
    ///             see-through minimap (a panel) stay.
    ///   Point 11 + answer 9   the Follow view's tilt: in resize mode, one grip alone (either hand) + that hand raised or lowered; the turn
    ///             stick no longer tilts (VR/DioramaRig.cs PitchHand, VR/ViewController.cs).
    ///   Point 23 + answer 16  the Table view's free grab: a grip that starts in empty air is the table's, as in resize mode, with no resize
    ///             mode (VR/LokiInput.cs ClaimForTable; the probe below says where a grip has another job).
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>"Laser walking" changes made on the overhead menu, grips the Table view gave the table, the last probe's refusal (smoke, log).</summary>
        public int W14LaserWalkChanges { get; private set; }
        public int W14TableGrips { get; private set; }
        public string W14TableRefused { get; private set; } = "";

        /// <summary>Called by InitW13Resize after the other controls: the overhead menu's "Laser walking", the ground aim, the table's probe.</summary>
        private void InitW14Laser(LokiBootstrap boot, OverheadMenu m)
        {
            var rig = boot.Rig;
            m.AddSwitch("laserwalk", () => Lang.T("Laser walking"), 2, () => Comfort.LaserWalk ? 1 : 0, k => W14SetLaserWalk(k == 1, "the overhead menu"),
                k => k == 0 ? Lang.T("~switch~Off") : Lang.T("~switch~On"));
            rig.GroundAimWanted = () => _armed != null && _armed.Ground; // (a ground skill is aimed with the laser on the ground)
            LokiInput.Hand.TableProbe = W14TableProbe;
            Debug.Log($"[Laser] laser walking {(Comfort.LaserWalk ? "on" : "off")}; the walking hand's laser on panels; the Table view's free grab");
        }

        /// <summary>"Laser walking: Off / On" (the overhead menu).</summary>
        public void W14SetLaserWalk(bool on, string why)
        {
            if (Comfort.LaserWalk == on) return;
            Comfort.LaserWalk = on; Comfort.Save(); W14LaserWalkChanges++;
            Debug.Log($"[Laser] laser walking {(on ? "on" : "off")} ({why})");
            W13LastNote = on ? Lang.T("Laser walking on.") : Lang.T("Laser walking off."); Hud?.Notify(W13LastNote, 3f);
            Haptics.Pulse(Haptics.Kind.Ui, true, 0.2f, 0.03f);
        }

        /// <summary>LokiGame.cs OnFreeTrigger: the trigger on the ground walks there now (false: "Laser walking" is off).</summary>
        private bool W14LaserWalks(Vector2Int cell)
        {
            if (Comfort.LaserWalk) return true;
            Debug.Log($"[Laser] trigger on cell {cell.x},{cell.y}: laser walking is off, no walk");
            return false;
        }

        /// <summary>VR/LokiInput.cs ClaimForTable's probe, asked once in the frame a grip starts (not in resize mode, after the panel grabs):
        /// true = the grip is the table's until it is let go. Only in the Table view, and never where the grip has another job.</summary>
        private bool W14TableProbe(LokiInput.Hand input)
        {
            if (this == null || _boot == null || _boot.Rig == null || View == null || _w12 == null) return false;
            string no = W14TableNo(input);
            int i = input.IsLeft ? 0 : 1;
            if (no != null) { W14TableRefused = no; if (View.Mode == ViewController.ViewMode.Table) Debug.Log($"[Table] the {(i == 0 ? "left" : "right")} grip is not the table's: {no}"); return false; }
            W14TableGrips++; W14TableRefused = "";
            Debug.Log($"[Table] the {(i == 0 ? "left" : "right")} grip holds the table (the Table view's free grab)");
            return true;
        }

        /// <summary>Why a grip starting now is not the table's (null: it is).</summary>
        private string W14TableNo(LokiInput.Hand input)
        {
            if (View.Mode != ViewController.ViewMode.Table) return "not the Table view";
            if (LokiInput.ResizeMode) return "resize mode"; // (its own camera grip)
            if (W13Reposition) return "Reposition"; // its grips carry things
            if (W12WandPlacing) return "the holster's placement";
            if (input.WandHeld || input.GrabClaimed) return input.WandHeld ? "the wand" : "a panel grab";
            var rig = _boot.Rig;
            if (rig.InputHeld) return "the skill wheels"; // (their sticks)
            if (input.IsMain && (rig.LaserPanel != null || rig.LaserEdgePanel != null || rig.Grabbed != null)) return "the laser is on a panel";
            if (LokiInput.CastGestureEnabled && input.Pointing) return "the casting gesture (the index finger points)";
            int i = input.IsLeft ? 0 : 1;
            if (W12Wand != null && Comfort.CastGesture == 3 && Vector3.Distance(_w12[i].T.position, W12Wand.HolsterPos) <= WandHolster.Radius) return "the wand's holster";
            return null;
        }
    }
}
