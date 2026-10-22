#nullable disable
using System;

namespace Loki.Horde
{
    // ====================================================================================================================
    // Client wave 17 (h17cards): design 1 / point 15 of the design's Horde list ("Approved design specification."):
    //   A more cards (every job and job-free), B branches at level 3, C evolutions (one per job line), and the new weapon kinds
    //   Dash, Mark, Parry, Pet. The content types' new fields; the rules are in HordeSim.Cards.cs, the tables in
    //   Content/HordeCards.cs. Pure C# (Loki Host compiles it too). Every field's default is the engine's behaviour before wave 17.
    // ====================================================================================================================

    /// <summary>When a Dash weapon dashes.</summary>
    public enum DashTrigger : byte
    {
        /// <summary>On its cooldown, at its target (Targeting within Range): the player dashes Range cells that way.</summary>
        Cooldown = 0,
        /// <summary>When the player is hurt (and the weapon is ready): it dashes Range cells away from what hit it.</summary>
        WhenHit = 1,
        /// <summary>When at least Count enemies touch the player (and it is ready): it blinks Range cells to the side with the fewest.</summary>
        Crowded = 2,
    }

    /// <summary>What a Parry weapon's charges block.</summary>
    [Flags]
    public enum ParryFlags : byte
    {
        None = 0,
        /// <summary>An enemy's touch (and an exploder's burst).</summary>
        Touches = 1,
        /// <summary>An enemy's shot.</summary>
        Shots = 2,
        /// <summary>An enemy's area strike (an elite's or a boss's warned area).</summary>
        Strikes = 4,
        /// <summary>A blocked shot flies back at its shooter (the weapon's Damage plus the shot's own).</summary>
        Returns = 8,
    }

    /// <summary>What a Pet does.</summary>
    public enum PetMode : byte
    {
        /// <summary>Runs to the nearest enemy within 8 cells of its owner and strikes it (Area &gt; 0: everything around it).</summary>
        Striker = 0,
        /// <summary>Stays by its owner and shoots the nearest enemy within Range every Tick.</summary>
        Caster = 1,
        /// <summary>Stays by its owner and heals the most hurt ally within Range every Tick (Heal / HealPct).</summary>
        Healer = 2,
        /// <summary>Stays by its owner and strikes what comes close; while it is out its owner has the level's Buffs.</summary>
        Guard = 3,
    }

    public sealed partial class WeaponLevel
    {
        /// <summary>Mark: percent more damage the marked enemy takes; MarkHits: the mark ends after that many hits (0 = at Duration).</summary>
        public float MarkPct;
        public int MarkHits;
        /// <summary>Mark: percent less damage the marked enemy deals (100 = none: a blinded enemy misses).</summary>
        public float WeakenPct;
        /// <summary>Mark: the marked enemies go for the marker (Provoke).</summary>
        public bool Taunt;
        /// <summary>A weapon that casts on hit (WeaponDef.CastOnHit): chance in percent per hit of the player's other weapons.</summary>
        public float ProcPct;
        /// <summary>Aura with RevivePct &gt; 0: a fallen ally within Range rises at once with this share of its max HP (none fallen: it waits).</summary>
        public float RevivePct;
        /// <summary>Aura with LowestHpAlly: it heals only an ally (or the player) below this share of max HP, in percent (0 = any hurt one).</summary>
        public float BelowPct;
        /// <summary>Each enemy hit bursts: Damage × SplashPct / 100 to the enemies within Splash cells of it (an evolved Bash's chain).</summary>
        public float Splash, SplashPct;
        /// <summary>Chance in percent that a hit destroys an Undead enemy at once (not an elite or a boss).</summary>
        public float ExecutePct;
        /// <summary>A shallow copy (the tables' branch and evolution builders copy Effects and Buffs themselves).</summary>
        public WeaponLevel Clone() => (WeaponLevel)MemberwiseClone();
    }

    public sealed partial class WeaponDef
    {
        /// <summary>A level-3 branch (point 15 B): the weapon it branches from and which one it is (1 or 2). A branch def has the base's
        /// levels 1-2 and its own 3+; it is never offered as a new weapon (Weight 0): a card of the base at level 2 → 3 offers it.</summary>
        public int BranchOf, Branch;
        /// <summary>A base weapon's two branches (WeaponDef ids); empty = no branches.</summary>
        public int[] Branches = new int[0];
        /// <summary>The branch's name (a Lang key in the client: "Barrage", "Lance" …); empty on other weapons.</summary>
        public string BranchName = "";
        /// <summary>An evolved form (point 15 C): the weapon it evolves from (its top level, branch or not) and what it needs beside it
        /// (a weapon or a passive the player owns). A big chest (3+ upgrades) turns the weapon into this def, at the same level.</summary>
        public int EvolveOf, NeedsWeapon, NeedsPassive;
        /// <summary>The evolved form's own name (a Lang key in the client); empty = the base's.</summary>
        public string EvolvedName = "";
        /// <summary>Weapons of one group exclude each other in the offers (the homunculi): 0 = none.</summary>
        public int Group;
        /// <summary>Never a card, never in the loadout: a strike or a companion the engine makes for a passive (the falcon, Chain Combo's blow).</summary>
        public bool Hidden;
        public DashTrigger DashOn;
        public ParryFlags Parry;
        public PetMode Pet;
        /// <summary>Hits a Small / Medium / Large enemy one / two / three times (Pierce).</summary>
        public bool HitsBySize;
        /// <summary>Each use casts one of these weapons (WeaponDef ids), at random, at this weapon's level (Abracadabra; with CastOnHit: Auto Spell).</summary>
        public int[] RandomOf = new int[0];
        /// <summary>It does not fire on its cooldown: a hit of the player's other weapons may cast it (WeaponLevel.ProcPct; the cooldown is its
        /// shortest gap).</summary>
        public bool CastOnHit;
        /// <summary>Count + the level of this owned weapon (Finger Offensive throws the Spirit Spheres).</summary>
        public int BonusCountFrom;
        /// <summary>After its last blow, a hidden weapon (ComboWeapon) strikes once at the level of the passive ComboPassive, if owned (Triple
        /// Attack and Chain Combo).</summary>
        public int ComboPassive, ComboWeapon;
        /// <summary>Summon: it bursts on its first attack and is gone (Sphere Mine).</summary>
        public bool SummonBursts;
    }

    public sealed partial class PassiveDef
    {
        /// <summary>Passives of one group exclude each other in the offers (the arrows, Endow): 0 = none.</summary>
        public int Group;
        /// <summary>Owning the passive gives this hidden weapon at the passive's level (Falcon Mastery's falcon): 0 = none.</summary>
        public int GivesWeapon;
    }
}

namespace Loki.Horde
{
    /// <summary>Client wave 17 (h17cards): the content stamp's part for the new fields (HordeWire.ContentStamp calls it last). A field
    /// is hashed only when set, as the wave-9 fields are (tables without them keep their stamp).</summary>
    public static class HordeCardsStamp
    {
        public static void Add(HordeContent c, System.Action<double> m, System.Action<double, int> n)
        {
            void S(string s) { if (string.IsNullOrEmpty(s)) return; m(-40); foreach (char ch in s) m(ch); m(-1); }
            void A(int[] a, int tag) { if (a == null || a.Length == 0) return; m(tag); foreach (var v in a) m(v); m(-1); }
            foreach (var w in c.Weapons)
            {
                n(w.Id, -41);
                n(w.BranchOf, -42); n(w.Branch, -43); A(w.Branches, -44); S(w.BranchName);
                n(w.EvolveOf, -45); n(w.NeedsWeapon, -46); n(w.NeedsPassive, -47); S(w.EvolvedName);
                n(w.Group, -48); n(w.Hidden ? 1 : 0, -49); n((int)w.DashOn, -50); n((int)w.Parry, -51); n((int)w.Pet, -52); n(w.HitsBySize ? 1 : 0, -53);
                A(w.RandomOf, -54); n(w.CastOnHit ? 1 : 0, -55); n(w.BonusCountFrom, -56); n(w.ComboPassive, -57); n(w.ComboWeapon, -58);
                n(w.SummonBursts ? 1 : 0, -59); n(w.ItemId, -60);
                foreach (var l in w.Levels)
                {
                    if (l == null) continue;
                    n(l.MarkPct, -61); n(l.MarkHits, -62); n(l.WeakenPct, -63); n(l.Taunt ? 1 : 0, -64); n(l.ProcPct, -65); n(l.RevivePct, -66);
                    n(l.BelowPct, -67); n(l.Splash, -68); n(l.SplashPct, -69); n(l.ExecutePct, -70);
                }
            }
            foreach (var p in c.Passives) { n(p.Id, -71); n(p.Group, -72); n(p.GivesWeapon, -73); }
        }
    }
}
