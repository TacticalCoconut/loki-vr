using System.Collections.Generic;
using Loki.Effects;
using UnityEngine;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 17 (h17boss), point 23 of the Horde design proposal: the bosses' warnings on the table, drawn from the lasting effects
    /// HordeFxKind.EnemyLine .. EnemyTrail (the host's HordeSim.Bosses.cs sends each once with everything needed; this file only draws).
    /// One look for every warning (Loki's own numbers, in cells at table scale, readable from arm's length):
    ///   - the area the strike will hit, flat on the ground: a dark backing (contrast on any ground), a red fill that grows and pulses
    ///     faster as the strike nears (a lane fills from the caster to its end, a cone and a circle from the centre out, a square from
    ///     its middle), and a bright edge <see cref="EdgeCells"/> wide that turns white-hot for the last <see cref="HotSeconds"/>;
    ///   - where it is safe, in green: the gap of a ring;
    ///   - the strike itself: the shape flashes white for <see cref="FlashSeconds"/>, and the skill's own RO effect plays there;
    ///   - a pull: violet rings closing in toward the caster; a slow: a sand (trail) or stone (petrify) ring under the slowed player;
    ///   - a boss's second phase: a banner and a burst.
    /// Times are the host's (the estimated server tick, not the render-delayed one): a warning ends when the host strikes.
    /// Hooks in HordeGame.cs (marked "client wave 17 (h17boss)"): Create, OnRunStarted / LeaveArena (Clear), OnEvent, OnFxEnded, LateUpdate.
    /// </summary>
    public sealed class HordeTelegraphs
    {
        /// <summary>Loki's own numbers: the edge's width, the lift over the ground (cells), the white flash of a strike and the white-hot end.</summary>
        public const float EdgeCells = 0.14f, FillLift = 0.08f, EdgeLift = 0.11f, FlashSeconds = 0.25f, HotSeconds = 0.25f;
        /// <summary>RO effects a second at most for the strikes (a grid strikes many squares at once).</summary>
        public const int MaxEffectsPerSecond = 8;
        public static readonly Color Warn = new Color(0.98f, 0.06f, 0.05f, 1f), EdgeColor = new Color(1f, 0.24f, 0.16f, 1f), HotColor = new Color(1f, 0.97f, 0.9f, 1f),
            SafeColor = new Color(0.20f, 0.98f, 0.52f, 1f), SandColor = new Color(0.86f, 0.68f, 0.34f, 1f), StoneColor = new Color(0.78f, 0.78f, 0.84f, 1f),
            PullColor = new Color(0.78f, 0.5f, 1f, 1f), DarkColor = new Color(0.03f, 0.03f, 0.05f, 1f);

        /// <summary>The readability's "Combat effects: Normal / Large" (h17read's F1: 1.5 for Large) widens the edges; set at the merge.</summary>
        public static float Scale = 1f;
        private float _ew = 1f;
        private float EdgeW => EdgeCells * Mathf.Clamp(Scale, 0.5f, 3f) * _ew;
        /// <summary>The dark outline under every warning's edge (<see cref="OutlineWidth"/> times as wide): a crisp border on bright
        /// ground, where the bright additive edge alone washes out.</summary>
        public const float OutlineWidth = 2.8f;
        public static readonly Color OutlineColor = new Color(0.36f, 0.02f, 0.02f, 0.90f);
        /// <summary>An edge: <paramref name="build"/> once wide in the dark outline colour, then at its width in <paramref name="glow"/>.</summary>
        private void Edged(System.Action build, Color glow, float outlineK = 1f)
        {
            _ew = OutlineWidth; Begin(); build(); Flat(End(), new Color(OutlineColor.r, OutlineColor.g, OutlineColor.b, OutlineColor.a * Mathf.Clamp01(glow.a) * outlineK));
            _ew = 1f; Begin(); build(); Glow(End(), glow);
        }
        private readonly HordeGame _g;
        private Material _flat, _glow; private static Texture2D _white;
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private readonly List<Mesh> _pool = new List<Mesh>(); private int _used;
        private readonly List<Vector3> _v = new List<Vector3>(); private readonly List<Color> _c = new List<Color>(); private readonly List<int> _t = new List<int>(); private readonly List<Vector2> _uv = new List<Vector2>();
        private struct Fade { public HordeFxView Fx; public float At; }
        private readonly List<Fade> _fades = new List<Fade>();
        private struct SlowRing { public int Slot; public float Until; public Color C; }
        private readonly List<SlowRing> _slows = new List<SlowRing>();
        private float _fxWindow; private int _fxInWindow;
        private HordeArena Arena => _g.Arena;

        // counters for the log and the smoke
        /// <summary>Frames each fx kind was drawn in (index = HordeFxKind), warnings seen begin, strikes flashed, RO effects played, phases, slow rings.</summary>
        public readonly int[] Drawn = new int[32];
        public readonly int[] Seen = new int[32];
        private readonly HashSet<int> _seenIds = new HashSet<int>();
        public int Flashes, Effects, Phases, SlowRings, Strikes, MeshesLastFrame;
        public string LastPhase { get; private set; } = "";
        /// <summary>The shortest warning seen, in seconds from its start to its strike (the smoke: 1 to 1.5 s).</summary>
        public float ShortestWarning { get; private set; } = float.MaxValue;

        /// <summary>Called each frame while MY character stands where a warning will strike (its fx id, seconds to the strike): h17read's
        /// "you stand in a red area" tick and rumble (readability C5) is wired to it at the merge. Frames it was called in, for the smoke.</summary>
        public System.Action<int, float> InDanger;
        public int DangerFrames { get; private set; }

        public HordeTelegraphs(HordeGame g) { _g = g; }

        public void Clear() { _fades.Clear(); _slows.Clear(); _seenIds.Clear(); }

        // ------------------------------------------------------------------ the host's events

        /// <summary>A fault here never stops the rest of the run's drawing (HordeGame's LateUpdate goes on): logged once.</summary>
        private bool _faultLogged;
        private void Fault(System.Exception e) { if (_faultLogged) return; _faultLogged = true; Debug.LogWarning("[Horde] the bosses' warnings: " + e); }

        public void OnEvent(HordeEvent e) { try { Event(e); } catch (System.Exception x) { Fault(x); } }

        private void Event(HordeEvent e)
        {
            if (!_g.InArena) return;
            switch (e.Kind)
            {
                case HordeEventKind.BossStrike:
                {
                    Strikes++;
                    // the skill's own RO effect where it struck: along a lane, at a cone's middle, at the place of the rest
                    var kind = (AbilityKind)e.Value;
                    if (kind == AbilityKind.Line)
                    {
                        float dx = e.X2 - e.X, dy = e.Y2 - e.Y; int n = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dy * dy) / 3f), 1, 4);
                        for (int i = 1; i <= n; i++) Effect(e.DefId, e.X + dx * i / (n + 1f), e.Y + dy * i / (n + 1f));
                    }
                    else if (kind == AbilityKind.Cone) Effect(e.DefId, e.X + (e.X2 - e.X) * 0.55f, e.Y + (e.Y2 - e.Y) * 0.55f);
                    else Effect(e.DefId, e.X, e.Y);
                    break;
                }
                case HordeEventKind.BossPhase:
                {
                    Phases++;
                    var d = _g.Content?.Enemy(e.DefId);
                    string name = d != null ? HordeGame.MobName(d.MobId) : "?";
                    LastPhase = Lang.F("{0} is enraged!", name);
                    _g.Hud.ShowBanner(LastPhase, 2f, new Color(1f, 0.35f, 0.25f), 56);
                    _g.Say?.Invoke(Lang.F("Horde: {0} is enraged!", name), "ff7a6a");
                    if (Arena.Root != null) World.Fx.Burst(Arena.Root, Arena.Local(e.X, e.Y) + Vector3.up * 0.8f, new Color(1f, 0.3f, 0.2f), 3f, 36);
                    break;
                }
                case HordeEventKind.PlayerSlowed:
                {
                    SlowRings++;
                    _slows.Add(new SlowRing { Slot = e.Slot, Until = Time.time + Mathf.Clamp(e.A, 0.2f, 10f), C = e.DefId == 16 ? StoneColor : SandColor });
                    _g.Players.Pop(e.Slot, Lang.T("Slowed"), e.DefId == 16 ? StoneColor : SandColor);
                    break;
                }
            }
        }

        /// <summary>A warning the host ended (struck, or its caster gone): its shape flashes and fades (a hit one white, else it just fades).</summary>
        public void OnFxEnded(HordeFxView fx, bool hit)
        {
            if (fx == null || !HordeSim.IsBossFx(fx.Kind) || fx.Kind == HordeFxKind.EnemyTrail || fx.Kind == HordeFxKind.EnemyPull) return;
            if (fx.Kind == HordeFxKind.EnemyMark && fx.Target >= 0) return; // a following mark becomes a locked one: no flash
            if (hit) Flashes++;
            if (_fades.Count > 40) _fades.RemoveAt(0);
            _fades.Add(new Fade { Fx = fx, At = hit ? Time.time : -1f });
        }

        private void Effect(int skillId, float x, float y)
        {
            if (skillId <= 0 || _g.Fx == null || Arena.Root == null) return;
            if (Time.time > _fxWindow) { _fxWindow = Time.time + 1f; _fxInWindow = 0; }
            if (_fxInWindow >= MaxEffectsPerSecond) return;
            var ids = EffectDb.EffectIdFor(skillId, -1);
            if (ids == null || ids.Length == 0 || EffectDb.Effect(ids[0]) == null) return;
            if (_g.Fx.Spam(ids[0], 0, Arena.Local(x, y), Time.time, -1f) > 0) { _fxInWindow++; Effects++; }
        }

        // ------------------------------------------------------------------ drawing

        private void EnsureMaterials()
        {
            if (_flat != null) return;
            if (_white == null) { _white = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "horde-telegraph-white", wrapMode = TextureWrapMode.Clamp }; var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white; _white.SetPixels(px); _white.Apply(false); }
            _flat = new Material(EffectManager.StrShader) { mainTexture = _white, name = "horde-telegraph-flat" };
            _flat.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); _flat.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _glow = new Material(World.Fx.Glow) { mainTexture = _white, name = "horde-telegraph-glow" };
            // the bright parts after the flat ones (Loki/Str's queue is Transparent+25, Loki/Glow's +20): an edge over its dark outline
            _flat.renderQueue = 3025; _glow.renderQueue = 3026;
        }

        /// <param name="myPos">My own predicted place (a mark on me follows it); null = the snapshot's.</param>
        public void Draw(HordeWorldView w, HordeContent content, double now, int mySlot, Vector2? myPos) { try { DrawAll(w, content, now, mySlot, myPos); } catch (System.Exception x) { Fault(x); } }

        private void DrawAll(HordeWorldView w, HordeContent content, double now, int mySlot, Vector2? myPos)
        {
            _used = 0; MeshesLastFrame = 0;
            var root = Arena != null ? Arena.Root : null;
            if (root == null || w == null || content == null) return;
            EnsureMaterials();
            float a = w.Alpha(now);
            // the host's time (the render time runs a snapshot and a margin behind it): a warning ends when the host strikes
            double st = w.HasHistory ? w.RenderTick(now) + w.RenderDelayTicks : w.DrawTick(now);
            st = System.Math.Min(st, w.Tick + (w.Paused ? 0 : w.SnapshotEvery));
            float rate = Mathf.Max(1, w.TickRate), t = Time.time;
            foreach (var f in w.Fx)
            {
                if (!HordeSim.IsBossFx(f.Kind)) continue;
                if (_seenIds.Add(f.Id)) Seen[(int)f.Kind]++;
                Drawn[(int)f.Kind]++;
                DrawOne(w, content, f, st, rate, t, mySlot, myPos, a, -1f);
                if (mySlot >= 0 && mySlot < w.Players.Length && w.Players[mySlot].Present && !w.Players[mySlot].Fallen && Inside(w, f, st, rate, mySlot, PlayerAt(w, mySlot, mySlot, myPos, a), content.Rules.BodyRadius))
                {
                    DangerFrames++;
                    InDanger?.Invoke(f.Id, (float)(((f.Kind == HordeFxKind.EnemyGrid && st >= f.ArmTick) ? f.EndTick : f.ArmTick) - st) / rate);
                }
            }
            for (int i = _fades.Count - 1; i >= 0; i--)
            {
                var fd = _fades[i];
                float age = fd.At >= 0 ? t - fd.At : FlashSeconds;
                if (fd.At < 0) { fd.At = t - FlashSeconds * 0.5f; _fades[i] = fd; age = FlashSeconds * 0.5f; }
                if (age > FlashSeconds) { _fades.RemoveAt(i); continue; }
                DrawOne(w, content, fd.Fx, fd.Fx.EndTick, rate, t, mySlot, myPos, a, 1f - age / FlashSeconds);
            }
            for (int i = _slows.Count - 1; i >= 0; i--)
            {
                var s = _slows[i];
                if (t > s.Until || s.Slot < 0 || s.Slot >= w.Players.Length || !w.Players[s.Slot].Present) { _slows.RemoveAt(i); continue; }
                var p = PlayerAt(w, s.Slot, mySlot, myPos, a);
                Begin(); Band(p.x, p.y, 0.45f, 0.75f, 0f, 360f, s.C, s.C, FillLift); var m = End();
                float k = 0.55f + 0.25f * Mathf.Sin(t * 8f); Flat(m, new Color(s.C.r, s.C.g, s.C.b, 0.75f * k));
            }
            MeshesLastFrame = _used;
        }

        /// <summary>Whether a player at <paramref name="me"/> stands where this warning will strike (the host's own geometry: the area + the body).</summary>
        public static bool Inside(HordeWorldView w, HordeFxView f, double st, float rate, int slot, Vector2 me, float body)
        {
            switch (f.Kind)
            {
                case HordeFxKind.EnemyLine:
                {
                    if (st >= f.EndTick && f.EndTick > f.ArmTick || st >= f.ArmTick && f.EndTick <= f.ArmTick) return false;
                    float vx = f.VX / rate, vy = f.VY / rate, len = Mathf.Sqrt(vx * vx + vy * vy); if (len < 0.05f) return false;
                    float ux = vx / len, uy = vy / len, reach = f.Radius + body;
                    for (int k = 0; k < Mathf.Max(1, f.Count); k++)
                    {
                        float off = (k - (Mathf.Max(1, f.Count) - 1) / 2f) * f.Range, ax = f.X - uy * off, ay = f.Y + ux * off;
                        float tt = Mathf.Clamp01(((me.x - ax) * vx + (me.y - ay) * vy) / (len * len));
                        if ((new Vector2(ax + vx * tt, ay + vy * tt) - me).sqrMagnitude <= reach * reach) return true;
                    }
                    return false;
                }
                case HordeFxKind.EnemyCone:
                {
                    if (st >= f.ArmTick) return false;
                    float vx = f.VX / rate, vy = f.VY / rate, r = Mathf.Sqrt(vx * vx + vy * vy); var d = me - new Vector2(f.X, f.Y); float dl = d.magnitude;
                    if (dl > r + body) return false;
                    float off = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(vy, vx) * Mathf.Rad2Deg, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg));
                    return off <= f.Angle / 2f + (dl > body ? Mathf.Asin(Mathf.Min(1f, body / dl)) * Mathf.Rad2Deg : 180f);
                }
                case HordeFxKind.EnemyRing:
                {
                    var d = me - new Vector2(f.X, f.Y); float dl = d.magnitude;
                    if (f.Level == (int)AbilityShape.Inward) return st < f.ArmTick && dl <= f.Range;
                    if (st >= f.EndTick || dl > f.Range + body) return false;
                    if (st >= f.ArmTick) { float r = Mathf.Clamp01((float)(st - f.ArmTick) / Mathf.Max(1, f.EndTick - f.ArmTick)) * f.Range; if (dl < r - f.Radius - body) return false; }
                    return !(f.Count > 0 && dl > 0.01f && Mathf.Abs(Mathf.DeltaAngle(f.Angle, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg)) <= f.Count / 2f);
                }
                case HordeFxKind.EnemyPull: return st < f.EndTick && (me - new Vector2(f.X, f.Y)).sqrMagnitude <= f.Radius * f.Radius;
                case HordeFxKind.EnemyMark:
                    if (f.Target >= 0) return f.Target == slot;
                    return st < f.ArmTick && (me - new Vector2(f.X, f.Y)).sqrMagnitude <= (f.Radius + body) * (f.Radius + body);
                case HordeFxKind.EnemyGrid:
                {
                    int n = Mathf.Max(1, f.Count), mid = n / 2; float side = Mathf.Max(0.3f, f.Range), half = n * side / 2f;
                    float ox = me.x - f.X + half, oy = me.y - f.Y + half;
                    if (ox < 0 || oy < 0 || ox >= n * side || oy >= n * side) return false;
                    int i = (int)(ox / side), j = (int)(oy / side);
                    if (f.Level != (int)AbilityShape.Checker) return st < f.ArmTick && (i == mid || j == mid);
                    return st < f.ArmTick ? ((i + j) & 1) == 0 : st < f.EndTick && ((i + j) & 1) == 1;
                }
                case HordeFxKind.EnemyTeleport: return st < f.ArmTick && f.Radius > 0 && (me - new Vector2(f.X, f.Y)).sqrMagnitude <= (f.Radius + body) * (f.Radius + body);
                default: return false;
            }
        }

        private static Vector2 PlayerAt(HordeWorldView w, int slot, int mySlot, Vector2? myPos, float a)
        {
            var p = w.Players[slot];
            return slot == mySlot && myPos.HasValue ? myPos.Value : new Vector2(p.PrevX + (p.X - p.PrevX) * a, p.PrevY + (p.Y - p.PrevY) * a);
        }

        /// <summary>One warning at the host's tick <paramref name="st"/>; <paramref name="flash"/> ≥ 0: its strike's white flash (1 → 0).</summary>
        private void DrawOne(HordeWorldView w, HordeContent content, HordeFxView f, double st, float rate, float t, int mySlot, Vector2? myPos, float a, float flash)
        {
            float span = Mathf.Max(1, f.ArmTick - f.StartTick);
            float life = Mathf.Clamp01((float)(st - f.StartTick) / span);
            float toStrike = (float)(f.ArmTick - st) / rate;
            if (flash < 0 && f.ArmTick > f.StartTick && st >= f.StartTick) ShortestWarning = Mathf.Min(ShortestWarning, (f.ArmTick - f.StartTick) / rate);
            bool hot = flash < 0 && toStrike <= HotSeconds && toStrike > -0.05f;
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * Mathf.Lerp(6f, 22f, life));
            var edge = flash >= 0 ? HotColor : hot ? HotColor : EdgeColor;
            float fillA = flash >= 0 ? 0.65f * flash : 0.22f + 0.22f * life + 0.12f * pulse * life;
            var fill = flash >= 0 ? new Color(1f, 0.92f, 0.8f, fillA) : new Color(Warn.r, Warn.g, Warn.b, fillA);
            float edgeK = flash >= 0 ? flash : 1f;
            switch (f.Kind)
            {
                case HordeFxKind.EnemyLine:
                {
                    float vx = f.VX / rate, vy = f.VY / rate, half = Mathf.Max(0.2f, f.Radius), sp = f.Range;
                    float len = Mathf.Sqrt(vx * vx + vy * vy); if (len < 0.05f) break;
                    float ux = vx / len, uy = vy / len;
                    bool active = flash < 0 && st >= f.ArmTick; // a dash under way: the lane stays, its end burning
                    for (int k = 0; k < Mathf.Max(1, f.Count); k++)
                    {
                        float off = (k - (Mathf.Max(1, f.Count) - 1) / 2f) * sp;
                        float ax = f.X - uy * off, ay = f.Y + ux * off, bx = ax + vx, by = ay + vy;
                        Begin(); Strip(ax, ay, bx, by, half + 0.06f, DarkColor, DarkColor, FillLift - 0.01f); Flat(End(), new Color(DarkColor.r, DarkColor.g, DarkColor.b, 0.35f * edgeK));
                        Begin(); Strip(ax, ay, bx, by, half, Color.white, Color.white, FillLift); Flat(End(), fill);
                        if (flash < 0 && !active)
                        {
                            // the filling: from the caster to the lane's end at the strike
                            Begin(); Strip(ax, ay, ax + vx * life, ay + vy * life, half * 0.9f, new Color(1f, 0.25f, 0.15f, 0.15f), new Color(1f, 0.35f, 0.2f, 0.55f), FillLift + 0.01f); Glow(End());
                        }
                        Edged(() => { RectEdge(ax, ay, bx, by, half); }, edge * new Color(1, 1, 1, edgeK));
                        if (f.Level == (int)AbilityShape.Runners && flash < 0)
                        {
                            // the runners' direction: chevrons along the lane
                            Begin(); for (float s = 1.2f; s < len - 0.5f; s += 2.4f) Chevron(ax + ux * s, ay + uy * s, ux, uy, half * 0.8f); Glow(End(), new Color(1f, 0.6f, 0.4f, 0.8f));
                        }
                    }
                    if (f.Level == (int)AbilityShape.Dash && flash < 0)
                    {
                        Begin(); Chevron(f.X + ux * (len - 0.6f), f.Y + uy * (len - 0.6f), ux, uy, half); Glow(End(), edge);
                    }
                    break;
                }
                case HordeFxKind.EnemyCone:
                {
                    float vx = f.VX / rate, vy = f.VY / rate, r = Mathf.Sqrt(vx * vx + vy * vy); if (r < 0.05f) break;
                    float axis = Mathf.Atan2(vy, vx) * Mathf.Rad2Deg, half = Mathf.Clamp(f.Angle, 5f, 360f) / 2f;
                    Begin(); Band(f.X, f.Y, 0f, r + 0.06f, axis - half - 1f, axis + half + 1f, DarkColor, DarkColor, FillLift - 0.01f); Flat(End(), new Color(DarkColor.r, DarkColor.g, DarkColor.b, 0.35f * edgeK));
                    Begin(); Band(f.X, f.Y, 0f, r, axis - half, axis + half, Color.white, Color.white, FillLift); Flat(End(), fill);
                    if (flash < 0) { Begin(); Band(f.X, f.Y, 0f, r * life, axis - half, axis + half, new Color(1f, 0.3f, 0.2f, 0.1f), new Color(1f, 0.35f, 0.2f, 0.55f), FillLift + 0.01f); Glow(End()); }
                    Edged(() => { SectorEdge(f.X, f.Y, r, axis - half, axis + half); }, edge * new Color(1, 1, 1, edgeK));
                    if (f.Count > 0 && flash < 0)
                    {
                        // a fan: where each shot will fly
                        Begin(); for (int i = 0; i < f.Count; i++) { float ang = (f.Count == 1 ? axis : axis - half + 2f * half * i / (f.Count - 1)) * Mathf.Deg2Rad; Strip(f.X, f.Y, f.X + Mathf.Cos(ang) * r, f.Y + Mathf.Sin(ang) * r, 0.06f, new Color(1f, 0.5f, 0.3f, 0.7f), new Color(1f, 0.5f, 0.3f, 0.1f), EdgeLift); }
                        Glow(End());
                    }
                    break;
                }
                case HordeFxKind.EnemyRing:
                {
                    float gap = f.Level == (int)AbilityShape.Inward ? 360f / Mathf.Max(3, f.Count) : f.Count, gapHalf = gap / 2f, g0 = f.Angle - gapHalf, g1 = f.Angle + gapHalf;
                    if (f.Level == (int)AbilityShape.Inward)
                    {
                        float R = f.Range;
                        Edged(() => { Band(f.X, f.Y, R - EdgeW / 2f, R + EdgeW / 2f, g1, g0 + 360f, Color.white, Color.white, EdgeLift); }, edge * new Color(1, 1, 1, edgeK));
                        if (flash < 0)
                        {
                            Begin(); for (int k = 1; k < f.Count; k++) { float ang = (f.Angle + k * 360f / f.Count) * Mathf.Deg2Rad; Band(f.X + Mathf.Cos(ang) * R, f.Y + Mathf.Sin(ang) * R, 0f, 0.35f + 0.25f * life, 0f, 360f, Color.white, Color.white, EdgeLift); }
                            Flat(End(), new Color(0.55f, 0.1f, 0.55f, 0.6f + 0.3f * pulse));
                            // the way out: the gap's arc and a green wedge
                            Begin(); Band(f.X, f.Y, R * 0.6f, R + 0.6f, g0 + 4f, g1 - 4f, new Color(SafeColor.r, SafeColor.g, SafeColor.b, 0.05f), new Color(SafeColor.r, SafeColor.g, SafeColor.b, 0.5f), FillLift); Glow(End());
                        }
                        break;
                    }
                    float maxR = f.Range, band = Mathf.Max(0.3f, f.Radius);
                    if (flash < 0 && st < f.ArmTick)
                    {
                        // the warning: where the band will travel (to its reach) and the gap, in green
                        Begin(); Band(f.X, f.Y, 0f, maxR * life, g1, g0 + 360f, new Color(1f, 0.3f, 0.2f, 0.05f), new Color(1f, 0.3f, 0.2f, 0.4f), FillLift); Glow(End());
                        Edged(() => { Band(f.X, f.Y, maxR - EdgeW / 2f, maxR + EdgeW / 2f, g1, g0 + 360f, Color.white, Color.white, EdgeLift); }, edge);
                        Begin(); Band(f.X, f.Y, 0.5f, maxR, g0 + 2f, g1 - 2f, new Color(SafeColor.r, SafeColor.g, SafeColor.b, 0.1f), new Color(SafeColor.r, SafeColor.g, SafeColor.b, 0.45f), FillLift); Glow(End());
                    }
                    else
                    {
                        // the band, its outer edge at r
                        float sp2 = Mathf.Max(1, f.EndTick - f.ArmTick), r = flash >= 0 ? maxR : Mathf.Clamp((float)(st - f.ArmTick) / sp2, 0f, 1f) * maxR;
                        float r0 = Mathf.Max(0f, r - band);
                        Begin(); Band(f.X, f.Y, r0, Mathf.Max(r, 0.1f), g1, g0 + 360f, Color.white, Color.white, FillLift); Flat(End(), flash >= 0 ? fill : new Color(Warn.r, Warn.g, Warn.b, 0.75f));
                        Begin(); Band(f.X, f.Y, Mathf.Max(0f, r - EdgeW), Mathf.Max(r, 0.1f), g1, g0 + 360f, Color.white, Color.white, EdgeLift); Glow(End(), HotColor * new Color(1, 1, 1, edgeK));
                    }
                    break;
                }
                case HordeFxKind.EnemyPull:
                {
                    float R = Mathf.Max(0.5f, f.Radius), stop = Mathf.Max(0.3f, f.Angle / HordeSim.U);
                    bool pulling = st >= f.ArmTick;
                    var col = PullColor; col.a = pulling ? 0.9f : 0.45f + 0.3f * life;
                    Begin(); Band(f.X, f.Y, R - EdgeW / 2f, R + EdgeW / 2f, 0f, 360f, Color.white, Color.white, EdgeLift); Glow(End(), col);
                    // rings closing in (faster while it pulls)
                    float speed = pulling ? 1.6f : 0.6f;
                    Begin();
                    for (int k = 0; k < 3; k++) { float u = Mathf.Repeat(-t * speed / 3f + k / 3f, 1f); float rr = stop + (R - stop) * u; Band(f.X, f.Y, rr - 0.05f, rr + 0.05f, 0f, 360f, Color.white, Color.white, EdgeLift); }
                    Glow(End(), new Color(col.r, col.g, col.b, col.a * 0.6f));
                    Begin(); Band(f.X, f.Y, 0f, R, 0f, 360f, Color.white, Color.white, FillLift); Flat(End(), new Color(PullColor.r * 0.5f, PullColor.g * 0.3f, PullColor.b * 0.6f, pulling ? 0.22f : 0.1f));
                    break;
                }
                case HordeFxKind.EnemyMark:
                {
                    float x = f.X, y = f.Y, R = Mathf.Max(0.5f, f.Radius);
                    bool following = f.Target >= 0 && flash < 0;
                    if (following && f.Target < w.Players.Length && w.Players[f.Target].Present) { var p = PlayerAt(w, f.Target, mySlot, myPos, a); x = p.x; y = p.y; }
                    var col = following ? new Color(1f, 0.6f, 0.2f, 1f) : edge;
                    Begin(); Band(x, y, 0f, R + 0.06f, 0f, 360f, Color.white, Color.white, FillLift - 0.01f); Flat(End(), new Color(DarkColor.r, DarkColor.g, DarkColor.b, 0.3f * edgeK));
                    Begin(); Band(x, y, 0f, R, 0f, 360f, Color.white, Color.white, FillLift); Flat(End(), following ? new Color(1f, 0.5f, 0.15f, 0.12f + 0.08f * pulse) : fill);
                    // the countdown: a sweep around it (following), a fill from the middle (locked)
                    if (flash < 0)
                    {
                        Begin();
                        if (following) Band(x, y, R * 0.75f, R, 90f, 90f - 360f * life, new Color(1f, 0.6f, 0.2f, 0.5f), new Color(1f, 0.7f, 0.3f, 0.9f), EdgeLift);
                        else Band(x, y, 0f, R * life, 0f, 360f, new Color(1f, 0.3f, 0.2f, 0.15f), new Color(1f, 0.35f, 0.2f, 0.6f), FillLift + 0.01f);
                        Glow(End());
                    }
                    Edged(() => { Band(x, y, R - EdgeW / 2f, R + EdgeW / 2f, 0f, 360f, Color.white, Color.white, EdgeLift); }, col * new Color(1, 1, 1, edgeK));
                    break;
                }
                case HordeFxKind.EnemyGrid:
                {
                    int n = Mathf.Max(1, f.Count), mid = n / 2; float side = Mathf.Max(0.3f, f.Range), half = n * side / 2f, inset = side * 0.06f;
                    bool checker = f.Level == (int)AbilityShape.Checker;
                    float life2 = checker ? Mathf.Clamp01((float)(st - f.ArmTick) / Mathf.Max(1, f.EndTick - f.ArmTick)) : life;
                    bool firstStruck = st >= f.ArmTick;
                    float sinceFirst = (float)(st - f.ArmTick) / rate;
                    for (int pass = 0; pass < 2; pass++)
                    {
                        // pass 0: the squares of the first strike (checker even / the cross), pass 1: the second strike's (checker odd)
                        if (pass == 1 && !checker) break;
                        Color c; bool outline = false, waiting = false;
                        if (pass == 0)
                        {
                            if (flash >= 0) { if (checker) continue; c = fill; }
                            else if (!firstStruck) { c = new Color(Warn.r, Warn.g, Warn.b, 0.18f + 0.3f * life + 0.1f * pulse * life); outline = true; }
                            else if (sinceFirst < FlashSeconds) c = new Color(1f, 0.92f, 0.8f, 0.65f * (1f - sinceFirst / FlashSeconds));
                            else continue;
                        }
                        else
                        {
                            if (flash >= 0) c = fill;
                            else if (!firstStruck) { waiting = true; outline = true; c = new Color(Warn.r, Warn.g, Warn.b, 0.08f + 0.06f * pulse); }
                            else { c = new Color(Warn.r, Warn.g, Warn.b, 0.18f + 0.3f * life2 + 0.1f * pulse * life2); outline = true; }
                        }
                        Begin();
                        for (int i = 0; i < n; i++)
                            for (int j = 0; j < n; j++)
                            {
                                bool mine = checker ? ((i + j) & 1) == pass : i == mid || j == mid;
                                if (mine) Square(f.X - half + i * side + inset, f.Y - half + j * side + inset, side - 2 * inset);
                            }
                        Flat(End(), c);
                        if (!outline) continue;
                        bool hotNow = (pass == 0 ? f.ArmTick : f.EndTick) - st <= HotSeconds * rate;
                        int ps = pass;
                        Edged(() =>
                        {
                            for (int i = 0; i < n; i++)
                                for (int j = 0; j < n; j++)
                                {
                                    bool mine = checker ? ((i + j) & 1) == ps : i == mid || j == mid;
                                    if (mine) SquareEdge(f.X - half + i * side + inset, f.Y - half + j * side + inset, side - 2 * inset);
                                }
                        }, waiting ? new Color(1f, 0.4f, 0.3f, 0.45f) : hotNow ? HotColor : EdgeColor, waiting ? 0.4f : 1f);
                    }
                    break;
                }
                case HordeFxKind.EnemyTeleport:
                {
                    var def = f.OwnerEnemy >= 0 && f.OwnerEnemy < w.Enemies.Length ? content.Enemy(w.Enemies[f.OwnerEnemy].DefId) : null;
                    float body = def != null ? Mathf.Max(0.5f, def.Radius) : 0.8f;
                    float fromX = f.X + f.VX / rate, fromY = f.Y + f.VY / rate;
                    bool jump = f.Level == (int)AbilityShape.Jump;
                    if (flash < 0)
                    {
                        // its shadow (growing) where it comes, a dotted trail from where it is
                        Begin(); Band(f.X, f.Y, 0f, body * (0.35f + 0.65f * life), 0f, 360f, Color.white, Color.white, FillLift); Flat(End(), new Color(DarkColor.r, DarkColor.g, DarkColor.b, 0.45f + 0.3f * life));
                        Begin(); float dx = f.X - fromX, dy = f.Y - fromY, dl = Mathf.Sqrt(dx * dx + dy * dy);
                        for (float s = 0.6f; s < dl - 0.6f; s += 0.9f) Band(fromX + dx / dl * s, fromY + dy / dl * s, 0f, 0.12f, 0f, 360f, Color.white, Color.white, EdgeLift);
                        Glow(End(), jump ? new Color(1f, 0.45f, 0.3f, 0.7f) : new Color(PullColor.r, PullColor.g, PullColor.b, 0.7f));
                        if (!jump) { Begin(); Band(fromX, fromY, body, body + 0.12f, 0f, 360f, Color.white, Color.white, EdgeLift); Band(f.X, f.Y, body, body + 0.12f, 0f, 360f, Color.white, Color.white, EdgeLift); Glow(End(), new Color(PullColor.r, PullColor.g, PullColor.b, 0.5f + 0.4f * pulse)); }
                    }
                    if (f.Radius > 0f)
                    {
                        float R = f.Radius;
                        Begin(); Band(f.X, f.Y, 0f, R, 0f, 360f, Color.white, Color.white, FillLift); Flat(End(), fill);
                        if (flash < 0) { Begin(); Band(f.X, f.Y, 0f, R * life, 0f, 360f, new Color(1f, 0.3f, 0.2f, 0.15f), new Color(1f, 0.35f, 0.2f, 0.6f), FillLift + 0.01f); Glow(End()); }
                        Edged(() => { Band(f.X, f.Y, R - EdgeW / 2f, R + EdgeW / 2f, 0f, 360f, Color.white, Color.white, EdgeLift); }, edge * new Color(1, 1, 1, edgeK));
                    }
                    break;
                }
                case HordeFxKind.EnemyTrail:
                {
                    float R = Mathf.Max(0.3f, f.Radius), left = (float)(f.EndTick - st) / rate;
                    float k = Mathf.Clamp01(left / 1f); // it fades in its last second
                    Begin(); Band(f.X, f.Y, 0f, R, 0f, 360f, Color.white, Color.white, FillLift); Flat(End(), new Color(SandColor.r * 0.8f, SandColor.g * 0.7f, SandColor.b * 0.5f, 0.42f * k));
                    Begin(); Band(f.X, f.Y, R - 0.06f, R + 0.06f, 0f, 360f, Color.white, Color.white, EdgeLift); Glow(End(), new Color(SandColor.r, SandColor.g, SandColor.b, 0.55f * k));
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ mesh building (map-local cells; heights from the arena's ground)

        private void Begin() { _v.Clear(); _c.Clear(); _t.Clear(); _uv.Clear(); }

        private Mesh End()
        {
            if (_v.Count == 0) return null;
            while (_pool.Count <= _used) _pool.Add(new Mesh { name = "horde-telegraph" });
            var m = _pool[_used++];
            m.Clear();
            m.SetVertices(_v); m.SetColors(_c); m.SetUVs(0, _uv); m.SetTriangles(_t, 0);
            m.RecalculateBounds();
            return m;
        }

        private void Flat(Mesh m, Color c)
        {
            if (m == null) return;
            _mpb.SetColor(ColorId, c);
            Graphics.DrawMesh(m, Arena.Root.localToWorldMatrix, _flat, 0, null, 0, _mpb, false, false);
        }

        private void Glow(Mesh m) => Glow(m, Color.white);
        private void Glow(Mesh m, Color tint)
        {
            if (m == null) return;
            _mpb.SetColor(ColorId, tint);
            Graphics.DrawMesh(m, Arena.Root.localToWorldMatrix, _glow, 0, null, 0, _mpb, false, false);
        }

        private int Vert(float x, float y, float lift, Color c)
        {
            _v.Add(new Vector3(x, Arena.Height(x, y) + lift, y)); _c.Add(c); _uv.Add(new Vector2(0.5f, 0.5f));
            return _v.Count - 1;
        }

        /// <summary>A band between radii r0 and r1 over the arc a0 → a1 (degrees, either way; r0 0 = a filled sector / disc).</summary>
        private void Band(float cx, float cy, float r0, float r1, float a0, float a1, Color cIn, Color cOut, float lift)
        {
            float span = a1 - a0; if (Mathf.Abs(span) < 0.01f || r1 <= r0) return;
            int seg = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(span) / 6f * Mathf.Clamp(r1 / 4f, 1f, 3f)), 3, 180);
            int b = _v.Count;
            for (int s = 0; s <= seg; s++)
            {
                float ang = (a0 + span * s / seg) * Mathf.Deg2Rad, ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                Vert(cx + ca * r0, cy + sa * r0, lift, cIn); Vert(cx + ca * r1, cy + sa * r1, lift, cOut);
            }
            for (int s = 0; s < seg; s++) { int o = b + s * 2; _t.Add(o); _t.Add(o + 1); _t.Add(o + 3); _t.Add(o); _t.Add(o + 3); _t.Add(o + 2); }
        }

        /// <summary>A strip <paramref name="half"/> wide to each side of the segment a → b, coloured c0 at a to c1 at b (a cell a step: it follows the ground).</summary>
        private void Strip(float ax, float ay, float bx, float by, float half, Color c0, Color c1, float lift)
        {
            float dx = bx - ax, dy = by - ay, len = Mathf.Sqrt(dx * dx + dy * dy); if (len < 1e-3f || half <= 0f) return;
            float nx = -dy / len * half, ny = dx / len * half;
            int seg = Mathf.Clamp(Mathf.CeilToInt(len), 1, 128), b = _v.Count;
            for (int s = 0; s <= seg; s++)
            {
                float u = s / (float)seg, px = ax + dx * u, py = ay + dy * u; var col = Color.Lerp(c0, c1, u);
                Vert(px + nx, py + ny, lift, col); Vert(px - nx, py - ny, lift, col);
            }
            for (int s = 0; s < seg; s++) { int o = b + s * 2; _t.Add(o); _t.Add(o + 1); _t.Add(o + 3); _t.Add(o); _t.Add(o + 3); _t.Add(o + 2); }
        }

        private void RectEdge(float ax, float ay, float bx, float by, float half)
        {
            float dx = bx - ax, dy = by - ay, len = Mathf.Sqrt(dx * dx + dy * dy); if (len < 1e-3f) return;
            float nx = -dy / len * half, ny = dx / len * half, e = EdgeW / 2f;
            Strip(ax + nx, ay + ny, bx + nx, by + ny, e, Color.white, Color.white, EdgeLift);
            Strip(ax - nx, ay - ny, bx - nx, by - ny, e, Color.white, Color.white, EdgeLift);
            Strip(ax + nx, ay + ny, ax - nx, ay - ny, e, Color.white, Color.white, EdgeLift);
            Strip(bx + nx, by + ny, bx - nx, by - ny, e, Color.white, Color.white, EdgeLift);
        }

        private void SectorEdge(float cx, float cy, float r, float a0, float a1)
        {
            float e = EdgeW / 2f;
            Band(cx, cy, r - e, r + e, a0, a1, Color.white, Color.white, EdgeLift);
            if (a1 - a0 < 359f)
                foreach (float ang in new[] { a0, a1 }) { float rad = ang * Mathf.Deg2Rad; Strip(cx, cy, cx + Mathf.Cos(rad) * r, cy + Mathf.Sin(rad) * r, e, Color.white, Color.white, EdgeLift); }
        }

        private void Square(float x0, float y0, float s)
        {
            int a = Vert(x0, y0, FillLift, Color.white), b = Vert(x0 + s, y0, FillLift, Color.white), c = Vert(x0 + s, y0 + s, FillLift, Color.white), d = Vert(x0, y0 + s, FillLift, Color.white);
            _t.Add(a); _t.Add(c); _t.Add(b); _t.Add(a); _t.Add(d); _t.Add(c);
        }

        private void SquareEdge(float x0, float y0, float s)
        {
            float e = EdgeW / 2f;
            Strip(x0, y0, x0 + s, y0, e, Color.white, Color.white, EdgeLift); Strip(x0 + s, y0, x0 + s, y0 + s, e, Color.white, Color.white, EdgeLift);
            Strip(x0 + s, y0 + s, x0, y0 + s, e, Color.white, Color.white, EdgeLift); Strip(x0, y0 + s, x0, y0, e, Color.white, Color.white, EdgeLift);
        }

        /// <summary>A "&gt;" pointing along (ux, uy) at (x, y).</summary>
        private void Chevron(float x, float y, float ux, float uy, float w)
        {
            float nx = -uy, ny = ux, e = EdgeW / 2f, back = w * 0.8f;
            Strip(x - ux * back + nx * w, y - uy * back + ny * w, x, y, e, Color.white, Color.white, EdgeLift + 0.01f);
            Strip(x - ux * back - nx * w, y - uy * back - ny * w, x, y, e, Color.white, Color.white, EdgeLift + 0.01f);
        }
    }
}
