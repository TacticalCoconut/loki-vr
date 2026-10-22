using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 25 (w25tut), item 9 of the design's next list (playtest notes): the tutorial's page AFTER the casting
    /// ways. design note: "Once the player has completed every single casting tutorial, progress it to the next tutorial panel, but do give them a
    /// button that says, require to try out the casting a bit more." and "after the player confirms that they want to progress, on the
    /// screen that's asks them if they want to try casting a bit more. Ask them which casting method do they want to, start the game with.
    /// Tell them that they can swap at any time. And when the game opens, have that be their chosen casting method."
    ///   Stage 1  "You tried every way to cast." with "Continue" and "Try casting a bit more" (LokiGame.W25Tut.cs shows it by itself once
    ///            every way the player can try was tried).
    ///   Stage 2  "Which way do you want to start with?", "You can switch any time in Settings.", one button per way (its name; a tick on
    ///            a way that was tried), and "Try casting a bit more" (back to the columns). Reached by "Continue" here, or by "Continue the
    ///            tutorial" on the columns.
    ///   Both     "End tutorial" (the laser's alone, as everywhere: LokiGame.W23Lines.cs item 5). The page is the tutorial guide's kind of
    ///            card (the theme's parchment, a HUD panel: it never switches the casting off), as wide as the guide, at its distance, in
    ///            front of the eyes when it is shown. The texts and what each button does are the owner's (LokiGame.W25Tut.cs).
    /// </summary>
    public sealed class CastingChoice : MonoBehaviour
    {
        /// <summary>The guide's own size and distance (UI/TutorialGuide.cs): the same page, read the same.</summary>
        public const float MetresPerPx = TutorialGuide.MetresPerPx, Distance = TutorialGuide.Distance, WidthPx = TutorialGuide.WidthPx, Pad = 28f;
        public const int TitlePx = 40, SentencePx = 34, NotePx = 28, ButtonPx = 30;
        public const float BigH = 76f, WayW = 410f, WayH = 72f, Gap = 14f, EndW = 380f, EndH = 60f;

        public bool Visible => gameObject.activeSelf;
        /// <summary>1: "every way tried" (Continue / Try more); 2: which way to start with.</summary>
        public int Stage { get; private set; }
        public Text Title { get; private set; }
        public Text Sentence { get; private set; }
        public Text Note { get; private set; }
        public UiButton ContinueButton { get; private set; }
        public UiButton MoreButton { get; private set; }
        public UiButton EndButton { get; private set; }
        /// <summary>Stage 2's buttons, one per way, in the columns' order.</summary>
        public IReadOnlyList<UiButton> WayButtons => _ways;
        public Action ContinuePressed, MorePressed, EndPressed; public Action<int> WayPressed;
        private readonly List<UiButton> _ways = new List<UiButton>();
        private RectTransform _rt, _border, _bg; private BoxCollider _col; private Func<Camera> _head;

        public static CastingChoice Create(Transform parent, Func<Camera> head, int ways)
        {
            var rt = UiKit.Panel("CastingChoice", parent, new Vector2(WidthPx, 600f), MetresPerPx, true, PanelKind.Hud, PanelLook.Card);
            var canvas = rt.GetComponent<Canvas>(); if (canvas != null) canvas.sortingOrder = TutorialGuide.SortingOrder; // (the guide's: under every panel)
            var c = rt.gameObject.AddComponent<CastingChoice>();
            c._rt = rt; c._head = head; c._col = rt.GetComponent<BoxCollider>();
            c._border = (RectTransform)rt.Find("border"); c._bg = (RectTransform)rt.Find("bg");
            float tw = WidthPx - 2f * Pad;
            c.Title = UiKit.Label(rt, Lang.T("Casting"), Vector2.zero, new Vector2(tw, 52f), TitlePx, TextAnchor.MiddleLeft); c.Title.fontStyle = FontStyle.Bold;
            c.Sentence = UiKit.Label(rt, "", Vector2.zero, new Vector2(tw, 56f), SentencePx, TextAnchor.MiddleLeft); c.Sentence.fontStyle = FontStyle.Bold;
            c.Sentence.resizeTextForBestFit = true; c.Sentence.resizeTextMinSize = 26; c.Sentence.resizeTextMaxSize = SentencePx;
            c.Note = UiKit.Label(rt, "", Vector2.zero, new Vector2(tw, 84f), NotePx, TextAnchor.UpperLeft);
            c.ContinueButton = UiKit.Button(rt, Lang.T("Continue"), Vector2.zero, new Vector2(tw, BigH), () => c.ContinuePressed?.Invoke(), ButtonPx);
            c.MoreButton = UiKit.Button(rt, Lang.T("Try casting a bit more"), Vector2.zero, new Vector2(tw, BigH), () => c.MorePressed?.Invoke(), ButtonPx);
            c.EndButton = UiKit.Button(rt, Lang.T("End tutorial"), Vector2.zero, new Vector2(EndW, EndH), () => c.EndPressed?.Invoke(), ButtonPx);
            for (int i = 0; i < ways; i++)
            {
                int k = i; var b = UiKit.Button(rt, "", Vector2.zero, new Vector2(WayW, WayH), () => c.WayPressed?.Invoke(k), ButtonPx);
                b.name = "btn:way" + (i + 1); c._ways.Add(b);
            }
            rt.gameObject.SetActive(false);
            return c;
        }

        /// <summary>Stage <paramref name="stage"/> with its texts; stage 2's way buttons with their labels (null: that way is not offered).</summary>
        public void Set(int stage, string sentence, string note, IList<string> wayLabels)
        {
            Stage = stage;
            Sentence.text = sentence ?? ""; Note.text = note ?? "";
            bool one = stage == 1;
            ContinueButton.gameObject.SetActive(one);
            for (int i = 0; i < _ways.Count; i++)
            {
                string l = !one && wayLabels != null && i < wayLabels.Count ? wayLabels[i] : null;
                bool on = !string.IsNullOrEmpty(l); if (_ways[i].gameObject.activeSelf != on) _ways[i].gameObject.SetActive(on);
                if (on && (_ways[i].Text == null || _ways[i].Text.text != l)) _ways[i].SetLabel(l);
            }
            Layout();
        }

        /// <summary>Top to bottom: the title, the sentence, the note, then stage 1's two big buttons or stage 2's ways (two side by side) and
        /// "Try casting a bit more", then "End tutorial" at the bottom right. The page is as tall as that.</summary>
        private void Layout()
        {
            float tw = WidthPx - 2f * Pad;
            var shown = new List<UiButton>(); foreach (var b in _ways) if (b.gameObject.activeSelf) shown.Add(b);
            int rows = (shown.Count + 1) / 2;
            float body = Stage == 1 ? BigH * 2f + Gap : rows * WayH + Mathf.Max(0, rows - 1) * Gap + Gap * 2f + BigH;
            float h = Pad + 52f + 8f + 56f + 8f + 84f + Gap + body + Gap * 2f + EndH + Pad;
            _rt.sizeDelta = new Vector2(WidthPx, h);
            if (_border != null) _border.sizeDelta = new Vector2(WidthPx, h);
            if (_bg != null) _bg.sizeDelta = new Vector2(WidthPx - 6f, h - 6f);
            if (_col != null) _col.size = new Vector3(WidthPx, h, 4f);
            float y = h / 2f - Pad;
            void Put(RectTransform r, float height, float x = 0f, float width = -1f) { r.sizeDelta = new Vector2(width < 0f ? tw : width, height); r.anchoredPosition = new Vector2(x, y - height / 2f); }
            Put(Title.rectTransform, 52f); y -= 52f + 8f;
            Put(Sentence.rectTransform, 56f); y -= 56f + 8f;
            Put(Note.rectTransform, 84f); y -= 84f + Gap;
            if (Stage == 1)
            {
                Put((RectTransform)ContinueButton.transform, BigH); y -= BigH + Gap;
                Put((RectTransform)MoreButton.transform, BigH); y -= BigH;
            }
            else
            {
                for (int i = 0; i < shown.Count; i++)
                {
                    int row = i / 2, col = i % 2; bool alone = i == shown.Count - 1 && shown.Count % 2 == 1;
                    float x = alone ? 0f : (col == 0 ? -(WayW + Gap) / 2f : (WayW + Gap) / 2f);
                    float yy = y - row * (WayH + Gap);
                    ((RectTransform)shown[i].transform).anchoredPosition = new Vector2(x, yy - WayH / 2f);
                }
                y -= rows * WayH + Mathf.Max(0, rows - 1) * Gap + Gap * 2f;
                Put((RectTransform)MoreButton.transform, BigH); y -= BigH;
            }
            y -= Gap * 2f;
            ((RectTransform)EndButton.transform).anchoredPosition = new Vector2(WidthPx / 2f - Pad - EndW / 2f, y - EndH / 2f);
        }

        public void Show() { bool place = !gameObject.activeSelf; if (place) { gameObject.SetActive(true); PlaceInFront(); } }
        public void Hide() { if (gameObject.activeSelf) gameObject.SetActive(false); }

        /// <summary>In front of the eyes, at eye level and the guide's distance, facing them (the head's heading, never its pitch).</summary>
        public void PlaceInFront()
        {
            var cam = _head != null ? _head() : null; if (cam == null) return;
            var t = cam.transform; var f = t.forward; f.y = 0f; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
            _rt.position = t.position + f * Distance; UiKit.FaceHead(_rt, cam);
        }
    }
}
