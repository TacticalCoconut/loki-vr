//! atcommand.cpp: the item-window @commands — @itemreset, @refine, @grade, @produce, @identify, @identifyall,
//! @refineui, @stylist, @enchantgradeui. Routed from atcommand.rs `run` by their ACMD_FUNC name.

use crate::clif;
use crate::clif_items as ci;
use crate::cstr::Scan;
use crate::data::{self, msg_txt};
use crate::db::items::{self as itemdb, CARD0_FORGE, IT_WEAPON};
use crate::db::items::enchantgrade::MAX_ENCHANTGRADE;
use crate::inventory::{self as inv, Item, EQI_AMMO, EQI_MAX, MAX_INVENTORY, MAX_REFINE};
use crate::inventory::{EQP_ACC_L, EQP_ACC_R, EQP_ARMOR, EQP_GARMENT, EQP_HAND_L, EQP_HAND_R, EQP_HEAD_LOW, EQP_HEAD_MID, EQP_HEAD_TOP, EQP_SHOES};
use crate::inventory::{EQP_SHADOW_ACC_L, EQP_SHADOW_ACC_R, EQP_SHADOW_ARMOR, EQP_SHADOW_SHIELD, EQP_SHADOW_SHOES, EQP_SHADOW_WEAPON};
use crate::pc_equip;
use crate::unit::Unit;
use crate::world::Ctx;

use super::*;

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

/// Runs `func` for the item-window commands; None when `func` is not one of them.
pub fn run(c: &Ctx, fd: Option<&Unit>, sd: u32, func: &str, _command: &str, message: &str) -> Option<i32> {
    let out = Out { c, fd };
    Some(match func {
        "itemreset" => itemreset(c, &out, sd),
        "refine" => refine(c, &out, sd, message),
        "grade" => grade(c, &out, sd, message),
        "produce" => produce(c, &out, sd, message),
        "identify" => identify(c, &out, sd),
        "identifyall" => {
            super::identify::pc_identifyall(c, sd, true);
            0
        }
        "refineui" => refineui(c, &out, sd),
        "stylist" => stylist(c, &out, sd),
        "enchantgradeui" => {
            clif_ui_open(c, sd, OUT_UI_ENCHANTGRADE, 0);
            0
        }
        _ => return None,
    })
}

/// ACMD_FUNC(itemreset): every unequipped item (but hatched eggs) is deleted.
fn itemreset(c: &Ctx, out: &Out, aid: u32) -> i32 {
    for i in 0..MAX_INVENTORY {
        let Some(r) = inv::inv_get(c, aid, i) else { continue };
        let it = &r.item;
        if it.amount != 0 && it.equip == 0 && !itemdb::itemdb_ishatched_egg(it.card0, it.attribute) {
            inv::pc_delitem(c, aid, i as i32, it.amount as i32, 0, 0, crate::map_log::LOG_TYPE_COMMAND);
        }
    }
    out.say(msg_txt(20)); // All of your items have been removed.
    0
}

/// "%11d %11d": (position, amount) and how many sscanf converted.
fn two_ints(message: &str) -> (i32, i32, i32) {
    let b = crate::net::wire::latin1(message);
    let mut s = Scan::new(&b);
    let mut n = 0;
    let (mut a, mut v) = (0i32, 0i32);
    if let Some(x) = s.int(11) {
        a = x as i32;
        n += 1;
        if let Some(y) = s.int(11) {
            v = y as i32;
            n += 1;
        }
    }
    (n, a, v)
}

/// The position list of @refine / @grade's usage text (msg 997..1006).
fn position_help(out: &Out) {
    for (m, pos) in [
        (997, EQP_HEAD_LOW),
        (998, EQP_HAND_R),
        (999, EQP_GARMENT),
        (1000, EQP_ACC_L),
        (1001, EQP_ARMOR),
        (1002, EQP_HAND_L),
        (1003, EQP_SHOES),
        (1004, EQP_ACC_R),
        (1005, EQP_HEAD_TOP),
        (1006, EQP_HEAD_MID),
    ] {
        out.say(&data::format_msg(msg_txt(m), &[&pos]));
    }
}

/// The worn items @refine / @grade walk: (equip index j → inventory index), skipping ammo and the second index of a
/// two-slot item (pc_is_same_equip_index), read live as rAthena reads sd->equip_index[j].
fn worn(c: &Ctx, aid: u32, j: usize) -> Option<i16> {
    let ei = inv::st(c, aid)?.equip_index;
    let i = ei[j];
    if i < 0 || j == EQI_AMMO || pc_equip::pc_is_same_equip_index(j, &ei, i) {
        return None;
    }
    Some(i)
}

/// ACMD_FUNC(refine): @refine <equip position> <+/- amount>.
fn refine(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    let (n, position, refine) = two_ints(message);
    if message.is_empty() || n < 2 {
        out.say(msg_txt(996)); // Please enter a position and an amount (usage: @refine <equip position> <+/- amount>).
        position_help(out);
        for (m, pos) in [
            (1521, EQP_SHADOW_ARMOR),
            (1522, EQP_SHADOW_WEAPON),
            (1523, EQP_SHADOW_SHIELD),
            (1524, EQP_SHADOW_SHOES),
            (1525, EQP_SHADOW_ACC_R),
            (1526, EQP_SHADOW_ACC_L),
        ] {
            out.say(&data::format_msg(msg_txt(m), &[&pos]));
        }
        return -1;
    }
    let refine = refine.clamp(-MAX_REFINE, MAX_REFINE);
    let mut count = 0;
    for j in 0..EQI_MAX {
        let Some(i) = worn(c, aid, j) else { continue };
        let Some(mut r) = inv::inv_get(c, aid, i as usize) else { continue };
        if position != 0 && r.item.equip & position as u32 == 0 {
            continue;
        }
        let final_refine = (r.item.refine as i32 + refine).clamp(0, MAX_REFINE);
        if r.item.refine as i32 != final_refine {
            r.item.refine = final_refine as u8;
            let current_position = r.item.equip;
            inv::inv_put(c, &r);
            pc_equip::pc_unequipitem(c, aid, i as i32, 3);
            ci::refine(c, aid, i as usize, ci::ITEMREFINING_SUCCESS);
            ci::delitem(c, aid, i as i32, 1, 3);
            ci::additem(c, aid, i as i32, 1, 0);
            pc_equip::pc_equipitem(c, aid, i, current_position, false);
            misceffect(c, aid, ci::NOTIFYEFFECT_REFINE_SUCCESS as i32);
            let d = inv::data_of(&r.item);
            if d.r#type == IT_WEAPON {
                crate::achievement::achievement_update_objective(c, Some(aid), crate::db::misc::achievement::AG_ENCHANT_SUCCESS, &[d.weapon_level as i32, r.item.refine as i32]);
            }
            count += 1;
        }
    }
    if count == 0 {
        out.say(msg_txt(166)); // No item has been refined.
    } else if count == 1 {
        out.say(msg_txt(167)); // 1 item has been refined.
    } else {
        out.say(&data::format_msg(msg_txt(168), &[&count])); // %d items have been refined.
    }
    0
}

/// ACMD_FUNC(grade): @grade <equip position> <+/- amount>.
fn grade(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    let (n, position, grade) = two_ints(message);
    if message.is_empty() || n < 2 {
        out.say(msg_txt(1519)); // Please enter a position and an amount (usage: @grade <equip position> <+/- amount>).
        position_help(out);
        return -1;
    }
    let max = MAX_ENCHANTGRADE as i32;
    let grade = grade.clamp(-max, max);
    let mut count = 0;
    for j in 0..EQI_MAX {
        let Some(i) = worn(c, aid, j) else { continue };
        let Some(mut r) = inv::inv_get(c, aid, i as usize) else { continue };
        if position != 0 && r.item.equip & position as u32 == 0 {
            continue;
        }
        let final_grade = (r.item.enchantgrade as i32 + grade).clamp(0, max);
        if r.item.enchantgrade as i32 != final_grade {
            r.item.enchantgrade = final_grade as u8;
            let current_position = r.item.equip;
            inv::inv_put(c, &r);
            pc_equip::pc_unequipitem(c, aid, i as i32, 3);
            ci::delitem(c, aid, i as i32, 1, 3);
            ci::additem(c, aid, i as i32, 1, 0);
            pc_equip::pc_equipitem(c, aid, i, current_position, false);
            misceffect(c, aid, ci::NOTIFYEFFECT_REFINE_SUCCESS as i32);
            count += 1;
        }
    }
    out.say(&data::format_msg(msg_txt(1520), &[&count])); // %d items have been graded.
    0
}

/// strtoul(s, nullptr, 10).
fn strtoul(s: &str) -> u32 {
    let b = s.trim_start();
    let b = b.strip_prefix('+').unwrap_or(b);
    let digits: String = b.chars().take_while(|c| c.is_ascii_digit()).collect();
    digits.parse::<u64>().map(|v| v.min(u32::MAX as u64) as u32).unwrap_or(0)
}

/// ACMD_FUNC(produce): @produce <equip name/ID> <element> <# of very's> — a forged equipment item signed by the
/// player (the produce effect is shown).
fn produce(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    let b = crate::net::wire::latin1(message);
    let (mut item_name, mut attribute, mut star) = (String::new(), 0i32, 0i32);
    // sscanf(message, "\"%99[^\"]\" %11d %11d", …) < 1
    let n1 = {
        let mut s = Scan::new(&b);
        let mut n = 0;
        if s.lit(b'"') {
            if let Some(name) = s.set(99, b"\"") {
                item_name = name;
                n += 1;
                if s.lit(b'"') {
                    s.ws();
                    if let Some(a) = s.int(11) {
                        attribute = a as i32;
                        n += 1;
                        s.ws();
                        if let Some(v) = s.int(11) {
                            star = v as i32;
                            n += 1;
                        }
                    }
                }
            }
        }
        n
    };
    let ok = n1 >= 1 || {
        // sscanf(message, "%99s %11d %11d", …) < 1
        let mut s = Scan::new(&b);
        let mut n = 0;
        if let Some(name) = s.word(99) {
            item_name = name;
            n += 1;
            if let Some(a) = s.int(11) {
                attribute = a as i32;
                n += 1;
                if let Some(v) = s.int(11) {
                    star = v as i32;
                    n += 1;
                }
            }
        }
        n >= 1
    };
    if message.is_empty() || !ok {
        out.say(msg_txt(1007)); // Please enter at least one item name/ID (usage: @produce <equip name/ID> <element> <# of very's>).
        return -1;
    }
    let item_data = itemdb::itemdb_searchname(&item_name).or_else(|| itemdb::itemdb_exists(strtoul(&item_name)));
    let Some(item_data) = item_data else {
        out.say(msg_txt(170)); // This item is not an equipment.
        return -1;
    };
    let item_id = item_data.nameid;
    if itemdb::itemdb_isequip2(item_data) {
        const MIN_ATTRIBUTE: i32 = 0;
        const MAX_ATTRIBUTE: i32 = 4;
        const ATTRIBUTE_NORMAL: i32 = 0;
        const MIN_STAR: i32 = 0;
        const MAX_STAR: i32 = 3;
        if !(MIN_ATTRIBUTE..=MAX_ATTRIBUTE).contains(&attribute) {
            attribute = ATTRIBUTE_NORMAL;
        }
        if !(MIN_STAR..=MAX_STAR).contains(&star) {
            star = 0;
        }
        let char_id = crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
        let mut tmp = Item::new(item_id);
        tmp.amount = 1;
        tmp.identify = 1;
        tmp.card0 = CARD0_FORGE;
        tmp.card1 = if item_data.r#type == IT_WEAPON { (((star * 5) << 8) + attribute) as u32 } else { 0 };
        tmp.card2 = char_id & 0xffff;
        tmp.card3 = char_id >> 16;
        ci::produceeffect(c, aid, 0, item_id);
        misceffect(c, aid, ci::NOTIFYEFFECT_REFINE_SUCCESS as i32);
        let flag = inv::pc_additem(c, aid, &mut tmp, 1, crate::map_log::LOG_TYPE_COMMAND);
        if flag != 0 {
            ci::additem(c, aid, 0, 0, flag);
        }
    } else {
        // The item (%u: '%s') is not equipable.
        out.say(&data::format_msg(msg_txt(169), &[&item_id, &item_data.name]));
        return -1;
    }
    0
}

/// ACMD_FUNC(identify): the identify list (GM's magnifier), or "There are no items to appraise.".
fn identify(c: &Ctx, out: &Out, aid: u32) -> i32 {
    let num = inv::inv_list(c, aid).iter().filter(|r| r.item.nameid > 0 && r.item.identify != 1).count();
    if num > 0 {
        super::identify::clif_item_identify_list(c, aid);
    } else {
        out.say(msg_txt(1238)); // There are no items to appraise.
    }
    0
}

/// ACMD_FUNC(refineui).
fn refineui(c: &Ctx, out: &Out, aid: u32) -> i32 {
    if !feature_refineui() {
        out.say(msg_txt(774)); // This command is disabled via configuration.
        return -1;
    }
    if state(c, aid).refineui_open {
        out.say(msg_txt(775)); // You have already opened the refine UI.
        return -1;
    }
    super::refine::clif_refineui_open(c, aid);
    0
}

/// ACMD_FUNC(stylist).
fn stylist(c: &Ctx, out: &Out, aid: u32) -> i32 {
    if !feature_stylist() {
        out.say(msg_txt(774)); // This command is disabled via configuration.
        return -1;
    }
    if state(c, aid).stylist_open {
        out.say(msg_txt(799)); // You have already opened the stylist UI.
        return -1;
    }
    clif_ui_open(c, aid, OUT_UI_STYLIST, 0);
    0
}
