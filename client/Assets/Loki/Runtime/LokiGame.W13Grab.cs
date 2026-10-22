using System;
using System.Collections.Generic;
using Loki.Game;
using Loki.UI;
using Loki.VR;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 13 (w13grab), deploy step 3: items 19 and 20 of design notes.
    ///   Item 19   (design note, 2026-10-07: "allow the player to move around regular HUD panels (like the settings panel, inventory, quests panel,
    ///             etc) by grabbing the edges with their hand or the laser. Give it a visual indicator when it a grab will lead to it being
    ///             dragged, for convenience. By default the panels cannot be rotated along the pitch or roll axis, but shaking a panel
    ///             unlocks that rotation until it is closed. The default behavior can be toggled with the overhead menu"; ~16:00: "When you
    ///             shake a panel, that panel will be affected by the opposite of whatever is the default mode").
    ///             ALWAYS (not only in resize mode): a plain grip of a hand whose grip point is at a panel's EDGE BAND, or of the pointing
    ///             hand while its laser is on one (UI/PanelGrab.cs: which panels, the band, the indicator), takes the panel. By hand it keeps
    ///             its offset to the hand and follows it; by laser it rides the laser at the distance it was taken (a fishing line), and
    ///             that hand's stick pushes it away / pulls it nearer (<see cref="GrabPushSpeed"/> m/s, <see cref="GrabMinDist"/>-
    ///             <see cref="GrabMaxDist"/> m; client wave 15: for every grab, by hand too, smoothed: LokiGame.W15Grab.cs). Let go, it stays there (a board keeps its distance, height and size for its next opening,
    ///             as wave 11's grab did: UI/WristMenu.Board.cs KeepBoardPlace; the windows open in front of the eyes again as always).
    ///             The edge grab never resizes (resize mode's two grips do). A trigger on the edge does nothing (VR/DioramaRig.cs).
    ///             TILT: "Panel tilt: upright / free" (VR/Comfort.cs PanelTiltFree, default upright; the overhead menu's sixth control and
    ///             Settings → Controls: one stored value). Upright: a held panel turns only about the vertical, level and vertical, facing
    ///             the head. Free: it takes the hand's full rotation. A SHAKE of the hand holding a panel (Game/ShakeDetector.cs, resize
    ///             mode's numbers) gives THAT panel the other one until it is closed (a note "Tilt free" / "Upright", a tick); a second
    ///             shake gives it back the default; a new default with a shaken panel open: that panel keeps the other one of the NEW
    ///             default (shaken = always "the other one"). Closed: forgotten (it opens with the default).
    ///   Item 20   (design note: "Add a 'backpack grab', that pulls out the inventory menu if you grab behind your shoulders"): a plain grip of
    ///             either hand in the BACKPACK ZONE behind its own shoulder (<see cref="W13InPack"/>) opens the bag (the new menu's Items
    ///             board on its Bag page; already open: the same board) IN THAT HAND, held by its outer side's middle (the right hand
    ///             holds its right edge, the left its left), upright, as an item 19 grab; let go, it stays. A tick when a hand enters
    ///             the zone with its grip free. Item 11: a player's menu opened anew (the menu that was open closes). Classic: the wrist
    ///             menu opens on its Items page, on the wrist as always (it rides on the hand: nothing to carry).
    ///   THE RULE  (design note: "Be careful not to allow this (or ANY other panel grab) to badly interact with the casting systems"): the grip's
    ///             claim is decided ONCE, in the frame it starts, before the casting gesture's timer (VR/LokiInput.cs ClaimForGrab asks
    ///             <see cref="W13GrabProbe"/>): a grip that starts at an edge or in the zone is the grab's (no pad, no flick, no wand,
    ///             no item buttons, no wheels from its trigger, no camera in resize mode, no Reposition), and a grip that started as
    ///             anything else never becomes a grab. Nothing is offered while a casting is under way (a pad / flick open, the wand
    ///             drawing, the skill wheels open), to a hand that holds the wand, at a thing Reposition or the holster's placement
    ///             would take, or in Horde's arena. A held panel is a placing state for item 7 (W13CastingAllowed: "a panel held").
    ///             A shake of a hand that holds a panel is the panel's: it never ends resize mode or Reposition (LokiGame.W13Resize.cs).
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>The backpack zone, in the BODY's frame (the holster's heading: VR/WandHolster.cs BodyYaw; origin the eyes): the shoulder
        /// estimated <see cref="ShoulderSide"/> m to its side, <see cref="ShoulderDown"/> m under the eyes and <see cref="ShoulderBack"/> m
        /// behind them; the zone: at least <see cref="PackBehind"/> m behind the eyes' plane (2 cm behind the shoulder), not lower than
        /// <see cref="PackBelow"/> m under the shoulder, on the hand's own side of the head, within <see cref="PackReach"/> m of that
        /// shoulder, and never within the holster's reach + <see cref="PackHolsterGap"/> m (the two never overlap).</summary>
        public const float ShoulderSide = 0.18f, ShoulderDown = 0.23f, ShoulderBack = 0.08f, PackBehind = 0.10f, PackBelow = 0.05f, PackReach = 0.35f, PackHolsterGap = 0.05f;
        public const float GrabPushSpeed = 0.8f, GrabMinDist = 0.25f, GrabMaxDist = 2.5f, GrabTick = 0.12f;

        private sealed class GrabCand { public RectTransform Panel; public bool Laser, Bag, Tree; public Vector3 At; public float Dist; public bool Valid; } // (Tree: client wave 22, w22tree: a grip on the 3D skill tree pulls it: LokiGame.W22Tree.cs)
        private sealed class GrabHold
        {
            public RectTransform Panel; public bool Laser, Bag, Free; public Vector3 GrabLocal, Offset, RelPos; public float Dist; public Quaternion RelRot;
            /// <summary>Client wave 14 (w14ui), point 16: an upright held panel's turn from facing the point behind the player (degrees), kept
            /// from the grab so that taking a panel never turns it (LokiGame.W14Ui.cs W14FacingAnchor).</summary>
            public float YawOffset;
            /// <summary>Client wave 15 (w15grab): point 5's push along the line from the eyes (a hand's grab, metres; the bag starts at
            /// BagReach: point 28), a free tilt's grab point in the hand's frame, point 8's filter (LokiGame.W15Grab.cs).</summary>
            public float Push, Speed, Alpha = 1f; public Vector3 RelGrab, SmoothAt, TargetWas; public bool SmoothInit;
            public float AngSpeed, AlphaRot = 1f; public Quaternion WantWas = Quaternion.identity; // (client wave 19, point 61: the rotation's own filter)
            public UiGrabbable Grabbable; public readonly ShakeDetector Shake = new ShakeDetector();
            /// <summary>Client wave 23 (w23inv), design note: "the inventory menu should always spawn upright": the hold that pulls the NEW
            /// inventory out of the backpack carries it upright whatever "Panel tilt" says (the bundle, and the strip once it has
            /// sprung open in that hand), until the hand lets go or shakes it (<see cref="W23HoldFree"/>).</summary>
            public bool BagUpright;
            /// <summary>Client wave 24 (w24): the keyboard lies tilted towards the eyes: an upright hold keeps the tilt it was taken
            /// with (degrees about its own right; 0 for every other panel: upright means vertical).</summary>
            public float KeepPitch;
        }
        /// <summary>Client wave 23 (w23inv): this hold follows the hand's full rotation now: the panel's own rule
        /// (<see cref="W13TiltFree"/>), except the pulled-out new inventory's hold, which is upright (GrabHold.BagUpright).</summary>
        private bool W23HoldFree(GrabHold hold) => !hold.BagUpright && W13TiltFree(hold.Panel);
        private readonly GrabCand[] _grabCand = { new GrabCand(), new GrabCand() }, _grabPending = { new GrabCand(), new GrabCand() };
        private readonly GrabHold[] _grabHold = new GrabHold[2];
        private readonly bool[] _grabStarted = new bool[2], _grabReadyWas = new bool[2];
        private readonly Dictionary<Transform, bool> _grabShaken = new Dictionary<Transform, bool>();
        private readonly List<Transform> _grabGone = new List<Transform>();
        private PanelGrab.Light[] _grabLights;

        /// <summary>Grabs begun (panels, the bag), let go, panels carried away closed; ticks of a hand or the laser coming onto an edge; of
        /// a hand entering the backpack zone; bags pulled out; shakes of a held panel (smoke, log).</summary>
        public int W13GrabStarts { get; private set; }
        public int W13GrabReleases { get; private set; }
        public int W13GrabTicks { get; private set; }
        public int W13PackTicks { get; private set; }
        public int W13BagPulls { get; private set; }
        public int W13GrabShakes { get; private set; }
        /// <summary>Client wave 19 (w19world), point 60 (design note: "Add a global cooldown to the 'shake panel to lock or unlock its rotation'
        /// function. 2s cooldown, to prevent it from being accidentally turned on an then off again"): after a shake turns a panel's tilt,
        /// every shake of a held panel (any panel, either hand) is ignored for this long. No cue while it waits (Loki's choice). Shakes
        /// ignored so (smoke, log). Resize mode's and Reposition's shake (LokiGame.W13Resize.cs) is another function: not affected.</summary>
        public const float GrabShakeCooldown = 2f;
        public int W13GrabShakesIgnored { get; private set; }
        public bool W13GrabShakeCooling => Time.unscaledTime - _w13GrabShakeAt < GrabShakeCooldown;
        private float _w13GrabShakeAt = -100f;
        /// <summary>The panel hand i (0 left, 1 right) holds now (null: none), and whether by its laser; whether it was the bag's pull.</summary>
        public RectTransform W13Holding(int hand) => _grabHold[hand] != null ? _grabHold[hand].Panel : null;
        public bool W13HoldingByLaser(int hand) => _grabHold[hand] != null && _grabHold[hand].Laser;
        public float W13HoldDistance(int hand) => _grabHold[hand] != null ? _grabHold[hand].Dist : 0f;
        /// <summary>A panel is held by either hand now (item 7's placing state).</summary>
        public bool W13PanelHeld => _grabHold[0] != null || _grabHold[1] != null;
        /// <summary>The indicator of hand i: shown, on which panel, held (orange) or ready (cyan), where its mark is.</summary>
        public PanelGrab.Light W13GrabLight(int hand) => _grabLights != null ? _grabLights[hand] : null;
        /// <summary>What a grip of hand i would take now (its grip free): a panel (by the hand or its laser), the bag, or nothing.</summary>
        public RectTransform W13GrabReadyPanel(int hand) => _grabReadyWas[hand] ? _grabCand[hand].Panel : null;
        public bool W13GrabReadyLaser(int hand) => _grabReadyWas[hand] && _grabCand[hand].Laser;
        public bool W13GrabReadyBag(int hand) => _grabReadyWas[hand] && _grabCand[hand].Bag;
        /// <summary>A held panel follows the hand's full rotation now (the default, or the other one when it was shaken).</summary>
        public bool W13TiltFree(Transform panel) => (Comfort.PanelTiltFree || WCodexHandBook(panel)) != (panel != null && _grabShaken.TryGetValue(panel, out bool s) && s); // (wave 28, wcodex: the hand-held Codex is held as a book is, with the hand's full turn)
        public bool W13Shaken(Transform panel) => panel != null && _grabShaken.TryGetValue(panel, out bool s) && s;

        /// <summary>Called by InitW13Resize after the overhead menu's other controls: its sixth control, the indicators, the claim's probe.</summary>
        private void InitW13Grab(LokiBootstrap boot, OverheadMenu m)
        {
            var rig = boot.Rig;
            m.AddSwitch("tilt", () => Lang.T("Panel tilt"), 2, () => Comfort.PanelTiltFree ? 1 : 0, k => W13SetPanelTilt(k == 1, "the overhead menu"),
                k => k == 0 ? Lang.T("~tilt~Upright") : Lang.T("~tilt~Free"));
            _grabLights = new[] { new PanelGrab.Light(rig.transform, "GrabEdgeLeft"), new PanelGrab.Light(rig.transform, "GrabEdgeRight") };
            rig.GrabSkip = W13GrabSkip;
            LokiInput.Hand.GrabProbe = W13GrabProbe;
        }

        /// <summary>"Panel tilt: upright / free" (the overhead menu; the Settings row writes the same value).</summary>
        public void W13SetPanelTilt(bool free, string why)
        {
            if (Comfort.PanelTiltFree == free) return;
            Comfort.PanelTiltFree = free; Comfort.Save();
            if (ComfortUi != null && ComfortUi.Visible) ComfortUi.Refresh(); // (the Settings row is the same setting: it shows the same)
            Debug.Log($"[Grab] panel tilt: {(free ? "free" : "upright")} ({why})");
        }

        /// <summary>A panel not to offer: held by a hand already, or by resize mode's board grab.</summary>
        private bool W13GrabSkip(RectTransform rt)
        {
            if (rt == null) return true;
            for (int i = 0; i < 2; i++) if (_grabHold[i] != null && _grabHold[i].Panel == rt) return true;
            var g = rt.GetComponent<UiGrabbable>(); return g != null && g.Held;
        }

        /// <summary>The hand is in the backpack zone behind its own shoulder (see the constants).</summary>
        public bool W13InPack(int hand) => _w12 != null && W13InPackAt(hand, _w12[hand].T.position);
        /// <summary>The point <paramref name="p"/> is in hand <paramref name="hand"/>'s backpack zone (0 left: behind the left shoulder, 1 right).</summary>
        public bool W13InPackAt(int hand, Vector3 p)
        {
            var rig = _boot.Rig; if (rig == null) return false;
            var head = rig.Head.transform;
            float yaw = W12Wand != null ? W12Wand.BodyYaw : head.eulerAngles.y;
            var l = Quaternion.Inverse(Quaternion.Euler(0f, yaw, 0f)) * (p - head.position); // x right, y up, z ahead of the body
            float side = hand == 0 ? -1f : 1f;
            var shoulder = new Vector3(side * ShoulderSide, -ShoulderDown, -ShoulderBack);
            bool zone = l.z <= -PackBehind && l.y >= -(ShoulderDown + PackBelow) && l.x * side >= 0f && (l - shoulder).magnitude <= PackReach;
            if (zone && W12Wand != null && Vector3.Distance(p, W12Wand.HolsterPos) <= WandHolster.Radius + PackHolsterGap) zone = false; // (never the holster's)
            return zone;
        }

        /// <summary>No grab is offered now (nothing at all, or not to this hand): the world is not shown, Horde's arena, a casting under way.</summary>
        private bool W13GrabBusy(int hand)
        {
            var rig = _boot.Rig;
            if (rig == null || _w12 == null || !(W13InGame || W24BeforeWorld) || W8HordeBlocksWorld) return true; // (client wave 24, w24: on the screens before the world too: the login panel and the keyboard. Until then nothing was offered before the world.)
            if (rig.InputHeld || _wandDrawing) return true; // the skill wheels open, the wand drawing
            for (int k = 0; k < 2; k++) if (_w12[k].Open) return true; // a pad / flick open (either hand)
            var h = _w12[hand];
            if (h.In.WandHeld || _wandHand == h || _grabHold[hand] != null) return true;
            if (W12WandPlacing && W12Wand != null && Vector3.Distance(h.T.position, W12Wand.HolsterPos) <= WandHolster.Radius) return true; // the holster's placement takes it
            if (W13Reposition || LokiInput.ResizeMode) foreach (var pl in _w13Places) if (pl.NearWas[hand] || (h.In.IsMain && pl.LaserWas)) return true; // Reposition takes it (client wave 21, w21wand: or resize mode, the wand at its holster: only the mode's own things are ever "near": W13PlaceUsable)
            return false;
        }

        /// <summary>What a grip of hand i would take now (c.Valid false: nothing): the hand at a panel's edge, else its laser on one (the
        /// pointing hand), else the bag (the backpack zone).</summary>
        private bool W13GrabCandidate(int hand, GrabCand c)
        {
            c.Valid = false; c.Panel = null; c.Laser = c.Bag = c.Tree = false;
            if (W13GrabBusy(hand)) return false;
            var rig = _boot.Rig; var h = _w12[hand];
            if (WCodexPassAt(hand, h.T.position, out var passAt, out float passD)) { c.Panel = CodexBook.Root; c.At = passAt; c.Dist = passD; return c.Valid = true; } // wave 28 (wcodex): the other hand holds the hand-held Codex: this hand takes it over
            var p = PanelGrab.AtHand(h.T.position, W13GrabSkip, out var rim, out float d);
            if (p != null) { c.Panel = p; c.At = rim; c.Dist = d; return c.Valid = true; }
            if (h.In.IsMain && rig.LaserEdgePanel != null && rig.Dragging == null && !W13GrabSkip(rig.LaserEdgePanel))
            {
                c.Panel = rig.LaserEdgePanel; c.Laser = true; c.At = rig.LaserEdgePoint; c.Dist = Vector3.Distance(rig.PointerOrigin, rig.LaserEdgePoint);
                return c.Valid = true;
            }
            if (!W13InGame) return false; // (client wave 24, w24: before the world only panels are taken: no skill tree, no backpack)
            if (W22TreeAt(hand)) { c.Tree = true; c.At = h.T.position; return c.Valid = true; } // client wave 22 (w22tree), point 64: the 3D skill tree's pull
            if (W13InPack(hand)) { c.Bag = true; c.At = h.T.position; return c.Valid = true; }
            return false;
        }

        /// <summary>VR/LokiInput.cs ClaimForGrab's probe, asked once in the frame a grip starts: true = this grip is the grab's.</summary>
        private bool W13GrabProbe(LokiInput.Hand input)
        {
            if (this == null || _w12 == null || _boot == null || _boot.Rig == null) return false;
            int i = input.IsLeft ? 0 : 1;
            if (_w12[i].In != input) return false;
            var c = _grabPending[i];
            if (!W13GrabCandidate(i, c)) return false;
            Debug.Log($"[Grab] the {(i == 0 ? "left" : "right")} grip is the grab's from its first frame: {(c.Tree ? "the skill tree's pull" : c.Bag ? "the backpack" : (c.Laser ? "the laser on the edge of " : "the hand at the edge of ") + c.Panel.name)}");
            return true;
        }

        /// <summary>Once a frame, first thing of the new casting's update (so a panel taken this frame makes the casting inert this frame).</summary>
        private void UpdateW13Grab()
        {
            var rig = _boot.Rig; if (rig == null || _w12 == null || _grabLights == null) return;
            float dt = Time.unscaledDeltaTime;
            UpdateW24Login(); // client wave 24 (w24): a let-go panel's place kept once more after its easing (LokiGame.W24Login.cs)
            UpdateW24Hud(); // client wave 24 (w24), item 9: the chat's open tab (LokiGame.W24Hud.cs)
            UpdateW22Tree(); // client wave 22 (w22tree): the notices over the open skill tree (LokiGame.W22Tree.cs)
            UpdateW15Ease(); // client wave 15 (w15grab), point 28: panels let go too near the eyes ease out
            // a shaken panel that was closed is forgotten: it opens with the default again
            _grabGone.Clear(); foreach (var kv in _grabShaken) if (!PanelGrab.IsShown(kv.Key)) _grabGone.Add(kv.Key);
            foreach (var t in _grabGone) { _grabShaken.Remove(t); Debug.Log($"[Grab] '{(t != null ? t.name : "?")}' closed: its shaken tilt forgotten"); }
            for (int i = 0; i < 2; i++)
            {
                var h = _w12[i]; var inp = h.In;
                if (inp.GrabClaimed)
                {
                    if (_grabHold[i] == null && !_grabStarted[i]) { _grabStarted[i] = true; W13GrabStart(i, _grabPending[i]); }
                }
                else
                {
                    if (_grabHold[i] != null) W13GrabRelease(i, "let go");
                    _grabStarted[i] = false;
                }
                W22PullTick(i, inp.GrabClaimed); // client wave 22 (w22tree): the skill tree follows the hand that pulls it
                if (_grabHold[i] != null) W13GrabCarry(i, dt);
                // the indicator: lit (cyan) exactly while a grip now would drag a panel; orange while one is held
                var c = _grabCand[i]; W13GrabCandidate(i, c);
                bool ready = c.Valid && !inp.PlainGrip && !c.Tree; // (client wave 22: the tree's pull has no edge light and no tick: the hand is "in the tree" all the time)
                var light = _grabLights[i]; var hold = _grabHold[i];
                if (hold != null && hold.Panel != null) { if (WCodexHandBook(hold.Panel)) light.Hide(); else light.Show(hold.Panel, true, hold.Panel.TransformPoint(hold.GrabLocal), rig.Head); } // (wave 28, wcodex: a book held in the hand is read while held: no "held" glow over its pages; the grab's tick says it is taken)
                else if (ready && c.Panel != null) light.Show(c.Panel, false, c.At, rig.Head);
                else if (W13ResizeBoardReady(inp, out var lp)) light.Show(lp, false, rig.PointedWorld.Value, rig.Head); // resize mode's own board grab (VR/DioramaRig.Ui.cs): a grip drags it too
                else light.Hide();
                if (ready && !_grabReadyWas[i])
                {
                    if (c.Bag) W13PackTicks++; else W13GrabTicks++;
                    Haptics.Pulse(Haptics.Kind.Grab, inp.IsMain, GrabTick, 0.02f);
                }
                _grabReadyWas[i] = ready;
            }
        }

        /// <summary>Resize mode: the pointing hand's laser on a board (not its edge) with the grip free: a grip drags it there too (wave 11's
        /// grab, VR/DioramaRig.Ui.cs UpdateGrab), so the indicator lights for it as well.</summary>
        private bool W13ResizeBoardReady(LokiInput.Hand inp, out RectTransform board)
        {
            var rig = _boot.Rig; board = null;
            if (!inp.IsMain || !LokiInput.ResizeMode || inp.PlainGrip || rig.Grabbed != null || rig.LaserEdgePanel != null || rig.LaserPanel == null || !rig.PointedWorld.HasValue) return false;
            var g = rig.LaserPanel.GetComponent<UiGrabbable>();
            if (g == null || g.Held) return false;
            board = rig.LaserPanel as RectTransform; return board != null;
        }

        private void W13GrabStart(int i, GrabCand c)
        {
            var rig = _boot.Rig; var h = _w12[i];
            if (!c.Valid) return;
            if (c.Tree) { W22PullStart(i); return; } // client wave 22 (w22tree), point 64
            RectTransform panel = c.Panel; Vector3 grabWorld = c.At;
            if (c.Bag)
            {
                W13BagPulls++;
                if (Hud != null && Hud.Wrist != null && Hud.Wrist.BodyYaw == null) Hud.Wrist.BodyYaw = () => W12Wand != null ? W12Wand.BodyYaw : _boot.Rig.Head.transform.eulerAngles.y; // (client wave 21, w21inv: the bundle springs open in front of the body)
                bool board = Hud != null && Hud.Wrist != null && Hud.Wrist.OpenBag();
                Debug.Log($"[Grab] the backpack: the bag pulled out by the {(i == 0 ? "left" : "right")} hand{(board ? "" : " (Classic: the wrist menu's Items page, on the wrist)")}");
                if (!board) { Haptics.Pulse(Haptics.Kind.Grab, h.In.IsMain, 0.2f, 0.03f); return; }
                panel = Hud.Wrist.Board.Root;
                if (W13GrabSkip(panel)) { Debug.Log("[Grab] the backpack: the bag is held already (the other hand, or resize mode's grab): shown, not taken"); return; }
                // held by its outer side's middle: the right hand its right edge, the left hand its left edge
                var side = Hud.Wrist.BagHoldLocal(i); // (client wave 21, w21inv: the new inventory is held by the middle of its lower edge: UI/WristMenu.Inv.cs)
                // (client wave 14, point 16: facing the point behind the player, not the eyes: it passes close to the head as it comes out;
                // client wave 15, point 11: the same rule as while it is carried, LokiGame.W15Grab.cs W15Upright)
                // (w15fix: from the held point, the hand, side.x right of its middle: final at once, one placing)
                panel.rotation = W15Upright(h.T.position, side.x * panel.lossyScale.x, panel.rotation);
                panel.position += h.T.position - panel.TransformPoint(side);
                grabWorld = panel.TransformPoint(side);
            }
            if (panel == null) return;
            for (int o = 0; o < 2; o++) if (o != i && _grabHold[o] != null && _grabHold[o].Panel == panel) { W13GrabRelease(o, "passed to the other hand"); WCodexPasses++; } // wave 28 (wcodex): taken from the other hand
            bool stands = c.Bag && Hud != null && Hud.Wrist != null && Hud.Wrist.InvShown; // client wave 23 (w23inv): the new inventory out of the backpack: upright in this hand
            var hold = new GrabHold { Panel = panel, Laser = c.Laser, Bag = c.Bag, BagUpright = stands, Free = !stands && W13TiltFree(panel) };
            // (client wave 14, point 16 kept the panel's own turn from the grab: YawOffset; client wave 15, point 11: an upright panel faces the
            // player while it is held, so it is no longer kept: LokiGame.W15Grab.cs)
            if (panel == _w24Kb) hold.KeepPitch = -W13Pitch(panel); // client wave 24 (w24): the keyboard keeps the tilt it has
            hold.Push = c.Bag ? BagReach : 0f; // client wave 15, point 28: the bag held farther out than the hand
            hold.GrabLocal = panel.InverseTransformPoint(grabWorld);
            // (a flat panel is held by a point of its plane; client wave 21 (w21inv): a panel bent on a cylinder, the new inventory, by the
            // point of its curve that was taken: its ends stand far in front of the plane of its middle)
            { var bend = panel.GetComponent<UiBend>(); if (bend == null || !bend.On) hold.GrabLocal.z = 0f; }
            hold.Offset = panel.TransformPoint(hold.GrabLocal) - h.T.position;
            hold.Dist = c.Laser ? Mathf.Clamp(Vector3.Distance(rig.PointerRayNow().origin, panel.TransformPoint(hold.GrabLocal)), GrabMinDist, GrabMaxDist) : 0f;
            var refRot = c.Laser ? rig.PointerHand.rotation : h.T.rotation;
            hold.RelRot = Quaternion.Inverse(refRot) * panel.rotation; hold.RelPos = Quaternion.Inverse(h.T.rotation) * (panel.position - h.T.position);
            hold.RelGrab = Quaternion.Inverse(h.T.rotation) * (panel.TransformPoint(hold.GrabLocal) - h.T.position);
            hold.Grabbable = panel.GetComponent<UiGrabbable>();
            if (hold.Grabbable != null) { hold.Grabbable.Held = true; hold.Grabbable.Grabs++; }
            hold.Shake.Reset();
            _grabHold[i] = hold; W13GrabStarts++;
            Haptics.Pulse(Haptics.Kind.Grab, h.In.IsMain, 0.2f, 0.03f);
            Debug.Log($"[Grab] '{panel.name}' taken by the {(i == 0 ? "left" : "right")} {(c.Laser ? $"hand's laser at {hold.Dist * 100f:0} cm" : "hand")}{(c.Bag ? $" (the backpack, held {BagReach * 100f:0} cm beyond the hand)" : "")}, tilt {(hold.Free ? "free" : "upright")}, smoothing {Comfort.PanelSmoothing * 100f:0} %");
        }

        /// <summary>The held panel follows the hand (or the laser): upright or with the hand's full rotation; a shake gives it the other tilt.</summary>
        private void W13GrabCarry(int i, float dt)
        {
            var rig = _boot.Rig; var h = _w12[i]; var hold = _grabHold[i]; var t = hold.Panel;
            if (t == null || !PanelGrab.IsShown(t)) { W13GrabRelease(i, "the panel closed"); return; }
            var hand = h.T;
            // a shake of the hand that holds it: the panel's own (never resize mode's or Reposition's: LokiGame.W13Resize.cs)
            bool shook = hold.Shake.Update(rig.transform.InverseTransformPoint(hand.position), dt);
            if (shook && W13GrabShakeCooling) // (client wave 19, point 60: the global cooldown)
            {
                W13GrabShakesIgnored++;
                Debug.Log($"[Grab] '{t.name}' shaken {Time.unscaledTime - _w13GrabShakeAt:0.0} s after the last turn: ignored (cooldown {GrabShakeCooldown:0} s)");
            }
            else if (shook)
            {
                _w13GrabShakeAt = Time.unscaledTime;
                // (client wave 23, w23inv: a shake ends the pulled-out inventory's upright hold: from here the panel's own rule. The
                // panel's "shaken" turns only when the hold's tilt would not change without it, so a shake always gives the other tilt.)
                bool wasFree = W23HoldFree(hold); hold.BagUpright = false;
                if (W13TiltFree(t) == wasFree) _grabShaken[t] = !W13Shaken(t);
                W13GrabShakes++;
                bool free = W13TiltFree(t);
                W13LastNote = free ? Lang.T("Tilt free") : Lang.T("Upright"); Hud?.Notify(W13LastNote, 2f);
                Haptics.Pulse(Haptics.Kind.Grab, h.In.IsMain, 0.25f, 0.04f);
                Debug.Log($"[Grab] '{t.name}' shaken: tilt {(free ? "free" : "upright")} until it is closed (the default: {(Comfort.PanelTiltFree ? "free" : "upright")})");
            }
            bool nowFree = W23HoldFree(hold); // (client wave 23, w23inv: the pulled-out new inventory's hold is upright: GrabHold.BagUpright)
            var refRot = hold.Laser ? rig.PointerHand.rotation : hand.rotation;
            if (nowFree != hold.Free)
            {
                hold.Free = nowFree; var gp = t.TransformPoint(hold.GrabLocal);
                if (nowFree) { hold.RelRot = Quaternion.Inverse(refRot) * t.rotation; hold.RelGrab = Quaternion.Inverse(hand.rotation) * (gp - hand.position); } // from here the hand's rotation
                else hold.Offset = gp - hand.position; // from here upright (it turns level at once, or eased by the smoothing)
                if (!hold.Laser) hold.Push = 0f; // (the new offset holds the push already)
            }
            // client wave 15 (w15grab), point 5: the holding hand's stick pushes / pulls it, whatever the grab (its other jobs wait: VR/LokiInput.cs)
            var st = h.In.GrabStick;
            float push = Mathf.Abs(st.y) > 0.2f && Mathf.Abs(st.y) > Mathf.Abs(st.x) ? st.y * GrabPushSpeed * dt : 0f;
            if (push != 0f) W15StickMoves++;
            Vector3 at; float lever;
            if (hold.Laser)
            {
                if (push != 0f) hold.Dist = Mathf.Clamp(hold.Dist + push, GrabMinDist, GrabMaxDist);
                at = rig.PointerRayNow().GetPoint(hold.Dist); lever = hold.Dist; // (the laser as it leaves the hand now: Ray's direction is normalised)
            }
            else
            {
                at = W15Pushed(hold, nowFree ? hand.position + hand.rotation * hold.RelGrab : hand.position + hold.Offset, push);
                lever = (at - hand.position).magnitude;
            }
            // client wave 15, points 8 and 11: smoothed; upright = facing the player while held (LokiGame.W15Grab.cs)
            W15Place(hold, at, nowFree ? refRot * hold.RelRot : (Quaternion?)null, lever, dt);
            W24GuardHeld(t); // client wave 24 (w24): the login panel and the keyboard stop at the ground's margin while they are dragged (here, so that the edge's light is drawn where the panel stands)
        }

        /// <summary>Puts the panel's grab point at <paramref name="at"/>: turned as given, or upright (client wave 14, point 16: facing the point
        /// behind the player, turned by <paramref name="yawOffset"/>: LokiGame.W14Ui.cs; a few rounds: turning it moves the grab point).</summary>
        private void W13Put(Transform t, Vector3 grabLocal, Vector3 at, Quaternion? rot, float yawOffset)
        {
            for (int k = 0; k < 5; k++)
            {
                t.rotation = rot ?? W14HeldUpright(t.position, yawOffset, t.rotation);
                t.position += at - t.TransformPoint(grabLocal);
            }
        }

        /// <summary>Upright and facing the head from <paramref name="pos"/>: a turn about the vertical only (no pitch, no roll).</summary>
        private Quaternion W13Upright(Vector3 pos, Quaternion keep)
        {
            var d = pos - _boot.Rig.Head.transform.position; d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) { var f = keep * Vector3.forward; f.y = 0f; if (f.sqrMagnitude < 1e-6f) f = Vector3.forward; d = f; }
            return Quaternion.LookRotation(d.normalized, Vector3.up);
        }

        private void W13GrabRelease(int i, string why)
        {
            var hold = _grabHold[i]; _grabHold[i] = null;
            if (hold == null) return;
            W13GrabReleases++;
            W14FaceAtLetGo(hold.Panel, why); // client wave 14 (w14ui), point 16: an upright panel turns to face the eyes, before its owner keeps its place
            // client wave 23 (w23inv): the pulled-out inventory was carried upright although "Panel tilt" is Free (W14FaceAtLetGo leaves a
            // free panel as it is): it faces the eyes as an upright panel does
            if (why == "let go" && hold.BagUpright && hold.Panel != null && W13TiltFree(hold.Panel)) hold.Panel.rotation = W13Upright(hold.Panel.position, hold.Panel.rotation);
            // client wave 24 (w24): the keyboard, let go upright, faces the eyes with the tilt it was taken with (W14FaceAtLetGo made it vertical)
            if (why == "let go" && hold.KeepPitch != 0f && hold.Panel != null && !W13TiltFree(hold.Panel)) hold.Panel.rotation = W13Upright(hold.Panel.position, hold.Panel.rotation) * Quaternion.Euler(hold.KeepPitch, 0f, 0f);
            bool eases = why == "let go" && W15EaseOutAtLetGo(hold); // client wave 15 (w15grab), point 28: too near the eyes: it eases out first (its owner is told then)
            if (hold.Grabbable != null)
            {
                hold.Grabbable.Held = false;
                if (why == "let go" && !eases) { try { hold.Grabbable.Released?.Invoke(); } catch (Exception e) { Debug.LogException(e); } } // (a board keeps its place: UI/WristMenu.Board.cs)
            }
            W24Released(hold.Panel, why); // client wave 24 (w24): the login panel's and the keyboard's places are kept (LokiGame.W24Login.cs)
            if (why == "let go" && !eases) W13SaveMenuPlace(hold.Panel);
            W13Shake.Reset(); // (resize mode's / Reposition's detector: a shake that was the panel's does not carry over)
            var t = hold.Panel; var head = _boot.Rig.Head.transform;
            if (t != null) Debug.Log($"[Grab] '{t.name}' {why}: {(t.position - head.position).magnitude * 100f:0} cm from the eyes, pitch {W13Pitch(t):0.0}°, roll {W13Roll(t):0.0}°");
        }

        /// <summary>Item 6: persists a moved menu window's place relative to the player's vision as soon as it is let go.</summary>
        private void W13SaveMenuPlace(Transform panel)
        {
            if (panel == null || _boot == null || _boot.Rig == null || _boot.Rig.Head == null) return;
            var cam = _boot.Rig.Head;
            if (panel.GetComponent<ListWindow>() != null) PanelPlacing.SaveMenuPlace("ListWindow:" + panel.name, panel, cam);
            else if (panel.GetComponent<CodexBook>() is CodexBook cb) PanelPlacing.SaveMenuPlace(cb.PlaceKey, panel, cam);
            else if (panel.GetComponent<WorldMapWindow>() != null) PanelPlacing.SaveMenuPlace("WorldMapWindow", panel, cam);
            else if (panel.GetComponent<GuideWindow>() != null) PanelPlacing.SaveMenuPlace("GuideWindow", panel, cam);
            else if (panel.GetComponent<MenuBoard>() is MenuBoard mb) PanelPlacing.SaveMenuPlace(mb.PlaceKey, panel, cam, panel.localScale.x / MenuBoard.MetresPerPx);
        }

        /// <summary>A panel's pitch (its face from vertical) and roll (its up from the vertical plane), in degrees.</summary>
        public static float W13Pitch(Transform t) => Mathf.Asin(Mathf.Clamp(t.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        public static float W13Roll(Transform t) { var r = t.right; return Mathf.Asin(Mathf.Clamp(r.y, -1f, 1f)) * Mathf.Rad2Deg; }
    }
}
