using System;
using System.Collections.Generic;
using System.Linq;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Equipment swap set (research/windows9_packets.md §6; rAthena e985006). The set is saved on the server and sent as 0x0a9b {index.W pos.L}*
    /// on every map load (4 bytes when empty). Add 0x0a97 {index, position} → 0x0a98 {index, pos, flag.W: 0 ok, 1 level, 2 failed} (worn items
    /// can't be added); remove 0x0a99 {index} → 0x0a9a; swap everything 0x0a9c — a cast of ALL_EQSWITCH (5067; 10 s cooldown, ignored while it
    /// runs) that answers 0x0a9d and then moves items between the body and the set with the usual equip packets plus 0x0a98 / 0x0a9a; swap one
    /// item 0x0ace {index} (an item not in the set is added instead). Items in the set can't be mailed, traded, stored, sold or dropped.
    /// </summary>
    public sealed class EquipSwitch
    {
        /// <summary>Client inventory index → equip position mask.</summary>
        public readonly Dictionary<int, uint> Set = new Dictionary<int, uint>();
        public int LastAdd = -1; public bool LastSwitchAck;
        public float CooldownUntil;
        public event Action Changed;

        private readonly RoSession _s; private readonly Inventory _inv; private readonly ListWindow _win; private readonly Func<Camera> _head; private readonly Action<string, string> _chat;

        public EquipSwitch(RoSession s, Inventory inv, ListWindow win, Func<Camera> head, Action<string, string> chat)
        {
            _s = s; _inv = inv; _win = win; _head = head; _chat = chat;
            _win.OnCloseClicked = Hide;
            _inv.Changed += () => { if (_win.Visible) Refresh(); };
        }
        public void Reset() { Set.Clear(); _picking = false; _win.Hide(); Changed?.Invoke(); }

        public static string SlotName(uint pos)
        {
            var p = new List<string>();
            // w4lang: each position is its own word, joined by " + " (in the current language, as EquipmentWindow.SlotNames)
            if ((pos & 0x100) != 0) p.Add(Lang.T("~pos~top")); if ((pos & 0x200) != 0) p.Add(Lang.T("~pos~middle")); if ((pos & 0x1) != 0) p.Add(Lang.T("~pos~lower"));
            if ((pos & 0x2) != 0) p.Add(Lang.T("~pos~right hand")); if ((pos & 0x20) != 0) p.Add(Lang.T("~pos~left hand")); if ((pos & 0x10) != 0) p.Add(Lang.T("~pos~armor"));
            if ((pos & 0x4) != 0) p.Add(Lang.T("~pos~garment")); if ((pos & 0x40) != 0) p.Add(Lang.T("~pos~shoes")); if ((pos & 0x88) != 0) p.Add(Lang.T("~pos~accessory"));
            if ((pos & 0x3c00) != 0) p.Add(Lang.T("~pos~costume")); if ((pos & 0x8000) != 0) p.Add(Lang.T("~pos~ammo")); if ((pos & 0x3f0000) != 0) p.Add(Lang.T("~pos~shadow"));
            return p.Count > 0 ? string.Join(" + ", p) : "";
        }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x0a9b: // ZC_EQUIPSWITCH_LIST len.W {index.W position.L}*
                {
                    r.Skip(4); Set.Clear();
                    while (r.Remaining >= 6) { int idx = r.U16(); uint pos = r.U32(); Set[idx] = pos; }
                    Debug.Log($"[Swap] set: {Set.Count} items"); Changed?.Invoke(); Refresh(); return true;
                }
                case 0x0a98: // ZC_ACK_ADD_EQUIPSWITCH index.W position.L flag.W
                {
                    r.Skip(2); int idx = r.U16(); uint pos = r.U32(); int flag = r.U16(); LastAdd = flag;
                    if (flag == 0) Set[idx] = pos;
                    else _chat(flag == 1 ? Lang.T("Your level is too low for that item.") : Lang.T("That item can't go into the swap set."), "ffb070");
                    Debug.Log($"[Swap] add {idx} pos 0x{pos:x} flag {flag}"); Changed?.Invoke(); Refresh(); return true;
                }
                case 0x0a9a: // ZC_ACK_REMOVE_EQUIPSWITCH index.W position.L failed.W
                {
                    r.Skip(2); int idx = r.U16(); Set.Remove(idx);
                    Debug.Log($"[Swap] removed {idx}"); Changed?.Invoke(); Refresh(); return true;
                }
                case 0x0a9d: // ZC_ACK_EQUIPSWITCH failed.W
                {
                    r.Skip(2); LastSwitchAck = true;
                    if (Set.Count == 0) _chat(Lang.T("There's nothing in the swap set."), "ffb070");
                    Debug.Log("[Swap] switch ack"); Changed?.Invoke(); return true;
                }
                default: return false;
            }
        }

        public void Add(Inventory.Entry e)
        {
            if (e == null || !e.IsEquip) return;
            if (e.Wearing != 0) { _chat(Lang.T("Take it off first — worn items can't go into the swap set."), "ffb070"); return; }
            LastAdd = -1; _s.SendPacket(new PacketWriter(0x0a97).U16((ushort)e.Index).U32(e.Location).ToArray());
        }
        public void Remove(int index) { _s.SendPacket(new PacketWriter(0x0a99).U16((ushort)index).ToArray()); }
        public void SwitchAll()
        {
            if (Time.time < CooldownUntil) { _chat(Lang.F("Swap again in {0} s.", Mathf.CeilToInt(CooldownUntil - Time.time)), "ffb070"); return; }
            if (Set.Count == 0) { _chat(Lang.T("Put some equipment in the swap set first (Items → an item → Swap set)."), "ffb070"); return; }
            CooldownUntil = Time.time + 10f; LastSwitchAck = false; // ALL_EQSWITCH cooldown (db/re/skill_db.yml); the server drops requests inside it
            _s.SendPacket(new PacketWriter(0x0a9c).ToArray()); Debug.Log("[Swap] switch all");
        }
        public void SwitchOne(int index) { _s.SendPacket(new PacketWriter(0x0ace).U16((ushort)index).ToArray()); }
        public bool InSet(int index) => Set.ContainsKey(index);

        // ------------------------------------------------------------------ window
        private bool _picking;
        public bool Visible => _win.Visible;
        public void Show() { _picking = false; _win.SetTabs(null, 0, null); _win.SetInput(false); _win.Open(_head()); Refresh(); }
        public void Hide() => _win.Hide();

        public void Refresh()
        {
            if (!_win.Visible) return;
            var rows = new List<ListWindow.Row>();
            _win.SetTitle(Lang.T("Swap set"));
            if (_picking)
            {
                _win.SetInfo(Lang.T("Equipment you carry (not worn)"));
                foreach (var e in _inv.Items.Values.Where(x => x.IsEquip && x.Wearing == 0 && !Set.ContainsKey(x.Index)))
                {
                    var ee = e;
                    rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(e.Info), Text = $"{Inventory.FullName(e)}  <color=#9aa3b5>{SlotName(e.Location)}</color>", A = Lang.T("Add"), OnA = () => { Add(ee); _picking = false; } });
                }
                if (rows.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>No spare equipment in your bag.</color>") });
                _win.SetFooter((Lang.T("Back"), () => { _picking = false; Refresh(); }), (Lang.T("Close"), Hide));
            }
            else
            {
                float cd = CooldownUntil - Time.time;
                _win.SetInfo(Set.Count == 0 ? Lang.T("The swap set is empty.") : (Set.Count > 1 ? Lang.F("{0} items ready to swap in", Set.Count) : Lang.T("1 item ready to swap in")) + (cd > 0 ? $" · {Mathf.CeilToInt(cd)} s" : ""));
                foreach (var kv in Set.OrderBy(k => k.Value))
                {
                    int idx = kv.Key; _inv.Items.TryGetValue(idx, out var e);
                    rows.Add(new ListWindow.Row
                    {
                        Icon = e != null ? WristMenu.ItemIcon(e.Info) : null,
                        Text = $"{(e != null ? Inventory.FullName(e) : Lang.F("Item #{0}", idx))}  <color=#9aa3b5>{SlotName(kv.Value)}</color>",
                        A = Lang.T("Swap in"), OnA = () => SwitchOne(idx), B = Lang.T("Remove"), OnB = () => Remove(idx),
                    });
                }
                rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>Add equipment from your bag</color>"), A = Lang.T("Choose"), OnA = () => { _picking = true; Refresh(); } });
                _win.SetFooter((Set.Count > 0 ? Lang.T("Swap all") : null, SwitchAll), (Lang.T("Close"), Hide));
            }
            _win.SetRows(rows);
        }
    }
}
