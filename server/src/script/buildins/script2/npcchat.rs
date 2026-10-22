//! npc_chat.cpp (PCRE_SUPPORT): NPCs that listen to public chat — defpattern, activatepset, deactivatepset,
//! deletepset, and npc_chat_sub (called by clif_parse_GlobalMessage for the NPCs around the speaker).

use crate::script::buildins::*;
use crate::script::vm::*;
use crate::world::Ctx;
use spacetimedb::Table;
use crate::pc_misc::{npc_chat_set as _, ChatPattern, NpcChatSet};

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "defpattern" => defpattern,
        "activatepset" => activatepset,
        "deactivatepset" => deactivatepset,
        "deletepset" => deletepset,
        _ => return None,
    })
}

fn sets(c: &Ctx, npc_id: u32, active: bool) -> Vec<NpcChatSet> {
    let mut v: Vec<NpcChatSet> = c.db().npc_chat_set().by_npc().filter(npc_id).filter(|s| s.active == active).collect();
    v.sort_by(|a, b| b.seq.cmp(&a.seq));
    v
}

fn next_seq(c: &Ctx) -> u64 {
    c.db().npc_chat_set().iter().map(|s| s.seq).max().unwrap_or(0) + 1
}

/// The world is rebuilt: every NPC's chatdb goes (npc_chat_finalize at npc_unload).
pub fn on_world_rebuilt(c: &Ctx) {
    for id in c.db().npc_chat_set().iter().map(|s| s.id).collect::<Vec<_>>() {
        c.db().npc_chat_set().id().delete(id);
    }
}

/// lookup_pcreset: the set (active first, then inactive), created at the head of the inactive list when missing.
fn lookup_pcreset(c: &Ctx, npc_id: u32, setid: i64) -> NpcChatSet {
    if let Some(s) = sets(c, npc_id, true).into_iter().chain(sets(c, npc_id, false)).find(|s| s.setid == setid) {
        return s;
    }
    let seq = next_seq(c);
    c.db().npc_chat_set().insert(NpcChatSet { id: 0, npc_id, setid, active: false, seq, patterns: Vec::new() })
}

/// defpattern <set id>,"<pattern>","<label>".
fn defpattern(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let setid = getnum64(rt, st, 2);
    let pattern = getstr(rt, st, 3);
    let label = lat(&getstr(rt, st, 4));
    let mut s = lookup_pcreset(c, st.oid, setid);
    s.patterns.push(ChatPattern { pattern, label });
    c.db().npc_chat_set().id().update(s);
    0
}

/// activatepset <set id>: the inactive set moves to the head of the active list (nothing when it is not inactive).
fn activatepset(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let setid = getnum64(rt, st, 2);
    if let Some(s) = sets(c, st.oid, false).into_iter().find(|s| s.setid == setid) {
        let seq = next_seq(c);
        c.db().npc_chat_set().id().update(NpcChatSet { active: true, seq, ..s });
    }
    0
}

fn deactivate(c: &Ctx, npc_id: u32, setid: i64) {
    if setid == -1 {
        while let Some(s) = sets(c, npc_id, true).into_iter().next() {
            deactivate(c, npc_id, s.setid);
        }
        return;
    }
    if let Some(s) = sets(c, npc_id, true).into_iter().find(|s| s.setid == setid) {
        let seq = next_seq(c);
        c.db().npc_chat_set().id().update(NpcChatSet { active: false, seq, ..s });
    }
}

/// deactivatepset <set id>: the active set moves to the head of the inactive list (-1: every active set).
fn deactivatepset(rt: &Rt, st: &mut State) -> i32 {
    let setid = getnum64(rt, st, 2);
    deactivate(rt.c.unwrap(), st.oid, setid);
    0
}

/// deletepset <set id>.
fn deletepset(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let setid = getnum64(rt, st, 2);
    if let Some(s) = sets(c, st.oid, true).into_iter().chain(sets(c, st.oid, false)).find(|s| s.setid == setid) {
        c.db().npc_chat_set().id().delete(s.id);
    }
    0
}

/// npc_chat.cpp npc_chat_sub for every NPC around the speaker (map_foreachinallrange(npc_chat_sub, sd, AREA_SIZE,
/// BL_NPC, output, strlen(output), sd)): the first matching pattern of an NPC's active sets stores the matched
/// strings in $@p0$.. and runs the pattern's label.
pub fn npc_chat_area(c: &Ctx, aid: u32, output: &str) {
    if c.db().npc_chat_set().iter().all(|s| !s.active) {
        return; // no NPC listens
    }
    let Some(sd) = crate::unit::get(c, aid) else { return };
    let r = crate::conf::area_size();
    let msg = crate::net::wire::latin1(output);
    for nd in crate::map::units_in_area(c, sd.map, sd.x - r, sd.y - r, sd.x + r, sd.y + r, crate::map::BL_NPC) {
        npc_chat_sub(c, nd.id, &msg, aid);
    }
}

fn npc_chat_sub(c: &Ctx, npc_id: u32, msg: &[u8], aid: u32) {
    // Not interested in anything you might have to say...
    for set in sets(c, npc_id, true) {
        for e in &set.patterns {
            let Some((r, caps)) = super::pcre::exec(&e.pattern, msg, 0, true) else { continue };
            if r <= 0 {
                continue;
            }
            // save out the matched strings
            for i in 0..r as usize {
                let val: Vec<u8> = match caps.get(i).copied().flatten() {
                    Some((a, b)) => msg[a..b].iter().copied().take(254).collect(),
                    None => Vec::new(),
                };
                crate::script::reg::set_var_str(c, aid, &format!("$@p{i}$"), &val);
            }
            // find the target label.. this sucks..
            let Some((key, code)) = crate::script::code::npc_code(c, npc_id) else { return };
            let want: Vec<u8> = e.label.as_bytes().iter().copied().take(24).collect();
            let Some(pos) = code.labels.iter().find(|(n, _)| n.as_bytes().iter().copied().take(24).eq(want.iter().copied())).map(|(_, p)| *p) else {
                log::warn!("Unable to find label: {}", e.label);
                return;
            };
            // run the npc script
            run_script(&Rt::new(c), &key, pos, aid, npc_id);
            return;
        }
    }
}
