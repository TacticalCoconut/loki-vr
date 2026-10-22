using System;
using System.Collections.Generic;
using System.Linq;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;
using Navigation = Loki.Game.Navigation;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 4 (w4world): the navigation window. What the guide says now (the order from 0x08e2 or the player's own choice) with
    /// "Walk there" (the next step, walked as a laser click would be: w3walk's legs) and "Stop"; whether the route may use Kafra and airship
    /// services (e_navigation_flag); this town's guide (the install's System\Towninfo.lub: storage, tool dealer, weapons, armour, smith,
    /// guide, inn — one button each, the guide takes you there); and a map search over the client's own navigation map list (Navi_Map)
    /// and map names (mapnames.tsv), typed on the VR keyboard. Opened from the wrist strip's "Guide" button, by /navi, and by a 0x08e2
    /// whose hideWindow is 0. A panel in front of the head like the world map.
    /// </summary>
    public sealed class GuideWindow : MonoBehaviour
    {
        // client wave 13 (w13look), item 24: 66 px taller at its bottom (900 before) for the look's eye between "<" and the results' line
        // (everything else is placed from the top, or from the bottom as before): 72 px (4.0 cm: 2.65° from its 0.86 m), 28 px (1.5 cm) over the rim
        private const float W = 760, H = 966, M = 0.00055f;
        public const float EyePx = 72f, EyeRimPx = 28f;
        /// <summary>Client wave 13 (w13look), item 24: the look's eye (UI/LookSelect.cs).</summary>
        public LookEye Eye { get; private set; }
        private RectTransform _rt; private Camera _head; private Navigation _nav; private Func<string> _map;
        private Text _status, _town, _results; private UiButton _walk, _stop, _services, _prev, _next;
        private VrTextField _search; private UiButton _back;
        private readonly List<UiButton> _townBtns = new List<UiButton>(), _resultBtns = new List<UiButton>();
        private List<(string map, string name)> _found = new List<(string, string)>(); private int _page, _townPage; private UiButton _townMore;
        private bool _kafraAirship = true;
        /// <summary>Walk to a cell of this map (LokiGame: the w3walk trip), and the keyboard.</summary>
        public Action<Vector2Int> WalkTo; public Action<VrTextField> RequestKeyboard;
        public bool Visible => gameObject.activeSelf;
        public string StatusText => _status != null ? _status.text : "";
        public IReadOnlyList<(string map, string name)> Found => _found;
        public int TownButtons => _townBtns.Count(b => b.gameObject.activeSelf);

        public static GuideWindow Create(Transform parent, Camera head, Navigation nav, Func<string> map)
        {
            var rt = UiKit.Panel("GuideWindow", parent, new Vector2(W, H), M);
            var g = rt.gameObject.AddComponent<GuideWindow>();
            g._rt = rt; g._head = head; g._nav = nav; g._map = map;
            g.Build();
            nav.Changed += g.Refresh;
            // client wave 13 (w13items), item 11: it can be the player's menu (UI/MenuNav.cs)
            MenuNav.Register(new MenuNav.Menu { Name = "GuideWindow", Panel = rt, IsOpen = () => g != null && g.Visible, Close = g.Hide,
                Capture = () => new MenuNav.Pose { Position = g.transform.position, Rotation = g.transform.rotation },
                Reopen = (o, st) => { o?.InvokeAction(); if (!g.Visible) g.Open(); if (st is MenuNav.Pose p) g.transform.SetPositionAndRotation(p.Position, p.Rotation); },
                Arrow = on => { if (g != null && g._back.gameObject.activeSelf != on) g._back.gameObject.SetActive(on); } });
            rt.gameObject.SetActive(false);
            return g;
        }

        private void Build()
        {
            float top = H / 2;
            UiKit.TitlePlate(_rt, Lang.T("Guide"), 36, W - 220); // client wave 21 (w21kit): the title on the gold plate on the frame's top edge (the same 36 px letters)
            UiKit.RoundButton(_rt, "X", new Vector2(W / 2 - 50, top - 40), new Vector2(72, 60), () => { if (!MenuNav.CloseButton(transform)) Hide(); }, 32);
            _back = MenuNav.MakeArrow(_rt, new Vector2(W, H), new Vector2(152, 144)); // client wave 13, item 11: 8.4 × 7.9 cm, 5.4° from its 0.84 m
            _status = UiKit.Label(_rt, "", new Vector2(0, top - 130), new Vector2(W - 60, 110), 24, TextAnchor.UpperLeft, UiKit.TextColor);
            float bw = (W - 80) / 3f;
            _walk = UiKit.Button(_rt, Lang.T("Walk there"), new Vector2(-bw - 10, top - 225), new Vector2(bw, 56), () => { if (_nav.Waypoint.HasValue) WalkTo?.Invoke(Vector2Int.RoundToInt(_nav.Waypoint.Value)); }, 22);
            _stop = UiKit.Button(_rt, Lang.T("Stop the guide"), new Vector2(0, top - 225), new Vector2(bw, 56), () => { _nav.Clear("stopped in the Guide window"); Refresh(); }, 22);
            _services = UiKit.Button(_rt, "", new Vector2(bw + 10, top - 225), new Vector2(bw, 56), () => { _kafraAirship = !_kafraAirship; Refresh(); }, 19);
            _town = UiKit.Label(_rt, "", new Vector2(-100, top - 290), new Vector2(W - 260, 40), 24, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            _townMore = UiKit.Button(_rt, Lang.T("More"), new Vector2(W / 2 - 110, top - 290), new Vector2(180, 44), () => { _townPage++; Refresh(); }, 20);
            float tw = (W - 80) / 2f;
            for (int i = 0; i < 8; i++)
            {
                int slot = i;
                _townBtns.Add(UiKit.Button(_rt, "", new Vector2(i % 2 == 0 ? -tw / 2 - 5 : tw / 2 + 5, top - 345 - (i / 2) * 62), new Vector2(tw, 54), () => GoTown(slot), 20));
            }
            UiKit.Label(_rt, Lang.T("Go to a map"), new Vector2(0, top - 600), new Vector2(W - 60, 40), 26, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            _search = VrTextField.Create(_rt, new Vector2(-90, top - 650), new Vector2(W - 250, 54), 24);
            _search.Placeholder = Lang.T("map or town name"); _search.MaxLength = 30;
            _search.OnFocus = f => RequestKeyboard?.Invoke(f); _search.OnSubmit = () => Search(_search.Text);
            UiKit.Button(_rt, Lang.T("Search"), new Vector2(W / 2 - 100, top - 650), new Vector2(150, 54), () => Search(_search.Text), 22);
            for (int i = 0; i < 6; i++)
            {
                int slot = i;
                _resultBtns.Add(UiKit.Button(_rt, "", new Vector2(i % 2 == 0 ? -tw / 2 - 5 : tw / 2 + 5, top - 715 - (i / 2) * 58), new Vector2(tw, 50), () => GoResult(slot), 19));
            }
            _prev = UiKit.Button(_rt, "<", new Vector2(-W / 2 + 60, -top + 36), new Vector2(80, 44), () => { _page = Mathf.Max(0, _page - 1); ShowResults(); }, 24);
            _next = UiKit.Button(_rt, ">", new Vector2(W / 2 - 60, -top + 36), new Vector2(80, 44), () => { _page++; ShowResults(); }, 24);
            _results = UiKit.Label(_rt, "", new Vector2(0, -top + 36), new Vector2(400, 40), 20, TextAnchor.MiddleCenter, UiKit.Dim); // (client wave 13, w13look: 540 → 400 px, the eye's room)
            _results.resizeTextForBestFit = true; _results.resizeTextMinSize = 16; _results.resizeTextMaxSize = 20;
            Eye = LookEye.Add(_rt, () => "GuideWindow", new Vector2(-240f, -top + EyeRimPx + EyePx / 2), EyePx);
            ShowResults();
        }

        /// <summary>The services the guide may use (e_navigation_flag): 101 = Kafra and airship (the navigateto default), 0 = none.</summary>
        public int Flag => _kafraAirship ? 101 : 0;

        private Vector3 _spawnPos;
        private Quaternion _spawnRot;

        public void Open()
        {
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
                if (_head != null)
                {
                    if (!PanelPlacing.TryPlaceMenu("GuideWindow", transform, _head, out _))
                    {
                        var f = _head.transform.forward; f.y = 0; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
                        transform.position = _head.transform.position + f * 0.8f + Vector3.up * -0.12f;
                        UiKit.FaceHead(transform, _head);
                    }
                }
                _spawnPos = transform.position;
                _spawnRot = transform.rotation;
            }
            Refresh();
        }
        public void Hide()
        {
            if (gameObject.activeSelf)
            {
                if ((transform.position - _spawnPos).sqrMagnitude > 1e-5f || Quaternion.Angle(transform.rotation, _spawnRot) > 0.5f)
                {
                    var cam = _head != null ? _head : Camera.main;
                    if (cam != null) PanelPlacing.SaveMenuPlace("GuideWindow", transform, cam);
                }
                gameObject.SetActive(false);
            }
        }
        public void Toggle() { if (Visible) Hide(); else Open(); }

        public void Refresh()
        {
            if (_rt == null) return;
            string here = _map?.Invoke() ?? "";
            if (_nav.Active)
            {
                string target = _nav.Type == 0 && _nav.TargetX > 0 ? string.Format("{0} ({1}, {2})", ClientText.MapName(_nav.TargetMap), _nav.TargetX, _nav.TargetY) : ClientText.MapName(_nav.TargetMap);
                _status.text = _nav.RouteHops > 1 ? Lang.F("To {0}: {1} steps.\n{2}", target, _nav.RouteHops, _nav.Instruction)
                             : _nav.RouteHops == 1 ? Lang.F("To {0}: one step.\n{1}", target, _nav.Instruction) : Lang.F("To {0}.\n{1}", target, _nav.Instruction);
            }
            else _status.text = Lang.T("No guide. Pick a place in this town or search for a map.");
            _walk.Interactable = _nav.Active && _nav.Waypoint.HasValue; _walk.SetHover(false);
            _stop.Interactable = _nav.Active; _stop.SetHover(false);
            _services.SetLabel(_kafraAirship ? Lang.T("Kafra & airship: used") : Lang.T("Kafra & airship: not used"));
            var spots = Navigation.TownSpots(here);
            int tpages = Mathf.Max(1, (spots.Count + _townBtns.Count - 1) / _townBtns.Count); if (_townPage >= tpages) _townPage = 0;
            _town.text = spots.Count > 0 ? Lang.F("In {0}", ClientText.MapName(here)) : Lang.F("{0} has no town guide", ClientText.MapName(here));
            _townMore.gameObject.SetActive(tpages > 1);
            _townMore.SetLabel(Lang.F("More ({0} of {1})", _townPage + 1, tpages));
            for (int i = 0; i < _townBtns.Count; i++)
            {
                int k = _townPage * _townBtns.Count + i; bool on = k < spots.Count;
                _townBtns[i].gameObject.SetActive(on);
                if (on) _townBtns[i].SetLabel(string.Format("{0} ({1}, {2})", spots[k].Name.Length > 0 ? spots[k].Name : Lang.T(Navigation.TownKind(spots[k].Type)), spots[k].X, spots[k].Y));
            }
        }

        private void GoTown(int slot)
        {
            string here = Navigation.Bare(_map?.Invoke());
            var spots = Navigation.TownSpots(here);
            int k = _townPage * _townBtns.Count + slot;
            if (k >= spots.Count) return;
            var s = spots[k];
            Debug.Log($"[Guide] town spot {s.Name} ({Navigation.TownKind(s.Type)}) at {s.X},{s.Y}");
            _nav.Go(0, here, s.X, s.Y, 0, Flag, true, true);
            Refresh();
        }

        /// <summary>Maps whose id, navigation name or display name contains the text (case-insensitive): the exact name first, then the
        /// names that start with it, the rest after; indoor maps last (Navi_Map's kind 5003 is on every house / shop interior, 5002 on the
        /// airship maps, 5001 on the rest).</summary>
        public void Search(string text)
        {
            text = (text ?? "").Trim();
            _found = new List<(string, string)>(); _page = 0;
            if (text.Length >= 2)
            {
                var hits = new List<(int rank, string map, string shown)>();
                foreach (var m in Navigation.Maps.Values)
                {
                    string shown = ClientText.MapName(m.Map);
                    bool idHit = m.Map.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0, shownHit = shown.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0, nameHit = m.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!idHit && !shownHit && !nameHit) continue;
                    int rank = string.Equals(m.Map, text, StringComparison.OrdinalIgnoreCase) || string.Equals(shown, text, StringComparison.OrdinalIgnoreCase) ? 0
                             : m.Map.StartsWith(text, StringComparison.OrdinalIgnoreCase) || shown.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
                    if (m.Kind == 5003) rank += 10;
                    hits.Add((rank, m.Map, shown));
                }
                foreach (var h in hits.OrderBy(h => h.rank).ThenBy(h => h.map, StringComparer.Ordinal)) _found.Add((h.map, h.shown));
            }
            Debug.Log($"[Guide] search '{text}': {_found.Count} maps");
            ShowResults();
        }

        private void ShowResults()
        {
            int pages = Mathf.Max(1, (_found.Count + 5) / 6); _page = Mathf.Clamp(_page, 0, pages - 1);
            for (int i = 0; i < _resultBtns.Count; i++)
            {
                int k = _page * 6 + i; bool on = k < _found.Count;
                _resultBtns[i].gameObject.SetActive(on);
                if (on) _resultBtns[i].SetLabel(string.Format("{0} ({1})", _found[k].name, _found[k].map));
            }
            _results.text = _found.Count > 0 ? Lang.F("{0} maps, page {1} of {2}", _found.Count, _page + 1, pages) : "";
            _prev.gameObject.SetActive(_found.Count > 6); _next.gameObject.SetActive(_found.Count > 6);
        }

        private void GoResult(int slot)
        {
            int k = _page * 6 + slot; if (k >= _found.Count) return;
            Debug.Log($"[Guide] go to map {_found[k].map}");
            _nav.Go(1, _found[k].map, 0, 0, 0, Flag, true, true);
            Refresh();
        }

        private void LateUpdate()
        {
            if (_nav != null && _nav.Active && Time.frameCount % 30 == 0) Refresh();
        }
    }
}
