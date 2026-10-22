//! skills/gunslinger/skill_factory_gunslinger.cpp: SkillFactoryGunslinger::create and the gunslinger family's classes (rAthena
//! e985006), one file per class as rAthena has them (the file names are rAthena's; 16thnight.cpp is the module
//! e985006), one file per class as rAthena has them (roundtrip.rs holds two classes, as roundtrip.cpp does).

use super::{SkillImpl, StatusSkillImpl};
use crate::db::skills::*;
use crate::unit::Unit;
use crate::world::Ctx;

pub mod antimaterialblast;
pub mod banishingbuster;
pub mod basicgrenade;
pub mod bindtrap;
pub mod bullseye;
pub mod chainaction;
pub mod cracker;
pub mod crimsonmarker;
pub mod desperado;
pub mod disarm;
pub mod dragontail;
pub mod dust;
pub mod fallenangel;
pub mod firedance;
pub mod firerain;
pub mod flicker;
pub mod fling;
pub mod fullbuster;
pub mod gatlingfever;
pub mod glittering;
pub mod grenadefragment;
pub mod grenadesdropping;
pub mod grounddrift;
pub mod hammerofgod;
pub mod hastyfireinthehole;
pub mod howlingmine;
pub mod intensiveaim;
pub mod magazineforone;
pub mod massspiral;
pub mod midnightfallen;
pub mod missionbombard;
pub mod onlyonebullet;
pub mod piercingshot;
pub mod quickdrawshot;
pub mod rapidshower;
pub mod richscoin;
pub mod roundtrip;
pub mod shatterstorm;
pub mod slugshot;
pub mod spiralshooting;
pub mod spreadattack;
pub mod thevigilanteatnight;
pub mod tracking;
pub mod tripleaction;
pub mod wildfire;
pub mod wildshot;

/// SkillFactoryGunslinger::create (skill_factory_gunslinger.cpp), its arms in rAthena's order.
pub fn create(skill_id: u16) -> Option<Box<dyn SkillImpl>> {
    match skill_id {
        GS_ADJUSTMENT => Some(Box::new(StatusSkillImpl::new(skill_id))),
        GS_BULLSEYE => Some(Box::new(bullseye::SkillBullseye)),
        GS_CHAINACTION => Some(Box::new(chainaction::SkillChainAction)),
        GS_CRACKER => Some(Box::new(cracker::SkillCracker)),
        GS_DESPERADO => Some(Box::new(desperado::SkillDesperado)),
        GS_DISARM => Some(Box::new(disarm::SkillDisarm)),
        GS_DUST => Some(Box::new(dust::SkillDust)),
        GS_FLING => Some(Box::new(fling::SkillFling)),
        GS_FULLBUSTER => Some(Box::new(fullbuster::SkillFullBuster)),
        GS_GATLINGFEVER => Some(Box::new(gatlingfever::SkillGatlingfever)),
        GS_GLITTERING => Some(Box::new(glittering::SkillGlittering)),
        GS_GROUNDDRIFT => Some(Box::new(grounddrift::SkillGroundDrift)),
        GS_INCREASING => Some(Box::new(StatusSkillImpl::new(skill_id))),
        GS_MADNESSCANCEL => Some(Box::new(StatusSkillImpl::new(skill_id))),
        GS_MAGICALBULLET => Some(Box::new(StatusSkillImpl::new(skill_id))), // #ifdef RENEWAL (WeaponSkillImpl otherwise)
        GS_PIERCINGSHOT => Some(Box::new(piercingshot::SkillPiercingShot)),
        GS_RAPIDSHOWER => Some(Box::new(rapidshower::SkillRapidShower)),
        GS_SPREADATTACK => Some(Box::new(spreadattack::SkillSpreadAttack)),
        GS_TRACKING => Some(Box::new(tracking::SkillTracking)),
        GS_TRIPLEACTION => Some(Box::new(tripleaction::SkillTripleAction)),
        NW_AUTO_FIRING_LAUNCHER => Some(Box::new(StatusSkillImpl::new(skill_id))),
        NW_BASIC_GRENADE => Some(Box::new(basicgrenade::SkillBasicGrenade)),
        NW_GRENADES_DROPPING => Some(Box::new(grenadesdropping::SkillGrenadesDropping)),
        NW_GRENADE_FRAGMENT => Some(Box::new(grenadefragment::SkillGrenadeFragment)),
        NW_HASTY_FIRE_IN_THE_HOLE => Some(Box::new(hastyfireinthehole::SkillHastyFireInTheHole)),
        NW_HIDDEN_CARD => Some(Box::new(StatusSkillImpl::new(skill_id))),
        NW_INTENSIVE_AIM => Some(Box::new(intensiveaim::SkillIntensiveAim)),
        NW_MAGAZINE_FOR_ONE => Some(Box::new(magazineforone::SkillMagazineForOne)),
        NW_MIDNIGHT_FALLEN => Some(Box::new(midnightfallen::SkillMidnightFallen)),
        NW_MISSION_BOMBARD => Some(Box::new(missionbombard::SkillMissionBombard)),
        NW_ONLY_ONE_BULLET => Some(Box::new(onlyonebullet::SkillOnlyOneBullet)),
        NW_SPIRAL_SHOOTING => Some(Box::new(spiralshooting::SkillSpiralShooting)),
        NW_THE_VIGILANTE_AT_NIGHT => Some(Box::new(thevigilanteatnight::SkillTheVigilanteAtNight)),
        NW_WILD_FIRE => Some(Box::new(wildfire::SkillWildFire)),
        NW_WILD_SHOT => Some(Box::new(wildshot::SkillWildShot)),
        RL_AM_BLAST => Some(Box::new(antimaterialblast::SkillAntiMaterialBlast)),
        RL_BANISHING_BUSTER => Some(Box::new(banishingbuster::SkillBanishingBuster)),
        RL_B_TRAP => Some(Box::new(bindtrap::SkillBindTrap)),
        RL_C_MARKER => Some(Box::new(crimsonmarker::SkillCrimsonMarker)),
        RL_D_TAIL => Some(Box::new(dragontail::SkillDragonTail)),
        RL_E_CHAIN => Some(Box::new(StatusSkillImpl::new(skill_id))),
        RL_FALLEN_ANGEL => Some(Box::new(fallenangel::SkillFallenAngel)),
        RL_FIREDANCE => Some(Box::new(firedance::SkillFireDance)),
        RL_FIRE_RAIN => Some(Box::new(firerain::SkillFireRain)),
        RL_FLICKER => Some(Box::new(flicker::SkillFlicker)),
        RL_HAMMER_OF_GOD => Some(Box::new(hammerofgod::SkillHammerOfGod)),
        RL_HEAT_BARREL => Some(Box::new(StatusSkillImpl::new(skill_id))),
        RL_H_MINE => Some(Box::new(howlingmine::SkillHowlingMine)),
        RL_MASS_SPIRAL => Some(Box::new(massspiral::SkillMassSpiral)),
        RL_P_ALTER => Some(Box::new(StatusSkillImpl::new(skill_id))),
        RL_QD_SHOT => Some(Box::new(quickdrawshot::SkillQuickDrawShot)),
        RL_RICHS_COIN => Some(Box::new(richscoin::SkillRichsCoin)),
        RL_R_TRIP => Some(Box::new(roundtrip::SkillRoundTrip)),
        RL_R_TRIP_PLUSATK => Some(Box::new(roundtrip::SkillRoundTripPlusAttack)),
        RL_S_STORM => Some(Box::new(shatterstorm::SkillShatterStorm)),
        RL_SLUGSHOT => Some(Box::new(slugshot::SkillSlugShot)),
        _ => None,
    }
}

// ---------------------------------------------------------------------------------------------- shared helpers

/// `sd->weapontype1` of a player caster (BL_CAST(BL_PC, src)): None for any other caster.
pub(crate) fn weapontype1(c: &Ctx, src: &Unit) -> Option<u8> {
    if src.kind != crate::map::BL_PC {
        return None;
    }
    crate::inventory::st(c, src.id).map(|it| it.weapontype1)
}

/// `sd->status.weapon` of a player caster (the mmo_charstatus weapon type): None for any other caster.
pub(crate) fn status_weapon(c: &Ctx, src: &Unit) -> Option<u8> {
    if src.kind != crate::map::BL_PC {
        return None;
    }
    crate::pc::character_of(c, src.id).map(|ch| ch.weapon as u8)
}

/// The modifyElement of SkillBasicGrenade / SkillGrenadesDropping / SkillHastyFireInTheHole / SkillMissionBombard
/// (Night Watch Grenade Fragment elementals): the first SC_GRENADE_FRAGMENT_1..6 the caster has picks water, wind,
/// earth, fire, dark or holy.
pub(crate) fn grenade_fragment_element(c: &Ctx, src: &Unit, element: &mut i32) {
    use crate::status::consts::*;
    use crate::status::defs::{ELE_DARK, ELE_EARTH, ELE_FIRE, ELE_HOLY, ELE_WATER, ELE_WIND};
    if !crate::battle::mobif::has_sc(src.kind) {
        return;
    }
    let has = |t| crate::status::sc::has_sce(c, src.id, t);
    if has(SC_GRENADE_FRAGMENT_1) {
        *element = ELE_WATER;
    } else if has(SC_GRENADE_FRAGMENT_2) {
        *element = ELE_WIND;
    } else if has(SC_GRENADE_FRAGMENT_3) {
        *element = ELE_EARTH;
    } else if has(SC_GRENADE_FRAGMENT_4) {
        *element = ELE_FIRE;
    } else if has(SC_GRENADE_FRAGMENT_5) {
        *element = ELE_DARK;
    } else if has(SC_GRENADE_FRAGMENT_6) {
        *element = ELE_HOLY;
    }
}

/// `if (sc && sc->getSCE(SC_INTENSIVE_AIM_COUNT)) status_change_end(src, SC_INTENSIVE_AIM_COUNT);` (the Night Watch
/// attacks spend the aim count).
pub(crate) fn end_intensive_aim_count(c: &Ctx, src: &Unit) {
    use crate::status::consts::SC_INTENSIVE_AIM_COUNT;
    if crate::skills::novice::sce(c, src, SC_INTENSIVE_AIM_COUNT).is_some() {
        crate::status::sc::status_change_end(c, src.id, SC_INTENSIVE_AIM_COUNT, 0);
    }
}

/// `sc->getSCE(SC_INTENSIVE_AIM_COUNT)->val1` when the caster has the status.
pub(crate) fn intensive_aim_count(c: &Ctx, src: &Unit) -> Option<i32> {
    crate::skills::novice::sce(c, src, crate::status::consts::SC_INTENSIVE_AIM_COUNT).map(|e| e.val1)
}

#[cfg(test)]
mod tests {
    use super::*;

    /// skill_factory_gunslinger.cpp's case labels, in its order.
    const RATHENA_ARMS: &[u16] = &[
        GS_ADJUSTMENT, GS_BULLSEYE, GS_CHAINACTION, GS_CRACKER, GS_DESPERADO, GS_DISARM, GS_DUST, GS_FLING,
        GS_FULLBUSTER, GS_GATLINGFEVER, GS_GLITTERING, GS_GROUNDDRIFT, GS_INCREASING, GS_MADNESSCANCEL,
        GS_MAGICALBULLET, GS_PIERCINGSHOT, GS_RAPIDSHOWER, GS_SPREADATTACK, GS_TRACKING, GS_TRIPLEACTION,
        NW_AUTO_FIRING_LAUNCHER, NW_BASIC_GRENADE, NW_GRENADES_DROPPING, NW_GRENADE_FRAGMENT,
        NW_HASTY_FIRE_IN_THE_HOLE, NW_HIDDEN_CARD, NW_INTENSIVE_AIM, NW_MAGAZINE_FOR_ONE, NW_MIDNIGHT_FALLEN,
        NW_MISSION_BOMBARD, NW_ONLY_ONE_BULLET, NW_SPIRAL_SHOOTING, NW_THE_VIGILANTE_AT_NIGHT, NW_WILD_FIRE,
        NW_WILD_SHOT, RL_AM_BLAST, RL_BANISHING_BUSTER, RL_B_TRAP, RL_C_MARKER, RL_D_TAIL, RL_E_CHAIN,
        RL_FALLEN_ANGEL, RL_FIREDANCE, RL_FIRE_RAIN, RL_FLICKER, RL_HAMMER_OF_GOD, RL_HEAT_BARREL, RL_H_MINE,
        RL_MASS_SPIRAL, RL_P_ALTER, RL_QD_SHOT, RL_RICHS_COIN, RL_R_TRIP, RL_R_TRIP_PLUSATK, RL_S_STORM, RL_SLUGSHOT,
    ];

    /// Every case of rAthena's factory has a class here, reporting its own skill id, and nothing else does.
    #[test]
    fn factory_arms_match_rathena() {
        for &id in RATHENA_ARMS {
            let imp = create(id).unwrap_or_else(|| panic!("no class for skill {id}"));
            assert_eq!(imp.skill_id(), id);
        }
        for s in skill_db().iter() {
            if !RATHENA_ARMS.contains(&s.nameid) {
                assert!(create(s.nameid).is_none(), "{} is not in rAthena's factory", s.name);
            }
        }
        assert_eq!(RATHENA_ARMS.len(), 56);
    }
}
