//! pc.cpp pc_autosave: every autosave_interval / (users + 1) ms (at least minsave_interval) one player is saved, in
//! turn, with chrif_save(sd, CSAVE_INVENTORY|CSAVE_CART). The char server's item save gives new items their SQL ids
//! (inventory.rs memitemdata_to_sql), which decides their slot order at the next login: a session that spans an
//! autosave can come back with another order than one saved only at logout. rAthena arms the timer when the map
//! server starts (do_init_pc); the module arms it with the first login of a module instance whose timer is gone.

use spacetimedb::{table, Table};

use crate::pc::pc as _;
use crate::timer::{self, Func};
use crate::world::Ctx;

/// conf/map_athena.conf `autosave_time: 300` (seconds; map_config_read makes it ms) and `minsave_time: 100` (ms).
pub const AUTOSAVE_INTERVAL: i64 = 300 * 1000;
pub const MINSAVE_INTERVAL: i64 = 100;

/// pc_autosave's static `last_save_id` / `save_flag`, and the running timer.
#[table(accessor = autosave_state)]
pub struct AutosaveState {
    #[primary_key]
    pub key: u8,
    pub last_save_id: u32,
    pub save_flag: u8,
    pub timer: u64,
}

fn state(c: &Ctx) -> AutosaveState {
    c.db().autosave_state().key().find(0).unwrap_or(AutosaveState { key: 0, last_save_id: 0, save_flag: 0, timer: 0 })
}

fn put(c: &Ctx, s: AutosaveState) {
    if c.db().autosave_state().key().find(0).is_some() {
        c.db().autosave_state().key().update(s);
    } else {
        c.db().autosave_state().insert(s);
    }
}

/// do_init_pc's `add_timer(gettick() + autosave_interval, pc_autosave, 0, 0)`, once per running timer.
pub fn ensure(c: &Ctx) {
    let mut s = state(c);
    if s.timer != 0 && timer::due(c, s.timer).is_some() {
        return;
    }
    s.timer = timer::add(c, c.tick() + AUTOSAVE_INTERVAL, Func::PcAutosave, 0, 0);
    put(c, s);
}

/// pc.cpp pc_autosave.
pub fn pc_autosave(c: &Ctx) {
    let mut s = state(c);
    if s.save_flag == 2 {
        s.save_flag = 0; //Someone was saved on last call, normal cycle
    } else {
        s.save_flag = 1; //Noone was saved, so save first found char.
    }
    // mapit_getallusers: pc_db in its DBMap iteration order
    let mut users: Vec<crate::pc::Pc> = c.db().pc().iter().collect();
    users.sort_by_key(|p| crate::party::idb_order(p.account_id));
    for p in &users {
        if !p.pc_loaded {
            continue; // Player data hasn't fully loaded
        }
        if p.account_id == s.last_save_id && s.save_flag != 1 {
            s.save_flag = 1;
            continue;
        }
        if s.save_flag != 1 {
            continue; //Not our turn to save yet.
        }
        //Save char.
        s.last_save_id = p.account_id;
        s.save_flag = 2;
        chrif_save(c, p.account_id);
        break;
    }
    // map_usercount: every session of pc_db
    let interval = (AUTOSAVE_INTERVAL / (users.len() as i64 + 1)).max(MINSAVE_INTERVAL);
    s.timer = timer::add(c, c.tick() + interval, Func::PcAutosave, 0, 0);
    put(c, s);
}

/// pc_autosave's chrif_save(sd, CSAVE_INVENTORY|CSAVE_CART) (chrif.rs).
pub fn chrif_save(c: &Ctx, aid: u32) {
    crate::chrif::chrif_save(c, aid, crate::chrif::CSAVE_INVENTORY | crate::chrif::CSAVE_CART);
}
