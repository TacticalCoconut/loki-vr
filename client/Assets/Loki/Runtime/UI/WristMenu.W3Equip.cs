using System.Collections.Generic;
using System.Text;
using Loki.Game;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 3 (w3equip): the Items page's "Equipment" button and equipment page (<see cref="EquipmentWindow"/>), and the
    /// item details' additions: the side choice for items rAthena puts on ONE of two positions, and a block with the refine
    /// level, the enchant grade, the cards / enchants and the random options.
    ///   Side choice (CZ_REQ_WEAR_EQUIP_V5 0x0998 index.W position.L with one side's bit): an accessory whose mask is EQP_ACC (both
    ///   sides) goes to the requested side (pc.cpp:12088-12107; a card whose own position is one side overrides it, 12093-12106), a
    ///   shadow accessory likewise (12116-12119), and a one-handed dagger / sword / axe that rAthena reports as EQP_ARMS (a character
    ///   who can dual-wield: pc_equippoint_sub, pc.cpp:1464-1482) goes to the requested hand (12108-12115). The weapon's SubType comes
    ///   from the client data (itemviews.tsv, AppearanceDb.WeaponSubType) because a two-handed weapon has the same EQP_ARMS mask
    ///   and always takes both hands. Everything else keeps the single Equip button (the item goes into all of its positions).
    /// Hooks in WristMenu.cs: BuildItems (BuildW3Equip), the Refresh cases of the Items page (detail / equipment / list).
    /// </summary>
    public sealed partial class WristMenu
    {
        private EquipmentWindow _w3equip; private bool _w3equipOpen;
        private UiButton _w3equipBtn, _w3equipSideR, _w3equipSideL;
        private uint _w3equipBitR, _w3equipBitL;
        /// <summary>The equipment page (null before Build).</summary>
        public EquipmentWindow Equipment => _w3equip;
        public bool EquipmentOpen => ((_w3equipOpen && _tab == Tab.Items) || CharWide) && gameObject.activeSelf && _detailIndex < 0; // (client wave 20: the wide Character board shows it beside Status and Stats)

        private void BuildW3Equip(RectTransform page)
        {
            float top = PH / 2;
            // between the last item row (bottom at -top+130) and the page buttons (top at -top+68)
            _w3equipBtn = UiKit.Button(_itemList, Lang.T("Equipment"), new Vector2(0, -top + 99), new Vector2(W - 50, 50), () => OpenEquipment(), 22);
            _w3equip = new EquipmentWindow(_inv, _ps, _s, m => Toast(m), idx => OpenDetailsFromBag(idx), // (client wave 13, item 11: on the board, a step of its history)
                                           () => { _w3equipOpen = false; _dirty = true; W15GearBack(); }, () => _dirty = true, W - 30, PH);
            _w3equip.Build(page);
            _w3equipSideR = UiKit.Button(_itemDetail, Lang.T("Equip right"), new Vector2(-W / 2 + 120, -top + 110), new Vector2(180, 60), () => W3EquipSide(false), 22);
            _w3equipSideL = UiKit.Button(_itemDetail, Lang.T("Equip left"), new Vector2(W / 2 - 110, -top + 110), new Vector2(180, 60), () => W3EquipSide(true), 22);
            _w3equipSideR.gameObject.SetActive(false); _w3equipSideL.gameObject.SetActive(false);
            _detName.resizeTextForBestFit = true; _detName.resizeTextMinSize = 16; _detName.resizeTextMaxSize = 26; // "+7 [B] Name [card] [3 options]" fits on two lines
        }

        /// <summary>Client wave 15 (w15gear), point 3: the equipment's Back on a board that has no bag (the Character board) goes to the bag on
        /// its own board (the Items group's), as Back does on the Items board.</summary>
        private void W15GearBack()
        {
            if (_onBoard && gameObject.activeSelf && GroupOf(Tab.Items, false, _group).group != _group) Show(Tab.Items);
        }

        /// <summary>Opens the equipment page of the Items tab (the wrist menu opens if it was closed).</summary>
        public void OpenEquipment()
        {
            _w3equipOpen = true; _detailIndex = -1; _w3equip?.Reset();
            Show(Tab.Items);
            Debug.Log("[Equip] equipment page opened");
        }

        // ---- Refresh hooks (WristMenu.cs, the Items cases)
        private void RefreshW3Equip()
        {
            _itemList.gameObject.SetActive(false); _itemDetail.gameObject.SetActive(false);
            _w3equip.Show(true); _w3equip.Refresh();
        }

        private void W3EquipListShown()
        {
            _w3equip?.Show(false);
            if (_w3equipBtn == null) return;
            int worn = 0; foreach (var e in _inv.Items.Values) if (e.Wearing != 0) worn++;
            _w3equipBtn.SetLabel(worn > 1 ? Lang.F("Equipment  <color=#9aa3b5>·  {0} items worn</color>", worn) : worn == 1 ? Lang.T("Equipment  <color=#9aa3b5>·  1 item worn</color>") : Lang.T("Equipment  <color=#9aa3b5>·  nothing worn</color>"));
        }

        private void RefreshW3EquipDetail(Inventory.Entry e)
        {
            _w3equip?.Show(false);
            var sides = W3EquipSides(e);
            bool two = sides.HasValue;
            _w3equipSideR.gameObject.SetActive(two); _w3equipSideL.gameObject.SetActive(two);
            if (two)
            {
                var (r, l, lr, ll) = sides.Value;
                _w3equipBitR = r; _w3equipBitL = l;
                _detAct.gameObject.SetActive(false);
                bool low = e.Info.MinLevel > _ps.BaseLevel;
                _w3equipSideR.SetLabel(low ? "Lv " + e.Info.MinLevel : lr); _w3equipSideL.SetLabel(low ? "Lv " + e.Info.MinLevel : ll);
                foreach (var b in new[] { _w3equipSideR, _w3equipSideL }) { b.Normal = low ? new Color(0.3f, 0.3f, 0.32f) : UiKit.ButtonBg; b.SetHover(false); }
            }
            string block = W3EquipBlock(e);
            if (block.Length > 0) _detDesc.text = block + _detDesc.text;
        }

        /// <summary>The side choice of <see cref="EquipmentWindow.SideChoice"/> (null: a single Equip button).</summary>
        public static (uint r, uint l, string lr, string ll)? W3EquipSides(Inventory.Entry e) => EquipmentWindow.SideChoice(e);

        private void W3EquipSide(bool left)
        {
            if (_detailIndex < 0 || !_inv.Items.TryGetValue(_detailIndex, out var e)) return;
            uint bit = left ? _w3equipBitL : _w3equipBitR;
            if (e.Info.MinLevel > _ps.BaseLevel) { Toast(Lang.F("{0} needs base level {1} (you are {2}).", e.Info.Name, e.Info.MinLevel, _ps.BaseLevel)); return; }
            Debug.Log($"[Equip] equip {e.ItemId} {e.Info.English} (index {e.Index}, location 0x{e.Location:x}) on the {(left ? "left" : "right")}: 0x0998 position 0x{bit:x}");
            _s.Equip(e.Index, bit);
            Toast($"{e.Info.Name} → {EquipmentWindow.SlotNames(bit)}");
        }

        /// <summary>The details' equipment block: where it is worn / where it goes, refine, grade, cards and enchants, random options.</summary>
        public static string W3EquipBlock(Inventory.Entry e)
        {
            if (e == null || (!e.IsEquip && !e.HasOptions)) return "";
            const string H = "<color=#ffd060>", E = "</color>";
            var sb = new StringBuilder();
            if (e.IsEquip)
            {
                var side = W3EquipSides(e); // one of two positions: "right or left" (the buttons below choose)
                string where = e.Wearing != 0 ? EquipmentWindow.SlotNames(e.Wearing) : side.HasValue ? Lang.F("{0} or {1}", EquipmentWindow.SlotNames(side.Value.r), EquipmentWindow.SlotNames(side.Value.l)) : EquipmentWindow.SlotNames(e.Location);
                sb.Append(H).Append(e.Wearing != 0 ? Lang.T("Worn: ") : Lang.T("Goes to: ")).Append(E).Append(where).Append('\n');
            }
            var parts = new List<string>();
            if (e.Refine > 0) parts.Add($"{H}{Lang.T("Refine")}{E} +{e.Refine}");
            if (e.Grade > 0 && e.Grade <= 4) parts.Add($"{H}{Lang.T("Grade")}{E} <color=#ffb347>{Inventory.GradeName(e.Grade)}</color>");
            if (parts.Count > 0) sb.Append(string.Join("    ", parts)).Append('\n');
            if (e.IsEquip && e.Cards != null)
            {
                int c0 = e.Cards[0];
                if (c0 == 0x00FF || c0 == 0x00FE || c0 == 0xFF00) sb.Append(H).Append(Lang.T("Cards")).Append(E).Append(" ").Append(Lang.T("<color=#9aa3b5>none (a forged, brewed or pet item: its card slots hold its maker)</color>")).Append('\n');
                else
                {
                    int slots = Mathf.Clamp(e.Info.Slots, 0, 4), last = -1;
                    for (int i = 0; i < 4; i++) if (e.Cards[i] > 0) last = i;
                    int n = Mathf.Max(slots, last + 1);
                    if (n > 0)
                    {
                        sb.Append(H).Append(Lang.T("Cards")).Append(E);
                        for (int i = 0; i < n; i++)
                        {
                            int c = e.Cards[i];
                            string what = c > 0 ? $"<color=#9fd0ff>{GameDb.GetItem(c).Name}</color>" : "<color=#6a7080>" + Lang.T("~slot~empty") + "</color>";
                            sb.Append(i < slots ? "  [" : "  <color=#9aa3b5>" + Lang.T("enchant") + "</color> [").Append(what).Append(']');
                        }
                        sb.Append('\n');
                    }
                }
            }
            if (e.HasOptions)
            {
                sb.Append(H).Append(Lang.T("Random options")).Append(E);
                // roBrowserLegacy lists them only for identified items (ItemInfo.js:216); rAthena sends them either way (clif.cpp:2809-2828)
                if (!e.Identified) sb.Append(" ").Append(Lang.T("<color=#9aa3b5>(identify the item to read them)</color>")).Append('\n');
                else
                {
                    sb.Append('\n');
                    foreach (var o in e.Options) if (o.Id > 0) sb.Append("  <color=#b8f0a0>").Append(RandomOptionDb.Format(o.Id, o.Value)).Append("</color>\n");
                }
            }
            if (sb.Length > 0) sb.Append("<color=#555c6a>────────</color>\n");
            return sb.ToString();
        }

    }
}
