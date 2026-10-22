//! skills/thief/abysssquare.cpp: class SkillAbyssSquare : public SkillImpl (ABC_ABYSS_SQUARE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface::skill_unitsetting;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillAbyssSquare;

impl SkillImpl for SkillAbyssSquare {
    fn skill_id(&self) -> u16 {
        ABC_ABYSS_SQUARE
    }

    /// SkillAbyssSquare::modifyDamageData: two hits for the unit's second damage type (miscflag 2).
    fn modify_damage_data(&self, _c: &Ctx, dmg: &mut Damage, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        if dmg.miscflag == 2 {
            dmg.div_ = 2;
        }
    }

    /// SkillAbyssSquare::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; //Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }

    /// SkillAbyssSquare::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 900 * lv;
        *skillratio += 50 * pc_checkskill(c, src, ABC_MAGIC_SWORD_M) * lv;
        *skillratio += 5 * sstatus.spl as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
