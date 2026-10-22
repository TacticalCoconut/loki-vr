//! skills/mage/earthgrave.cpp: class SkillEarthGrave : public SkillImpl (SO_EARTHGRAVE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::iface;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::{SC_BLEEDING, SC_CURSED_SOIL_OPTION};
use crate::status::{sc, status_get_status_data};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillEarthGrave;

impl SkillImpl for SkillEarthGrave {
    fn skill_id(&self) -> u16 {
        SO_EARTHGRAVE
    }

    /// SkillEarthGrave::applyAdditionalEffects: SC_BLEEDING at 5 * lv percent (val2 = the caster) for
    /// skill_get_time2.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let lv = skill_lv as i32;
        // Need official rate. [LimitLine]
        sc::sc_start2(c, Some(src), target, SC_BLEEDING, 5 * lv, lv, src.id as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillEarthGrave::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let int_ = status_get_status_data(c, src).int_ as i32;

        *skillratio += -100 + 2 * int_ + 300 * pc_checkskill(c, src, SA_SEISMICWEAPON) + int_ * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if sce(c, src, SC_CURSED_SOIL_OPTION).is_some() {
            // sd ? sd->status.job_level * 5 : 0
            *skillratio += if src.kind == BL_PC { crate::pc::character_of(c, src.id).map_or(0, |ch| ch.job_level as i32 * 5) } else { 0 };
        }
    }

    /// SkillEarthGrave::castendPos2: the skill's unit group.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
