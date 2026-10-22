using System;
using System.Collections.Generic;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Party booking (client wave 2; rAthena e985006 party.cpp:1462-1573 + clif.cpp:13988-14160, research/booking_searchstore_cashshop_repute.md §A):
    ///   register CZ_PARTY_BOOKING_REQ_REGISTER 0x0802 level.W mapId.W job.W×6 (the server keeps only the low byte of each job, so only
    ///     ids below 256; 0xFFFF = none) → ZC_PARTY_BOOKING_ACK_REGISTER 0x0803 result.W (0 ok, 2 already registered) + 0x0809 to everyone;
    ///   search CZ_PARTY_BOOKING_REQ_SEARCH 0x0804 level.W mapId.W job.W lastIndex.L resultCount.W → ZC_PARTY_BOOKING_ACK_SEARCH 0x0805
    ///     len.W more.B {index.L name[24] time.L level.W mapId.W job.W×6}* — rAthena advances by sizeof(party_booking_ad_info): 48 bytes
    ///     on Windows / the module, 56 on a 64-bit Linux build (8 unwritten bytes after each record), so the stride is detected here;
    ///     level ≠ 0 keeps ads whose level is in [level-15, level]; mapId 0 + job -1 lists everything; ≤ 10 per page ("more" = next page
    ///     from the last index, which comes back once more);
    ///   delete CZ 0x0806 → 0x080b to everyone + ZC_PARTY_BOOKING_ACK_DELETE 0x0807 (always 0); update jobs CZ 0x0808 job.W×6 (no ack);
    ///   live: ZC_PARTY_BOOKING_NOTIFY_INSERT 0x0809 (as a record, 50 bytes), _UPDATE 0x080a index.L job.W×6, _DELETE 0x080b index.L.
    ///   Joining: the ads name a character, so the official client whispers them. The join request packets belong to the web
    ///   booking (SQL party_bookings): CZ_PARTY_REQ_MASTER_TO_JOIN 0x0ae6 leaderCID.L leaderAID.L → the leader gets
    ///   ZC_PARTY_REQ_MASTER_TO_JOIN 0x0ae7 cid.L aid.L name[24] baseLevel.W job.W and answers CZ_PARTY_REQ_ACK_MASTER_TO_JOIN 0x0af8
    ///   cid.L aid.L accept.B → the asker gets ZC_PARTY_JOIN_REQ_ACK_FROM_MASTER 0x0afa leader[24] party[24] leaderAID.L refused.L.
    /// Ads live until deleted or the owner logs out; there is no expiry.
    /// </summary>
    public sealed class PartyBooking
    {
        public sealed class Ad { public uint Index; public string Name = ""; public uint Time; public int Level, MapId; public int[] Jobs = new int[6]; }
        public readonly List<Ad> Results = new List<Ad>();
        public bool More { get; private set; }
        public Ad Mine { get; private set; }
        public int LastRegister = -1, LastDelete = -1, Stride; public int SearchesAnswered;
        /// <summary>A join request waiting for our answer as leader (cid, aid, name, level, job).</summary>
        public (uint cid, uint aid, string name, int level, int job)? JoinRequest;
        public string LastJoinAnswer = "";
        public event Action Changed;
        public Action<string> Toast; public Action<string> OpenWhisper;

        private readonly RoSession _s; private readonly ListWindow _win; private readonly Func<Camera> _head; private readonly Action<string, string> _chat;
        private readonly Func<string> _selfName; private readonly Func<int> _selfLevel, _selfJob;
        private enum Page { Search, Mine }
        private Page _page;
        private int _qLevel, _qJob = -1; private uint _lastIndex; private readonly List<int> _myJobs = new List<int>();
        /// <summary>Jobs an ad can ask for: ids below 256 only (0x0802 keeps the low byte). First and second jobs.</summary>
        public static readonly int[] JobChoices = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 14, 15, 16, 17, 18, 19, 20, 23, 24, 25 };

        public PartyBooking(RoSession s, ListWindow win, Func<Camera> head, Action<string, string> chat, Func<string> selfName, Func<int> selfLevel, Func<int> selfJob)
        {
            _s = s; _win = win; _head = head; _chat = chat; _selfName = selfName; _selfLevel = selfLevel; _selfJob = selfJob;
            _win.OnCloseClicked = Hide;
        }

        public void ResetForCharacter() { Results.Clear(); Mine = null; More = false; JoinRequest = null; _win.Hide(); Changed?.Invoke(); }

        // ------------------------------------------------------------------ packets
        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x0803:
                {
                    r.Skip(2); LastRegister = r.U16();
                    Note(LastRegister == 0 ? Lang.T("Your party ad is up.") : LastRegister == 2 ? Lang.T("You already have an ad up.") : Lang.T("The ad could not be placed."));
                    Debug.Log($"[Booking] register → {LastRegister}");
                    Refresh(); Changed?.Invoke(); return true;
                }
                case 0x0805:
                {
                    r.Skip(4); bool more = r.U8() != 0; int body = n - 5;
                    Stride = PickStride(b, o + 5, body);
                    if (_lastIndex == 0) Results.Clear();
                    for (int at = 0; Stride > 0 && at + 48 <= body; at += Stride)
                    {
                        var rr = new PacketReader(b, o + 5 + at, 48);
                        var ad = ReadAd(ref rr);
                        Results.RemoveAll(x => x.Index == ad.Index); Results.Add(ad);
                    }
                    More = more; SearchesAnswered++;
                    Debug.Log($"[Booking] search: {Results.Count} ad(s) (stride {Stride}, more {more}): {string.Join(", ", Results.ConvertAll(a => a.Name + "#" + a.Index))}");
                    Refresh(); Changed?.Invoke(); return true;
                }
                case 0x0807:
                {
                    r.Skip(2); LastDelete = r.U16();
                    if (LastDelete == 0 || LastDelete == 1) { Mine = null; Note(Lang.T("Your party ad was taken down.")); }
                    Debug.Log($"[Booking] delete → {LastDelete}");
                    Refresh(); Changed?.Invoke(); return true;
                }
                case 0x0809:
                {
                    r.Skip(2); var ad = ReadAd(ref r);
                    Results.RemoveAll(x => x.Index == ad.Index); Results.Add(ad);
                    if (ad.Name == _selfName()) Mine = ad;
                    Debug.Log($"[Booking] new ad #{ad.Index} by {ad.Name} Lv {ad.Level}");
                    Refresh(); Changed?.Invoke(); return true;
                }
                case 0x080a:
                {
                    r.Skip(2); uint idx = r.U32(); var jobs = new int[6]; for (int i = 0; i < 6; i++) jobs[i] = r.S16();
                    var ad = Results.Find(x => x.Index == idx); if (ad != null) ad.Jobs = jobs;
                    if (Mine != null && Mine.Index == idx) Mine.Jobs = jobs;
                    Debug.Log($"[Booking] ad #{idx} updated");
                    Refresh(); Changed?.Invoke(); return true;
                }
                case 0x080b:
                {
                    r.Skip(2); uint idx = r.U32();
                    Results.RemoveAll(x => x.Index == idx); if (Mine != null && Mine.Index == idx) Mine = null;
                    Debug.Log($"[Booking] ad #{idx} removed");
                    Refresh(); Changed?.Invoke(); return true;
                }
                case 0x0ae7:
                {
                    r.Skip(2); uint cid = r.U32(), aid = r.U32(); string nm = r.Str(24); int lv = r.U16(), job = r.U16();
                    JoinRequest = (cid, aid, nm, lv, job);
                    Note(Lang.F("{0} (Lv {1} {2}) asks to join your party.", nm, lv, LoginPanel.JobName(job)));
                    Debug.Log($"[Booking] join request from {nm} cid {cid}");
                    Refresh(); Changed?.Invoke(); return true;
                }
                case 0x0afa:
                {
                    r.Skip(2); string leader = r.Str(24), party = r.Str(24); r.U32(); bool refused = r.S32() != 0;
                    LastJoinAnswer = refused ? Lang.F("{0} turned down your request to join '{1}'.", leader, party) : Lang.F("{0} accepted you into '{1}'.", leader, party);
                    Note(LastJoinAnswer);
                    Debug.Log("[Booking] join answer: " + LastJoinAnswer);
                    Changed?.Invoke(); return true;
                }
                default: return false;
            }
        }

        private static Ad ReadAd(ref PacketReader r)
        {
            var ad = new Ad { Index = r.U32(), Name = r.Str(24), Time = r.U32(), Level = r.S16(), MapId = r.S16() };
            for (int i = 0; i < 6; i++) ad.Jobs[i] = r.S16();
            return ad;
        }

        /// <summary>48 (Windows rAthena, the module) or 56 (64-bit Linux rAthena): the stride whose records all read as plausible ads.</summary>
        private static int PickStride(byte[] b, int start, int body)
        {
            if (body <= 0) return 48;
            foreach (int s in new[] { 48, 56 })
            {
                if (body % s != 0) continue;
                bool ok = true;
                for (int at = 0; at < body && ok; at += s)
                {
                    uint idx = BitConverter.ToUInt32(b, start + at);
                    byte first = b[start + at + 4];
                    short lv = BitConverter.ToInt16(b, start + at + 32);
                    ok = idx != 0 && first >= 0x20 && lv >= 0 && lv <= 999;
                }
                if (ok) return s;
            }
            return body % 56 == 0 && body % 48 != 0 ? 56 : 48;
        }

        // ------------------------------------------------------------------ requests
        public void Register(int level, int mapId, IList<int> jobs)
        {
            var w = new PacketWriter(0x0802).S16((short)level).S16((short)mapId);
            for (int i = 0; i < 6; i++) w.U16(i < jobs.Count ? (ushort)jobs[i] : (ushort)0xFFFF);
            _s.SendPacket(w.ToArray());
            Debug.Log($"[Booking] register Lv {level} map {mapId} jobs {string.Join(",", jobs)}");
        }
        public void UpdateJobs(IList<int> jobs)
        {
            var w = new PacketWriter(0x0808);
            for (int i = 0; i < 6; i++) w.U16(i < jobs.Count ? (ushort)jobs[i] : (ushort)0xFFFF);
            _s.SendPacket(w.ToArray());
            Debug.Log($"[Booking] update jobs {string.Join(",", jobs)}");
        }
        public void Delete() { _s.SendPacket(new PacketWriter(0x0806).ToArray()); Debug.Log("[Booking] delete"); }
        public void Search(int level, int job, bool nextPage = false)
        {
            _qLevel = level; _qJob = job;
            _lastIndex = nextPage && Results.Count > 0 ? MaxIndex() : 0;
            _s.SendPacket(new PacketWriter(0x0804).S16((short)level).S16(0).S16((short)job).U32(_lastIndex).S16(10).ToArray());
            Debug.Log($"[Booking] search Lv {level} job {job} from #{_lastIndex}");
        }
        private uint MaxIndex() { uint m = 0; foreach (var a in Results) if (a.Index > m) m = a.Index; return m; }
        /// <summary>Asks a leader to take us in (the web booking's join request; the server needs their char id and account id).</summary>
        public void AskToJoin(uint leaderCid, uint leaderAid) { _s.SendPacket(new PacketWriter(0x0ae6).U32(leaderCid).U32(leaderAid).ToArray()); Debug.Log($"[Booking] ask to join leader cid {leaderCid}"); }
        public void AnswerJoin(bool accept)
        {
            if (JoinRequest == null) return;
            var q = JoinRequest.Value; JoinRequest = null;
            _s.SendPacket(new PacketWriter(0x0af8).U32(q.cid).U32(q.aid).U8((byte)(accept ? 1 : 0)).ToArray());
            Note(accept ? Lang.F("You let {0} join.", q.name) : Lang.F("You turned {0} down.", q.name));
            Debug.Log($"[Booking] answer {q.name}: {accept}");
            Refresh(); Changed?.Invoke();
        }

        private void Note(string s) { _chat(s, "ffd9a8"); Toast?.Invoke(s); }

        // ------------------------------------------------------------------ window
        public bool Visible => _win.Visible;
        public ListWindow Window => _win;
        public void ShowSearch() { _page = Page.Search; _win.Open(_head()); Refresh(); Search(_qLevel, _qJob); }
        public void ShowMine() { _page = Page.Mine; if (_myJobs.Count == 0 && Mine != null) foreach (var j in Mine.Jobs) if (j >= 0 && j != 0xFFFF && j != 0xFF) _myJobs.Add(j); _win.Open(_head()); Refresh(); }
        public void Hide() { _win.Hide(); }

        public static string JobList(int[] jobs)
        {
            var l = new List<string>();
            foreach (var j in jobs) if (j >= 0 && j != 0xFFFF && j != 0xFF && j != -1) l.Add(LoginPanel.JobName(j));
            return l.Count == 0 ? Lang.T("~jobs~anyone") : string.Join(", ", l);
        }

        public void Refresh()
        {
            if (!_win.Visible) return;
            var rows = new List<ListWindow.Row>();
            _win.SetTabs(new[] { Lang.T("Find a party"), Lang.T("My ad") }, _page == Page.Mine ? 1 : 0, t => { _page = t == 1 ? Page.Mine : Page.Search; if (_page == Page.Search) Search(_qLevel, _qJob); Refresh(); });
            _win.SetInput(false);
            if (JoinRequest.HasValue)
            {
                var q = JoinRequest.Value;
                rows.Add(new ListWindow.Row { Highlight = true, Text = Lang.F("<color=#ffd060>{0}</color> (Lv {1} {2}) asks to join", q.name, q.level, LoginPanel.JobName(q.job)), A = Lang.T("Accept"), OnA = () => AnswerJoin(true), B = Lang.T("Decline"), OnB = () => AnswerJoin(false) });
            }
            if (_page == Page.Mine)
            {
                _win.SetTitle(Lang.T("My party ad"));
                _win.SetInfo(Mine != null ? Lang.F("Up since {0:HH:mm} · Lv {1} · wants {2}", DateTimeOffset.FromUnixTimeSeconds(Mine.Time).ToLocalTime(), Mine.Level, JobList(Mine.Jobs)) : Lang.T("Advertise that you look for people. Players search by level and job."));
                foreach (var j in JobChoices)
                {
                    int jj = j; bool on = _myJobs.Contains(j);
                    rows.Add(new ListWindow.Row { Text = $"{(on ? "<color=#7fd67f>✓</color> " : "")}{LoginPanel.JobName(j)}", Highlight = on, A = on ? Lang.T("Remove") : Lang.T("Want"), OnA = () => { if (on) _myJobs.Remove(jj); else if (_myJobs.Count < 6) _myJobs.Add(jj); else Toast?.Invoke(Lang.T("Six jobs at most.")); Refresh(); } });
                }
                _win.SetFooter((Lang.T("Close"), Hide), (Mine != null ? Lang.T("Take down") : null, Delete), (Mine != null ? Lang.T("Update") : null, () => UpdateJobs(_myJobs)), (Mine == null ? Lang.T("Advertise") : null, () => Register(_selfLevel(), 0, _myJobs)));
                _win.SetRows(rows);
                return;
            }
            _win.SetTitle(Lang.T("Find a party"));
            string filter = (_qLevel > 0 ? Lang.F("level {0}–{1}", Mathf.Max(1, _qLevel - 15), _qLevel) : Lang.T("any level")) + " · " + (_qJob >= 0 ? Lang.F("wants {0}", LoginPanel.JobName(_qJob)) : Lang.T("any job"));
            _win.SetInfo((Results.Count == 1 ? Lang.T("1 ad") : Lang.F("{0} ads", Results.Count)) + " · " + filter);
            var list = new List<Ad>(Results); list.Sort((a, c) => c.Time.CompareTo(a.Time));
            foreach (var ad in list)
            {
                var a2 = ad; bool me = ad.Name == _selfName();
                rows.Add(new ListWindow.Row { Text = $"{ad.Name}{(me ? " " + Lang.T("<color=#9aa3b5>(you)</color>") : "")}  " + Lang.F("<color=#ffd060>Lv {0}</color>\n<size=19><color=#9aa3b5>wants {1}</color></size>", ad.Level, JobList(ad.Jobs)),
                    A = me ? null : Lang.T("Whisper"), OnA = () => OpenWhisper?.Invoke(a2.Name) });
            }
            if (More) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>More ads…</color>"), A = Lang.T("More"), OnA = () => Search(_qLevel, _qJob, true) });
            _win.SetFooter((Lang.T("Close"), Hide), (_qJob >= 0 ? Lang.T("Any job") : Lang.T("My job"), () => Search(_qLevel, _qJob >= 0 ? -1 : _selfJob())), (_qLevel > 0 ? Lang.T("Any level") : Lang.T("My level"), () => Search(_qLevel > 0 ? 0 : _selfLevel(), _qJob)), (Lang.T("Refresh"), () => Search(_qLevel, _qJob)));
            _win.SetRows(rows);
        }

    }
}
