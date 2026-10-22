using System.Collections.Generic;
using System.Globalization;
using Loki.Formats;
using UnityEngine;

namespace Loki.World
{
    /// <summary>
    /// Indoor maps in RO pack many unrelated rooms into one map and teleport you between them. This shows only the room you are in:
    /// a per-cell mask (one texel per GAT cell holding the cell's room) that every Loki shader tests (LokiRoomMask.cginc). Outdoor
    /// maps are unaffected.
    ///
    /// Client wave 20 (w20rooms), the design's point 54 ("You should only be able to see the room that you are in at that moment"): the
    /// rooms and their exact extents come from the ROOM TABLE (data\rooms.tsv in the per-user data folder, made by the first-run
    /// builder from the player's own game files: Data/RoomTools.cs, which also decides which maps are indoor). On a map with two
    /// rooms or more the mask holds every cell's room id and only the current room's cells are drawn; when the character comes
    /// into another room (a warp on the same map, or any other move: <see cref="Track"/>) the old room dissolves away while the
    /// new one dissolves in over <see cref="FadeSeconds"/> (no pop). A map with one room is drawn whole.
    /// Without a room table (the builder could not make one), the behaviour before this wave: the client's indoor list
    /// (data\indoorrswtable.txt) and a live fill of the walkable cells 4-connected to the player's, dilated by <see cref="WallCells"/>.
    /// </summary>
    [DefaultExecutionOrder(1000)] // after the view controller has moved the map this frame
    public sealed class RoomMask : MonoBehaviour
    {
        /// <summary>The fallback's wall margin (cells), without a room table.</summary>
        public int WallCells = 4;
        /// <summary>Client wave 20 (w20rooms): the cross-fade between the old room and the new one (seconds; FLAGGED, default one).</summary>
        public const float FadeSeconds = 0.4f;
        public bool Active { get; private set; }
        /// <summary>The room drawn (its id in the room table; 1 in the fallback), the room fading out (= the drawn one when no fade
        /// runs), and the fade's progress (0 → 1, 1 = done).</summary>
        public int CurrentRoom { get; private set; }
        public int PreviousRoom { get; private set; }
        public float Fade { get; private set; } = 1f;
        /// <summary>Where the current map's rooms come from: "room table", "client list" (the fallback) or "" (not indoor).</summary>
        public string Source { get; private set; } = "";
        /// <summary>The map's rooms (room table), 1 in the fallback, 0 when not indoor.</summary>
        public int RoomCount { get; private set; }
        /// <summary>Milliseconds the last <see cref="Rebuild"/> took to make the mask (smokes, the log).</summary>
        public float LastBuildMs { get; private set; }
        /// <summary>How many times the drawn room changed on the same map (warp or walk), and when the last change began (unscaled time).</summary>
        public int Switches { get; private set; }
        public float SwitchedAt { get; private set; } = -1f;
        private Texture2D _tex; private byte[] _mask; private GatFile _gat; private Transform _mapRoot; private string _map;
        private HashSet<string> _indoor;
        private Dictionary<string, Entry> _table; private bool _tableRead;
        private static readonly int MaskId = Shader.PropertyToID("_LokiRoomMask"), MatId = Shader.PropertyToID("_LokiMapWorldToLocal"), ParamsId = Shader.PropertyToID("_LokiRoomMaskParams"),
            FadeId = Shader.PropertyToID("_LokiRoomFade");

        /// <summary>One map of the room table: its size, rooms ("R" rows: id, cells, x0, y0, x1, y1, models, extent) and its cells' runs.</summary>
        public sealed class Entry
        {
            public string Map, Rule, Runs; public int Width, Height, Rooms, Models, Assigned, Unassigned;
            public readonly List<int[]> RoomRows = new List<int[]>();
        }

        public void Init(Loki.IO.RoFileSystem fs)
        {
            _indoor = Loki.Data.RoomTools.ParseIndoorList(fs?.Read(Loki.Data.RoomTools.IndoorList));
            Debug.Log($"[RoomMask] {_indoor.Count} indoor maps listed by the client");
            Shader.SetGlobalVector(FadeId, new Vector4(1f, 1f, 1f, 0f));
            Disable();
        }

        /// <summary>The room table, read once (after the first-run builder: the first map's entry asks).</summary>
        private void ReadTable()
        {
            if (_tableRead) return;
            _tableRead = true;
            string path = Loki.Data.ClientData.File(Loki.Data.RoomTools.Table);
            if (!System.IO.File.Exists(path)) { Debug.LogWarning("[RoomMask] no room table: indoor maps by the client's list, each room by a live fill around the character"); return; }
            var t = new Dictionary<string, Entry>(System.StringComparer.Ordinal); int bad = 0, multi = 0;
            try
            {
                foreach (var raw in System.IO.File.ReadAllLines(path, System.Text.Encoding.UTF8))
                {
                    if (raw.Length == 0 || raw[0] == '#') continue;
                    var c = raw.Split('\t');
                    if (c[0] == "I" && c.Length >= 10)
                    {
                        var e = new Entry { Map = c[1], Rule = c[5] };
                        if (!Int(c[2], out e.Width) || !Int(c[3], out e.Height) || !Int(c[4], out e.Rooms) || !Int(c[6], out e.Models) || !Int(c[7], out e.Assigned) || !Int(c[8], out e.Unassigned)) { bad++; continue; }
                        t[e.Map] = e; if (e.Rooms >= 2) multi++;
                    }
                    else if (c[0] == "R" && c.Length >= 10 && t.TryGetValue(c[1], out var er))
                    {
                        var row = new int[8]; bool ok = true;
                        for (int k = 0; k < 8; k++) ok &= Int(c[2 + k], out row[k]);
                        if (ok) er.RoomRows.Add(row); else bad++;
                    }
                    else if (c[0] == "G" && c.Length >= 3 && t.TryGetValue(c[1], out var eg)) eg.Runs = c[2];
                    else bad++;
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[RoomMask] the room table cannot be read (" + e.Message + "): indoor maps by the client's list"); return; }
            _table = t;
            Debug.Log($"[RoomMask] room table {path}: {t.Count} indoor maps, {multi} with two rooms or more{(bad > 0 ? $", {bad} rows skipped" : "")}");
        }

        private static bool Int(string s, out int v) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

        /// <summary>The room table's entry of a map (null: not indoor, or no table).</summary>
        public Entry TableEntry(string map) { ReadTable(); return _table != null && map != null && _table.TryGetValue(map.ToLowerInvariant(), out var e) ? e : null; }
        public bool HasTable { get { ReadTable(); return _table != null; } }

        public bool IsIndoor(string map)
        {
            if (map == null) return false;
            ReadTable();
            return _table != null ? _table.ContainsKey(map.ToLowerInvariant()) : _indoor != null && _indoor.Contains(map.ToLowerInvariant());
        }

        public void Disable()
        {
            Active = false; _gat = null; RoomCount = 0; Source = "";
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
        }

        /// <summary>Called on map entry, when the player lands somewhere new on the same map, and after a Horde run.</summary>
        public void Rebuild(string map, GatFile gat, Transform mapRoot, Vector2Int self)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool sameMap = Active && _map == map && _gat == gat && _mask != null; // the same loaded map: a warp inside it fades
            int oldRoom = CurrentRoom;
            _map = map;
            if (gat == null || !IsIndoor(map)) { Disable(); return; }
            int w = gat.Width, h = gat.Height;
            var e = TableEntry(map);
            if (e != null)
            {
                if (e.Rooms < 2 || e.Runs == null) { Disable(); RoomCount = e.Rooms; Source = "room table"; Debug.Log($"[RoomMask] {map}: one room in the room table: drawn whole"); return; }
                if (e.Width != w || e.Height != h) { Disable(); Debug.LogWarning($"[RoomMask] {map}: the room table's {e.Width}x{e.Height} cells differ from the map's {w}x{h}: drawn whole"); return; }
                if (!sameMap)
                {
                    var labels = Loki.Data.RoomTools.ParseRuns(e.Runs, w * h);
                    if (labels == null) { Disable(); Debug.LogWarning($"[RoomMask] {map}: the room table's cells do not parse: drawn whole"); return; }
                    _mask = labels; Upload(w, h);
                }
                _gat = gat; _mapRoot = mapRoot; RoomCount = e.Rooms; Source = "room table";
                int room = RoomAt(self);
                if (room == 0) { Disable(); Debug.LogWarning($"[RoomMask] {map}: no room at {self}: drawn whole"); return; }
                SetRoom(room, sameMap && oldRoom != 0 && oldRoom != room);
                Active = true;
                Shader.SetGlobalVector(ParamsId, new Vector4(1, w, h, 0));
                LastBuildMs = (float)sw.Elapsed.TotalMilliseconds;
                var rr = e.RoomRows.Find(x => x[0] == room);
                Debug.Log($"[RoomMask] {map}: room {room} of {e.Rooms} ({(rr != null ? rr[1] : 0)} walkable cells, {(rr != null ? rr[6] : 0)} models, {(rr != null ? rr[7] : 0)} cells drawn) around {self}" +
                          $"{(sameMap ? (oldRoom != room ? $", from room {oldRoom} (a {FadeSeconds:0.0} s fade)" : ", the same room") : "")}; mask {LastBuildMs:0.0} ms");
                return;
            }
            // ---- the fallback (no room table): the walkable cells 4-connected to the player's, dilated for the walls
            _gat = gat; _mapRoot = mapRoot; RoomCount = 1; Source = "client list";
            if (_mask == null || _mask.Length != w * h) _mask = new byte[w * h];
            System.Array.Clear(_mask, 0, _mask.Length);
            var start = NearestWalkable(self);
            var q = new Queue<Vector2Int>(); var seen = new bool[w * h];
            q.Enqueue(start); seen[start.x + start.y * w] = true; int count = 0;
            while (q.Count > 0)
            {
                var c = q.Dequeue(); count++;
                _mask[c.x + c.y * w] = 1;
                for (int k = 0; k < 4; k++)
                {
                    int nx = c.x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = c.y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int i = nx + ny * w;
                    if (seen[i] || !gat.Get(nx, ny).Walkable) continue;
                    seen[i] = true; q.Enqueue(new Vector2Int(nx, ny));
                }
            }
            // dilate for walls (Chebyshev distance ≤ WallCells)
            var src = (byte[])_mask.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (src[x + y * w] != 0) continue;
                    bool near = false;
                    for (int dy = -WallCells; dy <= WallCells && !near; dy++)
                    {
                        int yy = y + dy; if (yy < 0 || yy >= h) continue;
                        for (int dx = -WallCells; dx <= WallCells; dx++)
                        {
                            int xx = x + dx; if (xx < 0 || xx >= w) continue;
                            if (src[xx + yy * w] != 0) { near = true; break; }
                        }
                    }
                    if (near) _mask[x + y * w] = 1;
                }
            Upload(w, h);
            SetRoom(1, false);
            Shader.SetGlobalVector(ParamsId, new Vector4(1, w, h, 0));
            Active = true;
            LastBuildMs = (float)sw.Elapsed.TotalMilliseconds;
            Debug.Log($"[RoomMask] {map}: room of {count} walkable cells around {self} (no room table: a live fill)");
        }

        private void Upload(int w, int h)
        {
            if (_tex == null || _tex.width != w || _tex.height != h)
            {
                if (_tex != null) Destroy(_tex);
                _tex = new Texture2D(w, h, TextureFormat.R8, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "roomMask" };
            }
            _tex.SetPixelData(_mask, 0); _tex.Apply(false, false);
            Shader.SetGlobalTexture(MaskId, _tex);
        }

        private void SetRoom(int room, bool fade)
        {
            if (fade) { PreviousRoom = CurrentRoom; Fade = 0f; Switches++; SwitchedAt = Time.unscaledTime; }
            else { PreviousRoom = room; Fade = 1f; }
            CurrentRoom = room;
            PushFade();
        }

        private void PushFade() => Shader.SetGlobalVector(FadeId, new Vector4(CurrentRoom, PreviousRoom, Fade, 0f));

        /// <summary>The room of a cell (its id; 0 = none, or not culled).</summary>
        public int RoomOf(Vector2 cell)
        {
            if (!Active || _gat == null || _mask == null) return 0;
            int x = Mathf.FloorToInt(cell.x), y = Mathf.FloorToInt(cell.y);
            return x < 0 || y < 0 || x >= _gat.Width || y >= _gat.Height ? 0 : _mask[x + y * _gat.Width];
        }

        /// <summary>The room the character at <paramref name="c"/> stands in: its cell's room, else the nearest walkable cell's with a room (8 cells).</summary>
        private int RoomAt(Vector2Int c)
        {
            int w = _gat.Width;
            if (_gat.InBounds(c.x, c.y) && _mask[c.x + c.y * w] != 0) return _mask[c.x + c.y * w];
            for (int r = 1; r <= 8; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = c.x + dx, y = c.y + dy;
                        if (_gat.InBounds(x, y) && _gat.Get(x, y).Walkable && _mask[x + y * w] != 0) return _mask[x + y * w];
                    }
            return 0;
        }

        /// <summary>Client wave 20 (w20rooms): the character's cell, a few times a second (LokiGame): on a walkable cell of another
        /// room (any move the map change did not announce) that room is drawn, with the fade.</summary>
        public void Track(Vector2 cell)
        {
            if (!Active || Source != "room table" || _gat == null) return;
            int x = Mathf.FloorToInt(cell.x), y = Mathf.FloorToInt(cell.y);
            if (!_gat.InBounds(x, y) || !_gat.Get(x, y).Walkable) return;
            int room = _mask[x + y * _gat.Width];
            if (room == 0 || room == CurrentRoom) return;
            Debug.Log($"[RoomMask] {_map}: the character is in room {room} now (was {CurrentRoom}; a {FadeSeconds:0.0} s fade)");
            SetRoom(room, true);
        }

        /// <summary>Whether a map cell is inside the visible room (always true when inactive).</summary>
        public bool Visible(Vector2 cell)
        {
            if (!Active || _gat == null) return true;
            int x = Mathf.FloorToInt(cell.x), y = Mathf.FloorToInt(cell.y);
            if (x < 0 || y < 0 || x >= _gat.Width || y >= _gat.Height) return false;
            return _mask[x + y * _gat.Width] == CurrentRoom;
        }

        private Vector2Int NearestWalkable(Vector2Int c)
        {
            for (int r = 0; r < 8; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = c.x + dx, y = c.y + dy;
                        if (_gat.InBounds(x, y) && _gat.Get(x, y).Walkable) return new Vector2Int(x, y);
                    }
            return new Vector2Int(Mathf.Clamp(c.x, 0, _gat.Width - 1), Mathf.Clamp(c.y, 0, _gat.Height - 1));
        }

        private void LateUpdate()
        {
            if (!Active) return;
            if (_mapRoot != null) Shader.SetGlobalMatrix(MatId, _mapRoot.worldToLocalMatrix);
            if (Fade < 1f)
            {
                Fade = Mathf.Clamp01((Time.unscaledTime - SwitchedAt) / FadeSeconds);
                if (Fade >= 1f) PreviousRoom = CurrentRoom;
            }
            PushFade();
        }
    }
}
