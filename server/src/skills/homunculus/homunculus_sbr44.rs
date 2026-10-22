//! skills/homunculus/homunculus_sbr44.cpp: class SkillSBR44 : public WeaponSkillImpl (HFLI_SBR44). Its damage is the
//! homunculus' intimacy (battle.cpp battle_calc_skill_base_damage's HFLI_SBR44 arm: battle/calc.rs).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::homunculus::data::{HOMGRADE_HATE_WITH_PASSION, SP_INTIMATE};
use crate::map::BL_HOM;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSBR44;

impl SkillImpl for SkillSBR44 {
    fn skill_id(&self) -> u16 {
        HFLI_SBR44
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillSBR44::applyCounterAdditionalEffects: the homunculus' intimacy falls to "hate with passion".
    fn apply_counter_additional_effects(&self, c: &Ctx, src: &Unit, _target: &Unit, _skill_lv: u16, _tick: Tick, _attack_type: &mut i32) {
        if src.kind == BL_HOM {
            crate::homunculus::data::with(c, src.id, |hd| hd.homunculus.intimacy = crate::homunculus::hom_intimacy_grade2intimacy(HOMGRADE_HATE_WITH_PASSION));
            crate::homunculus::clif::clif_send_homdata(c, src.id, SP_INTIMATE);
        }
    }

    /// SkillSBR44::calculateSkillRatio: + 100 * (lv - 1).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 100 * (skill_lv as i32 - 1);
    }
}
