using System.Collections.Generic;
using Loki.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 18 (h18rules), design note (after wave 18's first pass): "make some Elite Enemies ... They should have an HP bar visible above
    /// them." The HP BAR over every elite and boss, seen by every player: the run character's bar of wave 15 (HordeTag: a dark plate, the
    /// fill, a white trail of the HP just lost that holds 0.25 s and drains in 0.6 s, an outlined name over it) in RED, as wide as the
    /// monster is big (<see cref="WidthPx"/>: 120-320 canvas px by its radius and drawn size; a boss's taller and darker). Its HP is the
    /// snapshot's (0..255 of its max: every enemy's, H_SNAP); its place is where HordeWorldDraw drew its sprite this frame, over its head
    /// (<see cref="Seen"/>: wave 9's thin bar was drawn there; it is not any more). Size: HordeTag's (0.35 mm a canvas px x Settings ->
    /// Comfort's name size, growing beyond 1.2 m). At most <see cref="MaxBars"/> at once. Loki's own numbers.
    /// </summary>
    public sealed class HordeEliteBars
    {
        public const int MaxBars = 16;
        public const float MetresPerPx = HordeTag.MetresPerPx, TrailSeconds = HordeTag.TrailSeconds, LiftPx = 26f;
        /// <summary>True: the elites' and bosses' bars are these (HordeWorldDraw draws no thin bar of its own).</summary>
        public static bool On = true;
        private static readonly Vector3[] _at = new Vector3[HordeLimits.MaxEnemies];
        private static readonly int[] _seenFrame = new int[HordeLimits.MaxEnemies];

        /// <summary>HordeWorldDraw, for an elite or a boss it drew this frame: the point over its head (world).</summary>
        public static void Seen(int slot, Vector3 overHead)
        {
            if (slot < 0 || slot >= _at.Length) return;
            _at[slot] = overHead; _seenFrame[slot] = Time.frameCount;
        }

        /// <summary>A bar's width (canvas px) by the monster's size: its radius and drawn size.</summary>
        public static float WidthPx(EnemyDef d) => d == null ? 140f : Mathf.Clamp(260f * d.Radius * (d.DrawScale > 0 ? d.DrawScale / 150f : 1f), 120f, 320f);

        private sealed class Bar
        {
            public GameObject Go; public RectTransform Root, Bg, Fill, Trail; public Image FillImg; public Text Name;
            public int Slot = -1, Serial = -1; public float Shown = -1f, TrailFrac = -1f, TrailHold; public float W;
        }
        private readonly List<Bar> _bars = new List<Bar>();
        private readonly Transform _parent;
        /// <summary>Bars on screen this frame, the most at once, and the last one's fill (the smoke, the log).</summary>
        public int Showing { get; private set; }
        public int MostShown { get; private set; }
        public float LastFill { get; private set; } = -1f;
        public float LastWidthPx { get; private set; }
        public string LastName { get; private set; } = "";

        public HordeEliteBars(Transform parent) { _parent = parent; }

        /// <summary>The bar shown over enemy slot <paramref name="slot"/> now: its fill, width (canvas px) and name (false: none).</summary>
        public bool BarOf(int slot, out float fill, out float widthPx, out string name)
        {
            var b = _bars.Find(x => x.Slot == slot && x.Go != null && x.Go.activeSelf);
            fill = b != null ? b.Shown : -1f; widthPx = b != null ? b.W : 0f; name = b != null ? b.Name.text : "";
            return b != null;
        }

        public void Clear() { foreach (var b in _bars) { if (b.Go != null) b.Go.SetActive(false); b.Slot = -1; b.Serial = -1; } Showing = 0; }

        /// <summary>Client wave 18 merge (w9feel: the frame after the first enemies drawn rose from 11.0 to 14.4 ms once these bars came, with
        /// no pre-build slice in it): their first-use setup moves to the arena's opening frame, where h17fix put the other drawings' (the map's
        /// load): all <see cref="MaxBars"/> bars made (hidden) and the glyphs of every elite's and boss's name put in the font's texture.</summary>
        public void Prewarm(HordeContent c)
        {
            while (_bars.Count < MaxBars) { var b = Make(); b.Go.SetActive(false); _bars.Add(b); }
            if (c == null || _bars.Count == 0 || _bars[0].Name == null || _bars[0].Name.font == null) return;
            var names = new System.Text.StringBuilder();
            foreach (var d in c.Enemies) if (d != null && d.Rank >= EnemyRank.Elite) names.Append(HordeGame.MobName(d.MobId));
            var t = _bars[0].Name; t.font.RequestCharactersInTexture(names.ToString(), t.fontSize, t.fontStyle);
        }

        /// <summary>Once a frame after the world's drawing: a bar over each elite and boss it drew.</summary>
        public void Update(HordeWorldView w, HordeContent c)
        {
            if (w == null || c == null) return;
            int frame = Time.frameCount, n = 0;
            var head = Camera.main;
            foreach (var b in _bars) if (b.Slot >= 0 && (!w.Enemies[b.Slot].Alive || w.Enemies[b.Slot].Serial != b.Serial || _seenFrame[b.Slot] != frame)) { b.Slot = -1; b.Serial = -1; if (b.Go != null && b.Go.activeSelf) b.Go.SetActive(false); }
            for (int i = 0; i < w.Enemies.Length && n < MaxBars; i++)
            {
                var e = w.Enemies[i];
                if (!e.Alive || _seenFrame[i] != frame) continue;
                var d = c.Enemy(e.DefId); if (d == null || d.Rank < EnemyRank.Elite) continue;
                var b = _bars.Find(x => x.Slot == i && x.Serial == e.Serial) ?? Take(i, e.Serial, d);
                if (b == null) break;
                n++;
                Place(b, _at[i], head);
                SetHp(b, e.Hp / 255f, d.Rank >= EnemyRank.Boss);
                LastFill = b.Shown; LastWidthPx = b.W; LastName = b.Name.text;
            }
            Showing = n; if (n > MostShown) MostShown = n;
        }

        private Bar Take(int slot, int serial, EnemyDef d)
        {
            var b = _bars.Find(x => x.Slot < 0);
            if (b == null) { if (_bars.Count >= MaxBars) return null; b = Make(); _bars.Add(b); }
            b.Slot = slot; b.Serial = serial; b.Shown = -1f; b.TrailFrac = -1f;
            bool boss = d.Rank >= EnemyRank.Boss;
            b.W = WidthPx(d); float h = boss ? 20f : 15f;
            b.Bg.sizeDelta = new Vector2(b.W, h);
            b.Fill.sizeDelta = new Vector2(b.W - 4, h - 4); b.Trail.sizeDelta = new Vector2(b.W - 4, h - 4);
            b.Bg.GetComponent<Image>().color = boss ? new Color(0.12f, 0.02f, 0.02f, 0.92f) : new Color(0.02f, 0.02f, 0.04f, 0.88f);
            b.Name.text = HordeGame.MobName(d.MobId);
            b.Name.rectTransform.anchoredPosition = new Vector2(0, h / 2 + 15);
            b.Go.SetActive(true);
            return b;
        }

        private Bar Make()
        {
            var b = new Bar();
            b.Go = new GameObject("horde-elite-bar", typeof(RectTransform));
            b.Go.transform.SetParent(_parent, false);
            var canvas = b.Go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 5;
            b.Root = (RectTransform)b.Go.transform; b.Root.sizeDelta = new Vector2(400, 120);
            b.Bg = UiKit.Box(b.Root, "bg", Vector2.zero, new Vector2(160, 15), new Color(0.02f, 0.02f, 0.04f, 0.88f));
            HordeLook.GoldRim(b.Bg); // client wave 21 (w21horde), THE PANEL THEME: the theme's thin gold edge (it follows the bar's size); the red stays
            b.Trail = Fill(b.Bg, "trail", new Color(1f, 1f, 1f, 0.85f));
            b.Fill = Fill(b.Bg, "hp", new Color(0.9f, 0.16f, 0.12f));
            b.FillImg = b.Fill.GetComponent<Image>();
            b.Name = UiKit.Label(b.Root, "", new Vector2(0, 22), new Vector2(400, 28), 20, TextAnchor.MiddleCenter, new Color(1f, 0.72f, 0.6f));
            var o = b.Name.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0, 0, 0, 0.9f); o.effectDistance = new Vector2(2, -2);
            return b;
        }

        private static RectTransform Fill(RectTransform bg, string name, Color c)
        {
            var size = bg.sizeDelta;
            var f = UiKit.Box(bg, name, Vector2.zero, new Vector2(size.x - 4, size.y - 4), c);
            f.anchorMin = f.anchorMax = new Vector2(0, 0.5f); f.pivot = new Vector2(0, 0.5f); f.anchoredPosition = new Vector2(2, 0);
            return f;
        }

        /// <summary>Over the head, facing the eye, HordeTag's size (growing with distance beyond Overhead.FarMetres).</summary>
        private static void Place(Bar b, Vector3 at, Camera head)
        {
            float k = VR.Comfort.NameScale;
            if (head != null)
            {
                var d = at - head.transform.position; float dist = d.magnitude;
                if (dist > 1e-3f) b.Root.rotation = Quaternion.LookRotation(d / dist, Vector3.up);
                k *= Mathf.Clamp(dist / Overhead.FarMetres, 1f, 4f);
            }
            float s = MetresPerPx * k;
            b.Root.position = at + Vector3.up * (LiftPx * s);
            float ps = b.Root.parent != null ? Mathf.Abs(b.Root.parent.lossyScale.x) : 1f;
            b.Root.localScale = Vector3.one * (s / Mathf.Max(1e-6f, ps));
        }

        /// <summary>The fill (red; a boss's deeper) and the white trail of the HP just lost (HordeTag's rule).</summary>
        private static void SetHp(Bar b, float frac, bool boss)
        {
            frac = Mathf.Clamp01(frac);
            if (b.TrailFrac < 0f || frac >= b.TrailFrac) b.TrailFrac = frac;
            else
            {
                if (frac < b.Shown - 1e-4f) b.TrailHold = Time.time + 0.25f;
                if (Time.time > b.TrailHold) b.TrailFrac = Mathf.MoveTowards(b.TrailFrac, frac, Time.deltaTime / TrailSeconds);
            }
            b.Shown = frac;
            var size = b.Bg.sizeDelta; float w = size.x - 4, h = size.y - 4;
            b.Fill.sizeDelta = new Vector2(w * frac, h);
            b.Trail.sizeDelta = new Vector2(w * Mathf.Max(b.TrailFrac, frac), h);
            var c = boss ? new Color(0.78f, 0.08f, 0.06f) : new Color(0.92f, 0.18f, 0.12f);
            if (frac <= 0.25f) c = Color.Lerp(c, Color.white, 0.3f * (0.5f + 0.5f * Mathf.Sin(Time.time * 12f)));
            b.FillImg.color = c;
        }
    }
}
