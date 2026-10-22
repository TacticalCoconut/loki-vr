using System.Collections.Generic;
using Loki.Formats;
using Loki.Sprites;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 10 (w10move): the walking stick steers in any direction (Smooth movement; Classic keeps Game/Targeting.cs's
    /// cell-target stick). The server still walks our character cell by cell along path_search's steps and nothing else
    /// (rA unit.cpp:254-341, 542-790; the module the same, server/src/unit.rs): what changes is which cells the client asks for
    /// and where the body is drawn (Sprites/Unit.Smooth.cs, <see cref="Unit.Steer"/>).
    ///
    /// What a walk request does on the server, read end to end (rAthena e985006; the module's parse.rs:32-77 and
    /// unit.rs:459-520 are the same line for line):
    ///  - clif_parse_WalkToXY (clif.cpp:11379-11426) has no rate limit; it drops a request while dead / in a dialog / running,
    ///    and one for the cell exactly one to the WEST of the unit when a character or NPC stands there (:11402-11405).
    ///  - unit_walktoxy (unit.cpp:841-916): path_search from the cell the unit last reached must succeed within max_walk_path
    ///    (:855-869), else nothing is sent; a walk delay (canmove_tick) under 2 s postpones it (:872-879). Standing, the walk
    ///    starts at once with a ZC_NOTIFY_PLAYERMOVE 0x0087 (:915 → unit_walktoxy_sub → clif_walkok). WALKING, the request only
    ///    stores the target and sets change_walk_target (:894-899): at the end of the step in progress the server re-paths from
    ///    the cell reached and sends a fresh 0x0087 to us and the move to everyone in view (:738-744, 317), or 0x0088 when that
    ///    re-path fails. Several requests within one step: the last target counts, one re-path, one packet.
    /// So a request while walking is cheap for the server (one path_search) but costs one packet to every player in view per
    /// step, and on the module one reducer call on its single core: this class sends AT MOST ONE REQUEST PER SERVER STEP (and a
    /// second one only when the stick turned meanwhile), and none while the walk the server already runs follows the stick's
    /// line for two more steps.
    ///
    /// How it steers. The stick's direction and the drawn body give a line. From the cell the server will re-path from
    /// (<see cref="RouteWalker.SrvRepathCell"/>) the cells closest to that line are listed, step by step (each step one the
    /// server's path may take: a walkable cell, a diagonal only past two free orthogonal cells, rA path.cpp:385-405; never more
    /// than ~69° off the stick, so the unit slides along a wall and stops when pushed straight into one). The target sent is the
    /// farthest of them (up to <see cref="LookAhead"/>) whose server path — the client's RoPathSearch, rAthena's path_search line
    /// for line — begins with those very cells: the server then walks the staircase the body is drawn along, and because the
    /// body's line is fixed while the stick is, the staircase hugs it within half a cell. Where path_search would order the
    /// steps differently (a diagonal first where the line needs it last) only the nearer cells are asked for and the next
    /// request follows a step later.
    /// Letting go shortens the server's walk to one step beyond the cell it re-paths from (<see cref="RouteWalker.SrvStopTarget"/>:
    /// a one-step path, so that every client is told where the unit stops; an empty path would tell only us), and the body eases
    /// from where it is drawn onto that cell.
    ///
    /// Free movement (design note 2026-10-14, "client-authoritative sub-tile stop"): letting go now keeps the body exactly where it is
    /// (<see cref="Unit.Hold"/>) and the SETTLE brings the server's unit to the cell under it, T (rAthena keeps every character on a
    /// cell: at most half a cell off): nothing when the server's walk ends on T; T itself when T is further along that walk (a prefix:
    /// everyone is told); when T is the very cell the server re-paths from (an empty path would tell only us, rA unit.cpp:180-193), one
    /// step beyond first (<see cref="RouteWalker.SrvStopTarget"/>) and then T, one step back; otherwise T. One CZ_CHANGE_DIRECTION2 at the
    /// end when the server walked, so the others see the facing of the body, not of the last correction step. A walk or a stop the
    /// settle did not ask for, a trip, the player acting (<see cref="Busy"/>), not being able to walk, 3 requests or 3 s: the settle
    /// gives up and the body eases into the square of the cell the server's unit stands on (<see cref="Unit.Unhold"/>).
    ///
    /// The stick leads (design note 2026-10-15): the drawn body walks along the stick at the character's own pace
    /// (Sprites/Unit.Smooth.cs FreeSteerStep, held back only beyond <see cref="Unit.FreeLeadSoft"/> from the server's unit) and the line the
    /// requests follow is no longer the one through the body but the one from the cell the server re-paths from to where the body will be
    /// <see cref="AimAhead"/> later (<see cref="AimAt"/>: on an arc when the stick turns), so the server's unit cuts inside a curve and keeps up.
    /// A sharp turn of the stick is followed within ~10 ms instead of 50 (no hook on a reversal).
    /// </summary>
    public sealed class StickSteer
    {
        /// <summary>How many cells ahead a target may be (6 steps keeps the server walking across local RTT even at ramped OOC speed).</summary>
        public int LookAhead = 6;
        /// <summary>A step more than this far off the stick's direction (cosine) is not taken: cos 69.5°.</summary>
        public const float SlideCos = 0.35f;
        /// <summary>Two requests are never closer than this, seconds.</summary>
        public const float MinInterval = 0.06f;

        public bool Active { get; private set; }
        /// <summary>The steering direction in cell space (unit vector), smoothed over ~50 ms.</summary>
        public Vector2 Dir { get; private set; }

        // ---- diagnostics (the w10move smoke reads them)
        public sealed class Request
        {
            public float At; public int FromX, FromY, X, Y, Cells, Prefix; public bool Exact, Walking, Correction, Stop, Settle;
            /// <summary>0 waiting, 1 answered by a walk packet, 2 answered by a stop (0x0088: the re-path failed), 3 no answer in time.</summary>
            public int Answer; public float AnsweredAfter = -1f;
        }
        public readonly List<Request> Log = new List<Request>();
        public int Requests, Corrections, Stops, Unanswered, StopAnswers, Skipped, NoWay;
        public float SteeredSeconds;
        public static bool LogLines;

        private Vector2Int _reqRepath, _reqTarget, _reqFirst; private float _reqAt = -10f, _reqStale; private int _reqSeq = -1, _reqStopSeq; private bool _reqOpen;
        private Request _reqRec;
        private bool _blockedStopSent;
        private bool _nope, _nopeWalking; private Vector2Int _nopeRepath, _nopeA, _nopeB; private int _nopeN;
        private readonly Vector2Int[] _line = new Vector2Int[8];
        private readonly List<Vector2Int> _plan = new List<Vector2Int>();
        private static readonly RoPathSearch.WalkPath Path = new RoPathSearch.WalkPath();

        /// <summary>The stick is held: <paramref name="dir"/> is its direction in cell space (unit vector).</summary>
        public void Hold(Unit self, GatFile gat, RouteWalker routes, Vector2 dir, float dt, System.Action<int, int> send, System.Func<int, int, bool> occupied)
        {
            if (self == null || gat == null || routes == null) return;
            float now = Time.time;
            if (!Active) { if (Settling) CancelSettle(self, "the stick again"); Active = true; Dir = dir; _reqOpen = false; _blockedStopSent = false; _nope = false; _turnRate = 0f; }
            else
            {
                // smoothed over ~50 ms against a thumb's noise; (wmove) a sharp turn follows sooner, down to 10 ms from 150° (a reversal swept
                // through the old heading for ~0.1 s: the showcase's hook)
                var prev = Dir; float turn = Vector2.Angle(Dir, dir);
                float tc = Mathf.Lerp(0.05f, 0.01f, Mathf.InverseLerp(60f, 150f, turn));
                var d = Vector2.Lerp(Dir, dir, 1f - Mathf.Exp(-dt / tc)); Dir = d.sqrMagnitude > 1e-6f ? d.normalized : dir;
                // the stick's turn rate (smoothed over 0.1 s), for the aim (AimAt); a sharp turn is no curve: nothing to predict
                if (turn > 60f) _turnRate = 0f;
                else if (dt > 1e-4f) _turnRate += (Vector2.SignedAngle(prev, Dir) * Mathf.Deg2Rad / dt - _turnRate) * (1f - Mathf.Exp(-dt / 0.1f));
            }
            self.Steer = Dir;
            SteeredSeconds += dt;
            routes.SrvTrack(self);
            bool walking = routes.SrvWalking;
            Vector2Int r = routes.SrvRepathCell;
            Settle(routes, now);

            AimAt(self, r);
            int n = Line(gat, r, _aimSpot, _aimDir);
            if (_reqOpen)
            {
                // one request per server step; a second one only when the stick turned and this step can still take it
                if (n > 0 && r == _reqRepath && _line[0] != _reqFirst && now - _reqAt >= MinInterval) Ask(self, gat, routes, r, n, walking, now, send, occupied, true);
                return;
            }
            if (n == 0)
            {
                // nothing walkable that way: stop the walk the server still runs (once), then stand
                NoWay++;
                if (walking && !_blockedStopSent && now - _reqAt >= MinInterval) { _blockedStopSent = true; SendStop(routes, now, send); }
                return;
            }
            _blockedStopSent = false;
            if (walking)
            {
                // does the walk the server runs already follow the line? three more steps of it: nothing to ask yet
                routes.SrvCellsAfterRepath(_plan);
                int agree = 0; while (agree < _plan.Count && agree < n && _plan[agree] == _line[agree]) agree++;
                if (agree >= Mathf.Min(3, n)) { Skipped++; return; }
            }
            if (now - _reqAt < MinInterval) return;
            // asked about this very step already and there was nothing to send: not again until something changes
            if (_nope && r == _nopeRepath && n == _nopeN && walking == _nopeWalking && _line[0] == _nopeA && (n < 2 || _line[1] == _nopeB)) return;
            int sent = Requests;
            Ask(self, gat, routes, r, n, walking, now, send, occupied, false);
            _nope = Requests == sent;
            if (_nope) { _nopeRepath = r; _nopeN = n; _nopeWalking = walking; _nopeA = _line[0]; _nopeB = n >= 2 ? _line[1] : _line[0]; }
        }

        /// <summary>The stick was let go (or walking became impossible: <paramref name="stop"/> false sends nothing).</summary>
        public void Release(Unit self, RouteWalker routes, System.Action<int, int> send, bool stop = true)
        {
            if (self != null) self.Steer = null;
            if (!Active) return;
            Active = false;
            if (!stop || routes == null || self == null) { _reqOpen = false; return; }
            float now = Time.time;
            routes.SrvTrack(self);
            Settle(routes, now);
            // free movement (wfree): the body stays where it is; the settle brings the server's unit to the cell under it (SettleTick)
            if (Unit.Smooth && self.IsSelf) { BeginSettle(self, routes, now); return; }
            if (_reqOpen && _reqPath.Count > 0 && !_reqRec.Stop)
            {
                // a request is still in flight: the server will re-path to ITS target at the end of the step in progress (or start
                // that walk, if it stood), so the walk the client sees now is not the one to shorten. Ask for the second cell
                // of that path instead (its only cell when it has one): a step or two and no more, and never an empty path
                // (an empty path tells only us: RouteWalker.SrvStopTarget)
                var c = _reqPath[Mathf.Min(1, _reqPath.Count - 1)];
                Note(now, routes, _reqRepath, c, Mathf.Min(2, _reqPath.Count), 1, true, routes.SrvWalking, false, true);
                Stops++;
                send(c.x, c.y);
            }
            else if (routes.SrvWalking) SendStop(routes, now, send);
            _reqOpen = false;
        }

        // ================================================================== free movement (wfree): the settle
        /// <summary>The player acts (attacks, casts, talks, picks up ...): a settle gives way (a walk request ends an attack: rA unit.cpp:873-874).</summary>
        public System.Func<bool> Busy;
        /// <summary>Sends CZ_CHANGE_DIRECTION2 with a client direction (0-7) (LokiGame.W10Move.cs).</summary>
        public System.Action<int> Face;
        public const int SettleMaxRequests = 3;
        public const float SettleSeconds = 3f;
        /// <summary>The server's unit is being brought to the cell under the held body.</summary>
        public bool Settling { get; private set; }
        public int Settles, SettlesOk, SettlesGaveUp, SettleRequests, SettleBacks, SettleTurns;
        /// <summary>(client wave 26 merge, w26int) Settles whose request was answered with an empty path (told only us) and that
        /// stepped to a neighbour and back so that everyone sees us on the cell (<see cref="SettleTick"/>).</summary>
        public int SettlesUntold;
        /// <summary>One settle (the w10move / wfree smokes read them).</summary>
        public sealed class SettleRecord
        {
            public float At, Took = -1f; public Vector2 Spot; public Vector2Int Cell, End; public int Requests; public bool Ok, Back, Turned; public string Why = "";
            /// <summary>(client wave 26 merge, w26int) A request of this settle was answered with an empty path: only we were told.</summary>
            public bool Untold;
        }
        public readonly List<SettleRecord> Settled = new List<SettleRecord>();
        public SettleRecord LastSettle => Settled.Count > 0 ? Settled[Settled.Count - 1] : null;
        private SettleRecord _settle; private int _settleWalkSeq, _settleStopSeq; private bool _settleStepped;
        private bool _settleUntold; // (client wave 26 merge, w26int) the last answer to a settle request was an empty path: told only us
        // (wmove) the stick's last request was still in flight at the release: the settle's first request supersedes it (_settleSupersede: not
        // yet sent; _settleSuperseding: sent, and the stick's request may still be answered first)
        private bool _settleSupersede, _settleSuperseding;
        /// <summary>(wmove) Settles whose first request superseded the stick's last one, still in flight at the release.</summary>
        public int SettlesSuperseded;

        /// <summary>The stick was let go in free movement: hold the body, start the settle.</summary>
        private void BeginSettle(Unit self, RouteWalker routes, float now)
        {
            var at = self.DrawnCell;
            var cell = new Vector2Int(Mathf.FloorToInt(at.x + 0.5f), Mathf.FloorToInt(at.y + 0.5f));
            self.Hold(at);
            Settling = true; Settles++; _settleStepped = false; _settleUntold = false;
            _settleSupersede = _reqOpen && _reqRec != null && !_reqRec.Settle && !_reqRec.Stop; _settleSuperseding = false; // (wmove)
            _settleWalkSeq = routes.SrvWalkSeq; _settleStopSeq = routes.SrvStopSeq;
            _settle = new SettleRecord { At = now, Spot = at, Cell = cell };
            Settled.Add(_settle); if (Settled.Count > 500) Settled.RemoveRange(0, Settled.Count - 500);
            if (LogLines) Debug.Log($"[W10] t={now:F3} free stop at ({at.x:F2},{at.y:F2}): the server to ({cell.x},{cell.y}); it is {(routes.SrvWalking ? "walking, re-paths at " + routes.SrvRepathCell : "standing at " + routes.SrvCell)}{(_reqOpen ? ", a request in flight" : "")}");
        }

        /// <summary>Ends the settle: <paramref name="ok"/> the server stands on the cell under the body (it stays exactly there), else
        /// the body eases into the square of the cell the server's unit is on.</summary>
        private void EndSettle(Unit self, RouteWalker routes, bool ok, string why)
        {
            if (!Settling) return;
            Settling = false;
            var srv = routes != null ? (routes.SrvWalking ? routes.SrvWalkEnd : routes.SrvCell) : _settle.Cell;
            if (ok) SettlesOk++; else SettlesGaveUp++;
            if (_settle != null) { _settle.Ok = ok; _settle.Why = why; _settle.End = srv; _settle.Took = Time.time - _settle.At; }
            if (self != null && self.Held.HasValue)
            {
                // the server walked: its facing is its last step's; the others are told the body's own facing (rA clif.cpp:11609-11620)
                if (ok && _settleStepped && Face != null && self.Sprite != null) { Face(self.Sprite.Direction & 7); SettleTurns++; if (_settle != null) _settle.Turned = true; }
                self.Unhold(srv);
            }
            if (LogLines) Debug.Log($"[W10] t={Time.time:F3} free stop {(ok ? "settled" : "gave up")} ({why}): the server {(routes != null && routes.SrvWalking ? "walks to" : "stands on")} ({srv.x},{srv.y}), the body at ({(self != null ? self.DrawnCell.x : 0f):F2},{(self != null ? self.DrawnCell.y : 0f):F2}) after {(_settle != null ? _settle.Took : 0f):F2} s, {(_settle != null ? _settle.Requests : 0)} request(s)");
        }

        /// <summary>The stick steers again (from where the body stands) or Classic took over: the settle just stops.</summary>
        public void CancelSettle(Unit self, string why)
        {
            if (!Settling) return;
            Settling = false;
            if (_settle != null) { _settle.Why = "cancelled: " + why; _settle.Took = Time.time - _settle.At; }
            if (self != null) self.DropHold();
        }

        /// <summary>Every frame (Game/Targeting.cs, free movement): one decision of the settle.</summary>
        public void SettleTick(Unit self, GatFile gat, RouteWalker routes, System.Action<int, int> send, System.Func<int, int, bool> occupied, bool canMove)
        {
            if (!Settling) return;
            float now = Time.time;
            if (self == null || routes == null || gat == null) { Settling = false; return; }
            if (!self.Held.HasValue) { EndSettle(self, routes, false, "the body was placed (a warp, the setting)"); return; }
            if (!canMove) { EndSettle(self, routes, false, "walking is not possible now"); return; }
            if (routes.Active) { EndSettle(self, routes, false, "a walk to somewhere began"); return; }
            if (Busy != null && Busy()) { EndSettle(self, routes, false, "the player acts"); return; }
            routes.SrvTrack(self);
            Settle(routes, now);
            if (_reqOpen)
            {
                // the request in flight (the stick's last, or ours) is answered at the end of the step in progress. (wmove) The stick's last one,
                // while the server walks: rAthena takes the LAST target asked for at the end of the step in progress (rA unit.cpp:894-899, the
                // re-path :738-744), so the settle asks at once instead of letting the server walk a step towards the stick's target and back
                // (seen: 2 cells out and back after the tight circles of the showcase)
                if (!_settleSupersede || !routes.SrvWalking) return;
                _settleSupersede = false;
                var T0 = _settle.Cell; var r0 = routes.SrvRepathCell; Vector2Int c0;
                int sent0 = _settle.Requests;
                // re-pathing on T itself would be an empty path, told only us: one step out first (the walk's next cell, else a free neighbour), then back
                if (r0 == T0) { if (SettleOut(self, gat, routes, T0, occupied, out c0)) SettleAsk(self, gat, routes, c0, now, send, occupied, true); }
                else SettleAsk(self, gat, routes, T0, now, send, occupied, false);
                if (_settle.Requests > sent0) { _settleSuperseding = true; SettlesSuperseded++; }
                return;
            }
            if (routes.SrvStopSeq != _settleStopSeq) { EndSettle(self, routes, false, "the server stopped the walk"); return; }
            if (routes.SrvWalkSeq != _settleWalkSeq) { EndSettle(self, routes, false, "a walk the settle did not ask for"); return; }
            var T = _settle.Cell;
            if (now - _settle.At > SettleSeconds) { EndSettle(self, routes, false, "3 s"); return; }
            if (routes.SrvWalking)
            {
                if (routes.SrvWalkEnd == T) return; // it ends on T by itself
                var r = routes.SrvRepathCell;
                routes.SrvCellsAfterRepath(_plan);
                if (_plan.Contains(T)) { SettleAsk(self, gat, routes, T, now, send, occupied, false); return; } // a prefix of its walk
                if (r == T)
                {
                    // asking for T now would be an empty path: only we would be told (rA unit.cpp:180-193). One step beyond first, then back.
                    // (wmove: the step out goes to T's free neighbour nearest the held body when there is one: SettleOut)
                    if (SettleOut(self, gat, routes, T, occupied, out var c)) SettleAsk(self, gat, routes, c, now, send, occupied, true);
                    return; // (else the walk ends on the next cell by itself: then back from there)
                }
                SettleAsk(self, gat, routes, T, now, send, occupied, false);
                return;
            }
            var s = routes.SrvCell;
            if (s == T)
            {
                if (_settleUntold)
                {
                    // (client wave 26 merge, w26int) our request for T landed a few ms before the server's step INTO T ended (the re-path cell
                    // estimate is a step late at that edge): the server re-pathed from T itself, an empty path, and only we were told; the
                    // others still see the walk the stick asked for (seen: a friend drew us 2 cells on, smoke wfree E). As the r == T case
                    // above: one step to a free neighbour (the one nearest the body) and back to T, both walks everyone is told of.
                    _settleUntold = false; _settle.Untold = true; SettlesUntold++;
                    if (SettleNeighbour(self, gat, T, occupied, out var nb))
                    {
                        if (LogLines) Debug.Log($"[W10] t={now:F3} free stop: the server re-pathed from ({T.x},{T.y}) itself (an empty path, told only us): one step to ({nb.x},{nb.y}) and back");
                        SettleAsk(self, gat, routes, nb, now, send, occupied, true); return;
                    }
                    EndSettle(self, routes, false, "told only us, no free cell next to it"); return;
                }
                // the client's own walk of the unit (Unit.Cell, on the server's clock with its catch-up) may arrive a little after the server's:
                // the body is let go when that stands too, so nothing of the walk's last steps moves it
                if ((self.Walking || self.Sliding) && now - _settle.At < SettleSeconds) return;
                EndSettle(self, routes, true, "on the cell"); return;
            }
            if (_settle.Requests >= SettleMaxRequests) { EndSettle(self, routes, false, SettleMaxRequests + " requests"); return; }
            SettleAsk(self, gat, routes, T, now, send, occupied, false);
        }

        /// <summary>(client wave 26 merge, w26int) A walkable cell next to <paramref name="c"/> that nobody stands on and the server reaches in
        /// one step from it (path_search with the steering's flag 0), the nearest to the held body first.</summary>
        private static bool SettleNeighbour(Unit self, GatFile gat, Vector2Int c, System.Func<int, int, bool> occupied, out Vector2Int nb, Vector2Int? avoid = null)
        {
            nb = c; float best = float.MaxValue; var at = self.DrawnCell;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int x = c.x + dx, y = c.y + dy;
                    if (avoid.HasValue && avoid.Value.x == x && avoid.Value.y == y) continue; // (wmove)
                    if (!gat.InBounds(x, y) || !gat.Get(x, y).Walkable || (occupied != null && occupied(x, y))) continue;
                    if (!RoPathSearch.Search(Path, gat, c.x, c.y, x, y, 0) || Path.Len != 1) continue;
                    float d = Vector2.Distance(at, new Vector2(x, y));
                    if (d < best) { best = d; nb = new Vector2Int(x, y); }
                }
            return best < float.MaxValue;
        }

        /// <summary>(wmove) The step out when the server would re-path on T itself (an empty path, told only us): T's free neighbour nearest the held
        /// body, so that the server's unit goes as little as possible away from it (the walk's own next cell was up to 1.8 cells from the body
        /// after the showcase's diagonals), but not the walk's next cell (a request landing a step late would re-path there: empty again); else
        /// that next cell, as before (<see cref="RouteWalker.SrvStopTarget"/>).</summary>
        private bool SettleOut(Unit self, GatFile gat, RouteWalker routes, Vector2Int T, System.Func<int, int, bool> occupied, out Vector2Int c)
        {
            routes.SrvCellsAfterRepath(_plan);
            if (SettleNeighbour(self, gat, T, occupied, out c, _plan.Count > 0 ? _plan[0] : (Vector2Int?)null)) return true;
            return routes.SrvStopTarget(out c);
        }

        private void SettleAsk(Unit self, GatFile gat, RouteWalker routes, Vector2Int target, float now, System.Action<int, int> send, System.Func<int, int, bool> occupied, bool beyond)
        {
            var srv = routes.SrvCell;
            // the server checks the path from the cell its unit last REACHED (rA unit.cpp:855-869) and drops a request for the cell one to the
            // west when someone stands there (rA clif.cpp:11402-11405): never ask what it would drop
            if (!RoPathSearch.Search(Path, gat, srv.x, srv.y, target.x, target.y, 0) || Path.Len > 17) { EndSettle(self, routes, false, $"no path the server takes to ({target.x},{target.y})"); return; }
            if (target.x == srv.x - 1 && target.y == srv.y && occupied != null && occupied(target.x, target.y)) { EndSettle(self, routes, false, "the cell to the west is taken"); return; }
            if (_settle.Requests >= SettleMaxRequests) { EndSettle(self, routes, false, SettleMaxRequests + " requests"); return; }
            bool walking = routes.SrvWalking;
            Note(now, routes, walking ? routes.SrvRepathCell : srv, target, 0, 0, true, walking, false, true);
            _reqRec.Settle = true; _reqFirst = target; _reqPath.Clear();
            _settle.Requests++; SettleRequests++; if (beyond) { SettleBacks++; _settle.Back = true; }
            if (LogLines) Debug.Log($"[W10] t={now:F3} free stop: ask ({target.x},{target.y}){(beyond ? " (one step beyond: an empty path would tell only us; back afterwards)" : "")}, the server {(walking ? "walking, re-paths at " + routes.SrvRepathCell : "standing at " + srv)}");
            send(target.x, target.y);
        }

        private void SendStop(RouteWalker routes, float now, System.Action<int, int> send)
        {
            // one step beyond the cell the request is re-pathed from, so that the other clients are told too (RouteWalker.SrvStopTarget);
            // nothing when the walk ends there by itself
            if (!routes.SrvStopTarget(out var c)) return;
            Note(now, routes, routes.SrvRepathCell, c, 1, 1, true, true, false, true);
            Stops++;
            send(c.x, c.y);
        }

        /// <summary>Has the server answered the request in flight? (a walk packet of ours, a stop, or too long without either)</summary>
        private void Settle(RouteWalker routes, float now)
        {
            if (!_reqOpen) return;
            if (routes.SrvWalkSeq != _reqSeq)
            {
                // (wmove) a settle request that superseded the stick's last one: a walk that does not end on the settle's target is the answer to the
                // stick's request, which landed first; neither ours nor "a walk the settle did not ask for": ours comes at the end of that step
                if (Settling && _settleSuperseding && _reqRec != null && _reqRec.Settle && routes.SrvWalkEnd != _reqTarget)
                {
                    _settleSuperseding = false; _reqSeq = routes.SrvWalkSeq; _settleWalkSeq = routes.SrvWalkSeq; _settleStopSeq = routes.SrvStopSeq;
                    _reqStale += routes.SrvMsToRepath / 1000f + 0.25f;
                    if (LogLines) Debug.Log($"[W10] t={now:F3} free stop: the stick's last request answered first (to {routes.SrvWalkEnd}); the settle's ({_reqTarget.x},{_reqTarget.y}) is taken at the end of that step");
                    return;
                }
                _settleSuperseding = false;
                _reqOpen = false; if (_reqRec != null) { _reqRec.Answer = 1; _reqRec.AnsweredAfter = now - _reqRec.At; }
                if (Settling)
                {
                    _settleWalkSeq = routes.SrvWalkSeq; _settleStopSeq = routes.SrvStopSeq; if (_reqRec != null && _reqRec.Settle) _settleStepped = true;
                    // (client wave 26 merge, w26int) an answer that starts where it ends is an empty path: rAthena tells only us (clif_walkok,
                    // rA unit.cpp:180-193; unit_walktoxy_sub :254-317 sends no clif_move): the others still see the walk before it
                    if (_reqRec != null && _reqRec.Settle) _settleUntold = routes.SrvLastWalkFrom == routes.SrvWalkEnd;
                }
            }
            else if (routes.SrvStopSeq != _reqStopSeq) { _reqOpen = false; StopAnswers++; if (_reqRec != null) { _reqRec.Answer = 2; _reqRec.AnsweredAfter = now - _reqRec.At; } if (Settling) { _settleWalkSeq = routes.SrvWalkSeq; _settleStopSeq = routes.SrvStopSeq; } }
            else if (now - _reqAt > _reqStale)
            {
                _reqOpen = false; Unanswered++;
                if (_reqRec != null) _reqRec.Answer = 3;
                if (LogLines) Debug.Log($"[W10] t={now:F3} no answer to the walk request ({_reqTarget.x},{_reqTarget.y}) in {(now - _reqAt) * 1000f:F0} ms");
            }
        }

        private void Note(float now, RouteWalker routes, Vector2Int from, Vector2Int to, int cells, int prefix, bool exact, bool walking, bool correction, bool stop)
        {
            _reqRec = new Request { At = now, FromX = from.x, FromY = from.y, X = to.x, Y = to.y, Cells = cells, Prefix = prefix, Exact = exact, Walking = walking, Correction = correction, Stop = stop };
            Log.Add(_reqRec); if (Log.Count > 2000) Log.RemoveRange(0, Log.Count - 2000);
            _reqAt = now; _reqSeq = routes.SrvWalkSeq; _reqStopSeq = routes.SrvStopSeq; _reqOpen = true;
            _reqRepath = from; _reqTarget = to;
            // the answer: standing, at once (a round trip); walking, at the end of the step in progress
            float rtt = RouteWalker.SrvRtt;
            _reqStale = Mathf.Max(0.35f, 3f * rtt + 0.15f) + (walking ? routes.SrvMsToRepath / 1000f : 0f);
            Requests++;
        }

        // ================================================================== (wmove) the requests aim ahead of the body
        /// <summary>How far ahead of the drawn body the requests aim, seconds of its walk (+ half the round trip, at most 0.1 s). The body
        /// leads (Sprites/Unit.Smooth.cs FreeSteerStep); the server's unit re-paths only at the end of the step in progress and walks a staircase
        /// up to 7 % slower than the body's straight line, so aimed at the body itself it fell behind on every curve. Aimed at where the body
        /// will be, it cuts inside a curve and keeps up (modelled with rAthena's walk and path_search: circles of radius 1.4-5.5, a figure eight,
        /// a weave, a spiral at full pace with leads under 0.85 cell at 0.2 s; 0.1 s fell behind, 0.5 s overshot).</summary>
        public const float AimAhead = 0.2f;
        /// <summary>Diagnostics: where the requests aim now (cells), and the stick's turn rate (radians a second, counter-clockwise).</summary>
        public Vector2 AimSpot => _aimSpot;
        public float TurnRate => _turnRate;
        private Vector2 _aimSpot, _aimDir; private float _turnRate;

        /// <summary>The spot the body reaches <see cref="AimAhead"/> from now if the stick keeps turning as it does (an arc), and the
        /// direction from the cell the server re-paths from to it: the line the requests follow.</summary>
        private void AimAt(Unit self, Vector2Int r)
        {
            float pace = 1000f / Mathf.Max(self.Speed, 1), tau = AimAhead + 0.5f * Mathf.Min(0.2f, RouteWalker.SrvRtt);
            float w = Mathf.Clamp(_turnRate, -pace, pace); // (no curve tighter than a radius of 1 cell)
            float th = w * tau;
            float len = Mathf.Abs(w) > 1e-3f ? 2f * pace * Mathf.Sin(th * 0.5f) / w : pace * tau;
            float c = Mathf.Cos(th * 0.5f), sn = Mathf.Sin(th * 0.5f);
            var ad = new Vector2(Dir.x * c - Dir.y * sn, Dir.x * sn + Dir.y * c);
            _aimSpot = self.DrawnCell + ad * len;
            var v = _aimSpot - (Vector2)r; float m = v.magnitude;
            _aimDir = m > 0.3f ? v / m : Dir;
        }

        /// <summary>The cells closest to the line through <paramref name="origin"/> in direction <paramref name="d"/>, from
        /// <paramref name="from"/> on, one legal step at a time; returns how many (≤ LookAhead).</summary>
        private int Line(GatFile gat, Vector2Int from, Vector2 origin, Vector2 d)
        {
            int n = 0; var cur = from; int max = Mathf.Min(LookAhead, _line.Length);
            while (n < max)
            {
                int bx = 0, by = 0; float best = float.NegativeInfinity;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        float along = dx * d.x + dy * d.y;
                        bool diag = dx != 0 && dy != 0;
                        if (along < SlideCos * (diag ? 1.41421356f : 1f)) continue;
                        int nx = cur.x + dx, ny = cur.y + dy;
                        if (gat.Check(nx, ny)) continue;
                        if (diag && (gat.Check(cur.x + dx, cur.y) || gat.Check(cur.x, cur.y + dy))) continue; // a diagonal only past two free orthogonal cells (rA path.cpp:385-405)
                        float px = nx - origin.x, py = ny - origin.y;
                        float score = -Mathf.Abs(px * d.y - py * d.x) + 0.05f * along; // the step that ends closest to the line; of equals, the one that gets further
                        if (score > best) { best = score; bx = dx; by = dy; }
                    }
                if (float.IsNegativeInfinity(best)) break;
                cur = new Vector2Int(cur.x + bx, cur.y + by);
                _line[n++] = cur;
            }
            return n;
        }

        /// <summary>The cells of the server's path from <paramref name="r"/> to <paramref name="t"/> (the client's RoPathSearch: rAthena's
        /// path_search line for line), without <paramref name="r"/>; false when there is none.</summary>
        private static bool PathCells(GatFile gat, Vector2Int r, Vector2Int t, List<Vector2Int> into)
        {
            into.Clear();
            if (!RoPathSearch.Search(Path, gat, r.x, r.y, t.x, t.y, 0)) return false;
            int cx = r.x, cy = r.y;
            for (int i = 0; i < Path.Len; i++)
            {
                int dir = Path.Path[i]; if (dir < 0 || dir > 7) break;
                cx += RoPathSearch.DirX[dir]; cy += RoPathSearch.DirY[dir];
                into.Add(new Vector2Int(cx, cy));
            }
            return true;
        }

        private readonly List<Vector2Int> _cand = new List<Vector2Int>(), _new = new List<Vector2Int>(), _reqPath = new List<Vector2Int>();

        /// <summary>
        /// Chooses and sends the target. In order:
        ///  1. the farthest line cell whose server path begins with the line's first two cells (path_search goes diagonally first
        ///     on open ground, so this holds whenever the line's next step is a diagonal, or the line is straight);
        ///  2. else a cell two steps away whose server path goes through the line's FIRST cell (the line needs a straight step
        ///     before a diagonal and path_search would take them the other way round): the one of those nearest the line, usually
        ///     straight on. The server walks the right step now, and the request of the next step puts the rest right; should
        ///     that request come late, the unit walks on a cell beside the line instead of stopping;
        ///  3. else the line's first cell alone.
        /// Nothing is sent when the server's walk would stay what it is, or when the server would drop the request (no path from
        /// the cell it last reached: unit.cpp:855-869; the cell one to the west with someone on it: clif.cpp:11402-11405).
        /// </summary>
        private void Ask(Unit self, GatFile gat, RouteWalker routes, Vector2Int r, int n, bool walking, float now, System.Action<int, int> send, System.Func<int, int, bool> occupied, bool correction)
        {
            Vector2Int target = default; int cells = 0, prefix = 0; bool exact = false, found = false;
            for (int k = n; k >= 2 && !found; k--)
            {
                if (!PathCells(gat, r, _line[k - 1], _cand)) continue;
                int pre = 0; while (pre < _cand.Count && pre < k && _cand[pre] == _line[pre]) pre++;
                if (pre < 2) continue;
                found = true; target = _line[k - 1]; cells = _cand.Count; prefix = pre; exact = pre == k && _cand.Count == k;
                _new.Clear(); _new.AddRange(_cand);
            }
            if (!found && n >= 3)
            {
                // When the line needs a straight step before a diagonal, path_search takes the diagonal first (within 1 cell of the line)
                // and reaches the exact same cell in k steps. Accepting the full k-step path prevents 2-step starvation on local RTT.
                Vector2 o = _aimSpot, d = _aimDir;
                for (int k = n; k >= 3 && !found; k--)
                {
                    if (!PathCells(gat, r, _line[k - 1], _cand) || _cand.Count != k) continue;
                    bool hugs = true;
                    for (int i = 0; i < _cand.Count; i++)
                        if (Mathf.Abs((_cand[i].x - o.x) * d.y - (_cand[i].y - o.y) * d.x) > 1.08f) { hugs = false; break; }
                    if (!hugs) continue;
                    int pre = 0; while (pre < _cand.Count && pre < k && _cand[pre] == _line[pre]) pre++;
                    found = true; target = _line[k - 1]; cells = _cand.Count; prefix = Mathf.Max(1, pre); exact = false;
                    _new.Clear(); _new.AddRange(_cand);
                }
            }
            if (!found)
            {
                var first = _line[0]; Vector2 o = _aimSpot, d = _aimDir; float best = float.MaxValue; // (wmove: the line aimed ahead, as Line's)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var c = new Vector2Int(first.x + dx, first.y + dy);
                        if (c == r || gat.Check(c.x, c.y)) continue;
                        if (dx * d.x + dy * d.y < SlideCos * (dx != 0 && dy != 0 ? 1.41421356f : 1f)) continue; // not against the stick
                        float perp = Mathf.Abs((c.x - o.x) * d.y - (c.y - o.y) * d.x);
                        if (perp >= best) continue;
                        if (!PathCells(gat, r, c, _cand) || _cand.Count != 2 || _cand[0] != first) continue;
                        best = perp; found = true; target = c; cells = 2; prefix = 1; exact = false;
                        _new.Clear(); _new.AddRange(_cand);
                    }
            }
            if (!found && n >= 2)
            {
                for (int k = Mathf.Min(3, n); k >= 2 && !found; k--)
                {
                    if (!PathCells(gat, r, _line[k - 1], _cand) || _cand.Count < 2) continue;
                    found = true; target = _line[k - 1]; cells = _cand.Count; prefix = 1; exact = false;
                    _new.Clear(); _new.AddRange(_cand);
                }
            }
            if (!found)
            {
                if (!PathCells(gat, r, _line[0], _cand) || _cand.Count != 1) return;
                found = true; target = _line[0]; cells = 1; prefix = 1; exact = true;
                _new.Clear(); _new.AddRange(_cand);
            }
            if (walking)
            {
                // the same walk as the server already runs from that cell: nothing to ask
                routes.SrvCellsAfterRepath(_plan);
                bool same = _plan.Count == _new.Count;
                for (int i = 0; same && i < _new.Count; i++) same = _plan[i] == _new[i];
                if (same) { Skipped++; return; }
            }
            var srv = routes.SrvCell;
            if (srv == target) return;
            // the server checks the path from the cell the unit last REACHED (unit.cpp:855-869): never ask what it would drop
            if (!RoPathSearch.Search(Path, gat, srv.x, srv.y, target.x, target.y, 0) || Path.Len > 17) { NoWay++; return; }
            // clif.cpp:11402-11405: a request for the cell one to the west of the unit is dropped when someone stands on it
            if (target.x == srv.x - 1 && target.y == srv.y && occupied != null && occupied(target.x, target.y)) { NoWay++; return; }
            Note(now, routes, r, target, cells, prefix, exact, walking, correction, false);
            _reqFirst = _line[0]; _reqPath.Clear(); _reqPath.AddRange(_new);
            if (correction) Corrections++;
            if (LogLines) Debug.Log($"[W10] t={now:F3} steer {(correction ? "correction" : "request")} ({target.x},{target.y}) = {cells} step(s) from ({r.x},{r.y}) along {Mathf.Atan2(Dir.y, Dir.x) * Mathf.Rad2Deg:F1}°, {(exact ? "the line's own cells" : "the line's first " + prefix)}, server {(walking ? "walking" : "standing")} at ({srv.x},{srv.y})");
            send(target.x, target.y);
        }
    }
}
