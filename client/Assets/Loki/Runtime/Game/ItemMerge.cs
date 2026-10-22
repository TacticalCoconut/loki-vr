using System;
using System.Collections.Generic;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Item merge (client wave 2; rAthena e985006 clif.cpp:21144-21339, research §C): `mergeitem` (or the Mergician NPC) sends
    /// ZC_MERGE_ITEM_OPEN 0x096d len.W index.W* — every mergeable stack (stackable, not cash, with its own unique id) that has a
    /// partner of the same item and bound state, all item ids in one list (fewer than 2 → ZC_MSG 2183 instead). The client merges one
    /// item id at a time: CZ_REQ_MERGE_ITEM 0x096e len.W index.W* (the first index is the stack that stays; never repeat it) →
    /// ZC_ACK_MERGE_ITEM 0x096f index.W amount.W reason.B (0 ok: that stack now holds `amount`, the others were deleted with 0x07fa;
    /// 2 the total would reach the stack limit). Cancel CZ 0x0974 does nothing on the server; the window just closes.
    /// </summary>
    public sealed class ItemMerge
    {
        private readonly List<int> _indexes = new List<int>();
        public int LastReason = -1, LastAmount = -1; public bool IsOpen { get; private set; }
        public int GroupCount => Groups().Count;
        private readonly RoSession _s; private readonly Inventory _inv; private readonly ListWindow _win; private readonly Func<Camera> _head; private readonly Action<string, string> _chat;

        public ItemMerge(RoSession s, Inventory inv, ListWindow win, Func<Camera> head, Action<string, string> chat)
        {
            _s = s; _inv = inv; _win = win; _head = head; _chat = chat;
            _win.OnCloseClicked = Close;
            _inv.Changed += () => { if (IsOpen) { _indexes.RemoveAll(i => !_inv.Items.ContainsKey(i)); Refresh(); } };
        }

        public void Reset() { IsOpen = false; _indexes.Clear(); _win.Hide(); }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x096d:
                {
                    r.Skip(4); _indexes.Clear();
                    while (r.Remaining >= 2) _indexes.Add(r.S16());
                    IsOpen = true;
                    Debug.Log($"[Merge] open: {_indexes.Count} stack(s) in {Groups().Count} group(s)");
                    _win.Open(_head()); Refresh(); return true;
                }
                case 0x096f:
                {
                    r.Skip(2); int idx = r.S16(); LastAmount = r.S16(); LastReason = r.U8();
                    if (LastReason == 0 && _inv.Items.TryGetValue(idx, out var e))
                    {
                        // the server re-sends nothing for the kept stack: take the total from the ack (clif.cpp:21318-21325)
                        e.Amount = LastAmount; _inv.Patch(idx);
                        _chat(Lang.F("Merged into one stack of {0} × {1}.", LastAmount, e.Info.Name), "9fe39a");
                    }
                    else _chat(LastReason == 2 ? Lang.T("That would go over the most one stack can hold.") : Lang.T("Those items can't be merged."), "ffb070");
                    Debug.Log($"[Merge] result {LastReason}: index {idx} amount {LastAmount}");
                    Refresh(); return true;
                }
                default: return false;
            }
        }

        /// <summary>The offered stacks grouped by item id (only groups of two or more can merge).</summary>
        private List<List<Inventory.Entry>> Groups()
        {
            var by = new Dictionary<int, List<Inventory.Entry>>();
            foreach (var i in _indexes) if (_inv.Items.TryGetValue(i, out var e)) { if (!by.TryGetValue(e.ItemId, out var l)) by[e.ItemId] = l = new List<Inventory.Entry>(); l.Add(e); }
            var res = new List<List<Inventory.Entry>>();
            foreach (var l in by.Values) if (l.Count >= 2) res.Add(l);
            return res;
        }

        public void Merge(int itemId)
        {
            var g = Groups().Find(l => l[0].ItemId == itemId); if (g == null) return;
            g.Sort((a, c) => c.Amount.CompareTo(a.Amount)); // the biggest stack stays
            var w = new PacketWriter(0x096e).U16(0);
            foreach (var e in g) w.U16((ushort)e.Index);
            _s.SendPacket(w.ToArrayWithLength());
            Debug.Log($"[Merge] merge {itemId}: {string.Join(", ", g.ConvertAll(e => e.Index + "×" + e.Amount))}");
        }

        public void Close() { if (IsOpen) _s.SendPacket(new PacketWriter(0x0974).ToArray()); IsOpen = false; _indexes.Clear(); _win.Hide(); Debug.Log("[Merge] closed"); }

        public void Refresh()
        {
            if (!_win.Visible || !IsOpen) return;
            var rows = new List<ListWindow.Row>();
            foreach (var g in Groups())
            {
                int total = 0; foreach (var e in g) total += e.Amount; int id = g[0].ItemId;
                rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(g[0].Info), Text = $"{g[0].Info.Display}\n<size=19>" + Lang.F("<color=#9aa3b5>{0} stacks → one of {1}</color>", g.Count, total) + "</size>", A = Lang.T("Merge"), OnA = () => Merge(id) });
            }
            if (rows.Count == 0) rows.Add(new ListWindow.Row { Text = $"<color=#9aa3b5>{ClientText.Msg(2183)}</color>" });
            _win.SetTitle(Lang.T("Merge items")); _win.SetTabs(null, 0, null); _win.SetInput(false);
            _win.SetInfo(Lang.T("Items that came in separate stacks can be put together."));
            _win.SetFooter((Lang.T("Close"), Close));
            _win.SetRows(rows);
        }
    }
}
