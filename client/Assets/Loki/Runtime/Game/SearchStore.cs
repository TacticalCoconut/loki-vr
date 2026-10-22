using System;
using System.Collections.Generic;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Search store / Universal Catalog (client wave 2; rAthena e985006 searchstore.cpp + clif.cpp:19449-19648, research §B):
    ///   ZC_OPEN_SEARCH_STORE_INFO 0x083a effect.W uses.B (0 normal: a click shows where the shop is; 1 remote: a click opens the shop from
    ///   anywhere, one purchase per click) → CZ_SEARCH_STORE_INFO 0x0835 len.W type.B (0 vending, 1 buying stores) maxPrice.L minPrice.L
    ///   itemCount.B cardCount.B itemId.L* cardId.L* (0 = unbounded; each search spends a use, 10 s apart) → ZC_SEARCH_STORE_INFO_ACK 0x0b64
    ///   len.W firstPage.B nextPage.B usesLeft.B {storeId.L aid.L shop[80] itemId.L type.B price.L amount.W card.L×4 option(5×5) refine.B
    ///   grade.B}* (142 bytes, ≤ 10 per page; 0x0836 is not used by this version) or ZC_SEARCH_STORE_INFO_FAILED 0x0837 reason.B (0 none
    ///   found → msgstringtable 1803, 1 too many → 1784 (the first 30 still come), 2 no uses → 1798, 3 too soon → 1800, 4 gone → 1797);
    ///   next page CZ 0x0838; click CZ_SSILIST_ITEM_CLICK 0x083c aid.L storeId.L itemId.L → ZC_SSILIST_ITEM_CLICK_ACK 0x083d x.W y.W
    ///   ((-1,-1) another map) or, remote, the shop's own list (0x0b3d vending / 0x0818 buying store, handled by Market); close CZ 0x083b.
    /// Items are found by name in the client's item table (the server checks every id it gets).
    /// </summary>
    public sealed class SearchStore
    {
        public sealed class Hit { public uint StoreId, Aid; public string Shop; public int ItemId, Type, Price, Amount, Refine, Grade; public int[] Cards; }
        public readonly List<Hit> Hits = new List<Hit>();
        public bool IsOpen { get; private set; }
        public int Effect { get; private set; }
        public int Uses { get; private set; }
        public bool NextPage { get; private set; }
        public int LastFail = -1; public int Answers;
        public (int x, int y)? LastClick;
        /// <summary>Where a clicked shop stands (NORMAL catalogs): the game marks it on the minimap.</summary>
        public Action<int, int> MarkShop;
        private bool _buying; private string _query = ""; private readonly List<int> _items = new List<int>(); private int _minPrice, _maxPrice;
        private enum Field { None, Name, Min, Max }
        private Field _edit;
        private readonly RoSession _s; private readonly ListWindow _win; private readonly Func<Camera> _head; private readonly Action<string, string> _chat;

        public SearchStore(RoSession s, ListWindow win, Func<Camera> head, Action<string, string> chat)
        {
            _s = s; _win = win; _head = head; _chat = chat;
            _win.OnCloseClicked = Close;
        }

        public void Reset() { IsOpen = false; Hits.Clear(); _win.Hide(); } // the server closes the catalog on a map change (unit.cpp:3644)

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x083a:
                {
                    r.Skip(2); Effect = r.U16(); Uses = r.U8(); IsOpen = true; Hits.Clear(); NextPage = false;
                    Debug.Log($"[SearchStore] open: effect {Effect}, {Uses} use(s)");
                    _win.Open(_head()); Refresh(); return true;
                }
                case 0x0b64:
                {
                    r.Skip(4); bool first = r.U8() != 0; NextPage = r.U8() != 0; Uses = r.U8();
                    if (first) Hits.Clear();
                    while (r.Remaining >= 142)
                    {
                        var h = new Hit { StoreId = r.U32(), Aid = r.U32(), Shop = r.Str(80), ItemId = (int)r.U32(), Type = r.U8(), Price = (int)r.U32(), Amount = r.U16() };
                        h.Cards = Inventory.ReadCards(ref r); r.Skip(25); h.Refine = r.U8(); h.Grade = r.U8();
                        Hits.Add(h);
                    }
                    Answers++;
                    Debug.Log($"[SearchStore] results: {Hits.Count} (next page {NextPage}, uses left {Uses})");
                    Refresh(); return true;
                }
                case 0x0837:
                {
                    r.Skip(2); LastFail = r.U8(); Answers++;
                    int msg = LastFail == 0 ? 1803 : LastFail == 1 ? 1784 : LastFail == 2 ? 1798 : LastFail == 3 ? 1800 : 1797;
                    _chat(ClientText.Msg(msg), "ffb070");
                    Debug.Log($"[SearchStore] failed {LastFail}");
                    Refresh(); return true;
                }
                case 0x083d:
                {
                    r.Skip(2); int x = r.S16(), y = r.S16(); LastClick = (x, y);
                    if (x < 0 || y < 0) _chat(Lang.T("That shop is on another map."), "ffb070");
                    else { _chat(Lang.F("The shop is at {0}, {1} — marked on your minimap.", x, y), "9ad1ff"); MarkShop?.Invoke(x, y); }
                    Debug.Log($"[SearchStore] shop at {x},{y}");
                    return true;
                }
                default: return false;
            }
        }

        /// <summary>Items whose name contains the text (the client's item table), at most 10 — what the server's search takes.</summary>
        public static List<int> FindItems(string text, int max = 10)
        {
            var l = new List<int>(); text = (text ?? "").Trim(); if (text.Length < 2) return l;
            if (int.TryParse(text, out int id)) { l.Add(id); return l; }
            foreach (var it in GameDb.AllItems())
            {
                if ((it.Name != null && it.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) || (it.English != null && it.English.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)) l.Add(it.Id);
                if (l.Count >= max) break;
            }
            return l;
        }

        public void Search()
        {
            if (!IsOpen) return;
            _items.Clear(); _items.AddRange(FindItems(_query));
            if (_items.Count == 0) { _chat(Lang.T("Type part of an item's name first."), "ffb070"); return; }
            var w = new PacketWriter(0x0835).U16(0).U8((byte)(_buying ? 1 : 0)).U32((uint)Math.Max(0, _maxPrice)).U32((uint)Math.Max(0, _minPrice)).U8((byte)_items.Count).U8(0);
            foreach (var i in _items) w.U32((uint)i);
            _s.SendPacket(w.ToArrayWithLength());
            Debug.Log($"[SearchStore] search {(_buying ? "buying stores" : "vendors")} for '{_query}' → {_items.Count} item id(s) {string.Join(",", _items)}, price {_minPrice}..{_maxPrice}");
        }
        public void More() { _s.SendPacket(new PacketWriter(0x0838).ToArray()); }
        public void Click(Hit h) { _s.SendPacket(new PacketWriter(0x083c).U32(h.Aid).U32(h.StoreId).U32((uint)h.ItemId).ToArray()); Debug.Log($"[SearchStore] click store {h.StoreId} of {h.Aid} item {h.ItemId}"); }
        public void Close() { if (IsOpen) _s.SendPacket(new PacketWriter(0x083b).ToArray()); IsOpen = false; Hits.Clear(); _edit = Field.None; _win.SetInput(false); _win.Hide(); Debug.Log("[SearchStore] closed"); }

        private void Edit(Field f)
        {
            _edit = f;
            string cur = f == Field.Name ? _query : f == Field.Min ? (_minPrice > 0 ? _minPrice.ToString() : "") : (_maxPrice > 0 ? _maxPrice.ToString() : "");
            _win.SetInput(true, f == Field.Name ? Lang.T("item name") : "zeny", f == Field.Name ? null : (Func<char, bool>)char.IsDigit, Commit, cur, f == Field.Name ? 40 : 10);
            _win.Input.Focus(); Refresh();
        }
        private void Commit()
        {
            string t = (_win.Input.Text ?? "").Trim(); var f = _edit; _edit = Field.None; _win.SetInput(false);
            if (f == Field.Name) _query = t; else if (f == Field.Min) _minPrice = int.TryParse(t, out var a) ? a : 0; else if (f == Field.Max) _maxPrice = int.TryParse(t, out var c) ? c : 0;
            Refresh();
        }

        public void Refresh()
        {
            if (!_win.Visible || !IsOpen) return;
            var rows = new List<ListWindow.Row>();
            _win.SetTitle(Effect == 1 ? Lang.T("Universal Catalog (remote)") : Lang.T("Universal Catalog"));
            _win.SetTabs(new[] { Lang.T("Sellers"), Lang.T("Buyers") }, _buying ? 1 : 0, t => { _buying = t == 1; Hits.Clear(); Refresh(); });
            if (_edit == Field.None) _win.SetInfo((Uses == 1 ? Lang.T("1 search left") : Lang.F("{0} searches left", Uses)) + "  ·  " + (Effect == 1 ? Lang.T("a click opens the shop") : Lang.T("a click shows where the shop is")));
            else _win.SetInfo(_edit == Field.Name ? Lang.T("Item name, then Enter:") : Lang.T("Price, then Enter:"));
            var found = FindItems(_query);
            // w4lang: "item: query" then the match count (a whole key per number) and the first match
            rows.Add(new ListWindow.Row { Text = Lang.F("Item: <color=#ffd060>{0}</color>", _query.Length > 0 ? _query : "—") + "  <size=19><color=#9aa3b5>" + (_query.Length > 1 ? (found.Count == 1 ? Lang.T("1 match") : Lang.F("{0} matches", found.Count)) + (found.Count > 0 ? ": " + GameDb.GetItem(found[0]).Name + (found.Count > 1 ? "…" : "") : "") : "") + "</color></size>", A = Lang.T("Edit"), OnA = () => Edit(Field.Name) });
            // w4lang: the price range is a whole sentence for each of its four shapes ("any" is not a word to put in a placeholder)
            string range = _minPrice > 0 && _maxPrice > 0 ? Lang.F("Price: {0:N0} to {1:N0} z", _minPrice, _maxPrice) : _minPrice > 0 ? Lang.F("Price: {0:N0} to any z", _minPrice) : _maxPrice > 0 ? Lang.F("Price: any to {0:N0} z", _maxPrice) : Lang.T("Price: any to any z");
            rows.Add(new ListWindow.Row { Text = range, A = Lang.T("Min"), OnA = () => Edit(Field.Min), B = Lang.T("Max"), OnB = () => Edit(Field.Max) });
            foreach (var h in Hits)
            {
                var hh = h; var info = GameDb.GetItem(h.ItemId);
                rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(info), Text = $"{(h.Refine > 0 ? "+" + h.Refine + " " : "")}{info.Name}  <color=#ffd060>{h.Price:N0} z</color>  x{h.Amount}\n<size=19><color=#9aa3b5>{h.Shop}</color></size>", A = Effect == 1 ? Lang.T("Open") : Lang.T("Where?"), OnA = () => Click(hh) });
            }
            _win.SetFooter((Lang.T("Close"), Close), (NextPage ? Lang.T("Next page") : null, More), (Uses > 0 ? Lang.T("Search") : null, Search));
            _win.SetRows(rows);
        }

    }
}
