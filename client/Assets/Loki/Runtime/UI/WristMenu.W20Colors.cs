using System.Collections.Generic;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 20 (w20colors), the design's point 51 (each flick row its own colour, "so that I can know at a glance what submenu I have opened")
    /// in the quick-slot picker: a row is framed in ITS colour (UI/RowColours.cs, the same as on the flick, the pad and the wand's wheel)
    /// wherever the picker shows one, in every casting gesture's layout (UI/WristMenu.W14Cast.cs):
    ///   flick rows   each of the eight row buttons framed in its row's colour (the middle, the cancel: no frame);
    ///   a row        its nine slots (the plain bar, the pad, a row reached from the flick's rows) framed in the row's colour; the wand's own
    ///                pages (no row): no frame;
    ///   the wand     its four row parts framed in their rows' colours and filled with them darkened (<see cref="W20WandRowMix"/>; the
    ///                sketch's dark red before); the inner parts (the wand's own slots) unchanged; a row's wheel: its eight cells framed.
    /// A frame is <see cref="W20FramePx"/> px just inside the button's edge, drawn over its fill (under nothing: no raycast, no collider).
    /// </summary>
    public sealed partial class WristMenu
    {
        public const float W20FramePx = 6f, W20WandRowMix = 0.45f;
        private static readonly Color W20WandRowBase = new Color(0.10f, 0.11f, 0.15f, 1f);
        private readonly List<Image[]> _w20SlotFrames = new List<Image[]>(), _w20WandFrames = new List<Image[]>(), _w20RingFrames = new List<Image[]>();

        /// <summary>The frames, made once with the picker (the grid's nine slots, the wand's four row parts, a row wheel's eight cells).</summary>
        private void W20BuildFrames()
        {
            foreach (var s in _pickSlots) _w20SlotFrames.Add(W20Frame((RectTransform)s.btn.transform));
            for (int k = 0; k < _pickWandParts.Count; k++) _w20WandFrames.Add(k >= 4 && k < 8 ? W20Frame((RectTransform)_pickWandParts[k].btn.transform) : null);
            foreach (var c in _pickRingCells) _w20RingFrames.Add(W20Frame((RectTransform)c.btn.transform));
        }
        private static Image[] W20Frame(RectTransform btn)
        {
            var size = btn.sizeDelta; float w = W20FramePx, hx = (size.x - w) * 0.5f, hy = (size.y - w) * 0.5f;
            // (client wave 21, w21kit: a row's colour is data: drawn as it is on the theme's canvas too: UiKit.Raw)
            return new[]
            {
                UiKit.Raw(UiKit.Box(btn, "rowframe", new Vector2(0f, hy), new Vector2(size.x, w), Color.white)).GetComponent<Image>(),
                UiKit.Raw(UiKit.Box(btn, "rowframe", new Vector2(0f, -hy), new Vector2(size.x, w), Color.white)).GetComponent<Image>(),
                UiKit.Raw(UiKit.Box(btn, "rowframe", new Vector2(-hx, 0f), new Vector2(w, size.y - 2f * w), Color.white)).GetComponent<Image>(),
                UiKit.Raw(UiKit.Box(btn, "rowframe", new Vector2(hx, 0f), new Vector2(w, size.y - 2f * w), Color.white)).GetComponent<Image>(),
            };
        }
        /// <summary>A frame in row <paramref name="row"/>'s colour (-1: hidden).</summary>
        private static void W20Paint(Image[] frame, int row)
        {
            if (frame == null) return;
            bool on = row >= 0; var c = RowColours.Of(row);
            foreach (var img in frame) { if (img.gameObject.activeSelf != on) img.gameObject.SetActive(on); if (on) img.color = c; }
        }

        /// <summary>The grid's nine slots framed in <paramref name="row"/>'s colour (-1: the wand's pages: no frame).</summary>
        private void W20PaintGrid(int row) { foreach (var f in _w20SlotFrames) W20Paint(f, row); }
        /// <summary>The flick's rows: each row button in its row's colour; the middle (the cancel) none.</summary>
        private void W20PaintFlickRows()
        {
            for (int i = 0; i < _w20SlotFrames.Count; i++) { int d = FlickRecognizer.DirOfCell(i); W20Paint(_w20SlotFrames[i], d < 0 ? -1 : FlickRecognizer.RowOfDir(d)); }
        }
        /// <summary>The wand's wheel: its four row parts (bar <paramref name="tab"/>'s rows 1-4) framed and filled in their rows' colours.</summary>
        private void W20PaintWand(int tab)
        {
            for (int k = 4; k < 8 && k < _pickWandParts.Count; k++)
            {
                int row = tab * Hotkeys.RowsPerTab + (k & 3); W20Paint(_w20WandFrames[k], row);
                var fill = Color.Lerp(W20WandRowBase, RowColours.Of(row), W20WandRowMix); fill.a = 1f; _pickWandParts[k].btn.Normal = fill;
            }
        }
        /// <summary>A row's wheel: its eight cells framed in <paramref name="row"/>'s colour.</summary>
        private void W20PaintRing(int row) { foreach (var f in _w20RingFrames) W20Paint(f, row); }
    }
}
