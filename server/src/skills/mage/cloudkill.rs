//! skills/mage/cloudkill.cpp: class SkillCloudKill : public SkillImpl (SO_CLOUD_KILL).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::iface;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::{SC_CURSED_SOIL_OPTION, SC_DEEP_POISONING_OPTION};
use crate::status::{sc, status_get_status_data};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillCloudKill;

impl SkillImpl for SkillCloudKill {
    fn skill_id(&self) -> u16 {
        SO_CLOUD_KILL
    }

    /// SkillCloudKill::applyAdditionalEffects: the skill's status (skill_get_sc) at 100% for skill_get_time2.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
    }

    /// SkillCloudKill::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = status_get_status_data(c, src);

        *skillratio += -100 + 40 * skill_lv as i32;
        *skillratio += sstatus.int_ as i32 * 3;
        re_lvl_dmod(c, src, skillratio, 100);
        // if (sc) { … }
        if sce(c, src, SC_CURSED_SOIL_OPTION).is_some() {
            // sd ? sd->status.job_level : 0
            *skillratio += if src.kind == BL_PC { crate::pc::character_of(c, src.id).map_or(0, |ch| ch.job_level as i32) } else { 0 };
        }
        if sce(c, src, SC_DEEP_POISONING_OPTION).is_some() {
            *skillratio += *skillratio * 1500 / 100;
        }
    }

    /// SkillCloudKill::castendPos2: the skill's unit group (flag |= 4 first).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 4;
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
