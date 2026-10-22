namespace Loki.Sprites
{
    /// <summary>
    /// Client wave 5 (w5move): where a player's normal attack lands from, and where to walk to attack a target that is out of
    /// range or behind a wall — rAthena's own rules, so the attack chase (Game/AttackChase.cs) walks to a cell the server
    /// attacks from. Every reference is rAthena e985006 (C:\rAthena, PACKETVER 20211103).
    ///
    /// A player's attack request (CZ_REQUEST_ACT2 0x0437 action 7 → unit_attack, rA unit.cpp:2902-2990) runs
    /// unit_attack_timer_sub (unit.cpp:3187-3348) when the unit does not walk:
    ///  - OFFICIAL_WALKPATH (defined, config/core.hpp:20-26: "players will have to click to walk around it before attacking"):
    ///    when the straight line from the attacker's cell to the target's is blocked — path_search_long(..., CELL_CHKWALL),
    ///    unit.cpp:3221-3227 — the attack is dropped (unit_unattackable, unit.cpp:3361-3362, 2874-2891) and nothing at all is sent;
    ///  - range = the attacker's rhw.range, +1 when the target walks or the request came from a step (step_attack,
    ///    unit.cpp:3256-3258); a player out of range gets ZC_ATTACK_FAILURE_FOR_DISTANCE 0x0139 (unit.cpp:3260-3263,
    ///    clif_movetoattack clif.cpp:8172-8186: the target's cell, our cell, rhw.range). The range test is
    ///    check_distance_client_bl (path.hpp:48): distance_client(dx, dy) &lt;= range with distance_client =
    ///    (int)(sqrt(dx² + dy²) − 0.1) (path.cpp:485-521, "the client uses a circular distance");
    ///  - in range, battle_check_range (battle.cpp:8215-8240) checks the same circle and, from 2 cells on, the same line.
    /// CELL_CHKWALL is "neither walkable nor shootable" (map.cpp:3342-3343) over map_gat2cell (map.cpp:3280-3299): GAT type 1
    /// and unknown types; a gap (5) stops a walk but not an attack. Outside the map and on its last row / column it is false
    /// (map.cpp:3329-3331: only CELL_CHKNOPASS is true there).
    /// While the unit walks, an attack request is remembered and tried again half a step after each step (stepaction:
    /// unit.cpp:2955-2963, 722-735, unit_step_timer 439-496) until it is within range − 1, and once more when the walk ends.
    ///
    /// The chase cell: the route to the target (the whole-map planner of Sprites/RoRoute.cs) cut where the rest of it stays
    /// within range with a clear line — the cut roBrowserLegacy makes on its own path search (Utils/PathFinding.js:404-433,
    /// a reimplementation of the official client, used by EntityControl.onFocus :262-363 on a click and on 0x0139,
    /// Engine/MapEngine/Main.js:81-91) — with rAthena's circle and line instead of rB's.
    /// Pure C#, no Unity types: client/tools/move/chasecheck compiles this file and checks it against rAthena's path.o.
    /// </summary>
    public static class RoChase
    {
        /// <summary>distance_client (rA path.cpp:510-521).</summary>
        public static int DistanceClient(int dx, int dy)
        {
            double d = System.Math.Sqrt(dx * dx + dy * dy) - 0.1;
            if (d < 0) d = 0;
            return (int)d;
        }

        /// <summary>check_distance_client (rA path.cpp:485-490).</summary>
        public static bool CheckDistanceClient(int dx, int dy, int distance)
        {
            if (distance < 0) distance = 0;
            return DistanceClient(dx, dy) <= distance;
        }

        /// <summary>map_gat2cell (rA map.cpp:3280-3299): a GAT type that is neither walkable nor shootable (1, and every type
        /// map_gat2cell does not know).</summary>
        public static bool WallType(int type) => !(type == 0 || type == 2 || type == 3 || type == 4 || type == 5 || type == 6);

        /// <summary>The map as path_search_long sees it with CELL_CHKWALL (rA map.cpp:3329-3331, 3342-3343).</summary>
        public sealed class Walls : RoPathSearch.ICells
        {
            private readonly System.Func<int, int, int> _type;
            public int Xs { get; }
            public int Ys { get; }
            /// <param name="typeAt">The GAT type of a cell inside the map.</param>
            public Walls(int xs, int ys, System.Func<int, int, int> typeAt) { Xs = xs; Ys = ys; _type = typeAt; }
            public bool Check(int x, int y)
            {
                if (x < 0 || x >= Xs - 1 || y < 0 || y >= Ys - 1) return false; // map.cpp:3329-3331: true only for CELL_CHKNOPASS
                return WallType(_type(x, y));
            }
        }

        /// <summary>Whether a player's attack from (sx, sy) reaches a target on (tx, ty) with attack range
        /// <paramref name="range"/>: the line of unit_attack_timer_sub (unit.cpp:3221-3227) and the circle of its 0x0139 test
        /// (unit.cpp:3260-3263).</summary>
        public static bool CanAttackFrom(RoPathSearch.ICells walls, int sx, int sy, int tx, int ty, int range) =>
            CheckDistanceClient(sx - tx, sy - ty, range) && RoPathSearch.SearchLong(walls, sx, sy, tx, ty);

        /// <summary>Whether only the straight line stops an attack from (sx, sy): rAthena then drops the request without an
        /// answer (unit.cpp:3221-3227), so the client must walk round first.</summary>
        public static bool LineBlocked(RoPathSearch.ICells walls, int sx, int sy, int tx, int ty) => !RoPathSearch.SearchLong(walls, sx, sy, tx, ty);

        /// <summary>The earliest route index from which every later route cell can attack (tx, ty) (<see cref="CanAttackFrom"/>);
        /// -1 when the route's last cell cannot.</summary>
        public static int ChaseIndex(RoPathSearch.ICells walls, int[] rx, int[] ry, int len, int tx, int ty, int range)
        {
            int k = -1;
            for (int i = len - 1; i >= 0; i--)
            {
                if (!CanAttackFrom(walls, rx[i], ry[i], tx, ty, range)) break;
                k = i;
            }
            return k;
        }

        /// <summary>What <see cref="Plan"/> decided.</summary>
        public enum Kind
        {
            /// <summary>No route from the start to the target or next to it on the client's cells.</summary>
            NoWay,
            /// <summary>The start already attacks it: ask for the attack.</summary>
            InRange,
            /// <summary>One walk request reaches the chase cell (unit_walktoxy accepts it, RoRoute.Accepts): walk + attack.</summary>
            Direct,
            /// <summary>Farther than one request: walk in legs (Game/RouteWalker.cs), then attack.</summary>
            Legs,
        }

        public struct Result
        {
            public Kind Kind;
            /// <summary>The chase cell (the start for InRange).</summary>
            public int GoalX, GoalY;
            /// <summary>The route the cut was made on (cells, start included) and the cut's index in it.</summary>
            public int RouteLen, Index;
            /// <summary>The route ends next to the target (its own cell could not be reached).</summary>
            public bool Beside;
        }

        /// <summary>
        /// The chase from the server's cell (sx, sy) to a target on (tx, ty): the route on the client's cells
        /// (<paramref name="cells"/>, CELL_CHKNOPASS) to the target's cell — or, when that cell cannot be walked to, to the nearest
        /// walkable cell round it (up to 3 rings) — cut by <see cref="ChaseIndex"/>; the route's end when no cut exists.
        /// <paramref name="toTarget"/>: no cut, the route's end — the target's own cell (Game/AttackChase.cs's second try when the
        /// server's cells differ from the client's: walking at the target, rAthena attacks from the first cell within range − 1,
        /// stepaction, and from a cell next to it path_search_long checks no cell at all, path.cpp:167-190).
        /// </summary>
        public static Result Plan(RoPathSearch.ICells cells, RoPathSearch.ICells walls, RoRoute.Planner planner, ref int[] rx, ref int[] ry,
                                  RoPathSearch.WalkPath wp, int sx, int sy, int tx, int ty, int range, bool toTarget = false)
        {
            var r = new Result { GoalX = sx, GoalY = sy };
            if (!toTarget && CanAttackFrom(walls, sx, sy, tx, ty, range)) { r.Kind = Kind.InRange; return r; }
            int len = planner.Find(cells, sx, sy, tx, ty, ref rx, ref ry);
            if (len == 0)
            {
                // the target stands where nobody walks (or is boxed in): the nearest reachable cell round it, ring by ring,
                // the one nearest to the start first
                for (int ring = 1; ring <= 3 && len == 0; ring++)
                {
                    int bestD = int.MaxValue, bx = 0, by = 0;
                    for (int y = ty - ring; y <= ty + ring; y++)
                        for (int x = tx - ring; x <= tx + ring; x++)
                        {
                            if (System.Math.Max(System.Math.Abs(x - tx), System.Math.Abs(y - ty)) != ring || cells.Check(x, y)) continue;
                            int d = (x - sx) * (x - sx) + (y - sy) * (y - sy);
                            if (d < bestD) { bestD = d; bx = x; by = y; }
                        }
                    if (bestD == int.MaxValue) continue;
                    len = planner.Find(cells, sx, sy, bx, by, ref rx, ref ry);
                    r.Beside = len > 0;
                }
            }
            if (len == 0) { r.Kind = Kind.NoWay; return r; }
            int k = toTarget ? len - 1 : ChaseIndex(walls, rx, ry, len, tx, ty, range);
            if (k < 0) k = len - 1;
            r.RouteLen = len; r.Index = k; r.GoalX = rx[k]; r.GoalY = ry[k];
            if (k == 0) { r.Kind = Kind.InRange; return r; }
            r.Kind = RoRoute.Accepts(cells, sx, sy, r.GoalX, r.GoalY, wp) ? Kind.Direct : Kind.Legs;
            return r;
        }
    }
}
