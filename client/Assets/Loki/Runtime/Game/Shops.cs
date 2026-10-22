using System;
using System.Collections.Generic;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// NPC shops (research/shops_storage_packets.md; rAthena e985006, PACKETVER 20211103):
    ///   plain shop: 0x00c4 deal type → 0x00c5 → 0x0b77 buy list (19-byte entries) / 0x00c7 sell list (10-byte entries)
    ///               → 0x00c8 / 0x00c9 → 0x00ca / 0x00cb result. 0x09d4 releases sd->npc_shopid whenever the window closes.
    ///   market:     0x0b7a (19-byte entries: id.L type.B price.L qty.L weight.W location.L) → 0x09d6 → 0x0b4e; 0x09d8 closes
    ///               (the only packet that clears state.trading).
    ///   barter:     0x0b78 (31-byte entries: nameid.L type.B stock.L(-1 unlimited) currencyId.L currencyAmount.L weight.L index.L
    ///               viewSprite.W location.L) → 0x0b0f {itemId.L amount.L invIndex.W shopIndex.L}; close 0x0b12.
    ///   expanded:   0x0b79 (items_count.L then 32-byte blocks nameid.L type.W stock.L weight.L index.L zeny.L viewSprite.W location.L
    ///               currency_count.L + currency_count × 12-byte {nameid.L refine.W amount.L type.W}) → 0x0b57 {itemId.L shopIndex.L amount.L};
    ///               close 0x0b58. Both answer with 0x00ca (11 exchange failed, 13 out of stock, 14 not enough goods) and stay open;
    ///               the server takes the currency from unequipped stacks and refuses a purchase that names a shop index twice
    ///               (npc.cpp npc_barter_purchase, clif.cpp:23299-23348 / 23469-23518).
    /// The character can't walk while any of these are open on the server (pc_cant_act), so every way out of the
    /// window sends the matching release packet.
    /// </summary>
    public sealed class Shops
    {
        private enum Kind { None, Prompt, Waiting, Buy, Sell, Market, Barter }
        private sealed class Offer
        {
            public int ItemId, Price, Type, Stock = -1, Index;
            /// <summary>Barter currencies: item id, amount per unit bought, required refine (0 = any/plain).</summary>
            public List<(int itemId, int amount, int refine)> Needs = new List<(int, int, int)>();
        }

        private readonly RoSession _s; private readonly Inventory _inv; private readonly PlayerState _ps;
        private readonly ListWindow _win; private readonly Func<Camera> _head; private readonly Action<string, string> _chat; private readonly Action _dismissDialog;
        private Kind _kind; private uint _npc; private bool _barterExpanded;
        private readonly List<Offer> _offers = new List<Offer>();
        private readonly Dictionary<int, int> _cart = new Dictionary<int, int>(); // buy/market: itemId → amount; sell: inventory index → amount
        public bool IsOpen => _kind != Kind.None;

        public Shops(RoSession s, Inventory inv, PlayerState ps, ListWindow win, Func<Camera> head, Action<string, string> chat, Action dismissDialog)
        {
            _s = s; _inv = inv; _ps = ps; _win = win; _head = head; _chat = chat; _dismissDialog = dismissDialog;
            _win.OnCloseClicked = Close;
            _inv.Changed += () => { if (_kind == Kind.Sell || _kind == Kind.Barter) Refresh(); };
            _ps.Changed += () => { if (_kind == Kind.Buy || _kind == Kind.Market || _kind == Kind.Sell || _kind == Kind.Barter) Refresh(); };
        }

        /// <summary>Map change: the server already dropped npc_shopid / barter flags on warp (unit.cpp:3678, pc.cpp:6958).</summary>
        public void Reset() { _kind = Kind.None; _cart.Clear(); _offers.Clear(); _win.Hide(); }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x00c4: // ZC_SELECT_DEALTYPE npcId.L
                {
                    r.Skip(2); _npc = r.U32();
                    _dismissDialog();
                    _kind = Kind.Prompt; _cart.Clear(); _offers.Clear();
                    Refresh(); _win.Open(_head());
                    Debug.Log($"[Shop] deal type prompt from npc {_npc}");
                    return true;
                }
                case 0x0b77: // ZC_PC_PURCHASE_ITEMLIST len.W {itemId.L price.L discountPrice.L type.B viewSprite.W location.L}
                {
                    r.Skip(4); _offers.Clear(); _cart.Clear();
                    while (r.Remaining >= 19)
                    {
                        var of = new Offer { ItemId = (int)r.U32() }; r.Skip(4); of.Price = (int)r.U32(); of.Type = r.U8(); r.Skip(2); r.Skip(4);
                        _offers.Add(of);
                    }
                    _dismissDialog();
                    _kind = Kind.Buy; Refresh(); _win.Open(_head());
                    Debug.Log($"[Shop] buy list: {_offers.Count} items");
                    return true;
                }
                case 0x00c7: // ZC_PC_SELL_ITEMLIST len.W {index.W price.L overcharge.L}
                {
                    r.Skip(4); _offers.Clear(); _cart.Clear();
                    while (r.Remaining >= 10)
                    {
                        var of = new Offer { Index = r.U16() }; r.Skip(4); of.Price = (int)r.U32();
                        if (_inv.Items.TryGetValue(of.Index, out var e)) { of.ItemId = e.ItemId; of.Type = e.Type; }
                        _offers.Add(of);
                    }
                    _dismissDialog();
                    _kind = Kind.Sell; Refresh(); _win.Open(_head());
                    Debug.Log($"[Shop] sell list: {_offers.Count} items");
                    return true;
                }
                case 0x00ca: // ZC_PC_PURCHASE_RESULT result.B
                {
                    r.Skip(2); int res = r.U8();
                    _chat(BuyResult(res), res == 0 ? "9fe39a" : "ffb070");
                    Debug.Log($"[Shop] purchase result {res}");
                    if (_kind == Kind.Barter)
                    {
                        // the barter stays open (0x0b0f/0x0b57 never clear barter_open); limited stock went down by what was bought
                        if (res == 0) foreach (var kv in _cart) foreach (var of in _offers) if (of.Index == kv.Key && of.Stock >= 0) of.Stock = Math.Max(0, of.Stock - kv.Value);
                        LastResult = res; _cart.Clear(); Refresh();
                        return true;
                    }
                    if (_kind == Kind.Buy || _kind == Kind.Waiting) Finish(); // the server cleared npc_shopid after processing (clif.cpp:12324)
                    return true;
                }
                case 0x00cb: // ZC_PC_SELL_RESULT result.B
                {
                    r.Skip(2); int res = r.U8();
                    _chat(res == 0 ? Lang.T("Sold.") : Lang.T("The sale failed."), res == 0 ? "9fe39a" : "ffb070");
                    Debug.Log($"[Shop] sell result {res}");
                    if (_kind == Kind.Sell || _kind == Kind.Waiting) Finish();
                    return true;
                }
                case 0x0b7a: // ZC_NPC_MARKET_OPEN len.W {nameid.L type.B price.L qty.L weight.W location.L}
                {
                    r.Skip(4); _offers.Clear(); _cart.Clear();
                    while (r.Remaining >= 19)
                    {
                        var of = new Offer { ItemId = (int)r.U32(), Type = r.U8(), Price = (int)r.U32(), Stock = (int)r.U32() }; r.Skip(2); r.Skip(4);
                        _offers.Add(of);
                    }
                    _dismissDialog();
                    _kind = Kind.Market; Refresh(); _win.Open(_head());
                    Debug.Log($"[Shop] market: {_offers.Count} items");
                    return true;
                }
                case 0x0b4e: // ZC_NPC_MARKET_PURCHASE_RESULT len.W result.W {ITID.L qty.W price.L}
                {
                    r.Skip(4); int res = r.U16(); LastMarketResult = res; // 0 ok, 0xffff (-1 as uint16) failed: clif.cpp:2399-2401
                    if (res == 0)
                        while (r.Remaining >= 10)
                        {
                            int itemId = (int)r.U32(); int qty = r.U16(); r.Skip(4);
                            foreach (var of in _offers) if (of.ItemId == itemId && of.Stock >= 0) of.Stock = Math.Max(0, of.Stock - qty);
                        }
                    _chat(res == 0 ? Lang.T("Purchase complete.") : Lang.T("The purchase failed (zeny, weight or stock)."), res == 0 ? "9fe39a" : "ffb070");
                    _cart.Clear(); Refresh();
                    return true;
                }
                case 0x0b78: // ZC_NPC_BARTER_MARKET_ITEMINFO len.W {nameid.L type.B amount.L currencyNameid.L currencyAmount.L weight.L index.L viewSprite.W location.L}
                {
                    _barterExpanded = false; r.Skip(4); _offers.Clear(); _cart.Clear();
                    while (r.Remaining >= 31)
                    {
                        var of = new Offer { ItemId = (int)r.U32(), Type = r.U8(), Stock = r.S32() };
                        int cur = (int)r.U32(); int amt = r.S32(); r.Skip(4); of.Index = r.S32(); r.Skip(2); r.Skip(4);
                        if (cur > 0 && amt > 0) of.Needs.Add((cur, amt, 0));
                        _offers.Add(of);
                    }
                    _dismissDialog();
                    _kind = Kind.Barter; Refresh(); _win.Open(_head());
                    Debug.Log($"[Shop] barter: {_offers.Count} exchanges");
                    return true;
                }
                case 0x0b79: // ZC_NPC_EXPANDED_BARTER_MARKET_ITEMINFO len.W items_count.L {nameid.L type.W amount.L weight.L index.L zeny.L viewSprite.W location.L currency_count.L {nameid.L refine.W amount.L type.W}×n}
                {
                    _barterExpanded = true; r.Skip(4); int count = r.S32(); _offers.Clear(); _cart.Clear();
                    for (int i = 0; i < count && r.Remaining >= 32; i++)
                    {
                        var of = new Offer { ItemId = (int)r.U32(), Type = r.U16(), Stock = r.S32() }; r.Skip(4); of.Index = r.S32(); of.Price = r.S32(); r.Skip(2); r.Skip(4);
                        int cc = r.S32();
                        for (int c = 0; c < cc && r.Remaining >= 12; c++) { int cur = (int)r.U32(); int refine = r.U16(); int amt = r.S32(); r.Skip(2); of.Needs.Add((cur, amt, refine)); }
                        _offers.Add(of);
                    }
                    _dismissDialog();
                    _kind = Kind.Barter; Refresh(); _win.Open(_head());
                    Debug.Log($"[Shop] expanded barter: {_offers.Count} exchanges");
                    return true;
                }
                default: return false;
            }
        }

        // ---------------------------------------------------------------- actions
        private void Close()
        {
            switch (_kind)
            {
                case Kind.Prompt: case Kind.Waiting: case Kind.Buy: case Kind.Sell: _s.ShopClose(); break;
                case Kind.Market: _s.MarketClose(); break;
                case Kind.Barter: _s.BarterClose(_barterExpanded); break;
            }
            Debug.Log($"[Shop] closed ({_kind})");
            _kind = Kind.None; _cart.Clear(); _win.Hide();
        }

        /// <summary>After a buy/sell result: the window closes and 0x09d4 is sent anyway (harmless, never leaves a lock).</summary>
        private void Finish() { _s.ShopClose(); _kind = Kind.None; _cart.Clear(); _win.Hide(); }

        private void Add(int key, int amount, int max)
        {
            _cart.TryGetValue(key, out int have);
            int v = Mathf.Clamp(have + amount, 0, max);
            if (v == 0) _cart.Remove(key); else _cart[key] = v;
            Refresh();
        }

        private void Confirm()
        {
            if (_cart.Count == 0) return;
            var list = new List<(int, int)>();
            foreach (var kv in _cart) list.Add((kv.Key, kv.Value));
            Debug.Log($"[Shop] confirm {_kind}: {list.Count} lines");
            switch (_kind)
            {
                case Kind.Buy: _s.ShopBuy(list); _kind = Kind.Waiting; break;
                case Kind.Sell: _s.ShopSell(list); _kind = Kind.Waiting; break;
                case Kind.Market: _s.MarketBuy(list); break;
                case Kind.Barter:
                {
                    if (_barterExpanded)
                    {
                        var l = new List<(int, int, int)>();
                        foreach (var kv in _cart) { var of = OfferByIndex(kv.Key); if (of != null) l.Add((of.ItemId, of.Index, kv.Value)); }
                        _s.BarterExpandedBuy(l);
                    }
                    else
                    {
                        var l = new List<(int, int, int, int)>();
                        foreach (var kv in _cart) { var of = OfferByIndex(kv.Key); if (of != null) l.Add((of.ItemId, kv.Value, of.Needs.Count > 0 ? FirstStack(of.Needs[0].itemId, of.Needs[0].refine) : 0, of.Index)); }
                        _s.BarterBuy(l);
                    }
                    break;
                }
            }
            Refresh();
        }

        private Offer OfferByIndex(int shopIndex) { foreach (var of in _offers) if (of.Index == shopIndex) return of; return null; }

        /// <summary>How many of an item the barter can take: unequipped stacks, matching the refine when one is required
        /// (npc.cpp npc_barter_purchase; equip-switch and favourite flags are not tracked here).</summary>
        private int Have(int itemId, int refine)
        {
            int n = 0;
            foreach (var e in _inv.Items.Values) if (e.ItemId == itemId && e.Wearing == 0 && (refine <= 0 || e.Refine == refine)) n += e.Amount;
            return n;
        }
        private int FirstStack(int itemId, int refine)
        {
            foreach (var e in _inv.Items.Values) if (e.ItemId == itemId && e.Wearing == 0 && (refine <= 0 || e.Refine == refine)) return e.Index;
            return 0;
        }

        /// <summary>Most units of one exchange the inventory, zeny and stock allow, given what the rest of the cart already claims.</summary>
        private int BarterMax(Offer of)
        {
            int max = of.Stock >= 0 ? of.Stock : (Stackable(of.Type) ? 500 : 20);
            if (of.Price > 0) { long z = _ps.Zeny; foreach (var kv in _cart) { var o = OfferByIndex(kv.Key); if (o != null && o != of) z -= (long)o.Price * kv.Value; } max = (int)Math.Min(max, Math.Max(0, z) / of.Price); }
            foreach (var need in of.Needs)
            {
                int have = Have(need.itemId, need.refine);
                foreach (var kv in _cart) { var o = OfferByIndex(kv.Key); if (o == null || o == of) continue; foreach (var n2 in o.Needs) if (n2.itemId == need.itemId) have -= n2.amount * kv.Value; }
                max = Math.Min(max, Math.Max(0, have) / Math.Max(need.amount, 1));
            }
            return max;
        }

        // ---- smoke-test hooks (drive the window like the pointer would) ----
        public string State => _kind.ToString();
        public int OfferCount => _offers.Count;
        public bool IsExpandedBarter => _barterExpanded;
        /// <summary>Last 0x00ca result while a barter was open (-1 = none yet).</summary>
        public int LastResult = -1;
        /// <summary>Client wave 4 smoke: the last ZC_NPC_MARKET_PURCHASE_RESULT result (-1 none yet; 0 ok, 65535 failed).</summary>
        public int LastMarketResult = -1;

        private static bool Stackable(int type) => !(type == 4 || type == 5 || type == 7 || type == 8 || type == 12); // armor, weapon, pet egg/armor, shadow gear

        // ---------------------------------------------------------------- view
        private void Refresh()
        {
            var rows = new List<ListWindow.Row>();
            long total = 0; int weight = 0;
            switch (_kind)
            {
                case Kind.Prompt:
                    _win.SetTitle(Lang.T("Shop"));
                    _win.SetInfo(Lang.T("Would you like to buy or sell?"));
                    _win.SetTabs(null, 0, null);
                    _win.SetFooter((Lang.T("Cancel"), Close), (Lang.T("Sell"), () => { _kind = Kind.Waiting; _s.ShopDealType(_npc, true); Refresh(); }), (Lang.T("Buy"), () => { _kind = Kind.Waiting; _s.ShopDealType(_npc, false); Refresh(); }));
                    break;
                case Kind.Waiting:
                    _win.SetInfo("…");
                    _win.SetFooter((Lang.T("Close"), Close));
                    break;
                case Kind.Buy: case Kind.Market:
                {
                    foreach (var of in _offers)
                    {
                        var info = GameDb.GetItem(of.ItemId);
                        _cart.TryGetValue(of.ItemId, out int n);
                        total += (long)of.Price * n;
                        string stock = of.Stock >= 0 ? "   " + Lang.F("<color=#9aa3b5>stock {0}</color>", of.Stock) : "";
                        int max = of.Stock >= 0 ? of.Stock : (Stackable(of.Type) ? 30000 : 99);
                        var o2 = of;
                        rows.Add(new ListWindow.Row
                        {
                            Icon = WristMenu.ItemIcon(info), Highlight = n > 0,
                            Text = $"{info.Display}\n<color=#ffd060>{of.Price:N0} z</color>{stock}{(n > 0 ? "   " + Lang.F("<color=#7fd67f>in cart: {0}</color>", n) : "")}",
                            A = "+1", OnA = () => Add(o2.ItemId, 1, max), AEnabled = n < max,
                            B = Stackable(of.Type) ? "+10" : (n > 0 ? "-1" : null), OnB = Stackable(of.Type) ? (Action)(() => Add(o2.ItemId, 10, max)) : () => Add(o2.ItemId, -1, max),
                            BEnabled = Stackable(of.Type) ? n < max : true,
                        });
                    }
                    bool afford = total <= _ps.Zeny;
                    _win.SetTitle(_kind == Kind.Market ? Lang.T("Market") : Lang.T("Buy"));
                    // w4lang: "cart   you have   weight" are three units (the cart's colour picks one of two keys)
                    _win.SetInfo((afford ? Lang.F("Cart: <color=#ffd060>{0:N0} z</color>", total) : Lang.F("Cart: <color=#ff7a6a>{0:N0} z</color>", total)) + "   " + Lang.F("You have {0:N0} z", _ps.Zeny) + "   " + Lang.F("Weight {0}/{1}", _ps.Weight / 10, _ps.MaxWeight / 10));
                    _win.SetTabs(null, 0, null);
                    _win.SetFooter((Lang.T("Close"), Close), (_cart.Count > 0 ? Lang.T("Clear") : null, () => { _cart.Clear(); Refresh(); }), (_cart.Count > 0 && afford ? Lang.T("Buy") : null, Confirm));
                    break;
                }
                case Kind.Sell:
                {
                    foreach (var of in _offers)
                    {
                        if (!_inv.Items.TryGetValue(of.Index, out var e)) continue;
                        if (e.Wearing != 0) continue; // never offer worn gear
                        if (e.Favorite && WristMenu.SaleLock) continue; // client wave 4: the favourite tab's sale lock (roBrowserLegacy NpcStore.js:511)
                        var info = e.Info;
                        _cart.TryGetValue(of.Index, out int n);
                        total += (long)of.Price * n;
                        var o2 = of; int have = e.Amount;
                        rows.Add(new ListWindow.Row
                        {
                            Icon = WristMenu.ItemIcon(info), Highlight = n > 0,
                            Text = $"{Inventory.FullName(e)}  <color=#ffd060>x{have}</color>\n{Lang.F("<color=#ffd060>{0:N0} z each</color>", of.Price)}{(n > 0 ? "   " + Lang.F("<color=#7fd67f>selling {0}</color>", n) : "")}",
                            A = "+1", OnA = () => Add(o2.Index, 1, have), AEnabled = n < have,
                            B = have > 1 ? Lang.T("~amount~All") : (n > 0 ? "-1" : null), OnB = have > 1 ? (Action)(() => Add(o2.Index, have, have)) : () => Add(o2.Index, -1, have),
                        });
                    }
                    _win.SetTitle(Lang.T("Sell"));
                    _win.SetInfo(Lang.F("You get: <color=#ffd060>{0:N0} z</color>", total) + "   " + Lang.F("You have {0:N0} z", _ps.Zeny));
                    _win.SetTabs(null, 0, null);
                    _win.SetFooter((Lang.T("Close"), Close), (_cart.Count > 0 ? Lang.T("Clear") : null, () => { _cart.Clear(); Refresh(); }), (_cart.Count > 0 ? Lang.T("Sell") : null, Confirm));
                    break;
                }
                case Kind.Barter:
                {
                    bool ok = _cart.Count > 0;
                    foreach (var of in _offers)
                    {
                        var info = GameDb.GetItem(of.ItemId);
                        _cart.TryGetValue(of.Index, out int n);
                        total += (long)of.Price * n;
                        int max = BarterMax(of); // the line's ceiling (the rest of the cart's claims on the same currency are deducted)
                        if (n > max) ok = false;
                        var cost = new System.Text.StringBuilder();
                        foreach (var need in of.Needs)
                        {
                            var ni = GameDb.GetItem(need.itemId); int have = Have(need.itemId, need.refine);
                            bool enough = have >= need.amount * Math.Max(n, 1);
                            if (cost.Length > 0) cost.Append(" + ");
                            cost.Append($"<color={(enough ? "#e6e6e6" : "#ff7a6a")}>{need.amount}× {(need.refine > 0 ? $"+{need.refine} " : "")}{ni.Name}</color> ").Append(Lang.F("<color=#9aa3b5>(have {0})</color>", have)); // short names: several currencies share one line
                        }
                        if (of.Price > 0) { if (cost.Length > 0) cost.Append("  +  "); cost.Append($"<color=#ffd060>{of.Price:N0} z</color>"); }
                        string stock = of.Stock >= 0 ? "   " + Lang.F("<color=#9aa3b5>stock {0}</color>", of.Stock) : "";
                        var o2 = of;
                        rows.Add(new ListWindow.Row
                        {
                            Icon = WristMenu.ItemIcon(info), Highlight = n > 0,
                            Text = $"{info.Display}{stock}{(n > 0 ? "   " + Lang.F("<color=#7fd67f>exchanging {0}</color>", n) : "")}\n{cost}",
                            A = "+1", OnA = () => Add(o2.Index, 1, BarterMax(o2)), AEnabled = n < max,
                            B = n > 0 ? "-1" : null, OnB = () => Add(o2.Index, -1, max),
                        });
                    }
                    bool afford = total <= _ps.Zeny;
                    _win.SetTitle(Lang.T("Exchange"));
                    // w4lang: independent units joined by "   " (the zeny unit's colour picks one of two keys)
                    _win.SetInfo(Lang.T("Trade items for items.") + (total > 0 ? "   " + (afford ? Lang.F("Zeny: <color=#ffd060>{0:N0} z</color> of {1:N0}", total, _ps.Zeny) : Lang.F("Zeny: <color=#ff7a6a>{0:N0} z</color> of {1:N0}", total, _ps.Zeny)) : "") + "   " + Lang.F("Weight {0}/{1}", _ps.Weight / 10, _ps.MaxWeight / 10));
                    _win.SetTabs(null, 0, null);
                    _win.SetFooter((Lang.T("Close"), Close), (_cart.Count > 0 ? Lang.T("Clear") : null, () => { _cart.Clear(); Refresh(); }), (ok && afford ? Lang.T("~verb~Exchange") : null, Confirm));
                    break;
                }
                default: return;
            }
            _ = weight;
            _win.SetRows(rows);
        }

        private static string BuyResult(int r)
        {
            // clif.cpp:12274-12286 / clif.hpp e_purchase_result
            switch (r)
            {
                case 0: return Lang.T("Purchase complete.");
                case 1: return Lang.T("You don't have enough zeny.");
                case 2: return Lang.T("You can't carry that much weight.");
                case 3: return Lang.T("You can't carry any more items.");
                case 4: case 13: return Lang.T("That item is out of stock.");
                case 9: return Lang.T("You can't buy that many of one item.");
                case 11: return Lang.T("The exchange failed.");
                case 12: return Lang.T("Exchange complete.");
                case 14: return Lang.T("You don't have the items needed for that exchange.");
                default: return Lang.F("The purchase failed ({0}).", r);
            }
        }
    }
}
