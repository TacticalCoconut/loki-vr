//! Card insertion: clif.cpp clif_use_card, clif_insert_card, clif_parse_UseCard, clif_parse_InsertCard; pc.cpp
//! pc_insert_card.

use crate::db::items::{self as itemdb, IT_ARMOR, IT_CARD, IT_WEAPON};
use crate::inventory::{self as inv, EQP_ACC, EQP_SHIELD, MAX_INVENTORY};
use crate::net::packets::*;
use crate::net::wire::Reader;
use crate::unit;
use crate::world::Ctx;

use super::server_index;

/// clif.cpp clif_use_card: ZC_ITEMCOMPOSITION_LIST (0x017b) — the client indexes of the unequipped, identified
/// weapons / armors with a free slot the card at `idx` fits; nothing is sent when there is none.
pub fn clif_use_card(c: &Ctx, aid: u32, idx: i32) {
    if idx < 0 || idx as usize >= MAX_INVENTORY {
        return; // Crash-fix from bad packets.
    }
    let Some(card) = inv::inv_get(c, aid, idx as usize) else { return };
    let card_data = inv::data_of(&card.item);
    if card_data.r#type != IT_CARD {
        return; // Avoid parsing invalid item indexes (no card/no item)
    }
    let ep = card_data.equip;
    let Some(sd) = unit::get(c, aid) else { return };
    let mut w: Vec<u8> = vec![0x7b, 0x01, 0, 0];
    let mut n = 0;
    for r in inv::inv_list(c, aid) {
        let id = inv::data_of(&r.item);
        let it = &r.item;
        if id.r#type != IT_WEAPON && id.r#type != IT_ARMOR {
            continue;
        }
        if itemdb::itemdb_isspecial(it.card0) {
            continue; // Can't slot it
        }
        if it.identify == 0 {
            continue; // Not identified
        }
        if id.equip & ep == 0 {
            continue; // Not equippable on this part.
        }
        if id.r#type == IT_WEAPON && ep == EQP_SHIELD {
            continue; // Shield card won't go on left weapon.
        }
        if id.r#type == IT_ARMOR && ep & EQP_ACC != 0 && ep & EQP_ACC != EQP_ACC && id.equip & EQP_ACC != ep & EQP_ACC {
            continue; // specific accessory-card can only be inserted to specific accessory.
        }
        let slots = id.slots as usize;
        if !(0..slots).any(|j| it.card(j) == 0) {
            continue; // No room
        }
        if it.equip > 0 {
            continue; // Do not check items that are already equipped
        }
        w.extend_from_slice(&((r.idx as u16).wrapping_add(2)).to_le_bytes());
        n += 1;
    }
    if n == 0 {
        return; // no item is available for card insertion
    }
    let len = w.len() as u16;
    w[2..4].copy_from_slice(&len.to_le_bytes());
    crate::clif::send(c, &w, &sd, crate::clif::Target::SelfOnly);
}

/// clif.cpp clif_insert_card: ZC_ACK_ITEMCOMPOSITION.
pub fn clif_insert_card(c: &Ctx, aid: u32, idx_equip: i32, idx_card: i32, failure: bool) {
    let Some(sd) = unit::get(c, aid) else { return };
    let p = PACKET_ZC_ACK_ITEMCOMPOSITION {
        packetType: PACKET_ZC_ACK_ITEMCOMPOSITION::ID as i16,
        equipIndex: (idx_equip as u16).wrapping_add(2),
        cardIndex: (idx_card as u16).wrapping_add(2),
        result: failure as u8,
    };
    crate::clif::send(c, &p.to_vec(), &sd, crate::clif::Target::SelfOnly);
}

/// clif.cpp clif_parse_UseCard (CZ_REQ_ITEMCOMPOSITION_LIST 0x017a).
pub fn clif_parse_use_card(c: &Ctx, aid: u32, pkt: &[u8]) {
    if crate::pc::get(c, aid).map(|p| p.trading).unwrap_or(true) {
        return;
    }
    let Some(p) = PACKET_CZ_REQ_ITEMCOMPOSITION_LIST::read(&mut Reader::new(pkt)) else { return };
    let idx = server_index(p.index);
    if idx as usize >= MAX_INVENTORY {
        return;
    }
    clif_use_card(c, aid, idx as i32);
}

/// clif.cpp clif_parse_InsertCard (CZ_REQ_ITEMCOMPOSITION 0x017c).
pub fn clif_parse_insert_card(c: &Ctx, aid: u32, pkt: &[u8]) {
    if crate::pc::get(c, aid).map(|p| p.trading).unwrap_or(true) {
        return;
    }
    let Some(p) = PACKET_CZ_REQ_ITEMCOMPOSITION::read(&mut Reader::new(pkt)) else { return };
    let card_idx = server_index(p.index_card);
    let equip_idx = server_index(p.index_equip);
    if card_idx as usize >= MAX_INVENTORY || equip_idx as usize >= MAX_INVENTORY {
        return;
    }
    pc_insert_card(c, aid, card_idx as i32, equip_idx as i32);
}

/// pc.cpp pc_insert_card: the card goes into the first free slot of an unequipped, identified weapon / armor it fits
/// (the card leaves the inventory without ZC_DELETE_ITEM_FROM_BODY: pc_delitem type 1).
pub fn pc_insert_card(c: &Ctx, aid: u32, idx_card: i32, idx_equip: i32) -> i32 {
    if idx_equip < 0 || idx_equip as usize >= MAX_INVENTORY || idx_card < 0 || idx_card as usize >= MAX_INVENTORY {
        return 0;
    }
    let Some(eq) = inv::inv_get(c, aid, idx_equip as usize) else { return 0 }; // Invalid item index.
    let Some(card) = inv::inv_get(c, aid, idx_card as usize) else { return 0 }; // Invalid card index.
    let item_eq = inv::data_of(&eq.item);
    let item_card = inv::data_of(&card.item);
    if eq.item.nameid == 0 || eq.item.amount < 1 {
        return 0; // target item missing
    }
    if card.item.nameid == 0 || card.item.amount < 1 {
        return 0; // target card missing
    }
    if item_eq.r#type != IT_WEAPON && item_eq.r#type != IT_ARMOR {
        return 0; // only weapons and armor are allowed
    }
    if item_card.r#type != IT_CARD {
        return 0; // must be a card
    }
    if eq.item.identify == 0 {
        return 0; // target must be identified
    }
    if itemdb::itemdb_isspecial(eq.item.card0) {
        return 0; // card slots reserved for other purposes
    }
    if item_eq.equip & item_card.equip == 0 {
        return 0; // card cannot be compounded on this item type
    }
    if item_eq.r#type == IT_WEAPON && item_card.equip == EQP_SHIELD {
        return 0; // attempted to place shield card on left-hand weapon.
    }
    if item_eq.r#type == IT_ARMOR
        && item_card.equip & EQP_ACC != 0
        && item_card.equip & EQP_ACC != EQP_ACC
        && item_eq.equip & EQP_ACC != item_card.equip & EQP_ACC
    {
        return 0; // specific accessory-card can only be inserted to specific accessory.
    }
    if eq.item.equip != 0 {
        return 0; // item must be unequipped
    }
    let slots = item_eq.slots as usize;
    let Some(i) = (0..slots).find(|&i| eq.item.card(i) == 0) else { return 0 }; // no free slots
    // remember the card id to insert
    let nameid = card.item.nameid;
    if inv::pc_delitem(c, aid, idx_card, 1, 1, 0, crate::map_log::LOG_TYPE_OTHER) == 1 {
        // failed
        clif_insert_card(c, aid, idx_equip, idx_card, true);
    } else {
        // success
        if let Some(mut eq) = inv::inv_get(c, aid, idx_equip as usize) {
            crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_OTHER, -1, &eq.item);
            eq.item.set_card(i, nameid);
            inv::inv_put(c, &eq);
            crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_OTHER, 1, &eq.item);
        }
        clif_insert_card(c, aid, idx_equip, idx_card, false);
    }
    0
}
