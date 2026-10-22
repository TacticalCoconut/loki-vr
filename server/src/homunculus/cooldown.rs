//! skill.cpp skill_blockhomun_start / skill_blockhomun_end / skill_blockhomun_clear: a homunculus' skill cooldowns
//! (hd->scd, a std::unordered_map<uint16, int32> skill → timer, kept in the `hom_data` row with its iteration order:
//! skill/umap.rs).

use super::data::{get, put, HomScdTimer};
use crate::int_homun::MAX_SKILLCOOLDOWN;
use crate::timer::{self, Func};
use crate::world::{Ctx, Tick};

/// skill.cpp skill_blockhomun_end (Func::SkillBlockhomunEnd; target = the homunculus' block id, data = skill id).
pub fn skill_blockhomun_end(c: &Ctx, id: u32, data: i64) -> i32 {
    let Some(mut hd) = get(c, id) else { return 0 };
    let key = data as u16;
    hd.scd.retain(|e| e.skill_id != key);
    let mut m = hd.scd_map();
    m.erase(key);
    hd.set_scd_map(m);
    put(c, &hd);
    1
}

/// skill.cpp skill_blockhomun_start: blocks `skill_id` for `tick` ms (with the cooldown icon to the master when
/// display_status_timers is on). False when the skill does not exist, the tick is below 1 or 20 cooldowns run.
pub fn skill_blockhomun_start(c: &Ctx, hd_id: u32, skill_id: u16, tick: Tick) -> bool {
    if !crate::db::skills::skill_db().exists(skill_id) || tick < 1 {
        return false;
    }
    let Some(mut hd) = get(c, hd_id) else { return false };
    if hd.scd.len() == MAX_SKILLCOOLDOWN {
        log::warn!("skill_blockhomun_start: Too many skillcooldowns, increase MAX_SKILLCOOLDOWN.");
        return false;
    }
    // Add entry to list. (`hd.scd[skill_id] = add_timer(…)`: an existing entry's timer is replaced, the old one keeps
    // running and its end erases the new entry — as in rAthena)
    let t = timer::add(c, c.tick() + tick, Func::SkillBlockhomunEnd, hd_id, skill_id as i64);
    match hd.scd.iter_mut().find(|e| e.skill_id == skill_id) {
        Some(e) => e.timer = t,
        None => {
            hd.scd.push(HomScdTimer { skill_id, timer: t });
            let mut m = hd.scd_map();
            m.insert(skill_id);
            hd.set_scd_map(m);
        }
    }
    let master = hd.master;
    put(c, &hd);
    if crate::conf::display_status_timers() {
        if let Some(mu) = crate::unit::get(c, master) {
            crate::skill::clif::clif_skill_cooldown(c, &mu, skill_id, tick);
        }
    }
    true
}

/// skill.cpp skill_blockhomun_clear: every cooldown ends (the icon off for each when display_status_timers is on).
pub fn skill_blockhomun_clear(c: &Ctx, hd_id: u32) {
    let Some(mut hd) = get(c, hd_id) else { return };
    let mu = crate::unit::get(c, hd.master);
    for e in hd.scd_entries() {
        if crate::conf::display_status_timers() {
            if let Some(mu) = &mu {
                crate::skill::clif::clif_skill_cooldown(c, mu, e.skill_id, 0);
            }
        }
        timer::delete_checked(c, e.timer, Func::SkillBlockhomunEnd);
    }
    hd.scd.clear();
    let mut m = hd.scd_map();
    m.clear();
    hd.set_scd_map(m);
    put(c, &hd);
}

/// `util::umap_exists(hd->scd, skill_id)` (skill_isNotOk_hom, the cast checks).
pub fn is_blocked(c: &Ctx, hd_id: u32, skill_id: u16) -> bool {
    get(c, hd_id).map(|hd| hd.scd.iter().any(|e| e.skill_id == skill_id)).unwrap_or(false)
}
