//! Refining windows: the refine UI (clif.cpp clif_refineui_open, clif_parse_refineui_close, clif_refineui_info,
//! clif_parse_refineui_add, clif_parse_refineui_refine, clif_broadcast_refine_result) and the Whitesmith's weapon
//! refine list (clif.cpp clif_item_refine_list, clif_upgrademessage, clif_parse_WeaponRefine; skill.cpp
//! skill_weaponrefine — WS_WEAPONREFINE itself, which opens the list, is phase 4).
//!
//! Achievements (AG_ENCHANT_SUCCESS / AG_ENCHANT_FAIL) and fame (pc_addfame) are the extras agent's: their calls
//! are commented stubs at the places rAthena makes them.

use crate::clif::{self, Target};
use crate::clif_items as ci;
use crate::db::items::refine::{self as refine_db, REFINE_COST_NORMAL};
use crate::db::items::{self as itemdb, IT_WEAPON};
use crate::db::skills::ids::WS_WEAPONREFINE;
use crate::inventory::{self as inv, EQP_ARMS, MAX_INVENTORY, MAX_REFINE};
use crate::net::packets::*;
use crate::net::wire::Reader;
use crate::unit;
use crate::world::Ctx;

use super::*;

/// clif.hpp e_ack_itemrefining ITEMREFINING_FAILURE2.
pub const ITEMREFINING_FAILURE2: u16 = 3;
/// status.hpp REFINE_COST_MAX.
const REFINE_COST_MAX: u16 = 3;
/// itemdb.hpp ITEMID_BLACKSMITH_BLESSING, ITEMID_PHRACON, ITEMID_EMVERETARCON, ITEMID_ORIDECON.
pub const ITEMID_BLACKSMITH_BLESSING: u32 = 6635;
pub const ITEMID_PHRACON: u32 = 1010;
pub const ITEMID_EMVERETARCON: u32 = 1011;
pub const ITEMID_ORIDECON: u32 = 984;
/// script_constants ET_HUK.
const ET_HUK: u8 = 23;

fn send_self(c: &Ctx, aid: u32, b: &[u8]) {
    if let Some(sd) = unit::get(c, aid) {
        clif::send(c, b, &sd, Target::SelfOnly);
    }
}

// ---------------------------------------------------------------------------------------------- refine UI

/// clif.cpp clif_refineui_open: ZC_OPEN_REFINING_UI, then refineui_open.
pub fn clif_refineui_open(c: &Ctx, aid: u32) {
    let p = PACKET_ZC_OPEN_REFINING_UI { packetType: PACKET_ZC_OPEN_REFINING_UI::ID as i16 };
    send_self(c, aid, &p.to_vec());
    update(c, aid, |s| s.refineui_open = true);
}

/// clif.cpp clif_parse_refineui_close (CZ_CLOSE_REFINING_UI 0x0aa4).
pub fn clif_parse_refineui_close(c: &Ctx, aid: u32) {
    update(c, aid, |s| s.refineui_open = false);
}

/// clif.cpp clif_refineui_info: ZC_REFINING_MATERIAL_LIST for the item at `index` (nothing for an empty slot, an
/// unidentified or a broken item; no materials when the item cannot be refined further).
pub fn clif_refineui_info(c: &Ctx, aid: u32, index: u16) {
    let Some(r) = slot(c, aid, index as usize) else { return };
    let id = inv::data_of(&r.item);
    let item = &r.item;
    if item.identify == 0 {
        return; // Check if the item is identified
    }
    if item.attribute != 0 {
        return; // Check if the item is broken
    }
    let mut p = PACKET_ZC_REFINING_MATERIAL_LIST {
        packetType: PACKET_ZC_REFINING_MATERIAL_LIST::ID as i16,
        itemIndex: ci::client_index(index as usize) as i16,
        ..Default::default()
    };
    match refine_db::find_level_info(id, item.refine) {
        None => p.blacksmithBlessing = 0, // No possibilities were found
        Some(info) => {
            p.blacksmithBlessing = info.blessing_amount as i8;
            for i in REFINE_COST_NORMAL..REFINE_COST_MAX {
                let Some(cost) = info.cost(i) else { continue };
                p.req.push(PACKET_ZC_REFINING_MATERIAL_LIST_SUB {
                    itemId: itemdb::client_nameid(cost.nameid),
                    chance: (cost.chance / 100) as i8,
                    zeny: cost.zeny as i32,
                });
            }
        }
    }
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_parse_refineui_add (CZ_REFINING_SELECT_ITEM 0x0aa1).
pub fn clif_parse_refineui_add(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_REFINING_SELECT_ITEM::read(&mut Reader::new(pkt)) else { return };
    let index = server_index(p.index as u16);
    if !state(c, aid).refineui_open {
        return; // Check if the refine UI is open
    }
    if index as usize >= MAX_INVENTORY {
        return;
    }
    // Send out the requirements for the refine process
    clif_refineui_info(c, aid, index);
}

/// clif.cpp clif_broadcast_refine_result: ZC_BROADCAST_ITEMREFINING_RESULT to every client (the name hidden by
/// broadcast_hide_name; the item id is not converted with client_nameid).
pub fn clif_broadcast_refine_result(c: &Ctx, aid: u32, item_id: u32, level: i8, success: bool) {
    let Some(sd) = unit::get(c, aid) else { return };
    let name = crate::pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
    let dispname = if broadcast_hide_name() != 0 { clif_hide_name(&name) } else { name };
    let p = PACKET_ZC_BROADCAST_ITEMREFINING_RESULT {
        packetType: PACKET_ZC_BROADCAST_ITEMREFINING_RESULT::ID as i16,
        name: dispname,
        itemId: item_id,
        refine_level: level,
        status: success as i8,
    };
    clif::send(c, &p.to_vec(), &sd, Target::AllClient);
}

/// clif.cpp clif_parse_refineui_refine (CZ_REQ_REFINING 0x0aa3): pays the zeny, the material and the Blacksmith
/// Blessings, then rolls the refine: success +1; failure with blessings keeps the item; otherwise it may break,
/// go down, or stay. The item keeps being worn and the status is not recalculated (as in rAthena).
pub fn clif_parse_refineui_refine(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_REQ_REFINING::read(&mut Reader::new(pkt)) else { return };
    let index = server_index(p.index as u16);
    let material = p.itemId;
    if !state(c, aid).refineui_open {
        return;
    }
    if index as usize >= MAX_INVENTORY {
        return;
    }
    let idx = index as usize;
    let Some(r) = slot(c, aid, idx) else { return };
    let id = inv::data_of(&r.item);
    if r.item.identify == 0 {
        return;
    }
    if r.item.attribute != 0 {
        return;
    }
    let Some(info) = refine_db::find_level_info(id, r.item.refine) else { return }; // No refine possible
    if info.costs.is_empty() {
        return; // No possibilities were found
    }
    // Check if the player has the selected material
    let j = inv::pc_search_inventory(c, aid, material);
    if j < 0 {
        return;
    }
    let mut blacksmith_index: i16 = -1;
    let mut blacksmith_amount: u16 = 0;
    if p.blacksmithBlessing != 0 {
        blacksmith_amount = info.blessing_amount;
        // Player tried to use blacksmith blessing on a refine level, where it is not available
        if blacksmith_amount == 0 {
            return;
        }
        blacksmith_index = inv::pc_search_inventory(c, aid, ITEMID_BLACKSMITH_BLESSING);
        if blacksmith_index < 0 {
            return;
        }
        let have = slot(c, aid, blacksmith_index as usize).map(|b| b.item.amount).unwrap_or(0);
        if (have as i32) < blacksmith_amount as i32 {
            return;
        }
    }
    // The first cost with this material, in the unordered_map's iteration order
    let Some(cost) = info.costs.iter().find(|c| c.nameid == material) else { return };
    // Try to pay for the refine
    if inv::pc_payzeny(c, aid, cost.zeny as i64, crate::map_log::LOG_TYPE_CONSUME, 0) != 0 {
        crate::shop::clif_npc_buy_result(c, aid, crate::shop::PURCHASE_FAIL_MONEY); // "You do not have enough zeny."
        return;
    }
    // Delete the required material
    if inv::pc_delitem(c, aid, j as i32, 1, 0, 0, crate::map_log::LOG_TYPE_CONSUME) != 0 {
        return;
    }
    // Delete the required blacksmith blessings
    if blacksmith_amount > 0 && inv::pc_delitem(c, aid, blacksmith_index as i32, blacksmith_amount as i32, 0, 0, crate::map_log::LOG_TYPE_CONSUME) != 0 {
        return;
    }
    // Try to refine the item: cost->chance >= rnd() % 10000
    if cost.chance as u32 >= c.rnd() % 10000 {
        let Some(mut r) = slot(c, aid, idx) else { return };
        crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_OTHER, -1, &r.item);
        // Success
        r.item.refine = (r.item.refine as i32 + 1).clamp(0, MAX_REFINE) as u8;
        inv::inv_put(c, &r);
        crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_OTHER, 1, &r.item);
        misceffect(c, aid, ci::NOTIFYEFFECT_REFINE_SUCCESS as i32);
        ci::refine(c, aid, idx, ci::ITEMREFINING_SUCCESS);
        if info.broadcast_success {
            clif_broadcast_refine_result(c, aid, r.item.nameid, r.item.refine as i8, true);
        }
        if id.r#type == IT_WEAPON {
            crate::achievement::achievement_update_objective(c, Some(aid), crate::db::misc::achievement::AG_ENCHANT_SUCCESS, &[id.weapon_level as i32, r.item.refine as i32]);
        }
        clif_refineui_info(c, aid, index);
    } else {
        // Failure
        let Some(r) = slot(c, aid, idx) else { return };
        if info.broadcast_failure {
            clif_broadcast_refine_result(c, aid, r.item.nameid, r.item.refine as i8, false);
        }
        if blacksmith_amount > 0 {
            // Blacksmith blessings were used to prevent breaking and downgrading
            ci::refine(c, aid, idx, ITEMREFINING_FAILURE2);
            clif_refineui_info(c, aid, index);
        } else if cost.breaking_rate > 0 && c.rnd() % 10000 < cost.breaking_rate as u32 {
            // Delete the item if it is breakable
            ci::refine(c, aid, idx, ci::ITEMREFINING_FAILURE);
            inv::pc_delitem(c, aid, idx as i32, 1, 0, 2, crate::map_log::LOG_TYPE_CONSUME);
        } else if cost.downgrade_amount > 0 {
            // Downgrade the item if necessary
            let mut r = r;
            r.item.refine = (r.item.refine as i32 - cost.downgrade_amount as i32).clamp(0, MAX_REFINE) as u8;
            inv::inv_put(c, &r);
            ci::refine(c, aid, idx, ci::ITEMREFINING_DOWNGRADE);
            clif_refineui_info(c, aid, index);
        } else {
            // Only show failure, but dont do anything
            ci::refine(c, aid, idx, ITEMREFINING_FAILURE2);
            clif_refineui_info(c, aid, index);
        }
        misceffect(c, aid, ci::NOTIFYEFFECT_REFINE_FAILURE as i32);
        crate::achievement::achievement_update_objective(c, Some(aid), crate::db::misc::achievement::AG_ENCHANT_FAIL, &[1]);
    }
}

// ---------------------------------------------------------------------------------------------- weapon refine

/// clif.cpp clif_upgrademessage: ZC_ACK_WEAPONREFINE (0 upgraded, 1 failed, 2 skill level too low, 3 no material).
pub fn clif_upgrademessage(c: &Ctx, aid: u32, result: i32, item_id: u32) {
    let p = PACKET_ZC_ACK_WEAPONREFINE { packetType: PACKET_ZC_ACK_WEAPONREFINE::ID as i16, result, itemId: itemdb::client_nameid(item_id) };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_item_refine_list: ZC_NOTIFY_WEAPONITEMLIST — the identified, unworn weapons below the WS_WEAPONREFINE
/// level whose refine material the player carries (always sent); menuskill WS_WEAPONREFINE when not empty.
pub fn clif_item_refine_list(c: &Ctx, aid: u32) {
    let skill_lv = crate::skill::checkskill(c, aid, WS_WEAPONREFINE);
    // refine_item[MAX_WEAPON_LEVEL]: RENEWAL leaves level 5 without a material (-1)
    let refine_item: [i16; 5] = [
        inv::pc_search_inventory(c, aid, ITEMID_PHRACON),
        inv::pc_search_inventory(c, aid, ITEMID_EMVERETARCON),
        inv::pc_search_inventory(c, aid, ITEMID_ORIDECON),
        inv::pc_search_inventory(c, aid, ITEMID_ORIDECON),
        -1,
    ];
    let mut p = PACKET_ZC_NOTIFY_WEAPONITEMLIST { packetType: PACKET_ZC_NOTIFY_WEAPONITEMLIST::ID as i16, ..Default::default() };
    for r in inv::inv_list(c, aid) {
        let it = &r.item;
        let id = inv::data_of(it);
        if it.nameid > 0
            && it.refine < skill_lv
            && id.r#type == IT_WEAPON
            && it.identify != 0
            && id.weapon_level >= 1
            && refine_item.get(id.weapon_level as usize - 1).copied().unwrap_or(-1) != -1
            && it.equip & EQP_ARMS == 0
        {
            p.items.push(PACKET_ZC_NOTIFY_WEAPONITEMLIST_sub {
                index: ci::client_index(r.idx as usize) as i16,
                itemId: itemdb::client_nameid(it.nameid),
                refine: it.refine,
                slot: ci::addcards(Some(it)),
            });
        }
    }
    let count = p.items.len();
    send_self(c, aid, &p.to_vec());
    if count > 0 {
        update(c, aid, |s| {
            s.menuskill_id = WS_WEAPONREFINE as i32;
            s.menuskill_val = skill_lv as i32;
        });
    }
}

/// clif.cpp clif_parse_WeaponRefine (CZ_REQ_WEAPONREFINE 0x0222).
pub fn clif_parse_weapon_refine(c: &Ctx, aid: u32, pkt: &[u8]) {
    if menu(c, aid).0 != WS_WEAPONREFINE as i32 {
        return; // Packet exploit?
    }
    let Some(p) = crate::pc::get(c, aid) else { return };
    if inv::pc_istrading(&p) {
        // Make it fail to avoid shop exploits where you sell something different than you see.
        if let Some(sd) = unit::get(c, aid) {
            crate::skill::skill_fail(c, &sd, sd.ud.skill_id, 0);
        }
        clif_menuskill_clear(c, aid);
        return;
    }
    let off = packet_pos(0x0222).first().copied().unwrap_or(2) as usize;
    let Some(b) = pkt.get(off..off + 4) else { return };
    // server_index( RFIFOL( fd, 2 ) ): the 32-bit index is truncated to uint16
    let idx = server_index(u32::from_le_bytes([b[0], b[1], b[2], b[3]]) as u16);
    skill_weaponrefine(c, aid, idx as i32);
    clif_menuskill_clear(c, aid);
}

/// skill.cpp skill_weaponrefine: refines a weapon with its level's ore (the chance comes from the refine database's
/// normal cost, plus the job bonus); a failure destroys the weapon.
pub fn skill_weaponrefine(c: &Ctx, aid: u32, idx: i32) {
    // material[MAX_WEAPON_LEVEL] (RENEWAL: none for level 5)
    const MATERIAL: [u32; 5] = [ITEMID_PHRACON, ITEMID_EMVERETARCON, ITEMID_ORIDECON, ITEMID_ORIDECON, 0];
    if idx < 0 || idx as usize >= MAX_INVENTORY {
        return;
    }
    let idx_u = idx as usize;
    let Some(r) = slot(c, aid, idx_u) else { return };
    let ditem = inv::data_of(&r.item);
    if !(r.item.nameid > 0 && ditem.r#type == IT_WEAPON) {
        return;
    }
    let Some(sd) = unit::get(c, aid) else { return };
    let menuskill = menu(c, aid);
    if ditem.flag.no_refine || ditem.weapon_level < 1 {
        // if the item isn't refinable
        crate::skill::skill_fail(c, &sd, menuskill.0 as u16, 0);
        return;
    }
    if r.item.refine as i32 >= menuskill.1 || r.item.refine >= 10 {
        clif_upgrademessage(c, aid, 2, r.item.nameid);
        return;
    }
    let mat = MATERIAL.get(ditem.weapon_level as usize - 1).copied().unwrap_or(0);
    let i = inv::pc_search_inventory(c, aid, mat);
    if i < 0 {
        clif_upgrademessage(c, aid, 3, mat);
        return;
    }
    let Some(info) = refine_db::find_level_info(ditem, r.item.refine) else {
        crate::skill::skill_fail(c, &sd, menuskill.0 as u16, 0);
        return;
    };
    let Some(cost) = info.cost(REFINE_COST_NORMAL) else {
        crate::skill::skill_fail(c, &sd, menuskill.0 as u16, 0);
        return;
    };
    if cost.nameid != mat {
        log::debug!(
            "skill_weaponrefine: The hardcoded refine requirement {mat} for weapon level {} does not match {} from the refine database.",
            ditem.weapon_level,
            cost.nameid
        );
        crate::skill::skill_fail(c, &sd, menuskill.0 as u16, 0);
        return;
    }
    let Some(ch) = crate::pc::character_of(c, aid) else { return };
    let mut per = cost.chance as i32 / 100;
    if inv::class_mapid(&ch) & crate::db::jobs::JOBL_THIRD != 0 {
        per += 10;
    } else {
        per += (ch.job_level as i32 - 50) / 2; // Updated per the new kro descriptions. [Skotlex]
    }
    inv::pc_delitem(c, aid, i as i32, 1, 0, 0, crate::map_log::LOG_TYPE_OTHER);
    if per > (c.rnd() % 100) as i32 {
        let Some(mut r) = slot(c, aid, idx_u) else { return };
        crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_OTHER, -1, &r.item);
        r.item.refine = r.item.refine.wrapping_add(1);
        inv::inv_put(c, &r);
        crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_OTHER, 1, &r.item);
        let mut ep = 0;
        if r.item.equip != 0 {
            ep = r.item.equip;
            crate::pc_equip::pc_unequipitem(c, aid, idx, 3);
        }
        ci::delitem(c, aid, idx, 1, 3);
        clif_upgrademessage(c, aid, 0, r.item.nameid);
        ci::inventorylist(c, aid);
        ci::refine(c, aid, idx_u, ci::ITEMREFINING_SUCCESS);
        if ditem.r#type == IT_WEAPON {
            crate::achievement::achievement_update_objective(c, Some(aid), crate::db::misc::achievement::AG_ENCHANT_SUCCESS, &[ditem.weapon_level as i32, r.item.refine as i32]);
        }
        if ep != 0 {
            crate::pc_equip::pc_equipitem(c, aid, idx as i16, ep, false);
        }
        misceffect(c, aid, ci::NOTIFYEFFECT_REFINE_SUCCESS as i32);
        let Some(r) = slot(c, aid, idx_u) else { return };
        let maker = (r.item.card2 & 0xffff) | ((r.item.card3 & 0xffff) << 16);
        if r.item.refine == 10 && r.item.card0 == itemdb::CARD0_FORGE && maker == ch.id {
            // Fame point system [DracoRPG]
            match ditem.weapon_level {
                1 => pc_addfame_stub(c, aid, fame_refine_lv1()), // Success to refine to +10 a lv1 weapon you forged = +1 fame point
                2 => pc_addfame_stub(c, aid, fame_refine_lv2()), // Success to refine to +10 a lv2 weapon you forged = +25 fame point
                3 => pc_addfame_stub(c, aid, fame_refine_lv3()), // Success to refine to +10 a lv3 weapon you forged = +1000 fame point
                _ => {}
            }
        }
    } else {
        let Some(mut r) = slot(c, aid, idx_u) else { return };
        r.item.refine = 0;
        inv::inv_put(c, &r);
        if r.item.equip != 0 {
            crate::pc_equip::pc_unequipitem(c, aid, idx, 3);
        }
        clif_upgrademessage(c, aid, 1, r.item.nameid);
        ci::refine(c, aid, idx_u, ci::ITEMREFINING_FAILURE);
        crate::achievement::achievement_update_objective(c, Some(aid), crate::db::misc::achievement::AG_ENCHANT_FAIL, &[1]);
        inv::pc_delitem(c, aid, idx, 1, 0, 2, crate::map_log::LOG_TYPE_OTHER);
        misceffect(c, aid, ci::NOTIFYEFFECT_REFINE_FAILURE as i32);
        if let Some(sd) = unit::get(c, aid) {
            clif::emotion(c, &sd, ET_HUK);
        }
    }
}
