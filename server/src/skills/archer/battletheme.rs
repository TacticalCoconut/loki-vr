//! skills/archer/battletheme.cpp: class SkillBattleTheme : public SkillImpl (BD_DRUMBATTLEFIELD). A song: Renewal's are instant (skill_castend_song).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillBattleTheme;

impl SkillImpl for SkillBattleTheme {
    fn skill_id(&self) -> u16 {
        BD_DRUMBATTLEFIELD
    }

    /// SkillBattleTheme::castendNoDamageId: `#ifdef RENEWAL skill_castend_song(src, getSkillId(), skill_lv, tick); #endif`.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        super::song_castend_nodamage(c, self.skill_id(), src, skill_lv, tick);
    }

    /// SkillBattleTheme::castendPos2: its whole body is `#ifndef RENEWAL` (the pre-Renewal song unit): empty in this build.
    fn castend_pos2(&self, _c: &Ctx, _src: &Unit, _x: i16, _y: i16, _skill_lv: u16, _tick: Tick, _flag: &mut i32) {}
}
