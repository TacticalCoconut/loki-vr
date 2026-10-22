using System.Collections.Generic;
using System.Text;
using Loki.Formats;
using UnityEngine;

namespace Loki.Sprites
{
    /// <summary>
    /// One emote bubble (data\sprite\이팩트\emotion.act/.spr) played once above a unit's head. The client draws emotes as a
    /// "head" attachment: the ACT action for the emote id, origin 100 px above the feet, frames at delay × 25 ms, removed
    /// after the last frame (roBrowserLegacy EntityAttachments.js:297-330; the id → action table is the client's own
    /// emotionlist.lub, shipped as data/emotions.tsv). Layers are composited exactly like the actor's (SpriteFrameCache).
    /// Sits on a child of the unit, so it turns with the unit's billboard and scales with the diorama.
    /// </summary>
    public sealed class EmoteSprite : MonoBehaviour
    {
        private const float OriginPx = 100f; // roBrowser: _position[1] = attachment.head ? -100 : 0
        private SprFile _spr; private ActFile _act; private int _action; private float _start;
        private GameObject _quad; private MeshFilter _mf; private Material _mat; private string _lastKey;
        private readonly List<SpriteFrameCache.CompositeLayer> _layers = new List<SpriteFrameCache.CompositeLayer>();
        private readonly StringBuilder _kb = new StringBuilder();
        private static readonly Dictionary<(SprFile, int), int> _ids = new Dictionary<(SprFile, int), int>();

        /// <summary>Shows emote action `action` over `unit`; a running emote on the same unit is replaced.</summary>
        public static EmoteSprite Show(Transform unit, SprFile spr, ActFile act, int action)
        {
            if (spr == null || act == null || action < 0 || action >= act.Actions.Length) return null;
            var old = unit.GetComponentInChildren<EmoteSprite>();
            if (old != null) Destroy(old.gameObject);
            var go = new GameObject("emote");
            go.transform.SetParent(unit, false);
            var e = go.AddComponent<EmoteSprite>();
            e._spr = spr; e._act = act; e._action = action; e._start = Time.time;
            return e;
        }

        private void LateUpdate()
        {
            var a = _act.Actions[_action];
            if (a.Frames.Length == 0) { Destroy(gameObject); return; }
            float frameMs = Mathf.Max(a.Delay, 1f) * 25f;
            int f = (int)((Time.time - _start) * 1000f / frameMs);
            if (f >= a.Frames.Length) { Destroy(gameObject); return; }
            var frame = a.Frames[f];
            _layers.Clear(); _kb.Length = 0;
            if (!_ids.TryGetValue((_spr, 0), out int id)) { id = 100000 + _ids.Count; _ids[(_spr, 0)] = id; }
            foreach (var layer in frame.Layers)
            {
                if (layer.SpriteNumber < 0) continue;
                int available = layer.SpriteType == 1 ? _spr.RgbaFrames.Length : _spr.IndexedFrames.Length;
                if (layer.SpriteNumber >= available) continue;
                if (layer.Color.a == 0 || layer.Scale.x == 0f || layer.Scale.y == 0f) continue;
                var gpu = SpriteFrameCache.Get(_spr, layer.SpriteNumber, layer.SpriteType);
                if (gpu.Width == 0 || gpu.Height == 0) continue;
                _layers.Add(new SpriteFrameCache.CompositeLayer { Frame = gpu, X = layer.X, Y = layer.Y, Mirror = layer.Mirror, Color = layer.Color, ScaleX = layer.Scale.x, ScaleY = layer.Scale.y, Angle = layer.Angle });
                _kb.Append("emo").Append(id).Append(':').Append(layer.SpriteNumber).Append(layer.SpriteType == 1 ? 'r' : 'i').Append(layer.Mirror ? 'm' : 'n')
                   .Append('@').Append(layer.X).Append(',').Append(layer.Y).Append('#').Append(((uint)layer.Color.r << 24) | ((uint)layer.Color.g << 16) | ((uint)layer.Color.b << 8) | layer.Color.a);
                if (layer.Scale.x != 1f || layer.Scale.y != 1f || layer.Angle % 360 != 0)
                    _kb.Append('s').Append(Mathf.RoundToInt(layer.Scale.x * 1000f)).Append('/').Append(Mathf.RoundToInt(layer.Scale.y * 1000f)).Append('r').Append(layer.Angle);
                _kb.Append('|');
            }
            string key = _kb.ToString();
            if (key.Length == 0) { if (_quad != null) _quad.SetActive(false); return; }
            if (key == _lastKey) return;
            _lastKey = key;
            var comp = SpriteFrameCache.GetComposite(key, _layers, out var center);
            if (comp == null) { if (_quad != null) _quad.SetActive(false); return; }
            if (_quad == null)
            {
                _quad = new GameObject("bubble");
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
            _quad.transform.localPosition = new Vector3(center.x * sc, (OriginPx - center.y) * sc, -0.02f);
            _quad.transform.localScale = new Vector3(sc, sc, sc);
        }

        private void OnDestroy() { if (_mat != null) Destroy(_mat); }
    }
}
