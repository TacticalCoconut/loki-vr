using Loki.Game;
using Loki.Net;
using Loki.Sprites;
using Loki.UI;
using Loki.VR;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace Loki
{
    /// <summary>
    /// Client wave 12 (w12cast), parts B + C: a new way to cast what is on the quick slots, two schemes to compare (design note, 2026-10-06:
    /// the old way is "prone to misclicks and bad decision-making on the fly. It needs to be something that the player can
    /// intuitively and instinctively understand and use" … "Build both as settings to compare"). Nothing existing is removed: the
    /// skill wheels, the forearm quick bar, the menus' Use buttons and the slot picker work as before, and the new schemes read
    /// the SAME hotkey slots (Game/Hotkeys.cs: 2 bars x 4 rows x 9).
    ///   The setting   Settings → Controls "Casting gesture: off / pad / flick / wand" (VR/Comfort.cs CastGesture, default pad).
    ///   The start     pad and flick: the grip squeezed WHILE the index finger points (off the trigger; a tracked hand: the
    ///                 pointing pose) (VR/LokiInput.cs CastFrame). Client wave 15 (w15cast), point 32: it opens in that very frame (wave
    ///                 12 held it 0.1 s first); a finger back on the trigger within 0.1 s withdraws it, nothing taken. Never in resize mode (client wave 13: the grip is the
    ///                 camera's then), never while a menu is open (W13CastingAllowed). Client wave 14 (w14cast), point 5: a LIGHT
    ///                 squeeze is enough (Settings → Controls "Cast grip"; VR/LokiInput.cs CastGripOn: the resize threshold no longer
    ///                 gates casting).
    ///   Pad           a 3 x 3 pad of one hotkey row (UI/CastPad.cs; client wave 13: it opens around the fingertip, its middle cell
    ///                 on it, and stays fixed in the room while open; wave 12's head-locked place is gone). The row: the
    ///                 pointing hand palm down = row 1 of the shown bar, palm up = row 2; the other hand palm down = row 3,
    ///                 palm up = row 4 (the palm's state when the gesture opens; it becomes "up" when the palm's normal is more
    ///                 than <see cref="PalmUpOn"/> degrees from straight down and "down" again under <see cref="PalmUpOff"/>).
    ///                 The fingertip (<see cref="TipAhead"/> ahead of the controller's grip pose; a tracked hand: the index
    ///                 tip) in a cell marks it with a tick in that hand; letting the grip go takes it; let go beside the pad:
    ///                 nothing. An empty slot takes nothing. Client wave 18 (w18flick), point 16: with "Pad rows: Slap" (Palm, the above, is the
    ///                 default) the rows are by hand, the pointing hand row 1, the other row 2, and a slap of the pad's own hand switches
    ///                 to row 3 / 4 (LokiGame.W18Flick.cs); point 15: the pad opens at the fingertip + that hand's offset set in Reposition.
    ///   Flick         a two-stroke flick from where the fingertip is when the gesture opens, in the plane facing the player
    ///                 (Game/FlickRecognizer.cs): the first stroke picks one of the 8 rows, letting go takes its slot 5, a
    ///                 second stroke one of the other eight. Client wave 14 (w14cast), point 5: the icons from the first frame, opaque,
    ///                 every choice shown, empty ones too (UI/FlickHud.cs); wave 12's hesitation is gone. Client wave 16 (w16cast), design note:
    ///                 "Remove the center slot on the flick menu. That way the user can cancel a cast": letting go at the middle (before a
    ///                 first stroke, or at a row's middle without a second stroke) takes nothing; the second stroke takes slots 1-4, 6-9.
    ///   Spell in hand taking a skill arms it through the game's own armed-skill flow (LokiGame.cs UseSkill / CastArmed: the
    ///                 pointing hand's trigger casts at the laser's unit or cell, else at what the look has; B drops it; a new
    ///                 choice replaces it; the walk into range and the refusals are that flow's). It shows over the hand that
    ///                 chose it with the world's preview (UI/SpellInHand.cs). "Cast when taken" (Comfort.CastOnTake, on): a
    ///                 skill that needs no target is cast as it is taken, and a target skill when a unit is already under the
    ///                 laser or locked by the look; off: every skill waits in the hand for the trigger.
    ///                 An item on a slot is used as it is taken (as the quick bar does).
    ///   Item buttons  REMOVED in client wave 14 (w14cast; the design's answer 5: "Just remove the item button shortcut altogether"): wave
    ///                 12's grip + a face button / the stick click, their window (the Quick page's "Item buttons…") and wave 13's
    ///                 crosses low in the view are gone; a grip no longer changes what the face buttons do.
    /// Not while the character cannot act, a dialog or the keyboard is open, or Horde's arena is shown (W8HordeBlocksWorld).
    /// Hooks in LokiGame.cs: InitW12Cast (after the wheels), UpdateW12Cast (Update), UseSkill / ArmSkill / CastArmed / Disarm, the
    /// B button's CancelArmed.
    /// </summary>
    public sealed partial class LokiGame
    {
        public const float PalmUpOn = 80f, PalmUpOff = 55f, TipAhead = 0.10f;

        private sealed class CastHand
        {
            public bool Left; public LokiInput.Hand In; public Transform T;
            public bool Open; public int Scheme; public int Row; public int Cell = -1; public bool PalmUp;
            /// <summary>B called this hand's gesture off (or the casting gesture was changed under it): it stays closed until the grip
            /// is let go.</summary>
            public bool Cancelled;
            /// <summary>Client wave 13 (w13resize): a menu opened (or a mode started) while this hand's gesture was open: it was closed with
            /// nothing taken and does not open again until the grip is let go.</summary>
            public bool MenuBlocked;
            public readonly FlickRecognizer Flick = new FlickRecognizer(); public Vector3 Origin, Right, Up; public int Steps;
            public CastPad Pad; public FlickHud Hud; public Transform Dot;
        }
        private CastHand[] _w12; // 0 the left hand, 1 the right
        public SpellInHand W12Spell { get; private set; }
        public CastSkillTip W12SkillTip { get; private set; }
        private int _w12TipSource = -1, _w12TipSlot = -1, _w12TipId = -1;
        private bool _w12TipItem;
        private float _w12TipHoldT;
        private Progression.SkillEntry _w12Held; private bool _w12HeldLeft;
        /// <summary>Slots taken with the gesture, of those: cast at once; gestures opened (smoke, log).</summary>
        public int W12Takes { get; private set; }
        public int W12CastOnTake { get; private set; }
        public int W12Opens { get; private set; }
        /// <summary>The last slot taken: row (0-7) and slot (0-8); -1 before any.</summary>
        public (int row, int slot) W12LastTaken { get; private set; } = (-1, -1);
        /// <summary>Client wave 18 (w18tut): slots chosen by a gesture (the pad, the flick, the wand's strokes and its wheel's cells), an empty one
        /// too: what the interactive tutorial waits for (LokiGame.W18Tutorial.cs).</summary>
        public int W12Chosen { get; private set; }
        public CastPad W12Pad(bool left) => _w12 != null ? _w12[left ? 0 : 1].Pad : null;
        public FlickHud W12FlickHud(bool left) => _w12 != null ? _w12[left ? 0 : 1].Hud : null;
        public bool W12Open(bool left) => _w12 != null && _w12[left ? 0 : 1].Open;
        public bool W12PalmUp(bool left) => _w12 != null && _w12[left ? 0 : 1].PalmUp;
        public int W12MarkedCell(bool left) => _w12 != null ? _w12[left ? 0 : 1].Cell : -1;
        public FlickRecognizer W12Flick(bool left) => _w12 != null ? _w12[left ? 0 : 1].Flick : null;
        /// <summary>A skill taken with the gesture is in a hand now (armed and shown there).</summary>
        public bool W12SpellHeld => _armed != null && ReferenceEquals(_armed, _w12Held);

        private void InitW12Cast(LokiBootstrap boot)
        {
            var rig = boot.Rig; if (rig == null || Hud == null || Hotkeys == null) return;
            _w12 = new CastHand[2];
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0;
                var h = new CastHand { Left = left, In = left ? rig.Input.Left : rig.Input.Right, T = left ? rig.LeftHand : rig.RightHand };
                h.Pad = CastPad.Create(rig.transform, !left, Hotkeys, Inventory, Prog, Quick); // (client wave 13: it opens at the hand and stays in the room)
                h.Hud = FlickHud.Create(rig.transform, left ? "FlickLeft" : "FlickRight", Hotkeys, Inventory, Prog);
                var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere); dot.name = left ? "CastTipLeft" : "CastTipRight";
                Destroy(dot.GetComponent<Collider>()); dot.transform.SetParent(rig.transform, false); dot.transform.localScale = Vector3.one * 0.014f;
                var mr = dot.GetComponent<Renderer>(); mr.material = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.9f, 0.5f, 0.95f) };
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                dot.SetActive(false); h.Dot = dot.transform;
                _w12[i] = h;
            }
            W12Spell = SpellInHand.Create(rig.transform, rig.Head);
            W12SkillTip = CastSkillTip.Create(rig.transform, rig.Head);
            InitW12Wand(boot); // the wand, a variant of the flick (LokiGame.W12Wand.cs)
            InitW13Resize(boot); // client wave 13: the overhead menu and resize mode (LokiGame.W13Resize.cs)
            InitW18Flick(boot); // client wave 18 (w18flick), points 16 and 15: the pad's slap, the pads' places in Reposition (LokiGame.W18Flick.cs)
        }

        /// <summary>The pointing fingertip: a tracked hand's index tip, else a point ahead of the controller's grip pose.</summary>
        private Vector3 W12Tip(CastHand h)
        {
            var rig = _boot.Rig;
            if (h.In.JointMode && rig.Hands != null)
            {
                var side = h.Left ? rig.Hands.Left : rig.Hands.Right;
                if (side.TryJoint(XRHandJointID.IndexTip, out var p)) return rig.transform.TransformPoint(p.position);
            }
            return h.T.position + h.T.forward * TipAhead;
        }

        /// <summary>Out of the palm. A tracked hand: the palm joint's -Y (its +Y is the back of the hand: VR/HandJoints.cs). A
        /// controller: the grip pose's X axis: out of the palm on the left hand (+X), into it on the right (-X is out of it), by
        /// OpenXR's definition of the grip pose (from the specification as remembered: no copy of it on this PC to cite a line;
        /// a runtime that differs would swap "palm down" and "palm up" on that hand: for the headset check).</summary>
        private static Vector3 W12PalmNormal(CastHand h) => h.In.JointMode ? -h.T.up : h.Left ? h.T.right : -h.T.right;

        private void UpdateW12Cast()
        {
            var rig = _boot.Rig;
            if (rig == null || _w12 == null || Hud == null) return;
            UpdateW13Grab(); // client wave 13 (w13grab), items 19 and 20: panels taken by their edge, the backpack (LokiGame.W13Grab.cs): first, so a held panel counts below
            bool world = Session != null && Session.State == RoSession.Stage.InGame && !Player.Dead && Entities.Self != null && Hotkeys.Loaded && !W8HordeBlocksWorld;
            // client wave 13 (w13resize): the ONE rule for the new casting (LokiGame.W13Resize.cs W13CastingAllowed): no menu open (the
            // NPC dialog and the keyboard among them), no resize / reposition mode, no holster placement
            bool ui = W13CastingAllowed;
            int scheme = Comfort.CastGesture;
            LokiInput.CastGestureEnabled = world && ui && (scheme == 1 || scheme == 2);
            LokiInput.CastGestureScheme = scheme;
            LokiInput.FlickGripThreshold = Comfort.FlickGripThreshold;
            LokiInput.CastGripLevel = Comfort.CastGrip; // client wave 14 (w14cast), point 5: "Cast grip" (light by default)
            for (int i = 0; i < 2; i++)
            {
                var h = _w12[i];
                h.In = h.Left ? rig.Input.Left : rig.Input.Right;
                // the palm: up or down, with a wide margin between (decided before the gesture opens, read when it does)
                float fromDown = Vector3.Angle(W12PalmNormal(h), Vector3.down);
                h.PalmUp = h.PalmUp ? fromDown > PalmUpOff : fromDown > PalmUpOn;
                bool flickHandOk = scheme != 2 || Comfort.FlickHandMode == 2 || (Comfort.FlickHandMode == 0 ? h.In.IsMain : !h.In.IsMain);
                bool held = h.In.CastHeld && LokiInput.CastGestureEnabled && flickHandOk;
                if (!held) h.Cancelled = false;
                if (!h.In.PlainGrip) h.MenuBlocked = false;
                // client wave 13 (w13resize): the casting gesture was changed under an open gesture (Settings, the overhead menu): called off
                if (h.Open && h.Scheme != scheme) { h.Cancelled = true; W12CloseGesture(h, false); Debug.Log("[Cast] the casting gesture changed: the open one is called off, nothing taken"); }
                // the brief's seventh item: a menu opened (or a mode started) while it was open: closed, nothing taken, not again until the grip is let go
                if (h.Open && !held && world && !ui) { h.MenuBlocked = true; Debug.Log($"[Cast] {(h.Left ? "left" : "right")} hand's gesture called off: {W13CastingBlocker ?? "?"} (nothing taken)"); }
                if (held && !h.Open && !h.Cancelled && !h.MenuBlocked) W12OpenGesture(h, scheme);
                else if (!held && h.Open)
                {
                    // client wave 15 (w15cast), point 32: the gesture opens at once; a finger back on the trigger within 0.1 s withdraws it: nothing taken
                    bool blip = h.In.CastWithdrawn; if (blip) Debug.Log($"[Cast] {(h.Left ? "left" : "right")} hand: the finger back on the trigger at once: withdrawn, nothing taken");
                    W12CloseGesture(h, world && ui && !blip);
                }
                if (h.Open) W12Track(h);
            }
            UpdateW12Wand(); // (before the spell's preview: the wand's ray is what it shows while a hand holds the wand)
            UpdateW24WandHint(); // client wave 24 (w24), item 10: the tutorial's "put the wand away to press buttons" (LokiGame.W24Tut.cs)
            // client wave 13 (w13resize): the wand held in the pointing hand while the casting waits (a menu is open): its trigger presses
            // the menu's buttons and does nothing in the world (VR/DioramaRig.cs)
            rig.MenuClicksOnly = _wandHand != null && _wandHand.In.IsMain && !ui;
            UpdateW13Resize(); // client wave 13: the overhead menu, resize mode's mark and its shake (LokiGame.W13Resize.cs)
            UpdateW12Spell();
            UpdateW12CastTip(world);
        }

        private void W12OpenGesture(CastHand h, int scheme)
        {
            h.Open = true; h.Scheme = scheme; h.Cell = -1; W12Opens++;
            if (scheme == 1)
            {
                h.Row = W18FPadOpened(h); // client wave 18 (w18flick), point 16: "Pad rows": Palm (the default): wave 12's palm rows; Slap: by hand (pointing 1, other 2)
                // client wave 13 (w13resize): around the fingertip, facing the eyes, and it stays there (UI/CastPad.cs OpenAt); client wave 18
                // (w18flick), point 15: at the fingertip + this hand's offset from Reposition (zero: on the fingertip)
                int hi = h.Left ? 0 : 1; var tip = W12Tip(h); h.Pad.OpenAt(h.Row, tip, _boot.Rig.Head.transform.position, Comfort.PadOffset[hi], Comfort.PadTurn[hi]);
                var eyes = _boot.Rig.Head.transform; var lt = eyes.InverseTransformPoint(tip);
                Debug.Log($"[Cast] pad: {(h.Left ? "left" : "right")} hand ({(h.In.IsMain ? "pointing" : "walking")}), palm {(h.PalmUp ? "up" : "down")}: bar {h.Row / Hotkeys.RowsPerTab + 1} row {h.Row % Hotkeys.RowsPerTab + 1}, opened at the fingertip {lt.x * 100f:0} / {lt.y * 100f:0} / {lt.z * 100f:0} cm (right / up / ahead of the eyes), cells {h.Pad.CellMetres * 100f:0.0} cm");
            }
            else
            {
                // the strokes' plane: facing the player, upright: right of the head (level), straight up
                var head = _boot.Rig.Head.transform;
                var right = head.right; right.y = 0f; if (right.sqrMagnitude < 1e-4f) right = Vector3.right; right.Normalize();
                h.Origin = W12Tip(h); h.Right = right; h.Up = Vector3.up; h.Steps = 0;
                h.Flick.Begin(Comfort.FlickFollow); // (client wave 18, w18flick, point 7: "Flick follow")
                // client wave 18 (w18flick), point 32: "Flick shown: In front of you": the picture head-locked in front of the eyes (the strokes are
                // still measured from here); "At your hand" (the default): standing where the gesture began
                if (Comfort.FlickFront) h.Hud.BeginFront(head); else h.Hud.Begin(h.Origin, Quaternion.LookRotation(Vector3.Cross(right, Vector3.up), Vector3.up));
                Debug.Log($"[Cast] flick: {(h.Left ? "left" : "right")} hand{(Comfort.FlickFront ? " (shown in front of you)" : "")}");
            }
            Haptics.Pulse(Haptics.Kind.Casting, h.In.IsMain, 0.15f, 0.02f);
        }

        private void W12Track(CastHand h)
        {
            var tip = W12Tip(h);
            if (h.Scheme == 1)
            {
                int c = W18FSlapTick(h, tip) ? -1 : h.Pad.CellAt(tip, h.Cell); // client wave 18 (w18flick), point 16: a slap switches the row; no cell while its push goes on
                if (c != h.Cell) { h.Cell = c; h.Pad.Mark(c); if (c >= 0) Haptics.Pulse(Haptics.Kind.Casting, h.In.IsMain, 0.12f, 0.015f); }
                h.Dot.position = tip; if (!h.Dot.gameObject.activeSelf) h.Dot.gameObject.SetActive(true);
            }
            else
            {
                var d = tip - h.Origin; var p = new Vector2(Vector3.Dot(d, h.Right), Vector3.Dot(d, h.Up));
                h.Flick.Update(p, Time.unscaledDeltaTime);
                if (h.Flick.Steps != h.Steps) { h.Steps = h.Flick.Steps; Haptics.Pulse(Haptics.Kind.Casting, h.In.IsMain, 0.12f, 0.015f); }
                h.Hud.Draw(h.Flick.Phase, h.Flick.Dir1, h.Flick.Dir2, h.Flick.Corner, p);
            }
        }

        /// <summary>The grip was let go (take = the game still allows it): the marked slot is taken; nothing marked: nothing.</summary>
        private void W12CloseGesture(CastHand h, bool take)
        {
            h.Open = false;
            if (h.Dot.gameObject.activeSelf) h.Dot.gameObject.SetActive(false);
            if (h.Scheme == 1)
            {
                int cell = h.Cell; h.Cell = -1; h.Pad.Hide(); W18FPadClosed(h); // (client wave 18, w18flick: the hand's speeds, logged)
                if (take && cell >= 0) W12Take(h.Row, cell, h.Left); else Debug.Log($"[Cast] pad closed, nothing taken ({(take ? "let go beside the pad" : "not allowed now")})");
            }
            else
            {
                h.Hud.Hide();
                if (take && h.Flick.TryResult(out int row, out int slot)) W12Take(row, slot, h.Left);
                else Debug.Log($"[Cast] flick ended, nothing taken ({(!take ? "not allowed now" : h.Flick.Phase == 1 ? "let go at a row's middle: cancelled" : "let go at the start: cancelled")})"); // (client wave 16: the middle cancels)
            }
        }

        /// <summary>B: an open gesture is called off (nothing taken). True when there was one.</summary>
        private bool W12CancelGestures()
        {
            if (W12WandPlacing) { W12WandPlace(false); return true; }
            bool any = false;
            if (_w12 != null) foreach (var h in _w12) if (h.Open) { any = true; h.Cancelled = true; W12CloseGesture(h, false); }
            return any;
        }

        /// <summary>Takes a slot of the hotkey bar with a hand: an item is used; a skill goes into that hand, or is cast at once
        /// (Comfort.CastOnTake) when it needs no target or a unit is already under the laser / locked.</summary>
        private void W12Take(int row, int slot, bool leftHand) => W12Take(row, slot, leftHand, Comfort.CastOnTake, Comfort.VisionAimSpells);

        /// <summary>now: a skill that can be cast at once is (no target needed; a unit aimed at or locked); groundNow: a ground skill
        /// too, at the cell aimed at (the wand's look aim and its quick buttons).</summary>
        private void W12Take(int row, int slot, bool leftHand, bool now, bool groundNow)
        {
            var hk = Hotkeys.At(row, slot);
            W12LastTaken = (row, slot); W12Chosen++; // (client wave 18, w18tut: every slot chosen, an empty one too: the tutorial's casting steps)
            if (hk.Empty) { Debug.Log($"[Cast] bar {row / Hotkeys.RowsPerTab + 1} row {row % Hotkeys.RowsPerTab + 1} slot {slot + 1} is empty: nothing taken"); return; }
            W12Takes++;
            Debug.Log($"[Cast] took bar {row / Hotkeys.RowsPerTab + 1} row {row % Hotkeys.RowsPerTab + 1} slot {slot + 1}: {(hk.IsSkill ? "skill" : "item")} {hk.Id} with the {(leftHand ? "left" : "right")} hand");
            Progression.SkillEntry k = null;
            if (!hk.IsSkill || !Prog.Skills.TryGetValue(hk.Id, out k) || k.Level <= 0 || k.Passive) { Hud.Wrist.QuickUseAt(row, slot); return; } // an item: used; a skill not known any more: said so
            W12TakeSkill(k, leftHand, now, groundNow);
        }

        /// <summary>The wand's own slots hold skill ids: the same taking, by id.</summary>
        private void W12TakeSkillId(int skillId, bool leftHand, bool now, bool groundNow)
        {
            if (!Prog.Skills.TryGetValue(skillId, out var k) || k.Level <= 0 || k.Passive) { Hud.Wrist.Toast(Lang.T("You don't have that skill any more.")); return; }
            W12Takes++;
            W12TakeSkill(k, leftHand, now, groundNow);
        }

        private void W12TakeSkill(Progression.SkillEntry k, bool leftHand, bool now, bool groundNow)
        {
            if (k.SelfCast && now) { W12CastOnTake++; UseSkill(k); return; } // no target needed: cast (the existing self-cast)
            ArmSkill(k);
            if (!ReferenceEquals(_armed, k)) return; // refused (cooling down: said by the armed flow)
            _w12Held = k; _w12HeldLeft = leftHand;
            Hud.SetArmed(null); // the hand's own label takes the old one's place (UI/SpellInHand.cs)
            if (!now || k.SelfCast) return;
            bool lookAim = Comfort.VisionAimSpells || (_wandHand != null && W13AimLook);
            if (k.Ground)
            {
                if (!groundNow && !Comfort.VisionAimSpells) return;
                var c = W12AimedCell();
                if (c.HasValue) { W12CastOnTake++; CastArmed(null, c); } else if (lookAim) Hud.Notify(Lang.T("Look at a target, then pull the trigger."));
            }
            else
            {
                var aimed = W12AimedUnit();
                if (aimed != null) { W12CastOnTake++; CastArmed(aimed, null); } else if (lookAim) Hud.Notify(Lang.T("Look at a target, then pull the trigger."));
            }
        }

        /// <summary>The unit a cast would go to now: what the wand points at while a hand holds it, else what the laser is on; else
        /// what the look has locked (the armed flow's own order: LokiGame.cs OnTargetClicked / OnFreeTrigger).</summary>
        private Unit W12AimedUnit()
        {
            var rig = _boot.Rig; var locked = Target != null && Target.Locked != null ? Target.Locked.Unit : null;
            if (Comfort.VisionAimSpells) return W12Alive(locked) ? locked : W12Ray(rig.GazeRay.origin, rig.GazeRay.direction).unit;
            if (_wandHand != null) return W13AimLook ? locked : _wandUnit ?? locked; // ("Wand aim: look": the look alone)
            var hovered = rig.HoveredTarget != null ? rig.HoveredTarget.Unit : null;
            return W12Alive(hovered) ? hovered : locked; // (the laser also rests on the body of a monster that has just died: not a target for a cast when taken)
        }

        /// <summary>The ground cell a cast would go to now (the same order: the wand / the laser, else the look).</summary>
        private Vector2Int? W12AimedCell()
        {
            if (Comfort.VisionAimSpells) return Target?.GazeCell ?? W12Ray(_boot.Rig.GazeRay.origin, _boot.Rig.GazeRay.direction).cell;
            if (_wandHand != null) return W13AimLook ? Target?.GazeCell : _wandCell ?? Target?.GazeCell;
            return _boot.Rig.PointedCell ?? Target?.GazeCell;
        }

        /// <summary>The hand's label and the world's preview, while a skill taken by the gesture is armed.</summary>
        private void UpdateW12Spell()
        {
            if (W12Spell == null) return;
            if (!W12SpellHeld || W8HordeBlocksWorld)
            {
                if (W12Spell.Shown) W12Spell.Hide();
                // "Wand aim: look" or "Aim spells with vision": while a skill is armed or about to be taken, the world shows where it would go
                var cand = W13AimLook && !W8HordeBlocksWorld ? W12WandCandidate() : null;
                if (cand == null && Comfort.VisionAimSpells && !W8HordeBlocksWorld) cand = _armed;
                if (cand != null && !cand.SelfCast) W12Spell.Aim(_boot.Map, W12AimedUnit(), W12AimedCell(), cand.Ground ? GroundAreas.HalfSide(cand.Id, Prog.CastLevel(cand)) : 0, cand.Ground);
                else W12Spell.Aim(null, null, null, 0, false);
                return;
            }
            var rig = _boot.Rig; var k = _armed;
            var hand = _wandHand != null && W12Wand != null ? W12Wand.Tip : _w12HeldLeft ? rig.LeftHand : rig.RightHand; // (the wand: the skill sits on its tip)
            int half = k.Ground ? GroundAreas.HalfSide(k.Id, _armedLevel) : 0;
            bool sky = _wandHand != null && _wandSky; // the wand points at the sky: a pull would put the skill away (LokiGame.W12Wand.cs)
            if (!W12Spell.Shown || W12Spell.Hand != hand || W12Spell.NameShown != W12SpellName(k))
            {
                string hint = k.SelfCast ? Lang.T("trigger: cast on yourself · B: cancel")
                    : k.Ground ? (half >= 0 ? Lang.F("trigger: cast on the square ({0} x {0} cells) · B: cancel", 2 * half + 1) : Lang.T("trigger: cast at the marked cell (its area is not known here) · B: cancel"))
                    : k.Support ? Lang.T("trigger: cast on the ringed target, or on yourself · B: cancel") : Lang.T("trigger: cast on the ringed target · B: cancel");
                W12Spell.Show(hand, WristMenu.SkillIcon(k.Info), W12SpellName(k), hint);
            }
            // what the trigger would hit: the laser's (the wand's) unit or cell first, else the look's (as OnTargetClicked / OnFreeTrigger choose)
            W12Spell.SetCancel(sky, Lang.T("the wand points at the sky: the trigger cancels the skill"));
            if (sky) { W12Spell.Aim(null, null, null, 0, false); return; }
            var unit = W12AimedUnit();
            var cell = W12AimedCell();
            W12Spell.Aim(_boot.Map, k.SelfCast ? null : unit, cell, half, k.Ground);
        }
        private string W12SpellName(Progression.SkillEntry k) => Lang.F("{0} Lv {1}", k.Info.Name, _armedLevel);

        /// <summary>
        /// Tracks whichever skill/item slot is held or hovered across all casting widgets
        /// (Wand Wheel, Wand Strokes, Cast Pad, Cast Flick, Trigger-Hold Radial Skill Wheels,
        /// Forearm Quick Bar, and Top Quick Use Bubbles) and opens a non-obstructing info tab
        /// after 2.0 seconds of continuous hover.
        /// </summary>
        private void UpdateW12CastTip(bool world)
        {
            if (W12SkillTip == null) return;
            var rig = _boot.Rig;
            if (!world || rig == null || Hotkeys == null)
            {
                _w12TipSource = -1; _w12TipHoldT = 0f;
                if (W12SkillTip.Shown) W12SkillTip.Hide();
                return;
            }

            int src = -1, slotKey = -1, id = 0;
            bool isItem = false;
            Vector3 pos = Vector3.zero;
            Quaternion rot = Quaternion.identity;

            // 1. Wand Wheel or Wand Strokes
            if (_wandDrawing && _wandHand != null)
            {
                if (_wandWheelOn && W12Wheel != null && W12Wheel.Shown)
                {
                    if (_wheelRegion >= WandWheelGeo.Cell)
                    {
                        int r = _wheelTab * Hotkeys.RowsPerTab + _wheelSub;
                        int s = _wheelRegion - WandWheelGeo.Cell;
                        var hk = Hotkeys.At(r, s);
                        if (!hk.Empty) { src = 1; slotKey = r * 16 + s; id = hk.Id; isItem = !hk.IsSkill; }
                    }
                    else if (_wheelRegion >= 0 && _wheelRegion < WandWheelGeo.Outer)
                    {
                        int qid = W12WandSlots.quick[_wheelRegion];
                        if (qid > 0) { src = 1; slotKey = 100 + _wheelRegion; id = qid; isItem = W12WandSlots.quickItem[_wheelRegion]; }
                    }
                    if (src == 1)
                    {
                        var wt = W12Wheel.transform;
                        float sc = W12Wheel.Front ? WandWheel.FrontScale * W23WheelSize : _wheelSize;
                        float leftRadius = W12Wheel.SubShown && !W12Wheel.InPlace && W12Wheel.SubDir == 3
                            ? (WandWheelGeo.SubDistance + WandWheelGeo.SubEnd) * sc
                            : 0.145f * sc;
                        pos = wt.position - wt.right * (leftRadius + 0.088f);
                        rot = wt.rotation;
                    }
                }
                else if (!_wandWheelOn && _wandHand.Hud != null && _wandHand.Hud.Shown && _wandHand.Flick.TryResult(out int fRow, out int fSlot))
                {
                    var hk = Hotkeys.At(fRow, fSlot);
                    if (!hk.Empty)
                    {
                        src = 2; slotKey = fRow * 16 + fSlot; id = hk.Id; isItem = !hk.IsSkill;
                        var ht = _wandHand.Hud.transform;
                        pos = ht.position - ht.right * 0.22f;
                        rot = ht.rotation;
                    }
                }
            }

            // 2. Cast Pad or Cast Flick (either hand)
            if (src < 0 && _w12 != null)
            {
                for (int i = 0; i < 2; i++)
                {
                    var h = _w12[i];
                    if (!h.Open) continue;
                    if (h.Scheme == 1 && h.Pad != null && h.Pad.Shown && h.Cell >= 0)
                    {
                        var hk = Hotkeys.At(h.Row, h.Cell);
                        if (!hk.Empty)
                        {
                            src = 3 + i; slotKey = h.Row * 16 + h.Cell; id = hk.Id; isItem = !hk.IsSkill;
                            var pt = h.Pad.transform;
                            pos = pt.position - pt.right * (h.Pad.CellMetres * 1.65f + 0.088f);
                            rot = pt.rotation;
                            break;
                        }
                    }
                    else if (h.Scheme == 2 && h.Hud != null && h.Hud.Shown && h.Flick.TryResult(out int fRow, out int fSlot))
                    {
                        var hk = Hotkeys.At(fRow, fSlot);
                        if (!hk.Empty)
                        {
                            src = 5 + i; slotKey = fRow * 16 + fSlot; id = hk.Id; isItem = !hk.IsSkill;
                            var ht = h.Hud.transform;
                            float dist = h.Hud.Front ? 0.175f : 0.22f;
                            pos = ht.position - ht.right * dist;
                            rot = ht.rotation;
                            break;
                        }
                    }
                }
            }

            // 3. Trigger-Hold Radial Skill Wheels (WheelLeft / WheelRight)
            if (src < 0 && WheelsOpen)
            {
                SkillWheel activeW = _wheelPick != null && _wheelPick.Shown && _wheelPick.Selected >= 0 ? _wheelPick
                    : WheelRight != null && WheelRight.Shown && WheelRight.Selected >= 0 ? WheelRight
                    : WheelLeft != null && WheelLeft.Shown && WheelLeft.Selected >= 0 ? WheelLeft : null;
                if (activeW != null)
                {
                    var hk = Hotkeys.At(activeW.Row, activeW.Selected);
                    if (!hk.Empty)
                    {
                        src = activeW == WheelLeft ? 7 : 8;
                        slotKey = activeW.Row * 16 + activeW.Selected;
                        id = hk.Id;
                        isItem = !hk.IsSkill;
                        var wt = activeW.transform;
                        var anchor = wt.position;
                        if (activeW == WheelRight && WheelLeft != null && WheelLeft.Shown)
                        {
                            var dl = WheelLeft.transform.position - wt.position;
                            if (Vector3.Dot(dl, -wt.right) > 0f && dl.magnitude < 0.32f) anchor = WheelLeft.transform.position;
                        }
                        pos = anchor - wt.right * 0.155f;
                        rot = wt.rotation;
                    }
                }
            }

            // 4. Forearm Quick Bar ("the arm")
            if (src < 0 && Quick != null && Quick.Shown)
            {
                int qSlot = Quick.HoveredSlot;
                if (qSlot >= 0)
                {
                    var hk = Hotkeys.Quick(qSlot);
                    if (!hk.Empty)
                    {
                        src = 9;
                        slotKey = Hotkeys.CurrentRow * 16 + qSlot;
                        id = hk.Id;
                        isItem = !hk.IsSkill;
                        var qt = Quick.transform;
                        pos = qt.position - qt.right * 0.19f;
                        rot = qt.rotation;
                    }
                }
            }

            // 5. Top Quick Use Bubbles
            if (src < 0 && _w20 != null && W20Blocker == null)
            {
                var all = Loki.World.UserSettings.BubblesNow;
                var laserRay = new Ray(rig.PointerOrigin, rig.PointerDirection);
                var gazeRay = rig.GazeRay;
                for (int i = 0; i < BubbleCount; i++)
                {
                    if (!all[i].on) continue;
                    var (bid, bitem) = W20Holds(i);
                    if (bid <= 0) continue;
                    var bc = W20Centre(i);
                    float br = W20Radius(i);
                    bool hovered = _w20In[i, 0] || _w20In[i, 1]
                        || (rig.PointerDirection.sqrMagnitude > 1e-6f && W20RaySphere(laserRay, bc, br + 0.015f) >= 0f);
                    if (!hovered && all[i].shown != 0)
                    {
                        float dist = Vector3.Distance(gazeRay.origin, bc);
                        float own = Mathf.Atan2(br, Mathf.Max(dist, 1e-3f)) * Mathf.Rad2Deg;
                        float ang = Vector3.Angle(gazeRay.direction, bc - gazeRay.origin);
                        if (ang <= own + BubbleLookFull) hovered = true;
                    }
                    if (hovered)
                    {
                        src = 10 + i;
                        slotKey = i;
                        id = bid;
                        isItem = bitem;
                        var d = bc - rig.Head.transform.position;
                        if (d.sqrMagnitude < 1e-6f) d = rig.Head.transform.forward;
                        rot = Quaternion.LookRotation(d.normalized, Vector3.up);
                        pos = bc - (rot * Vector3.right) * (br + 0.088f);
                        break;
                    }
                }
            }

            if (src < 0 || id <= 0)
            {
                _w12TipSource = -1; _w12TipSlot = -1; _w12TipId = -1; _w12TipHoldT = 0f;
                if (W12SkillTip.Shown) W12SkillTip.Hide();
                return;
            }

            if (src == _w12TipSource && slotKey == _w12TipSlot && id == _w12TipId && isItem == _w12TipItem)
            {
                _w12TipHoldT += Time.unscaledDeltaTime;
            }
            else
            {
                _w12TipSource = src; _w12TipSlot = slotKey; _w12TipId = id; _w12TipItem = isItem; _w12TipHoldT = 0f;
                if (W12SkillTip.Shown) W12SkillTip.Hide();
            }

            if (_w12TipHoldT >= CastSkillTip.HoverDelaySeconds)
            {
                int job = Hud?.Wrist?.SkillJob != null ? Hud.Wrist.SkillJob() : (Session?.SelectedChar != null ? Session.SelectedChar.Job : 0);
                if (isItem) W12SkillTip.ShowItem(id, Inventory, pos, rot);
                else W12SkillTip.ShowSkill(id, Prog, job, pos, rot);
            }
        }
    }
}
