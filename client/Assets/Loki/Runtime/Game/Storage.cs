using System;
using System.Collections.Generic;
using System.Linq;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Kafra storage (research/shops_storage_packets.md §2; rAthena e985006, PACKETVER 20211103):
    ///   openstorage → 0x0b08 / 0x0b09 / 0x0b39 / 0x0b0b with invType 2 (same ids as the inventory list; storage client
    ///   index = server index + 1) → 0x00f2 count.W max.W.
    ///   store 0x0364 (inventory index.W amount.L) → 0x0b44 (index, amount added, item…) + inventory 0x07fa; failure: 0x00f6/0x00af with amount 0.
    ///   take  0x0365 (storage index.W amount.L) → inventory 0x0b41 + 0x00f6 (index, amount removed); failure: 0x00f6 amount 0.
    ///   close 0x0193 → 0x00f8. Storage open = storage_flag, which blocks walking (pc_cant_act) until 0x0193 or a warp.
    /// Client wave 4 (w4items): with a cart on, a "Cart" tab moves cart stacks into the storage (CZ_MOVE_ITEM_FROM_CART_TO_STORE
    ///   0x0129, see Game/Cart.cs); the cart window gets a "Storage" tab for the other way (0x0128) while the storage is open.
    /// </summary>
    public sealed class Storage
    {
        public readonly SortedDictionary<int, Inventory.Entry> Items = new SortedDictionary<int, Inventory.Entry>();
        public int Count, Max;
        public bool IsOpen { get; private set; }
        /// <summary>The 0x0b08 window name; guild storage uses the same packets with invType 2 and the name "Guild Storage".</summary>
        public string WindowName { get; private set; } = "";
        public bool IsGuildStorage => WindowName == "Guild Storage";
        private bool _loading; private int _tab; // 0 = storage, 1 = inventory, 2 = cart (client wave 4, only with a cart on)
        /// <summary>Client wave 4: the cart (its tab here), told when the storage opens, closes or changes.</summary>
        public Cart Cart;
        private bool CartOn => Cart != null && Cart.HasCart;
        private readonly RoSession _s; private readonly Inventory _inv; private readonly ListWindow _win; private readonly Func<Camera> _head;
        private readonly Action _dismissDialog;

        public Storage(RoSession s, Inventory inv, ListWindow win, Func<Camera> head, Action dismissDialog)
        {
            _s = s; _inv = inv; _win = win; _head = head; _dismissDialog = dismissDialog;
            _win.OnCloseClicked = Close;
            _inv.Changed += () => { if (IsOpen) Refresh(); };
        }

        /// <summary>A warp force-closes storage on the server without 0x00f8 (unit.cpp:3646-3654).</summary>
        public void Reset() { IsOpen = false; _loading = false; Items.Clear(); _win.Hide(); }

        private static bool IsStorage(ushort id, byte[] b, int o, int n)
        {
            if (id == 0x0b08 || id == 0x0b09 || id == 0x0b39) return n > 4 && b[o + 4] == 2;
            if (id == 0x0b0b) return n > 2 && b[o + 2] == 2;
            return false;
        }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            if ((id == 0x0b08 || id == 0x0b09 || id == 0x0b39 || id == 0x0b0b) && !IsStorage(id, b, o, n)) return false;
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x0b08: { r.Skip(5); WindowName = n > 5 ? r.Str(n - 5) : ""; Items.Clear(); _loading = true; return true; } // name: "Storage" or "Guild Storage" (the only in-band marker, storage.cpp:609)
                case 0x0b09: { r.Skip(5); while (r.Remaining >= 34) { var e = Inventory.ReadNormal(ref r); Items[e.Index] = e; } return true; }
                case 0x0b39: { r.Skip(5); while (r.Remaining >= 68) { var e = Inventory.ReadEquip(ref r); Items[e.Index] = e; } return true; }
                case 0x0b0b:
                {
                    _loading = false; IsOpen = true; _tab = 0;
                    _dismissDialog();
                    Refresh(); _win.Open(_head()); Cart?.StorageChanged();
                    Debug.Log($"[Storage] opened: {Items.Count} stacks");
                    return true;
                }
                case 0x00f2: { r.Skip(2); Count = r.U16(); Max = r.U16(); if (IsOpen) Refresh(); Cart?.StorageChanged(); return true; }
                case 0x0b44: // ZC_ADD_ITEM_TO_STORE index.W amount.L itemId.L type.B identified.B damaged.B slot(16) options(25) refine.B grade.B
                {
                    r.Skip(2); int idx = r.S16(); int amount = r.S32(); int itemId = (int)r.U32(); int type = r.U8(); bool ident = r.U8() != 0; bool damaged = r.U8() != 0;
                    var cards = Inventory.ReadCards(ref r); var opts = Inventory.ReadOptions(ref r); int refine = r.U8(); int grade = r.U8(); // options: client wave 3 (w3equip); grade: client wave 4 (packets_struct.hpp:2367-2384)
                    if (Items.TryGetValue(idx, out var e) && e.ItemId == itemId) e.Amount += amount;
                    else Items[idx] = new Inventory.Entry { Index = idx, ItemId = itemId, Amount = amount, Type = type, Identified = ident, Damaged = damaged, Refine = refine, Grade = grade, Cards = cards, Options = opts, Location = GameDb.GetItem(itemId).Type == 4 || GameDb.GetItem(itemId).Type == 5 ? 1u : 0u };
                    Debug.Log($"[Storage] stored {amount} x {itemId} in slot {idx}");
                    if (IsOpen) Refresh(); Cart?.StorageChanged();
                    return true;
                }
                case 0x00f6: // ZC_DELETE_ITEM_FROM_STORE index.W amount.L (amount 0 = a failed add/take: nothing changed)
                {
                    r.Skip(2); int idx = r.U16(); int amount = r.S32();
                    if (amount > 0 && Items.TryGetValue(idx, out var e)) { e.Amount -= amount; if (e.Amount <= 0) Items.Remove(idx); if (IsOpen) Refresh(); Cart?.StorageChanged(); }
                    return true;
                }
                case 0x00f8: { IsOpen = false; _win.Hide(); Debug.Log("[Storage] closed by server"); Cart?.StorageChanged(); return true; }
                default: return false;
            }
        }

        private void Close()
        {
            _s.StorageClose();
            IsOpen = false; _win.Hide();
            Debug.Log("[Storage] close sent"); Cart?.StorageChanged();
        }

        public void Store(int invIndex, int amount) { _s.StorageAdd(invIndex, amount); }
        public void Take(int storageIndex, int amount) { _s.StorageGet(storageIndex, amount); }

        private void Refresh()
        {
            var rows = new List<ListWindow.Row>();
            if (_tab == 0)
            {
                foreach (var e in Items.Values.OrderBy(x => x.Index))
                {
                    var info = e.Info; var e2 = e;
                    bool stack = e.Amount > 1;
                    rows.Add(new ListWindow.Row
                    {
                        Icon = WristMenu.ItemIcon(info),
                        Text = $"{Inventory.FullName(e)}  <color=#ffd060>x{e.Amount}</color>",
                        A = stack ? Lang.T("Take 1") : Lang.T("~takeout~Take"), OnA = () => Take(e2.Index, 1),
                        B = stack ? Lang.T("Take all") : null, OnB = () => Take(e2.Index, e2.Amount),
                    });
                }
            }
            else if (_tab == 2 && CartOn)
            {
                // client wave 4: cart → storage (CZ_MOVE_ITEM_FROM_CART_TO_STORE 0x0129, Game/Cart.cs)
                foreach (var e in Cart.Items.Values.OrderBy(x => x.Index))
                {
                    var e2 = e; bool stack = e.Amount > 1;
                    rows.Add(new ListWindow.Row
                    {
                        Icon = WristMenu.ItemIcon(e.Info), Text = $"{Inventory.FullName(e)}  <color=#ffd060>x{e.Amount}</color>",
                        A = stack ? Lang.T("Store 1") : Lang.T("Store"), OnA = () => Cart.ToStorage(e2.Index, 1),
                        B = stack ? Lang.T("Store all") : null, OnB = () => Cart.ToStorage(e2.Index, e2.Amount),
                    });
                }
                if (rows.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>The cart is empty.</color>") });
            }
            else
            {
                if (_tab == 2) _tab = 1;
                foreach (var e in _inv.Items.Values.OrderBy(x => x.Index))
                {
                    if (e.Wearing != 0) continue; // worn gear can't be stored
                    var info = e.Info; var e2 = e;
                    bool stack = e.Amount > 1;
                    rows.Add(new ListWindow.Row
                    {
                        Icon = WristMenu.ItemIcon(info),
                        Text = $"{Inventory.FullName(e)}  <color=#ffd060>x{e.Amount}</color>",
                        A = stack ? Lang.T("Store 1") : Lang.T("Store"), OnA = () => Store(e2.Index, 1),
                        B = stack ? Lang.T("Store all") : null, OnB = () => Store(e2.Index, e2.Amount),
                    });
                }
            }
            _win.SetTitle(IsGuildStorage ? Lang.T("Guild storage") : Lang.T("Storage"));
            _win.SetInfo((Max > 0 ? Lang.F("{0} / {1} slots used", Count, Max) : "") + "   " + (_tab == 0 ? Lang.T("Take items out.") : _tab == 2 ? Lang.T("Store items from the cart.") : Lang.T("Put items in.")));
            _win.SetTabs(CartOn ? new[] { Lang.T("Storage"), Lang.T("Inventory"), Lang.T("Cart") } : new[] { Lang.T("Storage"), Lang.T("Inventory") }, _tab, t => { _tab = t; Refresh(); });
            _win.SetFooter((Lang.T("Close"), Close));
            _win.SetRows(rows);
        }

        /// <summary>Client wave 4: the cart's contents changed (its tab here follows).</summary>
        public void CartChanged() { if (IsOpen && _tab == 2) Refresh(); }
    }
}
