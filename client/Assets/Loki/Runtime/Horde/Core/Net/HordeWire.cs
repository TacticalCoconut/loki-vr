#nullable disable
using System;
using System.Collections.Generic;
using System.Text;

namespace Loki.Horde.Net
{
    /// <summary>
    /// Horde's wire (client wave 8, w8core): a new capability of Loki Host's relay (Net/PresenceWire.cs), on the same TCP
    /// connection and framing (u16 little-endian payload length + payload; payload[0] the message type). Nothing of
    /// versions 1 / 2 changes:
    ///   - WELCOME 2's capability bit 2 (<see cref="CapHorde"/>): this relay runs Horde. Older clients test bits 0 and 1 only.
    ///   - A client that sees the bit sends H_HELLO once; only after it does is it ever sent a Horde frame (which may be up to
    ///     65,535 bytes: a client that never said H_HELLO keeps its 274-byte bound and never sees one).
    ///   - An older relay never sets the bit: a new client never sends a Horde message (that relay would close on it).
    ///   Client → relay (0x10..0x1F; version-2 connections after a WELCOME with CapHorde):
    ///     0x10 H_HELLO   u8 Horde protocol version (<see cref="Version"/>), u8 sex (0 unknown, 1 male, 2 female), u8 n, content stamp[n] (ASCII)
    ///     0x11 H_START   (no body): "Start a run" (every player the host knows is invited); client wave 18 (h18rules), point 38: or
    ///                    u8 n × u32 AID: only those are invited (at most HordeLimits.MaxRunPlayers - 1; n = 0: alone)
    ///     0x12 H_ANSWER  u32 run id, u8 join (1) / not now (0)
    ///     0x13 H_INPUT   u16 seq, i8 dx, i8 dy: one tick of my stick (HordeLimits.TickRate a second while in a run)
    ///     0x14 H_CHOOSE  u16 offer id, u8 index
    ///     0x15 H_LEAVE   (no body): I leave the run
    ///     0x16 H_END     u32 run id: END that run for everyone (client wave 14, point 15 of the design's headset list: a player who was
    ///                    in it, who left it or still plays it; each player gets the EXP earned so far). A host from before it
    ///                    drops the message as any unknown kind (DecodeClient: Kind 0); the protocol version is unchanged.
    ///     0x17 H_PAUSE   u8 pause (1) / resume (0): client wave 15 (w15hfx), point 19 of the design's wave 14 list: the Horde menu's Pause
    ///                    stops the run for everyone (HordeSim.Hold), any player of the run resumes it. The holder is marked in the
    ///                    snapshot by HordePlayerFlags.Held (bit 32 of the player's flags: an older client ignores it). An older host
    ///                    drops the message (Kind 0); the protocol version is unchanged.
    ///                    Client wave 18 (h18rules), point 23: with others in the run a request starts a 3 s countdown (HordeSim.Rules.cs),
    ///                    the same request again cancels it; alone it acts at once (the client sends pause when the Horde menu opens,
    ///                    resume when it closes). The countdown travels in H_SNAP's last block (HordeRulesWire).
    ///   Relay → client (0x90..0x9F; only to a connection that said H_HELLO):
    ///     0x90 H_STATUS  the mode's state (see EncodeStatus); since protocol 2 it ends with u8 the relay's protocol version
    ///     0x91 H_TOWNS   u16 n × (u8 len, map name): the server's town maps
    ///     0x92 H_INVITE  u32 run, u32 starter AID, u8 n, name, u16 seconds × 10, u8 m, arena map, u16 run length (s), u8 reward size
    ///     0x93 H_RUN     u32 run, u8 slot, u16 arena id, u8 m, map, u16 x0, y0, x1, y1, u8 tick rate, u16 run length (s),
    ///                    u16 final boss seconds, u8 late, i32 tick now, [protocol 2: u8 ticks between snapshots]
    ///     0x94 H_SNAP    u16 my input seq acknowledged, then the shared body (see <see cref="SnapshotEncoder"/>)
    ///     0x95 H_OFFER   u16 id, u8 is job, u16 seconds × 10, u8 n × (u8 card kind, u16 def id, u8 level)
    ///     0x96 H_OFFER_CLOSED u16 id
    ///     0x97 H_RESULTS u32 run, u8 outcome, u32 seconds × 10, i8 my slot, u8 n × player result
    ///     0x98 H_REWARD  u32 run, i64 base EXP, i64 job EXP, u16 base share, u16 job share
    /// Places on the wire are 16-bit units of 1/256 cell RELATIVE TO THE ARENA's corner (x0, y0) (an arena is at most 250 cells
    /// a side). Bounds are checked on every read; a malformed Horde message from a client is dropped (the relay never closes a
    /// connection for it: voice and headsets go on).
    ///
    /// THE PROTOCOL VERSION (client wave 9, h9move): <see cref="Version"/> names the layout of everything above (the tick rate,
    /// the snapshot rate and layout, the inputs). A client says it in H_HELLO; the relay says its own at the end of H_STATUS
    /// (a version-1 relay sends no such byte). A relay that gets another version answers H_STATUS with "content not ok" (so a
    /// version-1 client shows its "another version" line) and never sends that client a run; a client that sees another
    /// version (or none) is HordeLinkState.OtherProtocol and sends nothing of a run. Headsets, voice and the data check are
    /// untouched either way: Horde's refusal is a status line, never a closed connection. Versions: 1 = client wave 8 (20
    /// ticks, 10 snapshots a second); 2 = client wave 9 (40 ticks, 20 snapshots, the ramp's velocity, nibble-packed enemy
    /// moves, pickups' flight, the protocol byte). 3 = client wave 17 (ONE bump for the whole wave, set at the merge h17int):
    /// PlayerHit carries the attacker's place and the WindUp event, shooters aim 0.3 s and chargers keep the direction of their
    /// wind-up (h17read, HordeSim.Readability.cs); the bosses' fx kinds 8-15 and events 24-26 (h17boss); the objectives' events
    /// 32-36 (h17obj); h17cards' Guard fx 25, events 40-42, the Marked enemy flag and the branch card; the passives' Proc event 48
    /// (h17pass); and H_SNAP's two blocks after the events: h17pass's passives block, then h17obj's objectives block.
    /// </summary>
    public static class HordeWire
    {
        public const byte Version = 4; // client wave 17: 2 → 3 for the whole wave (h17read, h17boss, h17pass, h17obj, h17cards: ONE bump, set at the merge h17int); client wave 18 (h18feel): 3 → 4 (the souls' block, point 35; the orchestrator sets the final number at the merge)
        /// <summary>WELCOME 2's capability bit 2.</summary>
        public const byte CapHorde = 4;
        public const int MaxPayload = 65535;
        public const byte ClientFirst = 0x10, ClientLast = 0x1F, RelayFirst = 0x90, RelayLast = 0x9F;
        public const byte HHello = 0x10, HStart = 0x11, HAnswer = 0x12, HInput = 0x13, HChoose = 0x14, HLeave = 0x15, HEnd = 0x16, HPause = 0x17, HCast = 0x18;
        public const byte HStatus = 0x90, HTowns = 0x91, HInvite = 0x92, HRun = 0x93, HSnap = 0x94, HOffer = 0x95, HOfferClosed = 0x96, HResults = 0x97, HReward = 0x98;
        public const int MaxStampBytes = 64, MaxNameBytes = 48;

        public static bool IsRelayType(byte t) => t >= RelayFirst && t <= RelayLast;
        public static bool IsClientType(byte t) => t >= ClientFirst && t <= ClientLast;

        // ------------------------------------------------------------------------------------------- writer / reader

        public sealed class W
        {
            private byte[] _b = new byte[256];
            private int _n;
            public int Length => _n;
            private void Need(int k) { if (_n + k > _b.Length) Array.Resize(ref _b, Math.Max(_b.Length * 2, _n + k)); }
            public W U8(int v) { Need(1); _b[_n++] = (byte)v; return this; }
            public W I8(int v) { Need(1); _b[_n++] = (byte)(sbyte)Math.Max(-128, Math.Min(127, v)); return this; }
            public W U16(int v) { Need(2); _b[_n++] = (byte)v; _b[_n++] = (byte)(v >> 8); return this; }
            public W I16(int v) { v = Math.Max(-32768, Math.Min(32767, v)); return U16((ushort)(short)v); }
            public W U32(uint v) { Need(4); _b[_n++] = (byte)v; _b[_n++] = (byte)(v >> 8); _b[_n++] = (byte)(v >> 16); _b[_n++] = (byte)(v >> 24); return this; }
            public W I32(int v) => U32((uint)v);
            public W I64(long v) { U32((uint)v); return U32((uint)((ulong)v >> 32)); }
            public W Str(string s, int max)
            {
                var b = Encoding.UTF8.GetBytes(s ?? "");
                int n = Math.Min(b.Length, max);
                while (n > 0 && n < b.Length && (b[n] & 0xC0) == 0x80) n--; // never split a character
                U8(n); Need(n); Array.Copy(b, 0, _b, _n, n); _n += n; return this;
            }
            public W Raw(byte[] b, int off, int n) { Need(n); Array.Copy(b, off, _b, _n, n); _n += n; return this; }
            public void Reset() { _n = 0; }
            public byte[] Payload() { var r = new byte[_n]; Array.Copy(_b, r, _n); return r; }
            /// <summary>The payload as a frame: u16 length + payload.</summary>
            public byte[] Frame()
            {
                if (_n > MaxPayload) throw new InvalidOperationException("horde payload too long: " + _n);
                var f = new byte[_n + 2]; f[0] = (byte)_n; f[1] = (byte)(_n >> 8); Array.Copy(_b, 0, f, 2, _n); return f;
            }
            internal byte[] Buf => _b;
        }

        public struct R
        {
            private readonly byte[] _b; private int _o; private readonly int _end;
            public bool Bad;
            public R(byte[] b, int o, int n) { _b = b; _o = o; _end = o + n; Bad = false; }
            public int Left => _end - _o;
            private bool Has(int n) { if (_o + n <= _end) return true; Bad = true; return false; }
            public int U8() => Has(1) ? _b[_o++] : 0;
            public int I8() => Has(1) ? (sbyte)_b[_o++] : 0;
            public int U16() { if (!Has(2)) return 0; int v = _b[_o] | _b[_o + 1] << 8; _o += 2; return v; }
            public int I16() => (short)U16();
            public uint U32() { if (!Has(4)) return 0; uint v = (uint)(_b[_o] | _b[_o + 1] << 8 | _b[_o + 2] << 16 | _b[_o + 3] << 24); _o += 4; return v; }
            public int I32() => (int)U32();
            public long I64() { ulong lo = U32(), hi = U32(); return (long)(lo | hi << 32); }
            public string Str(int max)
            {
                int n = U8();
                if (n > max || !Has(n)) { Bad = true; return ""; }
                string s;
                try { s = new UTF8Encoding(false, true).GetString(_b, _o, n); } catch (ArgumentException) { Bad = true; s = ""; }
                _o += n;
                foreach (char c in s) if (char.IsControl(c)) { Bad = true; return ""; }
                return s;
            }
        }

        // ------------------------------------------------------------------------------------------- client → relay

        public static byte[] EncodeHello(string contentStamp, byte sex) => new W().U8(HHello).U8(Version).U8(sex).Str(contentStamp, MaxStampBytes).Frame();
        public static byte[] EncodeStart() => new W().U8(HStart).Frame();
        /// <summary>Client wave 18 (h18rules), point 38: "Start a run" inviting only <paramref name="invite"/> (empty: alone).</summary>
        public static byte[] EncodeStart(IList<uint> invite)
        {
            var w = new W().U8(HStart);
            int n = Math.Min(255, invite != null ? invite.Count : 0); w.U8(n);
            for (int i = 0; i < n; i++) w.U32(invite[i]);
            return w.Frame();
        }
        public static byte[] EncodeAnswer(uint runId, bool join) => new W().U8(HAnswer).U32(runId).U8(join ? 1 : 0).Frame();
        public static byte[] EncodeInput(ushort seq, int dx, int dy) => new W().U8(HInput).U16(seq).I8(dx).I8(dy).Frame();
        public static byte[] EncodeChoose(int offerId, int index) => new W().U8(HChoose).U16(offerId).U8(index).Frame();
        public static byte[] EncodeLeave() => new W().U8(HLeave).Frame();
        public static byte[] EncodeEnd(uint runId) => new W().U8(HEnd).U32(runId).Frame();
        /// <summary>Client wave 15 (w15hfx), point 19: pause (true) or resume (false) the run I am in. Decoded into ClientMsg.Join.</summary>
        public static byte[] EncodePause(bool pause) => new W().U8(HPause).U8(pause ? 1 : 0).Frame();
        /// <summary>Casts an active laser-targeted weapon at (targetX, targetY) in simulation units (1 cell = 256 units).</summary>
        public static byte[] EncodeCast(int weaponId, int targetX, int targetY) => new W().U8(HCast).U16(weaponId).I32(targetX).I32(targetY).Frame();

        /// <summary>A client message, decoded and checked. Kind 0 = malformed (dropped).</summary>
        public struct ClientMsg
        {
            public byte Kind;
            public byte Version, Sex;
            public string Stamp;
            public uint RunId;
            public bool Join;
            public ushort Seq;
            public int Dx, Dy;
            public int OfferId, Index;
            public int WeaponId, TargetX, TargetY;
            /// <summary>Client wave 18 (h18rules), point 38: H_START's chosen players (null: none named, everyone is asked).</summary>
            public uint[] Invite;
        }

        public static ClientMsg DecodeClient(byte[] b, int o, int n)
        {
            var m = new ClientMsg();
            if (n < 1) return m;
            var r = new R(b, o, n);
            byte k = (byte)r.U8();
            switch (k)
            {
                case HHello: m.Version = (byte)r.U8(); m.Sex = (byte)r.U8(); m.Stamp = r.Str(MaxStampBytes); if (m.Sex > 2) m.Sex = 0; break;
                case HStart: // (client wave 18, h18rules, point 38: the chosen players, when named)
                    if (r.Left > 0) { int ni = r.U8(); if (r.Left < ni * 4) return m; m.Invite = new uint[ni]; for (int i = 0; i < ni; i++) m.Invite[i] = r.U32(); }
                    break;
                case HLeave: break;
                case HAnswer: m.RunId = r.U32(); m.Join = r.U8() != 0; break;
                case HEnd: m.RunId = r.U32(); break;
                case HPause: m.Join = r.U8() != 0; break; // (w15hfx, point 19: Join = pause)
                case HInput: m.Seq = (ushort)r.U16(); m.Dx = r.I8(); m.Dy = r.I8(); break;
                case HChoose: m.OfferId = r.U16(); m.Index = r.U8(); break;
                case HCast: m.WeaponId = r.U16(); m.TargetX = r.I32(); m.TargetY = r.I32(); break;
                default: return m;
            }
            if (r.Bad || r.Left != 0) return m;
            m.Kind = k;
            return m;
        }

        // ------------------------------------------------------------------------------------------- relay → client

        public static byte[] EncodeStatus(HordeStatus s, bool contentOk)
        {
            var w = new W().U8(HStatus).U8((s.Enabled ? 1 : 0) | (contentOk ? 2 : 0)).U8((byte)s.Phase).U32(s.RunId).U32(s.StarterAid).Str(s.StarterName, MaxNameBytes)
                .U16((int)Math.Round(Math.Max(0, s.SecondsLeft) * 10)).U16((int)Math.Min(65535, s.RunSeconds)).U16((int)Math.Min(65535, s.RunLength))
                .Str(s.ArenaMap, 15).U8((byte)s.Reward).U8(Math.Min(255, s.LowestLevel));
            int n = Math.Min(64, s.Members.Count);
            w.U8(n);
            for (int i = 0; i < n; i++)
            {
                var m = s.Members[i];
                w.U32(m.Aid).Str(m.Name, MaxNameBytes).U8((byte)m.State).I8(m.Slot).U8(Math.Min(255, m.Level)).U16(m.JobId).U8(m.Fallen ? 1 : 0);
            }
            w.U8(Version); // protocol 2: the relay's protocol version (a version-1 client reads up to the members and ignores the rest)
            // client wave 18 (h18rules), point 38: who could be invited now (an older client stops at the version)
            int no = Math.Min(64, s.Online.Count); w.U8(no);
            for (int i = 0; i < no; i++) w.U32(s.Online[i].Aid).Str(s.Online[i].Name, MaxNameBytes);
            return w.Frame();
        }

        /// <summary>Decodes a status; <paramref name="s"/>.Protocol is the relay's protocol version (1 when the status has none:
        /// a wave-8 relay).</summary>
        public static bool DecodeStatus(byte[] b, int o, int n, HordeStatus s, out bool contentOk)
        {
            var r = new R(b, o, n);
            contentOk = false;
            if (r.U8() != HStatus) return false;
            int f = r.U8(); s.Enabled = (f & 1) != 0; contentOk = (f & 2) != 0;
            s.Phase = (HordePhase)r.U8(); s.RunId = r.U32(); s.StarterAid = r.U32(); s.StarterName = r.Str(MaxNameBytes);
            s.SecondsLeft = r.U16() / 10f; s.RunSeconds = r.U16(); s.RunLength = r.U16(); s.ArenaMap = r.Str(15);
            s.Reward = (HordeRewardSize)r.U8(); s.LowestLevel = r.U8();
            int cnt = r.U8();
            s.Members.Clear();
            for (int i = 0; i < cnt && !r.Bad; i++)
                s.Members.Add(new HordeMember { Aid = r.U32(), Name = r.Str(MaxNameBytes), State = (HordeMemberState)r.U8(), Slot = r.I8(), Level = r.U8(), JobId = r.U16(), Fallen = r.U8() != 0 });
            if (r.Bad) return false;
            s.Protocol = r.Left >= 1 ? (byte)r.U8() : (byte)1;
            s.Online.Clear(); // client wave 18 (h18rules), point 38: who could be invited (none from a host from before)
            if (r.Left >= 1) { int no = r.U8(); for (int i = 0; i < no && !r.Bad; i++) s.Online.Add(new HordeMember { Aid = r.U32(), Name = r.Str(MaxNameBytes) }); }
            return !r.Bad;
        }

        public static byte[] EncodeTowns(IList<string> towns)
        {
            var w = new W().U8(HTowns);
            int n = Math.Min(towns.Count, 1000);
            w.U16(n);
            for (int i = 0; i < n; i++) w.Str(towns[i], 15);
            return w.Frame();
        }

        public static bool DecodeTowns(byte[] b, int o, int n, List<string> into)
        {
            var r = new R(b, o, n);
            if (r.U8() != HTowns) return false;
            int c = r.U16();
            into.Clear();
            for (int i = 0; i < c && !r.Bad; i++) into.Add(r.Str(15));
            return !r.Bad;
        }

        public static byte[] EncodeInvite(HordeInvite v) =>
            new W().U8(HInvite).U32(v.RunId).U32(v.StarterAid).Str(v.StarterName, MaxNameBytes).U16((int)Math.Round(Math.Max(0, v.Seconds) * 10))
                .Str(v.ArenaMap, 15).U16((int)Math.Min(65535, v.RunLength)).U8((byte)v.Reward).Frame();

        public static bool DecodeInvite(byte[] b, int o, int n, HordeInvite v)
        {
            var r = new R(b, o, n);
            if (r.U8() != HInvite) return false;
            v.RunId = r.U32(); v.StarterAid = r.U32(); v.StarterName = r.Str(MaxNameBytes); v.Seconds = r.U16() / 10f; v.ArenaMap = r.Str(15);
            v.RunLength = r.U16(); v.Reward = (HordeRewardSize)r.U8();
            return !r.Bad;
        }

        public static byte[] EncodeRun(HordeRunInfo v, int tickNow) =>
            new W().U8(HRun).U32(v.RunId).U8(v.Slot).U16(v.ArenaId).Str(v.Map, 15).U16(v.X0).U16(v.Y0).U16(v.X1).U16(v.Y1).U8(v.TickRate)
                .U16((int)Math.Min(65535, v.RunLength)).U16((int)Math.Min(65535, v.FinalBossSeconds)).U8(v.Late ? 1 : 0).I32(tickNow).U8(Math.Max(1, Math.Min(255, v.SnapshotEvery))).Frame();

        public static bool DecodeRun(byte[] b, int o, int n, HordeRunInfo v, out int tickNow)
        {
            var r = new R(b, o, n);
            tickNow = 0;
            if (r.U8() != HRun) return false;
            v.RunId = r.U32(); v.Slot = r.U8(); v.ArenaId = r.U16(); v.Map = r.Str(15); v.X0 = r.U16(); v.Y0 = r.U16(); v.X1 = r.U16(); v.Y1 = r.U16();
            v.TickRate = r.U8(); v.RunLength = r.U16(); v.FinalBossSeconds = r.U16(); v.Late = r.U8() != 0; tickNow = r.I32();
            v.SnapshotEvery = r.Left >= 1 ? Math.Max(1, r.U8()) : 2;
            return !r.Bad && v.Slot < HordeLimits.MaxPlayers && v.X1 >= v.X0 && v.Y1 >= v.Y0 && v.X1 - v.X0 < HordeLimits.MaxArenaCells + 1 && v.TickRate > 0;
        }

        public static byte[] EncodeOffer(HordeOffer v)
        {
            var w = new W().U8(HOffer).U16(v.Id).U8(v.IsJob ? 1 : 0).U16((int)Math.Round(Math.Max(0, v.Seconds) * 10));
            int n = Math.Min(16, v.Cards.Length);
            w.U8(n);
            bool anyBonus = false;
            for (int i = 0; i < n; i++)
            {
                w.U8((byte)v.Cards[i].Kind).U16(v.Cards[i].DefId).U8(Math.Min(255, v.Cards[i].Level));
                if (v.Cards[i].BonusStat != 0 || v.Cards[i].BonusStatPoints != 0) anyBonus = true;
            }
            if (anyBonus)
                for (int i = 0; i < n; i++) w.U8(v.Cards[i].BonusStat).U8(v.Cards[i].BonusStatPoints);
            return w.Frame();
        }

        public static bool DecodeOffer(byte[] b, int o, int n, out HordeOffer v)
        {
            var r = new R(b, o, n);
            v = new HordeOffer();
            if (r.U8() != HOffer) return false;
            v.Id = r.U16(); v.IsJob = r.U8() != 0; v.Seconds = r.U16() / 10f;
            int c = r.U8();
            v.Cards = new HordeCard[Math.Min(c, 16)];
            for (int i = 0; i < v.Cards.Length; i++) v.Cards[i] = new HordeCard { Kind = (HordeCardKind)r.U8(), DefId = r.U16(), Level = r.U8() };
            if (r.Left >= v.Cards.Length * 2)
                for (int i = 0; i < v.Cards.Length; i++) { v.Cards[i].BonusStat = (byte)r.U8(); v.Cards[i].BonusStatPoints = (byte)r.U8(); }
            return !r.Bad;
        }

        public static byte[] EncodeOfferClosed(int id) => new W().U8(HOfferClosed).U16(id).Frame();

        public static byte[] EncodeResults(HordeResults v, int mySlot)
        {
            var w = new W().U8(HResults).U32(v.RunId).U8((byte)v.Outcome).U32((uint)Math.Round(v.Seconds * 10)).I8(mySlot);
            int n = Math.Min(64, v.Players.Count);
            w.U8(n);
            for (int i = 0; i < n; i++)
            {
                var p = v.Players[i];
                w.U8(p.Slot).U32(p.Aid).Str(p.Name, MaxNameBytes).U16(p.Level).U16(p.JobId).I32(p.Kills).I32(p.EliteKills).I32(p.BossKills).U16(p.Falls).U16(p.Revives)
                 .I64(p.Damage).I64(p.Healing).U32((uint)Math.Round(p.Seconds * 10)).I32(p.Score).U16(p.BaseShare).U16(p.JobShare).U8((byte)p.Reward).U8(p.Left ? 1 : 0);
            }
            return w.Frame();
        }

        public static bool DecodeResults(byte[] b, int o, int n, out HordeResults v)
        {
            var r = new R(b, o, n);
            v = new HordeResults();
            if (r.U8() != HResults) return false;
            v.RunId = r.U32(); v.Outcome = (HordeOutcome)r.U8(); v.Seconds = r.U32() / 10f; v.MySlot = r.I8();
            int c = r.U8();
            for (int i = 0; i < c && !r.Bad; i++)
                v.Players.Add(new HordePlayerResult
                {
                    Slot = r.U8(), Aid = r.U32(), Name = r.Str(MaxNameBytes), Level = r.U16(), JobId = r.U16(), Kills = r.I32(), EliteKills = r.I32(), BossKills = r.I32(),
                    Falls = r.U16(), Revives = r.U16(), Damage = r.I64(), Healing = r.I64(), Seconds = r.U32() / 10f, Score = r.I32(), BaseShare = r.U16(), JobShare = r.U16(),
                    Reward = (HordeRewardState)r.U8(), Left = r.U8() != 0,
                });
            return !r.Bad;
        }

        public static byte[] EncodeReward(HordeRewardNote v) => new W().U8(HReward).U32(v.RunId).I64(v.BaseExp).I64(v.JobExp).U16(v.BaseShare).U16(v.JobShare).Frame();

        public static bool DecodeReward(byte[] b, int o, int n, out HordeRewardNote v)
        {
            var r = new R(b, o, n);
            v = new HordeRewardNote();
            if (r.U8() != HReward) return false;
            v.RunId = r.U32(); v.BaseExp = r.I64(); v.JobExp = r.I64(); v.BaseShare = r.U16(); v.JobShare = r.U16();
            return !r.Bad;
        }

        // ------------------------------------------------------------------------------------------- the content stamp

        /// <summary>
        /// A stamp of the content tables (a hash of everything the engine reads): host and client must agree. Every number is
        /// hashed in 1/1000 steps (never its raw float / double bits): tables that compute a float at run time (0.75f - 0.03f * i)
        /// can differ by one ulp between Unity's Mono and .NET's RyuJIT (Mono may keep a float in double precision), and the same
        /// sources must give the same stamp on both (found by w8game, COORD 2026-10-04 20:43: h1-5fa2710c… vs h1-7baeed53…).
        /// The step is floor(v × 1000 + 0.25), not a rounding to nearest: its boundaries fall at 0.00075 past a 1/1000 step, where no
        /// value written with up to four decimals lies (a rounding to nearest puts them at 0.0005, i.e. on a value like 0.0125, where
        /// an ulp decides); an ulp of any table value below a few hundred stays far from them.
        /// </summary>
        public static string ContentStamp(HordeContent c)
        {
            ulong h = 1469598103934665603UL;
            void M(double v)
            {
                long q = double.IsNaN(v) ? long.MinValue : double.IsInfinity(v) ? (v > 0 ? long.MaxValue : long.MinValue + 1)
                       : (long)Math.Floor(Math.Max(-9e15, Math.Min(9e15, v * 1000.0)) + 0.25);
                unchecked { h ^= (ulong)q; h *= 1099511628211UL; }
            }
            void S(string s) { foreach (char ch in s ?? "") M(ch); M(-1); }
            // fields added after wave 8 (client wave 9, h9content's additive fields) are hashed only when set, so that tables
            // without them keep their wave-8 stamp (0 = wave 8's behaviour in the engine too)
            void N(double v, int tag) { if (!double.IsNaN(v) && Math.Floor(Math.Max(-9e15, Math.Min(9e15, v * 1000.0)) + 0.25) != 0) { M(tag); M(v); } } // "set" in the stamp's own 1/1000 steps (an ulp of 0 is not set)
            var r = c.Rules;
            N(r.FinalBossWarnSeconds, -11); N(r.PickupRadiusPerLevel, -12);
            N(r.ClusterSpawnCells, -60); N(r.ClusterSpawnMaxShares, -61); // client wave 18 (h18rules), point 31 (+ its cap)
            N(r.EliteFirstSeconds, -62); N(r.EliteEverySeconds, -63); N(r.EliteEveryMaxSeconds, -64); N(r.EliteQuietSeconds, -65); if (r.EliteEverySeconds > 0) N(r.ElitePlayersPerExtra, -66); // client wave 18 (h18rules): the elites
            if (c.Elites != null) foreach (var el in c.Elites) { M(-67); M(el.EnemyId); M(el.From); M(el.To); } // client wave 18 (h18rules)
            foreach (var v in new double[] { r.ReferenceRunSeconds, r.FinalBossEnemyId, r.FinalBossSeconds, r.MaxEnemies, r.StartHp, r.HpPerLevel, r.MoveSpeed, r.BodyRadius, r.PickupRadius,
                r.HurtGraceSeconds, r.CritChance, r.CritDamagePct, r.StartWeapon, r.MaxWeapons, r.MaxPassives, r.ExpGrowthPct, r.MaxLevel, r.ExpSharePct, r.CardsPerOffer, r.ChoiceSeconds,
                r.WeightNewWeapon, r.WeightWeaponLevel, r.WeightNewPassive, r.WeightPassiveLevel, r.WeightStat, r.StatCardPoints, r.ReviveSeconds, r.ReviveRadius, r.ReviveNearMul, r.ReviveHpPct, r.ReviveSafeSeconds,
                // client wave 9 (h9move): the walk's ramp, the soft wall, the knockback impulse, the pickups' flight, the choice's shrinking limit
                r.MoveAccelSeconds, r.MoveDecelSeconds, r.WallSoftCells, r.KnockbackKeepPct, r.PickupPullSpeed, r.PickupPullAccel, r.ChoiceSecondsStep, r.ChoiceSecondsMin }) M(v);
            foreach (var v in r.ExpTable ?? new int[0]) M(v);
            foreach (var v in r.JobMarks ?? new int[0]) M(v);
            M(-5); foreach (var v in r.PartyExpScale ?? new float[0]) M(v);
            foreach (var w in c.Weapons)
            {
                M(w.Id); S(w.Key); M(w.SkillId); M((int)w.Kind); M((int)w.Target); M((int)w.Class); M((int)w.Element); M((int)w.Zone); M(w.Homing ? 1 : 0); M(w.SummonMobId); M(w.SummonHp); M(w.Weight);
                foreach (var l in w.Levels)
                {
                    foreach (var v in new double[] { l.Damage, l.Cooldown, l.Count, l.Pierce, l.Speed, l.Range, l.Area, l.Arc, l.Spread, l.Burst, l.Delay, l.Duration, l.Tick, l.Heal, l.HealPct, l.Falloff }) M(v);
                    if (l.Effects != null) foreach (var e in l.Effects) if (e != null) { M((int)e.Kind); M(e.Chance); M(e.Duration); M(e.Power); M(e.Tick); }
                    if (l.Buffs != null) foreach (var m in l.Buffs) if (m != null) { M((int)m.Kind); M(m.Value); M(m.Arg); }
                }
            }
            foreach (var p in c.Passives) { M(p.Id); S(p.Key); M(p.SkillId); M(p.ItemId); M(p.Weight); foreach (var l in p.Levels) { M(-2); if (l != null) foreach (var m in l) if (m != null) { M((int)m.Kind); M(m.Value); M(m.Arg); } } }
            foreach (var p in c.Passives) if (p.Proc != null) { M(-21); M(p.Id); foreach (var v in HordePassiveStamp.Numbers(p.Proc)) M(v); } // client wave 17 (h17pass): the passives' triggers
            foreach (var j in c.Jobs) { M(j.JobId); M(j.Tier); M(j.SignatureWeapon); M(j.Sex); foreach (var v in j.Next) M(v); M(-3); foreach (var v in j.Weapons) M(v); M(-3); foreach (var v in j.Passives) M(v); M(-3); if (j.StatBonus != null) foreach (var v in j.StatBonus) M(v); if (j.Mods != null) foreach (var m in j.Mods) if (m != null) { M((int)m.Kind); M(m.Value); M(m.Arg); } }
            foreach (var e in c.Enemies)
            {
                foreach (var v in new double[] { e.Id, e.MobId, (int)e.Rank, (int)e.Behavior, e.Hp, e.Damage, e.AttackInterval, e.Speed, e.Radius, e.Exp, (int)e.Element, (int)e.Race, (int)e.Size,
                    e.KnockbackResist, e.EffectResist, e.ShotDamage, e.ShotRange, e.ShotSpeed, e.ChargeCooldown, e.ChargeWindup, e.ChargeSpeed, e.BurstArea, e.DrawScale }) M(v);
                foreach (var a in e.Abilities) { M((int)a.Kind); M(a.Cooldown); M(a.FirstAfter); M(a.Damage); M(a.Area); M(a.Delay); M(a.Speed); M(a.Range); M(a.Count); M(a.EnemyId); M(a.SkillId); }
                foreach (var a in e.Abilities) { N(a.Width, -17); N(a.Spacing, -18); N(a.Arc, -19); N(a.Duration, -20); N(a.Linger, -21); N(a.SlowPct, -22); N(a.HpBelow, -23); N(a.PhaseCooldownPct, -24); N((int)a.Aim, -25); N((int)a.Shape, -26); } // client wave 17 (h17boss)
                foreach (var d in e.Drops) if (d != null) { M(d.PickupId); M(d.Chance); N(d.ScatterCells, -13); }
                N(e.KeepRange, -14);
            }
            foreach (var w in c.Waves) { M(w.From); M(w.To); M(w.EnemyId); M(w.Interval); M(w.Count); M(w.MaxAlive); M((int)w.Pattern); M(w.Once ? 1 : 0); N(w.WarnSeconds, -15); }
            foreach (var p in c.Pickups) { M(p.Id); M((int)p.Kind); M(p.ItemId); M(p.Value); M(p.Area); }
            foreach (var a in c.Arenas) { M(a.Id); S(a.Map); M(a.X0); M(a.Y0); M(a.X1); M(a.Y1); }
            foreach (var s in c.Stats) { M((int)s.Stat); foreach (var m in s.PerPoint) if (m != null) { M((int)m.Kind); M(m.Value); M(m.Arg); } }
            var sc = c.Scaling;
            foreach (var arr in new[] { sc.SpawnMul, sc.EnemyHpMul, sc.EliteHpMul, sc.BossHpMul, sc.EnemyDamageMul }) { M(-4); if (arr != null) foreach (var v in arr) M(v); }
            M(sc.EnemyHpPctPerMinute); M(sc.EnemyDamagePctPerMinute);
            var so = c.Score;
            foreach (var v in new double[] { so.PerKill, so.PerEliteKill, so.PerBossKill, so.PerRunLevel, so.PerJobTier, so.PerDamage1000, so.PerHeal100, so.PerRevive, so.PerSecond, so.Victory }) M(v);
            var rw = c.Reward;
            M(rw.ScoreForFull); M(rw.MinSeconds); foreach (var v in rw.BaseCapPct ?? new float[0]) M(v); foreach (var v in rw.JobCapPct ?? new float[0]) M(v);
            N(rw.SecondsForFull, -16); // client wave 15 (w15horde): the time reward
            if (c.ElementPct != null) foreach (var v in c.ElementPct) M(v);
            if (c.Objectives != null) foreach (var v in c.Objectives.StampValues()) M(v); // client wave 17 (h17obj): the map objectives' numbers
            if (c.Souls != null) foreach (var v in c.Souls.StampValues()) M(v); // client wave 18 (h18feel): soul retrieval's numbers
            HordeCardsStamp.Add(c, M, N); // client wave 17 (h17cards): the new cards' fields (HordeContent.Cards.cs)
            HordeTransStamp.Add(c, M, N); // client wave 18 (h18trans): the transcendence fields (HordeContent.Trans.cs)
            return "h4-" + h.ToString("x16"); // h3: client wave 9 (the rules above were added; a wave-8 "h2-" stamp never matches); h4: client wave 17 (the whole wave's tables: h17pass's triggers, h17boss's attacks, h17obj's objectives, h17cards' cards; set at the merge h17int)
        }
    }
}
