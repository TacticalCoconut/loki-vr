//! Script commands of the item windows (script.cpp BUILDIN_FUNCs): produce, cooking, makerune, mergeitem,
//! mergeitem2, opendressroom, openstylist, getequiprandomoption, setrandomoption, randomoptgroup, refineui,
//! identifyall, laphine_synthesis, laphine_upgrade, enchantgradeui, item_reform, item_enchant, searchitem,
//! setiteminfo, setitemscript.
//! Chained into script/buildins/mod.rs.

use crate::db::items::{self as itemdb, laphine as laphine_db, randomopt, reform as reform_db};
use crate::db::skills::ids::{AM_PHARMACY, RK_RUNEMASTERY};
use crate::inventory::{self as inv, ItemOption, EQI_MAX, EQUIP_BITMASK, MAX_INVENTORY, MAX_ITEM_RDM_OPT};
use crate::script::buildins::{charid2sd, lat, BuildinFn};
use crate::script::reg;
use crate::script::vm::*;
use crate::world::Ctx;

use super::*;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "produce" => produce,
        "cooking" => cooking,
        "makerune" => makerune,
        "mergeitem" => mergeitem,
        "mergeitem2" => mergeitem2,
        "opendressroom" => opendressroom,
        "openstylist" => openstylist,
        "getequiprandomoption" => getequiprandomoption,
        "setrandomoption" => setrandomoption,
        "randomoptgroup" => randomoptgroup,
        "refineui" => refineui,
        "identifyall" => identifyall,
        "laphine_synthesis" => laphine_synthesis,
        "laphine_upgrade" => laphine_upgrade,
        "enchantgradeui" => enchantgradeui,
        "item_reform" => item_reform,
        "item_enchant" => item_enchant,
        "searchitem" => searchitem,
        "setiteminfo" => setiteminfo,
        "setitemscript" => setitemscript,
        _ => return None,
    })
}

/// script.cpp ROA_ID / ROA_VALUE / ROA_PARAM.
const ROA_ID: i32 = 0;
const ROA_VALUE: i32 = 1;
const ROA_PARAM: i32 = 2;
/// itemdb.hpp MAX_SEARCH.
const MAX_SEARCH: usize = 10;

/// setd_sub_num(st, nullptr, name, idx, v, nullptr) for a `.@` scope variable (or any variable by name).
fn set_var(rt: &Rt, st: &mut State, sd: Option<u32>, name: &str, idx: u32, v: i64) {
    let n = crate::net::wire::latin1(name);
    let id = add_str(&n);
    reg::set_reg_num(rt, st, sd, id, idx, &n, v, &RegRef::None);
}

/// produce <item level>: the produce list for this trigger (clif_skill_produce_mix_list with skill -1).
fn produce(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = rid2sd(rt, st, "buildin_produce") else { return SCRIPT_CMD_SUCCESS };
    let trigger = getnum(rt, st, 2);
    super::produce::clif_skill_produce_mix_list(c, aid, -1, trigger);
    SCRIPT_CMD_SUCCESS
}

/// cooking <dish level>: the cooking list (clif_cooking_list(sd, trigger, AM_PHARMACY, 1, 1)).
fn cooking(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = rid2sd(rt, st, "buildin_cooking") else { return SCRIPT_CMD_SUCCESS };
    let trigger = getnum(rt, st, 2);
    super::produce::clif_cooking_list(c, aid, trigger, AM_PHARMACY, 1, 1);
    SCRIPT_CMD_SUCCESS
}

/// makerune <% success bonus>{,<char_id>}: the rune list (RK_RUNEMASTERY, trigger 24); sd->itemid holds the bonus.
fn makerune(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 3, "buildin_makerune") else { return SCRIPT_CMD_FAILURE };
    super::produce::clif_skill_produce_mix_list(c, aid, RK_RUNEMASTERY as i32, 24);
    let bonus = getnum(rt, st, 2);
    if let Some(mut s) = inv::st(c, aid) {
        s.itemid = bonus as u32;
        inv::put_st(c, &s);
    }
    SCRIPT_CMD_SUCCESS
}

/// mergeitem {<char_id>}: the merge window.
fn mergeitem(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 2, "buildin_mergeitem") else { return SCRIPT_CMD_FAILURE };
    super::merge::clif_merge_item_open(c, aid);
    SCRIPT_CMD_SUCCESS
}

/// mergeitem2 {<item_id>,{<char_id>}}: every stackable unique-id item (or only `item_id`) is taken out and added back
/// merged by (nameid, bound, cards); returns the number of stacks.
fn mergeitem2(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 3, "buildin_mergeitem2") else { return SCRIPT_CMD_FAILURE };
    let mut nameid = 0u32;
    let mut count: u16 = 0;
    if st.hasdata(2) {
        if isstring(rt, st, 2) {
            let name = lat(&getstr(rt, st, 2));
            match itemdb::itemdb_searchname(&name) {
                Some(id) => nameid = id.nameid,
                None => {
                    log::error!("buildin_mergeitem2: Nonexistant item {name} requested.");
                    st.pushint(count as i64);
                    return SCRIPT_CMD_FAILURE;
                }
            }
        } else {
            nameid = getnum(rt, st, 2) as u32;
            if itemdb::itemdb_exists(nameid).is_none() {
                log::error!("buildin_mergeitem: Nonexistant item {nameid} requested.");
                st.pushint(count as i64);
                return SCRIPT_CMD_FAILURE;
            }
        }
    }
    let mut items: Vec<inv::Item> = Vec::new();
    for i in 0..MAX_INVENTORY {
        let Some(r) = inv::inv_get(c, aid, i) else { continue };
        let it = r.item;
        if it.unique_id == 0 || it.expire_time != 0 || !itemdb::itemdb_isstackable(it.nameid) {
            continue;
        }
        if nameid == 0 || nameid == it.nameid {
            let amount = it.amount as i32;
            match items.iter_mut().find(|m| m.nameid == it.nameid && m.bound == it.bound && m.cards() == it.cards()) {
                Some(m) if count > 0 => {
                    // Find Match
                    m.amount = m.amount.wrapping_add(it.amount);
                }
                _ => {
                    // New entry
                    items.push(it.clone());
                    count += 1;
                }
            }
            inv::pc_delitem(c, aid, i as i32, amount, 0, 0, crate::map_log::LOG_TYPE_NPC);
        }
    }
    if items.is_empty() {
        return SCRIPT_CMD_SUCCESS; // Nothing todo here (no value is pushed, as in rAthena)
    }
    // Retrieve the items
    for (i, it) in items.iter_mut().enumerate() {
        it.unique_id = 0;
        let amount = it.amount as i32;
        let flag = inv::pc_additem(c, aid, it, amount, crate::map_log::LOG_TYPE_NPC);
        if flag != 0 {
            crate::clif_items::additem(c, aid, i as i32, amount, flag);
        }
    }
    st.pushint(count as i64);
    SCRIPT_CMD_SUCCESS
}

/// opendressroom({<char_id>}).
fn opendressroom(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 2, "buildin_opendressroom") else { return SCRIPT_CMD_FAILURE };
    super::stylist::clif_dressing_room(c, aid);
    SCRIPT_CMD_SUCCESS
}

/// openstylist({<char_id>}).
fn openstylist(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    if !feature_stylist() {
        log::error!("buildin_openstylist: stylist is disabled.");
        return SCRIPT_CMD_FAILURE;
    }
    let Some(aid) = charid2sd(rt, st, 2, "buildin_openstylist") else { return SCRIPT_CMD_FAILURE };
    clif_ui_open(c, aid, OUT_UI_STYLIST, 0);
    SCRIPT_CMD_SUCCESS
}

/// The inventory index of the item worn at equipment slot `pos` (-1: none or a bad slot).
fn equipped_at(c: &Ctx, aid: u32, pos: i32) -> i16 {
    if pos >= 0 && (pos as usize) < EQI_MAX {
        crate::pc_equip::pc_checkequip(c, aid, EQUIP_BITMASK[pos as usize], false)
    } else {
        -1
    }
}

/// getequiprandomoption(<equipment slot>,<index>,<type>{,<char id>}).
fn getequiprandomoption(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let pos = getnum(rt, st, 2);
    let index = getnum(rt, st, 3);
    let kind = getnum(rt, st, 4);
    let Some(aid) = charid2sd(rt, st, 5, "buildin_getequiprandomoption") else {
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    };
    if index < 0 || index as usize >= MAX_ITEM_RDM_OPT {
        log::error!("buildin_getequiprandomoption: Invalid random option index {index}.");
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    }
    let i = equipped_at(c, aid, pos);
    let Some(r) = (if i >= 0 { inv::inv_get(c, aid, i as usize) } else { None }) else {
        let (cid, _) = ids(c, aid);
        log::error!("buildin_getequiprandomoption: No item equipped at pos {pos} (CID={cid}/AID={aid}).");
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    };
    let o = r.item.opt(index as usize);
    let val = match kind {
        ROA_ID => o.id as i32,
        ROA_VALUE => o.value as i32,
        ROA_PARAM => o.param as i8 as i32,
        _ => {
            log::warn!("buildin_getequiprandomoption: Invalid attribute type {kind} (Max {MAX_ITEM_RDM_OPT}).");
            0
        }
    };
    st.pushint(val as i64);
    SCRIPT_CMD_SUCCESS
}

fn ids(c: &Ctx, aid: u32) -> (u32, u32) {
    (crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0), aid)
}

/// setrandomoption(<equipment slot>,<index>,<id>,<value>,<param>{,<char id>}): overwrites one option of a worn item
/// (unequip, change, delete + add packets, equip again).
fn setrandomoption(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 7, "buildin_setrandomoption") else { return SCRIPT_CMD_FAILURE };
    let pos = getnum(rt, st, 2);
    let index = getnum(rt, st, 3);
    let id = getnum(rt, st, 4);
    let value = getnum(rt, st, 5);
    let param = getnum(rt, st, 6);
    if randomopt::random_option(id as u16).is_none() {
        log::error!("buildin_setrandomoption: Random option ID {id} does not exists.");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    }
    if index < 0 || index as usize >= MAX_ITEM_RDM_OPT {
        log::error!("buildin_setrandomoption: Invalid random option index {index}.");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    }
    let i = equipped_at(c, aid, pos);
    if i >= 0 {
        if let Some(r) = inv::inv_get(c, aid, i as usize) {
            let ep = r.item.equip;
            crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_SCRIPT, -1, &r.item);
            crate::pc_equip::pc_unequipitem(c, aid, i as i32, 2);
            if let Some(mut r) = inv::inv_get(c, aid, i as usize) {
                let mut opts = r.item.options();
                opts[index as usize] = ItemOption { id: id as i16, value: value as i16, param: param as i8 as u8 };
                r.item.option = opts.to_vec();
                inv::inv_put(c, &r);
            }
            crate::clif_items::delitem(c, aid, i as i32, 1, 3);
            // (rAthena logs the changed item with -1 again here, not +1)
            if let Some(r) = inv::inv_get(c, aid, i as usize) {
                crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_SCRIPT, -1, &r.item);
            }
            crate::clif_items::additem(c, aid, i as i32, 1, 0);
            crate::pc_equip::pc_equipitem(c, aid, i, ep, false);
            st.pushint(1);
            return SCRIPT_CMD_SUCCESS;
        }
    }
    let (cid, _) = ids(c, aid);
    log::error!("buildin_setrandomoption: No item equipped at pos {pos} (CID={cid}/AID={aid}).");
    st.pushint(0);
    SCRIPT_CMD_FAILURE
}

/// randomoptgroup <random option group ID>: the group applied to an empty item, into .@opt_id[], .@opt_value[],
/// .@opt_param[].
fn randomoptgroup(rt: &Rt, st: &mut State) -> i32 {
    let id = getnum(rt, st, 2);
    // random_option_group.find(id): the int32 argument converts to the uint16 key
    let Some(group) = randomopt::random_option_group(id as u16) else {
        log::error!("buildin_randomoptgroup: Invalid random option group id ({id})!");
        return SCRIPT_CMD_FAILURE;
    };
    let opts = match rt.c {
        Some(c) => group.apply(&mut |a, b| c.rnd_value(a, b)),
        None => return SCRIPT_CMD_SUCCESS,
    };
    for (i, o) in opts.iter().enumerate() {
        set_var(rt, st, None, ".@opt_id", i as u32, o.id as i64);
        set_var(rt, st, None, ".@opt_value", i as u32, o.value as i64);
        set_var(rt, st, None, ".@opt_param", i as u32, o.param as i64);
    }
    SCRIPT_CMD_SUCCESS
}

/// refineui({<char_id>}): opens the refine UI unless it is open.
fn refineui(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 2, "buildin_refineui") else { return SCRIPT_CMD_FAILURE };
    if !feature_refineui() {
        log::error!("buildin_refineui: This command is disabled via configuration.");
        return SCRIPT_CMD_FAILURE;
    }
    if !state(c, aid).refineui_open {
        super::refine::clif_refineui_open(c, aid);
    }
    SCRIPT_CMD_SUCCESS
}

/// identifyall({<type>{,<account_id>}}): identifies (type true, the default) and returns the number of unidentified
/// items; -1 without a player.
fn identifyall(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let identify_item = if st.hasdata(2) { getnum(rt, st, 2) != 0 } else { true };
    let sd = if !st.hasdata(3) {
        rid2sd(rt, st, "buildin_identifyall")
    } else {
        let id = getnum(rt, st, 3) as u32;
        crate::pc::get(c, id).map(|_| id)
    };
    let Some(aid) = sd else {
        st.pushint(-1);
        return SCRIPT_CMD_SUCCESS;
    };
    let n = super::identify::pc_identifyall(c, aid, identify_item);
    st.pushint(n as i64);
    SCRIPT_CMD_SUCCESS
}

/// The item that called the script: (sd->itemid, inventory_data[sd->itemindex]->flag.delay_consume != 0).
fn using_item(c: &Ctx, aid: u32) -> (u32, bool) {
    let Some(s) = inv::st(c, aid) else { return (0, false) };
    let dc = if s.itemindex >= 0 { inv::inv_get(c, aid, s.itemindex as usize).map(|r| inv::data_of(&r.item).flag.delay_consume != 0).unwrap_or(false) } else { false };
    (s.itemid, dc)
}

/// The item argument `i` by name (item_db.searchname) or id (item_db.exists); Err(()) after the error message.
fn item_arg(rt: &Rt, st: &mut State, i: i32, func: &str) -> Result<u32, ()> {
    if isstring(rt, st, i) {
        let name = lat(&getstr(rt, st, i));
        match itemdb::itemdb_searchname(&name) {
            Some(id) => Ok(id.nameid),
            None => {
                log::error!("buildin_{func}: Item \"{name}\" does not exist.");
                Err(())
            }
        }
    } else {
        let id = getnum(rt, st, i) as u32;
        if itemdb::itemdb_exists(id).is_none() {
            log::error!("buildin_{func}: Item ID {id} does not exist.");
            return Err(());
        }
        Ok(id)
    }
}

/// laphine_synthesis {<item id>}: opens the synthesis window of the box (by default the item being used, which must
/// be a delayed-consume item).
fn laphine_synthesis(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = rid2sd(rt, st, "buildin_laphine_synthesis") else { return SCRIPT_CMD_FAILURE };
    let item_id = if st.hasdata(2) {
        match item_arg(rt, st, 2, "laphine_synthesis") {
            Ok(i) => i,
            Err(()) => return SCRIPT_CMD_FAILURE,
        }
    } else {
        let (itemid, dc) = using_item(c, aid);
        if itemid == 0 {
            log::error!("buildin_laphine_synthesis: Called outside of an item script without item id.");
            return SCRIPT_CMD_FAILURE;
        }
        if !dc {
            log::error!("buildin_laphine_synthesis: Called from item {itemid}, which is not a consumed delayed.");
            return SCRIPT_CMD_FAILURE;
        }
        itemid
    };
    if state(c, aid).laphine_synthesis != 0 {
        log::error!("buildin_laphine_synthesis: Laphine Synthesis window was already open. Player AID {aid} with item id {item_id}.");
        return SCRIPT_CMD_FAILURE;
    }
    let Some(synthesis) = laphine_db::laphine_synthesis(item_id) else {
        log::error!("buildin_laphine_synthesis: {item_id} is not a valid Laphine Synthesis item.");
        return SCRIPT_CMD_FAILURE;
    };
    super::laphine::clif_laphine_synthesis_open(c, aid, synthesis);
    SCRIPT_CMD_SUCCESS
}

/// laphine_upgrade: opens the upgrade window of the box being used.
fn laphine_upgrade(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = rid2sd(rt, st, "buildin_laphine_upgrade") else { return SCRIPT_CMD_FAILURE };
    let (itemid, dc) = using_item(c, aid);
    if itemid == 0 {
        log::error!("buildin_laphine_upgrade: Called outside of an item script without item id.");
        return SCRIPT_CMD_FAILURE;
    }
    if !dc {
        log::error!("buildin_laphine_upgrade: Called from item {itemid}, which is not a consumed delayed.");
        return SCRIPT_CMD_FAILURE;
    }
    if state(c, aid).laphine_upgrade != 0 {
        log::error!("buildin_laphine_upgrade: Laphine Upgrade window was already open. Player AID {aid} with item id {itemid}.");
        return SCRIPT_CMD_FAILURE;
    }
    let Some(upgrade) = laphine_db::laphine_upgrade(itemid) else {
        log::error!("buildin_laphine_upgrade: {itemid} is not a valid Laphine Upgrade item.");
        return SCRIPT_CMD_FAILURE;
    };
    super::laphine::clif_laphine_upgrade_open(c, aid, upgrade);
    SCRIPT_CMD_SUCCESS
}

/// enchantgradeui({<char_id>}).
fn enchantgradeui(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 2, "buildin_enchantgradeui") else { return SCRIPT_CMD_FAILURE };
    clif_ui_open(c, aid, OUT_UI_ENCHANTGRADE, 0);
    SCRIPT_CMD_SUCCESS
}

/// item_reform({<item id>{,<char id>}}): opens the reform window of the ticket (by default the item being used).
fn item_reform(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 3, "buildin_item_reform") else { return SCRIPT_CMD_FAILURE };
    let item_id = if st.hasdata(2) {
        match item_arg(rt, st, 2, "item_reform") {
            Ok(i) => i,
            Err(()) => return SCRIPT_CMD_FAILURE,
        }
    } else {
        let (itemid, dc) = using_item(c, aid);
        if itemid == 0 {
            log::error!("buildin_item_reform: Called outside of an item script without item id.");
            return SCRIPT_CMD_FAILURE;
        }
        if !dc {
            log::error!("buildin_item_reform: Called from item {itemid}, which is not a DelayConsume type.");
            return SCRIPT_CMD_FAILURE;
        }
        itemid
    };
    if reform_db::item_reform(item_id).is_none() {
        log::error!("buildin_item_reform: Item ID {item_id} is not in the Item Reform database.");
        return SCRIPT_CMD_FAILURE;
    }
    super::reform::clif_item_reform_open(c, aid, item_id);
    SCRIPT_CMD_SUCCESS
}

/// item_enchant(<client lua index>{,<char id>}): opens the enchant window of this enchant group.
fn item_enchant(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 3, "buildin_item_enchant") else { return SCRIPT_CMD_FAILURE };
    let idx = getnum64(rt, st, 2) as u64;
    if itemdb::enchant::item_enchant(idx).is_none() {
        log::error!("buildin_item_enchant: {idx} is not a valid item enchant index.");
        return SCRIPT_CMD_FAILURE;
    }
    super::enchant::clif_enchantwindow_open(c, aid, idx);
    SCRIPT_CMD_SUCCESS
}

/// searchitem <array variable>,"<item name>": the ids of the items whose name contains it (at most MAX_SEARCH, by
/// ascending id) into the array; returns how many.
fn searchitem(rt: &Rt, st: &mut State) -> i32 {
    let data = st.data(2).clone();
    let (id, start, rf) = match &data {
        Data::Name(id, idx, rf) => (*id, *idx, rf.clone()),
        _ => {
            log::error!("buildin_searchitem: Argument is not a variable.");
            reportdata(&data);
            st.state = END;
            return SCRIPT_CMD_FAILURE; // not a variable
        }
    };
    let name = name_of(id);
    if is_string_variable(&name) {
        log::error!("buildin_searchitem: Argument {} is not an integer array.", lat(&name));
        reportdata(&data);
        st.state = END;
        return SCRIPT_CMD_FAILURE; // not supported
    }
    let mut sd = None;
    if not_server_variable(name.first().copied().unwrap_or(0)) {
        sd = rid2sd(rt, st, "buildin_searchitem");
        if sd.is_none() {
            return SCRIPT_CMD_SUCCESS; // no player attached
        }
    }
    let itemname = lat(&getstr(rt, st, 3));
    let items = itemdb::itemdb_searchname_array(&itemname, MAX_SEARCH);
    let mut idx = start;
    for it in &items {
        reg::set_reg_num(rt, st, sd, id, idx, &name, it.nameid as i64, &rf);
        idx += 1;
    }
    st.pushint(items.len() as i64);
    SCRIPT_CMD_SUCCESS
}

/// setiteminfo(<item id>|"<aegis name>",<type>,<value>): changes an item_data field for the rest of the server's
/// run (itemdb_rt.rs); returns the value, -1 for an unknown item or type.
fn setiteminfo(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let nameid = if isstring(rt, st, 2) {
        let name = lat(&getstr(rt, st, 2));
        itemdb::search_aegisname(&name).map(|d| d.nameid)
    } else {
        itemdb::itemdb_exists(getnum(rt, st, 2) as u32).map(|d| d.nameid)
    };
    let Some(nameid) = nameid else {
        st.pushint(-1);
        return SCRIPT_CMD_SUCCESS;
    };
    let value = getnum(rt, st, 4);
    let field = getnum(rt, st, 3);
    match super::itemdb_rt::setiteminfo(c, nameid, field, value) {
        Ok(pushes) => {
            for v in pushes {
                st.pushint(v);
            }
            SCRIPT_CMD_SUCCESS
        }
        Err(()) => {
            st.pushint(-1);
            SCRIPT_CMD_FAILURE
        }
    }
}

/// setitemscript(<item id>,"{<new item script>}"{,<n>}): replaces the item's script (n 0), equip script (1) or
/// unequip script (2) for the rest of the server's run; 1 when done, 0 otherwise.
fn setitemscript(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let nameid = getnum(rt, st, 2) as u32;
    let script = lat(&getstr(rt, st, 3));
    let n = if st.hasdata(4) { getnum(rt, st, 4) } else { 0 };
    let r = super::itemdb_rt::setitemscript(c, nameid, &script, n);
    st.pushint(r);
    SCRIPT_CMD_SUCCESS
}
