//! skills/gunslinger/flicker.cpp: class SkillFlicker : public SkillImpl (RL_FLICKER), and its static skill_bind_trap,
//! rAthena e985006.
//!
//! map_session_data::flicker (pc.hpp) is set to true for the duration of SkillFlicker::castendNoDamageId only (the
//! Howling Mines it sets off read it through skill_castend_damage_id). It never outlives that call, so it is kept
//! here as a flag of the module instance (a reducer runs to completion before another starts) instead of a table
//! column; `sd->flicker` of a player is `flicker(aid)`.

use std::cell::Cell;

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_PC, BL_SKILL};
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_SPLASH;
use crate::skills::novice::pc_checkskill;
use crate::skills::SkillImpl;
use crate::skillunit::consts::{UNT_B_TRAP, UNT_USED_TRAPS};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

thread_local! {
    /// The player whose `sd->flicker` is true (0: none).
    static FLICKER: Cell<u32> = const { Cell::new(0) };
}

/// `sd->flicker` of the player `aid`.
pub(crate) fn flicker(aid: u32) -> bool {
    aid != 0 && FLICKER.with(|f| f.get()) == aid
}

pub struct SkillFlicker;

impl SkillImpl for SkillFlicker {
    fn skill_id(&self) -> u16 {
        RL_FLICKER
    }

    /// SkillFlicker::castendNoDamageId: with sd->flicker set, the animation, then (when the player knows them) its
    /// Bind Traps in the area explode (skill_bind_trap) and its Howling Mines go off: skill_castend_damage_id of
    /// RL_H_MINE at the player's level on the enemies within Flicker's splash (flag without 1: SkillHowlingMine's
    /// Flicker path).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        if src.kind != BL_PC {
            return;
        }
        FLICKER.with(|f| f.set(src.id));
        area::set_temp(1, 0);
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        // Detonate RL_B_TRAP
        if pc_checkskill(c, src, RL_B_TRAP) != 0 {
            let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
            foreach_in_range(c, &s, crate::conf::area_size(), BL_SKILL, Wall::None, |c, bl| skill_bind_trap(c, bl, &s));
        }
        // Detonate RL_H_MINE
        let mine_lv = pc_checkskill(c, src, RL_H_MINE);
        if mine_lv != 0 {
            let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let f = *flag | BCT_ENEMY | SD_SPLASH;
            foreach_in_range(c, &s, skill_get_splash(self.skill_id(), skill_lv) as i16, BL_CHAR, Wall::None, |c, bl| {
                area::skill_area_sub(c, bl, &s, RL_H_MINE, mine_lv as u16, tick, f, skill_castend_damage_id)
            });
        }
        FLICKER.with(|f| f.set(0));
    }
}

/// flicker.cpp skill_bind_trap (Rebellion's Bind Trap explosion [Cydh]): a Bind Trap unit of `src` hits the characters
/// within its range (skill_trap_splash), turns into a used trap (clif_changetraplook) and ends 500 ms from now.
fn skill_bind_trap(c: &Ctx, bl: &Unit, src: &Unit) -> i32 {
    if bl.kind != BL_SKILL {
        return 0;
    }
    let Some(su) = crate::skillunit::data::get_unit(c, bl.id) else { return 0 };
    let Some(group) = crate::skillunit::data::skill_id2group(c, su.group_id) else { return 0 };
    if group.unit_id != UNT_B_TRAP || group.src_id != src.id {
        return 0;
    }
    let group_tick = group.tick;
    foreach_in_range(c, bl, su.range, BL_CHAR, Wall::None, |c, t| crate::skillunit::ontimer::skill_trap_splash(c, t, bl.id, group_tick));
    crate::skillunit::clif::clif_changetraplook(c, bl, UNT_USED_TRAPS);
    crate::skillunit::data::upd_group(c, group.group_id, |g| {
        g.unit_id = UNT_USED_TRAPS;
        g.limit = c.tick() - g.tick + 500;
    });
    1
}
