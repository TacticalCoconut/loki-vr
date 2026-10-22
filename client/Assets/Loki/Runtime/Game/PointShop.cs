using System;
using System.Collections.Generic;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// NPC cash / item / point shops (client wave 2; rAthena e985006 clif.cpp:17409-17545, npc.cpp:2427-2666, research §C): all three
    /// NPC types open the same window. ZC_PC_CASH_POINT_ITEMLIST 0x0287 len.W cash.L kafra.L {price.L discount.L type.B itemId.L}* (13-byte
    /// records; sets sd->npc_shopid) → CZ_PC_BUY_CASH_POINT_ITEM 0x0288 len.W kafraPoints.L count.W {amount.W itemId.L}* →
    /// ZC_PC_CASH_POINT_UPDATE 0x0289 cash.L kafra.L result.W (0 ok, 1 no shop / NPC, 3 weight or slots, 4 trading, 5 bad item,
    /// 6 not enough points, 8 some items failed). "cash" is the cash points, the count of the currency item (itemshop) or the
    /// variable's value (pointshop): npc_shop_currency_type, npc.cpp:2614-2665. Kafra points only exist for real cash shops (an item /
    /// point shop always sends 0 there, and any kafra points make its purchase fail). Closing sends CZ_NPC_TRADE_QUIT 0x09d4 (clears
    /// npc_shopid).
    /// Client wave 4 (w4items, re-audit §3): the packet never names the currency. rAthena names it only in a blue broadcast it sends
    /// just before the list ("Item Shop List: %s (%u)" / "Point Shop List: '%s'", map_msg 714 / 715, npc.cpp:2638-2658): that text
    /// reaches the chat like any broadcast and is NOT parsed here any more (a SpacetimeDB module need not send it). The window shows
    /// the balance as the packet gives it, as roBrowserLegacy's generic cash-shop window does (Engine/MapEngine/Store.js:31-57, a
    /// reimplementation), and offers Kafra points only when the packet carries some.
    /// </summary>
    public sealed class PointShop
    {
        private sealed class Offer { public int ItemId, Price, Type; }
        private readonly List<Offer> _offers = new List<Offer>();
        private readonly Dictionary<int, int> _cart = new Dictionary<int, int>();
        public long Cash { get; private set; } = -1; public long Kafra { get; private set; } = -1;
        /// <summary>Kafra points can be spent here: the packet carries some (only a real cash shop does).</summary>
        public bool KafraOffered => Kafra > 0;
        public bool IsOpen { get; private set; }
        public int LastResult = -1; public int OfferCount => _offers.Count;
        private bool _useKafra;
        private readonly RoSession _s; private readonly ListWindow _win; private readonly Func<Camera> _head; private readonly Action<string, string> _chat; private readonly Action _dismissDialog;

        public PointShop(RoSession s, ListWindow win, Func<Camera> head, Action<string, string> chat, Action dismissDialog)
        {
            _s = s; _win = win; _head = head; _chat = chat; _dismissDialog = dismissDialog;
            _win.OnCloseClicked = Close;
        }

        public void Reset() { IsOpen = false; _cart.Clear(); _win.Hide(); }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x0287:
                {
                    r.Skip(4); Cash = r.U32(); Kafra = r.U32(); _offers.Clear(); _cart.Clear();
                    while (r.Remaining >= 13) { var of = new Offer { Price = (int)r.U32() }; r.U32(); of.Type = r.U8(); of.ItemId = (int)r.U32(); _offers.Add(of); }
                    _useKafra = false; IsOpen = true; _dismissDialog?.Invoke();
                    Debug.Log($"[PointShop] shop: {_offers.Count} items, balance {Cash} (kafra {Kafra})");
                    _win.Open(_head()); Refresh();
                    return true;
                }
                case 0x0289:
                {
                    r.Skip(2); Cash = r.U32(); Kafra = r.U32(); LastResult = r.U16();
                    string m = LastResult == 0 ? Lang.T("Purchase complete.") : LastResult == 1 ? Lang.T("The shop isn't open any more.") : LastResult == 3 ? ClientText.Msg(56)
                             : LastResult == 4 ? Lang.T("You can't buy while trading.") : LastResult == 5 ? Lang.T("That item isn't sold here.") : LastResult == 6 ? Lang.T("Your balance isn't enough for that.")
                             : LastResult == 8 ? Lang.T("Some items could not be bought (is your balance enough?).") : Lang.F("The purchase failed ({0}).", LastResult);
                    _chat(m, LastResult == 0 ? "9fe39a" : "ffb070");
                    Debug.Log($"[PointShop] result {LastResult}, balance {Cash} (kafra {Kafra})");
                    if (LastResult == 0) _cart.Clear();
                    Refresh(); return true;
                }
                default: return false;
            }
        }

        private long Total() { long t = 0; foreach (var kv in _cart) { var of = _offers.Find(x => x.ItemId == kv.Key); if (of != null) t += (long)of.Price * kv.Value; } return t; }

        public void Buy()
        {
            if (_cart.Count == 0) return;
            long total = Total();
            uint kafra = KafraOffered && _useKafra ? (uint)Math.Min(Math.Max(0, Kafra), total) : 0u;
            var w = new PacketWriter(0x0288).U16(0).U32(kafra).U16((ushort)_cart.Count);
            foreach (var kv in _cart) w.U16((ushort)kv.Value).U32((uint)kv.Key);
            _s.SendPacket(w.ToArrayWithLength());
            Debug.Log($"[PointShop] buy {_cart.Count} line(s), total {total}, kafra {kafra}");
        }

        public void Close() { if (IsOpen) _s.ShopClose(); IsOpen = false; _cart.Clear(); _win.Hide(); Debug.Log("[PointShop] closed"); }

        private static bool Stackable(int type) => !(type == 4 || type == 5 || type == 7 || type == 8 || type == 12);
        private void Add(int itemId, int n, int max) { _cart.TryGetValue(itemId, out int have); int v = Mathf.Clamp(have + n, 0, max); if (v == 0) _cart.Remove(itemId); else _cart[itemId] = v; Refresh(); }

        public void Refresh()
        {
            if (!_win.Visible || !IsOpen) return;
            var rows = new List<ListWindow.Row>();
            long total = Total();
            foreach (var of in _offers)
            {
                var info = GameDb.GetItem(of.ItemId); _cart.TryGetValue(of.ItemId, out int n); var o2 = of;
                int max = Stackable(of.Type) ? 30000 : 1; // non-stackables are forced to 1 per line (npc_cashshop_buylist)
                rows.Add(new ListWindow.Row
                {
                    Icon = WristMenu.ItemIcon(info), Highlight = n > 0,
                    Text = $"{info.Display}\n<color=#ffd060>{Lang.F("price {0:N0}", of.Price)}</color>{(n > 0 ? "   " + Lang.F("<color=#7fd67f>in cart: {0}</color>", n) : "")}",
                    A = "+1", OnA = () => Add(o2.ItemId, 1, max), AEnabled = n < max,
                    B = Stackable(of.Type) ? "+10" : n > 0 ? "−1" : null, OnB = Stackable(of.Type) ? (Action)(() => Add(o2.ItemId, 10, max)) : () => Add(o2.ItemId, -1, max),
                });
            }
            if (KafraOffered) rows.Add(new ListWindow.Row { Text = Lang.F("Pay with Kafra points too ({0:N0} available)", Kafra), A = _useKafra ? Lang.T("~state~On") : Lang.T("~state~Off"), OnA = () => { _useKafra = !_useKafra; Refresh(); }, Highlight = _useKafra });
            long pay = KafraOffered && _useKafra ? Math.Max(0, total - Math.Min(Kafra, total)) : total;
            bool afford = pay <= Cash;
            _win.SetTitle(Lang.T("Shop"));
            string line = KafraOffered ? Lang.F("Cart {0:N0}  ·  your balance {1:N0}  ·  Kafra points {2:N0}", total, Cash, Kafra) : Lang.F("Cart {0:N0}  ·  your balance {1:N0}", total, Cash);
            _win.SetInfo(afford ? line : "<color=#ff7a6a>" + line + "</color>");
            _win.SetTabs(null, 0, null); _win.SetInput(false);
            _win.SetFooter((Lang.T("Close"), Close), (_cart.Count > 0 ? Lang.T("Clear") : null, () => { _cart.Clear(); Refresh(); }), (_cart.Count > 0 && afford ? Lang.T("Buy") : null, Buy));
            _win.SetRows(rows);
        }

    }
}
