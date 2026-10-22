using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 12 (w12cast): the shape of the wand's two-tier wheel, as sketched on 2026-10-06, 21:40
    /// (design notes): where its segments and cells are, and which one the wand's tip is on. Pure
    /// geometry in the wheel's plane (metres from where the tip was when the trigger went down; x to the right, y up); the
    /// picture is UI/WandWheel.cs, the taking LokiGame.W12Wand.cs.
    ///   The main wheel  a white centre of <see cref="Centre"/> radius (nothing: letting go there takes nothing); four INNER
    ///                   segments (up, right, down, left) from the centre to <see cref="InnerEnd"/>: the wand's four quick
    ///                   slots; four OUTER segments in the same directions from <see cref="OuterStart"/> to
    ///                   <see cref="OuterEnd"/>: the four rows of the shown hotbar (up = row 1, right = row 2, down = row 3,
    ///                   left = row 4: clockwise from the top). Between the quadrants a gap of <see cref="Gap"/> along each
    ///                   diagonal, with parallel sides (nothing).
    ///   A sub-wheel     opened by touching an outer segment, <see cref="SubDistance"/> from the main wheel's centre on that
    ///                   segment's side (its rim <see cref="SubMargin"/> clear of the main wheel's): a white centre of
    ///                   <see cref="SubCentre"/> and, out to <see cref="SubEnd"/>, four wedges (up, right, down, left) each
    ///                   split in two along its axis = eight cells. Cell 1 is the right half of the upper wedge, and they go
    ///                   on clockwise (2, 3: the right wedge's upper and lower half; 4, 5: the lower wedge's right and left
    ///                   half; 6, 7: the left wedge's lower and upper half; 8: the upper wedge's left half): the row's slots
    ///                   1 to 8, the same on every side. Slot 9 of a row is not on the wheel.
    ///   Which one       <see cref="Next"/>: a region is entered when the tip is on its drawn shape and it is kept until the tip
    ///                   is more than <see cref="Keep"/> outside it (a tip trembling half a centimetre either side of a border
    ///                   does not flicker), with one exception, by the design's rule ("it opens immediately on contact"): from an
    ///                   inner segment, the tip touching an outer segment's drawn edge is on the outer one at once. The gaps
    ///                   are wider than <see cref="Keep"/>: the middle of a gap is nothing, whichever side the tip came from.
    ///   Slices          client wave 18 (w18wand), point 14 (design note: "Provide the ability to control how many slices the ring menu has. Up to
    ///                   a maximum of eight"; "the default amount is still four"): the INNER ring has <see cref="Slices"/> segments
    ///                   (VR/Comfort.cs WandSlices, 2-8), the circle divided evenly: slice i is centred <see cref="SliceAngle"/>(i) = i x 360 / n
    ///                   degrees clockwise from straight up (slice 1 at the top), the same gaps (<see cref="Gap"/>, parallel sides) between
    ///                   neighbours. Four slices are wave 12's four quadrants exactly (up, right, down, left). The outer ring stays the four
    ///                   rows of the shown bar in their four quadrants, whatever the inner ring's count.
    /// Regions are numbered: -1 nothing, 0-7 the inner slices (0 .. n-1 in use), 8-11 the outer segments, 12-19 the cells of the open
    /// sub-wheel (client wave 18: the inner ring has up to 8; wave 12-17 numbered them 0-3, 4-7, 8-15: every user names them by these
    /// constants).
    /// </summary>
    public static class WandWheelGeo
    {
        public const float Centre = 0.025f, InnerEnd = 0.070f, OuterStart = 0.074f, OuterEnd = 0.110f, Gap = 0.014f;
        public const float SubCentre = 0.026f, SubEnd = 0.090f, SubGap = 0.014f, Keep = 0.006f, SubMargin = 0.010f, SubDistance = OuterEnd + SubMargin + SubEnd;
        public const int None = -1, Inner = 0, Outer = 8, Cell = 12;
        /// <summary>Client wave 18 (w18wand), point 14: the inner ring's slices now (the setting, within 2-8).</summary>
        public static int Slices => Mathf.Clamp(VR.Comfort.WandSlices, VR.Comfort.WandSlicesMin, VR.Comfort.WandSlicesMax);
        /// <summary>The middle of inner slice i, degrees clockwise from straight up; its two edges (the lines through the middle at half a slice
        /// either side; each edge's gap is Gap / 2 in from it).</summary>
        public static float SliceAngle(int i) => i * 360f / Slices;
        public static float SliceA0(int i) => SliceAngle(i) - 180f / Slices;
        public static float SliceA1(int i) => SliceAngle(i) + 180f / Slices;
        /// <summary>Inner slice i's axis (four slices: exactly <see cref="Axis"/>).</summary>
        public static Vector2 SliceAxis(int i) => Slices == 4 ? Axis(i) : Dir(SliceAngle(i));

        /// <summary>The unit vector of an angle in degrees, clockwise from straight up.</summary>
        public static Vector2 Dir(float deg) { float a = deg * Mathf.Deg2Rad; return new Vector2(Mathf.Sin(a), Mathf.Cos(a)); }
        /// <summary>A direction's axis: 0 up, 1 right, 2 down, 3 left.</summary>
        public static Vector2 Axis(int d) { switch (d & 3) { case 0: return Vector2.up; case 1: return Vector2.right; case 2: return Vector2.down; default: return Vector2.left; } }
        /// <summary>The middle of the sub-wheel an outer segment opens.</summary>
        public static Vector2 SubCentreOf(int d) => Axis(d) * SubDistance;
        /// <summary>Where a tip rests on a region: the middle of an inner / outer segment, of cell c (0-7) of sub-wheel d.</summary>
        public static Vector2 InnerMid(int d) => SliceAxis(d) * ((Centre + InnerEnd) * 0.5f); // (client wave 18: slice d of the inner ring)
        public static Vector2 OuterMid(int d) => Axis(d) * ((OuterStart + OuterEnd) * 0.5f);
        public static Vector2 CellMid(int d, int c) => SubCentreOf(d) + Dir(c * 45f + 22.5f) * ((SubCentre + SubEnd) * 0.5f);
        /// <summary>How far a cell's two straight edges stand in from the lines through the sub-wheel's middle: the lower edge (at
        /// c x 45 degrees) and the upper one (45 more). An edge on an axis is the split of a wedge (a line, no gap); an edge on a
        /// diagonal is half the gap between two wedges.</summary>
        public static float CellOff0(int c) => (c & 1) == 0 ? 0f : SubGap * 0.5f;
        public static float CellOff1(int c) => (c & 1) == 0 ? SubGap * 0.5f : 0f;

        /// <summary>p is in the shape between the two lines through the middle at a0 and a1 degrees (clockwise from up, less than
        /// 180 apart), each moved inwards by off0 / off1, and between the radii r0 and r1; the shape grown by
        /// <paramref name="grow"/> on every side.</summary>
        public static bool InShape(Vector2 p, float a0, float a1, float off0, float off1, float r0, float r1, float grow)
        {
            float r = p.magnitude; if (r < r0 - grow || r > r1 + grow) return false;
            var d0 = Dir(a0); var d1 = Dir(a1);
            float s0 = p.x * d0.y - p.y * d0.x;      // how far clockwise of the line at a0
            float s1 = -(p.x * d1.y - p.y * d1.x);   // how far anticlockwise of the line at a1
            return s0 >= off0 - grow && s1 >= off1 - grow;
        }

        /// <summary>p is on that region's drawn shape, grown by <paramref name="grow"/>; sub: the open sub-wheel's direction (-1 none).</summary>
        public static bool On(int region, Vector2 p, int sub, float grow)
        {
            if (region < 0) return false;
            if (region < Outer) return region < Slices && InShape(p, SliceA0(region), SliceA1(region), Gap * 0.5f, Gap * 0.5f, Centre, InnerEnd, grow); // (client wave 18: n slices)
            if (region < Cell) { int d = region - Outer; return InShape(p, d * 90f - 45f, d * 90f + 45f, Gap * 0.5f, Gap * 0.5f, OuterStart, OuterEnd, grow); }
            if (sub < 0) return false;
            int c = region - Cell;
            return InShape(p - SubCentreOf(sub), c * 45f, c * 45f + 45f, CellOff0(c), CellOff1(c), SubCentre, SubEnd, grow);
        }

        /// <summary>The outer segment whose drawn shape p is on (0-3), else -1.</summary>
        public static int OuterAt(Vector2 p) { for (int d = 0; d < 4; d++) if (On(Outer + d, p, -1, 0f)) return d; return -1; }

        /// <summary>The region whose drawn shape p is on (-1: the centre, a gap, outside everything).</summary>
        public static int At(Vector2 p, int sub)
        {
            for (int r = 0; r < Cell; r++) if (On(r, p, sub, 0f)) return r;
            if (sub >= 0) for (int c = 0; c < 8; c++) if (On(Cell + c, p, sub, 0f)) return Cell + c;
            return None;
        }

        /// <summary>Client wave 14 (w14cast), point 14: "Wand rows: in place": the row's wheel (sub-wheel <paramref name="sub"/>) stands in
        /// the main wheel's place, its middle on the plane's origin (the tip's place when it replaced the main wheel): the cell the tip
        /// is on (kept as <see cref="Next"/> keeps one), else -1 (its middle, its gaps, outside it). Only its eight cells count.</summary>
        public static int NextInPlace(int current, Vector2 p, int sub)
        {
            if (sub < 0) return None;
            var q = p + SubCentreOf(sub); // (the sub-wheel's own shapes, moved so that its middle is the origin)
            if (current >= Cell && On(current, q, sub, Keep)) return current;
            for (int c = 0; c < 8; c++) if (On(Cell + c, q, sub, 0f)) return Cell + c;
            return None;
        }

        /// <summary>The region the tip is on now, given the one it was on; an outer segment touched opens its sub-wheel
        /// (<paramref name="sub"/> becomes its direction; the one open before is closed by that).</summary>
        public static int Next(int current, Vector2 p, ref int sub)
        {
            if (current >= 0 && On(current, p, sub, Keep))
            {
                if (current < Outer) { int o = OuterAt(p); if (o >= 0) { sub = o; return Outer + o; } } // contact with an outer segment: at once
                return current;
            }
            int r = At(p, sub);
            if (r >= Outer && r < Cell) sub = r - Outer;
            return r;
        }
    }
}
