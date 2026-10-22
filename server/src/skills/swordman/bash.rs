//! skills/swordman/bash.cpp: class SkillBash : public WeaponSkillImpl (SM_BASH).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::novice::pc_checkskill;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_STUN;
use crate::status::defs::SCSTART_NONE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillBash;

impl SkillImpl for SkillBash {
    fn skill_id(&self) -> u16 {
        SM_BASH
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillBash::calculateSkillRatio: base 100% + 30% per level.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 30 * skill_lv as i32;
    }

    /// SkillBash::modifyHitRate: it is proven that bonus is applied on final hitrate, not hit. +5% hit per level.
    fn modify_hit_rate(&self, _c: &Ctx, hit_rate: &mut i16, _src: &Unit, _target: &Unit, skill_lv: u16) {
        let h = *hit_rate as i32;
        *hit_rate = (h + h * 5 * skill_lv as i32 / 100) as i16;
    }

    /// SkillBash::applyAdditionalEffects: Fatal Blow's stun for levels above 5.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        // map_session_data* sd = BL_CAST(BL_PC, src)
        if src.kind != BL_PC {
            return;
        }
        let Some(ch) = crate::pc::character_of(c, src.id) else { return };
        if skill_lv > 5 && pc_checkskill(c, src, SM_FATALBLOW) > 0 {
            // BaseChance gets multiplied with BaseLevel/50.0; 500/50 simplifies to 10 [Playtester]
            let stun_chance = (skill_lv as i32 - 5) * ch.base_level as i32 * 10;
            sc::status_change_start(c, Some(src), target, SC_STUN, stun_chance, skill_lv as i32, 0, 0, 0, skill_get_time2(self.skill_id(), skill_lv) as i64, SCSTART_NONE, 0);
        }
    }
}
