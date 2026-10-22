using System;
using System.Collections.Generic;
using Loki.Net;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 8 (w8game): a scripted stand-in for <see cref="IHordeChannel"/>, with no server: the offline mode of the
    /// smoke (-lokiSmokeOnly w8horde-offline), the frame-time measurement (any number of enemies held alive) and a way to see
    /// the mode without a Loki Host. It is NOT the engine (w8core's HordeSim runs in Loki Host): it plays a small scripted run
    /// from the real content tables (Horde/Core/Content: the arena, the enemies' monster ids, the weapons' skills, the jobs,
    /// the pickups' items) so that everything the client draws is exercised: an invitation, the countdown, enemies walking in,
    /// shots, kills, pickups, level-ups with cards, a job change, a fall and a rise (scripted at <see cref="FallAt"/>), the end
    /// and the results with a reward note. Its numbers (spawn rings, the damage of a touch, the fake reward) are the stand-in's
    /// own and mean nothing for the real game.
    /// </summary>
    public sealed class HordeFakeChannel : IHordeChannel
    {
        public HordeLinkState LinkState { get; private set; } = HordeLinkState.Ready;
        public HordeStatus Status { get; } = new HordeStatus { Enabled = true };
        public bool InRun { get; private set; }
        public HordeRunInfo Run { get; private set; }
        public HordeWorldView World { get; } = new HordeWorldView();
        public double Now => _clock();
        public float MyX { get; private set; }
        public float MyY { get; private set; }

        public event Action StatusChanged;
        public event Action<HordeInvite> Invited;
        public event Action<HordeRunInfo> RunStarted;
        public event Action SnapshotArrived;
        public event Action<HordeEvent> Happened;
        public event Action<HordeFxView> FxBegan;
        public event Action<HordeFxView, bool> FxEnded;
        public event Action<HordeOffer> Offered;
        public event Action<int> OfferClosed;
        public event Action<HordeResults> Ended;
        public event Action<HordeRewardNote> Rewarded;
        public event Action Lost;

        // ---- the script (the smoke and the measurement set these)
        public float GatherSeconds = 5f, RunLength = 900f;
        /// <summary>Enemies held alive (spawned on a ring around me as they die).</summary>
        public int TargetEnemies = 40;
        /// <summary>Seconds into the run at which I fall (scripted: the HUD, the falling and the rising are seen); &lt; 0 never.</summary>
        public float FallAt = -1f, ReviveSeconds = 5f;
        /// <summary>Touches hurt (false for the measurement).</summary>
        public bool EnemiesHurt = true;
        /// <summary>A second, scripted player in the run (walks after me).</summary>
        public bool Ally = true;
        /// <summary>Run EXP needed is divided by this (the smoke reaches the job mark quickly).</summary>
        public float ExpFactor = 1f;
        /// <summary>The stand-in's own pickup reach (cells): the smoke's walk collects without aiming.</summary>
        public float PickupRadius = 4f;
        /// <summary>Client wave 9 (h9flow): seconds into the run at which the tables' first elite walks in (BossAppeared, Elite flag; the
        /// HUD's boss bar and banner are seen); &lt; 0 never. And the second at which the scripted ally drops in LATE (PlayerJoined with
        /// the Late flag: the entry effect); &lt; 0: the ally is there from the start when <see cref="Ally"/> is set.</summary>
        public float BossAt = -1f, LateAllyAt = -1f;
        /// <summary>Seconds the run stood paused for open offers (the design's rule; the smoke).</summary>
        public float PausedSeconds;
        /// <summary>Client wave 15 (w15horde), point 26: alone, an offer has no time limit (the host's engine always does this; the stand-in
        /// only when set: the older smokes on it expect a limit in every run).</summary>
        public bool SoloNoLimit { get; set; }
        /// <summary>design note (2026-10-05 01:25), as h9move's engine does it: the pause has a limit of 30 s in every run, solo included; each time it is
        /// MET (the host's auto-pick at the deadline, or an answer within its final second) it drops by 10 s for the rest of the run, floor 5 s.
        /// HordeOffer.Seconds carries the current limit. <see cref="LimitsMet"/> counts the drops (the smoke).</summary>
        public const float PauseLimitMetWindow = 1.0f;
        /// <summary>The schedule's numbers are the TABLES' (RulesDef.ChoiceSeconds / ChoiceSecondsStep / ChoiceSecondsMin), as h9move's engine reads them.</summary>
        private float PauseLimitStart => _c.Rules.ChoiceSeconds > 0f ? _c.Rules.ChoiceSeconds : 30f;
        private float PauseLimitStep => _c.Rules.ChoiceSecondsStep > 0f ? _c.Rules.ChoiceSecondsStep : 10f;
        private float PauseLimitFloor => _c.Rules.ChoiceSecondsMin > 0f ? _c.Rules.ChoiceSecondsMin : 5f;
        public float PauseLimit = 30f; public int LimitsMet;

        private readonly HordeContent _c; private readonly uint _myAid; private readonly string _myName; private readonly Func<double> _clock;
        private readonly Random _rnd = new Random(8);
        private float _mvx, _mvy; private double _lastPoll, _tickAcc, _gatherEnd = -1, _endAt = -1;
        private int _tick, _nextFxId = 1, _nextPickupId = 1, _offerId, _openOffer = -1; private double _offerUntil; private HordeOffer _offer;
        private readonly Dictionary<int, float> _cool = new Dictionary<int, float>();
        private readonly List<HordeEvent> _events = new List<HordeEvent>();
        private float _hurtGrace; private bool _fellScripted; private int _tier; private int _serial; private bool _bossScripted, _lateScripted;
        private HordeResults _pendingResults; private double _rewardAt = -1;
        private readonly int[] _killsBy = new int[HordeLimits.MaxPlayers]; private readonly long[] _damageBy = new long[HordeLimits.MaxPlayers];

        public HordeFakeChannel(HordeContent content, uint myAid, string myName, Func<double> clock)
        {
            _c = content; _myAid = myAid; _myName = myName ?? "?"; _clock = clock;
            Status.Phase = HordePhase.Idle;
            Status.Towns.AddRange(new[] { "prontera", "izlude", "geffen", "payon", "morocc", "alberta", "aldebaran", "comodo", "juno", "lighthalzen", "einbroch", "hugel", "rachel", "veins" });
        }

        private HordeArenaShim Arena => new HordeArenaShim(_c.Arenas.Count > 0 ? _c.Arenas[0] : new ArenaDef { Map = "prt_fild08", X0 = 130, Y0 = 130, X1 = 220, Y1 = 220 });
        private readonly struct HordeArenaShim { public readonly ArenaDef A; public HordeArenaShim(ArenaDef a) { A = a; } }

        // ---------------------------------------------------------------- requests
        public void RequestStart()
        {
            if (Status.Phase != HordePhase.Idle) return;
            Gather(_myAid, _myName, true);
        }

        /// <summary>The script: another player starts a run (the prompt).</summary>
        public void FakeInvite(uint starterAid, string starterName)
        {
            if (Status.Phase != HordePhase.Idle) return;
            Gather(starterAid, starterName, false);
            Invited?.Invoke(new HordeInvite { RunId = Status.RunId, StarterAid = starterAid, StarterName = starterName, Seconds = GatherSeconds, ArenaMap = Arena.A.Map, RunLength = RunLength, Reward = HordeRewardSize.Normal });
        }

        private void Gather(uint aid, string name, bool meIn)
        {
            Status.Phase = HordePhase.Gathering; Status.RunId++; Status.StarterAid = aid; Status.StarterName = name;
            Status.SecondsLeft = GatherSeconds; Status.ArenaMap = Arena.A.Map; Status.RunLength = RunLength; Status.Reward = HordeRewardSize.Normal;
            Status.Members.Clear();
            Status.Members.Add(new HordeMember { Aid = aid, Name = name, State = HordeMemberState.Joined });
            if (aid != _myAid) Status.Members.Add(new HordeMember { Aid = _myAid, Name = _myName, State = meIn ? HordeMemberState.Joined : HordeMemberState.Asked });
            _gatherEnd = Now + GatherSeconds;
            StatusChanged?.Invoke();
        }

        public void Answer(uint runId, bool join)
        {
            if (runId != Status.RunId) return;
            var m = Status.Member(_myAid);
            if (Status.Phase == HordePhase.Gathering) { if (m != null) m.State = join ? HordeMemberState.Joined : HordeMemberState.Declined; StatusChanged?.Invoke(); }
            else if (Status.Phase == HordePhase.Running && join && !InRun) Begin(true);
        }

        public void Choose(int offerId, int index)
        {
            if (_offer == null || offerId != _offer.Id) return;
            Apply(_offer, Math.Max(0, Math.Min(index, _offer.Cards.Length - 1)), _offerUntil - Now <= PauseLimitMetWindow); // within the final second: the limit was met
        }

        public void LeaveRun()
        {
            if (!InRun) return;
            if (KeepRunOnLeave) { LeaveKeepingRun(); return; }
            Finish(HordeOutcome.Stopped, true);
        }

        // ---------------------------------------------------------------- client wave 14 (w14kit), point 15 of the design's headset list
        /// <summary>The script: leaving keeps the run going, as the real host does (my member "left", results with no reward now, the
        /// run rejoinable or ENDED by <see cref="EndRun"/>); off (the older smokes): leaving ends the stand-in's run.</summary>
        public bool KeepRunOnLeave;
        /// <summary>H_END's stand-in: the run ends for everyone and each gets the EXP earned so far (the results' reward, then the note).</summary>
        public void EndRun(uint runId)
        {
            if (runId != Status.RunId || Status.Phase != HordePhase.Running) return;
            var m = Status.Member(_myAid);
            if (m == null || (m.State != HordeMemberState.Joined && m.State != HordeMemberState.Left)) return;
            EndRuns++;
            Finish(HordeOutcome.Stopped, m.State == HordeMemberState.Left);
        }
        /// <summary>H_END requests the stand-in took (the smoke).</summary>
        public int EndRuns { get; private set; }

        private void LeaveKeepingRun()
        {
            var my = World.Players[0];
            var r = new HordeResults { RunId = Status.RunId, Outcome = HordeOutcome.Stopped, Seconds = World.Seconds, MySlot = 0 };
            r.Players.Add(new HordePlayerResult { Slot = 0, Aid = my.Aid, Name = my.Name, Level = my.Level, JobId = my.JobId, Kills = _killsBy[0], Seconds = World.Seconds, Score = _killsBy[0] + my.Level * 10, Reward = HordeRewardState.None, Left = true });
            if (_offer != null) { OfferClosed?.Invoke(_offer.Id); _offer = null; _openOffer = -1; }
            InRun = false;
            var me = Status.Member(_myAid); if (me != null) me.State = HordeMemberState.Left;
            StatusChanged?.Invoke();
            Ended?.Invoke(r);
        }

        public void SetMove(float x, float y) { _mvx = x; _mvy = y; }

        // ---------------------------------------------------------------- client wave 15 (w15hfx), point 19 of the design's wave 14 list
        /// <summary>H_PAUSE's stand-in: the run waits (as for a choice), I am marked as the holder; resume ends it.</summary>
        public void Pause(bool pause)
        {
            if (!InRun || pause == _held) return;
            _held = pause; Pauses++;
            var my = World.Players[0];
            if (pause) my.Flags |= HordePlayerFlags.Held; else my.Flags &= ~HordePlayerFlags.Held;
        }
        /// <summary>H_PAUSE requests the stand-in took (the smoke).</summary>
        public int Pauses { get; private set; }
        public bool Held => _held;
        private bool _held;
        public void CastActive(int weaponId, float cellX, float cellY)
        {
            if (!InRun || _held || _offer != null) return;
            var my = World.Players[0];
            if (!my.Present || my.Fallen) return;
            var def = _c.Weapon(weaponId);
            if (def == null || def.Levels == null || def.Levels.Length == 0) return;
            int level = 1;
            foreach (var ow in my.Weapons) if (ow.DefId == weaponId) { level = ow.Level; break; }
            var lv = def.Levels[Math.Min(level, def.Levels.Length) - 1];
            float dx = cellX - my.X, dy = cellY - my.Y;
            float aimAng = dx * dx + dy * dy > 1e-4f ? (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI) : my.Facing;
            my.Facing = aimAng < 0f ? aimAng + 360f : aimAng;
            float r = Math.Max(1.2f, lv.Area > 0f ? lv.Area : 2.2f);
            int durTicks = Math.Max(20, (int)((lv.Duration > 0f ? lv.Duration : 4.5f) * HordeLimits.TickRate));
            if ((def.Zone & ZoneFlags.BlocksEnemies) != 0)
            {
                float wallAng = (my.Facing + 90f) % 360f;
                var fx = new HordeFxView
                {
                    Id = _nextFxId++,
                    Kind = HordeFxKind.Zone,
                    OwnerSlot = 0,
                    DefId = def.Id,
                    Level = level,
                    X = cellX,
                    Y = cellY,
                    Radius = Math.Max(0.75f, Math.Min(1.05f, r * 0.42f)),
                    Range = Math.Max(1.8f, Math.Min(4.2f, r * 1.45f)),
                    Angle = wallAng,
                    StartTick = _tick,
                    EndTick = _tick + durTicks,
                    Target = -1
                };
                World.Fx.Add(fx);
                FxBegan?.Invoke(fx);
            }
            else
            {
                var fx = new HordeFxView
                {
                    Id = _nextFxId++,
                    Kind = def.Kind == WeaponKind.Trap ? HordeFxKind.Trap : HordeFxKind.Zone,
                    OwnerSlot = 0,
                    DefId = def.Id,
                    Level = level,
                    X = cellX,
                    Y = cellY,
                    Radius = r,
                    StartTick = _tick,
                    ArmTick = _tick,
                    EndTick = _tick + durTicks,
                    Target = -1
                };
                World.Fx.Add(fx);
                FxBegan?.Invoke(fx);
            }
        }

        // ---------------------------------------------------------------- the run
        private void Begin(bool late)
        {
            InRun = true; World.Clear(); _tick = 0; _serial = 0; _tier = 0; _fellScripted = false; _bossScripted = false; _lateScripted = false; PausedSeconds = 0f; PauseLimit = PauseLimitStart; LimitsMet = 0; _cool.Clear(); Array.Clear(_killsBy, 0, _killsBy.Length); Array.Clear(_damageBy, 0, _damageBy.Length);
            var a = Arena.A;
            Run = new HordeRunInfo { RunId = Status.RunId, Slot = 0, ArenaId = a.Id, Map = a.Map, X0 = a.X0, Y0 = a.Y0, X1 = a.X1, Y1 = a.Y1, TickRate = HordeLimits.TickRate, RunLength = RunLength, FinalBossSeconds = 120f, Late = late };
            MyX = (a.X0 + a.X1) / 2f + 0.5f; MyY = (a.Y0 + a.Y1) / 2f + 0.5f;
            var start = _c.StartJob();
            AddPlayer(0, _myAid, _myName, start != null ? start.JobId : 0, MyX, MyY);
            if (Ally && LateAllyAt < 0f) AddPlayer(1, 0xFFFF0001u, "Bot Ally", start != null ? start.JobId : 0, MyX + 2f, MyY);
            World.TickAt = World.PrevTickAt = Now;
            Status.Phase = HordePhase.Running; Status.RunSeconds = 0f;
            foreach (var m in Status.Members) if (m.State == HordeMemberState.Joined) m.Slot = m.Aid == _myAid ? 0 : -1;
            _endAt = Now + RunLength;
            StatusChanged?.Invoke();
            RunStarted?.Invoke(Run);
        }

        private void AddPlayer(int slot, uint aid, string name, int job, float x, float y)
        {
            var p = World.Players[slot];
            p.Present = true; p.Aid = aid; p.Name = name; p.X = p.PrevX = x; p.Y = p.PrevY = y; p.JobId = job; p.Level = 1;
            p.MaxHp = p.Hp = _c.Rules.StartHp; p.Exp = 0; p.ExpToNext = ExpNeed(1); p.Flags = HordePlayerFlags.None; p.Facing = 270f;
            p.Weapons.Clear(); p.Passives.Clear();
            if (_c.Rules.StartWeapon > 0) p.Weapons.Add(new HordeOwned { DefId = _c.Rules.StartWeapon, Level = 1 });
        }

        private int ExpNeed(int level)
        {
            var t = _c.Rules.ExpTable; int i = level - 1;
            float v = i < t.Length ? t[i] : t[t.Length - 1] * (float)Math.Pow(1f + _c.Rules.ExpGrowthPct / 100f, i - t.Length + 1);
            return Math.Max(1, (int)Math.Round(v / Math.Max(ExpFactor, 0.01f)));
        }

        public void Poll()
        {
            double now = Now; float dt = (float)Math.Min(0.25, Math.Max(0, now - _lastPoll)); _lastPoll = now;
            if (Status.Phase == HordePhase.Gathering)
            {
                Status.SecondsLeft = (float)Math.Max(0, _gatherEnd - now);
                if (now >= _gatherEnd)
                {
                    var me = Status.Member(_myAid);
                    if (me != null && me.State == HordeMemberState.Joined) Begin(false);
                    else { Status.Phase = HordePhase.Idle; Status.Members.Clear(); StatusChanged?.Invoke(); } // nobody here plays it: the stand-in has no other players
                }
            }
            if (_rewardAt > 0 && now >= _rewardAt)
            {
                _rewardAt = -1;
                var mine = _pendingResults?.Mine();
                Rewarded?.Invoke(new HordeRewardNote { RunId = _pendingResults != null ? _pendingResults.RunId : 0, BaseExp = 1234, JobExp = 567, BaseShare = mine != null ? mine.BaseShare : 0, JobShare = mine != null ? mine.JobShare : 0 });
            }
            if (!InRun) return;
            var my = World.Players[0];
            // design note (2026-10-05): the run PAUSES while a card / job offer is open (as h9move's protocol-2 engine does): nothing moves, no
            // tick, the end timer waits; at the pause limit (every run, solo too) the host takes the first card and the limit drops
            World.Paused = _offer != null || _held; // (as protocol 2's snapshot says it; w15hfx: + the menu's pause)
            if (_offer != null || _held)
            {
                PausedSeconds += dt; _endAt += dt;
                if (_held) _offerUntil += dt; // (w15hfx: an open choice waits while the menu's pause holds)
                World.PrevTick = World.Tick; World.PrevTickAt = World.TickAt = now; // a "same tick" snapshot: the drawing holds still
                my.Moving = false;
                if (_offer != null && now > _offerUntil) Apply(_offer, 0, true);
                return;
            }
            // my movement (in the fake the prediction is the authority)
            float spd = _c.Rules.MoveSpeed;
            if (!my.Fallen)
            {
                MyX = Clamp(MyX + _mvx * spd * dt, Run.X0 + 0.5f, Run.X1 + 0.5f); MyY = Clamp(MyY + _mvy * spd * dt, Run.Y0 + 0.5f, Run.Y1 + 0.5f);
                if (_mvx * _mvx + _mvy * _mvy > 0.01f) my.Facing = (float)(Math.Atan2(_mvy, _mvx) * 180.0 / Math.PI);
            }
            my.Moving = !my.Fallen && _mvx * _mvx + _mvy * _mvy > 0.01f;
            _tickAcc += dt;
            bool any = false;
            while (_tickAcc >= 1.0 / HordeLimits.TickRate) { _tickAcc -= 1.0 / HordeLimits.TickRate; Tick(); any = true; if (!InRun) return; }
            if (any)
            {
                World.PrevTick = World.Tick; World.Tick = _tick; World.PrevTickAt = World.TickAt; World.TickAt = now;
                Status.RunSeconds = World.Seconds;
                SnapshotArrived?.Invoke();
                foreach (var e in _events) Happened?.Invoke(e);
                _events.Clear();
            }
            if (now >= _endAt) Finish(HordeOutcome.TimeUp, false);
        }

        private static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;

        private void Event(HordeEventKind k, int slot = -1, int target = -1, int def = 0, int value = 0, float x = 0, float y = 0, HordeEventFlags f = HordeEventFlags.None, float a = 0)
            => _events.Add(new HordeEvent { Kind = k, Slot = slot, Target = target, DefId = def, Value = value, X = x, Y = y, Flags = f, Tick = _tick, A = a });

        private void Tick()
        {
            _tick++;
            float dt = 1f / HordeLimits.TickRate, secs = _tick * dt;
            var my = World.Players[0];
            foreach (var p in World.Players) { p.PrevX = p.X; p.PrevY = p.Y; }
            my.X = MyX; my.Y = MyY;
            if (Ally && World.Players[1].Present)
            {
                var al = World.Players[1]; float dx = my.X - 2f - al.X, dy = my.Y - al.Y; float d = (float)Math.Sqrt(dx * dx + dy * dy);
                al.Moving = d > 0.6f;
                if (al.Moving) { float s = Math.Min(d, _c.Rules.MoveSpeed * dt); al.X += dx / d * s; al.Y += dy / d * s; al.Facing = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI); }
            }
            // the scripted late ally (h9flow: the entry effect), the scripted elite (the boss bar and banner)
            if (Ally && LateAllyAt >= 0f && !_lateScripted && secs >= LateAllyAt)
            {
                _lateScripted = true;
                var start = _c.StartJob();
                AddPlayer(1, 0xFFFF0001u, "Bot Ally", start != null ? start.JobId : 0, my.X + 2f, my.Y);
                World.Players[1].Level = my.Level; World.Players[1].Exp = my.Exp; World.Players[1].ExpToNext = my.ExpToNext; // the party's shared level and bar
                Event(HordeEventKind.PlayerJoined, 1, -1, 0, 0, my.X + 2f, my.Y, HordeEventFlags.Late);
            }
            if (BossAt >= 0f && !_bossScripted && secs >= BossAt)
            {
                _bossScripted = true;
                EnemyDef elite = null; foreach (var ed in _c.Enemies) if (ed.Rank == EnemyRank.Elite) { elite = ed; break; }
                for (int i = 0; elite != null && i < World.Enemies.Length; i++)
                {
                    var e = World.Enemies[i]; if (e.Alive) continue;
                    e.Alive = true; e.Serial = ++_serial; e.DefId = elite.Id; e.Hp = 255; e.Flags = HordeEnemyFlags.None; e.SinceTick = _tick;
                    e.X = e.PrevX = Clamp(my.X + 10f, Run.X0, Run.X1 + 1); e.Y = e.PrevY = Clamp(my.Y + 6f, Run.Y0, Run.Y1 + 1);
                    _hp[i] = elite.Hp;
                    Event(HordeEventKind.BossAppeared, -1, i, elite.Id, 0, e.X, e.Y, HordeEventFlags.Elite);
                    break;
                }
            }
            // the scripted fall and the rise
            if (FallAt >= 0 && !_fellScripted && secs >= FallAt && !my.Fallen) { _fellScripted = true; my.Hp = 0; my.Flags |= HordePlayerFlags.Fallen; my.Revive = 0; Event(HordeEventKind.Fell, 0); }
            if (my.Fallen)
            {
                my.Revive = Math.Min(1f, my.Revive + dt / Math.Max(ReviveSeconds, 0.1f));
                if (my.Revive >= 1f) { my.Flags &= ~HordePlayerFlags.Fallen; my.Hp = my.MaxHp / 2; my.Revive = 0; _hurtGrace = 2f; Event(HordeEventKind.Rose, 0, Ally ? 1 : -1); }
            }
            // enemies: keep the number up, walk at the nearest standing player, touch
            var normals = Crowd(); // client wave 17 (h17boss): as before point 23, see Crowd
            int alive = 0; foreach (var e in World.Enemies) if (e.Alive) alive++;
            for (int i = 0; i < World.Enemies.Length && alive < Math.Min(TargetEnemies, HordeLimits.MaxEnemies) && normals.Count > 0; i++)
            {
                var e = World.Enemies[i]; if (e.Alive) continue;
                var def = normals[_rnd.Next(normals.Count)];
                double ang = _rnd.NextDouble() * Math.PI * 2; float r = 9f + (float)_rnd.NextDouble() * 5f;
                e.Alive = true; e.Serial = ++_serial; e.DefId = def.Id; e.Hp = 255; e.Flags = HordeEnemyFlags.None; e.SinceTick = _tick;
                e.X = e.PrevX = Clamp(my.X + (float)Math.Cos(ang) * r, Run.X0, Run.X1 + 1); e.Y = e.PrevY = Clamp(my.Y + (float)Math.Sin(ang) * r, Run.Y0, Run.Y1 + 1);
                _hp[i] = def.Hp; alive++;
            }
            World.EnemiesAlive = alive;
            if (_hurtGrace > 0) _hurtGrace -= dt;
            for (int i = 0; i < World.Enemies.Length; i++)
            {
                var e = World.Enemies[i]; if (!e.Alive) continue;
                var def = _c.Enemy(e.DefId); e.PrevX = e.X; e.PrevY = e.Y;
                float dx = my.X - e.X, dy = my.Y - e.Y, d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (my.Fallen) { if (d > 0.1f) { e.X -= dx / d * def.Speed * dt * 0.3f; e.Y -= dy / d * def.Speed * dt * 0.3f; } continue; }
                if (d > def.Radius + _c.Rules.BodyRadius) { float s = Math.Min(d, def.Speed * dt * (_openOffer >= 0 ? 0.5f : 1f)); e.X += dx / d * s; e.Y += dy / d * s; }
                else if (EnemiesHurt && _hurtGrace <= 0 && _openOffer < 0)
                {
                    int dmg = Math.Max(1, def.Damage); my.Hp = Math.Max(0, my.Hp - dmg); _hurtGrace = _c.Rules.HurtGraceSeconds;
                    Event(HordeEventKind.PlayerHit, 0, -1, def.Id, dmg, my.X, my.Y);
                    if (my.Hp <= 0) { my.Flags |= HordePlayerFlags.Fallen; my.Revive = 0; Event(HordeEventKind.Fell, 0); }
                }
            }
            // my weapons
            if (!my.Fallen)
                foreach (var w in my.Weapons)
                {
                    var def = _c.Weapon(w.DefId); if (def == null || def.Levels.Length == 0) continue;
                    var lv = def.Levels[Math.Min(w.Level, def.Levels.Length) - 1];
                    _cool.TryGetValue(w.DefId, out float cd); cd -= dt; if (cd > 0) { _cool[w.DefId] = cd; continue; }
                    _cool[w.DefId] = Math.Max(0.15f, lv.Cooldown);
                    int target = Nearest(my.X, my.Y, Math.Max(lv.Range, 3f) + 2f);
                    if (def.Kind == WeaponKind.Bolt)
                    {
                        if (target < 0) continue;
                        for (int n = 0; n < Math.Max(1, lv.Count); n++)
                        {
                            var t = World.Enemies[target]; float dx = t.X - my.X, dy = t.Y - my.Y, dd = Math.Max(0.01f, (float)Math.Sqrt(dx * dx + dy * dy));
                            float spread = (n - (lv.Count - 1) / 2f) * lv.Spread * (float)Math.PI / 180f; float ca = (float)Math.Cos(spread), sa = (float)Math.Sin(spread);
                            float ux = dx / dd * ca - dy / dd * sa, uy = dx / dd * sa + dy / dd * ca, sp = Math.Max(lv.Speed, 4f);
                            var fx = new HordeFxView { Id = _nextFxId++, Kind = HordeFxKind.Shot, OwnerSlot = 0, DefId = def.Id, Level = w.Level, X = my.X, Y = my.Y, VX = ux * sp, VY = uy * sp, StartTick = _tick, EndTick = _tick + (int)(Math.Max(lv.Range, 4f) / sp * HordeLimits.TickRate), Target = -1 };
                            World.Fx.Add(fx); FxBegan?.Invoke(fx);
                        }
                    }
                    else
                    {
                        float area = Math.Max(Math.Max(lv.Area, lv.Range), 2f);
                        if (def.Kind == WeaponKind.Orbiter || def.Kind == WeaponKind.Aura) area = Math.Max(area, 2.5f);
                        if (def.Kind == WeaponKind.MeleeArc)
                        {
                            // client wave 15 (w15hfx), point 14: a swing carries the engine's fields (A aim in degrees, B arc, C reach), as HordeSim sends them
                            float aim = target >= 0 ? (float)(Math.Atan2(World.Enemies[target].Y - my.Y, World.Enemies[target].X - my.X) * 180.0 / Math.PI) : my.Facing;
                            _events.Add(new HordeEvent { Kind = HordeEventKind.Swing, Slot = 0, Target = -1, DefId = def.Id, Value = w.Level, X = my.X, Y = my.Y, Tick = _tick, A = aim, B = lv.Arc > 0 ? lv.Arc : 90f, C = Math.Max(lv.Range, 2f) });
                        }
                        else Event(HordeEventKind.Nova, 0, -1, def.Id, 0, my.X, my.Y, HordeEventFlags.None, area);
                        for (int i = 0; i < World.Enemies.Length; i++) { var e = World.Enemies[i]; if (e.Alive && Dist(e.X, e.Y, my.X, my.Y) <= area) Damage(i, Math.Max(1, lv.Damage), def.Id); }
                    }
                }
            // shots in flight
            for (int f = World.Fx.Count - 1; f >= 0; f--)
            {
                var fx = World.Fx[f];
                if (fx.Kind != HordeFxKind.Shot) continue;
                float t = (_tick - fx.StartTick) / (float)HordeLimits.TickRate; float x = fx.X + fx.VX * t, y = fx.Y + fx.VY * t;
                int hit = -1; for (int i = 0; i < World.Enemies.Length; i++) { var e = World.Enemies[i]; if (e.Alive && Dist(e.X, e.Y, x, y) < 0.8f) { hit = i; break; } }
                if (hit >= 0 || _tick >= fx.EndTick)
                {
                    if (hit >= 0) { var wd = _c.Weapon(fx.DefId); var lv = wd.Levels[Math.Min(fx.Level, wd.Levels.Length) - 1]; Damage(hit, Math.Max(1, lv.Damage), fx.DefId); }
                    World.Fx.RemoveAt(f); FxEnded?.Invoke(fx, hit >= 0);
                }
            }
            // pickups
            if (PullLikeEngine) { PullPickups(); return; } // wave 27 (wfix)
            for (int i = World.Pickups.Count - 1; i >= 0; i--)
            {
                var p = World.Pickups[i];
                if (my.Fallen || Dist(p.X, p.Y, my.X, my.Y) > Math.Max(_c.Rules.PickupRadius, PickupRadius)) continue;
                World.Pickups.RemoveAt(i);
                Event(HordeEventKind.PickupTaken, 0, p.Id, p.DefId, p.Value, p.X, p.Y);
                var pd = _c.Pickup(p.DefId);
                if (pd != null && pd.Kind == PickupKind.Heal) my.Hp = Math.Min(my.MaxHp, my.Hp + my.MaxHp * pd.Value / 100);
                else GainExp(p.Value);
            }
        }

        private readonly int[] _hp = new int[HordeLimits.MaxEnemies];

        // wave 27 (wfix)
        /// <summary>Pickups as the engine moves them (HordeSim.StepPickups / TakePickup), for the w9feel smoke: a pickup within a player's
        /// reach (<see cref="HordeWorldDraw.Reach"/>, the engine's radius) is pulled (the view's PulledBy / PullTick, as protocol 2's snapshots
        /// carry them; X / Y stay where it was dropped) and flies to that player at RulesDef.PickupPullSpeed + PickupPullAccel × seconds; it is
        /// taken within 0.6 cell; a Magnet pulls every EXP pickup lying around. Off (the older smokes): a pickup within the stand-in's own reach
        /// is taken at once, as before.</summary>
        public bool PullLikeEngine { get; set; }
        private readonly Dictionary<int, (float x, float y)> _pulled = new Dictionary<int, (float, float)>();

        private void PullPickups()
        {
            const float Grab = 0.6f; // HordeSim.StepPickups: Units(0.6)
            for (int i = World.Pickups.Count - 1; i >= 0; i--)
            {
                var p = World.Pickups[i];
                HordePlayerView taker = null;
                if (p.PulledBy >= 0 && p.PulledBy < World.Players.Length && World.Players[p.PulledBy].Present && !World.Players[p.PulledBy].Fallen) taker = World.Players[p.PulledBy];
                else
                {
                    p.PulledBy = -1; _pulled.Remove(p.Id); float best = float.MaxValue;
                    foreach (var pl in World.Players)
                    {
                        if (!pl.Present || pl.Fallen) continue;
                        float d = Dist(pl.X, pl.Y, p.X, p.Y);
                        if (d <= HordeWorldDraw.Reach(_c, pl) && d < best) { best = d; taker = pl; }
                    }
                    if (taker != null) { p.PulledBy = taker.Slot; p.PullTick = _tick; }
                }
                if (taker == null) continue;
                if (!_pulled.TryGetValue(p.Id, out var at)) at = (p.X, p.Y);
                float dd = Dist(taker.X, taker.Y, at.x, at.y);
                if (dd > Grab)
                {
                    float sp = (_c.Rules.PickupPullSpeed + _c.Rules.PickupPullAccel * Math.Max(0, _tick - p.PullTick) / (float)HordeLimits.TickRate) / HordeLimits.TickRate;
                    float s = Math.Min(sp, dd);
                    _pulled[p.Id] = (at.x + (taker.X - at.x) / dd * s, at.y + (taker.Y - at.y) / dd * s);
                    continue;
                }
                World.Pickups.RemoveAt(i); _pulled.Remove(p.Id);
                Event(HordeEventKind.PickupTaken, taker.Slot, p.Id, p.DefId, p.Value, at.x, at.y);
                var pd = _c.Pickup(p.DefId);
                if (pd != null && pd.Kind == PickupKind.Heal) taker.Hp = Math.Min(taker.MaxHp, taker.Hp + taker.MaxHp * pd.Value / 100);
                else if (pd != null && pd.Kind == PickupKind.Magnet)
                {
                    foreach (var q in World.Pickups) { var qd = _c.Pickup(q.DefId); if (qd != null && qd.Kind == PickupKind.Exp && q.PulledBy < 0) { q.PulledBy = taker.Slot; q.PullTick = _tick; } }
                }
                else GainExp(p.Value);
            }
        }
        /// <summary>Where the stand-in has a pulled pickup now (cells); its view's X / Y otherwise (the w9feel smoke).</summary>
        public (float x, float y) PickupAt(HordePickupView p) => _pulled.TryGetValue(p.Id, out var at) ? at : (p.X, p.Y);

        /// <summary>Client wave 17 (h17boss): the stand-in's crowd: the normal kinds the waves bring and the adds of the waves' elites and bosses and of
        /// the final boss (every normal kind of the tables until point 23; its four new bosses are dealt among the marks at run time and are
        /// in no wave, so their adds and the lanes' runners stay out: here they would be 1 spawn in 4, and the last of the sprite pre-build).</summary>
        private List<EnemyDef> Crowd()
        {
            if (_crowd != null) return _crowd;
            bool InWaves(int id) => _c.Waves.Exists(w => w.EnemyId == id);
            _crowd = new List<EnemyDef>();
            foreach (var e in _c.Enemies)
                if (e.Rank == EnemyRank.Normal && (InWaves(e.Id) || _c.Enemies.Exists(b => (InWaves(b.Id) || b.Id == _c.Rules.FinalBossEnemyId) && Array.Exists(b.Abilities, a => a.Kind == AbilityKind.SummonAdds && a.EnemyId == e.Id))))
                    _crowd.Add(e);
            return _crowd;
        }
        private List<EnemyDef> _crowd;

        private void Damage(int slot, int dmg, int weaponId)
        {
            var e = World.Enemies[slot]; var def = _c.Enemy(e.DefId);
            bool crit = _rnd.NextDouble() < _c.Rules.CritChance / 100.0; if (crit) dmg = dmg * (int)_c.Rules.CritDamagePct / 100;
            _hp[slot] -= dmg; _damageBy[0] += dmg;
            bool kill = _hp[slot] <= 0;
            e.Hp = (byte)Math.Max(0, Math.Min(255, 255 * _hp[slot] / Math.Max(1, def.Hp)));
            Event(HordeEventKind.Hit, 0, slot, weaponId, dmg, e.X, e.Y, (crit ? HordeEventFlags.Crit : 0) | (kill ? HordeEventFlags.Kill : 0));
            if (!kill) return;
            e.Alive = false; _killsBy[0]++; World.Players[0].Kills++;
            Event(HordeEventKind.EnemyDied, 0, slot, def.Id, 0, e.X, e.Y);
            if (World.Pickups.Count < HordeLimits.MaxPickups)
            {
                PickupDef exp = null; foreach (var pd in _c.Pickups) if (pd.Kind == PickupKind.Exp && pd.Value <= Math.Max(1, def.Exp) && (exp == null || pd.Value > exp.Value)) exp = pd;
                if (exp != null) World.Pickups.Add(new HordePickupView { Id = _nextPickupId++, DefId = exp.Id, X = e.X, Y = e.Y, Value = Math.Max(1, def.Exp) });
            }
        }

        private int Nearest(float x, float y, float range)
        {
            int best = -1; float bd = range;
            for (int i = 0; i < World.Enemies.Length; i++) { var e = World.Enemies[i]; if (!e.Alive) continue; float d = Dist(e.X, e.Y, x, y); if (d < bd) { bd = d; best = i; } }
            return best;
        }
        private static float Dist(float ax, float ay, float bx, float by) { float dx = ax - bx, dy = ay - by; return (float)Math.Sqrt(dx * dx + dy * dy); }

        private void GainExp(int v)
        {
            var my = World.Players[0];
            my.Exp += v;
            if (World.Players[1].Present) { World.Players[1].Exp = my.Exp; World.Players[1].ExpToNext = my.ExpToNext; } // one shared EXP bar (design note, 2026-10-05)
            World.PartyLevel = my.Level; World.PartyExp = my.Exp; World.PartyExpToNext = my.ExpToNext; // (as protocol 2's snapshot carries them)
            while (my.Exp >= my.ExpToNext && _offer == null)
            {
                my.Exp -= my.ExpToNext; my.Level++; my.ExpToNext = ExpNeed(my.Level); my.MaxHp += _c.Rules.HpPerLevel; my.Hp += _c.Rules.HpPerLevel;
                World.PartyLevel = my.Level; World.PartyExp = my.Exp; World.PartyExpToNext = my.ExpToNext;
                if (World.Players[1].Present) { World.Players[1].Level = my.Level; World.Players[1].Exp = my.Exp; World.Players[1].ExpToNext = my.ExpToNext; }
                Event(HordeEventKind.LevelUp, 0, -1, 0, my.Level, my.X, my.Y);
                var job = _c.Job(my.JobId);
                var marks = _c.Rules.JobMarks;
                if (job != null && _tier < marks.Length && my.Level >= marks[_tier] && job.Next.Length > 0) OfferJob(job);
                else OfferCards();
            }
        }

        private void OfferJob(JobDef job)
        {
            var cards = new List<HordeCard>(); foreach (var n in job.Next) cards.Add(new HordeCard { Kind = HordeCardKind.Job, DefId = n, Level = 1 });
            Open(new HordeOffer { Id = ++_offerId, IsJob = true, Seconds = PauseLimit, Cards = cards.ToArray() });
        }

        private void OfferCards()
        {
            var my = World.Players[0]; var cards = new List<HordeCard>();
            var opened = new List<int>(); var openedP = new List<int>();
            for (var j = _c.Job(my.JobId); j != null;) { opened.AddRange(j.Weapons); openedP.AddRange(j.Passives); j = Parent(j); }
            foreach (var w in my.Weapons) { var d = _c.Weapon(w.DefId); if (d != null && w.Level < d.Levels.Length && cards.Count < 3) cards.Add(new HordeCard { Kind = HordeCardKind.WeaponLevel, DefId = w.DefId, Level = w.Level + 1 }); }
            foreach (var id in opened) if (cards.Count < 3 && !my.Weapons.Exists(o => o.DefId == id) && _c.Weapon(id) != null) cards.Add(new HordeCard { Kind = HordeCardKind.NewWeapon, DefId = id, Level = 1 });
            foreach (var id in openedP) if (cards.Count < 3 && !my.Passives.Exists(o => o.DefId == id) && _c.Passive(id) != null) cards.Add(new HordeCard { Kind = HordeCardKind.NewPassive, DefId = id, Level = 1 });
            while (cards.Count < 3) cards.Add(new HordeCard { Kind = HordeCardKind.Stat, DefId = _rnd.Next(6), Level = _c.Rules.StatCardPoints });
            Open(new HordeOffer { Id = ++_offerId, IsJob = false, Seconds = PauseLimit, Cards = cards.ToArray() });
        }

        private JobDef Parent(JobDef j) { foreach (var p in _c.Jobs) if (Array.IndexOf(p.Next, j.JobId) >= 0) return p; return null; }

        private void Open(HordeOffer o)
        {
            // client wave 15 (w15horde), the design's point 26, as the host's engine when SoloNoLimit is set: alone, no time limit (Seconds 0:
            // no bar, no auto-pick). Off by default: the older smokes on the stand-in were written for a limit in every run.
            bool alone = SoloNoLimit; for (int i = 1; i < World.Players.Length && alone; i++) if (World.Players[i].Present) alone = false;
            if (alone) o.Seconds = 0f;
            _offer = o; _openOffer = o.Id; _offerUntil = alone ? double.MaxValue : Now + o.Seconds;
            World.Players[0].Flags |= HordePlayerFlags.Choosing | HordePlayerFlags.Safe;
            Offered?.Invoke(o);
        }

        private void Apply(HordeOffer o, int i, bool limitMet = false)
        {
            var my = World.Players[0]; var k = o.Cards[i];
            _offer = null; _openOffer = -1; my.Flags &= ~(HordePlayerFlags.Choosing | HordePlayerFlags.Safe);
            if (limitMet) { PauseLimit = Math.Max(PauseLimitFloor, PauseLimit - PauseLimitStep); LimitsMet++; } // the next offers carry the shorter limit
            OfferClosed?.Invoke(o.Id);
            switch (k.Kind)
            {
                case HordeCardKind.NewWeapon: my.Weapons.Add(new HordeOwned { DefId = k.DefId, Level = 1 }); break;
                case HordeCardKind.WeaponLevel: { int at = my.Weapons.FindIndex(w => w.DefId == k.DefId); if (at >= 0) my.Weapons[at] = new HordeOwned { DefId = k.DefId, Level = k.Level }; break; }
                case HordeCardKind.NewPassive: my.Passives.Add(new HordeOwned { DefId = k.DefId, Level = 1 }); break;
                case HordeCardKind.Stat: my.Stats[k.DefId] += k.Level; break;
                case HordeCardKind.Job:
                {
                    var j = _c.Job(k.DefId);
                    my.JobId = k.DefId; _tier++;
                    if (j != null && j.SignatureWeapon > 0 && !my.Weapons.Exists(w => w.DefId == j.SignatureWeapon)) my.Weapons.Add(new HordeOwned { DefId = j.SignatureWeapon, Level = 1 });
                    if (World.Players[1].Present && j != null) World.Players[1].JobId = j.JobId;
                    Event(HordeEventKind.JobChanged, 0, -1, j != null ? j.SignatureWeapon : 0, k.DefId, my.X, my.Y);
                    break;
                }
            }
            Event(HordeEventKind.CardTaken, 0, -1, k.DefId, k.Level, 0, 0, HordeEventFlags.None, (float)k.Kind);
            if (my.Exp >= my.ExpToNext) GainExp(0);
        }

        private void Finish(HordeOutcome outcome, bool left)
        {
            var my = World.Players[0];
            var r = new HordeResults { RunId = Status.RunId, Outcome = outcome, Seconds = World.Seconds, MySlot = 0 };
            for (int s = 0; s < World.Players.Length; s++)
            {
                var p = World.Players[s]; if (!p.Present) continue;
                r.Players.Add(new HordePlayerResult
                {
                    Slot = s, Aid = p.Aid, Name = p.Name, Level = p.Level, JobId = p.JobId, Kills = s == 0 ? _killsBy[0] : 0, Damage = s == 0 ? _damageBy[0] : 0,
                    Seconds = World.Seconds, Falls = s == 0 && _fellScripted ? 1 : 0, Score = (s == 0 ? _killsBy[0] : 0) + p.Level * 10,
                    BaseShare = s == 0 && World.Seconds >= 10f ? 1500 : 0, JobShare = s == 0 && World.Seconds >= 10f ? 1500 : 0,
                    Reward = s == 0 && World.Seconds >= 10f ? HordeRewardState.Pending : HordeRewardState.None, Left = left && s == 0,
                });
            }
            if (_offer != null) { OfferClosed?.Invoke(_offer.Id); _offer = null; _openOffer = -1; }
            InRun = false; Status.Phase = HordePhase.Idle; Status.Members.Clear();
            _pendingResults = r; if (r.Mine() != null && r.Mine().Reward == HordeRewardState.Pending) _rewardAt = Now + 1.5;
            StatusChanged?.Invoke();
            Ended?.Invoke(r);
        }

        /// <summary>The smoke: the link drops (Lost).</summary>
        public void FakeLost() => Lost?.Invoke();
        /// <summary>The smoke (h9flow): my HP set to a share of its max (the HUD's low-HP flash).</summary>
        public void FakeSetHp(float share) { var my = World.Players[0]; if (my.Present) my.Hp = Math.Max(1, (int)(my.MaxHp * share)); }
    }
}
