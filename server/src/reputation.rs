//! Reputation: pc.cpp's ReputationDatabase (db/re/reputation.yml; the replay is db/jobs.rs reputation_find, the
//! list is in the unordered_map's iteration order), clif.cpp clif_reputation_type / clif_reputation_list /
//! clif_reputation_open (ZC_REPUTE_INFO 0x0b8d, ZC_REPUTE_OPEN 0x0b8e) and the pc.cpp registry helpers
//! pc_readreg2 / pc_setreg2 the reputation and attendance systems keep their numbers in. The script commands
//! reputationui, set_reputation_points, get_reputation_points and add_reputation_points are in buildins/extras.rs.

use std::rc::Rc;

use crate::script::compiler::ScriptCode;
use crate::script::reg::{self, Store};
use crate::script::vm::{CodeKey, Rt, State};
use crate::world::Ctx;

// ------------------------------------------------------------------------------------------------ registries

/// The registry a pc_readreg2 / pc_setreg2 name lives in (`@` temporary, `##` global account, `#` account, else
/// character).
fn store(c: &Ctx, aid: u32, name: &str) -> Option<Store> {
    let b = name.as_bytes();
    Some(match b.first().copied().unwrap_or(0) {
        b'@' => Store::CharTmp(aid),
        b'#' if b.get(1) == Some(&b'#') => Store::GlobalAcc(aid),
        b'#' => Store::Acc(aid),
        _ => Store::Char(crate::pc::get(c, aid)?.char_id),
    })
}

fn is_string_variable(name: &str) -> bool {
    name.ends_with('$')
}

/// script.hpp not_server_variable.
fn not_server_variable(prefix: u8) -> bool {
    prefix != b'$' && prefix != b'.' && prefix != b'\''
}

fn scratch(aid: u32) -> State {
    State::new(CodeKey::Func(String::new()), Rc::new(ScriptCode { buf: Vec::new(), labels: Vec::new() }), 0, aid, 0)
}

/// pc.cpp pc_readreg2: a player variable by name (numbers only; 0 when it is not one).
pub fn pc_readreg2(c: &Ctx, aid: u32, name: &str) -> i64 {
    if is_string_variable(name) {
        log::error!("pc_readreg2: Invalid variable '{name}'. String type variables are not supported.");
        return 0;
    }
    if !not_server_variable(name.as_bytes().first().copied().unwrap_or(0)) {
        log::error!("pc_readreg2: Invalid variable scope '{name}'.");
        return 0;
    }
    let Some(s) = store(c, aid, name) else { return 0 };
    reg::store_get_num(&Rt::new(c), &scratch(aid), &s, &reg::key_of(&crate::net::wire::latin1(name)), 0)
}

/// pc.cpp pc_setreg2: sets a player variable by name (the value capped to the int32 range; 0 deletes it).
pub fn pc_setreg2(c: &Ctx, aid: u32, name: &str, val: i64) -> bool {
    if is_string_variable(name) {
        log::error!("pc_setreg2: Invalid variable '{name}'. String type variables are not supported.");
        return false;
    }
    if !not_server_variable(name.as_bytes().first().copied().unwrap_or(0)) {
        log::error!("pc_setreg2: Invalid variable scope '{name}'.");
        return false;
    }
    let val = val.clamp(i32::MIN as i64, i32::MAX as i64);
    let Some(s) = store(c, aid, name) else { return false };
    let mut st = scratch(aid);
    reg::store_set_num(&Rt::new(c), &mut st, &s, &reg::key_of(&crate::net::wire::latin1(name)), 0, val);
    true
}

/// utils.hpp cap_value(a, min, max): `a >= max ? max : a <= min ? min : a` (max is tested first).
pub fn cap_value(a: i64, min: i64, max: i64) -> i64 {
    if a >= max {
        max
    } else if a <= min {
        min
    } else {
        a
    }
}

// ------------------------------------------------------------------------------------------------ clif.cpp

fn send_self(c: &Ctx, aid: u32, w: &[u8]) {
    if let Some(u) = crate::unit::get(c, aid) {
        crate::clif::send(c, w, &u, crate::clif::Target::SelfOnly);
    }
}

/// clif.cpp clif_reputation_type: ZC_REPUTE_INFO (0x0b8d) with one reputation.
pub fn clif_reputation_type(c: &Ctx, aid: u32, type_: i64, points: i64) {
    let mut w = Vec::with_capacity(21);
    w.extend_from_slice(&0x0b8du16.to_le_bytes());
    w.extend_from_slice(&21u16.to_le_bytes());
    w.push(1); // success
    w.extend_from_slice(&type_.to_le_bytes());
    w.extend_from_slice(&points.to_le_bytes());
    send_self(c, aid, &w);
}

/// clif.cpp clif_reputation_list: ZC_REPUTE_INFO (0x0b8d) with every reputation of the database and the player's
/// points (LoadEndAck on a map change and at login).
pub fn clif_reputation_list(c: &Ctx, aid: u32) {
    let db = &crate::db::jobs::job_misc().reputation;
    let len = 5 + 16 * db.len();
    let mut w = Vec::with_capacity(len);
    w.extend_from_slice(&0x0b8du16.to_le_bytes());
    w.extend_from_slice(&(len as u16).to_le_bytes());
    w.push(1); // success
    for r in db {
        w.extend_from_slice(&r.id.to_le_bytes());
        w.extend_from_slice(&pc_readreg2(c, aid, &r.variable).to_le_bytes());
    }
    send_self(c, aid, &w);
}

/// clif.cpp clif_reputation_open: ZC_REPUTE_OPEN (0x0b8e) group.Q reputation.L mode.L (mode 1 when no reputation
/// is given: "0 = red texture").
pub fn clif_reputation_open(c: &Ctx, aid: u32, tab_id: u64, rep_id: u64) {
    let mut w = Vec::with_capacity(18);
    w.extend_from_slice(&0x0b8eu16.to_le_bytes());
    w.extend_from_slice(&tab_id.to_le_bytes());
    w.extend_from_slice(&(rep_id as u32).to_le_bytes());
    w.extend_from_slice(&((rep_id == 0) as u32).to_le_bytes());
    send_self(c, aid, &w);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn cap_value_tests_max_first() {
        assert_eq!(cap_value(5, -3000, 3000), 5);
        assert_eq!(cap_value(5000, -3000, 3000), 3000);
        assert_eq!(cap_value(-5000, -3000, 3000), -3000);
        // a Maximum left at INT64_MIN (rAthena's default) wins over everything
        assert_eq!(cap_value(5, i64::MIN, i64::MIN), i64::MIN);
    }
}
