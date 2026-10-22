//! skills/acolyte/effligo.cpp: class SkillEffligo : public WeaponSkillImpl (CD_EFFLIGO).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::defs::{RC_DEMON, RC_UNDEAD};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillEffligo;

impl SkillImpl for SkillEffligo {
    fn skill_id(&self) -> u16 {
        CD_EFFLIGO
    }

    /// SkillEffligo::castendDamageId: the animation, then WeaponSkillImpl::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        weapon_castend_damage_id(c, id, src, &t, skill_lv, tick, *flag);
    }

    /// SkillEffligo::calculateSkillRatio: 1800% per level + 7 x POW + 8% per Mace & Book Mastery level; more on
    /// undead and demons (RE_LVL_DMOD 100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let tstatus = crate::status::status_get_status_data(c, target);
        *skillratio += -100 + 1800 * skill_lv as i32;
        *skillratio += 7 * sstatus.pow as i32;
        *skillratio += 8 * pc_checkskill(c, src, CD_MACE_BOOK_M);
        if tstatus.race as i32 == RC_UNDEAD || tstatus.race as i32 == RC_DEMON {
            *skillratio += 200 * skill_lv as i32;
            *skillratio += 7 * pc_checkskill(c, src, CD_MACE_BOOK_M);
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
