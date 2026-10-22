#nullable disable
using System;

namespace Loki.Horde
{
    // ====================================================================================================================
    // Client wave 18 (h18trans), point 30 of the design's Horde list ("include transcendence for the items where after they reach
    // their maximum level, they can transcend into new items that have the original effect, but in a completely reworked
    // way that is significantly more interesting. Yes, passives should transcend as well. And weapons should get two
    // branches for transcendence."):
    //   - Every non-hidden base weapon gets two transcendence branches (5000 + id and 6000 + id) offered side by side once
    //     the weapon reaches its maximum level.
    //   - Every base passive gets a transcended form (1000 + id) offered once the passive reaches its maximum level.
    // ====================================================================================================================

    /// <summary>The reworked mechanic of a transcended weapon branch.</summary>
    public enum TransKind : byte
    {
        None = 0,
        /// <summary>Bolt branch 1 ("Starburst"): fires a 360-degree radial ring of piercing bolts all around the runner.</summary>
        BoltNova = 1,
        /// <summary>Bolt branch 2 ("Boomerang"): bolts pierce every enemy and reverse at max reach to return to the runner.</summary>
        BoltReturn = 2,
        /// <summary>MeleeArc branch 1 ("Shockwave"): each melee swing also launches 3 piercing shockwave bolts forward.</summary>
        ArcWave = 3,
        /// <summary>MeleeArc branch 2 ("Whirlwind"): a 360-degree whirlwind that pulls surrounding enemies inward before striking.</summary>
        ArcVortex = 4,
        /// <summary>Nova branch 1 ("Aftershock"): erupts a second wider aftershock ring around the epicenter.</summary>
        NovaEcho = 5,
        /// <summary>Nova branch 2 ("Singularity"): pulls nearby enemies inward and leaves a lingering ground zone.</summary>
        NovaImplode = 6,
        /// <summary>AreaAtTarget branch 1 ("Cataclysm"): each strike spawns 3 secondary strikes around its perimeter.</summary>
        AreaCascade = 7,
        /// <summary>AreaAtTarget branch 2 ("Scorched Earth"): each strike leaves a lingering damaging ground zone.</summary>
        AreaScorch = 8,
        /// <summary>GroundZone branch 1 ("Living Storm"): zones actively drift toward enemies (or follow allies) and expand.</summary>
        ZoneDrift = 9,
        /// <summary>GroundZone branch 2 ("Supernova"): zones pull enemies inward on each pulse and detonate when they end.</summary>
        ZoneCollapse = 10,
        /// <summary>Orbiter branch 1 ("Breathing Orbit"): extra orbs whose orbit radius breathes in and out across a wide band.</summary>
        OrbitSpiral = 11,
        /// <summary>Orbiter branch 2 ("Starlight Volley"): orbiting spheres periodically fire piercing bolts at nearby enemies.</summary>
        OrbitShoot = 12,
        /// <summary>Chain branch 1 ("Forked Lightning"): splits into 2 independent chain arcs after hitting the first target.</summary>
        ChainFork = 13,
        /// <summary>Chain branch 2 ("Arc Overload"): every chained enemy erupts in a shock blast and is briefly stunned.</summary>
        ChainOverload = 14,
        /// <summary>Aura branch 1 ("Sanctified Pulse"): expanded aura that both heals/buffs allies and scorches enemies in range.</summary>
        AuraPulse = 15,
        /// <summary>Aura branch 2 ("Radiant Detonation"): every 3rd pulse unleashes a wide shockwave that stuns and knocks back.</summary>
        AuraNova = 16,
        /// <summary>Summon branch 1 ("Legion"): calls +2 faster helpers whose attacks cleave all nearby enemies.</summary>
        SummonLegion = 17,
        /// <summary>Summon branch 2 ("Volatile Kin"): tougher, quicker helpers that detonate in a blast when they expire or fall.</summary>
        SummonExplode = 18,
        /// <summary>Trap branch 1 ("Seeker Mines"): drops +1 trap, and armed traps home in on nearby enemies.</summary>
        TrapSeeker = 19,
        /// <summary>Trap branch 2 ("Cluster Mines"): triggered traps detonate and scatter 3 mini-mines around the blast.</summary>
        TrapCluster = 20,
        /// <summary>Dash branch 1 ("Phantom Afterimage"): dashing leaves a damaging mirage zone along the path.</summary>
        DashMirage = 21,
        /// <summary>Dash branch 2 ("Thunderous Arrival"): dashing unleashes a stun shockwave at both departure and arrival.</summary>
        DashNova = 22,
        /// <summary>Mark branch 1 ("Contagion"): marks +2 enemies, and marks leap to 2 nearby enemies when a marked foe dies.</summary>
        MarkSpread = 23,
        /// <summary>Mark branch 2 ("Doom Brand"): marked enemies take extra damage and explode when the mark ends or they die.</summary>
        MarkDetonate = 24,
        /// <summary>Parry branch 1 ("Aegis Tempest"): blocking any hit unleashes a counter-shockwave around the runner.</summary>
        ParryTempest = 25,
        /// <summary>Parry branch 2 ("Mirror Prism"): +2 guard charges, and every block fires 3 piercing retaliatory bolts.</summary>
        ParryPrism = 26,
        /// <summary>Pet branch 1 ("Twin Companions"): a second twin companion acts alongside the first.</summary>
        PetTwin = 27,
        /// <summary>Pet branch 2 ("Apex Companion"): every 3rd companion action unleashes an elemental burst around its target.</summary>
        PetApex = 28,
        /// <summary>Random caster branch 1 ("Twin Chorus"): casts 2 random spells simultaneously on each trigger.</summary>
        RandomTwin = 29,
        /// <summary>Random caster branch 2 ("Grand Arcanum"): casts a supercharged spell and unleashes an elemental nova.</summary>
        RandomOverload = 30,
    }

    /// <summary>The reworked keystone mechanic of a transcended passive.</summary>
    public enum PassiveTransKind : byte
    {
        None = 0,
        /// <summary>Vitality / Defence passives ("Immortal Bastion"): periodically pulses a Max-HP-scaling shockwave and heals you.</summary>
        VitalFortress = 1,
        /// <summary>Speed / Dodge passives ("Tempest Stride"): moving leaves sparking wind trails that shock and slow enemies.</summary>
        Windrunner = 2,
        /// <summary>Power / Melee passives ("Overwhelming Force"): every 4th hit unleashes a cleave shockwave and sunders enemy armour.</summary>
        Warlord = 3,
        /// <summary>Ranged / Shot passives ("Ricochet Mastery"): projectiles gain +1 pierce and every 3rd ranged hit fires a ricochet bolt.</summary>
        Deadeye = 4,
        /// <summary>Magic / Cooldown / Area passives ("Arcane Convergence"): periodically erupts an arcane nova and reduces cooldowns.</summary>
        Archmage = 5,
        /// <summary>Crit / Luck / Effect passives ("Fated Execution"): crits chain lightning to a nearby foe and execute low-HP non-bosses.</summary>
        FortuneCrit = 6,
        /// <summary>Slayer / Bane passives ("Bane Mastery"): half of your bane bonus applies to all foes, and favored foes explode on hit.</summary>
        Slayer = 7,
        /// <summary>Utility / Harvest passives ("Soul Harvest"): pickups and every 5th kill fire homing soul bolts and heal you.</summary>
        Harvester = 8,
    }

    public sealed partial class WeaponDef
    {
        /// <summary>Client wave 18 (h18trans): the root weapon this transcended def ascends from (0 = not a transcended weapon)
        /// and which transcendence branch it is (1 or 2).</summary>
        public int TransOf, TransBranch;
        /// <summary>A base weapon's two transcendence branch ids (5000 + id, 6000 + id); empty on other weapons.</summary>
        public int[] TransBranches = new int[0];
        /// <summary>The transcendence branch's title (a Lang key in the client); empty on non-transcended weapons.</summary>
        public string TransName = "";
        /// <summary>The reworked mechanic of this transcended weapon.</summary>
        public TransKind Trans;
    }

    public sealed partial class PassiveDef
    {
        /// <summary>Client wave 18 (h18trans): the base passive this transcended def ascends from (0 = base passive).</summary>
        public int TransOf;
        /// <summary>The transcended passive's title (a Lang key in the client); empty on base passives.</summary>
        public string TransName = "";
        /// <summary>The reworked keystone mechanic of this transcended passive.</summary>
        public PassiveTransKind Trans;
    }

    /// <summary>Client wave 18 (h18trans): the content stamp's part for the transcendence fields.</summary>
    public static class HordeTransStamp
    {
        public static void Add(HordeContent c, Action<double> m, Action<double, int> n)
        {
            void S(string s) { if (string.IsNullOrEmpty(s)) return; m(-80); foreach (char ch in s) m(ch); m(-1); }
            void A(int[] a, int tag) { if (a == null || a.Length == 0) return; m(tag); foreach (var v in a) m(v); m(-1); }
            foreach (var w in c.Weapons)
            {
                if (w.TransOf == 0 && (w.TransBranches == null || w.TransBranches.Length == 0) && w.Trans == TransKind.None) continue;
                n(w.Id, -81);
                n(w.TransOf, -82);
                n(w.TransBranch, -83);
                A(w.TransBranches, -84);
                S(w.TransName);
                n((int)w.Trans, -85);
            }
            foreach (var p in c.Passives)
            {
                if (p.TransOf == 0 && p.Trans == PassiveTransKind.None) continue;
                n(p.Id, -86);
                n(p.TransOf, -87);
                S(p.TransName);
                n((int)p.Trans, -88);
            }
        }
    }
}
