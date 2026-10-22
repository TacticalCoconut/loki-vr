using System;
using System.Collections.Generic;
using Loki.Net;
using Loki.World;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Map marks (client wave 2): ZC_COMPASS 0x0144 npcId.L type.L x.L y.L id.B color.L (0x00RRGGBB) from the `viewpoint` script command
    /// (clif.cpp:2709-2731, research/items_combat.md §D): type 0 shows the mark for 15 s, 1 until the map changes, 2 removes mark `id`.
    /// Also the position a search-store click answers (0x083d). Each mark is a dot on the minimap and a coloured ring standing on the
    /// map, so the player sees it without opening the minimap.
    /// </summary>
    public sealed class ViewPoints
    {
        private sealed class Mark { public uint Npc; public int Id; public Vector2 Cell; public Color Color; public float Until; public GameObject Ring; }
        private readonly List<Mark> _marks = new List<Mark>();
        private readonly Func<MapLoader> _map;
        public int Count => _marks.Count;
        public int Received { get; private set; }

        public ViewPoints(Func<MapLoader> map) { _map = map; }

        public IEnumerable<(Vector2 cell, Color color)> Cells() { Tick(); foreach (var m in _marks) yield return (m.Cell, m.Color); }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            if (id != 0x0144) return false;
            var r = new PacketReader(b, o, n);
            r.Skip(2); uint npc = r.U32(); int type = r.S32(); int x = r.S32(), y = r.S32(); int mid = r.U8(); uint col = r.U32();
            Received++;
            Remove(npc, mid);
            if (type != 2) Add(npc, mid, x, y, new Color(((col >> 16) & 0xFF) / 255f, ((col >> 8) & 0xFF) / 255f, (col & 0xFF) / 255f), type == 0 ? 15f : -1f);
            Debug.Log($"[ViewPoint] npc {npc} type {type} mark {mid} at {x},{y} colour {col:x6}");
            return true;
        }

        public void Add(uint npc, int id, int x, int y, Color c, float seconds)
        {
            var m = new Mark { Npc = npc, Id = id, Cell = new Vector2(x, y), Color = c, Until = seconds > 0 ? Time.time + seconds : float.MaxValue };
            var map = _map(); var gat = map?.Current?.Gat;
            if (gat != null && gat.InBounds(x, y))
            {
                m.Ring = Fx.Ring(map.transform, new Vector3(x + 0.5f, -gat.Get(x, y).AverageHeight * RoSpace.Scale + 0.1f, y + 0.5f), 1.4f, c, -1f, "viewpoint");
                m.Ring.AddComponent<FxSpin>().Speed = 45f;
            }
            _marks.Add(m);
        }

        private void Remove(uint npc, int id)
        {
            for (int i = _marks.Count - 1; i >= 0; i--) if (_marks[i].Npc == npc && _marks[i].Id == id) { if (_marks[i].Ring != null) UnityEngine.Object.Destroy(_marks[i].Ring); _marks.RemoveAt(i); }
        }

        public void Tick()
        {
            for (int i = _marks.Count - 1; i >= 0; i--) if (Time.time > _marks[i].Until) { if (_marks[i].Ring != null) UnityEngine.Object.Destroy(_marks[i].Ring); _marks.RemoveAt(i); }
        }

        /// <summary>Map change / teleport: every mark goes (type 1 lasts "until dead or teleported").</summary>
        public void Clear() { foreach (var m in _marks) if (m.Ring != null) UnityEngine.Object.Destroy(m.Ring); _marks.Clear(); }
    }
}
