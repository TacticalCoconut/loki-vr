using System.Collections.Generic;
using Loki.Formats;
using Loki.Sprites;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 5 (w5ui): one frame of an SPR / ACT pair from the player's game data as a flat picture for a window row (the cart
    /// styles of the cart window). The frame is composited exactly as the game draws units (Sprites/SpriteFrameCache: every layer's
    /// sprite, offset, mirror, colour, scale and rotation), then made a Sprite; cached per path, action and frame.
    /// </summary>
    public static class SpritePortrait
    {
        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();
        private static readonly List<SpriteFrameCache.CompositeLayer> _layers = new List<SpriteFrameCache.CompositeLayer>();

        /// <summary>Frame <paramref name="frame"/> of action <paramref name="action"/> of <paramref name="basePath"/> (without .spr / .act);
        /// a negative frame takes the middle one, as roBrowserLegacy's change-cart window does (UI/Components/ChangeCart/ChangeCart.js
        /// pickLayers :196-202: animations[(length / 2) | 0]). Null when the data has no such sprite or frame.</summary>
        public static Sprite Frame(string basePath, int action, int frame = -1)
        {
            string key = basePath + "|" + action + "|" + frame;
            if (_cache.TryGetValue(key, out var s)) return s;
            _cache[key] = s = Compose(basePath, action, frame);
            return s;
        }

        /// <summary>Whether the last <see cref="Frame"/> of this sprite drew something (for the smokes and the log).</summary>
        public static bool Has(string basePath, int action, int frame = -1) => _cache.TryGetValue(basePath + "|" + action + "|" + frame, out var s) && s != null;

        private static Sprite Compose(string basePath, int action, int frame)
        {
            var assets = LokiBootstrap.Instance != null ? LokiBootstrap.Instance.Assets : null;
            if (assets == null || !assets.Sprite(basePath, out SprFile spr, out ActFile act) || act.Actions.Length == 0) return null;
            var a = act.Actions[Mathf.Clamp(action, 0, act.Actions.Length - 1)];
            if (a.Frames.Length == 0) return null;
            int f = frame < 0 ? a.Frames.Length / 2 : Mathf.Min(frame, a.Frames.Length - 1);
            _layers.Clear();
            var sb = new System.Text.StringBuilder("portrait:").Append(basePath).Append('#').Append(action).Append('/').Append(f).Append(':');
            foreach (var layer in a.Frames[f].Layers)
            {
                // the same filters as ActorSprite.AddLayers: no sprite, an index past the SPR, invisible placeholder layers
                if (layer.SpriteNumber < 0) continue;
                int available = layer.SpriteType == 1 ? spr.RgbaFrames.Length : spr.IndexedFrames.Length;
                if (layer.SpriteNumber >= available || layer.Color.a == 0 || layer.Scale.x == 0f || layer.Scale.y == 0f) continue;
                var gpu = SpriteFrameCache.Get(spr, layer.SpriteNumber, layer.SpriteType);
                if (gpu.Width == 0 || gpu.Height == 0) continue;
                _layers.Add(new SpriteFrameCache.CompositeLayer { Frame = gpu, X = layer.X, Y = layer.Y, Mirror = layer.Mirror, Color = layer.Color, ScaleX = layer.Scale.x, ScaleY = layer.Scale.y, Angle = layer.Angle });
                sb.Append(layer.SpriteNumber).Append(layer.SpriteType).Append(layer.Mirror ? 'm' : 'n').Append(layer.X).Append(',').Append(layer.Y).Append('|');
            }
            var comp = SpriteFrameCache.GetComposite(sb.ToString(), _layers, out _);
            if (comp == null || comp.Texture == null) return null;
            return Sprite.Create(comp.Texture, new Rect(0, 0, comp.Texture.width, comp.Texture.height), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
