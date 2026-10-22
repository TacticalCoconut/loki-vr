//! skills/gunslinger/disarm.cpp: class SkillDisarm : public WeaponSkillImpl (GS_DISARM), rAthena e985006.

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::effect::skill_strip_equip;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDisarm;

impl SkillImpl for SkillDisarm {
    fn skill_id(&self) -> u16 {
        GS_DISARM
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillDisarm::applyAdditionalEffects: skill_strip_equip, then the animation.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        skill_strip_equip(c, src, target, id, skill_lv);
        let t = crate::unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, true);
    }
}
