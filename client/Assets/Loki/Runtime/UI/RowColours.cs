using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 20 (w20colors), the design's point 51 ("Give each one of the submenus for the flick a color pattern so that I can know at a glance
    /// what submenu I have opened") and the design's answer of 2026-10-10 (the same colours on the pad's rows and the wand wheel's rows: "Yes"): the
    /// ONE colour of each hotkey row (0-7: bar 1 rows 1-4, bar 2 rows 1-4), the same wherever a row is drawn: the flick (UI/FlickHud.cs), the
    /// pad (UI/CastPad.cs), the wand's wheel (UI/WandWheel.cs), the quick-slot editor (UI/WristMenu.W20Colors.cs), and a friend's copy of
    /// each (UI/FriendGestures.cs draws them with the same classes and the friend's row numbers: the colour depends on the row alone).
    ///   The set   Okabe &amp; Ito's colour-blind-safe set ("Color Universal Design", 2002) as R ships it (grDevices palette.colors
    ///             "Okabe-Ito"), black left out (it cannot be seen on the dark plates), its grey kept: eight colours.
    ///   The order which row gets which: of the 8! orders, the one whose closest pair among the pairs drawn side by side (the flick's
    ///             neighbouring rows round the ring, and every two rows of one bar: the wand wheel shows a bar's four at once) is the least
    ///             alike, by CIEDE2000, for normal colour vision and simulated protanopia, deuteranopia and tritanopia (Machado et al. 2009,
    ///             severity 1): that closest pair is 13.9 apart (rows 5 / 8 for tritanopia); the published order would put two 6.2 apart.
    /// </summary>
    public static class RowColours
    {
        /// <summary>The colours of rows 1-8 (index 0-7).</summary>
        public static readonly Color[] Palette =
        {
            new Color32(0xE6, 0x9F, 0x00, 255), // row 1 (bar 1 row 1): orange
            new Color32(0x99, 0x99, 0x99, 255), // row 2 (bar 1 row 2): grey
            new Color32(0x56, 0xB4, 0xE9, 255), // row 3 (bar 1 row 3): sky blue
            new Color32(0x00, 0x72, 0xB2, 255), // row 4 (bar 1 row 4): blue
            new Color32(0xD5, 0x5E, 0x00, 255), // row 5 (bar 2 row 1): vermillion
            new Color32(0xF0, 0xE4, 0x42, 255), // row 6 (bar 2 row 2): yellow
            new Color32(0x00, 0x9E, 0x73, 255), // row 7 (bar 2 row 3): bluish green
            new Color32(0xCC, 0x79, 0xA7, 255), // row 8 (bar 2 row 4): reddish purple
        };
        /// <summary>The colours' names (the log).</summary>
        public static readonly string[] Names = { "orange", "grey", "sky blue", "blue", "vermillion", "yellow", "bluish green", "reddish purple" };

        /// <summary>Row <paramref name="row"/>'s colour (opaque); any other number: white.</summary>
        public static Color Of(int row) => row >= 0 && row < Palette.Length ? Palette[row] : Color.white;
        /// <summary><paramref name="basis"/> taken <paramref name="k"/> of the way to row <paramref name="row"/>'s colour, its alpha kept.</summary>
        public static Color Tint(Color basis, int row, float k) { var c = Color.Lerp(basis, Of(row), k); c.a = basis.a; return c; }
        /// <summary>Row <paramref name="row"/>'s colour at <paramref name="alpha"/>.</summary>
        public static Color Of(int row, float alpha) { var c = Of(row); c.a = alpha; return c; }
        /// <summary>Row <paramref name="row"/>'s colour made lighter (towards white by <paramref name="k"/>) at <paramref name="alpha"/>: a lit piece.</summary>
        public static Color Lit(int row, float k, float alpha) { var c = Color.Lerp(Of(row), Color.white, k); c.a = alpha; return c; }
        /// <summary>Row <paramref name="row"/>'s colour times <paramref name="k"/> (darker), opaque.</summary>
        public static Color Dark(int row, float k) { var c = Of(row) * k; c.a = 1f; return c; }
    }
}
