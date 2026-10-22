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
    /// Reputation (client wave 2; rAthena e985006 clif.cpp:24253-24308, pc.cpp:279-379, research §D; enabled for PACKETVER_RE ≥ 20211103):
    ///   ZC_REPUTE_INFO 0x0b8d len.W success.B {type.Q points.q}* — the whole list at login and on every map change, one entry after a
    ///   script changes it (set_ / add_reputation_points); ZC_REPUTE_OPEN 0x0b8e group.Q type.L mode.L — the `reputationui` script
    ///   command opens the window on a group (0 = all) and highlights a type. The server never sends names: the official client reads
    ///   reputeinfodata / reputegroupdata made from db/re/reputation*.yml, which Loki copies into data/account/reputation.json.
    /// </summary>
    public sealed class Reputation
    {
        public sealed class Type { public int Id, Min, Max; public string Name; }
        public sealed class Group { public int Id; public string Name; public List<int> Types = new List<int>(); }
        public readonly Dictionary<long, long> Points = new Dictionary<long, long>();
        public readonly List<Type> Types = new List<Type>();
        public readonly List<Group> Groups = new List<Group>();
        public bool Received { get; private set; }
        public int Updates { get; private set; }
        public event Action Changed;
        private readonly ListWindow _win; private readonly Func<Camera> _head; private readonly Action<string, string> _chat;
        private int _group; private long _highlight;

        public Reputation(ListWindow win, Func<Camera> head, Action<string, string> chat)
        {
            _win = win; _head = head; _chat = chat; _win.OnCloseClicked = () => _win.Hide();
            try
            {
                var o = MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "data", "account", "reputation.json"))));
                foreach (var t in MiniJson.AsList(o["types"])) { var d = MiniJson.AsObject(t); Types.Add(new Type { Id = MiniJson.Int(d["id"]), Name = MiniJson.Str(d["name"], ""), Min = MiniJson.Int(d["min"]), Max = MiniJson.Int(d["max"]) }); }
                foreach (var g in MiniJson.AsList(o["groups"])) { var d = MiniJson.AsObject(g); var gr = new Group { Id = MiniJson.Int(d["id"]), Name = MiniJson.Str(d["name"], "") }; foreach (var x in MiniJson.AsList(d["types"])) gr.Types.Add(MiniJson.Int(x)); Groups.Add(gr); }
            }
            catch (Exception e) { Debug.LogWarning("[Reputation] data/account/reputation.json: " + e.Message); }
        }

        public void ResetForCharacter() { Points.Clear(); Received = false; _win.Hide(); }
        public string NameOf(long id) { var t = Types.Find(x => x.Id == id); return t != null ? t.Name : Lang.F("Reputation {0}", id); }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x0b8d:
                {
                    r.Skip(4); r.U8(); int count = 0;
                    while (r.Remaining >= 16) { long type = r.S64(); long pts = r.S64(); bool had = Points.TryGetValue(type, out long old); Points[type] = pts; count++; if (Received && had && old != pts) _chat(Lang.F("{0} reputation {1}{2} → {3}", NameOf(type), pts > old ? "+" : "", pts - old, pts), pts >= old ? "9fe39a" : "ff8a7a"); }
                    Received = true; Updates++;
                    Debug.Log($"[Reputation] {count} entr{(count == 1 ? "y" : "ies")}: {string.Join(", ", new List<long>(Points.Keys).ConvertAll(k => NameOf(k) + " " + Points[k]))}");
                    Refresh(); Changed?.Invoke(); return true;
                }
                case 0x0b8e:
                {
                    r.Skip(2); long group = r.S64(); long type = r.U32(); r.U32();
                    _group = (int)group; _highlight = type;
                    Debug.Log($"[Reputation] window: group {group}, type {type}");
                    Show(); return true;
                }
                default: return false;
            }
        }

        public bool Visible => _win.Visible;
        public ListWindow Window => _win;
        public void Show() { _win.Open(_head()); Refresh(); }

        public void Refresh()
        {
            if (!_win.Visible) return;
            var names = new List<string> { Lang.T("All") }; foreach (var g in Groups) names.Add(g.Name);
            int tab = _group <= 0 ? 0 : Mathf.Max(0, Groups.FindIndex(g => g.Id == _group) + 1);
            _win.SetTabs(names.Count > 1 ? names.ToArray() : null, tab, t => { _group = t == 0 ? 0 : Groups[t - 1].Id; Refresh(); });
            _win.SetTitle(Lang.T("Reputation")); _win.SetInput(false);
            _win.SetInfo(Received ? Lang.T("Earn or lose it with the people of each place.") : Lang.T("Not received from the server yet."));
            var rows = new List<ListWindow.Row>();
            var g0 = Groups.Find(g => g.Id == _group);
            foreach (var t in Types)
            {
                if (g0 != null && !g0.Types.Contains(t.Id)) continue;
                Points.TryGetValue(t.Id, out long p);
                int bars = 20; float f = t.Max > t.Min ? Mathf.Clamp01((p - t.Min) / (float)(t.Max - t.Min)) : 0.5f; int fill = Mathf.RoundToInt(f * bars);
                string bar = $"<color={(p >= 0 ? "#7fd67f" : "#ff8a7a")}>{new string('|', fill)}</color><color=#555a66>{new string('|', bars - fill)}</color>";
                rows.Add(new ListWindow.Row { Text = $"{t.Name}  <color=#ffd060>{p:N0}</color>  " + Lang.F("<color=#9aa3b5>({0:N0} to {1:N0})</color>", t.Min, t.Max) + $"\n{bar}", Highlight = t.Id == _highlight });
            }
            _win.SetFooter((Lang.T("Close"), () => _win.Hide()));
            _win.SetRows(rows, true);
        }
    }
}
