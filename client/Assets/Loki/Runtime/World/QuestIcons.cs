using System.Collections.Generic;
using System.Text;
using Loki.Formats;
using Loki.Sprites;
using UnityEngine;

namespace Loki.World
{
    /// <summary>
    /// Client wave 4 (w4world): the quest icons over NPCs and their marks on the minimap, from 0x0446 ZC_QUEST_NOTIFY_EFFECT
    /// npcId.L x.W y.W effect.W color.W (rAthena clif_quest_show_event clif.cpp:18179-18206, the only quest-icon packet of e985006;
    /// sent per NPC with questinfo by pc_show_questinfo pc.cpp:15366-15420 when the player's state changes, and "none" when a condition
    /// stops holding). effect = e_questinfo_types (script.hpp:405-421): 0 ! quest, 1 ? quest, 2 ! job, 3 ? job, 4 ! event, 5 ? event,
    /// 6 click me (warg before 2017-03-15), 7 daily quest, 8 event 3 (warg face before), 9 job quest, 10 jumping poring, 9999 none;
    /// color = e_questinfo_markcolor (script.hpp:423-429): 0 no mark, 1 yellow, 2 green, 3 purple (rAthena's doc/script_commands.txt
    /// "questinfo": "a colored mark in the mini-map like viewpoint").
    /// The icon is a frame loop of the client's own emotion sprite (data\sprite\이팩트\emotion.act) over the NPC's head, as roBrowser
    /// draws it (Engine/MapEngine/Entity.js:1191-1231: emotion id = effect + 81, attached at the head, repeating; DB/Emotions.js:257-277
    /// gives those emotions the actions 80 + effect). The 2026-09-30 GRF's emotionlist.lub uses actions 0-92 for the 87 emotes but never
    /// 80-85 (the icons) nor 93-97; roBrowser's list stops at effect 6 and would show action 86, which this GRF gives the Yut emote 81.
    /// The mark colours are roBrowser's RGB (yellow ffff00, green 00e16a, purple 800080), on rAthena's colour numbers (roBrowser counts
    /// them from the older 0 yellow / 1 orange / 2 green / 3 purple list: it would draw "no mark" yellow).
    /// </summary>
    public sealed class QuestIcons
    {
        /// <summary>Emotion action per quest effect 0..10 (see the class notes; <see cref="IconAction"/>).</summary>
        public static int IconAction(int effect)
        {
            if (effect >= 0 && effect <= 5) return 80 + effect;        // roBrowser DB/Emotions.js:257-274
            if (effect >= 6 && effect <= 10) return 93 + (effect - 6);  // the five actions after the emotes' last (92): see design notes
            return -1;
        }
        public static Color? MarkColor(int color) => color == 1 ? new Color(1f, 1f, 0f) : color == 2 ? new Color(0f, 0xe1 / 255f, 0x6a / 255f) : color == 3 ? new Color(0x80 / 255f, 0f, 0x80 / 255f) : (Color?)null;

        private readonly Dictionary<uint, (int effect, int color, Vector2Int cell)> _state = new Dictionary<uint, (int, int, Vector2Int)>();
        private readonly EntityManager _ents; private readonly RoAssets _assets;
        private SprFile _spr; private ActFile _act; private bool _loaded;
        public int Received { get; private set; }
        public int Shown { get { int n = 0; foreach (var kv in _state) if (kv.Value.effect != 9999) n++; return n; } }
        public (int effect, int color) Of(uint npc) => _state.TryGetValue(npc, out var s) ? (s.effect, s.color) : (9999, 0);

        public QuestIcons(EntityManager ents, RoAssets assets)
        {
            _ents = ents; _assets = assets;
            ents.UnitAdded += u => { if (_state.TryGetValue(u.Id, out var s)) Attach(u, s.effect); };
        }

        public void Set(uint npc, int x, int y, int effect, int color)
        {
            Received++;
            _state[npc] = (effect, color, new Vector2Int(x, y));
            var u = _ents.Get(npc);
            Debug.Log($"[QuestIcon] npc {npc} at {x},{y}: effect {effect} (action {IconAction(effect)}), mark colour {color}{(u == null ? " (not in sight yet)" : "")}");
            if (u != null) Attach(u, effect);
        }

        /// <summary>A new map: the server sends the icons of the new map's NPCs again (pc_show_questinfo after the warp).</summary>
        public void Clear() => _state.Clear();

        /// <summary>The minimap marks: (cell, colour) of every NPC with a mark colour.</summary>
        public IEnumerable<(Vector2 cell, Color color)> Marks()
        {
            foreach (var kv in _state)
            {
                if (kv.Value.effect == 9999) continue;
                var c = MarkColor(kv.Value.color); if (!c.HasValue) continue;
                yield return (kv.Value.cell, c.Value);
            }
        }

        private void Attach(Unit u, int effect)
        {
            var old = u.GetComponentInChildren<QuestIconSprite>();
            int action = IconAction(effect);
            if (!_loaded) { _loaded = true; _assets.Sprite($"data\\sprite\\{RoNames.Effect}\\emotion", out _spr, out _act); }
            if (effect == 9999 || action < 0 || _spr == null || _act == null || action >= _act.Actions.Length) { if (old != null) Object.Destroy(old.gameObject); return; }
            if (old != null && old.Action == action) return;
            if (old != null) Object.Destroy(old.gameObject);
            QuestIconSprite.Show(u.transform, _spr, _act, action);
        }
    }

    /// <summary>
    /// A looping head attachment (the quest icon): <see cref="EmoteSprite"/>'s drawing (ACT frames at delay × 25 ms, 100 px above the
    /// feet, layers composited by SpriteFrameCache) without the end — roBrowser's "repeat: true" (Entity.js:1199-1206).
    /// </summary>
    public sealed class QuestIconSprite : MonoBehaviour
    {
        private const float OriginPx = 100f;
        private SprFile _spr; private ActFile _act; private float _start; private string _lastKey;
        private GameObject _quad; private MeshFilter _mf; private Material _mat;
        private readonly List<SpriteFrameCache.CompositeLayer> _layers = new List<SpriteFrameCache.CompositeLayer>();
        private readonly StringBuilder _kb = new StringBuilder();
        public int Action { get; private set; }
        public int Frame { get; private set; }

        public static QuestIconSprite Show(Transform unit, SprFile spr, ActFile act, int action)
        {
            var go = new GameObject("quest-icon");
            go.transform.SetParent(unit, false);
            var q = go.AddComponent<QuestIconSprite>();
            q._spr = spr; q._act = act; q.Action = action; q._start = Time.time;
            return q;
        }

        private void LateUpdate()
        {
            var a = _act.Actions[Action];
            if (a.Frames.Length == 0) return;
            float frameMs = Mathf.Max(a.Delay, 1f) * 25f;
            int f = (int)((Time.time - _start) * 1000f / frameMs) % a.Frames.Length;
            Frame = f;
            _layers.Clear(); _kb.Length = 0;
            foreach (var layer in a.Frames[f].Layers)
            {
                if (layer.SpriteNumber < 0) continue;
                int available = layer.SpriteType == 1 ? _spr.RgbaFrames.Length : _spr.IndexedFrames.Length;
                if (layer.SpriteNumber >= available || layer.Color.a == 0 || layer.Scale.x == 0f || layer.Scale.y == 0f) continue;
                var gpu = SpriteFrameCache.Get(_spr, layer.SpriteNumber, layer.SpriteType);
                if (gpu.Width == 0 || gpu.Height == 0) continue;
                _layers.Add(new SpriteFrameCache.CompositeLayer { Frame = gpu, X = layer.X, Y = layer.Y, Mirror = layer.Mirror, Color = layer.Color, ScaleX = layer.Scale.x, ScaleY = layer.Scale.y, Angle = layer.Angle });
                _kb.Append("qi:").Append(layer.SpriteNumber).Append(layer.SpriteType == 1 ? 'r' : 'i').Append(layer.Mirror ? 'm' : 'n').Append('@').Append(layer.X).Append(',').Append(layer.Y)
                   .Append('#').Append(((uint)layer.Color.r << 24) | ((uint)layer.Color.g << 16) | ((uint)layer.Color.b << 8) | layer.Color.a)
                   .Append('s').Append(Mathf.RoundToInt(layer.Scale.x * 1000f)).Append('/').Append(Mathf.RoundToInt(layer.Scale.y * 1000f)).Append('r').Append(layer.Angle).Append('|');
            }
            string key = _kb.ToString();
            if (key.Length == 0) { if (_quad != null) _quad.SetActive(false); return; }
            if (key == _lastKey) return;
            _lastKey = key;
            var comp = SpriteFrameCache.GetComposite("emo-quest:" + key, _layers, out var center);
            if (comp == null) { if (_quad != null) _quad.SetActive(false); return; }
            if (_quad == null)
            {
                _quad = new GameObject("icon");
                _quad.transform.SetParent(transform, false);
                _mf = _quad.AddComponent<MeshFilter>();
                var mr = _quad.AddComponent<MeshRenderer>();
                _mat = new Material(Shader.Find("Loki/Sprite"));
                _mat.SetFloat("_Relief", 0.15f);
                mr.sharedMaterial = _mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            }
            _quad.SetActive(true);
            _mf.sharedMesh = comp.Mesh; _mat.mainTexture = comp.Texture;
            float sc = 1f / ActorSprite.PixelsPerCell;
            // roBrowser hangs it at the head (100 px); Loki's name plate sits there too (RO draws names under the feet): the icon goes
            // just above the plate so both stay readable
            var plate = transform.parent != null ? transform.parent.GetComponent<Loki.UI.Overhead>() : null;
            float lift = plate != null ? Mathf.Max(0f, plate.HeightCells + 0.55f - OriginPx * sc) : 0f;
            _quad.transform.localPosition = new Vector3(center.x * sc, (OriginPx - center.y) * sc + lift, -0.03f);
            _quad.transform.localScale = new Vector3(sc, sc, sc);
        }

        private void OnDestroy() { if (_mat != null) Destroy(_mat); }
    }
}
