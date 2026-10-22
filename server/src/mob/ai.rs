//! Monster AI (mob.cpp): the hard AI of monsters near players (mob_ai_sub_hard and its search helpers), the lazy AI
//! of the others, the AI timers (mob_ai_hard every MIN_MOBTHINKTIME, mob_ai_lazy every 10 x MIN_MOBTHINKTIME), random
//! walks, target locking / unlocking, looting, assisting (mob_linksearch), slaves (mob_ai_sub_hard_slavemob), warp
//! chasing, and the attacked-by hooks combat calls (mob_set_attacked_id and its timers).
//!
//! Monster skills (mobskill_use, mobskill_delay) are `mob/skill.rs` (wave D skillcore); the calls below sit where
//! mob.cpp makes them. battle_check_target / battle_check_range / unit_attack are the battle agent's (`battle_iface`).

use spacetimedb::Table;

use crate::flooritem::flooritem as _;
use crate::npc::npc as _;
use crate::pc::pc as _;

use super::battle_iface::{self as battle, BCT_ENEMY};
use super::unit::{check_distance_bl, check_distance_blxy, distance_bl, mob_can_reach, status_check_skilluse, status_check_visibility};
use super::{conf, get, mob_setstate, put, MobData, ACTIVE_AI_RANGE, AI_ABR, AI_BIONIC, LOOTITEM_SIZE, MIN_MOBLINKTIME, MIN_MOBTHINKTIME, MIN_RANDOMWALKTIME, MOB_SKILL_INTERVAL, MOB_SLAVEDISTANCE, MSS_ANGRY, MSS_BERSERK, MSS_FOLLOW, MSS_IDLE, MSS_LOOT, MSS_RUSH, MSS_WALK, RUDE_ATTACKED_COUNT};
use crate::db::mobs::{MD_AGGRESSIVE, MD_ASSIST, MD_CANATTACK, MD_CANMOVE, MD_CHANGECHASE, MD_CHANGETARGETCHASE, MD_CHANGETARGETMELEE, MD_LOOTER, MD_NORANDOMWALK, MD_RANDOMTARGET, MD_STATUSIMMUNE, MD_TARGETWEAK};
use crate::map::{self, CellChk, BL_ITEM, BL_MOB, BL_NPC, BL_PC, BL_PET, BL_SKILL};
use crate::timer::{self, Func};
use crate::unit::{self, Unit, USW_FIXPOS, USW_NONE, USW_RELEASE_TARGET};
use crate::world::{Ctx, Tick};

/// The AI timers' ids (do_init_mob's add_timer_interval calls).
#[spacetimedb::table(accessor = mob_ai_state)]
pub struct MobAiState {
    #[primary_key]
    #[index(direct)]
    pub key: u8,
    pub hard_timer: u64,
    pub lazy_timer: u64,
}

/// mob.cpp do_init_mob: `add_timer_interval(gettick()+MIN_MOBTHINKTIME, mob_ai_hard, 0, 0, MIN_MOBTHINKTIME)` and
/// `add_timer_interval(gettick()+MIN_MOBTHINKTIME*10, mob_ai_lazy, 0, 0, MIN_MOBTHINKTIME*10)`. The module has no
/// server start: they are created when a player first enters a map (and again if they are ever gone).
pub fn ensure_ai_timers(c: &Ctx) {
    let s = c.db().mob_ai_state().key().find(0);
    let alive = |t: u64| t != 0 && timer::get(c, t).is_some();
    if let Some(s) = &s {
        if alive(s.hard_timer) && alive(s.lazy_timer) {
            return;
        }
    }
    let (mut hard, mut lazy) = s.as_ref().map(|s| (s.hard_timer, s.lazy_timer)).unwrap_or((0, 0));
    if !alive(hard) {
        hard = timer::add_interval(c, c.tick() + MIN_MOBTHINKTIME, Func::MobAiHard, 0, 0, MIN_MOBTHINKTIME);
    }
    if !alive(lazy) {
        lazy = timer::add_interval(c, c.tick() + MIN_MOBTHINKTIME * 10, Func::MobAiLazy, 0, 0, MIN_MOBTHINKTIME * 10);
    }
    let row = MobAiState { key: 0, hard_timer: hard, lazy_timer: lazy };
    if s.is_some() {
        c.db().mob_ai_state().key().update(row);
    } else {
        c.db().mob_ai_state().insert(row);
    }
}

// ---------------------------------------------------------------------------------------------- timers

/// mob.cpp mob_ai_hard (interval timer): mob_ai_sub_foreachclient for every player (monster_ai 0x20 is off).
pub fn mob_ai_hard(c: &Ctx, tick: Tick) {
    if conf::mob_ai() & 0x20 != 0 {
        mob_ai_lazy(c, tick);
        return;
    }
    // map_foreachpc: pc_db's iteration order
    let mut pcs: Vec<(u32, u32)> = c.db().pc().iter().map(|p| (p.account_id, p.char_id)).collect();
    pcs.sort_by_key(|&(aid, _)| crate::party::idb_order(aid));
    // Per-map monster block-order list `(gen, Vec<(id, x, y)>)` and per-monster quiescent generation for this tick:
    // a subset of a block-ordered map list is already in exact block order, and an idle monster whose first visit on
    // `tick` left it with no target, no skill ready and `next_walktime > tick` cannot act differently for a second
    // player on the same `tick` unless a unit or flooritem moved/changed (`unit::mob_pos_gen()`).
    let mut map_mobs: std::collections::HashMap<u16, (u64, Vec<(u32, i16, i16)>)> = std::collections::HashMap::new();
    let mut quiescent: std::collections::HashMap<u32, u64> = std::collections::HashMap::new();
    let r = crate::conf::area_size() + ACTIVE_AI_RANGE;
    for (aid, char_id) in pcs {
        let Some(sd) = unit::get(c, aid) else { continue };
        let gen = unit::mob_pos_gen();
        let refresh = map_mobs.get(&sd.map).map(|&(g, _)| g != gen).unwrap_or(true);
        if refresh {
            let list: Vec<(u32, i16, i16)> = map::units_in_area(c, sd.map, 0, 0, i16::MAX, i16::MAX, BL_MOB)
                .into_iter()
                .map(|u| (u.id, u.x, u.y))
                .collect();
            map_mobs.insert(sd.map, (gen, list));
        }
        let (x0, y0, x1, y1) = (sd.x - r, sd.y - r, sd.x + r, sd.y + r);
        let ids: Vec<u32> = map_mobs[&sd.map]
            .1
            .iter()
            .filter(|&&(_, x, y)| x >= x0 && x <= x1 && y >= y0 && y <= y1)
            .map(|&(id, _, _)| id)
            .collect();
        for id in ids {
            mob_ai_sub_hard_timer_cached(c, id, char_id, tick, &mut quiescent);
        }
    }
}

/// True when `id` finished `mob_ai_sub_hard` on `tick` in a state where another call on the same `tick` and world
/// generation `unit::mob_pos_gen()` is guaranteed to be a no-op returning `true`.
fn is_quiescent_after_hard(md: &MobData, u: &Unit, tick: Tick) -> bool {
    u.on_map
        && md.status.hp > 0
        && !u.ud.force_walk
        && md.next_thinktime == tick
        && md.last_pcneartime == tick
        && u.ud.skilltimer == 0
        && u.sc.opt1 == 0
        && (u.ud.walktimer != 0 || (md.skillstate != MSS_BERSERK && tick - md.last_skillcheck < MOB_SKILL_INTERVAL))
        && md.target_id == 0
        && md.attacked_id == 0
        && md.master_id == 0
        && md.bg_id == 0
        && (md.skillstate == MSS_IDLE || (md.skillstate == MSS_WALK && u.ud.walktimer != 0))
        && (u.ud.walktimer != 0 || md.idle_event.is_empty())
        && !u.ud.attack_continue
        && u.ud.target_to == 0
        && (conf::mob_ai() & 0x8 == 0 || u.ud.ignore_cell_stack_limit || crate::conf::official_cell_stack_limit() <= 0)
        && md.next_walktime - tick > 0
}

/// mob.cpp mob_ai_sub_hard_timer.
fn mob_ai_sub_hard_timer_cached(c: &Ctx, id: u32, char_id: u32, tick: Tick, quiescent: &mut std::collections::HashMap<u32, u64>) {
    let Some(mut md) = get(c, id) else { return };
    if super::mob_add_spotted(&mut md, char_id) {
        put(c, &md);
    }
    let gen_before = unit::mob_pos_gen();
    if quiescent.get(&id) == Some(&gen_before) {
        return;
    }
    if mob_ai_sub_hard_with(c, id, tick, Some(md)) {
        // Hard AI triggered.
        if let Some(mut md) = get(c, id) {
            if md.last_pcneartime != tick {
                md.last_pcneartime = tick;
                put(c, &md);
            }
            if let Some(u) = unit::get(c, id) {
                let gen_after = unit::mob_pos_gen();
                if gen_after == gen_before && is_quiescent_after_hard(&md, &u, tick) {
                    quiescent.insert(id, gen_after);
                }
            }
        }
    }
}

/// mob.cpp mob_ai_lazy (interval timer): mob_ai_sub_lazy for every monster (map_foreachmob).
pub fn mob_ai_lazy(c: &Ctx, tick: Tick) {
    let ids = if conf::mob_ai() & (0x20 | 0x8) != 0
        || conf::boss_icewall_walk_block() == 1
        || conf::mob_icewall_walk_block() == 1
    {
        super::preload_all(c)
    } else {
        super::lazy_candidate_ids(c)
    };
    for id in ids {
        mob_ai_sub_lazy(c, id, tick);
    }
}

/// mob.cpp mob_ai_sub_lazy: monsters away from players.
fn mob_ai_sub_lazy(c: &Ctx, id: u32, tick: Tick) -> i32 {
    let Some(mut md) = get(c, id) else { return 0 };
    if conf::mob_ai() & 0x20 == 0 {
        if md.status.hp == 0 {
            return 1;
        }
        if md.last_pcneartime == 0
            && !super::mob_is_spotted(&md)
            && ((tick - md.next_thinktime < MIN_MOBTHINKTIME * 2 || tick - md.last_skillcheck < MOB_SKILL_INTERVAL)
                || (md.target_id == 0
                    && md.attacked_id == 0
                    && md.master_id == 0
                    && md.skillstate == MSS_IDLE
                    && md.idle_event.is_empty()
                    && md.dmglog.is_empty()
                    && conf::mob_ai() & 0x8 == 0
                    && conf::boss_icewall_walk_block() != 1
                    && conf::mob_icewall_walk_block() != 1))
        {
            return 0;
        }
    }
    let Some(mut u) = unit::get(c, id) else { return 0 };
    if !u.on_map {
        return 0;
    }
    // Monsters force-walked by script commands should not be searching for targets.
    if u.ud.force_walk {
        return 0;
    }
    if conf::mob_ai() & 0x20 != 0 && super::map_users(c, u.map) > 0 {
        return mob_ai_sub_hard(c, id, tick) as i32;
    }
    if md.status.hp == 0 {
        return 1;
    }
    let immune = super::has_mode(&md, MD_STATUSIMMUNE);
    // the row is written back only when this pass changed it: every monster runs through here once a second, and
    // rewriting ~3,700 unchanged rows (the castles' monsters included) made every timer of the world run late
    let mut changed = false;
    if conf::mob_active_time() != 0 && md.last_pcneartime != 0 && !immune && tick - md.next_thinktime > 0 {
        if tick - md.last_pcneartime < conf::mob_active_time() {
            return mob_ai_sub_hard(c, id, tick) as i32;
        }
        md.last_pcneartime = 0;
        changed = true;
    }
    if conf::boss_active_time() != 0 && md.last_pcneartime != 0 && immune && tick - md.next_thinktime > 0 {
        if tick - md.last_pcneartime < conf::boss_active_time() {
            return mob_ai_sub_hard(c, id, tick) as i32;
        }
        md.last_pcneartime = 0;
        changed = true;
    }
    // Clean the spotted log
    if super::mob_clean_spotted(c, &mut md) {
        changed = true;
    }
    if changed {
        put(c, &md);
        // (put's sync_unit may refresh the unit row's copies)
        let Some(u2) = unit::get(c, id) else { return 0 };
        u = u2;
    }
    // Don't continue if the hard AI was executed recently (twice the hard AI interval to account for lag)
    if tick - md.next_thinktime < MIN_MOBTHINKTIME * 2 {
        return 0;
    }
    // Don't continue if skills cannot be used yet
    if tick - md.last_skillcheck < MOB_SKILL_INTERVAL {
        return 0;
    }
    // Check for warp chase if there's still a target
    if md.target_id > 0 {
        if let Some(tbl) = unit::get(c, md.target_id) {
            if mob_warpchase(c, id, Some(&tbl)) > 0 {
                return 0;
            }
        }
    }
    if md.master_id != 0 {
        if !super::mob_is_spotted(&md) {
            if !conf::slave_active_with_master() {
                return 0;
            }
            // If neither master nor slave have been spotted we don't have to execute the slave AI
            if let Some(mmd) = get(c, md.master_id) {
                if !super::mob_is_spotted(&mmd) {
                    return 0;
                }
            }
        }
        mob_ai_sub_hard_slavemob(c, id, tick);
        return 0;
    }
    // Remove target and set to idle state when movement was finished; this also triggers the idle NPC event.
    // mob_unlocktarget changes nothing for an idle monster with no idle event due, no target and no attack in progress
    // (mob.cpp:1631-1666 with mob_ai & 0x8 off: no cell stack check); every idle monster comes here once a second, so
    // that case keeps the rows already read instead of reading them twice more
    let unlock_noop = md.skillstate == MSS_IDLE
        && (u.ud.walktimer != 0 || md.idle_event.is_empty())
        && md.target_id == 0
        && !u.ud.attack_continue
        && u.ud.target_to == 0
        && (conf::mob_ai() & 0x8 == 0 || u.ud.ignore_cell_stack_limit || crate::conf::official_cell_stack_limit() <= 0);
    let (md, u) = if unlock_noop {
        (md, u)
    } else {
        mob_unlocktarget(c, id, tick);
        let Some(md) = get(c, id) else { return 0 };
        let Some(u) = unit::get(c, id) else { return 0 };
        (md, u)
    };
    let immune = super::has_mode(&md, MD_STATUSIMMUNE);
    if md.next_walktime - tick < 0 && super::has_mode(&md, MD_CANMOVE) && unit::can_move(c, &u) {
        // Move probability for mobs away from players. In Aegis, this is 100% for mobs that have been activated by
        // players and none otherwise.
        if super::mob_is_spotted(&md)
            && ((!immune && c.rnd() % 100 < conf::mob_nopc_move_rate()) || (immune && c.rnd() % 100 < conf::boss_nopc_move_rate()))
        {
            mob_randomwalk(c, id, tick);
        }
    } else if u.ud.walktimer == 0 {
        // Probability for mobs far from players from doing their IDLE skill.
        if super::mob_is_spotted(&md)
            && ((!immune && c.rnd() % 100 < conf::mob_nopc_idleskill_rate()) || (immune && c.rnd() % 100 < conf::boss_nopc_idleskill_rate()))
        {
            mobskill_use(c, id, tick, -1); // mob.cpp:2451
        }
    }
    0
}

// ---------------------------------------------------------------------------------------------- the hard AI

/// Is the unit's status change list holding a status with SCF_MOBLOSETARGET? (status_db.hasSCF)
fn has_scf_moblosetarget(c: &Ctx, id: u32) -> bool {
    let scl = crate::status::sc::load(c, id);
    scl.list.iter().any(|e| crate::db::misc::status::status_db().find(e.sc_type).map(|s| s.has_flag(crate::status::consts::SCF_MOBLOSETARGET)).unwrap_or(false))
}

/// A player's gangster paradise state (Gangster's Paradise, phase 4 skills) or invincibility timer.
fn pc_protected(c: &Ctx, t: &Unit, mode: i32) -> bool {
    if t.kind != BL_PC {
        return false;
    }
    // sd->state.gangsterparadise is set by the skill (phase 4 skills): never here
    let gangster = false;
    (gangster && mode & MD_STATUSIMMUNE == 0) || crate::pc::get(c, t.id).map(|p| p.invincible_timer != 0).unwrap_or(false)
}

/// mob.cpp mob_ai_sub_hard: the AI of a monster near a player. Returns whether the AI ran.
pub fn mob_ai_sub_hard(c: &Ctx, id: u32, tick: Tick) -> bool {
    mob_ai_sub_hard_with(c, id, tick, None)
}

/// mob_ai_sub_hard with the monster's row when the caller holds it as it is in the table (or None: read it).
fn mob_ai_sub_hard_with(c: &Ctx, id: u32, tick: Tick, md: Option<MobData>) -> bool {
    let Some(mut md) = md.or_else(|| get(c, id)) else { return false };
    let Some(u) = unit::get(c, id) else { return false };
    if !u.on_map || md.status.hp == 0 {
        return false;
    }
    // Monsters force-walked by script commands should not be searching for targets.
    if u.ud.force_walk {
        return false;
    }
    if tick - md.next_thinktime < 0 {
        return false;
    }
    // This prevents the lazy AI from being executed at the same time (saved only when it changes: the second player
    // near the monster in the same AI tick finds it set)
    if md.next_thinktime != tick {
        md.next_thinktime = tick;
        put(c, &md);
    }
    if u.ud.skilltimer != 0 {
        return false;
    }
    // Abnormalities
    let opt1 = u.sc.opt1 as i32;
    if (opt1 != 0 && opt1 != crate::status::defs::OPT1_STONEWAIT as i32 && opt1 != crate::status::defs::OPT1_BURNING as i32) || has_scf_moblosetarget(c, id) {
        // Should reset targets.
        md.target_id = 0;
        md.attacked_id = 0;
        md.norm_attacked_id = 0;
        put(c, &md);
        return false;
    }
    // Before a monster processes its AI, it will check for a skill; if it uses a skill it will not process its AI
    // further until the next interval
    if u.ud.walktimer == 0 {
        let skill_ready = match md.skillstate {
            MSS_BERSERK => tick - u.ud.attackabletime >= 0,
            _ => tick - md.last_skillcheck >= MOB_SKILL_INTERVAL,
        };
        if skill_ready && mobskill_use(c, id, tick, -1) {
            return true;
        }
    }
    // mobskill_use saved last_skillcheck / skill_idx (and a failed cast may have touched the unit): read them again
    let Some(mut md) = get(c, id) else { return false };
    let Some(u) = unit::get(c, id) else { return false };
    let Some(db) = super::db(c, &md) else { return false };
    let view_range = if crate::status::sc::has_sce(c, id, crate::status::consts::SC_BLIND) { 1 } else { db.range2 as i32 };
    let mode = md.status.mode;
    let can_move = mode & MD_CANMOVE != 0 && unit::can_move(c, &u);
    if can_move && md.last_canmove != tick {
        md.last_canmove = tick;
        put(c, &md);
    }
    let mut tbl: Option<Unit> = None;
    if md.target_id != 0 {
        // Check validity of current target. [Skotlex]
        tbl = unit::get(c, md.target_id);
        let invalid = match &tbl {
            None => true,
            Some(t) => {
                t.map != u.map
                    || (u.ud.attacktimer == 0 && !status_check_skilluse(c, Some(&u), Some(t), 0, 0))
                    || pc_protected(c, t, mode)
            }
        };
        if invalid {
            // No valid target
            if mob_warpchase(c, id, tbl.as_ref()) > 0 {
                return true; // Chasing this target.
            }
            let u = unit::get(c, id).unwrap_or(u.clone());
            if tbl.is_some() && u.ud.walktimer != 0 && (!can_move || u.ud.path_pos as i32 <= conf::mob_chase_refresh()) {
                return true; // Walk at least "mob_chase_refresh" cells before dropping the target unless target is non-existent
            }
            mob_unlocktarget(c, id, tick); // Unlock target
            tbl = None;
        }
        // Monsters in chase state that are not moving will recheck their target (this usually happens when they
        // used a skill that stopped them or they were hit while chasing); items are handled later in this function
        let md2 = get(c, id).unwrap_or(md.clone());
        let u2 = unit::get(c, id).unwrap_or(u.clone());
        if let Some(t) = &tbl {
            if t.kind != BL_ITEM && u2.ud.walktimer == 0 && super::mob_is_chasing(md2.skillstate) {
                // But don't do this if they stopped because target is already in attack range
                if !battle::battle_check_range(c, &u2, t, md2.status.rhw.range as i32) {
                    // If target is no longer visible, target is dropped and the monster waits for next iteration
                    if !status_check_visibility(c, &u2, t, true) {
                        mob_unlocktarget(c, id, tick);
                        return true;
                    }
                }
            }
        }
    }
    let mut md = get(c, id).unwrap_or(md);
    let u = unit::get(c, id).unwrap_or(u);
    // Check for target change.
    if md.attacked_id != 0 && mode & MD_CANATTACK != 0 {
        let reach_blocked = |c: &Ctx, md: &MobData, u: &Unit| -> bool {
            // (!can_move && canmove_tick passed && (mob_ai&0x2 || trapped by Spider Web / Bite / Vacuum Extreme /
            // Thorn Trap / Manhole || walktoxy_fail_count > 0))
            let sc = crate::status::sc::load(c, u.id);
            use crate::status::consts::*;
            !can_move
                && tick - u.ud.canmove_tick > 0
                && (conf::mob_ai() & 0x2 != 0 || sc.has(SC_SPIDERWEB) || sc.has(SC_BITE) || sc.has(SC_VACUUM_EXTREME) || sc.has(SC_THORNSTRAP) || sc.has(SC__MANHOLE) || md.walktoxy_fail_count > 0)
        };
        if md.attacked_id == md.target_id {
            // Rude attacked check.
            let t = tbl.clone();
            let in_range = t.as_ref().map(|t| battle::battle_check_range(c, &u, t, md.status.rhw.range as i32)).unwrap_or(false);
            if !in_range && (reach_blocked(c, &md, &u) || !t.as_ref().map(|t| mob_can_reach(c, &u, t, db.range3 as i32)).unwrap_or(false)) {
                md.attacked_count = md.attacked_count.wrapping_add(1);
                if md.attacked_count > RUDE_ATTACKED_COUNT {
                    put(c, &md);
                    mobskill_use(c, id, tick, crate::db::mobs::MSC_RUDEATTACKED as i32);
                    md = get(c, id).unwrap_or(md);
                }
            }
        } else if let Some(abl) = unit::get(c, md.attacked_id).filter(|abl| tbl.is_none() || mob_can_changetarget(c, &md, abl, mode)) {
            let dist = distance_bl(&u, &abl);
            let rude = u.map != abl.map
                || !abl.on_map
                || dist > crate::conf::area_size() as i32 // Attacker longer than visual area
                || battle::battle_check_target(c, &u, &abl, BCT_ENEMY) <= 0 // Attacker is not enemy of mob
                || (conf::mob_ai() & 0x2 != 0 && !status_check_skilluse(c, Some(&u), Some(&abl), 0, 0)) // Cannot normal attack back to Attacker
                || (!battle::battle_check_range(c, &u, &abl, md.status.rhw.range as i32) // Not on Melee Range and ...
                    && (reach_blocked(c, &md, &u) || !mob_can_reach(c, &u, &abl, dist + db.range3 as i32)));
            if rude {
                // Rude attacked
                if abl.id != md.id {
                    // Self damage does not cause rude attack
                    md.attacked_count = md.attacked_count.wrapping_add(1);
                    if md.attacked_count > RUDE_ATTACKED_COUNT {
                        put(c, &md);
                        mobskill_use(c, id, tick, crate::db::mobs::MSC_RUDEATTACKED as i32);
                        md = get(c, id).unwrap_or(md);
                    }
                }
            } else if conf::mob_ai() & 0x2 == 0 && !status_check_skilluse(c, Some(&u), Some(&abl), 0, 0) {
                // Can't attack back, but didn't invoke a rude attacked skill...
            } else {
                // Attackable: if a monster can change the target to the attacker, it will change the target
                md.target_id = md.attacked_id; // set target
                if md.attacked_count != 0 {
                    md.attacked_count -= 1; // Should we reset rude attack count?
                }
                tbl = Some(abl); // Set the new target
            }
        }
        // Clear it since it's been checked for already.
        md.attacked_id = 0;
        md.norm_attacked_id = 0;
        put(c, &md);
    }
    let mut slave_lost_target = false;
    // Processing of slave monster
    if md.master_id > 0 {
        if mob_ai_sub_hard_slavemob(c, id, tick) == 1 {
            return true;
        }
        slave_lost_target = true;
    }
    let mut md = get(c, id).unwrap_or(md);
    let u = unit::get(c, id).unwrap_or(u);
    // Scan area for items to loot, avoid trying to loot if the mob is full and can't consume the items.
    if can_move && mode & MD_LOOTER != 0 && md.looter && tick - u.ud.canact_tick > 0 && (md.lootitems.len() < LOOTITEM_SIZE || conf::monster_loot_type() != 1) {
        if tbl.is_none() {
            // Search for items in loot range
            tbl = mob_ai_sub_hard_lootsearch(c, &mut md, &u, conf::loot_range(), None);
        } else if tbl.as_ref().map(|t| t.kind == BL_ITEM).unwrap_or(false) && conf::monster_loot_search_type() == 0 {
            // Looter already has a target item, but we want to check if there is an item that's closer
            let dist = distance_bl(&u, tbl.as_ref().unwrap()) as i16 - 1;
            if dist > 0 {
                tbl = mob_ai_sub_hard_lootsearch(c, &mut md, &u, dist, tbl.clone());
            }
        }
        put(c, &md);
    }
    if (mode & MD_AGGRESSIVE != 0 && (tbl.is_none() || slave_lost_target)) || md.skillstate == MSS_FOLLOW {
        let prev_id = md.target_id;
        let found = mob_ai_sub_hard_activesearch_all(c, &mut md, &u, view_range, mode, tbl.clone());
        if found.is_some() {
            tbl = found;
        }
        // If a monster finds a new target that is already in attack range it immediately switches to rush mode;
        // this behavior overrides even angry mode and other mode-specific behavior
        if let Some(t) = &tbl {
            if prev_id != md.target_id && battle::battle_check_range(c, &u, t, md.status.rhw.range as i32) {
                md.aggressive = false;
                mob_setstate(&mut md, MSS_RUSH);
            }
        }
        put(c, &md);
    } else if mode & MD_CHANGECHASE != 0 && (md.skillstate == MSS_RUSH || md.skillstate == MSS_FOLLOW) {
        let search_size = view_range.min(md.status.rhw.range as i32);
        for bl in map::units_in_range(c, u.map, u.x, u.y, search_size as i16, super::default_enemy_type(&md)) {
            // mob_ai_sub_hard_changechase
            if tbl.as_ref().map(|t| t.id) == Some(bl.id) || battle::battle_check_target(c, &u, &bl, BCT_ENEMY) <= 0 || !status_check_skilluse(c, Some(&u), Some(&bl), 0, 0) {
                continue;
            }
            if battle::battle_check_range(c, &u, &bl, md.status.rhw.range as i32) {
                md.target_id = bl.id;
                tbl = Some(bl);
            }
        }
        put(c, &md);
    }
    let Some(tbl) = tbl else {
        // No targets available.
        // bg guardians follow allies when no targets nearby (mob.cpp:2044-2053)
        if md.bg_id != 0 && mode & MD_CANATTACK != 0 {
            let u = unit::get(c, id).unwrap_or(u.clone());
            if u.ud.walktimer != 0 {
                return true; // we are already moving
            }
            // map_foreachinallrange(mob_ai_sub_hard_bg_ally, …): the last ally found in the area wins
            let mut ally: Option<Unit> = None;
            for bl in map::units_in_range(c, u.map, u.x, u.y, view_range as i16, BL_PC) {
                if status_check_skilluse(c, Some(&u), Some(&bl), 0, 0) && battle::battle_check_target(c, &u, &bl, BCT_ENEMY) <= 0 {
                    ally = Some(bl);
                }
            }
            if let Some(t) = ally {
                if super::unit::distance_bl(&u, &t) <= 3 || super::unit::unit_walktobl(c, id, t.id, 1, 1) {
                    return true; // we're moving or close enough don't unlock the target.
                }
            }
        }
        // Make sure target is unlocked
        let unlock_noop = (md.skillstate == MSS_IDLE || (md.skillstate == MSS_WALK && u.ud.walktimer != 0))
            && (u.ud.walktimer != 0 || md.idle_event.is_empty())
            && md.target_id == 0
            && !u.ud.attack_continue
            && u.ud.target_to == 0
            && (conf::mob_ai() & 0x8 == 0 || u.ud.ignore_cell_stack_limit || crate::conf::official_cell_stack_limit() <= 0);
        let mut md = if unlock_noop {
            md
        } else {
            mob_unlocktarget(c, id, tick);
            get(c, id).unwrap_or(md)
        };
        // Random walk
        if md.master_id == 0 && md.next_walktime - tick <= 0 && !mob_randomwalk(c, id, tick) {
            md = get(c, id).unwrap_or(md);
            let u = unit::get(c, id).unwrap_or(u);
            // Delay next random walk when this one failed
            if md.next_walktime < u.ud.canmove_tick {
                md.next_walktime = u.ud.canmove_tick;
            } else {
                md.next_walktime = tick + (c.rnd() % 1000) as i64;
            }
            put(c, &md);
        }
        return true;
    };
    // Target exists, attack or loot as applicable.
    let mut md = get(c, id).unwrap_or(md);
    let u = unit::get(c, id).unwrap_or(u);
    if tbl.kind == BL_ITEM {
        // Loot time.
        let mut loot_range = 0;
        if u.ud.walktimer != 0 {
            // Ready to loot
            let (x, y, _, _) = super::unit::getpos(c, &mut md, &u, tick);
            put(c, &md);
            if x == tbl.x && y == tbl.y {
                loot_range = 1;
            } else if u.ud.target == tbl.id {
                return true; // Already moving to target item
            }
        }
        if !md.looter {
            // Can't loot...
            mob_unlocktarget(c, id, tick);
            return true;
        }
        if !check_distance_bl(&u, &tbl, loot_range) {
            // Still not within loot range.
            if mode & MD_CANMOVE == 0 {
                // A looter that can't move? Real smart.
                mob_unlocktarget(c, id, tick);
                return true;
            }
            if !can_move {
                return true; // Stuck. Wait before walking.
            }
            mob_setstate(&mut md, MSS_LOOT);
            put(c, &md);
            if !super::unit::unit_walktobl(c, id, tbl.id, 0, 0) {
                mob_unlocktarget(c, id, tick); // Can't loot...
            } else if let Some(mut u) = unit::get(c, id) {
                unit::set_target(c, &mut u, tbl.id); // Remember current loot target
                unit::save(c, &u);
            }
            return true;
        }
        // Within looting range.
        if u.ud.attacktimer != 0 {
            return true; // Busy attacking?
        }
        let Some(fitem) = c.db().flooritem().id().find(tbl.id) else {
            mob_unlocktarget(c, id, tick);
            return true;
        };
        //Logs items, taken by (L)ooter Mobs [Lupus]
        crate::map_log::log_pick_mob(c, md.mob_id, u.map, crate::map_log::LOG_TYPE_LOOT, fitem.item.amount as i32, &fitem.item);
        let li = super::LootItem { item: fitem.item.clone(), mob_id: fitem.mob_id };
        if md.lootitems.len() < LOOTITEM_SIZE {
            md.lootitems.push(li);
        } else {
            //Destroy first looted item... (a pet egg's pet goes with it: intif_delete_petdata, pet.rs)
            if md.lootitems[0].item.card0 == crate::db::items::CARD0_PET {
                crate::pet::intif_delete_petdata(crate::pet::egg_pet_id(&md.lootitems[0].item));
            }
            md.lootitems.remove(0);
            md.lootitems.push(li);
        }
        put(c, &md);
        if crate::clif::pcdb_checkid(u.vd.class) {
            // Give them walk act/delay to properly mimic players. [Skotlex]
            crate::flooritem::clif_takeitem(c, &u, tbl.id);
            if let Some(mut u) = unit::get(c, id) {
                u.ud.canact_tick = tick + md.status.amotion as i64;
                unit::save(c, &u);
            }
            battle::unit_set_walkdelay(c, id, tick, md.status.amotion as i64, 1);
        }
        // Clear item.
        crate::flooritem::map_clearflooritem(c, tbl.id);
        mob_unlocktarget(c, id, tick);
        return true;
    }
    // At this point we know the target is attackable, attempt to attack
    // Normal attack / berserk skill is only used when target is in range
    if battle::battle_check_range(c, &u, &tbl, md.status.rhw.range as i32) {
        let mut stop_flag = USW_FIXPOS | USW_RELEASE_TARGET;
        // Hiding is a special case because it prevents normal attacks but allows skill usage
        if u.sc.option & unit::OPTION_HIDE == 0 {
            // Target within range and potentially able to use normal attack, engage
            if u.ud.target != tbl.id || u.ud.attacktimer == 0 {
                // Only attack if no more attack delay left
                stop_flag = battle::unit_attack(c, id, tbl.id, 1);
            }
        } else {
            // These status changes pretend the attack was successful without attempting it
            let mut md = get(c, id).unwrap_or(md.clone());
            mob_setstate(&mut md, MSS_BERSERK);
            put(c, &md);
        }
        // Stop and make sure there is no chase target when attack was not skipped
        if stop_flag != USW_NONE {
            if let Some(mut u) = unit::get(c, id) {
                unit::stop_walking(c, &mut u, stop_flag, 0);
                unit::save(c, &u);
            }
        }
        // Target still in attack range, no need to chase the target
        return true;
    }
    // Only update target cell / drop target after having moved at least "mob_chase_refresh" cells
    if u.ud.walktimer != 0 && (!can_move || u.ud.path_pos as i32 <= conf::mob_chase_refresh()) && super::mob_is_chasing(md.skillstate) {
        return true;
    }
    // Out of range
    if mode & MD_CANMOVE == 0 || (!can_move && (crate::status::sc::cant(c, id, crate::status::consts::SCS_NOMOVE) || tick - u.ud.canmove_tick > 0)) {
        // Can't chase. Immobile and trapped mobs will use idle skills and unlock their target after a while
        if u.ud.attacktimer == 0 {
            // Only switch mode if no more attack delay left
            if tick - md.last_canmove > conf::mob_unlock_time() {
                // Unlock target
                mob_unlocktarget(c, id, tick);
            } else {
                // Set to use idle skill next interval but keep target for now
                mob_setstate(&mut md, MSS_IDLE);
                put(c, &md);
            }
        }
        return true;
    }
    // Officially we can stop here if monster is still chasing its target. If setting to update the chase path is set,
    // we continue if current target tile not within attack range; in this case, we should also stop updating the
    // chase path when target no longer in chase range.
    if u.ud.walktimer != 0
        && u.ud.target_to == tbl.id
        && (conf::mob_ai() & 0x1 == 0 || check_distance_blxy(&tbl, u.ud.to_x, u.ud.to_y, md.status.rhw.range as i32) || !check_distance_bl(&u, &tbl, db.range3 as i32))
    {
        return true;
    }
    // Follow up if possible. Monsters in Angry state only start chasing when target is in chase range
    if md.skillstate == MSS_ANGRY && !mob_can_reach(c, &u, &tbl, db.range3 as i32) {
        mob_unlocktarget(c, id, tick);
        return true;
    }
    if !super::unit::unit_walktobl(c, id, tbl.id, md.status.rhw.range as i32, 2) {
        mob_unlocktarget(c, id, tick);
    }
    true
}

/// mob.cpp mobskill_use(md, tick, event) with mob.hpp's default damage 0: `mob/skill.rs` (the rows are saved; callers
/// read them again).
pub fn mobskill_use(c: &Ctx, id: u32, tick: Tick, event: i32) -> bool {
    super::skill::mobskill_use(c, id, tick, event, 0)
}

/// mob.cpp mob_ai_sub_hard_activesearch over DEFAULT_ENEMY_TYPE within `range` (map_foreachinallrange): the closest
/// enemy the monster can path to. Returns the target found (md.target_id is set).
fn mob_ai_sub_hard_activesearch_all(c: &Ctx, md: &mut MobData, u: &Unit, range: i32, mode: i32, current: Option<Unit>) -> Option<Unit> {
    let mut target = current;
    let mut found = None;
    let Some(db) = super::db(c, md) else { return None };
    for bl in map::units_in_range(c, u.map, u.x, u.y, range as i16, super::default_enemy_type(md)) {
        // If can't seek yet, not an enemy, or you can't attack it, skip.
        if target.as_ref().map(|t| t.id) == Some(bl.id) || !status_check_skilluse(c, Some(u), Some(&bl), 0, 0) {
            continue;
        }
        if mode & MD_TARGETWEAK != 0 && crate::status::status_get_lv(c, &bl) >= md.level - 5 {
            continue;
        }
        if battle::battle_check_target(c, u, &bl, BCT_ENEMY) <= 0 {
            continue;
        }
        // Gangster paradise protection (sd->state.gangsterparadise: phase 4 skills)
        if conf::hom_setting() & conf::HOMSET_FIRST_TARGET != 0 && target.as_ref().map(|t| t.kind == map::BL_HOM).unwrap_or(false) && bl.kind != map::BL_HOM {
            continue; // For some reason Homun targets are never overridden.
        }
        let dist = distance_bl(u, &bl);
        if (target.is_none() || !check_distance_bl(u, target.as_ref().unwrap(), dist)) && battle::battle_check_range(c, u, &bl, db.range2 as i32) {
            // Pick closest target? (ACTIVEPATHSEARCH)
            let Some(wpd) = crate::path::path_search(u.map, u.x, u.y, bl.x, bl.y, 0, CellChk::ChkWall) else { continue };
            // Standing monsters use range2, walking monsters use range3
            if (u.ud.walktimer == 0 && wpd.path.len() > db.range2 as usize) || (u.ud.walktimer != 0 && wpd.path.len() > db.range3 as usize) {
                continue;
            }
            md.target_id = bl.id;
            target = Some(bl.clone());
            found = Some(bl);
        }
    }
    found
}

/// mob.cpp mob_ai_sub_hard_lootsearch over BL_ITEM within `range` in shoot range (map_foreachinshootrange).
fn mob_ai_sub_hard_lootsearch(c: &Ctx, md: &mut MobData, u: &Unit, range: i16, current: Option<Unit>) -> Option<Unit> {
    let mut target = current;
    for bl in map::units_in_range(c, u.map, u.x, u.y, range, BL_ITEM) {
        // map_foreachinshootrange: only units in a straight shootable line
        if !crate::path::path_search_long(u.map, u.x, u.y, bl.x, bl.y, CellChk::ChkWall) {
            continue;
        }
        let dist = distance_bl(u, &bl);
        if mob_can_reach(c, u, &bl, conf::loot_range() as i32)
            && (target.is_none()
                || (conf::monster_loot_search_type() != 0 && md.target_id > bl.id)
                || (conf::monster_loot_search_type() == 0 && !check_distance_bl(u, target.as_ref().unwrap(), dist)))
        {
            md.target_id = bl.id;
            target = Some(bl);
        }
    }
    target
}

/// mob.cpp mob_can_changetarget: may the monster switch to `target`?
fn mob_can_changetarget(_c: &Ctx, md: &MobData, target: &Unit, mode: i32) -> bool {
    // Special feature that makes monsters always attack the person that provoked them
    if conf::mob_ai() & 0x800 != 0 && md.provoke_flag != 0 {
        if md.provoke_flag == target.id {
            return true;
        } else if conf::mob_ai() & 0x4 == 0 {
            return false;
        }
    }
    match md.skillstate {
        MSS_BERSERK => {
            if mode & MD_CHANGETARGETMELEE == 0 {
                return false;
            }
            // If the special normal attacked event occurred, always change target in berserk state
            if md.norm_attacked_id == target.id {
                return true;
            }
            // If the special setting to switch target even on skills is set, we need to verify the range here
            if conf::mob_ai() & 0x80 == 0 {
                return false;
            }
            false // (mob_ai&0x80 is off)
        }
        MSS_RUSH => mode & MD_CHANGETARGETCHASE != 0,
        MSS_FOLLOW | MSS_ANGRY | MSS_IDLE | MSS_WALK | MSS_LOOT => true,
        _ => false,
    }
}

/// mob.cpp mob_target: lock on `bl` unless the monster may not change target now. (script unitattack)
pub fn mob_target(c: &Ctx, id: u32, bl: &Unit, _dist: i32) -> i32 {
    let Some(mut md) = get(c, id) else { return 0 };
    let Some(u) = unit::get(c, id) else { return 0 };
    // Nothing will be carried out if there is no mind of changing TAGE by TAGE ending.
    if md.target_id != 0 && !mob_can_changetarget(c, &md, bl, md.status.mode) {
        return 0;
    }
    if !status_check_skilluse(c, Some(&u), Some(bl), 0, 0) {
        return 0;
    }
    md.target_id = bl.id; // Since there was no disturbance, it locks on to target.
    if md.provoke_flag != 0 && bl.id != md.provoke_flag {
        md.provoke_flag = 0;
    }
    // When an angry monster is provoked, it will switch to retaliate AI
    if md.provoke_flag != 0 && md.aggressive {
        md.aggressive = false;
    }
    put(c, &md);
    0
}

/// mob.cpp mob_randomtarget: a monster with MD_RANDOMTARGET picks a random visible enemy in attack range. Returns
/// false when none is found (the attack is skipped); `target_id` is replaced otherwise.
pub fn mob_randomtarget(c: &Ctx, id: u32, target_id: &mut u32) -> bool {
    let Some(md) = get(c, id) else { return true };
    if !super::has_mode(&md, MD_RANDOMTARGET) {
        return true; // Keep current target
    }
    let Some(u) = unit::get(c, id) else { return true };
    // Pick a random visible target
    let Some(target) = battle::battle_getenemy(c, &u, super::default_enemy_type(&md), md.status.rhw.range as i32) else { return false };
    // Check if target is in shoot range
    if !battle::battle_check_range(c, &u, &target, md.status.rhw.range as i32) {
        return false;
    }
    *target_id = target.id;
    true
}

/// mob.cpp mob_linksearch: `md` (a monster of `mob_id` with MD_ASSIST and no target) links to `target_id`.
pub fn mob_linksearch(c: &Ctx, id: u32, mob_id: i32, target_id: u32, tick: Tick) -> bool {
    let Some(mut md) = get(c, id) else { return false };
    // Check if mob qualifies for assistance: line of sight to the ally is already checked at this point, no valid
    // path to the target is required
    if md.mob_id == mob_id && super::has_mode(&md, MD_ASSIST) && tick - md.last_linktime >= MIN_MOBLINKTIME && md.target_id == 0 {
        md.last_linktime = tick;
        md.target_id = target_id;
        put(c, &md);
        return true;
    }
    false
}

/// unit.cpp unit_attack_timer_sub's `map_foreachinshootrange(mob_linksearch, src, battle_config.assist_range,
/// BL_MOB, md->mob_id, target->id, tick)` for the battle agent: every monster of the same kind in shoot range of
/// `src` links to `target_id`.
pub fn mob_linksearch_area(c: &Ctx, src: &Unit, mob_id: i32, target_id: u32, tick: Tick) -> i32 {
    let r = conf::assist_range();
    let mut n = 0;
    for bl in map::units_in_range(c, src.map, src.x, src.y, r, BL_MOB) {
        if !crate::path::path_search_long(src.map, src.x, src.y, bl.x, bl.y, CellChk::ChkWall) {
            continue;
        }
        if mob_linksearch(c, bl.id, mob_id, target_id, tick) {
            n += 1;
        }
    }
    n
}

// ---------------------------------------------------------------------------------------------- slaves

/// mob.cpp mob_ai_sub_hard_slavemob: slaves follow their master and take over its target. Returns 1 when the slave
/// did something this round.
pub fn mob_ai_sub_hard_slavemob(c: &Ctx, id: u32, tick: Tick) -> i32 {
    let Some(mut md) = get(c, id) else { return 0 };
    let Some(u) = unit::get(c, id) else { return 0 };
    let master = unit::get(c, md.master_id);
    let Some(bl) = master.filter(|m| !crate::status::sc::status_isdead(c, m)) else {
        crate::status::hp::status_kill(c, id);
        return 1;
    };
    if !bl.on_map {
        return 0; // Master not on a map? Could be warping, do not process.
    }
    if super::has_mode(&md, MD_CANMOVE) {
        // If the mob can move, follow around. [Check by Skotlex]
        // Distance with between slave and master is measured.
        md.master_dist = distance_bl(&u, &bl);
        put(c, &md);
        if conf::slave_stick_with_master() || md.special_ai == AI_ABR || md.special_ai == AI_BIONIC {
            // Teleport to master if further away than 15 cells (official value for AI_ABR and AI_BIONIC)
            if bl.map != u.map || md.master_dist > crate::conf::area_size() as i32 + 1 {
                md.master_dist = 0;
                put(c, &md);
                super::unit::unit_warp(c, id, bl.map as i32, bl.x, bl.y, unit::CLR_TELEPORT);
                return 1;
            }
        }
        // Slave is busy with a target.
        if md.target_id != 0 {
            // Player's slave should come back when master's too far, even if it is doing with a target.
            if bl.kind == BL_PC && md.master_dist > 5 {
                mob_unlocktarget(c, id, tick);
                super::unit::unit_walktobl(c, id, bl.id, MOB_SLAVEDISTANCE as i32, 1);
                return 1;
            } else {
                return 0;
            }
        }
        // Approach master if within view range, chase back to Master's area also if standing on top of the master.
        if (md.master_dist > MOB_SLAVEDISTANCE as i32 || md.master_dist == 0) && unit::can_move(c, &u) {
            if let Some((x, y)) = super::spawn::map_search_freecell(c, Some((u.map, u.x, u.y)), bl.map as i32, bl.x, bl.y, MOB_SLAVEDISTANCE, MOB_SLAVEDISTANCE, 1, 50) {
                if !unit::walktoxy(c, id, x, y, 0) {
                    // Slave is too far from master (outside of battle_config.max_walk_path range), stay put
                    if let Some(mut u) = unit::get(c, id) {
                        unit::stop_walking(c, &mut u, USW_FIXPOS, 0);
                        unit::save(c, &u);
                    }
                    return 0; // Fail here so target will be picked back up when in range
                } else {
                    // Slave will walk back to master if in range
                    if let Some(mut u) = unit::get(c, id) {
                        unit::stop_attack(c, &mut u);
                        unit::save(c, &u);
                    }
                    return 1;
                }
            }
        }
    } else if bl.map != u.map && crate::status::map_flag_gvg2(c, u.map) {
        // Delete the summoned mob if it's in a gvg ground and the master is elsewhere. [Skotlex]
        crate::status::hp::status_kill(c, id);
        return 1;
    }
    // Avoid attempting to lock the master's target too often to avoid unnecessary overload. [Skotlex]
    let mut md = get(c, id).unwrap_or(md);
    if tick - md.last_linktime >= MIN_MOBLINKTIME && md.target_id == 0 {
        let ud = &bl.ud;
        let mut tbl: Option<Unit> = None;
        if ud.target != 0 && ud.attack_continue {
            tbl = unit::get(c, ud.target);
        } else if ud.target_to != 0 && ud.attack_continue {
            tbl = unit::get(c, ud.target_to);
        } else if ud.skilltarget != 0 {
            tbl = unit::get(c, ud.skilltarget);
            // Required check as skilltarget is not always an enemy. [Skotlex]
            if tbl.as_ref().map(|t| battle::battle_check_target(c, &u, t, BCT_ENEMY) <= 0).unwrap_or(false) {
                tbl = None;
            }
        } else if bl.kind == BL_MOB {
            // If master is a monster, it might still have a target after using a skill
            if let Some(mmd) = get(c, bl.id) {
                if mmd.target_id > 0 {
                    tbl = unit::get(c, mmd.target_id);
                }
            }
        }
        if let Some(t) = tbl {
            md.last_linktime = tick;
            put(c, &md);
            if status_check_skilluse(c, Some(&u), Some(&t), 0, 0) {
                md.target_id = t.id;
                put(c, &md);
                return 1;
            }
        }
    }
    0
}

// ---------------------------------------------------------------------------------------------- unlock / walk

/// mob.cpp mob_unlocktarget: drops the target and goes back to standby (running the idle NPC event, stopping a
/// looter soon). The rows are saved.
pub fn mob_unlocktarget(c: &Ctx, id: u32, tick: Tick) -> i32 {
    let Some(mut md) = get(c, id) else { return 0 };
    let Some(u) = unit::get(c, id) else { return 0 };
    let mut fall_default = false;
    // the rows are written back at the end only when this call changed them (every idle monster passes here once a
    // second from mob_ai_sub_lazy)
    let mut md_changed = false;
    match md.skillstate {
        MSS_WALK | MSS_IDLE => {
            let mut idle = md.skillstate == MSS_IDLE;
            if md.skillstate == MSS_WALK {
                if u.ud.walktimer == 0 {
                    // Because it is not unset when the mob finishes walking.
                    mob_setstate(&mut md, MSS_IDLE);
                    md_changed = true;
                    idle = true;
                }
            }
            if idle && u.ud.walktimer == 0 && !md.idle_event.is_empty() {
                let ev = md.idle_event.clone();
                put(c, &md);
                if crate::script::npc_run::npc_event_do_id(c, &ev, id) > 0 {
                    if let Some(mut m2) = get(c, id) {
                        m2.idle_event.clear();
                        put(c, &m2);
                    }
                }
                md = match get(c, id) {
                    Some(m) => m,
                    None => return 0,
                };
            }
        }
        MSS_LOOT => {
            // Looters that lost their target stop after 0.5-1.5 cells moved
            put(c, &md);
            super::unit::unit_stop_walking_soon(c, id, tick);
            md = get(c, id).unwrap_or(md);
            fall_default = true;
        }
        _ => fall_default = true,
    }
    if fall_default {
        put(c, &md);
        if let Some(mut u) = unit::get(c, id) {
            unit::stop_attack(c, &mut u);
            unit::save(c, &u);
        }
        md = get(c, id).unwrap_or(md);
        mob_setstate(&mut md, MSS_IDLE);
        if conf::mob_ai() & 0x8 != 0 {
            // Walk instantly after dropping target
            md.next_walktime = tick + (c.rnd() % 1000) as i64;
        } else {
            md.next_walktime = tick + (c.rnd() % 1000) as i64 + MIN_RANDOMWALKTIME;
        }
        md_changed = true;
    }
    let mut u = unit::get(c, id).unwrap_or(u);
    let mut u_changed = false;
    if md.target_id != 0 {
        md.target_id = 0;
        unit::set_target(c, &mut u, 0);
        md_changed = true;
        u_changed = true;
    }
    if u.ud.attack_continue || u.ud.target_to != 0 {
        u.ud.attack_continue = false;
        u.ud.target_to = 0;
        u_changed = true;
    }
    if u_changed {
        unit::save(c, &u);
    }
    if md_changed {
        put(c, &md);
    }
    if !u.ud.ignore_cell_stack_limit
        && crate::conf::official_cell_stack_limit() > 0
        && conf::mob_ai() & 0x8 != 0
        && map::count_oncell(c, u.map, u.x, u.y, map::BL_CHAR | BL_NPC, 1) > crate::conf::official_cell_stack_limit() as usize
    {
        unit::walktoxy(c, id, u.x, u.y, 8);
    }
    0
}

/// mob.cpp mob_randomwalk: a random walk of up to 7 cells. Returns whether the monster started walking.
pub fn mob_randomwalk(c: &Ctx, id: u32, tick: Tick) -> bool {
    const D: i32 = 7;
    let Some(mut md) = get(c, id) else { return false };
    let Some(mut u) = unit::get(c, id) else { return false };
    // Initialize next_walktime (INVALID_TIMER is -1)
    if md.next_walktime == -1 {
        md.next_walktime = tick + (c.rnd() % 1000) as i64 + MIN_RANDOMWALKTIME;
        put(c, &md);
        return true;
    }
    if md.next_walktime - tick > 0 || super::has_mode(&md, MD_NORANDOMWALK) || !unit::can_move(c, &u) || !super::has_mode(&md, MD_CANMOVE) {
        return false;
    }
    // Make sure the monster has no target anymore, otherwise the walkpath will check for it
    u.ud.attack_continue = false;
    u.ud.target_to = 0;
    unit::save(c, &u);
    let r = c.rnd() as i32;
    let rdir = c.rnd() % 4; // Randomize direction in which we iterate to prevent monster cluttering up in one corner
    let mut dx = r % (D * 2 + 1) - D;
    let mut dy = r / (D * 2 + 1) % (D * 2 + 1) - D;
    let max = (D * 2 + 1) * (D * 2 + 1);
    let mut i = 0;
    while i < max {
        // Search of a movable place
        let x = dx + u.x as i32;
        let y = dy + u.y as i32;
        if (x != u.x as i32 || y != u.y as i32) && map::getcell(c, u.map, x as i16, y as i16, CellChk::ChkPass) && unit::walktoxy(c, id, x as i16, y as i16, 0) {
            break;
        }
        // Could not move to cell, try the 7th cell in direction randomly decided by rdir. We don't move
        // step-by-step because this will make monster stick to the walls
        match rdir {
            0 => {
                dx += D;
                if dx > D {
                    dx -= D * 2 + 1;
                    dy += D;
                    if dy > D {
                        dy -= D * 2 + 1;
                    }
                }
            }
            1 => {
                dx -= D;
                if dx < -D {
                    dx += D * 2 + 1;
                    dy -= D;
                    if dy < -D {
                        dy += D * 2 + 1;
                    }
                }
            }
            2 => {
                dy += D;
                if dy > D {
                    dy -= D * 2 + 1;
                    dx += D;
                    if dx > D {
                        dx -= D * 2 + 1;
                    }
                }
            }
            _ => {
                dy -= D;
                if dy < -D {
                    dy += D * 2 + 1;
                    dx -= D;
                    if dx < -D {
                        dx += D * 2 + 1;
                    }
                }
            }
        }
        i += 1;
    }
    let mut md = get(c, id).unwrap_or(md);
    if i == max {
        // None of the available cells worked, try again next interval
        if conf::mob_stuck_warning() {
            md.move_fail_count += 1;
            if md.move_fail_count > 1000 {
                log::warn!("MOB can't move. random spawn {}, class = {}, at map {} ({},{})", id, md.mob_id, u.map, u.x, u.y);
                md.move_fail_count = 0;
                put(c, &md);
                super::spawn::mob_spawn(c, id);
                return false;
            }
            put(c, &md);
        }
        return false;
    }
    mob_setstate(&mut md, MSS_WALK);
    md.move_fail_count = 0;
    let u = unit::get(c, id).unwrap_or(u);
    md.next_walktime = tick + (c.rnd() % 1000) as i64 + MIN_RANDOMWALKTIME + super::unit::unit_get_walkpath_time(&u);
    put(c, &md);
    true
}

// ---------------------------------------------------------------------------------------------- warp chase

/// mob.cpp mob_warpchase: moves the monster to the warp that leads closest to its target (monster_ai 0x40,
/// mob_warp). Returns 0 no warp chase, 1 warp chasing, 2 already walking to a warp.
pub fn mob_warpchase(c: &Ctx, id: u32, target: Option<&Unit>) -> i32 {
    if conf::mob_ai() & 0x40 == 0 {
        return 0; // Warp chase disabled
    }
    let Some(target) = target else { return 0 }; // No target
    let mut kinds = 0u16;
    if crate::conf::mob_warp() & 1 != 0 {
        kinds |= BL_NPC;
    }
    if crate::conf::mob_warp() & 2 != 0 {
        kinds |= BL_SKILL;
    }
    if kinds == 0 {
        return 0; // No warp types to search for
    }
    let Some(u) = unit::get(c, id) else { return 0 };
    let Some(md) = get(c, id) else { return 0 };
    if target.map == u.map && check_distance_bl(&u, target, crate::conf::area_size() as i32) {
        return 0; // No need to do a warp chase.
    }
    if u.ud.walktimer != 0 && map::getcell(c, u.map, u.ud.to_x, u.ud.to_y, CellChk::ChkNpc) {
        return 2; // Already walking to a warp.
    }
    let mut warp: Option<u32> = None;
    let mut distance = crate::conf::area_size() as i32;
    let range2 = super::db(c, &md).map(|d| d.range2).unwrap_or(0) as i16;
    // Search for warps within mob's viewing range (mob_warpchase_sub)
    for bl in map::units_in_range(c, u.map, u.x, u.y, range2, kinds) {
        if bl.kind == BL_NPC {
            let Some(nd) = c.db().npc().id().find(bl.id) else { continue };
            // Not a warp
            if nd.subtype != crate::npc::NPCTYPE_WARP {
                continue;
            }
            // Does not lead to the same map as target
            if nd.warp_map != target.map as i32 {
                continue;
            }
            // Leads to a different map that is set to not be accessible through warp chase
            if crate::conf::mob_warp() & 4 != 0 && nd.warp_map as u16 != bl.map && crate::npc::mapflag(c, nd.warp_map as u16, super::MF_NOBRANCH) {
                continue;
            }
            // Get distance from warp exit to target
            let cur = crate::path::distance((target.x - nd.warp_x) as i32, (target.y - nd.warp_y) as i32);
            // Pick warp that leads closest to target
            if cur < distance {
                warp = Some(bl.id);
                distance = cur;
            }
        }
        if bl.kind == BL_SKILL {
            // Skill Warp (a priest's Warp Portal: skillunit)
            let Some(g) = crate::skillunit::group_of_unit(c, bl.id) else { continue };
            if g.unit_id != crate::skillunit::consts::UNT_WARP_WAITING {
                continue; // UNT_DIMENSIONDOOR's target is random; the rest cannot warp
            }
            // Monsters cannot use priest warps
            if crate::conf::mob_warp() & 2 == 0 {
                continue;
            }
            // Does not lead to the same map as target (val3: the map, as the module's map id)
            if g.val3 != target.map as i32 {
                continue;
            }
            // Leads to a different map that is set to not be accessible through warp chase
            if crate::conf::mob_warp() & 4 != 0 && g.val3 as u16 != bl.map && crate::npc::mapflag(c, g.val3 as u16, super::MF_NOBRANCH) {
                continue;
            }
            // Get distance from warp exit to target
            let cur = crate::path::distance((target.x - (g.val2 >> 16) as i16) as i32, (target.y - (g.val2 & 0xffff) as i16) as i32);
            //Pick warp that leads closest to target.
            if cur < distance {
                warp = Some(bl.id);
                distance = cur;
            }
        }
    }
    if let Some(w) = warp {
        if super::unit::unit_walktobl(c, id, w, 0, 0) {
            return 1;
        }
    }
    0
}

// ---------------------------------------------------------------------------------------------- attacked

/// mob.cpp mob_set_attacked_id: remembers who attacked the monster (a pet's master; a slave's master with
/// retaliate_to_master) and runs its AI at once. The battle agent calls it through the MobAttacked /
/// MobNormAttacked timers (battle.cpp battle_damage's add_timer(tick + delay, mob_attacked, target, src)).
pub fn mob_set_attacked_id(c: &Ctx, src_id: u32, target_id: u32, tick: Tick, is_norm_attacked: bool) {
    let Some(src) = unit::get(c, src_id) else { return };
    let Some(mut md) = get(c, target_id) else { return };
    match src.kind {
        BL_PET => {
            // Let mobs retaliate against the pet's master (pd.master, pet/data.rs)
            if let Some(pd) = crate::pet::data::get(c, src_id) {
                if crate::pc::get(c, pd.master).is_some() {
                    md.attacked_id = pd.master;
                }
            }
        }
        BL_MOB => {
            let md2 = get(c, src_id);
            // Config to decide whether to retaliate versus the master or the mob
            match md2 {
                Some(m2) if m2.master_id != 0 && conf::retaliate_to_master() => md.attacked_id = m2.master_id,
                _ => md.attacked_id = src.id,
            }
        }
        _ => md.attacked_id = src.id, // Retaliate against attacker
    }
    if is_norm_attacked {
        md.norm_attacked_id = md.attacked_id;
    }
    // As it was attacked, monster leaves aggressive mode
    md.aggressive = false;
    put(c, &md);
    // Need to call mob AI routine immediately, otherwise the attacked ID might get overwritten before it is processed
    mob_ai_sub_hard(c, target_id, tick);
}

/// mob.cpp mob_attacked (timer: id = the monster, data = the attacker).
pub fn mob_attacked(c: &Ctx, tick: Tick, id: u32, data: i64) {
    mob_set_attacked_id(c, data as u32, id, tick, false);
}

/// mob.cpp mob_norm_attacked (timer).
pub fn mob_norm_attacked(c: &Ctx, tick: Tick, id: u32, data: i64) {
    mob_set_attacked_id(c, data as u32, id, tick, true);
}

/// mob.cpp mob_ai_sub_hard_attacktimer: at the end of its attack delay a monster re-checks warp chase and whether
/// it still sees its target, then runs its whole AI. False = the attack should be cancelled. (unit_attack_timer_sub,
/// battle agent, calls it.)
pub fn mob_ai_sub_hard_attacktimer(c: &Ctx, id: u32, tick: Tick) -> bool {
    let Some(u) = unit::get(c, id) else { return false };
    // No target anymore
    let Some(target) = unit::get(c, u.ud.target) else { return false };
    // Check for warp chase
    if mob_warpchase(c, id, Some(&target)) > 0 {
        return true;
    }
    // Monsters have a special visibility check at the end of their attack delay
    if !status_check_visibility(c, &u, &target, true) {
        return false;
    }
    // Go through the whole monster AI
    mob_ai_sub_hard(c, id, tick);
    true
}

/// unit.hpp enum e_delay_event (DELAY_EVENT_ATTACK = 1; the values battle/unit_attack.rs passes).
pub const DELAY_EVENT_ATTACK: i32 = 1;
pub const DELAY_EVENT_CASTBEGIN_ID: i32 = 2;
pub const DELAY_EVENT_CASTBEGIN_POS: i32 = 3;
pub const DELAY_EVENT_CASTEND: i32 = 4;
pub const DELAY_EVENT_CASTCANCEL: i32 = 5;
pub const DELAY_EVENT_DAMAGED: i32 = 6;
pub const DELAY_EVENT_PARRY: i32 = 7;

/// mob.cpp mob_set_delay: a monster's AI rests for its attack motion after attacking / finishing a cast; cast end
/// and cancel set its skill delays (mobskill_delay, mob/skill.rs). (unit_set_attackdelay, battle agent, calls it.)
pub fn mob_set_delay(c: &Ctx, id: u32, tick: Tick, event: i32) {
    let Some(mut md) = get(c, id) else { return };
    // A monster's AI is inactive for its attack motion after attacking or finish casting a skill
    if conf::mob_ai() & 0x2000 == 0 && (event == DELAY_EVENT_ATTACK || event == DELAY_EVENT_CASTEND) {
        md.next_thinktime = (tick + md.status.amotion as i64).max(md.next_thinktime);
        put(c, &md);
    }
    // On cast end and cast cancel, the monster skill delays are set
    if event == DELAY_EVENT_CASTEND || event == DELAY_EVENT_CASTCANCEL {
        super::skill::mobskill_delay(c, &mut md, tick);
        put(c, &md);
    }
}

#[cfg(test)]
mod tests {
    /// unit.hpp e_delay_event starts at 1: the values here are the ones unit_set_attackdelay passes.
    #[test]
    fn delay_events_match_unit_attack() {
        use crate::battle::unit_attack as ua;
        assert_eq!(
            [super::DELAY_EVENT_ATTACK, super::DELAY_EVENT_CASTBEGIN_ID, super::DELAY_EVENT_CASTBEGIN_POS, super::DELAY_EVENT_CASTEND, super::DELAY_EVENT_CASTCANCEL, super::DELAY_EVENT_DAMAGED, super::DELAY_EVENT_PARRY],
            [ua::DELAY_EVENT_ATTACK, ua::DELAY_EVENT_CASTBEGIN_ID, ua::DELAY_EVENT_CASTBEGIN_POS, ua::DELAY_EVENT_CASTEND, ua::DELAY_EVENT_CASTCANCEL, ua::DELAY_EVENT_DAMAGED, ua::DELAY_EVENT_PARRY]
        );
        assert_eq!(super::DELAY_EVENT_ATTACK, 1);
    }
}
