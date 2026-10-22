using Loki.Sprites;
using Loki.World;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 12 (w12cast): the "spell in hand": the skill taken with the casting gesture sits over the hand that chose it (its
    /// icon, 4.5 cm, its name and level, one line of what the trigger does), and the world shows what it will hit:
    ///   a unit     a white-gold ring (wider than the look's lock ring, turning the other way) on the unit the trigger would cast
    ///              on: the one the laser is on, else the one the look has locked: the same choice the cast itself makes
    ///              (LokiGame.OnTargetClicked / OnFreeTrigger);
    ///   the ground a square on the ground where the laser points, else where the look meets the ground: (2n + 1) cells a side
    ///              when the skill's area is known (Game/GroundAreas.cs: rAthena's skill_db), else the one target cell, drawn dashed.
    /// Follows the hand, faces the head (it is on the hand, like the old armed label), no collider.
    /// The wand's sky rule (design note, 2026-10-06, 22:00; LokiGame.W12Wand.cs): while the wand points at the sky the icon is dimmed
    /// under a red cross and the line under the name says that a pull puts the skill away (<see cref="SetCancel"/>); when it
    /// is put away so, the icon leaves the tip: it rises and fades (<see cref="Fizzle"/>, <see cref="SpellFizzle"/>).
    /// </summary>
    public sealed class SpellInHand : MonoBehaviour
    {
        public const float Scale = 0.0003f, IconPx = 150f;
        private RectTransform _rt; private Transform _hand; private Camera _head;
        private Image _icon; private Text _name, _hint; private RectTransform _cancel; private string _hintNormal = ""; private SpellFizzle _fizzle;
        /// <summary>The cancel mark is on the icon; the line under the name; icons that have left the tip so far, and one is rising now.</summary>
        public bool CancelShown => _cancel != null && _cancel.gameObject.activeSelf;
        public string HintShown => _hint != null ? _hint.text : "";
        public int Fizzles => _fizzle != null ? _fizzle.Count : 0;
        public bool FizzleShown => _fizzle != null && _fizzle.Shown;
        private GameObject _ring, _area; private Material _areaMat; private Texture2D _frame, _dashed; private Transform _mapRoot;
        public bool Shown => _rt != null && _rt.gameObject.activeSelf;
        public Transform Hand => _hand;
        public string NameShown => _name != null ? _name.text : "";
        public bool IconShown => _icon != null && _icon.enabled && _icon.sprite != null;
        /// <summary>The unit ringed now (null: none), and the ground square: shown, its middle cell, its side in cells, whether that
        /// side is the skill's known area (smoke, log).</summary>
        public Unit RingedUnit { get; private set; }
        public bool AreaShown => _area != null && _area.activeSelf;
        public Vector2Int AreaCell { get; private set; }
        public int AreaSide { get; private set; }
        public bool AreaKnown { get; private set; }

        public static SpellInHand Create(Transform parent, Camera head)
        {
            var rt = UiKit.Panel("SpellInHand", parent, new Vector2(520, 300), Scale, false);
            var s = rt.gameObject.AddComponent<SpellInHand>(); s._rt = rt; s._head = head;
            UiKit.Box(rt, "plate", new Vector2(0, -78), new Vector2(520, 124), new Color(0.07f, 0.08f, 0.12f, 0.82f));
            s._icon = UiKit.Icon(rt, new Vector2(0, 68), new Vector2(IconPx, IconPx));
            s._name = UiKit.Label(rt, "", new Vector2(0, -50), new Vector2(500, 56), 36, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.6f));
            s._name.resizeTextForBestFit = true; s._name.resizeTextMinSize = 22; s._name.resizeTextMaxSize = 36;
            s._hint = UiKit.Label(rt, "", new Vector2(0, -106), new Vector2(500, 60), 24, TextAnchor.MiddleCenter, UiKit.Dim);
            s._hint.resizeTextForBestFit = true; s._hint.resizeTextMinSize = 16; s._hint.resizeTextMaxSize = 24;
            // the cancel mark: a red cross over the icon (two bars, each on a dark one: no glyph of the font is needed)
            s._cancel = UiKit.Box(rt, "cancel", new Vector2(0, 68), new Vector2(IconPx, IconPx), new Color(0f, 0f, 0f, 0f));
            foreach (float turn in new[] { 45f, -45f }) UiKit.Box(s._cancel, "shade", Vector2.zero, new Vector2(IconPx * 1.25f, 34f), new Color(0f, 0f, 0f, 0.9f)).localRotation = Quaternion.Euler(0f, 0f, turn);
            foreach (float turn in new[] { 45f, -45f }) UiKit.Box(s._cancel, "bar", Vector2.zero, new Vector2(IconPx * 1.2f, 20f), new Color(1f, 0.25f, 0.2f, 1f)).localRotation = Quaternion.Euler(0f, 0f, turn);
            s._cancel.gameObject.SetActive(false);
            s._fizzle = SpellFizzle.Create(parent, head);
            rt.gameObject.SetActive(false);
            return s;
        }

        public void Show(Transform hand, Sprite icon, string name, string hint)
        {
            _hand = hand; _icon.sprite = icon; _icon.enabled = icon != null;
            if (icon != null && icon.texture != null) icon.texture.filterMode = FilterMode.Point; // 24-pixel art drawn large: crisp
            _name.text = name ?? ""; _hintNormal = hint ?? ""; if (!CancelShown) _hint.text = _hintNormal;
            if (!_rt.gameObject.activeSelf) _rt.gameObject.SetActive(true);
            Place();
        }

        /// <summary>The cancel mark (the wand points at the sky: a pull puts the skill away) with its own line under the name; off:
        /// the icon and the usual line again.</summary>
        public void SetCancel(bool on, string hint)
        {
            if (on == CancelShown && (!on || _hint.text == hint)) return;
            _cancel.gameObject.SetActive(on); _icon.color = on ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
            _hint.text = on ? hint ?? "" : _hintNormal;
        }

        /// <summary>The skill is put away: its icon leaves the hand (call before <see cref="Hide"/>).</summary>
        public void Fizzle() { if (Shown && _icon.enabled && _icon.sprite != null) _fizzle.Play(_icon.sprite, _icon.transform.position); }

        public void Hide()
        {
            if (_rt.gameObject.activeSelf) _rt.gameObject.SetActive(false);
            if (CancelShown) { _cancel.gameObject.SetActive(false); _icon.color = Color.white; }
            Aim(null, null, null, 0, false);
        }

        private void Place()
        {
            if (_hand == null || _head == null) return;
            var toHead = _head.transform.position - _hand.position; toHead.y = 0; if (toHead.sqrMagnitude > 1e-4f) toHead.Normalize();
            transform.position = _hand.position + Vector3.up * 0.12f + toHead * 0.03f;
            UiKit.FaceHead(transform, _head);
        }

        private void LateUpdate() { if (_rt.gameObject.activeSelf) Place(); }

        /// <summary>The world's preview for this frame. unit: the unit the cast would go to (null: none). cell + halfSide: a ground
        /// skill's target cell and its area's half side in cells (-1: not known: the cell alone). map null: nothing.</summary>
        public void Aim(MapLoader map, Unit unit, Vector2Int? cell, int halfSide, bool ground)
        {
            var root = map != null ? map.transform : null;
            if (root != _mapRoot) { if (_ring != null) Destroy(_ring); if (_area != null) Destroy(_area); _ring = _area = null; _mapRoot = root; }
            bool wantRing = root != null && !ground && unit != null;
            if (wantRing)
            {
                if (_ring == null) { _ring = Fx.Ring(root, Vector3.zero, 1.45f, new Color(1f, 0.95f, 0.7f), -1f, "cast-target"); _ring.AddComponent<FxSpin>().Speed = -90f; }
                _ring.transform.localPosition = unit.transform.localPosition + Vector3.up * 0.10f;
                if (!_ring.activeSelf) _ring.SetActive(true);
            }
            else if (_ring != null && _ring.activeSelf) _ring.SetActive(false);
            RingedUnit = wantRing ? unit : null;
            bool wantArea = root != null && ground && cell.HasValue && map.Current != null && map.Current.Gat.InBounds(cell.Value.x, cell.Value.y);
            if (wantArea)
            {
                if (_area == null) BuildArea(root);
                bool known = halfSide >= 0; int side = known ? 2 * halfSide + 1 : 1; var c = cell.Value;
                _area.transform.localPosition = new Vector3(c.x + 0.5f, -map.Current.Gat.Get(c.x, c.y).AverageHeight * RoSpace.Scale + 0.09f, c.y + 0.5f);
                _area.transform.localScale = new Vector3(side, 1f, side);
                _areaMat.mainTexture = known ? _frame : _dashed;
                if (!_area.activeSelf) _area.SetActive(true);
                AreaCell = c; AreaSide = side; AreaKnown = known;
            }
            else if (_area != null && _area.activeSelf) _area.SetActive(false);
        }

        /// <summary>A flat unit square (one cell; scaled to the area) with a bright frame and a faint fill.</summary>
        private void BuildArea(Transform root)
        {
            _frame = FrameTexture(false); _dashed = FrameTexture(true);
            _area = new GameObject("cast-area"); _area.transform.SetParent(root, false);
            var mesh = new Mesh { name = "cast-area" };
            mesh.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 }; mesh.RecalculateBounds();
            _area.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = _area.AddComponent<MeshRenderer>();
            _areaMat = new Material(Shader.Find("Sprites/Default")) { name = "LokiCastArea", mainTexture = _frame, color = new Color(0.55f, 0.9f, 1f, 1f) };
            r.sharedMaterial = _areaMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            _area.SetActive(false);
        }

        private static Texture2D FrameTexture(bool dashed)
        {
            const int n = 128; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = dashed ? "cast-area-dashed" : "cast-area", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int edge = Mathf.Min(Mathf.Min(x, n - 1 - x), Mathf.Min(y, n - 1 - y));
                    bool border = edge < 5 && (!dashed || ((x + y) / 12) % 2 == 0);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(border ? 235 : 46));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return tex;
        }

        private void OnDestroy() { if (_ring != null) Destroy(_ring); if (_area != null) Destroy(_area); }
    }

    /// <summary>
    /// Client wave 12 (w12cast): the icon of a skill put away by the wand's sky rule leaves the tip: a copy of it rises
    /// <see cref="Rise"/> m and fades out in <see cref="Seconds"/> s, growing a little. Its own small panel, no collider.
    /// </summary>
    public sealed class SpellFizzle : MonoBehaviour
    {
        public const float Seconds = 0.4f, Rise = 0.12f;
        private Image _icon; private Camera _head; private float _t = -1f; private Vector3 _from;
        public int Count { get; private set; }
        public bool Shown => gameObject.activeSelf;

        public static SpellFizzle Create(Transform parent, Camera head)
        {
            var rt = UiKit.Panel("SpellFizzle", parent, new Vector2(220, 220), SpellInHand.Scale, false);
            var f = rt.gameObject.AddComponent<SpellFizzle>(); f._head = head;
            f._icon = UiKit.Icon(rt, Vector2.zero, new Vector2(SpellInHand.IconPx, SpellInHand.IconPx));
            rt.gameObject.SetActive(false);
            return f;
        }

        public void Play(Sprite icon, Vector3 from)
        {
            Count++; _icon.sprite = icon; _icon.enabled = true; _icon.color = Color.white; _from = from; _t = 0f;
            transform.position = from; transform.localScale = Vector3.one * SpellInHand.Scale;
            gameObject.SetActive(true); UiKit.FaceHead(transform, _head);
        }

        private void LateUpdate()
        {
            if (_t < 0f) return;
            _t += Time.unscaledDeltaTime; float x = Mathf.Clamp01(_t / Seconds);
            transform.position = _from + Vector3.up * (Rise * x); UiKit.FaceHead(transform, _head);
            _icon.color = new Color(1f, 1f, 1f, 1f - x); transform.localScale = Vector3.one * (SpellInHand.Scale * (1f + 0.3f * x));
            if (x >= 1f) { _t = -1f; gameObject.SetActive(false); }
        }
    }
}
