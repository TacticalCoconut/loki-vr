using System;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 5 (w5ui): the roulette as a wheel beside the roulette window (Game/Roulette.cs keeps the table list and the buttons).
    /// Sources: the player's GRF has no roulette window art (no basic_interface\roullette\* — the files roBrowserLegacy's window names —
    /// in data.grf: searched 2026-09-30), so the look follows roBrowserLegacy, a reimplementation (UI/Components/Roulette/Roulette.html,
    /// .css, .js): one wheel of the prize slots, a red pointer at the top (.wheel-pointer), slots placed clockwise from the top
    /// (generateWheelSlots, Roulette.js:268-308: angle = 360 / n · i, radian = angle − 90°), and a spin of five turns plus the slot
    /// (startSpin, :313-349) with the CSS "ease" curve cubic-bezier(0.25, 0.1, 0.25, 1) (Roulette.css:95).
    /// Where Loki differs, and why:
    ///  • one wheel per row: rAthena's table is 7 rows of 9…3 prizes and ZC_ACK_GENERATE_ROULETTE 0x0a20 names the row (step) and the
    ///    position in that row (idx) (clif.cpp:21036-21091); rB puts all prizes on one wheel and turns it by idx. Shown: the row of the
    ///    prize waiting to be taken, else the row the next spin plays — row 1 with a bronze coin, else 3 with 10 silver, else 5 with
    ///    10 gold (clif.cpp:21064-21078; every claim, and every spin with a prize waiting, sets the stage back to 0: clif_roulette_getitem
    ///    clif.cpp:21008-21011, called at :21051-21053).
    ///  • it stops on idx: rB's rotation of 5 · 360° + idx · slot puts slot (n − idx) mod n under its pointer; here the wheel turns so
    ///    that slot idx ends under the pointer.
    ///  • the spin lasts as long as the server locks the result: sd->roulette.tick = now + max(1, 9 − row − 3) s (clif.cpp:21084; a
    ///    claim or spin before it is ignored, :21046 / :21109) — rB's own 3 s otherwise — so the wheel stops when "Receive" appears
    ///    (Roulette.Wait, which adds a margin for the server's slower clock).
    ///  • sound: rB plays none. The player's GRF has one sound named for it, data\wav\roulette.wav (a 0.15 s click, 22 kHz mono); no
    ///    map, lub or text file of the GRF names it, nor does rB or rAthena, and Ragexe.exe is packed (its strings cannot be read), so
    ///    how the official window uses it is not known: Loki plays it as the wheel's tick each time a slot passes the pointer.
    /// </summary>
    public sealed class RouletteWheel : MonoBehaviour
    {
        public const float Size = 560f;
        private const float Radius = 178f, SlotPx = 84f;
        private RectTransform _rt, _disc;
        private Text _title, _status;
        private readonly SlotUi[] _slots = new SlotUi[Roulette.Columns];
        private sealed class SlotUi { public RectTransform Box; public Image Bg, Icon; public Text Count; }
        private Roulette _r;
        private static Sprite _discSprite;

        // the spin
        private float _angle;                 // the disc's turn, degrees clockwise
        private float _from, _to, _t0, _dur;  // a spin from _from to _to over _dur seconds from _t0
        private int _lastTickSlot;
        private float _lastTickAt;
        public bool Spinning { get; private set; }
        public int Row { get; private set; } = -1;
        public int Count { get; private set; }
        /// <summary>Ticks played during the last spin (one per slot passing the pointer, at most one a frame).</summary>
        public int Ticks { get; private set; }
        public int SpinsDrawn { get; private set; }
        /// <summary>How many slots passed the pointer in the last spin (the ticks it could play).</summary>
        public int SlotsPassed { get; private set; }
        public float LastSpinSeconds { get; private set; }
        /// <summary>The slot under the pointer now.</summary>
        public int PointerSlot => Count <= 0 ? -1 : Mod(Mathf.RoundToInt(-_angle / (360f / Count)), Count);

        /// <summary>Builds the wheel to the right of <paramref name="window"/> (a ListWindow's panel; it moves and hides with it).</summary>
        public static RouletteWheel Attach(RectTransform window, Roulette r)
        {
            var windowSize = window.sizeDelta; // UiKit.Panel's size in pixels
            var rt = UiKit.Box(window, "RouletteWheel", new Vector2(windowSize.x / 2 + UiKit.OutsetOf(window) + Size / 2 + 24, 0), new Vector2(Size, Size + 80), new Color(1, 1, 1, 0));
            rt.GetComponent<UnityEngine.UI.Image>().enabled = false; UiKit.Dress(rt, new Vector2(Size, Size + 80), Mathf.Max(1e-5f, rt.lossyScale.x), PanelLook.Card); // client wave 21 (w21kit): a parchment card of the theme
            // the laser stops on the wheel as on any panel (UiKit.Panel: a collider behind it and a UiBlocker), instead of walking behind it
            var col = rt.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(Size, Size + 80, 4f); col.center = new Vector3(0, 0, 3f);
            rt.gameObject.AddComponent<UiBlocker>();
            var w = rt.gameObject.AddComponent<RouletteWheel>();
            w._rt = rt; w._r = r; w.Build();
            return w;
        }

        private void Build()
        {
            _title = UiKit.Label(_rt, "", new Vector2(0, (Size + 80) / 2 - 32), new Vector2(Size - 30, 44), 26, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.45f));
            _status = UiKit.Label(_rt, "", new Vector2(0, -(Size + 80) / 2 + 30), new Vector2(Size - 30, 40), 20, TextAnchor.MiddleCenter, UiKit.Dim);
            _status.resizeTextForBestFit = true; _status.resizeTextMinSize = 14; _status.resizeTextMaxSize = 20;
            // the disc: a ring the slots sit on (rB .wheel-center: a round dark plate with a grey rim)
            var discBg = new GameObject("disc", typeof(RectTransform)); discBg.transform.SetParent(_rt, false);
            var dr = (RectTransform)discBg.transform; dr.anchorMin = dr.anchorMax = dr.pivot = new Vector2(0.5f, 0.5f); dr.anchoredPosition = new Vector2(0, -6); dr.sizeDelta = new Vector2(Radius * 2 + SlotPx + 20, Radius * 2 + SlotPx + 20);
            var dimg = discBg.AddComponent<Image>(); dimg.sprite = DiscSprite(); dimg.raycastTarget = false; if (UiKit.Overlay != null) dimg.material = UiKit.Overlay;
            _disc = (RectTransform)new GameObject("slots", typeof(RectTransform)).transform; _disc.SetParent(_rt, false);
            _disc.anchorMin = _disc.anchorMax = _disc.pivot = new Vector2(0.5f, 0.5f); _disc.anchoredPosition = new Vector2(0, -6); _disc.sizeDelta = Vector2.one;
            for (int i = 0; i < _slots.Length; i++)
            {
                var s = new SlotUi();
                s.Box = UiKit.Box(_disc, "slot" + i, Vector2.zero, new Vector2(SlotPx, SlotPx), new Color(1, 1, 1, 0.1f));
                s.Bg = s.Box.GetComponent<Image>();
                s.Icon = UiKit.Icon(s.Box, new Vector2(0, 8), new Vector2(54, 54));
                s.Count = UiKit.Label(s.Box, "", new Vector2(0, -28), new Vector2(SlotPx - 4, 24), 18, TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.7f));
                _slots[i] = s;
            }
            // the pointer (rB .wheel-pointer: a red triangle at the top pointing down)
            UiKit.Label(_rt, "▼", new Vector2(0, Radius + SlotPx / 2 + 8 - 6), new Vector2(60, 50), 44, TextAnchor.MiddleCenter, new Color(0.95f, 0.15f, 0.12f));
        }

        /// <summary>A round plate (rB: radial-gradient(#4a4a4a → #2a2a2a), a 3 px #8a8a8a rim), made once.</summary>
        private static Sprite DiscSprite()
        {
            if (_discSprite != null) return _discSprite;
            const int N = 256; var px = new Color32[N * N]; float c = (N - 1) / 2f, r = N / 2f - 1;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    if (d > r) { px[y * N + x] = new Color32(0, 0, 0, 0); continue; }
                    float a = Mathf.Clamp01(r - d); // a soft edge
                    if (d > r - 6) px[y * N + x] = new Color32(138, 138, 138, (byte)(255 * a));
                    else { byte g = (byte)Mathf.Lerp(74, 42, d / r); px[y * N + x] = new Color32(g, g, g, 235); }
                }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "roulette-disc", wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px); tex.Apply(false, true);
            return _discSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>The row the wheel shows when it is not spinning: the waiting prize's, else the one the next spin plays (see the class notes).</summary>
        public static int IdleRow(Roulette r)
        {
            if (r.PrizeRow >= 0 && r.PrizeIdx >= 0) return r.PrizeRow;
            if (r.Bronze > 0) return 0;
            if (r.Silver >= 10) return 2;
            if (r.Gold >= 10) return 4;
            return 0;
        }

        /// <summary>Draws a row's prizes on the wheel (the prize under the pointer highlighted when one is waiting).</summary>
        public void ShowRow(int row)
        {
            if (row < 0 || row >= Roulette.Rows) row = 0;
            Row = row; Count = _r.Table[row].Length;
            float step = 360f / Count;
            for (int i = 0; i < _slots.Length; i++)
            {
                var s = _slots[i]; bool on = i < Count; s.Box.gameObject.SetActive(on);
                if (!on) continue;
                float a = step * i * Mathf.Deg2Rad; // clockwise from the top (rB: (angle − 90°) on a y-down page)
                s.Box.anchoredPosition = new Vector2(Mathf.Sin(a) * Radius, Mathf.Cos(a) * Radius);
                s.Box.localRotation = Quaternion.Euler(0, 0, -step * i); // the slot faces outwards, as rB's rotate(angle)
                var (item, cnt) = _r.Table[row][i];
                var info = item > 0 ? GameDb.GetItem(item) : null;
                s.Icon.sprite = info != null ? WristMenu.ItemIcon(info) : null; s.Icon.enabled = s.Icon.sprite != null;
                s.Count.text = item <= 0 ? "…" : cnt > 1 ? "x" + cnt : "";
                bool bonus = item > 0 && item == _r.BonusItem;
                s.Bg.color = bonus ? new Color(1f, 0.7f, 0.28f, 0.35f) : new Color(1, 1, 1, 0.1f);
            }
            if (!Spinning) Highlight();
        }

        private void Highlight()
        {
            bool waiting = _r.PrizeRow == Row && _r.PrizeIdx >= 0;
            for (int i = 0; i < Count; i++)
            {
                var (item, _) = _r.Table[Row][i];
                bool bonus = item > 0 && item == _r.BonusItem;
                _slots[i].Bg.color = waiting && i == _r.PrizeIdx ? new Color(0.5f, 0.84f, 0.5f, 0.5f) : bonus ? new Color(1f, 0.7f, 0.28f, 0.35f) : new Color(1, 1, 1, 0.1f);
            }
        }

        /// <summary>The server's answer to a spin: turns the wheel of <paramref name="row"/> five times and stops it with slot
        /// <paramref name="idx"/> under the pointer after <paramref name="seconds"/>.</summary>
        public void Spin(int row, int idx, float seconds)
        {
            Spinning = false; ShowRow(row);
            if (Count <= 0) return;
            idx = Mod(idx, Count);
            float step = 360f / Count;
            float final = Mod((Count - idx) % Count * step, 360f);          // the turn that puts idx at the top
            float cur = Mod(_angle, 360f);
            _from = cur; _to = cur + 5f * 360f + Mod(final - cur, 360f);   // rB: 360 · 5 + the slot (Roulette.js:336)
            _t0 = Time.time; _dur = Mathf.Max(0.5f, seconds); LastSpinSeconds = _dur;
            Ticks = 0; _lastTickSlot = SlotAt(_from); _lastTickAt = -1f; SlotsPassed = SlotAt(_to) - SlotAt(_from);
            Spinning = true; SpinsDrawn++;
            for (int i = 0; i < Count; i++) _slots[i].Bg.color = new Color(1, 1, 1, 0.1f);
            Debug.Log($"[Roulette] wheel: row {row + 1} ({Count} slots), stops on slot {idx} after {_dur:0.0} s, {(_to - _from) / 360f:0.00} turns");
        }

        private int SlotAt(float angle) => Count <= 0 ? 0 : Mathf.FloorToInt((angle + 360f / Count / 2f) / (360f / Count));

        private void Update()
        {
            if (_r == null) return;
            if (!Spinning)
            {
                int want = IdleRow(_r);
                if (want != Row || Count != _r.Table[Mathf.Clamp(want, 0, Roulette.Rows - 1)].Length) ShowRow(want);
                _status.text = _r.PrizeIdx >= 0 && _r.PrizeRow == Row ? Lang.T("The prize is under the pointer.") : Lang.F("The next spin plays row {0}.", Row + 1);
                _title.text = Lang.F("Row {0}", Row + 1);
                SetDisc();
                return;
            }
            float t = Mathf.Clamp01((Time.time - _t0) / _dur);
            _angle = Mathf.Lerp(_from, _to, Ease(t));
            int slot = SlotAt(_angle);
            if (slot != _lastTickSlot && Time.time - _lastTickAt >= 0.03f)
            {
                _lastTickSlot = slot; _lastTickAt = Time.time; Ticks++;
                Loki.Audio.LokiAudio.Instance?.PlayWav("roulette.wav", null, 0.7f);
            }
            _title.text = Lang.F("Row {0}", Row + 1);
            _status.text = Lang.T("The wheel is turning…");
            SetDisc();
            if (t >= 1f)
            {
                Spinning = false; _angle = Mod(_to, 360f); SetDisc(); Highlight();
                Debug.Log($"[Roulette] wheel stopped: row {Row + 1}, slot {PointerSlot} under the pointer, {Ticks} ticks");
            }
        }

        private void SetDisc() { _disc.localRotation = Quaternion.Euler(0, 0, -_angle); }

        /// <summary>CSS "ease" = cubic-bezier(0.25, 0.1, 0.25, 1): x(s) solved for the time fraction, then y(s).</summary>
        public static float Ease(float x)
        {
            const float x1 = 0.25f, y1 = 0.1f, x2 = 0.25f, y2 = 1f;
            float s = x;
            for (int i = 0; i < 8; i++)
            {
                float bx = Bez(s, x1, x2) - x, d = BezD(s, x1, x2);
                if (Mathf.Abs(bx) < 1e-5f || Mathf.Abs(d) < 1e-6f) break;
                s = Mathf.Clamp01(s - bx / d);
            }
            return Bez(s, y1, y2);
        }
        private static float Bez(float s, float p1, float p2) { float u = 1 - s; return 3 * u * u * s * p1 + 3 * u * s * s * p2 + s * s * s; }
        private static float BezD(float s, float p1, float p2) { float u = 1 - s; return 3 * u * u * p1 + 6 * u * s * (p2 - p1) + 3 * s * s * (1 - p2); }
        private static int Mod(int a, int m) => ((a % m) + m) % m;
        private static float Mod(float a, float m) => ((a % m) + m) % m;
    }
}
