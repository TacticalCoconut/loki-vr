using Loki.Game;
using Loki.Net;
using Loki.Sprites;
using Loki.UI;
using Loki.VR;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 12 (w12cast): the WAND, a variant of the flick scheme (design note, 2026-10-06, 20:15: "a variant to the flick method that
    /// places a magic wand on your hip (placement customizable). This magic wand, when held, primes it so that when the trigger is
    /// held you start 'drawing'"). Settings → Controls "Casting gesture: wand". The object and its holster: VR/WandHolster.cs.
    ///   Taking    a hand within 12 cm of the holster buzzes lightly; squeezing its grip there takes the wand. Either hand.
    ///             Client wave 14 (w14cast), point 13 + the design's answer 10 ("rather than grip again with the hand at the holster, it's
    ///             either grip OR release again at the holster"): EQUIP / UNEQUIP: the wand stays in the hand when the grip is let go.
    ///             It goes back to the hip (0.25 s, a spell on its tip dropped with it) when that hand grips again at the holster, or
    ///             lets go of a grip at the holster after it had left it (more than <see cref="WandAwayMargin"/> beyond the reach: the
    ///             grip that took it, let go on the spot, keeps it). Elsewhere that hand's grip does nothing while it holds the wand
    ///             (no panel grab, no casting gesture: VR/LokiInput.cs WandHeld). The CUE (point 13: "a visual indication that my hand
    ///             is correctly positioned"): while a grip now would take the wand, or put it back, a ring of light stands round the
    ///             holster and the wand is drawn lit (VR/WandHolster.cs SetCue); a hand coming within reach buzzes as before.
    ///             Client wave 15 (w15cast), point 30 (design note: "click grip again with a little bit of force to let go of the wand and have it
    ///             automatically return to the holster"): a FIRM squeeze of the holding hand's grip ANYWHERE puts it back too (VR/LokiInput.cs
    ///             FirmGrip: firmer than every "Cast grip" level), with a short rumble. Only a NEW squeeze counts: the grip must have been
    ///             let go since the wand was taken (the grip that took it, squeezed on, keeps it). Point 2: while the wand is in a hand a
    ///             dark see-through disc marks the holster, drawn through the world and the panels (VR/WandHolster.cs SpotShown).
    ///   The wheel "Wand quick cast: strokes / both" (Comfort.WandQuick; the design's sketch and rules of 21:40,
    ///             design notes): holding the wand hand's trigger (over 0.6 of its travel, until under
    ///             0.35; a tracked hand: the pinch) opens the two-tier wheel where the tip is, facing the player; it stays there
    ///             (Game/WandWheelGeo.cs: the shape; UI/WandWheel.cs: the picture). The tip on one of the four INNER segments
    ///             lights it: letting the trigger go there takes that quick skill (the wand's own four slots, per character).
    ///             Client wave 14 (w14cast), point 14 + the design's answer 11 ("New replacing. Remember that this is meant to be a variant
    ///             of the current one"): "Wand rows: in place" (VR/Comfort.cs WandRows 0, the DEFAULT): touching an outer segment
    ///             REPLACES the main wheel with that row's wheel in the same place and the cursor goes back to its middle: the tip's
    ///             movement counts from where it is at that moment (Game/WandWheelGeo.cs NextInPlace); a cell is chosen from the
    ///             middle; letting go in the middle or outside takes nothing (no way back to the main wheel within one pull: pull
    ///             again). Client wave 16 (w16cast), point 3: the row's wheel opens on the FIRST wheel's middle and the cursor snaps
    ///             back to it, at the wand as in front of the eyes (wave 14 moved the picture at the wand under the tip): the cursor no
    ///             longer lines up with the tip; the tip's movement from that moment moves it. "Wand rows: beside" (1) is wave 12's, below:
    ///             The tip TOUCHING one of the four OUTER segments opens that row's sub-wheel beside the main wheel at once, the
    ///             main wheel staying open (up = row 1, right = row 2, down = row 3, left = row 4 of the shown hotbar); the tip
    ///             on one of its eight cells lights it: letting go there takes that slot (slots 1 to 8, clockwise from the top;
    ///             slot 9 of a row is not on the wheel). Only where the tip is when the trigger is let go counts: what it passed
    ///             over does nothing; let go on the white middle, in a gap, on an outer segment or outside everything: nothing is
    ///             taken. A tick in the hand at every segment or cell entered. The tip's path between two frames is followed
    ///             in steps of 4 mm, so a fast tip still touches the outer segment it crosses.
    ///             With a skill waiting on the tip the pull is most likely its cast: the wheel is then drawn only once the tip
    ///             has moved <see cref="WheelLateMove"/> or the pull has lasted <see cref="WheelLateSeconds"/> (it works the same).
    ///             Client wave 13 (w13view): "Wand wheel shown: in front of you" (the default) draws the same wheel in front of
    ///             the eyes, head-locked, with the tip's dot as the cursor (UI/WandWheel.cs OpenFront); the control above is
    ///             unchanged (the tip's movement in the plane at the tip). The plain strokes' hesitation icons stay at the hand.
    ///   Plain     "Wand quick cast: off / buttons": the trigger held draws the flick scheme's two strokes instead, by the same
    ///             recogniser (Game/FlickRecognizer.cs) and picture (UI/FlickHud.cs): all 8 rows x 9 slots (client wave 16: x 8, slots
    ///             1-4 and 6-9; letting go at a row's middle takes nothing: a pull that moved no stroke still casts the skill on the tip).
    ///   Casting   a trigger pull during which the tip touched nothing (the wheel: no segment; the strokes: no step) casts the
    ///             skill on the tip when the trigger is let go. B drops the spell.
    ///   The sky   (design note, 22:00: "if aiming a spell with the wand, casting it towards the sky cancels the spell cast selection")
    ///             such a cast pull let go while the wand points at the sky puts the skill away instead, as B does: nothing is
    ///             sent. The sky: the wand's line rises more than <see cref="SkyAngle"/> degrees above the horizon AND meets no
    ///             unit, no ground and no panel within 50 m. Every kind of skill, both aims. While the wand points there with a
    ///             skill on its tip, the tip shows a cancel mark and the world's preview is gone.
    ///   Buttons   "Wand quick cast: buttons / both": while the wand is in the POINTING hand, that hand's lower face button, its
    ///             stick click and its stick pushed up / down each cast their own quick skill at once (four button slots). They
    ///             do not do their own jobs meanwhile (sit / NPC next; turn in place); with a dialog open, or a slider / scrolling
    ///             text under the laser or the look using the stick, the buttons / the stick are left to those. Not offered on
    ///             the walking hand: none of its controls is idle (its stick walks, its click recentres, its buttons are the
    ///             menu and the minimap).
    ///   Aim       "Wand aim: wand / look" (Comfort.WandAimLook; design note, 20:50). wand: a cast goes to what the wand points at (the
    ///             pointing hand: its laser's ray; the other hand: along the hand), else to what the look has. look: the wand
    ///             is not aimed at all: a skill taken is cast AT ONCE at what the look has (the unit it locks; the gaze cell for
    ///             a ground skill; ourselves for a self skill); when the look has nothing (nothing locked, or it rests on a
    ///             panel; the wheel itself is no panel) the skill waits on the tip with a note and the next pull that touches
    ///             nothing casts it at what the look has then. While the tip is on a segment or a cell the world's preview
    ///             shows where the skill about to be taken would go.
    ///   Priorities while a hand holds the wand, that hand's grip and trigger are the wand's alone (VR/LokiInput.cs WandHeld): never
    ///             a laser click, never the skill wheels, never the item buttons, never the pad / flick gesture (which the wand
    ///             scheme does not use at all). The other hand keeps everything: walking, its laser or the wheels. Client wave 13
    ///             (w13resize): the camera is resize mode's (the overhead menu): when it starts, the wand goes back to the hip; while
    ///             a menu is open the wand stays in the hand but its trigger only presses the menu's buttons (W13CastingAllowed). The
    ///             pointing hand's laser is not drawn while it holds the wand: the wand's own line is, along the same ray.
    ///   Placing   Settings → Controls "Wand holster: Place…": a marker shows on the holster; a hand at it squeezes its grip, carries
    ///             it anywhere around the body and lets go: kept as an offset from the body anchor (Comfort.WandOffset). B ends
    ///             the mode. Client wave 15 (w15cast), point 12 (design note: "It should only stop repositioning the wand ... When I click the
    ///             button to end the repositioning, which in this case would be B"): letting go saves the place and the mode STAYS on:
    ///             the marker can be taken and moved again as often as wanted; only B ends it. "Reset" puts it back on the hip of the pointing hand's side.
    ///   Wave 18   client wave 18 (w18wand), the design's points after wave 17 (design notes): 9 the holster's spot shows only while the
    ///             holding hand is near it, 10 the holster follows the head's left / right turns at once (VR/WandHolster.cs), 11 the wand's line
    ///             follows the laser's rule (drawn by "Laser walking" on, else only near a target: UpdateW12Wand), 13 the inner wheel's and the
    ///             buttons' slots may hold items (used as a bar slot's: W12TakeItemId), 14 the inner wheel has "Wand slices" slices (2-8,
    ///             Game/WandWheelGeo.cs), 18 the placing hint goes at once when the placing ends (W12WandHintGone), 20 the wand is drawn as its
    ///             ghost where it lies over its open wheel (VR/WandHolster.cs Wheel).
    ///   Wave 21   client wave 21 (w21wand), design note (2026-10-11): the wand's SIZE, from its default length down to a wand that ends on the
    ///             holding hand's fingertip (the flick's point: W12Tip; VR/WandHolster.cs HeldPose, VR/Comfort.cs WandMinScale / WandMaxScale):
    ///             its tip, and so its line, the spell on it, its wheel and its strokes, are wherever that size puts the tip. Resized in
    ///             resize mode with both grips (LokiGame.W21Wand.cs): there a grip at the holster takes the wand for SIZING only (no casting:
    ///             <see cref="W12WandHand"/> stays -1), with the cue below.
    ///   Wave 23   client wave 23 (w23wand), the design's answers 54 and 55 (2026-10-11 / 12): "the casting of the wand with the tip misaligning with
    ///             the wheel feels weird. Bring it back to how it was before, where it stayed aligned"; "My idea of not spawning the new
    ///             wheel where the tip of the wand was is the problem"; "Wheel should be resizable on its own, not according to the wand."
    ///             (1) AT THE WAND a row's wheel opens CENTRED ON THE TIP as the tip is at that moment (<see cref="WandRowOnTip"/>): wave
    ///             14's rule again (wave 16 had left the picture on the first wheel's middle with the cursor snapped back to it, the tip
    ///             7.4 cm or more away). In front of the eyes nothing changed. (2) THE WHEEL HAS ITS OWN SIZE (VR/Comfort.cs WheelScale,
    ///             <see cref="W23WheelSize"/>: Settings "Wheel size", Reposition's two grips on its picture), whatever the wand's size: the
    ///             picture at the wand is drawn at that share about the tip and the tip's movement is read in the wheel's own (life-size)
    ///             metres, so a piece is where it is drawn; in front the picture is that share of its usual size and the cursor still
    ///             moves the same share of the tip's way. (3) The tip is carried by the HAND alone while the trigger is held
    ///             (<see cref="WandTipHeldInPull"/>): a tracked hand's wand ends on / leans to its index tip (wave 21), the finger whose
    ///             pinch is the trigger: its place in the hand is kept from the pull's first frame to its last.
    /// Not while the character cannot act or Horde's arena is shown (the wand is hidden then).
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>The wand "points at the sky" when its line rises more than this above the horizon and meets nothing.</summary>
        public const float SkyAngle = 40f;
        public const float WheelLateSeconds = 0.15f, WheelLateMove = 0.01f, WheelPathStep = 0.004f;
        /// <summary>Client wave 14 (w14cast): how far beyond the holster's reach the holding hand must have been for a grip let go at the
        /// holster to put the wand back (the grip that took it, let go on the spot, keeps it).</summary>
        public const float WandAwayMargin = 0.04f;
        // ---- client wave 23 (w23wand), the design's answers 54 and 55: a row's wheel on the tip; the wheel's own size; the tip carried by the hand
        /// <summary>At the wand a row's wheel opens centred on the tip as the tip is when the row is entered (wave 14's rule). False: wave
        /// 16 (the picture stays on the first wheel's middle, the cursor snaps back to it).</summary>
        public const bool WandRowOnTip = true;
        /// <summary>The wheel's own size, a share of life size (1 = 22 cm across): VR/Comfort.cs WheelScale within Reposition's limits (the
        /// Settings row "Wheel size"; Reposition's two grips on the wheel's picture). Never the wand's size.</summary>
        public static float W23WheelSize => Mathf.Clamp(Comfort.WheelScale, Comfort.PlaceMinScale, Comfort.PlaceMaxScale);
        /// <summary>While the trigger is held the fingertip the wand ends on / leans to (VR/WandHolster.cs HeldPose) is kept where it was in
        /// the hand's own frame when the pull began: the hand alone carries the tip. False: wave 21 (a tracked hand's pinch moves it).</summary>
        public const bool WandTipHeldInPull = true;
        private float _wheelSize = 1f; private Vector3 _wandFingerLocal;
        /// <summary>The open wheel's size, a share of life size (as it was when the trigger went down; smoke, log).</summary>
        public float W12WheelSize => _wheelSize;
        /// <summary>The fingertip VR/WandHolster.cs asks for (hand 0 left, 1 right): the flick's and the pad's own point (W12Tip), and during
        /// the wand hand's pull that point as it stood in the hand when the pull began.</summary>
        private Vector3 W23WandFinger(int i)
        {
            var h = _w12[i];
            if (WandTipHeldInPull && _wandDrawing && _wandHand == h) return h.T.TransformPoint(_wandFingerLocal);
            return W23FingerNow(i);
        }
        /// <summary>Hand i's fingertip as it is in this frame (W12Tip).</summary>
        private Vector3 W23FingerNow(int i)
        {
            return W12Tip(_w12[i]);
        }
        private bool _wandAway, _wheelInPlace, _wandFirmArmed;
        private readonly bool[] _wandFirmWas = new bool[2];
        /// <summary>Client wave 15 (w15cast), point 30: the wand put back by a firm squeeze away from the holster (smoke, log).</summary>
        public int W12WandFirmPutAways { get; private set; }
        /// <summary>Client wave 14 (w14cast): the wand went back to the hip by a grip at the holster / a grip let go there (smoke, log); the
        /// cue is shown now; the row wheel replaced the main wheel in this pull.</summary>
        public int W12WandPutAways { get; private set; }
        public bool W12WandCue => W12Wand != null && W12Wand.CueShown;
        public bool W12WheelInPlace => _wandDrawing && _wandWheelOn && _wheelInPlace;
        public WandHolster W12Wand { get; private set; }
        public WandWheel W12Wheel { get; private set; }
        private CastHand _wandHand, _wandPlaceHand; private bool _wandTrig, _wandDrawing, _wandStickArmed = true, _wandWheelOn, _wandSky, _wheelLate;
        private readonly bool[] _wandGripWas = new bool[2], _wandNear = new bool[2];
        private Unit _wandUnit; private Vector2Int? _wandCell;
        /// <summary>Client wave 18 (w18wand), point 11: how solid the wand's line is drawn now (0 to 1) and why ("laser walking", or the laser's
        /// own words: "panel", "unit", "ground", "near", "none"; smoke, log).</summary>
        public float W12WandLineAlpha => _wandLineAlpha;
        public string W12WandLineFor => _wandLineFor;
        private float _wandLineAlpha = 1f; private string _wandLineFor = "";
        private int _wheelRegion = -1, _wheelSub = -1, _wheelSteps, _wheelTab; private Vector2 _wheelPrev; private float _wheelT, _wandRise;
        /// <summary>The holster's placement mode is on.</summary>
        public bool W12WandPlacing { get; private set; }
        /// <summary>Which hand holds the wand: -1 none, 0 left, 1 right; the trigger is held (the wheel is open / a stroke is being
        /// drawn); casts made by a pull that touched nothing; skills put away by such a pull at the sky; buzzes of a hand entering
        /// the holster; quick slots taken on the wheel; button slots used (smoke, log).</summary>
        public int W12WandHand => _wandHand == null ? -1 : _wandHand.Left ? 0 : 1;
        public bool W12WandDrawing => _wandDrawing;
        public int W12WandCasts { get; private set; }
        public int W12WandSkyCancels { get; private set; }
        public int W12WandBuzzes { get; private set; }
        public int W12WandQuickTakes { get; private set; }
        public int W12WandButtonUses { get; private set; }
        /// <summary>The last quick slot (0-3: up, right, down, left; client wave 18: the inner slice, 0 .. "Wand slices" - 1, clockwise from the
        /// top) or button slot (0-3) used; -1 before any.</summary>
        public int W12WandLastQuick { get; private set; } = -1;
        public int W12WandLastButton { get; private set; } = -1;
        /// <summary>The wheel while the trigger is held: the region the tip is on (Game/WandWheelGeo.cs: -1 none, 0-7 inner (client wave 18:
        /// as many as "Wand slices"), 8-11 outer, 12-19 a cell), the open sub-wheel's side (-1 none), how many frames changed the region.</summary>
        public int W12WheelRegion => _wandDrawing && _wandWheelOn ? _wheelRegion : -1;
        public int W12WheelSub => _wandDrawing && _wandWheelOn ? _wheelSub : -1;
        public int W12WheelSteps => _wheelSteps;
        /// <summary>The wand held now points at the sky; how many degrees its line rises above the horizon.</summary>
        public bool W12WandSky => _wandHand != null && _wandSky;
        public float W12WandRise => _wandRise;
        private bool W12QuickStrokes => (Comfort.WandQuick & 1) != 0;
        private bool W12QuickButtons => (Comfort.WandQuick & 2) != 0;
        /// <summary>This character's wand slots (loki-user.json wandSlots, by the character's name: World/UserSettings.cs).</summary>
        public Loki.World.UserSettings.WandSlots W12WandSlots => Loki.World.UserSettings.WandSlotsOf(Session.SelectedChar?.Name ?? "");

        private void InitW12Wand(LokiBootstrap boot)
        {
            W12Wand = boot.Rig.gameObject.AddComponent<WandHolster>(); W12Wand.Init(boot.Rig);
            W12Wheel = WandWheel.Create(boot.Rig.transform, Hotkeys, Inventory, Prog, Quick);
            W12Wand.Wheel = W12Wheel; // client wave 18 (w18wand), point 20: the wand is ghosted where it lies over its wheel
            // client wave 21 (w21wand): the smallest wand ends on the holding hand's fingertip: the flick's and the pad's own point (W12Tip)
            // client wave 23 (w23wand): kept where it is in the hand while that hand's trigger is held (W23WandFinger)
            W12Wand.Fingertip = t => W23WandFinger(t == boot.Rig.LeftHand ? 0 : 1);
            InitW21Wand(boot); // resize mode's two grips on the wand (LokiGame.W21Wand.cs)
            if (ComfortUi != null) ComfortUi.PlaceWand = () => W12WandPlace(true);
        }

        /// <summary>Starts (or ends) the holster's placement mode.</summary>
        public void W12WandPlace(bool on)
        {
            if (!on && W12WandPlacing) { Comfort.Save(); Debug.Log("[Wand] holster placement ended (B)"); } // (client wave 15: B is the only end)
            if (!on) W12WandHintGone();
            W12WandPlacing = on; _wandPlaceHand = null;
            if (on) { if (_wandHand != null) W12WandDrop(); _wandPlaceHint = Lang.T("Grip the marker, move it, let go. B: done."); Hud?.Notify(_wandPlaceHint, 8f); }
        }

        // ---- client wave 18 (w18wand), point 18 (design note: "When I'm repositioning the wand through the menu, and I press B to confirm its location,
        // the text on the screen that tells me ... to move the wand and press B to be done needs to immediately disappear."): the hint was a
        // plain notice that lived its 8 s whatever happened. It is kept as shown and taken back the moment the placing ends (B, or the world
        // going away: UpdateW12Wand), from the notes in front of the eyes and from the menu's toast line (UI/LokiHud.cs Dismiss).
        private string _wandPlaceHint;
        /// <summary>The placing hint shown now (null: none; smoke, log).</summary>
        public string W12WandPlaceHint => _wandPlaceHint;
        private void W12WandHintGone() { if (_wandPlaceHint == null) return; Hud?.Dismiss(_wandPlaceHint); _wandPlaceHint = null; }

        /// <summary>The unit is on the map: a monster that has died is off the unit list at once but its body stays for about two
        /// seconds, with its collider (Sprites/EntityManager.cs:216: Destroy(u.gameObject, CorpseSeconds)). A cast at it would be
        /// sent and come to nothing (seen in the smoke: the skill left the tip and no damage came).</summary>
        private bool W12Alive(Unit u) => u != null && ReferenceEquals(Entities.Get(u.Id), u);

        /// <summary>What a ray meets, as the pointer's own ray reads it (VR/DioramaRig.cs): a unit, else a ground cell; a panel:
        /// nothing of the world; hit false: nothing at all within 50 m. The body of a monster that has died is no unit: the ray
        /// goes on through it.</summary>
        // client wave 18 (w18wand), point 11: also whether it met a panel, and whether it met a unit or a ground item (what the laser's
        // fade rule asks: VR/DioramaRig.Laser.cs LaserWant)
        private (Unit unit, Vector2Int? cell, Vector3 end, bool hit, bool panel, bool thing) W12Ray(Vector3 origin, Vector3 dir)
        {
            // client wave 14 (w14ui), point 18: a panel anywhere on the ray wins over the world, as on the laser (VR/DioramaRig.Ui.cs LaserHit)
            if (_boot.Rig != null && dir.sqrMagnitude > 1e-8f && _boot.Rig.LaserOnPanel(new Ray(origin, dir), 50f, out var onPanel)) return (null, null, onPanel.point, true, true, false);
            for (int through = 0; through < 6; through++)
            {
                if (!Physics.Raycast(origin, dir, out var hit, 50f, ~0, QueryTriggerInteraction.Ignore)) return (null, null, origin + dir * 3f, false, false, false);
                if (hit.collider.GetComponent<UiButton>() != null || hit.collider.GetComponent<UiBlocker>() != null) return (null, null, hit.point, true, true, false);
                var t = hit.collider.GetComponentInParent<Targetable>();
                if (t != null && t.Unit != null && !W12Alive(t.Unit)) { origin = hit.point + dir * 0.003f; continue; } // a body: on from just inside it (a ray does not meet the collider it starts in)
                if (t != null) return (t.Unit, null, hit.point, true, false, true);
                var map = _boot.Map;
                if (map != null && map.Current != null)
                {
                    var l = map.transform.InverseTransformPoint(hit.point); int cx = Mathf.FloorToInt(l.x), cy = Mathf.FloorToInt(l.z);
                    if (map.Current.Gat.InBounds(cx, cy)) return (null, new Vector2Int(cx, cy), hit.point, true, false, false);
                }
                return (null, null, hit.point, true, false, false);
            }
            return (null, null, origin, true, false, false);
        }

        /// <summary>The skill letting the trigger go now would take (null: none): the preview while the trigger is held.</summary>
        private Progression.SkillEntry W12WandCandidate()
        {
            if (!_wandDrawing || _wandHand == null) return null;
            int id = 0;
            if (_wandWheelOn)
            {
                if (_wheelRegion >= WandWheelGeo.Cell) { var hk = Hotkeys.At(_wheelTab * Hotkeys.RowsPerTab + _wheelSub, _wheelRegion - WandWheelGeo.Cell); if (!hk.Empty && hk.IsSkill) id = hk.Id; }
                else if (_wheelRegion >= 0 && _wheelRegion < WandWheelGeo.Outer && !W12WandSlots.quickItem[_wheelRegion]) id = W12WandSlots.quick[_wheelRegion]; // (client wave 18: an item slice has no skill to preview)
            }
            else if (_wandHand.Flick.TryResult(out int row, out int slot)) { var hk = Hotkeys.At(row, slot); if (!hk.Empty && hk.IsSkill) id = hk.Id; }
            return id > 0 && Prog.Skills.TryGetValue(id, out var k) && k.Level > 0 && !k.Passive ? k : null;
        }

        private void UpdateW12Wand()
        {
            var rig = _boot.Rig; var w = W12Wand;
            if (rig == null || w == null || _w12 == null) return;
            bool world = Session != null && Session.State == RoSession.Stage.InGame && !Player.Dead && Entities.Self != null && !W8HordeBlocksWorld;
            bool on = world && Comfort.CastGesture == 3;
            if (!world && W12WandPlacing) { W12WandPlacing = false; W12WandHintGone(); } // (client wave 18, point 18: its hint with it)
            bool placing = W12WandPlacing;
            // client wave 13 (w13view): Reposition (the overhead menu) shows the holster's marker too and carries it (LokiGame.W13View.cs)
            w.Tick(on || W12WandPlacing, placing || (W13Reposition && on), Time.unscaledDeltaTime);
            if (!on && !W12WandPlacing) { if (_wandHand != null) W12WandDrop(); rig.HidePointerRay = false; _wandNear[0] = _wandNear[1] = false; return; }
            // client wave 13 (w13resize): in resize mode a grip is the camera's alone: a wand in a hand goes back to the hip when the mode
            // starts (a spell on its tip is dropped with it, as when it is let go: W13SetResizeMode drops it at once; this keeps it so).
            // Reposition (w13view) the same: its grips carry things
            if ((LokiInput.ResizeMode || W13Reposition) && _wandHand != null) W12WandDrop();
            // the ONE rule for the new casting (client wave 13, the brief's seventh item: LokiGame.W13Resize.cs W13CastingAllowed)
            bool allowed = W13CastingAllowed;
            bool cue = false, cueAway = false;
            // client wave 21 (w21wand): in resize mode a grip at the holster takes the wand for sizing (LokiGame.W21Wand.cs): the same cue (the
            // hand coming within reach ticks as every thing a mode's grip can take does: LokiGame.W13View.cs UpdateW13Places)
            bool sizing = W21WandSizeFree;
            for (int i = 0; i < 2; i++)
            {
                var h = _w12[i];
                float dist = Vector3.Distance(h.T.position, w.HolsterPos);
                bool near = dist <= WandHolster.Radius;
                bool edge = h.In.PlainGrip && !_wandGripWas[i], nearWas = _wandNear[i]; var holder = _wandHand; // (client wave 21, w21wand: for the log at the loop's end)
                bool grip = h.In.PlainGrip, down = grip && !_wandGripWas[i] && !h.In.GrabClaimed, up = !grip && _wandGripWas[i]; _wandGripWas[i] = grip; // (client wave 13, w13grab: a grip that took a panel or the bag takes no wand)
                bool firm = h.In.FirmGrip, firmDown = firm && !_wandFirmWas[i]; _wandFirmWas[i] = firm; // client wave 15 (w15cast), point 30
                if (_wandHand == h && !grip) _wandFirmArmed = true; // (the grip let go since the wand was taken: a new firm squeeze counts)
                if (W20PenIn(h)) { _wandNear[i] = near; continue; } // client wave 20 (w20pen): a hand that holds the air pen takes no wand (LokiGame.W20Pen.cs)
                if (near && !_wandNear[i] && ((_wandHand == null && (allowed || placing)) || _wandHand == h)) { W12WandBuzzes++; Haptics.Pulse(Haptics.Kind.Wand, h.In.IsMain, 0.12f, 0.02f); } // a light buzz: the wand is here (client wave 14: or it can be put back here)
                _wandNear[i] = near;
                if (_wandHand == h && dist > WandHolster.Radius + WandAwayMargin) _wandAway = true;
                if (placing)
                {
                    if (_wandPlaceHand == null && near && down) _wandPlaceHand = h;
                    if (_wandPlaceHand == h)
                    {
                        if (grip) w.PlaceAt(h.T.position);
                        else { Comfort.Save(); _wandPlaceHand = null; Debug.Log($"[Wand] holster placed: offset {Comfort.WandOffset.x:0.00}, {Comfort.WandOffset.y:0.00}, {Comfort.WandOffset.z:0.00} m from the body anchor (the placement stays on until B)"); } // (client wave 15, point 12: the mode stays on)
                    }
                    continue;
                }
                if (_wandHand == null && near && down && allowed)
                {
                    _wandHand = h; h.In.WandHeld = true; _wandTrig = h.In.TriggerValue > 0.6f; _wandDrawing = false; _wandStickArmed = false; _wandAway = false; _wandFirmArmed = false;
                    _wandLineAlpha = Comfort.LaserWalk ? 1f : 0f; // (client wave 18, point 11: with "Laser walking" off the line fades in only near a target)
                    w.Hold(h.T); Haptics.Pulse(Haptics.Kind.Wand, h.In.IsMain, 0.25f, 0.04f);
                    Debug.Log($"[Wand] taken by the {(h.Left ? "left" : "right")} hand (it stays there until a grip at the holster, or a grip let go there, puts it back)");
                }
                // client wave 14 (w14cast), point 13 + answer 10: put back by a grip at the holster, or by a grip let go there after the hand had left it
                else if (_wandHand == h && near && (down || (up && _wandAway)))
                {
                    W12WandPutAways++;
                    Debug.Log($"[Wand] put back by the {(h.Left ? "left" : "right")} hand: {(down ? "a grip at the holster" : "a grip let go at the holster")}");
                    Haptics.Pulse(Haptics.Kind.Wand, h.In.IsMain, 0.2f, 0.03f);
                    W12WandDrop();
                }
                // client wave 15 (w15cast), point 30: a firm squeeze anywhere else puts it back too (a new squeeze: the grip let go since the take)
                else if (_wandHand == h && firmDown && _wandFirmArmed)
                {
                    W12WandPutAways++; W12WandFirmPutAways++;
                    Debug.Log($"[Wand] put back by the {(h.Left ? "left" : "right")} hand: a firm squeeze {dist * 100f:0} cm from the holster");
                    Haptics.Pulse(Haptics.Kind.Wand, h.In.IsMain, 0.3f, 0.05f);
                    W12WandDrop();
                }
                // the cue: a grip now would take it (a hand in reach, the wand on the hip, the game allows it) or put it back
                if (near && !h.In.GrabClaimed && ((_wandHand == null && (allowed || sizing)) || _wandHand == h)) { cue = true; cueAway |= _wandHand == h; }
                // client wave 21 (w21wand): a grip at the holster that takes no wand says why (log only: a scripted run's
                // grip that never drew the wand): its squeeze began there and the game refused it, or the hand came within reach already squeezed
                // (only the squeeze's start takes: line "down" above)
                if (_wandHand == null && holder == null && near && grip && (edge || !nearWas) && !sizing && !W21WandSizing)
                    Debug.Log($"[Wand] the {(h.Left ? "left" : "right")} grip at the holster ({dist * 100f:0.0} cm) takes no wand: {(h.In.GrabClaimed ? "a panel grab has this grip" : !allowed ? "the casting is off (" + (W13CastingBlocker ?? "?") + ")" : !edge ? "the grip was squeezed before the hand came within reach (let go and grip again)" : "?")}");
            }
            w.SetCue(cue, cueAway);
            if (_wandHand == null) { rig.HidePointerRay = false; _wandUnit = null; _wandCell = null; _wandSky = false; return; }
            var hd = _wandHand;
            if (!allowed)
            {
                // a menu is open (client wave 13): the wand stays in the hand and a skill on its tip stays, but its trigger is the
                // laser's click again, so the menu can be worked with that hand; a wheel or a stroke under way is closed, nothing taken
                if (_wandDrawing) { _wandDrawing = false; W12Wheel.Close(); hd.Hud.Hide(); w.TrailEnd(); Debug.Log($"[Wand] a menu opened ({W13CastingBlocker}): the wheel is closed, nothing taken"); }
                hd.In.WandHeld = false; hd.In.WandButtons = false; rig.HidePointerRay = false; w.SetLine(false, default);
                _wandUnit = null; _wandCell = null; _wandSky = false; _wandStickArmed = false;
                _wandTrig = hd.In.TriggerValue > 0.35f; // (a trigger still held when the menu closes opens no wheel)
                return;
            }
            hd.In.WandHeld = true;
            // where the wand points: the pointing hand's own ray (what its laser would be on); the other hand: along the hand
            Vector3 origin, dir;
            if (hd.In.IsMain) { origin = rig.PointerOrigin; dir = rig.PointerDirection; }
            else if (hd.In.TryAim(out var ap, out var ar)) { origin = rig.transform.TransformPoint(ap); dir = rig.transform.rotation * (ar * Vector3.forward); }
            else { origin = hd.T.position; dir = hd.T.forward; }
            var ray = W12Ray(origin, dir);
            _wandUnit = ray.unit; _wandCell = ray.cell;
            // the sky: the line rises clearly above the horizon and meets nothing at all
            _wandRise = dir.sqrMagnitude > 1e-8f ? Mathf.Asin(Mathf.Clamp(dir.normalized.y, -1f, 1f)) * Mathf.Rad2Deg : 0f;
            _wandSky = !ray.hit && _wandRise > SkyAngle;
            rig.HidePointerRay = hd.In.IsMain;
            // client wave 18 (w18wand), point 11: the wand's line follows the laser's rule (VR/DioramaRig.Laser.cs LaserWant): "Laser walking"
            // on: drawn whole, as before; off: drawn on a panel, a unit or a ground item, on the ground while a ground skill waits to be aimed,
            // faded in near a panel or a unit, not drawn elsewhere. Only the drawing: the ray (what it points at, where it casts) is the same.
            float lineWant = Comfort.LaserWalk ? 1f : rig.LaserWant(origin, dir, ray.panel, ray.thing, ray.cell.HasValue, out _wandLineFor, out _);
            if (Comfort.LaserWalk) _wandLineFor = "laser walking";
            _wandLineAlpha = Mathf.MoveTowards(_wandLineAlpha, lineWant, Time.unscaledDeltaTime / DioramaRig.LaserFadeSeconds);
            w.SetLine(true, ray.end, _wandLineAlpha);
            // the quick buttons: the pointing hand's lower face button, stick click, stick up / down
            bool buttons = W12QuickButtons && hd.In.IsMain && !Dialog.IsOpen && (Keyboard == null || !Keyboard.Visible);
            hd.In.WandButtons = buttons;
            if (buttons)
            {
                if (hd.In.WandButtonDown(0)) W12WandButton(0);
                if (hd.In.WandButtonDown(1)) W12WandButton(1);
                var st = hd.In.StickValue; bool free = !rig.StickTakenByUi && !rig.InputHeld; // (a slider or a scrolling text under the laser / the look, or the wheels, have the stick)
                if (Mathf.Abs(st.y) < 0.3f) _wandStickArmed = true;
                else if (free && _wandStickArmed && Mathf.Abs(st.y) > 0.7f && Mathf.Abs(st.y) > Mathf.Abs(st.x)) { _wandStickArmed = false; W12WandButton(st.y > 0f ? 2 : 3); }
            }
            float tv = hd.In.TriggerValue; bool trig = _wandTrig ? tv > 0.35f : tv > 0.6f;
            if (trig && !_wandTrig)
            {
                // the pull begins where the tip is now
                var head = rig.Head.transform;
                // client wave 23 (w23wand): from this frame to the pull's end the hand alone carries the tip: the fingertip the wand ends on /
                // leans to is kept where it is in the hand's own frame (W23WandFinger; this frame's wand was placed on this same point), and
                // the wheel is read at its own size (Settings "Wheel size")
                _wandFingerLocal = hd.T.InverseTransformPoint(W23FingerNow(hd.Left ? 0 : 1)); _wheelSize = W23WheelSize;
                _wandDrawing = true; hd.Origin = w.Tip.position; hd.Steps = 0; _wandWheelOn = W12QuickStrokes;
                if (_wandWheelOn)
                {
                    // the wheel's plane: facing the player (it stands square to the line from the eyes to the tip), upright
                    var f = hd.Origin - head.position; if (f.sqrMagnitude < 1e-6f) f = head.forward; f.Normalize();
                    var right = Vector3.Cross(Vector3.up, f); if (right.sqrMagnitude < 1e-4f) right = head.right; right.Normalize();
                    hd.Right = right; hd.Up = Vector3.Cross(f, right);
                    _wheelRegion = -1; _wheelSub = -1; _wheelSteps = 0; _wheelPrev = Vector2.zero; _wheelT = 0f; _wheelTab = Hotkeys.CurrentTab; _wheelInPlace = false;
                    _wheelLate = _armed != null; // a skill waits: this pull is most likely its cast
                    // client wave 13 (w13view), the brief's third item: the picture in front of the eyes (the default) or here at the tip;
                    // the control is the tip's movement in this plane either way (below)
                    var wsl = W12WandSlots; // (client wave 18, w18wand, points 13 and 14: "Wand slices" of them, skills or items)
                    // client wave 23 (w23wand): at the wand the picture at the wheel's own size, its middle on the tip (in front: PlaceFront)
                    if (Comfort.WandWheelFront) W12Wheel.OpenFront(head, wsl.quick, _wheelTab, wsl.quickItem);
                    else W12Wheel.Open(hd.Origin, Quaternion.LookRotation(f, hd.Up), wsl.quick, _wheelTab, wsl.quickItem, _wheelSize);
                }
                else
                {
                    // the plain strokes: measured in the upright plane facing the player, as the flick scheme's
                    var right = head.right; right.y = 0f; if (right.sqrMagnitude < 1e-4f) right = Vector3.right; right.Normalize();
                    hd.Right = right; hd.Up = Vector3.up;
                    hd.Flick.Begin(Comfort.FlickFollow); // (client wave 18, w18flick, point 7: the flick's "Flick follow", one recogniser for both)
                    hd.Hud.Begin(hd.Origin, Quaternion.LookRotation(Vector3.Cross(right, Vector3.up), Vector3.up));
                }
                w.TrailBegin();
            }
            if (_wandDrawing)
            {
                var d = w.Tip.position - hd.Origin; var p = new Vector2(Vector3.Dot(d, hd.Right), Vector3.Dot(d, hd.Up));
                bool look = W13AimLook; // (client wave 13, w13look: "Look select: off" aims with the wand)
                if (_wandWheelOn)
                {
                    // client wave 23 (w23wand): the tip's place in the wheel's OWN metres (life size): a piece is where it is drawn at every
                    // "Wheel size"; its keeping, the path's step and the late wheel's centimetre below scale with it (100 %: divided by 1,
                    // wave 12's numbers to the bit)
                    p /= _wheelSize;
                    _wheelT += Time.unscaledDeltaTime; int before = _wheelRegion, subBefore = _wheelSub;
                    // the tip's path since the last frame, in small steps: what it crosses is touched (an outer segment opens its sub-wheel)
                    int n = Mathf.Clamp(Mathf.CeilToInt((p - _wheelPrev).magnitude / WheelPathStep), 1, 150);
                    for (int i = 1; i <= n; i++)
                    {
                        var q = Vector2.Lerp(_wheelPrev, p, i / (float)n);
                        if (_wheelInPlace) { _wheelRegion = WandWheelGeo.NextInPlace(_wheelRegion, q, _wheelSub); continue; }
                        _wheelRegion = WandWheelGeo.Next(_wheelRegion, q, ref _wheelSub);
                        // client wave 14 (w14cast), point 14: "Wand rows: in place": an outer segment touched REPLACES the main wheel with its
                        // row's wheel, the cursor back in its middle: the plane's origin moves to where the tip is now; the rest of this
                        // frame's path counts from there.
                        // Client wave 16 (w16cast), point 3 (design note: "you are opening the new wheel centered where my cursor was when I pulled it
                        // towards the outer circle. require the new menu to open up in the same center as the first circle, and the selector
                        // cursor to snap back to the center, even if that means that it doesn't line up with the actual tip of the wand"):
                        // the PICTURE stays where it is, at the wand as in front of the eyes: the row's wheel is drawn on the first wheel's
                        // middle and the cursor (drawn at p from that middle) is back on it; only the control's origin moves to the tip (wave 14
                        // also moved the picture there at the wand: W12Wheel.transform.position = hd.Origin)
                        if (Comfort.WandRows == 0 && _wheelRegion >= WandWheelGeo.Outer && _wheelRegion < WandWheelGeo.Cell)
                        {
                            _wheelInPlace = true; _wheelRegion = WandWheelGeo.None;
                            if (WandRowOnTip && !W12Wheel.Front)
                            {
                                // client wave 23 (w23wand), the design's answer 55 ("My idea of not spawning the new wheel where the tip of the wand
                                // was is the problem"): AT THE WAND the row's wheel opens centred on the TIP as it is in this frame (wave 14
                                // moved the picture under the tip; wave 16 left it on the first wheel's middle): the pull's origin and the
                                // picture go to the tip, the cursor is on it, the tip on nothing yet
                                hd.Origin = w.Tip.position; p = Vector2.zero; W12Wheel.Recentre(hd.Origin);
                                Debug.Log($"[Wand] wheel: row {_wheelSub + 1}'s wheel opens on the tip (it touched the row {q.magnitude * 100f:0.0} cm of the wheel's own from the first wheel's middle)");
                                break;
                            }
                            hd.Origin += (hd.Right * q.x + hd.Up * q.y) * _wheelSize; p -= q; // (client wave 23, w23wand: q is in the wheel's own metres, the origin in the world's)
                            int m = Mathf.Clamp(Mathf.CeilToInt(p.magnitude / WheelPathStep), 1, 150);
                            for (int j = 1; j <= m; j++) _wheelRegion = WandWheelGeo.NextInPlace(_wheelRegion, p * (j / (float)m), _wheelSub);
                            Debug.Log($"[Wand] wheel: row {_wheelSub + 1}'s wheel replaces the main wheel on its middle; the cursor back in the middle (the tip touched it {q.magnitude * 100f:0.0} cm from the middle)");
                            break;
                        }
                    }
                    _wheelPrev = p;
                    if (_wheelRegion != before || _wheelSub != subBefore)
                    {
                        _wheelSteps++;
                        if (_wheelRegion >= 0 || _wheelSub != subBefore) Haptics.Pulse(Haptics.Kind.Casting, hd.In.IsMain, _wheelSub != subBefore ? 0.2f : 0.12f, _wheelSub != subBefore ? 0.025f : 0.015f);
                    }
                    bool visible = !_wheelLate || _wheelSteps > 0 || _wheelT >= WheelLateSeconds || p.magnitude >= WheelLateMove;
                    W12Wheel.Draw(_wheelRegion, _wheelSub, p, visible, _wheelInPlace);
                }
                else
                {
                    var fl = hd.Flick;
                    fl.Update(p, Time.unscaledDeltaTime);
                    if (fl.Steps != hd.Steps) { hd.Steps = fl.Steps; Haptics.Pulse(Haptics.Kind.Casting, hd.In.IsMain, 0.12f, 0.015f); }
                    hd.Hud.Draw(fl.Phase, fl.Dir1, fl.Dir2, fl.Corner, p);
                }
                w.TrailAdd(w.Tip.position);
                if (!trig)
                {
                    _wandDrawing = false; w.TrailEnd();
                    if (_wandWheelOn)
                    {
                        W12Wheel.Close(); int r = _wheelRegion;
                        if (r >= WandWheelGeo.Cell)
                            W12Take(_wheelTab * Hotkeys.RowsPerTab + _wheelSub, r - WandWheelGeo.Cell, hd.Left, look || Comfort.CastOnTake, look); // a cell of the open sub-wheel: that row's slot
                        else if (r >= 0 && r < WandWheelGeo.Outer)
                        {
                            // an inner segment: that direction's quick slot (client wave 18, w18wand, point 13: a skill, or an item: used, as a bar slot's)
                            int id = W12WandSlots.quick[r]; bool item = W12WandSlots.quickItem[r];
                            W12WandLastQuick = r;
                            Debug.Log($"[Wand] wheel: quick slot {r + 1} of {WandWheelGeo.Slices}: {(item ? "item" : "skill")} {id}");
                            if (id > 0 && item) { W12WandQuickTakes++; W12TakeItemId(id); }
                            else if (id > 0) { W12WandQuickTakes++; W12TakeSkillId(id, hd.Left, look || Comfort.CastOnTake, look); }
                            else Hud.Notify(Lang.T("Empty: pin a skill here with ★."));
                        }
                        else if (r < 0 && _wheelSteps == 0) W12WandCastPull(hd, look);
                        else Debug.Log($"[Wand] wheel: let go on {(r < 0 ? "nothing" : "row " + (r - WandWheelGeo.Outer + 1) + "'s outer segment")}: nothing taken");
                    }
                    else
                    {
                        var fl = hd.Flick; hd.Hud.Hide();
                        if (fl.TryResult(out int row, out int slot)) W12Take(row, slot, hd.Left, look || Comfort.CastOnTake, look);
                        else if (fl.Steps == 0) W12WandCastPull(hd, look);
                    }
                }
            }
            _wandTrig = trig;
        }

        /// <summary>A pull that touched nothing was let go: the skill on the tip is cast (wand aim: where the wand points, else the
        /// look; look aim: the look); with the wand pointing at the sky it is put away instead, as B does.</summary>
        private void W12WandCastPull(CastHand hd, bool look)
        {
            if (_armed == null) return;
            if (_wandSky)
            {
                W12WandSkyCancels++;
                Debug.Log($"[Wand] the wand points at the sky ({_wandRise:0}° up, nothing on its line): {_armed.Info.Aegis} is put away, nothing sent");
                if (W12SpellHeld) W12Spell.Fizzle();
                Loki.Audio.LokiAudio.Instance?.PlayUi();
                Haptics.Pulse(Haptics.Kind.Wand, hd.In.IsMain, 0.15f, 0.03f);
                Disarm();
                return;
            }
            W12WandCasts++;
            var u = W12AimedUnit(); var c = W12AimedCell();
            Debug.Log($"[Wand] cast {_armed.Info.Aegis}: {(look ? "the look" : "the wand")} on {(u != null ? "unit " + u.Id : c.HasValue ? "cell " + c.Value : "nothing")}");
            CastArmed(u, _armed.Ground ? c : null);
        }

        /// <summary>A quick button of the wand hand: 0 the lower face button, 1 the stick click, 2 stick up, 3 stick down: its own
        /// skill, cast at once at what the wand points at / the look has (the setting "Wand aim"); with no target it waits on the tip.</summary>
        private void W12WandButton(int b)
        {
            int id = W12WandSlots.buttons[b]; bool item = W12WandSlots.buttonsItem[b]; // (client wave 18, w18wand, point 13: an item too)
            W12WandLastButton = b;
            Debug.Log($"[Wand] button slot {b + 1}: {(item ? "item" : "skill")} {id}");
            if (id <= 0) { Hud.Notify(Lang.T("Empty button: pin a skill with ★.")); return; }
            W12WandButtonUses++;
            Haptics.Pulse(Haptics.Kind.Wand, true, 0.2f, 0.03f);
            if (item) { W12TakeItemId(id); return; }
            W12TakeSkillId(id, _wandHand.Left, true, true);
        }

        /// <summary>Client wave 18 (w18wand), point 13 (design note: "allow the player to also place items on the initial inner wheel of the wand menu.
        /// Not just skills."): a wand slot's item is used at once, as a quick bar slot's item is (UI/WristMenu.cs UseItemById: its first stack;
        /// none in the bag: said so).</summary>
        private void W12TakeItemId(int itemId)
        {
            W12Takes++; W12WandItemUses++;
            Debug.Log($"[Wand] item {itemId} used from a wand slot ({Inventory.Count(itemId)} in the bag)");
            Hud.Wrist.UseItemById(itemId);
        }
        /// <summary>Items used from the wand's own slots (smoke, log).</summary>
        public int W12WandItemUses { get; private set; }

        /// <summary>The wand goes back to the hip (the grip was let go, the scheme changed, the world went away); a spell on its tip
        /// is dropped, a wheel or a stroke under way is forgotten.</summary>
        private void W12WandDrop()
        {
            if (_wandHand == null) return;
            Debug.Log($"[Wand] let go by the {(_wandHand.Left ? "left" : "right")} hand: back to the hip");
            _wandHand.In.WandHeld = false; _wandHand.In.WandButtons = false; _wandHand.Hud.Hide(); W12Wheel.Close();
            _wandHand = null; _wandDrawing = false; _wandTrig = false; _wandUnit = null; _wandCell = null; _wandSky = false; _wandAway = false; _wheelInPlace = false;
            W12Wand.Release(); W12Wand.SetCue(false, false); _boot.Rig.HidePointerRay = false;

            if (W12SpellHeld) Disarm();
        }
    }
}
