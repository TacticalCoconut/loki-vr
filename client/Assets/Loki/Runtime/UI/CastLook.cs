using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 21 (w21tools): the look of the CASTING TOOLS (the pad UI/CastPad.cs, the flick UI/FlickHud.cs, the wand's wheel
    /// UI/WandWheel.cs, the Quick Use Bubbles UI/QuickBubble.cs). design note, 2026-10-10, on THE PANEL THEME's scope: "No, require them to be
    /// slightly stylized, but they need to be as SIMPLISTIC AS POSSIBLE, as they are meant to be utilitarian things rather than pretty.
    /// Provide a toggle that makes them look like the current super single design again in case require it." and "Keep the row colours in
    /// both looks."
    ///   The switch  Settings → Controls → Casting "Casting look: Themed / Plain" (VR/Comfort.cs CastPlain; Themed by default; kept with the
    ///               account). A friend's copy of a casting menu (UI/FriendGestures.cs) is drawn by these same classes on MY PC, so it
    ///               follows MY switch.
    ///   Plain       waves 12-20's look EXACTLY: <see cref="C"/> gives back the colour it is handed, untouched.
    ///   Themed      a PALETTE only: the same shapes, sizes, places and alphas, nothing added, nothing ornamental.
    ///                 the pad and the flick   a slot is a cream cell of the theme (a filled one its parchment, an empty one its darker
    ///                                         cell), mixed with its row's colour exactly as before; names and numbers in the theme's dark
    ///                                         ink and burnt sienna (a text that had a black outline gets a pale one: <see cref="State.Halo"/>);
    ///                                         the plates' dark rims the ink; the pad's back the theme's wood; the CHOSEN slot the dark wood with pale lettering;
    ///                 the wand's wheel        see-through as before: its tan pieces the theme's leather, its white middles its canvas, a
    ///                                         lit piece its gold, the outlines its ink, the lettering its cream;
    ///                 the bubbles             the glass in the canvas' cream (it was pale cyan), lit and ringed in the theme's golds.
    ///               A colour that is NOT in the table (<see cref="Build"/>) is drawn as it is: every ROW colour (UI/RowColours.cs), the
    ///               cancel mark's red, the wheel's cooldown seconds, a switched off bubble's grey.
    ///   How         a tool paints every constant through <see cref="C"/>; what it coloured once when it was built (labels, the texts'
    ///               outlines, fixed boxes and shapes) is remembered as plain by <see cref="Capture"/> and drawn again in the look in force
    ///               by <see cref="Sync"/>, which says when the tool must repaint what it paints itself.
    /// </summary>
    public static class CastLook
    {
        /// <summary>The plain look is chosen (Settings → Controls → Casting "Casting look: Plain").</summary>
        public static bool Plain => VR.Comfort.CastPlain;
        /// <summary>A number that names the look in force (a tool compares it with the one it was last drawn in).</summary>
        public const int PlainStamp = 1, ThemedStamp = 2;
        public static int Stamp => Plain ? PlainStamp : ThemedStamp;

        private static Dictionary<int, Color> _table;
        private static int Key(Color c) { Color32 k = c; return (k.r << 16) | (k.g << 8) | k.b; }
        private static void Add(Color plain, Color themed) { _table[Key(plain)] = themed; }
        private static Dictionary<int, Color> Build()
        {
            _table = new Dictionary<int, Color>();
            // a slot's fill (before its row's colour is mixed in): a filled one the theme's parchment, an empty one its darker cell; the pad's
            // back: the theme's wood
            Add(new Color(0.08f, 0.09f, 0.13f), Theme.Parchment);
            Add(new Color(0.035f, 0.04f, 0.055f), Theme.Cell);
            Add(UiKit.PanelBg, Theme.Wood);
            // the dark rims and outlines (the plates' rims, the wheel's pieces and fanning lines, a bubble's wait): the theme's ink
            Add(Color.black, Theme.Ink);
            Add(new Color(0.06f, 0.04f, 0.04f), Theme.Ink);
            // lettering on a cream cell (the pad's names, its slot numbers, the counts and numbers, a cooldown's seconds): ink, dim ink,
            // burnt sienna, dark red
            Add(UiKit.TextColor, Theme.Ink);
            Add(UiKit.Dim, Theme.InkDim);
            Add(new Color(1f, 0.85f, 0.45f), Theme.Accent);
            Add(new Color(1f, 0.6f, 0.45f), Theme.Bad);
            // lettering over the see-through wheel and in a bubble: the theme's cream; its counts: the light gold
            Add(new Color(1f, 0.93f, 0.78f), Theme.OnClear);
            Add(new Color(1f, 0.97f, 0.9f), Theme.OnClear);
            Add(new Color(1f, 0.95f, 0.8f), Theme.OnClear);
            Add(new Color(1f, 0.87f, 0.45f), Theme.GoldLight);
            // the strokes' line, the hand's and the tip's dot, a lit bubble: the light gold; the chosen slot, a lit piece of the wheel: the
            // lit gold; the bubble's ring and number: the gold
            Add(new Color(1f, 0.9f, 0.5f), Theme.GoldLight);
            Add(new Color(1f, 0.95f, 0.6f), Theme.GoldLight);
            Add(new Color(1f, 0.88f, 0.55f), Theme.GoldLight);
            Add(new Color(0.95f, 0.75f, 0.3f), Theme.Wood); // (the CHOSEN slot of the pad and the flick: dark among the cream cells, as the plain look's is bright among the dark ones: gold would be lost beside the orange and the yellow rows' cells)
            Add(new Color(1f, 0.84f, 0.45f), Theme.Lit);
            Add(new Color(1f, 0.85f, 0.4f), Theme.Lit);
            Add(OverheadMenu.Lit, Theme.Gold);
            // the wheel's tan: the theme's leather; its fanning lines' pale halo and the bubbles' glass: its canvas
            Add(new Color32(185, 122, 87, 255), Theme.Leather);
            Add(new Color(1f, 0.96f, 0.86f), Theme.Canvas);
            Add(new Color(0.72f, 0.92f, 1f), Theme.Canvas);
            return _table;
        }

        /// <summary>The colour a plain colour is drawn with in the look <paramref name="plainLook"/> (its alpha kept).</summary>
        public static Color For(Color plain, bool plainLook)
        {
            if (plainLook) return plain;
            var t = _table ?? Build();
            if (t.TryGetValue(Key(plain), out var c)) { c.a = plain.a; return c; }
            return plain;
        }
        /// <summary>The colour a plain colour is drawn with in the look in force.</summary>
        public static Color C(Color plain) => For(plain, Plain);
        /// <summary>The wheel's white middles (plain: white; themed: the theme's canvas) at <paramref name="alpha"/>. By name, never through
        /// the table: icons are white too.</summary>
        public static Color Pale(float alpha) { var c = Plain ? Color.white : Theme.Canvas; c.a = alpha; return c; }
        /// <summary>Themed lettering on a slot of the pad or the flick: on a cream cell, or (<paramref name="chosen"/>) on the CHOSEN slot,
        /// which is the theme's dark wood: a name, a number or an amount, the small slot number, a cooldown's seconds, a text's outline.</summary>
        public static Color Name(bool chosen) => chosen ? Theme.OnClear : Theme.Ink;
        public static Color Number(bool chosen) => chosen ? Theme.GoldLight : Theme.Accent;
        public static Color Small(bool chosen) => chosen ? new Color(0.78f, 0.72f, 0.60f, 1f) : Theme.InkDim;
        public static Color Wait(bool chosen) => chosen ? new Color(1f, 0.6f, 0.45f, 1f) : Theme.Bad;
        public static Color Outline(bool chosen, float alpha) { var c = chosen ? Theme.Ink : HaloColour; c.a = alpha; return c; }
        /// <summary>The pale outline a dark text gets in the themed look where the plain look gave a pale text a black one.</summary>
        public static Color HaloColour => Theme.Parchment;

        /// <summary>What a tool coloured once when it was built, remembered in its plain colours (on the tool's root).</summary>
        public sealed class State : MonoBehaviour
        {
            internal readonly List<(Graphic g, Color plain)> Graphics = new List<(Graphic, Color)>();
            internal readonly List<(Shadow s, Color plain)> Effects = new List<(Shadow, Color)>();
            internal readonly List<(WheelShape w, Color fill, Color edge, Color rim)> Shapes = new List<(WheelShape, Color, Color, Color)>();
            internal readonly HashSet<int> Known = new HashSet<int>();
            internal readonly Dictionary<int, Color> Own = new Dictionary<int, Color>();
            /// <summary>The look its remembered colours were last drawn in (<see cref="PlainStamp"/> at first: a tool is built plain).</summary>
            public int Stamp { get; internal set; } = PlainStamp;
            /// <summary>This tool's texts stand on cream cells in the themed look (the flick): a text's black outline becomes a pale one
            /// (<see cref="HaloColour"/>) there, for its letters are dark. False (the wheel: cream letters): the outline is the ink.</summary>
            public bool Halo { get; set; }
            /// <summary>How many things are remembered (smoke, log).</summary>
            public int Count => Graphics.Count + Effects.Count + Shapes.Count;
            internal Color Of(Graphic g, Color c, bool plain)
            {
                if (plain) return c;
                if (Own.TryGetValue(g.GetInstanceID(), out var o)) { o.a = c.a; return o; }
                return For(c, false);
            }
            internal Color OfEffect(Color c, bool plain)
            {
                if (plain) return c;
                if (Halo && Key(c) == 0) { var h = HaloColour; h.a = c.a; return h; }
                return For(c, false);
            }
        }

        /// <summary>Remembers the colour of every Graphic (and text outline) under <paramref name="root"/> that is not yet remembered, as
        /// PLAIN: call it right after the tool (or a new part of it) was built with its plain constants, before anything paints it.
        /// <paramref name="drawNew"/>: the newly remembered ones are drawn at once in the look the others were last drawn in (a part built
        /// later: the wheel's slices; the others are not touched).</summary>
        public static State Capture(Transform root, bool drawNew = false)
        {
            var st = root.GetComponent<State>(); if (st == null) st = root.gameObject.AddComponent<State>();
            st.Graphics.RemoveAll(e => e.g == null); st.Effects.RemoveAll(e => e.s == null); st.Shapes.RemoveAll(e => e.w == null);
            bool draw = drawNew && st.Stamp != PlainStamp;
            foreach (var g in root.GetComponentsInChildren<Graphic>(true))
            {
                if (!st.Known.Add(g.GetInstanceID())) continue;
                if (g is WheelShape w)
                {
                    Color f = w.FillColour, e = w.EdgeColour, r = w.RimColour; st.Shapes.Add((w, f, e, r));
                    if (draw) w.Paint(For(f, false), For(e, false), For(r, false));
                }
                else { var c = g.color; st.Graphics.Add((g, c)); if (draw) g.color = st.Of(g, c, false); }
                foreach (var s in g.GetComponents<Shadow>()) { var c = s.effectColor; st.Effects.Add((s, c)); if (draw) s.effectColor = st.OfEffect(c, false); } // (an Outline is a Shadow)
            }
            return st;
        }

        /// <summary>One remembered thing has a themed colour of its own (not the table's): the pad's heading stands on its wood back, not on
        /// a cream cell.</summary>
        public static void Own(State st, Graphic g, Color themed) { if (st != null && g != null) st.Own[g.GetInstanceID()] = themed; }

        /// <summary>Draws what <paramref name="st"/> remembers in the look in force when that changed since it last did. True: it did, and
        /// the tool must repaint what it paints itself.</summary>
        public static bool Sync(State st)
        {
            if (st == null) return false;
            int now = Stamp; if (st.Stamp == now) return false;
            st.Stamp = now; bool plain = now == PlainStamp;
            foreach (var (g, c) in st.Graphics) { if (g == null) continue; var k = st.Of(g, c, plain); if (g.color != k) g.color = k; }
            foreach (var (s, c) in st.Effects) { if (s == null) continue; var k = st.OfEffect(c, plain); if (s.effectColor != k) s.effectColor = k; }
            foreach (var (w, f, e, r) in st.Shapes) if (w != null) w.Paint(For(f, plain), For(e, plain), For(r, plain));
            return true;
        }
    }
}
