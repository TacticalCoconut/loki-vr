using System;
using System.Collections.Generic;
using Loki.Sprites;
using UnityEngine;

namespace Loki.World
{
    /// <summary>
    /// A faint outline on the ground around our character showing how far the server lets us see (specified after the 2026-10-01
    /// playtest, with Loki Host's view range raised to 30: "add a faint radius circle showing the vision area").
    /// rAthena's view area is a square, not a circle: a unit is in sight while its x and y are each within area_size of ours
    /// (map.hpp:66 AREA_SIZE = battle_config.area_size; clif_insight / clif_outsight through map_foreachinmovearea, unit.cpp:621-631),
    /// so the outline is that square — the cells x ± range, y ± range — laid on the ground (its height at every cell edge).
    /// The client is never told area_size, so it is measured: a unit leaving our sight (ZC_NOTIFY_VANISH type 0, CLR_OUTSIGHT) is,
    /// at that moment, area_size + 1 cells away (Chebyshev) — one step past the edge, its own or ours. The most common of the last
    /// <see cref="Window"/> such distances, once <see cref="MinAgree"/> agree, is the range (a unit hidden by a script at any distance
    /// only adds a stray sample). Until then nothing is drawn rather than a guess.
    /// </summary>
    public sealed class VisionArea : MonoBehaviour
    {
        public const int Window = 25, MinAgree = 3;
        /// <summary>The measured view range in cells (-1: not measured yet).</summary>
        public int Range { get; private set; } = -1;
        public int SampleCount => _samples.Count;
        /// <summary>The outline's points (one per cell corner along the edge) and its bounds in cells, as last drawn.</summary>
        public int PointCount => _local.Count;
        public RectInt Drawn { get; private set; }
        public bool Shown => _line != null && _line.enabled;
        public Color Colour = new Color(1f, 1f, 1f, 0.22f);
        /// <summary>The line's width in cells.</summary>
        public float WidthCells = 0.12f;

        private readonly Queue<int> _samples = new Queue<int>();
        private Func<Unit> _self; private Func<uint, Unit> _unit; private MapLoader _map;
        private LineRenderer _line;
        private Vector2Int _drawnAt = new Vector2Int(int.MinValue, 0); private int _drawnRange = -1; private object _drawnMap;

        public void Init(Func<Unit> self, Func<uint, Unit> unit, MapLoader map)
        {
            _self = self; _unit = unit; _map = map;
            var go = new GameObject("VisionArea");
            go.transform.SetParent(transform, false);
            _line = go.AddComponent<LineRenderer>();
            _line.useWorldSpace = true; _line.loop = true;
            _line.material = new Material(Shader.Find("Sprites/Default")) { name = "vision-area" };
            _line.startColor = _line.endColor = Colour;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; _line.receiveShadows = false;
            _line.enabled = false;
        }

        /// <summary>EntityManager.Vanished (before the unit is removed): a type 0 (out of sight) is a sample.</summary>
        public void OnVanished(uint id, int type)
        {
            if (type != 0) return;
            var s = _self?.Invoke(); var u = _unit?.Invoke(id);
            if (s == null || u == null) return;
            Vector2Int a = Vector2Int.RoundToInt(s.Cell), b = Vector2Int.RoundToInt(u.Cell);
            int d = Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
            if (d >= 2) AddSample(d - 1);
        }

        /// <summary>One measured range (out-of-sight distance − 1); public for the smoke.</summary>
        public void AddSample(int range)
        {
            _samples.Enqueue(range);
            while (_samples.Count > Window) _samples.Dequeue();
            var counts = new Dictionary<int, int>();
            int best = -1, bestN = 0;
            foreach (var r in _samples)
            {
                counts.TryGetValue(r, out int n); counts[r] = ++n;
                if (n > bestN || (n == bestN && r > best)) { best = r; bestN = n; }
            }
            if (bestN >= MinAgree && best != Range)
            {
                Debug.Log($"[Vision] the server's view range: {best} cells ({bestN} of the last {_samples.Count} units left sight one step past it)");
                Range = best;
            }
        }

        private void LateUpdate()
        {
            var s = _self?.Invoke();
            var map = _map != null ? _map.Current : null;
            if (_line == null) return;
            if (s == null || map == null || Range < 0) { _line.enabled = false; return; }
            var c = Vector2Int.RoundToInt(s.Cell);
            if (c != _drawnAt || Range != _drawnRange || !ReferenceEquals(map, _drawnMap)) Rebuild(c, map.Gat);
            // world space: the points follow the map root as it moves / turns / scales (Table, Follow and First person move it)
            var root = _map.transform;
            for (int i = 0; i < _local.Count; i++) _world[i] = root.TransformPoint(_local[i]);
            _line.positionCount = _local.Count;
            _line.SetPositions(_world);
            _line.widthMultiplier = WidthCells * Mathf.Abs(root.lossyScale.x);
            _line.enabled = true;
            _drawnMap = map;
        }

        private readonly List<Vector3> _local = new List<Vector3>();
        private Vector3[] _world = new Vector3[0];

        /// <summary>The square's edge, a point at every cell corner, on the ground (Loki units: cell x spans x .. x + 1).</summary>
        private void Rebuild(Vector2Int c, Formats.GatFile gat)
        {
            _drawnAt = c; _drawnRange = Range;
            int x0 = c.x - Range, y0 = c.y - Range, x1 = c.x + Range + 1, y1 = c.y + Range + 1;
            if (gat != null) { x0 = Mathf.Max(0, x0); y0 = Mathf.Max(0, y0); x1 = Mathf.Min(gat.Width, x1); y1 = Mathf.Min(gat.Height, y1); }
            Drawn = new RectInt(x0, y0, x1 - x0, y1 - y0);
            _local.Clear();
            for (int x = x0; x < x1; x++) _local.Add(P(gat, x, y0));
            for (int y = y0; y < y1; y++) _local.Add(P(gat, x1, y));
            for (int x = x1; x > x0; x--) _local.Add(P(gat, x, y1));
            for (int y = y1; y > y0; y--) _local.Add(P(gat, x0, y));
            if (_world.Length != _local.Count) _world = new Vector3[_local.Count];
        }

        private static Vector3 P(Formats.GatFile gat, int x, int y)
        {
            // a corner shared by up to four cells: the highest of their corners there (a cliff's top), a little above the ground
            float h = float.MinValue;
            for (int dx = -1; dx <= 0; dx++) for (int dy = -1; dy <= 0; dy++)
            {
                if (gat == null || !gat.InBounds(x + dx, y + dy)) continue;
                const float e = 0.001f; // just inside that cell, by its corner
                h = Mathf.Max(h, Game.GroundItems.GroundHeight(gat, x + (dx < 0 ? -e : e), y + (dy < 0 ? -e : e)));
            }
            if (h == float.MinValue) h = 0f;
            return new Vector3(x, h + 0.08f, y);
        }
    }
}
