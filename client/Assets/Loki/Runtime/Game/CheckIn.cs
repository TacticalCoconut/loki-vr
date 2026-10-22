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
    /// Daily check-in (attendance; research/windows9_packets.md §4; rAthena e985006). Open with CZ_OPEN_UI 0x0a68 {5} → ZC_UI_OPEN 0x0ae2
    /// {7, data} where data = 10 × days claimed + (claimed today ? 1 : 0) (pc.cpp:15590-15610); the server also pops it at login while today
    /// is unclaimed. Claim with 0x0aef → 0x0af0 {0.L, day.L}; the item comes by RODEX mail. No running period → ZC_MSG_COLOR 3474.
    /// The reward list is never sent: data/account/attendance.json is made from the server's db/import/attendance.yml.
    /// </summary>
    public sealed class CheckIn
    {
        public int DaysClaimed = -1; public bool ClaimedToday; public int LastClaimedDay = -1;
        public event Action Changed;
        private readonly RoSession _s; private readonly ListWindow _win; private readonly Func<Camera> _head; private readonly Action<string, string> _chat;

        public CheckIn(RoSession s, ListWindow win, Func<Camera> head, Action<string, string> chat)
        {
            _s = s; _win = win; _head = head; _chat = chat;
            _win.OnCloseClicked = Hide;
        }
        public void Reset() { DaysClaimed = -1; ClaimedToday = false; _win.Hide(); }

        // ---- data
        private static List<(int start, int end, List<(int day, int item, int amount)> rewards)> _periods;
        private static void Load()
        {
            if (_periods != null) return;
            _periods = new List<(int, int, List<(int, int, int)>)>();
            try
            {
                var o = MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "data", "account", "attendance.json"))));
                foreach (var po in MiniJson.AsList(o["periods"]) ?? new List<object>())
                {
                    var p = MiniJson.AsObject(po); var list = new List<(int, int, int)>();
                    foreach (var ro in MiniJson.AsList(p["rewards"]) ?? new List<object>()) { var r = MiniJson.AsList(ro); list.Add((MiniJson.Int(r[0]), MiniJson.Int(r[1]), MiniJson.Int(r[2]))); }
                    list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
                    _periods.Add((MiniJson.Int(p["start"]), MiniJson.Int(p["end"]), list));
                }
            }
            catch (Exception e) { Debug.LogWarning("[CheckIn] data: " + e.Message); }
        }
        /// <summary>The period covering today (server time is assumed to match this PC's local date, as on a local server).</summary>
        public static (int start, int end, List<(int day, int item, int amount)> rewards)? Period()
        {
            Load();
            int today = int.Parse(DateTime.Now.ToString("yyyyMMdd"));
            foreach (var p in _periods) if (p.start <= today && today <= p.end) return p;
            return null;
        }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x0ae2:
                {
                    r.Skip(2); int type = r.U8(); int data = r.S32();
                    if (type != 7) return false;
                    DaysClaimed = data / 10; ClaimedToday = data % 10 != 0;
                    Debug.Log($"[CheckIn] open: {DaysClaimed} days claimed, today {ClaimedToday}");
                    Show(); Changed?.Invoke(); return true;
                }
                case 0x0af0: // ZC_ACK_CHECK_ATTENDANCE 0.L day.L
                {
                    r.Skip(6); LastClaimedDay = r.S32(); DaysClaimed = Math.Max(DaysClaimed, LastClaimedDay); ClaimedToday = true;
                    var p = Period(); string what = null; // w4lang: the reward item alone; the two sentences below
                    if (p.HasValue) foreach (var x in p.Value.rewards) if (x.day == LastClaimedDay) what = $"{GameDb.GetItem(x.item).Name}{(x.amount > 1 ? " x" + x.amount : "")}";
                    _chat(what != null ? Lang.F("Checked in: day {0} — {1} is on its way by mail.", LastClaimedDay, what) : Lang.F("Checked in: day {0}.", LastClaimedDay), "a8e6a3");
                    Debug.Log($"[CheckIn] claimed day {LastClaimedDay}");
                    Changed?.Invoke(); Refresh(); return true;
                }
                default: return false;
            }
        }

        /// <summary>Asks the server for the window (0x0a68 type 5); it answers with 0x0ae2 type 7, or msg 3474 when no period runs.</summary>
        public void Open() { _s.SendPacket(new PacketWriter(0x0a68).U8(5).ToArray()); Debug.Log("[CheckIn] request"); }
        public void Claim() { if (ClaimedToday) return; LastClaimedDay = -1; _s.SendPacket(new PacketWriter(0x0aef).ToArray()); Debug.Log("[CheckIn] claim"); }

        public bool Visible => _win.Visible;
        public void Show() { _win.SetTabs(null, 0, null); _win.SetInput(false); _win.Open(_head()); Refresh(); }
        public void Hide() => _win.Hide();

        public void Refresh()
        {
            if (!_win.Visible) return;
            _win.SetTitle(Lang.T("Daily check-in"));
            var p = Period();
            var rows = new List<ListWindow.Row>();
            if (!p.HasValue)
            {
                _win.SetInfo(Lang.T("No check-in period is running."));
                rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>The server has no attendance period for today (db/import/attendance.yml).</color>") });
            }
            else
            {
                var (start, end, rewards) = p.Value;
                string d(int v) => $"{v / 10000}-{v / 100 % 100:00}-{v % 100:00}";
                _win.SetInfo(Lang.F("{0} of {1} days", Math.Max(0, DaysClaimed), rewards.Count) + "  ·  " + (ClaimedToday ? Lang.T("done for today") : Lang.T("today is waiting")) + "  ·  " + Lang.F("{0} to {1}", d(start), d(end)));
                foreach (var (day, item, amount) in rewards)
                {
                    var info = GameDb.GetItem(item);
                    bool got = day <= DaysClaimed, next = !ClaimedToday && day == DaysClaimed + 1;
                    rows.Add(new ListWindow.Row
                    {
                        Icon = WristMenu.ItemIcon(info), Highlight = next,
                        Text = Lang.F("Day {0}: {1}", day, info.Name) + $"{(amount > 1 ? $" x{amount}" : "")}  {(got ? "<color=#7fd67f>✓</color>" : next ? Lang.T("<color=#ffd060>today</color>") : "")}",
                        A = next ? Lang.T("Check in") : null, OnA = Claim,
                    });
                }
                _win.SetRows(rows, true); _win.ShowRow(Math.Max(0, DaysClaimed));
                _win.SetFooter((ClaimedToday || DaysClaimed >= rewards.Count ? null : Lang.T("Check in"), Claim), (Lang.T("Close"), Hide));
                return;
            }
            _win.SetRows(rows);
            _win.SetFooter((Lang.T("Close"), Hide));
        }
    }
}
