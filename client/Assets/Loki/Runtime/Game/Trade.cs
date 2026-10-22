using System;
using System.Collections.Generic;
using System.Linq;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Player-to-player trade (research/trade_cart_vending_packets.md §1; rAthena e985006, PACKETVER 20211103).
    ///   request 0x00e4 (target AID) → target gets 0x01f4 (name[24], requester AID, level) → target answers 0x00e6 (3 accept / 4 reject)
    ///   → both get 0x01f5 (result.B partnerAID.L level.W: 0 too far, 1 no such character, 2 failed, 3 accepted, 4 cancelled, 5 busy).
    ///   add 0x00e8 (index.W: 0 = zeny else the inventory's wire index; amount.L) → self gets 0x00ea (index, result: 0 ok, 1 overweight,
    ///   2 refused, 3 partner's inventory full, 4 stack limit) and the partner gets 0x0b42 (itemId.L — 0 for zeny — type.B amount.L
    ///   identified.B damaged.B cards 4×L options 5×5 location.L look.W refine.B grade.B); zeny replaces, items add up, no ack for zeny.
    ///   lock 0x00eb → 0x00ea (0,0) + 0x00ec who.B (0 = you, 1 = them); commit 0x00ef after both locked → 0x00f0 to both;
    ///   cancel 0x00ed (or any failure) → the placed items come back as 0x0b41 / zeny as 0x00b1, then 0x00ee to both.
    /// The official client takes an item out of its inventory when it goes into the window, and the server relies on it: the giver
    /// never gets a delete packet on commit, and a cancel re-adds through the pickup packet. So this class does the same.
    /// </summary>
    public sealed class Trade
    {
        public enum Stage { None, Asking, Asked, Open }
        public sealed class Offer { public int ItemId, Type, Amount; public bool Identified; public int Refine; public int[] Cards = new int[4]; public uint Location; }

        public Stage State { get; private set; } = Stage.None;
        public string PartnerName { get; private set; } = ""; public uint PartnerAid { get; private set; } public int PartnerLevel { get; private set; }
        /// <summary>What we put in, by inventory index (server index): item ids and amounts already acknowledged with 0x00ea result 0.</summary>
        public readonly List<Offer> Mine = new List<Offer>();
        public readonly List<Offer> Theirs = new List<Offer>();
        public int MyZeny { get; private set; } public int TheirZeny { get; private set; }
        public bool MineLocked { get; private set; } public bool TheirsLocked { get; private set; } public bool MineConfirmed { get; private set; }
        public event Action Changed;
        /// <summary>Set by the last completed trade — for the smoke test.</summary>
        public int LastResult { get; private set; } = -1;

        private readonly RoSession _s; private readonly Inventory _inv; private readonly ListWindow _win; private readonly Func<Camera> _head;
        private readonly Action<string, string> _chat; private readonly Func<uint, string> _nameOf; private readonly Func<long> _zeny;
        private readonly Dictionary<int, (int itemId, int amount)> _pending = new Dictionary<int, (int, int)>(); // by server inventory index
        private readonly HashSet<int> _placedIdx = new HashSet<int>();
        private int _tab; // 0 inventory, 1 offers, 2 zeny

        public Trade(RoSession s, Inventory inv, ListWindow win, Func<Camera> head, Func<uint, string> nameOf, Func<long> zeny, Action<string, string> chat)
        {
            _s = s; _inv = inv; _win = win; _head = head; _nameOf = nameOf; _zeny = zeny; _chat = chat;
            _win.OnCloseClicked = () => { if (State == Stage.Asked) Decline(); else Cancel(); };
            _inv.Changed += () => { if (State == Stage.Open && _win.Visible) Refresh(); };
            _inv.QuietPickup = idx => State == Stage.Open && _placedIdx.Contains(idx); // the cancel re-add is not a pickup
        }

        public bool Active => State != Stage.None;

        /// <summary>A warp cancels a trade on the server without 0x00ee (unit.cpp:3641).</summary>
        public void Reset() { ClearAll(); _win.Hide(); }

        private void ClearAll()
        {
            State = Stage.None; PartnerName = ""; PartnerAid = 0; PartnerLevel = 0;
            Mine.Clear(); Theirs.Clear(); MyZeny = TheirZeny = 0; MineLocked = TheirsLocked = MineConfirmed = false; _pending.Clear(); _placedIdx.Clear(); _tab = 0;
            Changed?.Invoke();
        }

        /// <summary>Client wave 4: /notrade is on (every trade request is declined at once).</summary>
        public System.Func<bool> AutoDecline;

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x01f4:
                {
                    r.Skip(2); string name = r.Str(24); uint aid = r.U32(); int lv = r.U16();
                    // client wave 4 (w4social): the official /notrade declines every offer at once (msgstringtable 804-806) with the
                    // same CZ_ACK_EXCHANGE_ITEM 0x00e6 type 4 the Decline button sends (trade.cpp:129-137)
                    if (AutoDecline != null && AutoDecline()) { _s.SendPacket(new PacketWriter(0x00e6).U8(4).ToArray()); _chat(Lang.F("{0} (Lv {1}) wanted to trade: declined (/notrade).", name, lv), "c8cbd6"); Debug.Log($"[Trade] auto-declined {name}"); return true; }
                    if (State != Stage.None) { Decline(); } // the server only asks one at a time, but be safe
                    ClearAll(); State = Stage.Asked; PartnerName = name; PartnerAid = aid; PartnerLevel = lv;
                    _chat(Lang.F("{0} (Lv {1}) wants to trade with you.", name, lv), "ffd060");
                    Debug.Log($"[Trade] request from {name} ({aid}) lv {lv}");
                    Refresh(); _win.Open(_head());
                    return true;
                }
                case 0x01f5:
                {
                    r.Skip(2); int res = r.U8(); uint aid = r.U32(); int lv = r.U16();
                    Debug.Log($"[Trade] response {res} partner {aid} lv {lv}");
                    switch (res)
                    {
                        case 3:
                            State = Stage.Open; if (aid != 0) PartnerAid = aid; if (lv != 0) PartnerLevel = lv;
                            if (string.IsNullOrEmpty(PartnerName)) PartnerName = _nameOf(PartnerAid) ?? "";
                            Mine.Clear(); Theirs.Clear(); MyZeny = TheirZeny = 0; MineLocked = TheirsLocked = MineConfirmed = false; _pending.Clear(); _placedIdx.Clear(); _tab = 0;
                            _chat(Lang.F("Trading with {0}. Add items or zeny, press OK, then Trade.", PartnerName), "a8e6a3");
                            Refresh(); _win.Open(_head());
                            break;
                        case 4: _chat(State == Stage.Asking ? Lang.F("{0} declined the trade.", PartnerName) : Lang.T("Trade cancelled."), "ffb070"); ClearAll(); _win.Hide(); break;
                        case 0: _chat(Lang.T("Too far away to trade — stand next to them."), "ffb070"); ClearAll(); _win.Hide(); break;
                        case 1: _chat(Lang.T("That character isn't here."), "ffb070"); ClearAll(); _win.Hide(); break;
                        case 5: _chat(Lang.F("{0} is busy.", PartnerName), "ffb070"); ClearAll(); _win.Hide(); break;
                        default: _chat(Lang.T("The trade couldn't start (the other player is busy with an NPC, a shop, storage or another trade)."), "ffb070"); ClearAll(); _win.Hide(); break;
                    }
                    Changed?.Invoke();
                    return true;
                }
                case 0x0b42:
                {
                    r.Skip(2); int itemId = (int)r.U32(); int type = r.U8(); int amount = r.S32(); bool ident = r.U8() != 0; r.Skip(1);
                    var cards = Inventory.ReadCards(ref r); r.Skip(25); uint loc = r.U32(); r.Skip(2); int refine = r.U8();
                    if (itemId == 0) { TheirZeny = amount; _chat(Lang.F("{0} offers {1:N0} zeny.", PartnerName, amount), "c8cbd6"); }
                    else
                    {
                        var same = loc == 0 ? Theirs.FirstOrDefault(x => x.ItemId == itemId && x.Location == 0) : null;
                        if (same != null) same.Amount += amount; else Theirs.Add(new Offer { ItemId = itemId, Type = type, Amount = amount, Identified = ident, Refine = refine, Cards = cards, Location = loc });
                        _chat(Lang.F("{0} offers {1} x{2}.", PartnerName, OfferName(itemId, refine, ident, loc), amount), "c8cbd6");
                    }
                    Debug.Log($"[Trade] partner adds item {itemId} x{amount}");
                    Refresh(); Changed?.Invoke();
                    return true;
                }
                case 0x00ea:
                {
                    r.Skip(2); int cidx = r.U16(); int res = r.U8();
                    if (cidx == 0) { Debug.Log($"[Trade] ack result {res}"); return true; } // the OK ack / a zero-amount add
                    int idx = cidx; // the inventory model is keyed by the wire (client) index
                    _pending.TryGetValue(idx, out var p); _pending.Remove(idx);
                    if (res == 0)
                    {
                        if (p.amount > 0 && _inv.Items.TryGetValue(idx, out var e))
                        {
                            var mine = e.IsEquip ? null : Mine.FirstOrDefault(x => x.ItemId == e.ItemId && x.Location == 0);
                            if (mine != null) mine.Amount += p.amount; else Mine.Add(new Offer { ItemId = e.ItemId, Type = e.Type, Amount = p.amount, Identified = e.Identified, Refine = e.Refine, Cards = e.Cards, Location = e.Location });
                            _placedIdx.Add(idx);
                            _inv.TakeOut(idx, p.amount); // the official client's behaviour, which the server counts on (no delete on commit, re-add on cancel)
                        }
                        Debug.Log($"[Trade] added inventory {idx} x{p.amount}");
                    }
                    else
                    {
                        _chat(res == 1 ? Lang.F("{0} can't carry that much weight.", PartnerName) : res == 2 ? Lang.T("That item can't be traded.") : res == 3 ? Lang.F("{0} has no free inventory slot.", PartnerName) : res == 4 ? Lang.F("{0} can't hold that many of that item.", PartnerName) : Lang.F("Couldn't add that item ({0}).", res), "ffb070");
                        Debug.Log($"[Trade] add refused: index {idx} result {res}");
                    }
                    Refresh(); Changed?.Invoke();
                    return true;
                }
                case 0x00ec:
                {
                    r.Skip(2); int who = r.U8();
                    if (who == 0) MineLocked = true; else TheirsLocked = true;
                    if (who == 1) _chat(Lang.F("{0} pressed OK.", PartnerName), "c8cbd6");
                    Debug.Log($"[Trade] locked by {(who == 0 ? "us" : "them")}");
                    Refresh(); Changed?.Invoke();
                    return true;
                }
                case 0x00ee:
                {
                    Debug.Log("[Trade] cancelled");
                    if (State != Stage.None) _chat(Lang.T("The trade was cancelled."), "ffb070");
                    LastResult = 1;
                    ClearAll(); _win.Hide();
                    return true;
                }
                case 0x00f0:
                {
                    r.Skip(2); int res = r.U8();
                    Debug.Log($"[Trade] completed result {res}");
                    _chat(res == 0 ? Lang.F("Trade with {0} completed.", PartnerName) : Lang.F("Trade failed ({0}).", res), res == 0 ? "a8e6a3" : "ffb070");
                    LastResult = res;
                    ClearAll(); _win.Hide();
                    return true;
                }
                case 0x00f1: { Debug.Log("[Trade] undo (never sent by rAthena)"); return true; }
                default: return false;
            }
        }

        private static string OfferName(int itemId, int refine, bool ident, uint loc)
        {
            var info = GameDb.GetItem(itemId);
            string s = info.Display;
            if (refine > 0) s = "+" + refine + " " + s;
            if (!ident && loc != 0) s = Lang.T("(unidentified)") + " " + s;
            return s;
        }

        // ---- sending
        public void Request(uint aid, string name)
        {
            if (State != Stage.None) { _chat(Lang.T("You are already trading."), "ffb070"); return; }
            ClearAll(); State = Stage.Asking; PartnerAid = aid; PartnerName = name ?? "";
            _s.SendPacket(new PacketWriter(0x00e4).U32(aid).ToArray());
            _chat(Lang.F("Asking {0} to trade…", PartnerName), "c8cbd6");
            Debug.Log($"[Trade] request to {aid} '{name}'");
            Changed?.Invoke();
        }
        public void Accept() { if (State == Stage.Asked) _s.SendPacket(new PacketWriter(0x00e6).U8(3).ToArray()); }
        public void Decline() { if (State == Stage.Asked) { _s.SendPacket(new PacketWriter(0x00e6).U8(4).ToArray()); ClearAll(); _win.Hide(); } }
        public void AddItem(int invIndex, int amount)
        {
            if (State != Stage.Open || MineLocked || amount <= 0) return;
            if (!_inv.Items.TryGetValue(invIndex, out var e)) return;
            if (e.Wearing != 0) { _chat(Lang.T("Take it off first."), "ffb070"); return; }
            if (_pending.ContainsKey(invIndex)) return; // wait for the ack of the previous add of this stack
            _pending[invIndex] = (e.ItemId, Math.Min(amount, e.Amount));
            _s.SendPacket(new PacketWriter(0x00e8).U16((ushort)invIndex).S32(Math.Min(amount, e.Amount)).ToArray()); // Inventory keys are the wire indices already
        }
        /// <summary>Zeny offered — replaces the previous amount on the server; never more than we have.</summary>
        public void SetZeny(long z)
        {
            if (State != Stage.Open || MineLocked) return;
            z = Math.Max(0, Math.Min(z, _zeny()));
            MyZeny = (int)Math.Min(z, int.MaxValue);
            _s.SendPacket(new PacketWriter(0x00e8).U16(0).S32(MyZeny).ToArray());
            Refresh(); Changed?.Invoke();
        }
        public void Lock() { if (State == Stage.Open && !MineLocked) _s.SendPacket(new PacketWriter(0x00eb).ToArray()); }
        public void Confirm() { if (State == Stage.Open && MineLocked && !MineConfirmed) { MineConfirmed = true; _s.SendPacket(new PacketWriter(0x00ef).ToArray()); Refresh(); Changed?.Invoke(); } }
        public void Cancel()
        {
            if (State == Stage.None) return;
            _s.SendPacket(new PacketWriter(0x00ed).ToArray());
            if (State == Stage.Asking) { ClearAll(); _win.Hide(); }
            Debug.Log("[Trade] cancel sent");
        }

        // ---- window
        private void Refresh()
        {
            var rows = new List<ListWindow.Row>();
            if (State == Stage.Asked)
            {
                _win.SetTitle(Lang.T("Trade request"));
                _win.SetInfo(Lang.F("{0} (Lv {1}) wants to trade with you.", PartnerName, PartnerLevel));
                _win.SetTabs(null, 0, null);
                _win.SetFooter((Lang.T("Accept"), Accept), (Lang.T("Decline"), Decline));
                _win.SetRows(rows, true);
                return;
            }
            if (State != Stage.Open) return;
            _win.SetTitle(Lang.F("Trade with {0}  <color=#9aa3b5>Lv {1}</color>", PartnerName, PartnerLevel));
            // w4lang: the item count is a whole key per number (1 item / {0} items); the zeny and OK parts carry no words
            string mine = (Mine.Count == 1 ? Lang.T("You: 1 item") : Lang.F("You: {0} items", Mine.Count)) + $"{(MyZeny > 0 ? $" + {MyZeny:N0} z" : "")} {(MineLocked ? "<color=#7fd67f>OK</color>" : "")}";
            string theirs = (Theirs.Count == 1 ? Lang.F("{0}: 1 item", PartnerName) : Lang.F("{0}: {1} items", PartnerName, Theirs.Count)) + $"{(TheirZeny > 0 ? $" + {TheirZeny:N0} z" : "")} {(TheirsLocked ? "<color=#7fd67f>OK</color>" : Lang.T("<color=#9aa3b5>waiting</color>"))}";
            _win.SetInfo(mine + "   ·   " + theirs);
            _win.SetTabs(new[] { Lang.T("Inventory"), Lang.T("Offers"), Lang.T("Zeny") }, _tab, t => { _tab = t; Refresh(); });
            if (_tab == 0)
            {
                foreach (var e in _inv.Items.Values.OrderBy(x => x.Index))
                {
                    if (e.Wearing != 0) continue;
                    var e2 = e; bool stack = e.Amount > 1; bool can = !MineLocked && !_pending.ContainsKey(e.Index);
                    rows.Add(new ListWindow.Row
                    {
                        Icon = WristMenu.ItemIcon(e.Info), Text = $"{Inventory.FullName(e)}  <color=#ffd060>x{e.Amount}</color>",
                        A = stack ? Lang.T("Add 1") : Lang.T("Add"), OnA = () => AddItem(e2.Index, 1), AEnabled = can,
                        B = stack ? Lang.T("Add all") : null, OnB = () => AddItem(e2.Index, e2.Amount), BEnabled = can,
                    });
                }
            }
            else if (_tab == 1)
            {
                foreach (var m in Mine) rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(GameDb.GetItem(m.ItemId)), Text = Lang.F("<color=#9ad1ff>You give</color>  {0}  <color=#ffd060>x{1}</color>", OfferName(m.ItemId, m.Refine, m.Identified, m.Location), m.Amount) });
                if (MyZeny > 0) rows.Add(new ListWindow.Row { Text = Lang.F("<color=#9ad1ff>You give</color>  <color=#ffd060>{0:N0} zeny</color>", MyZeny) });
                foreach (var t in Theirs) rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(GameDb.GetItem(t.ItemId)), Text = Lang.F("<color=#a8e6a3>You get</color>  {0}  <color=#ffd060>x{1}</color>", OfferName(t.ItemId, t.Refine, t.Identified, t.Location), t.Amount), Highlight = true });
                if (TheirZeny > 0) rows.Add(new ListWindow.Row { Text = Lang.F("<color=#a8e6a3>You get</color>  <color=#ffd060>{0:N0} zeny</color>", TheirZeny), Highlight = true });
                if (rows.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>Nothing offered yet — add items from the Inventory tab or zeny from the Zeny tab.</color>") });
            }
            else
            {
                rows.Add(new ListWindow.Row { Text = Lang.F("Offering <color=#ffd060>{0:N0} zeny</color> of your {1:N0}", MyZeny, _zeny()), A = Lang.T("Clear"), OnA = () => SetZeny(0), AEnabled = !MineLocked && MyZeny > 0 });
                foreach (int step in new[] { 1000, 10000, 100000, 1000000 })
                {
                    int s2 = step;
                    rows.Add(new ListWindow.Row { Text = $"{step:N0} zeny", A = "+", OnA = () => SetZeny((long)MyZeny + s2), AEnabled = !MineLocked && MyZeny + s2 <= _zeny(), B = "−", OnB = () => SetZeny((long)MyZeny - s2), BEnabled = !MineLocked && MyZeny >= s2 });
                }
                rows.Add(new ListWindow.Row { Text = Lang.T("All of it"), A = Lang.T("Offer all"), OnA = () => SetZeny(_zeny()), AEnabled = !MineLocked && _zeny() > 0 });
            }
            // footer: right to left
            if (!MineLocked) _win.SetFooter(("OK", Lock), (Lang.T("Cancel"), Cancel));
            else if (!MineConfirmed) _win.SetFooter((Lang.T("Trade"), Confirm), (Lang.T("Cancel"), Cancel));
            else _win.SetFooter((Lang.T("Waiting…"), () => { }), (Lang.T("Cancel"), Cancel));
            _win.SetRows(rows);
        }

        /// <summary>For the smoke test.</summary>
        public void ShowTab(int t) { _tab = t; if (State == Stage.Open) Refresh(); }
    }
}
