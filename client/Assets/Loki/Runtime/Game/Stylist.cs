using System;
using System.Collections.Generic;
using System.IO;
using Loki.IO;
using Loki.Net;
using Loki.Sprites;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// The stylist (client wave 2; rAthena e985006 clif.cpp:22847-23017, npc.cpp:126-390, research/stylist_laphine_merge_mail_marriage.md §A):
    ///   ZC_UI_OPEN 0x0ae2 type 1 opens it (openstylist / @stylist; the player can't act while it is open) → CZ_REQ_STYLE_CHANGE2 0x0afc
    ///   headPalette.W headStyle.W bodyPalette.W top.W mid.W bottom.W bodyStyle.W — each a stylist.yml option *index*, 0 = unchanged (never
    ///   0x0a46: the handler reads the 16-byte struct) → ZC_STYLE_CHANGE_RES 0x0a47 flag.B (0 ok and closed, 1 failed and still open).
    ///   The server buys the looks one by one in that order and stops at the first it can't pay (earlier ones stay bought); hair and
    ///   colours change at once (0x01d7 to the area), headgear comes by RODEX mail; a body style only applies to 3rd jobs.
    ///   Close: CZ_REQ_STYLE_CLOSE 0x0a48. The server sends no list: data/account/stylist.json is made from db/re/stylist.yml.
    /// The chosen hair style and colours are previewed on our own character until bought or the window closes.
    /// </summary>
    public sealed class Stylist
    {
        public sealed class Option { public int Index, Value; public (int price, int item, int box)? Human, Doram; }
        private readonly Dictionary<string, List<Option>> _db = new Dictionary<string, List<Option>>();
        private static readonly string[] Looks = { "Hair", "Hair_Color", "Clothes_Color", "Head_Top", "Head_Mid", "Head_Bottom", "Body2" };
        private static readonly string[] LookNames = { Lang.N("Hair style"), Lang.N("Hair colour"), Lang.N("Clothes colour"), Lang.N("Upper headgear"), Lang.N("Middle headgear"), Lang.N("Lower headgear"), Lang.N("Body style") }; // w4lang: shown with Lang.T
        private readonly Dictionary<string, int> _pick = new Dictionary<string, int>(); // look → option index
        public bool IsOpen { get; private set; }
        public int LastResult = -1;
        private int _tab;
        private readonly RoSession _s; private readonly ListWindow _win; private readonly Func<Camera> _head; private readonly Action<string, string> _chat;
        private readonly EntityManager _ents; private readonly Inventory _inv; private readonly Func<long> _zeny; private readonly Func<int> _job;

        public Stylist(RoSession s, EntityManager ents, Inventory inv, ListWindow win, Func<Camera> head, Func<long> zeny, Func<int> job, Action<string, string> chat)
        {
            _s = s; _ents = ents; _inv = inv; _win = win; _head = head; _zeny = zeny; _job = job; _chat = chat;
            _win.OnCloseClicked = Close;
            try
            {
                var o = MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "data", "account", "stylist.json"))));
                foreach (var kv in o)
                {
                    var l = new List<Option>();
                    foreach (var e in MiniJson.AsList(kv.Value))
                    {
                        var d = MiniJson.AsObject(e);
                        (int, int, int)? Cost(object c) { var cd = MiniJson.AsObject(c); return cd == null ? ((int, int, int)?)null : (MiniJson.Int(cd["price"]), MiniJson.Int(cd["item"]), MiniJson.Int(cd["box"])); }
                        l.Add(new Option { Index = MiniJson.Int(d["index"]), Value = MiniJson.Int(d["value"]), Human = Cost(d["human"]), Doram = Cost(d["doram"]) });
                    }
                    _db[kv.Key] = l;
                }
            }
            catch (Exception e) { Debug.LogWarning("[Stylist] data/account/stylist.json: " + e.Message); }
        }

        public void Reset() { if (IsOpen) RestorePreview(); IsOpen = false; _pick.Clear(); _win.Hide(); }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x0ae2:
                {
                    r.Skip(2); int type = r.U8(); if (type != 1) return false;
                    IsOpen = true; _pick.Clear(); _tab = 0;
                    Debug.Log($"[Stylist] open ({_db.Count} looks in the table)");
                    _win.Open(_head()); Refresh(); return true;
                }
                case 0x0a47:
                {
                    r.Skip(2); LastResult = r.U8();
                    Debug.Log($"[Stylist] result {LastResult}");
                    if (LastResult == 0) { _chat(Lang.T("Your new look is ready. Headgear comes by mail."), "9fe39a"); IsOpen = false; _pick.Clear(); _win.Hide(); }
                    else { _chat(Lang.T("The stylist couldn't do all of that (zeny, coupon or job) — what was paid for stays."), "ffb070"); RestorePreview(); Refresh(); }
                    return true;
                }
                default: return false;
            }
        }

        private bool Doram => RoNames.IsDoram(_job());
        private (int price, int item, int box)? CostOf(Option op) => Doram ? op.Doram : op.Human;
        private bool Afford(Option op)
        {
            var c = CostOf(op); if (c == null) return false;
            if (c.Value.price > _zeny()) return false;
            if (c.Value.item != 0 && _inv.Count(c.Value.item) == 0 && (c.Value.box == 0 || _inv.Count(c.Value.box) == 0)) return false;
            return true;
        }
        private static string CostText((int price, int item, int box)? c)
        {
            if (c == null) return Lang.T("<color=#ff8a7a>not for your race</color>");
            var parts = new List<string>();
            if (c.Value.price > 0) parts.Add($"{c.Value.price:N0} z");
            if (c.Value.item != 0) parts.Add(GameDb.GetItem(c.Value.item).Name + (c.Value.box != 0 && c.Value.box != c.Value.item ? " " + Lang.T("(or its box)") : ""));
            return parts.Count == 0 ? Lang.T("free") : string.Join(" + ", parts);
        }

        public void Choose(string look, int index) { if (_pick.TryGetValue(look, out int cur) && cur == index) _pick.Remove(look); else _pick[look] = index; Preview(); Refresh(); }

        public void Buy()
        {
            if (_pick.Count == 0) return;
            short V(string look) => (short)(_pick.TryGetValue(look, out int i) ? i : 0);
            _s.SendPacket(new PacketWriter(0x0afc).S16(V("Hair_Color")).S16(V("Hair")).S16(V("Clothes_Color")).S16(V("Head_Top")).S16(V("Head_Mid")).S16(V("Head_Bottom")).S16(V("Body2")).ToArray());
            Debug.Log($"[Stylist] buy {string.Join(", ", new List<string>(_pick.Keys).ConvertAll(k => k + " #" + _pick[k]))}");
        }

        public void Close()
        {
            if (IsOpen) { _s.SendPacket(new PacketWriter(0x0a48).ToArray()); Debug.Log("[Stylist] close"); }
            RestorePreview(); IsOpen = false; _pick.Clear(); _win.Hide();
        }

        // ---- preview on our own character (hair style / colours only; headgear arrives by mail)
        private void Preview()
        {
            var self = _ents.Self; var baseLook = _ents.SelfLook; if (self == null || baseLook == null) return;
            var look = baseLook.Clone();
            Option Pick(string k) { if (!_pick.TryGetValue(k, out int i) || !_db.TryGetValue(k, out var l)) return null; return l.Find(x => x.Index == i); }
            var h = Pick("Hair"); if (h != null) look.Hair = h.Value;
            var hc = Pick("Hair_Color"); if (hc != null) look.HairColor = hc.Value;
            var cc = Pick("Clothes_Color"); if (cc != null) look.ClothesColor = cc.Value;
            self.SetLook(look, LokiBootstrap.Instance.Assets);
        }
        private void RestorePreview() { var self = _ents.Self; var baseLook = _ents.SelfLook; if (self != null && baseLook != null && !self.Look.SameAs(baseLook)) self.SetLook(baseLook, LokiBootstrap.Instance.Assets); }

        public void Refresh()
        {
            if (!_win.Visible) return;
            // the server applies a body style to 3rd jobs only (clif.cpp:23002-23006) and ignores it for the others
            var tabs = new List<(string name, string[] looks)> { (Lang.T("Hair"), new[] { "Hair" }), (Lang.T("Hair colour"), new[] { "Hair_Color" }), (Lang.T("Clothes"), new[] { "Clothes_Color" }), (Lang.T("Headgear"), new[] { "Head_Top", "Head_Mid", "Head_Bottom" }) };
            if (_db.ContainsKey("Body2")) tabs.Add((Lang.T("Body (3rd)"), new[] { "Body2" }));
            _tab = Mathf.Clamp(_tab, 0, tabs.Count - 1);
            _win.SetTabs(tabs.ConvertAll(t => t.name).ToArray(), _tab, t => { _tab = t; Refresh(); });
            _win.SetTitle(Lang.T("Stylist")); _win.SetInput(false);
            long total = 0; foreach (var kv in _pick) if (_db.TryGetValue(kv.Key, out var l)) { var op = l.Find(x => x.Index == kv.Value); var c = op != null ? CostOf(op) : null; if (c != null) total += c.Value.price; }
            _win.SetInfo((_pick.Count == 0 ? Lang.T("Choose a look (it shows on you).") : _pick.Count > 1 ? Lang.F("{0} changes · {1:N0} z", _pick.Count, total) : Lang.F("1 change · {0:N0} z", total)) + "  ·  " + Lang.F("you have {0:N0} z", _zeny()));
            var rows = new List<ListWindow.Row>();
            foreach (var look in tabs[_tab].looks)
            {
                if (!_db.TryGetValue(look, out var list)) continue;
                int li = Array.IndexOf(Looks, look);
                if (tabs[_tab].looks.Length > 1) rows.Add(new ListWindow.Row { Text = $"<color=#ffd060>{Lang.T(LookNames[li])}</color>" });
                foreach (var op in list)
                {
                    var o2 = op; string lk = look; bool on = _pick.TryGetValue(look, out int sel) && sel == op.Index;
                    string what = look.StartsWith("Head_") ? GameDb.GetItem(op.Value).Name : look == "Body2" ? (op.Value == 0 ? Lang.T("Normal outfit") : Lang.T("Alternative outfit")) : look == "Hair" ? Lang.F("Style {0}", op.Value) : Lang.F("Colour {0}", op.Value);
                    var icon = look.StartsWith("Head_") ? WristMenu.ItemIcon(GameDb.GetItem(op.Value)) : null;
                    rows.Add(new ListWindow.Row { Icon = icon, Highlight = on, Text = $"{(on ? "<color=#7fd67f>✓</color> " : "")}{what}\n<size=19><color=#9aa3b5>{CostText(CostOf(op))}</color></size>", A = on ? Lang.T("Undo") : Lang.T("Choose"), OnA = () => Choose(lk, o2.Index), AEnabled = on || CostOf(op) != null });
                }
            }
            bool canPay = total <= _zeny();
            _win.SetFooter((Lang.T("Close"), Close), (_pick.Count > 0 ? Lang.T("Clear") : null, () => { _pick.Clear(); RestorePreview(); Refresh(); }), (_pick.Count > 0 && canPay ? Lang.T("Buy") : null, Buy));
            _win.SetRows(rows);
        }

        // ---- smoke hooks
        public int OptionCount(string look) => _db.TryGetValue(look, out var l) ? l.Count : 0;
        public bool CanAfford(string look, int index) { return _db.TryGetValue(look, out var l) && l.Find(x => x.Index == index) is Option op && Afford(op); }
    }
}
