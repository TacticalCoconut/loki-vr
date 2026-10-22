using System.Collections.Generic;
using System.Text;
using Loki.Sprites;
using Loki.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 17 (h17pass), design 2 / point 22 of design notes ("passives you can see and play around"):
    /// the BODY of every passive family on and around its runner (Horde/Core/HordeSim.Passives.cs keeps the rules, the snapshot carries
    /// each runner's family states: HordePlayerView.Procs; a trigger that goes off is a HordeEventKind.Proc event). What is drawn, by
    /// ProcBody (the table's words): an after-image where a hit was dodged and a puff ring; a small monster circling the runner and a
    /// flash on the heavy hit; a shell under the runner (and a strip under the HUD's HP bar) that cracks; green motes rising and a ring
    /// at the feet that fills; a star at each critical hit; trails behind the runner's shots and a ring on a marked enemy; a wider,
    /// brighter swing and a flash on a full swing; a blue coat that dims as it soaks; dust behind the walk (a Peco Peco for a Knight with
    /// Riding: <see cref="DrawnJob"/>) and a glow when the dash is ready; the pickup reach on the ground, glowing EXP gems and a pulse
    /// ring; an icon over the bane's enemies; the runner's own zones pulsing; a glowing hand; a red aura; a mist; a slash; a shield.
    /// Counters above the head show a counter's stacks (dodges, crits). The HUD shows a meter over every passive's icon (blue filling,
    /// gold ready, green on). Allies' bodies are drawn paler and cyan-tinted (the readability proposal's B5), the pickup ring, the gems'
    /// glow and the bane's icons only for my own runner. All drawing is Graphics.DrawMesh: no GameObject per thing.
    /// Every size and time here is Loki's own, in cells (1 cell ≈ 2 cm at the table's scale).
    /// </summary>
    public sealed class HordePassivesDraw
    {
        /// <summary>The run's tables (the Peco's look needs them: <see cref="DrawnJob"/>); set by HordeGame.</summary>
        public static HordeContent Content;
        private readonly HordeSprites _sprites;
        private readonly HordeArena _arena;
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private Mesh _quad; private Material _glow, _ring, _thinRing, _disc;

        // ---- counters (the smoke, the log)
        /// <summary>Frames each body was drawn in (by ProcBody), proc events seen (by ProcKind), and the transient effects shown.</summary>
        public readonly int[] BodyFrames = new int[32], ProcEvents = new int[32];
        public int SpritesDrawn, AfterImages, StarsShown, MarksShown, SlicesWidened, ZonePulses, MetersShown, MetersReady, GemsGlowing, IconsShown, DrawCalls, Transients;
        public string LastProc { get; private set; } = "";

        private enum Tk : byte { Ring, Ghost, Star, Flash, Crack, Slash, Dust, ArcLine }
        private struct Tr { public Tk Kind; public Vector2 Pos, Dir; public float Start, Life, Size, Size2; public Color C; public int Owner; }
        private readonly List<Tr> _tr = new List<Tr>();
        public const int MaxTransients = 160;
        private readonly Dictionary<int, (int serial, float until)> _marks = new Dictionary<int, (int, float)>();
        private readonly float[] _dustAt = new float[HordeLimits.MaxPlayers];
        private readonly Vector2[] _pos = new Vector2[HordeLimits.MaxPlayers];
        private readonly List<Fam> _fams = new List<Fam>();
        private struct Fam { public ProcKind Kind; public PassiveProcDef Look; public int PassiveId, Level; public HordeProcView State; }

        // Client-side event fallback counters so HitCount / CritCount / DodgeCounter / TimerCharge always count down smoothly
        private int _clientHits, _clientCrits, _clientDodges;
        private bool _clientHitReady;
        private readonly Dictionary<ProcKind, float> _clientProcSince = new Dictionary<ProcKind, float>();

        // Repositionable overhead proc counter strip above the player character
        public static Vector3 OverheadLocalOffset = Vector3.zero;
        public static float OverheadScale = 1f;
        public static HordePassivesDraw ActiveInstance { get; private set; }
        private RectTransform _overRt;
        private Image _overPlacingRim;
        private readonly List<(RectTransform pill, Image bg, Image icon, Text txt)> _overPills = new List<(RectTransform, Image, Image, Text)>();

        public bool OverheadVisible => _overRt != null && _overRt.gameObject.activeSelf;
        public Vector3 OverheadPosition => _overRt != null ? _overRt.position : Vector3.zero;
        public (Vector3 c, Vector3 r, Vector3 u) OverheadFrameWorld
        {
            get
            {
                if (_overRt == null) return (Vector3.zero, Vector3.right * 0.05f, Vector3.up * 0.02f);
                var s = _overRt.sizeDelta * 0.5f;
                return (_overRt.position, _overRt.TransformVector(new Vector3(s.x, 0, 0)), _overRt.TransformVector(new Vector3(0, s.y, 0)));
            }
        }
        public void CarryOverheadTo(Vector3 worldPos, float scale)
        {
            OverheadScale = Mathf.Clamp(scale, VR.Comfort.PlaceMinScale, VR.Comfort.PlaceMaxScale);
            if (_arena?.Root == null) return;
            var cam = Camera.main;
            var my = _pos[Mathf.Clamp(_lastMySlot, 0, _pos.Length - 1)];
            float k = Mathf.Max(0.001f, Mathf.Abs(_arena.Root.lossyScale.x));
            var baseWorld = _arena.Root.TransformPoint(_arena.Local(my.x, my.y)) + Vector3.up * (3.65f * k);
            var delta = (worldPos - baseWorld) / k;
            var yawInv = cam != null ? Quaternion.Inverse(Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f)) : Quaternion.identity;
            OverheadLocalOffset = yawInv * delta;
        }
        public void SetOverheadPlacingLook(bool lit)
        {
            if (_overPlacingRim != null) _overPlacingRim.gameObject.SetActive(lit);
        }
        public static void ResetOverheadPlace()
        {
            OverheadLocalOffset = Vector3.zero;
            OverheadScale = 1f;
        }
        private int _lastMySlot;

        public HordePassivesDraw(HordeSprites sprites, HordeArena arena) { _sprites = sprites; _arena = arena; ActiveInstance = this; }

        public void Clear()
        {
            _tr.Clear(); _marks.Clear();
            for (int i = 0; i < _dustAt.Length; i++) _dustAt[i] = 0f;
            _clientHits = 0; _clientCrits = 0; _clientDodges = 0; _clientHitReady = false;
            _clientProcSince.Clear();
            if (_overRt != null) _overRt.gameObject.SetActive(false);
        }

        // =============================================================================================== families of a runner

        /// <summary>The runner's families in its passives' order (the first passive of each kind gives the look), with their states.</summary>
        private void Families(HordePlayerView p, HordeContent c, List<Fam> into, bool isMe = false)
        {
            into.Clear();
            if (c == null) return;
            foreach (var o in p.Passives)
            {
                var pd = c.Passive(o.DefId);
                if (pd?.Proc == null || pd.Proc.Kind == ProcKind.None) continue;
                bool seen = false; foreach (var f in into) if (f.Kind == pd.Proc.Kind) { seen = true; break; }
                if (seen) continue;
                int lv = Mathf.Clamp(o.Level, 1, Mathf.Max(1, pd.Levels.Length));
                var st = new HordeProcView { Kind = pd.Proc.Kind };
                bool found = false;
                foreach (var s in p.Procs) if (s.Kind == pd.Proc.Kind) { st = s; found = true; break; }
                if (!found && isMe)
                {
                    st = SynthesizeClientProc(pd.Proc, lv);
                }
                into.Add(new Fam { Kind = pd.Proc.Kind, Look = pd.Proc, PassiveId = pd.Id, Level = lv, State = st });
            }
        }

        private HordeProcView SynthesizeClientProc(PassiveProcDef d, int lv)
        {
            var st = new HordeProcView { Kind = d.Kind };
            int need = Mathf.Max(1, d.Count);
            switch (d.Kind)
            {
                case ProcKind.HitCount:
                    st.Stacks = (byte)Mathf.Clamp(_clientHits % need, 0, need - 1);
                    if (_clientHitReady) st.Flags |= HordeProcFlags.Ready;
                    st.Charge = (byte)Mathf.RoundToInt(255f * (st.Ready ? 1f : st.Stacks / (float)need));
                    break;
                case ProcKind.CritCount:
                    st.Stacks = (byte)Mathf.Clamp(_clientCrits % need, 0, need - 1);
                    st.Charge = (byte)Mathf.RoundToInt(255f * (st.Stacks / (float)need));
                    break;
                case ProcKind.DodgeCounter:
                    st.Stacks = (byte)Mathf.Clamp(_clientDodges % need, 0, need - 1);
                    st.Charge = (byte)Mathf.RoundToInt(255f * (st.Stacks / (float)need));
                    break;
                case ProcKind.TimerCharge:
                case ProcKind.Shell:
                case ProcKind.MagnetPulse:
                case ProcKind.Stride:
                case ProcKind.IdleCharge:
                case ProcKind.BossBlock:
                case ProcKind.Coat:
                case ProcKind.Cloak:
                case ProcKind.Gangster:
                {
                    float total = Mathf.Max(0.5f, PassiveProcDef.At(d.Seconds, d.SecondsPerLevel, lv));
                    if (!_clientProcSince.TryGetValue(d.Kind, out float since)) { since = Time.time; _clientProcSince[d.Kind] = since; }
                    float elapsed = Time.time - since;
                    if (d.Kind == ProcKind.MagnetPulse && elapsed >= total) { since = Time.time; _clientProcSince[d.Kind] = since; elapsed = 0f; }
                    float frac = Mathf.Clamp01(elapsed / total);
                    if (frac >= 0.999f) st.Flags |= HordeProcFlags.Ready;
                    st.Charge = (byte)Mathf.RoundToInt(255f * frac);
                    break;
                }
            }
            return st;
        }

        private static bool Has(HordePlayerView p, HordeContent c, ProcBody body)
        {
            if (c == null) return false;
            foreach (var o in p.Passives) { var pd = c.Passive(o.DefId); if (pd?.Proc != null && pd.Proc.Body == body) return true; }
            return false;
        }

        /// <summary>The job a runner is drawn as: a Knight (or Crusader) whose passives include a Peco body rides a Peco Peco (rAthena's
        /// JOB_KNIGHT2 13 / JOB_CRUSADER2 21: the install's mounted sprites).</summary>
        public static int DrawnJob(HordePlayerView p)
        {
            if (p == null) return 0;
            int job = p.JobId;
            if ((job == 7 || job == 14) && Has(p, Content, ProcBody.Peco)) return job == 7 ? 13 : 21;
            return job;
        }

        // =============================================================================================== events

        /// <summary>Every event of the run (HordeGame.OnEvent): the Proc events, the dodges, the criticals, the swings, the aura pulses.</summary>
        public void OnEvent(HordeEvent e, HordeContent c, HordeWorldView w, int mySlot)
        {
            if (c == null || w == null) return;
            var runner = e.Slot >= 0 && e.Slot < w.Players.Length ? w.Players[e.Slot] : null;
            switch (e.Kind)
            {
                case HordeEventKind.Proc:
                {
                    var pd = c.Passive(e.DefId); var look = pd?.Proc;
                    if (look == null) return;
                    ProcEvents[(int)look.Kind & 31]++;
                    LastProc = look.Kind + " " + e.Value;
                    bool mine = e.Slot == mySlot; int lv = LevelOf(runner, e.DefId);
                    if (mine)
                    {
                        _clientProcSince[look.Kind] = Time.time;
                        if (look.Kind == ProcKind.HitCount) { _clientHits = 0; _clientHitReady = false; }
                        else if (look.Kind == ProcKind.CritCount) _clientCrits = 0;
                        else if (look.Kind == ProcKind.DodgeCounter) _clientDodges = 0;
                    }
                    var at = new Vector2(e.X, e.Y);
                    switch (look.Kind)
                    {
                        case ProcKind.DodgeCounter: Add(Tk.Ring, at, Col(new Color(0.6f, 0.95f, 1f), mine), 0.35f, Mathf.Max(1f, PassiveProcDef.At(look.Area, look.AreaPerLevel, lv)), e.Slot); break;
                        case ProcKind.HitCount: Add(Tk.Flash, at, Col(new Color(1f, 0.75f, 0.35f), mine), 0.25f, 0.75f, e.Slot); Add(Tk.Ring, at, Col(new Color(1f, 0.8f, 0.4f), mine), 0.25f, 0.9f, e.Slot); break;
                        case ProcKind.Shell: Add(Tk.Crack, at, Col(new Color(1f, 0.8f, 0.35f), mine), 0.45f, 0.85f, e.Slot); break;
                        case ProcKind.StillBoost: case ProcKind.MoveBoost: case ProcKind.KillHeal: Add(Tk.Ring, at, Col(new Color(0.45f, 1f, 0.5f), mine), 0.4f, 0.95f, e.Slot); break;
                        case ProcKind.CritCount: case ProcKind.Bane: Add(Tk.Flash, at, Col(new Color(0.5f, 1f, 0.45f), mine), 0.3f, 0.65f, e.Slot); break;
                        case ProcKind.Mark:
                            if (e.Target >= 0 && e.Target < w.Enemies.Length) { _marks[e.Target] = (w.Enemies[e.Target].Serial, Time.time + Mathf.Max(0.5f, e.Value / (float)Mathf.Max(1, w.TickRate))); MarksShown++; }
                            break;
                        case ProcKind.FullSwing: case ProcKind.OwnZone: Add(Tk.Ring, at, Col(new Color(1f, 0.85f, 0.4f), mine), 0.3f, 1.15f, e.Slot); break;
                        case ProcKind.Coat:
                            if ((e.Flags & HordeEventFlags.Dot) != 0) Add(Tk.Flash, at, Col(new Color(0.5f, 0.75f, 1f), mine), 0.22f, 0.75f, e.Slot);
                            else Add(Tk.Ring, at, Col(new Color(0.45f, 0.7f, 1f), mine), 0.45f, 1.0f, e.Slot);
                            break;
                        case ProcKind.Stride: case ProcKind.IdleCharge: case ProcKind.TimerCharge: Add(Tk.Flash, at, Col(new Color(1f, 0.95f, 0.6f), mine), 0.25f, 0.7f, e.Slot); break;
                        case ProcKind.MagnetPulse: Add(Tk.Ring, at, Col(new Color(0.55f, 1f, 0.6f), mine), 0.5f, Mathf.Max(1f, PassiveProcDef.At(look.Area, look.AreaPerLevel, lv)), e.Slot); break;
                        case ProcKind.DodgeReady: Add(Tk.Ring, at, Col(new Color(0.6f, 0.95f, 1f), mine), 0.25f, 0.85f, e.Slot); break;
                        case ProcKind.LowHpBoost: Add(Tk.Ring, at, Col(new Color(1f, 0.3f, 0.25f), mine), 0.45f, 1.15f, e.Slot); break;
                        case ProcKind.LowHpHide: case ProcKind.Cloak: case ProcKind.Gangster: Add(Tk.Ring, at, Col(new Color(0.55f, 0.55f, 0.6f), mine), 0.5f, 1.1f, e.Slot); break;
                        case ProcKind.HurtCounter:
                        {
                            var from = runner != null ? new Vector2(runner.X, runner.Y) : at;
                            var tr = new Tr { Kind = Tk.Slash, Pos = at, Dir = (at - from).sqrMagnitude > 1e-4f ? (at - from).normalized : Vector2.right, Start = Time.time, Life = 0.25f, Size = 1.1f, C = Col(new Color(1f, 0.85f, 0.8f), mine), Owner = e.Slot };
                            Push(tr); break;
                        }
                        case ProcKind.Reflect: case ProcKind.BossBlock: Add(Tk.Ring, runner != null ? new Vector2(runner.X, runner.Y) : at, Col(new Color(0.6f, 0.8f, 1f), mine), 0.3f, 0.95f, e.Slot); break;
                        case ProcKind.KillDrop: Add(Tk.Flash, at, Col(new Color(1f, 0.9f, 0.5f), mine), 0.25f, 0.65f, e.Slot); break;
                    }
                    break;
                }
                case HordeEventKind.PlayerHit:
                    if (e.Slot == mySlot)
                    {
                        if ((e.Flags & HordeEventFlags.Dodged) != 0) _clientDodges++;
                        else { _clientDodges = 0; _clientProcSince[ProcKind.Shell] = Time.time; }
                    }
                    // an after-image where a hit was dodged (the dodge families)
                    if ((e.Flags & HordeEventFlags.Dodged) != 0 && runner != null && runner.Present && Has(runner, c, ProcBody.AfterImage))
                    {
                        var p = e.Slot == mySlot ? _pos[e.Slot] : new Vector2(runner.X, runner.Y);
                        var off = new Vector2(Random.Range(-0.4f, 0.4f), Random.Range(-0.4f, 0.4f));
                        Add(Tk.Ghost, p + off, Col(new Color(0.55f, 0.9f, 1f), e.Slot == mySlot), 0.4f, 0.7f, e.Slot); AfterImages++;
                    }
                    break;
                case HordeEventKind.Hit:
                    if (e.Slot == mySlot && (e.Flags & HordeEventFlags.Dot) == 0)
                    {
                        if (_clientHitReady) { _clientHitReady = false; _clientHits = 0; }
                        else
                        {
                            _clientHits++;
                            if (runner != null)
                                foreach (var o in runner.Passives)
                                {
                                    var pd = c.Passive(o.DefId);
                                    if (pd?.Proc != null && pd.Proc.Kind == ProcKind.HitCount && _clientHits >= Mathf.Max(1, pd.Proc.Count))
                                    { _clientHitReady = true; _clientHits = 0; break; }
                                }
                        }
                        if ((e.Flags & HordeEventFlags.Crit) != 0) _clientCrits++;
                        var wd = c.Weapon(e.DefId);
                        if (wd != null && wd.Class == DamageClass.Magic)
                        {
                            if (_clientProcSince.TryGetValue(ProcKind.TimerCharge, out float ts) && Time.time - ts >= 6f)
                                _clientProcSince[ProcKind.TimerCharge] = Time.time;
                        }
                    }
                    if ((e.Flags & HordeEventFlags.Crit) != 0 && runner != null && Has(runner, c, ProcBody.Stars)) { Add(Tk.Star, new Vector2(e.X, e.Y), Col(new Color(1f, 0.95f, 0.45f), e.Slot == mySlot), 0.35f, 0.75f, e.Slot); StarsShown++; }
                    break;
                case HordeEventKind.Swing:
                    // the melee masteries: the swing drawn wider and brighter (an arc line past the slice's reach)
                    if (runner != null && Has(runner, c, ProcBody.Arc) && e.C > 0.05f)
                    {
                        Push(new Tr { Kind = Tk.ArcLine, Pos = new Vector2(e.X, e.Y), Dir = new Vector2(e.A, e.B), Start = Time.time, Life = 0.3f, Size = e.C * 1.12f, C = Col(new Color(1f, 0.9f, 0.5f), e.Slot == mySlot), Owner = e.Slot });
                        SlicesWidened++;
                    }
                    break;
                case HordeEventKind.Nova:
                    // the zone family: a runner's own aura pulse is drawn as a ring to its radius
                    if (runner != null && e.A > 0.2f && Has(runner, c, ProcBody.ZonePulse) && c.Weapon(e.DefId)?.Kind == WeaponKind.Aura)
                    { Add(Tk.Ring, new Vector2(e.X, e.Y), Col(new Color(1f, 0.85f, 0.45f), e.Slot == mySlot), 0.45f, e.A, e.Slot); ZonePulses++; }
                    break;
            }
        }

        private static int LevelOf(HordePlayerView p, int passiveId) { if (p != null) foreach (var o in p.Passives) if (o.DefId == passiveId) return Mathf.Max(1, o.Level); return 1; }

        /// <summary>An ally's colour: paler, cyan-tinted, and very faint (the readability proposal's B5); mine: as given.</summary>
        private static Color Col(Color c, bool mine) => mine ? c : Color.Lerp(c, new Color(0.75f, 0.95f, 1f), 0.35f) * new Color(1f, 1f, 1f, 0.18f);

        private void Add(Tk kind, Vector2 pos, Color c, float life, float size, int owner) => Push(new Tr { Kind = kind, Pos = pos, Start = Time.time, Life = life, Size = size, C = c, Owner = owner });
        private void Push(Tr t) { if (_tr.Count >= MaxTransients) _tr.RemoveAt(0); _tr.Add(t); Transients++; }

        // =============================================================================================== drawing

        private void EnsureMeshes()
        {
            if (_quad != null) return;
            _quad = new Mesh { name = "horde-pass-quad" };
            _quad.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) };
            _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            _quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _quad.RecalculateBounds(); _quad.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            _glow = new Material(World.Fx.Glow) { mainTexture = World.Fx.DotTexture(), name = "horde-pass-dot" };
            _ring = new Material(World.Fx.Glow) { mainTexture = World.Fx.RingTexture(), name = "horde-pass-ring" };
            _thinRing = new Material(World.Fx.Glow) { mainTexture = ThinRing(), name = "horde-pass-thinring" };
            _disc = new Material(Effects.EffectManager.StrShader) { mainTexture = World.Fx.DotTexture(), name = "horde-pass-disc" };
            _disc.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); _disc.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        private static Texture2D _thin;
        private static Texture2D ThinRing()
        {
            if (_thin != null) return _thin;
            const int n = 128; _thin = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "horde-pass-thinring" };
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                _thin.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1f - Mathf.Abs(d - 0.93f) * 24f)));
            }
            _thin.Apply(true); return _thin;
        }

        private readonly List<ProcKind> _procSinceKeys = new List<ProcKind>();

        /// <summary>Every frame (HordeGame.LateUpdate): each runner's bodies, the marks, the transient effects.</summary>
        public void Draw(HordeWorldView w, HordeContent c, double now, int mySlot, Vector2 myPos)
        {
            DrawCalls = 0;
            var root = _arena.Root; var cam = Camera.main;
            if (root == null || cam == null || w == null || c == null) return;
            EnsureMeshes();
            if (HordeGame.IsPaused(w) && Time.deltaTime > 0f && _clientProcSince.Count > 0)
            {
                _procSinceKeys.Clear();
                foreach (var kv in _clientProcSince) _procSinceKeys.Add(kv.Key);
                for (int ki = 0; ki < _procSinceKeys.Count; ki++)
                {
                    var pk = _procSinceKeys[ki];
                    _clientProcSince[pk] += Time.deltaTime;
                }
            }
            float k = Mathf.Abs(root.lossyScale.x), t = Time.time;
            float a = w.Alpha(now);
            for (int i = 0; i < w.Players.Length; i++)
            {
                var p = w.Players[i];
                if (!p.Present) continue;
                _pos[i] = i == mySlot ? myPos : new Vector2(p.PrevX + (p.X - p.PrevX) * a, p.PrevY + (p.Y - p.PrevY) * a);
            }
            _lastMySlot = mySlot;
            bool myOverheadUpdated = false;
            float drawTick = w.DrawTick(now); float rate = Mathf.Max(1, w.TickRate);
            for (int i = 0; i < w.Players.Length; i++)
            {
                var p = w.Players[i];
                if (!p.Present || p.Fallen) continue;
                bool mine = i == mySlot;
                Families(p, c, _fams, mine);
                var pos = _pos[i];
                var feet = root.TransformPoint(_arena.Local(pos.x, pos.y));
                if (mine)
                {
                    UpdateOverheadCounters(c, _fams, feet, k, cam);
                    myOverheadUpdated = true;
                }
                if (_fams.Count == 0) continue;
                int counterDots = 0; Color counterCol = Color.white;
                foreach (var f in _fams)
                {
                    var s = f.State; var look = f.Look; var body = look.Body;
                    switch (body)
                    {
                        case ProcBody.AfterImage:
                            if (f.Kind == ProcKind.DodgeCounter && s.Stacks > 0) { counterDots = s.Stacks; counterCol = new Color(0.6f, 0.95f, 1f); }
                            Mark(body); break;
                        case ProcBody.Orbiter:
                        {
                            // a small monster (or item, or spark) circling at 1.1 cells, a turn every 2.4 s; it glows as the count fills
                            float ang = (t / 2.4f + i * 0.13f) * Mathf.PI * 2f;
                            var op = pos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 1.25f;
                            var world = root.TransformPoint(_arena.Local(op.x, op.y) + Vector3.up * 0.25f);
                            if (!Sprite(look, world, new Vector2(-Mathf.Sin(ang), Mathf.Cos(ang)), k, mine, t))
                                Dot(world + Vector3.up * (0.3f * k), 0.35f * k, Col(new Color(1f, 0.8f, 0.4f), mine), cam);
                            if (s.Charge > 0) Dot(world + Vector3.up * (0.3f * k), (0.25f + 0.5f * s.Charge01) * k, Col(new Color(1f, 0.7f, 0.3f, 0.25f + 0.5f * s.Charge01), mine), cam);
                            Mark(body); break;
                        }
                        case ProcBody.Shell:
                            // a shell under the runner: whole when ready, a faint ring filling meanwhile
                            if (s.Ready) { Disc(feet, 1.7f * k, Col(new Color(1f, 0.75f, 0.3f, 0.38f), mine)); Ring(feet, 1.7f * k, Col(new Color(1f, 0.8f, 0.4f, 0.95f), mine), false); }
                            else Ring(feet, 1.7f * k, Col(new Color(1f, 0.8f, 0.4f, 0.15f + 0.4f * s.Charge01), mine), true);
                            Mark(body); break;
                        case ProcBody.Motes:
                        {
                            int n = s.Active ? 6 : 3;
                            for (int m = 0; m < n; m++)
                            {
                                float ph = (t * 0.8f + m / (float)n + i * 0.21f) % 1f, ang = m * 2.4f + i;
                                var mp = _arena.Local(pos.x + Mathf.Cos(ang) * 0.45f, pos.y + Mathf.Sin(ang) * 0.45f) + Vector3.up * (0.2f + 1.6f * ph);
                                Dot(root.TransformPoint(mp), (s.Active ? 0.36f : 0.26f) * k, Col(new Color(0.45f, 1f, 0.5f, (1f - ph) * (s.Active ? 0.95f : 0.7f)), mine), cam);
                            }
                            if (s.Charge > 0 && !s.Active) Ring(feet, (0.6f + 0.9f * s.Charge01) * k, Col(new Color(0.45f, 1f, 0.5f, 0.5f), mine), true);
                            if (s.Active) Ring(feet, 1.5f * k, Col(new Color(0.45f, 1f, 0.5f, 0.75f), mine), true);
                            Mark(body); break;
                        }
                        case ProcBody.Stars:
                            if (s.Stacks > 0) { counterDots = s.Stacks; counterCol = new Color(1f, 0.95f, 0.45f); }
                            Mark(body); break;
                        case ProcBody.Trail:
                            // the runner's shots trail: four fading dots behind each, along its flight
                            foreach (var fx in w.Fx)
                            {
                                if (fx.Kind != HordeFxKind.Shot || fx.OwnerSlot != i) continue;
                                float since = (drawTick - fx.StartTick) / rate;
                                if (since < 0f || (fx.EndTick > 0 && drawTick > fx.EndTick)) continue;
                                var wd = c.Weapon(fx.DefId); var col = World.Fx.ElementColor(wd != null ? (int)wd.Element : 0);
                                for (int d = 1; d <= 4; d++)
                                {
                                    float back = Mathf.Max(0f, since - d * 0.045f);
                                    var tp = _arena.Local(fx.X + fx.VX * back, fx.Y + fx.VY * back) + Vector3.up * 0.8f;
                                    Dot(root.TransformPoint(tp), (0.32f - d * 0.05f) * k, Col(new Color(col.r, col.g, col.b, 0.55f - d * 0.11f), mine), cam);
                                }
                            }
                            Mark(body); break;
                        case ProcBody.Arc: Mark(body); break;
                        case ProcBody.Coat:
                        {
                            // a blue coat around the runner that dims as it soaks
                            float ch = s.Charge01;
                            if (ch > 0.01f)
                            {
                                Disc(feet + Vector3.up * (0.4f * k), 2.1f * k, Col(new Color(0.35f, 0.6f, 1f, 0.10f + 0.16f * ch), mine));
                                Ring(feet, 2.1f * k, Col(new Color(0.45f, 0.7f, 1f, 0.3f + 0.65f * ch), mine), false);
                            }
                            Mark(body); break;
                        }
                        case ProcBody.Dust: case ProcBody.Peco:
                        {
                            if (p.Moving && t >= _dustAt[i])
                            {
                                _dustAt[i] = t + 0.12f;
                                var back = new Vector2(-Mathf.Cos(p.Facing * Mathf.Deg2Rad), -Mathf.Sin(p.Facing * Mathf.Deg2Rad)) * 0.35f;
                                Add(Tk.Dust, pos + back, Col(new Color(0.75f, 0.68f, 0.55f, 0.55f), mine), 0.5f, 0.45f, i);
                            }
                            if (s.Ready)
                            {
                                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 10f);
                                var front = new Vector2(Mathf.Cos(p.Facing * Mathf.Deg2Rad), Mathf.Sin(p.Facing * Mathf.Deg2Rad)) * 0.45f;
                                Dot(root.TransformPoint(_arena.Local(pos.x + front.x, pos.y + front.y) + Vector3.up * 0.6f), (0.35f + 0.15f * pulse) * k, Col(new Color(1f, 0.9f, 0.4f), mine), cam);
                                Ring(feet, 1.3f * k, Col(new Color(1f, 0.9f, 0.45f, 0.6f), mine), true);
                            }
                            else if (s.Charge > 0) Ring(feet, (0.5f + 0.8f * s.Charge01) * k, Col(new Color(1f, 0.9f, 0.45f, 0.35f), mine), true);
                            Mark(body); break;
                        }
                        case ProcBody.PickupRing:
                        {
                            if (!mine) break;
                            float reach = HordeWorldDraw.Reach(c, p);
                            Ring(feet, 2f * reach * k, new Color(0.5f, 1f, 0.6f, 0.25f + 0.35f * s.Charge01), true);
                            int g = 0;
                            foreach (var q in w.Pickups)
                            {
                                if (g >= 150) break;
                                var qd = c.Pickup(q.DefId); if (qd == null || qd.Kind != PickupKind.Exp || q.PulledBy >= 0) continue;
                                Dot(root.TransformPoint(_arena.Local(q.X, q.Y) + Vector3.up * 0.25f), 0.7f * k, new Color(0.55f, 0.85f, 1f, 0.45f + 0.2f * Mathf.Sin(t * 6f + q.Id)), cam);
                                g++;
                            }
                            GemsGlowing = g;
                            Mark(body); break;
                        }
                        case ProcBody.RaceIcon:
                        {
                            if (!mine) break;
                            int n = 0;
                            for (int e = 0; e < w.Enemies.Length && n < 150; e++)
                            {
                                var en = w.Enemies[e]; if (!en.Alive) continue;
                                bool target = false;
                                foreach (var o in p.Passives) { var pd = c.Passive(o.DefId); if (pd?.Proc != null && pd.Proc.Kind == ProcKind.Bane && pd.Proc.Targets(c.Enemy(en.DefId))) { target = true; break; } }
                                if (!target) continue;
                                var ed = c.Enemy(en.DefId);
                                float ex = en.PrevX + (en.X - en.PrevX) * a, ey = en.PrevY + (en.Y - en.PrevY) * a;
                                float top = 1.4f * (ed != null ? ed.DrawScale / 100f : 1f) + 0.3f;
                                var ip = root.TransformPoint(_arena.Local(ex, ey) + Vector3.up * top);
                                Dot(ip, 0.42f * k, new Color(0.05f, 0.03f, 0.08f, 0.6f), cam, _disc);
                                Dot(ip, 0.3f * k, new Color(1f, 0.35f, 0.55f, 0.95f), cam);
                                n++;
                            }
                            IconsShown = n;
                            Mark(body); break;
                        }
                        case ProcBody.ZonePulse:
                        {
                            foreach (var fx in w.Fx)
                            {
                                if (fx.Kind != HordeFxKind.Zone || fx.OwnerSlot != i) continue;
                                float zx = fx.X, zy = fx.Y; if (fx.Follows) { zx = pos.x; zy = pos.y; }
                                float ph = (t * 0.9f + fx.Id * 0.17f) % 1f;
                                Ring(root.TransformPoint(_arena.Local(zx, zy)), 2f * Mathf.Max(0.5f, fx.Radius) * (0.55f + 0.45f * ph) * k, Col(new Color(1f, 0.85f, 0.45f, 0.65f * (1f - ph)), mine), true);
                            }
                            if (s.Active) Disc(feet + Vector3.up * (0.02f * k), 1.4f * k, Col(new Color(1f, 0.85f, 0.4f, 0.22f), mine));
                            Mark(body); break;
                        }
                        case ProcBody.Glow:
                        {
                            // the hand: on the runner's right as seen, at hand height; compact when ready
                            var right = cam.transform.right; right.y = 0; right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;
                            var hand = feet + Vector3.up * (0.95f * k) + right * (0.45f * k);
                            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 9f);
                            if (s.Ready) Dot(hand, (0.28f + 0.10f * pulse) * k, Col(new Color(1f, 0.95f, 0.65f, 0.85f), mine), cam);
                            else Dot(hand, (0.14f + 0.14f * s.Charge01) * k, Col(new Color(1f, 0.95f, 0.65f, 0.25f + 0.3f * s.Charge01), mine), cam);
                            Mark(body); break;
                        }
                        case ProcBody.RedAura:
                            if (s.Active)
                            {
                                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 7f);
                                Disc(feet + Vector3.up * (0.02f * k), 1.7f * k, Col(new Color(1f, 0.15f, 0.1f, 0.18f + 0.1f * pulse), mine));
                                Ring(feet, (1.6f + 0.25f * pulse) * k, Col(new Color(1f, 0.25f, 0.2f, 0.85f), mine), false);
                            }
                            Mark(body); break;
                        case ProcBody.Mist:
                            if (s.Active)
                            {
                                Disc(feet + Vector3.up * (0.02f * k), 1.9f * k, new Color(0.06f, 0.06f, 0.08f, mine ? 0.45f : 0.3f));
                                for (int m = 0; m < 5; m++)
                                {
                                    float ang = t * 1.6f + m * 1.2566f;
                                    Dot(root.TransformPoint(_arena.Local(pos.x + Mathf.Cos(ang) * 0.7f, pos.y + Mathf.Sin(ang) * 0.7f) + Vector3.up * (0.3f + 0.15f * m)), 0.38f * k, new Color(0.55f, 0.55f, 0.6f, 0.28f), cam, _disc);
                                }
                            }
                            Mark(body); break;
                        case ProcBody.Shield:
                            if (s.Ready || f.Kind == ProcKind.Reflect)
                                Dot(feet + Vector3.up * (3.15f * k), 0.18f * k, Col(new Color(0.55f, 0.8f, 1f, 0.78f), mine), cam);
                            Mark(body); break;
                        case ProcBody.Slash:
                            if (s.Ready) Dot(feet + Vector3.up * (3.15f * k), 0.15f * k, Col(new Color(1f, 0.5f, 0.45f, 0.75f), mine), cam);
                            Mark(body); break;
                    }
                }
                // a counter's stacks as tiny non-occluding dots well above the head (dodges in a row, criticals toward a clover)
                for (int d = 0; d < counterDots && d < 8; d++)
                {
                    var right = cam.transform.right; right.y = 0; right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;
                    Dot(feet + Vector3.up * (3.25f * k) + right * ((d - (counterDots - 1) / 2f) * 0.26f * k), 0.18f * k, Col(counterCol, mine), cam);
                }
            }
            // ---- marked enemies: a ring that turns
            if (_marks.Count > 0)
            {
                List<int> gone = null;
                foreach (var kv in _marks)
                {
                    var en = kv.Key >= 0 && kv.Key < w.Enemies.Length ? w.Enemies[kv.Key] : null;
                    if (en == null || !en.Alive || en.Serial != kv.Value.serial || t > kv.Value.until) { (gone ??= new List<int>()).Add(kv.Key); continue; }
                    var ed = c.Enemy(en.DefId);
                    float ex = en.PrevX + (en.X - en.PrevX) * a, ey = en.PrevY + (en.Y - en.PrevY) * a;
                    float r = (ed != null ? ed.Radius : 0.5f) * 2.6f * (1f + 0.1f * Mathf.Sin(t * 8f));
                    Ring(root.TransformPoint(_arena.Local(ex, ey)), r * k, new Color(1f, 0.45f, 0.2f, 0.9f), true);
                }
                if (gone != null) foreach (int g in gone) _marks.Remove(g);
            }
            // ---- the transient effects
            for (int n = _tr.Count - 1; n >= 0; n--)
            {
                var tr = _tr[n]; float u = (t - tr.Start) / Mathf.Max(0.01f, tr.Life);
                if (u >= 1f) { _tr.RemoveAt(n); continue; }
                var col = tr.C; var at = root.TransformPoint(_arena.Local(tr.Pos.x, tr.Pos.y));
                switch (tr.Kind)
                {
                    case Tk.Ring: col.a *= 1f - u; Ring(at, 2f * tr.Size * (0.25f + 0.75f * Mathf.Sqrt(u)) * k, col, true); break;
                    case Tk.Ghost:
                        col.a *= 0.45f * (1f - u);
                        Dot(at + Vector3.up * (0.55f * k), 0.65f * k, col, cam);
                        Dot(at + Vector3.up * (1.05f * k), 0.38f * k, col, cam);
                        break;
                    case Tk.Star:
                    {
                        col.a *= 0.85f * (1f - u); float s = tr.Size * (0.35f + 0.3f * u) * k; var up = at + Vector3.up * (0.85f * k);
                        Stretched(up, Vector3.up, s * 1.35f, s * 0.18f, col, cam); Stretched(up, cam.transform.right, s * 1.35f, s * 0.18f, col, cam);
                        Dot(up, s * 0.48f, col, cam);
                        break;
                    }
                    case Tk.Flash: col.a *= 0.65f * (1f - u); Dot(at + Vector3.up * (0.25f * k), tr.Size * (0.4f + 0.35f * u) * k, col, cam); break;
                    case Tk.Crack:
                        col.a *= 1f - u;
                        for (int m = 0; m < 6; m++) { float ang = m * Mathf.PI / 3f; Dot(at + Vector3.up * (0.25f * k) + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * ((0.35f + 0.85f * u) * k), 0.22f * k, col, cam); }
                        break;
                    case Tk.Slash:
                    {
                        col.a *= 1f - u;
                        var dir = root.TransformDirection(new Vector3(tr.Dir.y, 0f, -tr.Dir.x)); // across the strike
                        Stretched(at + Vector3.up * (0.55f * k), dir.normalized + Vector3.up * 0.6f, tr.Size * k * (0.5f + 0.35f * u), 0.10f * k, col, cam);
                        break;
                    }
                    case Tk.Dust: col.a *= 1f - u; Dot(at + Vector3.up * ((0.1f + 0.3f * u) * k), (0.22f + 0.28f * u) * k, col, cam, _disc); break;
                    case Tk.ArcLine:
                    {
                        // the swing's arc (Dir = aim, arc in degrees), drawn as bright dots just past the slice's reach, following its owner
                        var op = tr.Owner >= 0 && tr.Owner < w.Players.Length && w.Players[tr.Owner].Present ? _pos[tr.Owner] : tr.Pos;
                        col.a *= 1f - u; int dots = Mathf.Clamp(Mathf.CeilToInt(tr.Dir.y / 12f), 5, 30);
                        for (int m = 0; m <= dots; m++)
                        {
                            float ang = (tr.Dir.x - tr.Dir.y * 0.5f + tr.Dir.y * m / dots) * Mathf.Deg2Rad;
                            Dot(root.TransformPoint(_arena.Local(op.x + Mathf.Cos(ang) * tr.Size, op.y + Mathf.Sin(ang) * tr.Size) + Vector3.up * 0.3f), 0.25f * k, col, cam);
                        }
                        break;
                    }
                }
            }
            if (!myOverheadUpdated && _overRt != null && _overRt.gameObject.activeSelf)
                _overRt.gameObject.SetActive(false);
        }

        private void EnsureOverheadUi()
        {
            if (_overRt != null || _arena == null) return;
            _overRt = UiKit.Panel("HordeProcOverhead", _arena.transform, new Vector2(280, 24), 0.00020f, false);
            var bgImg = _overRt.GetComponent<Image>();
            if (bgImg != null) bgImg.enabled = false;
            _overPlacingRim = HordeLook.GoldRim(_overRt, 2f);
            _overPlacingRim.gameObject.SetActive(false);
            for (int i = 0; i < 4; i++)
            {
                var pill = HordeLook.Raw(UiKit.Soft(_overRt, "proc-pill-" + i, Vector2.zero, new Vector2(68, 20), new Color(0.06f, 0.05f, 0.04f, 0.54f)));
                HordeLook.GoldRim(pill, 0f, true);
                var icon = UiKit.Icon(pill, new Vector2(-22, 0), new Vector2(14, 14));
                var txt = UiKit.Label(pill, "", new Vector2(7, 0), new Vector2(48, 18), 11, TextAnchor.MiddleCenter, Color.white);
                txt.fontStyle = FontStyle.Bold;
                txt.resizeTextForBestFit = true; txt.resizeTextMinSize = 8; txt.resizeTextMaxSize = 11;
                txt.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.85f);
                pill.gameObject.SetActive(false);
                _overPills.Add((pill, pill.GetComponent<Image>(), icon, txt));
            }
            _overRt.gameObject.SetActive(false);
        }

        private static bool TryFormatOverheadCountdown(Fam f, out string label, out Color col)
        {
            label = null; col = Color.white;
            var d = f.Look; var st = f.State;
            if (d == null) return false;
            switch (d.Kind)
            {
                case ProcKind.HitCount:
                case ProcKind.CritCount:
                case ProcKind.DodgeCounter:
                {
                    int need = Mathf.Max(1, d.Count);
                    if (st.Ready || st.Active)
                    {
                        label = d.Kind == ProcKind.HitCount ? Lang.T("Hit ★")
                              : d.Kind == ProcKind.CritCount ? Lang.T("Crit ★")
                              : Lang.T("Blast ★");
                        col = new Color(1f, 0.92f, 0.38f);
                        return true;
                    }
                    int rem = Mathf.Clamp(need - st.Stacks, 1, need);
                    label = d.Kind == ProcKind.HitCount ? Lang.F("Hit ({0})", rem)
                          : d.Kind == ProcKind.CritCount ? Lang.F("Crit ({0})", rem)
                          : Lang.F("Dodge ({0})", rem);
                    col = rem == 1 ? new Color(1f, 0.9f, 0.4f) : new Color(0.88f, 0.95f, 1f);
                    return true;
                }
                case ProcKind.TimerCharge:
                case ProcKind.Shell:
                case ProcKind.MagnetPulse:
                case ProcKind.Stride:
                case ProcKind.IdleCharge:
                case ProcKind.BossBlock:
                case ProcKind.Coat:
                case ProcKind.Cloak:
                case ProcKind.Gangster:
                {
                    if (st.Ready || st.Active)
                    {
                        label = d.Kind == ProcKind.Shell ? Lang.T("Block ★")
                              : d.Kind == ProcKind.TimerCharge ? Lang.T("Spell ★")
                              : d.Kind == ProcKind.MagnetPulse ? Lang.T("Pull ★")
                              : d.Kind == ProcKind.Stride ? Lang.T("Dash ★")
                              : d.Kind == ProcKind.IdleCharge ? Lang.T("Focus ★")
                              : d.Kind == ProcKind.BossBlock ? Lang.T("Parry ★")
                              : d.Kind == ProcKind.Coat ? Lang.T("Coat ★")
                              : d.Kind == ProcKind.Cloak ? Lang.T("Cloak ★")
                              : Lang.T("Guard ★");
                        col = new Color(0.55f, 1f, 0.62f);
                        return true;
                    }
                    float total = Mathf.Max(1f, PassiveProcDef.At(d.Seconds, d.SecondsPerLevel, f.Level));
                    int remSec = Mathf.Clamp(Mathf.CeilToInt((1f - st.Charge01) * total), 1, 99);
                    label = d.Kind == ProcKind.Shell ? Lang.F("Block {0}s", remSec)
                          : d.Kind == ProcKind.TimerCharge ? Lang.F("Spell {0}s", remSec)
                          : d.Kind == ProcKind.MagnetPulse ? Lang.F("Pull {0}s", remSec)
                          : d.Kind == ProcKind.Stride ? Lang.F("Dash {0}s", remSec)
                          : d.Kind == ProcKind.IdleCharge ? Lang.F("Focus {0}s", remSec)
                          : d.Kind == ProcKind.BossBlock ? Lang.F("Parry {0}s", remSec)
                          : d.Kind == ProcKind.Coat ? Lang.F("Coat {0}s", remSec)
                          : d.Kind == ProcKind.Cloak ? Lang.F("Cloak {0}s", remSec)
                          : Lang.F("Guard {0}s", remSec);
                    col = new Color(0.78f, 0.9f, 1f);
                    return true;
                }
                default:
                    return false;
            }
        }

        private void UpdateOverheadCounters(HordeContent c, List<Fam> fams, Vector3 feet, float k, Camera cam)
        {
            EnsureOverheadUi();
            if (_overRt == null) return;
            int shown = 0;
            for (int i = 0; i < fams.Count && shown < _overPills.Count; i++)
            {
                var f = fams[i];
                if (!TryFormatOverheadCountdown(f, out string label, out var col)) continue;
                var p = _overPills[shown++];
                if (!p.pill.gameObject.activeSelf) p.pill.gameObject.SetActive(true);
                var sp = HordeText.PassiveIcon(c, f.PassiveId);
                p.icon.sprite = sp; p.icon.enabled = sp != null;
                p.txt.text = label;
                p.txt.color = col;
                bool ready = f.State.Ready || f.State.Active;
                p.bg.color = ready ? new Color(0.22f, 0.18f, 0.05f, 0.62f) : new Color(0.05f, 0.05f, 0.07f, 0.46f);
            }
            for (int i = shown; i < _overPills.Count; i++)
                if (_overPills[i].pill.gameObject.activeSelf) _overPills[i].pill.gameObject.SetActive(false);

            bool any = shown > 0;
            if (_overRt.gameObject.activeSelf != any) _overRt.gameObject.SetActive(any);
            if (!any) return;

            const float pillW = 68f, gap = 4f;
            float totalW = shown * pillW + (shown - 1) * gap;
            _overRt.sizeDelta = new Vector2(totalW + 6f, 24f);
            for (int i = 0; i < shown; i++)
            {
                float x = -totalW * 0.5f + pillW * 0.5f + i * (pillW + gap);
                _overPills[i].pill.anchoredPosition = new Vector2(x, 0f);
            }
            var camYaw = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f);
            var worldPos = feet + Vector3.up * (3.65f * k) + (camYaw * OverheadLocalOffset) * k;
            _overRt.position = worldPos;
            _overRt.localScale = Vector3.one * (0.00020f * Mathf.Max(0.45f, k / 0.02f) * OverheadScale);
            UiKit.FaceHead(_overRt, cam);
        }

        private void Mark(ProcBody b) => BodyFrames[(int)b & 31]++;

        /// <summary>The body's monster (walking, facing along <paramref name="dir"/>) or item sprite; false when it has none.</summary>
        private bool Sprite(PassiveProcDef look, Vector3 world, Vector2 dir, float k, bool mine, float t)
        {
            if (_sprites == null || (look.BodyMob <= 0 && look.BodyItem <= 0)) return false;
            var cam = Camera.main; if (cam == null) return false;
            HordeSprites.Frame f;
            if (look.BodyMob > 0)
            {
                int d = (Unit.DirFromDelta(dir.normalized) + ActorSprite.CameraDirection) & 7;
                f = _sprites.Get(_sprites.Mob(look.BodyMob), (int)ActorSprite.MonsterAction.Walk, d, t * 1000f, true);
            }
            else f = _sprites.Get(_sprites.Item(look.BodyItem), 0, 0, 0f, false);
            if (f == null) return false;
            _mpb.SetColor(ColorId, mine ? Color.white : new Color(0.85f, 0.95f, 1f, 0.75f));
            Graphics.DrawMesh(f.Mesh, HordeSprites.Matrix(f, world, cam.transform.position, k, look.BodyMob > 0 ? 0.75f : 0.9f), _sprites.MaterialFor(f.Texture), 0, null, 0, _mpb, false, true);
            DrawCalls++; SpritesDrawn++;
            return true;
        }

        private void Dot(Vector3 world, float size, Color c, Camera cam, Material mat = null)
        {
            var rot = Quaternion.LookRotation(world - cam.transform.position, Vector3.up) * Quaternion.Euler(-90f, 0, 0);
            _mpb.SetColor(ColorId, c);
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(world, rot, Vector3.one * size), mat ?? _glow, 0, null, 0, _mpb, false, false);
            DrawCalls++;
        }

        /// <summary>A dot stretched along <paramref name="along"/> (projected on the face plane): a star's arm, a slash.</summary>
        private void Stretched(Vector3 world, Vector3 along, float length, float width, Color c, Camera cam)
        {
            var face = Quaternion.LookRotation(world - cam.transform.position, Vector3.up) * Quaternion.Euler(-90f, 0, 0);
            var axis = Vector3.ProjectOnPlane(along, face * Vector3.up);
            if (axis.sqrMagnitude < 1e-8f) axis = cam.transform.right;
            var rot = Quaternion.LookRotation(face * Vector3.up, axis.normalized) * Quaternion.Euler(-90f, 0, 0);
            _mpb.SetColor(ColorId, c);
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(world, rot, new Vector3(width, 1f, length)), _glow, 0, null, 0, _mpb, false, false);
            DrawCalls++;
        }

        private void Ring(Vector3 world, float diameter, Color c, bool thin)
        {
            var root = _arena.Root; float up = 0.07f * Mathf.Abs(root.lossyScale.x);
            _mpb.SetColor(ColorId, c);
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(world + Vector3.up * up, root.rotation, Vector3.one * diameter), thin ? _thinRing : _ring, 0, null, 0, _mpb, false, false);
            DrawCalls++;
        }

        private void Disc(Vector3 world, float diameter, Color c)
        {
            var root = _arena.Root;
            _mpb.SetColor(ColorId, c);
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(world + Vector3.up * (0.05f * Mathf.Abs(root.lossyScale.x)), root.rotation, Vector3.one * diameter), _disc, 0, null, 0, _mpb, false, false);
            DrawCalls++;
        }

        // =============================================================================================== the HUD

        private readonly List<(RectTransform bg, RectTransform fill, Image img)> _meters = new List<(RectTransform, RectTransform, Image)>();
        private RectTransform _shellStrip; private Image _shellImg; private HordeHud _hud;
        public const float MeterH = 7f;

        /// <summary>The HUD (ten times a second, HordeGame): a meter over each passive's icon, and the shell's strip under the HP bar.</summary>
        public void Hud(HordeHud hud, HordeWorldView w, HordeContent c, int mySlot)
        {
            if (hud == null || w == null || c == null || mySlot < 0 || mySlot >= w.Players.Length) return;
            if (_hud != hud) { _hud = hud; _meters.Clear(); _shellStrip = null; }
            var me = w.Players[mySlot];
            for (int i = 0; i < 6; i++)
            {
                var box = hud.PassiveSlot(i); if (box == null) continue;
                while (_meters.Count <= i) _meters.Add((null, null, null));
                var m = _meters[i];
                if (m.bg == null)
                {
                    float wdt = box.sizeDelta.x - 8f;
                    var bg = UiKit.Box((RectTransform)box.parent, "pass-meter", box.anchoredPosition + new Vector2(0, box.sizeDelta.y / 2f + MeterH / 2f + 2f), new Vector2(wdt, MeterH), new Color(0, 0, 0, 0.7f));
                    var fill = UiKit.Box(bg, "fill", Vector2.zero, new Vector2(wdt - 2f, MeterH - 2f), new Color(0.4f, 0.65f, 1f));
                    fill.anchorMin = fill.anchorMax = new Vector2(0, 0.5f); fill.pivot = new Vector2(0, 0.5f); fill.anchoredPosition = new Vector2(1, 0);
                    m = (bg, fill, fill.GetComponent<Image>()); _meters[i] = m;
                }
                HordeProcView? st = null;
                if (i < me.Passives.Count)
                {
                    var pd = c.Passive(me.Passives[i].DefId);
                    if (pd?.Proc != null) { st = new HordeProcView { Kind = pd.Proc.Kind }; foreach (var s in me.Procs) if (s.Kind == pd.Proc.Kind) { st = s; break; } }
                }
                bool on = st.HasValue;
                if (m.bg.gameObject.activeSelf != on) m.bg.gameObject.SetActive(on);
                if (!on) continue;
                var v = st.Value; float full = m.bg.sizeDelta.x - 2f;
                float frac = v.Active || v.Ready ? 1f : v.Charge01;
                m.fill.sizeDelta = new Vector2(full * Mathf.Clamp01(frac), MeterH - 2f);
                m.img.color = v.Active ? new Color(0.45f, 1f, 0.5f) : v.Ready ? new Color(1f, 0.85f, 0.35f) : new Color(0.4f, 0.65f, 1f);
                MetersShown++; if (v.Ready) MetersReady++;
            }
            // the shell under the HP bar (a Shell family): amber when it is up, a thin line growing meanwhile
            var hp = hud.HpBar;
            if (hp != null)
            {
                if (_shellStrip == null)
                {
                    _shellStrip = UiKit.Box((RectTransform)hp.parent, "pass-shell", hp.anchoredPosition + new Vector2(0, -hp.sizeDelta.y / 2f - 3f), new Vector2(hp.sizeDelta.x, 5f), new Color(1f, 0.75f, 0.3f));
                    _shellImg = _shellStrip.GetComponent<Image>();
                }
                HordeProcView? shell = null; foreach (var s in me.Procs) if (s.Kind == ProcKind.Shell) shell = s;
                bool show = shell.HasValue && !me.Fallen;
                if (_shellStrip.gameObject.activeSelf != show) _shellStrip.gameObject.SetActive(show);
                if (show)
                {
                    var sv = shell.Value;
                    _shellStrip.sizeDelta = new Vector2(hp.sizeDelta.x * (sv.Ready ? 1f : sv.Charge01), sv.Ready ? 5f : 2f);
                    _shellImg.color = sv.Ready ? new Color(1f, 0.78f, 0.3f) : new Color(1f, 0.78f, 0.3f, 0.45f);
                }
            }
        }

        /// <summary>The HUD meters shown now (the smoke): how many passives carry one, and their fills (0..1).</summary>
        public int MetersOn { get { int n = 0; foreach (var m in _meters) if (m.bg != null && m.bg.gameObject.activeSelf) n++; return n; } }
        public bool ShellStripShown => _shellStrip != null && _shellStrip.gameObject.activeSelf;
    }

    /// <summary>
    /// Client wave 17 (h17pass): what a passive's trigger does, on its card (HordeText's card texts, wave 15's style: one plain
    /// sentence first, then the numbers that change, "a → b").
    /// </summary>
    public static class HordePassivesText
    {
        private static float At(float b, float per, int lv) => PassiveProcDef.At(b, per, lv);
        private static string Num(float v) => v.ToString(Mathf.Abs(v - Mathf.Round(v)) < 0.0005f ? "0" : "0.##", Lang.Culture);

        /// <summary>The trigger's sentence at <paramref name="level"/> ("" when none).</summary>
        public static string Does(HordeContent c, PassiveDef p, int level)
        {
            var d = p?.Proc; if (d == null) return "";
            int lv = Mathf.Max(1, level);
            bool magic = d.ClassMask == 1 << (int)DamageClass.Magic;
            switch (d.Kind)
            {
                case ProcKind.DodgeCounter: return Lang.F("After {0} dodges in a row: blasts nearby enemies back.", d.Count);
                case ProcKind.HitCount: return d.HitEffect == EffectKind.Stun ? Lang.F("Every {0} hits: next attack deals bonus damage and stuns.", d.Count) : Lang.F("Every {0} hits: next attack deals bonus damage.", d.Count);
                case ProcKind.Shell: return Lang.F("Forms a barrier that blocks 1 hit every {0} s.", Num(At(d.Seconds, d.SecondsPerLevel, lv)));
                case ProcKind.StillBoost: return Lang.F("Stand still {0} s: doubles this card's stat bonuses.", Num(d.Seconds));
                case ProcKind.MoveBoost: return Lang.F("Move for {0} s: doubles this card's stat bonuses.", Num(d.Seconds));
                case ProcKind.CritCount: return Lang.F("Every {0} critical hits: restores your HP.", d.Count);
                case ProcKind.Mark: return Lang.F("Every {0} ranged hits marks the target to take extra damage.", d.Count);
                case ProcKind.FullSwing: return Lang.F("Hitting {0}+ enemies in one swing refunds attack cooldown.", d.Count);
                case ProcKind.Coat: return Lang.F("Shield absorbs damage; recharges after {0} s unhit.", Num(d.Seconds));
                case ProcKind.Stride: return Lang.F("Moving for {0} s charges your next melee hit.", Num(d.Seconds));
                case ProcKind.MagnetPulse: return Lang.F("Every {0} s, pulls in nearby EXP gems.", Num(At(d.Seconds, d.SecondsPerLevel, lv)));
                case ProcKind.Bane: return Lang.F("{0}: kills can drop a heal item.", BaneNames(d));
                case ProcKind.OwnZone: return Lang.T("Standing inside your own zones, walls, or auras boosts all damage.");
                case ProcKind.IdleCharge: return Lang.F("After {0} s without attacking: empowers your next hit.", Num(d.Seconds));
                case ProcKind.DodgeReady: return Lang.F("Dodging an attack resets {0}'s cooldown.", HordeText.Weapon(c, d.Arg));
                case ProcKind.LowHpBoost: return Lang.F("Below {0} % HP: all attacks deal bonus damage.", Num(d.Threshold));
                case ProcKind.LowHpHide: return Lang.F("Dropping below {0} % HP grants brief invulnerability.", Num(d.Threshold));
                case ProcKind.HurtCounter: return Lang.T("When hit by an enemy, automatically strikes them back.");
                case ProcKind.Reflect: return Lang.T("When hit, reflects part of the damage back at the attacker.");
                case ProcKind.TimerCharge: return magic ? Lang.F("Every {0} s: empowers your next spell for bonus damage.", Num(At(d.Seconds, d.SecondsPerLevel, lv))) : Lang.F("Every {0} s: empowers your next attack for bonus damage.", Num(At(d.Seconds, d.SecondsPerLevel, lv)));
                case ProcKind.KillHeal: return magic ? Lang.T("Killing an enemy with a spell restores HP.") : Lang.T("Killing an enemy restores HP.");
                case ProcKind.KillDrop: return Lang.T("Kills can drop a quiver granting +1 projectile briefly.");
                case ProcKind.BossBlock: return Lang.F("Blocks one elite or boss attack every {0} s.", Num(At(d.Cooldown, d.CooldownPerLevel, lv)));
                case ProcKind.Cloak: return Lang.F("Stand still {0} s to cloak so enemies lose sight of you.", Num(At(d.Seconds, d.SecondsPerLevel, lv)));
                case ProcKind.Gangster: return Lang.F("Stand still {0} s so normal enemies ignore you.", Num(d.Seconds));
                default: return "";
            }
        }

        private static string BaneNames(PassiveProcDef d)
        {
            var names = new List<string>();
            for (int r = 0; r < 10; r++) if ((d.RaceMask >> r & 1) != 0) names.Add(HordeText.RaceName((Race)r));
            for (int e = 0; e < 10; e++) if ((d.ElementMask >> e & 1) != 0) names.Add(HordeText.ElementName((Element)e));
            if ((d.SizeMask >> (int)UnitSize.Small & 1) != 0) names.Add(Lang.T("~size~Small"));
            if ((d.SizeMask >> (int)UnitSize.Medium & 1) != 0) names.Add(Lang.T("~size~Medium"));
            if ((d.SizeMask >> (int)UnitSize.Large & 1) != 0) names.Add(Lang.T("~size~Large"));
            if ((d.RankMask & (1 << (int)EnemyRank.Elite | 1 << (int)EnemyRank.Boss)) != 0) names.Add(Lang.T("Elites and bosses"));
            return string.Join(", ", names);
        }

        /// <summary>The trigger's sentence first, then the modifiers' sentences so no passive effect is hidden.</summary>
        public static string Join(string trigger, string mods)
        {
            if (string.IsNullOrEmpty(trigger)) return mods ?? "";
            if (string.IsNullOrEmpty(mods)) return trigger;
            return trigger + " " + mods;
        }

        /// <summary>The trigger's numbers on a card: all of a new passive's (<paramref name="from"/> 0), else only those that change.</summary>
        public static void Numbers(PassiveDef p, int from, int to, List<string> lines)
        {
            var d = p?.Proc; if (d == null || lines == null) return;
            void Line(string label, float b, float per, System.Func<float, string> f, bool show)
            {
                if (from <= 0) { if (show) lines.Add(label + " " + f(At(b, per, to))); return; }
                float x = At(b, per, from), y = At(b, per, to);
                if (Mathf.Abs(x - y) > 0.0005f) lines.Add(label + " " + f(x) + " → " + f(y));
            }
            string Pct(float v) => "+" + Num(v) + " %";
            string P(float v) => Num(v) + " %";
            string S(float v) => Lang.F("{0} s", Num(v));
            switch (d.Kind)
            {
                case ProcKind.DodgeCounter: Line(Lang.T("Blast damage"), d.Damage, d.DamagePerLevel, Num, true); break;
                case ProcKind.HitCount: Line(Lang.T("Bonus hit damage"), d.Power, d.PowerPerLevel, Pct, true); break;
                case ProcKind.Shell: Line(Lang.T("Barrier cooldown"), d.Seconds, d.SecondsPerLevel, S, true); break;
                case ProcKind.CritCount: Line(Lang.T("HP restored"), d.Power, d.PowerPerLevel, P, true); break;
                case ProcKind.Mark: Line(Lang.T("Marked target damage"), d.Power, d.PowerPerLevel, Pct, true); break;
                case ProcKind.FullSwing: Line(Lang.T("Cooldown refunded"), d.Power, d.PowerPerLevel, P, true); break;
                case ProcKind.Coat: Line(Lang.T("Shield (% of max HP)"), d.Power, d.PowerPerLevel, P, true); break;
                case ProcKind.Stride: case ProcKind.IdleCharge: Line(Lang.T("Empowered hit bonus"), d.Power, d.PowerPerLevel, Pct, true); break;
                case ProcKind.TimerCharge: Line(Lang.T("Every"), d.Seconds, d.SecondsPerLevel, S, true); Line(Lang.T("Empowered hit bonus"), d.Power, d.PowerPerLevel, Pct, true); break;
                case ProcKind.MagnetPulse: Line(Lang.T("Every"), d.Seconds, d.SecondsPerLevel, S, true); Line(Lang.T("Pull radius"), d.Area, d.AreaPerLevel, v => Lang.F("{0} cells", Num(v)), true); break;
                case ProcKind.BossBlock: case ProcKind.Gangster: Line(Lang.T("Every"), d.Cooldown, d.CooldownPerLevel, S, true); break;
                case ProcKind.Cloak: Line(Lang.T("Stand still time"), d.Seconds, d.SecondsPerLevel, S, true); break;
                case ProcKind.Bane: Line(Lang.T("Heal drop chance"), d.Chance, d.ChancePerLevel, P, true); break;
                case ProcKind.OwnZone: Line(Lang.T("Damage in own zone"), d.Power, d.PowerPerLevel, Pct, true); break;
                case ProcKind.LowHpBoost: if (d.Power > 0) Line(Lang.T("Damage bonus"), d.Power, d.PowerPerLevel, Pct, true); break;
                case ProcKind.LowHpHide: Line(Lang.T("Invulnerable time"), d.Seconds, d.SecondsPerLevel, S, true); break;
                case ProcKind.HurtCounter: Line(Lang.T("Counter damage"), d.Damage, d.DamagePerLevel, Num, true); break;
                case ProcKind.Reflect: Line(Lang.T("Damage reflected"), d.Power, d.PowerPerLevel, P, true); break;
                case ProcKind.KillHeal: Line(Lang.T("HP per kill"), d.Damage, d.DamagePerLevel, Num, true); break;
                case ProcKind.KillDrop: Line(Lang.T("Drop chance"), d.Chance, d.ChancePerLevel, P, true); break;
            }
        }
    }
}
