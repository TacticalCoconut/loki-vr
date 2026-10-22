using System;
using System.Collections.Generic;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Zeny bank (research/windows9_packets.md §5; rAthena e985006). The vault is per account (#BANKVAULT) and the server never pushes it.
    /// Open 0x09b6 {AID} → 0x09b7 (sets state.banking, which blocks storage), balance 0x09ab → 0x09a6 {money.Q, reason.W}, deposit 0x09a7
    /// {AID, zeny.L} → 0x09a8 {reason.W, money.Q, zeny.L} (0 ok, 1 error, 2 not enough zeny, 3 overflow / amount ≤ 0), withdraw 0x09a9 → 0x09aa
    /// (0 ok, 1 not enough in the vault, 2 error), close 0x09b8 → 0x09b9. Every request carries the AID; a wrong one is silently ignored.
    /// The `openbank` script command opens it with 0x0ae2 type 0.
    /// </summary>
    public sealed class Bank
    {
        public long Vault = -1; public long Zeny = -1;
        public bool Open { get; private set; }
        public int LastDeposit = -1, LastWithdraw = -1;
        public event Action Changed;

        private readonly RoSession _s; private readonly ListWindow _win; private readonly Func<Camera> _head; private readonly Func<long> _zeny; private readonly Action<string, string> _chat;

        public Bank(RoSession s, ListWindow win, Func<Camera> head, Func<long> zeny, Action<string, string> chat)
        {
            _s = s; _win = win; _head = head; _zeny = zeny; _chat = chat;
            _win.OnCloseClicked = Hide;
        }

        public void Reset() { Open = false; Vault = -1; _win.Hide(); }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x0ae2: // ZC_UI_OPEN type.B data.L — type 0 = the bank (script openbank)
                {
                    r.Skip(2); int type = r.U8();
                    if (type != 0) return false;
                    Show(); return true;
                }
                case 0x09b7: Open = true; Debug.Log("[Bank] open"); _s.SendPacket(new PacketWriter(0x09ab).U32(_s.AccountId).ToArray()); Changed?.Invoke(); return true;
                case 0x09b9: Open = false; Debug.Log("[Bank] closed"); Changed?.Invoke(); return true;
                case 0x09a6: // ZC_BANKING_CHECK money.Q reason.W
                {
                    r.Skip(2); Vault = r.S64(); Debug.Log($"[Bank] vault {Vault}"); Changed?.Invoke(); Refresh(); return true;
                }
                case 0x09a8: // ZC_ACK_BANKING_DEPOSIT reason.W money.Q zeny.L
                {
                    r.Skip(2); LastDeposit = r.U16(); Vault = r.S64(); Zeny = r.S32();
                    if (LastDeposit != 0) _chat(LastDeposit == 2 ? Lang.T("You don't have that much zeny.") : LastDeposit == 3 ? Lang.T("The bank can't take that amount.") : Lang.T("The bank refused the deposit."), "ffb070");
                    Debug.Log($"[Bank] deposit result {LastDeposit}, vault {Vault}, zeny {Zeny}");
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x09aa: // ZC_ACK_BANKING_WITHDRAW reason.W money.Q zeny.L
                {
                    r.Skip(2); LastWithdraw = r.U16(); Vault = r.S64(); Zeny = r.S32();
                    if (LastWithdraw != 0) _chat(LastWithdraw == 1 ? Lang.T("There isn't that much in the bank.") : Lang.T("The bank refused the withdrawal."), "ffb070");
                    Debug.Log($"[Bank] withdraw result {LastWithdraw}, vault {Vault}, zeny {Zeny}");
                    Changed?.Invoke(); Refresh(); return true;
                }
                default: return false;
            }
        }

        public void Deposit(long amount)
        {
            if (amount <= 0) { _chat(Lang.T("Type an amount first."), "ffb070"); return; }
            LastDeposit = -1; _s.SendPacket(new PacketWriter(0x09a7).U32(_s.AccountId).S32((int)Math.Min(amount, int.MaxValue)).ToArray()); Debug.Log($"[Bank] deposit {amount}");
        }
        public void Withdraw(long amount)
        {
            if (amount <= 0) { _chat(Lang.T("Type an amount first."), "ffb070"); return; }
            LastWithdraw = -1; _s.SendPacket(new PacketWriter(0x09a9).U32(_s.AccountId).S32((int)Math.Min(amount, int.MaxValue)).ToArray()); Debug.Log($"[Bank] withdraw {amount}");
        }

        // ------------------------------------------------------------------ window
        public bool Visible => _win.Visible;
        public void Show()
        {
            _win.SetTabs(null, 0, null);
            _win.SetInput(true, Lang.T("amount (zeny)"), c => char.IsDigit(c), () => Refresh(), "", 10);
            _win.Open(_head());
            _s.SendPacket(new PacketWriter(0x09b6).U32(_s.AccountId).ToArray());
            Refresh();
        }
        public void Hide()
        {
            if (Open) _s.SendPacket(new PacketWriter(0x09b8).U32(_s.AccountId).ToArray()); // storage stays blocked while the bank is open (storage.cpp:1144)
            _win.SetInput(false); _win.Hide();
        }
        private long Typed() => long.TryParse(_win.Input.Text, out var v) ? v : 0;

        public void Refresh()
        {
            if (!_win.Visible) return;
            long zeny = _zeny();
            _win.SetTitle(Lang.T("Bank"));
            _win.SetInfo((Vault >= 0 ? Lang.F("In the bank {0:N0} z", Vault) : Lang.T("In the bank … z")) + "  ·  " + Lang.F("on hand {0:N0} z", zeny) + "  ·  " + Lang.T("amount:"));
            var rows = new List<ListWindow.Row>();
            long typed = Typed();
            rows.Add(new ListWindow.Row { Text = typed > 0 ? $"<color=#ffd060>{typed:N0} z</color>  " + Lang.T("<color=#9aa3b5>(typed)</color>") : Lang.T("<color=#9aa3b5>Type an amount above, or pick one below.</color>"), A = Lang.T("Deposit"), OnA = () => Deposit(Typed()), B = Lang.T("Withdraw"), OnB = () => Withdraw(Typed()) }); // read the box at click time: typing doesn't refresh the rows
            foreach (long amt in new long[] { 1000, 10000, 100000, 1000000 })
            {
                long a = amt;
                rows.Add(new ListWindow.Row { Text = $"{a:N0} z", A = Lang.T("Deposit"), OnA = () => Deposit(a), AEnabled = zeny >= a, B = Lang.T("Withdraw"), OnB = () => Withdraw(a), BEnabled = Vault >= a });
            }
            rows.Add(new ListWindow.Row { Text = Lang.T("Everything"), A = Lang.T("Deposit all"), OnA = () => Deposit(_zeny()), AEnabled = zeny > 0, B = Lang.T("Withdraw all"), OnB = () => Withdraw(Vault), BEnabled = Vault > 0 });
            _win.SetRows(rows);
            _win.SetFooter((Lang.T("Close"), Hide));
        }
    }
}
