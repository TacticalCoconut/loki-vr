using System;
using System.Collections.Generic;
using System.Linq;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// The push cart (research/trade_cart_vending_packets.md §2; rAthena e985006, PACKETVER 20211103, NEW_CARTS).
    ///   Having a cart = status EFST_ON_PUSH_CART 673 on ourselves (0x0983 start / 0x0984 on login, val1 = cart type; 0x0196 end) —
    ///   ZC_STATE_CHANGE never carries cart bits on this build. The cart comes from an NPC (Kafra rental), @cart, or MC_CHANGECART.
    ///   Contents: 0x0b08 / 0x0b09 / 0x0b39 / 0x0b0b with invType 1 (same ids as the inventory; cart client index = slot + 2), on
    ///   login, cart-on, and after a vending-triggered save (preceded by 0x012b). 0x0121 count.W max.W weight.L maxWeight.L.
    ///   inventory → cart 0x0126 (index+2, amount.L) → 0x0b45 (index, amount added, item…) + 0x0121 + inventory 0x07fa; failure 0x012c
    ///   (0 weight/other, 1 slots) then a 0x0b41/0x07fa resync pair. cart → inventory 0x0127 → inventory 0x0b41 + 0x0125 (index, amount) + 0x0121.
    ///   0x012a asks the cart off (0x0196 + 0x012b). The cart holds the vending stock.
    /// Client wave 4 (w4items):
    ///   Cart ↔ storage while the storage is open (clif.cpp:13673-13724; storage.cpp:394-452): cart → storage CZ_MOVE_ITEM_FROM_CART_TO_STORE
    ///   0x0129 cartIndex.W amount.L → storage 0x0b44 + 0x00f2, cart 0x0125 + 0x0121; storage → cart CZ_MOVE_ITEM_FROM_STORE_TO_CART 0x0128
    ///   storageIndex.W amount.L → cart 0x0b45 + 0x0121, storage 0x00f6 + 0x00f2 (a full cart: 0x012c). Both are refused silently while
    ///   vending, without a cart or on a nousecart map.
    ///   Change cart: using MC_CHANGECART 154 (the server confirms with ZC_USE_SKILL 0x09cb, changecart.cpp:11-13) opens the cart choice
    ///   (the official client's own window: roBrowserLegacy Engine/MapEngine/Skill.js:720-724, UI/Components/ChangeCart, a
    ///   reimplementation); CZ_REQ_CHANGECART 0x01af num.W — rAthena accepts type 1 always, 2 above base level 40, 3 above 65, 4 above 80,
    ///   5 above 90, 6 above 100, 7 above 110, 8 above 120, 9 above 130 and 13 above 100 (NEW_CARTS, PACKETVER ≥ 20191106:
    ///   clif.cpp:12681-12712) and answers with EFST_ON_PUSH_CART again (the new val1). Decorate cart (MC_CARTDECORATE 2544):
    ///   ZC_SELECTCART 0x097f len.W aid.L {type.B}* (types 10-12, decoratecart.cpp:12-18, clif.cpp:12638-12656) → CZ_SELECTCART 0x0980
    ///   aid.L type.B (clif.cpp:12660-12676).
    ///   Remove the cart: CZ_REQ_CARTOFF 0x012a (clif.cpp:12623-12636) — a rider / falcon / dragon / Madogear option is removed first,
    ///   the cart only by the next press. The cart's items stay saved with the character.
    /// </summary>
    public sealed class Cart
    {
        public const int EfstPushCart = 673;
        public const int SkillChangeCart = 154, SkillDecorateCart = 2544;
        public readonly SortedDictionary<int, Inventory.Entry> Items = new SortedDictionary<int, Inventory.Entry>();
        public int Count, Max = 100; public long Weight, MaxWeight;
        public bool HasCart { get; private set; } public int CartType { get; private set; }
        public bool IsOpen => _win.Visible && _shown;
        public event Action Changed;
        /// <summary>The window's "Open a shop" button — wired by the owner to the vending setup.</summary>
        public Action OpenShop;
        public Func<bool> CanVend;
        /// <summary>Client wave 4: the Kafra storage (cart ↔ storage while it is open), the base level (which cart styles the server
        /// accepts), the known skills (the Change cart button) and the skill cast (the button uses MC_CHANGECART like the skill window).</summary>
        public Storage Storage; public Func<int> BaseLevel; public Func<int, bool> KnowsSkill; public Action<int> CastSkill; public Func<uint> SelfAid;
        private bool _shown; private int _tab; // 0 cart, 1 inventory, 2 storage (only while the storage is open)
        private enum Pick { None, Change, Decorate }
        private Pick _pick; private readonly List<int> _decorTypes = new List<int>();
        private float _removeArmedUntil;
        private readonly RoSession _s; private readonly Inventory _inv; private readonly ListWindow _win; private readonly Func<Camera> _head; private readonly Action<string, string> _chat;

        public Cart(RoSession s, Inventory inv, Progression prog, ListWindow win, Func<Camera> head, Action<string, string> chat)
        {
            _s = s; _inv = inv; _win = win; _head = head; _chat = chat;
            _win.OnCloseClicked = Hide;
            _inv.Changed += () => { if (IsOpen) Refresh(); };
            prog.EffectChanged += (type, on, val1) =>
            {
                if (type != EfstPushCart) return;
                bool had = HasCart; int oldType = CartType; HasCart = on; CartType = on ? val1 : 0;
                if (on != had) { Debug.Log($"[Cart] {(on ? $"cart on (type {val1})" : "cart off")}"); if (!on) { Items.Clear(); Hide(); } }
                else if (on && val1 != oldType) { Debug.Log($"[Cart] cart type {oldType} → {val1}"); if (_pick != Pick.None) { _pick = Pick.None; } if (IsOpen) Refresh(); }
                Changed?.Invoke();
            };
        }

        private static bool IsCart(ushort id, byte[] b, int o, int n)
        {
            if (id == 0x0b08 || id == 0x0b09 || id == 0x0b39) return n > 4 && b[o + 4] == 1;
            if (id == 0x0b0b) return n > 2 && b[o + 2] == 1;
            return false;
        }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            if ((id == 0x0b08 || id == 0x0b09 || id == 0x0b39 || id == 0x0b0b) && !IsCart(id, b, o, n)) return false;
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x0b08: { Items.Clear(); return true; }
                case 0x0b09: { r.Skip(5); while (r.Remaining >= 34) { var e = Inventory.ReadNormal(ref r); Items[e.Index] = e; } return true; }
                case 0x0b39: { r.Skip(5); while (r.Remaining >= 68) { var e = Inventory.ReadEquip(ref r); Items[e.Index] = e; } return true; }
                case 0x0b0b: { Debug.Log($"[Cart] list: {Items.Count} stacks"); if (IsOpen) Refresh(); Changed?.Invoke(); return true; }
                case 0x0121:
                {
                    r.Skip(2); Count = r.U16(); Max = r.U16(); Weight = r.S32(); MaxWeight = r.S32();
                    if (IsOpen) Refresh(); Changed?.Invoke();
                    return true;
                }
                case 0x0b45: // ZC_ADD_ITEM_TO_CART index.W amount.L itemId.L type.B identified.B damaged.B cards(16) options(25) refine.B grade.B (packets_struct.hpp:2464-2481)
                {
                    r.Skip(2); int idx = r.U16(); int amount = r.S32(); int itemId = (int)r.U32(); int type = r.U8(); bool ident = r.U8() != 0; bool damaged = r.U8() != 0;
                    var cards = Inventory.ReadCards(ref r); var opts = Inventory.ReadOptions(ref r); int refine = r.U8(); int grade = r.U8(); // grade: client wave 4 (was skipped)
                    if (Items.TryGetValue(idx, out var e) && e.ItemId == itemId) e.Amount += amount;
                    else { var info = GameDb.GetItem(itemId); Items[idx] = new Inventory.Entry { Index = idx, ItemId = itemId, Amount = amount, Type = type, Identified = ident, Damaged = damaged, Refine = refine, Grade = grade, Cards = cards, Options = opts, Location = info.Type == 4 || info.Type == 5 ? 1u : 0u }; }
                    LastAdded = (idx, itemId, amount);
                    Debug.Log($"[Cart] +{amount} x {itemId} in slot {idx}{(opts.Any(x => x.Id > 0) ? $" ({opts.Count(x => x.Id > 0)} random options)" : "")}");
                    if (IsOpen) Refresh(); Changed?.Invoke();
                    return true;
                }
                case 0x0125:
                {
                    r.Skip(2); int idx = r.U16(); int amount = r.S32();
                    if (Items.TryGetValue(idx, out var e)) { e.Amount -= amount; if (e.Amount <= 0) Items.Remove(idx); }
                    Debug.Log($"[Cart] -{amount} from slot {idx}");
                    if (IsOpen) Refresh(); Changed?.Invoke();
                    return true;
                }
                case 0x012c: { r.Skip(2); int res = r.U8(); LastFail = res; _chat(res == 1 ? Lang.T("The cart has no room for that.") : Lang.T("The cart can't carry that (weight, or the item can't go in a cart)."), "ffb070"); return true; }
                case 0x012b: { Items.Clear(); Debug.Log("[Cart] cleared (0x012b)"); if (IsOpen) Refresh(); Changed?.Invoke(); return true; }
                case 0x097f: // ZC_SELECTCART len.W aid.L {type.B}*: the Decorate Cart styles (clif.cpp:12638-12656)
                {
                    r.Skip(4); r.U32(); _decorTypes.Clear(); while (r.Remaining >= 1) _decorTypes.Add(r.U8());
                    Debug.Log($"[Cart] decorate: styles [{string.Join(",", _decorTypes)}]");
                    _pick = Pick.Decorate; _shown = true; _tab = 0; Refresh(); _win.Open(_head());
                    return true;
                }
                default: return false;
            }
        }

        /// <summary>ZC_USE_SKILL 0x09cb of our own MC_CHANGECART: the server accepted the skill, the cart choice opens (see the class notes).</summary>
        public void OnOwnSkillUsed(int skillId)
        {
            if (skillId != SkillChangeCart || !HasCart) return;
            Debug.Log("[Cart] change cart: choose a style");
            _pick = Pick.Change; _shown = true; _tab = 0; Refresh(); _win.Open(_head());
        }

        public void Put(int invIndex, int amount) { _s.SendPacket(new PacketWriter(0x0126).U16((ushort)invIndex).S32(amount).ToArray()); } // both models are keyed by the wire index (slot + 2)
        public void Take(int cartIndex, int amount) { _s.SendPacket(new PacketWriter(0x0127).U16((ushort)cartIndex).S32(amount).ToArray()); }
        /// <summary>CZ_MOVE_ITEM_FROM_CART_TO_STORE 0x0129: a cart stack into the open storage (the cart's own wire index).</summary>
        public void ToStorage(int cartIndex, int amount) { Debug.Log($"[Cart] to storage: slot {cartIndex} x{amount}"); _s.SendPacket(new PacketWriter(0x0129).U16((ushort)cartIndex).S32(amount).ToArray()); }
        /// <summary>CZ_MOVE_ITEM_FROM_STORE_TO_CART 0x0128: a storage stack into the cart (the storage's own wire index).</summary>
        public void FromStorage(int storageIndex, int amount) { Debug.Log($"[Cart] from storage: slot {storageIndex} x{amount}"); _s.SendPacket(new PacketWriter(0x0128).U16((ushort)storageIndex).S32(amount).ToArray()); }
        public void RemoveCart() { Debug.Log("[Cart] remove the cart (0x012a)"); _s.SendPacket(new PacketWriter(0x012a).ToArray()); }
        /// <summary>CZ_REQ_CHANGECART 0x01af num.W.</summary>
        public void ChangeTo(int type) { Debug.Log($"[Cart] change cart → {type} (0x01af)"); _s.SendPacket(new PacketWriter(0x01af).U16((ushort)type).ToArray()); _pick = Pick.None; if (IsOpen) Refresh(); }
        /// <summary>CZ_SELECTCART 0x0980 aid.L type.B.</summary>
        public void Decorate(int type) { Debug.Log($"[Cart] decorate cart → {type} (0x0980)"); _s.SendPacket(new PacketWriter(0x0980).U32(SelfAid != null ? SelfAid() : 0u).U8((byte)type).ToArray()); _pick = Pick.None; if (IsOpen) Refresh(); }

        /// <summary>The cart styles the server accepts at a base level (clif.cpp:12697-12711; type 13 needs PACKETVER ≥ 20191106, which this build is).</summary>
        public static bool StyleAllowed(int type, int baseLevel)
        {
            switch (type)
            {
                case 1: return true;
                case 2: return baseLevel > 40; case 3: return baseLevel > 65; case 4: return baseLevel > 80; case 5: return baseLevel > 90;
                case 6: return baseLevel > 100; case 7: return baseLevel > 110; case 8: return baseLevel > 120; case 9: return baseLevel > 130;
                case 13: return baseLevel > 100;
                default: return false;
            }
        }
        private static readonly int[] ChangeStyles = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 13 };
        private static readonly int[] StyleLevel = { 0, 0, 41, 66, 81, 91, 101, 111, 121, 131, 0, 0, 0, 101 };

        public void Show() { if (!HasCart) { _chat(Lang.T("You don't have a cart. Rent one from a Kafra employee."), "ffb070"); return; } _shown = true; _pick = Pick.None; _tab = 0; Refresh(); _win.Open(_head()); }
        public void Hide() { _shown = false; _pick = Pick.None; _win.Hide(); }
        /// <summary>The storage opened or closed, or its contents changed: the Storage tab follows it.</summary>
        public void StorageChanged() { if (IsOpen) Refresh(); }

        private bool StorageOpen => Storage != null && Storage.IsOpen;

        private void Refresh()
        {
            var rows = new List<ListWindow.Row>();
            if (_pick != Pick.None) { RefreshPick(rows); return; }
            if (_tab == 2 && !StorageOpen) _tab = 0;
            if (_tab == 0)
            {
                foreach (var e in Items.Values.OrderBy(x => x.Index))
                {
                    var e2 = e; bool stack = e.Amount > 1;
                    rows.Add(new ListWindow.Row
                    {
                        Icon = WristMenu.ItemIcon(e.Info), Text = $"{Inventory.FullName(e)}  <color=#ffd060>x{e.Amount}</color>",
                        A = stack ? Lang.T("Take 1") : Lang.T("~takeout~Take"), OnA = () => Take(e2.Index, 1),
                        // with the storage open the second button stores the whole stack (0x0129), as dragging it onto the storage window does
                        B = StorageOpen ? Lang.T("To storage") : stack ? Lang.T("Take all") : null, OnB = StorageOpen ? (Action)(() => ToStorage(e2.Index, e2.Amount)) : () => Take(e2.Index, e2.Amount),
                    });
                }
                if (rows.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>The cart is empty.</color>") });
            }
            else if (_tab == 1)
            {
                foreach (var e in _inv.Items.Values.OrderBy(x => x.Index))
                {
                    if (e.Wearing != 0) continue;
                    var e2 = e; bool stack = e.Amount > 1; bool ok = (e.Info.TradeFlags & 16) == 0;
                    rows.Add(new ListWindow.Row
                    {
                        Icon = WristMenu.ItemIcon(e.Info), Text = $"{Inventory.FullName(e)}  <color=#ffd060>x{e.Amount}</color>{(ok ? "" : "  " + Lang.T("<color=#ff8a7a>can't go in a cart</color>"))}",
                        A = stack ? Lang.T("Put 1") : Lang.T("Put"), OnA = () => Put(e2.Index, 1), AEnabled = ok, B = stack ? Lang.T("Put all") : null, OnB = () => Put(e2.Index, e2.Amount), BEnabled = ok,
                    });
                }
            }
            else
            {
                foreach (var e in Storage.Items.Values.OrderBy(x => x.Index))
                {
                    var e2 = e; bool stack = e.Amount > 1; bool ok = (e.Info.TradeFlags & 16) == 0;
                    rows.Add(new ListWindow.Row
                    {
                        Icon = WristMenu.ItemIcon(e.Info), Text = $"{Inventory.FullName(e)}  <color=#ffd060>x{e.Amount}</color>{(ok ? "" : "  " + Lang.T("<color=#ff8a7a>can't go in a cart</color>"))}",
                        A = stack ? Lang.T("To cart 1") : Lang.T("To cart"), OnA = () => FromStorage(e2.Index, 1), AEnabled = ok, B = stack ? Lang.T("~amount~All") : null, OnB = () => FromStorage(e2.Index, e2.Amount), BEnabled = ok,
                    });
                }
                if (rows.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>The storage is empty.</color>") });
            }
            _win.SetTitle(Lang.T("Cart"));
            string what = _tab == 0 ? (StorageOpen ? Lang.T("Take items out, or move them to the storage.") : Lang.T("Take items out.")) : _tab == 1 ? Lang.T("Put items in.") : Lang.T("Move storage items into the cart.");
            _win.SetInfo(Lang.F("{0} / {1} slots · {2:0.#} / {3:0.#} weight", Count, Max, Weight / 10f, MaxWeight / 10f) + "   " + what);
            _win.SetInput(false);
            _win.SetTabs(StorageOpen ? new[] { Lang.T("Cart"), Lang.T("Inventory"), Lang.T("Storage") } : new[] { Lang.T("Cart"), Lang.T("Inventory") }, _tab, t => { _tab = t; Refresh(); });
            var foot = new List<(string, Action)> { (Lang.T("Close"), Hide) };
            if (KnowsSkill != null && KnowsSkill(SkillChangeCart)) foot.Add((Lang.T("Change cart"), () => CastSkill?.Invoke(SkillChangeCart)));
            if (CanVend != null && CanVend()) foot.Add((Lang.T("Open a shop"), () => OpenShop?.Invoke()));
            foot.Add((Time.time < _removeArmedUntil ? Lang.T("Sure? Remove") : Lang.T("Remove cart"), PressRemove));
            _win.SetFooter(foot.ToArray());
            _win.SetRows(rows);
        }

        /// <summary>"Remove cart": a second press within 4 s sends 0x012a (the first one only asks).</summary>
        public void PressRemove()
        {
            if (Time.time < _removeArmedUntil) { _removeArmedUntil = 0; RemoveCart(); _chat(Lang.T("You put the cart away (a mount or falcon goes first). Its items stay saved with you."), "9ad1ff"); }
            else { _removeArmedUntil = Time.time + 4f; _chat(Lang.T("Press \"Remove cart\" again to put the cart away."), "ffd060"); }
            if (IsOpen) Refresh();
        }

        private void RefreshPick(List<ListWindow.Row> rows)
        {
            int lv = BaseLevel != null ? BaseLevel() : 1;
            if (_pick == Pick.Change)
            {
                _win.SetTitle(Lang.T("Change cart"));
                _win.SetInfo(Lang.F("Choose the cart's look. Now: style {0}.", CartType));
                foreach (int t in ChangeStyles)
                {
                    int t2 = t; bool ok = StyleAllowed(t, lv);
                    rows.Add(new ListWindow.Row { Text = ok ? Lang.F("Cart style {0}", t) : Lang.F("Cart style {0}  <color=#9aa3b5>(base level {1}+)</color>", t, StyleLevel[t]), A = t == CartType ? Lang.T("Current") : Lang.T("Choose"), OnA = () => ChangeTo(t2), AEnabled = ok && t != CartType, Highlight = t == CartType,
                                                  Icon = CartPicture(t), IconSize = CartPictureSize }); // client wave 5 (w5ui)
                }
            }
            else
            {
                _win.SetTitle(Lang.T("Decorate cart"));
                _win.SetInfo(Lang.F("Choose the cart's decoration. Now: style {0}.", CartType));
                foreach (int t in _decorTypes)
                {
                    int t2 = t;
                    rows.Add(new ListWindow.Row { Text = Lang.F("Decorated cart {0}", t), A = t == CartType ? Lang.T("Current") : Lang.T("Choose"), OnA = () => Decorate(t2), AEnabled = t != CartType, Highlight = t == CartType,
                                                  Icon = CartPicture(t), IconSize = CartPictureSize }); // client wave 5 (w5ui)
                }
            }
            _win.SetTabs(null, 0, null); _win.SetInput(false);
            _win.SetFooter((Lang.T("Back"), () => { _pick = Pick.None; Refresh(); }), (Lang.T("Close"), Hide));
            _win.SetRows(rows);
        }

        /// <summary>
        /// Client wave 5 (w5ui): the picture of a cart style in the change / decorate lists — the cart's own sprite from the player's data,
        /// as roBrowserLegacy's change-cart window draws each choice (a reimplementation: UI/Components/ChangeCart/ChangeCart.js
        /// loadCartData :77-84 loads DB.getCartPath(i) for every cart id, drawActionToCanvas / render :207-243 draws action 0 at its middle
        /// frame; DB/DBManager.js:2118-2136 the paths, the same table as Sprites/UnitFollowers.cs CartNames). The style number is the cart
        /// id the server sends back in EFST_ON_PUSH_CART val1 (Cart.CartType) and the decorate types 10-12 of ZC_SELECTCART are cart ids too
        /// (decoratecart.cpp:12-18). Job 0 (not a Super Novice) so each style shows its own sprite, as rB's window does.
        /// </summary>
        public static Sprite CartPicture(int type) => SpritePortrait.Frame(Loki.Sprites.UnitFollowers.CartPath(type, 0), 0);
        public const float CartPictureSize = 80f;

        // ---- client wave 4 smoke hooks (the smoke presses what the buttons press)
        public (int index, int itemId, int amount) LastAdded { get; private set; } = (-1, 0, 0);
        public int LastFail = -1;
        public bool Picking => _pick != Pick.None;
        public string PickKind => _pick.ToString();
        public List<int> DecorStyles => new List<int>(_decorTypes);
        private string Info => string.Format("{0} / {1} slots · {2:0.#} / {3:0.#} weight", Count, Max, Weight / 10f, MaxWeight / 10f);
    }
}
