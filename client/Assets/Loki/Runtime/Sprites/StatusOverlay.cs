using System.Collections.Generic;
using System.Text;
using Loki.Formats;
using UnityEngine;

namespace Loki.Sprites
{
    /// <summary>
    /// Client wave 3 (w3status): a sprite attached to a unit for one of its states — the stun / sleep / curse sprites at the head, the
    /// ice block around a frozen unit, the "…" emotion of a silenced one. After roBrowserLegacy's attachments
    /// (Renderer/Entity/EntityAttachments.js; a reimplementation, not the official client):
    ///  • file: data\sprite\이팩트\&lt;file&gt; (add(): 'data/sprite/이팩트/' + file);
    ///  • action: the ACT action (rB's "frame" field); when none is given the unit's facing picks it
    ///    ((camera + unit direction) % actions, renderAttachment());
    ///  • Loop (repeat: the action's frames cycle), Hold (animationId: one fixed frame), Once (play to the last frame, then removed);
    ///  • head: the origin 100 px above the feet (renderAttachment: _position[1] = head ? -100 : 0);
    ///  • opacity: the alpha the layers are drawn with; attachments are NOT tinted by the unit's colour (_renderFiltered resets
    ///    effectColor to white), so a frozen unit's blue doesn't colour its ice block.
    /// Frames advance at the action's delay × 25 ms, as EmoteSprite does. The overlay is a child of the unit, so it turns with the
    /// unit's billboard; it sits a little in front of the unit's relief mesh (rB draws attachments at zIndex 500, in front of the body).
    /// </summary>
    public sealed class StatusOverlay : MonoBehaviour
    {
        public enum Mode { Loop, Hold, Once }
        private const float HeadOriginPx = 100f;

        public string Uid { get; private set; }
        public Mode PlayMode { get; private set; }
        public int ActionIndex => _action;
        private Unit _unit; private SprFile _spr; private ActFile _act;
        private int _action; private int _holdFrame; private bool _head; private float _opacity; private float _start;
        private GameObject _quad; private MeshFilter _mf; private Material _mat; private string _lastKey;
        private readonly List<SpriteFrameCache.CompositeLayer> _layers = new List<SpriteFrameCache.CompositeLayer>();
        private readonly StringBuilder _kb = new StringBuilder();
        private static readonly Dictionary<SprFile, int> _ids = new Dictionary<SprFile, int>();
        /// <summary>The frame drawn last (smoke).</summary>
        public int LastFrame { get; private set; } = -1;

        /// <summary>Adds (or replaces: one per uid, as rB's non-stackable attachments) an overlay on a unit. action &lt; 0 = by facing.</summary>
        public static StatusOverlay Attach(Unit unit, string uid, SprFile spr, ActFile act, int action, Mode mode, int holdFrame = 0, bool head = false, float opacity = 1f)
        {
            if (unit == null || spr == null || act == null || act.Actions.Length == 0) return null;
            if (_described.Add(act)) Debug.Log($"[W3Status] overlay {uid}: {Describe(act, action)}");
            Remove(unit, uid);
            var go = new GameObject("state:" + uid);
            go.transform.SetParent(unit.transform, false);
            var o = go.AddComponent<StatusOverlay>();
            o.Uid = uid; o._unit = unit; o._spr = spr; o._act = act; o._action = action; o.PlayMode = mode; o._holdFrame = holdFrame;
            o._head = head; o._opacity = opacity; o._start = Time.time;
            return o;
        }

        private static readonly HashSet<ActFile> _described = new HashSet<ActFile>();
        /// <summary>The ACT's shape for the log: actions, and for the action used its frames, delay and the first frame's layers.</summary>
        private static string Describe(ActFile act, int action)
        {
            var sb = new StringBuilder();
            sb.Append(act.Actions.Length).Append(" actions");
            int ai = Mathf.Clamp(action < 0 ? 0 : action, 0, act.Actions.Length - 1);
            var a = act.Actions[ai];
            sb.Append($"; action {ai}: {a.Frames.Length} frames, delay {a.Delay:0.##}");
            if (a.Frames.Length > 0)
                foreach (var l in a.Frames[0].Layers)
                    sb.Append($"; layer spr {l.SpriteNumber}{(l.SpriteType == 1 ? "r" : "i")} at ({l.X}, {l.Y}) rgba ({l.Color.r}, {l.Color.g}, {l.Color.b}, {l.Color.a}) scale {l.Scale.x:0.##}");
            return sb.ToString();
        }

        public static StatusOverlay Find(Unit unit, string uid)
        {
            if (unit == null) return null;
            foreach (var o in unit.GetComponentsInChildren<StatusOverlay>(true)) if (o.Uid == uid && !o._removed) return o;
            return null;
        }

        public static void Remove(Unit unit, string uid)
        {
            if (unit == null) return;
            foreach (var o in unit.GetComponentsInChildren<StatusOverlay>(true)) if (o.Uid == uid) o.Kill();
        }

        /// <summary>The uids of the overlays on a unit, e.g. "status-stun status-curse" (smoke).</summary>
        public static string List(Unit unit)
        {
            if (unit == null) return "";
            var sb = new StringBuilder();
            foreach (var o in unit.GetComponentsInChildren<StatusOverlay>(true)) if (!o._removed) sb.Append(sb.Length > 0 ? " " : "").Append(o.Uid).Append(o.PlayMode == Mode.Once ? "(once)" : "");
            return sb.ToString();
        }

        private bool _removed;
        private void Kill() { _removed = true; Uid = "(removed)"; Destroy(gameObject); }

        private void LateUpdate()
        {
            if (_removed) return;
            int ai = _action;
            if (ai < 0) ai = _unit != null && _unit.Sprite != null ? (_unit.Sprite.Direction + ActorSprite.CameraDirection) & 7 : 0;
            ai %= _act.Actions.Length;
            var a = _act.Actions[ai];
            int count = a.Frames.Length;
            if (count == 0) { if (_quad != null) _quad.SetActive(false); return; }
            float frameMs = Mathf.Max(a.Delay, 1f) * 25f;
            int step = (int)((Time.time - _start) * 1000f / frameMs);
            int f;
            bool last = false;
            switch (PlayMode)
            {
                case Mode.Hold: f = Mathf.Clamp(_holdFrame, 0, count - 1); break;
                case Mode.Loop: f = step % count; break;
                default: f = Mathf.Min(step, count - 1); last = f == count - 1; break; // rB: removed once the last frame was drawn
            }
            LastFrame = f;
            Draw(a.Frames[f]);
            if (last) Kill();
        }

        private void Draw(ActFile.Frame frame)
        {
            _layers.Clear(); _kb.Length = 0;
            if (!_ids.TryGetValue(_spr, out int id)) { id = 200000 + _ids.Count; _ids[_spr] = id; }
            foreach (var layer in frame.Layers)
            {
                if (layer.SpriteNumber < 0) continue;
                int available = layer.SpriteType == 1 ? _spr.RgbaFrames.Length : _spr.IndexedFrames.Length;
                if (layer.SpriteNumber >= available) continue;
                if (layer.Color.a == 0 || layer.Scale.x == 0f || layer.Scale.y == 0f) continue;
                var gpu = SpriteFrameCache.Get(_spr, layer.SpriteNumber, layer.SpriteType);
                if (gpu.Width == 0 || gpu.Height == 0) continue;
                _layers.Add(new SpriteFrameCache.CompositeLayer { Frame = gpu, X = layer.X, Y = layer.Y, Mirror = layer.Mirror, Color = layer.Color, ScaleX = layer.Scale.x, ScaleY = layer.Scale.y, Angle = layer.Angle });
                _kb.Append("st").Append(id).Append(':').Append(layer.SpriteNumber).Append(layer.SpriteType == 1 ? 'r' : 'i').Append(layer.Mirror ? 'm' : 'n')
                   .Append('@').Append(layer.X).Append(',').Append(layer.Y).Append('#').Append(((uint)layer.Color.r << 24) | ((uint)layer.Color.g << 16) | ((uint)layer.Color.b << 8) | layer.Color.a);
                if (layer.Scale.x != 1f || layer.Scale.y != 1f || layer.Angle % 360 != 0)
                    _kb.Append('s').Append(Mathf.RoundToInt(layer.Scale.x * 1000f)).Append('/').Append(Mathf.RoundToInt(layer.Scale.y * 1000f)).Append('r').Append(layer.Angle);
                _kb.Append('|');
            }
            string key = _kb.ToString();
            if (key.Length == 0) { if (_quad != null) _quad.SetActive(false); return; }
            // in front of the unit's relief (its bulge towards the viewer) so the body never pokes through
            float z = -((_unit != null && _unit.Sprite != null ? _unit.Sprite.ReliefCells : 0.25f) + 0.06f);
            if (key == _lastKey) { if (_quad != null) { var p = _quad.transform.localPosition; if (p.z != z) _quad.transform.localPosition = new Vector3(p.x, p.y, z); } return; }
            _lastKey = key;
            var comp = SpriteFrameCache.GetComposite(key, _layers, out var center);
            if (comp == null) { if (_quad != null) _quad.SetActive(false); return; }
            if (_quad == null)
            {
                _quad = new GameObject("overlay");
                _quad.transform.SetParent(transform, false);
                _mf = _quad.AddComponent<MeshFilter>();
                var mr = _quad.AddComponent<MeshRenderer>();
                _mat = new Material(Shader.Find("Loki/Sprite"));
                _mat.SetFloat("_Relief", 0.15f);
                _mat.SetColor("_Color", new Color(1f, 1f, 1f, _opacity));
                // drawn after the units (Loki/Sprite sits in AlphaTest+10, sorted front to back): a translucent overlay in front of the
                // body would otherwise write its depth first and hide the body behind it (rB draws attachments after the entity)
                _mat.renderQueue = 3000;
                mr.sharedMaterial = _mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            }
            _quad.SetActive(true);
            _mf.sharedMesh = comp.Mesh; _mat.mainTexture = comp.Texture;
            float sc = 1f / ActorSprite.PixelsPerCell;
            _quad.transform.localPosition = new Vector3(center.x * sc, ((_head ? HeadOriginPx : 0f) - center.y) * sc, z);
            _quad.transform.localScale = new Vector3(sc, sc, sc);
        }

        private void OnDestroy() { if (_mat != null) Destroy(_mat); }
    }
}
