using System.Collections.Generic;
using Loki.Net;
using Loki.UI;
using Loki.VR;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 20 (w20pen), the design's point 57, the AIR PEN (playtest notes row 57): "On the social menu, allow the
    /// player to spawn a simple pen object that can be used to draw on the air. The pen also comes with a button that erases individual strokes
    /// or the entire thing. Storing the pen away does not delete what has been written. It writes in the air in 3D space." + "They should stay
    /// on the map until you erase them, log out, or until you change maps." + "You should also be able to pull this pen out from above you.
    /// When you look at the menu, it will show up and you can pull it out of the air." Design notes of 2026-10-10: "Drawings seen by" and
    /// "Drawings I see", both friends only by default (Settings → Headsets: VR/AirPen.cs ComfortRows).
    ///   Taking it   the Social page's "Take pen" (UI/WristMenu.W4Social.cs, both menu styles): the pen in the POINTING hand (a wand in that
    ///               hand goes back to the hip first); or a grip of either hand at the pen floating beside the overhead menu (shown while the
    ///               menu is: looking up; VR/AirPen.cs), within <see cref="PenReach"/> (the wand's reach at its holster).
    ///   Holding it  it stays in the hand when the grip is let go (the wand's equip rule). Meanwhile that hand's grip and trigger are the pen's
    ///               (VR/LokiInput.cs WandHeld: no click, no skill wheels, no casting gesture, no panel grab from them) and its lower face
    ///               button and stick click too (WandButtons): the pen never interferes with casting; the other hand keeps everything. While a
    ///               menu is open (the one casting rule: LokiGame.W13Resize.cs W13CastingAllowed) the pen draws nothing and its trigger presses
    ///               the menus' buttons again, as the wand's does. The pointing hand's laser is not drawn while it holds the pen.
    ///   Drawing     the trigger held (over 0.6 of its travel, until under 0.35: the wand's) draws a stroke from the pen's tip: a point every
    ///               <see cref="PenStep"/> of the tip's travel, <see cref="PenWidth"/> thick at the table's size of the moment, FIXED IN THE
    ///               WORLD: kept in the map's frame (UI/PenDrawings.cs), so it stays where it was drawn on the diorama as the table moves,
    ///               turns or scales. A pull held when the pen came to the hand (or when a menu closed) draws nothing until it is let go.
    ///   Erasing     the pen's button (the holding hand's lower face button: A / X) switches it between pen and ERASER; as an eraser its tip
    ///               (a pink ball of <see cref="PenEraser"/>) erases every stroke of mine it touches, at once. Held <see cref="PenLongPress"/> s,
    ///               the button erases ALL my strokes (and the pen is a pen again). Only my own strokes.
    ///   Putting it away  the Social page's "Put pen away", or the holding hand's grip at the pen's slot beside the overhead menu (a grip there, or
    ///               a grip let go there after the hand had left it: the wand's rule); resize mode, Reposition, the holster's placement, Horde's
    ///               arena and leaving the game put it away too. My drawings stay.
    ///   Gone        my drawings go when I erase them, when I change map (a warp on the same map keeps them) and when I leave the game: the
    ///               character list reloads the scene and a quit ends it (the drawings are scene objects: UI/PenDrawings.cs OnDestroy); a
    ///               session that fails keeps the scene, and UpdateW20Pen erases them then. For the others they go by the relay's GONE.
    /// Hooks: LokiGame.cs Update (UpdateW20Pen after UpdateW12Cast), LokiGame.W12Wand.cs (a hand that holds the pen takes no wand),
    /// VR/HeadsetAvatars.cs ComfortRows (the two settings), UI/WristMenu.W4Social.cs (the button).
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>The pen's tip ahead of the hand (m), a stroke's point every PenStep of the tip's travel (m), a stroke's thickness (m at the
        /// table's size when drawn), the eraser's reach from the tip (m), the button's long press (s), the reach at the pen's spot (m: the wand's
        /// WandHolster.Radius), how far beyond it the hand must have been for a grip let go there to put the pen back (m: the wand's).</summary>
        public const float PenTip = AirPen.Tip, PenStep = 0.004f, PenWidth = 0.005f, PenEraser = 0.015f, PenLongPress = 1.0f, PenReach = WandHolster.Radius, PenAwayMargin = WandAwayMargin;
        /// <summary>The overhead menu drawn at least this much: its pen can be taken (it is seen).</summary>
        public const float PenSpotAlpha = 0.5f;

        public AirPen W20Pen { get; private set; }
        public PenDrawings W20Drawings { get; private set; }
        private CastHand _penHand; private bool _penErase, _penTrig, _penAway, _penBtnPrev, _penPress, _penLongDone, _penWasAllowed; private float _penBtnSince;
        private readonly bool[] _penGripWas = new bool[2];
        private Vector3 _penLast; private string _penMap; private bool _penInGame; private uint _penSelf;
        /// <summary>Which hand holds the pen: -1 none, 0 left, 1 right; it is an eraser now; times taken / put away; strokes drawn; strokes erased
        /// by the eraser; long presses that erased all (smoke, log).</summary>
        public int W20PenHand => _penHand == null ? -1 : _penHand.Left ? 0 : 1;
        public bool W20PenEraser => _penHand != null && _penErase;
        public int W20PenTakes { get; private set; }
        public int W20PenPutAways { get; private set; }
        public int W20PenStrokes { get; private set; }
        public int W20PenErased { get; private set; }
        public int W20PenClears { get; private set; }
        /// <summary>How the pen came to the hand the last time, and went away ("the Social page", "the air beside the overhead menu", ...).</summary>
        public string W20PenFrom { get; private set; } = "";
        public string W20PenAwayWhy { get; private set; } = "";
        /// <summary>A hand that holds the pen takes no wand (LokiGame.W12Wand.cs).</summary>
        private bool W20PenIn(CastHand h) => h != null && _penHand == h;

        private void InitW20Pen()
        {
            var rig = _boot.Rig;
            W20Pen = rig.gameObject.AddComponent<AirPen>(); W20Pen.Init(rig, HeadsetAvatars.TintFor(Session != null ? Session.AccountId : 0));
            if (Hud != null && Hud.Wrist != null) { Hud.Wrist.TogglePen = () => W20PenToggle("the Social page"); Hud.Wrist.PenHeld = () => _penHand != null; }
        }

        /// <summary>The drawings, once the game is on a map (with the headsets' channel when there is one: Loki Host's relay; else mine only).</summary>
        private void StartW20Drawings()
        {
            var ch = _presence as IDrawChannel;
            W20Drawings = PenDrawings.Create(_boot.Map, Headsets, ch, Session.AccountId);
            _penSelf = Session.AccountId; W20Pen.SetInk(HeadsetAvatars.TintFor(_penSelf));
            Debug.Log($"[Pen] drawings: mine shown to {PenDrawings.Who(Comfort.DrawAudience)}; I see {PenDrawings.Who(Comfort.DrawFilter)}'s{(ch == null ? " (no relay that carries drawings: mine stay on my table)" : "")}");
        }

        /// <summary>The Social page's button: the pen in the pointing hand, or put away.</summary>
        public void W20PenToggle(string why)
        {
            if (_penHand != null) { W20PenAway(why); return; }
            if (!W20PenWorld) { Hud?.Notify(Lang.T("No pen here now.")); return; }
            var h = _w12 != null ? _w12[Comfort.LeftHanded ? 0 : 1] : null; if (h == null) return;
            if (_wandHand == h) W12WandDrop(); // (the pen takes the pointing hand: the wand goes back to the hip, a skill on its tip with it)
            W20PenTake(h, why);
        }

        private bool W20PenWorld => Session != null && Session.State == RoSession.Stage.InGame && Entities.Self != null && !W8HordeBlocksWorld && W20Pen != null;

        private void W20PenTake(CastHand h, string why)
        {
            _penHand = h; _penErase = false; _penTrig = true; _penBtnPrev = h.In.Primary.IsPressed(); _penPress = false; _penLongDone = false; _penWasAllowed = false;
            _penAway = Vector3.Distance(h.T.position, W20Pen.Spot) > PenReach + PenAwayMargin; // (taken at its spot: a grip let go there keeps it)
            W20PenTakes++; W20PenFrom = why;
            Haptics.Pulse(Haptics.Kind.Wand, h.In.IsMain, 0.25f, 0.04f);
            Debug.Log($"[Pen] taken by the {(h.Left ? "left" : "right")} hand ({why})");
            Hud?.Notify(Lang.T("Pen: trigger draws. A or X: eraser. Hold it: erase all."), 5f);
        }

        private void W20PenAway(string why)
        {
            var h = _penHand; if (h == null) return;
            W20Drawings?.End();
            h.In.WandHeld = false; h.In.WandButtons = false; // (the wand's hand is never the pen's: W20PenIn)
            _penHand = null; _penErase = false; W20PenPutAways++; W20PenAwayWhy = why;
            Haptics.Pulse(Haptics.Kind.Wand, h.In.IsMain, 0.2f, 0.03f);
            Debug.Log($"[Pen] put away ({why}); my drawings stay ({(W20Drawings != null ? W20Drawings.Mine.Strokes.Count : 0)} stroke(s))");
        }

        /// <summary>Once a frame, after the casting (LokiGame.Update: UpdateW12Cast ran the wand, the overhead menu and the rig's flags).</summary>
        private void UpdateW20Pen()
        {
            var rig = _boot.Rig;
            if (rig == null || _w12 == null || _boot.Map == null || Session == null) return;
            if (W20Pen == null) InitW20Pen();
            bool inGame = Session.State == RoSession.Stage.InGame && Entities.Self != null;
            // leaving the game takes my drawings with it. The character list reloads the scene (LokiGame and the drawings with it); a session
            // that fails or goes back to the login in this scene is caught here. A map-server change passes through CharSelected /
            // MapConnecting / MapEntering and is the map change below
            var st = Session.State;
            bool loggedOut = st == RoSession.Stage.Idle || st == RoSession.Stage.LoginConnecting || st == RoSession.Stage.LoginSent || st == RoSession.Stage.CharServerList
                          || st == RoSession.Stage.CharConnecting || st == RoSession.Stage.CharList || st == RoSession.Stage.Failed;
            if (loggedOut && _penInGame)
            {
                _penInGame = false; _penMap = null;
                if (_penHand != null) W20PenAway("left the game");
                if (W20Drawings != null) { W20Drawings.ClearMine("left the game"); W20Drawings.DropOthers("left the game"); }
            }
            if (!inGame) { W20Pen.Hide(); W20Pen.SetCue(false); W20Pen.SetSlot(false, 0f); return; }
            _penInGame = true;
            if (W20Drawings == null) StartW20Drawings();
            else if (Session.AccountId != _penSelf) { Destroy(W20Drawings.gameObject); StartW20Drawings(); } // (another account on this PC)
            // a map change: my drawings and everyone else's go (they belonged to the old map); a warp on the same map keeps them
            string map = _boot.Map.Current != null ? _boot.Map.Current.Name : null;
            if (map != null && _penMap != null && !string.Equals(map, _penMap, System.StringComparison.OrdinalIgnoreCase))
            {
                W20Drawings.End();
                int n = W20Drawings.ClearMine($"I changed map: {_penMap} → {map}"); W20Drawings.DropOthers("I changed map");
                Debug.Log($"[Pen] map {_penMap} → {map}: my {n} stroke(s) and the others' gone");
            }
            if (map != null) _penMap = map;
            W20Drawings.Hidden = W8HordeBlocksWorld;
            if (!W20PenWorld) { if (_penHand != null) W20PenAway(W8HordeBlocksWorld ? "Horde's arena" : "the world went away"); W20Pen.Hide(); W20Pen.SetCue(false); W20Pen.SetSlot(false, 0f); return; }
            // the grips belong to another mode: the pen goes away (the wand's rule: resize mode, Reposition, the holster's placement)
            bool modes = LokiInput.ResizeMode || W13Reposition || W12WandPlacing;
            if (modes && _penHand != null) W20PenAway(LokiInput.ResizeMode ? "resize mode" : W13Reposition ? "Reposition" : "the holster's placement");
            bool allowed = W13CastingAllowed;

            // ---- where it waits: beside the overhead menu, drawn as much as the menu is
            var m = W13Overhead;
            float menuAlpha = m != null ? m.Alpha : 0f;
            if (m != null) { var f = m.FrameWorld; W20Pen.SetSpot(f.c + f.r.normalized * (f.r.magnitude + AirPen.SpotGap), menuAlpha > 0.002f); }
            else W20Pen.SetSpot(rig.Head.transform.position, false);
            bool spotUsable = W20Pen.SpotShown && menuAlpha >= PenSpotAlpha;

            // ---- the grips at the spot: take it, or put it back (either hand; not a grip that took a panel)
            bool cue = false;
            for (int i = 0; i < 2; i++)
            {
                var h = _w12[i];
                bool grip = h.In.PlainGrip, down = grip && !_penGripWas[i] && !h.In.GrabClaimed, up = !grip && _penGripWas[i]; _penGripWas[i] = grip;
                float dist = Vector3.Distance(h.T.position, W20Pen.Spot);
                bool near = spotUsable && dist <= PenReach;
                if (_penHand == h && dist > PenReach + PenAwayMargin) _penAway = true;
                if (_penHand == null && near && down && !modes && _wandHand != h) { W20PenTake(h, "the air beside the overhead menu"); continue; }
                if (_penHand == h && near && (down || (up && _penAway))) { W20PenAway(down ? "a grip at its place beside the overhead menu" : "a grip let go at its place beside the overhead menu"); continue; }
                if (near && !h.In.GrabClaimed && ((_penHand == null && !modes && _wandHand != h) || _penHand == h)) cue = true;
            }
            W20Pen.SetCue(cue);
            W20Pen.SetSlot(_penHand != null && W20Pen.SpotShown, menuAlpha);
            if (_penHand == null) { W20Pen.ShowFloating(W20Pen.Spot, W20Pen.SpotShown ? menuAlpha : 0f); return; }

            // ---- in a hand
            var hd = _penHand;
            W20Pen.ShowInHand(hd.T, _penErase);
            hd.In.WandHeld = allowed; hd.In.WandButtons = allowed;
            if (hd.In.IsMain) { if (allowed) rig.HidePointerRay = true; else rig.MenuClicksOnly = true; } // (after the wand's own: UpdateW12Cast)
            float tv = hd.In.TriggerValue;
            if (!allowed || !_penWasAllowed) { W20Drawings.End(); _penTrig = tv > 0.35f; } // (a menu: nothing drawn; a pull held across it draws nothing)
            _penWasAllowed = allowed;
            // the button (the holding hand's lower face button): a press that began while the pen could act switches pen / eraser when it is let
            // go; held PenLongPress s it erases everything of mine instead. While a menu is open it is the menu's (no press counts).
            bool btn = hd.In.Primary.IsPressed();
            if (!allowed) { _penPress = false; _penBtnPrev = btn; return; }
            float now = Time.unscaledTime;
            if (btn && !_penBtnPrev) { _penPress = true; _penBtnSince = now; _penLongDone = false; }
            _penBtnPrev = btn;
            if (_penPress && btn && !_penLongDone && now - _penBtnSince >= PenLongPress)
            {
                _penLongDone = true; W20Drawings.End(); int n = W20Drawings.ClearMine("the pen's button held"); W20PenClears++; _penErase = false;
                Haptics.Pulse(Haptics.Kind.Wand, hd.In.IsMain, 0.5f, 0.15f);
                Hud?.Notify(Lang.T("Drawings erased."), 3f);
                Debug.Log($"[Pen] the button held {PenLongPress:0.0} s: all {n} of my stroke(s) erased");
            }
            if (_penPress && !btn)
            {
                _penPress = false;
                if (!_penLongDone)
                {
                    _penErase = !_penErase; W20Drawings.End();
                    Haptics.Pulse(Haptics.Kind.Wand, hd.In.IsMain, 0.15f, 0.025f);
                    Debug.Log($"[Pen] the button: {(_penErase ? "eraser" : "pen")}");
                }
            }
            var tip = W20Pen.TipPoint.position;
            var mt = _boot.Map.transform; float mpc = Mathf.Max(mt.lossyScale.x, 1e-6f);
            var tipMap = mt.InverseTransformPoint(tip);

            if (_penErase)
            {
                // the eraser's ball touches strokes of mine: gone
                var hit = W20Drawings.MineNear(tipMap, PenEraser / mpc);
                if (hit.Count > 0)
                {
                    W20Drawings.EraseMine(hit); W20PenErased += hit.Count;
                    Haptics.Pulse(Haptics.Kind.Wand, hd.In.IsMain, 0.2f, 0.03f);
                    Debug.Log($"[Pen] eraser: stroke(s) {string.Join(", ", hit)} erased");
                }
                _penTrig = tv > 0.35f;
                return;
            }
            // the trigger draws (the wand's thresholds: on over 0.6, off under 0.35)
            bool trig = _penTrig ? tv > 0.35f : tv > 0.6f;
            var drawing = W20Drawings.Drawing;
            if (trig && !_penTrig)
            {
                W20Drawings.Begin(tipMap, PenWidth / mpc, HeadsetAvatars.TintFor(_penSelf)); _penLast = tip; W20PenStrokes++;
                Haptics.Pulse(Haptics.Kind.Wand, hd.In.IsMain, 0.08f, 0.015f);
            }
            else if (trig && drawing != null && Vector3.Distance(tip, _penLast) >= PenStep)
            {
                if (W20Drawings.Add(tipMap)) _penLast = tip;
                else { W20Drawings.End(); Debug.Log($"[Pen] a stroke is full ({DrawWire.MaxStrokePoints} points): it ends here"); }
            }
            else if (!trig && _penTrig && drawing != null)
            {
                if (Vector3.Distance(tip, _penLast) >= PenStep * 0.25f) W20Drawings.Add(tipMap); // (where the pull was let go)
                Debug.Log($"[Pen] stroke {drawing.Id}: {drawing.Points.Count} points, {(drawing.Points.Count - 1) * PenStep * 100f:0} cm or so");
                W20Drawings.End();
            }
            _penTrig = trig;
        }
    }
}
