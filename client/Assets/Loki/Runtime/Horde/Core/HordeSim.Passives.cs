#nullable disable
using System;
using System.Collections.Generic;
using Loki.Horde.Net;

namespace Loki.Horde
{
    // ====================================================================================================================
    // Client wave 17 (h17pass), design 2 / point 22 of design notes ("passives you can see and play
    // around"; design note 2026-10-08: "Approved design specification."): EVERY passive gets a BODY (something drawn on or around the
    // runner: Horde/Client/HordePassivesDraw.cs) and a TRIGGER the player can use (a "proc": a counter, a timer or a threshold).
    //
    // Which passive has which trigger and body is DATA (PassiveDef.Proc, set by Content/HordePassiveProcs.cs by passive id); this
    // file is the engine's rule for each ProcKind. Passives of the same kind are ONE family on a runner: one counter, one timer,
    // one body (two dodge cards do not give two puffs): the family's numbers are the members' together (counts and times: the
    // lowest; powers, damage and chances: the sum; areas: the largest), each at its own level.
    //
    // The snapshot carries each runner's family states (charge 0..255, stacks, flags: HordePassiveBlock below); a trigger that
    // goes off is a HordeEventKind.Proc event (Slot = the runner, DefId = the family's first passive, Value = what it did, X / Y =
    // where, Target = an enemy slot or -1).
    //
    // Hooks into the shared engine (each one line, marked "client wave 17 (h17pass)"): HordeSim.Step (StepPassives), HitEnemy
    // (PassiveHitMul), HurtPlayer (PassiveDodged, PassiveBlocks, PassiveHurt), EnemyDied (PassiveKill), TakePickup (PassivePickup),
    // NearestStanding (PassiveHidden). Every number is Loki's own (Content/HordePassiveProcs.cs), never an official value.
    // ====================================================================================================================

    /// <summary>The engine's trigger rules (one per family). Which fields of <see cref="PassiveProcDef"/> each reads is on the member.</summary>
    public enum ProcKind : byte
    {
        None = 0,
        /// <summary>Count dodges in a row (a hit taken breaks the streak): a counter puff, Damage to every enemy within Area of the runner, pushed 1 cell.</summary>
        DodgeCounter = 1,
        /// <summary>Every Count-th hit of the runner is a heavy hit: +Power %, and HitEffect (Stun / Freeze / Dot) for HitEffectSeconds.</summary>
        HitCount = 2,
        /// <summary>A shell grows Seconds after the last one cracked: it takes the next hit whole.</summary>
        Shell = 3,
        /// <summary>Standing still Seconds: each member's own modifiers count Mul times.</summary>
        StillBoost = 4,
        /// <summary>Every Count-th critical hit drops PickupId (a clover: heals Power % of max HP) at the enemy.</summary>
        CritCount = 5,
        /// <summary>Count hits of the runner's shots on the same enemy mark it for Seconds: it takes +Power % from everyone.</summary>
        Mark = 6,
        /// <summary>A melee swing that hits Count enemies or more gives Power % of that weapon's wait back (once per wait).</summary>
        FullSwing = 7,
        /// <summary>A coat soaks Power % of max HP of damage; it fills again after Seconds without damage.</summary>
        Coat = 8,
        /// <summary>Walking Seconds (in all) charges a dash: the next hit +Power %.</summary>
        Stride = 9,
        /// <summary>Every Seconds a magnet pulse: every EXP gem within Area cells flies to the runner.</summary>
        MagnetPulse = 10,
        /// <summary>A kill of a target (RaceMask / ElementMask / SizeMask / RankMask) drops PickupId (a heal mote) Chance % of the time, at most once per Cooldown.</summary>
        Bane = 11,
        /// <summary>Inside one of the runner's own zones, or holding a boost of its own aura: +Power % damage.</summary>
        OwnZone = 12,
        /// <summary>Seconds without hitting anything: the next hit +Power %.</summary>
        IdleCharge = 13,
        /// <summary>A dodge makes the weapon Arg (owned) fire at once.</summary>
        DodgeReady = 14,
        /// <summary>Under Threshold % HP: +Power % damage and each member's own modifiers Mul times.</summary>
        LowHpBoost = 15,
        /// <summary>Falling under Threshold % HP: untouchable for Seconds, once per Cooldown.</summary>
        LowHpHide = 16,
        /// <summary>Touched by an enemy: a counter slash at it (Damage, melee), once per Cooldown.</summary>
        HurtCounter = 17,
        /// <summary>Every Seconds the next use of a weapon of ClassMask deals +Power % (every hit of that use, that tick).</summary>
        TimerCharge = 18,
        /// <summary>A kill by a weapon of ClassMask heals Damage HP.</summary>
        KillHeal = 19,
        /// <summary>A kill drops PickupId Chance % of the time (an arrow bundle: taking it gives +1 shot for Seconds).</summary>
        KillDrop = 20,
        /// <summary>Blocks the next hit of an elite or boss, once per Cooldown.</summary>
        BossBlock = 21,
        /// <summary>Standing still Seconds: enemies lose the runner until it moves (Area seconds at most), then Cooldown apart.</summary>
        Cloak = 22,
        /// <summary>Standing still Seconds: normal enemies' touches miss the runner for Area seconds, once per Cooldown.</summary>
        Gangster = 23,
        /// <summary>Walking Seconds without a stop: each member's own modifiers count Mul times.</summary>
        MoveBoost = 24,
        /// <summary>A touch returns Power % of its damage to the toucher.</summary>
        Reflect = 25,
    }

    /// <summary>What the client draws for a family (HordePassivesDraw); the engine never reads it.</summary>
    public enum ProcBody : byte
    {
        None = 0,
        /// <summary>A fading copy of the runner where it dodged; a ring at the puff.</summary>
        AfterImage = 1,
        /// <summary>A small monster (BodyMob) circling the runner; a flash at the heavy hit.</summary>
        Orbiter = 2,
        /// <summary>A shell under the runner (and under the HP bar); it cracks.</summary>
        Shell = 3,
        /// <summary>Green motes rising; a ring at the feet fills.</summary>
        Motes = 4,
        /// <summary>A star pops at each critical hit.</summary>
        Stars = 5,
        /// <summary>Shots leave a trail; a marked enemy carries a ring.</summary>
        Trail = 6,
        /// <summary>The swing is drawn wider and brighter; a flash on a full swing.</summary>
        Arc = 7,
        /// <summary>A blue coat around the runner that dims as it soaks.</summary>
        Coat = 8,
        /// <summary>Dust behind the walking runner; a glow when the dash is ready.</summary>
        Dust = 9,
        /// <summary>As Dust, and a Knight / Crusader rides a Peco Peco.</summary>
        Peco = 10,
        /// <summary>The pickup reach drawn on the ground; EXP gems glow; a pulse ring.</summary>
        PickupRing = 11,
        /// <summary>An icon over the enemies of the bane.</summary>
        RaceIcon = 12,
        /// <summary>The runner's zones and songs pulse; the runner glows inside.</summary>
        ZonePulse = 13,
        /// <summary>The hand glows when the charge is ready.</summary>
        Glow = 14,
        /// <summary>A red aura while active.</summary>
        RedAura = 15,
        /// <summary>A smoky ring: the runner is hidden.</summary>
        Mist = 16,
        /// <summary>A slash at the toucher.</summary>
        Slash = 17,
        /// <summary>A shield over the runner (ready) that flashes when it blocks or returns a hit.</summary>
        Shield = 18,
    }

    /// <summary>A passive's trigger and body (client wave 17, h17pass). Per-level numbers: base + PerLevel × (level − 1).</summary>
    public sealed class PassiveProcDef
    {
        public ProcKind Kind;
        public ProcBody Body;
        /// <summary>Counters: hits, criticals, dodges in a row, enemies in one swing, shot hits on one enemy.</summary>
        public int Count;
        /// <summary>Times in seconds (a shell's regrowth, standing / walking time, a pulse's period, a mark's length, idle time, untouchable time).</summary>
        public float Seconds, SecondsPerLevel;
        /// <summary>Percent: a heavy hit's, a mark's, a charge's bonus, a refund, a coat's share of max HP, a reflection, a clover's heal.</summary>
        public float Power, PowerPerLevel;
        /// <summary>Flat damage (a puff, a counter slash) or HP (a kill heal).</summary>
        public float Damage, DamagePerLevel;
        /// <summary>Cells (a puff, a magnet pulse), or seconds (Cloak's / Gangster's length).</summary>
        public float Area, AreaPerLevel;
        /// <summary>Percent per kill (Bane, KillDrop).</summary>
        public float Chance, ChancePerLevel;
        /// <summary>Percent of max HP (the low-HP kinds).</summary>
        public float Threshold;
        /// <summary>The member's own modifiers count this many times while active (StillBoost, MoveBoost, LowHpBoost); 1 = no change.</summary>
        public float Mul = 1f;
        /// <summary>Seconds between two triggers.</summary>
        public float Cooldown, CooldownPerLevel;
        /// <summary>A weapon id (DodgeReady), a buff's extra shots (KillDrop's pickup).</summary>
        public int Arg;
        /// <summary>A PickupDef id (CritCount, Bane, KillDrop).</summary>
        public int PickupId;
        /// <summary>HitCount: the heavy hit's effect (0 none) and its length.</summary>
        public EffectKind HitEffect;
        public float HitEffectSeconds;
        /// <summary>Bit (1 &lt;&lt; DamageClass) of the weapons that count (TimerCharge, KillHeal); 0 = any.</summary>
        public int ClassMask;
        /// <summary>Bane's targets: bits of Race, Element, UnitSize, EnemyRank.</summary>
        public int RaceMask, ElementMask, SizeMask, RankMask;
        /// <summary>The client's look: a monster drawn as the body (rAthena mob id), an item (rAthena item id); never read by the engine.</summary>
        public int BodyMob, BodyItem;

        public static float At(float baseValue, float perLevel, int level) => baseValue + perLevel * Math.Max(0, level - 1);
        public bool Targets(EnemyDef d) => d != null && ((RaceMask >> (int)d.Race & 1) != 0 || (ElementMask >> (int)d.Element & 1) != 0 || (SizeMask >> (int)d.Size & 1) != 0 || (RankMask >> (int)d.Rank & 1) != 0);
    }

    /// <summary>A family's state as the snapshot carries it (HordePlayerView.Procs).</summary>
    public struct HordeProcView
    {
        public ProcKind Kind;
        /// <summary>0..255: the meter (a counter's fill, a timer's progress, a coat's charge).</summary>
        public byte Charge;
        public byte Stacks;
        /// <summary><see cref="HordeProcFlags"/>.</summary>
        public byte Flags;
        public float Charge01 => Charge / 255f;
        public bool Ready => (Flags & HordeProcFlags.Ready) != 0;
        public bool Active => (Flags & HordeProcFlags.Active) != 0;
    }

    public static class HordeProcFlags
    {
        /// <summary>The trigger is ready (a shell up, a charge full, a cooldown over).</summary>
        public const byte Ready = 1;
        /// <summary>The family's effect is on now (standing still, inside a zone, low HP, hidden).</summary>
        public const byte Active = 2;
    }

    public sealed partial class HordeSim
    {
        /// <summary>ProcKind values the engine keeps a state for (0..ProcKindSlots-1).</summary>
        public const int ProcKindSlots = 32;

        private sealed class ProcSt
        {
            public int Stacks, Acc, Since = int.MinValue, Next = int.MinValue, Until, Pool, PoolMax, Fired;
            public bool On;
            public double Carry;
            public OwnedWeapon ChargedBy; public int ChargedAt = -1;
            public void Clear() { Stacks = Acc = Until = Pool = PoolMax = Fired = 0; Since = Next = int.MinValue; On = false; Carry = 0; ChargedBy = null; ChargedAt = -1; }
        }

        /// <summary>One family on one runner: its members (passive, def, level) and the numbers together.</summary>
        private sealed class ProcFam
        {
            public ProcKind Kind;
            public readonly List<OwnedPassive> Members = new List<OwnedPassive>();
            public int FirstId, Count, Seconds, Cooldown, Area, AreaTicks, Threshold, Arg, PickupId, ClassMask;
            public double Power, Damage, Chance;
            public EffectKind HitEffect; public float HitEffectSeconds;
            public PassiveProcDef Look;
        }

        private sealed class ProcAgg
        {
            public int Version = int.MinValue;
            public readonly List<ProcFam> Fams = new List<ProcFam>();
            public readonly ProcFam[] ByKind = new ProcFam[ProcKindSlots];
            // Mark: the last enemies hit by this runner's shots (enemy slot × 256 + serial) and their counts (a small ring)
            public readonly int[] MarkKey = new int[8], MarkHits = new int[8]; public int MarkAt;
            // FullSwing: the swing being counted (weapon, tick), its hits, and the refunds waiting for the next tick
            public OwnedWeapon SwingOw; public int SwingTick = -1, SwingHits; public bool SwingDone;
            public readonly List<KeyValuePair<OwnedWeapon, int>> Refunds = new List<KeyValuePair<OwnedWeapon, int>>();
            public readonly Dictionary<OwnedWeapon, int> LastRefund = new Dictionary<OwnedWeapon, int>();
            // KillHeal: the weapon of the hit being dealt (a kill inside it is that weapon's)
            public OwnedWeapon HitOw; public int HitTick = -1, HitEnemy = -1;
        }

        private readonly ProcSt[] _procSt = NewProcStates();
        private readonly ProcAgg[] _procAgg = NewProcAggs();
        // marks on enemies (by enemy slot; a new tenant of the slot has another serial)
        private readonly int[] _markUntil = new int[HordeLimits.MaxEnemies], _markSerial = new int[HordeLimits.MaxEnemies], _markBy = new int[HordeLimits.MaxEnemies];
        private readonly double[] _markPct = new double[HordeLimits.MaxEnemies];
        /// <summary>Triggers that went off, by slot and kind (tests, the smoke's log).</summary>
        private readonly int[] _procFired = new int[HordeLimits.MaxPlayers * ProcKindSlots];
        /// <summary>A runner's coats together soak at most this share of its max HP (percent; Loki's own: several "less damage" passives
        /// must not make a runner untouchable).</summary>
        public const double CoatCapPct = 25;
        /// <summary>The buffs a family holds on its runner (SimBuff.WeaponId: never a weapon's id).</summary>
        private const int ProcBuffId = -17000;

        private static ProcSt[] NewProcStates() { var a = new ProcSt[HordeLimits.MaxPlayers * ProcKindSlots]; for (int i = 0; i < a.Length; i++) a[i] = new ProcSt(); return a; }
        private static ProcAgg[] NewProcAggs() { var a = new ProcAgg[HordeLimits.MaxPlayers]; for (int i = 0; i < a.Length; i++) a[i] = new ProcAgg(); return a; }
        private ProcSt St(int slot, ProcKind k) => _procSt[slot * ProcKindSlots + (int)k];

        /// <summary>Triggers of <paramref name="k"/> that went off for <paramref name="slot"/> this run (tests, the smoke).</summary>
        public int ProcsFired(int slot, ProcKind k) => slot >= 0 && slot < HordeLimits.MaxPlayers ? _procFired[slot * ProcKindSlots + (int)k] : 0;
        /// <summary>The runner has a passive of that family.</summary>
        public bool HasProc(int slot, ProcKind k) { var p = Player(slot); return p != null && Fam(p, k) != null; }
        /// <summary>The family's effect is on now (standing still, low HP, hidden, inside its zone …).</summary>
        public bool ProcActive(int slot, ProcKind k) { var p = Player(slot); return p != null && Fam(p, k) != null && St(slot, k).On; }
        public int ProcStacks(int slot, ProcKind k) => Player(slot) != null ? St(slot, k).Stacks : 0;
        /// <summary>A coat's soak left (HP) and its full size.</summary>
        public int CoatLeft(int slot) => Player(slot) != null ? St(slot, ProcKind.Coat).Pool : 0;
        public int CoatMax(int slot) => Player(slot) != null ? St(slot, ProcKind.Coat).PoolMax : 0;
        /// <summary>The enemy in <paramref name="enemySlot"/> is marked (Mark): the percent it takes more, 0 when not.</summary>
        public double MarkedPct(int enemySlot) => enemySlot >= 0 && enemySlot < HordeLimits.MaxEnemies && Enemies[enemySlot].Alive && _markUntil[enemySlot] > Tick && _markSerial[enemySlot] == Enemies[enemySlot].Serial ? _markPct[enemySlot] : 0;

        /// <summary>Gives (or levels to <paramref name="level"/>) a passive without a card: tests, the balance tool, the smoke's own engine.</summary>
        public bool GivePassive(int slot, int passiveId, int level)
        {
            var p = Player(slot); var d = C.Passive(passiveId);
            if (p == null || d == null || d.Levels.Length == 0) return false;
            level = Math.Max(1, Math.Min(level, d.Levels.Length));
            OwnedPassive have = null; foreach (var op in p.Passives) if (op.Def == d) have = op;
            if (have == null) p.Passives.Add(new OwnedPassive { Def = d, Level = level }); else have.Level = level;
            Recalc(p, false); p.LoadoutVersion++;
            return true;
        }

        // =============================================================================================== families

        private ProcAgg Agg(SimPlayer p)
        {
            var a = _procAgg[p.Slot];
            if (a.Version == p.LoadoutVersion) return a;
            a.Version = p.LoadoutVersion;
            // a boost held by a family was made from the members' levels as they were: it is taken off and made again (next tick)
            bool recalc = false;
            for (int k = 0; k < ProcKindSlots; k++)
            {
                if (a.ByKind[k] == null || !St(p.Slot, (ProcKind)k).On) continue;
                int id = ProcBuffId - k, at = p.Buffs.FindIndex(b => b.WeaponId == id && b.SourceSlot == p.Slot);
                if (at >= 0) { p.Buffs.RemoveAt(at); St(p.Slot, (ProcKind)k).On = false; recalc = true; }
            }
            if (recalc) Recalc(p, false);
            a.Fams.Clear(); Array.Clear(a.ByKind, 0, a.ByKind.Length);
            foreach (var op in p.Passives)
            {
                var pd = op.Def.Proc;
                if (pd == null || pd.Kind == ProcKind.None || (int)pd.Kind >= ProcKindSlots || op.Level < 1) continue;
                int lv = Math.Min(op.Level, op.Def.Levels.Length);
                var f = a.ByKind[(int)pd.Kind];
                if (f == null)
                {
                    f = new ProcFam { Kind = pd.Kind, FirstId = op.Def.Id, Look = pd };
                    a.ByKind[(int)pd.Kind] = f; a.Fams.Add(f);
                }
                f.Members.Add(op);
                int secs = Ticks(PassiveProcDef.At(pd.Seconds, pd.SecondsPerLevel, lv)), cd = Ticks(PassiveProcDef.At(pd.Cooldown, pd.CooldownPerLevel, lv));
                f.Count = pd.Count > 0 && (f.Count == 0 || pd.Count < f.Count) ? pd.Count : f.Count;
                f.Seconds = secs > 0 && (f.Seconds == 0 || secs < f.Seconds) ? secs : f.Seconds;
                f.Cooldown = cd > 0 && (f.Cooldown == 0 || cd < f.Cooldown) ? cd : f.Cooldown;
                float area = PassiveProcDef.At(pd.Area, pd.AreaPerLevel, lv);
                f.Area = Math.Max(f.Area, Units(area)); f.AreaTicks = Math.Max(f.AreaTicks, Ticks(area));
                f.Threshold = Math.Max(f.Threshold, (int)Math.Round(pd.Threshold));
                f.Power += PassiveProcDef.At(pd.Power, pd.PowerPerLevel, lv);
                f.Damage += PassiveProcDef.At(pd.Damage, pd.DamagePerLevel, lv);
                f.Chance = Math.Min(100, f.Chance + PassiveProcDef.At(pd.Chance, pd.ChancePerLevel, lv));
                if (f.Arg == 0) f.Arg = pd.Arg;
                if (f.PickupId == 0) f.PickupId = pd.PickupId;
                if (f.HitEffect == 0 && pd.HitEffect != 0) { f.HitEffect = pd.HitEffect; f.HitEffectSeconds = pd.HitEffectSeconds; }
                f.ClassMask |= pd.ClassMask;
            }
            return a;
        }

        private ProcFam Fam(SimPlayer p, ProcKind k) { var a = Agg(p); return a.ByKind[(int)k]; }

        /// <param name="flags">Dot on a coat's soak (its refill has none).</param>
        private void Fired(SimPlayer p, ProcFam f, int target, int value, int x, int y, HordeEventFlags flags = HordeEventFlags.None)
        {
            _procFired[p.Slot * ProcKindSlots + (int)f.Kind]++;
            St(p.Slot, f.Kind).Fired++;
            AddEvent(HordeEventKind.Proc, flags, p.Slot, target, f.FirstId, value, x, y);
        }

        /// <summary>A family's boost on (the members' own modifiers × (Mul − 1), plus <paramref name="extra"/>) or off: one SimBuff per family.</summary>
        private void ProcBuff(SimPlayer p, ProcFam f, bool on, ModDef extra = null)
        {
            int id = ProcBuffId - (int)f.Kind;
            int at = p.Buffs.FindIndex(b => b.WeaponId == id && b.SourceSlot == p.Slot);
            if (!on) { if (at >= 0) { p.Buffs.RemoveAt(at); Recalc(p, false); } return; }
            if (at >= 0) return;
            var mods = new List<ModDef>();
            if (extra != null) mods.Add(extra);
            foreach (var op in f.Members)
            {
                float mul = op.Def.Proc.Mul;
                if (Math.Abs(mul - 1f) < 1e-6f) continue;
                var lvMods = op.Def.Levels[Math.Min(op.Level, op.Def.Levels.Length) - 1];
                if (lvMods != null) foreach (var m in lvMods) if (m != null) mods.Add(new ModDef(m.Kind, m.Value * (mul - 1f), m.Arg));
            }
            p.Buffs.Add(new SimBuff { Mods = mods.ToArray(), Until = int.MaxValue, SourceSlot = p.Slot, WeaponId = id });
            Recalc(p, false);
        }

        private static readonly WeaponDef[] ProcWeapons =
        {
            new WeaponDef { Id = 0, Key = "proc_melee", Class = DamageClass.Melee, Element = Element.Neutral },
            new WeaponDef { Id = 0, Key = "proc_ranged", Class = DamageClass.Ranged, Element = Element.Neutral },
            new WeaponDef { Id = 0, Key = "proc_magic", Class = DamageClass.Magic, Element = Element.Neutral },
        };

        /// <summary>A trigger's own damage to an enemy: <paramref name="baseDmg"/> through the runner's modifiers as a Neutral weapon of that class.</summary>
        private bool ProcStrike(SimPlayer p, SimEnemy e, double baseDmg, DamageClass cls, int fromX, int fromY)
        {
            if (e == null || !e.Alive || baseDmg <= 0) return false;
            int dmg = Math.Max(1, (int)Math.Round(baseDmg * DamageMul(p, ProcWeapons[(int)cls % 3], e)));
            return DamageEnemy(e, dmg, p.Slot, 0, false, false, fromX, fromY);
        }

        /// <summary>The enemy whose touch this is: -1 none (a shot, an area, a burst). Merge h17int (client wave 17): h17read's HurtPlayer
        /// parameters name the attacker and how the hit came, so a shot fired from where its shooter still stands is no touch (the exact-place
        /// match of h17pass's branch would have taken it for one).</summary>
        private int Toucher(int fromEnemy, HordeHitVia via) =>
            via == HordeHitVia.Touch && fromEnemy >= 0 && fromEnemy < Enemies.Length && Enemies[fromEnemy].Alive ? fromEnemy : -1;

        // =============================================================================================== each tick

        private void StepPassives()
        {
            for (int i = 0; i < _markUntil.Length; i++) if (_markUntil[i] != 0 && (_markUntil[i] <= Tick || !Enemies[i].Alive || Enemies[i].Serial != _markSerial[i])) _markUntil[i] = 0;
            foreach (var p in Players)
            {
                if (!p.Present) continue;
                var a = Agg(p);
                if (a.Fams.Count == 0) continue;
                // the swings' refunds (the weapon's next use was set after the swing: given back now)
                foreach (var kv in a.Refunds) kv.Key.NextTick = Math.Max(Tick, kv.Key.NextTick - kv.Value);
                a.Refunds.Clear();
                foreach (var f in a.Fams) StepFamily(p, f);
            }
        }

        private void StepFamily(SimPlayer p, ProcFam f)
        {
            var st = St(p.Slot, f.Kind);
            bool up = !p.Fallen;
            switch (f.Kind)
            {
                case ProcKind.Shell:
                    if (st.Since == int.MinValue) st.Since = Tick;
                    break;
                case ProcKind.StillBoost:
                case ProcKind.MoveBoost:
                {
                    bool cond = up && (f.Kind == ProcKind.StillBoost ? !p.Moving : p.Moving);
                    st.Acc = cond ? st.Acc + 1 : 0;
                    bool on = cond && st.Acc >= Math.Max(1, f.Seconds);
                    if (on != st.On) { st.On = on; ProcBuff(p, f, on); if (on) Fired(p, f, -1, 0, p.X, p.Y); }
                    break;
                }
                case ProcKind.LowHpBoost:
                {
                    bool on = up && (long)p.Hp * 100 < (long)f.Threshold * p.MaxHp;
                    if (on != st.On) { st.On = on; ProcBuff(p, f, on, f.Power > 0 ? new ModDef(Mod.DamagePct, (float)f.Power) : null); if (on) Fired(p, f, -1, (int)Math.Round(f.Power), p.X, p.Y); }
                    break;
                }
                case ProcKind.OwnZone:
                {
                    bool on = up && InOwnZone(p);
                    if (on != st.On) { st.On = on; ProcBuff(p, f, on, new ModDef(Mod.DamagePct, (float)f.Power)); if (on) Fired(p, f, -1, (int)Math.Round(f.Power), p.X, p.Y); }
                    break;
                }
                case ProcKind.Coat:
                {
                    st.PoolMax = Math.Max(0, (int)Math.Round(p.MaxHp * Math.Min(CoatCapPct, f.Power) / 100.0));
                    if (st.Since == int.MinValue) { st.Since = Tick; st.Pool = st.PoolMax; }
                    if (st.Pool > st.PoolMax) st.Pool = st.PoolMax;
                    if (up && st.Pool < st.PoolMax && Tick - st.Since >= Math.Max(1, f.Seconds)) { st.Pool = st.PoolMax; Fired(p, f, -1, st.PoolMax, p.X, p.Y); }
                    break;
                }
                case ProcKind.Stride:
                    if (up && p.Moving && !st.On && ++st.Acc >= Math.Max(1, f.Seconds)) { st.On = true; st.Acc = 0; }
                    break;
                case ProcKind.MagnetPulse:
                {
                    if (st.Next == int.MinValue) st.Next = Tick + Math.Max(1, f.Seconds);
                    if (!up || Tick < st.Next) break;
                    st.Next = Tick + Math.Max(1, f.Seconds);
                    int n = 0; long r2 = (long)f.Area * f.Area;
                    foreach (var q in Pickups)
                        if (q.Def.Kind == PickupKind.Exp && q.PulledBy < 0 && Dist2(q.X, q.Y, p.X, p.Y) <= r2) { q.PulledBy = p.Slot; q.PullTick = Tick; PickupsPulled.Add(q.Id); n++; }
                    Fired(p, f, -1, n, p.X, p.Y);
                    break;
                }
                case ProcKind.TimerCharge:
                    if (st.Next == int.MinValue) st.Next = Tick + Math.Max(1, f.Seconds);
                    break;
                case ProcKind.Cloak:
                {
                    if (!up || p.Moving) { st.Acc = 0; if (st.On) { st.On = false; st.Next = Tick + f.Cooldown; } break; }
                    st.Acc++;
                    if (st.On && Tick >= st.Until) { st.On = false; st.Next = Tick + f.Cooldown; }
                    else if (!st.On && st.Acc >= Math.Max(1, f.Seconds) && Tick >= st.Next) { st.On = true; st.Until = Tick + Math.Max(1, f.AreaTicks); Fired(p, f, -1, f.AreaTicks, p.X, p.Y); }
                    break;
                }
                case ProcKind.Gangster:
                {
                    st.Acc = up && !p.Moving ? st.Acc + 1 : 0;
                    if (st.On && (Tick >= st.Until || !up)) st.On = false;
                    else if (!st.On && up && st.Acc >= Math.Max(1, f.Seconds) && Tick >= st.Next)
                    { st.On = true; st.Until = Tick + Math.Max(1, f.AreaTicks); st.Next = Tick + f.Cooldown; Fired(p, f, -1, f.AreaTicks, p.X, p.Y); }
                    break;
                }
                case ProcKind.LowHpHide:
                    if (st.On && Tick >= st.Until) st.On = false;
                    break;
            }
        }

        private bool InOwnZone(SimPlayer p)
        {
            foreach (var z in Fx)
            {
                if (z.Kind != HordeFxKind.Zone || z.Ended || z.OwnerSlot != p.Slot) continue;
                WallClosestPoint(z, p.X, p.Y, out int zx, out int zy);
                if (Dist2(zx, zy, p.X, p.Y) <= (long)z.Radius * z.Radius) return true;
            }
            foreach (var b in p.Buffs) if (b.SourceSlot == p.Slot && b.WeaponId > 0) return true; // a boost of its own aura or zone
            return false;
        }

        /// <summary>Client wave 17 (h17pass), Cloaking: a hidden runner is not chased (NearestStanding skips it).</summary>
        private bool PassiveHidden(SimPlayer p) => p.Slot >= 0 && St(p.Slot, ProcKind.Cloak).On && Fam(p, ProcKind.Cloak) != null;

        // =============================================================================================== a hit dealt

        /// <summary>HitEnemy's hook: the multiplier of this hit (a heavy hit, a mark, a charge) and the counters it moves.</summary>
        private double PassiveHitMul(SimPlayer p, OwnedWeapon ow, SimEnemy e, bool crit)
        {
            double mul = 1;
            if (_markUntil[e.Slot] > Tick && _markSerial[e.Slot] == e.Serial) mul *= 1 + _markPct[e.Slot] / 100.0;
            var a = Agg(p);
            a.HitOw = ow; a.HitTick = Tick; a.HitEnemy = e.Slot;
            if (a.Fams.Count == 0) return mul;
            ProcFam f;
            // HitCount: every Count-th hit is heavy
            if ((f = a.ByKind[(int)ProcKind.HitCount]) != null && f.Count > 0)
            {
                var st = St(p.Slot, f.Kind);
                if (++st.Stacks >= f.Count)
                {
                    st.Stacks = 0;
                    mul *= 1 + f.Power / 100.0;
                    if (f.HitEffect != 0 && e.Def.EffectResist < 100)
                    {
                        int t = Tick + Ticks(f.HitEffectSeconds * Math.Max(0, 1 - e.Def.EffectResist / 100.0));
                        if (f.HitEffect == EffectKind.Stun) e.StunUntil = Math.Max(e.StunUntil, t);
                        else if (f.HitEffect == EffectKind.Freeze) e.FrozenUntil = Math.Max(e.FrozenUntil, t);
                    }
                    Fired(p, f, e.Slot, (int)Math.Round(f.Power), e.X, e.Y);
                }
            }
            // CritCount: every Count-th critical drops a clover
            if (crit && (f = a.ByKind[(int)ProcKind.CritCount]) != null && f.Count > 0)
            {
                var st = St(p.Slot, f.Kind);
                if (++st.Stacks >= f.Count)
                {
                    st.Stacks = 0;
                    var pd = C.Pickup(f.PickupId);
                    if (pd != null) AddPickup(pd, e.X, e.Y, Math.Max(1, (int)Math.Round(f.Power)));
                    Fired(p, f, e.Slot, (int)Math.Round(f.Power), e.X, e.Y);
                }
            }
            // Mark: Count hits of this runner's shots on one enemy
            if (ow.Def.Kind == WeaponKind.Bolt && (f = a.ByKind[(int)ProcKind.Mark]) != null && f.Count > 0)
            {
                int key = e.Slot * 256 + e.Serial, at = -1;
                for (int i = 0; i < a.MarkKey.Length; i++) if (a.MarkHits[i] > 0 && a.MarkKey[i] == key) { at = i; break; }
                if (at < 0) { at = a.MarkAt; a.MarkAt = (a.MarkAt + 1) % a.MarkKey.Length; a.MarkKey[at] = key; a.MarkHits[at] = 0; }
                if (++a.MarkHits[at] >= f.Count)
                {
                    a.MarkHits[at] = 0;
                    _markUntil[e.Slot] = Tick + Math.Max(1, f.Seconds); _markSerial[e.Slot] = e.Serial; _markPct[e.Slot] = f.Power; _markBy[e.Slot] = p.Slot;
                    St(p.Slot, f.Kind).Stacks = (St(p.Slot, f.Kind).Stacks + 1) & 255;
                    Fired(p, f, e.Slot, Math.Max(1, f.Seconds), e.X, e.Y);
                }
            }
            // FullSwing: a swing that reaches Count enemies gives part of its wait back (once per wait)
            if (ow.Def.Kind == WeaponKind.MeleeArc && (f = a.ByKind[(int)ProcKind.FullSwing]) != null && f.Count > 0)
            {
                if (a.SwingOw != ow || a.SwingTick != Tick) { a.SwingOw = ow; a.SwingTick = Tick; a.SwingHits = 0; a.SwingDone = false; }
                if (++a.SwingHits >= f.Count && !a.SwingDone)
                {
                    a.SwingDone = true;
                    int wait = CooldownTicks(p, ow.Lv), back = (int)Math.Round(wait * Math.Min(90, f.Power) / 100.0);
                    // once per use: not again before the shortened wait has passed (a burst's later swings are the same use)
                    if (!a.LastRefund.TryGetValue(ow, out int last) || Tick - last >= wait - back)
                    {
                        a.LastRefund[ow] = Tick; a.Refunds.Add(new KeyValuePair<OwnedWeapon, int>(ow, back));
                        Fired(p, f, e.Slot, back, p.X, p.Y);
                    }
                }
            }
            // Stride: the charge of a walk goes into the next hit
            if ((f = a.ByKind[(int)ProcKind.Stride]) != null)
            {
                var st = St(p.Slot, f.Kind);
                if (st.On) { st.On = false; mul *= 1 + f.Power / 100.0; Fired(p, f, e.Slot, (int)Math.Round(f.Power), e.X, e.Y); }
            }
            // IdleCharge: a hit after Seconds without one
            if ((f = a.ByKind[(int)ProcKind.IdleCharge]) != null)
            {
                var st = St(p.Slot, f.Kind);
                if (st.Since == int.MinValue || Tick - st.Since >= Math.Max(1, f.Seconds)) { mul *= 1 + f.Power / 100.0; Fired(p, f, e.Slot, (int)Math.Round(f.Power), e.X, e.Y); }
                st.Since = Tick;
            }
            // TimerCharge: every Seconds the next use of a weapon of the class deals more (every hit of that use, this tick)
            if ((f = a.ByKind[(int)ProcKind.TimerCharge]) != null && (f.ClassMask == 0 || (f.ClassMask >> (int)ow.Def.Class & 1) != 0))
            {
                var st = St(p.Slot, f.Kind);
                if (st.ChargedBy == ow && st.ChargedAt == Tick) mul *= 1 + f.Power / 100.0;
                else if (st.Next != int.MinValue && Tick >= st.Next)
                {
                    st.ChargedBy = ow; st.ChargedAt = Tick; st.Next = Tick + Math.Max(1, f.Seconds);
                    mul *= 1 + f.Power / 100.0;
                    Fired(p, f, e.Slot, (int)Math.Round(f.Power), e.X, e.Y);
                }
            }
            return mul;
        }

        // =============================================================================================== a hit taken

        /// <summary>HurtPlayer's hook: the runner dodged.</summary>
        private void PassiveDodged(SimPlayer p, int enemyDefId, int x, int y)
        {
            var a = Agg(p);
            if (a.Fams.Count == 0) return;
            ProcFam f;
            if ((f = a.ByKind[(int)ProcKind.DodgeCounter]) != null && f.Count > 0)
            {
                var st = St(p.Slot, f.Kind);
                if (++st.Stacks >= f.Count)
                {
                    st.Stacks = 0;
                    int r = Math.Max(Units(0.5), f.Area), kb = HordeMove.ImpulseFor(Units(1), _kbKeepPct), n = 0;
                    for (int i = 0; i < Enemies.Length; i++)
                    {
                        var e = Enemies[i]; int rr = r + e.Radius;
                        if (!e.Alive || Dist2(e.X, e.Y, p.X, p.Y) > (long)rr * rr) continue;
                        n++;
                        if (ProcStrike(p, e, f.Damage, DamageClass.Melee, p.X, p.Y)) continue;
                        if (!CanKnockback(e)) continue;
                        long dx = e.X - p.X, dy = e.Y - p.Y, d = Math.Max(1, HordeMove.ISqrt(dx * dx + dy * dy));
                        double resist = Math.Max(0, 1 - e.Def.KnockbackResist / 100.0);
                        e.KbVx = Clamp(e.KbVx + (int)(dx * kb * resist / d), -8 * U, 8 * U); e.KbVy = Clamp(e.KbVy + (int)(dy * kb * resist / d), -8 * U, 8 * U);
                    }
                    Fired(p, f, -1, n, p.X, p.Y);
                }
            }
            if ((f = a.ByKind[(int)ProcKind.DodgeReady]) != null && f.Arg > 0)
                foreach (var ow in p.Weapons) if (ow.Def.Id == f.Arg && ow.NextTick > Tick) { ow.NextTick = Tick; Fired(p, f, -1, f.Arg, p.X, p.Y); break; }
        }

        /// <summary>HurtPlayer's hook before the HP: a hit kept off (Gangster's Paradise, Blade Stop, a shell) or soaked (a coat).
        /// True: nothing more of it lands. <paramref name="taken"/> becomes what is left.</summary>
        private bool PassiveBlocks(SimPlayer p, int dmg, ref int taken, int enemyDefId, int fromEnemy, HordeHitVia via)
        {
            var a = Agg(p);
            if (a.Fams.Count == 0) return false;
            ProcFam f;
            var src = C.Enemy(enemyDefId);
            if ((f = a.ByKind[(int)ProcKind.Gangster]) != null && St(p.Slot, f.Kind).On && src != null && src.Rank == EnemyRank.Normal && Toucher(fromEnemy, via) >= 0)
                return true;
            if ((f = a.ByKind[(int)ProcKind.BossBlock]) != null && src != null && src.Rank >= EnemyRank.Elite)
            {
                var st = St(p.Slot, f.Kind);
                if (Tick >= st.Next) { st.Next = Tick + f.Cooldown; Fired(p, f, -1, taken, p.X, p.Y); return true; }
            }
            if ((f = a.ByKind[(int)ProcKind.Shell]) != null)
            {
                var st = St(p.Slot, f.Kind);
                if (st.Since != int.MinValue && Tick - st.Since >= Math.Max(1, f.Seconds)) { st.Since = Tick; Fired(p, f, -1, taken, p.X, p.Y); return true; }
            }
            if ((f = a.ByKind[(int)ProcKind.Coat]) != null)
            {
                var st = St(p.Slot, f.Kind);
                st.Since = Tick;
                if (st.Pool > 0 && taken > 0)
                {
                    int soak = Math.Min(st.Pool, taken);
                    st.Pool -= soak; taken -= soak;
                    Fired(p, f, -1, soak, p.X, p.Y, HordeEventFlags.Dot); // (Dot: a soak, not a refill)
                    if (taken <= 0) return true;
                }
            }
            return false;
        }

        /// <summary>HurtPlayer's hook after the HP: the dodge streak breaks; a counter, a reflection, Hiding.</summary>
        private void PassiveHurt(SimPlayer p, int dmg, int taken, int enemyDefId, int fromEnemy, HordeHitVia via)
        {
            var a = Agg(p);
            if (a.Fams.Count == 0) return;
            ProcFam f;
            if ((f = a.ByKind[(int)ProcKind.DodgeCounter]) != null) St(p.Slot, f.Kind).Stacks = 0;
            if ((f = a.ByKind[(int)ProcKind.Coat]) != null) St(p.Slot, f.Kind).Since = Tick;
            int toucher = -2;
            if ((f = a.ByKind[(int)ProcKind.HurtCounter]) != null && f.Damage > 0)
            {
                var st = St(p.Slot, f.Kind);
                if (Tick >= st.Next && (toucher = Toucher(fromEnemy, via)) >= 0)
                {
                    st.Next = Tick + f.Cooldown;
                    var e = Enemies[toucher];
                    Fired(p, f, toucher, (int)Math.Round(f.Damage), e.X, e.Y);
                    ProcStrike(p, e, f.Damage, DamageClass.Melee, p.X, p.Y);
                }
            }
            if ((f = a.ByKind[(int)ProcKind.Reflect]) != null && f.Power > 0)
            {
                if (toucher == -2) toucher = Toucher(fromEnemy, via);
                if (toucher >= 0 && Enemies[toucher].Alive)
                {
                    var e = Enemies[toucher];
                    int back = Math.Max(1, (int)Math.Round(dmg * f.Power / 100.0));
                    Fired(p, f, toucher, back, e.X, e.Y);
                    DamageEnemy(e, back, p.Slot, 0, false, false, p.X, p.Y);
                }
            }
            if ((f = a.ByKind[(int)ProcKind.LowHpHide]) != null && p.Hp > 0 && (long)p.Hp * 100 < (long)f.Threshold * p.MaxHp)
            {
                var st = St(p.Slot, f.Kind);
                if (Tick >= st.Next)
                {
                    st.On = true; st.Until = Tick + Math.Max(1, f.Seconds); st.Next = Tick + f.Cooldown;
                    p.SafeUntil = Math.Max(p.SafeUntil, st.Until);
                    Fired(p, f, -1, f.Seconds, p.X, p.Y);
                }
            }
        }

        // =============================================================================================== a kill, a pickup

        /// <summary>EnemyDied's hook (a kill by <paramref name="p"/>).</summary>
        private void PassiveKill(SimPlayer p, SimEnemy e)
        {
            var a = Agg(p);
            if (a.Fams.Count == 0) return;
            ProcFam f;
            if ((f = a.ByKind[(int)ProcKind.Bane]) != null && f.Look != null)
            {
                bool target = false; foreach (var op in f.Members) if (op.Def.Proc.Targets(e.Def)) { target = true; break; }
                var st = St(p.Slot, f.Kind);
                if (target && Tick >= st.Next && _rng.Chance(f.Chance))
                {
                    st.Next = Tick + f.Cooldown;
                    var pd = C.Pickup(f.PickupId);
                    if (pd != null) AddPickup(pd, e.X, e.Y, pd.Value);
                    Fired(p, f, e.Slot, pd != null ? pd.Value : 0, e.X, e.Y);
                }
            }
            if ((f = a.ByKind[(int)ProcKind.KillHeal]) != null && !p.Fallen && a.HitTick == Tick && a.HitEnemy == e.Slot && a.HitOw != null
                && (f.ClassMask == 0 || (f.ClassMask >> (int)a.HitOw.Def.Class & 1) != 0))
            {
                var st = St(p.Slot, f.Kind);
                st.Carry += f.Damage;
                if (st.Carry >= 1) { int h = (int)st.Carry; st.Carry -= h; int before = p.Hp; p.Hp = Math.Min(p.MaxHp, p.Hp + h); if (p.Hp > before) Fired(p, f, e.Slot, p.Hp - before, p.X, p.Y); }
            }
            if ((f = a.ByKind[(int)ProcKind.KillDrop]) != null && f.Chance > 0 && _rng.Chance(f.Chance))
            {
                var pd = C.Pickup(f.PickupId);
                if (pd != null) { AddPickup(pd, e.X, e.Y, pd.Value); Fired(p, f, e.Slot, pd.Value, e.X, e.Y); }
            }
        }

        /// <summary>TakePickup's hook: a pickup of kind Passive (an arrow bundle: +Value shots for KillDrop's Seconds).</summary>
        private void PassivePickup(SimPlayer p, SimPickup k)
        {
            if (k.Def.Kind != PickupKind.Passive) return;
            var f = Fam(p, ProcKind.KillDrop);
            int until = Tick + (f != null && f.Seconds > 0 ? f.Seconds : Ticks(10));
            int id = ProcBuffId - 100 - k.Def.Id;
            var b = p.Buffs.Find(x => x.WeaponId == id && x.SourceSlot == p.Slot);
            if (b == null) { p.Buffs.Add(new SimBuff { Mods = new[] { new ModDef(Mod.ProjectileCount, Math.Max(1, k.Value)) }, Until = until, SourceSlot = p.Slot, WeaponId = id }); Recalc(p, false); }
            else b.Until = Math.Max(b.Until, until);
            if (f != null) Fired(p, f, -1, Math.Max(1, k.Value), p.X, p.Y);
        }

        // =============================================================================================== the snapshot's view

        /// <summary>The runner's family states for the wire (4 bytes each: kind, charge, stacks, flags), in its passives' order.
        /// Returns how many were written into <paramref name="into"/> (at least 4 × <see cref="ProcKindSlots"/> bytes).</summary>
        public int ProcStates(int slot, byte[] into)
        {
            var p = Player(slot);
            if (p == null) return 0;
            var a = Agg(p);
            int n = 0;
            foreach (var f in a.Fams)
            {
                var st = St(slot, f.Kind);
                int charge = 0, flags = 0, stacks = st.Stacks;
                int secs = Math.Max(1, f.Seconds);
                switch (f.Kind)
                {
                    case ProcKind.DodgeCounter: case ProcKind.HitCount: case ProcKind.CritCount:
                        charge = f.Count > 0 ? st.Stacks * 255 / f.Count : 0; break;
                    case ProcKind.Shell:
                        charge = st.Since == int.MinValue ? 0 : Math.Min(255, (Tick - st.Since) * 255 / secs);
                        if (charge >= 255) flags |= HordeProcFlags.Ready; break;
                    case ProcKind.StillBoost: case ProcKind.MoveBoost:
                        charge = Math.Min(255, st.Acc * 255 / secs); break;
                    case ProcKind.Coat:
                        charge = st.PoolMax > 0 ? st.Pool * 255 / st.PoolMax : 0; if (st.PoolMax > 0 && st.Pool >= st.PoolMax) flags |= HordeProcFlags.Ready; break;
                    case ProcKind.Stride:
                        charge = st.On ? 255 : Math.Min(254, st.Acc * 255 / secs); if (st.On) flags |= HordeProcFlags.Ready; break;
                    case ProcKind.MagnetPulse: case ProcKind.TimerCharge:
                        charge = st.Next == int.MinValue ? 0 : Math.Max(0, Math.Min(255, 255 - (st.Next - Tick) * 255 / secs)); if (charge >= 255) flags |= HordeProcFlags.Ready; break;
                    case ProcKind.IdleCharge:
                        charge = st.Since == int.MinValue ? 255 : Math.Min(255, (Tick - st.Since) * 255 / secs); if (charge >= 255) flags |= HordeProcFlags.Ready; break;
                    case ProcKind.Bane: case ProcKind.HurtCounter: case ProcKind.BossBlock: case ProcKind.LowHpHide:
                    {
                        int cd = Math.Max(1, f.Cooldown);
                        charge = st.Next == int.MinValue || Tick >= st.Next ? 255 : Math.Max(0, 255 - (st.Next - Tick) * 255 / cd);
                        if (charge >= 255) flags |= HordeProcFlags.Ready; break;
                    }
                    case ProcKind.Cloak: case ProcKind.Gangster:
                        charge = st.On ? Math.Max(0, Math.Min(255, (st.Until - Tick) * 255 / Math.Max(1, f.AreaTicks))) : Math.Min(255, st.Acc * 255 / secs);
                        if (!st.On && Tick >= st.Next) flags |= HordeProcFlags.Ready; break;
                    case ProcKind.Mark:
                    {
                        int m = 0; for (int i = 0; i < _markUntil.Length; i++) if (_markUntil[i] > Tick && _markBy[i] == slot) m++;
                        stacks = m; charge = Math.Min(255, m * 64); break;
                    }
                }
                if (st.On) flags |= HordeProcFlags.Active;
                into[n * 4] = (byte)f.Kind; into[n * 4 + 1] = (byte)Math.Max(0, Math.Min(255, charge)); into[n * 4 + 2] = (byte)Math.Max(0, Math.Min(255, stacks)); into[n * 4 + 3] = (byte)flags;
                if (++n >= ProcKindSlots) break;
            }
            return n;
        }
    }
}

namespace Loki.Horde.Net
{
    /// <summary>
    /// Client wave 17 (h17pass): H_SNAP's passives block, ONE block at the very end of the shared body (after the events):
    ///   u8 n × (u8 slot, u8 m × (u8 proc kind, u8 charge 0..255, u8 stacks, u8 flags (1 ready, 2 active)))
    /// A key carries every present runner (m may be 0); a delta only the runners whose states changed since the last snapshot.
    /// </summary>
    public sealed class HordePassiveBlock
    {
        private readonly byte[][] _cur = new byte[HordeLimits.MaxPlayers][], _told = new byte[HordeLimits.MaxPlayers][];
        private readonly int[] _curN = new int[HordeLimits.MaxPlayers], _toldN = new int[HordeLimits.MaxPlayers];
        private readonly bool[] _present = new bool[HordeLimits.MaxPlayers];
        private HordeSim _sim;

        public HordePassiveBlock() { for (int i = 0; i < _cur.Length; i++) { _cur[i] = new byte[HordeSim.ProcKindSlots * 4]; _told[i] = new byte[HordeSim.ProcKindSlots * 4]; _toldN[i] = -1; } }

        /// <summary>Once per snapshot, before its bodies are built: what was told last time is told; the states now.</summary>
        public void Begin(HordeSim sim)
        {
            _sim = sim;
            for (int s = 0; s < _cur.Length; s++)
            {
                if (_present[s]) { Buffer.BlockCopy(_cur[s], 0, _told[s], 0, _curN[s] * 4); _toldN[s] = _curN[s]; }
                var p = sim.Players[s];
                _present[s] = p.Present;
                _curN[s] = p.Present ? sim.ProcStates(s, _cur[s]) : 0;
                if (!p.Present) _toldN[s] = -1;
            }
        }

        private bool Changed(int s)
        {
            if (_toldN[s] != _curN[s]) return true;
            for (int i = 0; i < _curN[s] * 4; i++) if (_cur[s][i] != _told[s][i]) return true;
            return false;
        }

        public void Write(HordeWire.W w, bool key)
        {
            int n = 0;
            for (int s = 0; s < _cur.Length; s++) if (_present[s] && (key || Changed(s))) n++;
            w.U8(n);
            for (int s = 0; s < _cur.Length; s++)
            {
                if (!_present[s] || !(key || Changed(s))) continue;
                w.U8(s).U8(_curN[s]).Raw(_cur[s], 0, _curN[s] * 4);
            }
        }

        /// <summary>The client's side: the block into the runners' views (none = nothing changed; a host without the block: nothing).</summary>
        public static void Read(ref HordeWire.R r, HordeWorldView v)
        {
            if (r.Left <= 0) return;
            int n = r.U8();
            for (int k = 0; k < n && !r.Bad; k++)
            {
                int slot = r.U8(), m = r.U8();
                if (slot >= HordeLimits.MaxPlayers || m > HordeSim.ProcKindSlots) { r.Bad = true; return; }
                var list = v.Players[slot].Procs;
                list.Clear();
                for (int i = 0; i < m && !r.Bad; i++) list.Add(new HordeProcView { Kind = (ProcKind)r.U8(), Charge = (byte)r.U8(), Stacks = (byte)r.U8(), Flags = (byte)r.U8() });
            }
        }
    }
}
