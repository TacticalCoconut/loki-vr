using System.Collections.Generic;
using UnityEngine;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 18 (h18rules): what points 23 and 27 look like (Loki's own look; the engine's rules: Horde/Core/HordeSim.Rules.cs).
    ///   Point 23, the co-op pause's countdown (HordeWorldView.PauseCountBy / PauseCountToPause / PauseCountSeconds, H_SNAP's rules block),
    ///     shown to everyone: a banner each second "Player: pause in 3" (gold) / "Player: resume in 2" (green), a chat line when it starts and
    ///     when it is cancelled; the HUD's status line and the Horde menu read <see cref="CountdownLine"/>.
    ///   Point 27, Back Slide: the runner under HordePlayerFlags.Vanished is drawn see-through (<see cref="StealthTint"/>: mine 40 %, an
    ///     ally's 22 %) with a speed trail of fading dots behind it (mine gold-white, an ally's paler: the readability rule); each trail
    ///     area's explosion (a Strike of a Back Slide weapon) is an orange ring opening over its area and a burst, instead of the skill's
    ///     own effect (HordeGame skips it: <see cref="OwnsStrike"/>). The trail's warning rings are the world's player Warning look
    ///     (HordeWorldDraw: a ring that fills, gold for mine, pale cyan for an ally's).
    /// HordeGame calls Clear, OnEvent, Draw and Tick (marked hooks).
    /// </summary>
    public sealed class HordeRulesDraw
    {
        /// <summary>Loki's own: the see-through alphas, the trail's length in seconds and its dot size (cells), the explosion's time.</summary>
        public const float MineAlpha = 0.4f, AllyAlpha = 0.22f, TrailSeconds = 0.35f, TrailDot = 0.42f, BlastSeconds = 0.32f;
        private readonly HordeArena _arena;
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private Mesh _quad; private Material _glow, _ring, _solid;
        private static Texture2D _discTex;
        private struct Sample { public Vector2 Pos; public float At; }
        private readonly List<Sample>[] _trail = new List<Sample>[HordeLimits.MaxPlayers];
        private struct Blast { public Vector2 Pos; public float Radius, Start; public bool Mine; }
        private readonly List<Blast> _blasts = new List<Blast>();
        // the countdown as last seen (who, which way, its second) and the holder then (a cancel: the countdown went, nothing changed)
        private int _cdBy = -1, _cdSecond = -1; private bool _cdToPause;
        /// <summary>Counters for the smoke and the log.</summary>
        public int TrailsDrawn { get; private set; }
        public int Blasts { get; private set; }
        public int VanishedSeen { get; private set; }
        public int CountdownsSeen { get; private set; }
        public int CancelsSeen { get; private set; }
        public string LastCountdownBanner { get; private set; } = "";

        public HordeRulesDraw(HordeArena arena) { _arena = arena; for (int i = 0; i < _trail.Length; i++) _trail[i] = new List<Sample>(); }

        public void Clear() { foreach (var t in _trail) t.Clear(); _blasts.Clear(); _cdBy = -1; _cdSecond = -1; }

        // ------------------------------------------------------------------ point 23: the countdown

        /// <summary>"Player: pause in 3" / "Player: resume in 2" while a co-op countdown runs (null: none).</summary>
        public static string CountdownLine(HordeWorldView w)
        {
            if (w == null || w.PauseCountBy < 0 || w.PauseCountBy >= w.Players.Length) return null;
            string who = w.Players[w.PauseCountBy].Name;
            int s = Mathf.Max(1, Mathf.CeilToInt(w.PauseCountSeconds - 0.001f));
            return w.PauseCountToPause ? Lang.F("{0}: pause in {1}", who, s) : Lang.F("{0}: resume in {1}", who, s);
        }

        /// <summary>Once a frame (HordeGame.LateUpdate): the countdown's banner each second, its chat lines.</summary>
        public void Tick(HordeGame g, HordeWorldView w)
        {
            if (w == null) return;
            bool held = HordeWindows.Holder(w) != null;
            if (w.PauseCountBy >= 0)
            {
                int sec = Mathf.Max(1, Mathf.CeilToInt(w.PauseCountSeconds - 0.001f));
                bool fresh = w.PauseCountBy != _cdBy || w.PauseCountToPause != _cdToPause;
                if (fresh)
                {
                    CountdownsSeen++;
                    string who = w.Players[w.PauseCountBy].Name;
                    g.Say?.Invoke(w.PauseCountToPause ? Lang.F("Horde: {0} asked for a pause.", who) : Lang.F("Horde: {0} asked to resume.", who), "9ad1ff");
                    Debug.Log($"[Horde] the pause's countdown: slot {w.PauseCountBy} ({who}) asks to {(w.PauseCountToPause ? "pause" : "resume")}, {w.PauseCountSeconds:0.00} s");
                }
                if (fresh || sec != _cdSecond)
                {
                    LastCountdownBanner = CountdownLine(w);
                    g.Hud?.ShowBanner(LastCountdownBanner, 1.05f, w.PauseCountToPause ? new Color(1f, 0.85f, 0.45f) : new Color(0.7f, 1f, 0.6f), 64);
                    g.Windows?.RefreshRunMenu();
                }
                _cdBy = w.PauseCountBy; _cdToPause = w.PauseCountToPause; _cdSecond = sec;
                return;
            }
            if (_cdBy >= 0)
            {
                // it went: done (the pause came / the run goes on: HordeGame's own banner and line) or cancelled (nothing changed)
                bool cancelled = _cdToPause ? !held : held;
                if (cancelled)
                {
                    CancelsSeen++;
                    g.Say?.Invoke(_cdToPause ? Lang.T("Horde: pause cancelled.") : Lang.T("Horde: resume cancelled."), "9ad1ff");
                    g.Hud?.ShowBanner(_cdToPause ? Lang.T("Pause cancelled") : Lang.T("Resume cancelled"), 0.9f, new Color(0.75f, 0.85f, 1f), 56);
                    Debug.Log($"[Horde] the pause's countdown was cancelled ({(_cdToPause ? "pause" : "resume")})");
                }
                _cdBy = -1; _cdSecond = -1;
                g.Windows?.RefreshRunMenu();
            }
        }

        // ------------------------------------------------------------------ point 27: Back Slide

        /// <summary>The run character's Stealth tint: see-through while Vanished (mine more visible than an ally's), else white.</summary>
        public static Color StealthTint(HordePlayerView p, bool mine)
        {
            if (p == null || p.Fallen || (p.Flags & HordePlayerFlags.Vanished) == 0) return Color.white;
            return new Color(1f, 1f, 1f, mine ? MineAlpha : AllyAlpha);
        }

        /// <summary>A Strike of a Back Slide weapon (its trail's explosion): drawn here; HordeGame plays no skill effect for it.</summary>
        public static bool OwnsStrike(HordeEvent e, HordeContent content) => e.Kind == HordeEventKind.Strike && e.Slot >= 0 && content != null && HordeSim.IsSlide(content.Weapon(e.DefId));

        public void OnEvent(HordeEvent e, HordeContent content, int me)
        {
            if (!OwnsStrike(e, content)) return;
            Blasts++;
            _blasts.Add(new Blast { Pos = new Vector2(e.X, e.Y), Radius = Mathf.Max(0.5f, e.A), Start = Time.time, Mine = e.Slot == me });
            if (_blasts.Count > 24) _blasts.RemoveAt(0);
            var root = _arena.Root;
            if (root != null) World.Fx.Burst(root, _arena.Local(e.X, e.Y) + Vector3.up * 0.4f, e.Slot == me ? new Color(1f, 0.55f, 0.2f) : HordeReadability.AllyTint(new Color(1f, 0.55f, 0.2f)), Mathf.Clamp(e.A * 0.9f, 0.5f, 1.6f), 14);
            if (Blasts == 1) Debug.Log($"[Horde] Back Slide: first trail explosion drawn at ({e.X:0.0}, {e.Y:0.0}), {e.A:0.00} cells");
        }

        public void Draw(HordeWorldView w, HordeContent content, double now, int mySlot, Vector2 myPos)
        {
            var root = _arena.Root; var cam = Camera.main;
            if (root == null || cam == null || w == null) return;
            Ensure();
            float k = Mathf.Abs(root.lossyScale.x), t = Time.time;
            float a = w.Alpha(now);
            // the speed trail behind a vanished runner
            for (int i = 0; i < w.Players.Length; i++)
            {
                var p = w.Players[i]; var tr = _trail[i];
                bool on = p.Present && !p.Fallen && (p.Flags & HordePlayerFlags.Vanished) != 0;
                for (int j = tr.Count - 1; j >= 0; j--) if (t - tr[j].At > TrailSeconds) tr.RemoveAt(j);
                if (on)
                {
                    var pos = i == mySlot ? myPos : new Vector2(p.PrevX + (p.X - p.PrevX) * a, p.PrevY + (p.Y - p.PrevY) * a);
                    if (tr.Count == 0 || (tr[tr.Count - 1].Pos - pos).sqrMagnitude > 0.02f) tr.Add(new Sample { Pos = pos, At = t });
                    if (tr.Count > 24) tr.RemoveAt(0);
                    if (VanishedSeen++ == 0) Debug.Log($"[Horde] Back Slide: slot {i} drawn see-through (alpha {(i == mySlot ? MineAlpha : AllyAlpha)}) with a speed trail");
                }
                if (tr.Count < 2) continue;
                bool mine = i == mySlot;
                for (int j = 0; j < tr.Count - 1; j++)
                {
                    float fade = 1f - (t - tr[j].At) / TrailSeconds;
                    var dp = root.TransformPoint(_arena.Local(tr[j].Pos.x, tr[j].Pos.y) + Vector3.up * 0.7f);
                    float ds = TrailDot * k * (0.45f + 0.55f * fade);
                    var col = mine ? new Color(1f, 0.95f, 0.75f, 0.85f * fade) : HordeReadability.AllyTint(new Color(1f, 0.95f, 0.75f, 0.6f * fade));
                    Dot(dp, ds * 1.4f, new Color(0.03f, 0.03f, 0.05f, 0.45f * fade), cam, _solid);
                    Dot(dp, ds, col, cam);
                }
                TrailsDrawn++;
            }
            // the trail's explosions: an orange ring opening over the area, fading
            for (int i = _blasts.Count - 1; i >= 0; i--)
            {
                var b = _blasts[i]; float age = t - b.Start;
                if (age > BlastSeconds) { _blasts.RemoveAt(i); continue; }
                float u = age / BlastSeconds;
                var col = b.Mine ? new Color(1f, 0.55f, 0.18f, 1f - u) : HordeReadability.AllyTint(new Color(1f, 0.55f, 0.18f, 1f - u));
                var c = root.TransformPoint(_arena.Local(b.Pos.x, b.Pos.y));
                Ring(c, 2f * b.Radius * (0.45f + 0.65f * u) * k, col);
                Ring(c, 2f * b.Radius * (0.25f + 0.4f * u) * k, new Color(1f, 0.9f, 0.5f, 0.8f * (1f - u)));
            }
        }

        private void Ensure()
        {
            if (_quad != null) return;
            _quad = new Mesh { name = "horde-rules-quad" };
            _quad.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) };
            _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            _quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _quad.RecalculateBounds(); _quad.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            _glow = new Material(World.Fx.Glow) { mainTexture = World.Fx.DotTexture(), name = "horde-rules-dot" };
            _ring = new Material(World.Fx.Glow) { mainTexture = World.Fx.RingTexture(), name = "horde-rules-ring" };
            _solid = new Material(Effects.EffectManager.StrShader) { mainTexture = Disc(), name = "horde-rules-solid" };
            _solid.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); _solid.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        /// <summary>A crisp white disc (the colour comes from the draw): the dark rim under a bright dot, readable on light ground.</summary>
        private static Texture2D Disc()
        {
            if (_discTex != null) return _discTex;
            const int n = 64; var tx = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "horde-rules-disc" };
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / (n / 2f) - 1f, dy = (y + 0.5f) / (n / 2f) - 1f;
                tx.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((0.94f - Mathf.Sqrt(dx * dx + dy * dy)) * 16f)));
            }
            tx.Apply(true);
            return _discTex = tx;
        }

        private void Dot(Vector3 world, float size, Color c, Camera cam, Material mat = null)
        {
            var rot = Quaternion.LookRotation(world - cam.transform.position, Vector3.up) * Quaternion.Euler(-90f, 0, 0);
            _mpb.SetColor(ColorId, c);
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(world, rot, Vector3.one * size), mat ?? _glow, 0, null, 0, _mpb, false, false);
        }

        private void Ring(Vector3 world, float diameter, Color c)
        {
            var root = _arena.Root; float up = 0.08f * Mathf.Abs(root.lossyScale.x);
            _mpb.SetColor(ColorId, c);
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(world + Vector3.up * up, root.rotation, Vector3.one * diameter), _ring, 0, null, 0, _mpb, false, false);
        }
    }
}
