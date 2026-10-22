//! skills/acolyte/ragingquadrupleblow.cpp: class SkillRagingQuadrupleBlow : public WeaponSkillImpl (MO_CHAINCOMBO).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_BLADESTOP;
use crate::status::defs::W_KNUCKLE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRagingQuadrupleBlow;

/// `sd != nullptr && sd->status.weapon == W_KNUCKLE`.
fn knuckle(c: &Ctx, src: &Unit) -> bool {
    src.kind == BL_PC && crate::pc::character_of(c, src.id).is_some_and(|ch| ch.weapon as i32 == W_KNUCKLE)
}

impl SkillImpl for SkillRagingQuadrupleBlow {
    fn skill_id(&self) -> u16 {
        MO_CHAINCOMBO
    }

    /// SkillRagingQuadrupleBlow::modifyDamageData (RENEWAL): 6 hits shown as one (div -6) with a knuckle.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if knuckle(c, src) {
            dmg.div_ = -6;
        }
    }

    /// SkillRagingQuadrupleBlow::castendDamageId: WeaponSkillImpl::castendDamageId, then Blade Stop ends.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
        sc::status_change_end(c, src.id, SC_BLADESTOP, 0);
    }

    /// SkillRagingQuadrupleBlow::calculateSkillRatio (RENEWAL): +150% + 50% per level, doubled with a knuckle.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 150 + 50 * skill_lv as i32;
        if knuckle(c, src) {
            *base_skillratio *= 2;
        }
    }
}
