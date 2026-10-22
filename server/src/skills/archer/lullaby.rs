//! skills/archer/lullaby.cpp: class SkillLullaby : public SkillImpl (BD_LULLABY). An ensemble: Renewal's are instant
//! (skill_castend_song; its hit is skill_apply_songs' skill_additional_effect).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillLullaby;

impl SkillImpl for SkillLullaby {
    fn skill_id(&self) -> u16 {
        BD_LULLABY
    }

    /// SkillLullaby::applyAdditionalEffects: "In renewal the chance is simply 100% and uses the original song duration
    /// as sleep duration": `sc_start(src, target, skill_get_sc(getSkillId()), 100, skill_lv, skill_get_time(…))`.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
    }

    /// SkillLullaby::castendNoDamageId: `#ifdef RENEWAL skill_castend_song(src, getSkillId(), skill_lv, tick); #endif`.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        super::song_castend_nodamage(c, self.skill_id(), src, skill_lv, tick);
    }

    /// SkillLullaby::castendPos2: its whole body is `#ifndef RENEWAL` (the pre-Renewal song unit): empty here.
    fn castend_pos2(&self, _c: &Ctx, _src: &Unit, _x: i16, _y: i16, _skill_lv: u16, _tick: Tick, _flag: &mut i32) {}
}
