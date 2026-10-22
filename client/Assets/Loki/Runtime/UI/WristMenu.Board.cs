using System.Collections.Generic;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 11 (w11menu), part 2: the menu's two styles (specified on 2026-10-06: "make the menu redesign and keep the
    /// current menu as a toggle option"). Settings → Comfort "Menu" (VR/Comfort.cs MenuStyle): New, the default, or Classic.
    ///   Classic  the wrist menu as it was (25 % larger: WristMenu.W11.cs): twenty tabs on a panel over the walking hand.
    ///   New      X / A (the walking hand's menu button, or the strip's Menu) opens a ring of eight large icons over that hand
    ///            (UI/MenuRing.cs): the twenty tabs in eight groups (<see cref="Groups"/>). Choosing one opens the group as a board
    ///            in front of the player (UI/MenuBoard.cs): its pages as large tabs down the side, the page beside them.
    ///            Wave 28 (wcodex), design note (2026-10-15): "The Codex is supposed to be its own menu on the wrist menu, along with Character,
    ///            Settings, Items, etc.": a NINTH group, Codex (between World and Settings: the arch keeps Companions as its left end,
    ///            Settings in its middle, Character just right of it), opens the book itself (no board: <see cref="OpenCodexHere"/>).
    /// The pages are the SAME objects in both styles: every page of the wrist menu (and the quick-slot picker) is moved onto the
    /// board (HostPages) and back, so each page's build / refresh code, its state and its test hooks are one and the same; only
    /// the Items page shows something new on the board: a grid of item icons in place of the list (below), with the item under
    /// the laser or the look as a card beside the board and, for equipment, what is worn there under it at once.
    /// While a board is open this object is active (it refreshes the pages) but draws nothing itself (its canvas and collider are
    /// off, the tabs and strips are hidden); <see cref="Visible"/> is true, <see cref="ShownOnWrist"/> false.
    /// The existing entry points keep working in both styles: Show(tab) opens the board of that tab's group, Hide() closes it,
    /// Toggle() is the menu button (the ring in New).
    /// Hooks in WristMenu.cs: Toggle, Show, Hide, LateUpdate, RefreshPage (the grid), Toast; WristMenu.W11.cs W11AfterRefresh;
    /// LokiHud.Build (InitBoard), LokiHud.MainSecondary (B closes).
    /// </summary>
    public sealed partial class WristMenu
    {
        public sealed class MenuEntry
        {
            /// <summary>The side tab's text (English: a table key).</summary>
            public string Label;
            /// <summary>The page it shows (null: an action that opens something else).</summary>
            public Tab? Page;
            /// <summary>Items page only: the equipment page instead of the bag.</summary>
            public bool Equip;
            public System.Action<WristMenu> Act;
            /// <summary>Client wave 15 (w15gear), point 3: the page's home is another group (the Character group's Equipment is the Items
            /// group's page): <see cref="GroupOf"/> finds the home unless the board already shows this group.</summary>
            public bool Alias;
        }
        public sealed class MenuGroup
        {
            public string Name; public MenuEntry[] Entries;
            /// <summary>The ring's picture: an item's icon from the player's game data, or a skill's.</summary>
            public int IconItem, IconSkill;
        }
        private static MenuEntry P(string label, Tab page, bool equip = false) => new MenuEntry { Label = label, Page = page, Equip = equip };
        private static MenuEntry A(string label, System.Action<WristMenu> act) => new MenuEntry { Label = label, Act = act };

        /// <summary>The twenty tabs in nine groups, every other entry point of the wrist menu as an action beside them.</summary>
        public static readonly MenuGroup[] Groups =
        {
            new MenuGroup { Name = Lang.N("Character"), IconItem = 2220, Entries = new[] { P(Lang.N("Status"), Tab.Status), P(Lang.N("Stats"), Tab.Stats),
                new MenuEntry { Label = Lang.N("Equipment"), Page = Tab.Items, Equip = true, Alias = true } } }, // client wave 15 (w15gear), point 3: the equipment and the character's look
            new MenuGroup { Name = Lang.N("Items"), IconItem = 501, Entries = new[] { P(Lang.N("Bag"), Tab.Items), P(Lang.N("Equipment"), Tab.Items, true), P(Lang.N("Bank"), Tab.Bank), P(Lang.N("Cash shop"), Tab.CashShop),
                A(Lang.N("Cart"), m => m.OpenCart?.Invoke()), A(Lang.N("~board~Market"), m => { MenuNav.ExpectLater(); m.OpenMarket?.Invoke(); }) } }, // (client wave 13: the buying store's window opens on the server's answer)
            new MenuGroup { Name = Lang.N("Skills"), IconSkill = 5, Entries = new[] { P(Lang.N("Skills"), Tab.Skills), P(Lang.N("Quick bar"), Tab.Quick) } },
            new MenuGroup { Name = Lang.N("Quests"), IconItem = 1550, Entries = new[] { P(Lang.N("Quests"), Tab.Quests), P(Lang.N("Achievements"), Tab.Achievements) } },
            new MenuGroup { Name = Lang.N("World"), IconItem = 602, Entries = new[] { P(Lang.N("Modes"), Tab.Modes), P(Lang.N("Emotes"), Tab.Emotes),
                A(Lang.N("Horde"), m => m.OpenHorde?.Invoke()), A(Lang.N("Guide"), m => m.OpenGuide?.Invoke()), A(Lang.N("Screenshot"), m => m.TakeScreenshot?.Invoke()),
                A(Lang.N("Desktop view"), m => { m.CycleDesktopView?.Invoke(); m.RefreshW4World(); }) } },
            new MenuGroup { Name = Lang.N("Companions"), IconItem = 9001, Entries = new[] { P(Lang.N("Companions"), Tab.Companions) } }, // client wave 19 (w19small), point 50: before Social, so it is the arch's left end
            new MenuGroup { Name = Lang.N("Social"), IconItem = 744, Entries = new[] { P(Lang.N("Friends"), Tab.Social), P(Lang.N("Party"), Tab.Party), P(Lang.N("Guild"), Tab.Guild), P(Lang.N("Family"), Tab.Family),
                P(Lang.N("Chat rooms"), Tab.Community), P(Lang.N("Booking"), Tab.Booking), P(Lang.N("Mail"), Tab.Mail) } },
            // wave 28 (wcodex): the Codex, its own group (design note, 2026-10-15); its picture the game's Hardcover Book (1561); English only
            new MenuGroup { Name = "Codex", IconItem = 1561, Entries = new[] { A("Codex", m => m.OpenCodexHere()) } },
            new MenuGroup { Name = Lang.N("Settings"), IconItem = 1531, Entries = new[] { P(Lang.N("View"), Tab.View), A(Lang.N("Settings…"), m => m.OpenComfort?.Invoke()),
                A(Lang.N("Commands"), m => m.OpenCommands?.Invoke()), A(Lang.N("Leave the game…"), m => m.OpenLeave?.Invoke()) } },
        };

        /// <summary>The group a tab's page is in (and its entry there; the Items page: the bag's). Client wave 15 (w15gear), point 3: a page
        /// that two groups show (the equipment: Items and Character) stays on <paramref name="prefer"/>'s board when that group has it;
        /// otherwise its home group (the entries that are not an <see cref="MenuEntry.Alias"/>).</summary>
        public static (int group, int entry) GroupOf(Tab t, bool equip = false, int prefer = -1)
        {
            if (prefer >= 0 && prefer < Groups.Length)
                for (int e = 0; e < Groups[prefer].Entries.Length; e++)
                    if (Groups[prefer].Entries[e].Page == t && Groups[prefer].Entries[e].Equip == equip) return (prefer, e);
            for (int g = 0; g < Groups.Length; g++)
                for (int e = 0; e < Groups[g].Entries.Length; e++)
                    if (Groups[g].Entries[e].Page == t && Groups[g].Entries[e].Equip == equip && !Groups[g].Entries[e].Alias) return (g, e);
            return (0, 0);
        }

        /// <summary>The Settings row was pressed: the player's choice counts from now on, not a run's forced style.</summary>
        public static void ClearSessionStyle()
        {
        }
        /// <summary>The menu's style now: true = New (the ring and the boards).</summary>
        public bool NewStyle
        {
            get
            {
                return VR.Comfort.MenuStyle == 0;
            }
        }
        /// <summary>The classic wrist panel is shown now (the buff bar and the PvP counter sit over it, the forearm quick bar hides).</summary>
        public bool ShownOnWrist => gameObject.activeSelf && !_onBoard;
        public MenuBoard Board => _board;
        public MenuRing Ring => _ring;
        /// <summary>The pages are on the board now (New), and the group shown there.</summary>
        public bool OnBoard => _onBoard;
        public int BoardGroup => _group;
        /// <summary>The page object of a tab (smoke: where it is hosted, whether it is shown).</summary>
        public RectTransform PageOf(Tab t) => _pages.TryGetValue(t, out var p) ? p : null;

        private MenuBoard _board; private MenuRing _ring; private VR.DioramaRig _rig;
        private bool _onBoard; private int _group; private readonly int[] _groupLast = new int[Groups.Length]; // (wave 28, wcodex: one per group; a fixed 8 threw for the ninth)
        private readonly List<GameObject> _chrome = new List<GameObject>();
        private bool _ringIcons;

        /// <summary>Builds the ring and the board (LokiHud, once, after Create) and puts the pages where the style wants them.</summary>
        public void InitBoard(VR.DioramaRig rig)
        {
            _rig = rig;
            _board = MenuBoard.Create(_rt.parent);
            _board.CloseClicked = () => { if (!MenuNav.CloseButton(_board.transform)) Hide(); }; // client wave 13 (w13items), item 11: "back" mode: the X goes back
            _board.Grab.Released = KeepBoardPlace;
            var names = new List<(string, Sprite)>();
            foreach (var g in Groups) names.Add((Lang.T(g.Name), null));
            _ring = MenuRing.Create(_rt.parent, rig, _hand, _head, names, g => { _navFromRing = true; try { OpenGroup(g); } finally { _navFromRing = false; } }); // (client wave 13, item 11: a board opened from the ring)
            if (rig != null) rig.BlockWorldClicks = () => _ring != null && _ring.Shown;
            if (rig != null) rig.PanelPull = BagPull; // client wave 13 (w13items), item 12: a pull on the bag, not on an item, uses the locked item
            // what the wrist panel shows around the pages: the tabs, the strips under it and their buttons
            var keep = new HashSet<Transform>(); foreach (var kv in _pages) keep.Add(kv.Value);
            keep.Add(_pick); keep.Add(_toast.transform); foreach (var c in _w11cards) if (c != null) keep.Add(c.Root);
            for (int i = 0; i < _rt.childCount; i++) { var ch = _rt.GetChild(i); if (!keep.Contains(ch) && ch.name != "border" && ch.name != "bg") _chrome.Add(ch.gameObject); }
            BuildGrid(_pages[Tab.Items]);
            BuildAside();
            BuildInv(); // client wave 21 (w21inv), point 62: the new inventory (WristMenu.Inv.cs)
            InitNav(); // client wave 13 (w13items), item 11
            // client wave 21 (w21anim), the design's point 63: every menu opened from the wrist flies out of it (UI/MenuAnim.cs): the boards by
            // their group, the classic wrist menu; the windows are watched there by their names
            MenuAnim.Wrist = () => this != null ? _hand : null;
            MenuAnim.Track(_board.transform, () => this != null && _board != null && _board.Shown && _onBoard && gameObject.activeSelf, () => MenuAnim.KeyOfGroup(Groups[Mathf.Clamp(_group, 0, Groups.Length - 1)].Name));
            MenuAnim.Track(_rt, () => this != null && ShownOnWrist, () => MenuAnim.Classic);
            VR.Comfort.Changed += OnComfortChanged;
            ApplyStyle();
        }

        private void OnComfortChanged() { if (this == null) return; ApplyStyle(); _dirty = true; } // (client wave 21, w21inv: "Inventory: New / Old" is drawn at once)
        private void OnDestroy() { VR.Comfort.Changed -= OnComfortChanged; }
        private void OnDisable() { if (_board != null) _board.Close(); _gridLock = -1; } // (client wave 13, item 12: the lock ends with the board)

        /// <summary>Moves the pages to where the style in force wants them; an open menu stays open on the same page.</summary>
        public void ApplyStyle()
        {
            if (_board == null) return;
            bool want = NewStyle;
            if (want == _onBoard) return;
            bool open = gameObject.activeSelf;
            HostPages(want);
            _ring.Hide(); _board.Close();
            Debug.Log($"[Menu] style: {(want ? "new (ring and boards)" : "classic (wrist menu)")}");
            if (open) { if (want) { var (g, _) = GroupOf(_tab, _tab == Tab.Items && _w3equipOpen); _group = g; } Show(_tab); }
        }

        private void HostPages(bool board)
        {
            if (_wide) SetCharWide(false); // client wave 20 (w20char), point 52: the pages leave the wings first
            var host = board ? _board.Content : _rt;
            var at = board ? Vector2.zero : new Vector2(0, PageY);
            foreach (var kv in _pages) { kv.Value.SetParent(host, false); kv.Value.anchoredPosition = at; }
            _pick.SetParent(host, false); _pick.anchoredPosition = at;
            foreach (var c in _chrome) if (c != null) c.SetActive(!board);
            // client wave 14 (w14ui), point 26 (design note: "on the settings menu, the first time you click it, there are two different buttons both that
            // lead to the settings"; dropping the View page's own one: "Yes"): on the board the Settings group's "Settings…" tab is the one way
            // (the View page's button is not shown there; "Leave the game…" moves to the middle of its row); Classic keeps both, its only way there
            if (_viewSettings != null) _viewSettings.gameObject.SetActive(!board);
            if (_codexClassic != null) _codexClassic.gameObject.SetActive(!board); // wave 28 (wcodex): with the new menu the ring's Codex group is the way in
            if (_viewLeave != null) { var lr = (RectTransform)_viewLeave.transform; lr.anchoredPosition = new Vector2(board ? 0f : (W - 90) / 4 + 5, lr.anchoredPosition.y); }
            var canvas = _rt.GetComponent<Canvas>(); if (canvas != null) canvas.enabled = !board;
            var col = _rt.GetComponent<BoxCollider>(); if (col != null) col.enabled = !board;
            foreach (var c in _w11cards) c?.Clear();
            if (_grid != null) _grid.gameObject.SetActive(false);
            _onBoard = board; _dirty = true;
        }

        // ------------------------------------------------------------------ the ring
        private void ToggleRing()
        {
            if (_ring == null) return;
            // client wave 13 (w13resize), the brief's fourteenth item: the menu button with the ring open closes it and opens nothing, in both
            // modes (a trigger opens the highlighted group: UI/MenuRing.cs); wave 11 / 12 opened twist mode's top icon here
            if (_ring.Shown) { _ring.Hide(); return; }
            if (gameObject.activeSelf) gameObject.SetActive(false); // the launcher closes the board that is open: the next one is placed afresh
            ShowRing();
            MenuNav.StartFresh(_navRing, null, "the menu button"); // client wave 13 (w13items), item 11: a window that was the player's menu closes, the history is cleared
        }

        /// <summary>Shows the ring with its pictures (twist: the group opened last at the top).</summary>
        private void ShowRing()
        {
            if (!_ringIcons)
            {
                _ringIcons = true;
                for (int i = 0; i < Groups.Length; i++)
                {
                    var g = Groups[i];
                    Sprite s = g.IconSkill > 0 ? SkillIcon(SkillDb.Get(g.IconSkill)) : g.IconItem > 0 ? ItemIcon(GameDb.GetItem(g.IconItem)) : null;
                    if (s != null && s.texture != null) s.texture.filterMode = FilterMode.Point; // 24-pixel art drawn large: crisp, as the game draws it
                    _ring.SetIcon(i, s);
                }
            }
            // client wave 14 (w14menu), point 25 (design note: "Sometimes when I open the wrist rotating menu, it is not on the settings. button to begin
            // with, it is on whatever location it was in when I closed it"): twist mode always opens on Settings (wave 11-13: the group opened
            // last); the arch or the circle, the icons still or sliding (Settings → Controls → Menus: VR/Comfort.cs RingArch / RingStill)
            // client wave 15 (w15menu), point 13 (design note: "On the wrist, arch and circle menus, the initial option that is highlighted should be the
            // the character tab"): it opens on Character; the arch keeps its order, cut opposite Settings (UI/MenuRing.cs Show's centre)
            int opensOn = CharacterGroup;
            _ring.Show(VR.Comfort.RingTwist, opensOn, VR.Comfort.RingArch, VR.Comfort.RingStill, CharacterGroup);
        }

        /// <summary>Wave 28 (wcodex): the ring's Codex group (the book, no board).</summary>
        public static int CodexGroup => System.Array.FindIndex(Groups, g => g.Name == "Codex");
        private CodexGlow _codexGlowRing, _codexGlowClassic; private bool _codexAura;
        /// <summary>Wave 28 (wcodex): an NPC named a creature since the book was last opened on it: the green aura round the Codex button (the
        /// ring's, and the classic wrist menu's on its Achievements page) until it is (UI/CodexFlair.cs CodexGlow).</summary>
        public void SetCodexAura(bool on)
        {
            _codexAura = on;
            if (on && _codexGlowRing == null && _ring != null && CodexGroup >= 0 && _ring.Button(CodexGroup) != null) _codexGlowRing = CodexGlow.Add((RectTransform)_ring.Button(CodexGroup).transform, 44f);
            if (on && _codexGlowClassic == null && _codexClassic != null) _codexGlowClassic = CodexGlow.Add((RectTransform)_codexClassic.transform, 28f);
            if (_codexGlowRing != null && _codexGlowRing.gameObject.activeSelf != on) _codexGlowRing.gameObject.SetActive(on);
            if (_codexGlowClassic != null && _codexGlowClassic.gameObject.activeSelf != on) _codexGlowClassic.gameObject.SetActive(on);
        }
        public bool CodexAura => _codexAura;
        /// <summary>The ring's Codex button shows its aura now (the ring open, the glow drawn) and how strong it is (smoke).</summary>
        public bool CodexAuraShown => _codexGlowRing != null && _codexGlowRing.gameObject.activeInHierarchy;
        public float CodexAuraAlpha => _codexGlowRing != null ? _codexGlowRing.Alpha : 0f;
        /// <summary>Wave 28 (wcodex): the book's own MenuNav entry (LokiGame.WCodex.cs), for the step back to the ring.</summary>
        public MenuNav.Menu CodexNav;
        /// <summary>Wave 28 (wcodex): the Codex group chosen: the book opens itself (hand-held or large, Settings → "Codex book"); picked on
        /// the ring it has a step back to the ring, as the skill tree has (WristMenu.Tree.cs TreeOpens).</summary>
        private void OpenCodexHere()
        {
            bool fromRing = _navFromRing;
            if (_ring != null) _ring.Hide();
            if (_onBoard && Board != null && Board.Shown) Hide(); // (one menu at a time: a board opened before closes)
            OpenCodex?.Invoke();
            if (CodexNav != null && CodexNav.IsOpen != null && CodexNav.IsOpen()) MenuNav.BoardShown(CodexNav, _navRing, fromRing);
        }

        /// <summary>Client wave 14 (w14menu), point 25: Settings (the group twist mode opened on in wave 14; the arch's centre since wave 15).</summary>
        public static int SettingsGroup => Groups.Length - 1;
        /// <summary>Client wave 15 (w15menu), point 13: the group the ring opens on (Character).</summary>
        public static int CharacterGroup => 0;

        /// <summary>Client wave 13 (w13look), item 24: a group's kind for the look (UI/LookSelect.cs), the same on its board and on the classic
        /// wrist menu's pages of that group: "Menu:" + the group's English name (Menu:Items is the bag, Menu:Quests the quests ...).</summary>
        public static string LookKeyOf(int group) => "Menu:" + Groups[Mathf.Clamp(group, 0, Groups.Length - 1)].Name;

        /// <summary>Looking chooses this button now (false: an icon of the ring while it is in twist mode: there the roll of the hand
        /// chooses, and the design specifies that "the sight choosing is disabled for the wheel menu specifically"; everywhere else: true).</summary>
        public bool GazeAllowed(UiButton b) => !(_ring != null && _ring.Twist && b != null && b.Panel == _ring.transform);

        /// <summary>Opens a group's board on the page it showed last.</summary>
        public void OpenGroup(int g)
        {
            if (g < 0 || g >= Groups.Length) return;
            int e = Mathf.Clamp(_groupLast[g], 0, Groups[g].Entries.Length - 1);
            if (!Groups[g].Entries[e].Page.HasValue) e = 0;
            if (g == SkillsGroup && TreeWanted) e = 0; // client wave 22 (w22tree), point 64: the ring's Skills is the tree, whatever page its board showed last
            Debug.Log($"[Menu] group {Groups[g].Name}");
            Activate(g, e);
        }

        /// <summary>A side tab: a page of the group, or an action (the board stays as it is).</summary>
        public void Activate(int g, int e)
        {
            if (g < 0 || g >= Groups.Length || e < 0 || e >= Groups[g].Entries.Length) return;
            var en = Groups[g].Entries[e];
            if (!en.Page.HasValue) { try { en.Act?.Invoke(this); } catch (System.Exception ex) { Debug.LogException(ex); } if (_onBoard && gameObject.activeSelf) BoardSync(); return; }
            _group = g; _groupLast[g] = e;
            // client wave 20 (w20char), point 52: the Character board opened afresh shows the equipment in its third column (not an item's details
            // or the picker left open on another board)
            if (g == CharacterGroup && _onBoard) { _detailIndex = -1; _w3equip?.Reset(); }
            if (en.Page.Value == Tab.Items)
            {
                if (en.Equip) { OpenEquipment(); return; }
                _w3equipOpen = false; _detailIndex = -1;
            }
            Show(en.Page.Value);
        }

        /// <summary>Client wave 13 (w13grab), item 20, the backpack grab: the bag. New style: the Items board on its Bag page (a board already on
        /// the Items page stays as it is: the bag, an item's Details ...); shown outside a press, it is a fresh start for UI/MenuNav.cs (the
        /// player's menu that was open closes: item 11). Classic: the wrist menu on its Items page. True: the board shows it (it can be
        /// carried); false: Classic (the wrist menu rides on the hand).</summary>
        public bool OpenBag()
        {
            if (NewStyle && _board != null)
            {
                bool onItems = _onBoard && gameObject.activeSelf && _board.Shown && _tab == Tab.Items && !_w3equipOpen;
                if (!onItems) { if (!(_board.Shown && gameObject.activeSelf)) _invFromPack = true; var (g, e) = GroupOf(Tab.Items); Activate(g, e); _invFromPack = false; } // (client wave 21, w21inv: out of the backpack: the bundle, never the wrist's animation) // (client wave 21, point 63: out of the backpack, not out of the wrist)
                return _onBoard && _board.Shown;
            }
            _w3equipOpen = false; _detailIndex = -1; Show(Tab.Items);
            return false;
        }

        /// <summary>B: the ring closes, else the board (true: something was closed).</summary>
        public bool CloseNewMenu()
        {
            if (_ring != null && _ring.Shown) { _ring.Hide(); return true; }
            if (Tree != null && Tree.Shown) { Tree.Close(); return true; } // client wave 22 (w22tree): the 3D skill tree
            if (_onBoard && gameObject.activeSelf) { Hide(); return true; }
            return false;
        }

        // ------------------------------------------------------------------ the board
        private string EntryLabel(MenuEntry en) => en.Act != null && en.Label == "~board~Market" && MarketLabel != null ? MarketLabel() : Lang.T(en.Label);

        /// <summary>The board for the page shown: its group's name and tabs, placed in front of the player if it was closed.</summary>
        private void BoardSync()
        {
            if (_board == null || !_onBoard) return;
            bool was = _board.Shown, equip = _tab == Tab.Items && _w3equipOpen;
            var (g, _) = GroupOf(_tab, equip, _group); // (client wave 15: the equipment stays on the Character board when opened there)
            _group = g; // (a hook may have shown a page of another group)
            InvSync(true); // client wave 21 (w21inv): the inventory gives the board back before the board changes its shape
            var grp = Groups[_group];
            // client wave 20 (w20char), point 52: the Character group's board is the wide one, its three pages shown at once, no side tabs
            bool wide = _group == CharacterGroup;
            if (wide != _wide) SetCharWide(wide);
            if (wide) foreach (var t in WideTabs) if (!_pages[t].gameObject.activeSelf) _pages[t].gameObject.SetActive(true);
            InvSync(); // client wave 21 (w21inv), point 62: the bag page is the new inventory (the board's own picture hidden: no title, no tabs)
            var list = new List<(string, bool)>(); int active = -1;
            for (int i = 0; i < grp.Entries.Length && !wide; i++)
            {
                var en = grp.Entries[i];
                list.Add((EntryLabel(en), !en.Page.HasValue));
                if (en.Page == _tab && en.Equip == equip) active = i;
            }
            if (active >= 0) _groupLast[_group] = active;
            int group = _group;
            if (_invOn) list.Clear();
            _board.SetGroup(_invOn ? "" : Lang.T(grp.Name), list, active, i => Activate(group, i));
            _board.LookKey = LookKeyOf(_group); // client wave 13 (w13look), item 24: the board's kind for the look is its group
            _board.PlaceKey = "Board:" + grp.Name; // item 6: per-menu position memory relative to player vision
            _board.Open(_head, VR.Comfort.BoardDistance, VR.Comfort.BoardHeight, VR.Comfort.BoardScale);
            if (_board.Placed != _placedSeen) { _placedSeen = _board.Placed; UpdateEye(); InvPlaced(); } // client wave 13 (w13items), item 8: the panels face the eyes of this placing
            // client wave 13 (w13items), item 11: the board shown now (from the ring's pick: a step back to the ring; else a fresh start unless a
            // press of another menu opened it: UI/MenuNav.cs decides); another group's page shown on it by one of its own buttons: the board
            // it showed is a step of the history (in place)
            if (!was) InvOpened(); // client wave 21 (w21inv): the inventory unrolls, or comes out of the backpack as a bundle (WristMenu.Inv.cs)
            if (!was) MenuNav.BoardShown(_navBoard, _navRing, _navFromRing);
            else if (_navGroupShown >= 0 && group != _navGroupShown && MenuNav.PreClickState(_navBoard) is BoardState pre) MenuNav.PushInPlace(_navBoard, pre, "the board of " + grp.Name);
            _navGroupShown = group;
        }

        /// <summary>The grip let the board go: where it is now against the eyes, and its size, are the player's (kept in loki-user.json).</summary>
        private void KeepBoardPlace()
        {
            if (_board == null || _head == null) return;
            var d = _board.transform.position - _head.transform.position;
            VR.Comfort.BoardDistance = Mathf.Clamp(new Vector2(d.x, d.z).magnitude, 0.3f, 1.6f);
            VR.Comfort.BoardHeight = Mathf.Clamp(d.y, -0.9f, 0.6f);
            VR.Comfort.BoardScale = Mathf.Clamp(_board.transform.localScale.x / MenuBoard.MetresPerPx, 0.5f, 2f);
            PanelPlacing.SaveMenuPlace(_board.PlaceKey, _board.transform, _head, VR.Comfort.BoardScale);
            Debug.Log($"[Menu] board left {VR.Comfort.BoardDistance:0.00} m ahead, {VR.Comfort.BoardHeight:+0.00;-0.00} m against the eyes, size x{VR.Comfort.BoardScale:0.00}");
            VR.Comfort.Save();
            UpdateEye(); _dirty = true; // client wave 13 (w13items), item 8: the panels turn to the eyes again
            InvPlaced(); // client wave 21 (w21inv): the inventory is bent round the eyes of the new place
        }

        // ------------------------------------------------------------------ client wave 20 (w20char), point 52: the wide Character board
        // The Character group's board shows Status, Stats and the equipment (the Items page's equipment view) at once, side by side on the
        // board's three facets (UI/MenuBoard.cs SetWide): Status on the left wing, Stats in the middle, the equipment on the right wing. Every
        // refresh draws the three (RefreshPage); _tab stays the page the ring's entry named (the side tabs are gone; older hooks keep working).
        // The Items group's Equipment stays on the Items board alone (narrow), as before; Classic is untouched.
        private static readonly Tab[] WideTabs = { Tab.Status, Tab.Stats, Tab.Items };
        private bool _wide;
        /// <summary>The wide Character board is in force (its three pages are on the board's three facets).</summary>
        public bool CharWide => _wide && _onBoard;

        private void SetCharWide(bool on)
        {
            if (_board == null) return;
            _wide = on; _board.SetWide(on);
            void Host(Tab t, RectTransform at) { var p = _pages[t]; if (p.parent != at) p.SetParent(at, false); p.anchoredPosition = Vector2.zero; }
            Host(Tab.Status, on ? _board.ContentLeft : _board.Content);
            Host(Tab.Stats, _board.Content);
            Host(Tab.Items, on ? _board.ContentRight : _board.Content);
            if (!on) foreach (var kv in _pages) kv.Value.gameObject.SetActive(kv.Key == _tab); // (Show's rule again: one page)
            _dirty = true;
        }

        // ------------------------------------------------------------------ the item grid (the board's Items page)
        public const int GridCols = 5, GridRows = 5, GridPerPage = GridCols * GridRows;
        private const float CellW = 114, CellH = 94;
        private sealed class GridCell { public UiButton Btn; public Image Icon; public Text Count, Mark; public GameObject LockFrame; public int Index = -1; }
        private RectTransform _grid; private readonly List<GridCell> _cells = new List<GridCell>();
        private readonly List<UiButton> _gridFilters = new List<UiButton>(); private UiButton _gridSort; private Text _gridPage;
        private int _gridPageIdx, _gridSel = -1;
        /// <summary>The item whose card is shown beside the grid (its inventory index; -1: none), and the grid's cells (smoke).</summary>
        public int GridSelected => _gridSel;
        public bool GridShown => (_grid != null && _grid.gameObject.activeInHierarchy) || InvShown; // (client wave 21, w21inv: whichever bag is shown; the hooks below too)
        public UiButton GridCellButton(int i) => _invOn ? _invPanel.CellButton(i) : i >= 0 && i < _cells.Count ? _cells[i].Btn : null;
        public int GridCellIndex(int i) => _invOn ? _invPanel.CellIndex(i) : i >= 0 && i < _cells.Count ? _cells[i].Index : -1;
        public string GridCellText(int i) => _invOn ? _invPanel.CellText(i) : i >= 0 && i < _cells.Count ? _cells[i].Count.text + "|" + _cells[i].Mark.text : "";
        public bool GridCellHasIcon(int i) => _invOn ? _invPanel.CellHasIcon(i) : i >= 0 && i < _cells.Count && _cells[i].Icon.enabled;
        /// <summary>Client wave 13 (w13items), item 12: cell i shows the locked mark.</summary>
        public bool GridCellLocked(int i) => _invOn ? _invPanel.CellLocked(i) : i >= 0 && i < _cells.Count && _cells[i].LockFrame != null && _cells[i].LockFrame.activeInHierarchy;
        /// <summary>The grid's box (smoke: where it is, as seen from the eyes).</summary>
        public RectTransform GridBox => _invOn ? _invPanel.Root : _grid;

        private void BuildGrid(RectTransform page)
        {
            float top = PH / 2;
            var g = _grid = UiKit.Box(page, "grid", Vector2.zero, new Vector2(W - 30, PH), new Color(0, 0, 0, 0));
            float fw = (W - 40 - 150) / ItemFilters.Length;
            for (int i = 0; i < ItemFilters.Length; i++)
            {
                int f = i;
                var fb = UiKit.Button(g, Lang.T(ItemFilters[i]), new Vector2(-W / 2 + 20 + fw * (i + 0.5f), top - 52), new Vector2(fw - 6, 92), () => { _itemFilter = f; _gridPageIdx = 0; _dirty = true; }, 24);
                fb.Text.resizeTextMinSize = 23; _gridFilters.Add(fb);
            }
            // the sort order; on the favourites' filter the sale lock instead (as the classic list page)
            _gridSort = UiKit.Button(g, "", new Vector2(W / 2 - 20 - 72, top - 52), new Vector2(144, 92), () =>
            {
                if (_itemFilter == 4) { SaleLock = !SaleLock; Toast(SaleLock ? Lang.T("Favourites won't show in NPC sell lists.") : Lang.T("Favourites can be sold to NPCs again.")); }
                else _itemSort = (_itemSort + 1) % ItemSorts.Length;
                _dirty = true;
            }, 24);
            _gridSort.Text.resizeTextMinSize = 23; // two lines of 23 px fit the button
            float x0 = -(GridCols * (CellW + 4)) / 2 + (CellW + 4) / 2, y0 = top - 108 - CellH / 2;
            for (int i = 0; i < GridPerPage; i++)
            {
                int cell = i, col = i % GridCols, row = i / GridCols;
                var c = new GridCell();
                c.Btn = UiKit.Button(g, "", new Vector2(x0 + col * (CellW + 4), y0 - row * (CellH + 4)), new Vector2(CellW, CellH), () => GridPress(cell), 18);
                var brt = (RectTransform)c.Btn.transform;
                c.Btn.Text.fontSize = 24; c.Btn.Text.resizeTextMaxSize = 24; c.Btn.Text.resizeTextMinSize = 23; // an item without a picture: its name
                c.Icon = UiKit.Icon(brt, new Vector2(0, 0), new Vector2(72, 72));
                c.Count = UiKit.Label(brt, "", new Vector2(-4, -CellH / 2 + 17), new Vector2(CellW - 12, 30), 24, TextAnchor.MiddleRight, new Color(1f, 0.85f, 0.45f));
                c.Mark = UiKit.Label(brt, "", new Vector2(4, CellH / 2 - 17), new Vector2(CellW - 12, 30), 24, TextAnchor.MiddleLeft, new Color(0.5f, 0.84f, 0.5f));
                // client wave 13 (w13items), item 12: the locked cell's mark: a bright frame round the cell (with its own colour: LockedCell)
                var lf = UiKit.Box(brt, "lockframe", Vector2.zero, new Vector2(CellW, CellH), new Color(0, 0, 0, 0));
                foreach (var (p, s) in new[] { (new Vector2(0, CellH / 2 - 3), new Vector2(CellW, 6)), (new Vector2(0, -CellH / 2 + 3), new Vector2(CellW, 6)), (new Vector2(-CellW / 2 + 3, 0), new Vector2(6, CellH)), (new Vector2(CellW / 2 - 3, 0), new Vector2(6, CellH)) })
                    UiKit.Box(lf, "edge", p, s, LockFrameColor);
                c.LockFrame = lf.gameObject; lf.gameObject.SetActive(false);
                _cells.Add(c);
            }
            UiKit.Button(g, "<", new Vector2(-W / 2 + 85, -top + 50), new Vector2(130, 92), () => { _gridPageIdx = Mathf.Max(0, _gridPageIdx - 1); _dirty = true; }, 36);
            UiKit.Button(g, ">", new Vector2(W / 2 - 85, -top + 50), new Vector2(130, 92), () => { _gridPageIdx++; _dirty = true; }, 36);
            _gridPage = UiKit.Label(g, "", new Vector2(0, -top + 50), new Vector2(W - 330, 64), 24, TextAnchor.MiddleCenter, UiKit.Dim);
            g.gameObject.SetActive(false);
        }

        /// <summary>The board's bag view is wanted now (New, the Items page, neither the details nor the equipment page open).</summary>
        private bool GridWanted => _onBoard && _tab == Tab.Items && _detailIndex < 0 && !_w3equipOpen;

        private void RefreshGrid()
        {
            _itemList.gameObject.SetActive(false); _itemDetail.gameObject.SetActive(false); _w3equip?.Show(false);
            if (!_grid.gameObject.activeSelf) _grid.gameObject.SetActive(true);
            for (int f = 0; f < _gridFilters.Count; f++) { _gridFilters[f].Normal = f == _itemFilter ? new Color(0.45f, 0.35f, 0.15f) : UiKit.ButtonBg; if (!_gridFilters[f].Hovered) _gridFilters[f].SetHover(false); }
            _gridSort.SetLabel(_itemFilter == 4 ? (SaleLock ? Lang.T("Sale lock: on") : Lang.T("Sale lock: off")) : Lang.T(ItemSorts[_itemSort]));
            var list = SortedItems();
            int pages = Mathf.Max(1, (list.Count + GridPerPage - 1) / GridPerPage);
            _gridPageIdx = Mathf.Clamp(_gridPageIdx, 0, pages - 1);
            _gridPage.text = (list.Count == 0 ? Lang.T("~bag~empty") : $"{_gridPageIdx + 1} / {pages}") + "   ·   " + Lang.F("{0} of {1} slots", _inv.Items.Count, _inv.Capacity);
            for (int i = 0; i < _cells.Count; i++)
            {
                var c = _cells[i]; int at = _gridPageIdx * GridPerPage + i;
                bool has = at < list.Count;
                if (c.Btn.gameObject.activeSelf != has) c.Btn.gameObject.SetActive(has);
                if (!has) { c.Index = -1; continue; }
                var e = list[at]; c.Index = e.Index;
                c.Icon.sprite = ItemIcon(e.Info); c.Icon.enabled = c.Icon.sprite != null;
                c.Count.text = e.IsEquip ? (e.Refine > 0 ? "+" + e.Refine : "") : e.Amount.ToString();
                c.Mark.text = (e.Wearing != 0 ? Lang.T("~grid~worn") : "") + (e.Favorite ? " <color=#ffd060>★</color>" : "");
                bool sel = e.Index == _gridSel, locked = e.Index == _gridLock;
                c.Btn.Normal = locked ? LockedCell : sel ? new Color(0.45f, 0.35f, 0.15f) : e.Wearing != 0 ? new Color(0.13f, 0.27f, 0.2f) : UiKit.ButtonBg;
                if (c.LockFrame.activeSelf != locked) c.LockFrame.SetActive(locked);
                if (!c.Btn.Hovered) c.Btn.SetHover(false);
                if (!c.Icon.enabled) { c.Count.text = ""; c.Btn.SetLabel(e.Info.Name); } else c.Btn.SetLabel(""); // no picture in the data: its name
            }
            for (int i = 0; i < _cells.Count; i++) if (_cells[i].Index < 0 && _cells[i].LockFrame.activeSelf) _cells[i].LockFrame.SetActive(false);
            // client wave 13 (w13items), item 12: the lock ends when its item is gone (used up, dropped, moved away) or no shown cell holds it
            // (another filter, sort or page hid it): kept while its cell is there
            if (_gridLock >= 0)
            {
                bool there = false; for (int i = 0; i < _cells.Count; i++) if (_cells[i].Index == _gridLock) { there = true; break; }
                if (!_inv.Items.ContainsKey(_gridLock)) GridUnlock("its item is gone");
                else if (!there) GridUnlock("no shown cell holds it");
            }
            if (_gridSel >= 0 && !_inv.Items.ContainsKey(_gridSel)) _gridSel = -1;
        }

        // ---- client wave 13 (w13items), the brief's twelfth item (design note, 2026-10-07: "the first time that I pull the trigger, it will select that
        // item and prevent me from selecting other items with my gaze ... If I look at another item and I pull the trigger, then that item will
        // be locked in. If I am looking at that item and I pull the trigger again, or I am looking anywhere other than another different item,
        // pulling the trigger will use the item that I am currently locked into"; and "For now, just the bag."):
        //   nothing locked: the item under the laser or the look is shown in the panels (a preview), as before;
        //   a pull on an item LOCKS it (nothing is used, no packet): the panels stay on it whatever the pointer or the look crosses, its
        //   cell is marked (LockedCell + a bright frame), the item panel says "Locked · trigger again: <its action>";
        //   with an item locked: a pull on ANOTHER item locks that one instead (nothing used); a pull on the locked item, or anywhere on
        //   the bag that is no button (the board's background, an empty place in the grid, the panels' text) USES the locked item: its
        //   own action, the call wave 11's first pull made (ItemActionOn). Buttons stay buttons (tabs, filters, sort, pages, X, the
        //   panels' Equip / Use / Details / ★ / Unlock, the back arrow). "The bag" ends at the board and its panels: a pull with the laser
        //   on another window, or with the laser and the look both on the world, does what it does today and uses nothing
        //   (VR/DioramaRig.cs PanelPull: the laser's panel decides, else the look's).
        //   The lock ends: another item locked; its item gone (used up, dropped, moved away); no shown cell holds it (a filter, the sort or
        //   a page hid it); the board closed (item 11's back arrow brings it back with the board); the Items page left; the Unlock button
        //   on the item panel (a way to let go without closing: a pull on the background would use the item).
        private int _gridLock = -1;
        /// <summary>The locked item's inventory index (-1: none), and the counts of locks / uses by the bag's rule (smoke, log).</summary>
        public int GridLocked => _gridLock;
        public int GridLocks { get; private set; }
        public int GridUses { get; private set; }
        public string GridLastUse { get; private set; } = "";
        public static readonly Color LockedCell = new Color(0.62f, 0.45f, 0.10f), LockFrameColor = new Color(1f, 0.86f, 0.35f, 1f);

        /// <summary>The trigger on a cell: locks its item; on the item already locked: uses it.</summary>
        private void GridPress(int cell)
        {
            if (cell < 0 || cell >= _cells.Count) return;
            int idx = _cells[cell].Index; if (idx < 0) return;
            if (idx == _gridLock) { BagUse("the locked item's own cell"); return; }
            _gridLock = idx; _gridSel = idx; GridLocks++; _dirty = true;
            if (_inv.Items.TryGetValue(idx, out var e)) Debug.Log($"[Bag] locked {e.ItemId} {e.Info.English} (index {idx})");
        }

        /// <summary>The locked item's own action (use / equip / take off / insert).</summary>
        private void BagUse(string where)
        {
            if (_gridLock < 0) return;
            GridUses++; GridLastUse = where; _dirty = true;
            Debug.Log($"[Bag] use the locked item (index {_gridLock}): {where}");
            ItemActionOn(_gridLock);
        }

        public void GridUnlock(string why)
        {
            if (_gridLock < 0) return;
            Debug.Log($"[Bag] unlocked (index {_gridLock}): {why}");
            _gridLock = -1; _dirty = true;
        }

        /// <summary>The rig's pull that pressed no button, on a panel (the laser's, else the look's): on the bag with an item locked it
        /// unlocks/deselects that item (true: the pull is taken).</summary>
        private bool BagPull(Transform panel)
        {
            if (_board == null || panel != _board.transform || !GridShown || _gridLock < 0) return false;
            GridUnlock("a pull on the bag, not on an item");
            _gridSel = -1; _dirty = true;
            return true;
        }

        /// <summary>Every frame while the grid is shown: the cell under the laser or the look is the one whose panel is shown (not while
        /// an item is locked: item 12).</summary>
        private void BoardTick()
        {
            if (_board != null && _board.Toast != null && _toast != null && _board.Toast.text != _toast.text) _board.Toast.text = _toast.text;
            if (_gridLock >= 0 && (_tab != Tab.Items || _w3equipOpen)) GridUnlock("the Items page was left"); // (client wave 13, item 12)
            InvTick(); // client wave 21 (w21inv), point 62: the new inventory's pointer, cards and opening (WristMenu.Inv.cs)
            if (_grid == null || !_grid.gameObject.activeInHierarchy) return;
            if (_gridLock >= 0) { if (_gridSel != _gridLock) { _gridSel = _gridLock; _dirty = true; } return; }
            for (int i = 0; i < _cells.Count; i++)
            {
                var c = _cells[i];
                if (c.Index < 0 || !c.Btn.Hovered || c.Index == _gridSel) continue;
                _gridSel = c.Index; _dirty = true; // the page is drawn again (the cell's mark) and with it the card
                break;
            }
        }

        // ------------------------------------------------------------------ the item panels beside the board
        // Client wave 13 (w13items), the brief's eighth item (design note, 2026-10-07: "the entire description and stats needs to be visible, so just
        // use a panel on the right ... If I point at a piece of equipment, an extra panel should also show up to the right of that one as the
        // comparison one. Both of them need to be visible in their entirety"). Wave 11's column of scrolling cards (ItemCard, a 470 px column
        // turned 32° at the board's right edge) is replaced by panels that show everything (UI/ItemPanel.cs: no scrolling, nothing cut):
        //   the ITEM PANEL right of the board: the item pointed at (or locked: item 12, or whose Details are open), with its buttons;
        //   the COMPARISON PANEL right of it, for a piece of equipment with something worn where it would go: the worn item, whole.
        //   Two worn items (two accessories, a two-handed weapon against weapon + shield): both, one under the other in one column
        //   when both fit in ItemPanel.MaxH, else side by side (a third panel). Nothing worn there: no comparison panel, a line in the
        //   item panel ("Nothing worn at …").
        // WHERE (LayoutPanels): on a circle around the eyes at the board's own distance, as seen from where the eyes were when the board
        // was placed or let go: each panel faces the eyes, the first begins PanelGapPx past the board's right edge, the next one the
        // same past it: they never cover the grid or each other as seen from the eyes. Their tops are level with the board's top.
        // They are children of the board: they move and resize with it.
        private ItemPanel _pItem, _pWornA, _pWornB; private UiButton _asideAct, _asideDetails, _asidePin, _asideUnlock;
        private RectTransform _hint; private Text _hintText;
        /// <summary>The gap between the board and the first panel, and between panels, in px (at the board's distance: 1.8°).</summary>
        public const float PanelGapPx = 28f;
        /// <summary>Where the eyes were, in the board's own pixels, when the board was last placed or let go (the panels face them).</summary>
        private Vector3 _eye = DefaultEye;
        private static readonly Vector3 DefaultEye = new Vector3(0f, 0.10f / MenuBoard.MetresPerPx, -0.65f / MenuBoard.MetresPerPx);
        private int _placedSeen = -1;
        /// <summary>The panels beside the board (smoke): the item; what is worn where it would go (A, and B for a second worn item).</summary>
        public ItemPanel AsideSelected => _invOn ? _invPanel.CardItem : _pItem;
        public ItemPanel AsideWorn(int i) { var p = _invOn ? (i == 0 ? _invPanel.CardWornA : i == 1 ? _invPanel.CardWornB : null) : i == 0 ? _pWornA : i == 1 ? _pWornB : null; return p != null && p.Shown ? p : null; }
        public UiButton AsideButton(string which) => _invOn ? (which == "act" ? _invPanel.CardAct : which == "details" ? _invPanel.CardDetails : which == "pin" ? _invPanel.CardPin : which == "unlock" ? _invPanel.CardUnlock : null) : which == "act" ? _asideAct : which == "details" ? _asideDetails : which == "pin" ? _asidePin : which == "unlock" ? _asideUnlock : null;
        /// <summary>The worn items' panels stand one under the other (true) or side by side (smoke).</summary>
        public bool WornStacked { get => _invOn ? _invPanel.WornStacked : _wornStacked; private set => _wornStacked = value; }
        private bool _wornStacked;
        public Vector3 PanelEye => _eye;
        public RectTransform HintBox => _hint;

        private void BuildAside()
        {
            var a = _board.Aside;
            _pItem = ItemPanel.Create(a, "item", true);
            var row = _pItem.ButtonRow; float bw = (ItemPanel.TextW - 3 * 8) / 4f;
            float X(int i) => -ItemPanel.TextW / 2 + bw / 2 + i * (bw + 8);
            _asideAct = UiKit.Button(row, "", new Vector2(X(0), 0), new Vector2(bw, ItemPanel.ButtonH), () => { int i = AsideIndex; if (i >= 0) ItemActionOn(i); _dirty = true; }, 24);
            _asideDetails = UiKit.Button(row, Lang.T("Details"), new Vector2(X(1), 0), new Vector2(bw, ItemPanel.ButtonH), () => { if (_gridSel >= 0) OpenDetailsFromBag(_gridSel); }, 24);
            _asidePin = UiKit.Button(row, Lang.T("★ Quick slot"), new Vector2(X(2), 0), new Vector2(bw, ItemPanel.ButtonH), () => { if (_gridSel >= 0 && _inv.Items.TryGetValue(_gridSel, out var e)) OpenSlotPicker(false, e.ItemId, 0); }, 24);
            _asideUnlock = UiKit.Button(row, Lang.T("Unlock"), new Vector2(X(3), 0), new Vector2(bw, ItemPanel.ButtonH), () => GridUnlock("the Unlock button"), 24);
            foreach (var b in new[] { _asideAct, _asideDetails, _asidePin, _asideUnlock }) b.Text.resizeTextMinSize = 21; // two lines of 21 px or more (the panels' floor: ItemPanel.FontFloor)
            _asideUnlock.gameObject.SetActive(false);
            _pWornA = ItemPanel.Create(a, "wornA", false); _pWornB = ItemPanel.Create(a, "wornB", false);
            _hint = UiKit.Box(a, "hint", Vector2.zero, new Vector2(ItemPanel.TextW + 2 * ItemPanel.Pad, 150), new Color(1, 1, 1, 0));
            _hint.GetComponent<UnityEngine.UI.Image>().enabled = false; UiKit.Dress(_hint, new Vector2(ItemPanel.TextW + 2 * ItemPanel.Pad, 150), Mathf.Max(1e-5f, _hint.lossyScale.x), PanelLook.Card); // client wave 21 (w21kit): a parchment card of the theme
            _hintText = UiKit.Label(_hint, "", Vector2.zero, new Vector2(ItemPanel.TextW, 130), 24, TextAnchor.MiddleCenter, UiKit.Dim);
            var hc = _hint.gameObject.AddComponent<BoxCollider>(); hc.size = new Vector3(ItemPanel.TextW + 2 * ItemPanel.Pad, 150, 4f); hc.center = new Vector3(0, 0, 3f);
            _hint.gameObject.AddComponent<UiBlocker>();
            _hint.gameObject.SetActive(false);
        }

        /// <summary>The item panel's Details: the full page of the item on the board (client wave 13, item 11: a step of the board's history).</summary>
        private void OpenDetailsFromBag(int idx)
        {
            if (idx < 0) return;
            if (_onBoard && _board != null && _board.Shown) MenuNav.PushInPlace(_navBoard, CaptureBoard(), "the item's Details");
            _detailIndex = idx; _dirty = true;
        }

        /// <summary>The item the panels are about: the one whose details are open, else the locked / pointed one (-1: none).</summary>
        private int AsideIndex => _detailIndex >= 0 ? _detailIndex : _gridSel;

        /// <summary>The panels for the item pointed at in the grid (or locked, or whose details are open): see above.</summary>
        private void RefreshAside()
        {
            if (_board == null || !_onBoard) return;
            if (_invOn) { _board.ShowAside(false); return; } // client wave 21 (w21inv): the new inventory has its own cards (WristMenu.Inv.cs)
            bool show = _tab == Tab.Items && !_w3equipOpen && !W11PickerOpen;
            _board.ShowAside(show);
            if (!show) return;
            int idx = AsideIndex;
            Inventory.Entry e = null; if (idx >= 0) _inv.Items.TryGetValue(idx, out e);
            if (e == null)
            {
                _pItem.Clear(); _pWornA.Clear(); _pWornB.Clear();
                _hintText.text = Lang.T("Look at an item. Trigger: lock it. Again: use it.");
                if (!_hint.gameObject.activeSelf) _hint.gameObject.SetActive(true);
                LayoutPanels();
                return;
            }
            if (_hint.gameObject.activeSelf) _hint.gameObject.SetActive(false);
            string verb = e.Wearing != 0 ? Lang.T("Take off") : e.IsEquip ? Lang.T("Equip") : e.IsCard ? Lang.T("Insert") : e.Info.IsConsumable ? Lang.T("Use") : null;
            bool tooLow = e.Info.MinLevel > _ps.BaseLevel && e.Wearing == 0;
            bool locked = _detailIndex < 0 && _gridLock >= 0 && _gridLock == e.Index;
            string head = locked ? (verb != null ? Lang.F("Locked · trigger again: {0}", verb) : Lang.T("Locked"))
                        : e.Wearing != 0 ? Lang.F("You wear at {0}:", EquipmentWindow.SlotNames(e.Wearing)) : Lang.T("In your bag:");
            var worn = W4CompareTargets(e);
            string note = e.IsEquip && e.Wearing == 0 && worn.Count == 0 ? Lang.F("Nothing worn at {0}", EquipmentWindow.SlotNames(e.Location)) : null;
            _asideAct.gameObject.SetActive(verb != null);
            if (verb != null) { _asideAct.SetLabel(tooLow ? "Lv " + e.Info.MinLevel : verb); _asideAct.Normal = tooLow ? new Color(0.3f, 0.3f, 0.32f) : UiKit.ButtonBg; if (!_asideAct.Hovered) _asideAct.SetHover(false); }
            _asideDetails.gameObject.SetActive(_detailIndex < 0);
            _asidePin.gameObject.SetActive(Hotkeys != null && !e.IsEquip && e.Info.IsConsumable);
            _asideUnlock.gameObject.SetActive(locked);
            _pItem.Set(e, head, note);
            if (worn.Count >= 1) _pWornA.Set(worn[0], Lang.F("You wear at {0}:", EquipmentWindow.SlotNames(worn[0].Wearing))); else _pWornA.Clear();
            if (worn.Count >= 2) _pWornB.Set(worn[1], Lang.F("You wear at {0}:", EquipmentWindow.SlotNames(worn[1].Wearing))); else _pWornB.Clear();
            LayoutPanels();
        }

        /// <summary>Where the eyes are now against the board, in its pixels (when it was placed or let go): the panels face that point.</summary>
        private void UpdateEye()
        {
            if (_board == null || _head == null) return;
            var e = _board.transform.InverseTransformPoint(_head.transform.position);
            _eye = e.z < -300f && Mathf.Abs(e.x) < 2000f ? e : DefaultEye; // (behind or beside the board: the default place's eye)
        }

        /// <summary>The panels on the circle around the eyes (see above), upright (the board itself leans back towards the eyes; the panels
        /// stand straight, so their tops stay level with the board's top and their letters face the eyes). Angles in radians from the
        /// eyes' straight-ahead (towards the board's middle, level), positive to the right.</summary>
        private void LayoutPanels()
        {
            var bt = _board.transform; float s = Mathf.Abs(bt.lossyScale.x); if (s < 1e-6f) return;
            var eye = bt.TransformPoint(_eye);
            var fwd = bt.position - eye; fwd.y = 0f; if (fwd.sqrMagnitude < 1e-8f) { fwd = bt.forward; fwd.y = 0f; } fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);
            var d0 = bt.position - eye; float r = Mathf.Max(0.2f, new Vector2(Vector3.Dot(d0, right), Vector3.Dot(d0, fwd)).magnitude);
            var corners = new Vector3[4]; _board.Root.GetWorldCorners(corners);
            float th = float.MinValue, top = float.MinValue;
            foreach (var c in corners) { var v = c - eye; th = Mathf.Max(th, Mathf.Atan2(Vector3.Dot(v, right), Vector3.Dot(v, fwd))); top = Mathf.Max(top, c.y); }
            float gap = PanelGapPx * s / r, facetHalf = Mathf.Atan(ItemPanel.BoxW * s / 2f / r), colGap = ItemPanel.ColGap * s / r;
            th += gap; // the board's right edge as the eyes see it, then the gap
            Vector3 Dir(float ang) => Mathf.Sin(ang) * right + Mathf.Cos(ang) * fwd;
            void Stand(Transform t, float ang, float yTop, float hPx) { t.SetPositionAndRotation(eye + Dir(ang) * r + Vector3.up * (yTop - hPx * s / 2f - eye.y), Quaternion.LookRotation(Dir(ang), Vector3.up)); }
            // a panel: its facets one after the other on the circle, each facing the eyes; returns where the next panel may begin
            float Put(ItemPanel p, float left, float yTop)
            {
                float c = left;
                for (int i = 0; i < p.Columns; i++)
                {
                    c += facetHalf; Stand(p.Facet(i), c, yTop, p.Height);
                    c += facetHalf + (i + 1 < p.Columns ? colGap : 0f);
                }
                return c + gap;
            }
            if (_hint.gameObject.activeSelf) Stand(_hint, th + Mathf.Atan(_hint.sizeDelta.x * s / 2f / r), top, _hint.sizeDelta.y);
            if (_pItem.Shown) th = Put(_pItem, th, top);
            bool a = _pWornA.Shown, b = _pWornB.Shown;
            // two worn items: one under the other in one column when both are one facet and fit in MaxH together, else side by side
            WornStacked = a && b && _pWornA.Columns == 1 && _pWornB.Columns == 1 && _pWornA.Height + PanelGapPx + _pWornB.Height <= ItemPanel.MaxH;
            if (WornStacked)
            {
                float c = th + facetHalf;
                Stand(_pWornA.Facet(0), c, top, _pWornA.Height);
                Stand(_pWornB.Facet(0), c, top - (_pWornA.Height + PanelGapPx) * s, _pWornB.Height);
            }
            else
            {
                if (a) th = Put(_pWornA, th, top);
                if (b) th = Put(_pWornB, th, top);
            }
        }

        // ------------------------------------------------------------------ one menu at a time (client wave 13, w13items, the brief's eleventh item)
        // UI/MenuNav.cs holds the rule; here the ring and the board are registered with it, and the board's state is taken and put back:
        // its place and size, the eyes' point its panels face, its group, page, the bag's filter / sort / page, the item shown and the lock,
        // the item's Details and their scroll, the classic list's page. The board's own steps (the item's Details, the quick-slot picker, a
        // page of another group) are steps in place: the board stays where it is. Classic is left as it is (MenuNav.Enabled = the new style).
        private MenuNav.Menu _navRing, _navBoard; private bool _navFromRing; private int _navGroupShown = -1;
        private sealed class BoardState { public Vector3 Pos, Scale, Eye; public Quaternion Rot; public Tab Tab; public int Group, Detail, Filter, Sort, GridPage, Sel, Lock, ItemPage; public bool Equip; public float DetailScroll; }
        public MenuNav.Menu NavBoard => _navBoard;
        public MenuNav.Menu NavRing => _navRing;

        private void InitNav()
        {
            MenuNav.Enabled = () => this != null && NewStyle;
            MenuNav.BackMode = () => VR.Comfort.MenuBack;
            _navRing = new MenuNav.Menu { Name = "MenuRing", Panel = _ring.transform, IsOpen = () => _ring != null && _ring.Shown, Close = () => _ring.Hide(), Reopen = (o, st) => ShowRing() };
            _navBoard = new MenuNav.Menu { Name = "MenuBoard", Panel = _board.transform, IsOpen = () => _board != null && _board.Shown && gameObject.activeSelf, Close = HidePages,
                                           Capture = CaptureBoard, Reopen = (o, st) => RestoreBoard(st as BoardState), Arrow = _board.SetBackArrow };
            MenuNav.Register(_navRing); MenuNav.Register(_navBoard);
            // client wave 15 (w15menu), point 10: the classic wrist menu follows the rule too in the Classic style (UI/MenuNav.cs CLASSIC): a
            // window opened from it closes it; the window's arrow brings it back on the page it showed. Not registered with the windows (it is
            // no window: the menu button and the backpack open it, as before)
            MenuNav.ClassicStyle = () => this != null && !NewStyle;
            MenuNav.ClassicMenu = new MenuNav.Menu { Name = "WristMenu", Panel = _rt, IsOpen = () => this != null && ShownOnWrist, Close = HidePages,
                                                     Capture = () => _tab, Reopen = (o, st) => Show(st is Tab t ? t : _tab) };
            // the buttons whose window opens on the server's answer (the daily check-in, the roulette)
            foreach (var b in _pages[Tab.Achievements].GetComponentsInChildren<UiButton>(true)) if (b.name == "btn:Daily check-in") MenuNav.OpensLater(b);
            MenuNav.OpensLater(_w4rouletteBtn);
        }

        private object CaptureBoard()
        {
            if (_board == null) return null;
            var t = _board.transform;
            return new BoardState { Pos = t.position, Rot = t.rotation, Scale = t.localScale, Eye = _eye, Tab = _tab, Group = _group, Equip = _w3equipOpen, Detail = _detailIndex, Filter = _itemFilter, Sort = _itemSort,
                                    GridPage = _gridPageIdx, Sel = _gridSel, Lock = _gridLock, ItemPage = _itemPageIdx, DetailScroll = _detScroll != null ? _detScroll.Offset : 0f };
        }

        /// <summary>The board as it was: on its page, where it was (if it was closed), the same item shown and locked, the details scrolled as they were.</summary>
        private void RestoreBoard(BoardState st)
        {
            if (st == null || _board == null) return;
            if (W11PickerOpen) CloseSlotPicker(false);
            if (!(_board.Shown && gameObject.activeSelf)) _board.PlaceNext(st.Pos, st.Rot, st.Scale);
            _tab = st.Tab; _group = st.Group; _w3equipOpen = st.Equip;
            _detailIndex = st.Detail >= 0 && _inv.Items.ContainsKey(st.Detail) ? st.Detail : -1;
            _itemFilter = st.Filter; _itemSort = st.Sort; _gridPageIdx = st.GridPage; _itemPageIdx = st.ItemPage;
            _gridSel = st.Sel >= 0 && _inv.Items.ContainsKey(st.Sel) ? st.Sel : -1;
            _gridLock = st.Lock >= 0 && _inv.Items.ContainsKey(st.Lock) ? st.Lock : -1;
            Show(_tab);
            _eye = st.Eye; _placedSeen = _board.Placed;
            _dirty = false; Refresh();
            if (_detScroll != null && _detailIndex >= 0) _detScroll.ScrollTo(st.DetailScroll);
            Debug.Log($"[Menu] the board back: {Groups[_group].Name} / {_tab}{(_detailIndex >= 0 ? " (details)" : "")}, item {_gridSel}{(_gridLock >= 0 ? " locked" : "")}");
        }

    }
}
