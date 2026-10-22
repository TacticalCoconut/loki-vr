//! Crafting: skill.cpp skill_can_produce_mix, skill_produce_mix, skill_arrow_create; clif.cpp
//! clif_skill_produce_mix_list, clif_cooking_list, clif_arrow_create_list, clif_msg_skill, clif_parse_ProduceMix,
//! clif_parse_Cooking, clif_parse_SelectArrow. The skills that open these lists (AM_PHARMACY, AC_MAKINGARROW,
//! GC_CREATENEWPOISON, GN_*, …) are phase 4; the `produce`, `cooking` and `makerune` script commands (item scripts
//! of hammers, cooking kits and rune stones) open them now.
//!
//! Skill state rAthena reads here that belongs to the skill system: sd->skill_id_old / skill_lv_old (skill/state.rs),
//! the homunculus's HVAN_INSTRUCT (homunculus/data.rs), and the skills
//! behind SelectArrow's other branches (SA_CREATECON produces through here; GC_POISONINGWEAPON / NC_MAGICDECOY are
//! skill effects).

use crate::clif::{self, Target};
use crate::clif_items as ci;
use crate::db::items::{self as itemdb, CARD0_CREATE, CARD0_FORGE, IT_WEAPON};
use crate::db::jobs::{JOBL_BABY};
use crate::db::skills::ids::*;
use crate::db::skills::{skill_changematerial_db, skill_produce_db, SkillProduce};
use crate::inventory::{self as inv, Item, CHKADDITEM_OVERAMOUNT};
use crate::net::packets::*;
use crate::net::wire::Reader;
use crate::skill::checkskill;
use crate::unit;
use crate::world::Ctx;

use super::*;

/// skill.hpp MAX_PRODUCE_RESOURCE, MAX_SKILL_CHANGEMATERIAL_SET.
const MAX_PRODUCE_RESOURCE: usize = 12;
const MAX_SKILL_CHANGEMATERIAL_SET: usize = 3;
/// clif.hpp e_clif_messages.
pub const MSI_SKILL_INVENTORY_KINDCNT_OVER: i32 = 1572;
pub const MSI_SKILL_SUCCESS: i32 = 1574;
pub const MSI_SKILL_FAIL: i32 = 1575;
/// clif.hpp e_notify_effect.
const NOTIFYEFFECT_REFINE_FAILURE: i32 = 2;
const NOTIFYEFFECT_REFINE_SUCCESS: i32 = 3;
const NOTIFYEFFECT_PHARMACY_SUCCESS: i32 = 5;
const NOTIFYEFFECT_PHARMACY_FAILURE: i32 = 6;
/// script_constants EF_COOKING_OK / EF_COOKING_FAIL.
const EF_COOKING_OK: i32 = 608;
const EF_COOKING_FAIL: i32 = 609;
/// map.hpp e_element.
const ELE_WATER: i32 = 1;
const ELE_EARTH: i32 = 2;
const ELE_FIRE: i32 = 3;
const ELE_WIND: i32 = 4;

/// itemdb.hpp item ids used here.
mod id {
    pub const RED_POTION: u32 = 501;
    pub const YELLOW_POTION: u32 = 503;
    pub const WHITE_POTION: u32 = 504;
    pub const RED_SLIM_POTION: u32 = 545;
    pub const YELLOW_SLIM_POTION: u32 = 546;
    pub const WHITE_SLIM_POTION: u32 = 547;
    pub const ALCOHOL: u32 = 970;
    pub const ANVIL: u32 = 986;
    pub const ORIDECON_ANVIL: u32 = 987;
    pub const GOLDEN_ANVIL: u32 = 988;
    pub const EMPERIUM_ANVIL: u32 = 989;
    pub const FLAME_HEART: u32 = 994;
    pub const GREAT_NATURE: u32 = 997;
    pub const IRON: u32 = 998;
    pub const STEEL: u32 = 999;
    pub const STAR_CRUMB: u32 = 1000;
    pub const FIRE_BOTTLE: u32 = 7135;
    pub const ACID_BOTTLE: u32 = 7136;
    pub const MAN_EATER_BOTTLE: u32 = 7137;
    pub const MINI_BOTTLE: u32 = 7138;
    pub const COATING_BOTTLE: u32 = 7139;
    pub const HOMUNCULUS_SUPPLEMENT: u32 = 100371;
    pub const NAUTHIZ: u32 = 12725;
    pub const RAIDO: u32 = 12726;
    pub const BERKANA: u32 = 12727;
    pub const ISA: u32 = 12728;
    pub const OTHILA: u32 = 12729;
    pub const URUZ: u32 = 12730;
    pub const THURISAZ: u32 = 12731;
    pub const WYRD: u32 = 12732;
    pub const HAGALAZ: u32 = 12733;
    pub const SEED_OF_HORNY_PLANT: u32 = 6210;
    pub const BLOODSUCK_PLANT_SEED: u32 = 6211;
    pub const BOMB_MUSHROOM_SPORE: u32 = 6212;
    pub const HP_INCREASE_POTION_SMALL: u32 = 12422;
    pub const HP_INCREASE_POTION_MEDIUM: u32 = 12423;
    pub const HP_INCREASE_POTION_LARGE: u32 = 12424;
    pub const SP_INCREASE_POTION_SMALL: u32 = 12425;
    pub const SP_INCREASE_POTION_MEDIUM: u32 = 12426;
    pub const SP_INCREASE_POTION_LARGE: u32 = 12427;
    pub const CONCENTRATED_WHITE_POTION_Z: u32 = 12428;
    pub const SAVAGE_FULL_ROAST: u32 = 12429;
    pub const COCKTAIL_WARG_BLOOD: u32 = 12430;
    pub const MINOR_STEW: u32 = 12431;
    pub const SIROMA_ICED_TEA: u32 = 12432;
    pub const DROSERA_HERB_SALAD: u32 = 12433;
    pub const PETITE_TAIL_NOODLES: u32 = 12434;
    pub const BLACK_MASS: u32 = 12435;
    pub const VITATA500: u32 = 12436;
    pub const CONCENTRATED_CEROMAIN_SOUP: u32 = 12437;
    pub const CURE_FREE: u32 = 12475;
    pub const APPLE_BOMB: u32 = 13260;
    pub const COCONUT_BOMB: u32 = 13261;
    pub const MELON_BOMB: u32 = 13262;
    pub const PINEAPPLE_BOMB: u32 = 13263;
    pub const BANANA_BOMB: u32 = 13264;
    pub const BLACK_LUMP: u32 = 13265;
    pub const BLACK_HARD_LUMP: u32 = 13266;
    pub const VERY_HARD_LUMP: u32 = 13267;
    pub const MYSTERIOUS_POWDER: u32 = 13268;
}

fn send_self(c: &Ctx, aid: u32, b: &[u8]) {
    if let Some(sd) = unit::get(c, aid) {
        clif::send(c, b, &sd, Target::SelfOnly);
    }
}

/// clif.cpp clif_msg_skill: ZC_MSG_SKILL.
pub fn clif_msg_skill(c: &Ctx, aid: u32, skill_id: u16, msg_id: i32) {
    let p = PACKET_ZC_MSG_SKILL { packetType: PACKET_ZC_MSG_SKILL::ID as i16, skillId: skill_id, msgId: msg_id };
    send_self(c, aid, &p.to_vec());
}

// ---------------------------------------------------------------------------------------------- cook mastery

/// pc.hpp COOKMASTERY_VAR: sd->cook_mastery lives in this character variable (loaded at login, written by
/// pc_setparam(SP_COOKMASTERY)).
const COOKMASTERY_VAR: &str = "cook_mastery";

/// sd->cook_mastery (int16).
pub fn cook_mastery(c: &Ctx, aid: u32) -> i32 {
    let Some(p) = crate::pc::get(c, aid) else { return 0 };
    let key = COOKMASTERY_VAR.to_string();
    let v = c.db().char_reg_num().by_key().filter((&p.char_id, &key, &0u32)).next().map(|r| r.value as i16 as i32).unwrap_or(0);
    v
}

/// pc.cpp pc_setparam(sd, SP_COOKMASTERY, val): capped to 0..1999 and saved in COOK_MASTERY.
pub fn set_cook_mastery(c: &Ctx, aid: u32, val: i32) {
    if cook_mastery(c, aid) == val {
        return;
    }
    let val = val.clamp(0, 1999);
    // pc_setglobalreg(sd, add_str(COOKMASTERY_VAR), sd->cook_mastery)
    let Some(p) = crate::pc::get(c, aid) else { return };
    let rt = crate::script::vm::Rt::new(c);
    let mut st = crate::script::vm::State::new(crate::script::vm::CodeKey::Func(String::new()), std::rc::Rc::new(Default::default()), 0, aid, 0);
    crate::script::reg::store_set_num(&rt, &mut st, &crate::script::reg::Store::Char(p.char_id), COOKMASTERY_VAR, 0, val as i64);
}

use crate::script::reg::char_reg_num as _;

// ---------------------------------------------------------------------------------------------- produce lists

/// skill.cpp skill_can_produce_mix: 0 when it cannot be made, else the produce_db index + 1.
pub fn skill_can_produce_mix(c: &Ctx, aid: u32, nameid: u32, trigger: i32, qty: i32) -> i16 {
    if itemdb::itemdb_exists(nameid).is_none() {
        return 0;
    }
    let db = skill_produce_db();
    let menuskill_id = menu(c, aid).0;
    let mut found: Option<usize> = None;
    for (i, e) in db.iter().enumerate() {
        if e.nameid == nameid {
            let j = e.req_skill as i32;
            if j > 0 && checkskill(c, aid, j as u16) < e.req_skill_lv {
                continue; // must iterate again to check other skills that produce it. [malufett]
            }
            if j > 0 && menuskill_id > 0 && menuskill_id != j {
                continue; // special case
            }
            found = Some(i);
            break;
        }
    }
    if nameid == id::HOMUNCULUS_SUPPLEMENT {
        // Temporary check since the produce_db specifically wants the Pharmacy skill to use
        if checkskill(c, aid, AM_BIOETHICS) == 0 {
            return 0;
        }
    }
    let Some(i) = found else { return 0 };
    let e = &db[i];
    // Cannot carry the produced stuff
    if inv::pc_checkadditem(c, aid, nameid, qty) == CHKADDITEM_OVERAMOUNT {
        return 0;
    }
    // Matching the requested produce list
    if trigger >= 0 {
        let itemlv = e.itemlv as i32;
        if trigger > 20 {
            // Non-weapon, non-food item (itemlv must match)
            if itemlv != trigger {
                return 0;
            }
        } else if trigger > 10 {
            // Food (any item level between 10 and 20 will do)
            if itemlv <= 10 || itemlv > 20 {
                return 0;
            }
        } else if itemlv > trigger {
            // Weapon (itemlv must be higher or equal)
            return 0;
        }
    }
    // Check on player's inventory
    let list = inv::inv_list(c, aid);
    for j in 0..MAX_PRODUCE_RESOURCE {
        let nameid_produce = e.mat_id[j];
        if nameid_produce == 0 {
            continue;
        }
        if e.mat_amount[j] == 0 {
            if inv::pc_search_inventory(c, aid, nameid_produce) < 0 {
                return 0;
            }
        } else {
            let mut amt: u16 = 0;
            for r in &list {
                if r.item.nameid == nameid_produce {
                    amt = amt.wrapping_add(r.item.amount as u16);
                }
            }
            if (amt as i32) < qty * e.mat_amount[j] as i32 {
                return 0;
            }
        }
    }
    i as i16 + 1
}

/// clif.cpp clif_skill_produce_mix_list: ZC_MAKABLEITEMLIST (always sent unless the same menu is open; menuskill =
/// skill_id / trigger when it lists something).
pub fn clif_skill_produce_mix_list(c: &Ctx, aid: u32, skill_id: i32, trigger: i32) {
    // Avoid resending the menu
    if menu(c, aid).0 == skill_id {
        return;
    }
    let skill_id = if skill_id == GC_CREATENEWPOISON as i32 { GC_RESEARCHNEWPOISON as i32 } else { skill_id };
    let mut p = PACKET_ZC_MAKABLEITEMLIST { packetType: PACKET_ZC_MAKABLEITEMLIST::ID as i16, ..Default::default() };
    for e in skill_produce_db() {
        if skill_can_produce_mix(c, aid, e.nameid, trigger, 1) == 0 {
            continue;
        }
        if skill_id > 0 && e.req_skill as i32 != skill_id {
            continue;
        }
        p.items.push(PACKET_ZC_MAKABLEITEMLIST_sub { itemId: itemdb::client_nameid(e.nameid), material: [0; 3] });
    }
    let count = p.items.len();
    send_self(c, aid, &p.to_vec());
    if count > 0 {
        update(c, aid, |s| {
            s.menuskill_id = skill_id;
            s.menuskill_val = trigger;
        });
    }
}

/// clif.cpp clif_cooking_list: ZC_MAKINGITEM_LIST (sent when it lists something or for AM_PHARMACY; menuskill =
/// skill_id / trigger / qty), otherwise the menu is cleared and ZC_MSG_SKILL(MSI_SKILL_INVENTORY_KINDCNT_OVER).
pub fn clif_cooking_list(c: &Ctx, aid: u32, trigger: i32, skill_id: u16, qty: i32, list_type: i32) {
    // Avoid resending the menu
    if menu(c, aid).0 == skill_id as i32 {
        return;
    }
    let mut p = PACKET_ZC_MAKINGITEM_LIST { packetType: PACKET_ZC_MAKINGITEM_LIST::ID as i16, makeItem: list_type as u16, ..Default::default() };
    for e in skill_produce_db() {
        if skill_can_produce_mix(c, aid, e.nameid, trigger, qty) == 0 {
            continue;
        }
        p.items.push(PACKET_ZC_MAKINGITEM_LIST_sub { itemId: itemdb::client_nameid(e.nameid) });
    }
    if !p.items.is_empty() || skill_id == AM_PHARMACY {
        send_self(c, aid, &p.to_vec());
        set_menu(c, aid, skill_id as i32, trigger, qty); // menuskill_val2 = amount
    } else {
        clif_menuskill_clear(c, aid);
        clif_msg_skill(c, aid, skill_id, MSI_SKILL_INVENTORY_KINDCNT_OVER);
    }
}

/// clif.cpp clif_arrow_create_list: ZC_MAKINGARROW_LIST of the arrow sources the player carries unworn and
/// identified (always sent; menuskill AC_MAKINGARROW when not empty).
pub fn clif_arrow_create_list(c: &Ctx, aid: u32) {
    let mut p = PACKET_ZC_MAKINGARROW_LIST { packetType: PACKET_ZC_MAKINGARROW_LIST::ID as i16, ..Default::default() };
    for a in &crate::db::skills::skill_misc().arrow {
        let nameid = a.nameid;
        if itemdb::itemdb_exists(nameid).is_none() {
            continue;
        }
        let index = inv::pc_search_inventory(c, aid, nameid);
        if index < 0 {
            continue;
        }
        let Some(r) = slot(c, aid, index as usize) else { continue };
        if r.item.equip != 0 || r.item.identify == 0 {
            continue;
        }
        p.items.push(PACKET_ZC_MAKINGARROW_LIST_sub { itemId: itemdb::client_nameid(nameid) });
    }
    let count = p.items.len();
    send_self(c, aid, &p.to_vec());
    if count > 0 {
        update(c, aid, |s| {
            s.menuskill_id = AC_MAKINGARROW as i32;
            s.menuskill_val = count as i32;
        });
    }
}

/// clif.cpp clif_elementalconverter_list (SA_CREATECON): ZC_MAKINGARROW_LIST of the skill_produce_db items
/// skill_can_produce_mix accepts with trigger 23 (the elemental converters) and qty 1, in skill_produce_db order
/// (always sent; menuskill SA_CREATECON / the count when not empty). rAthena walks all MAX_SKILL_PRODUCE_DB slots;
/// the empty ones have nameid 0, which skill_can_produce_mix rejects (itemdb_exists), so walking the loaded entries
/// is the same list.
pub fn clif_elementalconverter_list(c: &Ctx, aid: u32) {
    let mut p = PACKET_ZC_MAKINGARROW_LIST { packetType: PACKET_ZC_MAKINGARROW_LIST::ID as i16, ..Default::default() };
    for e in skill_produce_db() {
        if skill_can_produce_mix(c, aid, e.nameid, 23, 1) != 0 {
            p.items.push(PACKET_ZC_MAKINGARROW_LIST_sub { itemId: itemdb::client_nameid(e.nameid) });
        }
    }
    let count = p.items.len();
    send_self(c, aid, &p.to_vec());
    if count > 0 {
        update(c, aid, |s| {
            s.menuskill_id = SA_CREATECON as i32;
            s.menuskill_val = count as i32;
        });
    }
}

// ---------------------------------------------------------------------------------------------- parse

/// clif.cpp clif_parse_ProduceMix (CZ_REQMAKINGITEM 0x018e): the answer to ZC_MAKABLEITEMLIST.
pub fn clif_parse_produce_mix(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_REQMAKINGITEM::read(&mut Reader::new(pkt)) else { return };
    let (menuskill_id, menuskill_val, _) = menu(c, aid);
    match menuskill_id {
        -1 => {}
        x if x == AM_PHARMACY as i32 || x == RK_RUNEMASTERY as i32 || x == GC_RESEARCHNEWPOISON as i32 => {}
        _ => return,
    }
    if trading_refused(c, aid) {
        return;
    }
    let produce_idx = skill_can_produce_mix(c, aid, p.itemId, menuskill_val, 1);
    if produce_idx != 0 {
        skill_produce_mix(c, aid, 0, p.itemId, p.material[0] as i32, p.material[1] as i32, p.material[2] as i32, 1, produce_idx - 1);
    }
    clif_menuskill_clear(c, aid);
}

/// The pc_istrading refusal of the list answers: a skill failure and the menu cleared.
fn trading_refused(c: &Ctx, aid: u32) -> bool {
    let Some(pc) = crate::pc::get(c, aid) else { return true };
    if inv::pc_istrading(&pc) {
        // Make it fail to avoid shop exploits where you sell something different than you see.
        if let Some(sd) = unit::get(c, aid) {
            crate::skill::skill_fail(c, &sd, sd.ud.skill_id, 0);
        }
        clif_menuskill_clear(c, aid);
        return true;
    }
    false
}

/// clif.cpp clif_parse_Cooking (CZ_REQ_MAKINGITEM 0x025b): the answer to ZC_MAKINGITEM_LIST.
pub fn clif_parse_cooking(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_REQ_MAKINGITEM::read(&mut Reader::new(pkt)) else { return };
    let (menuskill_id, menuskill_val, menuskill_val2) = menu(c, aid);
    let amount = if menuskill_val2 != 0 { menuskill_val2 } else { 1 };
    if p.r#type == 6 && menuskill_id != GN_MIX_COOKING as i32 && menuskill_id != GN_S_PHARMACY as i32 {
        return;
    }
    if trading_refused(c, aid) {
        return;
    }
    let food_idx = skill_can_produce_mix(c, aid, p.itemId, menuskill_val, amount);
    if food_idx != 0 {
        let skill = if p.r#type > 1 { menuskill_id as u16 } else { 0 };
        skill_produce_mix(c, aid, skill, p.itemId, 0, 0, 0, amount, food_idx - 1);
    }
    clif_menuskill_clear(c, aid);
}

/// clif.cpp clif_parse_SelectArrow (CZ_REQ_MAKINGARROW 0x01ae): the answer to arrow crafting (and to the other
/// item lists that use this packet).
pub fn clif_parse_select_arrow(c: &Ctx, aid: u32, pkt: &[u8]) {
    if trading_refused(c, aid) {
        return;
    }
    let Some(p) = PACKET_CZ_REQ_MAKINGARROW::read(&mut Reader::new(pkt)) else { return };
    match menu(c, aid).0 {
        x if x == AC_MAKINGARROW as i32 => {
            skill_arrow_create(c, aid, p.itemId);
        }
        x if x == SA_CREATECON as i32 => {
            skill_produce_mix(c, aid, SA_CREATECON, p.itemId, 0, 0, 0, 1, -1);
        }
        x if x == GC_POISONINGWEAPON as i32 => {
            // skill_poisoningweapon(*sd, p->itemId) (thief agent)
            if let Some(u) = unit::get(c, aid) {
                crate::skills::thief::ext::skill_poisoningweapon(c, &u, p.itemId);
            }
        }
        x if x == NC_MAGICDECOY as i32 => {
            crate::skills::merchant::fawmagicdecoy::skill_magicdecoy(c, aid, p.itemId);
        }
        _ => {}
    }
    clif_menuskill_clear(c, aid);
}

// ---------------------------------------------------------------------------------------------- produce

/// skill.cpp skill_arrow_create: one source item becomes its arrows.
pub fn skill_arrow_create(c: &Ctx, aid: u32, nameid: u32) -> bool {
    let arrows = &crate::db::skills::skill_misc().arrow;
    if nameid == 0 || itemdb::itemdb_exists(nameid).is_none() || arrows.is_empty() {
        return false;
    }
    let arrow = arrows.iter().find(|a| a.nameid == nameid);
    let j = inv::pc_search_inventory(c, aid, nameid);
    let Some(arrow) = arrow else { return false };
    if j < 0 || arrow.created.is_empty() {
        return false;
    }
    inv::pc_delitem(c, aid, j as i32, 1, 0, 0, crate::map_log::LOG_TYPE_PRODUCE);
    let char_id = crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
    for &(made, amount) in &arrow.created {
        if made == 0 || itemdb::itemdb_exists(made).is_none() || amount == 0 {
            continue;
        }
        let mut tmp = Item::new(made);
        tmp.identify = 1;
        tmp.amount = amount as i16;
        if produce_item_name_input() & 0x4 != 0 {
            tmp.card0 = CARD0_CREATE;
            tmp.card1 = 0;
            tmp.card2 = char_id & 0xffff; // CharId
            tmp.card3 = char_id >> 16;
        }
        let flag = inv::pc_additem(c, aid, &mut tmp, amount as i32, crate::map_log::LOG_TYPE_PRODUCE);
        if flag != 0 {
            ci::additem(c, aid, 0, 0, flag);
            // skill_drop_items_full: no (map_addflooritem)
        }
    }
    true
}

/// skill.cpp skill_produce_mix: consumes the materials and makes the item (forged weapons carry the maker; potions,
/// runes, cooking… roll their own success rates). `produce_idx` -1 = look the entry up.
#[allow(clippy::too_many_arguments)]
pub fn skill_produce_mix(c: &Ctx, aid: u32, skill_id: u16, nameid: u32, slot1: i32, slot2: i32, slot3: i32, qty: i32, produce_idx: i16) -> bool {
    let Some(ch) = crate::pc::character_of(c, aid) else { return false };
    let Some(status) = crate::status::data::sd_get(c, aid).map(|s| s.battle_status) else { return false };
    // if( sd->skill_id_old == skill_id ) skill_lv = sd->skill_lv_old (the skill pipeline's fields, skill/state.rs)
    let mut skill_lv: i32 = 0;
    let st = crate::skill::state::get(c, aid);
    if st.skill_id_old == skill_id {
        skill_lv = st.skill_lv_old as i32;
    }
    let idx: usize = if produce_idx == -1 {
        let i = skill_can_produce_mix(c, aid, nameid, -1, qty);
        if i == 0 {
            return false;
        }
        (i - 1) as usize
    } else {
        produce_idx as usize
    };
    let db = skill_produce_db();
    let Some(entry): Option<&SkillProduce> = db.get(idx) else { return false };
    let mut qty = qty.max(1);
    let mut skill_id = if skill_id == 0 { entry.req_skill } else { skill_id }; // A skill can be specified for some override cases.
    if skill_id == GC_RESEARCHNEWPOISON {
        skill_id = GC_CREATENEWPOISON;
    }
    let slot = [slot1, slot2, slot3];
    let (mut sc, mut ele) = (0i32, 0i32);
    for &s in &slot {
        // Note that qty should always be one if you are using these!
        if s <= 0 {
            continue;
        }
        let j = inv::pc_search_inventory(c, aid, s as u32);
        if j < 0 {
            continue;
        }
        if s as u32 == id::STAR_CRUMB {
            inv::pc_delitem(c, aid, j as i32, 1, 1, 0, crate::map_log::LOG_TYPE_PRODUCE);
            sc += 1;
        }
        if s as u32 >= id::FLAME_HEART && s as u32 <= id::GREAT_NATURE && ele == 0 {
            const ELE_TABLE: [i32; 4] = [ELE_FIRE, ELE_WATER, ELE_WIND, ELE_EARTH];
            inv::pc_delitem(c, aid, j as i32, 1, 1, 0, crate::map_log::LOG_TYPE_PRODUCE);
            ele = ELE_TABLE[(s as u32 - id::FLAME_HEART) as usize];
        }
    }
    let mut num: i32 = -1; // exclude the recipe
    for i in 0..MAX_PRODUCE_RESOURCE {
        let mid = entry.mat_id[i];
        if itemdb::itemdb_exists(mid).is_none() {
            continue;
        }
        num += 1;
        let mut x: i32 = (if skill_id == RK_RUNEMASTERY { 1 } else { qty }) * entry.mat_amount[i] as i32;
        x = x as i16 as i32;
        loop {
            let j = inv::pc_search_inventory(c, aid, mid);
            let mut y;
            if j >= 0 {
                y = slot_amount(c, aid, j);
                if y > x {
                    y = x;
                }
                inv::pc_delitem(c, aid, j as i32, y, 0, 0, crate::map_log::LOG_TYPE_PRODUCE);
            } else {
                log::error!("skill_produce_mix: material item error");
                return false;
            }
            x = (x - y) as i16 as i32;
            if !(j >= 0 && x > 0) {
                break;
            }
        }
    }
    let equip = itemdb::itemdb_isequip(nameid) && skill_id != GN_CHANGEMATERIAL && skill_id != GN_MAKEBOMB;
    let wlv: i32 = if equip && itemdb::itemdb_type(nameid) == IT_WEAPON { itemdb::itemdb_wlv(nameid) as i32 } else { 0 };
    let job_level = ch.job_level as i32;
    let (dex, luk, int_) = (status.dex as i32, status.luk as i32, status.int_ as i32);
    let class = inv::class_mapid(&ch);
    let menu_now = menu(c, aid);
    let mut make_per: i32;
    if !equip {
        match skill_id {
            BS_IRON | BS_STEEL | BS_ENCHANTEDSTONE => {
                // Ores & Metals Refining - skill bonuses are straight from kRO website [DracoRPG]
                let i = checkskill(c, aid, skill_id) as i32;
                make_per = job_level * 20;
                make_per += dex * 10;
                make_per += luk * 10;
                make_per += c.rnd_value(1, 100) * 10;
                match nameid {
                    id::IRON => make_per += 4000 + i * 500,  // Temper Iron bonus: +45/+50/+55/+60/+65
                    id::STEEL => make_per += 3000 + i * 500, // Temper Steel bonus: +35/+40/+45/+50/+55
                    id::STAR_CRUMB => make_per = 100000,     // Star Crumbs are 100% success crafting rate? [Skotlex]
                    _ => make_per += 1000 + i * 500,         // Enchanted stone Craft bonus: +15/+20/+25/+30/+35
                }
            }
            ASC_CDP => make_per = 2000 + 40 * dex + 20 * luk,
            AL_HOLYWATER | AB_ANCILLA => make_per = 100000, // 100% success
            AM_PHARMACY | AM_TWILIGHT1 | AM_TWILIGHT2 | AM_TWILIGHT3 => {
                // Potion Preparation - reviewed with the help of various Ragnainfo sources [DracoRPG]
                make_per = checkskill(c, aid, AM_LEARNINGPOTION) as i32 * 50
                    + checkskill(c, aid, AM_PHARMACY) as i32 * 300
                    + job_level * 20
                    + (int_ / 2) * 10
                    + dex * 10
                    + luk * 10;
                let hd = crate::homunculus::data::of_master(c, aid);
                if crate::homunculus::data::hom_is_active(hd.as_ref()) {
                    // Player got a homun (skill.cpp:12969-12973)
                    let skill = crate::homunculus::hom_checkskill(hd.as_ref(), crate::db::skills::HVAN_INSTRUCT) as i32;
                    if skill > 0 {
                        // His homun is a vanil with instruction change
                        make_per += skill * 100; // +1% bonus per level
                    }
                }
                match nameid {
                    id::RED_POTION | id::YELLOW_POTION | id::WHITE_POTION => make_per += (1 + (c.rnd() % 100) as i32) * 10 + 2000,
                    id::ALCOHOL => make_per += (1 + (c.rnd() % 100) as i32) * 10 + 1000,
                    id::FIRE_BOTTLE | id::ACID_BOTTLE | id::MAN_EATER_BOTTLE | id::MINI_BOTTLE => make_per += (1 + (c.rnd() % 100) as i32) * 10,
                    id::YELLOW_SLIM_POTION => make_per -= (1 + (c.rnd() % 50) as i32) * 10,
                    id::WHITE_SLIM_POTION | id::COATING_BOTTLE => make_per -= (1 + (c.rnd() % 100) as i32) * 10,
                    // Common items, receive no bonus or penalty, listed just because they are commonly produced
                    _ => {}
                }
                // (int32 arithmetic: a large rate wraps as the C does)
                if pp_rate() != 100 {
                    make_per = make_per.wrapping_mul(pp_rate()) / 100;
                }
                // Baby classes receive a 30% penalty for these skills
                if class & JOBL_BABY != 0 {
                    make_per = (make_per * 70) / 100;
                }
            }
            SA_CREATECON => make_per = 100000, // Elemental Converter Creation: should be 100% success rate
            RK_RUNEMASTERY => {
                let lv = checkskill(c, aid, skill_id) as i32;
                let a = 100 * (30 + 2 * lv);
                let b = 100 * dex / 30 + 10 * (luk + job_level);
                let itemid = inv::st(c, aid).map(|s| s.itemid as i32).unwrap_or(0);
                let cc = 100 * itemid.clamp(0, 100); // itemid depend on makerune()
                let d = match nameid {
                    id::BERKANA => -2000,                                       // Rank S
                    id::NAUTHIZ | id::URUZ => -1500,                            // Rank A
                    id::ISA | id::WYRD => -1000,                                // Rank B
                    id::RAIDO | id::THURISAZ | id::HAGALAZ | id::OTHILA => -500, // Rank C
                    _ => -1500,                                                 // not specified =-15%
                };
                make_per = a + b + cc + d;
                let runemastery_skill_lv = lv as u8;
                if runemastery_skill_lv > 9 {
                    qty = 2 + (c.rnd() % 5) as i32; // 2~6
                } else if runemastery_skill_lv > 4 {
                    qty = 2 + (c.rnd() % 3) as i32; // 2~4
                } else {
                    qty = 2;
                }
            }
            GC_CREATENEWPOISON => {
                let lv = checkskill(c, aid, GC_RESEARCHNEWPOISON) as i32;
                make_per = 3000 + 500 * lv;
                // 1 + rnd() % lv: a character without the skill divides by zero in rAthena (unreachable: the list
                // needs the skill)
                qty = 1 + if lv > 0 { (c.rnd() % lv as u32) as i32 } else { 0 };
            }
            GN_CHANGEMATERIAL => {
                make_per = 0;
                if let Some(cm) = skill_changematerial_db().iter().find(|e| e.nameid == nameid) {
                    make_per = cm.rate as i32 * 10;
                }
            }
            GN_S_PHARMACY => {
                let mut difficulty = 620 - 20 * skill_lv; // (620 - 20 * Skill Level)
                const PRODUCTION_COUNT: [i32; 10] = [7, 8, 8, 9, 9, 10, 10, 11, 11, 12];
                match nameid {
                    id::HP_INCREASE_POTION_SMALL | id::SP_INCREASE_POTION_SMALL | id::CONCENTRATED_WHITE_POTION_Z => difficulty += 10,
                    id::BOMB_MUSHROOM_SPORE | id::SP_INCREASE_POTION_MEDIUM => difficulty += 15,
                    id::HP_INCREASE_POTION_MEDIUM | id::SP_INCREASE_POTION_LARGE | id::VITATA500 => difficulty += 20,
                    id::SEED_OF_HORNY_PLANT | id::BLOODSUCK_PLANT_SEED | id::CONCENTRATED_CEROMAIN_SOUP => difficulty += 30,
                    id::HP_INCREASE_POTION_LARGE | id::CURE_FREE => difficulty += 40,
                    _ => {}
                }
                make_per = int_ + dex / 2 + luk + job_level + (30 + (c.rnd() % 120) as i32 + 1)
                    + ch.base_level as i32
                    + 5 * (checkskill(c, aid, AM_LEARNINGPOTION) as i32 - 20)
                    + checkskill(c, aid, CR_FULLPROTECTION) as i32 * (6 + (c.rnd() % 4) as i32 + 1);
                make_per -= difficulty;
                // production_count[skill_lv - 1]: only the skill (phase 4) opens this list with a level
                qty = PRODUCTION_COUNT.get((skill_lv - 1).max(0) as usize).copied().unwrap_or(0);
                // Determine quantity from difficulty
                if make_per < 1 {
                    qty -= 6;
                } else if make_per < 100 {
                    qty -= 5;
                } else if make_per < 300 {
                    qty -= 4;
                } else if make_per < 400 {
                    qty -= 3;
                }
                make_per = 100000; // Adjust success back to 100% for crafting
            }
            GN_MAKEBOMB | GN_MIX_COOKING => {
                let mut difficulty = 30 + (c.rnd() % 120) as i32 + 1; // Random number between (30 ~ 150)
                match nameid {
                    id::APPLE_BOMB => difficulty += 5,
                    id::COCONUT_BOMB | id::MELON_BOMB => difficulty += 10,
                    id::PINEAPPLE_BOMB => difficulty += 15,
                    id::BANANA_BOMB => difficulty += 20,
                    id::SAVAGE_FULL_ROAST | id::COCKTAIL_WARG_BLOOD | id::MINOR_STEW | id::SIROMA_ICED_TEA | id::DROSERA_HERB_SALAD | id::PETITE_TAIL_NOODLES => difficulty += 15,
                    _ => {}
                }
                make_per = job_level / 4 + luk / 2 + dex / 3; // (Caster's Job Level / 4) + (Caster's LUK / 2) + (Caster's DEX / 3)
                if skill_lv > 1 {
                    make_per -= difficulty;
                    // Determine quantity from difficulty
                    qty = if make_per >= 30 {
                        10 + (c.rnd() % 2) as i32
                    } else if make_per >= 10 {
                        10
                    } else if make_per >= -10 {
                        8
                    } else if make_per >= -30 {
                        5
                    } else {
                        0
                    };
                } else if make_per < difficulty {
                    qty = 0;
                }
                make_per = 100000; // Adjust success back to 100% for crafting
            }
            MT_M_MACHINE | BO_BIONIC_PHARMACY => {
                // Difficulty formula unknown. Making it 100% success for now. [Rytech]
                qty = if skill_id == MT_M_MACHINE { 7 + skill_lv } else { 10 + skill_lv };
                make_per = 100000;
            }
            _ => {
                if menu_now.0 == AM_PHARMACY as i32 && menu_now.1 > 10 && menu_now.1 <= 20 {
                    // Assume Cooking Dish
                    if menu_now.1 >= 15 {
                        make_per = 10000; // Legendary Cooking Set: 100% Success
                    } else {
                        let cm = cook_mastery(c, aid);
                        let m = 30 + 5 * (cm / 400) - (6 + cm / 80);
                        // rnd() % m: a modulus of 0 would divide by zero in rAthena (cook mastery 1999 keeps it > 0)
                        let r = if m != 0 { (c.rnd() as i32) % m } else { 0 };
                        make_per = 1200 * (menu_now.1 - 10)
                            + 20 * (ch.base_level as i32 + 1)
                            + 20 * (dex + 1)
                            + 100 * (r + (6 + cm / 80))
                            - 400 * (entry.itemlv as i32 - 11 + 1)
                            - 10 * (100 - luk + 1)
                            - 500 * (num - 1)
                            - 100 * ((c.rnd() % 4) as i32 + 1);
                    }
                } else {
                    make_per = 5000;
                }
            }
        }
    } else {
        // Weapon Forging
        make_per = job_level * 20;
        make_per += dex * 10;
        make_per += luk * 10;
        make_per += c.rnd_value(1, 100) * 10;
        // Weapon level base chance
        if wlv > 0 && wlv < 4 {
            make_per += (4 / wlv) * 1000; // +40/+20/+10
        }
        make_per += checkskill(c, aid, skill_id) as i32 * 500; // Smithing skills bonus: +5/+10/+15
        // Weaponry Research bonus: +1/+2/+3/+4/+5/+6/+7/+8/+9/+10
        make_per += checkskill(c, aid, BS_WEAPONRESEARCH) as i32 * 100;
        // Oridecon Research bonus (custom): +1/+2/+3/+4/+5
        if oridecon_research_fix() == 1 && wlv >= 3 {
            make_per += checkskill(c, aid, BS_ORIDEOCON) as i32 * 100;
        }
        // Element Stone: -25%
        if ele != 0 {
            make_per -= 2500;
        }
        // Star Crumb: -15% each
        make_per -= sc * 1500;
        if inv::pc_search_inventory(c, aid, id::EMPERIUM_ANVIL) > -1 {
            make_per += 1000; // Emperium Anvil: +10
        } else if inv::pc_search_inventory(c, aid, id::GOLDEN_ANVIL) > -1 {
            make_per += 500; // Golden Anvil: +5
        } else if inv::pc_search_inventory(c, aid, id::ORIDECON_ANVIL) > -1 {
            make_per += 250; // Oridecon Anvil: +2.5
        } else if inv::pc_search_inventory(c, aid, id::ANVIL) > -1 {
            make_per += 0; // Anvil: +0
        }
        if wp_rate() != 100 {
            make_per = make_per.wrapping_mul(wp_rate()) / 100;
        }
        // Baby classes receive a 30% penalty when the target item is a weapon
        if class & JOBL_BABY != 0 {
            make_per = (make_per * 70) / 100;
        }
    }
    if make_per < 1 {
        make_per = 1;
    }
    let char_id = ch.id;
    // Success, or crafting multiple items: `qty > 1 || rnd()%10000 < make_per` (rnd() is only drawn for qty <= 1)
    let success = qty > 1 || ((c.rnd() % 10000) as i32) < make_per;
    if success {
        let mut tmp = Item::new(nameid);
        tmp.amount = 1;
        tmp.identify = 1;
        if equip {
            tmp.card0 = CARD0_FORGE;
            tmp.card1 = (((sc * 5) << 8) + ele) as u32;
            tmp.card2 = char_id & 0xffff; // CharId
            tmp.card3 = char_id >> 16;
        } else {
            // Flag is only used on the end, so it can be used here. [Skotlex]
            let flag = match skill_id {
                BS_DAGGER | BS_SWORD | BS_TWOHANDSWORD | BS_AXE | BS_MACE | BS_KNUCKLE | BS_SPEAR => produce_item_name_input() & 0x1,
                AM_PHARMACY | AM_TWILIGHT1 | AM_TWILIGHT2 | AM_TWILIGHT3 => produce_item_name_input() & 0x2,
                AL_HOLYWATER | AB_ANCILLA => produce_item_name_input() & 0x8,
                ASC_CDP => produce_item_name_input() & 0x10,
                _ => produce_item_name_input() & 0x80,
            };
            if flag != 0 {
                tmp.card0 = CARD0_CREATE;
                tmp.card1 = 0;
                tmp.card2 = char_id & 0xffff; // CharId
                tmp.card3 = char_id >> 16;
            }
        }
        // log_produce (TODO in rAthena)
        if equip {
            ci::produceeffect(c, aid, 0, nameid);
            misceffect(c, aid, NOTIFYEFFECT_REFINE_SUCCESS);
            if wlv >= 3 && ((ele != 0) as i32 + sc) >= 3 {
                // Fame point system [DracoRPG]: forge a lv3 weapon with 3 additional ingredients
                pc_addfame_stub(c, aid, fame_forge());
            }
        } else {
            let mut fame = 0;
            tmp.amount = 0;
            for _ in 0..qty {
                // Apply quantity modifiers.
                if matches!(skill_id, GN_MIX_COOKING | GN_MAKEBOMB | GN_S_PHARMACY | MT_M_MACHINE | BO_BIONIC_PHARMACY) && make_per > 1 {
                    tmp.amount = qty as i16;
                    break;
                }
                if qty == 1 || ((c.rnd() % 10000) as i32) < make_per {
                    // Success
                    tmp.amount += 1;
                    if nameid < id::RED_SLIM_POTION || nameid > id::WHITE_SLIM_POTION {
                        continue;
                    }
                    if !matches!(skill_id, AM_PHARMACY | AM_TWILIGHT1 | AM_TWILIGHT2 | AM_TWILIGHT3) {
                        continue;
                    }
                    // Add fame as needed.
                    let mut s = state(c, aid);
                    s.potion_success_counter += 1;
                    match s.potion_success_counter {
                        3 => fame += fame_pharmacy_3(), // Success to prepare 3 Condensed Potions in a row
                        5 => fame += fame_pharmacy_5(),
                        7 => fame += fame_pharmacy_7(),
                        10 => {
                            fame += fame_pharmacy_10();
                            s.potion_success_counter = 0;
                        }
                        _ => {}
                    }
                    put_state(c, &s);
                } else {
                    // Failure
                    update(c, aid, |s| s.potion_success_counter = 0);
                }
            }
            if fame != 0 {
                pc_addfame_stub(c, aid, fame);
            }
            // Visual effects and the like.
            match skill_id {
                AM_PHARMACY | AM_TWILIGHT1 | AM_TWILIGHT2 | AM_TWILIGHT3 | ASC_CDP | GC_CREATENEWPOISON => {
                    ci::produceeffect(c, aid, 2, nameid);
                    misceffect(c, aid, NOTIFYEFFECT_PHARMACY_SUCCESS);
                }
                BS_IRON | BS_STEEL | BS_ENCHANTEDSTONE => {
                    ci::produceeffect(c, aid, 0, nameid);
                    misceffect(c, aid, NOTIFYEFFECT_REFINE_SUCCESS);
                }
                _ => {
                    // Those that don't require a skill?
                    if entry.itemlv > 10 && entry.itemlv <= 20 {
                        // Cooking items.
                        if let Some(sd) = unit::get(c, aid) {
                            clif::specialeffect(c, &sd, EF_COOKING_OK, Target::Area);
                        }
                        let cm = cook_mastery(c, aid);
                        set_cook_mastery(c, aid, cm + (1 << ((entry.itemlv as i32 - 11) / 2)) * 5);
                    }
                }
            }
        }
        if skill_id == GN_CHANGEMATERIAL && tmp.amount != 0 {
            // Success
            let mut k = 0;
            let is_stackable = itemdb::itemdb_isstackable(tmp.nameid);
            if let Some(cm) = skill_changematerial_db().iter().find(|e| e.nameid == nameid) {
                for j in 0..MAX_SKILL_CHANGEMATERIAL_SET {
                    if ((c.rnd() % 1000) as i32) < cm.qty_rate[j] as i32 {
                        let total_qty = (qty * cm.qty[j] as i32) as u16;
                        tmp.amount = if is_stackable { total_qty as i16 } else { 1 };
                        let mut l: i32 = 0;
                        while l < total_qty as i32 {
                            let amount = tmp.amount as i32;
                            let f = inv::pc_additem(c, aid, &mut tmp, amount, crate::map_log::LOG_TYPE_PRODUCE);
                            if f != 0 {
                                ci::additem(c, aid, 0, 0, f);
                                // skill_drop_items_full: no (map_addflooritem)
                            }
                            if amount <= 0 {
                                break; // rAthena loops forever on an amount of 0 (total_qty > 0 with tmp.amount 0 cannot happen)
                            }
                            l += amount;
                        }
                        k += 1;
                    }
                }
            }
            if k != 0 {
                ci::produceeffect(c, aid, 6, nameid);
                misceffect(c, aid, NOTIFYEFFECT_PHARMACY_SUCCESS);
                clif_msg_skill(c, aid, skill_id, MSI_SKILL_SUCCESS);
                return true;
            }
        } else if tmp.amount != 0 {
            // Success
            let amount = tmp.amount as i32;
            let f = inv::pc_additem(c, aid, &mut tmp, amount, crate::map_log::LOG_TYPE_PRODUCE);
            if f != 0 {
                ci::additem(c, aid, 0, 0, f);
                // skill_drop_items_full: no (map_addflooritem)
            }
            match skill_id {
                RK_RUNEMASTERY => {
                    ci::produceeffect(c, aid, 4, nameid);
                    misceffect(c, aid, NOTIFYEFFECT_PHARMACY_SUCCESS);
                }
                GN_MIX_COOKING | GN_MAKEBOMB | GN_S_PHARMACY => {
                    ci::produceeffect(c, aid, 6, nameid);
                    misceffect(c, aid, NOTIFYEFFECT_PHARMACY_SUCCESS);
                    clif_msg_skill(c, aid, skill_id, MSI_SKILL_SUCCESS);
                }
                MT_M_MACHINE => {
                    ci::produceeffect(c, aid, 0, nameid);
                    misceffect(c, aid, NOTIFYEFFECT_REFINE_SUCCESS);
                    clif_msg_skill(c, aid, skill_id, MSI_SKILL_SUCCESS);
                }
                BO_BIONIC_PHARMACY => {
                    ci::produceeffect(c, aid, 2, nameid);
                    misceffect(c, aid, NOTIFYEFFECT_PHARMACY_SUCCESS);
                    clif_msg_skill(c, aid, skill_id, MSI_SKILL_SUCCESS);
                }
                _ => {}
            }
            return true;
        }
    }
    // Failure (log_produce: TODO in rAthena)
    if equip {
        ci::produceeffect(c, aid, 1, nameid);
        misceffect(c, aid, NOTIFYEFFECT_REFINE_FAILURE);
    } else {
        match skill_id {
            ASC_CDP | AM_PHARMACY | AM_TWILIGHT1 | AM_TWILIGHT2 | AM_TWILIGHT3 | GC_CREATENEWPOISON => {
                if skill_id == ASC_CDP {
                    //25% Damage yourself, and display same effect as failed potion. (thief agent)
                    crate::status::hp::status_percent_damage(c, None, aid, -25, 0, 0, true);
                }
                ci::produceeffect(c, aid, 3, nameid);
                misceffect(c, aid, NOTIFYEFFECT_PHARMACY_FAILURE);
                update(c, aid, |s| s.potion_success_counter = 0); // Fame point system [DracoRPG]
            }
            BS_IRON | BS_STEEL | BS_ENCHANTEDSTONE => {
                ci::produceeffect(c, aid, 1, nameid);
                misceffect(c, aid, NOTIFYEFFECT_REFINE_FAILURE);
            }
            RK_RUNEMASTERY => {
                ci::produceeffect(c, aid, 5, nameid);
                misceffect(c, aid, NOTIFYEFFECT_PHARMACY_FAILURE);
            }
            GN_MIX_COOKING => {
                if qty == 0 {
                    const COMPENSATION: [u32; 5] = [id::BLACK_LUMP, id::BLACK_HARD_LUMP, id::VERY_HARD_LUMP, id::BLACK_MASS, id::MYSTERIOUS_POWDER];
                    let rate = (c.rnd() % 1000) as i32 + 1;
                    let i = if rate < 500 {
                        0
                    } else if rate < 750 {
                        1
                    } else if rate < 850 {
                        2
                    } else if rate < 950 {
                        3
                    } else {
                        4
                    };
                    let mut tmp = Item::new(COMPENSATION[i]);
                    // (skill_lv == 1) ? 1 : 5 + rand() % 5 — libc rand() in rAthena, the reducer RNG here
                    tmp.amount = if skill_lv == 1 { 1 } else { 5 + (c.rnd() % 5) as i16 };
                    tmp.identify = 1;
                    let amount = tmp.amount as i32;
                    let f = inv::pc_additem(c, aid, &mut tmp, amount, crate::map_log::LOG_TYPE_PRODUCE);
                    if f != 0 {
                        ci::additem(c, aid, 0, 0, f);
                    }
                    ci::produceeffect(c, aid, 7, nameid);
                    misceffect(c, aid, NOTIFYEFFECT_PHARMACY_FAILURE);
                    clif_msg_skill(c, aid, skill_id, MSI_SKILL_FAIL);
                }
            }
            GN_MAKEBOMB | GN_S_PHARMACY | GN_CHANGEMATERIAL => {
                ci::produceeffect(c, aid, 7, nameid);
                misceffect(c, aid, NOTIFYEFFECT_PHARMACY_FAILURE);
                clif_msg_skill(c, aid, skill_id, MSI_SKILL_FAIL);
            }
            MT_M_MACHINE => {
                ci::produceeffect(c, aid, 1, nameid);
                misceffect(c, aid, NOTIFYEFFECT_REFINE_FAILURE);
                clif_msg_skill(c, aid, skill_id, MSI_SKILL_FAIL);
            }
            BO_BIONIC_PHARMACY => {
                ci::produceeffect(c, aid, 3, nameid);
                misceffect(c, aid, NOTIFYEFFECT_PHARMACY_FAILURE);
                clif_msg_skill(c, aid, skill_id, MSI_SKILL_FAIL);
            }
            _ => {
                if entry.itemlv > 10 && entry.itemlv <= 20 {
                    // Cooking items.
                    if let Some(sd) = unit::get(c, aid) {
                        clif::specialeffect(c, &sd, EF_COOKING_FAIL, Target::Area);
                    }
                    // todo: What in the world is this calculation
                    let s = 1 << ((entry.itemlv as i32 - 11) / 2);
                    let cm = cook_mastery(c, aid);
                    set_cook_mastery(c, aid, cm - s - ((s >> 1) * 3));
                }
            }
        }
    }
    false
}

/// The amount in inventory slot `j`.
fn slot_amount(c: &Ctx, aid: u32, j: i16) -> i32 {
    slot(c, aid, j as usize).map(|r| r.item.amount as i32).unwrap_or(0)
}

// ---------------------------------------------------------------------------------------------- change material

/// clif.hpp MSI_SKILL_RECIPE_NOTEXIST / MSI_SKILL_FAIL_MATERIAL_IDENTITY.
pub const MSI_SKILL_RECIPE_NOTEXIST: i32 = 1570;
pub const MSI_SKILL_FAIL_MATERIAL_IDENTITY: i32 = 1581;

// clif.cpp clif_skill_itemlistwindow and clif_parse_ItemListWindowSelected (0x07e3 / 0x07e4) are skill/clif.rs's and
// skill/parse.rs's (mage and merchant agents, wave E); the parser's type-0 arm calls skill_changematerial below.

/// skill.cpp skill_changematerial: finds the first produce recipe of item level 26 whose materials the submitted
/// list matches exactly (every listed stack an exact multiple p of the recipe's amount) and makes p of it with
/// skill_produce_mix; an unidentified item or no recipe answers with ZC_MSG_SKILL.
pub fn skill_changematerial(c: &Ctx, aid: u32, n: i32, item_list: &[u16]) -> i32 {
    use crate::db::skills::MAX_PRODUCE_RESOURCE;
    let inv_a = inv::inv_array(c, aid);
    let mut p: i32 = 0;
    // Search for objects that can be created.
    for (i, e) in skill_produce_db().iter().enumerate() {
        if e.itemlv == 26 && e.nameid > 0 {
            p = 0;
            loop {
                let mut cnt = 0;
                let mut j = 0usize;
                // Verification of overlap between the objects required and the list submitted.
                while j < MAX_PRODUCE_RESOURCE {
                    if e.mat_id[j] > 0 {
                        for k in 0..n.max(0) as usize {
                            let idx = item_list.get(k * 2).copied().unwrap_or(0) as i32 - 2;
                            if idx < 0 || idx >= inv::MAX_INVENTORY as i32 {
                                return 0;
                            }
                            let slot = inv_a.get(idx as usize).cloned().flatten();
                            let nameid = slot.as_ref().map(|s| s.item.nameid).unwrap_or(0);
                            let amount = item_list.get(k * 2 + 1).copied().unwrap_or(0) as i32;
                            if nameid > 0 && slot.as_ref().map(|s| s.item.identify == 0).unwrap_or(false) {
                                clif_msg_skill(c, aid, GN_CHANGEMATERIAL, MSI_SKILL_FAIL_MATERIAL_IDENTITY);
                                return 0;
                            }
                            let ma = e.mat_amount[j] as i32;
                            // must be in exact amount (a zero amount would divide by zero in rAthena: never matches here)
                            if nameid == e.mat_id[j] && ma != 0 && (amount - p * ma) >= ma && (amount - p * ma) % ma == 0 {
                                cnt += 1; // match
                            }
                        }
                    } else {
                        break; // No more items required
                    }
                    j += 1;
                }
                p += 1;
                if !(n as usize == j && cnt == n) {
                    break;
                }
            }
            p -= 1;
            if p > 0 {
                skill_produce_mix(c, aid, GN_CHANGEMATERIAL, e.nameid, 0, 0, 0, p, i as i16);
                return 1;
            }
        }
    }
    if p == 0 {
        clif_msg_skill(c, aid, GN_CHANGEMATERIAL, MSI_SKILL_RECIPE_NOTEXIST);
    }
    0
}
