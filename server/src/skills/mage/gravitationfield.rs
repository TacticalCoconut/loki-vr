//! skills/mage/gravitationfield.cpp: class SkillGravitationField : public SkillImpl (HW_GRAVITATION). The hits
//! come from its skill unit (skillunit/ontimer.rs).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGravitationField;

impl SkillImpl for SkillGravitationField {
    fn skill_id(&self) -> u16 {
        HW_GRAVITATION
    }

    /// SkillGravitationField::calculateSkillRatio (RENEWAL branch; pre-renewal has no modifier).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 100 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillGravitationField::castendPos2 (RENEWAL branch; pre-renewal's SC_GRAVITATION on the caster is dropped).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; //Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }

    /// SkillGravitationField::applyAdditionalEffects: its whole body is `#ifndef RENEWAL` (Gravitation can trigger
    /// physical autospells: attack_type |= BF_NORMAL | BF_WEAPON on a by-value copy): nothing in Renewal.
    fn apply_additional_effects(&self, _c: &Ctx, _src: &Unit, _target: &Unit, _skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {}
}
