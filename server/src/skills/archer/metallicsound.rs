//! skills/archer/metallicsound.cpp: class SkillMetallicSound : public SkillImpl (WM_METALICSOUND).

use crate::battle::calc::Damage;
use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::{SC_SLEEP, SC_SOUNDBLEND};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMetallicSound;

impl SkillImpl for SkillMetallicSound {
    fn skill_id(&self) -> u16 {
        WM_METALICSOUND
    }

    /// SkillMetallicSound::applyAdditionalEffects: the target's Sound Blend ends.
    fn apply_additional_effects(&self, c: &Ctx, _src: &Unit, target: &Unit, _skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::status_change_end(c, target.id, SC_SOUNDBLEND, 0);
    }

    /// SkillMetallicSound::calculateSkillRatio: -100 + 120 * lv + 60 * Lesson (1 for a non-player), + 100 on a
    /// sleeping target, RE_LVL_DMOD(100), + 50% on a Sound Blend target.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let lesson = if src.kind == crate::map::BL_PC { pc_checkskill(c, src, WM_LESSON) } else { 1 };
        *skillratio += -100 + 120 * skill_lv as i32 + 60 * lesson;
        if sce(c, target, SC_SLEEP).is_some() {
            *skillratio += 100; // !TODO: Confirm target sleeping bonus
        }
        re_lvl_dmod(c, src, skillratio, 100);
        if sce(c, target, SC_SOUNDBLEND).is_some() {
            *skillratio += *skillratio * 50 / 100;
        }
    }

    /// SkillMetallicSound::castendDamageId: `skill_attack(BF_MAGIC, src, src, target, getSkillId(), skill_lv, tick, flag)`.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        crate::skill::attack::skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }
}
