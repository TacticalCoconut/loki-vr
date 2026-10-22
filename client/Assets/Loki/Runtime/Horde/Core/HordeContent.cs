#nullable disable
using System;
using System.Collections.Generic;

namespace Loki.Horde
{
    // ====================================================================================================================
    // Horde (client wave 8): the content types. The engine (HordeSim, this folder) owns the rules; the tables that say WHAT is
    // in the game are data in these types, written by w8content in Horde/Core/Content/ (HordeTables.Build()).
    //
    // Pure C#: no UnityEngine, C# 9 / .NET Standard 2.1. Loki Host compiles this folder too (tools/lokihost/Horde): the
    // simulation runs there, the client only draws, so both ends must hold THE SAME tables (HordeContent.Stamp() is compared
    // when a client says hello to the mode).
    //
    // Units for authors: distances in CELLS (1 = one GAT cell), times in SECONDS, percentages in PERCENT (10 = 10 %),
    // damage / HP / EXP as whole numbers. The engine turns them into fixed-point once, when a run is made (it simulates in
    // integers: 1 cell = 256 units, 20 ticks a second), so a table never needs to care about ticks.
    //
    // Every number in these tables is LOKI'S OWN DESIGN (damage, cooldowns, waves, rewards): none is an official Ragnarok
    // value. What refers to Ragnarok refers to it by rAthena's ids only (job ids of common/mmo.hpp e_job, skill ids of
    // db/re/skill_db.yml, mob ids of db/re/mob_db.yml, item ids of db/re/item_db_*.yml): names, sprites, icons and sounds
    // come from the player's own install through the client's tables, never from this repository.
    // ====================================================================================================================

    /// <summary>The engine's weapon vocabulary: what a Ragnarok skill is mapped onto. Which fields of <see cref="WeaponLevel"/>
    /// each kind reads is written on that class.</summary>
    public enum WeaponKind : byte
    {
        /// <summary>Projectiles at a target: Count, Pierce, Speed, Range; Area &gt; 0 makes each burst on its first hit.</summary>
        Bolt = 1,
        /// <summary>A swing: every enemy within Range and within Arc degrees of the aim.</summary>
        MeleeArc = 2,
        /// <summary>A burst around the player: every enemy within Area.</summary>
        Nova = 3,
        /// <summary>An area at the target's place, striking after Delay.</summary>
        AreaAtTarget = 4,
        /// <summary>A lasting ground zone (damage, heal allies, slow, block): Area, Duration, Tick.</summary>
        GroundZone = 5,
        /// <summary>Count orbs circling the player at Range, each Area wide.</summary>
        Orbiter = 6,
        /// <summary>A hit that jumps between enemies: Count jumps of at most Area cells.</summary>
        Chain = 7,
        /// <summary>A periodic buff / heal for self and allies within Area.</summary>
        Aura = 8,
        /// <summary>Allied units that fight beside the player.</summary>
        Summon = 9,
        /// <summary>Traps laid where the player walks; they go off when an enemy steps within Area.</summary>
        Trap = 10,
        // client wave 17 (h17cards): design 1 / point 15's new kinds (their fields: HordeContent.Cards.cs)
        /// <summary>The player dashes (WeaponDef.DashOn says when): Range cells toward its target (or away from what hit it), hitting
        /// every enemy within Area of its path once (Damage, Effects); it cannot be hurt while it dashes.</summary>
        Dash = 11,
        /// <summary>Marks enemies (one, or every one within Area / Arc): they take MarkPct more damage (for MarkHits hits, 0 = until the
        /// mark ends), deal WeakenPct less, rush the marker (Taunt), for Duration.</summary>
        Mark = 12,
        /// <summary>A guard on the player: Count charges, each blocks one hit of the kinds in WeaponDef.Parry (from within Arc of where it
        /// walks; 0 = all around); Duration 0 = always up (a charge comes back every Cooldown), else it lasts Duration and comes back after
        /// Cooldown once broken. WeaponDef.Parry Returns: a blocked shot flies back (Damage + its own).</summary>
        Parry = 13,
        /// <summary>One companion that follows the player for the whole run (WeaponDef.Pet: strikes, casts, heals or guards).</summary>
        Pet = 14,
    }

    /// <summary>Where a weapon aims.</summary>
    public enum Targeting : byte
    {
        /// <summary>The nearest enemy within Range (the default).</summary>
        Nearest = 0,
        /// <summary>A random enemy within Range.</summary>
        Random = 1,
        /// <summary>The enemy with the most HP within Range (elites and bosses first).</summary>
        Strongest = 2,
        /// <summary>The farthest enemy within Range.</summary>
        Farthest = 3,
        /// <summary>The player's own walking direction (its last one while it stands).</summary>
        Facing = 4,
        /// <summary>The player's own place (novas, auras, zones laid under oneself).</summary>
        Self = 5,
        /// <summary>The ally (or oneself) with the lowest HP share within Range (heals).</summary>
        LowestHpAlly = 6,
        /// <summary>A random ally within Range, oneself included.</summary>
        RandomAlly = 7,
        /// <summary>The enemy within Range that has the most other enemies within Area of it (areas, zones).</summary>
        Densest = 8,
    }

    /// <summary>How a weapon's damage is counted: which modifiers and which stats raise it.</summary>
    public enum DamageClass : byte { Melee = 0, Ranged = 1, Magic = 2 }

    /// <summary>rAthena's e_element (src/map/map.hpp:390-401), same numbers.</summary>
    public enum Element : byte { Neutral = 0, Water = 1, Earth = 2, Fire = 3, Wind = 4, Poison = 5, Holy = 6, Dark = 7, Ghost = 8, Undead = 9 }

    /// <summary>rAthena's e_race (src/map/map.hpp:324-335), same numbers.</summary>
    public enum Race : byte { Formless = 0, Undead = 1, Brute = 2, Plant = 3, Insect = 4, Fish = 5, Demon = 6, Demihuman = 7, Angel = 8, Dragon = 9 }

    /// <summary>rAthena's e_size (SZ_SMALL, SZ_MEDIUM, SZ_BIG), same numbers.</summary>
    public enum UnitSize : byte { Small = 0, Medium = 1, Large = 2 }

    /// <summary>Ragnarok's six stats, as a way to give modifiers (<see cref="HordeContent.Stats"/> says what a point is worth).</summary>
    public enum Stat : byte { Str = 0, Agi = 1, Vit = 2, Int = 3, Dex = 4, Luk = 5 }

    /// <summary>What a weapon's hit does to an enemy besides damage.</summary>
    public enum EffectKind : byte
    {
        /// <summary>Power = how many percent slower it walks, for Duration.</summary>
        Slow = 1,
        /// <summary>Cannot move or attack for Duration (drawn frozen).</summary>
        Freeze = 2,
        /// <summary>Cannot move or attack for Duration (drawn stunned).</summary>
        Stun = 3,
        /// <summary>Power = damage every Tick seconds for Duration (the weapon's element says how it is drawn: burning, poison …).</summary>
        Dot = 4,
        /// <summary>Power = cells pushed away from the hit's source.</summary>
        Knockback = 5,
    }

    /// <summary>One modifier. Percent kinds add up (two +10 % make +20 %); see each member for its unit.</summary>
    public enum Mod : byte
    {
        /// <summary>Flat max HP.</summary>
        MaxHp = 1,
        MaxHpPct = 2,
        /// <summary>HP per second (fractions allowed: 0.5).</summary>
        Regen = 3,
        MoveSpeedPct = 4,
        /// <summary>All damage.</summary>
        DamagePct = 5,
        MeleeDamagePct = 6,
        RangedDamagePct = 7,
        MagicDamagePct = 8,
        /// <summary>-10 = weapons come back 10 % sooner (the engine never goes below 25 % of a cooldown).</summary>
        CooldownPct = 9,
        AreaPct = 10,
        /// <summary>Whole extra projectiles / orbs / jumps / strikes (added to Count of the kinds that count).</summary>
        ProjectileCount = 11,
        ProjectileSpeedPct = 12,
        /// <summary>How long zones, orbs, buffs and summons last.</summary>
        DurationPct = 13,
        PickupRadiusPct = 14,
        /// <summary>Chance in percent of a critical hit.</summary>
        CritChance = 15,
        /// <summary>Added to the rules' critical multiplier (RulesDef.CritDamagePct).</summary>
        CritDamagePct = 16,
        ExpGainPct = 17,
        /// <summary>Flat damage taken off every hit received (never below 1).</summary>
        Defence = 18,
        /// <summary>-10 = takes 10 % less damage (never below 10 % of a hit).</summary>
        DamageTakenPct = 19,
        /// <summary>Chance in percent to take nothing from a hit (capped at 75).</summary>
        Dodge = 20,
        /// <summary>What this player's heals and healing zones give.</summary>
        HealPowerPct = 21,
        /// <summary>How fast this player rises after a fall.</summary>
        ReviveSpeedPct = 22,
        /// <summary>Stat points (a passive may give "+3 STR"): worth what HordeContent.Stats says.</summary>
        Str = 23, Agi = 24, Vit = 25, Int = 26, Dex = 27, Luk = 28,
        /// <summary>Damage against enemies of the race in Arg (a Race).</summary>
        DamageVsRacePct = 29,
        /// <summary>Damage against enemies whose element is Arg (an Element).</summary>
        DamageVsElementPct = 30,
        /// <summary>Damage of this player's weapons whose element is Arg (an Element).</summary>
        ElementDamagePct = 31,
        /// <summary>Damage against elites and bosses.</summary>
        DamageVsBossPct = 32,
        /// <summary>Chance of the weapons' own effects (EffectDef.Chance is multiplied by 100 % + this).</summary>
        EffectChancePct = 33,
        KnockbackPct = 34,
        /// <summary>HP healed per enemy killed by this player (fractions allowed).</summary>
        HpOnKill = 35,
        // client wave 17 (h17cards): the new cards' modifiers (read by HordeSim.Cards.cs; HordeSim.AddMod ignores them)
        /// <summary>Damage against enemies of the size in Arg (a UnitSize).</summary>
        DamageVsSizePct = 36,
        /// <summary>-10 = 10 % less damage from enemies of the race in Arg (a Race).</summary>
        DamageTakenVsRacePct = 37,
        /// <summary>-10 = 10 % less damage from enemies of the element in Arg (an Element: the hitting enemy's own).</summary>
        DamageTakenVsElementPct = 38,
        /// <summary>Chance in percent that a hit stuns for 0.6 s (Arg 1: melee hits only, 1 s).</summary>
        StunOnHitPct = 39,
        /// <summary>Melee swings this many percent wider.</summary>
        ArcPct = 40,
        /// <summary>HP per second while walking.</summary>
        MovingRegen = 41,
        /// <summary>This player's Neutral weapons take the element in Arg (Value 1: every weapon, 2: ranged weapons only).</summary>
        EndowElement = 42,
        /// <summary>Chance in percent that a chest gives one more upgrade.</summary>
        ChestBonusPct = 43,
        /// <summary>Melee damage while walking.</summary>
        MovingMeleePct = 44,
        /// <summary>Chance in percent that a hit poisons (Value; the poison: 3 damage a second per level of the passive, 4 s).</summary>
        PoisonOnHitPct = 45,
        /// <summary>Every melee hit strikes again at this many percent of its damage.</summary>
        OffhandPct = 46,
        /// <summary>Traps arm this many percent sooner (Arg 0) / are this many percent wider (Arg 1).</summary>
        TrapPct = 47,
        /// <summary>Chance in percent that an aura pulses a second time (0.5 s later).</summary>
        EchoPct = 48,
        /// <summary>Max HP lost each second, in percent (never below 25 % of max HP).</summary>
        HpDrainPct = 49,
        /// <summary>Damage of the weapon whose id is Arg.</summary>
        WeaponDamagePct = 50,
        /// <summary>-10 = the weapon whose id is Arg comes back 10 % sooner.</summary>
        WeaponCooldownPct = 51,
        /// <summary>Damage of magic weapons against elites and bosses.</summary>
        MagicVsBossPct = 52,
        /// <summary>-10 = this player and allies within 6 cells take 10 % less from Demons (race) and Undead (element) (the best one counts).</summary>
        ProvidencePct = 53,
        /// <summary>Chance in percent (Arg 0) that a kill drops a small chest; (Arg 1) that an elite or boss drops one more chest.</summary>
        ChestDropPct = 54,
        /// <summary>Percent of max HP healed by an EXP pickup (at most once every 2 s).</summary>
        ExpPickupHealPct = 55,
        /// <summary>Chance in percent that a hit drops a small EXP pickup (once per enemy).</summary>
        StealPct = 56,
        /// <summary>An enemy area strike within Value cells of this player is cancelled (once every CancelStrikeEvery seconds).</summary>
        CancelStrikeCells = 57,
        /// <summary>The last enemy shot that hit this player is fired back for 10 s, at 100 % + Value percent of its damage.</summary>
        CopyShotPct = 58,
        /// <summary>Seconds between two cancelled strikes (CancelStrikeCells); the shortest counts.</summary>
        CancelStrikeEvery = 59,
    }

    public sealed class ModDef
    {
        public Mod Kind;
        public float Value;
        /// <summary>The race / element of the kinds that name one; 0 otherwise.</summary>
        public int Arg;
        public ModDef() { }
        public ModDef(Mod kind, float value, int arg = 0) { Kind = kind; Value = value; Arg = arg; }
    }

    public sealed class EffectDef
    {
        public EffectKind Kind;
        /// <summary>Chance in percent per hit (100 = always).</summary>
        public float Chance = 100f;
        /// <summary>Seconds (Slow, Freeze, Stun, Dot).</summary>
        public float Duration;
        /// <summary>Slow: percent slower. Dot: damage per tick. Knockback: cells.</summary>
        public float Power;
        /// <summary>Dot: seconds between its ticks.</summary>
        public float Tick = 1f;
        public EffectDef() { }
        public EffectDef(EffectKind kind, float chance, float duration, float power = 0f, float tick = 1f) { Kind = kind; Chance = chance; Duration = duration; Power = power; Tick = tick; }
    }

    /// <summary>
    /// One level of a weapon. Which fields a kind reads (the others are ignored for it):
    ///   Bolt          Damage, Cooldown, Count, Pierce, Speed, Range (how far it flies; also the aiming range), Spread
    ///                 (degrees between the Count bolts; 0 = one after another, Burst seconds apart), Area (&gt; 0: it bursts
    ///                 on its first hit and damages every enemy within Area; else it hits what it touches), Falloff, Effects
    ///   MeleeArc      Damage, Cooldown, Count (swings, Burst seconds apart), Range (reach), Arc (degrees, 360 = all around), Effects
    ///   Nova          Damage, Cooldown, Area (radius around the player), Count (bursts, Burst seconds apart), Effects
    ///   AreaAtTarget  Damage, Cooldown, Count (strikes, each on its own target, Burst seconds apart), Range (aiming), Area
    ///                 (radius), Delay (seconds between the warning and the strike), Effects
    ///   GroundZone    Damage (per Tick, to enemies inside), Heal / HealPct (per Tick, to allies inside), Cooldown, Count (zones
    ///                 per use), Range (aiming), Area (radius), Duration, Tick, Effects (each Tick), Buffs (allies inside, while
    ///                 inside), and WeaponDef.Zone (blocks enemies, blocks shots, follows the owner)
    ///   Orbiter       Damage, Count (orbs), Range (the circle's radius), Area (an orb's radius), Speed (TURNS per second),
    ///                 Tick (seconds before the same orb may hit the same enemy again), Duration + Cooldown (up for Duration,
    ///                 then away for Cooldown; Duration 0 = always up), Effects
    ///   Chain         Damage, Cooldown, Range (aiming, first target), Count (jumps after the first hit), Area (the longest
    ///                 jump), Falloff, Effects
    ///   Aura          Cooldown (seconds between pulses), Area (radius around the player; allies inside, and the player),
    ///                 Buffs (kept for Duration), Heal / HealPct (per pulse), Damage (per pulse to enemies inside, if any),
    ///                 Effects; with Targeting.LowestHpAlly: one ally within Range instead of everyone in Area (a heal)
    ///   Summon        Count (alive at once), Cooldown (seconds to call one more while below Count), Duration (its life; 0 =
    ///                 until it falls), Damage (per attack), Tick (seconds between its attacks), Range (its reach), Speed
    ///                 (cells per second), Area (&gt; 0: its attack hits every enemy within Area of its target), Effects, and
    ///                 WeaponDef.SummonMobId / SummonHp
    ///   Trap          Damage, Cooldown (seconds between traps laid at the player's feet), Count (alive at once; the oldest
    ///                 goes), Delay (seconds until armed), Area (it goes off when an enemy is within Area, and hits every
    ///                 enemy within Area), Duration (how long an unsprung trap stays), Effects
    /// </summary>
    public sealed partial class WeaponLevel // client wave 17 (h17cards): partial (its new fields: HordeContent.Cards.cs)
    {
        public int Damage;
        public float Cooldown = 1f;
        public int Count = 1;
        public int Pierce;
        public float Speed;
        public float Range;
        public float Area;
        public float Arc;
        public float Spread;
        public float Burst = 0.1f;
        public float Delay;
        public float Duration;
        public float Tick = 1f;
        /// <summary>Flat HP to allies (zones: per Tick; auras: per pulse).</summary>
        public int Heal;
        /// <summary>The same as a share of the healed ally's max HP, in percent, added to Heal.</summary>
        public float HealPct;
        /// <summary>Percent of the damage kept after each pierce (Bolt) or jump (Chain): 100 = no loss.</summary>
        public float Falloff = 100f;
        public EffectDef[] Effects;
        public ModDef[] Buffs;
    }

    [Flags]
    public enum ZoneFlags : byte
    {
        None = 0,
        /// <summary>Enemies cannot walk into it (an ice wall): they are pushed back to its edge. Bosses ignore it.</summary>
        BlocksEnemies = 1,
        /// <summary>Enemies' shots end at its edge (a pneuma).</summary>
        BlocksShots = 2,
        /// <summary>The zone stays centred on its owner (a sanctuary carried along).</summary>
        FollowsOwner = 4,
    }

    public sealed partial class WeaponDef // client wave 17 (h17cards): partial (its new fields: HordeContent.Cards.cs)
    {
        /// <summary>The mode's own id (1..65535, stable: it travels on the wire and is kept in results).</summary>
        public int Id;
        /// <summary>A name for logs, tests and the design document ("mg_firebolt"); never shown to a player.</summary>
        public string Key = "";
        /// <summary>The Ragnarok skill this is (rAthena's skill id): the client takes its name, icon, sound and effect from the
        /// player's install by it. 0 = none (the client then shows Key through Lang).</summary>
        public int SkillId;
        /// <summary>Client wave 9 (asked by h9content / h9flow): for a weapon without a skill, the Ragnarok item it stands for
        /// (rAthena's item id: its icon and name; nv_knife → 1201 Knife). 0 = none. The engine never reads it.</summary>
        public int ItemId;
        public WeaponKind Kind;
        public Targeting Target;
        public DamageClass Class;
        public Element Element;
        public ZoneFlags Zone;
        /// <summary>Bolt: it turns toward its target while it flies.</summary>
        public bool Homing;
        /// <summary>Summon: the monster it is drawn as (rAthena's mob id) and its HP (0 = it cannot be hurt).</summary>
        public int SummonMobId, SummonHp;
        /// <summary>How often the card is drawn relative to the others (100 = normal; 0 = never offered: only given, as a
        /// job's signature weapon or the starting weapon).</summary>
        public int Weight = 100;
        /// <summary>Levels[0] is level 1. Its length is the weapon's max level.</summary>
        public WeaponLevel[] Levels = new WeaponLevel[0];
    }

    public sealed partial class PassiveDef // client wave 17 (h17cards): partial (its new fields: HordeContent.Cards.cs)
    {
        /// <summary>The mode's own id (1..65535, its own number space, apart from the weapons').</summary>
        public int Id;
        public string Key = "";
        /// <summary>The Ragnarok passive skill it stands for (name and icon by the client's tables); 0 = none.</summary>
        public int SkillId;
        /// <summary>When the passive is shown as an item (a card, an accessory): rAthena's item id; 0 = none.</summary>
        public int ItemId;
        public int Weight = 100;
        /// <summary>Levels[n] = the modifiers the passive gives AT level n + 1 (not added to the lower levels').</summary>
        public ModDef[][] Levels = new ModDef[0][];
        /// <summary>Client wave 17 (h17pass), point 22: the passive's body and trigger (HordeSim.Passives.cs; set by passive id in
        /// Content/HordePassiveProcs.cs). Null = none.</summary>
        public PassiveProcDef Proc;
    }

    public sealed class StatDef
    {
        public Stat Stat;
        /// <summary>What ONE point of this stat gives (multiplied by the points held).</summary>
        public ModDef[] PerPoint = new ModDef[0];
    }

    public sealed class JobDef
    {
        /// <summary>rAthena's job id (common/mmo.hpp e_job: JOB_NOVICE 0, JOB_SWORDMAN 1 …): the run character is drawn with
        /// that job's sprite, and the id travels on the wire.</summary>
        public int JobId;
        /// <summary>rAthena's constant ("JOB_SWORDMAN"), for logs and the design document.</summary>
        public string Key = "";
        /// <summary>0 Novice, 1 first job, 2 second job, 3 transcendent, 4 third job, 5 fourth job.</summary>
        public int Tier;
        /// <summary>The jobs offered at the next mark (RulesDef.JobMarks[Tier]). One entry: the change happens by itself;
        /// none: the end of this line.</summary>
        public int[] Next = new int[0];
        /// <summary>The weapon given at the change (a WeaponDef id; 0 = none). If the player's weapon slots are full it is
        /// given all the same (a signature weapon does not take a slot).</summary>
        public int SignatureWeapon;
        /// <summary>The weapons and passives this job adds to the player's cards (ids). Earlier jobs' stay available.</summary>
        public int[] Weapons = new int[0];
        public int[] Passives = new int[0];
        /// <summary>What the job itself gives while it is the player's job (replaced by the next job's).</summary>
        public ModDef[] Mods = new ModDef[0];
        /// <summary>Stat points given once at the change (Str, Agi, Vit, Int, Dex, Luk); kept afterwards.</summary>
        public int[] StatBonus = new int[6];
        /// <summary>0 = any character; 1 = male characters only, 2 = female only (Bard / Dancer: rAthena pc.cpp:7739
        /// "case MAPID_BARDDANCER: return sex?JOB_BARD:JOB_DANCER;"). A job of the other sex is left out of Next; a player whose
        /// sex is not known (0) is offered both.</summary>
        public byte Sex;
    }

    public enum EnemyRank : byte { Normal = 0, Elite = 1, Boss = 2, FinalBoss = 3 }

    public enum EnemyBehavior : byte
    {
        /// <summary>Walks at the nearest standing player.</summary>
        Chase = 0,
        /// <summary>Chases; every ChargeCooldown it stops for ChargeWindup, then dashes ChargeSpeed in a straight line.</summary>
        Charge = 1,
        /// <summary>Chases until within ShotRange, then shoots every AttackInterval (ShotDamage, ShotSpeed).</summary>
        Ranged = 2,
        /// <summary>Crosses the arena in a straight line through where a player was, then is gone (a pack that sweeps by).</summary>
        Sweep = 3,
        /// <summary>Chases; on touching a player it bursts (Damage to every player within BurstArea) and dies.</summary>
        Exploder = 4,
        /// <summary>Stands where it appeared (a plant, a turret); shoots when ShotDamage &gt; 0.</summary>
        Still = 5,
    }

    public enum AbilityKind : byte
    {
        /// <summary>Calls Count enemies of EnemyId around itself.</summary>
        SummonAdds = 1,
        /// <summary>After Delay: Damage to every player within Area of itself.</summary>
        Nova = 2,
        /// <summary>After Delay: Damage within Area at the place of Count players (a warning is drawn first).</summary>
        AreaAtPlayers = 3,
        /// <summary>A dash at a player: Speed, Range (how far), Damage on touch.</summary>
        Charge = 4,
        /// <summary>Count shots in a ring (or at the players when Range &gt; 0): Speed, Damage.</summary>
        Barrage = 5,
        // ---- client wave 17 (h17boss), point 23 of the design proposal (design notes section 3): the
        //      bosses' telegraphed attacks, HordeSim.Bosses.cs. Each draws its warning (HordeFxKind 8-15) for Delay seconds, then
        //      strikes; the caster aims at a player by Aim. EnemyAbilityDef says which fields each kind reads.
        /// <summary>Count parallel lanes from the caster toward a player, Width wide, Spacing apart, Range long (0: to the arena's
        /// edge). Shape Plain: Damage on everyone in a lane at the strike. Dash: the caster dashes Range along it at Speed (Damage
        /// once to each player it passes). Runners: one EnemyId per lane runs along it at its own speed (a pack crossing).</summary>
        Line = 6,
        /// <summary>A cone from the caster toward a player: Range long, Arc degrees wide. Damage (and SlowPct for Duration) on
        /// everyone in it; Shape Fan: instead Count shots across the arc at Speed, flying Range.</summary>
        Cone = 7,
        /// <summary>Shape Plain: a band Width wide expands from the caster at Speed to Range, Damage once to each player it passes,
        /// except in its gap (Arc degrees, a random side). Inward: Count orbs on a circle of Range around a player (one place left
        /// empty: the gap) close in at Speed, Damage each.</summary>
        Ring = 8,
        /// <summary>Everyone within Range of the caster is pulled toward it at Speed cells a second for Duration (it stands
        /// meanwhile; walking away still gains ground). No damage of its own.</summary>
        Pull = 9,
        /// <summary>A player is marked: the mark follows it for Delay, stays put for Duration, then bursts: Damage within Area of it
        /// (allies step away; the marked one steps out in the last moment).</summary>
        Mark = 10,
        /// <summary>Squares of Width cells around a player. Shape Checker: Count × Count squares, half strike after Delay, the other
        /// half Duration later. Cross: the squares on the two lines through the player (Count per line). Rain: Count circles of
        /// Area at random places within Spacing of a player, Duration apart.</summary>
        Grid = 11,
        /// <summary>The caster appears at a player after Delay (a shadow / a puff shows where first). Shape Jump: it lands where the
        /// player stood, Damage within Area. Plain (a teleport): it comes Width cells from the player, no damage.</summary>
        Teleport = 12,
        /// <summary>Once, when its HP falls under HpBelow percent: the caster's second phase (a roar; abilities with
        /// PhaseCooldownPct come that much sooner). Nothing else.</summary>
        Phase = 13,
        /// <summary>For Duration the caster leaves a trail of slowing zones (Area wide, each lasting Linger seconds, one every half
        /// second while it walks): SlowPct slower while in one. No damage.</summary>
        Trail = 14,
    }

    /// <summary>Client wave 17 (h17boss): the player a boss's ability aims at.</summary>
    public enum BossAim : byte { Nearest = 0, Random = 1, Farthest = 2 }

    /// <summary>Client wave 17 (h17boss): a variant of an ability kind (see <see cref="AbilityKind"/>; Plain = the kind's first one).</summary>
    public enum AbilityShape : byte { Plain = 0, Dash = 1, Runners = 2, Fan = 3, Inward = 4, Checker = 5, Cross = 6, Rain = 7, Jump = 8 }

    /// <summary>Something an elite or a boss does besides walking and touching (Cooldown seconds apart; the first use after
    /// FirstAfter seconds of its life).</summary>
    public sealed class EnemyAbilityDef
    {
        public AbilityKind Kind;
        public float Cooldown = 10f, FirstAfter = 5f;
        public int Damage;
        public float Area, Delay, Speed, Range;
        public int Count = 1;
        public int EnemyId;
        /// <summary>The Ragnarok skill it is drawn as (rAthena's skill id); 0 = the engine's plain marker.</summary>
        public int SkillId;
        // ---- client wave 17 (h17boss): the numbers of the kinds Line .. Trail (0 = not used; AbilityKind says who reads what)
        /// <summary>Cells: a lane's width (Line), the band's width (Ring), a square's side (Grid), how far from the player a teleport
        /// lands (Teleport).</summary>
        public float Width;
        /// <summary>Cells between two lanes (Line); how far from the player the circles fall (Grid Rain).</summary>
        public float Spacing;
        /// <summary>Degrees: the cone's width (Cone), the band's gap (Ring).</summary>
        public float Arc;
        /// <summary>Seconds: the pull (Pull), the mark staying put (Mark), between the two halves / the circles (Grid), the slow
        /// (Cone), the trail (Trail).</summary>
        public float Duration;
        /// <summary>Seconds a trail's zone lasts (Trail).</summary>
        public float Linger;
        /// <summary>Percent slower (Cone, Trail).</summary>
        public float SlowPct;
        /// <summary>Percent of max HP: used only while the caster's HP is under it (0 = always); Phase: its trigger.</summary>
        public float HpBelow;
        /// <summary>Percent: after the caster's Phase, this ability's cooldown (and a Grid's time between halves) is this share of
        /// its own (50 = twice as often). 0 = unchanged.</summary>
        public float PhaseCooldownPct;
        public BossAim Aim;
        public AbilityShape Shape;
    }

    public sealed class DropDef
    {
        /// <summary>A PickupDef id.</summary>
        public int PickupId;
        /// <summary>Chance in percent per death.</summary>
        public float Chance;
        /// <summary>Client wave 9 (h9content): the drop lands this many cells from the death's place, in a random direction (clamped
        /// to the arena): a chest off the path. 0 = at the body (wave 8).</summary>
        public float ScatterCells;
        public DropDef() { }
        public DropDef(int pickupId, float chance) { PickupId = pickupId; Chance = chance; }
        public DropDef(int pickupId, float chance, float scatterCells) { PickupId = pickupId; Chance = chance; ScatterCells = scatterCells; }
    }

    public sealed class EnemyDef
    {
        /// <summary>The mode's own id (1..65535).</summary>
        public int Id;
        /// <summary>rAthena's mob id (db/re/mob_db.yml): the sprite, by the client's tables.</summary>
        public int MobId;
        /// <summary>The mob's AegisName ("PORING"), for logs and the design document.</summary>
        public string Key = "";
        public EnemyRank Rank;
        public EnemyBehavior Behavior;
        public int Hp = 10;
        /// <summary>Damage per touch (a touched player is hit again after AttackInterval).</summary>
        public int Damage = 1;
        public float AttackInterval = 1f;
        /// <summary>Cells per second.</summary>
        public float Speed = 1f;
        /// <summary>Its body's radius in cells (hits and touching).</summary>
        public float Radius = 0.5f;
        /// <summary>Run EXP it drops as a pickup (0 = none).</summary>
        public int Exp = 1;
        public Element Element;
        public Race Race;
        public UnitSize Size = UnitSize.Medium;
        /// <summary>How much of a knockback it ignores, in percent (bosses: 100).</summary>
        public float KnockbackResist;
        /// <summary>Slow, Freeze and Stun last this many percent shorter (bosses: 100 = immune).</summary>
        public float EffectResist;
        /// <summary>Ranged / Still: shots. ShotDamage 0 = it does not shoot.</summary>
        public int ShotDamage;
        public float ShotRange, ShotSpeed = 6f;
        /// <summary>Client wave 9 (h9content): Ranged behaviour only: it walks AWAY from a player nearer than this many cells (it
        /// keeps its distance; it still shoots within ShotRange). 0 = it stands when within ShotRange (wave 8).</summary>
        public float KeepRange;
        /// <summary>Charge.</summary>
        public float ChargeCooldown = 6f, ChargeWindup = 0.8f, ChargeSpeed = 8f;
        /// <summary>Exploder: the burst's radius.</summary>
        public float BurstArea = 1.5f;
        /// <summary>The size it is drawn at, in percent of the sprite's own (elites drawn larger).</summary>
        public float DrawScale = 100f;
        public EnemyAbilityDef[] Abilities = new EnemyAbilityDef[0];
        public DropDef[] Drops = new DropDef[0];
    }

    public enum SpawnPattern : byte
    {
        /// <summary>Scattered on a ring around a random standing player (just out of sight).</summary>
        Ring = 0,
        /// <summary>All together, on one side of a random standing player.</summary>
        Cluster = 1,
        /// <summary>A closed circle around a random standing player, walking in.</summary>
        Circle = 2,
        /// <summary>At random places of the arena's edge.</summary>
        Edge = 3,
    }

    /// <summary>
    /// One line of the run's schedule. Times are in seconds OF THE REFERENCE RUN (RulesDef.ReferenceRunSeconds, 900): a run
    /// the host made shorter or longer plays the same schedule stretched to its length.
    /// </summary>
    public sealed class WaveDef
    {
        /// <summary>Active from From (included) to To (excluded).</summary>
        public float From, To;
        /// <summary>An EnemyDef id.</summary>
        public int EnemyId;
        /// <summary>Seconds between spawns, and enemies per spawn FOR ONE PLAYER (ScalingDef.SpawnMul scales Count).</summary>
        public float Interval = 1f;
        public int Count = 1;
        /// <summary>No spawn while this many of this enemy are alive (0 = no limit of its own; RulesDef.MaxEnemies always holds).</summary>
        public int MaxAlive;
        public SpawnPattern Pattern;
        /// <summary>One spawn only, at From (an elite or a boss at its mark): Interval and To are ignored, Count enemies are
        /// spawned (exactly Count: not scaled by player count; client wave 9 — wave 8 spawned one), and the spawn is never skipped
        /// for RulesDef.MaxEnemies.</summary>
        public bool Once;
        /// <summary>Client wave 9 (h9content): a Once wave announces itself this many seconds (real seconds, not stretched) before
        /// its spawn with one HordeEventKind.Incoming event (DefId = its enemy, Value = the seconds, Flags = the rank's). 0 = none.</summary>
        public float WarnSeconds;
    }

    public enum PickupKind : byte
    {
        /// <summary>Run EXP. Several Exp pickups = several looks: a drop takes the one with the largest Value not above its EXP.</summary>
        Exp = 1,
        /// <summary>Heals Value percent of the taker's max HP.</summary>
        Heal = 2,
        /// <summary>Pulls every EXP pickup of the arena to the taker.</summary>
        Magnet = 3,
        /// <summary>Gives the taker Value upgrades (a level of a weapon or passive it owns, chosen by the engine's dice).</summary>
        Chest = 4,
        /// <summary>Value damage to every enemy within the pickup's Area.</summary>
        Bomb = 5,
        /// <summary>Client wave 17 (h17pass): a passive's own pickup (an arrow bundle: +Value shots for a while; HordeSim.PassivePickup).</summary>
        Passive = 32,
    }

    public sealed class PickupDef
    {
        /// <summary>The mode's own id (1..255).</summary>
        public int Id;
        public string Key = "";
        public PickupKind Kind;
        /// <summary>rAthena's item id: the sprite it is drawn with.</summary>
        public int ItemId;
        public int Value;
        public float Area;
    }

    /// <summary>How the run grows with the number of players and with time. Each array has 8 entries: [players - 1].</summary>
    public sealed class ScalingDef
    {
        /// <summary>Multiplies WaveDef.Count (fractions carry over from spawn to spawn).</summary>
        public float[] SpawnMul = { 1f, 1.6f, 2.2f, 2.7f, 3.2f, 3.6f, 4f, 4.4f };
        public float[] EnemyHpMul = { 1f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f, 1.7f };
        public float[] EliteHpMul = { 1f, 1.5f, 2f, 2.5f, 3f, 3.5f, 4f, 4.5f };
        public float[] BossHpMul = { 1f, 1.8f, 2.6f, 3.4f, 4.2f, 5f, 5.8f, 6.6f };
        public float[] EnemyDamageMul = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
        /// <summary>Percent added per minute of the reference run (Normal enemies; elites and bosses take their mark's value).</summary>
        public float EnemyHpPctPerMinute = 10f, EnemyDamagePctPerMinute = 5f;
    }

    public sealed class RulesDef
    {
        /// <summary>The length the waves are written for; a run of another length stretches them (and its EXP) to fit.</summary>
        public float ReferenceRunSeconds = 900f;
        /// <summary>The final boss appears when the run's length is over; the run ends at its death or this many seconds later.</summary>
        public int FinalBossEnemyId;
        public float FinalBossSeconds = 120f;
        /// <summary>Client wave 9 (h9content): the final boss is announced this many seconds before it comes (one
        /// HordeEventKind.Incoming event with the Boss | Final flags). 0 = none.</summary>
        public float FinalBossWarnSeconds;
        /// <summary>The engine never holds more live enemies than this (the wire is sized for 300; the hard limit is 400).</summary>
        public int MaxEnemies = 300;

        // ---- the run character
        public int StartHp = 100, HpPerLevel = 5;
        /// <summary>Cells per second.</summary>
        public float MoveSpeed = 4f;
        /// <summary>Client wave 9 (h9move; Loki's design): seconds from standing to full speed, and from full speed to standing
        /// (the walk's velocity ramps toward the stick: no jerk; a change of direction never takes more than ten ticks).</summary>
        public float MoveAccelSeconds = 0.10f, MoveDecelSeconds = 0.06f;
        /// <summary>The arena's bound is a soft wall: over the last this many cells before it the step toward it shrinks.</summary>
        public float WallSoftCells = 0.75f;
        /// <summary>A knockback is an impulse that keeps this many percent of its velocity each tick (75 at 40 ticks a second:
        /// it has travelled its distance in about a quarter of a second).</summary>
        public float KnockbackKeepPct = 75f;
        /// <summary>A pickup within the pickup radius flies to the player: starting at PickupPullSpeed cells per second and
        /// gaining PickupPullAccel cells per second each second (an ease-in; the client draws the same flight).</summary>
        public float PickupPullSpeed = 3f, PickupPullAccel = 48f;
        public float BodyRadius = 0.4f;
        public float PickupRadius = 1.5f;
        /// <summary>Client wave 9 (h9content): cells added to PickupRadius per run level above 1 (before the percent modifiers). 0 = none.</summary>
        public float PickupRadiusPerLevel;
        /// <summary>Seconds a player cannot be hurt again after a hit.</summary>
        public float HurtGraceSeconds = 0.4f;
        public float CritChance = 5f, CritDamagePct = 150f;
        /// <summary>The weapon a Novice starts with (a WeaponDef id).</summary>
        public int StartWeapon;
        public int MaxWeapons = 6, MaxPassives = 6;

        // ---- levels, cards, jobs
        /// <summary>ExpTable[n] = run EXP from level n + 1 to n + 2. Beyond the table each level needs ExpGrowthPct more than the last.</summary>
        public int[] ExpTable = { 5, 10, 15, 20, 25 };
        public float ExpGrowthPct = 10f;
        public int MaxLevel = 99;
        /// <summary>Wave 8: percent of a pickup's EXP that the players who did not take it got. NOT READ since 2026-10-05 (design note): run
        /// EXP is one party pool and the run level one party level; kept so wave-8 tables compile.</summary>
        public float ExpSharePct = 100f;
        /// <summary>Client wave 9 (design note, 2026-10-05, one EXP bar): what the party's next level costs = ExpTable[level] ×
        /// PartyExpScale[players - 1] (the index clamped to the array; empty = 1). The default is h9content's measurement of what
        /// 2 / 4 / 8 bots' pickups add up to against a solo player's intake (kills follow the spawn scaling, not the player count):
        /// 1.51× / 2.53× / 4.28×, smoothed; a solo run paces as the table says.</summary>
        public float[] PartyExpScale = { 1f, 1.55f, 2.1f, 2.65f, 3.2f, 3.75f, 4.3f, 4.8f };
        /// <summary>Client wave 18 (h18rules), the design's point 31: a group of normal enemies spawned around a player is that much larger for
        /// every other standing player within this many cells of it: x (1 + the sum of max(0, 1 - distance / ClusterSpawnCells)) (an ally
        /// on top: one more whole share; half way: half). 0 = off (HordeSim.Rules.cs Closeness).</summary>
        public float ClusterSpawnCells;
        /// <summary>Client wave 18 (h18rules), point 31 (design note: "Yes, cap the closeness bit"): the closeness adds at most this many solo shares to
        /// a group (1: n stacked players give at most 2 n shares). 0 = no cap.</summary>
        public float ClusterSpawnMaxShares;
        /// <summary>Client wave 18 (h18rules), the design's elites ("make some Elite Enemies, that sit somewhere between a boss and a regular enemy"):
        /// the first of HordeContent.Elites comes EliteFirstSeconds into the run (seconds of the reference run, stretched as the waves), then
        /// one every EliteEverySeconds .. EliteEveryMaxSeconds (real seconds, the run's dice), 1 + (players - 1) / ElitePlayersPerExtra of
        /// them (fewer when some are still alive), never from EliteQuietSeconds before a boss's or a marked elite's mark (a Once wave) to as
        /// long after it, nor in the final phase. EliteEverySeconds 0 = none (HordeSim.Rules.cs StepElites).</summary>
        public float EliteFirstSeconds, EliteEverySeconds, EliteEveryMaxSeconds, EliteQuietSeconds;
        public int ElitePlayersPerExtra = 3;
        public int CardsPerOffer = 3;
        /// <summary>design note, 2026-10-05 01:25: the card / job choice (the world pauses meanwhile) lasts at most this long at a run's start,
        /// solo included; a choice not answered in time picks its first card (jobs: the first job). Every time the limit is MET (the
        /// host auto-picked, or the answer that ended the pause came within its last second) the run's limit drops by
        /// ChoiceSecondsStep, down to ChoiceSecondsMin (30 → 20 → 10 → 5 → 5 …), for the whole party, never back up within the
        /// run; a new run starts at ChoiceSeconds again. HordeOffer.Seconds carries the run's current limit.</summary>
        public float ChoiceSeconds = 30f;
        public float ChoiceSecondsStep = 10f, ChoiceSecondsMin = 5f;
        /// <summary>JobMarks[tier] = the run level at which a job of that tier offers its Next (index 0: Novice → first job).</summary>
        public int[] JobMarks = { 5, 15, 30, 45, 60 };
        /// <summary>The dice of a card offer: relative weights of the five kinds of card (each def's own Weight then picks within a kind).</summary>
        public int WeightNewWeapon = 100, WeightWeaponLevel = 120, WeightNewPassive = 80, WeightPassiveLevel = 80, WeightStat = 40;
        /// <summary>Stat points a stat card gives.</summary>
        public int StatCardPoints = 2;

        // ---- falling and rising
        public float ReviveSeconds = 12f;
        /// <summary>An ally standing within ReviveRadius makes the fallen rise ReviveNearMul times as fast.</summary>
        public float ReviveRadius = 3f, ReviveNearMul = 3f;
        public float ReviveHpPct = 50f, ReviveSafeSeconds = 2f;
    }

    /// <summary>What a result is worth in points (Loki's own). A player's score = its own part + the party's part.</summary>
    public sealed class ScoreDef
    {
        // own
        public int PerKill = 1, PerEliteKill = 20, PerBossKill = 100;
        public int PerRunLevel = 10, PerJobTier = 50;
        /// <summary>Per 1000 damage dealt / per 100 HP healed on allies / per ally raised by standing close.</summary>
        public int PerDamage1000 = 1, PerHeal100 = 2, PerRevive = 30;
        // party (the same for everyone who was in the run, in proportion to the time they were in it)
        public int PerSecond = 1;
        public int Victory = 500;
    }

    /// <summary>The host's reward size (a Loki Host setting).</summary>
    public enum HordeRewardSize : byte { Off = 0, Low = 1, Normal = 2, High = 3 }

    /// <summary>
    /// From a run's kills and survival time to the real character's EXP, as a share of what it needs for its NEXT base / job level
    /// (so it means the same at any level). 0 when the player was in the run for less than MinSeconds. The host computes it; the
    /// client never names an amount.
    /// </summary>
    public sealed class RewardDef
    {
        /// <summary>Reference run seconds (e.g. 900 s = 15 min) for the base cap; endless survival and higher kill counts scale above it up to MaxShare.</summary>
        public float SecondsForFull;
        public int ScoreForFull = 3000;
        public float MinSeconds = 60f;
        /// <summary>The reference reward in percent of the next level for a standard 15-minute run, by HordeRewardSize (Off, Low, Normal, High).</summary>
        public float[] BaseCapPct = { 0f, 10f, 20f, 35f };
        public float[] JobCapPct = { 0f, 10f, 20f, 35f };
    }

    /// <summary>A bounded field on a Ragnarok map: the rectangle of cells X0..X1 × Y0..Y1 (both included), every cell of it
    /// walkable (Loki Host checks it against its server's map cache before it offers the arena).</summary>
    public sealed class ArenaDef
    {
        public int Id;
        /// <summary>rAthena's map name ("prt_fild08").</summary>
        public string Map = "";
        public int X0, Y0, X1, Y1;

        /// <summary>Scales the arena by +20% of its base size for each additional party member beyond the first, clamped to MaxArenaCells.</summary>
        public ArenaDef ScaledForParty(int partyCount)
        {
            if (partyCount <= 1) return this;
            double mul = 1.0 + 0.20 * (partyCount - 1);
            int w = Math.Min(HordeLimits.MaxArenaCells, (int)Math.Round((X1 - X0) * mul));
            int h = Math.Min(HordeLimits.MaxArenaCells, (int)Math.Round((Y1 - Y0) * mul));
            int cx = (X0 + X1) / 2, cy = (Y0 + Y1) / 2;
            int x0 = Math.Max(16, cx - w / 2), y0 = Math.Max(16, cy - h / 2);
            return new ArenaDef { Id = Id, Map = Map, X0 = x0, Y0 = y0, X1 = x0 + w, Y1 = y0 + h };
        }
    }

    /// <summary>Client wave 18 (h18rules): an elite of the periodic deal (HordeContent.Elites): its EnemyDef and its window in the run.</summary>
    public sealed class EliteDeal { public int EnemyId; public float From, To; }

    /// <summary>Everything a run is made of. Built once (HordeTables.Build()); the engine never changes it.</summary>
    public sealed class HordeContent
    {
        public RulesDef Rules = new RulesDef();
        public ScalingDef Scaling = new ScalingDef();
        public ScoreDef Score = new ScoreDef();
        public RewardDef Reward = new RewardDef();
        public List<WeaponDef> Weapons = new List<WeaponDef>();
        public List<PassiveDef> Passives = new List<PassiveDef>();
        public List<JobDef> Jobs = new List<JobDef>();
        public List<EnemyDef> Enemies = new List<EnemyDef>();
        public List<WaveDef> Waves = new List<WaveDef>();
        public List<PickupDef> Pickups = new List<PickupDef>();
        public List<ArenaDef> Arenas = new List<ArenaDef>();
        /// <summary>One entry per stat (missing = that stat gives nothing).</summary>
        public List<StatDef> Stats = new List<StatDef>();
        /// <summary>ElementPct[weapon's element, enemy's element] = percent of the damage dealt (100 = unchanged). Null = all 100.</summary>
        public int[,] ElementPct;
        /// <summary>Client wave 17 (h17obj): the map objectives' numbers (HordeSim.Objectives.cs; the defaults are the game's).</summary>
        public ObjectiveRulesDef Objectives = new ObjectiveRulesDef();
        /// <summary>Client wave 18 (h18feel), point 35: soul retrieval's numbers (HordeSim.Souls.cs; the defaults are the game's).</summary>
        public SoulRulesDef Souls = new SoulRulesDef();
        /// <summary>Client wave 18 (h18rules): the elites that come now and then (RulesDef.Elite*), each from From to To seconds of the
        /// reference run (stretched as the waves).</summary>
        public List<EliteDeal> Elites = new List<EliteDeal>();

        public WeaponDef Weapon(int id) { foreach (var w in Weapons) if (w.Id == id) return w; return Objectives?.Weapon(id); } // client wave 17 (h17obj): + the objectives' effect weapons
        public PassiveDef Passive(int id) { foreach (var p in Passives) if (p.Id == id) return p; return null; }
        public JobDef Job(int jobId) { foreach (var j in Jobs) if (j.JobId == jobId) return j; return null; }
        public EnemyDef Enemy(int id) { foreach (var e in Enemies) if (e.Id == id) return e; return null; }
        public PickupDef Pickup(int id) { foreach (var p in Pickups) if (p.Id == id) return p; return null; }
        public ArenaDef Arena(int id) { foreach (var a in Arenas) if (a.Id == id) return a; return null; }
        /// <summary>The Novice: the one job of tier 0.</summary>
        public JobDef StartJob() { foreach (var j in Jobs) if (j.Tier == 0) return j; return null; }
    }
}
