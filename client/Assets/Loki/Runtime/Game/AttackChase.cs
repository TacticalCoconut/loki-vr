using Loki.Formats;
using Loki.Sprites;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 5 (w5move): the attack chase, routed in legs like every other far walk (design notes "Left": "Not
    /// routed: the attack chase after ZC_ATTACK_FAILURE_FOR_DISTANCE").
    ///
    /// What rAthena does with a player's attack (Sprites/RoChase.cs has the lines): out of range it answers
    /// ZC_ATTACK_FAILURE_FOR_DISTANCE 0x0139 and does nothing more — the client must walk; with the straight line to the target
    /// blocked it answers nothing at all (OFFICIAL_WALKPATH, rA config/core.hpp:20-26, unit.cpp:3221-3227). A walk request farther
    /// than 17 steps (or 14 with the line blocked) is refused without an answer (unit.cpp:855-869, Sprites/RoRoute.cs), so the old
    /// chase (a walk to the target's cell, then the attack again) did nothing for a monster across the map, and a click on a
    /// monster behind a wall did nothing either. roBrowserLegacy (a reimplementation of the official client) walks to a cell in
    /// range before it attacks, on a click and on 0x0139 (Controls/EntityControl.js:262-363 onFocus, Engine/MapEngine/Main.js:81-91,
    /// the attack sent when the walk ends, Engine/MapEngine.js:1169-1185).
    ///
    /// Here, on a 0x0139 for the player's target (LokiGame.W5Move.cs), and on a click whose line rAthena would refuse:
    ///  1. the chase cell (RoChase.Plan on the client's GAT): the route to the target cut where the rest of it attacks — rAthena's
    ///     circle and line (checked offline against the server's path.o: client/tools/move/run_chasecheck.py);
    ///  2. already there: the attack request at once; else a trip to the chase cell through Game/RouteWalker.cs (legs rAthena
    ///     accepts, refusals asked again shorter, portals kept off, a new click / the stick / a skill / a dialog cancel it);
    ///  3. the hand-over: as soon as the server walks the trip's last leg (or we already stand where the attack reaches, or the
    ///     trip arrived) the trip stops and the attack request goes out: while walking rAthena keeps it and attacks from the first
    ///     step within range − 1, or when the walk ends (stepaction, unit.cpp:2955-2963, 722-735, 439-496) — the same thing the
    ///     old one-request chase relied on, and nothing of RouteWalker is left to answer the server's stop (clif_fixpos before the
    ///     attack and the walk's stop, unit.cpp:3285-3286, 3294-3295) with another leg;
    ///  4. a watch: standing after an attack request (a hand-over, or a click's own), no hit of ours on the target and no new 0x0139 within 2 s means the server
    ///     dropped the attack — its map data differ from the GRF's on the line (prontera and 50 other maps with rAthena's own map
    ///     cache, design notes §1): one more chase, walking at the target's own cell (RoChase.Plan toTarget; the server
    ///     finds its own path there, attacks from the first cell within range − 1 on the way, and from any cell next to the target
    ///     path_search_long checks no cell at all, path.cpp:167-190), then the chase gives up (a Loki fallback; the official
    ///     client has none).
    /// The player's target changing (a click elsewhere, stop attacking, a map change) or the target leaving (died, logged out,
    /// teleported: ZC_NOTIFY_VANISH types 1-3) ends a chase. Everything comes from the RO packets and the client's GAT.
    /// </summary>
    public sealed class AttackChase : MonoBehaviour
    {
        public const string TripTag = "attack";
        /// <summary>Seconds to wait, standing after a hand-over, for our hit or a new 0x0139.</summary>
        public const float WatchSeconds = 0.85f;

        // ---- wiring (LokiGame.W5Move.cs)
        public System.Func<Unit> Self;
        public System.Func<GatFile> Gat;
        public System.Func<uint, Unit> Find;
        public RouteWalker Routes;
        /// <summary>CZ_REQUEST_ACT2 action 7 (continuous attack) on the target.</summary>
        public System.Action<uint> SendAttack;
        /// <summary>The player's attack target now (LokiGame's): a chase of any other id ends.</summary>
        public System.Func<uint> Target;
        /// <summary>Our attack range: ZC_ATTACK_RANGE 0x013a = rhw.range (rA clif.cpp:3777-3779; Game/Progression.cs), the value
        /// 0x0139 carries too.</summary>
        public System.Func<int> Range;
        /// <summary>A short message for the player.</summary>
        public System.Action<string> Tell;
        public static bool Log = true;

        private enum Phase { None, Trip, Handed }
        private Phase _phase;
        private uint _target; private int _tx, _ty, _range; private bool _closer; private int _goalX, _goalY;
        private int _quiet; private float _watchAt = -1f;
        private readonly RoRoute.Planner _planner = new RoRoute.Planner();
        private readonly RoPathSearch.WalkPath _wp = new RoPathSearch.WalkPath();
        private int[] _rx, _ry;
        private GatFile _wallsOf; private RoChase.Walls _walls;

        // ---- counters (smoke / diagnostics)
        public int TooFar, Chases, InRangeAtOnce, BlockedClicks, Trips, Handovers, LastLegHandovers, InRangeHandovers, ArrivedHandovers,
                   Closer, GaveUp, Gone, Ended, NoWay, Landed;
        public string LastEvent = "", LastHandover = "";
        public bool Active => _phase != Phase.None;
        public bool Walking => _phase == Phase.Trip;
        public uint ChaseTarget => _target;
        public Vector2Int Goal => new Vector2Int(_goalX, _goalY);

        private RoChase.Walls WallsOf(GatFile g)
        {
            if (_wallsOf != g) { _wallsOf = g; _walls = new RoChase.Walls(g.Width, g.Height, (x, y) => (int)g.Cells[x + y * g.Width].Type); }
            return _walls;
        }

        // ================================================================== the game's calls
        /// <summary>ZC_ATTACK_FAILURE_FOR_DISTANCE 0x0139 for the player's target: the target's cell, ours, our range
        /// (clif_movetoattack, rA clif.cpp:8172-8186). LokiGame throttles it.</summary>
        public void OnTooFar(uint target, int tx, int ty, int x, int y, int range)
        {
            TooFar++;
            if (_phase == Phase.Trip && target == _target)
            {
                if (Log) Debug.Log($"[W5Move] t={Time.time:F3} 0x0139 for {target} at ({tx},{ty}) while the chase walks to ({_goalX},{_goalY}): kept");
                return;
            }
            Begin(target, tx, ty, x, y, range, "0x0139 (too far)", false);
        }

        /// <summary>
        /// A click to attack <paramref name="u"/>. true when the chase takes it over: our character stands and the straight line to
        /// the target is blocked, so rAthena would drop the request without an answer (unit.cpp:3221-3227). Otherwise false: the
        /// game sends the attack request as before (in range: the attack; out of range: 0x0139 and <see cref="OnTooFar"/>; while
        /// walking, rAthena keeps it for every step, unit.cpp:2955-2963), and the watch of a hand-over runs (where the server's map
        /// data differ, it may drop a request the client's cells allow).
        /// </summary>
        public bool OnAttackClick(Unit u)
        {
            Stop(null);
            var self = Self?.Invoke(); var gat = Gat?.Invoke();
            if (u == null || self == null || gat == null) return false;
            var from = Routes != null ? Routes.SrvRepathCell : Vector2Int.RoundToInt(self.Cell);
            var to = Vector2Int.RoundToInt(u.Cell);
            int range = Range != null ? Range() : 1;
            if (self.Sliding || !RoChase.LineBlocked(WallsOf(gat), from.x, from.y, to.x, to.y))
            {
                _target = u.Id; _tx = to.x; _ty = to.y; _range = range; _closer = false; _goalX = from.x; _goalY = from.y;
                _phase = Phase.Handed; _watchAt = -1f; LastEvent = "attack request";
                return false;
            }
            BlockedClicks++;
            Begin(u.Id, to.x, to.y, from.x, from.y, range, "the straight line is blocked", false);
            return true;
        }

        /// <summary>A unit left (ZC_NOTIFY_VANISH: 0 out of sight, 1 died, 2 logged out, 3 teleported).</summary>
        public void OnVanished(uint id, int type)
        {
            if (_phase == Phase.None || id != _target || type == 0) return; // out of sight: the trip goes on to where it was
            Gone++;
            Stop($"the target is gone (vanish type {type})");
        }

        /// <summary>A hit (or miss) was shown: ours on the chased target ends the chase — the server attacks.</summary>
        public void OnDamage(Unit src, Unit dst)
        {
            if (_phase == Phase.None || src == null || dst == null || dst.Id != _target) return;
            var self = Self?.Invoke();
            if (self == null || src != self) return;
            Landed++;
            if (Log) Debug.Log($"[W5Move] t={Time.time:F3} our attack on {_target} landed: the chase is over");
            Reset("landed");
        }

        /// <summary>A RouteWalker trip ended (its Ended event).</summary>
        public void OnTripEnded(string tag, string outcome)
        {
            if (tag != TripTag || _quiet > 0 || _phase != Phase.Trip) return;
            if (outcome == "arrived") { ArrivedHandovers++; Hand("the trip arrived"); return; }
            if (Log) Debug.Log($"[W5Move] t={Time.time:F3} the chase walk to ({_goalX},{_goalY}) ended '{outcome}': no attack");
            Reset(outcome);
        }

        // ================================================================== the chase
        private void Begin(uint target, int tx, int ty, int sx, int sy, int range, string why, bool closer)
        {
            var gat = Gat?.Invoke();
            Stop(null);
            if (gat == null || Routes == null) { SendAttack?.Invoke(target); return; }
            Chases++;
            _target = target; _tx = tx; _ty = ty; _range = range; _closer = closer;
            var p = RoChase.Plan(gat, WallsOf(gat), _planner, ref _rx, ref _ry, _wp, sx, sy, tx, ty, range, closer);
            if (Log) Debug.Log($"[W5Move] t={Time.time:F3} chase {Chases} of {target} ({why}): from ({sx},{sy}) to the target at ({tx},{ty}), range {range}{(closer ? ", walking at the target itself" : "")}: {p.Kind}, chase cell ({p.GoalX},{p.GoalY}), route {p.RouteLen} cells cut at #{p.Index}{(p.Beside ? " (beside the target)" : "")}");
            switch (p.Kind)
            {
                case RoChase.Kind.NoWay:
                    NoWay++;
                    Tell?.Invoke(Lang.T("You can't reach that target from here."));
                    Reset("no way");
                    return;
                case RoChase.Kind.InRange:
                    InRangeAtOnce++;
                    _goalX = sx; _goalY = sy;
                    Hand("in range from where we stand");
                    return;
            }
            _goalX = p.GoalX; _goalY = p.GoalY;
            _quiet++;
            bool began = Routes.BeginTrip(_goalX, _goalY, TripTag, false);
            _quiet--;
            if (!began) { Reset("the walk could not begin"); return; } // RouteWalker said why
            Trips++;
            _phase = Phase.Trip;
            if (!Routes.Active)
            {
                // ended inside BeginTrip: it stood there already (arrived), or walking is not possible now (dead, a dialog …)
                if (Routes.LastOutcome == "arrived") { ArrivedHandovers++; Hand("the trip arrived at once"); }
                else { if (Log) Debug.Log($"[W5Move] t={Time.time:F3} the chase walk ended at once '{Routes.LastOutcome}': no attack"); Reset(Routes.LastOutcome); }
            }
        }

        /// <summary>Stops asking for legs and asks for the attack: rAthena attacks within range − 1 while walking, or at the
        /// walk's end (stepaction), or answers 0x0139 again.</summary>
        private void Hand(string how)
        {
            Handovers++; LastEvent = how; LastHandover = how;
            if (Routes != null && Routes.Active && Routes.Tag == TripTag) { _quiet++; Routes.Cancel("attack"); _quiet--; }
            _phase = Phase.Handed; _watchAt = -1f;
            if (Log) Debug.Log($"[W5Move] t={Time.time:F3} hand-over ({how}): attack request for {_target}");
            SendAttack?.Invoke(_target);
        }

        /// <summary>Ends a chase and its walk (null: quietly, before a new one).</summary>
        private void Stop(string why)
        {
            if (_phase == Phase.None) return;
            if (_phase == Phase.Trip && Routes != null && Routes.Active && Routes.Tag == TripTag) { _quiet++; Routes.Cancel(why ?? "a new chase"); _quiet--; }
            if (why != null) { Ended++; if (Log) Debug.Log($"[W5Move] t={Time.time:F3} chase of {_target} ended: {why}"); }
            Reset(why ?? "replaced");
        }

        private void Reset(string why)
        {
            _phase = Phase.None; _watchAt = -1f; LastEvent = why ?? LastEvent;
        }

        private void Update()
        {
            if (_phase == Phase.None) return;
            var self = Self?.Invoke(); var gat = Gat?.Invoke();
            if (self == null || gat == null) { Stop("left the map"); return; }
            if (Target != null && Target() != _target) { Stop("the player's target changed"); return; }
            string blocked = Routes != null ? Routes.Blocked?.Invoke() : null; // dead, a dialog, trading … (LokiGame.W3WalkBlocked)
            if (blocked != null) { Stop(blocked); return; }
            var u = Find?.Invoke(_target);
            int prevTx = _tx, prevTy = _ty;
            if (u != null) { var c = Vector2Int.RoundToInt(u.Cell); _tx = c.x; _ty = c.y; }
            var me = Vector2Int.RoundToInt(self.Cell);
            if (_phase == Phase.Trip)
            {
                if (Routes == null || !Routes.Active || Routes.Tag != TripTag) return; // OnTripEnded decides
                // (the second try walks at the target itself: the server attacks on the way, so no hand-over before its last leg)
                if (!_closer && u != null && RoChase.CanAttackFrom(WallsOf(gat), me.x, me.y, _tx, _ty, _range)) { InRangeHandovers++; Hand("in range on the way"); return; }
                if (u != null && (_tx != prevTx || _ty != prevTy) && !RoChase.CanAttackFrom(WallsOf(gat), _goalX, _goalY, _tx, _ty, _range))
                {
                    var from = Routes != null ? Routes.SrvRepathCell : me;
                    Begin(_target, _tx, _ty, from.x, from.y, _range, "target moved", _closer);
                    return;
                }
                if (Routes.LastLegRunning) { LastLegHandovers++; Hand("the server walks the last leg"); }
                return;
            }
            // handed over: the server attacks, or tells us again how far it is (0x0139: OnTooFar); a silence means it dropped it
            if (self.Walking || self.Sliding) { _watchAt = -1f; return; }
            if (_watchAt < 0f) { _watchAt = Time.time + WatchSeconds; return; }
            if (Time.time < _watchAt) return;
            if (!_closer)
            {
                Closer++;
                if (Log) Debug.Log($"[W5Move] t={Time.time:F3} no hit and no answer {WatchSeconds} s after the hand-over at ({me.x},{me.y}): the server dropped the attack; chasing again, at the target itself");
                Begin(_target, _tx, _ty, me.x, me.y, _range, "the server dropped the attack", true);
                return;
            }
            GaveUp++;
            if (Log) Debug.Log($"[W5Move] t={Time.time:F3} no hit next to the target either: the chase gives up");
            Tell?.Invoke(Lang.T("You can't attack that target from here."));
            Reset("gave up");
        }
    }
}
