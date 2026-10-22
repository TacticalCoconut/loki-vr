using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Loki.Net;
using Loki.Sprites;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Navigation (client wave 4, w4world; before it wave 1 routed over rAthena's own warp list, data/warps.tsv — server data).
    ///
    /// The order: 0x08e2 ZC_NAVIGATION (rAthena clif_navigateTo clif.cpp:21463-21497, the `navigateto` script command script.cpp:24489-24516,
    /// 162 uses in rAthena's NPC scripts): type.B (0 go to map + x/y, 1 go to the map, 3 hunt a monster on the map: x/y 0), flag.B (which
    /// services the route may use, e_navigation_flag script.hpp:724-733: the ones digit airship, the tens digit scroll, the hundreds digit
    /// Kafra — "actually 1-9", "11-99", "101-109", "111-255"; the script's default is 101, Kafra and airship), hideWindow.B (1 = do not open
    /// the navigation window: the script's default), map[16], x.W, y.W, mobId.W.
    ///
    /// The route: the client's own navigation data (data\luafiles514\lua files\navigation\navi_*_br.lub → data/navigation.tsv on first run,
    /// Data/WorldTools.cs): every link a player can take (Navi_Link: 3510 portals of type 200, 522 NPC transports of type 201, 166 service
    /// NPCs of type 204 — Kafra employees' sprites 112-117 on 63 of them, the ships — and 45 airship links of type 205), and after each link
    /// the links reachable on the map it leads to with the walk to each (Navi_Distance), plus, for NPCs, the walk from each way into their
    /// map (Navi_NpcDistance). The search is a Dijkstra over links, fewest links first then the shortest walk — the order roBrowser's
    /// MapPathFinder.js:242-293 uses (it is the only reference, and its loader leaves every table empty: rB never routes across maps).
    /// Types 200 and 201 are always used (roBrowser passes [200, 201], Navigation.js:1300); 204 when the flag allows Kafra, 205 when it
    /// allows the airship (roBrowser's "Services" box adds 202-205, :1301-1304). Navi_Scroll is empty in this data: scrolls add nothing.
    /// The first leg (from where we stand to a link on this map) and the last one (from the arrival to x/y) are straight-line distances.
    ///
    /// The guide: an arrow over our character pointing at the next step on this map (the portal, the NPC to talk to, or the target), the
    /// minimap marker, a chat line saying what to do at each step, the Guide window (World/GuideWindow.cs); it clears on arrival (≤ 3
    /// cells), on a new order, or when no route exists, and re-plans after every map change.
    /// </summary>
    public sealed class Navigation
    {
        // ------------------------------------------------------------------ the client's navigation data
        public sealed class Link { public int Id, X, Y, Type, Class, Dx, Dy; public string Map, Dest, Name, Label; }
        public sealed class Npc { public int Id, X, Y, Type, Class; public string Map, Name, Label; }
        public sealed class Mob { public string Map, Name, Aegis; public int Id, Type, Class, Level; }
        /// <summary>Wave 28 (wtextc): English never shows the Portuguese name (it did when the English one was empty: the guide then names
        /// the spot by its kind, GuideWindow.cs).</summary>
        public sealed class TownSpot { public string Map, NameEn, NamePt; public int Type, X, Y; public string Name => ClientText.Portuguese ? (NamePt.Length > 0 ? NamePt : NameEn) : NameEn; }
        /// <summary>Wave 28 (wtextc): the map's Portuguese name (navi_map_br.lub) and English one (M column 7, the install's
        /// System\english\mapinfo.lub; the text pack over both); <see cref="Name"/> in the game's language.</summary>
        public sealed class MapInfo { public string Map, NameEn = "", NamePt = ""; public int Kind; public string Name => ClientText.Portuguese && NamePt.Length > 0 ? NamePt : NameEn; }
        /// <summary>Wave 28: the install's English name of a map ("" when it has none).</summary>
        public static string EnglishMapName(string map) { Load(); string k = CanonicalMap(map); return k != null && _maps.TryGetValue(k, out var m) ? m.NameEn : ""; }
        /// <summary>Wave 28: every navigation text a player can see in the game's language (the console's map search shows map names).</summary>
        public static IEnumerable<string> ShownTexts() { Load(); foreach (var m in _maps.Values) if (m.Name.Length > 0) yield return m.Name; foreach (var l in _town.Values) foreach (var t in l) if (t.Name.Length > 0) yield return t.Name; }

        private static Dictionary<int, Link> _links; private static Dictionary<string, List<Link>> _linksFrom; private static Dictionary<string, List<Link>> _linksTo;
        private static Dictionary<int, List<(int to, int dist)>> _after; private static Dictionary<int, Npc> _npcs; private static Dictionary<string, List<Npc>> _npcsOn;
        private static Dictionary<int, List<(int link, int dist)>> _npcEntries; private static List<Mob> _mobs;
        private static Dictionary<string, List<TownSpot>> _town; private static Dictionary<string, MapInfo> _maps;
        public static int LinkCount { get { Load(); return _links.Count; } }
        public static int NpcCount { get { Load(); return _npcs.Count; } }
        public static int MobCount { get { Load(); return _mobs.Count; } }
        public static int EdgeCount { get { Load(); int n = 0; foreach (var l in _after.Values) n += l.Count; return n; } }
        public static IReadOnlyDictionary<string, MapInfo> Maps { get { Load(); return _maps; } }
        /// <summary>Client wave 11 (w11menu): every monster spawn row of the table (the console commands' monster browser: Game/GmPickers.cs).</summary>
        public static IEnumerable<Mob> AllMobs() { Load(); return _mobs; }

        private static void Load()
        {
            if (_links != null) return;
            _links = new Dictionary<int, Link>(); _linksFrom = new Dictionary<string, List<Link>>(StringComparer.OrdinalIgnoreCase); _linksTo = new Dictionary<string, List<Link>>(StringComparer.OrdinalIgnoreCase);
            _after = new Dictionary<int, List<(int, int)>>(); _npcs = new Dictionary<int, Npc>(); _npcsOn = new Dictionary<string, List<Npc>>(StringComparer.OrdinalIgnoreCase); _npcEntries = new Dictionary<int, List<(int, int)>>();
            _mobs = new List<Mob>(); _town = new Dictionary<string, List<TownSpot>>(StringComparer.OrdinalIgnoreCase); _maps = new Dictionary<string, MapInfo>(StringComparer.OrdinalIgnoreCase);
            string p = Loki.Data.ClientData.File("navigation.tsv");
            if (!File.Exists(p)) { Debug.LogWarning("[Nav] missing " + p + " (made on first run: Data/WorldTools.cs)"); return; }
            int I(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
            List<(int, int)> Pairs(string s)
            {
                var o = new List<(int, int)>();
                foreach (var pr in s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) { int c = pr.IndexOf(':'); if (c > 0) o.Add((I(pr.Substring(0, c)), I(pr.Substring(c + 1)))); }
                return o;
            }
            foreach (var line in File.ReadLines(p, System.Text.Encoding.UTF8))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var c = line.Split('\t');
                switch (c[0])
                {
                    case "M" when c.Length >= 6: _maps[c[1]] = new MapInfo { Map = c[1], NamePt = c[2], NameEn = c.Length > 6 ? c[6] : "", Kind = I(c[3]) }; break;
                    case "L" when c.Length >= 12:
                    {
                        var l = new Link { Id = I(c[1]), Map = c[2], X = I(c[3]), Y = I(c[4]), Type = I(c[5]), Class = I(c[6]), Dest = c[7], Dx = I(c[8]), Dy = I(c[9]), Name = c[10], Label = c[11] };
                        _links[l.Id] = l;
                        if (!_linksFrom.TryGetValue(l.Map, out var lst)) _linksFrom[l.Map] = lst = new List<Link>();
                        lst.Add(l);
                        if (!string.IsNullOrEmpty(l.Dest))
                        {
                            if (!_linksTo.TryGetValue(l.Dest, out var tlst)) _linksTo[l.Dest] = tlst = new List<Link>();
                            tlst.Add(l);
                        }
                        break;
                    }
                    case "E" when c.Length >= 3: _after[I(c[1])] = Pairs(c[2]); break;
                    case "N" when c.Length >= 9:
                    {
                        var n = new Npc { Id = I(c[1]), Map = c[2], X = I(c[3]), Y = I(c[4]), Type = I(c[5]), Class = I(c[6]), Name = c[7], Label = c[8] };
                        _npcs[n.Id] = n;
                        if (!_npcsOn.TryGetValue(n.Map, out var nlst)) _npcsOn[n.Map] = nlst = new List<Npc>();
                        nlst.Add(n);
                        break;
                    }
                    case "D" when c.Length >= 3: _npcEntries[I(c[1])] = Pairs(c[2]); break;
                    case "B" when c.Length >= 9: _mobs.Add(new Mob { Map = c[1], Id = I(c[2]), Type = I(c[3]), Class = I(c[4]), Name = c[5], Aegis = c[6], Level = I(c[7]) }); break;
                    case "T" when c.Length >= 7:
                    {
                        var t = new TownSpot { Map = c[1], Type = I(c[2]), X = I(c[3]), Y = I(c[4]), NameEn = c[5], NamePt = c[6] };
                        if (!_town.TryGetValue(t.Map, out var tl)) _town[t.Map] = tl = new List<TownSpot>();
                        tl.Add(t); break;
                    }
                }
            }
            EnsureNoviceFallbackLinks();
            ApplyPack();
            Debug.Log($"[Nav] navigation.tsv: {_maps.Count} maps, {_links.Count} links, {EdgeCount} link-to-link walks, {_npcs.Count} NPCs, {_mobs.Count} monster spawns, {_town.Count} town guides ({p})");
        }

        private static void EnsureNoviceFallbackLinks()
        {
            if (!_maps.ContainsKey("t_garden"))
                _maps["t_garden"] = new MapInfo { Map = "t_garden", NameEn = "Time Garden", NamePt = "Jardim do Tempo", Kind = 5001 };
            if (!_linksFrom.TryGetValue("iz_int", out var izInt) || !izInt.Exists(l => string.Equals(l.Dest, "int_land", StringComparison.OrdinalIgnoreCase)))
            {
                var l = new Link { Id = 20369, Map = "iz_int", X = 56, Y = 15, Type = 200, Class = 99999, Dest = "int_land", Dx = 85, Dy = 107, Name = "", Label = "ship_out" };
                _links[l.Id] = l;
                if (izInt == null) _linksFrom["iz_int"] = izInt = new List<Link>();
                izInt.Add(l);
                if (!_linksTo.TryGetValue("int_land", out var toIntLand)) _linksTo["int_land"] = toIntLand = new List<Link>();
                toIntLand.Add(l);
                if (_after.TryGetValue(15062, out var afterDeck) && !afterDeck.Exists(p => p.to == 20369)) afterDeck.Add((20369, 9));
                else if (!_after.ContainsKey(15062)) _after[15062] = new List<(int, int)> { (20369, 9) };
            }
            if (!_linksFrom.TryGetValue("int_land", out var intLand) || !intLand.Exists(l => string.Equals(l.Dest, "izlude", StringComparison.OrdinalIgnoreCase)))
            {
                var l = new Link { Id = 20372, Map = "int_land", X = 49, Y = 57, Type = 200, Class = 99999, Dest = "izlude", Dx = 196, Dy = 209, Name = "", Label = "intro_to_izlude" };
                _links[l.Id] = l;
                if (intLand == null) _linksFrom["int_land"] = intLand = new List<Link>();
                intLand.Add(l);
                if (!_linksTo.TryGetValue("izlude", out var toIzlude)) _linksTo["izlude"] = toIzlude = new List<Link>();
                toIzlude.Add(l);
                if (!_after.ContainsKey(20369)) _after[20369] = new List<(int, int)> { (20372, 53) };
            }
        }

        /// <summary>Wave 28 (wtextc): the text pack's navigation.tsv over the player's: "M map en pt", "N id name label", "L id name label",
        /// "B map monsterId name", "T map:type:x:y en pt" (a non-empty field wins; a key the player's table lacks is ignored, counted).</summary>
        private static void ApplyPack()
        {
            var t = Loki.Data.TextPack.Table("navigation.tsv");
            if (t == null) return;
            var P = (Func<string, string, string>)Loki.Data.TextPack.Pick;
            int applied = 0, unknown = 0; string first = null;
            var mobs = new Dictionary<string, List<Mob>>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in _mobs) { string k = m.Map + "\t" + m.Id; if (!mobs.TryGetValue(k, out var ml)) mobs[k] = ml = new List<Mob>(); ml.Add(m); }
            foreach (var c in t.Rows)
            {
                bool ok = false;
                int.TryParse(c[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id);
                switch (c[0])
                {
                    case "M": if (_maps.TryGetValue(c[1], out var mi)) { mi.NameEn = P(c[2], mi.NameEn); mi.NamePt = P(c[3], mi.NamePt); ok = true; } break;
                    case "N": if (_npcs.TryGetValue(id, out var n)) { n.Name = P(c[2], n.Name); n.Label = P(c[3], n.Label); ok = true; } break;
                    case "L": if (_links.TryGetValue(id, out var l)) { l.Name = P(c[2], l.Name); l.Label = P(c[3], l.Label); ok = true; } break;
                    case "B": if (mobs.TryGetValue(c[1] + "\t" + c[2], out var bl)) { foreach (var b in bl) b.Name = P(c[3], b.Name); ok = true; } break;
                    case "T": // the town guide: "map:type:x:y", English, Portuguese
                    {
                        var k = c[1].Split(':');
                        if (k.Length == 4 && _town.TryGetValue(k[0], out var tl))
                            foreach (var s in tl)
                                if (s.Type.ToString(CultureInfo.InvariantCulture) == k[1] && s.X.ToString(CultureInfo.InvariantCulture) == k[2] && s.Y.ToString(CultureInfo.InvariantCulture) == k[3])
                                { s.NameEn = P(c[2], s.NameEn); s.NamePt = P(c[3], s.NamePt); ok = true; }
                        break;
                    }
                }
                if (ok) applied++; else { unknown++; if (first == null) first = c[0] + " " + c[1]; }
            }
            Loki.Data.TextPack.Report(t, applied, unknown, first);
        }

        /// <summary>The install's town guide for a map (System\Towninfo.lub: storage / tool dealer / weapons / armour / smith / guide / inn).</summary>
        public static IReadOnlyList<TownSpot> TownSpots(string map) { Load(); string k = CanonicalMap(map); return k != null && _town.TryGetValue(k, out var l) ? l : (IReadOnlyList<TownSpot>)Array.Empty<TownSpot>(); }
        /// <summary>The town guide's kinds (roBrowser Navigation.js:799-836 names the icons: store, weapon, armor, smithy, guide, inn, kafra).</summary>
        public static string TownKind(int type) => type == 0 ? Lang.N("Tool dealer") : type == 1 ? Lang.N("Weapons") : type == 2 ? Lang.N("Armour") : type == 3 ? Lang.N("Smith") : type == 4 ? Lang.N("Guide") : type == 5 ? Lang.N("Inn") : type == 6 ? Lang.N("Kafra") : type == 7 ? Lang.N("Stylist") : Lang.N("Place"); // 7: the data's own English name says "Stylist"; w4lang: English (logs), Lang.T where shown (GuideWindow)
        /// <summary>The monsters the client's data lists on a map (Navi_Mob: its aegis name and level).</summary>
        public static IEnumerable<Mob> MobsOn(string map) { Load(); string k = CanonicalMap(map); foreach (var m in _mobs) if (string.Equals(m.Map, k, StringComparison.OrdinalIgnoreCase)) yield return m; }
        public static IEnumerable<Link> LinksOn(string map) { Load(); string k = CanonicalMap(map); return k != null && _linksFrom.TryGetValue(k, out var l) ? l : (IEnumerable<Link>)Array.Empty<Link>(); }
        public static Link LinkById(int id) { Load(); return _links.TryGetValue(id, out var l) ? l : null; }

        public static string Bare(string map) => map != null && map.EndsWith(".gat", StringComparison.OrdinalIgnoreCase) ? map.Substring(0, map.Length - 4) : map;

        /// <summary>Normalizes Renewal novice copy maps (iz_int01..04, int_land01..04, izlude_a..d, iz_ac01_a..d, iz_ac02_a..d, prt_fild08a..d, tra_fild01..03)
        /// to their canonical base map names used in navigation.tsv, &lt;NAVI&gt; links, and 0x08e2 packets.</summary>
        public static string CanonicalMap(string map)
        {
            string b = Bare(map);
            if (string.IsNullOrEmpty(b)) return b;
            string m = b.ToLowerInvariant();
            if (m.Length == 8 && m.StartsWith("iz_int0", StringComparison.Ordinal) && m[7] >= '1' && m[7] <= '4') return "iz_int";
            if (m.Length == 10 && m.StartsWith("int_land0", StringComparison.Ordinal) && m[9] >= '1' && m[9] <= '4') return "int_land";
            if (m.Length == 8 && m.StartsWith("izlude_", StringComparison.Ordinal) && m[7] >= 'a' && m[7] <= 'd') return "izlude";
            if (m.Length == 9 && m.StartsWith("iz_ac01_", StringComparison.Ordinal) && m[8] >= 'a' && m[8] <= 'd') return "iz_ac01";
            if (m.Length == 9 && m.StartsWith("iz_ac02_", StringComparison.Ordinal) && m[8] >= 'a' && m[8] <= 'd') return "iz_ac02";
            if (m.Length == 11 && m.StartsWith("prt_fild08", StringComparison.Ordinal) && m[10] >= 'a' && m[10] <= 'd') return "prt_fild08";
            if (m.Length == 10 && m.StartsWith("tra_fild0", StringComparison.Ordinal) && m[9] >= '1' && m[9] <= '3') return "tra_fild";
            return m;
        }

        // ------------------------------------------------------------------ the order and the plan
        public string TargetMap { get; private set; } public int TargetX { get; private set; } public int TargetY { get; private set; }
        public int MobId { get; private set; } public int Type { get; private set; } public int Flag { get; private set; } = 101;
        public bool HideWindow { get; private set; } = true;
        public bool Active => TargetMap != null;
        /// <summary>Where the arrow points on the current map: the target, or the next link on the route.</summary>
        public Vector2? Waypoint { get; private set; }
        public string WaypointMap { get; private set; }
        /// <summary>Links still to take (0 = the target is on this map).</summary>
        public int RouteHops { get; private set; }
        /// <summary>The planned links from this map on (the first is the next step).</summary>
        public IReadOnlyList<Link> Route => _route;
        private readonly List<Link> _route = new List<Link>();
        /// <summary>What to do next, one sentence (the Guide window shows it).</summary>
        public string Instruction { get; private set; } = "";
        public event Action Changed;
        /// <summary>A 0x08e2 asked for the navigation window (hideWindow 0).</summary>
        public event Action WindowRequested;
        public float Distance { get; private set; }
        public int Received { get; private set; }
        /// <summary>The unit the server shows near a step (LokiGame: a warp portal for a portal step, else an NPC), or null. The client's data
        /// places some portals a cell or two from where rAthena puts them (prontera's south gate: the data 156,25, rAthena prontera.txt:24
        /// 156,22 with a 3 × 2 touch area): the arrow then points at the portal the server really sent.</summary>
        public Func<Vector2, bool, Vector2?> Near;
        /// <summary>Where the step was moved to (smoke / log), or null.</summary>
        public Vector2? SnappedTo { get; private set; }
        private float _nextSnap;

        private readonly Func<string> _currentMap; private readonly Func<Unit> _self; private readonly Action<string, string> _chat;
        private GameObject _arrow; private readonly Transform _mapRoot;

        public Navigation(Func<string> currentMap, Func<Unit> self, Transform mapRoot, Action<string, string> chat)
        {
            _currentMap = currentMap; _self = self; _mapRoot = mapRoot; _chat = chat;
        }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            if (id != 0x08e2 || n < 27) return false;
            var r = new PacketReader(b, o, n); r.Skip(2); int type = r.U8(); int flag = r.U8(); int hide = r.U8(); string map = Bare(r.Str(16)); int x = r.U16(), y = r.U16(), mob = r.U16();
            Received++;
            Debug.Log($"[Nav] 0x08e2 navigate type {type} flag {flag} (airship {AllowsAirship(flag)}, scroll {AllowsScroll(flag)}, Kafra {AllowsKafra(flag)}) hide window {hide} → {map} ({x},{y}) monster {mob}");
            Go(type, map, x, y, mob, flag, hide != 0, true);
            if (hide == 0) WindowRequested?.Invoke();
            return true;
        }

        /// <summary>e_navigation_flag (script.hpp:724-733): the ones digit airship ("1-9"), the tens scroll ("11-99"), the hundreds Kafra.</summary>
        public static bool AllowsAirship(int flag) => flag % 10 != 0;
        public static bool AllowsScroll(int flag) => flag % 100 >= 10;
        public static bool AllowsKafra(int flag) => flag >= 100;

        /// <summary>A new order (the packet, the Guide window, /navi): plans and announces it.</summary>
        public void Go(int type, string map, int x, int y, int mob, int flag, bool hideWindow, bool announce)
        {
            Type = type; TargetMap = CanonicalMap(map); TargetX = x; TargetY = y; MobId = mob; Flag = flag; HideWindow = hideWindow;
            if (string.IsNullOrEmpty(TargetMap)) { TargetMap = null; return; }
            Recompute(announce);
        }

        public void Clear(string why)
        {
            if (!Active) return;
            Debug.Log("[Nav] cleared: " + why);
            TargetMap = null; Waypoint = null; WaypointMap = null; _route.Clear(); Instruction = ""; RouteHops = 0;
            if (_arrow != null) _arrow.SetActive(false);
            Changed?.Invoke();
        }

        /// <summary>After a map change or a new order: plans from where we are and names the next step.</summary>
        public void Recompute(bool announce)
        {
            if (!Active) return;
            Load();
            string rawHere = Bare(_currentMap())?.ToLowerInvariant();
            string here = CanonicalMap(rawHere);
            var self = _self();
            if (here == null) return;
            _route.Clear();
            var start = self != null ? self.Cell : new Vector2(0, 0);
            var targetCell = Type == 0 && TargetX > 0 && TargetY > 0 ? new Vector2(TargetX, TargetY) : (Vector2?)null;
            List<Link> intraRoute = null;
            if (here == TargetMap && targetCell.HasValue && self != null && NeedsIntraRoomRoute(here, start, targetCell.Value))
                intraRoute = Plan(here, start, TargetMap, targetCell, Flag);

            if (here == TargetMap && (intraRoute == null || intraRoute.Count == 0))
            {
                if (targetCell.HasValue) { Waypoint = targetCell.Value; WaypointMap = rawHere; RouteHops = 0; Instruction = Lang.F("Walk to ({0}, {1}).", TargetX, TargetY); }
                else
                {
                    string what = MobId > 0 ? Lang.F("You are on {0}: the monster #{1} is to be hunted here.", ClientText.MapName(here), MobId) : Lang.F("You are on {0}.", ClientText.MapName(here));
                    if (announce || Type != 0) _chat(what, "9ad1ff");
                    Clear("arrived on map"); return;
                }
            }
            else
            {
                var route = intraRoute ?? Plan(here, start, TargetMap, targetCell, Flag);
                if (route == null)
                {
                    if (announce) _chat(Lang.F("No known way from {0} to {1}.", ClientText.MapName(here), ClientText.MapName(TargetMap)), "ffb070");
                    Clear("no route"); return;
                }
                _route.AddRange(route); SnappedTo = null;
                var first = route[0];
                Waypoint = new Vector2(first.X, first.Y); WaypointMap = rawHere; RouteHops = route.Count;
                Instruction = StepText(first);
            }
            Debug.Log($"[Nav] plan from {here} to {TargetMap}{(Type == 0 ? $" ({TargetX},{TargetY})" : "")}: {(_route.Count == 0 ? "on this map" : string.Join(" → ", _route.ConvertAll(l => $"{l.Map}({l.X},{l.Y}) type {l.Type} #{l.Id}")))} then {TargetMap}");
            if (announce)
            {
                string target = Type == 0 && TargetX > 0 ? string.Format("{0} ({1}, {2})", ClientText.MapName(TargetMap), TargetX, TargetY) : ClientText.MapName(TargetMap);
                _chat(RouteHops > 1 ? Lang.F("Guide to {0}: {1} steps. {2}", target, RouteHops, Instruction)
                    : RouteHops == 1 ? Lang.F("Guide to {0}: one step. {1}", target, Instruction) : Lang.F("Guide to {0}: {1}", target, Instruction), "9ad1ff");
            }
            Changed?.Invoke();
        }

        /// <summary>What to do at a link: walk into a portal, or talk to the NPC there and choose the destination.</summary>
        public static string StepText(Link l)
        {
            string dest = ClientText.MapName(l.Dest);
            if (l.Type == 200) return Lang.F("Walk into the portal at ({0}, {1}) to {2}.", l.X, l.Y, dest);
            if (l.Type == 205) return Lang.F("Take the airship at ({0}, {1}) to {2}.", l.X, l.Y, dest);
            // w4lang: one whole sentence per kind of NPC (NpcKind stays English)
            string kind = NpcKind(l.Class, l.Type);
            if (kind == "Kafra employee") return Lang.F("Talk to the Kafra employee at ({0}, {1}) and travel to {2}.", l.X, l.Y, dest);
            if (kind == "service NPC") return Lang.F("Talk to the service NPC at ({0}, {1}) and travel to {2}.", l.X, l.Y, dest);
            return Lang.F("Talk to the NPC at ({0}, {1}) and travel to {2}.", l.X, l.Y, dest);
        }

        /// <summary>Who stands at an NPC link: the client's sprite name tells a Kafra employee (4_F_KAFRA…); the data's own names are text
        /// ids the install cannot read (7904 of 7947 NPC names).</summary>
        public static string NpcKind(int cls, int type)
        {
            string s = RoNames.MobName(cls) ?? "";
            if (s.IndexOf("KAFRA", StringComparison.OrdinalIgnoreCase) >= 0) return "Kafra employee";
            return type == 204 ? "service NPC" : "NPC";
        }

        /// <summary>For multi-room maps (iz_ac02 classrooms, iz_int ship rooms, prt_in, geffen_in, etc.), returns the incoming link IDs
        /// that land in the same room on `map` as `cell`, or null if `map` is a single open room.</summary>
        private static HashSet<int> RoomEntryLinks(string map, Vector2 cell)
        {
            if (map == null || (cell.x <= 0 && cell.y <= 0) || !_linksTo.TryGetValue(map, out var incoming) || incoming.Count <= 1) return null;
            if (_npcsOn.TryGetValue(map, out var npcs))
            {
                Npc bestNpc = null; float bestNpcD = 20f;
                foreach (var n in npcs)
                {
                    if (!_npcEntries.TryGetValue(n.Id, out var entries) || entries.Count == 0) continue;
                    float d = Vector2.Distance(cell, new Vector2(n.X, n.Y));
                    if (d <= bestNpcD) { bestNpcD = d; bestNpc = n; }
                }
                if (bestNpc != null)
                {
                    var set = new HashSet<int>();
                    foreach (var (lid, _) in _npcEntries[bestNpc.Id]) set.Add(lid);
                    return set.Count > 0 && set.Count < incoming.Count ? set : null;
                }
            }
            bool hasIntra = _linksFrom.TryGetValue(map, out var outgoing) && outgoing.Exists(l => string.Equals(l.Dest, map, StringComparison.OrdinalIgnoreCase));
            if (!hasIntra) return null;
            int nearestIn = 0; float bestInD = float.MaxValue;
            foreach (var l in incoming)
            {
                float d = Vector2.Distance(cell, new Vector2(l.Dx, l.Dy));
                if (d < bestInD) { bestInD = d; nearestIn = l.Id; }
            }
            int nearestOut = 0; float bestOutD = float.MaxValue;
            if (outgoing != null)
                foreach (var l in outgoing)
                {
                    float d = Vector2.Distance(cell, new Vector2(l.X, l.Y));
                    if (d < bestOutD) { bestOutD = d; nearestOut = l.Id; }
                }
            var room = new HashSet<int>();
            if (bestOutD < bestInD && nearestOut != 0)
            {
                foreach (var l in incoming)
                    if (_after.TryGetValue(l.Id, out var next) && next.Exists(p => p.to == nearestOut))
                        room.Add(l.Id);
            }
            if (room.Count == 0 && nearestIn != 0)
            {
                room.Add(nearestIn);
                if (_after.TryGetValue(nearestIn, out var myOut) && myOut.Count > 0)
                {
                    var myOutIds = new HashSet<int>();
                    foreach (var (to, _) in myOut) myOutIds.Add(to);
                    foreach (var l in incoming)
                        if (_after.TryGetValue(l.Id, out var otherOut))
                            foreach (var (to, _) in otherOut)
                                if (myOutIds.Contains(to)) { room.Add(l.Id); break; }
                }
            }
            return room.Count > 0 && room.Count < incoming.Count ? room : null;
        }

        /// <summary>For a player standing at `cell` on a map with intra-map portals, returns the outgoing link IDs in their current room,
        /// or null if all links on `map` are directly reachable.</summary>
        private static HashSet<int> RoomOutLinks(string map, Vector2 cell)
        {
            if (map == null || (cell.x <= 0 && cell.y <= 0) || !_linksFrom.TryGetValue(map, out var outgoing) || outgoing.Count <= 1) return null;
            if (!outgoing.Exists(l => string.Equals(l.Dest, map, StringComparison.OrdinalIgnoreCase))) return null;
            var outSet = new HashSet<int>();
            var inRoom = RoomEntryLinks(map, cell);
            if (inRoom != null)
            {
                foreach (int inId in inRoom)
                    if (_after.TryGetValue(inId, out var next))
                        foreach (var (to, _) in next)
                            if (_links.TryGetValue(to, out var nl) && string.Equals(nl.Map, map, StringComparison.OrdinalIgnoreCase))
                                outSet.Add(to);
            }
            if (outSet.Count == 0)
            {
                int nearestOut = 0; float bestD = float.MaxValue;
                foreach (var l in outgoing)
                {
                    float d = Vector2.Distance(cell, new Vector2(l.X, l.Y));
                    if (d < bestD) { bestD = d; nearestOut = l.Id; }
                }
                if (nearestOut != 0) outSet.Add(nearestOut);
            }
            return outSet.Count > 0 && outSet.Count < outgoing.Count ? outSet : null;
        }

        private static bool NeedsIntraRoomRoute(string map, Vector2 start, Vector2 target)
        {
            if (!_linksFrom.TryGetValue(map, out var outgoing) || !outgoing.Exists(l => string.Equals(l.Dest, map, StringComparison.OrdinalIgnoreCase))) return false;
            var targetRoom = RoomEntryLinks(map, target);
            if (targetRoom == null) return false;
            var startRoom = RoomEntryLinks(map, start);
            if (startRoom != null && startRoom.Overlaps(targetRoom)) return false;
            return true;
        }

        /// <summary>
        /// The search: nodes are links; from our cell to every allowed link leaving this map (straight line), then Navi_Distance's walks
        /// between a link's arrival and the next links, until a link arrives on the target map (+ the straight line to x/y). Cost: fewest
        /// links, then the shortest walk (roBrowser MapPathFinder.js:242-293). Null when the target cannot be reached.
        /// </summary>
        public static List<Link> Plan(string from, Vector2 cell, string to, Vector2? target, int flag)
        {
            Load();
            from = CanonicalMap(from);
            to = CanonicalMap(to);
            if (from == null || to == null || !_linksFrom.TryGetValue(from, out var starts)) return null;
            var startFilter = RoomOutLinks(from, cell);
            var targetRoom = target.HasValue ? RoomEntryLinks(to, target.Value) : null;
            var res = PlanCore(starts, startFilter, cell, to, target, targetRoom, flag);
            if (res == null && startFilter != null) res = PlanCore(starts, null, cell, to, target, targetRoom, flag);
            if (res == null && targetRoom != null) res = PlanCore(starts, null, cell, to, target, null, flag);
            return res;
        }

        private static List<Link> PlanCore(List<Link> starts, HashSet<int> startFilter, Vector2 cell, string to, Vector2? target, HashSet<int> targetRoom, int flag)
        {
            bool Allowed(Link l) => l.Type == 200 || l.Type == 201 || (l.Type == 204 && AllowsKafra(flag)) || (l.Type == 205 && AllowsAirship(flag));
            var best = new Dictionary<int, (int hops, float dist)>(); var prev = new Dictionary<int, int>();
            var open = new SortedSet<(int hops, float dist, int id)>();
            foreach (var l in starts)
            {
                if (!Allowed(l) || (startFilter != null && !startFilter.Contains(l.Id))) continue;
                float d = Vector2.Distance(cell, new Vector2(l.X, l.Y));
                var k = (1, d);
                if (!best.TryGetValue(l.Id, out var b) || Less(k, b)) { if (best.ContainsKey(l.Id)) open.Remove((b.hops, b.dist, l.Id)); best[l.Id] = k; prev[l.Id] = 0; open.Add((1, d, l.Id)); }
            }
            int bestEnd = 0; (int hops, float dist) bestEndCost = (int.MaxValue, float.MaxValue);
            while (open.Count > 0)
            {
                var cur = open.Min; open.Remove(cur);
                if (!Less((cur.hops, cur.dist), bestEndCost)) break;
                var l = _links[cur.id];
                if (string.Equals(l.Dest, to, StringComparison.OrdinalIgnoreCase) && (targetRoom == null || targetRoom.Contains(l.Id)))
                {
                    float last = target.HasValue ? Vector2.Distance(new Vector2(l.Dx, l.Dy), target.Value) : 0f;
                    var end = (cur.hops, cur.dist + last);
                    if (Less(end, bestEndCost)) { bestEndCost = end; bestEnd = l.Id; }
                    continue;
                }
                if (!_after.TryGetValue(l.Id, out var next)) continue;
                foreach (var (nid, walk) in next)
                {
                    if (!_links.TryGetValue(nid, out var nl) || !Allowed(nl)) continue;
                    var k = (cur.hops + 1, cur.dist + walk);
                    if (best.TryGetValue(nid, out var b) && !Less(k, b)) continue;
                    if (best.ContainsKey(nid)) open.Remove((b.hops, b.dist, nid));
                    best[nid] = k; prev[nid] = l.Id; open.Add((k.Item1, k.Item2, nid));
                }
            }
            if (bestEnd == 0) return null;
            var route = new List<Link>();
            for (int id = bestEnd; id != 0; id = prev[id]) route.Add(_links[id]);
            route.Reverse();
            return route;
        }
        private static bool Less((int hops, float dist) a, (int hops, float dist) b) => a.hops < b.hops || (a.hops == b.hops && a.dist < b.dist);

        /// <summary>Per frame: keeps the arrow over the player, pointing at the waypoint; clears on arrival.</summary>
        public void Update()
        {
            var self = _self();
            if (!Active || !Waypoint.HasValue || self == null || !string.Equals(CanonicalMap(WaypointMap), CanonicalMap(_currentMap()), StringComparison.OrdinalIgnoreCase)) { if (_arrow != null) _arrow.SetActive(false); return; }
            if (_route.Count > 0 && string.Equals(CanonicalMap(_route[0].Map), CanonicalMap(_route[0].Dest), StringComparison.OrdinalIgnoreCase))
            {
                var step = _route[0];
                if (Vector2.Distance(self.Cell, new Vector2(step.Dx, step.Dy)) <= 4f && Vector2.Distance(self.Cell, new Vector2(step.X, step.Y)) > 4f)
                {
                    Recompute(false);
                    if (!Active || !Waypoint.HasValue) return;
                }
            }
            if (_route.Count > 0 && Near != null && Time.time >= _nextSnap)
            {
                _nextSnap = Time.time + 0.5f;
                var l = _route[0];
                var p = Near(new Vector2(l.X, l.Y), l.Type == 200);
                if (p.HasValue && (Waypoint.Value - p.Value).sqrMagnitude > 0.01f)
                {
                    Waypoint = p; SnappedTo = p;
                    Debug.Log($"[Nav] step #{l.Id} ({l.X},{l.Y}): the {(l.Type == 200 ? "portal" : "NPC")} the server shows is at {p.Value}");
                    Changed?.Invoke();
                }
            }
            var d = Waypoint.Value - self.Cell;
            if (RouteHops == 0 && d.magnitude <= 3f) { _chat(Lang.T("You have arrived."), "9ad1ff"); Clear("arrived"); return; }
            if (_arrow == null) BuildArrow();
            _arrow.SetActive(true);
            // above the name plate, in the map's frame; the arrow object lives under the map root so the unit's billboard yaw does not turn it
            var pos = self.transform.localPosition + Vector3.up * (3.6f + Mathf.Sin(Time.time * 3f) * 0.15f);
            _arrow.transform.localPosition = pos;
            var dir = new Vector3(d.x, 0, d.y);
            if (dir.sqrMagnitude > 1e-4f) _arrow.transform.localRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            Distance = d.magnitude;
        }
        public GameObject ArrowObject => _arrow;

        private void BuildArrow()
        {
            _arrow = new GameObject("nav-arrow");
            _arrow.transform.SetParent(_mapRoot, false);
            var mat = new Material(Loki.World.Fx.Glow) { mainTexture = Texture2D.whiteTexture, color = new Color(0.35f, 1f, 1f, 0.95f) }; // the glow shader ships in Resources (Standard may be stripped from builds)
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cube); shaft.name = "shaft";
            shaft.transform.SetParent(_arrow.transform, false); shaft.transform.localScale = new Vector3(0.25f, 0.25f, 1.1f); shaft.transform.localPosition = new Vector3(0, 0, -0.2f);
            var head = GameObject.CreatePrimitive(PrimitiveType.Cube); head.name = "head";
            head.transform.SetParent(_arrow.transform, false); head.transform.localScale = new Vector3(0.55f, 0.25f, 0.55f); head.transform.localPosition = new Vector3(0, 0, 0.55f); head.transform.localRotation = Quaternion.Euler(0, 45, 0);
            foreach (var t in new[] { shaft, head }) { UnityEngine.Object.Destroy(t.GetComponent<Collider>()); t.GetComponent<MeshRenderer>().sharedMaterial = mat; t.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
        }

    }
}
