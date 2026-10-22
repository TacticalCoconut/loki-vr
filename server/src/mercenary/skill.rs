//! The mercenary parts of skill.cpp and clif.cpp's skill requests: skill_blockmerc_start / skill_blockmerc_end /
//! skill_blockmerc_clear (md->scd, the mercenary's cooldowns), skill_isNotOk_mercenary, and
//! clif_parse_UseSkillToId_mercenary / clif_parse_UseSkillToPos_mercenary. (skill_check_condition_mercenary, shared
//! with the homunculi, is skill/homun.rs'.)

use super::data::{self, MercScd};
use crate::db::skills::{skill_db, skill_get_inf, INF_SELF_SKILL};
use crate::skill::clif::fail::*;
use crate::timer::{self, Func};
use crate::unit;
use crate::world::{Ctx, Tick};

/// skill.cpp skill_blockmerc_end (Func::SkillBlockmercEnd; target = the mercenary, data = skill id): the cooldown entry
/// goes.
pub fn skill_blockmerc_end(c: &Ctx, _tid: u64, id: u32, data: i64) -> i32 {
    let Some(mut md) = data::get(c, id) else { return 0 };
    let key = data as u16;
    md.scd.retain(|e| e.skill_id != key);
    let mut m = md.scd_umap();
    m.erase(key);
    md.set_scd_umap(m);
    data::put(c, &md);
    1
}

/// skill.cpp skill_blockmerc_start: blocks the mercenary's skill for `tick` ms (and shows the master the cooldown).
pub fn skill_blockmerc_start(c: &Ctx, id: u32, skill_id: u16, tick: Tick) -> bool {
    if !skill_db().exists(skill_id) || tick < 1 {
        return false;
    }
    let Some(mut md) = data::get(c, id) else { return false };
    if md.scd.len() == crate::int_mercenary::MAX_SKILLCOOLDOWN {
        log::warn!("skill_blockpc_start: Too many skillcooldowns, increase MAX_SKILLCOOLDOWN.");
        return false;
    }
    // Add entry to list. (`mc.scd[skill_id] = add_timer(…)`: an existing entry's timer is replaced; the old one keeps
    // running and its end erases the entry — as in rAthena)
    let t = timer::add(c, c.tick() + tick, Func::SkillBlockmercEnd, id, skill_id as i64);
    match md.scd.iter_mut().find(|e| e.skill_id == skill_id) {
        Some(e) => e.timer = t,
        None => {
            md.scd.push(MercScd { skill_id, timer: t });
            let mut m = md.scd_umap();
            m.insert(skill_id);
            md.set_scd_umap(m);
        }
    }
    data::put(c, &md);
    if crate::conf::display_status_timers() {
        if let Some(sd) = unit::get(c, md.master) {
            crate::skill::clif::clif_skill_cooldown(c, &sd, skill_id, tick);
        }
    }
    true
}

/// skill.cpp skill_blockmerc_clear: every cooldown of the mercenary ends (icons cleared at the master).
pub fn skill_blockmerc_clear(c: &Ctx, id: u32) {
    let Some(mut md) = data::get(c, id) else { return };
    let sd = unit::get(c, md.master);
    for e in md.scd_entries() {
        if crate::conf::display_status_timers() {
            if let Some(sd) = &sd {
                crate::skill::clif::clif_skill_cooldown(c, sd, e.skill_id, 0);
            }
        }
        timer::delete_checked(c, e.timer, Func::SkillBlockmercEnd);
    }
    md.scd.clear();
    // mc.scd.clear(): the bucket count and the rehash policy stay
    let mut m = md.scd_umap();
    m.clear();
    md.set_scd_umap(m);
    data::put(c, &md);
}

/// skill.cpp skill_isNotOk_mercenary: a mercenary skill on cooldown fails (USESKILL_FAIL_SKILLINTERVAL); otherwise the
/// master's skill_isNotOk decides. True: cannot be used.
pub fn skill_is_not_ok_mercenary(c: &Ctx, skill_id: u16, id: u32) -> bool {
    let Some(md) = data::get(c, id) else { return true };
    let Some(sd) = unit::get(c, md.master) else { return true };
    if crate::pc::get(c, md.master).is_none() {
        return true;
    }
    if md.scd.iter().any(|e| e.skill_id == skill_id) {
        crate::skill::clif::skill_fail_cause(c, &sd, skill_id, USESKILL_FAIL_SKILLINTERVAL);
        return true;
    }
    crate::skill::condition::skill_is_not_ok(c, &sd, skill_id)
}

/// clif.cpp clif_parse_UseSkillToId_mercenary: the mercenary casts `skill_id` (at most its own level) on
/// `target_id` (itself for self skills); refused while it casts (but Cast Cancel / Spell Fist) or cannot act yet.
pub fn clif_parse_use_skill_to_id_mercenary(c: &Ctx, aid: u32, tick: Tick, skill_id: u16, skill_lv: u16, target_id: u32) {
    let Some(md) = data::of_master(c, aid) else { return };
    if skill_is_not_ok_mercenary(c, skill_id, md.id) {
        return;
    }
    let mut target_id = target_id;
    if md.id != target_id && skill_get_inf(skill_id) & INF_SELF_SKILL as i32 != 0 {
        target_id = md.id;
    }
    let Some(u) = unit::get(c, md.id) else { return };
    if u.ud.skilltimer != 0 {
        if skill_id != crate::db::skills::SA_CASTCANCEL && skill_id != crate::db::skills::SO_SPELLFIST {
            return;
        }
    } else if tick - u.ud.canact_tick < 0 {
        return;
    }
    let lv = super::mercenary_checkskill(&md, skill_id);
    let skill_lv = skill_lv.min(lv);
    if skill_lv != 0 {
        crate::skill::unit::unit_skilluse_id(c, md.id, target_id, skill_id, skill_lv);
    }
}

/// clif.cpp clif_parse_UseSkillToPos_mercenary: the mercenary casts a ground skill at (x, y); while it cannot act yet
/// the master gets USESKILL_FAIL_SKILLINTERVAL. (The pre-renewal Basilica check is not compiled.)
pub fn clif_parse_use_skill_to_pos_mercenary(c: &Ctx, aid: u32, tick: Tick, skill_id: u16, skill_lv: u16, x: i16, y: i16) {
    let Some(md) = data::of_master(c, aid) else { return };
    if skill_is_not_ok_mercenary(c, skill_id, md.id) {
        return;
    }
    let Some(u) = unit::get(c, md.id) else { return };
    if u.ud.skilltimer != 0 {
        return;
    }
    if tick - u.ud.canact_tick < 0 {
        if let Some(sd) = unit::get(c, md.master) {
            crate::skill::clif::skill_fail_cause(c, &sd, skill_id, USESKILL_FAIL_SKILLINTERVAL);
        }
        return;
    }
    let lv = super::mercenary_checkskill(&md, skill_id);
    let skill_lv = skill_lv.min(lv);
    if skill_lv != 0 {
        crate::skill::unit::unit_skilluse_pos(c, md.id, x, y, skill_id, skill_lv);
    }
}
