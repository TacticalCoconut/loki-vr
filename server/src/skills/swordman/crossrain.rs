//! skills/swordman/crossrain.cpp: class SkillCrossRain : public SkillImpl (IG_CROSS_RAIN).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::SC_HOLY_S;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillCrossRain;

impl SkillImpl for SkillCrossRain {
    fn skill_id(&self) -> u16 {
        IG_CROSS_RAIN
    }

    /// SkillCrossRain::castendPos2: the skill's unit group (skillunit agent).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }

    /// SkillCrossRain::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        if sce(c, src, SC_HOLY_S).is_some() {
            *skillratio += -100 + (650 + 15 * pc_checkskill(c, src, IG_SPEAR_SWORD_M)) * lv;
        } else {
            *skillratio += -100 + (450 + 10 * pc_checkskill(c, src, IG_SPEAR_SWORD_M)) * lv;
        }
        *skillratio += 7 * sstatus.spl as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
