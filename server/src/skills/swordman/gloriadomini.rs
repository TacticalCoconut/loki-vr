//! skills/swordman/gloriadomini.cpp: class SkillGloriaDomini : public SkillImpl (PA_PRESSURE).

use crate::battle::calc::Damage;
use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGloriaDomini;

impl SkillImpl for SkillGloriaDomini {
    fn skill_id(&self) -> u16 {
        PA_PRESSURE
    }

    /// SkillGloriaDomini::applyAdditionalEffects: its whole body is `#ifndef RENEWAL` (the SP damage and the
    /// physical autospell trigger of pre-renewal); in renewal it does nothing.
    fn apply_additional_effects(&self, _c: &Ctx, _src: &Unit, _target: &Unit, _skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {}

    /// SkillGloriaDomini::calculateSkillRatio (RENEWAL).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 500 + 150 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillGloriaDomini::castendDamageId (RENEWAL: a magic attack).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }
}
