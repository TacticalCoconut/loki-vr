using Loki.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 9 (h9feel): the boss / elite bar of the HUD: the name of the strongest enemy alive (final boss, boss, then elite:
    /// the install's own monster name through the client's sprite table) and its HP as a bar and a percentage. Made by
    /// <see cref="Create"/> inside a HUD panel (h9flow places it: HordeHud); <see cref="Refresh"/> each HUD refresh shows or hides
    /// it by what the world view holds. When the shown enemy changes it pops in (1.15 → 1 over 0.4 s) and its name flashes brighter
    /// for a second: an arrival is an event, without anything full-screen (VR comfort). All numbers are Loki's own.
    /// </summary>
    public sealed class HordeBossBar : MonoBehaviour
    {
        private RectTransform _rt, _fill, _bg; private Text _name, _hp; private float _w;
        private int _serial = -1, _slot = -1; private float _shownAt = -1f;
        public bool Shown => _rt != null && _rt.gameObject.activeSelf;
        public string LastName { get; private set; } = "";
        public float LastHp { get; private set; } = -1f;
        public int Appearances { get; private set; }

        /// <param name="parent">The HUD panel's rect (pixel space).</param>
        /// <param name="pos">The bar's centre in the panel's pixel space.</param>
        /// <param name="width">The bar's width in pixels (the name sits above it).</param>
        public static HordeBossBar Create(RectTransform parent, Vector2 pos, float width)
        {
            var rt = UiKit.Box(parent, "bossbar", pos, new Vector2(width, 70), new Color(0, 0, 0, 0));
            var b = rt.gameObject.AddComponent<HordeBossBar>();
            b._rt = rt; b._w = width;
            b._name = UiKit.Label(rt, "", new Vector2(0, 20), new Vector2(width, 34), 26, TextAnchor.MiddleCenter, new Color(1f, 0.55f, 0.45f));
            b._name.gameObject.AddComponent<Outline>().effectColor = Color.black;
            b._bg = UiKit.Box(rt, "barbg", new Vector2(0, -14), new Vector2(width, 22), new Color(0, 0, 0, 0.75f));
            HordeLook.GoldRim(b._bg); // client wave 21 (w21horde), THE PANEL THEME: the theme's thin gold edge; the fill keeps its red / amber (the danger's colour)
            b._fill = UiKit.Box(b._bg, "fill", Vector2.zero, new Vector2(width - 4, 18), new Color(0.9f, 0.2f, 0.15f));
            b._fill.anchorMin = b._fill.anchorMax = new Vector2(0, 0.5f); b._fill.pivot = new Vector2(0, 0.5f); b._fill.anchoredPosition = new Vector2(2, 0);
            b._hp = UiKit.Label(b._bg, "", Vector2.zero, new Vector2(width - 10, 22), 16, TextAnchor.MiddleCenter, Color.white);
            rt.gameObject.SetActive(false);
            return b;
        }

        /// <summary>Shows the strongest enemy alive (its rank, then the lowest slot), hides when none.</summary>
        public void Refresh(HordeWorldView w, HordeContent c)
        {
            if (_rt == null) return;
            HordeEnemyView best = null; EnemyDef bestDef = null; int bestSlot = -1;
            if (w != null && c != null)
                for (int i = 0; i < w.Enemies.Length; i++)
                {
                    var e = w.Enemies[i]; if (!e.Alive) continue;
                    var d = c.Enemy(e.DefId); if (d == null || d.Rank < EnemyRank.Elite) continue;
                    if (best == null || d.Rank > bestDef.Rank) { best = e; bestDef = d; bestSlot = i; }
                }
            if (best == null)
            {
                if (_rt.gameObject.activeSelf) _rt.gameObject.SetActive(false);
                _serial = -1; _slot = -1; LastHp = -1f;
                return;
            }
            if (bestSlot != _slot || best.Serial != _serial)
            {
                _slot = bestSlot; _serial = best.Serial; _shownAt = Time.time; Appearances++;
                LastName = HordeGame.MobName(bestDef.MobId);
                _name.text = LastName;
                var col = bestDef.Rank >= EnemyRank.Boss ? new Color(0.9f, 0.2f, 0.15f) : new Color(0.95f, 0.65f, 0.25f);
                _fill.GetComponent<Image>().color = col;
            }
            if (!_rt.gameObject.activeSelf) _rt.gameObject.SetActive(true);
            float hp = Mathf.Clamp01(best.Hp / 255f); LastHp = hp;
            _fill.sizeDelta = new Vector2((_w - 4) * hp, 18);
            _hp.text = Lang.F("{0} %", Mathf.RoundToInt(hp * 100));
            float since = Time.time - _shownAt;
            _rt.localScale = Vector3.one * (1f + 0.15f * Mathf.Clamp01(1f - since / 0.4f));
            _name.color = Color.Lerp(new Color(1f, 0.55f, 0.45f), Color.white, 0.5f + 0.5f * Mathf.Sin(since * 9f)) * (since < 1f ? 1f : 0f) + new Color(1f, 0.55f, 0.45f) * (since < 1f ? 0f : 1f);
        }
    }
}
