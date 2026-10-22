using System.Collections.Generic;
using Loki.UI;
using Loki.VR;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 14 (w14ui), deploy A: points of the design's headset test of wave 13 (design notes).
    ///   Point 18  the laser reaches a panel whatever of the world is in front of it (VR/DioramaRig.Ui.cs LaserHit; the wand's line too).
    ///   Point 12  the pointing hand's TRIGGER on a panel's edge band drags it as the grip does (VR/LokiInput.cs ClaimForTrigger; the probe
    ///             below: the grip's laser candidate of LokiGame.W13Grab.cs).
    ///   Point 16  a panel held upright faces a point behind the player, not the eyes; let go, it faces the eyes (below, LokiGame.W13Grab.cs).
    ///   Point 1   both hands drawn over the panels (VR/DioramaRig.cs MakeHand, VR/HandJoints.cs).
    ///   Point 26  the board's View page has no "Settings…" of its own (UI/WristMenu.Board.cs HostPages).
    ///   Point 29  the menu button with a player's menu open closes everything (below; UI/MenuNav.cs tells the game's windows apart).
    ///   "● Upright" every choice of the overhead menu on one line (UI/OverheadMenu.cs FitChoices).
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>Called by InitW13Resize after the grab's own init: the trigger's probe and the menu button's first say.</summary>
        private void InitW14Ui(LokiBootstrap boot)
        {
            LokiInput.Hand.TriggerGrabProbe = W14TriggerGrabProbe;
            if (Hud != null) Hud.MenuButtonFirst = () => W14CloseEverything("the menu button");
        }

        // ------------------------------------------------------------------ point 12: the trigger on an edge
        /// <summary>Trigger grabs begun by the probe (smoke, log).</summary>
        public int W14TriggerGrabs { get; private set; }
        /// <summary>VR/LokiInput.cs ClaimForTrigger's probe, asked in the frame the pointing hand's trigger clicks: true = the laser is on a
        /// panel's edge band (and nothing is under way that a grab must not interrupt: LokiGame.W13Grab.cs W13GrabBusy), the panel is taken.</summary>
        private bool W14TriggerGrabProbe(LokiInput.Hand input)
        {
            if (this == null || _w12 == null || _boot == null || _boot.Rig == null || !input.IsMain) return false;
            int i = input.IsLeft ? 0 : 1;
            if (_w12[i].In != input || W13GrabBusy(i)) return false;
            var rig = _boot.Rig; var edge = rig.LaserEdgePanel;
            if (edge == null || rig.Dragging != null || W13GrabSkip(edge)) return false;
            var c = _grabPending[i];
            c.Valid = true; c.Panel = edge; c.Laser = true; c.Bag = false; c.At = rig.LaserEdgePoint; c.Dist = Vector3.Distance(rig.PointerOrigin, rig.LaserEdgePoint);
            W14TriggerGrabs++;
            Debug.Log($"[Grab] the {(i == 0 ? "left" : "right")} trigger on the edge of '{edge.name}': it takes the panel (no click), held until the trigger is let go");
            return true;
        }

        // ------------------------------------------------------------------ point 16: what a held upright panel faces
        /// <summary>How far behind the eyes the point a held upright panel faces lies (metres, in the body's heading, at the eyes' height).
        /// 1 m: a panel within arm's reach never comes near it (the hand reaches about 0.4 m behind the eyes at most), and a panel in front
        /// turns little while it is carried (one 30 cm to the side of the eyes' line at 65 cm: 10° from straight ahead, 25° when it faced the eyes).</summary>
        public const float FacingBehind = 1.0f;
        /// <summary>The point a held upright panel faces: <see cref="FacingBehind"/> m behind the eyes in the BODY's heading (the holster's
        /// BodyYaw, VR/WandHolster.cs: a glance does not move it).</summary>
        public Vector3 W14FacingAnchor
        {
            get
            {
                var head = _boot.Rig.Head.transform;
                float yaw = W12Wand != null ? W12Wand.BodyYaw : head.eulerAngles.y;
                return head.position - Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * FacingBehind;
            }
        }
        /// <summary>The heading (degrees about the vertical) of a panel at <paramref name="pos"/> facing <see cref="W14FacingAnchor"/> (its face
        /// away from the point, as a panel facing the eyes has its face away from them); NaN: right over the point.</summary>
        private float W14AnchorYaw(Vector3 pos)
        {
            var d = pos - W14FacingAnchor; d.y = 0f;
            return d.sqrMagnitude < 1e-6f ? float.NaN : Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }
        /// <summary>The panel's turn about the vertical from facing the point (degrees; 0 when it lies flat or sits over the point).</summary>
        private float W14YawFromAnchor(Transform t)
        {
            var f = t.forward; f.y = 0f; float a = W14AnchorYaw(t.position);
            if (f.sqrMagnitude < 1e-6f || float.IsNaN(a)) return 0f;
            return Mathf.DeltaAngle(a, Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg);
        }
        /// <summary>Upright and facing the point behind the player from <paramref name="pos"/>, turned by <paramref name="yawOffset"/>: a turn
        /// about the vertical only (no pitch, no roll).</summary>
        private Quaternion W14HeldUpright(Vector3 pos, float yawOffset, Quaternion keep)
        {
            float a = W14AnchorYaw(pos);
            if (float.IsNaN(a)) { var f = keep * Vector3.forward; f.y = 0f; if (f.sqrMagnitude < 1e-6f) f = Vector3.forward; return Quaternion.LookRotation(f.normalized, Vector3.up); }
            return Quaternion.Euler(0f, a + yawOffset, 0f);
        }
        /// <summary>Upright panels turned to face the eyes at their let go, and the last turn (degrees) (smoke, log).</summary>
        public int W14FacedAtLetGo { get; private set; }
        public float W14LastLetGoTurn { get; private set; }
        /// <summary>Let go (not closed): an upright panel turns at once to face the eyes, about its middle (it does not move); a panel whose
        /// tilt is free keeps its turn.</summary>
        private void W14FaceAtLetGo(RectTransform panel, string why)
        {
            if (why != "let go" || panel == null || W13TiltFree(panel)) return;
            var head = _boot.Rig.Head.transform.position;
            var f = panel.forward; f.y = 0f; var want = panel.position - head; want.y = 0f;
            W14LastLetGoTurn = f.sqrMagnitude > 1e-6f && want.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(f, want, Vector3.up) : 0f;
            panel.rotation = W13Upright(panel.position, panel.rotation); W14FacedAtLetGo++;
            Debug.Log($"[Grab] '{panel.name}' let go: turned {W14LastLetGoTurn:0.0}° to face the eyes");
        }

        // ------------------------------------------------------------------ point 29: the menu button closes everything
        // design note (2026-10-07): "Pressing the controller menu button while a panel is open immediately closes
        // everything, including the wrist menu." With any of the PLAYER's menus open (a board, the ring / arch, the classic wrist menu, Settings,
        // any window the player opened, the VR keyboard unless an NPC asks for text) the menu button closes them all at once (each by its own
        // close: a window's X, which releases what the server holds open), clears item 11's history and opens nothing. What the game opened by
        // itself stays (an NPC dialog, its shop or storage, a trade, an invitation, the check-in at login, Horde's invitation and results:
        // UI/MenuNav.cs; design note: "Yes"), and so do the HUD panels (the chat, the minimap, the forearm bar ...). With none of the player's menus
        // open it opens the ring (Classic: the wrist menu) as before.
        /// <summary>Presses of the menu button that closed everything, and what the last one closed (smoke, log).</summary>
        public int W14MenuCloses { get; private set; }
        public string W14LastClosed { get; private set; } = "";
        /// <summary>The player's menus open now (registered windows, the board, the ring; the classic wrist menu; the keyboard).</summary>
        public List<string> W14PlayerMenusOpen()
        {
            var l = MenuNav.PlayerOpenNames();
            var w = Hud != null ? Hud.Wrist : null;
            if (w != null)
            {
                if (w.ShownOnWrist && !l.Contains("WristMenu")) l.Add("WristMenu");
                if (w.Ring != null && w.Ring.Shown && !l.Contains("MenuRing")) l.Add("MenuRing");
                if (w.Visible && w.OnBoard && !l.Contains("MenuBoard")) l.Add("MenuBoard");
            }
            if (Keyboard != null && Keyboard.Visible && !(Dialog != null && Dialog.WantsKeyboard)) l.Add("VrKeyboard");
            return l;
        }
        /// <summary>Closes every menu the player opened (see above); false: none was open (the menu button then does its usual job).</summary>
        public bool W14CloseEverything(string why)
        {
            if (Hud == null || Hud.Wrist == null) return false;
            var open = W14PlayerMenusOpen();
            if (open.Count == 0) return false;
            var closed = MenuNav.ClosePlayerMenus();
            var w = Hud.Wrist;
            if (w.Visible || (w.Ring != null && w.Ring.Shown)) { w.Hide(); if (!closed.Contains("MenuBoard") && !closed.Contains("MenuRing")) closed.Add(w.OnBoard ? "MenuBoard" : "WristMenu"); }
            if (open.Contains("VrKeyboard")) { Keyboard.Hide(); closed.Add("VrKeyboard"); }
            W14MenuCloses++; W14LastClosed = string.Join(", ", closed);
            var stay = MenuNav.OpenNames(); if (Dialog != null && Dialog.IsOpen) stay.Add("NpcDialog");
            Debug.Log($"[Menu] {why}: everything the player opened closed ({W14LastClosed}), the history cleared; left open (the game's): {(stay.Count > 0 ? string.Join(", ", stay) : "nothing")}");
            Haptics.Pulse(Haptics.Kind.Ui, false, 0.2f, 0.03f);
            return true;
        }
    }
}
