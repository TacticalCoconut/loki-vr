using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 22 (w22clips), the tutorial's CASTING WAYS (design note, 2026-10-11: "there's going to be a panel that says casting. There are
    /// different ways to cast your abilities in Loki. And then each one shows up as a column on a large panel. And it describes its name,
    /// how it works, a small video of it that plays on loop, and its best use case scenarios. And then if you click on that column, it moves
    /// you to a following panel that has you test it out. And at the end, it brings you back to the multiple columns. But at that point, you
    /// can actually also choose to progress."). The columns' panel; LokiGame.W22Cast.cs lists the ways and runs their lessons.
    ///   Shape    ONE LARGE PANEL of the theme, folded: a facet per way, side by side on an arc around the eyes
    ///            (<see cref="Distance"/> m away, each facing them), joined edge to edge as the Character board's three facets are
    ///            (UI/UiKit.cs Join: no frame at a fold, so ONE wood frame runs around all of them, its top and bottom rails from end
    ///            to end). Flat, seven columns would be 1.85 m wide and read at a slant of 46° at the ends, 1.3 m away: hence the folds.
    ///            The facets stand upright (a leaning facet's edges would not meet its neighbours').
    ///            Its HEAD is the top <see cref="HeaderH"/> px of the panel: the gold title plate "Casting" on the frame over the middle
    ///            facet and the sentence under it; on the facet to its left what to do (from the first way tried on: where the casting is
    ///            changed later); on the facet to its right "Continue the tutorial" (from the first way tried on) and "End tutorial".
    ///            Under the head, on each facet, ITS WAY'S COLUMN, top to bottom: the way's name, its small picture (the way's clip, played
    ///            live by a stage of its own: UI/TutorialClip.cs), how it works, "Best for" and its lines; a green check when it was
    ///            tried. THE WHOLE COLUMN IS THE BUTTON (a kit button under everything: lit, with its gold outline, while the laser or
    ///            the look is on it; a fingertip through it presses it too: LokiGame.W22Cast.cs).
    ///   Sizes    <see cref="MetresPerPx"/> m a pixel (client wave 23: 0.8 mm at 1.2 m; it was the guide's 0.6 mm at 0.9 m: the same from
    ///            the eyes): the name <see cref="NamePx"/> px, every other text <see cref="TextPx"/> px (1.07°; the boards' smallest is
    ///            0.95°: the Equipment page); a facet <see cref="ColW"/> x <see cref="FacetH"/> px (35.2 x 88.8 cm),
    ///            <see cref="PitchDegrees"/>° of the arc each.
    ///   Kind     HUD panels (UI/UiKit.cs PanelKind.Hud): never an open menu, never grabbed. It stays where it was put (in front of the
    ///            eyes when the casting step begins).
    /// </summary>
    public sealed class CastingWays : MonoBehaviour
    {
        /// <summary>Client wave 23 (w23place), the design's answer 49 ("the casting options panel is way too close to the player. It needs to be
        /// further away"): <see cref="Distance"/> m from the eyes (it was 0.9 m), every pixel grown by the same 4 / 3
        /// (<see cref="MetresPerPx"/> 0.0008, was 0.0006), so every text, the pictures and the arc are from the eyes exactly what they
        /// were (28 px: 1.07°; a facet 16.7°; the top edge <see cref="TopM"/> m above the eyes: 17.9° up, as 0.29 m was at 0.9 m).</summary>
        // client wave 25 (w25tut), item 9 (design note: "move the casting panel's default position further"): 1.6 m (it was 1.2; FLAGGED: Loki's
        // number), every pixel grown by the same factor again, so from the eyes every text and the arc are still what they were.
        public const float Distance = 1.6f, MetresPerPx = 0.0006f * Distance / 0.9f;
        public const float ColW = 440f, ColH = 940f, HeaderH = 170f, FacetH = HeaderH + ColH, PadPx = 18f, CardInsetPx = 8f;
        public const float PictureW = 404f, PictureH = 227f, HowH = 372f, BestH = 204f, TopM = 0.29f * Distance / 0.9f;
        /// <summary>Client wave 23 (w23place): the facets' canvas order is the guide's (UI/TutorialGuide.cs SortingOrder, under the panels'
        /// 10; it was 18): a casting tool or a window is always drawn over the Casting panel.</summary>
        public const int NamePx = 38, TextPx = 28, SentencePx = 30, Order = TutorialGuide.SortingOrder;
        /// <summary>A facet's share of the arc (degrees): its edges meet its neighbours' exactly.</summary>
        public static float PitchDegrees => 2f * Mathf.Atan(ColW * 0.5f * MetresPerPx / Distance) * Mathf.Rad2Deg;

        public sealed class Column
        {
            /// <summary>The way's facet of the panel (head and column) and its column (the facet under the head: the button's place).</summary>
            public RectTransform Facet, Panel;
            public UiButton Button; public Text Name, How, BestHead, Best, Note; public RawImage Picture; public RectTransform Badge, PictureFrame;
            public bool Tried, Blocked;
        }
        private readonly List<Column> _cols = new List<Column>();
        public IReadOnlyList<Column> Columns => _cols;
        /// <summary>The head's three places (what to do / the sentence under the title / the buttons), the middle one, their texts.</summary>
        public IReadOnlyList<RectTransform> HeadCells => _cells;
        public RectTransform Head { get; private set; }
        public Text Sentence { get; private set; }
        public Text Note { get; private set; }
        public UiButton ContinueButton { get; private set; }
        public UiButton EndButton { get; private set; }
        public UiTitlePlate Title { get; private set; }
        /// <summary>A column was pressed (its number); the two buttons.</summary>
        public Action<int> Picked; public Action ContinuePressed, EndPressed;
        public bool Visible => gameObject.activeSelf;
        private Func<Camera> _head; private Material _pictureMat; private readonly List<RectTransform> _cells = new List<RectTransform>();
        /// <summary>The whole panel's width seen from the eyes, degrees.</summary>
        public float WidthDegrees => _cols.Count * PitchDegrees;

        public static CastingWays Create(Transform parent, Func<Camera> head, int columns)
        {
            var go = new GameObject("CastingWays"); go.transform.SetParent(parent, false);
            var w = go.AddComponent<CastingWays>(); w._head = head;
            var sh = Shader.Find("Loki/ClipView"); if (sh != null) w._pictureMat = new Material(sh) { name = "LokiClipViewColumn" };
            int n = Mathf.Max(1, columns), mid = (n - 1) / 2; // (an even count: the facet left of the middle carries the title)
            for (int i = 0; i < n; i++) w._cols.Add(w.MakeColumn(i, n));
            // the head: three facets' tops (with fewer than three facets they share what there is)
            float tw = ColW - 2f * PadPx, hy = FacetH * 0.5f - HeaderH * 0.5f;
            RectTransform Cell(int i, string name) { var c = Zone(w._cols[Mathf.Clamp(i, 0, n - 1)].Facet, name, new Vector2(0f, hy), new Vector2(ColW, HeaderH)); w._cells.Add(c); return c; }
            var note = Cell(mid - 1, "head:note"); var middle = Cell(mid, "head:sentence"); var buttons = Cell(mid + 1, "head:buttons");
            w.Head = middle; w.Title = UiKit.TitlePlate(w._cols[mid].Facet, Lang.T("Casting"), 40, ColW - 40f);
            w.Sentence = UiKit.Label(middle, "", new Vector2(0f, -14f), new Vector2(tw, HeaderH - 44f), SentencePx, TextAnchor.MiddleCenter);
            w.Sentence.resizeTextForBestFit = true; w.Sentence.resizeTextMinSize = TextPx; w.Sentence.resizeTextMaxSize = SentencePx;
            w.Note = UiKit.Label(note, "", new Vector2(0f, -4f), new Vector2(tw, HeaderH - 24f), TextPx, TextAnchor.MiddleCenter, Theme.InkDim);
            w.ContinueButton = UiKit.Button(buttons, Lang.T("Continue the tutorial"), new Vector2(0f, 33f), new Vector2(ColW - 60f, 64f), () => w.ContinuePressed?.Invoke(), 30);
            w.EndButton = UiKit.Button(buttons, Lang.T("End tutorial"), new Vector2(0f, -41f), new Vector2(ColW - 60f, 64f), () => w.EndPressed?.Invoke(), 30);
            w.ContinueButton.gameObject.SetActive(false);
            go.SetActive(false);
            return w;
        }

        /// <summary>A place on a panel with nothing drawn: its children are laid out from its middle.</summary>
        private static RectTransform Zone(RectTransform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer; go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        private Column MakeColumn(int i, int n)
        {
            var c = new Column(); int index = i;
            var facet = UiKit.Panel("CastingWay" + (i + 1), transform, new Vector2(ColW, FacetH), MetresPerPx, true, PanelKind.Hud, PanelLook.Framed);
            var canvas = facet.GetComponent<Canvas>(); if (canvas != null) canvas.sortingOrder = Order;
            // one panel: no frame where a facet meets its neighbour (the frame runs around all of them)
            UiKit.Join(facet, (i > 0 ? Sides.Left : Sides.None) | (i < n - 1 ? Sides.Right : Sides.None));
            c.Facet = facet;
            var rt = Zone(facet, "column", new Vector2(0f, -HeaderH * 0.5f), new Vector2(ColW, ColH)); c.Panel = rt;
            // the whole column is the button: a kit button under everything else (lit, with its gold outline, while the laser or the look is on it)
            c.Button = UiKit.Button(rt, "", Vector2.zero, new Vector2(ColW - 2f * CardInsetPx, ColH - 2f * CardInsetPx), () => Picked?.Invoke(index), 20);
            c.Button.name = "btn:way" + (i + 1);
            float tw = ColW - 2f * PadPx, y = ColH / 2f - PadPx;
            c.Name = UiKit.Label(rt, "", new Vector2(0f, y - 24f), new Vector2(tw, 48f), NamePx, TextAnchor.MiddleLeft); c.Name.fontStyle = FontStyle.Bold;
            c.Name.resizeTextForBestFit = true; c.Name.resizeTextMinSize = 30; c.Name.resizeTextMaxSize = NamePx;
            y -= 48f + 6f;
            var frame = UiKit.Box(rt, "pictureFrame", new Vector2(0f, y - PictureH / 2f), new Vector2(PictureW + 6f, PictureH + 6f), Theme.Gold); if (frame.GetComponent<Image>() is ThemedImage fi) fi.Raw = true;
            c.PictureFrame = frame;
            var back = UiKit.Box(rt, "pictureBack", new Vector2(0f, y - PictureH / 2f), new Vector2(PictureW, PictureH), Theme.WoodDark); if (back.GetComponent<Image>() is ThemedImage bi) bi.Raw = true;
            var prt = Zone(rt, "picture", new Vector2(0f, y - PictureH / 2f), new Vector2(PictureW, PictureH));
            c.Picture = prt.gameObject.AddComponent<RawImage>(); c.Picture.raycastTarget = false; c.Picture.enabled = false;
            if (_pictureMat != null) c.Picture.material = _pictureMat; else if (UiKit.Overlay != null) c.Picture.material = UiKit.Overlay;
            y -= PictureH + 10f;
            c.How = UiKit.Label(rt, "", new Vector2(0f, y - HowH / 2f), new Vector2(tw, HowH), TextPx, TextAnchor.UpperLeft);
            y -= HowH + 6f;
            c.BestHead = UiKit.Label(rt, Lang.T("Best for"), new Vector2(0f, y - 18f), new Vector2(tw, 36f), TextPx, TextAnchor.MiddleLeft, Theme.Accent); c.BestHead.fontStyle = FontStyle.Bold;
            y -= 36f;
            c.Best = UiKit.Label(rt, "", new Vector2(0f, y - BestH / 2f), new Vector2(tw, BestH), TextPx, TextAnchor.UpperLeft);
            c.Note = UiKit.Label(rt, "", new Vector2(0f, -ColH / 2f + PadPx + 20f), new Vector2(tw, 40f), TextPx, TextAnchor.MiddleCenter, Theme.Bad); c.Note.gameObject.SetActive(false);
            // the check of a way that was tried: a green disc with a white tick in the name's corner
            var badge = UiKit.Soft(rt, "tried", new Vector2(ColW / 2f - PadPx - 24f, ColH / 2f - PadPx - 24f), new Vector2(48f, 48f), Theme.Good); if (badge.GetComponent<Image>() is ThemedImage gi) gi.Raw = true;
            var tick = UiKit.Label(badge, "✓", Vector2.zero, new Vector2(48f, 48f), 34, TextAnchor.MiddleCenter, Theme.OnWood); tick.fontStyle = FontStyle.Bold;
            badge.gameObject.SetActive(false); c.Badge = badge;
            return c;
        }

        /// <summary>A column's texts and state. <paramref name="blocked"/>: why this way cannot be tried now (null: it can).</summary>
        public void SetColumn(int i, string name, string how, string best, bool tried, string blocked, Texture picture)
        {
            if (i < 0 || i >= _cols.Count) return; var c = _cols[i];
            Set(c.Name, name); Set(c.How, how); Set(c.Best, best); Set(c.BestHead, Lang.T("Best for"));
            c.Tried = tried; if (c.Badge.gameObject.activeSelf != tried) c.Badge.gameObject.SetActive(tried);
            c.Blocked = blocked != null; c.Button.Interactable = blocked == null;
            if (c.Note.gameObject.activeSelf != c.Blocked) c.Note.gameObject.SetActive(c.Blocked);
            if (c.Blocked) Set(c.Note, blocked);
            if (c.Picture.texture != picture) c.Picture.texture = picture;
            if (c.Picture.enabled != (picture != null)) c.Picture.enabled = picture != null;
        }
        private static void Set(Text t, string s) { s = s ?? ""; if (t.text != s) t.text = s; }

        /// <summary>The head's texts (<paramref name="note"/>: what to do, or where the casting is changed later) and whether the way on can
        /// be taken (from the first way tried on).</summary>
        public void SetHead(string sentence, string note, bool canContinue, string continueLabel, string endLabel)
        {
            Set(Sentence, sentence); Set(Note, note);
            if (ContinueButton.gameObject.activeSelf != canContinue) ContinueButton.gameObject.SetActive(canContinue);
            if (canContinue && ContinueButton.Text != null && ContinueButton.Text.text != continueLabel) ContinueButton.SetLabel(continueLabel);
            if (EndButton.Text != null && EndButton.Text.text != endLabel) EndButton.SetLabel(endLabel);
            if (Title != null && Title.Text != null && Title.Text.text != Lang.T("Casting")) Title.Set(Lang.T("Casting"));
        }

        public void Show() { bool place = !gameObject.activeSelf; if (place) { gameObject.SetActive(true); PlaceInFront(); } }
        public void Hide() { if (gameObject.activeSelf) gameObject.SetActive(false); }

        /// <summary>In front of the eyes: the frame's top edge <see cref="TopM"/> m above them (17.9° up: under the overhead menu's look-up
        /// zone, which begins 24° up), the facets on an arc of <see cref="Distance"/> m around the eyes, each facing them, edge to edge.</summary>
        public void PlaceInFront()
        {
            var cam = _head != null ? _head() : null; if (cam == null || _cols.Count == 0) return;
            var t = cam.transform; var f = t.forward; f.y = 0f; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
            transform.SetPositionAndRotation(t.position, Quaternion.LookRotation(f, Vector3.up));
            float y = TopM - UiKit.OutsetOf(_cols[0].Facet) * MetresPerPx - FacetH * MetresPerPx * 0.5f;
            int n = _cols.Count;
            for (int i = 0; i < n; i++)
            {
                var q = Quaternion.Euler(0f, (i - (n - 1) * 0.5f) * PitchDegrees, 0f);
                _cols[i].Facet.localPosition = q * new Vector3(0f, 0f, Distance) + Vector3.up * y; _cols[i].Facet.localRotation = q;
            }
        }

        /// <summary>A place on column <paramref name="i"/> for a check mark: its badge.</summary>
        public Vector3 BadgeWorld(int i) => i >= 0 && i < _cols.Count ? _cols[i].Badge.position - _cols[i].Panel.forward * 0.01f : transform.position;
    }
}
