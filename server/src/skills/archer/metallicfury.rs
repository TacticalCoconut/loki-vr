//! skills/archer/metallicfury.cpp: class SkillMetallicFury : public SkillImplRecursiveDamageSplash (TR_METALIC_FURY).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_SOUNDBLEND;
use crate::unit::Unit;
use crate::world::Ctx;

pub struct SkillMetallicFury;

impl SkillImpl for SkillMetallicFury {
    fn skill_id(&self) -> u16 {
        TR_METALIC_FURY
    }

    crate::splash_skill_defaults!();

    /// SkillMetallicFury::calculateSkillRatio: -100 + 3850 * lv; against a Sound Blend target + 800 * lv and
    /// 2 * Stage Manner * SPL; RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 3850 * skill_lv as i32;
        // !Todo: skill affected by SPL (without SC_SOUNDBLEND) as well?
        if sce(c, target, SC_SOUNDBLEND).is_some() {
            *skillratio += 800 * skill_lv as i32;
            *skillratio += 2 * pc_checkskill(c, src, TR_STAGE_MANNER) * sstatus.spl as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillMetallicFury::modifyElement: a player's arrow element.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        super::arrow_element(c, src, element);
    }
}

impl RecursiveDamageSplash for SkillMetallicFury {}
