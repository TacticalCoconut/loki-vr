//! skills/swordman/imperialcross.cpp: class SkillImperialCross : public WeaponSkillImpl (IG_IMPERIAL_CROSS).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_SPEAR_SCAR;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillImperialCross;

impl SkillImpl for SkillImperialCross {
    fn skill_id(&self) -> u16 {
        IG_IMPERIAL_CROSS
    }

    /// SkillImperialCross::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillImperialCross::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 1650 + 1350 * lv;
        *skillratio += pc_checkskill(c, src, IG_SPEAR_SWORD_M) * 25;
        *skillratio += 5 * sstatus.pow as i32; // !TODO: check POW ratio
        if sce(c, src, SC_SPEAR_SCAR).is_some() {
            *skillratio += 100 + 300 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
