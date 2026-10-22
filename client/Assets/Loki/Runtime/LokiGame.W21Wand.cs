using Loki.Net;
using Loki.VR;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 21 (w21wand): THE WAND CAN BE RESIZED in resize mode. design note (2026-10-11): "gives me the ability to resize the wand itself
    /// (during resize mode, by grabbing it with both hands) such that the maximum size is its current default size, and the minimum size
    /// makes it so that the tip of my finger is the casting tip much like the positioning of the flick casting method".
    ///   What a player does  in resize mode (the overhead menu's switch) with the wand as the casting gesture, the wand hangs at its holster
    ///             as always. A hand within the holster's reach (VR/WandHolster.cs Radius, 12 cm: it never shrinks with the wand) ticks
    ///             and the holster's cue lights; that hand's grip takes the wand into the hand, for SIZING only: nothing is cast with it
    ///             (LokiGame.W12Wand.cs: W12WandHand stays -1, the casting is off in resize mode as before). The OTHER hand's grip, anywhere,
    ///             then resizes it: the hands pulled apart make it longer, pushed together shorter, by the share the distance between them
    ///             changed since that grip began (the rule every two-grip resize of the game has: the table, a board, Reposition's
    ///             pictures). Letting the second grip go keeps the size; gripping again goes on from it. Letting the FIRST grip go ends
    ///             it: the wand flies back to its holster and the size is saved. Ending resize mode (its switch, a shake) the same.
    ///   The size  one number, VR/Comfort.cs WandScale (the Settings slider "Wand size" and Reposition's two grips on the holster change
    ///             the same one), from WandMinScale to WandMaxScale: the largest is the wand's default size (30 cm: VR/WandHolster.cs
    ///             Length), the smallest the size at which its tip lies on the holding hand's fingertip, the point the flick and the pad
    ///             measure from (LokiGame.W12Cast.cs W12Tip; VR/WandHolster.cs HeldPose). A tick in both hands when an end is reached.
    ///             Kept where the holster's place is kept: loki-user.json wandScale (World/UserSettings.cs: the ACCOUNT's, not in
    ///             PerPcFields); the default is the largest; the overhead menu's Reposition "Reset" puts it back with the holster's place
    ///             (LokiGame.W13View.cs W13ResetPlaces, as since wave 14).
    ///   One code path  the taking, the two-grip resize, the letting go and the saving are Reposition's own (LokiGame.W13View.cs
    ///             UpdateW13Places: this is one more of its things, marked as resize mode's: Placeable.InResize); nothing is built twice.
    ///   The camera  in resize mode a plain grip is the camera's (VR/LokiInput.cs CameraGrip: the table's move, both grips its resize, a
    ///             board's hold). The grip that takes the wand, and the other hand's grip while the wand is held, are the WAND's from
    ///             their first frame until they are let go (VR/LokiInput.cs ClaimForWandSize, <see cref="W21WandSizeProbe"/>): the table
    ///             neither moves nor resizes with them. Every other grip in resize mode does what it did. A grip at a panel's edge or in
    ///             the backpack zone is the panel's first (LokiGame.W13Grab.cs), as everywhere.
    ///   Friends   see the wand in the hand at its size while it is sized, as when it is held to cast (UI/GestureShare.cs Wand: its size
    ///             and, new, its lean in the hand's frame travel in fields the wire already had: no change of the relay or of the format).
    /// Not while the holster is being placed, the character cannot act, another casting gesture is chosen or Horde's arena is shown (the
    /// wand is not drawn then).
    /// </summary>
    public sealed partial class LokiGame
    {
        private Placeable _w21Wand; private readonly bool[] _w21Take = new bool[2]; private int _w21End;
        /// <summary>Resize mode may size the wand now: the mode is on and the wand hangs in the world (UpdateW12Wand's own test).</summary>
        public bool W21WandSizable => LokiInput.ResizeMode && Comfort.CastGesture == 3 && !W12WandPlacing && W12Wand != null && _w12 != null && _w21Wand != null
            && Session != null && Session.State == RoSession.Stage.InGame && !Player.Dead && Entities.Self != null && !W8HordeBlocksWorld;
        /// <summary>A grip at the holster now would take the wand for sizing (the cue: LokiGame.W12Wand.cs).</summary>
        private bool W21WandSizeFree => _w21Wand != null && _w21Wand.By < 0 && W21WandSizable;
        /// <summary>A hand holds the wand for sizing; which (-1 none, 0 left, 1 right); the other hand's grip resizes it now (smoke, log).</summary>
        public bool W21WandSizing => _w21Wand != null && _w21Wand.By >= 0;
        public int W21WandSizeHand => _w21Wand != null ? _w21Wand.By : -1;
        public bool W21WandResizing => W21WandSizing && _w21Wand.Resizing;
        /// <summary>Times the wand was taken for sizing; ticks at an end of its sizes (smoke, log).</summary>
        public int W21WandSizeTakes { get; private set; }
        public int W21WandEndTicks { get; private set; }

        /// <summary>Called by InitW12Wand: resize mode's thing for the grips (LokiGame.W13View.cs UpdateW13Places) and the input's probe.</summary>
        private void InitW21Wand(LokiBootstrap boot)
        {
            _w21Wand = new Placeable
            {
                Name = "the wand", InResize = true, Resizable = true, Min = Comfort.WandMinScale, Max = Comfort.WandMaxScale,
                Shown = () => W21WandSizable, Centre = () => W12Wand.HolsterPos, Factor = () => Comfort.WandScale,
                // a hand whose grip is squeezed takes it only when the input gave that grip to the wand (the probe below): the camera has
                // every other grip; a grip the probe gave it takes it even if the holster moved a little since (it hangs from the head)
                Hand = i => { var inp = _w12[i].In; if (!inp.PlainGrip) { _w21Take[i] = false; return true; } return inp.WandSizeClaimed; },
                Near = p => { for (int i = 0; i < 2; i++) if (_w21Take[i] && p == _w12[i].T.position) return true; return Vector3.Distance(p, W12Wand.HolsterPos) <= WandHolster.Radius; },
                Resizer = i => _w12[i].In.WandSizeClaimed,
                Carry = (to, f) => W21WandCarry(f), Release = W21WandLetGo,
            };
            _w13Places.Add(_w21Wand);
            LokiInput.Hand.WandSizeProbe = W21WandSizeProbe;
        }

        /// <summary>VR/LokiInput.cs ClaimForWandSize's probe, asked once in the frame a grip starts in resize mode (after the panel grabs):
        /// true = the grip is the wand's until it is let go: it takes the wand at its holster, or resizes the wand the other hand holds.</summary>
        private bool W21WandSizeProbe(LokiInput.Hand input)
        {
            if (this == null || _boot == null || _boot.Rig == null || !W21WandSizable) return false;
            int i = input.IsLeft ? 0 : 1; var h = _w12[i];
            if (h.In != input) return false;
            if (_w21Wand.By >= 0)
            {
                if (_w21Wand.By == i) return false;
                Debug.Log($"[Wand] resize mode: the {(i == 0 ? "left" : "right")} grip is the wand's: it resizes the wand in the other hand");
                return true;
            }
            if (_boot.Rig.Grabbed != null) return false; // (the laser holds a board: this grip resizes the board, VR/DioramaRig.Ui.cs)
            if (W20PenIn(h)) return false; // (a hand that holds the air pen takes no wand: LokiGame.W20Pen.cs)
            float d = Vector3.Distance(h.T.position, W12Wand.HolsterPos);
            if (d > WandHolster.Radius) return false;
            _w21Take[i] = true;
            Debug.Log($"[Wand] resize mode: the {(i == 0 ? "left" : "right")} grip is the wand's: it takes the wand at its holster ({d * 100f:0.0} cm) for sizing");
            return true;
        }

        /// <summary>Every frame the wand is held for sizing (UpdateW13Places: the size as the two grips have it, or as it is).</summary>
        private void W21WandCarry(float factor)
        {
            var p = _w21Wand; var w = W12Wand; if (p.By < 0 || w == null) return;
            var h = _w12[p.By];
            if (w.HeldBy != h.T)
            {
                w.Hold(h.T); W21WandSizeTakes++; _w21Take[p.By] = false;
                float s0 = Comfort.WandScale; _w21End = s0 <= Comfort.WandMinScale ? -1 : s0 >= Comfort.WandMaxScale ? 1 : 0;
                Debug.Log($"[Wand] resize mode: held for sizing by the {(h.Left ? "left" : "right")} hand at x{s0:0.000} ({W21WandCm():0.0} cm to its tip); the other grip resizes it");
            }
            float was = Comfort.WandScale, now = Mathf.Clamp(factor, Comfort.WandMinScale, Comfort.WandMaxScale);
            if (now == was) return;
            Comfort.WandScale = now; w.Refresh();
            int end = now <= Comfort.WandMinScale ? -1 : now >= Comfort.WandMaxScale ? 1 : 0;
            if (end != 0 && end != _w21End)
            {
                W21WandEndTicks++; Haptics.Both(Haptics.Kind.Wand, 0.2f, 0.03f);
                Debug.Log($"[Wand] resize mode: the wand at its {(end < 0 ? "smallest: its tip on the fingertip" : "largest: its default size")} (x{now:0.000}, {W21WandCm():0.0} cm to its tip)");
            }
            _w21End = end;
        }

        /// <summary>The holding grip was let go (or the mode ended, or the wand went away): the size is kept, the wand goes back to the hip.</summary>
        private void W21WandLetGo()
        {
            var w = W12Wand;
            Comfort.Save();
            Debug.Log($"[Wand] resize mode: let go: the wand's size x{Comfort.WandScale:0.000} ({W21WandCm():0.0} cm to its tip) is kept; back to the hip");
            if (w != null && _wandHand == null) w.Release();
            _w21Take[0] = _w21Take[1] = false;
        }

        /// <summary>From the wand's handle end to its tip as drawn now, in centimetres.</summary>
        private float W21WandCm() => W12Wand != null && W12Wand.Tip != null ? Vector3.Distance(W12Wand.Tip.position, W12Wand.Wand.position) * 100f : 0f;
    }
}
