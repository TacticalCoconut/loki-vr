//! skills/swordman/rageburst.cpp: class SkillRageBurst : public WeaponSkillImpl (LG_RAGEBURST).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRageBurst;

impl SkillImpl for SkillRageBurst {
    fn skill_id(&self) -> u16 {
        LG_RAGEBURST
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillRageBurst::calculateSkillRatio: the spheres the skill consumed (sd->spiritball_old) and the HP lost count.
    /// `(status_get_max_hp(src) - status_get_hp(src))` is uint32 arithmetic in rAthena.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lost = sstatus.max_hp.wrapping_sub(sstatus.hp);
        let spiritball_old = if src.kind == BL_PC { crate::skill::state::get(c, src.id).spiritball_old as i32 } else { 0 };
        if src.kind == BL_PC && spiritball_old != 0 {
            let add = ((-100 + 200 * spiritball_old) as u32).wrapping_add(lost / 100);
            *skillratio = (*skillratio as u32).wrapping_add(add) as i32;
        } else {
            let add = 2900u32.wrapping_add(lost);
            *skillratio = (*skillratio as u32).wrapping_add(add) as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
