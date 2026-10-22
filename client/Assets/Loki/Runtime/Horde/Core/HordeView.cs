#nullable disable
using System;
using System.Collections.Generic;

namespace Loki.Horde
{
    // ====================================================================================================================
    // Horde (client wave 8): what a client knows of a run, as the channel (Net/IHordeChannel.cs) hands it over. Pure C#,
    // no UnityEngine: the Unity client draws from these, Loki Host's headless bots read the same.
    //
    // Places are in the MAP's own cells (floats: 1 = one GAT cell, x east, y north, as RO's cell coordinates; a unit at
    // cell (150, 120)'s centre is at 150.5, 120.5). Times are in server TICKS (HordeRunInfo.TickRate a second: 40 since
    // client wave 9, 20 in wave 8).
    // Ids: a player is its SLOT (0..7) in the run; an enemy is its slot in World.Enemies (reused after a death: Serial
    // tells two tenants apart); pickups and effects have ids of their own. Def ids are HordeContent's (WeaponDef.Id …).
    // ====================================================================================================================

    public enum HordePhase : byte
    {
        /// <summary>No run: one can be started.</summary>
        Idle = 0,
        /// <summary>A run was started: the invitation's countdown is running, players answer.</summary>
        Gathering = 1,
        /// <summary>A run is being played (it can be joined late).</summary>
        Running = 2,
    }

    public enum HordeMemberState : byte { Asked = 0, Joined = 1, Declined = 2, Left = 3 }

    public sealed class HordeMember
    {
        public uint Aid;
        public string Name = "";
        public HordeMemberState State;
        /// <summary>While the run is played: its slot (-1 = not in it), run level, job (rAthena job id), fallen.</summary>
        public int Slot = -1, Level, JobId;
        public bool Fallen;
    }

    /// <summary>The mode's state on this server: what the wrist menu's panel shows. Kept current by the channel.</summary>
    public sealed class HordeStatus
    {
        /// <summary>The host has the mode switched on.</summary>
        public bool Enabled;
        public HordePhase Phase;
        public uint RunId;
        /// <summary>Who started the run (Gathering / Running).</summary>
        public uint StarterAid;
        public string StarterName = "";
        /// <summary>Gathering: seconds until the run begins (counted down locally between the host's updates).</summary>
        public float SecondsLeft;
        /// <summary>Running: seconds played, and the run's length (the final boss comes at RunLength).</summary>
        public float RunSeconds, RunLength;
        /// <summary>The arena's map (rAthena's map name).</summary>
        public string ArenaMap = "";
        public HordeRewardSize Reward;
        /// <summary>Everyone the host asked, with their answer (the starter is Joined).</summary>
        public List<HordeMember> Members = new List<HordeMember>();
        /// <summary>The lowest run level among the players in the run (a late joiner starts there).</summary>
        public int LowestLevel = 1;
        /// <summary>The server's town maps (its "town" map flags): the prompt warns a player who is not on one of them.
        /// Empty = the host does not know (a standalone relay): no warning can be given.</summary>
        public List<string> Towns = new List<string>();
        /// <summary>The relay's Horde protocol version (HordeWire.Version; 1 = a wave-8 Loki Host, which says none).</summary>
        public byte Protocol = 1;
        /// <summary>Client wave 18 (h18rules), point 38: the players a run's starter may invite now (every player connected to this host
        /// with Horde and the same tables, the host's rule since wave 8): Aid and Name. Empty from a host from before (H_STATUS's tail).</summary>
        public List<HordeMember> Online = new List<HordeMember>();

        public bool IsTown(string map)
        {
            if (string.IsNullOrEmpty(map)) return false;
            foreach (var t in Towns) if (string.Equals(t, map, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        public HordeMember Member(uint aid) { foreach (var m in Members) if (m.Aid == aid) return m; return null; }
    }

    /// <summary>The prompt: "&lt;StarterName&gt; is starting a Horde run. Join?"</summary>
    public sealed class HordeInvite
    {
        public uint RunId;
        public uint StarterAid;
        public string StarterName = "";
        /// <summary>The countdown, in seconds from now.</summary>
        public float Seconds;
        public string ArenaMap = "";
        public float RunLength;
        public HordeRewardSize Reward;
    }

    /// <summary>I am in a run: where and how it is played.</summary>
    public sealed class HordeRunInfo
    {
        public uint RunId;
        /// <summary>My slot (0..7).</summary>
        public int Slot;
        public int ArenaId;
        public string Map = "";
        /// <summary>The field: cells X0..X1 × Y0..Y1, both included (players stay inside).</summary>
        public int X0, Y0, X1, Y1;
        /// <summary>Server ticks per second (40).</summary>
        public int TickRate = HordeLimits.TickRate;
        /// <summary>Ticks between two snapshots (2: 20 snapshots a second).</summary>
        public int SnapshotEvery = 2;
        /// <summary>The run's length in seconds (the final boss comes then) and the final boss's own timer.</summary>
        public float RunLength, FinalBossSeconds;
        /// <summary>I joined a run already in progress.</summary>
        public bool Late;
    }

    public enum HordeCardKind : byte
    {
        /// <summary>A weapon I do not own: DefId = WeaponDef id, Level = 1.</summary>
        NewWeapon = 1,
        /// <summary>One more level of a weapon I own: DefId, Level = the level it becomes.</summary>
        WeaponLevel = 2,
        NewPassive = 3,
        PassiveLevel = 4,
        /// <summary>Stat points: DefId = the Stat (0 Str … 5 Luk), Level = the points given.</summary>
        Stat = 5,
        /// <summary>A job change: DefId = rAthena job id.</summary>
        Job = 6,
        /// <summary>Nothing left to offer: heals Level percent of max HP.</summary>
        Heal = 7,
        /// <summary>Client wave 17 (h17cards), point 15 B: a weapon I own at level 2 takes this branch: DefId = the branch's WeaponDef id
        /// (WeaponDef.BranchOf names the weapon), Level = 3. An offer shows a weapon's two branches side by side.</summary>
        WeaponBranch = 8,
        /// <summary>Client wave 18 (h18trans), point 30: a weapon at its top level transcends into one of two reworked branches:
        /// DefId = the transcended WeaponDef id (5000 + root or 6000 + root), Level = top + 1. Both branches are offered side by side.</summary>
        WeaponTranscend = 9,
        /// <summary>Client wave 18 (h18trans), point 30: a passive at its top level transcends into a reworked form:
        /// DefId = the transcended PassiveDef id (1000 + id), Level = top + 1.</summary>
        PassiveTranscend = 10,
    }

    public struct HordeCard
    {
        public HordeCardKind Kind;
        public int DefId;
        public int Level;
        /// <summary>Wave 28 (item 52): stat points attached as a bonus on another card (0 = none, 1..6 = Stat.Str..Stat.Luk + 1).</summary>
        public byte BonusStat;
        public byte BonusStatPoints;
        public bool HasBonusStat => BonusStat >= 1 && BonusStat <= 6 && BonusStatPoints > 0;
        public Stat BonusStatKind => (Stat)(BonusStat - 1);
    }

    /// <summary>A choice for me: cards after a level-up, or the next job. Answer with IHordeChannel.Choose(Id, index); not
    /// answered within Seconds, the host takes the first.</summary>
    public sealed class HordeOffer
    {
        public int Id;
        /// <summary>True: a job choice (every card is a Job).</summary>
        public bool IsJob;
        public float Seconds;
        public HordeCard[] Cards = new HordeCard[0];
    }

    public enum HordeOutcome : byte
    {
        /// <summary>The final boss died.</summary>
        Victory = 1,
        /// <summary>The final boss's timer ran out with someone standing.</summary>
        TimeUp = 2,
        /// <summary>Everyone had fallen at once.</summary>
        Defeat = 3,
        /// <summary>The host stopped the run (Loki Host stopped, or the mode was switched off).</summary>
        Stopped = 4,
    }

    public enum HordeRewardState : byte
    {
        /// <summary>No reward (the host's reward is off, the player was in the run too short, or the host cannot grant: a relay without a server).</summary>
        None = 0,
        /// <summary>Decided and written down; the character gets it when it is next online and able to gain EXP.</summary>
        Pending = 1,
        Granted = 2,
    }

    public sealed class HordePlayerResult
    {
        public int Slot;
        public uint Aid;
        public string Name = "";
        public int Level, JobId;
        public int Kills, EliteKills, BossKills, Falls, Revives;
        public long Damage, Healing;
        /// <summary>Seconds this player was in the run.</summary>
        public float Seconds;
        public int Score;
        /// <summary>The real character's reward as decided by the host: shares of what it needs for its next base / job
        /// level, in 1/100 of a percent (1500 = 15 %).</summary>
        public int BaseShare, JobShare;
        public HordeRewardState Reward;
        /// <summary>The player left before the end (its result stood from then on).</summary>
        public bool Left;
    }

    public sealed class HordeResults
    {
        public uint RunId;
        public HordeOutcome Outcome;
        /// <summary>Seconds the run lasted.</summary>
        public float Seconds;
        public int MySlot = -1;
        public List<HordePlayerResult> Players = new List<HordePlayerResult>();
        public HordePlayerResult Mine() { foreach (var p in Players) if (p.Slot == MySlot) return p; return null; }
    }

    /// <summary>The real EXP my character was given for a run (it may come seconds after the results: rAthena's side grants
    /// it; when my character was not online or could not gain EXP then, it comes when it next can).</summary>
    public sealed class HordeRewardNote
    {
        public uint RunId;
        public long BaseExp, JobExp;
        public int BaseShare, JobShare;
    }

    // ---------------------------------------------------------------------------------------------------- the world

    [Flags]
    public enum HordePlayerFlags : byte
    {
        None = 0,
        Fallen = 1,
        /// <summary>Cannot be hurt now (choosing a card, just risen).</summary>
        Safe = 2,
        /// <summary>A choice is open for this player.</summary>
        Choosing = 4,
        /// <summary>Its link is gone: it stands still (it leaves the run after a while).</summary>
        Away = 8,
        /// <summary>An ally stands close to this fallen player (it rises faster).</summary>
        Helped = 16,
        /// <summary>Client wave 15 (w15hfx), point 19: this player paused the run from its Horde menu (HordeSim.HeldBy).</summary>
        Held = 32,
        /// <summary>Client wave 18 (h18rules), point 27: just after a Back Slide: unseen by enemies, unhurt, faster (HordeSim.SlideHidden;
        /// old clients ignore the bit).</summary>
        Vanished = 64,
    }

    public struct HordeOwned
    {
        public int DefId, Level;
    }

    public sealed class HordePlayerView
    {
        public bool Present;
        public int Slot;
        public uint Aid;
        public string Name = "";
        /// <summary>The place to draw between (PrevX, PrevY at Alpha 0 and X, Y at Alpha 1; World.Alpha). Since client wave 9 the
        /// channel sets them each frame (World.Prepare) to the two snapshots around the render time, one snapshot behind the
        /// newest, so nothing steps when a snapshot is late; the own slot's are the prediction's.</summary>
        public float X, Y, PrevX, PrevY;
        /// <summary>The walking direction in degrees (0 = east, 90 = north), kept while standing.</summary>
        public float Facing;
        /// <summary>The sprite direction (Sprites/Unit.cs's eight: 0 south … 6 east) with hysteresis: it never flickers at a
        /// boundary between two directions (client wave 9; draw with it instead of DirFromDelta(Facing)).</summary>
        public int Dir8;
        public bool Moving;
        /// <summary>The walking velocity in cells per second (the host's ramp; the own slot's is the prediction's).</summary>
        public float Vx, Vy;
        public int Hp, MaxHp;
        public int Level, JobId;
        public int Exp, ExpToNext;
        public HordePlayerFlags Flags;
        /// <summary>Fallen: 0..1 of the way to rising.</summary>
        public float Revive;
        public int Kills;
        public List<HordeOwned> Weapons = new List<HordeOwned>();
        public List<HordeOwned> Passives = new List<HordeOwned>();
        public int[] Stats = new int[6];
        /// <summary>Client wave 17 (h17pass): the passives' families and their states (H_SNAP's passives block; HordeSim.Passives.cs).</summary>
        public readonly List<HordeProcView> Procs = new List<HordeProcView>();
        public bool Fallen => (Flags & HordePlayerFlags.Fallen) != 0;
    }

    [Flags]
    public enum HordeEnemyFlags : byte
    {
        None = 0,
        Slowed = 1,
        Frozen = 2,
        Stunned = 4,
        /// <summary>A damage-over-time effect is on it.</summary>
        Hurting = 8,
        /// <summary>Winding up a charge or an ability (it stands still).</summary>
        Winding = 16,
        /// <summary>Dashing.</summary>
        Charging = 32,
        /// <summary>Being knocked back (an impulse is still moving it; client wave 9).</summary>
        Knocked = 64,
        /// <summary>Client wave 17 (h17cards): a player's Mark is on it (it takes more damage, deals less, or rushes its marker).</summary>
        Marked = 128,
    }

    public sealed class HordeEnemyView
    {
        public bool Alive;
        /// <summary>Counts up each time this slot gets a new enemy.</summary>
        public int Serial;
        /// <summary>EnemyDef id (its MobId gives the sprite).</summary>
        public int DefId;
        /// <summary>Draw between them (World.Alpha); set each frame by World.Prepare since client wave 9 (see HordePlayerView).</summary>
        public float X, Y, PrevX, PrevY;
        /// <summary>HP as 0..255 of its max.</summary>
        public byte Hp;
        public HordeEnemyFlags Flags;
        /// <summary>The tick it appeared (for a spawn effect), as far as this client saw it.</summary>
        public int SinceTick;
        /// <summary>Its sprite direction with hysteresis, from its drawn motion (client wave 9; 0 south … 6 east).</summary>
        public int Dir8;
        /// <summary>Its velocity as drawn, cells per second (client wave 9).</summary>
        public float Vx, Vy;
    }

    public sealed class HordePickupView
    {
        public int Id;
        /// <summary>PickupDef id (its ItemId gives the sprite).</summary>
        public int DefId;
        /// <summary>Where it lies (where it was dropped; it does not move until it is pulled).</summary>
        public float X, Y;
        public int Value;
        /// <summary>Client wave 9: the player it is flying to (-1: lying still) since PullTick. The engine moves it from (X, Y) at
        /// PullTick toward that player at RulesDef.PickupPullSpeed + PickupPullAccel × seconds cells per second (an ease-in) until
        /// it is taken (PickupTaken); draw the same flight.</summary>
        public int PulledBy = -1;
        public int PullTick;
    }

    public sealed class HordeSummonView
    {
        public int Id;
        public int OwnerSlot;
        /// <summary>The WeaponDef that called it (its SummonMobId gives the sprite).</summary>
        public int DefId;
        public float X, Y, PrevX, PrevY;
        public byte Hp;
        internal readonly float[] HX = new float[3], HY = new float[3];
        internal int HN;
    }

    public enum HordeFxKind : byte
    {
        /// <summary>A player's projectile: from (X, Y) at StartTick, VX / VY cells per second, gone at EndTick or when the
        /// host says so (it hit something). Target ≥ 0: it homes on that enemy slot.</summary>
        Shot = 1,
        /// <summary>An enemy's projectile (OwnerEnemy = its slot at the time, DefId = EnemyDef id).</summary>
        EnemyShot = 2,
        /// <summary>A ground zone of radius Radius until EndTick. Follows = it stays centred on its owner.</summary>
        Zone = 3,
        /// <summary>A trap: armed from ArmTick, gone at EndTick or when it goes off.</summary>
        Trap = 4,
        /// <summary>A player's area that will strike at EndTick (the warning circle of a Meteor).</summary>
        Warning = 5,
        /// <summary>An enemy's area that will strike at EndTick (step out of it). DefId = the ability's skill id or 0.</summary>
        EnemyWarning = 6,
        /// <summary>Count orbs circling the owner at Range, each Radius wide: orb i is at Angle + 360 × Turns × seconds
        /// since StartTick + i × 360 / Count degrees.</summary>
        Orbit = 7,
        // ---- client wave 17 (h17boss): a boss's telegraphed attack (HordeSim.Bosses.cs; drawn by Horde/Client/HordeTelegraphs.cs).
        //      OwnerEnemy = the caster's slot, DefId = the ability's skill id (0 none), Level = its AbilityShape, StartTick = the
        //      warning's start, ArmTick = the strike. The fields each kind uses (places in cells; "a vector" travels in VX / VY
        //      as units of 1/256 cell: the view's VX / TickRate is its x in cells):
        /// <summary>Count lanes from (X, Y) along the vector (VX, VY) / TickRate (its length = the lane's), Radius = half a lane's
        /// width, Range = between two lanes' centres; EndTick = ArmTick (a strike), or the end of the caster's dash / the runners' start.</summary>
        EnemyLine = 8,
        /// <summary>A cone from (X, Y) along the vector (its length = the reach), Angle = its width in degrees; Count &gt; 0: that many
        /// shots fly at ArmTick instead of a strike.</summary>
        EnemyCone = 9,
        /// <summary>Level Plain: a band Radius wide grows from (X, Y) at ArmTick to Range at EndTick, its gap Angle (its centre, degrees)
        /// ± Count / 2 degrees. Level Inward: Count places on a circle of Range around (X, Y), the one at Angle left empty: orbs
        /// fly in from the others at ArmTick (the warning ends then).</summary>
        EnemyRing = 10,
        /// <summary>From ArmTick to EndTick everyone within Radius of (X, Y) is pulled toward it by Range units each tick, never
        /// nearer than Angle units (HordeSim.PullStep: the client predicts my own walk with it).</summary>
        EnemyPull = 11,
        /// <summary>Target ≥ 0: the mark follows that player until ArmTick (= EndTick; a locked one follows). Target -1: locked at
        /// (X, Y), it bursts at ArmTick within Radius.</summary>
        EnemyMark = 12,
        /// <summary>Squares of Range cells' side: Level Checker: Count × Count centred on (X, Y), the even ones strike at ArmTick, the
        /// odd ones at EndTick; Level Cross: the squares of the row and the column through (X, Y), Count each, at ArmTick.</summary>
        EnemyGrid = 13,
        /// <summary>The caster comes to (X, Y) at ArmTick from (X, Y) + the vector; Level Jump: it lands, hitting within Radius; Plain:
        /// a teleport (a puff at both places).</summary>
        EnemyTeleport = 14,
        /// <summary>A slowing zone of Radius at (X, Y) until EndTick: Count percent slower inside.</summary>
        EnemyTrail = 15,
        // client wave 17 (h17cards): kinds 24-31 are h17cards' (h17read's split of the wave's ids)
        /// <summary>A Parry weapon's guard on its owner (it follows it): Count charges left, Radius its drawn size, Angle the arc it
        /// guards in degrees around where the owner walks (0 = all around). A charge used or back ends it and begins a new one.</summary>
        Guard = 25,
    }

    /// <summary>Something that lasts and can be drawn from its start values alone (the host does not send it again).</summary>
    public sealed class HordeFxView
    {
        public int Id;
        public HordeFxKind Kind;
        /// <summary>The player it belongs to, or -1 when it is an enemy's.</summary>
        public int OwnerSlot = -1;
        public int OwnerEnemy = -1;
        /// <summary>WeaponDef id (a player's), EnemyDef id (EnemyShot), a skill id or 0 (EnemyWarning).</summary>
        public int DefId;
        public int Level;
        public float X, Y;
        public float VX, VY;
        public float Radius, Range;
        public float Angle, Turns;
        public int Count;
        public int Target = -1;
        public bool Follows;
        public int StartTick, ArmTick, EndTick;
    }

    public enum HordeEventKind : byte
    {
        /// <summary>An enemy was hit. Target = enemy slot, Value = damage, Slot = the player whose weapon it was (-1 none),
        /// DefId = WeaponDef id, X / Y = the enemy's place. Flags: Crit, Kill, Dot. Only hits worth drawing are sent
        /// (the host keeps the largest of a busy tick).</summary>
        Hit = 1,
        /// <summary>A player was hurt. Slot, Value, DefId = EnemyDef id (0 = unknown), X / Y. Flags: Dodged (Value 0).
        /// Client wave 17 (h17read), the readability proposal's D1: Target = the attacking enemy's slot (-1 unknown), X2 / Y2 = the
        /// attacker's place (the enemy for a touch or a burst, where a shot was fired from, the caster of an area: its centre when the
        /// caster is gone), A = how it came (<see cref="HordeHitVia"/>). A host from before sends none of it (X2 = Y2 = 0, A = 0).</summary>
        PlayerHit = 2,
        /// <summary>A player was healed. Slot = healed, Target = the healer's slot, Value, DefId = WeaponDef id (0 = a pickup).</summary>
        Heal = 3,
        /// <summary>A melee swing. Slot, DefId, X / Y = the player, A = aim in degrees, B = arc in degrees, C = reach.</summary>
        Swing = 4,
        /// <summary>A nova / an aura's pulse. Slot, DefId, X / Y, A = radius.</summary>
        Nova = 5,
        /// <summary>An area struck (after its Warning), a bolt burst, a trap went off. Slot, DefId, X / Y, A = radius.</summary>
        Strike = 6,
        /// <summary>One link of a chain. Slot, DefId, from X / Y to X2 / Y2.</summary>
        Chain = 7,
        /// <summary>Target = enemy slot, DefId = EnemyDef id, X / Y, Slot = the killer (-1 none). Flags: Elite, Boss.</summary>
        EnemyDied = 8,
        /// <summary>An elite or boss appeared. Target = its slot, DefId = EnemyDef id, X / Y. Flags: Elite, Boss, Final.</summary>
        BossAppeared = 9,
        /// <summary>A pickup was taken. Target = pickup id, Slot = taker, DefId = PickupDef id, Value, X / Y.</summary>
        PickupTaken = 10,
        /// <summary>Slot reached run level Value.</summary>
        LevelUp = 11,
        /// <summary>Slot took a card. A = the HordeCardKind, DefId, Value = the level it has now.</summary>
        CardTaken = 12,
        /// <summary>Slot changed job. Value = the new rAthena job id, DefId = the signature weapon given (0 none).</summary>
        JobChanged = 13,
        Fell = 14,
        /// <summary>Slot rose. Target = the ally who stood close (-1 none). Client wave 18 (h18feel): Value 1 = its soul was gathered
        /// (Target = who claimed the last shard; it stands beside it, invulnerable), 2 = the party reached a class-change level.</summary>
        Rose = 15,
        /// <summary>An enemy's ability went off. Target = enemy slot, DefId = its skill id (0 none), X / Y, A = radius,
        /// Value = the AbilityKind.</summary>
        EnemyAbility = 16,
        /// <summary>A summon appeared / was lost. Target = summon id, Slot = owner, DefId = WeaponDef id, X / Y. Flags: Kill = lost.</summary>
        Summon = 17,
        /// <summary>The run's length is over: the final boss comes. Value = seconds of its timer.</summary>
        FinalPhase = 18,
        /// <summary>Slot joined the run (Flags Late when it was in progress) / left it.</summary>
        PlayerJoined = 19,
        PlayerLeft = 20,
        /// <summary>An effect took hold of an enemy. Target = enemy slot, Value = the EffectKind, Slot, DefId = WeaponDef id.</summary>
        Effect = 21,
        /// <summary>An enemy's body touched nobody but burst (an Exploder). Target, DefId = EnemyDef id, X / Y, A = radius.</summary>
        EnemyBurst = 22,
        /// <summary>Client wave 9 (h9content): an elite / boss / the final boss comes in Value seconds (WaveDef.WarnSeconds,
        /// RulesDef.FinalBossWarnSeconds). DefId = EnemyDef id, Flags = the rank's (Elite, Boss, Boss | Final), X / Y = the arena's
        /// centre (where it comes is not known yet). Once per wave line.</summary>
        Incoming = 23,
        // ---- client wave 17 (h17boss): the bosses' attacks (HordeSim.Bosses.cs)
        /// <summary>A boss's second phase began (AbilityKind.Phase). Target = its slot, DefId = EnemyDef id, Value = its HP percent, X / Y.</summary>
        BossPhase = 24,
        /// <summary>A player was slowed by a boss (a petrifying cone, a trail). Slot, Value = the slow in percent, A = seconds, DefId = the skill id.</summary>
        PlayerSlowed = 25,
        /// <summary>A telegraphed attack struck. Target = the caster's slot, DefId = the skill id, Value = the AbilityKind, X / Y = where,
        /// X2 / Y2 = the far end (a lane, a cone's axis, a jump's start), A = the radius / half width, B = the cone's width in degrees.</summary>
        BossStrike = 26,
        // ---- client wave 18 (h18feel), point 35: soul retrieval (HordeSim.Souls.cs; kinds 27-29 are h18feel's)
        /// <summary>Slot fell and its soul split: Value = how many shards (its count N), X / Y = where it fell. The shards are in the
        /// snapshot's souls block (HordeWorldView.Souls).</summary>
        SoulSplit = 27,
        /// <summary>Slot claimed a soul shard: Target = its id, DefId = the players it counted for (bit = slot), Value = whose fall made
        /// it, X / Y = its place. A player with all it needs rises (Rose, Value 1).</summary>
        SoulTaken = 28,
        /// <summary>Client wave 18 (h18feel), point 37: Slot's Yggdrasil Berry saved its life (full HP, invulnerable; enemies round it
        /// cleared). DefId = the berry's WeaponDef id, Value = the seconds it rests (0: alone, it is spent and its slot burnt), X / Y.</summary>
        LifeSaved = 29,
        // client wave 17 (h17obj): the map objectives (HordeSim.Objectives.cs; kinds 32-39 are h17obj's: COORD 23:15 / 23:21)
        /// <summary>An objective appeared. Target = its id, DefId = its ObjectiveKind, Value = seconds it waits, X / Y = its place.</summary>
        ObjectiveUp = 32,
        /// <summary>Slot claimed objective Target (DefId = kind); Value = how many players got its gift; X / Y.</summary>
        ObjectiveClaimed = 33,
        /// <summary>Objective Target (DefId = kind) faded unclaimed (Value 0) or was lost (1: the Emperium left alone); X / Y.</summary>
        ObjectiveFaded = 34,
        /// <summary>Slot got objective Target's gift (DefId = kind); Value = its seconds (0: at once: meteors, upgrades).</summary>
        ObjectiveGot = 35,
        /// <summary>A wave came at the Emperium (objective Target): DefId = EnemyDef id, Value = how many; X / Y.</summary>
        EmperiumWave = 36,
        // client wave 17 (h17cards): kinds 40-47 are h17cards' (h17read's split of the wave's ids)
        /// <summary>Slot dashed (a Dash weapon) from X / Y to X2 / Y2. DefId = WeaponDef id.</summary>
        Dash = 40,
        /// <summary>Slot's weapon evolved (point 15 C). DefId = the evolved WeaponDef id, Value = the weapon it was.</summary>
        Evolved = 41,
        /// <summary>Slot's guard blocked a hit (a Parry weapon, or Land Protector). DefId = WeaponDef id (0: a passive), Value = what it
        /// blocked (1 touch, 2 shot, 4 area strike), X / Y = where.</summary>
        Parried = 42,
        /// <summary>Client wave 17 (h17pass), point 22: a passive's trigger went off. Slot = the runner, DefId = the family's first
        /// passive (its PassiveDef.Proc says the kind and the body), Value = what it did (damage, HP, ticks, gems pulled ...), Target =
        /// an enemy slot or -1, X / Y = where. Flags: Dot = a coat's soak (its refill has none). Kinds 48-55 are h17pass's.</summary>
        Proc = 48,
        // ---- client wave 17 (h17read): event kinds 56-63 (the kind byte keeps 6 bits; COORD: h17boss 24-31, h17obj 32-39, h17cards
        //      40-47, h17pass 48-55, h17read 56-63)
        /// <summary>The readability proposal's C3 / C4: an enemy begins a wind-up. Target = its slot, Slot = the player it aims at (-1
        /// none), DefId = EnemyDef id, Value = the wind-up's length in ticks, X / Y = its place, X2 / Y2 = where it goes (a charge: the
        /// dash's end) or aims (a shot: the player's place), A = the direction in degrees (0 east, 90 north), B = the
        /// <see cref="HordeWindUpKind"/>, C = the length in cells. Flags: the rank's (Elite, Boss, Boss | Final).</summary>
        WindUp = 56,
    }

    /// <summary>Client wave 17 (h17read): how a PlayerHit came (its A).</summary>
    public enum HordeHitVia : byte { Unknown = 0, Touch = 1, Shot = 2, Area = 3, Burst = 4 }

    /// <summary>Client wave 17 (h17read): what a WindUp event winds up (its B).</summary>
    public enum HordeWindUpKind : byte { Charge = 1, Shot = 2 }

    [Flags]
    public enum HordeEventFlags : byte
    {
        None = 0, Crit = 1, Kill = 2, Dot = 4, Dodged = 8, Elite = 16, Boss = 32, Final = 64, Late = 128,
    }

    /// <summary>Something that happened and is worth drawing or sounding once. Which fields a kind fills is on the kind.</summary>
    public struct HordeEvent
    {
        public HordeEventKind Kind;
        public HordeEventFlags Flags;
        public int Tick;
        public int Slot, Target, DefId, Value;
        public float X, Y, X2, Y2;
        public float A, B, C;
    }

    /// <summary>
    /// The run as the client last heard of it. The channel keeps ONE instance and updates it in place when a snapshot
    /// arrives (never between two Poll calls of the main thread). Moving things carry two places to draw between: draw at
    /// Prev + (Cur - Prev) × <see cref="Alpha"/>. Since client wave 9 (h9move) the channel keeps the last three snapshots of
    /// every moving thing and, once per frame (<see cref="Prepare"/>, called by Alpha), sets Prev / Cur to the two snapshots
    /// around the RENDER time: the server's time as this client estimates it, minus one snapshot interval and a jitter
    /// margin (<see cref="RenderDelayTicks"/>). A snapshot that arrives late is thus still in time to be drawn; only when
    /// nothing newer exists is the motion extrapolated, for at most one more snapshot interval, then held. My own character
    /// is the exception: draw it at the channel's MyX / MyY (predicted from my own stick); its slot's Facing / Dir8 / Moving /
    /// Vx / Vy are the prediction's too.
    /// </summary>
    public sealed class HordeWorldView
    {
        /// <summary>The tick of the latest snapshot, and of the one before.</summary>
        public int Tick, PrevTick;
        /// <summary>When they arrived, on the channel's own clock in seconds (the one <see cref="Alpha"/> takes).</summary>
        public double TickAt, PrevTickAt;
        public int TickRate = HordeLimits.TickRate;
        /// <summary>Ticks between two snapshots (the run's; 2 = 20 a second).</summary>
        public int SnapshotEvery = 2;
        /// <summary>My slot (-1: none): the channel owns that player's drawn place and facing.</summary>
        public int MySlot = -1;
        /// <summary>Seconds played, at the latest snapshot.</summary>
        public float Seconds => TickRate > 0 ? Tick / (float)TickRate : 0f;
        /// <summary>True from the final boss's appearance on.</summary>
        public bool FinalPhase;
        /// <summary>design note, 2026-10-05: the world waits while someone chooses a card or a job (<see cref="PausedBy"/>: their slots;
        /// their names are Players[slot].Name). The tick stands; draw everything where it is (Prepare does), send no stick.</summary>
        public bool Paused;
        public readonly List<int> PausedBy = new List<int>();
        /// <summary>design note, 2026-10-05: ONE EXP bar for the party: its run level, EXP into it and what the next level needs (every
        /// player's Level / Exp / ExpToNext repeat them).</summary>
        public int PartyLevel = 1, PartyExp, PartyExpToNext;
        public readonly HordePlayerView[] Players = new HordePlayerView[HordeLimits.MaxPlayers];
        /// <summary>Every enemy slot (HordeLimits.MaxEnemies of them); the live ones are those with Alive.</summary>
        public readonly HordeEnemyView[] Enemies = new HordeEnemyView[HordeLimits.MaxEnemies];
        public int EnemiesAlive;
        public readonly List<HordePickupView> Pickups = new List<HordePickupView>();
        public readonly List<HordeSummonView> Summons = new List<HordeSummonView>();
        public readonly List<HordeFxView> Fx = new List<HordeFxView>();
        /// <summary>Client wave 17 (h17obj): the map objectives now, and the gifts players hold (HordeSim.Objectives.cs).</summary>
        public readonly List<HordeObjectiveView> Objectives = new List<HordeObjectiveView>();
        public readonly List<HordeObjectiveBuffView> ObjectiveBuffs = new List<HordeObjectiveBuffView>();
        /// <summary>Client wave 18 (h18rules), point 23: the co-op pause's countdown (H_SNAP's rules block): the player who asked (-1: none),
        /// to pause (true) or to resume, and its seconds left at the latest snapshot.</summary>
        public int PauseCountBy = -1;
        public bool PauseCountToPause;
        public float PauseCountSeconds;
        /// <summary>Client wave 18 (h18feel), point 35: the soul shards on the field, and each fallen player's count (HordeSim.Souls.cs).</summary>
        public readonly List<HordeSoulView> Souls = new List<HordeSoulView>();
        public readonly List<HordeSoulCountView> SoulCounts = new List<HordeSoulCountView>();
        /// <summary>Client wave 18 (h18feel), point 37: the runners whose Yggdrasil Berry does nothing now (burnt / resting).</summary>
        public readonly List<HordeBerryView> Berries = new List<HordeBerryView>();
        public HordeBerryView BerryOf(int slot) { foreach (var b in Berries) if (b.Slot == slot) return b; return new HordeBerryView { Slot = slot }; }
        /// <summary>A fallen player's shard count (Total 0: none known).</summary>
        public HordeSoulCountView SoulCountOf(int slot) { foreach (var c in SoulCounts) if (c.Slot == slot) return c; return new HordeSoulCountView { Slot = slot }; }

        // ---- the last three snapshots of every moving thing (index 0 = the newest), and the render clock (client wave 9)
        private readonly int[] _hTick = new int[3];
        private int _hN;
        private readonly float[,] _eX = new float[HordeLimits.MaxEnemies, 3], _eY = new float[HordeLimits.MaxEnemies, 3];
        private readonly int[] _eN = new int[HordeLimits.MaxEnemies];
        private readonly float[,] _pX = new float[HordeLimits.MaxPlayers, 3], _pY = new float[HordeLimits.MaxPlayers, 3];
        private readonly float[] _pFacing = new float[HordeLimits.MaxPlayers];
        private readonly int[] _pN = new int[HordeLimits.MaxPlayers];
        private double _clockOffset, _clockJitter; private bool _clockSet;
        private double _preparedAt = double.NaN; private float _alpha = 1f; private double _renderTick;
        /// <summary>Frames drawn past the newest snapshot (extrapolated) and frames drawn at all, for the measurements.</summary>
        public int FramesExtrapolated, FramesPrepared;
        /// <summary>Hysteresis of the eight sprite directions, in degrees past a sector's half width (Loki's own).</summary>
        public const float Dir8HysteresisDeg = 10f;

        public HordeWorldView()
        {
            for (int i = 0; i < Players.Length; i++) Players[i] = new HordePlayerView { Slot = i };
            for (int i = 0; i < Enemies.Length; i++) Enemies[i] = new HordeEnemyView();
        }

        /// <summary>True once the channel feeds this view with snapshot history (the render-delayed drawing); false for a
        /// view written directly (the offline stand-in), which keeps wave 8's Prev / Cur / Alpha meaning.</summary>
        public bool HasHistory => _hN > 0;

        /// <summary>How far between Prev and Cur to draw at <paramref name="now"/> (the channel's clock): 0 = Prev, 1 = Cur; it runs
        /// on to at most 1 + one snapshot interval when nothing newer has arrived (a bounded extrapolation instead of a freeze).
        /// With history, this also sets every moving thing's Prev / Cur for this frame (<see cref="Prepare"/>).</summary>
        public float Alpha(double now)
        {
            if (_hN > 0) { Prepare(now); return _alpha; }
            double span = TickAt - PrevTickAt;
            if (span <= 0.0) return 1f;
            double a = (now - TickAt) / span;
            return a < 0.0 ? 0f : a > 1.5 ? 1.5f : (float)a;
        }

        /// <summary>The tick being drawn at <paramref name="now"/>, with its fraction (for effects that are drawn from
        /// their start values: a Shot's place is X + VX × (DrawTick - StartTick) / TickRate).</summary>
        public float DrawTick(double now)
        {
            if (_hN > 0) { Prepare(now); return (float)_renderTick; }
            return PrevTick + (Tick - PrevTick) * Alpha(now);
        }

        /// <summary>The server tick this client draws at <paramref name="now"/> (one snapshot and a jitter margin behind the
        /// server's time as estimated from the snapshots' arrivals).</summary>
        public double RenderTick(double now) { if (_hN > 0) { Prepare(now); return _renderTick; } return DrawTick(now); }

        /// <summary>Ticks between the server's estimated time and the render time: one snapshot interval plus a margin of twice
        /// the measured arrival jitter (at least a quarter tick, at most one more interval).</summary>
        public double RenderDelayTicks => SnapshotEvery + Math.Max(0.25, Math.Min(SnapshotEvery, _clockJitter * 2.0));

        /// <summary>Called by the channel when a snapshot was applied at <paramref name="now"/>: keeps this tick's places of
        /// every moving thing and feeds the render clock. <paramref name="reset"/>: a key after a gap (the history starts over).
        /// <paramref name="paused"/>: the world waits (the tick repeats: nothing is pushed, the clock is not fed; when it resumes
        /// the clock starts over, the places' history goes on: the ticks are contiguous across a pause).</summary>
        public void PushSnapshot(int tick, double now, bool reset, bool paused = false)
        {
            bool resumed = Paused && !paused;
            Paused = paused;
            if (resumed) _clockSet = false;
            if (paused && !reset && _hN > 0 && tick == _hTick[0]) { _preparedAt = double.NaN; return; }
            if (reset || _hN == 0 || tick <= _hTick[0]) { _hN = 0; _clockSet = false; _clockJitter = 0; }
            // the clock: the server's tick minus this clock's, following the EARLIEST arrivals (the latest are the jitter)
            double o = tick - now * TickRate;
            if (!_clockSet) { _clockOffset = o; _clockSet = true; }
            else
            {
                double dev = _clockOffset - o;
                if (dev < 0) _clockOffset = o;                         // earlier than ever seen: the clock moves up at once
                else _clockOffset -= Math.Min(dev, 0.02 * SnapshotEvery); // and relaxes slowly (a route that got slower, drift)
                _clockJitter += (Math.Abs(dev) - _clockJitter) * 0.1;
            }
            // the history: shift, then this tick at index 0
            for (int k = Math.Min(_hN, 2); k > 0; k--) _hTick[k] = _hTick[k - 1];
            _hTick[0] = tick; if (_hN < 3) _hN++;
            for (int i = 0; i < Enemies.Length; i++)
            {
                var e = Enemies[i];
                if (!e.Alive) { _eN[i] = 0; continue; }
                if (_eN[i] == 0) { for (int k = 0; k < 3; k++) { _eX[i, k] = e.X; _eY[i, k] = e.Y; } _eN[i] = 1; continue; }
                _eX[i, 2] = _eX[i, 1]; _eY[i, 2] = _eY[i, 1]; _eX[i, 1] = _eX[i, 0]; _eY[i, 1] = _eY[i, 0]; _eX[i, 0] = e.X; _eY[i, 0] = e.Y;
                if (_eN[i] < 3) _eN[i]++;
            }
            for (int i = 0; i < Players.Length; i++)
            {
                var p = Players[i];
                if (!p.Present) { _pN[i] = 0; continue; }
                _pFacing[i] = p.Facing;
                if (_pN[i] == 0) { for (int k = 0; k < 3; k++) { _pX[i, k] = p.X; _pY[i, k] = p.Y; } _pN[i] = 1; continue; }
                _pX[i, 2] = _pX[i, 1]; _pY[i, 2] = _pY[i, 1]; _pX[i, 1] = _pX[i, 0]; _pY[i, 1] = _pY[i, 0]; _pX[i, 0] = p.X; _pY[i, 0] = p.Y;
                if (_pN[i] < 3) _pN[i]++;
            }
            foreach (var s in Summons)
            {
                if (s.HN == 0) { for (int k = 0; k < 3; k++) { s.HX[k] = s.X; s.HY[k] = s.Y; } s.HN = 1; continue; }
                s.HX[2] = s.HX[1]; s.HY[2] = s.HY[1]; s.HX[1] = s.HX[0]; s.HY[1] = s.HY[0]; s.HX[0] = s.X; s.HY[0] = s.Y;
                if (s.HN < 3) s.HN++;
            }
            _preparedAt = double.NaN;
        }

        /// <summary>Once per frame (idempotent for the same <paramref name="now"/>): the render tick, and every moving thing's
        /// Prev / Cur set to the two snapshots around it, its Dir8 and velocity.</summary>
        public void Prepare(double now)
        {
            if (_hN == 0 || now == _preparedAt) return;
            _preparedAt = now; FramesPrepared++;
            double r = now * TickRate + _clockOffset - RenderDelayTicks;
            int newest = _hTick[0];
            if (Paused && r > newest) r = newest; // the world waits: everything where the newest snapshot has it, no extrapolation
            if (r > newest + SnapshotEvery) r = newest + SnapshotEvery; // at most one snapshot of extrapolation
            if (r > newest) FramesExtrapolated++;
            // the bracket: samples k+1 (older) → k (newer) around r; alpha may pass 1 (extrapolation) but never below 0
            int k; // the newer sample's index
            if (_hN < 2) { k = 0; _alpha = 1f; }
            else if (r >= _hTick[1] || _hN < 3) { k = 0; }
            else { k = 1; }
            if (_hN >= 2)
            {
                int tNew = _hTick[k], tOld = _hTick[k + 1];
                double span = tNew - tOld;
                double a = span > 0 ? (r - tOld) / span : 1.0;
                if (a < 0) a = 0;
                _alpha = (float)a;
            }
            _renderTick = r;
            int older = Math.Min(k + 1, 2);
            float rate = TickRate;
            for (int i = 0; i < Enemies.Length; i++)
            {
                var e = Enemies[i];
                if (!e.Alive || _eN[i] == 0) continue;
                int kn = Math.Min(k, _eN[i] - 1), ko = Math.Min(older, _eN[i] - 1);
                e.PrevX = _eX[i, ko]; e.PrevY = _eY[i, ko]; e.X = _eX[i, kn]; e.Y = _eY[i, kn];
                int tNew = _hTick[kn], tOld = _hTick[ko];
                float dx = e.X - e.PrevX, dy = e.Y - e.PrevY;
                if (tNew > tOld) { float s = rate / (tNew - tOld); e.Vx = dx * s; e.Vy = dy * s; } else { e.Vx = e.Vy = 0; }
                if (dx * dx + dy * dy > 1e-6f) e.Dir8 = HordeMove.Dir8Hysteresis(e.Dir8, HordeMove.AngleDeg(dx, dy), Dir8HysteresisDeg);
            }
            for (int i = 0; i < Players.Length; i++)
            {
                var p = Players[i];
                if (!p.Present || _pN[i] == 0 || i == MySlot) continue;
                int kn = Math.Min(k, _pN[i] - 1), ko = Math.Min(older, _pN[i] - 1);
                p.PrevX = _pX[i, ko]; p.PrevY = _pY[i, ko]; p.X = _pX[i, kn]; p.Y = _pY[i, kn];
                int tNew = _hTick[kn], tOld = _hTick[ko];
                float dx = p.X - p.PrevX, dy = p.Y - p.PrevY;
                p.Moving = dx * dx + dy * dy > 1e-6f && !p.Fallen;
                if (tNew > tOld) { float s = rate / (tNew - tOld); p.Vx = dx * s; p.Vy = dy * s; } else { p.Vx = p.Vy = 0; }
                p.Dir8 = HordeMove.Dir8Hysteresis(p.Dir8, p.Facing, Dir8HysteresisDeg);
            }
            foreach (var s in Summons)
            {
                if (s.HN == 0) continue;
                int kn = Math.Min(k, s.HN - 1), ko = Math.Min(older, s.HN - 1);
                s.PrevX = s.HX[ko]; s.PrevY = s.HY[ko]; s.X = s.HX[kn]; s.Y = s.HY[kn];
            }
        }

        public HordePickupView Pickup(int id) { foreach (var p in Pickups) if (p.Id == id) return p; return null; }
        public HordeFxView FxOf(int id) { foreach (var f in Fx) if (f.Id == id) return f; return null; }
        public HordeSummonView Summon(int id) { foreach (var s in Summons) if (s.Id == id) return s; return null; }

        public void Clear()
        {
            Tick = PrevTick = 0; TickAt = PrevTickAt = 0; FinalPhase = false; EnemiesAlive = 0;
            foreach (var p in Players) { p.Present = false; p.Weapons.Clear(); p.Passives.Clear(); p.Flags = HordePlayerFlags.None; p.Dir8 = 0; p.Vx = p.Vy = 0; }
            foreach (var p in Players) p.Procs.Clear(); // client wave 17 (h17pass)
            foreach (var e in Enemies) { e.Alive = false; e.Dir8 = 0; }
            Pickups.Clear(); Summons.Clear(); Fx.Clear();
            Objectives.Clear(); ObjectiveBuffs.Clear(); // client wave 17 (h17obj)
            PauseCountBy = -1; PauseCountSeconds = 0f; // client wave 18 (h18rules)
            Souls.Clear(); SoulCounts.Clear(); Berries.Clear(); // client wave 18 (h18feel)
            _hN = 0; _clockSet = false; _clockJitter = 0; _preparedAt = double.NaN; _alpha = 1f; MySlot = -1;
            FramesExtrapolated = FramesPrepared = 0; Paused = false; PausedBy.Clear(); PartyLevel = 1; PartyExp = PartyExpToNext = 0;
        }
    }

    /// <summary>The engine's hard limits (the wire is sized by them).</summary>
    public static class HordeLimits
    {
        public const int MaxPlayers = 8;
        /// <summary>Client wave 18 (h18rules), the design's point 38 ("for now, artificially insert a maximum limitation of four players at the same
        /// time ... I mean the horde mode, not the game overall"): the players of ONE run, its starter included (the host seats no more; a
        /// seat stays its player's for the run: point 25's rejoin). The engine itself still takes MaxPlayers (tests, the balance tool).</summary>
        public const int MaxRunPlayers = 4;
        /// <summary>Enemy slots. RulesDef.MaxEnemies (300 by default) is the number the waves fill; the slots above it are
        /// room for the spawns that are never skipped (bosses, their adds). Client wave 18 (h18rules), point 29: 400 -> 520 with the
        /// tables' cap (300 -> 420), the same 100 slots of room above it (slots travel as u16: the wire's layout is unchanged).</summary>
        public const int MaxEnemies = 520;
        public const int MaxPickups = 400;
        public const int MaxFx = 600;
        public const int MaxSummons = 32;
        /// <summary>An arena is at most this many cells wide and high (places travel as 16 bits of 1/256 cell).</summary>
        public const int MaxArenaCells = 250;
        /// <summary>Ticks a second: 40 since client wave 9 (h9move; 20 in wave 8). The wire's protocol version changed with it.</summary>
        public const int TickRate = 40;
    }
}
