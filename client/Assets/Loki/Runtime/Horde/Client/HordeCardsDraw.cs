using System.Collections.Generic;
using Loki.Sprites;
using UnityEngine;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 17 (h17cards): what the new weapon kinds of design 1 / point 15 look like on the table (Loki's own look, at table
    /// scale, readable on any ground): a Mark over a marked enemy (a red-gold diamond that pulses), a Parry weapon's guard around its
    /// owner (a pale blue ring, one pip per charge; Defender: an arc in front), the falcon (a pet without a monster sprite: a bright
    /// bird-dot circling its owner), a dash's streak along its path, the flash of a parried hit. Events: Dash, Evolved (a banner and
    /// a chat line), Parried. HordeGame calls Draw after the world's and OnEvent for every event (marked hooks).
    /// </summary>
    public sealed class HordeCardsDraw
    {
        /// <summary>Loki's own sizes (cells) and times (seconds).</summary>
        public const float MarkSize = 0.52f, MarkLift = 2.45f, GuardPip = 0.18f, DashSeconds = 0.35f, ParrySeconds = 0.3f, FalconSize = 0.38f;
        private readonly HordeArena _arena;
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private Mesh _quad; private Material _glow, _ring, _darkRing, _solid, _diamond;
        private static Texture2D _discTex, _diamondTex;
        private struct Streak { public Vector2 From, To; public float Start; public bool Mine; }
        private struct Flash { public Vector2 Pos; public float Start; public bool Strike; }
        private readonly List<Streak> _dashes = new List<Streak>();
        private readonly List<Flash> _flashes = new List<Flash>();
        /// <summary>Counters for the smoke and the log.</summary>
        public int MarksDrawn { get; private set; }
        public int GuardsDrawn { get; private set; }
        public int FalconsDrawn { get; private set; }
        public int Dashes { get; private set; }
        public int Evolutions { get; private set; }
        public int Parries { get; private set; }
        public string LastEvolved { get; private set; } = "";

        public HordeCardsDraw(HordeArena arena) { _arena = arena; }

        public void Clear() { _dashes.Clear(); _flashes.Clear(); }

        private void Ensure()
        {
            if (_quad != null) return;
            _quad = new Mesh { name = "horde-cards-quad" };
            _quad.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) };
            _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            _quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _quad.RecalculateBounds(); _quad.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            _glow = new Material(World.Fx.Glow) { mainTexture = World.Fx.DotTexture(), name = "horde-cards-dot" };
            _ring = new Material(World.Fx.Glow) { mainTexture = World.Fx.RingTexture(), name = "horde-cards-ring" };
            // flat, alpha-blended (not additive): the dark ring under a guard's bright ring, so it reads on light ground (HordeWorldDraw's _darkRing)
            _darkRing = new Material(Effects.EffectManager.StrShader) { mainTexture = World.Fx.RingTexture(), name = "horde-cards-darkring" };
            _darkRing.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); _darkRing.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            // crisp, solid (alpha-blended) shapes: a soft additive dot is lost on light ground at table scale (the first smoke's screenshots)
            _solid = new Material(Effects.EffectManager.StrShader) { mainTexture = Shape(false), name = "horde-cards-solid" };
            _solid.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); _solid.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _diamond = new Material(Effects.EffectManager.StrShader) { mainTexture = Shape(true), name = "horde-cards-diamond" };
            _diamond.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); _diamond.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        /// <summary>A crisp disc or diamond (sharp edge, a pixel of soft rim), white: the colour comes from the draw.</summary>
        private static Texture2D Shape(bool diamond)
        {
            if (diamond && _diamondTex != null) return _diamondTex;
            if (!diamond && _discTex != null) return _discTex;
            const int n = 64; var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = diamond ? "horde-cards-diamond" : "horde-cards-disc" };
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float dx = Mathf.Abs((x + 0.5f) / (n / 2f) - 1f), dy = Mathf.Abs((y + 0.5f) / (n / 2f) - 1f);
                float d = diamond ? dx + dy : Mathf.Sqrt(dx * dx + dy * dy);
                t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((0.94f - d) * 16f)));
            }
            t.Apply(true);
            if (diamond) _diamondTex = t; else _discTex = t;
            return t;
        }

        /// <summary>An event of the new cards: a dash's streak, an evolution's banner and chat line, a parry's flash.</summary>
        public void OnEvent(HordeGame g, HordeEvent e, int me)
        {
            switch (e.Kind)
            {
                case HordeEventKind.Dash:
                    _dashes.Add(new Streak { From = new Vector2(e.X, e.Y), To = new Vector2(e.X2, e.Y2), Start = Time.time, Mine = e.Slot == me });
                    if (_dashes.Count > 16) _dashes.RemoveAt(0);
                    Dashes++;
                    break;
                case HordeEventKind.Parried:
                    _flashes.Add(new Flash { Pos = new Vector2(e.X, e.Y), Start = Time.time, Strike = e.Value == 4 });
                    if (_flashes.Count > 16) _flashes.RemoveAt(0);
                    Parries++;
                    break;
                case HordeEventKind.Evolved:
                {
                    Evolutions++;
                    string name = HordeText.Weapon(g.Content, e.DefId);
                    LastEvolved = name;
                    var w = g.Channel?.World; string who = w != null && e.Slot >= 0 && e.Slot < w.Players.Length ? w.Players[e.Slot].Name : "";
                    if (e.Slot == me) { g.Hud?.ShowBanner(Lang.F("{0}!", name), 2.2f, new Color(1f, 0.85f, 0.4f), 64); g.Say?.Invoke(Lang.F("Horde: your weapon evolved: {0}.", name), "ffd060"); }
                    else g.Say?.Invoke(Lang.F("Horde: {0}'s weapon evolved: {1}.", who, name), "ffd060");
                    break;
                }
                case HordeEventKind.CardTaken:
                {
                    var kind = (HordeCardKind)Mathf.RoundToInt(e.A);
                    if (kind == HordeCardKind.WeaponTranscend || kind == HordeCardKind.PassiveTranscend)
                    {
                        Evolutions++;
                        string name = kind == HordeCardKind.WeaponTranscend ? HordeText.Weapon(g.Content, e.DefId) : HordeText.Passive(g.Content, e.DefId);
                        LastEvolved = name;
                        var w = g.Channel?.World; string who = w != null && e.Slot >= 0 && e.Slot < w.Players.Length ? w.Players[e.Slot].Name : "";
                        if (e.Slot == me) { g.Hud?.ShowBanner(Lang.F("✦ {0}!", name), 2.2f, new Color(1f, 0.82f, 0.35f), 60); g.Say?.Invoke(Lang.F("Horde: transcended into {0}!", name), "ffd060"); }
                        else g.Say?.Invoke(Lang.F("Horde: {0} transcended into {1}!", who, name), "ffd060");
                    }
                    break;
                }
            }
        }

        public void Draw(HordeWorldView w, HordeContent content, double now, int mySlot, Vector2 myPos)
        {
            var root = _arena.Root; var cam = Camera.main;
            if (root == null || cam == null || w == null || content == null) return;
            Ensure();
            float k = Mathf.Abs(root.lossyScale.x), t = Time.time;
            float a = w.Alpha(now);
            float drawTick = w.DrawTick(now);
            // marks over marked enemies
            for (int i = 0; i < w.Enemies.Length; i++)
            {
                var e = w.Enemies[i];
                if (!e.Alive || (e.Flags & HordeEnemyFlags.Marked) == 0) continue;
                var def = content.Enemy(e.DefId); float scale = def != null ? Mathf.Clamp(def.DrawScale / 100f, 0.8f, 2.5f) : 1f;
                float x = e.PrevX + (e.X - e.PrevX) * a, y = e.PrevY + (e.Y - e.PrevY) * a;
                var pos = root.TransformPoint(_arena.Local(x, y) + Vector3.up * (MarkLift * scale));
                float pulse = 0.85f + 0.15f * Mathf.Sin(t * 8f + i);
                Dot(pos, MarkSize * 1.35f * k * pulse, new Color(0.08f, 0.02f, 0.02f, 0.9f), cam, _diamond);
                Dot(pos, MarkSize * k * pulse, new Color(1f, 0.42f, 0.12f, 1f), cam, _diamond);
                Dot(pos, MarkSize * 0.4f * k, new Color(1f, 0.95f, 0.65f, 1f), cam, _diamond);
                if (MarksDrawn++ == 0) Debug.Log($"[Horde] cards: first mark drawn at {pos} ({MarkSize * k:0.0000} m; in the view at {cam.WorldToViewportPoint(pos)}), the eye at {cam.transform.position}, the table's scale {k:0.0000}");
            }
            // guards (Parry): a ring around the owner, a pip per charge; an arc in front when it guards one side
            foreach (var fx in w.Fx)
            {
                if (fx.Kind != HordeFxKind.Guard || fx.OwnerSlot < 0 || fx.OwnerSlot >= w.Players.Length) continue;
                var op = w.Players[fx.OwnerSlot]; if (!op.Present || op.Fallen) continue;
                Vector2 c = fx.OwnerSlot == mySlot ? myPos : new Vector2(op.PrevX + (op.X - op.PrevX) * a, op.PrevY + (op.Y - op.PrevY) * a);
                var centre = root.TransformPoint(_arena.Local(c.x, c.y));
                float r = Mathf.Max(0.8f, fx.Radius);
                var col = fx.OwnerSlot == mySlot ? new Color(0.55f, 0.85f, 1f, 0.9f) : new Color(0.55f, 0.85f, 1f, 0.55f);
                int n = Mathf.Max(1, fx.Count);
                if (fx.Angle > 0f && fx.Angle < 360f)
                {
                    // the front arc: dots along it, the charges brighter
                    float face = op.Facing; int dots = 9;
                    for (int d = 0; d < dots; d++)
                    {
                        float ang = (face - fx.Angle / 2f + fx.Angle * d / (dots - 1)) * Mathf.Deg2Rad;
                        var p = root.TransformPoint(_arena.Local(c.x + Mathf.Cos(ang) * r, c.y + Mathf.Sin(ang) * r) + Vector3.up * 0.6f);
                        Dot(p, GuardPip * 1.5f * k, new Color(0.03f, 0.04f, 0.08f, 0.8f), cam, _solid);
                        Dot(p, GuardPip * k * (d * n / dots < n ? 1.2f : 0.8f), new Color(col.r, col.g, col.b, 1f), cam, _solid);
                    }
                }
                else
                {
                    Ring(centre, 2f * r * k * 1.06f, new Color(0.03f, 0.04f, 0.08f, 0.6f), _darkRing);
                    Ring(centre, 2f * r * k, col);
                    for (int d = 0; d < n; d++)
                    {
                        float ang = (t * 60f + d * 360f / n) * Mathf.Deg2Rad;
                        var p = root.TransformPoint(_arena.Local(c.x + Mathf.Cos(ang) * r, c.y + Mathf.Sin(ang) * r) + Vector3.up * 0.7f);
                        Dot(p, GuardPip * 1.5f * k, new Color(0.03f, 0.04f, 0.08f, 0.85f), cam, _solid);
                        Dot(p, GuardPip * k, new Color(col.r, col.g, col.b, 1f), cam, _solid);
                    }
                }
                if (GuardsDrawn++ == 0) Debug.Log($"[Horde] cards: first guard drawn at {centre} ({2f * r * k:0.0000} m across, {n} charge(s))");
            }
            // the falcon (a pet with no monster sprite): a bright bird-dot with two wing dots
            foreach (var s in w.Summons)
            {
                var wd = content.Weapon(s.DefId); if (wd == null || wd.SummonMobId != 0 || wd.Kind != WeaponKind.Pet) continue;
                float x = s.PrevX + (s.X - s.PrevX) * a, y = s.PrevY + (s.Y - s.PrevY) * a;
                var pos = root.TransformPoint(_arena.Local(x, y) + Vector3.up * 1.6f);
                float flap = Mathf.Sin(t * 14f + s.Id) * 0.18f;
                var right = cam.transform.right;
                var wingL = pos - right * (0.4f * k) + Vector3.up * (flap * k); var wingR = pos + right * (0.4f * k) + Vector3.up * (flap * k);
                Dot(wingL, FalconSize * 0.85f * k, new Color(0.06f, 0.04f, 0.03f, 0.85f), cam, _diamond);
                Dot(wingR, FalconSize * 0.85f * k, new Color(0.06f, 0.04f, 0.03f, 0.85f), cam, _diamond);
                Dot(pos, FalconSize * 1.35f * k, new Color(0.06f, 0.04f, 0.03f, 0.85f), cam, _solid);
                Dot(wingL, FalconSize * 0.6f * k, new Color(0.75f, 0.6f, 0.4f, 1f), cam, _diamond);
                Dot(wingR, FalconSize * 0.6f * k, new Color(0.75f, 0.6f, 0.4f, 1f), cam, _diamond);
                Dot(pos, FalconSize * k, new Color(1f, 0.95f, 0.85f, 1f), cam, _solid);
                if (FalconsDrawn++ == 0) Debug.Log($"[Horde] cards: first falcon drawn at {pos}");
            }
            // dash streaks: dots along the path, fading from its start
            for (int i = _dashes.Count - 1; i >= 0; i--)
            {
                var d = _dashes[i]; float age = t - d.Start;
                if (age > DashSeconds) { _dashes.RemoveAt(i); continue; }
                float fade = 1f - age / DashSeconds; int dots = 8;
                var col = d.Mine ? new Color(1f, 0.9f, 0.55f, fade) : new Color(0.6f, 0.9f, 1f, 0.8f * fade);
                for (int j = 0; j <= dots; j++)
                {
                    var q = Vector2.Lerp(d.From, d.To, j / (float)dots);
                    var dp = root.TransformPoint(_arena.Local(q.x, q.y) + Vector3.up * 0.5f); float ds = 0.45f * k * (0.5f + 0.5f * j / dots);
                    Dot(dp, ds * 1.4f, new Color(0.03f, 0.03f, 0.05f, 0.6f * fade), cam, _solid);
                    Dot(dp, ds, col, cam, _solid);
                }
            }
            // a parried hit / a cancelled strike: a ring that opens and fades
            for (int i = _flashes.Count - 1; i >= 0; i--)
            {
                var f = _flashes[i]; float age = t - f.Start;
                if (age > ParrySeconds) { _flashes.RemoveAt(i); continue; }
                float u = age / ParrySeconds;
                var col = f.Strike ? new Color(1f, 1f, 0.7f, 1f - u) : new Color(0.7f, 0.95f, 1f, 1f - u);
                var fc = root.TransformPoint(_arena.Local(f.Pos.x, f.Pos.y)); float fd = (f.Strike ? 4f : 1.6f) * (0.6f + 0.8f * u) * k;
                Ring(fc, fd * 1.06f, new Color(0.03f, 0.04f, 0.08f, 0.5f * (1f - u)), _darkRing);
                Ring(fc, fd, col);
            }
        }

        /// <summary>A dot facing the eye (the glow, or an alpha-blended material for a dark rim).</summary>
        private void Dot(Vector3 world, float size, Color c, Camera cam, Material mat = null)
        {
            var rot = Quaternion.LookRotation(world - cam.transform.position, Vector3.up) * Quaternion.Euler(-90f, 0, 0);
            _mpb.SetColor(ColorId, c);
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(world, rot, Vector3.one * size), mat ?? _glow, 0, null, 0, _mpb, false, false);
        }

        private void Ring(Vector3 world, float diameter, Color c, Material mat = null)
        {
            var root = _arena.Root; float up = 0.07f * Mathf.Abs(root.lossyScale.x);
            _mpb.SetColor(ColorId, c);
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(world + Vector3.up * (mat != null ? up * 0.8f : up), root.rotation, Vector3.one * diameter), mat ?? _ring, 0, null, 0, _mpb, false, false);
        }
    }
}
