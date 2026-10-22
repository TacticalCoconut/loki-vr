//! Item appraisal: clif.cpp clif_item_identify_list, clif_item_identified, clif_parse_ItemIdentify,
//! clif_parse_Oneclick_Itemidentify; skill.cpp skill_identify; pc.cpp pc_identifyall. (MC_IDENTIFY, the skill that
//! opens the list, is phase 4; @identify opens it too.)

use crate::db::skills::ids::MC_IDENTIFY;
use crate::inventory::{self as inv, MAX_INVENTORY};
use crate::net::packets::*;
use crate::unit;
use crate::world::Ctx;

use super::*;

/// itemdb.hpp ITEMID_MAGNIFIER / ITEMID_NOVICE_MAGNIFIER.
pub const ITEMID_MAGNIFIER: u32 = 611;
pub const ITEMID_NOVICE_MAGNIFIER: u32 = 12325;

/// clif.cpp clif_item_identify_list: ZC_ITEMIDENTIFY_LIST (0x0177) with the client index of every unidentified item;
/// sent only when there is one, and then menuskill = MC_IDENTIFY (val = the count) and workinprogress = ALL.
pub fn clif_item_identify_list(c: &Ctx, aid: u32) {
    let Some(sd) = unit::get(c, aid) else { return };
    let mut w: Vec<u8> = vec![0x77, 0x01, 0, 0];
    let mut n = 0i32;
    for r in inv::inv_list(c, aid) {
        if r.item.nameid > 0 && r.item.identify == 0 {
            w.extend_from_slice(&crate::clif_items::client_index(r.idx as usize).to_le_bytes());
            n += 1;
        }
    }
    if n > 0 {
        let len = w.len() as u16;
        w[2..4].copy_from_slice(&len.to_le_bytes());
        crate::clif::send(c, &w, &sd, crate::clif::Target::SelfOnly);
        update(c, aid, |s| {
            s.menuskill_id = MC_IDENTIFY as i32;
            s.menuskill_val = n;
            s.workinprogress = WIP_DISABLE_ALL;
        });
    }
}

/// clif.cpp clif_item_identified: ZC_ACK_ITEMIDENTIFY (the index is client_index(idx) even for a bad one).
pub fn clif_item_identified(c: &Ctx, aid: u32, idx: i32, failure: bool) {
    let Some(sd) = unit::get(c, aid) else { return };
    let p = PACKET_ZC_ACK_ITEMIDENTIFY { packetType: PACKET_ZC_ACK_ITEMIDENTIFY::ID as i16, index: (idx as u16).wrapping_add(2), result: failure as u8 };
    crate::clif::send(c, &p.to_vec(), &sd, crate::clif::Target::SelfOnly);
}

/// skill.cpp skill_identify: identifies the item, answers, and on success sets `@identify_idx` and runs the
/// OnPCIdentifyEvent scripts (NPCE_IDENTIFY).
pub fn skill_identify(c: &Ctx, aid: u32, idx: i32) {
    set_workinprogress(c, aid, WIP_DISABLE_NONE);
    let mut failure = true;
    if idx >= 0 && (idx as usize) < MAX_INVENTORY {
        if let Some(mut r) = inv::inv_get(c, aid, idx as usize) {
            if r.item.nameid > 0 && r.item.identify == 0 {
                failure = false;
                r.item.identify = 1;
                inv::inv_put(c, &r);
            }
        }
    }
    clif_item_identified(c, aid, idx, failure);
    if !failure {
        crate::script::reg::pc_setreg(c, aid, "@identify_idx", idx as i64);
        crate::script::npc_script_event(c, aid, crate::script::npc_run::NPCE_IDENTIFY);
    }
}

/// clif.cpp clif_parse_ItemIdentify (CZ_REQ_ITEMIDENTIFY 0x0178): the answer to the identify list; an invalid index,
/// an empty or already identified slot (the client's Cancel sends -1) only closes the list.
pub fn clif_parse_item_identify(c: &Ctx, aid: u32, pkt: &[u8]) {
    if menu(c, aid).0 != MC_IDENTIFY as i32 {
        return;
    }
    let Some(p) = PACKET_CZ_REQ_ITEMIDENTIFY::read(&mut crate::net::wire::Reader::new(pkt)) else { return };
    let idx = server_index(p.index);
    let bad = (idx as usize) >= MAX_INVENTORY
        || match inv::inv_get(c, aid, idx as usize) {
            None => true,
            // `!sd->inventory.u.items_inventory[idx].nameid || sd->inventory_data[idx] == nullptr || identify`
            Some(r) => r.item.nameid == 0 || crate::db::items::itemdb_exists(r.item.nameid).is_none() || r.item.identify != 0,
        };
    if bad {
        // cancel pressed
        set_workinprogress(c, aid, WIP_DISABLE_NONE);
        clif_menuskill_clear(c, aid);
        return;
    }
    skill_identify(c, aid, idx as i32);
    clif_menuskill_clear(c, aid);
}

/// clif.cpp clif_parse_Oneclick_Itemidentify (CZ_REQ_ONECLICK_ITEMIDENTIFY 0x0a35): identifies with one Magnifier
/// (or Novice Magnifier) from the inventory.
pub fn clif_parse_oneclick_itemidentify(c: &Ctx, aid: u32, pkt: &[u8]) {
    let pos = packet_pos(u16::from_le_bytes([pkt[0], pkt[1]])).first().copied().unwrap_or(2) as usize;
    let Some(b) = pkt.get(pos..pos + 2) else { return };
    let idx = u16::from_le_bytes([b[0], b[1]]) as i16 as i32 - 2;
    if idx < 0 || idx as usize >= MAX_INVENTORY {
        return;
    }
    match inv::inv_get(c, aid, idx as usize) {
        Some(r) if r.item.nameid != 0 && r.item.identify == 0 => {}
        _ => return,
    }
    let mut magnifier_idx = inv::pc_search_inventory(c, aid, ITEMID_MAGNIFIER);
    if magnifier_idx == -1 {
        magnifier_idx = inv::pc_search_inventory(c, aid, ITEMID_NOVICE_MAGNIFIER);
        if magnifier_idx == -1 {
            return;
        }
    }
    if inv::pc_delitem(c, aid, magnifier_idx as i32, 1, 0, 0, crate::map_log::LOG_TYPE_OTHER) != 0 {
        return; // Deleting of magnifier failed, for whatever reason...
    }
    skill_identify(c, aid, idx);
}

/// pc.cpp pc_identifyall: counts the unidentified items and, with `identify_item`, identifies each (one
/// ZC_ACK_ITEMIDENTIFY per item).
pub fn pc_identifyall(c: &Ctx, aid: u32, identify_item: bool) -> i32 {
    let mut unidentified = 0;
    for mut r in inv::inv_list(c, aid) {
        if r.item.nameid > 0 && r.item.identify != 1 {
            if identify_item {
                r.item.identify = 1;
                let idx = r.idx as i32;
                inv::inv_put(c, &r);
                clif_item_identified(c, aid, idx, false);
            }
            unidentified += 1;
        }
    }
    unidentified
}
