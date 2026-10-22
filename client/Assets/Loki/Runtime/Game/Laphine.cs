using System;
using System.Collections.Generic;
using System.IO;
using Loki.IO;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Laphine synthesis and upgrade (client wave 2; rAthena e985006 clif.cpp:23560-23916, itemdb.cpp:1531-1909, research §B):
    ///   synthesis: ZC_RANDOM_COMBINE_ITEM_UI_OPEN 0x0a4e itemId.L (the recipe item used) → CZ_REQ_RANDOM_COMBINE_ITEM 0x0a4f len.W itemId.L
    ///     {index.W count.W}* — exactly RequiredRequirementsCount entries, each an item of the recipe's list with exactly its Amount, not worn,
    ///     refine within [min, max], no index twice (else the server stays silent) → ZC_ACK_RANDOM_COMBINE_ITEM 0x0a50 result.W (0 ok, 5 amount,
    ///     7 item); the window stays open (and the player blocked) until CZ 0x0a70.
    ///   upgrade: ZC_RANDOM_UPGRADE_ITEM_UI_OPEN 0x0ab4 itemId.L → CZ_REQ_RANDOM_UPGRADE_ITEM 0x0ab6 itemId.L index.W (a target item: identified,
    ///     not worn, refine in range, no cards unless allowed) → ZC_CHANGE_ITEM_OPTION 0x0b43 (index cards options refine grade) +
    ///     ZC_ACK_RANDOM_UPGRADE_ITEM 0x0ab7 result.W (0 ok, 1 failed); close CZ 0x0ab5.
    /// The server sends only the item id: the recipes are data/craft/laphine_synthesis.json / laphine_upgrade.json (from db/re).
    /// </summary>
    public sealed class Laphine
    {
        private sealed class Synth { public string Group; public int Count, MinRefine, MaxRefine; public List<(int item, int amount)> Needs = new List<(int, int)>(); }
        private sealed class Upg { public string Group; public int Result, ResultMin, ResultMax, MinRefine, MaxRefine, Options; public bool Cards; public HashSet<int> Targets = new HashSet<int>(); }
        private readonly Dictionary<int, Synth> _synth = new Dictionary<int, Synth>();
        private readonly Dictionary<int, Upg> _upg = new Dictionary<int, Upg>();
        public enum Mode { None, Synthesis, Upgrade }
        public Mode State { get; private set; }
        public int ItemId { get; private set; }
        public int LastResult = -1; public int Results;
        private readonly List<int> _sel = new List<int>(); // chosen inventory indexes
        private readonly RoSession _s; private readonly Inventory _inv; private readonly ListWindow _win; private readonly Func<Camera> _head; private readonly Action<string, string> _chat;

        public Laphine(RoSession s, Inventory inv, ListWindow win, Func<Camera> head, Action<string, string> chat)
        {
            _s = s; _inv = inv; _win = win; _head = head; _chat = chat;
            _win.OnCloseClicked = Close;
            _inv.Changed += () => { if (State != Mode.None) { _sel.RemoveAll(i => !_inv.Items.ContainsKey(i)); Refresh(); } };
            string dir = Path.Combine(Application.streamingAssetsPath, "data", "craft");
            try
            {
                foreach (var kv in MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(Path.Combine(dir, "laphine_synthesis.json")))))
                {
                    var d = MiniJson.AsObject(kv.Value); var s0 = new Synth { Group = MiniJson.Str(d["group"], ""), Count = MiniJson.Int(d["count"], 1), MinRefine = MiniJson.Int(d["minRefine"]), MaxRefine = MiniJson.Int(d["maxRefine"], 20) };
                    foreach (var p in MiniJson.AsList(d["needs"])) { var l = MiniJson.AsList(p); s0.Needs.Add((MiniJson.Int(l[0]), MiniJson.Int(l[1], 1))); }
                    _synth[int.Parse(kv.Key)] = s0;
                }
                foreach (var kv in MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(Path.Combine(dir, "laphine_upgrade.json")))))
                {
                    var d = MiniJson.AsObject(kv.Value);
                    var u = new Upg { Group = MiniJson.Str(d["group"], ""), Result = MiniJson.Int(d["result"]), ResultMin = MiniJson.Int(d["resultMin"]), ResultMax = MiniJson.Int(d["resultMax"]), MinRefine = MiniJson.Int(d["minRefine"]), MaxRefine = MiniJson.Int(d["maxRefine"], 20), Options = MiniJson.Int(d["options"]), Cards = MiniJson.Bool(d["cards"]) };
                    foreach (var t in MiniJson.AsList(d["targets"])) u.Targets.Add(MiniJson.Int(t));
                    _upg[int.Parse(kv.Key)] = u;
                }
            }
            catch (Exception e) { Debug.LogWarning("[Laphine] data/craft/laphine_*.json: " + e.Message); }
        }

        public void Reset() { State = Mode.None; _sel.Clear(); _win.Hide(); } // a map change clears the server's laphine state (pc.cpp:6960)

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x0a4e:
                case 0x0ab4:
                {
                    r.Skip(2); ItemId = (int)r.U32(); State = id == 0x0a4e ? Mode.Synthesis : Mode.Upgrade; _sel.Clear();
                    Debug.Log($"[Laphine] {State} window for {ItemId} ({GameDb.GetItem(ItemId).English}); recipe known {(State == Mode.Synthesis ? _synth.ContainsKey(ItemId) : _upg.ContainsKey(ItemId))}");
                    _win.Open(_head()); Refresh(); return true;
                }
                case 0x0a50:
                {
                    r.Skip(2); LastResult = r.S16(); Results++;
                    _chat(LastResult == 0 ? Lang.T("Laphine synthesis done — check your inventory.") : LastResult == 5 ? Lang.T("Wrong amount of an ingredient.") : LastResult == 7 ? Lang.T("One of those items can't be used for this.") : Lang.F("Synthesis failed ({0}).", LastResult), LastResult == 0 ? "9fe39a" : "ffb070");
                    Debug.Log($"[Laphine] synthesis result {LastResult}");
                    _sel.Clear(); Refresh(); return true;
                }
                case 0x0ab7:
                {
                    r.Skip(2); LastResult = r.U16(); Results++;
                    _chat(LastResult == 0 ? Lang.T("The upgrade worked.") : Lang.T("That item can't be upgraded with this."), LastResult == 0 ? "9fe39a" : "ffb070");
                    Debug.Log($"[Laphine] upgrade result {LastResult}");
                    _sel.Clear(); Refresh(); return true;
                }
                case 0x0b43:
                {
                    // ZC_CHANGE_ITEM_OPTION index.W damaged.B card.L×4 option(5×5) refine.B grade.B: the upgraded item, re-sent in place
                    r.Skip(2); int idx = r.S16(); r.U8(); var cards = Inventory.ReadCards(ref r); var opts = Inventory.ReadOptions(ref r); int refine = r.U8(), grade = r.U8();
                    for (int i = 0; i < 4; i++) _inv.Patch(idx, slot: i, card: cards[i]);
                    _inv.PatchOptions(idx, opts); // client wave 3 (w3equip): the new random options
                    _inv.Patch(idx, refine: refine, grade: grade);
                    Debug.Log($"[Laphine] item {idx} now +{refine} grade {grade}");
                    return true;
                }
                default: return false;
            }
        }

        private List<Inventory.Entry> Candidates()
        {
            var l = new List<Inventory.Entry>();
            if (State == Mode.Synthesis && _synth.TryGetValue(ItemId, out var s))
                foreach (var e in _inv.Items.Values)
                {
                    if (e.Wearing != 0 || e.Refine < s.MinRefine || e.Refine > s.MaxRefine) continue;
                    int need = s.Needs.FindIndex(x => x.item == e.ItemId); if (need < 0 || e.Amount < s.Needs[need].amount) continue;
                    l.Add(e);
                }
            else if (State == Mode.Upgrade && _upg.TryGetValue(ItemId, out var u))
                foreach (var e in _inv.Items.Values)
                {
                    if (!u.Targets.Contains(e.ItemId) || e.Wearing != 0 || !e.Identified || e.Refine < u.MinRefine || e.Refine > u.MaxRefine) continue;
                    if (!u.Cards && e.Cards != null && Array.Exists(e.Cards, c => c > 0 && GameDb.GetItem(c).Type == 6)) continue;
                    l.Add(e);
                }
            return l;
        }

        public void Toggle(int index)
        {
            if (_sel.Contains(index)) _sel.Remove(index);
            else if (State == Mode.Upgrade) { _sel.Clear(); _sel.Add(index); }
            else if (_synth.TryGetValue(ItemId, out var s) && _sel.Count < s.Count) _sel.Add(index);
            Refresh();
        }

        public void Go()
        {
            if (State == Mode.Synthesis && _synth.TryGetValue(ItemId, out var s))
            {
                if (_sel.Count != s.Count) return;
                var w = new PacketWriter(0x0a4f).U16(0).U32((uint)ItemId);
                foreach (var idx in _sel) { var e = _inv.Items[idx]; int amt = s.Needs.Find(x => x.item == e.ItemId).amount; w.S16((short)idx).S16((short)amt); }
                _s.SendPacket(w.ToArrayWithLength());
                Debug.Log($"[Laphine] synthesize {ItemId} with {string.Join(", ", _sel)}");
            }
            else if (State == Mode.Upgrade && _sel.Count == 1)
            {
                _s.SendPacket(new PacketWriter(0x0ab6).U32((uint)ItemId).U16((ushort)_sel[0]).ToArray());
                Debug.Log($"[Laphine] upgrade {_sel[0]} with {ItemId}");
            }
        }

        public void Close()
        {
            if (State == Mode.Synthesis) _s.SendPacket(new PacketWriter(0x0a70).ToArray());
            else if (State == Mode.Upgrade) _s.SendPacket(new PacketWriter(0x0ab5).ToArray());
            Debug.Log($"[Laphine] close {State}");
            State = Mode.None; _sel.Clear(); _win.Hide();
        }

        public void Refresh()
        {
            if (!_win.Visible || State == Mode.None) return;
            var rows = new List<ListWindow.Row>();
            var recipe = GameDb.GetItem(ItemId);
            _win.SetTabs(null, 0, null); _win.SetInput(false);
            if (State == Mode.Synthesis)
            {
                _synth.TryGetValue(ItemId, out var s);
                _win.SetTitle(Lang.F("Laphine synthesis: {0}", recipe.Name));
                // w4lang: three units joined by "  ·  "; the item count is a whole key per number
                _win.SetInfo(s == null ? Lang.T("Loki doesn't know this recipe (data/craft/laphine_synthesis.json).") : (s.Count > 1 ? Lang.F("Put in {0} items from the list", s.Count) : Lang.F("Put in {0} item from the list", s.Count)) + "  ·  " + Lang.F("chosen {0}/{1}", _sel.Count, s.Count) + (s.MinRefine > 0 || s.MaxRefine < 20 ? "  ·  " + Lang.F("refine +{0}..+{1}", s.MinRefine, s.MaxRefine) : ""));
                foreach (var e in Candidates())
                {
                    int idx = e.Index; bool on = _sel.Contains(idx); int amt = s.Needs.Find(x => x.item == e.ItemId).amount;
                    rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(e.Info), Highlight = on, Text = $"{Inventory.FullName(e)}  <color=#ffd060>x{e.Amount}</color>\n<size=19>" + Lang.F("<color=#9aa3b5>uses {0}</color>", amt) + "</size>", A = on ? Lang.T("Remove") : Lang.T("Add"), OnA = () => Toggle(idx), AEnabled = on || _sel.Count < s.Count });
                }
                if (s != null && rows.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>You carry none of the items this recipe takes:</color>") + "\n<size=19>" + string.Join(", ", s.Needs.GetRange(0, Mathf.Min(6, s.Needs.Count)).ConvertAll(x => $"{x.amount}× {GameDb.GetItem(x.item).Name}")) + (s.Needs.Count > 6 ? "…" : "") + "</size>" });
                _win.SetFooter((Lang.T("Close"), Close), (s != null && _sel.Count == s.Count ? Lang.T("Combine") : null, Go));
            }
            else
            {
                _upg.TryGetValue(ItemId, out var u);
                _win.SetTitle(Lang.F("Laphine upgrade: {0}", recipe.Name));
                string res = u == null ? "" : u.Result > 0 ? Lang.F("refine becomes at least +{0}", u.Result) : u.ResultMax > 0 ? Lang.F("refine rolls up to +{0}", u.ResultMax) : u.ResultMin > 0 ? Lang.F("refine rolls from +{0}", u.ResultMin) : Lang.T("new random options");
                _win.SetInfo(u == null ? Lang.T("Loki doesn't know this upgrade (data/craft/laphine_upgrade.json).") : Lang.T("Pick the item to upgrade") + "  ·  " + res + (u.Cards ? "" : "  ·  " + Lang.T("no cards")));
                foreach (var e in Candidates())
                {
                    int idx = e.Index; bool on = _sel.Contains(idx);
                    rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(e.Info), Highlight = on, Text = Inventory.FullName(e), A = on ? Lang.T("Remove") : Lang.T("Pick"), OnA = () => Toggle(idx) });
                }
                if (u != null && rows.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>None of your items can take this upgrade (take gear off first).</color>") });
                _win.SetFooter((Lang.T("Close"), Close), (_sel.Count == 1 ? Lang.T("Upgrade") : null, Go));
            }
            _win.SetRows(rows);
        }

        // ---- smoke hooks
        public int CandidateCount => Candidates().Count;
        public int FirstSelected => _sel.Count > 0 ? _sel[0] : -1;
    }
}
