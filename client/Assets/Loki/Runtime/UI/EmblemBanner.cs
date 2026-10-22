using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// A guild emblem drawn large at a unit (client wave 4, w4social): on a guild flag NPC (class 722 JT_GUILD_FLAG, whose unit packets
    /// carry the owning guild in GUID / GEmblemVer — rAthena clif.cpp:1067-1071, 1136-1140 — and whose emblem is re-announced with
    /// ZC_CHANGE_GUILD when it changes, guild.cpp:1652-1657) and over players' heads on a siege map (ZC_MAPPROPERTY_R2 flag bit 2
    /// SIEGE, "Show emblem over characters heads when in GvG (WoE castle)", clif.cpp:6892). A tiny world-space canvas that billboards
    /// to the head and keeps its real-world size however the diorama is scaled, like the name plate (Overhead). The official client
    /// paints the emblem onto its 3-D flag (Guildflag90_1.gr2); Loki does too since client wave 4b (Sprites/GrannyUnit.cs), which then
    /// suppresses this banner — it stays for a flag drawn with its 2-D stand-in (while the model loads, or if it cannot be read), where it
    /// hangs where the cloth is.
    /// </summary>
    public sealed class EmblemBanner : MonoBehaviour
    {
        public float HeightCells = 1.6f;
        public float MetresPerPx = 0.00035f;
        private RectTransform _root; private Image _img, _frame;
        public bool Shown => _img != null && _img.enabled && _img.sprite != null;
        /// <summary>Client wave 4b (w4granny): hidden while the unit's 3-D guild flag paints the emblem on its cloth, as roBrowserLegacy
        /// draws it (Sprites/GrannyUnit.cs); the banner keeps its emblem and shows again if the flag falls back to its sprite.</summary>
        public bool Suppressed;
        /// <summary>The banner holds an emblem and is drawn.</summary>
        public bool Visible => Shown && !Suppressed;
        public Sprite Emblem => _img != null ? _img.sprite : null;

        public static EmblemBanner Attach(Transform unit, float heightCells, float sizePx)
        {
            var b = unit.GetComponent<EmblemBanner>();
            if (b == null) { b = unit.gameObject.AddComponent<EmblemBanner>(); b.Build(sizePx); }
            b.HeightCells = heightCells;
            return b;
        }

        private void Build(float size)
        {
            var go = new GameObject("emblemBanner", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 4;
            _root = (RectTransform)go.transform; _root.sizeDelta = new Vector2(size + 12, size + 12);
            _frame = UiKit.Box(_root, "frame", Vector2.zero, new Vector2(size + 8, size + 8), new Color(0.05f, 0.05f, 0.08f, 0.75f)).GetComponent<Image>();
            _frame.raycastTarget = false;
            _img = UiKit.Icon(_root, Vector2.zero, new Vector2(size, size));
        }

        public void Set(Sprite emblem)
        {
            if (_img == null) return;
            _img.sprite = emblem; _img.enabled = emblem != null; _frame.enabled = emblem != null;
        }

        private void LateUpdate()
        {
            if (_root == null) return;
            if (_root.gameObject.activeSelf == Suppressed) _root.gameObject.SetActive(!Suppressed);
            if (Suppressed) return;
            float s = transform.lossyScale.x; if (s < 1e-6f) return;
            _root.position = transform.position + Vector3.up * (HeightCells * s);
            var head = Camera.main;
            float k = 1f;
            if (head != null)
            {
                var d = _root.position - head.transform.position; float dist = d.magnitude;
                if (dist > 1e-3f) _root.rotation = Quaternion.LookRotation(d / dist, Vector3.up);
                k = Mathf.Clamp(dist / Overhead.FarMetres, 1f, 4f);
            }
            _root.localScale = Vector3.one * (MetresPerPx * k / s);
        }
    }
}
