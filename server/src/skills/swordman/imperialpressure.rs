//! skills/swordman/imperialpressure.cpp: class SkillImperialPressure : public SkillImplRecursiveDamageSplash
//! (IG_IMPERIAL_PRESSURE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_GUARD_STANCE;
use crate::status::defs::ELE_HOLY;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillImperialPressure;

impl SkillImpl for SkillImperialPressure {
    fn skill_id(&self) -> u16 {
        IG_IMPERIAL_PRESSURE
    }

    crate::splash_skill_defaults!();

    /// SkillImperialPressure::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 3750 + 2650 * skill_lv as i32;
        *skillratio += 7 * sstatus.spl as i32;
        *skillratio += 50 * pc_checkskill(c, src, IG_SPEAR_SWORD_M);
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillImperialPressure::modifyElement: holy in Guard Stance.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        if sce(c, src, SC_GUARD_STANCE).is_some() {
            *element = ELE_HOLY;
        }
    }
}

impl RecursiveDamageSplash for SkillImperialPressure {
    /// SkillImperialPressure::splashSearch.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
