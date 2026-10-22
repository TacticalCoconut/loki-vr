//! skills/gunslinger/roundtrip.cpp: class SkillRoundTrip : public SkillImplRecursiveDamageSplash (RL_R_TRIP), class
//! SkillRoundTripPlusAttack : public SkillImpl (RL_R_TRIP_PLUSATK), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

// RL_R_TRIP
pub struct SkillRoundTrip;

impl SkillImpl for SkillRoundTrip {
    fn skill_id(&self) -> u16 {
        RL_R_TRIP
    }

    crate::splash_skill_defaults!();

    /// SkillRoundTrip::calculateSkillRatio: 350% per level, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 350 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillRoundTrip::castendNoDamageId: the animation, then skill_castend_damage_id (the splash; the second shot is
    /// skill_attack_blow's RL_R_TRIP_PLUSATK timerskill).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        skill_castend_damage_id(c, src, target, id, skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillRoundTrip {}

// RL_R_TRIP_PLUSATK
pub struct SkillRoundTripPlusAttack;

impl SkillImpl for SkillRoundTripPlusAttack {
    fn skill_id(&self) -> u16 {
        RL_R_TRIP_PLUSATK
    }

    /// SkillRoundTripPlusAttack::calculateSkillRatio: 300 + 300% per level.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -100 + 300 + 300 * skill_lv as i32;
    }
}
