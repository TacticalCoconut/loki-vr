using System.Collections.Generic;
using System.IO;
using Loki.Effects;
using Loki.IO;
using UnityEngine;

namespace Loki.Sprites
{
    /// <summary>
    /// Client wave 4 (w4units): hat effects — the auras some headgears / costumes draw on a unit (ZC_EQUIPMENT_EFFECT 0x0a3b
    /// len.W aid.L status.B { effect.W }*: rAthena clif_hat_effects clif.cpp:21499-21532 sends a unit's whole list with status 1 to the
    /// area when it spawns (:1761) and to each player it comes into view of (:5139); clif_hat_effect_single :21534-21553 one id with
    /// status 1 / 0 when the script command hateffect turns it on / off (script.cpp BUILDIN_FUNC(hateffect)); nothing on a map with the
    /// nocostume flag). The packet names only ids; what they draw is the client's hateffectinfo.lub (data/hateffects.tsv, made on first
    /// run by Data/W4HatEffectTool.cs = client/tools/clientdata/build_hateffects.py). As roBrowserLegacy draws them
    /// (Engine/MapEngine/Entity.js:2800-2856 onHatEffects, Renderer/EffectManager.js:1108-1186 spamHatEffect; a reimplementation):
    ///  • status 1: every id not already on the unit is added; status 0: the listed ids are removed;
    ///  • an entry with a resourceFileName: that STR (data\texture\effect\&lt;file&gt;, its textures from the STR's own folder), attached
    ///    to the unit and looping (EntityAttachments.js:86-110), offset by hatEffectPosX / hatEffectPos × 1/5 × 35 px (positive = right /
    ///    up) and, unlike other STRs, not lowered by half a cell; its size and offset scaled with the unit's (babies 4/5:
    ///    StrEffect.js:330-372); isRenderBeforeCharacter: drawn behind the unit (Loki: a tenth of a cell further from the camera);
    ///  • an entry with only a hatEffectID: that effect of the effect table, attached and persistent (spamHatEffect's EffectTable
    ///    fallback) — through EffectManager.Spam.
    /// isIgnoreRiding, isAdjust*WhenShrinkState, isEffectPair and hatEffectExtraDoramY are read by neither rB nor Loki.
    /// </summary>
    public static class HatEffects
    {
        public sealed class Info { public int Id; public string Str; public int EffectId; public float PosY, PosX; public bool Head, Before; }
        private static Dictionary<int, Info> _db;

        public static Info Get(int id) { Load(); return _db.TryGetValue(id, out var i) ? i : null; }
        public static int Count { get { Load(); return _db.Count; } }

        private static void Load()
        {
            if (_db != null) return;
            _db = new Dictionary<int, Info>();
            string p = Loki.Data.ClientData.File("hateffects.tsv");
            if (!File.Exists(p)) { Debug.LogWarning("[HatEffects] missing " + p); return; }
            foreach (var line in File.ReadAllLines(p, RoReader.Latin1))
            {
                var f = line.Split('\t');
                if (f.Length < 8 || !int.TryParse(f[0], out int id)) continue;
                var i = new Info { Id = id, Str = f[1].Length > 0 ? f[1] : null, Head = f[5] == "1", Before = f[6] == "1" };
                int.TryParse(f[2], out i.EffectId);
                float.TryParse(f[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out i.PosY);
                float.TryParse(f[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out i.PosX);
                _db[id] = i;
            }
            Debug.Log($"[HatEffects] {_db.Count} hat effects");
        }
    }

    /// <summary>The hat effects on one unit (see <see cref="HatEffects"/>).</summary>
    public sealed class UnitHatEffects : MonoBehaviour
    {
        private Unit _u;
        private readonly Dictionary<int, GameObject> _strs = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, int> _fx = new Dictionary<int, int>(); // hat id → effect-table id spammed
        public IEnumerable<int> Ids { get { foreach (var k in _strs.Keys) yield return k; foreach (var k in _fx.Keys) yield return k; } }
        public int Count => _strs.Count + _fx.Count;

        public static UnitHatEffects Of(Unit u)
        {
            var h = u.GetComponent<UnitHatEffects>();
            if (h == null) { h = u.gameObject.AddComponent<UnitHatEffects>(); h._u = u; }
            return h;
        }

        /// <summary>0x0a3b: status 1 adds the ids not on the unit yet, status 0 removes them. Returns how many changed.</summary>
        public int Apply(bool enable, IList<int> ids, EffectManager fx)
        {
            int n = 0;
            foreach (int id in ids)
            {
                if (enable)
                {
                    if (_strs.ContainsKey(id) || _fx.ContainsKey(id)) continue;
                    var info = HatEffects.Get(id);
                    if (info == null) { Debug.Log($"[W4Units] {_u.Id}: hat effect {id} is not in hateffects.tsv"); continue; }
                    if (info.Str != null && fx != null)
                    {
                        var go = new GameObject("hat:" + id);
                        go.transform.SetParent(transform.parent, false); // the map (the unit's own transform turns with its billboard)
                        var s = go.AddComponent<HatStr>();
                        if (s.Init(fx, _u, info)) { _strs[id] = go; n++; } else Destroy(go);
                    }
                    else if (info.EffectId > 0 && fx != null)
                    {
                        int parts = fx.Spam(info.EffectId.ToString(), _u.Id, null, Time.time, -1f, 0, persistent: true);
                        _fx[id] = info.EffectId; n++;
                        Debug.Log($"[W4Units] {_u.Id}: hat effect {id} → effect {info.EffectId} ({parts} drawable part(s))");
                    }
                }
                else
                {
                    if (_strs.TryGetValue(id, out var go)) { if (go != null) Destroy(go); _strs.Remove(id); n++; }
                    if (_fx.TryGetValue(id, out int ef)) { fx?.Remove(_u.Id, ef); _fx.Remove(id); n++; }
                }
            }
            return n;
        }

        private void OnDestroy() { foreach (var go in _strs.Values) if (go != null) Destroy(go); }
    }

    /// <summary>One looping STR attached to a unit with roBrowserLegacy's attachment placement (HatEffects' summary). Its drawing
    /// otherwise follows Fx/FxRenderers.cs StrFxRenderer (the key-frame evaluation, blend factors and colours), which it reads through
    /// the public StrFile / EffectManager API.</summary>
    public sealed class HatStr : MonoBehaviour
    {
        private EffectManager _m; private Unit _u; private HatEffects.Info _info;
        private StrFile _str; private Texture2D[][] _tex; private GameObject[] _layers; private Mesh[] _meshes; private MeshRenderer[] _mr;
        private readonly StrFile.Frame _frame = new StrFile.Frame();
        private readonly Vector3[] _v = new Vector3[4]; private readonly Vector2[] _uv = new Vector2[4];
        private MaterialPropertyBlock _block; private float _start;
        public string File { get; private set; }

        public bool Init(EffectManager m, Unit u, HatEffects.Info info)
        {
            _m = m; _u = u; _info = info; _start = Time.time;
            string file = info.Str.Replace('/', '\\');
            if (file.EndsWith(".str", System.StringComparison.OrdinalIgnoreCase)) file = file.Substring(0, file.Length - 4);
            File = file;
            _str = m.Str(file);
            if (_str == null) { Debug.Log($"[W4Units] {u.Id}: hat effect {info.Id}: no STR {file}"); return false; }
            int cut = file.LastIndexOf('\\');
            string folder = cut >= 0 ? file.Substring(0, cut + 1) : ""; // textures beside the STR (rB EntityAttachments.js:89-97)
            _tex = new Texture2D[_str.Layers.Length][]; _layers = new GameObject[_str.Layers.Length]; _meshes = new Mesh[_str.Layers.Length]; _mr = new MeshRenderer[_str.Layers.Length];
            for (int i = 0; i < _str.Layers.Length; i++)
            {
                var l = _str.Layers[i];
                _tex[i] = new Texture2D[l.Textures.Length];
                for (int t = 0; t < l.Textures.Length; t++)
                {
                    // a texture the GRF lacks: skip that layer (EffectManager.Texture gives null, or RoAssets.MissingTexture before w4fx's change)
                    var tx = m.Texture("effect\\" + folder + l.Textures[t]);
                    _tex[i][t] = tx != null && m.Assets != null && tx == m.Assets.MissingTexture ? null : tx;
                }
                var go = new GameObject("layer" + i); go.transform.SetParent(transform, false);
                _meshes[i] = new Mesh(); _meshes[i].MarkDynamic();
                _meshes[i].vertices = new Vector3[4]; _meshes[i].uv = new Vector2[4]; _meshes[i].triangles = new[] { 0, 1, 2, 2, 1, 3, 0, 2, 1, 2, 3, 1 };
                go.AddComponent<MeshFilter>().sharedMesh = _meshes[i];
                _mr[i] = go.AddComponent<MeshRenderer>(); _mr[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; _mr[i].receiveShadows = false;
                go.SetActive(false);
                _layers[i] = go;
            }
            Debug.Log($"[W4Units] {u.Id}: hat effect {info.Id} → STR {file} ({_str.Layers.Length} layers, {_str.MaxKey} frames at {_str.Fps} fps), offset ({info.PosX}, {info.PosY}), behind {info.Before}");
            LateUpdate();
            return true;
        }

        private void LateUpdate()
        {
            if (_u == null || _u.gameObject == null) { Destroy(gameObject); return; }
            var cam = _m.Head != null ? _m.Head : Camera.main;
            transform.localPosition = _u.transform.localPosition;
            if (cam != null)
            {
                transform.rotation = cam.transform.rotation;
                if (_info.Before) transform.position += cam.transform.forward * 0.1f * (transform.parent != null ? transform.parent.lossyScale.x : 1f);
            }
            bool shown = _u.Sprite == null || _u.Sprite.Tint.a > 0.01f; // a hidden unit's aura hides with it
            float scale = _u.Sprite != null ? _u.Sprite.Scale : 1f;
            float key = (Time.time - _start) * _str.Fps;
            if (_str.MaxKey > 0) key %= _str.MaxKey;
            for (int i = 0; i < _str.Layers.Length; i++)
            {
                var l = _str.Layers[i];
                if (!shown || l.Textures.Length == 0 || !StrFile.Evaluate(l, key, _frame) || _frame.TexIndex >= _tex[i].Length || _tex[i][_frame.TexIndex] == null) { _layers[i].SetActive(false); continue; }
                var f = _frame;
                float ang = -f.Angle * Mathf.Deg2Rad, c = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                // rB StrEffect.js:366-371: attachments center on (320, 320) + the hat offsets (× 1/5 × 35 px × size), no half-cell lowering
                float ox = (f.PosX - 320f + _info.PosX * 7f * scale) / ActorSprite.PixelsPerCell;
                float oy = -((f.PosY - 320f - _info.PosY * 7f * scale) / ActorSprite.PixelsPerCell);
                for (int k = 0; k < 4; k++)
                {
                    float x = f.Xy[k] * scale / ActorSprite.PixelsPerCell, y = -f.Xy[4 + k] * scale / ActorSprite.PixelsPerCell;
                    _v[k] = new Vector3(x * c - y * sn + ox, x * sn + y * c + oy, 0);
                }
                _uv[0] = new Vector2(0, 1); _uv[1] = new Vector2(1, 1); _uv[2] = new Vector2(1, 0); _uv[3] = new Vector2(0, 0);
                var mesh = _meshes[i]; mesh.vertices = _v; mesh.uv = _uv; mesh.RecalculateBounds();
                int src = EffectManager.D3dBlend(f.SrcAlpha), dst = EffectManager.D3dBlend(f.DestAlpha);
                if (src < 0) src = (int)UnityEngine.Rendering.BlendMode.SrcAlpha; if (dst < 0) dst = (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
                var mat = _m.Material(_tex[i][f.TexIndex], src, dst);
                if (_mr[i].sharedMaterial != mat) _mr[i].sharedMaterial = mat;
                (_block ?? (_block = new MaterialPropertyBlock())).SetColor("_Color", new Color(f.Color[0], f.Color[1], f.Color[2], f.Color[3]));
                _mr[i].SetPropertyBlock(_block);
                _layers[i].SetActive(true);
            }
        }

        /// <summary>Layers drawn now (smoke).</summary>
        public int ActiveLayers { get { int n = 0; if (_layers != null) foreach (var g in _layers) if (g != null && g.activeSelf) n++; return n; } }
        private void OnDestroy() { if (_meshes != null) foreach (var m in _meshes) if (m != null) Destroy(m); }
    }
}
