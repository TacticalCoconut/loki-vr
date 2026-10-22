//! clif.cpp parse functions of the inventory: clif_parse_UseItem, clif_parse_EquipItem, clif_parse_UnequipItem and
//! the equip switch window (clif_parse_equipswitch_add / _remove / _request / _request_single).

use crate::clif;
use crate::clif_items as ci;
use crate::db::items as itemdb;
use crate::inventory::*;
use crate::net::packets::packet_pos;
use crate::pc;
use crate::unit;
use crate::world::Ctx;

/// conf/battle/feature.conf `feature.equipswitch: on`.
fn feature_equipswitch() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_EQUIPSWITCH) != 0
}
/// pc.hpp IDLE_USEITEM (0x0008), tested against player.conf idletime_option.
const IDLE_USEITEM: i32 = 0x0008;

fn rd_u16(pkt: &[u8], off: usize) -> Option<u16> {
    pkt.get(off..off + 2).map(|b| u16::from_le_bytes([b[0], b[1]]))
}
fn rd_u32(pkt: &[u8], off: usize) -> Option<u32> {
    pkt.get(off..off + 4).map(|b| u32::from_le_bytes([b[0], b[1], b[2], b[3]]))
}
fn pos0(pkt: &[u8]) -> usize {
    packet_pos(u16::from_le_bytes([pkt[0], pkt[1]])).first().copied().unwrap_or(2) as usize
}

fn touch_idle(c: &Ctx, aid: u32) {
    if crate::conf::idletime_option() & IDLE_USEITEM != 0 {
        if let Some(mut p) = pc::get(c, aid) {
            p.idletime = crate::pc::last_tick(c);
            pc::put(c, &p);
        }
    }
}

/// clif.cpp clif_parse_UseItem (CZ_USE_ITEM2).
pub fn use_item(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = pc::get(c, aid) else { return };
    let Some(sd) = unit::get(c, aid) else { return };
    if p.dead_sit == 1 {
        clif::clearunit_area(c, &sd, unit::CLR_DEAD);
        return;
    }
    if (p.npc_id == 0 && pc_istrading(&p)) || p.chat_id != 0 || p.block_action & pc::PCBLOCK_USEITEM != 0 {
        ci::msg(c, &sd, ci::MSI_BUSY);
        return;
    }
    // Whether the item is used or not is irrelevant, the char ain't idle. [Skotlex]
    touch_idle(c, aid);
    let n = rd_u16(pkt, pos0(pkt)).unwrap_or(0) as i32 - 2;
    if n < 0 || n as usize >= MAX_INVENTORY {
        return;
    }
    if pc_useitem(c, aid, n as usize) == 0 {
        ci::useitemack(c, aid, n, 0, false); // Send an empty ack packet or the client gets stuck.
    }
}

/// clif.cpp clif_parse_EquipItem (CZ_REQ_WEAR_EQUIP).
pub fn equip_item(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = pc::get(c, aid) else { return };
    let Some(sd) = unit::get(c, aid) else { return };
    if p.dead_sit == 1 {
        clif::clearunit_area(c, &sd, unit::CLR_DEAD);
        return;
    }
    let (Some(index), Some(position)) = (rd_u16(pkt, 2), rd_u32(pkt, 4)) else { return };
    let index = index.wrapping_sub(2);
    if index as usize >= MAX_INVENTORY {
        return;
    }
    let npc_item_flag = crate::script::npc_run::sdx(c, aid).npc_item_flag;
    if (p.npc_id != 0 && !npc_item_flag) || p.block_action & pc::PCBLOCK_EQUIP != 0 {
        // color_table[COLOR_RED]
        ci::msg_color(c, &sd, ci::MSI_CAN_NOT_EQUIP_ITEM, crate::social::COLOR_RED);
        return;
    } else if p.storage_flag != 0 || sd.sc.opt1 != 0 {
        // You can equip/unequip stuff while storage is open/under status changes
    } else if pc_cant_act2(&p, sd.sc.opt1) {
        return;
    }
    // an empty slot is a zeroed struct item: its identify is 0 too, so it gets the fail ack before the
    // inventory_data test (clif.cpp:12135-12141)
    let Some(r) = inv_get(c, aid, index as usize).filter(|r| r.item.identify != 0) else {
        ci::equipitemack(c, aid, ITEM_EQUIP_ACK_FAIL, index as i32, 0); // fail
        return;
    };
    let id = data_of(&r.item);
    if id.r#type == itemdb::IT_PETARMOR {
        crate::pet::pet_equipitem(c, aid, index as i32);
        return;
    }
    touch_idle(c, aid);
    // Client doesn't send the position for ammo.
    if id.r#type == itemdb::IT_AMMO {
        crate::pc_equip::pc_equipitem(c, aid, index as i16, EQP_AMMO, false);
    } else {
        crate::pc_equip::pc_equipitem(c, aid, index as i16, position, false);
    }
}

/// clif.cpp clif_parse_UnequipItem (CZ_REQ_TAKEOFF_EQUIP).
pub fn unequip_item(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = pc::get(c, aid) else { return };
    let Some(sd) = unit::get(c, aid) else { return };
    if p.dead_sit == 1 {
        clif::clearunit_area(c, &sd, unit::CLR_DEAD);
        return;
    }
    let npc_item_flag = crate::script::npc_run::sdx(c, aid).npc_item_flag;
    if (p.npc_id != 0 && !npc_item_flag) || p.block_action & pc::PCBLOCK_EQUIP != 0 {
        ci::msg_color(c, &sd, ci::MSI_CAN_NOT_EQUIP_ITEM, crate::social::COLOR_RED);
        return;
    } else if p.storage_flag != 0 || sd.sc.opt1 != 0 {
        // You can equip/unequip stuff while storage is open/under status changes
    } else if pc_cant_act2(&p, sd.sc.opt1) {
        return;
    }
    let index = rd_u16(pkt, pos0(pkt)).unwrap_or(0) as i32 - 2;
    touch_idle(c, aid);
    crate::pc_equip::pc_unequipitem(c, aid, index, 1);
}

/// clif.cpp clif_parse_equipswitch_add (0x0a97).
pub fn equipswitch_add(c: &Ctx, aid: u32, pkt: &[u8]) {
    let (Some(index), Some(mut position)) = (rd_u16(pkt, 2), rd_u32(pkt, 4)) else { return };
    let index = index.wrapping_sub(2);
    if !feature_equipswitch() {
        return;
    }
    let Some(r) = inv_get(c, aid, index as usize) else { return };
    let Some(p) = pc::get(c, aid) else { return };
    if p.trading || p.npc_shopid != 0 {
        ci::equipswitch_add(c, aid, index, position, ITEM_EQUIP_ACK_OK);
        return;
    }
    if data_of(&r.item).r#type == itemdb::IT_AMMO {
        position = EQP_AMMO;
    }
    crate::pc_equip::pc_equipitem(c, aid, index as i16, position, true);
}

/// clif.cpp clif_parse_equipswitch_remove (0x0a99).
pub fn equipswitch_remove(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(index) = rd_u16(pkt, 2) else { return };
    let index = index.wrapping_sub(2);
    if !feature_equipswitch() {
        return;
    }
    if index as usize >= MAX_INVENTORY {
        return;
    }
    crate::pc_equip::pc_equipswitch_remove(c, aid, index as usize);
}

/// clif.cpp clif_parse_equipswitch_request (0x0a9c): the full switch is the skill ALL_EQSWITCH
/// (clif_parse_skill_toid; its class is skills/other/equipswitch.rs).
pub fn equipswitch_request(c: &Ctx, aid: u32) {
    let tick = c.tick();
    let Some(mut s) = st(c, aid) else { return };
    if tick - s.equipswitch_tick < 0 {
        return; // Client will not let you send a request
    }
    let cooldown = crate::db::skills::skill_get_cooldown(crate::db::skills::ALL_EQSWITCH, 1) as i64;
    s.equipswitch_tick = tick + cooldown;
    put_st(c, &s);
    if !feature_equipswitch() {
        return;
    }
    if !s.equip_switch_index.iter().any(|&i| i >= 0) {
        // client will show: "There is no item to replace." and should not even come here
        ci::equipswitch_reply(c, aid, false);
        return;
    }
    if pc_issit(c, aid) && !pc::setstand(c, aid, false) {
        return;
    }
    crate::skill::parse::clif_parse_skill_toid(c, aid, crate::db::skills::ALL_EQSWITCH, 1, aid);
}

/// clif.cpp clif_parse_equipswitch_request_single (0x0ace).
pub fn equipswitch_request_single(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(index) = rd_u16(pkt, 2) else { return };
    let index = index.wrapping_sub(2);
    if !feature_equipswitch() {
        return;
    }
    if index as usize >= MAX_INVENTORY {
        return;
    }
    let Some(r) = inv_get(c, aid, index as usize) else { return };
    if r.item.equip_switch != 0 {
        let Some(p) = pc::get(c, aid) else { return };
        if p.npc_id != 0 {
            // RENEWAL
            if crate::itemui::pc_hasprogress(c, aid, crate::itemui::WIP_DISABLE_SKILLITEM) {
                if let Some(sd) = unit::get(c, aid) {
                    ci::msg(c, &sd, ci::MSI_BUSY);
                }
                return;
            }
            if !crate::script::npc_run::sdx(c, aid).npc_item_flag {
                return;
            }
        }
        crate::pc_equip::pc_equipswitch(c, aid, index as usize);
        return;
    }
    let pos = crate::pc_equip::pc_equippoint(c, aid, &r.item);
    crate::pc_equip::pc_equipitem(c, aid, index as i16, pos, true);
}
