#nullable disable
using System;
using System.Collections.Generic;

namespace Loki.Horde
{
    /// <summary>
    /// Horde's simulation (client wave 8, w8core): fixed ticks (40 a second since client wave 9, h9move; 20 in wave 8), seeded,
    /// deterministic (the same content, seed, options and inputs give the same run, tick for tick: <see cref="StateHash"/>),
    /// host-authoritative (it runs in Loki Host's relay; clients only draw and predict their own walk). Places are integers:
    /// 1 cell = 256 units, in the MAP's cell coordinates. Everything the tables do not say is the engine's rule, written here;
    /// every number of the game itself comes from <see cref="HordeContent"/> (Loki's own design, not official values).
    /// Client wave 9: a player's walk is a velocity that ramps toward the stick (HordeMove.StepRamp: the client predicts the same
    /// integers), the arena's bound is a soft wall, knockbacks are decaying impulses, enemies keep apart from each other AND
    /// from the players' bodies (a crowd parts; a player is never pushed: its place is only ever its own walk), pickups fly to
    /// their taker with an ease-in the client can draw.
    ///
    /// The caller (Loki Host's HordeService, the tests, the bots) drives it: <see cref="AddPlayer"/>, <see cref="PushInput"/>,
    /// <see cref="Choose"/>, <see cref="Step"/>; after each Step it reads what happened (<see cref="Events"/>, <see cref="Hits"/>,
    /// <see cref="FxBegun"/>, <see cref="FxEnded"/>, <see cref="OffersOpened"/>, <see cref="OffersClosed"/>) and calls
    /// <see cref="ClearOutputs"/> when it has sent them.
    /// </summary>
    public sealed partial class HordeSim
    {
        public const int TickRate = HordeLimits.TickRate;
        public const int U = HordeMove.Unit;

        public readonly HordeContent C;
        public readonly ArenaDef Arena;
        public readonly RulesDef R;
        /// <summary>The arena in units (players and enemies stay inside, both ends included).</summary>
        public readonly int MinX, MinY, MaxX, MaxY;
        public int Tick { get; private set; }
        /// <summary>The run's length in ticks (the final boss comes then) and the final boss's timer in ticks.</summary>
        public readonly int RunTicks, FinalTicks;
        public readonly float RunSeconds;
        /// <summary>Run length / reference length: the waves' times are stretched by it, EXP shrunk by it.</summary>
        public readonly double TimeScale;
        public bool FinalPhase { get; private set; }
        /// <summary>design note, 2026-10-05 00:05 ("the game should pause when selecting an augment"): while any player has an open
        /// card or job offer the world waits: Step advances nothing (no tick, no enemies, weapons, waves, pickups, timers;
        /// inputs stay queued and are applied when it resumes), <see cref="WallTick"/> alone counts on (the choice's timeout:
        /// <see cref="CurrentChoiceSeconds"/>). The snapshot says so (flag 4) with the slots that are choosing (HordePlayerFlags.Choosing).</summary>
        public bool Paused { get; private set; }
        /// <summary>Client wave 15 (w15hfx), point 19 of the design's wave 14 list ("a pause button for the Horde menu, one that does not include
        /// having to leave the run"): the slot of the player who PAUSED the run from its Horde menu (H_PAUSE), -1 when none. While held the
        /// world waits exactly as for a choice (<see cref="Paused"/>), for everyone; the open choices' clocks wait too. Any player of the
        /// run may resume it (the orchestrator's proposal, flagged for review); it also ends when its holder leaves the run or loses its link.
        /// The snapshot marks the holder (HordePlayerFlags.Held).</summary>
        public int HeldBy { get; private set; } = -1;
        /// <summary>Pause (on) or resume (off) asked by <paramref name="slot"/>; false when nothing changed (not a player, the run over,
        /// already held: the first holder stays, nothing to resume).</summary>
        public bool Hold(int slot, bool on)
        {
            var p = Player(slot);
            if (p == null || Over) return false;
            LastHold = HordeHoldResult.None; // client wave 18 (h18rules), point 23: with others a 3 s countdown, alone at once (HordeSim.Rules.cs)
            if (PauseCountsDown(slot)) return HoldCountdown(slot, on); // client wave 18 (h18rules)
            CancelPauseCount(); // client wave 18 (h18rules)
            if (on) { if (HeldBy >= 0) return false; HeldBy = slot; LastHold = HordeHoldResult.Paused; return true; } // (client wave 18, h18rules: + LastHold)
            if (HeldBy < 0) return false;
            HeldBy = -1; LastHold = HordeHoldResult.Resumed; return true; // (client wave 18, h18rules: + LastHold)
        }
        /// <summary>Steps taken, pauses included (the host calls Step at the tick rate throughout).</summary>
        public int WallTick { get; private set; }
        /// <summary>design note, 2026-10-05 01:25: the run's current limit for a choice (seconds of wall time; HordeOffer.Seconds carries it):
        /// RulesDef.ChoiceSeconds at the start (alone: no limit at all since client wave 15, point 26); each time it is MET (an auto-pick, or the answer that ended the pause
        /// came within the window's last second) it drops by ChoiceSecondsStep to at least ChoiceSecondsMin, for the whole party,
        /// never back up within the run.</summary>
        public float CurrentChoiceSeconds { get; private set; }
        /// <summary>Times the limit was met in this run.</summary>
        public int ChoiceLimitsMet { get; private set; }
        private bool _autoPickedInPause, _lastAnswerLate;
        /// <summary>design note, 2026-10-05 00:10 ("all players share the same experience bar"): run EXP is ONE party pool and the run level
        /// ONE party level (every pickup, whoever takes it, feeds the pool; the level's requirement is ExpTable × RulesDef.PartyExpScale
        /// [players - 1]); at a party level-up every present player, fallen or not, gets its card offer in the same pause; the job
        /// marks are party levels, each player picks its own job. Per player stay HP, weapons, passives, stats, score, kills, reward.</summary>
        public int PartyLevel { get; private set; } = 1;
        public int PartyExp { get; private set; }
        private double _partyExpCarry;
        public bool Over { get; private set; }
        public HordeOutcome Outcome { get; private set; }
        public int EndTick { get; private set; }

        public readonly SimPlayer[] Players = new SimPlayer[HordeLimits.MaxPlayers];
        public readonly SimEnemy[] Enemies = new SimEnemy[HordeLimits.MaxEnemies];
        public int EnemiesAlive { get; private set; }
        public readonly List<SimPickup> Pickups = new List<SimPickup>();
        public readonly List<SimFx> Fx = new List<SimFx>();
        public readonly List<SimSummon> Summons = new List<SimSummon>();

        // ---- outputs since the last ClearOutputs
        public readonly List<HordeEvent> Events = new List<HordeEvent>();
        /// <summary>Every hit on an enemy (the sender keeps the ones worth drawing).</summary>
        public readonly List<HordeEvent> Hits = new List<HordeEvent>();
        public readonly List<SimFx> FxBegun = new List<SimFx>();
        public readonly List<KeyValuePair<int, bool>> FxEnded = new List<KeyValuePair<int, bool>>();
        public readonly List<KeyValuePair<int, HordeOffer>> OffersOpened = new List<KeyValuePair<int, HordeOffer>>();
        public readonly List<KeyValuePair<int, int>> OffersClosed = new List<KeyValuePair<int, int>>();
        public readonly List<int> PickupsAdded = new List<int>();
        public readonly List<int> PickupsRemoved = new List<int>();
        /// <summary>Pickups that began flying to a player this tick (their PulledBy / PullTick are set).</summary>
        public readonly List<int> PickupsPulled = new List<int>();

        private readonly HordeRng _rng;
        /// <summary>The soft wall's width in units, and the knockback impulse's keep per tick (percent), from the rules.</summary>
        private readonly int _wallUnits, _kbKeepPct;
        private int _nextFxId = 1, _nextPickupId = 1, _nextSummonId = 1, _nextOfferId = 1;
        private readonly WaveState[] _waves;
        private int _finalBossSlot = -1;
        private bool _finalWarned;
        private int _waveCycle;
        private readonly int[] _chargeAbWarned = new int[HordeLimits.MaxEnemies], _chargeAbVx = new int[HordeLimits.MaxEnemies], _chargeAbVy = new int[HordeLimits.MaxEnemies];
        private readonly Grid _grid;

        private sealed class WaveState
        {
            public WaveDef Def; public int From, To, Every, Next; public double Carry; public bool Done, Warned;
            /// <summary>Client wave 15 (w15horde), point 21: the enemy this wave spawns in THIS run (the tables' own unless the bosses
            /// were shuffled), and the strength it takes from the tables' boss of its mark (HP and damage ratios; 1 = its own).</summary>
            public int EnemyId; public double HpScale = 1, DmgScale = 1;
        }

        /// <summary>Client wave 15 (w15horde), the design's point 21 ("the bosses shouldn't always come in a predetermined order, e.g. the
        /// orc hero always being the first boss"): the bosses' marks (Once waves of EnemyRank.Boss) are dealt a random order per run,
        /// from the run's seed (a separate generator: the run's own dice are untouched). A boss keeps its own body, attacks and adds,
        /// but takes the HP and damage of the tables' boss of the mark it lands on (its adds and attacks too), so the difficulty curve
        /// stays the tables'. Within a run no boss comes twice. Elites keep their order; the final boss stays RulesDef.FinalBossEnemyId.</summary>
        public static bool ShuffleBosses = true;
        /// <summary>The bosses of this run's marks, in order (the tests, the log).</summary>
        public readonly List<int> BossOrder = new List<int>();
        // the strength the next Spawn takes (a shuffled boss, and the adds of a shuffled boss)
        private double _spawnHpScale = 1, _spawnDmgScale = 1;

        public HordeSim(HordeContent content, ArenaDef arena, ulong seed, float runSeconds)
        {
            C = content ?? throw new ArgumentNullException(nameof(content));
            Arena = arena ?? throw new ArgumentNullException(nameof(arena));
            R = content.Rules;
            if (arena.X1 - arena.X0 + 1 > HordeLimits.MaxArenaCells || arena.Y1 - arena.Y0 + 1 > HordeLimits.MaxArenaCells || arena.X1 < arena.X0 || arena.Y1 < arena.Y0)
                throw new ArgumentException($"arena {arena.Map} {arena.X0},{arena.Y0}-{arena.X1},{arena.Y1}: at most {HordeLimits.MaxArenaCells} cells a side");
            MinX = arena.X0 * U; MinY = arena.Y0 * U; MaxX = (arena.X1 + 1) * U - 1; MaxY = (arena.Y1 + 1) * U - 1;
            _rng = new HordeRng(seed);
            RunSeconds = runSeconds > 0 ? runSeconds : R.ReferenceRunSeconds;
            TimeScale = R.ReferenceRunSeconds > 0 ? RunSeconds / R.ReferenceRunSeconds : 1.0;
            RunTicks = Math.Max(1, (int)Math.Round(RunSeconds * TickRate));
            FinalTicks = Math.Max(1, (int)Math.Round(R.FinalBossSeconds * TickRate));
            for (int i = 0; i < Players.Length; i++) Players[i] = new SimPlayer { Slot = i };
            for (int i = 0; i < Enemies.Length; i++) Enemies[i] = new SimEnemy { Slot = i };
            _waves = new WaveState[C.Waves.Count];
            for (int i = 0; i < _waves.Length; i++)
            {
                var w = C.Waves[i];
                _waves[i] = new WaveState
                {
                    Def = w, From = Ticks(w.From * TimeScale), To = w.Once ? int.MaxValue : Ticks(w.To * TimeScale),
                    Every = Math.Max(1, Ticks(w.Interval)), Next = Ticks(w.From * TimeScale), EnemyId = w.EnemyId,
                };
            }
            if (ShuffleBosses) DealBosses(seed);
            foreach (var ws in _waves) { var d = C.Enemy(ws.EnemyId); if (ws.Def.Once && d != null && d.Rank == EnemyRank.Boss) BossOrder.Add(d.Id); }
            InitObjectives(seed); // client wave 17 (h17obj): the map objectives (HordeSim.Objectives.cs)
            InitSouls(seed); // client wave 18 (h18feel), point 35: soul retrieval (HordeSim.Souls.cs)
            _grid = new Grid(MinX, MinY, MaxX, MaxY);
            CurrentChoiceSeconds = Math.Max(R.ChoiceSecondsMin, R.ChoiceSeconds);
            _wallUnits = Math.Max(0, Units(R.WallSoftCells));
            _kbKeepPct = Clamp((int)Math.Round(R.KnockbackKeepPct), 0, 95);
        }

        /// <summary>Point 21 (client wave 15): a random order of the bosses over the boss marks (see <see cref="ShuffleBosses"/>).</summary>
        private void DealBosses(ulong seed)
        {
            var marks = new List<WaveState>();
            foreach (var ws in _waves) { var d = C.Enemy(ws.Def.EnemyId); if (ws.Def.Once && d != null && d.Rank == EnemyRank.Boss) marks.Add(ws); }
            if (marks.Count < 1) return; // client wave 17 (h17boss): one mark is enough (the pool may hold more bosses than marks; was < 2)
            marks.Sort((a, b) => a.Next != b.Next ? a.Next.CompareTo(b.Next) : a.Def.EnemyId.CompareTo(b.Def.EnemyId));
            var ids = new List<int>(); foreach (var m in marks) ids.Add(m.Def.EnemyId);
            BossPool(ids); // client wave 17 (h17boss): the bosses without a mark of their own join the deal (HordeSim.Bosses.cs)
            var dice = new HordeRng(seed ^ 0xB055B0550D0E5EEDUL);
            for (int i = ids.Count - 1; i > 0; i--) { int j = dice.Next(i + 1); int t = ids[i]; ids[i] = ids[j]; ids[j] = t; }
            for (int i = 0; i < marks.Count; i++)
            {
                var m = marks[i]; var mine = C.Enemy(ids[i]); var slot = C.Enemy(m.Def.EnemyId);
                m.EnemyId = ids[i];
                m.HpScale = mine.Hp > 0 ? slot.Hp / (double)mine.Hp : 1;
                m.DmgScale = mine.Damage > 0 ? slot.Damage / (double)mine.Damage : 1;
            }
        }

        public static int Ticks(double seconds) => seconds <= 0 ? 0 : (int)Math.Round(seconds * TickRate);
        public static int Units(double cells) => (int)Math.Round(cells * U);
        public static float Cells(int units) => units / (float)U;

        public int PlayersPresent { get { int n = 0; foreach (var p in Players) if (p.Present) n++; return n; } }

        public void ClearOutputs()
        {
            Events.Clear(); Hits.Clear(); FxBegun.Clear(); FxEnded.Clear(); OffersOpened.Clear(); OffersClosed.Clear();
            PickupsAdded.Clear(); PickupsRemoved.Clear(); PickupsPulled.Clear();
        }

        // =============================================================================================== players

        /// <summary>A player enters the run (at its start, or late: as a Novice at the party's lowest level). Returns its slot,
        /// or -1 when the run is full or over.</summary>
        public int AddPlayer(uint aid, string name) => AddPlayer(aid, name, 0);

        /// <summary>As above, with the real character's sex (1 male, 2 female, 0 not known): it filters JobDef.Sex.</summary>
        public int AddPlayer(uint aid, string name, byte sex)
        {
            if (Over) return -1;
            foreach (var q in Players) if (q.Present && q.Aid == aid) return q.Slot;
            SimPlayer p = null;
            foreach (var q in Players) if (!q.Present && !q.Used) { p = q; break; }
            if (p == null) return -1;
            bool late = Tick > 0;
            // client wave 15 (w15horde), the design's point 25 ("If I leave a run, require it to remember the choices for my abilities, items"):
            // the latest earlier stay of this account in this run, if any: its build comes back
            SimPlayer before = null;
            foreach (var q in Players) if (q.Used && !q.Present && q.Left && q.Aid == aid && (before == null || q.LeftTick >= before.LeftTick)) before = q;
            p.Reset(); CardsReset(p.Slot); // client wave 17 (h17cards)
            p.Present = true; p.Used = true; p.Aid = aid; p.Name = name ?? ""; p.JoinTick = Tick; p.Sex = sex;
            var job = C.StartJob();
            p.JobId = job != null ? job.JobId : 0; p.Tier = 0;
            if (job != null && before == null) p.Jobs.Add(job);
            // the party's level (a late joiner enters at it as a Novice with the starting weapon and is offered the job changes whose
            // marks the party passed; no catch-up card offers: the simplest rule, and it keeps the pause short for the others)
            p.Level = PartyLevel;
            // places: around the arena's centre, one cell apart
            int cx = (MinX + MaxX) / 2, cy = (MinY + MaxY) / 2;
            double ang = p.Slot * Math.PI / 4;
            p.X = Clamp(cx + (int)(Math.Cos(ang) * 1.5 * U), MinX, MaxX); p.Y = Clamp(cy + (int)(Math.Sin(ang) * 1.5 * U), MinY, MaxY);
            var start = C.Weapon(R.StartWeapon);
            if (before != null) Rebuild(p, before);
            else if (start != null && start.Levels.Length > 0) p.Weapons.Add(new OwnedWeapon { Def = start, Level = 1, NextTick = Tick + TickRate });
            Recalc(p, true);
            p.Hp = p.MaxHp;
            p.SafeUntil = Tick + Ticks(R.ReviveSafeSeconds);
            AddEvent(HordeEventKind.PlayerJoined, late ? HordeEventFlags.Late : HordeEventFlags.None, p.Slot, -1, 0, p.Level, p.X, p.Y);
            // a late joiner chooses the jobs whose marks the party has passed, one after the other
            if (late) QueueJobOffer(p);
            // a returning player gets back the card choices it had not made when it left (point 25)
            if (before != null) for (int i = 0; i < before.CardsOwed; i++) QueueCardOffer(p);
            return p.Slot;
        }

        /// <summary>Client wave 15 (w15horde), point 25: a player back in a run it left gets its build as it was when it left: its jobs
        /// (job, tier and the jobs passed through, which open its weapons and passives), every weapon and passive at its level, its
        /// stat points (cards and job bonuses). Not its HP (it comes back whole, as any joiner), not the party's level (shared; the
        /// card offers of levels gained while it was away are not given: the late joiner's rule), not its kills or score (the results
        /// add up its stays, wave 14 point 31).</summary>
        private void Rebuild(SimPlayer p, SimPlayer before)
        {
            p.JobId = before.JobId; p.Tier = before.Tier;
            foreach (var j in before.Jobs) p.Jobs.Add(j);
            foreach (var w in before.Weapons) p.Weapons.Add(new OwnedWeapon { Def = w.Def, Level = w.Level, Signature = w.Signature, NextTick = Tick + TickRate });
            foreach (var ps in before.Passives) p.Passives.Add(new OwnedPassive { Def = ps.Def, Level = ps.Level });
            Array.Copy(before.StatPoints, p.StatPoints, 6);
            p.Rebuilt = true;
        }

        /// <summary>The player leaves the run (its result stands as it is now).</summary>
        public void RemovePlayer(int slot)
        {
            var p = Player(slot);
            if (p == null) return;
            // the card choices it leaves unmade are owed to it if it comes back (point 25; a job choice is offered again anyway)
            p.CardsOwed = 0;
            if (p.Open != null && !p.Open.IsJob) p.CardsOwed++;
            foreach (var o in p.Offers) if (!o.IsJob) p.CardsOwed++;
            CloseOffers(p);
            p.Present = false; p.Left = true; p.LeftTick = Tick;
            foreach (var f in Fx) if (f.OwnerSlot == slot && f.Kind == HordeFxKind.Orbit && f.EndTick > Tick) EndFx(f, false);
            AddEvent(HordeEventKind.PlayerLeft, HordeEventFlags.None, slot, -1, 0, 0, p.X, p.Y);
            CheckEnd();
        }

        public SimPlayer Player(int slot) => slot >= 0 && slot < Players.Length && Players[slot].Present ? Players[slot] : null;
        public SimPlayer PlayerByAid(uint aid) { foreach (var p in Players) if (p.Present && p.Aid == aid) return p; return null; }

        /// <summary>One tick of the player's stick (seq counts up per input; the host applies each input once, in order).</summary>
        public void PushInput(int slot, ushort seq, int dx, int dy)
        {
            var p = Player(slot);
            if (p == null) return;
            if (p.Inputs.Count >= 40) p.Inputs.Dequeue();
            p.Inputs.Enqueue(new SimInput { Seq = seq, Dx = (sbyte)Clamp(dx, -127, 127), Dy = (sbyte)Clamp(dy, -127, 127) });
            p.Away = false;
        }

        public void SetAway(int slot, bool away) { var p = Player(slot); if (p != null) { p.Away = away; if (away) p.Inputs.Clear(); } }

        /// <summary>The player's choice for an open offer (ignored when that offer is not the open one).</summary>
        public bool Choose(int slot, int offerId, int index)
        {
            var p = Player(slot);
            if (p == null || p.Open == null || p.Open.Id != offerId) return false;
            if (index < 0 || index >= p.Open.Cards.Length) index = 0;
            p.IdleChooser = false;
            // an answer within the window's last second counts as the limit met, if it is the one that ends the pause (Step decides)
            _lastAnswerLate = p.OpenDeadline != NoDeadline && WallTick >= p.OpenDeadline - Ticks(1.0);
            ApplyCard(p, p.Open, p.Open.Cards[index]);
            return true;
        }

        // =============================================================================================== the tick

        public void Step()
        {
            if (Over) return;
            WallTick++;
            StepPauseCount(); // client wave 18 (h18rules), point 23: the co-op pause's countdown (HordeSim.Rules.cs)
            // client wave 15 (w15hfx), point 19: a pause held from the Horde menu (HeldBy) ends with its holder; while it holds, the open
            // choices' deadlines move on with the wall clock (nobody's choice runs out during it)
            if (HeldBy >= 0 && (Player(HeldBy) == null || Players[HeldBy].Away)) HeldBy = -1;
            if (HeldBy >= 0) foreach (var hp in Players) if (hp.Present && hp.Open != null) hp.OpenDeadline++;
            StepOffers();
            bool wasPaused = Paused;
            Paused = HeldBy >= 0;
            foreach (var p in Players) if (p.Present && p.Open != null) { Paused = true; break; }
            if (wasPaused && !Paused)
            {
                // the pause ended: was its limit met (the host picked, or the last answer came in the window's last second)?
                if (_autoPickedInPause || _lastAnswerLate) { ChoiceLimitsMet++; CurrentChoiceSeconds = Math.Max(R.ChoiceSecondsMin, CurrentChoiceSeconds - Math.Max(0, R.ChoiceSecondsStep)); }
                _autoPickedInPause = _lastAnswerLate = false;
            }
            if (!wasPaused && Paused) { _autoPickedInPause = _lastAnswerLate = false; }
            if (Paused) return; // the world waits while someone chooses (the tick does not advance; nothing moves, nothing times out)
            Tick++;
            StepPlayers();
            StepPassives(); // client wave 17 (h17pass): the passives' timers and states (HordeSim.Passives.cs)
            StepWaves();
            StepElites(); // client wave 18 (h18rules): the elites that come now and then (HordeSim.Rules.cs)
            _grid.Build(Enemies);
            StepEnemies();
            _grid.Build(Enemies);
            StepWeapons();
            StepFx();
            StepBosses(); // client wave 17 (h17boss): the bosses' warnings and strikes (HordeSim.Bosses.cs)
            StepEnemyEffects();
            StepPickups();
            StepSouls(); // client wave 18 (h18feel), point 35: soul retrieval replaces the timed rise (StepRevive) (HordeSim.Souls.cs)
            StepObjectives(); // client wave 17 (h17obj): the map objectives (HordeSim.Objectives.cs)
            CheckEnd();
        }

        private void StepPlayers()
        {
            foreach (var p in Players)
            {
                if (!p.Present) continue;
                // the stick: each input is one tick of walking (the velocity ramps toward it: HordeMove.StepRamp, the client's own
                // prediction); up to two inputs may wait (they absorb the link's jitter), more are caught up two or three a tick;
                // no input, no step at all (a stalled client stands, its velocity held as it was: the client, which never saw the
                // gap, predicts exactly the same when its inputs resume)
                int steps = p.Inputs.Count > 6 ? 3 : p.Inputs.Count > 2 ? 2 : p.Inputs.Count > 0 ? 1 : 0;
                for (int s = 0; s < steps; s++)
                {
                    var inp = p.Inputs.Dequeue();
                    p.AckSeq = inp.Seq;
                    if (p.Fallen) { p.Vx = p.Vy = 0; continue; }
                    int dx = inp.Dx, dy = inp.Dy;
                    HordeMove.StepRamp(ref p.X, ref p.Y, ref p.Vx, ref p.Vy, dx, dy, p.SpeedPerTick, p.AccelPerTick, p.DecelPerTick, _wallUnits, MinX, MinY, MaxX, MaxY);
                    BossPull(p); // client wave 17 (h17boss): a boss's pull, per input (HordeSim.Bosses.cs; the client predicts the same)
                    if (dx != 0 || dy != 0) p.FaceDeg = (int)Math.Round(Math.Atan2(dy, dx) * 180.0 / Math.PI);
                }
                p.Moving = steps > 0 && !p.Fallen && (p.Vx != 0 || p.Vy != 0);
                if (p.Fallen) continue;
                StepCardsPlayer(p); // client wave 17 (h17cards): a dash, HP while walking, Fly Wing, the guards, Plagiarism
                StepSlide(p); // client wave 18 (h18rules), point 27: Back Slide's exploding trail (HordeSim.Rules.cs)
                // regeneration
                if (p.S.Regen > 0 && p.Hp < p.MaxHp)
                {
                    p.RegenCarry += p.S.Regen / TickRate;
                    if (p.RegenCarry >= 1) { int h = (int)p.RegenCarry; p.RegenCarry -= h; p.Hp = Math.Min(p.MaxHp, p.Hp + h); }
                }
                // buffs that ran out
                bool changed = false;
                for (int i = p.Buffs.Count - 1; i >= 0; i--) if (p.Buffs[i].Until <= Tick) { p.Buffs.RemoveAt(i); changed = true; }
                if (changed) Recalc(p, false);
            }
        }

        /// <summary>Every Step, paused or not: the offers' timeouts and the next queued offer of every present player (fallen too:
        /// its client can answer; everyone with a pending offer chooses during the same pause). A choice not answered within the run's
        /// current limit (<see cref="CurrentChoiceSeconds"/> of wall time; none when alone, client wave 15) takes its first card; a player who lets one
        /// lapse is not safe while choosing again until it answers one itself (no endless safety from never answering).</summary>
        private void StepOffers()
        {
            bool solo = PlayersPresent <= 1;
            foreach (var p in Players)
            {
                if (!p.Present) continue;
                // client wave 15 (w15horde), the design's point 26: alone, a choice has no time limit; when a second player comes in (or the
                // other leaves) while it is open, the limit starts (or ends) and the offer is sent again with its Seconds
                if (p.Open != null && solo != (p.OpenDeadline == NoDeadline)) { SetDeadline(p, p.Open, solo); OffersOpened.Add(new KeyValuePair<int, HordeOffer>(p.Slot, p.Open)); }
                if (p.Open != null && WallTick >= p.OpenDeadline) { p.IdleChooser = true; _autoPickedInPause = true; ApplyCard(p, p.Open, p.Open.Cards[0]); }
                if (p.Open == null && p.Offers.Count > 0) OpenNext(p);
            }
        }

        /// <summary>Client wave 15 (point 26): an open choice's deadline when the player is alone: none (it waits for the player).</summary>
        public const int NoDeadline = int.MaxValue;

        /// <summary>The open offer's limit: none alone (Seconds 0: the client shows no time bar and never picks by itself), else the
        /// run's current limit (<see cref="CurrentChoiceSeconds"/>, from now).</summary>
        private void SetDeadline(SimPlayer p, HordeOffer o, bool solo)
        {
            if (solo) { o.Seconds = 0f; p.OpenDeadline = NoDeadline; }
            else { o.Seconds = CurrentChoiceSeconds; p.OpenDeadline = WallTick + Math.Max(1, Ticks(CurrentChoiceSeconds)); }
        }

        // =============================================================================================== waves

        private int PlayersForScaling() { int n = PlayersPresent; return n < 1 ? 1 : n > 8 ? 8 : n; }

        private void StepWaves()
        {
            int cycleLen = Math.Max(1, RunTicks);
            int nextMilestone = (_waveCycle + 1) * cycleLen;
            // the announcements (client wave 9, h9content): the milestone boss and Once waves say they come, WarnSeconds before
            if (!_finalWarned && !FinalPhase && R.FinalBossWarnSeconds > 0 && Tick >= nextMilestone - Ticks(R.FinalBossWarnSeconds) && C.Enemy(R.FinalBossEnemyId) != null)
            {
                _finalWarned = true;
                AddEvent(HordeEventKind.Incoming, HordeEventFlags.Boss | HordeEventFlags.Final, -1, -1, R.FinalBossEnemyId, Math.Max(1, (int)Math.Round((nextMilestone - Tick) / (double)TickRate)), (MinX + MaxX) / 2, (MinY + MaxY) / 2);
            }
            if (Tick >= nextMilestone)
            {
                _waveCycle = Tick / cycleLen;
                _finalWarned = false;
                FinalPhase = true;
                AddEvent(HordeEventKind.FinalPhase, HordeEventFlags.Final, -1, -1, R.FinalBossEnemyId, (int)R.FinalBossSeconds, (MinX + MaxX) / 2, (MinY + MaxY) / 2);
                var fb = C.Enemy(R.FinalBossEnemyId);
                if (fb != null && (_finalBossSlot < 0 || !Enemies[_finalBossSlot].Alive))
                    _finalBossSlot = SpawnOne(fb, SpawnPattern.Edge, true);
                int cycleOffset = _waveCycle * cycleLen;
                foreach (var w in _waves)
                {
                    bool endgameWave = !w.Def.Once && R.ReferenceRunSeconds > 0 && w.Def.To >= R.ReferenceRunSeconds - 10f;
                    w.From = endgameWave ? Tick : Ticks(w.Def.From * TimeScale) + cycleOffset;
                    w.To = w.Def.Once || endgameWave ? int.MaxValue : Ticks(w.Def.To * TimeScale) + cycleOffset;
                    w.Next = w.From;
                    w.Done = false;
                    w.Warned = false;
                }
            }
            if (FinalPhase && (_finalBossSlot < 0 || !Enemies[_finalBossSlot].Alive || Tick >= _waveCycle * cycleLen + FinalTicks))
                FinalPhase = false;
            if (_waveCycle > 0 && Tick > RunTicks && (Tick - RunTicks) % (90 * TickRate) == 0)
            {
                var fb = C.Enemy(R.FinalBossEnemyId);
                if (fb != null) SpawnOne(fb, SpawnPattern.Edge, true);
            }
            int n = PlayersForScaling();
            double minutes = Tick / (double)TickRate / 60.0 / (TimeScale > 0 ? TimeScale : 1.0);
            double endlessSpawnMul = minutes > 20.0 ? Math.Min(4.0, Math.Pow(1.12, minutes - 20.0)) : 1.0;
            foreach (var w in _waves)
            {
                if (w.Done) continue;
                if (w.Def.Once && !w.Warned && w.Def.WarnSeconds > 0 && Tick >= w.Next - Ticks(w.Def.WarnSeconds))
                {
                    w.Warned = true;
                    var wd = C.Enemy(w.EnemyId);
                    if (wd != null) AddEvent(HordeEventKind.Incoming, RankFlags(wd.Rank), -1, -1, wd.Id, Math.Max(1, (int)Math.Round((w.Next - Tick) / (double)TickRate)), (MinX + MaxX) / 2, (MinY + MaxY) / 2);
                }
                if (Tick < w.Next) continue;
                var def = C.Enemy(w.EnemyId);
                if (def == null) { w.Done = true; continue; }
                if (w.Def.Once)
                {
                    w.Done = true;
                    _spawnHpScale = w.HpScale; _spawnDmgScale = w.DmgScale; // (a shuffled boss: its mark's strength; 1 otherwise)
                    SpawnGroup(def, Math.Max(1, w.Def.Count), w.Def.Pattern, true); // exactly Count (client wave 9; wave 8 spawned one)
                    _spawnHpScale = _spawnDmgScale = 1;
                    continue;
                }
                if (Tick >= w.To) { w.Done = true; continue; }
                w.Next = Tick + w.Every;
                SimPlayer around = SpawnAround(out double close); // client wave 18 (h18rules), point 31: the player the group comes to, x its allies near it (HordeSim.Rules.cs)
                double want = w.Def.Count * Arr(C.Scaling.SpawnMul, n) * close * SoulSpawnMul() * endlessSpawnMul + w.Carry; // (client wave 18, h18rules: x close; h18feel, point 35: x1.2 while soul shards lie on the field; the cap is SpawnGroup's)
                int count = (int)want; w.Carry = want - count;
                if (w.Def.MaxAlive > 0)
                {
                    int alive = 0; foreach (var e in Enemies) if (e.Alive && e.Def == def) alive++;
                    count = Math.Min(count, Math.Max(0, w.Def.MaxAlive - alive));
                }
                _spawnExpScale = 1 / close; // client wave 18 (h18rules), point 31: the closeness's extra enemies bring no extra EXP (HordeSim.Rules.cs)
                SpawnGroup(def, count, w.Def.Pattern, false, around); // (client wave 18, h18rules: around that player)
                _spawnExpScale = 1; // (client wave 18, h18rules)
            }
        }

        private static double Arr(float[] a, int players) => a == null || a.Length == 0 ? 1.0 : a[Math.Min(players, a.Length) - 1];

        private SimPlayer RandomStanding()
        {
            int n = 0; foreach (var p in Players) if (p.Present && !p.Fallen) n++;
            if (n == 0) { foreach (var p in Players) if (p.Present) return p; return null; }
            int k = _rng.Next(n);
            foreach (var p in Players) if (p.Present && !p.Fallen && k-- == 0) return p;
            return null;
        }

        private void SpawnGroup(EnemyDef def, int count, SpawnPattern pattern, bool force, SimPlayer around = null) // (client wave 18, h18rules: + around)
        {
            if (count <= 0) return;
            if (around == null) around = RandomStanding(); // (client wave 18, h18rules: unless StepWaves chose it)
            if (around == null) return;
            double baseAng = _rng.NextDouble() * Math.PI * 2;
            for (int i = 0; i < count; i++)
            {
                if (!force && EnemiesAlive >= Math.Min(R.MaxEnemies, HordeLimits.MaxEnemies)) return;
                int x, y;
                double dist = 11.0 + _rng.NextDouble() * 3.0; // just out of a table's usual sight (cells)
                switch (pattern)
                {
                    case SpawnPattern.Cluster:
                    {
                        double a = baseAng + (_rng.NextDouble() - 0.5) * 0.6;
                        x = around.X + Units(Math.Cos(a) * dist); y = around.Y + Units(Math.Sin(a) * dist);
                        break;
                    }
                    case SpawnPattern.Circle:
                    {
                        double a = baseAng + i * Math.PI * 2 / count;
                        x = around.X + Units(Math.Cos(a) * 9.0); y = around.Y + Units(Math.Sin(a) * 9.0);
                        break;
                    }
                    case SpawnPattern.Edge:
                    {
                        int side = _rng.Next(4); double t = _rng.NextDouble();
                        x = side == 0 ? MinX : side == 1 ? MaxX : MinX + (int)((MaxX - MinX) * t);
                        y = side == 2 ? MinY : side == 3 ? MaxY : MinY + (int)((MaxY - MinY) * t);
                        break;
                    }
                    default:
                    {
                        double a = _rng.NextDouble() * Math.PI * 2;
                        x = around.X + Units(Math.Cos(a) * dist); y = around.Y + Units(Math.Sin(a) * dist);
                        break;
                    }
                }
                Spawn(def, Clamp(x, MinX, MaxX), Clamp(y, MinY, MaxY), force);
            }
        }

        private int SpawnOne(EnemyDef def, SpawnPattern pattern, bool force)
        {
            int before = -1;
            var around = RandomStanding();
            int x = (MinX + MaxX) / 2, y = MaxY;
            if (around != null)
            {
                double a = _rng.NextDouble() * Math.PI * 2;
                x = Clamp(around.X + Units(Math.Cos(a) * 10), MinX, MaxX); y = Clamp(around.Y + Units(Math.Sin(a) * 10), MinY, MaxY);
            }
            before = Spawn(def, x, y, force);
            return before;
        }

        /// <summary>An enemy at (x, y) units. Returns its slot or -1 (no slot free).</summary>
        public int Spawn(EnemyDef def, int x, int y, bool force)
        {
            int slot = -1;
            for (int i = 0; i < Enemies.Length; i++) if (!Enemies[i].Alive) { slot = i; break; }
            if (slot < 0) return -1;
            int n = PlayersForScaling();
            double minutes = Tick / (double)TickRate / 60.0 / (TimeScale > 0 ? TimeScale : 1.0);
            double hpMul = def.Rank == EnemyRank.Normal ? Arr(C.Scaling.EnemyHpMul, n) * (1 + C.Scaling.EnemyHpPctPerMinute / 100.0 * minutes)
                         : def.Rank == EnemyRank.Elite ? Arr(C.Scaling.EliteHpMul, n) : Arr(C.Scaling.BossHpMul, n);
            double dmgMul = Arr(C.Scaling.EnemyDamageMul, n) * (def.Rank == EnemyRank.Normal ? 1 + C.Scaling.EnemyDamagePctPerMinute / 100.0 * minutes : 1);
            // Item 56 (2026-10-12): exponential difficulty scaling after 20 minutes so the horde inevitably overwhelms the party
            if (minutes > 20.0)
            {
                double expScale = Math.Pow(1.30, minutes - 20.0);
                if (def.Rank != EnemyRank.Normal)
                {
                    hpMul *= 1 + C.Scaling.EnemyHpPctPerMinute / 100.0 * (minutes - 20.0);
                    dmgMul *= 1 + C.Scaling.EnemyDamagePctPerMinute / 100.0 * (minutes - 20.0);
                }
                hpMul *= expScale;
                dmgMul *= expScale;
            }
            var e = Enemies[slot];
            e.Reset();
            _chargeAbWarned[slot] = _chargeAbVx[slot] = _chargeAbVy[slot] = 0;
            e.Alive = true; e.Serial = (e.Serial + 1) & 0xff; e.Def = def; e.X = x; e.Y = y;
            hpMul *= _spawnHpScale; dmgMul *= _spawnDmgScale; e.HpScale = _spawnHpScale; e.DmgScale = _spawnDmgScale; // (point 21; 1 = as the tables say)
            e.ExpScale = _spawnExpScale; // client wave 18 (h18rules), point 31
            e.MaxHp = Math.Max(1, (int)Math.Min(int.MaxValue / 2, Math.Round(def.Hp * hpMul))); e.Hp = e.MaxHp;
            e.Damage = Math.Max(0, (int)Math.Min(int.MaxValue / 2, Math.Round(def.Damage * dmgMul)));
            e.ShotDamage = Math.Max(0, (int)Math.Min(int.MaxValue / 2, Math.Round(def.ShotDamage * dmgMul)));
            e.Speed = Math.Max(1, Units(def.Speed / TickRate));
            e.Radius = Math.Max(16, Units(def.Radius));
            e.SpawnTick = Tick; e.NextAttack = Tick;
            e.ChargeNext = Tick + Ticks(def.ChargeCooldown);
            e.ShotNext = Tick + Ticks(def.AttackInterval);
            e.AbilityNext = new int[def.Abilities.Length];
            for (int i = 0; i < def.Abilities.Length; i++) e.AbilityNext[i] = Tick + Ticks(def.Abilities[i].FirstAfter);
            if (def.Behavior == EnemyBehavior.Sweep)
            {
                var t = RandomStanding();
                double a = t != null ? Math.Atan2(t.Y - y, t.X - x) : 0;
                e.DashVx = (int)(Math.Cos(a) * e.Speed); e.DashVy = (int)(Math.Sin(a) * e.Speed);
                int previewTicks = Math.Max(1, Ticks(0.6));
                WindUpEvent(e, HordeWindUpKind.Charge, t != null ? t.Slot : -1, Clamp(x + e.DashVx * Ticks(1.2), MinX, MaxX), Clamp(y + e.DashVy * Ticks(1.2), MinY, MaxY), previewTicks);
            }
            EnemiesAlive++;
            if (def.Rank != EnemyRank.Normal)
                AddEvent(HordeEventKind.BossAppeared, RankFlags(def.Rank), -1, slot, def.Id, 0, x, y);
            return slot;
        }

        private static HordeEventFlags RankFlags(EnemyRank r) =>
            r == EnemyRank.Elite ? HordeEventFlags.Elite : r == EnemyRank.Boss ? HordeEventFlags.Boss : r == EnemyRank.FinalBoss ? HordeEventFlags.Boss | HordeEventFlags.Final : HordeEventFlags.None;

        // =============================================================================================== enemies

        private SimPlayer NearestStanding(int x, int y, out long d2)
        {
            SimPlayer best = null; d2 = long.MaxValue;
            foreach (var p in Players)
            {
                if (!p.Present || p.Fallen) continue;
                if (PassiveHidden(p)) continue; // client wave 17 (h17pass): Cloaking (enemies lose a hidden runner)
                if (SlideHidden(p)) continue; // client wave 18 (h18rules), point 27: after a Back Slide, as Cloaking
                long dx = p.X - x, dy = p.Y - y, d = dx * dx + dy * dy;
                if (d < d2) { d2 = d; best = p; }
            }
            return best;
        }

        private void StepEnemies()
        {
            var neigh = new List<int>(32);
            int body = Units(R.BodyRadius);
            foreach (var e in Enemies)
            {
                if (!e.Alive) continue;
                var def = e.Def;
                bool held = e.FrozenUntil > Tick || e.StunUntil > Tick;
                var target = NearestStanding(e.X, e.Y, out long d2);
                int speed = e.SlowUntil > Tick ? Math.Max(1, e.Speed * (100 - e.SlowPct) / 100) : e.Speed;
                e.Flags = HordeEnemyFlags.None;
                if (e.SlowUntil > Tick) e.Flags |= HordeEnemyFlags.Slowed;
                if (e.FrozenUntil > Tick) e.Flags |= HordeEnemyFlags.Frozen;
                if (e.StunUntil > Tick) e.Flags |= HordeEnemyFlags.Stunned;
                if (e.DotUntil > Tick) e.Flags |= HordeEnemyFlags.Hurting;
                CardsEnemy(e, ref target, ref d2); // client wave 17 (h17cards): the Marked flag, a taunted enemy goes for its marker
                // a knockback is an impulse that dies down over ticks (it moves a held enemy too)
                if (e.KbVx != 0 || e.KbVy != 0)
                {
                    e.Flags |= HordeEnemyFlags.Knocked;
                    e.X = Clamp(e.X + e.KbVx, MinX, MaxX); e.Y = Clamp(e.Y + e.KbVy, MinY, MaxY);
                    HordeMove.Decay(ref e.KbVx, ref e.KbVy, _kbKeepPct);
                }
                if (held) { CancelAim(e); continue; } // (client wave 17, h17read: a frozen / stunned shooter loses its aim)
                int bossMove = BossMove(e); // client wave 17 (h17boss): 1 = it stands (casting), 2 = it moved itself (a dash): HordeSim.Bosses.cs
                if (bossMove == 2) continue;
                int vx = 0, vy = 0;
                if (bossMove == 0) // (client wave 17, h17boss)
                switch (def.Behavior)
                {
                    case EnemyBehavior.Sweep:
                        vx = e.DashVx; vy = e.DashVy;
                        if (e.X + vx <= MinX || e.X + vx >= MaxX || e.Y + vy <= MinY || e.Y + vy >= MaxY) { KillQuiet(e); continue; }
                        break;
                    case EnemyBehavior.Still:
                        break;
                    case EnemyBehavior.Charge:
                        if (e.DashUntil > Tick) { vx = e.DashVx; vy = e.DashVy; e.Flags |= HordeEnemyFlags.Charging; }
                        else if (e.WindUntil > Tick) { e.Flags |= HordeEnemyFlags.Winding; }
                        else if (e.WindUntil != 0 && e.WindUntil == Tick)
                        {
                            if (TakeLockedDash(e)) e.DashUntil = Tick + Ticks(1.0); // client wave 17 (h17read): the dash goes where its wind-up's arrow pointed (HordeSim.Readability.cs)
                            else if (target != null)
                            {
                                double a = Math.Atan2(target.Y - e.Y, target.X - e.X); int cs = Units(def.ChargeSpeed / TickRate);
                                e.DashVx = (int)(Math.Cos(a) * cs); e.DashVy = (int)(Math.Sin(a) * cs);
                                e.DashUntil = Tick + Ticks(1.0);
                            }
                            if (e.DashUntil > Tick) { vx = e.DashVx; vy = e.DashVy; e.Flags |= HordeEnemyFlags.Charging; }
                            e.WindUntil = 0;
                        }
                        else if (Tick >= e.ChargeNext && target != null && d2 < (long)Units(8) * Units(8))
                        {
                            e.WindUntil = Tick + Math.Max(1, Ticks(def.ChargeWindup)); e.ChargeNext = Tick + Ticks(def.ChargeCooldown);
                            e.Flags |= HordeEnemyFlags.Winding;
                            LockDash(e, target); // client wave 17 (h17read): the direction is kept from now and told (the WindUp event: C3's arrow)
                        }
                        else Toward(e, target, speed, out vx, out vy);
                        break;
                    case EnemyBehavior.Ranged:
                        // client wave 17 (h17read): a shot is aimed 0.3 s first (the Winding flag, a WindUp event: C4's line); it stands meanwhile
                        if (e.ShotDamage > 0 && AimShot(e, target, target != null && def.ShotRange > 0 && d2 <= (long)Units(def.ShotRange) * Units(def.ShotRange))) break;
                        if (target != null && def.ShotRange > 0 && d2 <= (long)Units(def.ShotRange) * Units(def.ShotRange))
                        {
                            // it keeps its distance (client wave 9, h9content): away from a player nearer than KeepRange
                            if (def.KeepRange > 0 && d2 < (long)Units(def.KeepRange) * Units(def.KeepRange)) { Toward(e, target, speed, out vx, out vy); vx = -vx; vy = -vy; }
                        }
                        else Toward(e, target, speed, out vx, out vy);
                        break;
                    default:
                        if (e.DashUntil > Tick) { vx = e.DashVx; vy = e.DashVy; e.Flags |= HordeEnemyFlags.Charging; }
                        else Toward(e, target, speed, out vx, out vy);
                        break;
                }
                if (def.Behavior == EnemyBehavior.Still && e.ShotDamage > 0) // client wave 17 (h17read): aimed 0.3 s first, as Ranged
                    AimShot(e, target, target != null && (def.ShotRange <= 0 || d2 <= (long)Units(def.ShotRange) * Units(def.ShotRange)));
                // keep apart from neighbours and from the players' bodies (soft separation: a crowd parts around a player,
                // who is never pushed; the player's own walk is the only thing that moves it, so its prediction stays exact).
                // Bosses are not pushed by anyone; sweeping packs fly straight through.
                if (def.Behavior != EnemyBehavior.Sweep && def.Rank < EnemyRank.Boss)
                {
                    neigh.Clear(); _grid.Query(e.X, e.Y, e.Radius * 2, Enemies, neigh);
                    int pushX = 0, pushY = 0, checkedN = 0;
                    foreach (int j in neigh)
                    {
                        if (j == e.Slot) continue;
                        var o = Enemies[j];
                        int minD = (e.Radius + o.Radius) * 3 / 4;
                        long dx = e.X - o.X, dy = e.Y - o.Y, dd = dx * dx + dy * dy;
                        if (dd >= (long)minD * minD) continue;
                        if (dd == 0) { dx = (e.Slot & 1) == 0 ? 1 : -1; dy = (e.Slot & 2) == 0 ? 1 : -1; dd = 2; }
                        int dist = HordeMove.ISqrt(dd);
                        int overlap = minD - dist;
                        pushX += (int)(dx * overlap / Math.Max(1, dist) / 2); pushY += (int)(dy * overlap / Math.Max(1, dist) / 2);
                        if (++checkedN >= 8) break;
                    }
                    foreach (var q in Players)
                    {
                        if (!q.Present || q.Fallen) continue;
                        int minD = (e.Radius + body) * 3 / 5; // still within touching reach (e.Radius + body): a crowd can hurt, not stack
                        long dx = e.X - q.X, dy = e.Y - q.Y, dd = dx * dx + dy * dy;
                        if (dd >= (long)minD * minD) continue;
                        if (dd == 0) { dx = (e.Slot & 1) == 0 ? 1 : -1; dy = (e.Slot & 2) == 0 ? 1 : -1; dd = 2; }
                        int dist = HordeMove.ISqrt(dd);
                        int overlap = minD - dist;
                        pushX += (int)(dx * overlap / Math.Max(1, dist)); pushY += (int)(dy * overlap / Math.Max(1, dist));
                    }
                    int maxPush = Math.Max(speed, 8);
                    vx += Clamp(pushX, -maxPush, maxPush); vy += Clamp(pushY, -maxPush, maxPush);
                }
                int nx = Clamp(e.X + vx, MinX, MaxX), ny = Clamp(e.Y + vy, MinY, MaxY);
                if (def.Rank < EnemyRank.Boss) BlockByZones(e, ref nx, ref ny);
                e.X = nx; e.Y = ny;
                // abilities: pre-telegraph AbilityKind.Charge before it fires (Item 55: telegraph all dashes without adding delay)
                for (int i = 0; i < def.Abilities.Length; i++)
                {
                    var ab = def.Abilities[i];
                    if (ab.Kind != AbilityKind.Charge) continue;
                    int windTicks = Math.Max(1, Ticks(def.ChargeWindup > 0 ? def.ChargeWindup : 0.6f));
                    if (Tick < e.AbilityNext[i] && Tick >= e.AbilityNext[i] - windTicks && target != null)
                    {
                        e.Flags |= HordeEnemyFlags.Winding;
                        if (_chargeAbWarned[e.Slot] != e.AbilityNext[i])
                        {
                            _chargeAbWarned[e.Slot] = e.AbilityNext[i];
                            double a = Math.Atan2(target.Y - e.Y, target.X - e.X);
                            int cs = Math.Max(1, Units(ab.Speed / TickRate));
                            int dvx = (int)(Math.Cos(a) * cs), dvy = (int)(Math.Sin(a) * cs);
                            _chargeAbVx[e.Slot] = dvx; _chargeAbVy[e.Slot] = dvy;
                            int dashTicks = Math.Max(1, (int)(Units(ab.Range > 0 ? ab.Range : 6) / cs));
                            int ex = Clamp(e.X + dvx * dashTicks, MinX, MaxX), ey = Clamp(e.Y + dvy * dashTicks, MinY, MaxY);
                            WindUpEvent(e, HordeWindUpKind.Charge, target.Slot, ex, ey, e.AbilityNext[i] - Tick);
                        }
                    }
                }
                for (int i = 0; i < def.Abilities.Length; i++)
                    if (Tick >= e.AbilityNext[i] && BossTakeTurn(e, i)) { e.AbilityNext[i] = Tick + Math.Max(1, Ticks(def.Abilities[i].Cooldown * BossCooldownMul(e, def.Abilities[i]))); UseAbility(e, def.Abilities[i]); if (!e.Alive) break; } // client wave 17 (h17boss): one warning at a time, the second phase's cooldowns
                if (!e.Alive) continue;
                // touching players
                if (e.Damage > 0 || def.Behavior == EnemyBehavior.Exploder)
                {
                    foreach (var p in Players)
                    {
                        if (!p.Present || p.Fallen) continue;
                        int reach = e.Radius + Units(R.BodyRadius);
                        long dx = p.X - e.X, dy = p.Y - e.Y;
                        if (dx * dx + dy * dy > (long)reach * reach) continue;
                        if (def.Behavior == EnemyBehavior.Exploder)
                        {
                            int area = Units(def.BurstArea);
                            AddEvent(HordeEventKind.EnemyBurst, HordeEventFlags.None, -1, e.Slot, def.Id, 0, e.X, e.Y, area / (float)U);
                            foreach (var q in Players)
                                if (q.Present && !q.Fallen && Dist2(q.X, q.Y, e.X, e.Y) <= (long)area * area) HurtPlayer(q, e.Damage, def.Id, e.X, e.Y, e.Slot, HordeHitVia.Burst); // (client wave 17, h17read: + who, how; h17cards reads them too)
                            KillQuiet(e);
                            break;
                        }
                        if (Tick >= e.NextAttack) { e.NextAttack = Tick + Math.Max(1, Ticks(def.AttackInterval)); HurtPlayer(p, e.Damage, def.Id, e.X, e.Y, e.Slot, HordeHitVia.Touch); } // (client wave 17, h17read: + who, how; h17cards reads them too)
                        break;
                    }
                }
            }
        }

        private static void Toward(SimEnemy e, SimPlayer t, int speed, out int vx, out int vy)
        {
            vx = vy = 0;
            if (t == null) return;
            long dx = t.X - e.X, dy = t.Y - e.Y;
            long d = HordeMove.ISqrt(dx * dx + dy * dy);
            if (d < 4) return;
            vx = (int)(dx * speed / d); vy = (int)(dy * speed / d);
        }

        internal static void WallClosestPoint(SimFx f, int px, int py, out int cx, out int cy)
        {
            if ((f.ZoneFlags & ZoneFlags.BlocksEnemies) == 0 || f.Range <= 0)
            {
                cx = f.X; cy = f.Y; return;
            }
            double rad = f.Angle * Math.PI / 180.0;
            double ux = Math.Cos(rad), uy = Math.Sin(rad);
            double proj = (px - f.X) * ux + (py - f.Y) * uy;
            if (proj < -f.Range) proj = -f.Range;
            else if (proj > f.Range) proj = f.Range;
            cx = f.X + (int)Math.Round(ux * proj);
            cy = f.Y + (int)Math.Round(uy * proj);
        }

        private bool CanKnockback(SimEnemy e)
        {
            if (e == null || !e.Alive) return false;
            int win = Ticks(1.8), gap = Ticks(0.55);
            if (e.KbCount == 0 || Tick - e.KbWindowTick >= win)
            {
                e.KbWindowTick = Tick;
                e.KbCount = 1;
                return true;
            }
            if (e.KbCount >= 2 || Tick - e.KbWindowTick < gap) return false;
            e.KbCount++;
            return true;
        }

        private void BlockByZones(SimEnemy e, ref int nx, ref int ny)
        {
            foreach (var f in Fx)
            {
                if (f.Kind != HordeFxKind.Zone || (f.ZoneFlags & ZoneFlags.BlocksEnemies) == 0 || f.EndTick <= Tick) continue;
                WallClosestPoint(f, nx, ny, out int cx, out int cy);
                int r = f.Radius + e.Radius;
                long dx = nx - cx, dy = ny - cy, dd = dx * dx + dy * dy;
                if (dd >= (long)r * r) continue;
                int d = Math.Max(1, HordeMove.ISqrt(dd));
                nx = Clamp(cx + (int)(dx * r / d), MinX, MaxX); ny = Clamp(cy + (int)(dy * r / d), MinY, MaxY);
                if (f.BaseDamage > 0 && f.OwnerSlot >= 0 && f.OwnerSlot < Players.Length && e.KbVx == 0 && e.KbVy == 0 && Tick >= e.ZoneHitUntil)
                {
                    var p = Players[f.OwnerSlot];
                    if (p.Present && f.Weapon != null && f.Lv != null)
                    {
                        e.X = nx; e.Y = ny;
                        e.ZoneHitUntil = Tick + Ticks(0.55);
                        if (HitEnemy(p, f.Weapon, f.Lv, e, f.BaseDamage, cx, cy)) return;
                    }
                }
            }
        }

        private void EnemyShoot(SimEnemy e, int tx, int ty)
        {
            var def = e.Def;
            double a = Math.Atan2(ty - e.Y, tx - e.X);
            int sp = Math.Max(1, Units(def.ShotSpeed / TickRate));
            int range = Units(def.ShotRange > 0 ? def.ShotRange * 1.5 : 12);
            var f = NewFx(HordeFxKind.EnemyShot);
            f.OwnerEnemy = e.Slot; f.DefId = def.Id; f.X = e.X; f.Y = e.Y; f.VX = (int)(Math.Cos(a) * sp); f.VY = (int)(Math.Sin(a) * sp);
            f.Radius = Units(0.3); f.EndTick = Tick + Math.Max(1, range / sp); f.Damage = e.ShotDamage;
            BeginFx(f);
        }

        private void UseAbility(SimEnemy e, EnemyAbilityDef ab)
        {
            switch (ab.Kind)
            {
                case AbilityKind.SummonAdds:
                {
                    var add = C.Enemy(ab.EnemyId);
                    if (add == null) return;
                    _spawnHpScale = e.HpScale; _spawnDmgScale = e.DmgScale; // (a shuffled boss's adds take its mark's strength too: point 21)
                    for (int i = 0; i < ab.Count; i++)
                    {
                        double a = i * Math.PI * 2 / Math.Max(1, ab.Count);
                        Spawn(add, Clamp(e.X + Units(Math.Cos(a) * 2), MinX, MaxX), Clamp(e.Y + Units(Math.Sin(a) * 2), MinY, MaxY), true);
                    }
                    _spawnHpScale = _spawnDmgScale = 1;
                    AddEvent(HordeEventKind.EnemyAbility, HordeEventFlags.None, -1, e.Slot, ab.SkillId, (int)ab.Kind, e.X, e.Y, 2f);
                    break;
                }
                case AbilityKind.Nova:
                case AbilityKind.AreaAtPlayers:
                {
                    int targets = ab.Kind == AbilityKind.Nova ? 1 : Math.Max(1, ab.Count);
                    var standing = new List<SimPlayer>();
                    foreach (var p in Players) if (p.Present && !p.Fallen) standing.Add(p);
                    for (int i = 0; i < targets; i++)
                    {
                        int x = e.X, y = e.Y;
                        if (ab.Kind == AbilityKind.AreaAtPlayers) { if (standing.Count == 0) break; var p = standing[_rng.Next(standing.Count)]; x = p.X; y = p.Y; }
                        var f = NewFx(HordeFxKind.EnemyWarning);
                        f.OwnerEnemy = e.Slot; f.DefId = ab.SkillId; f.X = x; f.Y = y; f.Radius = Units(ab.Area);
                        f.EndTick = Tick + Math.Max(1, Ticks(ab.Delay)); f.Damage = (int)Math.Round(ab.Damage * Arr(C.Scaling.EnemyDamageMul, PlayersForScaling()) * e.DmgScale);
                        f.EnemyDefId = e.Def.Id;
                        BeginFx(f);
                    }
                    break;
                }
                case AbilityKind.Charge:
                {
                    int cs = Math.Max(1, Units(ab.Speed / TickRate));
                    int dashTicks = Math.Max(1, (int)(Units(ab.Range > 0 ? ab.Range : 6) / cs));
                    if (_chargeAbWarned[e.Slot] == Tick && (_chargeAbVx[e.Slot] != 0 || _chargeAbVy[e.Slot] != 0))
                    {
                        e.DashVx = _chargeAbVx[e.Slot]; e.DashVy = _chargeAbVy[e.Slot];
                    }
                    else
                    {
                        var t = NearestStanding(e.X, e.Y, out _);
                        if (t == null) return;
                        double a = Math.Atan2(t.Y - e.Y, t.X - e.X);
                        e.DashVx = (int)(Math.Cos(a) * cs); e.DashVy = (int)(Math.Sin(a) * cs);
                        int ex = Clamp(e.X + e.DashVx * dashTicks, MinX, MaxX), ey = Clamp(e.Y + e.DashVy * dashTicks, MinY, MaxY);
                        WindUpEvent(e, HordeWindUpKind.Charge, t.Slot, ex, ey, dashTicks);
                    }
                    _chargeAbWarned[e.Slot] = 0;
                    e.DashUntil = Tick + dashTicks;
                    e.Flags |= HordeEnemyFlags.Charging;
                    AddEvent(HordeEventKind.EnemyAbility, HordeEventFlags.None, -1, e.Slot, ab.SkillId, (int)ab.Kind, e.X, e.Y, 0f);
                    break;
                }
                case AbilityKind.Barrage:
                {
                    int n = Math.Max(1, ab.Count);
                    int sp = Math.Max(1, Units((ab.Speed > 0 ? ab.Speed : 5) / TickRate));
                    for (int i = 0; i < n; i++)
                    {
                        double a = i * Math.PI * 2 / n;
                        if (ab.Range > 0) { var t = NearestStanding(e.X, e.Y, out _); if (t != null) a = Math.Atan2(t.Y - e.Y, t.X - e.X) + (i - (n - 1) / 2.0) * 0.15; }
                        var f = NewFx(HordeFxKind.EnemyShot);
                        f.OwnerEnemy = e.Slot; f.DefId = e.Def.Id; f.X = e.X; f.Y = e.Y; f.VX = (int)(Math.Cos(a) * sp); f.VY = (int)(Math.Sin(a) * sp);
                        f.Radius = Units(0.35); f.EndTick = Tick + Math.Max(1, Units(14) / sp);
                        f.Damage = (int)Math.Round(ab.Damage * Arr(C.Scaling.EnemyDamageMul, PlayersForScaling()) * e.DmgScale);
                        BeginFx(f);
                    }
                    AddEvent(HordeEventKind.EnemyAbility, HordeEventFlags.None, -1, e.Slot, ab.SkillId, (int)ab.Kind, e.X, e.Y, 0f);
                    break;
                }
                default: UseBossAbility(e, ab); break; // client wave 17 (h17boss): Line, Cone, Ring, Pull, Mark, Grid, Teleport, Phase, Trail (HordeSim.Bosses.cs)
            }
        }

        private void StepEnemyEffects()
        {
            foreach (var e in Enemies)
            {
                if (!e.Alive || e.DotUntil <= Tick || Tick < e.DotNext) continue;
                e.DotNext = Tick + Math.Max(1, e.DotEvery);
                DamageEnemy(e, e.DotDamage, e.DotSlot, e.DotWeapon, false, true, 0, 0);
            }
        }

        /// <summary>An enemy leaves without dying for anyone (a sweep crossing out, an exploder that burst).</summary>
        private void KillQuiet(SimEnemy e)
        {
            if (!e.Alive) return;
            e.Alive = false; EnemiesAlive--;
            AddEvent(HordeEventKind.EnemyDied, HordeEventFlags.None, -1, e.Slot, e.Def.Id, 0, e.X, e.Y);
        }

        // =============================================================================================== damage

        /// <param name="x">The attacker's place (units): client wave 17 (h17read) sends it in the PlayerHit event (X2 / Y2).</param>
        /// <param name="fromEnemy">Client wave 17 (h17read): the attacking enemy's slot (-1 unknown) and how the hit came (HordeHitVia).</param>
        private void HurtPlayer(SimPlayer p, int dmg, int enemyDefId, int x, int y, int fromEnemy = -1, HordeHitVia via = HordeHitVia.Unknown)
        {
            if (p.Fallen || !p.Present || dmg <= 0) return;
            if (p.SafeUntil > Tick || p.Open != null && !p.IdleChooser || p.HurtUntil > Tick) return;
            p.HurtUntil = Tick + Math.Max(1, Ticks(R.HurtGraceSeconds));
            int raw = dmg; if (CardsBlock(p, ref dmg, enemyDefId, x, y, fromEnemy, via)) return; // client wave 17 (h17cards): a guard blocks it; the damage taken by the enemy's race / element
            if (p.S.Dodge > 0 && _rng.Chance(Math.Min(75, p.S.Dodge)))
            {
                AddEvent(HordeEventKind.PlayerHit, HordeEventFlags.Dodged, p.Slot, fromEnemy, enemyDefId, 0, p.X, p.Y, (float)via, 0, 0, x, y); // client wave 17 (h17read): + the attacker
                PassiveDodged(p, enemyDefId, x, y); // client wave 17 (h17pass)
                return;
            }
            double d = dmg * Math.Max(0.1, 1 + p.S.DamageTakenPct / 100.0) - p.S.Defence;
            int taken = Math.Max(1, (int)Math.Round(d));
            if (PassiveBlocks(p, dmg, ref taken, enemyDefId, fromEnemy, via)) return; // client wave 17 (h17pass): a shell, a coat, Blade Stop, Gangster's Paradise
            p.Hp -= taken; p.DamageTaken += taken;
            AddEvent(HordeEventKind.PlayerHit, HordeEventFlags.None, p.Slot, fromEnemy, enemyDefId, taken, p.X, p.Y, (float)via, 0, 0, x, y); // client wave 17 (h17read): + the attacker
            PassiveHurt(p, dmg, taken, enemyDefId, fromEnemy, via); // client wave 17 (h17pass): a broken dodge streak, counters, Hiding
            CardsHurt(p, raw, enemyDefId, x, y, via); // client wave 17 (h17cards): Back Slide, Plagiarism
            if (p.Hp <= 0 && YggSave(p)) return; // client wave 18 (h18feel), point 37: the Yggdrasil Berry saves its holder (no fall, no soul split)
            if (p.Hp <= 0)
            {
                p.Hp = 0; p.Fallen = true; p.Revive = 0; p.Falls++; p.Inputs.Clear(); p.Vx = p.Vy = 0; p.Moving = false;
                CloseOffers(p, keepQueued: true);
                foreach (var f in Fx) if (f.OwnerSlot == p.Slot && f.Kind == HordeFxKind.Orbit && f.EndTick > Tick) EndFx(f, false);
                AddEvent(HordeEventKind.Fell, HordeEventFlags.None, p.Slot, -1, enemyDefId, 0, p.X, p.Y);
                SoulsFell(p); // client wave 18 (h18feel), point 35: its soul splits into shards (HordeSim.Souls.cs)
            }
        }

        private double DamageMul(SimPlayer p, WeaponDef w, SimEnemy e)
        {
            var s = p.S;
            double pct = s.DamagePct + (w.Class == DamageClass.Melee ? s.MeleePct : w.Class == DamageClass.Ranged ? s.RangedPct : s.MagicPct);
            double m = Math.Max(0.1, 1 + pct / 100.0);
            var el = CardsElement(p, w); // client wave 17 (h17cards): a Neutral weapon may be endowed (Endow, the arrows, Aspersio)
            m *= 1 + s.ElementDamage[(int)el % 10] / 100.0;
            if (e != null)
            {
                m *= 1 + s.VsRace[(int)e.Def.Race % 10] / 100.0;
                m *= 1 + s.VsElement[(int)e.Def.Element % 10] / 100.0;
                if (e.Def.Rank != EnemyRank.Normal) m *= 1 + s.VsBoss / 100.0;
                if (C.ElementPct != null) m *= C.ElementPct[(int)el % 10, (int)e.Def.Element % 10] / 100.0;
            }
            m *= CardsDamageMul(p, w, e); // client wave 17 (h17cards): vs size, marks, magic vs bosses, melee while walking, per weapon
            return m;
        }

        /// <summary>A player's weapon hits an enemy: base damage through the player's modifiers, the element table, a critical
        /// roll; then the weapon's effects. Returns true when the enemy died.</summary>
        private bool HitEnemy(SimPlayer p, OwnedWeapon ow, WeaponLevel lv, SimEnemy e, double baseDmg, int fromX, int fromY)
        {
            if (!e.Alive) return false;
            var w = ow.Def;
            double d = baseDmg * DamageMul(p, w, e);
            bool crit = _rng.Chance(p.S.Crit);
            if (crit) d *= (R.CritDamagePct + p.S.CritDamage) / 100.0;
            d *= PassiveHitMul(p, ow, e, crit); // client wave 17 (h17pass): heavy hits, marks, charges (HordeSim.Passives.cs)
            int dmg = Math.Max(1, (int)Math.Round(d));
            bool died = DamageEnemy(e, dmg, p.Slot, w.Id, crit, false, fromX, fromY);
            CardsAfterHit(p, ow, lv, e, dmg, crit, died, fromX, fromY); // client wave 17 (h17cards): marks spent, the on-hit cards, Auto Spell
            if (died || !e.Alive) return true;
            ApplyEffects(p, ow, lv, e, fromX, fromY);
            return false;
        }

        private void ApplyEffects(SimPlayer p, OwnedWeapon ow, WeaponLevel lv, SimEnemy e, int fromX, int fromY)
        {
            if (lv.Effects == null) return;
            foreach (var fx in lv.Effects)
            {
                if (fx == null || !_rng.Chance(fx.Chance * (1 + p.S.EffectChance / 100.0))) continue;
                double resist = Math.Max(0, 1 - e.Def.EffectResist / 100.0);
                switch (fx.Kind)
                {
                    case EffectKind.Slow:
                        if (resist <= 0) break;
                        e.SlowUntil = Math.Max(e.SlowUntil, Tick + Ticks(fx.Duration * resist)); e.SlowPct = Clamp((int)fx.Power, 0, 90); break;
                    case EffectKind.Freeze:
                        if (resist <= 0) break;
                        e.FrozenUntil = Math.Max(e.FrozenUntil, Tick + Ticks(fx.Duration * resist)); break;
                    case EffectKind.Stun:
                        if (resist <= 0) break;
                        e.StunUntil = Math.Max(e.StunUntil, Tick + Ticks(fx.Duration * resist)); break;
                    case EffectKind.Dot:
                        e.DotUntil = Tick + Ticks(fx.Duration); e.DotEvery = Math.Max(1, Ticks(fx.Tick)); e.DotNext = Tick + e.DotEvery;
                        e.DotDamage = Math.Max(1, (int)Math.Round(fx.Power * DamageMul(p, ow.Def, e))); e.DotSlot = p.Slot; e.DotWeapon = ow.Def.Id; break;
                    case EffectKind.Knockback:
                    {
                        // an impulse away from the hit's source that dies down over the next ticks (HordeMove.Decay), travelling
                        // the knockback's distance in all; a second hit adds to one still going. Item 54 (2026-10-12): never push
                        // an enemy toward the attacking player even when a zone/pierce/trap origin sits behind the enemy.
                        // Item 16: per-enemy knockback rate cap so rapid hits cannot infinitely juggle an enemy.
                        double kb = fx.Power * (1 + p.S.KnockbackPct / 100.0) * Math.Max(0, 1 - e.Def.KnockbackResist / 100.0);
                        if (kb <= 0) break;
                        if (!CanKnockback(e)) continue;
                        long dx = e.X - fromX, dy = e.Y - fromY;
                        if (p != null)
                        {
                            long px = e.X - p.X, py = e.Y - p.Y;
                            if (dx * px + dy * py <= 0) { dx = px; dy = py; }
                        }
                        if (dx == 0 && dy == 0) dx = 1;
                        long d = Math.Max(1, HordeMove.ISqrt(dx * dx + dy * dy));
                        int v0 = HordeMove.ImpulseFor(Units(kb), _kbKeepPct);
                        int nvx = Clamp(e.KbVx + (int)(dx * v0 / d), -8 * U, 8 * U);
                        int nvy = Clamp(e.KbVy + (int)(dy * v0 / d), -8 * U, 8 * U);
                        if (p != null)
                        {
                            long px = e.X - p.X, py = e.Y - p.Y;
                            if ((long)nvx * px + (long)nvy * py < 0 && (px != 0 || py != 0))
                            {
                                long pd = Math.Max(1, HordeMove.ISqrt(px * px + py * py));
                                nvx = Clamp((int)(px * v0 / pd), -8 * U, 8 * U);
                                nvy = Clamp((int)(py * v0 / pd), -8 * U, 8 * U);
                            }
                        }
                        e.KbVx = nvx; e.KbVy = nvy;
                        break;
                    }
                }
                AddEvent(HordeEventKind.Effect, HordeEventFlags.None, p.Slot, e.Slot, ow.Def.Id, (int)fx.Kind, e.X, e.Y);
            }
        }

        /// <summary>Damage to an enemy; true when it died (EXP, drops, counters, the event).</summary>
        private bool DamageEnemy(SimEnemy e, int dmg, int slot, int weaponId, bool crit, bool dot, int fromX, int fromY)
        {
            if (!e.Alive || dmg <= 0) return false;
            e.Hp -= dmg;
            var p = slot >= 0 ? Players[slot] : null;
            if (p != null) p.DamageDealt += dmg;
            bool died = e.Hp <= 0;
            var flags = (crit ? HordeEventFlags.Crit : 0) | (died ? HordeEventFlags.Kill : 0) | (dot ? HordeEventFlags.Dot : 0);
            Hits.Add(new HordeEvent { Kind = HordeEventKind.Hit, Flags = flags, Tick = Tick, Slot = slot, Target = e.Slot, DefId = weaponId, Value = dmg, X = Cells(e.X), Y = Cells(e.Y) });
            if (died) EnemyDied(e, p);
            return died;
        }

        private void EnemyDied(SimEnemy e, SimPlayer killer)
        {
            e.Alive = false; EnemiesAlive--;
            var def = e.Def;
            AddEvent(HordeEventKind.EnemyDied, RankFlags(def.Rank), killer != null ? killer.Slot : -1, e.Slot, def.Id, 0, e.X, e.Y);
            if (killer != null)
            {
                killer.Kills++;
                if (def.Rank == EnemyRank.Elite) killer.EliteKills++;
                else if (def.Rank >= EnemyRank.Boss) killer.BossKills++;
                if (killer.S.HpOnKill > 0 && !killer.Fallen)
                {
                    killer.KillHealCarry += killer.S.HpOnKill;
                    if (killer.KillHealCarry >= 1) { int h = (int)killer.KillHealCarry; killer.KillHealCarry -= h; killer.Hp = Math.Min(killer.MaxHp, killer.Hp + h); }
                }
                PassiveKill(killer, e); // client wave 17 (h17pass): bane motes, kill heals, kill drops
            }
            int exp = ExpOf(e); // client wave 18 (h18rules), point 31: a closeness group's share (HordeSim.Rules.cs; def.Exp otherwise)
            if (exp > 0) DropExp(e.X, e.Y, exp); // (client wave 18, h18rules: was def.Exp)
            foreach (var d in def.Drops)
            {
                if (d == null || !_rng.Chance(d.Chance)) continue;
                var pd = C.Pickup(d.PickupId);
                if (pd == null) continue;
                if (d.ScatterCells > 0)
                {
                    // off the path (client wave 9, h9content): ScatterCells away in a random direction, inside the arena
                    double a = _rng.NextDouble() * Math.PI * 2;
                    AddPickup(pd, e.X + Units(Math.Cos(a) * d.ScatterCells), e.Y + Units(Math.Sin(a) * d.ScatterCells), pd.Value);
                }
                else AddPickup(pd, e.X + Units(_rng.NextDouble() - 0.5), e.Y + Units(_rng.NextDouble() - 0.5), pd.Value);
            }
            CardsOnKill(e, killer); // client wave 17 (h17cards): Ore Discovery, Item Appraisal
            if (e.Slot == _finalBossSlot && def.Rank == EnemyRank.FinalBoss)
            {
                _finalBossSlot = -1;
                FinalPhase = false;
                foreach (var w in _waves)
                {
                    if (!w.Def.Once && R.ReferenceRunSeconds > 0 && w.Def.To >= R.ReferenceRunSeconds - 10f)
                    {
                        w.Done = false;
                        w.To = int.MaxValue;
                        if (w.Next < Tick) w.Next = Tick + 1;
                    }
                }
            } // Item 56 / Item 15: endless mode continues past the milestone boss
        }

        // =============================================================================================== pickups

        private void DropExp(int x, int y, int value)
        {
            PickupDef best = null;
            foreach (var pd in C.Pickups)
                if (pd.Kind == PickupKind.Exp && pd.Value <= value && (best == null || pd.Value > best.Value)) best = pd;
            if (best == null) foreach (var pd in C.Pickups) if (pd.Kind == PickupKind.Exp && (best == null || pd.Value < best.Value)) best = pd;
            if (best == null) { GiveExpAll(null, value); return; }
            if (Pickups.Count >= HordeLimits.MaxPickups)
            {
                // full: the value goes into the oldest EXP pickup (nothing is lost)
                foreach (var q in Pickups) if (q.Def.Kind == PickupKind.Exp) { q.Value += value; return; }
                return;
            }
            AddPickup(best, x, y, value);
        }

        private void AddPickup(PickupDef def, int x, int y, int value)
        {
            if (Pickups.Count >= HordeLimits.MaxPickups) return;
            var k = new SimPickup { Id = _nextPickupId, Def = def, X = Clamp(x, MinX, MaxX), Y = Clamp(y, MinY, MaxY), Value = value };
            _nextPickupId = _nextPickupId >= 65535 ? 1 : _nextPickupId + 1;
            Pickups.Add(k); PickupsAdded.Add(k.Id);
        }

        /// <summary>A flying pickup's speed in units per tick after this many ticks of flight (RulesDef.PickupPullSpeed +
        /// PickupPullAccel × seconds: an ease-in; the client draws the same flight from the pickup's place at PullTick).</summary>
        public int PullSpeed(int ticksFlying) => Math.Max(1, Units((R.PickupPullSpeed + R.PickupPullAccel * Math.Max(0, ticksFlying) / (double)TickRate) / TickRate));

        private void StepPickups()
        {
            for (int i = Pickups.Count - 1; i >= 0; i--)
            {
                var k = Pickups[i];
                SimPlayer taker = null;
                if (k.PulledBy >= 0 && Players[k.PulledBy].Present && !Players[k.PulledBy].Fallen) taker = Players[k.PulledBy];
                else
                {
                    k.PulledBy = -1;
                    long best = long.MaxValue;
                    foreach (var p in Players)
                    {
                        if (!p.Present || p.Fallen) continue;
                        int r = Units((R.PickupRadius + R.PickupRadiusPerLevel * Math.Max(0, p.Level - 1)) * (1 + p.S.PickupRadiusPct / 100.0));
                        long d = Dist2(p.X, p.Y, k.X, k.Y);
                        if (d <= (long)r * r && d < best) { best = d; taker = p; }
                    }
                    if (taker != null) { k.PulledBy = taker.Slot; k.PullTick = Tick; PickupsPulled.Add(k.Id); }
                }
                if (taker == null) continue;
                long dd = Dist2(taker.X, taker.Y, k.X, k.Y);
                int grab = Units(0.6);
                if (dd > (long)grab * grab)
                {
                    int d = HordeMove.ISqrt(dd), sp = PullSpeed(Tick - k.PullTick);
                    k.X += (int)((long)(taker.X - k.X) * Math.Min(sp, d) / d); k.Y += (int)((long)(taker.Y - k.Y) * Math.Min(sp, d) / d);
                    continue;
                }
                Pickups.RemoveAt(i); PickupsRemoved.Add(k.Id);
                TakePickup(taker, k);
            }
        }

        private void TakePickup(SimPlayer p, SimPickup k)
        {
            AddEvent(HordeEventKind.PickupTaken, HordeEventFlags.None, p.Slot, k.Id, k.Def.Id, k.Value, k.X, k.Y);
            PassivePickup(p, k); // client wave 17 (h17pass): an arrow bundle (PickupKind.Passive)
            CardsPickup(p, k); // client wave 17 (h17cards): Vending
            switch (k.Def.Kind)
            {
                case PickupKind.Exp: GiveExpAll(p, k.Value); break;
                case PickupKind.Heal:
                {
                    int h = Math.Max(1, p.MaxHp * k.Value / 100);
                    int before = p.Hp; p.Hp = Math.Min(p.MaxHp, p.Hp + h);
                    AddEvent(HordeEventKind.Heal, HordeEventFlags.None, p.Slot, -1, 0, p.Hp - before, p.X, p.Y);
                    break;
                }
                case PickupKind.Magnet:
                    foreach (var q in Pickups) if (q.Def.Kind == PickupKind.Exp && q.PulledBy < 0) { q.PulledBy = p.Slot; q.PullTick = Tick; PickupsPulled.Add(q.Id); }
                    break;
                case PickupKind.Chest:
                    CardsChest(p, k); // client wave 17 (h17cards): a big chest may evolve a weapon, Discount may add one (was: Value × ChestUpgrade)
                    break;
                case PickupKind.Bomb:
                {
                    int r = Units(k.Def.Area > 0 ? k.Def.Area : 8);
                    foreach (var e in Enemies) if (e.Alive && Dist2(e.X, e.Y, k.X, k.Y) <= (long)r * r) DamageEnemy(e, k.Value, p.Slot, 0, false, false, k.X, k.Y);
                    AddEvent(HordeEventKind.Strike, HordeEventFlags.None, p.Slot, -1, 0, k.Value, k.X, k.Y, r / (float)U);
                    break;
                }
            }
        }

        /// <summary>Run EXP into the party's pool (the taker's EXP bonus applies; RulesDef.ExpSharePct is not read since the pool is
        /// shared: 2026-10-05). A party level-up follows at once.</summary>
        private void GiveExpAll(SimPlayer taker, int value)
        {
            double scale = TimeScale > 0 ? 1.0 / TimeScale : 1.0;
            double bonus = taker != null ? 1 + taker.S.ExpGainPct / 100.0 : 1.0;
            _partyExpCarry += value * scale * bonus;
            int gain = (int)_partyExpCarry; _partyExpCarry -= gain;
            if (gain > 0) GainExp(gain);
        }

        /// <summary>The table's requirement from level n to n + 1, for one player.</summary>
        public int ExpToNext(int level)
        {
            var t = R.ExpTable;
            if (t == null || t.Length == 0) return 10 * level;
            if (level - 1 < t.Length) return Math.Max(1, t[Math.Max(0, level - 1)]);
            double v = t[t.Length - 1];
            for (int l = t.Length; l < level; l++) v *= 1 + R.ExpGrowthPct / 100.0;
            return (int)Math.Min(int.MaxValue / 2, Math.Round(v));
        }

        /// <summary>What the party needs from its level to the next: the table × RulesDef.PartyExpScale[players - 1] (the default
        /// multiplies by the player count, so a solo run paces as before).</summary>
        public int PartyExpToNext()
        {
            double mul = Arr(R.PartyExpScale, PlayersForScaling());
            return (int)Math.Max(1, Math.Min(int.MaxValue / 2, Math.Round(ExpToNext(PartyLevel) * Math.Max(0.01, mul))));
        }

        private void GainExp(int gain)
        {
            if (PartyLevel >= R.MaxLevel) return;
            PartyExp += gain;
            while (PartyLevel < R.MaxLevel && PartyExp >= PartyExpToNext())
            {
                PartyExp -= PartyExpToNext();
                PartyLevel++;
                SoulsJobMark(); // client wave 18 (h18feel), point 35: a class-change level brings every fallen player back (HordeSim.Souls.cs)
                foreach (var p in Players)
                {
                    if (!p.Present) continue;
                    p.Level = PartyLevel;
                    Recalc(p, false);
                    if (!p.Fallen) p.Hp = Math.Min(p.MaxHp, p.Hp + R.HpPerLevel);
                    AddEvent(HordeEventKind.LevelUp, HordeEventFlags.None, p.Slot, -1, 0, p.Level, p.X, p.Y);
                    QueueJobOffer(p);
                    QueueCardOffer(p);
                }
            }
        }

        // =============================================================================================== offers

        private void QueueJobOffer(SimPlayer p)
        {
            // the next tier whose mark is reached and not yet offered (one at a time: the next is queued when this one is taken)
            var job = C.Job(p.JobId);
            if (job == null || job.Next == null || job.Next.Length == 0) return;
            if (R.JobMarks == null || job.Tier >= R.JobMarks.Length || p.Level < R.JobMarks[job.Tier]) return;
            foreach (var o in p.Offers) if (o.IsJob) return;
            if (p.Open != null && p.Open.IsJob) return;
            var cards = new List<HordeCard>();
            foreach (int id in job.Next) if (C.Job(id) != null && (C.Job(id).Sex == 0 || p.Sex == 0 || C.Job(id).Sex == p.Sex)) cards.Add(new HordeCard { Kind = HordeCardKind.Job, DefId = id, Level = C.Job(id).Tier });
            if (cards.Count == 0) return;
            if (cards.Count == 1) { ChangeJob(p, C.Job(cards[0].DefId)); return; }
            p.Offers.Enqueue(new HordeOffer { Id = NextOfferId(), IsJob = true, Seconds = R.ChoiceSeconds, Cards = cards.ToArray() });
        }

        private int NextOfferId() { int id = _nextOfferId; _nextOfferId = _nextOfferId >= 65535 ? 1 : _nextOfferId + 1; return id; }

        private void QueueCardOffer(SimPlayer p)
        {
            // the cards are drawn when the offer opens (they reflect what the player has then)
            p.Offers.Enqueue(new HordeOffer { Id = NextOfferId(), IsJob = false, Seconds = R.ChoiceSeconds, Cards = null });
        }

        private void OpenNext(SimPlayer p)
        {
            while (p.Offers.Count > 0)
            {
                var o = p.Offers.Dequeue();
                if (!o.IsJob) o.Cards = DrawCards(p);
                if (o.Cards == null || o.Cards.Length == 0) continue;
                // the timeout (wall time: the world is paused meanwhile) is the run's current limit (design note 2026-10-05 01:25), but
                // ALONE there is none (design note 2026-10-08, client wave 15 point 26: "when I am playing alone, remove the timer"): Seconds 0
                p.Open = o; SetDeadline(p, o, PlayersPresent <= 1);
                OffersOpened.Add(new KeyValuePair<int, HordeOffer>(p.Slot, o));
                return;
            }
        }

        private void CloseOffers(SimPlayer p, bool keepQueued = false)
        {
            if (p.Open != null)
            {
                OffersClosed.Add(new KeyValuePair<int, int>(p.Slot, p.Open.Id));
                // a fallen player's open choice is put back: it is offered again when it rises
                if (keepQueued) { var rest = p.Offers.ToArray(); p.Offers.Clear(); var o = p.Open; if (!o.IsJob) o.Cards = null; o.Id = NextOfferId(); p.Offers.Enqueue(o); foreach (var r in rest) p.Offers.Enqueue(r); }
                p.Open = null;
            }
            if (!keepQueued) p.Offers.Clear();
        }

        private struct Cand { public HordeCardKind Kind; public int DefId, Level, Weight; }

        private HordeCard[] DrawCards(SimPlayer p)
        {
            var newW = new List<Cand>(); var lvlW = new List<Cand>(); var newP = new List<Cand>(); var lvlP = new List<Cand>();
            var seenW = new HashSet<int>(); var seenP = new HashSet<int>();
            foreach (var ow in p.Weapons) if (ow.Level < ow.Def.Levels.Length && !YggBurntWeapon(p, ow)) lvlW.Add(new Cand { Kind = HordeCardKind.WeaponLevel, DefId = ow.Def.Id, Level = ow.Level + 1, Weight = Math.Max(1, ow.Def.Weight) }); // (client wave 18, h18feel: a burnt berry never levels, point 37)
            foreach (var op in p.Passives) if (op.Level < op.Def.Levels.Length) lvlP.Add(new Cand { Kind = HordeCardKind.PassiveLevel, DefId = op.Def.Id, Level = op.Level + 1, Weight = Math.Max(1, op.Def.Weight) });
            TransOfferCandidates(p, lvlW, lvlP); // client wave 18 (h18trans): point 30 — max-level weapons & passives transcend
            int slotsW = 0, activeW = 0; foreach (var ow in p.Weapons) { if (!ow.Signature) slotsW++; if (IsActiveWeapon(ow.Def.Id)) activeW++; }
            foreach (var j in p.Jobs)
            {
                foreach (int id in j.Weapons)
                {
                    var w = C.Weapon(id);
                    if (w == null || w.Weight <= 0 || w.Levels.Length == 0 || !seenW.Add(id) || p.Owns(w) || slotsW >= R.MaxWeapons) continue;
                    if (IsActiveWeapon(id) && activeW >= 2) continue;
                    int wWeight = IsActiveWeapon(id) ? Math.Max(1, w.Weight / 3) : w.Weight;
                    newW.Add(new Cand { Kind = HordeCardKind.NewWeapon, DefId = id, Level = 1, Weight = wWeight });
                }
                foreach (int id in j.Passives)
                {
                    var ps = C.Passive(id);
                    if (ps == null || ps.Weight <= 0 || ps.Levels.Length == 0 || !seenP.Add(id) || p.Owns(ps) || p.Passives.Count >= R.MaxPassives) continue;
                    newP.Add(new Cand { Kind = HordeCardKind.NewPassive, DefId = id, Level = 1, Weight = ps.Weight });
                }
            }
            CardsFilter(p, newW, newP); // client wave 17 (h17cards): the arrows / Endow / the homunculi exclude each other
            // Item 52 (2026-10-12): stat boosts are attached as bonus stats on skill/ability/passive cards rather than standalone stat cards
            var pools = new[] { newW, lvlW, newP, lvlP };
            var weights = new[] { R.WeightNewWeapon, R.WeightWeaponLevel, R.WeightNewPassive, R.WeightPassiveLevel };
            var picked = new List<HordeCard>();
            int want = Math.Max(1, R.CardsPerOffer);
            while (picked.Count < want)
            {
                int total = 0;
                for (int i = 0; i < pools.Length; i++) if (pools[i].Count > 0) total += Math.Max(0, weights[i]);
                if (total <= 0) break;
                int r = _rng.Next(total), k = 0;
                for (; k < pools.Length; k++) { if (pools[k].Count == 0 || weights[k] <= 0) continue; r -= weights[k]; if (r < 0) break; }
                if (k >= pools.Length) break;
                var pool = pools[k];
                int tw = 0; foreach (var c in pool) tw += c.Weight;
                int rr = _rng.Next(Math.Max(1, tw)), idx = 0;
                for (; idx < pool.Count - 1; idx++) { rr -= pool[idx].Weight; if (rr < 0) break; }
                var ch = pool[idx]; pool.RemoveAt(idx);
                if (CardsBranchCards(p, ch, picked, want)) continue; // client wave 17 (h17cards): a level 2 -> 3 card is the weapon's two branches
                if (TransBranchCards(p, ch, picked, want)) continue; // client wave 18 (h18trans): a level 5 -> 6 transcendence card is the weapon's two branches
                picked.Add(new HordeCard { Kind = ch.Kind, DefId = ch.DefId, Level = ch.Level });
            }
            if (picked.Count == 0) picked.Add(new HordeCard { Kind = HordeCardKind.Heal, DefId = 0, Level = 30 });
            if (R.WeightStat > 0 && C.Stats.Count > 0 && R.StatCardPoints > 0)
            {
                byte pts = (byte)Math.Max(1, Math.Min(255, R.StatCardPoints));
                int start = _rng.Next(C.Stats.Count);
                int step = C.Stats.Count > 1 ? 1 + _rng.Next(C.Stats.Count - 1) : 1;
                for (int i = 0; i < picked.Count; i++)
                {
                    var card = picked[i];
                    var sd = C.Stats[(start + i * step) % C.Stats.Count];
                    card.BonusStat = (byte)((int)sd.Stat + 1);
                    card.BonusStatPoints = pts;
                    picked[i] = card;
                }
            }
            return picked.ToArray();
        }

        private void ApplyCard(SimPlayer p, HordeOffer o, HordeCard c)
        {
            OffersClosed.Add(new KeyValuePair<int, int>(p.Slot, o.Id));
            p.Open = null;
            switch (c.Kind)
            {
                case HordeCardKind.Job: ChangeJob(p, C.Job(c.DefId)); break;
                case HordeCardKind.NewWeapon:
                {
                    var w = C.Weapon(c.DefId);
                    if (w != null && !p.Owns(w)) p.Weapons.Add(new OwnedWeapon { Def = w, Level = 1, NextTick = Tick + 1 });
                    break;
                }
                case HordeCardKind.WeaponLevel:
                    foreach (var ow in p.Weapons) if (ow.Def.Id == c.DefId && ow.Level < ow.Def.Levels.Length) { if (!CardsLevelUp(p, ow)) ow.Level++; RestartOrbit(p, ow); break; } // client wave 17 (h17cards): a chest's 2 -> 3 takes a branch
                    break;
                case HordeCardKind.WeaponBranch: ApplyBranch(p, c); break; // client wave 17 (h17cards)
                case HordeCardKind.WeaponTranscend: ApplyWeaponTranscend(p, c); break; // client wave 18 (h18trans): point 30
                case HordeCardKind.NewPassive:
                {
                    var ps = C.Passive(c.DefId);
                    if (ps != null && !p.Owns(ps)) p.Passives.Add(new OwnedPassive { Def = ps, Level = 1 });
                    break;
                }
                case HordeCardKind.PassiveLevel:
                    foreach (var op in p.Passives) if (op.Def.Id == c.DefId && op.Level < op.Def.Levels.Length) { op.Level++; break; }
                    break;
                case HordeCardKind.PassiveTranscend: ApplyPassiveTranscend(p, c); break; // client wave 18 (h18trans): point 30
                case HordeCardKind.Stat:
                    if (c.DefId >= 0 && c.DefId < 6) p.StatPoints[c.DefId] += c.Level;
                    break;
                case HordeCardKind.Heal:
                    p.Hp = Math.Min(p.MaxHp, p.Hp + p.MaxHp * c.Level / 100);
                    break;
            }
            if (c.Kind != HordeCardKind.Job && c.Kind != HordeCardKind.Stat && c.HasBonusStat)
            {
                int si = (int)c.BonusStatKind;
                if (si >= 0 && si < 6) p.StatPoints[si] += c.BonusStatPoints;
            }
            int lvl = 0;
            foreach (var ow in p.Weapons) if (ow.Def.Id == c.DefId && (c.Kind == HordeCardKind.NewWeapon || c.Kind == HordeCardKind.WeaponLevel || c.Kind == HordeCardKind.WeaponBranch || c.Kind == HordeCardKind.WeaponTranscend)) lvl = ow.Level; // client wave 17 (h17cards) + wave 18 (h18trans)
            foreach (var op in p.Passives) if (op.Def.Id == c.DefId && (c.Kind == HordeCardKind.NewPassive || c.Kind == HordeCardKind.PassiveLevel || c.Kind == HordeCardKind.PassiveTranscend)) lvl = op.Level;
            if (c.Kind == HordeCardKind.Stat) lvl = c.DefId >= 0 && c.DefId < 6 ? p.StatPoints[c.DefId] : 0;
            if (c.Kind != HordeCardKind.Job)
                AddEvent(HordeEventKind.CardTaken, HordeEventFlags.None, p.Slot, -1, c.DefId, lvl, p.X, p.Y, (float)c.Kind);
            Recalc(p, false);
            p.LoadoutVersion++;
            QueueJobOffer(p);
        }

        private void ChestUpgrade(SimPlayer p)
        {
            var opts = new List<HordeCard>();
            foreach (var ow in p.Weapons) if (ow.Level < ow.Def.Levels.Length && !YggBurntWeapon(p, ow)) opts.Add(new HordeCard { Kind = HordeCardKind.WeaponLevel, DefId = ow.Def.Id, Level = ow.Level + 1 }); // (client wave 18, h18feel: point 37)
            foreach (var op in p.Passives) if (op.Level < op.Def.Levels.Length) opts.Add(new HordeCard { Kind = HordeCardKind.PassiveLevel, DefId = op.Def.Id, Level = op.Level + 1 });
            TransChestCandidates(p, opts); // client wave 18 (h18trans): point 30
            if (opts.Count == 0) { p.Hp = p.MaxHp; return; }
            var c = opts[_rng.Next(opts.Count)];
            ApplyCard(p, new HordeOffer { Id = 0 }, c);
            OffersClosed.RemoveAt(OffersClosed.Count - 1); // (no offer was open)
        }

        private void ChangeJob(SimPlayer p, JobDef job)
        {
            if (job == null) return;
            p.JobId = job.JobId; p.Tier = job.Tier;
            SoulsJobChanged(p); // client wave 18 (h18feel), point 35: the death part of its shard count drops by 2 (HordeSim.Souls.cs)
            if (!p.Jobs.Contains(job)) p.Jobs.Add(job);
            if (job.StatBonus != null) for (int i = 0; i < 6 && i < job.StatBonus.Length; i++) p.StatPoints[i] += job.StatBonus[i];
            var sig = C.Weapon(job.SignatureWeapon);
            if (sig != null && sig.Levels.Length > 0)
            {
                var have = p.Weapons.Find(w => RootId(w.Def) == sig.Id);
                if (have == null) p.Weapons.Add(new OwnedWeapon { Def = sig, Level = 1, NextTick = Tick + 1, Signature = true });
                else if (have.Level < have.Def.Levels.Length && !CardsLevelUp(p, have)) { have.Level++; RestartOrbit(p, have); }
            }
            Recalc(p, false);
            p.LoadoutVersion++;
            AddEvent(HordeEventKind.JobChanged, HordeEventFlags.None, p.Slot, -1, sig != null ? sig.Id : 0, job.JobId, p.X, p.Y);
            QueueJobOffer(p);
        }

        // =============================================================================================== falling and rising

        // client wave 18 (h18feel), the design's point 35: wave 8's timed rise (StepRevive: back after RulesDef.ReviveSeconds while an ally
        // stood, ReviveNearMul faster with one within ReviveRadius) is gone; soul retrieval and the class-change level bring a fallen
        // player back (HordeSim.Souls.cs StepSouls). ReviveSeconds / ReviveRadius / ReviveNearMul stay in the tables (and the stamp), unused.

        private void CheckEnd()
        {
            if (Over) return;
            int present = 0, standing = 0;
            foreach (var p in Players) if (p.Present) { present++; if (!p.Fallen) standing++; }
            if (present > 0 && standing == 0) { End(HordeOutcome.Defeat); return; }
        }

        /// <summary>Ends the run (the host may stop it: Stopped).</summary>
        public void End(HordeOutcome outcome)
        {
            if (Over) return;
            if (outcome == HordeOutcome.Defeat && (FinalPhase || _waveCycle > 0)) outcome = HordeOutcome.Victory;
            Over = true; Outcome = outcome; EndTick = Tick;
            foreach (var p in Players) if (p.Present) CloseOffers(p);
        }

        // =============================================================================================== helpers

        internal void AddEvent(HordeEventKind kind, HordeEventFlags flags, int slot, int target, int defId, int value, int x, int y, float a = 0, float b = 0, float c = 0, int x2 = 0, int y2 = 0)
        {
            Events.Add(new HordeEvent { Kind = kind, Flags = flags, Tick = Tick, Slot = slot, Target = target, DefId = defId, Value = value, X = Cells(x), Y = Cells(y), X2 = Cells(x2), Y2 = Cells(y2), A = a, B = b, C = c });
        }

        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
        public static long Dist2(int ax, int ay, int bx, int by) { long dx = ax - bx, dy = ay - by; return dx * dx + dy * dy; }

        /// <summary>A hash of the whole state (determinism tests: the same seed and inputs give the same hash, tick for tick).</summary>
        public ulong StateHash()
        {
            ulong h = 1469598103934665603UL;
            void M(long v) { unchecked { h ^= (ulong)v; h *= 1099511628211UL; } }
            M(Tick); M(EnemiesAlive); M((long)_rng.State0); M((long)_rng.State1); M(PartyLevel); M(PartyExp);
            foreach (var p in Players) { if (!p.Present) continue; M(p.X); M(p.Y); M(p.Vx); M(p.Vy); M(p.Hp); M(p.Level); M(p.JobId); M(p.Kills); M(p.Fallen ? 1 : 0); foreach (var w in p.Weapons) { M(w.Def.Id); M(w.Level); } }
            foreach (var e in Enemies) { if (!e.Alive) continue; M(e.Slot); M(e.X); M(e.Y); M(e.KbVx); M(e.KbVy); M(e.Hp); M(e.Def.Id); }
            foreach (var k in Pickups) { M(k.Id); M(k.X); M(k.Y); M(k.Value); M(k.PulledBy); M(k.PullTick); }
            foreach (var f in Fx) { M(f.Id); M(f.X); M(f.Y); M(f.EndTick); }
            HashObjectives(M); // client wave 17 (h17obj)
            HashSouls(M); // client wave 18 (h18feel)
            return h;
        }
    }

    // =================================================================================================== state classes

    public struct SimInput { public ushort Seq; public sbyte Dx, Dy; }

    /// <summary>A player's modifiers, summed (percent kinds in percent).</summary>
    public sealed class PlayerStats
    {
        public double MaxHpFlat, MaxHpPct, Regen, MovePct, DamagePct, MeleePct, RangedPct, MagicPct, CooldownPct, AreaPct, ProjectileCount,
                      ProjectileSpeedPct, DurationPct, PickupRadiusPct, Crit, CritDamage, ExpGainPct, Defence, DamageTakenPct, Dodge, HealPowerPct,
                      ReviveSpeedPct, VsBoss, EffectChance, KnockbackPct, HpOnKill;
        public readonly double[] VsRace = new double[10], VsElement = new double[10], ElementDamage = new double[10];
        public readonly double[] StatsAdded = new double[6];
        public void Clear()
        {
            MaxHpFlat = MaxHpPct = Regen = MovePct = DamagePct = MeleePct = RangedPct = MagicPct = CooldownPct = AreaPct = ProjectileCount = 0;
            ProjectileSpeedPct = DurationPct = PickupRadiusPct = Crit = CritDamage = ExpGainPct = Defence = DamageTakenPct = Dodge = HealPowerPct = 0;
            ReviveSpeedPct = VsBoss = EffectChance = KnockbackPct = HpOnKill = 0;
            Array.Clear(VsRace, 0, 10); Array.Clear(VsElement, 0, 10); Array.Clear(ElementDamage, 0, 10); Array.Clear(StatsAdded, 0, 6);
        }
    }

    public sealed class OwnedWeapon
    {
        public WeaponDef Def;
        public int Level;
        public bool Signature;
        public int NextTick, ActiveNextTick;
        public int BurstLeft, BurstNext, BurstTarget = -1, BurstX, BurstY;
        public double BurstAngle;
        public int OrbitFxId;
        public readonly Dictionary<int, int> OrbHits = new Dictionary<int, int>();
        public WeaponLevel Lv => Def.Levels[Math.Max(0, Math.Min(Level, Def.Levels.Length) - 1)];
    }

    public sealed class OwnedPassive { public PassiveDef Def; public int Level; }

    public sealed class SimBuff { public ModDef[] Mods; public int Until; public int SourceSlot; public int WeaponId; }

    public sealed class SimPlayer
    {
        public bool Present, Used, Left, Away, Moving, Fallen, Helped, IdleChooser;
        public int Slot;
        public uint Aid;
        public string Name = "";
        public int X, Y, FaceDeg;
        /// <summary>The walk's velocity (units per tick; it ramps toward the stick) and its ramp (units per tick per tick).</summary>
        public int Vx, Vy, AccelPerTick = 1, DecelPerTick = 1;
        public int Hp, MaxHp, SpeedPerTick;
        public double RegenCarry, ExpCarry, KillHealCarry, Revive;
        public int Level = 1, Exp, JobId, Tier;
        public byte Sex;
        public readonly List<JobDef> Jobs = new List<JobDef>();
        public readonly List<OwnedWeapon> Weapons = new List<OwnedWeapon>();
        public readonly List<OwnedPassive> Passives = new List<OwnedPassive>();
        public readonly List<SimBuff> Buffs = new List<SimBuff>();
        public readonly int[] StatPoints = new int[6];
        public readonly PlayerStats S = new PlayerStats();
        public int SafeUntil, HurtUntil;
        public readonly Queue<HordeOffer> Offers = new Queue<HordeOffer>();
        public HordeOffer Open;
        public int OpenDeadline;
        public readonly Queue<SimInput> Inputs = new Queue<SimInput>();
        public ushort AckSeq;
        public int JoinTick, LeftTick = -1;
        public int Kills, EliteKills, BossKills, Falls, Revives;
        public long DamageDealt, Healing, DamageTaken;
        public int LoadoutVersion;
        /// <summary>Client wave 15 (point 25): card choices left unmade when it left (given back if it returns), and whether this
        /// stay began with the build of an earlier one.</summary>
        public int CardsOwed; public bool Rebuilt;

        public bool Owns(WeaponDef w) { int r = HordeSim.RootId(w); foreach (var o in Weapons) if (HordeSim.RootId(o.Def) == r) return true; return false; }
        public bool Owns(PassiveDef d) { int r = HordeSim.PassiveRootId(d); foreach (var o in Passives) if (HordeSim.PassiveRootId(o.Def) == r) return true; return false; }
        public int Stat(int i) => StatPoints[i] + (int)Math.Round(S.StatsAdded[i]);

        public void Reset()
        {
            Present = Left = Away = Moving = Fallen = Helped = IdleChooser = false; Aid = 0; Name = ""; X = Y = FaceDeg = 0; Hp = MaxHp = SpeedPerTick = 0; Vx = Vy = 0; AccelPerTick = DecelPerTick = 1;
            RegenCarry = ExpCarry = KillHealCarry = Revive = 0; Level = 1; Exp = 0; JobId = 0; Tier = 0;
            Jobs.Clear(); Weapons.Clear(); Passives.Clear(); Buffs.Clear(); Array.Clear(StatPoints, 0, 6); S.Clear();
            SafeUntil = HurtUntil = 0; Offers.Clear(); Open = null; OpenDeadline = 0; Inputs.Clear(); AckSeq = 0;
            JoinTick = 0; LeftTick = -1; Kills = EliteKills = BossKills = Falls = Revives = 0; DamageDealt = Healing = DamageTaken = 0; LoadoutVersion++;
            CardsOwed = 0; Rebuilt = false;
        }
    }

    public sealed class SimEnemy
    {
        public bool Alive;
        public int Slot, Serial;
        public EnemyDef Def;
        public int X, Y, Hp, MaxHp, Damage, ShotDamage, Speed, Radius;
        public int SpawnTick, NextAttack, ShotNext;
        public int SlowUntil, SlowPct, FrozenUntil, StunUntil;
        public int DotUntil, DotNext, DotEvery, DotDamage, DotSlot = -1, DotWeapon;
        public int ChargeNext, WindUntil, DashUntil, DashVx, DashVy;
        /// <summary>A knockback's impulse (units per tick), dying down each tick.</summary>
        public int KbVx, KbVy, KbWindowTick, KbCount, ZoneHitUntil;
        public int[] AbilityNext = new int[0];
        public HordeEnemyFlags Flags;
        /// <summary>Client wave 15 (point 21): the strength a shuffled boss (and its adds) took from its mark (1 = the tables').</summary>
        public double HpScale = 1, DmgScale = 1;
        /// <summary>Client wave 18 (h18rules), point 31: the share of its EXP it drops (1 / the closeness of the group it came in; 1 = all).</summary>
        public double ExpScale = 1;
        public void Reset()
        {
            Alive = false; Def = null; X = Y = Hp = MaxHp = Damage = ShotDamage = Speed = Radius = 0; SpawnTick = NextAttack = ShotNext = 0;
            SlowUntil = SlowPct = FrozenUntil = StunUntil = 0; DotUntil = DotNext = DotEvery = DotDamage = DotWeapon = 0; DotSlot = -1;
            ChargeNext = WindUntil = DashUntil = DashVx = DashVy = 0; KbVx = KbVy = KbWindowTick = KbCount = ZoneHitUntil = 0; AbilityNext = new int[0]; Flags = HordeEnemyFlags.None; HpScale = DmgScale = 1;
            ExpScale = 1; // client wave 18 (h18rules)
        }
    }

    /// <summary>A pickup; PulledBy ≥ 0: it flies to that player since PullTick (HordeSim.PullSpeed).</summary>
    public sealed class SimPickup { public int Id; public PickupDef Def; public int X, Y, Value; public int PulledBy = -1; public int PullTick; }

    public sealed class SimSummon
    {
        public int Id, OwnerSlot, X, Y, Hp, MaxHp, NextAttack, Until;
        public OwnedWeapon Weapon;
    }

    /// <summary>A lasting effect (shots, zones, traps, warnings, orbits).</summary>
    public sealed class SimFx
    {
        public int Id;
        public HordeFxKind Kind;
        public int OwnerSlot = -1, OwnerEnemy = -1, DefId, Level, EnemyDefId;
        public int X, Y, VX, VY, Radius, Range;
        public double Angle, Turns;
        public int Count, Target = -1;
        public bool Follows;
        public int StartTick, ArmTick, EndTick;
        // the engine's own
        public OwnedWeapon Weapon;
        public WeaponLevel Lv;
        public double BaseDamage;
        public int Damage, PierceLeft, NextTick, Every, Heal;
        public double HealPct;
        public ZoneFlags ZoneFlags;
        public int BurstArea;
        public readonly List<int> HitList = new List<int>();
        public bool Ended;
    }

    /// <summary>Enemies binned by 2 × 2 cells, rebuilt each tick (range queries without looking at every enemy).</summary>
    internal sealed class Grid
    {
        private const int Shift = 9; // 512 units = 2 cells
        private readonly int _x0, _y0, _w, _h;
        private readonly int[] _head;
        private readonly int[] _next = new int[HordeLimits.MaxEnemies];
        public Grid(int minX, int minY, int maxX, int maxY)
        {
            _x0 = minX; _y0 = minY;
            _w = ((maxX - minX) >> Shift) + 1; _h = ((maxY - minY) >> Shift) + 1;
            _head = new int[_w * _h];
        }
        private int Cx(int x) { int c = (x - _x0) >> Shift; return c < 0 ? 0 : c >= _w ? _w - 1 : c; }
        private int Cy(int y) { int c = (y - _y0) >> Shift; return c < 0 ? 0 : c >= _h ? _h - 1 : c; }
        public void Build(SimEnemy[] es)
        {
            for (int i = 0; i < _head.Length; i++) _head[i] = -1;
            for (int i = 0; i < es.Length; i++)
            {
                if (!es[i].Alive) continue;
                int c = Cy(es[i].Y) * _w + Cx(es[i].X);
                _next[i] = _head[c]; _head[c] = i;
            }
        }
        /// <summary>Every live enemy whose cell is within r of (x, y) (a superset: the caller tests the distance).</summary>
        public void Query(int x, int y, int r, SimEnemy[] es, List<int> into)
        {
            int ax = Cx(x - r), bx = Cx(x + r), ay = Cy(y - r), by = Cy(y + r);
            for (int cy = ay; cy <= by; cy++)
                for (int cx = ax; cx <= bx; cx++)
                    for (int i = _head[cy * _w + cx]; i >= 0; i = _next[i])
                        if (es[i].Alive) into.Add(i);
        }
    }
}
