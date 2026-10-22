using System.Collections.Generic;
using Loki.Sprites;
using UnityEngine;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 8 (w8game): draws everything of a run that comes by the hundred, every frame, with no GameObject per thing:
    /// enemies (the real monster sprites, walking, hurt flash, frozen / stunned tints, elites and bosses at their DrawScale),
    /// their deaths (the monster's own death action, played once where it fell), summons, pickups (the real item sprites,
    /// bobbing), projectiles, zones, warnings, traps and orbiting orbs. Moving things are drawn between the last two snapshots
    /// (HordeWorldView.Alpha); projectiles and orbs from their start values (they are sent once).
    /// Cost bounds (Loki's own, measured in design notes and design notes): enemies cast no shadows, dead
    /// bodies are kept at most <see cref="MaxCorpses"/>, pickups beyond <see cref="MaxPickupsDrawn"/> are not drawn.
    ///
    /// Client wave 9 (h9feel), "every hit is seen": a struck enemy flashes white for <see cref="FlashSeconds"/> (a tint in the
    /// DrawMesh material); a knocked-back enemy leans and squashes for <see cref="PushSeconds"/> (from the engine's Effect event
    /// or from a displacement no walk could make); a dying monster pops (its death frames at a short scale pop, then a fade, and
    /// a small burst of sparks in its element's colour) and its death frames' own ACT sound plays; pickups inside a player's
    /// magnet reach fly to it with an ease-in (the engine pulls them at 12 cells/s but the snapshot never resends a moved
    /// pickup, so the flight is predicted here: nearest standing player within its reach, the Magnet pickup pulls every EXP
    /// gem); shots are a thin bright head with a streak and a dark outline (readable at table scale, no bloom); zones and
    /// warnings are thin high-contrast rings over a dark ring, an enemy's warning a disc that fills up to the strike; a winding
    /// elite / boss gets a pulsing ground ring (the telegraph) and a small HP bar over its head.
    ///
    /// Client wave 17 (h17read), the readability proposal (point 28; numbers in <see cref="HordeReadability"/>): shots by side (A1 sizes,
    /// A2 colours: enemies red-orange on black, mine the element with a white core, allies paler with a cyan rim), trails of 0.35 s
    /// (enemies) / 0.15 s (A3), a shadow on the ground under every shot (A4), an enemy shot that will pass within 1.5 cells of me glows
    /// and gets a red ring (A5); an ally's zones, traps, warnings and orbs are paler and cyan (B5); every winding enemy shows a red "!"
    /// (C1); an enemy's area has a bright 0.12-cell edge and flashes white in its last 0.2 s (C2). The dark outlines are drawn UNDER the
    /// glow now (their material's queue: before, Loki/Str's later queue dimmed the marks they framed).
    /// </summary>
    public sealed class HordeWorldDraw
    {
        public const int MaxCorpses = 60, MaxPickupsDrawn = 250, MaxPops = 24;
        /// <summary>Loki's own feel numbers: the hit flash, the knockback lean, the death pop, the pickup flight.</summary>
        public const float FlashSeconds = 0.08f, PushSeconds = 0.25f, PushLeanDeg = 16f, PopSeconds = 0.18f, FlyAccel = 48f, FlyMaxSpeed = 40f;
        /// <summary>Client wave 18 (h18feel), the design's point 24 ("the little animation ... of ghostly apparition flying up off of a dead monster
        /// on horde mode is occluding other monsters"). FOUND by the smoke h18feel (a death in front of a living monster, frame by frame,
        /// shot_h18feel_r_h18feel_death*): (1) the kill's EXP made a level-up and RO's level-up angel (EF_ANGEL 371) rose over the runner,
        /// white, about 4 cells high, over every monster near (HordeGame's LevelUp); (2) the body kept writing depth while it faded: a
        /// see-through "ghost" that cut the living monster behind it. Now a death is quiet and BEHIND the living: its body is drawn before
        /// them without depth (HordeSprites.CorpseMaterialFor), at <see cref="DeathScale"/> of its size (no pop), <see cref="DeathAlpha"/> at
        /// most, in half its time (its death frames at <see cref="DeathSpeed"/>, a <see cref="DeathFadeSeconds"/> s fade, was 0.6); its sparks
        /// smaller (<see cref="SparkSize"/>), low (they rise <see cref="SparkRise"/> cell, was 0.6) and half as long (<see cref="PopSeconds"/>,
        /// was 0.35), drawn before the living too; a level-up is a gold ring on the ground (HordeReadability.LevelUp), not the angel.</summary>
        public const float DeathScale = 0.85f, DeathAlpha = 0.7f, DeathSpeed = 2f, DeathFadeSeconds = 0.3f, SparkRise = 0.2f, SparkSize = 0.6f;
        public bool EnemyShadows;
        private readonly HordeSprites _sprites;
        private readonly HordeArena _arena;
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private readonly float[] _phase = new float[HordeLimits.MaxEnemies];
        private readonly int[] _serial = new int[HordeLimits.MaxEnemies], _dir = new int[HordeLimits.MaxEnemies], _evFrame = new int[HordeLimits.MaxEnemies];
        private readonly float[] _flashUntil = new float[HordeLimits.MaxEnemies], _pushUntil = new float[HordeLimits.MaxEnemies];
        private readonly Vector2[] _pushDir = new Vector2[HordeLimits.MaxEnemies];
        private struct Corpse { public HordeSprites.Body Body; public Vector2 Pos; public int Dir; public float Start; public float Scale; public int LastEvFrame; }
        private struct Pop { public Vector2 Pos; public float Start; public Color C; public float Size; }
        private struct Fly { public Vector2 Pos; public float Speed; public int Taker; public float Seen; public int ItemId; public PickupKind Kind; public float Gone; public bool Landed; }
        private readonly List<Corpse> _corpses = new List<Corpse>();
        private readonly List<Pop> _pops = new List<Pop>();
        private readonly Dictionary<int, Fly> _fly = new Dictionary<int, Fly>();
        private readonly List<int> _flyGone = new List<int>();
        private readonly List<(int, Fly)> _flyUpdates = new List<(int, Fly)>();
        private readonly float[] _reach = new float[HordeLimits.MaxPlayers];
        private readonly Vector2[] _playerPos = new Vector2[HordeLimits.MaxPlayers];
        private Mesh _quad; private Material _glow, _ring, _thinRing, _dark, _darkRing, _disc;
        private Material _under, _solid, _solidUnder, _bang, _bangUnder, _shadow, _shotRim, _shotHead, _shotCore, _trail; // (client wave 17, h17read)
        private Material _sparks; // (client wave 18, h18feel)
        public int EnemiesDrawn { get; private set; }
        public int DrawCalls { get; private set; }
        /// <summary>How many distinct enemy kinds (EnemyDef ids) were drawn so far this run (the smoke's first-appearance measurement).</summary>
        public int KindsDrawn { get; private set; }
        private readonly bool[] _kindSeen = new bool[1024];
        // counters for the log and the smoke (client wave 9)
        public int Flashes { get; private set; }
        public int Pushes { get; private set; }
        public int Deaths { get; private set; }
        /// <summary>Client wave 18 (h18feel), point 24: deaths drawn now (bodies, spark bursts), and the newest body's age (s), alpha and scale.</summary>
        public int CorpsesLive => _corpses.Count;
        public int PopsLive => _pops.Count;
        public Vector3 LastCorpse { get; private set; }
        public int PickupsFlying { get; private set; }
        public int PickupsFlown { get; private set; }
        public int BossBarsDrawn { get; private set; }
        public int TelegraphsDrawn { get; private set; }
        /// <summary>A monster's ACT frame sound (".wav" name, the arena cell it is at): HordeFeel plays it through the arena's listener.</summary>
        public System.Action<string, Vector2> Sound;

        public HordeRunInfo Run;
        public struct ActiveAimState { public bool Active, Ready, IsWall; public Vector2 Cell; public float Radius, CooldownFrac, Angle; public int Hand; public string Name; }
        public ActiveAimState AimMain, AimOff;
        public void SetActiveAim(int hand, bool active, Vector2 cell, float radius, bool ready, float cdFrac, string name, bool isWall = false, float angle = 0f)
        {
            var st = new ActiveAimState { Active = active, Ready = ready, IsWall = isWall, Cell = cell, Radius = radius, CooldownFrac = cdFrac, Angle = angle, Hand = hand, Name = name };
            if (hand == 0) AimMain = st; else AimOff = st;
        }

        public HordeWorldDraw(HordeSprites sprites, HordeArena arena) { _sprites = sprites; _arena = arena; }

        public void Clear()
        {
            _corpses.Clear(); _pops.Clear(); _fly.Clear(); _slices.Clear();
            AimMain = default; AimOff = default;
            _shotSeen.Clear(); _nearSeen.Clear(); _whooshed.Clear(); _warnSeen.Clear(); _flashSeen.Clear(); // (client wave 17, h17read)
            for (int i = 0; i < _serial.Length; i++) { _serial[i] = -1; _flashUntil[i] = 0f; _pushUntil[i] = 0f; _evFrame[i] = -1; }
            KindsDrawn = 0; System.Array.Clear(_kindSeen, 0, _kindSeen.Length);
        }

        // ------------------------------------------------------------------ client wave 15 (w15hfx), point 14 of the design's wave 14 list
        // "the basic melee attack ... does not look good. It needs to have some kind of effect that looks like a slice instead of just a
        // little burst ... it really needs to be apparent where the attack is going, where it is aiming." (the readability proposal's B1)
        /// <summary>The slice's numbers (Loki's own): the sweep across the arc, then the fade; the inner edge at this share of the reach;
        /// drawn this high over the ground (cells); at most this many at once (the oldest is reused).</summary>
        public const float SliceSweepSeconds = 0.12f, SliceFadeSeconds = 0.2f, SliceInner = 0.3f, SliceLift = 0.2f;
        public const int MaxSlices = 24;
        /// <summary>Client wave 18 (h18feel), the design's point 25 ("if I am standing on top of a hill and I strike an enemy that is slightly below
        /// me, it doesn't hit"): FOUND (smoke h18feel on moc_fild07's dunes) the engine has no height at all (a swing hits by the flat distance
        /// and the arc, HordeSim.Weapons.cs MeleeArc: hits below and above alike, none past the reach), but the slice was drawn FLAT at the
        /// swinger's feet: on a slope it floated over the enemies below (they looked missed) and sank under the ground of those above. Now it
        /// is laid over the ground: <see cref="SliceRings"/> rings from the inner edge to the reach, each point at the ground under it
        /// (HordeArena.GroundHeight) + <see cref="SliceLift"/>, within ± <see cref="SliceDrapeCells"/> of the swinger's ground (the limit).</summary>
        public const int SliceRings = 4;
        public const float SliceDrapeCells = 2.5f;
        /// <summary>Mine: white-gold; an ally's: very faint pale cyan (the proposal's B1 / B5).</summary>
        public static readonly Color SliceMine = new Color(1f, 0.9f, 0.55f, 1f), SliceAlly = new Color(0.6f, 0.9f, 1f, 0.18f);
        private struct SliceFx { public Vector2 Pos; public float Aim, Arc, Reach, Start; public bool Mine; public int Owner; public bool Flip; }
        private readonly List<SliceFx> _slices = new List<SliceFx>();
        private readonly List<Mesh> _sliceMeshes = new List<Mesh>();
        private bool _flip;
        /// <summary>Slices drawn so far, and the last one's aim / arc / reach (the smoke).</summary>
        public int Slices { get; private set; }
        public Vector3 LastSlice { get; private set; }
        public int SlicesLive => _slices.Count;
        /// <summary>Client wave 18 (h18feel), point 25: the newest slice's mesh and the matrix it was drawn with this frame (where it is on the
        /// slopes against what it hits).</summary>
        public Mesh LastSliceMesh { get; private set; }
        public Matrix4x4 LastSliceMatrix { get; private set; }

        /// <summary>A melee swing of <paramref name="owner"/> at (x, y): aimed at <paramref name="aimDeg"/> (0 east, 90 north), <paramref name="arcDeg"/>
        /// wide, <paramref name="reach"/> cells long (the host's Swing event: the engine's real hit area). It sweeps across the arc, then fades;
        /// it follows its owner while it lasts.</summary>
        public void Slice(int owner, float x, float y, float aimDeg, float arcDeg, float reach, bool mine)
        {
            if (reach <= 0.05f) return;
            if (_slices.Count >= MaxSlices) _slices.RemoveAt(0);
            _flip = !_flip; // one swing left to right, the next right to left
            _slices.Add(new SliceFx { Pos = new Vector2(x, y), Aim = aimDeg, Arc = Mathf.Clamp(arcDeg, 10f, 360f), Reach = reach, Start = Time.time, Mine = mine, Owner = owner, Flip = _flip });
            Slices++; LastSlice = new Vector3(aimDeg, arcDeg, reach);
        }

        /// <summary>The live slices: a crescent from <see cref="SliceInner"/> × reach to the reach over the arc, its leading edge bright (the
        /// sweep), a dark rim under it for contrast on any ground; then the whole crescent fades.</summary>
        private void DrawSlices(HordeWorldView w, int mySlot, Vector2? myPos, float a)
        {
            var root = _arena.Root; float t = Time.time;
            for (int i = _slices.Count - 1; i >= 0; i--)
            {
                var sl = _slices[i]; float age = t - sl.Start;
                if (age > SliceSweepSeconds + SliceFadeSeconds) { _slices.RemoveAt(i); continue; }
                // it follows its owner (the swing is around the swinger)
                var pos = sl.Pos;
                if (sl.Owner >= 0 && sl.Owner < w.Players.Length && w.Players[sl.Owner].Present)
                {
                    var op = w.Players[sl.Owner];
                    pos = sl.Owner == mySlot && myPos.HasValue ? myPos.Value : new Vector2(op.PrevX + (op.X - op.PrevX) * a, op.PrevY + (op.Y - op.PrevY) * a);
                }
                float sweep = Mathf.Clamp01(age / SliceSweepSeconds); sweep = 1f - (1f - sweep) * (1f - sweep); // ease out
                float fade = 1f - Mathf.Clamp01((age - SliceSweepSeconds) / SliceFadeSeconds);
                while (_sliceMeshes.Count <= i) _sliceMeshes.Add(new Mesh { name = "horde-slice" });
                var mesh = _sliceMeshes[i];
                var col = sl.Mine ? SliceMine : SliceAlly;
                BuildSlice(mesh, sl, pos, sweep, fade, col); // (client wave 18, h18feel: laid over the ground under it, point 25)
                var m = root.localToWorldMatrix * Matrix4x4.Translate(_arena.Local(pos.x, pos.y) + Vector3.up * SliceLift);
                if (i == _slices.Count - 1) { LastSliceMesh = mesh; LastSliceMatrix = m; } // (client wave 18, h18feel: the newest slice as drawn, point 25)
                _mpb.SetColor(ColorId, new Color(0.03f, 0.03f, 0.05f, 0.45f * fade));
                Graphics.DrawMesh(mesh, m * Matrix4x4.Scale(new Vector3(1.04f, 1f, 1.04f)), _under, 0, null, 0, _mpb, false, false); // (client wave 17, h17read: under the glow, not over it)
                _mpb.SetColor(ColorId, Color.white);
                Graphics.DrawMesh(mesh, m, _glow, 0, null, 0, _mpb, false, false);
                DrawCalls += 2;
            }
        }

        /// <param name="at">The swinger's place (map cells): the mesh is drawn from its ground (HordeArena.Local); each point's height is the
        /// ground under it less that (client wave 18, h18feel, point 25), so the crescent lies on the slope.</param>
        private void BuildSlice(Mesh mesh, SliceFx sl, Vector2 at, float sweep, float fade, Color col)
        {
            int seg = Mathf.Clamp(Mathf.CeilToInt(sl.Arc / 6f), 6, 60), rings = SliceRings;
            int nv = (seg + 1) * rings;
            var v = new Vector3[nv]; var c = new Color[nv]; var u = new Vector2[nv]; var tri = new int[seg * (rings - 1) * 6];
            float half = sl.Arc * 0.5f, from = sl.Flip ? sl.Aim + half : sl.Aim - half, span = (sl.Flip ? -1f : 1f) * sl.Arc * sweep;
            float rIn = sl.Reach * SliceInner, rOut = sl.Reach, baseY = _arena.Height(at.x, at.y);
            for (int s = 0; s <= seg; s++)
            {
                float f = s / (float)seg; // 0 the trailing edge, 1 the leading edge
                float ang = (from + span * f) * Mathf.Deg2Rad; float cx = Mathf.Cos(ang), cy = Mathf.Sin(ang);
                float lead = 0.15f + 0.85f * f * f; // bright at the leading edge, a fading tail
                float alpha = Mathf.Clamp01(lead * fade);
                for (int q = 0; q < rings; q++)
                {
                    float k = q / (float)(rings - 1), r = Mathf.Lerp(rIn, rOut, k);
                    float y = Mathf.Clamp(_arena.GroundHeight(at.x + cx * r, at.y + cy * r) - baseY, -SliceDrapeCells, SliceDrapeCells);
                    int i = s * rings + q;
                    v[i] = new Vector3(cx * r, y, cy * r);
                    c[i] = new Color(col.r, col.g, col.b, col.a * alpha * Mathf.Lerp(0.35f, 1f, k)); // the inner edge faint, the reach bright (as before)
                    u[i] = new Vector2(0.5f, 0.5f); // the textures' full centre: the colour alone draws
                }
            }
            int t = 0;
            for (int s = 0; s < seg; s++)
                for (int q = 0; q < rings - 1; q++)
                {
                    int b = s * rings + q, n = b + rings;
                    tri[t++] = b; tri[t++] = b + 1; tri[t++] = n + 1; tri[t++] = b; tri[t++] = n + 1; tri[t++] = n;
                }
            mesh.Clear(); mesh.vertices = v; mesh.colors = c; mesh.uv = u; mesh.triangles = tri;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (rOut * 2.2f + 2f * SliceDrapeCells + 1f));
        }

        /// <summary>An enemy was hit: its sprite flashes white for <see cref="FlashSeconds"/>.</summary>
        public void Flash(int slot) { if (slot >= 0 && slot < _flashUntil.Length) { _flashUntil[slot] = Time.time + FlashSeconds; Flashes++; } }

        /// <summary>An enemy was knocked back from (fromX, fromY) (the engine's Effect event; its new place is in the view): it leans away for <see cref="PushSeconds"/>.</summary>
        public void Push(int slot, Vector2 dir)
        {
            if (slot < 0 || slot >= _pushUntil.Length || dir.sqrMagnitude < 1e-6f) return;
            _pushUntil[slot] = Time.time + PushSeconds; _pushDir[slot] = dir.normalized; Pushes++;
        }

        /// <summary>An enemy died at (x, y): its death action plays there once (with a pop), and sparks fly.</summary>
        public void Died(HordeContent content, int enemyDefId, float x, float y, int slot)
        {
            var def = content?.Enemy(enemyDefId); if (def == null) return;
            if (_corpses.Count >= MaxCorpses) _corpses.RemoveAt(0);
            int dir = slot >= 0 && slot < _dir.Length ? _dir[slot] : 0;
            var body = _sprites.Mob(def.MobId);
            string deathWav = _sprites.DeathEvent(body, (dir + ActorSprite.CameraDirection) & 7);
            if (!string.IsNullOrEmpty(deathWav)) Sound?.Invoke(deathWav, new Vector2(x, y));
            _corpses.Add(new Corpse { Body = body, Pos = new Vector2(x, y), Dir = dir, Start = Time.time, Scale = def.DrawScale / 100f, LastEvFrame = string.IsNullOrEmpty(deathWav) ? -1 : 999 });
            if (_pops.Count >= MaxPops) _pops.RemoveAt(0);
            var c = Color.Lerp(World.Fx.ElementColor((int)def.Element), new Color(1f, 0.95f, 0.75f), 0.45f);
            _pops.Add(new Pop { Pos = new Vector2(x, y), Start = Time.time, C = c, Size = Mathf.Clamp(def.Radius * 1.6f, 0.5f, 2.2f) * (def.Rank >= EnemyRank.Elite ? 1.6f : 1f) });
            Deaths++;
        }

        /// <summary>A Magnet pickup was taken by <paramref name="slot"/>: every EXP pickup lying around flies to that player (the engine's rule).</summary>
        public void Magnet(HordeWorldView w, HordeContent content, int slot)
        {
            if (w == null || content == null || slot < 0) return;
            foreach (var p in w.Pickups)
            {
                var pd = content.Pickup(p.DefId); if (pd == null || pd.Kind != PickupKind.Exp) continue;
                if (!_fly.TryGetValue(p.Id, out var f)) f = new Fly { Pos = new Vector2(p.X, p.Y), ItemId = pd.ItemId, Kind = pd.Kind };
                f.Taker = slot; f.Seen = Time.time; f.Gone = -1f;
                _fly[p.Id] = f;
            }
        }

        /// <summary>Wave 27 (wfix): whether pickup <paramref name="pickupId"/> is drawn in flight now: its drawn place (cells) and whether it has
        /// landed on its taker (the w9feel smoke follows single pickups).</summary>
        public bool Flight(int pickupId, out Vector2 pos, out bool landed)
        {
            if (_fly.TryGetValue(pickupId, out var f)) { pos = f.Pos; landed = f.Landed; return true; }
            pos = Vector2.zero; landed = false; return false;
        }

        /// <summary>A pickup's flight is over (the engine took it): the gem is drawn landing on its taker for a moment, not cut mid-air.</summary>
        public void PickupTaken(int pickupId) { if (_fly.TryGetValue(pickupId, out var f) && f.Gone < 0f) { f.Gone = Time.time; _fly[pickupId] = f; } }

        private void EnsureMeshes()
        {
            if (_quad != null) return;
            _quad = new Mesh { name = "horde-quad" };
            _quad.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) };
            _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            _quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _quad.RecalculateBounds(); _quad.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            _glow = new Material(World.Fx.Glow) { mainTexture = World.Fx.DotTexture(), name = "horde-dot" };
            _ring = new Material(World.Fx.Glow) { mainTexture = World.Fx.RingTexture(), name = "horde-ring" };
            _thinRing = new Material(World.Fx.Glow) { mainTexture = ThinRingTexture(), name = "horde-thinring" };
            // flat, alpha-blended (not additive): the dark outlines under rings and streaks, the HP bars (Loki/Str with the SrcAlpha / OneMinusSrcAlpha
            // factors; its shader discards pure black, so the dark colour is a very dark blue)
            _dark = new Material(Effects.EffectManager.StrShader) { mainTexture = World.Fx.DotTexture(), name = "horde-dark" };
            _dark.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); _dark.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _darkRing = new Material(Effects.EffectManager.StrShader) { mainTexture = ThinRingTexture(), name = "horde-darkring" };
            _darkRing.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); _darkRing.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _disc = new Material(Effects.EffectManager.StrShader) { mainTexture = DiscTexture(), name = "horde-disc" };
            _disc.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); _disc.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            // client wave 17 (h17read): the dark outlines UNDER the glow. Loki/Str's queue (Transparent+25) drew them after Loki/Glow's
            // (+20), so a shot's dark outline and a slice's dark copy dimmed the bright mark they were meant to frame; drawn first now
            _dark.renderQueue = _darkRing.renderQueue = HordeReadability.UnderQueue;
            _under = new Material(_disc) { name = "horde-under", renderQueue = HordeReadability.UnderQueue };
            _solid = HordeReadability.SolidMaterial("horde-solid", HordeReadability.SolidQueue);
            _solidUnder = HordeReadability.SolidMaterial("horde-solid-under", HordeReadability.UnderQueue);
            _bang = new Material(Effects.EffectManager.StrShader) { mainTexture = BangTexture(), name = "horde-bang", renderQueue = HordeReadability.SolidQueue + 1 };
            _bang.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); _bang.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _bangUnder = new Material(_bang) { name = "horde-bang-under", renderQueue = HordeReadability.SolidQueue };
            // a shot's head as SOLID discs (crisp edges: an additive dot washed out on bright ground): its rim, the head, mine a white core,
            // in this order (one queue each: the same place, so the distance sort cannot order them); its shadow flat on the ground
            _shadow = new Material(_disc) { name = "horde-shot-shadow", renderQueue = HordeReadability.UnderQueue + 1 };
            _shotRim = new Material(_disc) { name = "horde-shot-rim", renderQueue = HordeReadability.SolidQueue - 3 };
            _shotHead = new Material(_disc) { name = "horde-shot-head", renderQueue = HordeReadability.SolidQueue - 2 };
            _shotCore = new Material(_disc) { name = "horde-shot-core", renderQueue = HordeReadability.SolidQueue - 1 };
            _trail = new Material(_dark) { name = "horde-shot-trail", renderQueue = HordeReadability.UnderQueue + 2 }; // (the soft dot stretched, alpha-blended)
            _sparks = new Material(_glow) { name = "horde-death-sparks", renderQueue = HordeSprites.CorpseQueue }; // (client wave 18, h18feel: a death's sparks before the living, point 24)
        }

        private static Texture2D _thin, _discTex;
        /// <summary>A thin ring (rim at 90-97 % of the radius, no fill): a zone's edge that stays a line at table scale.</summary>
        private static Texture2D ThinRingTexture()
        {
            if (_thin != null) return _thin;
            const int n = 128; _thin = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "horde-thinring" };
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                float rim = Mathf.Clamp01(1f - Mathf.Abs(d - 0.935f) * 28f);
                _thin.SetPixel(x, y, new Color(1, 1, 1, rim));
            }
            _thin.Apply(true); return _thin;
        }
        /// <summary>A soft-edged disc (a warning's fill).</summary>
        private static Texture2D DiscTexture()
        {
            if (_discTex != null) return _discTex;
            const int n = 64; _discTex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "horde-disc" };
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                _discTex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((0.98f - d) * 12f)));
            }
            _discTex.Apply(true); return _discTex;
        }

        /// <summary>A player's pickup reach in cells: the rules' radius × (1 + its PickupRadiusPct from passives and the job's own modifiers), as the engine computes it.</summary>
        public static float Reach(HordeContent c, HordePlayerView p)
        {
            float pct = 0f;
            foreach (var o in p.Passives)
            {
                var pd = c.Passive(o.DefId); if (pd == null) continue;
                int lv = Mathf.Clamp(o.Level, 1, pd.Levels.Length); if (lv <= 0) continue;
                var mods = pd.Levels[lv - 1]; if (mods == null) continue;
                foreach (var m in mods) if (m != null && m.Kind == Mod.PickupRadiusPct) pct += m.Value;
            }
            var job = c.Job(p.JobId);
            if (job != null) foreach (var m in job.Mods) if (m != null && m.Kind == Mod.PickupRadiusPct) pct += m.Value;
            // HordeSim.StepPickups' radius: (PickupRadius + PickupRadiusPerLevel × (level − 1)) × (1 + PickupRadiusPct / 100) (h9move 5a8420af, h9content's growth per level)
            return (c.Rules.PickupRadius + c.Rules.PickupRadiusPerLevel * Mathf.Max(0, p.Level - 1)) * (1f + pct / 100f);
        }

        public void Draw(HordeWorldView w, HordeContent content, double now, int mySlot) => Draw(w, content, now, mySlot, null);

        /// <summary>Wave 27 (wfix): nothing is drawn while set (HordeGame: the run's world waits for the arena's load to pass).</summary>
        public bool Hold { get; set; }

        /// <param name="myPos">My own predicted place (the pickups fly to it, not to the snapshot's place); null = the snapshot's.</param>
        public void Draw(HordeWorldView w, HordeContent content, double now, int mySlot, Vector2? myPos)
        {
            EnemiesDrawn = 0; DrawCalls = 0; PickupsFlying = 0;
            EnsureMeshes(); // (wave 27, wfix: before the hold: the arena's opening frame makes them, not the frame of the first enemies)
            if (Hold) return; // wave 27 (wfix)
            var root = _arena.Root; var cam = Camera.main;
            if (root == null || cam == null || w == null || content == null) return;
            float k = Mathf.Abs(root.lossyScale.x); var camPos = cam.transform.position; var camRight = cam.transform.right;
            float a = w.Alpha(now); float t = Time.time, dt = Time.deltaTime;
            float span = (float)(w.TickAt - w.PrevTickAt); if (span <= 0f) span = 0.1f;
            int camDir = ActorSprite.CameraDirection;
            _flyStart = content.Rules.PickupPullSpeed; _flyAccel = content.Rules.PickupPullAccel; // the engine's flight (protocol 2); the stand-in's prediction uses the same

            // ---- the players' drawn places and pickup reach (the flights)
            for (int i = 0; i < w.Players.Length; i++)
            {
                var p = w.Players[i];
                if (!p.Present || p.Fallen) { _reach[i] = -1f; continue; }
                _playerPos[i] = i == mySlot && myPos.HasValue ? myPos.Value : new Vector2(p.PrevX + (p.X - p.PrevX) * a, p.PrevY + (p.Y - p.PrevY) * a);
                _reach[i] = Reach(content, p);
            }
            Vector2? myRef = myPos;
            if (!myRef.HasValue && mySlot >= 0 && mySlot < w.Players.Length && w.Players[mySlot].Present) myRef = _playerPos[mySlot];
            DrawArenaBounds(content, myRef, root, t);

            // ---- enemies
            for (int i = 0; i < w.Enemies.Length; i++)
            {
                var e = w.Enemies[i];
                if (!e.Alive) continue;
                var def = content.Enemy(e.DefId); if (def == null) continue;
                if (_serial[i] != e.Serial)
                {
                    _serial[i] = e.Serial; _phase[i] = Random.value * 3f; _dir[i] = 0; _evFrame[i] = -1; _pushUntil[i] = 0f; _flashUntil[i] = 0f;
                    if (!_kindSeen[e.DefId & 1023]) { _kindSeen[e.DefId & 1023] = true; KindsDrawn++; }
                }
                float x = e.PrevX + (e.X - e.PrevX) * a, y = e.PrevY + (e.Y - e.PrevY) * a;
                var d = new Vector2(e.X - e.PrevX, e.Y - e.PrevY);
                bool moving = d.sqrMagnitude > 1e-5f;
                // the sprite direction: protocol 2's Dir8 with hysteresis (h9move: World.Prepare sets it each frame) when the view has history, else from the delta
                if (w.HasHistory) _dir[i] = e.Dir8; else if (moving) _dir[i] = Unit.DirFromDelta(d.normalized);
                // a knockback: protocol 2 flags it (Knocked, a decaying impulse) and gives the velocity as drawn (Vx / Vy); without the flag, a displacement no
                // walk could make (and not a dash) is one. Either way the sprite leans away from it
                bool knocked = (e.Flags & HordeEnemyFlags.Knocked) != 0;
                float speedNow = w.HasHistory ? Mathf.Sqrt(e.Vx * e.Vx + e.Vy * e.Vy) : d.magnitude / span;
                if (knocked && (e.Vx * e.Vx + e.Vy * e.Vy) > 1e-4f) Push(i, new Vector2(e.Vx, e.Vy));
                else if (moving && t >= _pushUntil[i] && (e.Flags & HordeEnemyFlags.Charging) == 0 && def.Rank < EnemyRank.Boss && speedNow > Mathf.Max(def.Speed, 0.5f) * 2.2f) Push(i, d);
                bool held = (e.Flags & (HordeEnemyFlags.Frozen | HordeEnemyFlags.Stunned)) != 0;
                bool winding = (e.Flags & HordeEnemyFlags.Winding) != 0;
                int group = held ? 0 : moving ? (int)ActorSprite.MonsterAction.Walk : winding ? (int)ActorSprite.MonsterAction.Attack : (int)ActorSprite.MonsterAction.Idle;
                float scale = def.DrawScale / 100f;
                var body = _sprites.Mob(def.MobId);
                var world = root.TransformPoint(_arena.Local(x, y));
                Color c = (e.Flags & HordeEnemyFlags.Frozen) != 0 ? new Color(0.55f, 0.75f, 1f) : (e.Flags & HordeEnemyFlags.Stunned) != 0 ? new Color(1f, 1f, 0.6f)
                        : (e.Flags & HordeEnemyFlags.Hurting) != 0 ? new Color(1f, 0.75f, 0.75f) : def.Rank == EnemyRank.Elite ? new Color(1f, 0.85f, 0.7f) : Color.white;
                if (t < _flashUntil[i]) c = Color.Lerp(c, new Color(2.4f, 2.4f, 2.4f, 1f), 0.85f); // the hit flash: nearly white, over-bright
                var f = _sprites.Get(body, group, (_dir[i] + camDir) & 7, held ? 0f : (t + _phase[i]) * 1000f, true, (e.Flags & HordeEnemyFlags.Slowed) != 0 ? 0.6f : 1f);
                if (f != null)
                {
                    if (group == (int)ActorSprite.MonsterAction.Attack && f.Event != null && f.Index != _evFrame[i]) { _evFrame[i] = f.Index; Sound?.Invoke(f.Event, new Vector2(x, y)); }
                    else if (group != (int)ActorSprite.MonsterAction.Attack) _evFrame[i] = -1;
                    _mpb.SetColor(ColorId, c);
                    Matrix4x4 m;
                    if (t < _pushUntil[i])
                    {
                        float kk = Mathf.Clamp01((_pushUntil[i] - t) / PushSeconds);
                        var pushWorld = root.TransformDirection(new Vector3(_pushDir[i].x, 0f, _pushDir[i].y));
                        float side = Vector3.Dot(camRight, pushWorld);
                        m = HordeSprites.Matrix(f, world, camPos, k, scale, -Mathf.Sign(side) * PushLeanDeg * kk * Mathf.Abs(side), new Vector2(1f + 0.18f * kk, 1f - 0.14f * kk));
                    }
                    else m = HordeSprites.Matrix(f, world, camPos, k, scale);
                    Graphics.DrawMesh(f.Mesh, m, _sprites.MaterialFor(f.Texture), 0, null, 0, _mpb, EnemyShadows, true);
                }
                else Dot(world + Vector3.up * (0.5f * k), 0.8f * k * scale, new Color(1f, 0.4f, 0.4f, 0.9f), cam);
                DrawCalls++; EnemiesDrawn++;
                if (winding) Bang(world, (f != null ? f.HeightPx * scale / ActorSprite.PixelsPerCell : 1.5f) + (def.Rank >= EnemyRank.Elite ? 0.75f : 0.3f), k, cam); // client wave 17 (h17read), C1: every wind-up shows
                if (def.Rank >= EnemyRank.Elite)
                {
                    // the telegraph: a winding elite / boss pulses its ground ring (something is coming); a dash streaks
                    var ringCol = def.Rank >= EnemyRank.Boss ? new Color(1f, 0.3f, 0.2f, 0.85f) : new Color(1f, 0.7f, 0.3f, 0.8f);
                    float ringD = def.Radius * 2.4f * k;
                    if (winding) { float pulse = 0.5f + 0.5f * Mathf.Sin(t * 18f); ringCol.a = 0.5f + 0.5f * pulse; ringD *= 1f + 0.35f * pulse; TelegraphsDrawn++; }
                    Ring(world, ringD, ringCol, true);
                    // its HP bar over its head
                    float top = (f != null ? f.HeightPx * scale / ActorSprite.PixelsPerCell : 1.5f) + 0.35f;
                    HordeEliteBars.Seen(i, world + Vector3.up * (top * k)); // client wave 18 (h18rules): its HP bar (HordeEliteBars.cs) goes here
                    if (!HordeEliteBars.On) // (client wave 18, h18rules: the thin bar only when the new bars are off)
                    Bar(world + Vector3.up * (top * k), Mathf.Clamp(def.Radius * 2.6f, 1.4f, 3.5f) * k, 0.14f * k, e.Hp / 255f, def.Rank >= EnemyRank.Boss ? new Color(1f, 0.25f, 0.2f, 1f) : new Color(1f, 0.75f, 0.3f, 1f), cam);
                    BossBarsDrawn++;
                }
            }

            // ---- deaths (played once, then fading) and their sparks: quiet and behind the living (client wave 18, h18feel, point 24: DeathScale ...)
            for (int i = _corpses.Count - 1; i >= 0; i--)
            {
                var c = _corpses[i];
                float age = (t - c.Start) * DeathSpeed, len = Mathf.Max(_sprites.LengthMs(c.Body, (int)ActorSprite.MonsterAction.Dead, c.Dir) / 1000f, 0.4f); // (age: the action's own time)
                if (t - c.Start > len / DeathSpeed + DeathFadeSeconds) { _corpses.RemoveAt(i); continue; }
                float fade = Mathf.Clamp01(1f - (t - c.Start - len / DeathSpeed) / DeathFadeSeconds);
                var f = _sprites.Get(c.Body, (int)ActorSprite.MonsterAction.Dead, (c.Dir + camDir) & 7, age * 1000f, false);
                if (f == null) continue;
                if (f.Event != null && f.Index != c.LastEvFrame) { c.LastEvFrame = f.Index; _corpses[i] = c; Sound?.Invoke(f.Event, c.Pos); }
                _mpb.SetColor(ColorId, new Color(1, 1, 1, DeathAlpha * fade));
                if (i == _corpses.Count - 1) LastCorpse = new Vector3(t - c.Start, DeathAlpha * fade, c.Scale * DeathScale); // (client wave 18, h18feel)
                Graphics.DrawMesh(f.Mesh, HordeSprites.Matrix(f, root.TransformPoint(_arena.Local(c.Pos.x, c.Pos.y)), camPos, k, c.Scale * DeathScale), _sprites.CorpseMaterialFor(f.Texture), 0, null, 0, _mpb, false, true);
                DrawCalls++;
            }
            for (int i = _pops.Count - 1; i >= 0; i--)
            {
                var p = _pops[i]; float age = t - p.Start;
                if (age > PopSeconds) { _pops.RemoveAt(i); continue; }
                float u = age / PopSeconds, ease = 1f - (1f - u) * (1f - u);
                var col = p.C; col.a = (1f - u) * 0.75f;
                var centre = _arena.Local(p.Pos.x, p.Pos.y) + Vector3.up * (0.2f + SparkRise * ease);
                for (int s = 0; s < 6; s++)
                {
                    float ang = s * Mathf.PI / 3f + p.Start; float r = p.Size * SparkSize * (0.3f + 1.4f * ease);
                    Dot(root.TransformPoint(centre + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r)), 0.24f * p.Size * SparkSize * (1f - 0.6f * u) * k, col, cam, _sparks);
                }
            }

            // ---- melee slices (client wave 15, w15hfx, point 14)
            DrawSlices(w, mySlot, myPos, a);

            // ---- summons
            foreach (var s in w.Summons)
            {
                var wd = content.Weapon(s.DefId); if (wd == null || wd.SummonMobId == 0) continue;
                float x = s.PrevX + (s.X - s.PrevX) * a, y = s.PrevY + (s.Y - s.PrevY) * a;
                var d = new Vector2(s.X - s.PrevX, s.Y - s.PrevY);
                int dir = d.sqrMagnitude > 1e-5f ? Unit.DirFromDelta(d.normalized) : 0;
                var f = _sprites.Get(_sprites.Mob(wd.SummonMobId), d.sqrMagnitude > 1e-5f ? 1 : 0, (dir + camDir) & 7, t * 1000f, true);
                if (f == null) continue;
                _mpb.SetColor(ColorId, new Color(0.8f, 1f, 0.85f));
                Graphics.DrawMesh(f.Mesh, HordeSprites.Matrix(f, root.TransformPoint(_arena.Local(x, y)), camPos, k, 1f), _sprites.MaterialFor(f.Texture), 0, null, 0, _mpb, false, true);
                DrawCalls++;
            }

            // ---- pickups (the item's sprite, bobbing a little; flying to a player inside its reach)
            int pn = 0;
            foreach (var p in w.Pickups)
            {
                if (pn++ >= MaxPickupsDrawn) break;
                var pd = content.Pickup(p.DefId); if (pd == null) continue;
                Vector3 local; float drawScale = pd.Kind == PickupKind.Exp ? 0.7f : 0.9f;
                // protocol 2 (h9move): the engine says who pulls it and since when (PulledBy / PullTick); the flight starts from where it lay at
                // PickupPullSpeed and gains PickupPullAccel each second, the same kinematics the engine moves it with. Without the field (the
                // offline stand-in) the taker is predicted from the players' reach as before
                if (p.PulledBy >= 0 && (!_fly.TryGetValue(p.Id, out var pulled) || pulled.Taker != p.PulledBy))
                {
                    float since = Mathf.Max(0f, (w.DrawTick(now) - p.PullTick) / Mathf.Max(1, w.TickRate));
                    var start = new Fly { Pos = new Vector2(p.X, p.Y), Speed = _flyStart + _flyAccel * since, Taker = p.PulledBy, Seen = t, ItemId = pd.ItemId, Kind = pd.Kind, Gone = -1f };
                    if (since > 0f) FlyStep(ref start, t, since, out _); // catch up to where the engine has it now
                    _fly[p.Id] = start;
                }
                if (_fly.TryGetValue(p.Id, out var fl))
                {
                    if (fl.Gone >= 0f) { fl.Gone = -1f; } // the engine took it and then it came back in a key snapshot: still here
                    // landed while the engine still lists it (its take is a snapshot away, or the run is paused with the cards open): it stays on
                    // the player, it never flies again from where it lay
                    bool landed = fl.Landed;
                    if (!landed) { local = FlyStep(ref fl, t, dt, out landed); if (landed) { fl.Landed = true; PickupsFlown++; } }
                    else local = FlyStep(ref fl, t, dt, out _);
                    if (landed) drawScale *= 0.6f;
                    fl.Seen = t; _fly[p.Id] = fl; PickupsFlying++;
                }
                else
                {
                    int taker = -1; float best = float.MaxValue;
                    for (int i = 0; i < w.Players.Length; i++)
                    {
                        if (_reach[i] <= 0f) continue;
                        float dd = (_playerPos[i] - new Vector2(p.X, p.Y)).sqrMagnitude;
                        if (dd <= _reach[i] * _reach[i] && dd < best) { best = dd; taker = i; }
                    }
                    if (taker >= 0) { _fly[p.Id] = new Fly { Pos = new Vector2(p.X, p.Y), Speed = _flyStart, Taker = taker, Seen = t, ItemId = pd.ItemId, Kind = pd.Kind, Gone = -1f }; PickupsFlying++; }
                    local = _arena.Local(p.X, p.Y) + Vector3.up * (0.15f + 0.1f * Mathf.Sin(t * 4f + p.Id));
                }
                DrawPickup(root.TransformPoint(local), pd.ItemId, pd.Kind, drawScale, k, camPos, cam);
            }
            // flights whose pickup the engine already took (or that left the view): finish the last stretch instead of vanishing mid-air (at most 0.3 s)
            _flyGone.Clear(); _flyUpdates.Clear();
            foreach (var kv in _fly)
            {
                var fl = kv.Value;
                if (fl.Seen >= t) continue; // drawn above
                if (fl.Gone < 0f) fl.Gone = t;
                bool present = fl.Taker >= 0 && fl.Taker < w.Players.Length && w.Players[fl.Taker].Present;
                if (fl.Landed || t - fl.Gone > 0.3f || !present) { _flyGone.Add(kv.Key); continue; }
                var local = FlyStep(ref fl, t, dt, out bool landed);
                if (landed) { _flyGone.Add(kv.Key); PickupsFlown++; continue; }
                DrawPickup(root.TransformPoint(local), fl.ItemId, fl.Kind, (fl.Kind == PickupKind.Exp ? 0.7f : 0.9f) * 0.8f, k, camPos, cam);
                _flyUpdates.Add((kv.Key, fl));
            }
            foreach (var (id, fl) in _flyUpdates) _fly[id] = fl;
            foreach (int id in _flyGone) _fly.Remove(id);

            // ---- lasting effects
            float drawTick = w.DrawTick(now); float rate = Mathf.Max(1, w.TickRate);
            foreach (var fx in w.Fx)
            {
                float since = (drawTick - fx.StartTick) / rate;
                switch (fx.Kind)
                {
                    case HordeFxKind.Shot:
                    case HordeFxKind.EnemyShot:
                    {
                        if (drawTick < fx.StartTick || (fx.EndTick > 0 && drawTick > fx.EndTick)) break;
                        // client wave 17 (h17read), the readability proposal's A1-A5 (+ B5): sizes, colours and trails by side, a shadow on the
                        // ground, the near-miss mark (Horde/Client/HordeReadability.cs holds the numbers)
                        DrawShot(fx, since, drawTick, rate, content, mySlot, myPos, root, k, cam);
                        break;
                    }
                    case HordeFxKind.Zone:
                    case HordeFxKind.Trap:
                    case HordeFxKind.Warning:
                    case HordeFxKind.EnemyWarning:
                    {
                        var wd = fx.OwnerSlot >= 0 ? content.Weapon(fx.DefId) : null;
                        float fx0 = fx.X, fy0 = fx.Y;
                        if (fx.Follows && fx.OwnerSlot >= 0 && fx.OwnerSlot < w.Players.Length && w.Players[fx.OwnerSlot].Present) { var op = w.Players[fx.OwnerSlot]; fx0 = op.PrevX + (op.X - op.PrevX) * a; fy0 = op.PrevY + (op.Y - op.PrevY) * a; }
                        float r = Mathf.Max(fx.Radius, 0.5f);
                        var centre = root.TransformPoint(_arena.Local(fx0, fy0));
                        if (fx.Kind == HordeFxKind.EnemyWarning)
                        {
                            DrawEnemyWarning(fx, fx0, fy0, r, drawTick, new Color(1f, 0.14f, 0.10f, 0.98f), root, k, t);
                            break;
                        }
                        bool mine = fx.OwnerSlot == mySlot;
                        bool beneficial = fx.Kind == HordeFxKind.Zone && IsBeneficialZone(wd, fx.Level);
                        if (beneficial)
                        {
                            // Wave 28 (item 53 / item 12): beneficial zones (Sanctuary, Safety Wall, Pneuma, Aqua Benedicta, buff songs/fields) — subtle emerald sanctuary green + double ring ("stand inside!")
                            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 4.5f + fx.Id);
                            var rim = new Color(0.18f, 0.96f, 0.52f, mine ? 0.65f : 0.22f);
                            var innerRim = new Color(0.58f, 1f, 0.78f, (0.38f + 0.16f * pulse) * (mine ? 1f : 0.22f));
                            var fill = new Color(0.16f, 0.92f, 0.48f, (0.08f + 0.03f * pulse) * (mine ? 1f : 0.18f));
                            Disc(centre, 2f * r * k, fill);
                            Ring(centre, 2f * r * k, rim, true);
                            Ring(centre, 2f * r * 0.72f * k, innerRim, true);
                        }
                        else if (fx.Kind == HordeFxKind.Warning)
                        {
                            // Wave 28 (item 53): player/ally delayed strike — cool arcane cyan-blue (never warm red/amber like an enemy strike)
                            float life = fx.EndTick > fx.StartTick ? Mathf.Clamp01((drawTick - fx.StartTick) / (fx.EndTick - fx.StartTick)) : 1f;
                            var elem = World.Fx.ElementColor(wd != null ? (int)wd.Element : 0);
                            var col = Color.Lerp(elem, new Color(0.28f, 0.84f, 1f, 1f), 0.72f);
                            if (!mine) col = HordeReadability.AllyTint(col);
                            col.a = mine ? 0.68f : 0.16f;
                            var fill = col; fill.a = (mine ? 0.06f : 0.015f) * life;
                            Disc(centre, 2f * r * k * Mathf.Lerp(0.25f, 1f, life), fill);
                            Ring(centre, 2f * r * k, col, true);
                        }
                        else
                        {
                            // Wave 28 (item 53 / item 18): player/ally offensive ground zone or trap — linear walls render as a contiguous line of cells,
                            // whereas circular zones render with a cool arcane cyan perimeter & subtle non-hazard fill.
                            bool isFireWall = fx.Kind == HordeFxKind.Zone && wd != null && (wd.Zone & ZoneFlags.BlocksEnemies) != 0;
                            var elem = fx.Kind == HordeFxKind.Trap
                                ? (drawTick >= fx.ArmTick ? new Color(0.45f, 0.78f, 1f, 0.8f) : new Color(0.42f, 0.62f, 0.82f, 0.5f))
                                : isFireWall
                                    ? new Color(1f, 0.55f, 0.18f, 0.92f)
                                    : Color.Lerp(World.Fx.ElementColor(wd != null ? (int)wd.Element : 0), new Color(0.26f, 0.82f, 1f, 1f), 0.68f);
                            if (!mine) elem = HordeReadability.AllyTint(elem);
                            elem.a = mine ? (isFireWall ? 0.88f : 0.68f) : 0.16f;
                            var fill = elem; fill.a = mine ? (isFireWall ? 0.20f : 0.055f) : 0.015f;
                            if (isFireWall)
                            {
                                float halfLen = fx.Range > 0.1f ? fx.Range : Mathf.Max(1.6f, r * 1.65f);
                                float halfThick = Mathf.Clamp(r, 0.65f, 1.15f);
                                DrawLinearWall(root, fx0, fy0, fx.Angle, halfLen, halfThick, fill, elem, new Color(0.28f, 0.88f, 1f, mine ? 0.58f : 0.14f), k);
                                float rad = fx.Angle * Mathf.Deg2Rad;
                                float tx = Mathf.Cos(rad), ty = Mathf.Sin(rad);
                                int pillars = mine ? 7 : 5;
                                for (int p = 0; p < pillars; p++)
                                {
                                    float u = pillars > 1 ? Mathf.Lerp(-0.92f, 0.92f, p / (float)(pillars - 1)) : 0f;
                                    float px = fx0 + tx * halfLen * u, py = fy0 + ty * halfLen * u;
                                    float flicker = 0.78f + 0.22f * Mathf.Sin(t * 13f + p * 1.7f + fx.Id);
                                    var pPos = root.TransformPoint(_arena.Local(px, py) + Vector3.up * (0.48f * flicker));
                                    var fCol = new Color(1f, 0.48f + 0.32f * flicker, 0.12f, (mine ? 0.82f : 0.16f) * flicker);
                                    Dot(pPos, 0.88f * k * flicker, fCol, cam);
                                }
                            }
                            else
                            {
                                Disc(centre, 2f * r * k, fill);
                                Ring(centre, 2f * r * k, elem, true);
                            }
                        }
                        break;
                    }
                    case HordeFxKind.Orbit:
                    {
                        if (fx.OwnerSlot < 0 || fx.OwnerSlot >= w.Players.Length || !w.Players[fx.OwnerSlot].Present) break;
                        var op = w.Players[fx.OwnerSlot];
                        float ox = op.PrevX + (op.X - op.PrevX) * a, oy = op.PrevY + (op.Y - op.PrevY) * a;
                        if (fx.OwnerSlot == mySlot && myPos.HasValue) { ox = myPos.Value.x; oy = myPos.Value.y; }
                        var wd = content.Weapon(fx.DefId);
                        var col = World.Fx.ElementColor(wd != null ? (int)wd.Element : 0);
                        if (fx.OwnerSlot != mySlot) col = HordeReadability.AllyTint(col); // client wave 17 (h17read), B5
                        int n = Mathf.Max(1, fx.Count);
                        for (int o = 0; o < n; o++)
                        {
                            float ang = (fx.Angle + 360f * fx.Turns * since + o * 360f / n) * Mathf.Deg2Rad;
                            float x = ox + Mathf.Cos(ang) * fx.Range, y = oy + Mathf.Sin(ang) * fx.Range;
                            var pos = root.TransformPoint(_arena.Local(x, y) + Vector3.up * 0.9f);
                            Dot(pos, Mathf.Max(0.5f, fx.Radius * 1.6f) * k * 1.25f, new Color(0.03f, 0.03f, 0.05f, 0.55f), cam, _dark);
                            Dot(pos, Mathf.Max(0.5f, fx.Radius * 1.6f) * k, col, cam);
                        }
                        break;
                    }
                }
            }
            DrawActiveAim(AimMain, root, k, cam, t);
            DrawActiveAim(AimOff, root, k, cam, t);
        }

        private void DrawLinearWall(Transform root, float cx, float cy, float angleDeg, float halfLen, float halfThick, Color fill, Color rim, Color innerCol, float k)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            var tanWorld = root.TransformDirection(new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)));
            if (tanWorld.sqrMagnitude < 1e-6f) tanWorld = root.forward;
            var rot = Quaternion.LookRotation(tanWorld.normalized, root.up);
            var centre = root.TransformPoint(_arena.Local(cx, cy));
            float up = 0.05f * Mathf.Abs(root.lossyScale.x);
            float w = 2f * halfThick * k, l = 2f * (halfLen + halfThick * 0.35f) * k;
            float edge = Mathf.Max(0.08f, halfThick * 0.16f) * k;
            // Dark outline strip underneath
            _mpb.SetColor(ColorId, new Color(0.03f, 0.03f, 0.05f, 0.72f));
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(centre + Vector3.up * (up * 0.7f), rot, new Vector3(w + edge * 2.2f, 1f, l + edge * 2.2f)), _under, 0, null, 0, _mpb, false, false);
            // Subtle rectangular ground fill
            _mpb.SetColor(ColorId, fill);
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(centre + Vector3.up * (up * 0.85f), rot, new Vector3(w, 1f, l)), _under, 0, null, 0, _mpb, false, false);
            // Crisp linear side rails & center friendly spine
            var right = rot * Vector3.right;
            _mpb.SetColor(ColorId, rim);
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(centre + right * (w * 0.5f) + Vector3.up * up, rot, new Vector3(edge, 1f, l)), _solid, 0, null, 0, _mpb, false, false);
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(centre - right * (w * 0.5f) + Vector3.up * up, rot, new Vector3(edge, 1f, l)), _solid, 0, null, 0, _mpb, false, false);
            _mpb.SetColor(ColorId, innerCol);
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(centre + Vector3.up * (up * 1.1f), rot, new Vector3(edge * 0.85f, 1f, l * 0.92f)), _solid, 0, null, 0, _mpb, false, false);
            DrawCalls += 5;
        }

        private void DrawActiveAim(ActiveAimState aim, Transform root, float k, Camera cam, float t)
        {
            if (!aim.Active || root == null) return;
            var pos = root.TransformPoint(_arena.Local(aim.Cell.x, aim.Cell.y));
            float r = Mathf.Max(0.9f, aim.Radius);
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 7f + aim.Hand * 1.5f);
            Color baseCol = aim.Hand == 0 ? new Color(0.32f, 0.92f, 1f, 1f) : new Color(1f, 0.82f, 0.28f, 1f);
            if (!aim.Ready) baseCol = new Color(0.6f, 0.65f, 0.72f, 0.45f);
            var rim = baseCol; rim.a = aim.Ready ? (0.78f + 0.18f * pulse) : 0.35f;
            var fill = baseCol; fill.a = aim.Ready ? (0.10f + 0.04f * pulse) : 0.03f;
            if (aim.IsWall)
            {
                float halfLen = Mathf.Clamp(r * 1.45f, 1.8f, 4.2f);
                float halfThick = Mathf.Clamp(r * 0.42f, 0.75f, 1.05f);
                float innerFrac = aim.Ready ? 1f : Mathf.Clamp01(1f - aim.CooldownFrac);
                var innerCol = rim; innerCol.a *= 0.75f;
                DrawLinearWall(root, aim.Cell.x, aim.Cell.y, aim.Angle, halfLen * Mathf.Max(0.2f, innerFrac), halfThick, fill, rim, innerCol, k);
                Dot(pos + Vector3.up * (0.08f * k), 0.42f * k, rim, cam);
                return;
            }
            Disc(pos, 2f * r * k, fill);
            Ring(pos, 2f * r * k, rim, true);
            float cdFrac = aim.Ready ? (0.35f + 0.1f * pulse) : Mathf.Clamp01(1f - aim.CooldownFrac);
            Ring(pos, 2f * r * Mathf.Max(0.15f, cdFrac) * k, rim, false);
            Dot(pos + Vector3.up * (0.08f * k), 0.45f * k, rim, cam);
        }

        private static bool IsBeneficialZone(WeaponDef wd, int level)
        {
            if (wd == null) return false;
            if (wd.Levels != null && wd.Levels.Length > 0)
            {
                var lv = wd.Levels[Mathf.Clamp(level, 1, wd.Levels.Length) - 1];
                if (lv.Heal > 0 || lv.HealPct > 0f || (lv.Buffs != null && lv.Buffs.Length > 0)) return true;
                if (lv.Damage > 0) return false;
            }
            return (wd.Zone & (ZoneFlags.BlocksEnemies | ZoneFlags.BlocksShots)) != 0;
        }

        private Mesh _boundsMesh, _boundsUnderMesh;
        private readonly List<Vector3> _bv = new List<Vector3>(), _buv = new List<Vector3>();
        private readonly List<Color> _bc = new List<Color>(), _buc = new List<Color>();
        private readonly List<Vector2> _btx = new List<Vector2>(), _butx = new List<Vector2>();
        private readonly List<int> _btri = new List<int>(), _butri = new List<int>();

        /// <summary>Wave 28 (item 51): draws a terrain-draped perimeter barrier around the active arena that brightens when the player nears an edge.</summary>
        private void DrawArenaBounds(HordeContent content, Vector2? myPos, Transform root, float t)
        {
            if (content == null || content.Arenas == null || content.Arenas.Count == 0 || root == null) return;
            float x0, y0, x1, y1;
            if (Run != null && Run.X1 > Run.X0 && Run.Y1 > Run.Y0)
            {
                x0 = Run.X0; y0 = Run.Y0; x1 = Run.X1 + 1f; y1 = Run.Y1 + 1f;
            }
            else
            {
                ArenaDef arena = null;
                string mapName = _arena.MapName;
                if (!string.IsNullOrEmpty(mapName))
                    foreach (var a in content.Arenas)
                        if (string.Equals(a.Map, mapName, System.StringComparison.OrdinalIgnoreCase)) { arena = a; break; }
                if (arena == null && myPos.HasValue)
                {
                    var mp = myPos.Value;
                    foreach (var a in content.Arenas)
                        if (mp.x >= a.X0 - 2f && mp.x <= a.X1 + 3f && mp.y >= a.Y0 - 2f && mp.y <= a.Y1 + 3f) { arena = a; break; }
                }
                if (arena == null) arena = content.Arenas[0];
                x0 = arena.X0; y0 = arena.Y0; x1 = arena.X1 + 1f; y1 = arena.Y1 + 1f;
            }
            if (x1 <= x0 || y1 <= y0) return;

            if (_boundsMesh == null) { _boundsMesh = new Mesh { name = "horde-arena-bounds" }; _boundsMesh.MarkDynamic(); }
            if (_boundsUnderMesh == null) { _boundsUnderMesh = new Mesh { name = "horde-arena-bounds-under" }; _boundsUnderMesh.MarkDynamic(); }
            _bv.Clear(); _bc.Clear(); _btx.Clear(); _btri.Clear();
            _buv.Clear(); _buc.Clear(); _butx.Clear(); _butri.Clear();

            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 4f);
            void AddEdge(float ax, float ay, float bx, float by, float nx, float ny)
            {
                float len = Mathf.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                int seg = Mathf.Clamp(Mathf.CeilToInt(len / 1.5f), 4, 96);
                int baseV = _bv.Count, baseU = _buv.Count;
                for (int s = 0; s <= seg; s++)
                {
                    float u = s / (float)seg;
                    float px = Mathf.Lerp(ax, bx, u), py = Mathf.Lerp(ay, by, u);
                    float gh = _arena.GroundHeight(px, py);
                    float near = 0f;
                    if (myPos.HasValue)
                    {
                        float d = Vector2.Distance(myPos.Value, new Vector2(px, py));
                        near = Mathf.Clamp01(1f - d / 10f);
                        near *= near;
                    }
                    float wIn = Mathf.Lerp(0.14f, 0.26f, near), wOut = Mathf.Lerp(0.10f, 0.18f, near);
                    float wallH = Mathf.Lerp(0.28f, 0.65f, near);
                    float alpha = Mathf.Lerp(0.42f, 0.92f, near) * (0.9f + 0.1f * pulse);
                    var col = Color.Lerp(new Color(1f, 0.78f, 0.28f, alpha), new Color(1f, 0.92f, 0.48f, alpha), near);
                    var topCol = new Color(col.r, col.g, col.b, alpha * 0.15f);
                    var uCol = new Color(0.03f, 0.03f, 0.05f, Mathf.Lerp(0.45f, 0.80f, near));

                    // Dark under-ribbon on ground
                    _buv.Add(new Vector3(px - nx * (wIn + 0.08f), gh + 0.05f, py - ny * (wIn + 0.08f)));
                    _buv.Add(new Vector3(px + nx * (wOut + 0.08f), gh + 0.05f, py + ny * (wOut + 0.08f)));
                    _buc.Add(uCol); _buc.Add(uCol);
                    _butx.Add(new Vector2(0.5f, 0.5f)); _butx.Add(new Vector2(0.5f, 0.5f));

                    // Ground ribbon + low vertical energy curtain (3 vertices per step: inner ground, edge ground, edge top)
                    _bv.Add(new Vector3(px - nx * wIn, gh + 0.08f, py - ny * wIn));
                    _bv.Add(new Vector3(px + nx * wOut, gh + 0.08f, py + ny * wOut));
                    _bv.Add(new Vector3(px, gh + 0.08f + wallH, py));
                    _bc.Add(col); _bc.Add(col); _bc.Add(topCol);
                    _btx.Add(new Vector2(0.5f, 0.5f)); _btx.Add(new Vector2(0.5f, 0.5f)); _btx.Add(new Vector2(0.5f, 0.5f));
                }
                for (int s = 0; s < seg; s++)
                {
                    int u0 = baseU + s * 2;
                    _butri.Add(u0); _butri.Add(u0 + 1); _butri.Add(u0 + 3);
                    _butri.Add(u0); _butri.Add(u0 + 3); _butri.Add(u0 + 2);

                    int v0 = baseV + s * 3, v1 = v0 + 3;
                    // Ground strip
                    _btri.Add(v0); _btri.Add(v0 + 1); _btri.Add(v1 + 1);
                    _btri.Add(v0); _btri.Add(v1 + 1); _btri.Add(v1);
                    // Vertical curtain (double-sided)
                    _btri.Add(v0 + 1); _btri.Add(v0 + 2); _btri.Add(v1 + 2);
                    _btri.Add(v0 + 1); _btri.Add(v1 + 2); _btri.Add(v1 + 1);
                    _btri.Add(v0 + 2); _btri.Add(v0 + 1); _btri.Add(v1 + 1);
                    _btri.Add(v0 + 2); _btri.Add(v1 + 1); _btri.Add(v1 + 2);
                }
            }

            AddEdge(x0, y0, x1, y0, 0f, -1f);
            AddEdge(x1, y0, x1, y1, 1f, 0f);
            AddEdge(x1, y1, x0, y1, 0f, 1f);
            AddEdge(x0, y1, x0, y0, -1f, 0f);

            var center = new Vector3((x0 + x1) * 0.5f, 0f, (y0 + y1) * 0.5f);
            var size = new Vector3(x1 - x0 + 8f, 40f, y1 - y0 + 8f);
            _boundsUnderMesh.Clear(); _boundsUnderMesh.SetVertices(_buv); _boundsUnderMesh.SetColors(_buc); _boundsUnderMesh.SetUVs(0, _butx); _boundsUnderMesh.SetTriangles(_butri, 0);
            _boundsUnderMesh.bounds = new Bounds(center, size);
            _boundsMesh.Clear(); _boundsMesh.SetVertices(_bv); _boundsMesh.SetColors(_bc); _boundsMesh.SetUVs(0, _btx); _boundsMesh.SetTriangles(_btri, 0);
            _boundsMesh.bounds = new Bounds(center, size);

            var toWorld = root.localToWorldMatrix;
            _mpb.SetColor(ColorId, Color.white);
            Graphics.DrawMesh(_boundsUnderMesh, toWorld, _under, 0, null, 0, _mpb, false, false);
            Graphics.DrawMesh(_boundsMesh, toWorld, _glow, 0, null, 0, _mpb, false, false);
            DrawCalls += 2;
        }

        /// <summary>One step of a pickup's flight to its taker: an ease-in (accelerating), a small rise towards the player's chest.</summary>
        private float _flyStart = 3f, _flyAccel = FlyAccel;

        private Vector3 FlyStep(ref Fly fl, float t, float dt, out bool landed)
        {
            landed = false;
            var target = fl.Taker >= 0 && fl.Taker < _playerPos.Length ? _playerPos[fl.Taker] : fl.Pos;
            var to = target - fl.Pos; float dist = to.magnitude;
            fl.Speed = Mathf.Min(FlyMaxSpeed, fl.Speed + _flyAccel * dt);
            float step = Mathf.Min(dist, fl.Speed * dt);
            if (dist > 1e-4f) fl.Pos += to / dist * step;
            if (dist - step < 0.35f) landed = true;
            float near = Mathf.Clamp01(1f - (dist - step) / 3f);
            return _arena.Local(fl.Pos.x, fl.Pos.y) + Vector3.up * (0.2f + 0.9f * near);
        }

        private void DrawPickup(Vector3 world, int itemId, PickupKind kind, float scale, float k, Vector3 camPos, Camera cam)
        {
            var f = _sprites.Get(_sprites.Item(itemId), 0, 0, 0f, false);
            if (f == null) { Dot(world, 0.5f * k, new Color(0.5f, 0.9f, 1f, 0.9f), cam); return; }
            _mpb.SetColor(ColorId, Color.white);
            Graphics.DrawMesh(f.Mesh, HordeSprites.Matrix(f, world, camPos, k, scale), _sprites.MaterialFor(f.Texture), 0, null, 0, _mpb, false, true);
            DrawCalls++;
        }

        private void Dot(Vector3 world, float size, Color c, Camera cam, Material mat = null)
        {
            var rot = Quaternion.LookRotation(world - cam.transform.position, Vector3.up) * Quaternion.Euler(-90f, 0, 0); // the quad lies in XZ: face the eye
            _mpb.SetColor(ColorId, c);
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(world, rot, Vector3.one * size), mat ?? _glow, 0, null, 0, _mpb, false, false);
            DrawCalls++;
        }

        /// <summary>A shot: a thin bright head, a streak behind it along its velocity, and a dark outline under both (contrast on any ground).</summary>
        private void Streak(Transform root, Vector3 local, Vector2 v, float headCells, Color col, float k, Camera cam)
        {
            var world = root.TransformPoint(local);
            float speed = v.magnitude;
            var back = speed > 1e-3f ? root.TransformDirection(new Vector3(-v.x, 0f, -v.y) / speed) : Vector3.zero;
            float len = Mathf.Clamp(speed * 0.09f, headCells, 2.2f) * k; // the last 90 ms of flight
            var face = Quaternion.LookRotation(world - cam.transform.position, Vector3.up) * Quaternion.Euler(-90f, 0, 0);
            if (speed > 1e-3f)
            {
                // the streak: the dot texture stretched along the flight, laid in the face plane
                var tailDir = Vector3.ProjectOnPlane(back, face * Vector3.up);
                if (tailDir.sqrMagnitude > 1e-6f)
                {
                    var rot = Quaternion.LookRotation(face * Vector3.up, tailDir.normalized) * Quaternion.Euler(-90f, 0, 0) ;
                    var centre = world + tailDir.normalized * (len * 0.5f);
                    _mpb.SetColor(ColorId, new Color(0.03f, 0.03f, 0.05f, 0.5f));
                    Graphics.DrawMesh(_quad, Matrix4x4.TRS(centre, rot, new Vector3(headCells * k * 0.9f, 1f, len * 1.15f)), _dark, 0, null, 0, _mpb, false, false);
                    var tail = col; tail.a *= 0.75f;
                    _mpb.SetColor(ColorId, tail);
                    Graphics.DrawMesh(_quad, Matrix4x4.TRS(centre, rot, new Vector3(headCells * k * 0.55f, 1f, len)), _glow, 0, null, 0, _mpb, false, false);
                    DrawCalls += 2;
                }
            }
            _mpb.SetColor(ColorId, new Color(0.03f, 0.03f, 0.05f, 0.6f));
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(world, face, Vector3.one * (headCells * k * 1.35f)), _dark, 0, null, 0, _mpb, false, false);
            var head = Color.Lerp(col, Color.white, 0.35f); head.a = 1f;
            _mpb.SetColor(ColorId, head);
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(world, face, Vector3.one * (headCells * k)), _glow, 0, null, 0, _mpb, false, false);
            DrawCalls += 2;
        }

        /// <summary>A ground ring of the given diameter; thin: a thin bright rim over a dark rim (readable at table scale), else wave 8's wider glowing ring.</summary>
        private void Ring(Vector3 world, float diameter, Color c, bool thin = false)
        {
            var root = _arena.Root; float up = 0.06f * Mathf.Abs(root.lossyScale.x);
            if (thin)
            {
                _mpb.SetColor(ColorId, new Color(0.03f, 0.03f, 0.05f, 0.7f));
                Graphics.DrawMesh(_quad, Matrix4x4.TRS(world + Vector3.up * (up * 0.8f), root.rotation, Vector3.one * (diameter * 1.045f)), _darkRing, 0, null, 0, _mpb, false, false);
                DrawCalls++;
                _mpb.SetColor(ColorId, c);
                Graphics.DrawMesh(_quad, Matrix4x4.TRS(world + Vector3.up * up, root.rotation, Vector3.one * diameter), _thinRing, 0, null, 0, _mpb, false, false);
                DrawCalls++;
                return;
            }
            _mpb.SetColor(ColorId, c);
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(world + Vector3.up * up, root.rotation, Vector3.one * diameter), _ring, 0, null, 0, _mpb, false, false);
            DrawCalls++;
        }

        /// <summary>A flat filled disc on the ground (a zone's area, a warning's fill).</summary>
        private void Disc(Vector3 world, float diameter, Color c)
        {
            var root = _arena.Root;
            _mpb.SetColor(ColorId, c);
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(world + Vector3.up * (0.04f * Mathf.Abs(root.lossyScale.x)), root.rotation, Vector3.one * diameter), _disc, 0, null, 0, _mpb, false, false);
            DrawCalls++;
        }

        /// <summary>A small HP bar facing the eye: a dark back, the fill from the left.</summary>
        private void Bar(Vector3 world, float width, float height, float fill01, Color fill, Camera cam)
        {
            var face = Quaternion.LookRotation(world - cam.transform.position, Vector3.up) * Quaternion.Euler(-90f, 0, 0);
            _mpb.SetColor(ColorId, new Color(0.03f, 0.03f, 0.05f, 0.8f));
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(world, face, new Vector3(width, 1f, height)), _under, 0, null, 0, _mpb, false, false); // (client wave 17, h17read: the back always under its fill)
            float w = width * 0.94f * Mathf.Clamp01(fill01);
            if (w > 1e-4f)
            {
                var right = face * Vector3.right;
                _mpb.SetColor(ColorId, fill);
                Graphics.DrawMesh(_quad, Matrix4x4.TRS(world - right * ((width * 0.94f - w) * 0.5f), face, new Vector3(w, 1f, height * 0.6f)), _disc, 0, null, 0, _mpb, false, false);
            }
            DrawCalls += 2;
        }

        // ------------------------------------------------------------------ client wave 17 (h17read): the readability proposal (point 28)
        // A1-A5 (shots), B5 (an ally's effects), C1 (every wind-up's "!"), C2 (enemy area warnings); the numbers are in HordeReadability.cs
        /// <summary>Shots drawn (by side), shots that showed the near-miss mark, wind-up marks drawn, enemy warnings drawn and flashed (each
        /// counted once per thing), and the last sizes drawn in cells (head, trail) (the smoke, the log).</summary>
        public int ShotsMine { get; private set; }
        public int ShotsAlly { get; private set; }
        public int ShotsEnemy { get; private set; }
        public int NearMisses { get; private set; }
        public int Bangs { get; private set; }
        public int WarningsDrawn { get; private set; }
        public int WarningFlashes { get; private set; }
        public int Shadows { get; private set; }
        public Vector2 LastEnemyShot { get; private set; }
        public Vector2 LastMyShot { get; private set; }
        public Vector2 LastAllyShot { get; private set; }
        public float LastWarnEdge { get; private set; }
        /// <summary>E2: an enemy shot passed close by without touching me (HordeFeel plays the whoosh there).</summary>
        public System.Action<Vector2> Whoosh;
        private readonly HashSet<int> _shotSeen = new HashSet<int>(), _nearSeen = new HashSet<int>(), _whooshed = new HashSet<int>(), _warnSeen = new HashSet<int>(), _flashSeen = new HashSet<int>();
        private readonly HordeMeshPool _pool = new HordeMeshPool();
        /// <summary>A shot flies this high over the ground (cells: wave 9's).</summary>
        public const float ShotLift = 0.8f;

        /// <summary>One shot: its side's size and colour (A1, A2), its trail (A3), its shadow (A4), the near-miss mark (A5) and the whoosh (E2).</summary>
        private void DrawShot(HordeFxView fx, float since, float drawTick, float rate, HordeContent content, int mySlot, Vector2? myPos, Transform root, float k, Camera cam)
        {
            bool enemy = fx.Kind == HordeFxKind.EnemyShot, mine = !enemy && fx.OwnerSlot == mySlot;
            float S = HordeReadability.Size;
            var wd = enemy ? null : content.Weapon(fx.DefId);
            var el = World.Fx.ElementColor(wd != null ? (int)wd.Element : 0);
            float x = fx.X + fx.VX * since, y = fx.Y + fx.VY * since;
            var v = new Vector2(fx.VX, fx.VY); float speed = v.magnitude;
            float head = (enemy ? HordeReadability.EnemyShotCells : mine ? HordeReadability.MyShotCells : HordeReadability.AllyShotCells) * S;
            // the trail: the last EnemyTrailSeconds / MyTrailSeconds of flight, never back past where it was fired
            float trail = Mathf.Min(speed * (enemy ? HordeReadability.EnemyTrailSeconds : HordeReadability.MyTrailSeconds), speed * Mathf.Max(0f, since));
            Color col, rim, core; bool near = false;
            if (enemy)
            {
                col = HordeReadability.EnemyShotColor; rim = HordeReadability.EnemyShotRim; core = Color.Lerp(col, Color.white, 0.25f);
                if (myPos.HasValue && speed > 1e-3f)
                {
                    // A5: will it pass within NearMissCells of me (ahead of it, within its life)?
                    var rel = myPos.Value - new Vector2(x, y);
                    float tc = Vector2.Dot(rel, v) / (speed * speed), left = fx.EndTick > 0 ? (fx.EndTick - drawTick) / rate : 99f;
                    float dmin = (rel - v * Mathf.Max(0f, tc)).magnitude;
                    near = tc > 0f && tc <= left && dmin <= HordeReadability.NearMissCells;
                    // E2: the whoosh, once, at its closest to me when that was within WhooshCells and it did not touch me
                    if (tc <= 0.02f && tc > -0.25f && rel.magnitude <= HordeReadability.WhooshCells && dmin >= HordeReadability.MissCells && _whooshed.Add(fx.Id)) Whoosh?.Invoke(new Vector2(x, y));
                }
                if (_shotSeen.Add(fx.Id)) ShotsEnemy++;
                LastEnemyShot = new Vector2(head, trail);
            }
            else if (mine) { col = el; col.a = 1f; rim = HordeReadability.MyShotRim; core = Color.white; if (_shotSeen.Add(fx.Id)) ShotsMine++; LastMyShot = new Vector2(head, trail); }
            else { col = HordeReadability.AllyTint(el); rim = HordeReadability.AllyRim; core = Color.Lerp(col, Color.white, 0.3f); if (_shotSeen.Add(fx.Id)) ShotsAlly++; LastAllyShot = new Vector2(head, trail); }
            if (near) { col = Color.Lerp(col, HordeReadability.NearMissHot, 0.45f); head *= 1.15f; if (_nearSeen.Add(fx.Id)) NearMisses++; } // brighter: hotter, bigger
            var local = _arena.Local(x, y);
            // A4: the shadow on the ground under it (its height and path read at a glance)
            _mpb.SetColor(ColorId, new Color(0.02f, 0.02f, 0.03f, enemy ? 0.5f : 0.38f));
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(root.TransformPoint(local + Vector3.up * 0.03f), root.rotation, Vector3.one * ((enemy ? HordeReadability.EnemyShadowCells : HordeReadability.ShadowCells) * S * k)), _shadow, 0, null, 0, _mpb, false, false); // (client wave 18, h18feel: an enemy's a third, point 28)
            DrawCalls++; Shadows++;
            var world = root.TransformPoint(local + Vector3.up * ShotLift);
            ShotStreak(root, world, v, head, trail, col, rim, mine ? core : (Color?)null, k, cam);
            if (near)
            {
                // A5's thin red ring around it, facing the eye
                var face = Quaternion.LookRotation(world - cam.transform.position, Vector3.up) * Quaternion.Euler(-90f, 0, 0);
                _mpb.SetColor(ColorId, HordeReadability.NearMissRing);
                Graphics.DrawMesh(_quad, Matrix4x4.TRS(world, face, Vector3.one * (head * 2.3f * k)), _thinRing, 0, null, 0, _mpb, false, false);
                DrawCalls++;
            }
        }

        /// <summary>A shot's look: its trail along the flight (a dark one under it), its rim (dark; an ally's cyan), its head, mine a white core.</summary>
        private void ShotStreak(Transform root, Vector3 world, Vector2 v, float head, float trailCells, Color col, Color rim, Color? core, float k, Camera cam)
        {
            float speed = v.magnitude, len = trailCells * k;
            var face = Quaternion.LookRotation(world - cam.transform.position, Vector3.up) * Quaternion.Euler(-90f, 0, 0);
            if (speed > 1e-3f && len > head * k * 0.25f)
            {
                var back = root.TransformDirection(new Vector3(-v.x, 0f, -v.y) / speed);
                var tailDir = Vector3.ProjectOnPlane(back, face * Vector3.up);
                if (tailDir.sqrMagnitude > 1e-6f)
                {
                    var rot = Quaternion.LookRotation(face * Vector3.up, tailDir.normalized) * Quaternion.Euler(-90f, 0, 0);
                    var centre = world + tailDir.normalized * (len * 0.5f);
                    _mpb.SetColor(ColorId, new Color(0.03f, 0.03f, 0.05f, 0.5f));
                    Graphics.DrawMesh(_quad, Matrix4x4.TRS(centre, rot, new Vector3(head * k * 0.9f, 1f, len * 1.1f)), _dark, 0, null, 0, _mpb, false, false);
                    var tail = col; tail.a = 0.95f;
                    _mpb.SetColor(ColorId, tail);
                    Graphics.DrawMesh(_quad, Matrix4x4.TRS(centre, rot, new Vector3(head * k * 0.7f, 1f, len * 1.15f)), _trail, 0, null, 0, _mpb, false, false);
                    DrawCalls += 2;
                }
            }
            // a soft glow around it (it reads on dark ground too), the rim, the solid head, mine a white core
            var halo = col; halo.a *= 0.45f;
            _mpb.SetColor(ColorId, halo);
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(world, face, Vector3.one * (head * k * 1.9f)), _glow, 0, null, 0, _mpb, false, false);
            _mpb.SetColor(ColorId, rim);
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(world, face, Vector3.one * (head * k * 1.3f)), _shotRim, 0, null, 0, _mpb, false, false);
            var h = col; h.a = 1f;
            _mpb.SetColor(ColorId, h);
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(world, face, Vector3.one * (head * k)), _shotHead, 0, null, 0, _mpb, false, false);
            DrawCalls += 3;
            if (core.HasValue)
            {
                _mpb.SetColor(ColorId, core.Value);
                Graphics.DrawMesh(_quad, Matrix4x4.TRS(world, face, Vector3.one * (head * k * 0.45f)), _shotCore, 0, null, 0, _mpb, false, false);
                DrawCalls++;
            }
        }

        /// <summary>C2: an enemy's area: the disc filling up to the strike (as before), a BRIGHT edge of WarnEdgeCells over a dark one, and a
        /// white flash over the area in its last WarnFlashSeconds.</summary>
        private void DrawEnemyWarning(HordeFxView fx, float x, float y, float r, float drawTick, Color col, Transform root, float k, float t)
        {
            float rate = Mathf.Max(1, HordeLimits.TickRate);
            float life = fx.EndTick > fx.StartTick ? Mathf.Clamp01((drawTick - fx.StartTick) / (fx.EndTick - fx.StartTick)) : 1f;
            float left = (fx.EndTick - drawTick) / rate;
            bool flash = left <= HordeReadability.WarnFlashSeconds;
            var local = _arena.Local(x, y); var centre = root.TransformPoint(local);
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * Mathf.Lerp(6f, 22f, life));
            var bgFill = new Color(col.r, col.g, col.b, 0.14f + 0.08f * pulse);
            Disc(centre, 2f * r * k, bgFill);
            var fill = col; fill.a = 0.28f + 0.22f * life * pulse;
            Disc(centre, 2f * r * k * Mathf.Lerp(0.18f, 1f, life), fill);
            float edge = HordeReadability.WarnEdgeCells * HordeReadability.Size;
            var m = root.localToWorldMatrix * Matrix4x4.Translate(local + Vector3.up * 0.07f);
            _mpb.SetColor(ColorId, new Color(0.03f, 0.03f, 0.05f, 0.78f));
            Graphics.DrawMesh(_pool.Annulus(Mathf.Max(0f, r - edge * 1.9f), r + edge * 0.6f, 0f, 360f), m, _solidUnder, 0, null, 0, _mpb, false, false);
            _mpb.SetColor(ColorId, flash ? Color.white : HordeReadability.WarnEdge);
            Graphics.DrawMesh(_pool.Annulus(Mathf.Max(0f, r - edge * 1.15f), r, 0f, 360f), m, _solid, 0, null, 0, _mpb, false, false);
            DrawCalls += 2;
            if (flash)
            {
                Disc(centre, 2f * r * k, new Color(1f, 1f, 1f, 0.28f + 0.22f * pulse));
                if (_flashSeen.Add(fx.Id)) WarningFlashes++;
            }
            if (_warnSeen.Add(fx.Id)) WarningsDrawn++;
            LastWarnEdge = edge;
        }

        /// <summary>C1: a red "!" over a winding enemy, <paramref name="topCells"/> over its feet, BangCells tall (× Size), a dark rim behind it.</summary>
        private void Bang(Vector3 feet, float topCells, float k, Camera cam)
        {
            float hgt = HordeReadability.BangCells * HordeReadability.Size;
            var world = feet + Vector3.up * ((topCells + hgt * 0.5f) * k);
            var face = Quaternion.LookRotation(world - cam.transform.position, Vector3.up) * Quaternion.Euler(-90f, 0, 0);
            _mpb.SetColor(ColorId, new Color(0.03f, 0.02f, 0.02f, 0.85f));
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(world, face, new Vector3(hgt * 0.62f * k, 1f, hgt * 1.2f * k)), _bangUnder, 0, null, 0, _mpb, false, false);
            _mpb.SetColor(ColorId, HordeReadability.BangColor);
            Graphics.DrawMesh(_quad, Matrix4x4.TRS(world, face, new Vector3(hgt * 0.5f * k, 1f, hgt * k)), _bang, 0, null, 0, _mpb, false, false);
            DrawCalls += 2; Bangs++;
        }

        private static Texture2D _bangTex;
        /// <summary>A "!" glyph (white, soft-edged; the bar above the dot), 32 × 64.</summary>
        private static Texture2D BangTexture()
        {
            if (_bangTex != null) return _bangTex;
            const int w = 32, h = 64; _bangTex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "horde-bang" };
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                // the bar: a capsule from y 24 to 60, 6 px wide at its top, 4 at its foot; the dot: a disc of radius 5.5 at y 9
                float half = Mathf.Lerp(4f, 6.5f, Mathf.InverseLerp(24f, 60f, py));
                float bar = py >= 24f && py <= 60f ? Mathf.Clamp01(half - Mathf.Abs(px - 16f) + 0.5f) : py > 60f ? Mathf.Clamp01(6.5f - Vector2.Distance(new Vector2(px, py), new Vector2(16f, 60f)) + 0.5f) : 0f;
                float dot = Mathf.Clamp01(5.5f - Vector2.Distance(new Vector2(px, py), new Vector2(16f, 9f)) + 0.5f);
                _bangTex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Max(bar, dot)));
            }
            _bangTex.Apply(true); return _bangTex;
        }
    }
}
