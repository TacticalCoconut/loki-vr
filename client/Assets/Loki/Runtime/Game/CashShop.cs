using System;
using System.Collections.Generic;
using System.Linq;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Cash shop (research/windows9_packets.md §8; rAthena e985006, PACKETVER 20211103). Open with 0x0b6d {tab.L} (0x0844 would make the server
    /// read past the packet) → 0x0b6e {cash.L, kafra.L, tab.L}; the item list 0x08c9 → one 0x08ca {count.W, tab.W, {item.L, price.L}*} per tab is
    /// sent once per login session, so it is cached. Buy 0x0848 {count.W, kafraPoints.L, {item.L, amount.L, tab.W}*} → one 0x0849 {item.L,
    /// result.W, cash.L, kafra.L} per delivered stack (0 ok, 2 not enough cash, 4 weight, 5 no room, 6 busy, 7 too many, 11 bad request …).
    /// Kafra points above the price would be turned into cash points (pc_paycash), so they are capped at the total. Close 0x084a.
    /// Sale broadcasts 0x09b2 / 0x09b3 / 0x09c4 can arrive at any time. Items come from the server's db/import/item_cash.yml.
    /// </summary>
    public sealed class CashShop
    {
        public static readonly string[] TabNames = { Lang.N("~cashtab~New"), Lang.N("~cashtab~Hot"), Lang.N("~cashtab~Limited"), Lang.N("~cashtab~Rental"), Lang.N("~cashtab~Permanent"), Lang.N("~cashtab~Scrolls"), Lang.N("~cashtab~Consumables"), Lang.N("~cashtab~Other"), Lang.N("~cashtab~Sale") }; // w4lang: table keys, shown with Lang.T
        public readonly Dictionary<int, List<(int item, int price)>> Tabs = new Dictionary<int, List<(int, int)>>();
        public readonly Dictionary<int, (long endsAt, int left)> Sales = new Dictionary<int, (long, int)>();
        public long Cash = -1, Kafra = -1;
        public bool Open { get; private set; }
        public bool UseKafra;
        public int LastResult = -1, Delivered;
        private bool _listAsked;
        public event Action Changed;

        private readonly RoSession _s; private readonly ListWindow _win; private readonly Func<Camera> _head; private readonly Action<string, string> _chat;

        public CashShop(RoSession s, ListWindow win, Func<Camera> head, Action<string, string> chat)
        {
            _s = s; _win = win; _head = head; _chat = chat;
            _win.OnCloseClicked = Hide;
        }
        public void ResetForCharacter() { Tabs.Clear(); Sales.Clear(); _listAsked = false; Open = false; Cash = Kafra = -1; _win.Hide(); }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x0b6e: // ZC_SE_CASHSHOP_OPEN cash.L kafra.L tab.L
                {
                    r.Skip(2); Cash = r.U32(); Kafra = r.U32(); Open = true;
                    Debug.Log($"[Cash] open: {Cash} cash, {Kafra} kafra");
                    if (!_listAsked) { _listAsked = true; _s.SendPacket(new PacketWriter(0x08c9).ToArray()); }
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x08ca: // ZC_ACK_SCHEDULER_CASHITEM len.W count.W tab.W {item.L price.L}*
                {
                    r.Skip(4); int count = r.U16(); int tab = r.U16();
                    if (!Tabs.TryGetValue(tab, out var list)) Tabs[tab] = list = new List<(int, int)>();
                    for (int i = 0; i < count && r.Remaining >= 8; i++) { int item = (int)r.U32(); int price = r.S32(); list.RemoveAll(x => x.Item1 == item); list.Add((item, price)); }
                    Debug.Log($"[Cash] tab {tab}: {list.Count} items"); Changed?.Invoke(); Refresh(); return true;
                }
                case 0x0849: // ZC_SE_PC_BUY_CASHITEM_RESULT item.L result.W cash.L kafra.L
                {
                    r.Skip(2); int item = (int)r.U32(); LastResult = r.U16(); Cash = r.U32(); Kafra = r.U32();
                    if (LastResult == 0) { Delivered++; _chat(item > 0 ? Lang.F("Bought {0}.", GameDb.GetItem(item).Name) : Lang.T("Bought it."), "a8e6a3"); }
                    else _chat(ResultText(LastResult), "ffb070");
                    Debug.Log($"[Cash] buy result {LastResult} item {item}; {Cash} cash, {Kafra} kafra"); Changed?.Invoke(); Refresh(); return true;
                }
                case 0x09b2: // ZC_NOTIFY_BARGAIN_SALE_SELLING item.L secondsLeft.L
                {
                    r.Skip(2); int item = (int)r.U32(); uint secs = r.U32();
                    Sales[item] = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + secs, Sales.TryGetValue(item, out var s) ? s.left : -1); Refresh(); return true;
                }
                case 0x09b3: { r.Skip(2); Sales.Remove((int)r.U32()); Refresh(); return true; } // ZC_NOTIFY_BARGAIN_SALE_CLOSE
                case 0x09c4: // ZC_ACK_COUNT_BARGAIN_SALE_ITEM item.L amountLeft.L
                {
                    r.Skip(2); int item = (int)r.U32(); int left = r.S32();
                    Sales[item] = (Sales.TryGetValue(item, out var s) ? s.endsAt : 0, left); Refresh(); return true;
                }
                default: return false;
            }
        }

        public static string ResultText(int r) => r == 2 ? Lang.T("Not enough cash points.") : r == 3 ? Lang.T("That item isn't sold here.") : r == 4 ? Lang.T("You can't carry that much weight.") :
            r == 5 ? Lang.T("Your inventory is full.") : r == 6 ? Lang.T("Finish your trade first.") : r == 7 ? Lang.T("That's more than you can hold of that item.") : r == 8 ? Lang.T("Some items couldn't be delivered.") :
            r == 12 ? Lang.T("The shop is busy — try again.") : Lang.T("The purchase failed.");

        public void Request(int tab = 0) { _s.SendPacket(new PacketWriter(0x0b6d).U32((uint)tab).ToArray()); Debug.Log("[Cash] open request"); }
        public void Buy(int item, int amount, int tab, int price)
        {
            if (amount < 1) return;
            long total = (long)price * amount;
            long kafra = UseKafra ? Math.Min(Math.Max(0, Kafra), total) : 0; // never more than the price: the rest would turn into cash points
            if (Cash + kafra < total) { _chat(Lang.T("Not enough cash points."), "ffb070"); return; }
            LastResult = -1;
            var w = new PacketWriter(0x0848).U16(0).U16(1).U32((uint)kafra).U32((uint)item).U32((uint)amount).U16((ushort)tab);
            _s.SendPacket(w.ToArrayWithLength()); Debug.Log($"[Cash] buy {amount} x {item} (tab {tab}, {total} total, {kafra} kafra)");
        }

        // ------------------------------------------------------------------ window
        private int _tab = -1;
        public bool Visible => _win.Visible;
        public void Show() { _tab = -1; _win.SetTabs(null, 0, null); _win.SetInput(false); _win.Open(_head()); Request(); Refresh(); }
        public void Hide() { if (Open) { _s.SendPacket(new PacketWriter(0x084a).ToArray()); Open = false; } _win.Hide(); }

        public void Refresh()
        {
            if (!_win.Visible) return;
            _win.SetTitle(_tab < 0 ? Lang.T("Cash shop") : Lang.T("Cash shop") + " · " + Lang.T(TabNames[Mathf.Clamp(_tab, 0, 8)]));
            _win.SetInfo(Cash < 0 ? Lang.T("Opening…") : UseKafra ? Lang.F("Cash {0:N0}  ·  Kafra points {1:N0} (used first)", Cash, Kafra) : Lang.F("Cash {0:N0}  ·  Kafra points {1:N0}", Cash, Kafra));
            var rows = new List<ListWindow.Row>();
            if (_tab < 0)
            {
                foreach (var kv in Tabs.OrderBy(k => k.Key))
                {
                    int t = kv.Key;
                    rows.Add(new ListWindow.Row { Text = Lang.T(TabNames[Mathf.Clamp(t, 0, 8)]) + "  " + (kv.Value.Count > 1 ? Lang.F("<color=#9aa3b5>{0} items</color>", kv.Value.Count) : Lang.F("<color=#9aa3b5>{0} item</color>", kv.Value.Count)), A = Lang.T("Open"), OnA = () => { _tab = t; Refresh(); } });
                }
                if (rows.Count == 0) rows.Add(new ListWindow.Row { Text = Open ? Lang.T("<color=#9aa3b5>Nothing for sale (db/import/item_cash.yml is empty).</color>") : Lang.T("<color=#9aa3b5>Opening the shop…</color>") });
                _win.SetFooter((UseKafra ? Lang.T("Kafra pts: on") : Lang.T("Kafra pts: off"), () => { UseKafra = !UseKafra; Refresh(); }), (Lang.T("Close"), Hide));
            }
            else
            {
                if (Tabs.TryGetValue(_tab, out var list))
                    foreach (var (item, price) in list)
                    {
                        int it = item, pr = price, tb = _tab; var info = GameDb.GetItem(item);
                        string sale = Sales.TryGetValue(item, out var s) ? "  " + (s.left >= 0 ? Lang.F("<color=#ff8a7a>sale, {0} left</color>", s.left) : Lang.T("<color=#ff8a7a>sale</color>")) : "";
                        rows.Add(new ListWindow.Row
                        {
                            Icon = WristMenu.ItemIcon(info), Text = $"{info.Name}  <color=#ffd060>{price:N0}</color>{sale}",
                            A = Lang.T("Buy 1"), OnA = () => Buy(it, 1, tb, pr), AEnabled = Cash + (UseKafra ? Kafra : 0) >= price,
                            B = Lang.T("Buy 5"), OnB = () => Buy(it, 5, tb, pr), BEnabled = Cash + (UseKafra ? Kafra : 0) >= price * 5L,
                        });
                    }
                _win.SetFooter((Lang.T("Back"), () => { _tab = -1; Refresh(); }), (UseKafra ? Lang.T("Kafra pts: on") : Lang.T("Kafra pts: off"), () => { UseKafra = !UseKafra; Refresh(); }), (Lang.T("Close"), Hide));
            }
            _win.SetRows(rows);
        }
    }
}
