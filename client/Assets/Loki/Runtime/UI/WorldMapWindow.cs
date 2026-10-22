using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 3 (w3social): the world map. The client's own world-view data (data\luafiles514\lua files\worldviewdata\ in the
    /// GRF, turned into data/worldmap.tsv by client/tools/clientdata/build_worldmap.py): 7 worlds (Midgard, Midgard North, …), each a
    /// 1280 × 1024 picture in data\texture\유저인터페이스\ read from the GRF at run time, with the towns' and fields' tiles, every dungeon
    /// floor and the red dungeon markers in picture pixels. The window shows the picture with "You are here" on the tile (or the dungeon
    /// marker) of the current map, the party members' maps, the dungeon markers and names (the client's DungeonBox colour, 255,0,0 alpha
    /// 180, worldviewdata_info.lub) and, on request, each field's notable monster and level (the tables' labels). Pointing the laser at
    /// the picture names the tile under it; the trigger keeps that name on the info line. A panel in front of the head like the other
    /// windows (0.00046 m per pixel: the picture is 59 × 47 cm), readable at table distance.
    /// </summary>
    public sealed class WorldMapWindow : MonoBehaviour
    {
        public sealed class World { public int Index; public string Image = "", NameEn = "", NamePt = ""; public readonly List<Place> Places = new List<Place>(); public readonly List<Dungeon> Dungeons = new List<Dungeon>(); public string Name => Game.ClientText.Portuguese && NamePt.Length > 0 ? NamePt : NameEn; }
        /// <summary>A map row: group &lt; 100 = a dungeon floor (Rect = its dungeon's name box), else a town / field tile.</summary>
        public sealed class Place
        {
            public int World, Group; public string Map = ""; public RectInt Rect; public string LabelEn = "", LabelPt = "", Level = "";
            public bool IsDungeonFloor => Group < 100;
            public string Label => Game.ClientText.Portuguese && LabelPt.Length > 0 ? LabelPt : LabelEn;
        }
        public sealed class Dungeon { public int World, Group; public RectInt Rect; public string NameEn = "", NamePt = "", Levels = ""; public string Name => Game.ClientText.Portuguese && NamePt.Length > 0 ? NamePt : NameEn; }

        private static List<World> _worlds;
        private static readonly Dictionary<string, string> _colors = new Dictionary<string, string>();
        public static IReadOnlyList<World> Worlds { get { Load(); return _worlds; } }

        /// <summary>Wave 28 (wtextc): the text pack's worldmap.tsv over the player's: "W world - en pt", "M world map en pt" (a map is
        /// listed once per world), "D world group en pt"; a non-empty field wins, a key the player's table lacks is ignored (counted).</summary>
        private static void ApplyPack()
        {
            var t = Loki.Data.TextPack.Table("worldmap.tsv");
            if (t == null) return;
            int applied = 0, unknown = 0; string first = null;
            foreach (var c in t.Rows)
            {
                bool ok = false;
                if (int.TryParse(c[1], out int wi) && wi >= 0 && wi < _worlds.Count)
                {
                    var w = _worlds[wi];
                    switch (c[0])
                    {
                        case "W": w.NameEn = Loki.Data.TextPack.Pick(c[3], w.NameEn); w.NamePt = Loki.Data.TextPack.Pick(c[4], w.NamePt); ok = true; break;
                        case "M": foreach (var pl in w.Places) if (pl.Map == c[2].ToLowerInvariant()) { pl.LabelEn = Loki.Data.TextPack.Pick(c[3], pl.LabelEn); pl.LabelPt = Loki.Data.TextPack.Pick(c[4], pl.LabelPt); ok = true; } break;
                        case "D": foreach (var d in w.Dungeons) if (d.Group.ToString() == c[2]) { d.NameEn = Loki.Data.TextPack.Pick(c[3], d.NameEn); d.NamePt = Loki.Data.TextPack.Pick(c[4], d.NamePt); ok = true; } break;
                    }
                }
                if (ok) applied++; else { unknown++; if (first == null) first = string.Join(" ", c[0], c[1], c[2]); }
            }
            Loki.Data.TextPack.Report(t, applied, unknown, first);
        }

        /// <summary>Wave 28: every world map text in the game's language (the no-Portuguese check reads them).</summary>
        public static IEnumerable<string> AllTexts()
        {
            Load();
            foreach (var w in _worlds) { yield return w.Name; foreach (var pl in w.Places) yield return pl.Label; foreach (var d in w.Dungeons) yield return d.Name; }
        }

        private static void Load()
        {
            if (_worlds != null) return;
            _worlds = new List<World>();
            string p = Loki.Data.ClientData.File("worldmap.tsv");
            if (!File.Exists(p)) { Debug.LogWarning("[WorldMap] missing " + p + " (client/tools/clientdata/build_worldmap.py)"); return; }
            World W(int i) { while (_worlds.Count <= i) _worlds.Add(new World { Index = _worlds.Count }); return _worlds[i]; }
            int I(string s) => int.TryParse(s, out int v) ? v : 0;
            foreach (var line in File.ReadLines(p, System.Text.Encoding.UTF8))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var c = line.Split('\t');
                switch (c[0])
                {
                    case "W" when c.Length >= 5: { var w = W(I(c[1])); w.Image = c[2]; w.NameEn = c[3]; w.NamePt = c[4]; break; }
                    case "M" when c.Length >= 11:
                    {
                        var pl = new Place { World = I(c[1]), Group = I(c[2]), Map = c[3].ToLowerInvariant(), LabelEn = c[8], LabelPt = c[9], Level = c[10] };
                        pl.Rect = new RectInt(I(c[4]), I(c[5]), I(c[6]) - I(c[4]), I(c[7]) - I(c[5]));
                        W(pl.World).Places.Add(pl); break;
                    }
                    case "D" when c.Length >= 10:
                    {
                        var d = new Dungeon { World = I(c[1]), Group = I(c[2]), NameEn = c[7], NamePt = c[8], Levels = c[9] };
                        d.Rect = new RectInt(I(c[3]), I(c[4]), I(c[5]) - I(c[3]), I(c[6]) - I(c[4]));
                        W(d.World).Dungeons.Add(d); break;
                    }
                    case "I" when c.Length >= 3: _colors[c[1]] = c[2]; break;
                }
            }
            ApplyPack();
            int places = 0; foreach (var w in _worlds) places += w.Places.Count;
            Debug.Log($"[WorldMap] {_worlds.Count} worlds, {places} map rows");
        }

        /// <summary>Where a map is on the world map: its row (the first one: a map is listed once per world) or null.</summary>
        public static Place Find(string map)
        {
            Load();
            if (string.IsNullOrEmpty(map)) return null;
            string k = map.ToLowerInvariant(); int dot = k.IndexOf('.'); if (dot > 0) k = k.Substring(0, dot);
            foreach (var w in _worlds) foreach (var pl in w.Places) if (pl.Map == k) return pl;
            return null;
        }
        /// <summary>The picture rectangle that stands for a map: a town / field tile, or its dungeon's red marker.</summary>
        public static RectInt? SpotOf(Place pl)
        {
            if (pl == null) return null;
            if (!pl.IsDungeonFloor) return pl.Rect;
            foreach (var d in _worlds[pl.World].Dungeons) if (d.Group == pl.Group) return d.Rect;
            return pl.Rect;
        }

        // ---------------------------------------------------------------- the window
        private const float W = 1340, H = 1320, IW = 1280, IH = 1024, M = 0.00046f;
        /// <summary>Client wave 13 (w13look), item 24: the look's eye on the picture's bottom left corner, 4 px inside it (the picture reaches
        /// 1.3 cm from the rim: no free place under it): 90 px (4.1 cm: 2.6° from its 0.91 m), 34 px (1.6 cm) inside the left rim and 60 px
        /// (2.8 cm) over the bottom one; it is chosen before the picture under it (its priority).</summary>
        public const float EyePx = 90f, EyeRimPx = 34f;
        public LookEye Eye { get; private set; }
        private const float ImageY = 420 - IH / 2; // the picture's centre: its top edge 420 px above the panel's centre
        private RectTransform _rt, _img, _overlay; private RawImage _raw;
        private Text _title, _info; private UiButton _x, _back;
        private readonly List<UiButton> _tabs = new List<UiButton>();
        private UiButton _dunBtn, _mobBtn, _partyBtn, _hereBtn;
        private bool _showDungeons = true, _showMonsters, _showParty = true;
        private int _world = -1; private string _here = ""; private Place _selected, _hover;
        private Camera _head; private System.Func<Vector3?> _pointed;
        /// <summary>The party's members away from us: (name, map, online).</summary>
        public System.Func<IEnumerable<(string name, string map, bool online)>> PartyMaps;
        private readonly List<GameObject> _marks = new List<GameObject>();
        private RectTransform _youRing, _hoverBox;
        private Text _youLabel;
        public bool Visible => gameObject.activeSelf;
        public int CurrentWorld => _world;
        /// <summary>Smoke: the info line's text.</summary>
        public string InfoText => _info != null ? _info.text : "";
        /// <summary>Smoke: whether the "You are here" marker is shown, and where (picture pixels, top-left origin).</summary>
        public (bool shown, Vector2 at) You => (_youRing != null && _youRing.gameObject.activeSelf, _youAt);
        private Vector2 _youAt;
        /// <summary>Smoke: the party markers drawn (name → picture pixel).</summary>
        public readonly List<(string name, Vector2 at)> PartyShown = new List<(string, Vector2)>();

        public static WorldMapWindow Create(Transform parent, Camera head, System.Func<Vector3?> pointedWorld)
        {
            var rt = UiKit.Panel("WorldMapWindow", parent, new Vector2(W, H), M);
            var w = rt.gameObject.AddComponent<WorldMapWindow>();
            w._rt = rt; w._head = head; w._pointed = pointedWorld;
            w.Build();
            // client wave 13 (w13items), item 11: it can be the player's menu (UI/MenuNav.cs)
            MenuNav.Register(new MenuNav.Menu { Name = "WorldMapWindow", Panel = rt, IsOpen = () => w != null && w.Visible, Close = w.Hide,
                Capture = () => new MenuNav.Pose { Position = w.transform.position, Rotation = w.transform.rotation },
                Reopen = (o, st) => { o?.InvokeAction(); if (!w.Visible) w.Open(); if (st is MenuNav.Pose p) w.transform.SetPositionAndRotation(p.Position, p.Rotation); },
                Arrow = on => { if (w != null && w._back.gameObject.activeSelf != on) w._back.gameObject.SetActive(on); } });
            rt.gameObject.SetActive(false);
            return w;
        }

        private void Build()
        {
            float top = H / 2;
            _title = UiKit.TitlePlate(_rt, Lang.T("World map"), 36, W - 220).Text; // client wave 21 (w21kit): the title on the gold plate on the frame's top edge (the same 36 px letters)
            _x = UiKit.RoundButton(_rt, "X", new Vector2(W / 2 - 50, top - 40), new Vector2(72, 60), () => { if (!MenuNav.CloseButton(transform)) Hide(); }, 32);
            _back = MenuNav.MakeArrow(_rt, new Vector2(W, H), new Vector2(184, 176)); // client wave 13, item 11: 8.5 × 8.1 cm, 5.3° from its 0.88 m
            Load();
            int n = Mathf.Max(1, _worlds.Count); float tw = (W - 40 - (n - 1) * 8) / n;
            for (int i = 0; i < _worlds.Count; i++)
            {
                int wi = i;
                _tabs.Add(UiKit.Button(_rt, _worlds[i].Name, new Vector2(-W / 2 + 20 + tw / 2 + i * (tw + 8), top - 104), new Vector2(tw, 54), () => ShowWorld(wi), 19));
            }
            float bw = 230;
            _hereBtn = UiKit.Button(_rt, Lang.T("Where am I"), new Vector2(-W / 2 + 20 + bw / 2, top - 166), new Vector2(bw, 52), () => { var pl = Find(_here); if (pl != null) ShowWorld(pl.World); }, 22);
            _dunBtn = UiKit.Button(_rt, Lang.T("Dungeons"), new Vector2(-W / 2 + 20 + bw * 1.5f + 10, top - 166), new Vector2(bw, 52), () => { _showDungeons = !_showDungeons; Redraw(); }, 22);
            _mobBtn = UiKit.Button(_rt, Lang.T("Monsters"), new Vector2(-W / 2 + 20 + bw * 2.5f + 20, top - 166), new Vector2(bw, 52), () => { _showMonsters = !_showMonsters; Redraw(); }, 22);
            _partyBtn = UiKit.Button(_rt, Lang.T("Party"), new Vector2(-W / 2 + 20 + bw * 3.5f + 30, top - 166), new Vector2(bw, 52), () => { _showParty = !_showParty; Redraw(); }, 22);
            _info = UiKit.Label(_rt, "", new Vector2(0, top - 214), new Vector2(W - 60, 44), 24, TextAnchor.MiddleLeft, UiKit.TextColor);
            // the picture is a button: the trigger on it keeps the name of the tile under the laser on the info line
            var imgBtn = UiKit.Button(_rt, "", new Vector2(0, ImageY), new Vector2(IW, IH), () => { _selected = _hover; ShowInfo(); }, 20);
            imgBtn.Normal = new Color(0, 0, 0, 0); imgBtn.Hover = new Color(0, 0, 0, 0); imgBtn.SetHover(false);
            _img = (RectTransform)imgBtn.transform;
            var bg = imgBtn.GetComponent<Image>(); bg.color = new Color(0, 0, 0, 0); // the RawImage draws the picture
            var rawGo = new GameObject("picture", typeof(RectTransform)); rawGo.transform.SetParent(_img, false);
            var rr = (RectTransform)rawGo.transform; rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.5f); rr.sizeDelta = new Vector2(IW, IH); rr.anchoredPosition = Vector2.zero;
            _raw = rawGo.AddComponent<RawImage>(); _raw.raycastTarget = false; if (UiKit.Overlay != null) _raw.material = UiKit.Overlay;
            _overlay = UiKit.OwnColours(UiKit.Box(_img, "overlay", Vector2.zero, new Vector2(IW, IH), new Color(0, 0, 0, 0))); // client wave 21 (w21kit): the marks and names on the map's picture keep their own colours (light letters with a dark edge), not the canvas' ink
            _hoverBox = MakeOutline(_overlay, new Color(1f, 1f, 1f, 0.9f), 3);
            _youRing = UiKit.Box(_overlay, "you", Vector2.zero, new Vector2(34, 34), new Color(1f, 0.85f, 0.2f, 0.95f));
            _youRing.localRotation = Quaternion.Euler(0, 0, 45);
            UiKit.Box(_youRing, "you-core", Vector2.zero, new Vector2(18, 18), new Color(0.2f, 0.1f, 0f, 0.95f));
            _youLabel = UiKit.Label(_overlay, Lang.T("You"), Vector2.zero, new Vector2(220, 36), 26, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.4f));
            _youLabel.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.95f);
            Eye = LookEye.Add(_rt, () => "WorldMapWindow", new Vector2(-IW / 2 + 4f + EyePx / 2, ImageY - IH / 2 + 4f + EyePx / 2), EyePx); // client wave 13 (w13look), item 24
        }

        /// <summary>An empty rectangle of four thin boxes (a highlight), hidden until placed.</summary>
        private static RectTransform MakeOutline(RectTransform parent, Color c, float t)
        {
            var root = UiKit.Box(parent, "outline", Vector2.zero, new Vector2(10, 10), new Color(0, 0, 0, 0));
            for (int i = 0; i < 4; i++) UiKit.Box(root, "edge" + i, Vector2.zero, new Vector2(t, t), c);
            root.gameObject.SetActive(false);
            return root;
        }
        private static void PlaceOutline(RectTransform o, RectInt r, float t = 3)
        {
            var c = PicToLocal(new Vector2(r.x + r.width / 2f, r.y + r.height / 2f));
            o.anchoredPosition = c; o.sizeDelta = new Vector2(r.width, r.height);
            var e0 = (RectTransform)o.GetChild(0); e0.anchoredPosition = new Vector2(0, r.height / 2f); e0.sizeDelta = new Vector2(r.width + t, t);
            var e1 = (RectTransform)o.GetChild(1); e1.anchoredPosition = new Vector2(0, -r.height / 2f); e1.sizeDelta = new Vector2(r.width + t, t);
            var e2 = (RectTransform)o.GetChild(2); e2.anchoredPosition = new Vector2(-r.width / 2f, 0); e2.sizeDelta = new Vector2(t, r.height + t);
            var e3 = (RectTransform)o.GetChild(3); e3.anchoredPosition = new Vector2(r.width / 2f, 0); e3.sizeDelta = new Vector2(t, r.height + t);
            o.gameObject.SetActive(true);
        }

        /// <summary>Picture pixels (x right, y down from the top-left, as the tables give them) → overlay local (origin centre, y up).</summary>
        private static Vector2 PicToLocal(Vector2 p) => new Vector2(p.x - IW / 2, IH / 2 - p.y);

        private Vector3 _spawnPos;
        private Quaternion _spawnRot;

        /// <summary>Opens in front of the head, on the world of the current map.</summary>
        public void Open(Camera head = null)
        {
            if (head != null) _head = head;
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
                if (_head != null)
                {
                    if (!PanelPlacing.TryPlaceMenu("WorldMapWindow", transform, _head, out _))
                    {
                        var f = _head.transform.forward; f.y = 0; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
                        transform.position = _head.transform.position + f * 0.8f + Vector3.up * -0.18f; // a little below the eyes: the 61 cm tall panel reads without craning
                        UiKit.FaceHead(transform, _head);
                    }
                }
                _spawnPos = transform.position;
                _spawnRot = transform.rotation;
            }
            var pl = Find(_here);
            _selected = null; _hover = null; if (_hoverBox != null) _hoverBox.gameObject.SetActive(false);
            ShowWorld(pl != null ? pl.World : Mathf.Max(0, _world));
            Debug.Log($"[WorldMap] open on world {_world} ({(_world >= 0 && _world < _worlds.Count ? _worlds[_world].NameEn : "?")}), here '{_here}' {(pl != null ? $"at {SpotOf(pl)}" : "(not on the world map)")}");
        }
        public void Hide()
        {
            if (gameObject.activeSelf)
            {
                if ((transform.position - _spawnPos).sqrMagnitude > 1e-5f || Quaternion.Angle(transform.rotation, _spawnRot) > 0.5f)
                {
                    var cam = _head != null ? _head : Camera.main;
                    if (cam != null) PanelPlacing.SaveMenuPlace("WorldMapWindow", transform, cam);
                }
                gameObject.SetActive(false);
            }
        }
        public void Toggle() { if (Visible) Hide(); else Open(); }
        /// <summary>The party changed (members, maps): redraw the markers if the window is open.</summary>
        public void Refresh() { if (Visible) Redraw(); }

        /// <summary>The map we are on (EnterMap).</summary>
        public void SetHere(string map) { _here = (map ?? "").ToLowerInvariant(); if (Visible) Redraw(); }

        public void ShowWorld(int i)
        {
            Load();
            if (_worlds.Count == 0) { _info.text = Lang.T("No world map data (data/worldmap.tsv is missing)."); return; }
            _world = Mathf.Clamp(i, 0, _worlds.Count - 1);
            var w = _worlds[_world];
            var assets = LokiBootstrap.Instance != null ? LokiBootstrap.Instance.Assets : null;
            Texture2D tex = null;
            if (assets != null) { tex = assets.Texture("À¯ÀúÀÎÅÍÆäÀÌ½º\\" + w.Image, true); if (tex == assets.MissingTexture) tex = null; } // 유저인터페이스\<image>
            _raw.texture = tex; _raw.color = tex != null ? Color.white : new Color(0.15f, 0.17f, 0.22f, 1f);
            for (int t = 0; t < _tabs.Count; t++) { _tabs[t].Normal = t == _world ? new Color(0.45f, 0.35f, 0.15f) : UiKit.ButtonBg; _tabs[t].SetHover(false); }
            _title.text = tex == null ? Lang.F("World map  <color=#ffd060>{0}</color>  <color=#ff8a7a>(picture missing from the game data)</color>", w.Name) : Lang.F("World map  <color=#ffd060>{0}</color>", w.Name);
            Redraw();
        }

        private void Redraw()
        {
            foreach (var g in _marks) if (g != null) Destroy(g);
            _marks.Clear(); PartyShown.Clear();
            if (_world < 0 || _world >= _worlds.Count) return;
            var w = _worlds[_world];
            SetToggle(_dunBtn, _showDungeons); SetToggle(_mobBtn, _showMonsters); SetToggle(_partyBtn, _showParty);
            var dunColor = new Color(Info("DungeonBox_ColorR", 255) / 255f, Info("DungeonBox_ColorG", 0) / 255f, Info("DungeonBox_ColorB", 0) / 255f, Info("DungeonBox_Alpha", 180) / 255f);
            if (_showDungeons)
                foreach (var d in w.Dungeons)
                {
                    var c = PicToLocal(new Vector2(d.Rect.x + d.Rect.width / 2f, d.Rect.y + d.Rect.height / 2f));
                    Mark(UiKit.Box(_overlay, "dun", c, new Vector2(Mathf.Max(d.Rect.width, 14), Mathf.Max(d.Rect.height, 14)), dunColor).gameObject);
                    var t = UiKit.Label(_overlay, d.Name, c + new Vector2(0, -26), new Vector2(260, 28), 18, TextAnchor.MiddleCenter, new Color(0.95f, 0.95f, 0.95f));
                    t.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.95f); Mark(t.gameObject);
                }
            if (_showMonsters)
                foreach (var pl in w.Places)
                {
                    if (pl.IsDungeonFloor || pl.Level.Length == 0) continue; // towns have no level: their label is the town's name
                    var c = PicToLocal(new Vector2(pl.Rect.x + pl.Rect.width / 2f, pl.Rect.y + pl.Rect.height / 2f));
                    var t = UiKit.Label(_overlay, pl.Label + "\n<color=#ffffff>" + Lang.F("Lv {0}", pl.Level) + "</color>", c, new Vector2(Mathf.Max(pl.Rect.width, 60), 44), 15, TextAnchor.MiddleCenter, new Color(0.5f, 1f, 0.5f));
                    t.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.95f); Mark(t.gameObject);
                }
            // you are here
            var here = Find(_here); var spot = here != null && here.World == _world ? SpotOf(here) : null;
            _youRing.gameObject.SetActive(spot.HasValue); _youLabel.gameObject.SetActive(spot.HasValue);
            if (spot.HasValue)
            {
                var r = spot.Value; _youAt = new Vector2(r.x + r.width / 2f, r.y + r.height / 2f);
                var c = PicToLocal(_youAt);
                _youRing.anchoredPosition = c; _youLabel.rectTransform.anchoredPosition = c + new Vector2(0, 32);
                _youRing.SetAsLastSibling(); _youLabel.transform.SetAsLastSibling();
            }
            // the party's members, on their maps' spots (a small offset each so two on one map stay apart)
            if (_showParty && PartyMaps != null)
            {
                var perSpot = new Dictionary<Vector2, int>();
                foreach (var (name, map, online) in PartyMaps())
                {
                    var pl = Find(map); if (pl == null || pl.World != _world) continue;
                    var s = SpotOf(pl).Value; var at = new Vector2(s.x + s.width / 2f, s.y + s.height / 2f);
                    perSpot.TryGetValue(at, out int k); perSpot[at] = k + 1;
                    var c = PicToLocal(at) + new Vector2(22 + 6 * k, -18 - 26 * k);
                    Mark(UiKit.Box(_overlay, "member", c, new Vector2(18, 18), online ? new Color(1f, 0.45f, 0.95f) : new Color(0.5f, 0.45f, 0.55f)).gameObject);
                    var t = UiKit.Label(_overlay, name, c + new Vector2(80, 0), new Vector2(140, 28), 20, TextAnchor.MiddleLeft, online ? new Color(1f, 0.7f, 0.97f) : new Color(0.7f, 0.7f, 0.75f));
                    t.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.95f); Mark(t.gameObject);
                    PartyShown.Add((name, at));
                }
            }
            ShowInfo();
        }

        private void Mark(GameObject g) { _marks.Add(g); }
        private static void SetToggle(UiButton b, bool on) { if (b == null) return; b.Normal = on ? new Color(0.45f, 0.35f, 0.15f) : UiKit.ButtonBg; b.SetHover(false); }
        private static float Info(string k, float dflt) => _colors.TryGetValue(k, out var s) && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : dflt;

        /// <summary>The dungeon or the town / field tile under a picture pixel on the current world.</summary>
        public Place PlaceAt(Vector2 pic)
        {
            if (_world < 0 || _world >= _worlds.Count) return null;
            var w = _worlds[_world];
            // the dungeon markers first: they are small and sit on top of the town / field tiles (Payon Dungeon's is inside Archer Village's)
            foreach (var d in w.Dungeons)
            {
                var r = new RectInt(d.Rect.x - 6, d.Rect.y - 6, d.Rect.width + 12, d.Rect.height + 12); // the markers are 20 px: a little slack for the laser
                if (r.Contains(Vector2Int.FloorToInt(pic))) foreach (var pl in w.Places) if (pl.IsDungeonFloor && pl.Group == d.Group) return pl;
            }
            foreach (var pl in w.Places) if (!pl.IsDungeonFloor && pl.Rect.Contains(Vector2Int.FloorToInt(pic))) return pl;
            return null;
        }

        private string Describe(Place pl)
        {
            if (pl == null) return "";
            var w = _worlds[pl.World];
            if (pl.IsDungeonFloor)
            {
                Dungeon d = null; foreach (var x in w.Dungeons) if (x.Group == pl.Group) d = x;
                var floors = new List<string>(); foreach (var x in w.Places) if (x.IsDungeonFloor && x.Group == pl.Group) floors.Add(x.Label);
                string lv = d != null && d.Levels.Length > 0 ? "  <color=#9aa3b5>" + Lang.F("Lv {0}", d.Levels) + "</color>" : ""; // w4lang: "1 floor" / "{0} floors" whole
                return $"<color=#ff9a8a>{(d != null ? d.Name : pl.Label)}</color>" + lv + "  ·  " + (floors.Count == 1 ? Lang.F("1 floor: {0}", string.Join(", ", floors)) : Lang.F("{0} floors: {1}", floors.Count, string.Join(", ", floors)));
            }
            string name = Game.ClientText.MapName(pl.Map);
            return $"<color=#ffd060>{name}</color>  <color=#9aa3b5>({pl.Map})</color>" + (pl.Level.Length > 0 ? $"  ·  {pl.Label}  <color=#9aa3b5>" + Lang.F("Lv {0}", pl.Level) + "</color>" : "");
        }

        private void ShowInfo()
        {
            var pl = _hover ?? _selected;
            if (pl != null) { _info.text = Describe(pl); return; }
            var here = Find(_here);
            _info.text = here == null ? Lang.F("<color=#9aa3b5>You are on {0} ({1}), which the world map doesn't show.</color>", Game.ClientText.MapName(_here), _here)
                       : here.World != _world ? Lang.F("<color=#9aa3b5>You are on {0}: see “{1}”.</color>", Game.ClientText.MapName(_here), _worlds[here.World].Name)
                       : Lang.F("You are on {0}", Describe(here));
        }

        private float _pulse;
        private void LateUpdate()
        {
            if (_head != null && (transform.position - _head.transform.position).sqrMagnitude > 16f) Hide(); // left behind (a teleport of the rig)
            _pulse += Time.deltaTime;
            if (_youRing != null && _youRing.gameObject.activeSelf) { float s = 1f + 0.18f * Mathf.Sin(_pulse * 5f); _youRing.localScale = new Vector3(s, s, 1f); }
            // the tile under the laser: the pointer's hit on the picture's plane
            Place hover = null;
            var pw = _pointed != null ? _pointed() : null;
            if (pw.HasValue && _img != null)
            {
                Vector3 lp = _img.InverseTransformPoint(pw.Value);
                if (Mathf.Abs(lp.z) < 20f && Mathf.Abs(lp.x) <= IW / 2 && Mathf.Abs(lp.y) <= IH / 2) hover = PlaceAt(new Vector2(lp.x + IW / 2, IH / 2 - lp.y));
            }
            if (hover != _hover)
            {
                _hover = hover;
                if (hover != null) { var s = hover.IsDungeonFloor ? SpotOf(hover).Value : hover.Rect; PlaceOutline(_hoverBox, s); _hoverBox.SetAsLastSibling(); } else _hoverBox.gameObject.SetActive(false);
                ShowInfo();
            }
        }

    }
}
