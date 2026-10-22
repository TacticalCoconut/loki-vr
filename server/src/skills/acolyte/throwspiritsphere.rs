//! skills/acolyte/throwspiritsphere.cpp: class SkillThrowSpiritSphere : public WeaponSkillImpl (MO_FINGEROFFENSIVE).

use crate::battle::calc::Damage;
use crate::battle::BF_WEAPON;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skills::novice::sce;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_BLADESTOP;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

use super::conf::finger_offensive_type;

pub struct SkillThrowSpiritSphere;

impl SkillImpl for SkillThrowSpiritSphere {
    fn skill_id(&self) -> u16 {
        MO_FINGEROFFENSIVE
    }

    /// SkillThrowSpiritSphere::modifyDamageData: one hit per packet with finger_offensive_type (the pre-renewal cap
    /// of the hits by the spheres is `#ifndef RENEWAL`).
    fn modify_damage_data(&self, _c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if src.kind == BL_PC && finger_offensive_type() != 0 {
            dmg.div_ = 1;
        }
    }

    /// SkillThrowSpiritSphere::castendDamageId: WeaponSkillImpl::castendDamageId; with finger_offensive_type one more
    /// hit per sphere used, 200 ms apart (skill_timerskill); Blade Stop ends.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        weapon_castend_damage_id(c, id, src, target, skill_lv, tick, *flag);
        if finger_offensive_type() != 0 && src.kind == BL_PC {
            let old = crate::skill::state::get(c, src.id).spiritball_old as i32;
            for i in 1..old {
                skill_addtimerskill(c, src, tick + i as i64 * 200, target.id, 0, 0, id, skill_lv, BF_WEAPON, *flag);
            }
        }
        sc::status_change_end(c, src.id, SC_BLADESTOP, 0);
    }

    /// SkillThrowSpiritSphere::calculateSkillRatio (RENEWAL): +500% + 200% per level; +50% on a Blade Stopped target.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, _src: &Unit, target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 500 + skill_lv as i32 * 200;
        if sce(c, target, SC_BLADESTOP).is_some() {
            *base_skillratio += *base_skillratio / 2;
        }
    }
}
