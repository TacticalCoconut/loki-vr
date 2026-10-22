using System;
using System.Collections.Generic;
using System.Text;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 4 (w4items): the roulette (rAthena e985006, PACKETVER 20211103; clif.cpp:20829-21134, packets_struct.hpp:1117-1183,
    /// clif.hpp:106-133, itemdb.hpp:30-31). Needs feature.roulette (conf/battle/feature.conf:73, on by default) and the db_roulette table
    /// (sql-files/roulette_default_data.sql; an empty table is filled with Apples, itemdb.cpp:4018-4028).
    ///   open  CZ_REQ_OPEN_ROULETTE 0x0a19 → ZC_ACK_OPEN_ROULETTE 0x0a1a result.B serial.L step.B idx.B bonusItem.L gold.L silver.L bronze.L
    ///         (step / idx: the prize still waiting to be taken, else 0 / -1)
    ///   table CZ_REQ_ROULETTE_INFO 0x0a1b → ZC_ACK_ROULETTE_INFO 0x0a1c len.W serial.L { row.W position.W itemId.L count.W unused.W } × 42:
    ///         7 rows (MAX_ROULETTE_LEVEL) of 9, 8 … 3 prizes (MAX_ROULETTE_COLUMNS - row)
    ///   spin  CZ_REQ_GENERATE_ROULETTE 0x0a1f → ZC_ACK_GENERATE_ROULETTE 0x0a20 result.B step.W idx.W bonusItem.L gold.L silver.L bronze.L:
    ///         0 won, 1 failed, 2 not enough points, 3 a losing slot. A spin costs 1 bronze (starting on row 1), else 10 silver (row 3),
    ///         else 10 gold (row 5) (clif.cpp:21064-21078). A prize not taken yet is given first by the next spin (clif.cpp:21050-21053).
    ///         The server ignores a spin or a claim for max(1, 9 - row - 3) s after a spin (sd->roulette.tick set at clif.cpp:21084, checked at 21046 and 21109) —
    ///         the official client's wheel animation; Loki waits the same time before it offers "Receive".
    ///   take  CZ_RECV_ROULETTE_ITEM 0x0a21 condition.B → ZC_RECV_ROULETTE_ITEM 0x0a22 result.B bonusItem.L: 0 received, 1 failed / nothing
    ///         to take, 2 too many items, 3 too heavy. The prize comes with a normal ZC_ITEM_PICKUP_ACK; the bonus item doubles it when it is
    ///         the prize (clif.cpp:20980-20984).
    ///   close CZ_REQ_CLOSE_ROULETTE 0x0a1d (no answer from rAthena; 0x0a1e exists in the table but is never sent).
    /// Points: the character variables RouletteGold / RouletteSilver / RouletteBronze (pc.hpp:52-54), set by NPC scripts.
    /// The official window is a wheel; in VR it is a list: one line per row, the prize of the last spin highlighted — and since client
    /// wave 5 (w5ui) the wheel beside it (UI/RouletteWheel.cs: the row it plays, the spin and its tick sound).
    /// </summary>
    public sealed class Roulette
    {
        public const int Rows = 7, Columns = 9;
        public bool IsOpen { get; private set; }
        public int Gold { get; private set; } = -1;
        public int Silver { get; private set; } = -1;
        public int Bronze { get; private set; } = -1;
        public int BonusItem { get; private set; }
        /// <summary>The prize waiting to be taken (row, position) or (-1, -1).</summary>
        public int PrizeRow { get; private set; } = -1;
        public int PrizeIdx { get; private set; } = -1;
        public int LastSpin { get; private set; } = -1;
        public int LastReceive { get; private set; } = -1;
        public int Spins { get; private set; }
        public int Receives { get; private set; }
        /// <summary>Prizes a spin gave before spinning (a prize left waiting).</summary>
        public int GivenBySpin { get; private set; }
        public bool TableReceived { get; private set; }
        /// <summary>The table: [row][position] = (item id, count); 0 = unknown.</summary>
        public readonly (int item, int count)[][] Table = new (int, int)[Rows][];
        private float _readyAt, _claimSentAt = -10f;
        private readonly RoSession _s; private readonly ListWindow _win; private readonly Func<Camera> _head; private readonly Action<string, string> _chat;

        public Roulette(RoSession s, ListWindow win, Func<Camera> head, Action<string, string> chat)
        {
            _s = s; _win = win; _head = head; _chat = chat;
            for (int i = 0; i < Rows; i++) Table[i] = new (int, int)[Columns - i];
            _win.OnCloseClicked = Close;
            _win.gameObject.AddComponent<RouletteTicker>().Owner = this;
            Wheel = RouletteWheel.Attach((RectTransform)_win.transform, this); // client wave 5 (w5ui)
        }

        /// <summary>Client wave 5 (w5ui): the wheel beside the window.</summary>
        public readonly RouletteWheel Wheel;

        /// <summary>A map change: rAthena keeps the roulette state on the character; the window just closes.</summary>
        public void Reset() { if (IsOpen) { IsOpen = false; _win.Hide(); } }

        /// <summary>The wrist button: asks the server to open the roulette (and for its table the first time).</summary>
        public void Open()
        {
            Debug.Log("[Roulette] open (0x0a19)");
            _s.SendPacket(new PacketWriter(0x0a19).ToArray());
            if (!TableReceived) _s.SendPacket(new PacketWriter(0x0a1b).ToArray());
        }

        public void Close()
        {
            if (IsOpen) { _s.SendPacket(new PacketWriter(0x0a1d).ToArray()); Debug.Log("[Roulette] close (0x0a1d)"); }
            IsOpen = false; _win.Hide();
        }

        public void Spin()
        {
            if (!IsOpen || Time.time < _readyAt) return;
            LastSpin = -1; Debug.Log("[Roulette] spin (0x0a1f)");
            _s.SendPacket(new PacketWriter(0x0a1f).ToArray());
        }

        public void Receive()
        {
            if (!IsOpen || PrizeIdx < 0 || Time.time < _readyAt) return;
            LastReceive = -1; _claimSentAt = Time.time; Debug.Log("[Roulette] receive (0x0a21)");
            _s.SendPacket(new PacketWriter(0x0a21).U8(0).ToArray());
            Refresh();
        }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x0a1a:
                {
                    r.Skip(2); int res = r.U8(); r.U32(); int step = r.S8(); int idx = r.S8(); BonusItem = (int)r.U32(); Gold = r.S32(); Silver = r.S32(); Bronze = r.S32();
                    PrizeRow = idx >= 0 ? step : -1; PrizeIdx = idx; _readyAt = 0;
                    Debug.Log($"[Roulette] open result {res}: gold {Gold} silver {Silver} bronze {Bronze}, bonus {BonusItem}, waiting prize {(idx >= 0 ? $"row {step} idx {idx}" : "none")}");
                    if (res != 0) { _chat(Lang.T("The roulette can't open now."), "ffb070"); return true; }
                    IsOpen = true; _win.Open(_head()); Refresh();
                    return true;
                }
                case 0x0a1c:
                {
                    r.Skip(4); r.U32(); int got = 0;
                    while (r.Remaining >= 12)
                    {
                        int row = r.U16(), pos = r.U16(); int item = (int)r.U32(); int cnt = r.U16(); r.U16();
                        if (row >= 0 && row < Rows && pos >= 0 && pos < Table[row].Length) { Table[row][pos] = (item, cnt); got++; }
                    }
                    TableReceived = true; Debug.Log($"[Roulette] table: {got} prizes");
                    if (IsOpen) Refresh();
                    return true;
                }
                case 0x0a1e: { r.Skip(2); Debug.Log($"[Roulette] close ack {r.U8()}"); return true; } // never sent by rAthena (see above)
                case 0x0a20:
                {
                    r.Skip(2); int res = r.U8(); int step = r.U16(); int idx = r.U16(); BonusItem = (int)r.U32(); Gold = r.S32(); Silver = r.S32(); Bronze = r.S32();
                    LastSpin = res; Spins++;
                    // an accepted spin first gives a prize still waiting (clif_roulette_getitem, clif.cpp:21050-21053): it is not waiting any more
                    if (PrizeIdx >= 0) { Debug.Log($"[Roulette] the waiting prize (row {PrizeRow} idx {PrizeIdx}) was given by this spin"); GivenBySpin++; }
                    PrizeRow = -1; PrizeIdx = -1;
                    if (res == 0 || res == 3)
                    {
                        PrizeRow = step; PrizeIdx = idx;
                        // sd->roulette.tick = now + max(1, 9 - row - 3) s (clif.cpp:21084); +10 % and 0.5 s because the server's clock may run
                        // slower than ours (the WSL test copies' clock ran 3.9 % slow, measured 2026-09-28) — a claim sent early is ignored
                        _readyAt = Time.time + Mathf.Max(1, Columns - step - 3) * 1.1f + 0.5f;
                        var (item, cnt) = step < Rows && idx < Table[step].Length ? Table[step][idx] : (0, 0);
                        string nm = item > 0 ? GameDb.GetItem(item).Name : "?";
                        _chat(res == 0 ? Lang.F("Roulette: you won {0} x{1} on row {2}!", nm, cnt, step + 1) : Lang.F("Roulette: a losing slot on row {0} ({1} x{2}).", step + 1, nm, cnt), res == 0 ? "a8e6a3" : "ffb070");
                        Wheel?.Spin(step, idx, Wait); // client wave 5 (w5ui): the wheel turns until the server takes a claim
                    }
                    else _chat(res == 2 ? Lang.T("Roulette: you don't have enough coins for a spin.") : Lang.T("Roulette: the spin failed."), "ffb070");
                    Debug.Log($"[Roulette] spin result {res}: row {step} idx {idx}, bonus {BonusItem}, gold {Gold} silver {Silver} bronze {Bronze}");
                    Refresh();
                    return true;
                }
                case 0x0a22:
                {
                    r.Skip(2); int res = r.U8(); BonusItem = (int)r.U32(); LastReceive = res; Receives++; _claimSentAt = -10f;
                    if (res == 0) { PrizeRow = -1; PrizeIdx = -1; }
                    _chat(res == 0 ? Lang.T("Roulette: prize received.") : res == 2 ? Lang.T("Roulette: you can't carry more items.") : res == 3 ? Lang.T("Roulette: the prize is too heavy for you now.") : Lang.T("Roulette: there is nothing to receive."), res == 0 ? "a8e6a3" : "ffb070");
                    Debug.Log($"[Roulette] receive result {res}, bonus {BonusItem}");
                    Refresh();
                    return true;
                }
                default: return false;
            }
        }

        /// <summary>Seconds until the server accepts the next spin / claim (0 = now).</summary>
        public float Wait => Mathf.Max(0f, _readyAt - Time.time);
        internal void Tick() { if (IsOpen && _readyAt > 0 && Time.time - _readyAt < 1.5f) Refresh(); }

        public void Refresh()
        {
            if (!IsOpen) return;
            if (Wheel != null && !Wheel.Spinning) Wheel.ShowRow(RouletteWheel.IdleRow(this)); // client wave 5 (w5ui)
            var rows = new List<ListWindow.Row>();
            for (int row = 0; row < Rows; row++)
            {
                var sb = new StringBuilder();
                sb.Append("<color=#ffd060>").Append(Lang.F("Row {0}", row + 1)).Append("</color>  ");
                for (int i = 0; i < Table[row].Length; i++)
                {
                    var (item, cnt) = Table[row][i];
                    if (i > 0) sb.Append(" · ");
                    string nm = item > 0 ? GameDb.GetItem(item).Name : "…";
                    if (nm.Length > 15) nm = nm.Substring(0, 14) + "…";
                    bool won = row == PrizeRow && i == PrizeIdx, bonus = item > 0 && item == BonusItem;
                    if (won) sb.Append("<color=#7fd67f><b>");
                    else if (bonus) sb.Append("<color=#ffb347>");
                    sb.Append(nm); if (cnt > 1) sb.Append(" x").Append(cnt);
                    if (won) sb.Append("</b></color>"); else if (bonus) sb.Append("</color>");
                }
                rows.Add(new ListWindow.Row { Text = "<size=19>" + sb + "</size>", Highlight = row == PrizeRow });
            }
            if (!TableReceived) rows.Insert(0, new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>Loading the prizes…</color>") });
            // the coins in the title, the state in the info line: the seven rows then fit one page
            _win.SetTitle(Lang.F("Roulette   <size=20><color=#ffd060>gold {0} · silver {1} · bronze {2}</color></size>", Gold, Silver, Bronze));
            string state;
            if (PrizeIdx >= 0 && PrizeRow >= 0 && PrizeRow < Rows && PrizeIdx < Table[PrizeRow].Length)
            {
                var (item, cnt) = Table[PrizeRow][PrizeIdx];
                state = Wait > 0 ? Lang.F("<color=#7fd67f>The wheel is turning… {0:0} s</color>", Mathf.Ceil(Wait))
                                 : Lang.F("<color=#7fd67f>Prize: {0} x{1}. Receive it, or spin again (that gives it to you first).</color>", item > 0 ? GameDb.GetItem(item).Name : "?", cnt);
            }
            else if (BonusItem > 0) state = Lang.F("Bonus: {0}, doubled if you win it. A spin: 1 bronze, else 10 silver (row 3) or 10 gold (row 5).", GameDb.GetItem(BonusItem).Name);
            else state = Lang.T("A spin uses 1 bronze coin; without bronze, 10 silver (from row 3) or 10 gold (from row 5).");
            _win.SetInfo(state);
            _win.SetTabs(null, 0, null); _win.SetInput(false);
            bool ready = Wait <= 0f; bool claiming = Time.time - _claimSentAt < 3f;
            _win.SetFooter((Lang.T("Close"), Close), (ready && PrizeIdx >= 0 && !claiming ? Lang.T("Receive") : null, Receive), (ready ? Lang.T("Spin") : null, Spin));
            _win.SetRows(rows);
        }

    }

    /// <summary>Redraws the roulette's countdown while the wheel turns (the window's own GameObject).</summary>
    internal sealed class RouletteTicker : MonoBehaviour
    {
        public Roulette Owner; private float _next;
        private void Update() { if (Owner == null || Time.time < _next) return; _next = Time.time + 0.5f; Owner.Tick(); }
    }
}
