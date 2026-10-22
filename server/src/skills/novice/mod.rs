//! skills/novice/skill_factory_novice.cpp: SkillFactoryNovice::create and the novice family's classes (rAthena e985006),
//! one file per class as rAthena has them.
//!
//! The end of this file holds the small helpers the class files of every family use (rAthena's header macros and
//! one-line accessors: RE_LVL_DMOD, pc_checkskill on a block_list, status_get_sc(bl)->getSCE, status_get_amotion,
//! party_foreachsamemap with skill_area_sub, the parent body of SkillImplRecursiveDamageSplash::splashDamage).

use super::{SkillImpl, StatusSkillImpl};
use crate::db::skills::*;
use crate::skill::area::SkillFunc;
use crate::status::data::ScEntry;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub mod doublebowlingbash;
pub mod firstaid;
pub mod groundgravitation;
pub mod hellsdrive;
pub mod helpangel;
pub mod jackfrostnova;
pub mod jupitelthunderstorm;
pub mod megasonicblow;
pub mod meteorstormbuster;
pub mod napalmvulcanstrike;
pub mod overcomingcrisis;
pub mod shieldchainrush;
pub mod spiralpiercemax;

/// SkillFactoryNovice::create.
pub fn create(skill_id: u16) -> Option<Box<dyn SkillImpl>> {
    match skill_id {
        HN_BREAKINGLIMIT => Some(Box::new(StatusSkillImpl::new(skill_id))),
        HN_DOUBLEBOWLINGBASH => Some(Box::new(doublebowlingbash::SkillDoubleBowlingBash)),
        HN_GROUND_GRAVITATION => Some(Box::new(groundgravitation::SkillGroundGravitation)),
        HN_HELLS_DRIVE => Some(Box::new(hellsdrive::SkillHellsDrive)),
        HN_JACK_FROST_NOVA => Some(Box::new(jackfrostnova::SkillJackFrostNova)),
        HN_JUPITEL_THUNDER_STORM => Some(Box::new(jupitelthunderstorm::SkillJupitelThunderstorm)),
        HN_MEGA_SONIC_BLOW => Some(Box::new(megasonicblow::SkillMegaSonicBlow)),
        HN_METEOR_STORM_BUSTER => Some(Box::new(meteorstormbuster::SkillMeteorStormBuster)),
        HN_NAPALM_VULCAN_STRIKE => Some(Box::new(napalmvulcanstrike::SkillNapalmVulcanStrike)),
        HN_OVERCOMING_CRISIS => Some(Box::new(overcomingcrisis::SkillOvercomingCrisis)),
        HN_RULEBREAK => Some(Box::new(StatusSkillImpl::new(skill_id))),
        HN_SHIELD_CHAIN_RUSH => Some(Box::new(shieldchainrush::SkillShieldChainRush)),
        HN_SPIRAL_PIERCE_MAX => Some(Box::new(spiralpiercemax::SkillSpiralPierceMax)),
        NV_FIRSTAID => Some(Box::new(firstaid::SkillFirstAid)),
        NV_HELPANGEL => Some(Box::new(helpangel::SkillHelpAngel)),
        NV_TRICKDEAD => Some(Box::new(StatusSkillImpl::new_toggle(skill_id, true))),
        _ => None,
    }
}

// ---------------------------------------------------------------------------------------------- shared helpers

/// config/const.hpp RE_LVL_DMOD(val) (RENEWAL_LVDMG is defined by config/renewal.hpp):
/// `if (status_get_lv(src) > 99 && val > 0) skillratio = skillratio * status_get_lv(src) / val;`
pub(crate) fn re_lvl_dmod(c: &Ctx, src: &Unit, skillratio: &mut i32, val: i32) {
    let lv = crate::status::status_get_lv(c, src);
    if lv > 99 && val > 0 {
        *skillratio = skillratio.wrapping_mul(lv) / val;
    }
}

/// pc.cpp pc_checkskill(BL_CAST(BL_PC, bl), skill_id): the uint8 level, 0 when `bl` is not a player (sd == nullptr).
pub(crate) fn pc_checkskill(c: &Ctx, bl: &Unit, skill_id: u16) -> i32 {
    if bl.kind != crate::map::BL_PC {
        return 0;
    }
    crate::skill::checkskill(c, bl.id, skill_id) as i32
}

/// `status_change* sc = status_get_sc(bl); sc ? sc->getSCE(type) : nullptr` (the entry as it is now).
pub(crate) fn sce(c: &Ctx, bl: &Unit, t: i32) -> Option<ScEntry> {
    if !crate::battle::mobif::has_sc(bl.kind) {
        return None;
    }
    crate::status::sc::get_sce(c, bl.id, t)
}

/// status.hpp status_get_amotion(bl).
pub(crate) fn status_get_amotion(c: &Ctx, bl: &Unit) -> i32 {
    crate::status::status_get_status_data(c, bl).amotion as i32
}

/// party.cpp party_foreachsamemap(skill_area_sub, sd, range, src, skill_id, skill_lv, tick, flag, func): the online
/// party members on sd's map (within `range`; 0 = the whole map), collected first (party::party_foreachsamemap), then
/// skill_area_sub on each (read fresh); returns the sum.
#[allow(clippy::too_many_arguments)]
pub(crate) fn party_foreachsamemap_area_sub(c: &Ctx, sd: &Unit, range: i16, src: &Unit, skill_id: u16, skill_lv: u16, tick: Tick, flag: i32, func: SkillFunc) -> i32 {
    let list = crate::party::party_foreachsamemap(c, sd, range);
    let mut total = 0;
    for u in list {
        let Some(u) = crate::unit::get(c, u.id) else { continue };
        total += crate::skill::area::skill_area_sub(c, &u, src, skill_id, skill_lv, tick, flag, func);
    }
    total
}

/// skill_impl.cpp SkillImplRecursiveDamageSplash::splashDamage (the parent body an override calls):
/// `skill_attack(skill_get_type(getSkillId()), src, src, target, getSkillId(), skill_lv, tick, flag)`.
pub(crate) fn splash_damage_default(c: &Ctx, skill_id: u16, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) -> i64 {
    crate::skill::attack::skill_attack(c, skill_get_type(skill_id), src, src, target, skill_id, skill_lv, tick, flag)
}

/// `skill_get_time(id, lv) / skill_get_unit_interval(id)` / `skill_get_time / skill_get_time2`: the number of
/// timerskills the ground skills queue. rAthena divides by the database value, never 0 for these skills; a zero
/// divisor (a broken database) queues none here instead of crashing.
pub(crate) fn repeat_count(time: i32, interval: i32) -> i32 {
    time.checked_div(interval).unwrap_or(0)
}
