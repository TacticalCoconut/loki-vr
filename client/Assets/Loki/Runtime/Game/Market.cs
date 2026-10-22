using System;
using System.Collections.Generic;
using System.Linq;
using Loki.Net;
using Loki.Sprites;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Player shops (research/trade_cart_vending_packets.md §3–4; rAthena e985006, PACKETVER 20211103).
    /// Vending (stock = the cart): cast MC_VENDING 41 → 0x012d slots.W (2 + level, ≤ 12; may be preceded by 0x012b + a fresh cart list)
    ///   → 0x01b2 (len, title[80], open.B, {cartIndex.W amount.W price.L}) → 0x0b40 own list + 0x0a28 result (0 ok, 1 failed); others get
    ///   0x0131 (aid, title[80]) / 0x0132 (aid) boards. Buyer: 0x0130 (vendor aid) → 0x0b3d (aid, shopId, {price.L amount.W index.W type.B
    ///   itemId.L identified damaged cards options location.L viewSprite.W refine grade} × 64 B) → 0x0801 (len, aid, shopId, {count.W index.W})
    ///   → success is only 0x0b41 + 0x00b1; failure 0x0135 (index amount result: 1 zeny, 2 weight, 4 stock, 6 shop changed).
    ///   Vendor per sold line: 0x0125 + 0x0121 (cart) + 0x09e5 (index amount buyerCid date zeny) + 0x00b1. Close 0x012e.
    /// Buying store (skill ALL_BUYING_STORE 2535, consumes a Buy Market Permit 6377): 0x0810 slots.B → 0x0811 (len, zenyLimit.L, open.B,
    ///   title[80], {itemId.L amount.W price.L}) → 0x0813 own list (aid limit {price amount type itemId} × 11 B) or 0x0812 (result.W weight.L);
    ///   boards 0x0814 / 0x0816. Seller: 0x0817 (aid) → 0x0818 (aid, storeId, limit, lines) → 0x0819 (len, aid, storeId, {invIndex.W itemId.L
    ///   amount.W}) → seller 0x081c (index amount price) + 0x00b1 (no 0x07fa!), buyer 0x0b41 + 0x00b1 + 0x09e6 (itemId amount zeny limit
    ///   sellerCid time); 0x081a (result.W 3 limit / 4 all bought) closes the store; seller failure 0x0824 (result.W 5/6/7, itemId.L).
    /// One ListWindow serves every mode; titles and prices are typed into its input box with the VR keyboard.
    /// </summary>
    public sealed class Market
    {
        public enum Mode { None, VendSetup, VendOpen, ShopBuy, BuySetup, BuyOpen, BuySell }
        public sealed class VendLine { public int CartIndex, Amount, ItemId, Type; public long Price; public bool Identified; public int Refine; public int[] Cards = new int[4]; public uint Location; }
        public sealed class BuyLine { public int ItemId, Amount, Type; public long Price; }

        public Mode State { get; private set; } = Mode.None;
        /// <summary>Boards in view: vendor aid → shop title, buying-store owner aid → title.</summary>
        public readonly Dictionary<uint, string> Shops = new Dictionary<uint, string>();
        public readonly Dictionary<uint, string> BuyingStores = new Dictionary<uint, string>();
        public bool Vending { get; private set; } public bool Buying { get; private set; }
        public readonly List<VendLine> MyShop = new List<VendLine>(); public string MyTitle = "";
        public readonly List<BuyLine> MyBuying = new List<BuyLine>(); public long MyLimit;
        public int VendSlots { get; private set; } public int BuySlots { get; private set; }
        // the shop / store we are looking at
        public uint OtherAid { get; private set; } public uint OtherId { get; private set; } public string OtherTitle { get; private set; } = "";
        public readonly List<VendLine> OtherShop = new List<VendLine>(); public readonly List<BuyLine> OtherWants = new List<BuyLine>(); public long OtherLimit;
        public event Action Changed;
        public long SoldZeny { get; private set; } public int SoldCount { get; private set; } public int BoughtCount { get; private set; } // running totals (own shop / store)
        public int LastBuyResult { get; private set; } = -1; public int LastSellResult { get; private set; } = -1;

        private readonly RoSession _s; private readonly Inventory _inv; private readonly Cart _cart; private readonly ListWindow _win; private readonly Func<Camera> _head;
        private readonly Action<string, string> _chat; private readonly Func<uint, Unit> _unit; private readonly Func<long> _zeny;
        private int _tab; private VendLine _editVend; private BuyLine _editBuy; private string _editWhat = ""; private bool _shown;

        public Market(RoSession s, Inventory inv, Cart cart, ListWindow win, Func<Camera> head, Func<uint, Unit> unit, Func<long> zeny, Action<string, string> chat)
        {
            _s = s; _inv = inv; _cart = cart; _win = win; _head = head; _unit = unit; _zeny = zeny; _chat = chat;
            _win.OnCloseClicked = CloseWindow;
            _inv.Changed += () => { if (_shown && (State == Mode.BuySetup || State == Mode.BuySell)) Refresh(); };
            _cart.Changed += () => { if (_shown && State == Mode.VendSetup) Refresh(); };
        }

        public bool HasShop(uint aid) => Shops.ContainsKey(aid);
        public bool HasBuyingStore(uint aid) => BuyingStores.ContainsKey(aid);

        /// <summary>A map change closes every window; boards are re-sent for whoever comes into view.</summary>
        public void Reset()
        {
            Shops.Clear(); BuyingStores.Clear(); OtherShop.Clear(); OtherWants.Clear();
            if (State == Mode.VendSetup || State == Mode.ShopBuy || State == Mode.BuySetup || State == Mode.BuySell) State = Vending ? Mode.VendOpen : Buying ? Mode.BuyOpen : Mode.None;
            _shown = false; _win.Hide(); Changed?.Invoke();
        }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                // ---- vending, vendor side
                case 0x012d:
                {
                    r.Skip(2); VendSlots = r.U16();
                    State = Mode.VendSetup; MyShop.Clear(); _tab = 0; _editVend = null; _editWhat = "title";
                    Debug.Log($"[Market] vending setup: {VendSlots} slots");
                    _chat(Lang.F("Set up your shop: pick items from the cart, give them prices, then open it ({0} slots).", VendSlots), "9ad1ff");
                    Refresh(); Open();
                    return true;
                }
                case 0x0b40:
                {
                    r.Skip(8); MyShop.Clear();
                    while (r.Remaining >= 58)
                    {
                        var l = new VendLine { Price = r.S32(), CartIndex = r.U16(), Amount = r.U16(), Type = r.U8(), ItemId = (int)r.U32(), Identified = r.U8() != 0 }; r.Skip(1);
                        l.Cards = Inventory.ReadCards(ref r); r.Skip(25); l.Refine = r.U8(); r.Skip(1);
                        var info = GameDb.GetItem(l.ItemId); l.Location = info.Type == 4 || info.Type == 5 ? 1u : 0u;
                        MyShop.Add(l);
                    }
                    Vending = true; State = Mode.VendOpen;
                    Debug.Log($"[Market] my shop is open with {MyShop.Count} lines");
                    Refresh(); Changed?.Invoke();
                    return true;
                }
                case 0x0a28:
                {
                    r.Skip(2); int res = r.U8();
                    if (res == 0) { _chat(Lang.F("Your shop '{0}' is open.", MyTitle), "a8e6a3"); Open(); }
                    else { _chat(Lang.T("The shop couldn't be opened (an item can't be sold, or the cart / skill is missing)."), "ffb070"); if (State == Mode.VendSetup) { State = Mode.None; CloseWindow(); } }
                    Debug.Log($"[Market] open store ack {res}");
                    Changed?.Invoke();
                    return true;
                }
                case 0x09e5:
                {
                    r.Skip(2); int idx = r.U16(); int amount = r.U16(); uint buyerCid = r.U32(); uint date = r.U32(); int zeny = r.S32();
                    var line = MyShop.FirstOrDefault(x => x.CartIndex == idx);
                    string name = line != null ? GameDb.GetItem(line.ItemId).Display : $"item #{idx}";
                    if (line != null) { line.Amount -= amount; if (line.Amount <= 0) MyShop.Remove(line); }
                    SoldZeny += zeny; SoldCount += amount;
                    _chat(Lang.F("Sold {0} × {1} for {2:N0} z.", amount, name, zeny), "a8e6a3");
                    Debug.Log($"[Market] sold {amount} x {name} (cart {idx}) for {zeny} to cid {buyerCid}");
                    if (_shown && State == Mode.VendOpen) Refresh(); Changed?.Invoke();
                    return true;
                }
                // ---- boards
                case 0x0131:
                {
                    r.Skip(2); uint aid = r.U32(); string title = r.Str(80);
                    Shops[aid] = title; Board(aid, Lang.F("[Shop] {0}", title));
                    Debug.Log($"[Market] shop board {aid}: '{title}'");
                    Changed?.Invoke();
                    return true;
                }
                case 0x0132: { r.Skip(2); uint aid = r.U32(); Shops.Remove(aid); Board(aid, BuyingStores.TryGetValue(aid, out var t) ? Lang.F("[Buying] {0}", t) : null); Changed?.Invoke(); return true; }
                case 0x0814:
                {
                    r.Skip(2); uint aid = r.U32(); string title = r.Str(80);
                    BuyingStores[aid] = title; Board(aid, Lang.F("[Buying] {0}", title));
                    Debug.Log($"[Market] buying-store board {aid}: '{title}'");
                    Changed?.Invoke();
                    return true;
                }
                case 0x0816: { r.Skip(2); uint aid = r.U32(); BuyingStores.Remove(aid); Board(aid, Shops.TryGetValue(aid, out var t) ? Lang.F("[Shop] {0}", t) : null); Changed?.Invoke(); return true; }
                // ---- vending, buyer side
                case 0x0b3d:
                {
                    r.Skip(4); OtherAid = r.U32(); OtherId = r.U32(); OtherShop.Clear();
                    while (r.Remaining >= 64)
                    {
                        var l = new VendLine { Price = r.S32(), Amount = r.U16(), CartIndex = r.U16(), Type = r.U8(), ItemId = (int)r.U32(), Identified = r.U8() != 0 }; r.Skip(1);
                        l.Cards = Inventory.ReadCards(ref r); r.Skip(25); l.Location = r.U32(); r.Skip(2); l.Refine = r.U8(); r.Skip(1);
                        OtherShop.Add(l);
                    }
                    OtherTitle = Shops.TryGetValue(OtherAid, out var t2) ? t2 : "";
                    State = Mode.ShopBuy; _tab = 0;
                    Debug.Log($"[Market] shop {OtherAid} ({OtherId}): {OtherShop.Count} lines");
                    Refresh(); Open(); Changed?.Invoke();
                    return true;
                }
                case 0x0135:
                {
                    r.Skip(2); int idx = r.U16(); int amount = r.U16(); int res = r.U8();
                    LastBuyResult = res;
                    _chat(res == 1 ? Lang.T("You don't have enough zeny.") : res == 2 ? Lang.T("You can't carry that much.") : res == 4 ? Lang.F("The shop only has {0} of those left.", amount) : res == 6 ? Lang.T("The shop changed — open it again.") : Lang.F("Couldn't buy that ({0}).", res), "ffb070");
                    Debug.Log($"[Market] buy failed: index {idx} amount {amount} result {res}");
                    if (res == 6) { State = Mode.None; CloseWindow(); }
                    return true;
                }
                // ---- buying store, owner side
                case 0x0810:
                {
                    r.Skip(2); BuySlots = r.U8();
                    State = Mode.BuySetup; MyBuying.Clear(); MyLimit = Math.Min(_zeny(), 1000000); _tab = 0; _editBuy = null; _editWhat = "title";
                    Debug.Log($"[Market] buying-store setup: {BuySlots} slots");
                    _chat(Lang.F("Set up your buying store: choose what you want to buy, the price per piece and a zeny limit ({0} kinds).", BuySlots), "9ad1ff");
                    Refresh(); Open();
                    return true;
                }
                case 0x0812:
                {
                    r.Skip(2); int res = r.U16(); int weight = r.S32();
                    _chat(res == 2 ? Lang.T("You'd be overweight with all of that — lower the amounts.") : Lang.T("The buying store couldn't be opened (check the items, prices and your zeny) — use the skill again."), "ffb070");
                    Debug.Log($"[Market] buying-store open failed {res} weight {weight}");
                    if (State == Mode.BuySetup) { State = Mode.None; CloseWindow(); }
                    Changed?.Invoke();
                    return true;
                }
                case 0x0813:
                {
                    r.Skip(8); MyLimit = r.S32(); MyBuying.Clear();
                    while (r.Remaining >= 11) MyBuying.Add(new BuyLine { Price = r.S32(), Amount = r.U16(), Type = r.U8(), ItemId = (int)r.U32() });
                    Buying = true; State = Mode.BuyOpen;
                    _chat(Lang.F("Your buying store '{0}' is open.", MyTitle), "a8e6a3");
                    Debug.Log($"[Market] my buying store is open: {MyBuying.Count} lines, limit {MyLimit}");
                    Refresh(); Open(); Changed?.Invoke();
                    return true;
                }
                case 0x09e6:
                {
                    r.Skip(2); int itemId = (int)r.U32(); int amount = r.U16(); int zeny = r.S32(); int limit = r.S32(); uint sellerCid = r.U32();
                    var line = MyBuying.FirstOrDefault(x => x.ItemId == itemId);
                    if (line != null) { line.Amount -= amount; if (line.Amount <= 0) MyBuying.Remove(line); }
                    MyLimit = limit; BoughtCount += amount;
                    _chat(Lang.F("Bought {0} × {1} for {2:N0} z.", amount, GameDb.GetItem(itemId).Display, zeny), "a8e6a3");
                    Debug.Log($"[Market] bought {amount} x {itemId} for {zeny} from cid {sellerCid}, limit now {limit}");
                    if (_shown && State == Mode.BuyOpen) Refresh(); Changed?.Invoke();
                    return true;
                }
                case 0x081a:
                {
                    r.Skip(2); int res = r.U16();
                    _chat(res == 4 ? Lang.T("Your buying store bought everything it wanted and closed.") : Lang.T("Your buying store reached its zeny limit and closed."), "9ad1ff");
                    Buying = false; MyBuying.Clear(); if (State == Mode.BuyOpen) { State = Mode.None; CloseWindow(); }
                    Debug.Log($"[Market] buying store closed by the server ({res})");
                    Changed?.Invoke();
                    return true;
                }
                // ---- buying store, seller side
                case 0x0818:
                {
                    r.Skip(4); OtherAid = r.U32(); OtherId = r.U32(); OtherLimit = r.S32(); OtherWants.Clear();
                    while (r.Remaining >= 11) OtherWants.Add(new BuyLine { Price = r.S32(), Amount = r.U16(), Type = r.U8(), ItemId = (int)r.U32() });
                    OtherTitle = BuyingStores.TryGetValue(OtherAid, out var t3) ? t3 : "";
                    State = Mode.BuySell; _tab = 0;
                    Debug.Log($"[Market] buying store {OtherAid} ({OtherId}): {OtherWants.Count} wanted, limit {OtherLimit}");
                    Refresh(); Open(); Changed?.Invoke();
                    return true;
                }
                case 0x081c:
                {
                    r.Skip(2); int idx = r.U16(); int amount = r.U16(); int price = r.S32();
                    string name = _inv.Items.TryGetValue(idx, out var e) ? GameDb.GetItem(e.ItemId).Display : $"item #{idx}";
                    _inv.TakeOut(idx, amount); // this packet replaces the usual delete (pc_delitem type 1)
                    LastSellResult = 0; SoldZeny += (long)price * amount; SoldCount += amount;
                    _chat(Lang.F("Sold {0} × {1} for {2:N0} z.", amount, name, (long)price * amount), "a8e6a3");
                    Debug.Log($"[Market] sold to buying store: {amount} x {name} (inv {idx}) at {price}");
                    var want = OtherWants.FirstOrDefault(x => e != null && x.ItemId == e.ItemId); if (want != null) { want.Amount -= amount; OtherLimit -= (long)price * amount; }
                    if (_shown && State == Mode.BuySell) Refresh(); Changed?.Invoke();
                    return true;
                }
                case 0x0824:
                {
                    r.Skip(2); int res = r.U16(); int itemId = (int)r.U32();
                    LastSellResult = res;
                    _chat(res == 6 ? Lang.T("The store doesn't want that many.") : res == 7 ? Lang.T("The store owner is out of zeny.") : Lang.T("The deal failed (the item may be carded, rental, bound or not wanted)."), "ffb070");
                    Debug.Log($"[Market] sell to buying store failed {res} item {itemId}");
                    return true;
                }
                default: return false;
            }
        }

        private void Board(uint aid, string text) { var u = _unit(aid); if (u != null && u.Overhead != null) u.Overhead.SetBoard(text); }
        /// <summary>A unit came into view after its board packet (the board is sent first): re-apply.</summary>
        public void OnUnitAdded(Unit u)
        {
            if (u == null || u.Overhead == null) return;
            if (Shops.TryGetValue(u.Id, out var t)) u.Overhead.SetBoard(Lang.F("[Shop] {0}", t));
            else if (BuyingStores.TryGetValue(u.Id, out var t2)) u.Overhead.SetBoard(Lang.F("[Buying] {0}", t2));
        }

        // ---- vendor
        /// <summary>Casts MC_VENDING; the server answers 0x012d and the setup window opens.</summary>
        public void BeginVending(int skillLevel, uint selfAid)
        {
            if (Vending) { _chat(Lang.T("Your shop is already open."), "ffb070"); Open(); return; }
            if (!_cart.HasCart) { _chat(Lang.T("You need a cart to open a shop."), "ffb070"); return; }
            _s.UseSkill(41, Math.Max(1, skillLevel), selfAid);
        }
        public void VendAdd(int cartIndex)
        {
            if (State != Mode.VendSetup || !_cart.Items.TryGetValue(cartIndex, out var e)) return;
            if (MyShop.Any(x => x.CartIndex == cartIndex)) return;
            if (MyShop.Count >= VendSlots) { _chat(Lang.F("Only {0} kinds of item fit in this shop.", VendSlots), "ffb070"); return; }
            MyShop.Add(new VendLine { CartIndex = cartIndex, Amount = e.Amount, ItemId = e.ItemId, Type = e.Type, Price = Math.Max(1, DefaultPrice(e.ItemId)), Identified = e.Identified, Refine = e.Refine, Cards = e.Cards, Location = e.Location });
            _tab = 1; Refresh();
        }
        private static long DefaultPrice(int itemId) { var i = GameDb.GetItem(itemId); return i.Buy > 0 ? i.Buy : 1; }
        public void VendRemove(VendLine l) { MyShop.Remove(l); Refresh(); }
        public void VendOpen()
        {
            if (State != Mode.VendSetup) return;
            if (string.IsNullOrWhiteSpace(MyTitle)) { _chat(Lang.T("Give the shop a title first."), "ffb070"); _editWhat = "title"; Refresh(); return; }
            if (MyShop.Count == 0) { _chat(Lang.T("Add at least one item."), "ffb070"); return; }
            var w = new PacketWriter(0x01b2).U16(0).Str(MyTitle, 80).U8(1);
            foreach (var l in MyShop) w.U16((ushort)l.CartIndex).U16((ushort)Math.Min(l.Amount, 30000)).S32((int)Math.Min(l.Price, int.MaxValue));
            _s.SendPacket(w.ToArrayWithLength());
            Debug.Log($"[Market] open shop '{MyTitle}' with {MyShop.Count} lines");
        }
        public void VendCancel() { if (State == Mode.VendSetup) { _s.SendPacket(new PacketWriter(0x01b2).U16(0).Str("", 80).U8(0).ToArrayWithLength()); State = Mode.None; CloseWindow(); } }
        public void CloseShop() { if (Vending) { _s.SendPacket(new PacketWriter(0x012e).ToArray()); Vending = false; MyShop.Clear(); State = Mode.None; CloseWindow(); _chat(Lang.T("Your shop is closed."), "9ad1ff"); Changed?.Invoke(); } }

        // ---- buyer
        public void OpenShop(uint vendorAid) { _s.SendPacket(new PacketWriter(0x0130).U32(vendorAid).ToArray()); }
        public void Buy(VendLine l, int count)
        {
            if (State != Mode.ShopBuy || l == null || count <= 0) return;
            count = Math.Min(count, l.Amount);
            _s.SendPacket(new PacketWriter(0x0801).U16(0).U32(OtherAid).U32(OtherId).U16((ushort)count).U16((ushort)l.CartIndex).ToArrayWithLength());
            LastBuyResult = 0;
            l.Amount -= count; if (l.Amount <= 0) OtherShop.Remove(l); // the server sends no success packet beyond the pickup: assume it, 0x0135 corrects
            Debug.Log($"[Market] buy {count} x {l.ItemId} from {OtherAid}");
            State = Mode.None; CloseWindow(); // the server forgets the buy window after one purchase (vended_id = 0)
        }

        // ---- buying store owner
        public void BeginBuying(uint selfAid)
        {
            if (Buying) { _chat(Lang.T("Your buying store is already open."), "ffb070"); Open(); return; }
            _s.UseSkill(2535, 1, selfAid);
        }
        public void BuyWant(int itemId)
        {
            if (State != Mode.BuySetup) return;
            if (MyBuying.Any(x => x.ItemId == itemId)) return;
            if (MyBuying.Count >= BuySlots) { _chat(Lang.F("Only {0} kinds of item fit in a buying store.", BuySlots), "ffb070"); return; }
            MyBuying.Add(new BuyLine { ItemId = itemId, Amount = 1, Price = Math.Max(1, DefaultPrice(itemId)), Type = GameDb.GetItem(itemId).Type });
            _tab = 1; Refresh();
        }
        public void BuyRemove(BuyLine l) { MyBuying.Remove(l); Refresh(); }
        public void BuyOpenStore()
        {
            if (State != Mode.BuySetup) return;
            if (string.IsNullOrWhiteSpace(MyTitle)) { _chat(Lang.T("Give the store a title first."), "ffb070"); _editWhat = "title"; Refresh(); return; }
            if (MyBuying.Count == 0) { _chat(Lang.T("Add at least one item you want."), "ffb070"); return; }
            if (MyLimit <= 0 || MyLimit > _zeny()) { _chat(Lang.T("The zeny limit must be between 1 and what you carry."), "ffb070"); return; }
            var w = new PacketWriter(0x0811).U16(0).S32((int)Math.Min(MyLimit, int.MaxValue)).U8(1).Str(MyTitle, 80);
            foreach (var l in MyBuying) w.U32((uint)l.ItemId).U16((ushort)l.Amount).S32((int)Math.Min(l.Price, int.MaxValue));
            _s.SendPacket(w.ToArrayWithLength());
            Debug.Log($"[Market] open buying store '{MyTitle}' with {MyBuying.Count} lines, limit {MyLimit}");
        }
        public void BuyCancel() { if (State == Mode.BuySetup) { _s.SendPacket(new PacketWriter(0x0811).U16(0).S32(0).U8(0).Str("", 80).ToArrayWithLength()); State = Mode.None; CloseWindow(); } }
        public void CloseBuyingStore() { if (Buying) { _s.SendPacket(new PacketWriter(0x0815).ToArray()); Buying = false; MyBuying.Clear(); State = Mode.None; CloseWindow(); _chat(Lang.T("Your buying store is closed."), "9ad1ff"); Changed?.Invoke(); } }

        // ---- seller into a buying store
        public void OpenBuyingStore(uint buyerAid) { _s.SendPacket(new PacketWriter(0x0817).U32(buyerAid).ToArray()); }
        public void Sell(BuyLine want, int amount)
        {
            if (State != Mode.BuySell || want == null || amount <= 0) return;
            var e = _inv.Items.Values.FirstOrDefault(x => x.ItemId == want.ItemId && x.Wearing == 0 && x.Cards.All(c => c == 0));
            if (e == null) { _chat(Lang.T("You don't have that item (or only carded ones, which can't be sold here)."), "ffb070"); return; }
            amount = Math.Min(amount, Math.Min(e.Amount, want.Amount));
            _s.SendPacket(new PacketWriter(0x0819).U16(0).U32(OtherAid).U32(OtherId).U16((ushort)e.Index).U32((uint)want.ItemId).U16((ushort)amount).ToArrayWithLength());
            Debug.Log($"[Market] sell {amount} x {want.ItemId} (inv {e.Index}) to {OtherAid}");
        }

        // ---- window
        private void Open() { _shown = true; _win.Open(_head()); }
        public void CloseWindow()
        {
            _shown = false; _win.SetInput(false); _win.Hide();
            if (State == Mode.VendSetup) { VendCancel(); return; }
            if (State == Mode.BuySetup) { BuyCancel(); return; }
            if (State == Mode.ShopBuy || State == Mode.BuySell) State = Vending ? Mode.VendOpen : Buying ? Mode.BuyOpen : Mode.None;
        }
        /// <summary>Re-opens the own-shop / own-store window from the wrist menu.</summary>
        public void ShowMine() { if (Vending) { State = Mode.VendOpen; Refresh(); Open(); } else if (Buying) { State = Mode.BuyOpen; Refresh(); Open(); } }
        public string Editing => _editWhat;

        private static bool Digit(char c) => c >= '0' && c <= '9';
        private static string Money(long z) => z.ToString("N0") + " z";

        private void Refresh()
        {
            var rows = new List<ListWindow.Row>();
            switch (State)
            {
                case Mode.VendSetup:
                {
                    _win.SetTitle(Lang.F("Set up your shop  <color=#9aa3b5>{0}/{1} kinds</color>", MyShop.Count, VendSlots));
                    if (_editWhat == "price" && _editVend != null)
                    {
                        var ev = _editVend;
                        _win.SetInfo(Lang.F("Price for {0}:", GameDb.GetItem(ev.ItemId).Display));
                        _win.SetInput(true, Lang.T("price per piece"), Digit, () => { if (long.TryParse(_win.Input.Text, out var p) && p > 0) ev.Price = Math.Min(p, 1000000000); _editWhat = "title"; _editVend = null; Refresh(); }, ev.Price.ToString(), 10);
                    }
                    else { _win.SetInfo(Lang.T("Shop title:")); _win.SetInput(true, Lang.T("shop title"), null, () => { MyTitle = _win.Input.Text.Trim(); Refresh(); }, MyTitle, 79); }
                    _win.SetTabs(new[] { Lang.T("Cart"), Lang.T("For sale") }, _tab, t => { _tab = t; Refresh(); });
                    if (_tab == 0)
                    {
                        foreach (var e in _cart.Items.Values.OrderBy(x => x.Index))
                        {
                            var e2 = e; bool listed = MyShop.Any(x => x.CartIndex == e.Index);
                            rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(e.Info), Text = $"{Inventory.FullName(e)}  <color=#ffd060>x{e.Amount}</color>{(listed ? "  " + Lang.T("<color=#7fd67f>listed</color>") : "")}", A = listed ? Lang.T("Listed") : Lang.T("Sell"), OnA = () => VendAdd(e2.Index), AEnabled = !listed });
                        }
                        if (rows.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>The cart is empty — put the goods in the cart first.</color>") });
                    }
                    else
                    {
                        foreach (var l in MyShop)
                        {
                            var l2 = l;
                            rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(GameDb.GetItem(l.ItemId)), Text = $"{GameDb.GetItem(l.ItemId).Display}  <color=#ffd060>x{l.Amount}</color>  @ <color=#ffd060>{Money(l.Price)}</color>", A = Lang.T("Price"), OnA = () => { _editVend = l2; _editWhat = "price"; Refresh(); _win.Input.Focus(); }, B = Lang.T("Remove"), OnB = () => VendRemove(l2) });
                        }
                        if (rows.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>Nothing for sale yet — pick items on the Cart tab.</color>") });
                    }
                    _win.SetFooter((Lang.T("Open shop"), VendOpen), (Lang.T("Cancel"), VendCancel));
                    break;
                }
                case Mode.VendOpen:
                {
                    _win.SetTitle(Lang.F("Your shop  <color=#ffd060>{0}</color>", MyTitle));
                    _win.SetInfo(Lang.F("{0} kinds on sale · sold {1} for {2:N0} z this session", MyShop.Count, SoldCount, SoldZeny)); _win.SetInput(false);
                    _win.SetTabs(null, 0, null);
                    foreach (var l in MyShop) rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(GameDb.GetItem(l.ItemId)), Text = $"{GameDb.GetItem(l.ItemId).Display}  <color=#ffd060>x{l.Amount}</color>  @ <color=#ffd060>{Money(l.Price)}</color>" });
                    _win.SetFooter((Lang.T("Close shop"), CloseShop), (Lang.T("Hide"), () => { _shown = false; _win.Hide(); }));
                    break;
                }
                case Mode.ShopBuy:
                {
                    _win.SetTitle(Lang.F("Shop  <color=#ffd060>{0}</color>", OtherTitle));
                    _win.SetInfo(Lang.F("You have {0:N0} z.", _zeny())); _win.SetInput(false);
                    _win.SetTabs(null, 0, null);
                    foreach (var l in OtherShop)
                    {
                        var l2 = l; bool can = _zeny() >= l.Price;
                        rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(GameDb.GetItem(l.ItemId)), Text = $"{(l.Refine > 0 ? "+" + l.Refine + " " : "")}{GameDb.GetItem(l.ItemId).Display}  <color=#ffd060>x{l.Amount}</color>  @ <color=#ffd060>{Money(l.Price)}</color>", A = Lang.T("Buy 1"), OnA = () => Buy(l2, 1), AEnabled = can, B = l.Amount > 1 ? Lang.T("Buy 10") : null, OnB = () => Buy(l2, 10), BEnabled = _zeny() >= l.Price * Math.Min(10, l.Amount) });
                    }
                    _win.SetFooter((Lang.T("Close"), CloseWindow));
                    break;
                }
                case Mode.BuySetup:
                {
                    _win.SetTitle(Lang.F("Set up your buying store  <color=#9aa3b5>{0}/{1} kinds</color>", MyBuying.Count, BuySlots));
                    if (_editWhat == "price" && _editBuy != null) { var eb = _editBuy; _win.SetInfo(Lang.F("Price per {0}:", GameDb.GetItem(eb.ItemId).Display)); _win.SetInput(true, Lang.T("price per piece"), Digit, () => { if (long.TryParse(_win.Input.Text, out var p) && p > 0) eb.Price = Math.Min(p, 99990000); _editWhat = "title"; _editBuy = null; Refresh(); }, eb.Price.ToString(), 9); }
                    else if (_editWhat == "amount" && _editBuy != null) { var eb = _editBuy; _win.SetInfo(Lang.F("How many {0}:", GameDb.GetItem(eb.ItemId).Display)); _win.SetInput(true, Lang.T("amount"), Digit, () => { if (int.TryParse(_win.Input.Text, out var a) && a > 0) eb.Amount = Math.Min(a, 9999); _editWhat = "title"; _editBuy = null; Refresh(); }, eb.Amount.ToString(), 4); }
                    else if (_editWhat == "limit") { _win.SetInfo(Lang.F("Zeny limit (you have {0:N0} z):", _zeny())); _win.SetInput(true, Lang.T("zeny limit"), Digit, () => { if (long.TryParse(_win.Input.Text, out var z) && z > 0) MyLimit = Math.Min(z, _zeny()); _editWhat = "title"; Refresh(); }, MyLimit.ToString(), 10); }
                    else { _win.SetInfo(Lang.F("Title:   <color=#9aa3b5>limit {0:N0} z</color>", MyLimit)); _win.SetInput(true, Lang.T("store title"), null, () => { MyTitle = _win.Input.Text.Trim(); Refresh(); }, MyTitle, 79); }
                    _win.SetTabs(new[] { Lang.T("Inventory"), Lang.T("Wanted") }, _tab, t => { _tab = t; Refresh(); });
                    if (_tab == 0)
                    {
                        var seen = new HashSet<int>();
                        foreach (var e in _inv.Items.Values.OrderBy(x => x.Index))
                        {
                            if (!seen.Add(e.ItemId) || !e.Info.BuyingStore) continue; // item_db Flags.BuyingStore: the server refuses the rest
                            int id = e.ItemId; bool listed = MyBuying.Any(x => x.ItemId == id);
                            rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(e.Info), Text = $"{e.Info.Display}  {Lang.F("<color=#9aa3b5>you have {0}</color>", e.Amount)}{(listed ? "  " + Lang.T("<color=#7fd67f>wanted</color>") : "")}", A = listed ? Lang.T("~item~Wanted") : Lang.T("~buy~Want"), OnA = () => BuyWant(id), AEnabled = !listed });
                        }
                        rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>A buying store can only ask for items you already own at least one of, and only the kinds the server allows.</color>") });
                    }
                    else
                    {
                        foreach (var l in MyBuying)
                        {
                            var l2 = l;
                            rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(GameDb.GetItem(l.ItemId)), Text = $"{GameDb.GetItem(l.ItemId).Display}  <color=#ffd060>x{l.Amount}</color>  @ <color=#ffd060>{Money(l.Price)}</color>", A = Lang.T("Price"), OnA = () => { _editBuy = l2; _editWhat = "price"; Refresh(); _win.Input.Focus(); }, B = Lang.T("Amount"), OnB = () => { _editBuy = l2; _editWhat = "amount"; Refresh(); _win.Input.Focus(); } });
                        }
                        if (rows.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>Nothing wanted yet — pick items on the Inventory tab.</color>") });
                        else rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>Remove a line:</color>"), A = MyBuying.Count > 0 ? Lang.T("Remove last") : null, OnA = () => { if (MyBuying.Count > 0) BuyRemove(MyBuying[MyBuying.Count - 1]); } });
                    }
                    _win.SetFooter((Lang.T("Open"), BuyOpenStore), (Lang.T("Limit"), () => { _editWhat = "limit"; Refresh(); _win.Input.Focus(); }), (Lang.T("Cancel"), BuyCancel));
                    break;
                }
                case Mode.BuyOpen:
                {
                    _win.SetTitle(Lang.F("Your buying store  <color=#ffd060>{0}</color>", MyTitle));
                    _win.SetInfo(Lang.F("{0:N0} z left to spend · bought {1} this session", MyLimit, BoughtCount)); _win.SetInput(false);
                    _win.SetTabs(null, 0, null);
                    foreach (var l in MyBuying) rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(GameDb.GetItem(l.ItemId)), Text = Lang.F("{0}  <color=#ffd060>x{1} wanted</color>  @ <color=#ffd060>{2:N0} z</color>", GameDb.GetItem(l.ItemId).Display, l.Amount, l.Price) });
                    _win.SetFooter((Lang.T("Close store"), CloseBuyingStore), (Lang.T("Hide"), () => { _shown = false; _win.Hide(); }));
                    break;
                }
                case Mode.BuySell:
                {
                    _win.SetTitle(Lang.F("Buying store  <color=#ffd060>{0}</color>", OtherTitle));
                    _win.SetInfo(Lang.F("The owner can still spend {0:N0} z.", OtherLimit)); _win.SetInput(false);
                    _win.SetTabs(null, 0, null);
                    foreach (var w in OtherWants)
                    {
                        var w2 = w; int have = _inv.Items.Values.Where(x => x.ItemId == w.ItemId && x.Wearing == 0 && x.Cards.All(c => c == 0)).Sum(x => x.Amount);
                        int max = Math.Min(have, w.Amount);
                        rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(GameDb.GetItem(w.ItemId)), Text = Lang.F("{0}  <color=#ffd060>wants {1}</color> @ <color=#ffd060>{2:N0} z</color>  <color=#9aa3b5>you have {3}</color>", GameDb.GetItem(w.ItemId).Display, w.Amount, w.Price, have), A = Lang.T("Sell 1"), OnA = () => Sell(w2, 1), AEnabled = max >= 1, B = max > 1 ? Lang.F("Sell {0}", max) : null, OnB = () => Sell(w2, max), BEnabled = max > 1 });
                    }
                    _win.SetFooter((Lang.T("Close"), CloseWindow));
                    break;
                }
                default: return;
            }
            _win.SetRows(rows);
        }
    }
}
