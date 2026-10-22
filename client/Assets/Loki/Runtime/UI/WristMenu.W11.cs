using System.Collections.Generic;
using System.Text;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 11 (w11menu), from the design's 2026-10-06 playtest. The wrist menu's part of the fixes:
    ///   Size: "increase the size of the legacy menu by about 25%": <see cref="ClassicMetresPerPx"/> (0.00042 m per pixel before), and
    ///   the menu floats 25 % higher over the hand so its lower edge stays where it was against the hand (<see cref="ClassicUp"/>).
    ///   Everything drawn inside the panel (tabs, pages, the strips under it, the toast line, the details, the equipment page)
    ///   scales with it; the buff bar and the PvP counter above it and the minimap beside it are moved out by the same amount
    ///   (UI/BuffBar.cs, Game/PvpState.cs, UI/Minimap.cs read the constants here).
    ///   Comparing: the worn items' cards open BESIDE the item's details (one or two: an accessory or a one-handed weapon may
    ///   replace either side), each with its name, equipment block and description, each scrolling (UI/ItemCard.cs). The details'
    ///   own description stays (it was swapped for the worn item's text).
    ///   Quick slots: the ★ of an item or a skill opens a picker instead of taking the first free slot: the nine slots of a row
    ///   with what they hold, ▲ / ▼ through the eight rows, a slot (free or not: what it held is replaced), or Cancel.
    ///   Game/Hotkeys.cs SetAt sends the slot (0x0b21) as before.
    ///   Commands: a button on the strip under the menu opens the list of GM console commands (Game/GmCommandList.cs).
    /// Hooks in WristMenu.cs: Create (the scale), Build (BuildW11), Show (W11BeforeShow), Refresh (the picker; W11AfterRefresh),
    /// LateUpdate (the height), PinItem / PinSkill; WristMenu.Skills.cs "pin"; WristMenu.W4Items.cs (the Compare button).
    /// </summary>
    public sealed partial class WristMenu
    {
        /// <summary>The classic wrist menu's size: 25 % larger than before client wave 11 (0.00042 m per pixel: 26.9 × 46.2 cm → 33.6 × 57.8 cm).</summary>
        public const float ClassicMetresPerPx = 0.00042f * 1.25f;
        /// <summary>How far above the hand its centre floats (0.2 m before), and how much wider / taller a half of it became: what
        /// is placed above or beside it moves out by these.</summary>
        public const float ClassicUp = 0.25f;
        public const float ClassicGrewHalfWidth = W / 2 * (ClassicMetresPerPx - 0.00042f), ClassicGrewTop = ClassicUp - 0.2f + H / 2 * (ClassicMetresPerPx - 0.00042f);

        private UiScrollText _detScroll;
        /// <summary>The item details' scrolling description (smoke).</summary>
        public UiScrollText DetailScroll => _detScroll;

        // ------------------------------------------------------------------ compare cards
        private const float CardW = 560, CardH = 540;
        private readonly ItemCard[] _w11cards = new ItemCard[2];
        public ItemCard CompareCard(int i) => i >= 0 && i < _w11cards.Length ? _w11cards[i] : null;
        /// <summary>The cards are shown now (the item details of an unworn piece of equipment, comparing).</summary>
        public bool W11CompareShown => _w11cards[0] != null && _w11cards[0].Shown;

        private void BuildW11()
        {
            for (int i = 0; i < _w11cards.Length; i++)
            {
                _w11cards[i] = ItemCard.Create(_rt, "compare" + i, Vector2.zero, new Vector2(CardW, CardH));
                _w11cards[i].Root.gameObject.SetActive(false);
            }
            BuildSlotPicker();
            // the strip under the menu: "Horde" gives a third of its width to "Commands"
            float y = -H / 2 - 40 - 80 - UiKit.OutsetOf(_rt), third = (W - 20) / 3f; // (client wave 21: under the menu's wood frame)
            if (_w8Horde != null) { _w8Horde.Resize(new Vector2(third * 2 - 6, 58)); ((RectTransform)_w8Horde.transform).anchoredPosition = new Vector2(-third / 2, y); }
            _w11Commands = UiKit.Button(_rt, Lang.T("Commands"), new Vector2(third, y), new Vector2(third - 6, 58), () => OpenCommands?.Invoke(), 22);
        }

        /// <summary>The GM console commands list (LokiGame wires it: Game/GmCommandList.cs).</summary>
        public System.Action OpenCommands;
        private UiButton _w11Commands;

        private void W11BeforeShow() { if (W11PickerOpen) CloseSlotPicker(false); }

        /// <summary>After a page was refreshed: the compare cards are shown beside the details of an unworn piece of equipment
        /// while "Compare" is on, and hidden everywhere else.</summary>
        private void W11AfterRefresh()
        {
            if (_onBoard) { BoardSync(); RefreshAside(); return; } // New style: the cards beside the board (UI/WristMenu.Board.cs)
            Inventory.Entry e = null;
            bool on = _tab == Tab.Items && _detailIndex >= 0 && _w4comparing && _inv.Items.TryGetValue(_detailIndex, out e) && e.IsEquip && e.Wearing == 0;
            if (!on) { foreach (var c in _w11cards) c?.Clear(); return; }
            var worn = W4CompareTargets(e);
            // beside the menu on the body's side (the right for a menu on the left hand); on the other side while the minimap is there
            float side = (VR.Comfort.LeftHanded ? -1f : 1f) * (_minimap != null && _minimap.Visible ? -1f : 1f);
            float x = side * (W / 2 + CardW / 2 + 14);
            for (int i = 0; i < _w11cards.Length; i++)
            {
                var c = _w11cards[i];
                c.Root.anchoredPosition = new Vector2(x, H / 2 - CardH / 2 - i * (CardH + 12));
                if (i < worn.Count) c.Set(worn[i], Lang.F("You wear at {0}:", EquipmentWindow.SlotNames(worn[i].Wearing)));
                else if (i == 0) c.SetNote(Lang.F("Compared with what you wear at {0}:", EquipmentWindow.SlotNames(e.Location)), Lang.T("<color=#7fd67f>Nothing is worn there: this would fill an empty slot.</color>"));
                else c.Clear();
            }
        }

        /// <summary>The cards' texts, one after the other (the smoke's and w4items' view of the comparison).</summary>
        public string W11CompareText()
        {
            var sb = new StringBuilder();
            foreach (var c in _w11cards) if (c != null && c.Shown) sb.Append(c.HeadText).Append('\n').Append(c.NameText).Append('\n').Append(c.BodyText).Append('\n');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ the quick-slot picker
        private RectTransform _pick; private Text _pickTitle, _pickRow;
        private readonly List<(UiButton btn, Image icon, Text label)> _pickSlots = new List<(UiButton, Image, Text)>();
        private bool _pickSkill; private int _pickId, _pickCount, _pickRowIdx;
        public bool W11PickerOpen => _pick != null && _pick.gameObject.activeSelf;
        // ---- client wave 12 (w12cast): the wand's own slots as targets of the picker. With "Casting gesture: wand" selected and a SKILL
        // being placed, the row switcher goes on past the 8 hotbar rows to two more pages: "Wand: the wheel's inner parts" (the 3 x 3
        // as a compass: up, right, down and left are the wheel's four inner segments; the middle and the corners are not slots:
        // the design's sketch of 21:40 has four) and "Wand: buttons" (four cells: the stick
        // pushed up at the top, pushed down at the bottom, its click in the middle, the lower face button at the right). Pressing a
        // cell puts the skill there (what it held is replaced); pressing the cell that already holds this skill clears it. Kept
        // per character in loki-user.json (World/UserSettings.cs wandSlots). A skill may be on the hotbar and in a wand slot at once.
        // Client wave 18 (w18wand), points 13 and 14: an ITEM may go on the wand's slots too (so the two pages are offered for an item as for a
        // skill), and the compass shows the wheel's "Wand slices" slices, each on the cell nearest its direction (four: up, right, down, left
        // as before; eight: every cell round the middle).
        public const int WandQuickPage = Hotkeys.Rows, WandButtonPage = Hotkeys.Rows + 1;
        private int PickPages => VR.Comfort.CastGesture == 3 ? Hotkeys.Rows + 2 : Hotkeys.Rows;
        private static readonly string[] CompassArrows = { "↖", "↑", "↗", "←", "", "→", "↙", "↓", "↘" };
        /// <summary>The compass's cells by direction, clockwise from up (up, up-right, right, down-right, down, down-left, left, up-left).</summary>
        private static readonly int[] CompassCells = { 1, 2, 5, 8, 7, 6, 3, 0 };
        /// <summary>Client wave 18 (w18wand), point 14: the compass cell of each of n slices: the free cell whose direction is nearest the slice's
        /// middle (slice 1 at the top, clockwise; four slices: 1, 5, 7, 3 as before).</summary>
        public static int[] SliceCells(int n)
        {
            var r = new int[n]; var used = new bool[8];
            for (int i = 0; i < n; i++)
            {
                float a = i * 360f / n; int best = -1; float bd = 999f;
                for (int k = 0; k < 8; k++) { if (used[k]) continue; float d = Mathf.Abs(Mathf.DeltaAngle(a, k * 45f)); if (d < bd - 1e-3f) { bd = d; best = k; } }
                used[best] = true; r[i] = CompassCells[best];
            }
            return r;
        }
        /// <summary>The wheel's quick slot a cell of the compass page stands for (-1: none): four slices 0 up, 1 right, 2 down, 3 left (client
        /// wave 18: the slice on that cell, of "Wand slices").</summary>
        public static int WandQuickOfCell(int cell) { var cells = SliceCells(WandWheelGeo.Slices); for (int i = 0; i < cells.Length; i++) if (cells[i] == cell) return i; return -1; }
        /// <summary>Client wave 18 (w18wand), point 13: a wand slot's thing: its name and its icon (an item, or a skill).</summary>
        private static string WandThingName(int id, bool item) => item ? GameDb.GetItem(id).Name : SkillDb.Get(id).Name;
        private static Sprite WandThingIcon(int id, bool item) => item ? ItemIcon(GameDb.GetItem(id)) : SkillIcon(SkillDb.Get(id));
        /// <summary>The wand button slot a cell of the buttons page stands for (-1: none): 0 the lower face button, 1 the stick click, 2 stick up, 3 stick down.</summary>
        public static int WandButtonOfCell(int cell) => cell == 5 ? 0 : cell == 4 ? 1 : cell == 1 ? 2 : cell == 7 ? 3 : -1;
        private string WandButtonName(int b)
        {
            var (l, r) = ComfortWindow.InHands(); bool left = VR.Comfort.LeftHanded;
            return b == 0 ? VR.ControllerProfiles.FaceNames(left ? l : r, left).lower : b == 1 ? Lang.T("stick click") : b == 2 ? Lang.T("stick up") : Lang.T("stick down");
        }
        /// <summary>The row the picker shows (0-7) and what is being placed (smoke).</summary>
        public int PickerRow => _pickRowIdx;
        public (bool skill, int id) PickerItem => (_pickSkill, _pickId);

        private void BuildSlotPicker()
        {
            float top = PH / 2;
            var p = _pick = UiKit.Box(_rt, "slotpicker", new Vector2(0, PageY), new Vector2(W - 30, PH), new Color(0, 0, 0, 0));
            _pickTitle = UiKit.Label(p, "", new Vector2(0, top - 40), new Vector2(W - 70, 64), 25, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            _pickTitle.resizeTextForBestFit = true; _pickTitle.resizeTextMinSize = 23; _pickTitle.resizeTextMaxSize = 25;
            _pickUp = UiKit.Button(p, "▲", new Vector2(-W / 2 + 80, top - 124), new Vector2(110, 92), () => { _pickRowIdx = (_pickRowIdx + PickPages - 1) % PickPages; _dirty = true; }, 30);
            _pickRow = UiKit.Label(p, "", new Vector2(0, top - 124), new Vector2(W - 300, 64), 24, TextAnchor.MiddleCenter, UiKit.TextColor);
            _pickDown = UiKit.Button(p, "▼", new Vector2(W / 2 - 80, top - 124), new Vector2(110, 92), () => { _pickRowIdx = (_pickRowIdx + 1) % PickPages; _dirty = true; }, 30);
            _pickUp.Tip = () => Lang.T("Previous row"); _pickDown.Tip = () => Lang.T("Next row"); // (client wave 14, w14menu, point 7: their tips)
            float cw = (W - 50) / 3f, ch = 130;
            for (int i = 0; i < Hotkeys.QuickSlots; i++)
            {
                int slot = i, col = i % 3, row = i / 3;
                var pos = new Vector2(-(W - 50) / 2 + cw * (col + 0.5f), top - 246 - row * (ch + 8));
                var b = UiKit.Button(p, "", pos, new Vector2(cw - 8, ch), () => PickSlot(slot), 18);
                var brt = (RectTransform)b.transform;
                UiKit.Label(brt, (i + 1).ToString(), new Vector2(-cw / 2 + 26, ch / 2 - 22), new Vector2(36, 32), 24, TextAnchor.MiddleCenter, UiKit.Dim);
                var icon = UiKit.Icon(brt, new Vector2(0, 26), new Vector2(52, 52));
                var label = UiKit.Label(brt, "", new Vector2(0, -32), new Vector2(cw - 16, 58), 23, TextAnchor.MiddleCenter);
                label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate; // a long name is cut after two lines rather than made small
                _pickSlots.Add((b, icon, label));
            }
            _pickCancel = UiKit.Button(p, Lang.T("Cancel"), new Vector2(0, -top + 52), new Vector2(W - 90, 92), () => CloseSlotPicker(true), 28);
            BuildW14Picker(p, top); // client wave 14 (w14cast): the picker in the casting gesture's own layout (WristMenu.W14Cast.cs)
            p.gameObject.SetActive(false);
        }

        /// <summary>Opens the picker for an item (isSkill false; count 0) or a skill (count = the cast level): the page behind it is
        /// hidden while it is open (two layers of buttons in one place would be an ambiguity for the laser).</summary>
        public void OpenSlotPicker(bool isSkill, int id, int count)
        {
            if (Hotkeys == null || _pick == null) return;
            if (_onBoard && _board != null && _board.Shown && !W11PickerOpen) MenuNav.PushInPlace(_navBoard, CaptureBoard(), "the quick-slot picker"); // client wave 13 (w13items), item 11
            _pickSkill = isSkill; _pickId = id; _pickCount = count; _pickRowIdx = Hotkeys.CurrentRow;
            W14PickerOpened(); // client wave 14 (w14cast): the casting gesture's own layout (pad: the 3 x 3; flick: its rows; wand: its wheel)
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            foreach (var kv in _pages) kv.Value.gameObject.SetActive(false);
            foreach (var c in _w11cards) c?.Clear();
            if (_onBoard && _board != null) { BoardSync(); _board.ShowAside(false); } // New style: on the board, in the page's place
            _pick.gameObject.SetActive(true);
            _dirty = true;
            Debug.Log($"[Quick] slot picker opened for {(isSkill ? "skill" : "item")} {id}");
        }

        private void CloseSlotPicker(bool restore)
        {
            if (_pick == null || !_pick.gameObject.activeSelf) return;
            _pick.gameObject.SetActive(false);
            if (_pickFromTree) { if (!restore) _pickFromTree = false; else if (TreePickerClosed()) return; } // client wave 22 (w22tree): opened from the 3D skill tree: back to it
            if (restore && _onBoard && MenuNav.TopInPlace(_navBoard)) { MenuNav.Back(); return; } // client wave 13 (w13items), item 11: the board as it was before the picker
            if (restore) { foreach (var kv in _pages) kv.Value.gameObject.SetActive(kv.Key == _tab); _dirty = true; }
        }

        private string PickName() => _pickSkill ? SkillDb.Get(_pickId).Name : GameDb.GetItem(_pickId).Name;
        private string SlotName(Hotkeys.Slot h) => h.Empty ? "<color=#6a7080>" + Lang.T("~slot~empty") + "</color>" : h.IsSkill ? SkillDb.Get(h.Id).Name : GameDb.GetItem(h.Id).Name;

        private void RefreshSlotPicker()
        {
            if (Hotkeys == null) return;
            _pickTitle.text = Lang.F("Choose a quick slot for <color=#ffffff>{0}</color>", PickName());
            if (W14PickerRefresh()) return; // client wave 14 (w14cast): the flick's rows, the wand's wheel and its rows' wheels (WristMenu.W14Cast.cs)
            if (_pickRowIdx >= PickPages) _pickRowIdx = Hotkeys.CurrentRow;
            if (_pickRowIdx >= Hotkeys.Rows)
            {
                // the wand's pages (client wave 12)
                var ws = World.UserSettings.WandSlotsOf(_charName?.Invoke() ?? ""); bool quick = _pickRowIdx == WandQuickPage;
                _pickRow.text = quick ? Lang.T("Wand: the wheel's inner parts (your quick skills)") : Lang.T("Wand: buttons (while you hold it)");
                for (int i = 0; i < _pickSlots.Count; i++)
                {
                    var (btn, icon, label) = _pickSlots[i];
                    int b = quick ? WandQuickOfCell(i) : WandButtonOfCell(i);
                    int id = b < 0 ? 0 : quick ? ws.quick[b] : ws.buttons[b]; bool item = b >= 0 && (quick ? ws.quickItem[b] : ws.buttonsItem[b]); // (client wave 18: an item too)
                    bool mine = id > 0 && id == _pickId && item == !_pickSkill;
                    btn.Interactable = b >= 0;
                    icon.sprite = id > 0 ? WandThingIcon(id, item) : null; icon.enabled = icon.sprite != null;
                    string where = b < 0 ? "" : quick ? CompassArrows[i] : WandButtonName(b);
                    string what = b < 0 ? "" : id > 0 ? WandThingName(id, item) : "<color=#6a7080>" + Lang.T("~slot~empty") + "</color>";
                    label.text = b < 0 ? (quick && i == 4 ? "<color=#6a7080>" + Lang.T("~compass~wand") + "</color>" : "") : (mine ? "<color=#ffd060>" : "") + where + "  " + what + (mine ? "</color>" : "");
                    btn.Normal = mine ? new Color(0.45f, 0.35f, 0.15f) : UiKit.ButtonBg; btn.SetHover(btn.Hovered);
                }
                W20PaintGrid(-1); // client wave 20 (w20colors): the wand's own pages are no row: no row colour
                return;
            }
            for (int i = 0; i < _pickSlots.Count; i++) if (!_pickSlots[i].btn.Interactable && !W14PickerFlickCentre(i)) { _pickSlots[i].btn.Interactable = true; _pickSlots[i].btn.SetHover(false); }
            _pickRow.text = Lang.F("Bar {0} · row {1}", _pickRowIdx / Hotkeys.RowsPerTab + 1, _pickRowIdx % Hotkeys.RowsPerTab + 1) + W14PickerRowNote() + (_pickRowIdx == Hotkeys.CurrentRow ? "  <color=#7fd67f>" + Lang.T("(shown now)") + "</color>" : "");
            for (int i = 0; i < _pickSlots.Count; i++)
            {
                var (btn, icon, label) = _pickSlots[i];
                if (W14PickerFlickCentre(i)) { if (btn.Interactable) btn.Interactable = false; btn.SetHover(false); icon.enabled = false; label.text = W14CancelMark; continue; } // client wave 16 (w16cast): the flick's middle cancels
                var h = Hotkeys.At(_pickRowIdx, i);
                bool mine = !h.Empty && h.IsSkill == _pickSkill && h.Id == _pickId;
                icon.sprite = h.Empty ? null : h.IsSkill ? SkillIcon(SkillDb.Get(h.Id)) : ItemIcon(GameDb.GetItem(h.Id));
                icon.enabled = icon.sprite != null;
                label.text = W14PickerCellMark(i) + (mine ? "<color=#ffd060>" + SlotName(h) + "</color>" : SlotName(h));
                btn.Normal = mine ? new Color(0.45f, 0.35f, 0.15f) : UiKit.ButtonBg; if (!btn.Hovered) btn.SetHover(false);
            }
            W20PaintGrid(_pickRowIdx); // client wave 20 (w20colors), point 51: the row's nine slots framed in its colour (WristMenu.W20Colors.cs)
        }

        /// <summary>Slot i (0-8) of the picker's row takes the item / skill; what it held is replaced, and another slot of that row
        /// that already held the same thing is emptied (it moved).</summary>
        public void PickSlot(int i)
        {
            if (Hotkeys == null || !W11PickerOpen || i < 0 || i >= Hotkeys.QuickSlots) return;
            if (W14PickerFlickRow(i)) return; // client wave 14 (w14cast): the flick's first stroke: a row chosen, its 3 x 3 shown
            if (W14PickerFlickCentre(i)) return; // client wave 16 (w16cast): the flick's middle is the cancel: nothing goes there
            if (_pickRowIdx >= Hotkeys.Rows)
            {
                // a wand slot (client wave 12): put there, or cleared when it already holds this skill
                var ws = World.UserSettings.WandSlotsOf(_charName?.Invoke() ?? ""); bool quick = _pickRowIdx == WandQuickPage;
                int b = quick ? WandQuickOfCell(i) : WandButtonOfCell(i);
                if (b < 0) return; // (client wave 18, w18wand, point 13: an item may go there too)
                var ids = quick ? ws.quick : ws.buttons; var lvs = quick ? ws.quickLv : ws.buttonsLv; var items = quick ? ws.quickItem : ws.buttonsItem;
                bool clear = ids[b] == _pickId && items[b] == !_pickSkill; int held = ids[b]; bool heldItem = items[b];
                ids[b] = clear ? 0 : _pickId; lvs[b] = clear || !_pickSkill ? 0 : _pickCount; items[b] = !clear && !_pickSkill;
                World.UserSettings.Save();
                string at = quick ? Lang.F("the wand's wheel {0}", CompassArrows[i]) : Lang.F("the wand's button: {0}", WandButtonName(b));
                Toast(clear ? Lang.F("{0} removed from {1}.", PickName(), at) : held > 0 ? Lang.F("{0} → {1} (it held {2}).", PickName(), at, WandThingName(held, heldItem)) : Lang.F("{0} → {1}.", PickName(), at));
                Debug.Log($"[Quick] wand {(quick ? "quick slot" : "button slot")} {b + 1} = {(items[b] ? "item" : "skill")} {ids[b]} lv {lvs[b]}");
                CloseSlotPicker(true);
                return;
            }
            int row = _pickRowIdx;
            var old = Hotkeys.At(row, i);
            string name = PickName();
            for (int j = 0; j < Hotkeys.QuickSlots; j++)
            {
                var h = Hotkeys.At(row, j);
                if (j != i && !h.Empty && h.IsSkill == _pickSkill && h.Id == _pickId) Hotkeys.SetAt(row, j, false, 0, 0);
            }
            bool same = !old.Empty && old.IsSkill == _pickSkill && old.Id == _pickId;
            if (!same) Hotkeys.SetAt(row, i, _pickSkill, _pickId, _pickCount);
            string where = Lang.F("bar {0} row {1} slot {2}", row / Hotkeys.RowsPerTab + 1, row % Hotkeys.RowsPerTab + 1, i + 1);
            Toast(same ? Lang.F("{0} is already in {1}.", name, where) : old.Empty ? Lang.F("{0} → {1}.", name, where) : Lang.F("{0} → {1} (it held {2}).", name, where, UiKit.StripTags(SlotName(old))));
            CloseSlotPicker(true);
        }

    }
}
