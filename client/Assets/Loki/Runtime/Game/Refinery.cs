using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Loki.IO;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Item improvement windows (research/refine_enchant_craft_packets.md; rAthena e985006, PACKETVER 20211103). All of them are opened by the
    /// server (an NPC or an @command) and most block walking / skills until their close packet, so every way out sends it.
    ///   Refine UI: 0x0aa0 open → pick 0x0aa1 index → 0x0aa2 (index, blessings needed, {itemId.L chance.B zeny.L}) → 0x0aa3 (index, material, blessing)
    ///     → 0x0188 (result 0 ok / 1 broke / 2 down / 3 failed, index, refine) + a fresh 0x0aa2; the item is NOT re-sent (patched here); close 0x0aa4.
    ///   Script refines only send 0x0188 + a full item re-send. 0x0ada (name, itemId, level, status) is the server-wide refine banner.
    ///   Upgrade Weapon: 0x0221 list (index.W itemId.L refine.B cards) → 0x0222 index.L (an invalid index cancels) → 0x0223 (result.L itemId.L).
    ///   Enchant grade: 0x0ae2 UIType 8 → 0x0b59 index → 0x0b5a (chance, catalyst, {item amount zeny downgrade breakable}) → 0x0b5b
    ///     (index, option, blessing flag, steps, 0) → 0x0b5d (index, grade, result: 0 up + refine reset to 0, 1 fail, 2 down, 3 destroyed); 0x0b5e banner; close 0x0b5c.
    ///   Enchant window: 0x0b9a type 10 + item_enchant Id (everything else comes from data/craft/item_enchant.json) → 0x0b9b random / 0x0b9c perfect /
    ///     0x0b9d upgrade / 0x0b9e reset → 0x0b9f (msg 3857 ok / 3858 fail, enchant id): one result per opening; close 0x0ba0.
    ///   Item reform: 0x0b8f reform item → 0x0b91 (reform item, index) → 0x07fa + 0x0b41 (new item) + 0x0b92 ok (failures are silent); close 0x0b90.
    /// </summary>
    public sealed class Refinery
    {
        public enum Mode { None, Refine, Upgrade, Grade, Enchant, Reform }
        public Mode State { get; private set; } = Mode.None;
        public event Action Changed;
        public int LastRefineResult = -1, LastGradeResult = -1, LastEnchantMsg = -1, LastUpgradeResult = -1; public bool LastReformOk;

        private readonly RoSession _s; private readonly Inventory _inv; private readonly ListWindow _win; private readonly Func<Camera> _head; private readonly Action<string, string> _chat;
        private int _sel = -1; private float _waitUntil; private string _waitMsg = ""; private float _upgradeAt = -10f;
        // refine UI
        private int _blessNeed; private readonly List<(int item, int chance, int zeny)> _mats = new List<(int, int, int)>(); private bool _useBless;
        // upgrade weapon
        private readonly List<(int idx, int item, int refine, int[] cards)> _weapons = new List<(int, int, int, int[])>();
        // enchant grade
        private int _gChance, _gCatalyst, _gCatAmount, _gCatMax, _gCatInc, _gSteps; private readonly List<(int item, int amount, int zeny, bool down, bool brk)> _gMats = new List<(int, int, int, bool, bool)>();
        // enchant window
        private ulong _enchantId; private Dictionary<string, object> _enchantTable; private int _pendingSlot = -1; private string _pendingKind = "";
        // reform
        private int _reformItem;

        public Refinery(RoSession s, Inventory inv, ListWindow win, Func<Camera> head, Action<string, string> chat)
        {
            _s = s; _inv = inv; _win = win; _head = head; _chat = chat;
            _win.OnCloseClicked = Close;
            _inv.Changed += () => { if (State != Mode.None && _win.Visible) Refresh(); };
        }

        public bool Open => State != Mode.None;
        /// <summary>Client wave 4: the zeny carried (the grade options' price: rAthena answers nothing when it is short, clif.cpp:24118-24121).</summary>
        public Func<long> Zeny;
        /// <summary>A warp closes these windows on the server (pc.cpp:6955, unit.cpp:3666).</summary>
        public void Reset() { State = Mode.None; _sel = -1; _win.Hide(); Changed?.Invoke(); }

        private void Show() { _win.SetInput(false); Refresh(); _win.Open(_head()); Changed?.Invoke(); }
        private int Have(int itemId) { int n = 0; foreach (var e in _inv.Items.Values) if (e.ItemId == itemId && e.Wearing == 0) n += e.Amount; return n; }
        private static string Name(int itemId) => GameDb.GetItem(itemId).Display;
        private static string Money(long z) => z.ToString("N0") + " z";
        private void Wait(string what) { _waitUntil = Time.time + 4f; _waitMsg = what; Refresh(); }
        private bool Waiting => Time.time < _waitUntil;

        /// <summary>Called every frame: a request the server refused silently stops waiting after 4 s.</summary>
        public void Tick()
        {
            if (_waitUntil > 0 && Time.time >= _waitUntil) { _waitUntil = 0; if (State != Mode.None) { _chat(_waitMsg.Length > 0 ? Lang.T(_waitMsg) : Lang.T("Nothing happened — the server refused that request."), "ffb070"); Refresh(); } }
        }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                // ---- refine
                case 0x0aa0: { State = Mode.Refine; _sel = -1; _mats.Clear(); _useBless = false; Debug.Log("[Refine] UI open"); Show(); return true; }
                case 0x0aa2:
                {
                    r.Skip(4); int idx = r.U16(); _blessNeed = r.U8(); _mats.Clear();
                    while (r.Remaining >= 9) _mats.Add(((int)r.U32(), r.U8(), r.S32()));
                    _sel = idx; _waitUntil = 0; if (_useBless && (_blessNeed == 0 || Have(6635) < _blessNeed)) _useBless = false;
                    Debug.Log($"[Refine] materials for {idx}: {_mats.Count}, blessings {_blessNeed}");
                    if (State == Mode.Refine) Refresh();
                    return true;
                }
                case 0x0188:
                {
                    r.Skip(2); int res = r.U16(); int idx = r.U16(); int val = r.U16();
                    LastRefineResult = res; _waitUntil = 0;
                    string name = _inv.Items.TryGetValue(idx, out var e) ? GameDb.GetItem(e.ItemId).Display : Lang.T("the item");
                    if (State == Mode.Refine && res != 1) _inv.Patch(idx, refine: val); // the refine UI never re-sends the item (clif.cpp:22765-22799)
                    // client wave 4 (w4items): Upgrade Weapon sends ZC_ACK_WEAPONREFINE 0x0223 and then this 0x0188 for the same weapon
                    // (skill_weaponrefine, skill.cpp:10662-10665 / 10691-10693): one message is enough
                    if (Time.time - _upgradeAt > 1f) _chat(res == 0 ? Lang.F("Refine success! {0} is now +{1}.", name, val) : res == 1 ? Lang.F("The refine failed and {0} was destroyed.", name) : res == 2 ? Lang.F("The refine failed; {0} dropped to +{1}.", name, val) : Lang.F("The refine failed, but {0} stays +{1}.", name, val), res == 0 ? "a8e6a3" : "ffb070");
                    Debug.Log($"[Refine] result {res} index {idx} level {val}");
                    if (res == 1 && State == Mode.Refine) { _sel = -1; _mats.Clear(); Refresh(); }
                    Changed?.Invoke();
                    return true;
                }
                case 0x0ada: { r.Skip(2); string who = r.Str(24); int item = (int)r.U32(); int lv = r.U8(); int ok = r.U8(); _chat(ok == 1 ? Lang.F("{0} refined {1} to +{2}!", who, Name(item), lv) : Lang.F("{0} failed to refine {1} to +{2}.", who, Name(item), lv + 1), "ffd060"); return true; }
                case 0x00ca: { if (State == Mode.Refine && Waiting) { _waitUntil = 0; Refresh(); } return false; } // not enough zeny: Shops prints it
                // ---- upgrade weapon
                case 0x0221:
                {
                    r.Skip(4); _weapons.Clear();
                    while (r.Remaining >= 23) { int idx = r.U16(); int item = (int)r.U32(); int rf = r.U8(); var cards = Inventory.ReadCards(ref r); _weapons.Add((idx, item, rf, cards)); }
                    Debug.Log($"[Refine] upgrade weapon list: {_weapons.Count}");
                    if (_weapons.Count == 0) { _chat(Lang.T("You have no weapon you can upgrade (it needs the right ore, and must not be held)."), "ffb070"); return true; }
                    State = Mode.Upgrade; Show();
                    return true;
                }
                case 0x0223:
                {
                    // client wave 4 (w4items): the client's own texts (msgstringtable 911 / 913 / 914, as rAthena's comment at clif.cpp:6980-6987
                    // names them); 912, the failure's text, reads "Weapons upgraded: %s" too, but the weapon broke and was deleted
                    // (skill.cpp:10688-10697), so Loki says so. On success the whole inventory is re-sent (clif_inventorylist, skill.cpp:10664).
                    r.Skip(2); int res = r.S32(); int item = (int)r.U32(); LastUpgradeResult = res; _upgradeAt = Time.time;
                    string nm = GameDb.GetItem(item).Name;
                    _chat(res == 0 ? ClientText.Fill(ClientText.Msg(911), nm) : res == 1 ? Lang.F("The upgrade failed and {0} broke.", nm) : res == 2 ? ClientText.Fill(ClientText.Msg(913), nm) : ClientText.Fill(ClientText.Msg(914), nm), res == 0 ? "a8e6a3" : "ffb070");
                    Debug.Log($"[Refine] upgrade weapon result {res} item {item}");
                    if (State == Mode.Upgrade) { State = Mode.None; _win.Hide(); }
                    Changed?.Invoke();
                    return true;
                }
                // ---- server-opened UIs
                case 0x0ae2:
                {
                    r.Skip(2); int type = r.U8(); int data = r.S32();
                    if (type == 0 || type == 7 || type == 1) return false; // the bank, the daily check-in (Bank.cs, CheckIn.cs) and the stylist (Stylist.cs, client wave 2)
                    if (type == 8) { State = Mode.Grade; _sel = -1; _gMats.Clear(); _gSteps = 0; Debug.Log("[Grade] UI open"); Show(); return true; }
                    // w4lang: the window's name is a translated word in the sentence ("the {0} window" / "a janela de {0}": no agreement needed)
                    string what = type == 0 ? Lang.T("~window~bank") : type == 1 ? Lang.T("~window~stylist") : type == 2 ? Lang.T("~window~captcha") : type == 3 ? Lang.T("~window~macro") : type == 5 ? Lang.T("~window~tip box") : type == 6 ? Lang.T("~window~quest") : type == 7 ? Lang.T("~window~attendance") : Lang.F("~window~type {0}", type);
                    _chat(Lang.F("The server opened the {0} window — Loki doesn't have it yet.", what), "ffb070");
                    Debug.Log($"[UI] open {type} data {data} (unsupported)");
                    return true;
                }
                case 0x0b5a:
                {
                    r.Skip(4); int idx = (short)r.U16(); _gChance = r.S32(); _gCatalyst = r.S32(); _gCatAmount = r.S32(); _gCatMax = r.S32(); _gCatInc = r.S32(); r.Skip(8);
                    _gMats.Clear(); while (r.Remaining >= 17) _gMats.Add(((int)r.U32(), r.S32(), r.S32(), r.S32() != 0, r.U8() != 0));
                    _waitUntil = 0;
                    if (idx < 0) { _chat(Lang.T("That item can't be graded."), "ffb070"); _sel = -1; }
                    else { _sel = idx; _gSteps = Mathf.Clamp(_gSteps, 0, _gCatMax); }
                    Debug.Log($"[Grade] materials for {idx}: chance {_gChance}%, {_gMats.Count} options, catalyst {_gCatalyst} x{_gCatAmount} up to {_gCatMax}");
                    if (State == Mode.Grade) Refresh();
                    return true;
                }
                case 0x0b5d:
                {
                    r.Skip(2); int idx = r.U16(); int grade = r.U16(); int res = r.S32(); LastGradeResult = res; _waitUntil = 0;
                    if (res == 0) _inv.Patch(idx, refine: 0, grade: grade); // grade +1 and refine back to 0, not re-sent (clif.cpp:24203-24205)
                    // result 2: the refine went down by the option's DowngradeAmount (clif.cpp:24220-24228); neither this packet nor the material
                    // list (only a "may lose refine" flag) says by how much, and the item is not re-sent — no option of rAthena's shipped
                    // db/re/enchantgrade.yml has a DowngradeAmount, so this never happens there. The refine shown stays the old one until the
                    // next inventory list (a map change).
                    _chat(res == 0 ? Lang.F("Grade up! The item is now grade {0} (its refine was reset to +0).", Inventory.GradeName(grade)) : res == 1 ? Lang.T("The grading failed; nothing changed.") : res == 2 ? Lang.T("The grading failed and the refine went down (the new level shows after your next map change).") : Lang.T("The grading failed and the item was destroyed."), res == 0 ? "a8e6a3" : "ffb070");
                    Debug.Log($"[Grade] result {res} index {idx} grade {grade}");
                    if (State == Mode.Grade)
                    {
                        if (res != 3 && _inv.Items.ContainsKey(idx)) { _s.SendPacket(new PacketWriter(0x0b59).U16((ushort)idx).ToArray()); Wait(""); }
                        else { _sel = -1; Refresh(); }
                    }
                    Changed?.Invoke();
                    return true;
                }
                case 0x0b5e: { r.Skip(2); string who = r.Str(24); int item = (int)r.U32(); int grade = r.U16(); int ok = r.U8(); _chat(ok == 1 ? Lang.F("{0} upgraded {1} to grade {2}!", who, Name(item), Inventory.GradeName(grade)) : Lang.F("{0} failed to grade {1}.", who, Name(item)), "ffd060"); return true; }
                // ---- enchant window
                case 0x0b9a:
                {
                    r.Skip(2); int type = r.U8(); ulong data = r.U64();
                    if (type != 10) { Debug.Log($"[UI] open v3 type {type} data {data} (unsupported)"); _chat(Lang.T("The server opened a window Loki doesn't have yet."), "ffb070"); return true; }
                    _enchantTable = EnchantTable((int)data);
                    if (_enchantTable == null) { _chat(Lang.F("Unknown enchant table {0}.", data), "ffb070"); _s.SendPacket(new PacketWriter(0x0ba0).ToArray()); return true; }
                    State = Mode.Enchant; _enchantId = data; _sel = -1; _pendingSlot = -1; Debug.Log($"[Enchant] UI open, table {data}"); Show();
                    return true;
                }
                case 0x0b9f:
                {
                    r.Skip(2); int msg = r.S32(); int item = (int)r.U32(); LastEnchantMsg = msg; _waitUntil = 0;
                    bool ok = msg == 3857;
                    if (ok && _sel >= 0 && _inv.Items.TryGetValue(_sel, out var e))
                    {
                        if (_pendingKind == "reset") { int slots = GameDb.GetItem(e.ItemId).Slots; for (int s2 = slots; s2 < 4; s2++) _inv.Patch(_sel, slot: s2, card: 0); }
                        else if (_pendingSlot >= 0) _inv.Patch(_sel, slot: _pendingSlot, card: item);
                    }
                    _chat(ok ? (_pendingKind == "reset" ? Lang.T("The enchantments were removed.") : Lang.F("Enchanted with {0}!", Name(item))) : Lang.T("The enchantment failed."), ok ? "a8e6a3" : "ffb070");
                    Debug.Log($"[Enchant] result msg {msg} item {item} ({_pendingKind} slot {_pendingSlot})");
                    // the server closes its side after every result (clif.cpp:24536): talk to the NPC again for another try
                    State = Mode.None; _win.Hide(); _pendingSlot = -1;
                    Changed?.Invoke();
                    return true;
                }
                // ---- reform
                case 0x0b8f: { r.Skip(2); _reformItem = (int)r.U32(); State = Mode.Reform; _sel = -1; Debug.Log($"[Reform] UI open with {_reformItem}"); Show(); return true; }
                case 0x0b92:
                {
                    r.Skip(2); int idx = r.U16(); int res = r.U8(); LastReformOk = res == 0; _waitUntil = 0;
                    _chat(res == 0 ? Lang.F("Reformed into {0}!", _inv.Items.TryGetValue(idx, out var e) ? Name(e.ItemId) : Lang.T("a new item")) : Lang.T("The reform failed."), res == 0 ? "a8e6a3" : "ffb070");
                    State = Mode.None; _win.Hide(); Changed?.Invoke(); // the server clears its state on success; the client closes the window
                    return true;
                }
                default: return false;
            }
        }

        // ---- data
        private static List<object> _enchantData; private static Dictionary<string, object> _reformData;
        private static Dictionary<string, object> EnchantTable(int id)
        {
            try
            {
                if (_enchantData == null) _enchantData = MiniJson.AsList(MiniJson.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "data", "craft", "item_enchant.json"))));
                foreach (var t in _enchantData) { var d = MiniJson.AsObject(t); if (d != null && MiniJson.Int(d["id"]) == id) return d; }
            }
            catch (Exception e) { Debug.LogError("[Enchant] item_enchant.json: " + e.Message); }
            return null;
        }
        private static List<object> ReformTable(int item)
        {
            try
            {
                if (_reformData == null) _reformData = MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "data", "craft", "item_reform.json"))));
                return _reformData.TryGetValue(item.ToString(), out var l) ? MiniJson.AsList(l) : null;
            }
            catch (Exception e) { Debug.LogError("[Reform] item_reform.json: " + e.Message); return null; }
        }
        private static List<(int item, int amount)> Mats(object o) { var l = new List<(int, int)>(); var list = MiniJson.AsList(o); if (list != null) foreach (var m in list) { var p = MiniJson.AsList(m); if (p != null && p.Count >= 2) l.Add((MiniJson.Int(p[0]), MiniJson.Int(p[1]))); } return l; }
        private string MatText(List<(int item, int amount)> mats, long zeny)
        {
            var parts = new List<string>();
            foreach (var m in mats) { int have = Have(m.item); parts.Add($"{Name(m.item)} ×{m.amount} " + (have >= m.amount ? Lang.F("<color=#9aa3b5>(have {0})</color>", have) : Lang.F("<color=#ff8a7a>(have {0})</color>", have))); }
            if (zeny > 0) parts.Add(Money(zeny));
            return parts.Count == 0 ? Lang.T("free") : string.Join(" + ", parts);
        }
        private bool CanPay(List<(int item, int amount)> mats) { foreach (var m in mats) if (Have(m.item) < m.amount) return false; return true; }

        // ---- sending
        public void RefineSelect(int idx) { if (State != Mode.Refine) return; _sel = idx; _mats.Clear(); _s.SendPacket(new PacketWriter(0x0aa1).U16((ushort)idx).ToArray()); Wait(Lang.N("That item can't be refined here.")); }
        public void RefineGo(int material)
        {
            if (State != Mode.Refine || _sel < 0 || Waiting) return;
            _s.SendPacket(new PacketWriter(0x0aa3).U16((ushort)_sel).U32((uint)material).U8((byte)(_useBless ? 1 : 0)).ToArray());
            Debug.Log($"[Refine] refine {_sel} with {material} blessing {_useBless}");
            Wait("");
        }
        public void UpgradeGo(int idx) { if (State != Mode.Upgrade) return; _s.SendPacket(new PacketWriter(0x0222).U32((uint)idx).ToArray()); Wait(""); }
        public void GradeSelect(int idx) { if (State != Mode.Grade) return; _sel = idx; _gMats.Clear(); _s.SendPacket(new PacketWriter(0x0b59).U16((ushort)idx).ToArray()); Wait(Lang.N("That item can't be graded.")); }
        public void GradeGo(int option)
        {
            if (State != Mode.Grade || _sel < 0 || Waiting) return;
            _s.SendPacket(new PacketWriter(0x0b5b).U16((ushort)_sel).U32((uint)option).U8((byte)(_gSteps > 0 ? 1 : 0)).U32((uint)_gSteps).U8(0).ToArray());
            Debug.Log($"[Grade] grade {_sel} option {option} catalyst steps {_gSteps}");
            Wait("");
        }
        private PacketWriter EnchantHeader(ushort id) => new PacketWriter(id).U32((uint)(_enchantId & 0xffffffff)).U32((uint)(_enchantId >> 32)).U16((ushort)_sel);
        public void EnchantRandom(int slot) { if (State != Mode.Enchant || _sel < 0 || Waiting) return; _pendingKind = "random"; _pendingSlot = slot; _s.SendPacket(EnchantHeader(0x0b9b).ToArray()); Wait(""); }
        public void EnchantPerfect(int slot, int item) { if (State != Mode.Enchant || _sel < 0 || Waiting) return; _pendingKind = "perfect"; _pendingSlot = slot; _s.SendPacket(EnchantHeader(0x0b9c).U32((uint)item).ToArray()); Wait(""); }
        public void EnchantUpgrade(int slot) { if (State != Mode.Enchant || _sel < 0 || Waiting) return; _pendingKind = "upgrade"; _pendingSlot = slot; _s.SendPacket(EnchantHeader(0x0b9d).U16((ushort)slot).ToArray()); Wait(""); }
        public void EnchantReset() { if (State != Mode.Enchant || _sel < 0 || Waiting) return; _pendingKind = "reset"; _pendingSlot = -1; _s.SendPacket(EnchantHeader(0x0b9e).ToArray()); Wait(""); }
        public void ReformGo(int idx) { if (State != Mode.Reform || Waiting) return; _sel = idx; _s.SendPacket(new PacketWriter(0x0b91).U32((uint)_reformItem).U16((ushort)idx).ToArray()); Wait(Lang.N("The reform didn't happen (check the item's refine, cards and the materials).")); }

        public void Close()
        {
            switch (State)
            {
                case Mode.Refine: _s.SendPacket(new PacketWriter(0x0aa4).ToArray()); break;
                case Mode.Upgrade: _s.SendPacket(new PacketWriter(0x0222).U32(0).ToArray()); break; // index 0 is invalid → the server clears the skill menu
                case Mode.Grade: _s.SendPacket(new PacketWriter(0x0b5c).ToArray()); break;
                case Mode.Enchant: _s.SendPacket(new PacketWriter(0x0ba0).ToArray()); break;
                case Mode.Reform: _s.SendPacket(new PacketWriter(0x0b90).ToArray()); break;
            }
            Debug.Log($"[Refine] close {State}");
            State = Mode.None; _sel = -1; _waitUntil = 0; _win.Hide(); Changed?.Invoke();
        }
        private void Back() { _sel = -1; Refresh(); }

        // ---- window
        private IEnumerable<Inventory.Entry> Equipment(Func<Inventory.Entry, bool> extra = null) => _inv.Items.Values.Where(e => e.IsEquip && e.Identified && !e.Damaged && (extra == null || extra(e))).OrderBy(e => e.Index);

        private void Refresh()
        {
            var rows = new List<ListWindow.Row>();
            string busy = Waiting ? "  " + Lang.T("<color=#ffd060>working…</color>") : "";
            switch (State)
            {
                case Mode.Refine:
                {
                    _win.SetTabs(null, 0, null);
                    if (_sel < 0 || !_inv.Items.TryGetValue(_sel, out var sel))
                    {
                        _win.SetTitle(Lang.T("~verb~Refine")); _win.SetInfo(Lang.T("Choose the equipment to refine.") + busy);
                        foreach (var e in Equipment()) { var e2 = e; rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(e.Info), Text = Inventory.FullName(e) + (e.Wearing != 0 ? "  " + Lang.T("<color=#9aa3b5>(worn)</color>") : ""), A = Lang.T("Choose"), OnA = () => RefineSelect(e2.Index), AEnabled = !Waiting }); }
                        if (rows.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>No identified, unbroken equipment in the bag.</color>") });
                        _win.SetFooter((Lang.T("Close"), Close));
                        break;
                    }
                    _win.SetTitle(Lang.F("Refine  {0}", Inventory.FullName(sel))); _win.SetInfo((_mats.Count == 0 ? (Waiting ? "" : Lang.T("It can't be refined any further here.")) : Lang.T("Pick the material to use.")) + busy);
                    foreach (var m in _mats)
                    {
                        int have = Have(m.item); var m2 = m;
                        rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(GameDb.GetItem(m.item)), Text = $"{Name(m.item)}  <color=#ffd060>{m.chance}%</color> · {Money(m.zeny)}  " + (have > 0 ? Lang.F("<color=#9aa3b5>(have {0})</color>", have) : Lang.F("<color=#ff8a7a>(have {0})</color>", have)), A = Lang.T("~verb~Refine"), OnA = () => RefineGo(m2.item), AEnabled = have > 0 && !Waiting });
                    }
                    if (_blessNeed > 0)
                    {
                        int have = Have(6635);
                        rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(GameDb.GetItem(6635)), Text = Lang.F("Blacksmith Blessing ×{0} — a failure keeps the item", _blessNeed) + "  " + (have >= _blessNeed ? Lang.F("<color=#9aa3b5>(have {0})</color>", have) : Lang.F("<color=#ff8a7a>(have {0})</color>", have)), A = _useBless ? Lang.T("Using") : Lang.T("Use"), OnA = () => { _useBless = !_useBless; Refresh(); }, AEnabled = have >= _blessNeed, Highlight = _useBless });
                    }
                    _win.SetFooter((Lang.T("Close"), Close), (Lang.T("Back"), Back));
                    break;
                }
                case Mode.Upgrade:
                {
                    _win.SetTabs(null, 0, null); _win.SetTitle(Lang.T("Upgrade Weapon")); _win.SetInfo(Lang.T("Choose the weapon to upgrade (uses one ore).") + busy);
                    foreach (var w in _weapons) { var w2 = w; rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(GameDb.GetItem(w.item)), Text = $"+{w.refine} {Name(w.item)}", A = Lang.T("Upgrade"), OnA = () => UpgradeGo(w2.idx), AEnabled = !Waiting }); }
                    _win.SetFooter((Lang.T("Cancel"), Close));
                    break;
                }
                case Mode.Grade:
                {
                    _win.SetTabs(null, 0, null);
                    if (_sel < 0 || !_inv.Items.TryGetValue(_sel, out var sel))
                    {
                        _win.SetTitle(Lang.T("Enchant Grade")); _win.SetInfo(Lang.T("Choose the equipment to grade (level 5 weapons, level 2 armor).") + busy);
                        foreach (var e in Equipment()) { var e2 = e; rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(e.Info), Text = Inventory.FullName(e), A = Lang.T("Choose"), OnA = () => GradeSelect(e2.Index), AEnabled = !Waiting }); }
                        if (rows.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>No identified equipment in the bag.</color>") });
                        _win.SetFooter((Lang.T("Close"), Close));
                        break;
                    }
                    int chance = _gChance + _gSteps * _gCatInc;
                    _win.SetTitle(Lang.F("Grade  {0}", Inventory.FullName(sel))); _win.SetInfo(Lang.F("Chance <color=#ffd060>{0}%</color> · success resets the refine to +0", chance) + busy);
                    if (_gCatalyst > 0 && _gCatMax > 0)
                    {
                        int have = Have(_gCatalyst);
                        rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(GameDb.GetItem(_gCatalyst)), Text = Lang.F("{0} ×{1}  (+{2}%, up to {3} steps)  <color=#9aa3b5>have {4}</color>", Name(_gCatalyst), _gSteps * _gCatAmount, _gSteps * _gCatInc, _gCatMax, have),
                            A = "+", OnA = () => { _gSteps = Mathf.Min(_gSteps + 1, _gCatMax); Refresh(); }, AEnabled = _gSteps < _gCatMax && have >= (_gSteps + 1) * _gCatAmount, B = "−", OnB = () => { _gSteps = Mathf.Max(0, _gSteps - 1); Refresh(); }, BEnabled = _gSteps > 0 });
                    }
                    for (int i = 0; i < _gMats.Count; i++)
                    {
                        var m = _gMats[i]; int opt = i; int have = Have(m.item); bool rich = Zeny == null || Zeny() >= m.zeny;
                        rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(GameDb.GetItem(m.item)), Text = $"{Name(m.item)} ×{m.amount} + {(rich ? Money(m.zeny) : "<color=#ff8a7a>" + Money(m.zeny) + "</color>")}{(m.brk ? "  " + Lang.T("<color=#ff8a7a>may break</color>") : "")}{(m.down ? "  " + Lang.T("<color=#ffb070>may lose refine</color>") : "")}  {Lang.F("<color=#9aa3b5>(have {0})</color>", have)}", A = Lang.T("Grade up"), OnA = () => GradeGo(opt), AEnabled = have >= m.amount && rich && !Waiting });
                    }
                    _win.SetFooter((Lang.T("Close"), Close), (Lang.T("Back"), Back));
                    break;
                }
                case Mode.Enchant: RefreshEnchant(rows, busy); break;
                case Mode.Reform:
                {
                    _win.SetTabs(null, 0, null); _win.SetTitle(Lang.F("Reform with {0}", Name(_reformItem))); _win.SetInfo(Lang.T("Choose the item to reform (it must not be worn).") + busy);
                    var table = ReformTable(_reformItem);
                    if (table != null)
                        foreach (var t in table)
                        {
                            var d = MiniJson.AsObject(t); if (d == null) continue;
                            int bas = MiniJson.Int(d["base"]), minR = MiniJson.Int(d["minRefine"]), maxR = MiniJson.Int(d["maxRefine"]), res = MiniJson.Int(d["result"]), chg = MiniJson.Int(d["changeRefine"]);
                            bool cards = MiniJson.Bool(d["cardsAllowed"], true); var mats = Mats(d["mats"]);
                            foreach (var e in _inv.Items.Values.Where(x => x.ItemId == bas && x.Wearing == 0))
                            {
                                bool ok = e.Refine >= minR && e.Refine <= maxR && (cards || e.Cards.Take(GameDb.GetItem(bas).Slots).All(c => c == 0)) && CanPay(mats); var e2 = e;
                                rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(e.Info), Text = $"{Inventory.FullName(e)} → {Name(res)}{(chg != 0 ? " " + (chg > 0 ? Lang.F("(+{0} refine)", chg) : Lang.F("({0} refine)", chg)) : "")}{(MiniJson.Bool(d["clearSlots"]) ? " · " + Lang.T("clears enchants") : "")}\n<size=19>{MatText(mats, 0)}{(e.Refine < minR || e.Refine > maxR ? " · " + Lang.F("needs +{0}..+{1}", minR, maxR) : "")}</size>", A = Lang.T("Reform"), OnA = () => ReformGo(e2.Index), AEnabled = ok && !Waiting });
                            }
                        }
                    if (rows.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>You carry nothing this can reform.</color>") });
                    _win.SetFooter((Lang.T("Close"), Close));
                    break;
                }
                default: return;
            }
            _win.SetRows(rows);
        }

        private int NextEnchantSlot(Inventory.Entry e)
        {
            var order = MiniJson.AsList(_enchantTable["order"]); int cardSlots = GameDb.GetItem(e.ItemId).Slots;
            if (order == null) return -1;
            foreach (var o in order) { int s = MiniJson.Int(o); if (s >= cardSlots && s < 4 && e.Cards[s] == 0) return s; }
            return -1;
        }

        private void RefreshEnchant(List<ListWindow.Row> rows, string busy)
        {
            _win.SetTabs(null, 0, null);
            var targets = new HashSet<int>(); var tl = MiniJson.AsList(_enchantTable["targets"]); if (tl != null) foreach (var t in tl) targets.Add(MiniJson.Int(t));
            int minRefine = MiniJson.Int(_enchantTable["minRefine"]), minGrade = MiniJson.Int(_enchantTable["minGrade"]);
            var slots = MiniJson.AsObject(_enchantTable["slots"]);
            if (_sel < 0 || !_inv.Items.TryGetValue(_sel, out var sel))
            {
                _win.SetTitle(Lang.T("Enchant")); _win.SetInfo((minRefine > 0 ? Lang.F("Choose the equipment to enchant (at least +{0}). It must not be worn.", minRefine) : Lang.T("Choose the equipment to enchant. It must not be worn.")) + busy);
                foreach (var e in _inv.Items.Values.Where(x => targets.Contains(x.ItemId)).OrderBy(x => x.Index))
                {
                    var e2 = e; bool ok = e.Wearing == 0 && e.Refine >= minRefine && e.Grade >= minGrade && e.Identified && !e.Damaged;
                    rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(e.Info), Text = Inventory.FullName(e) + (e.Wearing != 0 ? "  " + Lang.T("<color=#ff8a7a>(take it off first)</color>") : e.Refine < minRefine ? "  " + Lang.F("<color=#ff8a7a>(needs +{0})</color>", minRefine) : ""), A = Lang.T("Choose"), OnA = () => { _sel = e2.Index; Refresh(); }, AEnabled = ok });
                }
                if (rows.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>You carry nothing this enchanter works on.</color>") });
                _win.SetFooter((Lang.T("Close"), Close));
                return;
            }
            _win.SetTitle(Lang.F("Enchant  {0}", Inventory.FullName(sel)));
            int next = NextEnchantSlot(sel);
            _win.SetInfo((next >= 0 ? Lang.F("Next slot: {0}.", next + 1) : Lang.T("All enchant slots are used.")) + " " + Lang.T("One enchant per visit.") + busy);
            if (next >= 0 && slots != null && slots.TryGetValue(next.ToString(), out var so))
            {
                var s = MiniJson.AsObject(so);
                var mats = Mats(s["mats"]); long price = MiniJson.Int(s["price"]); int chance = MiniJson.Int(s["chance"], 100000);
                var gb = MiniJson.AsList(s["gradeBonus"]); if (gb != null) foreach (var g in gb) { var p = MiniJson.AsList(g); if (p != null && p.Count > 1) chance += MiniJson.Int(p[1]); } // every bonus counts (clif.cpp:24666-24670)
                var pool = MiniJson.AsObject(s["enchants"]); var list = pool != null && pool.TryGetValue(sel.Grade.ToString(), out var pl) ? MiniJson.AsList(pl) : null;
                if (list != null && list.Count > 0)
                {
                    int slot = next;
                    rows.Add(new ListWindow.Row { Text = Lang.F("Random enchant — <color=#ffd060>{0:0.#}%</color>, one of {1}", Mathf.Min(100, chance / 1000f), list.Count) + $"\n<size=19>{MatText(mats, price)}</size>", A = Lang.T("Enchant"), OnA = () => EnchantRandom(slot), AEnabled = CanPay(mats) && !Waiting, Highlight = true });
                }
                var perfect = MiniJson.AsList(s["perfect"]);
                if (perfect != null)
                    foreach (var pe in perfect)
                    {
                        var p = MiniJson.AsObject(pe); int item = MiniJson.Int(p["item"]); var pm = Mats(p["mats"]); int slot = next;
                        rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(GameDb.GetItem(item)), Text = Lang.F("{0} (sure)", Name(item)) + $"\n<size=19>{MatText(pm, MiniJson.Int(p["price"]))}</size>", A = Lang.T("Choose"), OnA = () => EnchantPerfect(slot, item), AEnabled = CanPay(pm) && !Waiting });
                    }
            }
            if (slots != null)
                for (int sl = 0; sl < 4; sl++)
                {
                    int card = sel.Cards[sl]; if (card == 0 || !slots.TryGetValue(sl.ToString(), out var so2)) continue;
                    var ups = MiniJson.AsList(MiniJson.AsObject(so2)["upgrades"]); if (ups == null) continue;
                    foreach (var ue in ups)
                    {
                        var u = MiniJson.AsObject(ue); if (MiniJson.Int(u["enchant"]) != card) continue;
                        int up = MiniJson.Int(u["upgrade"]); var um = Mats(u["mats"]); int slot = sl;
                        rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(GameDb.GetItem(up)), Text = Lang.F("Upgrade {0} → {1}", Name(card), Name(up)) + $"\n<size=19>{MatText(um, MiniJson.Int(u["price"]))}</size>", A = Lang.T("Upgrade"), OnA = () => EnchantUpgrade(slot), AEnabled = CanPay(um) && !Waiting });
                    }
                }
            if (_enchantTable.TryGetValue("reset", out var ro) && ro != null)
            {
                var rs = MiniJson.AsObject(ro); int cardSlots = GameDb.GetItem(sel.ItemId).Slots; bool any = false;
                for (int sl = cardSlots; sl < 4; sl++) if (sel.Cards[sl] != 0) any = true;
                if (any) { var rm = Mats(rs["mats"]); rows.Add(new ListWindow.Row { Text = Lang.F("Remove all enchants — {0:0.#}%", MiniJson.Int(rs["chance"]) / 1000f) + $"\n<size=19>{MatText(rm, MiniJson.Int(rs["price"]))}</size>", A = Lang.T("~enchant~Reset"), OnA = EnchantReset, AEnabled = CanPay(rm) && !Waiting }); }
            }
            if (rows.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>Nothing can be done to this item here.</color>") });
            _win.SetFooter((Lang.T("Close"), Close), (Lang.T("Back"), Back));
        }

        public int EnchantNextSlot => State == Mode.Enchant && _sel >= 0 && _inv.Items.TryGetValue(_sel, out var e) ? NextEnchantSlot(e) : -1;
        /// <summary>For the smoke test.</summary>
        public int MaterialCount => State == Mode.Refine ? _mats.Count : State == Mode.Grade ? _gMats.Count : 0;
        public int FirstMaterial => _mats.Count > 0 ? _mats[0].item : 0;
        /// <summary>Client wave 4 smoke: the grade window's chance / catalyst (0x0b5a) and the Upgrade Weapon list (0x0221).</summary>
        public int GradeChance => _gChance; public int GradeCatalyst => _gCatalyst;
        public List<int> UpgradeList => _weapons.Select(w => w.idx).ToList();
    }
}
