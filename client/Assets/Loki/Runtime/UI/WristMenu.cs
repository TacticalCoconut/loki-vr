using System.Collections.Generic;
using System.Linq;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// The menu on the left wrist: Status (HP/SP/EXP/zeny, effects, sit/stand), Stats (raise STR…LUK, derived stats),
    /// Skills (learn / level up, arm or cast), Items (use / equip / take off / details / drop), Quests (log with hunt counters),
    /// View (Table / Follow / First person, minimap, music), Emotes (0x00bf) and Quick (the server-saved hotkey bar, tab 0
    /// slots 0-8: pinned items and skills). Follows the left controller and always faces the head.
    /// </summary>
    public sealed partial class WristMenu : MonoBehaviour
    {
        public enum Tab { Status, Stats, Skills, Items, Quests, View, Emotes, Quick, Social, Party, Guild, Companions, Mail, Achievements, Bank, CashShop, Community, Booking, Modes, Family }
        private const int TabCount = 20;
        private const float W = 640, H = 1100, PH = 710; // PH = page height
        private const float PageY = H / 2 - 335 - PH / 2; // below five rows of tabs (4 per row; client wave 2 added the fifth)
        private RectTransform _rt;
        private Tab _tab = Tab.Status;
        /// <summary>Client wave 22 (w22clips, the casting ways): the page shown now (the Skills page's lesson waits for it).</summary>
        public Tab CurrentTab => _tab;
        private readonly Dictionary<Tab, RectTransform> _pages = new Dictionary<Tab, RectTransform>();
        private readonly Dictionary<Tab, UiButton> _tabs = new Dictionary<Tab, UiButton>();
        private Transform _hand; private Camera _head;
        private PlayerState _ps; private Inventory _inv; private QuestLog _quests; private Net.RoSession _s; private System.Func<string> _charName;
        private Progression _prog; private VR.ViewController _view; private Minimap _minimap; private System.Action<Progression.SkillEntry> _useSkill;
        private Text _toast; private float _toastUntil;
        private bool _dirty = true;

        // status widgets
        private Text _stName, _stLevels, _stHp, _stSp, _stExp, _stJexp, _stMisc;
        private RectTransform _hpFill, _hpShield, _spFill, _bexpFill, _jexpFill;
        private UiButton _sit; private bool _sitting;
        // item widgets
        private readonly List<(Image icon, Text label, UiButton act, UiButton pin, UiButton open)> _itemRows = new List<(Image, Text, UiButton, UiButton, UiButton)>();
        private Text _itemPage; private int _itemPageIdx; private const int ItemsPerPage = 6;
        // client wave 2: item filters (the official inventory's tabs, with the favourite tab) and sort order
        private int _itemFilter, _itemSort; private readonly List<UiButton> _filterBtns = new List<UiButton>(); private UiButton _sortBtn, _detFav;
        private static readonly string[] ItemFilters = { Lang.N("All"), Lang.N("~filter~Use"), Lang.N("~filter~Equip"), Lang.N("Etc"), Lang.N("★ Fav") }, ItemSorts = { Lang.N("Sort: type"), Lang.N("Sort: name"), Lang.N("Sort: weight") };
        // item detail view (inside the Items page)
        private RectTransform _itemList, _itemDetail; private int _detailIndex = -1;
        private Image _detIcon; private Text _detName, _detInfo, _detDesc; private UiButton _detAct, _detDrop1, _detDropAll, _detBack;
        // quick bar + emotes
        private readonly List<(Image icon, Text label, UiButton use, UiButton clear)> _quickRows = new List<(Image, Text, UiButton, UiButton)>();
        public Hotkeys Hotkeys; public System.Action<int> SendEmote;
        // social
        public SocialHub Social; public System.Func<string> GazedPlayerName; public System.Action<VrTextField> RequestKeyboard; public System.Action<string> PrefillChat;
        private VrTextField _friendName; private Text _socialInfo, _requestText; private UiButton _accept, _decline, _addGazed;
        private int _friendPage; private Text _friendPageText; private bool _socialHooked; // the rows: WristMenu.W4Social.cs (client wave 4)
        // party
        public Party Party; private bool _partyHooked;
        // guild
        public Guild Guild; private bool _guildHooked;
        public Companions Companions; private bool _compHooked;
        // mail, achievements, bank, cash shop, swap set, costumes (item 9)
        public Mail Mail; public Achievements Achievements; public Bank Bank; public CashShop CashShop; public EquipSwitch Swap; public CheckIn CheckIn;
        /// <summary>View tab: current "hide my costumes" state and the toggle (CZ_CONFIG type 5).</summary>
        /// <summary>View tab: the Settings window (Comfort &amp; controls before client wave 4).</summary>
        public System.Action OpenComfort;
        /// <summary>Client wave 13 (w13view): the View page's minimap button switches the see-through minimap's stored state (LokiGame.W13View.cs).</summary>
        public System.Action ToggleMinimap;
        /// <summary>"Leave the game…" (character select, log out, quit).</summary>
        public System.Action OpenLeave;
        private bool _item9Hooked; private Text _mailInfo, _achInfo, _bankInfo, _cashInfo; private UiButton _swapAllBtn, _detSwap;
        private Text _guildTitle, _guildInvite, _guildInfo; private UiButton _gAccept, _gDecline; private RectTransform _guildNone, _guildIn; private VrTextField _guildName;
        /// <summary>Social tab: ask the gaze-locked player to trade.</summary>
        public System.Action TradeGazed;
        /// <summary>Items tab: the cart window and the market button (buying store / own shop). MarketLabel names the button.</summary>
        public System.Action OpenCart, OpenMarket; public System.Func<string> MarketLabel;
        private UiButton _marketBtn;
        private Text _partyTitle, _partyInvite, _partyOptions; private UiButton _pAccept, _pDecline, _pLeave, _pExp, _pInviteGazed, _pRefuse;
        private VrTextField _partyName; private RectTransform _partyNoParty, _partyIn, _pInviteRow;
        /// <summary>Drop an item: client index, amount.</summary>
        public System.Action<int, int> DropItem;
        // stats widgets
        private readonly Text[] _statLabels = new Text[6]; private readonly UiButton[] _statPlus = new UiButton[6];
        private Text _statPoints, _derived;
        // skill widgets
        private readonly List<(Image icon, Text label, UiButton up, UiButton use, UiButton pin)> _skillRows = new List<(Image, Text, UiButton, UiButton, UiButton)>();
        private Text _skillPage; private int _skillPageIdx; private const int SkillsPerPage = 6;
        // view widgets
        private readonly UiButton[] _modeButtons = new UiButton[3]; private UiButton _mapButton; private Text _viewInfo;
        // quest widgets
        private readonly List<(Text title, Text body, UiButton row)> _questRows = new List<(Text, Text, UiButton)>();
        private Text _questPage; private int _questPageIdx; private const int QuestsPerPage = 3; private int _expandedQuest = -1;

        public bool Visible => gameObject.activeSelf;
        /// <summary>Client wave 13 (w13look), item 24: the classic menu's eye (its kind follows the page shown).</summary>
        public LookEye ClassicEye { get; private set; }
        /// <summary>A card was "used": the game asks the server which equipment can take it (0x017a).</summary>
        public System.Action<Inventory.Entry> OnUseCard;

        public static WristMenu Create(Transform parent, Transform leftHand, Camera head, PlayerState ps, Inventory inv, QuestLog quests, Net.RoSession s, System.Func<string> charName,
            Progression prog, VR.ViewController view, Minimap minimap, System.Action<Progression.SkillEntry> useSkill)
        {
            var rt = UiKit.Panel("WristMenu", parent, new Vector2(W, H), ClassicMetresPerPx); // client wave 11 (w11menu): 25 % larger (WristMenu.W11.cs)
            var m = rt.gameObject.AddComponent<WristMenu>();
            m._rt = rt; m._hand = leftHand; m._head = head; m._ps = ps; m._inv = inv; m._quests = quests; m._s = s; m._charName = charName;
            m._prog = prog; m._view = view; m._minimap = minimap; m._useSkill = useSkill;
            m.Build();
            ps.Changed += () => m._dirty = true; inv.Changed += () => m._dirty = true; quests.Changed += () => m._dirty = true; prog.Changed += () => m._dirty = true;
            if (view != null) view.Changed += _ => m._dirty = true;
            rt.gameObject.SetActive(false);
            return m;
        }

        /// <summary>The menu button (X / A, the strip's Menu). Client wave 11 (w11menu): in the New style it shows / hides the launcher
        /// ring (UI/WristMenu.Board.cs); Classic: the wrist menu, as before.</summary>
        public void Toggle() { if (NewStyle && _ring != null) { ToggleRing(); return; } gameObject.SetActive(!gameObject.activeSelf); _dirty = true; }
        public void Hide() { HidePages(); if (Tree != null && Tree.Shown) Tree.Close(); } // (client wave 22, w22tree: the 3D skill tree is a part of the menu: UI/WristMenu.Tree.cs)
        /// <summary>Client wave 22 (w22tree): the ring and the wrist menu / the board alone (the one-menu-at-a-time rule closes the board when the tree opens from it).</summary>
        private void HidePages() { if (_ring != null) _ring.Hide(); if (gameObject.activeSelf) gameObject.SetActive(false); }
        public void NextTab() { Show((Tab)(((int)_tab + 1) % TabCount)); }
        /// <summary>A short message at the bottom of the wrist menu. Client wave 11 (w11menu): the HUD also shows it in front of the
        /// player when the menu is closed, and always when <paramref name="front"/> is set (a refusal / failure: UI/LokiHud.cs Notify).</summary>
        public void Toast(string msg, float seconds = 4f, bool front = false)
        {
            if (_toast == null) return;
            _toast.text = msg; _toastUntil = Time.time + seconds;
            if (!string.IsNullOrEmpty(msg)) ToastShown?.Invoke(msg, seconds, front);
        }
        /// <summary>Client wave 11 (w11menu): a toast was set (text, seconds, whether it must be shown in front of the player).</summary>
        public event System.Action<string, float, bool> ToastShown;
        /// <summary>Client wave 18 (w18wand), point 18: the toast line emptied now if it still says <paramref name="msg"/> (UI/LokiHud.cs Dismiss).</summary>
        public void ClearToast(string msg) { if (_toast != null && !string.IsNullOrEmpty(msg) && _toast.text == msg) { _toast.text = ""; _toastUntil = 0f; } }
        public void Show(Tab t)
        {
            if (t == Tab.Skills && TreeOpens()) return; // client wave 22 (w22tree), point 64: the 3D skill tree in the flat page's place (UI/WristMenu.Tree.cs)
            W11BeforeShow(); // client wave 11 (w11menu): the slot picker closes, the compare cards go (WristMenu.W11.cs)
            _tab = t; if (!gameObject.activeSelf) gameObject.SetActive(true);
            foreach (var kv in _pages) kv.Value.gameObject.SetActive(kv.Key == t);
            foreach (var kv in _tabs) kv.Value.Normal = kv.Key == t ? new Color(0.45f, 0.35f, 0.15f) : UiKit.ButtonBg;
            foreach (var kv in _tabs) kv.Value.SetHover(false);
            _dirty = true;
            if (_onBoard) { if (_ring != null) _ring.Hide(); BoardSync(); } // client wave 11 (w11menu): New style: the page is on its group's board
        }

        private void Build()
        {
            float tw = (W - 40) / 4f;
            string[] names = { Lang.N("Status"), Lang.N("Stats"), Lang.N("Skills"), Lang.N("Items"), Lang.N("Quests"), Lang.N("View"), Lang.N("Emotes"), Lang.N("Quick"), Lang.N("Social"), Lang.N("Party"), Lang.N("Guild"), Lang.N("Companions"), Lang.N("Mail"), Lang.N("Achievements"), Lang.N("Bank"), Lang.N("Cash shop"), Lang.N("Chat rooms"), Lang.N("Booking"), Lang.N("Modes"), Lang.N("Family") };
            for (int i = 0; i < TabCount; i++)
            {
                var t = (Tab)i; int row = i / 4, col = i % 4;
                string label = Lang.T(names[i]); // w4lang: the page objects keep their English names
                _tabs[t] = UiKit.Button(_rt, label, new Vector2(-W / 2 + 20 + tw * (col + 0.5f), H / 2 - 40 - row * 60), new Vector2(tw - 8, 52), () => Show(t), label.Length > 10 ? 17 : label.Length > 8 ? 19 : 23);
                var page = UiKit.Box(_rt, "page:" + names[i], new Vector2(0, PageY), new Vector2(W - 30, PH), new Color(0, 0, 0, 0));
                _pages[t] = page;
            }
            BuildStatus(_pages[Tab.Status]);
            BuildStats(_pages[Tab.Stats]);
            BuildSkills(_pages[Tab.Skills]);
            BuildItems(_pages[Tab.Items]);
            BuildQuests(_pages[Tab.Quests]);
            BuildView(_pages[Tab.View]);
            BuildEmotes(_pages[Tab.Emotes]);
            BuildQuick(_pages[Tab.Quick]);
            BuildSocial(_pages[Tab.Social]);
            BuildParty(_pages[Tab.Party]);
            BuildGuild(_pages[Tab.Guild]);
            BuildCompanions(_pages[Tab.Companions]);
            BuildItem9Pages();
            BuildWave2Pages(); // client wave 2: chat rooms / whispers / ignore list, party booking, instances / battlegrounds / duels, family (WristMenu.Wave2.cs)
            BuildW4Items(); // client wave 4 (w4items): item comparison, the favourites' sale lock, the roulette button (WristMenu.W4Items.cs)
            BuildW4World(); // client wave 4 (w4world): the Screenshot / Desktop view / Guide strip under the menu (WristMenu.W4World.cs)
            BuildW8Horde(); // client wave 8 (w8game): the Horde side mode's button under that strip (WristMenu.W8Horde.cs)
            BuildW11(); // client wave 11 (w11menu): the compare cards beside the menu, the quick-slot picker, the Commands button (WristMenu.W11.cs)
            // client wave 13 (w13look), item 24: the look's eye in the bottom right corner (UI/LookSelect.cs): 46 px (2.4 cm: 2.7° from about
            // 0.51 m, the corner's distance with the menu's middle 0.42 m away), 29 px (1.5 cm) inside the right rim, 8 px over the panel's
            // lower edge (the strips hang under it: the menu's outer rim is theirs), under the pages' own bottom buttons (5 px); its kind is
            // the shown page's group, as on the boards. The toast line under the pages gave the eye its right end (600 → 470 px wide, its
            // text shrinks to fit: 22 → 17 px)
            ClassicEye = LookEye.Add(_rt, () => LookKeyOf(GroupOf(_tab, _tab == Tab.Items && _w3equipOpen).group), new Vector2(W / 2 - 29f - 23f, -H / 2 + 8f + 23f), 46f);
            _toast = UiKit.Label(_rt, "", new Vector2(0, -H / 2 + 24), new Vector2(W - 170, 36), 22, TextAnchor.MiddleCenter, new Color(1f, 0.8f, 0.5f));
            _toast.resizeTextForBestFit = true; _toast.resizeTextMinSize = 17; _toast.resizeTextMaxSize = 22;
            Show(Tab.Status);
        }

        private RectTransform Bar(RectTransform parent, float y, Color c, out Text label, string caption)
        {
            label = UiKit.Label(parent, caption, new Vector2(0, y + 26), new Vector2(W - 70, 32), 24, TextAnchor.MiddleLeft);
            var bg = UiKit.Box(parent, "barbg", new Vector2(0, y), new Vector2(W - 70, 22), new Color(0, 0, 0, 0.7f));
            var fill = UiKit.Raw(UiKit.Box(bg, "fill", Vector2.zero, new Vector2(W - 74, 18), c)); // (client wave 21: a bar's colour is drawn as it is)
            fill.anchorMin = fill.anchorMax = new Vector2(0, 0.5f); fill.pivot = new Vector2(0, 0.5f); fill.anchoredPosition = new Vector2(2, 0);
            return fill;
        }

        private void BuildStatus(RectTransform p)
        {
            float top = PH / 2;
            _stName = UiKit.Label(p, "", new Vector2(0, top - 35), new Vector2(W - 70, 44), 34, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            _stLevels = UiKit.Label(p, "", new Vector2(0, top - 80), new Vector2(W - 70, 36), 26, TextAnchor.MiddleLeft);
            _stLevels.resizeTextForBestFit = true; _stLevels.resizeTextMinSize = 18; _stLevels.resizeTextMaxSize = 26; // w4lang
            // client wave 21 (w21kit): on the theme's canvas the bars are dark inks in a light groove (bright fills are faint on cream)
            bool themed = Theme.SurfaceOf(p) != null && Theme.SurfaceOf(p).Light;
            _hpFill = Bar(p, top - 150, themed ? new Color(0.16f, 0.50f, 0.18f) : new Color(0.25f, 0.85f, 0.3f), out _stHp, "HP");
            _hpShield = UiKit.Box((RectTransform)_hpFill.parent, "shield", Vector2.zero, new Vector2(0, 18), new Color(0.45f, 0.90f, 1f, 0.92f));
            _hpShield.anchorMin = _hpShield.anchorMax = new Vector2(0, 0.5f); _hpShield.pivot = new Vector2(0, 0.5f); _hpShield.anchoredPosition = new Vector2(2, 0);
            _hpShield.gameObject.SetActive(false);
            _spFill = Bar(p, top - 225, themed ? new Color(0.14f, 0.34f, 0.70f) : new Color(0.3f, 0.5f, 1f), out _stSp, "SP");
            _bexpFill = Bar(p, top - 300, themed ? Theme.GoldDark : new Color(0.9f, 0.75f, 0.2f), out _stExp, Lang.T("Base EXP"));
            _jexpFill = Bar(p, top - 375, themed ? new Color(0.45f, 0.22f, 0.58f) : new Color(0.8f, 0.5f, 0.9f), out _stJexp, Lang.T("Job EXP"));
            _stMisc = UiKit.Label(p, "", new Vector2(0, top - 470), new Vector2(W - 70, 110), 24, TextAnchor.UpperLeft, UiKit.Dim);
            _sit = UiKit.Button(p, Lang.T("Sit down"), new Vector2(-(W - 70) / 4 - 5, -top + 50), new Vector2((W - 70) / 2 - 10, 64), () =>
            {
                if (_sitting) _s.Stand(); else _s.Sit();
                _sitting = !_sitting; _dirty = true;
            }, 28);
            // CZ_CANCEL_LOCKON 0x0118 (clif_parse_StopAttack): ends the continuous attack the trigger started
            _stopAttackBtn = UiKit.Button(p, Lang.T("Stop attacking"), new Vector2((W - 70) / 4 + 5, -top + 50), new Vector2((W - 70) / 2 - 10, 64), () => { StopAttack?.Invoke(); _dirty = true; }, 24);
        }

        private void BuildItems(RectTransform page)
        {
            float top = PH / 2;
            var p = _itemList = UiKit.Box(page, "list", Vector2.zero, new Vector2(W - 30, PH), new Color(0, 0, 0, 0));
            float fw = (W - 60 - 160) / ItemFilters.Length;
            for (int i = 0; i < ItemFilters.Length; i++)
            {
                int f = i;
                _filterBtns.Add(UiKit.Button(p, Lang.T(ItemFilters[i]), new Vector2(-W / 2 + 25 + fw * (i + 0.5f), top - 32), new Vector2(fw - 6, 48), () => { _itemFilter = f; _itemPageIdx = 0; _dirty = true; }, 19));
            }
            _sortBtn = UiKit.Button(p, Lang.T(ItemSorts[0]), new Vector2(W / 2 - 100, top - 32), new Vector2(150, 48), () => { _itemSort = (_itemSort + 1) % ItemSorts.Length; _dirty = true; }, 17);
            for (int i = 0; i < ItemsPerPage; i++)
            {
                float y = top - 100 - i * 88;
                int slot = i;
                // the row itself opens the detail view (description, weight, drop); the buttons act at once
                var open = UiKit.Button(p, "", new Vector2(0, y), new Vector2(W - 50, 80), () => OpenDetail(slot), 20);
                open.Normal = new Color(1, 1, 1, 0.04f); open.Hover = new Color(0.3f, 0.35f, 0.5f, 0.5f); open.SetHover(false);
                var icon = UiKit.Icon(p, new Vector2(-W / 2 + 60, y), new Vector2(48, 48));
                var label = UiKit.Label(p, "", new Vector2(-62, y), new Vector2(W - 355, 78), 22, TextAnchor.MiddleLeft);
                var pin = UiKit.Button(p, "★", new Vector2(W / 2 - 200, y), new Vector2(54, 60), () => PinItem(slot), 26);
                var act = UiKit.Button(p, Lang.T("Use"), new Vector2(W / 2 - 95, y), new Vector2(140, 60), () => ItemAction(slot), 24);
                _itemRows.Add((icon, label, act, pin, open));
            }
            UiKit.Button(p, "<", new Vector2(-W / 2 + 70, -top + 40), new Vector2(100, 56), () => { _itemPageIdx = Mathf.Max(0, _itemPageIdx - 1); _dirty = true; }, 28);
            UiKit.Button(p, ">", new Vector2(W / 2 - 70, -top + 40), new Vector2(100, 56), () => { _itemPageIdx++; _dirty = true; }, 28);
            _itemPage = UiKit.Label(p, "", new Vector2(0, -top + 40), new Vector2(140, 50), 17, TextAnchor.MiddleCenter, UiKit.Dim);
            UiKit.Button(p, Lang.T("Cart"), new Vector2(-125, -top + 40), new Vector2(110, 56), () => OpenCart?.Invoke(), 22);
            _marketBtn = UiKit.Button(p, Lang.T("Buy store"), new Vector2(125, -top + 40), new Vector2(110, 56), () => OpenMarket?.Invoke(), 19);

            var d = _itemDetail = UiKit.Box(page, "detail", Vector2.zero, new Vector2(W - 30, PH), new Color(0, 0, 0, 0));
            _detIcon = UiKit.Icon(d, new Vector2(-W / 2 + 70, top - 60), new Vector2(72, 72));
            _detName = UiKit.Label(d, "", new Vector2(50, top - 45), new Vector2(W - 200, 50), 26, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            _detInfo = UiKit.Label(d, "", new Vector2(50, top - 85), new Vector2(W - 200, 40), 20, TextAnchor.MiddleLeft, UiKit.Dim);
            _detInfo.resizeTextForBestFit = true; _detInfo.resizeTextMinSize = 14; _detInfo.resizeTextMaxSize = _detInfo.fontSize; // w4lang: the info and flags lines fit their box (they were cut in English too)
            UiKit.Box(d, "descbg", new Vector2(0, top - 335), new Vector2(W - 60, 400), new Color(0, 0, 0, 0.35f));
            // client wave 11 (w11menu): the description scrolls (the design's 2026-10-06 playtest: "I can't scroll down to see the stats": the
            // one text box cut what did not fit, and the stat lines are at a description's end): UI/UiScrollText.cs
            _detScroll = UiScrollText.Create(d, new Vector2(0, top - 335), new Vector2(W - 68, 388), 20);
            _detDesc = _detScroll.Text;
            _detAct = UiKit.Button(d, Lang.T("Use"), new Vector2(-W / 2 + 120, -top + 110), new Vector2(180, 60), () => { if (_detailIndex >= 0) ItemActionOn(_detailIndex); }, 24);
            _detDrop1 = UiKit.Button(d, Lang.T("Drop 1"), new Vector2(40, -top + 110), new Vector2(140, 60), () => Drop(1), 24);
            _detDropAll = UiKit.Button(d, Lang.T("Drop all"), new Vector2(190, -top + 110), new Vector2(150, 60), () => Drop(int.MaxValue), 24);
            _detSwap = UiKit.Button(d, Lang.T("Swap set +"), new Vector2(W / 2 - 110, -top + 180), new Vector2(180, 56), () => ToggleSwap(), 21);
            // CZ_INVENTORY_TAB 0x0907 (client wave 2): move the item to / out of the favourite tab; the server answers 0x0908
            _detFav = UiKit.Button(d, Lang.T("★ Favourite"), new Vector2(-W / 2 + 120, -top + 180), new Vector2(200, 56), () => { if (_detailIndex >= 0 && _inv.Items.TryGetValue(_detailIndex, out var e)) { _s.SendPacket(Inventory.FavoritePacket(e.Index, !e.Favorite)); Toast(e.Favorite ? Lang.F("{0} leaves the favourites.", e.Info.Name) : Lang.F("{0} → favourites.", e.Info.Name)); } }, 21);
            _detBack = UiKit.Button(d, Lang.T("Back"), new Vector2(0, -top + 40), new Vector2(W - 90, 56), () => { if (_onBoard && MenuNav.TopInPlace(_navBoard)) { MenuNav.Back(); return; } _detailIndex = -1; _dirty = true; }, 26); // (client wave 13, item 11: on the board, the step back)
            d.gameObject.SetActive(false);
            BuildW3Equip(page); // client wave 3 (w3equip): the Equipment button + page, the details' side choice (WristMenu.W3Equip.cs)
        }

        private void ToggleSwap()
        {
            if (Swap == null || _detailIndex < 0 || !_inv.Items.TryGetValue(_detailIndex, out var e)) return;
            if (Swap.InSet(e.Index)) { Swap.Remove(e.Index); Toast(Lang.F("{0} left the swap set.", e.Info.Name)); }
            else { Swap.Add(e); Toast(Lang.F("{0} → swap set.", e.Info.Name)); }
            _dirty = true;
        }

        private void OpenDetail(int slot)
        {
            var list = SortedItems();
            int i = _itemPageIdx * ItemsPerPage + slot;
            if (i >= list.Count) return;
            _detailIndex = list[i].Index; _dirty = true;
        }

        private void PinItem(int slot)
        {
            var list = SortedItems();
            int i = _itemPageIdx * ItemsPerPage + slot;
            if (i >= list.Count || Hotkeys == null) return;
            var e = list[i];
            OpenSlotPicker(false, e.ItemId, 0); // client wave 11 (w11menu): the player chooses the slot (WristMenu.W11.cs); it was the first free one
        }

        private void Drop(int amount)
        {
            if (_detailIndex < 0 || !_inv.Items.TryGetValue(_detailIndex, out var e)) return;
            string why = Inventory.DropBlocker(e);
            if (why != null) { Toast(why); return; }
            int n = Mathf.Clamp(amount, 1, e.Amount);
            Debug.Log($"[Items] drop {n} x {e.ItemId} {e.Info.English} (index {e.Index})");
            DropItem?.Invoke(e.Index, n);
            Toast(n > 1 ? Lang.F("Dropped {0} x {1}.", n, e.Info.Name) : Lang.F("Dropped {0}.", e.Info.Name));
        }

        private void BuildQuests(RectTransform p)
        {
            float top = PH / 2;
            for (int i = 0; i < QuestsPerPage; i++)
            {
                float y = top - 110 - i * 205;
                int slot = i;
                var row = UiKit.Button(p, "", new Vector2(0, y), new Vector2(W - 50, 195), () => { int idx = _questPageIdx * QuestsPerPage + slot; _expandedQuest = _expandedQuest == idx ? -1 : idx; _dirty = true; }, 20);
                row.Normal = new Color(1, 1, 1, 0.05f); row.Hover = new Color(0.3f, 0.35f, 0.5f, 0.6f); row.SetHover(false);
                var title = UiKit.Label((RectTransform)row.transform, "", new Vector2(0, 70), new Vector2(W - 80, 44), 25, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
                var body = UiKit.Label((RectTransform)row.transform, "", new Vector2(0, -25), new Vector2(W - 80, 140), 21, TextAnchor.UpperLeft);
                _questRows.Add((title, body, row));
            }
            UiKit.Button(p, "<", new Vector2(-W / 2 + 70, -top + 40), new Vector2(100, 56), () => { _questPageIdx = Mathf.Max(0, _questPageIdx - 1); _expandedQuest = -1; _dirty = true; }, 28);
            UiKit.Button(p, ">", new Vector2(W / 2 - 70, -top + 40), new Vector2(100, 56), () => { _questPageIdx++; _expandedQuest = -1; _dirty = true; }, 28);
            _questPage = UiKit.Label(p, "", new Vector2(0, -top + 40), new Vector2(300, 50), 24, TextAnchor.MiddleCenter, UiKit.Dim);
        }

        private void BuildStats(RectTransform p)
        {
            float top = PH / 2;
            _statPoints = UiKit.Label(p, "", new Vector2(-80, top - 35), new Vector2(W - 230, 44), 30, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            // 4th jobs: the same six rows show POW STA WIS SPL CON CRT (CZ_ADVANCED_STATUS_CHANGE 0x0b24)
            _traitToggle = UiKit.Button(p, Lang.T("Traits"), new Vector2(W / 2 - 95, top - 35), new Vector2(150, 48), () => { ShowTraits = !ShowTraits; _dirty = true; }, 22);
            for (int i = 0; i < 6; i++)
            {
                float y = top - 100 - i * 66;
                UiKit.Box(p, "row", new Vector2(0, y), new Vector2(W - 50, 60), new Color(1, 1, 1, 0.04f));
                _statLabels[i] = UiKit.Label(p, "", new Vector2(-60, y), new Vector2(W - 190, 58), 26, TextAnchor.MiddleLeft);
                int idx = i;
                _statPlus[i] = UiKit.Button(p, "+", new Vector2(W / 2 - 70, y), new Vector2(90, 54), () => StatPlus(idx), 32);
                _statPlus[i].Tip = () => Lang.T("Raise this stat"); // client wave 14 (w14menu), point 7
            }
            _derived = UiKit.Label(p, "", new Vector2(0, -top + 110), new Vector2(W - 70, 210), 23, TextAnchor.UpperLeft, UiKit.Dim);
        }

        /// <summary>Stats tab: basic stats, or the 4th-job traits (toggle, shown when the character has any).</summary>
        public bool ShowTraits;
        private UiButton _traitToggle;
        /// <summary>The "+" of stat row <paramref name="idx"/> (public for the smoke): 0x00bb for STR…LUK, 0x0b24 for POW…CRT.</summary>
        public void StatPlus(int idx)
        {
            if (ShowTraits)
            {
                if (_prog.TraitCost[idx] > 0 && _prog.TraitPoints >= _prog.TraitCost[idx]) { _s.TraitUp(Progression.SP_POW + idx, 1); Debug.Log($"[Stats] raise {Progression.TraitNames[idx]}"); }
                else Toast(Lang.T("Not enough trait points."));
                return;
            }
            if (_prog.Cost[idx] > 0 && _prog.StatusPoints >= _prog.Cost[idx]) _s.StatusUp(idx, 1); else Toast(Lang.T("Not enough status points."));
        }

        private void BuildSkills(RectTransform page)
        {
            float top = PH / 2;
            // client wave 3 (w3skills): the page is a skill window — header, tier tabs, tree grid, detail (WristMenu.Skills.cs);
            // the flat list below is its "Quick" tab
            BuildSkillWindow(page);
            var p = _skQuick;
            // Warp Portal memo (the official /memo, CZ_REMEMBER_WARPPOINT 0x011d): only offered with Warp Portal learned
            _memoBtn = UiKit.Button(page, Lang.T("Memo here"), new Vector2(W / 2 - 100, top - 24), new Vector2(170, 42), () => Memo?.Invoke(), 20);
            for (int i = 0; i < SkillsPerPage; i++)
            {
                float y = top - 140 - i * 90;
                UiKit.Box(p, "row", new Vector2(0, y), new Vector2(W - 50, 84), new Color(1, 1, 1, 0.04f));
                var icon = UiKit.Icon(p, new Vector2(-W / 2 + 55, y), new Vector2(44, 44));
                var label = UiKit.Label(p, "", new Vector2(-110, y), new Vector2(200, 82), 20, TextAnchor.MiddleLeft);
                int slot = i;
                // "Lv n": the level the next cast uses (steps down, wraps to the highest) — the official skill window's level choice
                var lvb = UiKit.Button(p, "Lv", new Vector2(22, y), new Vector2(60, 60), () => SkillLevelStep(slot), 19);
                var pin = UiKit.Button(p, "★", new Vector2(85, y), new Vector2(50, 60), () => PinSkill(slot), 26);
                var up = UiKit.Button(p, "+", new Vector2(145, y), new Vector2(60, 60), () => SkillAction(slot, true), 30);
                up.Tip = () => Lang.T("Raise this skill"); // client wave 14 (w14menu), point 7
                var use = UiKit.Button(p, Lang.T("Use"), new Vector2(W / 2 - 85, y), new Vector2(110, 60), () => SkillAction(slot, false), 24);
                _skillRows.Add((icon, label, up, use, pin));
                _skillLvButtons.Add(lvb);
            }
            UiKit.Button(p, "<", new Vector2(-W / 2 + 70, -top + 40), new Vector2(100, 56), () => { _skillPageIdx = Mathf.Max(0, _skillPageIdx - 1); _dirty = true; }, 28);
            UiKit.Button(p, ">", new Vector2(W / 2 - 70, -top + 40), new Vector2(100, 56), () => { _skillPageIdx++; _dirty = true; }, 28);
            _skillPage = UiKit.Label(p, "", new Vector2(0, -top + 40), new Vector2(300, 50), 24, TextAnchor.MiddleCenter, UiKit.Dim);
        }

        private List<Progression.SkillEntry> SortedSkills() =>
            _prog.Skills.Values.OrderBy(k => k.Passive ? 1 : 0).ThenByDescending(k => k.Level > 0).ThenBy(k => k.Id).ToList();
        private readonly List<UiButton> _skillLvButtons = new List<UiButton>();
        /// <summary>Skills tab "Memo here" (shown with Warp Portal, skill 27) and Status tab "Stop attacking" (shown while attacking).</summary>
        public System.Action Memo, StopAttack; public System.Func<bool> Attacking;
        private UiButton _memoBtn, _stopAttackBtn;

        /// <summary>Steps the cast level of the skill in row <paramref name="slot"/> down by one (wraps to the learned level).</summary>
        private void SkillLevelStep(int slot)
        {
            var list = SortedSkills();
            int i = _skillPageIdx * SkillsPerPage + slot;
            if (i >= list.Count) return;
            var k = list[i];
            int lv = _prog.CycleLevel(k);
            Toast(Lang.F("{0}: cast at level {1} of {2}.", k.Info.Name, lv, k.Level));
            _dirty = true;
        }

        private void PinSkill(int slot)
        {
            var list = SortedSkills();
            int i = _skillPageIdx * SkillsPerPage + slot;
            if (i >= list.Count || Hotkeys == null) return;
            var k = list[i];
            OpenSlotPicker(true, k.Id, _prog.CastLevel(k)); // client wave 11 (w11menu): the player chooses the slot (WristMenu.W11.cs)
        }

        /// <summary>Quick-bar use: an item by id (first stack in the inventory) or a known skill.</summary>
        public void QuickUse(int slot) { if (Hotkeys != null) QuickUseAt(Hotkeys.CurrentRow, slot); }

        /// <summary>The same for a slot of any row (the radial wheels show two rows: LokiGame.Wheels.cs).</summary>
        public void QuickUseAt(int row, int slot)
        {
            if (Hotkeys == null) return;
            var h = Hotkeys.At(row, slot);
            if (h.Empty) return;
            if (h.IsSkill)
            {
                if (_prog.Skills.TryGetValue(h.Id, out var k) && k.Level > 0 && !k.Passive) _useSkill?.Invoke(k);
                else Toast(Lang.T("You don't have that skill any more."));
                return;
            }
            UseItemById(h.Id);
        }

        /// <summary>Uses (equips, takes off …) the first stack of an item by its id, as a quick slot does; says so when there is none.</summary>
        public bool UseItemById(int itemId)
        {
            foreach (var e in _inv.Items.Values) if (e.ItemId == itemId) { ItemActionOn(e.Index); return true; }
            Toast(Lang.F("No {0} in your inventory.", GameDb.GetItem(itemId).Name));
            return false;
        }

        private void BuildQuick(RectTransform p)
        {
            float top = PH / 2;
            var quickHead = UiKit.Label(p, Lang.T("Quick bar  <color=#9aa3b5>(★ pins here; saved on the server)</color>"), new Vector2(-135, top - 30), new Vector2(W - 340, 40), 20, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            quickHead.resizeTextForBestFit = true; quickHead.resizeTextMinSize = 14; quickHead.resizeTextMaxSize = quickHead.fontSize; // w4lang: two lines fit (the heading was cut in English too since w4vr narrowed it)
            BuildW4VrQuick(p, top); // client wave 4 (w4vr): the row switcher (WristMenu.W4Vr.cs)
            for (int i = 0; i < Hotkeys.QuickSlots; i++)
            {
                float y = top - 85 - i * 64;
                UiKit.Box(p, "row", new Vector2(0, y), new Vector2(W - 50, 62), new Color(1, 1, 1, 0.04f));
                UiKit.Label(p, (i + 1).ToString(), new Vector2(-W / 2 + 40, y), new Vector2(30, 40), 22, TextAnchor.MiddleCenter, UiKit.Dim);
                var icon = UiKit.Icon(p, new Vector2(-W / 2 + 85, y), new Vector2(44, 44));
                var label = UiKit.Label(p, "", new Vector2(-40, y), new Vector2(W - 350, 60), 21, TextAnchor.MiddleLeft);
                int slot = i;
                var use = UiKit.Button(p, Lang.T("Use"), new Vector2(W / 2 - 120, y), new Vector2(120, 54), () => QuickUse(slot), 22);
                var clear = UiKit.Button(p, "x", new Vector2(W / 2 - 40, y), new Vector2(50, 54), () => { Hotkeys?.Clear(slot); _dirty = true; }, 22);
                _quickRows.Add((icon, label, use, clear));
            }
            // the equipment swap set (ALL_EQSWITCH): swap everything at once, or open the set
            // client wave 14 (w14cast): two buttons again: wave 12's "Item buttons…" is gone with the item buttons (the design's answer 5)
            float bw = (W - 100) / 2f;
            _swapAllBtn = UiKit.Button(p, Lang.T("Swap gear"), new Vector2(-bw / 2 - 5, -top + 34), new Vector2(bw, 52), () => { Swap?.SwitchAll(); _dirty = true; }, 21);
            UiKit.Button(p, Lang.T("Swap set…"), new Vector2(bw / 2 + 5, -top + 34), new Vector2(bw, 52), () => Swap?.Show(), 21);

        }

        private void BuildSocial(RectTransform p) => BuildW4SocialFriends(p); // client wave 4 (w4social): the friend list complete (WristMenu.W4Social.cs)

        private void BuildParty(RectTransform p)
        {
            float top = PH / 2;
            _partyTitle = UiKit.Label(p, Lang.T("Party"), new Vector2(0, top - 30), new Vector2(W - 70, 40), 28, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            _partyInvite = UiKit.Label(p, "", new Vector2(-70, top - 80), new Vector2(W - 260, 50), 21, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.5f));
            _pAccept = UiKit.Button(p, Lang.T("Accept"), new Vector2(W / 2 - 165, top - 80), new Vector2(120, 48), () => { Party?.Answer(true); _dirty = true; }, 20);
            _pDecline = UiKit.Button(p, Lang.T("Decline"), new Vector2(W / 2 - 45, top - 80), new Vector2(110, 48), () => { Party?.Answer(false); _dirty = true; }, 20);
            // no party: create / invite by name / refuse toggle
            _partyNoParty = UiKit.Box(p, "noparty", Vector2.zero, new Vector2(W - 30, PH), new Color(0, 0, 0, 0));
            UiKit.Label(_partyNoParty, Lang.T("You are not in a party."), new Vector2(0, top - 130), new Vector2(W - 70, 40), 24, TextAnchor.MiddleLeft, UiKit.Dim);
            UiKit.Label(_partyNoParty, Lang.T("Create one"), new Vector2(0, top - 180), new Vector2(W - 70, 36), 24, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            _partyName = VrTextField.Create(_partyNoParty, new Vector2(-80, top - 230), new Vector2(W - 270, 56), 24);
            _partyName.Placeholder = Lang.T("party name"); _partyName.OnFocus = f => RequestKeyboard?.Invoke(f); _partyName.OnSubmit = () => { Party?.Create(_partyName.Text); };
            UiKit.Button(_partyNoParty, Lang.T("Create"), new Vector2(W / 2 - 100, top - 230), new Vector2(150, 54), () => { Party?.Create(_partyName.Text); }, 22);
            _pRefuse = UiKit.Button(_partyNoParty, "", new Vector2(0, top - 310), new Vector2(W - 90, 54), () => { Party?.SetRefuseInvites(!Party.RefuseInvites); }, 22);
            // in a party: members, options, invite, leave
            _partyIn = UiKit.Box(p, "inparty", Vector2.zero, new Vector2(W - 30, PH), new Color(0, 0, 0, 0));
            _partyOptions = UiKit.Label(_partyIn, "", new Vector2(0, top - 125), new Vector2(W - 70, 40), 20, TextAnchor.MiddleLeft, UiKit.Dim);
            _partyOptions.resizeTextForBestFit = true; _partyOptions.resizeTextMinSize = 14; _partyOptions.resizeTextMaxSize = _partyOptions.fontSize; // w4lang
            _pExp = UiKit.Button(_partyIn, Lang.T("EXP share"), new Vector2(W / 2 - 105, top - 30), new Vector2(170, 46), () => { if (Party != null) Party.SetOptions(!Party.ExpShare, Party.ItemPickup, Party.ItemDivision); }, 20);
            BuildW3SocialParty(_partyIn); // client wave 3 (w3social): all 12 members, the picked member's actions, invite (WristMenu.W3Social.cs)
        }

        private void BuildGuild(RectTransform p)
        {
            float top = PH / 2;
            _guildTitle = UiKit.Label(p, Lang.T("Guild"), new Vector2(0, top - 30), new Vector2(W - 70, 40), 28, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            _guildInvite = UiKit.Label(p, "", new Vector2(-70, top - 80), new Vector2(W - 260, 50), 21, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.5f));
            // one pair of buttons answers whichever prompt is pending: a guild invitation first, then an alliance request
            _gAccept = UiKit.Button(p, Lang.T("Accept"), new Vector2(W / 2 - 165, top - 80), new Vector2(120, 48), () => { if (Guild == null) return; if (Guild.Invite.HasValue) Guild.Answer(true); else Guild.AnswerAlly(true); _dirty = true; }, 20);
            _gDecline = UiKit.Button(p, Lang.T("Decline"), new Vector2(W / 2 - 45, top - 80), new Vector2(110, 48), () => { if (Guild == null) return; if (Guild.Invite.HasValue) Guild.Answer(false); else Guild.AnswerAlly(false); _dirty = true; }, 20);
            _guildNone = UiKit.Box(p, "noguild", Vector2.zero, new Vector2(W - 30, PH), new Color(0, 0, 0, 0));
            UiKit.Label(_guildNone, Lang.T("You are not in a guild."), new Vector2(0, top - 130), new Vector2(W - 70, 40), 24, TextAnchor.MiddleLeft, UiKit.Dim);
            UiKit.Label(_guildNone, Lang.T("Found one (takes an Emperium)"), new Vector2(0, top - 180), new Vector2(W - 70, 36), 24, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            _guildName = VrTextField.Create(_guildNone, new Vector2(-80, top - 230), new Vector2(W - 270, 56), 24);
            _guildName.Placeholder = Lang.T("guild name"); _guildName.OnFocus = f => RequestKeyboard?.Invoke(f); _guildName.OnSubmit = () => { Guild?.Create(_guildName.Text); };
            UiKit.Button(_guildNone, Lang.T("Found"), new Vector2(W / 2 - 100, top - 230), new Vector2(150, 54), () => { Guild?.Create(_guildName.Text); }, 22);
            UiKit.Label(_guildNone, Lang.T("Or ask a guild member to invite you."), new Vector2(0, top - 300), new Vector2(W - 70, 36), 21, TextAnchor.MiddleLeft, UiKit.Dim);
            _guildIn = UiKit.Box(p, "inguild", Vector2.zero, new Vector2(W - 30, PH), new Color(0, 0, 0, 0));
            _guildInfo = UiKit.Label(_guildIn, "", new Vector2(0, top - 300), new Vector2(W - 70, 380), 21, TextAnchor.UpperLeft);
            UiKit.Button(_guildIn, Lang.T("Guild window"), new Vector2(-(W - 100) / 4 - 5, -top + 60), new Vector2((W - 100) / 2, 56), () => OpenGuild?.Invoke(), 21);
            UiKit.Button(_guildIn, Lang.T("Members"), new Vector2((W - 100) / 4 + 5, -top + 60), new Vector2((W - 100) / 2, 56), () => OpenGuildMembers?.Invoke(), 21);
        }
        /// <summary>Guild tab buttons: the guild window on its Info / Members page.</summary>
        public System.Action OpenGuild, OpenGuildMembers;
        /// <summary>Wave 28 (wcodex): the Codex book (LokiGame.WCodex.cs): the Quests group's tab, the classic Achievements page's button.</summary>
        public System.Action OpenCodex;
        private UiButton _codexClassic;

        // ---- companions: a summary of the pet, the homunculus and the mercenary, with the everyday commands; the rest is in the companion window
        private Text _cPet, _cHom, _cMerc;
        private UiButton _cPetFeed, _cPetPerform, _cHomFeed, _cHomAttack, _cHomFollow, _cMercAttack, _cMercFollow;
        private Text SimplePage(RectTransform p, string title, params (string label, System.Action a)[] buttons)
        {
            float top = PH / 2;
            UiKit.Label(p, title, new Vector2(0, top - 30), new Vector2(W - 70, 40), 28, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            var info = UiKit.Label(p, "", new Vector2(0, top - 250), new Vector2(W - 70, 400), 22, TextAnchor.UpperLeft);
            for (int i = 0; i < buttons.Length; i++)
            {
                var (label, a) = buttons[i];
                UiKit.Button(p, label, new Vector2(0, -top + 60 + (buttons.Length - 1 - i) * 72), new Vector2(W - 90, 62), () => { a?.Invoke(); _dirty = true; }, 24);
            }
            return info;
        }

        private void BuildItem9Pages()
        {
            _mailInfo = SimplePage(_pages[Tab.Mail], Lang.T("Mail"), (Lang.T("Open the mailbox"), () => Mail?.Show(Mail.Page.Inbox)), (Lang.T("Write a mail"), () => Mail?.Show(Mail.Page.Write)));
            _achInfo = SimplePage(_pages[Tab.Achievements], Lang.T("Achievements"), (Lang.T("Achievements"), () => Achievements?.Show()), (Lang.T("Titles"), () => Achievements?.Show(Achievements.Page.Titles)), (Lang.T("Daily check-in"), () => CheckIn?.Open()), ("Codex", () => OpenCodex?.Invoke()));
            foreach (var b in _pages[Tab.Achievements].GetComponentsInChildren<UiButton>(true)) if (b.name == "btn:Codex") _codexClassic = b; // wave 28 (wcodex): the classic wrist menu's way to the Codex
            _bankInfo = SimplePage(_pages[Tab.Bank], Lang.T("Bank"), (Lang.T("Open the bank"), () => Bank?.Show()));
            _cashInfo = SimplePage(_pages[Tab.CashShop], Lang.T("Cash shop"), (Lang.T("Open the cash shop"), () => CashShop?.Show()));
        }

        private void BuildCompanions(RectTransform p)
        {
            float top = PH / 2, bw = (W - 110) / 4f;
            UiKit.Label(p, Lang.T("Companions"), new Vector2(0, top - 30), new Vector2(W - 70, 40), 28, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            float x(int i) => -(W - 70) / 2 + bw * (i + 0.5f) + i * 10;
            float s0 = top - 70;
            _cPet = UiKit.Label(p, "", new Vector2(0, s0 - 55), new Vector2(W - 70, 110), 21, TextAnchor.UpperLeft);
            _cPetFeed = UiKit.Button(p, Lang.T("Feed"), new Vector2(x(0), s0 - 145), new Vector2(bw, 50), () => Companions?.PetCommand(1), 20);
            _cPetPerform = UiKit.Button(p, Lang.T("Perform"), new Vector2(x(1), s0 - 145), new Vector2(bw, 50), () => Companions?.PetCommand(2), 20);
            UiKit.Button(p, Lang.T("Pet"), new Vector2(x(3), s0 - 145), new Vector2(bw, 50), () => Companions?.Show(Companions.Page.Pet), 20);
            float s1 = s0 - 210;
            _cHom = UiKit.Label(p, "", new Vector2(0, s1 - 55), new Vector2(W - 70, 110), 21, TextAnchor.UpperLeft);
            _cHomFeed = UiKit.Button(p, Lang.T("Feed"), new Vector2(x(0), s1 - 145), new Vector2(bw, 50), () => Companions?.FeedHomunculus(), 20);
            _cHomAttack = UiKit.Button(p, Lang.T("Attack"), new Vector2(x(1), s1 - 145), new Vector2(bw, 50), () => Companions?.AttackGazed(), 20);
            _cHomFollow = UiKit.Button(p, Lang.T("~companion~Follow"), new Vector2(x(2), s1 - 145), new Vector2(bw, 50), () => Companions?.Recall(), 20);
            UiKit.Button(p, Lang.T("Homun."), new Vector2(x(3), s1 - 145), new Vector2(bw, 50), () => Companions?.Show(Companions.Page.Homunculus), 20);
            float s2 = s1 - 210;
            _cMerc = UiKit.Label(p, "", new Vector2(0, s2 - 55), new Vector2(W - 70, 110), 21, TextAnchor.UpperLeft);
            _cMercAttack = UiKit.Button(p, Lang.T("Attack"), new Vector2(x(1), s2 - 145), new Vector2(bw, 50), () => Companions?.AttackGazed(), 20);
            _cMercFollow = UiKit.Button(p, Lang.T("~companion~Follow"), new Vector2(x(2), s2 - 145), new Vector2(bw, 50), () => Companions?.Recall(), 20);
            UiKit.Button(p, Lang.T("Merc."), new Vector2(x(3), s2 - 145), new Vector2(bw, 50), () => Companions?.Show(Companions.Page.Mercenary), 20);
        }

        /// <summary>"Prontera City, Capitol of Rune-Midgarts" → "Prontera City": the member rows have no room for the full name.</summary>
        private static string ShortMapName(string map)
        {
            string n = ClientText.MapName(map) ?? map ?? "";
            int cut = n.IndexOfAny(new[] { ',', '(' }); if (cut > 0) n = n.Substring(0, cut).Trim();
            return n.Length > 18 ? n.Substring(0, 17) + "…" : n;
        }
        private List<Party.Member> SortedMembers()
        {
            var l = Party != null ? new List<Party.Member>(Party.Members.Values) : new List<Party.Member>();
            l.Sort((a, b) => a.Leader != b.Leader ? (a.Leader ? -1 : 1) : string.CompareOrdinal(a.Name, b.Name));
            return l;
        }

        private void BuildEmotes(RectTransform p) => BuildW3SocialEmotes(p); // client wave 3 (w3social): all 87 emotes, paged, with their pictures (WristMenu.W3Social.cs)

        private void SkillAction(int slot, bool levelUp)
        {
            var list = SortedSkills();
            int i = _skillPageIdx * SkillsPerPage + slot;
            if (i >= list.Count) return;
            var k = list[i];
            if (levelUp) { if (_ps.SkillPoint > 0 && k.Upgradable) _s.SkillUp(k.Id); else Toast(_ps.SkillPoint <= 0 ? Lang.T("No skill points left.") : Lang.T("Can't raise that skill yet.")); }
            else if (!k.Passive && k.Level > 0) _useSkill?.Invoke(k);
        }

        /// <summary>Client wave 14 (w14ui), point 26: the View page's "Settings…" and "Leave the game…" (the board shows them otherwise: WristMenu.Board.cs HostPages).</summary>
        private UiButton _viewSettings, _viewLeave;
        private void BuildView(RectTransform p)
        {
            float top = PH / 2;
            UiKit.Label(p, Lang.T("Camera"), new Vector2(0, top - 30), new Vector2(W - 70, 40), 28, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            string[] modes = { Lang.T("Table"), Lang.T("~view~Follow"), Lang.T("First person") };
            for (int i = 0; i < 3; i++)
            {
                int m = i;
                _modeButtons[i] = UiKit.Button(p, modes[i], new Vector2(0, top - 95 - i * 72), new Vector2(W - 90, 62), () => _view?.Choose((VR.ViewController.ViewMode)m), 28); // (client wave 19, w19view41, point 55: a choice, kept)
            }
            _mapButton = UiKit.Button(p, Lang.T("Minimap: off"), new Vector2(0, top - 330), new Vector2(W - 90, 62), () => { if (ToggleMinimap != null) ToggleMinimap(); else _minimap?.Toggle(); _dirty = true; }, 28); // (client wave 13: the stored switch, as the overhead menu's)
            BuildW3SocialView(p, top); // client wave 3 (w3social): "World map" beside the minimap toggle (WristMenu.W3Social.cs)
            UiKit.Label(p, Lang.T("Music"), new Vector2(-W / 2 + 110, top - 410), new Vector2(160, 50), 26, TextAnchor.MiddleLeft);
            UiKit.Button(p, "-", new Vector2(40, top - 410), new Vector2(90, 56), () => { VR.Comfort.MusicVolume = Mathf.Clamp01(VR.Comfort.MusicVolume - 0.1f); VR.Comfort.Save(); _dirty = true; }, 30);
            UiKit.Button(p, "+", new Vector2(150, top - 410), new Vector2(90, 56), () => { VR.Comfort.MusicVolume = Mathf.Clamp01(VR.Comfort.MusicVolume + 0.1f); VR.Comfort.Save(); _dirty = true; }, 30);
            // client wave 14 (w14menu), point 9 (design note: "that button is being given a lot of emphasis when it is a minor thing. So you can move it
            // somewhere else that is less intrusive"; answer 7): the full-width "My costumes: shown / hidden" button that was here is Settings →
            // Graphics "Costume looks" now (UI/ComfortWindows.cs); "Settings…" and "Leave the game…" moved up into its row
            _viewSettings = UiKit.Button(p, Lang.T("Settings…"), new Vector2(-(W - 90) / 4 - 5, top - 490), new Vector2((W - 90) / 2 - 10, 62), () => OpenComfort?.Invoke(), 24); // client wave 4 (w4vr): the Settings window
            _viewLeave = UiKit.Button(p, Lang.T("Leave the game…"), new Vector2((W - 90) / 4 + 5, top - 490), new Vector2((W - 90) / 2 - 10, 62), () => OpenLeave?.Invoke(), 24);
            _viewInfo = UiKit.Label(p, "", new Vector2(0, -top + 62), new Vector2(W - 70, 110), 20, TextAnchor.UpperLeft, UiKit.Dim);
            _viewInfo.resizeTextForBestFit = true; _viewInfo.resizeTextMinSize = 15; _viewInfo.resizeTextMaxSize = _viewInfo.fontSize; // w4lang
        }

        private List<Inventory.Entry> SortedItems()
        {
            // filters as the official inventory tabs (Item / Equip / Etc) plus the favourite tab; favourites first in every other view
            IEnumerable<Inventory.Entry> q = _inv.Items.Values;
            switch (_itemFilter)
            {
                case 1: q = q.Where(e => !e.IsEquip && e.Info.IsConsumable && W4ItemsInTypeTab(e)); break; // W4ItemsInTypeTab: client wave 4, favourites only in their tab
                case 2: q = q.Where(e => e.IsEquip && W4ItemsInTypeTab(e)); break;
                case 3: q = q.Where(e => !e.IsEquip && !e.Info.IsConsumable && W4ItemsInTypeTab(e)); break;
                case 4: q = q.Where(e => e.Favorite); break;
            }
            var o = q.OrderByDescending(e => e.Wearing != 0).ThenByDescending(e => e.Favorite);
            if (_itemSort == 1) return o.ThenBy(e => e.Info.Name, System.StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Index).ToList();
            if (_itemSort == 2) return o.ThenByDescending(e => e.Info.Weight * e.Amount).ThenBy(e => e.Index).ToList();
            return o.ThenBy(e => e.IsEquip ? 1 : 0).ThenBy(e => e.Index).ToList();
        }

        private void ItemAction(int slot)
        {
            var list = SortedItems();
            int i = _itemPageIdx * ItemsPerPage + slot;
            if (i >= list.Count) return;
            ItemActionOn(list[i].Index);
        }

        /// <summary>Use / equip / take off / insert by client inventory index (Items tab, detail view and quick bar).</summary>
        public void ItemActionOn(int index)
        {
            if (!_inv.Items.TryGetValue(index, out var e)) return;
            if (e.Info.MinLevel > _ps.BaseLevel && e.Wearing == 0) { Toast(Lang.F("{0} needs base level {1} (you are {2}).", e.Info.Name, e.Info.MinLevel, _ps.BaseLevel)); return; }
            Debug.Log($"[Items] {(e.Wearing != 0 ? "take off" : e.IsEquip ? "equip" : "use")} {e.ItemId} {e.Info.English} (index {e.Index})");
            if (e.Wearing != 0) _s.Unequip(e.Index);
            else if (e.IsEquip) _s.Equip(e.Index, e.Location);
            else if (e.IsCard) { OnUseCard?.Invoke(e); Toast(Lang.T("Choose the gear in the window that opens.")); }
            else if (e.Info.IsConsumable) _s.UseItem(e.Index);
        }

        public void MarkDirty() => _dirty = true;
        private bool _hotkeysHooked;
        public bool DetailOpen => _detailIndex >= 0;
        private float _tick;

        private static readonly Dictionary<int, Sprite> _skillIcons = new Dictionary<int, Sprite>();
        /// <summary>Skill icons live next to item icons: data\texture\유저인터페이스\item\&lt;aegis name, lower case&gt;.bmp.</summary>
        public static Sprite SkillIcon(SkillDb.Skill info)
        {
            if (_skillIcons.TryGetValue(info.Id, out var s)) return s;
            s = null;
            var a = LokiBootstrap.Instance != null ? LokiBootstrap.Instance.Assets : null;
            if (a != null && !string.IsNullOrEmpty(info.Aegis))
            {
                var tex = a.Texture("\u00c0\u00af\u00c0\u00fa\u00c0\u00ce\u00c5\u00cd\u00c6\u00e4\u00c0\u00cc\u00bd\u00ba\\item\\" + info.Aegis.ToLowerInvariant() + ".bmp", false);
                if (tex != a.MissingTexture) s = UiKit.ToSprite(tex);
            }
            _skillIcons[info.Id] = s;
            return s;
        }
        public void SetSitting(bool s) { _sitting = s; _dirty = true; }

        /// <summary>Re-attach to another hand (left-handed mode).</summary>
        public void SetHand(Transform hand) { _hand = hand; if (_ring != null) _ring.SetHand(hand); }

        private void LateUpdate()
        {
            if (_onBoard) BoardTick(); // client wave 11 (w11menu): New style: the pages are on a board in front of the player, which stays where it is
            else if (_hand != null && _head != null)
            {
                // Hover above the back of the left hand, slightly towards the head, facing it.
                var toHead = _head.transform.position - _hand.position; toHead.y = 0; if (toHead.sqrMagnitude > 1e-4f) toHead.Normalize();
                transform.position = _hand.position + Vector3.up * ClassicUp + toHead * 0.03f; // client wave 11: 25 % larger, 25 % higher (0.2 m before)
                UiKit.FaceHead(transform, _head);
            }
            if (_toast != null && _toast.text.Length > 0 && Time.time > _toastUntil) _toast.text = "";
            if (_tab == Tab.Skills || _tab == Tab.Status || CharWide) { _tick += Time.deltaTime; if (_tick > 1f) { _tick = 0; _dirty = true; } } // cooldown / effect timers (client wave 20: Status on the wide Character board)
            if (Hotkeys != null && !_hotkeysHooked) { _hotkeysHooked = true; Hotkeys.Changed += () => _dirty = true; }
            if (Social != null && !_socialHooked) { _socialHooked = true; Social.Changed += () => _dirty = true; }
            if (Party != null && !_partyHooked) { _partyHooked = true; Party.Changed += () => _dirty = true; }
            if (Guild != null && !_guildHooked) { _guildHooked = true; Guild.Changed += () => _dirty = true; }
            if (Companions != null && !_compHooked) { _compHooked = true; Companions.Changed += () => _dirty = true; }
            if (!_item9Hooked && Mail != null && Achievements != null && Bank != null && CashShop != null && Swap != null)
            {
                _item9Hooked = true; Mail.Changed += () => _dirty = true; Achievements.Changed += () => _dirty = true; Bank.Changed += () => _dirty = true;
                CashShop.Changed += () => _dirty = true; Swap.Changed += () => _dirty = true; if (CheckIn != null) CheckIn.Changed += () => _dirty = true;
            }
            if (_tab == Tab.Quick && Swap != null && Swap.CooldownUntil > Time.time) { _tick += Time.deltaTime; if (_tick > 1f) { _tick = 0; _dirty = true; } }
            if (_tab == Tab.Companions) { _tick += Time.deltaTime; if (_tick > 1f) { _tick = 0; _dirty = true; } } // contract countdown, capture mode
            if (_tab == Tab.Social) { _tick += Time.deltaTime; if (_tick > 0.5f) { _tick = 0; _dirty = true; } } // gaze target label
            if (_tab >= Tab.Community) { _tick += Time.deltaTime; if (_tick > 0.5f) { _tick = 0; _dirty = true; } } // wave-2 pages: timers, gaze target, queue state
            if (_tab == Tab.Emotes) W3SocialTick(); // client wave 3 (w3social): the palette's "next in … s" (WristMenu.W3Social.cs)
            W4VrQuickTick(); // client wave 4 (w4vr): quick-tab cooldown seconds (WristMenu.W4Vr.cs)
            SkillHoverTick(Time.unscaledDeltaTime); // hover-only prerequisite lines and animated dotted lines on the flat skill tree
            if (_dirty) { _dirty = false; Refresh(); }
        }

        private void Refresh()
        {
            InvSync(); // client wave 21 (w21inv), point 62: the new inventory in place of the board's own picture, or the board back (WristMenu.Inv.cs)
            if (W11PickerOpen) { RefreshSlotPicker(); return; } // client wave 11 (w11menu): the quick-slot picker covers the page
            RefreshPage();
            W11AfterRefresh(); // client wave 11 (w11menu): the compare cards beside the item details
        }

        private void RefreshPage()
        {
            // client wave 20 (w20char), point 52: the wide Character board draws its three pages every time (UI/WristMenu.Board.cs)
            // (its third column is the equipment, or the details of an item opened from it, whatever page the ring's entry named)
            if (CharWide)
            {
                RefreshPageOf(Tab.Status); RefreshPageOf(Tab.Stats);
                if (_detailIndex >= 0) RefreshPageOf(Tab.Items);
                else { if (_grid != null && _grid.gameObject.activeSelf) _grid.gameObject.SetActive(false); RefreshW3Equip(); }
                return;
            }
            RefreshPageOf(_tab);
        }

        private void RefreshPageOf(Tab tab)
        {
            // client wave 11 (w11menu): on the board the bag is a grid of icons (UI/WristMenu.Board.cs); the list below is Classic's
            if (tab == Tab.Items && _invOn) { RefreshInv(); return; } // client wave 21 (w21inv), point 62: the new inventory (WristMenu.Inv.cs)
            if (tab == Tab.Items) { if (GridWanted) { RefreshGrid(); return; } if (_grid != null && _grid.gameObject.activeSelf) _grid.gameObject.SetActive(false); }
            switch (tab)
            {
                case Tab.Status:
                {
                    _stName.text = _charName?.Invoke() ?? "";
                    _stLevels.text = _ps.StatusPoint > 0 ? Lang.F("Base Lv {0}   |   Job Lv {1}   |   <color=#ffd060>{2} stat pts</color>", _ps.BaseLevel, _ps.JobLevel, _ps.StatusPoint) : Lang.F("Base Lv {0}   |   Job Lv {1}", _ps.BaseLevel, _ps.JobLevel);
                    SetFill(_hpFill, _ps.Hp / (float)_ps.MaxHp); _stHp.text = $"HP  {_ps.Hp} / {_ps.MaxHp}";
                    if (_hpShield != null)
                    {
                        bool showShield = _ps.ShieldFraction > 0.001f;
                        if (_hpShield.gameObject.activeSelf != showShield) _hpShield.gameObject.SetActive(showShield);
                        if (showShield) SetFill(_hpShield, _ps.ShieldFraction);
                    }
                    SetFill(_spFill, _ps.Sp / (float)_ps.MaxSp); _stSp.text = $"SP  {_ps.Sp} / {_ps.MaxSp}";
                    float be = _ps.BaseExp / (float)_ps.NextBaseExp, je = _ps.JobExp / (float)_ps.NextJobExp;
                    SetFill(_bexpFill, be); _stExp.text = Lang.F("Base EXP  {0:F1}%", be * 100f);
                    SetFill(_jexpFill, je); _stJexp.text = Lang.F("Job EXP  {0:F1}%", je * 100f);
                    var fx = new System.Text.StringBuilder();
                    foreach (var ef in _prog.Effects.Values)
                    {
                        if (!ef.Listed) continue;
                        if (fx.Length > 0) fx.Append(", ");
                        fx.Append(ef.Kind == "debuff" ? "<color=#ff8a7a>" : "<color=#9fe39a>").Append(ef.Name);
                        if (ef.Timed) fx.Append(" ").Append(Mathf.Max(0, Mathf.CeilToInt(ef.EndsAt - Time.time))).Append("s");
                        fx.Append("</color>");
                    }
                    // each "label  value" is its own unit (w4lang)
                    _stMisc.text = Lang.F("Zeny  {0:N0}", _ps.Zeny) + "\n" + Lang.F("Weight  {0} / {1}", _ps.Weight / 10, _ps.MaxWeight / 10) + (_ps.SkillPoint > 0 ? "   " + Lang.F("Skill points  {0}", _ps.SkillPoint) : "")
                                 + (_prog.MaxAp > 0 ? $"   AP  {_prog.Ap} / {_prog.MaxAp}" : "") + (_prog.TraitPoints > 0 ? "   " + Lang.F("<color=#ffd060>{0} trait pts</color>", _prog.TraitPoints) : "") + (fx.Length > 0 ? "\n" + fx : "")
                                 + (StatusExtra != null && StatusExtra() is string extra && extra.Length > 0 ? "\n" + extra : "");
                    _sit.SetLabel(_sitting ? Lang.T("Stand up") : Lang.T("Sit down"));
                    bool attacking = Attacking != null && Attacking();
                    _stopAttackBtn.Interactable = attacking; _stopAttackBtn.Normal = attacking ? UiKit.ButtonBg : new Color(0.28f, 0.28f, 0.3f); _stopAttackBtn.SetHover(false);
                    break;
                }
                case Tab.Stats:
                {
                    bool traits = _prog.HasTraits;
                    _traitToggle.gameObject.SetActive(traits);
                    if (!traits) ShowTraits = false;
                    _traitToggle.SetLabel(ShowTraits ? Lang.T("Stats") : Lang.T("Traits"));
                    if (ShowTraits)
                    {
                        _statPoints.text = Lang.F("Trait points: {0}", _prog.TraitPoints);
                        for (int i = 0; i < 6; i++)
                        {
                            string bonus = _prog.TraitBonus[i] != 0 ? $" <color=#9fe39a>+{_prog.TraitBonus[i]}</color>" : "";
                            _statLabels[i].text = Lang.F("<b>{0}</b>   {1}{2}      <color=#9aa3b5>next +1 costs {3}</color>", Progression.TraitNames[i], _prog.TraitBase[i], bonus, _prog.TraitCost[i] > 0 ? _prog.TraitCost[i].ToString() : "-");
                            bool can = _prog.TraitCost[i] > 0 && _prog.TraitPoints >= _prog.TraitCost[i];
                            _statPlus[i].Normal = can ? UiKit.ButtonBg : new Color(0.3f, 0.3f, 0.32f); _statPlus[i].SetHover(false);
                        }
                        _derived.text = $"P.ATK  {_prog.PAtk}        S.MATK  {_prog.SMatk}\n" +
                                        $"RES  {_prog.Res}        MRES  {_prog.MRes}\n" +
                                        $"H.PLUS  {_prog.HPlus}        C.RATE  {_prog.CRate}\n" +
                                        $"AP  {_prog.Ap} / {_prog.MaxAp}";
                        break;
                    }
                    _statPoints.text = Lang.F("Status points: {0}", _prog.StatusPoints);
                    for (int i = 0; i < 6; i++)
                    {
                        string bonus = _prog.Bonus[i] != 0 ? $" <color=#9fe39a>+{_prog.Bonus[i]}</color>" : "";
                        _statLabels[i].text = Lang.F("<b>{0}</b>   {1}{2}      <color=#9aa3b5>next +1 costs {3}</color>", StatName(i), _prog.Base[i], bonus, _prog.Cost[i] > 0 ? _prog.Cost[i].ToString() : "-");
                        bool can = _prog.Cost[i] > 0 && _prog.StatusPoints >= _prog.Cost[i];
                        _statPlus[i].Normal = can ? UiKit.ButtonBg : new Color(0.3f, 0.3f, 0.32f); _statPlus[i].SetHover(false);
                    }
                    _derived.text = Lang.F("ATK  {0} + {1}        MATK  {2} ~ {3}\nDEF  {4} + {5}        MDEF  {6} + {7}\nHIT  {8}        FLEE  {9} + {10}\nCRIT  {11}        ASPD  {12}        Range  {13}",
                                    _prog.Atk1, _prog.Atk2, _prog.Matk2, _prog.Matk1, _prog.Def1, _prog.Def2, _prog.Mdef1, _prog.Mdef2, _prog.Hit, _prog.Flee1, _prog.Flee2, _prog.Crit, _prog.AspdDisplay, _prog.AttackRange);
                    break;
                }
                case Tab.Skills:
                {
                    if (RefreshSkillWindow()) break; // tree / detail (WristMenu.Skills.cs); the Quick tab continues here
                    var list = SortedSkills();
                    int pages = Mathf.Max(1, (list.Count + SkillsPerPage - 1) / SkillsPerPage);
                    _skillPageIdx = Mathf.Clamp(_skillPageIdx, 0, pages - 1);
                    _skillPage.text = list.Count == 0 ? Lang.T("no skills") : $"{_skillPageIdx + 1} / {pages}";
                    for (int r = 0; r < SkillsPerPage; r++)
                    {
                        int i = _skillPageIdx * SkillsPerPage + r;
                        var (icon, label, up, use, pin) = _skillRows[r];
                        var lvb = _skillLvButtons[r];
                        if (i >= list.Count) { icon.enabled = false; label.text = ""; up.gameObject.SetActive(false); use.gameObject.SetActive(false); pin.gameObject.SetActive(false); lvb.gameObject.SetActive(false); continue; }
                        var k = list[i]; var info = k.Info;
                        icon.sprite = SkillIcon(info); icon.enabled = icon.sprite != null;
                        string kind = k.Passive ? "<color=#9aa3b5>" + Lang.T("passive") + "</color>" : k.Ground ? Lang.T("ground") : k.SelfCast ? Lang.T("self") : k.Support ? Lang.T("support") : Lang.T("~kind~attack");
                        float cd = k.CooldownUntil - Time.time;
                        int castLv = _prog.CastLevel(k);
                        // SP of the chosen level from the client's skill table (skills.tsv); the server's figure is for the learned level
                        int sp = castLv < k.Level && info.Sp != null && castLv - 1 < info.Sp.Length && castLv >= 1 ? info.Sp[castLv - 1] : k.Sp;
                        label.text = $"<b>{info.Name}</b>  Lv {k.Level}/{Mathf.Max(info.MaxLevel, k.Level)}\n<color=#9aa3b5>{kind}{(k.Passive ? "" : $"  ·  SP {sp}")}{(cd > 0 ? $"  ·  {cd:F0}s" : "")}</color>";
                        up.gameObject.SetActive(k.Upgradable && _ps.SkillPoint > 0);
                        use.gameObject.SetActive(!k.Passive && k.Level > 0);
                        pin.gameObject.SetActive(!k.Passive && k.Level > 0 && Hotkeys != null);
                        lvb.gameObject.SetActive(!k.Passive && k.Level > 1);
                        lvb.SetLabel("Lv " + castLv); lvb.Normal = castLv < k.Level ? new Color(0.45f, 0.35f, 0.15f) : UiKit.ButtonBg; lvb.SetHover(false);
                    }
                    break;
                }
                case Tab.View:
                {
                    var mode = _view != null ? _view.Mode : VR.ViewController.ViewMode.Table;
                    for (int i = 0; i < 3; i++) { _modeButtons[i].Normal = (int)mode == i ? new Color(0.45f, 0.35f, 0.15f) : UiKit.ButtonBg; _modeButtons[i].SetHover(false); }
                    _mapButton.SetLabel(_minimap != null && _minimap.Visible ? Lang.T("Minimap: on") : Lang.T("Minimap: off"));
                    var a = Audio.LokiAudio.Instance;
                    _viewInfo.text = Lang.F("Music volume {0}%", a != null ? Mathf.RoundToInt(a.MusicVolume * 100) : 0) + "\n" +
                                     (VR.Comfort.LeftHanded ? Lang.T("Hold the right stick click: switch camera.\nRight stick: walk. Resize mode: grip + stick pans.\nLook at a target, pull the trigger.")
                                                            : Lang.T("Hold the left stick click: switch camera.\nLeft stick: walk. Resize mode: grip + stick pans.\nLook at a target, pull the trigger."));
                    break;
                }
                case Tab.Party:
                {
                    bool inParty = Party != null && Party.InParty;
                    _partyNoParty.gameObject.SetActive(!inParty); _partyIn.gameObject.SetActive(inParty);
                    _partyTitle.text = inParty ? Lang.F("Party  <color=#ffd060>{0}</color>  <color=#9aa3b5>{1}/12</color>", Party.Name, Party.Members.Count) : Lang.T("Party");
                    var inv = Party?.Invite;
                    _partyInvite.text = inv.HasValue ? Lang.F("Invited to '{0}'", inv.Value.name) : (Party != null && Party.LastInviteResult.Length > 0 ? Party.LastInviteResult : "");
                    _pAccept.gameObject.SetActive(inv.HasValue); _pDecline.gameObject.SetActive(inv.HasValue);
                    if (!inParty) { _pRefuse.SetLabel(Party != null && Party.RefuseInvites ? Lang.T("Invitations: refused (tap to allow)") : Lang.T("Invitations: allowed (tap to refuse)")); break; }
                    bool leader = Party.IsLeader;
                    _partyOptions.text = (Party.ExpShare ? Lang.T("EXP share <color=#7fd67f>on</color>") : Lang.T("EXP share <color=#ff8a7a>off</color>")) + "  ·  " +
                                         (Party.ItemDivision ? Lang.T("loot divided evenly") : Lang.T("loot to whoever picks it up")) + (Party.ItemPickup ? "  ·  " + Lang.T("shared pickup") : "");
                    _pExp.gameObject.SetActive(leader);
                    RefreshW3SocialParty(leader); // client wave 3 (w3social): the 12-member grid and the picked member's actions (WristMenu.W3Social.cs)
                    _pInviteRow.gameObject.SetActive(leader);
                    break;
                }
                case Tab.Guild:
                {
                    bool inGuild = Guild != null && Guild.InGuild;
                    _guildNone.gameObject.SetActive(!inGuild); _guildIn.gameObject.SetActive(inGuild);
                    _guildTitle.text = inGuild ? Lang.F("Guild  <color=#ffd060>{0}</color>  <color=#9aa3b5>Lv {1}</color>", Guild.Name, Guild.Level) : Lang.T("Guild");
                    var gi = Guild?.Invite; var ar = Guild?.AllyRequest;
                    _guildInvite.text = gi.HasValue ? Lang.F("Invited to '{0}'", gi.Value.name) : ar.HasValue ? Lang.F("'{0}' asks for an alliance", ar.Value.guild) : Guild != null ? Guild.LastResult : "";
                    bool prompt = gi.HasValue || (ar.HasValue && inGuild);
                    _gAccept.gameObject.SetActive(prompt); _gDecline.gameObject.SetActive(prompt);
                    if (!inGuild) break;
                    double pct = Guild.NextExp > 0 ? Guild.Exp * 100.0 / Guild.NextExp : 0;
                    string notice = Guild.NoticeSubject.Length + Guild.NoticeText.Length > 0 ? $"\n\n<color=#b8f0b8>{Guild.NoticeSubject}</color>\n{Guild.NoticeText}" : "";
                    _guildInfo.text = (Guild.AmMaster ? Lang.F("Master <color=#ffd060>{0}</color> (you)", Guild.MasterName) : Lang.F("Master <color=#ffd060>{0}</color>", Guild.MasterName)) + "\n" +
                                      Lang.F("{0} / {1} online · average level {2}", Guild.OnlineMembers, Mathf.Max(Guild.Capacity, Guild.Members.Count), Guild.AverageLevel) + "\n" +
                                      Lang.F("Guild EXP {0:0.0}% · skill points {1}", pct, Guild.SkillPoints) + "\n" +
                                      Lang.F("Your rank <color=#ffd060>{0}</color>", Guild.MyTitle.Length > 0 ? Guild.MyTitle : "?") + (Guild.CanInvite ? " · " + Lang.T("can invite") : "") + (Guild.CanExpel ? " · " + Lang.T("can expel") : "") + "\n" +
                                      Lang.T("Chat with the guild: start a line with $") + notice;
                    break;
                }
                case Tab.Mail:
                {
                    if (Mail == null) break;
                    int unread = Mail.Letters.Count(x => !x.Read);
                    var latest = Mail.Letters.Take(6).Select(l => $"{(l.Read ? "  " : "<color=#ffd060>●</color> ")}{l.Title}  <color=#9aa3b5>— {(l.Sender.Length > 0 ? l.Sender : Lang.T("~mail~server"))}</color>");
                    _mailInfo.text = (Mail.Unread ? Lang.T("<color=#ffd060>You have unread mail.</color>") : Lang.T("No unread mail.")) +
                                     (Mail.ListReceived ? "\n" + (unread > 0 ? Lang.F("{0} in the mailbox, {1} unread", Mail.Letters.Count, unread) : Lang.F("{0} in the mailbox", Mail.Letters.Count)) + "\n\n" + string.Join("\n", latest) : "\n" + Lang.T("Open the mailbox to see your letters."));
                    break;
                }
                case Tab.Achievements:
                {
                    if (Achievements == null) break;
                    var a = Achievements; int claim = a.Claimable;
                    _achInfo.text = a.ListReceived ? Lang.F("Level <color=#ffd060>{0}</color>  ·  {1:N0} / {2:N0}\nScore {3:N0}  ·  {4} completed, {5} under way", a.Level, a.LevelPoints, a.LevelNeed, a.TotalScore, a.Log.Values.Count(x => x.Completed), a.Log.Values.Count(x => !x.Completed)) +
                                   (claim > 1 ? "\n" + Lang.F("<color=#ffd060>{0} rewards waiting</color>", claim) : claim == 1 ? "\n" + Lang.T("<color=#ffd060>1 reward waiting</color>") : "") +
                                   "\n" + (a.CurrentTitle > 0 ? Lang.F("Title: «{0}»  ({1} owned)", Achievements.TitleName(a.CurrentTitle), a.Titles.Count) : Lang.F("Title: none  ({0} owned)", a.Titles.Count)) +
                                   (CheckIn != null && CheckIn.DaysClaimed >= 0 ? "\n" + (CheckIn.DaysClaimed == 1
                                        ? (CheckIn.ClaimedToday ? Lang.T("Daily check-in: 1 day, done today") : Lang.T("Daily check-in: 1 day, today is waiting"))
                                        : (CheckIn.ClaimedToday ? Lang.F("Daily check-in: {0} days, done today", CheckIn.DaysClaimed) : Lang.F("Daily check-in: {0} days, today is waiting", CheckIn.DaysClaimed))) : "")
                                 : Lang.T("No achievements yet.");
                    break;
                }
                case Tab.Bank:
                {
                    if (Bank == null) break;
                    _bankInfo.text = Bank.Vault >= 0 ? Lang.F("On hand {0:N0} z\nIn the bank {1:N0} z\n\nThe bank is shared by every character of your account.", _ps.Zeny, Bank.Vault)
                                                     : Lang.F("On hand {0:N0} z\nIn the bank <color=#9aa3b5>(open the bank to see)</color>\n\nThe bank is shared by every character of your account.", _ps.Zeny);
                    break;
                }
                case Tab.CashShop:
                {
                    if (CashShop == null) break;
                    _cashInfo.text = CashShop.Cash >= 0 ? Lang.F("Cash points {0:N0}\nKafra points {1:N0}\n\n{2} items for sale", CashShop.Cash, CashShop.Kafra, CashShop.Tabs.Sum(t => t.Value.Count)) : Lang.T("Open the shop to see your points.");
                    break;
                }
                case Tab.Companions:
                {
                    var c = Companions;
                    if (c == null) break;
                    _cPet.text = c.CaptureMode ? Lang.T("<color=#ffd060>Taming: point at the monster and pull the trigger.</color>")
                        : c.HasPet ? Lang.F("Pet <color=#ffd060>{0}</color>  <color=#9aa3b5>Lv {1}</color>\nHunger {2}/100 <color=#9aa3b5>{3}</color> · intimacy <color=#9aa3b5>{4}</color>", c.PetName, c.PetLevel, c.PetHunger, Companions.HungerBand(c.PetHunger), Companions.PetIntimacyBand(c.PetIntimacy))
                        : c.EggListOpen ? Lang.T("Pet: <color=#ffd060>choose an egg in the pet window</color>") : Lang.T("Pet: <color=#9aa3b5>none out</color>");
                    _cPetFeed.gameObject.SetActive(c.HasPet); _cPetPerform.gameObject.SetActive(c.HasPet);
                    string hs = c.HomDead ? Lang.T("<color=#ff8a7a>dead</color>") : c.HomResting ? Lang.T("<color=#9aa3b5>resting</color>") : c.HomOut ? Lang.T("<color=#7fd67f>out</color>") : Lang.T("<color=#9aa3b5>not here</color>");
                    _cHom.text = c.HomKnown || c.HomId != 0
                        ? Lang.F("Homunculus <color=#ffd060>{0}</color>  <color=#9aa3b5>Lv {1}</color> — {2}\nHP {3:N0} / {4:N0} · SP {5:N0} / {6:N0}\nHunger {7}/100 <color=#9aa3b5>{8}</color>", c.HomName, c.HomLevel, hs, c.HomHp, c.HomMaxHp, c.HomSp, c.HomMaxSp, c.HomHunger, Companions.HungerBand(c.HomHunger))
                        : Lang.T("Homunculus: <color=#9aa3b5>none</color>");
                    _cHomFeed.gameObject.SetActive(c.HomActive); _cHomAttack.gameObject.SetActive(c.HomActive); _cHomFollow.gameObject.SetActive(c.HomActive);
                    long left = c.MercExpire - System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    _cMerc.text = c.HasMerc
                        ? (left > 0 ? Lang.F("Mercenary <color=#ffd060>{0}</color>  <color=#9aa3b5>Lv {1}</color>\nHP {2:N0} / {3:N0} · SP {4:N0} / {5:N0}\nContract {6}:{7:00} left", c.MercName, c.MercLevel, c.MercHp, c.MercMaxHp, c.MercSp, c.MercMaxSp, left / 60, left % 60)
                                    : Lang.F("Mercenary <color=#ffd060>{0}</color>  <color=#9aa3b5>Lv {1}</color>\nHP {2:N0} / {3:N0} · SP {4:N0} / {5:N0}\nContract ending", c.MercName, c.MercLevel, c.MercHp, c.MercMaxHp, c.MercSp, c.MercMaxSp))
                        : Lang.T("Mercenary: <color=#9aa3b5>none</color>");
                    _cMercAttack.gameObject.SetActive(c.HasMerc); _cMercFollow.gameObject.SetActive(c.HasMerc);
                    break;
                }
                case Tab.Social: RefreshW4SocialFriends(); break; // client wave 4 (w4social): the friend list (WristMenu.W4Social.cs)
                case Tab.Quick:
                {
                    if (_swapAllBtn != null && Swap != null)
                    {
                        float cd = Swap.CooldownUntil - Time.time;
                        _swapAllBtn.SetLabel(cd > 0 ? Lang.F("Swap gear ({0} s)", Mathf.CeilToInt(cd)) : Swap.Set.Count > 0 ? Lang.F("Swap gear ({0})", Swap.Set.Count) : Lang.T("Swap gear"));
                    }
                    for (int i = 0; i < _quickRows.Count; i++)
                    {
                        var (icon, label, use, clear) = _quickRows[i];
                        var h = Hotkeys != null ? Hotkeys.Quick(i) : default;
                        if (h.Empty) { icon.enabled = false; label.text = "<color=#6a7080>" + Lang.T("~slot~empty") + "</color>"; use.gameObject.SetActive(false); clear.gameObject.SetActive(false); continue; }
                        use.gameObject.SetActive(true); clear.gameObject.SetActive(true);
                        if (h.IsSkill)
                        {
                            var info = SkillDb.Get(h.Id); icon.sprite = SkillIcon(info); icon.enabled = icon.sprite != null;
                            bool have = _prog.Skills.TryGetValue(h.Id, out var k) && k.Level > 0;
                            label.text = have ? Lang.F("{0}  <color=#9aa3b5>skill Lv {1}</color>", info.Name, k.Level) : Lang.F("{0}  <color=#9aa3b5>skill (not learned)</color>", info.Name);
                        }
                        else
                        {
                            var info = GameDb.GetItem(h.Id); icon.sprite = ItemIcon(info); icon.enabled = icon.sprite != null;
                            int have = _inv.Count(h.Id);
                            label.text = $"{info.Name}  <color={(have > 0 ? "#ffd060" : "#ff8a7a")}>x{have}</color>";
                        }
                    }
                    RefreshW4VrQuick(); // client wave 4 (w4vr): row label, cooldown seconds (WristMenu.W4Vr.cs)
                    break;
                }
                case Tab.Items when _detailIndex >= 0:
                {
                    if (!_inv.Items.TryGetValue(_detailIndex, out var e)) { _detailIndex = -1; _dirty = true; break; }
                    _itemList.gameObject.SetActive(false); _itemDetail.gameObject.SetActive(true);
                    var info = e.Info;
                    _detIcon.sprite = ItemIcon(info); _detIcon.enabled = _detIcon.sprite != null;
                    _detName.text = Inventory.FullName(e) + (e.Amount > 1 ? $"  <color=#ffd060>x{e.Amount}</color>" : "");
                    var flags = new List<string>();
                    if (e.Wearing != 0) flags.Add("<color=#7fd67f>" + Lang.T("worn") + "</color>");
                    if (e.Rental) { int left = _inv.SecondsLeft(e); flags.Add(left < 0 ? Lang.T("rental") : left >= 3600 ? Lang.F("rental, {0} h {1} min left", left / 3600, left % 3600 / 60) : Lang.F("rental, {0} min left", left / 60)); }
                    if (e.Bound) flags.Add(Lang.T("bound")); else if (e.BindType == 1) flags.Add(Lang.T("binds when worn"));
                    if (e.Damaged) flags.Add("<color=#ff7a6a>" + Lang.T("damaged") + "</color>"); if (e.Favorite) flags.Add("<color=#ffd060>" + Lang.T("★ favourite") + "</color>"); if (info.NoDrop) flags.Add(Lang.T("can't be dropped"));
                    _detFav.SetLabel(e.Favorite ? Lang.T("★ Unfavourite") : Lang.T("★ Favourite")); _detFav.gameObject.SetActive(e.Wearing == 0);
                    // "type   ·   weight   ·   slots   ·   level": each part is its own unit (w4lang)
                    _detInfo.text = info.TypeName + "   ·   " + (e.Amount > 1 ? Lang.F("weight {0:0.#} ({1:0.#} total)", info.Weight / 10f, info.Weight * e.Amount / 10f) : Lang.F("weight {0:0.#}", info.Weight / 10f))
                                  + (info.Slots > 0 ? "   ·   " + (info.Slots > 1 ? Lang.F("{0} slots", info.Slots) : Lang.T("1 slot")) : "") + (info.MinLevel > 0 ? "   ·   " + Lang.F("level {0}+", info.MinLevel) : "") + (flags.Count > 0 ? "\n" + string.Join(", ", flags) : "");
                    _detDesc.text = string.IsNullOrEmpty(info.Description) ? Lang.T("<color=#8890a0>No description in the client data.</color>") : NpcDialog.DataText(info.Description); // (waves 27-28 merge: the data's links as their names)
                    string verb = e.Wearing != 0 ? Lang.T("Take off") : e.IsEquip ? Lang.T("Equip") : e.IsCard ? Lang.T("Insert") : info.IsConsumable ? Lang.T("Use") : null;
                    _detAct.gameObject.SetActive(verb != null); if (verb != null) _detAct.SetLabel(verb);
                    string why = Inventory.DropBlocker(e);
                    foreach (var b in new[] { _detDrop1, _detDropAll }) { b.Interactable = why == null; b.Normal = why == null ? UiKit.ButtonBg : new Color(0.28f, 0.28f, 0.3f); b.SetHover(false); }
                    _detDropAll.gameObject.SetActive(e.Amount > 1);
                    _detDrop1.SetLabel(e.Amount > 1 ? Lang.T("Drop 1") : Lang.T("Drop"));
                    _detSwap.gameObject.SetActive(Swap != null && e.IsEquip && (e.Wearing == 0 || Swap.InSet(e.Index)));
                    if (Swap != null) _detSwap.SetLabel(Swap.InSet(e.Index) ? Lang.T("Swap set −") : Lang.T("Swap set +"));
                    RefreshW3EquipDetail(e); // client wave 3 (w3equip): side choice, refine / grade / cards / random options (WristMenu.W3Equip.cs)
                    RefreshW4ItemsDetail(e); // client wave 4 (w4items): the Compare button (WristMenu.W4Items.cs)
                    break;
                }
                case Tab.Items when _w3equipOpen: RefreshW3Equip(); break; // client wave 3 (w3equip): the equipment page (WristMenu.W3Equip.cs)
                case Tab.Items:
                {
                    _itemList.gameObject.SetActive(true); _itemDetail.gameObject.SetActive(false);
                    W3EquipListShown(); // client wave 3 (w3equip)
                    if (_marketBtn != null && MarketLabel != null) _marketBtn.SetLabel(MarketLabel());
                    for (int f = 0; f < _filterBtns.Count; f++) { _filterBtns[f].Normal = f == _itemFilter ? new Color(0.45f, 0.35f, 0.15f) : UiKit.ButtonBg; _filterBtns[f].SetHover(false); }
                    _sortBtn.SetLabel(Lang.T(ItemSorts[_itemSort]));
                    RefreshW4ItemsList(); // client wave 4 (w4items): the favourite tab's sale lock (WristMenu.W4Items.cs)
                    var list = SortedItems();
                    int pages = Mathf.Max(1, (list.Count + ItemsPerPage - 1) / ItemsPerPage);
                    _itemPageIdx = Mathf.Clamp(_itemPageIdx, 0, pages - 1);
                    _itemPage.text = list.Count == 0 ? Lang.F("empty · {0}/{1}", _inv.Items.Count, _inv.Capacity) : $"{_itemPageIdx + 1}/{pages} · {_inv.Items.Count}/{_inv.Capacity}";
                    for (int r = 0; r < ItemsPerPage; r++)
                    {
                        int i = _itemPageIdx * ItemsPerPage + r;
                        var (icon, label, act, pin, open) = _itemRows[r];
                        open.gameObject.SetActive(i < list.Count); pin.gameObject.SetActive(i < list.Count && Hotkeys != null && !list[i].IsEquip && list[i].Info.IsConsumable);
                        if (i >= list.Count) { icon.enabled = false; label.text = ""; act.gameObject.SetActive(false); continue; }
                        var e = list[i]; var info = e.Info;
                        icon.sprite = ItemIcon(info); icon.enabled = icon.sprite != null;
                        string name = (e.Favorite ? "<color=#ffd060>★</color> " : "") + Inventory.FullName(e);
                        string tags = (e.Rental ? "  <color=#9ad1ff>" + Lang.T("rental") + "</color>" : "") + (e.Bound ? "  <color=#c9a0ff>" + Lang.T("bound") + "</color>" : "");
                        label.text = e.IsEquip ? $"{name}{(e.Wearing != 0 ? "  <color=#7fd67f>" + Lang.T("[worn]") + "</color>" : "")}{tags}" : $"{name}  <color=#ffd060>x{e.Amount}</color>{tags}";
                        string verb = e.Wearing != 0 ? Lang.T("Take off") : e.IsEquip ? Lang.T("Equip") : e.IsCard ? Lang.T("Insert") : info.IsConsumable ? Lang.T("Use") : null;
                        bool tooLow = info.MinLevel > _ps.BaseLevel && e.Wearing == 0;
                        if (verb != null && tooLow) verb = "Lv " + info.MinLevel;
                        act.gameObject.SetActive(verb != null);
                        if (verb != null) { act.SetLabel(verb); act.Normal = tooLow ? new Color(0.3f, 0.3f, 0.32f) : UiKit.ButtonBg; act.SetHover(false); }
                    }
                    break;
                }
                case Tab.Quests:
                {
                    var list = _quests.Quests.Values.OrderByDescending(q => q.Active).ThenByDescending(q => q.Id).ToList();
                    int pages = Mathf.Max(1, (list.Count + QuestsPerPage - 1) / QuestsPerPage);
                    _questPageIdx = Mathf.Clamp(_questPageIdx, 0, pages - 1);
                    _questPage.text = list.Count == 0 ? Lang.T("no quests yet") : $"{_questPageIdx + 1} / {pages}";
                    for (int r = 0; r < QuestsPerPage; r++)
                    {
                        int i = _questPageIdx * QuestsPerPage + r;
                        var (title, body, row) = _questRows[r];
                        row.gameObject.SetActive(i < list.Count);
                        if (i >= list.Count) continue;
                        var q = list[i]; var info = q.Info;
                        title.text = (q.HuntsDone && q.Objectives.Count > 0 ? "<color=#7fd67f>" + Lang.T("[done]") + "</color> " : "") + info.Display + (q.Active ? "" : "  <color=#888888>" + Lang.T("(inactive)") + "</color>");
                        var sb = new System.Text.StringBuilder();
                        if (!string.IsNullOrEmpty(info.Summary)) sb.Append(NpcDialog.DataText(info.Summary)).Append('\n');
                        foreach (var ob in q.Objectives)
                        {
                            bool done = ob.Total > 0 && ob.Current >= ob.Total;
                            sb.Append(done ? "<color=#7fd67f>" : "<color=#ffd060>").Append("- ").Append(ob.Name).Append("  ").Append(ob.Current).Append(" / ").Append(ob.Total > 0 ? ob.Total.ToString() : "?").Append("</color>\n");
                        }
                        if (_expandedQuest == i && !string.IsNullOrEmpty(info.Description)) sb.Append("<color=#c8cbd6>").Append(NpcDialog.DataText(info.Description)).Append("</color>");
                        else if (!string.IsNullOrEmpty(info.Description)) sb.Append("<color=#8890a0><i>").Append(Lang.T("(click for details)")).Append("</i></color>");
                        body.text = sb.ToString();
                    }
                    break;
                }
                case Tab.Emotes: RefreshW3SocialEmotes(); break; // client wave 3 (w3social): the palette page (WristMenu.W3Social.cs)
                default: RefreshWave2(); break; // client wave 2 pages (WristMenu.Wave2.cs)
            }
        }

        private static void SetFill(RectTransform fill, float f) { fill.sizeDelta = new Vector2((W - 74) * Mathf.Clamp01(float.IsNaN(f) ? 0 : f), 18); }
        /// <summary>Client wave 4b (w4lang): STR…LUK as the LATAM client writes them in Portuguese (FOR AGI VIT INT DES SOR, its
        /// msgstringtable 4283-4288); Progression.StatNames keeps the English for logs.</summary>
        private static string StatName(int i) => i switch { 0 => Lang.T("STR"), 1 => Lang.T("AGI"), 2 => Lang.T("VIT"), 3 => Lang.T("INT"), 4 => Lang.T("DEX"), _ => Lang.T("LUK") };

        private static readonly Dictionary<int, Sprite> _icons = new Dictionary<int, Sprite>();
        public static Sprite ItemIcon(GameDb.Item info)
        {
            if (_icons.TryGetValue(info.Id, out var s)) return s;
            s = null;
            var a = LokiBootstrap.Instance != null ? LokiBootstrap.Instance.Assets : null;
            if (a != null && !string.IsNullOrEmpty(info.Resource))
            {
                var tex = a.Texture("À¯ÀúÀÎÅÍÆäÀÌ½º\\item\\" + info.Resource + ".bmp", false); // 유저인터페이스\item
                if (tex != a.MissingTexture) s = UiKit.ToSprite(tex);
            }
            _icons[info.Id] = s;
            return s;
        }
    }
}
