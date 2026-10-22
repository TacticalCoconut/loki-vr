using System;
using System.Collections.Generic;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Pick-one menus the server opens (research/menus_items_msgs.md; rAthena e985006, PACKETVER 20211103):
    ///   0x0abe ZC_WARPLIST len.W skillId.W {map[16]} (Warp Portal, Teleport lv2+, Odin's Recall; names come with ".gat",
    ///          Teleport's random option as "Random.gat") → 0x011b skillId.W map[16], "cancel" cancels. While the menu is
    ///          unanswered every target/ground skill is refused (menuskill_id), so every way out answers it.
    ///   0x0177 ZC_ITEMIDENTIFY_LIST len.W {inventory index.W} → 0x0178 index.W (0xFFFF cancels) → 0x0179 index.W result.B.
    ///   0x017b ZC_ITEMCOMPOSITION_LIST len.W {equipment index.W} (after the client sent 0x017a for a card) → 0x017c card.W equip.W
    ///          → 0x017d equip.W card.W result.B. Nothing is locked server-side; cancel = just close.
    /// </summary>
    public sealed class Menus
    {
        private enum Kind { None, Warp, Identify, Card }
        private readonly RoSession _s; private readonly Inventory _inv; private readonly ListWindow _win; private readonly Func<Camera> _head; private readonly Action<string, string> _chat;
        private Kind _kind; private int _skillId; private int _cardIndex;
        private readonly List<string> _maps = new List<string>();
        private readonly List<int> _indices = new List<int>();
        public bool IsOpen => _kind != Kind.None;

        public Menus(RoSession s, Inventory inv, ListWindow win, Func<Camera> head, Action<string, string> chat)
        {
            _s = s; _inv = inv; _win = win; _head = head; _chat = chat;
            _win.OnCloseClicked = Cancel;
        }

        /// <summary>A map change clears menuskill on the server (unit.cpp:3666).</summary>
        public void Reset() { _kind = Kind.None; _win.Hide(); }

        /// <summary>The player chose a card to use (Items tab): ask the server which equipment can take it.</summary>
        public void UseCard(int cardIndex)
        {
            _cardIndex = cardIndex;
            _s.CardList(cardIndex);
            Debug.Log($"[Menus] card list requested for index {cardIndex}");
        }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x0abe:
                {
                    r.Skip(4); _skillId = r.U16(); _maps.Clear();
                    while (r.Remaining >= 16) { string m = r.Str(16); if (m.Length > 0) _maps.Add(m); }
                    _kind = Kind.Warp; Refresh(); _win.Open(_head());
                    Debug.Log($"[Menus] warp list for skill {_skillId}: {string.Join(", ", _maps)}");
                    return true;
                }
                case 0x0177:
                {
                    r.Skip(4); _indices.Clear();
                    while (r.Remaining >= 2) _indices.Add(r.U16());
                    _kind = Kind.Identify; Refresh(); _win.Open(_head());
                    Debug.Log($"[Menus] identify list: {_indices.Count} items");
                    return true;
                }
                case 0x0179:
                {
                    r.Skip(2); int idx = r.U16(); int res = r.U8();
                    if (res == 0) _inv.MarkIdentified(idx);
                    _chat(ClientText.Msg(res == 0 ? 491 : 492), res == 0 ? "9fe39a" : "ffb070"); // MSI_ITEM_IDENTIFY_SUCCEESS / _FAIL
                    Debug.Log($"[Menus] identify result {res} for {idx}");
                    return true;
                }
                case 0x017b:
                {
                    r.Skip(4); _indices.Clear();
                    while (r.Remaining >= 2) _indices.Add(r.U16());
                    _kind = Kind.Card; Refresh(); _win.Open(_head());
                    Debug.Log($"[Menus] card targets: {_indices.Count}");
                    return true;
                }
                case 0x017d:
                {
                    r.Skip(2); int equip = r.U16(); int card = r.U16(); int res = r.U8();
                    if (res == 0) _inv.ApplyCardInsert(equip, card);
                    _chat(res == 0 ? Lang.T("The card was inserted.") : Lang.T("The card couldn't be inserted."), res == 0 ? "9fe39a" : "ffb070");
                    Debug.Log($"[Menus] card insert result {res} ({card} → {equip})");
                    return true;
                }
                default: return false;
            }
        }

        private void Cancel()
        {
            switch (_kind)
            {
                case Kind.Warp: _s.SelectWarpPoint(_skillId, "cancel"); break;
                case Kind.Identify: _s.Identify(0xFFFF); break;
                case Kind.Card: break; // no server state (clif_use_card sets none)
            }
            Debug.Log($"[Menus] cancelled {_kind}");
            _kind = Kind.None; _win.Hide();
        }

        private void Pick(int i)
        {
            switch (_kind)
            {
                case Kind.Warp: if (i < _maps.Count) { _s.SelectWarpPoint(_skillId, _maps[i]); Debug.Log($"[Menus] warp → {_maps[i]}"); } break;
                case Kind.Identify: if (i < _indices.Count) _s.Identify(_indices[i]); break;
                case Kind.Card: if (i < _indices.Count) _s.InsertCard(_cardIndex, _indices[i]); break;
            }
            _kind = Kind.None; _win.Hide();
        }

        public int OptionCount => _kind == Kind.Warp ? _maps.Count : _indices.Count;

        private void Refresh()
        {
            var rows = new List<ListWindow.Row>();
            switch (_kind)
            {
                case Kind.Warp:
                {
                    for (int i = 0; i < _maps.Count; i++)
                    {
                        int k = i; string m = _maps[i]; string bare = m.EndsWith(".gat", StringComparison.OrdinalIgnoreCase) ? m.Substring(0, m.Length - 4) : m;
                        string label = bare.Equals("Random", StringComparison.OrdinalIgnoreCase) ? Lang.T("Random spot on this map") : $"{ClientText.MapName(bare)}  <color=#9aa3b5>({bare})</color>";
                        rows.Add(new ListWindow.Row { Text = label, A = Lang.T("Go"), OnA = () => Pick(k) });
                    }
                    string title = _skillId == 27 ? Lang.T("Warp Portal") : _skillId == 26 ? Lang.T("Teleport") : Lang.T("Choose a destination");
                    _win.SetTitle(title); _win.SetInfo(Lang.T("Choose where to go."));
                    break;
                }
                case Kind.Identify:
                {
                    for (int i = 0; i < _indices.Count; i++)
                    {
                        int k = i; _inv.Items.TryGetValue(_indices[i], out var e);
                        var info = GameDb.GetItem(e?.ItemId ?? 0);
                        rows.Add(new ListWindow.Row { Icon = e != null ? WristMenu.ItemIcon(info) : null, Text = e != null ? info.Display : Lang.F("item #{0}", _indices[i]), A = Lang.T("Appraise"), OnA = () => Pick(k) });
                    }
                    _win.SetTitle(Lang.T("Item Appraisal")); _win.SetInfo(Lang.T("Choose an unidentified item."));
                    break;
                }
                case Kind.Card:
                {
                    _inv.Items.TryGetValue(_cardIndex, out var card);
                    for (int i = 0; i < _indices.Count; i++)
                    {
                        int k = i; _inv.Items.TryGetValue(_indices[i], out var e);
                        var info = GameDb.GetItem(e?.ItemId ?? 0);
                        rows.Add(new ListWindow.Row { Icon = e != null ? WristMenu.ItemIcon(info) : null, Text = e != null ? Inventory.FullName(e) : Lang.F("item #{0}", _indices[i]), A = Lang.T("Insert"), OnA = () => Pick(k) });
                    }
                    _win.SetTitle(Lang.T("Insert Card")); _win.SetInfo(card != null ? Lang.F("{0} → choose the equipment (cards can't be removed).", card.Info.Display) : Lang.T("Choose the equipment."));
                    break;
                }
                default: return;
            }
            _win.SetTabs(null, 0, null);
            _win.SetFooter((Lang.T("Cancel"), Cancel));
            _win.SetRows(rows, true);
        }
    }
}
