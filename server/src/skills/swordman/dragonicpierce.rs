//! skills/swordman/dragonicpierce.cpp: class SkillDragonicPierce : public WeaponSkillImpl (DK_DRAGONIC_PIERCE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_DRAGONIC_AURA;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDragonicPierce;

impl SkillImpl for SkillDragonicPierce {
    fn skill_id(&self) -> u16 {
        DK_DRAGONIC_PIERCE
    }

    /// SkillDragonicPierce::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillDragonicPierce::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 900 + 730 * lv;
        *skillratio += 7 * sstatus.pow as i32; // !TODO: unknown ratio
        if sce(c, src, SC_DRAGONIC_AURA).is_some() {
            *skillratio += 200 + 50 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
