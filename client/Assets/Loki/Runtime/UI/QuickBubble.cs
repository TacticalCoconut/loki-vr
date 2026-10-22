using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 20 (w20bubbles), point 56: ONE Quick Use Bubble as the player sees it (what it does, where it is and when it shows:
    /// LokiGame.W20Bubbles.cs). design note (2026-10-09): "a quick use menu that holds up to 4 items inside spheric transparent bubbles".
    ///   Look     a see-through glass sphere: a small world-space panel that always faces the eyes (a sphere looks round from every side),
    ///            drawn with a picture made here (<see cref="Glass"/>: a faint fill, a bright rim, a soft highlight at the upper left, a
    ///            smaller one at the lower right), tinted pale cyan; the thing it holds (a skill's or an item's icon) floats in its middle,
    ///            an item's count in the bag at its lower right, a skill's wait (its cooldown or the after-cast delay) as a dark sector
    ///            with the seconds, as the forearm bar draws it (UI/QuickBar.cs). Empty: the glass alone, fainter.
    ///   States   lit (a hand's tip is in it and it may be used): a warm gold tint; used: a short pop (<see cref="PopSeconds"/>, up to
    ///            <see cref="PopScale"/>); the selected one in the editor: a gold ring around it and its number over it; switched off: a
    ///            grey ghost (the editor and Reposition only).
    ///   Size     <see cref="Px"/> px across = the bubble's diameter (<see cref="BaseRadius"/> × its size factor × 2).
    /// A UiKit panel "QuickBubble" without a background: no collider, so the laser and the look pass through it (UI/UiKit.cs PanelKinds:
    /// Own, a casting way's own picture, never a menu).
    /// </summary>
    public sealed class QuickBubble : MonoBehaviour
    {
        public const float Px = 200f, BaseRadius = 0.04f, IconPx = 96f, CoolPx = 150f, PopSeconds = 0.25f, PopScale = 1.18f;
        public static readonly Color Tint = new Color(0.72f, 0.92f, 1f, 1f), LitTint = new Color(1f, 0.88f, 0.55f, 1f), GhostTint = new Color(0.62f, 0.64f, 0.70f, 1f);
        private RectTransform _rt; private Canvas _canvas; private CanvasGroup _group; private Camera _head;
        private Image _glass, _ring, _icon, _cool, _body, _edge; private Text _count, _coolText, _num;
        // ---- client wave 24 (w24), items 11 and 12 (design note, 2026-10-12: "the spheres are far too transparent when you
        // look up at them ... Provide a slider that controls how transparent they are" and "The blue sky makes it very difficult to
        // see the spheres. So during the tutorial, make them have a little bit more contrast").
        //   Until now a bubble was the glass picture alone: its body 0.10 solid, its rim 0.82 (Glass below), an empty one's glass
        //   at 0.6 of that, the whole x BubbleEmptyAlpha (0.6) when empty: almost nothing against a bright sky.
        //   Solid     how solid it is drawn (0.2 to 1; the game gives the general slider x the bubble's own): a BODY disc under the
        //             glass in the glass's own colour, BodyMax x Solid solid (an empty bubble's: x EmptyBody), and everything of the
        //             bubble (rim, icon, number, texts) x GroupOf(Solid): whole from the default (0.8) up, down to 0.44 at 0.2.
        //             The states stay apart at every value: empty is fainter than filled (its body and its whole), the lit one is
        //             the warm gold, the ghost grey.
        //   Contrast  the look against a bright sky (the tutorial's bubbles lesson: the game says when): a dark edge line round
        //             the rim (the theme's dark ink) and the glass and body in the theme's gold; icon and number unchanged.
        /// <summary>The body's alpha at Solid 1; an empty bubble's share of it; the dark edge's size against the glass.</summary>
        public const float BodyMax = 0.9f, EmptyBody = 0.5f, EdgeScale = 1.07f;
        public static readonly Color ContrastGlass = new Color(0.93f, 0.74f, 0.30f, 1f), ContrastEdge = new Color(0.14f, 0.09f, 0.04f, 1f);
        /// <summary>How solid it is drawn (set by the game before Draw) and whether it wears the contrast look.</summary>
        public float Solid = 0.8f; public bool Contrast;
        /// <summary>Everything's share at a solidity: 1 from 0.8 up.</summary>
        public static float GroupOf(float solid) => Mathf.Clamp01(0.25f + 0.75f * Mathf.Clamp01(solid) / 0.8f);
        /// <summary>As drawn this frame (smoke, log): the whole's alpha, the body's alpha, the glass's and the edge's colours, the edge shown.</summary>
        public float DrawnAlpha => _group != null ? _group.alpha : 0f;
        public float BodyAlpha => _body != null && _body.enabled ? _body.color.a : 0f;
        public Color GlassColor => _glass != null ? _glass.color : Color.clear;
        public bool EdgeShown => _edge != null && _edge.enabled;
        private float _popUntil = -1f; private int _iconId; private bool _iconItem; private CastLook.State _look; // (client wave 21, w21tools: the look in force, UI/CastLook.cs)
        /// <summary>Which bubble (0-3).</summary>
        public int Index { get; private set; }
        /// <summary>How solid it is drawn now (0: not drawn at all); whether its canvas draws.</summary>
        public float Alpha { get; private set; }
        public bool Drawn => _canvas != null && _canvas.enabled;
        /// <summary>Its middle and radius in the world now (metres).</summary>
        public Vector3 Centre { get; private set; }
        public float Radius { get; private set; }
        /// <summary>Its look this frame (smoke, log): lit, the selection ring, the ghost, the icon shown, the count / wait texts.</summary>
        public bool LitNow { get; private set; }
        public bool RingShown => _ring != null && _ring.enabled;
        public bool Ghost { get; private set; }
        public bool IconShown => _icon != null && _icon.enabled && _icon.sprite != null;
        public string CountText => _count != null ? _count.text : "";
        public float CoolFill => _cool != null && _cool.enabled ? _cool.fillAmount : 0f;
        public int Pops { get; private set; }

        public static QuickBubble Create(Transform parent, Camera head, int index)
        {
            var rt = UiKit.Panel("QuickBubble", parent, new Vector2(Px, Px), 2f * BaseRadius / Px, false);
            var b = rt.gameObject.AddComponent<QuickBubble>();
            b._rt = rt; b._head = head; b.Index = index; b._canvas = rt.GetComponent<Canvas>(); b._group = rt.gameObject.AddComponent<CanvasGroup>();
            b._group.interactable = false; b._group.blocksRaycasts = false;
            b._ring = UiKit.Icon(rt, Vector2.zero, new Vector2(Px * 1.14f, Px * 1.14f)); b._ring.sprite = Glass; b._ring.preserveAspect = false; b._ring.color = OverheadMenu.Lit; b._ring.enabled = false;
            b._edge = UiKit.Icon(rt, Vector2.zero, new Vector2(Px * EdgeScale, Px * EdgeScale)); b._edge.sprite = Glass; b._edge.preserveAspect = false; b._edge.color = ContrastEdge; b._edge.enabled = false; // (client wave 24: the contrast look's dark edge, under the glass)
            b._body = UiKit.Icon(rt, Vector2.zero, new Vector2(Px * 0.96f, Px * 0.96f)); b._body.sprite = Disc; b._body.preserveAspect = false; b._body.color = new Color(Tint.r, Tint.g, Tint.b, 0f); b._body.enabled = true; // (client wave 24: the body, under the glass)
            b._glass = UiKit.Icon(rt, Vector2.zero, new Vector2(Px, Px)); b._glass.sprite = Glass; b._glass.preserveAspect = false; b._glass.color = Tint; b._glass.enabled = true;
            b._icon = UiKit.Icon(rt, Vector2.zero, new Vector2(IconPx, IconPx));
            b._cool = UiKit.Icon(rt, Vector2.zero, new Vector2(CoolPx, CoolPx)); b._cool.sprite = Disc; b._cool.preserveAspect = false;
            b._cool.type = Image.Type.Filled; b._cool.fillMethod = Image.FillMethod.Radial360; b._cool.fillOrigin = (int)Image.Origin360.Top; b._cool.fillClockwise = false;
            b._cool.color = new Color(0f, 0f, 0f, 0.6f); b._cool.enabled = false;
            b._coolText = UiKit.Label(rt, "", Vector2.zero, new Vector2(140, 70), 52, TextAnchor.MiddleCenter, new Color(1f, 0.95f, 0.8f));
            b._count = UiKit.Label(rt, "", new Vector2(34, -50), new Vector2(110, 50), 40, TextAnchor.LowerRight, new Color(1f, 0.85f, 0.45f));
            b._num = UiKit.Label(rt, (index + 1).ToString(), new Vector2(0, Px * 0.5f + 30), new Vector2(80, 60), 52, TextAnchor.MiddleCenter, OverheadMenu.Lit);
            b._num.gameObject.SetActive(false);
            b._look = CastLook.Capture(rt); CastLook.Own(b._look, b._count, Theme.GoldLight); // client wave 21 (w21tools): built plain; drawn in the look in force from the first Draw (the amount floats in the glass: light gold, not a cell's ink)
            b.Apply(0f);
            return b;
        }

        /// <summary>Puts it at <paramref name="centre"/> with radius <paramref name="radius"/> (metres), facing the eyes; a pop under way
        /// swells it a little.</summary>
        public void Place(Vector3 centre, float radius)
        {
            Centre = centre; Radius = radius;
            float pop = 1f;
            if (_popUntil > 0f) { float t = 1f - (_popUntil - Time.unscaledTime) / PopSeconds; if (t >= 1f) _popUntil = -1f; else pop = 1f + (PopScale - 1f) * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI); }
            transform.position = centre;
            transform.localScale = Vector3.one * (2f * radius / Px * pop);
            UiKit.FaceHead(transform, _head);
        }

        /// <summary>It was used: a short pop.</summary>
        public void Pop() { _popUntil = Time.unscaledTime + PopSeconds; Pops++; }

        /// <summary>Its look this frame. <paramref name="icon"/> null: empty; <paramref name="cool"/> the wait's share left and its text.</summary>
        public void Draw(float alpha, Sprite icon, string count, float coolFill, string coolText, bool lit, bool selected, bool number, bool ghost)
        {
            Apply(alpha);
            if (!Drawn) return;
            CastLook.Sync(_look); var tint = CastLook.C(Tint); // client wave 21 (w21tools): the glass, the ring, the number and the texts in the look in force
            LitNow = lit; Ghost = ghost;
            bool contrast = Contrast && !ghost && !lit; if (contrast) tint = ContrastGlass; // client wave 24 (w24), item 12
            _glass.color = ghost ? new Color(GhostTint.r, GhostTint.g, GhostTint.b, 0.8f) : lit ? CastLook.C(LitTint) : icon == null ? new Color(tint.r, tint.g, tint.b, 0.6f) : tint;
            // client wave 24 (w24), item 11: the body under the glass, in the glass's colour
            var gc = _glass.color; float solid = lit ? 1f : Mathf.Clamp01(Solid); // (the lit one is whole at every value: the most solid of the states)
            _body.color = new Color(gc.r, gc.g, gc.b, BodyMax * solid * (icon == null && !lit && !ghost ? EmptyBody : 1f) * (ghost ? 0.5f : 1f));
            if (_edge.enabled != contrast) _edge.enabled = contrast;
            _group.alpha = Alpha * GroupOf(solid);
            if (_ring.enabled != selected) _ring.enabled = selected;
            if (_icon.sprite != icon) _icon.sprite = icon;
            bool showIcon = icon != null && !ghost;
            if (_icon.enabled != showIcon) _icon.enabled = showIcon;
            if (_count.text != (count ?? "")) _count.text = count ?? "";
            bool cool = coolFill > 0.001f && !ghost;
            if (_cool.enabled != cool) _cool.enabled = cool;
            if (cool) _cool.fillAmount = Mathf.Clamp01(coolFill);
            string ct = cool ? coolText ?? "" : "";
            if (_coolText.text != ct) _coolText.text = ct;
            if (_num.gameObject.activeSelf != number) _num.gameObject.SetActive(number);
        }

        private void Apply(float alpha)
        {
            Alpha = Mathf.Clamp01(alpha); _group.alpha = Alpha;
            bool draw = Alpha > 0.002f;
            if (_canvas.enabled != draw) _canvas.enabled = draw;
        }

        // ------------------------------------------------------------------ the pictures, made once
        private static Sprite _glassSprite, _discSprite;
        /// <summary>The glass: 128 px, white (the Image's colour tints it); alpha by the distance d from the middle (1 at the rim): 0.10 inside,
        /// rising from d 0.70 to 0.82 at d 0.93 (the rim), 0 at d 1.0; a highlight of up to 0.55 around (-0.38, +0.42) within 0.24, and one
        /// of up to 0.22 around (+0.32, -0.46) within 0.13.</summary>
        public static Sprite Glass
        {
            get
            {
                if (_glassSprite != null) return _glassSprite;
                const int n = 128; var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "LokiBubbleGlass", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[n * n]; float half = (n - 1) * 0.5f;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x - half) / half, v = (y - half) / half, d = Mathf.Sqrt(u * u + v * v);
                        float a;
                        if (d >= 1f) a = 0f;
                        else if (d <= 0.70f) a = 0.10f;
                        else if (d <= 0.93f) a = Mathf.Lerp(0.10f, 0.82f, Mathf.SmoothStep(0f, 1f, (d - 0.70f) / 0.23f));
                        else a = Mathf.Lerp(0.82f, 0f, Mathf.SmoothStep(0f, 1f, (d - 0.93f) / 0.07f));
                        if (d < 1f)
                        {
                            float h1 = Mathf.Sqrt((u + 0.38f) * (u + 0.38f) + (v - 0.42f) * (v - 0.42f)), h2 = Mathf.Sqrt((u - 0.32f) * (u - 0.32f) + (v + 0.46f) * (v + 0.46f));
                            a = Mathf.Max(a, 0.55f * (1f - Mathf.SmoothStep(0f, 1f, h1 / 0.24f)));
                            a = Mathf.Max(a, 0.22f * (1f - Mathf.SmoothStep(0f, 1f, h2 / 0.13f)));
                        }
                        px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                    }
                tex.SetPixels32(px); tex.Apply(true, true);
                return _glassSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            }
        }
        /// <summary>A filled disc (the wait's sector is cut from it): 64 px, alpha 1 inside 0.95 of the radius, 0 at the rim.</summary>
        public static Sprite Disc
        {
            get
            {
                if (_discSprite != null) return _discSprite;
                const int n = 64; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "LokiBubbleDisc", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[n * n]; float half = (n - 1) * 0.5f;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x - half) / half, v = (y - half) / half, d = Mathf.Sqrt(u * u + v * v);
                        float a = d <= 0.95f ? 1f : d >= 1f ? 0f : (1f - d) / 0.05f;
                        px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                tex.SetPixels32(px); tex.Apply(false, true);
                return _discSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            }
        }
    }
}
