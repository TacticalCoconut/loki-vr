//! atcommand.cpp: the item system's @commands — @item / @itembound, @item2 / @itembound2, @zeny, @storeall,
//! @storage, @cart. Routed from atcommand.rs `run` by their ACMD_FUNC name.

use crate::clif;
use crate::cstr::{atoi, Scan};
use crate::data::{self, msg_txt};
use crate::db::items as itemdb;
use crate::inventory::*;
use crate::pc;
use crate::unit::Unit;
use crate::world::Ctx;

/// Replies go to the invoker (rAthena's fd).
struct Out<'a> {
    c: &'a Ctx<'a>,
    fd: Option<&'a Unit>,
}

impl Out<'_> {
    fn say(&self, text: &str) {
        if let Some(u) = self.fd {
            clif::displaymessage(self.c, u, text);
        }
    }
}

/// Runs `func` for the item commands; None when `func` is not one of them.
pub fn run(c: &Ctx, fd: Option<&Unit>, sd: u32, func: &str, command: &str, message: &str) -> Option<i32> {
    let out = Out { c, fd };
    Some(match func {
        "item" => item(c, &out, sd, command, message),
        "item2" => item2(c, &out, sd, command, message),
        "zeny" => zeny(c, &out, sd, message),
        "storeall" => storeall(c, &out, sd),
        "storage" => storage(c, &out, sd),
        "cart" => cart(c, &out, sd, command, message),
        _ => return None,
    })
}

/// strtoul(s, nullptr, 10) on a token.
fn strtoul(s: &str) -> u32 {
    let b = s.trim_start();
    let digits: String = b.chars().take_while(|c| c.is_ascii_digit()).collect();
    digits.parse::<u64>().map(|v| v.min(u32::MAX as u64) as u32).unwrap_or(0)
}

/// `item_db.searchname(name)`, else `item_db.find(strtoul(name))`.
fn search_item(name: &str) -> Option<&'static itemdb::ItemData> {
    itemdb::itemdb_searchname(name).or_else(|| itemdb::itemdb_exists(strtoul(name)))
}

use crate::pet::pet_create_egg;

/// ACMD_FUNC(item): @item / @itembound.
fn item(c: &Ctx, out: &Out, aid: u32, command: &str, message: &str) -> i32 {
    let parent_cmd = data::atcommands().check_alias(command.get(1..).unwrap_or(""));
    let b = crate::net::wire::latin1(message);
    let mut item_name = String::new();
    let mut number: i32 = 0;
    let mut bound: i32 = BOUND_NONE as i32;
    if parent_cmd.eq_ignore_ascii_case("itembound") {
        // sscanf(message, "\"%99[^\"]\" %11d %11d", …) < 3 && sscanf(message, "%99s %11d %11d", …) < 3
        let quoted = {
            let mut s = Scan::new(&b);
            let mut n = 0;
            if s.lit(b'"') {
                if let Some(name) = s.set(99, b"\"") {
                    item_name = name;
                    n += 1;
                    if s.lit(b'"') {
                        if let Some(v) = s.int(11) {
                            number = v as i32;
                            n += 1;
                            if let Some(v) = s.int(11) {
                                bound = v as i32;
                                n += 1;
                            }
                        }
                    }
                }
            }
            n
        };
        let plain = if quoted < 3 {
            let mut s = Scan::new(&b);
            let mut n = 0;
            if let Some(name) = s.word(99) {
                item_name = name;
                n += 1;
                if let Some(v) = s.int(11) {
                    number = v as i32;
                    n += 1;
                    if let Some(v) = s.int(11) {
                        bound = v as i32;
                        n += 1;
                    }
                }
            }
            n
        } else {
            3
        };
        if message.is_empty() || (quoted < 3 && plain < 3) {
            out.say(msg_txt(295)); // Please enter an item name or ID (usage: @item <item name/ID> <quantity> <bound_type>).
            out.say(msg_txt(298)); // Invalid bound type
            return -1;
        }
        if bound <= BOUND_NONE as i32 || bound >= BOUND_MAX as i32 {
            out.say(msg_txt(298)); // Invalid bound type
            return -1;
        }
    } else {
        let quoted = {
            let mut s = Scan::new(&b);
            let mut n = 0;
            if s.lit(b'"') {
                if let Some(name) = s.set(99, b"\"") {
                    item_name = name;
                    n += 1;
                    if s.lit(b'"') {
                        if let Some(v) = s.int(11) {
                            number = v as i32;
                            n += 1;
                        }
                    }
                }
            }
            n
        };
        let plain = if quoted < 1 {
            let mut s = Scan::new(&b);
            let mut n = 0;
            if let Some(name) = s.word(99) {
                item_name = name;
                n += 1;
                if let Some(v) = s.int(11) {
                    number = v as i32;
                    n += 1;
                }
            }
            n
        } else {
            1
        };
        if message.is_empty() || (quoted < 1 && plain < 1) {
            out.say(msg_txt(983)); // Please enter an item name or ID (usage: @item <item name/ID> <quantity>).
            return -1;
        }
    }
    let mut items = Vec::new();
    for tok in item_name.split(':').filter(|t| !t.is_empty()) {
        let Some(it) = search_item(tok) else {
            out.say(msg_txt(19)); // Invalid item ID or name.
            return -1;
        };
        items.push(it);
    }
    if number <= 0 {
        number = 1;
    }
    let mut get_count = number;
    let mut flag = 0u8;
    // Produce items in list
    for it in items {
        let item_id = it.nameid;
        // Check if it's stackable.
        if !it.is_stackable() {
            get_count = 1;
        }
        let mut i = 0;
        while i < number {
            // if not pet egg
            if !pet_create_egg(c, aid, item_id) {
                let mut item_tmp = Item::new(item_id);
                item_tmp.identify = 1;
                item_tmp.bound = bound as u8;
                flag = pc_additem(c, aid, &mut item_tmp, get_count, crate::map_log::LOG_TYPE_COMMAND);
                if flag != 0 {
                    crate::clif_items::additem(c, aid, 0, 0, flag);
                }
            }
            i += get_count;
        }
    }
    if flag == 0 {
        out.say(msg_txt(18)); // Item created.
    }
    0
}

/// ACMD_FUNC(item2): @item2 / @itembound2.
fn item2(c: &Ctx, out: &Out, aid: u32, command: &str, message: &str) -> i32 {
    let parent_cmd = data::atcommands().check_alias(command.get(1..).unwrap_or(""));
    let b = crate::net::wire::latin1(message);
    let mut item_name = String::new();
    // number, identify, refine, attr, c1, c2, c3, c4, bound
    let mut v = [0i32; 9];
    let scan_all = |quoted: bool, count: usize, name: &mut String, v: &mut [i32; 9]| -> usize {
        let mut s = Scan::new(&b);
        let mut n = 0;
        if quoted {
            if !s.lit(b'"') {
                return 0;
            }
            match s.set(99, b"\"") {
                Some(x) => *name = x,
                None => return 0,
            }
            n += 1;
            if !s.lit(b'"') {
                return n;
            }
        } else {
            match s.word(99) {
                Some(x) => *name = x,
                None => return 0,
            }
            n += 1;
        }
        for e in v.iter_mut().take(count - 1) {
            match s.int(11) {
                Some(x) => {
                    *e = x as i32;
                    n += 1;
                }
                None => break,
            }
        }
        n
    };
    // (rAthena compares parent_cmd + 1 with "itembound2", so @itembound2 is parsed like @item2)
    let itembound2 = parent_cmd.get(1..).map(|p| p.eq_ignore_ascii_case("itembound2")).unwrap_or(false);
    if itembound2 {
        let ok = message.is_empty() || (scan_all(true, 10, &mut item_name, &mut v) < 10 && scan_all(false, 10, &mut item_name, &mut v) < 10);
        if ok {
            out.say(msg_txt(296));
            out.say(msg_txt(297));
            out.say(msg_txt(298)); // Invalid bound type
            return -1;
        }
        if v[8] <= BOUND_NONE as i32 || v[8] >= BOUND_MAX as i32 {
            out.say(msg_txt(298));
            return -1;
        }
    } else if message.is_empty() || (scan_all(true, 9, &mut item_name, &mut v) < 9 && scan_all(false, 9, &mut item_name, &mut v) < 9) {
        out.say(msg_txt(984)); // Please enter all parameters (usage: @item2 <item name/ID> <quantity>
        out.say(msg_txt(985)); //   <identify_flag> <refine> <attribute> <card1> <card2> <card3> <card4>).
        return -1;
    }
    let (mut number, mut identify, mut refine, mut attr) = (v[0], v[1], v[2], v[3]);
    let (c1, c2, c3, c4, bound) = (v[4], v[5], v[6], v[7], if itembound2 { v[8] } else { BOUND_NONE as i32 });
    if number <= 0 {
        number = 1;
    }
    let Some(item_data) = search_item(&item_name) else {
        out.say(msg_txt(19)); // Invalid item ID or name.
        return -1;
    };
    let (loop_n, get_count) = if !item_data.is_stackable() { (number, 1) } else { (1, number) };
    if itemdb::itemdb_isequip2(item_data) {
        refine = refine.clamp(0, MAX_REFINE);
    } else {
        // All other items cannot be refined and are always identified
        identify = 1;
        refine = 0;
        attr = 0;
    }
    let mut flag = 0u8;
    for _ in 0..loop_n {
        if !pet_create_egg(c, aid, item_data.nameid) {
            let mut item_tmp = Item::new(item_data.nameid);
            item_tmp.identify = identify as u8;
            item_tmp.refine = refine as u8;
            item_tmp.attribute = attr as u8;
            item_tmp.card0 = c1 as u32;
            item_tmp.card1 = c2 as u32;
            item_tmp.card2 = c3 as u32;
            item_tmp.card3 = c4 as u32;
            item_tmp.bound = bound as u8;
            flag = pc_additem(c, aid, &mut item_tmp, get_count, crate::map_log::LOG_TYPE_COMMAND);
            if flag != 0 {
                crate::clif_items::additem(c, aid, 0, 0, flag);
            }
        }
    }
    if flag == 0 {
        out.say(msg_txt(18)); // Item created.
    }
    0
}

/// ACMD_FUNC(zeny).
fn zeny(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    let mut zeny = atoi(message);
    if message.is_empty() || zeny == 0 {
        out.say(msg_txt(1012)); // Please enter an amount (usage: @zeny <amount>).
        return -1;
    }
    let ret;
    if zeny > 0 {
        ret = pc_getzeny(c, aid, zeny as i64, crate::map_log::LOG_TYPE_COMMAND, 0);
        if ret == 1 {
            out.say(msg_txt(149)); // Unable to increase the number/value.
        }
    } else {
        let have = status(c, aid).map(|ch| ch.zeny as i64).unwrap_or(0);
        if have < -(zeny as i64) {
            zeny = -(have as i32);
        }
        ret = pc_payzeny(c, aid, -(zeny as i64), crate::map_log::LOG_TYPE_COMMAND, 0);
        if ret == 1 {
            out.say(msg_txt(41)); // Unable to decrease the number/value.
        }
    }
    if ret == 0 {
        out.say(msg_txt(176)); // ret=0 mean cmd success
    }
    0
}

/// ACMD_FUNC(storeall).
fn storeall(c: &Ctx, out: &Out, aid: u32) -> i32 {
    if pc::get(c, aid).map(|p| p.storage_flag).unwrap_or(0) != 1 {
        // Open storage.
        if crate::storage::storage_storageopen(c, aid) == 1 {
            out.say(msg_txt(1161)); // You currently cannot open your storage.
            return -1;
        }
    }
    for i in 0..MAX_INVENTORY {
        let Some(r) = inv_get(c, aid, i) else { continue };
        if r.item.amount != 0 {
            if r.item.equip != 0 {
                crate::pc_equip::pc_unequipitem(c, aid, i as i32, 3);
            }
            if itemdb::itemdb_ishatched_egg(r.item.card0, r.item.attribute) {
                // pet_return_egg(sd, sd->pd) (pet.rs)
                if let Some(pd) = crate::pet::data::of_master(c, aid) {
                    crate::pet::pet_return_egg(c, aid, pd.id);
                }
            }
            crate::pc_equip::pc_equipswitch_remove(c, aid, i);
            let amount = inv_get(c, aid, i).map(|r| r.item.amount as i32).unwrap_or(0);
            crate::storage::storage_storageadd(c, aid, i as i32, amount);
        }
    }
    crate::storage::storage_storageclose(c, aid);
    out.say(msg_txt(1162)); // All items stored.
    0
}

/// ACMD_FUNC(storage).
fn storage(c: &Ctx, out: &Out, aid: u32) -> i32 {
    let Some(p) = pc::get(c, aid) else { return -1 };
    if p.npc_id != 0 || p.vending || p.buyingstore || p.trading || p.storage_flag != 0 {
        return -1;
    }
    if crate::storage::storage_storageopen(c, aid) == 1 {
        // Already open.
        out.say(msg_txt(250)); // You have already opened your storage. Close it first.
        return -1;
    }
    out.say(msg_txt(919)); // Storage opened.
    0
}

/// ACMD_FUNC(cart): MC_PUSHCART is lent for the call when the character does not have it (MC_CART_MDFY).
fn cart(c: &Ctx, out: &Out, aid: u32, command: &str, message: &str) -> i32 {
    let val = atoi(message);
    let need_skill = crate::skill::checkskill(c, aid, crate::db::skills::MC_PUSHCART) == 0;
    if message.is_empty() || val < 0 || val > crate::cart::MAX_CARTS {
        out.say(&data::format_msg(msg_txt(1390), &[&command, &crate::cart::MAX_CARTS])); // Unknown Cart (usage: %s <0-%d>).
        return -1;
    }
    if val == 0 && !pc_iscarton(c, aid) {
        out.say(msg_txt(1391)); // You do not possess a cart to be removed
        return -1;
    }
    if crate::db::skills::skill_get_index(crate::db::skills::MC_PUSHCART) == 0 {
        return -1;
    }
    let lent = if need_skill { crate::skill::lend_skill(c, aid, crate::db::skills::MC_PUSHCART, 1) } else { None };
    if !crate::cart::pc_setcart(c, aid, val) {
        if let Some(id) = lent {
            crate::skill::unlend_skill(c, id);
        }
        return -1; // @cart failed
    }
    if let Some(id) = lent {
        crate::skill::unlend_skill(c, id);
    }
    out.say(msg_txt(1392)); // Cart Added
    0
}
