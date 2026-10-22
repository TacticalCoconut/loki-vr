//! skills/mage/conflagration.cpp: class SkillConflagration : public SkillImpl (EM_CONFLAGRATION), rAthena e985006.
//!
//! The cast places the skill's unit group (skill_unitsetting; skillunit agent), whose hits go through skill_attack,
//! which asks the class for the skill ratio and the additional effect (SC_HANDICAPSTATE_CONFLAGRATION).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::{SC_HANDICAPSTATE_CONFLAGRATION, SC_SUMMON_ELEMENTAL_ARDOR};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillConflagration;

impl SkillImpl for SkillConflagration {
    fn skill_id(&self) -> u16 {
        EM_CONFLAGRATION
    }

    /// SkillConflagration::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_HANDICAPSTATE_CONFLAGRATION, 3, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillConflagration::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 700 + 1100 * lv;
        *skillratio += 5 * sstatus.spl as i32;

        if sce(c, src, SC_SUMMON_ELEMENTAL_ARDOR).is_some() {
            *skillratio += 200 * lv;
            *skillratio += 2 * sstatus.spl as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillConflagration::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
