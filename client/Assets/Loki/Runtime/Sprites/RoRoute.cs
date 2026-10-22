namespace Loki.Sprites
{
    /// <summary>
    /// Far walk targets (client wave 3b, w3walk): a route across the whole map, cut into legs that rAthena accepts.
    ///
    /// Why legs. A player's CZ_REQUEST_MOVE (0x0085 / 0x035f) reaches unit_walktoxy(sd, x, y, 4) (rA clif.cpp:11379-11426),
    /// which answers nothing at all when (rA unit.cpp:841-869):
    ///  - path_search from the unit's cell (bl->x, bl->y: the cell it last reached, unit.cpp:623-625) to (x, y) fails
    ///    (A*, CELL_CHKNOPASS, at most 32 steps and a 32 x 32 node table, path.cpp:269-432) — :855-856;
    ///  - the path is longer than battle_config.max_walk_path, 17 by default (conf/battle/client.conf:42, battle.cpp:8684) —
    ///    :860-862;
    ///  - OFFICIAL_WALKPATH (defined, config/core.hpp:26): the path is longer than 14 and the straight line to (x, y) is
    ///    blocked (path_search_long, path.cpp:132-193) — :864-869.
    /// The same 14-cell rule is checked again when the walk really starts (unit_walktoxy_sub, unit.cpp:267-272): at once for a
    /// standing unit, or — for a unit already walking, whose new target is only stored (unit.cpp:894-899) — at the end of the
    /// step in progress, from the cell that step reaches (unit.cpp:738-744); a failure there is answered with ZC_STOPMOVE
    /// (clif_fixpos, unit.cpp:742-743). The SpacetimeDB module has the same limits (server/src/conf.rs:6, mob/unit.rs:76-77).
    /// The official client sends one request per click (and every 500 ms while the button is held: roBrowserLegacy
    /// Engine/MapEngine.js:1075-1101, a reimplementation); on its screen a click is never far. In the VR diorama the whole map
    /// can be clicked, so Loki walks there in legs: <see cref="Planner.Find"/> plans the whole route on the client's GAT with
    /// the moves path_search allows, and <see cref="NextLeg"/> picks the farthest route cell that unit_walktoxy accepts.
    ///
    /// The route itself cannot come from path_search: it gives up past 32 steps and on node-table collisions (path.cpp:245-246,
    /// 409-416), so a far target never has an answer. <see cref="Planner"/> is a plain A* over the whole map with
    /// path_search's own moves and costs: the 8 neighbours, a diagonal only when both orthogonal cells are free
    /// (path.cpp:385-405), 10 per straight and 14 per diagonal step (path.hpp:11-12, which are also the walk times: a step
    /// takes speed ms, a diagonal speed*14/10, unit.cpp:226-229), the cell check CELL_CHKNOPASS (map.cpp:3323-3331, 3378-3383);
    /// its heuristic is the octile distance (admissible, so the route is the quickest walk). Each leg's own steps are the
    /// server's: path_search from the leg's start (<see cref="RoPathSearch"/>, identical to rAthena's, design notes §2).
    /// Pure C#, no Unity types: client/tools/walk/routecheck compiles this very file.
    /// </summary>
    public static class RoRoute
    {
        /// <summary>battle_config.max_walk_path's default (conf/battle/client.conf:42) and OFFICIAL_WALKPATH's 14 (unit.cpp:864-869).</summary>
        public const int MaxWalkPath = 17, OfficialWalkPath = 14;

        /// <summary>
        /// Whether unit_walktoxy (unit.cpp:855-869) accepts a player's walk request from (sx, sy) to (tx, ty): path_search
        /// (A*: CZ_REQUEST_MOVE walks with flag 4, so walk_easy = flag &amp; 1 = 0, clif.cpp:11425, unit.cpp:855, 882) finds a path of
        /// at most <paramref name="maxWalkPath"/> steps, and a path of more than 14 steps has a clear straight line. The path is
        /// left in <paramref name="wp"/>.
        /// </summary>
        public static bool Accepts(RoPathSearch.ICells map, int sx, int sy, int tx, int ty, RoPathSearch.WalkPath wp, int maxWalkPath = MaxWalkPath)
        {
            if (!RoPathSearch.Search(wp, map, sx, sy, tx, ty, 0)) return false;
            if (wp.Len > maxWalkPath) return false;
            if (wp.Len > OfficialWalkPath && !RoPathSearch.SearchLong(map, sx, sy, tx, ty)) return false;
            return true;
        }

        private static int Chebyshev(int ax, int ay, int bx, int by) => System.Math.Max(System.Math.Abs(ax - bx), System.Math.Abs(ay - by));

        /// <summary>
        /// The farthest route cell after <paramref name="from"/> (route index) that every one of the start cells accepts
        /// (<see cref="Accepts"/>): the server's unit may be on any of them when the request arrives (it is walking, or the
        /// client's clock is a few ms off). -1 when none does. A leg is at most <paramref name="maxWalkPath"/> steps, so route
        /// cells further than that from a start (Chebyshev: a path has at least that many steps) are skipped without a search.
        /// With <paramref name="avoided"/>, a leg whose server path (path_search's, from any start) steps on an avoided cell is
        /// not taken either.
        /// </summary>
        public static int NextLeg(RoPathSearch.ICells map, int[] routeX, int[] routeY, int routeLen, int from, int[] startX, int[] startY, int starts,
                                  RoPathSearch.WalkPath wp, int maxWalkPath = MaxWalkPath, System.Func<int, int, bool> avoided = null)
        {
            int last = System.Math.Min(routeLen - 1, from + 2 * RoPathSearch.MaxWalkPath);
            for (int k = last; k > from; k--)
            {
                bool ok = true;
                for (int s = 0; s < starts && ok; s++)
                    if (Chebyshev(startX[s], startY[s], routeX[k], routeY[k]) > maxWalkPath) ok = false;
                for (int s = 0; s < starts && ok; s++)
                {
                    if (!Accepts(map, startX[s], startY[s], routeX[k], routeY[k], wp, maxWalkPath)) { ok = false; break; }
                    if (avoided == null) continue;
                    int x = startX[s], y = startY[s];
                    for (int i = 0; i < wp.Len && ok; i++)
                    {
                        x += RoPathSearch.DirX[wp.Path[i]]; y += RoPathSearch.DirY[wp.Path[i]];
                        if (avoided(x, y)) ok = false;
                    }
                }
                if (ok) return k;
            }
            return -1;
        }

        /// <summary>
        /// The map as a trip sees it: the client's cells, plus cells it learned the server will not walk to (a leg to them was
        /// refused at every length, <see cref="Trip.OnRefused"/>) — both what the server's path search is expected to see — and,
        /// for planning only, cells to keep off (portals: <see cref="Trip.SetAvoid"/>), which the server walks through.
        /// Nothing is learned across trips.
        /// </summary>
        public sealed class View : RoPathSearch.ICells
        {
            private readonly RoPathSearch.ICells _map;
            private readonly System.Collections.Generic.HashSet<int> _blocked = new System.Collections.Generic.HashSet<int>();
            private readonly System.Collections.Generic.HashSet<int> _avoid = new System.Collections.Generic.HashSet<int>();
            public View(RoPathSearch.ICells map) { _map = map; ForPlanning = new Avoiding(this); }
            public int Xs => _map.Xs;
            public int Ys => _map.Ys;
            public bool Check(int x, int y) => _map.Check(x, y) || (_blocked.Count > 0 && x >= 0 && y >= 0 && x < Xs && y < Ys && _blocked.Contains(x + y * Xs));
            public void Block(int x, int y) { if (x >= 0 && y >= 0 && x < Xs && y < Ys) _blocked.Add(x + y * Xs); }
            public int BlockedCount => _blocked.Count;
            public bool IsAvoided(int x, int y) => _avoid.Count > 0 && x >= 0 && y >= 0 && x < Xs && y < Ys && _avoid.Contains(x + y * Xs);
            public int AvoidCount => _avoid.Count;
            /// <summary>Replaces the cells to keep off; true when the set changed.</summary>
            public bool SetAvoid(System.Collections.Generic.HashSet<int> cells)
            {
                if (_avoid.SetEquals(cells)) return false;
                _avoid.Clear(); _avoid.UnionWith(cells);
                return true;
            }
            /// <summary>The view with the avoided cells blocked too (what the route planner uses).</summary>
            public readonly RoPathSearch.ICells ForPlanning;
            private sealed class Avoiding : RoPathSearch.ICells
            {
                private readonly View _v;
                public Avoiding(View v) { _v = v; }
                public int Xs => _v.Xs;
                public int Ys => _v.Ys;
                public bool Check(int x, int y) => _v.Check(x, y) || _v.IsAvoided(x, y);
            }
        }

        /// <summary>
        /// One far walk: the route and where the walk is on it, and what to do when the server does not answer a leg. The game
        /// (Game/RouteWalker.cs: packets, clock, timing) and the offline simulator (client/tools/walk/routecheck: rAthena's
        /// rules on the server's own cells) drive the same object.
        /// A refused leg is asked again shorter — 17, 11, 6, then 2 steps (a shorter leg is accepted where the server's path
        /// is longer than the client's, the map cache and the GRF differing: design notes §1); when every length is
        /// refused the refused target cells are taken as blocked and the route is planned again around them, at most
        /// <see cref="MaxReplans"/> times. Nothing here reads a server setting: 17 and 14 are rAthena's defaults (see
        /// <see cref="MaxWalkPath"/>), and a server allowing more simply accepts every leg.
        /// </summary>
        public sealed class Trip
        {
            public const int MaxReplans = 3;
            /// <summary>The leg lengths tried after 0, 1, 2, 3 refusals in a row.</summary>
            public static readonly int[] Lengths = { MaxWalkPath, 11, 6, 2 };
            public readonly int GoalX, GoalY;
            public readonly View Map;
            private readonly Planner _planner;
            private readonly RoPathSearch.WalkPath _wp = new RoPathSearch.WalkPath();
            public int[] RouteX, RouteY;
            public int RouteLen;
            /// <summary>The route index the current leg goes to (the start, 0, before the first one is accepted).</summary>
            public int Index;
            public int Consecutive, Refused, Replanned, Accepted, Stops;
            public string FailReason;
            /// <summary>The route goes through avoided cells: there was no way round them (the legs are not checked against them).</summary>
            public bool ThroughAvoided;
            private readonly System.Collections.Generic.List<(int x, int y)> _streak = new System.Collections.Generic.List<(int, int)>();

            public Trip(RoPathSearch.ICells map, Planner planner, int goalX, int goalY) { Map = new View(map); _planner = planner; GoalX = goalX; GoalY = goalY; }

            /// <summary>The leg length allowed now (shorter after refusals).</summary>
            public int MaxLen => Lengths[System.Math.Min(Consecutive, Lengths.Length - 1)];

            /// <summary>Plans (again) from (x0, y0) to the goal, round the avoided cells when there is a way round them;
            /// false (with FailReason) when there is no way there at all.</summary>
            public bool Plan(int x0, int y0)
            {
                ThroughAvoided = false;
                RouteLen = Map.AvoidCount > 0 ? _planner.Find(Map.ForPlanning, x0, y0, GoalX, GoalY, ref RouteX, ref RouteY) : 0;
                if (RouteLen == 0)
                {
                    RouteLen = _planner.Find(Map, x0, y0, GoalX, GoalY, ref RouteX, ref RouteY);
                    ThroughAvoided = RouteLen > 0 && Map.AvoidCount > 0 && Crosses(0);
                }
                Index = 0;
                if (RouteLen == 0) FailReason = Map.BlockedCount > 0 ? "no other way there" : "no way there from here";
                return RouteLen > 0;
            }

            /// <summary>
            /// The cells to keep off: every cell within <paramref name="radius"/> of each centre (a portal: rAthena warps a player
            /// stepping on its touch area, x ± xs, y ± ys, npc_setcells npc.cpp:4972-5001, npc_touch_area_allnpc from each walk step,
            /// unit.cpp:680-688; the unit packets do not carry xs / ys, and 1,1 or 2,2 are 87 % of rAthena's warp lines, so radius
            /// 2 is the client's margin). A centre within <paramref name="radius"/> of the goal is not avoided (the player asked to
            /// go there), nor one within <paramref name="radius"/> of (<paramref name="hereX"/>, <paramref name="hereY"/>), where
            /// the unit stands unwarped (its real area is smaller: a map's arrival cell next to a portal). true when the set changed.
            /// </summary>
            public bool SetAvoid(System.Collections.Generic.IEnumerable<(int x, int y)> centres, int radius, int hereX = -1000, int hereY = -1000)
            {
                var set = new System.Collections.Generic.HashSet<int>();
                foreach (var c in centres)
                {
                    if (Chebyshev(c.x, c.y, GoalX, GoalY) <= radius || Chebyshev(c.x, c.y, hereX, hereY) <= radius) continue;
                    for (int y = c.y - radius; y <= c.y + radius; y++)
                        for (int x = c.x - radius; x <= c.x + radius; x++)
                            if (x >= 0 && y >= 0 && x < Map.Xs && y < Map.Ys) set.Add(x + y * Map.Xs);
                }
                return Map.SetAvoid(set);
            }

            /// <summary>Whether the route from index <paramref name="from"/> on steps on an avoided cell.</summary>
            public bool Crosses(int from)
            {
                for (int i = System.Math.Max(0, from); i < RouteLen; i++) if (Map.IsAvoided(RouteX[i], RouteY[i])) return true;
                return false;
            }

            public bool IsGoal(int x, int y) => x == GoalX && y == GoalY;

            /// <summary>The route index of the next leg, accepted from every start cell (the server's unit may be on any of them
            /// when the request lands); -1 when none is.</summary>
            public int NextLeg(int[] sx, int[] sy, int n) =>
                RoRoute.NextLeg(Map, RouteX, RouteY, RouteLen, Index, sx, sy, n, _wp, MaxLen, ThroughAvoided || Map.AvoidCount == 0 ? null : (System.Func<int, int, bool>)Map.IsAvoided);

            /// <summary>The server started the leg to route cell k (its ZC_NOTIFY_PLAYERMOVE came).</summary>
            public void OnAccepted(int k) { Index = k; Consecutive = 0; _streak.Clear(); Accepted++; }

            /// <summary>
            /// The server did not answer the leg to route cell k; the unit stands on (x, y). Shorter next time; after every length,
            /// the refused cells are avoided and the route planned again. false = give up (FailReason says why).
            /// </summary>
            public bool OnRefused(int k, int x, int y)
            {
                Refused++; Consecutive++;
                _streak.Add((RouteX[k], RouteY[k]));
                if (Consecutive < Lengths.Length) return Resume(x, y);
                if (Replanned >= MaxReplans) { FailReason = "the server refused every leg from here"; return false; }
                foreach (var c in _streak) if (!IsGoal(c.x, c.y)) Map.Block(c.x, c.y);
                bool goalRefused = _streak.Exists(c => IsGoal(c.x, c.y));
                _streak.Clear(); Consecutive = 0; Replanned++;
                if (goalRefused && Map.BlockedCount == 0) { FailReason = "the server refused every leg to that cell"; return false; }
                return Plan(x, y);
            }

            /// <summary>The unit stands (or will stand) on (x, y), not necessarily where the route expected it (ZC_STOPMOVE, a
            /// knockback, a walk the server made for its own reasons): go on along the route when (x, y) is on it near the
            /// current index, else plan again from there.</summary>
            public bool Resume(int x, int y)
            {
                if (Index < RouteLen && RouteX[Index] == x && RouteY[Index] == y) return true;
                int lo = System.Math.Max(0, Index - 2 * RoPathSearch.MaxWalkPath), hi = System.Math.Min(RouteLen - 1, Index + 2 * RoPathSearch.MaxWalkPath);
                for (int i = hi; i >= lo; i--) if (RouteX[i] == x && RouteY[i] == y) { Index = i; return true; }
                return Plan(x, y);
            }
        }

        /// <summary>
        /// A* over the whole map with path_search's moves (see the class comment). Keeps its arrays between searches (one map at
        /// a time; generation stamps instead of clearing), like path.cpp's static open set: not thread-safe.
        /// </summary>
        public sealed class Planner
        {
            private int _w, _h, _gen, _x0, _y0;
            private int[] _g, _parent, _stamp;
            private bool[] _closed;
            // binary heap of (f, h, cell); stale entries (a better g found later) are skipped when popped
            private int[] _hf = new int[1024], _hh = new int[1024], _hc = new int[1024];
            private int _hn;
            /// <summary>Cells taken off the open set by the last search (diagnostics).</summary>
            public int Expanded { get; private set; }

            private static int Octile(int x0, int y0, int x1, int y1)
            {
                int dx = System.Math.Abs(x1 - x0), dy = System.Math.Abs(y1 - y0);
                return RoPathSearch.MoveCost * System.Math.Max(dx, dy) + (RoPathSearch.MoveDiagonalCost - RoPathSearch.MoveCost) * System.Math.Min(dx, dy);
            }

            private bool Less(int a, int b) => _hf[a] < _hf[b] || (_hf[a] == _hf[b] && _hh[a] < _hh[b]);

            private void Swap(int a, int b)
            {
                int t = _hf[a]; _hf[a] = _hf[b]; _hf[b] = t;
                t = _hh[a]; _hh[a] = _hh[b]; _hh[b] = t;
                t = _hc[a]; _hc[a] = _hc[b]; _hc[b] = t;
            }

            private void Push(int f, int h, int c)
            {
                if (_hn == _hf.Length) { System.Array.Resize(ref _hf, _hn * 2); System.Array.Resize(ref _hh, _hn * 2); System.Array.Resize(ref _hc, _hn * 2); }
                int i = _hn++;
                _hf[i] = f; _hh[i] = h; _hc[i] = c;
                while (i > 0) { int p = (i - 1) / 2; if (!Less(i, p)) break; Swap(i, p); i = p; }
            }

            private int Pop()
            {
                int c = _hc[0];
                _hn--;
                if (_hn > 0)
                {
                    _hf[0] = _hf[_hn]; _hh[0] = _hh[_hn]; _hc[0] = _hc[_hn];
                    int i = 0;
                    for (;;)
                    {
                        int l = 2 * i + 1, r = l + 1, m = i;
                        if (l < _hn && Less(l, m)) m = l;
                        if (r < _hn && Less(r, m)) m = r;
                        if (m == i) break;
                        Swap(i, m); i = m;
                    }
                }
                return c;
            }

            private void Prepare(int w, int h)
            {
                if (_g == null || _w != w || _h != h)
                {
                    _w = w; _h = h; _gen = 0;
                    _g = new int[w * h]; _parent = new int[w * h]; _stamp = new int[w * h]; _closed = new bool[w * h];
                }
                if (++_gen == int.MaxValue) { System.Array.Clear(_stamp, 0, _stamp.Length); _gen = 1; }
                _hn = 0; Expanded = 0;
            }

            private bool Seen(int c) => _stamp[c] == _gen;

            private void Relax(RoPathSearch.ICells map, int x, int y, int g, int parent, int x1, int y1)
            {
                int c = x + y * _w;
                if (Seen(c) && (_closed[c] || _g[c] <= g)) return;
                if (!Seen(c)) { _stamp[c] = _gen; _closed[c] = false; }
                _g[c] = g; _parent[c] = parent;
                int hc = Octile(x, y, x1, y1);
                long cross = System.Math.Abs((long)(x - _x0) * (y1 - _y0) - (long)(y - _y0) * (x1 - _x0));
                int tie = (int)System.Math.Min(cross * 64 + hc, int.MaxValue / 2);
                Push(g + hc, tie, c);
            }

            /// <summary>
            /// The quickest walk from (x0, y0) to (x1, y1): its cells, start and destination included, into routeX / routeY
            /// (grown as needed); the cell count, or 0 when the destination is blocked or cannot be reached. The start cell is not
            /// checked (path.cpp:284-286). At most <paramref name="maxExpand"/> cells are expanded (0 = the whole map).
            /// </summary>
            public int Find(RoPathSearch.ICells map, int x0, int y0, int x1, int y1, ref int[] routeX, ref int[] routeY, int maxExpand = 0)
            {
                int w = map.Xs, h = map.Ys;
                if (x0 < 0 || y0 < 0 || x0 >= w || y0 >= h) return 0;
                if (x1 < 0 || y1 < 0 || x1 >= w || y1 >= h || map.Check(x1, y1)) return 0;
                Prepare(w, h);
                _x0 = x0; _y0 = y0;
                int start = x0 + y0 * w, goal = x1 + y1 * w;
                _stamp[start] = _gen; _closed[start] = false; _g[start] = 0; _parent[start] = -1;
                Push(Octile(x0, y0, x1, y1), Octile(x0, y0, x1, y1), start);
                bool found = false;
                if (maxExpand <= 0) maxExpand = w * h;
                const int C = RoPathSearch.MoveCost, D = RoPathSearch.MoveDiagonalCost;
                while (_hn > 0)
                {
                    int f = _hf[0]; int c = Pop();
                    if (_closed[c] || f != _g[c] + Octile(c % w, c / w, x1, y1)) continue; // a stale entry
                    _closed[c] = true;
                    if (c == goal) { found = true; break; }
                    if (++Expanded > maxExpand) break;
                    int x = c % w, y = c / w, g = _g[c];
                    // path.cpp:385-405: the orthogonal neighbours, then a diagonal only past two free orthogonal cells
                    bool n = !map.Check(x, y + 1), s = !map.Check(x, y - 1), e = !map.Check(x + 1, y), wv = !map.Check(x - 1, y);
                    if (s && e && !map.Check(x + 1, y - 1)) Relax(map, x + 1, y - 1, g + D, c, x1, y1);
                    if (e) Relax(map, x + 1, y, g + C, c, x1, y1);
                    if (n && e && !map.Check(x + 1, y + 1)) Relax(map, x + 1, y + 1, g + D, c, x1, y1);
                    if (n) Relax(map, x, y + 1, g + C, c, x1, y1);
                    if (n && wv && !map.Check(x - 1, y + 1)) Relax(map, x - 1, y + 1, g + D, c, x1, y1);
                    if (wv) Relax(map, x - 1, y, g + C, c, x1, y1);
                    if (s && wv && !map.Check(x - 1, y - 1)) Relax(map, x - 1, y - 1, g + D, c, x1, y1);
                    if (s) Relax(map, x, y - 1, g + C, c, x1, y1);
                }
                if (!found) return 0;
                int len = 0;
                for (int c = goal; c != -1; c = _parent[c]) len++;
                if (routeX == null || routeX.Length < len) { routeX = new int[len + 64]; routeY = new int[len + 64]; }
                int i = len - 1;
                for (int c = goal; c != -1; c = _parent[c], i--) { routeX[i] = c % w; routeY[i] = c / w; }
                return len;
            }
        }
    }
}
