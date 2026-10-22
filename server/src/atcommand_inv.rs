//! map/atcommand.cpp: inventory, storage, cart and equipment: @delitem, @dropall, @stockall, @clearstorage, @clearcart,
//! @repairall, @setcard, @cloneequip, @clonestat, @fullstrip; family: @marry, @divorce, @adopt; @resurrect (atcmd
//! agent, wave D). Routed from atcommand_atcmd.rs `run` by the ACMD_FUNC name.
//!
//! Helpers ported here because the module had no port of them: atcommand.cpp getring, clif.cpp clif_wedding_effect
//! (script2's `wedding` buildin writes the same packet inline), pc.cpp pc_revive_item and itemdb.cpp
//! ItemGroupDatabase::item_exists_pc. The family functions (pc_marriage, pc_divorce, pc_try_adopt, clif_Adopt_request)
//! are script2's (script/buildins/script2/family.rs).
//!
//! Not ported anywhere yet, left as a comment at its call site: pet_return_egg (pet agent; an egg is only "hatched"
//! while its pet is out, which cannot happen before the pet agent's hatching exists).

use crate::atcommand_atcmd::Out;
use crate::clif::{self, Target};
use crate::clif_items as ci;
use crate::cstr::{atoi, Scan};
use crate::data::{format_msg, msg_txt};
use crate::db::items::{self as itemdb, CARD0_FORGE, CARD0_PET, IT_AMMO, IT_ARMOR, IT_CARD, IT_ETC, IT_HEALING, IT_PETARMOR, IT_PETEGG, IT_USABLE, IT_WEAPON};
use crate::inventory::{self as inv, Item, EQI_AMMO, EQI_MAX, MAX_CART, MAX_INVENTORY, MAX_SLOTS, SP_CARTINFO};
use crate::inventory::{EQP_ACC_L, EQP_ACC_R, EQP_ARMOR, EQP_GARMENT, EQP_HAND_L, EQP_HAND_R, EQP_HEAD_LOW, EQP_HEAD_MID, EQP_HEAD_TOP, EQP_SHOES};
use crate::pc;
use crate::pc_equip;
use crate::script::buildins::script2::family;
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// clif.cpp color_table[COLOR_RED]: 0xFF0000 turned to BGR by do_init_clif.
const COLOR_RED: u32 = 0x0000FF;
/// itemdb.hpp IG_TOKEN_OF_SIEGFRIED (the compiler's value: tools/script_constants.tsv).
const IG_TOKEN_OF_SIEGFRIED: u16 = 490;

/// Runs `func` when it is one of this file's commands; None otherwise.
pub fn run(c: &Ctx, fd: Option<&Unit>, sd: u32, func: &str, command: &str, message: &str) -> Option<i32> {
    let out = Out { c, fd };
    Some(match func {
        "delitem" => delitem(c, &out, sd, message),
        "dropall" => dropall(c, &out, sd, message),
        "stockall" => stockall(c, &out, sd, message),
        "clearstorage" => clearstorage(c, &out, sd),
        "clearcart" => clearcart(c, &out, sd),
        "repairall" => repairall(c, &out, sd),
        "setcard" => setcard(c, &out, sd, message),
        "cloneequip" => cloneequip(c, &out, sd, command, message),
        "clonestat" => clonestat(c, &out, sd, command, message),
        "fullstrip" => fullstrip(c, &out, sd, message),
        "marry" => marry(c, &out, sd, message),
        "divorce" => divorce(c, &out, sd),
        "adopt" => adopt(c, &out, sd, command, message),
        "resurrect" => resurrect(c, sd),
        _ => return None,
    })
}

// ---------------------------------------------------------------------------------------------- helpers

fn bytes(s: &str) -> Vec<u8> {
    crate::net::wire::latin1(s)
}

/// nullpo_retr(-1, sd): true when the player (the invoker, or a #command's target) is not in the world.
fn nullpo(c: &Ctx, sd: u32) -> bool {
    pc::get(c, sd).is_none()
}

/// `!message || !*message || sscanf(message, "%23[^\n]", atcmd_player_name) < 1`: None when there is no name.
fn player_name(message: &str) -> Option<String> {
    if message.is_empty() {
        return None;
    }
    Scan::new(&bytes(message)).set(23, b"\n")
}

/// clif_displaymessage(sd->fd, text): to the player itself, not to the invoker's fd.
fn say_to(c: &Ctx, aid: u32, text: &str) {
    if let Some(u) = unit::get(c, aid) {
        clif::displaymessage(c, &u, text);
    }
}

/// clif_msg_color(*sd, msg_id, color_table[COLOR_RED]).
fn msg_red(c: &Ctx, aid: u32, msg_id: u16) {
    if let Some(u) = unit::get(c, aid) {
        ci::msg_color(c, &u, msg_id, COLOR_RED);
    }
}

/// The item types @dropall and @stockall accept: -1 (all), IT_HEALING, IT_USABLE, IT_ETC, IT_WEAPON, IT_ARMOR,
/// IT_CARD, IT_PETEGG, IT_PETARMOR, IT_AMMO.
fn valid_type(t: i8) -> bool {
    t == -1 || [IT_HEALING, IT_USABLE, IT_ETC, IT_WEAPON, IT_ARMOR, IT_CARD, IT_PETEGG, IT_PETARMOR, IT_AMMO].iter().any(|&k| k as i32 == t as i32)
}

/// @delitem's `sscanf(message, "\"%99[^\"]\" %11d", item_name, &amount) < 2 && sscanf(message, "%99s %11d", item_name,
/// &amount) < 2`: Some((name, amount)) when one of the two calls converted both (the second one runs only when the
/// first did not, and what it converts replaces the first one's).
fn scan_name_amount(message: &str) -> Option<(String, i32)> {
    let b = bytes(message);
    let mut s = Scan::new(&b);
    if s.lit(b'"') {
        if let Some(name) = s.set(99, b"\"") {
            if s.lit(b'"') {
                if let Some(v) = s.int(11) {
                    return Some((name, v as i32));
                }
            }
        }
    }
    let mut s = Scan::new(&b);
    let name = s.word(99)?;
    let v = s.int(11)?;
    Some((name, v as i32))
}

/// `sscanf(message, "%11d %11d %11d", …) < 3`: Some when all three convert.
fn scan_three(message: &str) -> Option<(i32, i32, i32)> {
    let b = bytes(message);
    let mut s = Scan::new(&b);
    let x = s.int(11)? as i32;
    let y = s.int(11)? as i32;
    let z = s.int(11)? as i32;
    Some((x, y, z))
}

/// map.cpp map_id2sd: the player with this account id (none for an id <= 0).
fn map_id2sd(c: &Ctx, id: i32) -> Option<u32> {
    if id <= 0 {
        return None;
    }
    pc::get(c, id as u32).map(|p| p.account_id)
}

/// `(sd->status.sex) ? WEDDING_RING_M : WEDDING_RING_F`.
fn ring_of(c: &Ctx, aid: u32) -> u32 {
    if pc::character_of(c, aid).map(|ch| ch.sex != 0).unwrap_or(false) {
        family::WEDDING_RING_M
    } else {
        family::WEDDING_RING_F
    }
}

/// GetWord(partner_id, 0) / GetWord(partner_id, 1): the partner's char id in a ring's card[2] and card[3].
fn ring_cards(partner_id: u32) -> (u32, u32) {
    (partner_id & 0xFFFF, partner_id >> 16)
}

// ---------------------------------------------------------------------------------------------- inventory

/// ACMD_FUNC(delitem): @delitem <item name/ID> <quantity> (#delitem <player> …): the quantity leaves the player's
/// inventory slot after slot (pc_search_inventory order); a pet egg takes its pet with it (intif_delete_petdata). The
/// player is told how many went (clif_displaymessage(sd->fd, …)), then the invoker how it went.
fn delitem(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    if nullpo(c, sd) {
        return -1;
    }
    let parsed = if message.is_empty() { None } else { scan_name_amount(message) };
    let Some((item_name, amount)) = parsed.filter(|(_, a)| *a >= 1) else {
        out.say(msg_txt(1355)); // Please enter an item name/ID, a quantity, and a player name (usage: #delitem <player> <item_name_or_ID> <quantity>).
        return -1;
    };
    // item_db.searchname, then item_db.find(strtoul(item_name, nullptr, 10)) (t_itemid: the unsigned long cut to 32 bits)
    let id = itemdb::itemdb_searchname(&item_name).or_else(|| itemdb::itemdb_exists(crate::atcommand_atcmd::c_strtoul(&item_name) as u32));
    let Some(id) = id else {
        out.say(msg_txt(19)); // Invalid item ID or name.
        return -1;
    };
    let nameid = id.nameid;
    let total = amount;
    let mut amount = amount;
    // delete items
    while amount != 0 {
        let idx = inv::pc_search_inventory(c, sd, nameid);
        if idx == -1 {
            break;
        }
        let Some(r) = inv::inv_get(c, sd, idx as usize) else { break };
        let delamount = if amount < r.item.amount as i32 { amount } else { r.item.amount as i32 };
        if inv::data_of(&r.item).r#type == IT_PETEGG && r.item.card0 == CARD0_PET {
            // delete pet
            crate::pet::intif_delete_petdata(crate::pet::egg_pet_id(&r.item));
        }
        if inv::pc_delitem(c, sd, idx as i32, delamount, 0, 0, crate::map_log::LOG_TYPE_COMMAND) != 0 {
            break; // (cannot fail with 1 <= delamount <= the slot's amount; rAthena would loop forever)
        }
        amount -= delamount;
    }
    // notify target
    say_to(c, sd, &format_msg(msg_txt(113), &[&(total - amount)])); // %d item(s) removed by a GM.
    // notify source
    if amount == total {
        out.say(msg_txt(116)); // Character does not have the specified item.
    } else if amount != 0 {
        out.say(&format_msg(msg_txt(115), &[&(total - amount), &(total - amount), &total])); // %d item(s) removed. Player had only %d on %d items.
    } else {
        out.say(&format_msg(msg_txt(114), &[&total])); // %d item(s) removed from the player.
    }
    0
}

/// ACMD_FUNC(dropall): @dropall {<type>}: every droppable item (of the type) falls at the player's feet, worn ones
/// taken off first. rAthena quirks kept: `int8 type = atoi(message)` (so 256 is IT_HEALING), pc_candrop is asked
/// before the type test, and the counters are uint16.
fn dropall(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    if nullpo(c, sd) {
        return -1;
    }
    let mut kind: i8 = -1;
    if !message.is_empty() {
        kind = atoi(message) as i8;
        if !valid_type(kind) {
            out.say(msg_txt(1492)); // Usage: @dropall {<type>}
            out.say(msg_txt(1493)); // Type List: (default) all = -1, healing = 0, usable = 2, etc = 3, armor = 4, weapon = 5, card = 6, petegg = 7, petarmor = 8, ammo = 10
            return -1;
        }
    }
    let (mut count, mut count2) = (0u16, 0u16);
    for i in 0..MAX_INVENTORY {
        let Some(r) = inv::inv_get(c, sd, i) else { continue };
        if r.item.amount == 0 {
            continue;
        }
        let Some(id) = itemdb::itemdb_exists(r.item.nameid) else {
            let char_id = pc::get(c, sd).map(|p| p.char_id).unwrap_or(0);
            log::debug!("Non-existant item {} on dropall list (account_id: {sd}, char_id: {char_id})", r.item.nameid);
            continue;
        };
        if !crate::flooritem::pc_candrop(c, sd, &r.item) {
            continue;
        }
        // type == (uint8)id->type
        if kind == -1 || kind as i32 == id.r#type as i32 {
            if r.item.equip != 0 {
                pc_equip::pc_unequipitem(c, sd, i as i32, 3);
            }
            let hatched = inv::inv_get(c, sd, i).map(|r| itemdb::itemdb_ishatched_egg(r.item.card0, r.item.attribute)).unwrap_or(false);
            if hatched {
                if let Some(pd) = crate::pet::data::of_master(c, sd) {
                    crate::pet::pet_return_egg(c, sd, pd.id);
                }
            }
            pc_equip::pc_equipswitch_remove(c, sd, i);
            let amount = inv::inv_get(c, sd, i).map(|r| r.item.amount as i32).unwrap_or(0);
            if crate::flooritem::pc_dropitem(c, sd, i as i32, amount) {
                count = count.wrapping_add(amount as u16);
            } else {
                count2 = count2.wrapping_add(amount as u16);
            }
        }
    }
    out.say(&format_msg(msg_txt(1494), &[&count, &count2])); // %d items are dropped (%d skipped)!
    0
}

/// ACMD_FUNC(stockall): @stockall {<type>}: every cart item (of the type) goes to the inventory (pc_getitemfromcart).
/// rAthena quirk kept: pc_getitemfromcart answers true when pc_additem refuses the item too (it only resends the
/// packets), so such items count as transferred.
fn stockall(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    if nullpo(c, sd) {
        return -1;
    }
    if !inv::pc_iscarton(c, sd) {
        out.say(msg_txt(1533)); // You do not have a cart.
        return -1;
    }
    let mut kind: i8 = -1;
    if !message.is_empty() {
        kind = atoi(message) as i8;
        if !valid_type(kind) {
            out.say(msg_txt(1534)); // Usage: @stockall {<type>}
            out.say(msg_txt(1493)); // Type List: (default) all = -1, healing = 0, usable = 2, etc = 3, armor = 4, weapon = 5, card = 6, petegg = 7, petarmor = 8, ammo = 10
            return -1;
        }
    }
    let (mut count, mut count2) = (0i32, 0i32);
    for i in 0..MAX_CART {
        let Some(r) = crate::cart::cart_get(c, sd, i) else { continue };
        if r.item.amount > 0 {
            let Some(id) = itemdb::itemdb_exists(r.item.nameid) else {
                let char_id = pc::get(c, sd).map(|p| p.char_id).unwrap_or(0);
                log::debug!("Non-existent item {} on stockall list (account_id: {sd}, char_id: {char_id})", r.item.nameid);
                continue;
            };
            if kind == -1 || kind as i32 == id.r#type as i32 {
                let amount = r.item.amount as i32;
                if crate::cart::pc_getitemfromcart(c, sd, i as i32, amount) {
                    count = count.wrapping_add(amount);
                } else {
                    count2 = count2.wrapping_add(amount);
                }
            }
        }
    }
    out.say(&format_msg(msg_txt(1535), &[&count, &count2])); // %d items are transferred (%d skipped)!
    0
}

/// ACMD_FUNC(clearstorage): the account storage is emptied (storage_delitem on slots 0 .. storage.amount - 1, no
/// window packets as the storage is closed), then storage_flag is set to 1 so that storage_storageclose saves it and
/// sends ZC_CLOSE_STORE. rAthena quirks kept: only the first `amount` slots are looked at (items behind a gap left by
/// an earlier removal stay), and an open guild storage (storage_flag 2) does not stop it.
fn clearstorage(c: &Ctx, out: &Out, sd: u32) -> i32 {
    let Some(p) = pc::get(c, sd) else { return -1 };
    if p.storage_flag == 1 {
        out.say(msg_txt(250)); // You have already opened your storage. Close it first.
        return -1;
    }
    if p.storage_flag == 3 {
        out.say(msg_txt(250)); // You have already opened your storage. Close it first.
        return -1;
    }
    let j = inv::st(c, sd).map(|s| s.storage_amount).unwrap_or(0) as usize;
    for i in 0..j {
        let amount = crate::storage::storage_get(c, sd, i).map(|r| r.item.amount as i32).unwrap_or(0);
        crate::storage::storage_delitem(c, sd, i, amount);
    }
    if let Some(mut p) = pc::get(c, sd) {
        p.storage_flag = 1;
        pc::put(c, &p);
    }
    crate::storage::storage_storageclose(c, sd);
    out.say(msg_txt(1394)); // Your storage was cleaned.
    0
}

/// ACMD_FUNC(clearcart): every cart item is deleted without packets (pc_cart_delitem type 1), then ZC_CARTOFF goes to
/// the invoker's session (clif_clearcart(fd): for #clearcart the GM gets it, not the target) and SP_CARTINFO to the
/// player. A vending player: failure without a message.
fn clearcart(c: &Ctx, out: &Out, sd: u32) -> i32 {
    let Some(p) = pc::get(c, sd) else { return -1 };
    if !inv::pc_iscarton(c, sd) {
        out.say(msg_txt(1396)); // You do not have a cart to be cleaned.
        return -1;
    }
    if p.vending {
        return -1; // Somehow...
    }
    for i in 0..MAX_CART {
        if let Some(r) = crate::cart::cart_get(c, sd, i) {
            if r.item.nameid > 0 {
                crate::cart::pc_cart_delitem(c, sd, i as i32, r.item.amount as i32, 1, crate::map_log::LOG_TYPE_OTHER);
            }
        }
    }
    if let Some(f) = out.fd {
        ci::clearcart(c, f.id); // clif_clearcart(fd)
    }
    inv::updatestatus(c, sd, SP_CARTINFO);
    out.say(msg_txt(1397)); // Your cart was cleaned.
    0
}

/// ACMD_FUNC(repairall): every item whose attribute is exactly 1 (broken; hatched eggs excepted) is repaired, each
/// with clif_produceeffect(sd, 0, nameid); then the refine success effect and the inventory list. (The repair window,
/// itemui/repair.rs, is the skill's; this is the command's own loop.)
fn repairall(c: &Ctx, out: &Out, sd: u32) -> i32 {
    if nullpo(c, sd) {
        return -1;
    }
    let mut count = 0;
    for i in 0..MAX_INVENTORY {
        let Some(mut r) = inv::inv_get(c, sd, i) else { continue };
        if r.item.nameid != 0 && r.item.attribute == 1 && !itemdb::itemdb_ishatched_egg(r.item.card0, r.item.attribute) {
            r.item.attribute = 0;
            inv::inv_put(c, &r);
            ci::produceeffect(c, sd, 0, r.item.nameid);
            count += 1;
        }
    }
    if count > 0 {
        if let Some(u) = unit::get(c, sd) {
            crate::status::clif::misceffect(c, &u, ci::NOTIFYEFFECT_REFINE_SUCCESS as i32);
        }
        ci::equiplist(c, sd);
        out.say(msg_txt(107)); // All items have been repaired.
    } else {
        out.say(msg_txt(108)); // No item need to be repaired.
        return -1;
    }
    0
}

/// ACMD_FUNC(setcard): @setcard <equip position> <slot> <card id>: the worn item at the position gets the card (0:
/// none) in the slot — taken off, changed, shown again (delete + add) and put back on at its old position.
fn setcard(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    if nullpo(c, sd) {
        return -1;
    }
    let parsed = if message.is_empty() { None } else { scan_three(message) };
    let Some((position, slot, card_id)) = parsed else {
        out.say(msg_txt(1527)); // Please enter the position, the slot number and the card ID (usage: @setcard <equip position> <slot> <card_id>).
        for (m, pos) in [
            (997, EQP_HEAD_LOW),  // %d: Lower Headgear
            (998, EQP_HAND_R),    // %d: Right Hand
            (999, EQP_GARMENT),   // %d: Garment
            (1000, EQP_ACC_L),    // %d: Left Accessory
            (1001, EQP_ARMOR),    // %d: Body Armor
            (1002, EQP_HAND_L),   // %d: Left Hand
            (1003, EQP_SHOES),    // %d: Shoes
            (1004, EQP_ACC_R),    // %d: Right Accessory
            (1005, EQP_HEAD_TOP), // %d: Top Headgear
            (1006, EQP_HEAD_MID), // %d: Mid Headgear
        ] {
            out.say(&format_msg(msg_txt(m), &[&pos]));
        }
        return -1;
    };
    if position < EQP_HEAD_LOW as i32 || position > EQP_HEAD_MID as i32 {
        out.say(msg_txt(1531)); // Invalid position.
        return -1;
    }
    if slot < 0 || slot >= MAX_SLOTS as i32 {
        out.say(msg_txt(1532)); // Invalid slot number.
        return -1;
    }
    if card_id != 0 {
        // item_db.find(card_id): the int32 as a t_itemid
        let Some(item_data) = itemdb::itemdb_exists(card_id as u32) else {
            out.say(msg_txt(1530)); // Invalid card ID.
            return -1;
        };
        if item_data.r#type != IT_CARD {
            out.say(msg_txt(1529)); // The item type must be a card or enchant.
            return -1;
        }
    }
    let i = pc_equip::pc_checkequip(c, sd, position as u32, false);
    if i < 0 {
        out.say(msg_txt(1528)); // You are not wearing any equipment in this position.
        return -1;
    }
    let current_position = inv::inv_get(c, sd, i as usize).map(|r| r.item.equip).unwrap_or(0);
    pc_equip::pc_unequipitem(c, sd, i as i32, 3);
    if let Some(mut r) = inv::inv_get(c, sd, i as usize) {
        crate::map_log::log_pick_pc(c, sd, crate::map_log::LOG_TYPE_COMMAND, -1, &r.item);
        r.item.set_card(slot as usize, card_id as u32);
        inv::inv_put(c, &r);
        crate::map_log::log_pick_pc(c, sd, crate::map_log::LOG_TYPE_COMMAND, 1, &r.item);
    }
    ci::delitem(c, sd, i as i32, 1, 0);
    ci::additem(c, sd, i as i32, 1, 0);
    pc_equip::pc_equipitem(c, sd, i, current_position, false);
    0
}

/// The player an @cloneequip / @clonestat names: map_nick2sd(name, true) (a unique prefix is enough), else
/// map_charid2sd(atoi(name)).
fn clone_target(c: &Ctx, name: &str) -> Option<u32> {
    pc::nick2sd(c, name, true).map(|u| u.id).or_else(|| pc::charid2sd(c, atoi(name) as u32).map(|u| u.id))
}

/// ACMD_FUNC(cloneequip): @cloneequip <char name/ID>: a copy of every item the other player wears (ammunition and
/// the second slot of a two-slot item excepted; cards of forged / created items cleared; unbound, no rental, a new
/// unique id) is given to the invoker and worn. rAthena quirk kept: the copy is worn from sd->last_addeditem_index,
/// the last slot pc_additem *created*.
fn cloneequip(c: &Ctx, out: &Out, sd: u32, command: &str, message: &str) -> i32 {
    if nullpo(c, sd) {
        return -1;
    }
    let Some(name) = player_name(message) else {
        out.say(&format_msg(msg_txt(735), &[&command])); // Usage: %s <char name/ID>
        return -1;
    };
    let Some(pl_sd) = clone_target(c, &name) else {
        out.say(msg_txt(3)); // Character not found.
        return -1;
    };
    if sd == pl_sd {
        out.say(&format_msg(msg_txt(734), &[&"equip"])); // Cannot clone your own %s.
        return -1;
    }
    if pc::group_level(c, sd) < pc::group_level(c, pl_sd) {
        out.say(&format_msg(msg_txt(736), &[&"equip"])); // Cannot clone %s from this player.
        return -1;
    }
    for i in 0..EQI_MAX {
        let Some(ps) = inv::st(c, pl_sd) else { break };
        let idx = ps.equip_index[i];
        if idx < 0 {
            continue;
        }
        if i == EQI_AMMO {
            continue;
        }
        if pc_equip::pc_is_same_equip_index(i, &ps.equip_index, idx) {
            continue;
        }
        let Some(r) = inv::inv_get(c, pl_sd, idx as usize) else { continue };
        let mut tmp_item: Item = r.item;
        if itemdb::itemdb_isspecial(tmp_item.card0) {
            for k in 0..MAX_SLOTS {
                tmp_item.set_card(k, 0);
            }
        }
        tmp_item.bound = 0;
        tmp_item.expire_time = 0;
        tmp_item.unique_id = 0;
        tmp_item.favorite = 0;
        tmp_item.amount = 1;
        let flag = inv::pc_additem(c, sd, &mut tmp_item, 1, crate::map_log::LOG_TYPE_COMMAND);
        if flag != 0 {
            ci::additem(c, sd, 0, 0, flag);
        } else {
            let n = inv::st(c, sd).map(|s| s.last_addeditem_index).unwrap_or(-1);
            pc_equip::pc_equipitem(c, sd, n, itemdb::itemdb_equip(tmp_item.nameid), false);
        }
    }
    out.say(&format_msg(msg_txt(738), &[&"equip"])); // Clone '%s' is done.
    0
}

/// ACMD_FUNC(clonestat): @clonestat <char name/ID>: the invoker's stats are reset (pc_resetstate: every status point
/// back) and then set to the other player's, each capped (SHRT_MAX with the bypass_stat_onclone permission, else
/// pc_maxparameter) with msg 737 for a capped one; no status point is spent. rAthena quirks kept: the message names the
/// member (`int_` for Int), and without the permission a 4th class keeps a limit of 0 for its traits (that branch
/// skips the trait parameters of trait jobs, the inverse of the permission branch's test).
fn clonestat(c: &Ctx, out: &Out, sd: u32, command: &str, message: &str) -> i32 {
    use crate::db::jobs::{pc_is_trait_job, PARAM_AGI, PARAM_CON, PARAM_CRT, PARAM_DEX, PARAM_INT, PARAM_LUK, PARAM_MAX, PARAM_POW, PARAM_SPL, PARAM_STA, PARAM_STR, PARAM_VIT, PARAM_WIS};
    use crate::status::consts::{SP_AGI, SP_CON, SP_CRT, SP_DEX, SP_INT, SP_LUK, SP_POW, SP_SPL, SP_STA, SP_STR, SP_UPOW, SP_USTR, SP_VIT, SP_WIS};
    use crate::status::progress;
    if nullpo(c, sd) {
        return -1;
    }
    let Some(name) = player_name(message) else {
        out.say(&format_msg(msg_txt(735), &[&command])); // Usage: %s <char name/ID>
        return -1;
    };
    let Some(pl_sd) = clone_target(c, &name) else {
        out.say(msg_txt(3)); // Character not found.
        return -1;
    };
    if sd == pl_sd {
        out.say(&format_msg(msg_txt(734), &[&"status"])); // Cannot clone your own %s.
        return -1;
    }
    if pc::group_level(c, sd) < pc::group_level(c, pl_sd) {
        out.say(&format_msg(msg_txt(736), &[&"status"])); // Cannot clone %s from this player.
        return -1;
    }
    let Some(pch) = pc::character_of(c, pl_sd) else { return -1 };
    progress::pc_resetstate(c, sd);
    let class_ = crate::status::data::sd_get(c, sd).map(|s| s.class_).unwrap_or(0);
    let trait_job = pc_is_trait_job(class_);
    let Some(mut ch) = pc::character_of(c, sd) else { return -1 };
    let mut max_status = [0i16; PARAM_MAX];
    if pc::has_permission(c, sd, crate::rules::PC_PERM_BYPASS_STAT_ONCLONE) {
        for (i, m) in max_status.iter_mut().enumerate() {
            if i >= PARAM_POW && !trait_job {
                continue;
            }
            *m = i16::MAX; // SHRT_MAX
        }
    } else {
        for (i, m) in max_status.iter_mut().enumerate() {
            if i >= PARAM_POW && trait_job {
                continue;
            }
            *m = progress::pc_maxparameter(c, sd, &ch, i) as u16 as i16;
        }
    }
    // clonestat_check(cmd, stat): pl_sd->status.cmd (uint16) against max_status[stat] (int16), both as int
    let check = |ch: &mut crate::character::Character, member: &str, sp: i32, stat: usize| {
        let v = progress::pc_getstat(&pch, sp);
        let max = max_status[stat] as i32;
        if v > max {
            out.say(&format_msg(msg_txt(737), &[&member, &v, &max])); // '%s' (%d) cannot be cloned, limit (%d).
            progress::pc_setstat(ch, sp, max);
        } else {
            progress::pc_setstat(ch, sp, v);
        }
    };
    check(&mut ch, "str", SP_STR, PARAM_STR);
    check(&mut ch, "agi", SP_AGI, PARAM_AGI);
    check(&mut ch, "vit", SP_VIT, PARAM_VIT);
    check(&mut ch, "int_", SP_INT, PARAM_INT);
    check(&mut ch, "dex", SP_DEX, PARAM_DEX);
    check(&mut ch, "luk", SP_LUK, PARAM_LUK);
    crate::status::put_char(c, &ch);
    for i in PARAM_STR..PARAM_POW {
        crate::status::clif::updatestatus(c, sd, SP_STR + i as i32);
        crate::status::clif::updatestatus(c, sd, SP_USTR + i as i32);
    }
    if trait_job {
        check(&mut ch, "pow", SP_POW, PARAM_POW);
        check(&mut ch, "sta", SP_STA, PARAM_STA);
        check(&mut ch, "wis", SP_WIS, PARAM_WIS);
        check(&mut ch, "spl", SP_SPL, PARAM_SPL);
        check(&mut ch, "con", SP_CON, PARAM_CON);
        check(&mut ch, "crt", SP_CRT, PARAM_CRT);
        crate::status::put_char(c, &ch);
        for i in PARAM_POW..PARAM_MAX {
            crate::status::clif::updatestatus(c, sd, SP_POW + (i - PARAM_POW) as i32);
            crate::status::clif::updatestatus(c, sd, SP_UPOW + (i - PARAM_POW) as i32);
        }
    }
    crate::status::calc_pc(c, sd, crate::status::defs::SCO_FORCE);
    out.say(&format_msg(msg_txt(738), &[&"status"])); // Clone '%s' is done.
    0
}

/// ACMD_FUNC(fullstrip): @fullstrip <char name/ID>: everything the player wears comes off (pc_unequipitem flag 2:
/// forced, no status recalculation — rAthena's choice, kept). No group level check and no message on success. The
/// name is map_nick2sd(name, false), else map_id2sd(atoi(name)) (an account id).
fn fullstrip(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    if nullpo(c, sd) {
        return -1;
    }
    let Some(name) = player_name(message) else {
        out.say(msg_txt(349)); // Please enter a player name (usage: @fullstrip <char name/ID>).
        return -1;
    };
    let tsd = pc::nick2sd(c, &name, false).map(|u| u.id).or_else(|| map_id2sd(c, atoi(&name)));
    let Some(tsd) = tsd else {
        out.say(msg_txt(3)); // Character not found.
        return -1;
    };
    for i in 0..EQI_MAX {
        // tsd->equip_index[i], read again each time (a two-handed item frees both hands at once)
        let idx = inv::st(c, tsd).map(|s| s.equip_index[i]).unwrap_or(-1);
        if idx >= 0 {
            pc_equip::pc_unequipitem(c, tsd, idx as i32, 2);
        }
    }
    0
}

// ---------------------------------------------------------------------------------------------- family

/// clif.cpp clif_wedding_effect: ZC_CONGRATULATION with the unit's id to its area.
pub(crate) fn clif_wedding_effect(c: &Ctx, bl: &Unit) {
    use crate::net::packets::PACKET_ZC_CONGRATULATION;
    let p = PACKET_ZC_CONGRATULATION { packetType: PACKET_ZC_CONGRATULATION::ID as i16, GID: bl.id as i32 };
    clif::send(c, &p.to_vec(), bl, Target::Area);
}

/// atcommand.cpp getring: a wedding ring for the player's sex, forged (card[0] CARD0_FORGE) with the partner's char id
/// in card[2] / card[3]; a refusal is shown with clif_additem's failure entry.
fn getring(c: &Ctx, aid: u32) {
    let partner_id = pc::character_of(c, aid).map(|ch| ch.partner_id).unwrap_or(0);
    let mut item_tmp = Item::new(ring_of(c, aid));
    item_tmp.identify = 1;
    item_tmp.card0 = CARD0_FORGE;
    let (w0, w1) = ring_cards(partner_id);
    item_tmp.card2 = w0;
    item_tmp.card3 = w1;
    let flag = inv::pc_additem(c, aid, &mut item_tmp, 1, crate::map_log::LOG_TYPE_COMMAND);
    if flag != 0 {
        ci::additem(c, aid, 0, 0, flag);
    }
}

/// ACMD_FUNC(marry): @marry <char name>: the invoker and the named player marry (pc_marriage); the wedding effect
/// shows around the partner (and around the invoker too when they are on different maps) and both get their ring.
/// Each needs a free inventory slot. rAthena quirk kept in the weight test: `if (w = itemdb_weight(ring) && w +
/// sd->weight > sd->max_weight)` assigns the whole `&&` to w, so it tests "the ring weighs something and the player is
/// already over the limit" (w is 0 then) — the rings weigh 0 in the item db, so it never refuses.
fn marry(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    if nullpo(c, sd) {
        return -1;
    }
    let Some(name) = player_name(message) else {
        out.say(msg_txt(1172)); // Usage: @marry <char name>
        return -1;
    };
    let Some(pl) = pc::nick2sd(c, &name, false) else {
        out.say(msg_txt(3)); // Character not found.
        return -1;
    };
    let pl_sd = pl.id;
    if inv::pc_inventoryblank(c, sd) == 0 {
        msg_red(c, sd, ci::MSI_CANT_GET_ITEM_BECAUSE_COUNT);
        return -1;
    }
    if inv::pc_inventoryblank(c, pl_sd) == 0 {
        msg_red(c, pl_sd, ci::MSI_CANT_GET_ITEM_BECAUSE_COUNT);
        return -1;
    }
    // w = (itemdb_weight(ring) && w + weight > max_weight)
    let over = |aid: u32, w: u32| -> bool {
        itemdb::itemdb_weight(ring_of(c, aid)) != 0 && inv::st(c, aid).map(|s| w.wrapping_add(s.weight) > s.max_weight).unwrap_or(false)
    };
    let w = over(sd, 0) as u32;
    if w != 0 {
        msg_red(c, sd, ci::MSI_CANT_GET_ITEM_BECAUSE_WEIGHT);
        return -1;
    }
    if over(pl_sd, w) {
        msg_red(c, pl_sd, ci::MSI_CANT_GET_ITEM_BECAUSE_WEIGHT);
        return -1;
    }
    if family::pc_marriage(c, sd, pl_sd) {
        out.say(msg_txt(1173)); // They are married... wish them well.
        let (me, partner) = (unit::get(c, sd), unit::get(c, pl_sd));
        if let Some(p) = &partner {
            clif_wedding_effect(c, p);
        }
        if let (Some(m), Some(p)) = (&me, &partner) {
            if p.map != m.map {
                clif_wedding_effect(c, m);
            }
        }
        getring(c, sd); // Auto-give named rings (Aru)
        getring(c, pl_sd);
        return 0;
    }
    out.say(msg_txt(1174)); // The two cannot wed because one is either a baby or already married.
    -1
}

/// ACMD_FUNC(divorce): pc_divorce (both online: the rings go and both get ZC_DIVORCE; the partner offline: the char
/// server's divorce, family.rs).
fn divorce(c: &Ctx, out: &Out, sd: u32) -> i32 {
    if nullpo(c, sd) {
        return -1;
    }
    let name = pc::character_of(c, sd).map(|ch| ch.name).unwrap_or_default();
    if !family::pc_divorce(c, sd) {
        out.say(&format_msg(msg_txt(1175), &[&name])); // '%s' is not married.
        return -1;
    }
    out.say(&format_msg(msg_txt(1176), &[&name])); // '%s' and his/her partner are now divorced.
    0
}

/// ACMD_FUNC(adopt): @adopt <char name>: pc_try_adopt(invoker, the invoker's partner, the named player); when allowed
/// the baby gets the request (adopt_invite, clif_Adopt_request); otherwise msg 744 + response - 1 for the responses
/// before ADOPT_MORE_CHILDREN (the later ones were answered to the client by pc_try_adopt itself).
fn adopt(c: &Ctx, out: &Out, sd: u32, command: &str, message: &str) -> i32 {
    if nullpo(c, sd) {
        return -1;
    }
    let Some(name) = player_name(message) else {
        out.say(&format_msg(msg_txt(435), &[&command])); // Please enter a player name (usage: %s <char name>).
        return -1;
    };
    let Some(b_sd) = pc::nick2sd(c, &name, false).map(|u| u.id) else {
        out.say(msg_txt(3)); // Character not found.
        return -1;
    };
    // map_charid2sd(sd->status.partner_id)
    let partner = || pc::character_of(c, sd).and_then(|ch| pc::charid2sd(c, ch.partner_id)).map(|u| u.id);
    let response = family::pc_try_adopt(c, Some(sd), partner(), Some(b_sd));
    if response == family::ADOPT_ALLOWED {
        let p_sd = partner().unwrap_or(0);
        let mut m = crate::pc_misc::pcm(c, b_sd);
        m.adopt_invite = sd; // sd->status.account_id
        crate::pc_misc::put_pcm(c, &m);
        family::clif_adopt_request(c, b_sd, sd, p_sd);
        return 0;
    }
    if response < family::ADOPT_MORE_CHILDREN {
        // No displaymessage for client-type responses
        out.say(msg_txt((744 + response - 1) as u16));
    }
    -1
}

// ---------------------------------------------------------------------------------------------- resurrection

/// itemdb.cpp ItemGroupDatabase::item_exists_pc: the inventory index of the first item of the group the player carries
/// (sub groups and entries in the unordered_maps' iteration order, as db/items/group.rs keeps them), -1 when none.
fn item_exists_pc(c: &Ctx, aid: u32, group_id: u16) -> i16 {
    let Some(group) = itemdb::group::item_group(group_id) else { return -1 };
    if group.random.is_empty() {
        return -1;
    }
    for random in &group.random {
        for it in &random.data {
            let item_position = inv::pc_search_inventory(c, aid, it.nameid);
            if item_position != -1 {
                return item_position;
            }
        }
    }
    -1
}

/// pc.cpp pc_revive_item: a dead player (with no respawn timer, not under SC_HELLPOWER) comes back with a Token of
/// Siegfried (IG_TOKEN_OF_SIEGFRIED: 100% HP and SP, the token is used up) or under SC_LIGHT_OF_REGENE (val2% HP, no
/// SP; the status ends), with the ALL_RESURRECTION effect. Also clif_parse_AutoRevive's (CZ_STANDING_RESURRECTION)
/// whole body.
pub(crate) fn pc_revive_item(c: &Ctx, aid: u32) -> bool {
    use crate::status::consts::{SC_HELLPOWER, SC_LIGHT_OF_REGENE};
    use crate::status::sc;
    if pc::get(c, aid).is_none() {
        return false; // nullpo_retr(false, sd)
    }
    if !pc::isdead(c, aid) || crate::battle::state::get(c, aid).respawn_tid != 0 {
        return false;
    }
    if sc::has_sce(c, aid, SC_HELLPOWER) {
        return false; // Cannot resurrect while under the effect of SC_HELLPOWER.
    }
    let item_position = item_exists_pc(c, aid, IG_TOKEN_OF_SIEGFRIED);
    let (mut hp, mut sp): (u8, u8) = (100, 100);
    if item_position < 0 {
        match sc::get_sce(c, aid, SC_LIGHT_OF_REGENE) {
            Some(e) => {
                hp = e.val2 as u8; // uint8 hp = …->val2
                sp = 0;
            }
            None => return false,
        }
    }
    if crate::status::hp::status_revive(c, aid, hp, sp, 0) == 0 {
        return false;
    }
    if item_position < 0 {
        sc::status_change_end(c, aid, SC_LIGHT_OF_REGENE, 0);
    } else {
        inv::pc_delitem(c, aid, item_position as i32, 1, 0, 1, crate::map_log::LOG_TYPE_CONSUME);
    }
    if let Some(u) = unit::get(c, aid) {
        crate::battle::atcommand::clif_skill_nodamage(c, Some(&u), &u, crate::db::skills::ALL_RESURRECTION, 4, true);
    }
    true
}

/// ACMD_FUNC(resurrect): pc_revive_item (ATCMD_NOCONSOLE); a failure is only the dispatcher's "%s failed.".
fn resurrect(c: &Ctx, sd: u32) -> i32 {
    if nullpo(c, sd) {
        return -1;
    }
    if !pc_revive_item(c, sd) {
        return -1;
    }
    0
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn item_types() {
        for t in [-1i8, 0, 2, 3, 4, 5, 6, 7, 8, 10] {
            assert!(valid_type(t), "{t}");
        }
        for t in [1i8, 9, 11, 12, -2, 127, -128] {
            assert!(!valid_type(t), "{t}");
        }
        // int8 type = atoi(message)
        assert_eq!(atoi("256") as i8, 0);
        assert_eq!(atoi("259") as i8, 3);
        assert_eq!(atoi("-1") as i8, -1);
        assert_eq!(atoi("abc") as i8, 0);
    }

    #[test]
    fn delitem_arguments() {
        assert_eq!(scan_name_amount("\"Red Potion\" 5"), Some(("Red Potion".to_string(), 5)));
        assert_eq!(scan_name_amount("\"Red Potion\"5"), Some(("Red Potion".to_string(), 5)));
        assert_eq!(scan_name_amount("Red_Potion 2"), Some(("Red_Potion".to_string(), 2)));
        assert_eq!(scan_name_amount("501 -3"), Some(("501".to_string(), -3)));
        // the quoted form without an amount falls back to %99s, which stops at the space
        assert_eq!(scan_name_amount("\"Red Potion\""), None);
        assert_eq!(scan_name_amount("\"Red Potion\" x"), None);
        assert_eq!(scan_name_amount("\"Red 7"), Some(("\"Red".to_string(), 7)));
        assert_eq!(scan_name_amount("501"), None);
        assert_eq!(scan_name_amount("   "), None);
    }

    #[test]
    fn setcard_arguments() {
        assert_eq!(scan_three("2 0 4001"), Some((2, 0, 4001)));
        assert_eq!(scan_three("2 0"), None);
        assert_eq!(scan_three("2 x 1"), None);
        // %11d: 11 characters, then the C cast to int
        assert_eq!(scan_three("99999999999 1 1"), Some((99999999999i64 as i32, 1, 1)));
    }

    #[test]
    fn ring_words() {
        assert_eq!(ring_cards(150001), (150001 & 0xFFFF, 2));
        assert_eq!(ring_cards(0), (0, 0));
    }
}
