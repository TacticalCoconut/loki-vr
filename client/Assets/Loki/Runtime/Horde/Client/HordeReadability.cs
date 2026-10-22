using System.Collections.Generic;
using Loki.UI;
using Loki.VR;
using UnityEngine;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 17 (h17read): Horde's readability (the design's point 28: "the visibility of attacks, projectiles, both allied and enemy, and
    /// every single source of feedback when it comes to where attacks are coming from and towards where is pretty much non-existent. They
    /// need to be very apparent."), built as design notes wrote it (design note, 2026-10-08: "I like the proposals.
    /// Go for it."). Its options, where each lives:
    ///   A1-A5 shots by side, their trails, shadows, the near-miss mark ........ HordeWorldDraw.DrawShot (the numbers below)
    ///   B1 swing arcs (client wave 15, HordeWorldDraw.Slice) · B5 an ally's effects paler and cyan ............ HordeWorldDraw + here
    ///   B2 a nova / aura pulse: a ring expanding to its radius, 0.25 s · B3 an area strike: a flash and a ring at its radius
    ///   B4 chain links 0.25 cell wide for 0.2 s ................................................................ here
    ///   C1 every winding enemy's red "!" · C2 an enemy area's bright edge and its white flash ................. HordeWorldDraw
    ///   C3 a charge's red arrow along its path · C4 a shooter's line to its target (the host's WindUp event) ... here
    ///   C5 standing in a red area: a soft tick, one short rumble 0.3 s before it strikes ...................... here (+ HordeFeel)
    ///   D1 a red wedge on a ring around me toward whatever hit me · (D2, a red arc at the edge of my view, is gone: client wave 18, h18feel,
    ///   the design's point 21: "there is no need for an indicator of where the damage is coming from on the headset's view. It's okay to keep
    ///   it under the character, but not on the headset's view.")
    ///   D3 my character flashes red and leans away · D4 an ally's HP bar flashes, a red ring pulses under it
    ///   D5 the rumble on the hit's side, stronger with the share of my HP ..................................... here
    ///   E1 the hurt sound (RO's own by my job) · E2 an enemy's shot sounds, a whoosh when it passes close · E3 an elite's / boss's
    ///   wind-up sound ................................................................................ HordeFeel
    ///   E4 a light rumble on my critical hit (the casting hand), a medium one when an elite's / boss's strike lands near me ... here
    ///   F1 Settings → Horde "Combat effects: Normal / Large" (Large = the marks 1.5 × bigger; the real areas keep their size) ... here
    /// D1 / D3's lean / D5's side, C3 and C4 need Loki Host's part (HordeSim.Readability.cs: PlayerHit's attacker and place, the
    /// WindUp event); without it (an older host) those show nothing and the rumble is on both hands. Every rumble goes through the
    /// existing Settings → Rumble switches ("Hits you take" for D5, C5 and a strike near me; "Hits you deal" for a critical), every
    /// sound through the effects volume. Numbers: the proposal's where it gave them (sizes, seconds), Loki's own otherwise.
    /// </summary>
    public sealed class HordeReadability
    {
        // ---- A (the proposal's numbers): shot heads in cells, trails in seconds of flight, the shadow, the near miss
        // client wave 18 (h18feel), the design's point 28: "the sphere for a projectile such as a skeleton archer's arrow is way too big, reduce it
        // to a third of its current size": an ENEMY shot's disc 0.9 → 0.3 cell (its glow, rim, trail width and near-miss ring are drawn as
        // shares of it: a third too), its shadow a third (0.5 → 0.17); the players' own shots unchanged (the brief's P28)
        public const float EnemyShotCells = 0.3f, MyShotCells = 0.8f, AllyShotCells = 0.6f, EnemyTrailSeconds = 0.35f, MyTrailSeconds = 0.15f;
        public const float ShadowCells = 0.5f, EnemyShadowCells = ShadowCells / 3f, NearMissCells = 1.5f;
        /// <summary>E2: the whoosh when an enemy shot passes within this many cells without touching me (MissCells: my body 0.4 + a shot 0.3, and a margin).</summary>
        public const float WhooshCells = 2f, MissCells = 0.75f;
        // ---- B: a nova's ring (from 30 % of its radius, 0.25 s), a strike's flash and ring, a chain's links
        public const float NovaSeconds = 0.25f, NovaFrom = 0.3f, RingCells = 0.12f, StrikeFlashSeconds = 0.15f, StrikeRingSeconds = 0.3f, ChainCells = 0.25f, ChainSeconds = 0.2f, ChainLift = 0.7f;
        // ---- C: the "!", an area's edge and its last flash, the charge's arrow, the aim line, the red-area tick and rumble
        public const float BangCells = 0.6f, WarnEdgeCells = 0.12f, WarnFlashSeconds = 0.2f;
        public const float ArrowShaftCells = 0.3f, ArrowHeadCells = 0.8f, ArrowHeadLength = 0.7f, ArrowLift = 0.06f, AimLineCells = 0.1f, AimLift = 0.45f;
        public const float WarnTickEvery = 0.4f, WarnRumbleBefore = 0.3f;
        // ---- D: the wedge (0.6 s; 40° → 80° and brighter up to a hit of BigHitShare of my max HP), the flash and lean, an ally's ring
        public const float WedgeSeconds = 0.6f, WedgeInner = 1.1f, WedgeCells = 0.35f, WedgeDeg = 40f, WedgeDegBig = 80f, BigHitShare = 0.15f;
        public const float FlashSeconds = 0.1f, LeanSeconds = 0.25f, LeanDeg = 14f, AllyRingSeconds = 0.45f;
        /// <summary>F1: "Large" multiplies the marks' sizes by this (the proposal's 1.5).</summary>
        public const float LargeFactor = 1.5f;
        /// <summary>Render queues: dark outlines first (under the glow, Loki/Glow is Transparent+20), the solid marks after the glow and
        /// Loki/Str's discs (+25).</summary>
        public const int UnderQueue = 3010, SolidQueue = 3030;
        public static readonly Color EnemyShotColor = new Color(1f, 0.38f, 0.12f, 1f), EnemyShotRim = new Color(0.01f, 0.01f, 0.01f, 0.9f), MyShotRim = new Color(0.02f, 0.02f, 0.03f, 0.75f),
            AllyRim = new Color(0.35f, 0.9f, 1f, 0.22f), NearMissRing = new Color(1f, 0.2f, 0.15f, 1f), WarnEdge = new Color(1f, 0.25f, 0.18f, 1f), BangColor = new Color(1f, 0.15f, 0.1f, 1f),
            Danger = new Color(1f, 0.16f, 0.1f, 1f), FlashTint = new Color(1f, 0.35f, 0.3f, 1f), NearMissHot = new Color(1f, 0.85f, 0.25f, 1f);

        /// <summary>F1: the marks' size factor (Settings → Horde "Combat effects": Normal 1, Large 1.5).</summary>
        public static float Size => World.UserSettings.Prefs.hordeFxSize == 1 ? LargeFactor : 1f;
        /// <summary>B5: an ally's colour: paler, cyan-tinted, and very faint (mine stay full colour).</summary>
        public static Color AllyTint(Color c) { var t = Color.Lerp(c, new Color(0.7f, 0.95f, 1f), 0.45f); t.a = c.a * 0.18f; return t; }

        /// <summary>F1's row in Settings → Horde (HordeHud.SettingsRows).</summary>
        public static void SettingsRow(List<ListWindow.Row> rows, System.Action<System.Action> set)
        {
            var p = World.UserSettings.Prefs;
            rows.Add(SettingRow.Choice(Lang.T("Combat effects"), Lang.T("Size of shots and warnings"), new[] { Lang.T("~fx~Normal"), Lang.T("~fx~Large") },
                () => Mathf.Clamp(p.hordeFxSize, 0, 1), i => set(() => p.hordeFxSize = i)));
        }

        private static Texture2D _white;
        /// <summary>A flat, alpha-blended colour (Loki/Str with a white texture: the colour alone draws) at a render queue.</summary>
        public static Material SolidMaterial(string name, int queue)
        {
            if (_white == null) { _white = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "horde-white" }; var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white; _white.SetPixels(px); _white.Apply(); }
            var m = new Material(Effects.EffectManager.StrShader) { mainTexture = _white, name = name, renderQueue = queue };
            m.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            return m;
        }

        // ------------------------------------------------------------------ the instance (one per HordeGame)
        private readonly HordeGame _g;
        private readonly HordeMeshPool _pool = new HordeMeshPool();
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private Material _solid, _under;

        private struct Wind { public int Enemy, Serial, Target; public HordeWindUpKind Kind; public Vector2 From, To; public float Start, Seconds; }
        private struct Ring { public Vector2 Pos; public int Follow; public float From, To, Start, Seconds, Width; public Color C; public bool Fill; }
        private struct Link { public Vector2 A, B; public float Start; public Color C; }
        private struct Wedge { public float Deg, Start, K; }
        private readonly List<Wind> _winds = new List<Wind>();
        private readonly List<Ring> _rings = new List<Ring>();
        private readonly List<Link> _links = new List<Link>();
        private readonly List<Wedge> _wedges = new List<Wedge>();
        private float _flashUntil, _leanUntil, _lastCrit = -9f; private Vector2 _leanFrom; private bool _tinted, _leaning; private int _hitFrame = -1;
        private readonly Dictionary<int, float> _dangerNow = new Dictionary<int, float>(), _dangerTick = new Dictionary<int, float>();
        private readonly HashSet<int> _dangerRumbled = new HashSet<int>();
        private readonly List<int> _stale = new List<int>();

        // ---- counters for the log and the smoke (each a count of marks started, sounds asked, rumbles asked)
        public int Arrows, AimLines, ArrowsDrawn, AimLinesDrawn, NovaRings, StrikeMarks, ChainLinks, Wedges, MyFlashes, Leans, AllyHurts, DangerTicks, DangerRumbles, CritRumbles, NearStrikeRumbles, SideRumbles;
        /// <summary>The last wedge's direction (degrees, 0 east, 90 north) and size factor (0..1), the last side rumble's amplitudes (left,
        /// right), the last arrow's / aim line's length in cells (the smoke).</summary>
        public float LastWedgeDeg, LastWedgeK, LastRumbleLeft, LastRumbleRight, LastArrowLen, LastAimLen;
        public HordeHitVia LastHitVia;

        public HordeReadability(HordeGame g) { _g = g; }

        private void EnsureMaterials()
        {
            if (_solid != null) return;
            _solid = SolidMaterial("horde-read-solid", SolidQueue);
            _under = SolidMaterial("horde-read-under", UnderQueue);
        }

        /// <summary>A run began or ended: everything drawn goes, my character's tint and lean come back.</summary>
        public void Clear()
        {
            _winds.Clear(); _rings.Clear(); _links.Clear(); _wedges.Clear();
            _dangerNow.Clear(); _dangerTick.Clear(); _dangerRumbled.Clear();
            _flashUntil = _leanUntil = 0f; _tinted = _leaning = false;
        }

        // ------------------------------------------------------------------ the events (HordeGame.OnEvent)
        public void OnEvent(HordeEvent e)
        {
            if (!_g.InArena || _g.Channel == null) return;
            var w = _g.Channel.World; int me = _g.Run != null ? _g.Run.Slot : -1;
            var c = _g.Content; float t = Time.time; float S = Size;
            switch (e.Kind)
            {
                case HordeEventKind.PlayerHit:
                    if ((e.Flags & HordeEventFlags.Dodged) != 0 || e.Slot < 0 || e.Slot >= w.Players.Length) break;
                    if (e.Slot == me) MyHit(e, w, me); else AllyHit(e, S);
                    break;
                case HordeEventKind.WindUp:
                {
                    if (e.Target < 0 || e.Target >= w.Enemies.Length) break;
                    var kind = (HordeWindUpKind)Mathf.RoundToInt(e.B);
                    if (_winds.Count >= 64) _winds.RemoveAt(0);
                    _winds.Add(new Wind { Enemy = e.Target, Serial = w.Enemies[e.Target].Serial, Target = e.Slot, Kind = kind, From = new Vector2(e.X, e.Y), To = new Vector2(e.X2, e.Y2), Start = t, Seconds = Mathf.Max(0.05f, e.Value / (float)Mathf.Max(1, w.TickRate)) });
                    if (kind == HordeWindUpKind.Charge) { Arrows++; LastArrowLen = e.C; } else { AimLines++; LastAimLen = e.C; }
                    break;
                }
                case HordeEventKind.Nova:
                    // B2: a nova's (an aura pulse's) ring out to its radius
                    if (e.A > 0.05f && e.Slot >= 0) { AddRing(new Vector2(e.X, e.Y), -1, e.A * NovaFrom, e.A, NovaSeconds, RingCells * S, WeaponColor(c, e.DefId, e.Slot == me), false); NovaRings++; }
                    break;
                case HordeEventKind.Strike:
                {
                    if (e.A <= 0.05f) break;
                    // B3: an area struck: a bright flash over it and a ring at its real radius (an enemy's in red)
                    var col = e.Slot >= 0 ? WeaponColor(c, e.DefId, e.Slot == me) : Danger;
                    AddRing(new Vector2(e.X, e.Y), -1, 0f, e.A, StrikeFlashSeconds, 0f, Color.Lerp(col, Color.white, 0.6f), true);
                    AddRing(new Vector2(e.X, e.Y), -1, e.A * 0.92f, e.A, StrikeRingSeconds, RingCells * 1.25f * S, col, false);
                    StrikeMarks++;
                    // E4: an elite's / boss's strike landing near me (not on me: the hit has its own rumble)
                    if (e.Slot < 0 && e.Target >= 0 && e.Target < w.Enemies.Length && me >= 0 && _hitFrame != Time.frameCount)
                    {
                        var en = w.Enemies[e.Target]; var d = c?.Enemy(en.DefId);
                        var my = new Vector2(_g.Channel.MyX, _g.Channel.MyY);
                        if (en.Alive && d != null && d.Rank >= EnemyRank.Elite && (my - new Vector2(e.X, e.Y)).magnitude <= e.A + 2.5f) { Haptics.Both(Haptics.Kind.HitTaken, 0.45f, 0.1f); NearStrikeRumbles++; }
                    }
                    break;
                }
                case HordeEventKind.Chain:
                    // B4: a thicker link that stays a moment
                    if (_links.Count >= 32) _links.RemoveAt(0);
                    _links.Add(new Link { A = new Vector2(e.X, e.Y), B = new Vector2(e.X2, e.Y2), Start = t, C = WeaponColor(c, e.DefId, e.Slot == me) });
                    ChainLinks++;
                    break;
                case HordeEventKind.Hit:
                    // E4: a light rumble on my critical hit, on the casting hand (the main hand), at most one every 0.15 s
                    if (e.Slot == me && (e.Flags & HordeEventFlags.Crit) != 0 && (e.Flags & HordeEventFlags.Dot) == 0 && t - _lastCrit >= 0.15f) { _lastCrit = t; Haptics.Pulse(Haptics.Kind.HitDealt, true, 0.22f, 0.03f); CritRumbles++; }
                    break;
            }
        }

        private static Color WeaponColor(HordeContent c, int weaponId, bool mine)
        {
            var wd = c?.Weapon(weaponId);
            var col = World.Fx.ElementColor(wd != null ? (int)wd.Element : 0); col.a = 1f;
            return mine ? col : AllyTint(col);
        }

        private void AddRing(Vector2 pos, int follow, float from, float to, float seconds, float width, Color col, bool fill)
        {
            if (_rings.Count >= 48) _rings.RemoveAt(0);
            _rings.Add(new Ring { Pos = pos, Follow = follow, From = from, To = to, Start = Time.time, Seconds = seconds, Width = width, C = col, Fill = fill });
        }

        /// <summary>D1, D3, D5: I was hurt (client wave 18, h18feel, point 21: nothing in the headset's view any more, D2's arc is gone).
        /// Where from: the host's PlayerHit X2 / Y2 (none from an older host: no direction then).</summary>
        private void MyHit(HordeEvent e, HordeWorldView w, int me)
        {
            float t = Time.time; _hitFrame = Time.frameCount;
            var p = w.Players[me];
            float share = p.MaxHp > 0 ? e.Value / (float)p.MaxHp : 0f, k = Mathf.Clamp01(share / BigHitShare);
            var at = new Vector2(_g.Channel.MyX, _g.Channel.MyY); var from = new Vector2(e.X2, e.Y2);
            bool told = e.A != 0f || e.X2 != 0f || e.Y2 != 0f;
            var d = from - at; bool dir = told && d.sqrMagnitude > 0.0025f;
            LastHitVia = (HordeHitVia)Mathf.RoundToInt(e.A);
            _flashUntil = t + FlashSeconds; MyFlashes++;
            Vector3 world = Vector3.zero; var root = _g.Arena != null ? _g.Arena.Root : null; var cam = Camera.main;
            if (dir && root != null) world = root.TransformDirection(new Vector3(d.x, 0f, d.y)).normalized;
            if (dir)
            {
                float deg = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                if (_wedges.Count >= 8) _wedges.RemoveAt(0);
                _wedges.Add(new Wedge { Deg = deg, Start = t, K = k }); Wedges++; LastWedgeDeg = deg; LastWedgeK = k;
                _leanUntil = t + LeanSeconds; _leanFrom = d; Leans++;
            }
            // D5: the rumble, stronger on the hit's side; its strength grows with the hit's share of my max HP (both hands alike without a side)
            float kk = Mathf.Clamp01(share / 0.2f), amp = 0.3f + 0.5f * kk, secs = 0.06f + 0.06f * kk;
            float side = 0f;
            if (dir && cam != null) { var r = cam.transform.right; r.y = 0f; if (r.sqrMagnitude > 1e-6f) side = Vector3.Dot(world, r.normalized); }
            float right = side > 0.25f ? amp : side < -0.25f ? amp * 0.35f : amp, left = side < -0.25f ? amp : side > 0.25f ? amp * 0.35f : amp;
            // Haptics.Pulse's "main hand" is the right one unless left-handed play is on
            Haptics.Pulse(Haptics.Kind.HitTaken, !Comfort.LeftHanded, right, secs);
            Haptics.Pulse(Haptics.Kind.HitTaken, Comfort.LeftHanded, left, secs);
            LastRumbleLeft = left; LastRumbleRight = right; if (side > 0.25f || side < -0.25f) SideRumbles++;
        }

        /// <summary>Client wave 18 (h18feel), the design's point 24: a level-up is a gold ring on the ground around the runner (out from
        /// <see cref="LevelUpFrom"/> to <see cref="LevelUpTo"/> cells in <see cref="LevelUpSeconds"/> s, following it), in place of RO's
        /// level-up angel (EF_ANGEL 371) that rose over the monsters at every kill's level-up; an ally's paler (B5).</summary>
        public const float LevelUpFrom = 0.5f, LevelUpTo = 1.8f, LevelUpSeconds = 0.6f;
        public static readonly Color LevelUpColor = new Color(1f, 0.85f, 0.3f, 0.95f);
        public int LevelUpRings;
        public void LevelUp(int slot) { Pulse(slot, LevelUpColor); LevelUpRings++; }

        /// <summary>Client wave 18 (h18feel), point 35: the same ring in another colour (a rise: the soul's lavender), for slot.</summary>
        public void Pulse(int slot, Color col)
        {
            var w = _g.Channel?.World; if (w == null || slot < 0 || slot >= w.Players.Length || !w.Players[slot].Present) return;
            int me = _g.Run != null ? _g.Run.Slot : -1; var p = w.Players[slot];
            AddRing(new Vector2(p.X, p.Y), slot, LevelUpFrom, LevelUpTo, LevelUpSeconds, RingCells * 1.5f * Size, slot == me ? col : AllyTint(col), false);
        }

        /// <summary>D4: an ally was hurt: its HP bar flashes, a red ring pulses under it.</summary>
        private void AllyHit(HordeEvent e, float S)
        {
            _g.Players?.TagOf(e.Slot)?.Hurt();
            AddRing(new Vector2(e.X, e.Y), e.Slot, 0.45f, 1.1f, AllyRingSeconds, RingCells * S, Danger, false);
            AllyHurts++;
        }

        /// <summary>C5 for any danger shape (h17boss's telegraphs may call it each frame while my character is inside one): <paramref name="key"/>
        /// tells one danger from another, <paramref name="secondsToStrike"/> how long before it strikes.</summary>
        public void InDanger(int key, float secondsToStrike) { _dangerNow[key] = Mathf.Min(secondsToStrike, _dangerNow.TryGetValue(key, out float s) ? s : float.MaxValue); }

        // ------------------------------------------------------------------ every frame (HordeGame.LateUpdate, after the world is drawn)
        public void Draw(HordeWorldView w, double now, int me, Vector2 myPos)
        {
            var root = _g.Arena != null ? _g.Arena.Root : null; var cam = Camera.main;
            if (root == null || cam == null || w == null) return;
            EnsureMaterials();
            float t = Time.time, a = w.Alpha(now), k = Mathf.Abs(root.lossyScale.x), S = Size;
            var toWorld = root.localToWorldMatrix;
            var arena = _g.Arena;

            // ---- C3 / C4: the wind-ups the host told
            for (int i = _winds.Count - 1; i >= 0; i--)
            {
                var wd = _winds[i]; float age = t - wd.Start;
                var en = wd.Enemy >= 0 && wd.Enemy < w.Enemies.Length ? w.Enemies[wd.Enemy] : null;
                bool alive = en != null && en.Alive && en.Serial == wd.Serial, winding = alive && (en.Flags & HordeEnemyFlags.Winding) != 0;
                bool charging = alive && (en.Flags & HordeEnemyFlags.Charging) != 0;
                // Wave 28 (item 55): keep charge path arrows visible for their full duration (and while charging) even if Winding transitioned to Charging
                bool keepCharge = wd.Kind == HordeWindUpKind.Charge && (charging || age <= wd.Seconds);
                if (!alive || age > wd.Seconds + 0.25f || (age > 0.2f && !winding && !keepCharge)) { _winds.RemoveAt(i); continue; }
                float prog = Mathf.Clamp01(age / wd.Seconds);
                if (wd.Kind == HordeWindUpKind.Charge)
                {
                    // the arrow along the dash's path: dark under it, the whole path faint, filling up to the dash
                    var dirv = wd.To - wd.From; float len = dirv.magnitude; if (len < 0.2f) continue;
                    float shaft = ArrowShaftCells * S, head = ArrowHeadCells * S, headLen = Mathf.Min(ArrowHeadLength * S, len * 0.5f);
                    DrawMesh(_pool.Arrow(wd.From, wd.To, shaft * 1.6f, head * 1.25f, headLen, ArrowLift * 0.6f, arena.GroundHeight), toWorld, _under, new Color(0.03f, 0.02f, 0.02f, 0.55f));
                    DrawMesh(_pool.Arrow(wd.From, wd.To, shaft, head, headLen, ArrowLift, arena.GroundHeight), toWorld, _solid, new Color(Danger.r, Danger.g, Danger.b, 0.38f));
                    var tip = wd.From + dirv * Mathf.Max(0.05f, prog);
                    DrawMesh(_pool.Arrow(wd.From, tip, shaft * 0.75f, prog > 0.95f ? head * 0.85f : 0f, prog > 0.95f ? headLen * 0.85f : 0f, ArrowLift + 0.01f, arena.GroundHeight), toWorld, _solid, new Color(Danger.r, Danger.g, Danger.b, 0.90f));
                    ArrowsDrawn++;
                }
                else
                {
                    // the aim line from the shooter to the player it aims at, as both are drawn now
                    var from = new Vector2(en.PrevX + (en.X - en.PrevX) * a, en.PrevY + (en.Y - en.PrevY) * a);
                    Vector2 to = wd.To;
                    if (wd.Target == me && me >= 0) to = myPos;
                    else if (wd.Target >= 0 && wd.Target < w.Players.Length && w.Players[wd.Target].Present) { var tp = w.Players[wd.Target]; to = new Vector2(tp.PrevX + (tp.X - tp.PrevX) * a, tp.PrevY + (tp.Y - tp.PrevY) * a); }
                    float alpha = 0.45f + 0.45f * prog, wid = AimLineCells * S;
                    DrawMesh(_pool.Ribbon(arena.Local(from.x, from.y) + Vector3.up * AimLift, arena.Local(to.x, to.y) + Vector3.up * AimLift, wid * 2.2f), toWorld, _under, new Color(0.03f, 0.02f, 0.02f, alpha * 0.6f));
                    DrawMesh(_pool.Ribbon(arena.Local(from.x, from.y) + Vector3.up * (AimLift + 0.01f), arena.Local(to.x, to.y) + Vector3.up * (AimLift + 0.01f), wid), toWorld, _solid, new Color(Danger.r, Danger.g, Danger.b, alpha));
                    AimLinesDrawn++;
                }
            }

            // ---- B2 / B3 / D4: rings and flashes
            for (int i = _rings.Count - 1; i >= 0; i--)
            {
                var r = _rings[i]; float u = (t - r.Start) / r.Seconds;
                if (u >= 1f) { _rings.RemoveAt(i); continue; }
                var pos = r.Pos;
                if (r.Follow >= 0 && r.Follow < w.Players.Length && w.Players[r.Follow].Present) { var fp = w.Players[r.Follow]; pos = r.Follow == me ? myPos : new Vector2(fp.PrevX + (fp.X - fp.PrevX) * a, fp.PrevY + (fp.Y - fp.PrevY) * a); }
                float ease = 1f - (1f - u) * (1f - u), radius = Mathf.Lerp(r.From, r.To, ease);
                var m = toWorld * Matrix4x4.Translate(arena.Local(pos.x, pos.y) + Vector3.up * 0.08f);
                var col = r.C; col.a *= r.Fill ? 0.55f * (1f - u) : 1f - u;
                if (r.Fill) DrawMesh(_pool.Annulus(0f, Mathf.Max(0.05f, radius), 0f, 360f), m, _solid, col);
                else DrawMesh(_pool.Annulus(Mathf.Max(0f, radius - r.Width), radius, 0f, 360f), m, _solid, col);
            }

            // ---- B4: chain links
            for (int i = _links.Count - 1; i >= 0; i--)
            {
                var l = _links[i]; float u = (t - l.Start) / ChainSeconds;
                if (u >= 1f) { _links.RemoveAt(i); continue; }
                var pa = arena.Local(l.A.x, l.A.y) + Vector3.up * ChainLift; var pb = arena.Local(l.B.x, l.B.y) + Vector3.up * ChainLift;
                var col = l.C; col.a = 0.9f * (1f - u);
                DrawMesh(_pool.Ribbon(pa - Vector3.up * 0.01f, pb - Vector3.up * 0.01f, ChainCells * S * 1.5f), toWorld, _under, new Color(0.03f, 0.03f, 0.05f, 0.5f * (1f - u)));
                DrawMesh(_pool.Ribbon(pa, pb, ChainCells * S), toWorld, _solid, col);
                DrawMesh(_pool.Ribbon(pa + Vector3.up * 0.01f, pb + Vector3.up * 0.01f, ChainCells * S * 0.4f), toWorld, _solid, new Color(1f, 1f, 1f, 0.9f * (1f - u)));
            }

            // ---- D1: the wedges around me
            for (int i = _wedges.Count - 1; i >= 0; i--)
            {
                var wg = _wedges[i]; float u = (t - wg.Start) / WedgeSeconds;
                if (u >= 1f) { _wedges.RemoveAt(i); continue; }
                float pop = 1f + 0.15f * Mathf.Clamp01(1f - (t - wg.Start) / 0.12f);
                float inner = WedgeInner * pop, outer = inner + WedgeCells * S * (1f + 0.4f * wg.K), span = Mathf.Lerp(WedgeDeg, WedgeDegBig, wg.K);
                var m = toWorld * Matrix4x4.Translate(arena.Local(myPos.x, myPos.y) + Vector3.up * 0.1f);
                DrawMesh(_pool.Annulus(inner - 0.06f, outer + 0.06f, wg.Deg - span * 0.5f - 3f, span + 6f), m, _under, new Color(0.03f, 0.02f, 0.02f, 0.5f * (1f - u)));
                DrawMesh(_pool.Annulus(inner, outer, wg.Deg - span * 0.5f, span), m, _solid, new Color(Danger.r, Danger.g, Danger.b, (0.55f + 0.4f * wg.K) * (1f - u)));
            }

            // ---- D3: my character's red flash and lean (its own sprite; HordePlayers sets the other tints every frame)
            var unit = _g.Players?.UnitOf(me);
            if (unit != null && unit.Sprite != null)
            {
                bool flash = t < _flashUntil;
                if (flash || _tinted) { unit.Sprite.SetTint(Sprites.ActorSprite.TintFactor.Flash, flash ? FlashTint : Color.white); _tinted = flash; }
                var quad = unit.Sprite.CurrentQuad;
                if (quad != null && (t < _leanUntil || _leaning))
                {
                    if (t < _leanUntil)
                    {
                        // as an enemy's knockback lean (HordeWorldDraw): away from the hit, about the axis toward the eye
                        var away = root.TransformDirection(new Vector3(-_leanFrom.x, 0f, -_leanFrom.y)).normalized;
                        float side = Vector3.Dot(cam.transform.right, away), kk = Mathf.Clamp01((_leanUntil - t) / LeanSeconds);
                        quad.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Sign(side) * LeanDeg * kk * Mathf.Abs(side));
                        _leaning = true;
                    }
                    else { quad.localRotation = Quaternion.identity; _leaning = false; }
                }
            }

            // ---- C5: standing in an enemy's area (and any shape h17boss reports through InDanger)
            if (me >= 0 && me < w.Players.Length && w.Players[me].Present && !w.Players[me].Fallen)
            {
                float drawTick = w.DrawTick(now), rate = Mathf.Max(1, w.TickRate), body = _g.Content != null ? _g.Content.Rules.BodyRadius : 0.4f;
                foreach (var fx in w.Fx)
                {
                    if (fx.Kind != HordeFxKind.EnemyWarning) continue;
                    float rr = fx.Radius + body;
                    if ((myPos - new Vector2(fx.X, fx.Y)).sqrMagnitude <= rr * rr) InDanger(fx.Id, (fx.EndTick - drawTick) / rate);
                }
            }
            _stale.Clear();
            foreach (var kv in _dangerTick) if (!_dangerNow.ContainsKey(kv.Key)) _stale.Add(kv.Key);
            foreach (int key in _stale) { _dangerTick.Remove(key); _dangerRumbled.Remove(key); }
            foreach (var kv in _dangerNow)
            {
                if (!_dangerTick.TryGetValue(kv.Key, out float last) || t - last >= WarnTickEvery) { _dangerTick[kv.Key] = t; _g.Feel?.WarnTick(myPos); DangerTicks++; }
                if (kv.Value <= WarnRumbleBefore && kv.Value > -0.1f && _dangerRumbled.Add(kv.Key)) { Haptics.Both(Haptics.Kind.HitTaken, 0.35f, 0.05f); DangerRumbles++; }
            }
            _dangerNow.Clear();
        }

        private void DrawMesh(Mesh mesh, Matrix4x4 m, Material mat, Color c)
        {
            if (mesh == null) return;
            _mpb.SetColor(ColorId, c);
            Graphics.DrawMesh(mesh, m, mat, 0, null, 0, _mpb, false, false);
        }
    }

    /// <summary>Client wave 17 (h17read): meshes made each frame for the readability marks (rings, wedges, ribbons, arrows), in a pool: a mesh
    /// handed out is not handed out again in the same frame (Graphics.DrawMesh draws it at the frame's end, as it is then).</summary>
    internal sealed class HordeMeshPool
    {
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private int _used, _frame = -1;
        private readonly List<Vector3> _v = new List<Vector3>(); private readonly List<Vector2> _uv = new List<Vector2>(); private readonly List<int> _t = new List<int>();

        private Mesh Next()
        {
            if (Time.frameCount != _frame) { _frame = Time.frameCount; _used = 0; }
            if (_used >= _meshes.Count) { var nm = new Mesh { name = "horde-read" }; nm.MarkDynamic(); _meshes.Add(nm); }
            var m = _meshes[_used++]; m.Clear(); _v.Clear(); _uv.Clear(); _t.Clear();
            return m;
        }

        private Mesh Done(Mesh m, float size)
        {
            for (int i = 0; i < _v.Count; i++) _uv.Add(new Vector2(0.5f, 0.5f));
            m.SetVertices(_v); m.SetUVs(0, _uv); m.SetTriangles(_t, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * (size * 2f + 2f));
            return m;
        }

        /// <summary>A flat ring segment in the XZ plane (cells): radii <paramref name="rIn"/> to <paramref name="rOut"/>, from <paramref name="fromDeg"/>
        /// over <paramref name="spanDeg"/> (0 = +X east, 90 = +Z north); rIn 0 = a filled disc (segment).</summary>
        public Mesh Annulus(float rIn, float rOut, float fromDeg, float spanDeg)
        {
            var m = Next();
            int seg = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(spanDeg) / 6f), 4, 64);
            for (int s = 0; s <= seg; s++)
            {
                float a = (fromDeg + spanDeg * s / seg) * Mathf.Deg2Rad, cx = Mathf.Cos(a), cz = Mathf.Sin(a);
                _v.Add(new Vector3(cx * rIn, 0f, cz * rIn)); _v.Add(new Vector3(cx * rOut, 0f, cz * rOut));
            }
            for (int s = 0; s < seg; s++) { int o = s * 2; _t.Add(o); _t.Add(o + 1); _t.Add(o + 3); _t.Add(o); _t.Add(o + 3); _t.Add(o + 2); }
            return Done(m, rOut);
        }

        /// <summary>A flat ribbon from <paramref name="a"/> to <paramref name="b"/> (the map's local frame), <paramref name="width"/> wide across the XZ direction.</summary>
        public Mesh Ribbon(Vector3 a, Vector3 b, float width)
        {
            var m = Next();
            var d = new Vector3(b.x - a.x, 0f, b.z - a.z); if (d.sqrMagnitude < 1e-8f) d = Vector3.right;
            var side = new Vector3(-d.z, 0f, d.x).normalized * (width * 0.5f);
            _v.Add(a - side); _v.Add(a + side); _v.Add(b - side); _v.Add(b + side);
            _t.Add(0); _t.Add(1); _t.Add(3); _t.Add(0); _t.Add(3); _t.Add(2);
            return DoneAt(m, a, b);
        }

        /// <summary>An arrow on the ground from <paramref name="from"/> to <paramref name="to"/> (map cells), its shaft and head widths, the head's length,
        /// <paramref name="lift"/> cells over the ground at every vertex (<paramref name="height"/>: the ground's local height of a cell); head 0 = a shaft only.
        /// Wave 28 (item 55): subdivided along its length so it drapes over hills and slopes without clipping into the terrain.</summary>
        public Mesh Arrow(Vector2 from, Vector2 to, float shaft, float head, float headLen, float lift, System.Func<float, float, float> height)
        {
            var m = Next();
            var d = to - from; float len = d.magnitude; if (len < 1e-4f) { d = Vector2.right; len = 1e-4f; }
            var u = d / len; var n = new Vector2(-u.y, u.x);
            float bodyLen = head > 0f ? Mathf.Max(0f, len - headLen) : len;
            System.Func<Vector2, Vector3> P = q => new Vector3(q.x, height(q.x, q.y) + lift, q.y);
            int seg = Mathf.Clamp(Mathf.CeilToInt(bodyLen / 0.75f), 1, 24);
            for (int s = 0; s <= seg; s++)
            {
                var p = from + u * (bodyLen * s / seg);
                _v.Add(P(p - n * shaft * 0.5f));
                _v.Add(P(p + n * shaft * 0.5f));
            }
            for (int s = 0; s < seg; s++)
            {
                int o = s * 2;
                _t.Add(o); _t.Add(o + 1); _t.Add(o + 3);
                _t.Add(o); _t.Add(o + 3); _t.Add(o + 2);
            }
            if (head > 0f)
            {
                var b1 = from + u * bodyLen;
                int ho = _v.Count;
                _v.Add(P(b1 - n * head * 0.5f)); _v.Add(P(b1 + n * head * 0.5f)); _v.Add(P(to));
                _t.Add(ho); _t.Add(ho + 1); _t.Add(ho + 2);
            }
            return DoneAt(m, P(from), P(to));
        }

        private Mesh DoneAt(Mesh m, Vector3 a, Vector3 b)
        {
            for (int i = 0; i < _v.Count; i++) _uv.Add(new Vector2(0.5f, 0.5f));
            m.SetVertices(_v); m.SetUVs(0, _uv); m.SetTriangles(_t, 0);
            var bounds = new Bounds((a + b) * 0.5f, Vector3.zero); bounds.Encapsulate(a); bounds.Encapsulate(b); bounds.Expand(4f);
            m.bounds = bounds;
            return m;
        }
    }
}
