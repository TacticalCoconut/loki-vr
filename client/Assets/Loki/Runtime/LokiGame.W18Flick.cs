using System.Collections.Generic;
using Loki.Game;
using Loki.UI;
using Loki.VR;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 18 (w18flick): the design's points 16 and 15 after wave 17 (design notes); points 7 and 8 are Game/FlickRecognizer.cs.
    ///   Point 16, the PAD SLAP (design note: "if I slap a specific pad, it will cycle to a second one. So for example, the right hand is row one, the
    ///             left hand is row two. If I slap the row one, it will go to row three and if I slapped row two, it will go to row four. When I
    ///             let go of the pad, it all reverts back"; "The slap needs to be with the same hand that opened the pad. Slap should be a default
    ///             feature of the pad system"; "a slider that controls how much force is needed"; after the wave: "Keep the slap as an alternate
    ///             mode to swap. The default is still the palm method"). Settings → Controls → Casting "Pad rows: Palm / Slap" (Palm by default:
    ///             wave 12's palm rows, no slap; VR/Comfort.cs PadSlap = Slap mode): the pointing hand's pad opens on row 1 of the shown bar, the
    ///             other hand's on row 2 (the palm picks no row then, <see cref="W18FBaseRow"/>). A SLAP: the pad's OWN hand's fingertip
    ///             moving through the pad, away from the eyes (along the pad's facing), at "Slap strength" (VR/Comfort.cs SlapSpeed, m/s) or
    ///             faster, measured over the last <see cref="SlapWindow"/> s, while it is inside the pad's outline (+ <see cref="SlapMarginCells"/>
    ///             of a cell) and within CastPad.Depth of its plane. It switches that pad's row (1 ↔ 3, 2 ↔ 4), with a rumble tick; a second
    ///             slap switches it back; letting the grip go closes the pad, and the next one opens on row 1 / 2 again. A slap never takes a
    ///             slot: no cell is marked while the push goes on (until the push slows under <see cref="SlapRearm"/> of the speed). One slap
    ///             a push: the next needs the push to slow down first and <see cref="SlapLockSeconds"/> s; none in the first
    ///             <see cref="SlapArmSeconds"/> s after the pad opened (a hand still reaching out as the grip closes).
    ///             Every pad gesture logs the fastest push through the pad and the fastest move across it (the same 50 ms measure): real hand
    ///             speeds for the default ("[Cast] pad speeds").
    ///   Point 15, the PAD'S PLACE (design note: "allow the player to reposition where the pad shows up relative to my hand when I activate it.
    ///             specifically"): in Reposition, with the pad as the casting gesture, each hand shows its pad as a GHOST (see-through) where it
    ///             would open (UI/CastPad.cs Pose: the fingertip + that hand's offset); the OTHER hand's grip (or the pointing laser + grip for
    ///             the walking hand's pad) carries it; let go: its offset from the fingertip (in the frame facing the eyes: right, up, away)
    ///             and its turn (the carrying hand's turn; upright unless "Panel tilt: Free") are kept per hand (VR/Comfort.cs PadOffset /
    ///             PadTurn). The other grip resizes it, as Reposition's pictures (it is "Pad size"). Reposition's Reset puts both back on the
    ///             fingertip.
    /// Hooks: LokiGame.W12Cast.cs (InitW12Cast, W12OpenGesture, W12Track, W12CloseGesture), LokiGame.W13View.cs (UpdateW13View, the
    /// Placeable's hand rule, W13ResetPlaces).
    /// </summary>
    public sealed partial class LokiGame
    {
        public const float SlapWindow = 0.05f, SlapArmSeconds = 0.15f, SlapLockSeconds = 0.25f, SlapMarginCells = 0.5f, SlapRearm = 0.5f;

        private sealed class W18FPad
        {
            public readonly List<(float t, Vector3 p)> Trail = new List<(float t, Vector3 p)>();
            public bool Slapped, Armed, Pushing, Measured; public float OpenedT, LockT, Fwd, Across, PeakFwd, PeakAcross; public int Slaps;
            public Placeable Place; public bool Carrying; public Quaternion HandRot0, Turn0;
        }
        private W18FPad[] _w18f;
        /// <summary>Slaps so far, the push speed of the last one (m/s); a hand's pad switched by a slap now; its last measured speeds through and
        /// across the pad, and the fastest of this gesture (smoke, log).</summary>
        public int W18FSlaps { get; private set; }
        public float W18FLastSlapSpeed { get; private set; }
        public bool W18FSlapped(bool left) => _w18f != null && _w18f[left ? 0 : 1].Slapped;
        public (float through, float across, float peakThrough, float peakAcross) W18FSpeeds(bool left) { var s = _w18f != null ? _w18f[left ? 0 : 1] : null; return s == null ? default : (s.Fwd, s.Across, s.PeakFwd, s.PeakAcross); }
        /// <summary>Point 15: a hand's pad shown as Reposition's ghost now; carried now (smoke, log).</summary>
        public bool W18FGhost(bool left) => _w12 != null && _w12[left ? 0 : 1].Pad.Ghost && _w12[left ? 0 : 1].Pad.Shown;
        public bool W18FCarried(bool left) => _w18f != null && _w18f[left ? 0 : 1].Place != null && _w18f[left ? 0 : 1].Place.By >= 0;

        /// <summary>Called by InitW12Cast after InitW13Resize (Reposition's list exists): the pads' places in Reposition.</summary>
        private void InitW18Flick(LokiBootstrap boot)
        {
            _w18f = new[] { new W18FPad(), new W18FPad() };
            if (_w12 == null) return;
            for (int k = 0; k < 2; k++)
            {
                int own = k; var pad = _w12[k].Pad;
                _w18f[k].Place = new Placeable
                {
                    Name = own == 0 ? "the left hand's pad" : "the right hand's pad",
                    Shown = () => pad.Ghost && pad.Shown, Centre = () => pad.CellWorld(4),
                    Near = p => W13InRect(pad.FrameWorld, p, PlaceDepth, PlaceMargin), Laser = ray => W13RayRect(pad.FrameWorld, ray),
                    Hand = i => i != own, // (its own hand carries it about anyway: the other hand moves it from there)
                    Factor = () => Comfort.PadCell / Comfort.PadCellDefault, Resizable = true,
                    Carry = (w, f) => W18FCarry(own, w, f), Release = () => W18FRelease(own), Look = lit => pad.SetGhostLit(lit),
                };
                _w13Places.Add(_w18f[k].Place);
            }
        }

        // ------------------------------------------------------------------ point 16: the rows and the slap
        /// <summary>The row a pad of hand <paramref name="h"/> shows: "Pad rows: Slap": the pointing hand row 1 of the shown bar, the other row 2,
        /// slapped + 2; Palm (the default): wave 12's rule (the pointing hand palm down 1, up 2; the other hand 3, 4).</summary>
        private int W18FBaseRow(CastHand h, bool slapped) => Hotkeys.CurrentTab * Hotkeys.RowsPerTab
            + (Comfort.PadSlap ? (h.In.IsMain ? 0 : 1) + (slapped ? 2 : 0) : (h.In.IsMain ? 0 : 2) + (h.PalmUp ? 1 : 0));

        /// <summary>A pad gesture opens (LokiGame.W12Cast.cs W12OpenGesture): the slap's state anew; the row it opens on.</summary>
        private int W18FPadOpened(CastHand h)
        {
            var s = _w18f[h.Left ? 0 : 1];
            s.Trail.Clear(); s.Slapped = false; s.Armed = false; s.Pushing = false; s.Measured = false; s.OpenedT = Time.unscaledTime; s.LockT = 0f;
            s.Fwd = s.Across = s.PeakFwd = s.PeakAcross = 0f; s.Slaps = 0;
            int row = W18FBaseRow(h, false);
            W24PadOpens++; W24PadPalmUp = h.PalmUp; W24PadRow = row; // client wave 24 (w24), item 8: the tutorial's "the palm picks the pad" part reads them (LokiGame.W18Tutorial.cs)
            return row;
        }
        /// <summary>Client wave 24 (w24), item 8: pads opened so far; the last one's palm (up / down) and the row it opened on.</summary>
        public int W24PadOpens { get; private set; }
        public bool W24PadPalmUp { get; private set; }
        public int W24PadRow { get; private set; }

        /// <summary>Once a frame while a pad is open (LokiGame.W12Cast.cs W12Track), before its cell is found: the fingertip's speed through
        /// and across the pad; a slap switches the row. True while a slap's push goes on (no cell is marked then).</summary>
        private bool W18FSlapTick(CastHand h, Vector3 tip)
        {
            var s = _w18f[h.Left ? 0 : 1]; float now = Time.unscaledTime;
            s.Trail.Add((now, tip)); while (s.Trail.Count > 2 && now - s.Trail[0].t > 0.3f) s.Trail.RemoveAt(0);
            if (!Comfort.PadSlap)
            {
                int wantRow = W18FBaseRow(h, false);
                if (wantRow != h.Row)
                {
                    h.Row = wantRow; h.Cell = -1; h.Pad.Show(h.Row);
                    W24PadOpens++; W24PadPalmUp = h.PalmUp; W24PadRow = h.Row;
                    Haptics.Pulse(Haptics.Kind.Casting, h.In.IsMain, 0.20f, 0.025f);
                }
                s.Pushing = false;
                return false;
            }
            int k = -1; for (int i = s.Trail.Count - 2; i >= 0; i--) if (now - s.Trail[i].t >= SlapWindow) { k = i; break; }
            if (k < 0) return s.Pushing; // (not yet SlapWindow of the hand's path since the pad opened)
            var pad = h.Pad; var tr = pad.transform; var v = (tip - s.Trail[k].p) / Mathf.Max(now - s.Trail[k].t, 1e-4f);
            s.Fwd = Vector3.Dot(v, tr.forward); s.Across = Vector3.ProjectOnPlane(v, tr.forward).magnitude; s.Measured = true;
            s.PeakFwd = Mathf.Max(s.PeakFwd, s.Fwd); s.PeakAcross = Mathf.Max(s.PeakAcross, s.Across);
            float need = Comfort.SlapSpeed;
            if (s.Pushing && s.Fwd < SlapRearm * need) s.Pushing = false;
            if (!s.Armed && !s.Pushing && now - s.OpenedT >= SlapArmSeconds && now >= s.LockT && s.Fwd < SlapRearm * need) s.Armed = true;
            if (s.Armed && s.Fwd >= need && W18FInPad(pad, tip))
            {
                s.Armed = false; s.Pushing = true; s.LockT = now + SlapLockSeconds;
                s.Slapped = !s.Slapped; int was = h.Row; h.Row = W18FBaseRow(h, s.Slapped); h.Cell = -1; pad.Show(h.Row);
                s.Slaps++; W18FSlaps++; W18FLastSlapSpeed = s.Fwd;
                Haptics.Pulse(Haptics.Kind.Casting, h.In.IsMain, 0.35f, 0.04f);
                Debug.Log($"[Cast] pad slap ({(h.Left ? "left" : "right")} hand): bar {was / Hotkeys.RowsPerTab + 1} row {was % Hotkeys.RowsPerTab + 1} → row {h.Row % Hotkeys.RowsPerTab + 1}, {s.Fwd:0.00} m/s through the pad (a slap needs {need:0.00}), {s.Across:0.00} m/s across it");
            }
            return s.Pushing;
        }

        /// <summary>The fingertip inside the pad's outline (+ half a cell) and within its depth.</summary>
        private static bool W18FInPad(CastPad pad, Vector3 tip)
        {
            var tr = pad.transform; var d = tip - pad.CellWorld(4);
            float half = 1.5f * CastPad.PitchPx * tr.lossyScale.x + SlapMarginCells * pad.CellMetres;
            return Mathf.Abs(Vector3.Dot(d, tr.right)) <= half && Mathf.Abs(Vector3.Dot(d, tr.up)) <= half && Mathf.Abs(Vector3.Dot(d, tr.forward)) <= CastPad.Depth;
        }

        /// <summary>A pad gesture ended (LokiGame.W12Cast.cs W12CloseGesture): its hand's speeds, for the headset (the slap's default).</summary>
        private void W18FPadClosed(CastHand h)
        {
            var s = _w18f[h.Left ? 0 : 1];
            if (s.Measured) Debug.Log($"[Cast] pad speeds ({(h.Left ? "left" : "right")} hand, {Time.unscaledTime - s.OpenedT:0.00} s open): the fastest push through the pad {s.PeakFwd:0.00} m/s, the fastest move across it {s.PeakAcross:0.00} m/s (over {SlapWindow * 1000f:0} ms); slaps {s.Slaps} (Pad rows {(Comfort.PadSlap ? "Slap" : "Palm")}, a slap needs {Comfort.SlapSpeed:0.00} m/s)");
        }

        // ------------------------------------------------------------------ point 15: the pads' places in Reposition
        /// <summary>Once a frame (LokiGame.W13View.cs UpdateW13View, before Reposition's grips): in Reposition with the pad as the casting gesture,
        /// each hand's pad as a ghost where it would open; else no ghost.</summary>
        private void UpdateW18Flick()
        {
            if (_w12 == null || _w18f == null) return;
            bool want = W13Reposition && Comfort.CastGesture == 1;
            var eyes = _boot.Rig.Head.transform.position;
            for (int k = 0; k < 2; k++)
            {
                var h = _w12[k]; var pad = h.Pad; var s = _w18f[k];
                if (!want) { if (pad.Ghost) { pad.Hide(); s.Carrying = false; } continue; }
                if (h.Open) continue; // (Reposition closes a gesture as it starts: never both)
                if (s.Place.By >= 0) continue; // (carried: W18FCarry places it)
                var (c, r) = CastPad.Pose(W12Tip(h), eyes, Comfort.PadOffset[k], Comfort.PadTurn[k]);
                pad.PlaceAt(c, r, Comfort.PadCell); pad.ShowGhost(W18FBaseRow(h, false), s.Place.LitWas);
            }
        }

        /// <summary>Reposition carries hand <paramref name="own"/>'s pad: its middle cell to <paramref name="world"/>, still facing the eyes from
        /// there with the turn it had, and turned as the carrying hand turned since it took it: about the vertical only (the hand's heading),
        /// unless "Panel tilt: Free" (the hand's whole turn); <paramref name="factor"/>: the other grip's resize ("Pad size").</summary>
        private void W18FCarry(int own, Vector3 world, float factor)
        {
            var s = _w18f[own]; var pad = _w12[own].Pad; int by = s.Place.By; if (by < 0) return;
            var hand = _w12[by].T; var eyes = _boot.Rig.Head.transform.position;
            if (!s.Carrying) { s.Carrying = true; s.HandRot0 = hand.rotation; s.Turn0 = Quaternion.Inverse(CastPad.Facing(pad.CellWorld(4) - eyes)) * pad.transform.rotation; }
            Comfort.PadCell = Mathf.Clamp(factor * Comfort.PadCellDefault, Comfort.PadCellMin, Comfort.PadCellMax);
            var facing = CastPad.Facing(world - eyes); Quaternion turn;
            if (Comfort.PanelTiltFree) turn = hand.rotation * Quaternion.Inverse(s.HandRot0);
            else
            {
                var h0 = s.HandRot0 * Vector3.forward; var h1 = hand.rotation * Vector3.forward; h0.y = 0f; h1.y = 0f;
                turn = Quaternion.AngleAxis(h0.sqrMagnitude > 1e-4f && h1.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(h0, h1, Vector3.up) : 0f, Vector3.up);
            }
            pad.PlaceAt(world, turn * facing * s.Turn0, Comfort.PadCell);
        }

        /// <summary>Let go: the pad's place from its own hand's fingertip now is kept (VR/Comfort.cs PadOffset / PadTurn).</summary>
        private void W18FRelease(int own)
        {
            var s = _w18f[own]; s.Carrying = false; var h = _w12[own]; var pad = h.Pad;
            var (o, t) = CastPad.PlaceOf(W12Tip(h), _boot.Rig.Head.transform.position, pad.CellWorld(4), pad.transform.rotation);
            float m = Comfort.PadOffsetMax; o = new Vector3(Mathf.Clamp(o.x, -m, m), Mathf.Clamp(o.y, -m, m), Mathf.Clamp(o.z, -m, m));
            Comfort.PadOffset[own] = o; Comfort.PadTurn[own] = t; Comfort.Save();
            Debug.Log($"[Place] {s.Place.Name}: {o.x * 100f:0.0} / {o.y * 100f:0.0} / {o.z * 100f:0.0} cm right / up / away from the eyes from the fingertip, turned {Quaternion.Angle(Quaternion.identity, t):0.0}° from facing the eyes, cells {Comfort.PadCell * 100f:0.0} cm");
        }
    }
}
