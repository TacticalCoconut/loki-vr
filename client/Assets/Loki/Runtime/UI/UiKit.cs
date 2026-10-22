using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Minimal world-space UI toolkit on top of uGUI (no EventSystem): panels are world-space canvases measured in pixels,
    /// buttons carry a BoxCollider so the VR pointer ray (a plain Physics.Raycast) can hover and click them.
    /// Text uses a dynamic OS font so Portuguese accents and Korean both render.
    /// </summary>
    public static class UiKit
    {
        private static Font _font;
        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    try { _font = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial", "Malgun Gothic", "Tahoma" }, 32); }
                    catch (Exception e) { Debug.LogWarning("[UI] OS font failed: " + e.Message); }
                    if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                return _font;
            }
        }

        private static Material _overlay;
        /// <summary>UI/Default with ZTest Always (Resources/Shaders/LokiUIOverlay.shader): UI is never hidden inside the diorama.</summary>
        public static Material Overlay
        {
            get
            {
                if (_overlay == null) { var sh = Shader.Find("Loki/UIOverlay"); if (sh != null) _overlay = new Material(sh) { name = "LokiUIOverlay" }; else Debug.LogWarning("[UI] Loki/UIOverlay shader missing"); }
                return _overlay;
            }
        }

        /// <summary>Client wave 5 (w5weather): the layer every panel is on, Unity's built-in layer 5 "UI", so the desktop spectator camera
        /// can leave the player's floating panels out (World/SpectatorView.cs); the head camera and screenshots draw every layer.</summary>
        public const int PanelLayer = 5;
        /// <summary>Every panel made by <see cref="Panel"/> (destroyed ones are dropped by the spectator view's sweep).</summary>
        public static readonly System.Collections.Generic.List<Transform> Panels = new System.Collections.Generic.List<Transform>();

        // The DARK panel's colours (waves 1-20): what a Legacy panel and a bare HUD piece still use. On the theme's surfaces the colours
        // are UI/Theme.cs's (Theme.Ink, Theme.Cell, Theme.Lit ...): a text given one of these there is drawn in the matching ink.
        public static readonly Color PanelBg = new Color(0.07f, 0.08f, 0.12f, 0.92f);
        public static readonly Color PanelBorder = new Color(0.85f, 0.7f, 0.35f, 1f);
        public static readonly Color ButtonBg = new Color(0.18f, 0.22f, 0.32f, 1f);
        public static readonly Color ButtonHover = new Color(0.35f, 0.45f, 0.7f, 1f);
        public static readonly Color TextColor = new Color(0.95f, 0.95f, 0.92f, 1f);
        public static readonly Color Dim = new Color(0.7f, 0.72f, 0.78f, 1f);

        /// <summary>Creates a world-space canvas of sizePx pixels, metresPerPx big. Client wave 21 (w21kit): its picture is THE PANEL THEME's
        /// (UI/Theme.cs): <paramref name="look"/> Auto = the wood frame and the cream canvas when it has a background (the names in
        /// Theme.LegacyNames keep the dark panel of waves 1-20), nothing when it has none; Card = a parchment card; SeeThrough = the gold
        /// line and scrolls only. The frame stands OUT of the rectangle (<see cref="OutsetOf"/>): the rectangle is the canvas, so a
        /// window's layout, its texts and its buttons stay where and as large as they were. The children "border" and "bg" keep their
        /// names and sizes (sizePx and sizePx - 6): code that resizes them resizes the frame and the canvas.</summary>
        public static RectTransform Panel(string name, Transform parent, Vector2 sizePx, float metresPerPx, bool background = true, PanelKind kind = PanelKind.ByName, PanelLook look = PanelLook.Auto)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = PanelLayer; if (Panels.Count >= 256) Panels.RemoveAll(t => t == null); Panels.Add(go.transform); // client wave 5 (w5weather): out of the spectator camera's view
            var canvas = go.AddComponent<Canvas>();
            PanelKinds.Register(name, canvas, kind); // client wave 13 (w13resize): a menu, an always-there panel, or the casting's own picture
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 10;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = sizePx;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one * metresPerPx;
            if (look == PanelLook.Auto) look = !background ? PanelLook.None : Theme.LegacyNames.Contains(name) ? PanelLook.Legacy : PanelLook.Framed;
            Dress(rt, sizePx, metresPerPx, look);
            if (background)
            {
                // A collider behind the whole panel so the pointer ray stops on it instead of clicking the map behind. (Client wave 21: it is
                // the RECTANGLE's, as always, not the frame's: the wood frame lies where the grab band is, from the rim to 4 cm outside it
                // (UI/PanelGrab.cs BandOutside): the laser or a hand on the frame takes the panel, as it did beside a dark panel's rim.)
                var col = go.AddComponent<BoxCollider>();
                col.size = new Vector3(sizePx.x, sizePx.y, 4f);
                col.center = new Vector3(0, 0, 3f);
                go.AddComponent<UiBlocker>();
            }
            return rt;
        }

        /// <summary>Client wave 21 (w21kit): gives <paramref name="box"/> (a panel's root or a part of one: a board's facet, an item box) the
        /// picture of <paramref name="look"/>: the children "border" (sizePx) and "bg" (sizePx - 6) and, for a themed look, the
        /// <see cref="UiSurface"/> that tells its texts and buttons what they stand on. Returns how far the frame stands out of the
        /// rectangle, px (Framed only).</summary>
        public static float Dress(RectTransform box, Vector2 sizePx, float metresPerPx, PanelLook look)
        {
            if (look == PanelLook.None || look == PanelLook.Auto) return 0f;
            if (look == PanelLook.Legacy)
            {
                var lb = Box(box, "border", Vector2.zero, sizePx, PanelBorder); lb.SetAsFirstSibling();
                var lg = Box(box, "bg", Vector2.zero, sizePx - new Vector2(6, 6), PanelBg); lg.SetSiblingIndex(1);
                return 0f;
            }
            var surf = box.GetComponent<UiSurface>(); if (surf == null) surf = box.gameObject.AddComponent<UiSurface>();
            float m = Mathf.Max(1e-6f, Mathf.Abs(metresPerPx)), side = Mathf.Min(sizePx.x, sizePx.y);
            if (look == PanelLook.Framed)
            {
                float u = Theme.Unit(sizePx, metresPerPx);
                surf.Kind = Surface.Canvas; surf.Unit = u; surf.Outset = Theme.FrameOutset(u);
                var border = Picture(box, "border", sizePx, Theme.Frame, u, surf.Outset, false); border.SetAsFirstSibling();
                border.GetComponent<OutsetImage>().JoinPad = FoldLap;
                var bg = Picture(box, "bg", sizePx - new Vector2(6, 6), Theme.CanvasFill, u, 3f + Theme.LeatherPx * u, true); bg.SetSiblingIndex(1);
                bg.GetComponent<OutsetImage>().JoinPad = 3f + FoldLap;
                var st = Stitch(bg, Vector2.zero, Vector2.zero, u); // along the canvas' edge, inside it
                st.rectTransform.anchorMin = Vector2.zero; st.rectTransform.anchorMax = Vector2.one; st.rectTransform.offsetMin = new Vector2(-3, -3); st.rectTransform.offsetMax = new Vector2(3, 3);
                st.Inset = Theme.StitchInPx * u;
                return surf.Outset;
            }
            if (look == PanelLook.Card)
            {
                float u = Mathf.Min(0.0035f / m / Theme.CardShadowPx, 0.3f * side / 22f); // the shadow 3.5 mm wide; a small card's edge at most 30 % of it
                surf.Kind = Surface.Card; surf.Unit = u; surf.Outset = 0f;
                var border = Picture(box, "border", sizePx, Theme.CardFill, u, Theme.CardShadowPx * u, true); border.SetAsFirstSibling();
                var bg = Box(box, "bg", Vector2.zero, sizePx - new Vector2(6, 6), new Color(1, 1, 1, 0)); bg.SetSiblingIndex(1); bg.GetComponent<Image>().enabled = false;
                return 0f;
            }
            {
                float u = Mathf.Min(0.016f / m / 46f, 0.35f * side / 46f); // SeeThrough: the corner 1.6 cm; at most 35 % of a small piece
                surf.Kind = Surface.Clear; surf.Unit = u; surf.Outset = 0f;
                var border = Picture(box, "border", sizePx, Theme.SeeFrame, u, 0f, false); border.SetAsFirstSibling();
                var bg = Box(box, "bg", Vector2.zero, sizePx - new Vector2(6, 6), new Color(1, 1, 1, 0)); bg.SetSiblingIndex(1); bg.GetComponent<Image>().enabled = false;
                return 0f;
            }
        }

        /// <summary>Client wave 21 (w21kit): a framed box joined to a neighbour on <paramref name="sides"/> (a folded board's facets): no
        /// frame, leather or stitching on those sides; the other sides' run to the fold. Sides.None gives the whole frame back.</summary>
        public static void Join(RectTransform box, Sides sides)
        {
            if (box == null) return;
            var b = box.Find("border"); var g = box.Find("bg");
            var bi = b != null ? b.GetComponent<OutsetImage>() : null; if (bi != null) bi.Join = sides;
            var gi = g != null ? g.GetComponent<OutsetImage>() : null; if (gi != null) gi.Join = sides;
            var st = g != null ? g.GetComponentInChildren<StitchGraphic>(true) : null; if (st != null) st.Skip = sides;
        }

        /// <summary>Client wave 21: at a joined side (a fold) the frame and the canvas run this many px past the box's edge, so two facets
        /// leave no slit between them.</summary>
        public const float FoldLap = 5f;
        /// <summary>How far the frame of the panel <paramref name="t"/> is on stands out of its rectangle, px (0: no frame).</summary>
        public static float OutsetOf(Transform t) { var s = Theme.SurfaceOf(t); return s != null ? s.Outset : 0f; }

        /// <summary>A 9-slice picture of the theme, <paramref name="unit"/> panel px per design px, standing <paramref name="outset"/> px out
        /// of its rectangle.</summary>
        public static RectTransform Picture(RectTransform parent, string name, Vector2 size, Sprite sprite, float unit, float outset = 0f, bool fillCenter = true, Vector2 pos = default)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var img = go.AddComponent<OutsetImage>();
            img.raycastTarget = false; img.sprite = sprite; img.color = Color.white;
            img.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            img.fillCenter = fillCenter; img.pixelsPerUnitMultiplier = 1f / Mathf.Max(0.01f, unit); img.Outset = outset;
            if (Overlay != null) img.material = Overlay;
            JobTheme.NotifyPicture(parent);
            return rt;
        }

        /// <summary>Dashed stitching along the edges of a rectangle (the theme's leather-brown thread), <paramref name="unit"/> as the panel's.</summary>
        public static StitchGraphic Stitch(RectTransform parent, Vector2 pos, Vector2 size, float unit = 0f)
        {
            if (unit <= 0f) { var s = Theme.SurfaceOf(parent); unit = s != null ? s.Unit : 0.45f; }
            var go = new GameObject("stitch", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var st = go.AddComponent<StitchGraphic>();
            st.raycastTarget = false; st.color = Theme.Thread; // (client wave 21, w21jobui: the leather brown, or the job variant's thread)
            st.Dash = Theme.StitchPx * unit; st.Gap = Theme.StitchGapPx * unit; st.Wide = Mathf.Max(1.6f, Theme.StitchWidePx * unit);
            if (Overlay != null) st.material = Overlay;
            JobTheme.NotifyPicture(parent);
            return st;
        }
        /// <summary>One dashed line (a divider between two sections): from <paramref name="a"/> to <paramref name="b"/>, level or upright.</summary>
        public static StitchGraphic StitchLine(RectTransform parent, Vector2 a, Vector2 b)
        {
            bool level = Mathf.Abs(b.x - a.x) >= Mathf.Abs(b.y - a.y);
            var st = Stitch(parent, (a + b) * 0.5f, level ? new Vector2(Mathf.Abs(b.x - a.x), 8f) : new Vector2(8f, Mathf.Abs(b.y - a.y)));
            st.Line = true; return st;
        }

        /// <summary>A parchment card on a panel (an item's details, a note): the texts and buttons made under it stand on parchment.</summary>
        public static RectTransform Card(RectTransform parent, string name, Vector2 pos, Vector2 size)
        {
            var s = Theme.SurfaceOf(parent); float u = Mathf.Min(s != null ? s.Unit * 1.6f : 0.7f, 0.3f * Mathf.Min(size.x, size.y) / 22f);
            var rt = Picture(parent, name, size, Theme.CardFill, u, Theme.CardShadowPx * u, true, pos);
            var surf = rt.gameObject.AddComponent<UiSurface>(); surf.Kind = Surface.Card; surf.Unit = u;
            return rt;
        }

        /// <summary>A soft square (an item's cell, a row's band): the theme's rounded box, tinted <paramref name="color"/>.</summary>
        public static RectTransform Soft(RectTransform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var rt = Box(parent, name, pos, size, color);
            var img = rt.GetComponent<Image>(); img.sprite = Theme.Soft; img.type = Image.Type.Sliced;
            return rt;
        }
        /// <summary>An item's cell: a soft square a little darker than the canvas with a thin rim.</summary>
        public static RectTransform Cell(RectTransform parent, string name, Vector2 pos, Vector2 size)
        {
            var rt = Soft(parent, name, pos, size, Theme.Cell);
            Rim(rt, Theme.SoftRim, Theme.CellRim);
            return rt;
        }
        /// <summary>The gold outline of a hovered thing, as large as <paramref name="around"/> (made hidden: enable it while the laser or
        /// the look is on the thing). A button made by <see cref="Button"/> has its own.</summary>
        public static Image HoverOutline(RectTransform around)
        {
            var img = Rim(around, Theme.HoverRim, Color.white);
            img.name = "hover"; img.enabled = false;
            return img;
        }
        private static Image Rim(RectTransform around, Sprite sprite, Color color)
        {
            var rt = Box(around, "rim", Vector2.zero, Vector2.zero, color);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = rt.GetComponent<Image>(); img.sprite = sprite; img.type = Image.Type.Sliced;
            return img;
        }

        /// <summary>The small gold title plate on the top edge of a framed panel (in the middle of its wood), as wide as its text.</summary>
        public static UiTitlePlate TitlePlate(RectTransform panel, string text, int fontSize = 34, float maxWidth = 700f)
        {
            var s = Theme.SurfaceOf(panel); float u = s != null ? s.Unit : 0.45f;
            float h = fontSize + 22f;
            float up = s != null && s.Outset > 0f ? (Theme.LeatherPx + Theme.WoodPx * 0.5f) * u : -h * 0.5f - 4f; // no frame: just inside the top edge
            var rt = Picture(panel, "plate", new Vector2(maxWidth, h), Theme.Plate, Mathf.Clamp(h / 48f, 0.6f, 1.4f), 0f, true);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.anchoredPosition = new Vector2(0f, up);
            var surf = rt.gameObject.AddComponent<UiSurface>(); surf.Kind = Surface.Gold; surf.Unit = u;
            var t = Label(rt, text ?? "", Vector2.zero, new Vector2(maxWidth - 60f, h - 6f), fontSize, TextAnchor.MiddleCenter, Theme.Ink);
            t.fontStyle = FontStyle.Bold; t.horizontalOverflow = HorizontalWrapMode.Wrap;
            var plate = rt.gameObject.AddComponent<UiTitlePlate>();
            plate.MaxW = maxWidth; plate.Init(rt, t, h); plate.Fit();
            return plate;
        }

        /// <summary>A gold ring with a dark line icon in it (a section's header): set <see cref="UiRingIcon.Icon"/>'s sprite, or give a glyph.</summary>
        public static UiRingIcon RingIcon(RectTransform parent, Vector2 pos, float diameter, string glyph = null)
        {
            var rt = Box(parent, "ring", pos, new Vector2(diameter, diameter), Color.white);
            rt.GetComponent<Image>().sprite = Theme.Ring;
            var r = rt.gameObject.AddComponent<UiRingIcon>();
            r.Ring = rt.GetComponent<Image>();
            r.Icon = Icon(rt, Vector2.zero, new Vector2(diameter * 0.6f, diameter * 0.6f)); r.Icon.color = Theme.Ink;
            r.Glyph = Label(rt, glyph ?? "", Vector2.zero, new Vector2(diameter * 0.8f, diameter * 0.8f), Mathf.RoundToInt(diameter * 0.5f), TextAnchor.MiddleCenter, Theme.Ink);
            return r;
        }

        /// <summary>Solid rectangle. pos is the centre in the parent's pixel space (origin = parent centre, y up).</summary>
        public static RectTransform Box(RectTransform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer; // client wave 5 (w5weather): a panel's parts share its layer
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var img = go.AddComponent<ThemedImage>(); // client wave 21 (w21kit): on the theme's light surfaces a dark box is drawn as its light counterpart (UI/Theme.cs Theme.Fill)
            img.color = color; img.raycastTarget = false;
            if (Overlay != null) img.material = Overlay;
            return rt;
        }

        /// <summary>Client wave 21 (w21kit): this box's colour is DATA (a bar, a row's colour, a swatch): drawn as it is on the theme's
        /// surfaces too (UI/Theme.cs ThemedImage.Raw). Returns the box.</summary>
        public static RectTransform Raw(RectTransform box) { if (box != null && box.GetComponent<Image>() is ThemedImage ti) ti.Raw = true; return box; }

        /// <summary>Client wave 21 (w21kit): everything made under this box keeps the colours its code gives it (a map's picture with marks
        /// and names on it, a portrait): the box is its own surface, outside the theme. Returns the box.</summary>
        public static RectTransform OwnColours(RectTransform box)
        {
            if (box == null) return box;
            var s = box.GetComponent<UiSurface>(); if (s == null) s = box.gameObject.AddComponent<UiSurface>();
            s.Kind = Surface.None; s.Outset = 0f;
            return box;
        }

        /// <summary>Text block; pos/size as in Box. Rich text on.</summary>
        public static Text Label(RectTransform parent, string text, Vector2 pos, Vector2 size, int fontSize, TextAnchor align = TextAnchor.UpperLeft, Color? color = null)
        {
            var go = new GameObject("text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer; // client wave 5 (w5weather)
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            // client wave 21 (w21kit): on a surface of the theme the text takes its ink, and every letter is drawn readable on what is
            // behind it whatever colour the code gives it (UI/Theme.cs ThemedText); a see-through piece's lettering gets a dark edge
            var surf = Theme.SurfaceOf(parent);
            var t = go.AddComponent<ThemedText>();
            t.font = Font; t.fontSize = fontSize; t.alignment = align; t.supportRichText = true;
            t.color = color ?? Theme.TextOn(surf); t.raycastTarget = false;
            if (surf != null && surf.Kind == Surface.Clear) { var o = go.AddComponent<Outline>(); o.effectColor = new Color(0.08f, 0.05f, 0.02f, 0.9f); o.effectDistance = new Vector2(1.3f, -1.3f); }
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            if (Overlay != null) t.material = Overlay;
            t.text = text;
            Lang.Track(t); // client wave 4b (w4lang): a fixed text follows a change of language
            return t;
        }

        public static UiButton Button(RectTransform parent, string label, Vector2 pos, Vector2 size, Action onClick, int fontSize = 26)
        {
            // w4lang: the object keeps the English name ("btn:Close") in both languages; the label may shrink to fit its button
            // (best fit down to 70 % of its size: a Portuguese label is often a little longer than the English one)
            // client wave 21 (w21kit): on canvas or parchment it is the theme's plain button: a soft square with a thin rim, dark ink;
            // under the laser or the look it turns light and gets the gold outline (UiButton.SetHover)
            var surf = Theme.SurfaceOf(parent); bool themed = surf != null && surf.Light;
            var rt = themed ? Soft(parent, "btn:" + Lang.SourceOf(label), pos, size, Theme.Cell) : Box(parent, "btn:" + Lang.SourceOf(label), pos, size, ButtonBg);
            var rim = themed ? Rim(rt, Theme.SoftRim, Theme.CellRim) : null;
            var t = Label(rt, label, Vector2.zero, size - new Vector2(12, 4), fontSize, TextAnchor.MiddleCenter);
            t.resizeTextForBestFit = true; t.resizeTextMaxSize = fontSize; t.resizeTextMinSize = Mathf.Max(10, Mathf.RoundToInt(fontSize * 0.7f));
            var col = rt.gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(size.x, size.y, 8f);
            col.center = new Vector3(0, 0, -2f);
            var b = rt.gameObject.AddComponent<UiButton>();
            b.Init(rt.GetComponent<Image>(), t, onClick);
            if (themed) b.Themed(rim, surf, Theme.Cell, Theme.CellHover);
            return b;
        }

        /// <summary>Client wave 21 (w21kit): the theme's small round gold button (close, page arrows, sort ...): a gold disc with a dark
        /// glyph. Its laser collider is the whole <paramref name="size"/> (never smaller than the square button it replaces); the disc is
        /// as large as the smaller side. Off the theme (a dark panel) it is the plain button.</summary>
        public static UiButton RoundButton(RectTransform parent, string label, Vector2 pos, Vector2 size, Action onClick, int fontSize = 26)
        {
            var surf = Theme.SurfaceOf(parent);
            if (surf == null || !surf.Themed || surf.Kind == Surface.Clear) return Button(parent, label, pos, size, onClick, fontSize);
            var rt = Box(parent, "btn:" + Lang.SourceOf(label), pos, size, new Color(1, 1, 1, 0));
            rt.GetComponent<Image>().enabled = false;
            var disc = Box(rt, "disc", Vector2.zero, Vector2.zero, Theme.Lit);
            disc.anchorMin = Vector2.zero; disc.anchorMax = Vector2.one; disc.offsetMin = disc.offsetMax = Vector2.zero;
            var img = disc.GetComponent<Image>(); img.sprite = Theme.Disc; img.preserveAspect = true;
            var t = Label(disc, label, Vector2.zero, size - new Vector2(12, 4), fontSize, TextAnchor.MiddleCenter, Theme.Ink);
            t.fontStyle = FontStyle.Bold;
            t.resizeTextForBestFit = true; t.resizeTextMaxSize = fontSize; t.resizeTextMinSize = Mathf.Max(10, Mathf.RoundToInt(fontSize * 0.7f));
            var col = rt.gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(size.x, size.y, 8f);
            col.center = new Vector3(0, 0, -2f);
            var b = rt.gameObject.AddComponent<UiButton>();
            b.Init(img, t, onClick);
            b.Themed(null, surf, Theme.Lit, Theme.LitHover); b.Round = true;
            return b;
        }

        public static Image Icon(RectTransform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("icon", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer; // client wave 5 (w5weather)
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.raycastTarget = false; img.preserveAspect = true; img.enabled = false;
            if (Overlay != null) img.material = Overlay;
            return img;
        }

        public static Sprite ToSprite(Texture2D tex) => tex == null ? null : Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);

        private static readonly System.Text.RegularExpressions.Regex RichTag = new System.Text.RegularExpressions.Regex(@"</?(color|size|b|i)(=[^>]*)?>", System.Text.RegularExpressions.RegexOptions.Compiled);
        /// <summary>Removes the uGUI rich-text tags Loki itself adds to names ("Faca <color=#9aa3b5>(Knife)</color>" → "Faca (Knife)"): the chat log shows text literally.</summary>
        public static string StripTags(string s) => string.IsNullOrEmpty(s) ? s ?? "" : RichTag.Replace(s, "");

        /// <summary>RO text colour codes (^RRGGBB) → uGUI rich text; also escapes stray '<' so it is not parsed as a tag.
        /// Client wave 13, item 21 (w13grab): every colour is made READABLE on Loki's dark panels (<see cref="ReadableHex"/>): the codes
        /// were written for the official client's white windows (^0000FF blue, ^777777 grey ... nearly vanish on <see cref="PanelBg"/>).
        /// The one place for the item panels and cards (UI/ItemCard.cs, UI/ItemPanel.cs), the classic item details (UI/WristMenu.cs,
        /// WristMenu.W4Items.cs), NPC dialogs (Game/NpcDialog.cs), the chat and the speech bubbles; the skill descriptions use the same
        /// rule (UI/WristMenu.Skills.cs Lighten).</summary>
        public static string RoColors(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 32);
            bool open = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '^' && i + 6 < s.Length && IsHex(s, i + 1, 6))
                {
                    string hex = s.Substring(i + 1, 6);
                    if (open) sb.Append("</color>");
                    if (hex.Equals("000000", StringComparison.OrdinalIgnoreCase)) open = false; // ^000000 = back to default
                    else { sb.Append("<color=#").Append(ReadableHex(hex)).Append('>'); open = true; }
                    i += 6; continue;
                }
                if (c == '<') { sb.Append("<​"); continue; }
                sb.Append(c);
            }
            if (open) sb.Append("</color>");
            return sb.ToString();
        }

        private static bool IsHex(string s, int start, int n)
        {
            for (int i = start; i < start + n; i++) { char c = s[i]; if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false; }
            return true;
        }

        // ---- client wave 13, item 21 (w13grab; the coordinator's addition, documented): RO's colour codes readable on the dark panels
        /// <summary>The least relative luminance (WCAG 2.x: sRGB made linear, 0.2126 R + 0.7152 G + 0.0722 B) a text colour of the game data
        /// is drawn with: 0.35, against <see cref="PanelBg"/> (#12141F, luminance 0.0074) a contrast of (0.35 + 0.05) / (0.0074 + 0.05)
        /// = 7.0 : 1 (WCAG's AAA level for small text; AA asks 4.5 : 1): VR letters of ~1.3° are softer than a monitor's. The same rule
        /// the skill descriptions have used since client wave 3 (UI/WristMenu.Skills.cs), now everywhere.</summary>
        public const float ReadableLuminance = 0.35f;
        /// <summary>#RRGGBB → the same colour mixed towards white in 5 % steps until its relative luminance is at least
        /// <see cref="ReadableLuminance"/>: its hue (HSV) is kept exactly, only lightened; colours already that bright are left alone.
        /// E.g. 0000FF → 9999FF (2.1 → 7.3 : 1), 777777 → A0A0A0 (4.1 → 7.0), FF0000 → FF8080 (4.6 → 7.5), 00FF00 unchanged (13.3).</summary>
        public static string ReadableHex(string hex)
        {
            if (hex == null || hex.Length != 6 || !IsHex(hex, 0, 6)) return hex;
            float r = Convert.ToInt32(hex.Substring(0, 2), 16) / 255f, g = Convert.ToInt32(hex.Substring(2, 2), 16) / 255f, b = Convert.ToInt32(hex.Substring(4, 2), 16) / 255f;
            for (int step = 0; step <= 20; step++)
            {
                float t = step / 20f;
                float rr = r + (1 - r) * t, gg = g + (1 - g) * t, bb = b + (1 - b) * t;
                if (step == 0 && Luminance(rr, gg, bb) >= ReadableLuminance) return hex; // (bright enough: the code's own text, unchanged)
                if (Luminance(rr, gg, bb) >= ReadableLuminance || step == 20) return $"{Mathf.RoundToInt(rr * 255):X2}{Mathf.RoundToInt(gg * 255):X2}{Mathf.RoundToInt(bb * 255):X2}";
            }
            return hex;
        }
        private static float Linear(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
        /// <summary>Relative luminance (WCAG 2.x) of an sRGB colour, 0-1.</summary>
        public static float Luminance(float r, float g, float b) => 0.2126f * Linear(r) + 0.7152f * Linear(g) + 0.0722f * Linear(b);
        public static float Luminance(Color c) => Luminance(c.r, c.g, c.b);
        /// <summary>WCAG contrast ratio of two colours (1 : 1 to 21 : 1).</summary>
        public static float Contrast(Color a, Color b) { float la = Luminance(a), lb = Luminance(b); return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f); }

        /// <summary>Turns the panel to face the head (yaw + pitch), keeping it upright.</summary>
        public static void FaceHead(Transform panel, Camera head)
        {
            if (head == null) return;
            var d = panel.position - head.transform.position;
            if (d.sqrMagnitude < 1e-6f) return;
            panel.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }
    }

    /// <summary>Client wave 21 (w21kit): a gold ring with an icon (a sprite) or a glyph (a text) in dark ink: UiKit.RingIcon.</summary>
    public sealed class UiRingIcon : MonoBehaviour { public Image Ring, Icon; public Text Glyph; }

    /// <summary>Marks a panel background: the pointer stops here but nothing happens.</summary>
    public sealed class UiBlocker : MonoBehaviour { }

    /// <summary>Client wave 13 (w13resize): what a panel is to the new casting (the pad / flick gesture, the wand, the item buttons).
    /// Menu: while it is open the new casting is inert. Hud: a panel that is simply always there: it never switches the casting
    /// off. Own: one of the casting's own pictures. ByName: looked up in <see cref="PanelKinds"/>' table by the panel's name.
    /// Undecided: a name the table does not know: it counts as a menu (the safe side) and the smoke's check fails on it, so a
    /// window added later must be decided on purpose.</summary>
    public enum PanelKind { ByName, Menu, Hud, Own, Undecided }

    /// <summary>
    /// Client wave 13 (w13resize; design note, 2026-10-07: "the grip still activates stuff like the wheel and other casting systems while I
    /// am messing around with menus and stuff. Those should stay deactivated"): every panel UiKit.Panel makes is registered here
    /// with its kind, and <see cref="MenuOpen"/> says whether a MENU is open now: its object is active and its canvas enabled
    /// (or its own test says so: the classic wrist menu, whose object is also the host of the boards' pages). The one place the
    /// casting asks is LokiGame.W13CastingAllowed (LokiGame.W13Resize.cs).
    ///   Hud   the chat, the forearm quick bar, the wrist's button strip, the buff bar, the minimap, the notes, the old armed-skill
    ///         label, the PvP counter, the microphone mark, Horde's HUD, the skill wheels (an existing way of casting), the ring's
    ///         flying pictures.
    ///   Own   the casting pads, the flick's icons, the wand's wheel, the spell in the hand and its fizzle, the item crosses, the
    ///         marks of resize / place mode, the Quick Use Bubbles (client wave 20).
    ///   Menu  everything else by name (the wrist menu, the boards, the ring, the NPC dialog, the keyboard, the guide, the world
    ///         map, the login / data panels, the notice, the death panel, Horde's choice, the overhead menu while it is shown) and
    ///         every ListWindow (Settings, shops, storage, the pickers, Commands, the tutorial ...: UI/ListWindow.cs says Menu).
    /// </summary>
    public static class PanelKinds
    {
        private static readonly System.Collections.Generic.Dictionary<string, PanelKind> Named = new System.Collections.Generic.Dictionary<string, PanelKind>
        {
            { "Chat", PanelKind.Hud }, { "QuickBar", PanelKind.Hud }, { "ButtonStrip", PanelKind.Hud }, { "BuffBar", PanelKind.Hud }, { "Minimap", PanelKind.Hud },
            { "Notes", PanelKind.Hud }, { "ArmedSkill", PanelKind.Hud }, { "PvpCounter", PanelKind.Hud }, { "VoiceMic", PanelKind.Hud }, { "HordeHud", PanelKind.Hud },
            { "SkillWheelOff", PanelKind.Hud }, { "SkillWheelMain", PanelKind.Hud }, { "MenuRingFx", PanelKind.Hud },
            { "ElbowButton", PanelKind.Hud }, { "MicMuted", PanelKind.Hud }, // client wave 13 (w13view): the forearm bar's elbow button, the muted microphone's mark
            { "ChatButton", PanelKind.Hud }, // client wave 19 (w19view41), point 41: the chat button beside the elbow button (LokiGame.W13View.cs): never a menu
            { "ButtonTip", PanelKind.Hud }, // client wave 14 (w14menu), point 7: a button's tip after the laser rests on it (UI/ButtonTip.cs)
            { "SettingHelp", PanelKind.Hud }, // client wave 15 (w15menu), point 7: the Settings window's help panel (UI/SettingHelp.cs)
            { "HordeJobHelp", PanelKind.Hud }, // client wave 19 merge: the job cards' help on the laser (Horde/Client/HordeJobHelp.cs, wave 18 h18rules, point 22): a tip like ButtonTip; it shows only over Horde's choice (a Menu). Undecided, it failed w13resize G, w13view G and w13grab H
            { "CastPadLeft", PanelKind.Own }, { "CastPadRight", PanelKind.Own }, { "FlickLeft", PanelKind.Own }, { "FlickRight", PanelKind.Own }, { "WandWheel", PanelKind.Own },
            { "SpellInHand", PanelKind.Own }, { "SpellFizzle", PanelKind.Own },
            { "ModeMarkLeft", PanelKind.Own }, { "ModeMarkRight", PanelKind.Own },
            { "QuickBubble", PanelKind.Own }, // client wave 20 (w20bubbles), point 56: a Quick Use Bubble (UI/QuickBubble.cs) is a casting way's own picture: never a menu; its own rule keeps it out of the others' way (LokiGame.W20Bubbles.cs)
            { "WristMenu", PanelKind.Menu }, { "MenuBoard", PanelKind.Menu }, { "MenuRing", PanelKind.Menu }, { "NpcDialog", PanelKind.Menu }, { "VrKeyboard", PanelKind.Menu },
            { "GuideWindow", PanelKind.Menu }, { "WorldMapWindow", PanelKind.Menu }, { "LoginPanel", PanelKind.Menu }, { "DataSetupPanel", PanelKind.Menu },
            { "NoDataPanel", PanelKind.Menu }, { "Notice", PanelKind.Menu }, { "Death", PanelKind.Menu }, { "HordeChoice", PanelKind.Menu }, { "OverheadMenu", PanelKind.Menu },
            { "CodexBook", PanelKind.Menu }, // wave 28 (wcodex): the Codex book (UI/CodexBook.cs): a menu (the casting is inert while it is open)
        };
        private sealed class Entry { public string Name; public Canvas Canvas; public PanelKind Kind; public Func<bool> Open; }
        private static readonly System.Collections.Generic.List<Entry> All = new System.Collections.Generic.List<Entry>();

        internal static void Register(string name, Canvas canvas, PanelKind kind)
        {
            if (kind == PanelKind.ByName && !Named.TryGetValue(name, out kind))
            {
                kind = PanelKind.Undecided;
                Debug.LogWarning($"[UI] the panel '{name}' has no kind in PanelKinds (UI/UiKit.cs): it counts as a menu (the new casting is inert while it is open) until it is decided");
            }
            if (All.Count >= 256) All.RemoveAll(e => e.Canvas == null);
            All.Add(new Entry { Name = name, Canvas = canvas, Kind = kind });
        }

        /// <summary>This panel's own test of "open" (in place of: active and its canvas enabled).</summary>
        public static void SetOpenTest(Transform panel, Func<bool> open)
        {
            foreach (var e in All) if (e.Canvas != null && e.Canvas.transform == panel) e.Open = open;
        }

        private static bool IsOpen(Entry e)
        {
            if (e.Canvas == null) return false;
            if (e.Open != null) return e.Open();
            return e.Canvas.enabled && e.Canvas.gameObject.activeInHierarchy;
        }

        /// <summary>A menu is open now; which: its panel's name. Client wave 19 (w19world), point 44: <paramref name="except"/> (a panel's
        /// name) does not count.</summary>
        public static bool MenuOpen(out string which, string except = null)
        {
            for (int i = 0; i < All.Count; i++)
            {
                var e = All[i];
                if ((e.Kind == PanelKind.Menu || e.Kind == PanelKind.Undecided) && e.Name != except && IsOpen(e)) { which = e.Name; return true; }
            }
            which = null; return false;
        }

        /// <summary>Client wave 13 (w13items): the kind of the panel whose root is <paramref name="panel"/> (ByName: not registered).</summary>
        public static PanelKind KindOf(Transform panel)
        {
            foreach (var e in All) if (e.Canvas != null && e.Canvas.transform == panel) return e.Kind;
            return PanelKind.ByName;
        }

        /// <summary>Client wave 23 (w23tree): every MENU open now (its panel's root; a panel of no decided kind counts, as for
        /// <see cref="MenuOpen"/>), but for <paramref name="except"/>: the windows the skill trees' preview must not stand in front of
        /// (UI/SkillTree3D.Panels.cs FreeSpot).</summary>
        public static void OpenMenus(System.Collections.Generic.List<Transform> into, Transform except = null)
        {
            into.Clear();
            for (int i = 0; i < All.Count; i++)
            {
                var e = All[i];
                if ((e.Kind == PanelKind.Menu || e.Kind == PanelKind.Undecided) && e.Canvas != null && e.Canvas.transform != except && IsOpen(e)) into.Add(e.Canvas.transform);
            }
        }

        /// <summary>Every panel alive now: its name, its kind, whether it is open (smoke: the audit).</summary>
        public static System.Collections.Generic.List<(string name, PanelKind kind, bool open)> List()
        {
            var l = new System.Collections.Generic.List<(string, PanelKind, bool)>();
            foreach (var e in All) if (e.Canvas != null) l.Add((e.Name, e.Kind, IsOpen(e)));
            return l;
        }
    }

    /// <summary>Client wave 11 (w11menu): a piece of UI the pointer hand's stick works while the laser or the gaze is on its button
    /// (<see cref="UiButton.Stick"/>). Returns true when it used the stick this frame (the rig then does not turn the world).</summary>
    public interface IUiStick { bool OnStick(Vector2 stick, float dt); }

    /// <summary>A clickable UI element for the VR pointer.</summary>
    public sealed class UiButton : MonoBehaviour
    {
        private Image _bg; private Text _text; private Action _onClick; private bool _hover;
        // client wave 21 (w21kit): on the theme's surfaces: its thin rim (the gold outline while it is hovered) and what it stands on
        private Image _rim; private UiSurface _surface;
        /// <summary>Client wave 21: a button of the theme (a soft square or a gold disc on canvas / parchment).</summary>
        public bool IsThemed => _surface != null;
        /// <summary>Client wave 21: the round gold button (its picture is a disc inside the collider's rectangle).</summary>
        public bool Round { get; set; }
        /// <summary>Client wave 21: the image that shows the gold outline while the laser or the look is on it (null: none).</summary>
        public Image Rim => _rim;
        internal void Themed(Image rim, UiSurface surface, Color normal, Color hover) { _rim = rim; _surface = surface; Normal = normal; Hover = hover; SetHover(_hover); }
        public Color Normal = UiKit.ButtonBg, Hover = UiKit.ButtonHover;
        public bool Interactable = true;
        public Text Text => _text;
        /// <summary>Client wave 9 (h9flow): the laser or the gaze is on this button now (DioramaRig.SetHover): a Horde card pre-highlighted this way is the one A takes.</summary>
        public bool Hovered => _hover;

        /// <summary>Client wave 11 (w11menu): which of several buttons under one ray is chosen (the laser and the gaze, VR/DioramaRig.Ui.cs):
        /// the higher priority, then the SMALLER button (a list row is itself a button with its Use / Equip button on top of it: the
        /// colliders' front faces are in one plane, so a plain Physics.Raycast gave either one and it changed from frame to frame:
        /// the design's 2026-10-06 playtest, "it keeps rapidly flashing" and "the equip button ... opens up the item's description page").
        /// Two shown buttons that overlap with the same priority and the same size would be a real ambiguity: w11's smoke looks for one.</summary>
        public int Priority;
        /// <summary>Client wave 11: what the pointer hand's stick does while the laser or the gaze is on this button (a slider steps, a
        /// scrolling text scrolls: UI/UiSlider.cs, UI/UiScrollText.cs); null: nothing, the stick turns the world as always.</summary>
        public IUiStick Stick;
        /// <summary>Client wave 11: the slider this button is the track of (the rig drags it while the laser's trigger is held).</summary>
        public UiSlider Slider;
        /// <summary>Client wave 14 (w14menu), point 7: what this button does in a few words, shown after the laser rests on it
        /// (UI/ButtonTip.cs; null: a glyph's own tip or none). A property: never serialized.</summary>
        public Func<string> Tip { get; set; }
        /// <summary>Client wave 23 (w23lines), item 5 (design note: "the player should not be able to pick the skip tutorial button with their eyes to
        /// avoid accidentally canceling the tutorial"): the LASER alone chooses and presses this button; the look never highlights it and
        /// look + trigger never presses it (VR/DioramaRig.Ui.cs Choosable; the look on it chooses nothing else either). A property: never
        /// serialized. The tutorial sets it on its buttons that leave it early (LokiGame.W23Lines.cs).</summary>
        public bool LaserOnly { get; set; }
        private Transform _panel;
        /// <summary>The panel (root canvas) this button is on.</summary>
        public Transform Panel { get { if (_panel == null) _panel = PanelOf(transform); return _panel; } }
        private void OnTransformParentChanged() { _panel = null; }
        public static Transform PanelOf(Transform t)
        {
            var c = t.GetComponentInParent<Canvas>();
            return c != null ? c.rootCanvas.transform : t.root;
        }
        /// <summary>The button's face in square metres.</summary>
        public float Area { get { var rt = (RectTransform)transform; var r = rt.rect; var s = rt.lossyScale; return Mathf.Abs(r.width * s.x * r.height * s.y); } }
        /// <summary>Areas within this share of each other count as the same size.</summary>
        public const float SameArea = 0.01f;
        /// <summary>a is chosen before b when both are under the ray. The last rule (the instance id) only makes the choice the same
        /// every frame: reaching it means the two are equal in priority and size (<see cref="SameRank"/>).</summary>
        public static bool Outranks(UiButton a, UiButton b)
        {
            if (b == null) return true;
            if (a.Priority != b.Priority) return a.Priority > b.Priority;
            float aa = a.Area, ab = b.Area;
            if (Mathf.Abs(aa - ab) > SameArea * Mathf.Max(aa, ab)) return aa < ab;
            return a.GetInstanceID() < b.GetInstanceID();
        }
        public static bool SameRank(UiButton a, UiButton b) { float aa = a.Area, ab = b.Area; return a.Priority == b.Priority && Mathf.Abs(aa - ab) <= SameArea * Mathf.Max(aa, ab); }

        public void Init(Image bg, Text text, Action onClick)
        {
            _bg = bg; _text = text; _onClick = onClick;
            if (_surface == null) { var s = Theme.SurfaceOf(transform); if (s != null && s.Light) _surface = s; } // client wave 21: any button on canvas or parchment
        }
        public void SetLabel(string s) { if (_text != null) { _text.text = s; Lang.Track(_text); } }
        public void SetAction(Action a) { _onClick = a; }
        /// <summary>New size for the button, its laser collider and its label.</summary>
        public void Resize(Vector2 size)
        {
            ((RectTransform)transform).sizeDelta = size;
            var col = GetComponent<BoxCollider>(); if (col != null) col.size = new Vector3(size.x, size.y, 8f);
            if (_text != null) ((RectTransform)_text.transform).sizeDelta = size - new Vector2(12, 4);
        }
        public void SetHover(bool h)
        {
            _hover = h;
            if (_bg != null)
            {
                // (client wave 21: a themed button that cannot be pressed is washed towards its surface, its text faint)
                Color c = !Interactable ? (_surface != null ? Color.Lerp(Theme.Fill(Normal), _surface.Color, 0.6f) : Normal * 0.6f) : h ? Hover : Normal;
                if (_bg.color != c) { _bg.color = c; if (_text != null) _text.SetVerticesDirty(); } // its letters are drawn readable on the new fill (UI/Theme.cs ThemedText)
            }
            if (_surface == null) return;
            if (_rim != null)
            {
                bool on = h && Interactable;
                var sp = on ? Theme.HoverRim : Theme.SoftRim;
                if (_rim.sprite != sp) _rim.sprite = sp;
                var rc = on ? Color.white : Interactable ? Theme.CellRim : new Color(Theme.CellRim.r, Theme.CellRim.g, Theme.CellRim.b, 0.25f);
                if (_rim.color != rc) _rim.color = rc;
            }
            if (_text is ThemedText tt) tt.Faint = !Interactable;
        }
        public void Click()
        {
            if (!Interactable || !gameObject.activeInHierarchy) return;
            Audio.LokiAudio.Instance?.PlayUi();
            MenuNav.BeforeClick(this); // client wave 13 (w13items), item 11: a menu opened by this press is decided when it ends (UI/MenuNav.cs)
            try { _onClick?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            finally { MenuNav.AfterClick(this); }
        }
        /// <summary>Client wave 13 (w13items), item 11: the button's action again, without a press (the back arrow brings a window back by
        /// the button that opened it: its own window may be closed by then).</summary>
        public void InvokeAction() { try { _onClick?.Invoke(); } catch (Exception e) { Debug.LogException(e); } }
        private void OnDisable() { _hover = false; if (_bg != null) _bg.color = Normal; if (_rim != null) { _rim.sprite = Theme.SoftRim; _rim.color = Theme.CellRim; } }
    }
}
