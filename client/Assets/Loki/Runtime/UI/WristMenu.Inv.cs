using System.Collections.Generic;
using Loki.Game;
using Loki.World;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 21 (w21inv), the design's point 62 (2026-10-09): THE NEW INVENTORY in place of the board's bag page (the picture and what
    /// is drawn: UI/InvPanel.cs; the curve: UI/InvBent.cs). Here: when it is shown, how it opens, and every item action of the old bag
    /// done through it (the same calls: nothing new is sent to the server).
    ///   WHEN: the new menu style, the Items group's Bag page, "Inventory: New" (Settings -> Controls -> Menus; default New; the old
    ///   grid page stays as "Old": <see cref="InvNew"/>). The inventory is drawn under the menu board's own root with the board's own
    ///   picture hidden (<see cref="InvSync"/>); an item's Details, the quick-slot picker, the Equipment / Bank / Cash shop pages are
    ///   the board as before (the inventory gives the board back) and Back or the foot line's buttons lead between them.
    ///   OPENING (design note: "It opens by smoothly 'sliding open' from left to right ... If you reopen the inventory again within 5s of
    ///   having closed it, the sliding animation is skipped. If you pull the inventory out of your backpack, it comes as a compacted
    ///   panel that springs out to the left and right ... Add a sound effect"): from the ring it UNROLLS left to right
    ///   (InvPanel.UnrollSeconds, within the menu's 0.8 s: UI/MenuAnim.cs key Inventory: its switch, its 5 s rule; the cells take the
    ///   laser from the first frame); out of the backpack it is a COMPACT bundle in the hand (held by the frame's bottom middle, the
    ///   bag's reach beyond the hand and the stick's push as before: LokiGame.W13Grab.cs / W15Grab.cs) that SPRINGS open to both sides
    ///   when the hand has brought it <see cref="PackSpringAhead"/> m in front of the body, or lets it go.
    ///   ITEMS: the pointer on a cell outlines it in gold; resting there <see cref="InvPanel.CardDwell"/> s shows its parchment card
    ///   beside its section (the whole item, as the old item panel) and beside that what is worn where it would go (the comparison);
    ///   the cards go when another cell is pointed at, or <see cref="CardLinger"/> s after the pointer left the cells and the cards.
    ///   The strip's buttons behind a shown card are switched off (a card is in front of them); the items' cells behind it too once
    ///   an item is locked (before that the pointer on a cell behind the card chooses that cell: the card goes to it). The bag's lock is the old bag's (UI/WristMenu.Board.cs): a pull on an item locks it, a pull on the
    ///   locked item or anywhere on the bag that is no button uses it; the card's buttons: its action, Details, ★ Quick slot, Unlock.
    ///   THE EQUIPMENT PANEL (design note, 2026-10-11: "a small cloth tab at the top of the center portion that, when clicked, opens an extra
    ///   panel above it that shows the character equipment screen ... It has the same ability to show you what pieces of equipment can
    ///   go on a given slot, lasering over a slot highlights valid equipments on the inventory. This defaults to closed, but can be
    ///   'pinned' such that it always pulls open immediately after the inventory's base opening animation finishes playing"; "The state
    ///   of the 'pinning' is stored on the per-account settings memory"): the tab pulls up THE BAND over the strip's middle (the
    ///   character, its slots as cells with the worn item's picture and the slot's name, one line for the slot pointed at; drawn by
    ///   UI/InvPanel.cs), or, InvPanel.EquipFull, the equipment screen itself at its full size (the proposal's other half). Either
    ///   way the rules are a second UI/EquipmentWindow.cs's (its own logic: what fits a slot, the equip into a slot, the take-off; on
    ///   the full screen its own picture too). The slot under the laser or the look: every item that the equipment screen's
    ///   own rule lets go there (EquipmentWindow.Candidates) is outlined in gold in the strip, the other cells are dimmed; leaving the
    ///   slot clears it. A slot PRESSED is armed: its highlight stays, and a pull on a highlighted item equips it into that slot (the
    ///   equipment screen's own call, EquipInto); armed ends with that equip, the slot pressed again, another slot pressed, a pull on
    ///   another cell, the panel folded.
    ///   With the look on a slot and the laser on a highlighted item the pull does the same. Pinned (the round button beside the tab;
    ///   World/UserSettings.cs invEquipPinned, with the account): it pulls open by itself when the base opening has ended.
    /// Hooks: WristMenu.cs Refresh, RefreshPageOf; WristMenu.Board.cs InitBoard, BoardSync, BoardTick, BagPull, OpenBag, RefreshAside,
    /// KeepBoardPlace and the grid's public hooks (they answer for whichever bag is shown); LokiGame.W13Grab.cs (the held point).
    /// </summary>
    public sealed partial class WristMenu
    {
        private InvPanel _invPanel; private bool _invOn, _invFromPack, _invPackAnimate, _invBaseDone = true, _invBaseAnimated;
        private int _invHover = -1; private float _invHoverAt, _invAwayAt = -1f;
        private EquipmentWindow _invEquip; private uint _invSlotShown, _invSlotArmed; private float _invEquipFoldedAt = -100f;
        private readonly HashSet<int> _invFit = new HashSet<int>();
        /// <summary>The cards go this long after the pointer left the cells and the cards (not while an item is locked).</summary>
        public const float CardLinger = 0.5f;
        /// <summary>The bundle out of the backpack springs open when its middle is this far in front of the body (m), or let go.</summary>
        public const float PackSpringAhead = 0.35f;
        public InvPanel Inv => _invPanel;
        /// <summary>The inventory's own equipment view (its panel is pulled up by the tab).</summary>
        public EquipmentWindow InvEquip => _invEquip;
        /// <summary>The slot whose fitting items are highlighted now (its position bit; 0: none), and the armed one.</summary>
        public uint InvSlotShown => _invSlotShown;
        public uint InvSlotArmed => _invSlotArmed;
        /// <summary>The body's heading, degrees about the vertical (LokiGame.W13Grab.cs gives the wand holster's estimate; none: the head's).</summary>
        public System.Func<float> BodyYaw { get; set; }
        /// <summary>The new inventory is shown now (in place of the board's own picture).</summary>
        public bool InvShown => _invOn && _board != null && _board.Shown && gameObject.activeSelf;
        public static int ItemsGroup => GroupOf(Tab.Items).group;

        /// <summary>"Inventory: New / Old" (Settings -> Controls -> Menus): true = the new inventory.</summary>
        public static bool InvNew
        {
            get
            {
                return !UserSettings.Prefs.invOld;
            }
        }
        /// <summary>The Settings row was pressed.</summary>
        public static void SetInvOld(bool old)
        {
            if (UserSettings.Prefs.invOld == old) return;
            UserSettings.Prefs.invOld = old; UserSettings.Prefs.saved = true; UserSettings.Save();
            Debug.Log($"[Inv] inventory: {(old ? "old (the bag page)" : "new")}");
        }

        /// <summary>The bag page is wanted and it is the new inventory.</summary>
        private bool InvWanted => GridWanted && InvNew;

        private void BuildInv()
        {
            _invPanel = InvPanel.Create(_board);
            _invPanel.CellPressed = InvPress;
            _invPanel.EntryPressed = e => { int g = ItemsGroup; if (e > 0 && e < Groups[g].Entries.Length) Activate(g, e); };
            _invPanel.EntryLabel = e => { var en = Groups[ItemsGroup].Entries; return e > 0 && e < en.Length ? EntryLabel(en[e]) + (en[e].Page.HasValue ? "" : "  ›") : ""; };
            _invPanel.SaleLock = () => SaleLock;
            _invPanel.SaleLockPressed = () => { SaleLock = !SaleLock; Toast(SaleLock ? Lang.T("Favourites won't show in NPC sell lists.") : Lang.T("Favourites can be sold to NPCs again.")); _dirty = true; };
            _invPanel.CardButton = InvCardButton;
            _invPanel.TabPressed = () => InvEquipShow(!_invPanel.EquipOpen);
            _invPanel.PinPressed = () => { InvPanel.SetPinned(!InvPanel.Pinned); if (InvPanel.Pinned && !_invPanel.EquipOpen && _invPanel.State == InvPanel.Mode.Open) InvEquipShow(true); _dirty = true; };
            // the equipment view: a second EquipmentWindow (it keeps no shared state: only its own choice, its own doll), on the panel's page
            _invEquip = new EquipmentWindow(_inv, _ps, _s, m => Toast(m), idx => OpenDetailsFromBag(idx), () => InvEquipShow(false), () => _dirty = true, InvPanel.EquipPageW, InvPanel.EquipPageH);
            _invEquip.Build(_invPanel.EquipPage);
            _invEquip.Show(true);
            // a slot pressed (on the band, or on the full screen) is armed; pressed again, it lets go
            _invEquip.Pressed = InvSlotPressed; _invPanel.BandPressed = InvSlotPressed;
            _invPanel.BandTakeOffPressed = () => { if (_invSlotArmed != 0) { _invEquip.Select(_invSlotArmed); _invEquip.TakeOff(); } };
            MenuAnim.Own(MenuAnim.Inventory); // its own opening: the unroll, the spring (the registry's switch, limit and 5 s rule stay)
        }

        private void InvSlotPressed(uint bit)
        {
            bool off = _invSlotArmed == bit; _invSlotArmed = off ? 0u : bit; _dirty = true;
            Debug.Log(off ? $"[Inv] slot {EquipmentWindow.SlotNames(bit)} let go" : $"[Inv] slot {EquipmentWindow.SlotNames(bit)} armed: a pull on a highlighted item equips it there");
        }
        /// <summary>A slot's button on the equipment panel that is up (the band's cell, or the full screen's; null: not shown).</summary>
        public UiButton InvSlotButton(uint bit) => _invPanel.EquipIsFull ? _invEquip.CellButton(bit) : _invPanel.BandButton(bit);

        /// <summary>Shows the inventory in place of the board's own picture when it is wanted, gives the board back when it is not.
        /// <paramref name="onlyOff"/>: only the giving back (before the board changes its shape: BoardSync).</summary>
        private void InvSync(bool onlyOff = false)
        {
            if (_invPanel == null || _board == null) return;
            bool want = _onBoard && gameObject.activeSelf && InvWanted && !W11PickerOpen;
            if (want == _invOn || (want && onlyOff)) return;
            _invOn = want;
            if (want)
            {
                _board.ShowAside(false); _pItem?.Clear(); _pWornA?.Clear(); _pWornB?.Clear();
                if (_grid != null && _grid.gameObject.activeSelf) _grid.gameObject.SetActive(false);
            }
            else InvEquipForget();
            _invPanel.Show(want);
            if (want)
            {
                // client wave 23 (w23inv): shown for the first time on a board that is open already (it was on another page or group):
                // stood upright there (a board that opens now is stood when it has been placed: InvPlaced)
                if (_board.Shown && _board.Placed != _invStoodPlaced && !_board.Grab.Held) { InvStand("shown on the open board"); UpdateEye(); }
                _invPanel.Rebend(_eye); _invHover = -1; _invAwayAt = -1f; _invBaseDone = false; _invBaseAnimated = false; // (the pinned panel pulls open when the strip is whole: InvTick)
            }
            _dirty = true;
        }

        /// <summary>The board was placed or let go: the strip is bent round the eyes of that place. Client wave 23 (w23inv): a board
        /// that was PLACED (opened: UI/MenuBoard.cs Open counts its placings) is stood upright first; one a hand let go keeps the
        /// turn the hand gave it.</summary>
        private void InvPlaced()
        {
            if (!_invOn || _invPanel == null) return;
            if (_board.Placed != _invStoodPlaced) { InvStand("the board was opened"); UpdateEye(); }
            _invPanel.Rebend(_eye);
        }

        // ------------------------------------------------------------------ client wave 23 (w23inv): the inventory always spawns upright
        // design note (2026-10-11): "the inventory menu should always spawn upright". Every board opens leaning back to face
        // the eyes (UI/MenuBoard.cs Open: UiKit.FaceHead: at its usual place, 10 cm under the eyes and 65 cm ahead, 8.7 degrees; lower
        // down, more), and a board put back where it was (UI/MenuNav.cs: the back arrow) keeps the turn a hand gave it. THE NEW
        // INVENTORY NEVER DOES: each time it comes up it stands upright where the board is: no pitch, no roll, turned about the
        // vertical to face the player; its place (the board's usual distance and height) is not touched. "Comes up" = the board
        // opened with it (the ring, a reopening within 5 s, the back arrow), the inventory shown for the first time on a board that
        // was open on something else, and the bundle out of the backpack springing open (carried, it is upright in the hand whatever
        // "Panel tilt" says: LokiGame.W13Grab.cs). Within one opening a turn given by hand (its edge taken with "Panel tilt: Free",
        // or shaken) stays until the board closes: it is never kept for the next opening.
        private int _invStoodPlaced = -1;
        /// <summary>Times the inventory was stood upright, and how far the last one turned the board (degrees) (smoke, log).</summary>
        public int InvStands { get; private set; }
        public float InvLastStandTurn { get; private set; }
        private void InvStand(string why)
        {
            if (_board == null || _head == null) return;
            _invStoodPlaced = _board.Placed;
            var t = _board.transform; var was = t.rotation;
            var d = t.position - _head.transform.position; d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) { d = was * Vector3.forward; d.y = 0f; if (d.sqrMagnitude < 1e-6f) d = Vector3.forward; } // (right over or under the eyes: its own heading)
            t.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
            InvStands++; InvLastStandTurn = Quaternion.Angle(was, t.rotation);
            Debug.Log($"[Inv] stood upright ({why}): turned {InvLastStandTurn:0.0} degrees; no pitch, no roll, facing the player");
        }

        /// <summary>The board was shown now (it was closed): the Items group's opening (see the class).</summary>
        private void InvOpened()
        {
            bool pack = _invFromPack; _invFromPack = false;
            if (_invPanel == null || _group != ItemsGroup) return;
            float limit = MenuAnim.MaxSeconds(MenuAnim.Inventory) - MenuAnim.EndMargin;
            if (_invOn)
            {
                _invBaseDone = false;
                _invPanel.ShowEquip(false, 0f); InvEquipForget(); // (every opening begins with the equipment panel folded: pinned, it pulls open when the base opening has ended)
                if (pack) { _invPackAnimate = MenuAnim.ShouldPlay(MenuAnim.Inventory); _invBaseAnimated = _invPackAnimate; _invPanel.SetCompact(); _dirty = true; }
                else if (MenuAnim.ShouldPlay(MenuAnim.Inventory)) { _invBaseAnimated = true; _invPanel.PlayUnroll(Mathf.Min(InvPanel.UnrollSeconds, limit)); }
                else { _invBaseAnimated = false; _invPanel.OpenNow(); }
            }
            else if (!pack) MenuAnim.Play(MenuAnim.Inventory, _board.transform); // another page of the group, or the old bag: out of the wrist as every board
        }

        /// <summary>Where a hand holds the bag pulled out of the backpack, in the board's own px: the old bag by its outer side's middle
        /// (the right hand its right edge, the left its left); the new inventory by the middle of its frame's lower edge.</summary>
        public Vector3 BagHoldLocal(int hand)
        {
            var r = _board.Root.rect;
            if (_invOn) return _invPanel.HoldPoint;
            return new Vector3(hand == 1 ? r.xMax : r.xMin, r.center.y, 0f);
        }

        // ------------------------------------------------------------------ the equipment panel
        /// <summary>The tab (or the pin, or the page's Back): the panel pulled up over the strip's middle, or folded. Its pull is played
        /// unless the inventory's switch is off, the panel was folded less than 5 s ago (UI/MenuAnim.cs ReopenSeconds), or a test run.</summary>
        public void InvEquipShow(bool on, bool atOnce = false)
        {
            if (_invPanel == null || !_invOn || on == _invPanel.EquipOpen) return;
            if (on && _invPanel.State != InvPanel.Mode.Open) return;
            bool play = !atOnce && MenuAnim.IsOn(MenuAnim.Inventory) && InvPanel.Now - _invEquipFoldedAt >= MenuAnim.ReopenSeconds;
            _invPanel.ShowEquip(on, play ? InvPanel.EquipSeconds : 0f);
            if (on) { _invEquip.Reset(); _invEquip.Show(true); _invEquip.Refresh(); }
            else { _invEquipFoldedAt = InvPanel.Now; InvEquipForget(); }
            _dirty = true;
        }
        private void InvEquipForget()
        {
            _invSlotArmed = 0;
            if (_invSlotShown != 0) { _invSlotShown = 0; _invFit.Clear(); _invPanel.SetHighlight(null); }
        }

        /// <summary>The slot of the equipment panel under the laser or under the look now (its position bit; 0: none). The rig's own
        /// choice is read (the equipment view's redraw resets UiButton.Hovered: UI/EquipmentWindow.cs Refresh).</summary>
        private uint InvSlotUnderPointer()
        {
            if (_rig == null || !_invPanel.EquipOpen) return 0;
            var lb = _rig.LaserButton; var gb = _rig.GazedButton;
            if (lb == null && gb == null) return 0;
            foreach (var sl in EquipmentWindow.Slots)
            {
                var b = InvSlotButton(sl.Bit);
                if (b != null && b.isActiveAndEnabled && (b == lb || b == gb)) return sl.Bit;
            }
            return 0;
        }

        /// <summary>The highlight follows the slot under the pointer, else the armed slot; none: cleared. The band's line says what
        /// that slot holds (the equipment screen's own strip text).</summary>
        private void InvSlotTick()
        {
            if (!_invPanel.EquipOpen) { if (_invSlotShown != 0 || _invSlotArmed != 0) InvEquipForget(); return; }
            bool band = !_invPanel.EquipIsFull;
            if (band && _invSlotArmed != 0 && _invPanel.BandButton(_invSlotArmed) == null) { _invSlotArmed = 0; _dirty = true; } // (the band shows the other group now: the slot lets go)
            uint under = InvSlotUnderPointer(), want = under != 0 ? under : _invSlotArmed;
            if (want == _invSlotShown && !_invFitStale) return;
            _invSlotShown = want; _invFitStale = false; _invFit.Clear();
            if (band) _invPanel.SetBandLine(want != 0 ? _invEquip.StripFor(want) : Lang.T("~inv~Point at a slot: what fits lights up."));
            if (want == 0) { _invPanel.SetHighlight(null); return; }
            foreach (var e in _invEquip.Candidates(want)) _invFit.Add(e.Index);
            _invPanel.SetHighlight(_invFit);
        }
        private bool _invFitStale;

        // ------------------------------------------------------------------ every frame while it is shown
        private void InvTick()
        {
            if (!_invOn || _invPanel == null || !_board.Shown) return;
            var p = _invPanel;
            // the bundle out of the backpack: springs open in front of the player, or when it is let go
            if (p.State == InvPanel.Mode.Compact && _head != null)
            {
                // in front of the BODY (the heading the backpack's zone is measured in: LokiGame.W13Grab.cs), not of where the head looks:
                // a player who turns the head to look at the bundle beside the shoulder does not open it there
                var d = _board.transform.position - _head.transform.position;
                float yaw = BodyYaw != null ? BodyYaw() : _head.transform.eulerAngles.y;
                float ahead = Vector3.Dot(new Vector3(d.x, 0f, d.z), Quaternion.Euler(0f, yaw, 0f) * Vector3.forward);
                bool spring = !_board.Grab.Held || ahead >= PackSpringAhead;
                if (spring)
                {
                    if (!_board.Grab.Held) InvStand("the bundle let go"); // (client wave 23, w23inv: it springs open upright; still in the hand, the hand carries it upright: LokiGame.W13Grab.cs)
                    UpdateEye(); p.Rebend(_eye);
                    p.PlaySpring(_invPackAnimate ? Mathf.Min(InvPanel.SpringSeconds, MenuAnim.MaxSeconds(MenuAnim.Inventory) - MenuAnim.EndMargin) : 0f);
                    Debug.Log($"[Inv] the bundle {(_board.Grab.Held ? $"{ahead * 100f:0} cm in front of the body" : "let go")}: it springs open");
                    _dirty = true;
                }
            }
            p.Tick();
            if (p.Dirty) _dirty = true;
            if (p.State == InvPanel.Mode.Compact) return;
            // the base opening has ended (or there was none): the pinned equipment panel pulls open by itself
            if (!_invBaseDone && p.State == InvPanel.Mode.Open)
            {
                _invBaseDone = true;
                if (_invBaseAnimated) MenuAnim.Played(MenuAnim.Inventory, p.LastSeconds);
                if (InvPanel.Pinned && !p.EquipOpen) { Debug.Log($"[Inv] the base opening has ended: the pinned equipment panel {(_invBaseAnimated ? "pulls open" : "is open at once (the opening had no animation)")}"); InvEquipShow(true, !_invBaseAnimated); }
            }
            InvSlotTick();
            float now = InvPanel.Now, dwell = InvPanel.CardDwell; bool linger = true;
            if (_gridLock >= 0) { if (_gridSel != _gridLock) { _gridSel = _gridLock; _dirty = true; } _invHover = -1; _invAwayAt = -1f; return; }
            int cell = p.HoveredCell(), idx = cell >= 0 ? p.CellIndex(cell) : -1;
            if (idx >= 0)
            {
                _invAwayAt = -1f;
                if (idx != _invHover) { _invHover = idx; _invHoverAt = now; if (_gridSel >= 0 && _gridSel != idx) { _gridSel = -1; _dirty = true; } } // another cell: the cards go at once
                if (_gridSel != idx && now - _invHoverAt >= dwell) { _gridSel = idx; _dirty = true; }
                return;
            }
            _invHover = -1;
            if (_gridSel < 0 || !linger) return;
            // on no cell: the cards stay while the pointer is on them, and CardLinger after it left
            if (InvPointerOnCard()) { _invAwayAt = -1f; return; }
            if (_invAwayAt < 0f) _invAwayAt = now;
            else if (now - _invAwayAt >= CardLinger) { _gridSel = -1; _invAwayAt = -1f; _dirty = true; }
        }

        /// <summary>The laser (the pointing hand's ray) or the look (the head's own direction) is on a shown card, or on one of its buttons.</summary>
        private bool InvPointerOnCard()
        {
            var p = _invPanel; if (!p.CardsShown) return false;
            foreach (var b in new[] { p.CardAct, p.CardDetails, p.CardPin, p.CardUnlock }) if (b != null && b.Hovered) return true;
            if (_rig != null && _rig.LaserPanel == _board.transform) { var ray = _rig.PointerRayNow(); if (p.BehindCard(ray.origin, ray.origin + ray.direction * 6f)) return true; }
            if (_head != null) { var t = _head.transform; if (p.BehindCard(t.position, t.position + t.forward * 6f)) return true; }
            return false;
        }

        /// <summary>The trigger on a cell. With a slot of the equipment panel shown (under the look, or armed) and this item one of those
        /// that fit it: the item is equipped into that slot (the equipment screen's own call). Else the bag's rule
        /// (UI/WristMenu.Board.cs GridPress): locks its item; on the locked item: uses it.</summary>
        private void InvPress(int idx)
        {
            if (idx < 0 || !_inv.Items.TryGetValue(idx, out var e)) return;
            if (_invPanel.EquipOpen && _invSlotShown != 0 && _invFit.Contains(idx))
            {
                uint bit = _invSlotShown;
                Debug.Log($"[Inv] {e.ItemId} {e.Info.English} (index {idx}) into {EquipmentWindow.SlotNames(bit)}: a pull on a highlighted item");
                InvEquips++;
                if (_invEquip.EquipInto(e, bit)) { _invEquip.Select(bit); _invSlotArmed = 0; _invFitStale = true; _dirty = true; }
                return;
            }
            if (_invSlotArmed != 0) { _invSlotArmed = 0; _dirty = true; } // (a pull on another cell: the slot lets go; the pull does what it does)
            if (idx == _gridLock) { BagUse("the locked item's own cell"); return; }
            _gridLock = idx; _gridSel = idx; GridLocks++; _dirty = true;
            Debug.Log($"[Bag] locked {e.ItemId} {e.Info.English} (index {idx})");
        }
        /// <summary>Items equipped by a pull on a highlighted cell (smoke, log).</summary>
        public int InvEquips { get; private set; }

        private void InvCardButton(string which)
        {
            int i = AsideIndex;
            switch (which)
            {
                case "act": if (i >= 0) ItemActionOn(i); _dirty = true; break;
                case "details": if (_gridSel >= 0) OpenDetailsFromBag(_gridSel); break;
                case "pin": if (_gridSel >= 0 && _inv.Items.TryGetValue(_gridSel, out var e)) OpenSlotPicker(false, e.ItemId, 0); break;
                case "unlock": GridUnlock("the Unlock button"); break;
            }
        }

        // ------------------------------------------------------------------ drawn again (WristMenu.cs RefreshPageOf, the Items page)
        private void RefreshInv()
        {
            _itemList.gameObject.SetActive(false); _itemDetail.gameObject.SetActive(false); _w3equip?.Show(false);
            if (_grid != null && _grid.gameObject.activeSelf) _grid.gameObject.SetActive(false);
            var p = _invPanel;
            p.SetAll(_inv);
            p.Refresh(_inv, _gridSel, _gridLock, _ps.Weight / 10f, _ps.MaxWeight / 10f, _ps.BaseLevel);
            // the lock ends when its item is gone, or when no shown cell holds it (a filter, the sort or a page hid it): the old bag's rule
            if (_gridLock >= 0)
            {
                if (!_inv.Items.ContainsKey(_gridLock)) GridUnlock("its item is gone");
                else if (p.CellOf(_gridLock) < 0) GridUnlock("no shown cell holds it");
            }
            if (_gridSel >= 0 && !_inv.Items.ContainsKey(_gridSel)) _gridSel = -1;
            if (p.EquipOpen) { if (p.EquipIsFull) _invEquip.Refresh(); else p.RefreshBand(_inv, _invSlotArmed); _invFitStale = true; InvSlotTick(); } // (the worn pieces and what fits follow every equip and take-off)
            InvCards();
        }

        /// <summary>The cards for the item pointed at (or locked): the old item panel's and comparison panel's texts and buttons
        /// (UI/WristMenu.Board.cs RefreshAside), on parchment beside the item's section.</summary>
        private void InvCards()
        {
            var p = _invPanel;
            Inventory.Entry e = null; if (_gridSel >= 0) _inv.Items.TryGetValue(_gridSel, out e);
            if (e == null || p.State != InvPanel.Mode.Open) { p.HideCards(); p.Cover(); return; } // (no card while it opens or is the bundle)
            string verb = e.Wearing != 0 ? Lang.T("Take off") : e.IsEquip ? Lang.T("Equip") : e.IsCard ? Lang.T("Insert") : e.Info.IsConsumable ? Lang.T("Use") : null;
            bool tooLow = e.Info.MinLevel > _ps.BaseLevel && e.Wearing == 0;
            bool locked = _gridLock >= 0 && _gridLock == e.Index;
            string head = locked ? (verb != null ? Lang.F("Locked · trigger again: {0}", verb) : Lang.T("Locked"))
                        : e.Wearing != 0 ? Lang.F("You wear at {0}:", EquipmentWindow.SlotNames(e.Wearing)) : Lang.T("In your bag:");
            var worn = W4CompareTargets(e);
            string note = e.IsEquip && e.Wearing == 0 && worn.Count == 0 ? Lang.F("Nothing worn at {0}", EquipmentWindow.SlotNames(e.Location)) : null;
            p.CardAct.gameObject.SetActive(verb != null);
            if (verb != null) { p.CardAct.SetLabel(tooLow ? "Lv " + e.Info.MinLevel : verb); p.CardAct.Interactable = true; if (!p.CardAct.Hovered) p.CardAct.SetHover(false); }
            p.CardPin.gameObject.SetActive(Hotkeys != null && !e.IsEquip && e.Info.IsConsumable);
            p.CardUnlock.gameObject.SetActive(locked);
            p.CardSolid = locked; // (a locked item's card is solid; an unlocked one lets the pointer through to the cells behind it)
            p.ShowCards(e, head, note, worn, w => Lang.F("You wear at {0}:", EquipmentWindow.SlotNames(w.Wearing)));
            p.Cover();
        }

    }
}
