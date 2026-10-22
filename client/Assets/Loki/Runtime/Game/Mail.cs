using System;
using System.Collections.Generic;
using System.Linq;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// RODEX mail (research/windows9_packets.md §1; rAthena e985006, PACKETVER 20211103).
    /// List: open 0x0ac0 / refresh 0x0ac1 → 0x0ac2 (inbox and returned mail in one packet, newest first; also pushed whenever mail arrives,
    /// is read or emptied). Read 0x09ea → 0x0b63 (body + zeny + items; unknown id → 0x0274). Take zeny 0x09f1 → 0x09f2, items 0x09f3 → 0x09f4
    /// (0 ok; 1 zeny cap; 2 no room). Delete 0x09f5 → 0x09f6 — silently refused while zeny or items remain. Unread flag 0x09e7.
    /// Write: 0x0a08 → 0x0a12 {1 ok} starts the server's writing state (clears the draft; blocks walking, item use and NPC clicks),
    /// attach 0x0a04 → 0x0b3f (0 ok, 1 overweight, 2 error, 3 five items already, 4 untradeable; 0x0291 3015 for a swap-set item),
    /// detach 0x0a06 → 0x0a07, recipient check 0x0b97 → 0x0a51 (char id 0 = no such character), send 0x0a6e (68-byte header, title ≤ 40 and
    /// body ≤ 500 bytes with the NUL, never an empty title: the server would keep the attachments and answer nothing) → 0x09ed {0 ok, 1 failed,
    /// 2 daily limit}. The writing state only ends with 0x0a03 or a map change, so Loki sends 0x0a03 whenever it leaves the write page.
    /// Returning a mail is not possible in this build (0x0b98 is ignored); mail is returned by the char server after 15 days.
    /// </summary>
    public sealed class Mail
    {
        public sealed class Letter { public int Type; public long Id; public bool Read; public int Flags; public string Sender = "", Title = ""; public uint ExpireSecs; public float SeenAt; public bool HasZeny => (Flags & 2) != 0; public bool HasItems => (Flags & 4) != 0; public bool FromNpc => (Flags & 8) != 0; }
        public sealed class Attached { public int ItemId, Count, Refine, Grade; public bool Identified, Damaged; public int[] Cards = new int[4]; }
        public sealed class Opened { public int Type; public long Id; public string Text = ""; public long Zeny; public readonly List<Attached> Items = new List<Attached>(); }

        public readonly List<Letter> Letters = new List<Letter>();
        public bool Unread { get; private set; }
        public bool ListReceived { get; private set; }
        public Opened Reading { get; private set; }
        public int LastSend = -1, LastTakeZeny = -1, LastTakeItems = -1, LastAttach = -1; public long LastDeleted;
        public (uint charId, int job, int level, string name)? Receiver;
        public event Action Changed;
        public Action<string> Toast;

        // ---- the draft
        public string To = "", Subject = "", Body = ""; public long Zeny;
        public readonly List<(int index, int itemId, int amount)> Attachments = new List<(int, int, int)>();
        public bool Writing { get; private set; }
        private bool _writeRequested;

        private readonly RoSession _s; private readonly Inventory _inv; private readonly ListWindow _win; private readonly Func<Camera> _head; private readonly Func<long> _zeny; private readonly Action<string, string> _chat;

        public Mail(RoSession s, Inventory inv, ListWindow win, Func<Camera> head, Func<long> zeny, Action<string, string> chat)
        {
            _s = s; _inv = inv; _win = win; _head = head; _zeny = zeny; _chat = chat;
            _win.OnCloseClicked = Hide;
            _inv.Changed += () => { if (_win.Visible && (Current == Page.Write)) Refresh(); };
        }

        public void ResetForCharacter() { Letters.Clear(); Unread = false; ListReceived = false; Reading = null; ClearDraft(); Writing = false; _writeRequested = false; _win.Hide(); Changed?.Invoke(); }
        /// <summary>A map change ends the server's writing state (pc.cpp:6954).</summary>
        public void OnMapChange() { Writing = false; _writeRequested = false; Attachments.Clear(); if (_win.Visible && Current == Page.Write) Refresh(); }

        // ------------------------------------------------------------------ packets
        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x09e7: // ZC_NOTIFY_UNREADMAIL flag.B
                {
                    r.Skip(2); bool u = r.U8() != 0;
                    if (u && !Unread) { _chat(Lang.T("You have unread mail."), "9ad1ff"); Toast?.Invoke(Lang.T("You have mail")); }
                    Unread = u; Changed?.Invoke(); return true;
                }
                case 0x0ac2: // ZC_ACK_MAIL_LIST3 len.W isEnd.B {type.B id.Q read.B flags.B sender[24] expire.L titleLen.W title}*
                {
                    r.Skip(5); Letters.Clear();
                    while (r.Remaining >= 41)
                    {
                        var l = new Letter { Type = r.U8(), Id = r.S64(), Read = r.U8() != 0, Flags = r.U8(), Sender = ClientText.Expand(r.Str(24)), ExpireSecs = r.U32(), SeenAt = Time.time };
                        int tl = r.U16(); if (tl > r.Remaining) tl = r.Remaining;
                        l.Title = tl > 0 ? ClientText.Expand(r.Text(tl)) : "";
                        Letters.Add(l);
                    }
                    ListReceived = true; Unread = Letters.Any(x => !x.Read);
                    Debug.Log($"[Mail] list: {Letters.Count} ({Letters.Count(x => !x.Read)} unread)");
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x0b63: // ZC_ACK_READ_RODEX len.W type.B id.Q textLen.W zeny.Q count.B text {60}*
                {
                    r.Skip(4); var m = new Opened { Type = r.U8(), Id = r.S64() }; int tl = r.U16(); m.Zeny = r.S64(); int cnt = r.U8();
                    m.Text = tl > 0 ? ClientText.Expand(r.Text(Math.Min(tl, r.Remaining))) : "";
                    for (int i = 0; i < cnt && r.Remaining >= 60; i++)
                    {
                        var a = new Attached { Count = r.S16(), ItemId = (int)r.U32(), Identified = r.U8() != 0, Damaged = r.U8() != 0 };
                        for (int c = 0; c < 4; c++) a.Cards[c] = (int)r.U32();
                        r.Skip(4 + 1 + 2 + 2 + 25); a.Refine = r.U8(); a.Grade = r.U8(); // location, type, viewSprite, bindOnEquip, 5 options
                        m.Items.Add(a);
                    }
                    Reading = m; var l = Letters.Find(x => x.Id == m.Id); if (l != null) l.Read = true;
                    Debug.Log($"[Mail] read {m.Id}: {m.Text.Length} chars, {m.Zeny} z, {m.Items.Count} items");
                    Current = Page.Inbox; Changed?.Invoke(); Refresh(); return true;
                }
                case 0x0274: // ZC_ACK_MAIL_RETURN mailId.L result.W — here: that mail isn't in the server's cache
                {
                    r.Skip(2); uint mid = r.U32(); _chat(Lang.T("That mail is gone."), "ffb070"); Debug.Log($"[Mail] 0x0274 {mid}"); Refresh(); return true;
                }
                case 0x09f2: // ZC_ACK_ZENY_FROM_MAIL id.Q type.B result.B
                case 0x09f4: // ZC_ACK_ITEM_FROM_MAIL
                {
                    r.Skip(2); long mid = r.S64(); r.Skip(1); int res = r.U8();
                    if (id == 0x09f2) { LastTakeZeny = res; if (res == 0 && Reading != null && Reading.Id == mid) Reading.Zeny = 0; }
                    else { LastTakeItems = res; if (res == 0 && Reading != null && Reading.Id == mid) Reading.Items.Clear(); }
                    _chat(res == 0 ? (id == 0x09f2 ? Lang.T("You took the zeny.") : Lang.T("You took the items.")) : res == 1 ? Lang.T("That would put you over the zeny limit.") : Lang.T("You can't carry those items (weight, amount or slots)."), res == 0 ? "a8e6a3" : "ffb070");
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x09f6: // ZC_ACK_DELETE_MAIL type.B id.Q
                {
                    r.Skip(3); long mid = r.S64(); LastDeleted = mid;
                    Letters.RemoveAll(x => x.Id == mid); if (Reading != null && Reading.Id == mid) Reading = null;
                    Debug.Log($"[Mail] deleted {mid}"); Changed?.Invoke(); Refresh(); return true;
                }
                case 0x0a12: // ZC_ACK_OPEN_WRITE_MAIL name[24] result.B (1 = may write)
                {
                    r.Skip(26); bool ok = r.U8() != 0; _writeRequested = false;
                    Writing = ok; if (ok) Attachments.Clear();
                    if (!ok) _chat(Lang.T("You can't write mail right now (storage or a trade is open)."), "ffb070");
                    Debug.Log($"[Mail] write mode {ok}"); Refresh(); return true;
                }
                case 0x0b3f: // ZC_ACK_ADD_ITEM_RODEX result.B index.W count.W itemId.L … weight.W@55
                {
                    r.Skip(2); int res = r.U8(); int idx = r.U16(); int cnt = r.U16(); int item = (int)r.U32(); LastAttach = res;
                    if (res == 0)
                    {
                        int k = Attachments.FindIndex(x => x.index == idx);
                        if (k >= 0) Attachments[k] = (idx, item, Attachments[k].amount + cnt); else Attachments.Add((idx, item, cnt));
                    }
                    else _chat(res == 1 ? Lang.T("That's too heavy to mail (2,000 weight at most).") : res == 3 ? Lang.T("A mail holds at most 5 different items.") : res == 4 ? Lang.T("That item can't be mailed.") : Lang.T("The item couldn't be attached."), "ffb070");
                    Debug.Log($"[Mail] attach result {res}: index {idx} x{cnt} item {item}"); Refresh(); return true;
                }
                case 0x0a07: // ZC_ACK_REMOVE_ITEM_MAIL result.B index.W count.W weight.W
                {
                    r.Skip(2); bool ok = r.U8() != 0; int idx = r.U16(); int cnt = r.U16();
                    if (ok) { int k = Attachments.FindIndex(x => x.index == idx); if (k >= 0) { int left = Attachments[k].amount - cnt; if (left > 0) Attachments[k] = (idx, Attachments[k].itemId, left); else Attachments.RemoveAt(k); } }
                    Refresh(); return true;
                }
                case 0x0a51: // ZC_CHECKNAME charId.L class.W level.W name[24]
                {
                    r.Skip(2); uint cid = r.U32(); int job = r.U16(); int lv = r.U16(); string nm = r.Str(24);
                    Receiver = (cid, job, lv, nm);
                    if (cid == 0) _chat(Lang.F("There's no character called \"{0}\".", nm), "ffb070");
                    Debug.Log($"[Mail] receiver '{nm}': char {cid} job {job} lv {lv}"); Refresh(); return true;
                }
                case 0x09ed: // ZC_ACK_WRITE_MAIL result.B
                {
                    r.Skip(2); LastSend = r.U8();
                    if (LastSend == 0)
                    {
                        _chat(Lang.F("Mail sent to {0}.", To), "a8e6a3");
                        CancelWrite(); ClearDraft(); Current = Page.Inbox; Refresh(ListRefresh: true);
                    }
                    else { _chat(LastSend == 2 ? Lang.T("You've reached today's mail limit.") : Lang.T("The mail couldn't be sent (check the name, zeny and fees)."), "ffb070"); Refresh(); }
                    Debug.Log($"[Mail] send result {LastSend}"); Changed?.Invoke(); return true;
                }
                default: return false;
            }
        }

        // ------------------------------------------------------------------ sending
        public void RequestList(bool reload = false)
        {
            var w = new PacketWriter(reload ? (ushort)0x0ac1 : (ushort)0x0ac0).U64(0);
            if (reload) w.Zero(16); else w.U64(0).U64(0);
            _s.SendPacket(w.ToArray());
        }
        public void Read(Letter l) { if (l != null) _s.SendPacket(new PacketWriter(0x09ea).U8((byte)l.Type).U64((ulong)l.Id).ToArray()); }
        public void TakeZeny(Opened m) { if (m != null && m.Zeny > 0) { LastTakeZeny = -1; _s.SendPacket(new PacketWriter(0x09f1).U64((ulong)m.Id).U8((byte)m.Type).ToArray()); } }
        public void TakeItems(Opened m) { if (m != null && m.Items.Count > 0) { LastTakeItems = -1; _s.SendPacket(new PacketWriter(0x09f3).U64((ulong)m.Id).U8((byte)m.Type).ToArray()); } }
        public void Delete(int type, long mailId) { _s.SendPacket(new PacketWriter(0x09f5).U8((byte)type).U64((ulong)mailId).ToArray()); Debug.Log($"[Mail] delete {mailId}"); }

        public void BeginWrite()
        {
            if (Writing || _writeRequested) return;
            _writeRequested = true; _s.SendPacket(new PacketWriter(0x0a08).Str(To, 24).ToArray());
        }
        /// <summary>Ends the server's writing state (it survives a successful send) — always sent when leaving the write page.</summary>
        public void CancelWrite()
        {
            if (!Writing && !_writeRequested) return;
            Writing = false; _writeRequested = false; Attachments.Clear();
            _s.SendPacket(new PacketWriter(0x0a03).ToArray()); Debug.Log("[Mail] write mode off");
        }
        public void CheckName(string name) { if (!string.IsNullOrWhiteSpace(name)) { Receiver = null; _s.SendPacket(new PacketWriter(0x0b97).Str(name.Trim(), 24).U8(0).ToArray()); } }
        public void Attach(int index, int amount) { if (!Writing) { _chat(Lang.T("Wait a moment — the mail form is opening."), "ffb070"); BeginWrite(); return; } LastAttach = -1; _s.SendPacket(new PacketWriter(0x0a04).U16((ushort)index).U16((ushort)Mathf.Clamp(amount, 1, 30000)).ToArray()); }
        public void Detach(int index, int amount) { _s.SendPacket(new PacketWriter(0x0a06).U16((ushort)index).U16((ushort)amount).ToArray()); }
        public void ClearDraft() { To = ""; Subject = ""; Body = ""; Zeny = 0; Receiver = null; Attachments.Clear(); }

        /// <summary>Client wave 4 (w4items, re-audit §3): the fee is NOT in any packet and the server alone decides it (rAthena charges
        /// its battle config's mail_attachment_price per item when the mail is sent and mail_zeny_fee % of the zeny: mail.cpp:59-63,
        /// 114-132, 313; a mail it can't pay fails with ZC_ACK_WRITE_MAIL 1). What is shown here is only the official client's own
        /// estimate, the RODEX window's constants (2,500 z per item, 2 % of the zeny; roBrowserLegacy WriteRodex.js:306-318, a
        /// reimplementation: the only source available). It never blocks a send: another server's fees may differ.</summary>
        public const long EstimateFeePerItem = 2500; public const int EstimateZenyFeePercent = 2;
        public long EstimatedFees => Attachments.Count * EstimateFeePerItem + Zeny * EstimateZenyFeePercent / 100;
        public string SendBlocker()
        {
            if (string.IsNullOrWhiteSpace(To)) return Lang.T("Who is it for?");
            if (string.IsNullOrWhiteSpace(Subject)) return Lang.T("Give it a title.");
            if (Zeny > _zeny()) return Lang.T("You don't have that much zeny."); // the zeny itself (the fees are the server's call: see EstimatedFees)
            if (!Writing) return Lang.T("The mail form isn't open yet.");
            return null;
        }
        public void Send()
        {
            string why = SendBlocker();
            if (why != null) { _chat(why, "ffb070"); if (!Writing) BeginWrite(); return; }
            // one byte per character (Net/RoText.cs: cp1252), so the byte counts are the lengths
            string title = Subject.Trim(); if (RoText.Encode(title).Length > 39) title = title.Substring(0, 39);
            string body = Body ?? ""; if (RoText.Encode(body).Length > 499) body = body.Substring(0, 499);
            var w = new PacketWriter(0x0a6e).U16(0).Str(To.Trim(), 24).Str("", 24).U64((ulong)Math.Max(0, Zeny))
                .U16((ushort)(RoText.Encode(title).Length + 1)).U16((ushort)(RoText.Encode(body).Length + 1)).U32(0).StrZ(title).StrZ(body);
            LastSend = -1; _s.SendPacket(w.ToArrayWithLength());
            Debug.Log($"[Mail] send to '{To}': '{title}' ({body.Length} chars), {Zeny} z, {Attachments.Count} items");
        }

        // ------------------------------------------------------------------ window
        public enum Page { Inbox, Write }
        public Page Current { get; private set; } = Page.Inbox;
        public bool Visible => _win.Visible;
        private static readonly string[] TabNames = { Lang.N("Inbox"), Lang.N("Write") };
        private enum Field { None, To, Title, Body, Zeny }
        private Field _edit = Field.None;
        private bool _picking; private string _confirmKey; private float _confirmUntil;

        public void Show(Page p = Page.Inbox)
        {
            _picking = false; _edit = Field.None;
            SwitchTo(p);
            _win.Open(_head());
            if (p == Page.Inbox) RequestList();
            Refresh();
        }
        public void Hide() { CommitEdit(); CancelWrite(); _win.SetInput(false); _win.Hide(); }
        private void SwitchTo(Page p)
        {
            if (Current == Page.Write && p != Page.Write) { CommitEdit(); CancelWrite(); }
            Current = p; _picking = false;
            if (p == Page.Write) BeginWrite();
        }
        private bool Confirm(string key, string what)
        {
            if (_confirmKey == key && Time.time <= _confirmUntil) { _confirmKey = null; return true; }
            _confirmKey = key; _confirmUntil = Time.time + 4f; _chat(Lang.F("{0} — press again to confirm.", what), "ffd060"); Refresh();
            return false;
        }
        private string Armed(string key, string label) => _confirmKey == key && Time.time <= _confirmUntil ? Lang.T("Sure?") : label;

        private void EditField(Field f)
        {
            CommitEdit(); _edit = f;
            string cur = f == Field.To ? To : f == Field.Title ? Subject : f == Field.Body ? Body : f == Field.Zeny ? (Zeny > 0 ? Zeny.ToString() : "") : "";
            string ph = f == Field.To ? Lang.T("character name") : f == Field.Title ? Lang.T("title") : f == Field.Body ? Lang.T("message") : "zeny";
            _win.SetInput(true, ph, f == Field.Zeny ? (Func<char, bool>)(c => char.IsDigit(c)) : null, () => { CommitEdit(); Refresh(); }, cur, f == Field.To ? 23 : f == Field.Title ? 39 : f == Field.Zeny ? 10 : 400);
            _win.Input.Focus();
        }
        private void CommitEdit()
        {
            if (_edit == Field.None) return;
            string t = _win.Input.Text ?? "";
            switch (_edit)
            {
                case Field.To: if (t.Trim() != To) { To = t.Trim(); Receiver = null; if (To.Length > 0) CheckName(To); } break;
                case Field.Title: Subject = t; break;
                case Field.Body: Body = t; break;
                case Field.Zeny: Zeny = long.TryParse(t, out var z) ? Math.Max(0, z) : 0; break;
            }
            _edit = Field.None; _win.SetInput(false);
        }


        public void Refresh(bool ListRefresh = false)
        {
            if (ListRefresh) RequestList(true);
            if (!_win.Visible) return;
            _win.SetTabs(Array.ConvertAll(TabNames, Lang.T), (int)Current, t => { SwitchTo((Page)t); if (Current == Page.Inbox) RequestList(); Refresh(); });
            _win.SetTitle(Lang.T("Mail"));
            var rows = new List<ListWindow.Row>();
            if (Current == Page.Inbox && Reading != null) RefreshRead(rows);
            else if (Current == Page.Inbox) RefreshInbox(rows);
            else if (_picking) RefreshPicker(rows);
            else RefreshWrite(rows);
            _win.SetRows(rows);
        }

        private static string Ago(Letter l)
        {
            long left = (long)l.ExpireSecs - (long)(Time.time - l.SeenAt);
            if (l.ExpireSecs >= 365u * 24 * 3600 - 60) return "";
            return left > 86400 ? " · " + (left / 86400 == 1 ? Lang.T("1 days left") : Lang.F("{0} days left", left / 86400)) : left > 3600 ? " · " + (left / 3600 == 1 ? Lang.T("1 h left") : Lang.F("{0} h left", left / 3600)) : " · " + Lang.T("expires soon"); // w4lang: "1 days left" is the English as it was; the Portuguese needs the singular
        }

        private void RefreshInbox(List<ListWindow.Row> rows)
        {
            if (_edit != Field.None) CommitEdit(); _win.SetInput(false);
            int unread = Letters.Count(x => !x.Read);
            _win.SetInfo(!ListReceived ? Lang.T("Loading…") : Letters.Count == 0 ? Lang.T("No mail.") : (Letters.Count > 1 ? Lang.F("{0} mails", Letters.Count) : Lang.T("1 mail")) + (unread > 0 ? ", " + (unread == 1 ? Lang.T("1 unread") : Lang.F("{0} unread", unread)) : "")); // w4lang: the count and the unread count are separate texts
            foreach (var l in Letters)
            {
                var ll = l; bool attach = l.HasZeny || l.HasItems;
                string tags = (l.HasZeny ? " · zeny" : "") + (l.HasItems ? " · " + Lang.T("~mail~items") : "") + (l.Type == 2 ? " · " + Lang.T("~mail~returned") : "") + Ago(l);
                rows.Add(new ListWindow.Row
                {
                    Highlight = !l.Read,
                    Text = $"{(l.Read ? "" : "<color=#ffd060>● </color>")}{l.Title}\n<size=19><color=#9aa3b5>{(l.FromNpc && l.Sender.Length == 0 ? Lang.T("from the server") : Lang.F("from {0}", l.Sender))}{tags}</color></size>",
                    A = Lang.T("Open"), OnA = () => Read(ll),
                    B = attach ? null : Armed("del" + l.Id, Lang.T("Delete")), OnB = () => { if (Confirm("del" + ll.Id, Lang.F("Delete \"{0}\"", ll.Title))) Delete(ll.Type, ll.Id); },
                });
            }
            _win.SetFooter((Lang.T("Refresh"), () => RequestList(true)), (Lang.T("Write"), () => { SwitchTo(Page.Write); Refresh(); }), (Lang.T("Close"), Hide));
        }

        private void RefreshRead(List<ListWindow.Row> rows)
        {
            var m = Reading; var l = Letters.Find(x => x.Id == m.Id);
            _win.SetInput(false);
            _win.SetInfo(l != null ? (l.Sender.Length > 0 ? Lang.F("{0} — from {1}", l.Title, l.Sender) : Lang.F("{0} — from the server", l.Title)) : Lang.T("Mail"));
            // the body, in chunks a row can hold
            string text = m.Text.Replace("\r", "");
            var chunks = new List<string>(); var sb = new System.Text.StringBuilder();
            foreach (var word in text.Split(' '))
            {
                if (sb.Length + word.Length > 110 || word.Contains('\n')) { if (sb.Length > 0) chunks.Add(sb.ToString()); sb.Clear(); }
                sb.Append(sb.Length > 0 ? " " : "").Append(word.Replace("\n", " "));
            }
            if (sb.Length > 0) chunks.Add(sb.ToString());
            foreach (var c in chunks) rows.Add(new ListWindow.Row { Text = $"<size=20>{c}</size>" });
            if (m.Zeny > 0) rows.Add(new ListWindow.Row { Text = Lang.F("<color=#ffd060>{0:N0} zeny</color> attached", m.Zeny), A = Lang.T("Take"), OnA = () => TakeZeny(m), Highlight = true });
            foreach (var a in m.Items)
            {
                var info = GameDb.GetItem(a.ItemId);
                rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(info), Text = $"{(a.Refine > 0 ? "+" + a.Refine + " " : "")}{info.Name}{(a.Count > 1 ? $"  <color=#ffd060>x{a.Count}</color>" : "")}", Highlight = true });
            }
            bool attach = m.Zeny > 0 || m.Items.Count > 0;
            _win.SetFooter((Lang.T("Back"), () => { Reading = null; Refresh(); }),
                           (m.Items.Count > 0 ? Lang.T("Take items") : null, () => TakeItems(m)),
                           (attach ? null : Armed("delr", Lang.T("Delete")), () => { if (Confirm("delr", Lang.T("Delete this mail"))) Delete(m.Type, m.Id); }),
                           (l != null && l.Sender.Length > 0 && !l.FromNpc ? Lang.T("Reply") : null, () => { var who = l.Sender; var subj = l.Title.StartsWith("RE:") ? l.Title : "RE:" + l.Title; Reading = null; SwitchTo(Page.Write); To = who; Subject = subj.Length > 39 ? subj.Substring(0, 39) : subj; CheckName(who); Refresh(); }));
        }

        private void RefreshWrite(List<ListWindow.Row> rows)
        {
            if (_edit == Field.None) _win.SetInput(false);
            long fee = EstimatedFees;
            string feeLine = Lang.F("Fees about {0:N0} z (the server decides)  ·  you have {1:N0} z", fee, _zeny());
            if (Zeny + fee > _zeny()) feeLine = "<color=#ffb070>" + feeLine + "</color>";
            _win.SetInfo(_edit != Field.None ? (_edit == Field.To ? Lang.T("Type the name, then Enter:") : _edit == Field.Title ? Lang.T("Type the title, then Enter:") : _edit == Field.Body ? Lang.T("Type the message, then Enter:") : Lang.T("Type the zeny, then Enter:")) : Writing ? feeLine : Lang.T("Opening the mail form…"));
            string rcv = Receiver.HasValue ? (Receiver.Value.charId == 0 ? "  " + Lang.T("<color=#ff8a7a>no such character</color>") : "  " + Lang.F("<color=#7fd67f>✓ Lv {0} {1}</color>", Receiver.Value.level, LoginPanel.JobName(Receiver.Value.job))) : "";
            rows.Add(new ListWindow.Row { Text = Lang.F("To: <color=#ffd060>{0}</color>", To.Length > 0 ? To : "—") + rcv, A = Lang.T("Edit"), OnA = () => EditField(Field.To) });
            rows.Add(new ListWindow.Row { Text = Lang.F("Title: {0}", Subject.Length > 0 ? Subject : "<color=#9aa3b5>—</color>"), A = Lang.T("Edit"), OnA = () => EditField(Field.Title) });
            rows.Add(new ListWindow.Row { Text = Lang.F("Message: <size=19>{0}</size>", Body.Length > 0 ? (Body.Length > 90 ? Body.Substring(0, 90) + "…" : Body) : "<color=#9aa3b5>—</color>"), A = Lang.T("Edit"), OnA = () => EditField(Field.Body) });
            rows.Add(new ListWindow.Row { Text = Lang.F("Zeny: {0:N0}  <color=#9aa3b5>(about +{1} % fee)</color>", Zeny, EstimateZenyFeePercent), A = Lang.T("Edit"), OnA = () => EditField(Field.Zeny) });
            foreach (var (index, itemId, amount) in Attachments)
            {
                int idx = index, amt = amount; var info = GameDb.GetItem(itemId);
                rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(info), Text = $"{info.Name}{(amount > 1 ? $"  <color=#ffd060>x{amount}</color>" : "")}  <color=#9aa3b5>{Lang.F("(about +{0:N0} z)", EstimateFeePerItem)}</color>", B = Lang.T("Remove"), OnB = () => Detach(idx, amt) });
            }
            if (Attachments.Count < 5) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>Attach an item (up to 5)</color>"), A = Lang.T("Choose"), OnA = () => { CommitEdit(); _picking = true; Refresh(); }, AEnabled = Writing });
            _win.SetFooter((Lang.T("Send"), () => { CommitEdit(); Send(); }), (Lang.T("Clear"), () => { CommitEdit(); foreach (var a in Attachments.ToList()) Detach(a.index, a.amount); ClearDraft(); Refresh(); }), (Lang.T("Close"), Hide));
        }

        private void RefreshPicker(List<ListWindow.Row> rows)
        {
            _win.SetInput(false);
            _win.SetInfo(Lang.T("Choose an item to attach"));
            foreach (var e in _inv.Items.Values.Where(x => x.Wearing == 0 && !x.Bound && !x.Rental && (x.Info.TradeFlags & 64) == 0))
            {
                var ee = e; int already = Attachments.Where(a => a.index == e.Index).Sum(a => a.amount); int left = e.Amount - already;
                if (left <= 0) continue;
                rows.Add(new ListWindow.Row
                {
                    Icon = WristMenu.ItemIcon(e.Info), Text = $"{Inventory.FullName(e)}{(left > 1 ? $"  <color=#ffd060>x{left}</color>" : "")}",
                    A = Lang.T("Attach 1"), OnA = () => { Attach(ee.Index, 1); _picking = false; },
                    B = left > 1 ? Lang.T("All") : null, OnB = () => { Attach(ee.Index, left); _picking = false; },
                });
            }
            if (rows.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>Nothing you carry can be mailed.</color>") });
            _win.SetFooter((Lang.T("Back"), () => { _picking = false; Refresh(); }), (Lang.T("Close"), Hide));
        }
    }
}
