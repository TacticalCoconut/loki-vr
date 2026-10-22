//! skills/npc/widebleeding.cpp: class SkillWideBleeding : public SkillImpl (NPC_WIDEBLEEDING), and the body the
//! monsters' area status skills share: widebleeding2, wideconfusion(2), widecurse(2), widefreeze(2), widesilence(2),
//! widesleep(2), widestun(2), wideweb and slowcast.cpp have widebleeding.cpp's castendNoDamageId statement for
//! statement ([`wide_status_castend`]); widepetrify(2), wideleash and widesouldrain.cpp share its `else` branch
//! ([`wide_area`]) and differ in the `flag & 1` one.

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_PREAMBLE;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWideBleeding;

impl SkillImpl for SkillWideBleeding {
    fn skill_id(&self) -> u16 {
        NPC_WIDEBLEEDING
    }

    /// SkillWideBleeding::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        wide_status_castend(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }
}

/// SkillWideBleeding::castendNoDamageId (and its copies): `flag & 1` (a target of the area):
/// `sc_start2(src, target, skill_get_sc(skill), 100, skill_lv, src->id, skill_get_time2(…))`; else the area
/// ([`wide_area`]).
pub(super) fn wide_status_castend(c: &Ctx, skill_id: u16, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
    if flag & 1 != 0 {
        sc::sc_start2(c, Some(src), target, skill_get_sc(skill_id) as i32, 100, skill_lv as i32, src.id as i32, skill_get_time2(skill_id, skill_lv) as i64);
    } else {
        wide_area(c, skill_id, src, target, skill_lv, tick, flag);
    }
}

/// The `else` branch of the NPC_WIDE* castendNoDamageId: the animation on the target, then
/// `map_foreachinallrange(skill_area_sub, target, skill_get_splash(…), BL_CHAR, src, skill_id, skill_lv, tick,
/// flag|BCT_ENEMY|SD_PREAMBLE|1, skill_castend_nodamage_id)` (no wall check; the first target gets the dummy damage
/// packet).
pub(super) fn wide_area(c: &Ctx, skill_id: u16, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
    area::set_temp(2, 0); //For SD_PREAMBLE
    clif_skill_nodamage(c, Some(src), target, skill_id, skill_lv as i32, true);
    let f = flag | BCT_ENEMY | SD_PREAMBLE | 1;
    foreach_in_range(c, target, skill_get_splash(skill_id, skill_lv) as i16, BL_CHAR, Wall::None, |c, bl| {
        area::skill_area_sub(c, bl, src, skill_id, skill_lv, tick, f, skill_castend_nodamage_id)
    });
}
