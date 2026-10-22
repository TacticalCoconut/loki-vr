using System.Collections.Generic;
using Loki.Net;
using Loki.VR;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 20 (w20pen), the design's point 57 (the AIR PEN: "It writes in the air in 3D space"; "Storing the pen away does not delete what has
    /// been written"; "They should stay on the map until you erase them, log out, or until you change maps"; others see them, by "Drawings seen
    /// by" and "Drawings I see", both friends only by default: design notes of 2026-10-10). Every drawing on the map, mine and other players', and
    /// how mine travel. The pen itself (taking it, its trigger, its eraser) is LokiGame.W20Pen.cs.
    ///   Where     a child of the map (World/MapLoader.cs's transform), so a stroke is FIXED IN THE WORLD: it stays where it was drawn on the
    ///             diorama as the table moves, turns or scales, and in every view. Points are kept in the map's frame (cells, World/RoSpace.cs),
    ///             as the floating headsets' poses are; a stroke's thickness too (so it grows with the table).
    ///   Drawn     each stroke a solid tube (<see cref="Sides"/> sides, flat ends) in its drawer's colour (VR/HeadsetAvatars.cs TintFor: the
    ///             colour their floating headset has on every client), Resources/Shaders/LokiPenStroke.shader: depth-tested, writing depth.
    ///             No collider: the laser, the look and the panel grab never meet a drawing.
    ///   Mine      sent on <see cref="IDrawChannel"/> (Net/DrawWire.cs) while my headset is shared and the relay forwards drawings: a stroke as
    ///             it is drawn (a part every <see cref="LiveSeconds"/> s, the last one marked), erasures at once, and EVERYTHING again (the list of
    ///             my strokes, then every part) when a link starts, when a player comes onto my map, when my "Drawings seen by" changes (first a
    ///             clear to the old audience) and every <see cref="RefreshSeconds"/> s, paced at <see cref="RefreshRate"/> frames a second: the
    ///             relay keeps nothing. "Drawings seen by" rides in every frame: the RELAY sends it to nobody else.
    ///   Theirs    kept per player with what the relay says that player is to me (friend / family / guild); drawn only when my "Drawings I
    ///             see" allows it (<see cref="Shows"/>: fail closed). A player's drawings go when the relay says it left my map or the relay
    ///             (GONE: their map change, logout, or their headset no longer shared), when they erase them (also by the list they send), when
    ///             my link drops, when I change map, and a stroke not sent again for <see cref="StaleSeconds"/> s.
    ///   Limits    <see cref="DrawWire.MaxStrokes"/> strokes, <see cref="DrawWire.MaxStrokePoints"/> points a stroke and <see cref="DrawWire.MaxPoints"/>
    ///             points in all per player: my oldest strokes go first when I draw past them; other players' frames past them are not taken.
    /// </summary>
    [DefaultExecutionOrder(615)] // after HeadsetAvatars (600: its Poll raises GONE) and FriendGestures (610); the pen (LokiGame.Update) ran before
    public sealed class PenDrawings : MonoBehaviour
    {
        public const int Sides = 6;
        public const float LiveSeconds = 0.1f, RefreshSeconds = 20f, RefreshRate = 25f, StaleSeconds = 75f, ArrivalDelay = 1f;

        public sealed class Stroke
        {
            public int Id; public float Width; public Color Colour;
            /// <summary>Points in the map's frame (cells); for another player's stroke the ones not received yet are not <see cref="Has"/>.</summary>
            public readonly List<Vector3> Points = new List<Vector3>();
            public readonly List<bool> Has = new List<bool>();
            /// <summary>The stroke ended (its last part came; mine: the trigger was let go).</summary>
            public bool Complete;
            public float RefreshedAt;
            public GameObject Go; public Mesh Mesh; public bool Dirty = true;
            public int Known { get { int n = 0; foreach (var h in Has) if (h) n++; return n; } }
        }
        public sealed class Owner
        {
            public uint Aid; public Transform Root; public byte Relation; public float SeenAt;
            public readonly Dictionary<int, Stroke> Strokes = new Dictionary<int, Stroke>();
            /// <summary>Mine: the order they were drawn in (the oldest go first past the limits).</summary>
            public readonly List<int> Order = new List<int>();
            public int Points;
        }

        private World.MapLoader _map; private HeadsetAvatars _heads; private IDrawChannel _ch; private uint _self;
        private Transform _root; private Material _mat;
        private readonly Dictionary<uint, Owner> _owners = new Dictionary<uint, Owner>();
        private readonly DrawWire.Msg _in = new DrawWire.Msg();
        public Owner Mine { get; private set; }
        public IDrawChannel Channel => _ch;
        /// <summary>Hidden as a whole (Horde's arena is shown instead of the map).</summary>
        public bool Hidden { get; set; }

        /// <summary>Frames received, refused as malformed, not taken past the limits; my frames handed to the channel; full refresh passes
        /// started; strokes of others dropped as stale (smoke, log).</summary>
        public int Received { get; private set; }
        public int Refused { get; private set; }
        public int OverLimit { get; private set; }
        public int Frames { get; private set; }
        public int Passes { get; private set; }
        public int StaleDropped { get; private set; }

        public static PenDrawings Create(World.MapLoader map, HeadsetAvatars heads, IDrawChannel ch, uint self)
        {
            var d = new GameObject("PenDrawings").AddComponent<PenDrawings>();
            d._map = map; d._heads = heads; d._ch = ch; d._self = self;
            d._root = new GameObject("drawings").transform; d._root.SetParent(map.transform, false);
            var sh = Shader.Find("Loki/PenStroke");
            if (sh == null) { Debug.LogWarning("[Pen] Loki/PenStroke missing: drawn with Sprites/Default"); sh = Shader.Find("Sprites/Default"); }
            d._mat = new Material(sh) { name = "PenStroke", color = Color.white };
            d.Mine = d.OwnerOf(self);
            if (heads != null && heads.Channel != null)
            {
                heads.Channel.Gone += aid => d.DropOwner(aid, "gone (the relay: left this map or the game)");
                heads.Channel.Lost += () => d.DropOthers("my link to the relay dropped");
            }
            return d;
        }

        public Owner Get(uint aid) => _owners.TryGetValue(aid, out var o) ? o : null;
        public IEnumerable<Owner> Owners => _owners.Values;

        private Owner OwnerOf(uint aid)
        {
            if (_owners.TryGetValue(aid, out var o)) return o;
            o = new Owner { Aid = aid, SeenAt = Time.unscaledTime };
            o.Root = new GameObject(aid == _self ? "mine" : $"drawings:{aid}").transform;
            o.Root.SetParent(_root, false);
            _owners[aid] = o;
            return o;
        }

        /// <summary>The map's transform the drawings hang from: kept under it (a map change keeps this object: MapLoader destroys only its map roots).</summary>
        private void EnsureRoot()
        {
            if (_root == null) { _root = new GameObject("drawings").transform; foreach (var o in _owners.Values) if (o.Root != null) o.Root.SetParent(_root, false); }
            if (_map != null && _root.parent != _map.transform) _root.SetParent(_map.transform, false);
        }

        // ------------------------------------------------------------------------------------------------ whose drawings I see

        /// <summary>"Drawings I see" (VR/Comfort.cs DrawFilter) lets me see a player who is <paramref name="relation"/> to me (the relay's bits).</summary>
        public static bool Shows(byte filter, byte relation) => (filter & GestureWire.ToEveryone) != 0 || (filter & relation & (GestureWire.ToFriends | GestureWire.ToFamily | GestureWire.ToGuild)) != 0;

        /// <summary>Another player's drawings are drawn here now (their owner exists, my filter allows them, nothing hides the drawings).</summary>
        public bool Drawn(uint aid) => _owners.TryGetValue(aid, out var o) && o.Root != null && o.Root.gameObject.activeInHierarchy;

        // ------------------------------------------------------------------------------------------------ my strokes

        private int _nextId = 1; private Stroke _drawing;
        /// <summary>The stroke being drawn now (null: none).</summary>
        public Stroke Drawing => _drawing;

        /// <summary>A new stroke of mine, <paramref name="width"/> cells thick; its first point.</summary>
        public Stroke Begin(Vector3 mapPoint, float width, Color colour)
        {
            if (_drawing != null) End();
            int id = NextFreeId();
            var s = new Stroke { Id = id, Width = width, Colour = colour, RefreshedAt = Time.unscaledTime };
            s.Points.Add(mapPoint); s.Has.Add(true);
            Mine.Strokes[id] = s; Mine.Order.Add(id); Mine.Points++;
            _drawing = s; _liveSent = 0; _liveAt = -1f;
            Trim(keep: s);
            return s;
        }

        /// <summary>The stroke being drawn gets a point (false: it is full).</summary>
        public bool Add(Vector3 mapPoint)
        {
            var s = _drawing; if (s == null) return false;
            if (s.Points.Count >= DrawWire.MaxStrokePoints) return false;
            s.Points.Add(mapPoint); s.Has.Add(true); s.Dirty = true; Mine.Points++;
            Trim(keep: s);
            return true;
        }

        /// <summary>The stroke being drawn ends (a stroke of one point gets a second one a width above it: a dot).</summary>
        public void End()
        {
            var s = _drawing; if (s == null) return;
            if (s.Points.Count == 1) { s.Points.Add(s.Points[0] + Vector3.up * s.Width); s.Has.Add(true); Mine.Points++; }
            s.Complete = true; s.Dirty = true; _drawing = null; _liveEnd = s;
        }

        /// <summary>The next stroke id (1..65535, counting on from the last; one still in use is skipped: an id comes back only after 65,535 strokes).</summary>
        private int NextFreeId()
        {
            for (int k = 0; k < 65535; k++)
            {
                int id = _nextId; _nextId = _nextId % 65535 + 1;
                if (!Mine.Strokes.ContainsKey(id)) return id;
            }
            return 1;
        }

        /// <summary>My oldest strokes go while I am past the limits (never <paramref name="keep"/>, the one being drawn).</summary>
        private void Trim(Stroke keep)
        {
            var gone = new List<int>();
            while ((Mine.Order.Count > DrawWire.MaxStrokes || Mine.Points > DrawWire.MaxPoints) && Mine.Order.Count > 1 && Mine.Order[0] != keep.Id)
            {
                int id = Mine.Order[0]; gone.Add(id); RemoveStroke(Mine, id);
            }
            if (gone.Count > 0) { Debug.Log($"[Pen] past the limits ({DrawWire.MaxStrokes} strokes, {DrawWire.MaxPoints} points): my oldest {gone.Count} stroke(s) erased"); SentErase(gone); }
        }

        /// <summary>My strokes that a ball of <paramref name="radius"/> cells at <paramref name="mapPoint"/> touches (their own thickness counted).</summary>
        public List<int> MineNear(Vector3 mapPoint, float radius)
        {
            var l = new List<int>();
            foreach (var s in Mine.Strokes.Values)
            {
                if (s == _drawing) continue;
                float r = radius + s.Width * 0.5f, r2 = r * r;
                var p = s.Points;
                for (int i = 0; i < p.Count; i++)
                {
                    float d2 = i + 1 < p.Count ? SegmentDistance2(mapPoint, p[i], p[i + 1]) : (mapPoint - p[i]).sqrMagnitude;
                    if (d2 <= r2) { l.Add(s.Id); break; }
                }
            }
            return l;
        }

        private static float SegmentDistance2(Vector3 x, Vector3 a, Vector3 b)
        {
            var ab = b - a; float len2 = ab.sqrMagnitude;
            float t = len2 < 1e-12f ? 0f : Mathf.Clamp01(Vector3.Dot(x - a, ab) / len2);
            return (a + ab * t - x).sqrMagnitude;
        }

        /// <summary>Erases strokes of mine (the eraser touched them); they are gone for the others at once.</summary>
        public void EraseMine(List<int> ids)
        {
            var done = new List<int>();
            foreach (var id in ids) if (Mine.Strokes.ContainsKey(id)) { RemoveStroke(Mine, id); done.Add(id); }
            if (done.Count > 0) SentErase(done);
        }

        /// <summary>Every stroke of mine is gone (the eraser's long press, a map change, a logout); for the others at once (if they can hear it).</summary>
        public int ClearMine(string why)
        {
            int n = Mine.Strokes.Count;
            if (_drawing != null) _drawing = null;
            foreach (var id in new List<int>(Mine.Strokes.Keys)) RemoveStroke(Mine, id);
            Mine.Order.Clear(); Mine.Points = 0; _liveEnd = null;
            _queue.Clear();
            if (_audience != 0 && _sending) Send(DrawWire.EncodeClear(_audience), -1);
            if (n > 0) Debug.Log($"[Pen] my {n} stroke(s) erased ({why})");
            return n;
        }

        private void RemoveStroke(Owner o, int id)
        {
            if (!o.Strokes.TryGetValue(id, out var s)) return;
            o.Points -= s.Points.Count; o.Strokes.Remove(id); o.Order.Remove(id);
            if (s.Go != null) Destroy(s.Go);
            if (s.Mesh != null) Destroy(s.Mesh);
            if (o == Mine) _queue.RemoveAll(q => q.id == id);
        }

        /// <summary>Every other player's drawings go (I changed map, my link dropped).</summary>
        public void DropOthers(string why)
        {
            int n = 0;
            foreach (var aid in new List<uint>(_owners.Keys)) if (aid != _self) { n += _owners[aid].Strokes.Count; DropOwner(aid, null); }
            if (n > 0) Debug.Log($"[Pen] other players' {n} stroke(s) dropped ({why})");
        }

        private void DropOwner(uint aid, string why)
        {
            if (aid == _self || !_owners.TryGetValue(aid, out var o)) return;
            int n = o.Strokes.Count;
            foreach (var id in new List<int>(o.Strokes.Keys)) RemoveStroke(o, id);
            if (o.Root != null) Destroy(o.Root.gameObject);
            _owners.Remove(aid);
            if (why != null && n > 0) Debug.Log($"[Pen] AID {aid}: {n} stroke(s) dropped ({why})");
        }

        // ------------------------------------------------------------------------------------------------ sending mine

        private readonly List<(int id, byte[] frame)> _queue = new List<(int, byte[])>();
        private int _links = -1, _liveSent; private float _liveAt = -1f, _refreshAt = float.MaxValue, _pace; private byte _audience; private bool _sending;
        private Stroke _liveEnd;
        private readonly HashSet<uint> _onMap = new HashSet<uint>(); private string _arrivalMap;
        /// <summary>My audience in force (the relay's bits; 0 nobody), whether my drawings go out now (smoke, log).</summary>
        public byte Audience => _audience;
        public bool Sending => _sending;
        /// <summary>Frames of a refresh pass still waiting (smoke).</summary>
        public int Queued => _queue.Count;

        /// <summary>Everything of mine goes out again in <paramref name="delay"/> s (or sooner when already asked).</summary>
        public void RequestRefresh(float delay) => _refreshAt = Mathf.Min(_refreshAt, Time.unscaledTime + delay);

        private void Send(byte[] f, int id)
        {
            if (f == null || _ch == null) return;
            _ch.Send(f); Frames++;
        }

        private void SentErase(List<int> ids)
        {
            if (!_sending || _audience == 0 || ids.Count == 0) return;
            for (int i = 0; i < ids.Count; i += DrawWire.MaxStrokes) Send(DrawWire.EncodeIds(_audience, DrawWire.OpErase, ids.GetRange(i, Mathf.Min(DrawWire.MaxStrokes, ids.Count - i))), -1);
        }

        /// <summary>The parts of points <paramref name="from"/>.. of a stroke (each part after the first starts on the previous one's last point,
        /// so no segment is missing between them); the last part marked when the stroke is complete.</summary>
        private void Parts(Stroke s, int from, List<(int id, byte[] frame)> into, byte audience)
        {
            int n = s.Points.Count; if (n == 0) return;
            from = Mathf.Clamp(from, 0, n - 1);
            var xyz = new float[3 * n];
            for (int i = 0; i < n; i++) { var p = s.Points[i]; xyz[3 * i] = p.x; xyz[3 * i + 1] = p.y; xyz[3 * i + 2] = p.z; }
            int at = from;
            while (true)
            {
                int count = Mathf.Min(DrawWire.MaxPartPoints, n - at);
                bool last = at + count >= n;
                var f = DrawWire.EncodeStroke(audience, s.Id, at, last && s.Complete, 0, s.Width, xyz, at, count);
                if (f != null) into.Add((s.Id, f));
                if (last) break;
                at += count - 1;
            }
        }

        private readonly List<(int id, byte[] frame)> _tmp = new List<(int, byte[])>();

        /// <summary>Once a frame (LateUpdate): what of mine goes out. on: the relay forwards drawings and my headset is shared.</summary>
        private void SendTick(float now, float dt)
        {
            bool on = _ch != null && _ch.CanShare && _heads != null && _heads.Sharing;
            byte aud = Comfort.DrawAudience;
            if (!on)
            {
                // the relay hid my headset (LEAVE / GONE: the others dropped mine) or the link is down: all is sent again later
                _queue.Clear(); _liveEnd = null; _liveSent = 0;
                _sending = false; _audience = aud; return;
            }
            // a new link, or my headset shared again: everything a second later (my poses first: the relay forwards nothing of a player whose
            // map it does not know yet)
            if (!_sending || _ch.Links != _links) { _links = _ch.Links; _sending = true; _audience = aud; _refreshAt = now + ArrivalDelay; _liveSent = 0; }
            if (aud != _audience)
            {
                // the choice changed: whoever had my drawings drops them (a clear to the old audience), then all again to the new one
                if (_audience != 0) Send(DrawWire.EncodeClear(_audience), -1);
                Debug.Log($"[Pen] my drawings now shown to {Who(aud)} (were: {Who(_audience)})");
                _audience = aud; _queue.Clear(); _refreshAt = now; _liveSent = 0;
            }
            if (aud == 0) { _queue.Clear(); _refreshAt = now + RefreshSeconds; return; } // ("nobody": nothing is sent)
            // a player came onto my map (a new floating headset here): all again a moment later (it said READY before its first pose)
            Arrivals(now);
            // the stroke being drawn: its new points every LiveSeconds; the stroke just ended: its last part at once
            var live = _drawing ?? _liveEnd;
            if (live != null && Mine.Strokes.ContainsKey(live.Id) && (live == _liveEnd || (now - _liveAt >= LiveSeconds && live.Points.Count > _liveSent)))
            {
                _tmp.Clear(); Parts(live, Mathf.Max(0, _liveSent - 1), _tmp, aud);
                foreach (var (_, f) in _tmp) Send(f, live.Id);
                _liveSent = live.Points.Count; _liveAt = now;
            }
            if (_liveEnd != null) { if (_drawing == null) _liveSent = 0; _liveEnd = null; }
            // everything again: the list of my strokes first (a viewer drops any other of mine), then every part, paced
            if (now >= _refreshAt && _queue.Count == 0)
            {
                Passes++; _refreshAt = now + RefreshSeconds;
                var ids = new List<int>(Mine.Order);
                if (ids.Count <= DrawWire.MaxStrokes) _queue.Add((-1, DrawWire.EncodeIds(aud, DrawWire.OpList, ids)));
                foreach (var id in Mine.Order) if (Mine.Strokes.TryGetValue(id, out var s)) Parts(s, 0, _queue, aud);
            }
            if (_queue.Count > 0)
            {
                _pace += dt * RefreshRate; int n = Mathf.Min(_queue.Count, Mathf.FloorToInt(_pace));
                _pace -= n; if (_pace > 2f) _pace = 2f;
                for (int i = 0; i < n; i++) Send(_queue[i].frame, _queue[i].id);
                _queue.RemoveRange(0, n);
            }
            else _pace = 0f;
        }

        private readonly HashSet<uint> _hereSet = new HashSet<uint>();
        private readonly List<uint> _goneList = new List<uint>();

        /// <summary>A player whose headset newly stands on my map: everything of mine again in <see cref="ArrivalDelay"/> s.</summary>
        private void Arrivals(float now)
        {
            string map = _heads.CurrentMap;
            if (map != _arrivalMap) { _onMap.Clear(); _arrivalMap = map; }
            _hereSet.Clear();
            foreach (var a in _heads.All)
                if (a.HasPose && a.Target.Map != null && string.Equals(a.Target.Map, map, System.StringComparison.OrdinalIgnoreCase)) _hereSet.Add(a.Aid);
            bool arrived = false;
            foreach (var aid in _hereSet) if (_onMap.Add(aid)) { arrived = true; Debug.Log($"[Pen] AID {aid} is on my map: my drawings go to them in {ArrivalDelay:0.0} s"); }
            if (_onMap.Count > _hereSet.Count)
            {
                _goneList.Clear();
                foreach (var aid in _onMap) if (!_hereSet.Contains(aid)) _goneList.Add(aid);
                for (int i = 0; i < _goneList.Count; i++) _onMap.Remove(_goneList[i]);
            }
            if (arrived && Mine.Strokes.Count > 0) RequestRefresh(ArrivalDelay);
        }

        public static string Who(byte audience)
        {
            if ((audience & GestureWire.ToEveryone) != 0) return "everyone";
            var l = new List<string>();
            if ((audience & GestureWire.ToFriends) != 0) l.Add("friends");
            if ((audience & GestureWire.ToFamily) != 0) l.Add("family");
            if ((audience & GestureWire.ToGuild) != 0) l.Add("guild");
            return l.Count == 0 ? "nobody" : string.Join(" + ", l);
        }

        // ------------------------------------------------------------------------------------------------ receiving theirs

        private void Take(uint aid, byte relation, byte[] body, float now)
        {
            Received++;
            if (aid == _self) return;
            if (!DrawWire.TryDecode(body, 0, body.Length, _in)) { Refused++; if (Refused <= 5) Debug.LogWarning($"[Pen] a drawing frame of AID {aid} could not be read ({body.Length} bytes): not drawn"); return; }
            bool known = _owners.ContainsKey(aid);
            var o = OwnerOf(aid); o.Relation = relation; o.SeenAt = now;
            if (!known) Debug.Log($"[Pen] first drawing frame of AID {aid} (to me: {Who(relation)}; my \"Drawings I see\": {Who(Comfort.DrawFilter)}: {(Shows(Comfort.DrawFilter, relation) ? "drawn" : "not drawn")})");
            var m = _in;
            switch (m.Op)
            {
                case DrawWire.OpStroke:
                {
                    if (!o.Strokes.TryGetValue(m.Id, out var s))
                    {
                        if (o.Strokes.Count >= DrawWire.MaxStrokes) { OverLimit++; return; }
                        s = new Stroke { Id = m.Id, Colour = HeadsetAvatars.TintFor(aid) };
                        o.Strokes[m.Id] = s; o.Order.Add(m.Id);
                    }
                    int need = m.First + m.Count, grow = Mathf.Max(0, need - s.Points.Count);
                    if (o.Points + grow > DrawWire.MaxPoints) { OverLimit++; return; }
                    while (s.Points.Count < need) { s.Points.Add(Vector3.zero); s.Has.Add(false); }
                    o.Points += grow;
                    for (int i = 0; i < m.Count; i++) { s.Points[m.First + i] = new Vector3(m.Points[3 * i], m.Points[3 * i + 1], m.Points[3 * i + 2]); s.Has[m.First + i] = true; }
                    if (m.Last && s.Points.Count > need) { o.Points -= s.Points.Count - need; s.Points.RemoveRange(need, s.Points.Count - need); s.Has.RemoveRange(need, s.Has.Count - need); }
                    s.Width = m.Width; s.Complete |= m.Last; s.RefreshedAt = now; s.Dirty = true;
                    break;
                }
                case DrawWire.OpErase: foreach (var id in m.Ids) RemoveStroke(o, id); break;
                case DrawWire.OpClear: foreach (var id in new List<int>(o.Strokes.Keys)) RemoveStroke(o, id); break;
                case DrawWire.OpList:
                {
                    var keep = new HashSet<int>(m.Ids);
                    foreach (var id in new List<int>(o.Strokes.Keys)) if (!keep.Contains(id)) RemoveStroke(o, id);
                    foreach (var id in m.Ids) if (o.Strokes.TryGetValue(id, out var s)) s.RefreshedAt = now; // (a stroke still being drawn is listed before its parts come)
                    break;
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ the frame

        private void LateUpdate()
        {
            if (_map == null) return;
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            EnsureRoot();
            if (_ch != null) while (_ch.TryTake(out uint aid, out byte rel, out var body)) Take(aid, rel, body, now);
            SendTick(now, dt);
            bool shown = !Hidden;
            if (_root.gameObject.activeSelf != shown) _root.gameObject.SetActive(shown);
            byte filter = Comfort.DrawFilter;
            var stale = (List<(Owner, int)>)null;
            foreach (var o in _owners.Values)
            {
                bool mine = o.Aid == _self, show = mine || Shows(filter, o.Relation);
                if (o.Root.gameObject.activeSelf != show) o.Root.gameObject.SetActive(show);
                foreach (var s in o.Strokes.Values)
                {
                    if (!mine && now - s.RefreshedAt > StaleSeconds) (stale ??= new List<(Owner, int)>()).Add((o, s.Id));
                    else if (s.Dirty && show) Build(o, s);
                }
            }
            if (stale != null) foreach (var (o, id) in stale) { RemoveStroke(o, id); StaleDropped++; Debug.Log($"[Pen] AID {o.Aid}: stroke {id} not sent again for {StaleSeconds:0} s: dropped"); }
        }

        // ------------------------------------------------------------------------------------------------ the tubes

        private readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>();
        private readonly List<Color> _c = new List<Color>(); private readonly List<int> _t = new List<int>();

        /// <summary>The stroke's mesh made again from its points: a tube along every run of received points (map cells: the object is at the map's
        /// origin), flat ends.</summary>
        private void Build(Owner o, Stroke s)
        {
            s.Dirty = false;
            if (s.Go == null)
            {
                s.Go = new GameObject($"stroke:{s.Id}"); s.Go.transform.SetParent(o.Root, false);
                s.Mesh = new Mesh { name = "PenStroke" }; s.Mesh.MarkDynamic();
                s.Go.AddComponent<MeshFilter>().sharedMesh = s.Mesh;
                var mr = s.Go.AddComponent<MeshRenderer>(); mr.sharedMaterial = _mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            }
            _v.Clear(); _n.Clear(); _c.Clear(); _t.Clear();
            var run = new List<Vector3>();
            for (int i = 0; i <= s.Points.Count; i++)
            {
                bool has = i < s.Points.Count && s.Has[i];
                if (has) { var p = s.Points[i]; if (run.Count == 0 || (p - run[run.Count - 1]).sqrMagnitude > 1e-12f) run.Add(p); continue; }
                if (run.Count > 0) { if (run.Count == 1) run.Add(run[0] + Vector3.up * s.Width); Tube(run, s.Width * 0.5f, s.Colour); run.Clear(); }
            }
            s.Mesh.Clear();
            s.Mesh.SetVertices(_v); s.Mesh.SetNormals(_n); s.Mesh.SetColors(_c); s.Mesh.SetTriangles(_t, 0);
            s.Mesh.RecalculateBounds();
        }

        private void Tube(List<Vector3> p, float r, Color col)
        {
            int n = p.Count, b = _v.Count;
            // (a vertex colour is used as it is, a material's colour is taken from sRGB: in the project's linear colour space the tube gets the
            // linear value, so it is drawn in the very colour of its drawer's headset and pen)
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) col = col.linear;
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                var t = (p[Mathf.Min(i + 1, n - 1)] - p[Mathf.Max(i - 1, 0)]); if (t.sqrMagnitude < 1e-12f) t = Vector3.up; t.Normalize();
                // the ring's frame carried along the line (no twist): the last normal made square to this point's direction
                if (i == 0) { normal = Vector3.Cross(t, Mathf.Abs(t.y) < 0.9f ? Vector3.up : Vector3.right).normalized; }
                else { normal -= t * Vector3.Dot(normal, t); if (normal.sqrMagnitude < 1e-10f) normal = Vector3.Cross(t, Mathf.Abs(t.y) < 0.9f ? Vector3.up : Vector3.right); normal.Normalize(); }
                var bi = Vector3.Cross(t, normal);
                for (int k = 0; k < Sides; k++)
                {
                    float a = k * 2f * Mathf.PI / Sides; var d = normal * Mathf.Cos(a) + bi * Mathf.Sin(a);
                    _v.Add(p[i] + d * r); _n.Add(d); _c.Add(col);
                }
            }
            for (int i = 0; i + 1 < n; i++)
                for (int k = 0; k < Sides; k++)
                {
                    int a0 = b + i * Sides + k, a1 = b + i * Sides + (k + 1) % Sides, b0 = a0 + Sides, b1 = a1 + Sides;
                    _t.Add(a0); _t.Add(b0); _t.Add(a1); _t.Add(a1); _t.Add(b0); _t.Add(b1);
                }
            // the flat ends
            for (int e = 0; e < 2; e++)
            {
                int ring = e == 0 ? b : b + (n - 1) * Sides;
                var dir = e == 0 ? (p[0] - p[1]).normalized : (p[n - 1] - p[n - 2]).normalized;
                int c = _v.Count; _v.Add(e == 0 ? p[0] : p[n - 1]); _n.Add(dir); _c.Add(col);
                for (int k = 0; k < Sides; k++) { _t.Add(c); _t.Add(ring + k); _t.Add(ring + (k + 1) % Sides); }
            }
        }

        private void OnDestroy()
        {
            // leaving the game (the character list reloads the scene, a quit ends it): every drawing here goes with it
            int mine = Mine != null ? Mine.Strokes.Count : 0, others = 0;
            foreach (var o in _owners.Values) if (o != Mine) others += o.Strokes.Count;
            if (mine + others > 0) Debug.Log($"[Pen] the drawings go with the scene: {mine} stroke(s) of mine, {others} of other players");
            foreach (var o in _owners.Values) foreach (var s in o.Strokes.Values) if (s.Mesh != null) Destroy(s.Mesh);
            if (_mat != null) Destroy(_mat);
            if (_root != null) Destroy(_root.gameObject);
        }
    }
}
