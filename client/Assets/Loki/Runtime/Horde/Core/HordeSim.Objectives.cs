#nullable disable
using System;
using System.Collections.Generic;
using Loki.Horde.Net;

namespace Loki.Horde
{
    // ====================================================================================================================
    // Client wave 17 (h17obj): MAP OBJECTIVES, design proposal point 24 (design notes section 4; design note,
    // 2026-10-08: "Approved design specification."). Something appears on the map now and then that is worth walking to: a
    // circle you stand in for a few seconds (a ring fills; enemies inside slow it), and the claimer and the allies near it get
    // its gift. Seven kinds, drawn with Ragnarok's own skills and items (by rAthena's ids only):
    //   1 Blessing shrine   +30 % attack speed for 20 s (AL_INCAGI's look)
    //   2 Sanctuary well    heals 4 % of max HP a second for 15 s (PR_SANCTUARY's look)
    //   3 Fire pylons       fire walls around the fight for 20 s: enemies crossing them burn (MG_FIREWALL's look)
    //   4 Flame ring        three fire balls circle you for 25 s (MG_SIGHT's look)
    //   5 Meteor beacon     three meteors on the thickest crowds (WZ_METEOR's look)
    //   6 Guarded chest     an elite guards an Old Violet Box (item 617): three upgrades
    //   7 Defend the Emperium (mob 1288; at most once a run): stay near it 30 s while waves come; three upgrades and a magnet
    // When: the first at 1:30 of the run, then one every 75-100 s (the objectives' own dice), never during a boss's entrance;
    // each waits 25 s, then fades. One at a time; with 3+ players two at once, apart (to split up).
    // Every number is LOKI'S OWN DESIGN (ObjectiveRulesDef, part of HordeContent: host and client compare it in the stamp).
    // The engine's rules are here; HordeSim.cs only calls InitObjectives (the constructor), StepObjectives (Step) and
    // HashObjectives (StateHash). The snapshot carries the objectives and the buffs they gave in ONE block at the end of
    // H_SNAP's body (HordeObjectiveWire; protocol 3). Tests: tools/lokihost/Tests/HordeObjectiveTests.cs.
    // ====================================================================================================================

    /// <summary>The seven kinds (the wire's byte; client wave 17, h17obj).</summary>
    public enum ObjectiveKind : byte { Shrine = 1, Well = 2, Pylons = 3, FlameRing = 4, Meteor = 5, Chest = 6, Emperium = 7 }

    /// <summary>Where an objective is in its life.</summary>
    public enum ObjectiveState : byte
    {
        /// <summary>Up: stand in its circle to claim it (it fades at its deadline).</summary>
        Up = 0,
        /// <summary>The Emperium is being defended: its ring fills while a player stands near it; waves come.</summary>
        Defending = 1,
        /// <summary>Claimed, and it lasts at its place: the pylons' fire walls until EndTick.</summary>
        Active = 2,
    }

    /// <summary>The objectives' numbers (Loki's own design, client wave 17 h17obj; distances in cells, times in seconds, percent in
    /// percent). The defaults ARE the game's table (HordeTables does not override them); the stamp hashes every one.</summary>
    public sealed class ObjectiveRulesDef
    {
        public bool Enabled = true;
        // ---- when
        /// <summary>The first objective at this many seconds of the run (real seconds: the run's own clock, not stretched).</summary>
        public float FirstSeconds = 90f;
        /// <summary>The next one this many seconds after the last one appeared (a random time between the two).</summary>
        public float EveryMinSeconds = 75f, EveryMaxSeconds = 100f;
        /// <summary>An objective waits this long, then fades (a claim under way finishes first).</summary>
        public float WaitSeconds = 25f;
        /// <summary>A boss's entrance (no objective appears then): from its warning to this many seconds after it appeared.</summary>
        public float BossEntranceAfterSeconds = 10f;
        /// <summary>From this many players on, two objectives come at once (apart), else one at a time.</summary>
        public int TwoAtOncePlayers = 3;
        // ---- where
        /// <summary>Placed this far from the party (a random direction), at least EdgeMarginCells inside the arena; two at once at
        /// least ApartCells apart.</summary>
        public float NearCells = 9f, FarCells = 14f, EdgeMarginCells = 4f, ApartCells = 12f;
        // ---- the claim
        /// <summary>The circle's radius, and the seconds a player stands in it to claim it (a ring fills).</summary>
        public float Radius = 3f, ClaimSeconds = 3f;
        /// <summary>Enemies inside slow the ring: each normal enemy takes this many percent off its speed (an elite counts
        /// EliteWeight, a boss BossWeight), never below ClaimMinPct.</summary>
        public float SlowPctPerEnemy = 12f, ClaimMinPct = 20f;
        public int EliteWeight = 3, BossWeight = 5;
        /// <summary>Nobody inside: the ring empties at this share of its filling speed.</summary>
        public float DrainShare = 0.5f;
        /// <summary>The claimer and every standing ally within this many cells of it get the gift.</summary>
        public float ShareCells = 5f;
        /// <summary>The kinds' weights in the draw (index = ObjectiveKind; 0 unused). The Emperium at most once a run.</summary>
        public int[] KindWeights = { 0, 16, 16, 16, 16, 16, 16, 8 };
        // ---- the gifts
        /// <summary>Blessing shrine: +30 % attack speed = every cooldown ÷ 1.3 (CooldownPct −23.1), for ShrineSeconds.</summary>
        public float ShrineSeconds = 20f, ShrineCooldownPct = -23.1f;
        /// <summary>Sanctuary well: WellHealPct of max HP every second for WellSeconds.</summary>
        public float WellSeconds = 15f, WellHealPct = 4f;
        /// <summary>Fire pylons: four walls PylonHalfCells from the pylon on each side (inside the arena) for PylonSeconds; an enemy
        /// within PylonBandCells of a wall is struck every PylonEverySeconds (PylonWeapon: its damage, its burn).</summary>
        public float PylonSeconds = 20f, PylonHalfCells = 9f, PylonBandCells = 0.75f, PylonEverySeconds = 0.5f;
        /// <summary>Flame ring: FlameRingWeapon's orbit around each player who got it, for FlameRingSeconds.</summary>
        public float FlameRingSeconds = 25f;
        /// <summary>Meteor beacon: MeteorCount strikes (MeteorWeapon) on the thickest crowds within MeteorReachCells of it.</summary>
        public int MeteorCount = 3; public float MeteorReachCells = 14f;
        /// <summary>Guarded chest: ChestUpgrades upgrades each; its guard is the tables' weakest elite at GuardHpPct of its HP, kept
        /// within GuardLeashCells of the box until the box is taken (it leaves with the box if the box fades).</summary>
        public int ChestUpgrades = 3; public float GuardHpPct = 50f, GuardLeashCells = 3.5f;
        /// <summary>Defend the Emperium: its circle (EmperiumRadius) held EmperiumSeconds in all (not slowed); lost when nobody stood in
        /// it for EmperiumLostSeconds; a wave of EmperiumWaveCount enemies (× the player scaling) every EmperiumWaveEvery seconds on a
        /// circle EmperiumWaveCells around it; EmperiumUpgrades upgrades each and a magnet.</summary>
        public float EmperiumSeconds = 30f, EmperiumRadius = 4f, EmperiumLostSeconds = 6f, EmperiumWaveEvery = 6f, EmperiumWaveCells = 8f;
        public int EmperiumWaveCount = 4, EmperiumUpgrades = 3;
        /// <summary>The effect weapons' damage grows this many percent per party level above 1 (the run's enemies grow too).</summary>
        public float DamagePctPerLevel = 8f;
        /// <summary>rAthena's ids of what is drawn: EMPELIUM 1288 (mob_db), Old_Violet_Box 617 (item_db_usable).</summary>
        public int EmperiumMobId = 1288, ChestItemId = 617;

        /// <summary>The three effect weapons (fire, magic: the claimer's own magic and fire bonuses count). Never offered: they are
        /// not in any job's cards nor in HordeContent.Weapons; HordeContent.Weapon(id) finds them for the client's drawing.</summary>
        public WeaponDef FlameRingWeapon = new WeaponDef
        {
            // MG_SIGHT (10, Fire): a fire ball circling the caster
            Id = 990, Key = "obj_flamering", SkillId = 10, Kind = WeaponKind.Orbiter, Target = Targeting.Self, Class = DamageClass.Magic, Element = Element.Fire, Weight = 0,
            Levels = new[] { new WeaponLevel { Damage = 12, Count = 3, Range = 2.2f, Area = 0.55f, Speed = 0.55f, Tick = 0.5f } },
        };
        public WeaponDef MeteorWeapon = new WeaponDef
        {
            // WZ_METEOR (83, Fire): meteors with a stun chance
            Id = 991, Key = "obj_meteor", SkillId = 83, Kind = WeaponKind.AreaAtTarget, Target = Targeting.Densest, Class = DamageClass.Magic, Element = Element.Fire, Weight = 0,
            Levels = new[] { new WeaponLevel { Damage = 60, Area = 2.6f, Delay = 1.0f, Effects = new[] { new EffectDef(EffectKind.Stun, 30f, 1f) } } },
        };
        public WeaponDef PylonWeapon = new WeaponDef
        {
            // MG_FIREWALL (18, Fire): the walls' strike, and a burn (3 s, a tick a second)
            Id = 992, Key = "obj_pylon", SkillId = 18, Kind = WeaponKind.GroundZone, Target = Targeting.Self, Class = DamageClass.Magic, Element = Element.Fire, Weight = 0,
            Levels = new[] { new WeaponLevel { Damage = 14, Tick = 0.5f, Effects = new[] { new EffectDef(EffectKind.Dot, 100f, 3f, 5f, 1f) } } },
        };

        public WeaponDef Weapon(int id) => id == FlameRingWeapon?.Id ? FlameRingWeapon : id == MeteorWeapon?.Id ? MeteorWeapon : id == PylonWeapon?.Id ? PylonWeapon : null;

        /// <summary>The circle's radius of a kind (cells).</summary>
        public float RadiusOf(ObjectiveKind k) => k == ObjectiveKind.Emperium ? EmperiumRadius : Radius;

        /// <summary>Every number, in a fixed order (HordeWire.ContentStamp hashes them).</summary>
        public IEnumerable<double> StampValues()
        {
            yield return -17; // (a tag: client wave 17's block)
            foreach (var v in new double[] { Enabled ? 1 : 0, FirstSeconds, EveryMinSeconds, EveryMaxSeconds, WaitSeconds, BossEntranceAfterSeconds, TwoAtOncePlayers,
                NearCells, FarCells, EdgeMarginCells, ApartCells, Radius, ClaimSeconds, SlowPctPerEnemy, ClaimMinPct, EliteWeight, BossWeight, DrainShare, ShareCells,
                ShrineSeconds, ShrineCooldownPct, WellSeconds, WellHealPct, PylonSeconds, PylonHalfCells, PylonBandCells, PylonEverySeconds, FlameRingSeconds,
                MeteorCount, MeteorReachCells, ChestUpgrades, GuardHpPct, GuardLeashCells, EmperiumSeconds, EmperiumRadius, EmperiumLostSeconds, EmperiumWaveEvery,
                EmperiumWaveCells, EmperiumWaveCount, EmperiumUpgrades, DamagePctPerLevel, EmperiumMobId, ChestItemId }) yield return v;
            foreach (var v in KindWeights ?? new int[0]) yield return v;
            foreach (var w in new[] { FlameRingWeapon, MeteorWeapon, PylonWeapon })
            {
                if (w == null) { yield return -1; continue; }
                foreach (var v in new double[] { w.Id, w.SkillId, (int)w.Kind, (int)w.Target, (int)w.Class, (int)w.Element }) yield return v;
                foreach (var l in w.Levels)
                {
                    foreach (var v in new double[] { l.Damage, l.Cooldown, l.Count, l.Pierce, l.Speed, l.Range, l.Area, l.Arc, l.Spread, l.Burst, l.Delay, l.Duration, l.Tick, l.Heal, l.HealPct, l.Falloff }) yield return v;
                    if (l.Effects != null) foreach (var e in l.Effects) if (e != null) { yield return (int)e.Kind; yield return e.Chance; yield return e.Duration; yield return e.Power; yield return e.Tick; }
                }
            }
        }

        /// <summary>The pylons' four walls around (cx, cy) (any unit), each side kept inside the arena's box: left, bottom, right, top.</summary>
        public static void PylonSquare(float cx, float cy, float half, float minX, float minY, float maxX, float maxY, out float x0, out float y0, out float x1, out float y1)
        {
            x0 = Math.Max(minX, cx - half); x1 = Math.Min(maxX, cx + half); y0 = Math.Max(minY, cy - half); y1 = Math.Min(maxY, cy + half);
        }
    }

    /// <summary>An objective in the engine (client wave 17, h17obj).</summary>
    public sealed class SimObjective
    {
        public int Id;
        public ObjectiveKind Kind;
        public ObjectiveState State;
        public int X, Y, Radius;
        /// <summary>When it appeared; its deadline while Up (then it fades); the end of its lasting effect while Active.</summary>
        public int StartTick, Deadline, EndTick;
        /// <summary>The claim's ring, 0..1, and who stands in it (-1 none); the enemies' weight inside now.</summary>
        public double Progress;
        public int Claimer = -1, Crowd;
        /// <summary>The Emperium: the last tick a standing player was inside, and the next wave's tick.</summary>
        public int LastInside, NextWave, Waves;
        /// <summary>The guarded chest's guard (enemy slot and its serial; -1 none).</summary>
        public int GuardSlot = -1, GuardSerial;
        /// <summary>The pylons while Active: whose they are, the weapon struck with (at its scaled level), the last strike per enemy.</summary>
        public int Owner = -1;
        public OwnedWeapon Weapon;
        public WeaponLevel Lv;
        public readonly Dictionary<int, int> LastHit = new Dictionary<int, int>();
    }

    /// <summary>A gift a player holds for a while (the snapshot shows it): the shrine's speed, the well's healing, the flame ring.</summary>
    public sealed class SimObjectiveBuff
    {
        public int Slot;
        public ObjectiveKind Kind;
        public int Until, NextHeal, FxId;
    }

    public sealed partial class HordeSim
    {
        /// <summary>Client wave 17 (h17obj): the objectives on the map now (and the pylons' walls while they last).</summary>
        public readonly List<SimObjective> Objectives = new List<SimObjective>();
        /// <summary>The gifts players hold now (shrine, well, flame ring).</summary>
        public readonly List<SimObjectiveBuff> ObjectiveBuffs = new List<SimObjectiveBuff>();
        /// <summary>Counters (tests, the host's log): appeared, claimed, faded; and whether this run had its Emperium.</summary>
        public int ObjectivesUp, ObjectivesClaimed, ObjectivesFaded;
        public bool EmperiumSeen { get; private set; }
        /// <summary>The tick the next objective is due (-1 before the first is planned).</summary>
        public int ObjectiveNextTick { get; private set; } = -1;
        private HordeRng _objDice;
        private int _objNextId = 1;
        private ObjectiveKind _objLastKind;
        private readonly List<KeyValuePair<int, int>> _bossEntrances = new List<KeyValuePair<int, int>>();
        private ObjectiveRulesDef ObjRules => C.Objectives;
        /// <summary>The buffs' ids in SimBuff.WeaponId (apart from every weapon's: negative).</summary>
        private static int ObjBuffId(ObjectiveKind k) => -1700 - (int)k;

        /// <summary>From the constructor: the objectives' own dice (the run's dice are untouched until a gift acts) and the bosses'
        /// entrances (their warnings and arrivals, as StepWaves will play them).</summary>
        private void InitObjectives(ulong seed)
        {
            _objDice = new HordeRng(seed ^ 0x0B1EC7175EED2417UL);
            _bossEntrances.Clear();
            var r = ObjRules; if (r == null) return;
            int after = Ticks(r.BossEntranceAfterSeconds);
            foreach (var ws in _waves)
            {
                var d = C.Enemy(ws.EnemyId);
                if (!ws.Def.Once || d == null || d.Rank < EnemyRank.Boss) continue;
                _bossEntrances.Add(new KeyValuePair<int, int>(ws.Next - Ticks(ws.Def.WarnSeconds), ws.Next + after));
            }
            if (C.Enemy(R.FinalBossEnemyId) != null) _bossEntrances.Add(new KeyValuePair<int, int>(RunTicks - Ticks(R.FinalBossWarnSeconds), RunTicks + after));
        }

        /// <summary>True while a boss is making its entrance (no objective appears then).</summary>
        public bool InBossEntrance(int tick)
        {
            foreach (var kv in _bossEntrances) if (tick >= kv.Key && tick <= kv.Value) return true;
            return false;
        }

        /// <summary>Once per tick of the world (after StepRevive): the gifts, the objectives' claims and lives, then a new one when due.</summary>
        private void StepObjectives()
        {
            var r = ObjRules;
            if (r == null || !r.Enabled || Over) return;
            StepObjectiveBuffs(r);
            for (int i = Objectives.Count - 1; i >= 0; i--)
            {
                if (i >= Objectives.Count) continue;
                var o = Objectives[i];
                if (o.State == ObjectiveState.Active) StepPylons(o, r);
                else StepClaim(o, r);
            }
            if (ObjectiveNextTick < 0) ObjectiveNextTick = Math.Max(1, Ticks(r.FirstSeconds));
            if (Tick < ObjectiveNextTick || InBossEntrance(Tick) || PlayersPresent == 0) return;
            int want = PlayersPresent >= Math.Max(2, r.TwoAtOncePlayers) ? 2 : 1, up = 0;
            foreach (var o in Objectives) if (o.State != ObjectiveState.Active) up++;
            if (up >= want) return; // one at a time (two with 3+ players): the next comes when a place is free
            SimObjective first = null; // a second one goes across the party from the first (or from the one already up)
            foreach (var o in Objectives) if (o.State != ObjectiveState.Active) { first = o; break; }
            for (int k = up; k < want; k++) { var o = SpawnObjective(r, first); if (o != null && first == null) first = o; }
            double every = r.EveryMinSeconds + _objDice.NextDouble() * Math.Max(0, r.EveryMaxSeconds - r.EveryMinSeconds);
            ObjectiveNextTick = Tick + Math.Max(1, Ticks(every));
        }

        /// <summary>A new objective: its kind (the weights; never the last kind again; the Emperium once a run), its place (NearCells to
        /// FarCells from the party, in the arena; a second one across the party from <paramref name="other"/>).</summary>
        private SimObjective SpawnObjective(ObjectiveRulesDef r, SimObjective other)
        {
            int total = 0; bool again = false;
            for (int k = 1; k <= 7; k++) total += KindWeight(r, (ObjectiveKind)k, other, false);
            if (total <= 0) { again = true; for (int k = 1; k <= 7; k++) total += KindWeight(r, (ObjectiveKind)k, other, true); } // only the last kind is left: it may come again
            if (total <= 0) return null;
            int roll = _objDice.Next(total); var kind = ObjectiveKind.Shrine;
            for (int k = 1; k <= 7; k++) { int kw = KindWeight(r, (ObjectiveKind)k, other, again); if (kw <= 0) continue; if (roll < kw) { kind = (ObjectiveKind)k; break; } roll -= kw; }
            // the party's middle (standing players; everyone when all fell)
            long sx = 0, sy = 0; int n = 0;
            foreach (var p in Players) if (p.Present && !p.Fallen) { sx += p.X; sy += p.Y; n++; }
            if (n == 0) foreach (var p in Players) if (p.Present) { sx += p.X; sy += p.Y; n++; }
            if (n == 0) return null;
            int cx = (int)(sx / n), cy = (int)(sy / n);
            int margin = Units(r.EdgeMarginCells);
            int lx = MinX + margin, ly = MinY + margin, hx = MaxX - margin, hy = MaxY - margin;
            if (lx > hx) { lx = hx = (MinX + MaxX) / 2; } if (ly > hy) { ly = hy = (MinY + MaxY) / 2; }
            double baseAng = other != null ? Math.Atan2(other.Y - cy, other.X - cx) + Math.PI : _objDice.NextDouble() * Math.PI * 2;
            int x = cx, y = cy;
            for (int tryN = 0; tryN < 12; tryN++)
            {
                double a = other != null ? baseAng + (_objDice.NextDouble() - 0.5) * 1.2 : baseAng + tryN * 0.9;
                double d = r.NearCells + _objDice.NextDouble() * Math.Max(0, r.FarCells - r.NearCells);
                x = Clamp(cx + Units(Math.Cos(a) * d), lx, hx); y = Clamp(cy + Units(Math.Sin(a) * d), ly, hy);
                // far enough from every standing player (a corner may clamp it close) and from the other one
                bool ok = true; long near2 = (long)Units(r.NearCells * 0.6) * Units(r.NearCells * 0.6);
                foreach (var p in Players) if (p.Present && !p.Fallen && Dist2(p.X, p.Y, x, y) < near2) { ok = false; break; }
                if (ok && other != null && Dist2(other.X, other.Y, x, y) < (long)Units(r.ApartCells) * Units(r.ApartCells)) ok = false;
                if (ok) break;
            }
            var o = new SimObjective
            {
                Id = _objNextId, Kind = kind, State = ObjectiveState.Up, X = x, Y = y, Radius = Math.Max(16, Units(r.RadiusOf(kind))),
                StartTick = Tick, Deadline = Tick + Math.Max(1, Ticks(r.WaitSeconds)),
            };
            _objNextId = _objNextId >= 255 ? 1 : _objNextId + 1;
            if (kind == ObjectiveKind.Emperium) EmperiumSeen = true;
            _objLastKind = kind;
            if (kind == ObjectiveKind.Chest) SpawnGuard(o, r);
            Objectives.Add(o); ObjectivesUp++;
            AddEvent(HordeEventKind.ObjectiveUp, HordeEventFlags.None, -1, o.Id, (int)kind, (int)Math.Round(r.WaitSeconds), o.X, o.Y);
            return o;
        }

        private int KindWeight(ObjectiveRulesDef r, ObjectiveKind k, SimObjective other, bool lastAllowed)
        {
            int i = (int)k;
            int w = r.KindWeights != null && i < r.KindWeights.Length ? Math.Max(0, r.KindWeights[i]) : 0;
            if ((k == _objLastKind && !lastAllowed) || (other != null && other.Kind == k)) return 0;
            if (k == ObjectiveKind.Emperium && EmperiumSeen) return 0;
            if (k == ObjectiveKind.Chest && WeakestElite() == null) return 0;
            return w;
        }

        /// <summary>The tables' elite with the least HP (the chest's guard), or null.</summary>
        private EnemyDef WeakestElite()
        {
            EnemyDef best = null;
            foreach (var d in C.Enemies) if (d.Rank == EnemyRank.Elite && !IsDealElite(d.Id) && (best == null || d.Hp < best.Hp)) best = d; // (client wave 18, h18rules: the guard stays a marked elite, not one of the periodic deal)
            return best;
        }

        private void SpawnGuard(SimObjective o, ObjectiveRulesDef r)
        {
            var d = WeakestElite(); if (d == null) return;
            _spawnHpScale = Math.Max(0.05, r.GuardHpPct / 100.0);
            int slot = Spawn(d, Clamp(o.X + Units(1.2), MinX, MaxX), o.Y, true);
            _spawnHpScale = 1;
            if (slot >= 0) { o.GuardSlot = slot; o.GuardSerial = Enemies[slot].Serial; }
        }

        private SimEnemy Guard(SimObjective o) => o.GuardSlot >= 0 && Enemies[o.GuardSlot].Alive && Enemies[o.GuardSlot].Serial == o.GuardSerial ? Enemies[o.GuardSlot] : null;

        /// <summary>A standing player's place counts as inside when its centre is within the circle.</summary>
        private bool Inside(SimObjective o, SimPlayer p) => p.Present && !p.Fallen && Dist2(p.X, p.Y, o.X, o.Y) <= (long)o.Radius * o.Radius;

        private void StepClaim(SimObjective o, ObjectiveRulesDef r)
        {
            // the guard keeps to its box until the box is taken
            var g = Guard(o);
            if (g != null && o.Kind == ObjectiveKind.Chest)
            {
                int leash = Units(r.GuardLeashCells);
                long d2 = Dist2(g.X, g.Y, o.X, o.Y);
                if (d2 > (long)leash * leash) { int d = Math.Max(1, HordeMove.ISqrt(d2)); g.X = Clamp(o.X + (int)((long)(g.X - o.X) * leash / d), MinX, MaxX); g.Y = Clamp(o.Y + (int)((long)(g.Y - o.Y) * leash / d), MinY, MaxY); }
            }
            // who stands in it, and the enemies' weight inside
            SimPlayer claimer = o.Claimer >= 0 && Inside(o, Players[o.Claimer]) ? Players[o.Claimer] : null;
            int insideN = 0;
            foreach (var p in Players) if (Inside(o, p)) { insideN++; if (claimer == null) claimer = p; }
            int crowd = 0;
            foreach (var e in Enemies)
            {
                if (!e.Alive || Dist2(e.X, e.Y, o.X, o.Y) > (long)o.Radius * o.Radius) continue;
                crowd += e.Def.Rank == EnemyRank.Normal ? 1 : e.Def.Rank == EnemyRank.Elite ? Math.Max(1, r.EliteWeight) : Math.Max(1, r.BossWeight);
            }
            o.Crowd = crowd;
            o.Claimer = claimer != null ? claimer.Slot : -1;
            if (o.Kind == ObjectiveKind.Emperium) { StepEmperium(o, r, claimer); return; }
            double full = 1.0 / Math.Max(1, Ticks(r.ClaimSeconds));
            if (claimer != null)
            {
                double pct = Math.Max(r.ClaimMinPct, 100.0 - r.SlowPctPerEnemy * crowd);
                o.Progress += full * pct / 100.0;
                if (o.Progress >= 1.0 - 1e-9) { ClaimObjective(o, claimer, r); return; }
            }
            else o.Progress = Math.Max(0, o.Progress - full * Math.Max(0, r.DrainShare));
            // it fades at its deadline; a claim under way (someone inside, the ring not empty) finishes first
            if (Tick >= o.Deadline && !(claimer != null && o.Progress > 0)) FadeObjective(o, false);
        }

        private void StepEmperium(SimObjective o, ObjectiveRulesDef r, SimPlayer claimer)
        {
            if (o.State == ObjectiveState.Up)
            {
                if (claimer == null) { if (Tick >= o.Deadline) FadeObjective(o, false); return; }
                o.State = ObjectiveState.Defending; o.NextWave = Tick; o.LastInside = Tick;
            }
            if (claimer != null) { o.LastInside = Tick; o.Progress += 1.0 / Math.Max(1, Ticks(r.EmperiumSeconds)); }
            if (o.Progress >= 1.0 - 1e-9) { ClaimObjective(o, claimer ?? Player(o.Claimer) ?? FirstStanding(), r); return; }
            if (Tick - o.LastInside >= Math.Max(1, Ticks(r.EmperiumLostSeconds))) { FadeObjective(o, true); return; }
            if (Tick >= o.NextWave) { o.NextWave = Tick + Math.Max(1, Ticks(r.EmperiumWaveEvery)); EmperiumWave(o, r); }
        }

        private SimPlayer FirstStanding() { foreach (var p in Players) if (p.Present && !p.Fallen) return p; return null; }

        /// <summary>A wave at the Emperium: the enemy the run's waves spawn now (its latest normal line), on a circle around it.</summary>
        private void EmperiumWave(SimObjective o, ObjectiveRulesDef r)
        {
            EnemyDef def = null; int from = int.MinValue;
            foreach (var ws in _waves)
            {
                var d = C.Enemy(ws.EnemyId);
                if (ws.Def.Once || d == null || d.Rank != EnemyRank.Normal || Tick < ws.From || Tick >= ws.To || ws.From < from) continue;
                if (ws.From > from) { from = ws.From; def = d; }
            }
            if (def == null) foreach (var d in C.Enemies) if (d.Rank == EnemyRank.Normal && d.Behavior != EnemyBehavior.Sweep) { def = d; break; }
            if (def == null) return;
            int count = Math.Max(1, (int)Math.Round(r.EmperiumWaveCount * Arr(C.Scaling.SpawnMul, PlayersForScaling())));
            double a0 = _objDice.NextDouble() * Math.PI * 2; int made = 0;
            for (int i = 0; i < count; i++)
            {
                if (EnemiesAlive >= Math.Min(R.MaxEnemies, HordeLimits.MaxEnemies)) break;
                double a = a0 + i * Math.PI * 2 / count;
                if (Spawn(def, Clamp(o.X + Units(Math.Cos(a) * r.EmperiumWaveCells), MinX, MaxX), Clamp(o.Y + Units(Math.Sin(a) * r.EmperiumWaveCells), MinY, MaxY), true) >= 0) made++;
            }
            o.Waves++;
            AddEvent(HordeEventKind.EmperiumWave, HordeEventFlags.None, -1, o.Id, def.Id, made, o.X, o.Y);
        }

        /// <summary>The scaled level of an effect weapon: its damage (and its burn) grown by the party's level.</summary>
        private WeaponLevel ObjLevel(WeaponDef w, ObjectiveRulesDef r)
        {
            var b = w.Levels[0];
            double k = 1 + Math.Max(0, r.DamagePctPerLevel) / 100.0 * Math.Max(0, PartyLevel - 1);
            var l = new WeaponLevel
            {
                Damage = (int)Math.Round(b.Damage * k), Cooldown = b.Cooldown, Count = b.Count, Pierce = b.Pierce, Speed = b.Speed, Range = b.Range, Area = b.Area, Arc = b.Arc,
                Spread = b.Spread, Burst = b.Burst, Delay = b.Delay, Duration = b.Duration, Tick = b.Tick, Heal = b.Heal, HealPct = b.HealPct, Falloff = b.Falloff, Buffs = b.Buffs,
            };
            if (b.Effects != null)
            {
                l.Effects = new EffectDef[b.Effects.Length];
                for (int i = 0; i < b.Effects.Length; i++) { var e = b.Effects[i]; if (e != null) l.Effects[i] = new EffectDef(e.Kind, e.Chance, e.Duration, e.Kind == EffectKind.Dot ? (float)(e.Power * k) : e.Power, e.Tick); }
            }
            return l;
        }

        /// <summary>The claim: the claimer and every standing ally within ShareCells of it get the gift.</summary>
        private void ClaimObjective(SimObjective o, SimPlayer claimer, ObjectiveRulesDef r)
        {
            if (claimer == null) { FadeObjective(o, false); return; }
            var got = new List<SimPlayer> { claimer };
            long share2 = (long)Units(r.ShareCells) * Units(r.ShareCells);
            foreach (var p in Players) if (p != claimer && p.Present && !p.Fallen && Dist2(p.X, p.Y, claimer.X, claimer.Y) <= share2) got.Add(p);
            ObjectivesClaimed++;
            o.Progress = 1; o.Claimer = claimer.Slot;
            AddEvent(HordeEventKind.ObjectiveClaimed, HordeEventFlags.None, claimer.Slot, o.Id, (int)o.Kind, got.Count, o.X, o.Y);
            bool stays = false;
            switch (o.Kind)
            {
                case ObjectiveKind.Shrine:
                    foreach (var p in got) GiveObjectiveBuff(p, o.Kind, r.ShrineSeconds, new[] { new ModDef(Mod.CooldownPct, r.ShrineCooldownPct) });
                    break;
                case ObjectiveKind.Well:
                    foreach (var p in got) GiveObjectiveBuff(p, o.Kind, r.WellSeconds, null);
                    break;
                case ObjectiveKind.Pylons:
                    if (r.PylonWeapon != null && r.PylonWeapon.Levels.Length > 0)
                    {
                        o.State = ObjectiveState.Active; o.EndTick = Tick + Math.Max(1, Ticks(r.PylonSeconds)); o.Owner = claimer.Slot;
                        o.Weapon = new OwnedWeapon { Def = r.PylonWeapon, Level = 1 }; o.Lv = ObjLevel(r.PylonWeapon, r); o.LastHit.Clear();
                        stays = true;
                    }
                    break;
                case ObjectiveKind.FlameRing:
                    foreach (var p in got) GiveFlameRing(p, r);
                    break;
                case ObjectiveKind.Meteor:
                    Meteors(o, claimer, r);
                    break;
                case ObjectiveKind.Chest:
                    foreach (var p in got) if (r.ChestUpgrades > 0) ChestOf(p, r.ChestUpgrades); // (merge h17int: a chest's rules, h17cards' ChestOf: a big chest may evolve, Discount)
                    o.GuardSlot = -1; // the guard is free now (it fights on)
                    break;
                case ObjectiveKind.Emperium:
                    foreach (var p in got) for (int i = 0; i < Math.Max(0, r.EmperiumUpgrades); i++) ChestUpgrade(p);
                    foreach (var q in Pickups) if (q.Def.Kind == PickupKind.Exp && q.PulledBy < 0) { q.PulledBy = claimer.Slot; q.PullTick = Tick; PickupsPulled.Add(q.Id); } // the magnet
                    break;
            }
            float secs = o.Kind == ObjectiveKind.Shrine ? r.ShrineSeconds : o.Kind == ObjectiveKind.Well ? r.WellSeconds : o.Kind == ObjectiveKind.Pylons ? r.PylonSeconds
                       : o.Kind == ObjectiveKind.FlameRing ? r.FlameRingSeconds : 0f;
            foreach (var p in got) AddEvent(HordeEventKind.ObjectiveGot, HordeEventFlags.None, p.Slot, o.Id, (int)o.Kind, (int)Math.Round(secs), p.X, p.Y);
            if (!stays) Objectives.Remove(o);
        }

        private void FadeObjective(SimObjective o, bool lost)
        {
            ObjectivesFaded++;
            var g = Guard(o);
            if (g != null) KillQuiet(g); // the guard leaves with its box
            AddEvent(HordeEventKind.ObjectiveFaded, HordeEventFlags.None, -1, o.Id, (int)o.Kind, lost ? 1 : 0, o.X, o.Y);
            Objectives.Remove(o);
        }

        private void GiveObjectiveBuff(SimPlayer p, ObjectiveKind k, float seconds, ModDef[] mods)
        {
            int until = Tick + Math.Max(1, Ticks(seconds));
            if (mods != null)
            {
                var b = p.Buffs.Find(x => x.WeaponId == ObjBuffId(k) && x.SourceSlot == -1);
                if (b == null) p.Buffs.Add(new SimBuff { Mods = mods, Until = until, SourceSlot = -1, WeaponId = ObjBuffId(k) });
                else b.Until = Math.Max(b.Until, until);
                Recalc(p, false);
            }
            var ob = ObjectiveBuffs.Find(x => x.Slot == p.Slot && x.Kind == k);
            if (ob == null) ObjectiveBuffs.Add(ob = new SimObjectiveBuff { Slot = p.Slot, Kind = k, NextHeal = Tick }); // (the well: the first heal at once, then each second)
            ob.Until = Math.Max(ob.Until, until);
        }

        private void GiveFlameRing(SimPlayer p, ObjectiveRulesDef r)
        {
            var w = r.FlameRingWeapon; if (w == null || w.Levels.Length == 0) return;
            var lv = ObjLevel(w, r);
            var ow = new OwnedWeapon { Def = w, Level = 1 };
            // a ring already turning: it ends, the new one (this level's damage) takes its place
            var had = ObjectiveBuffs.Find(x => x.Slot == p.Slot && x.Kind == ObjectiveKind.FlameRing);
            if (had != null) foreach (var f0 in Fx) if (f0.Id == had.FxId && f0.Kind == HordeFxKind.Orbit && !f0.Ended) EndFx(f0, false);
            var o = NewFx(HordeFxKind.Orbit);
            o.OwnerSlot = p.Slot; o.DefId = w.Id; o.Level = 1; o.X = p.X; o.Y = p.Y;
            o.Count = Math.Max(1, lv.Count); o.Range = Math.Max(16, Units(lv.Range)); o.Radius = Math.Max(16, Units(lv.Area));
            o.Turns = lv.Speed > 0 ? lv.Speed : 0.5; o.Angle = 0; o.Every = Math.Max(1, Ticks(lv.Tick));
            o.EndTick = Tick + Math.Max(1, Ticks(r.FlameRingSeconds));
            o.Weapon = ow; o.Lv = lv; o.BaseDamage = lv.Damage; o.Follows = true;
            ow.OrbitFxId = o.Id;
            BeginFx(o);
            GiveObjectiveBuff(p, ObjectiveKind.FlameRing, r.FlameRingSeconds, null);
            var ob = ObjectiveBuffs.Find(x => x.Slot == p.Slot && x.Kind == ObjectiveKind.FlameRing);
            if (ob != null) { ob.FxId = o.Id; ob.Until = o.EndTick; }
        }

        /// <summary>Meteor beacon: MeteorCount strikes on the thickest crowds near it (each a warning, then the strike: the claimer's).</summary>
        private void Meteors(SimObjective o, SimPlayer claimer, ObjectiveRulesDef r)
        {
            var w = r.MeteorWeapon; if (w == null || w.Levels.Length == 0) return;
            var lv = ObjLevel(w, r);
            var ow = new OwnedWeapon { Def = w, Level = 1 };
            int area = Math.Max(16, Units(lv.Area)), reach = Units(r.MeteorReachCells);
            var used = new List<int>();
            for (int i = 0; i < Math.Max(1, r.MeteorCount); i++)
            {
                // the densest enemy within reach that is not under an earlier strike
                int best = -1; int bestNear = -1; long bestD = long.MaxValue;
                for (int s = 0; s < Enemies.Length; s++)
                {
                    var e = Enemies[s];
                    if (!e.Alive || Dist2(e.X, e.Y, o.X, o.Y) > (long)reach * reach) continue;
                    bool covered = false; foreach (int u in used) if (Dist2(Enemies[u].X, Enemies[u].Y, e.X, e.Y) <= (long)area * area) { covered = true; break; }
                    if (covered) continue;
                    int near = 0; foreach (var q in Enemies) if (q.Alive && Dist2(q.X, q.Y, e.X, e.Y) <= (long)area * area) near++;
                    long d = Dist2(e.X, e.Y, o.X, o.Y);
                    if (near > bestNear || (near == bestNear && d < bestD)) { best = s; bestNear = near; bestD = d; }
                }
                int tx, ty;
                if (best >= 0) { used.Add(best); tx = Enemies[best].X; ty = Enemies[best].Y; }
                else { double a = _objDice.NextDouble() * Math.PI * 2; tx = Clamp(o.X + Units(Math.Cos(a) * 3), MinX, MaxX); ty = Clamp(o.Y + Units(Math.Sin(a) * 3), MinY, MaxY); }
                var f = NewFx(HordeFxKind.Warning);
                f.OwnerSlot = claimer.Slot; f.DefId = w.Id; f.Level = 1; f.X = tx; f.Y = ty; f.Radius = area;
                f.EndTick = Tick + Math.Max(1, Ticks(lv.Delay)) + i * Ticks(0.25); f.Weapon = ow; f.Lv = lv; f.BaseDamage = lv.Damage;
                BeginFx(f);
            }
        }

        /// <summary>Fire pylons while they last: an enemy within the band of a wall is struck (and set burning) every PylonEverySeconds.</summary>
        private void StepPylons(SimObjective o, ObjectiveRulesDef r)
        {
            var owner = o.Owner >= 0 ? Players[o.Owner] : null;
            if (Tick >= o.EndTick || owner == null || !owner.Present || o.Weapon == null) { Objectives.Remove(o); return; }
            ObjectiveRulesDef.PylonSquare(o.X, o.Y, Units(r.PylonHalfCells), MinX, MinY, MaxX, MaxY, out float fx0, out float fy0, out float fx1, out float fy1);
            int x0 = (int)fx0, y0 = (int)fy0, x1 = (int)fx1, y1 = (int)fy1, band = Math.Max(16, Units(r.PylonBandCells)), every = Math.Max(1, Ticks(r.PylonEverySeconds));
            for (int s = 0; s < Enemies.Length; s++)
            {
                var e = Enemies[s];
                if (!e.Alive) continue;
                if (e.X < x0 - band || e.X > x1 + band || e.Y < y0 - band || e.Y > y1 + band) continue; // outside the square's outer edge
                int wx = e.X, wy = e.Y;
                bool onWall = false;
                if (Math.Abs(e.X - x0) <= band) { onWall = true; wx = x0; } else if (Math.Abs(e.X - x1) <= band) { onWall = true; wx = x1; }
                else if (Math.Abs(e.Y - y0) <= band) { onWall = true; wy = y0; } else if (Math.Abs(e.Y - y1) <= band) { onWall = true; wy = y1; }
                if (!onWall) continue;
                int key = s * 256 + e.Serial;
                if (o.LastHit.TryGetValue(key, out int last) && Tick - last < every) continue;
                o.LastHit[key] = Tick;
                HitEnemy(owner, o.Weapon, o.Lv, e, o.Lv.Damage, wx, wy);
            }
            if (o.LastHit.Count > 2000) o.LastHit.Clear();
        }

        /// <summary>The gifts: the well's healing each second, and the end of each (the shrine's SimBuff ends in StepPlayers).</summary>
        private void StepObjectiveBuffs(ObjectiveRulesDef r)
        {
            for (int i = ObjectiveBuffs.Count - 1; i >= 0; i--)
            {
                var b = ObjectiveBuffs[i];
                var p = Player(b.Slot);
                bool ringGone = b.Kind == ObjectiveKind.FlameRing && !Fx.Exists(f => f.Id == b.FxId && f.Kind == HordeFxKind.Orbit && !f.Ended);
                if (p == null || Tick >= b.Until || ringGone) { ObjectiveBuffs.RemoveAt(i); continue; }
                if (b.Kind == ObjectiveKind.Well && Tick >= b.NextHeal)
                {
                    b.NextHeal = Tick + TickRate;
                    if (p.Fallen || p.Hp >= p.MaxHp) continue;
                    int h = Math.Max(1, (int)Math.Round(p.MaxHp * r.WellHealPct / 100.0));
                    int before = p.Hp; p.Hp = Math.Min(p.MaxHp, p.Hp + h);
                    AddEvent(HordeEventKind.Heal, HordeEventFlags.None, p.Slot, -1, 0, p.Hp - before, p.X, p.Y);
                }
            }
        }

        /// <summary>StateHash's part (the determinism tests).</summary>
        private void HashObjectives(Action<long> m)
        {
            m(ObjectiveNextTick);
            foreach (var o in Objectives) { m(o.Id); m((int)o.Kind); m((int)o.State); m(o.X); m(o.Y); m((long)(o.Progress * 1e6)); m(o.Claimer); }
            foreach (var b in ObjectiveBuffs) { m(b.Slot); m((int)b.Kind); m(b.Until); }
        }

        /// <summary>A line for the host's log for an objective's event (null for any other event).</summary>
        public static string ObjectiveLogLine(HordeEvent e, int tick)
        {
            string t = $"{tick / TickRate / 60}:{tick / TickRate % 60:00}";
            switch (e.Kind)
            {
                case HordeEventKind.ObjectiveUp: return $"objective {e.Target} ({(ObjectiveKind)e.DefId}) up at {e.X:0.#},{e.Y:0.#} ({t})";
                case HordeEventKind.ObjectiveClaimed: return $"objective {e.Target} ({(ObjectiveKind)e.DefId}) claimed by slot {e.Slot}, {e.Value} player(s) got it ({t})";
                case HordeEventKind.ObjectiveFaded: return $"objective {e.Target} ({(ObjectiveKind)e.DefId}) {(e.Value != 0 ? "lost" : "faded")} ({t})";
                default: return null;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------- the client's view

    /// <summary>An objective as a client knows it (client wave 17, h17obj): places in the map's cells, times in server ticks.</summary>
    public sealed class HordeObjectiveView
    {
        public int Id;
        public ObjectiveKind Kind;
        public ObjectiveState State;
        public float X, Y, Radius;
        /// <summary>The claim's ring 0..1; who stands in it (-1 none); the enemies' weight inside (they slow it).</summary>
        public float Progress;
        public int Claimer = -1, Crowd;
        /// <summary>When it appeared; Up: its fade deadline; Defending: when it is lost if nobody comes back; Active: its effect's end.</summary>
        public int StartTick, EndTick;
        /// <summary>The guarded chest's guard (enemy slot; -1 none).</summary>
        public int GuardSlot = -1;
    }

    /// <summary>A gift a player holds (until the tick).</summary>
    public struct HordeObjectiveBuffView
    {
        public int Slot;
        public ObjectiveKind Kind;
        public int Until;
    }

    /// <summary>
    /// The objectives' block of H_SNAP (client wave 17, h17obj; protocol 3), after the events (and after h17pass's block once merged).
    /// Every snapshot, key or delta, carries all of it (two objectives at most, a few gifts):
    ///   u8 n × (u8 id, u8 kind, u8 state, u16 x, u16 y (1/256 cell from the arena's corner), u8 radius × 16, u8 progress × 255,
    ///           i8 claimer, u8 crowd, i32 start tick, i32 end tick, i16 guard slot)
    ///   u8 m × (u8 slot, u8 kind, i32 until tick)
    /// </summary>
    public static class HordeObjectiveWire
    {
        public static void Write(HordeWire.W w, HordeSim sim, int bx, int by)
        {
            int n = Math.Min(255, sim.Objectives.Count);
            w.U8(n);
            for (int i = 0; i < n; i++)
            {
                var o = sim.Objectives[i];
                var g = o.GuardSlot >= 0 && sim.Enemies[o.GuardSlot].Alive && sim.Enemies[o.GuardSlot].Serial == o.GuardSerial ? o.GuardSlot : -1;
                int end = o.State == ObjectiveState.Up ? o.Deadline : o.State == ObjectiveState.Defending ? o.LastInside + HordeSim.Ticks(sim.C.Objectives != null ? sim.C.Objectives.EmperiumLostSeconds : 6f) : o.EndTick;
                w.U8(o.Id).U8((byte)o.Kind).U8((byte)o.State).U16(HordeSim.Clamp(o.X - bx, 0, 65535)).U16(HordeSim.Clamp(o.Y - by, 0, 65535))
                 .U8(HordeSim.Clamp(o.Radius * 16 / HordeSim.U, 0, 255)).U8(HordeSim.Clamp((int)Math.Round(o.Progress * 255), 0, 255)).I8(o.Claimer).U8(Math.Min(255, o.Crowd))
                 .I32(o.StartTick).I32(end).I16(g);
            }
            int m = Math.Min(255, sim.ObjectiveBuffs.Count);
            w.U8(m);
            for (int i = 0; i < m; i++) { var b = sim.ObjectiveBuffs[i]; w.U8(b.Slot).U8((byte)b.Kind).I32(b.Until); }
        }

        public static void Read(ref HordeWire.R r, HordeWorldView v, float bx, float by)
        {
            v.Objectives.Clear(); v.ObjectiveBuffs.Clear();
            int n = r.U8();
            for (int i = 0; i < n && !r.Bad; i++)
            {
                var o = new HordeObjectiveView { Id = r.U8(), Kind = (ObjectiveKind)r.U8(), State = (ObjectiveState)r.U8() };
                o.X = bx + r.U16() / (float)HordeSim.U; o.Y = by + r.U16() / (float)HordeSim.U; o.Radius = r.U8() / 16f; o.Progress = r.U8() / 255f;
                o.Claimer = r.I8(); o.Crowd = r.U8(); o.StartTick = r.I32(); o.EndTick = r.I32(); o.GuardSlot = r.I16();
                v.Objectives.Add(o);
            }
            int m = r.U8();
            for (int i = 0; i < m && !r.Bad; i++) v.ObjectiveBuffs.Add(new HordeObjectiveBuffView { Slot = r.U8(), Kind = (ObjectiveKind)r.U8(), Until = r.I32() });
        }
    }
}
