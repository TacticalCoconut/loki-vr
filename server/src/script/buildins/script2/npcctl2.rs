//! NPC control commands: npcshopitem, npcshopattach (with npc_buylist_sub / npc_selllist_sub for script-controlled
//! shops, called from shop.rs), isnpccloaked, addtimercount, ignoretimeout, duplicate / duplicate_dynamic
//! (npc_load.rs's npc_duplicate_npc / npc_duplicate_npc_for_player, wave F npcload).

use crate::npc::{npc, NPCTYPE_CASHSHOP, NPCTYPE_ITEMSHOP, NPCTYPE_MARKETSHOP, NPCTYPE_POINTSHOP, NPCTYPE_SHOP};
use crate::script::buildins::*;
use crate::script::npc_run;
use crate::script::reg;
use crate::script::vm::*;
use crate::world::Ctx;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "npcshopitem" => npcshopitem,
        "npcshopattach" => npcshopattach,
        "isnpccloaked" => isnpccloaked,
        "addtimercount" => addtimercount,
        "ignoretimeout" => ignoretimeout,
        "duplicate" => duplicate,
        "duplicate_dynamic" => duplicate_dynamic,
        _ => return None,
    })
}

fn is_item_shop(subtype: u8) -> bool {
    matches!(subtype, NPCTYPE_SHOP | NPCTYPE_CASHSHOP | NPCTYPE_ITEMSHOP | NPCTYPE_POINTSHOP | NPCTYPE_MARKETSHOP)
}

/// npcshopitem "<name>",<item id>,<price>{,…} (market shops: item id, price, stock): replaces the shop's list.
fn npcshopitem(rt: &Rt, st: &mut State) -> i32 {
    use crate::npc::ShopItem;
    let c = rt.c.unwrap();
    let npcname = lat(&getstr(rt, st, 2));
    let Some(mut nd) = npc_run::npc_name2id(c, &npcname).filter(|nd| is_item_shop(nd.subtype)) else {
        // Not found.
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let mut offs = 2;
    if nd.subtype == NPCTYPE_MARKETSHOP {
        offs = 3;
        // npc_market_delfromsql_(nd->exname, 0, true): DELETE FROM market WHERE name = exname
        crate::shop::npc_market_clearfromsql(c, &nd.exname);
    }
    // get the count of new entries
    let amount = (st.lastdata() - 2) / offs;
    // generate new shop item list
    nd.shop.clear();
    let mut i = 3;
    for _ in 0..amount {
        let nameid = getnum(rt, st, i) as u32;
        let Some(id) = crate::db::items::itemdb_exists(nameid) else {
            log::error!("builtin_npcshopitem: Item ID {nameid} does not exist.");
            c.db().npc().id().update(nd); // (the entries so far stay: u.shop.count)
            st.pushint(0);
            return SCRIPT_CMD_FAILURE;
        };
        let mut price = getnum(rt, st, i + 1);
        if price < 0 {
            if matches!(nd.subtype, NPCTYPE_CASHSHOP | NPCTYPE_POINTSHOP | NPCTYPE_ITEMSHOP) {
                log::error!("builtin_npcshopitem: Invalid price in shop '{}'.", nd.exname);
                c.db().npc().id().update(nd);
                st.pushint(0);
                return SCRIPT_CMD_FAILURE;
            }
            price = id.value_buy as i32;
        }
        let mut entry = ShopItem { nameid, value: price, qty: 0, flag: 0 };
        if nd.subtype == NPCTYPE_MARKETSHOP {
            entry.qty = getnum(rt, st, i + 2);
            entry.flag = 1;
            crate::shop::npc_market_tosql(c, &nd.exname, nameid, price, entry.qty, entry.flag);
        }
        nd.shop.push(entry);
        i += offs;
    }
    c.db().npc().id().update(nd);
    st.pushint(1);
    SCRIPT_CMD_SUCCESS
}

/// npcshopattach "<name>"{,<flag>}: the shop's buy / sell lists go to this NPC's OnBuyItem / OnSellItem
/// (nd->master_nd; flag 0 detaches).
fn npcshopattach(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let npcname = lat(&getstr(rt, st, 2));
    let flag = if st.hasdata(3) { getnum(rt, st, 3) } else { 1 };
    let Some(nd) = npc_run::npc_name2id(c, &npcname).filter(|nd| is_item_shop(nd.subtype)) else {
        // Not found.
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let mut m = crate::pc_misc::npcm(c, nd.id);
    // nd->master_nd = (npc_data *)map_id2bl(st->oid)
    m.shop_master = if flag != 0 { super::unitctl::map_id2bl(c, st.oid).map(|b| b.id()).unwrap_or(0) } else { 0 };
    crate::pc_misc::put_npcm(c, &m);
    st.pushint(1);
    SCRIPT_CMD_SUCCESS
}

/// nd->master_nd of a shop (0: none).
pub fn shop_master(c: &Ctx, shop_id: u32) -> u32 {
    let m = crate::pc_misc::npcm(c, shop_id).shop_master;
    if m != 0 && c.db().npc().id().find(m).is_some() {
        m
    } else {
        0
    }
}

/// A throw-away script state for the player-variable helpers.
fn dummy_state(aid: u32) -> State {
    State::new(CodeKey::Func(String::new()), std::rc::Rc::new(Default::default()), 0, aid, 0)
}

/// script.cpp script_cleararray_pc: every element of the player's array `name` is cleared.
pub fn script_cleararray_pc(c: &Ctx, aid: u32, name: &str) {
    let rt = Rt::new(c);
    let mut st = dummy_state(aid);
    let n = crate::net::wire::latin1(name);
    let id = add_str(&n);
    if let Some(list) = reg::members(&rt, &mut st, Some(aid), &n, &RegRef::None) {
        for i in list {
            reg::clear_reg(&rt, &mut st, Some(aid), id, i, &n, &RegRef::None);
        }
    }
}

/// script.cpp script_setarray_pc (a number).
pub fn script_setarray_pc(c: &Ctx, aid: u32, name: &str, idx: u32, v: i64) {
    let rt = Rt::new(c);
    let mut st = dummy_state(aid);
    let n = crate::net::wire::latin1(name);
    let id = add_str(&n);
    reg::set_reg_num(&rt, &mut st, Some(aid), id, idx, &n, v, &RegRef::None);
}

/// npc.cpp npc_buylist_sub: @bought_nameid / @bought_quantity, then the master's OnBuyItem.
pub fn npc_buylist_sub(c: &Ctx, aid: u32, item_list: &[crate::shop::BuyItem], master: u32) {
    // discard old contents
    script_cleararray_pc(c, aid, "@bought_nameid");
    script_cleararray_pc(c, aid, "@bought_quantity");
    // save list of bought items
    for (i, e) in item_list.iter().enumerate() {
        script_setarray_pc(c, aid, "@bought_nameid", i as u32, e.nameid as i64);
        script_setarray_pc(c, aid, "@bought_quantity", i as u32, e.qty as i64);
    }
    // invoke event
    let Some(nd) = c.db().npc().id().find(master) else { return };
    let ev = format!("{}::OnBuyItem", nd.exname);
    npc_run::npc_event(c, aid, &ev, 0);
}

/// npc.cpp npc_selllist_sub: @sold_nameid / _quantity / _refine / _attribute / _identify / _enchantgrade /
/// _card1-4 / _option_id1-5 / _option_val1-5 / _option_param1-5, then the master's OnSellItem. 0 = sold.
pub fn npc_selllist_sub(c: &Ctx, aid: u32, list: &[(u16, u16)], master: u32) -> u8 {
    const MAX_SLOTS: usize = 4;
    const MAX_ITEM_RDM_OPT: usize = 5;
    for n in ["@sold_nameid", "@sold_quantity", "@sold_refine", "@sold_attribute", "@sold_identify", "@sold_enchantgrade"] {
        script_cleararray_pc(c, aid, n);
    }
    for j in 0..MAX_SLOTS {
        // clear each of the card slot entries
        script_cleararray_pc(c, aid, &format!("@sold_card{}", j + 1));
    }
    for j in 0..MAX_ITEM_RDM_OPT {
        // Clear each of the item option entries
        script_cleararray_pc(c, aid, &format!("@sold_option_id{}", j + 1));
        script_cleararray_pc(c, aid, &format!("@sold_option_val{}", j + 1));
        script_cleararray_pc(c, aid, &format!("@sold_option_param{}", j + 1));
    }
    // save list of to be sold items
    for (i, &(index, amount)) in list.iter().enumerate() {
        let idx = index as i32 - 2;
        let it = crate::inventory::inv_get(c, aid, idx.max(0) as usize).map(|r| r.item).unwrap_or_default();
        let i = i as u32;
        script_setarray_pc(c, aid, "@sold_nameid", i, it.nameid as i64);
        script_setarray_pc(c, aid, "@sold_quantity", i, amount as i64);
        script_setarray_pc(c, aid, "@sold_refine", i, it.refine as i64);
        script_setarray_pc(c, aid, "@sold_attribute", i, it.attribute as i64);
        script_setarray_pc(c, aid, "@sold_identify", i, it.identify as i64);
        script_setarray_pc(c, aid, "@sold_enchantgrade", i, it.enchantgrade as i64);
        for j in 0..MAX_SLOTS {
            // store each of the cards from the equipment in the array
            let card = [it.card0, it.card1, it.card2, it.card3][j];
            script_setarray_pc(c, aid, &format!("@sold_card{}", j + 1), i, card as i64);
        }
        for j in 0..MAX_ITEM_RDM_OPT {
            // Store each of the item options in the array
            let o = it.option.get(j).cloned().unwrap_or_default();
            script_setarray_pc(c, aid, &format!("@sold_option_id{}", j + 1), i, o.id as i64);
            script_setarray_pc(c, aid, &format!("@sold_option_val{}", j + 1), i, o.value as i64);
            script_setarray_pc(c, aid, &format!("@sold_option_param{}", j + 1), i, o.param as i64);
        }
    }
    // invoke event
    if let Some(nd) = c.db().npc().id().find(master) {
        let ev = format!("{}::OnSellItem", nd.exname);
        npc_run::npc_event(c, aid, &ev, 0);
    }
    0
}

/// isnpccloaked({"<NPC name>"{,<char id>}}): npc_is_cloaked for the player.
fn isnpccloaked(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let nd = if st.hasdata(2) { npc_run::npc_name2id(c, &lat(&getstr(rt, st, 2))) } else { c.db().npc().id().find(st.oid) };
    let Some(nd) = nd else {
        if st.hasdata(2) {
            log::error!("buildin_isnpccloaked: {} is a non-existing NPC.", lat(&getstr(rt, st, 2)));
        } else {
            log::error!("buildin_isnpccloaked: non-existing NPC.");
        }
        return SCRIPT_CMD_FAILURE;
    };
    let Some(aid) = charid2sd(rt, st, 3, "buildin_isnpccloaked") else { return SCRIPT_CMD_FAILURE };
    st.pushint(npc_run::npc_is_cloaked(c, nd.id, aid) as i64);
    SCRIPT_CMD_SUCCESS
}

/// script.cpp check_event.
fn check_event(rt: &Rt, st: &State, evt: &[u8]) {
    if !evt.is_empty() && !evt.to_ascii_lowercase().windows(4).any(|w| w == b"::on") {
        log::warn!("NPC event parameter deprecated! Please use 'NPCNAME::OnEVENT' instead of '{}'.", lat(evt));
        reportsrc(rt, st);
    }
}

/// addtimercount <ticks>,"<event>": pc_addeventtimercount — the player's timer for the event runs `ticks` later.
fn addtimercount(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_addtimercount") else { return SCRIPT_CMD_SUCCESS };
    let c = rt.c.unwrap();
    let tick = getnum(rt, st, 2);
    let event = getstr(rt, st, 3);
    check_event(rt, st, &event);
    // pc_addeventtimercount: the first live event timer with that name
    let name = lat(&event);
    let x = npc_run::sdx(c, aid);
    if let Some(i) = (0..x.eventtimer.len()).find(|&i| x.eventtimer[i] != 0 && x.eventtimer_name[i] == name) {
        if let Some(due) = crate::timer::due(c, x.eventtimer[i]) {
            crate::timer::settick(c, x.eventtimer[i], due + tick as i64);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// ignoretimeout <flag>{,<char id>|"<name>"}: sd->state.ignoretimeout (SECURE_NPCTIMEOUT).
fn ignoretimeout(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let aid = if st.hasdata(3) {
        if !isstring(rt, st, 3) {
            crate::pc::charid2sd(c, getnum(rt, st, 3) as u32).map(|u| u.id)
        } else {
            crate::pc::nick2sd(c, &lat(&getstr(rt, st, 3)), false).map(|u| u.id)
        }
    } else {
        rid2sd(rt, st, "buildin_ignoretimeout")
    };
    let Some(aid) = aid else { return SCRIPT_CMD_FAILURE };
    let mut x = npc_run::sdx(c, aid);
    x.ignoretimeout = getnum(rt, st, 2) > 0;
    npc_run::put_sdx(c, &x);
    SCRIPT_CMD_SUCCESS
}

/// duplicate "<NPC name>","<map>",<x>,<y>{,"<Duplicate NPC name>"{,<sprite>{,<dir>{,<xs>,<ys>}}}}:
/// npc_duplicate_npc (npc_load.rs, through npc.rs's run-time npc_parse_duplicate); pushes the copy's unique name, or ""
/// with a failure. Defaults are the source's name, class and direction, and its OnTouch area (nd->u.scr.xs / ys: for a
/// warp source the same fields; a shop source reads other members of the union in rAthena, -1 here).
fn duplicate(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else {
        st.pushstr(Vec::new());
        return SCRIPT_CMD_FAILURE;
    };
    let old_npcname = lat(&getstr(rt, st, 2));
    let Some(nd) = npc_run::npc_name2id(c, &old_npcname) else {
        log::error!("buildin_duplicate: No such NPC '{old_npcname}'.");
        st.pushstr(Vec::new());
        return SCRIPT_CMD_FAILURE;
    };
    let mapname = lat(&getstr(rt, st, 3));
    let Some(mapid) = crate::data::maps().id_of(&mapname) else {
        log::error!("buildin_duplicate: map '{mapname}' in not found!");
        st.pushstr(Vec::new());
        return SCRIPT_CMD_FAILURE;
    };
    let Some(md) = crate::data::maps().get(mapid) else {
        // Should not happen, but who knows...
        log::error!("buildin_duplicate: mapdata for '{mapname}' is unavailable!");
        st.pushstr(Vec::new());
        return SCRIPT_CMD_FAILURE;
    };
    let (mxs, mys) = (md.xs, md.ys);
    let x = getnum(rt, st, 4) as i16;
    if x < 0 || x >= mxs {
        log::error!("buildin_duplicate: x coordinate {x} is out of bounds for map {mapname}[0-{mxs}]!");
        st.pushstr(Vec::new());
        return SCRIPT_CMD_FAILURE;
    }
    let y = getnum(rt, st, 5) as i16;
    if y < 0 || y >= mys {
        log::error!("buildin_duplicate: y coordinate {y} is out of bounds for map {mapname}[0-{mys}]!");
        st.pushstr(Vec::new());
        return SCRIPT_CMD_FAILURE;
    }
    let name = if st.hasdata(6) {
        let new_name = getstr(rt, st, 6);
        if new_name.len() > crate::npc::NPC_NAME_LENGTH {
            log::error!("buildin_duplicate: new NPC name \"{}\" is too long!", lat(&new_name));
            st.pushstr(Vec::new());
            return SCRIPT_CMD_FAILURE;
        }
        lat(&new_name)
    } else {
        nd.name.clone()
    };
    let class = if st.hasdata(7) { getnum(rt, st, 7) } else { nd.class };
    let dir = if st.hasdata(8) { getnum(rt, st, 8) as u8 } else { crate::unit::get(c, nd.id).map(|u| u.ud.dir).unwrap_or(0) };
    let xs = if st.hasdata(9) { getnum(rt, st, 9) as i16 } else { nd.xs };
    let ys = if st.hasdata(10) { getnum(rt, st, 10) as i16 } else { nd.ys };
    match crate::npc_load::npc_duplicate_npc(c, &nd, &name, mapid, x, y, class, dir, xs, ys, 0) {
        Some(dnd) => {
            st.pushstr(crate::net::wire::latin1(&dnd.exname));
            SCRIPT_CMD_SUCCESS
        }
        None => {
            st.pushstr(Vec::new());
            SCRIPT_CMD_FAILURE
        }
    }
}

/// duplicate_dynamic "<NPC name>"{,<char id>}: npc_duplicate_npc_for_player (npc_load.rs) — a copy next to the player
/// that only the player sees; pushes its unique name, or "" (a failure only when the NPC or the player is not found).
fn duplicate_dynamic(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else {
        st.pushstr(Vec::new());
        return SCRIPT_CMD_FAILURE;
    };
    let old_npcname = lat(&getstr(rt, st, 2));
    let Some(nd) = npc_run::npc_name2id(c, &old_npcname) else {
        log::error!("buildin_duplicate_dynamic: No such NPC '{old_npcname}'.");
        st.pushstr(Vec::new());
        return SCRIPT_CMD_FAILURE;
    };
    let Some(aid) = charid2sd(rt, st, 3, "buildin_duplicate_dynamic") else {
        st.pushstr(Vec::new());
        return SCRIPT_CMD_FAILURE;
    };
    match crate::npc_load::npc_duplicate_npc_for_player(c, &nd, aid) {
        Some(dnd) => st.pushstr(crate::net::wire::latin1(&dnd.exname)),
        None => st.pushstr(Vec::new()),
    }
    SCRIPT_CMD_SUCCESS
}
