using Loki.Game;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// The radial skill wheels (specified after the 2026-10-01 playtest): holding the walking hand's trigger (the left one; the
    /// right in left-handed mode) on its own for <see cref="WheelHoldSeconds"/> opens two wheels of the quick bar (UI/SkillWheel.cs):
    /// over the left controller the row the bar shows now, over the right one the next row ("the left radial wheel will always be one
    /// number less than the right one"). Each stick points at a slot of its own wheel; the pointer hand's trigger uses the slot picked
    /// last, as the quick bar does (WristMenu.QuickUseAt: an item is used, a self skill cast, a target skill armed — then cast at once
    /// at what the laser is on, else at the gaze lock, else it stays armed for the next pull); a stick click shows the rows before
    /// (left) or after (right), both wheels together; letting go of the trigger closes them. While they are open the sticks and that
    /// trigger are theirs (VR/DioramaRig.cs InputHeld): no walking, turning, table moves, recentring or world clicks.
    /// Not for bare tracked hands (no sticks). The quick bar's own row (saved on the server) is not changed.
    /// </summary>
    public sealed partial class LokiGame
    {
        public const float WheelHoldSeconds = 0.25f;
        public SkillWheel WheelLeft { get; private set; }
        public SkillWheel WheelRight { get; private set; }
        public bool WheelsOpen { get; private set; }
        /// <summary>Slots used from the wheels (smoke, log).</summary>
        public int WheelUses { get; private set; }
        private float _wheelHold = -1f; private int _wheelRow; private SkillWheel _wheelPick;

        private void InitWheels(LokiBootstrap boot)
        {
            if (boot.Rig == null || Hotkeys == null) return;
            string hint = Lang.T("Stick: choose · laser trigger: use · stick click: other rows");
            WheelLeft = SkillWheel.Create("SkillWheelOff", boot.Rig.transform, boot.Rig.OffHand, boot.Rig.Head, Hotkeys, Inventory, Prog, null);
            WheelRight = SkillWheel.Create("SkillWheelMain", boot.Rig.transform, boot.Rig.PointerHand, boot.Rig.Head, Hotkeys, Inventory, Prog, hint);
            boot.Rig.HandsSwapped += () => { WheelLeft.SetHand(boot.Rig.OffHand); WheelRight.SetHand(boot.Rig.PointerHand); };
        }

        private void UpdateWheels()
        {
            var rig = _boot.Rig;
            if (rig == null || WheelLeft == null || Hud == null) return;
            var off = rig.Off; var main = rig.Main;
            // TriggerFree: 0 while the pull is the casting gesture's, the wand's or the menu ring's (VR/LokiInput.cs; client wave 13: the ring's
            // hand's trigger is the ring's while it is open, and a pull that opened a group stays the ring's until it is let go)
            float trig = off.TriggerFree;
            Vector2 sOff = off.StickValue, sMain = main.StickValue;
            bool clickOff = off.StickClickDown, clickMain = main.StickClickDown, use = main.ClickDown;
            bool allowed = Session != null && Session.State == RoSession.Stage.InGame && !Player.Dead && Hotkeys != null && Hotkeys.Loaded
                && !VR.ControllerProfiles.IsHand(off.Profile) && !Dialog.IsOpen && (Keyboard == null || !Keyboard.Visible);
            if (W8HordeBlocksWorld) allowed = false; // client wave 8 (w8game): no skill wheels while the Horde arena is shown (LokiGame.W8Horde.cs)
            if (Hud.Wrist != null && Hud.Wrist.Ring != null && Hud.Wrist.Ring.Shown) allowed = false; // client wave 13 (w13resize): the menu ring has that trigger
            // Item 48 (design note): never open radial skill wheels while holding/dragging or resizing in Table mode, nor when not chosen as the casting method
            if (off.TableClaimed || main.TableClaimed || off.CameraGrip || main.CameraGrip || VR.LokiInput.ResizeMode || rig.PanningNow || rig.ScalingNow)
                allowed = false;
            bool wheelsChosen = VR.Comfort.SkillWheels
                || (W18TutRunning ? W22LessonKey == "cast-wheels" : (VR.Comfort.CastGesture == 0 && VR.Comfort.QuickBarMode != 2 && string.IsNullOrEmpty(_w25Chosen)));
            if (!wheelsChosen) allowed = false;
            if (!WheelsOpen)
            {
                if (allowed && trig > 0.6f) { if (_wheelHold < 0f) _wheelHold = Time.time; else if (Time.time - _wheelHold >= WheelHoldSeconds) OpenWheels(); }
                else _wheelHold = -1f;
                return;
            }
            if (!allowed || trig < 0.35f) { CloseWheels(); return; }
            int li = SkillWheel.SlotAt(sOff), ri = SkillWheel.SlotAt(sMain);
            if (li >= 0) { WheelLeft.Select(li); if (_wheelPick != WheelLeft) _wheelPick = WheelLeft; }
            if (ri >= 0) { WheelRight.Select(ri); if (_wheelPick != WheelRight) _wheelPick = WheelRight; }
            WheelLeft.SetActive(_wheelPick == WheelLeft); WheelRight.SetActive(_wheelPick == WheelRight);
            if (clickOff) ShiftWheels(-1);
            if (clickMain) ShiftWheels(1);
            if (use) UseWheelSlot();
        }

        private void OpenWheels()
        {
            WheelsOpen = true; _wheelHold = -1f; _wheelPick = null;
            _boot.Rig.Off.ClaimTrigger(); // this pull is the wheels' until it is let go
            ShowWheelRows(Hotkeys.CurrentRow);
            _boot.Rig.InputHeld = true;
            VR.Haptics.Pulse(VR.Haptics.Kind.Casting, false, 0.15f, 0.03f);
            Debug.Log($"[Wheels] open: rows {WheelLeft.Row + 1} / {WheelRight.Row + 1}");
        }

        private void CloseWheels()
        {
            WheelsOpen = false; _wheelHold = -1f; _wheelPick = null;
            WheelLeft.Hide(); WheelRight.Hide();
            _boot.Rig.InputHeld = false;
            Debug.Log("[Wheels] closed");
        }

        private void ShowWheelRows(int row)
        {
            _wheelRow = ((row % Hotkeys.Rows) + Hotkeys.Rows) % Hotkeys.Rows;
            WheelLeft.Show(_wheelRow); WheelRight.Show((_wheelRow + 1) % Hotkeys.Rows);
            _wheelPick = null;
        }

        private void ShiftWheels(int delta)
        {
            ShowWheelRows(_wheelRow + delta);
            VR.Haptics.Pulse(VR.Haptics.Kind.Casting, delta > 0, 0.1f, 0.02f);
            Debug.Log($"[Wheels] rows {WheelLeft.Row + 1} / {WheelRight.Row + 1}");
        }

        private void UseWheelSlot()
        {
            var w = _wheelPick;
            if (w == null || w.Selected < 0) return;
            var h = Hotkeys.At(w.Row, w.Selected);
            if (h.Empty) return;
            WheelUses++;
            Debug.Log($"[Wheels] use row {w.Row + 1} slot {w.Selected + 1}: {(h.IsSkill ? "skill" : "item")} {h.Id}");
            Hud.Wrist.QuickUseAt(w.Row, w.Selected);
            if (_armed == null) return;
            // a target / ground skill was armed: cast it at what the laser is on, else at the gaze lock (as the trigger would after it)
            var rig = _boot.Rig;
            var aimed = VR.Comfort.VisionAimSpells ? W12AimedUnit() : (rig.HoveredTarget != null && rig.HoveredTarget.Unit != null ? rig.HoveredTarget.Unit : Target != null && Target.Locked != null ? Target.Locked.Unit : null);
            if (_armed.Ground) { var c = VR.Comfort.VisionAimSpells ? W12AimedCell() : (rig.PointedCell ?? Target?.GazeCell); if (c.HasValue) CastArmed(null, c); }
            else if (aimed != null) CastArmed(aimed, null);
        }
    }
}
