namespace Loki.Sprites
{
    /// <summary>
    /// rAthena's walk path search, ported line for line from src/map/path.cpp (e985006) so that a unit drawn by the client
    /// walks the cells the server walks it through (client wave 3, w3move; re-audit 2026-09-29 §2b). Pure C#: no Unity types,
    /// so the offline check (client/tools/move/pathcheck) compiles this very file against a harness linked with the oracle's
    /// own compiled path.o.
    /// What is ported, with the rAthena lines:
    ///  - path_search (path.cpp:269-432): the easy path (flag &amp; 1: diagonal first, then straight, fails on the first
    ///    blocked cell, :292-325) and A* (:326-429) with costs 10 / 14 (path.hpp:11-12), the inadmissible Manhattan x 10
    ///    heuristic (:55), neighbours in the order SE, E, NE, N, NW, W, SW, S (:392-407), diagonals only when both
    ///    orthogonal cells are free (:385-405), the 32 x 32 node table indexed (x + y*32) &amp; 1023 whose collisions fail the
    ///    whole search (:51, :245-246, :409-411), a path of more than 32 steps failing (:414-416);
    ///  - the binary heap exactly as common/db.hpp's BHEAP_PUSH2 / BHEAP_POP2 / BHEAP_UPDATE / SIFTDOWN / SIFTUP
    ///    (db.hpp:1508-1513, 1535-1541, 1600-1653), "matching client bheap": its tie order decides which of two equal-cost
    ///    paths is taken;
    ///  - path_search_long (path.cpp:132-193), used by path_search's flag &amp; 2;
    ///  - the cell rule the walk uses, map_getcellp(..., CELL_CHKNOPASS) (map.cpp:3323-3331, 3378-3383) over map_gat2cell
    ///    (map.cpp:3280-3299): GAT types 0, 2, 3, 4, 6 walkable, 1 and 5 (and anything else) not, and the last column and
    ///    row of a map always blocked (x &gt;= xs-1 or y &gt;= ys-1). CELL_NOSTACK is off (config/core.hpp:31).
    /// Not thread-safe, like the original (one static open set, path.cpp:44, 267).
    /// </summary>
    public static class RoPathSearch
    {
        public const int MoveCost = 10, MoveDiagonalCost = 14, MaxWalkPath = 32; // path.hpp:11-14

        // enum directions (path.hpp:16-27): 0 north (+y), counter-clockwise; the server's direction numbering
        public const sbyte DirCenter = -1, DirNorth = 0, DirNorthWest = 1, DirWest = 2, DirSouthWest = 3, DirSouth = 4,
            DirSouthEast = 5, DirEast = 6, DirNorthEast = 7;

        /// <summary>unit.cpp:70-71: the cell step of each direction.</summary>
        public static readonly int[] DirX = { 0, -1, -1, -1, 0, 1, 1, 1 };
        public static readonly int[] DirY = { 1, 1, 0, -1, -1, -1, 0, 1 };

        /// <summary>path.cpp:59-64 walk_choices[-dy + 1][dx + 1].</summary>
        private static readonly sbyte[,] WalkChoices =
        {
            { DirNorthWest, DirNorth, DirNorthEast },
            { DirWest, DirCenter, DirEast },
            { DirSouthWest, DirSouth, DirSouthEast },
        };

        /// <summary>direction_diagonal (path.cpp:523-525).</summary>
        public static bool Diagonal(int dir) => dir == DirNorthWest || dir == DirSouthWest || dir == DirSouthEast || dir == DirNorthEast;

        /// <summary>The map as path_search sees it: its size and map_getcellp(m, x, y, cell) for the cell check in use.</summary>
        public interface ICells
        {
            int Xs { get; }
            int Ys { get; }
            /// <summary>map_getcellp(mapdata, x, y, cell): true when the check matches (for CELL_CHKNOPASS: blocked).</summary>
            bool Check(int x, int y);
        }

        /// <summary>map_gat2cell (map.cpp:3280-3299): the walkable bit of a GAT cell type.</summary>
        public static bool GatWalkable(int type) => type == 0 || type == 2 || type == 3 || type == 4 || type == 6;

        /// <summary>map_getcellp(m, x, y, CELL_CHKNOPASS) (map.cpp:3329-3331 + 3378-3383): outside the map and the last
        /// column / row are blocked ("this intentionally overrides the last row and column"), else !walkable.</summary>
        public static bool NoPass(int xs, int ys, int x, int y, bool walkable)
        {
            if (x < 0 || x >= xs - 1 || y < 0 || y >= ys - 1) return true;
            return !walkable;
        }

        /// <summary>struct walkpath_data (path.hpp:29-32): the steps as server directions.</summary>
        public sealed class WalkPath
        {
            public int Len, Pos;
            public readonly sbyte[] Path = new sbyte[MaxWalkPath];
        }

        // ------------------------------------------------------------------ A* state (path.cpp:21-45)
        private const short SetOpen = 0, SetClosed = 1;
        private const int PathDirNorth = 1, PathDirWest = 2, PathDirSouth = 4, PathDirEast = 8; // path.cpp:24-27

        /// <summary>struct path_node (path.cpp:33-40); parent is an index into the node table, -1 for nullptr.</summary>
        private struct PathNode
        {
            public int Parent;
            public short X, Y, GCost, FCost, Flag;
        }

        private static readonly PathNode[] Tp = new PathNode[MaxWalkPath * MaxWalkPath];
        private static int[] _heap = new int[256];
        private static int _heapLen;

        private static int CalcIndex(int x, int y) => (x + y * MaxWalkPath) & (MaxWalkPath * MaxWalkPath - 1); // path.cpp:51
        private static int Heuristic(int x0, int y0, int x1, int y1) => MoveCost * (System.Math.Abs(x1 - x0) + System.Math.Abs(y1 - y0)); // path.cpp:55
        private static int Cmp(int i, int j) => Tp[i].FCost - Tp[j].FCost; // NODE_MINTOPCMP (path.cpp:49)

        private static void Swap(int a, int b) { int t = _heap[a]; _heap[a] = _heap[b]; _heap[b] = t; }

        /// <summary>BHEAP_SIFTDOWN (db.hpp:1600-1612): towards the root, stop at startIdx or when the parent is not larger.</summary>
        private static void SiftDown(int startIdx, int idx)
        {
            int i2 = idx;
            while (i2 > startIdx)
            {
                int parent = (i2 - 1) / 2;
                if (Cmp(_heap[parent], _heap[i2]) <= 0) break;
                Swap(parent, i2);
                i2 = parent;
            }
        }

        /// <summary>BHEAP_SIFTUP (db.hpp:1620-1641): swap with the smaller child down to a leaf (the right child on a tie),
        /// then back up with SIFTDOWN from idx.</summary>
        private static void SiftUp(int idx)
        {
            int i = idx;
            int lchild = i * 2 + 1;
            while (lchild < _heapLen)
            {
                int rchild = i * 2 + 2;
                if (rchild >= _heapLen || Cmp(_heap[lchild], _heap[rchild]) < 0) { Swap(i, lchild); i = lchild; }
                else { Swap(i, rchild); i = rchild; }
                lchild = i * 2 + 1;
            }
            SiftDown(idx, i);
        }

        /// <summary>heap_push_node (path.cpp:201-207): BHEAP_ENSURE2 + BHEAP_PUSH2 (db.hpp:1508-1513).</summary>
        private static void HeapPush(int node)
        {
            if (_heapLen == _heap.Length) System.Array.Resize(ref _heap, _heap.Length + 256);
            int i = _heapLen;
            _heap[_heapLen++] = node;
            SiftDown(0, i);
        }

        /// <summary>BHEAP_POP2 (db.hpp:1535-1541): the last element to the root, then SIFTUP.</summary>
        private static void HeapPop()
        {
            _heap[0] = _heap[--_heapLen];
            if (_heapLen == 0) return;
            SiftUp(0);
        }

        /// <summary>heap_update_node (path.cpp:210-220): ARR_FIND, then BHEAP_UPDATE (db.hpp:1649-1653) = SIFTDOWN from the
        /// root side, then SIFTUP at the same index (whatever element sits there by then). 1 = not found.</summary>
        private static int HeapUpdate(int node)
        {
            int i;
            for (i = 0; i < _heapLen; ++i) if (_heap[i] == node) break;
            if (i == _heapLen) return 1; // ShowError("heap_update_node: node not found")
            SiftDown(0, i);
            SiftUp(i);
            return 0;
        }

        /// <summary>add_path (path.cpp:224-257). 1 = error (the index is taken by another cell, or the update failed).</summary>
        private static int AddPath(int x, int y, int gCost, int parent, int hCost)
        {
            int i = CalcIndex(x, y);
            if (Tp[i].X == x && Tp[i].Y == y)
            {
                // We processed this node before
                if (gCost < Tp[i].GCost)
                {
                    Tp[i].GCost = (short)gCost;
                    Tp[i].Parent = parent;
                    Tp[i].FCost = (short)(gCost + hCost);
                    if (Tp[i].Flag == SetClosed) HeapPush(i); // Put it in open set again
                    else if (HeapUpdate(i) != 0) return 1;
                    Tp[i].Flag = SetOpen;
                }
                return 0;
            }
            if (Tp[i].X != 0 || Tp[i].Y != 0) return 1; // Index is already taken (the tp array FIXME)
            Tp[i].X = (short)x;
            Tp[i].Y = (short)y;
            Tp[i].GCost = (short)gCost;
            Tp[i].Parent = parent;
            Tp[i].FCost = (short)(gCost + hCost);
            Tp[i].Flag = SetOpen;
            HeapPush(i);
            return 0;
        }

        /// <summary>
        /// path_search (path.cpp:269-432): (x0,y0) → (x1,y1). flag &amp; 1: easy path only; flag &amp; 2: path_search_long
        /// instead (no path written). The start cell itself is never checked (:284-286).
        /// </summary>
        public static bool Search(WalkPath wpd, ICells map, int x0, int y0, int x1, int y1, int flag)
        {
            int i, x, y, dx = 0, dy = 0;
            if ((flag & 2) != 0) return SearchLong(map, x0, y0, x1, y1);
            if (wpd == null) wpd = new WalkPath();
            int mxs = map.Xs, mys = map.Ys;
            if (x0 < 0 || x0 >= mxs || y0 < 0 || y0 >= mys) return false;
            // Check destination cell
            if (x1 < 0 || x1 >= mxs || y1 < 0 || y1 >= mys || map.Check(x1, y1)) return false;

            if ((flag & 1) != 0)
            {
                // Try finding direct path to target: diagonally first, then in a straight line
                dx = (dx = x1 - x0) != 0 ? (dx < 0 ? -1 : 1) : 0;
                dy = (dy = y1 - y0) != 0 ? (dy < 0 ? -1 : 1) : 0;
                x = x0; y = y0; i = 0;
                while (i < MaxWalkPath)
                {
                    wpd.Path[i] = WalkChoices[-dy + 1, dx + 1];
                    i++;
                    x += dx; y += dy;
                    if (x == x1) dx = 0; // destination x reached, no longer move along x-axis
                    if (y == y1) dy = 0;
                    if (dx == 0 && dy == 0) break; // success
                    if (map.Check(x, y)) break;    // obstacle = failure
                }
                if (x == x1 && y == y1)
                {
                    wpd.Len = i; wpd.Pos = 0;
                    return true;
                }
                return false; // easy path unsuccessful
            }

            // A* (path.cpp:326-429)
            int xs = mxs - 1, ys = mys - 1, len = 0, j, current;
            _heapLen = 0; // BHEAP_RESET
            for (int k = 0; k < Tp.Length; k++) Tp[k] = new PathNode { Parent = -1 }; // memset(tp, 0, sizeof(tp))

            i = CalcIndex(x0, y0);
            Tp[i].Parent = -1;
            Tp[i].X = (short)x0;
            Tp[i].Y = (short)y0;
            Tp[i].GCost = 0;
            Tp[i].FCost = (short)Heuristic(x0, y0, x1, y1);
            Tp[i].Flag = SetOpen;
            HeapPush(i);

            for (;;)
            {
                int e = 0;
                int allowedDirs = 0;
                if (_heapLen == 0) return false;
                current = _heap[0];   // BHEAP_PEEK
                HeapPop();            // BHEAP_POP2
                x = Tp[current].X;
                y = Tp[current].Y;
                int gCost = Tp[current].GCost;
                Tp[current].Flag = SetClosed;
                if (x == x1 && y == y1) break;

                if (y < ys && !map.Check(x, y + 1)) allowedDirs |= PathDirNorth;
                if (y > 0 && !map.Check(x, y - 1)) allowedDirs |= PathDirSouth;
                if (x < xs && !map.Check(x + 1, y)) allowedDirs |= PathDirEast;
                if (x > 0 && !map.Check(x - 1, y)) allowedDirs |= PathDirWest;

                if (Chk(allowedDirs, PathDirSouth | PathDirEast) && !map.Check(x + 1, y - 1))
                    e += AddPath(x + 1, y - 1, gCost + MoveDiagonalCost, current, Heuristic(x + 1, y - 1, x1, y1)); // 5
                if (Chk(allowedDirs, PathDirEast))
                    e += AddPath(x + 1, y, gCost + MoveCost, current, Heuristic(x + 1, y, x1, y1)); // 6
                if (Chk(allowedDirs, PathDirNorth | PathDirEast) && !map.Check(x + 1, y + 1))
                    e += AddPath(x + 1, y + 1, gCost + MoveDiagonalCost, current, Heuristic(x + 1, y + 1, x1, y1)); // 7
                if (Chk(allowedDirs, PathDirNorth))
                    e += AddPath(x, y + 1, gCost + MoveCost, current, Heuristic(x, y + 1, x1, y1)); // 0
                if (Chk(allowedDirs, PathDirNorth | PathDirWest) && !map.Check(x - 1, y + 1))
                    e += AddPath(x - 1, y + 1, gCost + MoveDiagonalCost, current, Heuristic(x - 1, y + 1, x1, y1)); // 1
                if (Chk(allowedDirs, PathDirWest))
                    e += AddPath(x - 1, y, gCost + MoveCost, current, Heuristic(x - 1, y, x1, y1)); // 2
                if (Chk(allowedDirs, PathDirSouth | PathDirWest) && !map.Check(x - 1, y - 1))
                    e += AddPath(x - 1, y - 1, gCost + MoveDiagonalCost, current, Heuristic(x - 1, y - 1, x1, y1)); // 3
                if (Chk(allowedDirs, PathDirSouth))
                    e += AddPath(x, y - 1, gCost + MoveCost, current, Heuristic(x, y - 1, x1, y1)); // 4
                if (e != 0) return false;
            }

            for (int it = current; Tp[it].Parent != -1; it = Tp[it].Parent, len++) { }
            if (len > MaxWalkPath) return false; // sizeof(wpd->path): 32 one-byte directions

            wpd.Len = len;
            wpd.Pos = 0;
            j = len - 1;
            for (int it = current; j >= 0; it = Tp[it].Parent, j--)
            {
                int p = Tp[it].Parent;
                dx = Tp[it].X - Tp[p].X;
                dy = Tp[it].Y - Tp[p].Y;
                wpd.Path[j] = WalkChoices[-dy + 1, dx + 1];
            }
            return true;
        }

        private static bool Chk(int allowed, int d) => (allowed & d) == d; // chk_dir (path.cpp:390)

        /// <summary>path_search_long (path.cpp:132-193): is the straight line from (x0,y0) to (x1,y1) free of the cell check?
        /// The shoot-path cells are not kept (the client has no use for them).</summary>
        public static bool SearchLong(ICells map, int x0, int y0, int x1, int y1)
        {
            int dx, dy, wx = 0, wy = 0, weight;
            dx = x1 - x0;
            if (dx < 0)
            {
                int t = x0; x0 = x1; x1 = t;
                t = y0; y0 = y1; y1 = t;
                dx = -dx;
            }
            dy = y1 - y0;
            if (dx > System.Math.Abs(dy)) weight = dx;
            else weight = System.Math.Abs(y1 - y0);
            while (x0 != x1 || y0 != y1)
            {
                wx += dx;
                wy += dy;
                if (wx >= weight) { wx -= weight; x0++; }
                if (wy >= weight) { wy -= weight; y0++; }
                else if (wy < 0) { wy += weight; y0--; }
                if ((x0 != x1 || y0 != y1) && map.Check(x0, y0)) return false;
            }
            return true;
        }
    }
}
