using System.Collections.Generic;
using Loki.Formats;
using Loki.Net;
using Loki.Sprites;
using Loki.World;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Walks our own character to a far cell in legs (client wave 3b, w3walk). rAthena answers nothing to a walk request whose
    /// path is over 17 steps, or over 14 with the straight line blocked (Sprites/RoRoute.cs has the rules and the lines); in the
    /// VR diorama the whole map can be clicked, so a click across it did nothing. Here:
    ///  - the whole route is planned on the client's GAT (<see cref="RoRoute.Planner"/>), and each request goes to the
    ///    farthest route cell rAthena accepts from where the server's unit is (<see cref="RoRoute.Trip.NextLeg"/>);
    ///  - the next leg is requested during the last steps of the current one, validated from every cell the server's unit
    ///    may be on when the request lands: the server stores the new target and re-paths from the cell the step in progress
    ///    reaches, with a fresh ZC_NOTIFY_PLAYERMOVE (rA unit.cpp:894-899, 738-744, 317-318; clif.cpp:2051-2061), so the walk
    ///    does not stop between legs; if no route cell passes from all of them it waits for the arrival;
    ///  - a request the server does not answer in time (refused: unit.cpp:855-869 sends nothing) is asked again shorter, then
    ///    around the refused cells (<see cref="RoRoute.Trip.OnRefused"/>); a ZC_STOPMOVE (a stop, a knockback's end, a failed
    ///    re-path, clif_fixpos unit.cpp:742-743) or a walk we did not ask for resumes the route from where the unit stands;
    ///  - a new click, the stick, an attack, a skill, a dialog, sitting, a chat room, death or a map change ends it (the game
    ///    calls <see cref="Cancel"/>, <see cref="Blocked"/> says why walking is not possible now);
    ///  - routes and legs keep off the portals in view (walking onto one warps: npc_touch_area_allnpc, rA unit.cpp:680-688),
    ///    and a walk already sent is diverted when a portal comes into view on it;
    ///  - a destination marker while walking, and for a route of more than one leg a line along the rest of the route
    ///    (hidden in first person).
    /// Everything comes from the RO packets (0x0087, 0x0088, 0x01ff, 0x08d2, 0x0091 / 0x0092) and the client's GAT: nothing is
    /// rAthena-specific beyond its default limits, which the SpacetimeDB module shares (server/src/conf.rs:6, mob/unit.rs:76-77).
    /// Where the server's map cache and the GRF differ (51 maps, design notes §1), some legs are refused and asked
    /// again (client/tools/walk/run_routecheck.py measures it offline).
    /// </summary>
    public sealed class RouteWalker : MonoBehaviour
    {
        // ---- wiring (LokiGame.W3Walk.cs)
        public System.Func<Unit> Self;
        public System.Func<GatFile> Gat;
        public System.Func<Transform> MapRoot;
        /// <summary>CZ_REQUEST_MOVE2 to (x, y).</summary>
        public System.Action<int, int> SendWalk;
        /// <summary>Why walking is not possible now (dead, a dialog, a chat room …), or null. A trip ends when it says something.</summary>
        public System.Func<string> Blocked;
        public System.Func<bool> FirstPerson;
        /// <summary>The portals the client knows on this map (units of rAthena's warp classes in view): routes keep off their
        /// touch areas (<see cref="RoRoute.Trip.SetAvoid"/>) unless the destination is one.</summary>
        public System.Func<IEnumerable<Vector2Int>> Warps;
        /// <summary>How far round a portal the routes keep off (see RoRoute.Trip.SetAvoid).</summary>
        public const int WarpMargin = 2;
        /// <summary>A short message for the player (a trip failed).</summary>
        public System.Action<string> Tell;
        /// <summary>A trip ended: its tag and "arrived", "cancelled: …" or "failed: …".</summary>
        public event System.Action<string, string> Ended;
        public static bool Log = true;

        public bool Active => _trip != null;
        public string Tag { get; private set; } = "";
        public Vector2Int? Goal => _trip != null ? new Vector2Int(_trip.GoalX, _trip.GoalY) : (Vector2Int?)null;
        /// <summary>The route of the running trip (smoke / diagnostics).</summary>
        public RoRoute.Trip Trip => _trip;
        /// <summary>The route is more than one leg: the line is drawn.</summary>
        public bool Far { get; private set; }
        public GameObject MarkerObject => _marker;
        public GameObject LineObject => _line;

        /// <summary>One request sent (smoke / diagnostics).</summary>
        public sealed class LegRecord
        {
            public int Trip, K, X, Y, Starts, MaxLen;
            public float SentAt, AnsweredAt = -1f;
            public bool Chained, Refused, Accepted;
            public int FromX = -1, FromY = -1;
            /// <summary>For a chained leg: server ms between where the previous leg's plan reached the re-path cell and the new
            /// walk's moveStartTime (0 = no pause); -1 unknown.</summary>
            public int GapMs = -1;
            public string Starts2 = "";
        }
        public readonly List<LegRecord> Legs = new List<LegRecord>();
        public int Trips, Refusals, Stops, Foreign, Arrived, Failed, Cancelled, Diverts;
        public string LastOutcome = "";

        // ---- the server's own unit, from our packets (tracked whether a trip runs or not)
        private Unit _tracked;
        private bool _srvMoving;                  // the last 0x0087 walk (it may be over by now: see ServerWalking)
        private int _srvX, _srvY;                 // where it stands when not moving
        private readonly List<Vector2Int> _walkCells = new List<Vector2Int>();
        private readonly List<int> _walkReach = new List<int>();
        private uint _walkStart; private float _walkRecvLocal; private int _walkSpeed = 150;
        private float _lastStopAt = -10f, _lastHitAt = -10f, _lastDivertAt = -10f;

        // ---- the trip
        private readonly RoRoute.Planner _planner = new RoRoute.Planner();
        private RoRoute.Trip _trip;
        private Unit _self;
        private bool _pending; private int _pendingK, _pendingX, _pendingY; private float _pendingDeadline; private LegRecord _pendingRec;
        private readonly List<LegRecord> _sent = new List<LegRecord>(); // this trip's requests (a late answer is still matched)
        private bool _waitForeign;
        private float _retryAt;
        /// <summary>The server's walk in progress is one of this trip's legs (else: a walk from before the trip, or one the
        /// server made on its own).</summary>
        private bool _legRunning;
        private int[] _sx = new int[64], _sy = new int[64];

        // ---- visuals
        private GameObject _marker, _line; private Mesh _lineMesh; private static Material _lineMat;
        private readonly List<Vector3> _pts = new List<Vector3>();

        // ================================================================== the game's calls
        /// <summary>Walks to (x, y): one request when the server accepts it at once, else a trip of legs. Replaces a running trip.
        /// false (and a message) when there is no way there. <paramref name="marker"/> false: the destination ring only for a trip
        /// of more than one leg (client wave 5, w5move: the attack chase, Game/AttackChase.cs — the official client marks no
        /// attack walk).</summary>
        public bool BeginTrip(int x, int y, string tag, bool marker = true) // not "Start": Unity takes a MonoBehaviour's Start as its message and logs "Start() can not take parameters"
        {
            var self = Self?.Invoke(); var gat = Gat?.Invoke();
            if (self == null || gat == null) return false;
            if (_trip != null) End("cancelled: a new destination", false);
            SyncTracked(self);
            var trip = new RoRoute.Trip(gat, _planner, x, y);
            // the route starts where the server's unit will re-path from: the cell the step in progress reaches, or where it stands
            var from = RepathEstimate();
            _warpKey = WarpKey(); trip.SetAvoid(WarpCentres(), WarpMargin, from.x, from.y);
            if (!trip.Plan(from.x, from.y))
            {
                Debug.Log($"[W3Walk] t={Time.time:F3} no route {from.x},{from.y} -> {x},{y} ({tag}): {trip.FailReason}");
                Tell?.Invoke(trip.FailReason == "no way there from here" ? Lang.T("You can't walk there from here.") : Lang.T("No way there."));
                LastOutcome = "failed: " + trip.FailReason; Failed++;
                Ended?.Invoke(tag, LastOutcome);
                return false;
            }
            _trip = trip; _self = self; Tag = tag; Trips++;
            _pending = false; _sent.Clear(); _waitForeign = false; _legRunning = false;
            // one leg does it? (then no line: an ordinary click walk)
            int n = Starts(true);
            int k = trip.NextLeg(_sx, _sy, n);
            Far = trip.RouteLen > 1 && k != trip.RouteLen - 1;
            if (Log) Debug.Log($"[W3Walk] t={Time.time:F3} trip #{Trips} {tag}: ({from.x},{from.y}) -> ({x},{y}) route {trip.RouteLen} cells, {(Far ? "far (legs)" : "one leg")}, server {(ServerWalking ? "walking" : "standing")} at {ServerCellNow}; {trip.Map.AvoidCount} cells kept off round portals{(trip.ThroughAvoided ? " (no way round them: the route crosses them)" : "")}");
            if (marker || Far) ShowMarker();
            Step();
            return true;
        }

        /// <summary>Client wave 5 (w5move): the server walks the trip's last leg now (its ZC_NOTIFY_PLAYERMOVE to the destination came
        /// and the walk is not over): nothing more will be asked (Game/AttackChase.cs hands over to the server's own stepaction).</summary>
        public bool LastLegRunning => _trip != null && _legRunning && ServerWalking && _walkCells[_walkCells.Count - 1] == new Vector2Int(_trip.GoalX, _trip.GoalY);

        /// <summary>Ends a running trip without sending anything (a new click / an attack / a skill … sends its own request).</summary>
        public void Cancel(string why) { if (_trip != null) End("cancelled: " + why, false); }

        /// <summary>We were hit (a damage packet on us): the server may delay our next walk by the damage motion
        /// (unit_set_walkdelay, rA unit.cpp:2003-2060; unit_walktoxy flag 4 waits up to 2 s, :872-879).</summary>
        public void NoteHit() => _lastHitAt = Time.time;

        /// <summary>Called for every map packet before the game handles it (LokiGame.OnMapPacket's first line).</summary>
        public void Peek(ushort id, byte[] b, int o, int n, uint selfId)
        {
            switch (id)
            {
                case 0x0087: // ZC_NOTIFY_PLAYERMOVE moveStartTime.L MoveData[6] (clif_walkok, rA clif.cpp:2051-2061)
                {
                    if (n < 12) return;
                    var r = new PacketReader(b, o, n); r.Skip(2);
                    uint start = r.U32();
                    var md = new byte[6]; for (int i = 0; i < 6; i++) md[i] = r.U8();
                    var (x0, y0, x1, y1) = EntityManager.UnpackMove(md);
                    OnSelfMove(x0, y0, x1, y1, start);
                    return;
                }
                case 0x0088: // ZC_STOPMOVE id.L x.W y.W (clif_fixpos): a stop, a knockback's end, a failed re-path
                case 0x01ff: // ZC_HIGHJUMP id.L x.W y.W (knockback, High Jump: a 0x0088 follows, clif_blown clif.cpp:2192-2198)
                case 0x08d2: // ZC_FASTMOVE id.L x.W y.W
                {
                    if (n < 10) return;
                    var r = new PacketReader(b, o, n); r.Skip(2);
                    uint aid = r.U32(); int x = r.U16(), y = r.U16();
                    if (aid == selfId && selfId != 0) OnSelfStop(x, y, id);
                    return;
                }
                case 0x0091: case 0x0092: // ZC_NPCACK_MAPMOVE / SERVERMOVE: a map change (a portal, a warp, @jump on the same map)
                    if (_trip != null) End(_trip != null && Tag == "portal" ? "arrived" : "cancelled: map change", false);
                    _tracked = null;
                    return;
            }
        }

        // ================================================================== portals
        private long _warpKey;
        private IEnumerable<(int x, int y)> WarpCentres()
        {
            var w = Warps?.Invoke();
            if (w == null) yield break;
            foreach (var c in w) yield return (c.x, c.y);
        }
        private long WarpKey()
        {
            long k = 17;
            foreach (var c in WarpCentres()) k = k * 31 + (c.x * 1024 + c.y + 1);
            return k;
        }

        /// <summary>
        /// Portals and the walk: when the portals in view change, the avoided cells follow and the rest of the route is planned
        /// again if it steps on one; when the walk the server runs now steps on a portal's area (a portal that came into view
        /// after the leg was sent), a new request goes out at once from where the server's unit will re-path — a leg of a new
        /// route, or that cell itself (a stop) — as a new click would (the server takes the latest target at the end of the step
        /// in progress, unit.cpp:894-899, 738-744). true when it sent a request or ended the trip.
        /// </summary>
        private bool UpdatePortals()
        {
            long key = WarpKey();
            bool walking = ServerWalking;
            if (key != _warpKey)
            {
                _warpKey = key;
                var here = walking ? _walkCells[IndexAt(ElapsedMs())] : new Vector2Int(_srvX, _srvY);
                if (_trip.SetAvoid(WarpCentres(), WarpMargin, here.x, here.y) && _trip.Crosses(_trip.Index))
                {
                    var from = walking ? _walkCells[_walkCells.Count - 1] : here;
                    if (Log) Debug.Log($"[W3Walk] t={Time.time:F3} a portal in view on the rest of the route: planning again from ({from.x},{from.y}), {_trip.Map.AvoidCount} cells kept off");
                    if (!_trip.Plan(from.x, from.y)) { Fail(_trip.FailReason); return true; }
                }
            }
            if (!walking || _trip.ThroughAvoided || _trip.Map.AvoidCount == 0 || Time.time - _lastDivertAt < 0.5f) return false;
            int at = IndexAt(ElapsedMs());
            int hit = -1;
            for (int i = at + 1; i < _walkCells.Count; i++) if (_trip.Map.IsAvoided(_walkCells[i].x, _walkCells[i].y)) { hit = i; break; }
            if (hit < 0) return false;
            _lastDivertAt = Time.time; Diverts++;
            var r = RepathEstimate();
            if (Log) Debug.Log($"[W3Walk] t={Time.time:F3} the walk the server runs steps on a portal's area at ({_walkCells[hit].x},{_walkCells[hit].y}): diverting from ({r.x},{r.y})");
            if (!_trip.Plan(r.x, r.y)) { Fail(_trip.FailReason); return true; }
            if (_pendingRec != null && _pending) _pendingRec.AnsweredAt = Time.time; // superseded, not refused
            _pending = false;
            int n = Starts(true);
            int k = _trip.NextLeg(_sx, _sy, n);
            if (k < 0) k = 0; // no leg keeps off it from every start: stop on the re-path cell, then go on from there
            SendLeg(k, n, true);
            return true;
        }

        // ================================================================== the server's unit
        private void SyncTracked(Unit self)
        {
            if (self == _tracked) return;
            _tracked = self; _srvMoving = false; _walkCells.Clear(); _walkReach.Clear();
            _srvX = Mathf.RoundToInt(self.Cell.x); _srvY = Mathf.RoundToInt(self.Cell.y);
        }

        private void OnSelfMove(int x0, int y0, int x1, int y1, uint start)
        {
            var self = Self?.Invoke(); var gat = Gat?.Invoke();
            if (self == null || gat == null) return;
            SyncTracked(self);
            // the server's steps: path_search A* from its cell (players walk with flag 0: unit.cpp:268 with walk_easy 0)
            var prevCells = new List<Vector2Int>(_walkCells); var prevReach = new List<int>(_walkReach); uint prevStart = _walkStart; bool prevMoving = _srvMoving;
            _walkCells.Clear(); _walkReach.Clear();
            _walkCells.Add(new Vector2Int(x0, y0)); _walkReach.Add(0);
            _walkSpeed = Mathf.Max(1, self.Speed);
            var wp = new RoPathSearch.WalkPath();
            if (RoPathSearch.Search(wp, gat, x0, y0, x1, y1, 0))
            {
                int cx = x0, cy = y0, t = 0;
                for (int i = 0; i < wp.Len; i++)
                {
                    int d = wp.Path[i];
                    if (d < 0 || d > 7) break;
                    cx += RoPathSearch.DirX[d]; cy += RoPathSearch.DirY[d];
                    t += RoPathSearch.Diagonal(d) ? _walkSpeed * RoPathSearch.MoveDiagonalCost / RoPathSearch.MoveCost : _walkSpeed; // unit.cpp:226-229
                    _walkCells.Add(new Vector2Int(cx, cy)); _walkReach.Add(t);
                }
            }
            else if (x0 != x1 || y0 != y1) { _walkCells.Add(new Vector2Int(x1, y1)); _walkReach.Add(Mathf.RoundToInt(Vector2.Distance(new Vector2(x0, y0), new Vector2(x1, y1)) * _walkSpeed)); }
            _walkStart = start; _walkRecvLocal = Time.time; _srvMoving = true;
            _srvX = x1; _srvY = y1; SrvWalkSeq++;
            if (_trip == null) return;

            // whose walk is it?
            LegRecord rec = null;
            for (int i = _sent.Count - 1; i >= 0; i--) if (_sent[i].X == x1 && _sent[i].Y == y1) { rec = _sent[i]; break; }
            if (rec == null)
            {
                // not ours: the server walks us for its own reasons (a resend while damaged keeps our target: that one is ours;
                // the cell stack limit, unit.cpp:766-781; a confused unit's random target, unit.cpp:891-892)
                if (x0 == _trip.GoalX && y0 == _trip.GoalY && System.Math.Abs(x1 - x0) <= 1 && System.Math.Abs(y1 - y0) <= 1)
                {
                    // the cell stack limit on the destination itself: the walk that reached it ended on a cell another character
                    // or NPC holds, and rAthena walks us on to a free neighbour (official_cell_stack_limit 1, conf/battle/misc.conf:103;
                    // unit.cpp:764-775: unit_walktoxy flag 8 -> map_nearby_freecell range 1). Asking for the taken cell again only
                    // repeats that until the trip fails ("the server keeps walking us elsewhere", seen in the integration run of
                    // 2026-09-30 on payon 159,129): the trip has arrived next to it, and what follows an arrival (talk, pick up) runs.
                    Foreign++; _pending = false; _legRunning = false; _waitForeign = false;
                    if (Log) Debug.Log($"[W3Walk] t={Time.time:F3} the destination ({x0},{y0}) is taken: rAthena walks us on to ({x1},{y1}); arrived next to it");
                    End("arrived", true);
                    return;
                }
                Foreign++; _waitForeign = true; _pending = false; _legRunning = false;
                if (Log) Debug.Log($"[W3Walk] t={Time.time:F3} a walk we did not ask for: ({x0},{y0}) -> ({x1},{y1}); the route resumes where it ends");
                if (++_trip.Stops > 12) Fail("the server keeps walking us elsewhere");
                return;
            }
            _legRunning = true;
            if (rec.Refused && !rec.Accepted)
            {
                // an answer after we took it as refused (the server delayed the walk: canmove_tick, unit.cpp:872-879)
                rec.Accepted = true; rec.FromX = x0; rec.FromY = y0;
                if (!_pending) _trip.OnAccepted(rec.K);
                _waitForeign = false;
                if (Log) Debug.Log($"[W3Walk] t={Time.time:F3} late answer: the leg to route #{rec.K} ({x1},{y1}) was accepted after all, {(Time.time - rec.SentAt) * 1000f:F0} ms after the request");
            }
            else if (rec.AnsweredAt < 0f)
            {
                rec.AnsweredAt = Time.time; rec.Accepted = true; rec.FromX = x0; rec.FromY = y0;
                // continuity: the re-path cell's time on the previous walk's plan vs the new walk's start tick
                if (rec.Chained && prevMoving)
                {
                    int idx = prevCells.IndexOf(new Vector2Int(x0, y0));
                    if (idx >= 0) rec.GapMs = unchecked((int)(start - prevStart)) - prevReach[idx];
                }
                _trip.OnAccepted(rec.K);
                if (rec == _pendingRec) _pending = false;
                _waitForeign = false;
                if (Log) Debug.Log($"[W3Walk] t={Time.time:F3} leg to route #{rec.K} ({x1},{y1}) accepted {(rec.AnsweredAt - rec.SentAt) * 1000f:F0} ms after the request: the server walks from ({x0},{y0}), {_walkCells.Count - 1} steps, {_walkReach[_walkReach.Count - 1]} ms{(rec.GapMs >= 0 ? $", {rec.GapMs} ms after the previous leg's plan reached that cell" : "")}");
            }
        }

        private void OnSelfStop(int x, int y, ushort packet)
        {
            _srvMoving = false; _srvX = x; _srvY = y; _legRunning = false;
            if (packet == 0x0088) SrvStopSeq++;
            if (packet != 0x0088) return; // a knockback / fast move: its 0x0088 follows (0x08d2 has none: the stand below covers it)
            _lastStopAt = Time.time;
            if (_trip == null) return;
            Stops++; _trip.Stops++;
            if (_pending && Time.time - _lastHitAt > 0.5f)
            {
                // the leg in flight re-pathed at a step end and failed (clif_fixpos, unit.cpp:742-743): a refusal
                _pending = false; if (_pendingRec != null) { _pendingRec.Refused = true; _pendingRec.AnsweredAt = Time.time; }
                Refusals++;
                if (Log) Debug.Log($"[W3Walk] t={Time.time:F3} stopped at ({x},{y}) with the leg to ({_pendingX},{_pendingY}) in flight: taken as refused");
                if (!_trip.OnRefused(_pendingK, x, y)) { Fail(_trip.FailReason); return; }
            }
            else
            {
                _pending = false;
                if (Log) Debug.Log($"[W3Walk] t={Time.time:F3} the server stopped us at ({x},{y}): the route resumes from there");
                if (_trip.Stops > 12) { Fail("the server keeps stopping us"); return; }
                if (!_trip.Resume(x, y)) { Fail(_trip.FailReason); return; }
            }
        }

        /// <summary>Server ms since the last walk's moveStartTime: the server clock (ServerClock), or — before it has a sample,
        /// or when its answer is absurd — the local time since the packet came.</summary>
        private int ElapsedMs()
        {
            int total = _walkReach.Count > 0 ? _walkReach[_walkReach.Count - 1] : 0;
            int local = Mathf.RoundToInt((Time.time - _walkRecvLocal) * 1000f);
            if (!ServerClock.Valid) return local;
            int e = ServerClock.MsSince(_walkStart);
            if (e < -500 || e > 4 * total + 3000) return local;
            return e;
        }

        private int TotalMs => _walkReach.Count > 0 ? _walkReach[_walkReach.Count - 1] : 0;
        private bool ServerWalking => _srvMoving && _walkCells.Count > 1 && ElapsedMs() < TotalMs;

        private int IndexAt(int ms)
        {
            int k = 0;
            while (k + 1 < _walkReach.Count && _walkReach[k + 1] <= ms) k++;
            return k;
        }

        /// <summary>The server's unit's cell (its bl->x / bl->y: the last cell reached, unit.cpp:623-625).</summary>
        private Vector2Int ServerCellNow => ServerWalking ? _walkCells[IndexAt(ElapsedMs())] : new Vector2Int(_srvX, _srvY);

        /// <summary>Where a request sent now is re-pathed from: the cell the step in progress reaches, or where it stands.</summary>
        private Vector2Int RepathEstimate()
        {
            if (!ServerWalking) return new Vector2Int(_srvX, _srvY);
            int k = IndexAt(ElapsedMs() + OneWayMs);
            return _walkCells[Mathf.Min(k + 1, _walkCells.Count - 1)];
        }

        private static int OneWayMs => Mathf.Clamp(Mathf.RoundToInt((float)ServerClock.LastRttMs * 0.5f), 0, 1000);
        private int StepMs => _walkSpeed * RoPathSearch.MoveDiagonalCost / RoPathSearch.MoveCost;

        /// <summary>
        /// The cells the server's unit may be on when a request sent now lands, and the one it re-paths from: standing, its
        /// cell; walking, from where it was 150 ms ago (the clock's error) to where the step in progress will be when the
        /// request arrives, and one more (the re-path cell) — for a chained leg, every cell to the end of the walk.
        /// </summary>
        private int Starts(bool first)
        {
            if (!ServerWalking) { _sx[0] = _srvX; _sy[0] = _srvY; return 1; }
            int e = ElapsedMs();
            int lo = IndexAt(e - 150);
            int hi = first ? Mathf.Min(IndexAt(e + OneWayMs + 150) + 1, _walkCells.Count - 1) : _walkCells.Count - 1;
            int n = 0;
            for (int i = lo; i <= hi && n < _sx.Length; i++) { _sx[n] = _walkCells[i].x; _sy[n] = _walkCells[i].y; n++; }
            return n;
        }

        // ================================================================== client wave 10 (w10move): the steering stick's view
        // Game/StickSteer.cs chooses its walk targets from where the SERVER's unit is and what it is walking (never from the
        // drawn body): the same tracking the legs use, read-only.
        /// <summary>Our own ZC_NOTIFY_PLAYERMOVE 0x0087 packets seen (each accepted walk request, each re-path) and our own
        /// ZC_STOPMOVE 0x0088 packets (a stop, a failed re-path: clif_fixpos, rA unit.cpp:742-743).</summary>
        public int SrvWalkSeq { get; private set; }
        public int SrvStopSeq { get; private set; }
        /// <summary>Starts following <paramref name="self"/> when no packet of this map has yet (it stands on its cell).</summary>
        public void SrvTrack(Unit self) { if (self != null) SyncTracked(self); }
        /// <summary>The server's unit is on a walk now (by the server's clock).</summary>
        public bool SrvWalking => ServerWalking;
        /// <summary>The server's unit's cell now (its bl->x / bl->y: the last cell reached).</summary>
        public Vector2Int SrvCell => ServerCellNow;
        /// <summary>Free movement (wfree): where the server's walk ends (the last 0x0087's destination), or the cell it stands on.</summary>
        public Vector2Int SrvWalkEnd => new Vector2Int(_srvX, _srvY);
        /// <summary>Free movement (client wave 26 merge, w26int): where the last 0x0087 walk started (its first cell). A walk rAthena starts by
        /// itself the moment a walk ends on a cell another unit stands on (the cell stack limit, rA unit.cpp:761-781: unit_walktoxy flag 8
        /// to a free neighbour, unit.cpp:850) starts on that very cell; an answer with an empty path (told only us, unit.cpp:180-193) starts where
        /// it ends (StickSteer's settle). Read-only.</summary>
        public Vector2Int SrvLastWalkFrom => _walkCells.Count > 0 ? _walkCells[0] : new Vector2Int(_srvX, _srvY);
        /// <summary>Where a request sent now is re-pathed from: the cell the step in progress reaches when the request lands
        /// (unit_walktoxy_timer, rA unit.cpp:738-744), or the cell it stands on.</summary>
        public Vector2Int SrvRepathCell => RepathEstimate();
        /// <summary>Server ms until the unit reaches <see cref="SrvRepathCell"/> (0 when it stands).</summary>
        public int SrvMsToRepath
        {
            get
            {
                if (!ServerWalking) return 0;
                int e = ElapsedMs(); int k = Mathf.Min(IndexAt(e + OneWayMs) + 1, _walkCells.Count - 1);
                return Mathf.Max(0, _walkReach[k] - e);
            }
        }
        /// <summary>The cells of the server's walk after <see cref="SrvRepathCell"/>, in order (none when it stands or the walk ends there).</summary>
        public void SrvCellsAfterRepath(List<Vector2Int> into)
        {
            into.Clear();
            if (!ServerWalking) return;
            int k = Mathf.Min(IndexAt(ElapsedMs() + OneWayMs) + 1, _walkCells.Count - 1);
            for (int i = k + 1; i < _walkCells.Count; i++) into.Add(_walkCells[i]);
        }
        /// <summary>
        /// Stopping a walk so that EVERYONE sees where it ends. A request for the very cell the unit re-paths from gives an empty
        /// path, and for an empty path the server tells only us (unit_walktoxy_nextcell: clif_walkok to the player, no clif_move
        /// to the area, rA unit.cpp:187-193): the other clients would draw the unit walking on to the old target. So the cell to
        /// ask for is the one AFTER the cell the request is re-pathed from (a one-step path: a move packet to everyone, rA
        /// unit.cpp:241-245), and nothing needs asking when the walk ends there or sooner by itself. The re-path cell is the one
        /// the step in progress reaches when the request lands — the next one when that is less than <paramref name="marginMs"/>
        /// away, so that a request landing a little late still finds a step to shorten. false: nothing to ask.
        /// </summary>
        public bool SrvStopTarget(out Vector2Int cell, int marginMs = 40)
        {
            cell = new Vector2Int(_srvX, _srvY);
            if (!ServerWalking) return false;
            int last = _walkCells.Count - 1;
            int e = ElapsedMs() + OneWayMs; int k = Mathf.Min(IndexAt(e) + 1, last);
            if (_walkReach[k] - e < marginMs && k < last) k++;
            if (k + 1 >= last) return false; // the walk ends on the next cell or this one: it stops there by itself, as everyone knows
            cell = _walkCells[k + 1];
            return true;
        }
        /// <summary>The last round trip to the server, seconds (0.1 before the clock has a sample).</summary>
        public static float SrvRtt { get { float r = (float)ServerClock.LastRttMs / 1000f; return r > 0f ? r : 0.1f; } }

        // ================================================================== the trip, every frame
        private void Update()
        {
            if (_trip != null) Step();
            DrawLine();
        }

        private void Step()
        {
            var self = Self?.Invoke();
            if (self == null || self != _self) { End("cancelled: left the map", false); return; }
            string why = Blocked?.Invoke();
            if (why != null) { End("cancelled: " + why, false); return; }
            float now = Time.time;
            if (UpdatePortals()) return;
            bool walking = ServerWalking;

            if (_pending)
            {
                if (now < _pendingDeadline) return;
                // no answer: unit_walktoxy refused it (it sends nothing, unit.cpp:855-869)
                _pending = false; Refusals++;
                if (_pendingRec != null) { _pendingRec.Refused = true; _pendingRec.AnsweredAt = now; }
                var at = walking ? _walkCells[_walkCells.Count - 1] : new Vector2Int(_srvX, _srvY);
                if (Log) Debug.Log($"[W3Walk] t={now:F3} no answer to the leg to route #{_pendingK} ({_pendingX},{_pendingY}) in {(now - (_pendingRec?.SentAt ?? now)) * 1000f:F0} ms: refused; asking again from ({at.x},{at.y}) with legs of at most {RoRoute.Trip.Lengths[System.Math.Min(_trip.Consecutive + 1, RoRoute.Trip.Lengths.Length - 1)]} steps");
                if (!_trip.OnRefused(_pendingK, at.x, at.y)) { Fail(_trip.FailReason); return; }
            }
            if (_waitForeign) { if (walking) return; _waitForeign = false; if (!_trip.Resume(_srvX, _srvY)) { Fail(_trip.FailReason); return; } }

            // arrived: the server's unit stands on the destination
            if (!walking && _srvX == _trip.GoalX && _srvY == _trip.GoalY) { End("arrived", true); return; }
            // the last leg is running: nothing more to ask
            if (walking && _walkCells[_walkCells.Count - 1] == new Vector2Int(_trip.GoalX, _trip.GoalY)) return;

            int n, k;
            bool chained = walking;
            if (walking && _legRunning)
            {
                // ask for the next leg while the last steps run (lead: the one-way trip and two steps)
                int remaining = TotalMs - ElapsedMs();
                int lead = OneWayMs + Mathf.Max(2 * StepMs, 250);
                if (remaining > lead) return;
                if (now < _retryAt) return;
                n = Starts(false);
                k = _trip.NextLeg(_sx, _sy, n);
                if (k < 0) { _retryAt = now + 0.05f; return; } // no route cell passes from all of them: fewer start cells as it walks on, or wait until it stands at the end
            }
            else if (walking)
            {
                // the trip began while the server walks a walk of its own (an earlier click): ask at once, from the cells the
                // request may find it on
                if (now < _retryAt) return;
                n = Starts(true);
                k = _trip.NextLeg(_sx, _sy, n);
                if (k < 0) { _retryAt = now + 0.05f; return; } // wait until it stands
            }
            else
            {
                if (_trip.Index >= _trip.RouteLen || _trip.RouteX[_trip.Index] != _srvX || _trip.RouteY[_trip.Index] != _srvY)
                    if (!_trip.Resume(_srvX, _srvY)) { Fail(_trip.FailReason); return; }
                n = Starts(true);
                k = _trip.NextLeg(_sx, _sy, n);
                if (k < 0 && (!_trip.Plan(_srvX, _srvY) || (k = _trip.NextLeg(_sx, _sy, n)) < 0)) { Fail(_trip.FailReason ?? "no leg the server would accept from here"); return; }
            }
            if (_sent.Count >= 150) { Fail("too many legs"); return; } // a 400-cell route takes about 30
            SendLeg(k, n, chained);
        }

        private void SendLeg(int k, int starts, bool chained)
        {
            int x = _trip.RouteX[k], y = _trip.RouteY[k];
            float now = Time.time;
            var rec = new LegRecord { Trip = Trips, K = k, X = x, Y = y, Starts = starts, MaxLen = _trip.MaxLen, SentAt = now, Chained = chained };
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < starts; i++) sb.Append(i > 0 ? " " : "").Append(_sx[i]).Append(',').Append(_sy[i]);
            rec.Starts2 = sb.ToString();
            Legs.Add(rec); _sent.Add(rec);
            if (Legs.Count > 500) Legs.RemoveRange(0, Legs.Count - 500);
            _pending = true; _pendingK = k; _pendingX = x; _pendingY = y; _pendingRec = rec;
            // the answer: at once when standing, at the end of the step in progress when walking, up to 2 s later when the server
            // delays the walk (canmove_tick after a hit or a skill: unit_walktoxy flag 4, unit.cpp:872-879)
            float rtt = (float)ServerClock.LastRttMs / 1000f; if (rtt <= 0f) rtt = 0.1f;
            float wait = Mathf.Max(0.35f, 3f * rtt + 0.15f);
            if (chained) wait += Mathf.Min(Mathf.Max(0, TotalMs - ElapsedMs()), 1.5f * StepMs) / 1000f;
            if (now - _lastStopAt < 2.5f || now - _lastHitAt < 2.5f) wait += 2f;
            _pendingDeadline = now + wait;
            if (Log) Debug.Log($"[W3Walk] t={now:F3} leg {Legs.Count} of trip #{Trips}: request ({x},{y}) = route #{k} of {_trip.RouteLen}, at most {rec.MaxLen} steps from {starts} start cell(s) [{rec.Starts2}]{(chained ? ", chained" : "")}; answer expected within {wait * 1000f:F0} ms");
            SendWalk?.Invoke(x, y);
        }

        private void Fail(string why)
        {
            // w4lang: one whole sentence per reason (Sprites/RoRoute.cs's and this file's); the English reason stays in the outcome
            string said;
            switch (why)
            {
                case "no way there from here": said = Lang.T("Stopped walking: no way there from here."); break;
                case "no other way there": said = Lang.T("Stopped walking: no other way there."); break;
                case "the server refused every leg from here": said = Lang.T("Stopped walking: the server refused every leg from here."); break;
                case "the server refused every leg to that cell": said = Lang.T("Stopped walking: the server refused every leg to that cell."); break;
                case "the server keeps walking us elsewhere": said = Lang.T("Stopped walking: the server keeps walking us elsewhere."); break;
                case "the server keeps stopping us": said = Lang.T("Stopped walking: the server keeps stopping us."); break;
                case "no leg the server would accept from here": said = Lang.T("Stopped walking: no leg the server would accept from here."); break;
                case "too many legs": said = Lang.T("Stopped walking: too many legs."); break;
                default: said = Lang.F("Stopped walking: {0}.", why); break;
            }
            Tell?.Invoke(said);
            End("failed: " + why, false);
        }

        private void End(string outcome, bool arrived)
        {
            if (_trip == null) return;
            if (arrived || outcome == "arrived") Arrived++; else if (outcome.StartsWith("failed")) Failed++; else Cancelled++;
            LastOutcome = outcome;
            if (Log) Debug.Log($"[W3Walk] t={Time.time:F3} trip #{Trips} {Tag} ended: {outcome} (legs accepted {_trip.Accepted}, refused {_trip.Refused}, re-plans {_trip.Replanned}, stops {_trip.Stops})");
            string tag = Tag;
            _trip = null; _pending = false; _pendingRec = null; _waitForeign = false; Far = false;
            if (_marker != null) Destroy(_marker);
            _marker = null;
            if (_line != null) _line.SetActive(false);
            Ended?.Invoke(tag, outcome);
        }

        // ================================================================== visuals
        private Vector3 CellPoint(GatFile gat, float x, float y, float lift)
        {
            int cx = Mathf.Clamp(Mathf.FloorToInt(x + 0.5f), 0, gat.Width - 1), cy = Mathf.Clamp(Mathf.FloorToInt(y + 0.5f), 0, gat.Height - 1);
            return new Vector3(x + 0.5f, -gat.Get(cx, cy).AverageHeight * RoSpace.Scale + lift, y + 0.5f);
        }

        /// <summary>A spinning ring on the destination with a thin beam over it (seen from across the table).</summary>
        private void ShowMarker()
        {
            if (_marker != null) Destroy(_marker);
            var root = MapRoot?.Invoke(); var gat = Gat?.Invoke();
            if (root == null || gat == null) return;
            var col = new Color(0.55f, 0.95f, 1f);
            _marker = new GameObject("w3walk-goal");
            _marker.transform.SetParent(root, false);
            _marker.transform.localPosition = CellPoint(gat, _trip.GoalX, _trip.GoalY, 0f);
            var ring = Fx.Ring(_marker.transform, Vector3.zero, 0.8f, col, -1f, "w3walk-goal-ring");
            ring.AddComponent<FxSpin>().Speed = 90f;
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "w3walk-goal-beam";
            var c = beam.GetComponent<Collider>(); if (c != null) Destroy(c); // never in the laser's way
            beam.transform.SetParent(_marker.transform, false);
            beam.transform.localScale = new Vector3(0.08f, 1.6f, 0.08f);
            beam.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var r = beam.GetComponent<Renderer>();
            r.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(col.r, col.g, col.b, 0.55f) };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        }

        /// <summary>The rest of the route as a glowing ribbon on the ground, in map space (it scales with the table): from the
        /// character as drawn, along the server's walk in progress, then the planned route to the destination.</summary>
        private void DrawLine()
        {
            bool show = _trip != null && Far && !(FirstPerson != null && FirstPerson());
            if (!show) { if (_line != null && _line.activeSelf) _line.SetActive(false); return; }
            var root = MapRoot?.Invoke(); var gat = Gat?.Invoke(); var self = Self?.Invoke();
            if (root == null || gat == null || self == null) return;
            if (_line == null || _line.transform.parent != root) BuildLine(root);
            _pts.Clear();
            const float lift = 0.2f; // above the cell's mean height: the ground between cell centres can be higher on slopes
            _pts.Add(new Vector3(self.DrawnCell.x + 0.5f, self.transform.localPosition.y + lift, self.DrawnCell.y + 0.5f)); // client wave 10 (w10move): from the body as drawn
            int from = _trip.Index;
            if (ServerWalking)
            {
                int k = IndexAt(ElapsedMs());
                for (int i = k + 1; i < _walkCells.Count; i++) _pts.Add(CellPoint(gat, _walkCells[i].x, _walkCells[i].y, lift));
                // continue from the route cell the walk ends on
                var end = _walkCells[_walkCells.Count - 1];
                for (int i = System.Math.Max(0, from - 2); i < _trip.RouteLen && i <= from + 2; i++) if (_trip.RouteX[i] == end.x && _trip.RouteY[i] == end.y) { from = i; break; }
            }
            for (int i = from + 1; i < _trip.RouteLen; i++) _pts.Add(CellPoint(gat, _trip.RouteX[i], _trip.RouteY[i], lift));
            FillRibbon(_pts, 0.3f);
            _line.SetActive(true);
        }

        private void BuildLine(Transform root)
        {
            if (_line != null) Destroy(_line);
            _line = new GameObject("w3walk-route");
            _line.transform.SetParent(root, false);
            _lineMesh = new Mesh { name = "w3walk-route" }; _lineMesh.MarkDynamic();
            _line.AddComponent<MeshFilter>().sharedMesh = _lineMesh;
            var r = _line.AddComponent<MeshRenderer>();
            if (_lineMat == null)
            {
                // soft across its width; alpha-blended (an additive glow vanished on payon's bright grass in the smoke's shots)
                var tex = new Texture2D(4, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "w3walk-route" };
                for (int v = 0; v < 32; v++) { float d = Mathf.Abs((v + 0.5f) / 16f - 1f); float a = Mathf.Clamp01(1f - d * d); for (int u = 0; u < 4; u++) tex.SetPixel(u, v, new Color(1, 1, 1, a)); }
                tex.Apply(false);
                _lineMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex, color = new Color(0.3f, 0.85f, 1f, 0.85f), name = "w3walk-route" };
            }
            r.sharedMaterial = _lineMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        }

        private readonly List<Vector3> _v = new List<Vector3>(); private readonly List<Vector2> _uv = new List<Vector2>(); private readonly List<int> _tri = new List<int>();
        private readonly List<Color> _col = new List<Color>();
        private void FillRibbon(List<Vector3> p, float width)
        {
            _v.Clear(); _uv.Clear(); _tri.Clear();
            for (int i = 0; i < p.Count; i++)
            {
                var a = p[Mathf.Max(0, i - 1)]; var b = p[Mathf.Min(p.Count - 1, i + 1)];
                var d = b - a; d.y = 0f;
                var side = d.sqrMagnitude > 1e-6f ? new Vector3(-d.z, 0f, d.x).normalized * (width * 0.5f) : Vector3.zero;
                _v.Add(p[i] - side); _v.Add(p[i] + side);
                _uv.Add(new Vector2(i, 0f)); _uv.Add(new Vector2(i, 1f));
                if (i > 0) { int q = 2 * i; _tri.Add(q - 2); _tri.Add(q - 1); _tri.Add(q + 1); _tri.Add(q - 2); _tri.Add(q + 1); _tri.Add(q); }
            }
            _col.Clear(); for (int i = 0; i < _v.Count; i++) _col.Add(Color.white);
            _lineMesh.Clear();
            _lineMesh.SetVertices(_v); _lineMesh.SetUVs(0, _uv); _lineMesh.SetColors(_col); _lineMesh.SetTriangles(_tri, 0);
            _lineMesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (_marker != null) Destroy(_marker);
            if (_line != null) Destroy(_line);
        }
    }
}
