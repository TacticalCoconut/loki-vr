//! skills/archer/hipshaker.cpp: class SkillHipShaker : public SkillImpl (DC_UGLYDANCE). A song: Renewal's are
//! instant (skill_castend_song; its hit is skill_apply_songs' skill_additional_effect).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHipShaker;

impl SkillImpl for SkillHipShaker {
    fn skill_id(&self) -> u16 {
        DC_UGLYDANCE
    }

    /// SkillHipShaker::applyAdditionalEffects: Renewal `status_zap(target, 0, 2 * skill_lv + 10)` (!TODO: How does
    /// caster's DEX/AGI play a role?).
    fn apply_additional_effects(&self, c: &Ctx, _src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        crate::status::hp::status_zap(c, target.id, 0, 2 * skill_lv as i64 + 10, 0);
    }

    /// SkillHipShaker::castendNoDamageId: `#ifdef RENEWAL skill_castend_song(src, getSkillId(), skill_lv, tick); #endif`.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        super::song_castend_nodamage(c, self.skill_id(), src, skill_lv, tick);
    }

    /// SkillHipShaker::castendPos2: its whole body is `#ifndef RENEWAL` (the pre-Renewal song unit): empty here.
    fn castend_pos2(&self, _c: &Ctx, _src: &Unit, _x: i16, _y: i16, _skill_lv: u16, _tick: Tick, _flag: &mut i32) {}
}
