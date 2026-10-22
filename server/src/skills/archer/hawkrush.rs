//! skills/archer/hawkrush.cpp: class SkillHawkRush : public WeaponSkillImpl (WH_HAWKRUSH).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHawkRush;

impl SkillImpl for SkillHawkRush {
    fn skill_id(&self) -> u16 {
        WH_HAWKRUSH
    }

    /// SkillHawkRush::calculateSkillRatio: -100 + 500 * lv + 5 * CON, + 10% per Nature Friendly level,
    /// RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 500 * skill_lv as i32 + 5 * sstatus.con as i32;
        if src.kind == crate::map::BL_PC {
            *skillratio += *skillratio * pc_checkskill(c, src, WH_NATUREFRIENDLY) / 10;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillHawkRush::castendDamageId: the no-damage packet, then WeaponSkillImpl::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        weapon_castend_damage_id(c, self.skill_id(), src, &super::fresh(c, target), skill_lv, tick, *flag);
    }
}
