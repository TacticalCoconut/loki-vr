using System;
using System.Collections;
using System.Collections.Generic;
using Loki.Game;
using Loki.World;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 21 (w21inv), the design's point 62 (2026-10-09): "The inventory feels too disorganized. Let's redo it. When you open up the
    /// inventory, it is a curved panel that sits in front of you. It opens by smoothly 'sliding open' from left to right. Instead of the
    /// entire inventory being placed in a single grid, with the option to filter them, it is baseline already separated by item types,
    /// with one subsection of the large curved panel being dedicated to each type of item ... Each subsection can be filtered
    /// independently from the others"; its look: the UI theme palette (answers 18, 19).
    /// 2026-10-11, after the preview: "the individual inventory sections should have a default of 3 columns instead of 2 per item
    /// category. The Player can customize how many columns a given category has, up to 4", and "a small cloth tab at the top of the
    /// center portion that, when clicked, opens an extra panel above it that shows the character equipment screen ... lasering over a
    /// slot highlights valid equipments on the inventory. This defaults to closed, but can be 'pinned'".
    ///
    /// THE VIEW (the rules of use are UI/WristMenu.Inv.cs). It is drawn UNDER THE MENU BOARD'S OWN ROOT while the board's own picture
    /// (frame, title, tabs, page) is hidden, so everything that knows "the board" keeps working unchanged: one menu at a time
    /// (UI/MenuNav.cs), the grab by an edge and the backpack (LokiGame.W13Grab.cs: the held panel is the board's root), resize mode,
    /// the place kept for the next opening (UI/WristMenu.Board.cs KeepBoardPlace), the pull on the bag (rig.PanelPull).
    /// WHAT IS DRAWN, back to front: the dark wood frame with its gold line and corner scrolls and the cream canvas with its leather
    /// edge (the kit's own 9-slice pictures bent on a cylinder: UI/InvBent.cs BentImage); the stitching along the canvas and round each
    /// section's pocket (BentStitch); per section a header (a dark line picture in a thin gold ring, the section's name and count, two
    /// small round gold buttons: sort, filter) and its cells (1 to 4 columns of <see cref="Rows"/>, pooled: bound again to the items
    /// when the section scrolls) with a line "1-12 of 34" that turns the page; a foot line (slots, weight, the Items group's other
    /// pages); the rolled end with its two straps (RollGraphic); the small gold title plate; over the frame's top middle the cloth tab
    /// and its pin; pulled up by the tab, the equipment panel above the strip, the tab and the pin carried up on its top edge; the
    /// item's parchment card(s).
    /// THE CURVE: the cylinder's axis stands where the eyes were when the board was placed (<see cref="Rebend"/>), so every column is
    /// as far from the eyes as the middle one. Each column of cells and each header is its own flat rectangle touching the cylinder.
    /// SIZES (px at MenuBoard.MetresPerPx): a cell is a square of the old bag's cell's height (94 px; its picture 72, its texts 24, as the
    /// old bag's; the old cell was 114 wide: 10 px of empty margin each side of the picture). The strip's width follows the sections'
    /// columns (<see cref="StripW"/>): six sections of three columns = 2000 px = 1.48 m = 129 degrees at 0.66 m; of four = 2588 px, bent
    /// on a larger circle so that it never turns more than <see cref="MaxWrap"/> degrees of its own.
    /// </summary>
    public sealed class InvPanel
    {
        public const int Sections = 6, MaxCols = 4, MinCols = 1, DefaultCols = 3, Rows = 4, MaxPerSection = MaxCols * Rows, CellCount = Sections * MaxPerSection;
        public const float CellW = 94f, CellH = 94f, CellGap = 4f, PocketPad = 6f, PocketGap = 8f, PocketMin = 166f, MarginL = 20f, MarginR = 12f, RollW = 116f, RollR = 62f;
        public const float PadTop = 26f, RingD = 104f, NameH = 28f, BtnD = 72f, GridH = Rows * CellH + (Rows - 1) * CellGap, PageH = 46f, FootH = 72f, PadBottom = 22f;
        public const float StripH = PadTop + RingD + 2f + NameH + 4f + BtnD + 10f + GridH + 8f + PageH + 10f + FootH + PadBottom;
        /// <summary>The cylinder's radius in px: no smaller than 0.64 m (the strip is never bent tighter, however near the eyes it is
        /// held) nor than the strip's width over <see cref="MaxWrap"/> degrees (a very wide strip is bent on a larger circle), no larger
        /// than <see cref="RadiusMax"/>.</summary>
        public const float RadiusMin = 860f, RadiusMax = 2400f, MaxWrap = 135f;
        /// <summary>The cards stand this many px nearer the eyes than the strip (5 cm), this far from the pointed section, and show after
        /// the pointer has rested this long on a cell.</summary>
        public const float CardOff = 68f, CardGap = 16f, CardDwell = 0.25f;
        /// <summary>The unroll (from the wrist) and the spring (out of the backpack), seconds (cut to the menu's limit: UI/MenuAnim.cs).</summary>
        public const float UnrollSeconds = 0.6f, SpringSeconds = 0.5f;
        /// <summary>The compact bundle's half width (two rolls side by side), and its size against the strip's (it grows as it springs open).</summary>
        public const float CompactHalf = 2f * RollR + 10f, CompactScale = 0.5f;
        /// <summary>The equipment panel the tab pulls up. THE BAND (the default): a wide, low panel: the character in the middle, its
        /// slots as cells beside it (the worn item's picture and the slot's name) in two blocks of two columns by three rows, Take off,
        /// the other group's slots, and one line that says what the slot under the pointer (or the armed one) holds. Its texts are
        /// <see cref="BandFont"/> px (the equipment screen's own are 18): never drawn smaller.
        /// THE FULL SCREEN (<see cref="EquipFull"/>): UI/EquipmentWindow.cs itself at its own size (its page needs 596 x 697 px at
        /// least).
        /// Client wave 23 (w23inv; design note, 2026-10-11: "When the cloth tab above the inventory menu is clicked, the
        /// inventory should not be pushed down. The cloth tab should be pushed up with the panel instead"): THE STRIP STAYS EXACTLY
        /// WHERE IT STANDS while either is up (wave 21 turned the inventory's whole drawing down about the eyes, 14 degrees for the
        /// band, to bring the pair's middle to the board's level: gone). The panel rises from behind the strip's top edge at its
        /// middle (its lower frame edge <see cref="EquipTuck"/> px behind the strip's frame; it is drawn BEHIND the strip, so where
        /// the bent strip's ends stand in front of the flat panel's lower corners the strip covers them, never the other way round)
        /// and leans about that edge to face the eyes of the placing (<see cref="PlaceEquip"/>; between <see cref="EquipLeanMin"/>
        /// and <see cref="EquipLeanMax"/> degrees); the tab and its pin are carried up onto its top edge (<see cref="ShowEquip"/>).
        /// The pull (seconds); the tab's and the pin's sizes; a dimmed cell's alpha.</summary>
        public const float EquipPageW = 610f, EquipPageH = 710f, EquipW = EquipPageW + 32f, EquipH = EquipPageH + 32f, EquipSeconds = 0.35f;
        public const float EquipTuck = 6f, EquipLeanMin = -45f, EquipLeanMax = 75f;
        public const float BandCellW = 210f, BandCellH = 84f, BandPitch = 90f, BandDollW = 150f, BandPad = 14f, BandLineH = 36f;
        public const float BandW = 2f * (BandDollW / 2f + 12f + 2f * BandCellW + 6f + BandPad), BandH = BandPad + 3f * BandPitch - 6f + 8f + BandLineH + BandPad;
        public const int BandFont = 23;
        /// <summary>The tab pulls up the equipment screen at its full size instead of the band (the proposal's other half: configurable).</summary>
        public static bool EquipFull { get; set; }
        public const float TabW = 170f, TabH = 64f, PinD = 56f, DimAlpha = 0.4f;
        /// <summary>On the strip the tab stands this far over the frame's outer edge (clear of the title plate). Carried up, it sits
        /// on the equipment panel's own top edge (its lower <see cref="EquipTuck"/> px over that frame).</summary>
        public const float TabUp = 14f;

        // ------------------------------------------------------------------ sections, sorts, filters
        public static readonly string[] SectionNames = { Lang.N("~inv~Equipment"), Lang.N("~inv~Weapons"), Lang.N("~inv~Consumables"), Lang.N("~inv~Cards"), Lang.N("~inv~Ammo"), Lang.N("~inv~Misc") };
        private static readonly string[] SectionIcons = { "helmet", "sword", "flask", "cards", "arrows", "pouch" };
        public enum SortKey { Slot, Class, Kind, Name, Level, Refine, Weight, Amount, Value }
        private static readonly string[] SortNames = { Lang.N("~inv~Slot"), Lang.N("~inv~Class"), Lang.N("~inv~Kind"), Lang.N("~inv~Name"), Lang.N("~inv~Level"), Lang.N("~inv~Refine"), Lang.N("~inv~Weight"), Lang.N("~inv~Amount"), Lang.N("~inv~Value") };
        /// <summary>Each section's sorts; the first is its own order.</summary>
        public static readonly SortKey[][] Sorts =
        {
            new[] { SortKey.Slot, SortKey.Name, SortKey.Level, SortKey.Refine, SortKey.Weight, SortKey.Value },
            new[] { SortKey.Class, SortKey.Name, SortKey.Level, SortKey.Refine, SortKey.Weight, SortKey.Value },
            new[] { SortKey.Kind, SortKey.Name, SortKey.Amount, SortKey.Weight, SortKey.Value },
            new[] { SortKey.Name, SortKey.Amount, SortKey.Value },
            new[] { SortKey.Name, SortKey.Amount, SortKey.Weight },
            new[] { SortKey.Name, SortKey.Amount, SortKey.Weight, SortKey.Value },
        };
        public enum Filter { All, Fav, Head, Body, Shield, Garment, Shoes, Accessory, Costume, Shadow, OneHand, TwoHand, Healing, Usable, Cash, CardWeapon, CardShield, CardArmor, CardGarment, CardShoes, CardAccessory, CardHead, Wear }
        private static readonly string[] FilterNames =
        {
            Lang.N("~inv~All"), Lang.N("~inv~★ Favourites"), Lang.N("~inv~Head"), Lang.N("~inv~Body"), Lang.N("~inv~Shield"), Lang.N("~inv~Garment"), Lang.N("~inv~Shoes"), Lang.N("~inv~Accessory"),
            Lang.N("~inv~Costume"), Lang.N("~inv~Shadow"), Lang.N("~inv~One-handed"), Lang.N("~inv~Two-handed"), Lang.N("~inv~Healing"), Lang.N("~inv~Usable"), Lang.N("~inv~Cash"),
            Lang.N("~inv~For weapons"), Lang.N("~inv~For shields"), Lang.N("~inv~For armor"), Lang.N("~inv~For garments"), Lang.N("~inv~For shoes"), Lang.N("~inv~For accessories"), Lang.N("~inv~For headgear"),
            Lang.N("~inv~I can wear"),
        };
        /// <summary>Each section's filters (All and the favourites first; of the others only those that fit an item now are offered).
        /// "I can wear": the equipment screen's own rule for this character (Game/EquipRules.cs: level, sex, job, class tier).</summary>
        public static readonly Filter[][] Filters =
        {
            new[] { Filter.All, Filter.Fav, Filter.Wear, Filter.Head, Filter.Body, Filter.Shield, Filter.Garment, Filter.Shoes, Filter.Accessory, Filter.Costume, Filter.Shadow },
            new[] { Filter.All, Filter.Fav, Filter.Wear, Filter.OneHand, Filter.TwoHand },
            new[] { Filter.All, Filter.Fav, Filter.Healing, Filter.Usable, Filter.Cash },
            new[] { Filter.All, Filter.Fav, Filter.CardWeapon, Filter.CardShield, Filter.CardArmor, Filter.CardGarment, Filter.CardShoes, Filter.CardAccessory, Filter.CardHead },
            new[] { Filter.All, Filter.Fav },
            new[] { Filter.All, Filter.Fav },
        };
        public static string SortName(SortKey k) => Lang.T(SortNames[(int)k]);
        public static string FilterName(Filter f) => Lang.T(FilterNames[(int)f]);
        public static string SectionKey(int sec) => SectionNames[sec].Substring(5);

        // equip positions (rAthena's equip_pos, mmo.hpp): head low / mid / top, armor, hands, garment, shoes, accessories, costumes, shadow gear
        private const uint PosHead = 0x301, PosArmor = 0x10, PosHandR = 0x02, PosHandL = 0x20, PosGarment = 0x04, PosShoes = 0x40, PosAcc = 0x88, PosCostume = 0x3C00, PosShadow = 0x3F0000, PosShadowWeapon = 0x20000;

        /// <summary>The section an item is in, from its type as the old bag's tabs read it (the item table's type, the server's for an item
        /// the table does not know; the positions it is worn at): 0 Equipment (armor, headgear, garments, shoes, accessories, costumes,
        /// shadow gear, pet eggs and pet equipment), 1 Weapons, 2 Consumables (healing, usable, cash), 3 Cards, 4 Ammo, 5 Misc.</summary>
        public static int SectionOf(Inventory.Entry e)
        {
            var info = e.Info;
            bool known = !string.IsNullOrEmpty(info.Resource) || !string.IsNullOrEmpty(info.English);
            int t = known ? info.Type : e.Type;
            if (e.IsCard || t == 6) return 3;
            if (t == 10 || (e.Location & Inventory.AmmoSlot) != 0) return 4;
            if (t == 12) return (e.Location & PosShadowWeapon) != 0 ? 1 : 0;
            if (t == 5 || (e.Type == 5 && e.IsEquip)) return 1;
            if (e.IsEquip || t == 4 || t == 7 || t == 8) return 0;
            if (t == 0 || t == 2 || t == 11 || t == 18) return 2;
            return 5;
        }

        private Inventory _inv; private int _baseLevel = 1;
        public bool Passes(Inventory.Entry e, Filter f)
        {
            uint loc = e.Location; int t = e.Info.Type;
            switch (f)
            {
                case Filter.All: return true;
                case Filter.Fav: return e.Favorite;
                case Filter.Wear: return e.Wearing != 0 || (e.IsEquip && _inv != null && EquipRules.WhyNot(e, _inv, _baseLevel) == null);
                case Filter.Head: return (loc & PosHead) != 0;
                case Filter.Body: return (loc & PosArmor) != 0;
                case Filter.Shield: return (loc & PosHandL) != 0;
                case Filter.Garment: return (loc & PosGarment) != 0;
                case Filter.Shoes: return (loc & PosShoes) != 0;
                case Filter.Accessory: return (loc & PosAcc) != 0;
                case Filter.Costume: return (loc & PosCostume) != 0;
                case Filter.Shadow: return (loc & PosShadow) != 0;
                case Filter.TwoHand: return (loc & 0x22) == 0x22 && !e.DualPatched && TwoHanded(e);
                case Filter.OneHand: return !((loc & 0x22) == 0x22 && !e.DualPatched && TwoHanded(e));
                case Filter.Healing: return t == 0;
                case Filter.Usable: return t == 2 || t == 11;
                case Filter.Cash: return t == 18;
                case Filter.CardWeapon: return (loc & PosHandR) != 0;
                case Filter.CardShield: return (loc & PosHandL) != 0 && (loc & PosHandR) == 0;
                case Filter.CardArmor: return (loc & PosArmor) != 0;
                case Filter.CardGarment: return (loc & PosGarment) != 0;
                case Filter.CardShoes: return (loc & PosShoes) != 0;
                case Filter.CardAccessory: return (loc & PosAcc) != 0;
                case Filter.CardHead: return (loc & PosHead) != 0;
            }
            return true;
        }
        /// <summary>A weapon that takes both hands by itself: not one of the one-handed kinds a dual wielder may put in either hand
        /// (dagger 1, one-handed sword 2, one-handed axe 6: Game/Inventory.cs ApplyDualWield).</summary>
        private static bool TwoHanded(Inventory.Entry e) { int sub = Sprites.AppearanceDb.WeaponSubType(e.ItemId); return sub != 1 && sub != 2 && sub != 6; }

        private static int SlotRank(uint loc)
        {
            if ((loc & PosShadow) != 0) return 8;
            if ((loc & PosCostume) != 0) return 7;
            if ((loc & PosHead) != 0) return 0;
            if ((loc & PosArmor) != 0) return 1;
            if ((loc & PosHandR) != 0) return 2;
            if ((loc & PosHandL) != 0) return 3;
            if ((loc & PosGarment) != 0) return 4;
            if ((loc & PosShoes) != 0) return 5;
            if ((loc & PosAcc) != 0) return 6;
            return 9;
        }
        private static int KindRank(int type) => type == 0 ? 0 : type == 2 || type == 11 ? 1 : type == 18 ? 2 : 3;
        private static int ValueOf(GameDb.Item i) => i.Sell > 0 ? i.Sell : i.Buy / 2;

        /// <summary>The order of two items of a section: what is worn first, then the favourites, then the sort, then the order they came in.</summary>
        public static int Compare(Inventory.Entry a, Inventory.Entry b, SortKey k)
        {
            int c = (b.Wearing != 0).CompareTo(a.Wearing != 0); if (c != 0) return c;
            c = b.Favorite.CompareTo(a.Favorite); if (c != 0) return c;
            var ia = a.Info; var ib = b.Info;
            switch (k)
            {
                case SortKey.Slot: c = SlotRank(a.Location).CompareTo(SlotRank(b.Location)); if (c == 0) c = string.Compare(ia.Name, ib.Name, StringComparison.OrdinalIgnoreCase); break;
                case SortKey.Class: c = Sprites.AppearanceDb.WeaponSubType(a.ItemId).CompareTo(Sprites.AppearanceDb.WeaponSubType(b.ItemId)); if (c == 0) c = string.Compare(ia.Name, ib.Name, StringComparison.OrdinalIgnoreCase); break;
                case SortKey.Kind: c = KindRank(ia.Type).CompareTo(KindRank(ib.Type)); if (c == 0) c = string.Compare(ia.Name, ib.Name, StringComparison.OrdinalIgnoreCase); break;
                case SortKey.Name: c = string.Compare(ia.Name, ib.Name, StringComparison.OrdinalIgnoreCase); break;
                case SortKey.Level: c = ib.MinLevel.CompareTo(ia.MinLevel); break;
                case SortKey.Refine: c = b.Refine.CompareTo(a.Refine); break;
                case SortKey.Weight: c = (ib.Weight * b.Amount).CompareTo(ia.Weight * a.Amount); break;
                case SortKey.Amount: c = b.Amount.CompareTo(a.Amount); break;
                case SortKey.Value: c = ValueOf(ib).CompareTo(ValueOf(ia)); break;
            }
            return c != 0 ? c : a.Index.CompareTo(b.Index);
        }

        // ---- the choices, kept with the account (World/UserSettings.cs invSort / invFilter / invCols: by the section's place)
        public SortKey SortOf(int sec)
        {
            var a = UserSettings.Prefs.invSort;
            if (a != null && sec < a.Length && !string.IsNullOrEmpty(a[sec]) && Enum.TryParse(a[sec], out SortKey k) && Array.IndexOf(Sorts[sec], k) >= 0) return k;
            return Sorts[sec][0];
        }
        public Filter FilterOf(int sec)
        {
            var a = UserSettings.Prefs.invFilter;
            if (a != null && sec < a.Length && !string.IsNullOrEmpty(a[sec]) && Enum.TryParse(a[sec], out Filter f) && Array.IndexOf(Filters[sec], f) >= 0) return f;
            return Filter.All;
        }
        public void SetSort(int sec, SortKey k)
        {
            if (sec < 0 || sec >= Sections || Array.IndexOf(Sorts[sec], k) < 0 || SortOf(sec) == k) return;
            var a = Fit(UserSettings.Prefs.invSort); a[sec] = k == Sorts[sec][0] ? "" : k.ToString();
            UserSettings.Prefs.invSort = a; UserSettings.Prefs.saved = true; UserSettings.Save();
            _top[sec] = 0; Dirty = true;
            Debug.Log($"[Inv] {SectionKey(sec)}: sort {k}");
        }
        public void SetFilter(int sec, Filter f)
        {
            if (sec < 0 || sec >= Sections || Array.IndexOf(Filters[sec], f) < 0 || FilterOf(sec) == f) return;
            var a = Fit(UserSettings.Prefs.invFilter); a[sec] = f == Filter.All ? "" : f.ToString();
            UserSettings.Prefs.invFilter = a; UserSettings.Prefs.saved = true; UserSettings.Save();
            _top[sec] = 0; Dirty = true;
            Debug.Log($"[Inv] {SectionKey(sec)}: filter {f}");
        }
        private static string[] Fit(string[] a) { var r = new string[Sections]; for (int i = 0; i < Sections; i++) r[i] = a != null && i < a.Length && a[i] != null ? a[i] : ""; return r; }
        /// <summary>The columns a section's choice says (1-4; none stored: three).</summary>
        public static int StoredCols(int sec)
        {
            var a = UserSettings.Prefs.invCols; int c = a != null && sec < a.Length ? a[sec] : 0;
            return c < MinCols || c > MaxCols ? DefaultCols : c;
        }
        public int Cols(int sec) => _cols[sec];
        /// <summary>A section's columns (1-4), kept with the account; the strip is laid out again for its new width.</summary>
        public void SetCols(int sec, int n)
        {
            n = Mathf.Clamp(n, MinCols, MaxCols);
            if (sec < 0 || sec >= Sections || StoredCols(sec) == n) return;
            var r = new int[Sections]; var a = UserSettings.Prefs.invCols;
            for (int i = 0; i < Sections; i++) r[i] = a != null && i < a.Length ? a[i] : 0;
            r[sec] = n == DefaultCols ? 0 : n;
            UserSettings.Prefs.invCols = r; UserSettings.Prefs.saved = true; UserSettings.Save();
            _top[sec] = 0;
            Debug.Log($"[Inv] {SectionKey(sec)}: {n} column(s)");
            _keepPopup = PopupOpen; Relayout(); _keepPopup = false;
            AfterCols();
        }

        // ------------------------------------------------------------------ the objects
        private sealed class Cell { public UiButton Btn; public Image Icon, Lock, Pick; public Text Count, Mark; public CanvasGroup Group; public int Index = -1, Sec; }
        private sealed class Placed { public RectTransform Rt; public float X, Y, Half; public CanvasGroup Group; }
        private sealed class SecStick : IUiStick
        {
            public InvPanel P; public int Sec; private float _acc;
            public bool OnStick(Vector2 stick, float dt)
            {
                if (Mathf.Abs(stick.y) < 0.3f || Mathf.Abs(stick.y) < Mathf.Abs(stick.x)) { _acc = 0f; return false; }
                _acc += -stick.y * ScrollRowsPerSecond * dt;
                while (_acc >= 1f) { _acc -= 1f; P.Scroll(Sec, 1); }
                while (_acc <= -1f) { _acc += 1f; P.Scroll(Sec, -1); }
                return true;
            }
        }
        /// <summary>The stick on a section (the laser or the look on one of its cells) scrolls it this many rows a second at full push.</summary>
        public const float ScrollRowsPerSecond = 8f;

        private readonly MenuBoard _board; private readonly RectTransform _root; private readonly UiSurface _surf;
        private readonly BentImage _frame, _canvas; private readonly BentStitch _stitch; private readonly RollGraphic _rollR, _rollL;
        private readonly RectTransform _popup, _plateRt;
        private readonly UiTitlePlate _plate;
        private readonly Cell[] _cells = new Cell[CellCount];
        private readonly Placed[] _heads = new Placed[Sections], _pages = new Placed[Sections], _hosts = new Placed[Sections];
        private readonly Placed[,] _colPs = new Placed[Sections, MaxCols];
        private readonly List<Placed> _backs = new List<Placed>(), _footPs = new List<Placed>(), _all = new List<Placed>();
        private Placed _rollP, _rollLP, _xHost, _backHost, _footEye, _footInfo, _footToast, _tabP;
        private readonly Text[] _names = new Text[Sections]; private readonly UiButton[] _sortBtns = new UiButton[Sections], _filterBtns = new UiButton[Sections], _pageBtns = new UiButton[Sections];
        private readonly UiRingIcon[] _rings = new UiRingIcon[Sections];
        private readonly List<Inventory.Entry>[] _lists = new List<Inventory.Entry>[Sections];
        private readonly int[] _top = new int[Sections], _counts = new int[Sections], _cols = new int[Sections];
        private readonly float[] _pl = new float[Sections], _pw = new float[Sections];
        private readonly List<UiButton> _footBtns = new List<UiButton>(), _popRows = new List<UiButton>();
        private readonly List<(UiButton b, float x, float y, bool cell)> _strip = new List<(UiButton, float, float, bool)>(); // every button of the strip, where it is (arc px); an item's cell or not
        private readonly List<Vector4> _spans = new List<Vector4>();
        private Text _info;
        private float _w = 2000f, _radius = 889f, _u = 0.42f, _outset = 36f;
        private Vector3 _eyeLocal = new Vector3(0f, 0f, -889f);
        private bool _shown;
        // the board's own pieces borrowed while it is shown, and where they came from
        private readonly List<(Transform t, Transform parent, Vector3 pos, Quaternion rot, int sibling)> _borrowed = new List<(Transform, Transform, Vector3, Quaternion, int)>();
        private readonly List<GameObject> _hidden = new List<GameObject>();
        private Vector2 _boardSize, _toastSize;

        public RectTransform Root => _root;
        public bool Shown => _shown && _root.gameObject.activeInHierarchy;
        public float Radius => _radius;
        /// <summary>The strip's width now (px): it follows the sections' columns.</summary>
        public float StripW => _w;
        /// <summary>Something changed: the owner draws it again (UI/WristMenu.Inv.cs RefreshInv).</summary>
        public bool Dirty { get; set; } = true;
        /// <summary>A cell was pressed (its item's inventory index); a foot button (the Items group's entry); the popup's sale lock row; the
        /// cloth tab; the pin.</summary>
        public Action<int> CellPressed; public Action<int> EntryPressed; public Action SaleLockPressed, TabPressed, PinPressed;
        public Func<bool> SaleLock; public Func<int, string> EntryLabel;
        /// <summary>The smallest body font of this board's item cards (px; the boards' own floor is ItemPanel.FontFloor, 21). FLAGGED.</summary>
        public const int CardFontFloor = 23;
        public ItemPanel CardItem { get; private set; }
        public ItemPanel CardWornA { get; private set; }
        public ItemPanel CardWornB { get; private set; }
        public UiButton CardAct { get; private set; }
        public UiButton CardDetails { get; private set; }
        public UiButton CardPin { get; private set; }
        public UiButton CardUnlock { get; private set; }
        public bool WornStacked { get; private set; }

        public static InvPanel Create(MenuBoard board) => new InvPanel(board);

        private static RectTransform Node(RectTransform parent, string name, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = size;
            return rt;
        }
        private T Bent<T>(string name) where T : MaskableGraphic
        {
            var rt = Node(_root, name, Vector2.zero); var g = rt.gameObject.AddComponent<T>();
            g.raycastTarget = false; if (UiKit.Overlay != null) g.material = UiKit.Overlay;
            return g;
        }
        /// <summary>A flat rectangle on the cylinder at the arc place (x, y) (set by <see cref="Relayout"/>); it fades in as the roll passes it.</summary>
        private Placed Facet(string name, float y, Vector2 size, bool fades = true)
        {
            var rt = Node(_root, name, size);
            rt.gameObject.AddComponent<UiFacet>(); // (a button's tip faces as its facet does: UI/ButtonTip.cs)
            var p = new Placed { Rt = rt, Y = y, Half = size.x * 0.5f, Group = fades ? rt.gameObject.AddComponent<CanvasGroup>() : null };
            _all.Add(p);
            return p;
        }

        // ---- where things are (arc px from the strip's middle; y up from its middle)
        public float PocketLeft(int sec) => _pl[sec];
        public float PocketW(int sec) => _pw[sec];
        public float PocketMid(int sec) => _pl[sec] + _pw[sec] / 2f;
        public float ColX(int sec, int col) { int n = _cols[sec]; float gw = n * CellW + (n - 1) * CellGap; return PocketMid(sec) - gw / 2f + CellW / 2f + col * (CellW + CellGap); }
        private const float Top = StripH / 2f;
        private const float RingY = Top - PadTop - RingD / 2f, NameY = Top - PadTop - RingD - 2f - NameH / 2f, BtnY = NameY - NameH / 2f - 4f - BtnD / 2f;
        public const float GridTop = BtnY - BtnD / 2f - 10f, GridMidY = GridTop - GridH / 2f, PageY = GridTop - GridH - 8f - PageH / 2f, FootY = -StripH / 2f + PadBottom + FootH / 2f;
        private const float HeadH = RingD + 2f + NameH + 4f + BtnD, HeadMidY = Top - PadTop - HeadH / 2f;
        public float RollX => _w / 2f - RollW / 2f + 8f;
        /// <summary>How far the strip's wood frame stands out of its rectangle (px).</summary>
        public float FrameOutset => _outset;
        /// <summary>Where a hand holds it when it comes out of the backpack: the middle of the frame's lower edge (the board's own px).</summary>
        public Vector3 HoldPoint => new Vector3(0f, -StripH / 2f - _outset * 0.5f, 0f);
        /// <summary>The strip's width for these columns (px).</summary>
        public static float WidthFor(int[] cols)
        {
            float w = MarginL + (Sections - 1) * PocketGap + MarginR + RollW;
            for (int s = 0; s < Sections; s++) w += Mathf.Max(PocketMin, cols[s] * CellW + (cols[s] - 1) * CellGap + 2f * PocketPad);
            return w;
        }
        public static int CellAt(int sec, int row, int col) => sec * MaxPerSection + row * MaxCols + col;

        private InvPanel(MenuBoard board)
        {
            _board = board;
            _root = Node(board.Root, "inv", Vector2.zero);
            _surf = Theme.SurfaceOf(board.Root);
            _u = _surf != null ? _surf.Unit : Theme.Unit(new Vector2(2000f, StripH), MenuBoard.MetresPerPx);
            _outset = Theme.FrameOutset(_u);
            if (_surf == null) { var s = _root.gameObject.AddComponent<UiSurface>(); s.Kind = Surface.Canvas; s.Unit = _u; } // (the board is a framed panel: it has one)
            // what the laser stops on: the strip in slices (each a flat rectangle on the cylinder), behind everything; enough for the widest.
            // The strip's RECTANGLE only, as every framed panel's collider (UI/UiKit.cs Panel): the wood frame lies in the grab's band
            // (UI/PanelGrab.cs BandOutside), where the laser or a hand takes the panel.
            var widest = new int[Sections]; for (int i = 0; i < Sections; i++) widest[i] = MaxCols;
            int slices = Mathf.CeilToInt(WidthFor(widest) / BackSlice);
            for (int i = 0; i < slices; i++)
            {
                var b = Facet("back" + i, 0f, new Vector2(BackSlice + 6f, StripH), false);
                var col = b.Rt.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(BackSlice + 6f, StripH, 4f); col.center = new Vector3(0, 0, 3f);
                b.Rt.gameObject.AddComponent<UiBlocker>();
                _backs.Add(b);
            }
            _frame = Bent<BentImage>("frame"); _frame.Sprite = Theme.Frame; _frame.Unit = _u; _frame.Outset = _outset; _frame.FillCenter = false;
            _canvas = Bent<BentImage>("canvas"); _canvas.Sprite = Theme.CanvasFill; _canvas.Unit = _u; _canvas.Outset = 3f + Theme.LeatherPx * _u;
            _stitch = Bent<BentStitch>("stitch"); _stitch.color = Theme.Thread; // (client waves 21 + 22, the merge: the thread of the look in force: FollowTheme)
            _stitch.Dash = Theme.StitchPx * _u; _stitch.Gap = Theme.StitchGapPx * _u; _stitch.Wide = Mathf.Max(1.6f, Theme.StitchWidePx * _u);

            for (int s = 0; s < Sections; s++)
            {
                int sec = s; _lists[s] = new List<Inventory.Entry>(); _cols[s] = StoredCols(s);
                // the pocket's host (the sort / filter list stands on it)
                _hosts[s] = Facet("sec" + s, 0f, new Vector2(PocketMin, StripH));
                // the header: the line picture in its gold ring, the name and count, the two round gold buttons under it
                var h = _heads[s] = Facet("head" + s, HeadMidY, new Vector2(PocketMin, HeadH));
                _rings[s] = UiKit.RingIcon(h.Rt, new Vector2(0, RingY - HeadMidY), RingD);
                _rings[s].Icon.sprite = InvIcons.Get(SectionIcons[s]); _rings[s].Icon.enabled = true;
                _rings[s].Icon.rectTransform.sizeDelta = new Vector2(RingD * 0.66f, RingD * 0.66f);
                _names[s] = UiKit.Label(h.Rt, "", new Vector2(0, NameY - HeadMidY), new Vector2(PocketMin, NameH + 2f), 24, TextAnchor.MiddleCenter, Theme.Ink);
                _names[s].horizontalOverflow = HorizontalWrapMode.Overflow; _names[s].verticalOverflow = VerticalWrapMode.Overflow;
                _sortBtns[s] = Round(h.Rt, "sort", new Vector2(-BtnD / 2f - 7f, BtnY - HeadMidY), BtnD, () => TogglePopup(sec, false));
                _sortBtns[s].Tip = () => Lang.F("~inv~Sort: {0}", SortName(SortOf(sec)));
                _filterBtns[s] = Round(h.Rt, "filter", new Vector2(BtnD / 2f + 7f, BtnY - HeadMidY), BtnD, () => TogglePopup(sec, true));
                _filterBtns[s].Tip = () => Lang.F("~inv~Filter: {0}", FilterName(FilterOf(sec)));
                var stick = new SecStick { P = this, Sec = s };
                for (int c = 0; c < MaxCols; c++)
                {
                    var colP = _colPs[s, c] = Facet($"col{s}.{c}", GridMidY, new Vector2(CellW, GridH));
                    for (int r = 0; r < Rows; r++)
                    {
                        int at = CellAt(s, r, c);
                        var cell = new Cell { Sec = s };
                        cell.Btn = UiKit.Button(colP.Rt, "", new Vector2(0, GridH / 2f - CellH / 2f - r * (CellH + CellGap)), new Vector2(CellW, CellH), () => { int idx = _cells[at].Index; if (idx >= 0) CellPressed?.Invoke(idx); }, 18);
                        cell.Btn.name = "cell" + at; cell.Btn.Stick = stick;
                        if (cell.Btn.GetComponent<Image>() is ThemedImage raw) raw.Raw = true; // (its fills are the theme's own or WornCell: drawn as they are)
                        var brt = (RectTransform)cell.Btn.transform;
                        cell.Group = brt.gameObject.AddComponent<CanvasGroup>();
                        cell.Btn.Text.fontSize = 24; cell.Btn.Text.resizeTextMaxSize = 24; cell.Btn.Text.resizeTextMinSize = 23; // an item without a picture: its name
                        cell.Icon = UiKit.Icon(brt, Vector2.zero, new Vector2(72, 72));
                        cell.Count = UiKit.Label(brt, "", new Vector2(-4, -CellH / 2f + 17), new Vector2(CellW - 12, 30), 24, TextAnchor.MiddleRight, Theme.Accent);
                        cell.Count.fontStyle = FontStyle.Bold;
                        cell.Mark = UiKit.Label(brt, "", new Vector2(4, CellH / 2f - 17), new Vector2(CellW - 12, 30), 24, TextAnchor.MiddleLeft, Theme.Good);
                        cell.Mark.horizontalOverflow = HorizontalWrapMode.Overflow;
                        cell.Lock = UiKit.HoverOutline(brt); cell.Lock.name = "lock";
                        cell.Pick = UiKit.HoverOutline(brt); cell.Pick.name = "pick";
                        cell.Btn.gameObject.SetActive(false);
                        _cells[at] = cell;
                    }
                }
                var pg = _pages[s] = Facet("page" + s, PageY, new Vector2(PocketMin, PageH));
                _pageBtns[s] = UiKit.Button(pg.Rt, "", Vector2.zero, new Vector2(PocketMin - 2f * PocketPad, PageH), () => NextPage(sec), 24);
                _pageBtns[s].Text.resizeTextMinSize = 23; _pageBtns[s].Stick = stick;
                _pageBtns[s].Tip = () => Lang.T("~inv~Next page");
            }
            BuildFoot();
            // the rolled end(s), over the cells' right end; the left one only while the bundle springs open
            _rollP = Facet("roll", 0f, new Vector2(2f * RollR, StripH + 2f * _outset), false);
            _rollR = Roll(_rollP.Rt, false);
            _rollLP = Facet("rollLeft", 0f, new Vector2(2f * RollR, StripH + 2f * _outset), false);
            _rollL = Roll(_rollLP.Rt, true); _rollLP.Rt.gameObject.SetActive(false);
            _plate = UiKit.TitlePlate(_root, Lang.T("Inventory"), 34, 420f);
            _plateRt = (RectTransform)_plate.transform;
            // the X and the back arrow stand over the frame's top edge at its two ends (the board's own buttons, borrowed while shown)
            _xHost = Facet("xHost", Top + _outset + 6f + 47f, new Vector2(120, 100), false);
            _backHost = Facet("backHost", Top + _outset + 6f + 52f, new Vector2(124, 108), false);
            BuildTab();
            // the sort / filter list: one parchment card, moved to the section that asks
            _popup = UiKit.Card(_hosts[0].Rt, "popup", Vector2.zero, new Vector2(PopW, 100f));
            for (int i = 0; i < 12; i++)
            {
                int row = i;
                var b = UiKit.Button(_popup, "", Vector2.zero, new Vector2(PopW - 20f, PopRowH - 6f), () => PopupPick(row), 24);
                b.Text.resizeTextMinSize = 23; b.Priority = 10; b.gameObject.SetActive(false);
                _popRows.Add(b);
            }
            // the sort list's last line: the section's columns, a - and a + beside the number
            _colsLabel = UiKit.Label(_popup, "", Vector2.zero, new Vector2(PopW - 20f - 2f * 62f, PopRowH - 6f), 24, TextAnchor.MiddleCenter, Theme.Ink);
            ColsMinus = UiKit.Button(_popup, "−", Vector2.zero, new Vector2(56f, PopRowH - 6f), () => { if (PopupOpen) SetCols(_popSec, _cols[_popSec] - 1); }, 28);
            ColsPlus = UiKit.Button(_popup, "+", Vector2.zero, new Vector2(56f, PopRowH - 6f), () => { if (PopupOpen) SetCols(_popSec, _cols[_popSec] + 1); }, 28);
            ColsMinus.Priority = ColsPlus.Priority = 10;
            ColsMinus.Tip = () => Lang.T("~inv~Fewer columns"); ColsPlus.Tip = () => Lang.T("~inv~More columns");
            _popup.gameObject.SetActive(false);
            BuildEquip();
            BuildCards();
            _driver = _root.gameObject.AddComponent<InvDrawDriver>(); _driver.Panel = this;
            _root.gameObject.AddComponent<InvDrawEarly>().Panel = this;
            Relayout();
            _root.gameObject.SetActive(false);
        }
        private const float BackSlice = 260f;
        private readonly InvDrawDriver _driver;

        private UiButton Round(RectTransform parent, string icon, Vector2 pos, float d, Action act)
        {
            var b = UiKit.RoundButton(parent, "", pos, new Vector2(d, d), act, 26);
            b.name = "btn:" + icon;
            var img = UiKit.Icon((RectTransform)b.transform, Vector2.zero, new Vector2(d * 0.56f, d * 0.56f));
            img.sprite = InvIcons.Get(icon); img.color = Theme.Ink; img.enabled = true;
            return b;
        }
        private RollGraphic Roll(RectTransform rt, bool mirror)
        {
            var g = rt.gameObject.AddComponent<RollGraphic>();
            g.raycastTarget = false; if (UiKit.Overlay != null) g.material = UiKit.Overlay;
            g.R = RollR; g.Height = StripH + 2f * _outset - 6f; g.Mirror = mirror;
            return g;
        }

        // ---- the foot line: the look's eye, slots and weight, the toast, the Items group's other pages
        public const float FootBtnMax = 158f, FootBtnMin = 96f, FootBtnH = 60f, FootInfoW = 260f, FootToastW = 366f;
        private void BuildFoot()
        {
            _footEye = Facet("footEye", FootY, new Vector2(60, 60));
            _footInfo = Facet("footInfo", FootY, new Vector2(FootInfoW, FootH));
            _info = UiKit.Label(_footInfo.Rt, "", Vector2.zero, new Vector2(FootInfoW, FootH - 4f), 24, TextAnchor.MiddleLeft, Theme.InkDim);
            _info.verticalOverflow = VerticalWrapMode.Overflow; _info.horizontalOverflow = HorizontalWrapMode.Overflow; _info.lineSpacing = 0.9f;
            _footToast = Facet("footToast", FootY, new Vector2(FootToastW, FootH), false);
            for (int i = 0; i < 5; i++)
            {
                int entry = i + 1; // the Items group's entries after the bag: Equipment, Bank, Cash shop, Cart, Market
                var f = Facet("foot" + entry, FootY, new Vector2(FootBtnMax, FootBtnH));
                var b = UiKit.Button(f.Rt, "", Vector2.zero, new Vector2(FootBtnMax, FootBtnH), () => EntryPressed?.Invoke(entry), 24);
                b.Text.resizeTextMinSize = 23;
                _footBtns.Add(b); _footPs.Add(f);
            }
        }
        public UiButton FootButton(int entry) => entry >= 1 && entry <= _footBtns.Count ? _footBtns[entry - 1] : null;

        // ---- the cloth tab over the frame's top middle, its pin, and the equipment panel it pulls up
        public UiButton TabButton { get; private set; }
        public UiButton PinButton { get; private set; }
        /// <summary>The band's box; the full equipment screen's box (642 x 742 px) and where its page goes (610 x 710); the one that is
        /// up (not up: the one the tab would pull up).</summary>
        public RectTransform EquipBand { get; private set; }
        public RectTransform EquipFullBox { get; private set; }
        public RectTransform EquipPage { get; private set; }
        public RectTransform EquipBox => _equipUp != null ? _equipUp : EquipFull ? EquipFullBox : EquipBand;
        public bool EquipOpen => _equipUp != null;
        public bool EquipIsFull => EquipBox == EquipFullBox;
        private RectTransform _equipUp, _equipWas; private float _equipOutsetFull = 36f; private CanvasGroup _equipGroupFull;
        private Image _pinIcon; private Text _tabArrow; private CanvasGroup _equipGroup;

        private void BuildTab()
        {
            // the tab rises behind the frame's top edge, over the title plate: canvas, a seam, the helmet picture; the pin beside it
            _tabP = Facet("tab", Top + _outset + TabUp + TabH / 2f, new Vector2(TabW + PinD + 16f, TabH), false);
            TabButton = UiKit.Button(_tabP.Rt, "", new Vector2(-(PinD + 16f) / 2f, 0f), new Vector2(TabW, TabH), () => TabPressed?.Invoke(), 24);
            TabButton.name = "btn:tab"; TabButton.Normal = Theme.Canvas; TabButton.Hover = Theme.CellHover; TabButton.SetHover(false);
            if (TabButton.GetComponent<Image>() is ThemedImage raw) raw.Raw = true;
            var trt = (RectTransform)TabButton.transform;
            UiKit.Stitch(trt, Vector2.zero, new Vector2(TabW - 10f, TabH - 10f));
            var icon = UiKit.Icon(trt, new Vector2(-22f, 0f), new Vector2(46f, 46f)); icon.sprite = InvIcons.Get("helmet"); icon.color = Theme.Ink; icon.enabled = true;
            _tabArrow = UiKit.Label(trt, "▲", new Vector2(30f, 0f), new Vector2(40f, 40f), 24, TextAnchor.MiddleCenter, Theme.InkDim);
            TabButton.Tip = () => EquipOpen ? Lang.T("~inv~Fold the equipment") : Lang.T("~inv~Equipment: pull up");
            PinButton = UiKit.RoundButton(_tabP.Rt, "", new Vector2(TabW / 2f + 8f, 0f), new Vector2(PinD, PinD), () => PinPressed?.Invoke(), 24);
            PinButton.name = "btn:pin";
            _pinIcon = UiKit.Icon((RectTransform)PinButton.transform, Vector2.zero, new Vector2(PinD * 0.6f, PinD * 0.6f));
            _pinIcon.sprite = InvIcons.Get("pin"); _pinIcon.color = Theme.Ink; _pinIcon.enabled = true;
            PinButton.Tip = () => Pinned ? Lang.T("~inv~Pinned: opens with the inventory") : Lang.T("~inv~Pin: open with the inventory");
        }
        /// <summary>The equipment panel is pinned (kept with the account: World/UserSettings.cs invEquipPinned).</summary>
        public static bool Pinned => UserSettings.Prefs.invEquipPinned;
        public static void SetPinned(bool on)
        {
            if (UserSettings.Prefs.invEquipPinned == on) return;
            UserSettings.Prefs.invEquipPinned = on; UserSettings.Prefs.saved = true; UserSettings.Save();
            Debug.Log($"[Inv] the equipment panel {(on ? "pinned" : "unpinned")}");
        }

        /// <summary>A framed panel of the inventory's own canvas (the laser stops on its rectangle).</summary>
        private RectTransform EquipHost(string name, Vector2 size, out float outset, out CanvasGroup group)
        {
            var box = Node(_root, name, size);
            box.gameObject.AddComponent<UiFacet>();
            outset = UiKit.Dress(box, size, MenuBoard.MetresPerPx, PanelLook.Framed);
            var col = box.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(size.x, size.y, 4f); col.center = new Vector3(0, 0, 3f);
            box.gameObject.AddComponent<UiBlocker>();
            group = box.gameObject.AddComponent<CanvasGroup>();
            return box;
        }
        private void BuildEquip()
        {
            EquipFullBox = EquipHost("equipFull", new Vector2(EquipW, EquipH), out _equipOutsetFull, out _equipGroupFull);
            EquipPage = Node(EquipFullBox, "page", new Vector2(EquipPageW, EquipPageH));
            EquipFullBox.gameObject.SetActive(false);
            EquipBand = EquipHost("equipBand", new Vector2(BandW, BandH), out _equipOutset, out _equipGroup);
            BuildBand();
            EquipBand.gameObject.SetActive(false);
        }

        // ---- the band: the character in the middle, its slots as cells (the worn item's picture, the slot's name), Take off, the
        // other group, the line
        private sealed class BandCell { public UiButton Btn; public Image Icon; public Text Name; public uint Bit; public int Side, Place; }
        private readonly List<BandCell> _bandCells = new List<BandCell>();
        private CharRowPreview _bandDoll; private Text _bandLine;
        /// <summary>The group whose slots the band shows (Gear; Costume and shadow).</summary>
        public EquipmentWindow.Group BandGroup { get; private set; } = EquipmentWindow.Group.Gear;
        /// <summary>A slot's cell was pressed (its position bit); Take off.</summary>
        public Action<uint> BandPressed; public Action BandTakeOffPressed;
        public UiButton BandTakeOff { get; private set; }
        public UiButton BandGroupButton { get; private set; }
        private BandCell BandCellOf(uint bit) { foreach (var c in _bandCells) if (c.Bit == bit && c.Btn.gameObject.activeSelf) return c; return null; }
        /// <summary>A slot's cell on the band as it is drawn now (null: the group shown has no such slot): its button, its name, whether
        /// it shows a worn item's picture; the line under the cells; the character's picture.</summary>
        public UiButton BandButton(uint bit) => BandCellOf(bit)?.Btn;
        public Text BandName(uint bit) => BandCellOf(bit)?.Name;
        public bool BandHasIcon(uint bit) { var c = BandCellOf(bit); return c != null && c.Icon.enabled; }
        public Text BandLine => _bandLine;
        public CharRowPreview BandDoll => _bandDoll;

        private void BuildBand()
        {
            var b = EquipBand; float x1 = BandDollW / 2f + 12f + BandCellW / 2f, x2 = x1 + BandCellW + 6f, top = BandH / 2f - BandPad, bottom = top - 3f * BandPitch + 6f;
            // two blocks of two columns by three rows. The equipment screen's left column goes to the left block, its right column to
            // the right block (UI/EquipmentWindow.cs Slots: Col, Row), each filled row by row from its OUTER column inward, so the two
            // blocks mirror each other: outer columns head top / right hand / right accessory and head middle / left hand / left
            // accessory; inner columns head low / garment / ammunition and armor / shoes / Take off.
            for (int side = 0; side < 2; side++)
                for (int place = 0; place < 6; place++)
                {
                    int row = place / 2, col = place % 2; float x = (side == 0 ? -1f : 1f) * (col == 0 ? x2 : x1), y = top - BandCellH / 2f - row * BandPitch;
                    if (side == 1 && place == 5)
                    {
                        BandTakeOff = UiKit.Button(b, Lang.T("Take off"), new Vector2(x, y), new Vector2(BandCellW, BandCellH), () => BandTakeOffPressed?.Invoke(), BandFont);
                        BandTakeOff.Text.resizeTextMinSize = BandFont;
                        continue;
                    }
                    var cell = new BandCell { Side = side, Place = place };
                    cell.Btn = UiKit.Button(b, "", new Vector2(x, y), new Vector2(BandCellW, BandCellH), () => { if (cell.Bit != 0) BandPressed?.Invoke(cell.Bit); }, 18);
                    cell.Btn.name = "slot" + side + place;
                    if (cell.Btn.GetComponent<Image>() is ThemedImage raw) raw.Raw = true; // (its fills are the theme's own: drawn as they are)
                    var brt = (RectTransform)cell.Btn.transform;
                    cell.Icon = UiKit.Icon(brt, new Vector2(-BandCellW / 2f + 34f, 0f), new Vector2(52f, 52f));
                    cell.Name = UiKit.Label(brt, "", new Vector2(31f, 0f), new Vector2(BandCellW - 70f, BandCellH - 6f), BandFont, TextAnchor.MiddleLeft, Theme.Ink);
                    cell.Name.horizontalOverflow = HorizontalWrapMode.Wrap; cell.Name.verticalOverflow = VerticalWrapMode.Overflow; cell.Name.lineSpacing = 0.85f;
                    _bandCells.Add(cell);
                }
            // the character as the equipment screen draws it; under it the button to the other group's slots
            float groupH = 78f, dollH = top - bottom - groupH - 6f, dollY = top - dollH / 2f; // (78 px: 5 degrees from the eyes, the boards' smallest button)
            UiKit.Raw(UiKit.Soft(b, "dollbg", new Vector2(0f, dollY), new Vector2(BandDollW, dollH), Theme.CanvasShade));
            _bandDoll = CharRowPreview.Create(b, new Vector2(0f, dollY), new Vector2(BandDollW - 8f, dollH - 8f), EquipmentWindow.DollScale);
            _bandDoll.gameObject.name = "w21:doll";
            BandGroupButton = UiKit.Button(b, "", new Vector2(0f, bottom + groupH / 2f), new Vector2(BandDollW, groupH), () => { BandGroup = BandGroup == EquipmentWindow.Group.Gear ? EquipmentWindow.Group.CostumeShadow : EquipmentWindow.Group.Gear; Dirty = true; }, 22);
            BandGroupButton.name = "btn:group"; BandGroupButton.Text.resizeTextMinSize = 22; BandGroupButton.Text.lineSpacing = 0.85f;
            // the line: what the slot under the pointer (or the armed one) holds
            float lineY = bottom - 8f - BandLineH / 2f;
            UiKit.Raw(UiKit.Soft(b, "linebg", new Vector2(0f, lineY), new Vector2(BandW - 2f * BandPad, BandLineH), Theme.CanvasShade));
            _bandLine = UiKit.Label(b, "", new Vector2(0f, lineY), new Vector2(BandW - 2f * BandPad - 16f, BandLineH), BandFont, TextAnchor.MiddleCenter, Theme.Ink);
            _bandLine.horizontalOverflow = HorizontalWrapMode.Wrap; _bandLine.verticalOverflow = VerticalWrapMode.Overflow; _bandLine.lineSpacing = 0.85f;
        }

        /// <summary>The band drawn again: each slot of the group shown on its place (its worn item's picture, the slot's name; the
        /// ammunition's count), the armed one gold; Take off for an armed slot that wears something; the character's look.</summary>
        public void RefreshBand(Inventory inv, uint armed)
        {
            if (EquipBand == null || !EquipBand.gameObject.activeSelf) return;
            foreach (var c in _bandCells) c.Bit = 0;
            foreach (var sl in EquipmentWindow.Slots)
            {
                if (sl.Group != BandGroup) continue;
                int side = sl.Col == 1 ? 1 : 0, place = sl.Col == 2 ? 5 : sl.Row;
                foreach (var c in _bandCells) if (c.Side == side && c.Place == place) c.Bit = sl.Bit;
            }
            bool wearing = false;
            foreach (var c in _bandCells)
            {
                bool on = c.Bit != 0; if (c.Btn.gameObject.activeSelf != on) c.Btn.gameObject.SetActive(on);
                if (!on) continue;
                var e = inv.WornAt(c.Bit);
                c.Icon.sprite = e != null ? WristMenu.ItemIcon(e.Info) : null; c.Icon.enabled = c.Icon.sprite != null;
                c.Name.text = EquipmentWindow.Shown(EquipmentWindow.SlotOf(c.Bit)) + (e != null && c.Bit == Inventory.AmmoSlot && e.Amount > 1 ? "\n×" + e.Amount : "");
                c.Name.color = e != null ? Theme.Ink : Theme.InkDim;
                c.Btn.Normal = c.Bit == armed ? Theme.Lit : e != null ? Theme.Cell : Theme.CellOff;
                if (!c.Btn.Hovered) c.Btn.SetHover(false);
                if (c.Bit == armed && e != null) wearing = true;
            }
            BandTakeOff.Interactable = wearing; if (!BandTakeOff.Hovered) BandTakeOff.SetHover(false);
            BandGroupButton.SetLabel(BandGroup == EquipmentWindow.Group.Gear ? Lang.T("Costume · shadow") : Lang.T("Gear"));
            if (!BandGroupButton.Hovered) BandGroupButton.SetHover(false);
            _bandDoll.ShowLook(EquipmentWindow.SelfLook?.Invoke(), CharRowPreview.Pose.Idle);
        }
        /// <summary>The band's line (what the slot under the pointer, or the armed one, holds).</summary>
        public void SetBandLine(string text) { if (_bandLine != null && _bandLine.text != text) _bandLine.text = text ?? ""; }
        private float _equipOutset = 36f;

        // ---- the cards: the item whole on parchment, what is worn where it would go beside it (UI/ItemPanel.cs)
        private void BuildCards()
        {
            CardItem = ItemPanel.Create(_root, "invItem", true);
            var row = CardItem.ButtonRow; float bw = (ItemPanel.TextW - 3 * 8) / 4f;
            float X(int i) => -ItemPanel.TextW / 2 + bw / 2 + i * (bw + 8);
            CardAct = UiKit.Button(row, "", new Vector2(X(0), 0), new Vector2(bw, ItemPanel.ButtonH), () => CardButton?.Invoke("act"), 24);
            CardDetails = UiKit.Button(row, Lang.T("Details"), new Vector2(X(1), 0), new Vector2(bw, ItemPanel.ButtonH), () => CardButton?.Invoke("details"), 24);
            CardPin = UiKit.Button(row, Lang.T("★ Quick slot"), new Vector2(X(2), 0), new Vector2(bw, ItemPanel.ButtonH), () => CardButton?.Invoke("pin"), 24);
            CardUnlock = UiKit.Button(row, Lang.T("Unlock"), new Vector2(X(3), 0), new Vector2(bw, ItemPanel.ButtonH), () => CardButton?.Invoke("unlock"), 24);
            foreach (var b in new[] { CardAct, CardDetails, CardPin, CardUnlock }) { b.Text.resizeTextMinSize = 21; b.Priority = 5; }
            CardWornA = ItemPanel.Create(_root, "invWornA", false); CardWornB = ItemPanel.Create(_root, "invWornB", false);
            // client wave 23 (the merge), item 6's cost: beside the UPRIGHT strip the cards stand farther from the eyes than beside the leaning
            // one, and a description at the boards' 21 px floor was 1.26 degrees from the eyes (w21inv L; the floor is 1.3): this board's
            // cards stop at 23 px (22 px gave 1.297 degrees on development build 7)
            CardItem.Floor = CardWornA.Floor = CardWornB.Floor = CardFontFloor;
        }
        /// <summary>A button of the item's card was pressed: "act", "details", "pin", "unlock".</summary>
        public Action<string> CardButton;

        // ------------------------------------------------------------------ the layout for the sections' columns
        /// <summary>Lays the strip out for the sections' columns now (their stored choice): the pockets' places and widths, the columns shown,
        /// the foot line, the strip's width; then everything is stood on the cylinder again.</summary>
        public void Relayout()
        {
            for (int s = 0; s < Sections; s++) _cols[s] = StoredCols(s);
            _w = WidthFor(_cols);
            float x = -_w / 2f + MarginL;
            for (int s = 0; s < Sections; s++) { _pw[s] = Mathf.Max(PocketMin, _cols[s] * CellW + (_cols[s] - 1) * CellGap + 2f * PocketPad); _pl[s] = x; x += _pw[s] + PocketGap; }
            _strip.Clear();
            for (int s = 0; s < Sections; s++)
            {
                float mid = PocketMid(s), pw = _pw[s];
                Put(_hosts[s], mid, pw); _hosts[s].Rt.sizeDelta = new Vector2(pw, StripH);
                Put(_heads[s], mid, pw); _heads[s].Rt.sizeDelta = new Vector2(pw, HeadH);
                _names[s].rectTransform.sizeDelta = new Vector2(pw + PocketGap - 2f, NameH + 2f);
                _strip.Add((_sortBtns[s], mid - BtnD / 2f - 7f, BtnY, false)); _strip.Add((_filterBtns[s], mid + BtnD / 2f + 7f, BtnY, false));
                for (int c = 0; c < MaxCols; c++)
                {
                    bool on = c < _cols[s]; var p = _colPs[s, c];
                    if (p.Rt.gameObject.activeSelf != on) p.Rt.gameObject.SetActive(on);
                    if (!on) { for (int r = 0; r < Rows; r++) _cells[CellAt(s, r, c)].Index = -1; continue; }
                    Put(p, ColX(s, c), CellW);
                    for (int r = 0; r < Rows; r++) _strip.Add((_cells[CellAt(s, r, c)].Btn, ColX(s, c), GridTop - CellH / 2f - r * (CellH + CellGap), true));
                }
                Put(_pages[s], mid, pw); _pages[s].Rt.sizeDelta = new Vector2(pw, PageH);
                _pageBtns[s].Resize(new Vector2(pw - 2f * PocketPad, PageH));
                _strip.Add((_pageBtns[s], mid, PageY, false));
            }
            // the foot line: the eye, slots and weight, the toast where it fits (else under the frame), the five buttons at the right
            float left = -_w / 2f + MarginL, right = _w / 2f - RollW - MarginR, avail = right - left;
            Put(_footEye, left + 30f, 60f); Put(_footInfo, left + 64f + FootInfoW / 2f, FootInfoW);
            float rest = avail - 64f - FootInfoW - 8f;
            bool toastIn = rest >= 5f * (FootBtnMax + 8f) + 240f;
            float bw = Mathf.Clamp(((toastIn ? rest - 248f : rest) - 5f * 8f) / 5f, FootBtnMin, FootBtnMax);
            float bx = right - 5f * bw - 4f * 8f + bw / 2f;
            for (int i = 0; i < _footBtns.Count; i++)
            {
                Put(_footPs[i], bx + i * (bw + 8f), bw); _footPs[i].Rt.sizeDelta = new Vector2(bw, FootBtnH); _footBtns[i].Resize(new Vector2(bw, FootBtnH));
                _strip.Add((_footBtns[i], bx + i * (bw + 8f), FootY, false));
            }
            float toastW = toastIn ? Mathf.Min(FootToastW, bx - bw / 2f - 8f - (left + 64f + FootInfoW + 8f)) : FootToastW;
            if (toastIn) { _footToast.Y = FootY; Put(_footToast, left + 64f + FootInfoW + 8f + toastW / 2f, toastW); }
            else { _footToast.Y = -StripH / 2f - _outset - 8f - FootH / 2f; Put(_footToast, 0f, toastW); }
            _footToast.Rt.sizeDelta = new Vector2(toastW, FootH);
            if (_shown && _board.Toast != null) _board.Toast.rectTransform.sizeDelta = new Vector2(toastW, FootH - 6f);
            // the back slices under the whole width, the roll(s), the X and the arrow at the two ends, the tab in the middle
            float span = _w; int need = Mathf.CeilToInt(span / BackSlice);
            for (int i = 0; i < _backs.Count; i++)
            {
                bool on = i < need; if (_backs[i].Rt.gameObject.activeSelf != on) _backs[i].Rt.gameObject.SetActive(on);
                if (on) Put(_backs[i], -span / 2f + span / need * (i + 0.5f), BackSlice);
            }
            _rollP.X = RollX; _rollLP.X = -RollX;
            _xHost.X = _w / 2f - 60f; _backHost.X = -_w / 2f + 64f; _tabP.X = 0f;
            if (_shown) _board.Root.sizeDelta = new Vector2(_w, StripH);
            if (_mode == Mode.Open) { _xl = -_w / 2f; _xr = _w / 2f; }
            ClosePopupQuiet();
            Rebend(_eyeLocal);
            Dirty = true;
            Debug.Log($"[Inv] laid out: columns {string.Join(" ", _cols)}, the strip {_w:0} px ({_w * MenuBoard.MetresPerPx:0.00} m), {_w / _radius * Mathf.Rad2Deg:0.0} degrees of its circle");
        }
        private static void Put(Placed p, float x, float width) { p.X = x; p.Half = width * 0.5f; }

        // ------------------------------------------------------------------ shown / hidden: the board's own picture swapped for this one
        private void Borrow(Component c, RectTransform host, Vector2 pos)
        {
            if (c == null) return; var t = c.transform;
            _borrowed.Add((t, t.parent, t.localPosition, t.localRotation, t.GetSiblingIndex()));
            t.SetParent(host, false); t.localRotation = Quaternion.identity; t.localPosition = new Vector3(pos.x, pos.y, 0f);
        }

        /// <summary>Shows the inventory in place of the board's own picture (or gives the board back).</summary>
        public void Show(bool on)
        {
            if (on == _shown) return;
            _shown = on;
            var br = _board.Root;
            if (on)
            {
                _boardSize = br.sizeDelta; _hidden.Clear(); _borrowed.Clear();
                foreach (var n in new[] { "border", "bg", "rule" }) { var t = br.Find(n); if (t != null && t.gameObject.activeSelf) { t.gameObject.SetActive(false); _hidden.Add(t.gameObject); } }
                if (_board.Content.gameObject.activeSelf) { _board.Content.gameObject.SetActive(false); _hidden.Add(_board.Content.gameObject); }
                var col = br.GetComponent<BoxCollider>(); if (col != null) col.enabled = false;
                br.sizeDelta = new Vector2(_w, StripH);
                Borrow(_board.CloseButton, _xHost.Rt, Vector2.zero); Borrow(_board.BackButton, _backHost.Rt, Vector2.zero);
                Borrow(_board.Eye, _footEye.Rt, Vector2.zero); Borrow(_board.Toast, _footToast.Rt, Vector2.zero);
                _toastSize = _board.Toast.rectTransform.sizeDelta; _board.Toast.rectTransform.sizeDelta = new Vector2(_footToast.Rt.sizeDelta.x, FootH - 6f);
                var bend = br.GetComponent<UiBend>(); if (bend == null) bend = br.gameObject.AddComponent<UiBend>();
                bend.Radius = _radius;
                _root.gameObject.SetActive(true); _root.SetAsLastSibling();
                FollowTheme(); // (client waves 21 + 22, the merge: the look in force before the first picture)
                Relayout(); // (the account's columns may have come from another PC meanwhile)
                Dirty = true;
            }
            else
            {
                ClosePopup(); HideCards(); ShowEquip(false, 0f); SetHighlight(null);
                _driver.Restore();
                _root.gameObject.SetActive(false);
                for (int i = _borrowed.Count - 1; i >= 0; i--)
                {
                    var (t, parent, pos, rot, sib) = _borrowed[i];
                    if (t == null || parent == null) continue;
                    t.SetParent(parent, false); t.localPosition = pos; t.localRotation = rot; t.SetSiblingIndex(Mathf.Min(sib, parent.childCount - 1));
                }
                _borrowed.Clear();
                _board.Toast.rectTransform.sizeDelta = _toastSize;
                foreach (var g in _hidden) if (g != null) g.SetActive(true);
                _hidden.Clear();
                var col = br.GetComponent<BoxCollider>(); if (col != null) col.enabled = true;
                br.sizeDelta = _boardSize;
                var bend = br.GetComponent<UiBend>(); if (bend != null) { bend.Radius = 0f; bend.ClearSpan(); bend.SetDraw(Vector3.zero, Quaternion.identity, 1f); }
                _mode = Mode.Open; _xl = -_w / 2f; _xr = _w / 2f;
            }
        }

        /// <summary>The cylinder's axis through the eyes: <paramref name="eyeLocal"/> is where they are in the board's own frame (px).
        /// Everything is stood on the cylinder again. Client wave 23 (w23inv): the strip stands UPRIGHT in front of the eyes
        /// (UI/WristMenu.Inv.cs InvStand; until then the board leaned to face them, so they were on its own level), so its axis is as
        /// far from its middle as the eyes are ACROSS its own vertical, whatever their height against it.</summary>
        public void Rebend(Vector3 eyeLocal)
        {
            _eyeLocal = eyeLocal;
            float r = eyeLocal.z < -50f ? new Vector2(eyeLocal.x, eyeLocal.z).magnitude : RadiusMin;
            r = Mathf.Clamp(Mathf.Max(r, RadiusMin, _w / (MaxWrap * Mathf.Deg2Rad)), RadiusMin, RadiusMax);
            _radius = r;
            _frame.Radius = r; _canvas.Radius = r; _stitch.Radius = r;
            foreach (var p in _all) Bend.Place(p.Rt, r, p.X, p.Y);
            // the equipment band (and the full screen) on the strip's top edge at its middle, leaning about that edge to face the eyes
            // of this placing; the tab on the strip's top edge, or on the top edge of the one that is up
            BandLean = PlaceEquip(EquipBand, BandH / 2f + _equipOutset, out _bandHome);
            FullLean = PlaceEquip(EquipFullBox, EquipH / 2f + _equipOutsetFull, out _fullHome);
            PlaceTab();
            var bend = _board.Root.GetComponent<UiBend>(); if (bend != null && _shown) bend.Radius = r;
            ApplyPose();
            Lay(); _cardsLaid = false;
            Debug.Log($"[Inv] bent round the eyes: radius {r * Mathf.Abs(_board.Root.lossyScale.x):0.000} m ({r:0} px), the strip {_w / r * Mathf.Rad2Deg:0.0} degrees wide; the eyes {eyeLocal.y:0} px over its middle; the equipment band leans {BandLean:0.0} degrees");
        }
        /// <summary>Stands an equipment panel (flat; <paramref name="half"/> px from its middle to its frame's lower and upper outer
        /// edge) on the strip's top edge at the strip's middle: the middle of its lower frame edge in the strip's own plane,
        /// <see cref="EquipTuck"/> px behind the strip's frame; it leans about that edge until its middle faces the eyes of the
        /// placing (they stand as far in front of the strip as they are across its vertical, and over or under the edge by the
        /// board's height against them). Returns the lean: degrees its top comes towards the eyes (negative: away from them, a
        /// strip placed far under the eyes).</summary>
        private float PlaceEquip(RectTransform box, float half, out Vector3 home)
        {
            float seam = Top + _outset - EquipTuck;
            bool known = _eyeLocal.z < -50f;
            float ahead = known ? Mathf.Max(100f, new Vector2(_eyeLocal.x, _eyeLocal.z).magnitude) : _radius, over = seam - (known ? _eyeLocal.y : 0f);
            float far = Mathf.Sqrt(ahead * ahead + over * over);
            float lean = Mathf.Clamp((Mathf.Atan2(over, ahead) + Mathf.Asin(Mathf.Clamp01(half / far))) * Mathf.Rad2Deg, EquipLeanMin, EquipLeanMax);
            var rot = Quaternion.Euler(-lean, 0f, 0f);
            home = new Vector3(0f, seam, 0f) + rot * Vector3.up * half;
            box.localPosition = home; box.localRotation = rot; box.localScale = Vector3.one;
            return lean;
        }
        /// <summary>How far the band (the full screen) leans towards the eyes of the placing, degrees (<see cref="PlaceEquip"/>); of
        /// the one that is up (or would be pulled up): its lean, its rectangle (px, without its frame) and its frame's width.</summary>
        public float BandLean { get; private set; }
        public float FullLean { get; private set; }
        public float EquipLean => EquipIsFull ? FullLean : BandLean;
        /// <summary>The height (px over the strip's middle, in the strip's own plane) at which the equipment panel's lower frame edge
        /// stands; the eyes of the placing in the board's own px (smoke, log).</summary>
        public float EquipSeam => Top + _outset - EquipTuck;
        public Vector3 EyeLocal => _eyeLocal;
        public Vector2 EquipSize => EquipIsFull ? new Vector2(EquipW, EquipH) : new Vector2(BandW, BandH);
        public float EquipOutset => EquipIsFull ? _equipOutsetFull : _equipOutset;
        private Vector3 _bandHome, _fullHome;
        private Vector3 EquipHome(RectTransform box) => box == EquipFullBox ? _fullHome : _bandHome;
        /// <summary>From an equipment panel's middle to its frame's outer edge, up or down (px).</summary>
        private float EquipHalf(RectTransform box) => box == EquipFullBox ? EquipH / 2f + _equipOutsetFull : BandH / 2f + _equipOutset;

        /// <summary>Where the cloth tab (with its pin) stands in the drawing's frame: sewn to the strip's top edge
        /// (<paramref name="box"/> null), or carried up on the top edge of that equipment panel, leaning as it does.</summary>
        private void TabPose(RectTransform box, out Vector3 pos, out Quaternion rot)
        {
            if (box == null) { pos = Bend.Point(_radius, _tabP.X, _tabP.Y); rot = Bend.Turn(_radius, _tabP.X); return; }
            rot = box.localRotation; pos = EquipHome(box) + rot * Vector3.up * (EquipHalf(box) + TabH / 2f - EquipTuck);
        }
        private void PlaceTab() { TabPose(_equipUp, out var pos, out var rot); _tabP.Rt.localPosition = pos; _tabP.Rt.localRotation = rot; }

        /// <summary>The drawing's own pose in the board's frame: small about the held point while it is the bundle; else the board's own.</summary>
        private float _shrink = 1f;
        private void ApplyPose()
        {
            var pos = HoldPoint * (1f - _shrink);
            _root.localPosition = pos; _root.localRotation = Quaternion.identity; _root.localScale = Vector3.one * _shrink;
            var bend = _board.Root.GetComponent<UiBend>(); if (bend != null) bend.SetDraw(pos, Quaternion.identity, _shrink);
        }

        // ------------------------------------------------------------------ the opening: unrolled from the left, or sprung out of the bundle
        /// <summary>The clock of the opening and of the pointer's rest (seconds; real time). A recording that advances the game's time
        /// frame by frame gives its own (LokiGame.W21InvShow.cs).</summary>
        public static Func<float> Clock { get; set; } = () => Time.unscaledTime;
        public static float Now => Clock();
        public enum Mode { Open, Unroll, Compact, Spring }
        private Mode _mode = Mode.Open; private float _t0, _dur, _opened; private int _frames;
        private float _xl = -1000f, _xr = 1000f;
        public Mode State => _mode;
        public bool Animating => _mode == Mode.Unroll || _mode == Mode.Spring;
        /// <summary>The share of the strip that is open now (0-1), the last opening's length (s, from the frame it began to the frame it
        /// was whole) and how many were played (smoke, log).</summary>
        public float Open01 => Mathf.Clamp01((_xr - _xl) / _w);
        public float LastSeconds { get; private set; }
        public int Unrolls { get; private set; }
        public int Springs { get; private set; }
        public float RevealLeft => _xl; public float RevealRight => _xr;

        /// <summary>Whole at once (no animation: switched off, reopened within 5 s, a test run).</summary>
        public void OpenNow() { _mode = Mode.Open; _xl = -_w / 2f; _xr = _w / 2f; Lay(); Cover(); }
        /// <summary>Unrolls from the left end to the right in <paramref name="seconds"/>, with its sound.</summary>
        public void PlayUnroll(float seconds)
        {
            _mode = Mode.Unroll; _dur = Mathf.Max(0.05f, seconds); _opened = Now; _frames = 0; Unrolls++;
            _xl = -_w / 2f; _xr = _xl + RollW + 30f; Lay(); Cover();
            Sound(UnrollSounds);
            Debug.Log($"[Inv] unrolling, {_dur:0.00} s ({_w:0} px)");
        }
        /// <summary>The bundle out of the backpack: two rolls side by side under the title plate, nothing open yet.</summary>
        public void SetCompact() { _mode = Mode.Compact; _xl = -CompactHalf; _xr = CompactHalf; HideCards(); ShowEquip(false, 0f); Lay(); Cover(); Debug.Log("[Inv] compact (the bundle)"); }
        /// <summary>The bundle springs open to both sides (<paramref name="seconds"/>; 0: at once), with its sound.</summary>
        public void PlaySpring(float seconds)
        {
            Springs++; Sound(SpringSounds);
            if (seconds <= 0.01f) { OpenNow(); LastSeconds = 0f; Debug.Log("[Inv] sprung open at once"); return; }
            _mode = Mode.Spring; _dur = seconds; _opened = Now; _frames = 0; Lay(); Cover();
            Debug.Log($"[Inv] springing open, {_dur:0.00} s");
        }

        private static float OutBack(float x) { const float c1 = 1.1f, c3 = c1 + 1f; x = Mathf.Clamp01(x) - 1f; return 1f + c3 * x * x * x + c1 * x * x; }

        /// <summary>Client waves 21 + 22, the merge (w21int; w21inv x w21jobui): the bent frame, the bent canvas and the bent stitching wear
        /// the panel theme AS IT IS NOW (our job's variant, or Base: UI/JobTheme.cs). They are graphics of this panel's own kind, so
        /// JobTheme.Redress, which swaps the kit's own images and stitchings, never reaches them: unmerged they kept the pictures of the
        /// moment the panel was made (the base look) while every other panel wore the job's. One reference compare a frame: the theme's
        /// pictures are read again only when the look in force has changed (JobTheme.Applied: reading a picture through Theme marks
        /// JobTheme dirty, which re-dresses every friend's shared copies in that frame: never once a frame).</summary>
        private void FollowTheme()
        {
            object now = JobTheme.Applied; if (_themeFollowed && ReferenceEquals(now, _themeSeen)) return;
            _themeFollowed = true; _themeSeen = now;
            var f = Theme.Frame; if (!ReferenceEquals(_frame.Sprite, f)) _frame.Sprite = f;
            var c = Theme.CanvasFill; if (!ReferenceEquals(_canvas.Sprite, c)) _canvas.Sprite = c;
            var t = Theme.Thread; if (_stitch.color != t) _stitch.color = t;
        }
        private object _themeSeen; private bool _themeFollowed;

        /// <summary>Once a frame while shown: the look in force (FollowTheme), then the opening goes on.</summary>
        public void Tick()
        {
            FollowTheme();
            if (!Animating) return;
            float now = Now;
            _frames++;
            if (_frames == 1) { Lay(); return; }
            if (_frames == 2) _t0 = Mathf.Max(_opened, now - MenuAnim.MaxFirstStep); // (a long first frame takes at most this much of it, as UI/MenuAnim.cs)
            float t = Mathf.Clamp01((now - _t0) / _dur);
            if (_mode == Mode.Unroll) { float e = Mathf.SmoothStep(0f, 1f, t); _xl = -_w / 2f; _xr = Mathf.Lerp(-_w / 2f + RollW + 30f, _w / 2f, e); }
            else { float e = OutBack(t); _xl = Mathf.LerpUnclamped(-CompactHalf, -_w / 2f, e); _xr = Mathf.LerpUnclamped(CompactHalf, _w / 2f, e); }
            if (t >= 1f) { LastSeconds = now - _opened; var was = _mode; OpenNow(); Debug.Log($"[Inv] {(was == Mode.Unroll ? "unrolled" : "sprung open")} after {LastSeconds:0.000} s ({_frames} frames)"); return; }
            Lay();
        }

        /// <summary>Draws the strip between the two ends that are open now: the frame, the canvas, the seams, the roll(s), what has appeared.</summary>
        private void Lay()
        {
            float xl = _xl, xr = _xr, hy = StripH / 2f, r = _radius;
            _frame.SetRect(xl, xr, -hy, hy);
            _canvas.SetRect(xl + 3f, xr - 3f, -hy + 3f, hy - 3f);
            float inset = Theme.StitchInPx * _u;
            _stitch.Clear();
            _stitch.AddRect(xl + inset, xr - inset, -hy + inset, hy - inset);
            for (int s = 0; s < Sections; s++) _stitch.AddRect(_pl[s] + 1f, _pl[s] + _pw[s] - 1f, GridTop - GridH - 5f, GridTop + 5f);
            bool compact = _mode == Mode.Compact, spring = _mode == Mode.Spring, open = _mode == Mode.Open;
            float span = Mathf.Clamp01((xr - xl - 2f * CompactHalf) / (_w - 2f * CompactHalf));
            // the right roll rides the right end (a little fatter while there is canvas on it); the left one only for the bundle
            float rr = RollR * (open ? 1f : compact ? 1f : Mathf.Lerp(1.45f, 1f, span));
            float off = _w / 2f - RollX;
            _rollR.R = rr; Bend.Place(_rollP.Rt, r, xr - off - (rr - RollR), 0f);
            bool left = compact || spring;
            if (_rollLP.Rt.gameObject.activeSelf != left) _rollLP.Rt.gameObject.SetActive(left);
            if (left) { float rl = RollR * (compact ? 1f : Mathf.Max(0.04f, Mathf.Pow(1f - span, 0.7f))); _rollL.R = rl; Bend.Place(_rollLP.Rt, r, xl + off + (rl - RollR) * 0.5f, 0f); }
            float leftEdge = xl + (left ? 2f * _rollL.R : 0f);
            float rollLeftArc = xr - off - (rr - RollR) - rr;
            _stitch.Clip(leftEdge + 2f, open ? float.PositiveInfinity : rollLeftArc);
            foreach (var p in _all)
            {
                if (p.Group == null) continue;
                float a = open ? 1f : compact ? 0f : Mathf.Clamp01((rollLeftArc - (p.X - p.Half)) / (2f * p.Half)) * Mathf.Clamp01((p.X + p.Half - leftEdge) / (2f * p.Half));
                a = a >= 0.999f ? 1f : a * a;
                if (p.Group.alpha != a) p.Group.alpha = a;
            }
            // the title plate over the middle of what is open; the X, the back arrow, the tab and the toast with the whole strip only
            float plateY = hy + (Theme.LeatherPx + Theme.WoodPx * 0.5f) * _u;
            Bend.Place(_plateRt, r, (xl + xr) * 0.5f, plateY);
            foreach (var p in new[] { _xHost, _backHost, _tabP, _footToast }) if (p.Rt.gameObject.activeSelf != open) p.Rt.gameObject.SetActive(open);
            // the bundle is small in the hand and grows to the strip's size as it springs open, about the point it is held by
            float k = compact ? CompactScale : spring ? Mathf.Lerp(CompactScale, 1f, Mathf.SmoothStep(0f, 1f, span)) : 1f;
            if (_shrink != k) { _shrink = k; ApplyPose(); }
            var bend = _board.Root.GetComponent<UiBend>(); // what of the strip is there now (the grab's edge and its outline follow it: UI/PanelGrab.cs)
            if (bend != null) { if (open) bend.ClearSpan(); else bend.SetSpan(xl, xr); }
        }

        // ---- the sounds: the install's own, chosen by name, the first that is there (never shipped audio)
        public static readonly string[] UnrollSounds = { "se_equip_item_01.wav", "effect\\assasin_cloaking.wav", "ef_beginspell.wav" };
        public static readonly string[] SpringSounds = { "effect\\assasin_cloaking.wav", "se_equip_item_01.wav", "ef_beginspell.wav" };
        public const float SoundVolume = 0.8f;
        private static readonly Dictionary<string, bool> Has = new Dictionary<string, bool>();
        public int SoundsPlayed { get; private set; }
        public string LastSound { get; private set; } = "";
        /// <summary>The first of the names that the install has (null: none, or no audio).</summary>
        public static string Pick(string[] names)
        {
            var a = Audio.LokiAudio.Instance; if (a == null) return null;
            foreach (var n in names)
            {
                if (!Has.TryGetValue(n, out bool has)) { has = a.HasWav(n); Has[n] = has; if (!has) Debug.Log($"[Inv] sound {n} is not in the game data: not played"); }
                if (has) return n;
            }
            return null;
        }
        private void Sound(string[] names)
        {
            string n = Pick(names); if (n == null) return;
            Audio.LokiAudio.Instance.PlayWav(n, null, SoundVolume);
            SoundsPlayed++; LastSound = n;
            Debug.Log($"[Inv] sound {n}");
        }

        // ------------------------------------------------------------------ the equipment panel: pulled up by the tab
        private float _eqT0 = -100f, _eqDur; private bool _eqPull;
        /// <summary>The panel's pull is playing (the picture only: its buttons are at their places).</summary>
        public bool EquipPulling => _eqPull;
        public int EquipPulls { get; private set; }
        public float EquipLastSeconds { get; private set; }

        /// <summary>Pulls the equipment panel up above the strip's middle (<paramref name="seconds"/>: its cloth unfurls upward and
        /// carries the tab up; 0: at once), or folds it (the tab comes back down to the strip's top edge). The strip itself stays
        /// where it stands (client wave 23: see <see cref="EquipSeconds"/>'s summary).</summary>
        public void ShowEquip(bool on, float seconds)
        {
            if (EquipBand == null || on == EquipOpen) return;
            _driver.Restore();
            if (on) { _equipUp = EquipFull ? EquipFullBox : EquipBand; _equipWas = _equipUp; _equipUp.gameObject.SetActive(true); }
            else { _equipWas = _equipUp; _equipUp.gameObject.SetActive(false); _equipUp = null; }
            PlaceTab();
            _eqPull = seconds > 0.01f; _eqDur = seconds; _eqT0 = Now;
            if (on) { _equipUp.SetAsFirstSibling(); if (_eqPull) EquipPulls++; } // (drawn behind the strip: it rises from behind the strip's top edge; the tab, a later sibling, is drawn on its top edge)
            _equipGroup.alpha = 1f; _equipGroupFull.alpha = 1f;
            if (_tabArrow != null) _tabArrow.text = on ? "▼" : "▲";
            _cardsLaid = false; Dirty = true;
            Debug.Log($"[Inv] the equipment panel {(on ? "pulled up" : "folded")}{(_eqPull ? $", {seconds:0.00} s" : " at once")}; the tab {(on ? "on its top edge" : "back on the strip's top edge")}");
        }

        /// <summary>After every script's LateUpdate (InvDrawDriver): the pull's picture for this frame: the panel growing from its lower
        /// edge, the tab on its way between the strip's top edge and the panel's (up with the pull, back down when it is folded); put
        /// back when the frame has been drawn. The true poses (the colliders) are the final ones from the first frame.</summary>
        internal bool DrawPose(out Vector3 boxPos, out Vector3 boxScale, out float alpha, out Vector3 tabPos, out Quaternion tabRot)
        {
            boxPos = default; boxScale = default; alpha = 1f; tabPos = default; tabRot = default;
            if (!_eqPull) return false;
            float t = Mathf.Clamp01((Now - _eqT0) / Mathf.Max(0.01f, _eqDur));
            if (t >= 1f) { _eqPull = false; EquipLastSeconds = Now - _eqT0; Debug.Log($"[Inv] the equipment panel's pull done after {EquipLastSeconds:0.000} s"); return false; }
            float e = 1f - (1f - t) * (1f - t) * (1f - t);
            bool open = EquipOpen; float grow = open ? Mathf.Lerp(0.06f, 1f, e) : 1f;
            // the panel grows upward from its lower edge (its own down direction, its half height)
            var box = DrawEquipBox;
            boxPos = EquipHome(box) + box.localRotation * Vector3.down * (EquipHalf(box) * (1f - grow));
            boxScale = new Vector3(1f, grow, 1f);
            alpha = open ? Mathf.Clamp01(t / 0.35f) : 1f;
            TabPose(null, out var low, out var lowRot); TabPose(box, out var high, out var highRot);
            float up = open ? e : 1f - e;
            tabPos = Vector3.Lerp(low, high, up); tabRot = Quaternion.Slerp(lowRot, highRot, up);
            return true;
        }
        /// <summary>The box whose pull is drawn: the one that is up, or the one that was folded last; the tab's own rectangle.</summary>
        internal RectTransform DrawEquipBox => _equipWas != null ? _equipWas : EquipBand;
        internal CanvasGroup DrawEquipGroup => DrawEquipBox == EquipFullBox ? _equipGroupFull : _equipGroup;
        internal RectTransform DrawTab => _tabP.Rt;

        // ------------------------------------------------------------------ the items in their sections
        /// <summary>Each section's items in its order, through its filter; the cells bound to them; the lines.</summary>
        public void Refresh(Inventory inv, int selected, int locked, float weight, float maxWeight, int baseLevel)
        {
            Dirty = false; _inv = inv; _baseLevel = baseLevel;
            for (int s = 0; s < Sections; s++) { _lists[s].Clear(); _counts[s] = 0; }
            foreach (var e in inv.Items.Values)
            {
                int s = SectionOf(e); _counts[s]++;
                if (Passes(e, FilterOf(s))) _lists[s].Add(e);
            }
            for (int s = 0; s < Sections; s++)
            {
                var key = SortOf(s); _lists[s].Sort((a, b) => Compare(a, b, key));
                var list = _lists[s]; int cols = _cols[s], per = cols * Rows, rows = (list.Count + cols - 1) / cols;
                _top[s] = Mathf.Clamp(_top[s], 0, Mathf.Max(0, rows - Rows));
                var f = FilterOf(s);
                // (a one-column section is too narrow for the name and the count: the name alone, the count on its page line)
                _names[s].text = f == Filter.All ? "<b>" + Lang.T(SectionNames[s]) + "</b>" + (cols > 1 ? "  " + _counts[s] : "")
                               : (cols > 1 ? "<b>" + Lang.T(SectionNames[s]) + "</b>  " : "") + FilterName(f) + " " + list.Count + "/" + _counts[s];
                bool hide = _popup.gameObject.activeSelf && _popSec == s;
                for (int i = 0; i < per; i++)
                {
                    var c = _cells[CellAt(s, i / cols, i % cols)]; int at = _top[s] * cols + i;
                    bool has = at < list.Count && !hide;
                    if (c.Btn.gameObject.activeSelf != has) c.Btn.gameObject.SetActive(has);
                    if (!has) { c.Index = -1; continue; }
                    var e = list[at]; c.Index = e.Index;
                    c.Icon.sprite = WristMenu.ItemIcon(e.Info); c.Icon.enabled = c.Icon.sprite != null;
                    c.Count.text = e.IsEquip && s != 4 ? (e.Refine > 0 ? "+" + e.Refine : "") : e.Amount.ToString();
                    c.Mark.text = (e.Wearing != 0 ? Lang.T("~grid~worn") : "") + (e.Favorite ? " <color=#ffd060>★</color>" : "");
                    bool sel = e.Index == selected, lck = e.Index == locked;
                    c.Btn.Normal = lck ? Theme.Lit : sel ? Theme.CellHover : e.Wearing != 0 ? WornCell : Theme.Cell;
                    if (c.Lock.enabled != lck) c.Lock.enabled = lck;
                    if (!c.Btn.Hovered) c.Btn.SetHover(false);
                    if (!c.Icon.enabled) { c.Count.text = ""; c.Btn.SetLabel(e.Info.Name); } else c.Btn.SetLabel("");
                }
                int first = _top[s] * cols + 1, last = Mathf.Min(list.Count, (_top[s] + Rows) * cols);
                bool more = list.Count > per;
                _pageBtns[s].Interactable = more;
                _pageBtns[s].SetLabel(list.Count == 0 ? (_counts[s] == 0 ? Lang.T("~bag~empty") : Lang.T("~inv~none fit")) : more ? (cols > 1 ? Lang.F("~inv~{0}-{1} of {2}  ▼", first, last, list.Count) : Lang.F("~inv~{0}/{1}  ▼", last, list.Count)) : Lang.F("~inv~{0} items", list.Count));
                if (!_pageBtns[s].Hovered) _pageBtns[s].SetHover(false);
            }
            _info.text = Lang.F("{0} of {1} slots", inv.Items.Count, inv.Capacity) + "\n" + Lang.F("Weight  {0} / {1}", Mathf.RoundToInt(weight), Mathf.RoundToInt(maxWeight));
            for (int i = 0; i < _footBtns.Count; i++) if (EntryLabel != null) _footBtns[i].SetLabel(EntryLabel(i + 1));
            if (PinButton != null) { PinButton.Normal = Pinned ? Theme.Lit : Theme.Cell; if (!PinButton.Hovered) PinButton.SetHover(false); _pinIcon.color = Pinned ? Theme.Ink : Theme.InkFaint; }
            if (_popup.gameObject.activeSelf) FillPopup();
            ApplyHighlight();
        }
        /// <summary>A worn item's cell: a soft sage on the canvas (the old bag marked it dark green on its dark panel); ink on it 9 : 1.</summary>
        public static readonly Color WornCell = new Color(0.776f, 0.800f, 0.616f, 1f);

        // ---- the highlight: the items that fit the equipment panel's slot under the pointer outlined in gold, the other cells dimmed
        private HashSet<int> _hi;
        public int Highlighted { get; private set; }
        public int Dimmed { get; private set; }
        /// <summary>The items to outline (their inventory indexes; null: no highlight, nothing dimmed).</summary>
        public void SetHighlight(HashSet<int> indexes) { _hi = indexes; ApplyHighlight(); }
        public bool IsHighlighted(int inventoryIndex) => _hi != null && _hi.Contains(inventoryIndex);
        private void ApplyHighlight()
        {
            Highlighted = Dimmed = 0;
            foreach (var c in _cells)
            {
                if (c == null) continue;
                bool shown = c.Index >= 0, pick = shown && _hi != null && _hi.Contains(c.Index);
                if (c.Pick.enabled != pick) c.Pick.enabled = pick;
                float a = _hi != null && shown && !pick ? DimAlpha : 1f;
                if (c.Group.alpha != a) c.Group.alpha = a;
                if (shown && c.Btn.gameObject.activeInHierarchy) { if (pick) Highlighted++; else if (_hi != null) Dimmed++; }
            }
        }
        public bool CellPicked(int i) => i >= 0 && i < CellCount && _cells[i].Pick.enabled && _cells[i].Index >= 0;
        public float CellAlpha(int i) => i >= 0 && i < CellCount ? _cells[i].Group.alpha : 1f;

        public void Scroll(int sec, int rows)
        {
            int cols = _cols[sec], total = (_lists[sec].Count + cols - 1) / cols, top = Mathf.Clamp(_top[sec] + rows, 0, Mathf.Max(0, total - Rows));
            if (top == _top[sec]) return;
            _top[sec] = top; Dirty = true;
        }
        /// <summary>The page line: the next rows; past the end, the first again.</summary>
        public void NextPage(int sec)
        {
            int cols = _cols[sec], total = (_lists[sec].Count + cols - 1) / cols; if (total <= Rows) return;
            _top[sec] = _top[sec] + Rows >= total ? 0 : Mathf.Min(_top[sec] + Rows, total - Rows);
            Dirty = true;
        }
        public void ScrollTo(int sec, int topRow) { _top[sec] = Mathf.Max(0, topRow); Dirty = true; }
        public int TopRow(int sec) => _top[sec];

        // ---- what the smokes and the owner read
        public UiButton CellButton(int i) => i >= 0 && i < CellCount ? _cells[i].Btn : null;
        public int CellIndex(int i) => i >= 0 && i < CellCount ? _cells[i].Index : -1;
        public int CellSection(int i) => i >= 0 && i < CellCount ? _cells[i].Sec : -1;
        public string CellText(int i) => i >= 0 && i < CellCount ? _cells[i].Count.text + "|" + _cells[i].Mark.text : "";
        public bool CellHasIcon(int i) => i >= 0 && i < CellCount && _cells[i].Icon.enabled;
        public bool CellLocked(int i) => i >= 0 && i < CellCount && _cells[i].Lock != null && _cells[i].Lock.enabled && _cells[i].Btn.gameObject.activeInHierarchy;
        public Text CellCountText(int i) => i >= 0 && i < CellCount ? _cells[i].Count : null;
        public Image CellIcon(int i) => i >= 0 && i < CellCount ? _cells[i].Icon : null;
        /// <summary>The shown cell that holds this item (-1: none shows it now).</summary>
        public int CellOf(int inventoryIndex) { for (int i = 0; i < CellCount; i++) if (_cells[i].Index == inventoryIndex && _cells[i].Btn.gameObject.activeInHierarchy) return i; return -1; }
        /// <summary>The cell under the laser or the look now (-1: none).</summary>
        public int HoveredCell() { for (int i = 0; i < CellCount; i++) if (_cells[i].Index >= 0 && _cells[i].Btn.Hovered) return i; return -1; }
        /// <summary>The cells shown now (bound to an item).</summary>
        public int ShownCells() { int n = 0; for (int i = 0; i < CellCount; i++) if (_cells[i].Index >= 0 && _cells[i].Btn.gameObject.activeInHierarchy) n++; return n; }
        /// <summary>A section's items as it shows them (all its rows, in order): their inventory indexes; its count before the filter.</summary>
        public List<int> SectionOrder(int sec) { var l = new List<int>(); foreach (var e in _lists[sec]) l.Add(e.Index); return l; }
        public int SectionCount(int sec) => _counts[sec];
        public Text NameText(int sec) => _names[sec];
        public UiButton SortButton(int sec) => _sortBtns[sec];
        public UiButton FilterButton(int sec) => _filterBtns[sec];
        public UiButton PageButton(int sec) => _pageBtns[sec];
        public UiRingIcon Ring(int sec) => _rings[sec];
        public RectTransform Head(int sec) => _heads[sec].Rt;
        public RectTransform Column(int sec, int col) => _colPs[sec, col].Rt;
        public BentImage FramePicture => _frame;
        public BentImage CanvasPicture => _canvas;
        public BentStitch Stitching => _stitch;
        public RollGraphic RollRight => _rollR;
        public RollGraphic RollLeft => _rollLP.Rt.gameObject.activeSelf ? _rollL : null;
        public UiTitlePlate Plate => _plate;
        public Text Info => _info;
        /// <summary>The section's place in the list, scrolled so that this item shows (false: its filter hides it).</summary>
        public bool Reveal(int inventoryIndex)
        {
            for (int s = 0; s < Sections; s++)
            {
                int at = _lists[s].FindIndex(e => e.Index == inventoryIndex); if (at < 0) continue;
                int row = at / _cols[s];
                if (row < _top[s] || row >= _top[s] + Rows) { _top[s] = Mathf.Max(0, row - Rows + 1); Dirty = true; }
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ the sort / filter list (a parchment card over the section's cells)
        public const float PopRowH = 52f, PopW = 246f;
        private int _popSec = -1; private bool _popFilter; private readonly List<int> _popValues = new List<int>();
        private Text _colsLabel;
        /// <summary>The sort list's last line: the section's columns, fewer / more.</summary>
        public UiButton ColsMinus { get; private set; }
        public UiButton ColsPlus { get; private set; }
        public bool PopupOpen => _popup.gameObject.activeSelf;
        public int PopupSection => PopupOpen ? _popSec : -1;
        public bool PopupIsFilter => _popFilter;
        public UiButton PopupRow(int i) => i >= 0 && i < _popRows.Count && _popRows[i].gameObject.activeSelf ? _popRows[i] : null;
        public int PopupRows { get; private set; }

        public void TogglePopup(int sec, bool filter)
        {
            if (PopupOpen && _popSec == sec && _popFilter == filter) { ClosePopup(); return; }
            _popSec = sec; _popFilter = filter;
            _popup.SetParent(_hosts[sec].Rt, false); _popup.SetAsLastSibling();
            _popup.gameObject.SetActive(true);
            FillPopup(); Dirty = true;
        }
        public void ClosePopup() { if (!_popup.gameObject.activeSelf) return; _popup.gameObject.SetActive(false); Dirty = true; }
        private void ClosePopupQuiet() { if (_popup != null && _popup.gameObject.activeSelf && !_keepPopup) _popup.gameObject.SetActive(false); }
        private bool _keepPopup;

        private void FillPopup()
        {
            int sec = _popSec; _popValues.Clear();
            var labels = new List<string>(); int current = -1;
            if (_popFilter)
            {
                var cur = FilterOf(sec);
                foreach (var f in Filters[sec])
                {
                    bool fits = f == Filter.All || f == Filter.Fav || f == cur;
                    if (!fits) foreach (var e in _everything) if (SectionOf(e) == sec && Passes(e, f)) { fits = true; break; }
                    if (!fits) continue;
                    if (f == cur) current = labels.Count;
                    _popValues.Add((int)f); labels.Add(FilterName(f));
                }
                _popValues.Add(-1); labels.Add(SaleLock != null && SaleLock() ? Lang.T("Sale lock: on") : Lang.T("Sale lock: off")); // the favourites' sale lock (the old bag's ★ tab had it)
            }
            else
            {
                var cur = SortOf(sec);
                foreach (var k in Sorts[sec]) { if (k == cur) current = labels.Count; _popValues.Add((int)k); labels.Add(SortName(k)); }
            }
            int n = Mathf.Min(labels.Count, _popRows.Count); PopupRows = n;
            bool colsRow = !_popFilter;
            float h = (n + (colsRow ? 1 : 0)) * PopRowH + 22f;
            _popup.sizeDelta = new Vector2(PopW, h);
            _popup.anchoredPosition = new Vector2(0f, GridTop + 6f - h / 2f);
            for (int i = 0; i < _popRows.Count; i++)
            {
                var b = _popRows[i]; bool on = i < n;
                if (b.gameObject.activeSelf != on) b.gameObject.SetActive(on);
                if (!on) continue;
                ((RectTransform)b.transform).anchoredPosition = new Vector2(0f, h / 2f - 11f - PopRowH / 2f - i * PopRowH);
                b.SetLabel(labels[i]);
                b.Normal = i == current ? Theme.Lit : Theme.Cell; if (!b.Hovered) b.SetHover(false);
            }
            foreach (var g in new[] { _colsLabel.gameObject, ColsMinus.gameObject, ColsPlus.gameObject }) if (g.activeSelf != colsRow) g.SetActive(colsRow);
            if (colsRow)
            {
                float y = h / 2f - 11f - PopRowH / 2f - n * PopRowH;
                _colsLabel.rectTransform.anchoredPosition = new Vector2(0f, y); _colsLabel.text = Lang.F("~inv~Columns: {0}", _cols[sec]);
                ((RectTransform)ColsMinus.transform).anchoredPosition = new Vector2(-PopW / 2f + 10f + 28f, y); ((RectTransform)ColsPlus.transform).anchoredPosition = new Vector2(PopW / 2f - 10f - 28f, y);
                ColsMinus.Interactable = _cols[sec] > MinCols; ColsPlus.Interactable = _cols[sec] < MaxCols;
                if (!ColsMinus.Hovered) ColsMinus.SetHover(false); if (!ColsPlus.Hovered) ColsPlus.SetHover(false);
            }
        }
        private readonly List<Inventory.Entry> _everything = new List<Inventory.Entry>();
        /// <summary>Every item (the filter list offers only what fits one).</summary>
        public void SetAll(Inventory inv) { _inv = inv; _everything.Clear(); _everything.AddRange(inv.Items.Values); }

        private void PopupPick(int row)
        {
            if (!PopupOpen || row < 0 || row >= _popValues.Count) return;
            int v = _popValues[row], sec = _popSec;
            if (_popFilter && v < 0) { SaleLockPressed?.Invoke(); FillPopup(); return; }
            if (_popFilter) SetFilter(sec, (Filter)v); else SetSort(sec, (SortKey)v);
            ClosePopup();
        }
        /// <summary>The columns were changed from the list: the list stays open on its section (the - and + are pressed again and again).</summary>
        private void AfterCols() { if (PopupOpen) { _popup.SetParent(_hosts[_popSec].Rt, false); _popup.SetAsLastSibling(); FillPopup(); } }

        // ------------------------------------------------------------------ the cards
        private bool _cardsLaid; private int _cardSec = -1; private string _cardKey = "";
        public bool CardsShown => CardItem != null && CardItem.Shown;
        /// <summary>The section the cards stand beside (-1: none shown).</summary>
        public int CardSection => CardsShown ? _cardSec : -1;

        public void HideCards()
        {
            if (CardItem == null) return;
            CardItem.Clear(); CardWornA.Clear(); CardWornB.Clear(); _cardKey = "";
            Cover();
        }

        /// <summary>The item's card beside its section, and what is worn where it would go beside that.</summary>
        public void ShowCards(Inventory.Entry e, string head, string note, List<Inventory.Entry> worn, Func<Inventory.Entry, string> wornHead)
        {
            if (_mode != Mode.Open) { HideCards(); return; } // (no card while it opens or is the bundle)
            int sec = SectionOf(e);
            string key = e.Index + "|" + sec + "|" + head + "|" + note + "|" + (worn.Count > 0 ? worn[0].Index : -1) + "|" + (worn.Count > 1 ? worn[1].Index : -1);
            CardItem.Set(e, head, note);
            if (worn.Count >= 1) CardWornA.Set(worn[0], wornHead(worn[0])); else CardWornA.Clear();
            if (worn.Count >= 2) CardWornB.Set(worn[1], wornHead(worn[1])); else CardWornB.Clear();
            if (key != _cardKey || !_cardsLaid || sec != _cardSec) { _cardKey = key; _cardSec = sec; LayCards(); }
        }

        /// <summary>The cards on their own cylinder, nearer the eyes: beside the section (sections of the left half: to its right; of the
        /// right half: to its left), each facet facing the axis, their tops level with the canvas' top.</summary>
        private void LayCards()
        {
            _cardsLaid = true;
            float r = _radius, rc = r - CardOff;
            float half = Mathf.Atan(ItemPanel.BoxW / 2f / rc), colGap = ItemPanel.ColGap / rc, gap = CardGap / rc;
            bool right = PocketMid(_cardSec) < 0f;
            float top = StripH / 2f - 8f;
            float Width(ItemPanel p) => p.Columns * 2f * half + (p.Columns - 1) * colGap;
            void Stand(RectTransform t, float ang, float yTop, float h) { t.anchorMin = t.anchorMax = new Vector2(0.5f, 0.5f); t.pivot = new Vector2(0.5f, 0.5f); t.localPosition = Bend.Point(r, ang * r, yTop - h / 2f, CardOff); t.localRotation = Quaternion.Euler(0f, ang * Mathf.Rad2Deg, 0f); t.SetAsLastSibling(); }
            // a card's facets from its left end (an angle), in reading order
            void Set(ItemPanel p, float leftAng, float yTop) { float c = leftAng; for (int i = 0; i < p.Columns; i++) { c += half; Stand(p.Facet(i), c, yTop, p.Height); c += half + colGap; } }
            float at = right ? (_pl[_cardSec] + _pw[_cardSec] + CardGap) / r : (_pl[_cardSec] - CardGap) / r;
            float Take(float w) { float l; if (right) { l = at; at += w + gap; } else { l = at - w; at -= w + gap; } return l; }
            Set(CardItem, Take(Width(CardItem)), top);
            bool a = CardWornA.Shown, b = CardWornB.Shown;
            WornStacked = a && b && CardWornA.Columns == 1 && CardWornB.Columns == 1 && CardWornA.Height + CardGap + CardWornB.Height <= ItemPanel.MaxH;
            if (WornStacked) { float l = Take(2f * half); Set(CardWornA, l, top); Set(CardWornB, l, top - CardWornA.Height - CardGap); }
            else { if (a) Set(CardWornA, Take(Width(CardWornA)), top); if (b) Set(CardWornB, Take(Width(CardWornB)), top); }
            Cover();
        }

        /// <summary>A card is IN FRONT of what it covers: the strip's buttons behind a shown card are switched off (neither the laser nor
        /// the look chooses a switched-off button: VR/DioramaRig.Ui.cs TopButton / PickGazed), and all of them while it is the bundle.
        /// Seen from the cylinder's axis (the eyes of the placing): a button whose middle lies within a card's angle and height.
        /// The ITEMS' CELLS behind a card stay on while no item is locked (<see cref="CardSolid"/> false): the pointer on a cell behind
        /// the card chooses that cell (the card goes to it: a tooltip never keeps an item out of reach); with an item LOCKED its card
        /// is solid: it can be read and its buttons pressed without choosing what is behind it.</summary>
        public bool CardSolid { get; set; }
        public void Cover()
        {
            bool all = _mode == Mode.Compact;
            _spans.Clear();
            if (!all && CardsShown)
            {
                float half = Mathf.Atan(ItemPanel.BoxW / 2f / (_radius - CardOff));
                foreach (var p in new[] { CardItem, CardWornA, CardWornB })
                {
                    if (!p.Shown) continue;
                    for (int i = 0; i < p.Columns; i++)
                    {
                        var f = p.Facet(i); if (f == null) continue;
                        float ang = Mathf.Atan2(f.localPosition.x, f.localPosition.z + _radius), yTop = f.localPosition.y + p.Height / 2f;
                        _spans.Add(new Vector4(ang - half, ang + half, yTop - p.Height, yTop));
                    }
                }
            }
            Covered = 0;
            foreach (var (b, x, y, cell) in _strip) Switch(b, all || ((!cell || CardSolid) && Under(x, y)));
            if (PopupOpen) for (int i = 0; i < _popRows.Count; i++) Switch(_popRows[i], all || Under(PocketMid(_popSec), _popup.anchoredPosition.y + ((RectTransform)_popRows[i].transform).anchoredPosition.y));
        }
        private bool Under(float x, float y) { float a = x / _radius; foreach (var s in _spans) if (a >= s.x && a <= s.y && y >= s.z && y <= s.w) return true; return false; }
        private void Switch(UiButton b, bool off) { if (off) Covered++; if (b.enabled == off) b.enabled = !off; }
        /// <summary>Buttons of the strip switched off now (behind a card, or the bundle).</summary>
        public int Covered { get; private set; }

        /// <summary>The line from <paramref name="from"/> to <paramref name="to"/> (world) passes through a shown card.</summary>
        public bool BehindCard(Vector3 from, Vector3 to)
        {
            if (!CardsShown) return false;
            foreach (var p in new[] { CardItem, CardWornA, CardWornB })
            {
                if (!p.Shown) continue;
                for (int i = 0; i < p.Columns; i++)
                {
                    var f = p.Facet(i); if (f == null) continue;
                    var a = f.InverseTransformPoint(from); var b = f.InverseTransformPoint(to);
                    if (a.z * b.z >= 0f) continue; // both on one side of the card
                    float t = a.z / (a.z - b.z); var hit = a + (b - a) * t; var rc = f.rect;
                    if (hit.x >= rc.xMin && hit.x <= rc.xMax && hit.y >= rc.yMin && hit.y <= rc.yMax) return true;
                }
            }
            return false;
        }
    }

    /// <summary>Client wave 21 (w21inv): the equipment panel's pull is a picture only, as the menus' openings are (UI/MenuAnim.cs): after
    /// every other script's LateUpdate the panel's and the tab's poses for this frame are written (client wave 23: the strip's own
    /// drawing is never moved); when the frame has been drawn the true ones are put back, so the buttons take the laser at their own
    /// places from the first frame.</summary>
    [DefaultExecutionOrder(31000)]
    public sealed class InvDrawDriver : MonoBehaviour
    {
        public InvPanel Panel { get; set; }
        private bool _applied; private Vector3 _boxPos, _boxScale, _tabPos; private Quaternion _tabRot; private float _alpha; private RectTransform _box, _tab; private CanvasGroup _g;
        private void OnEnable() { StartCoroutine(AfterDrawing()); }
        private void LateUpdate()
        {
            Restore();
            if (Panel == null || Panel.EquipBand == null) return;
            // (client wave 23, w23inv: the strip's own drawing is no longer turned: the panel grows, the tab rides up or comes back down)
            if (!Panel.DrawPose(out var bp, out var bs, out float alpha, out var tp, out var tr)) return;
            var box = Panel.DrawEquipBox; var g = Panel.DrawEquipGroup; var tab = Panel.DrawTab;
            _box = box; _g = g; _tab = tab;
            _boxPos = box.localPosition; _boxScale = box.localScale; _alpha = g != null ? g.alpha : 1f; _tabPos = tab.localPosition; _tabRot = tab.localRotation;
            box.localPosition = bp; box.localScale = bs; if (g != null) g.alpha = alpha; tab.localPosition = tp; tab.localRotation = tr;
            _applied = true;
        }
        public void Restore()
        {
            if (!_applied || Panel == null) return;
            _applied = false;
            var box = _box; var g = _g; var tab = _tab;
            if (box != null) { box.localPosition = _boxPos; box.localScale = _boxScale; }
            if (g != null) g.alpha = _alpha;
            if (tab != null) { tab.localPosition = _tabPos; tab.localRotation = _tabRot; }
        }
        private IEnumerator AfterDrawing() { var eof = new WaitForEndOfFrame(); while (true) { yield return eof; Restore(); } }
        private void OnDisable() { Restore(); }
    }

    /// <summary>Client wave 21 (w21inv): before every other script (and before the physics step): a picture's pose the end of the last
    /// frame did not put back is put back now.</summary>
    [DefaultExecutionOrder(-31000)]
    public sealed class InvDrawEarly : MonoBehaviour
    {
        public InvPanel Panel { get; set; }
        private InvDrawDriver _d;
        private void Awake() { _d = GetComponent<InvDrawDriver>(); }
        private void FixedUpdate() { if (_d != null) _d.Restore(); }
        private void Update() { if (_d != null) _d.Restore(); }
    }
}
