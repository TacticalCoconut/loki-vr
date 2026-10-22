//! skills/taekwon/turnkick.cpp: class SkillTurnKick : public SkillImpl (TK_TURNKICK).

use crate::battle::calc::Damage;
use crate::battle::{ATK_DEF, BCT_ENEMY, BF_MISC, BF_WEAPON};
use crate::db::skills::*;
use crate::map::BL_MOB;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::attack::{skill_attack, skill_blown, BLOWN_NONE};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::effect::skill_additional_effect;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_NOACTION, SC_STUN};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// script.hpp e_effect EF_SPINEDBODY.
pub(crate) const EF_SPINEDBODY: i32 = 414;

pub struct SkillTurnKick;

impl SkillImpl for SkillTurnKick {
    fn skill_id(&self) -> u16 {
        TK_TURNKICK
    }

    /// SkillTurnKick::modifyDamageData.
    fn modify_damage_data(&self, _c: &Ctx, dmg: &mut Damage, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        dmg.blewcount = 0;
    }

    /// SkillTurnKick::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 90 + 30 * skill_lv as i32;
    }

    /// SkillTurnKick::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        // Note: attack_type is passed as BF_WEAPON for the actual target, BF_MISC for the splash-affected mobs.
        if attack_type & BF_MISC != 0 {
            sc::sc_start(c, Some(src), target, SC_STUN, 200, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
            let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
            crate::clif::specialeffect(c, &t, EF_SPINEDBODY, crate::clif::Target::Area);
            sc::sc_start(c, Some(src), &t, SC_NOACTION, 100, 1, skill_get_time2(id, skill_lv) as i64);
        }
    }

    /// SkillTurnKick::castendDamageId: active part of the attack.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // Note: skill_area_temp[1] is used in castendNoDamageId to avoid affecting the target.
        area::set_temp(1, target.id as i32);
        if skill_attack(c, BF_WEAPON, src, src, target, id, skill_lv, tick, *flag) != 0 {
            let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = crate::unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
            let f = *flag | BCT_ENEMY | 1;
            foreach_in_range(c, &t, skill_get_splash(id, skill_lv) as i16, BL_MOB, Wall::None, |c, bl| {
                area::skill_area_sub(c, bl, &s, id, skill_lv, tick, f, skill_castend_nodamage_id)
            });
        }
    }

    /// SkillTurnKick::castendNoDamageId: passive part of the attack. Splash knock-back+stun.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if area::temp(1) != target.id as i32 {
            skill_blown(c, src, target, skill_get_blewcount(id, skill_lv), -1, BLOWN_NONE);
            let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
            skill_additional_effect(c, &s, &t, id, skill_lv, BF_MISC, ATK_DEF, tick); // Use Misc rather than weapon to signal passive pushback
        }
    }
}

#[cfg(test)]
mod tests {
    #[test]
    fn effect_ids() {
        assert_eq!(crate::data::constant("EF_SPINEDBODY"), Some(super::EF_SPINEDBODY as i64));
    }
}
