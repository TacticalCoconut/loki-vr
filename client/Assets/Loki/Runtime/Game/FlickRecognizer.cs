using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 12 (w12cast): the two-stroke flick of the "flick" casting scheme (the design's combination 2), and of the wand, which
    /// draws the same strokes: one recogniser for both. It is fed the hand's (or the wand tip's) position in a flat plane facing
    /// the player, in metres from where the gesture began (x to the right, y up), once a frame.
    ///   First stroke: <see cref="StrokeLength"/> from the start in one of eight directions picks one of the 8 hotkey rows.
    ///   Letting go there took that row's slot 5 (the middle of its 3 x 3) until client wave 16: now it takes nothing (a cancel).
    ///   Second stroke: <see cref="StrokeLength"/> from the corner in one of eight directions picks one of the other eight slots,
    ///   laid out as the pad's cells around the middle one.
    /// Directions are the cells of a 3 x 3 around its middle, in reading order: up-left, up, up-right, left, right, down-left, down,
    /// down-right. As the first stroke they are rows 1-8 in that order (bar 1 rows 1-4: up-left, up, up-right, left; bar 2 rows
    /// 1-4: right, down-left, down, down-right); as the second stroke they are the slot at that place of the pad (1, 2, 3, 4, 6,
    /// 7, 8, 9).
    /// The corner between the strokes: while the hand keeps going outwards along the first stroke the corner goes with it (a long
    /// first stroke is still one stroke); a turn of more than 60 degrees starts the second stroke at once; a second stroke
    /// straight on (or up to 60 degrees off) needs a stop first: the hand resting (<see cref="DwellRadius"/> for
    /// <see cref="DwellSeconds"/>) fixes the corner where it is ("flick, flick").
    /// Hysteresis everywhere: a direction changes only when the angle is <see cref="SectorHysteresis"/> degrees past the border
    /// between two directions; a chosen slot is given up only when the hand is back within <see cref="StrokeBack"/> of the
    /// corner. (Until client wave 18 coming back within 3 cm of the start cancelled everything: see below.)
    /// Client wave 14 (w14cast), point 5: wave 12's "icons only after a hesitation" (ShowIcons, 0.25 s) is gone: the picture
    /// (UI/FlickHud.cs) shows every choice from the first frame.
    /// Client wave 16 (w16cast), design note (2026-10-08): "Remove the center slot on the flick menu. That way the user can cancel a cast": the
    /// middle is CANCEL on both steps. Letting go before a first stroke, or after a first stroke without a second one (the hand at the
    /// row's middle, or back there within <see cref="StrokeBack"/>), takes NOTHING (<see cref="TryResult"/>); only a second stroke
    /// takes a slot: the eight around the middle (slots 1-4 and 6-9). Slot 5 of a row is not on the flick any more (the pad, the wand's
    /// wheel and the bar keep it).
    /// Client wave 18 (w18flick), the design's points 7 and 8 (design notes):
    ///   Point 8 (BUG: "when I drag my cursor over some slots on the second step of the flick menu it will drag me back to the first initial
    ///   menu"): steps 1 and 2 (<see cref="Phase"/> 1 and 2) went back to the first step whenever the hand came within 3 cm of the START
    ///   (wave 12's CancelRadius). The row's slots are drawn 7.5 cm round its middle (UI/FlickHud.cs RingPx x Scale), which sits 6-12 cm
    ///   out from the start, so the slot straight back (opposite the first stroke) is drawn ON the start (0.5-4.5 cm from it) and the
    ///   cursor dragged onto it, or across it between its two neighbours, fell back to the first step (proved on this class, old against
    ///   new, 64 paths and 8 sweeps: design notes). Now only the row's middle cancels on the second step (wave 16's rule):
    ///   the distance to the start means nothing once a row is chosen; letting go at the middle still takes nothing.
    ///   Point 7 ("for a moment after I flick towards the first tab ... the center will drag with my controller", then "just Provide a
    ///   slider that controls the duration ... and set the default duration to zero"): the corner follows the hand outwards (the rule
    ///   above) for at most <see cref="FollowSeconds"/> after the first stroke (VR/Comfort.cs FlickFollow, Settings → Controls → Casting
    ///   "Flick follow"); 0 (the default): it stays where the first stroke was recognised (<see cref="StrokeLength"/> out).
    /// </summary>
    public sealed class FlickRecognizer
    {
        public const float StrokeLength = 0.06f, StrokeBack = 0.035f, SectorHysteresis = 8f, DwellRadius = 0.008f, DwellSeconds = 0.12f; // (client wave 18: CancelRadius 0.03 gone, point 8)
        /// <summary>The corner goes with the hand only while the hand is still moving outwards: it has gone further along the first
        /// stroke than before AND this frame's move is within 60 degrees of that stroke (at least this share of it lies along it).
        /// A move across the stroke leaves the corner behind: the second stroke begins.</summary>
        public const float OutwardShare = 0.5f;
        /// <summary>0 nothing chosen, 1 a row (its middle slot), 2 a row and a slot.</summary>
        public int Phase { get; private set; }
        /// <summary>The strokes' directions: 0 up, then clockwise in steps of 45 degrees; -1 none.</summary>
        public int Dir1 { get; private set; } = -1;
        public int Dir2 { get; private set; } = -1;
        /// <summary>Where the first stroke ended (the second stroke is measured from here).</summary>
        public Vector2 Corner { get; private set; }
        public bool CornerFixed { get; private set; }
        /// <summary>Steps so far (a row chosen, changed, a slot chosen, changed, given up, cancelled): one tick in the hand each.</summary>
        public int Steps { get; private set; }
        public float SinceStep { get; private set; }
        private float _cornerProj, _stillT; private Vector2 _stillAt, _prev;
        /// <summary>Client wave 18 (w18flick), point 7: how long the corner may follow the hand after the first stroke (seconds; 0: not at all),
        /// as given to <see cref="Begin"/>.</summary>
        public float FollowSeconds { get; private set; }
        private float _sinceStroke;

        /// <summary>The cell (0-8, reading order) a direction points at from the middle cell.</summary>
        private static readonly int[] DirCell = { 1, 2, 5, 8, 7, 6, 3, 0 };
        public static int CellOfDir(int dir) => dir < 0 ? 4 : DirCell[dir & 7];
        /// <summary>The direction from the middle cell to cell c (-1 for the middle).</summary>
        public static int DirOfCell(int cell) { for (int d = 0; d < 8; d++) if (DirCell[d] == cell) return d; return -1; }
        /// <summary>The hotkey row (0-7) a first stroke's direction picks: the eight outer cells in reading order.</summary>
        public static int RowOfDir(int dir) { int c = CellOfDir(dir); return c < 4 ? c : c - 1; }
        public static int DirOfRow(int row) => DirOfCell(row < 4 ? row : row + 1);
        public static Vector2 DirVector(int dir) { float a = dir * 45f * Mathf.Deg2Rad; return new Vector2(Mathf.Sin(a), Mathf.Cos(a)); }

        /// <summary>A new gesture; <paramref name="followSeconds"/>: client wave 18, point 7, how long the corner follows the hand after the
        /// first stroke (VR/Comfort.cs FlickFollow).</summary>
        public void Begin(float followSeconds) { _prev = Vector2.zero; Phase = 0; Dir1 = Dir2 = -1; Corner = Vector2.zero; CornerFixed = false; Steps = 0; SinceStep = 0f; _stillT = 0f; _stillAt = Vector2.zero; _cornerProj = 0f; FollowSeconds = Mathf.Max(0f, followSeconds); _sinceStroke = 0f; }

        /// <summary>The direction of v; with a current direction, it is kept until v is past its border by the hysteresis.</summary>
        public static int Sector(Vector2 v, int current)
        {
            float a = Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg; if (a < 0f) a += 360f;
            if (current >= 0 && Mathf.Abs(Mathf.DeltaAngle(a, current * 45f)) <= 22.5f + SectorHysteresis) return current;
            return Mathf.RoundToInt(a / 45f) & 7;
        }

        private void Step() { Steps++; SinceStep = 0f; }

        public void Update(Vector2 p, float dt)
        {
            SinceStep += dt;
            if ((p - _stillAt).magnitude > DwellRadius) { _stillAt = p; _stillT = 0f; } else _stillT += dt;
            switch (Phase)
            {
                case 0:
                    if (p.magnitude >= StrokeLength) { Dir1 = Sector(p, -1); Phase = 1; Corner = p; CornerFixed = FollowSeconds <= 0f; _sinceStroke = 0f; _cornerProj = Vector2.Dot(p, DirVector(Dir1)); _stillAt = p; _stillT = 0f; Step(); } // (client wave 18, point 7: follow 0 = fixed at once)
                    break;
                case 1:
                {
                    // (client wave 18, point 8: no cancel by the distance to the START here any more: wave 12-17 went back to the first step
                    // within 3 cm of it, where the slot straight back is drawn)
                    if (!CornerFixed) { _sinceStroke += dt; if (_sinceStroke > FollowSeconds) CornerFixed = true; } // (client wave 18, point 7: the follow's time is up: it stays where it is)
                    if (!CornerFixed)
                    {
                        var step = p - _prev; float along = Vector2.Dot(step, DirVector(Dir1));
                        if (Vector2.Dot(p, DirVector(Dir1)) > _cornerProj && along > 0f && along >= OutwardShare * step.magnitude)
                        {
                            // still going out: the corner goes with the hand, and the stroke's direction may still settle
                            int d = Sector(p, Dir1); if (d != Dir1) { Dir1 = d; Step(); }
                            Corner = p; _cornerProj = Vector2.Dot(p, DirVector(Dir1));
                        }
                        if (_stillT >= DwellSeconds) { CornerFixed = true; Corner = p; }
                    }
                    var v = p - Corner;
                    if (v.magnitude >= StrokeLength) { Dir2 = Sector(v, -1); Phase = 2; Step(); }
                    break;
                }
                case 2:
                {
                    // (client wave 18, point 8: no cancel by the distance to the start: only back at the row's middle, below, gives the slot up)
                    var v = p - Corner;
                    if (v.magnitude < StrokeBack) { Phase = 1; Dir2 = -1; Step(); }
                    else { int d = Sector(v, Dir2); if (d != Dir2) { Dir2 = d; Step(); } }
                    break;
                }
            }
            _prev = p;
        }

        /// <summary>What letting go now takes: the row (0-7) and the slot (0-8, never 4); false: nothing (client wave 16: at the start or
        /// at a row's middle, a cancel).</summary>
        public bool TryResult(out int row, out int slot)
        {
            row = slot = -1;
            if (Phase != 2 || Dir1 < 0 || Dir2 < 0) return false;
            row = RowOfDir(Dir1); slot = CellOfDir(Dir2);
            return true;
        }
        /// <summary>Client wave 16 (w16cast): letting go now cancels: the hand is at the start or at a row's middle.</summary>
        public bool AtCentre => Phase != 2;
    }
}
