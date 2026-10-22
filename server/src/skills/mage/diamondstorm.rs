//! skills/mage/diamondstorm.cpp: class SkillDiamondStorm : public SkillImpl (EM_DIAMOND_STORM), rAthena e985006.
//!
//! The cast places the skill's unit group (skill_unitsetting; skillunit agent), whose hits go through skill_attack,
//! which asks the class for the skill ratio and the additional effect (SC_HANDICAPSTATE_FROSTBITE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::{SC_HANDICAPSTATE_FROSTBITE, SC_SUMMON_ELEMENTAL_DILUVIO};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDiamondStorm;

impl SkillImpl for SkillDiamondStorm {
    fn skill_id(&self) -> u16 {
        EM_DIAMOND_STORM
    }

    /// SkillDiamondStorm::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_HANDICAPSTATE_FROSTBITE, 5, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillDiamondStorm::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        if sce(c, src, SC_SUMMON_ELEMENTAL_DILUVIO).is_some() {
            *skillratio += -100 + 8100 + 2700 * lv;
            *skillratio += 10 * sstatus.spl as i32;
        } else {
            *skillratio += -100 + 600 + 3000 * lv;
            *skillratio += 7 * sstatus.spl as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillDiamondStorm::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
