//! skills/novice/spiralpiercemax.cpp: class SkillSpiralPierceMax : public WeaponSkillImpl (HN_SPIRAL_PIERCE_MAX).

use super::{pc_checkskill, re_lvl_dmod};
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::defs::{SZ_BIG, SZ_MEDIUM, SZ_SMALL};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSpiralPierceMax;

impl SkillImpl for SkillSpiralPierceMax {
    fn skill_id(&self) -> u16 {
        HN_SPIRAL_PIERCE_MAX
    }

    /// SkillSpiralPierceMax::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 1000 + 1500 * lv;
        *skillratio += pc_checkskill(c, src, HN_SELFSTUDY_TATICS) * 3 * lv;
        *skillratio += 5 * sstatus.pow as i32;
        // status_get_size(target)
        match crate::status::status_get_status_data(c, target).size as i32 {
            SZ_SMALL => *skillratio = *skillratio * 150 / 100,
            SZ_MEDIUM => *skillratio = *skillratio * 130 / 100,
            SZ_BIG => *skillratio = *skillratio * 120 / 100,
            _ => {}
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillSpiralPierceMax::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }
}
