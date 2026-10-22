using System;
using System.Collections.Generic;
using System.Linq;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 3 (w3equip): the equipment window, a page of the wrist menu (Items → Equipment) so it reads at wrist scale.
    /// Two tabs laid out like the official window as roBrowserLegacy draws it (UI/Components/Equipment/EquipmentV3/EquipmentV3.html:
    /// 38-106; the official client's layout itself was not available): the doll faces the player, so the right hand and the right
    /// accessory are on the LEFT column.
    ///   Gear:              head top | head middle      Costume &amp; shadow:  costume top     | costume middle
    ///                      head low | armor                                  costume low     | shadow armor
    ///                    right hand | left hand                          shadow weapon   | shadow shield
    ///                       garment | shoes                             costume garment  | shadow shoes
    ///               right accessory | left accessory                  shadow right acc.  | shadow left acc.
    ///                         ammunition
    /// A cell shows what <see cref="Inventory"/> says is worn there (EQUIPITEM_INFO WearState, then 0x0999 / 0x099a / 0x013c).
    /// Tapping a cell selects it; the strip below acts on it with RO requests only:
    ///   Take off   CZ_REQ_TAKEOFF_EQUIP 0x00ab index.W (clif_parse_UnequipItem, clif.cpp:12166-12193)
    ///   Choose / Change  lists the bag's items whose location mask covers this cell and equips the one picked with
    ///              CZ_REQ_WEAR_EQUIP_V5 0x0998 index.W position.L, position = THIS cell's bit. rAthena needs pos &amp; req_pos ≠ 0
    ///              (pc.cpp:12064) and then, for an accessory, a dual-wield weapon or a shadow accessory, puts it on the requested
    ///              side (pc.cpp:12088-12125; a card that forces a side wins, pc.cpp:12093-12106); any other item goes into all of its
    ///              positions, so a top + middle headgear picked in the top cell fills both, a two-handed weapon both hands.
    ///   Details    the item details page (refine, grade, cards, random options, description)
    ///   Take off all  CZ_REQ_TAKEOFF_EQUIP_ALL 0x0bad → ZC_ACK_TAKEOFF_EQUIP_ALL 0x0bae (this rAthena build parses it:
    ///              PACKETVER_RE_NUM 20211103, clif_packetdb.hpp:2002-2006). It empties the ten regular cells only (clif.cpp:22832-22840).
    /// Client wave 15 (w15gear), point 3 (design note: "see what equipment I have and my character's appearance, pretty much the same way that it
    /// is seen on the base game"): the two columns stand on either side of the CHARACTER, drawn in the middle as the official window does
    /// (roBrowserLegacy EquipmentV3.html:38-80: col1 | the character's canvas | col3; EquipmentCommon.js renderCharacter :736-808: the
    /// player's own entity, idle, facing the viewer): <see cref="CharRowPreview"/> draws <see cref="SelfLook"/> (EntityManager.SelfLook:
    /// what the map server says we look like, worn headgears, weapon, shield and garment included) at <see cref="DollScale"/>. The page
    /// is also the Character group's third tab (WristMenu.Board.cs).
    /// Point 29: a cell's "N in the bag fit", the strip's count and the picker list only items this character may wear now
    /// (<see cref="EquipRules"/>: rAthena's pc_isequip, item part).
    /// Client wave 18 (w18gear), points 4 and 5 of the design's notes after wave 17:
    ///   4  ("how many other items are valid options even on the slots that already have things equipped. Make it just as small number on
    ///      the side") a FILLED cell shows, small at its top right corner, how many other items in the bag this character may wear there
    ///      (<see cref="Candidates"/>: the same rules; the worn item is not in the bag's list). No number when there are none.
    ///   5  ("when I double-click an equipped item, that has other valid options, a menu will pop up on the right with the different options
    ///      that I can equip replacing it. And then I can click those to equip them"; answers: "Double-click window 0.4 is fine", an empty
    ///      slot opens it too when something fits) two presses on the same cell within <see cref="DoubleClickSeconds"/> open the SIDE PANEL
    ///      to the right of the page: what fits that cell (as the picker lists it); a press on one equips it there (0x0998 with that cell's
    ///      bit: the server takes the worn piece off first, pc_equipitem). It closes after the swap, on any other press on the page, on Back.
    ///      One press still selects the cell; "Change…" stays.
    /// </summary>
    public sealed class EquipmentWindow
    {
        public enum Group { Gear, CostumeShadow }
        public sealed class Slot
        {
            public uint Bit; public string Name; public Group Group; public int Col, Row;
            public Slot(uint bit, string name, Group g, int col, int row) { Bit = bit; Name = name; Group = g; Col = col; Row = row; }
        }

        /// <summary>Every equip position of the character (mmo.hpp:336-358 enum equip_pos; EQP_COSTUME_FLOOR is commented out there).
        /// Col 0 = left column, 1 = right column, 2 = the full-width ammunition row.</summary>
        public static readonly Slot[] Slots =
        {
            new Slot(0x000100, Lang.N("Head · top"), Group.Gear, 0, 0),         new Slot(0x000200, Lang.N("Head · middle"), Group.Gear, 1, 0),
            new Slot(0x000001, Lang.N("Head · low"), Group.Gear, 0, 1),         new Slot(0x000010, Lang.N("Armor"), Group.Gear, 1, 1),
            new Slot(0x000002, Lang.N("Right hand"), Group.Gear, 0, 2),         new Slot(0x000020, Lang.N("Left hand"), Group.Gear, 1, 2),
            new Slot(0x000004, Lang.N("Garment"), Group.Gear, 0, 3),            new Slot(0x000040, Lang.N("Shoes"), Group.Gear, 1, 3),
            new Slot(0x000008, Lang.N("Accessory · right"), Group.Gear, 0, 4),  new Slot(0x000080, Lang.N("Accessory · left"), Group.Gear, 1, 4),
            new Slot(0x008000, Lang.N("Ammunition"), Group.Gear, 2, 5),
            new Slot(0x000400, Lang.N("Costume · top"), Group.CostumeShadow, 0, 0),     new Slot(0x000800, Lang.N("Costume · middle"), Group.CostumeShadow, 1, 0),
            new Slot(0x001000, Lang.N("Costume · low"), Group.CostumeShadow, 0, 1),     new Slot(0x010000, Lang.N("Shadow armor"), Group.CostumeShadow, 1, 1),
            new Slot(0x020000, Lang.N("Shadow weapon"), Group.CostumeShadow, 0, 2),     new Slot(0x040000, Lang.N("Shadow shield"), Group.CostumeShadow, 1, 2),
            new Slot(0x002000, Lang.N("Costume garment"), Group.CostumeShadow, 0, 3),   new Slot(0x080000, Lang.N("Shadow shoes"), Group.CostumeShadow, 1, 3),
            new Slot(0x100000, Lang.N("Shadow acc. · right"), Group.CostumeShadow, 0, 4), new Slot(0x200000, Lang.N("Shadow acc. · left"), Group.CostumeShadow, 1, 4),
        };
        /// <summary>Client wave 4b (w4lang): a slot's name in the current language (Slot.Name stays the English one: logs, tests).</summary>
        public static string Shown(Slot s) => s == null ? null : Lang.T(s.Name);
        public static Slot SlotOf(uint bit) { foreach (var s in Slots) if (s.Bit == bit) return s; return null; }

        /// <summary>(right bit, left bit, right label, left label) when rAthena puts <paramref name="e"/> on ONE side of a pair, the side
        /// the 0x0998 position asks for: an accessory whose mask is EQP_ACC (pc.cpp:12088-12107; a card whose own position is one side
        /// overrides the request, 12093-12106), a shadow accessory with EQP_SHADOW_ACC (12116-12119), and a one-handed dagger / sword /
        /// axe that rAthena reports as EQP_ARMS for a character who can dual-wield (pc_equippoint_sub, pc.cpp:1464-1482; placed by
        /// 12108-12115). The weapon's SubType comes from the client data (itemviews.tsv: AppearanceDb.WeaponSubType), because a
        /// two-handed weapon has the same EQP_ARMS mask and always takes both hands. Null for anything else and for worn items.
        /// Client wave 4 (w4items): a shadow weapon reported as EQP_SHADOW_ARMS 0x060000 goes to the requested shadow hand (pc.cpp:12120-12124).
        /// Only pc_equippoint_sub reports that mask (a dagger / one-handed sword / axe shadow weapon for a dual-wielder, pc.cpp:1477-1478):
        /// no item of rAthena's db/re/item_db_equip.yml lists both shadow hands, so the mask alone decides (and none of its 189 shadow
        /// weapons has a SubType, so the case never occurs with the shipped item db).</summary>
        public static (uint r, uint l, string lr, string ll)? SideChoice(Inventory.Entry e)
        {
            if (e == null || !e.IsEquip || e.Wearing != 0) return null;
            if (e.Location == 0x88) return (0x08u, 0x80u, Lang.T("Equip right"), Lang.T("Equip left"));                 // EQP_ACC
            if (e.Location == 0x300000) return (0x100000u, 0x200000u, Lang.T("Equip right"), Lang.T("Equip left"));     // EQP_SHADOW_ACC
            if (e.Location == 0x060000 && e.Info.Type == 12) return (0x020000u, 0x040000u, Lang.T("Right hand"), Lang.T("Left hand")); // EQP_SHADOW_ARMS (client wave 4)
            if (e.Location == 0x22 && e.Info.Type == 5)                                                   // EQP_ARMS
            {
                int sub = Sprites.AppearanceDb.WeaponSubType(e.ItemId);
                if (sub == 1 || sub == 2 || sub == 6) return (0x02u, 0x20u, Lang.T("Right hand"), Lang.T("Left hand")); // W_DAGGER, W_1HSWORD, W_1HAXE (pc.hpp:960-966; pc.cpp:1471)
            }
            return null;
        }

        /// <summary>The names of the positions in a mask ("Head · top + Head · middle").</summary>
        public static string SlotNames(uint mask)
        {
            var l = new List<string>();
            foreach (var s in Slots) if ((mask & s.Bit) != 0) l.Add(Lang.T(s.Name));
            return string.Join(" + ", l);
        }

        private const int CandPerPage = 5;
        /// <summary>Client wave 15 (w15gear), point 3: our own look for the doll (set by LokiGame: EntityManager.SelfLook).</summary>
        public static Func<Loki.Sprites.PlayerLook> SelfLook = () => null;
        /// <summary>The doll's column (UI px) and its scale: 2.2 UI px a sprite pixel, a standing Novice (a ~92-px frame) about 200 px tall;
        /// a wider look (a big weapon, wings) shrinks to the column.</summary>
        public const float DollColumn = 150f, DollScale = 2.2f;
        private CharRowPreview _dollPic;
        private readonly Inventory _inv; private readonly PlayerState _ps; private readonly Net.RoSession _s;
        private readonly Action<string> _toast; private readonly Action<int> _openDetail; private readonly Action _back, _dirty;
        private readonly float _w, _h;
        private RectTransform _root, _doll, _pick;
        private Text _strip, _pickTitle, _pickPage;
        private UiButton _tabGear, _tabCostume, _bShow, _bDetails, _bTakeOff, _bChange, _bAll, _bBack;
        private readonly List<(Slot slot, UiButton cell, Image icon, Text text)> _cells = new List<(Slot, UiButton, Image, Text)>();
        private readonly List<(UiButton row, Image icon, Text text, UiButton act)> _candRows = new List<(UiButton, Image, Text, UiButton)>();
        private Group _group = Group.Gear;
        private uint _selected = 0x000100;
        private bool _picking; private int _pickPageIdx;
        // client wave 18 (w18gear), points 4 and 5: the cells' small numbers, the side panel and the double press
        /// <summary>Two presses on one cell within this many seconds open the side panel (design note: "Double-click window 0.4 is fine").</summary>
        public const float DoubleClickSeconds = 0.4f;
        /// <summary>The side panel: its width (UI px), its gap from the page's right edge, its rows a page.</summary>
        public const float SideW = 380f, SideGap = 16f;
        private const int SideRows = 6;
        private readonly Dictionary<uint, Text> _badges = new Dictionary<uint, Text>();
        private RectTransform _side; private Text _sideTitle, _sidePage;
        private readonly List<(UiButton row, Image icon, Text text)> _sideRows = new List<(UiButton, Image, Text)>();
        private uint _sideBit, _pressBit; private bool _sideOpen; private int _sidePageIdx; private float _pressAt = -10f;
        /// <summary>Whether the side panel is open, for which cell, and how many times it opened (log, smoke).</summary>
        public bool SideOpen => _sideOpen;
        public uint SideBit => _sideBit;
        public int SideOpens { get; private set; }

        public bool Visible => _root != null && _root.gameObject.activeSelf;
        public Group CurrentGroup => _group;
        public uint Selected => _selected;
        public bool Picking => _picking;

        public EquipmentWindow(Inventory inv, PlayerState ps, Net.RoSession s, Action<string> toast, Action<int> openDetail, Action back, Action dirty, float w, float h)
        {
            _inv = inv; _ps = ps; _s = s; _toast = toast; _openDetail = openDetail; _back = back; _dirty = dirty; _w = w; _h = h;
        }

        public void Build(RectTransform parent)
        {
            float top = _h / 2, W = _w;
            _root = UiKit.Box(parent, "equipment", Vector2.zero, new Vector2(W, _h), new Color(0, 0, 0, 0));
            // header: the two tabs, the "others may look" toggle (CZ_CONFIG type 0), Back (to the item list, or out of the picker)
            _tabGear = UiKit.Button(_root, Lang.T("Gear"), new Vector2(-W / 2 + 65, top - 30), new Vector2(120, 48), () => SetGroup(Group.Gear), 21);
            _tabCostume = UiKit.Button(_root, Lang.T("Costume · shadow"), new Vector2(-W / 2 + 235, top - 30), new Vector2(200, 48), () => SetGroup(Group.CostumeShadow), 18);
            _bShow = UiKit.Button(_root, "", new Vector2(W / 2 - 173, top - 30), new Vector2(176, 48), () => ToggleShowEquip(), 16);
            _bBack = UiKit.Button(_root, Lang.T("Back"), new Vector2(W / 2 - 42, top - 30), new Vector2(76, 48), () => { if (_sideOpen) { CloseSide(); _dirty(); } else if (_picking) { _picking = false; _dirty(); } else _back(); }, 21); // client wave 18: Back closes the side panel first

            // ---- the doll: two columns of five cells on either side of the character + the ammunition row
            // (client wave 15, point 3: the official window's three columns; wave 3 had the two columns side by side)
            _doll = UiKit.Box(_root, "doll", Vector2.zero, new Vector2(W, _h), new Color(0, 0, 0, 0));
            float ch = 78, pitch = 84, y0 = top - 98, cw = (W - 10 - DollColumn - 12) / 2f;
            float dollTop = y0 + ch / 2, dollBottom = y0 - 4 * pitch - ch / 2;
            UiKit.Box(_doll, "dollbg", new Vector2(0, (dollTop + dollBottom) / 2), new Vector2(DollColumn, dollTop - dollBottom), new Color(0, 0, 0, 0.3f));
            _dollPic = CharRowPreview.Create(_doll, new Vector2(0, (dollTop + dollBottom) / 2), new Vector2(DollColumn - 8, dollTop - dollBottom - 8), DollScale);
            _dollPic.gameObject.name = "w15:doll";
            foreach (var sl in Slots)
            {
                if (sl.Group != Group.Gear && sl.Group != Group.CostumeShadow) continue;
                float x = sl.Col == 2 ? 0 : sl.Col == 0 ? -W / 2 + 5 + cw / 2 : W / 2 - 5 - cw / 2;
                float w = sl.Col == 2 ? W - 10 : cw;
                float y = y0 - sl.Row * pitch;
                uint bit = sl.Bit;
                var cell = UiKit.Button(_doll, "", new Vector2(x, y), new Vector2(w, ch), () => Press(bit), 18); // client wave 18: Press (Select + the double press)
                cell.Normal = new Color(1, 1, 1, 0.05f); cell.Hover = new Color(0.3f, 0.35f, 0.5f, 0.55f); cell.SetHover(false);
                var icon = UiKit.Icon((RectTransform)cell.transform, new Vector2(-w / 2 + 26, 0), new Vector2(40, 40));
                // client wave 18 (w18gear), point 4: the text leaves the top right corner to the small number
                var text = UiKit.Label((RectTransform)cell.transform, "", new Vector2(15, 0), new Vector2(w - 74, ch - 4), 18, TextAnchor.MiddleLeft);
                text.resizeTextForBestFit = true; text.resizeTextMinSize = 14; text.resizeTextMaxSize = 18; // a long name shrinks rather than being cut
                var badge = UiKit.Label((RectTransform)cell.transform, "", new Vector2(w / 2 - 17, ch / 2 - 15), new Vector2(30, 24), 17, TextAnchor.MiddleRight, new Color(1f, 0.84f, 0.45f));
                badge.resizeTextForBestFit = false; badge.horizontalOverflow = HorizontalWrapMode.Overflow;
                _badges[bit] = badge;
                _cells.Add((sl, cell, icon, text));
            }
            // ---- the strip: what the selected cell holds and what can be done with it
            UiKit.Box(_doll, "stripbg", new Vector2(0, -top + 108), new Vector2(W - 10, 64), new Color(0, 0, 0, 0.35f));
            _strip = UiKit.Label(_doll, "", new Vector2(0, -top + 108), new Vector2(W - 30, 62), 19, TextAnchor.MiddleLeft);
            float bw = (W - 40) / 4f;
            float bx(int i) => -W / 2 + 10 + bw * (i + 0.5f) + i * 6.5f;
            _bDetails = UiKit.Button(_doll, Lang.T("Details"), new Vector2(bx(0), -top + 40), new Vector2(bw, 56), () => Details(), 21);
            _bTakeOff = UiKit.Button(_doll, Lang.T("Take off"), new Vector2(bx(1), -top + 40), new Vector2(bw, 56), () => TakeOff(), 21);
            _bChange = UiKit.Button(_doll, Lang.T("Change…"), new Vector2(bx(2), -top + 40), new Vector2(bw, 56), () => OpenPicker(), 21);
            _bAll = UiKit.Button(_doll, Lang.T("Take off all"), new Vector2(bx(3), -top + 40), new Vector2(bw, 56), () => TakeOffAll(), 19);

            // ---- the picker: the bag's items that fit the selected cell
            _pick = UiKit.Box(_root, "pick", Vector2.zero, new Vector2(W, _h), new Color(0, 0, 0, 0));
            _pickTitle = UiKit.Label(_pick, "", new Vector2(0, top - 90), new Vector2(W - 30, 50), 21, TextAnchor.MiddleLeft, UiKit.Dim);
            for (int i = 0; i < CandPerPage; i++)
            {
                float y = top - 160 - i * 88; int slot = i;
                var row = UiKit.Button(_pick, "", new Vector2(0, y), new Vector2(W - 10, 80), () => CandidateDetails(slot), 18);
                row.Normal = new Color(1, 1, 1, 0.05f); row.Hover = new Color(0.3f, 0.35f, 0.5f, 0.55f); row.SetHover(false);
                var icon = UiKit.Icon(_pick, new Vector2(-W / 2 + 40, y), new Vector2(48, 48));
                var text = UiKit.Label(_pick, "", new Vector2(-50, y), new Vector2(W - 250, 78), 20, TextAnchor.MiddleLeft);
                text.resizeTextForBestFit = true; text.resizeTextMinSize = 14; text.resizeTextMaxSize = 20;
                var act = UiKit.Button(_pick, Lang.T("Equip"), new Vector2(W / 2 - 80, y), new Vector2(140, 60), () => EquipCandidate(slot), 22);
                _candRows.Add((row, icon, text, act));
            }
            UiKit.Button(_pick, "<", new Vector2(-W / 2 + 60, -top + 40), new Vector2(100, 56), () => { _pickPageIdx = Mathf.Max(0, _pickPageIdx - 1); _dirty(); }, 28);
            _pickPage = UiKit.Label(_pick, "", new Vector2(0, -top + 40), new Vector2(200, 50), 20, TextAnchor.MiddleCenter, UiKit.Dim);
            UiKit.Button(_pick, ">", new Vector2(W / 2 - 60, -top + 40), new Vector2(100, 56), () => { _pickPageIdx++; _dirty(); }, 28);
            _pick.gameObject.SetActive(false);
            BuildSide(W, top);
            _root.gameObject.SetActive(false);
        }

        /// <summary>Client wave 18 (w18gear), point 5: the side panel, to the RIGHT of the page in its plane (<see cref="SideGap"/> px from its
        /// edge, the page's height), with a background the laser stops on (UiKit.Panel's own: a press on its empty space reaches nothing behind).</summary>
        private void BuildSide(float W, float top)
        {
            _side = UiKit.Box(_root, "w18:side", new Vector2(W / 2 + SideGap + SideW / 2, 0), new Vector2(SideW, _h), new Color(1, 1, 1, 0));
            _side.GetComponent<UnityEngine.UI.Image>().enabled = false; UiKit.Dress(_side, new Vector2(SideW, _h), Mathf.Max(1e-5f, _side.lossyScale.x), PanelLook.Card); // client wave 21 (w21kit): a parchment card of the theme
            var col = _side.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(SideW, _h, 4f); col.center = new Vector3(0, 0, 3f);
            _side.gameObject.AddComponent<UiBlocker>();
            _sideTitle = UiKit.Label(_side, "", new Vector2(0, top - 42), new Vector2(SideW - 30, 64), 19, TextAnchor.MiddleLeft, UiKit.Dim);
            _sideTitle.resizeTextForBestFit = true; _sideTitle.resizeTextMinSize = 15; _sideTitle.resizeTextMaxSize = 19;
            float rw = SideW - 24;
            for (int i = 0; i < SideRows; i++)
            {
                int slot = i;
                var row = UiKit.Button(_side, "", new Vector2(0, top - 112 - i * 84), new Vector2(rw, 76), () => EquipFromSide(slot), 18);
                row.Normal = new Color(1, 1, 1, 0.05f); row.Hover = new Color(0.3f, 0.35f, 0.5f, 0.55f); row.SetHover(false);
                var icon = UiKit.Icon((RectTransform)row.transform, new Vector2(-rw / 2 + 30, 0), new Vector2(44, 44));
                var text = UiKit.Label((RectTransform)row.transform, "", new Vector2(26, 0), new Vector2(rw - 76, 72), 18, TextAnchor.MiddleLeft);
                text.resizeTextForBestFit = true; text.resizeTextMinSize = 14; text.resizeTextMaxSize = 18;
                _sideRows.Add((row, icon, text));
            }
            UiKit.Button(_side, "<", new Vector2(-SideW / 2 + 56, -top + 40), new Vector2(90, 56), () => { _sidePageIdx = Mathf.Max(0, _sidePageIdx - 1); _dirty(); }, 28);
            _sidePage = UiKit.Label(_side, "", new Vector2(0, -top + 40), new Vector2(140, 50), 20, TextAnchor.MiddleCenter, UiKit.Dim);
            UiKit.Button(_side, ">", new Vector2(SideW / 2 - 56, -top + 40), new Vector2(90, 56), () => { _sidePageIdx++; _dirty(); }, 28);
            _side.gameObject.SetActive(false);
        }

        public void Show(bool on) { if (!on && _sideOpen) CloseSide(); if (_root != null && _root.gameObject.activeSelf != on) _root.gameObject.SetActive(on); }
        public void Reset() { _picking = false; _pickPageIdx = 0; CloseSide(); }

        public void SetGroup(Group g) { CloseSide(); _group = g; _picking = false; var first = Slots.First(s => s.Group == g); if (SlotOf(_selected)?.Group != g) _selected = first.Bit; _dirty(); }
        /// <summary>A single press on a cell's button selects the cell and immediately opens the side panel when anything in the bag fits that cell.</summary>
        public void Press(uint bit)
        {
            if (SlotOf(bit) == null) return;
            Select(bit);
            var cands = Candidates(bit);
            foreach (var c in cands) c.IsNew = false;
            if (cands.Count > 0) OpenSide(bit);
            Pressed?.Invoke(bit);
        }
        /// <summary>Client wave 21 (w21inv): a cell was pressed (its position bit): the inventory's equipment panel arms that slot
        /// (UI/WristMenu.Inv.cs). A cell's own button (the laser or the look on it: the inventory highlights what fits there).</summary>
        public Action<uint> Pressed { get; set; }
        public UiButton CellButton(uint bit) { foreach (var c in _cells) if (c.slot.Bit == bit) return c.cell; return null; }
        /// <summary>Selects a cell (client wave 18: and closes the side panel: a press elsewhere).</summary>
        public void Select(uint bit)
        {
            var sl = SlotOf(bit); if (sl == null) return;
            CloseSide();
            _selected = bit; _group = sl.Group; _picking = false; _dirty();
        }

        /// <summary>Client wave 18 (w18gear), point 5: the side panel for <paramref name="bit"/>.</summary>
        public void OpenSide(uint bit)
        {
            foreach (var c in Candidates(bit)) c.IsNew = false;
            _sideBit = bit; _sideOpen = true; _sidePageIdx = 0; SideOpens++;
            Debug.Log($"[Equip] side panel: {SlotOf(bit)?.Name} (0x{bit:x}), {Candidates(bit).Count} fit");
            _dirty();
        }
        public void CloseSide()
        {
            if (!_sideOpen) return;
            _sideOpen = false;
            if (_side != null) _side.gameObject.SetActive(false);
        }
        private void EquipFromSide(int row)
        {
            var list = Candidates(_sideBit); int i = _sidePageIdx * SideRows + row;
            if (i >= list.Count) return;
            uint bit = _sideBit;
            Debug.Log($"[Equip] side panel: equip {list[i].ItemId} {list[i].Info.English} into {SlotOf(bit)?.Name}");
            CloseSide(); // after the swap (the design's answer): it closes
            EquipInto(list[i], bit);
        }

        /// <summary>The bag's items that can go into <paramref name="bit"/>: location mask covers it, not worn (pc_equipitem refuses an item
        /// that is already worn somewhere, pc.cpp:12064), and (client wave 15, point 29) this character may wear it now: rAthena's
        /// pc_isequip, item part (<see cref="EquipRules"/>: job, class tier, sex, base level, broken, ammunition).</summary>
        public List<Inventory.Entry> Candidates(uint bit) =>
            _inv.Items.Values.Where(e => e.IsEquip && e.Wearing == 0 && (e.Location & bit) != 0 && EquipRules.WhyNot(e, _inv, _ps.BaseLevel) == null)
                .OrderBy(e => e.Info.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Index).ToList();

        /// <summary>Client wave 15, point 29: the bag's items whose location covers <paramref name="bit"/> but this character may not wear
        /// now (with rAthena's reason), for the log and the smoke.</summary>
        public List<(Inventory.Entry e, string why)> Unfit(uint bit) =>
            _inv.Items.Values.Where(e => e.IsEquip && e.Wearing == 0 && (e.Location & bit) != 0).Select(e => (e, why: EquipRules.WhyNot(e, _inv, _ps.BaseLevel)))
                .Where(x => x.why != null).OrderBy(x => x.e.Index).ToList();

        // ---------------------------------------------------------------- actions (public: the smoke presses them as the laser would)
        public void Details() { CloseSide(); var e = _inv.WornAt(_selected); if (e != null) _openDetail(e.Index); }
        public void TakeOff()
        {
            CloseSide(); // client wave 18: a press elsewhere on the page closes the side panel
            var e = _inv.WornAt(_selected);
            if (e == null) { _toast(Lang.T("Nothing is worn there.")); return; }
            Debug.Log($"[Equip] take off {e.ItemId} {e.Info.English} (index {e.Index}) from {SlotNames(e.Wearing)}");
            _s.Unequip(e.Index);
        }
        public void OpenPicker() { CloseSide(); _picking = true; _pickPageIdx = 0; _dirty(); }
        /// <summary>CZ_CONFIG type 0: let other players open our equipment window (or stop them); the answer is ZC_CONFIG 0x02d9.</summary>
        public void ToggleShowEquip()
        {
            if (_sideOpen) { CloseSide(); _dirty(); } // client wave 18
            bool on = _inv.ShowEquip != true;
            Debug.Log($"[Equip] others may view my equipment -> {on} (CZ_CONFIG 0x02d8 type 0)");
            _s.SendPacket(Inventory.ShowEquipPacket(on));
        }
        public void TakeOffAll()
        {
            if (_sideOpen) { CloseSide(); _dirty(); } // client wave 18
            if (!_inv.Items.Values.Any(x => (x.Wearing & Inventory.TakeOffAllMask) != 0)) { _toast(Lang.T("Nothing to take off.")); return; }
            Debug.Log("[Equip] take off all (0x0bad)");
            _s.SendPacket(Inventory.TakeOffAllPacket());
        }
        /// <summary>Equips <paramref name="e"/> into the cell <paramref name="bit"/>: 0x0998 with that one bit as the position.</summary>
        public bool EquipInto(Inventory.Entry e, uint bit)
        {
            if (e == null) return false;
            if (e.Info.MinLevel > _ps.BaseLevel) { _toast(Lang.F("{0} needs base level {1} (you are {2}).", e.Info.Name, e.Info.MinLevel, _ps.BaseLevel)); return false; }
            if ((e.Location & bit) == 0) { _toast(Lang.F("{0} doesn't go there.", e.Info.Name)); return false; }
            Debug.Log($"[Equip] equip {e.ItemId} {e.Info.English} (index {e.Index}, location 0x{e.Location:x}) into {SlotOf(bit)?.Name} (0x0998 position 0x{bit:x})");
            _s.Equip(e.Index, bit);
            _picking = false; _dirty();
            return true;
        }
        private void EquipCandidate(int row)
        {
            var list = Candidates(_selected); int i = _pickPageIdx * CandPerPage + row;
            if (i < list.Count) EquipInto(list[i], _selected);
        }
        private void CandidateDetails(int row)
        {
            var list = Candidates(_selected); int i = _pickPageIdx * CandPerPage + row;
            if (i < list.Count) _openDetail(list[i].Index);
        }

        // ---------------------------------------------------------------- drawing
        /// <summary>A cell's text: the position, then the item (refine, grade, card count, option count), or "empty".</summary>
        public string CellText(uint bit)
        {
            var sl = SlotOf(bit); var e = _inv.WornAt(bit);
            string head = $"<size=17><color=#aab3c5>{Shown(sl)}</color></size>";
            if (e == null) { int n = Candidates(bit).Count; return head + "\n" + (n > 0 ? Lang.F("<color=#8a92a6>empty · {0} in the bag fit</color>", n) : Lang.T("<color=#8a92a6>empty</color>")); }
            return head + "\n" + ShortName(e) + (bit == Inventory.AmmoSlot && e.Amount > 1 ? $"  <color=#ffd060>x{e.Amount}</color>" : "");
        }

        /// <summary>"+7 Faca [C] ◆1 ◇2" (◆ ◇ come from Malgun Gothic, the UI font's fallback: Segoe UI has neither): refine, the client's name, the enchant grade, cards, random options (compact, for a cell).</summary>
        public static string ShortName(Inventory.Entry e)
        {
            var sb = new System.Text.StringBuilder();
            if (!e.Identified && e.IsEquip) sb.Append("<color=#c9a0ff>?</color> ");
            if (e.Damaged) sb.Append("<color=#ff7a6a>").Append(Lang.T("(damaged)")).Append("</color> ");
            if (e.Refine > 0) sb.Append("<color=#ffe28a>+").Append(e.Refine).Append("</color> ");
            sb.Append(e.Info.Name);
            if (e.Grade > 0 && e.Grade <= 4) sb.Append(" <color=#ffb347>[").Append(Inventory.GradeName(e.Grade)).Append("]</color>");
            int cards = CardCount(e); if (cards > 0) sb.Append(" <color=#9fd0ff>◆").Append(cards).Append("</color>");
            int opts = e.Options != null ? e.Options.Count(o => o.Id > 0) : 0; if (opts > 0) sb.Append(" <color=#b8f0a0>◇").Append(opts).Append("</color>");
            return sb.ToString();
        }
        public static int CardCount(Inventory.Entry e)
        {
            if (!e.IsEquip || e.Cards == null) return 0;
            if (e.Cards[0] == 0x00FF || e.Cards[0] == 0x00FE || e.Cards[0] == 0xFF00) return 0; // forged / pet egg markers (card0 special), not cards
            int n = 0; foreach (var c in e.Cards) if (c > 0 && GameDb.GetItem(c).Type == 6) n++;
            return n;
        }

        public string StripText { get; private set; } = "";
        /// <summary>The strip's text for a cell: what is worn there (and where else that item sits), or how many of the bag's items fit it.
        /// Client wave 21 (w21inv): the inventory's equipment band shows it for the slot under the pointer (UI/InvPanel.cs).</summary>
        public string StripFor(uint bit) => SlotOf(bit) == null ? "" : StripFor(bit, _inv.WornAt(bit), Candidates(bit).Count);
        private string StripFor(uint bit, Inventory.Entry w, int fits)
        {
            var sel = SlotOf(bit);
            return w != null
                ? $"<color=#9aa3b5>{Shown(sel)}:</color> {Inventory.FullName(w)}" + (w.Wearing != bit ? "  " + Lang.F("<color=#9aa3b5>(also {0})</color>", SlotNames(w.Wearing & ~bit)) : "")
                : fits > 1 ? Lang.F("<color=#9aa3b5>{0}:</color> <color=#6a7080>empty</color> — {1} items in your bag fit here.", Shown(sel), fits)
                : fits == 1 ? Lang.F("<color=#9aa3b5>{0}:</color> <color=#6a7080>empty</color> — 1 item in your bag fits here.", Shown(sel))
                : Lang.F("<color=#9aa3b5>{0}:</color> <color=#6a7080>empty</color> — nothing in your bag fits here.", Shown(sel));
        }

        public void Refresh()
        {
            if (_root == null) return;
            _tabGear.Normal = _group == Group.Gear ? new Color(0.45f, 0.35f, 0.15f) : UiKit.ButtonBg; _tabGear.SetHover(false);
            _tabCostume.Normal = _group == Group.CostumeShadow ? new Color(0.45f, 0.35f, 0.15f) : UiKit.ButtonBg; _tabCostume.SetHover(false);
            _doll.gameObject.SetActive(!_picking); _pick.gameObject.SetActive(_picking);
            if (!_picking && _dollPic != null) _dollPic.ShowLook(SelfLook?.Invoke(), CharRowPreview.Pose.Idle); // client wave 15, point 3
            var sel = SlotOf(_selected);
            if (_picking) { CloseSide(); RefreshPicker(sel); return; }
            RefreshSide(); // client wave 18 (w18gear), point 5
            foreach (var (slot, cell, icon, text) in _cells)
            {
                bool show = slot.Group == _group;
                cell.gameObject.SetActive(show);
                if (!show) continue;
                var e = _inv.WornAt(slot.Bit);
                icon.sprite = e != null ? WristMenu.ItemIcon(e.Info) : null; icon.enabled = icon.sprite != null;
                text.text = CellText(slot.Bit);
                if (_badges.TryGetValue(slot.Bit, out var badge)) badge.text = BadgeText(slot.Bit); // client wave 18 (w18gear), point 4
                bool on = slot.Bit == _selected;
                cell.Normal = on ? new Color(0.45f, 0.35f, 0.15f, 0.85f) : e != null ? new Color(0.16f, 0.19f, 0.27f, 0.9f) : new Color(0, 0, 0, 0.3f); cell.SetHover(false);
            }
            var w = _inv.WornAt(_selected);
            int fits = Candidates(_selected).Count;
            StripText = StripFor(_selected, w, fits);
            _strip.text = StripText;
            SetEnabled(_bDetails, w != null); SetEnabled(_bTakeOff, w != null);
            _bChange.SetLabel(w != null ? Lang.T("Change…") : Lang.T("Choose…")); SetEnabled(_bChange, fits > 0);
            bool any = _inv.Items.Values.Any(x => (x.Wearing & Inventory.TakeOffAllMask) != 0);
            _bAll.gameObject.SetActive(_group == Group.Gear); SetEnabled(_bAll, any);
            var shown = _inv.ShowEquip;
            _bShow.SetLabel(shown.HasValue ? (shown.Value ? Lang.T("Others may look: on") : Lang.T("Others may look: off")) : Lang.T("Others may look: ?"));
            _bShow.Normal = shown == true ? new Color(0.2f, 0.4f, 0.25f) : UiKit.ButtonBg; _bShow.SetHover(false);
        }

        /// <summary>True when any valid candidate in the bag for <paramref name="bit"/> was newly gained (<see cref="Inventory.Entry.IsNew"/>).</summary>
        public bool HasNewCandidate(uint bit) => Candidates(bit).Any(e => e.IsNew);

        /// <summary>A cell's badge at its top-right corner: "new!" when a newly gained valid item in the bag fits this slot, else on a filled
        /// cell how many other items in the bag this character may wear there.</summary>
        public string BadgeText(uint bit)
        {
            var cands = Candidates(bit);
            if (cands.Any(e => e.IsNew)) return "<color=#ffe040>new!</color>";
            if (_inv.WornAt(bit) == null) return "";
            return cands.Count > 0 ? cands.Count.ToString() : "";
        }

        /// <summary>Client wave 18 (w18gear), point 5: the side panel's list (the picker's rows: icon, name with refine / grade / cards / options,
        /// what else it takes), paged; it closes itself when nothing fits that cell any more.</summary>
        private void RefreshSide()
        {
            if (!_sideOpen || _side == null) { if (_side != null && _side.gameObject.activeSelf) _side.gameObject.SetActive(false); return; }
            var list = Candidates(_sideBit);
            if (list.Count == 0) { CloseSide(); return; }
            if (!_side.gameObject.activeSelf) _side.gameObject.SetActive(true);
            int pages = Mathf.Max(1, (list.Count + SideRows - 1) / SideRows);
            _sidePageIdx = Mathf.Clamp(_sidePageIdx, 0, pages - 1);
            _sideTitle.text = Lang.F("Put on: <color=#ffd060>{0}</color>  <color=#9aa3b5>({1} in your bag)</color>", Shown(SlotOf(_sideBit)), list.Count);
            _sidePage.text = $"{_sidePageIdx + 1} / {pages}";
            for (int r = 0; r < SideRows; r++)
            {
                int i = _sidePageIdx * SideRows + r;
                var (row, icon, text) = _sideRows[r];
                bool show = i < list.Count;
                row.gameObject.SetActive(show);
                if (!show) continue;
                var e = list[i];
                icon.sprite = WristMenu.ItemIcon(e.Info); icon.enabled = icon.sprite != null;
                uint more = SideChoice(e).HasValue ? 0 : e.Location & ~_sideBit;
                text.text = ShortName(e) + (more != 0 ? "\n" + Lang.F("<size=16><color=#aab3c5>also takes {0}</color></size>", SlotNames(more)) : "");
            }
        }

        private void RefreshPicker(Slot sel)
        {
            var list = Candidates(_selected);
            int pages = Mathf.Max(1, (list.Count + CandPerPage - 1) / CandPerPage);
            _pickPageIdx = Mathf.Clamp(_pickPageIdx, 0, pages - 1);
            _pickTitle.text = Lang.F("Put on: <color=#ffd060>{0}</color>  <color=#9aa3b5>({1} in your bag)</color>", Shown(sel), list.Count);
            _pickPage.text = list.Count == 0 ? Lang.T("nothing fits") : $"{_pickPageIdx + 1} / {pages}";
            for (int r = 0; r < CandPerPage; r++)
            {
                int i = _pickPageIdx * CandPerPage + r;
                var (row, icon, text, act) = _candRows[r];
                bool show = i < list.Count;
                row.gameObject.SetActive(show); act.gameObject.SetActive(show);
                if (!show) { icon.enabled = false; text.text = ""; continue; }
                var e = list[i];
                icon.sprite = WristMenu.ItemIcon(e.Info); icon.enabled = icon.sprite != null;
                bool low = e.Info.MinLevel > _ps.BaseLevel;
                // an item that fills more than this cell says so (a top + middle headgear, a two-handed weapon); one that rAthena puts on
                // ONE side of a pair (SideChoice) goes exactly here
                uint more = SideChoice(e).HasValue ? 0 : e.Location & ~_selected;
                string where = more != 0 ? "\n" + Lang.F("<size=16><color=#aab3c5>also takes {0}</color></size>", SlotNames(more)) : "";
                text.text = ShortName(e) + where;
                act.SetLabel(low ? "Lv " + e.Info.MinLevel : Lang.T("Equip")); SetEnabled(act, !low);
            }
        }

        private static void SetEnabled(UiButton b, bool on) { b.Interactable = on; b.Normal = on ? UiKit.ButtonBg : new Color(0.28f, 0.28f, 0.3f); b.SetHover(false); }

    }
}
