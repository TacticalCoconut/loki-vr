//! script.cpp's `dummy_sd` and `atcommand_sub` (script.cpp:77, 4871-4874, 15736-15781): the `atcommand`, `charcommand`
//! and `useatcmd` script commands. With a player attached the command runs as that player; with none (an OnInit, a
//! timer, an event run by npc_event_doall) rAthena runs it as ONE dummy character it keeps for this purpose:
//!
//! ```text
//! static map_session_data* dummy_sd;                         // script.cpp:77
//! CREATE( dummy_sd, map_session_data, 1 );                   // script.cpp:4871: zeroed memory
//! new( dummy_sd ) map_session_data();
//! dummy_sd->group_id = 99;                                   // script.cpp:4873
//! dummy_sd->fd = 0;                                          // script.cpp:4874
//! ```
//!
//! It is in none of the server's databases: map_id2sd, map_nick2sd and map_foreachpc never see it (it is never given
//! to map_addiddb / pc_db), it stands in no block list, and its fd is 0, so nothing a command "sends" it reaches a
//! client. A command function is simply handed its pointer.
//!
//! Here a character is its `unit` and `pc` rows, read back by id wherever a function needs "the sd". So the dummy is
//! kept beside the tables: while `atcommand_sub` runs a command as the dummy, `unit::get` / `unit::save` and
//! `pc::get` / `pc::put` answer for id [`ID`] from this module's memory, and no table holds a row for it: every
//! table iteration (the module's map_foreachpc), every name lookup and every other transaction is as blind to it as
//! rAthena's are. Its `pc` says `disconnected` (fd 0): clif sends to it go nowhere.
//!
//! What differs from rAthena, said plainly:
//!  - rAthena's dummy lives until the server stops, so what one command writes into it (an option, a state bit) is
//!    still there for the next script's command. This one is zeroed again when the outermost `atcommand_sub` returns.
//!  - It has the two rows above and nothing else of a character: no saved status (`character`), no inventory, no
//!    status rows, no registry. A command that works on its OWN character's saved data finds none and fails or does
//!    nothing; on rAthena it reads and writes the zeroed struct (and several of them crash there).
//!  - `map_id2bl(st->oid)` for a script whose NPC is gone dereferences a null pointer on rAthena; here the command
//!    fails with a log line.

use std::cell::RefCell;

use spacetimedb::Identity;

use super::vm::*;
use crate::map::BL_PC;
use crate::npc::npc as _;
use crate::pc::Pc;
use crate::unit::{ScView, Unit, UnitData, ViewData};
use crate::world::Ctx;

/// dummy_sd's block id and account id: zeroed memory nothing ever fills in (script.cpp:4871).
pub const ID: u32 = 0;
/// `dummy_sd->group_id = 99` (script.cpp:4873); pc_group_pc_load gives it that group on first use (script.cpp:15766-15768).
const GROUP_ID: u8 = 99;
/// A `Unit::map` no map has: bl->m of a floating NPC is -1 (and so is the dummy's then, script.cpp:15750).
const NO_MAP: u16 = u16::MAX;
/// `snprintf( dummy_sd->status.name, ..., "script.cpp:%d", __LINE__ )` at script.cpp:15759: the name of the dummy of a
/// script that has no NPC.
const NO_NPC_NAME: &str = "script.cpp:15759";
/// NAME_LENGTH (mmo.hpp): safestrncpy copies at most 23 characters of the NPC's name.
const NAME_LENGTH: usize = 24;

struct Dummy {
    unit: Unit,
    pc: Pc,
    /// atcommand_sub calls in progress (a command run by the dummy can run a script that runs another).
    depth: u32,
}

thread_local! {
    static DUMMY: RefCell<Option<Dummy>> = const { RefCell::new(None) };
}

/// The dummy's block_list side, while a command runs as it (unit::get).
pub fn unit() -> Option<Unit> {
    DUMMY.with(|d| d.borrow().as_ref().map(|d| d.unit.clone()))
}

/// The dummy's map_session_data side, while a command runs as it (pc::get).
pub fn pc() -> Option<Pc> {
    DUMMY.with(|d| d.borrow().as_ref().map(|d| d.pc.clone()))
}

/// unit::save for id [`ID`]: true when the dummy took it (no table row exists to update).
pub fn save_unit(u: &Unit) -> bool {
    DUMMY.with(|d| match d.borrow_mut().as_mut() {
        Some(d) => {
            d.unit = u.clone();
            true
        }
        None => false,
    })
}

/// pc::put for id [`ID`]: true when the dummy took it.
pub fn put_pc(p: &Pc) -> bool {
    DUMMY.with(|d| match d.borrow_mut().as_mut() {
        Some(d) => {
            d.pc = p.clone();
            true
        }
        None => false,
    })
}

/// A new reducer call starts (world::run through unit::freed_reset): no command is running as the dummy.
pub fn reset() {
    DUMMY.with(|d| *d.borrow_mut() = None);
}

/// The zeroed map_session_data with the three things script.cpp sets (group 99, fd 0, then per call the name and
/// the position).
fn zeroed() -> Dummy {
    let unit = Unit {
        id: ID,
        kind: BL_PC,
        map: 0,
        x: 0,
        y: 0,
        on_map: false, // bl.prev == nullptr: never map_addblock'ed
        name: String::new(),
        vd: ViewData::default(),
        ud: UnitData::default(),
        sc: ScView::default(),
        speed: 0,
        mode: 0,
        level: 0,
        char_id: 0,
        head_dir: 0,
        font: 0,
        manner: 0,
        karma: false,
        guild_id: 0,
        emblem_id: 0,
        hp: 0,
        max_hp: 0,
        dead: true, // status_isdead: battle_status.hp == 0 (pc_isdead, state.dead_sit, is false: Pc::dead_sit)
        size: 0,
        boss: 0,
        invisible: false,
        npc_owner_char: 0,
    };
    let pc = Pc {
        account_id: ID,
        char_id: 0,
        identity: Identity::from_byte_array([0u8; 32]),
        group_id: GROUP_ID,
        client_tick: 0,
        active: false,
        pc_loaded: false,
        connect_new: false,
        changemap: false,
        warping: false,
        rewarp: false,
        debug_remove_map: false,
        mapindex: None,
        count_rewarp: 0,
        dead_sit: 0,
        disconnected: true, // fd == 0 (script.cpp:4874)
        pmap: 0,
        npc_id: 0,
        chat_id: 0,
        npc_shopid: 0,
        trading: false,
        vending: false,
        buyingstore: false,
        prevend: false,
        storage_flag: 0,
        mail_writing: false,
        ui_open: 0,
        block_action: 0,
        canlog_tick: 0,
        cantalk_tick: 0,
        idletime: 0,
        emotionlasttime: 0,
        invincible_timer: 0,
        areanpc: Vec::new(),
        cloaked_npc: Vec::new(),
        hpmeter_visible: false,
    };
    Dummy { unit, pc, depth: 0 }
}

/// script.cpp:15745-15764: the dummy takes the name and the place of the script's NPC, or the fixed name and map 0
/// at 0,0 when the script has none. `place`: (status_get_name(*bl), bl->m, bl->x, bl->y) of `map_id2bl(st->oid)`.
fn dress(d: &mut Dummy, place: Option<(String, i32, i16, i16)>) {
    let (name, m, x, y) = match place {
        Some((name, m, x, y)) => (name.chars().take(NAME_LENGTH - 1).collect::<String>(), m, x, y),
        None => (NO_NPC_NAME.to_string(), 0, 0, 0),
    };
    d.unit.name = name;
    d.unit.map = if m < 0 { NO_MAP } else { m as u16 };
    d.unit.x = x;
    d.unit.y = y;
    // "if( dummy_sd->m > 0 ) dummy_sd->mapindex = map_id2index( dummy_sd->m ); else dummy_sd->mapindex = 0;"
    // (script.cpp:15753-15757: map id 0, the first map of the list, gets no mapindex either)
    d.pc.mapindex = if m > 0 { Some(m as u16) } else { None };
}

/// `map_id2bl( st->oid )`'s name and place: the script's NPC (a floating NPC has no unit row: its npc row carries
/// m = -1 and its coordinates), else whatever block has that id.
fn place_of(c: &Ctx, oid: u32) -> Option<(String, i32, i16, i16)> {
    if let Some(nd) = c.db().npc().id().find(oid) {
        // status_get_name, BL_NPC: nd->name (status.cpp:8921-8960)
        return Some((nd.name, nd.map, nd.x, nd.y));
    }
    crate::unit::get(c, oid).map(|u| (u.name, u.map as i32, u.x, u.y))
}

/// script.cpp:15736-15781 atcommand_sub. `kind` is is_atcommand's type: 0 for `atcommand` / `charcommand`, 3 for
/// `useatcmd`.
pub fn atcommand_sub(rt: &Rt, st: &mut State, kind: i32) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let dummy = st.rid == 0;
    let aid = if !dummy {
        // Use the attached character
        match rid2sd(rt, st, "atcommand_sub") {
            Some(a) => a,
            None => return SCRIPT_CMD_FAILURE,
        }
    } else {
        let place = if st.oid != 0 {
            match place_of(c, st.oid) {
                Some(p) => Some(p),
                None => {
                    // (rAthena dereferences the null map_id2bl here)
                    log::error!("buildin_atcommand: the script's NPC {} is gone: no dummy character to run the command as", st.oid);
                    return SCRIPT_CMD_FAILURE;
                }
            }
        } else {
            None
        };
        // Use the dummy character.
        DUMMY.with(|d| {
            let mut d = d.borrow_mut();
            let d = d.get_or_insert_with(zeroed);
            dress(d, place);
            d.depth += 1;
        });
        ID
    };
    let cmd = getstr(rt, st, 2);
    let cmd = crate::net::wire::from_latin1(&cmd);
    let ok = crate::atcommand::is_atcommand(c, aid, &cmd, kind);
    if dummy {
        DUMMY.with(|d| {
            let mut d = d.borrow_mut();
            let done = match d.as_mut() {
                Some(x) => {
                    x.depth = x.depth.saturating_sub(1);
                    x.depth == 0
                }
                None => false,
            };
            if done {
                *d = None;
            }
        });
    }
    if !ok {
        log::warn!("buildin_atcommand: failed to execute command '{cmd}'");
        reportsrc(rt, st);
        return SCRIPT_CMD_FAILURE;
    }
    SCRIPT_CMD_SUCCESS
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_dummy_is_rathenas_zeroed_character_of_group_99_without_a_client() {
        let d = zeroed();
        assert_eq!((d.unit.id, d.pc.account_id, d.pc.char_id), (0, 0, 0));
        assert_eq!(d.pc.group_id, 99);
        assert!(d.pc.disconnected, "fd 0: nothing is sent to it");
        assert!(!d.unit.on_map, "it stands in no block list");
        assert_eq!(d.pc.dead_sit, 0);
    }

    #[test]
    fn it_takes_the_npcs_name_and_place() {
        let mut d = zeroed();
        // an NPC on a map: name, m, x, y; mapindex only for m > 0
        dress(&mut d, Some(("Kafra Employee#prt".to_string(), 12, 146, 89)));
        assert_eq!((d.unit.name.as_str(), d.unit.map, d.unit.x, d.unit.y), ("Kafra Employee#prt", 12, 146, 89));
        assert_eq!(d.pc.mapindex, Some(12));
        // map id 0: "if( dummy_sd->m > 0 )" leaves mapindex 0
        dress(&mut d, Some(("first".to_string(), 0, 5, 6)));
        assert_eq!((d.unit.map, d.unit.x, d.unit.y, d.pc.mapindex), (0, 5, 6, None));
        // a floating NPC (npc/test/ci/8886.txt's "8886#ci"): m = -1
        dress(&mut d, Some(("8886#ci".to_string(), -1, 0, 0)));
        assert_eq!((d.unit.name.as_str(), d.unit.map, d.pc.mapindex), ("8886#ci", NO_MAP, None));
        // safestrncpy into char[NAME_LENGTH]: 23 characters
        dress(&mut d, Some(("x".repeat(40), 3, 1, 1)));
        assert_eq!(d.unit.name.len(), 23);
        // no NPC: the fixed name, map 0 at 0,0, no mapindex
        dress(&mut d, None);
        assert_eq!((d.unit.name.as_str(), d.unit.map, d.unit.x, d.unit.y, d.pc.mapindex), ("script.cpp:15759", 0, 0, 0, None));
    }

    #[test]
    fn it_is_only_there_while_a_command_runs_as_it() {
        reset();
        assert!(unit().is_none() && pc().is_none());
        // nothing takes a write for id 0 while no command runs as the dummy
        let z = zeroed();
        assert!(!save_unit(&z.unit) && !put_pc(&z.pc));
        DUMMY.with(|d| *d.borrow_mut() = Some(zeroed()));
        let mut u = unit().expect("active");
        u.x = 7;
        assert!(save_unit(&u));
        assert_eq!(unit().map(|u| u.x), Some(7));
        let mut p = pc().expect("active");
        p.npc_id = 5;
        assert!(put_pc(&p));
        assert_eq!(pc().map(|p| p.npc_id), Some(5));
        reset();
        assert!(unit().is_none() && pc().is_none());
    }
}
