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
    /// Crafting lists (research/refine_enchant_craft_packets.md §2.12, §3.8). The server sends product ids only; recipes come from
    /// data/craft/produce.json (db/re/produce_db.txt) and arrows.json (db/create_arrow_db.yml).
    ///   0x018d (4 + 16·n: itemId.L material[3].L — the materials are always 0) → 0x018e (product.L, additive ids ×3; product 0 cancels):
    ///     forging (hammers), metals (Mini Furnace), potions (AM_PHARMACY), runes, GX poisons. Result 0x018f (result.W itemId.L): 0/1 smith,
    ///     2/3 alchemist, 4/5 rune, 6/7 Genetic. Forging additives (Star Crumb, elemental stone) are deleted WITHOUT 0x07fa → decremented here.
    ///   0x025a (mk_type.W, n × itemId.L) → 0x025b (mk_type echo, product; 0 cancels): cooking kits (1), Genetic/Mechanic lists (5-8).
    ///   0x01ad (n × itemId.L) → 0x01ae (id; 0 cancels): Arrow Crafting (ids are SOURCE items), elemental converters, poisons, decoys.
    ///   0x07e3 type.L (0 Change Material, 1/2 Four Spirit Analysis) → 0x07e4 (len, type, flag 1/0, {index.W amount.W}).
    /// Without a reply the server keeps the skill menu and refuses every skill use, so every way out answers.
    /// Client wave 4 (w4items): cooking (0x025a type 1, the `cooking` script command of the cookbooks: script.cpp:11099-11110) has no
    /// result packet — skill_produce_mix (skill_id 0) shows EF_COOKING_OK 608 / EF_COOKING_FAIL 609 with ZC_NOTIFY_EFFECT2 0x01f3 on the
    /// cook and adds the dish with a normal 0x0b41 (skill.cpp:13350-13354, 13505-13508; the enum values counted in script.hpp:766-1375).
    /// <see cref="OnSelfEffect"/> turns that effect into the result.
    /// </summary>
    public sealed class Crafting
    {
        public enum Mode { None, Produce, Making, Arrow, ItemList }
        public Mode State { get; private set; } = Mode.None;
        public int LastResult = -1; public int LastProduct;
        public readonly List<int> Products = new List<int>();
        public event Action Changed;
        private readonly RoSession _s; private readonly Inventory _inv; private readonly ListWindow _win; private readonly Func<Camera> _head; private readonly Action<string, string> _chat;
        private readonly Func<int> _lastSkill;
        private int _mkType, _listType; private int _stars; private int _element; // forging additives: 0-3 Star Crumbs, element stone index
        private readonly Dictionary<int, int> _picked = new Dictionary<int, int>(); // item list window: index → amount
        private (int product, int stars, int element) _pendingForge;
        private (int product, float at) _pendingCook = (0, -10f);
        public const int EfCookingOk = 608, EfCookingFail = 609;
        private static readonly int[] ElementStones = { 0, 994, 995, 996, 997 }; // none, Flame Heart, Mystic Frozen, Rough Wind, Great Nature
        private static readonly string[] ElementNames = { Lang.N("~element~none"), Lang.N("~element~fire"), Lang.N("~element~water"), Lang.N("~element~wind"), Lang.N("~element~earth") }; // w4lang: table keys, shown with Lang.T

        public Crafting(RoSession s, Inventory inv, ListWindow win, Func<Camera> head, Func<int> lastSkill, Action<string, string> chat)
        {
            _s = s; _inv = inv; _win = win; _head = head; _lastSkill = lastSkill; _chat = chat;
            _win.OnCloseClicked = Cancel;
            _inv.Changed += () => { if (State != Mode.None && _win.Visible) Refresh(); };
        }
        public bool Open => State != Mode.None;
        /// <summary>Forging additive: Star Crumbs 0-3 (the + / − row in the window).</summary>
        public int StarCrumbs { get => _stars; set { _stars = Mathf.Clamp(value, 0, 3); if (State == Mode.Produce) Refresh(); } }
        public void Reset() { State = Mode.None; _win.Hide(); Changed?.Invoke(); } // map change clears the menu server-side (unit.cpp:3666)

        // ---- recipe data
        private static List<(int product, int lv, int skill, int skillLv, List<(int item, int amount)> mats)> _recipes;
        private static Dictionary<int, List<(int item, int amount)>> _arrows;
        private static void Load()
        {
            if (_recipes != null) return;
            _recipes = new List<(int, int, int, int, List<(int, int)>)>(); _arrows = new Dictionary<int, List<(int, int)>>();
            try
            {
                string dir = Path.Combine(Application.streamingAssetsPath, "data", "craft");
                foreach (var o in MiniJson.AsList(MiniJson.Parse(File.ReadAllText(Path.Combine(dir, "produce.json")))))
                {
                    var d = MiniJson.AsObject(o); var mats = new List<(int, int)>();
                    foreach (var m in MiniJson.AsList(d["mats"])) { var p = MiniJson.AsList(m); mats.Add((MiniJson.Int(p[0]), MiniJson.Int(p[1]))); }
                    _recipes.Add((MiniJson.Int(d["product"]), MiniJson.Int(d["lv"]), MiniJson.Int(d["skill"]), MiniJson.Int(d["skillLv"]), mats));
                }
                foreach (var kv in MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(Path.Combine(dir, "arrows.json")))))
                {
                    var l = new List<(int, int)>(); foreach (var m in MiniJson.AsList(kv.Value)) { var p = MiniJson.AsList(m); l.Add((MiniJson.Int(p[0]), MiniJson.Int(p[1]))); }
                    _arrows[int.Parse(kv.Key)] = l;
                }
                Debug.Log($"[Craft] {_recipes.Count} recipes, {_arrows.Count} arrow sources");
            }
            catch (Exception e) { Debug.LogError("[Craft] craft tables: " + e.Message); }
        }
        private static List<(int item, int amount)> Recipe(int product)
        {
            Load();
            foreach (var r in _recipes) if (r.product == product) return r.mats;
            return null;
        }
        private static int RecipeLevel(int product) { Load(); foreach (var r in _recipes) if (r.product == product) return r.lv; return 0; }

        private int Have(int item) { int n = 0; foreach (var e in _inv.Items.Values) if (e.ItemId == item && e.Wearing == 0) n += e.Amount; return n; }
        private static string Name(int item) => GameDb.GetItem(item).Display;
        private string RecipeText(int product)
        {
            var mats = Recipe(product); if (mats == null || mats.Count == 0) return "";
            return string.Join(", ", mats.Select(m => m.amount == 0 ? Lang.F("{0} (kept)", Name(m.item)) : $"{Name(m.item)} ×{m.amount}" + (Have(m.item) < Math.Max(1, m.amount) ? " " + Lang.F("<color=#ff8a7a>(have {0})</color>", Have(m.item)) : "")));
        }
        private bool CanMake(int product) { var mats = Recipe(product); if (mats == null) return true; foreach (var m in mats) if (Have(m.item) < Math.Max(1, m.amount)) return false; return true; }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x018d:
                {
                    r.Skip(4); Products.Clear();
                    while (r.Remaining >= 16) { Products.Add((int)r.U32()); r.Skip(12); }
                    Debug.Log($"[Craft] makable list: {Products.Count}");
                    if (Products.Count == 0) { _chat(Lang.T("You have the materials for nothing on that list."), "ffb070"); return true; }
                    State = Mode.Produce; _stars = 0; _element = 0; Show();
                    return true;
                }
                case 0x025a:
                {
                    r.Skip(4); _mkType = r.U16(); Products.Clear();
                    while (r.Remaining >= 4) Products.Add((int)r.U32());
                    Debug.Log($"[Craft] making list type {_mkType}: {Products.Count}");
                    State = Mode.Making; Show(); // cookbook lists come even when empty; they still need an answer
                    return true;
                }
                case 0x01ad:
                {
                    r.Skip(4); Products.Clear();
                    while (r.Remaining >= 4) Products.Add((int)r.U32());
                    Debug.Log($"[Craft] arrow-style list: {Products.Count}");
                    if (Products.Count == 0) { _chat(Lang.T("Nothing on that list can be made right now."), "ffb070"); return true; }
                    State = Mode.Arrow; Show();
                    return true;
                }
                case 0x07e3:
                {
                    r.Skip(2); _listType = r.S32(); _picked.Clear();
                    Debug.Log($"[Craft] item list window type {_listType}");
                    State = Mode.ItemList; Show();
                    return true;
                }
                case 0x018f:
                {
                    r.Skip(2); int res = r.U16(); int item = (int)r.U32(); LastResult = res; LastProduct = item;
                    bool ok = res == 0 || res == 2 || res == 4 || res == 6;
                    _chat(ok ? Lang.F("You made {0}!", Name(item)) : Lang.F("Making {0} failed.", Name(item)), ok ? "a8e6a3" : "ffb070");
                    // forging additives are deleted with pc_delitem(...,1,...) = no 0x07fa (skill.cpp:12886-12894)
                    if ((res == 0 || res == 1) && _pendingForge.product == item)
                    {
                        TakeOut(1000, _pendingForge.stars);
                        if (_pendingForge.element > 0) TakeOut(ElementStones[_pendingForge.element], 1);
                    }
                    _pendingForge = default;
                    Debug.Log($"[Craft] result {res} item {item}");
                    Changed?.Invoke();
                    return true;
                }
                default: return false;
            }
        }

        private void TakeOut(int item, int count)
        {
            foreach (var e in _inv.Items.Values.ToList())
            {
                if (count <= 0) break;
                if (e.ItemId != item || e.Wearing != 0) continue;
                int k = Math.Min(count, e.Amount); _inv.TakeOut(e.Index, k); count -= k;
            }
        }

        // ---- sending
        public void Produce(int product)
        {
            if (State != Mode.Produce) return;
            bool forging = RecipeLevel(product) >= 1 && RecipeLevel(product) <= 3;
            int s = forging ? Mathf.Min(_stars, Have(1000)) : 0, el = forging && _element > 0 && Have(ElementStones[_element]) > 0 ? _element : 0;
            var w = new PacketWriter(0x018e).U32((uint)product);
            var adds = new List<int>(); for (int i = 0; i < s; i++) adds.Add(1000); if (el > 0) adds.Add(ElementStones[el]);
            for (int i = 0; i < 3; i++) w.U32(i < adds.Count ? (uint)adds[i] : 0u);
            _s.SendPacket(w.ToArray());
            _pendingForge = forging ? (product, s, el) : default;
            Debug.Log($"[Craft] produce {product} stars {s} element {el}");
            Finish();
        }
        public void Make(int product)
        {
            if (State != Mode.Making) return;
            _s.SendPacket(new PacketWriter(0x025b).U16((ushort)_mkType).U32((uint)product).ToArray()); Debug.Log($"[Craft] make {product} type {_mkType}");
            if (_mkType == 1) _pendingCook = (product, Time.time); // client wave 4: the answer is an effect, not 0x018f (see the class notes)
            Finish();
        }

        /// <summary>Client wave 4: a ZC_NOTIFY_EFFECT2 on ourselves (the packet is CombatFx's; LokiGame.W4Items.cs peeks at it). Cooking's
        /// success / failure effect right after a cooking choice is its result: LastResult 0 made, 1 failed.</summary>
        public void OnSelfEffect(int effect)
        {
            if ((effect != EfCookingOk && effect != EfCookingFail) || _pendingCook.product == 0 || Time.time - _pendingCook.at > 5f) return;
            bool ok = effect == EfCookingOk; LastResult = ok ? 0 : 1; LastProduct = _pendingCook.product; _pendingCook = (0, -10f);
            _chat(ok ? Lang.F("You cooked {0}!", Name(LastProduct)) : Lang.F("Cooking {0} failed.", Name(LastProduct)), ok ? "a8e6a3" : "ffb070");
            Debug.Log($"[Craft] cooking result {(ok ? "made" : "failed")} item {LastProduct}");
            Changed?.Invoke();
        }
        public void Arrow(int item) { if (State != Mode.Arrow) return; _s.SendPacket(new PacketWriter(0x01ae).U32((uint)item).ToArray()); Debug.Log($"[Craft] arrow-list pick {item}"); Finish(); }
        public void ItemListOk()
        {
            if (State != Mode.ItemList) return;
            var w = new PacketWriter(0x07e4).U16(0).S32(_listType).S32(1);
            foreach (var kv in _picked) if (kv.Value > 0) w.U16((ushort)kv.Key).U16((ushort)kv.Value);
            _s.SendPacket(w.ToArrayWithLength());
            Debug.Log($"[Craft] item list ok: {_picked.Count(k => k.Value > 0)} entries");
            Finish();
        }
        /// <summary>Every list must be answered: product 0 / flag 0 clears the server's skill menu (research §1.6).</summary>
        public void Cancel()
        {
            switch (State)
            {
                case Mode.Produce: _s.SendPacket(new PacketWriter(0x018e).U32(0).U32(0).U32(0).U32(0).ToArray()); break;
                case Mode.Making: _s.SendPacket(new PacketWriter(0x025b).U16((ushort)_mkType).U32(0).ToArray()); break;
                case Mode.Arrow: _s.SendPacket(new PacketWriter(0x01ae).U32(0).ToArray()); break;
                case Mode.ItemList: _s.SendPacket(new PacketWriter(0x07e4).U16(0).S32(_listType).S32(0).ToArrayWithLength()); break;
            }
            Debug.Log($"[Craft] cancel {State}");
            Finish();
        }
        private void Finish() { State = Mode.None; _win.Hide(); Changed?.Invoke(); }

        private void Show() { _win.SetInput(false); Refresh(); _win.Open(_head()); Changed?.Invoke(); }

        private void Refresh()
        {
            var rows = new List<ListWindow.Row>();
            _win.SetTabs(null, 0, null);
            switch (State)
            {
                case Mode.Produce:
                {
                    int lv = Products.Count > 0 ? RecipeLevel(Products[0]) : 0; bool forging = lv >= 1 && lv <= 3;
                    _win.SetTitle(forging ? Lang.T("Forge") : lv == 21 ? Lang.T("Mini Furnace") : lv == 22 ? Lang.T("Pharmacy") : lv == 24 ? Lang.T("Runes") : lv == 25 ? Lang.T("Poisons") : Lang.T("Make"));
                    _win.SetInfo(forging ? (_stars == 1 ? Lang.F("Additives: 1 Star Crumb, element {0} (each lowers the chance).", Lang.T(ElementNames[_element])) : Lang.F("Additives: {0} Star Crumbs, element {1} (each lowers the chance).", _stars, Lang.T(ElementNames[_element]))) : Lang.T("Choose what to make."));
                    if (forging)
                    {
                        rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(GameDb.GetItem(1000)), Text = Lang.F("Star Crumbs: {0}", _stars) + "  " + Lang.F("<color=#9aa3b5>(have {0})</color>", Have(1000)), A = "+", OnA = () => { _stars = Mathf.Min(3, _stars + 1); Refresh(); }, AEnabled = _stars < 3 && _stars < Have(1000), B = "−", OnB = () => { _stars = Mathf.Max(0, _stars - 1); Refresh(); }, BEnabled = _stars > 0 });
                        rows.Add(new ListWindow.Row { Icon = _element > 0 ? WristMenu.ItemIcon(GameDb.GetItem(ElementStones[_element])) : null, Text = Lang.F("Element: {0}", Lang.T(ElementNames[_element])) + (_element > 0 ? "  " + Lang.F("<color=#9aa3b5>(have {0})</color>", Have(ElementStones[_element])) : ""), A = Lang.T("Next"), OnA = () => { for (int k = 1; k <= 5; k++) { int c = (_element + k) % 5; if (c == 0 || Have(ElementStones[c]) > 0) { _element = c; break; } } Refresh(); } });
                    }
                    foreach (int p in Products) { int p2 = p; rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(GameDb.GetItem(p)), Text = $"{Name(p)}\n<size=19>{RecipeText(p)}</size>", A = Lang.T("Make"), OnA = () => Produce(p2), AEnabled = CanMake(p) }); }
                    break;
                }
                case Mode.Making:
                {
                    // client wave 5 (w5live, seen live): rAthena sends type 6 for Mix Cooking AND Special Pharmacy (skills/merchant/mixcooking.cpp:21,
                    // specialpharmacy.cpp:19), 5 Create Bomb, 7 Manufacture Machine, 8 Bionic Pharmacy (createbomb.cpp:21, manufacturemachine.cpp:19,
                    // bionicpharmacy.cpp:19): the skill we just used names the window (its name from the player's GRF), the type only as a fallback
                    int msk = _lastSkill();
                    bool own = (_mkType == 6 && (msk == 2495 || msk == 2497)) || (_mkType == 5 && msk == 2496) || (_mkType == 7 && msk == 5297) || (_mkType == 8 && msk == 5336);
                    _win.SetTitle(own ? SkillDb.Get(msk).Name : _mkType == 1 ? Lang.T("Cooking") : _mkType == 6 ? Lang.T("Mixing") : _mkType == 7 ? Lang.T("Manufacture") : _mkType == 8 ? Lang.T("Bionic Pharmacy") : _mkType == 5 ? Lang.T("Bombs") : Lang.T("Make"));
                    _win.SetInfo(Products.Count == 0 ? Lang.T("You don't have the ingredients for any recipe in this book.") : Lang.T("Choose what to make."));
                    foreach (int p in Products) { int p2 = p; rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(GameDb.GetItem(p)), Text = $"{Name(p)}\n<size=19>{RecipeText(p)}</size>", A = Lang.T("Make"), OnA = () => Make(p2), AEnabled = CanMake(p) }); }
                    break;
                }
                case Mode.Arrow:
                {
                    Load();
                    int sk = _lastSkill();
                    bool arrows = sk == 147 || Products.All(p => _arrows.ContainsKey(p));
                    _win.SetTitle(sk == 1007 ? Lang.T("Elemental Converter") : sk == 2027 ? Lang.T("Poison") : sk == 2282 ? Lang.T("Magic Decoy") : arrows ? Lang.T("Arrow Crafting") : Lang.T("Choose"));
                    _win.SetInfo(arrows ? Lang.T("Choose the item to turn into arrows.") : Lang.T("Choose one."));
                    foreach (int p in Products)
                    {
                        int p2 = p; string extra = arrows && _arrows.TryGetValue(p, out var outs) ? " → " + string.Join(", ", outs.Select(x => $"{Name(x.item)} ×{x.amount}")) : sk == 1007 ? $"\n<size=19>{RecipeText(p)}</size>" : "";
                        rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(GameDb.GetItem(p)), Text = $"{Name(p)}{(arrows ? "  " + Lang.F("<color=#9aa3b5>(have {0})</color>", Have(p)) : "")}{extra}", A = Lang.T("Choose"), OnA = () => Arrow(p2) });
                    }
                    break;
                }
                case Mode.ItemList:
                {
                    _win.SetTitle(_listType == 0 ? Lang.T("Change Material") : Lang.T("Four Spirit Analysis"));
                    _win.SetInfo(_listType == 2 ? Lang.T("Pick rough elemental stones (in tens).") : _listType == 1 ? Lang.T("Pick pure elemental stones to break down.") : Lang.T("Pick the materials to combine."));
                    foreach (var e in _inv.Items.Values.Where(x => !x.IsEquip).OrderBy(x => x.Index))
                    {
                        var e2 = e; int step = _listType == 2 ? 10 : 1; _picked.TryGetValue(e.Index, out int cur);
                        rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(e.Info), Text = $"{e.Info.Display}  <color=#ffd060>{cur}</color> / {e.Amount}", A = "+" + step, OnA = () => { _picked[e2.Index] = Mathf.Min(e2.Amount, cur + step); Refresh(); }, AEnabled = cur + step <= e.Amount, B = "−", OnB = () => { _picked[e2.Index] = Mathf.Max(0, cur - step); Refresh(); }, BEnabled = cur > 0, Highlight = cur > 0 });
                    }
                    _win.SetFooter((Lang.T("Cancel"), Cancel), ("OK", ItemListOk));
                    _win.SetRows(rows);
                    return;
                }
                default: return;
            }
            _win.SetFooter((Lang.T("Cancel"), Cancel));
            _win.SetRows(rows);
        }
    }
}
