//! Unit control commands (script.cpp "[zBuffer] List of unit control commands" and the player control ones before
//! them): unittalk, setpcblock / getpcblock, pcblockmove / unitblockmove, pcblockskill / unitblockskill, hateffect /
//! has_hateffect.

use crate::script::buildins::*;
use crate::clif::Target;
use crate::map::BL_PC;
use crate::npc::{npc, Npc};
use crate::pc_misc;
use crate::script::vm::*;
use crate::unit::{self, Unit};
use crate::world::Ctx;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "unittalk" => unittalk,
        "setpcblock" => setpcblock,
        "getpcblock" => getpcblock,
        "pcblockmove" => pcblockmove,
        "pcblockskill" => pcblockskill,
        "hateffect" => hateffect,
        "has_hateffect" => has_hateffect,
        _ => return None,
    })
}

/// What map_id2bl finds: a unit with a `unit` row, or a floating NPC (npc_data without a map, so without a unit row
/// here; rAthena keeps it in id_db all the same).
#[derive(Clone, Debug)]
pub enum Bl {
    Unit(Unit),
    FloatingNpc(Npc),
}

impl Bl {
    pub fn id(&self) -> u32 {
        match self {
            Bl::Unit(u) => u.id,
            Bl::FloatingNpc(n) => n.id,
        }
    }
    pub fn kind(&self) -> u16 {
        match self {
            Bl::Unit(u) => u.kind,
            Bl::FloatingNpc(_) => crate::map::BL_NPC,
        }
    }
    pub fn unit(&self) -> Option<&Unit> {
        match self {
            Bl::Unit(u) => Some(u),
            Bl::FloatingNpc(_) => None,
        }
    }
}

/// map.cpp map_id2bl: the objects of id_db. Warp NPCs are never put there (npc_parse_warp calls no map_addiddb), so
/// they are not found, as in rAthena.
pub fn map_id2bl(c: &Ctx, id: u32) -> Option<Bl> {
    if let Some(u) = unit::get(c, id) {
        if u.kind == crate::map::BL_NPC && c.db().npc().id().find(id).map(|n| n.subtype == crate::npc::NPCTYPE_WARP).unwrap_or(false) {
            return None;
        }
        return Some(Bl::Unit(u));
    }
    c.db().npc().id().find(id).filter(|n| n.subtype != crate::npc::NPCTYPE_WARP).map(Bl::FloatingNpc)
}

/// unit.cpp unit_bl2ud: the unit types that have a unit_data (players, monsters, pets, homunculi, mercenaries,
/// elementals, NPCs).
pub fn has_ud(kind: u16) -> bool {
    use crate::map::*;
    matches!(kind, BL_PC | BL_MOB | BL_PET | BL_NPC | BL_HOM | BL_MER | BL_ELEM)
}

/// script.cpp script_rid2bl_: the unit whose id is argument `loc` (missing or 0: the attached player).
pub fn script_rid2bl(rt: &Rt, st: &mut State, loc: i32, func: &str) -> Option<Bl> {
    let mut unit_id = if st.hasdata(loc) { getnum(rt, st, loc) as u32 } else { 0 };
    if unit_id == 0 {
        unit_id = st.rid;
    }
    let bl = rt.c.and_then(|c| map_id2bl(c, unit_id));
    if bl.is_none() {
        log::error!("{func}: Unit with ID '{}' is not found.", unit_id as i32);
    }
    bl
}

/// script.cpp script_mapid2sd_: the player whose account id is argument `loc`, or the attached one.
pub fn script_mapid2sd(rt: &Rt, st: &mut State, loc: i32, func: &str) -> Option<u32> {
    if st.hasdata(loc) {
        let id = getnum(rt, st, loc);
        let ok = rt.c.map(|c| crate::pc::get(c, id as u32).is_some()).unwrap_or(false);
        if !ok {
            log::error!("{func}: Player with map id '{id}' is not found.");
            return None;
        }
        Some(id as u32)
    } else {
        rid2sd(rt, st, func)
    }
}

/// unittalk <unit_id>,"<message>"{,<flag>}: the unit says the message (clif_disp_overhead_); flag bc_self shows it
/// only to the player itself.
fn unittalk(rt: &Rt, st: &mut State) -> i32 {
    let message = getstr(rt, st, 3);
    if let Some(bl) = script_rid2bl(rt, st, 2, "buildin_unittalk") {
        let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
        let mut target = Target::Area;
        if st.hasdata(4) && getnum(rt, st, 4) == BC_SELF {
            if crate::pc::get(c, bl.id()).is_none() {
                log::warn!("script: unittalk: bc_self can't be used for non-players objects.");
                return SCRIPT_CMD_FAILURE;
            }
            target = Target::SelfOnly;
        }
        // StringBuf_Printf(&sbuf, "%s", message)
        match &bl {
            Bl::Unit(u) => crate::clif_misc::disp_overhead(c, u, &message, target),
            // a floating NPC is on no map: clif_send's area search finds nobody
            Bl::FloatingNpc(_) => {}
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// clif.hpp e_broadcast_flags BC_SELF.
pub const BC_SELF: i32 = 3;

/// setpcblock <type>,<state>{,<account ID>}: sets or clears PCBLOCK_* bits (sd->state.block_action).
fn setpcblock(rt: &Rt, st: &mut State) -> i32 {
    if let Some(aid) = script_mapid2sd(rt, st, 4, "buildin_setpcblock") {
        let kind = getnum(rt, st, 2) as u32;
        let on = getnum(rt, st, 3) > 0;
        let c = rt.c.unwrap();
        if let Some(mut p) = crate::pc::get(c, aid) {
            if on {
                p.block_action |= kind;
            } else {
                p.block_action &= !kind;
            }
            crate::pc::put(c, &p);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// getpcblock {<account ID>}.
fn getpcblock(rt: &Rt, st: &mut State) -> i32 {
    match script_mapid2sd(rt, st, 2, "buildin_getpcblock") {
        Some(aid) => {
            let v = rt.c.and_then(|c| crate::pc::get(c, aid)).map(|p| p.block_action).unwrap_or(0);
            st.pushint(v as i64);
        }
        None => st.pushint(0),
    }
    SCRIPT_CMD_SUCCESS
}

/// The unit of pcblockmove / pcblockskill: argument 2 (0: the attached player).
fn block_target(rt: &Rt, st: &mut State) -> Option<Bl> {
    let c = rt.c?;
    let id = getnum(rt, st, 2);
    if id != 0 {
        map_id2bl(c, id as u32)
    } else {
        map_id2bl(c, st.rid)
    }
}

/// pcblockmove / unitblockmove <unit_id>,<option>: ud->state.blockedmove.
fn pcblockmove(rt: &Rt, st: &mut State) -> i32 {
    if let Some(Bl::Unit(mut u)) = block_target(rt, st) {
        if has_ud(u.kind) {
            u.ud.blockedmove = getnum(rt, st, 3) > 0;
            unit::save(rt.c.unwrap(), &u);
        }
    }
    // (a floating NPC's unit_data has no row here; it cannot move either way)
    SCRIPT_CMD_SUCCESS
}

/// pcblockskill / unitblockskill <unit_id>,<option>: ud->state.blockedskill (read by skill use, phase 4).
fn pcblockskill(rt: &Rt, st: &mut State) -> i32 {
    if let Some(Bl::Unit(mut u)) = block_target(rt, st) {
        if has_ud(u.kind) {
            u.ud.blockedskill = getnum(rt, st, 3) > 0;
            unit::save(rt.c.unwrap(), &u);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// script.hpp e_hat_effects: HAT_EF_MIN is 0 and HAT_EF_MAX follows the last effect (HAT_EF_C_ANGEL_GIFT).
fn hat_ef_max() -> i32 {
    crate::data::constant("HAT_EF_C_ANGEL_GIFT").map(|v| v as i32 + 1).unwrap_or(0)
}

/// hateffect <effect id>,<state>{,<unit id>} (PACKETVER_RE_NUM >= 20150429): ud->hatEffects and
/// clif_hat_effect_single (not while a player is still loading: the effects go out with its spawn).
fn hateffect(rt: &Rt, st: &mut State) -> i32 {
    let Some(bl) = script_rid2bl(rt, st, 4, "buildin_hateffect") else { return SCRIPT_CMD_FAILURE };
    let c = rt.c.unwrap();
    let effect_id = getnum(rt, st, 2) as i16;
    let enable = getnum(rt, st, 3) != 0;
    if effect_id as i32 <= 0 || effect_id as i32 >= hat_ef_max() {
        log::error!("buildin_hateffect: unsupported hat effect id {effect_id}");
        return SCRIPT_CMD_FAILURE;
    }
    if !has_ud(bl.kind()) {
        log::error!("buildin_hateffect: unsupported unit type {}", bl.kind());
        return SCRIPT_CMD_FAILURE;
    }
    let mut effects = pc_misc::hat_effects(c, bl.id());
    if enable {
        if effects.contains(&effect_id) {
            return SCRIPT_CMD_SUCCESS;
        }
        effects.push(effect_id);
    } else {
        // util::vector_erase_if_exists
        match effects.iter().position(|&e| e == effect_id) {
            Some(i) => {
                effects.remove(i);
            }
            None => return SCRIPT_CMD_SUCCESS,
        }
    }
    pc_misc::set_hat_effects(c, bl.id(), effects);
    let connect_new = bl.kind() == BL_PC && crate::pc::get(c, bl.id()).map(|p| p.connect_new).unwrap_or(false);
    if !connect_new {
        if let Some(u) = bl.unit() {
            crate::clif_misc::hat_effect_single(c, u, effect_id as u16, enable);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// has_hateffect(<effect id>{,<unit id>}).
fn has_hateffect(rt: &Rt, st: &mut State) -> i32 {
    let Some(bl) = script_rid2bl(rt, st, 3, "buildin_has_hateffect") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    if !has_ud(bl.kind()) {
        log::error!("buildin_has_hateffect: unsupported unit type {}", bl.kind());
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    }
    let effect_id = getnum(rt, st, 2) as i16;
    if effect_id as i32 <= 0 || effect_id as i32 >= hat_ef_max() {
        log::error!("buildin_has_hateffect: unsupported hat effect id {effect_id}");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    }
    let has = pc_misc::hat_effects(rt.c.unwrap(), bl.id()).contains(&effect_id);
    st.pushint(has as i64);
    SCRIPT_CMD_SUCCESS
}
