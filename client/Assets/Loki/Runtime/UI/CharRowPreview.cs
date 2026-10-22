using Loki.Sprites;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 5 (w5ui): the character drawn in its row of the character list (w4account's "No sprite preview in the character list
    /// rows"). The game's own sprite code draws it: an <see cref="ActorSprite"/> fed by <see cref="PlayerAppearance.Apply"/> with the look
    /// CHARACTER_INFO carries (<see cref="PlayerLook.FromCharInfo"/>: body, hair style / colour, clothes colour, headgears, weapon, shield,
    /// robe) — the path every player unit and w4account's creation preview take. The sprite itself is never on screen (a scale-0 holder:
    /// the panels draw over the world, LokiUIOverlay.shader, and would cover it); its composited frame (Sprites/SpriteFrameCache) is shown
    /// as a picture in the row.
    /// What it plays follows roBrowserLegacy's character select (a reimplementation: UI/Components/CharSelect/CharSelectCommon.js), facing
    /// the viewer (Entity.set sets direction 4 under Camera.direction 4 → drawn direction 0, Renderer/Entity/Entity.js:224 and
    /// CharSelectCommon.js renderPaginated :1152-1165): standing (ACTION.IDLE, :1066), sitting while a deletion is scheduled (ACTION.SIT,
    /// :1061, :998), dead once it is due (ACTION.DIE for the slot looked at, :1097-1100; the other slots of the page sit, :1061). rB shows the slot under its cursor in ACTION.READYFIGHT (:1123); Loki's
    /// list has no cursor (every row has its own Play button), so every other row stands as rB's unselected slots do.
    /// </summary>
    public sealed class CharRowPreview : MonoBehaviour
    {
        public enum Pose { Idle, Sit, Dead }
        private RawImage _img; private ActorSprite _sprite; private Vector2 _box;
        private PlayerLook _shown; private Pose _pose = (Pose)(-1);
        /// <summary>UI pixels per sprite pixel: a standing Novice (a ~92-px frame) about 70 px tall in the 76-px row; bigger looks shrink to the box.</summary>
        private const float PxScale = 0.76f;
        /// <summary>Client wave 15 (w15gear), point 3: the scale this picture uses (<see cref="PxScale"/> in the character list; the equipment page's doll draws larger).</summary>
        private float _pxScale = PxScale;
        public bool Drawn => _img != null && _img.enabled && _img.texture != null;
        public Pose Shown => _pose;
        public Vector2 DrawnSize => _img != null ? _img.rectTransform.sizeDelta : Vector2.zero;
        public string Layers => _sprite != null ? _sprite.LayerSummary() : "";

        public static CharRowPreview Create(RectTransform row, Vector2 pos, Vector2 box) => Create(row, pos, box, PxScale);

        /// <summary>Client wave 15 (w15gear), point 3: the same picture at <paramref name="pxScale"/> UI pixels per sprite pixel (the equipment page's doll).</summary>
        public static CharRowPreview Create(RectTransform row, Vector2 pos, Vector2 box, float pxScale)
        {
            var go = new GameObject("w5:charpreview", typeof(RectTransform));
            go.transform.SetParent(row, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos; rt.sizeDelta = box;
            var p = go.AddComponent<CharRowPreview>();
            p._box = box; p._pxScale = pxScale;
            p._img = go.AddComponent<RawImage>(); p._img.raycastTarget = false; p._img.enabled = false;
            if (UiKit.Overlay != null) p._img.material = UiKit.Overlay;
            var holder = new GameObject("w5:charpreview-sprite");
            holder.transform.SetParent(go.transform, false);
            holder.transform.localScale = Vector3.zero; // never drawn in the world: only its frame is shown
            p._sprite = holder.AddComponent<ActorSprite>(); p._sprite.IsPlayerType = true;
            return p;
        }

        /// <summary>Draws a character (only when its look or pose changed); null hides the picture.</summary>
        public void Show(Loki.Net.CharInfo c, Pose pose)
        {
            if (c == null) { _img.enabled = false; _shown = null; _pose = (Pose)(-1); return; }
            var boot = LokiBootstrap.Instance;
            if (boot == null || boot.Assets == null) return;
            ShowLook(PlayerLook.FromCharInfo(c), pose);
        }

        /// <summary>Client wave 15 (w15gear), point 3: draws a look (only when it or the pose changed); null hides the picture.</summary>
        public void ShowLook(PlayerLook look, Pose pose)
        {
            if (look == null) { _img.enabled = false; _shown = null; _pose = (Pose)(-1); return; }
            var boot = LokiBootstrap.Instance;
            if (boot == null || boot.Assets == null) return;
            if (_shown == null || !_shown.SameAs(look)) { _shown = look.Clone(); PlayerAppearance.Apply(_sprite, look, boot.Assets); _pose = (Pose)(-1); }
            if (pose != _pose)
            {
                _pose = pose;
                _sprite.Play(pose == Pose.Sit ? ActorSprite.PlayerAction.Sit : pose == Pose.Dead ? ActorSprite.PlayerAction.Dead : ActorSprite.PlayerAction.Idle, true);
            }
        }

        private void LateUpdate()
        {
            if (_sprite == null || _shown == null) return;
            _sprite.Direction = (0 - ActorSprite.CameraDirection) & 7; // drawn direction 0: facing the viewer (ActorSprite draws Direction + CameraDirection)
            var tex = _sprite.CurrentTexture;
            if (tex == null) { _img.enabled = false; return; }
            if (_img.texture != tex)
            {
                _img.texture = tex;
                float w = tex.width * _pxScale, h = tex.height * _pxScale, k = Mathf.Min(1f, Mathf.Min(_box.x / Mathf.Max(w, 1f), _box.y / Mathf.Max(h, 1f)));
                _img.rectTransform.sizeDelta = new Vector2(w * k, h * k);
            }
            _img.enabled = true;
        }
    }
}
