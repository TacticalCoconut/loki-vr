using System.Collections.Generic;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 14 (w14cast), the design's answer 5: "when configuring skills and their positioning on the quickslot bar, the menu that
    /// shows up needs to be the same as the currently chosen gesture type (pad will open the pads to be configured. Wheel will open
    /// the wheel and its sub-wheels, etc)". The quick-slot picker (wave 11's, WristMenu.W11.cs: the ★ of a skill or an item) opens in
    /// the layout of the casting gesture chosen in Settings (VR/Comfort.cs CastGesture, read when it opens):
    ///   off    the bar as before: a row's nine slots in a 3 x 3, ▲ / ▼ through the eight rows.
    ///   pad    the same 3 x 3, which IS the pad (slot 1 top left … 9 bottom right, UI/CastPad.cs); the row's line names the pad that
    ///          opens it: the hand and the palm (LokiGame.W12Cast.cs: the pointing hand palm down = row 1 of the shown bar, palm up =
    ///          row 2; the other hand palm down = row 3, up = row 4).
    ///   flick  first the eight rows around the middle, each in the direction of the first flick that picks it, with the icon of its
    ///          middle slot (Game/FlickRecognizer.cs: up-left = bar 1 row 1 … down-right = bar 2 row 4); a row pressed shows its 3 x 3,
    ///          each slot marked with the second flick's arrow (the middle: "●", no second flick); "Back" returns to the rows.
    ///          Client wave 16 (w16cast), design note: "Remove the center slot on the flick menu. That way the user can cancel a cast": the
    ///          middle of the rows and of a row's 3 x 3 is the cancel ("×", greyed, nothing can be put there): the flick reaches slots
    ///          1-4 and 6-9; slot 5 stays on the bar, the pad and the wand (the other layouts and the plain bar still offer it).
    ///   wand   the wand's wheel (Game/WandWheelGeo.cs, the design's sketch): its four inner parts (the wand's own quick skills: up, right,
    ///          down, left) round the middle and its four outer parts (rows 1-4 of the shown bar) beyond them; an outer part pressed
    ///          shows that row's wheel: its eight cells in a ring, slot 1 at the top right, clockwise (slot 9 is not on the wheel);
    ///          "Back" returns to the wheel. "Buttons…" opens the wand's button slots (wave 12's page). An item cannot go on the wand's
    ///          own slots (they hold skills): those parts are greyed for an item.
    ///          Client wave 18 (w18wand), point 13: an item CAN go on the wand's own slots now (the inner parts and the buttons); point 14:
    ///          the inner parts are the wheel's "Wand slices" slices (four: as before; any other count: a ring of <see cref="PickSliceR"/>
    ///          px round the middle, slice 1 at the top, clockwise; the outer parts stay at wave 14's places: <see cref="W14LayoutWand"/>).
    /// The smokes' TestPickerPage / TestPickerRow still give the plain grid of nine (wave 11 / 12's pages, every check of theirs).
    /// </summary>
    public sealed partial class WristMenu
    {
        public enum PickView { Grid, FlickRows, WandWheel, WandRow }
        private PickView _pickView = PickView.Grid; private int _pickStyle, _pickWandRow;
        /// <summary>Client wave 16 (w16cast): the grid shown is a row reached by the flick's rows (its middle is the cancel); the mark.</summary>
        private bool _pickFlickGrid;
        private const string W14CancelMark = "<color=#ff6b61>×</color>";
        /// <summary>Cell i of the grid is the flick's cancel now (the middle of a row reached by the flick's rows).</summary>
        private bool W14PickerFlickCentre(int i) => i == 4 && _pickStyle == 2 && _pickView == PickView.Grid && _pickFlickGrid && _pickRowIdx < Hotkeys.Rows;
        private UiButton _pickUp, _pickDown, _pickCancel, _pickBack, _pickWandButtons;
        private RectTransform _pickWand, _pickRing; private Text _pickWandMiddle, _pickRingMiddle;
        private readonly List<(UiButton btn, Image icon, Text label)> _pickWandParts = new List<(UiButton, Image, Text)>(), _pickRingCells = new List<(UiButton, Image, Text)>();
        // ---- client wave 18 (w18wand), point 14: the picker's wheel laid out for this many slices (-1: not yet); a part's index for slice i
        // (0-3 the first four, as wave 14's indices; 8-11 slices 5-8); the generic ring's radius and a part's size there (px): no two parts
        // touch for any count up to 8, none reaches the outer parts (wave 14's places) or the middle's "Bar n"
        private int _pickSlicesLaid = -1;
        private static int PartOfSlice(int i) => i < 4 ? i : 4 + i;
        private static int SliceOfPart(int k) => k < 4 ? k : k >= 8 ? k - 4 : -1;
        public const float PickSliceR = 130f, PickSliceW = 90f, PickSliceH = 56f;
        /// <summary>The picker's layout now and the casting gesture it was opened for (0 off, 1 pad, 2 flick, 3 wand).</summary>
        public PickView PickerView => _pickView;
        public int PickerStyle => _pickStyle;
        /// <summary>The wand's wheel in the picker: the inner parts' and the outer parts' distance from its middle, their sizes; the row's
        /// wheel: its cells' distance from its middle and their size (the panel's pixels).</summary>
        public const float PickInnerR = 105f, PickOuterR = 195f, PickRingR = 185f;
        private static readonly string[] FlickArrows = { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" }; // the directions 0-7, clockwise from up (Game/FlickRecognizer.cs)

        private void BuildW14Picker(RectTransform p, float top)
        {
            // "Back" shares the bottom line with Cancel while a second layout is shown (the flick's 3 x 3, a wand row's wheel)
            _pickBack = UiKit.Button(p, Lang.T("Back"), new Vector2(-(W - 90) / 4f - 5f, -top + 52), new Vector2((W - 90) / 2f - 10f, 92), () => W14PickerBack(), 28);
            _pickBack.gameObject.SetActive(false);
            // the wand's wheel: four inner parts round the middle, four outer parts beyond them (up, right, down, left)
            _pickWand = UiKit.Box(p, "pickwand", new Vector2(0, -25), new Vector2(W - 40, 500), new Color(0, 0, 0, 0));
            for (int k = 0; k < 8; k++)
            {
                int part = k, d = k & 3; bool outer = k >= 4; var axis = WandWheelGeo.Axis(d);
                bool vertical = (d & 1) == 0;
                var size = outer ? (vertical ? new Vector2(250, 64) : new Vector2(96, 170)) : (vertical ? new Vector2(160, 84) : new Vector2(130, 84));
                var pos = axis * (outer ? (vertical ? PickOuterR : 250f) : (vertical ? PickInnerR : 125f));
                var b = UiKit.Button(_pickWand, "", pos, size, () => W14WandPart(part), 18);
                var brt = (RectTransform)b.transform;
                var icon = UiKit.Icon(brt, outer ? (vertical ? new Vector2(-size.x / 2 + 30, 0) : new Vector2(0, 50)) : new Vector2(0, 16), new Vector2(40, 40));
                var label = UiKit.Label(brt, "", outer ? (vertical ? new Vector2(18, 0) : new Vector2(0, -24)) : new Vector2(0, -22), outer ? (vertical ? new Vector2(size.x - 60, 60) : new Vector2(size.x - 8, 110)) : new Vector2(size.x - 10, 40), 18, TextAnchor.MiddleCenter);
                label.resizeTextForBestFit = true; label.resizeTextMinSize = 12; label.resizeTextMaxSize = 18;
                b.Normal = outer ? new Color(0.36f, 0.06f, 0.09f) : new Color(0.42f, 0.28f, 0.2f); b.SetHover(false); // (the sketch's dark red and tan)
                _pickWandParts.Add((b, icon, label));
            }
            // client wave 18 (w18wand), point 14: slices 5-8 of the inner ring (indices 8-11; shown only with more than four slices)
            for (int k = 8; k < 12; k++)
            {
                int part = k;
                var b = UiKit.Button(_pickWand, "", Vector2.zero, new Vector2(PickSliceW, PickSliceH), () => W14WandPart(part), 18);
                var brt = (RectTransform)b.transform;
                var icon = UiKit.Icon(brt, new Vector2(0, 11), new Vector2(28, 28));
                var label = UiKit.Label(brt, "", new Vector2(0, -15), new Vector2(PickSliceW - 8, 24), 15, TextAnchor.MiddleCenter);
                label.resizeTextForBestFit = true; label.resizeTextMinSize = 11; label.resizeTextMaxSize = 15;
                b.Normal = new Color(0.42f, 0.28f, 0.2f); b.SetHover(false);
                _pickWandParts.Add((b, icon, label));
                b.gameObject.SetActive(false);
            }
            _pickWandMiddle = UiKit.Label(_pickWand, "", Vector2.zero, new Vector2(110, 60), 18, TextAnchor.MiddleCenter, UiKit.Dim);
            _pickWandButtons = UiKit.Button(_pickWand, Lang.T("Buttons…"), new Vector2(-(W - 40) / 2 + 75, -195), new Vector2(140, 60), () => { _pickView = PickView.Grid; _pickRowIdx = WandButtonPage; _dirty = true; }, 20);
            _pickWand.gameObject.SetActive(false);
            // a row's wheel: eight cells in a ring, slot 1 at the top right, clockwise (Game/WandWheelGeo.cs CellMid)
            _pickRing = UiKit.Box(p, "pickring", new Vector2(0, -25), new Vector2(W - 40, 500), new Color(0, 0, 0, 0));
            for (int c = 0; c < 8; c++)
            {
                int cell = c; var pos = WandWheelGeo.Dir(c * 45f + 22.5f) * PickRingR;
                var b = UiKit.Button(_pickRing, "", pos, new Vector2(126, 84), () => W14WandCell(cell), 18);
                var brt = (RectTransform)b.transform;
                UiKit.Label(brt, (c + 1).ToString(), new Vector2(-46, 26), new Vector2(30, 28), 18, TextAnchor.MiddleCenter, UiKit.Dim);
                var icon = UiKit.Icon(brt, new Vector2(0, 14), new Vector2(40, 40));
                var label = UiKit.Label(brt, "", new Vector2(0, -24), new Vector2(118, 36), 17, TextAnchor.MiddleCenter);
                label.resizeTextForBestFit = true; label.resizeTextMinSize = 12; label.resizeTextMaxSize = 17;
                b.Normal = new Color(0.42f, 0.28f, 0.2f); b.SetHover(false);
                _pickRingCells.Add((b, icon, label));
            }
            _pickRingMiddle = UiKit.Label(_pickRing, "", Vector2.zero, new Vector2(150, 70), 20, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.45f));
            _pickRing.gameObject.SetActive(false);
            W20BuildFrames(); // client wave 20 (w20colors), point 51: the rows' colour frames (WristMenu.W20Colors.cs)
        }

        /// <summary>The picker opened: its layout from the casting gesture in force.</summary>
        private void W14PickerOpened()
        {
            _pickStyle = VR.Comfort.CastGesture; _pickFlickGrid = false;
            _pickView = _pickStyle == 2 ? PickView.FlickRows : _pickStyle == 3 ? PickView.WandWheel : PickView.Grid;
            Debug.Log($"[Quick] the picker in the {VR.Comfort.CastGestures[Mathf.Clamp(_pickStyle, 0, 3)]} layout");
        }

        /// <summary>"Back": the flick's 3 x 3 → its rows; a wand row's wheel, or the grid reached from the wand → the wand's wheel.</summary>
        private void W14PickerBack()
        {
            if (_pickView == PickView.WandRow || (_pickView == PickView.Grid && _pickStyle == 3)) _pickView = PickView.WandWheel;
            else if (_pickView == PickView.Grid && _pickStyle == 2) _pickView = PickView.FlickRows;
            _pickFlickGrid = false; _dirty = true;
        }
        private bool W14BackShown => _pickView == PickView.WandRow || (_pickView == PickView.Grid && (_pickStyle == 2 || _pickStyle == 3));

        /// <summary>One refresh of the picker: the parts of the layout shown, and the layouts other than the grid drawn. True: done
        /// (the grid is not drawn).</summary>
        private bool W14PickerRefresh()
        {
            bool grid = _pickView == PickView.Grid || _pickView == PickView.FlickRows;
            bool nav = _pickView == PickView.Grid;
            if (_pickUp.gameObject.activeSelf != nav) { _pickUp.gameObject.SetActive(nav); _pickDown.gameObject.SetActive(nav); }
            foreach (var s in _pickSlots) if (s.btn.gameObject.activeSelf != grid) s.btn.gameObject.SetActive(grid);
            if (_pickWand.gameObject.activeSelf != (_pickView == PickView.WandWheel)) _pickWand.gameObject.SetActive(_pickView == PickView.WandWheel);
            if (_pickRing.gameObject.activeSelf != (_pickView == PickView.WandRow)) _pickRing.gameObject.SetActive(_pickView == PickView.WandRow);
            bool back = W14BackShown;
            if (_pickBack.gameObject.activeSelf != back)
            {
                _pickBack.gameObject.SetActive(back);
                float top = PH / 2;
                ((RectTransform)_pickCancel.transform).anchoredPosition = back ? new Vector2((W - 90) / 4f + 5f, -top + 52) : new Vector2(0, -top + 52);
                _pickCancel.Resize(back ? new Vector2((W - 90) / 2f - 10f, 92) : new Vector2(W - 90, 92));
            }
            switch (_pickView)
            {
                case PickView.FlickRows: W14DrawFlickRows(); return true;
                case PickView.WandWheel: W14DrawWandWheel(); return true;
                case PickView.WandRow: W14DrawWandRow(); return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ the pad and the flick (the grid of nine)
        /// <summary>The grid's row line: the pad that opens this row (pad), the first flick that picks it (flick).</summary>
        private string W14PickerRowNote()
        {
            if (_pickRowIdx >= Hotkeys.Rows) return "";
            int k = _pickRowIdx % Hotkeys.RowsPerTab;
            if (_pickStyle == 1)
            {
                bool rightMain = !VR.Comfort.LeftHanded, right = k < 2 ? rightMain : !rightMain, up = (k & 1) == 1;
                string hand = right ? (up ? Lang.T("Right hand, palm up") : Lang.T("Right hand, palm down")) : (up ? Lang.T("Left hand, palm up") : Lang.T("Left hand, palm down"));
                return "\n<size=19>" + hand + (_pickRowIdx / Hotkeys.RowsPerTab != Hotkeys.CurrentTab ? " · " + Lang.T("other bar") : "") + "</size>";
            }
            if (_pickStyle == 2) return "  " + FlickArrows[FlickRecognizer.DirOfRow(_pickRowIdx)];
            return "";
        }
        /// <summary>A grid cell's mark: the flick's second-flick arrow (the middle: ●, no second flick).</summary>
        private string W14PickerCellMark(int cell)
        {
            if (_pickStyle != 2 || _pickView != PickView.Grid) return "";
            int d = FlickRecognizer.DirOfCell(cell);
            return (d < 0 ? "●" : FlickArrows[d]) + " ";
        }

        /// <summary>The flick's first stroke: the eight rows in their directions, the middle slot's icon on each (client wave 16: else the
        /// row's first filled slot's, as the flick's own plates: UI/FlickHud.cs RowIconSlot).</summary>
        private void W14DrawFlickRows()
        {
            _pickRow.text = Lang.T("First flick: choose a row");
            for (int i = 0; i < _pickSlots.Count; i++)
            {
                var (btn, icon, label) = _pickSlots[i];
                if (i == 4) { btn.Interactable = false; btn.SetHover(false); icon.enabled = false; label.text = W14CancelMark; continue; } // (client wave 16: the start cancels)
                if (!btn.Interactable) { btn.Interactable = true; btn.SetHover(false); }
                int d = FlickRecognizer.DirOfCell(i), row = FlickRecognizer.RowOfDir(d);
                int at = FlickHud.RowIconSlot(Hotkeys, row); var h = at >= 0 ? Hotkeys.At(row, at) : default; // (client wave 16, the design's answer: slot 5's icon, else the first filled slot's, as the flick)
                icon.sprite = h.Empty ? null : h.IsSkill ? SkillIcon(SkillDb.Get(h.Id)) : ItemIcon(GameDb.GetItem(h.Id)); icon.enabled = icon.sprite != null;
                label.text = FlickArrows[d] + " " + Lang.F("Bar {0} · row {1}", row / Hotkeys.RowsPerTab + 1, row % Hotkeys.RowsPerTab + 1);
                btn.Normal = UiKit.ButtonBg; if (!btn.Hovered) btn.SetHover(false);
            }
            W20PaintFlickRows(); // client wave 20 (w20colors): each row button in its row's colour
        }
        /// <summary>A cell pressed in the flick's rows: that row's 3 x 3. True: it was the rows' layout (nothing placed).</summary>
        private bool W14PickerFlickRow(int cell)
        {
            if (_pickView != PickView.FlickRows) return false;
            int d = FlickRecognizer.DirOfCell(cell); if (d < 0) return true;
            _pickRowIdx = FlickRecognizer.RowOfDir(d); _pickView = PickView.Grid; _pickFlickGrid = true; _dirty = true;
            for (int i = 0; i < _pickSlots.Count; i++) if (!_pickSlots[i].btn.Interactable) { _pickSlots[i].btn.Interactable = true; _pickSlots[i].btn.SetHover(false); }
            Debug.Log($"[Quick] the picker: flick {FlickArrows[d]}: bar {_pickRowIdx / Hotkeys.RowsPerTab + 1} row {_pickRowIdx % Hotkeys.RowsPerTab + 1}");
            return true;
        }

        // ------------------------------------------------------------------ the wand
        private string W14Empty => "<color=#9aa3b5>" + Lang.T("~slot~empty") + "</color>";
        private void W14Fill((UiButton btn, Image icon, Text label) ui, Hotkeys.Slot h, string prefix)
        {
            bool mine = !h.Empty && h.IsSkill == _pickSkill && h.Id == _pickId;
            ui.icon.sprite = h.Empty ? null : h.IsSkill ? SkillIcon(SkillDb.Get(h.Id)) : ItemIcon(GameDb.GetItem(h.Id)); ui.icon.enabled = ui.icon.sprite != null;
            ui.label.text = prefix + (h.Empty ? W14Empty : mine ? "<color=#ffd060>" + SlotName(h) + "</color>" : SlotName(h));
        }

        private void W14DrawWandWheel()
        {
            var ws = World.UserSettings.WandSlotsOf(_charName?.Invoke() ?? ""); int tab = Hotkeys.CurrentTab;
            int n = WandWheelGeo.Slices; if (n != _pickSlicesLaid) W14LayoutWand(n); // client wave 18 (w18wand), point 14
            _pickRow.text = Lang.T("Wand wheel");
            _pickWandMiddle.text = Lang.F("Bar {0}", tab + 1);
            if (!_pickWandButtons.gameObject.activeSelf) _pickWandButtons.gameObject.SetActive(true); // (client wave 18, point 13: an item may go on the buttons too)
            var cells = SliceCells(n);
            W20PaintWand(tab); // client wave 20 (w20colors): the row parts in their rows' colours (before the hover state below paints them)
            for (int k = 0; k < _pickWandParts.Count; k++)
            {
                var ui = _pickWandParts[k]; int d = k & 3, slice = SliceOfPart(k);
                if (slice >= n) continue; // (not shown with this many slices)
                if (slice >= 0)
                {
                    // an inner part: the wand's own quick slot (client wave 18, point 13: a skill or an item)
                    int id = ws.quick[slice]; bool item = ws.quickItem[slice]; bool mine = id > 0 && id == _pickId && item == !_pickSkill;
                    if (!ui.btn.Interactable) ui.btn.Interactable = true;
                    ui.icon.sprite = id > 0 ? WandThingIcon(id, item) : null; ui.icon.enabled = ui.icon.sprite != null;
                    string name = id > 0 ? WandThingName(id, item) : W14Empty;
                    ui.label.text = CompassArrows[cells[slice]] + " " + (mine ? "<color=#ffd060>" + name + "</color>" : name);
                }
                else
                {
                    // an outer part: a row of the shown bar (its wheel opens when pressed)
                    int row = tab * Hotkeys.RowsPerTab + d; Hotkeys.Slot first = default;
                    for (int s = 0; s < 8; s++) { var h = Hotkeys.At(row, s); if (!h.Empty) { first = h; break; } }
                    ui.icon.sprite = first.Empty ? null : first.IsSkill ? SkillIcon(SkillDb.Get(first.Id)) : ItemIcon(GameDb.GetItem(first.Id)); ui.icon.enabled = ui.icon.sprite != null;
                    ui.label.text = Lang.F("Row {0}", d + 1);
                    if (!ui.btn.Interactable) ui.btn.Interactable = true;
                }
                if (!ui.btn.Hovered) ui.btn.SetHover(false);
            }
        }
        /// <summary>A part of the wand's wheel pressed: an inner part takes the skill (or lets it go when it already holds it); an outer
        /// part opens its row's wheel.</summary>
        private void W14WandPart(int k)
        {
            if (!W11PickerOpen || _pickView != PickView.WandWheel) return;
            int d = k & 3, slice = SliceOfPart(k);
            if (slice < 0) { _pickWandRow = d; _pickView = PickView.WandRow; _dirty = true; Debug.Log($"[Quick] the picker: the wand's row {d + 1}"); return; }
            if (slice >= WandWheelGeo.Slices) return;
            // the same as wave 12's compass page (four slices: its cells 1, 5, 7, 3 are up, right, down, left; client wave 18: the slice's own cell)
            _pickView = PickView.Grid; _pickRowIdx = WandQuickPage;
            PickSlot(SliceCells(WandWheelGeo.Slices)[slice]);
        }

        /// <summary>Client wave 18 (w18wand), point 14: the picker's wheel for n slices. Four: wave 14's places, unchanged. Any other count: the n
        /// inner parts on a ring of <see cref="PickSliceR"/> px (slice 1 at the top, clockwise; <see cref="PickSliceW"/> x <see cref="PickSliceH"/>
        /// px each: no two touch for any count up to 8, and they stay clear of the four outer parts (the rows, at wave 14's places), of the
        /// middle's "Bar n", of "Buttons…" and of Cancel).</summary>
        private void W14LayoutWand(int n)
        {
            _pickSlicesLaid = n;
            for (int k = 0; k < _pickWandParts.Count; k++)
            {
                var ui = _pickWandParts[k]; var brt = (RectTransform)ui.btn.transform; int d = k & 3, slice = SliceOfPart(k);
                bool vertical = (d & 1) == 0; var axis = WandWheelGeo.Axis(d);
                if (slice < 0) continue; // an outer part (a row): wave 14's place, whatever the count
                bool on = slice < n; if (ui.btn.gameObject.activeSelf != on) ui.btn.gameObject.SetActive(on);
                if (!on) continue;
                Vector2 size = n == 4 ? (vertical ? new Vector2(160, 84) : new Vector2(130, 84)) : new Vector2(PickSliceW, PickSliceH);
                brt.anchoredPosition = n == 4 ? axis * (vertical ? PickInnerR : 125f) : WandWheelGeo.Dir(slice * 360f / n) * PickSliceR;
                ui.btn.Resize(size);
                ((RectTransform)ui.icon.transform).anchoredPosition = new Vector2(0, n == 4 ? 16f : 11f); ((RectTransform)ui.icon.transform).sizeDelta = Vector2.one * (n == 4 ? 40f : 28f);
                var lr = (RectTransform)ui.label.transform; lr.anchoredPosition = new Vector2(0, n == 4 ? -22f : -15f); lr.sizeDelta = new Vector2(size.x - 8f - (n == 4 ? 2f : 0f), n == 4 ? 40f : 24f);
            }
            Debug.Log($"[Quick] the picker's wand wheel laid out for {n} slices");
        }

        private void W14DrawWandRow()
        {
            int row = Hotkeys.CurrentTab * Hotkeys.RowsPerTab + _pickWandRow;
            _pickRow.text = Lang.T("Wand wheel");
            _pickRingMiddle.text = Lang.F("Row {0}", _pickWandRow + 1);
            for (int c = 0; c < 8; c++)
            {
                var ui = _pickRingCells[c];
                W14Fill(ui, Hotkeys.At(row, c), "");
                if (!ui.btn.Hovered) ui.btn.SetHover(false);
            }
            W20PaintRing(row); // client wave 20 (w20colors): the row's colour
        }
        /// <summary>A cell of a wand row's wheel pressed: that row's slot (1-8) takes the skill / item, as the grid's slot would.</summary>
        private void W14WandCell(int c)
        {
            if (!W11PickerOpen || _pickView != PickView.WandRow) return;
            _pickView = PickView.Grid; _pickRowIdx = Hotkeys.CurrentTab * Hotkeys.RowsPerTab + _pickWandRow;
            PickSlot(c);
        }

    }
}
