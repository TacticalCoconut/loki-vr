//! Weapon repair: clif.cpp clif_item_repair_list, clif_item_repaireffect, clif_parse_RepairItem; skill.cpp
//! skill_repairweapon. (BS_REPAIRWEAPON, the skill that opens the list, is phase 4.)

use crate::clif::{self, Target};
use crate::clif_items as ci;
use crate::db::items::{self as itemdb, IT_ARMOR, IT_WEAPON};
use crate::db::skills::ids::BS_REPAIRWEAPON;
use crate::inventory::{self as inv, MAX_INVENTORY};
use crate::net::packets::*;
use crate::net::wire::Reader;
use crate::unit;
use crate::world::Ctx;

use super::*;

/// itemdb.hpp ITEMID_IRON_ORE, ITEMID_IRON, ITEMID_STEEL, ITEMID_ORIDECON_STONE.
const ITEMID_IRON_ORE: u32 = 1002;
const ITEMID_IRON: u32 = 998;
const ITEMID_STEEL: u32 = 999;
const ITEMID_ORIDECON_STONE: u32 = 756;

/// clif.cpp clif_item_repair_list: ZC_REPAIRITEMLIST of `dst`'s broken items (the index field carries the *server*
/// index, as rAthena writes it), sent to `sd`; menuskill BS_REPAIRWEAPON (val = dst, val2 = lv), or a skill failure
/// when nothing is broken.
pub fn clif_item_repair_list(c: &Ctx, aid: u32, dst: u32, lv: u16) {
    let Some(sd) = unit::get(c, aid) else { return };
    let mut p = PACKET_ZC_REPAIRITEMLIST { packetType: PACKET_ZC_REPAIRITEMLIST::ID as i16, ..Default::default() };
    for r in inv::inv_list(c, dst) {
        let it = &r.item;
        if it.nameid > 0 && it.attribute != 0 && !itemdb::itemdb_ishatched_egg(it.card0, it.attribute) {
            p.items.push(REPAIRITEM_INFO2 {
                index: r.idx as i16,
                itemId: itemdb::client_nameid(it.nameid),
                refine: it.refine,
                slot: ci::addcards(Some(it)),
                grade: 0,
            });
        }
    }
    if !p.items.is_empty() {
        clif::send(c, &p.to_vec(), &sd, Target::SelfOnly);
        update(c, aid, |s| {
            s.menuskill_id = BS_REPAIRWEAPON as i32;
            s.menuskill_val = dst as i32;
            s.menuskill_val2 = lv as i32;
        });
    } else {
        crate::skill::skill_fail(c, &sd, sd.ud.skill_id, 0);
    }
}

/// clif.cpp clif_item_repaireffect: ZC_ACK_ITEMREPAIR.
pub fn clif_item_repaireffect(c: &Ctx, aid: u32, idx: i32, failure: bool) {
    let Some(sd) = unit::get(c, aid) else { return };
    let p = PACKET_ZC_ACK_ITEMREPAIR { packetType: PACKET_ZC_ACK_ITEMREPAIR::ID as i16, index: (idx as u16).wrapping_add(2), result: failure as u8 };
    clif::send(c, &p.to_vec(), &sd, Target::SelfOnly);
}

/// clif.cpp clif_skill_nodamage(src, dst, skill_id, heal) with success = true: ZC_USE_SKILL to dst's area (the
/// same function as battle/atcommand.rs clif_skill_nodamage).
fn clif_skill_nodamage(c: &Ctx, src: &unit::Unit, dst: &unit::Unit, skill_id: u16, heal: i32) {
    crate::battle::atcommand::clif_skill_nodamage(c, Some(src), dst, skill_id, heal, true);
}

/// clif.cpp clif_parse_RepairItem (CZ_REQ_ITEMREPAIR2 0x0b66 / 0x01fd).
pub fn clif_parse_repair_item(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_REQ_ITEMREPAIR2::read(&mut Reader::new(pkt)) else { return };
    if menu(c, aid).0 != BS_REPAIRWEAPON as i32 {
        return;
    }
    let Some(pc) = crate::pc::get(c, aid) else { return };
    if inv::pc_istrading(&pc) {
        // Make it fail to avoid shop exploits where you sell something different than you see.
        if let Some(sd) = unit::get(c, aid) {
            crate::skill::skill_fail(c, &sd, sd.ud.skill_id, 0);
        }
        clif_menuskill_clear(c, aid);
        return;
    }
    skill_repairweapon(c, aid, p.item.index as i32);
    clif_menuskill_clear(c, aid);
}

/// skill.cpp skill_repairweapon: repairs a broken weapon / armor of the menu's target with the ore of its level.
pub fn skill_repairweapon(c: &Ctx, aid: u32, idx: i32) {
    // weapon_materials[MAX_WEAPON_LEVEL], armor_materials[MAX_ARMOR_LEVEL] (RENEWAL: none for the last level)
    const WEAPON_MATERIALS: [u32; 5] = [ITEMID_IRON_ORE, ITEMID_IRON, ITEMID_STEEL, ITEMID_ORIDECON_STONE, 0];
    const ARMOR_MATERIALS: [u32; 2] = [ITEMID_STEEL, 0];
    let (menuskill_id, menuskill_val, _menuskill_val2) = menu(c, aid);
    let target = menuskill_val as u32;
    if crate::pc::get(c, target).is_none() {
        return; // Failed....
    }
    if idx == 0xFFFF {
        return; // No item selected ('Cancel' clicked)
    }
    if idx < 0 || idx as usize >= MAX_INVENTORY {
        return; // Invalid index??
    }
    let Some(r) = inv::inv_get(c, target, idx as usize) else { return };
    if r.item.nameid == 0 || r.item.attribute == 0 {
        return; // Again invalid item....
    }
    if itemdb::itemdb_ishatched_egg(r.item.card0, r.item.attribute) {
        return;
    }
    let (Some(sd), Some(tsd)) = (unit::get(c, aid), unit::get(c, target)) else { return };
    if sd.char_id != tsd.char_id {
        // battle_check_range(&sd, target_sd, skill_get_range2(&sd, menuskill_id, menuskill_val2, true)): the range
        // check belongs to the battle agent and skill ranges to phase 4; until both exist another player's item is
        // refused as if out of range (only BS_REPAIRWEAPON, a phase-4 skill, opens a list for someone else).
        clif_item_repaireffect(c, aid, idx, true);
        return;
    }
    let id = inv::data_of(&r.item);
    let material = if id.r#type == IT_WEAPON {
        (id.weapon_level as usize).checked_sub(1).and_then(|l| WEAPON_MATERIALS.get(l)).copied().unwrap_or(0)
    } else if id.r#type == IT_ARMOR {
        (id.armor_level as usize).checked_sub(1).and_then(|l| ARMOR_MATERIALS.get(l)).copied().unwrap_or(0)
    } else {
        0
    };
    if material == 0 || inv::pc_search_inventory(c, aid, material) < 0 {
        crate::skill::skill_fail(c, &sd, menuskill_id as u16, 0);
        return;
    }
    clif_skill_nodamage(c, &sd, &tsd, menuskill_id as u16, 1);
    let mut r = r;
    r.item.attribute = 0; // clear broken state
    inv::inv_put(c, &r);
    ci::equiplist(c, target);
    let m = inv::pc_search_inventory(c, aid, material);
    inv::pc_delitem(c, aid, m as i32, 1, 0, 0, crate::map_log::LOG_TYPE_CONSUME);
    clif_item_repaireffect(c, aid, idx, false);
    if sd.char_id != tsd.char_id {
        clif_item_repaireffect(c, target, idx, false);
    }
}
