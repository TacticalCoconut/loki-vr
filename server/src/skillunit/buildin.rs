//! The script commands of the skill unit system (script.cpp): warpportal.

use crate::script::buildins::{lat, BuildinFn};
use crate::script::vm::*;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "warpportal" => warpportal,
        _ => return None,
    })
}

/// script.cpp BUILDIN_FUNC(warpportal): `warpportal <source x>,<source y>,"<target map>",<target x>,<target y>;` opens a
/// level 4 Warp Portal of the NPC at once (no "portal opening" stage). The group's val3 holds the target map as the
/// module's map id (rAthena: its map index; every map is on this server).
fn warpportal(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let Some(bl) = crate::unit::get(c, st.oid) else {
        log::error!("buildin_warpportal: NPC is needed");
        return SCRIPT_CMD_FAILURE;
    };
    let spx = getnum(rt, st, 2);
    let spy = getnum(rt, st, 3);
    let mapname = lat(&getstr(rt, st, 4));
    let tpx = getnum(rt, st, 5);
    let tpy = getnum(rt, st, 6);
    let Some(mapindex) = crate::data::maps().id_of(&mapname) else {
        log::error!("buildin_warpportal: Target map not found {mapname}.");
        return SCRIPT_CMD_FAILURE;
    };
    // A floating NPC (m -1) has no map data: skill_unitsetting would read mapdata->xs through a null pointer in
    // rAthena; here the unit setting refuses it.
    if c.db().npc().id().find(bl.id).map(|n| n.map < 0).unwrap_or(false) {
        log::error!("buildin_warpportal: NPC {} is not on a map", bl.id);
        return SCRIPT_CMD_FAILURE;
    }
    let Some(gid) = super::setting::skill_unitsetting(c, &bl, crate::db::skills::AL_WARP, 4, spx as i16, spy as i16, 0) else {
        return SCRIPT_CMD_FAILURE; // failed
    };
    super::data::upd_group(c, gid, |g| {
        g.val1 = (g.val1 << 16) | 0;
        g.val2 = (tpx << 16) | tpy;
        g.val3 = mapindex as i32;
    });
    SCRIPT_CMD_SUCCESS
}

use crate::npc::npc as _;
