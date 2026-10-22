using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 21 (w21tools): the look of the SEE-THROUGH HUD PIECES under THE PANEL THEME. design note, 2026-10-10: see-through HUD pieces
    /// "stay see-through and take only the theme's edges and lettering": "Yes". The pieces: the chat (UI/ChatPanel.cs), the forearm quick
    /// bar and the button strip (UI/QuickBar.cs), its elbow button and the chat button (LokiGame.W13View.cs), the minimap (UI/Minimap.cs),
    /// the buff bar (UI/BuffBar.cs), the notes and the armed-skill label (UI/LokiHud.cs), the PvP counter (Game/PvpState.cs) and the
    /// microphone marks (Voice/VoiceChat.cs, LokiGame.W13View.cs).
    ///   Edges      a piece that is a panel is made with PanelLook.SeeThrough (UI/UiKit.cs): the theme's thin gold line with its corner
    ///              scrolls in place of the dark panel's gold border.
    ///   Lettering  the theme's lettering on a see-through piece: cream (<see cref="Text"/>), headings and counts in its light gold
    ///              (<see cref="Heading"/>), each letter with the theme's dark outline (<see cref="Outline"/>; UiKit.Label adds it by itself
    ///              on such a piece).
    ///   Never      a cream surface over the world. The dark backing a piece had stays (<see cref="Backing"/>: the same colour and alpha):
    ///              its texts were written for it. What changes: the opaque gold box that lay UNDER the whole dark panel (the old border,
    ///              UI/UiKit.cs Dress Legacy) is gone, so the world shows a little through the backing (alpha 0.92) where it was hidden.
    /// FLAGGED (default): the backing keeps the dark panels' blue-black (<see cref="Back"/>); the theme's dark wood at the
    /// same alpha is one line here. The notes ("Mic on", a refusal ...) are a see-through piece by the wave's brief
    /// (<see cref="NotesLook"/>); a parchment card is PanelLook.Card here.
    /// </summary>
    public static class HudLook
    {
        /// <summary>The dark backing behind a see-through piece's lettering: the dark panels' own (UI/UiKit.cs PanelBg, alpha 0.92).</summary>
        public static Color Back => UiKit.PanelBg;
        /// <summary>Lettering on a see-through piece: the text, a heading or a count, a small dim number, a letter's outline.</summary>
        public static Color Text => Theme.OnClear;
        public static Color Heading => Theme.GoldLight;
        public static readonly Color Dim = new Color(0.78f, 0.72f, 0.60f, 1f), Outline = new Color(0.08f, 0.05f, 0.02f, 0.9f);
        /// <summary>A small mark drawn from boxes (the microphone): the lettering's cream at the mark's own alpha.</summary>
        public static Color Mark(float alpha) { var c = Theme.OnClear; c.a = alpha; return c; }
        /// <summary>The dark edge round a small HUD button (the elbow button, the chat button): the theme's dark gold.</summary>
        public static Color ButtonEdge(float alpha) { var c = Theme.GoldDark; c.a = alpha; return c; }
        /// <summary>The notes' look (see the class: flagged).</summary>
        /// <summary>The armed-skill label's and the PvP counter's look: w21kit 6d098122 made them parchment cards too (its smoke's D and E);
        /// PanelLook.SeeThrough here = this wave's brief (the gold line, the dark backing). Flagged.</summary>
        public static readonly PanelLook LabelLook = PanelLook.Card;
        public static readonly PanelLook NotesLook = PanelLook.Card; // (the merge with w21kit 6d098122: its notices are parchment cards, as specified's sentence says; PanelLook.SeeThrough here = this wave's brief)

        /// <summary>Gives a panel made with PanelLook.SeeThrough its dark backing back: the kit's hidden "bg" box, shown in
        /// <see cref="Back"/>, its edge under the gold line (<see cref="InsetOf"/> px inside the panel's rectangle). Returns it.</summary>
        public static Image Backing(RectTransform panel)
        {
            var bg = panel != null ? panel.Find("bg") as RectTransform : null; var img = bg != null ? bg.GetComponent<Image>() : null;
            if (img == null) return null;
            float inset = InsetOf(panel);
            bg.sizeDelta = panel.sizeDelta - new Vector2(2f * inset, 2f * inset);
            img.color = Back; img.enabled = true;
            bg.SetAsFirstSibling(); // (under the gold line: the kit puts "border" first and "bg" over it, which hid the line's inner half)
            return img;
        }
        /// <summary>How far inside a see-through panel's rectangle its gold line runs, px (the picture's line is 6 design px in: UI/Theme.cs
        /// MakeSeeFrame; never less than the 3 px the dark panel's border had).</summary>
        public static float InsetOf(RectTransform panel)
        {
            var s = panel != null ? panel.GetComponent<UiSurface>() : null;
            return Mathf.Max(3f, s != null ? 6f * s.Unit : 3f);
        }
    }
}
