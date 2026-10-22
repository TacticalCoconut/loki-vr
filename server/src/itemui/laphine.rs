//! Laphine synthesis and upgrade boxes: clif.cpp clif_laphine_synthesis_open, clif_parse_laphine_synthesis_close,
//! clif_laphine_synthesis_result, clif_parse_laphine_synthesis, clif_laphine_upgrade_open,
//! clif_parse_laphine_upgrade_close, clif_laphine_upgrade_result, clif_item_preview, clif_parse_laphine_upgrade.

use crate::clif::{self, Target};
use crate::clif_items as ci;
use crate::db::items::laphine::{self as laphine_db, LaphineSynthesis, LaphineUpgrade};
use crate::db::items::{self as itemdb, DELAYCONSUME_NOCONSUME};
use crate::inventory::{self as inv, MAX_INVENTORY, MAX_ITEM_RDM_OPT, MAX_REFINE, MAX_SLOTS};
use crate::net::packets::*;
use crate::net::wire::Reader;
use crate::unit;
use crate::world::Ctx;

use super::*;

/// clif.cpp e_laphine_synthesis_result.
pub const LAPHINE_SYNTHESIS_SUCCESS: i16 = 0;
pub const LAPHINE_SYNTHESIS_AMOUNT: i16 = 5;
pub const LAPHINE_SYNTHESIS_ITEM: i16 = 7;

fn send_self(c: &Ctx, aid: u32, b: &[u8]) {
    if let Some(sd) = unit::get(c, aid) {
        clif::send(c, b, &sd, Target::SelfOnly);
    }
}

/// clif.cpp clif_laphine_synthesis_open: laphine_synthesis = the box, then ZC_RANDOM_COMBINE_ITEM_UI_OPEN.
pub fn clif_laphine_synthesis_open(c: &Ctx, aid: u32, synthesis: &LaphineSynthesis) {
    update(c, aid, |s| s.laphine_synthesis = synthesis.item_id);
    let p = PACKET_ZC_RANDOM_COMBINE_ITEM_UI_OPEN { packetType: PACKET_ZC_RANDOM_COMBINE_ITEM_UI_OPEN::ID as i16, itemId: itemdb::client_nameid(synthesis.item_id) as i32 };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_parse_laphine_synthesis_close (CZ_RANDOM_COMBINE_ITEM_UI_CLOSE 0x0a70).
pub fn clif_parse_laphine_synthesis_close(c: &Ctx, aid: u32) {
    update(c, aid, |s| s.laphine_synthesis = 0);
}

/// clif.cpp clif_laphine_synthesis_result: ZC_ACK_RANDOM_COMBINE_ITEM.
pub fn clif_laphine_synthesis_result(c: &Ctx, aid: u32, result: i16) {
    let p = PACKET_ZC_ACK_RANDOM_COMBINE_ITEM { packetType: PACKET_ZC_ACK_RANDOM_COMBINE_ITEM::ID as i16, result };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_parse_laphine_synthesis (CZ_REQ_RANDOM_COMBINE_ITEM 0x0a4f): checks the offered items against the
/// box's requirements, consumes the box (when it opened the window) and the items, and hands out the reward group.
pub fn clif_parse_laphine_synthesis(c: &Ctx, aid: u32, pkt: &[u8]) {
    let st = state(c, aid);
    if st.laphine_synthesis == 0 {
        return;
    }
    let Some(p) = PACKET_CZ_REQ_RANDOM_COMBINE_ITEM::read(&mut Reader::new(pkt)) else { return };
    if st.laphine_synthesis != p.itemId as u32 {
        return;
    }
    let Some(synthesis) = laphine_db::laphine_synthesis(st.laphine_synthesis) else { return };
    // ( p->packetLength - sizeof( PACKET_CZ_REQ_RANDOM_COMBINE_ITEM ) ) / sizeof( sub )
    let count = (p.packetLength as i32 - 8) / 4;
    // Player sent more or less than actually required
    if count != synthesis.required_requirements as i32 {
        return;
    }
    let items = &p.items;
    // Check for duplicates
    for i in 0..items.len() {
        for j in i + 1..items.len() {
            if items[i].index == items[j].index {
                return;
            }
        }
    }
    for e in items {
        // int16 index = server_index( … ): a negative index is not refused by `>= MAX_INVENTORY` in rAthena (it
        // then reads before the inventory array); here it is an empty slot
        let index = server_index(e.index as u16) as i16;
        if index as i32 >= MAX_INVENTORY as i32 || index < 0 {
            return;
        }
        let Some(r) = slot(c, aid, index as usize) else { return };
        let it = &r.item;
        let Some(req) = synthesis.requirement(it.nameid) else {
            clif_laphine_synthesis_result(c, aid, LAPHINE_SYNTHESIS_ITEM);
            return;
        };
        if e.count as i32 != req.amount as i32 {
            clif_laphine_synthesis_result(c, aid, LAPHINE_SYNTHESIS_AMOUNT);
            return;
        }
        if (it.amount as i32) < req.amount as i32 {
            clif_laphine_synthesis_result(c, aid, LAPHINE_SYNTHESIS_AMOUNT);
            return;
        }
        if (it.refine as u16) < synthesis.minimum_refine || it.refine as u16 > synthesis.maximum_refine || it.equip != 0 || it.equip_switch != 0 {
            clif_laphine_synthesis_result(c, aid, LAPHINE_SYNTHESIS_ITEM);
            return;
        }
    }
    // If triggered from item
    if inv::st(c, aid).map(|s| s.itemid).unwrap_or(0) == st.laphine_synthesis {
        let index = inv::pc_search_inventory(c, aid, st.laphine_synthesis);
        if index < 0 {
            clif_laphine_synthesis_result(c, aid, LAPHINE_SYNTHESIS_ITEM);
            return;
        }
        let nameid = slot(c, aid, index as usize).map(|r| r.item.nameid).unwrap_or(0);
        if itemdb::itemdb_search(nameid).flag.delay_consume & DELAYCONSUME_NOCONSUME == 0 && inv::pc_delitem(c, aid, index as i32, 1, 0, 0, crate::map_log::LOG_TYPE_LAPHINE) != 0 {
            return;
        }
    }
    for e in items {
        let index = server_index(e.index as u16);
        if inv::pc_delitem(c, aid, index as i32, e.count as i32, 0, 0, crate::map_log::LOG_TYPE_LAPHINE) != 0 {
            return;
        }
    }
    crate::itemgroup::pc_get_itemgroup(c, synthesis.reward_group_id, true, aid);
    clif_laphine_synthesis_result(c, aid, LAPHINE_SYNTHESIS_SUCCESS);
}

/// clif.cpp clif_laphine_upgrade_open: laphine_upgrade = the box, then ZC_RANDOM_UPGRADE_ITEM_UI_OPEN.
pub fn clif_laphine_upgrade_open(c: &Ctx, aid: u32, upgrade: &LaphineUpgrade) {
    update(c, aid, |s| s.laphine_upgrade = upgrade.item_id);
    let p = PACKET_ZC_RANDOM_UPGRADE_ITEM_UI_OPEN { packetType: PACKET_ZC_RANDOM_UPGRADE_ITEM_UI_OPEN::ID as i16, itemId: itemdb::client_nameid(upgrade.item_id) };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_parse_laphine_upgrade_close (CZ_RANDOM_UPGRADE_ITEM_UI_CLOSE 0x0ab5).
pub fn clif_parse_laphine_upgrade_close(c: &Ctx, aid: u32) {
    update(c, aid, |s| s.laphine_upgrade = 0);
}

/// clif.cpp clif_laphine_upgrade_result: ZC_ACK_RANDOM_UPGRADE_ITEM.
pub fn clif_laphine_upgrade_result(c: &Ctx, aid: u32, failed: bool) {
    let p = PACKET_ZC_ACK_RANDOM_UPGRADE_ITEM { packetType: PACKET_ZC_ACK_RANDOM_UPGRADE_ITEM::ID as i16, result: failed as u16 };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_item_preview: ZC_CHANGE_ITEM_OPTION with the item's new state.
pub fn clif_item_preview(c: &Ctx, aid: u32, index: usize) {
    let Some(r) = slot(c, aid, index) else { return };
    let it = &r.item;
    let p = PACKET_ZC_CHANGE_ITEM_OPTION {
        packetType: PACKET_ZC_CHANGE_ITEM_OPTION::ID as i16,
        index: ci::client_index(index) as i16,
        isDamaged: (it.attribute != 0) as i8,
        slot: ci::addcards(Some(it)),
        option_data: ci::add_random_options(it),
        refiningLevel: it.refine,
        grade: it.enchantgrade,
    };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_parse_laphine_upgrade (CZ_REQ_RANDOM_UPGRADE_ITEM 0x0ab6): consumes the box, gives the target item
/// new random options and a new refine, and previews it.
pub fn clif_parse_laphine_upgrade(c: &Ctx, aid: u32, pkt: &[u8]) {
    let st = state(c, aid);
    if st.laphine_upgrade == 0 {
        return;
    }
    let Some(p) = PACKET_CZ_REQ_RANDOM_UPGRADE_ITEM::read(&mut Reader::new(pkt)) else { return };
    if st.laphine_upgrade != p.itemId {
        return;
    }
    let Some(upgrade) = laphine_db::laphine_upgrade(st.laphine_upgrade) else { return };
    let index = server_index(p.index);
    if index as usize >= MAX_INVENTORY {
        return;
    }
    let idx = index as usize;
    let Some(r) = slot(c, aid, idx) else { return };
    let it = &r.item;
    // Not a valid target item; not identified; equipped; in the equip switch; refine out of range
    if !upgrade.target_item_ids.contains(&it.nameid)
        || it.identify == 0
        || it.equip != 0
        || it.equip_switch != 0
        || (it.refine as u16) < upgrade.minimum_refine
        || it.refine as u16 > upgrade.maximum_refine
    {
        clif_laphine_upgrade_result(c, aid, true);
        return;
    }
    // If no cards are allowed
    if !upgrade.cards_allowed && (0..MAX_SLOTS).any(|i| it.card(i) != 0) {
        clif_laphine_upgrade_result(c, aid, true);
        return;
    }
    // If random options are required
    if upgrade.required_random_options > 0 {
        let n = (0..MAX_ITEM_RDM_OPT).rev().find(|&i| it.opt(i).id != 0).map(|i| i as i32 + 1).unwrap_or(0);
        if n < upgrade.required_random_options as i32 {
            clif_laphine_upgrade_result(c, aid, true);
            return;
        }
    }
    let index2 = inv::pc_search_inventory(c, aid, st.laphine_upgrade);
    if index2 < 0 {
        clif_laphine_upgrade_result(c, aid, true);
        return;
    }
    let box_id = slot(c, aid, index2 as usize).map(|r| r.item.nameid).unwrap_or(0);
    if itemdb::itemdb_search(box_id).flag.delay_consume & DELAYCONSUME_NOCONSUME == 0 && inv::pc_delitem(c, aid, index2 as i32, 1, 0, 0, crate::map_log::LOG_TYPE_LAPHINE) != 0 {
        return;
    }
    let Some(mut r) = slot(c, aid, idx) else { return };
    // Log removal of item
    crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_LAPHINE, -1, &r.item);
    // Visually remove it from the client
    ci::delitem(c, aid, idx as i32, 1, 0);
    // Apply the random options
    if let Some(g) = upgrade.random_option_group {
        super::reform::apply_random_option_group(c, g, &mut r.item);
    }
    // Change the refine rate if needed
    if upgrade.result_refine > 0 {
        // Absolute refine level change
        r.item.refine = (r.item.refine as u16).max(upgrade.result_refine) as u8;
    } else if upgrade.result_refine_maximum > 0 {
        if upgrade.result_refine_minimum != 0 {
            // If a minimum is specified it can also downgrade
            r.item.refine = c.rnd_value(upgrade.result_refine_minimum as i32, upgrade.result_refine_maximum as i32) as u16 as u8;
        } else {
            // Otherwise it can only be upgraded until the maximum, but not downgraded
            r.item.refine = c.rnd_value(r.item.refine as i32, upgrade.result_refine_maximum as i32) as u16 as u8;
        }
    } else if upgrade.result_refine_minimum > 0 {
        // No maximum has been specified, so it can be anything between minimum and MAX_REFINE
        r.item.refine = c.rnd_value(upgrade.result_refine_minimum as i32, MAX_REFINE) as u16 as u8;
    }
    inv::inv_put(c, &r);
    // Log retrieving the item again -> with the new options
    crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_LAPHINE, 1, &r.item);
    // Make it visible for the client again
    ci::additem(c, aid, idx as i32, 1, 0);
    // Open a preview of the item
    clif_item_preview(c, aid, idx);
    // Tell the client we are done
    clif_laphine_upgrade_result(c, aid, false);
}
