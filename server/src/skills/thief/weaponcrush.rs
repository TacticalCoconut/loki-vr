//! skills/thief/weaponcrush.cpp: class SkillWeaponCrush : public WeaponSkillImpl (GC_WEAPONCRUSH).

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::effect::skill_strip_equip;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillWeaponCrush;

impl SkillImpl for SkillWeaponCrush {
    fn skill_id(&self) -> u16 {
        GC_WEAPONCRUSH
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillWeaponCrush::castendNoDamageId: skill_strip_equip, the animation when something was stripped, a failure
    /// when nothing was.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let i = skill_strip_equip(c, src, target, id, skill_lv);
        if i {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, i);
        }
        //Nothing stripped.
        if src.kind == BL_PC && !i {
            skill_fail(c, src, id);
        }
    }

    /// SkillWeaponCrush::applyAdditionalEffects: the strip part (skill_castend_nodamage_id with BCT_ENEMY).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        crate::skill::castend::skill_castend_nodamage_id(c, src, target, self.skill_id(), skill_lv, tick, BCT_ENEMY);
    }
}
