using System.Collections.Generic;
using System.Linq;
using System.Text;
using Loki.Game;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 4 (w4items): the wrist menu's additions for items and economy.
    ///   Item comparison: an unworn piece of equipment's details get a "Compare" button that swaps the description for what is worn
    ///   where it would go (the official client opens a second item-info window, ItemCompare, beside the candidate: roBrowserLegacy
    ///   UI/Components/Inventory/InventoryCommon.js:1286-1320 with Equipment.isInEquipList = any worn item whose location shares a bit,
    ///   EquipmentCommon.js:963-970, a reimplementation). Every worn item at any of the candidate's positions is listed (an accessory
    ///   or a dual-wield weapon may replace either side). Stats are the client data's description text (items.tsv): no stat is sent
    ///   by the server for items.
    ///   Favourites: the official inventory moves a favourite to its own tab (it leaves the Item / Equip / Etc tabs:
    ///   InventoryCommon.js:756-761), and its favourite tab has a sale lock that keeps favourites out of an NPC's sell list
    ///   (NpcStore.js:511). Loki's "All" view still lists everything, favourites first.
    ///   The roulette button (Cash shop page) opens Game/Roulette.cs.
    /// Hooks in WristMenu.cs: Build (BuildW4Items), the item details case (RefreshW4ItemsDetail), SortedItems (W4ItemsFilter) and the
    /// list case (RefreshW4ItemsList).
    /// </summary>
    public sealed partial class WristMenu
    {
        private UiButton _w4compareBtn, _w4saleLockBtn, _w4rouletteBtn; private bool _w4comparing; private int _w4compareIdx = -1;
        /// <summary>The roulette window's opener (wired by LokiGame.W4Items.cs).</summary>
        public System.Action OpenRoulette;
        /// <summary>The official favourite tab's sale lock: favourites stay out of NPC sell lists. Saved per user (PlayerPrefs).</summary>
        public static bool SaleLock
        {
            get { return PlayerPrefs.GetInt("loki.w4items.salelock", 0) != 0; }
            set { PlayerPrefs.SetInt("loki.w4items.salelock", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        private void BuildW4Items()
        {
            float top = PH / 2;
            // item details: between "★ Favourite" (left) and "Swap set" (right) on the upper button row
            _w4compareBtn = UiKit.Button(_itemDetail, Lang.T("Compare"), new Vector2(0, -top + 180), new Vector2(170, 56), () => { _w4comparing = !_w4comparing; _dirty = true; }, 21);
            _w4compareBtn.gameObject.SetActive(false);
            // the favourite tab: the sale lock takes the sort button's place (the official favourite tab has both: InventoryCommon.js:337-340)
            _w4saleLockBtn = UiKit.Button(_itemList, "", new Vector2(W / 2 - 100, top - 32), new Vector2(150, 48), () => { SaleLock = !SaleLock; Toast(SaleLock ? Lang.T("Favourites won't show in NPC sell lists.") : Lang.T("Favourites can be sold to NPCs again.")); _dirty = true; }, 16);
            _w4saleLockBtn.gameObject.SetActive(false);
            // Cash shop page: the roulette (the official client opens it from its own icon; 0x0a19)
            _w4rouletteBtn = UiKit.Button(_pages[Tab.CashShop], Lang.T("Roulette"), new Vector2(0, -top + 60 + 72), new Vector2(W - 90, 62), () => { OpenRoulette?.Invoke(); _dirty = true; }, 24);
        }

        /// <summary>SortedItems' filter for the Use / Equip / Etc tabs: favourites live in their own tab only.</summary>
        private static bool W4ItemsInTypeTab(Inventory.Entry e) => !e.Favorite;

        /// <summary>The list page: the favourite tab shows the sale lock instead of the sort button.</summary>
        private void RefreshW4ItemsList()
        {
            if (_w4saleLockBtn == null) return;
            bool fav = _itemFilter == 4;
            _w4saleLockBtn.gameObject.SetActive(fav); _sortBtn.gameObject.SetActive(!fav);
            if (fav) _w4saleLockBtn.SetLabel(SaleLock ? Lang.T("Sale lock: on") : Lang.T("Sale lock: off"));
        }

        /// <summary>The worn items an unworn candidate would replace or sit beside: everything worn at any of its positions.</summary>
        public List<Inventory.Entry> W4CompareTargets(Inventory.Entry e)
        {
            var l = new List<Inventory.Entry>();
            if (e == null || !e.IsEquip || e.Wearing != 0) return l;
            uint mask = e.Location;
            var side = W3EquipSides(e); if (side.HasValue) mask |= side.Value.r | side.Value.l;
            foreach (var w in _inv.Items.Values.OrderBy(x => x.Wearing)) if (w.Wearing != 0 && (w.Wearing & mask) != 0 && w.Index != e.Index) l.Add(w);
            return l;
        }

        /// <summary>The details' compare view: the candidate's name line, then each worn item's name, its equipment block and description.</summary>
        public static string W4CompareText(Inventory.Entry candidate, List<Inventory.Entry> worn)
        {
            var sb = new StringBuilder();
            sb.Append("<color=#9aa3b5>").Append(Lang.F("Compared with what you wear at {0}:", EquipmentWindow.SlotNames(candidate.Location))).Append("</color>\n");
            if (worn.Count == 0) { sb.Append(Lang.T("<color=#7fd67f>Nothing is worn there: this would fill an empty slot.</color>")).Append('\n'); return sb.ToString(); }
            foreach (var w in worn)
            {
                sb.Append("<color=#ffd060>").Append(Inventory.FullName(w)).Append("</color>\n");
                sb.Append(W3EquipBlock(w));
                var info = w.Info;
                sb.Append(string.IsNullOrEmpty(info.Description) ? Lang.T("<color=#8890a0>No description in the client data.</color>") : NpcDialog.DataText(info.Description)).Append('\n');
            }
            return sb.ToString();
        }

        private void RefreshW4ItemsDetail(Inventory.Entry e)
        {
            if (_w4compareBtn == null) return;
            if (_w4compareIdx != e.Index) { _w4compareIdx = e.Index; _w4comparing = false; }
            var worn = W4CompareTargets(e);
            bool can = e.IsEquip && e.Wearing == 0 && !_onBoard; // (client wave 11: on the board the worn item's card is beside the details at once)
            _w4compareBtn.gameObject.SetActive(can);
            if (!can) { _w4comparing = false; return; }
            // client wave 11 (w11menu): the worn items' cards open BESIDE the details (the design's 2026-10-06 playtest: "both of their stats
            // and names and descriptions should be open simultaneously"); the description stays where it is (it was swapped for the
            // worn item's text): WristMenu.W11.cs W11AfterRefresh
            _w4compareBtn.SetLabel(_w4comparing ? Lang.T("Hide comparison") : worn.Count > 0 ? Lang.T("Compare") : Lang.T("Compare (empty)"));
        }

    }
}
