#nullable disable
using System;
using System.Collections.Generic;
using Loki.Horde.Net;

namespace Loki.Horde
{
    // ====================================================================================================================
    // Client wave 18 (h18feel), the design's point 35: SOUL RETRIEVAL. design note: "If a player dies in horde mode, there are two ways to revive
    // them. First, is to reach a level that makes it so that you change classes. That will bring back anyone who was dead and also put
    // them in the menu to select their class. And also through uh, soul retrieval system. When a player dies, their soul splits into
    // four pieces that are sent flying far away. By collecting all four, by standing on a circle, kind of like when you pick up a buff,
    // the player is brought back invulnerable for 10 seconds, right next to the player that revived them. The base amount of souls
    // segments is four, it increases by one for each player above 2, And increases by one every time the same player dies. When
    // changing classes, this number of shards, if it has been increased due to deaths, is reduced by two." And: "While there are shards
    // on the field, the amount of enemies spawning increases by 20%. If multiple players are dead at the same time, shards can count for
    // both players. For example, let's say that player A dies, drops four shards. Player B survives, picks up two shards. Then player C
    // dies and drops four shards. Two of those four shards will count towards both player A and player C. And the remaining two will
    // count only for player C."
    //   - The timed self-revive of wave 8 (HordeSim.StepRevive: 12 s while an ally stands) is GONE: StepSouls takes its place.
    //   - A fall (with another player standing) splits the soul into N shards: N = BaseShards + PerPlayerAbove × (players present − 2,
    //     at least 0) + PerDeath × (the player's earlier deaths in the run), at most MaxShards; at the player's class change the death
    //     part drops by JobChangeDrop (never below 0). They land NearCells to FarCells from where it fell, spread round it, inside the
    //     arena (EdgeMarginCells: a shard beyond it is held at the edge, so nearer), ApartCells apart where they can be.
    //   - Shared shards: when a player falls while others still lie fallen, as many of its new shards as each of them still needs count
    //     for that one too (a shard's Owners is a set of players); a fallen player's older shards count only for it. Claiming a shard
    //     advances every player it counts for; a shard counting for nobody fallen any more is gone.
    //   - A shard is a small circle (Radius) a standing player claims by standing in it ClaimSeconds (enemies inside slow it, as the
    //     wave 17 objectives' circles; nobody inside: it drains at DrainShare). When a fallen player has all it needs, it stands up
    //     BesideCells from the claimer (on its body's side), ReviveHpPct of its HP, invulnerable InvulnerableSeconds and faster (below).
    //   - The party reaching a class-change level (RulesDef.JobMarks) brings back every fallen player where it lies (HP as above, the
    //     same rise as below; its class choice is offered as everyone's); their shards go.
    //   - Both rises (design note, after point 35: "A soul shard revive should give 5s, as should a class-change revive. During this time, the
    //     player also moves and attacks much faster."): InvulnerableSeconds (5) unhurt, and for as long RiseMovePct more speed and
    //     RiseAttackPct faster attacks (every weapon's wait / 1.6: CooldownPct -37.5), a buff (SimBuff, RiseBuffId) that runs out.
    //   - While any shard lies on the field the waves spawn SpawnPct more (StepWaves' count × SoulSpawnMul).
    //   - Alone nobody can collect: a fall ends the run as before (CheckEnd), no shards are made.
    // Every number is LOKI'S OWN where the design gave none (SoulRulesDef; the stamp hashes them). HordeSim.cs calls InitSouls (the
    // constructor), SoulsFell (the fall), StepSouls (Step, in StepRevive's place), SoulsJobMark (GainExp), SoulsJobChanged (ChangeJob),
    // SoulSpawnMul (StepWaves), HashSouls (StateHash). The snapshot carries the shards and the fallen players' counts in ONE block after
    // the objectives' (HordeSoulWire; protocol 4). Event kinds 27 SoulSplit, 28 SoulTaken (the orchestrator's 27-29 for h18feel); a
    // rise is the existing Rose (Value 1: souls, 2: a class-change level). Tests: tools/lokihost/Tests/HordeSoulTests.cs.
    //
    // Client wave 18 (h18feel), the design's point 37: THE YGGDRASIL BERRY SAVES A LIFE. design note: "Give the Fruit of Yggdrasil the property of
    // saving your life once. In Solo Mode, it is consumed and vanishes. In Party Mode. It is put on a 150s cooldown where it does
    // nothing." + "If it is consumed in solo mode, its slot cannot be taken by another thing. It effectively 'burns' that slot." + "If
    // you are saved by Yggdrasil, you are fully healed, enemies around you are killed (elites and bosses are pushed back instead), and
    // you become invulnerable for 4s" + "Reduce the Yggdrasil's passive power level in exchange, to keep it balanced."
    //   - A hit that would make the holder of the Yggdrasil Berry card (item 607; its branches too) fall leaves it standing instead
    //     (HurtPlayer -> YggSave, before the fall: no soul split): full HP, invulnerable SaveSafeSeconds; in the same tick (StepSouls)
    //     every normal enemy within SaveClearCells dies (the holder's kills, their EXP drops) and every elite and boss there is knocked
    //     out to SaveClearCells + SavePushMarginCells (the engine's knockback impulse, whatever its resistance); event 29 LifeSaved.
    //   - Alone (one runner present): the berry is spent and its slot BURNT for the rest of the run (it stays in the weapons, never
    //     fires, never levels, never saves again: the slot cannot take another weapon). With others: it does nothing for
    //     SaveCooldownSeconds (no save, no heal), then works again.
    //   - The berry's own heal is weaker (Content/HordeCards.cs: the balance runs in design notes).
    //   The snapshot's souls block carries each runner's berry state (burnt / cooling until) for the HUD.
    // ====================================================================================================================

    /// <summary>Soul retrieval's numbers (client wave 18, h18feel, point 35; distances in cells, times in seconds). the design's: 4 shards,
    /// +1 a player above 2, +1 a death, −2 deaths at a class change, 10 s invulnerable, +20 % enemies while shards lie; the rest Loki's.</summary>
    public sealed class SoulRulesDef
    {
        public bool Enabled = true;
        /// <summary>N = BaseShards + PerPlayerAbove × max(0, players − PlayersFrom) + PerDeath × earlier deaths, at most MaxShards.</summary>
        public int BaseShards = 4, PlayersFrom = 2, PerPlayerAbove = 1, PerDeath = 1, MaxShards = 16;
        /// <summary>At the player's class change its death part drops by this (never below 0).</summary>
        public int JobChangeDrop = 2;
        /// <summary>Where they land: this far from the fall, this far inside the arena's edge, this far apart (cells).</summary>
        public float NearCells = 12f, FarCells = 20f, EdgeMarginCells = 3f, ApartCells = 3f;
        /// <summary>A shard's circle (radius), the seconds to claim it, the enemies' slowing (as the objectives': each normal enemy inside
        /// takes SlowPctPerEnemy off its speed, an elite counts EliteWeight, a boss BossWeight, never below ClaimMinPct), the drain.</summary>
        public float Radius = 1.5f, ClaimSeconds = 1.5f, SlowPctPerEnemy = 12f, ClaimMinPct = 20f, DrainShare = 0.5f;
        public int EliteWeight = 3, BossWeight = 5;
        /// <summary>The rise: this far from the claimer, this share of max HP, this long invulnerable.</summary>
        public float BesideCells = 1f, ReviveHpPct = 50f, InvulnerableSeconds = 5f;
        /// <summary>For InvulnerableSeconds after either rise: this many percent faster walking and attacking (Loki's numbers for the design's
        /// "much faster"; attacks: every weapon's wait divided by 1 + RiseAttackPct / 100).</summary>
        public float RiseMovePct = 60f, RiseAttackPct = 60f;
        /// <summary>While any shard lies on the field the waves spawn this many percent more.</summary>
        public float SpawnPct = 20f;
        /// <summary>The party reaching a class-change level brings every fallen player back.</summary>
        public bool JobMarkRevives = true;
        /// <summary>Client wave 18 (h18feel), point 37: RO's Yggdrasil Berry (item_db 607) is the card that saves a life (0: none does).</summary>
        public const int YggdrasilBerry = 607;
        public int SaveItemId = YggdrasilBerry;
        /// <summary>The save: normal enemies within SaveClearCells die, elites and bosses there are pushed out to SaveClearCells +
        /// SavePushMarginCells; SaveSafeSeconds invulnerable (the design's 4 s); with others it rests SaveCooldownSeconds (the design's 150 s).</summary>
        public float SaveClearCells = 4f, SavePushMarginCells = 2.5f, SaveSafeSeconds = 4f, SaveCooldownSeconds = 150f;

        /// <summary>The shard count for a fall (players present then, the player's earlier deaths as counted).</summary>
        public int Count(int players, int deaths) => Math.Max(1, Math.Min(Math.Max(1, MaxShards), BaseShards + PerPlayerAbove * Math.Max(0, players - PlayersFrom) + PerDeath * Math.Max(0, deaths)));

        /// <summary>Every number, in a fixed order (HordeWire.ContentStamp hashes them).</summary>
        public IEnumerable<double> StampValues()
        {
            yield return -18; // (a tag: client wave 18's block)
            foreach (var v in new double[] { Enabled ? 1 : 0, BaseShards, PlayersFrom, PerPlayerAbove, PerDeath, MaxShards, JobChangeDrop, NearCells, FarCells, EdgeMarginCells,
                ApartCells, Radius, ClaimSeconds, SlowPctPerEnemy, ClaimMinPct, DrainShare, EliteWeight, BossWeight, BesideCells, ReviveHpPct, InvulnerableSeconds, SpawnPct,
                JobMarkRevives ? 1 : 0 }) yield return v;
            foreach (var v in new double[] { -35, RiseMovePct, RiseAttackPct }) yield return v; // (the rise's speed, after point 35)
            foreach (var v in new double[] { -37, SaveItemId, SaveClearCells, SavePushMarginCells, SaveSafeSeconds, SaveCooldownSeconds }) yield return v; // (client wave 18, point 37)
        }
    }

    /// <summary>A soul shard in the engine.</summary>
    public sealed class SimSoul
    {
        public int Id, X, Y, Radius, StartTick;
        /// <summary>The players it counts for (bit = slot) and the one whose fall made it.</summary>
        public int Owners, Dropper;
        /// <summary>The claim's ring 0..1, who stands in it (-1 none), the enemies' weight inside now.</summary>
        public double Progress;
        public int Claimer = -1, Crowd;
    }

    public sealed partial class HordeSim
    {
        /// <summary>Client wave 18 (h18feel): the soul shards on the field now.</summary>
        public readonly List<SimSoul> Souls = new List<SimSoul>();
        /// <summary>Per slot while it lies fallen with shards out: its shard count N and how many it still needs (0 otherwise).</summary>
        public readonly int[] SoulTotal = new int[HordeLimits.MaxPlayers], SoulNeed = new int[HordeLimits.MaxPlayers];
        /// <summary>Counters (tests, the host's log): falls split, shards claimed, rises by souls, rises at a class-change level.</summary>
        public int SoulsSplit, SoulsTaken, SoulRevives, SoulJobMarkRevives;
        /// <summary>The death part of each account's shard count (earlier deaths, less the class changes' drops).</summary>
        private readonly Dictionary<uint, int> _soulDeaths = new Dictionary<uint, int>();
        private HordeRng _soulDice;
        private int _soulNextId = 1;
        private SoulRulesDef SoulRules => C.Souls;

        // ---- client wave 18 (h18feel), point 37: the Yggdrasil Berry
        /// <summary>Accounts whose berry is spent and its slot burnt (alone), and the tick each party berry works again.</summary>
        private readonly HashSet<uint> _yggBurnt = new HashSet<uint>();
        private readonly Dictionary<uint, int> _yggUntil = new Dictionary<uint, int>();
        private readonly List<KeyValuePair<int, int>> _yggClear = new List<KeyValuePair<int, int>>(); // (slot, weapon id): saves whose clearing is due this tick
        /// <summary>Lives saved, enemies the saves killed and pushed (tests, the log).</summary>
        public int YggSaves, YggKills, YggPushes;
        public bool YggBurnt(SimPlayer p) => p != null && _yggBurnt.Contains(p.Aid);
        public int YggCoolUntil(SimPlayer p) => p != null && _yggUntil.TryGetValue(p.Aid, out int u) ? u : 0;
        /// <summary>The Yggdrasil Berry card (its branches too: they keep its item).</summary>
        public bool IsBerry(OwnedWeapon ow) { var r = SoulRules; return r != null && r.SaveItemId > 0 && ow != null && ow.Def != null && ow.Def.ItemId == r.SaveItemId; }
        /// <summary>The berry does nothing now: its slot burnt, or resting after a save in a party (StepWeapon skips it).</summary>
        private bool YggOff(SimPlayer p, OwnedWeapon ow) => IsBerry(ow) && (YggBurnt(p) || Tick < YggCoolUntil(p));
        /// <summary>A burnt berry: never offered a level, never upgraded by a chest.</summary>
        private bool YggBurntWeapon(SimPlayer p, OwnedWeapon ow) => IsBerry(ow) && YggBurnt(p);

        /// <summary>HurtPlayer, when the hit would make p fall: the berry saves it (true: it stands, full HP, invulnerable; the clearing
        /// follows in StepSouls this tick).</summary>
        private bool YggSave(SimPlayer p)
        {
            var r = SoulRules; if (r == null || r.SaveItemId <= 0 || p == null) return false;
            OwnedWeapon ow = null; foreach (var w in p.Weapons) if (IsBerry(w)) { ow = w; break; }
            if (ow == null || YggOff(p, ow)) return false;
            p.Hp = p.MaxHp;
            p.SafeUntil = Math.Max(p.SafeUntil, Tick + Ticks(r.SaveSafeSeconds));
            bool alone = PlayersPresent <= 1;
            if (alone) _yggBurnt.Add(p.Aid); else _yggUntil[p.Aid] = Tick + Ticks(r.SaveCooldownSeconds);
            p.LoadoutVersion++;
            YggSaves++;
            _yggClear.Add(new KeyValuePair<int, int>(p.Slot, ow.Def.Id));
            AddEvent(HordeEventKind.LifeSaved, HordeEventFlags.None, p.Slot, -1, ow.Def.Id, alone ? 0 : (int)Math.Round(r.SaveCooldownSeconds), p.X, p.Y);
            return true;
        }

        /// <summary>StepSouls: each save of this tick clears round its runner (normal enemies die, elites and bosses are pushed out).</summary>
        private void YggClear(SoulRulesDef r)
        {
            if (_yggClear.Count == 0 || r == null) return;
            foreach (var kv in _yggClear)
            {
                var p = Players[kv.Key]; if (!p.Present) continue;
                int rad = Units(r.SaveClearCells); long r2 = (long)rad * rad;
                foreach (var e in Enemies)
                {
                    if (!e.Alive) continue;
                    long d2 = Dist2(e.X, e.Y, p.X, p.Y); if (d2 > r2) continue;
                    if (e.Def.Rank == EnemyRank.Normal) { if (DamageEnemy(e, Math.Max(1, e.Hp), p.Slot, kv.Value, false, false, p.X, p.Y)) YggKills++; continue; }
                    long dx = e.X - p.X, dy = e.Y - p.Y; int d = HordeMove.ISqrt(d2); if (d < 1) { dx = 1; dy = 0; d = 1; }
                    int need = Math.Max(16, Units(r.SaveClearCells + r.SavePushMarginCells) - d);
                    int v0 = HordeMove.ImpulseFor(need, _kbKeepPct);
                    e.KbVx = Clamp((int)(dx * v0 / d), -8 * U, 8 * U); e.KbVy = Clamp((int)(dy * v0 / d), -8 * U, 8 * U);
                    YggPushes++;
                }
            }
            _yggClear.Clear();
        }

        /// <summary>From the constructor: the shards' own dice (the run's dice are untouched by them).</summary>
        private void InitSouls(ulong seed) { _soulDice = new HordeRng(seed ^ 0x50015EA7C0FFEE18UL); }

        /// <summary>The death part of the account's shard count now.</summary>
        public int SoulDeaths(uint aid) => _soulDeaths.TryGetValue(aid, out int d) ? d : 0;

        /// <summary>StepWaves' count factor: SpawnPct more while any shard lies on the field.</summary>
        private double SoulSpawnMul() { var r = SoulRules; return r != null && r.Enabled && Souls.Count > 0 ? 1 + Math.Max(0, r.SpawnPct) / 100.0 : 1.0; }

        /// <summary>A player fell (HurtPlayer): its soul splits, if anyone else stands to collect it.</summary>
        private void SoulsFell(SimPlayer p)
        {
            var r = SoulRules;
            if (r == null || !r.Enabled || p == null || !p.Present) return;
            bool other = false;
            foreach (var q in Players) if (q != p && q.Present && !q.Fallen) { other = true; break; }
            int deaths = SoulDeaths(p.Aid);
            _soulDeaths[p.Aid] = deaths + 1; // (this death counts for the next fall)
            if (!other) return; // alone (or the last one standing fell): the run ends as before
            int n = r.Count(PlayersPresent, deaths);
            SoulTotal[p.Slot] = n; SoulNeed[p.Slot] = n; p.Revive = 0;
            // shared shards: as many of the new ones as each other fallen player still needs count for it too
            var share = new int[Players.Length];
            foreach (var q in Players) if (q != p && q.Present && q.Fallen && SoulNeed[q.Slot] > 0) share[q.Slot] = Math.Min(SoulNeed[q.Slot], n);
            int margin = Units(r.EdgeMarginCells);
            int lx = MinX + margin, ly = MinY + margin, hx = MaxX - margin, hy = MaxY - margin;
            if (lx > hx) { lx = hx = (MinX + MaxX) / 2; } if (ly > hy) { ly = hy = (MinY + MaxY) / 2; }
            double baseAng = _soulDice.NextDouble() * Math.PI * 2;
            long apart2 = (long)Units(r.ApartCells) * Units(r.ApartCells);
            for (int i = 0; i < n; i++)
            {
                int x = p.X, y = p.Y;
                for (int tryN = 0; tryN < 8; tryN++)
                {
                    double a = baseAng + (i + (_soulDice.NextDouble() - 0.5) * 0.6) * Math.PI * 2 / n + tryN * 0.37;
                    double d = r.NearCells + _soulDice.NextDouble() * Math.Max(0, r.FarCells - r.NearCells);
                    x = Clamp(p.X + Units(Math.Cos(a) * d), lx, hx); y = Clamp(p.Y + Units(Math.Sin(a) * d), ly, hy);
                    bool ok = true;
                    foreach (var s in Souls) if (Dist2(s.X, s.Y, x, y) < apart2) { ok = false; break; }
                    if (ok) break;
                }
                int owners = 1 << p.Slot;
                for (int s = 0; s < share.Length; s++) if (i < share[s]) owners |= 1 << s;
                Souls.Add(new SimSoul { Id = _soulNextId, X = x, Y = y, Radius = Math.Max(16, Units(r.Radius)), StartTick = Tick, Owners = owners, Dropper = p.Slot });
                _soulNextId = _soulNextId >= 255 ? 1 : _soulNextId + 1;
            }
            SoulsSplit++;
            AddEvent(HordeEventKind.SoulSplit, HordeEventFlags.None, p.Slot, -1, 0, n, p.X, p.Y);
        }

        /// <summary>Once a tick (where StepRevive was): who counts as fallen, the shards' claims, the rises.</summary>
        private void StepSouls()
        {
            var r = SoulRules;
            YggClear(r); // client wave 18 (h18feel), point 37: the Yggdrasil Berry's saves of this tick clear round their runners
            int fallen = 0;
            foreach (var p in Players)
            {
                if (p.Present && p.Fallen && SoulTotal[p.Slot] > 0) { fallen |= 1 << p.Slot; p.Revive = (SoulTotal[p.Slot] - SoulNeed[p.Slot]) / (double)SoulTotal[p.Slot]; }
                else if (SoulTotal[p.Slot] != 0 || SoulNeed[p.Slot] != 0) { SoulTotal[p.Slot] = SoulNeed[p.Slot] = 0; if (!p.Fallen) p.Revive = 0; } // (risen another way, or gone)
                p.Helped = false;
            }
            for (int i = Souls.Count - 1; i >= 0; i--) { Souls[i].Owners &= fallen; if (Souls[i].Owners == 0) Souls.RemoveAt(i); }
            if (r == null || !r.Enabled) { Souls.Clear(); return; }
            double full = 1.0 / Math.Max(1, Ticks(r.ClaimSeconds));
            for (int i = Souls.Count - 1; i >= 0; i--)
            {
                if (i >= Souls.Count) continue;
                var s = Souls[i];
                if (s.Owners == 0) { Souls.RemoveAt(i); continue; }
                SimPlayer claimer = s.Claimer >= 0 && InSoul(s, Players[s.Claimer]) ? Players[s.Claimer] : null;
                foreach (var p in Players) if (claimer == null && InSoul(s, p)) claimer = p;
                int crowd = 0;
                foreach (var e in Enemies)
                {
                    if (!e.Alive || Dist2(e.X, e.Y, s.X, s.Y) > (long)s.Radius * s.Radius) continue;
                    crowd += e.Def.Rank == EnemyRank.Normal ? 1 : e.Def.Rank == EnemyRank.Elite ? Math.Max(1, r.EliteWeight) : Math.Max(1, r.BossWeight);
                }
                s.Crowd = crowd; s.Claimer = claimer != null ? claimer.Slot : -1;
                if (claimer == null) { s.Progress = Math.Max(0, s.Progress - full * Math.Max(0, r.DrainShare)); continue; }
                s.Progress += full * Math.Max(r.ClaimMinPct, 100.0 - r.SlowPctPerEnemy * crowd) / 100.0;
                if (s.Progress < 1.0 - 1e-9) continue;
                // claimed: every player it counts for comes one shard closer; those who have all they need stand up beside the claimer
                Souls.RemoveAt(i); SoulsTaken++;
                AddEvent(HordeEventKind.SoulTaken, HordeEventFlags.None, claimer.Slot, s.Id, s.Owners, s.Dropper, s.X, s.Y);
                foreach (var p in Players)
                {
                    if ((s.Owners & (1 << p.Slot)) == 0 || !p.Present || !p.Fallen || SoulNeed[p.Slot] <= 0) continue;
                    SoulNeed[p.Slot]--;
                    p.Revive = (SoulTotal[p.Slot] - SoulNeed[p.Slot]) / (double)Math.Max(1, SoulTotal[p.Slot]);
                    if (SoulNeed[p.Slot] == 0) SoulRise(p, claimer, r);
                }
                int still = 0; foreach (var p in Players) if (p.Present && p.Fallen && SoulTotal[p.Slot] > 0) still |= 1 << p.Slot;
                for (int k = Souls.Count - 1; k >= 0; k--) { Souls[k].Owners &= still; if (Souls[k].Owners == 0) Souls.RemoveAt(k); }
                i = Math.Min(i, Souls.Count); // (the list may have shrunk below this index)
            }
        }

        private static bool InSoul(SimSoul s, SimPlayer p) => p.Present && !p.Fallen && Dist2(p.X, p.Y, s.X, s.Y) <= (long)s.Radius * s.Radius;

        /// <summary>A fallen player has all its shards: it stands up beside the claimer, invulnerable for a while.</summary>
        private void SoulRise(SimPlayer p, SimPlayer claimer, SoulRulesDef r)
        {
            double a = p.X == claimer.X && p.Y == claimer.Y ? 0 : Math.Atan2(p.Y - claimer.Y, p.X - claimer.X); // (on its body's side)
            int x = Clamp(claimer.X + Units(Math.Cos(a) * r.BesideCells), MinX, MaxX), y = Clamp(claimer.Y + Units(Math.Sin(a) * r.BesideCells), MinY, MaxY);
            Rise(p, x, y, r);
            claimer.Revives++; SoulRevives++;
            AddEvent(HordeEventKind.Rose, HordeEventFlags.None, p.Slot, claimer.Slot, 0, 1, p.X, p.Y);
        }

        /// <summary>The rise's speed buff (SimBuff.WeaponId: apart from every weapon's and the objectives' -1701..-1707).</summary>
        public const int RiseBuffId = -1835;

        /// <summary>Both rises: InvulnerableSeconds unhurt and, as long, faster walking and attacking (a buff that runs out).</summary>
        private void Rise(SimPlayer p, int x, int y, SoulRulesDef r)
        {
            p.Fallen = false; p.Revive = 0; p.Helped = false;
            p.X = x; p.Y = y; p.Vx = p.Vy = 0; p.Moving = false;
            int until = Tick + Math.Max(1, Ticks(r.InvulnerableSeconds));
            var mods = new[] { new ModDef(Mod.MoveSpeedPct, r.RiseMovePct), new ModDef(Mod.CooldownPct, (float)((1.0 / (1 + Math.Max(0, r.RiseAttackPct) / 100.0) - 1) * 100)) };
            p.Buffs.RemoveAll(b => b.WeaponId == RiseBuffId);
            p.Buffs.Add(new SimBuff { Mods = mods, Until = until, SourceSlot = -1, WeaponId = RiseBuffId });
            Recalc(p, false);
            p.Hp = Math.Max(1, (int)Math.Round(p.MaxHp * r.ReviveHpPct / 100.0));
            p.SafeUntil = Math.Max(p.SafeUntil, until);
            SoulTotal[p.Slot] = SoulNeed[p.Slot] = 0;
            foreach (var ow in p.Weapons) ow.NextTick = Math.Max(ow.NextTick, Tick + 1);
        }

        /// <summary>GainExp: the party reached a level; at a class-change level (RulesDef.JobMarks) every fallen player stands up where it lies.</summary>
        private void SoulsJobMark()
        {
            var r = SoulRules;
            if (r == null || !r.Enabled || !r.JobMarkRevives || R.JobMarks == null || Array.IndexOf(R.JobMarks, PartyLevel) < 0) return;
            foreach (var p in Players)
            {
                if (!p.Present || !p.Fallen) continue;
                Rise(p, p.X, p.Y, r);
                SoulJobMarkRevives++;
                AddEvent(HordeEventKind.Rose, HordeEventFlags.None, p.Slot, -1, 0, 2, p.X, p.Y);
            }
            int still = 0; foreach (var p in Players) if (p.Present && p.Fallen && SoulTotal[p.Slot] > 0) still |= 1 << p.Slot;
            for (int k = Souls.Count - 1; k >= 0; k--) { Souls[k].Owners &= still; if (Souls[k].Owners == 0) Souls.RemoveAt(k); }
        }

        /// <summary>ChangeJob: the death part of this player's shard count drops by JobChangeDrop (never below 0).</summary>
        private void SoulsJobChanged(SimPlayer p)
        {
            var r = SoulRules; if (r == null || p == null) return;
            int d = SoulDeaths(p.Aid);
            if (d > 0) _soulDeaths[p.Aid] = Math.Max(0, d - Math.Max(0, r.JobChangeDrop));
        }

        /// <summary>StateHash's part (the determinism tests).</summary>
        private void HashSouls(Action<long> m)
        {
            foreach (var s in Souls) { m(s.Id); m(s.X); m(s.Y); m(s.Owners); m((long)(s.Progress * 1e6)); m(s.Claimer); }
            foreach (var p in Players) if (p.Present) { m(YggBurnt(p) ? 1 : 0); m(YggCoolUntil(p)); } // (point 37)
            for (int i = 0; i < SoulTotal.Length; i++) { m(SoulTotal[i]); m(SoulNeed[i]); }
        }

        /// <summary>A line for the host's log for a soul's event (null for any other event).</summary>
        public static string SoulLogLine(HordeEvent e, int tick)
        {
            string t = $"{tick / TickRate / 60}:{tick / TickRate % 60:00}";
            switch (e.Kind)
            {
                case HordeEventKind.SoulSplit: return $"slot {e.Slot} fell: its soul split into {e.Value} shards ({t})";
                case HordeEventKind.SoulTaken: return $"slot {e.Slot} took soul shard {e.Target} (it counted for mask {e.DefId}) ({t})";
                case HordeEventKind.LifeSaved: return $"slot {e.Slot}'s Yggdrasil Berry saved its life ({(e.Value > 0 ? $"again in {e.Value} s" : "spent: its slot is burnt")}) ({t})";
                case HordeEventKind.Rose: return e.Value == 1 ? $"slot {e.Slot} rose: its soul gathered by slot {e.Target} ({t})" : e.Value == 2 ? $"slot {e.Slot} rose at the class-change level ({t})" : null;
                default: return null;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------- the client's view

    /// <summary>A soul shard as a client knows it (client wave 18, h18feel): places in the map's cells.</summary>
    public sealed class HordeSoulView
    {
        public int Id;
        public float X, Y, Radius, Progress;
        /// <summary>The players it counts for (bit = slot), who stands in it (-1 none), the enemies' weight inside.</summary>
        public int Owners, Claimer = -1, Crowd;
        public bool CountsFor(int slot) => slot >= 0 && (Owners & (1 << slot)) != 0;
    }

    /// <summary>Client wave 18 (h18feel), point 37: a runner's Yggdrasil Berry that does nothing now: its slot burnt, or resting until a tick.</summary>
    public struct HordeBerryView
    {
        public int Slot, Until;
        public bool Burnt;
    }

    /// <summary>A fallen player's count: its shard count and how many it still needs.</summary>
    public struct HordeSoulCountView
    {
        public int Slot, Total, Need;
    }

    /// <summary>
    /// The souls' block of H_SNAP (client wave 18, h18feel; protocol 4), after the objectives' block. Every snapshot carries all of it:
    ///   u8 n × (u8 id, u16 x, u16 y (1/256 cell from the arena's corner), u8 radius × 16, u8 progress × 255, i8 claimer, u8 crowd, u8 owners)
    ///   u8 m × (u8 slot, u8 total, u8 need)
    ///   u8 k × (u8 slot, u8 state (1 the berry's slot burnt, 2 resting), i32 until tick)   (client wave 18, point 37)
    /// </summary>
    public static class HordeSoulWire
    {
        public static void Write(HordeWire.W w, HordeSim sim, int bx, int by)
        {
            int n = Math.Min(255, sim.Souls.Count);
            w.U8(n);
            for (int i = 0; i < n; i++)
            {
                var s = sim.Souls[i];
                w.U8(s.Id).U16(HordeSim.Clamp(s.X - bx, 0, 65535)).U16(HordeSim.Clamp(s.Y - by, 0, 65535)).U8(HordeSim.Clamp(s.Radius * 16 / HordeSim.U, 0, 255))
                 .U8(HordeSim.Clamp((int)Math.Round(s.Progress * 255), 0, 255)).I8(s.Claimer).U8(Math.Min(255, s.Crowd)).U8(s.Owners & 0xff);
            }
            int m = 0; for (int i = 0; i < sim.SoulTotal.Length; i++) if (sim.SoulTotal[i] > 0) m++;
            w.U8(m);
            for (int i = 0; i < sim.SoulTotal.Length; i++) if (sim.SoulTotal[i] > 0) w.U8(i).U8(Math.Min(255, sim.SoulTotal[i])).U8(Math.Min(255, sim.SoulNeed[i]));
            int k = 0; foreach (var p in sim.Players) if (p.Present && (sim.YggBurnt(p) || sim.YggCoolUntil(p) > sim.Tick)) k++;
            w.U8(k);
            foreach (var p in sim.Players) if (p.Present && (sim.YggBurnt(p) || sim.YggCoolUntil(p) > sim.Tick)) w.U8(p.Slot).U8(sim.YggBurnt(p) ? 1 : 2).I32(sim.YggCoolUntil(p));
        }

        public static void Read(ref HordeWire.R r, HordeWorldView v, float bx, float by)
        {
            v.Souls.Clear(); v.SoulCounts.Clear();
            int n = r.U8();
            for (int i = 0; i < n && !r.Bad; i++)
            {
                var s = new HordeSoulView { Id = r.U8() };
                s.X = bx + r.U16() / (float)HordeSim.U; s.Y = by + r.U16() / (float)HordeSim.U; s.Radius = r.U8() / 16f; s.Progress = r.U8() / 255f;
                s.Claimer = r.I8(); s.Crowd = r.U8(); s.Owners = r.U8();
                v.Souls.Add(s);
            }
            int m = r.U8();
            for (int i = 0; i < m && !r.Bad; i++) v.SoulCounts.Add(new HordeSoulCountView { Slot = r.U8(), Total = r.U8(), Need = r.U8() });
            v.Berries.Clear();
            int k = r.U8();
            for (int i = 0; i < k && !r.Bad; i++) { int slot = r.U8(), st = r.U8(), until = r.I32(); v.Berries.Add(new HordeBerryView { Slot = slot, Burnt = st == 1, Until = until }); }
        }
    }
}
