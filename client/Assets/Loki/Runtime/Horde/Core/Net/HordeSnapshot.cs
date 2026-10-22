#nullable disable
using System;
using System.Collections.Generic;

namespace Loki.Horde.Net
{
    /// <summary>
    /// H_SNAP's shared body (after the per-client u16 ack): what changed since the previous snapshot (a delta) or everything
    /// (a key). The host makes ONE delta and, when some client needs it, ONE key per snapshot; a client applies them in order
    /// (TCP keeps the order; a client that missed one is sent a key: the relay tells the host when it had to drop a Horde frame).
    /// Layout of Horde protocol version 2 (client wave 9, h9move; 20 snapshots a second at 40 ticks):
    ///   i32 tick, u8 flags (1 key, 2 final phase, 4 PAUSED: someone is choosing, the tick stands), i32 previous snapshot tick
    ///            (a delta applies only on top of it; while paused the tick repeats), u8 party level, i32 party exp, i32 party exp
    ///            to the next level (design note 2026-10-05: one EXP bar for the party)
    ///   players: u8 n × (u8 slot, u8 flags, u16 x, u16 y, i16 facing, i16 vx, i16 vy (units/tick), u8 parts (1 identity, 2 loadout,
    ///            4 vitals) [vitals, in a key or when any changed: u16 speed (units/tick), u16 hp, u16 max hp, u16 job,
    ///            u8 revive (0..255), u16 kills] [identity: u32 aid, str name]
    ///            [loadout: u8 n × (u16 weapon, u8 level), u8 n × (u16 passive, u8 level), 6 × u16 stats])
    ///   enemies, key:   u16 n × (u16 slot, u8 serial, u16 def, u16 x, u16 y, u8 hp, u8 flags)
    ///   enemies, delta: u16 n removed × u16 slot; u16 n added × (as in a key); u16 slots covered, moved-bits[(slots+7)/8],
    ///            per set bit i8 dx, i8 dy (1/64 cell: at 20 snapshots a second up to 40 cells/s; 1/16 in wave 8 made a slow
    ///            walker's drawn speed vary by a third from snapshot to snapshot); u16 n jumped × (u16 slot, u16 x, u16 y) (moved
    ///            too far for a byte: a long knockback); u16 n × (u16 slot, u8 hp); u16 n × (u16 slot, u8 flags)
    ///   pickups: key: u16 n × record; delta: u16 n removed × u16 id, u16 n added × record, u16 n pulled × (u16 id, u8 slot, u8 ticks back)
    ///            record = u16 id, u8 def, u16 x, u16 y, i32 value, i8 pulled by (-1 none), u8 ticks since pulled (255 = more)
    ///   summons: u8 n × (u16 id, u8 owner, u16 def, u16 x, u16 y, u8 hp)       (always all)
    ///   fx begun: u16 n × fx record; fx ended: u16 n × (u16 id, u8 hit)          (key: every live fx, nothing ended)
    ///   events:  u16 n × (u8 kind | 0x80 has place | 0x40 has extra, u8 flags, u8 ticks back, i8 slot, i16 target, u16 def, i32 value,
    ///            [u16 x, u16 y] [u16 x2, u16 y2, i16 a × 10, i16 b × 10, i16 c × 10])
    /// </summary>
    public sealed class SnapshotEncoder
    {
        /// <summary>Hits per snapshot that are sent (the largest; the rest are only counted): 12 at 20 snapshots a second are 240
        /// damage numbers a second, more than a player reads (24 in wave 8 at 10 a second).</summary>
        public int MaxHits = 12;
        /// <summary>An enemy's move on the wire: units per step (4 = 1/64 cell).</summary>
        public const int MoveUnit = 4;
        private readonly HordeSim _sim;
        private readonly int _bx, _by;
        private readonly bool[] _alive = new bool[HordeLimits.MaxEnemies];
        private readonly int[] _serial = new int[HordeLimits.MaxEnemies], _sx = new int[HordeLimits.MaxEnemies], _sy = new int[HordeLimits.MaxEnemies];
        private readonly byte[] _hp = new byte[HordeLimits.MaxEnemies], _fl = new byte[HordeLimits.MaxEnemies];
        private readonly int[] _loadout = new int[HordeLimits.MaxPlayers];
        private readonly bool[] _present = new bool[HordeLimits.MaxPlayers];
        private readonly Vitals[] _vitals = new Vitals[HordeLimits.MaxPlayers];
        private int _prevTick = -1;
        private readonly HordePassiveBlock _procs = new HordePassiveBlock(); // client wave 17 (h17pass): the passives' states
        // what the outputs of the sim said since the last snapshot
        private readonly List<HordeEvent> _events = new List<HordeEvent>();
        private readonly List<HordeEvent> _hits = new List<HordeEvent>();
        private readonly List<SimFx> _begun = new List<SimFx>();
        private readonly List<KeyValuePair<int, bool>> _ended = new List<KeyValuePair<int, bool>>();
        private readonly HashSet<int> _pAdded = new HashSet<int>(), _pRemoved = new HashSet<int>(), _pPulled = new HashSet<int>();
        public long HitsDropped;

        private struct Vitals
        {
            public int Speed, Hp, MaxHp, Job, Revive, Kills;
            public bool Same(in Vitals o) => Speed == o.Speed && Hp == o.Hp && MaxHp == o.MaxHp && Job == o.Job && Revive == o.Revive && Kills == o.Kills;
        }

        public SnapshotEncoder(HordeSim sim)
        {
            _sim = sim; _bx = sim.Arena.X0 * HordeSim.U; _by = sim.Arena.Y0 * HordeSim.U;
            for (int i = 0; i < _loadout.Length; i++) _loadout[i] = -1;
        }

        /// <summary>After each sim Step: keeps what the next snapshot must tell.</summary>
        public void Collect()
        {
            _events.AddRange(_sim.Events); _hits.AddRange(_sim.Hits); _begun.AddRange(_sim.FxBegun); _ended.AddRange(_sim.FxEnded);
            foreach (int id in _sim.PickupsAdded) _pAdded.Add(id);
            foreach (int id in _sim.PickupsPulled) if (!_pAdded.Contains(id)) _pPulled.Add(id);
            foreach (int id in _sim.PickupsRemoved) { _pPulled.Remove(id); if (!_pAdded.Remove(id)) _pRemoved.Add(id); }
        }

        private int X(int x) => HordeSim.Clamp(x - _bx, 0, 65535);
        private int Y(int y) => HordeSim.Clamp(y - _by, 0, 65535);

        /// <summary>Makes this snapshot: the delta (for clients that had the previous one) and, when asked, the key.</summary>
        public void Encode(bool wantKey, out byte[] delta, out byte[] key)
        {
            var sim = _sim;
            int tick = sim.Tick;
            // ---- enemies: work out the delta against what clients hold (and update it)
            var removed = new List<int>(); var added = new List<int>(); var moved = new List<int>(); var jumped = new List<int>(); var hpCh = new List<int>(); var flCh = new List<int>();
            var mdx = new sbyte[HordeLimits.MaxEnemies]; var mdy = new sbyte[HordeLimits.MaxEnemies];
            int covered = 0;
            for (int i = 0; i < HordeLimits.MaxEnemies; i++)
            {
                var e = sim.Enemies[i];
                bool now = e.Alive;
                byte hp = now ? (byte)Math.Max(1, Math.Min(255, (int)((long)e.Hp * 255 / Math.Max(1, e.MaxHp)))) : (byte)0;
                byte fl = now ? (byte)e.Flags : (byte)0;
                if (_alive[i] && (!now || e.Serial != _serial[i])) { removed.Add(i); _alive[i] = false; }
                if (now && !_alive[i])
                {
                    _alive[i] = true; _serial[i] = e.Serial; _sx[i] = X(e.X); _sy[i] = Y(e.Y); _hp[i] = hp; _fl[i] = fl; added.Add(i);
                    continue;
                }
                if (!now) continue;
                int dx = (X(e.X) - _sx[i]) / MoveUnit, dy = (Y(e.Y) - _sy[i]) / MoveUnit;
                if (dx != 0 || dy != 0)
                {
                    if (dx < -127 || dx > 127 || dy < -127 || dy > 127) { _sx[i] = X(e.X); _sy[i] = Y(e.Y); jumped.Add(i); }
                    else { mdx[i] = (sbyte)dx; mdy[i] = (sbyte)dy; _sx[i] += dx * MoveUnit; _sy[i] += dy * MoveUnit; moved.Add(i); covered = i + 1; }
                }
                if (hp != _hp[i]) { _hp[i] = hp; hpCh.Add(i); }
                if (fl != _fl[i]) { _fl[i] = fl; flCh.Add(i); }
            }
            // ---- hits: the largest
            _hits.Sort((a, b) => b.Value.CompareTo(a.Value));
            if (_hits.Count > MaxHits) { HitsDropped += _hits.Count - MaxHits; _hits.RemoveRange(MaxHits, _hits.Count - MaxHits); }

            _procs.Begin(sim); // client wave 17 (h17pass)
            delta = BuildBody(false, tick, removed, added, moved, jumped, covered, mdx, mdy, hpCh, flCh);
            key = wantKey ? BuildBody(true, tick, null, null, null, null, 0, null, null, null, null) : null;
            // what was told is told
            for (int s = 0; s < HordeLimits.MaxPlayers; s++) { var p = sim.Players[s]; _present[s] = p.Present; if (p.Present) { _loadout[s] = p.LoadoutVersion; _vitals[s] = VitalsOf(p); } }
            _events.Clear(); _hits.Clear(); _begun.Clear(); _ended.Clear(); _pAdded.Clear(); _pRemoved.Clear(); _pPulled.Clear();
            _prevTick = tick;
        }

        private Vitals VitalsOf(SimPlayer p) => new Vitals
        {
            Speed = p.SpeedPerTick, Hp = Math.Min(65535, p.Hp), MaxHp = Math.Min(65535, p.MaxHp), Job = p.JobId,
            Revive = (int)Math.Min(255, p.Revive * 255), Kills = Math.Min(65535, p.Kills),
        };

        private byte[] BuildBody(bool key, int tick, List<int> removed, List<int> added, List<int> moved, List<int> jumped, int covered, sbyte[] mdx, sbyte[] mdy, List<int> hpCh, List<int> flCh)
        {
            var sim = _sim;
            var w = new HordeWire.W();
            w.I32(tick).U8((key ? 1 : 0) | (sim.FinalPhase ? 2 : 0) | (sim.Paused ? 4 : 0)).I32(_prevTick);
            w.U8(Math.Min(255, sim.PartyLevel)).I32(sim.PartyExp).I32(sim.PartyExpToNext());
            // players
            int np = 0; foreach (var p in sim.Players) if (p.Present) np++;
            w.U8(np);
            foreach (var p in sim.Players)
            {
                if (!p.Present) continue;
                var fl = (p.Fallen ? HordePlayerFlags.Fallen : 0) | (p.SafeUntil > sim.Tick || p.Open != null && !p.IdleChooser ? HordePlayerFlags.Safe : 0)
                       | (p.Open != null ? HordePlayerFlags.Choosing : 0) | (p.Away ? HordePlayerFlags.Away : 0) | (p.Helped ? HordePlayerFlags.Helped : 0)
                       | (p.Slot == sim.HeldBy ? HordePlayerFlags.Held : 0) // client wave 15 (w15hfx), point 19 (old clients ignore the bit)
                       | (sim.SlideHidden(p) ? HordePlayerFlags.Vanished : 0); // client wave 18 (h18rules), point 27 (old clients ignore the bit)
                bool fresh = key || !_present[p.Slot];
                bool identity = fresh;
                bool loadout = fresh || _loadout[p.Slot] != p.LoadoutVersion;
                var vit = VitalsOf(p);
                bool vitals = fresh || !vit.Same(_vitals[p.Slot]);
                w.U8(p.Slot).U8((byte)fl).U16(X(p.X)).U16(Y(p.Y)).I16(p.FaceDeg).I16(p.Vx).I16(p.Vy)
                 .U8((identity ? 1 : 0) | (loadout ? 2 : 0) | (vitals ? 4 : 0));
                if (vitals) w.U16(vit.Speed).U16(vit.Hp).U16(vit.MaxHp).U16(vit.Job).U8(vit.Revive).U16(vit.Kills);
                if (identity) w.U32(p.Aid).Str(p.Name, HordeWire.MaxNameBytes);
                if (loadout)
                {
                    w.U8(Math.Min(32, p.Weapons.Count)); for (int i = 0; i < Math.Min(32, p.Weapons.Count); i++) w.U16(p.Weapons[i].Def.Id).U8(p.Weapons[i].Level);
                    w.U8(Math.Min(32, p.Passives.Count)); for (int i = 0; i < Math.Min(32, p.Passives.Count); i++) w.U16(p.Passives[i].Def.Id).U8(p.Passives[i].Level);
                    for (int i = 0; i < 6; i++) w.U16(Math.Max(0, Math.Min(65535, p.Stat(i))));
                }
            }
            // enemies
            if (key)
            {
                int n = 0; for (int i = 0; i < HordeLimits.MaxEnemies; i++) if (_alive[i]) n++;
                w.U16(n);
                for (int i = 0; i < HordeLimits.MaxEnemies; i++) if (_alive[i]) w.U16(i).U8(_serial[i]).U16(sim.Enemies[i].Def.Id).U16(_sx[i]).U16(_sy[i]).U8(_hp[i]).U8(_fl[i]);
            }
            else
            {
                w.U16(removed.Count); foreach (int i in removed) w.U16(i);
                w.U16(added.Count); foreach (int i in added) w.U16(i).U8(_serial[i]).U16(sim.Enemies[i].Def.Id).U16(_sx[i]).U16(_sy[i]).U8(_hp[i]).U8(_fl[i]);
                w.U16(covered);
                var bits = new byte[(covered + 7) / 8];
                foreach (int i in moved) bits[i >> 3] |= (byte)(1 << (i & 7));
                w.Raw(bits, 0, bits.Length);
                foreach (int i in moved) w.I8(mdx[i]).I8(mdy[i]);
                w.U16(jumped.Count); foreach (int i in jumped) w.U16(i).U16(_sx[i]).U16(_sy[i]);
                w.U16(hpCh.Count); foreach (int i in hpCh) w.U16(i).U8(_hp[i]);
                w.U16(flCh.Count); foreach (int i in flCh) w.U16(i).U8(_fl[i]);
            }
            // pickups
            if (key)
            {
                w.U16(sim.Pickups.Count);
                foreach (var k in sim.Pickups) PickupRecord(w, k, tick);
            }
            else
            {
                w.U16(_pRemoved.Count); foreach (int id in _pRemoved) w.U16(id);
                var add = new List<SimPickup>(); var pulled = new List<SimPickup>();
                foreach (var k in sim.Pickups) { if (_pAdded.Contains(k.Id)) add.Add(k); else if (_pPulled.Contains(k.Id) && k.PulledBy >= 0) pulled.Add(k); }
                w.U16(add.Count); foreach (var k in add) PickupRecord(w, k, tick);
                w.U16(pulled.Count); foreach (var k in pulled) w.U16(k.Id).U8(k.PulledBy).U8(Math.Max(0, Math.Min(255, tick - k.PullTick)));
            }
            // summons
            w.U8(Math.Min(255, sim.Summons.Count));
            foreach (var s in sim.Summons) w.U16(s.Id).U8(s.OwnerSlot).U16(s.Weapon.Def.Id).U16(X(s.X)).U16(Y(s.Y)).U8(s.MaxHp > 0 ? Math.Max(1, s.Hp * 255 / s.MaxHp) : 255);
            // fx
            if (key)
            {
                int n = 0; foreach (var f in sim.Fx) if (!f.Ended) n++;
                w.U16(n); foreach (var f in sim.Fx) if (!f.Ended) Fx(w, f);
                w.U16(0);
            }
            else
            {
                w.U16(_begun.Count); foreach (var f in _begun) Fx(w, f);
                w.U16(_ended.Count); foreach (var kv in _ended) w.U16(kv.Key).U8(kv.Value ? 1 : 0);
            }
            // events (a key carries the same events: a client that gets a key instead of a delta loses none of this tick's)
            w.U16(_events.Count + _hits.Count);
            foreach (var e in _events) Ev(w, e, tick);
            foreach (var e in _hits) Ev(w, e, tick);
            _procs.Write(w, key); // client wave 17 (h17pass): the passives' block, after the events (HordeSim.Passives.cs)
            HordeObjectiveWire.Write(w, sim, _bx, _by); // client wave 17 (h17obj): the map objectives' block (protocol 3)
            HordeSoulWire.Write(w, sim, _bx, _by); // client wave 18 (h18feel): the soul shards' block (protocol 4)
            HordeRulesWire.Write(w, sim); // client wave 18 (h18rules), point 23: the pause's countdown, LAST (HordeSim.Rules.cs; the wave 18 merge keeps it after h18feel's souls)
            return w.Payload();
        }

        /// <summary>A pickup's record: where it lies (its place when it was dropped: while it flies the client draws the flight
        /// from there), and whether it flies to someone.</summary>
        private void PickupRecord(HordeWire.W w, SimPickup k, int tick)
        {
            // a flying pickup's wire place is where it was when the pull began, which the engine has moved on from: the client
            // draws the flight itself; nothing else needs the exact place (it is taken within a second)
            w.U16(k.Id).U8(k.Def.Id).U16(X(k.X)).U16(Y(k.Y)).I32(k.Value).I8(k.PulledBy).U8(k.PulledBy >= 0 ? Math.Max(0, Math.Min(255, tick - k.PullTick)) : 0);
        }

        private void Fx(HordeWire.W w, SimFx f)
        {
            w.U16(f.Id).U8((byte)f.Kind).I8(f.OwnerSlot).I16(f.OwnerEnemy).U16(f.DefId).U8(f.Level).U16(X(f.X)).U16(Y(f.Y)).I16(f.VX).I16(f.VY)
             .U16(Math.Min(65535, f.Radius)).U16(Math.Min(65535, f.Range)).I16((int)Math.Round(f.Angle)).I16((int)Math.Round(f.Turns * 1000)).U8(Math.Min(255, f.Count))
             .I16(f.Target).U8(f.Follows ? 1 : 0).I32(f.StartTick).I32(f.ArmTick).I32(f.EndTick);
        }

        private static bool HasPlace(HordeEventKind k) => k != HordeEventKind.Hit && k != HordeEventKind.LevelUp && k != HordeEventKind.FinalPhase;
        private static bool HasExtra(HordeEventKind k) =>
            k == HordeEventKind.Swing || k == HordeEventKind.Nova || k == HordeEventKind.Strike || k == HordeEventKind.Chain || k == HordeEventKind.EnemyAbility
            || k == HordeEventKind.EnemyBurst || k == HordeEventKind.CardTaken
            || k == HordeEventKind.PlayerHit || k == HordeEventKind.WindUp // client wave 17 (h17read): the attacker's place, a wind-up's aim (the 0x40 bit: older decoders skip it alike)
            || HordeSim.BossEventHasExtra(k) // client wave 17 (h17boss): BossStrike, PlayerSlowed
            || k == HordeEventKind.Dash; // client wave 17 (h17cards): a dash's end place

        private void Ev(HordeWire.W w, HordeEvent e, int tick)
        {
            bool place = HasPlace(e.Kind), extra = HasExtra(e.Kind);
            w.U8((int)e.Kind | (place ? 0x80 : 0) | (extra ? 0x40 : 0)).U8((byte)e.Flags).U8(Math.Max(0, Math.Min(255, tick - e.Tick))).I8(e.Slot).I16(e.Target).U16(e.DefId).I32(e.Value);
            if (place) w.U16(X((int)Math.Round(e.X * HordeSim.U))).U16(Y((int)Math.Round(e.Y * HordeSim.U)));
            if (extra) w.U16(X((int)Math.Round(e.X2 * HordeSim.U))).U16(Y((int)Math.Round(e.Y2 * HordeSim.U))).I16((int)Math.Round(e.A * 10)).I16((int)Math.Round(e.B * 10)).I16((int)Math.Round(e.C * 10));
        }

        /// <summary>The whole H_SNAP frame for one client: its ack + the shared body.</summary>
        public static byte[] Frame(ushort ack, byte[] body)
        {
            int n = 1 + 2 + body.Length;
            if (n > HordeWire.MaxPayload) return null;
            var f = new byte[2 + n];
            f[0] = (byte)n; f[1] = (byte)(n >> 8); f[2] = HordeWire.HSnap; f[3] = (byte)ack; f[4] = (byte)(ack >> 8);
            Buffer.BlockCopy(body, 0, f, 5, body.Length);
            return f;
        }
    }

    /// <summary>The client's side: applies H_SNAP bodies to a <see cref="HordeWorldView"/>.</summary>
    public sealed class SnapshotDecoder
    {
        private float _bx, _by;
        private readonly int[] _sx = new int[HordeLimits.MaxEnemies], _sy = new int[HordeLimits.MaxEnemies];
        private int _lastTick = -1;
        public bool HaveKey { get; private set; }
        /// <summary>Snapshots refused (a delta that does not follow the one held): the client waits for the next key.</summary>
        public int Refused;
        /// <summary>My speed (units per tick), exact place (units) and walking velocity (units per tick) in the latest snapshot
        /// (the prediction replays my inputs from them).</summary>
        public int MySpeed, MyX, MyY, MyVx, MyVy;
        public readonly List<HordeEvent> Events = new List<HordeEvent>();
        public readonly List<HordeFxView> Begun = new List<HordeFxView>();
        public readonly List<KeyValuePair<HordeFxView, bool>> Ended = new List<KeyValuePair<HordeFxView, bool>>();

        public void Reset(HordeRunInfo run)
        {
            _bx = run.X0; _by = run.Y0; _lastTick = -1; HaveKey = false;
        }

        private float Cx(int u) => _bx + u / (float)HordeSim.U;
        private float Cy(int u) => _by + u / (float)HordeSim.U;

        /// <summary>Applies one body (from after the ack). False: refused or malformed (the world is left as it was, or a key is awaited).</summary>
        public bool Apply(byte[] b, int o, int n, HordeWorldView v, int mySlot, double now)
        {
            Events.Clear(); Begun.Clear(); Ended.Clear();
            var r = new HordeWire.R(b, o, n);
            int tick = r.I32(); int flags = r.U8(); int prev = r.I32();
            int partyLevel = r.U8(), partyExp = r.I32(), partyToNext = r.I32();
            bool key = (flags & 1) != 0, paused = (flags & 4) != 0;
            if (r.Bad) return false;
            if (!key && (!HaveKey || prev != _lastTick)) { Refused++; return false; }
            bool fresh = key && !HaveKey;
            v.MySlot = mySlot;
            v.PartyLevel = partyLevel; v.PartyExp = partyExp; v.PartyExpToNext = partyToNext;
            // players
            var seen = new bool[HordeLimits.MaxPlayers];
            int np = r.U8();
            for (int k = 0; k < np && !r.Bad; k++)
            {
                int slot = r.U8();
                if (slot >= HordeLimits.MaxPlayers) return false;
                var p = v.Players[slot];
                seen[slot] = true;
                bool was = p.Present;
                p.Present = true;
                p.Flags = (HordePlayerFlags)r.U8();
                int ux = r.U16(), uy = r.U16();
                float x = Cx(ux), y = Cy(uy);
                if (!was || key) { p.PrevX = x; p.PrevY = y; } else { p.PrevX = p.X; p.PrevY = p.Y; }
                p.Moving = x != p.X || y != p.Y;
                p.X = x; p.Y = y;
                p.Facing = r.I16(); int vx = r.I16(), vy = r.I16();
                if (slot != mySlot) { p.Vx = vx * (float)HordeSim.TickRate / HordeSim.U; p.Vy = vy * (float)HordeSim.TickRate / HordeSim.U; }
                int parts = r.U8();
                int speed = -1;
                if ((parts & 4) != 0)
                {
                    speed = r.U16(); p.Hp = r.U16(); p.MaxHp = r.U16(); p.JobId = r.U16();
                    p.Revive = r.U8() / 255f; p.Kills = r.U16();
                }
                p.Level = partyLevel; p.Exp = partyExp; p.ExpToNext = partyToNext; // the party's (one EXP bar): kept per player for the HUD
                if ((parts & 1) != 0) { p.Aid = r.U32(); p.Name = r.Str(HordeWire.MaxNameBytes); }
                if ((parts & 2) != 0)
                {
                    p.Weapons.Clear(); int nw = r.U8(); for (int i = 0; i < nw; i++) p.Weapons.Add(new HordeOwned { DefId = r.U16(), Level = r.U8() });
                    p.Passives.Clear(); int npv = r.U8(); for (int i = 0; i < npv; i++) p.Passives.Add(new HordeOwned { DefId = r.U16(), Level = r.U8() });
                    for (int i = 0; i < 6; i++) p.Stats[i] = r.U16();
                }
                if (slot == mySlot) { if (speed >= 0) MySpeed = speed; MyX = ux + (int)Math.Round(_bx * HordeSim.U); MyY = uy + (int)Math.Round(_by * HordeSim.U); MyVx = vx; MyVy = vy; }
            }
            for (int s = 0; s < HordeLimits.MaxPlayers; s++) if (!seen[s]) v.Players[s].Present = false;
            v.PausedBy.Clear();
            if (paused) foreach (var p in v.Players) if (p.Present && (p.Flags & (HordePlayerFlags.Choosing | HordePlayerFlags.Held)) != 0) v.PausedBy.Add(p.Slot); // (w15hfx: + the menu's pause)
            // enemies
            foreach (var e in v.Enemies) { e.PrevX = e.X; e.PrevY = e.Y; }
            if (key)
            {
                foreach (var e in v.Enemies) e.Alive = false;
                int ne = r.U16();
                for (int k = 0; k < ne && !r.Bad; k++) ReadEnemy(ref r, v, tick, true);
            }
            else
            {
                int nr = r.U16(); for (int k = 0; k < nr && !r.Bad; k++) { int i = r.U16(); if (i < HordeLimits.MaxEnemies) v.Enemies[i].Alive = false; }
                int na = r.U16(); for (int k = 0; k < na && !r.Bad; k++) ReadEnemy(ref r, v, tick, false);
                int covered = r.U16();
                if (covered > HordeLimits.MaxEnemies) return false;
                int nb = (covered + 7) / 8;
                var bits = new byte[nb]; for (int i = 0; i < nb; i++) bits[i] = (byte)r.U8();
                for (int i = 0; i < covered && !r.Bad; i++)
                {
                    if ((bits[i >> 3] & (1 << (i & 7))) == 0) continue;
                    int dx = r.I8(), dy = r.I8();
                    _sx[i] += dx * SnapshotEncoder.MoveUnit; _sy[i] += dy * SnapshotEncoder.MoveUnit;
                    var e = v.Enemies[i]; e.X = Cx(_sx[i]); e.Y = Cy(_sy[i]);
                }
                int nj = r.U16();
                for (int k = 0; k < nj && !r.Bad; k++)
                {
                    int i = r.U16(), x = r.U16(), y = r.U16();
                    if (i >= HordeLimits.MaxEnemies) { r.Bad = true; break; }
                    _sx[i] = x; _sy[i] = y;
                    var e = v.Enemies[i]; e.X = Cx(x); e.Y = Cy(y);
                }
                int nh = r.U16(); for (int k = 0; k < nh && !r.Bad; k++) { int i = r.U16(); int hp = r.U8(); if (i < HordeLimits.MaxEnemies) v.Enemies[i].Hp = (byte)hp; }
                int nf = r.U16(); for (int k = 0; k < nf && !r.Bad; k++) { int i = r.U16(); int fl = r.U8(); if (i < HordeLimits.MaxEnemies) v.Enemies[i].Flags = (HordeEnemyFlags)fl; }
            }
            int alive = 0; foreach (var e in v.Enemies) if (e.Alive) alive++;
            v.EnemiesAlive = alive;
            // pickups
            if (key)
            {
                v.Pickups.Clear();
                int nk = r.U16(); for (int k = 0; k < nk && !r.Bad; k++) v.Pickups.Add(ReadPickup(ref r, tick));
            }
            else
            {
                int nr = r.U16(); for (int k = 0; k < nr && !r.Bad; k++) { int id = r.U16(); v.Pickups.RemoveAll(q => q.Id == id); }
                int na = r.U16(); for (int k = 0; k < na && !r.Bad; k++) { var q = ReadPickup(ref r, tick); v.Pickups.RemoveAll(z => z.Id == q.Id); v.Pickups.Add(q); }
                int npl = r.U16();
                for (int k = 0; k < npl && !r.Bad; k++)
                {
                    int id = r.U16(), slot = r.U8(), back = r.U8();
                    var q = v.Pickup(id);
                    if (q != null) { q.PulledBy = slot < HordeLimits.MaxPlayers ? slot : -1; q.PullTick = tick - back; }
                }
            }
            // summons
            var old = new Dictionary<int, HordeSummonView>(); foreach (var s in v.Summons) old[s.Id] = s;
            v.Summons.Clear();
            int ns = r.U8();
            for (int k = 0; k < ns && !r.Bad; k++)
            {
                int id = r.U16(), owner = r.U8(), def = r.U16(); float x = Cx(r.U16()), y = Cy(r.U16()); byte hp = (byte)r.U8();
                var s = old.TryGetValue(id, out var was) ? was : new HordeSummonView { Id = id, PrevX = x, PrevY = y, X = x, Y = y };
                s.PrevX = s.X; s.PrevY = s.Y; s.X = x; s.Y = y; s.OwnerSlot = owner; s.DefId = def; s.Hp = hp;
                v.Summons.Add(s);
            }
            // fx
            if (key) { foreach (var f in v.Fx) Ended.Add(new KeyValuePair<HordeFxView, bool>(f, false)); v.Fx.Clear(); }
            int nfx = r.U16();
            for (int k = 0; k < nfx && !r.Bad; k++)
            {
                var f = new HordeFxView
                {
                    Id = r.U16(), Kind = (HordeFxKind)r.U8(), OwnerSlot = r.I8(), OwnerEnemy = r.I16(), DefId = r.U16(), Level = r.U8(), X = Cx(r.U16()), Y = Cy(r.U16()),
                };
                f.VX = r.I16() * HordeSim.TickRate / (float)HordeSim.U; f.VY = r.I16() * HordeSim.TickRate / (float)HordeSim.U;
                f.Radius = r.U16() / (float)HordeSim.U; f.Range = r.U16() / (float)HordeSim.U; f.Angle = r.I16(); f.Turns = r.I16() / 1000f; f.Count = r.U8();
                f.Target = r.I16(); f.Follows = r.U8() != 0; f.StartTick = r.I32(); f.ArmTick = r.I32(); f.EndTick = r.I32();
                v.Fx.RemoveAll(z => z.Id == f.Id);
                v.Fx.Add(f); Begun.Add(f);
            }
            int nend = r.U16();
            for (int k = 0; k < nend && !r.Bad; k++)
            {
                int id = r.U16(); bool hit = r.U8() != 0;
                var f = v.FxOf(id);
                if (f != null) { v.Fx.Remove(f); Ended.Add(new KeyValuePair<HordeFxView, bool>(f, hit)); }
            }
            // events
            int nev = r.U16();
            for (int k = 0; k < nev && !r.Bad; k++)
            {
                int kb = r.U8();
                var e = new HordeEvent { Kind = (HordeEventKind)(kb & 0x3f), Flags = (HordeEventFlags)r.U8() };
                e.Tick = tick - r.U8(); e.Slot = r.I8(); e.Target = r.I16(); e.DefId = r.U16(); e.Value = r.I32();
                if ((kb & 0x80) != 0) { e.X = Cx(r.U16()); e.Y = Cy(r.U16()); }
                else if (e.Kind == HordeEventKind.Hit && e.Target >= 0 && e.Target < HordeLimits.MaxEnemies) { e.X = v.Enemies[e.Target].X; e.Y = v.Enemies[e.Target].Y; }
                if ((kb & 0x40) != 0) { e.X2 = Cx(r.U16()); e.Y2 = Cy(r.U16()); e.A = r.I16() / 10f; e.B = r.I16() / 10f; e.C = r.I16() / 10f; }
                Events.Add(e);
            }
            HordePassiveBlock.Read(ref r, v); // client wave 17 (h17pass): the passives' block (after the events)
            HordeObjectiveWire.Read(ref r, v, _bx, _by); // client wave 17 (h17obj): the map objectives' block (protocol 3)
            HordeSoulWire.Read(ref r, v, _bx, _by); // client wave 18 (h18feel): the soul shards' block (protocol 4)
            HordeRulesWire.Read(ref r, v); // client wave 18 (h18rules), point 23: the pause's countdown (none when no byte is left), LAST (after h18feel's souls)
            if (r.Bad) { HaveKey = false; return false; }
            v.PrevTick = v.Tick == 0 || fresh ? tick : v.Tick; v.PrevTickAt = v.Tick == 0 || fresh ? now - 0.1 : v.TickAt;
            v.Tick = tick; v.TickAt = now; v.FinalPhase = (flags & 2) != 0;
            _lastTick = tick; HaveKey = true;
            // the history for the render-delayed drawing (client wave 9); while paused the tick repeats: nothing new to draw between
            v.PushSnapshot(tick, now, fresh, paused);
            return true;
        }

        private HordePickupView ReadPickup(ref HordeWire.R r, int tick)
        {
            var q = new HordePickupView { Id = r.U16(), DefId = r.U8(), X = Cx(r.U16()), Y = Cy(r.U16()), Value = r.I32() };
            int pulled = r.I8(), back = r.U8();
            q.PulledBy = pulled >= 0 && pulled < HordeLimits.MaxPlayers ? pulled : -1; q.PullTick = q.PulledBy >= 0 ? tick - back : 0;
            return q;
        }

        private void ReadEnemy(ref HordeWire.R r, HordeWorldView v, int tick, bool key)
        {
            int i = r.U16(), serial = r.U8(), def = r.U16(), x = r.U16(), y = r.U16(), hp = r.U8(), fl = r.U8();
            if (i >= HordeLimits.MaxEnemies) { r.Bad = true; return; }
            var e = v.Enemies[i];
            bool fresh = !e.Alive || e.Serial != serial;
            e.Alive = true; e.Serial = serial; e.DefId = def; e.Hp = (byte)hp; e.Flags = (HordeEnemyFlags)fl;
            _sx[i] = x; _sy[i] = y;
            float cx = Cx(x), cy = Cy(y);
            if (fresh) { e.PrevX = cx; e.PrevY = cy; e.Dir8 = 0; e.Vx = e.Vy = 0; if (!key) e.SinceTick = tick; }
            e.X = cx; e.Y = cy;
        }
    }
}
