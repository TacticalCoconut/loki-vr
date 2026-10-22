using System.Collections.Generic;
using Loki.Game;
using UnityEngine;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 18 (h18trans), the design's point 30: card titles, descriptions, and keystone summaries for Weapon &amp; Passive
    /// Transcendence (Ascension). Weapons at max level (Lv 5) offer two distinct reworked transcendence branches (Lv 6);
    /// passives at max level (Lv 5) transcend into an ascended keystone form (Lv 6).
    /// </summary>
    public static class HordeTransText
    {
        private static readonly string[] TransTableNames =
        {
            Lang.N("Spiral"), Lang.N("Boomerang"),
            Lang.N("Echo"), Lang.N("Waveblade"),
            Lang.N("Vacuum"), Lang.N("Aftershock"),
            Lang.N("Cataclysm"), Lang.N("Apocalypse"),
            Lang.N("Drifting"), Lang.N("Vortex"),
            Lang.N("Sentinel"), Lang.N("Pulsar"),
            Lang.N("Web"), Lang.N("Overload"),
            Lang.N("Sanctuary"), Lang.N("Judgment"),
            Lang.N("Cluster"), Lang.N("Seeker"),
            Lang.N("Martyr"), Lang.N("Frenzy"),
            Lang.N("Mirage"), Lang.N("Thunderstep"),
            Lang.N("Plague"), Lang.N("Doom"),
            Lang.N("Counter"), Lang.N("Fortress"),
            Lang.N("Twin"), Lang.N("Soulbond"),
            Lang.N("Triad"), Lang.N("Overdrive"),
            Lang.N("Immortal Bastion"), Lang.N("Gale Strider"), Lang.N("Conqueror"), Lang.N("Deadeye"),
            Lang.N("Arcane Sovereign"), Lang.N("Fateweaver"), Lang.N("Executioner"), Lang.N("Golden Harvest"),
        };

        public static string WeaponName(HordeContent c, int id)
        {
            var w = c?.Weapon(id);
            if (w == null || w.TransOf <= 0) return null;
            return HordeText.Weapon(c, w.TransOf) + " · " + Lang.T(w.TransName);
        }

        public static string PassiveName(HordeContent c, int id)
        {
            var p = c?.Passive(id);
            if (p == null || p.TransOf <= 0) return null;
            return HordeText.Passive(c, p.TransOf) + " · " + Lang.T(p.TransName);
        }

        public static string WeaponTransDoes(HordeContent c, WeaponDef w)
        {
            if (w == null) return "";
            string core = w.Trans switch
            {
                TransKind.BoltNova => Lang.T("TRANS: Fires a 360° spiral ring of piercing bolts all around you instead of aiming one way."),
                TransKind.BoltReturn => Lang.T("TRANS: Bolts pierce every enemy and boomerang back to you at 1.4x speed, striking twice."),
                TransKind.ArcVortex => Lang.T("TRANS: Each swing unleashes a mirrored back-slash behind you and pulls nearby enemies into the arc."),
                TransKind.ArcWave => Lang.T("TRANS: Each swing launches a fan of 3 piercing shockwave blades across the arena."),
                TransKind.NovaImplode => Lang.T("TRANS: Vacuum-implodes enemies inward from 1.5x radius and stuns them before the nova detonates."),
                TransKind.NovaEcho => Lang.T("TRANS: Leaves a burning aftershock zone on the ground and triggers a second nova wave."),
                TransKind.AreaCascade => Lang.T("TRANS: Each impact spawns 3 secondary cataclysm blasts around the strike zone."),
                TransKind.AreaScorch => Lang.T("TRANS: Impacts leave a lingering scorched zone that continuously damages and slows enemies."),
                TransKind.ZoneDrift => Lang.T("TRANS: Zones drift across the battlefield toward the nearest enemies, chasing the horde."),
                TransKind.ZoneCollapse => Lang.T("TRANS: Zones become gravity wells that continuously drag nearby enemies into their center."),
                TransKind.OrbitShoot => Lang.T("TRANS: Orbiting spheres automatically fire homing bolts at nearby enemies as they spin."),
                TransKind.OrbitSpiral => Lang.T("TRANS: Orbit radius expands and contracts in waves, sweeping both close and distant rings."),
                TransKind.ChainFork => Lang.T("TRANS: Arcs fork from the first target into 2 extra parallel lightning chains."),
                TransKind.ChainOverload => Lang.T("TRANS: Every enemy struck by the chain erupts in an area shockwave that stuns neighbours."),
                TransKind.AuraPulse => Lang.T("TRANS: Each pulse consecrates the ground beneath you with a lasting sanctuary zone."),
                TransKind.AuraNova => Lang.T("TRANS: Each pulse calls down lightning smites on the 3 strongest nearby enemies."),
                TransKind.TrapCluster => Lang.T("TRANS: Detonating a trap scatters 3 armed mini-traps around the blast."),
                TransKind.TrapSeeker => Lang.T("TRANS: Armed traps actively crawl toward nearby enemies like homing mines."),
                TransKind.SummonExplode => Lang.T("TRANS: When a summon expires or falls, it detonates in a massive explosion and heals you."),
                TransKind.SummonLegion => Lang.T("TRANS: Every hit dealt by your summons shaves 0.25s off all your other weapon cooldowns."),
                TransKind.DashMirage => Lang.T("TRANS: Dashing leaves a pulsing mirage zone at your start point and triggers a nova on arrival."),
                TransKind.DashNova => Lang.T("TRANS: Dashing calls down lightning bolts on every enemy along and near your path."),
                TransKind.MarkSpread => Lang.T("TRANS: When a marked enemy dies, its mark spreads to 2 nearby unmarked enemies and explodes."),
                TransKind.MarkDetonate => Lang.T("TRANS: Every hit against a marked enemy calls down a bonus judgment bolt for +35% damage."),
                TransKind.ParryTempest => Lang.T("TRANS: Blocking any hit unleashes a retaliatory shockwave nova and grants 2s of haste."),
                TransKind.ParryPrism => Lang.T("TRANS: While your guard holds charges, it pulses damage and knockback around you every second."),
                TransKind.PetTwin => Lang.T("TRANS: Summons a twin companion so two pets fight, cast, or heal side by side."),
                TransKind.PetApex => Lang.T("TRANS: Every time your pet acts, a resonant shockwave pulses around both the pet and you."),
                TransKind.RandomTwin => Lang.T("TRANS: Casts 2 additional random spells simultaneously on every trigger."),
                TransKind.RandomOverload => Lang.T("TRANS: Every random spell cast grants a stacking +25% damage & cooldown haste buff for 4s."),
                _ => "",
            };
            return core;
        }

        public static string PassiveKeystoneDoes(PassiveTransKind k) => k switch
        {
            PassiveTransKind.VitalFortress => Lang.T("KEYSTONE: Every 4s emits a fortress shockwave around you that damages and pushes back enemies."),
            PassiveTransKind.Windrunner => Lang.T("KEYSTONE: Moving builds wind charge that unleashes a piercing gale bolt at the nearest enemy."),
            PassiveTransKind.Warlord => Lang.T("KEYSTONE: Every 5th hit unleashes a conquering shockwave around the target."),
            PassiveTransKind.Deadeye => Lang.T("KEYSTONE: All your projectiles gain +1 Pierce and +15% critical damage."),
            PassiveTransKind.Archmage => Lang.T("KEYSTONE: Every 6s reduces all active weapon cooldowns by 1s and pulses arcane energy."),
            PassiveTransKind.FortuneCrit => Lang.T("KEYSTONE: Critical hits have a 35% chance to chain a lucky bolt to another nearby enemy."),
            PassiveTransKind.Slayer => Lang.T("KEYSTONE: Automatically executes normal enemies below 12% HP and elites below 5% HP on hit."),
            PassiveTransKind.Harvester => Lang.T("KEYSTONE: Every 12 EXP gems collected heals 5% HP and grants 3s of haste."),
            _ => "",
        };
    }
}
