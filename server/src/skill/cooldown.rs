//! skill.cpp skill_blockpc_start / skill_blockpc_end / skill_blockpc_clear (sd->scd, the skill cooldowns: one
//! `skill_scd` row per blocked skill, its timer Func::SkillBlockpcEnd) and the char server's `skillcooldown` table
//! (chrif.cpp chrif_skillcooldown_save at the quitting save, chrif_skillcooldown_request / load at login:
//! char_mapif.cpp chmapif_parse_req_saveskillcooldown / chmapif_parse_req_skillcooldown).
//! (skill_blockhomun_*: homunculus/cooldown.rs; skill_blockmerc_*: mercenary/skill.rs.)

use spacetimedb::{table, Table};

use crate::timer::{self, Func};
use crate::world::{Ctx, Tick};

/// mmo.hpp MAX_SKILLCOOLDOWN.
pub const MAX_SKILLCOOLDOWN: usize = 20;

/// sd->scd: skill id -> the skill_blockpc_end timer.
#[table(accessor = skill_scd, index(accessor = by_aid, btree(columns = [account_id])))]
#[derive(Clone, Debug)]
pub struct SkillScd {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub account_id: u32,
    pub skill_id: u16,
    pub timer: u64,
}

/// The char server's `skillcooldown` table (sql-files/main.sql): (account_id, char_id, skill, tick) — the remaining
/// time of each cooldown when the character logged out.
#[table(accessor = skillcooldown, index(accessor = by_char, btree(columns = [char_id])))]
#[derive(Clone, Debug)]
pub struct SkillCooldownRow {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub account_id: u32,
    pub char_id: u32,
    pub skill: u16,
    pub tick: i64,
}

/// `util::umap_exists(sd.scd, skill_id)`: the skill is on cooldown.
pub fn is_blocked(c: &Ctx, aid: u32, skill_id: u16) -> bool {
    c.db().skill_scd().by_aid().filter(aid).any(|r| r.skill_id == skill_id)
}

/// sd->scd as (skill_id, timer) in the unordered_map's iteration order (libstdc++'s list order, skill/umap.rs).
pub fn entries(c: &Ctx, aid: u32) -> Vec<SkillScd> {
    let rows: Vec<SkillScd> = c.db().skill_scd().by_aid().filter(aid).collect();
    let order = super::state::get(c, aid).scd_order;
    let mut v: Vec<SkillScd> = order.iter().filter_map(|k| rows.iter().find(|r| r.skill_id == *k).cloned()).collect();
    // (rows without an order entry cannot exist: both change together; kept last, by creation, as a safety net)
    let mut rest: Vec<SkillScd> = rows.into_iter().filter(|r| !order.contains(&r.skill_id)).collect();
    rest.sort_by_key(|r| r.id);
    v.extend(rest);
    v
}

/// skill.cpp skill_blockpc_end (Func::SkillBlockpcEnd; target = account id, data = skill id).
pub fn skill_blockpc_end(c: &Ctx, _tid: u64, aid: u32, data: i64) -> i32 {
    if crate::pc::get(c, aid).is_none() {
        return 0;
    }
    // sd->scd.erase(static_cast<uint16>(data))
    let key = data as u16;
    let ids: Vec<u64> = c.db().skill_scd().by_aid().filter(aid).filter(|r| r.skill_id == key).map(|r| r.id).collect();
    for id in ids {
        c.db().skill_scd().id().delete(id);
    }
    super::state::with(c, aid, |s| {
        let mut m = s.scd();
        m.erase(key);
        s.set_scd(m);
    });
    1
}

/// skill.cpp skill_blockpc_start: blocks the skill for `tick` ms (and shows the cooldown icon).
pub fn skill_blockpc_start(c: &Ctx, aid: u32, skill_id: u16, tick: Tick) -> bool {
    if !crate::db::skills::skill_db().exists(skill_id) || tick < 1 {
        return false;
    }
    let existing: Vec<SkillScd> = c.db().skill_scd().by_aid().filter(aid).collect();
    if existing.len() == MAX_SKILLCOOLDOWN {
        log::warn!("skill_blockpc_start: Too many skillcooldowns, increase MAX_SKILLCOOLDOWN.");
        return false;
    }
    // Add entry to list. (`sd.scd[skill_id] = add_timer(…)`: an existing entry's timer is replaced, the old one keeps
    // running and its end erases the new entry — as in rAthena)
    let timer = timer::add(c, c.tick() + tick, Func::SkillBlockpcEnd, aid, skill_id as i64);
    match existing.into_iter().find(|r| r.skill_id == skill_id) {
        Some(mut r) => {
            r.timer = timer;
            c.db().skill_scd().id().update(r);
        }
        None => {
            c.db().skill_scd().insert(SkillScd { id: 0, account_id: aid, skill_id, timer });
            super::state::with(c, aid, |s| {
                let mut m = s.scd();
                m.insert(skill_id);
                s.set_scd(m);
            });
        }
    }
    if crate::conf::display_status_timers() {
        if let Some(u) = crate::unit::get(c, aid) {
            super::clif::clif_skill_cooldown(c, &u, skill_id, tick);
        }
    }
    true
}

/// skill.cpp skill_blockpc_clear: every cooldown ends (unit_free, @refresh).
pub fn skill_blockpc_clear(c: &Ctx, aid: u32) {
    let u = crate::unit::get(c, aid);
    for r in entries(c, aid) {
        if crate::conf::display_status_timers() {
            if let Some(u) = &u {
                super::clif::clif_skill_cooldown(c, u, r.skill_id, 0);
            }
        }
        timer::delete(c, r.timer);
        c.db().skill_scd().id().delete(r.id);
    }
    // sd.scd.clear(): the bucket count and the rehash policy stay
    super::state::with(c, aid, |s| {
        let mut m = s.scd();
        m.clear();
        s.set_scd(m);
    });
}

/// chrif.cpp chrif_skillcooldown_save (at chrif_save with CSAVE_QUITTING) + char_mapif.cpp
/// chmapif_parse_req_saveskillcooldown: the running cooldowns' remaining time go to the `skillcooldown` table
/// (guild_skill_relog_type 0: guild skills too).
pub fn chrif_skillcooldown_save(c: &Ctx, aid: u32) {
    let Some(p) = crate::pc::get(c, aid) else { return };
    let tick = c.tick();
    for r in entries(c, aid) {
        // timer == nullptr || timer->func != skill_blockpc_end || DIFF_TICK(timer->tick, tick) < 0: skipped
        let Some(due) = timer::due(c, r.timer) else { continue };
        if due - tick < 0 {
            continue;
        }
        c.db().skillcooldown().insert(SkillCooldownRow { id: 0, account_id: aid, char_id: p.char_id, skill: r.skill_id, tick: due - tick });
    }
}

/// chrif.cpp chrif_skillcooldown_request → char_mapif.cpp chmapif_parse_req_skillcooldown → chrif_skillcooldown_load:
/// the saved cooldowns start again (at most MAX_SKILLCOOLDOWN, in the table's order) and the rows are deleted.
pub fn chrif_skillcooldown_load(c: &Ctx, aid: u32) {
    let Some(p) = crate::pc::get(c, aid) else { return };
    let mut rows: Vec<SkillCooldownRow> = c.db().skillcooldown().by_char().filter(p.char_id).filter(|r| r.account_id == aid).collect();
    if rows.is_empty() {
        return;
    }
    rows.sort_by_key(|r| r.id);
    if rows.len() >= MAX_SKILLCOOLDOWN {
        log::warn!("Too many skillcooldowns for {aid}:{}, some of them were not loaded.", p.char_id);
    }
    // Clear the data once loaded.
    for r in &rows {
        c.db().skillcooldown().id().delete(r.id);
    }
    for r in rows.into_iter().take(MAX_SKILLCOOLDOWN) {
        skill_blockpc_start(c, aid, r.skill, r.tick);
    }
}

/// unit_free / character deletion: the cooldown rows of a character that is gone.
pub fn free(c: &Ctx, aid: u32) {
    let ids: Vec<u64> = c.db().skill_scd().by_aid().filter(aid).map(|r| r.id).collect();
    for id in ids {
        c.db().skill_scd().id().delete(id);
    }
}
