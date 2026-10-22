//! skills/summoner/hyunrokbreeze.cpp: class SkillHyunrokBreeze : public SkillImpl (SH_HYUN_ROKS_BREEZE).

use super::{colors_of_hyun_rok_element, communes};
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHyunrokBreeze;

impl SkillImpl for SkillHyunrokBreeze {
    fn skill_id(&self) -> u16 {
        SH_HYUN_ROKS_BREEZE
    }

    /// SkillHyunrokBreeze::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 650 + 750 * lv;
        *skillratio += 20 * pc_checkskill(c, src, SH_MYSTICAL_CREATURE_MASTERY);
        *skillratio += 5 * sstatus.spl as i32;
        if communes(c, src, SH_COMMUNE_WITH_HYUN_ROK) {
            *skillratio += 100 + 200 * lv;
            *skillratio += 20 * pc_checkskill(c, src, SH_MYSTICAL_CREATURE_MASTERY);
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillHyunrokBreeze::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }

    /// SkillHyunrokBreeze::modifyElement.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        colors_of_hyun_rok_element(c, src, element);
    }
}
