#nullable disable
using System;
using Loki.Horde.Net;

namespace Loki.Horde
{
    // ====================================================================================================================
    // Client wave 18 (h18rules): two of the design's points after wave 17 (design notes), the engine's part.
    //
    // Point 23, the Horde menu's pause. design note: "When I open up the horde menu ... that should pause the game. I should not need to press a
    // pause button on that menu." and, for a run with others: "opening up the menu does not immediately pause it. I have to click a pause
    // button which then does a countdown of three seconds before pausing the game." + "yes, to both" (pressing Pause again during the
    // countdown cancels it; resuming counts down too). So <see cref="Hold"/>: ALONE (no other player present and not away) a pause or a
    // resume happens at once, as in wave 15; WITH OTHERS it starts a <see cref="PauseCountSeconds"/> countdown on the wall clock (the
    // world runs during a pause's countdown and stands during a resume's), shown to everyone with who asked (H_SNAP's rules block,
    // <see cref="HordeRulesWire"/>); the same request again while it runs cancels it (the button reads "Cancel" then). A countdown ends
    // when its asker leaves or loses the link. The holder of the pause it makes is its asker (HeldBy, HordePlayerFlags.Held).
    //
    // Point 27, Back Slide (TF_BACKSLIDING, h17cards' Dash weapon that goes off when hit). design note: "Leave behind on the path that the player
    // takes a small damaging area that explodes after a short moment." and "when you dodge, you turn briefly invisible and invulnerable.
    // And you gain a brief burst of movement speed. Increase the cooldown of the backstep and make this extra effect last about 1.5
    // seconds." The dash is h17cards' (HordeSim.Cards.cs CardsHurt, StartDash), unchanged; here (one hook there, one in StepPlayers):
    //   the TRAIL: a player Warning fx (the Meteor's: drawn as a ring that fills, then a Strike) every <see cref="SlideStepCells"/> of the
    //     path, the first where the hit came, the last where the dash ends; each strikes the level's Damage within Area after Delay s;
    //   UNSEEN, UNHURT, FASTER from the dodge until Duration s after the dash's end: enemies lose the runner (NearestStanding, as
    //     Cloaking), nothing hurts it (SafeUntil: every HurtPlayer), the level's Buffs (+50 % move speed) hold; the snapshot marks it
    //     (HordePlayerFlags.Vanished: the client draws it see-through with a speed trail).
    // Every number is in the tables (Content/HordeCards.cs, Back Slide's levels); Loki's own, never an official value.
    // ====================================================================================================================

    /// <summary>Client wave 18 (h18rules): what the last <see cref="HordeSim.Hold"/> did (Loki Host's log, the tests).</summary>
    public enum HordeHoldResult : byte { None = 0, Paused = 1, Resumed = 2, PauseCounting = 3, ResumeCounting = 4, Cancelled = 5 }

    public sealed partial class HordeSim
    {
        // ================================================================================================= point 23: the pause

        /// <summary>Seconds a pause or a resume waits when others are in the run (design note: "a countdown of three seconds").</summary>
        public const float PauseCountSeconds = 3f;
        /// <summary>The player whose pause / resume is counting down (-1: none), which of the two, and the wall tick it happens at.</summary>
        public int PauseCountBy { get; private set; } = -1;
        public bool PauseCountToPause { get; private set; }
        public int PauseCountEnd { get; private set; }
        /// <summary>Wall ticks until the countdown's pause / resume (0: none running).</summary>
        public int PauseCountTicksLeft => PauseCountBy >= 0 ? Math.Max(0, PauseCountEnd - WallTick) : 0;
        public HordeHoldResult LastHold { get; private set; }

        /// <summary>True when <paramref name="slot"/> is not alone: another player is present and not away (its link is up).</summary>
        public bool PauseCountsDown(int slot)
        {
            foreach (var q in Players) if (q.Present && q.Slot != slot && !q.Away) return true;
            return false;
        }

        private void CancelPauseCount() { PauseCountBy = -1; }

        /// <summary>Hold's rule with others in the run: a 3 s countdown, cancelled by the same request again (either way round: a pause
        /// asked during a resume's countdown, or a resume during a pause's, cancels it too).</summary>
        private bool HoldCountdown(int slot, bool on)
        {
            bool counting = PauseCountBy >= 0;
            if (counting && PauseCountToPause != on) { CancelPauseCount(); LastHold = HordeHoldResult.Cancelled; return true; } // (the other way round)
            if (on)
            {
                if (HeldBy >= 0) return false;                                                          // already paused
                if (counting) { CancelPauseCount(); LastHold = HordeHoldResult.Cancelled; return true; } // Pause again: cancels
                PauseCountBy = slot; PauseCountToPause = true; PauseCountEnd = WallTick + Math.Max(1, Ticks(PauseCountSeconds));
                LastHold = HordeHoldResult.PauseCounting; return true;
            }
            if (HeldBy < 0) return false;                                                               // nothing to resume
            if (counting) { CancelPauseCount(); LastHold = HordeHoldResult.Cancelled; return true; }     // Resume again: cancels
            PauseCountBy = slot; PauseCountToPause = false; PauseCountEnd = WallTick + Math.Max(1, Ticks(PauseCountSeconds));
            LastHold = HordeHoldResult.ResumeCounting; return true;
        }

        /// <summary>Every Step (paused or not), first: the countdown runs on the wall clock; it ends with its asker, or when there is
        /// nothing left to do (a resume's pause already over); at its end the run pauses (its asker holds it) or goes on.</summary>
        private void StepPauseCount()
        {
            if (PauseCountBy < 0) return;
            var p = Player(PauseCountBy);
            if (p == null || p.Away || (PauseCountToPause ? HeldBy >= 0 : HeldBy < 0)) { CancelPauseCount(); return; }
            if (WallTick < PauseCountEnd) return;
            HeldBy = PauseCountToPause ? PauseCountBy : -1;
            CancelPauseCount();
        }

        // ================================================================================================= point 27: Back Slide

        /// <summary>Cells of the dash between two trail areas (Loki's own: "one every ~1 cell of the dash").</summary>
        public const float SlideStepCells = 1f;
        /// <summary>The speed burst's SimBuff id (WeaponId, SourceSlot -1: never a weapon's, not "its own aura" for h17pass's OwnZone).</summary>
        public const int SlideBuffId = -1800;
        private readonly int[] _slideUntil = new int[HordeLimits.MaxPlayers], _slideX = new int[HordeLimits.MaxPlayers], _slideY = new int[HordeLimits.MaxPlayers];
        private readonly OwnedWeapon[] _slideWeapon = new OwnedWeapon[HordeLimits.MaxPlayers];

        /// <summary>Back Slide (and its branches): the Dash weapon that goes off when its owner is hit.</summary>
        public static bool IsSlide(WeaponDef w) => w != null && w.Kind == WeaponKind.Dash && w.DashOn == DashTrigger.WhenHit;

        /// <summary>Unseen, unhurt and faster after a Back Slide (enemies lose the runner: NearestStanding).</summary>
        public bool SlideHidden(SimPlayer p) => p != null && p.Slot >= 0 && p.Slot < _slideUntil.Length && _slideUntil[p.Slot] > Tick;

        /// <summary>The tick a runner's Back Slide time ends (0: none yet; the tests).</summary>
        public int SlideUntil(int slot) => slot >= 0 && slot < _slideUntil.Length ? _slideUntil[slot] : 0;

        /// <summary>CardsHurt's hook, right after a WhenHit dash began (StartDash set the dash): the first trail area where the hit came,
        /// then unseen, unhurt and faster until Duration s after the dash's end.</summary>
        private void SlideBegan(SimPlayer p, OwnedWeapon ow, WeaponLevel lv)
        {
            if (!IsSlide(ow.Def)) return;
            int s = p.Slot;
            _slideWeapon[s] = ow; _slideX[s] = p.X; _slideY[s] = p.Y;
            SlideArea(p, ow, lv, p.X, p.Y);
            if (lv.Duration <= 0) return; // (no unseen time in the level: the dash and its trail only)
            int until = Math.Max(CS(p).DashUntil, Tick) + Math.Max(1, Ticks(lv.Duration));
            _slideUntil[s] = until;
            p.SafeUntil = Math.Max(p.SafeUntil, until);
            if (lv.Buffs != null && lv.Buffs.Length > 0)
            {
                var b = p.Buffs.Find(x => x.WeaponId == SlideBuffId && x.SourceSlot == -1);
                if (b == null) p.Buffs.Add(new SimBuff { Mods = lv.Buffs, Until = until, SourceSlot = -1, WeaponId = SlideBuffId });
                else { b.Mods = lv.Buffs; b.Until = until; }
                Recalc(p, false);
            }
        }

        /// <summary>StepPlayers' hook (a standing player, after h17cards' step moved its dash): a trail area every SlideStepCells of the
        /// path; at the dash's end the last one (when it is half a step or more from the one before).</summary>
        private void StepSlide(SimPlayer p)
        {
            int s = p.Slot; var ow = _slideWeapon[s];
            if (ow == null) return;
            if (!p.Weapons.Contains(ow)) { _slideWeapon[s] = null; return; }
            var lv = ow.Lv; var st = CS(p);
            bool dashing = st.DashUntil > Tick && st.DashWeapon == ow;
            int step = Units(SlideStepCells);
            long d2 = Dist2(p.X, p.Y, _slideX[s], _slideY[s]);
            if (d2 >= (long)step * step || (!dashing && d2 * 4 >= (long)step * step))
            {
                SlideArea(p, ow, lv, p.X, p.Y);
                _slideX[s] = p.X; _slideY[s] = p.Y;
            }
            if (!dashing) _slideWeapon[s] = null;
        }

        /// <summary>One trail area: a player Warning (StepFx strikes it at EndTick through StrikeArea: the level's damage, the hits'
        /// rules, a Strike event).</summary>
        private void SlideArea(SimPlayer p, OwnedWeapon ow, WeaponLevel lv, int x, int y)
        {
            if (lv.Damage <= 0) return;
            var f = NewFx(HordeFxKind.Warning);
            f.OwnerSlot = p.Slot; f.DefId = ow.Def.Id; f.Level = ow.Level; f.X = x; f.Y = y; f.Radius = AreaUnits(p, lv.Area > 0 ? lv.Area : 0.8f);
            f.EndTick = Tick + Math.Max(1, Ticks(lv.Delay > 0 ? lv.Delay : 0.6)); f.Weapon = ow; f.Lv = lv; f.BaseDamage = lv.Damage;
            BeginFx(f);
        }
    }

    public sealed partial class HordeSim
    {
        // ================================================================================================= point 31: clustered players

        /// <summary>Client wave 18 (h18rules), the design's point 31 ("If multiple players are close to one another, the amount of enemies that are
        /// spawned close to them needs to linearly scale up with their distance"): StepWaves' group of normal enemies comes to one standing
        /// player (as before: RandomStanding) and is <paramref name="close"/> = 1 + the sum over the OTHER standing players of max(0, 1 -
        /// distance / RulesDef.ClusterSpawnCells) times larger (each extra player's own share is the tables' SpawnMul, point 31's first
        /// half). Alone or with the rule off: null (SpawnGroup picks as before; the dice are drawn exactly as before) and 1.</summary>
        private SimPlayer SpawnAround(out double close)
        {
            close = 1;
            if (R.ClusterSpawnCells <= 0) return null;
            int standing = 0; foreach (var p in Players) if (p.Present && !p.Fallen) standing++;
            if (standing < 2) return null;
            var around = RandomStanding();
            close = Closeness(around);
            return around;
        }

        /// <summary>The EXP share the next spawn carries (StepWaves sets 1 / closeness around a closeness group), and the fractions of EXP
        /// not dropped yet (a share of 1 / 2 of a 1-EXP enemy drops 1 every other kill).</summary>
        private double _spawnExpScale = 1, _expCarry;

        /// <summary>Point 31: a closeness group is larger, but brings the EXP of the group it would have been (the party's level pacing,
        /// measured: design notes); the per-player share is the tables' (SpawnMul and PartyExpScale grow together).</summary>
        private int ExpOf(SimEnemy e)
        {
            int exp = e.Def.Exp;
            if (exp <= 0 || e.ExpScale >= 0.99999) return exp;
            double v = exp * e.ExpScale + _expCarry;
            int whole = (int)Math.Floor(v); _expCarry = v - whole;
            return whole;
        }

        /// <summary>1 + the sum over the other standing players of max(0, 1 - distance / ClusterSpawnCells), the sum at most
        /// RulesDef.ClusterSpawnMaxShares (design note, after wave 18's first pass: "Yes, cap the closeness bit": one extra share at most, so n
        /// stacked players make 2 n shares, not n x n) (point 31).</summary>
        public double Closeness(SimPlayer around)
        {
            double sum = 0, r = R.ClusterSpawnCells * U;
            if (around == null || r <= 0) return 1;
            foreach (var q in Players)
            {
                if (q == around || !q.Present || q.Fallen) continue;
                double d = Math.Sqrt(Dist2(q.X, q.Y, around.X, around.Y));
                if (d < r) sum += 1 - d / r;
            }
            if (R.ClusterSpawnMaxShares > 0 && sum > R.ClusterSpawnMaxShares) sum = R.ClusterSpawnMaxShares;
            return 1 + sum;
        }
    }

    public sealed partial class HordeSim
    {
        // ================================================================================================= the elites (design note, wave 18)

        /// <summary>The tick the next periodic elite may come (-1: not set yet), and the elites spawned so far (the tests, the log).</summary>
        private int _eliteNext = -1;
        public int ElitesSpawned { get; private set; }

        /// <summary>One of HordeContent.Elites (the periodic deal).</summary>
        public bool IsDealElite(int enemyId) { if (C.Elites != null) foreach (var el in C.Elites) if (el.EnemyId == enemyId) return true; return false; }

        /// <summary>design note (after wave 18's first pass): "make some Elite Enemies, that sit somewhere between a boss and a regular enemy. They
        /// should have an HP bar visible above them." Every Step after the waves: the first elite of the deal EliteFirstSeconds into the run,
        /// then one every EliteEverySeconds .. EliteEveryMaxSeconds; 1 + (players - 1) / ElitePlayersPerExtra at once, minus those of the
        /// deal still alive; none near a Once wave's mark (a boss, a marked elite) or in the final phase; each a kind whose window holds now,
        /// picked by the run's dice, spawned as a group of one around a standing player (BossAppeared: the banner, the HUD's bar).</summary>
        private void StepElites()
        {
            if (C.Elites == null || C.Elites.Count == 0 || R.EliteEverySeconds <= 0 || FinalPhase) return;
            if (_eliteNext < 0) _eliteNext = Ticks(R.EliteFirstSeconds * TimeScale);
            if (Tick < _eliteNext) return;
            int quiet = Ticks(R.EliteQuietSeconds);
            foreach (var w in _waves) if (w.Def.Once && Math.Abs(Tick - w.From) < quiet) { _eliteNext = Tick + Ticks(5); return; }
            int per = 1 + (PlayersForScaling() - 1) / Math.Max(1, R.ElitePlayersPerExtra), alive = 0;
            foreach (var e in Enemies) if (e.Alive && IsDealElite(e.Def.Id)) alive++;
            if (alive >= per) { _eliteNext = Tick + Ticks(10); return; }
            int n = 0; foreach (var el in C.Elites) if (Tick >= Ticks(el.From * TimeScale) && Tick < Ticks(el.To * TimeScale) && C.Enemy(el.EnemyId) != null) n++;
            if (n == 0) { _eliteNext = Tick + Ticks(5); return; }
            for (int k = alive; k < per; k++)
            {
                int pick = _rng.Next(n);
                foreach (var el in C.Elites)
                {
                    if (!(Tick >= Ticks(el.From * TimeScale) && Tick < Ticks(el.To * TimeScale)) || C.Enemy(el.EnemyId) == null) continue;
                    if (pick-- > 0) continue;
                    int before = EnemiesAlive;
                    SpawnGroup(C.Enemy(el.EnemyId), 1, SpawnPattern.Ring, true);
                    if (EnemiesAlive > before) ElitesSpawned++;
                    break;
                }
            }
            double every = Math.Max(1, R.EliteEverySeconds), most = Math.Max(every, R.EliteEveryMaxSeconds);
            _eliteNext = Tick + Ticks(every + _rng.NextDouble() * (most - every));
        }
    }

    /// <summary>
    /// Client wave 18 (h18rules), point 23: H_SNAP's rules block, the LAST of the body (after h17pass's passives block and h17obj's
    /// objectives block): u8 countdown (0 none, 1 to pause, 2 to resume) [i8 slot of its asker, u16 wall ticks left]. A decoder reads it
    /// only when bytes are left (a host from before sends none: no countdown); a decoder from before never reads it (it stops at the
    /// objectives' block): the protocol version is unchanged by it.
    /// </summary>
    public static class HordeRulesWire
    {
        public static void Write(HordeWire.W w, HordeSim sim)
        {
            if (sim.PauseCountBy < 0) { w.U8(0); return; }
            w.U8(sim.PauseCountToPause ? 1 : 2).I8(sim.PauseCountBy).U16(Math.Min(65535, sim.PauseCountTicksLeft));
        }

        public static void Read(ref HordeWire.R r, HordeWorldView v)
        {
            v.PauseCountBy = -1; v.PauseCountSeconds = 0f;
            if (r.Left < 1) return;
            int k = r.U8();
            if (k == 0) return;
            int slot = r.I8(), left = r.U16();
            if (r.Bad || slot < 0 || slot >= HordeLimits.MaxPlayers) return;
            v.PauseCountBy = slot; v.PauseCountToPause = k == 1; v.PauseCountSeconds = left / (float)HordeLimits.TickRate;
        }
    }
}
