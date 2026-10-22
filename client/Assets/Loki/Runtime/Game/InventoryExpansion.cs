using System;
using System.Collections.Generic;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Inventory expansion (client wave 2; rAthena e985006 clif.cpp:23054-23189, research/items_combat.md §C): 100 slots, at most 200.
    /// CZ_INVENTORY_EXPAND 0x0b14 → ZC_ACK_INVENTORY_EXPAND 0x0b15 result.B itemId.L (0 = confirm using that coupon — 25791, 25792 or 25793,
    /// +10 slots each —, 1 failed, 2 busy, 3 no coupon (also when already full), 4 maximum) → CZ_INVENTORY_EXPAND_CONFIRMED 0x0b16 →
    /// ZC_ACK_INVENTORY_EXPAND_RESULT 0x0b17 result.B (same codes; 0 = done, then ZC_EXTEND_BODYITEM_SIZE 0x0b18 with the new size), or
    /// CZ_INVENTORY_EXPAND_REJECTED 0x0b19 (no answer). While a confirmation is pending the server counts the player as busy.
    /// </summary>
    public sealed class InventoryExpansion
    {
        public int LastAsk = -1, LastResult = -1; public int CouponId { get; private set; }
        public bool Asking { get; private set; }
        private readonly RoSession _s; private readonly Inventory _inv; private readonly ListWindow _win; private readonly Func<Camera> _head; private readonly Action<string, string> _chat;

        public InventoryExpansion(RoSession s, Inventory inv, ListWindow win, Func<Camera> head, Action<string, string> chat)
        {
            _s = s; _inv = inv; _win = win; _head = head; _chat = chat;
            _win.OnCloseClicked = () => Answer(false);
        }

        public void Reset() { Asking = false; _win.Hide(); } // a warp clears the pending state on the server (pc.cpp:6957)

        public void Request() { _s.SendPacket(new PacketWriter(0x0b14).ToArray()); Debug.Log("[Expand] request"); }

        private static string Text(int r) => r == 0 ? "" : r == 1 ? Lang.T("The expansion failed.") : r == 2 ? Lang.T("You're busy — finish what you're doing first.") : r == 3 ? Lang.T("You need an inventory expansion coupon (or your inventory is already as big as it gets).") : r == 4 ? Lang.T("Your inventory is already as big as it gets (200).") : Lang.F("Expansion refused ({0}).", r);

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x0b15:
                {
                    r.Skip(2); LastAsk = r.U8(); CouponId = (int)r.U32();
                    Debug.Log($"[Expand] ask {LastAsk} coupon {CouponId}");
                    if (LastAsk != 0) { _chat(Text(LastAsk), "ffb070"); return true; }
                    Asking = true;
                    _win.SetTitle(Lang.T("Expand the inventory")); _win.SetTabs(null, 0, null); _win.SetInput(false);
                    _win.SetInfo(Lang.F("Now {0} slots, at most 200.", _inv.Capacity));
                    _win.SetRows(new List<ListWindow.Row> { new ListWindow.Row { Icon = WristMenu.ItemIcon(GameDb.GetItem(CouponId)), Text = Lang.F("Use 1 × {0}\nfor 10 more slots ({1} → {2})", GameDb.GetItem(CouponId).Name, _inv.Capacity, Mathf.Min(200, _inv.Capacity + 10)), Highlight = true } }, true);
                    _win.SetFooter((Lang.T("Cancel"), () => Answer(false)), (Lang.T("Expand"), () => Answer(true)));
                    _win.Open(_head());
                    return true;
                }
                case 0x0b17:
                {
                    r.Skip(2); LastResult = r.U8();
                    _chat(LastResult == 0 ? Lang.T("Your inventory grew by 10 slots.") : Text(LastResult), LastResult == 0 ? "9fe39a" : "ffb070");
                    Debug.Log($"[Expand] result {LastResult}");
                    return true;
                }
                default: return false;
            }
        }

        public void Answer(bool yes)
        {
            if (Asking) { _s.SendPacket(new PacketWriter((ushort)(yes ? 0x0b16 : 0x0b19)).ToArray()); Debug.Log("[Expand] " + (yes ? "confirm" : "reject")); }
            Asking = false; _win.Hide();
        }
    }
}
