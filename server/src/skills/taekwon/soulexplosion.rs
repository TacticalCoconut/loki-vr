//! skills/taekwon/soulexplosion.cpp: class SkillSoulExplosion : public SkillImpl (SP_SOULEXPLOSION).

use crate::battle::BF_MISC;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::attack::skill_attack;
use crate::skill::clif::skill_fail;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_SOULFAIRY, SC_SOULFALCON, SC_SOULGOLEM, SC_SOULSHADOW, SC_SPIRIT};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSoulExplosion;

impl SkillImpl for SkillSoulExplosion {
    fn skill_id(&self) -> u16 {
        SP_SOULEXPLOSION
    }

    /// SkillSoulExplosion::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, _src: &Unit, target: &Unit, _skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        // Remove soul link when hit.
        for t in [SC_SPIRIT, SC_SOULGOLEM, SC_SOULSHADOW, SC_SOULFALCON, SC_SOULFAIRY] {
            sc::status_change_end(c, target.id, t, 0);
        }
    }

    /// SkillSoulExplosion::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let tstatus = crate::status::status_get_status_data(c, target);
        let linked = [SC_SPIRIT, SC_SOULGOLEM, SC_SOULSHADOW, SC_SOULFALCON, SC_SOULFAIRY].iter().any(|&t| sce(c, target, t).is_some());

        // tstatus->hp < 10 * tstatus->max_hp / 100: uint32 arithmetic
        if !linked || tstatus.hp < 10u32.wrapping_mul(tstatus.max_hp) / 100 {
            // Requires target to have a soul link and more then 10% of MaxHP.
            // With this skill requiring a soul link, and the target to have more then 10% if MaxHP, I wonder
            // if the cooldown still happens after it fails. Need a confirm. [Rytech]
            if src.kind == BL_PC {
                skill_fail(c, src, id);
            }
            return;
        }

        skill_attack(c, BF_MISC, src, src, target, id, skill_lv, tick, *flag);
    }
}
